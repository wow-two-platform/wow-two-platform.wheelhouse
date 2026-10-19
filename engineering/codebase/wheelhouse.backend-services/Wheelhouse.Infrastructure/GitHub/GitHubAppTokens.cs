using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wheelhouse.Infrastructure.Settings;

namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Issues the GitHub App's installation token per owner: signs an App token with the private key, lists the
/// App's installations (<c>/app/installations</c>, kept <see cref="InstallationsFor"/>), exchanges the owner's for an
/// installation token and keeps that until <see cref="RefreshBefore"/> its expiry.</summary>
/// <remarks>
/// One refresh runs at a time per owner; other callers wait for it, then share its token. An owner missing from the
/// list has no installation: its private repositories stay out of reach, and <see cref="BorrowAsync"/> lends any
/// installation's token for its public ones. A refused exchange is asked again after <see cref="RetryAfterRefusal"/>, a
/// failed call after <see cref="RetryAfterFailure"/>; meanwhile the caller falls back. The key is read from its file at
/// each signature, never kept, and no token, key or response body is ever logged.
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

    /// <summary>How long the list of installations is kept; an owner installed since shows within this time.</summary>
    public static readonly TimeSpan InstallationsFor = TimeSpan.FromMinutes(10);

    /// <summary>How long GitHub's refusal (of the App's credentials, or of a token) waits before it is asked again.</summary>
    public static readonly TimeSpan RetryAfterRefusal = TimeSpan.FromMinutes(10);

    /// <summary>How long a call that failed (unreachable, unexpected answer) waits before it is made again.</summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, OwnerState> _owners = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _listTurn = new(1, 1);
    private volatile InstallationList? _installations;
    private DateTimeOffset _listRetryAt;

    /// <summary>Gets whether the App is set up: an App ID and a private key file in place.</summary>
    public bool IsConfigured =>
        settings.HasGitHubApp && settings.GitHubAppKeyFile.Length > 0 && File.Exists(settings.GitHubAppKeyFile);

    /// <summary>Gets a usable installation token for an owner the App is installed on.</summary>
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

        var installations = await InstallationsAsync(ct);
        if (installations is null || !installations.TryGetValue(owner, out var installation))
            return null;

        await state.Turn.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            if (Fresh(state.Token, now) is { } shared)
                return shared;

            var issued = now < state.RetryAt ? null : await IssueAsync(owner, installation, state, now, ct);
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

    /// <summary>Gets an installation token of any owner the App is installed on, for public repositories of an owner it
    /// is not: a GitHub App token reaches public resources without the installation's permissions.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A token, or <c>null</c> when the App is off, installed nowhere, or no token could be issued.</returns>
    public async Task<GitHubAppToken?> BorrowAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        var now = time.GetUtcNow();
        if (_owners.Values.Select(state => Fresh(state.Token, now)).FirstOrDefault(token => token is not null) is { } kept)
            return kept;

        foreach (var owner in (await InstallationsAsync(ct))?.Keys.Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>())
        {
            if (await GetAsync(owner, ct) is { } token)
                return token;
        }

        return null;
    }

    /// <summary>Gets whether the App is installed on an owner, as the kept list says; <c>null</c> while unknown.</summary>
    /// <param name="owner">The GitHub account.</param>
    public bool? IsInstalled(string owner) =>
        _installations is { } list ? list.Owners.ContainsKey(owner.ToLowerInvariant()) : null;

    /// <summary>Drops an owner's kept token after GitHub refused it, so the next call issues a new one.</summary>
    /// <param name="owner">The GitHub account.</param>
    public void Invalidate(string owner)
    {
        if (_owners.TryGetValue(owner.ToLowerInvariant(), out var state))
            state.Token = null;
    }

    private static GitHubAppToken? Fresh(GitHubAppToken? token, DateTimeOffset now) =>
        token is not null && token.ExpiresAt - RefreshBefore > now ? token : null;

    /// <summary>Lists the owners the App is installed on, by lowercase login; kept <see cref="InstallationsFor"/>.</summary>
    /// <returns>The installations, or the last list (or <c>null</c>) when GitHub cannot list them now.</returns>
    private async Task<IReadOnlyDictionary<string, long>?> InstallationsAsync(CancellationToken ct)
    {
        if (_installations is { } kept && time.GetUtcNow() - kept.ListedAt < InstallationsFor)
            return kept.Owners;

        await _listTurn.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            if (_installations is { } shared && now - shared.ListedAt < InstallationsFor)
                return shared.Owners;
            if (now < _listRetryAt || await SignAsync(now, ct) is not { } jwt)
                return _installations?.Owners;

            using var client = clients.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, "app/installations?per_page=100");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    monitor.AppRefused(GitHubAccessCauses.AppRefused, (int)response.StatusCode);
                    _listRetryAt = now + RetryAfterRefusal;
                }
                else
                {
                    LogUnlisted(logger, "HTTP " + (int)response.StatusCode);
                    _listRetryAt = now + RetryAfterFailure;
                }

                return _installations?.Owners;
            }

            var owners = new Dictionary<string, long>(StringComparer.Ordinal);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            foreach (var item in document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray() : default)
            {
                if (item.TryGetProperty("id", out var id) && id.TryGetInt64(out var installation) && installation > 0
                    && item.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object
                    && account.TryGetProperty("login", out var login) && GitHubOwners.IsOwner(login.GetString()))
                    owners[login.GetString()!.ToLowerInvariant()] = installation;
            }

            monitor.AppWorks();
            _installations = new InstallationList(owners, now);
            return owners;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
                                              || (exception is TaskCanceledException && !ct.IsCancellationRequested))
        {
            LogUnlisted(logger, exception.GetType().Name);
            _listRetryAt = time.GetUtcNow() + RetryAfterFailure;
            return _installations?.Owners;
        }
        finally
        {
            _listTurn.Release();
        }
    }

    /// <summary>Signs a fresh App token; a missing or invalid key is noted and gives none.</summary>
    private async Task<string?> SignAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            return GitHubAppJwt.Create(settings.GitHubAppId, await File.ReadAllTextAsync(settings.GitHubAppKeyFile, ct), now);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            monitor.AppRefused(GitHubAccessCauses.KeyUnreadable, null);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            monitor.AppRefused(GitHubAccessCauses.KeyInvalid, null);
        }

        return null;
    }

    private async Task<GitHubAppToken?> IssueAsync(
        string owner, long installation, OwnerState state, DateTimeOffset now, CancellationToken ct)
    {
        if (await SignAsync(now, ct) is not { } jwt)
            return Wait(state, now, RetryAfterFailure);

        using var client = clients.CreateClient(ClientName);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"app/installations/{installation}/access_tokens");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            using var response = await client.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        monitor.AppRefused(GitHubAccessCauses.AppRefused, status);
                        return Wait(state, now, RetryAfterRefusal);
                    case HttpStatusCode.NotFound:
                        _installations = null; // Uninstalled since it was listed; the list is read again.
                        return Wait(state, now, RetryAfterFailure);
                    case HttpStatusCode.Forbidden or HttpStatusCode.UnprocessableEntity or HttpStatusCode.TooManyRequests:
                        monitor.InstallationRefused(owner, status);
                        return Wait(state, now, RetryAfterRefusal);
                    default:
                        LogNotIssued(logger, owner, "HTTP " + status);
                        return Wait(state, now, RetryAfterFailure);
                }
            }

            if (await ReadTokenAsync(response, ct) is not { } token)
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

    private static GitHubAppToken? Wait(OwnerState state, DateTimeOffset now, TimeSpan delay)
    {
        state.RetryAt = now + delay;
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A GitHub App token for {Owner} could not be issued ({Reason}); its calls fall back.")]
    private static partial void LogNotIssued(ILogger logger, string owner, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The GitHub App's installations could not be listed ({Reason}); calls fall back.")]
    private static partial void LogUnlisted(ILogger logger, string reason);

    /// <summary>Holds the owners the App is installed on and when they were listed.</summary>
    private sealed record InstallationList(IReadOnlyDictionary<string, long> Owners, DateTimeOffset ListedAt);

    /// <summary>Holds one owner's token and refresh turn; the retry time changes only inside the turn.</summary>
    private sealed class OwnerState
    {
        private GitHubAppToken? _token;

        public SemaphoreSlim Turn { get; } = new(1, 1);

        public GitHubAppToken? Token
        {
            get => Volatile.Read(ref _token);
            set => Volatile.Write(ref _token, value);
        }

        public DateTimeOffset RetryAt { get; set; }
    }
}
