using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentInventoryExportQuery"/> from what the exports noted; the snapshot is the whole
/// control plane's, so no deployment filter applies.</summary>
public sealed class DeploymentInventoryExportQueryHandler(IInventoryExportIssues issues)
    : IQueryHandler<DeploymentInventoryExportQuery, AppResult<IReadOnlyList<InventoryExportIssueDto>>>
{
    /// <inheritdoc />
    public ValueTask<AppResult<IReadOnlyList<InventoryExportIssueDto>>> HandleAsync(
        DeploymentInventoryExportQuery request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AppResult<IReadOnlyList<InventoryExportIssueDto>>.Ok(issues.Current()));
}
