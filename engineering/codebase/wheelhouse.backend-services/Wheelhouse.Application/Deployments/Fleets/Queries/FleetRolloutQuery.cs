using Wheelhouse.Application.Deployments.Fleets.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Fleets.Queries;

/// <summary>Reads one fleet rollout with its products.</summary>
/// <param name="Id">The fleet.</param>
public sealed record FleetRolloutQuery(Guid Id) : IQuery<AppResult<FleetRolloutDto>>;
