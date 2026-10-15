using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>Verifies authorization and explicit selection at the deployment boundary.</summary>
[Collection(WheelhouseCollection.Name)]
public sealed class DeploymentsE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Theory]
    [InlineData("/api/deployments")]
    [InlineData("/api/deployments/targets")]
    [InlineData("/api/deployments/releases")]
    [InlineData("/api/deployments/targets/pilot/state")]
    [InlineData("/api/deployments/targets/pilot/topology")]
    [InlineData("/api/deployments/targets/pilot/check")]
    [InlineData("/api/deployments/stats")]
    [InlineData("/api/deployments/vitals")]
    public async Task Inventory_RequiresAdmin(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await AnonymousClient.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AdminClient.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Stats_DefaultToThirtyDaysAndPassAnExplicitWindow()
    {
        Assert.Equal(HttpStatusCode.OK, (await AdminClient.GetAsync("/api/deployments/stats")).StatusCode);
        Assert.Equal(("stats", "30"), Fixture.Deployments.LastRead);
        Assert.Equal(HttpStatusCode.OK, (await AdminClient.GetAsync("/api/deployments/stats?days=7")).StatusCode);
        Assert.Equal(("stats", "7"), Fixture.Deployments.LastRead);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("91")]
    [InlineData("week")]
    public async Task Stats_RejectAWindowOutsideOneToNinetyDays(string days)
    {
        var response = await AdminClient.GetAsync("/api/deployments/stats?days=" + days);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Vitals_ReadEveryTarget()
    {
        Assert.Equal(HttpStatusCode.OK, (await AdminClient.GetAsync("/api/deployments/vitals")).StatusCode);
        Assert.Equal(("vitals", (string?)null), Fixture.Deployments.LastRead);
    }

    [Fact]
    public async Task Topology_ShouldReturn200WithEnvelope_WhenTargetIsSelected()
    {
        var response = await AdminClient.GetAsync("/api/deployments/targets/pilot/topology");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("topology", "pilot"), Fixture.Deployments.LastRead);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var topology = document.GetProperty("data");
        Assert.Equal("pilot", topology.GetProperty("targetId").GetString());
        Assert.Equal("available", topology.GetProperty("availability").GetString());
        Assert.Equal("api", topology.GetProperty("services")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Logs_ReadOneServiceWithABoundedTailAndAreNeverCached()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await AnonymousClient.GetAsync("/api/deployments/targets/pilot/services/api/logs")).StatusCode);
        var response = await AdminClient.GetAsync("/api/deployments/targets/pilot/services/api/logs?tail=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore, "Container output must never be cached.");
        Assert.Equal(("pilot", "api", 50), Fixture.Deployments.LastLogs);
        Assert.Equal(HttpStatusCode.OK, (await AdminClient.GetAsync("/api/deployments/targets/pilot/services/api/logs")).StatusCode);
        Assert.Equal(("pilot", "api", 200), Fixture.Deployments.LastLogs);
    }

    [Theory]
    [InlineData("/api/deployments/targets/pilot/services/api/logs?tail=0")]
    [InlineData("/api/deployments/targets/pilot/services/api/logs?tail=1001")]
    [InlineData("/api/deployments/targets/pilot/services/API/logs")]
    public async Task Logs_RejectAnUnboundedTailOrAnInvalidService(string path) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await AdminClient.GetAsync(path)).StatusCode);

    [Fact]
    public async Task Start_RequiresExplicitActionHeader()
    {
        var response = await AdminClient.PostAsJsonAsync("/api/deployments", new { target = "pilot", release = "v1" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Start_QueuesSelectedRelease()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        var response = await client.PostAsJsonAsync("/api/deployments", new { target = "pilot", release = "v1" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Matches("/api/deployments/[0-9a-f-]{36}$", response.Headers.Location!.ToString());
        Assert.Equal("pilot", Fixture.Deployments.LastTarget);
        Assert.Equal("v1", Fixture.Deployments.LastRelease);
    }

    [Fact]
    public async Task Start_ForwardsTheTypedConfirmation()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        var response = await client.PostAsJsonAsync("/api/deployments", new { target = "foreverpin-prod", release = "v1", confirm = "foreverpin-prod" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("foreverpin-prod", Fixture.Deployments.LastConfirm);
        Assert.False(Fixture.Deployments.LastSkipTestPass);
    }

    [Fact]
    public async Task Start_ForwardsASkippedTestPass()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        var response = await client.PostAsJsonAsync("/api/deployments",
            new { target = "foreverpin-prod", release = "v1", confirm = "foreverpin-prod", skipTestPass = true });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(Fixture.Deployments.LastSkipTestPass);
    }

    [Fact]
    public async Task Build_RequiresExplicitActionHeader()
    {
        var response = await AdminClient.PostAsJsonAsync("/api/deployments/products/foreverpin/builds", new { commit = new string('e', 40) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Build_RequestsTheCommitsBuild()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "build");
        var commit = new string('e', 40);
        var response = await client.PostAsJsonAsync("/api/deployments/products/foreverpin/builds", new { commit });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(("foreverpin", commit), Fixture.Deployments.LastBuild);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("Eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee")]
    [InlineData("eeee;id")]
    public async Task Build_AcceptsOnlyAFullCommitSha(string commit)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "build");
        var response = await client.PostAsJsonAsync("/api/deployments/products/foreverpin/builds", new { commit });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Commits_ListABranchWithEachBuild()
    {
        var response = await AdminClient.GetAsync("/api/deployments/products/foreverpin/commits?branch=feature/pins");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("foreverpin", "feature/pins"), Fixture.Deployments.LastCommits);
        var commits = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("foreverpin-ci-7", commits[0].GetProperty("buildId").GetString());
    }

    [Theory]
    [InlineData("/api/deployments/products/foreverpin/commits")]
    [InlineData("/api/deployments/products/foreverpin/commits?branch=-oops")]
    [InlineData("/api/deployments/products/Foreverpin/branches")]
    public async Task ProductRoutes_RejectBadProductsAndBranches(string path)
    {
        var response = await AdminClient.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Start_RejectsPathsAndShellSyntax()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        var response = await client.PostAsJsonAsync("/api/deployments", new { target = "../outside", release = "v1;id" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Check_ForwardsTheSelectedRelease()
    {
        var response = await AdminClient.GetAsync("/api/deployments/targets/pilot/check?release=foreverpin-gh-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("pilot", "foreverpin-gh-1"), (Fixture.Deployments.LastTarget, Fixture.Deployments.LastRelease));
    }

    [Theory]
    [InlineData("/api/deployments/targets/pilot/check?release=v1;id")]
    [InlineData("/api/deployments/targets/Pilot/state")]
    [InlineData("/api/deployments/targets/Pilot/topology")]
    [InlineData("/api/deployments/targets/pilot;id/topology")]
    [InlineData("/api/deployments/targets/pilot%2F..%2Foutside/topology")]
    [InlineData("/api/deployments/targets/pilot%2F..%2Foutside/state")]
    public async Task TargetRoutes_RejectAnythingButCatalogIds(string path)
    {
        var response = await AdminClient.GetAsync(path);
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, response.StatusCode.ToString());
    }

    [Fact]
    public async Task Reconcile_RequiresExplicitActionHeader()
    {
        var response = await AdminClient.PostAsJsonAsync("/api/deployments/targets/pilot/reconcile", new { job = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reconcile_ForwardsTheJobAndActor()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "reconcile");
        var job = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/deployments/targets/pilot/reconcile", new { job });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((job.ToString(), "test-admin"), (Fixture.Deployments.LastJob, Fixture.Deployments.LastActor));
    }

    [Fact]
    public async Task Reconcile_RequiresAJob()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "reconcile");
        var response = await client.PostAsJsonAsync("/api/deployments/targets/pilot/reconcile", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Start_RejectsAnonymousEvenWithActionHeader()
    {
        var client = AnonymousClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        var response = await client.PostAsJsonAsync("/api/deployments", new { target = "pilot", release = "v1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
