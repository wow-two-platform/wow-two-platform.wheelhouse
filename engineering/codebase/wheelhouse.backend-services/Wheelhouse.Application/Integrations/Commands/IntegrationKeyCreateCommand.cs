using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Integrations.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Integrations.Commands;

/// <summary>Represents a command to create an integration key for another program.</summary>
public sealed record IntegrationKeyCreateCommand : ICommand<AppResult<IntegrationKeyWithSecretDto>>, IAuditedCommand
{
    /// <summary>Gets the name the key carries, such as the program that will present it.</summary>
    public required string Name { get; init; }

    /// <summary>Gets what the key may reach.</summary>
    public required IReadOnlyList<string> Scopes { get; init; }

    /// <inheritdoc />
    public string AuditAction => "integration-key.create";

    /// <inheritdoc />
    public string AuditSubject => Name;

    /// <inheritdoc />
    public string? AuditDetail => string.Join(' ', Scopes);
}
