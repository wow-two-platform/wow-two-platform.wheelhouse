namespace Wheelhouse.Application.Vaults.Changes;

/// <summary>Represents one administrative change to a vault.</summary>
public abstract record VaultChange
{
    /// <summary>Gets the namespace the change targets.</summary>
    public required string Namespace { get; init; }

    /// <summary>Gets the display-safe audit description; never includes values or tokens.</summary>
    public abstract string Describe();
}
