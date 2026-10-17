using Wheelhouse.Application.Deployments.History.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.Queries;

/// <summary>Lists the live rollouts of the filter's entities with the step each is on, newest first.</summary>
/// <param name="Filter">Which deployments match; only its entities apply.</param>
public sealed record DeploymentRunningQuery(DeploymentFilter Filter) : IQuery<AppResult<IReadOnlyList<DeploymentItemDto>>>;
