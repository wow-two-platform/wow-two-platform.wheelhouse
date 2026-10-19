namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents one hub the catalog was read from, and what the read gave.</summary>
public sealed record CatalogSourceDto
{
    /// <summary>Gets the hub repository, as <c>owner/name</c>; entries and workspaces name it as their source.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets the catalog's path in the repository.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the ref the catalog was read at.</summary>
    public required string Ref { get; init; }

    /// <summary>Gets the hub's name, as the file's <c>hub.name</c> gives it; <c>null</c> when it gives none or was not
    /// read.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the full SHA of the commit the catalog was read from; <c>null</c> when the ref could not be read.</summary>
    public string? Commit { get; init; }

    /// <summary>Gets why the hub's entries are not listed, in display-safe words that name the hub; <c>null</c> when they
    /// are.</summary>
    public string? Error { get; init; }
}
