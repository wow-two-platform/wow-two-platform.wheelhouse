using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Enums;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.Commands;

/// <summary>Represents a command to mark a product active or inactive.</summary>
public sealed record ProductStatusUpdateCommand : ICommand<AppResult<ProductDto>>, IAuditedCommand
{
    /// <summary>Gets the product's identifier or slug.</summary>
    public required string Product { get; init; }

    /// <summary>Gets the product's new status.</summary>
    public required ProductStatus Status { get; init; }

    /// <inheritdoc />
    public string AuditAction => "product.status";

    /// <inheritdoc />
    public string AuditSubject => Product;

    /// <inheritdoc />
    public string? AuditDetail => Status.ToString();
}
