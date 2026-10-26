namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents the release source Wheelhouse derives for a product's repository, as a save would store it: the
/// shared publish workflow's asset and build workflow, and one image per service of the repository's <c>deploy.yml</c>
/// at the branch head.</summary>
public sealed record ProductReleaseSourceDto
{
    /// <summary>Gets the repository, as <c>owner/name</c>.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets the branch whose head was read.</summary>
    public required string Branch { get; init; }

    /// <summary>Gets the asset every published release carries.</summary>
    public required string Asset { get; init; }

    /// <summary>Gets the workflow that builds a commit into a bundle.</summary>
    public string? Workflow { get; init; }

    /// <summary>Gets each service's image repository, in the descriptor's order; empty while it cannot be read.</summary>
    public required IReadOnlyList<ReleaseImageDto> Images { get; init; }

    /// <summary>Gets whether the images are what a readable, valid <c>deploy.yml</c> names; <c>false</c> while it cannot
    /// be read or used, when a save keeps the images the product has.</summary>
    public required bool FromDescriptor { get; init; }

    /// <summary>Gets why the images are missing or what else needs a look, in display-safe words; <c>null</c> when nothing does.</summary>
    public string? Note { get; init; }
}
