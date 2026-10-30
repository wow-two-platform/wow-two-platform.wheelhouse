namespace Wheelhouse.Application.Inventory.Constants;

/// <summary>Holds the inventory shapes the SDK validation vocabulary does not carry — the same ones the runner enforces.
/// Names, repositories, images, hosts, addresses and paths take the be sdk <c>FormatPatternConstants</c>.</summary>
public static class InventoryPatternConstants
{
    /// <summary>Holds the shape of a Traefik entry point or certificate resolver name.</summary>
    public const string EntryPoint = "^[a-z][a-z0-9-]{0,31}$";
}
