# Wheelhouse — Backend Development Guidelines

*Last updated: 2026-10-26*

> Defer to the shared conventions in `wow-two-ws/conventions/development/backend/` — do not restate them. This file
> holds only Wheelhouse deltas.

## Repo-specific deltas

- **SDK:** every layer runs on the be sdk (`WoW2.Sdk.Beta.Be`) — host floor, mediator, `AppResult` / `AppError`, validation,
  identity, the GitHub and GHCR clients, the bespoke SQL migrator and the test harness. New infrastructure proves inline,
  then extracts to the SDK ([backlog](../planning/backlog.md) § SDK adoption).
- **Controllers** send a request through `ISender` and render the result with the SDK
  `this.ToActionResult(result)`. No logic in the host.
- **Audited commands** implement `IAuditedCommand` and land in the hash-chained `audit_entries` trail; never put a value
  in one.
- **Database:** PostgreSQL 18; hand-authored `Wheelhouse.Persistence/Migrations/{NNN-name}/{Apply,Rollback}.sql`,
  applied by the rollout's migrate step (`dotnet Wheelhouse.Api.dll migrate`) and at boot only on a local run. Every
  file only expands and reruns cleanly (`MigrationRulesTests`: the be sdk lint and idempotency proof). EF Core maps
  only; no EF migrations or `EnsureCreated`.
- **Transactions:** writes that commit together run in a unit of the SDK `IDataSession` (`BeginAsync` →
  `CompleteAsync`, which saves and commits); work that must follow a commit, such as the inventory export, registers
  with `OnCommittedAsync`. One transaction per request scope.
- **Outbound work** — the runner process, the vault admin API, GitHub — lives in `Infrastructure` behind Application
  ports, never in controllers or the Domain.
- **Runner process:** the gateways run `transport.py` and `infra.py` through the SDK process client keyed `runner`
  (`RunnerProcess`): 110 seconds, the JSON failure line, and an environment allowlist. A variable the runner starts
  reading is set by the gateway per call or added to `RunnerProcess.InheritedVariables`; nothing else passes.
- **Background passes** are SDK periodic jobs, registered in `PeriodicJobsServiceCollectionExtensions` with their
  interval key (0 turns one off); no hand-written `BackgroundService` loops.
- **Files the runner reads** (`inventory.json`, `github/tokens.json`) are written with the SDK `AtomicFileExtensions`,
  owner-only; a cache row that can lose an insert race saves with the SDK `UpsertAsync`.
- **Ports:** HTTPS 8210 / HTTP 8211.
