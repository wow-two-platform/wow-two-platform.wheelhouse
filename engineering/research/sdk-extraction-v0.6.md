# SDK extraction — v0.6 inventory

*Last updated: 2026-10-26*

What v0.5 (and earlier) built inline that is generic, where each block goes, and who else consumes it — the scope of
the v0.6 Adoption version (owner 2026-10-26: v0.5 closes, v0.6 extracts). Verdicts follow
[extract / keep / remove](../../../../../conventions/development/sdk-extraction.md); each extraction ships as the
[dev cycle](../../../../../conventions/development/dev-cycle.md)'s four parts: SDK code · SDK tests · convention · adoption,
Wheelhouse first.

Read-only analysis of the code on 2026-10-26: Wheelhouse pins be sdk `10.0.71-beta` (SDK head `10.0.78-beta`) and
fe sdk `0.0.30` (SDK head `0.0.32`). Sizes include SDK tests, doc and Wheelhouse adoption: S ≤ ½ day · M 1–2 days ·
L ≥ 3 days. Consumers come from a code search of every registry repo; planned apps come from `docs/platform-apps.md`.

**Verdict:** six unblocked, small blocks plus a re-pin fit one week; the audit trail and the API-key store are the
most valuable next pair, both gated on an owner decision.

Paths: backend relative to `wheelhouse.backend-services/` · `web/` = `wheelhouse.frontend-services/apps/web/src/` ·
`p/` = `web/presentation/` · `runner/` = `wheelhouse.runner-services/`.

## Inventory

| # | Block | Where in Wheelhouse | Target | Consumers | Size | Why |
|---|---|---|---|---|---|---|
| 1 | Periodic scoped job (interval, 0 = off, fresh scope, log and continue) | `Wheelhouse.Infrastructure/Operations/VitalsSampler.cs`, `Wheelhouse.Infrastructure/Deployments/{DeploymentOutcomeFollower,DeploymentAutomationWorker,FleetRolloutWorker,SettingsApplyWorker}.cs`, `Wheelhouse.Infrastructure/Infra/InfraJobFollower.cs` | be sdk `Jobs/Core` | Haven (5 workers), Ocharo Marketing (4), Config Checker, Podcast Readiness, File Watch, Procedure Review (retention and backup loops), Matinee, TranscriptForge | S | 6 near-copies here; the pilots copy the same loop; `Jobs/Core` is an empty folder |
| 2 | Bounded JSON process runner (timeout, kill tree, env allowlist, JSON stdout, failure line) | `Wheelhouse.Infrastructure/Deployments/DeploymentGateway.cs`, `Wheelhouse.Infrastructure/Infra/InfraGateway.cs`, `Wheelhouse.Infrastructure/Deployments/Parsers/RunnerFailureParser.cs` | be sdk (new area) | TranscriptForge (`CommandLineClient.cs`: yt-dlp, ffmpeg, no env scrub); Matinee and Sift once they shell out | M | 2 copies here; the failure-line parser becomes pluggable; which variables pass stays in the app |
| 3 | Schema decoder + problem-details reason | `web/integration/common/client.ts` (decoder, `failureReason`) | fe sdk `foundation/http` | ForeverPin, TranscriptForge, Sift, Config Checker, Podcast Readiness (`DecoderExtensions.ts`); `FailureMessages.ts` in 9 products | S | widest copy count in the portfolio; SDK `ApiDecoder` / `ProblemDetails` are partial |
| 4 | Atomic file write + EF upsert that survives a lost insert race | `Wheelhouse.Infrastructure/Inventory/InventorySnapshotExporter.cs`, `Wheelhouse.Infrastructure/GitHub/GitHubRunnerTokens.cs`, `Wheelhouse.Persistence/Repositories/EfProductManifests.cs` | be sdk `Storage` / `Data` | ListingShelf, TranscriptForge, Matinee, TNIS (atomic write); ForeverPin, Ocharo Marketing (upsert); the SDK's own `LocalFileBlobRepository` | S | 2 write copies here; the SDK blob store leaves a partial file on a crash |
| 5 | MCP request guard (Origin 403, 64 KiB cap) + tool error filter (`AppError` → retryable tool result) | `Wheelhouse.Api/Mcp/{McpRequestGuard,McpToolError,McpToolErrorFilter}.cs` | be sdk `Ai/Mcp` | TranscriptForge (bare `MapMcp`: no Origin check, no cap), Ocharo Desk (own Origin check, no cap), planned Events | S | spec-required check missing from `MapAuthenticatedMcp`; SDK backlog row exists |
| 6 | Audited-command trail + actor context | `Wheelhouse.Application/Audit/*`, `Wheelhouse.Application/Abstractions/{IAuditTrail,IActorContext}.cs`, `Wheelhouse.Api/Auth/HttpActorContext.cs`, `Wheelhouse.Persistence/Repositories/EfAuditTrail.cs`, `Wheelhouse.Persistence/Audit/AuditEntryCanonicalizer.cs` | be sdk `Mediator` + `Foundation/Audit` | planned Identity and Audit Log apps; Retainer Balance, Training Seats, Procedure Review, Vendor Renewals (unchained `AuditEntity`); Ocharo Desk approvals | M | SDK ships hash-chain primitives only; 49 commands carry the marker; `AuditProductScope.cs` stays — Point 1 |
| 7 | API-key store, management and expiry | `Wheelhouse.Domain/Integrations/*`, `Wheelhouse.Application/Integrations/*`, `Wheelhouse.Persistence/Repositories/IntegrationKeysRepository.cs`, `Wheelhouse.Api/Controllers/IntegrationKeysController.cs` | be sdk `Identity/ApiKeys` | TranscriptForge (same entity plus limits), File Watch, Config Checker, Documentation Checker, Podcast Readiness (hand-rolled tokens); planned Events, Harvest, Feedbacks | M | SDK covers authentication only; store and CRUD repeat 5× — Point 2 |
| 8 | Typed confirmation (type the target ID) | `p/deployments/components/DeployModal.vue`, `ReconcileModal.vue`, `automation/FleetPromoteModal.vue`, `p/settings/components/ApplyBar.vue` | fe sdk `overlays` | ForeverPin (delete code), TranscriptForge (key revoke), Ocharo Desk (publish), ListingShelf | S | generic atom, 4 copies; the typed value stays exposed for the API's `confirm` |
| 9 | Pieces the SDKs already ship | `web/bootstrap/index.css` (`--wh-glass-*`), `p/shell/components/{ProfileMenu,ProfileSheet}.vue`, `Wheelhouse.Application/Abstractions/ITransactions.cs`, `Wheelhouse.Persistence/Repositories/EfTransactions.cs`, `Wheelhouse.Application/Inventory/Interceptors/InventoryExportingInterceptor.cs` | remove: fe `0.0.31`–`0.0.32` glass, `AccountMenu`, menu link rows; be `Data/Sessions` `OnCommittedAsync` | Wheelhouse only | S–M | app copies of shipped SDK pieces — Point 6 for glass values and the phone sheet |
| 10 | Host floor leftovers | `Wheelhouse.Api/Configurations/{HostConfiguration,LocalRun}.cs`, `Wheelhouse.Api/Controllers/SystemController.cs`, `Wheelhouse.Infrastructure/Settings/SessionCookies.cs` | be sdk `Web` / `Meta`, then remove | every product on `AddApiDefaults` | S | `/.well-known` reserved prefix, readiness apart from liveness, public `IsDeployed`, per-environment cookie name; SDK backlog row |
| 11 | Commit-keyed GitHub reader cache; app-local file-source ports | `Wheelhouse.Infrastructure/Products/GitHubRepositoryReader.cs`; ports `Wheelhouse.Infrastructure/Catalog/GitHubRepositoryFileSource.cs`, `Wheelhouse.Infrastructure/Products/GitHubProductManifestSource.cs`, `Wheelhouse.Application/Abstractions/{IRepositoryFileSource,IProductManifestSource}.cs` | be sdk `Integrations/GitHub`; remove the ports | TranscriptForge (`GitHubReleaseClient.cs` → SDK `IGitHubClient`) | S | the ports remap the SDK's `FileRead` |
| 12 | Opaque cursor paging | `Wheelhouse.Application/Deployments/History/Services/DeploymentCursor.cs`, `before` in `Wheelhouse.Application/Audit/AuditListQuery*.cs` | be sdk `Data` paging (public token mapper, in-memory pages), then remove | every paged list; SDK backlog row | S | SDK `PageTokenMapper` is `internal` |
| 13 | Backend validation vocabulary (slug, `owner/repo`, image, hostname, SSH, IPv4) | `Wheelhouse.Application/Inventory/Constants/InventoryPatternConstants.cs`, `Wheelhouse.Application/Inventory/Extensions/InventoryRuleExtensions.cs`, `Wheelhouse.Application/InfraJobs/InfraPatterns.cs`, `Wheelhouse.Api/Requests/VaultRules.cs` | be sdk `Foundation/Validation` | Ocharo Desk (same slug regex, 2 validators); ForeverPin custom domains (planned) | S–M | backend half of the shared vocabulary; its slug regex differs from `Guards.NotSlug` |
| 14 | YAML reader with line-numbered refusals | `Wheelhouse.Application/Catalog/Parsers/YamlReader.cs` | be sdk | one-manifest readers; SDK backlog row | S | — |
| 15 | Typed setting values + secret-name heuristic | `Wheelhouse.Application/Deployments/Settings/Services/SettingTypes.cs` | be sdk (C# checks) + conventions (descriptor contract, shared golden cases) | `runner/runner.py` and pipelines `generator/release.py` (same rules); ~20 products declaring `deploy.yml` settings | S | one contract written in 3 languages — Point 5 |
| 16 | Status pill + tone variants | `p/common/components/StatusPill.vue`, `StatusStyles.ts` | fe sdk `display/badge`; keep `domain/common/StatusVocabulary.ts` maps | TranscriptForge, Haven admin, ListingShelf (`PipelineRow.vue`) | M | SDK badge lacks icon, detail and dashed tones |
| 17 | Panel header + hover info button | `p/common/components/{Panel,InfoPopover,InfoPopoverContent}.vue` | fe sdk `display/card` header slots + info popover; remove `Panel.vue` | every product with titled panels | M | 19 + 17 uses here; SDK backlog rows |
| 18 | Popover on desktop, sheet on phones + `useIsPhone` | `p/deployments/hooks/useIsPhone.ts` and 5 call sites | fe sdk | every phone layout | M | SDK backlog row |
| 19 | Refresh button | `p/common/components/RefreshButton.vue` | fe sdk actions, then remove | every list page | S | 16 uses; wrapper over `Button isLoading` + `useRefresh` |
| 20 | Rows field | `p/common/components/RowsField.vue` | fe sdk `forms` | ForeverPin (`RuleEditor` on `useFieldArray`) | M | no phone layout yet, despite the backlog note |
| 21 | Envelope crypto extras: file root-key broker, canary, HMAC fingerprints | `Wheelhouse.Infrastructure/SecretsVault/FileVaultKeySource.cs`, `Wheelhouse.Application/SecretsVault/Services/{SecretsVaultCrypto,SecretsVaultKeyCache}.cs` | be sdk `Foundation/Security` | planned Identity (signing keys), Harvest, AI Gateway | S–M | sits beside `ISealService`, whose broker is env-only |
| 22 | Test helpers | `Wheelhouse.Tests.Unit/Fakes/FakeGitHubApi.cs`, `Wheelhouse.Tests.E2E/Harness/Stub{RepositoryFileSource,ProductManifestSource,ProductIconSource}.cs`, `Wheelhouse.Tests.Migrations/Tests/ExpandOnlyTests.cs` | be sdk `Testing` / `Testing.Integrations` / `Testing.Data`, then remove | ~20 products on the migrator (expand-only lint, fixture schemas) | S | method-keyed scripted handler, fixture schema list, enumerated migrations |
| 23 | Small duplicates | `Wheelhouse.Application/Deployments/Settings/Services/SecretValues.cs`, `Wheelhouse.Infrastructure/Deployments/DeploymentRecordsMemo.cs` | remove: SDK `IIdGenerator` (+ Hex, Base64Url alphabets), `Caching/Hybrid` | — | S | — |
| 24 | Display extensions | `p/servers/components/{TrendLine,ResourceMeter}.vue`, `p/products/components/{WeeklyBars,ChoiceChips}.vue`, `p/infrastructure/components/PolicyDiffView.vue` | fe sdk `Sparkline` (time axis, thresholds, series), `MeterBar` unavailable, chip variant, `DiffViewer` hunks; then remove | ForeverPin (`CodeAnalyticsPage.vue` sparkline) | M | each extends an SDK primitive |
| 25 | Run-step list + `useNow` | `p/deployments/components/DeploymentSteps.vue`, `p/infrastructure/components/JobSteps.vue`, `CheckResultList.vue` | fe sdk | none found yet | M | SDK `progressSteps` / `timeline` lack detail and durations |
| 26 | Log viewer that follows the tail | `p/deployments/components/ServiceLogsModal.vue` (core only) | fe sdk | planned Logs, Feedbacks, Monitoring | M | Point 7 |
| 27 | List-detail route state, persisted preferences, stretched-link card | `p/common/hooks/useListDetailRoute.ts`, `web/application/preferences/index.ts`, `p/products/components/ProductTile.vue` and rows | fe sdk `router`, `foundation/storage`, `Card` | every list-detail product | S–M | SDK typed params take strings only |
| 28 | Selectable list with skeleton rows | `p/secrets/components/NamespaceList.vue` | fe sdk | Ocharo Desk, ListingShelf, TranscriptForge | M | one consumer left here |
| 29 | Activity heatmap, tree multi-select, date-time range, scope trail | `p/deployments/components/history/{ActivityGrid,EntityPicker,DateRangeFilter}.vue`, `p/shell/components/ScopeBar.vue` | fe sdk: extend `heatmapCalendar`, `treeSelectPicker`, `dateRangePicker`, `breadcrumb` | weak (day charts in ForeverPin, TranscriptForge) | L | SDK backlog rows; the range picker waits on the SDK's Temporal-or-`Date` decision |
| 30 | GitHub App credentials (App JWT, per-owner installation tokens, credential handler) | `Wheelhouse.Infrastructure/GitHub/*` (runner token files stay) | be sdk `Integrations/GitHub` | none outside Wheelhouse | L | generic, one consumer — Point 4 |
| 31 | Versioned encrypted secret store | `Wheelhouse.Application/SecretsVault/Services/SecretsVaultService.cs`, `Wheelhouse.Persistence/Repositories/SecretsVaultStore.cs`, `Wheelhouse.Domain/SecretsVault/*` | keep; be sdk only on Point 3 | none (secrets-vault inactive, merged here) | L | product scoping, render and import are domain |
| 32 | Standalone-vault client and import | `Wheelhouse.Infrastructure/Vaults/*`, `Wheelhouse.Application/Vaults/*`, `Wheelhouse.Api/Controllers/VaultsController.cs`, `Wheelhouse.Application/SecretsVault/Services/SecretsVaultImporter.cs` | remove | — | M | ~1.6k lines; after the prod import (Iteration 23), a contract migration under decision 65 |
| 33 | Deploy domain | catalog import parsers, settings render and derivation, job journals, `SelfDeploymentInterceptor`, `ReleaseLabel`, `EnvironmentPill`, `ServiceMap` (on SDK `CanvasArea`), the deploy dialog flow, vault Generate | keep | — | — | app-bound logic |
| 34 | Runner Python: pinned-key SSH, locks, job journals, Tailscale client, GHCR manifest probe | `runner/{transport,enroll,infra_jobs,tailnet,registry}.py` | keep; merge the 2 SSH classes and one lock helper inside the runner | no second Python consumer | — | the SDKs are .NET and TypeScript |

## Rejected or already covered

- Read-only derived fields → SDK `Field isReadOnly`, adopted.
- Themed scrollbars → SDK since `0.0.26`; no local CSS.
- Skeletons, toasts, session-expiry sign-out → SDK `DataRegion`, `FeedbackToastHost`, `createAuthBridge`, adopted.
- Tag input → Wheelhouse has none; Ocharo Catalogue's copy should adopt SDK `tagsInput`.
- Action header → SDK `AddActionHeaderGuard`, adopted.
- Bespoke migrator → no workarounds; only the lint and migration list in row 22.
- Registry and manifest probing → runner Python only (row 34).

## Backlog drift

- § SDK adoption names `ProductsPanel` and `WorkspacePage`; neither exists any more.
- "RowsField stacks on phones" is not implemented.
- Rows 6–7 merge with the backlog's audit checkpoint export and key-expiry items.
- Rows 9–16, 21–23 and 27 are not in the backlog yet.

## Points

Decide top to bottom.

1. [ ] Audit actor: a string actor (`key:{name}`, `automation`, `cli`) in the SDK, or a widened `ICurrentUserService`.
2. [ ] API-key store: move `integration_keys` to the SDK as a contract migration (decision 65), merging TranscriptForge's limits.
3. [ ] Secret store: keep the versioned vault Wheelhouse-only (proposed), or build a neutral SDK module now.
4. [ ] GitHub App credentials: extract with one consumer (the extraction rule says yes), or defer as L; borrowed-token semantics.
5. [ ] Setting-type contract: SDK C# checks plus golden cases in pipelines; Cronos or the hand parser for cron parity.
6. [ ] Platform theme: adopt Wheelhouse's stronger glass values; `AccountMenu` as a bottom sheet on phones.
7. [ ] Log viewer: always jump to the newest line, or follow only at the bottom with pause.
8. [ ] Other apps adopting in v0.6 beside Wheelhouse — e.g. TranscriptForge for rows 2, 3 and 5.

## Proposed v0.6 cut

About five days, every block ungated:

1. Re-pin be sdk `10.0.78-beta` and fe sdk `0.0.32`; remove the shipped copies (row 9) — 1 day.
2. be sdk: periodic scoped job (1), atomic write + upsert (4), MCP guard + tool error filter (5) — 1½ days.
3. be sdk: bounded process runner (2) — 1½ days.
4. fe sdk: schema decoder + problem reason (3), typed confirmation (8) — 1 day.

Left for the backlog: rows 6–7 first once Points 1–2 settle, then 10–28 in rank order; 29–32 wait on their Points or
triggers.
