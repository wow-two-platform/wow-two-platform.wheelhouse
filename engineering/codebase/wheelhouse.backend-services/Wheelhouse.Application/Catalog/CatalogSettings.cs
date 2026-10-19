namespace Wheelhouse.Application.Catalog;

/// <summary>Holds the hubs Wheelhouse reads the catalog from: each hub repository's <c>catalog.yml</c>, read with the
/// signed-in owner's token and merged in this order. Bound from <c>Catalog</c>; <c>Catalog:Sources</c> lists the hubs,
/// and an empty list reads <see cref="DefaultSources"/>.</summary>
public sealed record CatalogSettings
{
    /// <summary>Holds the most hubs Wheelhouse reads.</summary>
    public const int MaxSources = 20;

    /// <summary>Gets the hubs read when <see cref="Sources"/> lists none: the wow2 workspace, the ventures hub and the
    /// Ocharo hub, each <c>catalog.yml</c> at <c>main</c>.</summary>
    public static IReadOnlyList<CatalogSourceSettings> DefaultSources { get; } =
    [
        new() { Repository = "wow-two/wow-two-ws" },
        new() { Repository = "sulton-max/10x-ventures" },
        new() { Repository = "ocharo-hq/ocharo-ws" },
    ];

    /// <summary>Gets or sets the configured hubs, in merge order. Left empty here, so binding never appends to defaults.</summary>
    public List<CatalogSourceSettings> Sources { get; set; } = [];

    /// <summary>Gets the hubs read: <see cref="Sources"/>, or <see cref="DefaultSources"/> when it lists none.</summary>
    public IReadOnlyList<CatalogSourceSettings> Hubs => Sources is { Count: > 0 } ? Sources : DefaultSources;

    /// <summary>Gets whether every hub has its shape and names its repository once, with at most
    /// <see cref="MaxSources"/> hubs.</summary>
    public bool IsValid =>
        Hubs.Count <= MaxSources
        && Hubs.All(source => source is not null && source.IsValid)
        && Hubs.Select(source => source.Repository.ToLowerInvariant()).Distinct().Count() == Hubs.Count;
}
