namespace Wheelhouse.Application.Deployments.SelfDeploy.Models;

/// <summary>Represents one rollout of this console's own target, read from the host's journal.</summary>
/// <param name="Id">The deployment's identifier.</param>
/// <param name="Release">The release it deployed.</param>
/// <param name="PreviousRelease">The release the target ran before it, when known.</param>
/// <param name="Status">The runner status, such as <c>running</c>, <c>succeeded</c> or <c>rolled_back</c>.</param>
/// <param name="Outcome">What the status amounts to: <c>succeeded</c>, <c>failed</c>, <c>pending</c> or <c>unknown</c>.</param>
/// <param name="Actor">Who submitted it.</param>
/// <param name="Reason">The owner's why, when one was given.</param>
/// <param name="Failure">The display-safe reason it failed or rolled back.</param>
/// <param name="SubmittedAt">When it was submitted.</param>
/// <param name="CompletedAt">When it finished, once it has.</param>
/// <param name="BeforeBoot">Whether it was submitted before this console started: the rollout that started it.</param>
public sealed record SelfDeploymentRunDto(
    string Id, string? Release, string? PreviousRelease, string Status, string Outcome, string? Actor, string? Reason,
    string? Failure, DateTimeOffset SubmittedAt, DateTimeOffset? CompletedAt, bool BeforeBoot);
