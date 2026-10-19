using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Deployments.Automation.Services;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for deploy freezes: freezing everything, a product or a product's environment (audited, one freeze per scope),
/// lifting one, and what a freeze does — an owner's deploy is refused unless it overrides the freeze with a reason
/// (kept on the job and in the audit trail), and an automatic deploy is skipped.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class DeployFreezesE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Fact]
    public async Task Freezes_RequireTheOwner()
    {
        (await AnonymousClient.GetAsync("/api/deployments/freezes")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AnonymousClient.PostAsJsonAsync("/api/deployments/freezes", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AdminClient.PostAsJsonAsync("/api/deployments/freezes", new { }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a freeze carries its action header");
    }

    [Fact]
    public async Task Freeze_ShouldHoldEachScopeOnce_ListThem_AndBeAudited()
    {
        var portfolio = await FreezeAsync(new { reason = "launch day" });
        portfolio.GetProperty("scope").GetString().Should().Be("portfolio");
        portfolio.GetProperty("product").ValueKind.Should().Be(JsonValueKind.Null);
        portfolio.GetProperty("actor").GetString().Should().Be("test-admin");
        (await FreezeAsync(new { reason = "again" })).GetProperty("id").GetString()
            .Should().Be(portfolio.GetProperty("id").GetString(), "a scope is frozen once");
        (await FreezeAsync(new { product = "foreverpin" })).GetProperty("scope").GetString().Should().Be("product");
        var prod = await FreezeAsync(new { product = "foreverpin", environment = "prod", reason = "debugging billing" });
        prod.GetProperty("scope").GetString().Should().Be("environment");
        prod.GetProperty("covers").GetString().Should().Be("foreverpin prod");

        var listed = (await DataAsync("/api/deployments/freezes")).EnumerateArray()
            .Select(item => item.GetProperty("covers").GetString()).ToList();
        listed.Should().Equal("every product", "foreverpin", "foreverpin prod");

        var audit = (await DataAsync("/api/audit")).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "deployment.freeze")
            .Select(entry => (entry.GetProperty("subject").GetString(), entry.GetProperty("detail").GetString())).ToList();
        audit.Should().Contain(("portfolio", "launch day")).And.Contain(("foreverpin/prod", "debugging billing"));
    }

    [Theory]
    [InlineData("{\"product\":\"nothing-here\"}", HttpStatusCode.NotFound)]
    [InlineData("{\"environment\":\"prod\"}", HttpStatusCode.BadRequest)]
    [InlineData("{\"product\":\"foreverpin\",\"environment\":\"staging\"}", HttpStatusCode.BadRequest)]
    [InlineData("{\"product\":\"foreverpin\",\"reason\":\"two\\nlines\"}", HttpStatusCode.BadRequest)]
    public async Task Freeze_ShouldRefuseAScopeItCannotHold(string body, HttpStatusCode expected)
    {
        var response = await Owner("freeze").PostAsync("/api/deployments/freezes",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unfreeze_ShouldLiftOneScope_AndBeAudited()
    {
        await FreezeAsync(new { product = "foreverpin" });
        await FreezeAsync(new { product = "foreverpin", environment = "dev" });

        (await Owner("freeze").DeleteAsync("/api/deployments/freezes?product=foreverpin&environment=dev"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Owner("freeze").DeleteAsync("/api/deployments/freezes"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "lifting an unfrozen scope changes nothing");
        (await DataAsync("/api/deployments/freezes")).EnumerateArray()
            .Select(item => item.GetProperty("covers").GetString()).Should().Equal("foreverpin");
        (await DataAsync("/api/audit")).EnumerateArray()
            .Count(entry => entry.GetProperty("action").GetString() == "deployment.unfreeze").Should().Be(2);
    }

    [Fact]
    public async Task Deploy_ShouldBeRefusedOnAFrozenTarget_UntilItOverridesTheFreezeWithAReason()
    {
        await FreezeAsync(new { product = "foreverpin", environment = "prod" });

        var refused = await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-prod", release = "foreverpin-gh-1" });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("frozen (foreverpin prod)");
        (await Owner("deploy").PostAsJsonAsync("/api/deployments",
                new { target = "foreverpin-prod", release = "foreverpin-gh-1", overrideFreeze = true }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "an override needs a reason");
        Fixture.Deployments.Starts.Should().BeEmpty();

        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted, "the freeze holds prod only");
        var overridden = await Owner("deploy").PostAsJsonAsync("/api/deployments",
            new { target = "foreverpin-prod", release = "foreverpin-gh-1", overrideFreeze = true, reason = "security fix" });
        overridden.StatusCode.Should().Be(HttpStatusCode.Accepted, await overridden.Content.ReadAsStringAsync());
        Fixture.Deployments.LastReason.Should().Be("security fix");

        var details = (await DataAsync("/api/audit")).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "deployment.start")
            .Select(entry => entry.GetProperty("detail").GetString()).ToList();
        details.Should().Contain("foreverpin-gh-1, overriding a freeze, reason: security fix");

        // The refusal changed nothing: hidden from the list by default, listed when asked for, with the freeze as its cause.
        (await DataAsync("/api/deployments/history")).GetProperty("items").GetArrayLength().Should().Be(0);
        var refusal = (await DataAsync("/api/deployments/history?notApplied=true")).GetProperty("items").EnumerateArray()
            .Should().ContainSingle().Subject;
        (refusal.GetProperty("state").GetString(), refusal.GetProperty("substate").GetString(), refusal.GetProperty("attention").GetString(),
                refusal.GetProperty("product").GetString(), refusal.GetProperty("environment").GetString(), refusal.GetProperty("actor").GetString(),
                refusal.GetProperty("bundleId").GetString())
            .Should().Be(("not_applied", "refused", "none", "foreverpin", "prod", "test-admin", "foreverpin-gh-1"));
        (refusal.GetProperty("cause").GetProperty("kind").GetString(), refusal.GetProperty("cause").GetProperty("message").GetString())
            .Should().Be(("freeze", "Frozen: foreverpin prod."));
        (refusal.GetProperty("status").GetString(), refusal.GetProperty("outcome").GetString()).Should().Be(("rejected", "refused"));
        var detail = await DataAsync("/api/deployments/" + refusal.GetProperty("id").GetString() + "/detail");
        detail.GetProperty("observed").GetBoolean().Should().BeFalse("a deploy that never reached its target has no report");
    }

    [Fact]
    public async Task Deploy_ShouldKeepARunnerGateRefusal_AsADeployThatChangedNothing()
    {
        Fixture.Deployments.StartRefusal = "Deployment rejected: Deploy this release to dev first, or type the target ID to skip the dev pass";
        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-prod", release = "foreverpin-gh-2" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        Fixture.Deployments.StartRefusal = null;
        var refusal = (await DataAsync("/api/deployments/history?status=not-applied")).GetProperty("items")[0];
        (refusal.GetProperty("substate").GetString(), refusal.GetProperty("cause").GetProperty("kind").GetString())
            .Should().Be(("refused", "gate"));
        // The day around the refusal, on the host's clock.
        var at = refusal.GetProperty("submittedAt").GetDateTimeOffset().ToUniversalTime();
        var range = "from=" + Uri.EscapeDataString(at.AddDays(-1).ToString("O")) + "&to=" + Uri.EscapeDataString(at.AddDays(1).ToString("O"));
        var stats = (await DataAsync("/api/deployments/history/stats?notApplied=true&" + range)).GetProperty("current");
        (stats.GetProperty("deploys").GetInt32(), stats.GetProperty("notApplied").GetInt32()).Should().Be((1, 1));
        (await DataAsync("/api/deployments/history/stats?" + range)).GetProperty("current").GetProperty("deploys").GetInt32()
            .Should().Be(0, "stats leave out what changed nothing unless asked for it");
    }

    [Fact]
    public async Task Deploy_ShouldKeepAReasonOnTheJob_AndListIt()
    {
        var response = await Owner("deploy").PostAsJsonAsync("/api/deployments",
            new { target = "foreverpin-dev", release = "foreverpin-gh-1", reason = "trying the new login" });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        Fixture.Deployments.LastReason.Should().Be("trying the new login");
        (await Owner("deploy").PostAsJsonAsync("/api/deployments",
                new { target = "foreverpin-dev", release = "foreverpin-gh-1", reason = new string('x', 301) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        Fixture.Deployments.History.Add(new
        {
            id = "00000000-0000-4000-8000-000000000001", targetId = "foreverpin-dev", bundleId = "foreverpin-gh-1",
            release = "v1.0.0", actor = "test-admin", deployReason = "trying the new login", status = "succeeded",
            submittedAt = "2026-10-02T10:00:00+00:00", completedAt = "2026-10-02T10:02:00+00:00",
        });
        var item = (await DataAsync("/api/deployments/history")).GetProperty("items")[0];
        item.GetProperty("deployReason").GetString().Should().Be("trying the new login");
    }

    [Fact]
    public async Task AutomaticDeploy_ShouldSkipAFrozenTarget_AndTheAuditTrailShouldSayWhy()
    {
        Release("foreverpin-gh-1", "v1.0.0", "2026-10-01T10:00:00Z");
        await PassAsync();
        await FreezeAsync(new { });
        Release("foreverpin-gh-2", "v1.1.0", "2026-10-02T10:00:00Z");

        var pass = await PassAsync();
        pass.Dropped.Should().Be(1);
        Fixture.Deployments.Starts.Should().BeEmpty();
        var entry = (await DataAsync("/api/audit")).EnumerateArray()
            .Single(item => item.GetProperty("action").GetString() == "deployment.start");
        entry.GetProperty("actor").GetString().Should().Be("automation");
        entry.GetProperty("outcome").GetString().Should().Be("failed");
        entry.GetProperty("reason").GetString().Should().Contain("frozen (every product); the automatic deploy was skipped");
    }

    private void Release(string bundle, string release, string published)
    {
        Fixture.Deployments.Latest.Clear();
        Fixture.Deployments.Latest.Add(new { product = "foreverpin", ok = true, release = new { id = bundle, release, publishedAt = published } });
    }

    private async Task<Wheelhouse.Application.Deployments.Automation.Models.DeploymentAutomationPass> PassAsync()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DeploymentAutomationService>().RunOnceAsync(CancellationToken.None);
    }

    private async Task<JsonElement> FreezeAsync(object body)
    {
        var response = await Owner("freeze").PostAsJsonAsync("/api/deployments/freezes", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private HttpClient Owner(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private async Task<JsonElement> DataAsync(string path)
    {
        var response = await AdminClient.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
