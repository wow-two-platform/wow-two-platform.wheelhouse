#!/usr/bin/env python3
"""Keysmith: the local rig's product for generated and linked setting keys (`rehearse.py generated`). Never a real
product, and it never prints a setting's value.

  serve        answers every path on :8080 (the container's default command)
  health       the health check: the settings file Wheelhouse renders exists and holds a JSON object
  certificate  the descriptor's generate command: a throwaway self-signed certificate and its private key, as one PEM
               bundle on standard output. It refuses, before making anything, when the container has a route, an
               interface besides loopback that is up, or reaches the rig at all, so a value proves the run had no
               network.
"""
import http.server
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile

SETTINGS = Path("/app/appsettings.Target.json")
PORT = 8080
# What the certificate check tries to reach: the Docker host's published rig registry and the rig's own services. Any
# answer means the container had a network.
PROBES = (("host.docker.internal", 15000), ("ingress", 80), ("registry", 5000), ("target", 22))


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        body = b"keysmith\n"
        self.send_response(200)
        self.send_header("Content-Type", "text/plain")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *arguments):
        pass


def serve():
    http.server.ThreadingHTTPServer(("", PORT), Handler).serve_forever()


def health():
    try:
        document = json.loads(SETTINGS.read_text())
    except (OSError, ValueError):
        return 1
    return 0 if isinstance(document, dict) and document else 1


def network():
    """Why the container is not isolated, or None: a route, an interface besides loopback that is up, or any probe
    answering. The kernel's own tunnel devices (gre0, sit0, …) exist down in every namespace and do not count."""
    routes = Path("/proc/net/route").read_text().splitlines()[1:]
    if routes:
        return "routes via " + ", ".join(sorted({line.split()[0] for line in routes}))
    up = []
    for name in sorted(os.listdir("/sys/class/net")):
        try:
            state = (Path("/sys/class/net") / name / "operstate").read_text().strip()
        except OSError:
            continue
        if name != "lo" and state == "up":
            up.append(name)
    if up:
        return "interfaces up: " + ", ".join(up)
    for host, port in PROBES:
        try:
            socket.create_connection((host, port), timeout=3).close()
            return "reached " + host + ":" + str(port)
        except OSError:
            continue
    return None


def certificate():
    reason = network()
    if reason:
        print("refused: the generate container has a network (" + reason + ")", file=sys.stderr)
        return 3
    with tempfile.TemporaryDirectory() as folder:
        key, cert = Path(folder) / "key.pem", Path(folder) / "cert.pem"
        made = subprocess.run(["openssl", "req", "-x509", "-newkey", "ec", "-pkeyopt", "ec_paramgen_curve:P-256",
                               "-nodes", "-days", "30", "-subj", "/CN=keysmith-rig", "-keyout", str(key),
                               "-out", str(cert)], stdin=subprocess.DEVNULL, capture_output=True, text=True)
        if made.returncode:
            lines = [line for line in made.stderr.splitlines() if line.strip()]
            print("openssl failed: " + (lines[-1] if lines else "exit " + str(made.returncode)), file=sys.stderr)
            return 1
        sys.stdout.write(cert.read_text() + key.read_text())
    print("made a self-signed P-256 certificate for 30 days", file=sys.stderr)
    return 0


def main():
    action = sys.argv[1] if len(sys.argv) > 1 else "serve"
    if action == "serve":
        serve()
        return 0
    if action == "health":
        return health()
    if action == "certificate":
        return certificate()
    print("unknown action " + action, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
