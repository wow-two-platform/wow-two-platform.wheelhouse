namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents a server's ingress in the runner's inventory.</summary>
public sealed record SnapshotIngressModel
{
    /// <summary>Gets the URL scheme sites answer on.</summary>
    public required string Scheme { get; init; }

    /// <summary>Gets the port sites answer on, or <c>null</c>.</summary>
    public int? Port { get; init; }

    /// <summary>Gets the entry points public sites attach to.</summary>
    public required IReadOnlyList<string> EntryPoints { get; init; }

    /// <summary>Gets the entry points private sites attach to.</summary>
    public required IReadOnlyList<string> PrivateEntryPoints { get; init; }

    /// <summary>Gets the certificate resolver, or <c>null</c>.</summary>
    public string? CertResolver { get; init; }

    /// <summary>Gets the host pattern for unnamed sites, or <c>null</c>.</summary>
    public string? Pattern { get; init; }

    /// <summary>Gets where public sites are requested from, or <c>null</c>.</summary>
    public string? Probe { get; init; }

    /// <summary>Gets where private sites are requested from, or <c>null</c>.</summary>
    public string? PrivateProbe { get; init; }
}
