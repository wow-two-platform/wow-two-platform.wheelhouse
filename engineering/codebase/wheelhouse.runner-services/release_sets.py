"""A product built from several repositories releases as a set (decision 70): one release of each repository that has
released, composed on the control host into one bundle that deploys, rolls back and promotes like any single-repository
release. A repository with no release yet (or a deploy.yml that names no service yet) is left out until it releases, and
later sets take it; the primary repository serves the front door, so no set exists before its first release.

- Identity: a set's bundle ID is `<product>-set-<12 hex>`, the first 12 hex digits of the SHA-256 of its components'
  artifact IDs in repository order, so the same components always make the same set; a repository not in it yet
  changes neither its ID nor its label. Its release label names each
  component as `<short name>-<release>`, joined by `_` in repository order (`platform-v0.1.2_studio-v0.1.2`), or
  `set-<12 hex>` when that would pass 96 characters.
- Composition: services, sites and named volumes keep the names their repositories give them, and no two repositories
  of the product share one: a site is a host role of the one product and a volume its storage, so each repository
  names its own (`desk`, `studio`; `studio-data`), and a clash is refused naming both repositories rather than renamed
  — a rename would move a site's host or start a volume empty. The product's front door is its primary repository's
  primary site (another repository's primary site is an ordinary one), so the repository that serves the front door
  goes first. A service may depend on another repository's service.
- The set bundle is schema 2: the product's sites and each service's routes. Rollback compatibility holds only when
  every component in the set declares it (a repository not in it yet declares nothing); .NET environments stay with the
  services whose repository opted in.
"""
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import re
from runner import (RELEASE, SHA, SLUG, dependency_waves, read_json, require, service_dependencies, write_json)

SET_ID = re.compile(r"[a-z][a-z0-9-]*-set-([0-9a-f]{12})")
DIGEST = 12
SEPARATOR = "_"
LABEL_LIMIT = 96
# One component in a set's label: its repository's short name, then its release tag or commit build.
LABEL_PART = re.compile(r"(?P<name>[a-z][a-z0-9-]*?)-(?P<release>v(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)"
                        r"(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?|sha-[0-9a-f]{7})")
FOLDER = "sets"


def identifier(product, component_ids):
    """The set's bundle ID: the product, then the first 12 hex digits of its components' IDs hashed in order."""
    digest = hashlib.sha256("\n".join(component_ids).encode()).hexdigest()[:DIGEST]
    result = product + "-set-" + digest
    require(SLUG.fullmatch(result), "A product built from several repositories needs a slug of 31 characters at most")
    return result


def is_set(bundle_id):
    return isinstance(bundle_id, str) and SET_ID.fullmatch(bundle_id) is not None


def label(set_id, parts):
    """`parts` are (short name, release) in repository order."""
    text = SEPARATOR.join(name + "-" + release for name, release in parts)
    return text if len(text) <= LABEL_LIMIT and RELEASE.fullmatch(text) else "set-" + set_id.rsplit("-", 1)[1]


def label_parts(text):
    """A set label's components as {short name: release}, or None for a label that is not a set's joined one."""
    if not isinstance(text, str) or not text or text.startswith("set-"):
        return None
    result = {}
    for part in text.split(SEPARATOR):
        match = LABEL_PART.fullmatch(part)
        if match is None or match.group("name") in result:
            return None
        result[match.group("name")] = match.group("release")
    return result


def remember(root, item):
    """Keeps what a listed set is made of under <root>/sets/, so a set that left the listing (an older release proven on
    dev, waiting for prod) still composes again from its components."""
    path = Path(root) / FOLDER / (item["id"] + ".json")
    try:
        if path.is_file() and read_json(path) == item:
            return
    except (OSError, ValueError):
        pass
    write_json(path, item)


def recall(root, set_id):
    """A set as it was listed, or None when this control plane never listed it."""
    require(is_set(set_id), "Invalid release set")
    path = Path(root) / FOLDER / (set_id + ".json")
    try:
        item = read_json(path)
    except (OSError, ValueError):
        return None
    return item if isinstance(item, dict) and item.get("id") == set_id else None


def recall_label(root, product, text):
    """The remembered set of a product whose label is `text`, or None."""
    folder = Path(root) / FOLDER
    for path in sorted(folder.glob(product + "-set-*.json")) if folder.is_dir() else ():
        try:
            item = read_json(path)
        except (OSError, ValueError):
            continue
        if isinstance(item, dict) and item.get("product") == product and item.get("release") == text:
            return item
    return None


def site_parts(manifest):
    """A component's sites as schema 2 roles and routes, whatever its schema, and its own front door. Descriptor 1
    sites become roles (private when any service makes them so) and one-path routes."""
    if manifest.get("schemaVersion") == 2:
        roles = {name: dict(role) for name, role in (manifest.get("sites") or {}).items()}
        routes = {service: {site: dict(entry) for site, entry in sites.items()}
                  for service, sites in (manifest.get("routes") or {}).items()}
        primary = next((name for name, role in roles.items() if role.get("primary")), None)
    else:
        roles, routes = {}, {}
        for service, sites in sorted((manifest.get("sites") or {}).items()):
            for site, entry in sorted(sites.items()):
                exposure = entry.get("exposure", "public")
                kept = roles.get(site)
                roles[site] = {"exposure": "private" if exposure == "private" or (kept or {}).get("exposure") == "private"
                               else "public", "primary": False}
                route = {"paths": [str(entry.get("path", "/")).rstrip("/") or "/"]}
                if "port" in entry:
                    route["port"] = entry["port"]
                routes.setdefault(service, {})[site] = route
        primary = None
    if primary is None:
        # The runner's choice for a release that marks none: the first site of the first service by name.
        primary = next((site for service in sorted(routes) for site in sorted(routes[service])), None)
    return roles, routes, primary


def compose(product, set_id, parts):
    """The set's release.json and compose.json bytes. `parts` are each component's (short name, repository, manifest,
    compose) in repository order, primary first, each already validated against its own repository. Refuses a service,
    a site or a named volume two repositories declare, mixed platforms, and a dependency on no service or in a cycle
    across repositories."""
    require(parts, "A release set needs at least one component")
    platforms = {manifest["platform"] for _, _, manifest, _ in parts}
    require(len(platforms) == 1, "The product's repositories build for different platforms")
    owners, sites, roles, routes, primary = {}, {}, {}, {}, None
    services, volumes = {}, {}
    holders = {}
    images, required, settings, versions, ports, needs, dotnet = {}, {}, {}, {}, {}, {}, []
    components, migrations, verify = [], [], None
    for short, repository, manifest, document in parts:
        require(manifest["product"] == product,
                repository + " releases product " + str(manifest["product"]) + ", not " + product)
        for service in manifest["images"]:
            require(service not in owners, "Service " + service + " is in both " + owners.get(service, "")
                    + " and " + repository)
            owners[service] = repository
        for volume, options in (document.get("volumes") or {}).items():
            require(volume not in volumes, "Volume " + volume + " is in both " + holders.get(volume, "") + " and "
                    + repository + "; each repository names its own storage")
            holders[volume] = repository
            volumes[volume] = options
        services.update(document["services"])
        part_roles, part_routes, part_primary = site_parts(manifest)
        for site, role in part_roles.items():
            require(site not in sites, "Site " + site + " is in both " + sites.get(site, "") + " and " + repository
                    + "; a site names one host role of the product, so each repository names its own")
            sites[site] = repository
            roles[site] = {key: value for key, value in role.items() if key != "primary"}
        routes.update(part_routes)
        if primary is None and part_primary is not None:
            primary = part_primary
        images.update(manifest["images"])
        required.update(manifest.get("requiredConfiguration") or {})
        settings.update(manifest.get("settings") or {})
        versions.update(manifest.get("versions") or {})
        ports.update(manifest.get("ports") or {})
        needs.update(manifest.get("needs") or {})
        if manifest.get("dotnetEnvironment") is True:
            dotnet += sorted(manifest["images"])
        elif isinstance(manifest.get("dotnetEnvironment"), list):
            dotnet += list(manifest["dotnetEnvironment"])
        migrations.append(manifest.get("migrations"))
        if verify is None and isinstance(manifest.get("verify"), dict):
            verify = manifest["verify"]
        components.append({"name": short, "repository": repository, "release": manifest["release"],
                           "kind": manifest.get("kind", "release"), "sourceCommit": manifest["sourceCommit"],
                           "services": sorted(manifest["images"])})
    for name, role in roles.items():
        role["primary"] = name == primary
    networks = parts[0][3].get("networks")
    document = {"services": services, "networks": networks}
    if volumes:
        document["volumes"] = volumes
    dependency_waves(services, service_dependencies(document))
    compose_bytes = (json.dumps(document, indent=2) + "\n").encode()
    kinds = {component["kind"] for component in components}
    candidates = [manifest.get("branch") for _, _, manifest, _ in parts if manifest.get("kind") == "candidate"]
    result = {"schemaVersion": 2, "product": product,
              "release": label(set_id, [(component["name"], component["release"]) for component in components]),
              "kind": "candidate" if "candidate" in kinds else "release",
              # The primary repository's commit; `components` names every repository's.
              "sourceCommit": parts[0][2]["sourceCommit"], "platform": platforms.pop(),
              "rollbackCompatible": all(manifest.get("rollbackCompatible") is True for _, _, manifest, _ in parts),
              "composeSha256": hashlib.sha256(compose_bytes).hexdigest(), "images": images,
              "requiredConfiguration": required, "versions": versions, "ports": ports, "sites": roles,
              "routes": routes, "needs": needs, "set": set_id, "components": components}
    if settings:
        result["settings"] = settings
    if dotnet:
        result["dotnetEnvironment"] = True if set(dotnet) == set(images) else sorted(set(dotnet))
    if verify is not None:
        result["verify"] = verify
    if all(type(value) is bool for value in migrations):
        result["migrations"] = any(migrations)
    elif True in migrations:
        result["migrations"] = True
    if len(set(candidates)) == 1 and isinstance(candidates[0], str):
        result["branch"] = candidates[0]
    require(SHA.fullmatch(result["sourceCommit"]), "A full source commit is required")
    return result, compose_bytes


def write(bundle, manifest, compose_bytes):
    """Writes a composed set into its bundle folder, the manifest last so discovery never sees half of one."""
    bundle = Path(bundle)
    bundle.mkdir(parents=True, mode=0o700, exist_ok=True)
    (bundle / "compose.json").write_bytes(compose_bytes)
    (bundle / "release.json").write_text(json.dumps(manifest, indent=2) + "\n")
