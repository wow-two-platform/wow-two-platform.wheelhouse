namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to import catalog entries.</summary>
public sealed record CatalogImportApiRequest
{
    /// <summary>Gets the slugs of the entries to import.</summary>
    public required IReadOnlyList<string> Slugs { get; init; }
}
