using Wheelhouse.Application.Deployments.Fleets.Commands;
using Wheelhouse.Application.Deployments.Fleets.Models;
using Wheelhouse.Application.Deployments.Fleets.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Fleets.CommandHandlers;

/// <summary>Handles <see cref="FleetRolloutCancelCommand"/>.</summary>
public sealed class FleetRolloutCancelCommandHandler(FleetRolloutService fleets)
    : ICommandHandler<FleetRolloutCancelCommand, AppResult<FleetRolloutDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<FleetRolloutDto>> HandleAsync(FleetRolloutCancelCommand request, CancellationToken cancellationToken) =>
        await fleets.CancelAsync(request.Id, request.Actor, cancellationToken);
}
