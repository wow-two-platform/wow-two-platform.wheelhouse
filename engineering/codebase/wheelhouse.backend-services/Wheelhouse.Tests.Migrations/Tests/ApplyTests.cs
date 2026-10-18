using AwesomeAssertions;
using Wheelhouse.Tests.Migrations.Harness;
using Wheelhouse.Persistence;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

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

    /// <summary>Every embedded migration, in the order it applies.</summary>
    private static readonly string[] Migrations =
    [
        "001-baseline", "002-snake-enums", "003-audit-updated-at", "004-audit-trail", "005-vitals-samples",
        "006-product-catalog", "007-inventory", "008-product-manifests", "009-server-vitals", "010-workspaces",
        "011-product-status", "012-pins", "013-drop-test-environment", "015-product-pins-only",
        "016-deploy-automation", "017-deploy-freezes",
        "018-auto-promotions",
    ];

    /// <summary>The migrations after <paramref name="ordinal"/>, as the migrator labels them.</summary>
    private static string[] After(int ordinal) => Migrations[ordinal..];

    /// <summary>The migrations from <paramref name="name"/> on, as the migrator labels them.</summary>
    private static string[] From(string name) => Migrations[Array.IndexOf(Migrations, name)..];

    /// <summary>The ordinals 1 through <paramref name="ordinal"/>.</summary>
    private static int[] Through(int ordinal) => [.. Enumerable.Range(1, ordinal)];

    /// <summary>Every embedded migration's ordinal, in the order it applies; numbers may skip, since lanes claim them.</summary>
    private static int[] Ordinals => [.. Migrations.Select(label => int.Parse(label[..3], System.Globalization.CultureInfo.InvariantCulture))];

    /// <summary>The SQL that stores a bare product in the default workspace, as the schema stands after every migration.</summary>
    private static string InsertProduct(string id, string slug) =>
        "INSERT INTO products (id, slug, name, description, repository, default_branch, status, workspace_id, created_at_utc, updated_at_utc) " +
        $"VALUES ('{id}', '{slug}', '{slug}', '', 'owner/{slug}', 'main', 'active', " +
        "(SELECT id FROM workspaces WHERE slug = 'own-ventures'), now(), now())";

    /// <summary>Builds a migrator over the shared container DB reading the real embedded wheelhouse migrations.</summary>
    private MigratorHarness CreateMigrator(Action<WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke.MigrationOptions>? configure = null) =>
        MigratorHarness.CreatePostgres(fixture.ConnectionString, typeof(WheelhouseDbContext).Assembly, configure);

    [Fact]
    public async Task ApplyPending_OnFreshDb_CreatesSchema_RecordsHistory_AndIsIdempotent()
    {
        // A truly fresh DB — drop everything the prior test left so this asserts a from-nothing apply.
        await fixture.ResetAsync();

        await using var migrator = CreateMigrator();

        // First apply runs every real migration in order.
        var applied = Unwrap(await migrator.Runner.ApplyPendingAsync("startup", CancellationToken.None));
        applied.Should().Equal(Migrations);

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
        (await migrator.HasTableAsync("workspaces")).Should().BeTrue("010-workspaces groups products");
        (await migrator.HasIndexAsync("ix_workspaces_slug")).Should().BeTrue();

        // The migrator's own bookkeeping table exists, with one row per migration stamped by the host label.
        (await migrator.HasTableAsync("migration_history")).Should().BeTrue();
        var history = await migrator.ReadHistoryAsync();
        history.Select(r => r.Ordinal).Should().Equal(Ordinals);
        history.Select(r => r.Name).Should().Equal(Migrations.Select(label => label[4..]));
        history.Should().OnlyContain(r => r.AppliedBy == "startup");
        history.Should().OnlyContain(r => r.Checksum.Length == 64); // SHA-256 hex digest recorded at apply time.

        // GetStatus: all applied, nothing pending / drifted / orphaned.
        var status = Unwrap(await migrator.Runner.GetStatusAsync(CancellationToken.None));
        status.Applied.Select(a => a.Ordinal).Should().Equal(Ordinals);
        status.Pending.Should().BeEmpty();
        status.Drifted.Should().BeEmpty();
        status.Orphaned.Should().BeEmpty();

        // Second apply against an up-to-date DB is a no-op: no labels returned, history unchanged.
        var second = Unwrap(await migrator.Runner.ApplyPendingAsync("startup", CancellationToken.None));
        second.Should().BeEmpty();
        (await migrator.ReadHistoryAsync()).Should().HaveCount(Migrations.Length);
    }

    [Fact]
    public async Task Rollback_WithAllowRollback_RemovesLatestHistoryRow_AndReturnsItToPending()
    {
        await fixture.ResetAsync();

        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));

        // Roll back one at a time, newest first, down through 007-inventory.
        while ((await migrator.ReadHistoryAsync()).Count > 6)
            Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: null, CancellationToken.None));

        // 007's history row and tables are gone; product_metadata and the placeholder servers table come back.
        (await migrator.ReadHistoryAsync()).Select(h => h.Ordinal).Should().Equal(1, 2, 3, 4, 5, 6);
        (await migrator.HasTableAsync("targets")).Should().BeFalse();
        (await migrator.HasTableAsync("vaults")).Should().BeFalse();
        (await migrator.HasTableAsync("product_metadata")).Should().BeTrue();
        (await migrator.HasIndexAsync("ix_servers_host")).Should().BeTrue("the placeholder servers table comes back");

        // 007 onwards are pending again — rollback returned them to the source-but-not-applied state.
        (await migrator.HasTableAsync("product_manifests")).Should().BeFalse();
        (await migrator.HasTableAsync("workspaces")).Should().BeFalse();
        var status = Unwrap(await migrator.Runner.GetStatusAsync(CancellationToken.None));
        status.Applied.Select(a => a.Ordinal).Should().Equal(1, 2, 3, 4, 5, 6);
        status.Pending.Select(p => p.Ordinal).Should().Equal(Ordinals[6..]);
    }

    [Fact]
    public async Task Inventory_ShouldCarryTheLegacyRegistryIntoProducts_WhenApplied()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 5, CancellationToken.None)); // Everything from 006 on.

        // The legacy registry named ForeverPin 'forever-pin'; the runner always called it 'foreverpin'.
        await using var connection = await migrator.OpenConnectionAsync();
        await using (var insert = connection.CreateCommand())
        {
            // Rolling back keeps rows, so the registry is emptied to what a pre-catalog database held.
            insert.CommandText =
                "DELETE FROM products; " +
                "INSERT INTO products (id, slug, name, repo, status, created_at_utc, updated_at_utc) VALUES " +
                "(gen_random_uuid(), 'forever-pin', 'Forever Pin', 'Sulton-Max/10x-venture-forever-pin', 'active', now(), now()), " +
                "(gen_random_uuid(), 'sketch', 'Sketch', 'owner/sketch', 'draft', now(), now()), " +
                "(gen_random_uuid(), 'shelved', 'Shelved', 'owner/shelved', 'paused', now(), now())";
            await insert.ExecuteNonQueryAsync();
        }

        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));

        await using var read = connection.CreateCommand();
        // Through 007's lifecycle and 011's status: a paused registry row ends inactive, every other one active.
        read.CommandText = "SELECT slug, status, repository, release IS NOT NULL FROM products ORDER BY slug";
        var rows = new List<(string, string, string, bool)>();
        await using (var reader = await read.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
        rows.Should().Equal(
            ("foreverpin", "active", "sulton-max/10x-venture-forever-pin", true),
            ("shelved", "inactive", "owner/shelved", false),
            ("sketch", "active", "owner/sketch", false),
            ("wheelhouse", "active", "wow-two-platform/wow-two-platform.wheelhouse", false));
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

        await Execute(connection, InsertProduct("00000000-0000-0000-0000-000000000001", "pilot"));
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
        await Execute(connection, InsertProduct("00000000-0000-0000-0000-000000000001", "pilot"));
        await Execute(connection,
            "INSERT INTO product_manifests (product_id, repository, branch, commit_sha, name, description, services, created_at_utc, updated_at_utc) " +
            $"VALUES ('00000000-0000-0000-0000-000000000001', 'owner/pilot', 'main', '{new string('a', 40)}', 'Pilot', 'x', '[]', now(), now())");

        // Apply → Rollback (newest first, down through 008): the table goes and the rest of the schema, products
        // included, is untouched.
        var products = await Scalar(connection, "SELECT count(*) FROM products");
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 7, CancellationToken.None));
        (await migrator.HasTableAsync("product_manifests")).Should().BeFalse();
        (await Scalar(connection, "SELECT count(*) FROM products")).Should().Be(products);
        (await migrator.ReadHistoryAsync()).Select(h => h.Ordinal).Should().Equal(Through(7));
        Unwrap(await migrator.Runner.GetStatusAsync(CancellationToken.None)).Pending.Select(p => p.Name)
            .Should().Equal(After(7).Select(label => label[4..]));

        // Rollback → Apply: the table is back and empty, since it only ever cached repository files.
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal(After(7));
        (await migrator.HasTableAsync("product_manifests")).Should().BeTrue();
        (await Scalar(connection, "SELECT count(*) FROM product_manifests")).Should().Be(0L);
        (await ReadColumnsAsync(connection, "product_manifests")).Should().HaveCount(10);

        // A second reversal in the same database is as clean as the first.
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 7, CancellationToken.None));
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        (await migrator.ReadHistoryAsync()).Select(h => h.Ordinal).Should().Equal(Ordinals);
    }

    [Fact]
    public async Task ServerVitals_ShouldLiftEachPassHostFiguresIntoServerRows_WhenApplied()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 8, CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();

        // Two passes read the local host through two targets each; the fleet host's one target could not be read.
        const string row =
            "INSERT INTO vitals_samples (id, target_id, server_id, sampled_at_utc, readable, load_percent, memory_percent, " +
            "disk_percent, containers, healthy_containers, restarts) VALUES (gen_random_uuid(), '{0}', '{1}', '{2}', {3}, {4}, 3, 2, 1)";
        foreach (var (target, server, at, readable, figures) in new[]
        {
            ("pin-test", "local", "2026-10-17T10:00:00Z", true, "20, 50, 40"),
            ("pin-dev", "local", "2026-10-17T10:00:00Z", false, "NULL, NULL, NULL"),
            ("pin-dev", "local", "2026-10-17T10:05:00Z", true, "30, 60, 41"),
            ("pin-test", "local", "2026-10-17T10:05:00Z", true, "30, 60, 41"),
            ("pin-prod", "vps", "2026-10-17T10:05:00Z", false, "NULL, NULL, NULL"),
        })
            await Execute(connection, string.Format(row, target, server, at, readable ? "true" : "false", figures));

        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal(After(8));

        // One server row per server and pass, from a readable target row with figures where the pass had one.
        (await ReadColumnsAsync(connection, "vitals_samples")).Should().Contain(("target_id", "text", true));
        var lifted = new List<(string, string, bool, double?, int)>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT server_id, to_char(sampled_at_utc AT TIME ZONE 'UTC', 'HH24:MI'), readable, memory_percent, " +
                "containers FROM vitals_samples WHERE target_id IS NULL ORDER BY server_id, sampled_at_utc";
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                lifted.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2),
                    reader.IsDBNull(3) ? null : reader.GetDouble(3), reader.GetInt32(4)));
        }
        lifted.Should().Equal(("local", "10:00", true, 50.0, 0), ("local", "10:05", true, 60.0, 0), ("vps", "10:05", false, null, 0));
        (await Scalar(connection, "SELECT count(*) FROM vitals_samples WHERE target_id IS NOT NULL")).Should().Be(5L);

        // Rollback drops the server rows and requires a target again.
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 8, CancellationToken.None));
        (await Scalar(connection, "SELECT count(*) FROM vitals_samples")).Should().Be(5L);
        (await ReadColumnsAsync(connection, "vitals_samples")).Should().Contain(("target_id", "text", false));
    }

    [Fact]
    public async Task Workspaces_ShouldPutEveryExistingProductInTheDefaultWorkspace_WhenApplied()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 9, CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();
        foreach (var slug in new[] { "alpha", "beta" })
            await Execute(connection,
                "INSERT INTO products (id, slug, name, description, repository, default_branch, lifecycle, created_at_utc, updated_at_utc) " +
                $"VALUES (gen_random_uuid(), '{slug}', '{slug}', '', 'owner/{slug}', 'main', 'live', now(), now())");

        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal(After(9));

        (await ReadColumnsAsync(connection, "workspaces")).Should().Equal(
            ("id", "uuid", false), ("slug", "text", false), ("name", "text", false), ("description", "text", true),
            ("sort_order", "integer", false), ("created_at_utc", "timestamp with time zone", false),
            ("updated_at_utc", "timestamp with time zone", false));
        (await ReadColumnsAsync(connection, "products")).Should().Contain(("workspace_id", "uuid", false));
        (await Scalar(connection, "SELECT slug || '|' || name || '|' || sort_order || '|' || (description IS NULL) FROM workspaces"))
            .Should().Be("own-ventures|Own ventures|0|true");
        // 007's two products and the two inserted here all land in it.
        (await Scalar(connection, "SELECT count(*) FROM products p JOIN workspaces w ON w.id = p.workspace_id WHERE w.slug = 'own-ventures'"))
            .Should().Be(4L);
        (await Scalar(connection, "SELECT count(*) FROM products")).Should().Be(4L);

        // The slug is unique, a product needs an existing workspace, and a workspace holding a product stays.
        var duplicate = async () => await Execute(connection,
            "INSERT INTO workspaces (id, slug, name, sort_order, created_at_utc, updated_at_utc) VALUES (gen_random_uuid(), 'own-ventures', 'x', 1, now(), now())");
        await duplicate.Should().ThrowAsync<Npgsql.PostgresException>();
        var stray = async () => await Execute(connection,
            "UPDATE products SET workspace_id = gen_random_uuid() WHERE slug = 'alpha'");
        await stray.Should().ThrowAsync<Npgsql.PostgresException>();
        var orphaning = async () => await Execute(connection, "DELETE FROM workspaces");
        await orphaning.Should().ThrowAsync<Npgsql.PostgresException>();
    }

    [Fact]
    public async Task Workspaces_ShouldRollBackToNoWorkspacesAndApplyAgain_WhenReversed()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();
        await Execute(connection, InsertProduct("00000000-0000-0000-0000-000000000001", "pilot"));
        var products = await Scalar(connection, "SELECT count(*) FROM products");

        for (var cycle = 0; cycle < 2; cycle++)
        {
            Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 9, CancellationToken.None));
            (await migrator.HasTableAsync("workspaces")).Should().BeFalse();
            (await ReadColumnsAsync(connection, "products")).Select(column => column.Name).Should().NotContain("workspace_id");
            (await Scalar(connection, "SELECT count(*) FROM products")).Should().Be(products);

            Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal(After(9));
            (await Scalar(connection, "SELECT count(*) FROM workspaces")).Should().Be(1L);
            (await Scalar(connection, "SELECT count(*) FROM products WHERE workspace_id IS NOT NULL")).Should().Be(products);
        }
    }

    [Fact]
    public async Task ProductStatus_ShouldMapPausedAndKilledToInactiveAndEveryOtherLifecycleToActive_WhenApplied()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 10, CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();
        await Execute(connection, "DELETE FROM products");
        foreach (var lifecycle in new[] { "idea", "building", "live", "paused", "killed" })
            await Execute(connection,
                "INSERT INTO products (id, slug, name, description, repository, default_branch, lifecycle, workspace_id, created_at_utc, updated_at_utc) " +
                $"VALUES (gen_random_uuid(), '{lifecycle}', '{lifecycle}', '', 'owner/{lifecycle}', 'main', '{lifecycle}', " +
                "(SELECT id FROM workspaces WHERE slug = 'own-ventures'), now(), now())");

        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal(After(10));

        var mapped = new List<(string, string)>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT slug, status FROM products ORDER BY slug";
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                mapped.Add((reader.GetString(0), reader.GetString(1)));
        }
        mapped.Should().Equal(("building", "active"), ("idea", "active"), ("killed", "inactive"), ("live", "active"), ("paused", "inactive"));
        var columns = await ReadColumnsAsync(connection, "products");
        columns.Should().Contain(("status", "text", false));
        columns.Select(column => column.Name).Should().NotContain("lifecycle");
        (await ReadColumnsAsync(connection, "service_statuses")).Should().Equal(
            ("product_id", "uuid", false), ("service", "text", false), ("status", "text", false),
            ("created_at_utc", "timestamp with time zone", false), ("updated_at_utc", "timestamp with time zone", false));

        // One row per product and service, only for a product that exists, gone with its product.
        const string row =
            "INSERT INTO service_statuses (product_id, service, status, created_at_utc, updated_at_utc) " +
            "VALUES ((SELECT id FROM products WHERE slug = '{0}'), 'api', 'inactive', now(), now())";
        await Execute(connection, string.Format(row, "live"));
        var duplicate = async () => await Execute(connection, string.Format(row, "live"));
        await duplicate.Should().ThrowAsync<Npgsql.PostgresException>();
        var orphan = async () => await Execute(connection,
            "INSERT INTO service_statuses (product_id, service, status, created_at_utc, updated_at_utc) VALUES (gen_random_uuid(), 'api', 'inactive', now(), now())");
        await orphan.Should().ThrowAsync<Npgsql.PostgresException>();
        await Execute(connection, "DELETE FROM products WHERE slug = 'live'");
        (await Scalar(connection, "SELECT count(*) FROM service_statuses")).Should().Be(0L);
    }

    [Fact]
    public async Task ProductStatus_ShouldRollBackToALifecycleAndApplyAgain_WhenReversed()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();
        await Execute(connection, "UPDATE products SET status = 'inactive' WHERE slug = 'wheelhouse'");
        await Execute(connection,
            "INSERT INTO service_statuses (product_id, service, status, created_at_utc, updated_at_utc) " +
            "SELECT id, 'console', 'inactive', now(), now() FROM products WHERE slug = 'wheelhouse'");

        for (var cycle = 0; cycle < 2; cycle++)
        {
            Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 10, CancellationToken.None));
            (await migrator.HasTableAsync("service_statuses")).Should().BeFalse();
            (await ReadColumnsAsync(connection, "products")).Select(column => column.Name).Should().Contain("lifecycle").And.NotContain("status");
            (await Scalar(connection, "SELECT string_agg(slug || ':' || lifecycle, ',' ORDER BY slug) FROM products"))
                .Should().Be("foreverpin:building,wheelhouse:paused");

            Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal(After(10));
            (await Scalar(connection, "SELECT string_agg(slug || ':' || status, ',' ORDER BY slug) FROM products"))
                .Should().Be("foreverpin:active,wheelhouse:inactive");
            (await Scalar(connection, "SELECT count(*) FROM service_statuses")).Should().Be(0L);
        }
    }

    [Fact]
    public async Task Pins_ShouldHoldOnePinPerOwnerAndTargetAndGoWithIt_WhenApplied()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator();
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();

        (await ReadColumnsAsync(connection, "pins")).Should().Equal(
            ("id", "uuid", false), ("owner", "text", false), ("kind", "text", false), ("workspace_id", "uuid", true),
            ("product_id", "uuid", true), ("service", "text", true), ("sort_order", "integer", false),
            ("created_at_utc", "timestamp with time zone", false));
        (await migrator.HasIndexAsync("ix_pins_target")).Should().BeTrue();
        (await migrator.HasIndexAsync("ix_pins_owner_order")).Should().BeTrue();

        await Execute(connection, InsertProduct("00000000-0000-0000-0000-000000000001", "pilot"));
        const string pin =
            "INSERT INTO pins (id, owner, kind, workspace_id, product_id, service, sort_order, created_at_utc) " +
            "VALUES (gen_random_uuid(), '{0}', '{1}', {2}, {3}, {4}, 0, now())";
        const string workspace = "(SELECT id FROM workspaces WHERE slug = 'own-ventures')";
        const string product = "'00000000-0000-0000-0000-000000000001'";
        await Execute(connection, string.Format(pin, "max", "workspace", workspace, "NULL", "NULL"));
        await Execute(connection, string.Format(pin, "max", "product", "NULL", product, "NULL"));
        await Execute(connection, string.Format(pin, "max", "service", "NULL", product, "'api'"));
        await Execute(connection, string.Format(pin, "other", "product", "NULL", product, "NULL"));

        // An owner pins a target once, its null parts included; a pin names exactly the target its kind takes.
        foreach (var refused in new[]
        {
            string.Format(pin, "max", "product", "NULL", product, "NULL"),
            string.Format(pin, "max", "workspace", workspace, "NULL", "NULL"),
            string.Format(pin, "max", "workspace", "NULL", "NULL", "NULL"),
            string.Format(pin, "max", "product", workspace, product, "NULL"),
            string.Format(pin, "max", "service", "NULL", product, "NULL"),
            string.Format(pin, "max", "product", "NULL", "gen_random_uuid()", "NULL"),
        })
        {
            var insert = async () => await Execute(connection, refused);
            await insert.Should().ThrowAsync<Npgsql.PostgresException>(refused);
        }

        // A pin goes with the product or workspace it points at.
        await Execute(connection, "DELETE FROM products WHERE slug = 'pilot'");
        (await Scalar(connection, "SELECT string_agg(owner || ':' || kind, ',') FROM pins")).Should().Be("max:workspace");
        await Execute(connection, "DELETE FROM products");
        await Execute(connection, "DELETE FROM workspaces");
        (await Scalar(connection, "SELECT count(*) FROM pins")).Should().Be(0L);
    }

    [Fact]
    public async Task Pins_ShouldRollBackToNoPinsAndApplyAgain_WhenReversed()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();
        await Execute(connection,
            "INSERT INTO pins (id, owner, kind, workspace_id, sort_order, created_at_utc) " +
            "SELECT gen_random_uuid(), 'max', 'workspace', id, 0, now() FROM workspaces");

        for (var cycle = 0; cycle < 2; cycle++)
        {
            Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 11, CancellationToken.None));
            (await migrator.HasTableAsync("pins")).Should().BeFalse();
            (await Scalar(connection, "SELECT count(*) FROM workspaces")).Should().Be(1L);

            Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal(After(11));
            (await Scalar(connection, "SELECT count(*) FROM pins")).Should().Be(0L);
        }
    }

    [Fact]
    public async Task DropTestEnvironment_ShouldDeleteTestTargetsAndTheirReadings_AndKeepEverythingElse()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 12, CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();

        // A product on one server in dev, test and prod, each with a reading, beside the server's own host reading.
        await Execute(connection, InsertProduct("00000000-0000-0000-0000-000000000001", "pilot"));
        await Execute(connection,
            "INSERT INTO servers (id, slug, name, provider, host, region, ssh_user, ssh_port, ingress, created_at_utc, updated_at_utc) " +
            "VALUES ('00000000-0000-0000-0000-0000000000a1', 'hel1', 'Helsinki', 'hetzner', 'vps.example.net', 'hel1', " +
            "'deploy', 22, '{}'::jsonb, now(), now())");
        foreach (var environment in new[] { "dev", "test", "prod" })
        {
            await Execute(connection,
                "INSERT INTO targets (id, slug, product_id, server_id, environment, network, root, settings, smoke_checks, " +
                "sites, created_at_utc, updated_at_utc) VALUES (gen_random_uuid(), 'pilot-" + environment + "', " +
                "'00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-0000000000a1', '" + environment + "', " +
                "'platform', '/srv/wheelhouse', '[]'::jsonb, '[]'::jsonb, '[]'::jsonb, now(), now())");
            await Execute(connection, ReadingFor("'pilot-" + environment + "'"));
        }
        await Execute(connection, ReadingFor("NULL"));
        await Execute(connection, "INSERT INTO service_statuses (product_id, service, status, created_at_utc, updated_at_utc) " +
                                  "VALUES ('00000000-0000-0000-0000-000000000001', 'api', 'inactive', now(), now())");

        for (var run = 0; run < 2; run++)
        {
            Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal(After(12));

            (await Scalar(connection, "SELECT string_agg(slug, ',' ORDER BY slug) FROM targets"))
                .Should().Be("pilot-dev,pilot-prod");
            (await Scalar(connection, "SELECT string_agg(COALESCE(target_id, 'host'), ',' ORDER BY target_id) FROM vitals_samples"))
                .Should().Be("pilot-dev,pilot-prod,host");
            (await Scalar(connection, "SELECT count(*) FROM products WHERE slug = 'pilot'")).Should().Be(1L);
            (await Scalar(connection, "SELECT count(*) FROM service_statuses")).Should().Be(1L);

            // Forward-only: the rollback brings nothing back, and applying again finds no test target to delete.
            Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 12, CancellationToken.None));
            (await Scalar(connection, "SELECT count(*) FROM targets WHERE environment = 'test'")).Should().Be(0L);
            (await Scalar(connection, "SELECT count(*) FROM targets")).Should().Be(2L);
        }
    }

    [Fact]
    public async Task ProductPinsOnly_ShouldDropWorkspacePinsAndTurnServicePinsIntoTheirProducts_WhenApplied()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 14, CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();

        // max: a workspace, then pilot's api and web, then shop, then shop's api; other: pilot's web alone.
        await Execute(connection, InsertProduct("00000000-0000-0000-0000-000000000001", "pilot"));
        await Execute(connection, InsertProduct("00000000-0000-0000-0000-000000000002", "shop"));
        const string pin =
            "INSERT INTO pins (id, owner, kind, workspace_id, product_id, service, sort_order, created_at_utc) " +
            "VALUES (gen_random_uuid(), '{0}', '{1}', {2}, {3}, {4}, {5}, now() + interval '{5} seconds')";
        const string workspace = "(SELECT id FROM workspaces WHERE slug = 'own-ventures')";
        const string pilot = "'00000000-0000-0000-0000-000000000001'";
        const string shop = "'00000000-0000-0000-0000-000000000002'";
        await Execute(connection, string.Format(pin, "max", "workspace", workspace, "NULL", "NULL", 0));
        await Execute(connection, string.Format(pin, "max", "service", "NULL", pilot, "'api'", 1));
        await Execute(connection, string.Format(pin, "max", "service", "NULL", pilot, "'web'", 2));
        await Execute(connection, string.Format(pin, "max", "product", "NULL", shop, "NULL", 3));
        await Execute(connection, string.Format(pin, "max", "service", "NULL", shop, "'api'", 4));
        await Execute(connection, string.Format(pin, "other", "service", "NULL", pilot, "'web'", 0));

        const string pins =
            "SELECT string_agg(owner || ':' || kind || ':' || p.slug || ':' || sort_order, ',' ORDER BY owner, sort_order) " +
            "FROM pins JOIN products p ON p.id = pins.product_id";
        for (var run = 0; run < 2; run++)
        {
            Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Contain("015-product-pins-only");

            // pilot takes its first service pin's place; shop keeps its own pin; the order closes up.
            (await Scalar(connection, pins)).Should().Be("max:product:pilot:0,max:product:shop:1,other:product:pilot:0");
            (await Scalar(connection, "SELECT count(*) FROM pins WHERE kind <> 'product'")).Should().Be(0L);

            // Forward-only: the rollback brings nothing back, and applying again changes nothing.
            Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 14, CancellationToken.None));
            (await Scalar(connection, pins)).Should().Be("max:product:pilot:0,max:product:shop:1,other:product:pilot:0");
        }
    }

    [Fact]
    public async Task DeployAutomation_ShouldStartWheelhouseWithAutoDeployOff_AndGoWithItsProductAndTarget_WhenApplied()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 15, CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();
        await Execute(connection, InsertProduct("00000000-0000-0000-0000-000000000001", "pilot"));
        // The catalog migration seeded Wheelhouse beside ForeverPin.
        (await Scalar(connection, "SELECT count(*) FROM products WHERE slug = 'wheelhouse'")).Should().Be(1L);
        await Execute(connection,
            "INSERT INTO servers (id, slug, name, provider, host, region, ssh_user, ssh_port, ingress, created_at_utc, updated_at_utc) " +
            "VALUES ('00000000-0000-0000-0000-0000000000a1', 'hel1', 'Helsinki', 'hetzner', 'vps.example.net', 'hel1', " +
            "'deploy', 22, '{}'::jsonb, now(), now())");
        await Execute(connection,
            "INSERT INTO targets (id, slug, product_id, server_id, environment, network, root, settings, smoke_checks, " +
            "sites, created_at_utc, updated_at_utc) VALUES ('00000000-0000-0000-0000-0000000000b1', 'pilot-dev', " +
            "'00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-0000000000a1', 'dev', " +
            "'platform', '/srv/wheelhouse', '[]'::jsonb, '[]'::jsonb, '[]'::jsonb, now(), now())");

        for (var cycle = 0; cycle < 2; cycle++)
        {
            Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal(From("016-deploy-automation"));
            // Only Wheelhouse, which its own CI deploys, starts with a row; every other product takes the defaults.
            (await Scalar(connection, "SELECT string_agg(p.slug || ':' || a.auto_deploy_dev || ':' || a.auto_promote, ',') " +
                                      "FROM product_automation a JOIN products p ON p.id = a.product_id"))
                .Should().Be("wheelhouse:false:false");
            await Execute(connection, "INSERT INTO release_watermarks (product_id, release, published_at, checked_at_utc) " +
                                      "VALUES ('00000000-0000-0000-0000-000000000001', 'v1.0.0', now(), now())");
            await Execute(connection, "INSERT INTO deployment_queue (target_id, bundle_id, release, published_at, cause, queued_at_utc) " +
                                      "VALUES ('00000000-0000-0000-0000-0000000000b1', 'pilot-gh-1', 'v1.0.0', now(), 'auto_dev', now())");
            var second = async () => await Execute(connection,
                "INSERT INTO deployment_queue (target_id, bundle_id, release, cause, queued_at_utc) " +
                "VALUES ('00000000-0000-0000-0000-0000000000b1', 'pilot-gh-2', 'v1.1.0', 'auto_dev', now())");
            await second.Should().ThrowAsync<Npgsql.PostgresException>("a target has one queue slot");

            Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 15, CancellationToken.None));
            foreach (var table in new[] { "product_automation", "release_watermarks", "deployment_queue" })
                (await migrator.HasTableAsync(table)).Should().BeFalse();
        }

        // The rows go with their product and target.
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        await Execute(connection, "INSERT INTO deployment_queue (target_id, bundle_id, release, cause, queued_at_utc) " +
                                  "VALUES ('00000000-0000-0000-0000-0000000000b1', 'pilot-gh-1', 'v1.0.0', 'auto_dev', now())");
        await Execute(connection, "DELETE FROM targets");
        (await Scalar(connection, "SELECT count(*) FROM deployment_queue")).Should().Be(0L);
        await Execute(connection, "DELETE FROM products WHERE slug = 'wheelhouse'");
        (await Scalar(connection, "SELECT count(*) FROM product_automation")).Should().Be(0L);
    }

    [Fact]
    public async Task DeployFreezes_ShouldHoldEachScopeOnce_NeedAProductForAnEnvironment_AndGoWithTheProduct_WhenApplied()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();
        await Execute(connection, InsertProduct("00000000-0000-0000-0000-000000000001", "pilot"));
        const string freeze = "INSERT INTO deploy_freezes (id, product_id, environment, reason, actor, created_at_utc) " +
                              "VALUES (gen_random_uuid(), {0}, {1}, NULL, 'max', now())";
        const string pilot = "'00000000-0000-0000-0000-000000000001'";
        await Execute(connection, string.Format(freeze, "NULL", "NULL"));
        await Execute(connection, string.Format(freeze, pilot, "NULL"));
        await Execute(connection, string.Format(freeze, pilot, "'prod'"));
        foreach (var refused in new[]
        {
            string.Format(freeze, "NULL", "NULL"), string.Format(freeze, pilot, "'prod'"), string.Format(freeze, "NULL", "'dev'"),
        })
        {
            var insert = async () => await Execute(connection, refused);
            await insert.Should().ThrowAsync<Npgsql.PostgresException>(refused);
        }

        await Execute(connection, "DELETE FROM products WHERE slug = 'pilot'");
        (await Scalar(connection, "SELECT count(*) FROM deploy_freezes")).Should().Be(1L, "the portfolio freeze stays");

        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 16, CancellationToken.None));
        (await migrator.HasTableAsync("deploy_freezes")).Should().BeFalse();
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal(From("017-deploy-freezes"));
    }

    [Fact]
    public async Task AutoPromotions_ShouldHoldOneMarkPerTarget_AndGoWithIt_WhenApplied()
    {
        await fixture.ResetAsync();
        await using var migrator = CreateMigrator(o => o.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));
        await using var connection = await migrator.OpenConnectionAsync();
        await Execute(connection, InsertProduct("00000000-0000-0000-0000-000000000001", "pilot"));
        await Execute(connection,
            "INSERT INTO servers (id, slug, name, provider, host, region, ssh_user, ssh_port, ingress, created_at_utc, updated_at_utc) " +
            "VALUES ('00000000-0000-0000-0000-0000000000a1', 'hel1', 'Helsinki', 'hetzner', 'vps.example.net', 'hel1', " +
            "'deploy', 22, '{}'::jsonb, now(), now())");
        await Execute(connection,
            "INSERT INTO targets (id, slug, product_id, server_id, environment, network, root, settings, smoke_checks, " +
            "sites, created_at_utc, updated_at_utc) VALUES ('00000000-0000-0000-0000-0000000000b1', 'pilot-prod', " +
            "'00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-0000000000a1', 'prod', " +
            "'platform', '/srv/wheelhouse', '[]'::jsonb, '[]'::jsonb, '[]'::jsonb, now(), now())");
        const string mark = "INSERT INTO auto_promotions (target_id, release, attempted_at_utc) " +
                            "VALUES ('00000000-0000-0000-0000-0000000000b1', '{0}', now())";
        await Execute(connection, string.Format(mark, "v1.0.0"));
        var second = async () => await Execute(connection, string.Format(mark, "v1.1.0"));
        await second.Should().ThrowAsync<Npgsql.PostgresException>("a prod target holds one mark");
        await Execute(connection, "DELETE FROM targets");
        (await Scalar(connection, "SELECT count(*) FROM auto_promotions")).Should().Be(0L);

        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 17, CancellationToken.None));
        (await migrator.HasTableAsync("auto_promotions")).Should().BeFalse();
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal(From("018-auto-promotions"));
    }

    /// <summary>The SQL that stores one vitals reading of a target slug, or of its server's host for <c>NULL</c>.</summary>
    private static string ReadingFor(string target) =>
        "INSERT INTO vitals_samples (id, target_id, server_id, sampled_at_utc, readable, load_percent, memory_percent, " +
        "disk_percent, containers, healthy_containers, restarts) " +
        $"VALUES (gen_random_uuid(), {target}, 'hel1', now(), true, 1, 2, 3, 1, 1, 0)";

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
