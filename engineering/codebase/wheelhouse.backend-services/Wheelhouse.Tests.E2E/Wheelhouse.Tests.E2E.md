# Wheelhouse.Tests.E2E

*Last updated: 2026-09-29*

> The primary tier: every user-facing flow through the real host — auth gates, products, deployments, vaults,
> audit and SPA serving.

- Prerequisites: Docker for the Testcontainers PostgreSQL 18; the test host is a local run, so it migrates on boot.
- `MigrateVerbE2ETests`: the console image's `migrate` verbs over a fresh database on the SDK verb harness, as a
  deployed console builds them — exits, reruns, a write-free `check`, `sets --json` without a database, the operator
  guard, and a database an older release migrated.
- Run: `dotnet test Wheelhouse.Tests.E2E`; SPA-serving tests read `Wheelhouse.Api/wwwroot`, which the backend build fills.
- Harness: the SDK `WebApiTestHost` and `PostgresFixture`; `Harness/` keeps the test-admin policy and the runner,
  vault and GitHub stubs.
