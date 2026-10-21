namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents what to place for a product on a server: its dev and prod databases, its private sites, or both,
/// with what its <c>deploy.yml</c> declares, so the runner checks the request against it too.</summary>
/// <param name="Product">The product's slug.</param>
/// <param name="Server">The server's slug.</param>
/// <param name="Database">Whether to create the product's dev and prod databases.</param>
/// <param name="Sites">The private sites, each with its tailnet node name; dev's node adds <c>-dev</c>.</param>
/// <param name="Descriptor">The product's descriptor as read for this placement.</param>
public sealed record ProductPlacementRequest(
    string Product, string Server, bool Database, IReadOnlyList<ProductPlacementSite> Sites, ProductDescriptorDto Descriptor);
