using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Tailnet.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Tailnet.CommandHandlers;

/// <summary>Handles <see cref="TailnetPolicyApplyCommand"/>: the runner checks the digest, the ETag, Tailscale's
/// validation and the control-path guard again before it saves, and keeps the policy it replaces.</summary>
public sealed class TailnetPolicyApplyCommandHandler(IInfraGateway gateway, IInfraAccess access)
    : ICommandHandler<TailnetPolicyApplyCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(TailnetPolicyApplyCommand request, CancellationToken cancellationToken) =>
        access.Refusal is { } refusal
            ? AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(refusal))
            : await gateway.ApplyPolicyAsync(request.Etag, request.Digest, request.Actor, cancellationToken);
}
