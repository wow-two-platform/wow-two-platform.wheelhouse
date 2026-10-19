using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Infrastructure.Settings;

namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Keeps what keeps Wheelhouse from reaching GitHub, per owner: calls refused with 401, 403 or 429 (the
/// console's, and those the runner notes in <c>{root}/github/access.json</c>), an owner without the GitHub App, and an
/// App set up by half.</summary>
/// <remarks>A later success for an owner clears its refused call; a working token clears its installation problem. A
/// refused call nobody repeats drops after <see cref="CallKeptFor"/>, an installation problem after
/// <see cref="SetupKeptFor"/>. Only statuses and fixed words are kept, never a token or a response body.</remarks>
public sealed class GitHubAccessMonitor(DeploymentSettings settings, TimeProvider time) : IGitHubAccessIssues
{
    /// <summary>How long a refused call stays listed without being seen again.</summary>
    public static readonly TimeSpan CallKeptFor = TimeSpan.FromHours(1);

    /// <summary>How long an installation or App problem stays listed without being seen again.</summary>
    public static readonly TimeSpan SetupKeptFor = TimeSpan.FromHours(24);

    /// <summary>Holds the owner key the App's own problems are kept under.</summary>
    private const string AppKey = "";

    private readonly ConcurrentDictionary<NoteKey, GitHubAccessIssueDto> _notes = new();

    private enum NoteKind
    {
        Call,
        Installation,
    }

    /// <summary>Notes a GitHub answer to a call for an owner: a refusal is kept, a success clears the owner's refusal.</summary>
    /// <param name="owner">The owner the call addressed.</param>
    /// <param name="response">GitHub's answer; only its status and rate-limit headers are read.</param>
    /// <param name="authenticated">Whether the call carried a credential.</param>
    public void Observe(string owner, HttpResponseMessage response, bool authenticated)
    {
        ArgumentNullException.ThrowIfNull(response);
        var status = (int)response.StatusCode;
        if (status is 401 or 403 or 429)
            Refused(owner, status, authenticated, IsRateLimited(response));
        else if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
            _notes.TryRemove(new NoteKey(Normal(owner), NoteKind.Call), out _);
    }

    /// <summary>Notes a call GitHub refused.</summary>
    /// <param name="owner">The owner the call addressed.</param>
    /// <param name="status">401, 403 or 429.</param>
    /// <param name="authenticated">Whether the call carried a credential.</param>
    /// <param name="rateLimited">Whether GitHub said the rate limit ran out.</param>
    public void Refused(string owner, int status, bool authenticated, bool rateLimited) =>
        Note(Normal(owner), NoteKind.Call, GitHubAccessCauses.Refused(status, authenticated, rateLimited), status);

    /// <summary>Notes that the App is not installed on an owner.</summary>
    /// <param name="owner">The owner.</param>
    public void NotInstalled(string owner) => Note(Normal(owner), NoteKind.Installation, GitHubAccessCauses.NotInstalled, null);

    /// <summary>Notes that GitHub refused an installation token for an owner.</summary>
    /// <param name="owner">The owner.</param>
    /// <param name="status">The HTTP status.</param>
    public void InstallationRefused(string owner, int status) =>
        Note(Normal(owner), NoteKind.Installation, GitHubAccessCauses.InstallationRefused(status), status);

    /// <summary>Clears an owner's installation problem once a token was issued.</summary>
    /// <param name="owner">The owner.</param>
    public void Installed(string owner) => _notes.TryRemove(new NoteKey(Normal(owner), NoteKind.Installation), out _);

    /// <summary>Notes a problem with the App itself: its key or its credentials.</summary>
    /// <param name="cause">One of the <see cref="GitHubAccessCauses"/> words.</param>
    /// <param name="status">The HTTP status, when GitHub answered.</param>
    public void AppRefused(string cause, int? status) => Note(AppKey, NoteKind.Installation, cause, status);

    /// <summary>Clears the App's own problem once GitHub accepted its credentials.</summary>
    public void AppWorks() => _notes.TryRemove(new NoteKey(AppKey, NoteKind.Installation), out _);

    /// <inheritdoc />
    public IReadOnlyList<GitHubAccessIssueDto> Current()
    {
        var now = time.GetUtcNow();
        var calls = new Dictionary<string, GitHubAccessIssueDto>(StringComparer.Ordinal);
        var issues = new List<GitHubAccessIssueDto>();
        foreach (var (key, issue) in _notes)
        {
            if (now - issue.At > (key.Kind == NoteKind.Call ? CallKeptFor : SetupKeptFor))
                _notes.TryRemove(new KeyValuePair<NoteKey, GitHubAccessIssueDto>(key, issue));
            else if (key.Kind == NoteKind.Call)
                calls[key.Owner] = issue;
            else
                issues.Add(issue);
        }

        // One refused call per owner: the console's or the runner's, whichever is newer.
        foreach (var issue in ReadRunnerNotes())
        {
            if (now - issue.At <= CallKeptFor && (!calls.TryGetValue(issue.Owner!, out var kept) || kept.At < issue.At))
                calls[issue.Owner!] = issue;
        }

        issues.AddRange(calls.Values);
        if (SetupProblem() is { } setup && !issues.Any(issue => issue.Owner is null && issue.Cause == setup))
            issues.Add(new GitHubAccessIssueDto(null, setup, null, now));
        return [.. issues.OrderByDescending(issue => issue.At).ThenBy(issue => issue.Owner, StringComparer.Ordinal)];
    }

    /// <summary>Gets whether GitHub said a refusal came from a spent rate limit.</summary>
    /// <param name="response">GitHub's answer.</param>
    public static bool IsRateLimited(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.StatusCode == HttpStatusCode.TooManyRequests
            || response.Headers.RetryAfter is not null
            || (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0"));
    }

    private static string Normal(string owner) => owner.ToLowerInvariant();

    private void Note(string owner, NoteKind kind, string cause, int? status) =>
        _notes[new NoteKey(owner, kind)] =
            new GitHubAccessIssueDto(owner.Length == 0 ? null : owner, cause, status, time.GetUtcNow());

    /// <summary>Names an App set up by half; checked on every read, so placing the missing piece clears it.</summary>
    private string? SetupProblem()
    {
        var keyInPlace = settings.GitHubAppKeyFile.Length > 0 && File.Exists(settings.GitHubAppKeyFile);
        return (settings.HasGitHubApp, keyInPlace) switch
        {
            (true, false) => GitHubAccessCauses.KeyMissing,
            (false, true) => GitHubAccessCauses.AppIdMissing,
            _ => null,
        };
    }

    /// <summary>Reads the runner's notes: <c>{"owners": {owner: {status, authenticated, rateLimited, at}}}</c>. An
    /// unreadable or malformed file, or entry, counts as none.</summary>
    private List<GitHubAccessIssueDto> ReadRunnerNotes()
    {
        var notes = new List<GitHubAccessIssueDto>();
        if (settings.Root.Length == 0)
            return notes;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(GitHubRunnerFiles.AccessPath(settings.Root)));
            if (!document.RootElement.TryGetProperty("owners", out var owners) || owners.ValueKind != JsonValueKind.Object)
                return notes;
            foreach (var entry in owners.EnumerateObject())
            {
                var note = entry.Value;
                if (!GitHubOwners.IsOwner(entry.Name) || note.ValueKind != JsonValueKind.Object
                    || !note.TryGetProperty("status", out var status) || !status.TryGetInt32(out var code)
                    || code is not (401 or 403 or 429)
                    || !note.TryGetProperty("at", out var at) || at.ValueKind != JsonValueKind.String
                    || !DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var seen))
                    continue;
                var authenticated = note.TryGetProperty("authenticated", out var value) && value.ValueKind == JsonValueKind.True;
                var rateLimited = note.TryGetProperty("rateLimited", out var limited) && limited.ValueKind == JsonValueKind.True;
                notes.Add(new GitHubAccessIssueDto(
                    Normal(entry.Name), GitHubAccessCauses.Refused(code, authenticated, rateLimited), code, seen));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // No notes, or none readable: the runner rewrites the file on its next refused call.
        }

        return notes;
    }

    private readonly record struct NoteKey(string Owner, NoteKind Kind);
}
