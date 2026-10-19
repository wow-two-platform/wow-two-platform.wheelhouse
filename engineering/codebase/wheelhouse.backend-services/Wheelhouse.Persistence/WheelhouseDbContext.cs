using Microsoft.EntityFrameworkCore;
using Wheelhouse.Domain.Audit.Entities;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Domains.Entities;
using Wheelhouse.Domain.Integrations.Entities;
using Wheelhouse.Domain.Operations.Entities;
using Wheelhouse.Domain.Pins.Entities;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Secrets.Entities;
using Wheelhouse.Domain.SecretsVault.Entities;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Settings.Entities;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Vaults.Entities;
using Wheelhouse.Domain.Workspaces.Entities;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Naming;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Sqlite;

namespace Wheelhouse.Persistence;

/// <summary>EF Core context for the Wheelhouse control plane — a pure mapper over the Postgres schema the bespoke SQL
/// migrator owns. Each entity's mapping lives in <c>Configurations/</c>; snake_case naming and enums as snake_case text
/// apply model-wide. On the SDK <see cref="AppDbContextBase"/>, so the audit interceptor stamps the <c>IAuditable</c>
/// create and update timestamps.</summary>
public sealed class WheelhouseDbContext(DbContextOptions<WheelhouseDbContext> options) : AppDbContextBase(options)
{
    /// <summary>The EF Core SQLite provider name — gates the SQLite-only <c>DateTimeOffset</c> binary conversion (test
    /// hosts only; Npgsql maps <c>DateTimeOffset</c> natively).</summary>
    private const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";

    /// <summary>Gets the workspaces products are grouped into.</summary>
    public DbSet<WorkspaceEntity> Workspaces => Set<WorkspaceEntity>();

    /// <summary>Gets the workspaces, products and services owners keep one click away.</summary>
    public DbSet<PinEntity> Pins => Set<PinEntity>();

    /// <summary>Gets the portfolio's products.</summary>
    public DbSet<ProductEntity> Products => Set<ProductEntity>();

    /// <summary>Gets the statuses the owner gave products' services; a service without one is active.</summary>
    public DbSet<ServiceStatusEntity> ServiceStatuses => Set<ServiceStatusEntity>();

    /// <summary>Gets products' non-secret setting values per environment; secret values live in the vault.</summary>
    public DbSet<EnvironmentSettingEntity> EnvironmentSettings => Set<EnvironmentSettingEntity>();

    /// <summary>Gets the setting and secret keys owners added to products in the console.</summary>
    public DbSet<ProductSettingKeyEntity> ProductSettingKeys => Set<ProductSettingKeyEntity>();

    /// <summary>Gets what each deploy's settings render held, by fingerprint and without values.</summary>
    public DbSet<DeploymentRenderEntity> DeploymentRenders => Set<DeploymentRenderEntity>();

    /// <summary>Gets the scheduled applies of edited settings, one per target.</summary>
    public DbSet<SettingsApplyEntity> SettingsApplies => Set<SettingsApplyEntity>();

    /// <summary>Gets the built-in vault's canary row.</summary>
    public DbSet<VaultCanaryEntity> VaultCanary => Set<VaultCanaryEntity>();

    /// <summary>Gets each product's wrapped data key.</summary>
    public DbSet<VaultProductKeyEntity> VaultProductKeys => Set<VaultProductKeyEntity>();

    /// <summary>Gets the built-in vault's secrets, metadata only.</summary>
    public DbSet<VaultSecretEntity> VaultSecrets => Set<VaultSecretEntity>();

    /// <summary>Gets every encrypted value each secret has held.</summary>
    public DbSet<VaultSecretVersionEntity> VaultSecretVersions => Set<VaultSecretVersionEntity>();

    /// <summary>Gets the cached manifests of the products' repositories — one row per product.</summary>
    public DbSet<ProductManifestEntity> ProductManifests => Set<ProductManifestEntity>();

    /// <summary>Gets the hosts Wheelhouse deploys to.</summary>
    public DbSet<ServerEntity> Servers => Set<ServerEntity>();

    /// <summary>Gets each product's environments on their servers.</summary>
    public DbSet<TargetEntity> Targets => Set<TargetEntity>();

    /// <summary>Gets the secrets vaults Wheelhouse administers.</summary>
    public DbSet<VaultEntity> Vaults => Set<VaultEntity>();

    /// <summary>Gets the keys other programs present to read Wheelhouse.</summary>
    public DbSet<IntegrationKeyEntity> IntegrationKeys => Set<IntegrationKeyEntity>();

    /// <summary>Gets the placeholder deployment rows.</summary>
    public DbSet<Deployment> Deployments => Set<Deployment>();

    /// <summary>Gets the placeholder managed domains.</summary>
    public DbSet<ManagedDomain> Domains => Set<ManagedDomain>();

    /// <summary>Gets the placeholder encrypted secrets.</summary>
    public DbSet<SecretEntry> Secrets => Set<SecretEntry>();

    /// <summary>Gets the append-only, hash-chained audit trail.</summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>Gets thirty days of host and container readings.</summary>
    public DbSet<VitalsSample> VitalsSamples => Set<VitalsSample>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Base applies this assembly's entity configurations and the SDK conventions.
        base.OnModelCreating(modelBuilder);

        // Every enum as reversible snake_case text (RolledBack ↔ rolled_back); runs after the entities are mapped.
        modelBuilder.ApplyEnumStringConversions();

        // SQLite has no native DateTimeOffset — under the SQLite test provider, store each one as a binary Int64 so range
        // reads and ORDER BY match Postgres. No-op under Npgsql.
        if (Database.ProviderName == SqliteProviderName)
            modelBuilder.ApplyDateTimeOffsetToBinaryConversion();
    }
}
