using AwesomeAssertions;
using Wheelhouse.Tests.Migrations.Harness;
using Wheelhouse.Persistence;
using WoW.Two.Sdk.Backend.Beta.Foundation.Results;
using WoW.Two.Sdk.Backend.Beta.Testing.Data.Migrations;

namespace Wheelhouse.Tests.Migrations.Tests;

/// <summary>
/// The marquee: the real embedded wheelhouse migrator over a fresh Postgres — what E2E can't isolate. A fresh DB,
/// migrated, yields every control-plane table plus the <c>002-snake-enums</c> row, history is recorded, a re-run is a
/// no-op (idempotent), and (with rollback enabled) the latest migration rolls back to pending. Runs on the SDK
/// <see cref="MigratorHarness"/> over the drop-schema <see cref="MigratorPostgresFixture"/>.
/// </summary>
[Collection(MigratorCollection.Name)]
public sealed class ApplyTests(MigratorPostgresFixture fixture)
{
    /// <summary>The five tables the 001-baseline migration creates.</summary>
    private static readonly string[] SchemaTables = ["servers", "products", "deployments", "domains", "secrets"];

    /// <summary>Builds a migrator over the shared container DB reading the real embedded wheelhouse migrations (001-baseline through 007-inventory).</summary>
    private MigratorHarness CreateMigrator(Action<WoW.Two.Sdk.Backend.Beta.Data.Migrations.Bespoke.MigrationOptions>? configure = null) =>
        MigratorHarness.CreatePostgres(fixture.ConnectionString, typeof(WheelhouseDbContext).Assembly, configure);

    [Fact]
    public async Task ApplyPending_OnFreshDb_CreatesSchema_RecordsHistory_AndIsIdempotent()
    {
        // A truly fresh DB — drop everything the prior test left so this asserts a from-nothing apply.
        await fixture.ResetAsync();

        await using var migrator = CreateMigrator();

        // First apply runs all seven real migrations in order.
        var applied = Unwrap(await migrator.Runner.ApplyPendingAsync("startup", CancellationToken.None));
        applied.Should().Equal("001-baseline", "002-snake-enums", "003-audit-updated-at", "004-audit-trail", "005-vitals-samples",
            "006-product-catalog", "007-inventory");

        // Every control-plane table the baseline declares now exists.
        foreach (var table in SchemaTables)
            (await migrator.HasTableAsync(table)).Should().BeTrue($"{table} should be created by 001-baseline");
        (await migrator.HasTableAsync("audit_entries")).Should().BeTrue("004-audit-trail creates the audit trail");
        (await migrator.HasTableAsync("vitals_samples")).Should().BeTrue("005-vitals-samples creates the readings table");
        (await migrator.HasTableAsync("integration_keys")).Should().BeTrue("006-product-catalog keeps integration keys");
        (await migrator.HasIndexAsync("ix_integration_keys_hash")).Should().BeTrue();
        foreach (var table in new[] { "targets", "vaults" })
            (await migrator.HasTableAsync(table)).Should().BeTrue($"007-inventory creates {table}");
        (await migrator.HasTableAsync("product_metadata")).Should().BeFalse("007-inventory folds lifecycles into products");
        (await migrator.HasIndexAsync("ix_servers_slug")).Should().BeTrue("007-inventory replaces the placeholder servers");

        // The migrator's own bookkeeping table exists, with one row per migration stamped by the host label.
        (await migrator.HasTableAsync("migration_history")).Should().BeTrue();
        var history = await migrator.ReadHistoryAsync();
        history.Select(r => r.Ordinal).Should().Equal(1, 2, 3, 4, 5, 6, 7);
        history.Select(r => r.Name).Should().Equal(
            "baseline", "snake-enums", "audit-updated-at", "audit-trail", "vitals-samples", "product-catalog", "inventory");
        history.Should().OnlyContain(r => r.AppliedBy == "startup");
        history.Should().OnlyContain(r => r.Checksum.Length == 64); // SHA-256 hex digest recorded at apply time.

        // GetStatus: all applied, nothing pending / drifted / orphaned.
        var status = Unwrap(await migrator.Runner.GetStatusAsync(CancellationToken.None));
        status.Applied.Select(a => a.Ordinal).Should().Equal(1, 2, 3, 4, 5, 6, 7);
        status.Pending.Should().BeEmpty();
        status.Drifted.Should().BeEmpty();
        status.Orphaned.Should().BeEmpty();

        // Second apply against an up-to-date DB is a no-op: no labels returned, history unchanged.
        var second = Unwrap(await migrator.Runner.ApplyPendingAsync("startup", CancellationToken.None));
        second.Should().BeEmpty();
        (await migrator.ReadHistoryAsync()).Should().HaveCount(7);
    }

    [Fact]
    public async Task Rollback_WithAllowRollback_RemovesLatestHistoryRow_AndReturnsItToPending()
    {
        await fixture.ResetAsync();

        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));

        // Roll back the latest migration only (007-inventory).
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: null, CancellationToken.None));

        // 007's history row and tables are gone; product_metadata and the placeholder servers table come back.
        (await migrator.ReadHistoryAsync()).Select(h => h.Ordinal).Should().Equal(1, 2, 3, 4, 5, 6);
        (await migrator.HasTableAsync("targets")).Should().BeFalse();
        (await migrator.HasTableAsync("vaults")).Should().BeFalse();
        (await migrator.HasTableAsync("product_metadata")).Should().BeTrue();
        (await migrator.HasIndexAsync("ix_servers_host")).Should().BeTrue("the placeholder servers table comes back");

        // 007 is pending again — rollback returned it to the source-but-not-applied state.
        var status = Unwrap(await migrator.Runner.GetStatusAsync(CancellationToken.None));
        status.Applied.Select(a => a.Ordinal).Should().Equal(1, 2, 3, 4, 5, 6);
        status.Pending.Select(p => p.Ordinal).Should().Equal(7);
    }

    [Fact]
    public async Task Inventory_ShouldCarryTheLegacyRegistryIntoProducts_WhenApplied()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: null, CancellationToken.None));
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: null, CancellationToken.None));

        // The legacy registry named ForeverPin 'forever-pin'; the runner always called it 'foreverpin'.
        await using var connection = await migrator.OpenConnectionAsync();
        await using (var insert = connection.CreateCommand())
        {
            // Rolling back keeps rows, so the registry is emptied to what a pre-catalog database held.
            insert.CommandText =
                "DELETE FROM products; " +
                "INSERT INTO products (id, slug, name, repo, status, created_at_utc, updated_at_utc) VALUES " +
                "(gen_random_uuid(), 'forever-pin', 'Forever Pin', 'Sulton-Max/10x-venture-forever-pin', 'active', now(), now()), " +
                "(gen_random_uuid(), 'sketch', 'Sketch', 'owner/sketch', 'draft', now(), now())";
            await insert.ExecuteNonQueryAsync();
        }

        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT slug, lifecycle, repository, release IS NOT NULL FROM products ORDER BY slug";
        var rows = new List<(string, string, string, bool)>();
        await using (var reader = await read.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
        rows.Should().Equal(
            ("foreverpin", "live", "sulton-max/10x-venture-forever-pin", true),
            ("sketch", "building", "owner/sketch", false),
            ("wheelhouse", "building", "wow-two-platform/wow-two-platform.wheelhouse", false));
    }

    /// <summary>Returns a migrator result's value, failing the test with the migrator's own message otherwise.</summary>
    private static T Unwrap<T>(Result<T> result) where T : notnull =>
        result is Result<T>.Success success
            ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
}
