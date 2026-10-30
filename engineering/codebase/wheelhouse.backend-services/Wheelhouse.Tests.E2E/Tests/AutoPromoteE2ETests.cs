using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Deployments.Automation.Services;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the watch window and auto-promote: a rollout that regressed while watched is an Issue, and a product that
/// auto-promotes sends a release that passed its dev watch to prod once, as <c>automation</c>, never while frozen, never
/// an old pass, and never a degraded one.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class AutoPromoteE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Fact]
    public async Task AutoPromote_ShouldSendAReleaseThatPassedItsDevWatchToProd_OnceAsAutomation()
    {
        await AutoPromoteAsync();
        Seed("passed", TimeSpan.FromMinutes(2));

        (await PassAsync()).Submitted.Should().Be(1);
        Fixture.Deployments.Starts.Should().Equal(("foreverpin-prod", "foreverpin-gh-2", "automation"));
        var entry = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .First(item => item.GetProperty("action").GetString() == "deployment.start");
        entry.GetProperty("detail").GetString().Should().Be("foreverpin-gh-2, automatic: promoted after its dev watch");

        Fixture.Deployments.Starts.Clear();
        (await PassAsync()).Submitted.Should().Be(0, "a release is tried once per prod target");
        Fixture.Deployments.Starts.Should().BeEmpty();
    }

    [Theory]
    [InlineData("passed", 90, false, false)]   // an old pass
    [InlineData("degraded", 2, false, false)]  // regressed on dev
    [InlineData("watching", 2, false, false)]  // still watched
    [InlineData("passed", 2, true, false)]     // prod frozen
    [InlineData("passed", 2, false, true)]     // auto-promote off
    public async Task AutoPromote_ShouldLeaveProdAlone_UnlessARecentPassMayGo(string watch, int minutesAgo, bool frozen, bool off)
    {
        if (!off)
            await AutoPromoteAsync();
        if (frozen)
        {
            var client = AdminClient;
            client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "freeze");
            (await client.PostAsJsonAsync("/api/deployments/freezes", new { product = "foreverpin", environment = "prod" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        Seed(watch, TimeSpan.FromMinutes(minutesAgo));
        (await PassAsync()).Submitted.Should().Be(0);
        Fixture.Deployments.Starts.Should().BeEmpty();
        (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Count(item => item.GetProperty("action").GetString() == "deployment.start").Should().Be(0, "nothing is tried, so nothing is refused");
    }

    [Theory]
    [InlineData("passed", true, true)]
    [InlineData("failed", true, false)]
    [InlineData(null, true, false)]  // declares a check that has not reported
    [InlineData(null, false, true)]  // declares none
    public async Task AutoPromote_ShouldTakeOnlyAReleaseWhoseDevCheckPassed_WhenItDeclaresOne(string? check, bool verifies, bool promoted)
    {
        await AutoPromoteAsync();
        Seed("passed", TimeSpan.FromMinutes(2), verifies: verifies, check: check);

        // A manual promote shows the check's result either way.
        var offer = (await DataAsync("/api/deployments/promotions")).EnumerateArray().Single();
        offer.GetProperty("verifies").GetBoolean().Should().Be(verifies);
        if (check is null)
            offer.GetProperty("verification").ValueKind.Should().Be(JsonValueKind.Null);
        else
            offer.GetProperty("verification").GetProperty("status").GetString().Should().Be(check);

        (await PassAsync()).Submitted.Should().Be(promoted ? 1 : 0);
        Fixture.Deployments.Starts.Should().HaveCount(promoted ? 1 : 0);
    }

    [Fact]
    public async Task History_ShouldCarryTheDevCheck_OnTheDeploymentAndItsPage()
    {
        Seed("passed", TimeSpan.FromMinutes(2), verifies: true, check: "failed");

        var item = (await DataAsync("/api/deployments/history")).GetProperty("items").EnumerateArray()
            .Single(entry => entry.GetProperty("release").GetString() == "v1.1.0");
        item.GetProperty("verifies").GetBoolean().Should().BeTrue();
        var verification = item.GetProperty("verification");
        (verification.GetProperty("status").GetString(), verification.GetProperty("detail").GetString(),
            verification.GetProperty("exitCode").GetInt32()).Should().Be(("failed", "Exited 3", 3));

        var detail = await DataAsync("/api/deployments/00000000-0000-4000-8000-000000000002/detail");
        detail.GetProperty("deployment").GetProperty("verification").GetProperty("status").GetString().Should().Be("failed");
    }

    [Fact]
    public async Task Issues_ShouldRaiseARolloutThatRegressedWhileWatched()
    {
        Seed("degraded", TimeSpan.FromMinutes(2), findings: ["management restarted 3 times since the deploy"]);

        var issue = (await DataAsync("/api/deployments/issues")).EnumerateArray().Single();
        issue.GetProperty("kind").GetString().Should().Be("degraded");
        issue.GetProperty("needsReconcile").GetBoolean().Should().BeFalse();
        var watch = issue.GetProperty("deployment").GetProperty("watch");
        watch.GetProperty("status").GetString().Should().Be("degraded");
        watch.GetProperty("findings")[0].GetString().Should().Be("management restarted 3 times since the deploy");
    }

    /// <summary>Prod runs v1.0.0; dev ran v1.1.0, which finished its watch <paramref name="ago"/> with <paramref name="watch"/>.</summary>
    private void Seed(string watch, TimeSpan ago, string[]? findings = null, bool? verifies = null, string? check = null)
    {
        // The host runs on the harness's fake clock.
        var now = Fixture.Host.Clock.GetUtcNow();
        string At(TimeSpan before) => (now - before).ToString("O", CultureInfo.InvariantCulture);
        Fixture.Deployments.History.Add(new
        {
            id = "00000000-0000-4000-8000-000000000001", targetId = "foreverpin-prod", bundleId = "foreverpin-gh-1", release = "v1.0.0",
            status = "succeeded", kind = "release", submittedAt = At(TimeSpan.FromDays(2)), completedAt = At(TimeSpan.FromDays(2)),
            publishedAt = At(TimeSpan.FromDays(3)),
        });
        Fixture.Deployments.History.Add(new
        {
            id = "00000000-0000-4000-8000-000000000002", targetId = "foreverpin-dev", bundleId = "foreverpin-gh-2", release = "v1.1.0",
            status = "succeeded", kind = "release", submittedAt = At(ago + TimeSpan.FromMinutes(12)), completedAt = At(ago + TimeSpan.FromMinutes(10)),
            publishedAt = At(TimeSpan.FromHours(3)),
            verifies,
            verification = check is null ? null : new
            {
                status = check, service = "management", startedAt = At(ago + TimeSpan.FromMinutes(10)),
                completedAt = At(ago + TimeSpan.FromMinutes(9)), seconds = 41, exitCode = check == "passed" ? 0 : 3,
                detail = check == "passed" ? "Passed" : "Exited 3",
            },
            watch = new
            {
                status = watch, startedAt = At(ago + TimeSpan.FromMinutes(10)), until = At(ago), checks = 9,
                findings = findings ?? [], completedAt = watch == "watching" ? null : At(ago), degradedAt = watch == "degraded" ? At(ago) : null,
            },
        });
    }

    private async Task AutoPromoteAsync()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "automation");
        (await client.PutAsJsonAsync("/api/deployments/automation/foreverpin", new { autoPromote = true, autoDeployDev = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Wheelhouse.Application.Deployments.Automation.Models.DeploymentAutomationPass> PassAsync()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DeploymentAutomationService>().RunOnceAsync(CancellationToken.None);
    }

    private async Task<JsonElement> DataAsync(string path)
    {
        var response = await AdminClient.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
