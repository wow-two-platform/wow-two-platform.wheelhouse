using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products;
using WoW.Two.Sdk.Backend.Beta.Integrations;

namespace Wheelhouse.Infrastructure.Products;

/// <summary>Reads a product's icon files from its GitHub repository with the signed-in operator's token, cached per
/// repository and branch.</summary>
/// <remarks>
/// Two REST reads: the branch's recursive tree, kept to the files an icon can come from, and a chosen file's raw bytes.
/// Which file is chosen is the icon convention's decision, made by the caller. A repository without icons, or one that
/// cannot be read, is cached briefly, so the product shows its monogram without a GitHub call per page. Inline until
/// the SDK GitHub client reads repository trees and files.
/// </remarks>
public sealed partial class GitHubProductIconSource(
    IHttpClientFactory clients,
    IAccessTokenService tokens,
    IMemoryCache cache,
    ILogger<GitHubProductIconSource> logger) : IProductIconSource
{
    /// <summary>The named client, configured for the GitHub REST API.</summary>
    public const string ClientName = "github-files";

    private static readonly TimeSpan FoundFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan MissingFor = TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAsync(string repository, string branch, CancellationToken ct)
    {
        var key = $"product-icon-paths:{repository}@{branch}";
        if (cache.TryGetValue(key, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        var token = await tokens.GetAccessTokenAsync(ct);
        var paths = await ListTreeAsync(repository, branch, token, ct);
        // Without the operator's token only public repositories answer, so a miss then is no verdict on the repository.
        if (paths.Count > 0 || !string.IsNullOrWhiteSpace(token))
            cache.Set(key, paths, paths.Count == 0 ? MissingFor : FoundFor);
        return paths;
    }

    /// <inheritdoc />
    public async Task<ProductIconImage?> ReadAsync(string repository, string branch, string path, CancellationToken ct)
    {
        var key = $"product-icon:{repository}@{branch}:{path}";
        if (cache.TryGetValue(key, out ProductIconImage? cached))
            return cached;

        var token = await tokens.GetAccessTokenAsync(ct);
        var icon = await ReadFileAsync(repository, branch, path, token, ct);
        if (icon is not null || !string.IsNullOrWhiteSpace(token))
            cache.Set(key, icon, icon is null ? MissingFor : FoundFor);
        return icon;
    }

    private async Task<IReadOnlyList<string>> ListTreeAsync(string repository, string branch, string? token, CancellationToken ct)
    {
        try
        {
            using var tree = await GetJsonAsync(
                clients.CreateClient(ClientName), token,
                $"repos/{repository}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1", ct);
            if (tree is null || !tree.RootElement.TryGetProperty("tree", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return [];

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
            return [];
        }
    }

    private async Task<ProductIconImage?> ReadFileAsync(string repository, string branch, string path, string? token, CancellationToken ct)
    {
        try
        {
            using var request = Request(
                $"repos/{repository}/contents/{EscapePath(path)}?ref={Uri.EscapeDataString(branch)}", token, "application/vnd.github.raw");
            using var response = await clients.CreateClient(ClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await ReadBoundedAsync(response.Content, ct);
            return bytes is null ? null : new ProductIconImage(bytes, ProductIconPaths.ContentType(path), path);
        }
        catch (Exception exception) when (IsUnreadable(exception, ct))
        {
            LogUnreadable(logger, repository, exception.GetType().Name);
            return null;
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

    [LoggerMessage(Level = LogLevel.Information, Message = "The icon of {Repository} could not be read ({Reason}).")]
    private static partial void LogUnreadable(ILogger logger, string repository, string reason);
}
