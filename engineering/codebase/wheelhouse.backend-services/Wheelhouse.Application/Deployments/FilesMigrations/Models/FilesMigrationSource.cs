namespace Wheelhouse.Application.Deployments.FilesMigrations.Models;

/// <summary>Represents one source of a product's legacy files, as its <c>files-migration.yml</c> declares it. The mover
/// reads the fields as written, so they travel to the runner whole.</summary>
/// <param name="Id">The source's unique id.</param>
/// <param name="Wave">The wave it belongs to (1 or more); null when the manifest sets none.</param>
/// <param name="Kind"><c>volume-folder</c>, <c>postgres-column</c> or <c>sqlite-column</c>.</param>
/// <param name="Fields">The manifest object as written: <c>id</c>, <c>wave</c>, <c>kind</c> and the kind's fields, a
/// <c>key</c> being a string or a list of strings.</param>
/// <param name="Repository">The repository whose manifest lists it; set when the manifests are read, null from the parser.</param>
public sealed record FilesMigrationSource(
    string Id, int? Wave, string Kind, IReadOnlyDictionary<string, object> Fields, string? Repository = null);
