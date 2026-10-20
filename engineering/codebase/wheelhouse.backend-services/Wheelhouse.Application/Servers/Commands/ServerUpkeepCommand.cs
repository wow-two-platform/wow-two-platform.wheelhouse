using System.Text.Json;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Servers.Commands;

/// <summary>Starts bringing an enrolled server to the kit this console ships: the kit uploaded and laid out, its
/// firewall (80 and 443 public, SSH kept), PostgreSQL and the ingress, then a check of the public entry points.</summary>
public sealed record ServerUpkeepCommand : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <summary>Gets the server's slug.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets who starts it.</summary>
    public required string Actor { get; init; }

    /// <inheritdoc />
    public string AuditAction => "server.upkeep";

    /// <inheritdoc />
    public string AuditSubject => Slug;

    /// <inheritdoc />
    public string AuditDetail => "kit, firewall and platform";
}
