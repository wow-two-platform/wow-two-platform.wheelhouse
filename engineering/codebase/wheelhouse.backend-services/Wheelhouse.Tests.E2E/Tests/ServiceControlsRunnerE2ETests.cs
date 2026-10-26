using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Deployments;
using Wheelhouse.Infrastructure.Settings;
using Wheelhouse.Tests.E2E.Harness;
using WoW.Two.Sdk.Beta.Be.Integrations.Processes;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// Drives service controls through the API and the real runner, over the inventory the host exports from its database:
/// ForeverPin's dev target names a host settings file for its service, as the canary's does on wheelhouse-dev. A control
/// that passes every rule stops where it first needs SSH, which no test server has an identity for; and what a finished
/// control left stopped holds every deploy back before anything is downloaded.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class ServiceControlsRunnerE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture), IDisposable
{
    private const string PastTheRules = "Missing SSH identity file";
    private readonly string _root = Directory.CreateTempSubdirectory("wheelhouse-controls-").FullName;

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Control_ShouldResolveATargetWithHostSettingsFiles_AndGoAsFarAsTheTarget()
    {
        await UseRunnerAsync();
        var inventory = await File.ReadAllTextAsync(Path.Combine(_root, "inventory.json"));
        inventory.Should().Contain("/srv/settings/foreverpin-dev/management.json", "the target names its host settings file");

        foreach (var body in new object[] { new { operation = "restart", service = "management" }, new { operation = "stop" } })
        {
            var response = await ControlAsync("foreverpin-dev", body);
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain(PastTheRules);
        }

        Directory.Exists(Path.Combine(_root, "jobs")).Should().BeFalse("a control the transport could not submit leaves no record");
    }

    [Fact]
    public async Task Deploy_ShouldWait_WhileAFinishedControlKeepsAServiceStopped()
    {
        await UseRunnerAsync();
        Control("foreverpin-dev", "stop", new { environment = false, services = new[] { "management" } });

        var refused = await DeployAsync(new { target = "foreverpin-dev", release = "foreverpin-v1-0-0" });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("management is stopped on foreverpin-dev; start it before deploying");

        var history = (await DataAsync("/api/deployments/history?notApplied=true")).GetProperty("items").EnumerateArray().ToList();
        history.Should().Contain(item => item.GetProperty("state").GetString() == "not_applied"
            && item.GetProperty("reason").GetString()!.Contains("start it before deploying"));
        var dev = (await DataAsync("/api/portfolio/summary?weeks=1")).GetProperty("products").EnumerateArray()
            .Single(product => product.GetProperty("product").GetString() == "foreverpin").GetProperty("environments")
            .EnumerateArray().Single(row => row.GetProperty("environment").GetString() == "dev");
        dev.GetProperty("stopped").GetProperty("services")[0].GetString().Should().Be("management");

        // A later start that left nothing stopped lets deploys through again: the deploy reaches the release download.
        Control("foreverpin-dev", "start", null, minutesAgo: 1);
        var next = await DeployAsync(new { target = "foreverpin-dev", release = "foreverpin-v1-0-0" });
        (await next.Content.ReadAsStringAsync()).Should().NotContain("start it before deploying");
    }

    /// <summary>Routes the host's runner calls to the real runner over this test's root and the inventory the host
    /// exports from its database.</summary>
    private async Task UseRunnerAsync()
    {
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IInventorySnapshot>().ExportAsync(CancellationToken.None);
        File.Copy(Path.Combine(Fixture.InventoryRoot, "inventory.json"), Path.Combine(_root, "inventory.json"));
        var settings = new DeploymentSettings { TransportPath = TransportPath(), Root = _root, Rehearsal = DeploymentSettings.RigHost };
        var runner = Fixture.Host.Services.GetRequiredKeyedService<IProcessClient>(RunnerProcess.ClientName);
        Fixture.Deployments.Runner = new DeploymentGateway(runner, settings);
    }

    /// <summary>Writes a finished control the way the transport keeps it, with what its target reported left stopped.</summary>
    private void Control(string target, string kind, object? stopped, int minutesAgo = 5)
    {
        var id = Guid.NewGuid().ToString();
        var at = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo).ToString("O");
        Directory.CreateDirectory(Path.Combine(_root, "jobs"));
        Directory.CreateDirectory(Path.Combine(_root, "observed"));
        File.WriteAllText(Path.Combine(_root, "jobs", id + ".json"), JsonSerializer.Serialize(new
        {
            id, targetId = target, environment = "dev", operation = new { kind, services = Array.Empty<string>() },
            actor = "test-admin", status = "queued", submittedAt = at, remoteJobId = id, release = "v1.0.0",
        }));
        File.WriteAllText(Path.Combine(_root, "observed", id + ".json"), JsonSerializer.Serialize(new
        {
            id, status = "succeeded", completedAt = at, stopped,
        }));
    }

    private Task<HttpResponseMessage> ControlAsync(string target, object body)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "control");
        return client.PostAsJsonAsync("/api/deployments/targets/" + target + "/controls", body);
    }

    private Task<HttpResponseMessage> DeployAsync(object body)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        return client.PostAsJsonAsync("/api/deployments", body);
    }

    private async Task<JsonElement> DataAsync(string path)
    {
        var response = await AdminClient.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
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
