using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.CommandHandlers;

/// <summary>Handles <see cref="RootKeyOfflineCopyVerifyCommand"/>.</summary>
public sealed class RootKeyOfflineCopyVerifyCommandHandler(RootKeyChecks checks)
    : ICommandHandler<RootKeyOfflineCopyVerifyCommand, AppResult<RootKeyOfflineCopyDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<RootKeyOfflineCopyDto>> HandleAsync(
        RootKeyOfflineCopyVerifyCommand request, CancellationToken cancellationToken) =>
        await checks.VerifyOfflineCopyAsync(request.Fingerprint, request.Reason, request.Actor, cancellationToken);
}
