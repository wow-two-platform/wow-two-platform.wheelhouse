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

    /// <summary>Builds a migrator over the shared container DB reading the real embedded wheelhouse migrations (001-baseline through 008-product-manifests).</summary>
    private MigratorHarness CreateMigrator(Action<WoW.Two.Sdk.Backend.Beta.Data.Migrations.Bespoke.MigrationOptions>? configure = null) =>
        MigratorHarness.CreatePostgres(fixture.ConnectionString, typeof(WheelhouseDbContext).Assembly, configure);

    [Fact]
    public async Task ApplyPending_OnFreshDb_CreatesSchema_RecordsHistory_AndIsIdempotent()
    {
        // A truly fresh DB — drop everything the prior test left so this asserts a from-nothing apply.
        await fixture.ResetAsync();

        await using var migrator = CreateMigrator();

        // First apply runs all eight real migrations in order.
        var applied = Unwrap(await migrator.Runner.ApplyPendingAsync("startup", CancellationToken.None));
        applied.Should().Equal("001-baseline", "002-snake-enums", "003-audit-updated-at", "004-audit-trail", "005-vitals-samples",
            "006-product-catalog", "007-inventory", "008-product-manifests");

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
        (await migrator.HasTableAsync("product_manifests")).Should().BeTrue("008-product-manifests caches each product's manifest");

        // The migrator's own bookkeeping table exists, with one row per migration stamped by the host label.
        (await migrator.HasTableAsync("migration_history")).Should().BeTrue();
        var history = await migrator.ReadHistoryAsync();
        history.Select(r => r.Ordinal).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        history.Select(r => r.Name).Should().Equal(
            "baseline", "snake-enums", "audit-updated-at", "audit-trail", "vitals-samples", "product-catalog", "inventory",
            "product-manifests");
        history.Should().OnlyContain(r => r.AppliedBy == "startup");
        history.Should().OnlyContain(r => r.Checksum.Length == 64); // SHA-256 hex digest recorded at apply time.

        // GetStatus: all applied, nothing pending / drifted / orphaned.
        var status = Unwrap(await migrator.Runner.GetStatusAsync(CancellationToken.None));
        status.Applied.Select(a => a.Ordinal).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        status.Pending.Should().BeEmpty();
        status.Drifted.Should().BeEmpty();
        status.Orphaned.Should().BeEmpty();

        // Second apply against an up-to-date DB is a no-op: no labels returned, history unchanged.
        var second = Unwrap(await migrator.Runner.ApplyPendingAsync("startup", CancellationToken.None));
        second.Should().BeEmpty();
        (await migrator.ReadHistoryAsync()).Should().HaveCount(8);
    }

    [Fact]
    public async Task Rollback_WithAllowRollback_RemovesLatestHistoryRow_AndReturnsItToPending()
    {
        await fixture.ResetAsync();

        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));

        // Roll back the two latest migrations (008-product-manifests, then 007-inventory).
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: null, CancellationToken.None));
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: null, CancellationToken.None));

        // 007's history row and tables are gone; product_metadata and the placeholder servers table come back.
        (await migrator.ReadHistoryAsync()).Select(h => h.Ordinal).Should().Equal(1, 2, 3, 4, 5, 6);
        (await migrator.HasTableAsync("targets")).Should().BeFalse();
        (await migrator.HasTableAsync("vaults")).Should().BeFalse();
        (await migrator.HasTableAsync("product_metadata")).Should().BeTrue();
        (await migrator.HasIndexAsync("ix_servers_host")).Should().BeTrue("the placeholder servers table comes back");

        // 007 and 008 are pending again — rollback returned them to the source-but-not-applied state.
        (await migrator.HasTableAsync("product_manifests")).Should().BeFalse();
        var status = Unwrap(await migrator.Runner.GetStatusAsync(CancellationToken.None));
        status.Applied.Select(a => a.Ordinal).Should().Equal(1, 2, 3, 4, 5, 6);
        status.Pending.Select(p => p.Ordinal).Should().Equal(7, 8);
    }

    [Fact]
    public async Task Inventory_ShouldCarryTheLegacyRegistryIntoProducts_WhenApplied()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: null, CancellationToken.None));
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

    [Fact]
    public async Task ProductManifests_ShouldHoldOneRowPerProductAndGoWithIt_WhenApplied()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator();
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));

        await using var connection = await migrator.OpenConnectionAsync();
        var columns = await ReadColumnsAsync(connection, "product_manifests");
        columns.Should().Equal(
            ("product_id", "uuid", false), ("repository", "text", false), ("branch", "text", false),
            ("commit_sha", "text", false), ("name", "text", false), ("description", "text", false),
            ("homepage", "text", true), ("services", "jsonb", false), ("created_at_utc", "timestamp with time zone", false),
            ("updated_at_utc", "timestamp with time zone", false));

        await Execute(connection,
            "INSERT INTO products (id, slug, name, description, repository, default_branch, lifecycle, created_at_utc, updated_at_utc) " +
            "VALUES ('00000000-0000-0000-0000-000000000001', 'pilot', 'Pilot', '', 'owner/pilot', 'main', 'building', now(), now())");
        const string insert =
            "INSERT INTO product_manifests (product_id, repository, branch, commit_sha, name, description, services, created_at_utc, updated_at_utc) " +
            "VALUES ('00000000-0000-0000-0000-000000000001', 'owner/pilot', 'main', '{0}', 'Pilot', 'x', '[]', now(), now())";
        await Execute(connection, string.Format(insert, new string('a', 40)));

        // One row per product: a second row for the same product is refused.
        var duplicate = async () => await Execute(connection, string.Format(insert, new string('b', 40)));
        await duplicate.Should().ThrowAsync<Npgsql.PostgresException>();

        // A manifest cannot name a product that does not exist, and goes when its product goes.
        var orphan = async () => await Execute(connection, string.Format(insert, new string('c', 40)).Replace("0000000000001", "0000000000002"));
        await orphan.Should().ThrowAsync<Npgsql.PostgresException>();
        await Execute(connection, "DELETE FROM products WHERE slug = 'pilot'");
        (await Scalar(connection, "SELECT count(*) FROM product_manifests")).Should().Be(0L);
    }

    [Fact]
    public async Task ProductManifests_ShouldRollBackToNothingAndApplyAgain_WhenTheMigrationIsReversed()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();
        await Execute(connection,
            "INSERT INTO products (id, slug, name, description, repository, default_branch, lifecycle, created_at_utc, updated_at_utc) " +
            "VALUES ('00000000-0000-0000-0000-000000000001', 'pilot', 'Pilot', '', 'owner/pilot', 'main', 'building', now(), now())");
        await Execute(connection,
            "INSERT INTO product_manifests (product_id, repository, branch, commit_sha, name, description, services, created_at_utc, updated_at_utc) " +
            $"VALUES ('00000000-0000-0000-0000-000000000001', 'owner/pilot', 'main', '{new string('a', 40)}', 'Pilot', 'x', '[]', now(), now())");

        // Apply → Rollback: the table goes and the rest of the schema, products included, is untouched.
        var products = await Scalar(connection, "SELECT count(*) FROM products");
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: null, CancellationToken.None));
        (await migrator.HasTableAsync("product_manifests")).Should().BeFalse();
        (await Scalar(connection, "SELECT count(*) FROM products")).Should().Be(products);
        (await migrator.ReadHistoryAsync()).Select(h => h.Ordinal).Should().Equal(1, 2, 3, 4, 5, 6, 7);
        Unwrap(await migrator.Runner.GetStatusAsync(CancellationToken.None)).Pending.Select(p => p.Name)
            .Should().Equal("product-manifests");

        // Rollback → Apply: the table is back and empty, since it only ever cached repository files.
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal("008-product-manifests");
        (await migrator.HasTableAsync("product_manifests")).Should().BeTrue();
        (await Scalar(connection, "SELECT count(*) FROM product_manifests")).Should().Be(0L);
        (await ReadColumnsAsync(connection, "product_manifests")).Should().HaveCount(10);

        // A second reversal in the same database is as clean as the first.
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: null, CancellationToken.None));
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        (await migrator.ReadHistoryAsync()).Select(h => h.Ordinal).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
    }

    private static async Task Execute(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> Scalar(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task<List<(string Name, string Type, bool Nullable)>> ReadColumnsAsync(System.Data.Common.DbConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name, data_type, is_nullable = 'YES' FROM information_schema.columns " +
            $"WHERE table_schema = 'public' AND table_name = '{table}' ORDER BY ordinal_position";
        var columns = new List<(string, string, bool)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));
        return columns;
    }

    /// <summary>Returns a migrator result's value, failing the test with the migrator's own message otherwise.</summary>
    private static T Unwrap<T>(Result<T> result) where T : notnull =>
        result is Result<T>.Success success
            ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
}
