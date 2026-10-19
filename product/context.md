# Wheelhouse — Context

*Last updated: 2026-10-19*

## Current state

Wheelhouse is the private infrastructure-governance control plane. Its essential slice is deploying a published
ForeverPin release to a reviewed VPS target, with durable outcomes and recovery independent of the dashboard.
The .NET/Vue application uses PostgreSQL, GitHub authentication and an explicit production owner allowlist.
Products, a read-only fleet, release-artifact selection and deployment operations are implemented locally.
Operators see deployment history, check a target read-only, reconcile a locked target and administer secrets vaults.
The studio workspace selects a product/environment, shows observed services and release state, and opens contextual details.
Its portfolio attention list combines host/container vitals, 30-day deployment metrics and vault hygiene.
Workspace, Deployments, Servers, Secrets, Products and Activity share top navigation and coordinated light/dark themes.
Every product runs `dev` and `prod` on one host; dev takes any commit's build, prod only a release that passed dev.
Each rollout records its steps, requests its sites through the ingress and removes images old releases alone used.
Every operator action lands in a hash-chained audit trail. CI tests every push; Wheelhouse releases through the shared pipeline.
The local server (SSH target, Traefik, vault) exercises all of it end to end at `*.localhost:18080`.
Since 2026-10-13 Wheelhouse dev runs on the OVH control-plane VPS, reachable only inside the operator's tailnet
over Tailscale Serve HTTPS. Every `main` push tests, publishes and deploys it; the first run
([38422336103](https://github.com/wow-two-platform/wow-two-platform.wheelhouse/actions/runs/38422336103)) verified
the rollout, trusted HTTPS and anonymous MCP rejection. The public address answers only key-based SSH.
Since 2026-10-14 Wheelhouse prod runs beside it on the same host, on the private HTTPS port 443, with its own
database and sign-in; dev moved to port 8443. CI promoted `v0.3.7` to prod after the owner's approval
([38523785166](https://github.com/wow-two-platform/wow-two-platform.wheelhouse/actions/runs/38523785166)).
Restart persistence, authenticated MCP and off-provider backups remain open.

The essential milestone closes when a published ForeverPin release runs on a reviewed VPS with a working public
redirect and editor, real provider callbacks, persisted codes and cookie keys, and a verified backup.

## Decisions

- GitHub Actions builds and publishes artifacts; Wheelhouse only starts a product's build for a commit that has none.
- Every product builds through the shared publish workflow; a push to `main` releases `vX.Y.Z`.
- The inventory — products, servers, environments, vaults — lives in the database and is edited in the console;
  provider/environment values use enums; no dynamic integration registry. Decided 2026-10-02.
- SSH identities, pinned host keys and vault passwords stay files on the control host, never database rows.
- Runtime secrets are mounted separately. Product artifacts remain identical across environments.
- Image rollback requires schema compatibility; database recovery is an explicit operation.
- Secrets Vault stays a separate service; Wheelhouse is its central console and never reads values back.
- Domain, cost, backup and broader operations governance remain in scope for later slices.
- All environments of a product share one host; Compose projects, networks, databases and hostnames separate them.
- Products describe services, builds and sites in one `deploy.yml`; a service keeps the version it last changed in.
- The audit trail records actions, never values; its chain shows edits, not truncation of the newest entries.
- Wheelhouse stays private: a tailnet with Tailscale Serve HTTPS, no public domain or port. Decided 2026-10-09.
- The control-plane VPS hosts only platform apps; products get a separate VPS. Decided 2026-10-09.
- Wheelhouse prod is the one console for every product environment; dev and prod never share a database.
  Wheelhouse dev is its test instance and deploys only a sample app. Decided 2026-10-14.
- Prod answers on the private HTTPS port 443, dev on 8443. The sample app is `wow-two-platform.wheelhouse-canary`.
  Decided 2026-10-14. CI promoted Wheelhouse then; since 2026-10-19 Wheelhouse deploys itself: CI only builds, prod
  deploys each release to dev and promotes itself after a typed confirm, a host CLI covers a dead console, and public
  SSH closes after a week's trial (decisions 64–66).
- The canary lives permanently on the control-plane VPS beside both consoles, so the platform and its test app are
  always ready. Decided 2026-10-14.
- Only Wheelhouse prod deploys products, in every environment; dev never holds the products host's key. A flow a
  product needs tested is first added to the canary and exercised from dev. The canary stays tailnet-only; the
  control-plane host opens no public port. Decided 2026-10-15.
- Each canary environment gets its own tailnet name through a small Tailscale container (`canary-dev`, `canary`),
  each with its own trusted certificate; no path prefixes. Decided 2026-10-15; `canary-test` went with `test`.
- Products work on `main` only and fix forward, with no long-lived branches: every `main` release deploys to dev, then
  is promoted to prod. The `test` environment and the `test` branch are dropped everywhere. Decided 2026-10-17.
- A push that publishes no new release verifies the running dev release instead of restarting it.
- A product's name, description, homepage and per-service text are a manifest in its own repository
  (`product/product.json`); the database keeps a cache keyed by the commit it was read from, replaced only by a newer
  commit, refreshed on read (head re-checked at most every 5 minutes) and by an explicit Refresh. The console shows
  manifest values read-only; editing them from the console waits for the GitHub App. Decided 2026-10-15.

Contracts and boundaries: [architecture](../engineering/architecture/architecture.md). Unbuilt work and open
decisions: [backlog](../engineering/planning/backlog.md).
