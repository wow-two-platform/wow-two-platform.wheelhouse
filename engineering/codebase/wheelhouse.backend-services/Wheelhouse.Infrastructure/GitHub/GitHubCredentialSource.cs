namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Refers to where a GitHub credential came from.</summary>
public enum GitHubCredentialSource
{
    /// <summary>The GitHub App's installation token for the owner.</summary>
    App,

    /// <summary>The single mounted fallback token (<c>Deployment:GitHubTokenFile</c>).</summary>
    TokenFile,

    /// <summary>Another owner's installation token, lent for public repositories of an owner without the App.</summary>
    Borrowed,
}
