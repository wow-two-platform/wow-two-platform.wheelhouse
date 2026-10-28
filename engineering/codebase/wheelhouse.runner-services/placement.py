"""Places a product on a products host from the console, in place of vps.md's hand-run steps 4 and 5: the host
converges to this Wheelhouse's kit (upkeep.converge: the kit, its firewall, PostgreSQL and the ingress), the product's
dev and prod databases are created once, and each of its private sites gets a tailnet node per environment that joins
with its own single-use key.

A placement asks only for what the product's deploy.yml calls for, as the console read it and passes it along
(`declared`): databases when a service needs PostgreSQL, with the untrusted extensions it declares created in them as
superuser, and nodes for private sites. Public sites need none: they answer
on the server's baseline hosts.

Everything runs over prod's runner SSH as wheelhouse-deploy, through the kit's prepare.py on the host. Database
passwords never leave the host. A site node's key is minted just before the node first starts, written to a private
env file on the host through SSH stdin, and removed once the node has started; only its ID, tag and expiry are kept."""
from __future__ import annotations
import json
from pathlib import Path
import re
import shlex
import time
import catalog
import fleet
import infra_jobs
import tailnet
import upkeep
from infra_jobs import Journal, remote
from runner import EXPOSURES, EXTENSION, SLUG, CommandFailed, Rejected, now, read_json, require
from transport import Ssh

ROOT = upkeep.ROOT
PREPARE = upkeep.PREPARE
COMPOSE = upkeep.COMPOSE
NODE = re.compile(r"[a-z][a-z0-9-]{0,50}[a-z0-9]")
RESERVED = ("postgres", "ingress")
STEPS = (("platform", "Kit, firewall, PostgreSQL and the ingress"),
         ("databases", "Dev and prod databases"),
         ("sites", "Site nodes registered"),
         ("nodes", "Site nodes on the tailnet"))
NODE_SECONDS = 120
# The enrollment's platform step: prepare.py and Compose on a host whose kit its root phases just laid out.
converge_platform = upkeep.converge_platform


def journal_path(root, identifier):
    infra_jobs.valid_id(identifier)
    return infra_jobs.folder(root, "placements") / (identifier + ".json")


def server_ssh(root, server):
    config = fleet.resolve_server(root, server)
    return Ssh(config["ssh"])


def validate(request):
    server = next((item for item in fleet.active_servers() if item.id == request.get("serverId")), None)
    require(server is not None, "Server is not in the inventory")
    require(server.provider is not fleet.VpsProvider.LOCAL, "The local server keeps the rig's own layout")
    product = str(request.get("product") or "")
    require(SLUG.fullmatch(product), "Invalid product")
    catalog.product(product)
    exposures, postgres, extensions = declared(request.get("declared"))
    sites = request.get("sites") or []
    require(isinstance(sites, list) and len(sites) <= 10, "Place at most 10 sites at once")
    cleaned, names = [], set()
    for entry in sites:
        site, node = str((entry or {}).get("site") or ""), str((entry or {}).get("node") or "")
        require(SLUG.fullmatch(site), "Use the site's name from the product's manifest, such as app")
        require(site in exposures, "The product's deploy.yml declares no site " + site)
        require(exposures[site] == "private",
                "Site " + site + " is public: it answers on its baseline host and needs no tailnet node")
        require(NODE.fullmatch(node) and node not in RESERVED,
                "Use a lowercase node name such as ocharo-desk; dev's node adds -dev")
        require(node not in names and node + "-dev" not in names, "Each site needs its own node name")
        names.update((node, node + "-dev"))
        cleaned.append({"site": site, "node": node})
    database = bool(request.get("database"))
    require(not database or postgres, "No service of the product needs PostgreSQL, so it takes no databases")
    require(database or cleaned, "Choose databases, sites or both")
    facts = {"sites": dict(sorted(exposures.items())), "postgres": postgres}
    if extensions:
        facts["extensions"] = extensions
    return {"serverId": server.id, "product": product, "database": database, "sites": cleaned, "declared": facts}


def declared(value):
    """What the product's deploy.yml declares, as the console read it: each site's exposure, whether a service needs
    PostgreSQL, and the untrusted extensions its migration sets need (M41). A request without it is refused, so nothing
    is placed that the manifest does not call for."""
    require(isinstance(value, dict), "A placement follows the product's deploy.yml; the request carries none of it")
    sites = value.get("sites") or {}
    require(isinstance(sites, dict) and len(sites) <= 50 and all(
        isinstance(name, str) and SLUG.fullmatch(name) and exposure in EXPOSURES for name, exposure in sites.items()),
        "Invalid declared sites")
    extensions = value.get("extensions") or []
    require(isinstance(extensions, list) and len(extensions) <= 50
            and all(isinstance(name, str) and EXTENSION.fullmatch(name) for name in extensions),
            "Invalid declared extensions")
    return sites, value.get("postgres") is True, list(dict.fromkeys(extensions))


def start(root, request, actor, launch=True):
    require(isinstance(actor, str) and 0 < len(actor) <= 160 and "\n" not in actor, "Invalid actor")
    request = validate(request)
    if request["sites"]:
        require(tailnet.read_client(root) is not None,
                "Connect Tailscale first: each site node joins with its own single-use key")
    fleet.resolve_server(root, request["serverId"])
    require(not infra_jobs.busy(root, request["serverId"]), "Another job is running on this server; wait for it")
    identifier = infra_jobs.job_id()
    journal = Journal(journal_path(root, identifier), {
        "id": identifier, "kind": "placement", "serverId": request["serverId"], "product": request["product"],
        "request": request, "actor": actor, "status": "queued", "reason": None, "createdAt": now(),
        "queuedAt": now(),
        "startedAt": None, "completedAt": None, "recordedAt": None, "facts": {"sites": [], "databases": []},
        "mints": [], "steps": infra_jobs.new_steps(STEPS)})
    journal.save()
    if launch:
        infra_jobs.spawn(Path(__file__).with_name("infra.py"), "place-run", "--root", str(root), "--job", identifier)
    return shown(root, journal.record)


def shown(root, record):
    result = infra_jobs.shown(record, infra_jobs.busy(root, record["serverId"]))
    result.pop("recordedAt", None)
    return result


def listing(root, product=None, server=None):
    """A product's (or server's) placements, newest first."""
    folder = Path(root) / infra_jobs.INFRA / "placements"
    result = []
    for path in folder.glob("*.json") if folder.is_dir() else ():
        try:
            record = read_json(path)
        except (ValueError, OSError):
            continue
        if (isinstance(record, dict) and (product is None or record.get("product") == product)
                and (server is None or record.get("serverId") == server)):
            result.append(shown(root, record))
    return sorted(result, key=lambda item: item.get("createdAt") or "", reverse=True)[:20]


def databases(ssh, journal):
    request = journal.record["request"]
    if not request["database"]:
        return None
    product = request["product"]
    present = [environment for environment in ("dev", "prod")
               if remote(ssh, "test -e " + shlex.quote(ROOT + "/config/" + product + "-" + environment + "/database.json")
                          + " && echo yes || echo no", timeout=30).strip() == "yes"]
    names = [product.replace("-", "_") + "_" + environment for environment in ("dev", "prod")]
    journal.record["facts"]["databases"] = names
    if len(present) == 2:
        detail = "Kept " + " and ".join(names) + ": their credentials already exist on the host"
    elif present:
        raise Rejected("Only " + present[0] + "'s database exists; review the host before creating the other")
    else:
        remote(ssh, PREPARE + " --product " + shlex.quote(product) + " --database", timeout=180)
        detail = "Created " + " and ".join(names) + "; each password stays in its database.json on the host"
    # The untrusted extensions the product declares (M41), as the platform PostgreSQL's superuser, in both databases;
    # a rerun creates only what is missing, so a placement repeated after deploy.yml adds one brings it in.
    extensions = (request.get("declared") or {}).get("extensions") or []
    if extensions:
        remote(ssh, PREPARE + " --product " + shlex.quote(product)
               + "".join(" --extension " + shlex.quote(name) for name in extensions), timeout=180)
        journal.record["facts"]["extensions"] = list(extensions)
        detail += "; extensions " + ", ".join(extensions) + " created as superuser in both"
    return detail


def registered(ssh, product):
    """The product's site nodes from the host's registry, with whether each has joined (its state file exists)."""
    nodes = json.loads(remote(ssh, "cat " + ROOT + "/platform/sites.json", timeout=30)).get("nodes") or []
    result = []
    for item in nodes:
        if item.get("product") != product or not NODE.fullmatch(str(item.get("name"))):
            continue
        state = ROOT + "/platform/sites/" + item["name"] + "/tailscaled.state"
        joined = remote(ssh, "test -e " + shlex.quote(state) + " && echo yes || echo no", timeout=30).strip() == "yes"
        result.append({"site": item.get("site"), "environment": item.get("environment"), "node": item["name"],
                       "address": item.get("address"), "joined": joined})
    return result


def sites(ssh, journal, session):
    request = journal.record["request"]
    if not request["sites"]:
        return None
    domain = tailnet.tailnet_domain(session.devices())
    require(domain, "The tailnet's name could not be read from its devices")
    flags = "".join(" --site " + shlex.quote(item["site"] + "=" + item["node"]) for item in request["sites"])
    remote(ssh, PREPARE + " --product " + shlex.quote(request["product"]) + " --tailnet " + shlex.quote(domain) + flags,
            timeout=120)
    found = registered(ssh, request["product"])
    asked = {item["node"] for item in request["sites"]} | {item["node"] + "-dev" for item in request["sites"]}
    found = [item for item in found if item["node"] in asked]
    for item in found:
        item["url"] = "https://" + item["node"] + "." + domain
    journal.record["facts"].update(tailnet=domain, sites=found)
    return ", ".join(item["node"] for item in found) + " registered at fixed platform addresses"


def nodes(ssh, journal, session):
    """Mints one key per node that has not joined, writes it for that node only, starts the nodes and waits for each
    to show on the tailnet with tag:product-site."""
    facts = journal.record["facts"]
    if not facts.get("sites"):
        return None
    fresh = [item["node"] for item in facts["sites"] if not item["joined"]]
    env_files = [ROOT + "/private/sites/" + name + ".env" for name in fresh]
    try:
        for name, path in zip(fresh, env_files):
            record, key = session.mint(tailnet.SITE_TAG, "wheelhouse site " + name)
            journal.record["mints"].append({**record, "step": "nodes", "node": name})
            journal.save()
            remote(ssh, "umask 077; mkdir -p " + ROOT + "/private/sites && cat > " + shlex.quote(path),
                    "TS_AUTHKEY=" + key + "\n", timeout=30)
            del key
        names = " ".join(shlex.quote(item["node"]) for item in facts["sites"])
        remote(ssh, COMPOSE + " up -d " + names, timeout=300)
    finally:
        if env_files:
            try:
                remote(ssh, "rm -f " + " ".join(shlex.quote(path) for path in env_files), timeout=30)
            except (CommandFailed, Rejected):
                pass
    deadline = time.monotonic() + NODE_SECONDS
    while True:
        devices = session.devices()
        for item in facts["sites"]:
            device = next((entry for entry in devices if entry.get("hostname") == item["node"]
                           and tailnet.SITE_TAG in (entry.get("tags") or [])), None)
            item["tailnetAddress"] = tailnet.ipv4_of(device) if device else None
            item["online"] = bool(device and device.get("connectedToControl"))
        missing = [item["node"] for item in facts["sites"] if not item["tailnetAddress"]]
        journal.save()
        if not missing or time.monotonic() >= deadline:
            break
        time.sleep(5)
    require(not missing, "Not on the tailnet yet: " + ", ".join(missing)
            + ". Read a node's log on the host: docker logs wheelhouse-products-<node>-1")
    for item in facts["sites"]:
        item["joined"] = True
    return (str(len(fresh)) + " node" + ("" if len(fresh) == 1 else "s") + " joined with single-use keys; "
            if fresh else "Every node had already joined; ") + "sites answer at their tailnet names"


def run(root, identifier, session=None):
    journal = Journal.load(journal_path(root, identifier))
    server = journal.record["serverId"]
    with infra_jobs.server_lock(root, server):
        require(journal.record.get("status") == "queued", "This placement has already run; start a new one")
        journal.record.update(status="running", startedAt=now())
        journal.save()
        ssh = server_ssh(root, server)
        request = journal.record["request"]
        lazy = {}

        def tailscale():
            if "session" not in lazy:
                lazy["session"] = session or tailnet.Tailscale(root)
            return lazy["session"]

        actions = {"platform": lambda: upkeep.converge(root, server, ssh, journal.record["facts"]),
                   "databases": lambda: databases(ssh, journal),
                   "sites": lambda: sites(ssh, journal, tailscale()),
                   "nodes": lambda: nodes(ssh, journal, tailscale())}
        for key, _ in STEPS:
            if (key == "databases" and not request["database"]) or (key in ("sites", "nodes") and not request["sites"]):
                journal.skip(key, "Not asked")
                continue
            if not journal.run(key, actions[key]):
                return shown(root, journal.record)
        journal.finish("succeeded")
        return shown(root, journal.record)
