namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents one site a product's <c>deploy.yml</c> publishes.</summary>
/// <param name="Name">The site's name, such as <c>app</c>.</param>
/// <param name="Exposure"><c>public</c>, or <c>private</c> for a site reached over the private network only.</param>
/// <param name="Primary">Whether it is the product's front door: the site marked primary, else the first one.</param>
public sealed record DeployDescriptorSite(string Name, string Exposure, bool Primary);
