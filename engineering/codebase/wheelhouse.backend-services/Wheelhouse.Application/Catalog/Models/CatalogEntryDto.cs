using Wheelhouse.Domain.Catalog.Enums;

namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents a catalog entry as the console lists it, with the product it already is, if any.</summary>
public sealed record CatalogEntryDto
{
    /// <summary>Gets the entry's slug.</summary>
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

    /// <summary>Gets the entry's GitHub repositories, the primary first; empty while none exists, which makes the
    /// entry not importable.</summary>
    public required IReadOnlyList<string> Repositories { get; init; }

    /// <summary>Gets the slugs of the products the entry supersedes: importing it retires each one nothing ever deployed
    /// to — its environments, then the product.</summary>
    public IReadOnlyList<string> Replaces { get; init; } = [];

    /// <summary>Gets the repository of the hub that lists the entry.</summary>
    public required string Source { get; init; }

    /// <summary>Gets the identifier of the product with the entry's slug, or <c>null</c> when none exists.</summary>
    public Guid? ProductId { get; init; }
}
