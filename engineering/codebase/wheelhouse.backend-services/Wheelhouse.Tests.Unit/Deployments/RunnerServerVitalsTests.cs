using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Deployments;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Caching.Memo;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.Unit.Deployments;

/// <summary>
/// Tests for <see cref="RunnerServerVitals"/>: one runner read serves every reader for <c>Deployment:VitalsSeconds</c>,
/// a fresh read replaces it, concurrent readers share one read, and a failed read is never held.
/// </summary>
public sealed class RunnerServerVitalsTests
{
    private readonly VitalsGateway _gateway = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-27T10:00:00Z"));
    private readonly CoalescingMemo<AppResult<JsonElement>> _memo;

    public RunnerServerVitalsTests() => _memo = new(_time);

    private RunnerServerVitals Vitals(int seconds = 60) =>
        new(_gateway, _memo, new DeploymentSettings { VitalsSeconds = seconds });

    [Fact]
    public async Task ReadAsync_ShouldShareOneRunnerRead_WhileItIsYoungerThanTheHoldingTime()
    {
        (await Vitals().ReadAsync(false, CancellationToken.None)).Should().BeOfType<AppResult<JsonElement>.Success>();
        _time.Advance(TimeSpan.FromSeconds(59));
        var held = await Vitals().ReadAsync(false, CancellationToken.None);

        _gateway.Reads.Should().Be(1);
        Snapshot(held).Should().Be(1);

        _time.Advance(TimeSpan.FromSeconds(1));
        Snapshot(await Vitals().ReadAsync(false, CancellationToken.None)).Should().Be(2);
        _gateway.Reads.Should().Be(2);
    }

    [Fact]
    public async Task ReadAsync_ShouldReadTheHostsNow_WhenFresh_AndServeThatReadToEveryone()
    {
        await Vitals().ReadAsync(false, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(1));

        Snapshot(await Vitals().ReadAsync(true, CancellationToken.None)).Should().Be(2);
        Snapshot(await Vitals().ReadAsync(false, CancellationToken.None)).Should().Be(2);
        _gateway.Reads.Should().Be(2);
    }

    [Fact]
    public async Task ReadAsync_ShouldReadEveryTime_WhenHoldingIsOff()
    {
        await Vitals(0).ReadAsync(false, CancellationToken.None);
        await Vitals(0).ReadAsync(false, CancellationToken.None);

        _gateway.Reads.Should().Be(2);
    }

    [Fact]
    public async Task ReadAsync_ShouldNeverHoldAFailure()
    {
        _gateway.Failure = AppErrorFactory.ExternalUnavailable("SSH connection refused");
        (await Vitals().ReadAsync(false, CancellationToken.None)).Should().BeOfType<AppResult<JsonElement>.Failure>();
        _gateway.Failure = null;

        (await Vitals().ReadAsync(false, CancellationToken.None)).Should().BeOfType<AppResult<JsonElement>.Success>();
        _gateway.Reads.Should().Be(2);
    }

    [Fact]
    public async Task ReadAsync_ShouldShareOneRead_WhenTabsAskAtOnce()
    {
        _gateway.Hold = new TaskCompletionSource();
        var first = Vitals().ReadAsync(false, CancellationToken.None);
        var second = Vitals().ReadAsync(false, CancellationToken.None);
        var fresh = Vitals().ReadAsync(true, CancellationToken.None);

        _gateway.Hold.SetResult();
        var reads = await Task.WhenAll(first, second, fresh);

        _gateway.Reads.Should().Be(1);
        reads.Select(Snapshot).Should().Equal(1, 1, 1);
    }

    private static int Snapshot(AppResult<JsonElement> result) =>
        result.Should().BeOfType<AppResult<JsonElement>.Success>().Subject.Data.GetProperty("read").GetInt32();

    /// <summary>Answers each vitals read with its number; can hold a read open and fail.</summary>
    private sealed class VitalsGateway : IDeploymentGateway
    {
        public int Reads { get; private set; }

        public AppError? Failure { get; set; }

        public TaskCompletionSource? Hold { get; set; }

        public async Task<AppResult<JsonElement>> ReadAsync(string resource, string? id, CancellationToken ct)
        {
            resource.Should().Be("vitals");
            Reads++;
            if (Hold is not null)
                await Hold.Task;
            return Failure is not null
                ? AppResult<JsonElement>.Fail(Failure)
                : AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(new { read = Reads, targets = Array.Empty<object>() }));
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
