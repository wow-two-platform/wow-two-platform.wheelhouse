# Deployment operations

*Last updated: 2026-10-02*

## Ownership and current boundary

GitHub Actions builds immutable images. Wheelhouse submits reviewed release bundles over pinned SSH.
The target-side Python runner owns locks, durable intent, health gates and recovery. The same runner works without the dashboard.
Artifact publication and retention: [architecture](../architecture/architecture.md#artifacts-and-registry).
A product declares its services, builds and sites in `engineering/deployment/deploy.yml`
([convention](../../../../../conventions/deployment/descriptor/deploy-descriptor.md));
the release generator in `wow-two-platform.pipelines` (`generator/release.py`) turns it into the bundle Wheelhouse
deploys.

## Environments

Every product runs `dev`, `test` and `prod` targets, all on one host at once, told apart by Compose project,
platform-network alias, database, settings files and hostnames. No hardware separates them.

| Environment | Takes | Runs |
|---|---|---|
| `dev` | A build of any commit or branch, or a release | For work in progress and sharing a feature |
| `test` | Published releases | For acceptance |
| `prod` | Published releases that succeeded on test | For customers |

- The transport refuses a commit build on test or prod, before any file reaches the target.
- Prod takes a release only after it succeeded on one of the product's test targets.
- The test pass comes from the local deployment records, else from each test target's verified release.
- `--skip-test-pass` overrides the gate only with the typed target ID (`--confirm <target>`).
- On the local server, prod deploys only after the operator types the target ID (`--confirm foreverpin-prod`).

## Sites

- A release declares its sites: a named entry point on one service's port, optionally a path prefix, public or private.
- A target names each site's host; dev and test may follow the server's pattern, prod names every host itself.
- After a verified rollout the runner writes `<root>/ingress/<product>-<environment>.yml`, a Traefik file-provider route.
- The route maps the host to `<product>-<environment>-<service>:<port>` on the platform network.
- The target records each site's URL; Wheelhouse shows Open site links on the target and after a deploy.
- A release without sites removes the project's route file; a private site routes only on private entry points.
- After publishing, the runner requests every site through the ingress by its host name, the way a visitor reaches it.
- A site that does not answer within 20 seconds is a warning on the deploy and on its Open site link, not a failure.
- Traefik's own 404 for an unknown host counts as no answer; an application's 404 or 401 counts as an answer.
- A server's `Ingress.probe` names where the runner reaches the public entry points; it defaults to loopback on the scheme's port.
- Private sites are probed only when the server sets `private_probe`; the local server probes `http://ingress:80`.

Open VPS wiring and launch work waits in the [backlog](../planning/backlog.md) § Hosting.
Local image builds use the current working tree; publishing requires all intended source/dependency changes committed together.

## Wheelhouse's own releases

Wheelhouse ships like the products it deploys. `engineering/deployment/deploy.yml` declares one service, `console`,
with its settings keys, `keys` and `deployments` volumes, a private `console` site and `needs: [postgres]`.

| Workflow | Runs on | Does |
|---|---|---|
| `.github/workflows/ci.yml` | Every push and pull request | Backend tiers (building the SPA into `wwwroot`), runner tests with the bundle contract against the pinned generator, frontend tests and build |
| `.github/workflows/publish-docker-image.yml` | Every push, or a dispatched commit | Calls the shared `publish` workflow in `wow-two-platform.pipelines`: `main` releases `vX.Y.Z` with the asset `wheelhouse-release.tar.gz`; other branches publish a `bundle-<sha>` artifact |

A local build proves the path without GitHub:

```sh
python3 ../wow-two-platform.pipelines/generator/release.py build --repo . --checkout \
  --platform linux/arm64 --registry 127.0.0.1:15000/wheelhouse --output /tmp/wheelhouse-bundle
```

## Local packaging

From this repository root:

```sh
export POSTGRES_PASSWORD='<local-only generated password>'
export WHEELHOUSE_ADMIN='<your GitHub login>'
docker compose -p wheelhouse-local -f engineering/deployment/docker-compose.yml up --build --wait
```

This Compose file is a local acceptance stack. Its PostgreSQL database and cookie keys use project-scoped volumes.
The API binds to loopback. Configure GitHub credentials through a protected `appsettings.Local.json` mount for real sign-in.
Production refuses an empty `Identity:AllowedGitHubLogins`.
The local HTTP endpoint is not evidence of real HTTPS/OAuth readiness.

Build context `engineering/codebase/` excludes local overrides, environment files, private keys, logs and generated output.
The Node stage builds the SPA; .NET publishes the supplied SPA without running Node again.
The runtime runs as `app` and uses PostgreSQL, not SQLite.

## Runner installation and inventory

The image includes `/app/runner/{runner,transport,inventory,catalog,fleet,artifacts}.py`.
The runner reads products, servers, targets and vaults from `inventory.json`, which the API rewrites from its database
at startup and after every change; Servers and Products in the console edit them. A row reaches nothing until the
operator places its credential files below.

Mount a protected persistent directory at `/data/deployments` containing:

```text
inventory.json            ← written by the API; never edit by hand
ssh/<server-id>/identity
ssh/<server-id>/known_hosts
vaults/<vault-id>/password
bundles/<artifact-id>/{release,compose,source}.json
jobs/
observed/
reconciled/
```

A new database starts with ForeverPin and Wheelhouse as products and no server. During wiring, add the verified host
under Servers (slug, address, SSH user and port, ingress), place its `ssh/<slug>/` files, then add each environment
under Products (server, settings files per service, smoke checks, site hosts).

Add a real redirect smoke probe to the target after the stable pilot code exists.
The default root is `/srv/wheelhouse`; secret values never enter source, bundles or the browser.
IDs are stable lowercase slugs; never reuse a host ID for a different machine.

A product's release source on its inventory row names the approved public repository and exact service image repositories.
The catalog lists only published versioned release assets with completed upload and checksum metadata.
Wheelhouse validates the archive, bundle contents and tag/source commit when a release is selected.
A GHCR pull remains the definitive image-availability check before container replacement.
Use `Deployment:GitHubTokenFile` for a mounted read-only catalog token; the operator CLI reads the equivalent
`WHEELHOUSE_GITHUB_TOKEN_FILE` environment variable. Anonymous GitHub requests have a lower shared-IP rate limit.
The token is sent only to the API origin and is removed from CDN redirects. Private release downloads are not yet supported.

Registry login belongs to the target deployment account. Public GHCR images need no pull credential;
private images require a read-only token and `docker login --password-stdin`.
Verify first-publish package visibility explicitly; source-repository visibility does not prove image visibility.

The target requires Linux, Python 3, Docker Engine/Compose v2, private configuration files and the external `platform` network.
A deployment account with Docker access can control the host; membership in the Docker group is privileged.

Private settings can be mode `600` owned by the image UID (`1654`) with a runner account able to read them.
Alternatively, use mode `644` inside a mode `700` directory owned by the deployment account:
the directory protects host access, while the read-only file bind is readable by the container UID.
Never use group/world-writable settings. Cookie key volumes must remain writable by UID `1654`.

Generate each service's settings file from its release contract, then fill every value:

```sh
python3 engineering/codebase/wheelhouse.runner-services/transport.py template \
  --root /path/to/inventory --bundle <artifact-id> --service management > management.json
```

The runner rejects a deployment before changing any container when a listed key is blank,
when `AllowedHosts` omits `localhost`, or when `Deployment:TrustedProxies` is not a JSON array.
Health checks and smoke probes call `http://localhost:8080`, so use `"<public host>;localhost"`.
A plain-string proxy value binds to no proxy; use `["<ingress IP>"]`.

## Operator and API commands

From the repository root, using the same inventory as the dashboard:

```sh
R=engineering/codebase/wheelhouse.runner-services/transport.py
python3 $R targets  --root /path/to/inventory
python3 $R releases --root /path/to/inventory
python3 $R check    --root /path/to/inventory --target foreverpin-test --bundle <artifact-id>
python3 $R submit   --root /path/to/inventory --target foreverpin-test --bundle <artifact-id> --actor operator
python3 $R status   --root /path/to/inventory --job <returned-id>
python3 $R jobs     --root /path/to/inventory
python3 $R state    --root /path/to/inventory --target foreverpin-test
python3 $R topology --root /path/to/inventory --target foreverpin-test
python3 $R reconcile --root /path/to/inventory --target foreverpin-test --job <active-id> --actor operator
python3 $R vitals   --root /path/to/inventory [--target foreverpin-test]
python3 $R stats    --root /path/to/inventory --days 30
python3 $R logs     --root /path/to/inventory --target foreverpin-test --service management --tail 200
```

`check`, `state`, `topology`, `vitals` and `logs` stream the runner over SSH stdin and leave no files on the target.
`logs` returns one service's last 1-1000 container lines, timestamped; nothing stores them.
`submit --confirm <target>` carries the typed confirmation that prod on the local server requires.

```sh
python3 $R branches --root /path/to/inventory --product foreverpin
python3 $R commits  --root /path/to/inventory --product foreverpin --branch main
python3 $R build    --root /path/to/inventory --product foreverpin --commit <full sha>
```

`commits` marks each commit that has a build. `build` starts the product's build workflow only for a commit without one;
it needs `WHEELHOUSE_GITHUB_TOKEN_FILE` with Actions write access on the product repository.
`check` probes SSH, the deployment root, Docker architecture, Compose, disk, the shared network, the ingress,
every settings file and the target's lock state; each failure names the rule or key it broke.

| API | Purpose |
|---|---|
| `GET /api/deployments` | Recent submissions with their last observed outcome and reason |
| `GET /api/deployments/targets` | Configured target bindings |
| `GET /api/deployments/targets/{id}/state` | Verified release with its sites and service versions, and `ready` / `running` / `needs_reconciliation` |
| `GET /api/deployments/targets/{id}/topology` | Saved Compose services, startup dependencies, logical networks and named volumes from the last successful release |
| `GET /api/deployments/targets/{id}/check?release=` | Read-only readiness, optionally against one release |
| `POST /api/deployments/targets/{id}/reconcile` | `{"job":"<active-id>"}` with `X-Wheelhouse-Action: reconcile` |
| `GET /api/servers` | Hosts defined in code; registration and deletion return `405` |
| `GET /api/deployments/releases` | Published releases and per-commit builds (`kind`: `release` \| `candidate`) from approved sources |
| `GET /api/deployments/products/{product}/branches` | The product repository's branches |
| `GET /api/deployments/products/{product}/commits?branch=` | A branch's recent commits, each with its build when one exists |
| `POST /api/deployments/products/{product}/builds` | `{"commit":"<sha>"}` with `X-Wheelhouse-Action: build`; `202` means the workflow was started |
| `POST /api/deployments` | `{"target":"…","release":"…","confirm":"…"}` with `X-Wheelhouse-Action: deploy`; `202` means queued |
| `GET /api/deployments/{id}` | The target-owned outcome, with each rollout step and any warning |
| `GET /api/deployments/targets/{id}/services/{service}/logs?tail=200` | One service's last container lines (1-1000), never cached |
| `GET /api/audit?limit=50&before=` | Operator actions, newest first; `before` pages back from a sequence number |
| `GET /api/audit/verification` | Whether every stored audit entry and link still verifies |
| `GET /api/deployments/vitals` | Every target's host load, memory, disks, uptime and containers, read in parallel |
| `GET /api/deployments/stats?days=30` | Outcomes, success rate, median rollout and recovery, deploys per UTC day (1–90 days) |
| `GET /api/deployments/vitals/history?hours=24&target=` | Stored readings of the last 1–720 hours, oldest first, for one target or all |
| `GET /api/vaults/{vault}/hygiene` | Secrets and product tokens due for rotation; metadata only |

- A lost SSH response is an unknown outcome; inspect target state before retrying.
- Cookie-authenticated cross-origin writes cannot supply the custom header without an allowed CORS preflight.
- Restarting the dashboard does not terminate a launched target worker.
- Reconciling records who acknowledged the rollout under `<inventory>/reconciled/`.

The API reads its runner settings from the `Deployment` section: `TransportPath`, `Root`, `Python` and `GitHubTokenFile`.
A background follower observes durable deployment submissions every `Deployment:FollowSeconds` (default 10;
0 disables it). Each pass reads at most two due target journals, with a 30-second SSH limit per read. Transport
failures retain the last outcome; failed or unknown reads back off up to five minutes. Retry timing survives a control-plane
restart in `<runner root>/following/`; it never submits, retries or reconciles a deployment. A terminal status is
final only when `completedAt` is present, after rollback or cleanup. Explicit status reads can still observe a later
target-side acknowledgement. Preserve `jobs/`, `observed/`, `reconciled/` and `following/` with the runner root.

A background sampler reads every target's vitals each `Operations:VitalsSampleMinutes` (default 5; 0 turns it off),
stores load, memory, the fullest disk and container health in `vitals_samples`, and deletes readings older than 30 days.

## Rollout steps

Each rollout records its steps in its job record as they start and end, so the dashboard follows a deploy live:

| Step | Covers |
|---|---|
| Check target | Lock, settings files, routes, disk, Docker and the release snapshot |
| Pull images | Every image, before any container changes |
| Start containers | `compose up --wait` within the target's health timeout |
| Verify services | Exact image references, health, and the target's smoke checks |
| Publish sites | The route file for the release's sites |
| Probe sites | Each site through the ingress; a site that does not answer is a warning |
| Remove unused images | Images only releases older than the newest three used; skipped when there are none |
| Roll back to … | The previous release, only for a failed rollout that declared rollback compatibility |

A step's detail is operator-safe text; a failed step shows the refused rule or failed command, never command output.

## Audit trail

Every operator action lands in `audit_entries`: deploy, reconcile, build, vault changes and product changes.
A pipeline behavior records each audited command's outcome, refusals included, with the operator's login.
Entries hold the action, its subject and operator-safe detail, never a secret value or token.
Each entry is hash-chained to the one before it; the Activity page shows whether the chain still verifies.
The table refuses `UPDATE` and `DELETE`, so rewriting an entry needs someone who can disable its trigger.
An intact chain proves no stored entry was edited, reordered or removed from its middle.
It cannot prove the newest entries were kept; that needs a checkpoint held outside the database.

`vitals` reads `/proc`, filesystem capacity and `docker ps`/`inspect`/`stats` for the target's Compose project;
it never returns container environment, labels or logs. `stats` reads only the local submission records.
The overview flags: unreadable or locked targets, missing or unhealthy containers, 3+ restarts, disks over 75%
(red over 90%), memory over 90%, load over 150% of CPUs, an unrecovered failed rollout, sealed vaults,
active secrets older than 90 days, and tokens expired, expiring within 14 days or older than 180 days.

## Secrets vaults

Servers in the console declare each vault's slug, host and private management URL; the browser never chooses the URL,
and the status list never shows it.
Wheelhouse signs in with the administrator password at `<inventory>/vaults/<vault-id>/password`, a protected mount,
and keeps the one-hour session in memory. The console writes values and never reads them back.
A minted product token is returned once, with `Cache-Control: no-store`, and only its metadata remains afterwards.
Wheelhouse logs the operator, vault and change for every write; values and tokens never reach logs.

Reach a vault on Wheelhouse's own private network, or through an SSH tunnel when Wheelhouse runs on a workstation.
Vault-side audit records show Wheelhouse's administrator identity; Wheelhouse's log names the human operator.

## Local server

`engineering/deployment/rehearsal/` runs the local server: a disposable SSH target, a Traefik ingress, a registry,
a product database and, when a `secrets-vault:local` image exists, a vault. It hosts ForeverPin's `dev`, `test` and
`prod` at once. Dev is the default; test and prod deploy on demand. `WHEELHOUSE_REHEARSAL=1` exposes the local
targets, vault and imported bundles; a deployed control plane never sets it.

```sh
cd engineering/deployment/rehearsal
python3 rehearse.py run                                       # server, dev candidate, settings, check, deploy to dev
python3 rehearse.py bundle --tag v0.0.1-local.1               # a release, which test and prod also take
python3 rehearse.py settings --env test --tag v0.0.1-local.1
python3 rehearse.py deploy --env test --tag v0.0.1-local.1
python3 rehearse.py deploy --env prod --tag v0.0.1-local.1 --confirm foreverpin-prod
python3 rehearse.py bundle --tag v0.0.1-broken.1 --broken     # a release that fails after replacement
python3 rehearse.py state --env dev
python3 rehearse.py self                                      # Wheelhouse builds and deploys itself to wheelhouse-dev
python3 rehearse.py down --volumes
```

- Sites answer at `http://<site>-foreverpin.<environment>.localhost:18080` (`app` and `go`).
- Chromium and Firefox resolve `*.localhost` to loopback; verify Safari before relying on it.
- Bundles come from the pipelines generator (`../wow-two-platform.pipelines/generator/release.py`, a sibling checkout) with the local `foreverpin-{management,redirect}:local` images; a candidate keeps the
  services unchanged since the newest imported release.
- The bundle reads ForeverPin's own `engineering/deployment/deploy.yml` at the checkout's commit.
- The generator needs PyYAML; `rehearse.py` uses the system Python when the current one lacks it.
- The server generates its own SSH and vault keys under `rehearsal/state/` (ignored by Git) and pins the host key it generated.
- Each environment gets its own database (`foreverpin_<environment>`) and settings folder (`state/secrets/<environment>/`).
- Point a local API at the inventory with `WHEELHOUSE_REHEARSAL=1`, `Deployment__Root` and `Deployment__TransportPath`.
- `self` builds Wheelhouse's image and bundle from this checkout with `release.py` and deploys it to `wheelhouse-dev`.
- Its private `console` site answers at `http://console-wheelhouse.dev.localhost:18080`, on database `wheelhouse_dev`.
- Sign-in there needs a GitHub OAuth app in `state/secrets/dev/wheelhouse-console.json`; the file starts with placeholders.

### From an IDE

`python3 rehearse.py dev` starts the server plus a loopback database (`127.0.0.1:15432`) that
`appsettings.Development.json` points at, with `Deployment:Rehearsal` = `host` and the runner paths resolved from the
project folder. Run `Wheelhouse.Api` with the `https` launch profile and open `https://localhost:8210`; the header's
`Local server` badge marks an instance that deploys to the local server, not real hosts. Sign-in uses the API
project's user-secrets (`Identity:GitHub`, callback `https://localhost:8210/api/identity/callback`).
The build compiles the SPA with the Node pinned in `wheelhouse.frontend-services/.nvmrc` when nvm has it, so a Rider
launched from the Dock builds even when its PATH holds an older system Node.

`Deployment:Rehearsal` is the only switch: the API sets `WHEELHOUSE_REHEARSAL` for the runner from it and drops any
inherited value, so a deployed control plane cannot be pointed at the local server by environment alone.

### Local console — the whole system before a VPS

`python3 rehearse.py console` adds Wheelhouse itself: the production image (`wheelhouse:local`), its own
PostgreSQL, and the local inventory mounted at `/data/deployments`. It runs as `Production` at
`http://localhost:18210` and drives the target over SSH (`target:22`) and the vault (`http://vault:8080`) by service
name, the way it will drive a VPS; `Deployment:Rehearsal` = `network` selects that view and `Deployment:RehearsalState`
carries the host path for settings files. A transport run by hand inside the console needs
`WHEELHOUSE_REHEARSAL=network REHEARSAL_STATE=<host state path>`, because only the API sets them for its runner.

One-time sign-in setup, on the GitHub account that will operate Wheelhouse:

1. GitHub → Settings → Developer settings → OAuth Apps → New OAuth App.
2. Homepage `http://localhost:18210`; callback `http://localhost:18210/api/identity/callback`.
3. Put the client id and a new client secret in `rehearsal/state/console/appsettings.Local.json` (`Identity:GitHub`).
4. Check `Identity:AllowedGitHubLogins` names that account, then run `python3 rehearse.py console` again.

Use a Chromium-based browser: it accepts the `Secure` session cookie on `http://localhost`.
A separate OAuth app with the production callback serves the VPS; the local one stays local.

With a bundle and each environment's settings written, the console lists them for `foreverpin-dev`, `-test` and
`-prod`. The `v0.3` Verification iteration is the test plan. `rehearse.py down` stops everything; `--volumes` also
drops the console's database, the vault's data and the target's state.

## Target state and recovery

```text
/srv/wheelhouse/<product>-<environment>/
  lock
  active.json
  current.json                  release, service versions and site URLs of the verified rollout
  jobs/<job-id>.json
  releases/<job-id>/{release,compose}.json
  releases/<job-id>/images-removed    marks an older release whose images were removed
/srv/wheelhouse/ingress/<product>-<environment>.yml   Traefik routes for the verified release's sites
```

Pulls finish before replacement. Success requires healthy containers with the exact image references and configured smoke responses.
Failed image pulls preserve the running release. A failed rollout restores prior images only if the incoming bundle explicitly
declares `rollbackCompatible: true`. The default release generator leaves this false.

Database restore is never automatic. Image rollback cannot undo a destructive migration or data written after a backup.
An interrupted or unrecovered mutation blocks another deployment until the operator inspects containers/schema and acknowledges it:

```sh
python3 /srv/wheelhouse/incoming/<submission>/runner.py status \
  --target /srv/wheelhouse/incoming/<submission>/target.json --job <remote-job-id>
python3 /srv/wheelhouse/incoming/<submission>/runner.py acknowledge \
  --target /srv/wheelhouse/incoming/<submission>/target.json --job <remote-job-id>
```

Acknowledgement clears the previous-success pointer rather than assuming it is still safe for automatic rollback.
It changes bookkeeping only. Recover application/database state explicitly before acknowledging.
A retry after reconciliation establishes a new known-good release.

Raw Docker/SSH output never enters a job record because it may contain runtime secrets.
Job records keep actor, release, source SHA, timestamps, steps, outcome and failure category.
A `reason` names the refused rule, missing setting key, failed step or unhealthy service, never a value.
The API returns it as ProblemDetails `detail`: `409` for a refused precondition, `503` for a failed step.
A service's recent container output is readable on request through `logs`; Docker rotates it on the target.

## VPS wiring checklist

1. Verify provider account, host architecture, capacity and cost in the wiring session.
2. Verify the SSH fingerprint through the provider console; install the pinned known-hosts file.
3. Install Docker/Compose and Python using the chosen OS's official instructions.
4. Configure private administration and firewall; expose only intended ingress on 80/443.
5. Start Traefik and PostgreSQL on the private platform network; Traefik's file provider reads `/srv/wheelhouse/ingress`.
6. Create distinct least-privilege databases/users for every product/environment.
7. Write protected runtime settings and registry credentials.
8. Name each prod site's host on its environment under Products; the runner routes it after a verified rollout.
   Set the server's private probe to the private entry point's address so private sites are probed too.
9. Add the ingress IP to `Deployment:TrustedProxies` in both app settings; no trust-all proxy setting.
10. Bootstrap Wheelhouse privately or use the operator command from a workstation.
11. Deploy test, verify real URLs and provider callbacks, restore a backup, promote the same image digests to prod.

Existing product containers keep serving without Wheelhouse. A new VPS is a Servers entry plus its credential files; it uses the same product release.
Data relocation remains a separately planned copy/restore/cutover operation.

## Backups and launch gates

Before public cutover, create an encrypted off-provider backup of product/control-plane databases, key volumes and required configuration.
Hold decryption and recovery credentials outside the VPS. Record recovery time/data-loss targets and backup retention.
Restore into an empty database and verify a real code resolves before accepting the backup path.

Required live checks: stable printed URL, HTTPS, Google sign-in, Stripe test callback, external redirect monitor, disk/backup-age alerts,
and measured CPU/RAM headroom. Review SDK dependency advisories reported by the clean image restore.
The separate ForeverPin product track still owns incomplete content modes and validation features.

## Verification

```sh
python3 -m unittest discover -s engineering/codebase/wheelhouse.runner-services
dotnet test engineering/codebase/wheelhouse.backend-services/Wheelhouse.BackendServices.slnx -p:SkipSpaBuild=true -m:1
(cd engineering/codebase/wheelhouse.frontend-services && pnpm typecheck && pnpm build)
```

The rehearsal rig exercises real SSH, Compose, health gates and recovery; hosted `linux/amd64`, TLS, OAuth and
off-provider backups remain open in the [backlog](../planning/backlog.md) § Hosting.
