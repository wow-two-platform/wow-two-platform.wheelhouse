using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Operations;
using Wheelhouse.Tests.E2E.Harness;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>Verifies that a sampling pass stores every server's host readings and every target's container readings,
/// and the history endpoint returns them.</summary>
[Collection(WheelhouseCollection.Name)]
public sealed class VitalsHistoryE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Fact]
    public async Task History_RequiresAdmin() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await AnonymousClient.GetAsync("/api/deployments/vitals/history")).StatusCode);

    [Fact]
    public async Task SamplingPass_StoresEachTargetsReadingForTheHistory()
    {
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<VitalsSampling>().SampleOnceAsync(CancellationToken.None));

        var samples = (await AdminClient.GetFromJsonAsync<JsonElement>("/api/deployments/vitals/history?hours=1"))
            .GetProperty("data").EnumerateArray().ToArray();
        var sample = Assert.Single(samples);
        Assert.Equal(("pilot", "pilot-host", true), (sample.GetProperty("targetId").GetString(),
            sample.GetProperty("serverId").GetString(), sample.GetProperty("readable").GetBoolean()));
        Assert.Empty((await AdminClient.GetFromJsonAsync<JsonElement>("/api/deployments/vitals/history?target=other"))
            .GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task SamplingPass_StoresEachServersOwnHostReading_EvenBeforeItRunsATarget()
    {
        // The runner reads each server's host itself; fresh-host runs no target yet.
        var vitals = JsonDocument.Parse("""
            {"collectedAt":"2026-09-26T12:00:00+00:00",
             "servers":[{"serverId":"pilot-host","ok":true,"problems":[],
                         "host":{"cpus":2,"load":[1.0,0.5,0.2],"memoryTotalBytes":1000,"memoryAvailableBytes":600,
                                 "disks":[{"path":"/srv","totalBytes":100,"freeBytes":75}]}},
                        {"serverId":"fresh-host","ok":true,"problems":[],
                         "host":{"cpus":4,"load":[1.0,1.0,1.0],"memoryTotalBytes":1000,"memoryAvailableBytes":900,"disks":[]}}],
             "targets":[{"targetId":"pilot","serverId":"pilot-host","ok":true,
                         "containers":[{"service":"api","state":"running","health":"healthy","restarts":1}]}]}
            """).RootElement.Clone();
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var sampling = new VitalsSampling(new FleetVitals(vitals), services.GetRequiredService<IVitalsHistory>(),
                services.GetRequiredService<TimeProvider>());
            Assert.Equal(3, await sampling.SampleOnceAsync(CancellationToken.None));
        }

        var samples = (await AdminClient.GetFromJsonAsync<JsonElement>("/api/deployments/vitals/history?hours=1"))
            .GetProperty("data").EnumerateArray().ToArray();
        var hosts = samples.Where(sample => sample.GetProperty("targetId").ValueKind == JsonValueKind.Null)
            .Select(sample => (sample.GetProperty("serverId").GetString(), sample.GetProperty("loadPercent").GetDouble(),
                sample.GetProperty("memoryPercent").GetDouble()))
            .OrderBy(host => host.Item1).ToArray();
        Assert.Equal([("fresh-host", 25.0, 10.0), ("pilot-host", 50.0, 40.0)], hosts);
        var target = Assert.Single(samples, sample => sample.GetProperty("targetId").GetString() == "pilot");
        Assert.Equal((1, 1, JsonValueKind.Null), (target.GetProperty("containers").GetInt32(),
            target.GetProperty("healthyContainers").GetInt32(), target.GetProperty("loadPercent").ValueKind));

        // One target's history holds only that target's rows.
        var one = (await AdminClient.GetFromJsonAsync<JsonElement>("/api/deployments/vitals/history?hours=1&target=pilot"))
            .GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal("pilot", Assert.Single(one).GetProperty("targetId").GetString());
    }

    [Theory]
    [InlineData("?hours=0")]
    [InlineData("?hours=721")]
    [InlineData("?target=Not_A_Slug")]
    public async Task History_RejectsAnUnboundedWindowOrAnInvalidTarget(string query) =>
        Assert.Equal(HttpStatusCode.BadRequest,
            (await AdminClient.GetAsync("/api/deployments/vitals/history" + query)).StatusCode);
}

/// <summary>Answers the vitals read with a canned fleet pass; nothing else is called by a sampling pass.</summary>
file sealed class FleetVitals(JsonElement vitals) : IDeploymentGateway
{
    public Task<AppResult<JsonElement>> ReadAsync(string resource, string? id, CancellationToken ct) =>
        Task.FromResult(resource == "vitals"
            ? AppResult<JsonElement>.Ok(vitals)
            : throw new NotSupportedException(resource));

    public Task<AppResult<JsonElement>> CheckAsync(string target, string? release, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<AppResult<JsonElement>> StartAsync(
        string target, string release, string actor, string? confirm, bool skipTestPass, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<AppResult<JsonElement>> ReconcileAsync(string target, string job, string actor, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<AppResult<JsonElement>> CommitsAsync(string product, string branch, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<AppResult<JsonElement>> RequestBuildAsync(string product, string commit, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<AppResult<JsonElement>> LogsAsync(string target, string service, int tail, CancellationToken ct) =>
        throw new NotSupportedException();
}
