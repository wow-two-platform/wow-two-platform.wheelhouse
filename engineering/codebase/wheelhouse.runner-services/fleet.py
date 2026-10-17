"""Servers, targets and vaults as the control plane's inventory names them. The database owns them; the runner reads
the snapshot the control plane exports (see inventory.py), and the local server adds its own fixtures.

Credentials never travel in the inventory: SSH identities live under <root>/ssh/<server-id>/ and vault administrator
passwords under <root>/vaults/<vault-id>/password, placed by the operator. A server row without them reaches nothing,
and its known_hosts pin refuses a host that changed under the same name."""
from __future__ import annotations
from dataclasses import dataclass, field
from enum import Enum
import os
from pathlib import Path
import re
from catalog import product as defined_product, rehearsal
from runner import SLUG, require

VAULT_URL = re.compile(r"https?://[A-Za-z0-9.-]+(:[0-9]{1,5})?")
HOST = re.compile(r"(?=.{1,253}$)[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+")
# Where a target deploys unless its row names another root; also the disk a server's host reading covers before it
# runs a target.
DEFAULT_ROOT = "/srv/wheelhouse"


class VpsProvider(str, Enum):
    HETZNER = "hetzner"
    LOCAL = "local"
    OVHCLOUD = "ovhcloud"


class DeploymentEnvironment(str, Enum):
    DEV = "dev"
    PROD = "prod"


# Environments a target no longer takes; their old deployment records still read, labelled as retired. `test` went
# on 2026-10-17, when products moved to `main` only: every release deploys to dev, then is promoted to prod.
RETIRED_ENVIRONMENTS = ("test",)


def environment_label(value):
    """An environment as a record shows it: a current one by its name, a retired one marked "(retired)"."""
    if value in RETIRED_ENVIRONMENTS:
        return value + " (retired)"
    return value if any(value == environment.value for environment in DeploymentEnvironment) else None


@dataclass(frozen=True)
class Ingress:
    """How a server's ingress publishes sites: URL scheme and port, Traefik entry points and a host pattern."""
    scheme: str = "https"
    port: int | None = None
    entry_points: tuple[str, ...] = ("websecure",)
    private_entry_points: tuple[str, ...] = ()
    cert_resolver: str | None = "letsencrypt"
    # Hosts for sites a target leaves unnamed, e.g. "{site}-{product}.{environment}.preview.example"; prod names its own.
    pattern: str | None = None
    # Where the runner reaches the public entry points from the host, to request each site after a deploy;
    # unset means the scheme's port on loopback. Private sites are probed only when `private_probe` is set.
    probe: str | None = None
    private_probe: str | None = None


@dataclass(frozen=True)
class Server:
    id: str
    name: str
    provider: VpsProvider
    host: str
    region: str
    ssh_user: str = "deploy"
    ssh_port: int = 22
    ingress: Ingress = field(default_factory=Ingress)


@dataclass(frozen=True)
class Target:
    id: str
    server_id: str
    product: str
    environment: DeploymentEnvironment
    settings: tuple[tuple[str, str], ...]
    network: str
    root: str = DEFAULT_ROOT
    smoke: tuple[dict, ...] = ()
    # Site name -> host, for sites the server's pattern does not cover (every prod site).
    sites: tuple[tuple[str, str], ...] = ()


@dataclass(frozen=True)
class Vault:
    id: str
    name: str
    server_id: str
    # The private management endpoint Wheelhouse reaches; the browser never sees or chooses it.
    url: str


# Installed from the inventory snapshot; empty until the control plane has exported one.
SERVERS: tuple[Server, ...] = ()
TARGETS: tuple[Target, ...] = ()
VAULTS: tuple[Vault, ...] = ()

# The local server from engineering/deployment/rehearsal. Its settings paths are identical inside the target
# container and on the Docker host, so the daemon resolves the same bind-mount sources. The console container
# runs from /app, so the rig passes the host path in REHEARSAL_STATE.
LOCAL_STATE = Path(os.environ.get("REHEARSAL_STATE")
                   or Path(__file__).resolve().parents[2] / "deployment" / "rehearsal" / "state")
# `network`: Wheelhouse runs inside the rig and reaches the target and vault by service name, as it would a VPS.
IN_RIG = os.environ.get("WHEELHOUSE_REHEARSAL") == "network"
# The runner executes inside the rig's target container, which reaches Traefik by its service name.
LOCAL_INGRESS = Ingress(scheme="http", port=18080, entry_points=("web",), private_entry_points=("web",),
                        cert_resolver=None, pattern="{site}-{product}.{environment}.localhost",
                        probe="http://ingress:80", private_probe="http://ingress:80")
LOCAL_SERVERS = (Server("local", "Local server", VpsProvider.LOCAL, "target" if IN_RIG else "127.0.0.1", "local",
                        ssh_port=22 if IN_RIG else 2222, ingress=LOCAL_INGRESS),)
LOCAL_TARGETS = tuple(
    Target("foreverpin-" + environment.value, "local", "foreverpin", environment,
           (("management", str(LOCAL_STATE / "secrets" / environment.value / "management.json")),
            ("redirect", str(LOCAL_STATE / "secrets" / environment.value / "redirect.json"))),
           "wheelhouse-rehearsal",
           smoke=({"service": "management", "path": "/api/runtime-config", "status": 200},
                  {"service": "redirect", "path": "/health", "status": 200}))
    for environment in DeploymentEnvironment) + (
    # Wheelhouse ships like its products: `rehearse.py self` deploys its own bundle here.
    Target("wheelhouse-dev", "local", "wheelhouse", DeploymentEnvironment.DEV,
           (("console", str(LOCAL_STATE / "secrets" / "dev" / "wheelhouse-console.json")),),
           "wheelhouse-rehearsal", smoke=({"service": "console", "path": "/api/system/ready", "status": 200},)),)
LOCAL_VAULTS = (Vault("local-vault", "Local vault", "local", "http://vault:8080" if IN_RIG else "http://127.0.0.1:18201"),)


def with_fixtures(inventory, fixtures):
    """The inventory, plus the local server's fixtures whose IDs it lacks; a database row always wins."""
    if not rehearsal():
        return inventory
    known = {item.id for item in inventory}
    return inventory + tuple(item for item in fixtures if item.id not in known)


def active_servers():
    return with_fixtures(SERVERS, LOCAL_SERVERS)


def promotion_order(targets):
    """Targets by product, then along the promotion path in DeploymentEnvironment's declared order (dev, prod), then
    ID: the order every listing shows, never the alphabet."""
    path = list(DeploymentEnvironment)

    def rank(environment):
        return path.index(environment) if environment in path else len(path)
    return tuple(sorted(targets, key=lambda target: (target.product, rank(target.environment), target.id)))


def active_targets():
    return promotion_order(with_fixtures(TARGETS, LOCAL_TARGETS))


def active_vaults():
    return with_fixtures(VAULTS, LOCAL_VAULTS)


def vaults():
    catalog = active_vaults()
    servers()
    require(len({vault.id for vault in catalog}) == len(catalog), "Duplicate vault ID")
    result = []
    for vault in catalog:
        require(SLUG.fullmatch(vault.id) and VAULT_URL.fullmatch(vault.url), "Unsupported vault")
        require(any(server.id == vault.server_id for server in active_servers()), "Server is not in the inventory")
        result.append({"id": vault.id, "name": vault.name, "serverId": vault.server_id, "url": vault.url})
    return result


def servers():
    catalog = active_servers()
    require(len({server.id for server in catalog}) == len(catalog), "Duplicate server ID")
    result = []
    for server in catalog:
        require(SLUG.fullmatch(server.id) and isinstance(server.provider, VpsProvider), "Unsupported server")
        result.append({"id": server.id, "name": server.name, "provider": server.provider.value,
                       "host": server.host, "region": server.region, "sshUser": server.ssh_user})
    return result


def accepts_candidates(target):
    """Dev takes a build of any commit or branch; prod takes published releases only."""
    return target.environment is DeploymentEnvironment.DEV


def requires_dev_pass(target):
    """Prod takes a release only after it succeeded on one of the product's dev targets."""
    return target.environment is DeploymentEnvironment.PROD


def needs_confirmation(server, target):
    """Prod on the local server runs only after the operator types the target's ID."""
    return server.provider is VpsProvider.LOCAL and target.environment is DeploymentEnvironment.PROD


def ingress_of(server, target):
    ingress = server.ingress
    hosts = dict(target.sites)
    require(len(hosts) == len(target.sites), "Duplicate site host")
    require(all(SLUG.fullmatch(name) and HOST.fullmatch(host) for name, host in hosts.items()), "Invalid site host")
    # Prod on a VPS names every host itself; a shared preview pattern would publish it under the wrong domain.
    pattern = ingress.pattern if (target.environment is not DeploymentEnvironment.PROD
                                  or server.provider is VpsProvider.LOCAL) else None
    probe = ingress.probe or ingress.scheme + "://127.0.0.1" + (":" + str(ingress.port) if ingress.port else "")
    return {"scheme": ingress.scheme, "port": ingress.port, "entryPoints": list(ingress.entry_points),
            "privateEntryPoints": list(ingress.private_entry_points), "certResolver": ingress.cert_resolver,
            "pattern": pattern, "hosts": hosts, "probe": probe, "privateProbe": ingress.private_probe}


def ssh_of(root, server):
    """How the control plane reaches a server: its address and the operator-placed identity under <root>/ssh/<id>/."""
    identity = Path(root) / "ssh" / server.id
    return {"host": server.host, "user": server.ssh_user, "port": server.ssh_port,
            "keyFile": str(identity / "identity"), "knownHostsFile": str(identity / "known_hosts")}


def resolve_server(root, identifier):
    """A server as its own host reading needs it: how to reach it, and the deployment roots whose disks to cover —
    its targets' roots, or the default root while it runs none."""
    servers()
    server = next((item for item in active_servers() if item.id == identifier), None)
    require(server is not None and SLUG.fullmatch(server.id), "Server is not in the inventory")
    roots = sorted({target.root for target in active_targets() if target.server_id == server.id}) or [DEFAULT_ROOT]
    return {"serverId": server.id, "ssh": ssh_of(root, server), "roots": roots}


def resolve_target(root, identifier):
    servers()
    catalog = active_targets()
    require(len({target.id for target in catalog}) == len(catalog), "Duplicate target ID")
    target = next((item for item in catalog if item.id == identifier), None)
    require(target is not None and SLUG.fullmatch(target.id), "Target is not in the inventory")
    defined_product(target.product)
    require(isinstance(target.environment, DeploymentEnvironment), "Unsupported environment")
    server = next((item for item in active_servers() if item.id == target.server_id), None)
    require(server is not None, "Server is not in the inventory")
    return {"serverId": server.id, "provider": server.provider.value,
            "acceptsCandidates": accepts_candidates(target),
            "needsConfirmation": needs_confirmation(server, target),
            "requiresDevPass": requires_dev_pass(target),
            "ssh": ssh_of(root, server),
            "target": {"product": target.product, "environment": target.environment.value,
                       "root": target.root, "settings": dict(target.settings),
                       "variables": {"PLATFORM_NETWORK": target.network}, "smoke": list(target.smoke),
                       "ingress": ingress_of(server, target)}}
