using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Infrastructure.Deployments;
using Wheelhouse.Infrastructure.Deployments.Parsers;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

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
}
