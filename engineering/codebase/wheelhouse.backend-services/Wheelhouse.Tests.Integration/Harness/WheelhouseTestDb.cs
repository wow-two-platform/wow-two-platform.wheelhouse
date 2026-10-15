using Wheelhouse.Persistence;
using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Backend.Beta.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Backend.Beta.Testing.Data.EntityFrameworkCore;

namespace Wheelhouse.Tests.Integration.Harness;

/// <summary>
/// The Wheelhouse EF test database — a provider-switchable <see cref="RelationalTestDb{TContext}"/> over
/// <see cref="WheelhouseDbContext"/>. Schema comes from EF (<c>EnsureCreated</c> off <c>OnModelCreating</c>), not the bespoke
/// migrator, so the store / constraint / enum suites exercise the real EF model and run on Postgres or SQLite uniformly.
/// </summary>
/// <remarks>
/// Postgres by default, the fidelity baseline; <c>WHEELHOUSE_TEST_DB=sqlite</c> flips the whole suite to in-memory SQLite.
/// The migrator suite stays on Postgres regardless, since it asserts the real SQL migrations. <see cref="RelationalTestDb{TContext}.ResetAsync"/> clears data between tests (Postgres: Respawn truncate;
/// SQLite: recreate). <see cref="ApplyConventions"/> applies Wheelhouse's snake_case naming so EF maps onto the created schema.
/// </remarks>
public sealed class WheelhouseTestDb : RelationalTestDb<WheelhouseDbContext>
{
    /// <summary>Opens the test database on the provider <c>WHEELHOUSE_TEST_DB</c> names.</summary>
    public WheelhouseTestDb() : base(ProviderFromEnvironment()) { }

    /// <summary>Builds a <see cref="WheelhouseDbContext"/> over the active test provider; the SQLite-only
    /// <c>DateTimeOffset</c> conversion is applied inside <c>OnModelCreating</c>.</summary>
    protected override WheelhouseDbContext CreateContext(DbContextOptionsBuilder<WheelhouseDbContext> builder) =>
        new(builder.Options);

    /// <summary>Applies Wheelhouse's snake_case naming (the host convention) to every path that builds a context, so each
    /// maps onto the schema <c>EnsureCreated</c> made.</summary>
    protected override void ApplyConventions(DbContextOptionsBuilder builder) =>
        builder.UseSnakeCaseNamingConvention();

    private static DatabaseProvider ProviderFromEnvironment() =>
        string.Equals(Environment.GetEnvironmentVariable("WHEELHOUSE_TEST_DB"), "sqlite", StringComparison.OrdinalIgnoreCase)
            ? DatabaseProvider.Sqlite
            : DatabaseProvider.Postgres;
}

/// <summary>xUnit collection sharing one <see cref="WheelhouseTestDb"/> across the store / constraint / enum suites.</summary>
[CollectionDefinition(Name)]
public sealed class WheelhouseTestDbCollection : ICollectionFixture<WheelhouseTestDb>
{
    /// <summary>The collection name every EF-model test class joins.</summary>
    public const string Name = "wheelhouse-test-db";
}
