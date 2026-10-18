# File References

> **Lookup table only.** Do NOT read files proactively — only when the current task requires them.
> **Tracks docs outside `engineering/codebase/`.** Source files (`.cs`/`.ts`/`.vue`) are navigated via
> `tree`/`find`/`grep`, never listed here.

| Question about | Read |
|---|---|
| What it is, positioning | `product/product.md` |
| Identity Wheelhouse reads: name, description, homepage, service names | `product/product.json` |
| Current state, decisions, essential milestone | `product/context.md` |
| Flows | `product/flows/flows.md` |
| Brand assets, logo usage and sizes, the `icons/` set the console resolves | `product/brand/brand.md` |
| Features, deferred work, open decisions | `engineering/planning/backlog.md` |
| Per-version progress (newest folder = active version) | `engineering/planning/version-track/v{X.Y}/v{X.Y}.md` |
| Runtime, release contract, execution, artifacts, fleet, catalog, integrations and agents, workspace | `engineering/architecture/architecture.md` |
| Backend dev guidelines | `engineering/development/backend-guidelines.md` |
| Frontend dev guidelines | `engineering/development/frontend-guidelines.md` |
| Operational rules | `engineering/development/rules.md` |
| Deploy / ops, local rig, VPS wiring checklist | `engineering/deployment/deployment.md` |
| Host network: tailnet policy and tags, Docker networks, console isolation | `engineering/deployment/vps/network.md` |
| Next versions, close-out items | `engineering/research/next-versions.md` |
| Feature completeness, decision Points, sweep | `engineering/research/feature-completeness.md` |
| Deployment topology, environments, sites, builds | `engineering/research/deployment-topology.md` |
| Parked UI directions (Claude boards) | `engineering/research/design-directions/design-directions.md` |
| MCP security and reliability review, MCP vs REST | `engineering/research/mcp-review.md` |
| v0.5 platform decisions: console shell, hierarchy, pins, one manifest, who owns which fact | `engineering/research/platform-ideation.md` |
| Brand, app logo and icon-set export scripts | `engineering/scripts/export-brand-logos.py` · `export-app-icons.py` · `export-icon-set.py` |

## Source projects (`engineering/codebase/`)

> Individual files NOT listed — use `tree`/`find`/`grep`. Projects only.

### `codebase/wheelhouse.backend-services/` (.NET Clean Arch — `Wheelhouse.BackendServices.slnx`, folders `Services/` + `Tests/`)
| Project | What it is |
|---|---|
| `Wheelhouse.Api` | HTTP host — control-plane controllers; single-host SPA serving |
| `Wheelhouse.Application` | Use cases — mediator handlers, repository abstractions, DTOs |
| `Wheelhouse.Domain` | Entities (products, servers, targets, vaults, integration keys, audit, operations, legacy Deployment/ManagedDomain/SecretEntry) + enums |
| `Wheelhouse.Infrastructure` | Adapters — runner process gateway, inventory snapshot exporter and rig seed, vault admin client, GitHub repository reader (icons, `product.json`), settings |
| `Wheelhouse.Persistence` | EF Core + Postgres context, repositories, hand-authored SQL migrations |
| `Wheelhouse.Tests.Unit` | **Unit** tier — pure logic (validators, icon paths, runner and vault adapters); Docker-free |
| `Wheelhouse.Tests.Integration` | **Integration** tier — EF model below the pipeline over the SDK `RelationalTestDb`, no HTTP; PG↔SQLite |
| `Wheelhouse.Tests.E2E` | **E2E** tier — full host + Testcontainers PG (on `…Beta.Testing`) |
| `Wheelhouse.Tests.Migrations` | **Migrations** tier — bespoke SQL migrator apply/idempotency/rollback over real PG, on the SDK `MigratorHarness` |

### `codebase/wheelhouse.frontend-services/` (pnpm workspace)
| App | What it is |
|---|---|
| `apps/web` (`@wheelhouse/web`) | Control-plane console — home, workspaces, product pages, deployments, servers, secrets, activity, catalog, integration keys |

### `codebase/wheelhouse.runner-services/` (Python)
| File | What it is |
|---|---|
| `inventory.py` | Reads the API-exported `inventory.json` into the catalog and fleet |
| `catalog.py` | Product shape and validation; the local rig's fixture products |
| `fleet.py` | Server, target and vault shapes; the local rig's fixtures |
| `artifacts.py` | Approved release sources and catalog |
| `transport.py` | Runner CLI + SSH adapter used by the API |
| `history.py` | Deployment history projection the API filters: bundle facts, live steps, one observation |
| `runner.py` | Target-side executor: locks, health gates, recovery |
