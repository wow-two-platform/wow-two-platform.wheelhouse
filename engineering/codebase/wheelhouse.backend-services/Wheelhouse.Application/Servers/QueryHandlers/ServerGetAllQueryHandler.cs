using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Servers.Mappers;
using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Application.Servers.Queries;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Servers.QueryHandlers;

/// <summary>Handles <see cref="ServerGetAllQuery"/>.</summary>
public sealed class ServerGetAllQueryHandler(IServersRepository servers)
    : IQueryHandler<ServerGetAllQuery, AppResult<IReadOnlyList<ServerDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<ServerDto>>> HandleAsync(
        ServerGetAllQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ServerDto> listed = [.. (await servers.GetAllAsync(cancellationToken)).Select(ServerMapper.Map)];
        return AppResult<IReadOnlyList<ServerDto>>.Ok(listed);
    }
}
