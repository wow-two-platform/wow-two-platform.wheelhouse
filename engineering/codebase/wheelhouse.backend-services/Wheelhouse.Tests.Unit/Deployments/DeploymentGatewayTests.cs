using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Infrastructure.Deployments;
using Wheelhouse.Infrastructure.Deployments.Parsers;
using Wheelhouse.Infrastructure.GitHub;
using Wheelhouse.Infrastructure.Settings;
using Wheelhouse.Tests.Unit.Fakes;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.Unit.Deployments;

/// <summary>
/// Tests for <see cref="DeploymentGateway"/>'s runner environment: only <c>Deployment:Rehearsal</c> exposes the local
/// rig, so an inherited <c>WHEELHOUSE_REHEARSAL</c> cannot point a deployed control plane at it.
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
        var gateway = new DeploymentGateway(settings with { TransportPath = _transport, Root = _root }, new RunnerFailureParser());
        var result = await gateway.ReadAsync("targets", null, CancellationToken.None);
        var data = result.Should().BeOfType<AppResult<JsonElement>.Success>().Subject.Data;
        return (data.GetProperty("rig").GetString(), data.GetProperty("state").GetString());
    }

    [Fact]
    public async Task ReadAsync_ShouldRunTheSitesAction_WhenPublishedSitesAreRead()
    {
        File.WriteAllText(_transport, "import json, sys\nprint(json.dumps({'action': sys.argv[1]}))\n");
        var gateway = new DeploymentGateway(new DeploymentSettings() with { TransportPath = _transport, Root = _root }, new RunnerFailureParser());

        var result = await gateway.ReadAsync("sites", null, CancellationToken.None);

        result.Should().BeOfType<AppResult<JsonElement>.Success>().Subject.Data.GetProperty("action").GetString()
            .Should().Be("sites");
    }

    [Fact]
    public async Task BranchesCommitsAndBuilds_ShouldNameTheRepository_OnlyWhenOneIsChosen()
    {
        File.WriteAllText(_transport, "import json, sys\nprint(json.dumps({'argv': sys.argv[1:]}))\n");
        var gateway = new DeploymentGateway(new DeploymentSettings() with { TransportPath = _transport, Root = _root }, new RunnerFailureParser());

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
        var gateway = new DeploymentGateway(settings with { TransportPath = _transport, Root = _root }, new RunnerFailureParser());

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
        var gateway = new DeploymentGateway(settings, new RunnerFailureParser(), tokens);
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

    private static JsonElement Data(AppResult<JsonElement> result) =>
        result.Should().BeOfType<AppResult<JsonElement>.Success>().Subject.Data;
}
