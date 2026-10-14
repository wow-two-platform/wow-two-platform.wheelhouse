namespace Wheelhouse.Domain.Products.Models;

/// <summary>Represents the name and description a product manifest gives one of the product's services.</summary>
public sealed record ProductServiceIdentityValueObject
{
    /// <summary>Gets the service's key under <c>services:</c> in the product's <c>deploy.yml</c>.</summary>
    public required string Service { get; init; }

    /// <summary>Gets the service's display name, or <c>null</c> when the manifest gives none.</summary>
    public string? Name { get; init; }

    /// <summary>Gets one line on what the service does, or <c>null</c> when the manifest gives none.</summary>
    public string? Description { get; init; }
}
