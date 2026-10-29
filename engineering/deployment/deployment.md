# Deployment operations

*Last updated: 2026-10-28*

## Ownership and current boundary

GitHub Actions builds immutable images. Wheelhouse submits reviewed release bundles over pinned SSH.
The target-side Python runner owns locks, durable intent, health gates and recovery. The same runner works without the dashboard.
Artifact publication and retention: [architecture](../architecture/architecture.md#artifacts-and-registry).
A product declares its services, builds and sites in `engineering/deployment/deploy.yml`
([convention](../../../../../conventions/deployment/descriptor/deploy-descriptor.md));
the release generator in `wow-two-platform.pipelines` (`generator/release.py`) turns it into the bundle Wheelhouse
deploys.

## Environments

Products work on `main` only and fix forward, with no long-lived branches. Every `main` release deploys to dev, then
is promoted to prod. Every product runs `dev` and `prod` targets, both on one host at once, told apart by Compose
project, platform-network alias, database, settings files and hostnames. No hardware separates them.

| Environment | Takes | Runs |
|---|---|---|
| `dev` | A build of any commit or branch, or a release | For work in progress and proving each release |
| `prod` | Published releases that succeeded on dev | For customers |

- The transport refuses a commit build on prod, before any file reaches the target.
- Prod takes a release only after it succeeded on one of the product's dev targets.
- The dev pass comes from the local deployment records, else from each dev target's verified release.
- `--skip-dev-pass` overrides the gate only with the typed target ID (`--confirm <target>`).
- On the local server, prod deploys only after the operator types the target ID (`--confirm foreverpin-prod`).
- `test` was retired on 2026-10-17: migration `013-drop-test-environment` deleted its targets and their vitals, and
  the API refuses it. Its deployment records and audit entries still read, labelled `test (retired)`; a success on a
  retired test target is no dev pass.

## Sites

- A release declares its sites: host roles, public or private, each published in some environments (all by default).
- Each service serves paths of a site on its port; descriptor 2 lists several per site, descriptor 1 one
  ([convention](../../../../../conventions/deployment/descriptor/deploy-descriptor.md#sites-and-routing)).
- A target names each site's host; dev may follow the server's pattern, prod names every host itself. Domain
  bindings replace both later; `site_host` in the runner is that seam.
- A public site with no host answers on its baseline host (decision 68), on the public address enrollment or Refresh
  kit recorded on its server: `<product>-dev.<ip>.sslip.io` in dev, `<product>.<ip>.sslip.io` in prod, the address's
  dots as dashes; a site other than the release's primary one adds `-<site>` after the product
  (`ocharo-studio-admin-dev.203-0-113-10.sslip.io`). The runner fills it at deploy, so `AllowedHosts` and
  `Sites:<site>:Url` name it. The environment form lists every site the product declares, read-only: the host the target
  stores, else the derived baseline host (it follows the environment and server, and a stored host that differs from the
  derived one stays with a note); nothing is typed. A private site never gets a baseline host and shows its tailnet node.
- A baseline host is served by the server's own ingress on 80 and 443 with a Let's Encrypt HTTP-01 certificate, not
  through Cloudflare; a bound domain replaces it.
- After a verified rollout the runner writes `<root>/ingress/<product>-<environment>.yml`, a Traefik file-provider route.
- One router per site path maps the host and path to `<product>-<environment>-<service>:<port>` on the platform network.
- `/` renders `` Host(`h`) ``; `/api` renders `` Host(`h`) && (Path(`/api`) || PathPrefix(`/api/`)) ``, never `/apiary`.
- Priority is 100 plus the path's length, so the longest prefix wins whatever the rule's length (`/` 101, `/api` 104).
- A site whose `environments` leave out the target's environment is not routed there.
- The target records one URL per service site, its shortest path, the site's root first; Wheelhouse shows Open site
  links on the target and after a deploy, one per site.
- A release without sites removes the project's route file; a private site routes only on private entry points.
- Only public routers carry the server's certificate resolver: a private entry point sits behind its site node's Serve,
  which ends TLS.
- After publishing, the runner requests every site through the ingress by its host name, the way a visitor reaches it.
- A site that does not answer within 20 seconds is a warning on the deploy and on its Open site link, not a failure.
- Traefik's own 404 for an unknown host counts as no answer; an application's 404 or 401 counts as an answer.
- A server's `Ingress.probe` names where the runner reaches the public entry points; it defaults to loopback on the scheme's port.
- Private sites are probed only when the server sets `private_probe`; the local server probes `http://ingress:80`.

Open VPS wiring and launch work waits in the [backlog](../planning/backlog.md) § Hosting.
Local image builds use the current working tree; publishing requires all intended source/dependency changes committed together.

## Products of several repositories

A product may span several repositories (decision 70); Ocharo is the first: `ocharo` = `ocharo-hq/ocharo-platform`
(primary: Desk, jobs, image worker), `ocharo-studio`, `ocharo-marketing` (deploys nothing yet) and `ocharo-assets`
(the motion lab). Release sets: [architecture](../architecture/architecture.md#release-sets).

- Every repository's `deploy.yml` names `product: ocharo`; services (`desk`, `jobs`, `image-worker`, `studio`,
  `motion`), sites (`desk` public, the front door; `studio` public; `motion` private) and volumes (`keys`, `models`,
  `studio-data`, `motion-data`) are unique across them.
- One set of environments: `ocharo-dev` and `ocharo-prod`, each on one server, root `/srv/wheelhouse/ocharo-<env>/`,
  databases `ocharo_dev` and `ocharo_prod`, one secrets namespace per environment, baseline hosts
  `ocharo[-dev].<ip>.sslip.io` (Desk) and `ocharo-studio[-dev].<ip>.sslip.io`.
- The product has no release until each deploying repository has published one after its `product: ocharo` change;
  from then on, every repository release queues a new set for dev, and Ready to promote offers the set dev proved.
- Settings and secrets stay per service: Studio's `OcharoPlatform:BaseUrl` names Desk (its public origin, or
  `http://ocharo-<env>-desk:8080` on the platform network).

### Moving prod to the merged product (owner, after promoting this release)

Prod holds `ocharo-platform` and `ocharo-studio`, with Studio's dev and prod environments; nothing was deployed to
them, they hold no data and no placement ran. In the prod console:

1. **Catalog → Ocharo → select `ocharo` → Import.** The entry `replaces: [ocharo-platform, ocharo-studio]`, so the
   import creates `ocharo` with its four repositories and retires both: Studio's two environments first, then each
   product with its settings, secrets and pins. The result says `retired 2 replaced products`; a warning instead
   means one has deploys on record and stays (remove its environments and the product by hand once torn down).
2. **Products → Ocharo → Manage → Add environment**, twice: `ocharo-dev` (dev) and `ocharo-prod` (prod) on the products
   server; the second form starts as prod, and the site hosts fill themselves from the manifest.
3. **Overview → Hosting → Place on a server** (prod console only): PostgreSQL databases `ocharo_dev` and `ocharo_prod`
   (Desk and jobs need it) and the private `motion` site's tailnet nodes, offered from the merged `deploy.yml`s.
4. **Deployments → Deploy**, `ocharo-dev`, the newest set (`platform-v… + studio-v… + assets-v…`): the dialog asks for
   each required key no layer sets (Desk's `DataProtection:*`, Studio's `OcharoPlatform:BaseUrl`, the image worker's
   `ImageWorker:Key` secret), then deploys. Auto-deploy takes every later set to dev.
5. **Deployments → Ready to promote → the set → Promote** to `ocharo-prod`, with a reason; its changelog lists each
   repository that changed.

## Wheelhouse's own releases

Wheelhouse ships like the products it deploys. `engineering/deployment/deploy.yml` declares its identity (name,
description, homepage), one service, `console`, with its settings keys, `keys` and `deployments` volumes, a private
`console` site and `needs: [postgres]`, one `migrate` item on `console`
([§ Wheelhouse migrates itself](#wheelhouse-migrates-itself)), and `dotnetEnvironment: true`: `wheelhouse-dev` runs as
`Development`, `wheelhouse-prod` as `Production`. Its workflows pin pipelines `v0.13.0`, which checks Renovate's
branches without publishing (`v0.12.0` carries `migrate` and records each item image's migration sets, `v0.11.0`
accepts the identity keys, `v0.5.0` writes `dotnetEnvironment`).

| Workflow | Runs on | Does |
|---|---|---|
| `.github/workflows/publish-docker-image.yml` | Every push, pull request, or a dispatched commit | Calls the shared `delivery.yml` with Wheelhouse's checks (backend tiers; runner, CI contracts and VPS kit; frontend), then dev deployment for a published main push; a docs-only push runs nothing |
| `.github/workflows/codeql.yml` | Main pushes beyond markdown, and weekly | The shared CodeQL scan, beside delivery |
| `.github/workflows/deploy-dev.yml` | Called after successful main publication | The fallback: resolves the tested bundle and deploys it over pinned SSH; reports only, once `WHEELHOUSE_SELF_DEPLOY` is `true` |
| `.github/workflows/promote-prod.yml` | Dispatched, with an optional release tag | The fallback: after the owner's approval, deploys a dev-proven release to prod; refuses once `WHEELHOUSE_SELF_DEPLOY` is `true` |

Once activated, Wheelhouse deploys itself and CI only builds ([§ Wheelhouse deploys itself](#wheelhouse-deploys-itself)).

A local build proves the path without GitHub:

```sh
python3 ../wow-two-platform.pipelines/generator/release.py build --repo . --checkout \
  --platform linux/arm64 --registry 127.0.0.1:15000/wheelhouse --output /tmp/wheelhouse-bundle
```

## Wheelhouse deploys itself

Decisions 64–66: CI builds, tests and publishes; it never reaches the control host. Wheelhouse prod deploys each release
to `wheelhouse-dev` like any product and promotes itself from its own console. Nothing changes until prod runs a release
with this code and the owner switches it on ([§ Activation](#activation)); until then CI's path below works as before.

```text
main push -> CI: test, build, publish vX.Y.Z (the release carries the bundle)
prod: release watcher (every 2 min, the GitHub App) -> queue -> runner over SSH to 172.30.0.1 -> wheelhouse-dev
  -> health, site probe through Traefik, the declared dev check, the watch window
owner: Ready to promote -> type wheelhouse-prod + a reason -> detached runner job on the host -> wheelhouse-prod
  -> health, then the site probe; either failing restores the previous release -> the new console reads the outcome
no console answers -> self-deploy.py on the host: status, deploy, rollback (vps.md § Break-glass self-deploy)
```

| Piece | Where | What it does |
|---|---|---|
| Wheelhouse in prod's inventory | Products › Wheelhouse, deploy mode `wheelhouse`; targets `wheelhouse-dev`, `wheelhouse-prod` | Both consoles on the control host, as products are on theirs ([§ Inventory rows](#inventory-rows)) |
| Console ingress | runner `fleet.py` `CONSOLE_INGRESS` | A Wheelhouse target takes its environment's entry point, port and probe from the kit (`prepare.py`), whatever the server row says, so one row serves both |
| Rollback | runner `probeGate`, the rollback gate, `rollbackCompatible` on Wheelhouse targets | A new console its ingress cannot reach rolls back. A return between releases that declare `migrate` runs the older image's `migrate check` first; a return to an earlier release trusts the forced `rollbackCompatible`, and across a migration that release's boot refuses newer history (be sdk `10.0.80`): [vps.md § Rolling back across a migration](vps/vps.md#rolling-back-across-a-migration). Migrations only expand (decision 65, the be sdk lint in `MigrationRulesTests`) |
| Dev check | `deploy.yml` `verify` -> `self_check.py` in the image | After each dev rollout: readiness, status and an anonymous MCP initialization the console must refuse (CI's checks) |
| Promotion guard | `SelfDeploymentInterceptor`, runner `transport.submit` | Prod only by the owner: typed ID and a reason; never automation, never a fleet, never while a fleet runs |
| Read-back | `SelfDeploymentBootReader`, `GET /api/self-deployment`, the shell's notice | The new console reads its own newest rollout from the host's journal at boot and shows how it ended |
| Break-glass | `vps/self-deploy.py` | Host-local deploy, rollback and status with the release image's own runner |
| CI fallback switch | repository variable `WHEELHOUSE_SELF_DEPLOY` | `true` turns CI's deploy path off; `false` or unset turns it back on |
| Host hardening | `vps/harden-control.sh` | Firewall, keys-only SSH, updates; then tailnet-only SSH (vps.md § Host hardening) |

### Inventory rows

In Wheelhouse prod, once it runs v0.5:

- **Server** `control`: provider OVHcloud, host `172.30.0.1` (the host's `platform` address, as the consoles see it),
  SSH user `wheelhouse-deploy`, port 22, scheme `https`. Its entry points and probes matter to no Wheelhouse target; set
  `private` and `http://127.0.0.1:18080`. A server at a private address reads as the control host: Servers shows it so
  and offers no Enroll (the API refuses one, 409). Prod's runner identity for it:
  `sudo bash /srv/wheelhouse/bootstrap/authorize-runner.sh wheelhouse-prod control` (same-host mode).
- **Targets** `wheelhouse-dev` and `wheelhouse-prod`: Products › Wheelhouse › Add environment derives every field,
  read-only, and the API refuses any other (400): server `control` (the only control host), network `control`, root
  `/srv/wheelhouse`, host settings file `console` -> `/srv/wheelhouse/config/console-<env>.json`, site `console` -> this
  console's own hostname, the host's tailnet name (its rendered `Sites:console:Url`, else the first name in its
  `AllowedHosts`, which the kit's `console-<env>.json` sets). `GET /api/targets/self` shows what it derives.
- Each deploy renders `/srv/wheelhouse/config/wheelhouse-<env>/console.target.json`: the host file plus the derived
  `AllowedHosts`, site URL and `Deployment:TrustedProxies` (Traefik's `control` address). CI and `self-deploy.py`
  mount the host file itself, so moving between them restarts the console once.
- Prod reads Wheelhouse's public releases and pulls its private console image through the GitHub App
  (vps.md § GitHub access); without the App the host's own GHCR sign-in serves the pull.

### Self-promotion

- Ready to promote lists Wheelhouse like any product; the deploy dialog preselects the newest release that succeeded
  on dev, as it does for every prod target, and an older one is a rollback.
- The dialog asks for `wheelhouse-prod` typed and a reason, warns that the console goes away for about a minute, and
  keeps asking for the outcome while it does. The API answers 202 before anything changes; the runner's detached host
  job then snapshots prod's database, pulls, replaces the container and waits for health and the site probe.
- A failed health or site probe restores the previous release by itself; the database stays as the new release left
  it. Its schema suits the previous release (decision 65). A previous release that declares `migrate` passes its own
  `migrate check` first and boots on the newer history; an earlier one's migrator refuses history rows it does not
  know, so a restore across a migration fails to boot until those rows go
  ([vps.md § Rolling back across a migration](vps/vps.md#rolling-back-across-a-migration)). The snapshot under
  `snapshots/<job>/` is the manual restore that loses later writes.
- The new console (or the old one, after a roll back) reads its own newest rollout from the host's journal at boot,
  logs it and shows it above every page until dismissed or a day old.
- The audit trail keeps `deployment.start` with the actor, release and reason. Auto-promote, fleets and automatic
  causes never reach Wheelhouse prod; step-up 2FA replaces the typed ID once it ships (decision 66).
- Rollback is a promotion of an earlier release: it qualifies when dev's journal holds its success, whoever deployed it.

### Wheelhouse migrates itself

The console's database moves only in the rollout's migrate step (spec § 3.4, M19): `deploy.yml` declares
`[dotnet, Wheelhouse.Api.dll, migrate]` on `console`, and the runner runs it in a one-off container with the console's
settings, after the pull and before the console restarts ([§ Migrate step](#migrate-step)). A deployed console only
checks its level at boot and refuses a database the step did not bring to it; a local run still applies at boot. The
image answers every `migrate` verb (`check`, `sets --json` with no settings, the guarded operator verbs) before the web
host starts.

| Path | Runner | Runs the migrate step |
|---|---|---|
| Prod's console deploys a release to `wheelhouse-dev` | prod's own, uploaded by `transport.submit` | once prod runs v0.7.7 or later |
| Prod's console promotes a release to `wheelhouse-prod` | prod's own, the release being replaced | once prod runs v0.7.7 or later |
| `self-deploy.py deploy` | the release's, copied out of its image | always |
| `self-deploy.py rollback` | the older release's | never; its rollback gate when that release declares `migrate` |
| CI's fallback (`ci_deploy.py`) | the tested checkout's | always |

- The step's first release adds no migration; every live console's database stood at `034` already, so it boots on
  either path, and the step, where it runs, records the history's kinds.
- The first release that adds a migration ships only once prod runs v0.7.7 or later: deployed by an older runner it
  refuses to boot on the older database and restores the previous release, nothing applied.
- Returns: [Rollback](#wheelhouse-deploys-itself) above; the targets keep `rollbackCompatible` forced until neither
  console keeps a release from before the step (v0.7 track, Iteration 9).
- The local stacks run the same command first: the rig's `console-migrate` and the acceptance stack's `migrate`
  one-shots, each awaited (`service_completed_successfully`).

### The fallback switch and its retirement

`WHEELHOUSE_SELF_DEPLOY` (a repository variable) is the one switch. `true`: `deploy-dev.yml` only reports that prod
deploys dev, and a dispatched `promote-prod.yml` fails before the `prod` environment; `ci_deploy.py` refuses too.
`false` or unset: CI deploys dev and promotes prod as before. Set it with
`gh variable set WHEELHOUSE_SELF_DEPLOY --body true -R wow-two-platform/wow-two-platform.wheelhouse`.

Retire CI's path after a week and three self-promotions (decision 64), in this order:

| When | Where | Delete |
|---|---|---|
| After the trial | Repository | `.github/workflows/deploy-dev.yml`, `promote-prod.yml`, the `deploy-dev` job of `publish-docker-image.yml`, `ci_deploy.py`, `ci_remote.py`, `test_ci_deploy.py` (a commit) |
| Then | GitHub environment `dev` | Secrets `DEV_SSH_HOST`, `DEV_SSH_USER`, `DEV_SSH_KEY`, `DEV_SSH_KNOWN_HOSTS`; variable `DEV_DEPLOY_ENABLED` |
| Then | GitHub environment `prod` | Secrets `DEV_SSH_HOST`, `DEV_SSH_USER`, `DEV_SSH_KEY`, `DEV_SSH_KNOWN_HOSTS` |
| Then | Repository | Environments `dev` and `prod` themselves; the variable `WHEELHOUSE_SELF_DEPLOY` |
| Then | Control host | CI's key line in `/home/wheelhouse-deploy/.ssh/authorized_keys` (match `ssh-keygen -lf` of the CI public key); `/srv/wheelhouse/ci/` |
| Last | Control host | `harden-control.sh`, then `--tailnet-only-ssh` (vps.md § Host hardening) |

`DEV_SSH_PORT` is referenced but was never set. Repository-level secrets are already empty. After the deletions the
host CLI is the fallback; bringing CI back needs a new key and new secrets.

### Activation

The owner, in order; every step up to the burn-in reverts with the switch:

1. Promote prod to v0.5 through CI: `gh workflow run promote-prod.yml -R wow-two-platform/wow-two-platform.wheelhouse`
   (an empty release takes the newest one dev ran; the summary names it), then approve the `prod` environment.
2. Give prod the GitHub App (vps.md § GitHub access): its key in prod's volume, the App ID in `console-prod.json`.
3. Install the kit's new files on the host and read both consoles (vps.md § Break-glass self-deploy, Install).
4. In prod: Deployments › Automation, Wheelhouse's auto-deploy to dev off. Settings › Catalog lists Wheelhouse as
   Catalog changed (its `wow-two-ws/catalog.yml` entry says `deploy: wheelhouse`); import it for deploy mode
   `wheelhouse`, its release source and its catalog workspace. Servers: `control`; authorize prod's runner for it. Then
   both targets from Add environment; Check passes.
5. From prod, deploy the release dev runs to `wheelhouse-dev`; its dev check passes; the dev console restarts once.
6. `gh variable set WHEELHOUSE_SELF_DEPLOY --body true -R wow-two-platform/wow-two-platform.wheelhouse`, then turn
   Wheelhouse's auto-deploy to dev on in prod. The next main push reaches dev within about two minutes of publishing.
7. Promote one release from prod's Ready to promote; the page comes back about a minute later with the outcome.
8. Roll prod back once from the host, then promote forward again from the console:
   `sudo -u wheelhouse-deploy python3 /srv/wheelhouse/bootstrap/self-deploy.py rollback --env prod --reason "rehearsal"`.
9. Burn in for a week with at least three self-promotions. Fallback: the variable back to `false` and Wheelhouse's
   auto-deploy to dev off in prod.
10. Retire CI's path and its secrets ([the table above](#the-fallback-switch-and-its-retirement)).
11. Harden the host: `harden-control.sh`, then `--tailnet-only-ssh` (vps.md § Host hardening).

## Automatic Wheelhouse dev delivery

The fallback path until retired ([§ Wheelhouse deploys itself](#wheelhouse-deploys-itself)); it runs only while
`WHEELHOUSE_SELF_DEPLOY` is not `true`. The first remote target is `wheelhouse-dev`.
Use the reviewed [VPS bootstrap](vps/vps.md) for Ubuntu 26.04 and private HTTPS.

```text
one main push (one or many commits)
  -> check that SHA is still main; skip it if only docs changed since the last delivered commit
  -> verify its final SHA: the checks whose paths changed (backend, runner and VPS kit, frontend)
  -> shared publish (pipelines v0.12.0) builds changed services once and publishes the immutable bundle
  -> resolve that tested bundle, check main again
  -> pinned SSH -> existing durable runner -> container/digest + trusted HTTPS checks
```

[GitHub push events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#push)
select the tip commit; no job iterates the pushed commit list. Ten commits in one push produce one delivery run,
one shared publication invocation, and at most one console image build. Tests compile the application too;
"one build" here means one container publication, not eliminating test compilation.

Separate pushes share one branch-level concurrency group. It uses `cancel-in-progress:false` and
[`queue:max`](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#concurrency)
so a newer push never cancels an active target operation. Up to 100 runs can wait. Queue arrival order is not
commit order: a SHA superseded before publication or immediately before launch is skipped. Once a target
rollout starts, it finishes; a later main push follows. Manual reruns of old SHAs cannot roll dev backwards.

The shared publication workflow remains the sole image builder. Documentation-only pushes may publish no
new release. [Bundle resolution](../codebase/wheelhouse.runner-services/ci_artifacts.py) prefers an exact
source match; otherwise it requires an ancestral release and the pinned generator's proof that descriptor
and every console build input are unchanged. It verifies the asset digest, tag/source agreement, archive,
approved image repository and `linux/amd64`. The original bundle is retained: the receipt separately records
the tested push SHA and actual image-source SHA. It never deploys a moving image tag or blindly picks latest.

### Wiring the GitHub dev environment

Use a dedicated restricted CI SSH key for the deployment account. The initial GitHub-hosted runner connects
to the verified public SSH address; the web UI, database and MCP remain private. This workflow does not yet
join a tailnet, so `DEV_SSH_HOST` must be reachable by the GitHub-hosted runner. The bootstrap runbook describes
later ephemeral Tailscale CI access. No self-hosted GitHub runner is installed on the application VPS.

| GitHub `dev` environment value | Kind | Meaning |
|---|---|---|
| `DEV_SSH_HOST` | Secret | Verified SSH address reachable from GitHub-hosted runners |
| `DEV_SSH_USER` | Secret | Dedicated `wheelhouse-deploy` account |
| `DEV_SSH_KEY` | Secret | Dedicated private key; its public half must already be authorized |
| `DEV_SSH_KNOWN_HOSTS` | Secret | Host-key line independently checked through OVH; no trust-on-first-use scan |
| `DEV_SSH_PORT` | Variable | Optional; defaults to `22` |
| `DEV_DEPLOY_ENABLED` | Variable | Set `true` only after host, private HTTPS, OAuth and registry wiring |

Until enabled, the workflow reports explicitly that deployment was not performed. Enabled runs fail on
missing configuration. Limit the environment's deployment branch to `main`; prod values are not used.
GitHub's job token reads release metadata. GHCR credentials and application secrets live on the VPS under
the deployment account; they are not copied into build arguments or CI logs.

### Reruns and completion

[CI execution](../codebase/wheelhouse.runner-services/ci_deploy.py) uploads a run/attempt-specific bundle,
`runner.py` and a small [target adapter](../codebase/wheelhouse.runner-services/ci_remote.py).
The target configuration stays at `/srv/wheelhouse/config/wheelhouse-dev.json`.
The adapter records intent before calling the existing `runner.launch`; it does not implement Compose deployment.
The existing runner detaches on the VPS, so replacing Wheelhouse or losing the SSH connection does not own its lifetime.

Each GitHub run ID has a persistent receipt under `/srv/wheelhouse/ci/receipts`. A rerun resumes observation
of that original job. A lost launch reply can recover from the matching target journal; an ambiguous intent
fails closed instead of launching another deployment. Failed or interrupted mutations require the existing
reconciliation/recovery procedure; rerunning CI never silently retries them. Uploads/receipts are retained
for diagnosis; include them in bounded host retention work once the first deployment is accepted.

When the target already runs the resolved release and source commit, CI launches nothing: the receipt points at
the running job and the same verification follows. A documentation-only push therefore never restarts the console.

CI requires `status=succeeded` and a nonempty `completedAt`, then verifies the current job, running image
digests and health, trusted private HTTPS readiness, and unauthenticated MCP rejection. Route-probe warnings
cannot substitute for these checks. Real owner OAuth login, authenticated MCP reads/revocation, restart
persistence and off-provider restore remain separate acceptance checks. A timed-out CI observer reports
failure while the durable target job remains authoritative; inspect it before any new submission.

GitHub currently supports `queue:max`; local actionlint `1.7.12` does not yet parse that field. Validate YAML
duplicates/structure and use actionlint with only that documented field omitted in a temporary copy;
GitHub workflow acceptance remains the definitive check for this field.

## Promoting Wheelhouse to prod

Once activated, prod promotes itself ([§ Self-promotion](#self-promotion)). Until then, and while the switch is off,
CI promotes it:

```text
promote-prod (optional release tag, rollback flag, from main)
  -> GitHub `prod` environment waits for the owner's approval
  -> pinned SSH: read dev's journal and what prod runs; an empty release takes the newest release that succeeded on
     wheelhouse-dev (by version, as Ready to promote offers it); one older than prod's needs `rollback: true`
  -> the run summary names the release and why
  -> download exactly that release; verify asset digest, tag/source and approved image
  -> refuse unless a wheelhouse-dev job ran that release to `succeeded` with `completedAt`
  -> existing runner -> wheelhouse-prod -> the same digest, HTTPS and MCP checks as dev
```

- Run it from the Actions tab, `gh workflow run promote-prod.yml` (the newest dev-proven release) or
  `-f release=vX.Y.Z`; a rollback adds `-f rollback=true`.
- Roll back by promoting an earlier release with `rollback: true`; it qualifies because it once succeeded on dev.
- Re-promoting the running release verifies it without a restart.
- Migrations run forward in the rollout's migrate step, before the console restarts; a schema change that cannot roll
  back needs a reviewed recovery.
- The `prod` environment allows only `main` and carries the same SSH secrets as `dev`: one host, one deploy account.

## Local packaging

From this repository root:

```sh
export POSTGRES_PASSWORD='<local-only generated password>'
export WHEELHOUSE_ADMIN='<your GitHub login>'
docker compose -p wheelhouse-local -f engineering/deployment/docker-compose.yml up --build --wait
```

This Compose file is a local acceptance stack. Its PostgreSQL 18 database and cookie keys use project-scoped volumes;
the `migrate` one-shot runs the image's bare `migrate` before the console starts, as a rollout's migrate step does.
The API binds to loopback. Configure GitHub credentials through a protected `/app/appsettings.Target.json` mount for real sign-in.
Production refuses an empty `Identity:AllowedGitHubLogins`.
The local HTTP endpoint is not evidence of real HTTPS/OAuth readiness.

Build context `engineering/codebase/` excludes local overrides, environment files, private keys, logs and generated output.
The Node stage builds the SPA; .NET publishes the supplied SPA without running Node again.
The runtime runs as `app` and uses PostgreSQL, not SQLite.

## Runner installation and inventory

The image includes `/app/runner/{runner,transport,inventory,catalog,fleet,artifacts}.py` and the runner's other
modules, compiled at build (`compileall`, checked-hash): `app` cannot write the folder, so every runner start would
otherwise compile each module it imports.
The runner reads products, servers, targets and vaults from `inventory.json`, which the API rewrites from its database
at startup and after every change; Servers and Products in the console edit them. A row reaches nothing until the
operator places its credential files below.

Mount a protected persistent directory at `/data/deployments` containing:

```text
inventory.json            ← written by the API; never edit by hand
ssh/<server-id>/identity
ssh/<server-id>/known_hosts
vault/root.key            ← the built-in vault's root key: root-only, kept out of backups (Secrets Vault below)
vaults/<vault-id>/password ← a standalone vault's, until it is imported and retired
github-app/private-key.pem ← the GitHub App key, optional (GitHub access below)
github-token/token        ← the token file: a classic read:packages token, one line; private repositories' images need it
providers/tailscale/oauth-client.json ← prod only: the Tailscale OAuth client (Tailscale below)
providers/tailscale/policy/ ← the tailnet policy before Wheelhouse's last change, for one-click revert
infra/                    ← enrollment and placement journals, one-time enrollment keys while a host needs them
github/                   ← GitHub reads kept briefly, the App's tokens for the runner, refused-call notes
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
GitHub credentials, private releases included: [GitHub access](#github-access).

Products hosts keep no registry token: each deploy lends its pull a sign-in from the control host
([registry sign-in](../architecture/architecture.md#registry-sign-in)). Public GHCR images need none. The GitHub App's
installation token reads the packages of its owner's public repositories only; a package that inherits a private
repository's access needs the token file, a classic token with `read:packages` only (`Deployment:GitHubTokenFile`,
by default `<runner root>/github-token/token`, read at each call, so placing it needs no restart). The control host
offers whichever reads each image's manifest; the target signs in to a temporary Docker config for the pull only and
signs out at once. An image nothing can read fails the deploy's `Pull images` step, naming the package, and shows in
the readiness check (**Registry access**) and under Deployments › Issues › GitHub access. Check which credential reads
what:

```sh
docker exec <console-container> python3 /app/runner/transport.py registry --root /data/deployments [--product <slug>]
```

A host that still holds the earlier sign-in (`prepare.py --ghcr-user …`) can drop it:
`sudo -u wheelhouse-deploy docker logout ghcr.io`. Verify first-publish package visibility explicitly;
source-repository visibility does not prove image visibility.

The target requires Linux, Python 3, Docker Engine/Compose v2, private configuration files and the external `platform` network.
A deployment account with Docker access can control the host; membership in the Docker group is privileged.

Wheelhouse renders every service's settings file at each deploy
([settings at deploy](../architecture/architecture.md#settings-at-deploy)): `config/<product>-<environment>/<service>.target.json`,
mode `0444` inside the mode-`0700` folder, from the environment's stored settings and built-in vault secrets, what it derives
(`AllowedHosts`, `Sites:<site>:Url`, `Deployment:TrustedProxies`) and the target's `database.json`. Nothing on the
host is hand-edited: the deploy dialog asks for each required value no layer sets, and the deploy waits until it has
one. A settings file a target still names is the render's bottom layer, so existing hosts keep working; to retire one,
give its values in Wheelhouse, deploy, then clear the path on the target. Cookie key volumes must remain writable by
UID `1654`.

Without a console (a CI-deployed console), hand-placed settings files still
work: mode `600` owned by the image UID (`1654`), or mode `644`/`444` inside a mode-`700` directory owned by the
deployment account; never group- or world-writable. Generate one from its release contract, then fill every value:

```sh
python3 engineering/codebase/wheelhouse.runner-services/transport.py template \
  --root /path/to/inventory --bundle <artifact-id> --service management > management.json
```

The runner rejects a deployment before changing any container when a listed key is blank,
when `AllowedHosts` omits `localhost`, or when `Deployment:TrustedProxies` is not a JSON array.
Health checks and smoke probes call `http://localhost:8080`, so use `"<public host>;localhost"`.
A plain-string proxy value binds to no proxy; use `["<ingress IP>"]`.

## GitHub access

Every GitHub call — hub catalogs, manifests, icons, release listings and downloads, workflow runs, commits, branches
and build dispatches — carries the first credential at hand for the owner its address names:

1. The GitHub App's installation token for that owner (`Deployment:GitHubAppId`, `Deployment:GitHubAppKeyFile`).
2. The single token file `Deployment:GitHubTokenFile`, by default `<runner root>/github-token/token` once placed
   (`WHEELHOUSE_GITHUB_TOKEN_FILE` for the operator CLI).
3. Console reads only: the signed-in owner's sign-in token.
4. Another installation's token, for public repositories of an owner without the App: GitHub App tokens reach public
   resources without permissions
   ([GitHub](https://docs.github.com/en/rest/authentication/permissions-required-for-github-apps)).
   Should GitHub refuse it, the call is repeated anonymously.
5. None: public repositories only, 60 calls an hour shared by the host's IP.

- The API signs a 9-minute App token with the key, lists the App's installations (kept 10 minutes) and exchanges the
  owner's for an installation token, kept per owner until 5 minutes before its hour runs out; one refresh at a time.
- Before each runner action that reaches GitHub, the API writes `<runner root>/github/tokens.json` (mode `0600`: one
  token per installed inventory owner, plus one to lend) and passes it as `WHEELHOUSE_GITHUB_TOKENS_FILE`; the runner
  picks by the owner in each API address. Package versions take the token file only; build dispatches never borrow.
- With a credential, a release archive downloads through the API asset endpoint (`application/octet-stream`), so
  private releases work; without one, through the public download URL. A credential goes only to `api.github.com` and
  is stripped from the storage redirect.
- Deployments › Issues shows **GitHub access** for a call refused with 401, 403 or 429, a refused installation token,
  an App set up by half, and an owner without the App only once one of its repositories cannot be seen (a private
  one). A later success clears it. Tokens, the key and response bodies are never logged or returned.
- The key defaults to `<runner root>/github-app/private-key.pem`. A missing key never stops the console: the App stays
  off, Issues says so, and every call falls back.

Owner steps, once:

1. GitHub › Settings › Developer settings › GitHub Apps › New GitHub App; turn the webhook off.
2. Repository permissions: Actions read and write, Contents read-only, Metadata read-only, and Packages read-only;
   nothing else. GHCR still refuses the App's token the packages of a private repository, so those pulls take the token
   file ([registry sign-in](../architecture/architecture.md#registry-sign-in)).
3. Installable on any account. Create it, note its App ID and generate a private key.
4. Install it with all repositories on every owner with private repositories (`sulton-max`, `ocharo-hq`,
   `wow-two-apps`, `wow-two-platform`), and on any other owner Issues names. A public-only owner (the wow2 hub's
   `wow-two`) works without it; installing there too gives its reads its own rate limit.
5. Place the key and set the App ID on each console: [VPS › GitHub access](vps/vps.md#github-access).

Rotate by generating a new key, replacing the file and deleting the old key in GitHub; the key is read at each token
issue, so no restart is needed.

## Tailscale and enrollment

Wheelhouse prod brings a fresh server online and onto the tailnet itself, places a product's databases and private
sites on it, brings an enrolled host to the kit it ships, and applies the tailnet policy after the owner reviews its
diff ([vps.md § Products host](vps/vps.md#products-host)).
The runner's `infra.py` does the work beside `transport.py`; the API starts it, follows its journals and audits each
start and finish. Dev never does any of it: its Servers page reads "prod only".

- **OAuth client:** one owner file, `<runner root>/providers/tailscale/oauth-client.json`, mode `0600`, owned by the
  console's user: `{"clientId": "...", "clientSecret": "tskey-client-..."}`. Its secret is read at each token
  exchange, goes only to `api.tailscale.com`, and is never logged, audited, exported or shown.
- **Keys:** each host and each site node joins with its own single-use, pre-approved, non-ephemeral key, tagged
  `tag:products` or `tag:product-site`, valid 15 minutes. A key travels on SSH stdin into a file under `/run` on the host
  and is removed at once; jobs and audit keep its ID, tag and expiry only.
- **Enrollment:** a one-time Ed25519 key per server (Servers › Add server › Enroll a fresh server) that the owner adds
  to the account the image delivers. Its steps, each recorded live: first contact (host key pinned, checked against the
  owner's fingerprint when given), kit upload (`/app/kit`, its digest recorded), administrator, packages, host layout
  (UFW with 80 and 443 open), tailnet join, node check (its endpoints must hold the public address), prod's runner key
  proven over the tailnet against the first-seen host key, platform (private and public ingress), seal (keys-only SSH,
  no root, public SSH closed, one-time key removed) and verify. A rerun continues from the first step that has not
  succeeded; every step converges. A host enrolled before a kit change takes it with Refresh kit, below.
- **Placement:** Products › a product › Hosting › Place on a server, offered only when the product's `deploy.yml` (read
  at its branch head, `GET /api/products/<product>/descriptor`) calls for something: dev and prod databases when a
  service `needs: [postgres]`, and a node per environment for each private site, named `<product>-<site>` (dev adds
  `-dev`; a site keeps the node its server already registered). Public sites need none; a product that needs nothing
  says so on Hosting. The host converges to this console's kit (as Refresh kit does) and the databases are created once
  (passwords stay in `database.json` on the host). The API and the runner both refuse a site the manifest does not
  declare private, and databases for a product whose services need no PostgreSQL.
- **Host upkeep:** Servers › a server › Kit › Refresh kit, on an enrolled host: the kit uploaded as `wheelhouse-deploy`
  to `/srv/wheelhouse/bootstrap/kit`, its root phase (`enroll-products.sh upkeep`: layout, `platform` network, UFW
  with 80 and 443 public and SSH as it is), PostgreSQL and the ingress, then the public check (80 redirects to https,
  443 answers TLS, no container publishes beyond loopback but the ingress). The root phase runs through the deploy
  account's Docker access, which already grants host control: a one-off privileged container (`alpine`, pinned by
  digest) that enters PID 1's namespaces. The section shows the kit the host runs against this console's and the
  server's public address.
- **Policy:** Servers › Tailnet › Review policy renders `tailnet-policy.hujson` with one host alias and one
  control-path test per products host, diffs it against the live policy and runs Tailscale's validation. Apply sends
  the reviewed ETag and digest; the policy it replaces is kept for revert. A policy whose tests no longer accept
  `tag:control` to `tag:products:22` is refused, whatever Tailscale would save.
- **Recording:** while a job is queued or running, or a finished one is left unrecorded, every `Infra:FollowSeconds`
  (10) the API records finished jobs in the audit trail as the owner who
  started them (`server.enrollment.finish`, `product.placement.finish`, `server.upkeep.finish`), and moves an enrolled
  server's host to its tailnet address and `wheelhouse-deploy`. A job that brought the host to the kit records the
  kit's ingress on the server (`websecure` with `letsencrypt`, `private`, probes on the host; the owner's host pattern
  kept) and its public address: the one already recorded, else the enrollment's, else the host's own public source
  address. A server edit never changes the public address. A job the console starts wakes this follower; with none in
  flight it reads `infra.py finished` once per `Infra:FollowIdleSeconds` (300), which catches a job started by hand,
  and deployed dev, where no job can exist, never runs it.

Owner steps, once (Tailscale admin console):

1. Access controls: paste [tailnet-policy.hujson](vps/tailnet-policy.hujson) once, so `tag:wheelhouse` exists. From then
   on Wheelhouse applies it.
2. Settings › OAuth clients › Generate: scopes `auth_keys` (write), `devices:core:read` and `policy_file` (write, which
   reads too), tag `tag:wheelhouse`. Nothing else: no device writes, DNS or users.
3. On the control host, as the prod console's own user:

```sh
sudo docker exec -i wheelhouse-prod-console-1 sh -c 'umask 077; mkdir -p /data/deployments/providers/tailscale;
  cat > /data/deployments/providers/tailscale/oauth-client.json'   # paste the JSON, then Ctrl-D
```

Rotate by generating a new client, replacing the file and revoking the old one. The rig rehearses all of it against a
fake Tailscale API: `rehearse.py enroll` ([Local server](#local-server)).

## Operator and API commands

From the repository root, using the same inventory as the dashboard:

```sh
R=engineering/codebase/wheelhouse.runner-services/transport.py
python3 $R targets  --root /path/to/inventory
python3 $R releases --root /path/to/inventory
python3 $R check    --root /path/to/inventory --target foreverpin-dev --bundle <artifact-id>
python3 $R submit   --root /path/to/inventory --target foreverpin-dev --bundle <artifact-id> --actor operator [--reason "…"]
python3 $R status   --root /path/to/inventory --job <returned-id>
python3 $R jobs     --root /path/to/inventory
python3 $R state    --root /path/to/inventory --target foreverpin-dev
python3 $R topology --root /path/to/inventory --target foreverpin-dev
python3 $R reconcile --root /path/to/inventory --target foreverpin-dev --job <active-id> --actor operator
python3 $R vitals   --root /path/to/inventory [--target foreverpin-dev]
python3 $R stats    --root /path/to/inventory --days 30
python3 $R logs     --root /path/to/inventory --target foreverpin-dev --service management --tail 200
python3 $R control  --root /path/to/inventory --target foreverpin-dev --operation stop|start|restart [--service management] --actor operator [--reason "…"]
python3 $R stops    --root /path/to/inventory
```

`check`, `state`, `topology`, `vitals` and `logs` stream the runner over SSH stdin and leave no files on the target.
`logs` returns one service's last 1-1000 container lines, timestamped; nothing stores them.
`control` stops, starts or restarts the release a target runs — one service, or the whole environment without
`--service` (a restart names one) — as a job followed like a deploy; it refuses Wheelhouse's own targets and runs only
while no rollout or other control does. While anything stays stopped (`stops` lists it), every deploy is refused with
`<target> is stopped; start it before deploying`: start it first ([architecture](../architecture/architecture.md#service-controls)).
`submit --confirm <target>` carries the typed confirmation that prod on the local server requires; `submit --fleet <id>`
keeps the fleet rollout that submitted it on the job (the API sets it).

```sh
python3 $R branches --root /path/to/inventory --product foreverpin
python3 $R commits  --root /path/to/inventory --product foreverpin --branch main
python3 $R build    --root /path/to/inventory --product foreverpin --commit <full sha>
```

`commits` marks each commit that has a build. `build` starts the product's build workflow only for a commit without one;
it needs Actions write on the product repository: the GitHub App on its owner, or `WHEELHOUSE_GITHUB_TOKEN_FILE`.
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
| `POST /api/deployments/targets/{id}/controls` | `{"operation":"stop","service":"…","reason":"…","confirm":"…"}` with `X-Wheelhouse-Action: control`; owner only, prod needs `confirm` and `reason`; `202` means queued |
| `GET /api/servers` | Hosts defined in code; registration and deletion return `405` |
| `GET /api/deployments/releases` | Published releases and per-commit builds (`kind`: `release` \| `candidate`) from approved sources |
| `GET /api/deployments/products/{product}/branches` | The product repository's branches |
| `GET /api/deployments/products/{product}/commits?branch=` | A branch's recent commits, each with its build when one exists |
| `POST /api/deployments/products/{product}/builds` | `{"commit":"<sha>"}` with `X-Wheelhouse-Action: build`; `202` means the workflow was started |
| `POST /api/deployments` | `{"target":"…","release":"…","confirm":"…","reason":"…","overrideFreeze":false}` with `X-Wheelhouse-Action: deploy`; `202` means queued, `409` a freeze or gate refused it |
| `GET /api/deployments/automation` | Each product's auto-deploy and auto-promote choices, and the queued automatic deploys |
| `PUT /api/deployments/automation/{product}` | `{"autoDeployDev":false}` with `X-Wheelhouse-Action: automation` |
| `GET /api/deployments/freezes` | The holds on deploys: everywhere, per product, per product environment |
| `GET /api/deployments/products/{product}/usage` | Actions runs and minutes over 30 days, and GHCR package versions (a `read:packages` token); hourly |
| `GET /api/deployments/issues/builds` | Products whose newest finished main build failed, linked to the run (read every two minutes at most) |
| `GET /api/deployments/issues/github` | Owners GitHub refused, owners without the GitHub App, and an App set up by half |
| `GET /api/deployments/releases/{release}/facts` | Whether a release can roll back automatically and declares migrations; reads only the bundle |
| `GET /api/deployments/products/{product}/changelog?from=&to=` | Release notes and compare commits between what a target runs and a release |
| `POST` / `DELETE /api/deployments/freezes` | `{"product":"…","environment":"prod","reason":"…"}` / `?product=&environment=` with `X-Wheelhouse-Action: freeze` |
| `GET /api/deployments/{id}` | The target-owned outcome, with each rollout step and any warning |
| `GET /api/deployments/targets/{id}/services/{service}/logs?tail=200` | One service's last container lines (1-1000), never cached |
| `GET /api/audit?limit=50&before=` | Operator actions, newest first; `before` pages back from a sequence number |
| `GET /api/audit/verification` | Whether every stored audit entry and link still verifies |
| `GET /api/deployments/vitals[?fresh=true]` | Every server's own host load, memory, disks and uptime, and every target's containers, read in parallel; one read serves every tab and the MCP tool for `Deployment:VitalsSeconds` (60), and `fresh=true` (the console's explicit Refresh) reads the hosts now |
| `GET /api/deployments/stats?days=30` | Outcomes, success rate, median rollout and recovery, deploys per UTC day (1–90 days) |
| `GET /api/deployments/vitals/history?hours=24&target=` | Stored readings of the last 1–720 hours, oldest first: one target's, or every server's and target's |
| `GET /api/vaults/{vault}/hygiene` | A standalone vault's secrets and product tokens due for rotation; metadata only |
| `GET /api/deployments/targets/{target}/settings[?release=]` | The environment's keys, sources, types, secret metadata, unapplied keys and scheduled apply; never a secret's value |
| `PUT /api/deployments/targets/{target}/settings` | Saves values in one transaction (action `settings`): one value per field, several as a bulk edit; `generate` makes a secret's value on the server |
| `POST`/`DELETE /api/deployments/targets/{target}/settings/apply` | Applies edited values now or at a set time, or cancels the schedule |
| `GET /api/deployments/targets/{target}/secrets/{key}/versions` | A secret's versions: who, when, fingerprint, note; never a value |
| `POST …/secrets/{key}/restore` · `PUT …/secrets/{key}/expiry` | Restores a version as the newest; sets or clears the expiry |
| `PUT`/`DELETE /api/products/{product}/setting-keys/{key}` | Adds or removes a typed key the owner declares for every environment |
| `GET /api/vault` · `GET /api/vault/issues` | The built-in vault's state, key file path and counts, the root key's fingerprint and checks; sealed and expiring secrets, a due offline copy check, a failed key file check |
| `POST /api/vault/verify` · `POST /api/vault/import` | Decrypts everything (counts only); imports a listed standalone vault over its API |
| `POST /api/vault/offline-copy` | Marks the owner's offline root key copy verified against the key in use's fingerprint, with a reason; owner only |

- A lost SSH response is an unknown outcome; inspect target state before retrying.
- Cookie-authenticated cross-origin writes cannot supply the custom header without an allowed CORS preflight.
- Restarting the dashboard does not terminate a launched target worker.
- Reconciling records who acknowledged the rollout under `<inventory>/reconciled/`.

The API reads its runner settings from the `Deployment` section: `TransportPath`, `Root`, `Python`, `GitHubTokenFile`,
`GitHubAppId` and `GitHubAppKeyFile`.
A background follower observes durable deployment submissions every `Deployment:FollowSeconds` (default 10;
0 disables it) while one is unfinished, at the time the runner names for its next look (`follow` answers `open` and
`nextIn`); a deploy, control or reconcile the console submits wakes it, and with nothing in flight it reads the runner
once per `Deployment:FollowIdleSeconds` (default 300), which catches a rollout `self-deploy.py` or CI started. Each
pass reads at most two due target journals, with a 30-second SSH limit per read. Transport
failures retain the last outcome; failed or unknown reads back off up to five minutes. Retry timing survives a control-plane
restart in `<runner root>/following/`; it never submits, retries or reconciles a deployment. A terminal status is
final only when `completedAt` is present, after rollback or cleanup. Explicit status reads can still observe a later
target-side acknowledgement. Preserve `jobs/`, `observed/`, `reconciled/` and `following/` with the runner root.
Status reads stream the runner with the inventory's target, so an upload the retention removed does not stop them.

Retention runs inside each automation pass and stays bounded: the control host's downloaded bundles that none of each
target's newest 20 submissions, no unfinished one and no target's latest two successes use go once a week old (once a
day and after each finished rollout; imported bundles stay); and once a day each target, two a pass, prunes old
release folders (beyond the newest ten, only once their images are gone; never the running, rollback or active one)
and its project's uploads under `incoming/` (beyond the newest ten, each at least a day old) under its deployment
lock, skipping a target mid-rollout. Every successful rollout prunes the same on its target. Progress lives in
`<runner root>/retention.json`; `ci/uploads/` and `ci/receipts/` are left alone.

A background automation worker runs every `Deployment:AutomationSeconds` (default 120; 0 turns it off): it reads each
auto-deploying product's newest published release and queues a new one for its dev targets, one slot per target with
the latest release winning, and submits a queued deploy once its target runs no rollout, as actor `automation`
([architecture](../architecture/architecture.md#deployment-automation)). Each pass costs one GitHub API call per
auto-deploying product; the [GitHub App](#github-access) keeps the calls within the authenticated limit.
Each pass also looks at the targets inside their watch window (`Deployment:WatchMinutes`, default
10; 0 turns it off): one SSH read a minute per watched target, two per pass at most, recorded under
`<runner root>/watching/` (preserve it with the runner root). A product that auto-promotes sends a release that
passed its dev watch to prod.

A fleet rollout worker runs every `Deployment:FleetSeconds` (default 10; 0 turns it off) while a fleet runs: it reads
the history projection (no target is contacted) for the finished outcome of the product the fleet is on, then submits
the next one through the ordinary deploy path ([architecture](../architecture/architecture.md#deployment-automation)).
A fleet survives a restart: the next pass continues it.

Every rollout records `serviceRollout` in its job record — each service's change against the release the target ran,
the waves it started them in and how each ended — and `interpolation`, a hash of the target variables and settings
paths Compose interpolates; a changed hash counts every service as changed. Each Compose call also takes the
runner-written override `releases/<job>/environment.json`, which gives every service `DEPLOY_ENVIRONMENT`, and, when the
release sets `dotnetEnvironment`, `ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT` (dev `Development`, prod
`Production`). Beside it the runner keeps `releases/<job>/fingerprints.json`: one hash per service of its settings
file's contents and that environment, compared on the next rollout and never copied into a job record.

The job record's `serviceRollout` (the status lane projects it; fields are only ever added):

```json
{ "staged": true, "waves": [["db"], ["api"]], "removed": [],
  "services": [{ "name": "api", "change": "updated", "reason": "image", "dependsOn": ["db"],
                 "from": { "version": "0.5.2", "digest": "3f1c2a9b7d10" }, "to": { "version": "0.5.3", "digest": "9e0d4c1f2a35" },
                 "outcome": "updated", "health": "healthy", "at": "2026-10-19T09:12:03+00:00" }] }
```

| Field | Values |
|---|---|
| `change` | `added` · `updated` · `unchanged` |
| `reason` | Why it changed: `added` · `image` · `definition` (memory, health check, mounts; a `depends_on` change alone is none) · `settings` (its settings file's contents, the environment the runner gives it, or the target's variables or settings paths) · `null` when unchanged |
| `dependsOn` | The services it starts after (Compose `depends_on`) |
| `from` · `to` | The release it ran and runs: `version` (null when the bundle has none) and `digest`, the image digest's first 12 hex digits; `from` is null for an added service |
| `outcome` | `unchanged` (kept running) · `pending` (only while live) · `updated` · `failed` · `not_started` · `rolled_back` |
| `health` | At the end: `healthy` · `unhealthy` · `unknown` (no container, still starting, or the rollout failed before changing anything) |
| `at` | When its outcome was last set, ISO 8601; an unchanged service's is when the converging step that checked it ended; null when no step reached it |

A background sampler reads the vitals each `Operations:VitalsSampleMinutes` (default 5; 0 turns it off), stores each
server's load, memory and fullest disk (a row with no target) and each target's container health in `vitals_samples`,
and deletes readings older than 30 days.

## Rollout steps

Each rollout records its steps in its job record as they start and end, so the dashboard follows a deploy live:

| Step | Covers |
|---|---|
| Render settings | Each settings file the release mounts, from the host file, Wheelhouse's values and what is derived; names keys by source, never a value; a required key no layer sets refuses the deploy here |
| Check target | Lock, settings files, routes, disk, Docker and the release snapshot |
| Snapshot data | Prod only: dumps each database and archives each named volume before anything changes, pausing the writers of a volume that holds a file database (SQLite, LiteDB, LMDB, DuckDB…); a failure fails the rollout |
| Pull images | Every missing image, before any container changes; a private one under a sign-in made for this pull only; another owner's public pinned image anonymously |
| Migrate `<service>` | Each `migrate` item the release declares, in order, on every forward deploy and before the first wave ([§ Migrate step](#migrate-step)); a failure stops the rollout before any service restarts |
| Rollback gate `<service>` | A return to an older release — a deploy of one whose sets sit below the database's level, or the automatic restore after a migrated rollout fails — runs that release's `migrate check` instead; anything but exit 0 refuses, naming the pre-deploy snapshot |
| Start `<services>` | Only when the release changes some services but not all, or its changed services span dependency waves: `compose up --no-deps --force-recreate --wait` of one wave of changed services, after the waves they depend on, so a settings-only change applies and no dependent restarts; a failure leaves later waves unstarted |
| Start containers | `compose up --wait --remove-orphans` within the target's health timeout: the whole release, with `--no-recreate` after the waves (or when nothing changed), so unchanged services keep running untouched, and `--force-recreate` when every service changed in one wave; its detail names how many services changed and how many kept running |
| Verify services | Exact image references, health, and the target's smoke checks |
| Publish sites | The route file for the release's sites |
| Probe sites | Each site through the ingress; a site that does not answer is a warning |
| Verify end to end | Dev only, when the release declares `verify`: its check in a one-off container from the service's release image; a failure is a warning and holds auto-promote |
| Remove unused images | Images only releases older than the newest three used; skipped when there are none |
| Remove old files | Release folders beyond the newest ten whose images are gone, and this project's uploads beyond the newest ten once a day old |
| Roll back to … | The previous release, for a failed rollout that declared rollback compatibility, or whose migrate step ran and whose previous release passed its rollback gate, or a failed gated return (it changed nothing in the database the previous release ran on) |

A step's detail is operator-safe text; a failed step shows the refused rule or failed command, never command output.

## Audit trail

Every operator action lands in `audit_entries`: deploy, reconcile, build, freezes, automation choices, vault changes
and product changes. Automatic deploys record the actor `automation`; a deploy's reason ends its detail.
A pipeline behavior records each audited command's outcome, refusals included, with the operator's login.
Entries hold the action, its subject and operator-safe detail, never a secret value or token.
Each entry is hash-chained to the one before it; the Activity page shows whether the chain still verifies.
The table refuses `UPDATE` and `DELETE`, so rewriting an entry needs someone who can disable its trigger.
An intact chain proves no stored entry was edited, reordered or removed from its middle.
It cannot prove the newest entries were kept; that needs a checkpoint held outside the database.

`vitals` reads each server's `/proc` and filesystem capacity once, from the server itself, so a server with no target
still reports its host; its disks are its targets' roots (or `/srv/wheelhouse` before it runs one) and Docker's data root.
It reads `docker ps`/`inspect`/`stats` for each target's Compose project and never returns container environment,
labels or logs. `stats` reads only the local submission records.
The overview flags: unreadable or locked targets, missing or unhealthy containers, 3+ restarts, disks over 75%
(red over 90%), memory over 90%, load over 150% of CPUs, an unrecovered failed rollout, sealed vaults,
active secrets older than 90 days, and tokens expired, expiring within 14 days or older than 180 days.

## Secrets Vault

Secrets live in Wheelhouse's built-in Secrets Vault ([architecture](../architecture/architecture.md#secrets-vault)):
the `vault` schema of its own database, encrypted under a data key per product that one root key file wraps. Products
get them only from the deploy-time render. Dev Wheelhouse keeps its own vault and key, with test values only.

### The root key file (owner, once per console)

On the control host, as root, for the console the key unseals (`wheelhouse-prod`; `wheelhouse-dev` gets a key of its
own). The console reads `/data/deployments/vault/root.key` unless `Vault:KeyFile` names another path; the deployments
volume lives under `/var/lib/docker/volumes`, which only root reads.

```sh
sudo -i
volume=$(docker volume inspect --format '{{.Mountpoint}}' wheelhouse-prod_deployments)
install -d -m 0700 -o 1654 -g 1654 "$volume/vault"
# Keep the standalone vault's master key (same key: its import needs no second key), or make a new one:
openssl rand -base64 32 > "$volume/vault/root.key"          # or: cp <standalone master_key> "$volume/vault/root.key"
chown 1654:1654 "$volume/vault/root.key" && chmod 0400 "$volume/vault/root.key"
```

- Copy the key offline at once (the owner's password manager, plus one printed or offline copy): a lost key makes
  every secret unrecoverable. It never goes into a backup beside the database.
- Without the file the vault is sealed: the console says so under Secrets and in Issues, metadata still reads, and a
  deploy that needs secrets waits. Placing the file unseals it without a restart; a key from another vault reads as
  `mismatch` and stays sealed.
- Check it: Secrets → Built-in vault → *Check every secret decrypts*, or
  `docker exec <console> dotnet Wheelhouse.Api.dll vault status`.

### Checking the root key

The console checks the key file at start and every `Vault:KeyCheckMinutes` (60): present, readable by the console's user
only (mode `0400` or `0600`, its owner the console's user), and opening the canary. A failure shows under Secrets and in
Deployments › Issues until a check passes.

The offline copy is checked by the owner, every `Vault:OfflineCopyDays` (90) and after any change of key. Issues remind
once something is encrypted under the key. The key never goes into a page, a command argument or a message:

1. Secrets → Built-in vault shows the key in use's fingerprint (20 hex digits in groups of four).
2. On the machine that holds the copy, print the copy's fingerprint. The key comes from stdin; without the redirect, a
   hidden prompt reads it:

```sh
python3 -c 'import sys,base64,hashlib,getpass;t=getpass.getpass("Root key: ") if sys.stdin.isatty() else sys.stdin.read();k=base64.b64decode(t.strip(),validate=True);assert len(k)==32,"not a 256-bit key";h=hashlib.sha256(b"wheelhouse.vault.root-key.v1\0"+k).hexdigest()[:20];print(" ".join(h[i:i+4] for i in range(0,20,4)))' < root.key
```

3. Equal: *Offline copy verified*, saying how you checked it (audited). Different: the copy is not this vault's key —
   make the offline copy again from the control host's file before anything else.

### Importing the standalone vault

Only stores holding real values are imported; rehearsal fixtures are re-seeded instead. Import, check, then retire.

- **Over its API** (current values; each arrives as version 1 noting its old version): while the inventory still lists
  the vault with its `vaults/<vault>/password`, Secrets → Built-in vault → *Import from <vault>*, or
  `docker exec <console> dotnet Wheelhouse.Api.dll vault import --from-vault <vault>`.
- **From its database** (every version, with its author and time, under the same master key): restore a dump of the
  vault's database where the console can reach it, then pass its connection string on stdin, never as an argument:

```sh
docker exec -i <console> dotnet Wheelhouse.Api.dll vault import --from-db < connection.txt
# a different master key than the root key file: add --source-key-file <path inside the container>
```

- Namespaces named `{product}-{environment}` of a product Wheelhouse knows are imported; others are listed and left
  alone; a secret already in the built-in vault is skipped, so a second run is safe. Every value is decrypted again
  before the import commits, and any failure keeps nothing. The output and the audit entry hold counts only.
- Then: `vault verify` (or the console's check), deploy each environment once, and retire the standalone vault —
  delete its rows under Servers, remove `vaults/<vault>/password`, stop its container, keep its dump and key 30 days.
- `Vault:Mode` = `external` returns deploys and saves to the standalone vaults for one release, should the built-in
  vault need to step back; versions, expiry and types stay with the built-in mode.

### Backups and restore

- Wheelhouse's own backup is its database: the `vault` schema rides along encrypted. The root key file is never in that
  backup; it lives only in the deployments volume and the owner's offline copy. An off-host backup of the deployments
  volume leaves `vault/` out.
- A prod deploy's pre-deploy snapshot copies named volumes on the control host, the deployments volume included; those
  snapshots stay in the deploy account's mode-`0700` folder and never leave the host.
- After a restore: place the key file, then `vault verify`. It decrypts the canary — a random value made on the vault's
  first write — and every version of every secret, and names any that fail; nothing passes on a database alone.

## Local server

`engineering/deployment/rehearsal/` runs the local server: a disposable SSH target, a Traefik ingress, a registry,
a product database and, when a `secrets-vault:local` image exists, a standalone vault to import from. The console and an
IDE run write the rig's own built-in vault key (`state/inventory/vault/root.key`) on start and seed ForeverPin's test
secrets into it (`catalog.py`'s `LOCAL_SECRETS`). It hosts ForeverPin's `dev` and `prod` at
once. Dev is the default; prod deploys on demand. `WHEELHOUSE_REHEARSAL=1` exposes the local
targets, vault and imported bundles; a deployed control plane never sets it. The rig's databases run PostgreSQL 18 on
volumes named for the major (`*-pg18`): a rig last run on 16 starts empty and keeps its 16 volumes, which
`docker volume rm` drops once nothing there is needed.

```sh
cd engineering/deployment/rehearsal
python3 rehearse.py run                                       # server, dev candidate, settings, check, deploy to dev
python3 rehearse.py bundle --tag v0.0.1-local.1               # a release, which prod also takes
python3 rehearse.py deploy --env dev --tag v0.0.1-local.1       # the dev pass prod requires
python3 rehearse.py settings --env prod --tag v0.0.1-local.1
python3 rehearse.py deploy --env prod --tag v0.0.1-local.1 --confirm foreverpin-prod
python3 rehearse.py bundle --tag v0.0.1-broken.1 --broken     # a release that fails after replacement
python3 rehearse.py state --env dev
python3 rehearse.py self                                      # Wheelhouse builds and deploys itself to wheelhouse-dev
python3 rehearse.py self-deploy                               # its release, migrate step and dev check, then self-deploy.py
python3 rehearse.py harden                                    # harden-control.sh on a real Ubuntu host and UFW
python3 rehearse.py products                                  # the products-host kit on a second local host
python3 rehearse.py networks                                  # fixed platform addresses survive dynamic containers
python3 rehearse.py enroll                                    # a fresh Ubuntu 26.04 host enrolled, placed, policy applied
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
- `products` starts a products host with its own Docker daemon (SSH `127.0.0.1:2223`), laid out by
  `bootstrap-products.sh`'s own steps; `prepare.py --server products` creates its platform, ForeverPin's databases and
  site nodes. It pins and accepts a runner key and deploys the newest healthy release to `foreverpin-dev` over SSH from
  its own inventory (`state/products/inventory`), as a Contabo server whose ingress and public address Refresh kit
  recorded (its address on the rig's network) and whose target names no site host.
- Its public sites answer on their baseline hosts from another container on the rig's network: 80 redirects to https
  and keeps the HTTP-01 challenge path, 443 routes each host, 8080/8081/8443/18080 stay closed, and the rendered
  settings name the baseline hosts. Its ACME resolver points at a CA that never answers (`WHEELHOUSE_ACME_CA` in
  `host.env`), so Traefik's log shows the `letsencrypt` order for each baseline host and no certificate is issued:
  a real HTTP-01 validation needs a public address. A stand-in prod console proves `authorize-runner.sh --remote` and
  `--accept` at a stand-in tailnet address.
- Then ForeverPin becomes a product of two repositories: the release is cut into one bundle per service (`app`:
  management, `go`: redirect), as two repositories would publish them; `transport.py compose` (local server only) makes
  them a release set, `app-<release>_go-<release>`, which deploys over the single-repository release with no service
  restarted and both baseline hosts still answering.
- A password-protected local registry stands in for GHCR: the deploy account signs in through `prepare.py`'s GHCR
  sign-in, and the runner's SSH session pulls a private image that root, without a sign-in, cannot.
- Packages, the firewall, unattended updates and the tailnet need a real Ubuntu host; `products` leaves them out, and
  `enroll` covers them.
- `networks` starts dynamic containers on the products host's `platform` before Traefik, which still gets `.2`, and has
  `repair-networks.sh` give a scratch network made without the dynamic range one, its fixed address kept.
- `enroll` builds the console image and runs its own runner (`enroller`, on stand-in public and tailnet networks)
  against `fresh`: Ubuntu 26.04 with systemd, root signing in with the one-time key only, as a root-only image is
  ordered. The enrollment installs Docker, Tailscale and UFW for real, then seals the host; the rig then checks that the
  owner's administrator signs in over the tailnet, root and public SSH do not, and the runner reads the host. It places
  ForeverPin's databases and site, and reviews, applies and reverts the tailnet policy.
- Tailscale is the one stand-in there: `fake_tailscale.py` answers the OAuth client, keys, devices and policy calls,
  and the host's stub CLI and site-node image (`fresh/`) spend each minted key at it, once. On an arm64 machine the
  host is arm64, which only the rig's switch lets the kit accept; real hosts stay amd64.

### From an IDE

`python3 rehearse.py dev` starts the server plus a loopback database (`127.0.0.1:15432`) and, when it is missing,
writes `Wheelhouse.Api/appsettings.Local.json` (gitignored, never in the image) pointing at it, with
`Deployment:Rehearsal` = `host` and the runner paths resolved from the project folder. Only a run whose launch profile
sets `LOCAL_SETTINGS=true` reads that file; `appsettings.Development.json` holds no local value, since deployed dev
reads it too. Run `Wheelhouse.Api` with the `https` launch profile and open `https://localhost:8210`; the header's
`Local server` badge marks an instance that deploys to the local server, not real hosts. Sign-in uses the API
project's user-secrets (`Identity:GitHub`, callback `https://localhost:8210/api/identity/callback`).
The build compiles the SPA with the Node pinned in `wheelhouse.frontend-services/.nvmrc` when nvm has it, so a Rider
launched from the Dock builds even when its PATH holds an older system Node.

`Deployment:Rehearsal` is the only switch: the API sets `WHEELHOUSE_REHEARSAL` for the runner from it, so a deployed
control plane cannot be pointed at the local server by environment alone. The runner (`transport.py`, `infra.py` and
their `ssh`, `scp` and `ssh-keygen` children) runs through the be sdk process client and inherits only an allowlist of the
API's environment — `RunnerProcess.InheritedVariables`: `PATH`, `HOME`, the locale, the temporary folder, `TZ`, `USER`,
`LOGNAME`, `SSH_AUTH_SOCK`, the interpreter's `PYTHON*` setup, `SSL_CERT_FILE`/`SSL_CERT_DIR` and the proxy variables.
The API sets every `WHEELHOUSE_*` value and `REHEARSAL_STATE` per call; a variable the runner starts reading must be set
there or added to that list, or the runner never sees it.

### Local console — the whole system before a VPS

`python3 rehearse.py console` adds Wheelhouse itself: the production image (`wheelhouse:local`), its own
PostgreSQL, migrated first by the `console-migrate` one-shot, and the local inventory mounted at `/data/deployments`.
It runs as `Production` at
`http://localhost:18210` and drives the target over SSH (`target:22`) and the vault (`http://vault:8080`) by service
name, the way it will drive a VPS; `Deployment:Rehearsal` = `network` selects that view and `Deployment:RehearsalState`
carries the host path for settings files. A transport run by hand inside the console needs
`WHEELHOUSE_REHEARSAL=network REHEARSAL_STATE=<host state path>`, because only the API sets them for its runner.

One-time sign-in setup, on the GitHub account that will operate Wheelhouse:

1. GitHub → Settings → Developer settings → OAuth Apps → New OAuth App.
2. Homepage `http://localhost:18210`; callback `http://localhost:18210/api/identity/callback`.
3. Put the client id and a new client secret in `rehearsal/state/console/appsettings.Target.json` (`Identity:GitHub`).
4. Check `Identity:AllowedGitHubLogins` names that account, then run `python3 rehearse.py console` again.

Use a Chromium-based browser: it accepts the `Secure` session cookie on `http://localhost`.
A separate OAuth app with the production callback serves the VPS; the local one stays local.

With a bundle and each environment's settings written, the console lists them for `foreverpin-dev` and
`foreverpin-prod`. The `v0.3` Verification iteration is the test plan. `rehearse.py down` stops everything; `--volumes` also
drops the console's database, the vault's data and the target's state.

## Target state and recovery

```text
/srv/wheelhouse/<product>-<environment>/
  lock
  active.json
  current.json                  release, service versions and site URLs of the verified rollout
  migrations.json               each migration set's last reading: level, newest label, state, release, source
  stopped.json                  what the owner stopped (the environment or services); deploys refused while present
  jobs/<job-id>.json
  releases/<job-id>/{release,compose}.json
  releases/<job-id>/images-removed    marks an older release whose images were removed
  snapshots/<job-id>/           prod only: <host>-<database>.dump, <volume>.tar.gz, snapshot.json (newest 5 kept)
/srv/wheelhouse/ingress/<product>-<environment>.yml   Traefik routes for the verified release's sites
```

Pulls finish before replacement. Success requires healthy containers with the exact image references and configured smoke responses.
Failed image pulls preserve the running release. A failed rollout restores prior images only if the incoming bundle explicitly
declares `rollbackCompatible: true`. The default release generator leaves this false. A rollout that ran its migrate step
asks the previous release's own `migrate check` instead: exit 0 restores, anything else leaves the failure for a
reconcile, naming the snapshot; a previous release without a `migrate` item falls back to `rollbackCompatible`. A
failed gated return (a return to an older release that passed its rollback gate) restores the release it left either
way: the return checked and changed nothing, so that release's own `migrate check` decides, or, without one, it simply
returns.

### Migrate step

A release whose `deploy.yml` declares `migrate` (pipelines `v0.12.0`) carries `migrate` (`service`, `command`,
`timeoutSeconds`), `migrations`, `migrationSets` (each item image's `migrate sets --json`) and `extensions`; a bundle
without them deploys as before. Spec: `wow-two-ws/ideas/migrations-analysis-2026-10/migrations-spec.md` § 3.4.

- Each item runs once per forward deploy, after the pull and before the first wave, whatever changed: a one-off
  `docker run --rm` named `<project>-migrate-<service>-<job 8>`, label `wheelhouse.migrate=<project>`, the service's
  release image with `--pull never`, the item's command as the entrypoint, the service's environment, mounts (its
  settings file; named volumes that exist) and memory, the platform network, every capability dropped, the image's user.
- The migrate key: `Migrations__ConnectionString` from the target's `database.json`, for a service that needs
  PostgreSQL, passed by name in the docker client's environment; it never reaches an argument, a record or a log. Without
  a database the sets connect through the service's own settings.
- Exits: `0` applied or current; `1` refused, `2` failed, a crash or Docker unable to start it fail the step; `3` (the
  set's lock stayed held) retries once after 5 s, then fails busy; past the item's timeout the container is stopped and
  the step fails timed out. Each failure leaves the previous release serving and needs no reconcile.
- The job keeps per item `status`, `exitCode`, `seconds`, `attempts` and per set what the last `migrate-result` stdout
  line said (state, levels before and after, applied labels with kind, rollback floor, background, lock wait); stderr
  and every other line are discarded. A missing or malformed line is tolerated: the exit decides.
- `migrations.json` beside `current.json` keeps each set's last reading (level, newest label and kind, state, release,
  job, source, and the pre-deploy snapshot of the rollout that last applied to it); the target state carries it and the
  product page shows it. Check now (`GET /api/deployments/targets/{target}/migrations`) runs `migrate check` from the
  running release's images under the deployment lock, 60 s per item.
- A return to an older release is gated, never migrated: a release whose `migrationSets` level for a set is below the
  database's runs `check`; a release without `migrate` leaving one with it trusts the running release's
  `rollbackCompatible`, with a warning, or refuses.
- Placement creates a product's `extensions` as the platform PostgreSQL's superuser in both databases (`prepare.py
  --extension`); a rerun adds the ones `deploy.yml` gained.

Before a prod rollout pulls anything, the runner takes a restore point in `snapshots/<job-id>/`: `pg_dump --format
custom` of each PostgreSQL database a service that `needs: [postgres]` connects to (every Npgsql `ConnectionStrings`
value in its settings file, once per host and database), and a `tar.gz` of each named volume the release's Compose
declares that already exists. Both run in `postgres:18-alpine` (the platform's own image once a host runs 18; its
pg_dump also reads a host still on 16; a target's `snapshot` entry may name another `image` and `keep`, 1-50):
pg_dump on the platform network as the runner's user, with the password in `PGPASSWORD` only, and tar as root, handing
the archive to the runner's user. The target keeps the newest five.
A failed snapshot removes what it wrote and fails the rollout before any image is pulled, so the target needs no
reconcile. The job's `snapshot` names the folder, each file and the restore commands, and the deployment page shows
them:

```sh
docker stop $(docker ps --quiet --filter label=com.docker.compose.project=<product>-prod)   # volumes only
export PGPASSWORD=...   # the database user's password, from the service's settings file
docker run --rm --network <platform> -e PGPASSWORD -v <snapshot>:/snapshot:ro postgres:18-alpine \
  pg_restore --clean --if-exists --no-owner --host <host> --port 5432 --username <user> --dbname <db> /snapshot/<file>.dump
docker run --rm -v <volume>:/volume -v <snapshot>:/snapshot:ro postgres:18-alpine \
  sh -c 'find /volume -mindepth 1 -delete && tar -xzf /snapshot/<volume>.tar.gz -C /volume'
```

A snapshot is a restore point, not a backup: it lives on the same disk. Database restore is never automatic. Image rollback cannot undo a destructive migration or data written after a backup.
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
11. Deploy dev, verify real URLs and provider callbacks, restore a backup, promote the same image digests to prod.

A VPS that hosts products only follows [VPS › Products host](vps/vps.md#products-host): its bootstrap, `prepare.py`
and `authorize-runner.sh` script steps 3–7, while the fingerprint and settings values stay with the owner. Its public
sites answer on baseline hosts through its ingress on 80 and 443 until a domain is bound; private ones on the tailnet.
Existing product containers keep serving without Wheelhouse. A new VPS is a Servers entry plus its credential files; it uses the same product release.
Data relocation remains a separately planned copy/restore/cutover operation.

## Backups and launch gates

Before public cutover, create an encrypted off-provider backup of product/control-plane databases, key volumes and required configuration.
Wheelhouse's database backup carries its built-in vault encrypted; the vault's root key is kept apart and copied
offline by the owner ([Secrets Vault](#secrets-vault)), and `vault verify` proves a restore.
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
