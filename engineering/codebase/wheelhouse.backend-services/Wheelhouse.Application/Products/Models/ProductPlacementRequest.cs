namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents what to place for a product on a server: its dev and prod databases, its private sites, or both.</summary>
/// <param name="Product">The product's slug.</param>
/// <param name="Server">The server's slug.</param>
/// <param name="Database">Whether to create the product's dev and prod databases.</param>
/// <param name="Sites">The private sites, each with its tailnet node name; dev's node adds <c>-dev</c>.</param>
public sealed record ProductPlacementRequest(
    string Product, string Server, bool Database, IReadOnlyList<ProductPlacementSite> Sites);
