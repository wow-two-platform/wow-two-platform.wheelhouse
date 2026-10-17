using Wheelhouse.Application.Deployments.History.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.Queries;

/// <summary>Lists, per product and prod target, the newest release that succeeded on dev and is not what prod runs.</summary>
/// <param name="Filter">Which products count; only its workspaces and products apply.</param>
public sealed record DeploymentPromotionsQuery(DeploymentFilter Filter)
    : IQuery<AppResult<IReadOnlyList<DeploymentPromotionDto>>>;
