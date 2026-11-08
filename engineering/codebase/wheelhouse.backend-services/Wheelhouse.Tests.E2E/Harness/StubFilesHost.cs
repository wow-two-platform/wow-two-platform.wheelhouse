using System.Net;
using System.Text;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>A fake Files host: answers <c>GET /health</c>, <c>POST /imports</c> and <c>POST /imports/{id}/tokens</c> and records each request
/// with the operator key header it carried.</summary>
public sealed class StubFilesHost : HttpMessageHandler
{
    /// <summary>The token the fake mints.</summary>
    public const string Token = "import-token-SECRET-123";

    /// <summary>The import the fake opens.</summary>
    public const string ImportId = "imp-7";

    /// <summary>Every request, in order: method, URL, the <c>X-Api-Key</c> header and the body.</summary>
    public List<(string Method, string Url, string? Key, string Body)> Requests { get; } = [];

    /// <summary>Every health preflight, in order: the URL and whether it carried an <c>X-Api-Key</c> header (it must not).</summary>
    public List<(string Url, bool Keyed)> HealthChecks { get; } = [];

    /// <summary>The status the health preflight is answered with.</summary>
    public HttpStatusCode HealthStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>When set, the health preflight times out, as the client's five second limit would end it.</summary>
    public bool HealthTimesOut { get; set; }

    /// <summary>When set, every import request is answered with this status and a body that must never surface.</summary>
    public HttpStatusCode? Failure { get; set; }

    /// <summary>Clears the recorded requests and the failure.</summary>
    public void Reset()
    {
        Requests.Clear();
        HealthChecks.Clear();
        HealthStatus = HttpStatusCode.OK;
        HealthTimesOut = false;
        Failure = null;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/health", StringComparison.Ordinal))
        {
            HealthChecks.Add((request.RequestUri.ToString(), request.Headers.Contains("X-Api-Key")));
            if (HealthTimesOut)
                throw new TaskCanceledException("The request timed out.", new TimeoutException());
            return new HttpResponseMessage(HealthStatus) { Content = new StringContent("secret-body-from-files") };
        }

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method.Method, request.RequestUri!.ToString(),
            request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null, body));
        if (Failure is { } status)
            return new HttpResponseMessage(status) { Content = new StringContent("secret-body-from-files") };
        var path = request.RequestUri.AbsolutePath;
        var json = path.EndsWith("/tokens", StringComparison.Ordinal)
            ? $$"""{ "token": "{{Token}}", "expiresAt": "2026-11-08T20:00:00+00:00" }"""
            : $$"""{ "id": "{{ImportId}}", "token": "{{Token}}", "expiresAt": "2026-11-08T20:00:00+00:00" }""";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // Shared by every test: the client factory must not dispose it.
    }
}
