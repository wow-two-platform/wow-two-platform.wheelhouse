using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace Wheelhouse.Tests.Migrations.Harness;

/// <summary>
/// xUnit collection over the SDK <see cref="MigratorPostgresFixture"/> — the bespoke-migrator suite's shared container.
/// Its <c>ResetAsync</c> drops and recreates the <c>public</c> schema, so every migrator test applies from scratch
/// (apply / idempotency / rollback). The fixture's own <see cref="MigratorPostgresFixture.Name"/> is the collection name.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MigratorCollection : ICollectionFixture<MigratorPostgresFixture>
{
    /// <summary>The collection name every migrator test class joins (matches <see cref="MigratorPostgresFixture.Name"/>).</summary>
    public const string Name = "postgres-migrator";
}
