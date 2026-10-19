using System.Net;
using System.Net.Http.Headers;

namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Authorizes each call of the SDK GitHub client for the owner its path names: the owner's App installation or
/// the token file (<see cref="GitHubCredentials"/>), else what the client gave it (the signed-in owner's token), else
/// another installation's token for public repositories, else none. Notes every refusal and success per owner for the
/// Issues panel.</summary>
public sealed class GitHubCredentialHandler(
    GitHubCredentials credentials, GitHubAppTokens app, GitHubAccessMonitor monitor) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var owner = GitHubOwners.FromApiAddress(request.RequestUri);
        if (owner is null)
            return await base.SendAsync(request, cancellationToken);

        var credential = await credentials.ForOwnerAsync(owner, cancellationToken);
        if (credential is null && request.Headers.Authorization is null)
            credential = await credentials.BorrowAsync(cancellationToken);
        if (credential is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);

        var response = await base.SendAsync(request, cancellationToken);
        if (credential?.Source == GitHubCredentialSource.Borrowed
            && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            && !GitHubAccessMonitor.IsRateLimited(response))
        {
            // Should GitHub refuse another installation's token for a public repository, the call goes anonymous.
            response.Dispose();
            request.Headers.Authorization = null;
            credential = null;
            response = await base.SendAsync(request, cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized && credential?.Source == GitHubCredentialSource.App)
            app.Invalidate(owner);
        monitor.Observe(owner, request.RequestUri, response, request.Headers.Authorization is not null,
            installed: credential?.Source == GitHubCredentialSource.App);
        return response;
    }
}
