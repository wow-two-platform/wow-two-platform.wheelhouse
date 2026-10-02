namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents a vault in the runner's inventory; its administrator password stays a file on the control host.</summary>
public sealed record SnapshotVaultModel
{
    /// <summary>Gets the vault's slug.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the slug of the server the vault runs on.</summary>
    public required string ServerId { get; init; }

    /// <summary>Gets the management endpoint.</summary>
    public required string Url { get; init; }
}
