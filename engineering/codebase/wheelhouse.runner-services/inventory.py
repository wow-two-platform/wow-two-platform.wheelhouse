"""The inventory snapshot: products, servers, targets and vaults, exported by the control plane from its database.

The control plane rewrites <root>/inventory.json at startup and after every inventory change. The runner and the
operator CLI read it, so the CLI keeps working from the last snapshot while the control plane is down. A malformed
snapshot is refused whole: the runner never acts on part of an inventory."""
from __future__ import annotations
import json
from pathlib import Path
import catalog
import fleet
from runner import Rejected, require

SNAPSHOT = "inventory.json"
VERSION = 1


def install(root):
    """Reads the snapshot under `root` into the catalog and the fleet; without one, both stay empty."""
    path = Path(root) / SNAPSHOT
    if not path.is_file():
        catalog.PRODUCTS, fleet.SERVERS, fleet.TARGETS, fleet.VAULTS = (), (), (), ()
        return
    try:
        document = json.loads(path.read_text())
        require(isinstance(document, dict) and document.get("version") == VERSION, "Unsupported inventory snapshot")
        products = tuple(product_of(item) for item in listed(document, "products"))
        servers = tuple(server_of(item) for item in listed(document, "servers"))
        targets = tuple(target_of(item) for item in listed(document, "targets"))
        vaults = tuple(vault_of(item) for item in listed(document, "vaults"))
    except Rejected:
        raise
    except (KeyError, TypeError, ValueError, AttributeError):
        raise Rejected("Inventory snapshot is malformed") from None
    catalog.PRODUCTS, fleet.SERVERS, fleet.TARGETS, fleet.VAULTS = products, servers, targets, vaults


def fixtures():
    """The local server's fixtures in snapshot shape, for the control plane to seed its database on the local rig."""
    require(catalog.rehearsal(), "Fixtures exist only on the local server")
    return {"version": VERSION,
            "products": [product_json(item) for item in catalog.LOCAL_PRODUCTS],
            "servers": [server_json(item) for item in fleet.LOCAL_SERVERS],
            "targets": [target_json(item) for item in fleet.LOCAL_TARGETS],
            "vaults": [vault_json(item) for item in fleet.LOCAL_VAULTS]}


def listed(document, key):
    items = document[key]
    require(isinstance(items, list), "Inventory snapshot is malformed")
    return items


def text(item, key, optional=False):
    value = item.get(key)
    if optional and value is None:
        return None
    require(isinstance(value, str), "Inventory snapshot is malformed")
    return value


def number(item, key, optional=False):
    value = item.get(key)
    if optional and value is None:
        return None
    require(type(value) is int, "Inventory snapshot is malformed")
    return value


def pairs(items, first, second):
    require(isinstance(items, list), "Inventory snapshot is malformed")
    return tuple((text(entry, first), text(entry, second)) for entry in items)


def product_of(item):
    release = item.get("release")
    return catalog.Product(
        text(item, "slug"), text(item, "name"), text(item, "description"), text(item, "repository"),
        text(item, "defaultBranch"),
        None if release is None else catalog.Release(text(release, "asset"),
                                                     pairs(release["images"], "service", "image"),
                                                     text(release, "workflow", optional=True)))


def server_of(item):
    ingress = item["ingress"]
    return fleet.Server(
        text(item, "id"), text(item, "name"), fleet.VpsProvider(text(item, "provider")), text(item, "host"),
        text(item, "region"), text(item, "sshUser"), number(item, "sshPort"),
        fleet.Ingress(scheme=text(ingress, "scheme"), port=number(ingress, "port", optional=True),
                      entry_points=tuple(map(str, ingress["entryPoints"])),
                      private_entry_points=tuple(map(str, ingress["privateEntryPoints"])),
                      cert_resolver=text(ingress, "certResolver", optional=True),
                      pattern=text(ingress, "pattern", optional=True),
                      probe=text(ingress, "probe", optional=True),
                      private_probe=text(ingress, "privateProbe", optional=True)))


def target_of(item):
    smoke = item["smoke"]
    require(isinstance(smoke, list), "Inventory snapshot is malformed")
    return fleet.Target(
        text(item, "id"), text(item, "serverId"), text(item, "product"),
        fleet.DeploymentEnvironment(text(item, "environment")), pairs(item["settings"], "service", "path"),
        text(item, "network"), text(item, "root"),
        smoke=tuple({"service": text(check, "service"), "path": text(check, "path"), "status": number(check, "status")}
                    for check in smoke),
        sites=pairs(item["sites"], "site", "host"))


def vault_of(item):
    return fleet.Vault(text(item, "id"), text(item, "name"), text(item, "serverId"), text(item, "url"))


def product_json(product):
    release = product.release
    return {"slug": product.slug, "name": product.name, "description": product.description,
            "repository": product.repository, "defaultBranch": product.default_branch,
            "release": None if release is None else {
                "asset": release.asset, "workflow": release.workflow,
                "images": [{"service": service, "image": image} for service, image in release.images]}}


def server_json(server):
    ingress = server.ingress
    return {"id": server.id, "name": server.name, "provider": server.provider.value, "host": server.host,
            "region": server.region, "sshUser": server.ssh_user, "sshPort": server.ssh_port,
            "ingress": {"scheme": ingress.scheme, "port": ingress.port, "entryPoints": list(ingress.entry_points),
                        "privateEntryPoints": list(ingress.private_entry_points),
                        "certResolver": ingress.cert_resolver, "pattern": ingress.pattern,
                        "probe": ingress.probe, "privateProbe": ingress.private_probe}}


def target_json(target):
    return {"id": target.id, "serverId": target.server_id, "product": target.product,
            "environment": target.environment.value, "network": target.network, "root": target.root,
            "settings": [{"service": service, "path": path} for service, path in target.settings],
            "smoke": [dict(check) for check in target.smoke],
            "sites": [{"site": site, "host": host} for site, host in target.sites]}


def vault_json(vault):
    return {"id": vault.id, "name": vault.name, "serverId": vault.server_id, "url": vault.url}
