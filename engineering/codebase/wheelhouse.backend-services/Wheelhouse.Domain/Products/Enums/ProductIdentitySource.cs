namespace Wheelhouse.Domain.Products.Enums;

/// <summary>Refers to where a product's name, description and homepage come from.</summary>
public enum ProductIdentitySource
{
    /// <summary>The product's own row, edited in the console.</summary>
    Record,

    /// <summary>The cached copy of the identity the product's repository declares — its <c>deploy.yml</c>, or a retired
    /// <c>product/product.json</c> — edited there.</summary>
    Manifest,
}
