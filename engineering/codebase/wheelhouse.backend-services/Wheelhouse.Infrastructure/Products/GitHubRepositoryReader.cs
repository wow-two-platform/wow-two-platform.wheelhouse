using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Backend.Beta.Integrations;

namespace Wheelhouse.Infrastructure.Products;

/// <summary>Reads a product's repository on GitHub with the signed-in operator's token, cached per commit: the branch's
/// newest commit, a commit's file list, and one file at a commit.</summary>
/// <remarks>
/// Every consumer of a product's repository goes through here — icons, the manifest — so they share one cache and one
/// rule: a commit's tree and files never change, so they stay cached until the branch moves on; only the branch head is
/// re-read, at most every <see cref="HeadFor"/>, so a pushed change shows soon after. What a file means, and which files
/// matter, is the caller's decision. Inline until the SDK GitHub client reads repository trees and files.
/// </remarks>
public sealed partial class GitHubRepositoryReader(
    IHttpClientFactory clients,
    IAccessTokenService tokens,
    IMemoryCache cache,
    ILogger<GitHubRepositoryReader> logger)
{
    /// <summary>The named client, configured for the GitHub REST API.</summary>
    public const string ClientName = "github-files";

    /// <summary>How long a branch head is trusted before it is read again; a pushed change shows within this time.</summary>
    public static readonly TimeSpan HeadFor = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan CommitFor = TimeSpan.FromDays(1);

    /// <summary>Reads the branch's newest commit. The head is kept for <see cref="HeadFor"/>; one that could not be read
    /// is kept as long, so a repository without access costs no GitHub call per page.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch to read.</param>
    /// <param name="fresh">Whether to skip a kept head and read it now. A fresh read that fails leaves the kept head as it was.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The full commit SHA, or <c>null</c> when the branch cannot be read.</returns>
    public async Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct)
    {
        var key = $"github-head:{repository}@{branch}";
        if (!fresh && cache.TryGetValue(key, out string? cached))
            return cached;

        string? commit = null;
        try
        {
            var token = await tokens.GetAccessTokenAsync(ct);
            using var request = Request(
                $"repos/{repository}/commits/{Uri.EscapeDataString(branch)}", token, "application/vnd.github.sha");
            using var response = await clients.CreateClient(ClientName).SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var text = (await response.Content.ReadAsStringAsync(ct)).Trim();
                commit = CommitPattern().IsMatch(text) ? text : null;
            }
        }
        catch (Exception exception) when (IsUnreadable(exception, ct))
        {
            LogUnreadable(logger, repository, exception.GetType().Name);
        }

        if (commit is not null || !fresh)
            cache.Set(key, commit, HeadFor);
        return commit;
    }

    /// <summary>Lists the blobs of a commit that a caller keeps, kept per commit under the caller's own purpose name.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="commit">A full commit SHA <see cref="HeadAsync"/> returned.</param>
    /// <param name="purpose">Names what the list is for; it keys the cache, so two callers keep their own lists.</param>
    /// <param name="keep">Decides from a blob's path and size, when GitHub gave one, whether to keep it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The kept paths, or <c>null</c> when GitHub gave no tree.</returns>
    public async Task<IReadOnlyList<string>?> ListAsync(
        string repository, string commit, string purpose, Func<string, long?, bool> keep, CancellationToken ct)
    {
        var key = $"{purpose}:{repository}@{commit}";
        if (cache.TryGetValue(key, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        var paths = await ListTreeAsync(repository, commit, keep, ct);
        if (paths is not null)
            cache.Set(key, paths, CommitFor);
        return paths;
    }

    /// <summary>Reads one file as it was at a commit, at most <paramref name="maxBytes"/> of it.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="commit">A full commit SHA <see cref="HeadAsync"/> returned.</param>
    /// <param name="path">The file's repository path.</param>
    /// <param name="maxBytes">The largest file to read; a larger one is <see cref="RepositoryFileStatus.TooLarge"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    public async Task<RepositoryFile> ReadAsync(string repository, string commit, string path, int maxBytes, CancellationToken ct)
    {
        var key = $"github-file:{repository}@{commit}:{path}:{maxBytes}";
        if (cache.TryGetValue(key, out RepositoryFile? cached) && cached is not null)
            return cached;

        var file = await ReadFileAsync(repository, commit, path, maxBytes, ct);
        // A verdict about a commit is final; no verdict is retried on the next read.
        if (file.Status != RepositoryFileStatus.Unreadable)
            cache.Set(key, file, CommitFor);
        return file;
    }

    private async Task<IReadOnlyList<string>?> ListTreeAsync(
        string repository, string commit, Func<string, long?, bool> keep, CancellationToken ct)
    {
        try
        {
            var token = await tokens.GetAccessTokenAsync(ct);
            using var tree = await GetJsonAsync(
                clients.CreateClient(ClientName), token, $"repos/{repository}/git/trees/{commit}?recursive=1", ct);
            if (tree is null || !tree.RootElement.TryGetProperty("tree", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return null;

            return
            [
                .. entries.EnumerateArray()
                    .Where(entry => entry.TryGetProperty("type", out var type) && type.GetString() == "blob"
                        && entry.TryGetProperty("path", out var item) && item.ValueKind == JsonValueKind.String
                        && keep(item.GetString()!, entry.TryGetProperty("size", out var size) ? size.GetInt64() : null))
                    .Select(entry => entry.GetProperty("path").GetString()!),
            ];
        }
        catch (Exception exception) when (IsUnreadable(exception, ct))
        {
            LogUnreadable(logger, repository, exception.GetType().Name);
            return null;
        }
    }

    private async Task<RepositoryFile> ReadFileAsync(string repository, string commit, string path, int maxBytes, CancellationToken ct)
    {
        try
        {
            var token = await tokens.GetAccessTokenAsync(ct);
            using var request = Request(
                $"repos/{repository}/contents/{EscapePath(path)}?ref={commit}", token, "application/vnd.github.raw");
            using var response = await clients.CreateClient(ClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return new RepositoryFile(
                    response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? RepositoryFileStatus.Missing
                        : RepositoryFileStatus.Unreadable);

            var bytes = await ReadBoundedAsync(response.Content, maxBytes, ct);
            return bytes is null
                ? new RepositoryFile(RepositoryFileStatus.TooLarge)
                : new RepositoryFile(RepositoryFileStatus.Found, bytes);
        }
        catch (Exception exception) when (IsUnreadable(exception, ct))
        {
            LogUnreadable(logger, repository, exception.GetType().Name);
            return new RepositoryFile(RepositoryFileStatus.Unreadable);
        }
    }

    private static bool IsUnreadable(Exception exception, CancellationToken ct) =>
        (exception is HttpRequestException or JsonException or TaskCanceledException) && !ct.IsCancellationRequested;

    private static async Task<JsonDocument?> GetJsonAsync(HttpClient client, string? token, string path, CancellationToken ct)
    {
        using var request = Request(path, token, "application/vnd.github+json");
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static HttpRequestMessage Request(string path, string? token, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>Reads at most <paramref name="maxBytes"/>; <c>null</c> means the file is larger.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes)
                return null;
        }

        return buffer.ToArray();
    }

    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitPattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "The repository {Repository} could not be read ({Reason}).")]
    private static partial void LogUnreadable(ILogger logger, string repository, string reason);
}
