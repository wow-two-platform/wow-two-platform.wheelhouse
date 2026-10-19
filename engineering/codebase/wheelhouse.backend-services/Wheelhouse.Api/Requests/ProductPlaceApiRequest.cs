using System.ComponentModel.DataAnnotations;

namespace Wheelhouse.Api.Requests;

/// <summary>Carries what to place for a product on a server.</summary>
/// <param name="Server">The server's slug.</param>
/// <param name="Database">Whether to create the product's dev and prod databases.</param>
/// <param name="Sites">The private sites, each with its prod node's name.</param>
public sealed record ProductPlaceApiRequest(
    [Required] string Server, bool Database, [MaxLength(10)] IReadOnlyList<ProductPlaceSiteApiRequest>? Sites);

/// <summary>One private site and its prod node's name; dev's node adds <c>-dev</c>.</summary>
/// <param name="Site">The site's name in the product's manifest.</param>
/// <param name="Node">The prod node's name.</param>
public sealed record ProductPlaceSiteApiRequest([Required] string Site, [Required] string Node);
