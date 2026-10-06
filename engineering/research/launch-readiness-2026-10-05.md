# Wheelhouse launch readiness — 2026-10-05

*Last updated: 2026-10-06 — Asia/Samarkand*

Private Wheelhouse hosting today appears feasible, subject to account provisioning, production authentication,
an immutable release containing current work, and live acceptance. Local startup and adopted MCP authorization tests are verified. Production hosting
and an authenticated external MCP client are not verified. Public ForeverPin launch has additional product and recovery gates.

This is a dated readiness analysis. The [backlog](../planning/backlog.md) and
[v0.3 track](../planning/version-track/v0.3/v0.3.md) remain the capability and acceptance records.

## Verified baseline — October 5

| Surface | Observed result | Boundary |
|---|---|---|
| Native console | [HTTPS frontend](https://localhost:5174) serves successfully | Browser workflow acceptance remains open |
| Native API | [HTTPS API](https://localhost:8210) and readiness return `200` | Readiness proves database connectivity, not a complete deployment |
| Authentication | Protected anonymous request returns `401`; sign-in redirects to GitHub with `302` | Real callback, owner session and secure-cookie round trip remain unverified |
| Infrastructure | Nine infrastructure containers running under managed Compose | Existing container health is not a fresh release rollout |
| Native runtimes | API watcher rebuilt with OVH support and published SDK `10.0.63-beta`; Vite remains running | Codex owns these runtimes and their logs; MCP is not active |
| Packaged console | [Local production image](http://localhost:18210) healthy | OAuth client ID and client secret are empty; sign-in needs separate setup |
| SSH target | `wheelhouse-dev` transport readiness passes `10/10` checks | Persisted deployment state is September 29 history, not today's rollout |
| Runner tests | `166/166` pass | Tests exercise runner logic and local probe sockets; no production target |
| Backend E2E | `129/129` pass, zero skips | Existing binaries, source timestamps checked; test authentication and gateway stubs |
| Frontend tests | `78/78` pass after OVH support | Root session ran `pnpm test`; not browser acceptance |
| Normal backend solution | Build succeeded with zero warnings and errors | Published SDK `10.0.63-beta`; OVH support included, MCP adoption deferred |
| Idle container memory | Approximately `805 MiB` across local containers on ARM | Excludes a production load/sizing proof and cannot establish x86 capacity |

The runner tests were repeated with native sandbox escalation to allow temporary loopback sockets.
The completed E2E command used `--no-build --no-restore --property:SkipSpaBuild=true -m:1` against an isolated
Testcontainers PostgreSQL. No production deployment or purchase occurred during this baseline.

Evidence limits are explicit in the harness:
[test authentication](../codebase/wheelhouse.backend-services/Wheelhouse.Tests.E2E/Harness/TestAuth.cs#L25)
replaces real cookie/OAuth policies;
[gateway substitutions](../codebase/wheelhouse.backend-services/Wheelhouse.Tests.E2E/Harness/WheelhouseAppFixture.cs#L79)
replace the runner, vault and icon integrations. The API's
[readiness route](../codebase/wheelhouse.backend-services/Wheelhouse.Api/Controllers/SystemController.cs#L14)
only checks database connectivity.

---

## Release baseline — October 5

- GitHub `main` was `02ac3df5fbc3720fd93147841af514e84320b648`.
- Local `HEAD` was `3e1e9de13e7755e3f6a07fcf19b259f9e90933ef`, eight commits ahead.
- Those commits add database inventory, editable inventory forms and current SDK adoption.
- The baseline published release was [v0.3.4](https://github.com/wow-two-platform/wow-two-platform.wheelhouse/releases/tag/v0.3.4),
  published September 30 from `02ac3df`; its uploaded bundle has checksum metadata.
- Its console image is `ghcr.io/wow-two-platform/wow-two-platform.wheelhouse/console` on `linux/amd64`.
  An anonymous GHCR request for that repository's pull token returned `401` on October 5.
  The deployment target needs verified registry read credentials or an explicitly approved package visibility change.
- [CI](https://github.com/wow-two-platform/wow-two-platform.wheelhouse/actions/runs/36740873516) and
  [publication](https://github.com/wow-two-platform/wow-two-platform.wheelhouse/actions/runs/36740874860)
  succeeded for that older commit. This does not validate unpublished inventory or MCP changes.
- The repository remains public. Its visibility was verified through the GitHub API.

Publish and verify the intended source before selecting a production release. The
[descriptor](../deployment/deploy.yml#L6) publishes `linux/amd64`; the ARM rehearsal does not prove the
published x86 image deploys successfully.

Changing repository visibility needs care: [release preparation](../codebase/wheelhouse.runner-services/artifacts.py#L236)
downloads the `github.com` browser asset URL, while
[token attachment](../codebase/wheelhouse.runner-services/artifacts.py#L66) covers only `api.github.com`.
Private versioned release downloads remain unimplemented. Runtime inventory now lives in PostgreSQL;
the older rationale about real hosts living in `fleet.py` no longer describes current inventory ownership.

---

## Launch blockers and bounded work

### Private host and authentication

- Select and provision an x86 host; configure private administration and persistent storage.
- Prepare Docker/Compose, Python, PostgreSQL, protected runner files, pinned SSH and private ingress.
- Register the production GitHub OAuth callback for the actual private HTTPS hostname.
- Configure the owner allowlist and verify the real login/callback/session flow.
- Verify registry pull access, the mounted GitHub credential and the actual chosen release.
- Preserve the database, cookie keys, runner inventory, job records and recovery credentials across replacement.

The [production allowlist guard](../codebase/wheelhouse.backend-services/Wheelhouse.Api/Auth/AuthConfigurationExtensions.cs#L44)
refuses an empty owner list. [Sign-in](../codebase/wheelhouse.backend-services/Wheelhouse.Api/Controllers/IdentityController.cs#L23)
returns `503` without configured OAuth credentials. Host preparation and backup automation are backlog work;
the existing [VPS checklist](../deployment/deployment.md#vps-wiring-checklist) supports a reviewed manual bootstrap.

### MCP

Wheelhouse now adopts published SDK `10.0.64-beta` and exposes stateless Streamable HTTP at `/mcp`.
[SDK publication](https://github.com/wow-two-sdk-beta/wow-two-sdk.backend.beta/actions/runs/37442381839)
succeeded after the user pushed `b62a3c6` and `20c80a5`; all seven public NuGet downloads were verified.
The SDK checkout fast-forwarded to the clean release commit `a9b2a27`, tagged `v10.0.64-beta`.

Bearer integration keys authenticate every MCP request. `catalog:read` lists projects; `deployments:read`
lists servers, targets and deployment history and reads target state, readiness, vitals and health.
Tool discovery and invocation both enforce scopes; revocation applies to the next request. Cookies cannot
authenticate MCP. The slice excludes writes, builds, deploys, reconciliation, logs and secret access.
The [MCP runbook](../deployment/mcp.md) contains endpoint and operator-issued key instructions.

The normal checkout built successfully with the published dependency and production SPA: zero warnings,
zero errors. All four backend suites passed: 136 E2E, 20 integration, 3 migrations and 64 unit tests
(223 total; zero skips). The MCP tests exercise real scoped keys and PostgreSQL, with the existing test
OAuth and runner substitutions. Frontend tests passed 78/78. Build and test evidence is retained in
`/private/tmp/wheelhouse-combined-build-20261006.log`, `/private/tmp/wheelhouse-combined-tests-20261006.log`
and `/private/tmp/wheelhouse-combined-test-results-20261006`.

The rebuilt native API is running. Frontend, frontend-proxied readiness and direct API readiness return
`200` over trusted HTTPS. An anonymous MCP initialization POST returns `401`; this is a protocol request,
not a browser GET. Actual operator login and an authenticated external MCP client remain manual acceptance.
The previous verification-only local package and saved adoption patch are no longer product dependencies.

The SDK's combined source separately passed 1,095 tests with one existing Kafka skip. Its incremental
Release build emitted 639 analyzer warnings and no errors; the product's clean build does not imply a
warning-free SDK. The SDK includes the separately reviewed HTTP error boundary and breaking AI client update.

### Deployment outcomes

A background follower now observes durable submissions independently of browser polling. Each pass discovers
all jobs, reads at most two due target journals, and persists retry timing with backoff up to five minutes.
Per-job nonblocking locks keep competing browser reads responsive. Terminal observations require `completedAt`,
so rollback and cleanup can finish. Explicit reads can still discover direct target-side recovery, and local
reconciliation receipts take precedence over stale results. No background action submits or retries a deployment.

Runner verification passed 183/183, including restart discovery beyond the latest fifty jobs, transient failures,
missing receipts, malformed records, concurrent writers and recovery. Five real-process hosted-service checks
are included in the 20 passing integration tests. The target journal remains authoritative; missing receipts
still require operator recovery. A real rollout with the browser closed and a control-plane restart remains
manual acceptance. The projection is file-backed; deployment database ownership remains separate backlog work.

### Live acceptance and recovery

- Back up existing installations before migration 007: its forward migration drops the legacy `servers` table;
  its rollback is destructive recovery for dev/test. A fresh VPS has no existing inventory to lose.
- Run a fresh published x86 deployment; confirm the returned release and real site response.
- Restart the host/services and verify state, authentication and runner access recover.
- Take an encrypted off-provider backup; restore into an empty database and verify useful data.
- Keep recovery and decryption credentials outside the server.
- Add an external availability probe and operational backup-age/disk alerts for unattended operation.

The [backup and launch gates](../deployment/deployment.md#backups-and-launch-gates) also require real HTTPS,
Google/Stripe callback checks and redirect restoration before public ForeverPin cutover. Those product gates
do not prevent starting a private Wheelhouse control plane.

---

## Server recommendation — monthly billing, checked October 6

Choose **OVHcloud VPS-1 2027 with No commitment** for the private Wheelhouse control plane and read-only MCP.
Keep development and testing local. The screenshot's `$4.54/month` is the discounted twelve-month option:
`$54.48` payable before tax. The [worldwide public catalog](https://ca.api.ovh.com/1.0/order/catalog/public/vps?ovhSubsidiary=WS)
separately lists `default` billing at `$5.35`, commitment zero, interval one month, setup zero.
Select **No commitment** in the [configurator](https://www.ovhcloud.com/en/vps/configurator/), and confirm the
review total is for one month. The current cart has not been changed or submitted.

All alternatives below are x86, two vCPUs and 4 GB RAM. Prices exclude tax. Availability and account acceptance
are separate from a published catalog; no provider account or server was created.

| Option | Monthly price | Storage | Billing and practical boundary |
|---|---:|---:|---|
| OVHcloud VPS-1 2027 | `$5.35` | 40 GB NVMe | No commitment, one-month interval; IPv4 and standard daily backup advertised included |
| Hetzner CX23, EU | `€5.99` / `$7.09` including IPv4 | 40 GB NVMe | Hourly with monthly cap; public product page currently says unavailable |
| Vultr `vc2-2c-4gb`, Singapore | `$20`; `$24` with automatic backups | 80 GB SSD | Usage billing, no annual purchase; public region API currently lists this plan |
| DigitalOcean Basic Regular | `$24` before backups | 80 GB SSD | Per-second billing with a monthly cap; account capacity unverified |

Vultr is the fallback if leaving OVH: its [plan catalog](https://api.vultr.com/v2/plans?type=vc2) lists the named
Intel plan, and [Singapore availability](https://api.vultr.com/v2/regions/sgp/availability) currently includes it.
[Credit-card registration](https://docs.vultr.com/platform/create-an-account) permits a zero deposit; other payment
methods can require one. [Automatic backups add 20%](https://docs.vultr.com/support/platform/billing/how-much-does-it-cost-to-enable-automatic-backups).
[Usage billing](https://docs.vultr.com/support/platform/billing/how-am-i-billed-for-my-servers) does not require an annual purchase.

Hetzner's [June 2026 prices](https://docs.hetzner.com/general/infrastructure-and-availability/price-adjustment/)
list CX23 at `€5.49` / `$6.49`; [IPv4](https://docs.hetzner.com/cloud/servers/primary-ips/overview/) adds `€0.50` / `$0.60`.
[Backups](https://docs.hetzner.com/cloud/billing/faq/) add 20% of the server price. The
[CX23 page](https://www.hetzner.com/cloud/cost-optimized/) currently reports unavailable. CPX22 EU is substantially
more expensive at `€19.99` / `$23.59` including IPv4; its public page also reports unavailable. Old cheap CPX22
quotes are stale. DigitalOcean's [current pricing](https://www.digitalocean.com/pricing/droplets) verifies the
2-vCPU, 4-GiB, 80-GiB plan at `$24` and capped usage billing.

Singapore, Mumbai and Europe are plausible locations for Uzbekistan; actual connection latency is unmeasured.
OVH catalog region choices do not establish available capacity. Standard backup covers the previous day;
the catalog's one-day backup charge has an offsetting promotion, whose application still needs the final checkout.
Seven-day retention adds `$1.40/month`. Provider snapshots do not replace an encrypted off-provider restore test.

Capacity is an estimate. Wheelhouse declares 512 MB; three ForeverPin environments alone declare 3.375 GiB,
before PostgreSQL, vault, ingress and OS. Four GB is a control-plane starting point. One low-traffic product
environment may fit after measuring memory, disk and rollout peaks; multiple environments warrant 8 GB or more.
Forty GB also needs bounded image retention and off-provider backups.

OVH is implemented as `ovhcloud` across backend, frontend and runner inventory. Existing provider ordinals are
preserved; PostgreSQL stores unconstrained text, so this addition needs no migration. Choosing Vultr or DigitalOcean
would need its provider enum addition before registering the host; SSH transport itself is provider-independent.

---

## Work that can follow the private launch

- Automated host preparation, database provisioning and environment start/stop controls.
- Registrar purchasing, automatic DNS, preview-domain governance and certificate-expiry views.
- Cost dashboards, billing feeds, placement optimization and broader fleet capacity management.
- Deploy-time secret rendering/token issuance and the separate SDK vault consumer.
- Temporary branch environments, zero-downtime replacement and older release browsing.
- Live log streaming, external audit checkpoints and candidate-image retention automation.
- UI layout cleanup, formatting, broader SDK extraction and automatic client-contract generation.

Manual bootstrap does not remove backup, access-control or recovery acceptance. Alerts may be wired externally
for the pilot while their product UI remains deferred. The implemented outcome follower still needs live
acceptance on the selected host; it does not add mutation tools to the read-only MCP surface.

---

## Same-day sequence and completion evidence

1. Verify the existing native login in a browser; preserve the running local services.
2. Connect an actual MCP client using an operator-issued scoped integration key.
3. Choose monthly/no-commitment billing; confirm the exact quote, region and account acceptance.
4. Register the selected provider and target; OVH support is already implemented and locally verified.
5. Publish the intended Wheelhouse source and verify CI/release assets.
6. Provision the x86 host and private access; configure persistent PostgreSQL, keys, inventory, SSH and OAuth.
7. Bootstrap the same immutable release; check real login, private ingress, MCP reads and runner readiness.
8. Complete one supervised deployment, follow its terminal result, restart and verify persistence.
9. Restore an encrypted off-provider backup; record the verified service URL, release digest and recovery result.

Completion means a reachable private host, a real authenticated user session, successful MCP initialization/tool
calls, a verified current release and working recovery. A passing local build, old deployment record, HTTP health
response or successful image publication alone does not meet that bar.

The user is selecting monthly billing before purchasing a starter VPS. Production access hostname/OAuth setup and the backup
destination/recovery targets remain open. Account verification or unavailable capacity can move the live-host
portion beyond today even when the code and local checks finish.
