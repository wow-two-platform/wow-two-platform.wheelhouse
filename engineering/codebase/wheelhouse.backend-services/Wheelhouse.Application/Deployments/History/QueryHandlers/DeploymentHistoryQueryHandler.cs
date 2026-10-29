using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;
using WoW.Two.Sdk.Beta.Be.Web.Contracts.Paging;

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

        // Newest first by submission instant, then identifier: a page continues after the last entry it showed, so a new
        // submission never shifts a later page. The cursor is the SDK's opaque page token.
        var page = DeploymentQuerying.SelectWithControls(loaded, request.Filter).ToTokenPage(
            entry => entry.Record.SubmittedAt.UtcTicks,
            entry => entry.Record.Id,
            new TokenPageApiRequest { PageToken = request.Cursor, PageSize = request.Limit },
            descending: true,
            maxPageSize: DeploymentHistoryQuery.MaxLimit);
        return AppResult<DeploymentPageDto>.Ok(new DeploymentPageDto([.. page.Items.Select(DeploymentItemDto.From)], page.NextPageToken));
    }
}
