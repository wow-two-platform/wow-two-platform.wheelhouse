using Wheelhouse.Domain.Products.Enums;
using Wheelhouse.Domain.Products.Models;
using WoW.Two.Sdk.Backend.Beta.Data.Abstractions;

namespace Wheelhouse.Domain.Products.Entities;

/// <summary>Represents a portfolio product: its identity, where it stands and where its releases come from.</summary>
public sealed record ProductEntity : IKeyedEntity<Guid>, IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "products";

    /// <summary>Gets or sets the product's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the name targets, bundles, vault namespaces and URLs know the product by; fixed once
    /// the product exists.</summary>
    public required string Slug { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets one line on what the product does.</summary>
    public required string Description { get; set; }

    /// <summary>Gets or sets the GitHub repository that defines the product, as <c>owner/name</c>.</summary>
    public required string Repository { get; set; }

    /// <summary>Gets or sets the branch releases come from.</summary>
    public required string DefaultBranch { get; set; }

    /// <summary>Gets or sets where the product stands in the portfolio.</summary>
    public ProductLifecycle Lifecycle { get; set; } = ProductLifecycle.Building;

    /// <summary>Gets or sets where published releases and commit builds come from, or <c>null</c> when only
    /// hand-imported bundles deploy.</summary>
    public ProductReleaseValueObject? Release { get; set; }

    /// <summary>Gets or sets when the row was created; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row last changed; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
