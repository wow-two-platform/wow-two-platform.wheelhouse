using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Infrastructure.Settings;

namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Keeps what keeps Wheelhouse from reaching GitHub, per owner: a call refused with 401, 403 or 429, a repository
/// no credential at hand could see while the owner lacks the GitHub App (the console's notes, and those the runner keeps
/// in <c>{root}/github/access.json</c>), an image package no credential can read (the runner's
/// <c>{root}/github/packages.json</c>), a refused installation token, and an App set up by half.</summary>
/// <remarks>An owner without the App is named only when one of its repositories needs it: public ones are read with
/// another installation's token. A later success for an owner clears its refused call, and a read of the unseen
/// repository clears that; a working token clears an installation problem. A note nobody repeats drops after
/// <see cref="CallKeptFor"/> (<see cref="SetupKeptFor"/> for the App's own). Only statuses, repository names and fixed
/// words are kept, never a token or a response body.</remarks>
public sealed partial class GitHubAccessMonitor(DeploymentSettings settings, TimeProvider time) : IGitHubAccessIssues
{
    /// <summary>How long a refused call or unseen repository stays listed without being seen again.</summary>
    public static readonly TimeSpan CallKeptFor = TimeSpan.FromHours(1);

    /// <summary>How long an installation or App problem stays listed without being seen again.</summary>
    public static readonly TimeSpan SetupKeptFor = TimeSpan.FromHours(24);

    /// <summary>Holds the owner key the App's own problems are kept under.</summary>
    private const string AppKey = "";

    private readonly ConcurrentDictionary<NoteKey, Note> _notes = new();

    private enum NoteKind
    {
        Call,
        Installation,
    }

    /// <summary>Notes GitHub's answer to a call for an owner: a refusal, or an unseen repository while the owner lacks
    /// its own installation, is kept; a success clears what it answers.</summary>
    /// <param name="owner">The owner the call addressed.</param>
    /// <param name="address">The call's address; it tells a whole-repository read from a file read.</param>
    /// <param name="response">GitHub's answer; only its status and rate-limit headers are read.</param>
    /// <param name="authenticated">Whether the call carried a credential.</param>
    /// <param name="installed">Whether that credential was the owner's own App installation token.</param>
    public void Observe(string owner, Uri? address, HttpResponseMessage response, bool authenticated, bool installed)
    {
        ArgumentNullException.ThrowIfNull(response);
        var status = (int)response.StatusCode;
        var repository = GitHubOwners.RepositoryRead(address);
        if (status is 401 or 403 or 429)
            Refused(owner, status, authenticated, IsRateLimited(response), installed);
        else if (response.StatusCode == HttpStatusCode.NotFound && repository is not null && !installed && AppIsOn)
            Unseen(owner, repository);
        else if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
            Succeeded(owner, repository);
    }

    /// <summary>Notes a call GitHub refused.</summary>
    /// <param name="owner">The owner the call addressed.</param>
    /// <param name="status">401, 403 or 429.</param>
    /// <param name="authenticated">Whether the call carried a credential.</param>
    /// <param name="rateLimited">Whether GitHub said the rate limit ran out.</param>
    /// <param name="installed">Whether the call carried the owner's own App installation token.</param>
    public void Refused(string owner, int status, bool authenticated, bool rateLimited, bool installed = true) =>
        Keep(Normal(owner), NoteKind.Call, GitHubAccessCauses.Refused(status, authenticated, rateLimited, installed), status, null);

    /// <summary>Notes a repository no credential at hand could see while its owner lacks the App.</summary>
    /// <param name="owner">The owner.</param>
    /// <param name="repository">The lowercase <c>{owner}/{repo}</c>.</param>
    public void Unseen(string owner, string repository) =>
        Keep(Normal(owner), NoteKind.Call, GitHubAccessCauses.Unseen(repository), 404, repository);

    /// <summary>Clears an owner's refused call, and its unseen repository when this read saw it.</summary>
    /// <param name="owner">The owner.</param>
    /// <param name="repository">The repository the read saw whole, when it was one.</param>
    public void Succeeded(string owner, string? repository)
    {
        var key = new NoteKey(Normal(owner), NoteKind.Call);
        if (_notes.TryGetValue(key, out var note) && (note.Repository is null || note.Repository == repository))
            _notes.TryRemove(new KeyValuePair<NoteKey, Note>(key, note));
    }

    /// <summary>Notes that GitHub refused an installation token for an owner.</summary>
    /// <param name="owner">The owner.</param>
    /// <param name="status">The HTTP status.</param>
    public void InstallationRefused(string owner, int status) =>
        Keep(Normal(owner), NoteKind.Installation, GitHubAccessCauses.InstallationRefused(status), status, null);

    /// <summary>Clears an owner's installation problem once a token was issued.</summary>
    /// <param name="owner">The owner.</param>
    public void Installed(string owner) => _notes.TryRemove(new NoteKey(Normal(owner), NoteKind.Installation), out _);

    /// <summary>Notes a problem with the App itself: its key or its credentials.</summary>
    /// <param name="cause">One of the <see cref="GitHubAccessCauses"/> words.</param>
    /// <param name="status">The HTTP status, when GitHub answered.</param>
    public void AppRefused(string cause, int? status) => Keep(AppKey, NoteKind.Installation, cause, status, null);

    /// <summary>Clears the App's own problem once GitHub accepted its credentials.</summary>
    public void AppWorks() => _notes.TryRemove(new NoteKey(AppKey, NoteKind.Installation), out _);

    /// <inheritdoc />
    public IReadOnlyList<GitHubAccessIssueDto> Current()
    {
        var now = time.GetUtcNow();
        var calls = new Dictionary<string, GitHubAccessIssueDto>(StringComparer.Ordinal);
        var issues = new List<GitHubAccessIssueDto>();
        foreach (var (key, note) in _notes)
        {
            if (now - note.Issue.At > (key.Kind == NoteKind.Call ? CallKeptFor : SetupKeptFor))
                _notes.TryRemove(new KeyValuePair<NoteKey, Note>(key, note));
            else if (key.Kind == NoteKind.Call)
                calls[key.Owner] = note.Issue;
            else
                issues.Add(note.Issue);
        }

        // One note per owner: the console's or the runner's, whichever is newer.
        foreach (var issue in ReadRunnerNotes())
        {
            if (now - issue.At <= CallKeptFor && (!calls.TryGetValue(issue.Owner!, out var kept) || kept.At < issue.At))
                calls[issue.Owner!] = issue;
        }

        issues.AddRange(calls.Values);
        issues.AddRange(ReadPackageNotes().Where(issue => now - issue.At <= SetupKeptFor));
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

    /// <summary>Gets whether the App is set up, so an owner without it can be told to install it.</summary>
    private bool AppIsOn => settings.HasGitHubApp && settings.GitHubAppKeyFile.Length > 0 && File.Exists(settings.GitHubAppKeyFile);

    private static string Normal(string owner) => owner.ToLowerInvariant();

    private void Keep(string owner, NoteKind kind, string cause, int? status, string? repository) =>
        _notes[new NoteKey(owner, kind)] =
            new Note(new GitHubAccessIssueDto(owner.Length == 0 ? null : owner, cause, status, time.GetUtcNow()), repository);

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

    /// <summary>Reads the runner's notes: <c>{"owners": {owner: {status, authenticated, installed, rateLimited, at,
    /// repository}}}</c>. An unreadable or malformed file, or entry, counts as none.</summary>
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
                    || !note.TryGetProperty("at", out var at) || at.ValueKind != JsonValueKind.String
                    || !DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var seen))
                    continue;
                var owner = Normal(entry.Name);
                if (code is 401 or 403 or 429)
                {
                    notes.Add(new GitHubAccessIssueDto(owner, GitHubAccessCauses.Refused(code, Flag(note, "authenticated"),
                        Flag(note, "rateLimited"), !note.TryGetProperty("installed", out _) || Flag(note, "installed")), code, seen));
                }
                else if (code == 404 && note.TryGetProperty("repository", out var repository)
                         && repository.ValueKind == JsonValueKind.String && repository.GetString() is { } name
                         && GitHubOwners.FromRepository(name) == owner)
                {
                    notes.Add(new GitHubAccessIssueDto(owner, GitHubAccessCauses.Unseen(name.ToLowerInvariant()), code, seen));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // No notes, or none readable: the runner rewrites the file on its next refused call.
        }

        return notes;
    }

    /// <summary>Reads the packages the runner found no credential could read: <c>{"packages": {repository: {status,
    /// product, at}}}</c>, written at each submit, readiness check and registry probe, and cleared once one reads them.
    /// An unreadable or malformed file, or entry, counts as none.</summary>
    private List<GitHubAccessIssueDto> ReadPackageNotes()
    {
        var notes = new List<GitHubAccessIssueDto>();
        if (settings.Root.Length == 0)
            return notes;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(GitHubRunnerFiles.PackagesPath(settings.Root)));
            if (!document.RootElement.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Object)
                return notes;
            foreach (var entry in packages.EnumerateObject())
            {
                var note = entry.Value;
                var repository = entry.Name.ToLowerInvariant();
                var owner = repository.Split('/')[0];
                if (!PackageRepository().IsMatch(repository) || !GitHubOwners.IsOwner(owner) || note.ValueKind != JsonValueKind.Object
                    || !note.TryGetProperty("at", out var at) || at.ValueKind != JsonValueKind.String
                    || !DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var seen))
                    continue;
                int? status = note.TryGetProperty("status", out var code) && code.TryGetInt32(out var value) ? value : null;
                var product = note.TryGetProperty("product", out var named) && named.ValueKind == JsonValueKind.String
                              && named.GetString() is { } slug && ProductSlug().IsMatch(slug) ? slug : null;
                notes.Add(new GitHubAccessIssueDto(owner, GitHubAccessCauses.PackageUnreadable(repository, product), status, seen));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // No notes, or none readable: the runner rewrites the file at its next registry check.
        }

        return notes;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,38}(/[a-z0-9][a-z0-9._-]{0,127}){1,7}$")]
    private static partial Regex PackageRepository();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex ProductSlug();

    private static bool Flag(JsonElement note, string name) =>
        note.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private readonly record struct NoteKey(string Owner, NoteKind Kind);

    private sealed record Note(GitHubAccessIssueDto Issue, string? Repository);
}
