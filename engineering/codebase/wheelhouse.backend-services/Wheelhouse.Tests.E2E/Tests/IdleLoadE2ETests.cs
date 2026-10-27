using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Deployments.Following.Services;
using Wheelhouse.Application.InfraJobs;
using Wheelhouse.Tests.E2E.Harness;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for an idle console's load: the deployment and infrastructure followers read the runner only while work is in
/// flight — a deploy, control or reconcile the console submits, or an infrastructure job it starts, wakes them, and a
/// runner refusal does not — and every tab shares one vitals read until the owner's explicit refresh.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class IdleLoadE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task DeploymentFollower_ShouldReadNoRunnerWhileIdle_AndFollowADeployUntilItFinishes()
    {
        await SettleIdleAsync();
        (await FollowAsync()).Should().BeFalse("nothing is in flight, so the pass starts no runner");
        FollowReads().Should().Be(0);

        (await Act("deploy").PostAsJsonAsync("/api/deployments", new { target = "pilot", release = "v1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        Fixture.Deployments.FollowOpen = 1;
        Fixture.Deployments.FollowNextIn = 0;
        (await FollowAsync()).Should().BeTrue("the deploy woke the follower");
        (await FollowAsync()).Should().BeTrue("the rollout is unfinished and its next look is due");

        Fixture.Deployments.FollowOpen = 0;
        Fixture.Deployments.FollowNextIn = null;
        (await FollowAsync()).Should().BeTrue();
        (await FollowAsync()).Should().BeFalse("the rollout finished");
        FollowReads().Should().Be(3);
    }

    [Fact]
    public async Task DeploymentFollower_ShouldWakeForAControlAndAReconcile_AndNotForARunnerRefusal()
    {
        await SettleIdleAsync();
        Fixture.Deployments.StartRefusal = "Deployment rejected: Unknown release";
        (await Act("deploy").PostAsJsonAsync("/api/deployments", new { target = "pilot", release = "v9" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await FollowAsync()).Should().BeFalse("the runner refused before it recorded anything");

        (await Act("control").PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/controls",
            new { operation = "restart", service = "management" })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await FollowAsync()).Should().BeTrue("the control woke the follower");
        (await FollowAsync()).Should().BeFalse();

        (await Act("reconcile").PostAsJsonAsync("/api/deployments/targets/pilot/reconcile", new { job = Guid.NewGuid() }))
            .IsSuccessStatusCode.Should().BeTrue();
        (await FollowAsync()).Should().BeTrue("the reconcile woke the follower");
    }

    [Fact]
    public async Task InfraFollower_ShouldReadNoRunnerWhileIdle_AndFollowAStartedJobUntilItIsRecorded()
    {
        await RecordAsync();
        Fixture.Infra.Calls.Clear();
        (await RecordAsync()).Should().Be(0);
        Fixture.Infra.Calls.Should().NotContain(call => call.Action == "finished", "no job is in flight");

        (await Act("server").PostAsync("api/servers/prod-host/upkeep", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        Fixture.Infra.Open = 1;
        await RecordAsync();
        await RecordAsync();
        Fixture.Infra.Calls.Count(call => call.Action == "finished").Should().Be(2, "the job is still running");

        Fixture.Infra.Open = 0;
        await RecordAsync();
        await RecordAsync();
        Fixture.Infra.Calls.Count(call => call.Action == "finished").Should().Be(3, "the job finished");
    }

    [Fact]
    public async Task Vitals_ShouldServeEveryTabOneRead_UntilTheOwnersExplicitRefresh()
    {
        var first = await AdminClient.GetAsync("/api/deployments/vitals");
        Fixture.Host.Clock.Advance(TimeSpan.FromSeconds(59));
        var second = await AdminClient.GetAsync("/api/deployments/vitals");
        (first.StatusCode, second.StatusCode).Should().Be((HttpStatusCode.OK, HttpStatusCode.OK));
        (await Targets(second)).Should().Be("pilot");
        VitalsReads().Should().Be(1, "every tab shares the held snapshot");

        (await AdminClient.GetAsync("/api/deployments/vitals?fresh=true")).StatusCode.Should().Be(HttpStatusCode.OK);
        VitalsReads().Should().Be(2, "an explicit refresh reads the hosts now");
        Fixture.Host.Clock.Advance(TimeSpan.FromSeconds(59));
        (await AdminClient.GetAsync("/api/deployments/vitals")).StatusCode.Should().Be(HttpStatusCode.OK);
        VitalsReads().Should().Be(2, "the refresh replaced the shared snapshot");

        Fixture.Host.Clock.Advance(TimeSpan.FromSeconds(1));
        (await AdminClient.GetAsync("/api/deployments/vitals")).StatusCode.Should().Be(HttpStatusCode.OK);
        VitalsReads().Should().Be(3, "a snapshot older than Deployment:VitalsSeconds is read again");
    }

    [Fact]
    public async Task Vitals_ShouldNeverHoldAFailedRead()
    {
        Fixture.Deployments.ReadFailure = AppErrorFactory.ExternalUnavailable("SSH connection refused");
        (await AdminClient.GetAsync("/api/deployments/vitals")).IsSuccessStatusCode.Should().BeFalse();
        Fixture.Deployments.ReadFailure = null;

        (await AdminClient.GetAsync("/api/deployments/vitals")).StatusCode.Should().Be(HttpStatusCode.OK);
        VitalsReads().Should().Be(2);
    }

    /// <summary>Brings the shared deployment follower to idle, whatever earlier tests left, and forgets its reads.</summary>
    private async Task SettleIdleAsync()
    {
        Fixture.Deployments.FollowOpen = 0;
        await FollowAsync();
        Fixture.Deployments.Reads.Clear();
    }

    private async Task<bool> FollowAsync()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DeploymentFollowing>().PassAsync(Idle, CancellationToken.None);
    }

    private async Task<int> RecordAsync()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<InfraJobRecorder>().FollowAsync(Idle, CancellationToken.None);
    }

    private int FollowReads() => Fixture.Deployments.Reads.Count(read => read.Resource == "follow");

    private int VitalsReads() => Fixture.Deployments.Reads.Count(read => read.Resource == "vitals");

    private static async Task<string?> Targets(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("targets")[0]
            .GetProperty("targetId").GetString();

    private HttpClient Act(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }
}
