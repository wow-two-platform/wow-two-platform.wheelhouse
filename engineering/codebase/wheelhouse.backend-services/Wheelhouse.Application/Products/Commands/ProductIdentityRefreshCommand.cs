using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Products.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.Commands;

/// <summary>Represents a command to read a product's manifest again now: the branch head is checked without waiting for
/// the remembered one to age, and a head the cache has not seen re-reads the manifest at that commit.</summary>
public sealed record ProductIdentityRefreshCommand : ICommand<AppResult<ProductDto>>, IAuditedCommand
{
    /// <summary>Gets the slug of the product to refresh.</summary>
    public required string Slug { get; init; }

    /// <inheritdoc />
    public string AuditAction => "product.refresh";

    /// <inheritdoc />
    public string AuditSubject => Slug;
}
