namespace Wheelhouse.Domain.Products.Models;

/// <summary>Represents the image repository one of a product's services publishes to.</summary>
public sealed record ReleaseImageValueObject
{
    /// <summary>Gets the service's name in the product's Compose definition.</summary>
    public required string Service { get; init; }

    /// <summary>Gets the image repository, such as <c>ghcr.io/owner/product/api</c>.</summary>
    public required string Image { get; init; }
}
