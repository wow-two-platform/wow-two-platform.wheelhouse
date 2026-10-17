namespace Wheelhouse.Domain.Pins.Enums;

/// <summary>Refers to what a pin keeps one click away.</summary>
public enum PinKind
{
    /// <summary>A workspace.</summary>
    Workspace,

    /// <summary>A product.</summary>
    Product,

    /// <summary>One service of a product; one pin covers every environment the service runs in.</summary>
    Service,
}
