namespace Wheelhouse.Application.SecretsVault.Commands;

/// <summary>Holds a vault command's outcome in counts, for its audit entry.</summary>
public sealed class VaultOutcome
{
    /// <summary>Gets or sets the counts as one line; never a value.</summary>
    public string? Detail { get; set; }
}
