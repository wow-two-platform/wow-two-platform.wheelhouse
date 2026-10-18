using Wheelhouse.Application.Deployments.History.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.Queries;

/// <summary>Lists the inventory's targets that need attention: a latest rollout that failed or rolled back with no
/// success since, or a rollout live for too long. Those that likely need a reconcile come first, then newest first.</summary>
/// <param name="Filter">Which deployments count; only its entities apply.</param>
public sealed record DeploymentIssuesQuery(DeploymentFilter Filter) : IQuery<AppResult<IReadOnlyList<DeploymentIssueDto>>>;
