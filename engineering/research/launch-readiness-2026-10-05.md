# Wheelhouse launch readiness — 2026-10-05

*Last updated: 2026-10-06 — Asia/Samarkand*

The first remote deployment is **Wheelhouse dev** on the purchased OVH VPS. Separate test and prod
environments are deferred. Local startup and adopted MCP authorization tests are verified; remote hosting,
real OAuth and an authenticated external MCP client remain unverified. OVH has delivered the VPS and reports
it Active; authenticated SSH and the first live dev rollout remain open. Public ForeverPin launch remains separate.

This is a dated readiness analysis. The [backlog](../planning/backlog.md) and
[v0.3 track](../planning/version-track/v0.3/v0.3.md) remain the capability and acceptance records.

The user approved pushing the pending work and implementing automatic main-to-dev deployment on October 6.
The [delivery workflow and bootstrap runbook](../deployment/deployment.md#automatic-wheelhouse-dev-delivery)
now define the executable path: one main push tests its tip SHA, publishes through the shared workflow,
then deploys the verified bundle over pinned SSH. Multiple commits in one push do not multiply builds.
Local verification passed 217 runner tests and five host-configuration tests. The approved push sent fourteen
commits together (`02ac3df..7613d15`), creating one
[delivery run](https://github.com/wow-two-platform/wow-two-platform.wheelhouse/actions/runs/37503369037).
Backend, frontend and runner CI passed; the shared publisher produced
[v0.3.5](https://github.com/wow-two-platform/wow-two-platform.wheelhouse/releases/tag/v0.3.5)
from `7613d15d04c4fe77adfef37ca697343deb720d21`.

The GitHub `dev` environment now permits only `main`. Dedicated CI key, host and account secrets are
configured, with `DEV_DEPLOY_ENABLED=false`. The key is not yet authorized on the VPS, and the independently
verified host-key secret is still missing. Authenticated VPS access, private HTTPS/OAuth, target registry
access and live rollout acceptance remain open. The successful workflow explicitly skipped remote deployment;
it does not establish a running hosted app.

## First remote environment — dev, October 6

### Confirmed scope and provider state

Use one target, `wheelhouse-dev`, with one Compose project and a dedicated `wheelhouse_dev` database/user.
The environment name selects deployment policy: dev accepts candidate builds or releases. It does not require
ASP.NET's Development runtime. Keep `ASPNETCORE_ENVIRONMENT=Production` and rehearsal disabled on the VPS:
the [Development settings](../codebase/wheelhouse.backend-services/Wheelhouse.Api/appsettings.Development.json)
point at the local rehearsal and loopback database; the
[Production authentication guard](../codebase/wheelhouse.backend-services/Wheelhouse.Api/Auth/AuthConfigurationExtensions.cs)
requires an owner allowlist. No test-pass override is needed for a dev target.

During this review, the paid order moved from delivery in progress to **Your order is available**.
The VPS dashboard now reports **Active**, Ubuntu 26.04, Beauharnois in Canada (`os-bhs6`),
VPS-1 2027, 2 vCPU, 4 GB RAM and 40 GB disk. Its assigned hostname and IPv4/IPv6 addresses
are available in the provider console; host identifiers are not copied into this public-repository analysis.
Standard automated backup, no commitment, automatic renewal and the next payment date of November 6
are shown. The checked order total was $5.35 for one month at the displayed tax.
Provider status verifies delivery; SSH login, host fingerprint and installed application state remain unverified.

### Install and persist

| Location | Install or configure | Purpose and boundary |
|---|---|---|
| VPS OS | Security updates, OpenSSH, protected deployment account, Python 3 | Bootstrap and execute the existing target runner; Docker access is host-privileged |
| VPS OS | Official Docker Engine and Compose v2 | Run released `linux/amd64` images; verify Ubuntu 26.04 support and packages during installation |
| VPS OS | Tailscale and persistent Serve configuration | Private administration and HTTPS; laptop/phone enrollment remains open |
| Platform Compose | PostgreSQL 16 with persistent storage | Existing repository baseline; dedicated dev database/user, no public port |
| Platform Compose | Traefik file provider on the external `platform` network | Read runner-generated routes from `/srv/wheelhouse/ingress` |
| Product bundle | Wheelhouse `console`, declared 512 MiB cap | Contains SPA, .NET runtime, Python runner and SSH client |
| Persistent state | Database, `/data/keys`, `/data/deployments`, host target state and protected configuration | Preserve login cookies, inventory, integration keys, deployment jobs and recovery evidence |
| Recovery | Encrypted off-provider backup and a tested restore | Provider daily backup is additional protection, not the sole recovery path |

GitHub builds the SPA and container through the existing
[descriptor](../deployment/deploy.yml) and [Dockerfile](../deployment/Dockerfile).
Do not install Node, Vite, the .NET SDK or file watchers on the VPS. The first host does not need Kubernetes,
a broker, Valkey or a separate vault service. Measure memory, disk and rollout peaks before adding products.
The [local Compose file](../deployment/docker-compose.yml) is a verification stack, not the remote deployment contract.

### Private access

```text
Laptop / phone enrolled in the same tailnet
  -> https://<actual-node>.<actual-tailnet>.ts.net:443
  -> Tailscale Serve on the VPS
  -> http://127.0.0.1:<private-port> (Traefik's loopback-published private entrypoint)
  -> http://wheelhouse-dev-console:8080 (Docker platform network)
  -> PostgreSQL (internal network only)
```

Bootstrap SSH uses the delivered public address and provider-specified account. OVH's
[first-connection guide](https://support.us.ovhcloud.com/hc/en-us/articles/360009253639-Getting-started-with-a-VPS)
describes the delivery credentials; verify the host fingerprint through the provider console.
Establish and test ordinary OpenSSH over Tailscale, with the runner's existing identity and pinned
`known_hosts`, before narrowing public SSH. Retain provider-console recovery access.
No Docker API, database or Wheelhouse application port should be publicly published.

[Docker port publishing](https://docs.docker.com/engine/network/port-publishing/) and
[Ubuntu installation guidance](https://docs.docker.com/engine/install/ubuntu/) require attention to binding
and firewall behavior: published container traffic can bypass UFW. Bind the private ingress explicitly to
loopback and test reachability from outside the tailnet after configuration.
[Tailscale Serve](https://tailscale.com/docs/reference/tailscale-cli/serve) supplies private HTTPS;
Tailscale Funnel is not part of this plan.

The existing [route renderer](../codebase/wheelhouse.runner-services/runner.py) supports this layout:
set server ingress `scheme=https`, `port=null` (or 443), `privateEntryPoints=["private"]`,
`certResolver=null`, and both `probe` and `privateProbe` to `http://127.0.0.1:<private-port>`.
The runner's general readiness check uses `probe`; private site checks use `privateProbe`.
Set the target's `console`
site to the actual Tailscale DNS hostname. Traefik's `private` entrypoint serves plain HTTP behind Serve;
it has no default TLS. There is no `private_tls` inventory field.

Preserve the original host and HTTPS scheme through both proxy hops. Configure Traefik's
[forwarded-header trust](https://doc.traefik.io/traefik/reference/install-configuration/entrypoints/)
for Serve's observed source address, which may be a Docker gateway rather than loopback.
Set `Deployment:TrustedProxies` to the exact Traefik address visible to the app; do not use trust-all.
The app's one-hop forwarding limit does not establish the original client IP through both hops.
Verify real redirects and cookies; a successful route probe is insufficient.

### Bootstrap using the current deployment method

1. Verify the delivered host's architecture and SSH identity, and establish recoverable administrator access.
2. Install the host dependencies and create the platform network, PostgreSQL and private ingress.
3. Create the dev database/user and protected console settings. Supply the connection string, OAuth client,
   owner allowlist, `AllowedHosts` including the real hostname and `localhost`, and exact trusted proxies.
   Preserve UID `1654` readability for settings and write access to cookie keys as documented in
   [runner installation](../deployment/deployment.md#runner-installation-and-inventory).
4. Register the OAuth callback `https://<actual-node>.<actual-tailnet>.ts.net/api/identity/callback`.
   The enrolled user's browser must reach this private callback. Keep secrets out of source, terminal output and chat.
5. Publish the intended current source and verify both CI and the separate image-publication workflow.
   Download its release bundle, verify source/checksum metadata, and prove the VPS can pull the exact image digest.
   Configure a read-only registry credential if the package remains private.
6. Copy the reviewed target `runner.py` and verified bundle to the VPS. Prepare a protected target JSON with
   product `wheelhouse`, environment `dev`, root `/srv/wheelhouse`, service settings, private ingress,
   and `variables.PLATFORM_NETWORK="platform"`. Generated Compose needs that explicit variable;
   the runner does not inherit arbitrary ambient environment variables.
   Use the existing runner's `validate`, `check`, `launch` and `status` actions to bootstrap without the dashboard.
   Accept bootstrap only after `status=succeeded` and a nonempty `completedAt`.
   Terminal labels can appear before rollback or cleanup finishes; launching alone does not establish success.
7. Sign in and register the OVH server and remote dev target through the console. Set the product's public
   release repository and permitted image repositories: the fresh Wheelhouse product row has no release source.
   Place the runner's SSH identity and verified `known_hosts` in persistent `/data/deployments/ssh/<server-id>/`.
   Let the API generate `inventory.json`; do not hand-edit that API-owned export.
8. Perform an ordinary console-driven dev rollout. Verify completion with the browser closed, restart the
   control plane, and confirm inventory, integration keys, cookie keys and target state survive.
9. Restore an encrypted backup into an empty database and verify useful records before accepting unattended operation.

The runner already provides locks, image pull before replacement, health gates, target journals and recovery.
Automatic image rollback is allowed only when the release declares schema compatibility; the current published
manifest has `rollbackCompatible:false`. Database restore is a separate operator action. One-command host
bootstrap, backup automation and external alerts remain backlog work, not required new deployment engines.

### MCP acceptance

Use the same private hostname with `/mcp`, and create a scoped integration key from an authenticated owner session.
Verify initialization, tool listing, a catalog read, a deployment read, missing-key rejection and revoked-key rejection.
The [MCP runbook](../deployment/mcp.md) defines the implemented stateless Streamable HTTP and Bearer-key contract.

For a locally executing Codex client on the tailnet, the
[official MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli) supports an HTTP URL and
`bearer_token_env_var`. Keep the token in the client's protected environment, not committed configuration.
Phone browser access to the UI and cloud ChatGPT MCP access are different acceptance checks.

Cloud ChatGPT needs a reachable transport and compatible authentication. An
[OpenAI Secure MCP Tunnel](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels) can supply an
outbound private transport, subject to account/workspace eligibility and permissions. The documented
[ChatGPT custom MCP setup](https://developers.openai.com/api/docs/guides/custom-mcp-server) offers OAuth,
no authentication and mixed authentication; it does not establish support for this server's arbitrary static
Bearer-key configuration. Wheelhouse currently has no MCP OAuth discovery. Tunnel setup alone therefore
does not complete authentication compatibility; keep cloud ChatGPT integration as separate work.

### Outstanding inputs and live checks

- Provider-specified SSH username, initial access and a verified host fingerprint; the assigned address is known.
- Approved tailnet enrollment, actual private hostname and enrolled client devices.
- OAuth application credentials, callback registration and owner login.
- Target registry pull access and a verified running image digest; current source is published as `v0.3.5`.
- Backup destination, retention, recovery credentials and a completed restore.
- Real browser login, trusted TLS, runner readiness, MCP client calls and persistence after restart.

The runner's site checks report warnings and skip certificate validation for HTTPS routing probes.
Independently verify trusted TLS and real application workflows. A health response does not prove this list.

---

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

On October 6, a fresh GitHub read still found remote `main` at `02ac3df` and latest release `v0.3.4`.
Local `main` was `0d1cb9f`, twelve implementation/documentation commits ahead before this analysis update.
Those unpublished commits include the MCP endpoint, OVH inventory support and durable outcome follower.
Package visibility could not be read with the current GitHub credential; a real target pull remains required.
The user explicitly approved the publication-triggering push and dev CI implementation on October 6,
resolving the earlier automatic approval rejection. Release success still requires a completed live workflow;
local implementation and authorization alone do not establish publication or deployment.

Publish and verify the intended source before selecting the first remote dev release. The
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

- Establish access to the delivered OVH VPS; configure private administration and persistent storage for dev.
- Prepare Docker/Compose, Python, PostgreSQL, protected runner files, pinned SSH and private ingress.
- Register the remote dev GitHub OAuth callback for the actual private HTTPS hostname.
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

## Purchased server and earlier comparison — October 6

The user purchased **OVHcloud VPS-1 2027 with No commitment** for the initial private dev deployment.
Separate test/prod environments remain deferred. The earlier screenshot's `$4.54/month` was the twelve-month option:
`$54.48` payable before tax. The [worldwide public catalog](https://ca.api.ovh.com/1.0/order/catalog/public/vps?ovhSubsidiary=WS)
separately lists `default` billing at `$5.35`, commitment zero, interval one month, setup zero.
The completed order was checked as one month, Canada-East-Beauharnois and Ubuntu 26.04;
the current delivery status is recorded above. Purchase completion does not establish server readiness.

The following comparison records the earlier October 6 shopping research, not an open purchasing decision.
All alternatives are x86, two vCPUs and 4 GB RAM. Prices exclude tax; their capacity and account acceptance were unverified.

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

The purchased region is Canada-East-Beauharnois; actual connection latency from Uzbekistan is unmeasured.
The order includes standard daily backup. Earlier research found seven-day retention adds `$1.40/month`;
that add-on was not selected. Provider snapshots do not replace an encrypted off-provider restore test.

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

1. Establish authenticated SSH to the delivered host and verify its architecture and host identity.
2. Establish private access and install the host/platform dependencies for one dev environment.
3. Push the approved delivery workflow and pending source; verify CI and the resulting immutable bundle.
4. Configure persistent PostgreSQL, keys, runner state, SSH and the real private OAuth callback.
5. Bootstrap `wheelhouse-dev` with the existing runner and verify owner login.
6. Register the real server, dev target and product release source; check runner and registry access.
7. Connect a local MCP client through Tailscale using an operator-issued scoped key.
8. Complete a supervised dev rollout, follow its terminal result, restart and verify persistence.
9. Restore an encrypted off-provider backup; record the service URL, release digest and recovery result.

Completion means a reachable private host, a real authenticated user session, successful MCP initialization/tool
calls, a verified current release and working recovery. A passing local build, old deployment record, HTTP health
response or successful image publication alone does not meet that bar.

The VPS is delivered and Active in OVH, with an assigned address; authenticated SSH remains unverified.
Private hostname/enrollment, OAuth, registry access and recovery inputs remain open; `v0.3.5` publication passed.
These dependencies determine the first remote dev deployment date; separate test/prod setup is deferred.
