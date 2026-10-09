# Wheelhouse dev VPS

*Last updated: 2026-10-09*

This bootstrap prepares one private `wheelhouse-dev` target on Ubuntu 26.04 amd64.
GitHub builds the image; the existing [runner](../../codebase/wheelhouse.runner-services/runner.py)
deploys its immutable bundle over pinned SSH. Test and prod environments remain deferred.

## Files and boundaries

| Asset | Runs where | Result |
|---|---|---|
| [Host bootstrap](bootstrap.sh) | Verified VPS, administrator | Docker/Compose/Python/OpenSSH, deploy account, network, platform files |
| [Configuration generator](prepare.py) | VPS, deployment account | Initial dev database credentials, console settings and target JSON |
| [Identity template](identity.example.json) | Copy to a private VPS file | Real OAuth credentials and owner allowlist, supplied by the operator |
| [Platform Compose](platform.compose.yml) | VPS | PostgreSQL 16 and private Traefik ingress |

The scripts do not accept SSH host keys, enroll Tailscale, change firewall/sshd policy, fetch operator
credentials or start Wheelhouse. They are executable preparation assets, not evidence of deployment.
`bootstrap.sh` can rerun on its prepared host; it refuses incompatible engines/networks.
`prepare.py` refuses existing or partial credential output instead of rotating a live database password.
Review a partial failure before retrying; never delete a populated PostgreSQL volume to repeat initialization.

## Initial administrator access

1. Obtain the delivered SSH username and initial login through the OVH account/delivery instructions.
   The VPS-delivery email names the administrator `ubuntu`; its credential is separate from the OVH account.
   The account is in OVHcloud's Canada region: sign in at <https://manager.ca.ovhcloud.com/>.
   The EU sign-in (`auth.eu.ovhcloud.com`) rejects the same credentials as an invalid account or password.
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

The external Docker network `platform` uses `172.30.0.0/24`, gateway `172.30.0.1`.
Traefik is `172.30.0.2`; PostgreSQL is `172.30.0.3`. Docker refuses conflicting subnet creation.
If this subnet conflicts with the host or tailnet routes, review all addresses in these assets together
before bootstrap; do not silently reuse another network. The console trusts only Traefik's exact address.

Enroll the VPS in the operator's actual tailnet, with ordinary OpenSSH retained:

```sh
sudo tailscale up --hostname=wheelhouse-dev --accept-dns=true --accept-routes=false
tailscale status
```

Complete the displayed Tailscale authentication in the operator account. Enrollment supplies the real
`node.tailnet.ts.net` hostname; an example hostname must never become runtime configuration.
Enable MagicDNS and verify the actual hostname resolves on the VPS (`getent ahosts <actual-hostname>`),
because CI verifies the HTTPS endpoint from the server. Authorize only intended operator devices and
CI identities. Configure persistent private HTTPS:

```sh
sudo tailscale serve --bg --https=443 http://127.0.0.1:18080
sudo tailscale serve status
```

Tailnet HTTPS may require enabling certificates in its admin console. Use Serve, not Funnel.
Install/enroll Tailscale on the laptop and phone used to open the UI. The traffic path is:

```text
Enrolled client -> private HTTPS :443 -> Tailscale Serve
 -> 127.0.0.1:18080 -> Traefik :8080 -> wheelhouse-dev-console:8080
```

Traefik trusts forwarded headers only from loopback and the configured Docker gateway.
Verify the actual source address, HTTPS redirects and secure cookies on this host.
Traefik runs as the deployment UID/GID recorded in `platform/host.env`, so it can read runner-owned
mode-0600 routes without privileged capabilities. Its filesystem and route bind are read-only.

Only `127.0.0.1:18080` is published. Neither PostgreSQL nor the console has a public host port.
[Docker-published ports](https://docs.docker.com/engine/network/port-publishing/) require explicit binding;
UFW alone is insufficient. Verify external denial of app/database ports over IPv4 and IPv6.
Narrow public SSH only after a second verified private SSH session and provider-console recovery succeed.

## Private configuration

Copy `identity.example.json` to a private location accessible to `wheelhouse-deploy`, mode `0600`.
Fill its OAuth client and allowed owner login, without putting the file or values into source/logs.
Register the exact callback in the OAuth application:

```text
https://<actual-node>.<actual-tailnet>.ts.net/api/identity/callback
```

Run the generator as the deployment account, substituting the actual private hostname:

```sh
sudo -u wheelhouse-deploy python3 /srv/wheelhouse/bootstrap/prepare.py \
  --identity-file /protected/path/identity.json \
  --hostname '<actual-node>.<actual-tailnet>.ts.net'
```

The input's parent directories must permit the deployment account to reach it. No password is printed.
The generator creates independent random database-admin and application passwords on the VPS.
Initialization grants `wheelhouse_dev` ownership of its database, with no superuser, role-creation
or database-creation privileges. PostgreSQL initialization runs only on an empty data volume.

| Path | Handling |
|---|---|
| `/srv/wheelhouse/platform/.secrets/` | Mode `0700`; database initialization files readable only through private host paths/mounts |
| `/srv/wheelhouse/config/console.json` | Contains OAuth and DB credentials; read-only file inside mode `0700` directory |
| `/srv/wheelhouse/config/wheelhouse-dev.json` | Mode `0600`; target JSON for CI and the existing runner |
| `/srv/wheelhouse/ingress/` | Runner-generated route files, mounted read-only by Traefik |
| `/srv/wheelhouse/wheelhouse-dev/` | Runner deployment journals, current bundle and recovery state |

Container-mounted settings/init files are mode `0444` inside mode `0700` directories: host traversal is
private while container UID `1654`/PostgreSQL can read the individual file bind. This matches the existing
[runner settings contract](../deployment.md#runner-installation-and-inventory).
The generated target has `variables.PLATFORM_NETWORK=platform` and both ingress probes set to
`http://127.0.0.1:18080`. It does not replace the console's API-owned `inventory.json`.
The deployed image defaults to ASP.NET `Production`; target `dev` selects artifact policy only.
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
