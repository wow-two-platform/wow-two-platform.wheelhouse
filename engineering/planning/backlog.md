# Wheelhouse — Backlog

*Last updated: 2026-10-02*

Every unbuilt item; top of each group = next. The active version is the newest folder in
[version-track](version-track/).

## Features

| Feature | State | Boundary today |
|---|---|---|
| Product catalog | shipped v0.3 | Products, their environments and release sources edited in the console; lifecycle; sites and vault namespaces |
| Integration keys | shipped v0.3 | Scoped, revocable keys; `catalog:read` reads `/api/products`, nothing else |
| Server inventory | shipped v0.3 | Servers and their vaults edited in the console; credentials stay files on the control host |
| Environments | shipped v0.3 | `dev`, `test` and `prod` per product on one host; the local server runs all three |
| Release catalog | shipped v0.3 | Published releases and per-commit builds from approved repositories; a build starts for a commit without one |
| Deployment execution | shipped v0.3 | Pinned SSH, serialized rollout, health gates, prod only after test, live rollout steps, durable outcomes |
| Sites | shipped v0.3 | Products declare sites; the ingress routes them; each site is requested after a deploy |
| Recovery | shipped v0.3 | Independent runner; compatible image rollback; reconcile and redeploy from the dashboard |
| Service map | shipped v0.3 | Services, networks, volumes, startup order, sites, platform needs and versions; environment compare and promotion |
| Diagnostics | shipped v0.3 | Read-only target check; a service's recent logs on request |
| Audit | shipped v0.3 | Every operator action in a hash-chained trail, shown with its verification |
| Secrets console | shipped v0.3 | Namespaces, write-only secrets, product tokens, rotation hygiene; mounted runtime settings |
| Operations | shipped v0.3 | Host and container vitals, 30-day trends and deployment metrics, release drift, one attention list |
| Self-shipping | shipped v0.3 | CI on every push; releases through the shared pipeline |
| Domains | planned | Registrar and DNS integrations, expiry tracking; site hosts per target exist |
| Data | planned | Platform PostgreSQL per host, managed backups and verified restores; a backup runbook exists |
| Costs and capacity | planned | Provider billing and placement views; a manual host budget exists |

Dynamic provider plugins are excluded by product decision; a provider is an enum member plus its integration.

---

## Hosting

| Item | Type | Notes |
|---|---|---|
| Prepare a VPS for deployments with one command | feature | Traefik (file provider on `/srv/wheelhouse/ingress`), PostgreSQL, `platform` network, deploy account, protected root, firewall |
| Wire the first VPS: host, pinned SSH, ingress and domains | feature | Needs the wiring inputs under Open decisions |
| Deploy ForeverPin to the first VPS and watch its redirect | check | Live editor, create and scan; restart; redirect monitoring and headroom |
| Choose the shared preview domain for dev and test hosts | check | Topology point 25; one wildcard DNS record per environment |
| Start and stop an environment from Wheelhouse | feature | Topology point 15; `compose stop/start` under the target lock |
| Encrypted off-provider backups with a restore drill | feature | Product databases, key volumes, Wheelhouse state; decryption keys held off-host |
| Host Wheelhouse privately and let it deploy itself | feature | CI and its release workflow exist; needs a control host (completeness Point 7) |
| Retire `rehearse.py console` for the self-deployed console | check | `rehearse.py self` deploys Wheelhouse to `wheelhouse-dev`; it needs an OAuth app and the inventory mount |

---

## Operations

| Item | Type | Notes |
|---|---|---|
| Uptime, backup-age and disk alerts | feature | The vitals sampler exists; needs the alert channel (completeness Point 6) and an external probe |
| Notify deploy outcomes and alerts | feature | Needs the alert channel (completeness Point 6) |
| Stream a service's logs live | feature | Recent lines on request exist |
| Checkpoint the newest audit entries outside Wheelhouse | feature | The chain shows edits, not truncation of the newest entries |

---

## Secrets

| Item | Type | Notes |
|---|---|---|
| Grant a product environment its vault token during deployment | feature | Mint, then write into the target settings; never displayed |
| Render a target's settings from the vault at deploy time | feature | Mounted setting files today |
| Vault consumer in the backend SDK | feature | Startup resolution, bounded timeout, fail-closed; unblocks ForeverPin adoption |
| Scoped management credential for Wheelhouse | check | Vault-side change; replaces the shared administrator password |
| Mint expiring product tokens | feature | The vault takes an expiry and rotates since its v0.3; the gateway still sends a name only |

---

## Deployments

| Item | Type | Notes |
|---|---|---|
| Mount a GitHub token that can start product builds | check | `WHEELHOUSE_GITHUB_TOKEN_FILE` with Actions write on each product; the Build button needs it |
| Pull private GHCR images on targets | check | Wheelhouse's console image is private; a read-only pull token per target, or public images |
| Haven adopts `deploy.yml`, per-service versions and an edge health route | feature | Its Caddy edge has no health route; the runner requires one |
| Show each service's version inside every product | feature | Haven shows its build version beside the logo; adopt across products |
| Deploy a branch to its own temporary dev environment | feature | Topology point 17; from a code-owned template; later PR previews |
| Browse releases older than the recent catalog | feature | The target journal already retains deployed bundles |
| Per-service versions in the release catalog | feature | The catalog lists releases; versions need each bundle's manifest |
| Delete `sha-*` candidate images older than 14 days | feature | A scheduled workflow per product; the descriptor convention names it |
| Signed provenance for release bundles | feature | Attestation check before selection |
| Private release-asset download | feature | Token-authenticated catalog for private repositories |
| Zero-downtime replacement | idea | Blue/green only when measured demand warrants it |

---

## Domains

| Item | Type | Notes |
|---|---|---|
| Registrar search and purchase against a pre-funded balance | feature | Registrar choice open |
| DNS records and ingress routes per product environment | feature | Per-zone scoped tokens |
| Domain and certificate expiry tracking | feature | A dead domain is a dead product |

---

## Portfolio

| Item | Type | Notes |
|---|---|---|
| Ownership and kill-gate metrics on the catalog | feature | Products are database rows since v0.3; metrics need cost and usage feeds |
| Second provider and a placement view | feature | Provider enum plus integration in code |
| Cost per product and host | feature | Feeds the micro-SaaS kill gates |
| Host view with capacity and a portfolio matrix | feature | Beside the per-environment service map |
| Teardown with a final backup and archive | feature | |
| Zero-to-live scaffold from the product template | feature | Repository, CI, first deployment |

---

## Integrations

| Item | Type | Notes |
|---|---|---|
| MCP endpoint for Claude and Codex | feature | Tools over the existing handlers; the backend SDK `Ai/Mcp` module is empty today |
| Build, deploy and log scopes for integration keys | feature | `builds:write`, `deployments:read`, `deployments:write`, `logs:read`; prod keeps the typed target ID |
| Key expiry and rotation reminders | feature | Keys live until revoked today |

---

## SDK adoption

| Item | Type | Notes |
|---|---|---|
| Move the frame onto the SDK `AppShell` and `Navbar` | check | `AppLayout.vue` hand-builds the region-scrolling frame the SDK now ships |
| Replace the E2E `TestAuth` with the SDK's `AddTestAuth` header gate | check | Keep anonymous → 401 and admin → 200 |
| Replace the local `Stub*` clients with the SDK testing fakes | check | `Tests.E2E/Harness` |
| Derive the integration key repository from the SDK `EfRepository` | check | Products, servers, targets and vaults moved in v0.3; keep the key's `CreatedAt`/`Id` ordering |
| Extract the "allowlisted session or scoped key" policy to the backend SDK | check | `Api/Auth/ProductsReadAuthorizationHandler.cs` proves it |
| Extract the vault admin client to the backend SDK | check | v0.3 proves it |
| Extract repository tree and file reads to the backend SDK GitHub client | check | `Infrastructure/Products/GitHubProductIconSource.cs` calls the REST API inline |
| UI SDK `Table` sticky-header option | feature | `TableStyles` pins an opaque head meanwhile |
| UI SDK `AppShell` page-header actions slot | feature | `PageActions` teleports them meanwhile |
| Move the Wheelhouse palette into the UI SDK theme registry | check | Lives in `bootstrap/index.css` today |

---

## Cleanup

| Item | Type | Notes |
|---|---|---|
| Retire the placeholder deployment, domain and secret tables | issue | Deployments return with the outcome follower (Point 12); domains and secrets stay elsewhere |
| Shape-keeping first loads on the remaining pages | feature | Workspace, Deployments, Servers, Products and Activity still show block skeletons on a first load |
| Rename the runner's `fleet.py` and `rehearse.py` | check | The screen says Servers and local server; both now hold shapes and rig fixtures only |
| Stamp applied migrations with the product version | issue | `MigrationOptions.Version` keeps the SDK default `v1.0` |
| Rename backend tests to `{Unit}_Should{Expectation}_When{Condition}` | check | Tests older than the v0.3 catalog predate the testing convention's naming rule |
| Adopt the product template's ESLint, Prettier config and `format:check` gates | check | 86 app files predate a formatter config; format once in a dedicated commit |

---

## Open decisions

| Item | Notes |
|---|---|
| First VPS wiring inputs | Existing or new host, architecture, RAM/disk and cost; management and redirect domains; Google and Stripe callbacks; recovery targets and backup destination; private pilot or public launch |
| Registrar | Settle before the Domains group |
| One vault per environment or one per host with namespaces | Vault docs assume one per product environment |
| Prod Wheelhouse as the source of truth | The VPS instance owns settings and secrets; dev pulls from it and drops its own additions; secret flow prod → dev needs a security analysis first |
