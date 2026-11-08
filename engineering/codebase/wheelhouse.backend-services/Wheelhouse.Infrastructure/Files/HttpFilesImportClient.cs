using System.Net.Http.Json;
using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.FilesMigrations.Models;
using Wheelhouse.Application.Deployments.FilesMigrations.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Files;

/// <summary>Calls a Files host's import API with the operator key: <c>POST {url}imports</c> and
/// <c>POST {url}imports/{id}/tokens</c>. The key goes in <c>X-Api-Key</c> as <c>files_</c> followed by the vault value
/// and reaches only these two paths. An error answer becomes a failure naming the status; its body is never read.</summary>
public sealed class HttpFilesImportClient(HttpClient http) : IFilesImportClient
{
    /// <summary>Holds the header that carries the operator key.</summary>
    public const string KeyHeader = "X-Api-Key";

    /// <summary>Holds the prefix Files puts before the key's vault value.</summary>
    public const string KeyPrefix = "files_";

    /// <inheritdoc />
    public Task<AppResult<FilesImport>> OpenAsync(
        string filesUrl, string operatorKey, string tenant, IReadOnlyList<string> sources, CancellationToken ct) =>
        PostAsync(filesUrl + "imports", operatorKey, new { tenant, sources }, ct);

    /// <inheritdoc />
    public Task<AppResult<FilesImport>> TokenAsync(string filesUrl, string operatorKey, string import, CancellationToken ct) =>
        PostAsync(filesUrl + "imports/" + Uri.EscapeDataString(import) + "/tokens", operatorKey, null, ct);

    private async Task<AppResult<FilesImport>> PostAsync(string url, string operatorKey, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = body is null ? JsonContent.Create(new { }) : JsonContent.Create(body),
            };
            request.Headers.Add(KeyHeader, KeyPrefix + operatorKey);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            if (!response.IsSuccessStatusCode)
                return AppResult<FilesImport>.Fail(FilesImportErrors.Answered((int)response.StatusCode));
            var answer = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var token = answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
            if (string.IsNullOrEmpty(token))
                return AppResult<FilesImport>.Fail(FilesImportErrors.Answered((int)response.StatusCode));
            var id = answer.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            var expires = answer.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.String
                && e.TryGetDateTimeOffset(out var at) ? at : (DateTimeOffset?)null;
            return AppResult<FilesImport>.Ok(new FilesImport(id, token, expires));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            return AppResult<FilesImport>.Fail(FilesImportErrors.Unreachable());
        }
    }
}
