using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Fleets.Models;
using Wheelhouse.Application.Deployments.Fleets.Queries;
using Wheelhouse.Application.Deployments.Fleets.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Fleets.QueryHandlers;

/// <summary>Handles <see cref="FleetRolloutsQuery"/>.</summary>
public sealed class FleetRolloutsQueryHandler(IFleetRolloutsRepository fleets)
    : IQueryHandler<FleetRolloutsQuery, AppResult<IReadOnlyList<FleetRolloutDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<FleetRolloutDto>>> HandleAsync(FleetRolloutsQuery request, CancellationToken cancellationToken) =>
        AppResult<IReadOnlyList<FleetRolloutDto>>.Ok(
            [.. (await fleets.ListAsync(request.Limit, cancellationToken)).Select(FleetRolloutService.Map)]);
}
