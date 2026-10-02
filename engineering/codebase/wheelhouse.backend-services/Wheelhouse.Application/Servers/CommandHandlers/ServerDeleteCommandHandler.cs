using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Servers.Commands;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Servers.CommandHandlers;

/// <summary>Handles <see cref="ServerDeleteCommand"/>; a server a target or a vault still runs on stays.</summary>
public sealed class ServerDeleteCommandHandler(IServersRepository servers, ITargetsRepository targets, IVaultsRepository vaults)
    : ICommandHandler<ServerDeleteCommand, AppResult<Unit>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<Unit>> HandleAsync(ServerDeleteCommand request, CancellationToken cancellationToken)
    {
        var server = await servers.GetBySlugAsync(request.Slug, cancellationToken);
        if (server is null)
            return AppResult<Unit>.Fail(AppErrorFactory.NotFound($"Server '{request.Slug}' was not found."));
        if (await targets.AnyForServerAsync(server.Id, cancellationToken))
            return AppResult<Unit>.Fail(AppErrorFactory.Conflict("Move or remove the server's environments first."));
        if (await vaults.AnyForServerAsync(server.Id, cancellationToken))
            return AppResult<Unit>.Fail(AppErrorFactory.Conflict("Remove the server's vaults first."));

        await servers.DeleteAsync(server, cancellationToken);
        return AppResult<Unit>.Ok(default);
    }
}
