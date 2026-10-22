# Wheelhouse dev VPS

*Last updated: 2026-10-21*

This bootstrap prepares the private control-plane host on Ubuntu 26.04 amd64: `wheelhouse-prod` and
`wheelhouse-dev` side by side, each with its own database and sign-in. GitHub builds the image; the existing
[runner](../../codebase/wheelhouse.runner-services/runner.py) deploys its immutable bundle over pinned SSH.
A second VPS that runs products only is wired by [§ Products host](#products-host).

## Files and boundaries

| Asset | Runs where | Result |
|---|---|---|
| [Host bootstrap](bootstrap.sh) | Verified VPS, administrator | Docker/Compose/Python/OpenSSH, deploy account, network, platform files |
| [Administrator script](create-admin.sh) | Root-only products VPS, root, once | A sudo administrator that signs in with root's SSH keys; sets no password |
| [Products bootstrap](bootstrap-products.sh) | Verified products VPS, administrator | The same, plus SSH keys only, firewall, unattended security updates and the tailnet |
| [Enrollment phases](enroll-products.sh) | Fresh products VPS, as root, run by Wheelhouse prod | The bootstrap's steps one phase at a time, a minted key's tailnet join, the seal; `upkeep` brings an enrolled host to the kit |
| [Network repair](repair-networks.sh) | Either host, administrator, in a maintenance step | Recreates `control`/`platform` with the dynamic range `.128/25`, fixed addresses kept |
| [Configuration generator](prepare.py) | VPS, deployment account | Each console's database credentials, settings and target JSON; with `--server products`, a products host's platform, databases, site nodes and GHCR sign-in |
| [Canary generator](prepare_canary.py) | VPS, deployment account | The canary's dev and prod databases and settings files |
| [Runner authorization](authorize-runner.sh) | VPS, administrator | A console's own runner SSH identity for this host; with `--remote` and `--accept`, prod's identity for a products host |
| [Identity template](identity.example.json) | Copy to a private VPS file | Real OAuth credentials and owner allowlist, supplied by the operator |
| [Platform Compose](platform.compose.yml) | VPS | PostgreSQL 16, private Traefik ingress and the canary's tailnet nodes |
| [Products Compose](products.compose.yml) | Products VPS | PostgreSQL 16, Traefik (a private entry point, and public 80 and 443 with HTTP-01 certificates) and the site nodes `prepare.py` renders in |
| [Site Serve config](tailscale-serve-products.json) | Products VPS, each site node | Tailnet HTTPS `:443` to Traefik's private entry point |
| [Network and access](network.md) | Reference | Paths in, tailnet policy and tags, Docker networks, verification |
| [Console allowlist](control-only.yml) | VPS ingress folder | Traefik middleware: the console entry points accept only the host itself |
| [Tailnet policy](tailnet-policy.hujson) | Tailscale admin console | Owner devices reach consoles, SSH, canary and product sites; the control host reaches a products host's SSH only |
| [Network migration](migrate-control-network.sh) | VPS, administrator, once | Moves an existing host onto the `control` and `platform` split |
| [Break-glass self-deploy](self-deploy.py) | Control host, deployment account | Status, deploy and rollback of both consoles when none answers, with the release image's own runner |
| [Host hardening](harden-control.sh) | Control host, administrator, after the self-deploy trial | Firewall, keys-only SSH, unattended security updates; then tailnet-only SSH and pinned runner keys |

The control-host scripts do not accept SSH host keys, enroll Tailscale, fetch operator credentials or start Wheelhouse;
only `harden-control.sh` changes firewall and sshd policy, when the owner runs it. The products bootstrap does set SSH, firewall and tailnet policy; neither accepts a
host key or fetches a credential. They are executable preparation assets, not evidence of deployment.
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
4. Only for CI's fallback deploy path: create a dedicated CI Ed25519 key outside the repository, store its private
   half in the GitHub `dev` environment and copy only the public half onto the VPS. A host that Wheelhouse deploys
   itself to ([deployment.md § Wheelhouse deploys itself](../deployment.md#wheelhouse-deploys-itself)) needs none.
5. Copy this reviewed directory to the VPS through verified SSH. Run:

```sh
sudo bash bootstrap.sh --host-identity-verified --install-tailscale \
  [--ssh-public-key /path/to/wheelhouse-dev-ci.pub]
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
replaces it: owner devices reach the consoles (443, 8443), administrator SSH, the canary sites and the product sites.
The one tagged path is the control host to a products host's SSH ([§ Products host](#products-host)); every other
tagged node reaches nothing. Paste the policy under Access controls first (a tag must be defined before a machine can
carry it), then tag the control-plane machine `tag:control` (Machines › Edit ACL tags). The policy's tests refuse a
save that would let a canary, products or site node reach the control host, or the control host reach more than a
products host's SSH. Once Wheelhouse prod holds the Tailscale OAuth client, later changes go through its reviewed diff
(Servers › Tailnet › Review policy) instead of a paste.
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

## GitHub access

Each console reaches GitHub through the GitHub App (the owner's installation, or a lent one for public repositories),
then the token file, then anonymously ([order and App setup](../deployment.md#github-access)). Without the App, both
consoles run and fall back to anonymous reads. Once the App exists, for `wheelhouse-dev` and then `wheelhouse-prod`:

```sh
# The key, as the console's own user, into its persistent runner volume (a missing key leaves the App off).
sudo docker exec -i wheelhouse-dev-console-1 sh -c \
  'umask 077; mkdir -p /data/deployments/github-app; cat > /data/deployments/github-app/private-key.pem' < app.pem
# The App ID, in the console's settings file: "Deployment": { …, "GitHubAppId": "<App ID>" }.
sudoedit /srv/wheelhouse/config/console-dev.json
sudo docker restart wheelhouse-dev-console-1
```

- The key sits in the `/data/deployments` volume beside the runner's SSH identities: a console's containers come from
  its release bundle, whose descriptor mounts named volumes and the settings file only.
- The settings file is read at startup, hence the restart; the key is read at each token issue.
- Delete the downloaded `app.pem` once both consoles hold it; Deployments › Issues names any owner still without access.

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
environment left until the operator removes it: copy this folder to the host and run `sudo bash retire-test.sh`,
which does the following, as the deployment account in `/srv/wheelhouse/platform`:

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

## Products host

A second VPS runs products only, Ocharo first: PostgreSQL, Traefik and one tailnet node per private site. No console
and no Wheelhouse database run there. Only Wheelhouse prod deploys to it, over the tailnet; dev never receives its key.
Public sites answer on the host's own ingress, ports 80 and 443, with Let's Encrypt HTTP-01 certificates: on their
baseline host `<product>[-<site>][-dev].<ip-with-dashes>.sslip.io` until a domain is bound (decision 68). Private sites
answer on their tailnet nodes only. SSH stays tailnet-only.

Wheelhouse prod wires it from the console ([§ From the console](#from-the-console)); the hand-run steps after it stay
the fallback and the reference for what each console step does.

```text
Visitor -> https://<product>-dev.<ip>.sslip.io -> host :443 -> Traefik websecure (:8443, letsencrypt)
  -> <product>-<environment>-<service>:<port> on `platform`     (http :80 -> Traefik web: HTTP-01, else 301 to https)
Owner device -> https://<site-node>.<tailnet>.ts.net -> site node Serve :443 -> Traefik private 172.30.0.2:8080
  -> <product>-<environment>-<service>:<port> on `platform`
Prod console (control host) -> tailnet -> <products-tailnet-ip>:22 -> wheelhouse-deploy (prod's key, restrict)
```

| Path on the products host | Holds |
|---|---|
| `/srv/wheelhouse/platform/compose.yml` | Rendered by `prepare.py`: `products.compose.yml` plus one service per site node |
| `/srv/wheelhouse/platform/.secrets/postgres-password` | PostgreSQL's administrator password, created once |
| `/srv/wheelhouse/platform/sites.json` · `sites/<node>/` | Site nodes and their fixed addresses (`172.30.0.64/26`); each node's tailnet state |
| `/srv/wheelhouse/platform/acme/` | Traefik's ACME account and certificates (`acme.json`, mode `0600`, the deploy account's) |
| `/srv/wheelhouse/platform/host.env` | The deploy account's UID/GID for Traefik; an optional `WHEELHOUSE_ACME_CA=<staging directory URL>` line for a trial against Let's Encrypt staging, kept across reruns |
| `/srv/wheelhouse/bootstrap/kit/` | The kit Refresh kit uploaded, as the deploy account; its root phase lays it out from here |
| `/srv/wheelhouse/config/<product>-<environment>/database.json` | Owner-only role, password and connection string |
| `/srv/wheelhouse/config/<product>-<environment>/<service>.target.json` | Each service's settings, rendered by Wheelhouse at every deploy; mode `0444` inside the mode-`0700` folder |
| `/srv/wheelhouse/private/` | Owner-placed `tailscale-sites.env` (site nodes' auth key) |
| `/srv/wheelhouse/ingress/` | The runner's routes, read by Traefik |

| Step | Who |
|---|---|
| Buy, verify the host key, administrator key, tailnet policy, auth key, GitHub token | Owner |
| The administrator account and its sudo access, on a root-only image | `create-admin.sh`; the owner sets its password |
| Packages, deploy account, kit, `platform` network, SSH policy, firewall, updates, tailnet join | `bootstrap-products.sh` |
| PostgreSQL password, Compose file, databases, settings folders, site nodes | `prepare.py --server products` |
| Prod's runner identity, its host-key pin, its authorization and the proof it connects | `authorize-runner.sh` |
| Server and targets, settings values (asked for by the first deploy), deploys | Owner, in Wheelhouse prod |

The local rehearsal (`python3 rehearse.py products`) proves every scripted step except packages, the firewall, updates
and the tailnet, which need a real Ubuntu host ([Local server](../deployment.md#local-server)).

### From the console

Once per tailnet, the owner connects Tailscale ([deployment.md § Tailscale and enrollment](../deployment.md#tailscale-and-enrollment)):
the policy pasted once, an OAuth client with `auth_keys`, `devices:core:read` and `policy_file` on `tag:wheelhouse`,
its file placed in prod's volume. Then, in Wheelhouse prod:

1. **Servers › Add server › Enroll a fresh server:** slug, name, provider, region, public IP. Wheelhouse shows a
   one-time public key (`wheelhouse-enroll-<slug>`).
2. **Give the key to the image's account:** at order time (Contabo's SSH key field), or once to an image already
   delivered: `ssh root@<public-ip> 'cat >> ~/.ssh/authorized_keys'` with the key on stdin (`ubuntu@` on OVH). The
   owner's own key may stay there too.
3. **Enroll:** sign-in account (`root`, or `ubuntu` on OVH), administrator (`ubuntu` where it exists, kept as it is;
   a new one needs your public key and gets no password), tailnet hostname, and, best, the host key fingerprint from the
   provider's console (Contabo VNC: `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub`). Without it the tailnet proves
   the host instead.
4. **Watch the steps.** The job runs from first contact to the seal, about five minutes; a failed step names why, and
   Rerun continues from it. Afterwards the server's host is its tailnet address, root and public SSH are refused, the
   one-time key is gone from both hosts, and your administrator signs in over the tailnet. The server records the
   kit's public ingress and the public IP it was enrolled on; UFW allows 80 and 443.
5. **Products › the product › Hosting › Place on a server:** what its `deploy.yml` calls for — databases when a service
   needs PostgreSQL (Ocharo Platform), a node per private site (`<product>-<site>`) — as steps 4–5 below do by hand; each
   site node joins with its own single-use key. Public sites need no placement: they answer on their baseline hosts, so
   Ocharo Studio has nothing to place.
6. **Servers › Tailnet › Review policy:** the diff adds the new host's alias and control-path test; Apply once
   Tailscale's validation passes.
7. Continue with [step 8](#8-register-and-deploy-owner-wheelhouse-prod) for targets, settings and the first deploy.

### Host upkeep (Refresh kit)

A host enrolled before a kit change takes it from the console, never from a session of yours: **Servers › the server ›
Kit › Refresh kit** (prod only). It runs four recorded steps, audited at start (`server.upkeep`) and finish
(`server.upkeep.finish`):

| Step | What happens on the host |
|---|---|
| Kit uploaded | As `wheelhouse-deploy`: the kit this console ships, into `/srv/wheelhouse/bootstrap/kit/` |
| Kit laid out; firewall | As root: `enroll-products.sh upkeep` lays the kit out, keeps the `platform` network and converges UFW (80 and 443 allowed, SSH exposure as it is) |
| PostgreSQL and the ingress | As `wheelhouse-deploy`: `prepare.py --server products` renders `compose.yml`; Compose recreates Traefik with 80 and 443 (private sites pause for a few seconds) |
| Public entry points | 80 answers with a redirect to https, 443 answers TLS, and no container publishes beyond loopback except the ingress on 80 and 443 |

- Root comes from the deploy account's Docker group, which already grants host control: one
  `docker run --rm --privileged --pid=host` of a digest-pinned `alpine` that enters PID 1's namespaces with `nsenter`
  and runs the kit's `upkeep` phase from the uploaded folder. No other root path exists once the enrollment sealed the
  host.
- On success the server records the kit's ingress (`websecure` + `letsencrypt`, `private`, probes `https://127.0.0.1`
  and `http://127.0.0.1:18080`) and its public address: the one already recorded, else the one it was enrolled on,
  else the host's own public source address. A run that finds none fails at its last step and records nothing.
- The section shows the kit the host runs (from the last refresh that succeeded) against this console's, and the public
  address with where it came from. A placement's platform step runs the same converge.
- Fails safe: a failed step stops the run; every step converges, so Refresh kit again continues.

### 1. Buy and first sign-in (owner)

Any provider works: the kit needs Ubuntu 26.04 amd64, SSH and an out-of-band console. OVH and Contabo are registered
providers in Wheelhouse. Their images differ: OVH delivers an administrator account (`ubuntu`), Contabo delivers `root`
only. `<admin-user>` below is `ubuntu` on OVH, or the name you give `create-admin.sh` on a root-only image.

1. Order the VPS: Ubuntu 26.04 amd64, at least 8 GB RAM and 100 GB disk. The bootstrap refuses any other image.
2. Verify the SSH host fingerprint in the provider's out-of-band console before the first connection (OVH KVM,
   Contabo VNC), as in [Initial administrator access](#initial-administrator-access). That console is also the recovery
   path once SSH is tailnet-only.
3. Put your own key on the account the image delivers, and end with an administrator that you sign in to with that key.
   The bootstrap in step 3 turns password sign-in off.
   - OVH, an administrator is delivered: `ssh-copy-id -i ~/.ssh/<your-key>.pub ubuntu@<public-ip>`, then
     `ssh ubuntu@<public-ip>`.
   - Root-only image (Contabo): add your key to root, then create the administrator once from a root session. The script
     copies root's keys to it and sets no password; set that yourself:

     ```sh
     ssh-copy-id -i ~/.ssh/<your-key>.pub root@<public-ip>
     scp engineering/deployment/vps/create-admin.sh root@<public-ip>:
     ssh root@<public-ip>
     bash create-admin.sh <admin-user>   # on the host: refuses to run without your key on root
     passwd <admin-user>                 # your own password, for the console and sudo
     exit
     ```

     Then `ssh <admin-user>@<public-ip>` with your key. Rerunning the script is safe; it refuses an existing user that
     holds other keys.
4. Give the administrator a local password for the provider's console, the recovery path once SSH is tailnet-only:
   `sudo passwd <admin-user>` on OVH (the `passwd` above did it on a root-only image). SSH never accepts it. Keep it in
   the password manager.

### 2. Tailnet policy and site key (owner, Tailscale admin console)

1. Paste [tailnet-policy.hujson](tailnet-policy.hujson) under Access controls and save; its tests must pass.
   It defines `tag:products` (the host) and `tag:product-site` (the site nodes): owner devices reach the host's SSH and
   the sites on 443, and the control host reaches the products host's SSH, nothing else.
2. Settings › Keys › Generate auth key: reusable, pre-approved, tagged `tag:product-site`. Keep it for step 5.

### 3. Bootstrap (owner runs the script)

```sh
scp -r engineering/deployment/vps <admin-user>@<public-ip>:wheelhouse-kit
ssh <admin-user>@<public-ip>
sudo bash wheelhouse-kit/bootstrap-products.sh --host-identity-verified --tailnet-hostname <products-node>
```

- `tailscale up` prints a sign-in link; open it as the tailnet administrator. The node joins as `tag:products`, so its
  key never expires.
- The script refuses to run before your key signs you in, since it turns password sign-in off. It also turns root
  sign-in off, so on a root-only image the administrator from step 1 is the way in from then on.
- SSH stays public until you close it. From the laptop, open a session over the tailnet and keep it open:
  `ssh <admin-user>@<products-tailnet-ip>`. In that session:
  `sudo bash wheelhouse-kit/bootstrap-products.sh --host-identity-verified --tailnet-only-ssh`.
- Fallback when the tailnet path is lost: sign in as the administrator on the provider's console (OVH KVM, Contabo
  VNC) and run `sudo bash wheelhouse-kit/bootstrap-products.sh --host-identity-verified --public-ssh`
  (or `sudo ufw allow 22/tcp`). The provider's rescue mode is the last resort.
- 80 and 443 are open for the public ingress; Docker publishes nothing else beyond loopback. Apply pending OS
  updates once, in a reboot window:
  `sudo apt-get update && sudo apt-get upgrade && sudo reboot`. After that, security updates install unattended,
  without reboots; Docker and Tailscale upgrade only by hand.
- Every step converges, so a rerun is safe; a rerun keeps the current SSH exposure.

### 4. Platform (owner runs the script)

```sh
sudo -u wheelhouse-deploy -H python3 /srv/wheelhouse/bootstrap/prepare.py --server products
sudo -u wheelhouse-deploy docker compose --env-file /srv/wheelhouse/platform/host.env \
  -f /srv/wheelhouse/platform/compose.yml up -d --wait
```

Rerunning `prepare.py --server products` keeps the PostgreSQL password and renders `compose.yml` again, after a kit
update too: run it after every `bootstrap-products.sh` rerun.

### 5. Products and their sites (owner runs the script)

```sh
sudo -u wheelhouse-deploy -H python3 /srv/wheelhouse/bootstrap/prepare.py --server products \
  --product ocharo-platform --database --site app=ocharo-desk --tailnet <tailnet>.ts.net
sudo -u wheelhouse-deploy -H python3 /srv/wheelhouse/bootstrap/prepare.py --server products \
  --product ocharo-studio --site app=ocharo-studio --tailnet <tailnet>.ts.net
# The site nodes' auth key from step 2: paste TS_AUTHKEY=<key>, then Ctrl-D.
sudo -u wheelhouse-deploy sh -c 'umask 077; cat > /srv/wheelhouse/private/tailscale-sites.env'
sudo -u wheelhouse-deploy docker compose --env-file /srv/wheelhouse/platform/host.env \
  -f /srv/wheelhouse/platform/compose.yml up -d --wait
```

- `--database` creates `<product>_dev` and `<product>_prod` (hyphens become underscores), each its own role owning
  only its database, and writes `config/<product>-<environment>/database.json`. Existing credentials are refused.
  Studio keeps SQLite on its volume, so it takes no `--database`.
- `--site <site>=<node>` gives the site a node per environment: `<node>-dev` for dev, `<node>` for prod. The output names
  each site's address, `https://<node>.<tailnet>.ts.net`. A repeated site keeps its node and address.
- Each node appears under Machines as `tag:product-site`; HTTPS certificates must be enabled in the tailnet.

### 6. GHCR read access (owner, on the control host)

Ocharo's images are private, and the products host keeps no registry token: every deploy lends its pull a sign-in from
prod's console, which signs in for that pull only and signs out at once
([registry sign-in](../../architecture/architecture.md#registry-sign-in)). Prod offers the GitHub App's installation
token for the images' owner first, then its token file, whichever GHCR accepts:

```sh
# On the control host, once prod runs v0.5: which credential GHCR grants each Ocharo image.
sudo docker exec wheelhouse-prod-console-1 python3 /app/runner/transport.py registry --root /data/deployments \
  --product ocharo-platform
```

- The App's token: give the App Packages read-only (GitHub App settings › Permissions › Repository permissions) and
  approve the change on each installation; each private package keeps "inherit access from source repository".
- Else the token file: a classic token, scope `read:packages` only, with an expiry, from a dedicated machine account,
  as prod's `Deployment:GitHubTokenFile` (inside its `/data/deployments` volume).
- A host that took the earlier sign-in (`prepare.py --ghcr-user …`) drops it with
  `sudo -u wheelhouse-deploy docker logout ghcr.io`.

### 7. Prod's runner key (owner runs the script on both hosts)

Copy the updated kit to the control host first (`scp -r engineering/deployment/vps <admin>@<control-host>:wheelhouse-kit`).

```sh
# Products host: its host key; the fingerprint must match the one verified in step 1. Save the key line on the
# control host as products-host-key.pub.
cat /etc/ssh/ssh_host_ed25519_key.pub; ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub
# Control host: prod's identity for <server-id>, pinned to the products host's tailnet address; prints its public key.
sudo bash wheelhouse-kit/authorize-runner.sh wheelhouse-prod <server-id> --remote <products-tailnet-ip> \
  --host-key products-host-key.pub > wheelhouse-prod-runner.pub
# Products host: copy wheelhouse-prod-runner.pub over (a public key), then authorize it for wheelhouse-deploy.
sudo bash /srv/wheelhouse/bootstrap/authorize-runner.sh --accept wheelhouse-prod-runner.pub
# Control host: the same command again proves prod's console reaches it.
sudo bash wheelhouse-kit/authorize-runner.sh wheelhouse-prod <server-id> --remote <products-tailnet-ip> \
  --host-key products-host-key.pub
```

- The identity lives only in prod's `/data/deployments/ssh/<server-id>/`; an existing one is never replaced.
- `wheelhouse-dev` and any address outside the tailnet (`100.64.0.0/10`) are refused.
- The address, not the MagicDNS name: the console resolves nothing on the tailnet.

### 8. Register and deploy (owner, Wheelhouse prod)

Servers › Add server:

| Field | Value |
|---|---|
| Slug | `<server-id>`, as in step 7 |
| Provider, region | OVHcloud or Contabo, the VPS's region |
| Host · SSH user · SSH port | `<products-tailnet-ip>` · `wheelhouse-deploy` · `22` |
| Scheme · port · certificate resolver | `https` · empty · `letsencrypt` |
| Entry points · private entry points | `websecure` · `private` |
| Host pattern | Empty; public sites take their baseline hosts, private ones name their tailnet nodes |
| Probe · private probe | `https://127.0.0.1` · `http://127.0.0.1:18080` |
| Public address | Not typed: Refresh kit records it, as an enrollment does |

An enrolled server gets all of these from its enrollment or Refresh kit; this table is the hand-run path's.

Products: import Ocharo from its hub catalog (the GitHub App on `ocharo-hq` gives prod its private releases), then add
dev and prod environments for each product:

- Server `<server-id>`, network `platform`, root `/srv/wheelhouse`; no host settings files.
- Sites: the form lists each site the product declares, read-only. A public site shows its baseline host
  (`ocharo-studio-dev.203-0-113-10.sslip.io` in dev); a private site shows its node: `<node>-dev.<tailnet>.ts.net`
  (dev) or `<node>.<tailnet>.ts.net` (prod).

No settings file is written by hand: Wheelhouse renders each service's at every deploy
([settings at deploy](../../architecture/architecture.md#settings-at-deploy)).

- Derived: `AllowedHosts` (the site host, baseline or named, the service's platform name, `localhost`), `Sites:app:Url`,
  `Deployment:TrustedProxies` (`["172.30.0.2"]`, the ingress's `platform` address) and `ConnectionStrings:Database` from
  `database.json`.
- Asked for on the first deploy: the remaining required keys, such as Desk's `DataProtection:*`, the image worker's
  `ImageWorker:Key` (a secret: it goes to the vault) and Studio's `OcharoPlatform:BaseUrl` (Desk's address, such as
  `http://ocharo-platform-<environment>-desk:8080` on the `platform` network).

Deploy dev from Deployments: Check first, then Deploy, filling in what the dialog asks. Open a public site at its
baseline host (`https://<product>-dev.<ip-with-dashes>.sslip.io`) from anywhere; its first request may wait a few
seconds for the certificate. A private site opens at `https://<node>-dev.<tailnet>.ts.net` from an owner device.
Promote to prod once dev passes; prod's first deploy asks for its own values.

### Verify

```sh
sudo sshd -T | grep -E '^(passwordauthentication|kbdinteractiveauthentication|permitrootlogin) '   # all "no"
sudo ufw status verbose        # 22 on tailscale0 only after --tailnet-only-ssh; 80 and 443 allowed
curl -sI http://<product>-dev.<ip-with-dashes>.sslip.io/ | head -1    # from anywhere: 301 to https
curl -sI https://<product>-dev.<ip-with-dashes>.sslip.io/ | head -1   # a trusted Let's Encrypt certificate
sudo -u wheelhouse-deploy docker network inspect platform -f '{{json .IPAM.Config}}'
```

- From outside the tailnet, the public address answers on 80 and 443 only, IPv4 and IPv6, once SSH is tailnet-only.
- From an owner device, each site answers at its node; Traefik's own 404 means the target names another host.
- In prod, Servers shows the host's vitals and each target's check passes.

## Network repair

Every kit network reserves its fixed addresses: Docker hands out dynamic ones only from `.128/25`, so a container that
starts first after a reboot can never take Traefik's `.2`, PostgreSQL's `.3` or a tailnet node's address (the products
host's `Deployment:TrustedProxies` is `172.30.0.2`). `bootstrap.sh`, `bootstrap-products.sh` and Wheelhouse's
enrollment create them that way; a network created before 2026-10-19 lacks the range, and `bootstrap.sh` then refuses
with this section's name. Repair it in a short maintenance step:

```sh
sudo bash repair-networks.sh --check   # control: repair, platform: repair (or ok)
sudo bash repair-networks.sh           # recreates each network that lacks the range; a rerun changes nothing
```

- Each container is disconnected (its processes keep running), the network is created again with the range, and each
  reconnects with its aliases: fixed-address ones first at their own address, dynamic ones after, from `.128`.
- Traffic on that network pauses for a few seconds; the consoles' sessions survive. Run it outside a deploy.
- A network whose subnet or gateway differ is refused, untouched: that is an address review, not a repair.
- `rehearse.py networks` proves both: dynamic containers first, Traefik still at `.2`; and a repair that keeps a fixed
  address and moves a dynamic one into the range.

## Break-glass self-deploy

[`self-deploy.py`](self-deploy.py) deploys, rolls back and reads Wheelhouse on its control host when no console
answers, and deploys the first console on a fresh or rebuilt host. It is the host-local form of what the consoles do:
the release's own `runner.py`, copied out of its console image, validates the bundle, checks the target, takes the
target's lock and launches the detached rollout, whose journal every console's history reads. It never races a
console rollout, and the rollout outlives the session. Standard library and the Docker CLI only; no SSH, no secret.

```sh
# From a tailnet SSH session as the administrator, or the OVH KVM console.
sudo -u wheelhouse-deploy python3 /srv/wheelhouse/bootstrap/self-deploy.py status
sudo -u wheelhouse-deploy python3 /srv/wheelhouse/bootstrap/self-deploy.py rollback --env prod --reason "console down"
sudo -u wheelhouse-deploy python3 /srv/wheelhouse/bootstrap/self-deploy.py deploy --env prod --release v0.5.21 \
  --reason "console down"
```

- `status` reads each console's files alone: what it runs, whether a rollout runs or needs reconciliation, and its
  newest rollouts. It works with Docker or the network down. `--json` for a machine.
- `rollback` returns to the newest earlier release that succeeded there and whose bundle this host still keeps under
  `releases/<job>/` (the ten newest), or to `--release`; no network. The previous images stay on the host.
- `deploy` reads a published release anonymously from GitHub and checks its asset digest, tag, product and image
  repository; prod takes only a release that succeeded on `wheelhouse-dev` unless `--skip-dev-pass`. A bundle folder
  (`--bundle`) deploys without a download; a release the console already runs changes nothing.
- Prod asks for `wheelhouse-prod` typed (`--yes` skips it) and a `--reason`. The job's actor is
  `self-deploy:<administrator>`; `/srv/wheelhouse/self-deploy/log.jsonl` keeps each action with its reason.
- Its target is the kit's `config/wheelhouse-<env>.json` (`prepare.py`), gated as a console's inventory gates
  Wheelhouse's own: a new console its ingress cannot reach, or one that fails health, rolls back to the previous release.
- It refuses root: files written as root would lock the deployment account out of later rollouts.
- `rehearse.py self-deploy` proves it on the local server.

### Install

```sh
# From the repository's engineering/deployment/vps, copied to the host; bootstrap.sh installs the same on a new host.
sudo install -m 0500 -o wheelhouse-deploy -g wheelhouse-deploy self-deploy.py /srv/wheelhouse/bootstrap/
sudo install -m 0500 -o root -g root harden-control.sh bootstrap-products.sh /srv/wheelhouse/bootstrap/
sudo -u wheelhouse-deploy python3 /srv/wheelhouse/bootstrap/self-deploy.py status   # both consoles: ready
```

## Host hardening

[`harden-control.sh`](harden-control.sh) gives the control host a products host's policy once CI no longer reaches it.
The owner runs it after the self-deploy trial and the retirement of CI's path
([deployment.md § The fallback switch and its retirement](../deployment.md#the-fallback-switch-and-its-retirement)).

| UFW rule | Why |
|---|---|
| Default deny in, allow out | Baseline, as on a products host |
| 22/tcp on `tailscale0` | The owner's SSH |
| 443, 8443/tcp on `tailscale0` | Consoles through Serve; harmless where Serve bypasses UFW |
| 22/tcp from `172.30.1.0/24` to `172.30.0.1` | The consoles' runners reach the host here |
| 41641/udp | Tailscale's direct path |
| 22/tcp from anywhere | Only until `--tailnet-only-ssh` |

- Workloads on `platform` (`172.30.0.0/24`) lose host SSH: nothing allows them.
- sshd: no passwords, no keyboard-interactive, no root (`01-wheelhouse-control.conf`, the products host's policy).
  The administrator's local password stays, for the OVH KVM console only.
- Unattended security updates without reboots; Docker and Tailscale upgrade by hand.
- `--tailnet-only-ssh` also gives each runner key in `wheelhouse-deploy`'s `authorized_keys` `from="172.30.1.0/24"`,
  so a leaked console key fails anywhere else. Retire CI's key first.
- Only loopback ports are published here, so Docker's UFW bypass exposes nothing.

```sh
sudo bash /srv/wheelhouse/bootstrap/harden-control.sh --check   # what would change; exits 1 while anything would
sudo bash /srv/wheelhouse/bootstrap/harden-control.sh           # firewall on, public SSH kept
# Verify: each console's server check, both consoles over Serve, a canary deploy from dev.
# Keep a second SSH session open over the tailnet, and rehearse a sign-in at the OVH KVM console first.
sudo bash /srv/wheelhouse/bootstrap/harden-control.sh --check --tailnet-only-ssh
sudo bash /srv/wheelhouse/bootstrap/harden-control.sh --tailnet-only-ssh   # from a tailnet session
```

Every run converges, so a rerun changes nothing; a plain run never reopens public SSH it closed. Verify from outside
the tailnet that the public address answers on no port, over IPv4 and IPv6. Tailnet path lost: sign in at the OVH KVM
console (`manager.ca.ovhcloud.com`) and run `--public-ssh`; last resort, OVH rescue mode. The control host's Tailscale
key expiry stays disabled. `rehearse.py harden` proves the script on a real Ubuntu host with real UFW.

## GitHub-hosted CI access

The fallback path until it is retired; a self-deploying Wheelhouse needs none of it, and `harden-control.sh
--tailnet-only-ssh` closes it. Pinned OpenSSH to the verified public VPS address is usable for initial deployment while
the app stays private. Put host/user/key/known-hosts in the GitHub `dev` environment; keep strict host checking enabled.
Standard GitHub-hosted runners have changing broad IP ranges, so a fixed source-IP firewall exception
does not follow them. [GitHub recommends against allowlisting those ranges](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).

Tailnet CI was considered and set aside (2026-10-19): GitHub would still hold a host-root key, plus an OIDC client
and a `tag:ci` grant. Wheelhouse deploys itself instead. The earlier note stays for reference: the
[Tailscale action](https://tailscale.com/docs/integrations/github/github-action)
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
