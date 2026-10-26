namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents a failed export of the runner's inventory snapshot; deploys read the last snapshot that was written
/// until an export succeeds again. Never carries a path or an exception's text.</summary>
/// <param name="Cause">What failed, in plain words.</param>
/// <param name="At">When the export failed.</param>
public sealed record InventoryExportIssueDto(string Cause, DateTimeOffset At);
