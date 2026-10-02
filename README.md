<picture>
  <source media="(prefers-color-scheme: dark)" srcset="product/brand/soft-folds/wheelhouse-wordmark-white.png">
  <img src="product/brand/soft-folds/wheelhouse-wordmark.png" alt="Wheelhouse" height="48">
</picture>

# wow-two-platform.wheelhouse

**Wheelhouse** — the product ops & deploy control plane for the micro-SaaS portfolio. The essential slice deploys published
product artifacts to VPSs defined in code. Domain, cost, backup and fleet governance follow that pilot.
Named DryDock until 2026-09-26 — a ship moving containers, where Docker is the whale that carries them.

> Internal tooling — **never expose publicly**. Bind to loopback and reach it over Tailscale / an SSH tunnel.

## Layout

```
product/                          ← the definition (what · why · flows · brand) — no code
└── product.md · context.md · flows/ · brand/
engineering/                      ← the execution (build · ship · run)
├── engineering.md · architecture/ · development/ · deployment/ · planning/ · research/ · scripts/
└── codebase/
    ├── wheelhouse.backend-services/         ← .NET 10 API (Clean Architecture + SDK mediator + EF Core/PostgreSQL)
    │   ├── Wheelhouse.Domain        ← entities and enums
    │   ├── Wheelhouse.Application   ← commands and queries, repository abstractions
    │   ├── Wheelhouse.Infrastructure← integration clients and bounded deployment gateway
    │   ├── Wheelhouse.Persistence   ← EF Core PostgreSQL context, repositories, SQL migrations
    │   └── Wheelhouse.Api           ← slim host, controllers, serves the SPA from wwwroot
    ├── wheelhouse.frontend-services/        ← pnpm workspace; apps/web = Vue 3 + Vite + Tailwind v4 + @wow-two-beta/ui-vue
    └── wheelhouse.runner-services/          ← Python runner: fleet, release catalog, SSH transport, target executor
```

Follows `wow-two-ws/conventions/development/repo/structure/repo-structure.md`.

## Run it (dev)

**Backend** (API on `https://localhost:8210` / `http://localhost:8211`):
```bash
cd engineering/codebase/wheelhouse.backend-services
dotnet run --project Wheelhouse.Api --launch-profile https
```

**Frontend** (Node 24, pnpm; Vite on `https://localhost:5174`, proxies `/api` → `https://localhost:8210`):
```bash
cd engineering/codebase/wheelhouse.frontend-services
pnpm install
pnpm dev
```

Open https://localhost:5174 — the workspace hits the API through the HTTPS dev proxy.
`pnpm test` verifies inventory/selection, API contracts and sensitive-form lifecycles;
`pnpm build` checks Vue types, compiles every SFC and builds lazy route bundles.

## Single-host build (API serves the SPA)

```bash
cd engineering/codebase/wheelhouse.frontend-services && pnpm deploy   # build the app → copy into Api/wwwroot
cd ../wheelhouse.backend-services && dotnet run --project Wheelhouse.Api
```

Container startup requires an explicit database password and owner login; see
[deployment operations](engineering/deployment/deployment.md#local-packaging).

## Stack

- **Backend:** .NET 10, Clean Architecture, `WoW2.Sdk.Backend.Beta` (mediator, results, identity), EF Core 10 + PostgreSQL.
- **Frontend:** Vue 3, Vue Router, Vite 6, Tailwind v4, `@wow-two-beta/ui-vue` component library.
- **Runtime (target):** Docker + Traefik per Hetzner VPS; images from GHCR.

The original design spec lives in the workspace at `wow-two-ws/ideas/wheelhouse-spec.md`.
Unbuilt work: [backlog](engineering/planning/backlog.md). Design: [architecture](engineering/architecture/architecture.md).
Products, servers, environments and vaults are edited in the console; credentials stay files on the control host.
