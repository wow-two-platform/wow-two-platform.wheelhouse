namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents the parts of a product's <c>deploy.yml</c> Wheelhouse reads: the product slug and each service's
/// key and own image repository, in file order.</summary>
/// <param name="Product">The <c>product</c> the descriptor names.</param>
/// <param name="Services">Each service's key and its <c>image</c>, when it names one.</param>
public sealed record DeployDescriptorModel(string Product, IReadOnlyList<(string Service, string? Image)> Services);
