using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Infrastructure.Deployments.Parsers;
using Wheelhouse.Infrastructure.Infra;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.Unit.Infra;

/// <summary>
/// Tests for <see cref="InfraGateway"/>: what reaches the runner's infra.py (an argument list, never a secret), which
/// rig variables it may see, how a long job starts as the console's own child, and how a refusal reads.
/// </summary>
public sealed class InfraGatewayTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wheelhouse-infra-").FullName;
    private readonly string _calls;

    public InfraGatewayTests()
    {
        _calls = Path.Combine(_root, "calls.jsonl");
        // A stand-in infra.py beside a stand-in transport: logs its arguments and the variables it saw, answers a job.
        File.WriteAllText(Path.Combine(_root, "transport.py"), "");
        File.WriteAllText(Path.Combine(_root, "infra.py"),
            "import json, os, sys\n" +
            "names = ['WHEELHOUSE_REHEARSAL', 'WHEELHOUSE_TAILSCALE_API', 'WHEELHOUSE_KIT', 'REHEARSAL_STATE']\n" +
            "open(" + JsonSerializer.Serialize(_calls) + ", 'a').write(json.dumps({'argv': sys.argv[1:], " +
            "'env': {name: os.environ.get(name) for name in names}}) + '\\n')\n" +
            "if sys.argv[1] == 'refuse':\n" +
            "    print(json.dumps({'status': 'rejected', 'failure': 'Rejected', 'reason': 'Connect Tailscale first'}), file=sys.stderr); sys.exit(1)\n" +
            "print(json.dumps({'id': '6f1c2a9e-0000-4000-8000-000000000001', 'status': 'queued'}))\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private InfraGateway Gateway(DeploymentSettings? settings = null, Dictionary<string, string?>? configuration = null) =>
        new((settings ?? new DeploymentSettings()) with { TransportPath = Path.Combine(_root, "transport.py"), Root = _root },
            new ConfigurationBuilder().AddInMemoryCollection(configuration ?? []).Build(), new RunnerFailureParser());

    private async Task<List<JsonElement>> Calls(int expected)
    {
        // A started job is a child no call waits for; give it a moment to log.
        for (var attempt = 0; attempt < 100 && (!File.Exists(_calls) || File.ReadLines(_calls).Count() < expected); attempt++)
            await Task.Delay(50);
        return [.. File.ReadLines(_calls).Select(line => JsonDocument.Parse(line).RootElement.Clone())];
    }

    private static string[] Arguments(JsonElement call) =>
        [.. call.GetProperty("argv").EnumerateArray().Select(argument => argument.GetString()!)];

    [Fact]
    public async Task EnrollAsync_ShouldRecordTheJobThenRunItAsTheConsolesOwnChild()
    {
        var request = new ServerEnrollmentRequest("ocharo-products", "203.0.113.20", 22, "root", "ops",
            "ssh-ed25519 AAAA owner laptop", "ocharo-vps", null);

        var result = await Gateway().EnrollAsync(request, "sulton-max", CancellationToken.None);

        result.Should().BeOfType<AppResult<JsonElement>.Success>();
        var calls = await Calls(2);
        Arguments(calls[0]).Should().Equal("enroll", "--server", "ocharo-products", "--address", "203.0.113.20", "--port", "22",
            "--login-user", "root", "--admin", "ops", "--tailnet-hostname", "ocharo-vps", "--actor", "sulton-max",
            "--admin-key", "ssh-ed25519 AAAA owner laptop", "--no-launch", "--root", _root);
        Arguments(calls[1]).Should().Equal("enroll-run", "--server", "ocharo-products", "--root", _root);
    }

    [Fact]
    public async Task PlaceAsync_ShouldRunTheRecordedJobById()
    {
        var request = new ProductPlacementRequest("ocharo-platform", "ocharo-products", true,
            [new ProductPlacementSite("app", "ocharo-desk")]);

        await Gateway().PlaceAsync(request, "sulton-max", CancellationToken.None);

        var calls = await Calls(2);
        Arguments(calls[0]).Should().Equal("place", "--server", "ocharo-products", "--product", "ocharo-platform", "--actor",
            "sulton-max", "--database", "--site", "app=ocharo-desk", "--no-launch", "--root", _root);
        Arguments(calls[1]).Should().Equal("place-run", "--job", "6f1c2a9e-0000-4000-8000-000000000001", "--root", _root);
    }

    [Fact]
    public async Task UpkeepAsync_ShouldRecordTheJobThenRunItById()
    {
        await Gateway().UpkeepAsync("ocharo-vps", "sulton-max", CancellationToken.None);

        var calls = await Calls(2);
        Arguments(calls[0]).Should().Equal("upkeep", "--server", "ocharo-vps", "--actor", "sulton-max", "--no-launch",
            "--root", _root);
        Arguments(calls[1]).Should().Equal("upkeep-run", "--job", "6f1c2a9e-0000-4000-8000-000000000001", "--root", _root);
    }

    [Fact]
    public async Task TheTailscaleApi_ShouldChangeOnlyOnTheRig()
    {
        var configuration = new Dictionary<string, string?> { ["Infra:TailscaleApi"] = "http://fake-tailscale:8080" };
        await Gateway(configuration: configuration).TailnetAsync(CancellationToken.None);
        await Gateway(new DeploymentSettings { Rehearsal = DeploymentSettings.RigNetwork }, configuration)
            .TailnetAsync(CancellationToken.None);

        var calls = await Calls(2);
        calls[0].GetProperty("env").GetProperty("WHEELHOUSE_TAILSCALE_API").ValueKind.Should().Be(JsonValueKind.Null);
        calls[0].GetProperty("env").GetProperty("WHEELHOUSE_REHEARSAL").ValueKind.Should().Be(JsonValueKind.Null);
        calls[1].GetProperty("env").GetProperty("WHEELHOUSE_TAILSCALE_API").GetString().Should().Be("http://fake-tailscale:8080");
        calls[1].GetProperty("env").GetProperty("WHEELHOUSE_REHEARSAL").GetString().Should().Be("network");
    }

    [Fact]
    public async Task AStrayTailscaleVariable_ShouldNeverReachTheRunner()
    {
        Environment.SetEnvironmentVariable("WHEELHOUSE_TAILSCALE_API", "http://elsewhere.example");
        try
        {
            await Gateway(configuration: new() { ["Infra:KitPath"] = "/opt/kit" }).TailnetAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WHEELHOUSE_TAILSCALE_API", null);
        }

        var call = (await Calls(1))[0];
        call.GetProperty("env").GetProperty("WHEELHOUSE_TAILSCALE_API").ValueKind.Should().Be(JsonValueKind.Null);
        call.GetProperty("env").GetProperty("WHEELHOUSE_KIT").GetString().Should().Be("/opt/kit");
    }

    [Fact]
    public async Task ARefusal_ShouldReadAsTheRunnersOwnReason()
    {
        File.WriteAllText(Path.Combine(_root, "infra.py"),
            "import json, sys\nprint(json.dumps({'status': 'rejected', 'failure': 'Rejected', 'reason': " +
            "'Connect Tailscale first'}), file=sys.stderr)\nsys.exit(1)\n");

        var result = await Gateway().TailnetAsync(CancellationToken.None);

        var error = result.Should().BeOfType<AppResult<JsonElement>.Failure>().Subject.Error;
        (error.Message, error.Type).Should().Be(("Connect Tailscale first", AppErrorType.Conflict));
    }

    [Fact]
    public async Task AMissingRunner_ShouldFailWithoutStarting()
    {
        File.Delete(Path.Combine(_root, "infra.py"));

        var result = await Gateway().TailnetAsync(CancellationToken.None);

        result.Should().BeOfType<AppResult<JsonElement>.Failure>().Subject.Error.Message
            .Should().Be("The infrastructure runner is not installed.");
    }
}
