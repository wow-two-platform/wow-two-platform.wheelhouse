namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one step of a rollout; its detail is display-safe text.</summary>
/// <param name="Name">The step's name.</param>
/// <param name="Status"><c>running</c>, <c>succeeded</c>, <c>failed</c>, <c>warning</c> or <c>skipped</c>.</param>
/// <param name="StartedAt">When the step started.</param>
/// <param name="CompletedAt">When the step finished.</param>
/// <param name="Detail">What the step did or why it failed.</param>
public sealed record DeploymentStepDto(
    string Name, string Status, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? Detail);
