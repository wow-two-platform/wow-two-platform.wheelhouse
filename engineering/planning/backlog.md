# Wheelhouse — Backlog

*Last updated: 2026-10-17*

Every unbuilt item; top of each group = next. The active version is the newest folder in
[version-track](version-track/).

## Features

| Feature | State | Boundary today |
|---|---|---|
| Product catalog | shipped v0.3 | Products, their environments and release sources edited in the console; lifecycle; sites and vault namespaces; name, description and homepage read from the repository's manifest |
| Integration keys | shipped v0.3 | Scoped, revocable keys; `catalog:read` reads `/api/products`; catalog and deployment scopes authorize read-only MCP tools |
| MCP reads | shipped v0.3 | Private stateless endpoint; project/fleet reads, scoped discovery and immediate revocation |
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
| Sign in to the local console through a localhost GitHub OAuth app | feature | Local rig sign-in; the `rehearse.py console` check depends on it |
| Tell dev from prod by an environment tag beside the version | feature | Dev and prod consoles run side by side |
| Prepare a VPS for deployments with one command | feature | Traefik (file provider on `/srv/wheelhouse/ingress`), PostgreSQL, `platform` network, deploy account, protected root, firewall |
| Accept the hosted console: owner sign-in, restart persistence, authenticated MCP | check | After Wheelhouse prod is live (user 2026-10-13); dev already passed CI's rollout, HTTPS and anonymous-MCP checks |
| Give Wheelhouse a GitHub App installed on every org | feature | User decision 2026-10-14: reads, commits (identity edits), workflow runs and repository renames. Today catalog reads are anonymous and icons use the operator's expiring sign-in token |
| Add a second VPS for products once Wheelhouse dev runs | check | User decision 2026-10-09: the Wheelhouse host stays control-plane only (no public 80/443); products get their own host |
| Wire the products VPS: host, pinned SSH, public ingress and domains | feature | The control-plane VPS was wired 2026-10-13 through `vps/vps.md`; reuse it with public ingress |
| Deploy ForeverPin to the products VPS and watch its redirect | check | Live editor, create and scan; restart; redirect monitoring and headroom |
| Choose the shared preview domain for dev and test hosts | check | Topology point 25; one wildcard DNS record per environment |
| Start and stop an environment from Wheelhouse | feature | Topology point 15; `compose stop/start` under the target lock |
| Encrypted off-provider backups with a restore drill | feature | Product databases, key volumes, Wheelhouse state; decryption keys held off-host. OVH's daily VPS backup is the only copy today. Multi-cloud replication and failover are on the table (user 2026-10-13) |
| Retire `rehearse.py console` for the self-deployed console | check | `rehearse.py self` deploys Wheelhouse to `wheelhouse-dev`; it needs an OAuth app and the inventory mount |

---

## Operations

| Item | Type | Notes |
|---|---|---|
| Uptime, backup-age and disk alerts | feature | The vitals sampler exists; needs the alert channel (completeness Point 6) and an external probe |
| Notify deploy outcomes and alerts | feature | Needs the alert channel (completeness Point 6) |
| Stream a service's logs live | feature | Recent lines on request exist |
| Checkpoint the newest audit entries outside Wheelhouse | feature | The chain shows edits, not truncation of the newest entries |
| Ability to see an unreadable host in the attention list | feature | Servers shows the reason since v0.4; the attention list skips it |

---

## Secrets

| Item | Type | Notes |
|---|---|---|
| Grant a product environment its vault token during deployment | feature | Mint, then write into the target settings; never displayed |
| Render a target's settings from the vault at deploy time | feature | Mounted setting files today |
| Vault consumer in the backend SDK | feature | Startup resolution, bounded timeout, fail-closed; unblocks ForeverPin adoption |
| Scoped management credential for Wheelhouse | check | Vault-side change; replaces the shared administrator password |
| Mint expiring product tokens | feature | The vault takes an expiry and rotates since its v0.3; the gateway still sends a name only |
| Ability to share one setting or secret value across products | feature | User 2026-10-17: products declare their own keys; Wheelhouse links keys that hold the same value and must change together to one row; otherwise rows stay separate |

---

## Deployments

| Item | Type | Notes |
|---|---|---|
| Rehearse every deployment flow from dev on a sample app | feature | Promotion from dev to prod ships; this proves each flow end to end |
| Analyze the zero-disruption deployment contract | product | User 2026-10-15: what every app must implement so deployments never break work in flight; covers the rows below; the outcome becomes a wow2 convention |
| Ability to drain requests and show maintenance before a rollout | feature | The service refuses new requests, finishes in-flight ones, and its pages say so |
| Ability to deploy when a service is idle, with an estimate | feature | Services report busy work Wheelhouse must not interrupt; Wheelhouse tells the operator when the rollout will run |
| Ability to schedule a deployment for a set time | feature | E.g. a release set for tomorrow 2 PM |
| Ability for product frontends to refresh onto a new version | feature | Show "new version available"; before a write, refresh or warn so old pages never call a new API |
| Ability to dry-run pending migrations before a rollout | feature | The SDK migrator applies a batch atomically and dry-runs it (`wow-migrate apply --dry-run`, exit 2 on failure). Open: how the runner reaches the new image's embedded migrations (copy them out, ship the CLI in the image, or an app dry-run entry point); skip the gate on a first deploy |
| Roll prod back by promoting the previous release | feature | Dev-to-prod promotion exists |
| Run each app's migrations as a rollout step and show their state | feature | User decision 2026-10-14: apps own their SQL; the runner migrates with the new image between pull and start; prod waits for test. The app database's own migration journal stays the authority; Wheelhouse reads it at deploy and keeps what it saw in the job history, never a second table. Rollback scripts run only on a confirmed operator action |
| Mount a GitHub token that can start product builds | check | `WHEELHOUSE_GITHUB_TOKEN_FILE` with Actions write on each product; the Build button needs it |
| Pull private GHCR images on targets | check | Public images need no token (Wheelhouse's console is public since 2026-10-09); private ones need a read-only pull token per target |
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
| Add `product/product.json` to ForeverPin, every other product repository and the product template | feature | Decided 2026-10-15: identity text is a manifest in the repository ([convention](../../../../../conventions/design/identity/product-manifest.md)); Wheelhouse and the canary carry one; until a repo does, its row supplies the name and description |
| Edit a product's identity from the console by committing its manifest | feature | Needs the GitHub App (Hosting group); the console shows manifest-sourced name, description and homepage read-only meanwhile |
| Refresh a product's manifest on push | feature | A GitHub App push webhook feeds the same commit-keyed row; the 5-minute head check and the Refresh action stay as fallbacks |
| Add the icon set to ForeverPin and every other product repository | feature | Each product repo carries `product/brand/icons/` per the convention; until then Wheelhouse shows its web app favicon or a monogram |
| Ship Wheelhouse's monochrome icon variants | check | The approved artwork keeps a gray fold in every variant; a one-ink export needs a brand decision |
| Ownership and kill-gate metrics on the catalog | feature | Products are database rows since v0.3; metrics need cost and usage feeds |
| Second provider and a placement view | feature | Provider enum plus integration in code |
| Cost per product and host | feature | Feeds the micro-SaaS kill gates |
| Host view with capacity and a portfolio matrix | feature | Beside the per-environment service map |
| Teardown with a final backup and archive | feature | |
| Fix the console requesting icons a repository doesn't have | fix | Return no icon URL when the repo has none; the console now remembers each 404 for 12 hours |
| Zero-to-live scaffold from the product template | feature | Repository, CI, first deployment |
| Ability to build one product from several repositories | feature | Parked 2026-10-17: components, each repo with its own manifest and releases, grouped Product → Environment → Component → Service; every product is single-repo today |

---

## Integrations

| Item | Type | Notes |
|---|---|---|
| Audit every MCP tool call | feature | [MCP review](../research/mcp-review.md): key, tool, target, outcome and duration; required before any write tool |
| Limit MCP concurrency and rate per key | feature | Each runner tool spawns Python and SSH for up to 110 s; cap 2–4 concurrent calls |
| Ability to expire integration keys and see them near expiry | feature | Keys live until revoked today; default 90 days |
| Connect Claude Code to prod MCP with the key in the Keychain | check | The pending authenticated acceptance; a headers helper keeps the key out of the plain-text client config |
| Cover the 2026-07-28 MCP protocol in E2E | engineering | Tests use 2025-11-25 only |
| Build, deploy and log tools for agents | feature | `builds:write`, `deployments:write`, `logs:read` on short-lived keys; server-side approval (plan, approve in the console, execute); prod keeps the typed target ID |

---

## Experience

User 2026-10-15: layout, motion and notifications must work flawlessly and look their best. The rest of this group moved
to v0.5 on 2026-10-17, a full platform ideation, check and layout redesign; v0.4's manual checks and the carried v0.3
checks were dropped for it (the list stays in git history).

| Item | Type | Notes |
|---|---|---|
| Analyze full-page versus modal dialogs | product | Then record the dialog convention in wow2 |

## SDK adoption

Leftovers of the v0.4 sweep. Items marked *decision* wait on the owner.

| Item | Type | Notes |
|---|---|---|
| Extract the vault admin client → SDK | engineering | *decision*: SDK `Integrations` or a client package from the secrets-vault repo; `Infrastructure/Vaults/VaultGateway.cs`, session cache with one retry on 401, plus a fake |
| Extract the audited-command trail → SDK | engineering | *decision*: marker, interceptor, EF chain store with lock, paging and verify; the SDK has the primitives only |
| Extract a periodic scoped job → SDK | engineering | `VitalsSampler`, `DeploymentOutcomeFollower`: interval, 0 = off, log and continue; SDK `Jobs/Core` is empty |
| Extract a bounded process runner → SDK | engineering | `DeploymentGateway.cs`: timeout, kill tree, scrubbed environment, JSON stdout |
| Extract OAuth sign-in, me and sign-out endpoints → SDK | engineering | `IdentityController.cs`; registration only when configured |
| Extract an actor context → SDK | engineering | *decision*: session login or `key:{name}`; SDK `ICurrentUserService` is Guid-only |
| Extract GitHub, image and hostname rules → SDK validation vocabulary | engineering | `InventoryPatternConstants.cs`, `InventoryRuleExtensions.cs`; the backend half of the shared vocabulary |
| Adopt SDK token paging for the audit list | engineering | Wire change with the Activity page |
| Extract atomic file write and an EF upsert → SDK | engineering | `InventorySnapshotExporter.cs`, `EfProductManifests.cs` |
| Replace the local `Stub*` clients with SDK testing fakes | engineering | Waits on the vault and repository-reader extracts shipping fakes; the SDK fakes today cover clients Wheelhouse doesn't call |
| Extract a selectable list → UI SDK | engineering | `ProductsPanel`, `WorkspacePage`, `NamespaceList`: leading, title, meta, trailing, skeleton rows |
| Extract the rows field → UI SDK | engineering | `RowsField.vue` over `useFieldArray`; stacks on phones |
| Extract a log viewer that follows the tail → UI SDK | engineering | `ServiceLogsModal.vue` |
| Extract a schema decoder and problem reason → UI SDK HTTP client | engineering | `integration/common/client.ts` |
| Extract a status strip and tone classes → UI SDK | engineering | `WorkspacePage.vue` |
| Make the Sparkline time-based with thresholds → UI SDK | engineering | `TrendLine.vue` |
| Extract the MCP request guard → be sdk | engineering | `McpRequestGuard.cs`: Origin 403 and a 64 KiB body cap that `MapAuthenticatedMcp` lacks |
| Adopt be sdk reserved SPA prefixes for `/.well-known` | engineering | Replaces the extra 404 fallback in `HostConfiguration.cs` once the SDK ships it |

---

## Cleanup

| Item | Type | Notes |
|---|---|---|
| Retire the placeholder deployment, domain and secret tables | issue | Outcome following uses durable runner files; deployment database ownership remains separate; domains and secrets stay elsewhere |
| Return `servers[]` from the shared `StubDeploymentGateway` vitals | check | The v0.4 server-vitals E2E test carries its own stub instead |
| Rename the runner's `fleet.py` and `rehearse.py` | check | The screen says Servers and local server; both now hold shapes and rig fixtures only |
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
