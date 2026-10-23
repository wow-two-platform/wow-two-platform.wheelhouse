using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents the release source derived for a product created from the catalog, and why it has no images
/// when it has none.</summary>
/// <param name="Release">The release source: the conventional asset and workflow, and the images found.</param>
/// <param name="Note">Why the images are missing or incomplete, in display-safe words; <c>null</c> when they are complete.</param>
/// <param name="FromDescriptor">Whether the images are what a readable, valid <c>deploy.yml</c> names; <c>false</c> when
/// the descriptor could not be read or used, so the images are empty for that reason rather than by the descriptor's
/// say.</param>
public sealed record DerivedRelease(ProductReleaseValueObject Release, string? Note, bool FromDescriptor = true);
