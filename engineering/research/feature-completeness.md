# Feature completeness — vectors, reliability and shipping Wheelhouse

*Last updated: 2026-10-02*

What Wheelhouse needs before it is complete and reliable enough to run the portfolio: the five vectors the product
named (topology, secrets, domains, portfolio, service map), the reliability properties, how Wheelhouse ships itself,
and a sweep of gaps. This is analysis. Each decision is a box in [Points](#points); a settled point moves into a
version track. Platform-service decisions stay in [deployment topology](deployment-topology.md) points 5–11, 15, 23
and 25.

## Status

- [x] Swept: backend, runner, frontend, docs, backlog, pilot plan and the original spec (`wow-two-ws/ideas/wheelhouse-spec.md`).
- [x] Decision-free items built in v0.3 iterations 9-11: rollout steps, site probes, log reads, ingress check, image cleanup, audit trail, CI.
- [x] Second sweep: runner housekeeping, write guards, test layers and contracts (S19-S25).
- [x] Service map built in v0.3 iteration 12: sites, platform needs, versions, environments compared with promotion.
- [x] Third sweep: version stamps, version docs, SDK pins and dependency advisories (S26-S29); [next versions](next-versions.md).
- [x] Fourth sweep: outcome tracking, alert rules, runner reads, startup order, contracts and pins (S30-S43); every suite passed on 2026-10-01.
- [ ] Points decided.
- [ ] Version tracks written from the decided points.

---

## Current state

| Vector | Built | Missing |
|---|---|---|
| Deployments | `dev`/`test`/`prod` targets, releases and commit builds, prod gate, locks, health gates, rollback, reconcile, history, site routes, live rollout steps, site probes, image cleanup, a release catalog view | Notifications, stop/start, teardown, outcomes recorded without a page open |
| Topology | Traefik ingress on the local server, one `platform` network, per-environment databases created by `rehearse.py` | Host preparation, platform PostgreSQL with a database and role per target, derived settings, per-target networks, backups |
| Secrets | Vault console: namespaces, write-only values, product tokens, rotation hygiene; required-key checks on settings files | Settings rendering, deploy-time tokens, SDK vault consumer, expiring tokens, Wheelhouse's own credentials at rest |
| Domains | Site hosts per target (named or pattern); `ManagedDomain` placeholder entity | Inventory, registrar sync, DNS plan and apply, expiry tracking, preview wildcard |
| Portfolio | Products, environments, servers and vaults edited in the console and exported to the runner; the lifecycle record; `/api/products` for scoped integration keys | Lifecycle actions, cost, capacity, onboarding |
| Service map | Per-target map on a pan-and-zoom canvas: services, networks, volumes, dependencies, container vitals, sites, platform needs, versions; environments compared with promotion | Host view, portfolio matrix |
| Operations | Host and container vitals with 30 days of trends, 30-day deploy metrics, one attention list, service log reads, an ingress check | Alerts, notifications, uptime probes |
| Wheelhouse itself | Production image, CI on every push, its own `deploy.yml` and release workflow, the audit trail, a local self-deploy | A host, bootstrap, backups |

---

## What complete means

- The SDK doctrine applies to Wheelhouse: each vector gets every capability, not only ForeverPin's slice.
- Each capability runs end to end on the local server before a VPS exists.
- Each capability has coverage in the tier it touches: runner tests, backend E2E, frontend tests.

Reliable means seven properties:

| # | Property | Today |
|---|---|---|
| R1 | Every mutation is serialized, health-gated and recoverable without the dashboard | Built |
| R2 | Every operator action leaves an audit record | Built: hash-chained trail, no external checkpoint yet |
| R3 | A deploy shows its steps while it runs and verifies the path users take | Built: rollout steps and site probes |
| R4 | Desired state lives in code; Wheelhouse reports drift and repairs it on request | Release drift only |
| R5 | Failures reach the operator without a page open | Missing |
| R6 | Data survives a lost host: encrypted off-provider backups with a drilled restore | Runbook only |
| R7 | Wheelhouse itself is tested in CI, backed up and replaceable from the laptop | CI and runner CLI; no host or backups |

---

## Topology — host platform services

The platform decisions live in the topology doc. Once they settle, Wheelhouse builds:

| Capability | Design |
|---|---|
| Host preparation | `host.py prepare <server>`, idempotent: Docker, deploy account, protected root, firewall, Tailscale, `platform` network, Traefik, PostgreSQL; rehearsed on the local server |
| Platform services as code | A server lists its platform services in `fleet.py`; the runner deploys them as the Compose project `platform` under the same lock and health gates |
| Databases | `needs: [postgres]` makes the runner create `<product>_<env>` and its role on first deploy; the password never leaves the host |
| Derived settings | The runner writes connection strings, `AllowedHosts` and public URLs into the settings file; operators supply only secrets |
| Networks | One network per target plus `platform`; a dev service cannot reach a prod service |
| Lifecycle | Stop, start and teardown per target under the target lock; teardown takes a final backup and the typed target ID |
| Capacity gate | A deploy is refused when the host's summed memory limits exceed its budget |
| Backups | restic per host: database dumps and named volumes, encrypted, off-provider; a restore drill into the local server |

The earliest safe live point follows this vector: host preparation, platform PostgreSQL and backups make a real host
repeatable. Today a VPS needs hand-placed Traefik, PostgreSQL and settings files.

---

## Secrets

A VPS target reads hand-placed settings files; Wheelhouse checks their keys but never writes them.

| Capability | Design |
|---|---|
| Settings rendering | The runner writes each service's settings file (`0600`) from code-owned target values, derived topology values and the vault token |
| Product secrets | Products resolve secrets from the vault at startup through the backend SDK vault consumer; Wheelhouse never reads a value |
| Deploy-time tokens | A deploy mints the target's vault token when it is missing and writes it into the settings file; it is never displayed |
| Expiring tokens | The vault mints tokens with an expiry; hygiene flags them before they lapse (vault API change) |
| Required-secret preflight | The target check confirms the vault namespace holds every key the service requires |
| Wheelhouse credentials | SSH keys, vault administrator passwords and the GitHub credential are encrypted at rest; SSH keys rotate through a runner action |
| Scoped management credential | Replaces the shared vault administrator password (vault-side change) |
| Audit | Every secret write, rotation, token mint and revoke is an audit row; values never are |

---

## Domains

| Capability | Design |
|---|---|
| Inventory | Domains synced from the registrar: expiry, auto-renew, nameservers; assigned to targets through code-owned site hosts |
| DNS plan and apply | Records derive from target site hosts and server addresses; Wheelhouse shows the plan (create, change, delete) and applies it with a per-zone token |
| Preview wildcard | One wildcard record per environment and server under the preview domain (topology point 25) |
| Certificates | Traefik issues them; Wheelhouse probes each site's certificate expiry |
| Expiry alerts | Domains and certificates at 30, 14 and 7 days |
| Pinned domains | A domain printed on physical goods (ForeverPin redirects) is pinned and cannot be reassigned or released |
| Local provider | A no-op DNS provider for `*.localhost`, so the whole flow runs locally |
| Purchase | Registrar search and purchase against a pre-funded balance, after the inventory works |

---

## Portfolio

| Capability | Design |
|---|---|
| Product catalog | One code-owned `catalog.py`: slug, repository, build workflow, registry, descriptor path; `fleet.py` targets and `artifacts.py` sources read it |
| Operator metadata | The database keeps status, notes, costs and kill-gate metrics, keyed by slug |
| Portfolio matrix | Products × environments: release, service versions, health, sites, last deploy |
| Lifecycle | Pause stops every environment; kill tears down with a final backup and archives the repository |
| Cost | Server monthly cost in `fleet.py`, domain cost from the registrar, allocated to products by memory share |
| Capacity and placement | Each host's memory budget against the declared limits of its targets; the view shows which host has room |
| Onboarding | Zero-to-live: `create-repo` scaffold with `deploy.yml` and CI, a catalog entry, the first dev deploy |

Adding a product today takes two console forms (the product, each environment), hand-placed settings files and a
manual database. At the portfolio's target of 50–100 launches, onboarding cost dominates.

---

## Product services map

| Capability | Design |
|---|---|
| Sites | Each site is an ingress node: host, path, exposure and the service it routes to |
| Platform needs | PostgreSQL, Valkey or broker nodes from the manifest's `needs`, shared across the host |
| Service versions | Each node carries the release in which its service last changed |
| Environment compare | `dev`, `test` and `prod` side by side; services whose versions differ are marked |
| Host view | Every target on a host, its platform services and memory allocation against capacity |

---

## Operations

| Capability | Design |
|---|---|
| Step log | Built in v0.3: each step's status, detail and timing in the job record; the dialog and inspector show them |
| Ingress probe | Built in v0.3: every site requested through the ingress by host name; a site that does not answer is a warning |
| Log tail | Built in v0.3: one service's last 1-1000 lines, read on request, never stored |
| Vitals history | Built in v0.3: a sampler keeps 30 days of per-target readings; the Fleet page draws trends |
| Alerts | Site down, disk above 85%, backup older than 26 hours, domain or certificate expiring, failed deploy |
| Notifications | Alerts and deploy outcomes to one channel |
| Image cleanup | A scheduled job deletes `sha-*` candidate images older than 14 days |

Uptime probes must run outside the product host's failure domain; the Wheelhouse host below provides that.

---

## Shipping Wheelhouse

Wheelhouse ships like a product: a catalog entry, a `deploy.yml`, candidate and release builds and a code-owned target.

| Concern | Design |
|---|---|
| Host | A small control VPS, Tailscale only, no public ports, separate from product hosts |
| Access | `tailscale serve` gives HTTPS on the tailnet name; a GitHub OAuth app registered for that URL; the allowlist names the owner |
| Image | The existing `engineering/deployment/Dockerfile`; one service `console`, volumes `keys` and `deployments`, `needs: [postgres]` |
| CI | Built in v0.3: `ci.yml` runs every tier; `publish-docker-image.yml` calls the shared `publish` workflow in `wow-two-platform.pipelines` |
| Bootstrap | Prepare the control host, submit the first release from the laptop with `transport.py`, copy the inventory once over SSH |
| Updates | Wheelhouse deploys its own releases; the target-side runner completes while the container is replaced |
| Break-glass | The laptop keeps the operator CLI and an inventory copy; it deploys or rolls back Wheelhouse and every product |
| Backups | A nightly encrypted dump of Wheelhouse's database plus the inventory and key volumes, off-provider |
| Local rehearsal | Built in v0.3: `rehearse.py self` builds Wheelhouse with `release.py` and deploys it to `wheelhouse-dev` |
| Production settings | `AllowedHosts` names the tailnet host; `Deployment:TrustedProxies` names the address `tailscale serve` connects from, so OAuth callbacks keep `https` |

Placement options:

- Laptop: no cost; alerts and uptime probes stop whenever the laptop sleeps.
- Product host: no extra cost; every SSH key sits beside public workloads and fails with them.
- Control host: a few euros a month; it survives a product host failure and probes it from outside.

---

## Sweep — gaps and defects

| # | Finding | Evidence | Lands in |
|---|---|---|---|
| S1 | Product identity lives in four places; adding a product takes four edits and a rebuild | Database `products`, `artifacts.py` `SOURCES`, `fleet.py` targets, the frontend's `WorkspaceProductBindings` | v0.3 ✓ |
| S2 | No audit trail of operator actions | Jobs record `actor`; vault changes reach only the app log (`VaultChangeCommandHandler.cs:17`); build requests and product edits keep no actor | v0.3 ✓ |
| S3 | A deploy shows only its outcome and reason, never its steps | The job record holds status, failure, reason and timestamps | v0.3 ✓ |
| S4 | Nothing requests a published site through the ingress | Smoke runs `compose exec <service> curl http://localhost:8080<path>` inside the container | v0.3 ✓ |
| S5 | Smoke probes and the `AllowedHosts` check assume port 8080 | `runner.py` smoke and `validate_settings`; the descriptor allows any `port` | v0.3 ✓ |
| S6 | No CI: tests never run on push and no image is published | No `.github/` in the repository | v0.3 ✓ |
| S7 | GitHub sign-in requests `repo` and `read:packages`; the runner holds a second token | `AuthConfigurationExtensions.cs`; `WHEELHOUSE_GITHUB_TOKEN_FILE` | Point 9 |
| S8 | Wheelhouse's own credentials are plain files | Inventory `ssh/`, `vaults/` and the GitHub token file | Secrets |
| S9 | The GitHub repository is public | `wow-two-platform/wow-two-platform.wheelhouse`, renamed from `drydock` on 2026-09-29 | Point 8 |
| S10 | Settings files on a VPS are placed by hand | Target settings are host paths | Secrets |
| S11 | Pausing or removing an environment needs SSH | `rehearse.py down` covers only the local server | Topology |
| S12 | No log view; diagnosing a failed deploy needs SSH | No log action in the runner | v0.3 ✓ |
| S13 | Vitals were read on demand only, and nothing alerts | History built in v0.3; alerts wait on Point 6 | Operations |
| S14 | Candidate images accumulate | The 14-day `sha-*` cleanup is specified, not built | Operations |
| S15 | Placeholder tables and the single-image version query remain | Backlog Cleanup | Foundation |
| S16 | Product docs predate environments, sites, commit builds and the prod gate | `features.md`, `flows.md`, `context.md` | v0.3 ✓ |
| S17 | Old local containers run beside the rig | `drydock-pilot-*`, `foreverpin-rehearsal-*`, the old console image | Foundation |
| S18 | Local console sign-in is still open | v0.3 Iteration 5: a second OAuth app for `:18210` | v0.3 |
| S19 | Targets never removed images, so every release and candidate pull stayed on disk | No `docker image rm` anywhere in the runner | v0.3 ✓ |
| S20 | Product writes skip the `X-Wheelhouse-Action` guard every other write carries | `ProductsController` POST/PUT/DELETE | v0.3 ✓ |
| S21 | Releases and candidates were visible only inside the deploy dialog | No catalog view | v0.3 ✓ |
| S22 | The target check never looked at the ingress, so a stopped Traefik passed | `check` covered SSH, Docker, disk, network, settings | v0.3 ✓ |
| S23 | No browser tests: every UI flow is verified by hand | Frontend tests cover schemas and pure rules only | Adoption version |
| S24 | Frontend schemas mirror API shapes by hand; nothing catches a drift | Zod schemas beside C# DTOs, no contract test | Adoption version |
| S25 | The audit chain proves no middle edit, but not that the newest entries were kept | No checkpoint outside the database | Operations |
| S26 | The code said version `0.1.0` through v0.3, and nothing showed a running version | `Directory.Build.props`, `package.json`, the status endpoint | v0.3 ✓ |
| S27 | Applied migrations are stamped `v1.0`, the SDK default, not the product version | `MigrationOptions.Version` is never set | Adoption version |
| S28 | Nine transitive backend packages carry advisories, five of them high | `dotnet list package --vulnerable --include-transitive` on the API | Adoption version |
| S29 | v0.3 missed the Studio workspace and base map and still described the retired sidebar | `v0.3.md` Iterations 2 and 12 | v0.3 ✓ |
| S30 | A rollout's outcome is recorded only while a page polls it | Only `transport.status` writes `observed/`; its one caller is `GET /api/deployments/{id}`, which the console polls; the API hosts `VitalsSampler` alone | Point 12 |
| S31 | An unpolled rollout stays `queued` in history and stats, publishes no catalog sites and leaves the test pass to an SSH read | `transport.jobs`, `stats`, `latest_sites`, `test_pass`; read from code, since all 27 local rollouts were followed | Point 12 |
| S32 | The submission index is JSON files parsed in full on every history, stats and catalog read, a second store to back up | `transport.jobs(root, limit=None)`; the `deployments` table stays an unused placeholder | Point 12 |
| S33 | A deploy's audit entry records its submission, never its outcome | `DeploymentStartCommand` is audited when the submit returns `queued` | Point 12 |
| S34 | Attention rules run in the browser, so nothing can alert without a page open | `apps/web/src/domain/overview/AttentionRules.ts` | Point 13 |
| S35 | The backend SDK cannot send a Telegram message | `Comms/` holds Email, Sms, Push and WhatsApp; Telegram is an OTP handler and a webhook validator | Point 13 |
| S36 | A service that starts beside its database fails its startup migration | Docker restart on 2026-10-01: ForeverPin `management` crashed twice on `57P03`, then started; the `wheelhouse:local` and `drydock:local` consoles stayed at 97% CPU, unhealthy, never restarted | Point 14 |
| S37 | No published release has passed through the runner | ForeverPin has no GitHub release and runs a copied workflow that pins `release.py` by blob; bundles are `linux/amd64`, the local server is `linux/arm64`, and `preflight` refuses a mismatch | Points 10, 15 |
| S38 | No automated test crosses the API, the runner, SSH and Docker | E2E uses `StubDeploymentGateway`; runner tests inject a Docker factory; the rig runs by hand; v0.3 holds 46 manual checks | Point 15 |
| S39 | The deployment API passes the runner's JSON through untyped | Controllers return `ApiResponse<JsonElement>`; the console's zod schemas mirror Python dictionaries (S24); MCP tools would have no schema | Point 16 |
| S40 | Each read costs a Python process and an SSH session per target; host vitals are read once per target, not per server | `DeploymentGateway.RunAsync`, `transport.vitals`, `useTargetStates`; the gateway times out at 110 seconds | Point 17 |
| S41 | Eleven more venture repositories carry a `deploy.yml`; the catalog names ForeverPin and Wheelhouse | The inventory; the eleven have no commit, remote or workflow; only Wheelhouse calls the shared publish workflow | Point 18 |
| S42 | Pins and notes lag their sources | Backend SDK `10.0.62` against `10.0.63`; UI SDK `0.0.9` against `0.0.12`; the vault mints with an expiry and rotates since its v0.3, yet the backlog says it mints by name only | Point 19 ✓ |
| S43 | The agreed product-scoped structure is unbuilt | Routes stay tool-scoped; `WorkspacePage.vue` holds 1,079 lines | Point 20 |

---

## Build order

Local-first: each wave completes on the local server. Live comes last and can move forward once Topology lands.
The dev cycle numbers a Feature version odd and follows it with an even Adoption version that extracts its blocks
to the SDKs, so each wave below takes the next odd version when it opens.

| Wave | Scope | Needs |
|---|---|---|
| Foundation | Rollout steps, site probes, log reads, ingress check, image cleanup, audit trail, CI, product catalog: done in v0.3. Left: placeholder cleanup | Point 12 |
| Topology | Host preparation, platform services, databases per target, derived settings, networks, lifecycle, capacity gate, backups and restore drill | Topology points 5–11, 15, 23 |
| Secrets | Settings rendering, deploy-time tokens, SDK vault consumer, expiring tokens, required-secret preflight, credentials at rest, SSH key rotation, GitHub App | Points 2, 3, 9 |
| Domains | Inventory and registrar sync, DNS plan and apply, preview wildcard, certificate and domain expiry, pinned domains | Points 4, 5; topology point 25 |
| Portfolio and map | Service map sites, needs and versions plus environment compare: done in v0.3. Left: portfolio matrix, lifecycle actions, cost, capacity view, onboarding, host view | Point 18 |
| Operations | Vitals history: done in v0.3. Left: an outcome follower, server-side attention rules, alerts, notifications, an audit checkpoint, candidate image cleanup | Points 12, 13 |
| Live | Control host, Tailscale, OAuth app, bootstrap, self-deploy, first product host, ForeverPin live | Points 7, 8 |

The next version, v0.4, is the Adoption version for v0.3: its stable blocks (the vault admin client, the action-header
guard, the audit behavior, the Vue refresh and log-viewer patterns) move to the SDKs, with browser and contract tests.

---

## Points

Decide top to bottom; a parent settles before its children.

1. [x] Product catalog: one code-owned `catalog.py` names every product; the database keeps operator metadata only. Built in v0.3.
2. [ ] Settings delivery: the runner renders settings files; products read secrets from the vault at startup; Wheelhouse never reads a value.
3. [ ] Vault placement: one vault per host, one namespace per product environment.
4. [ ] DNS ownership: records derive from code-owned site hosts; Wheelhouse plans and applies them; no hand-edited records.
5. [ ] Providers: Cloudflare DNS with per-zone tokens; the registrar after its own analysis, including payment from Uzbekistan.
6. [x] Alert channel: a Telegram bot for alerts and deploy outcomes.
7. [ ] Wheelhouse host: a separate control VPS on Tailscale; the laptop CLI stays the break-glass path.
8. [x] Repository visibility: private before `fleet.py` holds real host addresses.
9. [ ] GitHub access: one GitHub App replaces the sign-in `repo` scope and the runner's token file.
10. [ ] Real host timing: one hand-wired `linux/amd64` test host now, or only after the Topology wave.
11. [ ] Next version: Adoption first as the dev cycle orders, or Operations first with the re-pins folded in.
12. [ ] Outcome follower: the API follows every rollout to its end and keeps deployments in PostgreSQL; the target journal stays the authority.
13. [ ] Alerts: attention rules move into the API, the console renders them, and a Telegram sender lands in the SDK `Comms` first.
14. [ ] Startup: the SDK migrator waits, bounded, for a database that is still starting.
15. [ ] CI rig: an `amd64` runner deploys the built bundle to an SSH target container; a browser tier drives the console.
16. [ ] Runner contract: typed models at the gateway, one schema for the API, the console and MCP tools.
17. [ ] Runner reads: one SSH session reads every project on a server.
18. [ ] Onboarding slice: database provisioning and derived settings before the third product.
19. [x] Re-pin both SDKs before the joint verification, so the frame is verified once.
20. [ ] Product-scoped workspace after the first host.
21. [ ] MCP endpoint after Points 12 and 16.
22. [x] Inventory in PostgreSQL: products, environments, servers and vaults are rows edited in the console; the runner
    reads an exported snapshot; credentials stay files on the control host.
