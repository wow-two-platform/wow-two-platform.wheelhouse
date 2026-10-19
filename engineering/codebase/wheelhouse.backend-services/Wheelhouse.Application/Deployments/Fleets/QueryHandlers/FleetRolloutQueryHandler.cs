using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Fleets.Models;
using Wheelhouse.Application.Deployments.Fleets.Queries;
using Wheelhouse.Application.Deployments.Fleets.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Fleets.QueryHandlers;

/// <summary>Handles <see cref="FleetRolloutQuery"/>.</summary>
public sealed class FleetRolloutQueryHandler(IFleetRolloutsRepository fleets)
    : IQueryHandler<FleetRolloutQuery, AppResult<FleetRolloutDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<FleetRolloutDto>> HandleAsync(FleetRolloutQuery request, CancellationToken cancellationToken) =>
        await fleets.GetAsync(request.Id, cancellationToken) is { } fleet
            ? AppResult<FleetRolloutDto>.Ok(FleetRolloutService.Map(fleet))
            : AppResult<FleetRolloutDto>.Fail(AppErrorFactory.NotFound($"Fleet rollout '{request.Id}' was not found."));
}
