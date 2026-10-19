using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Servers.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Servers.CommandHandlers;

/// <summary>Handles <see cref="ServerEnrollmentKeyCommand"/> on the console that may enroll.</summary>
public sealed class ServerEnrollmentKeyCommandHandler(IServersRepository servers, IInfraGateway gateway, IInfraAccess access)
    : ICommandHandler<ServerEnrollmentKeyCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(ServerEnrollmentKeyCommand request, CancellationToken cancellationToken)
    {
        if (access.Refusal is { } refusal)
            return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(refusal));
        if (await servers.GetBySlugAsync(request.Slug, cancellationToken) is null)
            return AppResult<JsonElement>.Fail(AppErrorFactory.NotFound($"Server '{request.Slug}' was not found."));
        return await gateway.EnrollmentKeyAsync(request.Slug, cancellationToken);
    }
}
