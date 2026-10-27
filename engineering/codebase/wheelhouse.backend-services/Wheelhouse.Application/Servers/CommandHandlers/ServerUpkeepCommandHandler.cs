using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.InfraJobs;
using Wheelhouse.Application.Servers.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Servers.CommandHandlers;

/// <summary>Handles <see cref="ServerUpkeepCommand"/>: the runner records the job and runs it detached; the console
/// follows its steps. Once it succeeds, <see cref="InfraJobs.InfraJobRecordCommand"/> records the ingress the kit lays
/// out and the server's public address. Deployed dev refuses: it never holds a products host's key. A started job wakes
/// the infrastructure follower.</summary>
public sealed class ServerUpkeepCommandHandler(
    IServersRepository servers, IInfraGateway gateway, IInfraAccess access, InfraFollowSchedule following)
    : ICommandHandler<ServerUpkeepCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(ServerUpkeepCommand request, CancellationToken cancellationToken)
    {
        if (access.Refusal is { } refusal)
            return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(refusal));
        if (await servers.GetBySlugAsync(request.Slug, cancellationToken) is null)
            return AppResult<JsonElement>.Fail(AppErrorFactory.NotFound($"Server '{request.Slug}' was not found."));
        var result = await gateway.UpkeepAsync(request.Slug, request.Actor, cancellationToken);
        if (result is AppResult<JsonElement>.Success)
            following.Wake();
        return result;
    }
}
