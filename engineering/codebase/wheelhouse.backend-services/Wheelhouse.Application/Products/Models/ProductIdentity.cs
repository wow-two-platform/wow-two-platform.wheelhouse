using Wheelhouse.Domain.Products.Enums;
using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents what a product is called and described as: the cached identity its repository declares when it
/// declares a valid one, otherwise the product's own recorded values.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Description">One line on what the product does.</param>
/// <param name="Homepage">The public address, or <c>null</c> when the repository gives none or declares no identity.</param>
/// <param name="Source">Where the name, description and homepage come from.</param>
/// <param name="Commit">The commit the identity was read from, or <c>null</c> when it is not the source.</param>
/// <param name="Problem">Why the repository's newest identity is ignored, as a sentence naming the file, or
/// <c>null</c> when nothing is.</param>
/// <param name="Services">The name and description of each service that has its own.</param>
/// <param name="File">The repository file the identity was read from, or <c>null</c> when it is not the source.</param>
/// <param name="Notice">What the owner should change in the repository, such as moving a retired
/// <c>product/product.json</c> into <c>deploy.yml</c>, or <c>null</c> when nothing.</param>
public sealed record ProductIdentity(
    string Name,
    string Description,
    string? Homepage,
    ProductIdentitySource Source,
    string? Commit,
    string? Problem,
    IReadOnlyList<ProductServiceIdentityValueObject> Services,
    string? File = null,
    string? Notice = null);
