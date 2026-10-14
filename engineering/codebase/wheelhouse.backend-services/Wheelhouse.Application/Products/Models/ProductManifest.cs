using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents a valid product manifest: the identity text a product's repository gives it.</summary>
/// <param name="Name">The product's display name.</param>
/// <param name="Description">One line on what the product does.</param>
/// <param name="Homepage">The product's public address, or <c>null</c> when the manifest gives none.</param>
/// <param name="Services">The name and description of each service that has its own, in key order.</param>
public sealed record ProductManifest(
    string Name,
    string Description,
    string? Homepage,
    IReadOnlyList<ProductServiceIdentityValueObject> Services);
