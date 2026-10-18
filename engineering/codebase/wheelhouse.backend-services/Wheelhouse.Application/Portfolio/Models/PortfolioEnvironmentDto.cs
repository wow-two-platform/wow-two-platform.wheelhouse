namespace Wheelhouse.Application.Portfolio.Models;

/// <summary>Represents one environment of a product: its latest rollout, the release it last ran successfully, and its
/// finished rollouts per week.</summary>
/// <param name="Environment">The environment, lowercase: <c>dev</c> or <c>prod</c>.</param>
/// <param name="LastDeploy">The newest rollout the runner accepted, whatever its outcome; null when there is none.</param>
/// <param name="Running">The newest rollout that succeeded, so the release it serves; null when none did.</param>
/// <param name="Weekly">Finished rollouts per week, aligned with <see cref="PortfolioSummaryDto.Weeks"/>.</param>
public sealed record PortfolioEnvironmentDto(
    string Environment, PortfolioDeployDto? LastDeploy, PortfolioDeployDto? Running, IReadOnlyList<int> Weekly);
