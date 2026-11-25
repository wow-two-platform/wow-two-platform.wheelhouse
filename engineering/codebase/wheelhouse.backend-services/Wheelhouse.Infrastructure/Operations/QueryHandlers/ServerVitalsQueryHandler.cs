using Wheelhouse.Application;
using Wheelhouse.Application.Operations;
using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Operations.QueryHandlers;

/// <summary>Handles <see cref="ServerVitalsQuery"/>.</summary>
public sealed class ServerVitalsQueryHandler(IServerVitals vitals) : IQueryHandler<ServerVitalsQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(ServerVitalsQuery request, CancellationToken cancellationToken) =>
        await vitals.ReadAsync(request.Fresh, cancellationToken);
}
