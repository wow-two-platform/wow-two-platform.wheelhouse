# Wheelhouse architecture

*Last updated: 2026-10-17*

## Runtime

A .NET 10 host serves the private administration API and Vue workspace.
PostgreSQL stores the inventory (products, servers, targets, vaults), integration keys, the audit trail and vitals;
bespoke SQL migrations run on startup. GitHub cookie authentication and an owner allowlist protect administration; a scoped integration key reads
the product catalog and nothing else. Production requires a nonempty owner allowlist.

```mermaid
flowchart LR
  CI[Shared publish workflow] --> Images[Immutable GHCR images]
  CI --> Bundle[Release manifest + Compose]
  Bundle --> Release[GitHub release asset or bundle artifact]
  Release --> Dock[Private Wheelhouse dashboard/API]
  Inventory[(PostgreSQL inventory)] --> Dock
  Dock --> Snapshot[Inventory snapshot file]
  Snapshot --> SSH
  Dock --> SSH[Pinned OpenSSH adapter]
  Operator[Operator CLI] --> SSH
  SSH --> Runner[Target-owned Python runner]
  Runner --> Compose[Docker Compose services]
  Images --> Compose
  Runner --> Journal[Durable target deployment journal]
  Journal --> Dock
```

## Responsibilities

| Layer | Responsibility |
|---|---|
| Domain | Product, server, target, vault, deployment, domain and secret models |
| Application | Inventory, workspace, status, integration key, audit, vault and deployment use cases |
| Infrastructure | SDK integration clients, the inventory snapshot exporter and the bounded runner-process adapter |
| Persistence | PostgreSQL EF mapping, repositories and bespoke migration files |
| API | Host wiring, authorization, request validation, controllers and SPA serving |
| Python runner | Inventory snapshot reads, release discovery, bundle validation, SSH, rollout, checks and recovery |
| Vault gateway | Administers inventory vaults over their management API; values are write-only |

Wheelhouse keeps business logic only: domain meaning, inventory and deployment use cases, version-resolution policy,
store predicates and settings values. Host wiring, data, web contracts, identity, integration clients, UI building
blocks and the test harness come from the wow-two SDKs; a new generic piece proves inline, then extracts in the next
Adoption version. snake_case is database-only (column names and stored enum values); the JSON wire stays camelCase.
Time comes from the SDK `TimeProvider`.

The placeholder deployment, domain and secret tables remain unused.
The deployment execution journal lives on each target, with a durable local submission index.

## Release contract

A release bundle holds `release.json` (schema version, product, release label, full source SHA, CPU platform, service
image digests, Compose SHA-256, required setting names, sites and whether its schema permits the previous images) and
`compose.json` (product-owned topology, named volumes, limits and health checks). Neither holds credentials,
environment-specific domains or database values; a target supplies its settings, so promotion changes settings, not
image bytes. The shared publish workflow builds bundles from each product's `deploy.yml`
([descriptor convention](../../../../../conventions/deployment/descriptor/deploy-descriptor.md)).

Bundles are privileged operator inputs: hashes bind files together, not publishers to identities.
The API accepts target and release IDs only. It cannot upload arbitrary Compose, run shell commands or disclose SSH keys.

## Execution model

1. Validate the manifest, digests, Compose hash and environment identity; refuse a commit build on test or prod.
2. Take the target lock that dashboard and operator share; refuse an unresolved interrupted rollout.
3. Check Docker, Compose, required settings, free disk and release platform.
4. Pull every image before replacing any running container.
5. Save durable intent and the previous successful bundle on the target.
6. Apply Compose, wait for every declared health check, then request each site through the ingress.
7. Record the outcome with release, source commit, actor and previous release.
8. On failure, keep the attempted release; restore previous images only under a declared schema-compatibility
   guarantee. Never restore a database or delete volumes automatically.

An SSH disconnect or control-plane restart does not stop the detached target worker; unknown or interrupted state
needs explicit reconciliation, not a blind retry. SSH uses strict host-key checking against a pinned known-hosts file,
batch authentication, explicit identities and a bounded timeout. Compose replacement has a restart window; there is
no zero-downtime promise.

## Artifacts and registry

A product's release source — repository, archive name, build workflow and service-to-image mappings — is part of its
inventory row; `artifacts.py` approves only those. The release catalog lists published releases with a complete, checksummed asset and per-commit bundle artifacts;
drafts and incomplete assets never appear. Discovery covers the 100 newest releases per source; deployed bundles
stay in each target's journal. Selection downloads and validates the archive, its checksum, source commit and approved
image names before any target changes. Wheelhouse dispatches a product's build workflow only for a commit that has
no build; it never pushes, tags or builds on a host.

Images live in GitHub Container Registry, one repository per service; the digest is the deployment identity.
Keep every deployed digest and declared rollback dependency, and at least the 10 newest releases; candidate
bundle artifacts expire after 14 days. Deleting a release image needs a cross-host reference inventory first.
A read-only GitHub token (`Deployment:GitHubTokenFile` / `WHEELHOUSE_GITHUB_TOKEN_FILE`) goes only to the GitHub API
and is stripped from cross-origin redirects. Release rules for every product:
[deploy descriptor](../../../../../conventions/deployment/descriptor/deploy-descriptor.md) § *Builds and versions*.

## Inventory

PostgreSQL is the source of truth for products, servers, targets (one product environment on one server) and vaults.
The console edits them; every change is an audited command. The API exports the four tables to
`<runner root>/inventory.json` at startup and after each change — atomically, owner-only, one write at a time — and
the runner reads only that snapshot, refusing a malformed one whole. The operator CLI keeps working from the last
snapshot while the API is down.

Credentials never enter the database. A server's SSH identity and pinned host key (`ssh/<server>/identity`,
`known_hosts`) and a vault's administrator password (`vaults/<vault>/password`) are files the operator places on
the control host: a new row reaches nothing until its files exist, and a changed host address fails its pinned key.
Provider and environment values stay enums; a new provider needs an enum member and its integration. A product or
server that a target or vault still uses cannot be deleted. Product images never change with the host.

On the local rig the runner holds the local server's fixtures. The API seeds them at startup: fixture products only
where the database lacks them, the local server, its targets and vault rewritten on every start, since their hosts,
ports and paths depend on where Wheelhouse runs. Fixtures never apply outside the rig. Every product runs `dev`, `test` and `prod` on one host, separated by Compose project, network alias,
database, settings files and hostnames. Moving a stateful product needs a backup, a write freeze, a verified restore
and an explicit cutover; a second host definition does not make a service redundant.

## Product catalog

A product is one inventory row: slug, name, description, repository, default branch, an optional release source, its
workspace and its status. The slug never changes once created; the runner
refuses a target or release source for a product the snapshot lacks. While the repository carries a valid manifest,
its name, description and homepage replace the row's ([product identity](#product-identity)).

`/api/products` joins the inventory and the product identity: each product's environments come from its targets, each environment's sites from
the newest rollout this control plane saw succeed (no target is contacted), and its vault namespace is
`{product}-{environment}` on its server's first vault. The response carries no targets, releases or deployment state,
so an integration reads products without deployment concepts. The runner read is cached for 30 seconds.

### Workspaces

A workspace groups products, such as the platform apps or one's own ventures (`workspaces`: id, unique slug, name,
optional description, `sort_order`, audit timestamps). Every product belongs to exactly one: `products.workspace_id`
is required and restricts deletes, so a workspace holding a product cannot be removed (409). Migration
`010-workspaces` created the default workspace `own-ventures` ("Own ventures") first in the order and moved every
existing product into it. A product created without a workspace joins `own-ventures`, which comes back last in the
order if it was removed.

- `GET /api/workspaces` lists `{ id, slug, name, description, order }` in the operator's order; the operator's
  session or a `catalog:read` key reads it, since every product names its `workspaceId`.
- `POST /api/workspaces` `{ slug, name, description? }` adds one last; `PUT /api/workspaces/{id}` `{ name,
  description? }` renames it (the slug stays); `DELETE /api/workspaces/{id}` removes an empty one;
  `PUT /api/workspaces/order` `{ ids }` reorders, and the list must name every workspace exactly once (409 otherwise).
- `PUT /api/products/{product}/workspace` `{ workspaceId }` moves a product; `{product}` is the product's id or slug.
- Every write carries the action header `workspace` and is audited (`workspace.create`, `workspace.update`,
  `workspace.delete`, `workspace.reorder`, `product.workspace`). Workspaces are not part of the runner's snapshot.

### Status

A product and each of its services are `active` or `inactive`. Inactive is a display filter only: the console hides
inactive products and services until the operator asks to see them, while deploys, reads and integrations keep
working. `products.status` replaced the five-value lifecycle in `011-product-status` (paused and killed became
inactive, idea, building and live active). A service's status is a `service_statuses` row keyed by (`product_id`,
service key in `deploy.yml`); a service without a row is active, one status covers every environment, and the rows go
with the product. The key is taken as given, since the repository declares a product's services.

- `PUT /api/products/{product}/status` `{ status }` and `PUT /api/products/{product}/services/{service}/status`
  `{ status }`; `{product}` is the id or the slug. Both carry the action header `status` and are audited
  (`product.status`, `service.status` with subject `{product}/{service}`). A new product starts active.
- `ProductDto` carries `id`, `workspaceId`, `status` and `inactiveServices` (sorted keys); `/api/products` and the MCP
  `list_projects` tool share it.

### Pins

An operator pins workspaces, products and services to keep them one click away, in a custom order (`pins`: id, owner
= the signed-in GitHub login, kind `workspace` | `product` | `service`, `workspace_id`, `product_id`, `service`,
`sort_order`, `created_at_utc`). A pin names exactly the target its kind takes (a check constraint); a service pin is
(`product_id`, service key) with no environment, so one pin covers every environment. An owner pins a target once
(unique index with `NULLS NOT DISTINCT`, PostgreSQL 15+), and a pin goes with its workspace or product (cascade).
Pins are a preference: they never reach the audit trail and are not inventory.

- `GET /api/pins` lists the operator's own `{ id, kind, workspaceId, productId, service, order }` in order.
- `POST /api/pins` `{ kind, workspaceId?, productId?, service? }` adds a pin last (201) or answers the existing pin
  on that target (200); `productId` takes the product's id (its slug also works). An unknown target is 400.
- `DELETE /api/pins/{id}` removes one (another operator's reads as 404); `PUT /api/pins/order` `{ ids }` reorders,
  and the list must name every one of the operator's pins exactly once (409 otherwise).
- Operators' sessions only (`OperatorSession` policy): an integration key is authenticated, then refused with 403;
  writes carry the action header `pin`.

### wow2 catalog

Wheelhouse reads one YAML catalog of every wow2 app from GitHub: `Catalog:Repository` (default `wow-two/wow-two-ws`),
`Catalog:Path` (`catalog.yml`) and `Catalog:Ref` (`main`), validated options, read with the signed-in operator's token
through `GitHubRepositoryReader` (`IRepositoryFileSource`). Each read checks the ref's head again, so a pushed catalog
shows at once; a commit's file stays cached. The format (`catalog: 1`, `workspaces`, `entries`) is owned by
wow-two-ws ([catalog convention](../../../../../conventions/development/repo/catalog/catalog.md)); Wheelhouse reads
an entry's slug, name, workspace, kind, deploy, purpose and repositories, and ignores `orgs`, `status`, `local` and any
key it does not know. The parser (YamlDotNet's representation model) names the line and the field of the first
problem.

- `GET /api/catalog` returns `{ source: { repository, path, ref, commit }, workspaces: [{ slug, name, description }],
  entries: [{ slug, name, workspace, kind, deploy, purpose, repositories, productId }] }`; `productId` is the product
  with the entry's slug, else `null`. 503 when GitHub cannot serve the repository, 404 when the file is missing, 422
  (`catalog.yml line N: field …`) when it is malformed. Operator's session only.
- `POST /api/catalog/import` `{ slugs }` (action header `catalog`, audited as `catalog.import`) walks the chosen
  entries in catalog order: an existing product with the slug moves into the entry's workspace; an entry with
  `deploy: wheelhouse` and a repository becomes a product; anything else is skipped with a warning (an entry with
  `repositories: []` is not importable). A workspace is created from its catalog declaration, last in the order, only
  when a product needs it. It answers `{ createdWorkspaces, createdProducts, movedProducts, warnings: [{ slug,
  message }] }` (slugs); a second run changes nothing. An unknown slug is 400 and nothing changes.
- A product created from the catalog needs no typed release config: name and description (clipped to 200) from the
  entry, the primary (first) repository, branch `main`, asset `{slug}-release.tar.gz`, workflow
  `publish-docker-image.yml`, and one image per service key of `engineering/deployment/deploy.yml` at `main`'s head:
  the service's own `image`, else `ghcr.io/{owner/repo, lowercase}/{service}`, the shared publish workflow's
  convention (`release.py` `repository_of`). Without a readable, valid descriptor the product gets no images and the
  warning says why; a descriptor naming another `product` keeps its images with a warning.

### Product identity

A product's name, description, homepage and per-service name and description are text in `product/product.json` in its
repository (the [product manifest convention](../../../../../conventions/design/identity/product-manifest.md); service
keys are the `services:` keys of `deploy.yml`). The database holds only a projection: one `product_manifests` row per
product with the manifest values, the repository and branch, and the commit they were read from. A save never writes
the manifest; the repository is where identity is edited.

- A row is replaced only by a copy read at a newer commit. Nothing expires it on a timer.
- On a read, `ProductIdentityService` compares the branch head with the row's commit; a different head reads the file
  at that commit. The head comes from `GitHubRepositoryReader`, which keeps it 5 minutes, so GitHub is asked at most
  that often per branch; a commit's file verdict is kept per commit.
- `POST /api/products/{slug}/identity/refresh` (action header `refresh`, audited as `product.refresh`) reads the head
  again now and answers with the product; it answers 503 when GitHub cannot serve the branch, and the row stays.
- A newer commit with no manifest drops the row; the product reads as its own row again. A row also goes when the
  product's repository or branch changes, and with the product.
- An invalid, oversized or unparsable manifest is ignored whole: the previous row stays and `identity.problem` carries
  one operator-safe reason, never file content beyond a shortened key name. An unreadable repository changes nothing.
- `/api/products` and the `list_projects` tool return the effective name, description and homepage plus
  `identity { source, commit, problem, services }`; `source` is `manifest` or `record`.
- While a manifest governs, a save leaves the row's own name and description as they are, and the console shows name,
  description and homepage read-only with a note that they are edited in the repository. A push webhook from a future
  GitHub App and writes from Wheelhouse feed the same commit-keyed row.

### Product icons

Icons are repository files, never rows: the
[product icons convention](../../../../../conventions/design/identity/product-icons.md) fixes their paths
(`product/brand/icons/{form}-{color}-{theme}.{svg|png}`, and `…/services/{service}/…` per service) and the fallback
order. `GET /api/products/{slug}/icon` and `GET /api/products/{slug}/services/{service}/icon` take `theme`
(`light`|`dark`), `form` (`full`|`short`) and `color` (`colored`|`monochrome`), default `light`, `short`, `colored`.
`ProductIconPaths` computes the candidate paths; the host reads the branch's newest commit (re-read every 5 minutes),
lists that commit's tree once (a commit never changes, so its listing and files stay cached until the branch moves),
keeps only the files an icon can come from — the icon and manifest sources share `GitHubRepositoryReader`'s head and
per-commit caches — picks the first candidate that exists, then the web
app's `favicon-dark`/`favicon`, and answers not found when nothing resolves, so the console shows its monogram. The
console asks for the theme of its active color mode and asks again when the mode changes. A file above 256 KiB is not
read, and every response is a download with `nosniff`, so a repository's SVG never runs on the console's origin.

## Integrations and agents

Another program — an app, a script, Claude or Codex — reads Wheelhouse with an integration key: the SDK `ApiKey`
scheme with the `wh_` marker. Only the secret's SHA-256 and a display prefix are stored; the secret is shown once;
a key can be revoked and shows its last use. A key grants scopes, `catalog:read` or `deployments:read`, and reaches only endpoints
whose policy names the key scheme; every other endpoint stays cookie-only through the default-deny fallback, and
key management is the operator's alone. A key's actions audit as `key:{name}`.

The private `/mcp` endpoint uses the SDK's stateless Streamable HTTP host (`Ai/Mcp`). Every request authenticates
with a Bearer integration key; browser cookies are excluded. Before authentication, a request whose `Origin` is
neither the console's own nor in `Mcp:AllowedOrigins` gets 403, a request without one (a CLI client) passes, and a body
over 64 KiB gets 413. Every `/.well-known/*` path answers 404 rather than the SPA shell, so an OAuth discovery probe
learns that the endpoint takes keys only. Tool discovery and invocation both enforce scopes.
`catalog:read` lists projects; `deployments:read` lists servers, targets and deployment history and reads target
state, readiness, vitals and control-plane health. Tools call existing mediator queries. A scope has one projection
for REST and MCP alike: `list_projects` returns the `ProductDto` list `/api/products` returns, vault namespaces
included, and never a secret value; the fleet tools' projections omit credential and settings paths. The
[MCP runbook](../deployment/mcp.md) owns connection details. No initial tool writes, builds, deploys, reconciles, reads
logs or reveals secrets.

A failed tool call is an MCP tool error (`isError`) whose text is `{ code, message, retryable }`: `code` is the SDK
error kind REST puts in a problem's `code`, `message` the operator-safe text REST returns as `detail`, and `retryable`
is true only for transient kinds, a runner timeout (`OperationTimeout`) or an unreachable runner or SSH host
(`ExternalUnavailable`). Any other exception is logged and answers its kind's generic message, so exception text never
reaches an agent. A refused scope stays a JSON-RPC error.

Future mutation tools require explicit scopes and the same handler/runner gates as the console: prod takes only a
release that passed test, and bypassing that gate requires its typed target ID. They remain backlog work.

## Deployment outcome observation

The target journal owns a rollout's state. The control plane keeps durable submissions in `jobs/` and their latest
observations in `observed/`; history and statistics read that projection. A cancellable hosted follower asks the
existing process gateway for bounded observation passes. It scans all submissions, persists retry timing in
`following/`, and resumes after restart without launching work. Browser status reads share a nonblocking per-job
lock; competing reads get the last complete observation. Unique atomic temporary files prevent concurrent writers
from sharing a temporary pathname. Explicit reconciliation receipts take precedence over earlier observations.

A terminal status without `completedAt` is still being finalized: the worker may be rolling back or cleaning up.
The follower stops observing only completed outcomes. Explicit status reads can refresh a completed failure after
direct target-side acknowledgement. Transport failures and unknown receipts do not become rollout failures or
automatic retries. Missing receipts still require operator recovery. This projection does not introduce deployment
database ownership or mutation MCP tools.

## Secrets vaults

Secrets Vault stays a separate service and repository; Wheelhouse is its central management console.
The gateway resolves a vault id through the inventory, signs in with a mounted administrator credential,
and forwards namespace, secret, state and token operations. It never requests secret values.
Products keep reading secrets from their own vault on the private network, so an unavailable Wheelhouse
cannot interrupt runtime reads. See the vault's own security analysis for the global-console trust boundary.

## Workspace

The product rail selects the object being operated; the centre shows its environment, verified release, observed
services, resource readings and recent deployments; a contextual inspector keeps the selected target, service or
deployment. Workspace, Deployments, Servers, Secrets, Products and Activity share the top bar, primary-action
placement, account menu and theme choice. Frontend invariants:
[frontend guidelines](../development/frontend-guidelines.md#repo-specific-deltas).

The service map draws the validated Compose definition of the target's last successful release. It never runs
Compose interpolation or returns raw configuration: environment values, labels, commands, credentials and bind-mount
paths stay out. Runtime readings are a separate, timestamped overlay; an unknown observation stays unavailable,
never healthy or zero. `depends_on` shows declared startup order and shared networks show configuration, neither
an observed call. Health attaches only when the runtime and target-state readings name the same release, and the
runner withholds attribution when a rollout changes that state mid-read.

## Security and recovery

Wheelhouse stays private through a tunnel or private network.
Runtime secrets live in protected host files, mounted read-only into applications; cookie key volumes survive
container replacement. The dashboard receives safe failure categories; raw runtime logs stay on the target.
A database backup and the cookie and recovery keys must be recoverable without Wheelhouse.

Executable commands, directory layouts and the VPS wiring checklist are in
[deployment operations](../deployment/deployment.md).
