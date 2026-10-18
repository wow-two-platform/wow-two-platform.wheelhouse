namespace Wheelhouse.Domain.Products.Enums;

/// <summary>Refers to whether a product, or one of its services, is in use. Inactive is a display filter only: an inactive
/// product or service still deploys and still reads.</summary>
public enum ProductStatus
{
    /// <summary>In use, and shown.</summary>
    Active,

    /// <summary>Not in use for now, and hidden until the owner asks to see inactive ones.</summary>
    Inactive,
}
