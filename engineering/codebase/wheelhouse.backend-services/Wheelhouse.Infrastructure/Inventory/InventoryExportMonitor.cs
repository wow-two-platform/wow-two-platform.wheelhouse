using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.History.Models;

namespace Wheelhouse.Infrastructure.Inventory;

/// <summary>Keeps whether the last export of the runner's inventory snapshot failed, for the Issues panel: the SDK's
/// after-commit hook logs a failed export and keeps the change saved, so without this note the owner would not learn that
/// deploys read an older snapshot. The next successful export clears it.</summary>
public sealed class InventoryExportMonitor(TimeProvider time) : IInventoryExportIssues
{
    /// <summary>Holds the cause of a snapshot that could not be written.</summary>
    public const string Unwritten = "The snapshot file could not be written";

    /// <summary>Holds the cause of an inventory that could not be read for the export.</summary>
    public const string Unread = "The inventory could not be read for the export";

    /// <summary>Holds the cause of an export that ran out of time.</summary>
    public const string TimedOut = "The export ran out of time";

    private InventoryExportIssueDto? _failure;

    /// <summary>Notes a failed export.</summary>
    /// <param name="cause">One of this type's causes.</param>
    public void Failed(string cause) => Volatile.Write(ref _failure, new InventoryExportIssueDto(cause, time.GetUtcNow()));

    /// <summary>Notes a written snapshot, which clears a failure.</summary>
    public void Succeeded() => Volatile.Write(ref _failure, null);

    /// <inheritdoc />
    public IReadOnlyList<InventoryExportIssueDto> Current() =>
        Volatile.Read(ref _failure) is { } failure ? [failure] : [];
}
