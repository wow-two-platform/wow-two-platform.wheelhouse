namespace Wheelhouse.Domain.Products.Models;

/// <summary>Represents where a product's published releases and commit builds come from.</summary>
public sealed record ProductReleaseValueObject
{
    /// <summary>Gets the asset every published GitHub release carries, such as <c>foreverpin-release.tar.gz</c>.</summary>
    public required string Asset { get; init; }

    /// <summary>Gets the workflow that builds a commit into a bundle, or <c>null</c> when only releases deploy.</summary>
    public string? Workflow { get; init; }

    /// <summary>Gets each service's image repository; a bundle's digests must come from exactly these.</summary>
    public required IReadOnlyList<ReleaseImageValueObject> Images { get; init; }
}
