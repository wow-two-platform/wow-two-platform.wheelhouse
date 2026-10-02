namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the export of the inventory — products, servers, targets and vaults — to the snapshot the runner
/// and the operator CLI read.</summary>
public interface IInventorySnapshot
{
    /// <summary>Writes the snapshot from what the database holds now, replacing the previous one whole.</summary>
    /// <param name="ct">A cancellation token.</param>
    Task ExportAsync(CancellationToken ct);
}
