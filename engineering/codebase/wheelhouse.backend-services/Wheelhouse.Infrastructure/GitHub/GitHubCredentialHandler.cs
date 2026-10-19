using System.Net;
using System.Net.Http.Headers;

namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Authorizes each call of the SDK GitHub client for the owner its path names (<see cref="GitHubCredentials"/>);
/// without a credential the call keeps what the client gave it, the signed-in owner's token or none. Notes every
/// refusal and success per owner for the Issues panel.</summary>
public sealed class GitHubCredentialHandler(
    GitHubCredentials credentials, GitHubAppTokens app, GitHubAccessMonitor monitor) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var owner = GitHubOwners.FromApiAddress(request.RequestUri);
        var credential = owner is null ? null : await credentials.ForOwnerAsync(owner, cancellationToken);
        if (credential is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);

        var response = await base.SendAsync(request, cancellationToken);
        if (owner is null)
            return response;

        if (response.StatusCode == HttpStatusCode.Unauthorized && credential?.Source == GitHubCredentialSource.App)
            app.Invalidate(owner);
        monitor.Observe(owner, response, request.Headers.Authorization is not null);
        return response;
    }
}
