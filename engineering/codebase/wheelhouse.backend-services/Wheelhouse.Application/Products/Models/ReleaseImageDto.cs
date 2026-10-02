namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents the image repository one of a product's services publishes to.</summary>
public sealed record ReleaseImageDto
{
    /// <summary>Gets the service's name.</summary>
    public required string Service { get; init; }

    /// <summary>Gets the image repository.</summary>
    public required string Image { get; init; }
}
