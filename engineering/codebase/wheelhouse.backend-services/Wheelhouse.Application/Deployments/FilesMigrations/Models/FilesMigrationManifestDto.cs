namespace Wheelhouse.Application.Deployments.FilesMigrations.Models;

/// <summary>Represents what a target's Files migration can move and where: the sources the manifests of the release it
/// runs list, and the Files targets of its environment.</summary>
/// <param name="Tenant">The Files tenant the manifests name.</param>
/// <param name="Sources">Every source of every repository of the release, in repository then manifest order.</param>
/// <param name="FilesTargets">Every target of the <c>files</c> product in the same environment.</param>
public sealed record FilesMigrationManifestDto(
    string Tenant, IReadOnlyList<FilesMigrationSourceDto> Sources, IReadOnlyList<FilesMigrationFilesTargetDto> FilesTargets);

/// <summary>Represents one source as the console lists it.</summary>
/// <param name="Id">The source's id.</param>
/// <param name="Kind"><c>volume-folder</c>, <c>postgres-column</c> or <c>sqlite-column</c>.</param>
/// <param name="Wave">Its wave; null when the manifest sets none.</param>
/// <param name="Repository">The repository whose manifest lists it.</param>
public sealed record FilesMigrationSourceDto(string Id, string Kind, int? Wave, string? Repository);

/// <summary>Represents a Files target the files can move into.</summary>
/// <param name="Target">The target's slug.</param>
/// <param name="Urls">The Files host's https URLs, ending with <c>/</c>: its <c>app</c> site's host, then its aliases.</param>
public sealed record FilesMigrationFilesTargetDto(string Target, IReadOnlyList<string> Urls);
