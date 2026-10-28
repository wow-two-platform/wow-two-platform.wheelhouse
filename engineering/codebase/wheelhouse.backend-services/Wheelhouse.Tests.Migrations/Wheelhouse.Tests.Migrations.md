# Wheelhouse.Tests.Migrations

*Last updated: 2026-10-28*

> The bespoke SQL migrator over real PostgreSQL: apply, history and rollback of every embedded migration, and the rules
> each one keeps.

- Prerequisites: Docker for PostgreSQL.
- Run: `dotnet test Wheelhouse.Tests.Migrations`.
- Harness: the SDK `MigratorHarness` and `MigratorPostgresFixture` over `Wheelhouse.Persistence/Migrations/NNN/*.sql`.
- `MigrationRulesTests`: the be sdk's `MigrationLint` (expand-only above `030`; lock-risk and published-table rules above
  `034`) and its idempotency proof with history bypassed (files above `011`). A file at or below a cutoff is applied
  on every live console and stays as it is.
- The console image's `migrate` verbs are proven in the E2E tier (`MigrateVerbE2ETests`).
