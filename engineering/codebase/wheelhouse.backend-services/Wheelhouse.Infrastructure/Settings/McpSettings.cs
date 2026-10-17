namespace Wheelhouse.Infrastructure.Settings;

/// <summary>Holds which browser origins may call the private MCP endpoint besides the console's own.</summary>
/// <remarks>
/// The Streamable HTTP transport requires the server to refuse a foreign <c>Origin</c>, so a page that rebinds a name
/// to the console's address cannot drive it. A request without <c>Origin</c>, as a CLI client sends, is accepted.
/// </remarks>
public sealed record McpSettings
{
    /// <summary>Gets or sets the extra browser origins, each <c>scheme://host[:port]</c> over http or https; empty by
    /// default, so only the console's own origin is accepted.</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Gets whether a request's origin may call the endpoint.</summary>
    /// <param name="origin">The request's <c>Origin</c> header value.</param>
    /// <param name="ownOrigin">The console's own origin, as the request reached it.</param>
    /// <returns><see langword="true"/> for the console's own origin or a configured one.</returns>
    public bool Allows(string origin, string ownOrigin)
    {
        var normalized = NormalizeOrigin(origin);
        return normalized is not null
            && (normalized == NormalizeOrigin(ownOrigin) || AllowedOrigins.Any(allowed => NormalizeOrigin(allowed) == normalized));
    }

    /// <summary>Normalizes an origin to <c>scheme://host[:port]</c>, lowercase and without a default port.</summary>
    /// <param name="value">The origin to normalize.</param>
    /// <returns>The normalized origin, or <see langword="null"/> when the value is not an http or https origin, such as
    /// <c>null</c>, a path or a query.</returns>
    public static string? NormalizeOrigin(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.UserInfo.Length == 0 && uri.PathAndQuery == "/" && uri.Fragment.Length == 0
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
}
