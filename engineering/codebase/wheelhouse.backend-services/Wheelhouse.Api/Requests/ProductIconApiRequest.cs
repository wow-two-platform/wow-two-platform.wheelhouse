using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Api.Requests;

/// <summary>Represents the query of a request for a product's or service's icon; absent values ask for the symbol, in
/// its brand colors, for a light surface.</summary>
public sealed record ProductIconApiRequest
{
    /// <summary>Gets the surface the icon is shown on: <c>light</c> or <c>dark</c>.</summary>
    public IconTheme Theme { get; init; } = IconTheme.Light;

    /// <summary>Gets how much of the identity the icon shows: <c>full</c> (symbol and wordmark) or <c>short</c> (symbol).</summary>
    public IconForm Form { get; init; } = IconForm.Short;

    /// <summary>Gets the ink the icon is drawn in: <c>colored</c> or <c>monochrome</c>.</summary>
    public IconColor Color { get; init; } = IconColor.Colored;
}
