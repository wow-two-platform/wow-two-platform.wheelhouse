namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents the merged catalog as the console lists it: every hub's outcome, then the readable hubs'
/// workspaces and entries.</summary>
public sealed record CatalogDto
{
    /// <summary>Gets each hub read, in settings order, with its commit or its error.</summary>
    public required IReadOnlyList<CatalogSourceDto> Sources { get; init; }

    /// <summary>Gets the declared workspaces, hub by hub in file order.</summary>
    public required IReadOnlyList<CatalogWorkspaceDto> Workspaces { get; init; }

    /// <summary>Gets the entries, hub by hub in file order.</summary>
    public required IReadOnlyList<CatalogEntryDto> Entries { get; init; }
}
