# Platform ideation — v0.5

*Last updated: 2026-10-21*

Settled decisions from the v0.5 platform brainstorm: what the platform is, how products are modelled and who owns each
fact. Iteration happens in the conversation; this file records only what the owner settled. The v0.5 version doc is
written from it when the version opens.

## Decisions

| # | Decision | Settled |
|---|---|---|
| 1 | One console shell for every platform app: each app ships an API and a module inside Wheelhouse; one sign-in, one navigation, one audit trail | 2026-10-17 |
| 2 | Hierarchy: Workspace → Product → Environment → Service, with shared platform services (PostgreSQL, Valkey) beneath | 2026-10-17 |
| 3 | Platform apps are ordinary products in a Platform workspace; Wheelhouse itself stays CI-promoted | 2026-10-17 |
| 4 | Environments are `dev` and `prod`: products work on `main` only and fix forward, every `main` release deploys to dev, then is promoted to prod; `test` and its branch are dropped; branch previews come later | 2026-10-17 |
| 5 | Pins: workspaces, products and services can be pinned and custom-sorted; a pinned service is one pin for all its environments | 2026-10-17 |
| 6 | The repo declares what a product is; Wheelhouse decides where and when it runs | 2026-10-17 |
| 7 | One manifest per repo replaces `product/product.json` and `engineering/deployment/deploy.yml` | 2026-10-17 |
| 8 | Fact ownership below | 2026-10-17 |
| 9 | Non-secret settings and their defaults are declared in the manifest; secret values live only in Secrets Vault | 2026-10-17 |
| 10 | Non-secret defaults live in the repo's `appsettings.json` plus one `appsettings.{Environment}.json` per environment, named the .NET way (`Development`, `Production`); overrides come from environment values or the secret store | 2026-10-17 |
| 11 | Each service declares its required settings; creating a product loads them into the config store and its secrets into the vault, empty | 2026-10-17 |
| 12 | A settings editor offers declared choices (enum values, numeric sets) and flags missing values per service and product | 2026-10-17 |
| 13 | Products and services carry a binary status, active or inactive; inactive ones stay hidden until a toggle shows them | 2026-10-17 |
| 14 | One `catalog.yml` in wow-two-ws lists every wow2 app (platform, backbone, apps); `repo-registry.md` is generated from it | 2026-10-17 |
| 15 | One repo per product for now; products spanning several repos (components) wait in the backlog. Superseded by decision 70 | 2026-10-17 |
| 16 | Secrets Vault keeps its name; the console's two editors are labelled Settings and Secrets | 2026-10-17 |
| 17 | Ocharo gets its own `ocharo` workspace in the catalog | 2026-10-17 |
| 18 | Prod skips v0.4 and takes v0.5 after the owner's review | 2026-10-17 |
| 19 | Two environments, `dev` and `prod`; products work on `main` only, every release goes to dev, then is promoted to prod | 2026-10-17 |
| 20 | Deployed dev runs as .NET `Development`, prod as `Production` | 2026-10-17 |
| 21 | Primary navigation is Home, Products, Deployments, Servers; secrets and activity belong to products | 2026-10-17 |
| 22 | Before Ocharo goes live: settings and secrets editor, public hosting, backups, logs, monitoring and alerts, one manifest, zero-disruption deploys | 2026-10-17 |
| 23 | Products page: workspaces on top, products grouped by workspace below; one Cards/Rows switch for both, cards only on phones; search, filter and sort (name by default) | 2026-10-18 |
| 24 | Environment pills: outline, status icon, env name and version or state; details on hover; "no target" and "nothing deployed" are one state | 2026-10-18 |
| 25 | Pins are products only and sit below the sidebar's main menu after a divider; on a product page, Products unfolds to Workspace › Product as a trail and remembers the last product; the sidebar has no workspace tree | 2026-10-19 |
| 26 | Promotion lives only on the Deployments page; the open-site menu lists every front-facing service with dev and prod | 2026-10-18 |
| 27 | Explanatory copy (legends, hints) sits behind an info button with a one-time first-run intro | 2026-10-18 |
| 28 | Every release auto-deploys to dev by default (opt-out per product); freeze, rollback-risk badges, prod deploy reasons, pre-deploy snapshots, changelogs from GitHub release notes, failed-build Issues, retention cleanup, watch window with opt-in auto-promote | 2026-10-18 |
| 29 | Info buttons always show context on click, dimmed and revealed on hover; a first-visit tour is a separate future feature | 2026-10-19 |
| 30 | Each hub (wow2, ventures, Ocharo) keeps its own `catalog.yml`; Wheelhouse reads all of them | 2026-10-19 |
| 31 | An expired session signs out once and returns to the same page after sign-in; no error toasts | 2026-10-19 |
| 32 | GitHub access goes through one GitHub App installed on every owner; a token file stays the fallback | 2026-10-19 |
| 33 | Deploys carry eight states, coloured by the attention they need rather than by state | 2026-10-19 |
| 34 | Deploys that changed nothing (refused, cancelled, expired) sit behind one toggle, off by default, that hides or shows them in both the records and the stats | 2026-10-19 |
| 35 | On phones a deploy's service map becomes an impact list with an "Open map" action | 2026-10-19 |
| 36 | Services declare dependencies as `dependsOn` in `deploy.yml`; rollouts follow that order and roll back release-wide | 2026-10-19 |
| 37 | A service restarts only when its own image or settings change; a dependency's update never restarts it | 2026-10-19 |
| 38 | Only Wheelhouse deploys through CI; Wheelhouse deploys every other product; catalog `ci`/`none` entries never create products | 2026-10-19 |
| 39 | Automatic rollback on a watch-window regression waits; the watch window flags the regression | 2026-10-19 |
| 40 | Zero-disruption phase 1 also takes an emergency path through the drain, automation that honours each target's deploy policy, and dry runs bounded by lock and statement timeouts | 2026-10-19 |
| 41 | A deployed target's mounted settings file is `appsettings.Target.json`; a machine's own overrides are `appsettings.Local.json`, replacing `appsettings.Machine.json` | 2026-10-19 |
| 42 | The grid, day rows and list show each product environment's latest deploy of a day; one toggle in the top filter bar shows every attempt and unchanged deploys; the stats follow the toggle; the status filter is dropped | 2026-10-19 |
| 43 | A product's deploy history and its releases share one name and tab: Deployments | 2026-10-19 |
| 44 | An environment on several servers shows one state: succeeded when all succeed, failed when all fail, degraded otherwise; an overlay lists each server | 2026-10-19 |
| 45 | Each service can be stopped, started or restarted from the console now; settings and secrets changes restart their services now or at a set time | 2026-10-19 |
| 46 | One build job per release in the shared pipeline; a product splits into per-service jobs only when its build regularly passes 10 minutes | 2026-10-19 |
| 47 | A product's manifest declares the public paths each service serves; Wheelhouse picks domains from a connected provider (Cloudflare first) and applies DNS and routing per environment | 2026-10-19 |
| 48 | Withdrawn 2026-10-19: no console switch; the owner keeps both links | 2026-10-19 |
| 49 | A product's apex domain (`ocharo.co`) serves its marketing page and `www` redirects to it; each other product gets its own host (`studio.ocharo.co`) with its API under `/api` | 2026-10-19 |
| 50 | Product sites are public behind the Cloudflare proxy; only SSH and Wheelhouse's control path stay on the tailnet; service-to-service APIs stay on the server's internal network | 2026-10-19 |
| 51 | Dev uses each product's own domain (`studio-dev.ocharo.co`); no shared preview domain | 2026-10-19 |
| 52 | Wheelhouse drives Tailscale through an owner-created OAuth client: tagged keys, devices and the access policy after a reviewed diff | 2026-10-19 |
| 53 | A fresh server enrolls from Wheelhouse with a one-time key, root-only images included; the hand-run kit stays the fallback | 2026-10-19 |
| 54 | Private images pull with a short-lived token per deploy; products hosts hold no registry token | 2026-10-19 |
| 55 | Wheelhouse renders settings with secret values at deploy; a first deploy asks for missing required values | 2026-10-19 |
| 56 | A one-click move goes through backups, freezes writes 10 minutes at most and keeps the old host 7 days | 2026-10-19 |
| 57 | v0.6 extracts code into the SDKs; one-click infrastructure (move job, fleet moves, decommission) is v0.7 | 2026-10-19 |
| 58 | Builds spend no money: CI stays inside the free Actions quotas and is trimmed before anything is paid | 2026-10-19 |
| 59 | Secrets Vault merges into Wheelhouse as a built-in module with its own schema; the standalone repo goes inactive | 2026-10-19 |
| 60 | Products get secrets only from the deploy-time render; an edit applies by re-render and restart, now or at a set time | 2026-10-19 |
| 61 | Secrets keep versions and an optional expiry with warnings; each product has its own master key; key files are root-only on the control host with an offline copy | 2026-10-19 |
| 62 | Products declare their setting and secret keys with types (text, number, boolean, choice, link, cron, date format); owners may add more keys in the console | 2026-10-19 |
| 63 | Dev Wheelhouse keeps its own vault and key with test values only | 2026-10-19 |
| 64 | Wheelhouse deploys itself: CI only builds, prod deploys each release to dev, and prod promotes itself after a typed confirm; supersedes decision 3's CI promotion | 2026-10-19 |
| 65 | Wheelhouse migrations only expand; a contract change takes two releases, so any release can roll back | 2026-10-19 |
| 66 | Step-up 2FA ships before Ocharo's public launch and covers every prod promotion | 2026-10-19 |
| 67 | The vault's root key has two recoveries: the owner's offline copy, checked against the key in use on a schedule, and later a passkey-sealed copy in the database | 2026-10-20 |
| 68 | A deployed site whose product has no domain gets a baseline public host on its server's IP through sslip.io: `<product>-dev.<ip>.sslip.io` in dev, `<product>.<ip>.sslip.io` in prod, a second site adding `-<site>` after the product; Wheelhouse fills it and a bound domain replaces it. It is served from the server's own ingress on 80 and 443 with an HTTP-01 certificate, not through Cloudflare; SSH and the control path stay on the tailnet. Narrows decision 50's Cloudflare proxy to sites with a bound domain | 2026-10-20 |
| 69 | An environment runs every service of its product on one server; services never split across servers. Uptime comes from up to three servers per product (primary, secondary, tertiary) plus a backup-only storage, proposed in w2 `deployment/servers/redundancy.md` for review | 2026-10-21 |
| 70 | A product may span several repositories: Ocharo is one brand and one domain, and Studio depends on Desk's sign-in tickets, so `ocharo` is one product of Platform, Studio, Marketing and Assets. A product has one set of environments, one placement, one secrets space and one redundancy setup (decision 69); every service of every repo runs in each environment on its one server, and every deployable service deploys. A product release is a set of repository releases: each repo keeps its CI, `vX.Y.Z` tags and version track, Wheelhouse composes the set; only a changed service restarts (37), and `dependsOn` and release-wide rollback (36) span the repos | 2026-10-21 |

| Fact | Owner |
|---|---|
| Name, description, homepage | Repo manifest |
| Services and image repositories | Repo manifest only; the console stops asking for images |
| Release asset and workflow | Derived from the pipelines convention; no console field |
| Settings: keys, non-secret defaults, which keys are secret | Repo manifest |
| Secret values | Secrets Vault |
| Site names | Repo manifest; hosts come from a bound domain, else the server's baseline host (decision 68) |
| Needs (PostgreSQL, Valkey, broker) and volumes | Repo manifest; shown and provisioned per environment |
| Placement, lifecycle, approvals | Wheelhouse database |

## Open topics

- Manifest fields and format.
- The Deployments page and everything connected to deploying.
- The deployment contract (`con-t-012` in the wow2 tasks).
