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

    /// <summary>Answers the history read with one good record and two malformed ones, counting reads.</summary>
    private sealed class CountingGateway : IDeploymentGateway
    {
        public int Reads { get; private set; }

        public AppError? Failure { get; set; }

        public Task<AppResult<JsonElement>> ReadAsync(string resource, string? id, CancellationToken ct)
        {
            Reads++;
            if (Failure is not null)
                return Task.FromResult(AppResult<JsonElement>.Fail(Failure));
            return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(new object[]
            {
                new
                {
                    id = "00000000-0000-4000-8000-000000000001", status = "succeeded", submittedAt = "2026-10-01T10:00:00.123456+00:00",
                    services = new Dictionary<string, object> { ["api"] = new { image = "ghcr.io/o/api@sha256:" + new string('a', 64), version = "1.0.0" } },
                },
                new { id = "broken", status = "failed", submittedAt = "yesterday" },
                new { status = "failed", submittedAt = "2026-10-01T10:00:00+00:00" },
            })));
        }

        public Task<AppResult<JsonElement>> CheckAsync(string target, string? release, CancellationToken ct) => throw new NotSupportedException();

        public Task<AppResult<JsonElement>> StartAsync(string target, string release, string actor, string? confirm, bool skip, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AppResult<JsonElement>> ReconcileAsync(string target, string job, string actor, CancellationToken ct) => throw new NotSupportedException();

        public Task<AppResult<JsonElement>> CommitsAsync(string product, string branch, CancellationToken ct) => throw new NotSupportedException();

        public Task<AppResult<JsonElement>> RequestBuildAsync(string product, string commit, CancellationToken ct) => throw new NotSupportedException();

        public Task<AppResult<JsonElement>> LogsAsync(string target, string service, int tail, CancellationToken ct) => throw new NotSupportedException();
    }
}
