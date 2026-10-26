using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Infrastructure.Deployments;
using Wheelhouse.Infrastructure.GitHub;
using Wheelhouse.Infrastructure.Settings;
using Wheelhouse.Tests.Unit.Fakes;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.Unit.Deployments;

/// <summary>
/// Tests for <see cref="DeploymentGateway"/>'s runner process: only <c>Deployment:Rehearsal</c> exposes the local rig, so an
/// inherited <c>WHEELHOUSE_REHEARSAL</c> cannot point a deployed control plane at it; only the allowlist passes from the
/// API's environment; and the runner's failure line reads as the owner's error.
/// </summary>
public sealed class DeploymentGatewayTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wheelhouse-gateway-").FullName;
    private readonly string _transport;

    public DeploymentGatewayTests()
    {
        // A stand-in transport that reports the rig variables it received.
        _transport = Path.Combine(_root, "transport.py");
        File.WriteAllText(_transport,
            "import json, os\nprint(json.dumps({'rig': os.environ.get('WHEELHOUSE_REHEARSAL'), " +
            "'state': os.environ.get('REHEARSAL_STATE')}))\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<(string? Rig, string? State)> RunWith(DeploymentSettings settings)
    {
        var gateway = new DeploymentGateway(RunnerProcessClients.Create(), settings with { TransportPath = _transport, Root = _root });
        var result = await gateway.ReadAsync("targets", null, CancellationToken.None);
        var data = result.Should().BeOfType<AppResult<JsonElement>.Success>().Subject.Data;
        return (data.GetProperty("rig").GetString(), data.GetProperty("state").GetString());
    }

    [Fact]
    public async Task ReadAsync_ShouldRunTheSitesAction_WhenPublishedSitesAreRead()
    {
        File.WriteAllText(_transport, "import json, sys\nprint(json.dumps({'action': sys.argv[1]}))\n");
        var gateway = new DeploymentGateway(RunnerProcessClients.Create(), new DeploymentSettings() with { TransportPath = _transport, Root = _root });

        var result = await gateway.ReadAsync("sites", null, CancellationToken.None);

        result.Should().BeOfType<AppResult<JsonElement>.Success>().Subject.Data.GetProperty("action").GetString()
            .Should().Be("sites");
    }

    [Fact]
    public async Task BranchesCommitsAndBuilds_ShouldNameTheRepository_OnlyWhenOneIsChosen()
    {
        File.WriteAllText(_transport, "import json, sys\nprint(json.dumps({'argv': sys.argv[1:]}))\n");
        var gateway = new DeploymentGateway(RunnerProcessClients.Create(), new DeploymentSettings() with { TransportPath = _transport, Root = _root });

        static string[] Args(AppResult<JsonElement> result) =>
            [.. result.Should().BeOfType<AppResult<JsonElement>.Success>().Subject.Data.GetProperty("argv").EnumerateArray()
                .Select(item => item.GetString()!).TakeWhile(item => item != "--root")];

        Args(await gateway.BranchesAsync("ocharo", "ocharo-hq/ocharo-studio", CancellationToken.None))
            .Should().Equal("branches", "--product", "ocharo", "--repository", "ocharo-hq/ocharo-studio");
        Args(await gateway.BranchesAsync("ocharo", null, CancellationToken.None)).Should().Equal("branches", "--product", "ocharo");
        Args(await gateway.CommitsAsync("ocharo", "main", "ocharo-hq/ocharo-assets", CancellationToken.None))
            .Should().Equal("commits", "--product", "ocharo", "--branch", "main", "--repository", "ocharo-hq/ocharo-assets");
        Args(await gateway.RequestBuildAsync("ocharo", new string('c', 40), null, CancellationToken.None))
            .Should().Equal("build", "--product", "ocharo", "--commit", new string('c', 40));
    }

    [Fact]
    public async Task ReadAsync_ShouldFailAsTransientlyUnavailable_WhenTheRunnerCannotStart()
    {
        var settings = new DeploymentSettings { Python = Path.Combine(_root, "missing-python") };
        var gateway = new DeploymentGateway(RunnerProcessClients.Create(), settings with { TransportPath = _transport, Root = _root });

        var result = await gateway.ReadAsync("targets", null, CancellationToken.None);

        var error = result.Should().BeOfType<AppResult<JsonElement>.Failure>().Subject.Error;
        (error.Type, error.Message).Should().Be((AppErrorType.ExternalUnavailable, "Deployment runner is unavailable."));
    }

    [Fact]
    public async Task An_inherited_rig_variable_never_reaches_the_runner()
    {
        var inherited = Environment.GetEnvironmentVariable("WHEELHOUSE_REHEARSAL");
        Environment.SetEnvironmentVariable("WHEELHOUSE_REHEARSAL", "1");
        try
        {
            (await RunWith(new DeploymentSettings())).Should().Be(((string?)null, (string?)null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WHEELHOUSE_REHEARSAL", inherited);
        }
    }

    [Fact]
    public async Task Host_and_network_rigs_map_to_the_runner_switch()
    {
        (await RunWith(new DeploymentSettings { Rehearsal = DeploymentSettings.RigHost }))
            .Should().Be(("1", (string?)null));
        (await RunWith(new DeploymentSettings { Rehearsal = DeploymentSettings.RigNetwork, RehearsalState = "/host/state" }))
            .Should().Be(("network", "/host/state"));
    }

    [Fact]
    public async Task GitHub_actions_get_the_per_owner_tokens_and_every_action_the_access_notes_file()
    {
        using var rig = new GitHubAppRig();
        rig.Install("sulton-max", 11);
        var products = new InMemoryProductsRepository();
        products.Rows.Add(new ProductEntity
        {
            Id = Guid.NewGuid(), Slug = "foreverpin", Name = "ForeverPin", Description = "",
            Repository = "sulton-max/10x-venture-forever-pin", DefaultBranch = "main",
        });
        var settings = rig.Settings with { TransportPath = _transport, Root = _root };
        var tokens = new GitHubRunnerTokens(rig.Tokens, products, settings, NullLogger<GitHubRunnerTokens>.Instance);
        var gateway = new DeploymentGateway(RunnerProcessClients.Create(), settings, tokens);
        File.WriteAllText(_transport,
            "import json, os\nprint(json.dumps({'tokens': os.environ.get('WHEELHOUSE_GITHUB_TOKENS_FILE'), " +
            "'access': os.environ.get('WHEELHOUSE_GITHUB_ACCESS_FILE')}))\n");
        var inherited = Environment.GetEnvironmentVariable("WHEELHOUSE_GITHUB_TOKENS_FILE");
        Environment.SetEnvironmentVariable("WHEELHOUSE_GITHUB_TOKENS_FILE", "/elsewhere/tokens.json");
        try
        {
            var releases = Data(await gateway.ReadAsync("releases", null, CancellationToken.None));
            var jobs = Data(await gateway.ReadAsync("jobs", null, CancellationToken.None));

            releases.GetProperty("tokens").GetString().Should().Be(GitHubRunnerFiles.TokensPath(_root));
            File.ReadAllText(GitHubRunnerFiles.TokensPath(_root)).Should().Contain("ghs_sulton-max_1");
            jobs.GetProperty("tokens").ValueKind.Should().Be(JsonValueKind.Null, "only actions that reach GitHub get tokens");
            jobs.GetProperty("access").GetString().Should().Be(GitHubRunnerFiles.AccessPath(_root));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WHEELHOUSE_GITHUB_TOKENS_FILE", inherited);
        }
    }

    [Fact]
    public async Task Only_the_allowlist_passes_from_the_APIs_environment()
    {
        File.WriteAllText(_transport,
            "import json, os\nprint(json.dumps({'env': dict(os.environ)}))\n");
        var inherited = Environment.GetEnvironmentVariable("WHEELHOUSE_PROBE_SECRET");
        Environment.SetEnvironmentVariable("WHEELHOUSE_PROBE_SECRET", "do-not-pass");
        try
        {
            var gateway = new DeploymentGateway(RunnerProcessClients.Create(), new DeploymentSettings { TransportPath = _transport, Root = _root });
            var environment = Data(await gateway.ReadAsync("targets", null, CancellationToken.None)).GetProperty("env")
                .EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString());

            environment.Should().NotContainKey("WHEELHOUSE_PROBE_SECRET");
            environment.Should().ContainKey("PATH").WhoseValue.Should().Be(Environment.GetEnvironmentVariable("PATH"));
            environment.Should().ContainKey("HOME");
            // Python itself may add LC_CTYPE when it coerces a C locale; everything else is the allowlist or the API's own.
            environment.Keys.Except(["LC_CTYPE", "__CF_USER_TEXT_ENCODING"]).Should().OnlyContain(name =>
                RunnerProcess.InheritedVariables.Contains(name) || name.StartsWith("WHEELHOUSE_GITHUB_", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WHEELHOUSE_PROBE_SECRET", inherited);
        }
    }

    [Theory]
    [InlineData("{\"status\": \"rejected\", \"failure\": \"Rejected\", \"reason\": \"Missing required setting: management:Billing:SecretKey\"}",
        AppErrorType.Conflict, "Deployment rejected: Missing required setting: management:Billing:SecretKey")]
    [InlineData("{\"status\": \"rejected\", \"failure\": \"CommandFailed\", \"reason\": \"SSH host key verification failed (exit 255)\"}",
        AppErrorType.ExternalUnavailable, "Deployment step failed: SSH host key verification failed (exit 255)")]
    [InlineData("warning: password=DO_NOT_LOG\n{\"failure\": \"Rejected\", \"reason\": \"Invalid identifier\"}\n",
        AppErrorType.Conflict, "Deployment rejected: Invalid identifier")]
    [InlineData("", AppErrorType.Unexpected, "Deployment operation failed. Inspect the target privately.")]
    [InlineData("password=DO_NOT_LOG", AppErrorType.Unexpected, "Deployment operation failed. Inspect the target privately.")]
    [InlineData("{\"status\": \"rejected\", \"failure\": \"ValueError\"}", AppErrorType.Unexpected, "Deployment operation failed. Inspect the target privately.")]
    [InlineData("{\"failure\": \"Rejected\", \"reason\": 42}", AppErrorType.Unexpected, "Deployment operation failed. Inspect the target privately.")]
    [InlineData("[\"Rejected\"]", AppErrorType.Unexpected, "Deployment operation failed. Inspect the target privately.")]
    public async Task A_failed_run_reads_only_the_runners_safe_reason(string standardError, AppErrorType type, string message)
    {
        var error = await FailWith(standardError);

        (error.Type, error.Message).Should().Be((type, message));
        error.Metadata.Should().BeNull("the process client's failure kind and exit code stay internal");
    }

    [Fact]
    public async Task A_reason_is_stripped_of_control_characters_and_bounded()
    {
        var error = await FailWith("{\"failure\": \"Rejected\", \"reason\": \"a\\u001b[31m" + new string('x', 400) + "\"}");

        error.Message.Should().NotContain("\u001b");
        error.Message.Length.Should().Be("Deployment rejected: ".Length + 300);
    }

    [Fact]
    public async Task Output_that_is_not_JSON_reads_as_an_unavailable_runner()
    {
        File.WriteAllText(_transport, "print('not json')\n");
        var gateway = new DeploymentGateway(RunnerProcessClients.Create(), new DeploymentSettings { TransportPath = _transport, Root = _root });

        var error = (await gateway.ReadAsync("targets", null, CancellationToken.None))
            .Should().BeOfType<AppResult<JsonElement>.Failure>().Subject.Error;

        (error.Type, error.Message).Should().Be((AppErrorType.ExternalUnavailable, "Deployment runner is unavailable."));
    }

    /// <summary>Runs a stand-in transport that writes <paramref name="standardError"/> and exits 1.</summary>
    private async Task<AppError> FailWith(string standardError)
    {
        var errorFile = Path.Combine(_root, "stderr.txt");
        File.WriteAllText(errorFile, standardError);
        File.WriteAllText(_transport,
            "import sys\nsys.stderr.write(open(" + JsonSerializer.Serialize(errorFile) + ").read())\nsys.exit(1)\n");
        var gateway = new DeploymentGateway(RunnerProcessClients.Create(), new DeploymentSettings { TransportPath = _transport, Root = _root });
        return (await gateway.ReadAsync("targets", null, CancellationToken.None))
            .Should().BeOfType<AppResult<JsonElement>.Failure>().Subject.Error;
    }

    private static JsonElement Data(AppResult<JsonElement> result) =>
        result.Should().BeOfType<AppResult<JsonElement>.Success>().Subject.Data;
}
