using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Integrations.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Integrations.Commands;

/// <summary>Represents a command to revoke an integration key, so it never authenticates again.</summary>
public sealed record IntegrationKeyRevokeCommand : ICommand<AppResult<IntegrationKeyDto>>, IAuditedCommand
{
    /// <summary>Gets the key's identifier.</summary>
    public required Guid Id { get; init; }

    /// <inheritdoc />
    public string AuditAction => "integration-key.revoke";

    /// <inheritdoc />
    public string AuditSubject => Id.ToString();
}
