using AwesomeAssertions;
using Wheelhouse.Persistence;
using Wheelhouse.Tests.Migrations.Harness;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Lint;

namespace Wheelhouse.Tests.Migrations.Tests;

/// <summary>
/// The rules every Wheelhouse migration keeps, on the be sdk's own lint and idempotency proof (spec M22, M59). Decision 65
/// and M20: a platform app only expands, so the previous release's image always runs on the schema a newer one left and
/// the rollback gate's <c>migrate check</c> lets any return through; a contract belongs to the second release of a
/// change and says so in its header (<c>-- @contract since=vX.Y</c>, or the older <c>-- contract:</c> line). Files every
/// live database has already applied cannot change, so each rule starts above the files written before it.
/// </summary>
[Collection(MigratorCollection.Name)]
public sealed class MigrationRulesTests(MigratorPostgresFixture fixture) : MigratorTestBase(fixture)
{
    /// <summary>The last migration written before the expand-only rule (decision 65).</summary>
    private const int ExpandOnlyFrom = 30;

    /// <summary>The last migration every live console applied before the lint's lock-risk and published-table rules:
    /// 010 adds a foreign key and an index to the existing products table, and 030 keys into the set's own
    /// <c>vault</c> schema.</summary>
    private const int LintFrom = 34;

    /// <summary>The last migration written before the idempotency rule: 001-011 rerun badly (a plain
    /// <c>CREATE TABLE</c>, columns read before they exist); every later file reruns cleanly.</summary>
    private const int IdempotentFrom = 11;

    [Fact]
    public async Task Scripts_ShouldPassTheLint()
    {
        await using var migrator = MigratorHarness.CreatePostgres(Fixture.ConnectionString, typeof(WheelhouseDbContext).Assembly);

        await MigrationLint.AssertCleanAsync(migrator.Services, WheelhouseRules);
    }

    [Fact]
    public async Task Scripts_ShouldBeIdempotent_WithHistoryBypassed()
    {
        await using var migrator = MigratorHarness.CreatePostgres(Fixture.ConnectionString, typeof(WheelhouseDbContext).Assembly);

        // Apply → Apply, Rollback → Rollback and Apply → Rollback → Apply, each script run directly.
        var report = await migrator.CheckIdempotencyAsync(after: IdempotentFrom);

        report.Passed.Should().BeTrue(report.ToString());
        report.Proven.Should().HaveCount(EmbeddedAbove(IdempotentFrom), "every file written since the rule is proven");
    }

    [Theory]
    [InlineData("ALTER TABLE products DROP COLUMN notes;", MigrationLintRules.ExpandOnly)]
    [InlineData("ALTER TABLE products RENAME COLUMN notes TO remarks;", MigrationLintRules.ExpandOnly)]
    [InlineData("ALTER TABLE products ALTER COLUMN notes SET NOT NULL;", MigrationLintRules.ExpandOnly)]
    [InlineData("CREATE INDEX IF NOT EXISTS ix_products_notes ON products (notes);", MigrationLintRules.IndexConcurrently)]
    [InlineData("-- @contract since=v0.8\nALTER TABLE products DROP COLUMN notes;", null)]
    [InlineData("ALTER TABLE products ADD COLUMN IF NOT EXISTS remarks text NULL;", null)]
    public async Task TheRules_ShouldHoldANewMigrationToExpandOnly(string apply, string? rule)
    {
        Workspace.Write("001-baseline", "CREATE TABLE IF NOT EXISTS products (id uuid PRIMARY KEY, notes text NULL);",
            "DROP TABLE IF EXISTS products;");
        Workspace.Write("035-change", apply, "SELECT 1;");
        await using var migrator = CreateMigrator();

        var findings = await MigrationLint.CheckAsync(migrator.Services, WheelhouseRules);

        findings.Select(finding => finding.Rule).Should().Equal(rule is null ? [] : [rule]);
    }

    /// <summary>Wheelhouse's lint over its own (default) set.</summary>
    private static void WheelhouseRules(MigrationLintOptions lint) => lint
        .ExpandOnlyAfter(MigrationConstants.DefaultSetName, ExpandOnlyFrom)
        .LintAfter(MigrationConstants.DefaultSetName, LintFrom);

    private static int EmbeddedAbove(int ordinal) => typeof(WheelhouseDbContext).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith("Migrations/", StringComparison.Ordinal) && name.EndsWith("/Apply.sql", StringComparison.Ordinal))
        .Count(name => int.Parse(name.Split('/')[1][..3], System.Globalization.CultureInfo.InvariantCulture) > ordinal);
}
