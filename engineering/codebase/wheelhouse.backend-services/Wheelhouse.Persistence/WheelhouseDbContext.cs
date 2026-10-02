using Microsoft.EntityFrameworkCore;
using Wheelhouse.Domain.Audit.Entities;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Domains.Entities;
using Wheelhouse.Domain.Integrations.Entities;
using Wheelhouse.Domain.Operations.Entities;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Secrets.Entities;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Vaults.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Backend.Beta.Data.EntityFrameworkCore.Naming;
using WoW.Two.Sdk.Backend.Beta.Data.EntityFrameworkCore.Sqlite;

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

    /// <summary>Gets the portfolio's products.</summary>
    public DbSet<ProductEntity> Products => Set<ProductEntity>();

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
