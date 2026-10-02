using Wheelhouse.Domain.Servers.Enums;

namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents a server in the runner's inventory; its credentials stay files on the control host.</summary>
public sealed record SnapshotServerModel
{
    /// <summary>Gets the server's slug.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets who hosts the server.</summary>
    public required VpsProvider Provider { get; init; }

    /// <summary>Gets the host SSH connects to.</summary>
    public required string Host { get; init; }

    /// <summary>Gets the provider's region.</summary>
    public required string Region { get; init; }

    /// <summary>Gets the user SSH signs in as.</summary>
    public required string SshUser { get; init; }

    /// <summary>Gets the port SSH connects to.</summary>
    public required int SshPort { get; init; }

    /// <summary>Gets how the server's ingress publishes sites.</summary>
    public required SnapshotIngressModel Ingress { get; init; }
}
