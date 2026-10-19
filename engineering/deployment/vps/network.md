# Network and access

*Last updated: 2026-10-19*

How the control-plane host and a products host are reached and how their containers are kept apart. Host-specific
values (tailnet name, addresses, accounts) stay out of this public repository; the operator keeps them in private notes.
A products host runs products only; its runbook is [vps.md § Products host](vps.md#products-host).

## Paths in

| From | Path | Lands on |
|---|---|---|
| Owner devices on the tailnet | Tailscale Serve HTTPS `:443` → `127.0.0.1:18080` | Traefik `private` → prod console |
| Owner devices on the tailnet | Tailscale Serve HTTPS `:8443` → `127.0.0.1:18082` | Traefik `dev` → dev console |
| Owner devices on the tailnet | each canary node's Serve HTTPS `:443` → Traefik `:8084` | Traefik `canary` → that canary environment |
| Owner devices on the tailnet | OpenSSH `:22` | Administrator account |
| GitHub Actions (not on the tailnet) | OpenSSH `:22` on the public address | `wheelhouse-deploy`, keys only, `restrict` |
| Consoles (runner) | OpenSSH `:22` at the host's `platform` bridge address `172.30.0.1` | `wheelhouse-deploy`, one key per console |

A products host:

| From | Path | Lands on |
|---|---|---|
| Owner devices on the tailnet | each site node's Serve HTTPS `:443` → Traefik `private` `172.30.0.2:8080` | That product site and environment |
| Owner devices on the tailnet | OpenSSH `:22` | Administrator account |
| Prod console (runner), from the control host | OpenSSH `:22` at the products host's tailnet address | `wheelhouse-deploy`, prod's key only, `restrict` |
| The public internet | Nothing, once `--tailnet-only-ssh` closed public SSH | UFW denies; 80 and 443 open with public ingress later |

Nothing else listens publicly: Traefik publishes only on loopback, and neither PostgreSQL nor a console has a host
port. Serve is tailnet-only; never use Funnel.

## Tailnet policy

[tailnet-policy.hujson](tailnet-policy.hujson) is the whole policy, pasted under Access controls in the Tailscale admin.

| Node | Tag | Reaches |
|---|---|---|
| Owner devices (laptop, phone) | none (owned by the owner) | Each other; the control host on 443, 8443, 22; canary nodes on 443 |
| Control-plane host | `tag:control` | A products host's SSH (22), for Wheelhouse prod's runner |
| Canary nodes (`canary-dev`, `canary`: one per environment) | `tag:canary` | Nothing on the tailnet |
| Products host | `tag:products` | Nothing on the tailnet |
| Product site nodes (`<node>-dev`, `<node>`: one per site and environment) | `tag:product-site` | Nothing on the tailnet |

- A tag must exist in the policy before a machine can carry it: save the policy first, then tag the machine.
- Tagging changes a machine's ownership from the user to the tag. Tagged machines don't appear in `autogroup:self`.
- The control host's key expiry is disabled (Machine settings › Disable key expiry); an expired key would cut both
  consoles off the tailnet. Canary nodes join with a tagged auth key and never expire. Owner devices keep expiry.
- The policy's `tests` refuse a save that lets a canary, products or site node reach the control host, or the control
  host reach more than a products host's SSH.
- Both consoles share the control host's tailnet identity, so dev could open the products host's port 22 too. Only
  prod's console volume holds a key the products host accepts; `authorize-runner.sh` refuses a remote key for dev.
- The products host joins as `tag:products` (bootstrap-products.sh); site nodes join with a reusable auth key tagged
  `tag:product-site`. Tagged at sign-in, neither key expires.
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

### A products host

| Network | Subnet, gateway | Members |
|---|---|---|
| `platform` | `172.30.0.0/24`, `172.30.0.1`; workloads from `172.30.0.128/25` | Product workloads; PostgreSQL `172.30.0.3` (`wheelhouse-postgres`); Traefik `172.30.0.2`; site nodes `172.30.0.64/26` |

- The same addresses as the control host's `platform`, so a product's settings read alike on either host.
- Docker hands workloads addresses from `172.30.0.128/25` only, so a restart order never lets one take a fixed address.
- No `control` network and no console; Traefik's one entry point, `private` (`:8080`, loopback `18080`), serves every
  site and trusts forwarded headers only from the host and the site nodes' range.
- Targets set `PLATFORM_NETWORK=platform`; products trust Traefik's `172.30.0.2` as their proxy.
- Service-to-service calls stay on `platform`; only SSH and the sites reach the tailnet.

Verify on the products host:

```sh
sudo docker network inspect platform -f '{{json .IPAM.Config}}'   # Subnet, IPRange 172.30.0.128/25, Gateway
sudo docker run --rm --network platform curlimages/curl:8.11.1 -s -o /dev/null -w '%{http_code}\n' \
  -H 'Host: <site-node>.<tailnet>.ts.net' http://172.30.0.2:8080/   # the site's own answer, not Traefik's 404
```

## Why it is shaped this way

The [MCP review](../../research/mcp-review.md) found a flat Docker network and an allow-all tailnet: a canary or
product container could reach a console directly, and every tailnet node could reach the consoles and SSH. Both are
closed above; the MCP-specific findings (call auditing, rate limits, key expiry) wait in the Integrations backlog.
