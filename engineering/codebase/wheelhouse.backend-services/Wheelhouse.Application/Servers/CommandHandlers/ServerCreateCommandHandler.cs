using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Servers.Commands;
using Wheelhouse.Application.Servers.Mappers;
using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Domain.Servers.Entities;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Servers.CommandHandlers;

/// <summary>Handles <see cref="ServerCreateCommand"/>; the server reaches nothing until its SSH identity and pinned
/// host key are on the control host.</summary>
public sealed class ServerCreateCommandHandler(IServersRepository servers)
    : ICommandHandler<ServerCreateCommand, AppResult<ServerDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ServerDto>> HandleAsync(ServerCreateCommand request, CancellationToken cancellationToken)
    {
        if (await servers.GetBySlugAsync(request.Slug, cancellationToken) is not null)
            return AppResult<ServerDto>.Fail(AppErrorFactory.Conflict($"A server is already named '{request.Slug}'."));

        var server = await servers.CreateAsync(new ServerEntity
        {
            Id = Guid.CreateVersion7(),
            Slug = request.Slug,
            Name = request.Name.Trim(),
            Provider = request.Provider,
            Host = request.Host,
            Region = request.Region,
            SshUser = request.SshUser,
            SshPort = request.SshPort,
            Ingress = request.Ingress,
        }, cancellationToken);
        return AppResult<ServerDto>.Ok(ServerMapper.Map(server));
    }
}
