using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Deployments.Fleets.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Fleets.Commands;

/// <summary>Represents a command to cancel a running fleet between products: the product it is on finishes, the rest
/// are not started.</summary>
/// <param name="Id">The fleet.</param>
/// <param name="Actor">Who cancels it.</param>
public sealed record FleetRolloutCancelCommand(Guid Id, string Actor) : ICommand<AppResult<FleetRolloutDto>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "deployment.fleet.cancel";

    /// <inheritdoc />
    public string AuditSubject => "portfolio";

    /// <inheritdoc />
    public string AuditDetail => "fleet " + Id;
}
