using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using Wheelhouse.Domain.Domains.Enums;

namespace Wheelhouse.Domain.Domains.Entities;

/// <summary>A domain name bought/managed through Wheelhouse and assigned to a product environment.</summary>
public sealed class ManagedDomain : IKeyedEntity<Guid>
{
    /// <summary>Gets the domain's unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets or sets the fully-qualified name (e.g. <c>example.com</c>).</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the registrar the domain was bought from (e.g. <c>porkbun</c>).</summary>
    public string? Registrar { get; set; }

    /// <summary>Gets or sets the DNS provider managing records (e.g. <c>cloudflare</c>).</summary>
    public string? DnsProvider { get; set; }

    /// <summary>Gets or sets the product this domain is assigned to.</summary>
    public Guid? AssignedProductId { get; set; }

    /// <summary>Gets or sets the lifecycle state.</summary>
    public DomainStatus Status { get; set; } = DomainStatus.Available;

    /// <summary>Gets or sets the UTC purchase instant.</summary>
    public DateTimeOffset? PurchasedAtUtc { get; set; }

    /// <summary>Gets or sets the UTC expiry instant.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>Gets or sets a value indicating whether auto-renew is enabled at the registrar.</summary>
    public bool AutoRenew { get; set; }
}
