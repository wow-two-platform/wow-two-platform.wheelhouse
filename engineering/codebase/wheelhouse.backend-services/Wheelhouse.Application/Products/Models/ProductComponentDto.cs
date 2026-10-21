namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents one repository of a product: its primary one, or a further one of a product built from several
/// (decision 70), with its own release source.</summary>
public sealed record ProductComponentDto
{
    /// <summary>Gets the repository's name within the product, as a release set's label names it (<c>studio</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Gets the repository, as <c>owner/name</c>.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets the repository's GitHub address.</summary>
    public required string Url { get; init; }

    /// <summary>Gets where the repository's releases and commit builds come from, or <c>null</c> when it has none.</summary>
    public ProductReleaseDto? Release { get; init; }
}
