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

    /// <summary>The session cookie's name for prod and for a local run.</summary>
    public const string SessionCookie = ".wheelhouse.auth";

    /// <summary>Names the session cookie for the environment a console serves. Cookies ignore the port, so dev and prod
    /// on one host name would overwrite each other's sign-in: prod keeps the plain name, any other deployed environment
    /// adds its own.</summary>
    /// <param name="deployEnvironment">The runner's <c>DEPLOY_ENVIRONMENT</c>; empty on a local run.</param>
    /// <returns>The cookie name, such as <c>.wheelhouse.auth</c> or <c>.wheelhouse.auth.dev</c>.</returns>
    public static string SessionCookieName(string? deployEnvironment)
    {
        var environment = deployEnvironment?.Trim().ToLowerInvariant() ?? "";
        if (environment.Length == 0 || environment == "prod" || !environment.All(char.IsAsciiLetterOrDigit))
            return SessionCookie;
        return SessionCookie + "." + environment;
    }

    /// <summary>Normalizes a peer URL to <c>https://host[:port]</c>.</summary>
    /// <param name="value">The URL to normalize.</param>
    /// <returns>The origin, or <see langword="null"/> when the value is not an absolute https origin: another scheme, a
    /// path, a query, a fragment or credentials in the address.</returns>
    public static string? NormalizePeerUrl(string? value) =>
        McpSettings.NormalizeOrigin(value) is { } origin && origin.StartsWith("https://", StringComparison.Ordinal)
            ? origin
            : null;
}
