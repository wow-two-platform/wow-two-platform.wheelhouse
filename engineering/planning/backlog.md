# Wheelhouse — Backlog

*Last updated: 2026-10-15*

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

## Verification

| Item | Type | Notes |
|---|---|---|
| Push a new name to a product's `product/product.json` → Products shows it after Refresh or within five minutes, read-only | check | carried from v0.3 |
| Connect an MCP client → only its scoped read tools are available | check | carried from v0.3 |
| Revoke its key → the following MCP request is refused | check | carried from v0.3 |
| Register an OVHcloud server → its provider survives a reload | check | carried from v0.3 |
| Close the browser mid-deploy → history records the final outcome | check | carried from v0.3 |
| Restart Wheelhouse mid-deploy → outcome tracking resumes | check | carried from v0.3 |
| Deploy a release to the local target → history shows the outcome after a reload | check | carried from v0.3 |
| Blank a required setting → the check names the key; a deploy changes nothing | check | carried from v0.3 |
| Interrupt a rollout → reconciling from the dashboard unlocks the target | check | carried from v0.3 |
| Set a secret in the console → a product token reads the new version | check | carried from v0.3 |
| Run `rehearse.py run` → the dev site opens at `http://app-foreverpin.dev.localhost:18080` | check | carried from v0.3 |
| Deploy a release to test beside dev → both sites answer at once, each on its own host | check | carried from v0.3 |
| Deploy to local prod without typing its ID → refused; typed → deployed | check | carried from v0.3 |
| Choose a commit build for test → refused before anything reaches the target | check | carried from v0.3 |
| Deploy an untested release to prod → refused; skipping with the typed ID → deployed | check | carried from v0.3 |
| Build a commit without a build → its workflow starts; a built commit offers Use build | check | carried from v0.3 |
| Stop a container on the local target → the fleet page shows it stopped and the overview flags it | check | carried from v0.3 |
| Deploy a broken release → the overview's failure count and success rate change | check | carried from v0.3 |
| Open the secrets page → each secret and token shows its age; overdue ones are flagged | check | carried from v0.3 |
| Run `rehearse.py console`, sign in with GitHub and open every page → each page loads without errors | check | carried from v0.3 |
| Deploy to dev → the dialog lists each step as it runs; the workspace inspector shows them after a reload | check | carried from v0.3 |
| Stop the local ingress and deploy → the deploy succeeds with a warning, and the site link flags it | check | carried from v0.3 |
| Check a target with the ingress stopped → the check names the ingress | check | carried from v0.3 |
| Open a service in the workspace → Read logs shows its latest lines; Refresh reads them again | check | carried from v0.3 |
| Deploy, then change a secret → Activity lists both with the operator, and the chain shows intact | check | carried from v0.3 |
| Push to GitHub → `ci` runs all three jobs; `publish-docker-image` uploads `bundle-<sha>` | check | carried from v0.3 |
| Push to `main` → the shared workflow releases `v0.3.Z`, and the top bar shows that version | check | carried from v0.3 |
| Open dev's service map → each site links out with its answer state, and `postgres` shows as a platform service | check | carried from v0.3 |
| Deploy a release to test only → Environments highlights what prod lacks and offers the promotion to prod | check | carried from v0.3 |
| Open Deployments → Releases lists releases and commit builds; its Deploy button opens the dialog with it chosen | check | carried from v0.3 |
| Leave Wheelhouse running for an hour → the Fleet page draws load, memory and disk lines for each server | check | carried from v0.3 |
| Run `rehearse.py self` → Wheelhouse's console answers at `http://console-wheelhouse.dev.localhost:18080` | check | carried from v0.3 |
| Open the workspace → the product rail selects a product; the inspector follows the selected service | check | carried from v0.3 |
| Open a service map → the controls, Ctrl + scroll and a pinch zoom it; a drag pans; a click still selects | check | carried from v0.3 |
| Open the profile menu → it shows the console version and the API version with its commit | check | carried from v0.3 |
| Sign out → the sign-in screen shows only the name, one line and the GitHub button | check | carried from v0.3 |
| Show scroll bars always (macOS setting) → the top bar still reaches the window's right edge | check | carried from v0.3 |
| Open Secrets → its frame, tabs and table headers show at once; only values load | check | carried from v0.3 |
| Refresh Secrets → statuses, counts and versions flash; vault, namespace and key names stay | check | carried from v0.3 |
| Open an old `/fleet` link → it lands on Servers with the same product selected | check | carried from v0.3 |
| Open Deploy a commit on a short window → the dialog fits and its commit list scrolls | check | carried from v0.3 |
| Click a commit's short SHA → GitHub opens that exact commit | check | carried from v0.3 |
| Build a commit of a product without a build workflow → no Build button; the note says why | check | carried from v0.3 |
| Choose a branch in a product's repository menu → the link and the clone command follow it | check | carried from v0.3 |
| Open Products → each product shows its repository icon, or a tinted letter when it has none | check | carried from v0.3 |
| Open the console on a slow session read → the wordmark and bar show at once and fill, then the page appears | check | carried from v0.3 |
| Switch to dark → the top bar and sign-in show the white wordmark; the tab shows the white boat | check | carried from v0.3 |
| Run `pnpm install && pnpm dev` in the frontend workspace → the console serves at `https://localhost:5174` | check | carried from v0.3 |
| Open Products → each product shows its environments, their sites and vault namespaces | check | carried from v0.3 |
| Set a product's lifecycle → Products and the workspace show it after a reload; Activity records it | check | carried from v0.3 |
| Create an integration key → it shows once; `curl` with it reads `/api/products` but gets 401 from `/api/deployments/targets` | check | carried from v0.3 |
| Revoke the key → the same read gets 401; the key list shows it revoked, with its last use | check | carried from v0.3 |
| Add a product with a dev environment on the local server → the runner lists its target after the save | check | carried from v0.3 |
| Delete a product an environment still uses → refused with the reason; remove the environment → deleted | check | carried from v0.3 |
| Add a server without its `ssh/<slug>/` files → saved; a check on its target names the missing identity | check | carried from v0.3 |
| Restart `rehearse.py console` → the local server, its targets and vault return; operator products stay | check | carried from v0.3 |
| Save an environment with a settings path `etc/x.json` → the error shows under that row's path | check | carried from v0.3 |

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
| Ability to group products into workspaces | feature | User 2026-10-15: personal, wow2 platform, wow2 backbone, wow2 apps, own ventures, other ventures |
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
| Zero-to-live scaffold from the product template | feature | Repository, CI, first deployment |

---

## Integrations

| Item | Type | Notes |
|---|---|---|
| Build, deploy and log tools for agents | feature | `builds:write`, `deployments:write`, `logs:read`; prod keeps the typed target ID; read-only MCP is implemented |
| Key expiry and rotation reminders | feature | Keys live until revoked today |

---

## Experience

User 2026-10-15: layout, motion and notifications must work flawlessly and look their best; pull this group first.

| Item | Type | Notes |
|---|---|---|
| Analyze the console's information architecture | product | Secrets and Activity may not need their own tabs; Workspace could carry activity as widgets. Decides the phone navigation below |
| Ability to navigate on phones from a bottom bar | feature | User 2026-10-15: bottom menus work best on phones; waits on the analysis above |
| Ability to follow a deployment in a compact progress notification | feature | Closing the deploy dialog keeps the rollout visible; the SDK feedback bus needs progress notices any app can publish |
| Ability to see rollout steps and page transitions animate | feature | Steps flip to done with no motion today; sweep transitions across the layout |
| Ability to swipe a table row on phones for its two main actions | feature | Part of a sweep of intuitive phone gestures, navigation and transitions for the SDK and conventions |
| Analyze full-page versus modal dialogs | product | Then record the dialog convention in wow2 |
| Ability to see an icon on each rollout step | feature | Lucide first; a Font Awesome-derived icon pack is planned |

## Product views

| Item | Type | Notes |
|---|---|---|
| Ability to see services grouped under their product and environment | feature | User 2026-10-15: products run several services. Servers nests server → target → containers, and the Deployments targets panel is a flat target list. Group both by product, then environment, then service. |

## Console UI

| Item | Type | Notes |
|---|---|---|
| Fix environments listing as dev, prod, test | fix | Order them dev, test, prod everywhere |
| Fix a new server reading "Host vitals are unavailable" until a target exists | fix | Read host vitals from the server itself |
| Fix a finished job's inspector refetching vitals, history and stats | fix | `useDeploymentOutcome` refetches them when it opens a job that already finished |

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

---

## Cleanup

| Item | Type | Notes |
|---|---|---|
| Retire the placeholder deployment, domain and secret tables | issue | Outcome following uses durable runner files; deployment database ownership remains separate; domains and secrets stay elsewhere |
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
