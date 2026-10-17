# Network and access

*Last updated: 2026-10-17*

How the control-plane host is reached and how its containers are kept apart. Host-specific values (tailnet name,
addresses, accounts) stay out of this public repository; the operator keeps them in private notes.

## Paths in

| From | Path | Lands on |
|---|---|---|
| Owner devices on the tailnet | Tailscale Serve HTTPS `:443` → `127.0.0.1:18080` | Traefik `private` → prod console |
| Owner devices on the tailnet | Tailscale Serve HTTPS `:8443` → `127.0.0.1:18082` | Traefik `dev` → dev console |
| Owner devices on the tailnet | each canary node's Serve HTTPS `:443` → Traefik `:8084` | Traefik `canary` → that canary environment |
| Owner devices on the tailnet | OpenSSH `:22` | Administrator account |
| GitHub Actions (not on the tailnet) | OpenSSH `:22` on the public address | `wheelhouse-deploy`, keys only, `restrict` |
| Consoles (runner) | OpenSSH `:22` at the host's `platform` bridge address `172.30.0.1` | `wheelhouse-deploy`, one key per console |

Nothing else listens publicly: Traefik publishes only on loopback, and neither PostgreSQL nor a console has a host
port. Serve is tailnet-only; never use Funnel.

## Tailnet policy

[tailnet-policy.hujson](tailnet-policy.hujson) is the whole policy, pasted under Access controls in the Tailscale admin.

| Node | Tag | Reaches |
|---|---|---|
| Owner devices (laptop, phone) | none (owned by the owner) | Each other; the control host on 443, 8443, 22; canary nodes on 443 |
| Control-plane host | `tag:control` | Nothing on the tailnet |
| Canary nodes (`canary-dev`, `canary`: one per environment) | `tag:canary` | Nothing on the tailnet |

- A tag must exist in the policy before a machine can carry it: save the policy first, then tag the machine.
- Tagging changes a machine's ownership from the user to the tag. Tagged machines don't appear in `autogroup:self`.
- The control host's key expiry is disabled (Machine settings › Disable key expiry); an expired key would cut both
  consoles off the tailnet. Canary nodes join with a tagged auth key and never expire. Owner devices keep expiry.
- The policy's `tests` refuse a save that lets a canary node or the host reach anything.
- Tailscale SSH stays in check mode between owner devices only; the host uses ordinary OpenSSH.
- After a node rename, `sudo tailscale serve reset` and add both console ports again, or TLS fails with "no webserver
  configured for name/port".

Verify after any policy change:

```sh
# From a canary node: the control host must not be a peer.
sudo docker exec wheelhouse-platform-canary-dev-1 tailscale --socket=/tmp/tailscaled.sock ping -c 1 <control-host>
# Expected: "no matching peer". Owner devices: curl https://<control-host>/health and :8443/health return 200.
```

A canary node can still open the host's public SSH port over the internet like anyone; closing public SSH means moving
CI onto the tailnet first (Hosting backlog).

## Docker networks

| Network | Subnet, gateway | Members |
|---|---|---|
| `control` | `172.30.1.0/24`, `172.30.1.1` | Prod and dev consoles; PostgreSQL `172.30.1.3`; Traefik `172.30.1.2` |
| `platform` | `172.30.0.0/24`, `172.30.0.1` | Product and canary workloads; canary tailnet nodes `172.30.0.10` (dev) and `172.30.0.12` (prod); PostgreSQL `172.30.0.3`; Traefik `172.30.0.2` |

- A workload shares PostgreSQL and Traefik with the consoles but no network with them, so it cannot open a console.
- Traefik's console entry points (`private` :8080, `dev` :8082) attach [control-only.yml](control-only.yml), which
  accepts only loopback and the two gateways. A workload that forges a console hostname at Traefik gets 403.
- The `canary` entry point (:8084) stays open to the canary nodes and the host. `172.30.0.11` was the retired
  `canary-test` node (removed 2026-10-17 with the `test` environment); it is left unassigned.
- Console targets set `PLATFORM_NETWORK=control`; product and canary targets set `platform`.
- Consoles trust forwarded headers only from Traefik's `control` address `172.30.1.2`.
- PostgreSQL and both bridges sit on fixed addresses so forwarded-header trust stays explicit; [bootstrap.sh](bootstrap.sh)
  refuses a network whose subnet differs.

Verify:

```sh
sudo docker run --rm --network platform curlimages/curl:8.11.1 -s -o /dev/null -w '%{http_code}\n' \
  -H 'Host: <control-host>' http://172.30.0.2:8080/health   # expected 403
```

[migrate-control-network.sh](migrate-control-network.sh) moved the existing host onto this split on 2026-10-16 and
prints the same checks; a fresh host gets both networks from `bootstrap.sh`.

## Why it is shaped this way

The [MCP review](../../research/mcp-review.md) found a flat Docker network and an allow-all tailnet: a canary or
product container could reach a console directly, and every tailnet node could reach the consoles and SSH. Both are
closed above; the MCP-specific findings (call auditing, rate limits, key expiry) wait in the Integrations backlog.
