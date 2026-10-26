using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Services;
using Wheelhouse.Infrastructure.Deployments;
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

        Args(await gateway.ControlAsync("foreverpin-dev", "stop", null, "max", null, CancellationToken.None))
            .Should().Equal("control", "--target", "foreverpin-dev", "--operation", "stop", "--actor", "max");
        Args(await gateway.ControlAsync("foreverpin-prod", "restart", "management", "max", "cache; rm -rf /", CancellationToken.None))
            .Should().Equal("control", "--target", "foreverpin-prod", "--operation", "restart", "--actor", "max",
                "--service", "management", "--reason", "cache; rm -rf /");
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
