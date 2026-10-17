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

- A single wow2 catalog that a new Wheelhouse instance loads to create every app.
- Products that span several repositories, and how their services group.
- Console layout and navigation on desktop and phone, including pins.
- Manifest fields and format.
- The deployment contract (`con-t-012` in the wow2 tasks).
