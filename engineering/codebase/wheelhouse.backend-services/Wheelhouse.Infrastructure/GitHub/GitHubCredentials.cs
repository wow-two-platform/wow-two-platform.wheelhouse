using Wheelhouse.Infrastructure.Settings;

namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Chooses the credential a GitHub call for an owner carries: the GitHub App's installation token for that
/// owner, else the single fallback token file (<c>Deployment:GitHubTokenFile</c>), else none; <see cref="BorrowAsync"/>
/// lends another installation's token for public repositories when nothing better is at hand.</summary>
public sealed class GitHubCredentials(GitHubAppTokens app, DeploymentSettings settings)
{
    /// <summary>Gets the credential for a call addressing an owner.</summary>
    /// <param name="owner">The GitHub account the call addresses.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The credential, or <c>null</c> when none is at hand.</returns>
    public async Task<GitHubCredential?> ForOwnerAsync(string owner, CancellationToken ct)
    {
        if (await app.GetAsync(owner, ct) is { } installed)
            return new GitHubCredential(installed.Token, GitHubCredentialSource.App);
        return ReadTokenFile() is { } fallback ? new GitHubCredential(fallback, GitHubCredentialSource.TokenFile) : null;
    }

    /// <summary>Gets another installation's token for public repositories of an owner without the App.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The credential, or <c>null</c> when the App is off or installed nowhere.</returns>
    public async Task<GitHubCredential?> BorrowAsync(CancellationToken ct) =>
        await app.BorrowAsync(ct) is { } lent ? new GitHubCredential(lent.Token, GitHubCredentialSource.Borrowed) : null;

    /// <summary>Reads the fallback token: one line, no spaces. A missing, unreadable or malformed file counts as none.</summary>
    /// <returns>The token, or <c>null</c>.</returns>
    public string? ReadTokenFile()
    {
        if (settings.GitHubTokenFile.Length == 0)
            return null;
        try
        {
            var value = File.ReadAllText(settings.GitHubTokenFile).Trim();
            return value.Length == 0 || value.Any(char.IsWhiteSpace) ? null : value;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
