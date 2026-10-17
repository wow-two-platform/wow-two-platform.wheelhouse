# Wheelhouse dev VPS

*Last updated: 2026-10-17*

This bootstrap prepares the private control-plane host on Ubuntu 26.04 amd64: `wheelhouse-prod` and
`wheelhouse-dev` side by side, each with its own database and sign-in. GitHub builds the image; the existing
[runner](../../codebase/wheelhouse.runner-services/runner.py) deploys its immutable bundle over pinned SSH.

## Files and boundaries

| Asset | Runs where | Result |
|---|---|---|
| [Host bootstrap](bootstrap.sh) | Verified VPS, administrator | Docker/Compose/Python/OpenSSH, deploy account, network, platform files |
| [Configuration generator](prepare.py) | VPS, deployment account | Each console's database credentials, settings and target JSON |
| [Canary generator](prepare_canary.py) | VPS, deployment account | The canary's dev and prod databases and settings files |
| [Runner authorization](authorize-runner.sh) | VPS, administrator | A console's own runner SSH identity for this host |
| [Identity template](identity.example.json) | Copy to a private VPS file | Real OAuth credentials and owner allowlist, supplied by the operator |
| [Platform Compose](platform.compose.yml) | VPS | PostgreSQL 16, private Traefik ingress and the canary's tailnet nodes |
| [Network and access](network.md) | Reference | Paths in, tailnet policy and tags, Docker networks, verification |
| [Console allowlist](control-only.yml) | VPS ingress folder | Traefik middleware: the console entry points accept only the host itself |
| [Tailnet policy](tailnet-policy.hujson) | Tailscale admin console | Owner devices reach consoles, SSH and canary sites; tagged nodes reach nothing |
| [Network migration](migrate-control-network.sh) | VPS, administrator, once | Moves an existing host onto the `control` and `platform` split |

The scripts do not accept SSH host keys, enroll Tailscale, change firewall/sshd policy, fetch operator
credentials or start Wheelhouse. They are executable preparation assets, not evidence of deployment.
`bootstrap.sh` can rerun on its prepared host; it refuses incompatible engines/networks and never upgrades an
installed package, because a Docker upgrade restarts every container on the host.
`prepare.py` refuses existing or partial credential output instead of rotating a live database password.
Review a partial failure before retrying; never delete a populated PostgreSQL volume to repeat initialization.

## Initial administrator access

1. Obtain the delivered SSH username and initial login through the OVH account/delivery instructions.
   The VPS-delivery email names the administrator `ubuntu`; its credential is separate from the OVH account.
   The account is in OVHcloud's Canada region: sign in at <https://manager.ca.ovhcloud.com/>.
   The EU sign-in (`auth.eu.ovhcloud.com`) rejects the same credentials as an invalid account or password.
   `www.ovh.com/manager` redirects to the EU portal; bookmark the
   [Canada sign-in](https://auth.ca.ovhcloud.com/signin/?onsuccess=https%3A%2F%2Fmanager.ca.ovhcloud.com%2F%23%2Fhub%2F).
   OVH's support email address rejects messages; use authenticated support tickets or the phone line.
2. Compare the SSH host fingerprint against the OVH console before accepting a connection.
   A network `ssh-keyscan` alone is not independent verification. Use a verified `known_hosts` file.
3. Sign in with the verified identity and preserve provider-console recovery access.
   Complete any provider-required initial password change interactively.
4. Create a dedicated CI Ed25519 key outside the repository. Store its private half in the GitHub
   `dev` environment; copy only the public half onto the VPS.
5. Copy this reviewed directory to the VPS through verified SSH. Run:

```sh
sudo bash bootstrap.sh --host-identity-verified \
  --ssh-public-key /path/to/wheelhouse-dev-ci.pub --install-tailscale
```

`wheelhouse-deploy` joins the Docker group, which grants host control. The CI key has `restrict`
in `authorized_keys`: commands and SCP are allowed, forwarding and interactive terminals are disabled.
Keep human administrator access separate. Do not put the provider's initial password into GitHub.

The script uses the official [Docker Ubuntu repository](https://docs.docker.com/engine/install/ubuntu/)
and optional [Tailscale Ubuntu 26.04 repository](https://pkgs.tailscale.com/stable/#ubuntu-2604).
Apply and verify OS security updates separately; a reboot may interrupt services.
It does not silently remove an existing engine or alter SSH/firewall configuration.

## Private network and HTTPS

The full reference is [network.md](network.md). Two external Docker networks keep workloads away from the consoles:

| Network | Subnet, gateway | Members |
|---|---|---|
| `control` | `172.30.1.0/24`, `172.30.1.1` | Both consoles, PostgreSQL `172.30.1.3`, Traefik `172.30.1.2` |
| `platform` | `172.30.0.0/24`, `172.30.0.1` | Product and canary workloads, the canary tailnet nodes, PostgreSQL `172.30.0.3`, Traefik `172.30.0.2` |

A workload shares PostgreSQL and Traefik with the consoles but no network with them. Traefik's console entry
points (`private`, `dev`) also carry the `control-only` middleware, which accepts only loopback and the two
gateways, so a workload that forges a console hostname at Traefik gets 403. Docker refuses conflicting subnet
creation; if a subnet conflicts with host or tailnet routes, review all addresses in these assets together.
The console trusts forwarded headers only from Traefik's `control` address. Consoles still reach the host's SSH at
`172.30.0.1`, which stays a host address on every bridge. An existing host moves with
`sudo bash migrate-control-network.sh`; it reports a workload's 403 and that the console is unreachable.

Enroll the VPS in the operator's actual tailnet, with ordinary OpenSSH retained:

```sh
sudo tailscale up --hostname=wheelhouse --accept-dns=true --accept-routes=false
tailscale status
```

Complete the displayed Tailscale authentication in the operator account. Enrollment supplies the real
`node.tailnet.ts.net` hostname; an example hostname must never become runtime configuration.
Enable MagicDNS and verify the actual hostname resolves on the VPS (`getent ahosts <actual-hostname>`),
because CI verifies the HTTPS endpoint from the server. Authorize only intended operator devices and
CI identities. Configure persistent private HTTPS:

```sh
sudo tailscale serve --bg --https=443 http://127.0.0.1:18080
sudo tailscale serve --bg --https=8443 http://127.0.0.1:18082
sudo tailscale serve status
```

Tailnet HTTPS may require enabling certificates in its admin console. Use Serve, not Funnel.
Serve keys its configuration by the node's full name: after renaming the node, run `sudo tailscale serve reset`
and add both ports again, or TLS handshakes fail with "no webserver configured for name/port".
Install/enroll Tailscale on the laptop and phone used to open the UI. The traffic path is:

```text
Enrolled client -> private HTTPS :443  -> Tailscale Serve -> 127.0.0.1:18080 -> Traefik private :8080
 -> wheelhouse-prod-console:8080
Enrolled client -> private HTTPS :8443 -> Tailscale Serve -> 127.0.0.1:18082 -> Traefik dev :8082
 -> wheelhouse-dev-console:8080
```

Both consoles share one hostname; Traefik keeps them apart by entry point, so their routes never collide.

Traefik trusts forwarded headers only from loopback and the configured Docker gateway.
Verify the actual source address, HTTPS redirects and secure cookies on this host.
Traefik runs as the deployment UID/GID recorded in `platform/host.env`, so it can read runner-owned
mode-0600 routes without privileged capabilities. Its filesystem and route bind are read-only.

Only `127.0.0.1:18080` and `127.0.0.1:18082` are published. Neither PostgreSQL nor the console has a public host port.
[Docker-published ports](https://docs.docker.com/engine/network/port-publishing/) require explicit binding;
UFW alone is insufficient. Verify external denial of app/database ports over IPv4 and IPv6.
Narrow public SSH only after a second verified private SSH session and provider-console recovery succeed.

### Tailnet policy

Tailscale's default policy lets every node reach every other. [tailnet-policy.hujson](tailnet-policy.hujson)
replaces it: owner devices reach the consoles (443, 8443), administrator SSH and the canary sites, and every tagged
node reaches nothing. Paste the policy under Access controls first (a tag must be defined before a machine can
carry it), then tag the control-plane machine `tag:control` (Machines › Edit ACL tags). The policy's tests refuse a
save that would let a canary node or the host reach anything.
Verify from a canary node afterwards:


```sh
sudo docker exec wheelhouse-platform-canary-dev-1 sh -c \
  'echo | timeout 5 tailscale --socket=/tmp/tailscaled.sock nc <control-host> 443 && echo REACHABLE || echo blocked'
```

Before the policy this prints `REACHABLE`; after it, `blocked`.

## Private configuration

Copy `identity.example.json` to a private location accessible to `wheelhouse-deploy`, mode `0600`.
Fill its OAuth client and allowed owner login, without putting the file or values into source/logs.
Each environment has its own OAuth application and identity file. Register the exact callbacks:

```text
prod  https://<actual-node>.<actual-tailnet>.ts.net/api/identity/callback
dev   https://<actual-node>.<actual-tailnet>.ts.net:8443/api/identity/callback
```

Run the generator as the deployment account for dev first, then prod, substituting the actual hostname:

```sh
sudo -u wheelhouse-deploy python3 /srv/wheelhouse/bootstrap/prepare.py --environment dev \
  --identity-file /protected/path/identity.json \
  --hostname '<actual-node>.<actual-tailnet>.ts.net'
sudo -u wheelhouse-deploy python3 /srv/wheelhouse/bootstrap/prepare.py --environment prod \
  --identity-file /protected/path/identity-prod.json \
  --hostname '<actual-node>.<actual-tailnet>.ts.net'
```

The input's parent directories must permit the deployment account to reach it. No password is printed.
Dev initializes PostgreSQL: independent random database-admin and application passwords, applied only to an
empty data volume. Prod joins the running database through `docker compose exec`, its SQL on stdin.
Each environment's role owns only its database, with no superuser, role-creation or database-creation privileges.

| Path | Handling |
|---|---|
| `/srv/wheelhouse/platform/.secrets/` | Mode `0700`; database initialization files readable only through private host paths/mounts |
| `/srv/wheelhouse/config/console-dev.json` · `console-prod.json` | Dev and prod OAuth and DB credentials; read-only files inside a mode `0700` directory |
| `/srv/wheelhouse/config/wheelhouse-dev.json` · `wheelhouse-prod.json` | Mode `0600`; target JSON for CI and the existing runner |
| `/srv/wheelhouse/ingress/` | Runner-generated route files, mounted read-only by Traefik |
| `/srv/wheelhouse/wheelhouse-dev/` · `wheelhouse-prod/` | Runner deployment journals, current bundle and recovery state |

Container-mounted settings/init files are mode `0444` inside mode `0700` directories: host traversal is
private while container UID `1654`/PostgreSQL can read the individual file bind. This matches the existing
[runner settings contract](../deployment.md#runner-installation-and-inventory).
The generated target has `variables.PLATFORM_NETWORK=platform`; prod probes `http://127.0.0.1:18080` on the
`private` entry point, dev probes `http://127.0.0.1:18082` on `dev` and publishes port 8443. It does not replace the console's API-owned `inventory.json`.
The deployed image defaults to ASP.NET `Production` in both; the target environment selects artifact policy only.
Rehearsal is explicitly disabled.

## Start the platform and deploy

Use a new deployment-account login after Docker-group enrollment, or the administrator's `sudo -u`:

```sh
sudo -u wheelhouse-deploy docker compose \
  --env-file /srv/wheelhouse/platform/host.env \
  -f /srv/wheelhouse/platform/compose.yml config --quiet
sudo -u wheelhouse-deploy docker compose \
  --env-file /srv/wheelhouse/platform/host.env \
  -f /srv/wheelhouse/platform/compose.yml up -d --wait
```

Configure GHCR read access for `wheelhouse-deploy` if the image is private; use a protected token with
`docker login ghcr.io --username <registry-user> --password-stdin`. Do not log credentials.
The current release's exact image digest must pull successfully under this same account.

CI uploads the verified bundle and current runner to a job-specific directory and uses
`/srv/wheelhouse/config/wheelhouse-dev.json`. It invokes the existing `validate`, `check`, `launch`
and `status` actions, waiting for a terminal outcome with a nonempty `completedAt`.
The runner owns target locks, image pulling, health checks, routes and recovery. Do not introduce a
second `docker compose up` implementation for Wheelhouse itself.

After initial deployment, sign in and register the actual OVH server and `wheelhouse-dev` target.
Configure Wheelhouse's public release source and approved image repositories. Place its separate runner
SSH identity and verified `known_hosts` under persistent `/data/deployments/ssh/<server-id>/`.
The product image's Docker named volumes preserve `/data/keys` and `/data/deployments`, initially owned
by UID `1654`; preserve them during updates and backups.

## Canary

The canary is the sample app only Wheelhouse dev deploys, to prove each flow before a release is promoted.
Each environment answers on its own tailnet node, so every one has its own name and trusted certificate:

```text
canary-dev.<tailnet>  -> Tailscale container 172.30.0.10 -> Traefik canary :8084 -> wheelhousecanary-dev
canary.<tailnet>      -> Tailscale container 172.30.0.12 -> Traefik canary :8084 -> wheelhousecanary-prod
```

- The tailnet policy owns `tag:canary`; a reusable tagged auth key sits in `/srv/wheelhouse/private/tailscale-canary.env`
  as `TS_AUTHKEY=…`. Each node logs in once and keeps its state in its own volume.
- `prepare_canary.py --tailnet <tailnet>` creates `wheelhousecanary_{dev,prod}` and `config/canary-{env}.json`.
- `sudo bash authorize-runner.sh wheelhouse-dev <server-id>` gives dev Wheelhouse its own key for this host; the
  server's address inside the console is the host's `platform` bridge address, `172.30.0.1`.
- Only Wheelhouse prod deploys products. Dev never receives a products host's key.

### Retiring `test` (2026-10-17)

Products work on `main` only; every release deploys to dev, then is promoted to prod. Wheelhouse's migration
`013-drop-test-environment` deletes each console's test targets on its next boot. The host keeps what the retired
environment left until the operator removes it, as the deployment account in `/srv/wheelhouse/platform`:

```sh
# Install this repository's platform.compose.yml as compose.yml (bootstrap.sh does the same), then drop the orphan node.
docker compose --env-file host.env -f compose.yml up -d --remove-orphans
docker volume rm wheelhouse-platform_canary-test
# The canary's test stack, its route, state, settings and database.
docker compose -p wheelhousecanary-test down
rm -f /srv/wheelhouse/ingress/wheelhousecanary-test.yml /srv/wheelhouse/config/canary-test.json
rm -rf /srv/wheelhouse/wheelhousecanary-test
docker compose --env-file host.env -f compose.yml exec -T postgres psql -U postgres -v ON_ERROR_STOP=1 \
  -c 'DROP DATABASE IF EXISTS wheelhousecanary_test' -c 'DROP ROLE IF EXISTS wheelhousecanary_test'
```

Then remove the `canary-test` machine in the Tailscale admin console (Machines › canary-test › Remove).
`172.30.0.11` stays unassigned.

## GitHub-hosted CI access

Pinned OpenSSH to the verified public VPS address is usable for initial deployment while the app stays
private. Put host/user/key/known-hosts in the GitHub `dev` environment; keep strict host checking enabled.
Standard GitHub-hosted runners have changing broad IP ranges, so a fixed source-IP firewall exception
does not follow them. [GitHub recommends against allowlisting those ranges](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).

Once the tailnet exists, prefer the [Tailscale action](https://tailscale.com/docs/integrations/github/github-action)
with workload identity federation and a narrowly scoped CI tag. The ephemeral runner joins for one job,
then logs out. Limit the tag's network grant to the dev host's SSH port; still use the pinned OpenSSH key.
Configure OIDC trust for the exact repository and `dev` environment/main branch and grant `id-token: write`
only to the deploy job. This requires a real tailnet administrator and federated client configuration;
no example client ID or token is sufficient. Do not install a GitHub job runner on this application VPS.

## Acceptance and recovery

- Verify `runner.py status` reaches `succeeded` with `completedAt`; the container's health alone is insufficient.
- Verify trusted private HTTPS, the real GitHub callback, owner login and an authenticated MCP client.
- Test `/mcp` initialization, tools, permitted reads, missing-key denial and revoked-key denial.
- Restart the console and verify inventory, keys and deployment journals survive.
- Verify an external host cannot reach the console/database, including the VPS IPv6 address.
- Back up PostgreSQL, cookie keys, runner state and protected configuration to encrypted off-provider storage.
- Test restoring into an empty isolated database before treating backups as recovery evidence.
- Keep decryption credentials outside the VPS; choose retention before unattended operation.

Database migrations run at application startup. The runner may roll back images only when the bundle
declares schema compatibility; it does not restore a database. A failed schema-changing deploy requires
reviewed forward recovery or database restore. Provider daily backup is supplementary protection.
