using Wheelhouse.Domain.Catalog.Enums;

namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents one catalog entry: a product, library, knowledge base module or template.</summary>
public sealed record CatalogEntryModel
{
    /// <summary>Gets the entry's slug; a deployable's equals the <c>product</c> in its <c>deploy.yml</c>.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the slug of the workspace the entry belongs to.</summary>
    public required string Workspace { get; init; }

    /// <summary>Gets what the entry is.</summary>
    public required CatalogKind Kind { get; init; }

    /// <summary>Gets what deploys the entry.</summary>
    public required CatalogDeploy Deploy { get; init; }

    /// <summary>Gets one line on what the entry is for.</summary>
    public required string Purpose { get; init; }

    /// <summary>Gets the entry's GitHub repositories as <c>owner/name</c>, the primary first; empty while none exists.</summary>
    public required IReadOnlyList<string> Repositories { get; init; }
}
