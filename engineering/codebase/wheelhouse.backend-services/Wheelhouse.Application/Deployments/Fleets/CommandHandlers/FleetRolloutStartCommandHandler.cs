using Wheelhouse.Application.Deployments.Fleets.Commands;
using Wheelhouse.Application.Deployments.Fleets.Models;
using Wheelhouse.Application.Deployments.Fleets.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Fleets.CommandHandlers;

/// <summary>Handles <see cref="FleetRolloutStartCommand"/>: records the fleet with every product waiting.</summary>
public sealed class FleetRolloutStartCommandHandler(FleetRolloutService fleets)
    : ICommandHandler<FleetRolloutStartCommand, AppResult<FleetRolloutDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<FleetRolloutDto>> HandleAsync(FleetRolloutStartCommand request, CancellationToken cancellationToken) =>
        await fleets.CreateAsync(request, cancellationToken);
}
