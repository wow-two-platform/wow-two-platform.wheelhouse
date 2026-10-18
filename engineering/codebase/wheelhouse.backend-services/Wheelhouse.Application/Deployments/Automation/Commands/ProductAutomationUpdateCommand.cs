using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Deployments.Automation.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Automation.Commands;

/// <summary>Represents a command to change what the control plane deploys of a product on its own; a null choice stays
/// as it is.</summary>
public sealed record ProductAutomationUpdateCommand : ICommand<AppResult<ProductAutomationDto>>, IAuditedCommand
{
    /// <summary>Gets the product's identifier or slug.</summary>
    public required string Product { get; init; }

    /// <summary>Gets whether each new release deploys to dev, or null to keep it.</summary>
    public bool? AutoDeployDev { get; init; }

    /// <summary>Gets whether a release that passed its dev watch is promoted to prod, or null to keep it.</summary>
    public bool? AutoPromote { get; init; }

    /// <inheritdoc />
    public string AuditAction => "product.automation";

    /// <inheritdoc />
    public string AuditSubject => Product;

    /// <inheritdoc />
    public string? AuditDetail => string.Join(", ", new[]
    {
        AutoDeployDev is { } dev ? "auto-deploy to dev " + (dev ? "on" : "off") : null,
        AutoPromote is { } promote ? "auto-promote to prod " + (promote ? "on" : "off") : null,
    }.OfType<string>());
}
