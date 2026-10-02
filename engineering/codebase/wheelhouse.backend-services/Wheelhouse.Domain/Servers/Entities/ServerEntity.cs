using Wheelhouse.Domain.Servers.Enums;
using Wheelhouse.Domain.Servers.Models;
using WoW.Two.Sdk.Backend.Beta.Data.Abstractions;

namespace Wheelhouse.Domain.Servers.Entities;

/// <summary>Represents a host Wheelhouse deploys to over SSH. Its credentials never live here: the SSH identity and
/// the pinned host key are files the operator places on the control host.</summary>
public sealed record ServerEntity : IKeyedEntity<Guid>, IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "servers";

    /// <summary>Gets or sets the server's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the name targets, vaults and the credential folders know the server by; fixed once the
    /// server exists.</summary>
    public required string Slug { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets who hosts the server.</summary>
    public VpsProvider Provider { get; set; }

    /// <summary>Gets or sets the host name or address SSH connects to.</summary>
    public required string Host { get; set; }

    /// <summary>Gets or sets the provider's region, such as <c>hel1</c>.</summary>
    public required string Region { get; set; }

    /// <summary>Gets or sets the user SSH signs in as.</summary>
    public required string SshUser { get; set; }

    /// <summary>Gets or sets the port SSH connects to.</summary>
    public int SshPort { get; set; } = 22;

    /// <summary>Gets or sets how the server's ingress publishes sites.</summary>
    public required ServerIngressValueObject Ingress { get; set; }

    /// <summary>Gets or sets when the row was created; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row last changed; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
