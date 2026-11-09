using System.Net.Http.Json;
using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.FilesMigrations.Models;
using Wheelhouse.Application.Deployments.FilesMigrations.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Files;

/// <summary>Calls a Files host's import API with the operator key: <c>POST {url}api/files/v1/imports</c> and
/// <c>POST {url}api/files/v1/imports/{id}/tokens</c>, and the keyless <c>GET {url}health</c> preflight. The key goes in
/// <c>X-Api-Key</c> as <c>files_</c> followed by the vault value and reaches only these two paths. Files answers inside
/// its <c>{ "data": … }</c> envelope. An error answer becomes a failure naming the status; its body is never read.</summary>
public sealed class HttpFilesImportClient(HttpClient http) : IFilesImportClient
{
    /// <summary>Holds the header that carries the operator key.</summary>
    public const string KeyHeader = "X-Api-Key";

    /// <summary>Holds the prefix Files puts before the key's vault value.</summary>
    public const string KeyPrefix = "files_";

    /// <summary>Holds the path of Files' imports below its URL.</summary>
    public const string ImportsPath = "api/files/v1/imports";

    /// <inheritdoc />
    public Task<AppResult<FilesImport>> OpenAsync(
        string filesUrl, string operatorKey, string tenant, IReadOnlyList<string> sources, CancellationToken ct) =>
        PostAsync(filesUrl + ImportsPath, operatorKey, new { tenant, sources }, ct);

    /// <inheritdoc />
    public Task<AppResult<FilesImport>> TokenAsync(string filesUrl, string operatorKey, string import, CancellationToken ct) =>
        PostAsync(filesUrl + ImportsPath + "/" + Uri.EscapeDataString(import) + "/tokens", operatorKey, null, ct);

    /// <summary>Holds how long a health answer may take.</summary>
    public static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<AppResult<bool>> HealthAsync(string filesUrl, CancellationToken ct)
    {
        string detail;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(HealthTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, filesUrl + "health");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
                return AppResult<bool>.Ok(true);
            detail = ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            detail = exception is TaskCanceledException ? "timeout" : "unreachable";
        }

        return AppResult<bool>.Fail(AppErrorFactory.Conflict($"Files at {new Uri(filesUrl).Host} is not healthy ({detail})."));
    }

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
            if (answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("data", out var data))
                answer = data;
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
