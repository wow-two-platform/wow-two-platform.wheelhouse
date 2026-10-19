namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents the parts of a catalog Wheelhouse reads: its hub, workspaces and entries, in file order.</summary>
/// <param name="Workspaces">The declared workspaces.</param>
/// <param name="Entries">The entries.</param>
/// <param name="Hub">The hub the file names, or <c>null</c> when it names none.</param>
public sealed record CatalogDocument(
    IReadOnlyList<CatalogWorkspaceModel> Workspaces, IReadOnlyList<CatalogEntryModel> Entries, CatalogHubModel? Hub = null);
