namespace Wheelhouse.Application.Portfolio.Models;

/// <summary>Represents one environment of a product: its latest rollout, the release it last ran successfully, and its
/// finished rollouts per week.</summary>
/// <param name="Environment">The environment, lowercase: <c>dev</c> or <c>prod</c>.</param>
/// <param name="LastDeploy">The newest rollout that changed or tried to change the environment, whatever its outcome;
/// null when there is none.</param>
/// <param name="Running">The newest rollout that succeeded, so the release it serves; null when none did.</param>
/// <param name="Weekly">Finished rollouts per week, aligned with <see cref="PortfolioSummaryDto.Weeks"/>.</param>
/// <param name="LastAttempt">The newest deploy that changed nothing and needs a look (a failed check, an expired
/// schedule), when it is newer than <paramref name="LastDeploy"/>; it marks the pill's corner. Refused, cancelled and
/// superseded deploys never do.</param>
/// <param name="Stopped">What the owner keeps stopped there — the whole environment or some services — so the pill reads
/// stopped rather than failed; null while nothing is.</param>
public sealed record PortfolioEnvironmentDto(
    string Environment, PortfolioDeployDto? LastDeploy, PortfolioDeployDto? Running, IReadOnlyList<int> Weekly,
    PortfolioDeployDto? LastAttempt = null, Deployments.History.Models.TargetStopDto? Stopped = null);
