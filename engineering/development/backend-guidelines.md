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
- **Database:** PostgreSQL; hand-authored `Wheelhouse.Persistence/Migrations/{NNN-name}/{Apply,Rollback}.sql`, applied
  on boot. EF Core maps only; no EF migrations or `EnsureCreated`.
- **Transactions:** writes that commit together run in a unit of the SDK `IDataSession` (`BeginAsync` →
  `CompleteAsync`, which saves and commits); work that must follow a commit, such as the inventory export, registers
  with `OnCommittedAsync`. One transaction per request scope.
- **Outbound work** — the runner process, the vault admin API, GitHub — lives in `Infrastructure` behind Application
  ports, never in controllers or the Domain.
- **Ports:** HTTPS 8210 / HTTP 8211.
