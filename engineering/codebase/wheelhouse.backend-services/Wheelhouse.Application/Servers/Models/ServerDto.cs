using Wheelhouse.Domain.Servers.Enums;

namespace Wheelhouse.Application.Servers.Models;

/// <summary>Represents a host Wheelhouse deploys to, as the operator edits it; never its credentials.</summary>
public sealed record ServerDto
{
    /// <summary>Gets the name targets, vaults and the credential folders know the server by.</summary>
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
    public required ServerIngressDto Ingress { get; init; }
}
