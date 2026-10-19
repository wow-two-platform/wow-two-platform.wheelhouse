using Wheelhouse.Application.Deployments.Fleets.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Fleets.Commands;

/// <summary>Represents a command to move a running fleet on: record its current product's outcome and submit the next.
/// The deploys it submits are audited on their own.</summary>
/// <param name="Id">The fleet.</param>
public sealed record FleetRolloutAdvanceCommand(Guid Id) : ICommand<AppResult<FleetRolloutDto>>;
