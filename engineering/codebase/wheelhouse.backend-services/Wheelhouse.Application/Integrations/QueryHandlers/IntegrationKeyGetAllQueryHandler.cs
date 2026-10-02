using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Integrations.Mappers;
using Wheelhouse.Application.Integrations.Models;
using Wheelhouse.Application.Integrations.Queries;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Integrations.QueryHandlers;

/// <summary>Handles <see cref="IntegrationKeyGetAllQuery"/>.</summary>
public sealed class IntegrationKeyGetAllQueryHandler(IIntegrationKeysRepository keys)
    : IQueryHandler<IntegrationKeyGetAllQuery, AppResult<IReadOnlyList<IntegrationKeyDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<IntegrationKeyDto>>> HandleAsync(
        IntegrationKeyGetAllQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<IntegrationKeyDto> listed = [.. (await keys.ListAsync(cancellationToken)).Select(IntegrationKeyMapper.Map)];
        return AppResult<IReadOnlyList<IntegrationKeyDto>>.Ok(listed);
    }
}
