namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents one read of every hub: each hub's outcome, in settings order, and what the readable hubs hold
/// together.</summary>
/// <param name="Sources">Each hub's read, in settings order.</param>
/// <param name="Document">The readable hubs' workspaces and entries, hub by hub, each tagged with its hub.</param>
public sealed record CatalogSnapshot(IReadOnlyList<CatalogSourceRead> Sources, CatalogDocument Document);
