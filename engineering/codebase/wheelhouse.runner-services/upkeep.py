"""Host upkeep: brings an enrolled products host to the kit this Wheelhouse ships, from the console. The kit goes up as
the deploy account; its root phase (layout, platform network, firewall with 80 and 443 public) runs through that
account's Docker access, which already grants host control (vps.md); the platform converges; the public entry points
must answer. A placement's platform step runs the same converge, so placing a product also brings its host up to date.

The converge is how a host enrolled before a kit change gets it (decision 68's public ingress first): no SSH session of
the owner's, no hand-run script. Only Wheelhouse prod holds a products host's key, so only prod runs it. Nothing here
reads or prints a secret."""
from __future__ import annotations
import ipaddress
import json
from pathlib import Path
import re
import fleet
import infra_jobs
import kit
from catalog import rehearsal
from infra_jobs import Journal, remote
from runner import CommandFailed, Rejected, now, read_json, require
from transport import Ssh

ROOT = fleet.DEFAULT_ROOT
PREPARE = "python3 " + ROOT + "/bootstrap/prepare.py --root " + ROOT + " --server products"
COMPOSE = "cd " + ROOT + "/platform && docker compose --env-file host.env -f compose.yml"
# Where the deploy account keeps the uploaded kit; the root phase lays it out from here.
KIT = ROOT + "/bootstrap/kit"
# A one-off root shell on the host for the kit's root phase: a privileged container in the host's PID namespace whose
# nsenter enters PID 1's namespaces. Pinned by digest, since it runs as the host's root.
HELPER = "alpine:3.24.2@sha256:294b683cb724975bec92580e1e685676bd4b50bda910ddb8c51d4cabeaec77e6"
STEPS = (("kit", "Products kit uploaded"),
         ("host", "Kit laid out; firewall opens 80 and 443, SSH kept as it is"),
         ("platform", "PostgreSQL and the ingress, private and public"),
         ("public", "Public entry points on 80 and 443, nothing else published"))
REDIRECTS = (301, 302, 307, 308)
# What the public step runs on the host, as the deploy account: 80 must send https, 443 must answer TLS for any name,
# and no container may publish a port beyond loopback except the ingress's 80 and 443.
CHECK = r'''
import http.client, json, socket, ssl, subprocess
result = {"http": None, "https": None, "published": []}
host = "wheelhouse-upkeep.invalid"
try:
    connection = http.client.HTTPConnection("127.0.0.1", 80, timeout=5)
    connection.request("GET", "/", headers={"Host": host})
    answer = connection.getresponse()
    result["http"] = [answer.status, answer.getheader("Location") or ""]
except OSError as error:
    result["http"] = [0, type(error).__name__]
try:
    context = ssl.create_default_context()
    context.check_hostname, context.verify_mode = False, ssl.CERT_NONE
    connection = http.client.HTTPConnection("127.0.0.1", 443, timeout=5)
    connection.sock = context.wrap_socket(socket.create_connection(("127.0.0.1", 443), timeout=5), server_hostname=host)
    connection.request("GET", "/", headers={"Host": host})
    result["https"] = [connection.getresponse().status, ""]
except (OSError, ssl.SSLError) as error:
    result["https"] = [0, type(error).__name__]
listing = subprocess.run(["docker", "ps", "--format", "{{.Names}}\t{{.Ports}}"], capture_output=True, text=True,
                         timeout=30)
for line in listing.stdout.splitlines():
    name, _, ports = line.partition("\t")
    for binding in filter(None, (part.strip() for part in ports.split(","))):
        if "->" in binding:
            result["published"].append([name, binding.split("->", 1)[0]])
print(json.dumps(result))
'''
LOOPBACK = ("127.0.0.1", "::1", "[::1]")
INGRESS_CONTAINER = re.compile(r"wheelhouse-products[-_]ingress[-_]1")


def journal_path(root, identifier):
    infra_jobs.valid_id(identifier)
    return infra_jobs.folder(root, "upkeeps") / (identifier + ".json")


def server_of(server):
    found = next((item for item in fleet.active_servers() if item.id == server), None)
    require(found is not None, "Server is not in the inventory")
    require(found.provider is not fleet.VpsProvider.LOCAL, "The local server keeps the rig's own layout")
    return found


def converge_platform(ssh):
    """prepare.py --server products keeps the PostgreSQL password and renders compose.yml again; then PostgreSQL and
    Traefik start or stay. Shared with the enrollment's platform step. prepare.py refuses first while PostgreSQL 16 data
    waits for its move to 18, so the kit's 18 never starts empty beside it; the refusal names postgres-move.py."""
    remote(ssh, PREPARE, timeout=120)
    remote(ssh, COMPOSE + " up -d --wait postgres ingress", timeout=900)
    return "PostgreSQL and the ingress run; compose.yml rendered from the kit"


def upload_kit(ssh):
    """The kit this Wheelhouse ships, unpacked as the deploy account into a fresh folder that replaces the last one."""
    entries = kit.files()
    remote(ssh, 'umask 077; d=' + KIT + '; rm -rf "$d.new" && mkdir -p "$d.new" && tar -xzf - -C "$d.new" '
                '&& rm -rf "$d" && mv "$d.new" "$d"', kit.archive(entries), timeout=120)
    return kit.digest(entries)


def root_phase(ssh, phase, timeout=600):
    """One of the kit's root phases on an enrolled host, through the deploy account's Docker access: the only path to
    root Wheelhouse keeps once the enrollment sealed the host. Returns the phase's output."""
    require(phase in ("upkeep",), "Unknown root phase")
    rig = "-e WHEELHOUSE_REHEARSAL=1 " if rehearsal() else ""
    return remote(ssh, "docker run --rm --privileged --pid=host --userns=host --network=none " + rig + HELPER
                  + " nsenter -t 1 -m -u -i -n -p -- bash " + KIT + "/enroll-products.sh "
                  + phase, timeout=timeout)


def host_address(output):
    """The IPv4 source address the root phase reported, from its last line; None when it named none."""
    try:
        address = json.loads(output.strip().splitlines()[-1]).get("hostAddress") or None
        return str(ipaddress.IPv4Address(address)) if address else None
    except (ValueError, IndexError, AttributeError):
        return None


def enrolled_address(root, server):
    """The public address the server's enrollment recorded, if Wheelhouse enrolled it."""
    path = Path(root) / infra_jobs.INFRA / "enrollments" / (server + ".json")
    try:
        record = read_json(path) if path.is_file() else None
    except (ValueError, OSError):
        return None
    address = ((record or {}).get("request") or {}).get("publicAddress")
    return address if isinstance(address, str) and address else None


def usable(address, reported=False):
    """A public address a baseline host can name: IPv4, never a tailnet, loopback or unspecified one. One the host
    reports itself must also be globally routable, unless on the local rig, whose addresses are all private."""
    try:
        value = ipaddress.IPv4Address(address)
    except ValueError:
        return False
    if value in ipaddress.ip_network("100.64.0.0/10") or value.is_loopback or value.is_unspecified:
        return False
    return value.is_global or not reported or rehearsal()


def public_address(root, server, reported):
    """The server's public address and where it came from: the one already recorded on the server, else the one its
    enrollment recorded, else the source address the host reports when it is public. None when there is none."""
    recorded = server_of(server).ingress.public_address
    for address, source in ((recorded, "server"), (enrolled_address(root, server), "enrollment")):
        if address and usable(address):
            return address, source
    if reported and usable(reported, reported=True):
        return reported, "host"
    return None, None


def converge(root, server, ssh, facts):
    """The kit uploaded and laid out, the firewall converged and the platform up: what a placement's platform step and
    the upkeep's first three steps do. Records the kit's digest, the public address and the ingress in `facts`."""
    digest = upload_kit(ssh)
    address, source = public_address(root, server, host_address(root_phase(ssh, "upkeep")))
    converge_platform(ssh)
    facts.update(kitDigest=digest, publicAddress=address, addressSource=source, ingress=dict(kit.PUBLIC_INGRESS))
    return ("Kit " + digest[:12] + " laid out; firewall opens 80 and 443; PostgreSQL and the ingress run"
            + ("; public address " + address if address else "; no public address recorded yet"))


def check_public(ssh):
    """Over SSH as the deploy account: 80 redirects to https, 443 answers TLS, and no container publishes beyond
    loopback but the ingress on 80 and 443."""
    try:
        answer = json.loads(remote(ssh, "python3 -", CHECK, timeout=90).strip().splitlines()[-1])
    except (ValueError, IndexError):
        raise CommandFailed("The host's public entry point check answered unexpectedly") from None
    status, location = (answer.get("http") or [0, ""])[:2]
    require(status in REDIRECTS and str(location).startswith("https://"),
            "Port 80 does not redirect to https (" + (str(status) if status else str(location)[:40]) + ")")
    secure = (answer.get("https") or [0, ""])[0]
    require(isinstance(secure, int) and secure > 0, "Port 443 does not answer TLS")
    stray = sorted({name + " " + binding for name, binding in answer.get("published") or []
                    if binding.rsplit(":", 1)[0] not in LOOPBACK
                    and not (INGRESS_CONTAINER.fullmatch(name) and binding.rsplit(":", 1)[-1] in ("80", "443"))})
    require(not stray, "Published beyond loopback: " + ", ".join(stray)[:200])
    return "80 redirects to https; 443 answers TLS; only the ingress publishes beyond loopback, on 80 and 443"


def start(root, request, actor, launch=True):
    require(isinstance(actor, str) and 0 < len(actor) <= 160 and "\n" not in actor, "Invalid actor")
    server = server_of(str((request or {}).get("serverId") or "")).id
    fleet.resolve_server(root, server)
    require(not infra_jobs.busy(root, server), "Another job is running on this server; wait for it")
    identifier = infra_jobs.job_id()
    journal = Journal(journal_path(root, identifier), {
        "id": identifier, "kind": "upkeep", "serverId": server, "request": {"serverId": server}, "actor": actor,
        "status": "queued", "reason": None, "createdAt": now(), "queuedAt": now(), "startedAt": None,
        "completedAt": None, "recordedAt": None, "facts": {}, "mints": [], "steps": infra_jobs.new_steps(STEPS)})
    journal.save()
    if launch:
        infra_jobs.spawn(Path(__file__).with_name("infra.py"), "upkeep-run", "--root", str(root), "--job", identifier)
    return shown(root, journal.record)


def shown(root, record):
    result = infra_jobs.shown(record, infra_jobs.busy(root, record["serverId"]))
    result.pop("recordedAt", None)
    return result


def listing(root, server):
    """A server's upkeep runs, newest first, beside the digest of the kit this Wheelhouse ships now."""
    server_of(server)
    folder = Path(root) / infra_jobs.INFRA / "upkeeps"
    jobs = []
    for path in folder.glob("*.json") if folder.is_dir() else ():
        try:
            record = read_json(path)
        except (ValueError, OSError):
            continue
        if isinstance(record, dict) and record.get("serverId") == server:
            jobs.append(shown(root, record))
    try:
        current = kit.digest()
    except Rejected:
        current = None
    return {"serverId": server, "kit": current,
            "jobs": sorted(jobs, key=lambda item: item.get("createdAt") or "", reverse=True)[:20]}


def run(root, identifier):
    journal = Journal.load(journal_path(root, identifier))
    server = journal.record["serverId"]
    with infra_jobs.server_lock(root, server):
        require(journal.record.get("status") == "queued", "This upkeep has already run; start a new one")
        journal.record.update(status="running", startedAt=now())
        journal.save()
        ssh = Ssh(fleet.resolve_server(root, server)["ssh"])
        facts = journal.record["facts"]
        state = {}

        def upload():
            facts["kitDigest"] = upload_kit(ssh)
            return "Kit " + facts["kitDigest"][:12] + " in " + KIT

        def host():
            state["reported"] = host_address(root_phase(ssh, "upkeep"))
            facts.update(hostAddress=state["reported"])
            return "Kit laid out as root through the deploy account's Docker access; UFW allows 80 and 443"

        def platform():
            return converge_platform(ssh)

        def public():
            address, source = public_address(root, server, state.get("reported"))
            require(address, "No public address is recorded for this server: Wheelhouse records it at enrollment, "
                             "and the host reports none that is public")
            facts.update(publicAddress=address, addressSource=source, ingress=dict(kit.PUBLIC_INGRESS))
            return check_public(ssh) + "; public address " + address + " (" + source + ")"

        for key, action in (("kit", upload), ("host", host), ("platform", platform), ("public", public)):
            if not journal.run(key, action):
                return shown(root, journal.record)
        journal.finish("succeeded")
        return shown(root, journal.record)
