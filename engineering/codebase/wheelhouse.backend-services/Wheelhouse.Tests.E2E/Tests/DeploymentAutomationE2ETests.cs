using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Deployments.Automation.Services;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for automatic deploys: each product's choices (auto-deploy to dev on by default, auto-promote off, never for a
/// product its own CI deploys), the release watcher (a first read only records the watermark; a newer release is queued
/// for every dev target), and the per-target queue (latest wins, waits for a running rollout, submits as
/// <c>automation</c> through the audited deploy command, drops a deploy the owner turned off).
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class DeploymentAutomationE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Fact]
    public async Task Automation_RequiresTheOwner()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await AnonymousClient.GetAsync("/api/deployments/automation")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await AnonymousClient.PutAsJsonAsync("/api/deployments/automation/foreverpin", new { autoDeployDev = false })).StatusCode);
    }

    [Fact]
    public async Task Automation_ShouldDeployEachReleaseToDevByDefault_AndNeverACiDeployedProduct()
    {
        var products = (await DataAsync("/api/deployments/automation")).GetProperty("products").EnumerateArray()
            .ToDictionary(item => item.GetProperty("product").GetString()!);

        products["foreverpin"].GetProperty("autoDeployDev").GetBoolean().Should().BeTrue();
        products["foreverpin"].GetProperty("autoPromote").GetBoolean().Should().BeFalse();
        products["foreverpin"].GetProperty("ciDeployed").GetBoolean().Should().BeFalse();
        products["wheelhouse"].GetProperty("autoDeployDev").GetBoolean().Should().BeFalse();
        products["wheelhouse"].GetProperty("ciDeployed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAutomation_ShouldNeedItsActionHeader_ChangeOnlyWhatItNames_AndBeAudited()
    {
        (await AdminClient.PutAsJsonAsync("/api/deployments/automation/foreverpin", new { autoDeployDev = false }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var response = await Owner().PutAsJsonAsync("/api/deployments/automation/foreverpin", new { autoDeployDev = false });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("autoDeployDev").GetBoolean().Should().BeFalse();
        data.GetProperty("autoPromote").GetBoolean().Should().BeFalse();

        response = await Owner().PutAsJsonAsync("/api/deployments/automation/foreverpin", new { autoPromote = true });
        data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("autoDeployDev").GetBoolean().Should().BeFalse("an omitted choice stays as it is");
        data.GetProperty("autoPromote").GetBoolean().Should().BeTrue();

        var audit = (await DataAsync("/api/audit")).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "product.automation").ToList();
        audit.Should().HaveCount(2);
        audit.Select(entry => entry.GetProperty("detail").GetString())
            .Should().BeEquivalentTo("auto-deploy to dev off", "auto-promote to prod on");
    }

    [Fact]
    public async Task UpdateAutomation_ShouldRefuseAnEmptyChange_AnUnknownProduct_AndTurningOnACiDeployedOne()
    {
        (await Owner().PutAsJsonAsync("/api/deployments/automation/foreverpin", new { }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Owner().PutAsJsonAsync("/api/deployments/automation/nothing-here", new { autoDeployDev = true }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Owner().PutAsJsonAsync("/api/deployments/automation/wheelhouse", new { autoDeployDev = true }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Owner().PutAsJsonAsync("/api/deployments/automation/wheelhouse", new { autoDeployDev = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "turning a CI-deployed product off changes nothing it does");
    }

    [Fact]
    public async Task Watcher_ShouldOnlyRecordTheWatermarkOnItsFirstRead_ThenDeployANewerReleaseToDevAsAutomation()
    {
        Release("foreverpin-gh-1", "v1.0.0", "2026-10-01T10:00:00Z");
        (await PassAsync()).Submitted.Should().Be(0, "an existing release is the baseline, never a deploy");
        Fixture.Deployments.Starts.Should().BeEmpty();
        Fixture.Deployments.LastRead.Should().Be(("latest", (string?)null));

        (await PassAsync()).Submitted.Should().Be(0, "the same release is not new");

        Release("foreverpin-gh-2", "v1.1.0", "2026-10-02T10:00:00Z");
        var pass = await PassAsync();
        pass.Queued.Should().Be(1);
        pass.Submitted.Should().Be(1);
        Fixture.Deployments.Starts.Should().Equal(("foreverpin-dev", "foreverpin-gh-2", "automation"));

        var entry = (await DataAsync("/api/audit")).EnumerateArray()
            .Single(item => item.GetProperty("action").GetString() == "deployment.start");
        entry.GetProperty("actor").GetString().Should().Be("automation");
        entry.GetProperty("subject").GetString().Should().Be("foreverpin-dev");
        entry.GetProperty("detail").GetString().Should().Be("foreverpin-gh-2, automatic: a new release");
    }

    [Fact]
    public async Task Queue_ShouldWaitForARunningRollout_KeepOnlyTheLatestRelease_ThenSubmitIt()
    {
        Release("foreverpin-gh-1", "v1.0.0", "2026-10-01T10:00:00Z");
        await PassAsync();
        Fixture.Deployments.History.Add(new
        {
            id = "00000000-0000-4000-8000-000000000001", targetId = "foreverpin-dev", bundleId = "foreverpin-ci-9",
            release = "sha-abc1234", status = "running", submittedAt = "2026-10-02T09:00:00+00:00",
        });

        Release("foreverpin-gh-2", "v1.1.0", "2026-10-02T10:00:00Z");
        (await PassAsync()).Submitted.Should().Be(0, "a running rollout finishes first");
        Release("foreverpin-gh-3", "v1.2.0", "2026-10-02T11:00:00Z");
        (await PassAsync()).Submitted.Should().Be(0);

        var queue = (await DataAsync("/api/deployments/automation")).GetProperty("queue").EnumerateArray().ToList();
        var waiting = queue.Should().ContainSingle().Subject;
        waiting.GetProperty("targetId").GetString().Should().Be("foreverpin-dev");
        waiting.GetProperty("release").GetString().Should().Be("v1.2.0", "the latest release wins its target's slot");
        waiting.GetProperty("cause").GetString().Should().Be("auto_dev");
        Fixture.Deployments.Starts.Should().BeEmpty();

        Fixture.Deployments.History.Clear();
        (await PassAsync()).Submitted.Should().Be(1);
        Fixture.Deployments.Starts.Should().Equal(("foreverpin-dev", "foreverpin-gh-3", "automation"));
        (await DataAsync("/api/deployments/automation")).GetProperty("queue").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Queue_ShouldDropADeployTheOwnerTurnedOff_AndTheWatcherShouldReadNothingThen()
    {
        Release("foreverpin-gh-1", "v1.0.0", "2026-10-01T10:00:00Z");
        await PassAsync();
        Fixture.Deployments.History.Add(new
        {
            id = "00000000-0000-4000-8000-000000000001", targetId = "foreverpin-dev", bundleId = "foreverpin-ci-9",
            release = "sha-abc1234", status = "running", submittedAt = "2026-10-02T09:00:00+00:00",
        });
        Release("foreverpin-gh-2", "v1.1.0", "2026-10-02T10:00:00Z");
        await PassAsync();
        Fixture.Deployments.History.Clear();

        (await Owner().PutAsJsonAsync("/api/deployments/automation/foreverpin", new { autoDeployDev = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var pass = await PassAsync();
        pass.Dropped.Should().Be(1);
        Fixture.Deployments.Starts.Should().BeEmpty();
        Fixture.Deployments.LastRead.Should().NotBe(("latest", (string?)null), "no product is watched");
    }

    [Fact]
    public async Task Queue_ShouldDropARelease_TheTargetAlreadyRan()
    {
        Release("foreverpin-gh-1", "v1.0.0", "2026-10-01T10:00:00Z");
        await PassAsync();
        // The owner deployed v1.1.0 by hand before the watcher read it.
        Fixture.Deployments.History.Add(new
        {
            id = "00000000-0000-4000-8000-000000000001", targetId = "foreverpin-dev", bundleId = "foreverpin-gh-2",
            release = "v1.1.0", status = "succeeded", submittedAt = "2026-10-02T10:05:00+00:00",
            completedAt = "2026-10-02T10:07:00+00:00",
        });
        Release("foreverpin-gh-2", "v1.1.0", "2026-10-02T10:00:00Z");

        var pass = await PassAsync();
        pass.Queued.Should().Be(1);
        pass.Dropped.Should().Be(1);
        Fixture.Deployments.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task Pass_ShouldRunTheWatchLooksAndTheRetention_EvenWithNothingToDeploy()
    {
        var reads = new List<(string, string?)>();
        var recorder = new RecordingGateway(Fixture.Deployments, reads);
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var automation = ActivatorUtilities.CreateInstance<DeploymentAutomationService>(services,
            (Wheelhouse.Application.Abstractions.IDeploymentGateway)recorder);
        await automation.RunOnceAsync(CancellationToken.None, watchMinutes: 15);
        reads.Should().Contain(("watch", "15")).And.Contain(("retention", (string?)null));

        reads.Clear();
        await automation.RunOnceAsync(CancellationToken.None, watchMinutes: 0);
        reads.Should().NotContain(read => read.Item1 == "watch", "a zero window turns the looks off");
    }

    /// <summary>Records every read, then answers as the shared stub does.</summary>
    private sealed class RecordingGateway(StubDeploymentGateway inner, List<(string, string?)> reads)
        : Wheelhouse.Application.Abstractions.IDeploymentGateway
    {
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> ReadAsync(string resource, string? id, CancellationToken ct)
        {
            reads.Add((resource, id));
            return inner.ReadAsync(resource, id, ct);
        }

        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> CheckAsync(string target, string? release, CancellationToken ct) => inner.CheckAsync(target, release, ct);
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> StartAsync(string target, string release, string actor, string? confirm, bool skipDevPass, string? reason, CancellationToken ct) =>
            inner.StartAsync(target, release, actor, confirm, skipDevPass, reason, ct);
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> ReconcileAsync(string target, string job, string actor, CancellationToken ct) => inner.ReconcileAsync(target, job, actor, ct);
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> CommitsAsync(string product, string branch, CancellationToken ct) => inner.CommitsAsync(product, branch, ct);
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> ChangelogAsync(string product, string? from, string to, CancellationToken ct) => inner.ChangelogAsync(product, from, to, ct);
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> RequestBuildAsync(string product, string commit, CancellationToken ct) => inner.RequestBuildAsync(product, commit, ct);
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> LogsAsync(string target, string service, int tail, CancellationToken ct) => inner.LogsAsync(target, service, tail, ct);
    }

    /// <summary>Makes <paramref name="release"/> ForeverPin's newest published release.</summary>
    private void Release(string bundle, string release, string published)
    {
        Fixture.Deployments.Latest.Clear();
        Fixture.Deployments.Latest.Add(new { product = "foreverpin", ok = true, release = new { id = bundle, release, publishedAt = published } });
    }

    /// <summary>Runs one automation pass, outside any request, as the background worker does.</summary>
    private async Task<Wheelhouse.Application.Deployments.Automation.Models.DeploymentAutomationPass> PassAsync()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DeploymentAutomationService>().RunOnceAsync(CancellationToken.None);
    }

    private HttpClient Owner()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "automation");
        return client;
    }

    private async Task<JsonElement> DataAsync(string path)
    {
        var response = await AdminClient.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
