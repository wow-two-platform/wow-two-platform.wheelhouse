using Wheelhouse.Application.Deployments.Freezes.Models;
using Wheelhouse.Application.Deployments.Freezes.Queries;
using Wheelhouse.Application.Deployments.Freezes.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Freezes.QueryHandlers;

/// <summary>Handles <see cref="DeployFreezesQuery"/>.</summary>
public sealed class DeployFreezesQueryHandler(DeployFreezeService freezes)
    : IQueryHandler<DeployFreezesQuery, AppResult<IReadOnlyList<DeployFreezeDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<DeployFreezeDto>>> HandleAsync(DeployFreezesQuery request, CancellationToken cancellationToken) =>
        AppResult<IReadOnlyList<DeployFreezeDto>>.Ok(await freezes.ListAsync(cancellationToken));
}
