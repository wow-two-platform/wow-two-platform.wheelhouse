using System.Diagnostics;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.InfraJobs;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Infrastructure.Infra;
using Wheelhouse.Infrastructure.Jobs;
using WoW.Two.Sdk.Beta.Be.Jobs.Core.BackgroundServices;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.Integration.Tests;

/// <summary>Exercises the infrastructure follower as the host registers it: deployed dev, which can hold no
/// infrastructure job, runs no pass at all; prod reads the runner on start and then only while a job is in flight.</summary>
public sealed class InfraFollowerTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly CountingInfraGateway _gateway = new();

    [Fact]
    public async Task Following_ShouldStartNoPass_OnDeployedDev()
    {
        using var host = CreateHost("dev");
        await host.StartAsync();
        try
        {
            await Follower(host).ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
            _time.Advance(TimeSpan.FromHours(1));
            _gateway.Reads.Should().Be(0);
        }
        finally
        {
            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Following_ShouldReadOnStart_AndThenOnlyOncePerIdleInterval_OnProd()
    {
        using var host = CreateHost("prod");
        await host.StartAsync();
        try
        {
            await UntilAsync(() => _gateway.Reads == 1);
            for (var tick = 0; tick < 20; tick++)
            {
                _time.Advance(TimeSpan.FromSeconds(10));
                await Task.Delay(20);
            }
            _gateway.Reads.Should().Be(1, "no job is in flight, so passes read nothing until the safety pass");

            host.Services.GetRequiredService<InfraFollowSchedule>().Wake();
            await UntilAsync(() => _gateway.Reads == 2, () => _time.Advance(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private IHost CreateHost(string environment) => new HostBuilder()
        .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [ConsoleInfraAccess.DeployEnvironmentKey] = environment }))
        .ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(_time);
            services.AddSingleton<IInfraAccess, ConsoleInfraAccess>();
            services.AddSingleton<IInfraGateway>(_gateway);
            services.AddScoped<InfraJobRecorder>();
            services.AddInfraFollowingJob();
        })
        .Build();

    private static PeriodicJobBackgroundService Follower(IHost host) =>
        host.Services.GetRequiredService<IEnumerable<IHostedService>>().OfType<PeriodicJobBackgroundService>()
            .Single(job => job.Name == PeriodicJobsServiceCollectionExtensions.InfraFollowJob);

    private static async Task UntilAsync(Func<bool> condition, Action? advance = null)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            deadline.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the follower should read");
            advance?.Invoke();
            await Task.Delay(20);
        }
    }

    /// <summary>Answers the finished-jobs read with none finished and none in flight, counting each read.</summary>
    private sealed class CountingInfraGateway : IInfraGateway
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public Task<AppResult<JsonElement>> FinishedAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _reads);
            return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(new { jobs = Array.Empty<object>(), open = 0 })));
        }

        public Task<AppResult<JsonElement>> AcknowledgeAsync(string job, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppResult<JsonElement>> TailnetAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<AppResult<JsonElement>> PolicyAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<AppResult<JsonElement>> ApplyPolicyAsync(string etag, string digest, string actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppResult<JsonElement>> RevertPolicyAsync(string etag, string actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppResult<JsonElement>> EnrollmentAsync(string server, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppResult<JsonElement>> EnrollmentKeyAsync(string server, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppResult<JsonElement>> EnrollAsync(ServerEnrollmentRequest request, string actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppResult<JsonElement>> PlacementsAsync(string product, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppResult<JsonElement>> PlaceAsync(ProductPlacementRequest request, string actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppResult<JsonElement>> UpkeepsAsync(string server, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppResult<JsonElement>> UpkeepAsync(string server, string actor, CancellationToken ct) => throw new NotSupportedException();
    }
}
