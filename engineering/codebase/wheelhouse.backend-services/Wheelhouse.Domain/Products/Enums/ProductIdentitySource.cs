namespace Wheelhouse.Domain.Products.Enums;

/// <summary>Refers to where a product's name, description and homepage come from.</summary>
public enum ProductIdentitySource
{
    /// <summary>The product's own row, edited in the console.</summary>
    Record,

    /// <summary>The cached copy of the product manifest in the product's repository, edited there.</summary>
    Manifest,
}
