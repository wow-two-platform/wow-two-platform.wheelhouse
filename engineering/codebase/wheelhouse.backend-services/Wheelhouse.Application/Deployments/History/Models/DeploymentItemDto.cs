namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one deployment in a list: what went where, how it went and what it changed.</summary>
public sealed record DeploymentItemDto
{
    /// <summary>Gets the submission's identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the target the release went to.</summary>
    public string? TargetId { get; init; }

    /// <summary>Gets the target's environment; null for a removed target.</summary>
    public string? Environment { get; init; }

    /// <summary>Gets the product's slug.</summary>
    public string? Product { get; init; }

    /// <summary>Gets the product's workspace slug.</summary>
    public string? Workspace { get; init; }

    /// <summary>Gets the release label.</summary>
    public string? Release { get; init; }

    /// <summary>Gets the release bundle's catalog identifier, which a redeploy submits again.</summary>
    public string? BundleId { get; init; }

    /// <summary>Gets <c>release</c> or <c>candidate</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>Gets the branch a commit build came from.</summary>
    public string? Branch { get; init; }

    /// <summary>Gets the release's source commit.</summary>
    public string? SourceCommit { get; init; }

    /// <summary>Gets who submitted it.</summary>
    public string? Actor { get; init; }

    /// <summary>Gets the owner's one-line why for the deploy, when one was given.</summary>
    public string? DeployReason { get; init; }

    /// <summary>Gets the fleet rollout that submitted it; null for a deploy of its own.</summary>
    public string? FleetId { get; init; }

    /// <summary>Gets the runner status.</summary>
    public required string Status { get; init; }

    /// <summary>Gets what the status amounts to: <c>succeeded</c>, <c>failed</c>, <c>refused</c>, <c>pending</c> or
    /// <c>unknown</c>.</summary>
    public required string Outcome { get; init; }

    /// <summary>Gets what the environment runs because of it: <c>in_progress</c>, <c>succeeded</c>, <c>degraded</c>,
    /// <c>rolled_back</c>, <c>partial</c>, <c>failed</c>, <c>not_applied</c> or <c>unknown</c>.</summary>
    public required string State { get; init; }

    /// <summary>Gets which way it got there, such as <c>partly_rolled_back</c> or <c>check_failed</c>.</summary>
    public string? Substate { get; init; }

    /// <summary>Gets how much attention it needs: <c>none</c>, <c>info</c>, <c>look</c>, <c>act</c> or <c>muted</c>.</summary>
    public required string Attention { get; init; }

    /// <summary>Gets why it ended so, with where it started; null for a calm outcome.</summary>
    public DeploymentCauseDto? Cause { get; init; }

    /// <summary>Gets the services its origin likely hurt, along a declared path.</summary>
    public IReadOnlyList<DeploymentEffectDto> Effects { get; init; } = [];

    /// <summary>Gets each service's result in rollout order; null when the target did not report them.</summary>
    public IReadOnlyList<DeploymentServiceResultDto>? Services { get; init; }

    /// <summary>Gets the display-safe reason a rollout failed or was refused.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets what needs a look although the rollout succeeded.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Gets when the release was submitted.</summary>
    public required DateTimeOffset SubmittedAt { get; init; }

    /// <summary>Gets when the rollout started on the target.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Gets when the rollout finished.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Gets the seconds from start (or submission) to completion; null while it runs.</summary>
    public double? DurationSeconds { get; init; }

    /// <summary>Gets the services whose image the rollout changed; null when the bundle is unknown.</summary>
    public IReadOnlyList<string>? ChangedServices { get; init; }

    /// <summary>Gets the step a live rollout is on.</summary>
    public DeploymentStepRecord? CurrentStep { get; init; }

    /// <summary>Gets the watch after a successful rollout; null when it was not watched.</summary>
    public DeploymentWatchDto? Watch { get; init; }

    /// <summary>Gets whether the release declares an end-to-end check for dev; null when the bundle is unknown.</summary>
    public bool? Verifies { get; init; }

    /// <summary>Gets the release's end-to-end check after its dev rollout; null when the rollout ran none.</summary>
    public DeploymentVerificationDto? Verification { get; init; }

    /// <summary>Maps an entry.</summary>
    /// <param name="entry">The joined submission.</param>
    /// <returns>The list item.</returns>
    public static DeploymentItemDto From(DeploymentEntry entry)
    {
        var record = entry.Record;
        var began = record.StartedAt ?? record.SubmittedAt;
        return new DeploymentItemDto
        {
            Id = record.Id,
            TargetId = record.TargetId,
            Environment = entry.Environment,
            Product = entry.Product,
            Workspace = entry.Workspace,
            Release = record.Release,
            BundleId = record.BundleId,
            Kind = record.Kind,
            Branch = record.Branch,
            SourceCommit = record.SourceCommit,
            Actor = record.Actor,
            DeployReason = record.DeployReason,
            FleetId = record.FleetId,
            Status = record.Status,
            Outcome = entry.Outcome,
            State = entry.State.State,
            Substate = entry.State.Substate,
            Attention = entry.State.Attention,
            Cause = entry.State.Cause,
            Effects = entry.State.Effects,
            Services = Versioned(entry.State.Services, entry.Changes),
            Reason = record.Reason,
            Warnings = record.Warnings ?? [],
            SubmittedAt = record.SubmittedAt,
            StartedAt = record.StartedAt,
            CompletedAt = record.CompletedAt,
            DurationSeconds = record.CompletedAt is { } completed && completed >= began
                ? (completed - began).TotalSeconds
                : null,
            ChangedServices = entry.Changes?.Where(change => change.Changed).Select(change => change.Service).ToArray(),
            CurrentStep = entry.Outcome == DeploymentOutcome.Pending ? record.CurrentStep : null,
            Watch = entry.Outcome == DeploymentOutcome.Succeeded ? record.Watch : null,
            Verifies = record.Verifies,
            Verification = record.Verification,
        };
    }

    /// <summary>Fills each service's versions from the comparison with the target's previous success where the runner did
    /// not record them. @internal</summary>
    private static IReadOnlyList<DeploymentServiceResultDto>? Versioned(
        IReadOnlyList<DeploymentServiceResultDto>? services, IReadOnlyList<ServiceChangeDto>? changes) =>
        services is null || changes is null
            ? services
            : [.. services.Select(item => changes.FirstOrDefault(change => change.Service == item.Service) is { } change
                ? item with { From = item.From ?? change.From, To = item.To ?? change.To }
                : item)];
}
