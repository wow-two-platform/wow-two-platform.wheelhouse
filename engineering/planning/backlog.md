# Wheelhouse — Backlog

*Last updated: 2026-10-27*

Every unbuilt item; top of each group = next. The active version is the newest folder in
[version-track](version-track/).

## Features

| Feature | State | Boundary today |
|---|---|---|
| Product catalog | shipped v0.3 | Products, their environments and release sources edited in the console; lifecycle; sites and vault namespaces; name, description and homepage read from the repository's manifest |
| Integration keys | shipped v0.3 | Scoped, revocable keys; `catalog:read` reads `/api/products`; catalog and deployment scopes authorize read-only MCP tools |
| MCP reads | shipped v0.3 | Private stateless endpoint; project/fleet reads, scoped discovery and immediate revocation |
| Server inventory | shipped v0.3 | Servers and their vaults edited in the console; credentials stay files on the control host |
| Environments | shipped v0.3 | `dev` and `prod` per product; each `main` release deploys to dev, then is promoted to prod |
| Release catalog | shipped v0.3 | Published releases and per-commit builds from approved repositories; a build starts for a commit without one |
| Deployment execution | shipped v0.3 | Pinned SSH, serialized rollout, health gates, prod only after dev, live rollout steps, durable outcomes |
| Sites | shipped v0.3 | Products declare sites; the ingress routes them; each site is requested after a deploy |
| Recovery | shipped v0.3 | Independent runner; compatible image rollback; reconcile and redeploy from the dashboard |
| Service map | shipped v0.3 | Services, networks, volumes, startup order, sites, platform needs and versions; environment compare and promotion |
| Diagnostics | shipped v0.3 | Read-only target check; a service's recent logs on request |
| Audit | shipped v0.3 | Every operator action in a hash-chained trail, shown with its verification |
| Secrets console | shipped v0.3 | Namespaces, write-only secrets, product tokens, rotation hygiene; mounted runtime settings |
| Built-in Secrets Vault | shipped v0.5 | Per-product keys under a root key file, versions, expiry, typed and owner-added keys, one-transaction saves, apply now or later, failed-deploy hints, imports |
| Operations | shipped v0.3 | Host and container vitals, 30-day trends and deployment metrics, release drift, one attention list |
| Self-shipping | shipped v0.3 | CI on every push; releases through the shared pipeline |
| Workspaces and pins | shipped v0.5 | Products grouped into workspaces; active or inactive products and services; pinned and reordered workspaces, products and services |
| Console navigation | shipped v0.5 | Sidebar on desktop, bottom bar on phones; Home, Products, Deployments and Servers; a page per product, workspace and deployment |
| Hub catalogs | shipped v0.5 | Every hub's catalog listed; products, whole workspaces and multi-repository entries imported |
| Environment settings | shipped v0.5 | Declared settings and secrets per service, edited per environment from allowed values; missing required keys flagged; changes applied as a recorded deployment, now or at a set time |
| Deployments view | shipped v0.5 | Running rollouts, a daily activity grid, filters, grid and list, a page per deployment; eight states with the services each deploy touched |
| Products view | shipped v0.5 | Workspaces then products as cards or rows; search, filter and sort; environment pills, a site menu and deploy trends |
| Deployment automation | shipped v0.5 | Auto-deploy to dev, freezes, changelogs, rollback risk, prod snapshots, watch and auto-promote, retention, build minutes; a GitHub App for every owner |
| Fleet rollouts | shipped v0.5 | Many products promoted in one run; only changed services deployed, in dependency order, each restarted only on its own changes |
| Products hosts | shipped v0.5 | Servers enrolled onto the tailnet from the console; databases and private sites placed; public sites on baseline sslip.io hosts; kit refresh; per-deploy registry sign-in |
| Self-deploy | shipped v0.5 | Prod deploys each Wheelhouse release to dev and promotes itself; a host-side deploy and rollback when no console answers |
| Release sets | shipped v0.5 | A product of several repositories; its release is a set of repository releases, promoted together |
| Domains | planned | Registrar and DNS integrations, expiry tracking; site hosts per target exist |
| Data | planned | Platform PostgreSQL per host, managed backups and verified restores; a backup runbook exists |
| Costs and capacity | planned | Provider billing and placement views; a manual host budget exists |

Dynamic provider plugins are excluded by product decision; a provider is an enum member plus its integration.

---

## Hosting

| Item | Type | Notes |
|---|---|---|
| Burn in Wheelhouse's self-deploy with three self-promotions | product | Runs to 2026-10-31; the first self-promotion ran 2026-10-24. Fallback in `deployment.md` § Activation |
| Ability to manage the control host's firewall, SSH and updates like a products host | feature | |
| Ability to rebuild the control host from backups, rehearsed | feature | |
| Sign in to the local console through a localhost GitHub OAuth app | feature | Local rig sign-in; the `rehearse.py console` check depends on it |
| Tell dev from prod by an environment tag beside the version | feature | Dev and prod consoles run side by side |
| Accept the hosted console: owner sign-in, restart persistence, authenticated MCP | check | After Wheelhouse prod is live (user 2026-10-13); dev already passed CI's rollout, HTTPS and anonymous-MCP checks |
| Install the GitHub App and place its key on both consoles | check | Owner steps in `deployment.md` § GitHub access; Deployments › Issues names any owner still without it |
| Drop the sign-in `repo` and `read:packages` scopes | fix | Once the GitHub App serves every owner; package versions then need the token file |
| Rename a product's repository through the GitHub App | feature | User decision 2026-10-14; needs Administration write, the App has Actions write and Contents and Metadata read |
| Launch Ocharo publicly on its own domain | product | User 2026-10-17: the first product to go live (`app-t-005`), after decision 22's list; ForeverPin follows |
| Choose the shared preview domain for dev hosts | check | Topology point 25; one wildcard DNS record; `test` was retired 2026-10-17 |
| Retire `rehearse.py console` for the self-deployed console | check | `rehearse.py self` deploys Wheelhouse to `wheelhouse-dev`; it needs an OAuth app and the inventory mount |
| Ability to confirm dangerous actions with a second factor | feature | User 2026-10-17: deleting a server and similar actions ask for a fresh second factor. GitHub sign-in can't force a new 2FA check, so prefer a passkey (be sdk identity has passkeys and TOTP) with an authenticator-app code as fallback |

---

## Operations

| Item | Type | Notes |
|---|---|---|
| Keep an index of unfinished jobs and archive finished job records | engineering | 2026-10-27 idle-load investigation: every `follow`, `history` and `infra.py finished` pass still reads each `jobs/`, `observed/` and infra journal file, so a pass grows with the history; an index of unfinished submissions and an archive of finished records keep it flat |
| Run one long-lived runner process instead of one per call | engineering | 2026-10-27: each runner call starts Python and imports the runner (the v0.7 bytecode cut the compile, not the start); a resident runner answering the API over a local socket removes the start cost from every read |
| Replace the Python runner and host scripts with .NET code | engineering | Owner 2026-10-27, for later: one language across the API and the runner, and a .NET runner can be the long-lived process above; covers `codebase/wheelhouse.runner-services/` (20 modules) and the Python in `deployment/vps/` and `deployment/rehearsal/` |
| Publish the API ReadyToRun | engineering | 2026-10-27: faster warm-up after a restart or a self-promotion; weigh the larger image |
| Ability to show a server's logs and feedback through its governance service | feature | Owner 2026-10-26: Wheelhouse keeps widgets, sign-in and forwarding over the tailnet; collection, search and alerts run in each server's governance service (wow2 `docs/server-governance.md`) |
| Ability to get notified of deploy outcomes | feature | |
| Ability to schedule host reboots and Docker and Tailscale upgrades | feature | |
| Ability to be warned before a credential, key or certificate expires | feature | |
| Checkpoint the newest audit entries outside Wheelhouse | feature | The chain shows edits, not truncation of the newest entries |
| Ability to see an unreadable host in the attention list | feature | Servers shows the reason since v0.4; the attention list skips it |
| Show the manifest's service names and descriptions on the product page | feature | They showed only on the old Products detail, removed in v0.5 |
| Compact the product header on phones | fix | It pushes the tabs below the fold |
| Preselect the product's targets when deploying from its Deployments tab's releases | feature | |
| Point vault attention items at `/secrets?vault=…` | fix | |
| Drop the duplicate "Host vitals are unavailable" line under an unreachable server's alert | fix | |

---

## Secrets

| Item | Type | Notes |
|---|---|---|
| Import the standalone vault on prod, verify it and retire it | engineering | Owner steps in `deployment.md` § Importing the standalone vault |
| Ability to back up the vault with its keys kept apart, and prove a restore | feature | `vault verify` and the canary exist; waits on Wheelhouse's database backups |
| Ability to rotate the vault's root key with a transactional rewrap | feature | 2026-10-19 merge analysis: unwrap every product key under the old root key, wrap under the new one, prove the canary, one transaction; the owner swaps the file after |
| Ability to recover a lost root key with the owner's passkey | feature | User 2026-10-20 (decision 67): the database keeps the root key sealed by a passkey-derived key (WebAuthn PRF); after a lost key file, sign in, touch the passkey and Wheelhouse writes the file back. Waits on Wheelhouse database backups |
| Ability to export audit checkpoints off the control host | feature | 2026-10-19 merge analysis: the hash chain is unkeyed, so a database administrator could rewrite it consistently; a periodic head off-host makes that visible |
| Drop the vault inventory rows, password files and the `Vault:Mode` fallback | cleanup | Once every prod store is imported and verified; a contract migration under decision 65 |
| Ability for a product to fetch secrets at startup with its own token | feature | Parked (decision 60): the deploy-time render is the model; revisit for rotation-heavy secrets or hosts Wheelhouse does not deploy |
| Ability to run a product locally with its dev settings and secrets | feature | User 2026-10-19: settings and secrets live only in Wheelhouse dev and prod, yet a local run needs them too; e.g. pull the dev values into the gitignored `appsettings.Machine.json` and user secrets, never into the repository |
| Ability to share one setting or secret value across products | feature | User 2026-10-17: products declare their own keys; Wheelhouse links keys that hold the same value and must change together to one row; otherwise rows stay separate |

---

## Deployments

| Item | Type | Notes |
|---|---|---|
| Ability to back up databases and volumes off-provider on a schedule | feature | |
| Ability to restore a backup and prove it works | feature | |
| Ability to drain requests and show maintenance before a rollout | feature | |
| Ability to dry-run pending migrations before a rollout | feature | |
| Ability for product frontends to refresh onto a new version | feature | |
| Ability to deploy when a service is idle, or at a set time | feature | |
| Ability to roll back or hotfix a failed target through the drain | feature | |
| Ability for auto-deploy and auto-promote to follow each target's deploy policy | feature | |
| Ability to dry-run migrations on a live database without long locks | feature | |
| Ability to pull private images with a short-lived token per deploy | feature | Decision 54; GHCR refuses the App's token for packages of private repositories, so those pull with the token file |
| Rehearse every deployment flow from dev on a sample app | feature | Promotion from dev to prod ships; this proves each flow end to end |
| Store deployment history in PostgreSQL with retention | engineering | v0.5 reads runner job files on each request (3 s cache) and filters in the API; SQL filtering and retention scale past a few hundred jobs |
| Point MCP `list_deployments` at the filtered history | engineering | It still reads `GET /api/deployments`; the v0.5 history endpoints take workspace, product, service, status and dates |
| Clear the history cache on submit and reconcile | fix | A new rollout can take up to 3 s to appear |
| Show the release in a deployment page's breadcrumb | fix | The trail reads "Deployment" because the route title doesn't reach `ScopeBar` |
| Cover the Deployments page with browser end-to-end tests | engineering | Grid, filters, list paging and the detail page have unit tests only |
| Ability to promote or roll back from Telegram alert buttons | feature | Parked 2026-10-19: behind step-up 2FA; the console is enough for now |
| CVE gate: scan images at build, warn on prod confirm, rebuild on base-image fixes | feature | Parked 2026-10-19 |
| Refresh dev's database from a redacted, compacted prod copy | feature | User 2026-10-19: no personal data reaches dev; feeds the end-to-end dev checks that gate auto-promote |
| Roll prod back by promoting the previous release | feature | Dev-to-prod promotion exists |
| Ability to see a product's deploys by hour and in other views in its details | feature | User 2026-10-19: not in the Deployments grid; product details only, after the Deployments, Products and Servers pages |
| Ability to run one environment on several servers for uptime | feature | User 2026-10-19, future: one aggregated state (decision 44) and a per-server overlay; design first |
| Back up Wheelhouse's own database and data volume on a schedule to at least two locations | feature | User 2026-10-19: not before Ocharo deploys; the control plane's audit, inventory and history need off-host copies. 2026-10-21: the volume holds runner state the database doesn't (deploy history, infrastructure job journals, SSH identities), so a restore needs both; the vault root key stays out of every backup |
| Ability to run a product on primary, secondary and tertiary servers with a backup-only storage | feature | User 2026-10-21 (decision 69): the model waits in w2 `deployment/servers/redundancy.md` for review; after the SDKs gain replication, point-in-time recovery, failover hooks and job ownership, Wheelhouse adds roles per product, replication in placement, a storage connection, coordinated Promote and recovery drills, and runs itself the same way; with v0.7 |
| v0.7 one-click infrastructure: move a product to another server, fleet moves, decommission with step-up 2FA | feature | User 2026-10-19 (decisions 56–57): backup → restore → write freeze ≤10 min → DNS switch → old host kept 7 days; design in the automation analysis |
| Provider APIs (Contabo, OVH) for snapshots, reinstall and ordering | feature | Parked 2026-10-19: no credential that can spend money until servers are bought often |
| Run CI on our own runners or a second provider when GitHub Actions degrades | feature | User 2026-10-19: Actions was degraded for about an hour and every run queued; self-hosted runners or an outside CI as fallback |
| Fetch secrets at runtime from the vault, Key Vault style | decision | User 2026-10-19: deploy-time render is the model for now (decision 60); revisit for rotation-heavy secrets and per-read audit |
| Ability to roll back automatically when the watch window sees a regression | feature | User 2026-10-19: later; the watch window flags the regression and the owner decides |
| Recover database migrations across services when a later service fails | decision | User 2026-10-19, for discussion: A migrates, then B's migration fails, so A's must roll back too. Either every migration stays rollback-safe, or the release updates A and B first and migrates the databases after |
| Show the maintenance page in Uzbek, Russian and English at least | feature | Parked 2026-10-19: per-product copy in the manifest, chosen by `Accept-Language` |
| Turn on `dotnetEnvironment` for ForeverPin, Haven, Ocharo Studio, Ocharo Desk and Secrets Vault | check | User 2026-10-19: later, when each product's chat works on it. Each keys behaviour on `Development` or holds localhost values; move those to `appsettings.Local.json` first |
| Products adopt the Target/Local settings names when they bump pipelines v0.7.0 and the be sdk | engineering | User 2026-10-19. A v0.7.0 bundle mounts `/app/appsettings.Target.json`; the be sdk's `AddApiDefaults` reads it, and a legacy `appsettings.Local.json` on a deployed host for one release window. Each drops its own `AddJsonFile("appsettings.Local.json")`, sets `LOCAL_SETTINGS=true` in its launch profile and ignores `appsettings.Target.json` in `.gitignore` and `.dockerignore`. ForeverPin (`ForeverPin.Api`, `ForeverPin.Redirect.Api`, README, `google-auth.md`); Haven (`Haven.Platform.Web` `EnvironmentAliasExtensions`, `compose.verification.yml` mount, development and verification docs); Secrets Vault (`SecretsVault.Api` `HostConfiguration`); home-atlas (ignore files only); Ocharo Studio and Desk (outside this workspace, unchecked); the product template (pre-rename kit `10.0.21-beta`, no settings loading) |
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
| Ability to connect a Cloudflare account with a scoped token and see its status | feature | `ocharo.co` proves the chain, public dev then prod (decisions 47–51) |
| Ability to sync a Cloudflare account's zones and choose from its domains | feature | |
| Ability to bind a product's sites to a domain per environment with derived hosts | feature | |
| Ability to plan, apply and drift-check a binding's DNS records | feature | |
| Ability to wire a products VPS with public ingress and DNS-01 certificates in one step | feature | |
| Ability to route sites by host and path, with priorities and `www` redirects | feature | |
| Ability to apply a binding change as a recorded routes deployment, without a release | feature | |
| Ability to unbind a host and remove only the records Wheelhouse owns | feature | |
| Ability to hand each service its site URLs and allowed hosts | feature | For bound domains; baseline hosts already reach each service |
| Ability to track domain, certificate and token expiry | feature | |
| Registrar search and purchase against a pre-funded balance | feature | Registrar choice open |

---

## Portfolio

| Item | Type | Notes |
|---|---|---|
| Ability to declare `dependsOn` on another repository's service | feature | The runner orders and checks it across a release set; the shared generator in `wow-two-platform.pipelines` refuses it until a pipelines tag |
| Move ForeverPin's and every other product repository's identity into its `deploy.yml` | feature | Decided 2026-10-27 (decision 7, format A): `name`, `description`, `homepage` in `deploy.yml` ([convention](../../../../../conventions/deployment/descriptor/deploy-descriptor.md#identity)), pipelines `v0.11.0`; Wheelhouse, the canary and the product template moved in v0.7; a repo with a `product/product.json` reads it with a notice, one with neither shows its row |
| Edit a product's identity from the console by committing its `deploy.yml` | feature | Needs Contents write on the GitHub App (it reads only); the console shows repository-sourced name, description and homepage read-only meanwhile |
| Refresh a product's identity on push | feature | A GitHub App push webhook feeds the same commit-keyed row; the 5-minute head check and the Refresh action stay as fallbacks |
| Add the icon set to ForeverPin and every other product repository | feature | Each product repo carries `product/brand/icons/` per the convention; until then Wheelhouse shows its web app favicon or a monogram |
| Ship Wheelhouse's monochrome icon variants | check | The approved artwork keeps a gray fold in every variant; a one-ink export needs a brand decision |
| Ownership and kill-gate metrics on the catalog | feature | Products are database rows since v0.3; metrics need cost and usage feeds |
| Second provider and a placement view | feature | Provider enum plus integration in code |
| Cost per product and host | feature | Feeds the micro-SaaS kill gates |
| Host view with capacity and a portfolio matrix | feature | Beside the per-environment service map |
| Teardown with a final backup and archive | feature | |
| Fix the console requesting icons a repository doesn't have | fix | Return no icon URL when the repo has none; the console now remembers each 404 for 12 hours |
| Zero-to-live scaffold from the product template | feature | Repository, CI, first deployment |
| Ability to compose a release set by hand, one build per repository | feature | Sets follow repository releases, and a commit build joins the other repositories' newest releases; picking a commit of two repositories at once needs a composer in the deploy dialog |

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

User 2026-10-15: layout, motion and notifications must work flawlessly and look their best.

| Item | Type | Notes |
|---|---|---|
| Ability to follow a deployment in a compact progress notification | feature | |
| Ability to see rollout steps and page transitions animate | feature | |
| Ability to swipe a table row on phones for its two main actions | feature | |
| Analyze full-page versus modal dialogs | product | Then record the dialog convention in wow2 |
| Ability to tour a page on a first visit | feature | User 2026-10-19: a guided tour ("storybook") that spotlights one section at a time under a blurred overlay; separate from info buttons, which always show context on click; may use different text. Name to settle (tour vs walkthrough) |
| Analyze Home widgets: add, remove and move them | product | User 2026-10-19: pinned products leave Home (the sidebar has them); phones have no sidebar, so decide what replaces pins there |

## SDK adoption

Leftovers of the v0.4 sweep and the [extraction inventory](../research/sdk-extraction-v0.6.md). `decision` rows wait on
the owner, top to bottom; rows marked *decision* wait on one of them.

| Item | Type | Notes |
|---|---|---|
| Decide the SDK's audit actor | decision | A string actor (`key:{name}`, `automation`, `cli`) or a widened `ICurrentUserService` |
| Decide whether the API-key store moves to the SDK | decision | `integration_keys` as a contract migration (decision 65), merging TranscriptForge's limits |
| Decide whether the versioned secret store stays Wheelhouse-only | decision | Proposed: keep; otherwise a neutral SDK module now |
| Decide whether GitHub App credentials extract with one consumer | decision | The extraction rule says yes, at size L; settle borrowed-token semantics |
| Decide the setting-type contract | decision | SDK C# checks plus golden cases in pipelines; Cronos or the hand parser for cron parity |
| Decide the platform theme | decision | Wheelhouse's stronger glass values; `AccountMenu` as a bottom sheet on phones |
| Decide how the log viewer follows the tail | decision | Always jump to the newest line, or follow only at the bottom with pause |
| Decide which other apps adopt the extracted blocks | decision | e.g. TranscriptForge for the process runner, decoder and MCP guard; each adopts in its own backlog |
| Extract the host-floor leftovers → be sdk | engineering | `/.well-known` reserved prefix, readiness apart from liveness, public `IsDeployed`, per-environment cookie name; then remove them from `HostConfiguration`, `LocalRun`, `SystemController`, `SessionCookies` |
| Extract the commit-keyed GitHub reader cache → be sdk | engineering | `GitHubRepositoryReader.cs`; then remove the `IRepositoryFileSource` and `IProductManifestSource` ports, which remap the SDK's `FileRead` |
| Extract opaque cursor paging → be sdk | engineering | SDK `PageTokenMapper` is internal; replaces `DeploymentCursor` and the audit list's `before`, a wire change with the Activity page |
| Extract slug, `owner/repo`, image, hostname, SSH and IPv4 rules → SDK validation vocabulary | engineering | `InventoryPatternConstants.cs`, `InventoryRuleExtensions.cs`, `InfraPatterns.cs`, `VaultRules.cs`; the backend half of the shared vocabulary; the slug regex differs from `Guards.NotSlug` |
| Extract the YAML reader with line-numbered refusals → be sdk | engineering | `YamlReader.cs` |
| Extract the status pill and tone variants → fe sdk badge | engineering | `StatusPill.vue`, `StatusStyles.ts`; the SDK badge lacks icon, detail and dashed tones; `StatusVocabulary.ts` maps stay |
| Extract the root-key broker, canary and HMAC fingerprints → be sdk | engineering | `FileVaultKeySource.cs`, `SecretsVaultCrypto.cs`, `SecretsVaultKeyCache.cs`; beside `ISealService`, whose broker reads only the environment |
| Extract the scripted GitHub handler and migration test helpers → be sdk testing | engineering | `FakeGitHubApi.cs`, `ExpandOnlyTests.cs`: method-keyed handler, fixture schema list, enumerated migrations for the expand-only lint |
| Replace `SecretValues` and `DeploymentRecordsMemo` with SDK `IIdGenerator` and `Caching/Hybrid` | engineering | `IIdGenerator` needs Hex and Base64Url alphabets first |
| Extract list-detail route state, persisted preferences and a stretched-link card → fe sdk | engineering | `useListDetailRoute.ts`, `application/preferences`, `ProductTile.vue`; SDK typed params take strings only |
| Extract typed setting values and the secret-name heuristic → be sdk | engineering | *decision*: `SettingTypes.cs`; the runner and pipelines repeat the same rules |
| Adopt the fe sdk glass, `AccountMenu` and menu link rows | engineering | *decision*: removes `--wh-glass-*`, `ProfileMenu.vue` and `ProfileSheet.vue` |
| Extract the audited-command trail → SDK | engineering | *decision*: marker, interceptor, EF chain store with lock, paging and verify; the SDK has the primitives only |
| Extract OAuth sign-in, me and sign-out endpoints → SDK | engineering | `IdentityController.cs`; registration only when configured |
| Extract an actor context → SDK | engineering | *decision*: session login or `key:{name}`; SDK `ICurrentUserService` is Guid-only |
| Extract GitHub App credentials → SDK | engineering | *decision*: `Infrastructure/GitHub/`: App token, per-owner installation tokens, the per-call credential handler; the SDK client takes one token for every owner |
| Replace the local `Stub*` clients with SDK testing fakes | engineering | Waits on the vault and repository-reader extracts shipping fakes; the SDK fakes today cover clients Wheelhouse doesn't call |
| Extract a selectable list → fe sdk | engineering | `NamespaceList.vue`, the one consumer left: leading, title, meta, trailing, skeleton rows |
| Extract the rows field → fe sdk | engineering | `RowsField.vue` over `useFieldArray`; ForeverPin's `RuleEditor` is the second consumer |
| Extract a log viewer that follows the tail → fe sdk | engineering | *decision*: `ServiceLogsModal.vue` |
| Make the Sparkline time-based with thresholds → fe sdk | engineering | `TrendLine.vue` |

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
| Prod Wheelhouse as the source of truth | The VPS instance owns settings and secrets; dev pulls from it and drops its own additions; secret flow prod → dev needs a security analysis first |
