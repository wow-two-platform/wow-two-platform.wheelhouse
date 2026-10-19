using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Tailnet.Queries;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Tailnet.QueryHandlers;

/// <summary>Handles <see cref="TailnetPolicyQuery"/>; nothing changes.</summary>
public sealed class TailnetPolicyQueryHandler(IInfraGateway gateway, IInfraAccess access)
    : IQueryHandler<TailnetPolicyQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(TailnetPolicyQuery request, CancellationToken cancellationToken) =>
        access.Refusal is { } refusal
            ? AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(refusal))
            : await gateway.PolicyAsync(cancellationToken);
}
