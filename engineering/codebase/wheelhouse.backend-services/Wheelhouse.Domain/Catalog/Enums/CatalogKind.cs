namespace Wheelhouse.Domain.Catalog.Enums;

/// <summary>Refers to what a wow2 catalog entry is.</summary>
public enum CatalogKind
{
    /// <summary>Core identity: vision, roadmap, standards.</summary>
    Core,

    /// <summary>Off-ecosystem documents.</summary>
    Meta,

    /// <summary>A platform app that deploys and runs the portfolio.</summary>
    Platform,

    /// <summary>An internal building block: a platform library or pipeline.</summary>
    Backbone,

    /// <summary>An SDK package.</summary>
    Sdk,

    /// <summary>A knowledge base module.</summary>
    Kb,

    /// <summary>A community app.</summary>
    App,

    /// <summary>A product venture.</summary>
    Venture,

    /// <summary>A template other repositories start from.</summary>
    Template,
}
