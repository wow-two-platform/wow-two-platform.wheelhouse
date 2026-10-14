using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products;
using WoW.Two.Sdk.Backend.Beta.Integrations;

namespace Wheelhouse.Infrastructure.Products;

/// <summary>Reads a product's icon files from its GitHub repository with the signed-in operator's token, cached per
/// commit.</summary>
/// <remarks>
/// Three REST reads: the branch's newest commit, that commit's recursive tree kept to the files an icon can come from,
/// and a chosen file's raw bytes. A commit's tree and files never change, so they stay cached until the branch moves on;
/// only the branch head is re-read, every few minutes, so a pushed icon shows soon after. Which file is chosen is the
/// icon convention's decision, made by the caller. Inline until the SDK GitHub client reads repository trees and files.
/// </remarks>
public sealed partial class GitHubProductIconSource(
    IHttpClientFactory clients,
    IAccessTokenService tokens,
    IMemoryCache cache,
    ILogger<GitHubProductIconSource> logger) : IProductIconSource
{
    /// <summary>The named client, configured for the GitHub REST API.</summary>
    public const string ClientName = "github-files";

    /// <summary>How long a branch head is trusted before it is read again; a pushed icon shows within this time.</summary>
    public static readonly TimeSpan HeadFor = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan CommitFor = TimeSpan.FromDays(1);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAsync(string repository, string branch, CancellationToken ct)
    {
        var token = await tokens.GetAccessTokenAsync(ct);
        var commit = await HeadAsync(repository, branch, token, ct);
        if (commit is null)
            return [];

        var key = $"product-icon-paths:{repository}@{commit}";
        if (cache.TryGetValue(key, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        var paths = await ListTreeAsync(repository, commit, token, ct);
        if (paths is null)
            return [];
        cache.Set(key, paths, CommitFor);
        return paths;
    }

    /// <inheritdoc />
    public async Task<ProductIconImage?> ReadAsync(string repository, string branch, string path, CancellationToken ct)
    {
        var token = await tokens.GetAccessTokenAsync(ct);
        var commit = await HeadAsync(repository, branch, token, ct);
        if (commit is null)
            return null;

        var key = $"product-icon:{repository}@{commit}:{path}";
        if (cache.TryGetValue(key, out ProductIconImage? cached))
            return cached;

        var (icon, answered) = await ReadFileAsync(repository, commit, path, token, ct);
        if (answered)
            cache.Set(key, icon, CommitFor);
        return icon;
    }

    /// <summary>Reads the branch's newest commit, kept for <see cref="HeadFor"/>; an unreadable branch is kept as
    /// long, so a product without access shows its monogram without a GitHub call per page.</summary>
    private async Task<string?> HeadAsync(string repository, string branch, string? token, CancellationToken ct)
    {
        var key = $"product-icon-head:{repository}@{branch}";
        if (cache.TryGetValue(key, out string? cached))
            return cached;

        string? commit = null;
        try
        {
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

        cache.Set(key, commit, HeadFor);
        return commit;
    }

    /// <summary>Lists the commit's icon candidates, or <see langword="null"/> when GitHub gave no tree.</summary>
    private async Task<IReadOnlyList<string>?> ListTreeAsync(string repository, string commit, string? token, CancellationToken ct)
    {
        try
        {
            using var tree = await GetJsonAsync(
                clients.CreateClient(ClientName), token, $"repos/{repository}/git/trees/{commit}?recursive=1", ct);
            if (tree is null || !tree.RootElement.TryGetProperty("tree", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return null;

            return
            [
                .. entries.EnumerateArray()
                    .Where(entry => entry.TryGetProperty("type", out var type) && type.GetString() == "blob"
                        && (!entry.TryGetProperty("size", out var size) || size.GetInt64() <= ProductIconPaths.MaxBytes)
                        && entry.TryGetProperty("path", out var item) && item.ValueKind == JsonValueKind.String)
                    .Select(entry => entry.GetProperty("path").GetString()!)
                    .Where(ProductIconPaths.IsCandidate),
            ];
        }
        catch (Exception exception) when (IsUnreadable(exception, ct))
        {
            LogUnreadable(logger, repository, exception.GetType().Name);
            return null;
        }
    }

    /// <summary>Reads one file at a commit; <c>Answered</c> is false when GitHub gave no verdict, so nothing is cached.</summary>
    private async Task<(ProductIconImage? Icon, bool Answered)> ReadFileAsync(
        string repository, string commit, string path, string? token, CancellationToken ct)
    {
        try
        {
            using var request = Request(
                $"repos/{repository}/contents/{EscapePath(path)}?ref={commit}", token, "application/vnd.github.raw");
            using var response = await clients.CreateClient(ClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return (null, response.StatusCode == System.Net.HttpStatusCode.NotFound);

            var bytes = await ReadBoundedAsync(response.Content, ct);
            return (bytes is null ? null : new ProductIconImage(bytes, ProductIconPaths.ContentType(path), path), true);
        }
        catch (Exception exception) when (IsUnreadable(exception, ct))
        {
            LogUnreadable(logger, repository, exception.GetType().Name);
            return (null, false);
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

    /// <summary>Reads at most <see cref="ProductIconPaths.MaxBytes"/>; a larger file is no icon.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > ProductIconPaths.MaxBytes)
                return null;
        }

        return buffer.ToArray();
    }

    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitPattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "The icon of {Repository} could not be read ({Reason}).")]
    private static partial void LogUnreadable(ILogger logger, string repository, string reason);
}
