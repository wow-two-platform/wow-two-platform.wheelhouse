using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;

namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Holds the settings Wheelhouse's repository reads run with, through the SDK GitHub client and its commit-keyed
/// <see cref="IGitHubRepositoryReader"/>: icons, the catalog, each product's <c>deploy.yml</c> and identity.</summary>
public static class GitHubReadSettings
{
    /// <summary>How long a branch head is trusted before it is read again; a pushed change shows within this time.</summary>
    public static readonly TimeSpan HeadFor = TimeSpan.FromMinutes(5);

    /// <summary>How long a commit's tree and files stay cached; a commit never changes.</summary>
    public static readonly TimeSpan CommitFor = TimeSpan.FromDays(1);

    /// <summary>Applies the client settings the reads run with: Wheelhouse's user agent, the pinned REST API version, a
    /// ten-second timeout, and an anonymous call when no owner's token is at hand, which sees public repositories only.</summary>
    /// <param name="options">The SDK GitHub client options to set.</param>
    public static void ConfigureClient(GitHubIntegrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.UserAgent = "Wheelhouse";
        options.ApiVersion = "2022-11-28";
        options.Timeout = TimeSpan.FromSeconds(10);
        options.AllowAnonymousCalls = true;
    }

    /// <summary>Applies how long the reader keeps a branch head and what it read at a commit; the host section
    /// <c>Integrations:GitHub:Reader</c> may override either.</summary>
    /// <param name="options">The SDK reader options to set.</param>
    public static void ConfigureReader(GitHubRepositoryReaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.HeadFor = HeadFor;
        options.CommitFor = CommitFor;
    }
}
