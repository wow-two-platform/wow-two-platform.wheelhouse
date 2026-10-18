using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Deployments.Freezes.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Freezes.Commands;

/// <summary>Represents a command to hold deploys: everywhere, to one product, or to one product's environment.</summary>
public sealed record DeployFreezeCreateCommand : ICommand<AppResult<DeployFreezeDto>>, IAuditedCommand
{
    /// <summary>Gets the product's identifier or slug, or null to freeze every product.</summary>
    public string? Product { get; init; }

    /// <summary>Gets <c>dev</c> or <c>prod</c> to freeze one environment of the product, or null for all of them.</summary>
    public string? Environment { get; init; }

    /// <summary>Gets why, as the owner writes it.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets who freezes it.</summary>
    public required string Actor { get; init; }

    /// <inheritdoc />
    public string AuditAction => "deployment.freeze";

    /// <inheritdoc />
    public string AuditSubject => FreezeScope.Subject(Product, Environment);

    /// <inheritdoc />
    public string? AuditDetail => string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();
}
