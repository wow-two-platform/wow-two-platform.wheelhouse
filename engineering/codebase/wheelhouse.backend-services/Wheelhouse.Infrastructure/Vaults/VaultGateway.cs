using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Vaults;
using Wheelhouse.Application.Vaults.Changes;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Infrastructure.Vaults;

/// <summary>Administers the inventory's vaults over their management API with Wheelhouse's administrator credential.</summary>
/// <remarks>
/// Endpoints come only from the inventory the operator edits, so a request can never choose where Wheelhouse connects.
/// Secret values travel one way — to the vault — and responses carry metadata, except a freshly minted token.
/// </remarks>
public sealed class VaultGateway(
    IVaultsRepository vaults,
    IServersRepository servers,
    IHttpClientFactory clients,
    VaultSessionCache sessions,
    DeploymentSettings settings) : IVaultGateway
{
    /// <summary>The named client with redirects and cookies disabled.</summary>
    public const string ClientName = "vault";

    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();

    /// <inheritdoc />
    public async Task<AppResult<JsonElement>> ReadAsync(VaultResource resource, string? vault, string? ns, CancellationToken ct)
    {
        if (resource == VaultResource.Vaults)
            return await ListAsync(ct);
        var resolved = await ResolveAsync(vault, ct);
        if (resolved is not AppResult<VaultEndpoint>.Success { Data: var endpoint })
            return AppResult<JsonElement>.Fail(((AppResult<VaultEndpoint>.Failure)resolved).Error);
        var path = resource switch
        {
            VaultResource.Namespaces => "api/admin/namespaces",
            VaultResource.Secrets => "api/admin/secrets?ns=" + Uri.EscapeDataString(ns ?? ""),
            _ => "api/admin/namespaces/" + Uri.EscapeDataString(ns ?? "") + "/tokens"
        };
        return await SendAsync(endpoint, HttpMethod.Get, path, null, ct);
    }

    /// <inheritdoc />
    public async Task<AppResult<JsonElement>> ApplyAsync(string vault, VaultChange change, CancellationToken ct)
    {
        var resolved = await ResolveAsync(vault, ct);
        if (resolved is not AppResult<VaultEndpoint>.Success { Data: var endpoint })
            return AppResult<JsonElement>.Fail(((AppResult<VaultEndpoint>.Failure)resolved).Error);
        var ns = Uri.EscapeDataString(change.Namespace);
        var (method, path, body) = change switch
        {
            NamespaceCreateChange create => (HttpMethod.Post, "api/admin/namespaces", (object)new { slug = create.Namespace, name = create.Name }),
            SecretSetChange set => (HttpMethod.Put, "api/admin/secrets/" + ns + "/" + Uri.EscapeDataString(set.Key),
                new { value = set.Value, description = set.Description }),
            SecretStateChange state => (HttpMethod.Post, "api/admin/secrets/" + ns + "/" + Uri.EscapeDataString(state.Key) + "/state",
                new { disabled = state.Disabled }),
            TokenMintChange mint => (HttpMethod.Post, "api/admin/namespaces/" + ns + "/tokens", new { name = mint.Name }),
            TokenRevokeChange revoke => (HttpMethod.Post, "api/admin/namespaces/" + ns + "/tokens/" + revoke.TokenId + "/revoke", new { }),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        return await SendAsync(endpoint, method, path, body, ct);
    }

    // ---- Inventory ----

    private async Task<AppResult<JsonElement>> ListAsync(CancellationToken ct)
    {
        var serverSlugs = (await servers.GetAllAsync(ct)).ToDictionary(server => server.Id, server => server.Slug);
        var endpoints = (await vaults.GetAllAsync(ct))
            .Select(vault => new VaultEndpoint(vault.Slug, vault.Name, serverSlugs[vault.ServerId], vault.Url.TrimEnd('/')))
            .ToList();
        var statuses = await Task.WhenAll(endpoints.Select(vault => StatusAsync(vault, ct)));
        // The console lists names and state only; the endpoint stays with the inventory editor.
        var summaries = endpoints.Select((vault, index) => new { vault.Id, vault.Name, vault.ServerId, status = statuses[index] });
        return AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(summaries, JsonSerializerOptions.Web));
    }

    private async Task<AppResult<VaultEndpoint>> ResolveAsync(string? vault, CancellationToken ct)
    {
        var stored = vault is null ? null : await vaults.GetBySlugAsync(vault, ct);
        var server = stored is null ? null : await servers.GetByIdAsync(stored.ServerId, ct);
        return stored is null || server is null
            ? AppResult<VaultEndpoint>.Fail(AppErrorFactory.NotFound($"Vault '{vault}' was not found."))
            : AppResult<VaultEndpoint>.Ok(new VaultEndpoint(stored.Slug, stored.Name, server.Slug, stored.Url.TrimEnd('/')));
    }

    private async Task<string> StatusAsync(VaultEndpoint vault, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await clients.CreateClient(ClientName).GetAsync(vault.Url + "/api/system/status", timeout.Token);
            if (!response.IsSuccessStatusCode)
                return "unreachable";
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
            return body.TryGetProperty("isSealed", out var sealedFlag) && sealedFlag.ValueKind == JsonValueKind.False ? "unsealed" : "sealed";
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            return "unreachable";
        }
    }

    // ---- Transport ----

    private async Task<AppResult<JsonElement>> SendAsync(VaultEndpoint vault, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var token = sessions.Get(vault.Id);
                if (token is null)
                {
                    var session = await SignInAsync(vault, ct);
                    if (session is not AppResult<string>.Success { Data: var signedIn })
                        return AppResult<JsonElement>.Fail(((AppResult<string>.Failure)session).Error);
                    token = signedIn;
                }
                using var request = new HttpRequestMessage(method, vault.Url + "/" + path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (body is not null)
                    request.Content = JsonContent.Create(body);
                using var response = await clients.CreateClient(ClientName).SendAsync(request, ct);
                // An expired or rotated session gets one fresh sign-in before the call counts as refused.
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    sessions.Forget(vault.Id);
                    continue;
                }
                return await ReadAsync(response, ct);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return AppResult<JsonElement>.Fail(AppErrorFactory.ExternalUnavailable("The vault is unreachable from Wheelhouse."));
        }
    }

    private async Task<AppResult<string>> SignInAsync(VaultEndpoint vault, CancellationToken ct)
    {
        var file = Path.Combine(settings.Root, "vaults", vault.Id, "password");
        if (!File.Exists(file))
            return AppResult<string>.Fail(AppErrorFactory.ExternalUnavailable("Wheelhouse has no administrator credential for this vault."));
        var password = (await File.ReadAllTextAsync(file, ct)).Trim();
        using var response = await clients.CreateClient(ClientName)
            .PostAsJsonAsync(vault.Url + "/api/identity/sign-in", new { password }, ct);
        var result = await ReadAsync(response, ct);
        if (result is not AppResult<JsonElement>.Success { Data: var session })
            return AppResult<string>.Fail(((AppResult<JsonElement>.Failure)result).Error);
        var token = session.GetProperty("token").GetString()!;
        sessions.Set(vault.Id, token, session.GetProperty("expiresAtUtc").GetDateTimeOffset());
        return AppResult<string>.Ok(token);
    }

    private static async Task<AppResult<JsonElement>> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
                return AppResult<JsonElement>.Ok(Empty);
            var envelope = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return AppResult<JsonElement>.Ok(envelope.TryGetProperty("data", out var data) ? data.Clone() : envelope.Clone());
        }
        var detail = await DetailAsync(response, ct);
        return AppResult<JsonElement>.Fail(response.StatusCode switch
        {
            HttpStatusCode.BadRequest => AppErrorFactory.Validation(detail ?? "The vault rejected the request."),
            HttpStatusCode.NotFound => AppErrorFactory.NotFound(detail ?? "The vault has no such item."),
            HttpStatusCode.Conflict => AppErrorFactory.Conflict(detail ?? "The vault item already exists."),
            HttpStatusCode.TooManyRequests => AppErrorFactory.TooManyRequests("The vault is throttling Wheelhouse's sign-in."),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                AppErrorFactory.ExternalUnavailable("The vault rejected Wheelhouse's administrator credential."),
            HttpStatusCode.ServiceUnavailable => AppErrorFactory.ExternalUnavailable("The vault is sealed or unavailable."),
            _ => AppErrorFactory.ExternalUnavailable("The vault failed the request.")
        });
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (problem.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject().SelectMany(field => field.Value.EnumerateArray()).Select(item => item.GetString());
                var joined = string.Join(" ", messages.Where(message => !string.IsNullOrWhiteSpace(message)));
                if (joined.Length > 0)
                    return joined.Length > 300 ? joined[..300] : joined;
            }
            var detail = problem.TryGetProperty("detail", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return detail is { Length: > 300 } ? detail[..300] : detail;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
