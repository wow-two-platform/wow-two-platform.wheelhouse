using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentRunningQuery"/>.</summary>
public sealed class DeploymentRunningQueryHandler(DeploymentHistoryService history)
    : IQueryHandler<DeploymentRunningQuery, AppResult<IReadOnlyList<DeploymentItemDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<DeploymentItemDto>>> HandleAsync(
        DeploymentRunningQuery request, CancellationToken cancellationToken)
    {
        var read = await history.LoadAsync(cancellationToken);
        if (read is not AppResult<DeploymentHistory>.Success { Data: var loaded })
            return AppResult<IReadOnlyList<DeploymentItemDto>>.Fail(((AppResult<DeploymentHistory>.Failure)read).Error);

        return AppResult<IReadOnlyList<DeploymentItemDto>>.Ok([
            .. loaded.Entries
                .Where(entry => entry.Outcome == DeploymentOutcome.Pending && DeploymentQuerying.MatchesEntities(entry, request.Filter))
                .Select(DeploymentItemDto.From),
        ]);
    }
}
