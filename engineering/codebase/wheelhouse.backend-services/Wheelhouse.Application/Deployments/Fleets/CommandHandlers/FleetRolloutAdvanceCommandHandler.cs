using Wheelhouse.Application.Deployments.Fleets.Commands;
using Wheelhouse.Application.Deployments.Fleets.Models;
using Wheelhouse.Application.Deployments.Fleets.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Fleets.CommandHandlers;

/// <summary>Handles <see cref="FleetRolloutAdvanceCommand"/>.</summary>
public sealed class FleetRolloutAdvanceCommandHandler(FleetRolloutService fleets)
    : ICommandHandler<FleetRolloutAdvanceCommand, AppResult<FleetRolloutDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<FleetRolloutDto>> HandleAsync(FleetRolloutAdvanceCommand request, CancellationToken cancellationToken) =>
        await fleets.AdvanceAsync(request.Id, cancellationToken);
}
