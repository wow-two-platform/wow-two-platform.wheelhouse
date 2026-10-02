namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents the inventory as the runner reads it from <c>{root}/inventory.json</c>; the runner's
/// <c>inventory.py</c> holds the same contract.</summary>
public sealed record InventorySnapshotModel
{
    /// <summary>Gets the contract version the runner checks before reading anything else.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Gets the products.</summary>
    public required IReadOnlyList<SnapshotProductModel> Products { get; init; }

    /// <summary>Gets the servers.</summary>
    public required IReadOnlyList<SnapshotServerModel> Servers { get; init; }

    /// <summary>Gets the targets.</summary>
    public required IReadOnlyList<SnapshotTargetModel> Targets { get; init; }

    /// <summary>Gets the vaults.</summary>
    public required IReadOnlyList<SnapshotVaultModel> Vaults { get; init; }
}
