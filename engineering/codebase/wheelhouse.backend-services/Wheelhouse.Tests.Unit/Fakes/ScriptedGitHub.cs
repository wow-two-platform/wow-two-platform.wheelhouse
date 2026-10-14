using System.Net;
using System.Text;
using WoW.Two.Sdk.Backend.Beta.Integrations;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>A GitHub that answers the REST requests a test scripted and records every request path, so a test can tell
/// what was asked and how often. A path nobody scripted is not found.</summary>
public sealed class ScriptedGitHub : HttpMessageHandler
{
    /// <summary>Gets the JSON answers, by request path and query without the leading slash.</summary>
    public Dictionary<string, string> Json { get; } = [];

    /// <summary>Gets the raw answers, by request path and query without the leading slash.</summary>
    public Dictionary<string, byte[]> Raw { get; } = [];

    /// <summary>Gets the paths that answer a failure status instead of what was scripted.</summary>
    public Dictionary<string, HttpStatusCode> Failures { get; } = [];

    /// <summary>Gets every request path and query, without the leading slash, in the order asked.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Gets the <c>Authorization</c> header of each request, in the order asked; empty text when it had none.</summary>
    public List<string> Authorizations { get; } = [];

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = request.RequestUri!.PathAndQuery.TrimStart('/');
        Requests.Add(key);
        Authorizations.Add(request.Headers.Authorization?.ToString() ?? "");
        if (Failures.TryGetValue(key, out var failure))
            return Task.FromResult(new HttpResponseMessage(failure));
        if (Json.TryGetValue(key, out var json))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        return Task.FromResult(Raw.TryGetValue(key, out var bytes)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>Hands every client name the same <see cref="HttpMessageHandler"/>, aimed at the GitHub API address.</summary>
/// <param name="handler">The handler every client uses.</param>
public sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    /// <inheritdoc />
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
    {
        BaseAddress = new Uri("https://api.github.com/"),
    };
}

/// <summary>An OAuth token source that always gives one token.</summary>
/// <param name="token">The token to give.</param>
public sealed class FixedToken(string token) : IAccessTokenService
{
    /// <inheritdoc />
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(token);
}
