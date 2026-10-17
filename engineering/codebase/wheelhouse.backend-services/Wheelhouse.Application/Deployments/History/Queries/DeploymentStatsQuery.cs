using Wheelhouse.Application.Deployments.History.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.Queries;

/// <summary>Measures the filter's entities over its range (the last 30 days without one) beside the range before it;
/// the status filter does not apply, since the measures count every outcome.</summary>
/// <param name="Filter">Which deployments are measured.</param>
public sealed record DeploymentStatsQuery(DeploymentFilter Filter) : IQuery<AppResult<DeploymentStatsDto>>;
