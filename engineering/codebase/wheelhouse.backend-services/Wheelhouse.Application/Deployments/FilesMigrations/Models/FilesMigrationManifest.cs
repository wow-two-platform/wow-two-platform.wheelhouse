namespace Wheelhouse.Application.Deployments.FilesMigrations.Models;

/// <summary>Represents the validated <c>engineering/deployment/files-migration.yml</c> of one repository.</summary>
/// <param name="Tenant">The Files tenant the product's files belong to.</param>
/// <param name="Sources">The sources, in manifest order.</param>
public sealed record FilesMigrationManifest(string Tenant, IReadOnlyList<FilesMigrationSource> Sources);
