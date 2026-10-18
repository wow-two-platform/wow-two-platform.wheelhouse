using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents the release source derived for a product created from the catalog, and why it has no images
/// when it has none.</summary>
/// <param name="Release">The release source: the conventional asset and workflow, and the images found.</param>
/// <param name="Note">Why the images are missing or incomplete, in display-safe words; <c>null</c> when they are complete.</param>
public sealed record DerivedRelease(ProductReleaseValueObject Release, string? Note);
