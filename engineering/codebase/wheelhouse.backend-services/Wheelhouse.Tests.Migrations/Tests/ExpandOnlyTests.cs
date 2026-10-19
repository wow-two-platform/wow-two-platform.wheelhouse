using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Wheelhouse.Persistence;

namespace Wheelhouse.Tests.Migrations.Tests;

/// <summary>
/// Decision 65: Wheelhouse's migrations only expand, so the previous release's images always run on the schema a newer
/// release left, and any release can roll back — the runner rolls a failed self-promotion back on that promise. A
/// contracting statement (a drop, a rename, a narrowed type or a new NOT NULL) belongs only to the second release of a
/// change, once no release that can still run reads what it removes; its migration says so in a
/// <c>-- contract:</c> line. Migrations up to 030 predate the rule. Reads the embedded SQL only; no database.
/// </summary>
public sealed class ExpandOnlyTests
{
    /// <summary>The last migration written before the rule.</summary>
    private const int Before = 30;

    private static readonly Regex Contracting = new(
        @"\bDROP\s+(TABLE|COLUMN|TYPE|CONSTRAINT|INDEX|VIEW|SCHEMA)\b|\bRENAME\b|\bALTER\s+COLUMN\s+\S+\s+(SET\s+DATA\s+)?TYPE\b|\bSET\s+NOT\s+NULL\b|\bTRUNCATE\b|\bDELETE\s+FROM\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static IEnumerable<(int Ordinal, string Name, string Sql)> Applies()
    {
        var assembly = typeof(WheelhouseDbContext).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.EndsWith("/Apply.sql", StringComparison.Ordinal)))
        {
            var folder = resource.Split('/')[1];
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            yield return (int.Parse(folder[..3], System.Globalization.CultureInfo.InvariantCulture), folder, reader.ReadToEnd());
        }
    }

    [Fact]
    public void EveryMigrationAfterTheRule_ShouldOnlyExpand_UnlessItNamesItsContractStep()
    {
        var applies = Applies().ToList();
        applies.Should().NotBeEmpty();
        var offending = applies
            .Where(apply => apply.Ordinal > Before)
            .Where(apply => !apply.Sql.Split('\n').Any(line => line.TrimStart().StartsWith("-- contract:", StringComparison.Ordinal)))
            .Where(apply => Contracting.IsMatch(WithoutComments(apply.Sql)))
            .Select(apply => apply.Name)
            .ToList();
        offending.Should().BeEmpty("a release must be able to roll back over the schema it leaves (decision 65)");
    }

    [Theory]
    [InlineData("ALTER TABLE products DROP COLUMN status;", true)]
    [InlineData("DROP TABLE pins;", true)]
    [InlineData("ALTER TABLE targets RENAME COLUMN root TO folder;", true)]
    [InlineData("ALTER TABLE targets ALTER COLUMN root TYPE varchar(20);", true)]
    [InlineData("ALTER TABLE targets ALTER COLUMN root SET NOT NULL;", true)]
    [InlineData("ALTER TABLE targets ADD COLUMN notes text NULL;", false)]
    [InlineData("CREATE TABLE IF NOT EXISTS vault_keys (id uuid PRIMARY KEY);", false)]
    [InlineData("CREATE INDEX IF NOT EXISTS ix_targets_root ON targets (root);", false)]
    public void TheRule_ShouldTellContractingFromExpanding(string sql, bool contracting) =>
        Contracting.IsMatch(sql).Should().Be(contracting);

    private static string WithoutComments(string sql) =>
        string.Join('\n', sql.Split('\n').Select(line => line.Split("--", 2)[0]));
}
