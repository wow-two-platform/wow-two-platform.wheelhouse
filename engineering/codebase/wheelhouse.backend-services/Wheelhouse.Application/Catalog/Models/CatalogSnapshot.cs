namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents one read of the catalog: where it came from and what it holds.</summary>
/// <param name="Source">The repository, path, ref and commit the catalog was read from.</param>
/// <param name="Document">The workspaces and entries.</param>
public sealed record CatalogSnapshot(CatalogSourceDto Source, CatalogDocument Document);
