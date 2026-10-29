using System.Globalization;
using AwesomeAssertions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Wheelhouse.Api.Configurations;
using Wheelhouse.Persistence;
using Wheelhouse.Tests.E2E.Harness;
using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Verbs;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the console image's <c>migrate</c> verbs (spec M57), the command the rollout's migrate step runs as
/// <c>dotnet Wheelhouse.Api.dll migrate</c>: the host built exactly as <c>Program.cs</c> builds it before it answers a
/// verb, as a deployed console (<c>DEPLOY_ENVIRONMENT</c> set), over a fresh database on the run's PostgreSQL. A
/// deployed boot applies nothing: it only checks the level the step left, so the step is the one apply path.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class MigrateVerbE2ETests(WheelhouseAppFixture fixture) : IAsyncLifetime
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("wheelhouse-migrate-verbs-");
    private readonly string _database = "verbs_" + Guid.NewGuid().ToString("N")[..12];
    private WebApplication? _image;

    /// <summary>The highest ordinal the console image embeds: the level the step brings a database to.</summary>
    private static int Level => typeof(WheelhouseDbContext).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith("Migrations/", StringComparison.Ordinal) && name.EndsWith("/Apply.sql", StringComparison.Ordinal))
        .Max(name => int.Parse(name.Split('/')[1][..3], CultureInfo.InvariantCulture));

    private string ConnectionString => new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = _database }.ConnectionString;

    private WebApplication Image => _image ??= Build(ConnectionString);

    [Fact]
    public async Task Migrate_ShouldBringAnEmptyDatabaseToTheImagesLevel_AndARerunChangesNothing()
    {
        var verbs = new MigrateVerbHarness(Image.Services);

        (await verbs.AssertCheckWritesNothingAsync()).Exit.Should().Be(1, "an empty database is behind the image");
        var first = await verbs.AssertExitAsync(0);
        var set = first.Result.Sets.Should().ContainSingle().Which;
        (set.Name, set.LevelAfter).Should().Be(("default", Level));
        set.Applied.Should().HaveCount(EmbeddedCount(), "every embedded migration applies once, in order");

        await verbs.AssertRerunChangesNothingAsync();
        (await verbs.AssertCheckWritesNothingAsync()).Exit.Should().Be(0, "the image may run on the database the step left");
        await Image.Services.MigrateBespokeOnStartupAsync();
    }

    [Fact]
    public async Task DeployedBoot_ShouldRefuseADatabaseTheStepNeverMigrated_AndCreateNothing()
    {
        var boot = () => Image.Services.MigrateBespokeOnStartupAsync().AsTask();

        await boot.Should().ThrowAsync<InvalidOperationException>();
        (await TablesAsync()).Should().BeEmpty("a deployed console never applies at boot; the rollout's migrate step does");
    }

    [Fact]
    public async Task DatabaseAnOlderReleaseMigrated_ShouldBootBeforeTheStep_AndTheStepRecordsItsKinds()
    {
        // The live consoles' databases were migrated at boot by be sdk 10.0.80: history without kinds, an integer
        // execution time and no events table. Release v0.7.7's runner (and older) deploys without a migrate step.
        var verbs = new MigrateVerbHarness(Image.Services);
        await verbs.AssertExitAsync(0);
        await ExecuteAsync("""
            DO $$ DECLARE t text; BEGIN
              FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename LIKE 'migration_history_%' LOOP
                EXECUTE format('DROP TABLE %I', t);
              END LOOP;
            END $$;
            ALTER TABLE migration_history DROP COLUMN kind, DROP COLUMN contract_since, ALTER COLUMN execution_ms TYPE integer;
            """);

        // A release with no new migration boots on it whichever runner deployed it.
        await Image.Services.MigrateBespokeOnStartupAsync();
        (await verbs.AssertCheckWritesNothingAsync()).Exit.Should().Be(0);

        // The step applies nothing and brings the history to the current shape, every row with its kind.
        var step = await verbs.AssertExitAsync(0);
        step.Result.Sets.Should().ContainSingle().Which.Applied.Should().BeEmpty();
        (await ScalarAsync<string>(
                "SELECT format_type(atttypid, atttypmod) FROM pg_attribute WHERE attrelid = 'migration_history'::regclass AND attname = 'execution_ms'"))
            .Should().Be("bigint");
        (await ScalarAsync<long>("SELECT count(*) FROM migration_history WHERE kind IS NULL")).Should().Be(0);
        await verbs.AssertRerunChangesNothingAsync();
    }

    [Fact]
    public async Task Sets_ShouldDescribeTheImage_WithoutReachingADatabase()
    {
        await using var image = Build("Host=127.0.0.1;Port=9;Database=none;Username=none;Password=none;Timeout=2");

        var run = await new MigrateVerbHarness(image.Services).AssertExitAsync(0, "sets", "--json");

        run.Result.Sets.Should().ContainSingle().Which.LevelAfter.Should().Be(Level);
        run.Output.Should().Contain("\"name\":\"default\"").And.Contain("\"strategy\":\"bespoke\"");
    }

    [Fact]
    public async Task OperatorVerbs_ShouldRefuseWithoutTheDatabasesNameTyped()
    {
        var verbs = new MigrateVerbHarness(Image.Services);
        await verbs.AssertExitAsync(0);

        await verbs.AssertExitAsync(1, "forget", "--above", "1");
        await verbs.AssertExitAsync(1, "rollback", "--to", "1", "--confirm", "wheelhouse");
        (await verbs.AssertCheckWritesNothingAsync()).Exit.Should().Be(0, "a refused operator verb changes nothing");
    }

    /// <inheritdoc />
    public async Task InitializeAsync() => await ExecuteAsync($"CREATE DATABASE \"{_database}\"", fixture.ConnectionString);

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_image is not null)
            await _image.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", fixture.ConnectionString);
        _root.Delete(recursive: true);
    }

    /// <summary>The console's services as <c>Program.cs</c> builds them for a verb: no arguments as configuration, no
    /// pipeline, no start; a deployed dev console's environment over <paramref name="connectionString"/>, with none of
    /// the test process's variables.</summary>
    private WebApplication Build(string connectionString)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = _root.FullName,
            EnvironmentName = Environments.Development,
        });
        foreach (var source in builder.Configuration.Sources.OfType<EnvironmentVariablesConfigurationSource>().ToArray())
            builder.Configuration.Sources.Remove(source);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Wheelhouse"] = connectionString,
            ["DB_CONNECTION"] = connectionString,
            [LocalRunExtensions.DeployEnvironmentVariable] = "dev",
        });
        builder.Configure();
        return builder.Build();
    }

    private static int EmbeddedCount() => typeof(WheelhouseDbContext).Assembly.GetManifestResourceNames()
        .Count(name => name.StartsWith("Migrations/", StringComparison.Ordinal) && name.EndsWith("/Apply.sql", StringComparison.Ordinal));

    private async Task<IReadOnlyList<string>> TablesAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        return (await connection.QueryAsync<string>(
            "SELECT schemaname || '.' || tablename FROM pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema')")).ToList();
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql) ?? throw new InvalidOperationException("No value: " + sql);
    }

    private async Task ExecuteAsync(string sql, string? connectionString = null)
    {
        await using var connection = new NpgsqlConnection(connectionString ?? ConnectionString);
        await connection.ExecuteAsync(sql);
    }
}
