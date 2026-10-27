using Wheelhouse.Domain.Products.Models;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Products.Entities;

/// <summary>Represents the cached copy of a product's manifest — the identity text its repository declares in
/// <c>deploy.yml</c>, or in a retired <c>product/product.json</c> — and the commit and file it was read from. A
/// projection, never an edit target: it is replaced only by a copy read at a newer commit and dropped when the
/// repository stops declaring an identity. One row per product.</summary>
public sealed class ProductManifestEntity : IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "product_manifests";

    /// <summary>Gets or sets the product the manifest describes; the row's key.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the repository the manifest was read from, as <c>owner/name</c>. A copy read from another
    /// repository than the product's now names is not the product's.</summary>
    public required string Repository { get; set; }

    /// <summary>Gets or sets the branch the commit belongs to.</summary>
    public required string Branch { get; set; }

    /// <summary>Gets or sets the full SHA of the commit the manifest was read from.</summary>
    public required string Commit { get; set; }

    /// <summary>Gets or sets the repository file the manifest was read from: <c>engineering/deployment/deploy.yml</c>,
    /// or the retired <c>product/product.json</c> while the descriptor declares no identity.</summary>
    public required string SourceFile { get; set; }

    /// <summary>Gets or sets the product's display name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets one line on what the product does.</summary>
    public required string Description { get; set; }

    /// <summary>Gets or sets the product's public address, or <c>null</c> when the manifest gives none.</summary>
    public string? Homepage { get; set; }

    /// <summary>Gets or sets the name and description of each service that has its own, in key order.</summary>
    public IReadOnlyList<ProductServiceIdentityValueObject> Services { get; set; } = [];

    /// <summary>Gets or sets when the row was created; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row last changed; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
