namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents the parts of the wow2 catalog Wheelhouse reads: its workspaces and entries, in file order.</summary>
/// <param name="Workspaces">The declared workspaces.</param>
/// <param name="Entries">The entries.</param>
public sealed record CatalogDocument(IReadOnlyList<CatalogWorkspaceModel> Workspaces, IReadOnlyList<CatalogEntryModel> Entries);
