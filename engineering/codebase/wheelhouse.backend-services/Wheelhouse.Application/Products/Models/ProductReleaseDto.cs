namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents where a product's published releases and commit builds come from.</summary>
public sealed record ProductReleaseDto
{
    /// <summary>Gets the asset every published GitHub release carries.</summary>
    public required string Asset { get; init; }

    /// <summary>Gets the workflow that builds a commit into a bundle, or <c>null</c> when only releases deploy.</summary>
    public string? Workflow { get; init; }

    /// <summary>Gets each service's image repository.</summary>
    public required IReadOnlyList<ReleaseImageDto> Images { get; init; }
}
