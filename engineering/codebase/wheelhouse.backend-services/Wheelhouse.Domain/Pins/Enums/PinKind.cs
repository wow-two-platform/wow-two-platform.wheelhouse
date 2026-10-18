namespace Wheelhouse.Domain.Pins.Enums;

/// <summary>Refers to what a pin keeps one click away. Only products are pinned since 2026-10-18 (migration
/// <c>015-product-pins-only</c>); the other kinds name what the table can still hold, and the API refuses them.</summary>
public enum PinKind
{
    /// <summary>A workspace.</summary>
    Workspace,

    /// <summary>A product.</summary>
    Product,

    /// <summary>One service of a product; one pin covers every environment the service runs in.</summary>
    Service,
}
