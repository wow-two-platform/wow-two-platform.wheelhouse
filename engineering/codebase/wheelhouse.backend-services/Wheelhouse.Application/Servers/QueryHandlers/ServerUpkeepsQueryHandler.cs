using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Servers.Queries;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Servers.QueryHandlers;

/// <summary>Handles <see cref="ServerUpkeepsQuery"/> for a server the inventory holds.</summary>
public sealed class ServerUpkeepsQueryHandler(IServersRepository servers, IInfraGateway gateway)
    : IQueryHandler<ServerUpkeepsQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(ServerUpkeepsQuery request, CancellationToken cancellationToken) =>
        await servers.GetBySlugAsync(request.Slug, cancellationToken) is null
            ? AppResult<JsonElement>.Fail(AppErrorFactory.NotFound($"Server '{request.Slug}' was not found."))
            : await gateway.UpkeepsAsync(request.Slug, cancellationToken);
}
