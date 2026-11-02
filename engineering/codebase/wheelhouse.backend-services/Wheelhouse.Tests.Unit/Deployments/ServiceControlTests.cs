using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Application.Deployments.Controls.Commands;
using Wheelhouse.Application.Deployments.Controls.Validators;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Services;
using Wheelhouse.Infrastructure.Deployments;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Infrastructure.Settings;
using Wheelhouse.Tests.Unit.Fakes;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.Unit.Deployments;

/// <summary>
/// Tests for service controls below the API: the runner arguments a control becomes, how a control reads in the status
/// taxonomy, and which control says what stays stopped on a target.
/// </summary>
public sealed class ServiceControlTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wheelhouse-controls-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ControlAsync_ShouldNameTheServiceAndReason_OnlyWhenGiven()
    {
        var transport = Path.Combine(_root, "transport.py");
        File.WriteAllText(transport, "import json, sys\nprint(json.dumps({'argv': sys.argv[1:]}))\n");
        var gateway = new DeploymentGateway(RunnerProcessClients.Create(), new DeploymentSettings() with { TransportPath = transport, Root = _root });

        static string[] Args(AppResult<JsonElement> result) =>
            [.. result.Should().BeOfType<AppResult<JsonElement>.Success>().Subject.Data.GetProperty("argv").EnumerateArray()
                .Select(item => item.GetString()!).TakeWhile(item => item != "--root")];

        Args(await gateway.ControlAsync("foreverpin-dev", "stop", null, null, "max", null, CancellationToken.None))
            .Should().Equal("control", "--target", "foreverpin-dev", "--operation", "stop", "--actor", "max");
        Args(await gateway.ControlAsync("foreverpin-prod", "restart", "management", null, "max", "cache; rm -rf /", CancellationToken.None))
            .Should().Equal("control", "--target", "foreverpin-prod", "--operation", "restart", "--actor", "max",
                "--service", "management", "--reason", "cache; rm -rf /");
        Args(await gateway.ControlAsync("foreverpin-prod", "reset", null, null, "max", "schema drifted", CancellationToken.None))
            .Should().Equal("control", "--target", "foreverpin-prod", "--operation", "reset", "--actor", "max",
                "--reason", "schema drifted");
        Args(await gateway.ControlAsync("ocharo-prod", "reset", null, "identity", "max", "identity drifted", CancellationToken.None))
            .Should().Equal("control", "--target", "ocharo-prod", "--operation", "reset", "--actor", "max",
                "--set", "identity", "--reason", "identity drifted");
    }

    [Fact]
    public async Task ControlAsync_ShouldSendARunsCommandOnStdin_NeverAsAnArgument()
    {
        var transport = Path.Combine(_root, "transport.py");
        File.WriteAllText(transport, "import json, sys\nprint(json.dumps({'argv': sys.argv[1:], 'stdin': sys.stdin.read()}))\n");
        var gateway = new DeploymentGateway(RunnerProcessClients.Create(), new DeploymentSettings() with { TransportPath = transport, Root = _root });
        string[] command = ["dotnet", "Identity.Api.dll", "admin", "create", "--phone", "+998901234567"];

        var data = (await gateway.ControlAsync("ocharo-dev", "run", "identity", null, "max", null, command, CancellationToken.None))
            .Should().BeOfType<AppResult<JsonElement>.Success>().Subject.Data;

        data.GetProperty("argv").EnumerateArray().Select(item => item.GetString()!).TakeWhile(item => item != "--root")
            .Should().Equal("control", "--target", "ocharo-dev", "--operation", "run", "--actor", "max", "--service", "identity",
                "--stdin");
        JsonDocument.Parse(data.GetProperty("stdin").GetString()!).RootElement.GetProperty("command").EnumerateArray()
            .Select(item => item.GetString()).Should().Equal(command);
    }

    [Fact]
    public async Task RevealRunAsync_ShouldNameTheTargetAndJob()
    {
        var transport = Path.Combine(_root, "transport.py");
        File.WriteAllText(transport, "import json, sys\nprint(json.dumps({'argv': sys.argv[1:]}))\n");
        var gateway = new DeploymentGateway(RunnerProcessClients.Create(), new DeploymentSettings() with { TransportPath = transport, Root = _root });

        (await gateway.RevealRunAsync("ocharo-dev", "33333333-3333-4333-8333-333333333333", CancellationToken.None))
            .Should().BeOfType<AppResult<JsonElement>.Success>().Subject.Data.GetProperty("argv").EnumerateArray()
            .Select(item => item.GetString()!).TakeWhile(item => item != "--root")
            .Should().Equal("reveal", "--target", "ocharo-dev", "--job", "33333333-3333-4333-8333-333333333333");
    }

    [Fact]
    public void Run_ShouldNameOneServiceAndACommand_AndAuditOnlyItsLabel()
    {
        var validator = new TargetControlCommandValidator();
        string[] command = ["dotnet", "Identity.Api.dll", "admin", "create", "--slug", "ocharo", "--display-name", "Lola Karimova",
            "--phone=+998901234567"];
        var run = new TargetControlCommand("ocharo-prod", ControlOperation.Run, "identity", "max", "first owner", Command: command);
        validator.Validate(run).IsValid.Should().BeTrue();
        (run.OperationName, run.AuditAction, run.AuditSubject, run.AuditDetail).Should().Be(("run", "service.run",
            "ocharo-prod/identity",
            "command: dotnet Identity.Api.dll admin create --slug … --display-name … --phone=…; reason: first owner"));
        (run with { Reason = null }).AuditDetail.Should().Be("command: dotnet Identity.Api.dll admin create --slug … --display-name … --phone=…");

        validator.Validate(run with { Service = null }).Errors.Should().ContainSingle(error => error.ErrorMessage == "Run a command in one service.");
        foreach (var bad in new IReadOnlyList<string>?[] { null, [], ["a\nb"], [new string('x', 501)], [.. Enumerable.Repeat("x", 41)] })
            validator.Validate(run with { Command = bad }).Errors.Should().ContainSingle(error => error.ErrorMessage.StartsWith("A run takes its command")
                && !error.ErrorMessage.Contains("+998"));
        validator.Validate(new TargetControlCommand("ocharo-dev", ControlOperation.Restart, "identity", "max", Command: command)).Errors
            .Should().ContainSingle(error => error.ErrorMessage == "Only a run takes a command.");
    }

    [Fact]
    public void SetReset_ShouldNameOneSet_OnlyOnAReset_AndAuditAsAMigrationSetReset()
    {
        var validator = new TargetControlCommandValidator();
        var reset = new TargetControlCommand("ocharo-prod", ControlOperation.Reset, null, "max", "identity drifted", Set: "identity");
        validator.Validate(reset).IsValid.Should().BeTrue();
        (reset.OperationName, reset.AuditAction, reset.AuditSubject, reset.AuditDetail)
            .Should().Be(("reset", "migration-set.reset", "ocharo-prod/identity", "reason: identity drifted"));

        validator.Validate(reset with { Set = "identity_v2-a" }).IsValid.Should().BeTrue("set names take '_' as the runner does");
        validator.Validate(reset with { Operation = ControlOperation.Stop }).Errors
            .Should().ContainSingle(error => error.ErrorMessage == "Only a reset names a migration set.");
        foreach (var name in new[] { "Identity", "identity;rm", "1identity", "", new string('a', 65) })
            validator.Validate(reset with { Set = name }).IsValid.Should().BeFalse(name);
        validator.Validate(reset with { Service = "identity" }).Errors
            .Should().Contain(error => error.ErrorMessage == "A reset takes the environment's database, never one service.");
    }

    [Fact]
    public void Reset_ShouldTakeTheWholeEnvironment_AndAuditAsADatabaseReset()
    {
        var validator = new TargetControlCommandValidator();
        var reset = new TargetControlCommand("foreverpin-prod", ControlOperation.Reset, null, "max", " schema drifted ");
        validator.Validate(reset).IsValid.Should().BeTrue();
        (reset.OperationName, reset.AuditAction, reset.AuditSubject, reset.AuditDetail)
            .Should().Be(("reset", "database.reset", "foreverpin-prod", "reason: schema drifted"));

        var named = validator.Validate(reset with { Service = "management" });
        named.IsValid.Should().BeFalse();
        named.Errors.Should().ContainSingle(error => error.ErrorMessage == "A reset takes the environment's database, never one service.");
    }

    [Fact]
    public void StopsOf_ShouldLetAResetClearAnOlderStop()
    {
        var stopped = new TargetStopDto(true, [], null, "max");
        DeploymentEntry Entry(DeploymentRecord record) => new(record, "foreverpin", "own-ventures", "dev", null, null);

        DeploymentHistoryService.StopsOf([
            Entry(Control("reset", "succeeded", null) with { Operation = new DeploymentOperationDto("reset", []) }),
            Entry(Control("stop", "succeeded", stopped)),
        ]).Should().BeEmpty("the reset leaves every service for the next deploy to start, with nothing marked stopped");
    }

    [Theory]
    [InlineData("queued", DeploymentState.InProgress, DeploymentAttention.Info)]
    [InlineData("running", DeploymentState.InProgress, DeploymentAttention.Info)]
    [InlineData("succeeded", DeploymentState.Succeeded, DeploymentAttention.None)]
    [InlineData("failed", DeploymentState.Failed, DeploymentAttention.Act)]
    [InlineData("rejected", DeploymentState.NotApplied, DeploymentAttention.Look)]
    public void Of_ShouldReadAControlByItsStatusAlone(string status, string state, string attention)
    {
        var reading = DeploymentStateRules.Of(Control("c1", status, null), null, null);
        (reading.State, reading.Attention).Should().Be((state, attention));
        reading.Services.Should().BeNull("a control changes no release");
    }

    [Fact]
    public void Of_ShouldReadAFailedRunAsNotApplied_SinceNothingTheEnvironmentRunsChanged()
    {
        var run = Control("r1", "failed", null) with
        {
            Operation = new DeploymentOperationDto("run", ["identity"], Command: "dotnet Identity.Api.dll admin create --slug …"),
            Reason = "Exit 1 in 2s: admin-result {\"status\":\"refused\"}",
        };
        var reading = DeploymentStateRules.Of(run, null, null);
        (reading.State, reading.Substate, reading.Attention).Should().Be((DeploymentState.NotApplied, "command_failed", DeploymentAttention.Look));
        reading.Cause!.Message.Should().Be("Exit 1 in 2s: admin-result {\"status\":\"refused\"}");
        DeploymentStateRules.Of(run with { Status = "succeeded" }, null, null).State.Should().Be(DeploymentState.Succeeded);
        JsonSerializer.Deserialize<DeploymentOperationDto>("""{"kind":"run","services":["identity"],"command":"admin create --slug …"}""",
            JsonSerializerOptions.Web)!.Command.Should().Be("admin create --slug …", "the history keeps the runner's label");
    }

    [Fact]
    public void StopsOf_ShouldTakeEachTargetsNewestControlThatRanToAnEnd()
    {
        var stopped = new TargetStopDto(true, [], null, "max");
        DeploymentEntry Entry(DeploymentRecord record) => new(record, "foreverpin", "own-ventures", "dev", null, null);

        var stops = DeploymentHistoryService.StopsOf([
            Entry(Control("refused", "rejected", null)),
            Entry(Control("stop", "succeeded", stopped)),
            Entry(Control("old-start", "succeeded", null)),
        ]);
        stops.Should().ContainKey("foreverpin-dev").WhoseValue.Should().Be(stopped);

        DeploymentHistoryService.StopsOf([Entry(Control("start", "succeeded", null)), Entry(Control("stop", "succeeded", stopped))])
            .Should().BeEmpty("the newer start left nothing stopped");
    }

    private static DeploymentRecord Control(string id, string status, TargetStopDto? stopped) => new()
    {
        Id = id, TargetId = "foreverpin-dev", Status = status, SubmittedAt = DateTimeOffset.UnixEpoch,
        CompletedAt = status is "queued" or "running" ? null : DateTimeOffset.UnixEpoch, Release = "v1.0.0",
        Operation = new DeploymentOperationDto("stop", []), Stopped = stopped,
    };
}
