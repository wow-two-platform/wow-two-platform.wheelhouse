using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Integrations.Commands;
using Wheelhouse.Application.Integrations.Mappers;
using Wheelhouse.Application.Integrations.Models;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Integrations.CommandHandlers;

/// <summary>Handles <see cref="IntegrationKeyRevokeCommand"/>; revoking a revoked key keeps its first revocation.</summary>
public sealed class IntegrationKeyRevokeCommandHandler(IIntegrationKeysRepository keys, TimeProvider time)
    : ICommandHandler<IntegrationKeyRevokeCommand, AppResult<IntegrationKeyDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IntegrationKeyDto>> HandleAsync(
        IntegrationKeyRevokeCommand request, CancellationToken cancellationToken)
    {
        var key = await keys.GetByIdAsync(request.Id, cancellationToken);
        if (key is null)
            return AppResult<IntegrationKeyDto>.Fail(AppErrorFactory.NotFound($"Integration key '{request.Id}' was not found."));

        if (key.RevokedAt is null)
        {
            key.RevokedAt = time.GetUtcNow();
            await keys.UpdateAsync(key, cancellationToken);
        }

        return AppResult<IntegrationKeyDto>.Ok(IntegrationKeyMapper.Map(key));
    }
}
