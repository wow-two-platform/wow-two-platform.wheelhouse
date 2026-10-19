namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents where an environment's secret values live: the built-in Secrets Vault, or while the fallback is
/// on, an inventory vault and its namespace.</summary>
/// <param name="Id">The vault: <c>built-in</c>, or the inventory vault's identifier.</param>
/// <param name="Namespace">The namespace, <c>{product}-{environment}</c>.</param>
/// <param name="Reachable">Whether secret values can be read: the built-in vault unsealed, or the inventory vault
/// answering. A deploy that needs secrets waits until they can.</param>
public sealed record DeploySettingsVaultDto(string Id, string Namespace, bool Reachable)
{
    /// <summary>Gets where deploys take secrets from: <c>builtin</c> or <c>external</c>.</summary>
    public string Mode { get; init; } = SecretsVault.Constants.SecretsVaultModes.BuiltIn;

    /// <summary>Gets why the built-in vault is sealed (<c>missing</c>, <c>invalid</c>, <c>mismatch</c>); null otherwise.</summary>
    public string? Sealed { get; init; }
}
