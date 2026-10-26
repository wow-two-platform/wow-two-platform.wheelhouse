using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Audit;
using Wheelhouse.Domain.Audit.Enums;
using Wheelhouse.Infrastructure.Deployments;
using Wheelhouse.Infrastructure.Settings;
using Wheelhouse.Tests.E2E.Harness;
using WoW.Two.Sdk.Beta.Be.Integrations.Processes;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// Drives prod's dev pass and the retired <c>test</c> environment's records through the API and the real runner,
/// over the inventory the host exports from its database. A submission that passes every gate stops where it first
/// needs SSH, which no test server has an identity for; reaching that step proves the gates let it through.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class DevPassGateE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture), IDisposable
{
    private const string Release = "v1.0.0";
    private const string Bundle = "foreverpin-v1-0-0";
    private const string DevFirst = "Deploy this release to dev first, or type the target ID to skip the dev pass";
    private const string PastTheGates = "Missing SSH identity file";

    private readonly string _root = Directory.CreateTempSubdirectory("wheelhouse-gate-").FullName;

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Prod_ShouldRefuseARelease_ThatNeverSucceededOnDev()
    {
        await UseRunnerAsync();

        var response = await Deploy(new { target = "foreverpin-prod", release = Bundle });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(DevFirst, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Prod_ShouldTakeTheRelease_AfterItSucceededOnDev()
    {
        await UseRunnerAsync();
        Job("foreverpin-dev", Release, "succeeded");

        var response = await Deploy(new { target = "foreverpin-prod", release = Bundle });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("dev first", body);
        Assert.Contains(PastTheGates, body);
    }

    [Fact]
    public async Task Prod_ShouldSkipTheDevPass_OnlyWithTheTypedTargetId()
    {
        await UseRunnerAsync();

        var untyped = await Deploy(new { target = "foreverpin-prod", release = Bundle, skipDevPass = true, reason = "dev is down" });
        Assert.Equal(HttpStatusCode.Conflict, untyped.StatusCode);
        Assert.Contains("Type the target ID to deploy without a dev pass", await untyped.Content.ReadAsStringAsync());

        var typed = await Deploy(new { target = "foreverpin-prod", release = Bundle, confirm = "foreverpin-prod", skipDevPass = true, reason = "dev is down" });
        Assert.Equal(HttpStatusCode.Conflict, typed.StatusCode);
        Assert.Contains(PastTheGates, await typed.Content.ReadAsStringAsync());

        var entry = (await AdminClient.GetFromJsonAsync<JsonElement>("/api/audit")).GetProperty("data")[0];
        Assert.Equal(Bundle + ", without a dev pass, reason: dev is down", entry.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task RetiredTestRecords_ShouldStillRead_LabelledRetired()
    {
        await UseRunnerAsync();
        // A rollout to the retired test target, recorded before jobs kept their environment; its target journal names it.
        Job("foreverpin-test", "v0.9.0", "succeeded", project: "foreverpin-test");
        Job("foreverpin-dev", Release, "succeeded");

        var history = await AdminClient.GetFromJsonAsync<JsonElement>("/api/deployments");
        var environments = history.GetProperty("data").EnumerateArray()
            .ToDictionary(item => item.GetProperty("targetId").GetString()!, item => item.GetProperty("environment").GetString());
        Assert.Equal("test (retired)", environments["foreverpin-test"]);
        Assert.Equal("dev", environments["foreverpin-dev"]);

        var stats = await AdminClient.GetFromJsonAsync<JsonElement>("/api/deployments/stats?days=7");
        Assert.Contains("foreverpin-test", stats.GetProperty("data").GetProperty("targets").EnumerateArray()
            .Select(item => item.GetProperty("targetId").GetString()));

        // A success on the retired target is no dev pass.
        Assert.Contains(DevFirst, await (await Deploy(new { target = "foreverpin-prod", release = "foreverpin-v0-9-0" }))
            .Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RetiredTestAuditEntries_ShouldStillRead_LabelledRetired_AndKeepTheChainIntact()
    {
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
        {
            var trail = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
            await trail.AppendAsync(new AuditRecord("test-admin", "target.create", "foreverpin-test", AuditOutcome.Succeeded,
                "foreverpin test on pilot-host", null));
            await trail.AppendAsync(new AuditRecord("test-admin", "deployment.start", "foreverpin-test", AuditOutcome.Succeeded,
                "v0.9.0", null));
        }

        var entries = (await AdminClient.GetFromJsonAsync<JsonElement>("/api/audit")).GetProperty("data").EnumerateArray()
            .Select(entry => (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString(),
                entry.GetProperty("detail").GetString()))
            .ToArray();
        Assert.Equal([("deployment.start", "foreverpin-test", "v0.9.0"),
            ("target.create", "foreverpin-test", "foreverpin test (retired) on pilot-host")], entries);

        var verification = (await AdminClient.GetFromJsonAsync<JsonElement>("/api/audit/verification")).GetProperty("data");
        Assert.Equal((true, 2), (verification.GetProperty("intact").GetBoolean(), verification.GetProperty("entries").GetInt32()));
    }

    /// <summary>Routes the host's runner calls to the real runner over this test's root: the inventory the host exports
    /// from its database, plus two locally imported ForeverPin releases. The local-rig switch is what lets the runner
    /// take an imported bundle instead of downloading a published one.</summary>
    private async Task UseRunnerAsync()
    {
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IInventorySnapshot>().ExportAsync(CancellationToken.None);
        File.Copy(Path.Combine(Fixture.InventoryRoot, "inventory.json"), Path.Combine(_root, "inventory.json"));
        ImportRelease(Bundle, Release);
        ImportRelease("foreverpin-v0-9-0", "v0.9.0");

        var settings = new DeploymentSettings { TransportPath = TransportPath(), Root = _root, Rehearsal = DeploymentSettings.RigHost };
        // The host's own runner process client: its time limit, environment allowlist and failure parser.
        var runner = Fixture.Host.Services.GetRequiredKeyedService<IProcessClient>(RunnerProcess.ClientName);
        Fixture.Deployments.Runner = new DeploymentGateway(runner, settings);
    }

    private void ImportRelease(string bundle, string release)
    {
        const string image = "ghcr.io/sulton-max/10x-venture-forever-pin/management@sha256:";
        var folder = Directory.CreateDirectory(Path.Combine(_root, "bundles", bundle)).FullName;
        var compose = JsonSerializer.SerializeToUtf8Bytes(new
        {
            services = new
            {
                management = new
                {
                    image = image + new string('a', 64), platform = "linux/amd64",
                    healthcheck = new { test = new[] { "CMD", "true" } },
                },
            },
        });
        File.WriteAllBytes(Path.Combine(folder, "compose.json"), compose);
        File.WriteAllText(Path.Combine(folder, "release.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, product = "foreverpin", release, kind = "release", sourceCommit = new string('a', 40),
            platform = "linux/amd64", rollbackCompatible = false,
            composeSha256 = Convert.ToHexStringLower(SHA256.HashData(compose)),
            images = new { management = image + new string('a', 64) },
        }));
    }

    /// <summary>Writes a submission record the way the runner keeps them, with the outcome its target journal reported
    /// and, when given, the journal's project (<c>{product}-{environment}</c>).</summary>
    private void Job(string target, string release, string status, string? project = null)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.Combine(_root, "jobs"));
        Directory.CreateDirectory(Path.Combine(_root, "observed"));
        File.WriteAllText(Path.Combine(_root, "jobs", id + ".json"), JsonSerializer.Serialize(new
        {
            id, targetId = target, release, actor = "test-admin", status = "submitting", submittedAt = now.AddMinutes(-5).ToString("O"),
        }));
        File.WriteAllText(Path.Combine(_root, "observed", id + ".json"), JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["status"] = status, ["completedAt"] = now.ToString("O"), ["project"] = project,
        }.Where(pair => pair.Value is not null).ToDictionary()));
    }

    private Task<HttpResponseMessage> Deploy(object request)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        return client.PostAsJsonAsync("/api/deployments", request);
    }

    /// <summary>The runner's transport in this checkout, found above the test assembly.</summary>
    private static string TransportPath()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "wheelhouse.runner-services", "transport.py");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("The runner's transport.py was not found above " + AppContext.BaseDirectory);
    }
}
