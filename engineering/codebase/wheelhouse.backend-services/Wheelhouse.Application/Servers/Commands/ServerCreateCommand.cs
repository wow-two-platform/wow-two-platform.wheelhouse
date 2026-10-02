using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Inventory;
using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Domain.Servers.Enums;
using Wheelhouse.Domain.Servers.Models;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Servers.Commands;

/// <summary>Represents a command to add a host Wheelhouse deploys to.</summary>
public sealed record ServerCreateCommand : ICommand<AppResult<ServerDto>>, IAuditedCommand, IInventoryCommand
{
    /// <summary>Gets the name targets, vaults and the credential folders will know the server by; fixed once created.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets who hosts the server.</summary>
    public required VpsProvider Provider { get; init; }

    /// <summary>Gets the host name or address SSH connects to.</summary>
    public required string Host { get; init; }

    /// <summary>Gets the provider's region.</summary>
    public required string Region { get; init; }

    /// <summary>Gets the user SSH signs in as.</summary>
    public required string SshUser { get; init; }

    /// <summary>Gets the port SSH connects to.</summary>
    public required int SshPort { get; init; }

    /// <summary>Gets how the server's ingress publishes sites.</summary>
    public required ServerIngressValueObject Ingress { get; init; }

    /// <inheritdoc />
    public string AuditAction => "server.create";

    /// <inheritdoc />
    public string AuditSubject => Slug;

    /// <inheritdoc />
    public string? AuditDetail => SshUser + "@" + Host + ":" + SshPort;
}
