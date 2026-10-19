#!/usr/bin/env python3
"""A stand-in for the Tailscale API, for the runner's tests and the local rig only; it never touches a real tailnet.

It answers the calls Wheelhouse makes with its OAuth client: the token exchange, the device list, auth key mints, and
the policy file (read with an ETag, validated, saved behind If-Match). Its validation runs the one kind of test
Wheelhouse adds, tag:control accepting <host alias>:22, against the tags the devices here carry, as Tailscale does.

Rig hosts join through POST /rig/join with a key minted here: single-use, unexpired, tagged as minted. The rig's stub
`tailscale` CLI and stub site-node image call it, so a minted key must really travel to the host to be used.
GET /rig/state shows the keys (never their secrets) and devices."""
import argparse
import datetime
import hashlib
import ipaddress
import json
import re
import secrets
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import urllib.parse

OWNED_TAGS = {"tag:products", "tag:product-site"}


def standard_json(text):
    output, index, in_string = [], 0, False
    while index < len(text):
        char = text[index]
        if in_string:
            output.append(char)
            if char == "\\":
                output.append(text[index + 1])
                index += 1
            elif char == '"':
                in_string = False
        elif char == '"':
            in_string = True
            output.append(char)
        elif text.startswith("//", index):
            end = text.find("\n", index)
            index = len(text) if end < 0 else end
            continue
        else:
            output.append(char)
        index += 1
    return re.sub(r",(\s*[}\]])", r"\1", "".join(output))


class Tailnet:
    def __init__(self, client_id, client_secret, domain="rehearsal.ts.net", policy="{}\n"):
        self.client_id, self.client_secret, self.domain = client_id, client_secret, domain
        self.tokens, self.keys, self.devices = set(), {}, []
        self.policy = policy
        self.lock = threading.Lock()
        self.next_address = 2
        self.calls = []

    def etag(self):
        return '"' + hashlib.sha256(self.policy.encode()).hexdigest()[:16] + '"'

    def problems(self, text):
        try:
            policy = json.loads(standard_json(text))
        except ValueError:
            return {"message": "parse error"}
        if not isinstance(policy, dict):
            return {"message": "parse error"}
        hosts = policy.get("hosts") or {}
        errors = []
        for test in policy.get("tests") or []:
            if test.get("src") != "tag:control":
                continue
            for destination in test.get("accept") or []:
                name, _, port = destination.rpartition(":")
                if name.startswith("tag:"):
                    continue
                address = hosts.get(name, name)
                device = next((item for item in self.devices if address in item["addresses"]), None)
                if port != "22" or device is None or "tag:products" not in device["tags"]:
                    errors.append('address "' + destination + '": want: Accept, got: Drop')
        return {"message": "test(s) failed", "data": [{"user": "tag:control", "errors": errors}]} if errors else None

    def join(self, body):
        with self.lock:
            key = self.keys.get(body.get("authKey"))
            now = datetime.datetime.now(datetime.timezone.utc)
            if key is None or key["used"] or datetime.datetime.fromisoformat(key["expires"]) < now:
                return 401, {"message": "invalid key"}
            tags = sorted(body.get("tags") or [])
            if tags != sorted(key["tags"]):
                return 403, {"message": "requested tags are not permitted"}
            key["used"] = True
            hostname = str(body.get("hostname"))
            taken = {item["name"] for item in self.devices}
            name, suffix = hostname + "." + self.domain, 1
            while name in taken:
                name, suffix = hostname + "-" + str(suffix) + "." + self.domain, suffix + 1
            address = body.get("address") or "100.88.9." + str(self.next_address)
            self.next_address += 1
            node = "n" + secrets.token_hex(6) + "CNTRL"
            self.devices.append({
                "id": str(len(self.devices) + 1000), "nodeId": node, "name": name, "hostname": hostname,
                "addresses": [address], "tags": key["tags"], "connectedToControl": True, "keyExpiryDisabled": True,
                "created": now.isoformat().replace("+00:00", "Z"), "os": "linux", "authorized": True,
                "clientConnectivity": {"endpoints": list(body.get("endpoints") or [])}})
            return 200, {"nodeId": node, "address": address, "name": name}


def handler(tailnet):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def send(self, status, value, headers=None, raw=None, content_type="application/json"):
            body = raw if raw is not None else json.dumps(value).encode()
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            for name, header in (headers or {}).items():
                self.send_header(name, header)
            self.end_headers()
            self.wfile.write(body)

        def body(self):
            return self.rfile.read(int(self.headers.get("Content-Length") or 0))

        def authorized(self):
            header = self.headers.get("Authorization") or ""
            if not header.startswith("Bearer ") or header[7:] not in tailnet.tokens:
                self.send(401, {"message": "unauthorized"})
                return False
            return True

        def do_GET(self):
            path = urllib.parse.urlsplit(self.path).path
            tailnet.calls.append(("GET", path))
            if path == "/rig/state":
                with tailnet.lock:
                    keys = [{key: item[key] for key in ("id", "tags", "used", "expires", "expirySeconds", "reusable",
                                                        "preauthorized", "ephemeral")} for item in tailnet.keys.values()]
                    return self.send(200, {"keys": keys, "devices": tailnet.devices, "policyEtag": tailnet.etag()})
            if not self.authorized():
                return
            if path == "/api/v2/tailnet/-/devices":
                with tailnet.lock:
                    return self.send(200, {"devices": tailnet.devices})
            if path == "/api/v2/tailnet/-/acl":
                with tailnet.lock:
                    return self.send(200, None, {"ETag": tailnet.etag()}, tailnet.policy.encode(), "application/hujson")
            self.send(404, {"message": "not found"})

        def do_POST(self):
            path = urllib.parse.urlsplit(self.path).path
            tailnet.calls.append(("POST", path))
            body = self.body()
            if path == "/api/v2/oauth/token":
                form = urllib.parse.parse_qs(body.decode())
                if (form.get("client_id") != [tailnet.client_id] or form.get("client_secret") != [tailnet.client_secret]
                        or form.get("grant_type") != ["client_credentials"]):
                    return self.send(401, {"message": "invalid client"})
                token = "tskey-api-" + secrets.token_hex(16)
                tailnet.tokens.add(token)
                return self.send(200, {"access_token": token, "token_type": "Bearer", "expires_in": 3600})
            if path == "/rig/join":
                status, answer = tailnet.join(json.loads(body or b"{}"))
                return self.send(status, answer)
            if not self.authorized():
                return
            if path == "/api/v2/tailnet/-/keys":
                request = json.loads(body or b"{}")
                create = ((request.get("capabilities") or {}).get("devices") or {}).get("create") or {}
                tags = create.get("tags") or []
                if not tags or not set(tags) <= OWNED_TAGS:
                    return self.send(400, {"message": "requested tags are invalid or not permitted"})
                identifier = "k" + secrets.token_hex(6) + "CNTRL"
                key = "tskey-auth-" + identifier + "-" + secrets.token_hex(16)
                seconds = int(request.get("expirySeconds") or 90 * 86400)
                expires = datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(seconds=seconds)
                with tailnet.lock:
                    tailnet.keys[key] = {"id": identifier, "tags": tags, "used": False, "expires": expires.isoformat(),
                                         "expirySeconds": seconds, "reusable": bool(create.get("reusable")),
                                         "preauthorized": bool(create.get("preauthorized")),
                                         "ephemeral": bool(create.get("ephemeral"))}
                return self.send(200, {"id": identifier, "key": key, "created": datetime.datetime.now(
                    datetime.timezone.utc).isoformat(), "expires": expires.isoformat(),
                    "capabilities": request.get("capabilities")})
            if path == "/api/v2/tailnet/-/acl/validate":
                with tailnet.lock:
                    problems = tailnet.problems(body.decode())
                return self.send(200, problems or {})
            if path == "/api/v2/tailnet/-/acl":
                with tailnet.lock:
                    if self.headers.get("If-Match") not in (None, tailnet.etag()):
                        return self.send(412, {"message": "precondition failed, invalid old hash"})
                    problems = tailnet.problems(body.decode())
                    if problems:
                        return self.send(400, problems)
                    tailnet.policy = body.decode()
                    return self.send(200, None, {"ETag": tailnet.etag()}, tailnet.policy.encode(), "application/hujson")
            self.send(404, {"message": "not found"})

    return Handler


def serve(tailnet, port=0, host="127.0.0.1"):
    """Starts the fake in a background thread; returns the server (its port is server.server_address[1])."""
    server = ThreadingHTTPServer((host, port), handler(tailnet))
    threading.Thread(target=server.serve_forever, kwargs={"poll_interval": 0.05}, daemon=True).start()
    return server


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8080)
    parser.add_argument("--client-file", required=True, help="the OAuth client JSON the rig's console holds")
    parser.add_argument("--policy-file", required=True, help="the policy the fake tailnet starts with")
    parser.add_argument("--tailnet", default="rehearsal.ts.net")
    parser.add_argument("--control-address", help="a tag:control device at this address, as the control host")
    args = parser.parse_args()
    client = json.load(open(args.client_file))
    tailnet = Tailnet(client["clientId"], client["clientSecret"], args.tailnet, open(args.policy_file).read())
    if args.control_address:
        ipaddress.ip_address(args.control_address)
        tailnet.devices.append({"id": "1", "nodeId": "nControlCNTRL", "name": "wheelhouse." + args.tailnet,
                                "hostname": "wheelhouse", "addresses": [args.control_address], "tags": ["tag:control"],
                                "connectedToControl": True, "keyExpiryDisabled": True, "os": "linux",
                                "created": "2026-10-01T00:00:00Z", "authorized": True,
                                "clientConnectivity": {"endpoints": []}})
    server = ThreadingHTTPServer(("0.0.0.0", args.port), handler(tailnet))
    print("Fake Tailscale API on :" + str(args.port), flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
