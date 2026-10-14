namespace Wheelhouse.Application.Abstractions;

/// <summary>Represents the outcome of reading a product's manifest file at one commit.</summary>
/// <param name="Status">What the read gave.</param>
/// <param name="Content">The file's bytes when <paramref name="Status"/> is <see cref="ProductManifestFileStatus.Found"/>.</param>
public sealed record ProductManifestFile(ProductManifestFileStatus Status, byte[]? Content = null);
