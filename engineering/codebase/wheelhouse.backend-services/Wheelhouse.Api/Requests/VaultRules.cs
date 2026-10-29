namespace Wheelhouse.Api.Requests;

/// <summary>Holds the path-safe identifier patterns for vault routes and bodies; a vault id is an inventory slug
/// (<see cref="Application.Inventory.Constants.InventoryPatternConstants.Slug"/>).</summary>
public static class VaultRules
{
    /// <summary>A vault namespace slug.</summary>
    public const string Namespace = "^[a-z0-9][a-z0-9._-]{0,99}$";
    /// <summary>A secret key, including configuration paths such as <c>Billing:SecretKey</c>.</summary>
    public const string Key = "^[A-Za-z0-9][A-Za-z0-9._:-]{0,255}$";
}
