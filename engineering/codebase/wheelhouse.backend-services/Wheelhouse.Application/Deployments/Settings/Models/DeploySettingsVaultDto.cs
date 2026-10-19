namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents where an environment's secret values live: a vault and its namespace.</summary>
/// <param name="Id">The vault's identifier.</param>
/// <param name="Namespace">The namespace, <c>{product}-{environment}</c>.</param>
/// <param name="Reachable">Whether the vault answered; an unreachable one holds deploys of the environment.</param>
public sealed record DeploySettingsVaultDto(string Id, string Namespace, bool Reachable);
