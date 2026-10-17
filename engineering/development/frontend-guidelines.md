# Wheelhouse — Frontend Development Guidelines

*Last updated: 2026-10-17*

Shared frontend conventions live in `wow-two-ws/conventions/development/frontend/`.
This document records Wheelhouse-specific choices; the workspace design is in
[architecture](../architecture/architecture.md#workspace).

## Stack

- Vue 3 Composition API · Vue Router · Vite 6 · TypeScript (strict) · Tailwind v4 · Node 24 · pnpm workspace.
- UI: the published fe sdk **`@wow-two-beta/fe@0.0.19`**. Use its components, forms, queries and Result contracts. Shared capability gaps belong in the SDK; product composition stays here.
- `bootstrap/index.css` imports `tailwindcss` and `@wow-two-beta/fe/styles.css`; its `@source` includes SDK utilities.
- `bootstrap/query.ts` and `bootstrap/form.ts` are the application adapter pins. Integration functions decode wire payloads with Zod and return SDK `Result<T, ApiFailure>` values.

## Conventions

- One app, `apps/web` (`@wheelhouse/web`), in the pnpm workspace at `engineering/codebase/wheelhouse.frontend-services/`. API client is same-origin (`/api/...`); HTTPS dev server `:5174` proxies to HTTPS backend `:8210` (see `apps/web/vite.config.ts`); `pnpm dev:http` serves plain HTTP for headless previews. Preserve secure cookies and forwarded origin during GitHub authentication.
- Production: `pnpm deploy` (`scripts/deploy.mjs`) builds the app and copies `apps/web/dist/` into the API's `wwwroot` (single-host serving). Idempotent — `wwwroot` is wiped + repopulated each run.
- `pnpm typecheck` runs `vue-tsc` and the SFC compiler gate. `pnpm test` (`apps/web/tests/console/`) checks routing and redirects, product selection, the status filter, pin ordering, catalog selection, API decoding and sensitive-form lifecycles. `pnpm build` includes typechecking and lazy route bundling.

## Repo-specific deltas

- Control-plane workspace only — single operator, never public. Secret writes and token minting bypass the mutation cache. Plaintext stays inside the active dialog; abort/generation guards prevent dismissed requests restoring it. Failed writes preserve editable input; successful writes and context changes clear values and form baselines.
- The console's first load: `index.html` paints a static splash (`public/boot-splash.css`) that mounting replaces with `BootSplash`, the SDK `SplashScreen` overlay on the ambient surface; its bar reports scripts, session and first route (35, 70, 100). Keep the two in step.
- Each panel loads inside an fe sdk `DataRegion`: a first load renders the real layout over `useSkeletonItems` placeholders and every SDK component inside draws its own skeleton; explicit refresh uses the local `useRefresh` timing helper. A failed refresh keeps the last good content, and query failures reach the toast host.
- A page's primary action goes in the shared header through the SDK `PageHeaderActions`; the page owns the dialog.
- Colours come from the fe sdk `platform` theme (`theme-platform` on `<html>`, `surface-ambient` backdrop); `bootstrap/index.css` keeps only app tokens the theme lacks (charts, sidebar, glass, map). Vite generates `/theme.js` from the SDK `colorModeScript`, which applies the remembered scheme before the first paint.
- A **workspace** is a group of products. Routes: `/` Home (attention, pins, every workspace's product cards), `/w/:workspace` a workspace's products, `/p/:product?env=dev|test|prod&inspect=…&item=…` a product page; tool pages `/deployments`, `/servers`, `/secrets`, `/activity`, `/products` (registry forms and workspace management), `/settings/keys`, `/settings/catalog`. An old Workspace link `/?product=x&target=…` redirects to `/p/x?target=…`, and the product page rewrites the target as its `env`.
- Frame (`bootstrap/AppLayout.vue` on the SDK `AppShell`): desktop (≥1024px) a 248px sidebar (`presentation/shell/ConsoleNav` on the SDK `SidebarMenu`) that collapses to a 64px icon rail, remembered in `localStorage`; tablet the rail, with the full sidebar in a left `Drawer`; phone (<768px) no sidebar and the SDK `BottomNavMenu` in the shell's footer row (Home, Products sheet, Deployments, Servers, More sheet), so the scrolling region ends above it. The top bar (`ScopeBar`) shows the scope trail (Home › Workspace › Product › Service; phones keep the last two steps) and holds the page's `PageHeaderActions`; the page heading is the current crumb, for assistive technology.
- The product page carries one product: header (workspace chip, pin, Active switch, repository), then environment surface, comparison, services map/list and recent deployments, with the inspector beside them from `xl`. The environment segmented control sits in the top bar. Invalid explicit selections remain unavailable rather than silently selecting another target; without `env`, the environment last chosen on any product page applies when the product has it.
- Products and services are active or inactive; inactive ones stay hidden everywhere until the one "Show inactive" switch (sidebar footer, phone More sheet) is on. Pins (workspaces, products, services) list in custom order in the sidebar and on Home; a pinned service opens in the environment last chosen. Status, pin and order writes are optimistic (`useOptimisticMutation`) and roll back on refusal.
- View choices live in `localStorage` through `application/preferences` (`wheelhouse.show-inactive`, `wheelhouse.sidebar-collapsed`, `wheelhouse.environment`), every access wrapped so blocked storage only loses the choice.
- Registry/runner association uses explicit bindings in `application/product/ProductInventory.ts`. Product status, observed runtime condition and deployment outcome remain separate facts; per-environment health on product cards comes from `domain/overview/EnvironmentHealth.ts` over the targets, vitals, stats and history the console already reads.
- Host vitals are current snapshots that the SDK query refetches every 60 seconds; each server's host history comes from the backend sampler, which keeps samples for its retention window. Deployment follow-up refetches every three seconds while the tab is visible.
- Deployments submit approved target/release IDs. Reconciliation submits the authoritative remote active rollout ID and requires exact typed target confirmation. API action headers remain explicit.
- Clearing authentication invalidates the query session and HTTP request scope, preventing previous-session responses entering a replacement session.
