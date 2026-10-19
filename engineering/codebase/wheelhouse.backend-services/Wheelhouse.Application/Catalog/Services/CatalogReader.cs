using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Catalog.Parsers;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace Wheelhouse.Application.Catalog.Services;

/// <summary>Reads every hub <see cref="CatalogSettings"/> lists, each at the newest commit of its ref (the head is read
/// again on every call, so a pushed catalog shows at once; a commit's file stays cached), and merges them in settings
/// order. A hub that cannot be read, or that repeats a workspace or entry slug an earlier hub holds, keeps its error and
/// lists nothing; the other hubs still list theirs.</summary>
public sealed class CatalogReader(IRepositoryFileSource files, CatalogSettings settings)
{
    /// <summary>Reads, parses and merges the hubs.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Each hub's outcome and the readable hubs' workspaces and entries, each tagged with its hub.</returns>
    public async Task<CatalogSnapshot> ReadAsync(CancellationToken ct)
    {
        var reads = new List<CatalogSourceRead>();
        var workspaces = new List<CatalogWorkspaceModel>();
        var entries = new List<CatalogEntryModel>();
        var owners = new Dictionary<string, string>();

        foreach (var hub in settings.Hubs)
        {
            var (commit, document, failure) = await ReadOneAsync(hub, ct);
            if (document is not null && Conflict(hub, document, owners) is { } conflict)
                (document, failure) = (null, AppErrorFactory.BusinessRule(conflict));

            if (document is not null)
            {
                workspaces.AddRange(document.Workspaces.Select(workspace => workspace with { Source = hub.Repository }));
                entries.AddRange(document.Entries.Select(entry => entry with { Source = hub.Repository }));
            }

            reads.Add(new CatalogSourceRead(
                new CatalogSourceDto
                {
                    Repository = hub.Repository,
                    Path = hub.Path,
                    Ref = hub.Ref,
                    Name = document?.Hub?.Name,
                    Commit = commit,
                    Error = failure?.Message,
                },
                failure));
        }

        return new CatalogSnapshot(reads, new CatalogDocument(workspaces, entries));
    }

    /// <summary>Reads one hub: the commit, and its catalog or why there is none (503 when GitHub cannot serve it, 404 when
    /// the file is missing, 422 naming the line and field when it is malformed).</summary>
    private async Task<(string? Commit, CatalogDocument? Document, AppError? Failure)> ReadOneAsync(
        CatalogSourceSettings hub, CancellationToken ct)
    {
        var commit = await files.HeadAsync(hub.Repository, hub.Ref, fresh: true, ct);
        if (commit is null)
            return (null, null, AppErrorFactory.ExternalUnavailable(
                $"{hub.Where} could not be read from GitHub; check the sign-in's repository access and try again."));

        var file = await files.ReadAsync(hub.Repository, commit, hub.Path, CatalogConstants.MaxCatalogBytes, ct);
        switch (file.Status)
        {
            case RepositoryFileStatus.Missing:
                return (commit, null, AppErrorFactory.FileNotFound($"{hub.Where} has no {hub.Path}."));
            case RepositoryFileStatus.TooLarge:
                return (commit, null, AppErrorFactory.BusinessRule(
                    $"{hub.Path} in {hub.Where} is larger than {CatalogConstants.MaxCatalogBytes / 1024} KiB."));
            case RepositoryFileStatus.Unreadable:
                return (commit, null, AppErrorFactory.ExternalUnavailable(
                    $"{hub.Path} in {hub.Where} could not be read from GitHub; try again."));
        }

        var parsed = CatalogParser.Parse(file.Content!, hub.Path);
        return parsed.Value is null
            ? (commit, null, AppErrorFactory.BusinessRule($"{hub.Path} in {hub.Where}, {parsed.Problem}"))
            : (commit, parsed.Value, null);
    }

    /// <summary>Names the first workspace or entry slug the hub repeats from an earlier hub, with both hubs, or returns
    /// <c>null</c> and records the hub's slugs as its own.</summary>
    /// <param name="hub">The hub just read.</param>
    /// <param name="document">Its catalog.</param>
    /// <param name="owners">Each earlier slug, keyed <c>workspace:{slug}</c> or <c>entry:{slug}</c>, with the hub that holds it.</param>
    private static string? Conflict(CatalogSourceSettings hub, CatalogDocument document, Dictionary<string, string> owners)
    {
        var here = $"{hub.Path} in {hub.Where}";
        if (document.Workspaces.FirstOrDefault(workspace => owners.ContainsKey("workspace:" + workspace.Slug)) is { } workspace)
            return $"{here} declares the workspace '{workspace.Slug}', which {owners["workspace:" + workspace.Slug]} declares too; "
                + "each workspace belongs to one hub.";
        if (document.Entries.FirstOrDefault(entry => owners.ContainsKey("entry:" + entry.Slug)) is { } entry)
            return $"{here} lists the entry '{entry.Slug}', which {owners["entry:" + entry.Slug]} lists too; "
                + "each entry belongs to one hub.";

        foreach (var declared in document.Workspaces)
            owners["workspace:" + declared.Slug] = here;
        foreach (var listed in document.Entries)
            owners["entry:" + listed.Slug] = here;
        return null;
    }
}
