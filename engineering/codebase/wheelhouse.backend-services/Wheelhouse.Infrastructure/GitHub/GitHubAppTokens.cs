using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wheelhouse.Infrastructure.Settings;

namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Issues the GitHub App's installation token per owner: signs an App token with the private key, finds the
/// owner's installation (<c>/users/{owner}/installation</c>, then <c>/orgs/{owner}/installation</c>), exchanges it
/// for an installation token and keeps that until <see cref="RefreshBefore"/> its expiry.</summary>
/// <remarks>
/// One refresh runs at a time per owner; other callers wait for it, then share its token. An owner without the App is
/// asked again after <see cref="RetryAfterRefusal"/>, a failed call after <see cref="RetryAfterFailure"/>; meanwhile
/// the caller falls back. The key is read from its file at each issue, never kept, and no token, key or response body
/// is ever logged.
/// </remarks>
public sealed partial class GitHubAppTokens(
    IHttpClientFactory clients,
    DeploymentSettings settings,
    TimeProvider time,
    GitHubAccessMonitor monitor,
    ILogger<GitHubAppTokens> logger)
{
    /// <summary>Holds the name of the HTTP client the App's own calls use.</summary>
    public const string ClientName = "wheelhouse-github-app";

    /// <summary>How long before its expiry a token is replaced; a runner call started with it still finishes in time.</summary>
    public static readonly TimeSpan RefreshBefore = TimeSpan.FromMinutes(5);

    /// <summary>How long an owner GitHub refused (no installation, refused credentials) waits before it is asked again.</summary>
    public static readonly TimeSpan RetryAfterRefusal = TimeSpan.FromMinutes(10);

    /// <summary>How long an owner whose token could not be issued (unreachable, unexpected answer) waits.</summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, OwnerState> _owners = new(StringComparer.Ordinal);

    /// <summary>Gets whether the App is set up: an App ID and a private key file in place.</summary>
    public bool IsConfigured =>
        settings.HasGitHubApp && settings.GitHubAppKeyFile.Length > 0 && File.Exists(settings.GitHubAppKeyFile);

    /// <summary>Gets a usable installation token for an owner.</summary>
    /// <param name="owner">The GitHub account; case does not matter.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The token, or <c>null</c> when the App is off, not installed there, or GitHub refused or failed.</returns>
    public async Task<GitHubAppToken?> GetAsync(string owner, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (!IsConfigured)
            return null;

        owner = owner.ToLowerInvariant();
        var state = _owners.GetOrAdd(owner, static _ => new OwnerState());
        if (Fresh(state.Token, time.GetUtcNow()) is { } kept)
            return kept;

        await state.Turn.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            if (Fresh(state.Token, now) is { } shared)
                return shared;

            var issued = now < state.RetryAt ? null : await IssueAsync(owner, state, now, ct);
            if (issued is not null)
                state.Token = issued;
            // A token in its last minutes still serves while its replacement cannot be issued.
            return issued ?? (state.Token is { } previous && previous.ExpiresAt > now ? previous : null);
        }
        finally
        {
            state.Turn.Release();
        }
    }

    /// <summary>Drops an owner's kept token after GitHub refused it, so the next call issues a new one.</summary>
    /// <param name="owner">The GitHub account.</param>
    public void Invalidate(string owner)
    {
        if (_owners.TryGetValue(owner.ToLowerInvariant(), out var state))
            state.Token = null;
    }

    private static GitHubAppToken? Fresh(GitHubAppToken? token, DateTimeOffset now) =>
        token is not null && token.ExpiresAt - RefreshBefore > now ? token : null;

    private async Task<GitHubAppToken?> IssueAsync(string owner, OwnerState state, DateTimeOffset now, CancellationToken ct)
    {
        string jwt;
        try
        {
            jwt = GitHubAppJwt.Create(settings.GitHubAppId, await File.ReadAllTextAsync(settings.GitHubAppKeyFile, ct), now);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            monitor.AppRefused(GitHubAccessCauses.KeyUnreadable, null);
            return Wait(state, now, RetryAfterFailure);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            monitor.AppRefused(GitHubAccessCauses.KeyInvalid, null);
            return Wait(state, now, RetryAfterRefusal);
        }

        using var client = clients.CreateClient(ClientName);
        try
        {
            if (state.InstallationId is null)
            {
                var (installation, lookup) = await FindInstallationAsync(client, jwt, owner, ct);
                if (installation is null)
                    return Refused(owner, state, now, lookup, exchange: false);
                state.InstallationId = installation;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"app/installations/{state.InstallationId}/access_tokens");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    state.InstallationId = null; // Uninstalled since it was found; it is looked up again next time.
                return Refused(owner, state, now, (int)response.StatusCode, exchange: true);
            }

            var token = await ReadTokenAsync(response, ct);
            if (token is null)
            {
                LogNotIssued(logger, owner, "an unreadable token response");
                return Wait(state, now, RetryAfterFailure);
            }

            monitor.AppWorks();
            monitor.Installed(owner);
            state.RetryAt = default;
            return token;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
                                              || (exception is TaskCanceledException && !ct.IsCancellationRequested))
        {
            LogNotIssued(logger, owner, exception.GetType().Name);
            return Wait(state, now, RetryAfterFailure);
        }
    }

    /// <summary>Finds the owner's installation as a user, then as an organization.</summary>
    /// <returns>The installation, or <c>null</c> with the status that refused it (404 when neither has one).</returns>
    private static async Task<(long? Installation, int Status)> FindInstallationAsync(
        HttpClient client, string jwt, string owner, CancellationToken ct)
    {
        foreach (var kind in (string[])["users", "orgs"])
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{kind}/{Uri.EscapeDataString(owner)}/installation");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                continue;
            if (!response.IsSuccessStatusCode)
                return (null, (int)response.StatusCode);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            return document.RootElement.TryGetProperty("id", out var id) && id.TryGetInt64(out var value) && value > 0
                ? (value, (int)response.StatusCode)
                : throw new JsonException("The installation has no id.");
        }

        return (null, (int)HttpStatusCode.NotFound);
    }

    private static async Task<GitHubAppToken?> ReadTokenAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var root = document.RootElement;
        if (!root.TryGetProperty("token", out var token) || token.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("expires_at", out var expires) || !expires.TryGetDateTimeOffset(out var expiresAt))
            return null;
        var value = token.GetString();
        return string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace) ? null : new GitHubAppToken(value, expiresAt);
    }

    /// <summary>Records why GitHub refused, and when to ask again.</summary>
    private GitHubAppToken? Refused(string owner, OwnerState state, DateTimeOffset now, int status, bool exchange)
    {
        switch (status)
        {
            case (int)HttpStatusCode.Unauthorized:
                monitor.AppRefused(GitHubAccessCauses.AppRefused, status);
                return Wait(state, now, RetryAfterRefusal);
            case (int)HttpStatusCode.NotFound:
                // GitHub checks the App token before it looks for an installation, so the App's credentials work.
                monitor.AppWorks();
                monitor.NotInstalled(owner);
                return Wait(state, now, exchange ? RetryAfterFailure : RetryAfterRefusal);
            case (int)HttpStatusCode.Forbidden or (int)HttpStatusCode.UnprocessableEntity or (int)HttpStatusCode.TooManyRequests:
                monitor.InstallationRefused(owner, status);
                return Wait(state, now, RetryAfterRefusal);
            default:
                LogNotIssued(logger, owner, "HTTP " + status);
                return Wait(state, now, RetryAfterFailure);
        }
    }

    private static GitHubAppToken? Wait(OwnerState state, DateTimeOffset now, TimeSpan delay)
    {
        state.RetryAt = now + delay;
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A GitHub App token for {Owner} could not be issued ({Reason}); its calls fall back.")]
    private static partial void LogNotIssued(ILogger logger, string owner, string reason);

    /// <summary>Holds one owner's token and refresh turn; the installation and retry time change only inside the turn.</summary>
    private sealed class OwnerState
    {
        private GitHubAppToken? _token;

        public SemaphoreSlim Turn { get; } = new(1, 1);

        public GitHubAppToken? Token
        {
            get => Volatile.Read(ref _token);
            set => Volatile.Write(ref _token, value);
        }

        public long? InstallationId { get; set; }

        public DateTimeOffset RetryAt { get; set; }
    }
}
