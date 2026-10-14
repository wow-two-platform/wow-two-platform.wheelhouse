using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents which icon is wanted: a scope — the product or one of its services — and the theme, form and
/// color to show it in. Defaults ask for the product's symbol on a light surface.</summary>
public sealed record ProductIconRequest
{
    /// <summary>Gets the service whose icon is wanted, or <c>null</c> for the product's own.</summary>
    public string? Service { get; init; }

    /// <summary>Gets the surface the icon is shown on.</summary>
    public IconTheme Theme { get; init; } = IconTheme.Light;

    /// <summary>Gets how much of the identity the icon shows.</summary>
    public IconForm Form { get; init; } = IconForm.Short;

    /// <summary>Gets the ink the icon is drawn in.</summary>
    public IconColor Color { get; init; } = IconColor.Colored;
}
