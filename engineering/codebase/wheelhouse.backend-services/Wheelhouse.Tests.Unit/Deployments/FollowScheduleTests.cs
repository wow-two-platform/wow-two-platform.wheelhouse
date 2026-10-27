using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Wheelhouse.Application.Deployments.Following.Services;

namespace Wheelhouse.Tests.Unit.Deployments;

/// <summary>
/// Tests for the followers' schedule (<see cref="DeploymentFollowSchedule"/>): the first pass reads, a wake reads at
/// once, unfinished work reads when the runner names, idle reads once per safety interval, and a wake during a read is
/// never lost.
/// </summary>
public sealed class FollowScheduleTests
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-27T10:00:00Z"));

    private DeploymentFollowSchedule Schedule() => new(_time);

    [Fact]
    public void TryBegin_ShouldReadOnTheFirstPass_SoARestartLearnsWhatIsInFlight() =>
        Schedule().TryBegin(out _).Should().BeTrue();

    [Fact]
    public void TryBegin_ShouldReadOncePerIdleInterval_WhenNothingIsUnfinished()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket).Should().BeTrue();
        schedule.Settle(ticket, open: false, next: null, Idle);

        _time.Advance(TimeSpan.FromSeconds(10));
        schedule.TryBegin(out _).Should().BeFalse();
        _time.Advance(TimeSpan.FromMinutes(4));
        schedule.TryBegin(out _).Should().BeFalse();
        _time.Advance(TimeSpan.FromSeconds(50));
        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void TryBegin_ShouldReadAtOnce_WhenWorkWasSubmitted()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket);
        schedule.Settle(ticket, open: false, next: null, Idle);

        schedule.Wake();

        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void TryBegin_ShouldReadWhenTheRunnerNamed_WhileWorkIsUnfinished()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket);
        schedule.Settle(ticket, open: true, next: TimeSpan.FromSeconds(20), Idle);

        _time.Advance(TimeSpan.FromSeconds(10));
        schedule.TryBegin(out _).Should().BeFalse();
        // A pass is timed from the end of the one before; it may read a moment before the named time.
        _time.Advance(TimeSpan.FromSeconds(9.5));
        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void TryBegin_ShouldReadOnTheNextPass_WhenTheRunnerNamedNoTimeOrAPastOne()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket);
        schedule.Settle(ticket, open: true, next: null, Idle);
        schedule.TryBegin(out ticket).Should().BeTrue();
        schedule.Settle(ticket, open: true, next: TimeSpan.FromSeconds(-3), Idle);
        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void TryBegin_ShouldWaitAtMostTheIdleInterval_WhenTheRunnerNamedALaterTime()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket);
        schedule.Settle(ticket, open: true, next: TimeSpan.FromHours(1), Idle);

        _time.Advance(Idle);

        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void Settle_ShouldKeepTheNextPassDue_WhenWorkWasSubmittedDuringTheRead()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket).Should().BeTrue();

        schedule.Wake(); // the read may have listed the submissions before this one was written
        schedule.Settle(ticket, open: false, next: null, Idle);

        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void Missed_ShouldReadAgainOnTheNextPass_WhileWorkWasUnfinished_AndAfterTheIdleIntervalOtherwise()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket);
        schedule.Missed(ticket, Idle);
        schedule.TryBegin(out ticket).Should().BeTrue("nothing is known yet, so work counts as unfinished");

        schedule.Settle(ticket, open: false, next: null, Idle);
        _time.Advance(Idle);
        schedule.TryBegin(out ticket).Should().BeTrue();
        schedule.Missed(ticket, Idle);

        _time.Advance(TimeSpan.FromSeconds(10));
        schedule.TryBegin(out _).Should().BeFalse();
        _time.Advance(Idle);
        schedule.TryBegin(out _).Should().BeTrue();
    }
}
