using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents one secret read from a standalone vault, decrypted, for the built-in vault to keep.</summary>
/// <param name="ProductId">The product it belongs to.</param>
/// <param name="Environment">The environment.</param>
/// <param name="Key">The configuration path.</param>
/// <param name="Disabled">Whether it was disabled there; it arrives removed, its versions kept.</param>
/// <param name="CurrentVersion">The version it was on.</param>
/// <param name="Versions">Each version with its value.</param>
/// <param name="Origin">Where it came from, as its versions name it.</param>
public sealed record ImportedSecret(
    Guid ProductId, DeploymentEnvironment Environment, string Key, bool Disabled, int CurrentVersion,
    IReadOnlyList<ImportedVersion> Versions, string Origin)
{
    /// <summary>Returns the secret without its values.</summary>
    public override string ToString() => nameof(ImportedSecret) + " { " + Key + ", " + Versions.Count + " versions }";
}
