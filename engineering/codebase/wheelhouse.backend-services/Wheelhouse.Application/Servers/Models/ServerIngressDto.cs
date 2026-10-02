namespace Wheelhouse.Application.Servers.Models;

/// <summary>Represents how a server's ingress publishes sites.</summary>
public sealed record ServerIngressDto
{
    /// <summary>Gets the URL scheme sites answer on.</summary>
    public required string Scheme { get; init; }

    /// <summary>Gets the port sites answer on, or <c>null</c> for the scheme's default.</summary>
    public int? Port { get; init; }

    /// <summary>Gets the Traefik entry points public sites attach to.</summary>
    public required IReadOnlyList<string> EntryPoints { get; init; }

    /// <summary>Gets the Traefik entry points private sites attach to.</summary>
    public required IReadOnlyList<string> PrivateEntryPoints { get; init; }

    /// <summary>Gets the Traefik certificate resolver, or <c>null</c>.</summary>
    public string? CertResolver { get; init; }

    /// <summary>Gets the host pattern for sites a target leaves unnamed, or <c>null</c>.</summary>
    public string? Pattern { get; init; }

    /// <summary>Gets where the runner requests public sites from, or <c>null</c> for loopback.</summary>
    public string? Probe { get; init; }

    /// <summary>Gets where the runner requests private sites from, or <c>null</c>.</summary>
    public string? PrivateProbe { get; init; }
}
