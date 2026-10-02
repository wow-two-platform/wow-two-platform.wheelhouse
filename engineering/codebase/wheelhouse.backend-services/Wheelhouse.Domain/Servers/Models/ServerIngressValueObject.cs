namespace Wheelhouse.Domain.Servers.Models;

/// <summary>Represents how a server's ingress publishes sites: the URL scheme and port, the Traefik entry points and
/// the host pattern for sites a target leaves unnamed.</summary>
public sealed record ServerIngressValueObject
{
    /// <summary>Gets the URL scheme sites answer on: <c>https</c>, or <c>http</c> on the local server.</summary>
    public string Scheme { get; init; } = "https";

    /// <summary>Gets the port sites answer on, or <c>null</c> for the scheme's default.</summary>
    public int? Port { get; init; }

    /// <summary>Gets the Traefik entry points public sites attach to.</summary>
    public IReadOnlyList<string> EntryPoints { get; init; } = ["websecure"];

    /// <summary>Gets the Traefik entry points private sites attach to; none publishes no private site.</summary>
    public IReadOnlyList<string> PrivateEntryPoints { get; init; } = [];

    /// <summary>Gets the Traefik certificate resolver, or <c>null</c> where sites carry no certificate.</summary>
    public string? CertResolver { get; init; } = "letsencrypt";

    /// <summary>Gets the host pattern for sites a target leaves unnamed, such as
    /// <c>{site}-{product}.{environment}.preview.example</c>; prod on a VPS always names its own hosts.</summary>
    public string? Pattern { get; init; }

    /// <summary>Gets where the runner requests public sites from on the host, or <c>null</c> for loopback.</summary>
    public string? Probe { get; init; }

    /// <summary>Gets where the runner requests private sites from, or <c>null</c> to leave them unprobed.</summary>
    public string? PrivateProbe { get; init; }
}
