using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Servers.Commands;
using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Application.Servers.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Servers.CommandHandlers;

/// <summary>Handles <see cref="ServerEnrollCommand"/>: the runner records the job and runs it detached; the console
/// follows its steps. Once it succeeds, <see cref="InfraJobs.InfraJobRecordCommand"/> points the server at its tailnet
/// address. The control host is never enrolled (<see cref="ControlHost"/>).</summary>
public sealed class ServerEnrollCommandHandler(IServersRepository servers, IInfraGateway gateway, IInfraAccess access)
    : ICommandHandler<ServerEnrollCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(ServerEnrollCommand request, CancellationToken cancellationToken)
    {
        if (access.Refusal is { } refusal)
            return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(refusal));
        var server = await servers.GetBySlugAsync(request.Slug, cancellationToken);
        if (server is null)
            return AppResult<JsonElement>.Fail(AppErrorFactory.NotFound($"Server '{request.Slug}' was not found."));
        if (ControlHost.Is(server))
            return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(
                $"Server '{request.Slug}' is the control host, which the consoles run on; enrollment sets up a fresh public server."));
        return await gateway.EnrollAsync(new ServerEnrollmentRequest(
            request.Slug, request.PublicAddress, request.SshPort, request.LoginUser, request.Admin,
            string.IsNullOrWhiteSpace(request.AdminKey) ? null : request.AdminKey.Trim(), request.TailnetHostname,
            string.IsNullOrWhiteSpace(request.ExpectedHostKey) ? null : request.ExpectedHostKey.Trim()),
            request.Actor, cancellationToken);
    }
}
