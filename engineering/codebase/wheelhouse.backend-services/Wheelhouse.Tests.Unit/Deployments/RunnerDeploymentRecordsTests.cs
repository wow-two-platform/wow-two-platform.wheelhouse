using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Infrastructure.Deployments;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.Unit.Deployments;

/// <summary>
/// Tests for <see cref="RunnerDeploymentRecords"/>: one runner read serves a page's reads for
/// <c>Deployment:HistorySeconds</c>, a failed read is never held, and a malformed record is skipped.
/// </summary>
public sealed class RunnerDeploymentRecordsTests
{
    private readonly CountingGateway _gateway = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-17T10:00:00Z"));
    private readonly DeploymentRecordsMemo _memo = new();

    private RunnerDeploymentRecords Records(int seconds = 3) =>
        new(_gateway, _memo, new DeploymentSettings { HistorySeconds = seconds }, _time, NullLogger<RunnerDeploymentRecords>.Instance);

    [Fact]
    public async Task ListAsync_ShouldShareOneRunnerRead_WhileItIsFresh()
    {
        (await Records().ListAsync(CancellationToken.None)).Should().BeOfType<AppResult<IReadOnlyList<DeploymentRecord>>.Success>();
        _time.Advance(TimeSpan.FromSeconds(2));
        await Records().ListAsync(CancellationToken.None);
        _gateway.Reads.Should().Be(1);

        _time.Advance(TimeSpan.FromSeconds(2));
        await Records().ListAsync(CancellationToken.None);
        _gateway.Reads.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_ShouldReadEveryTime_WhenHoldingIsOff()
    {
        await Records(0).ListAsync(CancellationToken.None);
        await Records(0).ListAsync(CancellationToken.None);
        _gateway.Reads.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_ShouldNeverHoldAFailure()
    {
        _gateway.Failure = AppErrorFactory.OperationTimeout("Deployment response timed out.");
        (await Records().ListAsync(CancellationToken.None)).Should().BeOfType<AppResult<IReadOnlyList<DeploymentRecord>>.Failure>();
        _gateway.Failure = null;
        (await Records().ListAsync(CancellationToken.None)).Should().BeOfType<AppResult<IReadOnlyList<DeploymentRecord>>.Success>();
        _gateway.Reads.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_ShouldSkipAMalformedRecord()
    {
        var result = await Records().ListAsync(CancellationToken.None);
        var records = result.Should().BeOfType<AppResult<IReadOnlyList<DeploymentRecord>>.Success>().Subject.Data;
        records.Select(record => record.Id).Should().Equal("00000000-0000-4000-8000-000000000001");
        records[0].Services!["api"].Version.Should().Be("1.0.0");
    }

    [Fact]
    public async Task ListAsync_ShouldReadEachRowsServiceRolloutAndCallAReplacedWatchReplaced()
    {
        var result = await Records().ListAsync(CancellationToken.None);
        var record = result.Should().BeOfType<AppResult<IReadOnlyList<DeploymentRecord>>.Success>().Subject.Data[0];
        record.Watch!.Status.Should().Be("replaced", "a window recorded `superseded` before the rename reads by its new word");
        var api = record.ServiceRollout!.Services.Single();
        (api.Name, api.Outcome, api.From, api.To, api.Reason, api.Health).Should().Be(("api", "updated", "0.9.0", "1.0.0", "image", "healthy"));
        api.At.Should().Be(DateTimeOffset.Parse("2026-10-01T10:01:00Z"));
    }

    [Fact]
    public async Task ObserveAsync_ShouldReadEachServicesChangeWaveAndOutcome()
    {
        var observed = await Records().ObserveAsync(Guid.Parse("00000000-0000-4000-8000-000000000001"), CancellationToken.None);
        var rollout = observed.Should().BeOfType<AppResult<DeploymentObservation>.Success>().Subject.Data.ServiceRollout!;
        rollout.Staged.Should().BeTrue();
        rollout.Waves.Should().HaveCount(2);
        rollout.Waves[0].Should().Equal("db");
        rollout.Removed.Should().Equal("legacy");
        rollout.Services.Select(service => (service.Name, service.Change, service.Outcome)).Should().Equal(
            ("api", "updated", "failed"), ("db", "updated", "rolled_back"));
        rollout.Services[0].DependsOn.Should().Equal("db");
        // Malformed entries are left out; a report without the field has none.
        _gateway.Observation = new { status = "succeeded" };
        var plain = await Records().ObserveAsync(Guid.Parse("00000000-0000-4000-8000-000000000001"), CancellationToken.None);
        plain.Should().BeOfType<AppResult<DeploymentObservation>.Success>().Subject.Data.ServiceRollout.Should().BeNull();
    }

    /// <summary>Answers the history read with one good record and two malformed ones, counting reads.</summary>
    private sealed class CountingGateway : IDeploymentGateway
    {
        public int Reads { get; private set; }

        public AppError? Failure { get; set; }

        /// <summary>What the <c>observation</c> read answers.</summary>
        public object Observation { get; set; } = new
        {
            status = "rolled_back",
            serviceRollout = new
            {
                staged = true, waves = new[] { new[] { "db" }, new[] { "api" } }, removed = new[] { "legacy" },
                services = new object[]
                {
                    new { name = "api", change = "updated", outcome = "failed", dependsOn = new[] { "db" } },
                    new { name = "db", change = "updated", outcome = "rolled_back", dependsOn = Array.Empty<string>() },
                    new { name = "broken" },
                },
            },
        };

        public Task<AppResult<JsonElement>> ReadAsync(string resource, string? id, CancellationToken ct)
        {
            if (resource == "observation")
                return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(Observation)));
            Reads++;
            if (Failure is not null)
                return Task.FromResult(AppResult<JsonElement>.Fail(Failure));
            return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(new object[]
            {
                new
                {
                    id = "00000000-0000-4000-8000-000000000001", status = "succeeded", submittedAt = "2026-10-01T10:00:00.123456+00:00",
                    services = new Dictionary<string, object> { ["api"] = new { image = "ghcr.io/o/api@sha256:" + new string('a', 64), version = "1.0.0" } },
                    watch = new { status = "superseded", checks = 2 },
                    serviceRollout = new
                    {
                        staged = false, waves = new[] { new[] { "api" } }, removed = Array.Empty<string>(),
                        services = new object[]
                        {
                            new
                            {
                                name = "api", change = "updated", outcome = "updated", dependsOn = Array.Empty<string>(), from = "0.9.0",
                                to = "1.0.0", reason = "image", health = "healthy", at = "2026-10-01T10:01:00+00:00",
                            },
                        },
                    },
                },
                new { id = "broken", status = "failed", submittedAt = "yesterday" },
                new { status = "failed", submittedAt = "2026-10-01T10:00:00+00:00" },
            })));
        }

        public Task<AppResult<JsonElement>> CheckAsync(string target, string? release, CancellationToken ct) => throw new NotSupportedException();

        public Task<AppResult<JsonElement>> StartAsync(string target, string release, string actor, string? confirm, bool skip, string? reason, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AppResult<JsonElement>> ReconcileAsync(string target, string job, string actor, CancellationToken ct) => throw new NotSupportedException();

        public Task<AppResult<JsonElement>> CommitsAsync(string product, string branch, CancellationToken ct) => throw new NotSupportedException();

        public Task<AppResult<JsonElement>> ChangelogAsync(string product, string? from, string to, CancellationToken ct) => throw new NotSupportedException();

        public Task<AppResult<JsonElement>> RequestBuildAsync(string product, string commit, CancellationToken ct) => throw new NotSupportedException();

        public Task<AppResult<JsonElement>> LogsAsync(string target, string service, int tail, CancellationToken ct) => throw new NotSupportedException();
    }
}
