using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Deployments.Fleets.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Fleets.Commands;

/// <summary>Represents a command to promote several products to prod in one run, in the order given, stopping at the
/// first failure. It records the fleet; <see cref="FleetRolloutAdvanceCommand"/> submits its first product.</summary>
public sealed record FleetRolloutStartCommand : ICommand<AppResult<FleetRolloutDto>>, IAuditedCommand
{
    /// <summary>Gets the fleet's identifier, chosen up front so the audit entry names it.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Gets the products to promote, in order: each a release Ready to promote offers for one prod target.</summary>
    public required IReadOnlyList<FleetRolloutItemRequest> Items { get; init; }

    /// <summary>Gets who starts it.</summary>
    public required string Actor { get; init; }

    /// <summary>Gets the owner's one-line why, kept on every deploy; required to override a freeze.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets whether the deploys go through a freeze.</summary>
    public bool OverrideFreeze { get; init; }

    /// <inheritdoc />
    public string AuditAction => "deployment.fleet";

    /// <inheritdoc />
    public string AuditSubject => "portfolio";

    /// <inheritdoc />
    public string AuditDetail => "fleet " + Id + ": " + Items.Count + (Items.Count == 1 ? " product: " : " products: ")
        + string.Join(", ", Items.Select(item => item.Release + " to " + item.Target))
        + (OverrideFreeze ? ", overriding freezes" : "")
        + (string.IsNullOrWhiteSpace(Reason) ? "" : ", reason: " + Reason.Trim());
}
