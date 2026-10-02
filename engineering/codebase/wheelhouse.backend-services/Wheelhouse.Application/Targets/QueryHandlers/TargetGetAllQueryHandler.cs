using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Targets.Models;
using Wheelhouse.Application.Targets.Queries;
using Wheelhouse.Application.Targets.Services;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Targets.QueryHandlers;

/// <summary>Handles <see cref="TargetGetAllQuery"/>.</summary>
public sealed class TargetGetAllQueryHandler(ITargetsRepository targets, TargetsProjectionService projection)
    : IQueryHandler<TargetGetAllQuery, AppResult<IReadOnlyList<TargetDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<TargetDto>>> HandleAsync(
        TargetGetAllQuery request, CancellationToken cancellationToken)
    {
        var listed = await projection.ProjectAsync(await targets.GetAllAsync(cancellationToken), cancellationToken);
        return AppResult<IReadOnlyList<TargetDto>>.Ok(
            request.Product is null ? listed : [.. listed.Where(target => target.Product == request.Product)]);
    }
}
