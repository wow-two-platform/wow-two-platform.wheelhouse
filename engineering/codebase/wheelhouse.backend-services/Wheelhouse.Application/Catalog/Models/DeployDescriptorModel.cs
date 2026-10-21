namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents the parts of a product's <c>deploy.yml</c> Wheelhouse reads: the product slug, each service's
/// key, own image repository and platform needs, in file order, and the sites the product publishes.</summary>
/// <param name="Product">The <c>product</c> the descriptor names.</param>
/// <param name="Services">Each service, in file order.</param>
/// <param name="Sites">Each site the product publishes, by name.</param>
public sealed record DeployDescriptorModel(
    string Product, IReadOnlyList<DeployDescriptorService> Services, IReadOnlyList<DeployDescriptorSite> Sites);
