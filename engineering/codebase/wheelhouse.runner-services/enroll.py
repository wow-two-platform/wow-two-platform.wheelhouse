"""Enrolls a fresh products host from Wheelhouse prod: a one-time SSH key the owner adds at order time (root on a
root-only image), the kit run over that public session, a single-use tailnet key, and a second path that proves the
host before Wheelhouse trusts it.

The second path: the host's first-seen key is pinned at first contact (and checked against the owner's fingerprint
when given); after the host joins the tailnet, its node must report the server's public address among its endpoints,
and prod's runner must reach it over the tailnet presenting that same host key. An attacker in the middle of the first
contact gets a node that shows a foreign endpoint, or a relayed session with another key, and the enrollment stops
before any product secret goes there. Then public SSH closes and the one-time key is removed from host and console."""
from __future__ import annotations
import ipaddress
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import time
import fleet
import infra_jobs
import kit
import placement
import tailnet
from catalog import rehearsal
from infra_jobs import Journal
from runner import SLUG, CommandFailed, Rejected, now, require
from transport import Ssh

DEPLOY_USER = "wheelhouse-deploy"
KEYS = "enrollment-keys"
STEPS = (("contact", "Public SSH and the host key it presents"),
         ("upload", "Products kit uploaded"),
         ("admin", "Administrator account"),
         ("packages", "Docker, Tailscale, UFW and security updates installed"),
         ("host", "Deploy account, kit, platform network and firewall"),
         ("join", "Tailnet join with a single-use key"),
         ("node", "Tailnet node at the server's public address"),
         ("runner", "Prod's runner key, proven over the tailnet"),
         ("platform", "PostgreSQL and private ingress"),
         ("seal", "Keys-only SSH, public SSH closed, one-time key removed"),
         ("verify", "Public SSH refused, tailnet path open"))
USER = re.compile(r"[a-z_][a-z0-9_-]{0,31}")
ADMIN = re.compile(r"[a-z][a-z0-9-]{0,30}")
HOSTNAME = re.compile(r"[a-z][a-z0-9-]{0,61}[a-z0-9]")
FINGERPRINT = re.compile(r"SHA256:[A-Za-z0-9+/]{43}=?")
PUBLIC_KEY = re.compile(r"(ssh-ed25519|ssh-rsa|ecdsa-sha2-nistp(256|384|521)) [A-Za-z0-9+/]+=*( [^\s]{1,100})?")
NODE_SECONDS = 120


def journal_path(root, server):
    require(SLUG.fullmatch(server or ""), "Invalid server")
    return infra_jobs.folder(root, "enrollments") / (server + ".json")


def key_folder(root, server):
    require(SLUG.fullmatch(server or ""), "Invalid server")
    folder = infra_jobs.folder(root, KEYS) / server
    folder.mkdir(exist_ok=True, mode=0o700)
    return folder


def server_of(server):
    found = next((item for item in fleet.active_servers() if item.id == server), None)
    require(found is not None, "Server is not in the inventory")
    require(found.provider is not fleet.VpsProvider.LOCAL, "The local server keeps the rig's own layout")
    return found


def fingerprint(public_key):
    result = subprocess.run(["ssh-keygen", "-l", "-E", "sha256", "-f", "-"], input=public_key + "\n",
                            capture_output=True, text=True, timeout=10)
    require(result.returncode == 0, "The key does not parse")
    return result.stdout.split()[1]


def load(root, server):
    path = journal_path(root, server)
    return Journal.load(path) if path.is_file() else None


def prepare_key(root, server):
    """The enrollment's one-time key pair, created once and shown until the enrollment finishes."""
    server_of(server)
    journal = load(root, server)
    require(journal is None or journal.record.get("status") != "succeeded", "This server is already enrolled")
    folder = key_folder(root, server)
    identity = folder / "identity"
    if not identity.exists():
        result = subprocess.run(["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-C", "wheelhouse-enroll-" + server,
                                 "-f", str(identity)], capture_output=True, text=True, timeout=30)
        if result.returncode:
            raise CommandFailed("Creating the one-time key failed")
    public = " ".join((folder / "identity.pub").read_text().split()[:3])
    return {"serverId": server, "publicKey": public, "fingerprint": fingerprint(public)}


def validate(root, request):
    server = server_of(request.get("serverId"))
    try:
        address = ipaddress.ip_address(str(request.get("publicAddress") or ""))
    except ValueError:
        raise Rejected("Use the server's public IP address, as the provider shows it") from None
    require(address not in tailnet.TAILNET_V4, "Use the server's public address, not a tailnet one")
    port = request.get("sshPort", 22)
    require(type(port) is int and 1 <= port <= 65535, "Invalid SSH port")
    login = str(request.get("loginUser") or "")
    require(USER.fullmatch(login), "Use the account the image delivers: root, or its administrator such as ubuntu")
    admin = str(request.get("admin") or (login if login != "root" else ""))
    require(ADMIN.fullmatch(admin) and admin not in ("root", DEPLOY_USER),
            "Name the administrator something other than root or " + DEPLOY_USER)
    require(login == "root" or admin == login, "Without root, the sign-in account is the administrator")
    admin_key = request.get("adminKey") or None
    require(admin_key is None or (len(admin_key) <= 1000 and PUBLIC_KEY.fullmatch(admin_key.strip())),
            "The administrator key must be one SSH public key line")
    hostname = str(request.get("tailnetHostname") or "")
    require(HOSTNAME.fullmatch(hostname), "Use a lowercase tailnet hostname, such as ocharo-vps")
    expected = request.get("expectedHostKey") or None
    require(expected is None or FINGERPRINT.fullmatch(expected.strip()),
            "The host key fingerprint looks like SHA256:… (ssh-keygen -lf, or the provider's console)")
    return {"serverId": server.id, "publicAddress": str(address), "sshPort": port, "loginUser": login,
            "admin": admin, "adminKey": admin_key.strip() if admin_key else None, "tailnetHostname": hostname,
            "expectedHostKey": expected.strip() if expected else None}


def start(root, request, actor, launch=True):
    """Records the enrollment and starts it detached; a rerun continues from the first step that has not succeeded."""
    require(isinstance(actor, str) and 0 < len(actor) <= 160 and "\n" not in actor, "Invalid actor")
    request = validate(root, request)
    server = request["serverId"]
    journal = load(root, server)
    require(journal is None or journal.record.get("status") != "succeeded", "This server is already enrolled")
    require(tailnet.read_client(root) is not None,
            "Connect Tailscale first: the enrollment mints a single-use key for the host")
    require((key_folder(root, server) / "identity").is_file(), "Create the one-time key first and add it to the server")
    require(not infra_jobs.busy(root, server), "Another job is running on this server; wait for it to finish")
    if journal is None:
        journal = Journal(journal_path(root, server), {
            "kind": "enrollment", "serverId": server, "attempt": 0, "facts": {}, "mints": [],
            "steps": infra_jobs.new_steps(STEPS), "createdAt": now()})
    else:
        previous = journal.record.get("request") or {}
        if journal.done("contact"):
            fixed = ("publicAddress", "sshPort", "loginUser", "tailnetHostname")
            require(all(previous.get(key) == request[key] for key in fixed),
                    "The enrollment already reached this host; rerun it with the same address, account and hostname")
            request["expectedHostKey"] = previous.get("expectedHostKey")
        if journal.done("admin"):
            request["admin"], request["adminKey"] = previous.get("admin"), previous.get("adminKey")
        for item in journal.record["steps"]:
            if item["status"] not in ("succeeded", "skipped"):
                item.update(status="pending", detail=None, startedAt=None, completedAt=None)
    journal.record.update(id=infra_jobs.job_id(), request=request, actor=actor, status="queued", reason=None,
                          queuedAt=now(),
                          startedAt=None, completedAt=None, recordedAt=None,
                          attempt=int(journal.record.get("attempt") or 0) + 1)
    journal.save()
    if launch:
        infra_jobs.spawn(Path(__file__).with_name("infra.py"), "enroll-run", "--root", str(root), "--server", server)
    return show(root, server)


def show(root, server):
    """The enrollment as the console reads it, with the one-time public key while the host still needs it."""
    require(SLUG.fullmatch(server or ""), "Invalid server")
    journal = load(root, server)
    record = infra_jobs.shown(journal.record, infra_jobs.busy(root, server)) if journal else {
        "kind": "enrollment", "serverId": server, "status": "not_started", "steps": infra_jobs.new_steps(STEPS),
        "facts": {}, "mints": []}
    identity = Path(root) / infra_jobs.INFRA / KEYS / server / "identity.pub"
    if identity.is_file() and record.get("status") != "succeeded":
        public = " ".join(identity.read_text().split()[:3])
        record["oneTimeKey"] = {"publicKey": public, "fingerprint": fingerprint(public)}
    record.pop("recordedAt", None)
    return record


class EnrollSsh:
    """The enrollment's public SSH session: the one-time key, the host key pinned at first contact, no user config."""

    def __init__(self, root, request, first_contact=False):
        folder = key_folder(root, request["serverId"])
        self.identity, self.known_hosts = folder / "identity", folder / "known_hosts"
        require(self.identity.is_file(), "The one-time key is gone; this server's enrollment has finished")
        self.request, self.first_contact = request, first_contact

    def arguments(self, command):
        return ["ssh", "-F", "/dev/null", "-o", "BatchMode=yes", "-o", "IdentitiesOnly=yes",
                "-o", "ConnectTimeout=10", "-o", "ServerAliveInterval=15", "-o", "ServerAliveCountMax=4",
                "-o", "HostKeyAlgorithms=ssh-ed25519", "-o", "GlobalKnownHostsFile=/dev/null",
                "-o", "UserKnownHostsFile=" + str(self.known_hosts), "-o", "UpdateHostKeys=no",
                "-o", "StrictHostKeyChecking=" + ("accept-new" if self.first_contact else "yes"),
                "-i", str(self.identity), "-p", str(self.request["sshPort"]),
                self.request["loginUser"] + "@" + self.request["publicAddress"], command]

    def run(self, command, stdin=b"", timeout=120):
        try:
            result = subprocess.run(self.arguments(command), input=stdin if isinstance(stdin, bytes) else stdin.encode(),
                                    capture_output=True, timeout=timeout)
        except subprocess.TimeoutExpired:
            raise CommandFailed("The host did not finish within " + str(timeout) + " seconds") from None
        if result.returncode:
            raise infra_jobs.ssh_failure(result.returncode, result.stderr.decode("utf-8", "replace"),
                                         "SSH refused the one-time key: add its public key to the sign-in account")
        return result.stdout.decode("utf-8", "replace")

    def phase(self, name, *arguments, stdin=b"", timeout=600):
        """One enroll-products.sh phase, as root: directly, or through sudo -n from the image's administrator."""
        sudo = "" if self.request["loginUser"] == "root" else "sudo -n "
        rig = "env WHEELHOUSE_REHEARSAL=1 " if rehearsal() else ""
        return self.run(sudo + rig + 'bash "$HOME/.wheelhouse-kit/enroll-products.sh" ' + shlex.join([name, *arguments]),
                        stdin, timeout)


def contact(root, journal):
    request, facts = journal.record["request"], journal.record["facts"]
    session = EnrollSsh(root, request, first_contact=True)
    answer = session.run('. /etc/os-release; printf "%s %s %s %s\\n" "$ID" "$VERSION_ID" '
                         '"$(dpkg --print-architecture 2>/dev/null || uname -m)" "$(id -u)"; '
                         'if [ "$(id -u)" != 0 ]; then sudo -n true 2>/dev/null && echo sudo || echo nosudo; fi',
                         timeout=60).split()
    pinned = [line.split() for line in session.known_hosts.read_text().splitlines() if "ssh-ed25519" in line] \
        if session.known_hosts.is_file() else []
    require(pinned, "The host presented no Ed25519 host key")
    host_key = " ".join(pinned[0][1:3])
    seen = fingerprint(host_key)
    expected = request.get("expectedHostKey")
    if expected and seen != expected:
        session.known_hosts.unlink()
        raise Rejected("The host presented " + seen + ", not the fingerprint you gave; enrollment stopped")
    facts.update(hostKey=host_key, hostKeyFingerprint=seen, hostKeyConfirmedBy="owner" if expected else "tailnet")
    require(len(answer) >= 4, "The host's sign-in answered unexpectedly")
    # The local rig runs on the machine it is on; only it may be arm64.
    architectures = ("amd64", "x86_64") + (("arm64", "aarch64") if rehearsal() else ())
    require(answer[0] == "ubuntu" and answer[1] == "26.04" and answer[2] in architectures,
            "The kit is reviewed for Ubuntu 26.04 amd64 only; this host runs " + " ".join(answer[:3])[:60])
    require(answer[3] == "0" or answer[-1] == "sudo",
            "The sign-in account needs root or passwordless sudo for the enrollment")
    return "Host key " + seen + (" matches your fingerprint" if expected else " pinned; the tailnet proves it later")


def upload(root, journal):
    entries = kit.files()
    EnrollSsh(root, journal.record["request"]).run(
        'umask 077; d="$HOME/.wheelhouse-kit"; rm -rf "$d"; mkdir -p "$d"; tar -xzf - -C "$d"',
        kit.archive(entries), timeout=120)
    journal.record["facts"]["kitDigest"] = kit.digest(entries)
    return "Kit " + journal.record["facts"]["kitDigest"][:12] + " uploaded"


def admin(root, journal):
    request = journal.record["request"]
    if request["loginUser"] != "root":
        return "Signs in as " + request["loginUser"] + ", the image's administrator"
    output = EnrollSsh(root, request).phase("admin", request["admin"],
                                            stdin=((request.get("adminKey") or "") + "\n").encode(), timeout=120)
    note = " It has no password: set one in the provider's console for recovery." if "passwd" in output else ""
    return ("Administrator " + request["admin"] + (" kept" if "kept as it is" in output else " created") + "." + note)


def packages(root, journal):
    EnrollSsh(root, journal.record["request"]).phase("packages", timeout=1800)
    return "Docker, Tailscale, OpenSSH, UFW and unattended upgrades installed; nothing upgraded"


def host(root, journal):
    EnrollSsh(root, journal.record["request"]).phase("host", timeout=900)
    return "wheelhouse-deploy, /srv/wheelhouse, the platform network, security updates and UFW in place"


def join(root, journal, session=None):
    request, facts = journal.record["request"], journal.record["facts"]
    session = session or tailnet.Tailscale(root)
    record, key = session.mint(tailnet.HOST_TAG, "wheelhouse enroll " + request["serverId"])
    journal.record["mints"].append({**record, "step": "join"})
    journal.save()
    output = EnrollSsh(root, request).phase("join", request["tailnetHostname"], stdin=(key + "\n").encode(),
                                            timeout=300)
    del key
    try:
        answer = json.loads(output.strip().splitlines()[-1])
        address = ipaddress.ip_address(answer["address"])
    except (ValueError, KeyError, IndexError, TypeError):
        raise CommandFailed("The host did not report its tailnet address") from None
    require(address in tailnet.TAILNET_V4, "The host reported an address outside the tailnet")
    facts["tailnetAddress"] = str(address)
    return "Joined as " + request["tailnetHostname"] + " (" + str(address) + "), tag:products; key " + record["keyId"]


def node(root, journal, session=None):
    """The node at the reported address must carry tag:products and report the public address among its endpoints;
    its endpoints may take a few seconds to appear after the join."""
    request, facts = journal.record["request"], journal.record["facts"]
    session = session or tailnet.Tailscale(root)
    public = ipaddress.ip_address(request["publicAddress"])
    deadline, found = time.monotonic() + NODE_SECONDS, None
    while True:
        found = next((device for device in session.devices()
                      if facts["tailnetAddress"] in (device.get("addresses") or [])), None)
        if (found is not None and public in tailnet.endpoint_hosts(found)) or time.monotonic() >= deadline:
            break
        time.sleep(5)
    require(found is not None, "No tailnet node holds " + facts["tailnetAddress"] + "; enrollment stopped")
    require(tailnet.HOST_TAG in (found.get("tags") or []), "The node lacks tag:products; enrollment stopped")
    require(public in tailnet.endpoint_hosts(found),
            "The node does not report " + str(public) + " among its endpoints: another machine may hold the key. "
            "Enrollment stopped before trusting it")
    facts.update(nodeId=str(found.get("nodeId") or found.get("id") or ""), nodeName=str(found.get("name") or ""))
    return "Node " + (facts["nodeName"] or facts["nodeId"]) + " reports " + str(public) + "; tag:products"


def runner_identity(root, server, pin):
    """Prod's runner identity for the server, created once and pinned to the tailnet address and first-seen key; an
    existing identity that pins anything else is never replaced."""
    folder = Path(root) / "ssh" / server
    identity, known_hosts = folder / "identity", folder / "known_hosts"
    if identity.exists():
        require(known_hosts.is_file() and known_hosts.read_text().strip() == pin,
                "An identity for " + server + " already pins another address or host key; review ssh/" + server)
        return identity, False
    else:
        folder.mkdir(parents=True, exist_ok=True, mode=0o700)
        result = subprocess.run(["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-C", "wheelhouse-prod-runner",
                                 "-f", str(identity)], capture_output=True, text=True, timeout=30)
        if result.returncode:
            raise CommandFailed("Creating the runner identity failed")
        temporary = folder / ".known_hosts"
        temporary.write_text(pin + "\n")
        os.chmod(temporary, 0o600)
        os.replace(temporary, known_hosts)
    return identity, True


def runner_ssh(root, journal):
    facts = journal.record["facts"]
    folder = Path(root) / "ssh" / journal.record["serverId"]
    return Ssh({"host": facts["tailnetAddress"], "user": DEPLOY_USER, "port": 22,
                "keyFile": str(folder / "identity"), "knownHostsFile": str(folder / "known_hosts")})


def runner(root, journal):
    request, facts = journal.record["request"], journal.record["facts"]
    identity, created = runner_identity(root, request["serverId"], facts["tailnetAddress"] + " " + facts["hostKey"])
    public = " ".join(Path(str(identity) + ".pub").read_text().split()[:2])
    try:
        EnrollSsh(root, request).phase("accept-runner", stdin=(public + "\n").encode(), timeout=60)
        runner_ssh(root, journal).run("true", timeout=30)
    except (Rejected, CommandFailed):
        # An identity this step created and could not prove pins nothing trustworthy; a rerun makes a new one.
        if created:
            for name in ("identity", "identity.pub", "known_hosts"):
                (identity.parent / name).unlink(missing_ok=True)
        raise
    return ("Prod's runner reaches " + DEPLOY_USER + " at " + facts["tailnetAddress"]
            + " over the tailnet, and the host key matches first contact")


def platform(root, journal):
    return placement.converge_platform(runner_ssh(root, journal))


def seal(root, journal):
    request = journal.record["request"]
    session = EnrollSsh(root, request)
    public = " ".join(Path(str(session.identity) + ".pub").read_text().split()[:2])
    session.phase("seal", request["admin"], stdin=(public + "\n").encode(), timeout=120)
    return "Keys-only SSH, no root sign-in, public SSH closed on UFW, the one-time key removed from the host"


def verify(root, journal):
    request, facts = journal.record["request"], journal.record["facts"]
    runner_ssh(root, journal).run("true", timeout=30)
    try:
        EnrollSsh(root, request).run("true", timeout=30)
        opened = True
    except CommandFailed:
        opened = False
    require(not opened, "Public SSH still accepts the one-time key; enrollment stopped")
    facts["publicSshClosed"] = True
    folder = key_folder(root, request["serverId"])
    for name in ("identity", "identity.pub"):
        path = folder / name
        if path.exists():
            with path.open("r+b") as stream:
                stream.write(b"\0" * path.stat().st_size)
            path.unlink()
    return "The runner reaches the host over the tailnet; public SSH refuses; the one-time key is deleted here too"


ACTIONS = {"contact": contact, "upload": upload, "admin": admin, "packages": packages, "host": host, "join": join,
           "node": node, "runner": runner, "platform": platform, "seal": seal, "verify": verify}


def run(root, server):
    """Runs the enrollment's remaining steps in order, holding the server's lock; stops at the first failure."""
    with infra_jobs.server_lock(root, server):
        journal = load(root, server)
        require(journal is not None and journal.record.get("status") in ("queued", "running", "failed"),
                "Start the enrollment first")
        journal.record.update(status="running", startedAt=now(), reason=None)
        journal.save()
        for key, _ in STEPS:
            if journal.done(key):
                continue
            if not journal.run(key, lambda key=key: ACTIONS[key](root, journal)):
                return show(root, server)
        journal.finish("succeeded")
        return show(root, server)
