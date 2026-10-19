using Wheelhouse.Application.Deployments.Fleets.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Fleets.Queries;

/// <summary>Lists the newest fleet rollouts with their products, newest first.</summary>
/// <param name="Limit">How many at most, 1-50.</param>
public sealed record FleetRolloutsQuery(int Limit = 10) : IQuery<AppResult<IReadOnlyList<FleetRolloutDto>>>;
