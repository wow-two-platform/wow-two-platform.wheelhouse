# Wheelhouse — Frontend Development Guidelines

*Last updated: 2026-10-16*

Shared frontend conventions live in `wow-two-ws/conventions/development/frontend/`.
This document records Wheelhouse-specific choices; the workspace design is in
[architecture](../architecture/architecture.md#workspace).

## Stack

- Vue 3 Composition API · Vue Router · Vite 6 · TypeScript (strict) · Tailwind v4 · Node 24 · pnpm workspace.
- UI: published **`@wow-two-beta/ui-vue@0.0.16`**. Use its components, forms, queries and Result contracts. Shared capability gaps belong in the SDK; product composition stays here.
- `bootstrap/index.css` imports `tailwindcss` and `@wow-two-beta/ui-vue/styles.css`; its `@source` includes SDK utilities.
- `bootstrap/query.ts` and `bootstrap/form.ts` are the application adapter pins. Integration functions decode wire payloads with Zod and return SDK `Result<T, ApiFailure>` values.

## Conventions

- One app, `apps/web` (`@wheelhouse/web`), in the pnpm workspace at `engineering/codebase/wheelhouse.frontend-services/`. API client is same-origin (`/api/...`); HTTPS dev server `:5174` proxies to HTTPS backend `:8210` (see `apps/web/vite.config.ts`); `pnpm dev:http` serves plain HTTP for headless previews. Preserve secure cookies and forwarded origin during GitHub authentication.
- Production: `pnpm deploy` (`scripts/deploy.mjs`) builds the app and copies `apps/web/dist/` into the API's `wwwroot` (single-host serving). Idempotent — `wwwroot` is wiped + repopulated each run.
- `pnpm typecheck` runs `vue-tsc` and the SFC compiler gate. `pnpm test` checks inventory/selection, API decoding and sensitive-form lifecycles. `pnpm build` includes typechecking and lazy route bundling.

## Repo-specific deltas

- Control-plane workspace only — single operator, never public. Secret writes and token minting bypass the mutation cache. Plaintext stays inside the active dialog; abort/generation guards prevent dismissed requests restoring it. Failed writes preserve editable input; successful writes and context changes clear values and form baselines.
- The console's first load: `index.html` paints a static splash (`public/boot-splash.css`) that mounting replaces with `BootSplash`, the SDK `SplashScreen` overlay on the ambient surface; its bar reports scripts, session and first route (35, 70, 100). Keep the two in step.
- Each panel loads inside an fe sdk `DataRegion`: a first load renders the real layout over `useSkeletonItems` placeholders and every SDK component inside draws its own skeleton; explicit refresh uses the local `useRefresh` timing helper. A failed refresh keeps the last good content, and query failures reach the toast host.
- A page's primary action goes in the shared header through the SDK `PageHeaderActions`; the page owns the dialog.
- Colours come from the fe sdk `platform` theme (`theme-platform` on `<html>`, `surface-ambient` backdrop); `bootstrap/index.css` keeps only app tokens the theme lacks (charts, sidebar, glass, map). Vite generates `/theme.js` from the SDK `colorModeScript`, which applies the remembered scheme before the first paint.
- The Workspace route combines a product rail, environment/service surface and contextual inspector. Top navigation retains URL product/target selection. Invalid explicit selections remain unavailable rather than silently selecting another target.
- Registry/runner association uses explicit bindings in `application/workspace/WorkspaceInventory.ts`. Registry lifecycle, observed runtime condition and deployment outcome remain separate facts.
- Host vitals are current snapshots that the SDK query refetches every 60 seconds. Deployment follow-up refetches every three seconds while the tab is visible. Neither implies retained host history.
- Deployments submit approved target/release IDs. Reconciliation submits the authoritative remote active rollout ID and requires exact typed target confirmation. API action headers remain explicit.
- Clearing authentication invalidates the query session and HTTP request scope, preventing previous-session responses entering a replacement session.
