namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents the step a live rollout is on.</summary>
/// <param name="Name">The step's name, such as <c>Pull images</c>.</param>
/// <param name="Status">The step's status, usually <c>running</c>.</param>
/// <param name="StartedAt">When the step started.</param>
/// <param name="Index">The step's position in the rollout, from 1.</param>
public sealed record DeploymentStepRecord(string Name, string? Status, DateTimeOffset? StartedAt, int? Index);
