using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Tailnet.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Tailnet.QueryHandlers;

/// <summary>Handles <see cref="TailnetReadQuery"/>; a console that may not drive infrastructure reads as not connected,
/// with the reason, and never asks the runner.</summary>
public sealed class TailnetReadQueryHandler(IInfraGateway gateway, IInfraAccess access)
    : IQueryHandler<TailnetReadQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(TailnetReadQuery request, CancellationToken cancellationToken)
    {
        if (access.Refusal is { } refusal)
            return AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(new
            {
                configured = false, connected = false, prodOnly = true, reason = refusal, tailnet = (string?)null,
                clientFile = "providers/tailscale/oauth-client.json", nodes = Array.Empty<object>(),
                servers = Array.Empty<object>(),
            }));
        return await gateway.TailnetAsync(cancellationToken);
    }
}
