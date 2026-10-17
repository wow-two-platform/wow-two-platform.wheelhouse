# Platform ideation — v0.5

*Last updated: 2026-10-17*

Settled decisions from the v0.5 platform brainstorm: what the platform is, how products are modelled and who owns each
fact. Iteration happens in the conversation; this file records only what the owner settled. The v0.5 version doc is
written from it when the version opens.

## Decisions

| # | Decision | Settled |
|---|---|---|
| 1 | One console shell for every platform app: each app ships an API and a module inside Wheelhouse; one sign-in, one navigation, one audit trail | 2026-10-17 |
| 2 | Hierarchy: Workspace → Product → Environment → Service, with shared platform services (PostgreSQL, Valkey) beneath | 2026-10-17 |
| 3 | Platform apps are ordinary products in a Platform workspace; Wheelhouse itself stays CI-promoted | 2026-10-17 |
| 4 | Environments stay `dev`, `test`, `prod`; branch previews come later | 2026-10-17 |
| 5 | Pins: workspaces, products and services can be pinned and custom-sorted; a pinned service is one pin for all its environments | 2026-10-17 |
| 6 | The repo declares what a product is; Wheelhouse decides where and when it runs | 2026-10-17 |
| 7 | One manifest per repo replaces `product/product.json` and `engineering/deployment/deploy.yml` | 2026-10-17 |
| 8 | Fact ownership below | 2026-10-17 |
| 9 | Non-secret settings and their defaults are declared in the manifest; secret values live only in Secrets Vault | 2026-10-17 |
| 10 | Non-secret defaults live in the repo's `appsettings.json` plus one `appsettings.{env}.json` per environment; overrides come from environment values or the secret store | 2026-10-17 |
| 11 | Each service declares its required settings; creating a product loads them into the config store and its secrets into the vault, empty | 2026-10-17 |
| 12 | A settings editor offers declared choices (enum values, numeric sets) and flags missing values per service and product | 2026-10-17 |
| 13 | Products and services carry a binary status, active or inactive; inactive ones stay hidden until a toggle shows them | 2026-10-17 |
| 14 | One `catalog.yml` in wow-two-ws lists every wow2 app (platform, backbone, apps); `repo-registry.md` is generated from it | 2026-10-17 |
| 15 | One repo per product for now; products spanning several repos (components) wait in the backlog | 2026-10-17 |

| Fact | Owner |
|---|---|
| Name, description, homepage | Repo manifest |
| Services and image repositories | Repo manifest only; the console stops asking for images |
| Release asset and workflow | Derived from the pipelines convention; no console field |
| Settings: keys, non-secret defaults, which keys are secret | Repo manifest |
| Secret values | Secrets Vault |
| Site names | Repo manifest; hosts come from the server's pattern |
| Needs (PostgreSQL, Valkey, broker) and volumes | Repo manifest; shown and provisioned per environment |
| Placement, lifecycle, approvals | Wheelhouse database |

## Open topics

- Manifest fields and format.
- Environment names in `appsettings.{env}.json` versus .NET's `Development` / `Production`.
- Whether Secrets Vault is renamed Secrets Store beside a config store.
- The deployment contract (`con-t-012` in the wow2 tasks).
