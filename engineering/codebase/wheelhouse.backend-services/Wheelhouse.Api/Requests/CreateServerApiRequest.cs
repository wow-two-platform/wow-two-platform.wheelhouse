using Wheelhouse.Domain.Servers.Enums;
using Wheelhouse.Domain.Servers.Models;

namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to add a server.</summary>
public sealed record CreateServerApiRequest
{
    /// <summary>Gets the name targets, vaults and the credential folders will know the server by.</summary>
    public required string Slug { get; init; }    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets who hosts the server.</summary>
    public required VpsProvider Provider { get; init; }

    /// <summary>Gets the host name or address SSH connects to.</summary>
    public required string Host { get; init; }

    /// <summary>Gets the provider's region.</summary>
    public required string Region { get; init; }

    /// <summary>Gets the user SSH signs in as.</summary>
    public string SshUser { get; init; } = "deploy";

    /// <summary>Gets the port SSH connects to.</summary>
    public int SshPort { get; init; } = 22;

    /// <summary>Gets how the server's ingress publishes sites.</summary>
    public ServerIngressValueObject Ingress { get; init; } = new();
}
