using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Tailnet.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Tailnet.CommandHandlers;

/// <summary>Handles <see cref="TailnetPolicyRevertCommand"/> under the same checks as an apply.</summary>
public sealed class TailnetPolicyRevertCommandHandler(IInfraGateway gateway, IInfraAccess access)
    : ICommandHandler<TailnetPolicyRevertCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(TailnetPolicyRevertCommand request, CancellationToken cancellationToken) =>
        access.Refusal is { } refusal
            ? AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(refusal))
            : await gateway.RevertPolicyAsync(request.Etag, request.Actor, cancellationToken);
}
