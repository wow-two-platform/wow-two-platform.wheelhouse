using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Integrations.Commands;
using Wheelhouse.Application.Integrations.Mappers;
using Wheelhouse.Application.Integrations.Models;
using Wheelhouse.Domain.Integrations.Entities;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Identity.ApiKeys;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Integrations.CommandHandlers;

/// <summary>Handles <see cref="IntegrationKeyCreateCommand"/>; a live key's name stays unique, since it names the
/// caller in the audit trail.</summary>
public sealed class IntegrationKeyCreateCommandHandler(
    IIntegrationKeysRepository keys, ApiKeySecretFactory secrets, IOperatorContext operatorContext)
    : ICommandHandler<IntegrationKeyCreateCommand, AppResult<IntegrationKeyWithSecretDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IntegrationKeyWithSecretDto>> HandleAsync(
        IntegrationKeyCreateCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var existing = await keys.ListAsync(cancellationToken);
        if (existing.Any(key => key.RevokedAt is null && string.Equals(key.Name, name, StringComparison.OrdinalIgnoreCase)))
            return AppResult<IntegrationKeyWithSecretDto>.Fail(AppErrorFactory.Conflict($"A live key is already named '{name}'."));

        var secret = secrets.Create();
        var key = new IntegrationKeyEntity
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Prefix = secret.Prefix,
            Hash = secret.Hash,
            Scopes = string.Join(' ', request.Scopes.Distinct(StringComparer.Ordinal)),
            CreatedBy = operatorContext.Actor,
        };
        await keys.AddAsync(key, cancellationToken);

        return AppResult<IntegrationKeyWithSecretDto>.Ok(
            new IntegrationKeyWithSecretDto { Key = IntegrationKeyMapper.Map(key), Secret = secret.Secret });
    }
}
