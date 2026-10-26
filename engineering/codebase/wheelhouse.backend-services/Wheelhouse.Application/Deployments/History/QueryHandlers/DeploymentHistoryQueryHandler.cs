using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentHistoryQuery"/>: the filter's deployments newest first, each target's latest of
/// a day alone when the filter asks for it.</summary>
public sealed class DeploymentHistoryQueryHandler(DeploymentHistoryService history)
    : IQueryHandler<DeploymentHistoryQuery, AppResult<DeploymentPageDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<DeploymentPageDto>> HandleAsync(DeploymentHistoryQuery request, CancellationToken cancellationToken)
    {
        var read = await history.LoadAsync(cancellationToken);
        if (read is not AppResult<DeploymentHistory>.Success { Data: var loaded })
            return AppResult<DeploymentPageDto>.Fail(((AppResult<DeploymentHistory>.Failure)read).Error);

        var after = request.Cursor is { } cursor && DeploymentCursor.TryRead(cursor, out var position) ? position : ((long, string)?)null;
        var matching = DeploymentQuerying.SelectWithControls(loaded, request.Filter)
            .Where(entry => after is not { } start || DeploymentCursor.IsAfter(entry, start))
            .Take(request.Limit + 1)
            .ToList();
        var more = matching.Count > request.Limit;
        var page = matching.Take(request.Limit).ToList();
        return AppResult<DeploymentPageDto>.Ok(new DeploymentPageDto(
            [.. page.Select(DeploymentItemDto.From)], more ? DeploymentCursor.After(page[^1]) : null));
    }
}
