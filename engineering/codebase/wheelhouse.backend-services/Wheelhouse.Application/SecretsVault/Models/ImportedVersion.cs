namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents one version of an imported secret.</summary>
/// <param name="Version">Its number there.</param>
/// <param name="Value">Its value; never logged.</param>
/// <param name="CreatedBy">Who wrote it there.</param>
/// <param name="CreatedAt">When.</param>
public sealed record ImportedVersion(int Version, string Value, string CreatedBy, DateTimeOffset CreatedAt)
{
    /// <summary>Returns the version without its value.</summary>
    public override string ToString() => nameof(ImportedVersion) + " { v" + Version + " }";
}
