using System.Net;
using System.Text;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Answers GitHub API calls from a script keyed by method and path, with full control of status and headers,
/// and records every call with the credential it carried.</summary>
/// <remarks>Unscripted calls answer 404. Scripts may change between calls, so a test can move GitHub's answers on.</remarks>
public sealed class FakeGitHubApi : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<FakeGitHubCall> _calls = [];

    /// <summary>Gets the answers by <c>"{METHOD} {path}"</c>, the path without its leading slash or query.</summary>
    public Dictionary<string, Func<HttpResponseMessage>> Answers { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets every call received, in order.</summary>
    public IReadOnlyList<FakeGitHubCall> Calls
    {
        get
        {
            lock (_gate)
                return [.. _calls];
        }
    }

    /// <summary>Gets how many calls asked for one <c>"{METHOD} {path}"</c>.</summary>
    /// <param name="key">The method and path.</param>
    public int CountOf(string key) => Calls.Count(call => call.Key == key);

    /// <summary>Builds a JSON answer.</summary>
    /// <param name="status">The status.</param>
    /// <param name="json">The body.</param>
    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Builds an installation token answer.</summary>
    /// <param name="token">The token.</param>
    /// <param name="expiresAt">When it expires.</param>
    public static HttpResponseMessage Token(string token, DateTimeOffset expiresAt) =>
        Json(HttpStatusCode.Created, $$$"""{"token":"{{{token}}}","expires_at":"{{{expiresAt:O}}}","permissions":{}}""");

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = request.Method.Method + " " + request.RequestUri!.AbsolutePath.TrimStart('/');
        lock (_gate)
            _calls.Add(new FakeGitHubCall(key, request.RequestUri.Host, request.Headers.Authorization?.ToString()));
        var response = Answers.TryGetValue(key, out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NotFound);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

