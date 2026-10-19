namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents one private site to place and the tailnet node that serves it.</summary>
/// <param name="Site">The site's name in the product's manifest, such as <c>app</c>.</param>
/// <param name="Node">The prod node's name, such as <c>ocharo-desk</c>; dev's is <c>ocharo-desk-dev</c>.</param>
public sealed record ProductPlacementSite(string Site, string Node);
