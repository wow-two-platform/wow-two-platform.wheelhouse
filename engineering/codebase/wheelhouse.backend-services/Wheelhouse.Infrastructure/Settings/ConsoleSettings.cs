namespace Wheelhouse.Infrastructure.Settings;

/// <summary>Holds what the host knows about the other console: where its twin answers, so the environment tag can
/// switch to it. A host fact, never committed — the public repository names no host.</summary>
public sealed record ConsoleSettings
{
    /// <summary>Gets or sets the other console's base URL, <c>https://host[:port]</c>; empty (the default) leaves the
    /// switch off and the tag as it was.</summary>
    public string PeerUrl { get; set; } = "";

    /// <summary>Gets whether the setting is empty or an https origin the console can switch to.</summary>
    public bool IsValid => Trimmed.Length == 0 || NormalizePeerUrl(Trimmed) is not null;

    /// <summary>Gets the peer's origin, lowercase and without a default port or trailing slash, or
    /// <see langword="null"/> when no peer is configured (or the setting is malformed, which fails the boot).</summary>
    public string? PeerOrigin => NormalizePeerUrl(Trimmed);

    private string Trimmed => PeerUrl.Trim();

    /// <summary>Normalizes a peer URL to <c>https://host[:port]</c>.</summary>
    /// <param name="value">The URL to normalize.</param>
    /// <returns>The origin, or <see langword="null"/> when the value is not an absolute https origin: another scheme, a
    /// path, a query, a fragment or credentials in the address.</returns>
    public static string? NormalizePeerUrl(string? value) =>
        McpSettings.NormalizeOrigin(value) is { } origin && origin.StartsWith("https://", StringComparison.Ordinal)
            ? origin
            : null;
}
