using Wheelhouse.Application.Deployments.History.Models;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the read of whether the runner's inventory snapshot is behind the database: the last export failed and
/// none has succeeded since. In-process; the next successful export clears it.</summary>
public interface IInventoryExportIssues
{
    /// <summary>Lists the failed export, or nothing while the snapshot matches the last change.</summary>
    /// <returns>At most one issue: the newest failure.</returns>
    IReadOnlyList<InventoryExportIssueDto> Current();
}
