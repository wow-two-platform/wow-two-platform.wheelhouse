using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Enums;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.Commands;

/// <summary>Represents a command to mark one of a product's services active or inactive, in every environment.</summary>
public sealed record ServiceStatusUpdateCommand : ICommand<AppResult<ProductDto>>, IAuditedCommand
{
    /// <summary>Gets the product's identifier or slug.</summary>
    public required string Product { get; init; }

    /// <summary>Gets the service's key under <c>services:</c> in the product's <c>deploy.yml</c>.</summary>
    public required string Service { get; init; }

    /// <summary>Gets the service's new status.</summary>
    public required ProductStatus Status { get; init; }

    /// <inheritdoc />
    public string AuditAction => "service.status";

    /// <inheritdoc />
    public string AuditSubject => Product + "/" + Service;

    /// <inheritdoc />
    public string? AuditDetail => Status.ToString();
}
