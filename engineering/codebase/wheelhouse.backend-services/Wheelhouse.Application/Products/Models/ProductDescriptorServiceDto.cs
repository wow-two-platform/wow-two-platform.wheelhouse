namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents one service a product's <c>deploy.yml</c> declares.</summary>
public sealed record ProductDescriptorServiceDto
{
    /// <summary>Gets the service's key under <c>services:</c>.</summary>
    public required string Service { get; init; }

    /// <summary>Gets the platform services it consumes, such as <c>postgres</c>.</summary>
    public IReadOnlyList<string> Needs { get; init; } = [];
}
