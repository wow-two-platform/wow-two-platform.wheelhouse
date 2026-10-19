using AwesomeAssertions;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Services;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Deployments.Enums;

namespace Wheelhouse.Tests.Unit.Deployments;

/// <summary>
/// Tests for <see cref="DeploymentStateRules"/>: each runner status and intent reads into one of the eight states with
/// its sub-state and attention; Partial needs services on both releases; origins and effects follow declared paths and
/// timing only.
/// </summary>
public sealed class DeploymentStateRulesTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-18T23:40:00Z");

    [Theory]
    [InlineData("queued", null, "in_progress", "queued", "info")]
    [InlineData("running", "Pull images", "in_progress", "checking", "info")]
    [InlineData("running", "Start desk, jobs", "in_progress", "starting", "info")]
    [InlineData("running", "Roll back to v0.8.4", "in_progress", "rolling_back", "info")]
    [InlineData("rejected", null, "not_applied", "refused", "none")]
    [InlineData("unknown", null, "unknown", "no_receipt", "muted")]
    [InlineData("garbled", null, "unknown", "unreadable", "muted")]
    public void Of_ShouldReadEachRunnerStatus(string status, string? step, string state, string substate, string attention)
    {
        var reading = DeploymentStateRules.Of(Record(status) with
        {
            CurrentStep = step is null ? null : new DeploymentStepRecord(step, "running", At, 3),
        }, null, null);
        (reading.State, reading.Substate, reading.Attention).Should().Be((state, substate, attention));
    }

    [Fact]
    public void Of_ShouldReadASuccessByItsWatch_AndDegradeOnWarningsOrARegression()
    {
        Read(Record("succeeded"), Watch("watching")).Should().Be(("succeeded", "watching", "none"));
        Read(Record("succeeded"), Watch("superseded")).Should().Be(("succeeded", "replaced", "none"));
        Read(Record("succeeded")).Should().Be(("succeeded", null, "none"));
        Read(Record("succeeded") with { Warnings = ["Site app did not answer through the ingress: timed out"] })
            .Should().Be(("degraded", "at_deploy", "look"));

        var regressed = DeploymentStateRules.Of(Record("succeeded") with { ServiceRollout = Rollout(("worker", "updated", "updated", [], null, null)) },
            Watch("degraded", "worker restarted 2 times since the deploy"), null);
        (regressed.State, regressed.Substate, regressed.Attention).Should().Be(("degraded", "in_watch", "look"));
        (regressed.Cause!.Kind, regressed.Cause.Origin).Should().Be(("service", "worker"));
        regressed.Services!.Single().Should().Be(new DeploymentServiceResultDto("worker", "affected", true));
    }

    [Fact]
    public void Of_ShouldCallARegressionOfOnlyUntouchedServicesASideEffect()
    {
        var reading = DeploymentStateRules.Of(Record("succeeded") with
        {
            ServiceRollout = Rollout(("api", "updated", "updated", [], null, null), ("worker", "unchanged", "unchanged", [], null, null)),
        }, Watch("degraded", "worker is unhealthy"), null);
        (reading.State, reading.Substate).Should().Be(("degraded", "side_effect"));
    }

    [Fact]
    public void Of_ShouldReadAFailureBeforeChangesAsNotApplied_AndAfterThemByWhatRuns()
    {
        Read(Record("failed") with { Reason = "Snapshot failed: disk full" }).Should().Be(("not_applied", "check_failed", "look"));
        Read(Record("failed") with { MutationStarted = true }).Should().Be(("failed", "rollback_not_allowed", "act"));
        DeploymentStateRules.Of(Record("failed") with { MutationStarted = true }, null, null, firstDeploy: true)
            .Substate.Should().Be("first_deploy_unhealthy");

        var partial = DeploymentStateRules.Of(Record("failed") with
        {
            MutationStarted = true,
            ServiceRollout = Rollout(("desk", "updated", "updated", [], null, null), ("jobs", "updated", "failed", [], null, null),
                ("worker", "updated", "not_started", ["jobs"], null, null)),
        }, null, null);
        (partial.State, partial.Substate, partial.Attention).Should().Be(("partial", "stopped_mid_rollout", "act"));
        partial.Services!.Select(item => (item.Service, item.Result, item.Origin))
            .Should().Equal(("desk", "updated", false), ("jobs", "failed", true), ("worker", "not_reached", false));
        (partial.Cause!.Kind, partial.Cause.Origin).Should().Be(("service", "jobs"));
    }

    [Fact]
    public void Of_ShouldReadRollBacks_WholeOrPartly()
    {
        Read(Record("rolled_back")).Should().Be(("rolled_back", "automatic", "look"));
        Read(Record("rolled_back") with
        {
            ServiceRollout = Rollout(("desk", "updated", "rolled_back", [], null, null), ("jobs", "updated", "rolled_back", [], null, null)),
        }).Should().Be(("rolled_back", "together", "look"));
        Read(Record("rollback_failed")).Should().Be(("failed", "rollback_failed", "act"));
        Read(Record("rollback_failed") with
        {
            ServiceRollout = Rollout(("desk", "updated", "rolled_back", [], null, null), ("jobs", "updated", "failed", [], null, null)),
        }).Should().Be(("partial", "partly_rolled_back", "act"));
    }

    [Fact]
    public void Of_ShouldReadAnInterruptionByWhetherItChangedAnything()
    {
        Read(Record("interrupted") with { MutationStarted = true }).Should().Be(("failed", "interrupted", "act"));
        Read(Record("interrupted")).Should().Be(("not_applied", "interrupted", "look"));
    }

    [Fact]
    public void Of_ShouldNameTheOriginFromTheRunnersReason_WhenNoServiceReported()
    {
        var reading = DeploymentStateRules.Of(Record("failed") with
        {
            MutationStarted = true, Reason = "Compose up failed; not healthy: jobs (unhealthy), image-worker (restarting)",
        }, null, null);
        (reading.Cause!.Kind, reading.Cause.Origin).Should().Be(("service", "jobs"));
    }

    [Fact]
    public void Of_ShouldDrawAnEffectOnlyAlongADeclaredPathAndAfterTheOrigin()
    {
        var early = At.AddSeconds(10);
        var late = At.AddSeconds(40);
        var traced = DeploymentStateRules.Of(Record("failed") with
        {
            MutationStarted = true,
            ServiceRollout = Rollout(("jobs", "updated", "failed", [], "unhealthy", early),
                ("image-worker", "unchanged", "unchanged", ["jobs"], "restarting", late)),
        }, null, null);
        traced.Services!.Select(item => (item.Service, item.Result, item.Origin))
            .Should().Equal(("jobs", "failed", true), ("image-worker", "affected", false));
        traced.Effects.Should().Equal(new DeploymentEffectDto("image-worker", "restarting", "jobs", late));

        // Neither waits for the other: two origins, no edge.
        var apart = DeploymentStateRules.Of(Record("failed") with
        {
            MutationStarted = true,
            ServiceRollout = Rollout(("api", "updated", "failed", [], "unhealthy", early), ("mailer", "updated", "failed", [], "unhealthy", late)),
        }, null, null);
        apart.Services!.Where(item => item.Origin).Select(item => item.Service).Should().Equal("api", "mailer");
        apart.Effects.Should().BeEmpty();

        // A declared path but no timing: never a guessed edge.
        var untimed = DeploymentStateRules.Of(Record("failed") with
        {
            MutationStarted = true,
            ServiceRollout = Rollout(("jobs", "updated", "failed", [], null, null), ("worker", "updated", "failed", ["jobs"], null, null)),
        }, null, null);
        untimed.Effects.Should().BeEmpty();
        untimed.Services!.Count(item => item.Origin).Should().Be(2);
    }

    [Fact]
    public void Of_ShouldOrderServicesByWave_ThenTheOnesLeftAlone()
    {
        var rollout = Rollout(("web", "unchanged", "unchanged", [], null, null), ("api", "updated", "updated", ["db"], null, null),
            ("db", "updated", "updated", [], null, null)) with { Waves = [["db"], ["api"]] };
        DeploymentStateRules.Of(Record("succeeded") with { ServiceRollout = rollout }, null, null).Services!
            .Select(item => item.Service).Should().Equal("db", "api", "web");
    }

    [Fact]
    public void Of_ShouldTakeAnUnknownHealthAsNothingWrong_AndCarryWhyEachServiceChanged()
    {
        var rollout = new ServiceRolloutDto(false, [], [],
        [
            new ServiceRolloutItemDto("api", "updated", "updated", [], "0.9.0", "1.0.0", "definition", "unknown"),
            new ServiceRolloutItemDto("web", "unchanged", "unchanged", [], "1.0.0", "1.0.0", null, "unhealthy"),
        ]);
        var reading = DeploymentStateRules.Of(Record("succeeded") with { ServiceRollout = rollout }, null, null);
        reading.Services!.Select(item => (item.Service, item.Result, item.Reason))
            .Should().Equal(("api", "updated", "definition"), ("web", "affected", (string?)null));
        (reading.State, reading.Substate).Should().Be(("degraded", "at_deploy"));
    }

    [Theory]
    [InlineData(DeploymentIntentOutcome.Refused, DeploymentRefusal.Freeze, "refused", "none", "freeze")]
    [InlineData(DeploymentIntentOutcome.Refused, DeploymentRefusal.Gate, "refused", "none", "gate")]
    [InlineData(DeploymentIntentOutcome.Superseded, null, "superseded", "none", "platform")]
    [InlineData(DeploymentIntentOutcome.Cancelled, null, "cancelled", "none", "operator")]
    [InlineData(DeploymentIntentOutcome.Expired, null, "expired", "look", "platform")]
    [InlineData(DeploymentIntentOutcome.Aborted, null, "aborted", "look", "busy")]
    public void Of_ShouldReadAnIntentAsNotApplied(DeploymentIntentOutcome outcome, DeploymentRefusal? refusal, string substate, string attention, string kind)
    {
        var intent = new DeploymentIntentEntity
        {
            Id = Guid.NewGuid(), Target = "pilot-prod", Actor = "max", Outcome = outcome, Refusal = refusal, ReplacedBy = "v1.8.0", At = At,
        };
        var reading = DeploymentStateRules.Of(DeploymentHistoryService.FromIntent(intent), null, intent);
        (reading.State, reading.Substate, reading.Attention, reading.Cause!.Kind).Should().Be(("not_applied", substate, attention, kind));
        if (outcome == DeploymentIntentOutcome.Superseded)
            reading.Cause.Message.Should().Be("Replaced by v1.8.0 while queued.");
    }

    private static (string, string?, string) Read(DeploymentRecord record, DeploymentWatchDto? watch = null)
    {
        var reading = DeploymentStateRules.Of(record, watch ?? record.Watch, null);
        return (reading.State, reading.Substate, reading.Attention);
    }

    private static DeploymentRecord Record(string status) =>
        new() { Id = Guid.NewGuid().ToString(), Status = status, SubmittedAt = At, CompletedAt = At.AddMinutes(3) };

    private static DeploymentWatchDto Watch(string status, params string[] findings) =>
        new() { Status = status, Findings = findings, DegradedAt = findings.Length > 0 ? At.AddMinutes(5) : null };

    private static ServiceRolloutDto Rollout(
        params (string Name, string Change, string Outcome, string[] DependsOn, string? Health, DateTimeOffset? At)[] services) =>
        new(false, [], [],
        [
            .. services.Select(service => new ServiceRolloutItemDto(service.Name, service.Change, service.Outcome, service.DependsOn,
                Health: service.Health, At: service.At)),
        ]);
}
