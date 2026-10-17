using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Products.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.Commands;

/// <summary>Represents a command to move a product into another workspace.</summary>
public sealed record ProductWorkspaceUpdateCommand : ICommand<AppResult<ProductDto>>, IAuditedCommand
{
    /// <summary>Gets the product's identifier or slug.</summary>
    public required string Product { get; init; }

    /// <summary>Gets the workspace the product moves into.</summary>
    public required Guid WorkspaceId { get; init; }

    /// <inheritdoc />
    public string AuditAction => "product.workspace";

    /// <inheritdoc />
    public string AuditSubject => Product;

    /// <inheritdoc />
    public string? AuditDetail => WorkspaceId.ToString();
}
