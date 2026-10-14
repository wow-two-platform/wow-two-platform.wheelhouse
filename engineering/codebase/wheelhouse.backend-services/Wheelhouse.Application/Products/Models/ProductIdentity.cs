using Wheelhouse.Domain.Products.Enums;
using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents what a product is called and described as: the cached manifest of its repository when it has a
/// valid one, otherwise the product's own recorded values.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Description">One line on what the product does.</param>
/// <param name="Homepage">The public address, or <c>null</c> when the manifest gives none or there is no manifest.</param>
/// <param name="Source">Where the name, description and homepage come from.</param>
/// <param name="Commit">The commit the manifest was read from, or <c>null</c> when it is not the source.</param>
/// <param name="Problem">Why the repository's newest manifest is ignored, or <c>null</c> when nothing is.</param>
/// <param name="Services">The name and description of each service that has its own.</param>
public sealed record ProductIdentity(
    string Name,
    string Description,
    string? Homepage,
    ProductIdentitySource Source,
    string? Commit,
    string? Problem,
    IReadOnlyList<ProductServiceIdentityValueObject> Services);
