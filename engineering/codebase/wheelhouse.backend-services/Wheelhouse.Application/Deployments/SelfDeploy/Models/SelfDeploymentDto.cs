namespace Wheelhouse.Application.Deployments.SelfDeploy.Models;

/// <summary>Represents this console's own deployment as it reads it back: what replaced it, or what is replacing it.</summary>
/// <param name="Environment">The environment this console serves.</param>
/// <param name="Target">This console's own target in the inventory; null when the inventory holds none.</param>
/// <param name="BootedAt">When this console process started.</param>
/// <param name="Latest">The newest rollout on that target; null when there is none.</param>
public sealed record SelfDeploymentDto(string Environment, string? Target, DateTimeOffset BootedAt, SelfDeploymentRunDto? Latest);
