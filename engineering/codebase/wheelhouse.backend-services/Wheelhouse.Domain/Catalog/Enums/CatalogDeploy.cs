namespace Wheelhouse.Domain.Catalog.Enums;

/// <summary>Refers to what deploys a wow2 catalog entry.</summary>
public enum CatalogDeploy
{
    /// <summary>Wheelhouse deploys it, so importing it creates a product.</summary>
    Wheelhouse,

    /// <summary>Its own CI deploys it, as Wheelhouse itself is.</summary>
    Ci,

    /// <summary>Nothing runs.</summary>
    None,
}
