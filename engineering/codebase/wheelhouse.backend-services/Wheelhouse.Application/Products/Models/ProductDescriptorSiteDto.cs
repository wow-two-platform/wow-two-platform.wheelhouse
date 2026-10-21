namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents one site a product's <c>deploy.yml</c> publishes.</summary>
public sealed record ProductDescriptorSiteDto
{
    /// <summary>Gets the site's name, such as <c>app</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets <c>public</c>, or <c>private</c> for a site reached over the private network only, which a
    /// placement gives a tailnet node per environment.</summary>
    public required string Exposure { get; init; }

    /// <summary>Gets whether it is the product's front door, whose baseline host carries no site name.</summary>
    public bool Primary { get; init; }
}
