"""Tailscale through the owner's OAuth client: the tailnet's nodes, single-use tagged auth keys, and the access policy
applied after a reviewed diff.

The client is an owner-placed file, <root>/providers/tailscale/oauth-client.json, mode 0600:
    {"clientId": "...", "clientSecret": "tskey-client-..."}
Its secret is read at each token exchange and goes to the Tailscale API only; access tokens live in memory for one
process. No secret, token or minted key is ever logged, returned to the console or written to a record: a mint is
recorded by its key ID, tags and expiry. Only Wheelhouse prod holds the file; dev never does."""
from __future__ import annotations
import difflib
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import re
import stat
import urllib.error
import urllib.parse
import urllib.request
import fleet
from catalog import rehearsal
from runner import CommandFailed, Rejected, now, require, write_json

API = "https://api.tailscale.com"
CLIENT_FILE = Path("providers") / "tailscale" / "oauth-client.json"
POLICY_DIR = Path("providers") / "tailscale" / "policy"
# The tags Wheelhouse's keys give: an enrolled products host, and a product site node. The client carries
# tag:wheelhouse, which owns both (tailnet-policy.hujson), so it can mint either and nothing else.
HOST_TAG = "tag:products"
SITE_TAG = "tag:product-site"
CONTROL_TAG = "tag:control"
KEY_SECONDS = 15 * 60
CLIENT_ID = re.compile(r"[A-Za-z0-9_-]{4,128}")
CLIENT_SECRET = re.compile(r"tskey-client-[A-Za-z0-9_-]{4,256}")
TAILNET_V4 = ipaddress.ip_network("100.64.0.0/10")
KEY_DESCRIPTION = re.compile(r"[^A-Za-z0-9 -]+")
# The policy template's markers, filled from the inventory.
HOSTS_MARKER = "// wheelhouse:hosts"
TESTS_MARKER = "// wheelhouse:tests"
TIMEOUT = 20


def api_base():
    """The Tailscale API. Only the local rig may point elsewhere (its fake), so a stray variable never sends the client
    secret to another host."""
    override = os.environ.get("WHEELHOUSE_TAILSCALE_API", "")
    if override and rehearsal():
        require(re.fullmatch(r"https?://[A-Za-z0-9.-]+(:[0-9]{1,5})?", override), "Invalid Tailscale API address")
        return override
    return API


def client_path(root):
    return Path(root) / CLIENT_FILE


def read_client(root):
    """The OAuth client, or None while the owner has not placed it; a misplaced file is refused, never used."""
    path = client_path(root)
    if not path.exists() and not path.is_symlink():
        return None
    require(not path.is_symlink() and path.is_file(), "The Tailscale OAuth client file must be a regular file")
    info = path.stat()
    require(not stat.S_IMODE(info.st_mode) & 0o077, "The Tailscale OAuth client file must be mode 0600")
    require(info.st_size <= 4096, "The Tailscale OAuth client file is malformed")
    try:
        document = json.loads(path.read_text())
    except ValueError:
        raise Rejected("The Tailscale OAuth client file is malformed") from None
    require(isinstance(document, dict) and CLIENT_ID.fullmatch(str(document.get("clientId", "")))
            and CLIENT_SECRET.fullmatch(str(document.get("clientSecret", ""))),
            "The Tailscale OAuth client file needs clientId and a tskey-client- clientSecret")
    return {"clientId": document["clientId"], "clientSecret": document["clientSecret"]}


class Tailscale:
    """One process's Tailscale API session: the token exchange happens once, on the first call."""

    def __init__(self, root, opener=None):
        self.client = read_client(root)
        require(self.client is not None, "Connect Tailscale first: place the OAuth client at " + str(CLIENT_FILE))
        self.base = api_base()
        self.opener = opener or urllib.request.urlopen
        self.token = None

    def call(self, method, path, body=None, content_type=None, headers=None, authorized=True):
        """One API call: (status, headers, body bytes). Failures name the call and status, never a body."""
        request = urllib.request.Request(self.base + path, data=body, method=method)
        if content_type:
            request.add_header("Content-Type", content_type)
        for name, value in (headers or {}).items():
            request.add_header(name, value)
        if authorized:
            request.add_header("Authorization", "Bearer " + self.access_token())
        try:
            with self.opener(request, timeout=TIMEOUT) as response:
                return response.status, dict(response.headers.items()), response.read()
        except urllib.error.HTTPError as error:
            payload = error.read() if error.fp else b""
            return error.code, dict(error.headers.items()) if error.headers else {}, payload
        except (urllib.error.URLError, OSError, ValueError):
            raise CommandFailed("Tailscale API is unreachable") from None

    def access_token(self):
        if self.token is None:
            form = urllib.parse.urlencode({"grant_type": "client_credentials", "client_id": self.client["clientId"],
                                           "client_secret": self.client["clientSecret"]}).encode()
            status, _, body = self.call("POST", "/api/v2/oauth/token", form, "application/x-www-form-urlencoded",
                                        authorized=False)
            require(status != 401 and status != 400, "Tailscale refused the OAuth client (HTTP " + str(status) + ")")
            if status != 200:
                raise CommandFailed("Tailscale token exchange failed (HTTP " + str(status) + ")")
            try:
                token = json.loads(body).get("access_token")
            except ValueError:
                token = None
            if not isinstance(token, str) or not token:
                raise CommandFailed("Tailscale token exchange returned no token")
            self.token = token
        return self.token

    def json(self, method, path, value=None, expect=(200,)):
        body = None if value is None else json.dumps(value).encode()
        status, _, payload = self.call(method, path, body, "application/json" if body is not None else None)
        if status == 403:
            raise Rejected("The Tailscale OAuth client lacks the scope for " + path.split("?")[0].rsplit("/", 1)[-1])
        if status not in expect:
            raise CommandFailed("Tailscale " + method + " " + path.split("?")[0] + " failed (HTTP " + str(status) + ")")
        try:
            return json.loads(payload or b"{}")
        except ValueError:
            raise CommandFailed("Tailscale returned an unreadable answer") from None

    def devices(self):
        """Every device in the tailnet, with all fields (endpoints included)."""
        devices = self.json("GET", "/api/v2/tailnet/-/devices?fields=all").get("devices")
        require(isinstance(devices, list), "Tailscale returned an unreadable device list")
        return [device for device in devices if isinstance(device, dict)]

    def mint(self, tag, description, seconds=KEY_SECONDS):
        """A single-use, pre-approved, non-ephemeral auth key carrying one tag. Returns (record, key): the record holds
        what may be kept (ID, tag, expiry); the key itself goes on SSH stdin and nowhere else."""
        require(tag in (HOST_TAG, SITE_TAG), "Wheelhouse mints keys for products hosts and site nodes only")
        description = KEY_DESCRIPTION.sub(" ", description).strip()[:50] or "wheelhouse"
        answer = self.json("POST", "/api/v2/tailnet/-/keys", {
            "capabilities": {"devices": {"create": {"reusable": False, "ephemeral": False, "preauthorized": True,
                                                    "tags": [tag]}}},
            "expirySeconds": seconds, "description": description})
        key, identifier = answer.get("key"), answer.get("id")
        require(isinstance(key, str) and key.startswith("tskey-") and isinstance(identifier, str),
                "Tailscale returned no auth key")
        return {"keyId": identifier[:64], "tags": [tag], "expires": str(answer.get("expires") or "")[:40],
                "description": description, "mintedAt": now()}, key

    def policy(self):
        """The live policy as HuJSON, with its ETag."""
        status, headers, payload = self.call("GET", "/api/v2/tailnet/-/acl", headers={"Accept": "application/hujson"})
        if status == 403:
            raise Rejected("The Tailscale OAuth client lacks the policy_file scope")
        if status != 200:
            raise CommandFailed("Reading the tailnet policy failed (HTTP " + str(status) + ")")
        etag = next((value for name, value in headers.items() if name.lower() == "etag"), None)
        require(isinstance(etag, str) and etag, "Tailscale returned the policy without an ETag")
        return payload.decode("utf-8", "replace"), etag

    def validate(self, text):
        """Tailscale's own validation, tests included, without saving: a list of display-safe problems."""
        status, _, payload = self.call("POST", "/api/v2/tailnet/-/acl/validate", text.encode(), "application/hujson")
        if status == 403:
            raise Rejected("The Tailscale OAuth client lacks the policy_file scope")
        problems = []
        try:
            answer = json.loads(payload or b"{}")
        except ValueError:
            answer = {}
        if status != 200 or answer.get("message"):
            if isinstance(answer.get("message"), str):
                problems.append(answer["message"][:300])
            for item in answer.get("data") or []:
                if isinstance(item, dict):
                    for error in (item.get("errors") or [])[:10]:
                        problems.append((str(item.get("user") or "") + ": " + str(error))[:300].lstrip(": "))
            if status != 200 and not problems:
                problems.append("Tailscale refused the policy (HTTP " + str(status) + ")")
        return problems

    def set_policy(self, text, etag):
        """Saves the policy only while the live one still carries `etag`; Tailscale runs its tests first."""
        status, headers, payload = self.call("POST", "/api/v2/tailnet/-/acl", text.encode(), "application/hujson",
                                             headers={"If-Match": etag, "Accept": "application/hujson"})
        if status == 412:
            raise Rejected("The live tailnet policy changed since this review; review the diff again")
        if status == 403:
            raise Rejected("The Tailscale OAuth client lacks the policy_file scope")
        if status == 400:
            try:
                message = str(json.loads(payload).get("message") or "")[:200]
            except ValueError:
                message = ""
            raise Rejected("Tailscale refused the policy" + (": " + message if message else ""))
        if status != 200:
            raise CommandFailed("Saving the tailnet policy failed (HTTP " + str(status) + ")")
        return next((value for name, value in headers.items() if name.lower() == "etag"), None)


def tailnet_domain(devices):
    """The tailnet's MagicDNS suffix, such as tail1234.ts.net, from its devices' names."""
    for device in devices:
        name = str(device.get("name") or "")
        parts = name.rstrip(".").split(".", 1)
        if len(parts) == 2 and parts[1].endswith(".ts.net"):
            return parts[1]
    return None


def ipv4_of(device):
    return next((address for address in device.get("addresses") or []
                 if isinstance(address, str) and _in_tailnet(address)), None)


def _in_tailnet(address):
    try:
        return ipaddress.ip_address(address) in TAILNET_V4
    except ValueError:
        return False


def endpoint_hosts(device):
    """The IP addresses of a device's magicsock endpoints ("203.0.113.5:41641", "[2001:db8::1]:41641")."""
    hosts = []
    for endpoint in (device.get("clientConnectivity") or {}).get("endpoints") or []:
        host = str(endpoint).rsplit(":", 1)[0].strip("[]")
        try:
            hosts.append(ipaddress.ip_address(host))
        except ValueError:
            continue
    return hosts


def node_view(device):
    """What the console may show of a device: identity, addresses, tags, presence and key expiry; never keys."""
    return {"id": str(device.get("nodeId") or device.get("id") or ""), "name": str(device.get("name") or ""),
            "hostname": str(device.get("hostname") or ""),
            "addresses": [address for address in device.get("addresses") or [] if isinstance(address, str)],
            "tags": [tag for tag in device.get("tags") or [] if isinstance(tag, str)],
            "online": bool(device.get("connectedToControl")), "lastSeen": device.get("lastSeen"),
            "keyExpiryDisabled": bool(device.get("keyExpiryDisabled")), "expires": device.get("expires"),
            "created": device.get("created"), "os": device.get("os"), "authorized": device.get("authorized")}


def recorded_nodes(root):
    """Server ID -> the tailnet node its enrollment recorded."""
    result = {}
    folder = Path(root) / "infra" / "enrollments"
    for path in folder.glob("*.json") if folder.is_dir() else ():
        try:
            record = json.loads(path.read_text())
            node = (record.get("facts") or {}).get("nodeId")
            if isinstance(node, str) and node:
                result[path.stem] = node
        except (ValueError, OSError):
            continue
    return result


def status(root, session=None):
    """Whether the OAuth client is in place and works, and each server's node: matched by its tailnet address, or by
    the node its enrollment recorded. Untagged devices that no server names (people's laptops) are left out."""
    result = {"configured": False, "connected": False, "reason": None, "tailnet": None,
              "clientFile": str(CLIENT_FILE), "nodes": [], "servers": []}
    try:
        if read_client(root) is None:
            result["reason"] = "Place the OAuth client at <runner root>/" + str(CLIENT_FILE) + ", mode 0600"
            return result
        result["configured"] = True
        devices = (session or Tailscale(root)).devices()
    except (Rejected, CommandFailed) as error:
        result["reason"] = str(error)[:300]
        return result
    result["connected"] = True
    result["tailnet"] = tailnet_domain(devices)
    recorded = recorded_nodes(root)
    by_server = {}
    for server in fleet.active_servers():
        if server.provider is fleet.VpsProvider.LOCAL:
            continue  # the rig's own server is never on a tailnet
        node = next((device for device in devices if server.host in (device.get("addresses") or [])), None)
        if node is None and server.id in recorded:
            node = next((device for device in devices if recorded[server.id] in (device.get("nodeId"), device.get("id"))),
                        None)
        by_server[server.id] = node
        result["servers"].append({"serverId": server.id, "nodeId": str(node.get("nodeId") or node.get("id"))
                                  if node else None})
    owners = {id(node): server for server, node in by_server.items() if node is not None}
    for device in devices:
        if not device.get("tags") and id(device) not in owners:
            continue
        view = node_view(device)
        view["serverId"] = owners.get(id(device))
        tags = view["tags"]
        view["role"] = ("host" if HOST_TAG in tags else "site" if SITE_TAG in tags
                        else "control" if CONTROL_TAG in tags else "other")
        result["nodes"].append(view)
    return result


# --- The access policy ------------------------------------------------------------------------------------------


def standard_json(text):
    """HuJSON to JSON: comments and trailing commas removed, strings kept exactly."""
    output, index, length, in_string = [], 0, len(text), False
    while index < length:
        char = text[index]
        if in_string:
            output.append(char)
            if char == "\\" and index + 1 < length:
                output.append(text[index + 1])
                index += 1
            elif char == '"':
                in_string = False
        elif char == '"':
            in_string = True
            output.append(char)
        elif text.startswith("//", index):
            end = text.find("\n", index)
            index = length if end < 0 else end
            continue
        elif text.startswith("/*", index):
            end = text.find("*/", index + 2)
            require(end >= 0, "The policy has an unterminated comment")
            index = end + 2
            continue
        else:
            output.append(char)
        index += 1
    return re.sub(r",(\s*[}\]])", r"\1", "".join(output))


def parse_policy(text):
    try:
        document = json.loads(standard_json(text))
    except ValueError:
        raise Rejected("The policy is not valid HuJSON") from None
    require(isinstance(document, dict), "The policy is not valid HuJSON")
    return document


def control_path_kept(text):
    """Wheelhouse's own guard: the policy's tests must still accept tag:control to tag:products:22, the path prod's
    runner deploys over. A policy without that test is refused even if Tailscale would save it."""
    tests = parse_policy(text).get("tests")
    return isinstance(tests, list) and any(
        isinstance(test, dict) and test.get("src") == CONTROL_TAG
        and HOST_TAG + ":22" in (test.get("accept") or []) for test in tests)


def products_hosts():
    """The inventory's products hosts: servers reached at a tailnet address, by server ID."""
    return sorted((server.id, server.host) for server in fleet.active_servers()
                  if server.provider is not fleet.VpsProvider.LOCAL and _in_tailnet(server.host))


def render(template, hosts):
    """The template with one host alias and one control-path test per products host, at its markers."""
    lines = template.splitlines()
    require(sum(line.strip() == HOSTS_MARKER for line in lines) == 1
            and sum(line.strip() == TESTS_MARKER for line in lines) == 1,
            "The policy template must hold each wheelhouse: marker once")
    output = []
    for line in lines:
        indent = line[:len(line) - len(line.lstrip())]
        if line.strip() == HOSTS_MARKER:
            output.append(indent + "// Filled by Wheelhouse from its inventory.")
            output.extend(indent + json.dumps(name) + ": " + json.dumps(address) + "," for name, address in hosts)
        elif line.strip() == TESTS_MARKER:
            output.append(indent + "// Filled by Wheelhouse: the control host keeps each products host's SSH.")
            output.extend(indent + json.dumps({"src": CONTROL_TAG, "accept": [name + ":22"]}) + ","
                          for name, _ in hosts)
        else:
            output.append(line)
    return "\n".join(output) + "\n"


def digest(text):
    return hashlib.sha256(text.encode()).hexdigest()


def diff(old, new, context=3):
    """The change as hunks of lines the console shows: kind is same, add or remove, with old and new line numbers."""
    before, after = old.splitlines(), new.splitlines()
    hunks = []
    for group in difflib.SequenceMatcher(None, before, after, autojunk=False).get_grouped_opcodes(context):
        lines = []
        for tag, i1, i2, j1, j2 in group:
            if tag == "equal":
                lines.extend({"kind": "same", "old": i + 1, "new": j1 + offset + 1, "text": before[i]}
                             for offset, i in enumerate(range(i1, i2)))
                continue
            if tag in ("replace", "delete"):
                lines.extend({"kind": "remove", "old": i + 1, "new": None, "text": before[i]} for i in range(i1, i2))
            if tag in ("replace", "insert"):
                lines.extend({"kind": "add", "old": None, "new": j + 1, "text": after[j]} for j in range(j1, j2))
        hunks.append({"oldStart": group[0][1] + 1, "newStart": group[0][3] + 1, "lines": lines})
    return hunks


def template_path():
    from kit import kit_dir  # deferred: the kit is looked up only when a policy is rendered
    return kit_dir() / "tailnet-policy.hujson"


def previous_record(root):
    path = Path(root) / POLICY_DIR / "previous.json"
    try:
        record = json.loads(path.read_text())
        text = (Path(root) / POLICY_DIR / "previous.hujson").read_text()
    except (ValueError, OSError):
        return None, None
    return (record if isinstance(record, dict) else None), text


def policy_review(root, session=None):
    """The live policy beside the one Wheelhouse renders, the diff between them, Tailscale's validation of the rendered
    one and Wheelhouse's own guard. Nothing changes."""
    session = session or Tailscale(root)
    live, etag = session.policy()
    rendered = render(template_path().read_text(), products_hosts())
    problems = session.validate(rendered) if live != rendered else []
    record, _ = previous_record(root)
    return {"etag": etag, "live": live, "rendered": rendered, "renderedDigest": digest(rendered),
            "changed": live != rendered, "diff": diff(live, rendered),
            "hosts": [{"serverId": name, "address": address} for name, address in products_hosts()],
            "validation": {"ok": not problems, "problems": problems},
            "guard": {"ok": control_path_kept(rendered),
                      "reason": None if control_path_kept(rendered)
                      else "The rendered policy no longer accepts tag:control to tag:products:22"},
            "previous": None if record is None else {key: record.get(key) for key in ("savedAt", "actor", "digest")}}


def keep_previous(root, text, actor):
    """The live policy before a change, so one click can put it back."""
    folder = Path(root) / POLICY_DIR
    folder.mkdir(parents=True, exist_ok=True, mode=0o700)
    temporary = folder / ".previous.hujson"
    temporary.write_text(text)
    os.chmod(temporary, 0o600)
    os.replace(temporary, folder / "previous.hujson")
    write_json(folder / "previous.json", {"savedAt": now(), "actor": actor, "digest": digest(text)})


def apply_policy(root, etag, reviewed_digest, actor, session=None):
    """Applies the rendered policy the owner reviewed: the same digest, the same live ETag, Tailscale's validation and
    Wheelhouse's guard. The live policy before it is kept for one-click revert."""
    require(isinstance(etag, str) and 0 < len(etag) <= 200, "Review the policy first")
    require(isinstance(reviewed_digest, str) and re.fullmatch(r"[a-f0-9]{64}", reviewed_digest), "Review the policy first")
    session = session or Tailscale(root)
    rendered = render(template_path().read_text(), products_hosts())
    require(digest(rendered) == reviewed_digest, "The inventory changed since this review; review the diff again")
    require(control_path_kept(rendered), "The policy would cut tag:control from tag:products:22; refused")
    live, live_etag = session.policy()
    require(live_etag == etag, "The live tailnet policy changed since this review; review the diff again")
    require(live != rendered, "The live policy already matches; nothing to apply")
    problems = session.validate(rendered)
    require(not problems, "Tailscale refused the policy: " + "; ".join(problems)[:250])
    keep_previous(root, live, actor)
    new_etag = session.set_policy(rendered, etag)
    return {"applied": True, "digest": reviewed_digest, "etag": new_etag, "appliedAt": now()}


def revert_policy(root, etag, actor, session=None):
    """Puts back the policy that was live before the last change Wheelhouse made, under the same checks; the policy it
    replaces becomes the one a second revert restores."""
    require(isinstance(etag, str) and 0 < len(etag) <= 200, "Review the policy first")
    record, previous = previous_record(root)
    require(record is not None and previous, "No earlier policy is kept")
    require(control_path_kept(previous), "The earlier policy would cut tag:control from tag:products:22; refused")
    session = session or Tailscale(root)
    live, live_etag = session.policy()
    require(live_etag == etag, "The live tailnet policy changed since this review; review it again")
    require(live != previous, "The live policy already matches the earlier one")
    problems = session.validate(previous)
    require(not problems, "Tailscale refused the earlier policy: " + "; ".join(problems)[:250])
    keep_previous(root, live, actor)
    new_etag = session.set_policy(previous, etag)
    return {"reverted": True, "digest": digest(previous), "etag": new_etag, "revertedAt": now()}

