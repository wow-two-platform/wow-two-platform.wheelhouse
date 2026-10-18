using WoW.Two.Sdk.Beta.Be.Caching.Core;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;

namespace Wheelhouse.Infrastructure.Products;

/// <summary>Reads a product's repository through the SDK GitHub client with the signed-in owner's token, cached per
/// commit: the branch's newest commit, a commit's file list, and one file at a commit.</summary>
/// <remarks>
/// Every consumer of a product's repository goes through here — icons, the manifest — so they share one cache and one
/// rule: a commit's tree and files never change, so they stay cached until the branch moves on; only the branch head is
/// re-read, at most every <see cref="HeadFor"/>, so a pushed change shows soon after. What a file means, and which files
/// matter, is the caller's decision.
/// </remarks>
public sealed class GitHubRepositoryReader(IGitHubClient github, ICacheRepository cache)
{
    /// <summary>How long a branch head is trusted before it is read again; a pushed change shows within this time.</summary>
    public static readonly TimeSpan HeadFor = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan CommitFor = TimeSpan.FromDays(1);

    private static readonly CacheEntryOptions HeadEntry = new() { Expiration = HeadFor, LocalCacheExpiration = HeadFor };

    private static readonly CacheEntryOptions CommitEntry = new() { Expiration = CommitFor, LocalCacheExpiration = CommitFor };

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

    /// <summary>Names the cache entry that keeps a branch's head.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch.</param>
    public static string HeadKey(string repository, string branch) => $"github-head:{repository}@{branch}";

    /// <summary>Reads the branch's newest commit. The head is kept for <see cref="HeadFor"/>; one that could not be read
    /// is kept as long, so a repository without access costs no GitHub call per page.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch to read.</param>
    /// <param name="fresh">Whether to skip a kept head and read it now. A fresh read that fails leaves the kept head as it was.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The full commit SHA, or <c>null</c> when the branch cannot be read.</returns>
    public async Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct)
    {
        var key = HeadKey(repository, branch);
        if (!fresh)
        {
            var kept = await cache.GetOrCreateAsync(
                key, async token => await github.GetBranchHeadAsync(repository, branch, token), HeadEntry, ct);
            return kept.CommitSha;
        }

        var head = await github.GetBranchHeadAsync(repository, branch, ct);
        if (head.CommitSha is not null)
            await cache.SetAsync(key, head, HeadEntry, ct);
        return head.CommitSha;
    }

    /// <summary>Lists the blobs of a commit that a caller keeps, kept per commit under the caller's own purpose name.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="commit">A full commit SHA <see cref="HeadAsync"/> returned.</param>
    /// <param name="purpose">Names what the list is for; it keys the cache, so two callers keep their own lists.</param>
    /// <param name="keep">Decides from a blob's path and size, when GitHub gave one, whether to keep it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The kept paths, or <c>null</c> when GitHub gave no tree; no tree is retried on the next read.</returns>
    public async Task<IReadOnlyList<string>?> ListAsync(
        string repository, string commit, string purpose, Func<string, long?, bool> keep, CancellationToken ct) =>
        await cache.GetOrCreateUnlessTransientAsync<IReadOnlyList<string>?>(
            $"{purpose}:{repository}@{commit}",
            async token =>
            {
                var tree = await github.GetTreeAsync(repository, commit, token);
                if (tree.Outcome != RepositoryLookup.Found)
                    return CacheResult.Transient<IReadOnlyList<string>?>(null);

                return CacheResult.Keep<IReadOnlyList<string>?>(
                [
                    .. tree.Entries
                        .Where(entry => entry.Type == RepositoryTreeEntryType.Blob && keep(entry.Path, entry.Size))
                        .Select(entry => entry.Path),
                ]);
            },
            CommitEntry,
            ct);

    /// <summary>Reads one file as it was at a commit, at most <paramref name="maxBytes"/> of it.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="commit">A full commit SHA <see cref="HeadAsync"/> returned.</param>
    /// <param name="path">The file's repository path.</param>
    /// <param name="maxBytes">The largest file to read; a larger one is <see cref="FileRead.TooLarge"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The read; found, missing and too-large are final for the commit, any other outcome is retried.</returns>
    public async Task<RepositoryFile> ReadAsync(string repository, string commit, string path, int maxBytes, CancellationToken ct) =>
        await cache.GetOrCreateUnlessTransientAsync(
            $"github-file:{repository}@{commit}:{path}:{maxBytes}",
            async token =>
            {
                var file = await github.ReadFileAsync(repository, commit, path, maxBytes, token);
                // A verdict about a commit is final; no verdict is retried on the next read.
                return file.Outcome is FileRead.Found or FileRead.NotFound or FileRead.TooLarge
                    ? CacheResult.Keep(file)
                    : CacheResult.Transient(file);
            },
            CommitEntry,
            ct);
}
