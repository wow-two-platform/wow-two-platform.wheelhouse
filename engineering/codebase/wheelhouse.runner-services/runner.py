#!/usr/bin/env python3
"""Provider-neutral, target-side release executor. Requires Python 3 and Docker Compose v2."""
import argparse
import base64
import contextlib
import datetime
import fcntl
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import shutil
import socket
import ssl
import subprocess
import sys
import tempfile
import time
import urllib.parse
import uuid

SLUG = re.compile(r"[a-z][a-z0-9-]{0,47}")
IMAGE = re.compile(r"[a-z0-9][a-z0-9./:_-]*@sha256:[a-f0-9]{64}")
SHA = re.compile(r"[a-f0-9]{40}")
RELEASE = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,95}")
VERSION = re.compile(r"[0-9A-Za-z][0-9A-Za-z.+-]{0,63}")
BRANCH = re.compile(r"[A-Za-z0-9._/-]{1,200}")
HOST = re.compile(r"(?=.{1,253}$)[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+")
SITE_PATH = re.compile(r"/(?:[A-Za-z0-9._~-]+/)*(?:[A-Za-z0-9._~-]+)?")
ENTRY_POINT = re.compile(r"[a-z][a-z0-9-]{0,31}")
PROBE_ADDRESS = re.compile(r"https?://[A-Za-z0-9.-]+(?::[0-9]{1,5})?")
# Traefik's own answer for a host it has no router for; an application's 404 carries its own body.
NO_ROUTE = b"404 page not found"
LOG_LINE_LIMIT = 2000
# Releases whose images a target keeps for redeploys; older images are removed once no container uses them.
KEEP_RELEASES = 3
PRUNED = "images-removed"
KINDS = ("release", "candidate")
# Host platform services a release may declare it needs; see the deploy descriptor convention.
PLATFORM_SERVICES = ("postgres", "valkey", "broker")
EXPOSURES = ("public", "private")
TERMINAL = {"succeeded", "failed", "rolled_back", "rollback_failed", "interrupted", "rejected"}
PROXIES = "Deployment:TrustedProxies"
# What a target remembers about its verified release: identity, service versions and published sites.
CURRENT = ("id", "release", "kind", "branch", "sourceCommit", "versions", "sites")


class Rejected(ValueError):
    """A refused precondition. Messages hold only static text and contract key names."""


class CommandFailed(RuntimeError):
    """A failed external step. Messages hold only the step name and exit status."""


def require(condition, message):
    if not condition:
        raise Rejected(message)


def reason(error):
    # Any other message can echo inputs or credential values; only these carry operator-safe text.
    return str(error)[:300] if isinstance(error, (Rejected, CommandFailed)) else None


def rejection(error):
    result = {"status": "rejected", "failure": type(error).__name__}
    if reason(error):
        result["reason"] = reason(error)
    return result


def read_json(path):
    return json.loads(Path(path).read_text())


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    # Browser reads, background observations and explicit reconciliation can publish concurrently.
    # Each writer owns its temporary file; replacement still publishes one complete document.
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", dir=path.parent, prefix="." + path.name + ".",
                                         suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            os.chmod(temporary, 0o600)
            json.dump(value, stream, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
    directory = os.open(path.parent, os.O_RDONLY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def validate_bundle(bundle):
    bundle = Path(bundle).resolve()
    manifest = read_json(bundle / "release.json")
    compose_path = bundle / "compose.json"
    compose = read_json(compose_path)
    require(manifest.get("schemaVersion") == 1, "Unsupported release schema")
    require(SLUG.fullmatch(manifest.get("product", "")), "Invalid product")
    require(RELEASE.fullmatch(manifest.get("release", "")), "Invalid release")
    require(manifest.get("kind", "release") in KINDS, "Unsupported release kind")
    require(manifest.get("branch") is None or BRANCH.fullmatch(str(manifest["branch"])), "Invalid branch")
    require(SHA.fullmatch(manifest.get("sourceCommit", "")), "A full source commit is required")
    require(manifest.get("platform") in ("linux/amd64", "linux/arm64"), "Unsupported platform")
    require(type(manifest.get("rollbackCompatible")) is bool, "Declare rollback compatibility")
    require(hashlib.sha256(compose_path.read_bytes()).hexdigest() == manifest.get("composeSha256"), "Compose hash mismatch")
    images = manifest.get("images", {})
    services = compose.get("services", {})
    require(images and images.keys() == services.keys(), "Service image map mismatch")
    for name, image in images.items():
        require(SLUG.fullmatch(name) and IMAGE.fullmatch(image), "Services require immutable image digests")
        service = services[name]
        require(service.get("image") == image, "Compose image differs from release")
        require(service.get("platform") == manifest["platform"], "Compose platform differs from release")
        require("build" not in service, "Releases cannot build on the target")
        health = service.get("healthcheck", {})
        require(health.get("test") and not health.get("disable"), "Every service requires a health gate")
        require(health["test"][0] != "NONE", "Disabled health gate")
        require(not service.get("privileged") and service.get("network_mode") != "host", "Privileged workload denied")
        require(not service.get("container_name"), "Global container names prevent environment isolation")
    # A service without an entry takes no private settings file (a static edge, a worker configured by code).
    required = manifest.get("requiredConfiguration", {})
    require(isinstance(required, dict) and set(required) <= set(services),
            "Configuration contract names an unknown service")
    for fields in required.values():
        require(isinstance(fields, list) and all(isinstance(key, str) for key in fields), "Invalid configuration contract")
    versions = manifest.get("versions", {})
    require(isinstance(versions, dict) and set(versions) <= set(services), "Versions name an unknown service")
    for entry in versions.values():
        require(isinstance(entry, dict) and VERSION.fullmatch(str(entry.get("version", "")))
                and RELEASE.fullmatch(str(entry.get("changedIn", ""))), "Invalid service version")
    # The port each service listens on inside its container; a release without the map listens on 8080.
    ports = manifest.get("ports", {})
    require(isinstance(ports, dict) and set(ports) <= set(services)
            and all(type(port) is int and 1 <= port <= 65535 for port in ports.values()), "Invalid service ports")
    release_sites(manifest)
    return manifest


def service_port(manifest, service):
    return manifest.get("ports", {}).get(service, 8080)


def release_sites(manifest):
    """The entry points a release declares, one row per service site: port, path prefix and exposure."""
    declared = manifest.get("sites", {})
    require(isinstance(declared, dict) and set(declared) <= set(manifest.get("images", {})),
            "Sites name an unknown service")
    result, claimed = [], set()
    for service, sites in sorted(declared.items()):
        require(isinstance(sites, dict), "Invalid site declaration")
        for name, site in sorted(sites.items()):
            require(isinstance(name, str) and SLUG.fullmatch(name) and isinstance(site, dict), "Invalid site declaration")
            port, path, exposure = site.get("port", 8080), site.get("path", "/"), site.get("exposure", "public")
            require(type(port) is int and 1 <= port <= 65535, "Invalid site port")
            require(isinstance(path, str) and SITE_PATH.fullmatch(path), "Invalid site path")
            require(exposure in EXPOSURES, "Invalid site exposure")
            # One site may span services only by path prefix, so a request always has exactly one destination.
            require((name, path) not in claimed, "Two services claim the same site path")
            claimed.add((name, path))
            result.append({"service": service, "site": name, "port": port, "path": path, "exposure": exposure})
    return result


def validate_target(target, manifest=None):
    require(SLUG.fullmatch(target.get("product", "")), "Invalid target product")
    require(SLUG.fullmatch(target.get("environment", "")), "Invalid target environment")
    project = target["product"] + "-" + target["environment"]
    root = Path(target.get("root", ""))
    require(root.is_absolute() and str(root) not in ("/", "/tmp", "/srv"), "Use a dedicated absolute deployment root")
    require(not root.is_symlink(), "Deployment root cannot be a symlink")
    if manifest:
        require(target["product"] == manifest["product"], "Target product mismatch")
    target_ingress(target)
    return root, project


def target_ingress(target):
    """The code-owned ingress settings a target carries, or None for a target that publishes no sites."""
    ingress = target.get("ingress")
    if ingress is None:
        return None
    require(isinstance(ingress, dict) and ingress.get("scheme") in ("http", "https"), "Invalid ingress")
    port = ingress.get("port")
    require(port is None or (type(port) is int and 1 <= port <= 65535), "Invalid ingress port")
    for key in ("entryPoints", "privateEntryPoints"):
        points = ingress.get(key, [])
        require(isinstance(points, list) and all(isinstance(point, str) and ENTRY_POINT.fullmatch(point)
                                                  for point in points), "Invalid ingress entry point")
    resolver = ingress.get("certResolver")
    require(resolver is None or (isinstance(resolver, str) and ENTRY_POINT.fullmatch(resolver)),
            "Invalid certificate resolver")
    pattern = ingress.get("pattern")
    require(pattern is None or (isinstance(pattern, str) and "{site}" in pattern), "Invalid site host pattern")
    hosts = ingress.get("hosts", {})
    require(isinstance(hosts, dict) and all(isinstance(name, str) and SLUG.fullmatch(name) and isinstance(host, str)
                                            and HOST.fullmatch(host) for name, host in hosts.items()), "Invalid site host")
    # Where the runner reaches the ingress's public and private entry points from the target host.
    for key in ("probe", "privateProbe"):
        address = ingress.get(key)
        require(address is None or (isinstance(address, str) and PROBE_ADDRESS.fullmatch(address)),
                "Invalid ingress probe address")
    return ingress


def site_host(target, ingress, site):
    host = ingress.get("hosts", {}).get(site)
    if host is None and ingress.get("pattern"):
        host = (ingress["pattern"].replace("{site}", site).replace("{product}", target["product"])
                .replace("{environment}", target["environment"]))
        require(HOST.fullmatch(host), "Site host pattern produced an invalid host")
    return host


def ingress_routes(target, project, manifest):
    """Traefik dynamic configuration for a release's sites on this target, and the URL of each routed site."""
    ingress = target_ingress(target)
    if ingress is None:
        return None, []
    default_port = {"http": 80, "https": 443}[ingress["scheme"]]
    port = ingress.get("port")
    origin_port = ":" + str(port) if port and port != default_port else ""
    routers, upstreams, published = {}, {}, []
    for site in release_sites(manifest):
        host = site_host(target, ingress, site["site"])
        points = ingress.get("entryPoints" if site["exposure"] == "public" else "privateEntryPoints", [])
        if not host or not points:
            continue  # a site without a host or an entry point stays internal
        # Every service answers on the platform network under <product>-<environment>-<service>.
        upstream = project + "-" + site["service"] + "-" + str(site["port"])
        upstreams[upstream] = {"loadBalancer": {"servers": [
            {"url": "http://" + project + "-" + site["service"] + ":" + str(site["port"])}]}}
        rule = "Host(`" + host + "`)" + ("" if site["path"] == "/" else " && PathPrefix(`" + site["path"] + "`)")
        router = {"rule": rule, "service": upstream, "entryPoints": points}
        if ingress["scheme"] == "https" and ingress.get("certResolver"):
            router["tls"] = {"certResolver": ingress["certResolver"]}
        routers[project + "-" + site["site"] + "-" + site["service"]] = router
        published.append({"name": site["site"], "service": site["service"], "exposure": site["exposure"],
                          "url": ingress["scheme"] + "://" + host + origin_port + ("" if site["path"] == "/" else site["path"])})
    return {"http": {"routers": routers, "services": upstreams}}, published


def publish_routes(root, project, routes):
    """Replaces the project's route file in the file-provider ingress folder; an empty route set removes it."""
    config, published = routes
    if config is None:
        return []
    destination = root / "ingress" / (project + ".yml")
    if not published:
        with contextlib.suppress(FileNotFoundError):
            destination.unlink()
        return []
    # JSON is valid YAML. Staging outside the watched folder keeps the ingress from reading a partial file.
    staging = root / "ingress-staging" / (project + ".yml")
    write_json(staging, config)
    destination.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
    os.replace(staging, destination)
    return published


def ingress_request(address, host, path, timeout=5):
    """GETs `path` from the ingress at `address`, naming `host` in the Host header and TLS SNI."""
    origin = urllib.parse.urlsplit(address)
    port = origin.port or (443 if origin.scheme == "https" else 80)
    connection = http.client.HTTPConnection(origin.hostname, port, timeout=timeout)
    try:
        if origin.scheme == "https":
            # The certificate can still be pending right after a first deploy; this checks routing, not TLS.
            context = ssl.create_default_context()
            context.check_hostname = False
            context.verify_mode = ssl.CERT_NONE
            connection.sock = context.wrap_socket(socket.create_connection((origin.hostname, port), timeout=timeout),
                                                  server_hostname=host)
        connection.request("GET", path, headers={"Host": host, "User-Agent": "wheelhouse-probe"})
        response = connection.getresponse()
        return response.status, response.read(len(NO_ROUTE))
    finally:
        connection.close()


def classify_probe(request, address, host, path):
    try:
        status, body = request(address, host, path)
    except (OSError, ValueError, http.client.HTTPException) as error:
        return {"ok": False, "detail": "The ingress did not answer (" + type(error).__name__ + ")"}
    if status == 404 and body.startswith(NO_ROUTE):
        return {"ok": False, "status": status, "detail": "The ingress has no route for this host"}
    if status in (502, 503, 504):
        return {"ok": False, "status": status, "detail": "The ingress could not reach the service"}
    if status >= 500:
        return {"ok": False, "status": status, "detail": "The service answered " + str(status)}
    return {"ok": True, "status": status}


def probe_sites(target, sites, deadline_seconds=20, interval=1.0, request=ingress_request):
    """Requests every published site through the ingress by its host name, the way a visitor reaches it, until each
    answers or the deadline passes; attaches the outcome to the site. A site without a probe address stays unprobed."""
    ingress = target_ingress(target) or {}
    pending = {}
    for index, site in enumerate(sites):
        address = ingress.get("probe" if site.get("exposure") == "public" else "privateProbe")
        if address:
            pending[index] = address
    deadline = time.monotonic() + deadline_seconds
    results = {}
    while pending:
        for index, address in list(pending.items()):
            url = urllib.parse.urlsplit(sites[index]["url"])
            results[index] = classify_probe(request, address, url.hostname, url.path or "/")
            if results[index]["ok"]:
                del pending[index]
        # The ingress reloads a changed route file within seconds; a site that never answers is reported.
        if not pending or time.monotonic() >= deadline:
            break
        time.sleep(interval)
    for index, result in results.items():
        sites[index]["probe"] = result
    return [sites[index] for index in sorted(results)]


class Steps:
    """A rollout's ordered steps, saved into its job record as each starts and ends. Details are operator-safe."""

    def __init__(self, record, save):
        self.record, self.save = record, save
        record.setdefault("steps", [])

    def add(self, name, status, started, detail=None):
        step = {"name": name, "status": status, "startedAt": started, "completedAt": now()}
        if detail:
            step["detail"] = detail
        self.record["steps"].append(step)
        self.save()

    @contextlib.contextmanager
    def run(self, name):
        step = {"name": name, "status": "running", "startedAt": now()}
        self.record["steps"].append(step)
        self.save()
        try:
            yield step
        except BaseException as error:
            step.update(status="failed", completedAt=now())
            if reason(error):
                step["detail"] = reason(error)
            self.save()
            raise
        if step["status"] == "running":
            step["status"] = "succeeded"
        step["completedAt"] = now()
        self.save()


def plural(count, noun):
    return str(count) + " " + noun + ("" if count == 1 else "s")


def prune_images(base, record, docker, keep):
    """Removes the images only older releases on this target used, so candidate and release pulls do not fill the
    disk. Keeps every image of the newest `keep` releases and of the rollback target. Removal never forces: Docker
    refuses an image a container or another project still uses. A release whose images were removed is marked, so
    each one is handled once. Returns how many images went."""
    def copied_at(path):
        # The manifest is written once, when its rollout starts; the folder itself changes as markers are added.
        try:
            return (path / "release.json").stat().st_mtime
        except OSError:
            return 0.0

    releases = base / "releases"
    ordered = sorted((path for path in releases.iterdir() if path.is_dir() and not path.is_symlink()),
                     key=copied_at, reverse=True)
    kept_ids = {path.name for path in ordered[:keep]} | {record["id"], record.get("previous")}
    kept, older = set(), {}
    for path in ordered:
        try:
            images = set(read_json(path / "release.json").get("images", {}).values())
        except (OSError, ValueError):
            continue
        if path.name in kept_ids:
            kept.update(images)
        elif not (path / PRUNED).exists():
            older[path] = images
    removed = 0
    for path, images in older.items():
        for image in sorted(images - kept):
            try:
                docker.execute(["docker", "image", "rm", image], "docker image rm", timeout=60)
                removed += 1
            except CommandFailed:
                pass  # in use elsewhere or already gone
        with contextlib.suppress(OSError):
            (path / PRUNED).touch()
    return removed


def configuration_value(value, field):
    for key in field.split(":"):
        value = value.get(key) if isinstance(value, dict) else None
    return value


def base_environment(target):
    # Host files are the sole secret source. Never inherit ambient Compose or application overrides.
    environment = {key: value for key, value in os.environ.items()
                   if key in ("PATH", "HOME", "DOCKER_HOST", "DOCKER_CONFIG", "XDG_RUNTIME_DIR")}
    environment["COMPOSE_DISABLE_ENV_FILE"] = "1"
    environment["DEPLOY_ENVIRONMENT"] = target["environment"]
    for name, value in target.get("variables", {}).items():
        require(re.fullmatch(r"[A-Z][A-Z0-9_]*", name) and not name.startswith(("COMPOSE_", "DOCKER_")),
                "Invalid target variable")
        require(isinstance(value, str) and "\n" not in value, "Invalid target variable value")
        environment[name] = value
    return environment


def validate_settings(target, service, required):
    require(service in target.get("settings", {}), "Provide settings for every service")
    path = Path(target["settings"][service])
    require(path.is_absolute() and path.is_file() and not path.is_symlink(),
            "Settings must be an absolute regular file: " + service)
    require(path.stat().st_mode & 0o022 == 0 and
            (path.stat().st_mode & 0o077 == 0 or path.parent.stat().st_mode & 0o077 == 0),
            "Settings files must be private or inside a private directory: " + service)
    try:
        configuration = read_json(path)
    except ValueError:
        raise Rejected("Settings are not valid JSON: " + service) from None
    for key in required:
        require(configuration_value(configuration, key) not in (None, "", []), "Missing required setting: " + service + ":" + key)
    hosts = configuration.get("AllowedHosts") if isinstance(configuration, dict) else None
    if isinstance(hosts, str):
        # Health checks and smoke probes reach every service as http://localhost:<its port>.
        entries = {host.strip().lower() for host in hosts.split(";") if host.strip()}
        require(not entries or "*" in entries or "localhost" in entries,
                "AllowedHosts must include localhost for health probes: " + service)
    # ASP.NET binds only a JSON array here; a plain string silently trusts no proxy.
    proxies = configuration_value(configuration, PROXIES)
    require(proxies is None or isinstance(proxies, list), PROXIES + " must be a JSON array: " + service)
    return path


def environment_for(target, manifest):
    environment = base_environment(target)
    require(target.get("settings", {}).keys() == manifest["requiredConfiguration"].keys(),
            "Provide settings for every service that takes them")
    for service, required in manifest.get("requiredConfiguration", {}).items():
        environment[service.upper().replace("-", "_") + "_SETTINGS"] = str(validate_settings(target, service, required))
    return environment


@contextlib.contextmanager
def deployment_lock(root):
    root.mkdir(parents=True, exist_ok=True, mode=0o700)
    with (root / "lock").open("a") as stream:
        try:
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise Rejected("Another deployment owns this target")
        yield


class Docker:
    def __init__(self, target, environment):
        self.target = target
        self.environment = environment
        self.project = target["product"] + "-" + target["environment"]

    def execute(self, arguments, step, timeout=600):
        # Command output can contain runtime secrets. Persist only the step name and exit code.
        try:
            result = subprocess.run(arguments, env=self.environment, capture_output=True, text=True, timeout=timeout)
        except subprocess.TimeoutExpired:
            raise CommandFailed(step + " timed out after " + str(timeout) + "s") from None
        if result.returncode:
            raise CommandFailed(step + " failed (exit " + str(result.returncode) + "); inspect target containers privately")
        return result.stdout

    def output(self, arguments, step, timeout=60):
        """A command's interleaved stdout and stderr, for the operator who asked; the runner never stores it."""
        try:
            result = subprocess.run(arguments, env=self.environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                    text=True, errors="replace", timeout=timeout)
        except subprocess.TimeoutExpired:
            raise CommandFailed(step + " timed out after " + str(timeout) + "s") from None
        if result.returncode:
            raise CommandFailed(step + " failed (exit " + str(result.returncode) + ")")
        return result.stdout

    def preflight(self, manifest):
        architecture = self.execute(["docker", "info", "--format", "{{.OSType}}/{{.Architecture}}"], "docker info").strip()
        architecture = architecture.replace("x86_64", "amd64").replace("aarch64", "arm64")
        require(architecture == manifest["platform"], "Target architecture differs from release")
        self.execute(["docker", "compose", "version"], "docker compose version", timeout=30)

    def compose(self, bundle, *arguments):
        return self.execute(["docker", "compose", "--project-name", self.project,
                             "--env-file", "/dev/null", "-f", str(Path(bundle) / "compose.json"), *arguments],
                            "compose " + arguments[0])

    def unhealthy(self, bundle):
        # Diagnosis after a failed wait: service names and container states only, never logs.
        try:
            output = self.compose(bundle, "ps", "--all", "--format", "json").strip()
            rows = json.loads(output) if output.startswith("[") else [json.loads(line) for line in output.splitlines()]
        except (CommandFailed, ValueError):
            return []
        return sorted(row.get("Service", "unknown") + " (" + (row.get("Health") or row.get("State") or "unknown") + ")"
                      for row in rows if isinstance(row, dict) and row.get("Health") != "healthy")

    def pull(self, bundle):
        self.compose(bundle, "config", "--quiet")
        self.compose(bundle, "pull", "--policy", "always")

    def up(self, bundle):
        timeout = self.target.get("healthTimeoutSeconds", 120)
        require(type(timeout) is int and 10 <= timeout <= 300, "Invalid health timeout")
        self.compose(bundle, "up", "--detach", "--wait", "--wait-timeout", str(timeout),
                     "--remove-orphans", "--pull", "never")

    def verify(self, bundle, manifest):
        for service, image in manifest["images"].items():
            container = self.compose(bundle, "ps", "--all", "--quiet", service).strip()
            require(container and "\n" not in container, "Expected exactly one container per service")
            details = json.loads(self.execute(["docker", "inspect", container], "docker inspect"))[0]
            require(details["Config"]["Image"] == image, "Running image differs from release: " + service)
            health = details["State"].get("Health", {}).get("Status")
            require(health == "healthy", "Service failed readiness: " + service + " (" + str(health) + ")")
        # Target-owned smoke checks cannot be supplied by an uploaded release.
        for probe in self.target.get("smoke", []):
            require(probe.get("service") in manifest["images"], "Unknown smoke service")
            require(re.fullmatch(r"/[A-Za-z0-9/_?=&.%~-]*", probe.get("path", "")), "Invalid smoke path")
            expected = probe.get("status", 200)
            require(type(expected) is int and 200 <= expected <= 499, "Invalid smoke status")
            port = str(service_port(manifest, probe["service"]))
            status = self.compose(bundle, "exec", "-T", probe["service"], "curl", "--silent",
                                  "--output", "/dev/null", "--write-out", "%{http_code}",
                                  "--max-time", "10", "http://localhost:" + port + probe["path"])
            require(status.strip() == str(expected), "Smoke check failed: " + probe["service"] + " " + probe["path"]
                    + " returned " + status.strip()[:3] + ", expected " + str(expected))


def apply(bundle, target, actor, job_id=None, docker_factory=Docker, prober=probe_sites):
    started = now()
    manifest = validate_bundle(bundle)
    root, project = validate_target(target, manifest)
    root = root / project
    require(isinstance(actor, str) and 0 < len(actor) <= 160 and "\n" not in actor, "Invalid actor")
    job_id = job_id or str(uuid.uuid4())
    require(str(uuid.UUID(job_id)) == job_id, "Invalid deployment id")
    with deployment_lock(root):
        active_path = root / "active.json"
        active = read_json(active_path) if active_path.exists() else None
        require(not active or active["status"] in TERMINAL, "Interrupted deployment requires reconciliation")
        require(not active or not (active.get("mutationStarted") and active["status"] in ("failed", "rollback_failed")),
                "Failed mutation requires reconciliation")
        environment = environment_for(target, manifest)
        routes = ingress_routes(target, project, manifest)
        require(shutil.disk_usage(root).free >= target.get("minimumFreeBytes", 1024 ** 3), "Insufficient free disk")
        docker = docker_factory(target, environment)
        docker.preflight(manifest)
        destination = root / "releases" / job_id
        destination.mkdir(parents=True, mode=0o700)
        for filename in ("release.json", "compose.json"):
            shutil.copyfile(Path(bundle) / filename, destination / filename)
        # Revalidate the immutable snapshot, not only the source directory.
        manifest = validate_bundle(destination)
        previous_path = root / "current.json"
        previous = read_json(previous_path) if previous_path.exists() else None
        record = {"id": job_id, "project": project, "release": manifest["release"],
                  "sourceCommit": manifest["sourceCommit"], "kind": manifest.get("kind", "release"),
                  "actor": actor, "status": "running", "startedAt": now(),
                  "previous": previous.get("id") if previous else None}
        for key in ("branch", "versions"):
            if manifest.get(key):
                record[key] = manifest[key]
        def save():
            write_json(root / "jobs" / (job_id + ".json"), record)
            write_json(active_path, record)
        steps = Steps(record, save)
        steps.add("Check target", "succeeded", started, "Settings, disk, Docker and platform verified")
        mutation_started = False
        try:
            with steps.run("Pull images") as step:
                docker.pull(destination)
                step["detail"] = plural(len(manifest["images"]), "image")
            mutation_started = True
            record["mutationStarted"] = True
            save()
            with steps.run("Start containers"):
                docker.up(destination)
            with steps.run("Verify services") as step:
                docker.verify(destination, manifest)
                smoke = len(target.get("smoke", []))
                step["detail"] = plural(len(manifest["images"]), "service") + " healthy" + (
                    "; " + plural(smoke, "smoke check") + " passed" if smoke else "")
            with steps.run("Publish sites") as step:
                record["sites"] = publish_routes(root.parent, project, routes)
                step["detail"] = plural(len(record["sites"]), "site") if record["sites"] else "No sites to publish"
            if record["sites"]:
                # A site that does not answer is a warning: the containers are healthy, the path to them is not.
                with steps.run("Probe sites") as step:
                    try:
                        probed = prober(target, record["sites"])
                    except Exception:
                        probed = None
                    failed = [site for site in probed or () if not site["probe"]["ok"]]
                    if probed is None:
                        step.update(status="warning", detail="The site probe could not run")
                    elif not probed:
                        step.update(status="skipped", detail="No probe address for this ingress")
                    elif failed:
                        step.update(status="warning", detail=str(len(probed) - len(failed)) + " of "
                                    + plural(len(probed), "site") + " answered")
                        record["warnings"] = ["Site " + site["name"] + " did not answer through the ingress: "
                                              + site["probe"]["detail"] for site in failed]
                    else:
                        step["detail"] = plural(len(probed), "site") + " answered"
            record["status"] = "succeeded"
            write_json(previous_path, {key: record[key] for key in CURRENT if key in record})
            # Housekeeping after the release is recorded; it never changes the outcome.
            with contextlib.suppress(Exception), steps.run("Remove unused images") as step:
                removed = prune_images(root, record, docker, KEEP_RELEASES)
                step.update(status="succeeded" if removed else "skipped",
                            detail=plural(removed, "image") + " from older releases" if removed
                            else "No image only older releases used")
        except (Exception, KeyboardInterrupt) as error:
            record["status"] = "failed"
            # Only safe diagnostic categories; exception messages may include input/credential values.
            record["failure"] = type(error).__name__
            if reason(error):
                record["reason"] = reason(error)
            if mutation_started and isinstance(error, CommandFailed):
                with contextlib.suppress(Exception):
                    states = docker.unhealthy(destination)
                    if states:
                        record["reason"] += "; not healthy: " + ", ".join(states)
            if mutation_started and previous and manifest["rollbackCompatible"]:
                try:
                    with steps.run("Roll back to " + str(previous.get("release") or "the previous release")):
                        prior_bundle = root / "releases" / previous["id"]
                        prior_manifest = validate_bundle(prior_bundle)
                        prior_environment = environment_for(target, prior_manifest)
                        prior_docker = docker_factory(target, prior_environment)
                        prior_docker.pull(prior_bundle)
                        prior_docker.up(prior_bundle)
                        prior_docker.verify(prior_bundle, prior_manifest)
                    record["status"] = "rolled_back"
                except Exception:
                    record["status"] = "rollback_failed"
        record["completedAt"] = now()
        save()
        return record


def status(target, job_id):
    root, project = validate_target(target)
    require(str(uuid.UUID(job_id)) == job_id, "Invalid deployment id")
    return read_json(root / project / "jobs" / (job_id + ".json"))


def launch(bundle, target_path, actor):
    target = read_json(target_path)
    manifest = validate_bundle(bundle)
    root, project = validate_target(target, manifest)
    job_id = str(uuid.uuid4())
    jobs = root / project / "jobs"
    jobs.mkdir(parents=True, exist_ok=True, mode=0o700)
    write_json(jobs / (job_id + ".json"), {"id": job_id, "status": "queued", "actor": actor, "queuedAt": now()})
    # The target process owns the rollout across SSH/control-plane disconnects.
    subprocess.Popen([sys.executable, str(Path(__file__).resolve()), "apply", "--bundle", str(Path(bundle).resolve()),
                      "--target", str(Path(target_path).resolve()), "--actor", actor, "--job", job_id],
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     start_new_session=True, close_fds=True)
    return {"id": job_id, "status": "queued"}


def acknowledge(target, job_id):
    root, project = validate_target(target)
    with deployment_lock(root / project):
        active = read_json(root / project / "active.json")
        require(active["id"] == job_id and active["status"] in ("running", "failed", "rollback_failed"),
                "No matching interrupted deployment")
        require(active["status"] == "running" or active.get("mutationStarted"),
                "The deployment changed no containers; nothing to reconcile")
        # Acknowledgement does not assert that previous images still match the observed schema.
        current = root / project / "current.json"
        if current.exists():
            current.unlink()
        active["status"] = "interrupted"
        active["completedAt"] = now()
        write_json(root / project / "active.json", active)
        write_json(root / project / "jobs" / (job_id + ".json"), active)
        return active


def lock_held(base):
    # A shared probe fails only while a rollout holds the exclusive lock; the file is never created here.
    try:
        with (base / "lock").open("r") as stream:
            fcntl.flock(stream, fcntl.LOCK_SH | fcntl.LOCK_NB)
            fcntl.flock(stream, fcntl.LOCK_UN)
            return False
    except FileNotFoundError:
        return False
    except BlockingIOError:
        return True


def summary(record):
    fields = ("id", "release", "sourceCommit", "kind", "branch", "versions", "sites", "status", "actor", "startedAt",
              "completedAt", "failure", "reason", "mutationStarted", "previous", "steps", "warnings")
    return None if record is None else {key: record[key] for key in fields if key in record}


def state(target):
    root, project = validate_target(target)
    base = root / project
    current = read_json(base / "current.json") if (base / "current.json").is_file() else None
    active = read_json(base / "active.json") if (base / "active.json").is_file() else None
    condition = "ready"
    if active and active.get("status") == "running":
        # Only a status probe of a running record, so it can never refuse an otherwise valid rollout.
        condition = "running" if lock_held(base) else "needs_reconciliation"
    elif active and active.get("mutationStarted") and active.get("status") in ("failed", "rollback_failed"):
        condition = "needs_reconciliation"
    return {"project": project, "condition": condition, "current": summary(current), "active": summary(active)}


def empty_topology(availability, warning=None):
    return {"availability": availability, "release": None, "collectedAt": now(), "services": [],
            "networks": [], "volumes": [], "dependencies": [], "warnings": [warning] if warning else []}


def topology_port(value):
    """Allow only numeric port mappings; never return a host address or interpolated value."""
    if type(value) is int:
        value = str(value)
    if isinstance(value, str):
        parts = value.rsplit("/", 1)
        protocol = parts[1] if len(parts) == 2 else "tcp"
        mapping = parts[0].split(":")
        if len(mapping) > 3 and not parts[0].startswith("["):
            return None
        target = mapping[-1]
        published = mapping[-2] if len(mapping) > 1 else None
    elif isinstance(value, dict):
        target, published, protocol = value.get("target"), value.get("published"), value.get("protocol", "tcp")
    else:
        return None
    def port(part):
        if type(part) is int:
            part = str(part)
        if not isinstance(part, str) or not re.fullmatch(r"[0-9]{1,5}(?:-[0-9]{1,5})?", part):
            return None
        numbers = list(map(int, part.split("-")))
        return part if all(1 <= number <= 65535 for number in numbers) and numbers[0] <= numbers[-1] else None
    target, original_published = port(target), published
    published = port(published) if published is not None else None
    if not target or (original_published is not None and not published) or protocol not in ("tcp", "udp", "sctp"):
        return None
    return (published + ":" if published else "") + target + "/" + protocol


def compose_topology(compose, manifest):
    """Projects declared relationships only. This never invokes or interpolates Docker Compose."""
    warnings = set()
    identifier = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}")
    def resource_map(kind):
        raw = compose.get(kind)
        if raw is None:
            raw = {}
        require(isinstance(raw, dict), "Invalid topology resource declarations")
        result = {}
        for name, value in raw.items():
            if not isinstance(name, str) or not identifier.fullmatch(name):
                warnings.add("Unsupported resource identifiers were omitted.")
                continue
            if value is not None and not isinstance(value, dict):
                warnings.add("Unsupported resource declarations were omitted.")
                continue
            external = (value or {}).get("external", False)
            if type(external) is not bool and not isinstance(external, dict):
                warnings.add("Unsupported resource declarations were omitted.")
                continue
            # The legacy external.name form denotes an external resource; its name stays private.
            result[name] = {"name": name, "external": external is True or isinstance(external, dict)}
        return result
    networks, volumes = resource_map("networks"), resource_map("volumes")
    services, dependencies = [], []
    declared = compose["services"]
    for name, service in sorted(declared.items()):
        memberships = service.get("networks")
        if "network_mode" in service:
            memberships = []
            warnings.add("Services using network_mode have no projected network memberships.")
        elif memberships is None or memberships == [] or memberships == {}:
            memberships = ["default"]
        require(isinstance(memberships, (list, dict)), "Invalid topology network memberships")
        # Compose also creates default when services explicitly reference it without declaring it.
        if "default" in memberships and "default" not in (compose.get("networks") or {}):
            networks.setdefault("default", {"name": "default", "external": False})
        network_names = []
        for network in memberships:
            if isinstance(network, str) and network in networks:
                network_names.append(network)
            else:
                warnings.add("Undeclared or unsupported network memberships were omitted.")
        mounts = service.get("volumes")
        if mounts is None:
            mounts = []
        require(isinstance(mounts, list), "Invalid topology volume mounts")
        volume_names = []
        for mount in mounts:
            source = None
            if isinstance(mount, str) and ":" in mount:
                source = mount.split(":", 1)[0]
            elif isinstance(mount, dict) and mount.get("type") == "volume":
                source = mount.get("source")
            if isinstance(source, str) and source in volumes:
                volume_names.append(source)
            else:
                warnings.add("Bind mounts, anonymous volumes and unsupported mounts were omitted.")
        ports = service.get("ports")
        if ports is None:
            ports = []
        require(isinstance(ports, list), "Invalid topology port declarations")
        port_names = []
        for value in ports:
            normalized = topology_port(value)
            if normalized:
                port_names.append(normalized)
            else:
                warnings.add("Unsupported or interpolated port declarations were omitted.")
        depends = service.get("depends_on")
        if depends is None:
            depends = {}
        require(isinstance(depends, (list, dict)), "Invalid topology dependencies")
        seen_dependencies = set()
        for dependency in depends:
            if not isinstance(dependency, str) or dependency not in declared:
                warnings.add("Dependencies on undeclared services were omitted.")
                continue
            if dependency in seen_dependencies:
                continue
            seen_dependencies.add(dependency)
            details = depends[dependency] if isinstance(depends, dict) else {}
            if not isinstance(details, dict):
                warnings.add("Unsupported dependency declarations were omitted.")
                continue
            condition, required = details.get("condition", "service_started"), details.get("required", True)
            if condition not in ("service_started", "service_healthy", "service_completed_successfully") or type(required) is not bool:
                warnings.add("Unsupported dependency declarations were omitted.")
                continue
            dependencies.append({"from": name, "to": dependency, "condition": condition, "required": required})
        services.append({"name": name, "image": manifest["images"][name], "networks": sorted(set(network_names)),
                         "volumes": sorted(set(volume_names)), "ports": sorted(set(port_names))})
    return {"services": services, "networks": [networks[name] for name in sorted(networks)],
            "volumes": [volumes[name] for name in sorted(volumes)],
            "dependencies": sorted(dependencies, key=lambda item: (item["from"], item["to"])), "warnings": sorted(warnings)}


def release_facts(projected, manifest, current):
    """Adds what the release declares beside Compose: each service's version, the platform services it needs and
    the sites it serves, with the URL and probe outcome the verified rollout recorded."""
    versions, needs = manifest.get("versions", {}), manifest.get("needs", {})
    published = {(site.get("name"), site.get("service")): site for site in current.get("sites") or []
                 if isinstance(site, dict)}
    declared = release_sites(manifest)
    for service in projected["services"]:
        name = service["name"]
        entry = versions.get(name)
        service["version"] = {"version": entry["version"], "changedIn": entry["changedIn"]} if entry else None
        wanted = needs.get(name, []) if isinstance(needs, dict) else []
        service["needs"] = sorted({need for need in wanted if need in PLATFORM_SERVICES}) if isinstance(wanted, list) else []
        service["sites"] = []
        for site in declared:
            if site["service"] != name:
                continue
            record = published.get((site["site"], name), {})
            url = record.get("url")
            probe = record.get("probe") if isinstance(record.get("probe"), dict) else {}
            service["sites"].append({"name": site["site"], "path": site["path"], "port": site["port"],
                                     "exposure": site["exposure"],
                                     "url": url if isinstance(url, str) and url.startswith(("http://", "https://")) else None,
                                     "reachable": probe.get("ok") if type(probe.get("ok")) is bool else None})


def topology(target):
    """Reads the current successful release snapshot, never the incoming bundle or artifact catalog."""
    root, project = validate_target(target)
    base = root / project
    try:
        require(not base.is_symlink(), "Invalid topology state path")
        current_path = base / "current.json"
        require(not current_path.is_symlink(), "Invalid topology record path")
        if not current_path.exists():
            return empty_topology("not-deployed")
        current = read_json(current_path)
        identifier = current["id"]
        require(isinstance(identifier, str) and str(uuid.UUID(identifier)) == identifier, "Invalid topology release identity")
        releases = base / "releases"
        bundle = releases / identifier
        require(not releases.is_symlink() and not bundle.is_symlink(), "Invalid topology release path")
        for filename in ("release.json", "compose.json"):
            path = bundle / filename
            require(not path.is_symlink() and path.is_file() and path.stat().st_size <= 1024 * 1024,
                    "Invalid topology snapshot file")
        manifest = validate_bundle(bundle)
        validate_target(target, manifest)
        require(current["release"] == manifest["release"], "Topology release record mismatch")
        contents = (bundle / "compose.json").read_bytes()
        require(hashlib.sha256(contents).hexdigest() == manifest["composeSha256"], "Topology snapshot changed")
        projected = compose_topology(json.loads(contents), manifest)
        release_facts(projected, manifest, current)
        if state(target)["condition"] != "ready":
            projected["warnings"].append("The target is changing or needs reconciliation; this describes its last successful release.")
        require(read_json(current_path) == current, "Topology current release changed")
        result = empty_topology("available")
        result.update(projected, release=manifest["release"])
        return result
    except (OSError, ValueError, KeyError, TypeError, AttributeError):
        return empty_topology("unavailable", "The current release topology could not be verified.")


def check(target, manifest=None, docker_factory=None):
    """Reads the target without changing it; every detail is operator-safe text."""
    checks = []

    def probe(name, action):
        try:
            detail = action()
            checks.append({"name": name, "ok": True, "detail": detail or "ok"})
        except (Rejected, CommandFailed) as error:
            checks.append({"name": name, "ok": False, "detail": reason(error)})
        except Exception as error:
            checks.append({"name": name, "ok": False, "detail": type(error).__name__})

    root, project = validate_target(target, manifest)
    docker = (docker_factory or Docker)(target, base_environment(target))

    def writable_root():
        existing = next(path for path in (root, *root.parents) if path.exists())
        require(os.access(existing, os.W_OK | os.X_OK), "Deployment root is not writable: " + str(existing))
        return str(root) + (" exists" if root.exists() else " will be created")

    def architecture():
        value = docker.execute(["docker", "info", "--format", "{{.OSType}}/{{.Architecture}}"], "docker info").strip()
        value = value.replace("x86_64", "amd64").replace("aarch64", "arm64")
        if manifest:
            require(value == manifest["platform"], "Target is " + value + "; release needs " + manifest["platform"])
        return value

    def compose():
        return docker.execute(["docker", "compose", "version", "--short"], "docker compose version", timeout=30).strip()

    def disk():
        existing = next(path for path in (root, *root.parents) if path.exists())
        free = shutil.disk_usage(existing).free
        require(free >= target.get("minimumFreeBytes", 1024 ** 3), "Low disk: " + str(free // 1024 ** 2) + " MiB free")
        return str(free // 1024 ** 3) + " GiB free"

    def network():
        name = target.get("variables", {}).get("PLATFORM_NETWORK")
        if not name:
            return "no shared network declared"
        docker.execute(["docker", "network", "inspect", "--format", "{{.Name}}", name], "docker network inspect")
        return name

    def ingress():
        address = (target_ingress(target) or {}).get("probe")
        if not address:
            return "no probe address; sites are not requested after a deploy"
        # Any HTTP answer, Traefik's 404 for an unknown host included, shows the entry point is up.
        try:
            ingress_request(address, "wheelhouse-check.invalid", "/")
        except (OSError, ValueError, http.client.HTTPException) as error:
            raise CommandFailed("The ingress did not answer at " + address + " (" + type(error).__name__ + ")") from None
        return "answers at " + address

    probe("Deployment root", writable_root)
    probe("Docker", architecture)
    probe("Compose", compose)
    probe("Disk", disk)
    probe("Network", network)
    probe("Ingress", ingress)
    services = manifest["requiredConfiguration"] if manifest else {name: [] for name in target.get("settings", {})}
    for service, required in services.items():
        probe("Settings: " + service, lambda service=service, required=required:
              str(validate_settings(target, service, required).name) + " valid")
    current = state(target)
    checks.append({"name": "State", "ok": current["condition"] != "needs_reconciliation",
                   "detail": current["condition"].replace("_", " ")})
    return {"project": project, "ok": all(item["ok"] for item in checks), "checks": checks, "state": current}


SIZE_UNITS = {"B": 1, "kB": 1000, "KB": 1000, "MB": 1000 ** 2, "GB": 1000 ** 3, "TB": 1000 ** 4,
              "KiB": 1024, "MiB": 1024 ** 2, "GiB": 1024 ** 3, "TiB": 1024 ** 4}


def size_bytes(text):
    match = re.fullmatch(r"\s*([0-9.]+)\s*([A-Za-z]+)\s*", text or "")
    return int(float(match.group(1)) * SIZE_UNITS[match.group(2)]) if match and match.group(2) in SIZE_UNITS else None


def percent(text):
    match = re.fullmatch(r"\s*([0-9.]+)%\s*", text or "")
    return float(match.group(1)) if match else None


def host_vitals(root):
    """Load, memory, disks and uptime from /proc and statvfs; a field stays None where the host hides it."""
    def read(path):
        try:
            return Path(path).read_text()
        except OSError:
            return ""

    memory = {}
    for line in read("/proc/meminfo").splitlines():
        name, _, value = line.partition(":")
        if name in ("MemTotal", "MemAvailable") and value.split():
            memory[name] = int(value.split()[0]) * 1024
    load = read("/proc/loadavg").split()[:3]
    uptime = read("/proc/uptime").split()[:1]
    disks, seen = [], set()
    # The deployment root and Docker's data root, once per filesystem. Container mounts give one disk several
    # device ids, so identical capacity counts as the same disk too.
    for path in (root, Path("/var/lib/docker")):
        existing = next((item for item in (path, *path.parents) if item.exists()), None)
        try:
            device, usage = existing.stat().st_dev, shutil.disk_usage(existing)
        except (AttributeError, OSError):
            continue
        if device not in seen and (usage.total, usage.free) not in seen:
            seen.update((device, (usage.total, usage.free)))
            disks.append({"path": str(existing), "totalBytes": usage.total, "freeBytes": usage.free})
    return {"cpus": os.cpu_count(), "load": [float(value) for value in load] if len(load) == 3 else None,
            "memoryTotalBytes": memory.get("MemTotal"), "memoryAvailableBytes": memory.get("MemAvailable"),
            "uptimeSeconds": int(float(uptime[0])) if uptime else None, "disks": disks}


def container_vitals(docker, project):
    """State, health, restarts and resource use per container; never labels, environment or logs."""
    ids = docker.execute(["docker", "ps", "--all", "--quiet", "--no-trunc", "--filter",
                          "label=com.docker.compose.project=" + project], "docker ps", timeout=30).split()
    if not ids:
        return []
    details = json.loads(docker.execute(["docker", "inspect", *ids], "docker inspect", timeout=30))
    running = [item["Id"] for item in details if item.get("State", {}).get("Running")]
    usage = []
    if running:
        output = docker.execute(["docker", "stats", "--no-stream", "--no-trunc", "--format", "{{json .}}", *running],
                                "docker stats", timeout=30)
        for line in output.splitlines():
            try:
                row = json.loads(line)
            except ValueError:
                continue
            if isinstance(row, dict) and row.get("ID"):
                usage.append(row)
    result = []
    for item in details:
        state = item.get("State") or {}
        labels = (item.get("Config") or {}).get("Labels") or {}
        row = next((row for row in usage if item["Id"].startswith(row["ID"]) or row["ID"].startswith(item["Id"])), {})
        used, _, limit = (row.get("MemUsage") or "").partition("/")
        result.append({"service": labels.get("com.docker.compose.service") or item.get("Name", "").lstrip("/"),
                       "state": state.get("Status"), "health": (state.get("Health") or {}).get("Status"),
                       "restarts": item.get("RestartCount", 0),
                       # Docker reports a never-started container as year 1.
                       "startedAt": None if str(state.get("StartedAt")).startswith("0001-") else state.get("StartedAt"),
                       "exitCode": None if state.get("Running") else state.get("ExitCode"),
                       "cpuPercent": percent(row.get("CPUPerc")), "memoryBytes": size_bytes(used),
                       "memoryLimitBytes": size_bytes(limit)})
    return sorted(result, key=lambda container: container["service"])


def vitals(target, docker_factory=None):
    """Reads host and container vitals without changing the target; every field is operator-safe."""
    root, project = validate_target(target)
    docker = (docker_factory or Docker)(target, base_environment(target))
    before = state(target)
    result = {"project": project, "host": None, "containers": None, "problems": []}
    try:
        result["host"] = host_vitals(root)
    except (OSError, ValueError):
        result["problems"].append("Host vitals were unreadable")
    try:
        result["containers"] = container_vitals(docker, project)
    except (Rejected, CommandFailed) as error:
        result["problems"].append(reason(error))
    except (ValueError, KeyError, TypeError):
        result["problems"].append("Container details were unreadable")
    current = state(target)
    # Container collection spans several Docker calls. A completed rollout can otherwise attach
    # the new release to observations collected from its predecessor, including same-version redeploys.
    stable = before == current and current["condition"] == "ready"
    result.update(condition=current["condition"], release=(current["current"] or {}).get("release") if stable else None)
    if before != current:
        result["problems"].append("Deployment changed during observation; release attribution was withheld.")
    elif not stable:
        result["problems"].append("Target is not ready; release attribution was withheld.")
    return result


def logs(target, service, tail=200, docker_factory=None):
    """The last lines one service's container wrote, read on request. The runner never stores container output."""
    root, project = validate_target(target)
    require(isinstance(service, str) and SLUG.fullmatch(service), "Invalid service")
    require(type(tail) is int and 1 <= tail <= 1000, "Tail must be 1-1000 lines")
    docker = (docker_factory or Docker)(target, base_environment(target))
    containers = docker.execute(["docker", "ps", "--all", "--quiet", "--no-trunc",
                                 "--filter", "label=com.docker.compose.project=" + project,
                                 "--filter", "label=com.docker.compose.service=" + service],
                                "docker ps", timeout=30).split()
    require(containers, "No container runs this service")
    output = docker.output(["docker", "logs", "--tail", str(tail), "--timestamps", containers[0]], "docker logs")
    lines = output.splitlines()[-tail:]
    return {"project": project, "service": service, "tail": tail, "collectedAt": now(),
            "lines": [line[:LOG_LINE_LIMIT] for line in lines],
            "truncated": any(len(line) > LOG_LINE_LIMIT for line in lines)}


def decode(value):
    # Transport passes documents as base64 arguments so read-only calls leave no files on the target.
    return json.loads(base64.b64decode(value, validate=True))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["validate", "apply", "launch", "status", "acknowledge", "state", "check",
                                           "vitals", "topology", "logs"])
    parser.add_argument("--bundle")
    parser.add_argument("--target")
    parser.add_argument("--target-json")
    parser.add_argument("--release-json")
    parser.add_argument("--actor", default="operator")
    parser.add_argument("--job")
    parser.add_argument("--service")
    parser.add_argument("--tail", type=int, default=200)
    args = parser.parse_args()
    try:
        target = decode(args.target_json) if args.target_json else None
        if args.action == "validate":
            result = validate_bundle(args.bundle)
        elif args.action == "launch":
            result = launch(args.bundle, args.target, args.actor)
        elif args.action == "status":
            result = status(target or read_json(args.target), args.job)
        elif args.action == "acknowledge":
            result = acknowledge(target or read_json(args.target), args.job)
        elif args.action == "state":
            result = state(target or read_json(args.target))
        elif args.action == "topology":
            result = topology(target or read_json(args.target))
        elif args.action == "check":
            result = check(target or read_json(args.target), decode(args.release_json) if args.release_json else None)
        elif args.action == "vitals":
            result = vitals(target or read_json(args.target))
        elif args.action == "logs":
            result = logs(target or read_json(args.target), args.service, args.tail)
        else:
            result = apply(args.bundle, read_json(args.target), args.actor, args.job)
        print(json.dumps(result))
        return 1 if args.action == "apply" and result.get("status") != "succeeded" else 0
    except Exception as error:
        # A launched worker must not leave its own job queued if preflight rejected it.
        if args.action == "apply" and args.job:
            try:
                root, project = validate_target(read_json(args.target))
                refused = rejection(error)
                failed_step = {"name": "Check target", "status": "failed", "completedAt": now()}
                if refused.get("reason"):
                    failed_step["detail"] = refused["reason"]
                write_json(root / project / "jobs" / (args.job + ".json"),
                           {"id": args.job, **refused, "completedAt": now(), "steps": [failed_step]})
            except Exception:
                pass
        print(json.dumps(rejection(error)), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
