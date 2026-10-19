using System.Text.Json;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Servers.Commands;

/// <summary>Starts a fresh server's enrollment, or reruns it from the first step that has not succeeded.</summary>
public sealed record ServerEnrollCommand : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <summary>Gets the server's slug.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the server's public IPv4 address.</summary>
    public required string PublicAddress { get; init; }

    /// <summary>Gets its public SSH port.</summary>
    public int SshPort { get; init; } = 22;

    /// <summary>Gets the account the image delivers: <c>root</c>, or an administrator with passwordless sudo.</summary>
    public required string LoginUser { get; init; }

    /// <summary>Gets the administrator the owner signs in as afterwards.</summary>
    public required string Admin { get; init; }

    /// <summary>Gets the owner's SSH public key for a new administrator, or <c>null</c>.</summary>
    public string? AdminKey { get; init; }

    /// <summary>Gets the host's name on the tailnet.</summary>
    public required string TailnetHostname { get; init; }

    /// <summary>Gets the host key fingerprint from the provider's console, or <c>null</c>.</summary>
    public string? ExpectedHostKey { get; init; }

    /// <summary>Gets who starts it.</summary>
    public required string Actor { get; init; }

    /// <inheritdoc />
    public string AuditAction => "server.enroll";

    /// <inheritdoc />
    public string AuditSubject => Slug;

    /// <inheritdoc />
    public string AuditDetail => LoginUser + "@" + PublicAddress + ":" + SshPort + " as " + TailnetHostname
                                 + (ExpectedHostKey is null ? "" : ", host key " + ExpectedHostKey);
}
