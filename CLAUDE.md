# wow-two-platform.wheelhouse

## What is this

**Wheelhouse** — the internal product ops & deploy control plane for the micro-SaaS portfolio. Deploys
product release bundles to the VPS targets its inventory defines, over SSH (Docker + ingress). Domain governance,
secret rotation and fleet monitoring remain planned capabilities. Single-user; **never exposed publicly**.
Named DryDock until 2026-09-26; history before then says DryDock.

> This is a **platform service** in the `wow-two-platform` org. It manages the *other* products —
> it sits above them operationally.

## Structure

```
product/                  ← the definition (what · why · flows · brand) — no code
└── product.md · context.md · flows/ · brand/
engineering/              ← the execution (build · ship · run)
├── engineering.md · architecture/ · development/ · deployment/ · planning/ · research/ · scripts/
│   └── planning/ = backlog.md + version-track/v{X.Y}/ (newest folder = active version)
└── codebase/
    ├── wheelhouse.backend-services/   ← .NET 10 Clean Architecture solution (Wheelhouse.BackendServices.slnx)
    ├── wheelhouse.frontend-services/  ← pnpm workspace; apps/web = Vue 3 + Vite + Tailwind v4 + the fe sdk @wow-two-beta/fe
    │                                     (Home · Products · Deployments · Servers; /p/:product/:tab; profile menu)
    └── wheelhouse.runner-services/    ← Python runner: fleet, catalog, SSH transport, target executor,
                                          infra.py (Tailscale, server enrollment, product placement)
```

Follows `wow-two-ws/conventions/development/repo/structure/repo-structure.md`.

Backend layers: `Domain` (entities/enums) → `Application` (SDK mediator CQRS + repository abstractions)
→ `Infrastructure` (adapters) + `Persistence` (EF Core + Postgres) → `Api` (slim host). Mirrors the
`wow-two-platform.secrets-vault` sibling exactly.

## Core domains (the 5 things Wheelhouse manages)

Products · Servers · Deployments · Domains · Secrets. The **inventory** — products, servers, targets and vaults — lives
in PostgreSQL and is edited in the console; the API exports it to `<runner root>/inventory.json` for the runner.
Integrations read `/api/products` with scoped keys. Deployments use the independent runner and published artifact
catalog, with history, live rollout steps, site probes, read-only checks, log reads and reconciliation. **Secrets** live
in the built-in Secrets Vault (schema `vault`, per-product keys under a root key file, versioned, write-only); products
get them only from the deploy-time render, and the Settings and Secrets tabs edit them. Domains remain a scaffold model.
Prod (never dev) also enrolls fresh servers with a one-time key, places a product's databases and private sites, and
applies the tailnet policy after a reviewed diff, through the owner's Tailscale OAuth client; each is a runner job with
live steps, audited at start and finish.
Every audited command (`IAuditedCommand`) lands in the hash-chained `audit_entries` trail with its actor (a signed-in
user's login, or `key:{name}` for an integration key); never put a value in one.

## Build & run

```bash
# Backend
cd engineering/codebase/wheelhouse.backend-services && dotnet build Wheelhouse.BackendServices.slnx
dotnet run --project Wheelhouse.Api --launch-profile https   # 8210 https / 8211 http

# DB migrations: hand-authored SQL (bespoke migrator). No EF tooling. A local run applies them at boot; a deployed
# console takes them in the rollout's migrate step (`dotnet Wheelhouse.Api.dll migrate`) and only checks at boot.
# Add one → Wheelhouse.Persistence/Migrations/{NNN-name}/{Apply,Rollback}.sql — expand-only, rerunnable

# Frontend (pnpm workspace; the app lives in apps/web)
cd engineering/codebase/wheelhouse.frontend-services && pnpm install && pnpm dev   # HTTPS 5174, proxies /api → HTTPS 8210; Node 24
pnpm dev:http   # plain HTTP for headless previews (GitHub sign-in needs HTTPS)
pnpm test       # routing, selection, history filter, pins, catalog, protocol and sensitive-form checks
pnpm build      # vue-tsc + SFC compiler gate + route-split production bundle
pnpm deploy     # build + copy apps/web/dist into Wheelhouse.Api/wwwroot

# Local rig for an IDE run (then Wheelhouse.Api, profile https → https://localhost:8210)
cd engineering/deployment/rehearsal && python3 rehearse.py dev
# The whole system locally: production image + rehearsal SSH target + vault (deployment.md → Local console)
cd engineering/deployment/rehearsal && python3 rehearse.py console   # http://localhost:18210
```

## Testing

4-tier `{Product}.Tests.{Type}`, e2e-first (run all: `dotnet test Wheelhouse.BackendServices.slnx`). Solution folders: `Services/` + `Tests/`.
Package versions are central in `Directory.Packages.props`; `global.json` pins the SDK band; `tests.runsettings` runs tests as `Development`.

- **`Wheelhouse.Tests.Unit`** — pure logic (validators, icon paths, runner and vault adapters, hygiene rules). Docker-free.
- **`Wheelhouse.Tests.Integration`** — the EF model below the pipeline: `WheelhouseDbContext` over the SDK
  `RelationalTestDb` (enum round-trip, repository predicates/ordering, unique-index constraints), no HTTP.
  Provider-switchable PG↔SQLite (`WHEELHOUSE_TEST_DB=sqlite`). Docker (PG default).
- **`Wheelhouse.Tests.E2E`** — full host + Testcontainers PG 18 (on `…Beta.Testing`), and the image's `migrate` verbs
  on the SDK verb harness. The primary tier. Docker.
- **`Wheelhouse.Tests.Migrations`** — specialized: the bespoke SQL migrator's apply/idempotency/rollback over a real
  PG, on the SDK `MigratorHarness` + `MigratorPostgresFixture` (embedded `Migrations/NNN/*.sql`), plus the SDK
  `MigrationLint` and idempotency proof (`MigrationRulesTests`). Docker.

Reserve unit for I/O-free logic; everything user-facing is covered e2e. Full rule:
`wow-two-ws/conventions/development/backend/testing/testing.md`.

## Conventions

- **File-per-type**, slim `Program.cs` (delegates to `Api/Configurations/*`), SDK `AppResult` / `AppError`.
  Controllers send requests via `ISender`, render the result with the SDK `this.ToActionResult(result)`.
- **Ports:** HTTPS even (8210) + HTTP odd (8211), per the wow-two launch-profile rule.
- **DB:** Postgres (Npgsql). Schema owned by the **bespoke SQL migrator**
  (`…Beta.Data.Migrations.Bespoke`) over hand-authored `Migrations/{NNN}/{Apply,Rollback}.sql`;
  EF Core is a pure mapper. The rollout's migrate step applies; a deployed boot only checks; Testcontainers-PG under E2E.

## Beta SDK usage (per workspace direction)

- **Frontend → fe sdk `@wow-two-beta/fe` (`0.0.36`, in `apps/web/package.json`).** Use its components (Button, Card,
  Badge, Heading, Text, EmptyState, Alert, Spinner, TextInput, …) before hand-rolling. Tailwind v4 wiring: `index.css`
  imports `tailwindcss` + `@wow-two-beta/fe/styles.css` and `@source`s the package's `dist` so its
  utility classes are generated. Shared capability gaps belong in the SDK. Product composition stays local.
- **Backend → be sdk `WoW2.Sdk.Beta.Be` (adopted, `10.0.95-beta`; identity from `WoW2.Sdk.Beta.Be.Identity.*`
  `10.0.9-beta`, split out of the core at `10.0.82`).** `v0.2` migrated every layer onto
  the SDK: host floor (`AddApiDefaults`/`UseApiDefaults`), mediator + results + validation, identity
  (GitHub OAuth + cookie + allowlist/default-deny + scoped API keys), the bespoke SQL
  migrator, and `…Be.Testing` for the test harness. Products hold business logic only; new infra proves
  inline then extracts to the SDK in the next `+0.1` (see `engineering/planning/backlog.md` § SDK adoption).

## Security

Wheelhouse will hold VPS SSH keys, registrar billing APIs, the Cloudflare token, and a GHCR PAT — the
highest-value secret set in the portfolio. Keep it off the public internet (Tailscale / SSH tunnel),
encrypt secrets at rest, use scoped tokens, and keep an audit trail.

## Out of scope (deliberately, for now)

Auth/multi-tenant/billing (single-user — bind to Tailscale).

## Fleet and artifact policy

Products, servers, targets and vaults are database rows edited in the console; the runner reads the exported
`inventory.json` snapshot. Credentials stay owner-placed files (`ssh/<server>/`, `vault/root.key`, a standalone vault's
`vaults/<vault>/password` until it is imported, prod's `providers/tailscale/oauth-client.json`), never rows. Provider/environment choices use enums; no dynamic provider
plugins. The local rig's fixtures stay in `catalog.py`/`fleet.py` and seed the database on the rig only. A product's release source is part of its row.
Wheelhouse deploys published releases and per-commit builds; it starts a product's build workflow only for a commit
that has no build, and never builds on a target host.
Environments are `dev` and `prod` (`test` retired 2026-10-17): products work on `main` only and fix forward; every
`main` release deploys to dev, then is promoted to prod. Dev takes any build, and prod only a release that succeeded
on dev (a typed target ID skips that). Every product builds through the shared `publish` workflow in
`wow-two-platform.pipelines`: `main` releases `vX.Y.Z`, other branches build candidates.
Artifact, registry and fleet rules live in `engineering/architecture/architecture.md`.
Wheelhouse's own `engineering/deployment/deploy.yml` and `.github/workflows/` build and test it like any product; once
activated, prod deploys each release to its dev and promotes itself, CI only builds, and `vps/self-deploy.py` covers a
dead console (`engineering/deployment/deployment.md` § Wheelhouse deploys itself).
