using Wheelhouse.Application.Deployments.History.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.Queries;

/// <summary>Counts matching finished rollouts per calendar day in the operator's time zone; without a range, the last
/// six months.</summary>
/// <param name="Filter">Which deployments match.</param>
/// <param name="TimeZone">The IANA time zone days are counted in.</param>
public sealed record DeploymentActivityQuery(DeploymentFilter Filter, string TimeZone)
    : IQuery<AppResult<DeploymentActivityDto>>;
