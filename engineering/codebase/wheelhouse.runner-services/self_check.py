#!/usr/bin/env python3
"""Wheelhouse's declared dev check (deploy.yml `verify`): the checks CI ran after its own deploys, now run by the runner
after each wheelhouse-dev rollout, from the new console image, on the console's network.

It asks the new console, at its service address (VERIFY_SERVICE_CONSOLE), for its database readiness, its status and an
MCP initialization without credentials, which it must refuse. Every request names `localhost` as its host, which every
console allows for its own health probes. The runner keeps the last line printed, so the last line sums it up; no
response body is printed. Exits 0 when every check passes."""
import json
import os
import sys
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, Request, build_opener

SERVICE = "VERIFY_SERVICE_CONSOLE"
TIMEOUT = 20


class NoRedirect(HTTPRedirectHandler):
    """A redirect is an answer to check, never one to follow."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def ask(base, path, method="GET", body=None):
    """The status code and body of one request to the console; (None, b"") when it does not answer."""
    headers = {"Host": "localhost", "User-Agent": "wheelhouse-self-check", "Accept": "application/json"}
    if body is not None:
        headers.update({"Content-Type": "application/json", "Accept": "application/json, text/event-stream"})
    request = Request(base.rstrip("/") + path, method=method, headers=headers,
                      data=None if body is None else json.dumps(body).encode())
    try:
        with build_opener(NoRedirect()).open(request, timeout=TIMEOUT) as response:
            return response.status, response.read(4096)
    except HTTPError as error:
        return error.code, b""
    except (URLError, OSError, ValueError):
        return None, b""


def service_name(body):
    try:
        document = json.loads(body)
    except ValueError:
        return None
    return document.get("service") if isinstance(document, dict) else None


def checks(base, request=ask):
    """Each check's name and whether it passed, with an operator-safe detail."""
    results = []
    status, _ = request(base, "/api/system/ready")
    results.append(("readiness", status == 200, "answered " + str(status) if status else "no answer"))
    status, body = request(base, "/api/system/status")
    results.append(("status", status == 200 and service_name(body) == "Wheelhouse",
                    "answered " + str(status) if status else "no answer"))
    status, _ = request(base, "/mcp", "POST", {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {}})
    results.append(("anonymous MCP refused", status == 401, "answered " + str(status) if status else "no answer"))
    return results


def main(environment=None, request=ask, output=sys.stdout):
    environment = os.environ if environment is None else environment
    base = environment.get(SERVICE, "")
    if not base.startswith(("http://", "https://")):
        print("No console address: " + SERVICE + " is not set", file=output)
        return 2
    results = checks(base, request)
    for name, ok, detail in results:
        print(("passed " if ok else "failed ") + name + " (" + detail + ")", file=output)
    failed = [name for name, ok, _ in results if not ok]
    print(("Failed: " + ", ".join(failed)) if failed else
          "Passed: readiness, status and anonymous MCP refusal", file=output)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
