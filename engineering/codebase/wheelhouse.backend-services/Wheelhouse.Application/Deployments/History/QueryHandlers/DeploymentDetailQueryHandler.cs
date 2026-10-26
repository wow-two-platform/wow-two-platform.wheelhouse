using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentDetailQuery"/>. An unreadable target report still answers the deployment, with
/// no steps or sites and <c>observed</c> false.</summary>
public sealed class DeploymentDetailQueryHandler(DeploymentHistoryService history, IDeploymentRecords records)
    : IQueryHandler<DeploymentDetailQuery, AppResult<DeploymentDetailDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<DeploymentDetailDto>> HandleAsync(DeploymentDetailQuery request, CancellationToken cancellationToken)
    {
        var read = await history.LoadAsync(cancellationToken);
        if (read is not AppResult<DeploymentHistory>.Success { Data: var loaded })
            return AppResult<DeploymentDetailDto>.Fail(((AppResult<DeploymentHistory>.Failure)read).Error);

        var id = request.Id.ToString();
        var entry = loaded.Entries.FirstOrDefault(item => item.Record.Id == id)
            ?? loaded.Controls.FirstOrDefault(item => item.Record.Id == id);
        if (entry is null)
            return AppResult<DeploymentDetailDto>.Fail(AppErrorFactory.NotFound("Deployment was not found."));

        // A deploy that never reached its target has no report to read.
        var observed = entry.Record.Intent is null
            && await records.ObserveAsync(request.Id, cancellationToken) is AppResult<DeploymentObservation>.Success { Data: var data }
            ? data
            : null;
        var previous = entry.Previous is { } prior ? new DeploymentReferenceDto(prior.Id, prior.Release, prior.BundleId) : null;
        return AppResult<DeploymentDetailDto>.Ok(new DeploymentDetailDto(
            DeploymentItemDto.From(entry),
            entry.Changes,
            previous,
            CanRollBack(entry, loaded) ? previous : null,
            observed?.Steps ?? [],
            observed?.Sites ?? [],
            observed?.ReconciledBy,
            observed is not null,
            observed?.Snapshot,
            observed?.ServiceRollout));
    }

    /// <summary>Whether a roll back to the previous release applies: this rollout is what its target runs, its release
    /// declares the previous images safe on its schema, and the previous release is another one. @internal</summary>
    private static bool CanRollBack(DeploymentEntry entry, DeploymentHistory loaded)
    {
        if (entry.Outcome != DeploymentOutcome.Succeeded || entry.Record.RollbackCompatible != true
            || entry.Previous is not { BundleId: not null } previous || previous.Release == entry.Record.Release)
            return false;
        // Entries are newest first: the first success on the target is what it runs.
        var current = loaded.Entries.FirstOrDefault(item =>
            item.Record.TargetId == entry.Record.TargetId && item.Outcome == DeploymentOutcome.Succeeded);
        return current?.Record.Id == entry.Record.Id;
    }
}
