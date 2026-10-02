using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Servers.Commands;
using Wheelhouse.Application.Servers.Mappers;
using Wheelhouse.Application.Servers.Models;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Servers.CommandHandlers;

/// <summary>Handles <see cref="ServerUpdateCommand"/>; a host that changes keeps failing its pinned host key until
/// the operator replaces the pin.</summary>
public sealed class ServerUpdateCommandHandler(IServersRepository servers)
    : ICommandHandler<ServerUpdateCommand, AppResult<ServerDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ServerDto>> HandleAsync(ServerUpdateCommand request, CancellationToken cancellationToken)
    {
        var server = await servers.GetBySlugAsync(request.Slug, cancellationToken);
        if (server is null)
            return AppResult<ServerDto>.Fail(AppErrorFactory.NotFound($"Server '{request.Slug}' was not found."));

        server.Name = request.Name.Trim();
        server.Provider = request.Provider;
        server.Host = request.Host;
        server.Region = request.Region;
        server.SshUser = request.SshUser;
        server.SshPort = request.SshPort;
        server.Ingress = request.Ingress;
        await servers.UpdateAsync(server, cancellationToken);
        return AppResult<ServerDto>.Ok(ServerMapper.Map(server));
    }
}
