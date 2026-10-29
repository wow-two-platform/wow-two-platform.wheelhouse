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
import shlex
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
# A schema 2 site path: `/`, or segments without a trailing slash; `.` and `..` segments are refused apart.
ROUTE_PATH = re.compile(r"/|(?:/[A-Za-z0-9._~-]+)+")
ENTRY_POINT = re.compile(r"[a-z][a-z0-9-]{0,31}")
PROBE_ADDRESS = re.compile(r"https?://[A-Za-z0-9.-]+(?::[0-9]{1,5})?")
IPV4 = re.compile(r"(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}")
# A public site with no host gets one on its server's public address through sslip.io (decision 68).
BASELINE_DOMAIN = "sslip.io"
# Traefik's own answer for a host it has no router for; an application's 404 carries its own body.
NO_ROUTE = b"404 page not found"
LOG_LINE_LIMIT = 2000
# Releases whose images a target keeps for redeploys; older images are removed once no container uses them.
KEEP_RELEASES = 3
PRUNED = "images-removed"
# Release folders a target keeps beside the running and rollback releases, once their images are gone; and how many of
# a project's uploaded submissions (incoming/<request>/) it keeps, each at least a day.
KEEP_RELEASE_FOLDERS = 10
KEEP_INCOMING = 10
INCOMING_GRACE_SECONDS = 24 * 60 * 60
KINDS = ("release", "candidate")
# Release schemas: 1 from descriptor 1 (one path per service site), 2 from descriptor 2 (product sites + routes).
SCHEMAS = (1, 2)
# Host platform services a release may declare it needs; see the deploy descriptor convention.
PLATFORM_SERVICES = ("postgres", "valkey", "broker")
EXPOSURES = ("public", "private")
TERMINAL = {"succeeded", "failed", "rolled_back", "rollback_failed", "interrupted", "rejected"}
PROXIES = "Deployment:TrustedProxies"
HOSTS = "AllowedHosts"
# A rendering target's settings: <root>/config/<product>-<environment>/<service>.target.json, beside the host's own
# files (database.json, any hand-placed <service>.json), in the mode-0700 folder the host kit creates.
SETTINGS_FOLDER = "config"
RENDERED = ".target.json"
DATABASE_FILE = "database.json"
# A configuration path: sections and a key, as .NET binds them (`Section:Key`, `List:0`).
SETTING_KEY = re.compile(r"[A-Za-z0-9_.-]{1,100}(?::[A-Za-z0-9_.-]{1,100}){0,9}")
# What a typed key's value must look like (a bundle's `settings.<service>.types`); Wheelhouse checks saved values.
SETTING_TYPES = ("text", "number", "boolean", "choice", "url", "cron", "date", "email")
DATE_FORMAT = re.compile(r"(yyyy|MM|dd|HH|mm|ss|[-./: T])+")
# A key's last part naming a secret: its values belong in the vault, never in the settings store.
SECRET_KEY = re.compile(r".*(password|passphrase|secret|token|apikey|key|pwd|credentials?|connectionstring|salt)",
                        re.IGNORECASE)
# The platform's ingress, as the host kits name it: the Compose service `ingress` of a `wheelhouse-*` project.
INGRESS_SERVICE = "ingress"
REGISTRY_HOST = re.compile(r"[a-z0-9][a-z0-9.-]{0,252}(?::[0-9]{1,5})?")
REGISTRY_USER = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,99}")
# How a pull signed in, as a job record names it: the GitHub App's installation token, or the control host's token file.
CREDENTIALS = {"installation": "the GitHub App's token", "file": "the control host's read-only token"}
# An image repository the control host found nothing could read (`<registry>/<owner>/<path>`, lowercase).
IMAGE_REPOSITORY = re.compile(r"[a-z0-9][a-z0-9.-]{0,252}(?::[0-9]{1,5})?(?:/[a-z0-9][a-z0-9._-]{0,127}){1,8}")
# What the owner places when the control host has no credential that reads a private repository's package.
UNREADABLE_HINT = ("an App token cannot read a private repository's packages; place a classic read:packages token "
                   "in <runner root>/github-token/token (or Deployment:GitHubTokenFile)")
# The first bytes of every SQLite database file, and the names other embedded file databases give their files (LiteDB,
# LMDB, DuckDB, Realm, WiredTiger, Berkeley DB): a volume holding one is archived with its writers paused.
SQLITE_HEADER = "SQLite format 3"
FILE_DATABASES = ("*.db", "*.db3", "*.sqlite", "*.sqlite3", "*.litedb", "*.mdb", "*.duckdb", "*.realm", "*.wt", "*.bdb")
# Pre-deploy snapshots on prod: the image that runs pg_dump and tar (the platform's own PostgreSQL image, so nothing new
# is pulled), and how many a target keeps. A target's `snapshot` entry may name others.
SNAPSHOT_IMAGE = "postgres:16-alpine"
SNAPSHOT_KEEP = 5
SNAPSHOT_IMAGE_PATTERN = re.compile(r"[a-z0-9][a-z0-9./_-]*(:[A-Za-z0-9._-]+)?(@sha256:[a-f0-9]{64})?")
# A connection value pg_dump receives as one argument: never an option, never a space.
CONNECTION_VALUE = re.compile(r"[A-Za-z0-9_.$][A-Za-z0-9_.$-]{0,62}")
# What a target remembers about its verified release: identity, service versions and published sites.
CURRENT = ("id", "release", "kind", "branch", "sourceCommit", "versions", "sites")
# The Compose override the runner writes beside a release's compose.json: every service receives the environment it
# runs in as DEPLOY_ENVIRONMENT. Never part of a bundle or its hash.
ENVIRONMENT_OVERRIDE = "environment.json"
# Each service's settings fingerprint the runner keeps beside a release it applied (`settings_fingerprints`); it never
# leaves the target.
FINGERPRINTS = "fingerprints.json"
# A service's health at the end of a rollout, as its job record reports it.
HEALTH = ("healthy", "unhealthy", "unknown")
# How a release that opts in (`dotnetEnvironment`) runs under .NET: dev as Development, prod as Production. Another
# environment gets no mapping.
DOTNET_ENVIRONMENTS = {"dev": "Development", "prod": "Production"}
# A release's end-to-end check on dev: the longest it may run, its container's memory, and how much of its output's
# last line the job keeps.
VERIFY_TIMEOUT = 300
VERIFY_TIMEOUT_LIMIT = 1800
VERIFY_MEMORY = "512m"
VERIFY_SUMMARY_LIMIT = 300
# Service controls (decision 45): what the owner may do to the release a target runs. A stop keeps containers, volumes
# and routes and leaves `stopped.json`, which every deploy refuses until a start removes it; a start brings back the
# same release, never pulling or recreating; a restart is one service's stop and start.
OPERATIONS = ("stop", "start", "restart")
STOPPED = "stopped.json"
# How long a stopping container gets to exit before Docker kills it.
STOP_GRACE_SECONDS = 30
# The settings file each service of a release mounted, as its rollout's Compose interpolated it; kept beside the
# release (never part of its bundle or hash) so a control starts the containers on the very same files.
SETTINGS_PATHS = "settings-paths.json"
# The migrate step (migrations spec M8-M17, M37): each item a release declares runs its command in a one-off container
# from its service's release image after the pull and before the first wave; the default and allowed timeouts.
MIGRATE_TIMEOUT = 600
MIGRATE_TIMEOUT_LIMITS = (10, 3600)
# What a migrate run prints last on stdout: `migrate-result {json}`; the runner reads at most this much of its stdout.
MIGRATE_RESULT = "migrate-result "
MIGRATE_OUTPUT_LIMIT = 256 * 1024
# An exit 3 (busy: the set's lock stayed held) retries once after this pause, then fails the step.
MIGRATE_BUSY_PAUSE = 5
# "Check now" (M36): the check from the image the target runs, bounded to fit the console's request.
MIGRATE_CHECK_TIMEOUT = 60
# The migrate key (M8): the variable that fills `Migrations:ConnectionString` in the migrate container only.
MIGRATE_KEY = "Migrations__ConnectionString"
# What the target last learned about each migration set, from rollouts, rollback gates and checks; beside current.json.
MIGRATIONS_FILE = "migrations.json"
# A migration set's name as `migrate sets --json` gives it, and an untrusted PostgreSQL extension a product declares.
SET_NAME = re.compile(r"[a-z][a-z0-9_-]{0,63}")
EXTENSION = re.compile(r"[a-z][a-z0-9_-]{0,62}")
MIGRATION_KINDS = ("expand", "contract")


class Rejected(ValueError):
    """A refused precondition. Messages hold only static text and contract key names."""


class RenderRefused(Rejected):
    """A settings render that cannot complete, such as a required key no layer sets; its job names the render step."""
    step = "Render settings"


class CommandFailed(RuntimeError):
    """A failed external step. Messages hold only the step name and exit status."""


class Unreachable(CommandFailed):
    """Healthy containers that the ingress does not reach, on a target that gates on its site probe."""


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
    require(manifest.get("schemaVersion") in SCHEMAS, "Unsupported release schema")
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
    release_settings(manifest)
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
    dotnet = manifest.get("dotnetEnvironment", False)
    # A release set (decision 70) names the services whose repository opted in when not all of them did.
    require(type(dotnet) is bool or (isinstance(dotnet, list) and dotnet and set(dotnet) <= set(services)
                                     and all(isinstance(name, str) for name in dotnet)),
            "dotnetEnvironment must be true, false or a list of the release's services")
    release_components(manifest)
    release_verification(manifest)
    release_migrate(manifest)
    release_migration_sets(manifest)
    release_extensions(manifest)
    return manifest


def release_components(manifest):
    """A release set's components (decision 70): each repository's release, kind, source commit and services, which
    together are every service of the release. None for a release of one repository."""
    components = manifest.get("components")
    if components is None:
        return None
    require(isinstance(components, list) and components, "Invalid release components")
    services = []
    for component in components:
        require(isinstance(component, dict) and isinstance(component.get("repository"), str)
                and SLUG.fullmatch(str(component.get("name", ""))) and RELEASE.fullmatch(str(component.get("release", "")))
                and component.get("kind", "release") in KINDS and SHA.fullmatch(str(component.get("sourceCommit", "")))
                and isinstance(component.get("services"), list), "Invalid release components")
        services += component["services"]
    require(sorted(services) == sorted(manifest.get("images", {})), "Release components name other services")
    return components


def dotnet_services(manifest, services):
    """The services of a release that run under .NET's own environment names: all of them for `dotnetEnvironment:
    true`, the listed ones for a release set whose repositories differ, none otherwise."""
    declared = manifest.get("dotnetEnvironment") if isinstance(manifest, dict) else None
    if declared is True:
        return set(services)
    return set(declared) & set(services) if isinstance(declared, list) else set()


def release_settings(manifest):
    """What a release declares about its services' settings beyond the required keys, by service: `optional` keys with
    their defaults, the keys that are `secrets`, the `allowed` values of a key, and each typed key's `types` entry (its
    type, and a date's format). Optional in a bundle; only services that take a settings file may declare them."""
    declared = manifest.get("settings", {})
    require(isinstance(declared, dict) and set(declared) <= set(manifest.get("requiredConfiguration") or {}),
            "Settings declarations name a service without a settings file")
    result = {}
    for service, entry in declared.items():
        require(isinstance(entry, dict) and set(entry) <= {"optional", "secrets", "allowed", "types"},
                "Invalid settings declaration")
        types = entry.get("types", {})
        require(isinstance(types, dict) and all(
            SETTING_KEY.fullmatch(key) and isinstance(value, dict) and set(value) <= {"type", "format"}
            and value.get("type") in SETTING_TYPES
            and (value.get("format") is None or (value["type"] == "date" and isinstance(value["format"], str)
                                                 and DATE_FORMAT.fullmatch(value["format"])))
            for key, value in types.items()), "Invalid setting types")
        optional, secrets, allowed = entry.get("optional", {}), entry.get("secrets", []), entry.get("allowed", {})
        require(isinstance(optional, dict) and all(SETTING_KEY.fullmatch(key) and isinstance(value, str)
                                                   and len(value) <= 2000 for key, value in optional.items()),
                "Invalid optional settings")
        require(isinstance(secrets, list) and all(isinstance(key, str) and SETTING_KEY.fullmatch(key) for key in secrets),
                "Invalid secret settings")
        require(isinstance(allowed, dict) and all(
            SETTING_KEY.fullmatch(key) and isinstance(values, list) and 0 < len(values) <= 50
            and all(isinstance(value, str) and len(value) <= 200 for value in values) for key, values in allowed.items()),
            "Invalid allowed setting values")
        result[service] = {"optional": dict(optional), "secrets": list(secrets), "allowed": dict(allowed),
                           "types": {key: dict(value) for key, value in types.items()}}
    return result


def release_verification(manifest):
    """The end-to-end check a release declares for dev — the service whose image runs it, the command and its timeout
    in seconds — or None when it declares none."""
    verify = manifest.get("verify")
    if verify is None:
        return None
    require(isinstance(verify, dict) and isinstance(verify.get("service"), str)
            and verify["service"] in manifest.get("images", {}), "The verify check names an unknown service")
    command = verify.get("command")
    require(isinstance(command, list) and 0 < len(command) <= 32
            and all(isinstance(part, str) and part and len(part) <= 1000 and "\0" not in part for part in command),
            "Invalid verify command")
    timeout = verify.get("timeoutSeconds", VERIFY_TIMEOUT)
    require(type(timeout) is int and 10 <= timeout <= VERIFY_TIMEOUT_LIMIT, "The verify timeout must be 10-1800 seconds")
    return {"service": verify["service"], "command": command, "timeoutSeconds": timeout}


def release_migrate(manifest):
    """The migrate items a release declares (M9), in the order they run: each names the service whose release image
    hosts migration sets, the `command` that runs as that container's entrypoint (the program and its arguments, ending
    in the bare `migrate` verb) and its `timeoutSeconds`. Empty for a release that declares none."""
    items = manifest.get("migrate")
    if items is None:
        return []
    require(isinstance(items, list) and 0 < len(items) <= 20, "Invalid migrate list")
    result, seen = [], set()
    for item in items:
        require(isinstance(item, dict) and isinstance(item.get("service"), str)
                and item["service"] in manifest.get("images", {}), "A migrate item names an unknown service")
        require(item["service"] not in seen, "Two migrate items name " + item["service"])
        seen.add(item["service"])
        command = item.get("command")
        require(isinstance(command, list) and 0 < len(command) <= 32
                and all(isinstance(part, str) and part and len(part) <= 1000 and "\0" not in part for part in command),
                "Invalid migrate command")
        timeout = item.get("timeoutSeconds", MIGRATE_TIMEOUT)
        require(type(timeout) is int and MIGRATE_TIMEOUT_LIMITS[0] <= timeout <= MIGRATE_TIMEOUT_LIMITS[1],
                "The migrate timeout must be 10-3600 seconds")
        result.append({"service": item["service"], "command": list(command), "timeoutSeconds": timeout})
    return result


def release_migration_sets(manifest):
    """Each migrate item's sets as its image's `migrate sets --json` described them when the release was built (M78), by
    service: each set's name, schema, level (its highest embedded ordinal) and the rest the image said. Empty when the
    release does not say."""
    declared = manifest.get("migrationSets")
    if declared is None:
        return {}
    require(isinstance(declared, dict) and set(declared) <= set(manifest.get("images", {})),
            "Migration sets name an unknown service")
    for sets in declared.values():
        require(isinstance(sets, list) and len(sets) <= 50 and all(
            isinstance(entry, dict) and isinstance(entry.get("name"), str) and SET_NAME.fullmatch(entry["name"])
            and (entry.get("level") is None or type(entry["level"]) is int) for entry in sets), "Invalid migration sets")
    return declared


def release_levels(manifest):
    """The level each migration set has in a release's images, by set name; a set the release does not describe is
    left out."""
    levels = {}
    for sets in release_migration_sets(manifest).values():
        for entry in sets:
            if type(entry.get("level")) is int:
                levels[entry["name"]] = max(entry["level"], levels.get(entry["name"], entry["level"]))
    return levels


def release_extensions(manifest):
    """The untrusted PostgreSQL extensions a release declares (M41); placement creates them, the runner only checks
    their names."""
    declared = manifest.get("extensions")
    if declared is None:
        return []
    require(isinstance(declared, list) and len(declared) <= 50
            and all(isinstance(name, str) and EXTENSION.fullmatch(name) for name in declared), "Invalid extensions")
    return list(declared)


def service_dependencies(compose):
    """Each service's declared dependencies (Compose `depends_on`, as a list or a map), by service."""
    services = compose.get("services", {})
    result = {}
    for name, service in services.items():
        declared = service.get("depends_on") or {}
        require(isinstance(declared, (list, dict)), "Invalid service dependencies")
        names = set(declared)
        require(all(isinstance(other, str) and other in services and other != name for other in names),
                "Dependencies name an unknown service")
        result[name] = names
    return result


def dependency_waves(services, dependencies):
    """The services in start order, in waves: a wave starts only after every service it depends on. Refuses a cycle."""
    remaining, placed, waves = set(services), set(), []
    while remaining:
        wave = sorted(name for name in remaining if dependencies.get(name, set()) <= placed)
        require(wave, "Service dependencies form a cycle")
        waves.append(wave)
        placed.update(wave)
        remaining.difference_update(wave)
    return waves


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"))


def definition_of(service):
    """What decides whether a service's container must change: its Compose definition without `depends_on`, which only
    orders starts, so declaring or dropping a dependency never restarts the service."""
    return {key: value for key, value in service.items() if key != "depends_on"} if isinstance(service, dict) else service


def image_identity(image, version):
    """A service's release as a job record shows it: its version and its image digest's first 12 hex digits."""
    digest = image.split("@sha256:", 1)[1][:12] if isinstance(image, str) and "@sha256:" in image else None
    return {"version": version if isinstance(version, str) else None, "digest": digest}


def version_of(versions, name):
    entry = versions.get(name) if isinstance(versions, dict) else None
    return entry.get("version") if isinstance(entry, dict) else None


def rollout_plan(compose, environment, prior=None, prior_environment=None, same_inputs=True, dotnet=False,
                 settings=None, prior_settings=None, versions=None, prior_versions=None):
    """What a release changes on a target, service by service, against the release it runs (`prior`, its compose.json,
    and `prior_environment`, the runner's override it ran with): `added`, `updated` or `unchanged`, and `removed` for
    what it drops. A service's `reason` says why it changes: `added`; its `image`; the rest of its Compose `definition`
    (a `depends_on` change alone is none); or its `settings` — its settings file's contents (`settings` against
    `prior_settings`, each service's fingerprint), the environment the runner gives it (`dotnet`, the services the
    release runs under .NET's environment names, or true for all), or a changed interpolation input (`same_inputs`
    false), which changes every service.
    A dependency's change never changes a dependent. `from` and `to` name the release each side runs (`versions`, the
    release's map, and `prior_versions`, the running one's). A changed service starts in its dependency wave; `staged`
    holds when starting the changed services wave by wave differs from one Compose `up`: some service keeps running
    untouched, or the changed ones span more than one wave."""
    services = compose["services"]
    dependencies = service_dependencies(compose)
    before = (prior or {}).get("services") or {}
    overrides = ((prior_environment or {}).get("services") or {}) if isinstance(prior_environment, dict) else {}
    dotnet = set(services) if dotnet is True else set(dotnet or ())
    settings = settings if isinstance(settings, dict) else {}
    # A release copied by an older runner kept no fingerprints: its settings count as unchanged, not as a restart.
    prior_settings = prior_settings if isinstance(prior_settings, dict) else {}
    reasons = {}
    for name in sorted(services):
        old = before.get(name) if prior is not None else None
        if not isinstance(old, dict):
            reasons[name] = "added"
        elif canonical(services[name].get("image")) != canonical(old.get("image")):
            reasons[name] = "image"
        elif canonical(definition_of(services[name])) != canonical(definition_of(old)):
            reasons[name] = "definition"
        elif (not same_inputs or canonical(overrides.get(name))
              != canonical({"environment": service_environment(environment, name in dotnet)})
              or (name in prior_settings and prior_settings[name] != settings.get(name))):
            reasons[name] = "settings"
        else:
            reasons[name] = None
    changes = {name: "added" if reason == "added" else "updated" if reason else "unchanged"
               for name, reason in reasons.items()}
    waves = [wave for wave in ([name for name in level if changes[name] != "unchanged"]
                               for level in dependency_waves(services, dependencies)) if wave]
    unchanged = any(change == "unchanged" for change in changes.values())
    return {"services": [{"name": name, "change": changes[name], "reason": reasons[name],
                          "dependsOn": sorted(dependencies[name]),
                          "from": None if changes[name] == "added" else
                          image_identity(before[name].get("image"), version_of(prior_versions, name)),
                          "to": image_identity(services[name].get("image"), version_of(versions, name))}
                         for name in sorted(services)],
            "removed": sorted(set(before) - set(services)), "waves": waves,
            "staged": len(waves) > 1 or (bool(waves) and unchanged)}


def settings_fingerprints(target, compose, manifest):
    """Each service's own settings as one hash: its settings file's contents (none for a service that takes no file)
    and the environment the runner gives it. Kept beside the release on the target, never in a job record, so the
    next rollout restarts a service whose settings changed although its definition did not."""
    dotnet = dotnet_services(manifest, compose["services"])
    required = manifest.get("requiredConfiguration") or {}
    result = {}
    for name in sorted(compose["services"]):
        path = settings_file(target, name) if name in required else None
        contents = hashlib.sha256(Path(path).read_bytes()).hexdigest() if path else None
        values = service_environment(target["environment"], name in dotnet)
        result[name] = hashlib.sha256(canonical({"settings": contents, "environment": values}).encode()).hexdigest()
    return result


def interpolation_inputs(target, manifest):
    """A fingerprint of what Compose interpolates into a release on this target — its variables and settings paths —
    so a rollout knows whether an unchanged definition still resolves the same. Holds no value, only its hash."""
    files = {name: settings_file(target, name) for name in sorted(manifest.get("requiredConfiguration", {}))}
    inputs = {"environment": target["environment"], "variables": target.get("variables", {}),
              "settings": {name: str(path) if path else None for name, path in files.items()}}
    return hashlib.sha256(canonical(inputs).encode()).hexdigest()


def service_port(manifest, service):
    return manifest.get("ports", {}).get(service, 8080)


def release_sites(manifest):
    """The sites a release serves, one row per service site: its port, the path prefixes it serves there (shortest
    first), the site's exposure, the environments that publish it (None: every one) and whether it is the primary site.
    Schema 1 declares one path per service site, each with its own exposure; schema 2 declares the product's sites
    (`sites`) and each service's paths per site (`routes`)."""
    images = manifest.get("images", {})
    if manifest.get("schemaVersion") == 2:
        roles, declared = site_roles(manifest.get("sites", {})), manifest.get("routes", {})
        require(isinstance(declared, dict) and set(declared) <= set(images), "Routes name an unknown service")
    else:
        roles, declared = None, manifest.get("sites", {})
        require(isinstance(declared, dict) and set(declared) <= set(images), "Sites name an unknown service")
    result, claimed = [], set()
    for service, sites in sorted(declared.items()):
        require(isinstance(sites, dict), "Invalid site declaration")
        for name, site in sorted(sites.items()):
            require(isinstance(name, str) and SLUG.fullmatch(name) and isinstance(site, dict), "Invalid site declaration")
            port = site.get("port", 8080)
            require(type(port) is int and 1 <= port <= 65535, "Invalid site port")
            if roles is None:
                path, exposure = site.get("path", "/"), site.get("exposure", "public")
                require(isinstance(path, str) and SITE_PATH.fullmatch(path), "Invalid site path")
                require(exposure in EXPOSURES, "Invalid site exposure")
                # `/api/` routes like `/api`: the path itself and everything below it.
                paths, role = [path.rstrip("/") or "/"], {"exposure": exposure, "environments": None, "primary": False}
            else:
                require(name in roles, "Routes name an undeclared site")
                paths, role = site.get("paths"), roles[name]
                require(isinstance(paths, list) and paths and all(route_path(path) for path in paths)
                        and len(set(paths)) == len(paths), "Invalid site path")
            for path in paths:
                # Each path of a site has one service, so a request always has exactly one destination.
                require((name, path) not in claimed, "Two services claim the same site path")
                claimed.add((name, path))
            result.append({"service": service, "site": name, "port": port,
                           "paths": sorted(paths, key=lambda path: (len(path), path)), **role})
    return result


def site_roles(declared):
    """A schema 2 release's sites: each one's exposure, the environments that publish it and whether it is primary."""
    require(isinstance(declared, dict), "Invalid site declaration")
    result = {}
    for name, role in declared.items():
        require(isinstance(name, str) and SLUG.fullmatch(name) and isinstance(role, dict), "Invalid site declaration")
        exposure, primary, environments = role.get("exposure", "public"), role.get("primary", False), role.get("environments")
        require(exposure in EXPOSURES, "Invalid site exposure")
        require(type(primary) is bool, "Invalid site declaration")
        require(environments is None or (isinstance(environments, list) and environments and all(
            isinstance(environment, str) and SLUG.fullmatch(environment) for environment in environments)),
            "Invalid site environments")
        result[name] = {"exposure": exposure, "environments": environments, "primary": primary}
    return result


def route_path(path):
    return (isinstance(path, str) and len(path) <= 200 and ROUTE_PATH.fullmatch(path) is not None
            and not {".", ".."} & set(path.split("/")))


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
    # The server's public IPv4 address, recorded at enrollment or by host upkeep: what baseline hosts name.
    address = ingress.get("publicAddress")
    require(address is None or (isinstance(address, str) and IPV4.fullmatch(address)), "Invalid public address")
    return ingress


def baseline_host(product, environment, site, primary, address):
    """A public site's host before its product has a domain (decision 68): `<product>-dev.<ip>.sslip.io` in dev,
    `<product>.<ip>.sslip.io` in prod, the address's dots as dashes; a site other than the primary one adds `-<site>`
    after the product. None when the name would not be a valid host (a label over 63 characters)."""
    label = product + ("" if primary else "-" + site) + ("" if environment == "prod" else "-" + environment)
    host = label + "." + address.replace(".", "-") + "." + BASELINE_DOMAIN
    return host if HOST.fullmatch(host) and len(label) <= 63 else None


def primary_site(sites):
    """The release's front door: the site it marks primary (descriptor 2 always marks one), else its first site."""
    return next((site["site"] for site in sites if site.get("primary")), sites[0]["site"] if sites else None)


def site_host(target, ingress, site, primary=None):
    """The host a site answers on in this target's environment: the target's named host, else the server's pattern,
    else, for a public site, its baseline host on the server's public address. The seam a domain binding
    (product, environment, site -> host) replaces; a binding may narrow a site's exposure, never widen it."""
    name = site["site"]
    host = ingress.get("hosts", {}).get(name)
    if host is None and ingress.get("pattern"):
        host = (ingress["pattern"].replace("{site}", name).replace("{product}", target["product"])
                .replace("{environment}", target["environment"]))
        require(HOST.fullmatch(host), "Site host pattern produced an invalid host")
    if host is None and site.get("exposure") == "public" and ingress.get("publicAddress"):
        host = baseline_host(target["product"], target["environment"], name, name == (primary or name),
                             ingress["publicAddress"])
    return host


def route_rule(host, path):
    """The Traefik rule for one site path: the whole host for `/`; otherwise the path itself or anything below it, so
    `/api` answers `/api` and `/api/…`, never `/apiary`."""
    rule = "Host(`" + host + "`)"
    return rule if path == "/" else rule + " && (Path(`" + path + "`) || PathPrefix(`" + path + "/`))"


def route_priority(path):
    """The longest path prefix wins, whatever each rule's length: 100 plus the path's length (`/` is 101)."""
    return 100 + len(path)


def route_label(path):
    return "root" if path == "/" else re.sub(r"[^a-z0-9]+", "-", path.lower()).strip("-") or "path"


def ingress_routes(target, project, manifest):
    """Traefik dynamic configuration for a release's sites on this target, and the URL of each routed service site.

    One router per site path, on the site's host, with the path's priority; every path of a service shares its
    upstream `<product>-<environment>-<service>:<port>`. A site its environments leave out, or one without a host or an
    entry point, stays unrouted. Each service site publishes the URL of its shortest path; a site's root comes first."""
    ingress = target_ingress(target)
    if ingress is None:
        return None, []
    default_port = {"http": 80, "https": 443}[ingress["scheme"]]
    port = ingress.get("port")
    origin_port = ":" + str(port) if port and port != default_port else ""
    routers, upstreams, published = {}, {}, []
    sites = release_sites(manifest)
    primary = primary_site(sites)
    for site in sites:
        if site["environments"] is not None and target["environment"] not in site["environments"]:
            continue
        host = site_host(target, ingress, site, primary)
        points = ingress.get("entryPoints" if site["exposure"] == "public" else "privateEntryPoints", [])
        if not host or not points:
            continue  # a site without a host or an entry point stays internal
        # Every service answers on the platform network under <product>-<environment>-<service>.
        upstream = project + "-" + site["service"] + "-" + str(site["port"])
        upstreams[upstream] = {"loadBalancer": {"servers": [
            {"url": "http://" + project + "-" + site["service"] + ":" + str(site["port"])}]}}
        for path in site["paths"]:
            router = {"rule": route_rule(host, path), "priority": route_priority(path), "service": upstream,
                      "entryPoints": points}
            # Only a public router takes the resolver: a private entry point sits behind its site node's Serve, which
            # ends TLS, and an HTTP-01 order for a tailnet name could never validate.
            if ingress["scheme"] == "https" and ingress.get("certResolver") and site["exposure"] == "public":
                router["tls"] = {"certResolver": ingress["certResolver"]}
            base = name = project + "-" + site["site"] + "-" + site["service"] + "-" + route_label(path)
            suffix = 2
            while name in routers:
                name, suffix = base + "-" + str(suffix), suffix + 1
            routers[name] = router
        entry = site["paths"][0]
        published.append(((site["site"], len(entry), entry, site["service"]),
                          {"name": site["site"], "service": site["service"], "exposure": site["exposure"],
                           "primary": site["site"] == primary,
                           "url": ingress["scheme"] + "://" + host + origin_port + ("" if entry == "/" else entry)}))
    return ({"http": {"routers": routers, "services": upstreams}},
            [record for _, record in sorted(published, key=lambda item: item[0])])


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


def connection_of(text):
    """The host, port, database, user and password of an Npgsql connection string, or None when it names no host and
    database (a Redis or broker string)."""
    if not isinstance(text, str):
        return None
    parts = {}
    for segment in text.split(";"):
        key, _, value = segment.partition("=")
        parts[key.strip().lower().replace(" ", "")] = value.strip()
    host = parts.get("host") or parts.get("server")
    database = parts.get("database") or parts.get("initialcatalog")
    if not host or not database:
        return None
    user = parts.get("username") or parts.get("userid") or parts.get("uid") or parts.get("user")
    port = parts.get("port") or "5432"
    require(all(CONNECTION_VALUE.fullmatch(value or "") for value in (host, database, user)) and port.isdigit()
            and 1 <= int(port) <= 65535, "A snapshot cannot read a database connection it could dump safely")
    return {"host": host, "port": int(port), "database": database, "username": user,
            "password": parts.get("password") or parts.get("pwd") or ""}


def snapshot_settings(target):
    """The image and retention a target's snapshots use; the defaults unless its `snapshot` entry names others."""
    settings = target.get("snapshot") or {}
    require(isinstance(settings, dict), "Invalid snapshot settings")
    image, keep = settings.get("image", SNAPSHOT_IMAGE), settings.get("keep", SNAPSHOT_KEEP)
    require(isinstance(image, str) and SNAPSHOT_IMAGE_PATTERN.fullmatch(image), "Invalid snapshot image")
    require(type(keep) is int and 1 <= keep <= 50, "Snapshot retention must keep 1-50")
    return image, keep


def snapshot_sources(target, manifest, compose, project):
    """What a pre-deploy snapshot covers: each PostgreSQL database a service that needs PostgreSQL connects to (from its
    settings file's ConnectionStrings), once, and every named volume the release's Compose declares."""
    needs = manifest.get("needs", {}) if isinstance(manifest.get("needs"), dict) else {}
    databases, seen = [], set()
    for service in sorted(manifest.get("images", {})):
        wanted = needs.get(service, [])
        path = settings_file(target, service) if service in (manifest.get("requiredConfiguration") or {}) else None
        if not isinstance(wanted, list) or "postgres" not in wanted or path is None or not path.is_file():
            continue
        settings = read_json(path)
        strings = settings.get("ConnectionStrings") if isinstance(settings, dict) else None
        for value in (strings.values() if isinstance(strings, dict) else ()):
            connection = connection_of(value)
            if connection is None:
                continue
            key = (connection["host"], connection["port"], connection["database"])
            if key not in seen:
                seen.add(key)
                databases.append({**connection, "service": service})
    volumes = []
    declared = compose.get("volumes") or {}
    for name, value in sorted(declared.items() if isinstance(declared, dict) else ()):
        value = value or {}
        if not isinstance(value, dict) or value.get("external"):
            continue
        full = value.get("name") or project + "_" + name
        require(isinstance(full, str) and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}", full), "Invalid volume name")
        volumes.append(full)
    return databases, volumes


def take_snapshot(base, job_id, target, manifest, compose, project, docker):
    """Dumps each database and tars each existing named volume into <base>/snapshots/<job>/ before anything changes,
    beside a snapshot.json naming them and how to restore them; keeps the newest snapshots. Returns the snapshot, or
    None when there is nothing to snapshot. Any failure removes the partial snapshot and fails the rollout."""
    image, keep = snapshot_settings(target)
    databases, volumes = snapshot_sources(target, manifest, compose, project)
    present = []
    for volume in volumes:
        try:
            docker.execute(["docker", "volume", "inspect", "--format", "{{.Name}}", volume], "docker volume inspect", timeout=30)
            present.append(volume)
        except CommandFailed:
            pass  # not created yet: nothing to keep
    if not databases and not present:
        return None
    folder = base / "snapshots" / job_id
    folder.mkdir(parents=True, mode=0o700)
    owner = str(os.getuid()) + ":" + str(os.getgid())
    network = target.get("variables", {}).get("PLATFORM_NETWORK")
    on_network = ["--network", network] if network else []
    taken = {"path": str(folder), "createdAt": now(), "image": image, "project": project, "network": network,
             "databases": [], "volumes": [], "restore": []}
    try:
        for database in databases:
            file = re.sub(r"[^A-Za-z0-9_.-]", "_", database["host"] + "-" + database["database"]) + ".dump"
            docker.run(["docker", "run", "--rm", "--user", owner, *on_network, "-e", "PGPASSWORD",
                        "-v", str(folder) + ":/snapshot", image, "pg_dump", "--host", database["host"],
                        "--port", str(database["port"]), *(["--username", database["username"]] if database["username"] else []),
                        "--dbname", database["database"], "--format", "custom", "--no-password", "--file", "/snapshot/" + file],
                       "pg_dump " + database["database"], {"PGPASSWORD": database["password"]})
            taken["databases"].append({"service": database["service"], "host": database["host"], "port": database["port"],
                                       "database": database["database"], "file": file,
                                       "bytes": (folder / file).stat().st_size if (folder / file).exists() else None})
            taken["restore"].append(shlex.join([
                "docker", "run", "--rm", *on_network, "-e", "PGPASSWORD", "-v", str(folder) + ":/snapshot:ro", image,
                "pg_restore", "--clean", "--if-exists", "--no-owner", "--host", database["host"], "--port", str(database["port"]),
                *(["--username", database["username"]] if database["username"] else []), "--dbname", database["database"],
                "/snapshot/" + file]))
        for volume in present:
            file = volume + ".tar.gz"
            # A volume holding a file database is archived while the containers writing to it are paused, so the copy
            # is consistent: a live SQLite file can otherwise be copied mid-write. Other volumes are archived live.
            paused = writers_of(docker, project, volume) if holds_file_database(docker, volume, image) else []
            began = time.monotonic()
            with paused_containers(docker, paused):
                # As root, since a volume's files belong to the service's user; the archive then passes to the runner's.
                docker.run(["docker", "run", "--rm", "-v", volume + ":/volume:ro", "-v", str(folder) + ":/snapshot", image,
                            "sh", "-c", "tar -czf /snapshot/" + file + " -C /volume . && chown " + owner + " /snapshot/" + file],
                           "tar " + volume)
            entry = {"volume": volume, "file": file,
                     "bytes": (folder / file).stat().st_size if (folder / file).exists() else None,
                     "consistency": "paused" if paused else "live"}
            if paused:
                entry["pausedSeconds"] = round(time.monotonic() - began, 1)
                entry["pausedContainers"] = len(paused)
            taken["volumes"].append(entry)
            taken["restore"].append(shlex.join([
                "docker", "run", "--rm", "-v", volume + ":/volume", "-v", str(folder) + ":/snapshot:ro", image, "sh", "-c",
                "find /volume -mindepth 1 -delete && tar -xzf /snapshot/" + file + " -C /volume"]))
        if present:
            # Volumes restore into stopped containers.
            taken["restore"].insert(0, "docker stop $(docker ps --quiet --filter label=com.docker.compose.project=" + project + ")")
        write_json(folder / "snapshot.json", taken)
    except BaseException:
        shutil.rmtree(folder, ignore_errors=True)
        raise
    prune_snapshots(base / "snapshots", keep, job_id)
    return taken


def holds_file_database(docker, volume, image):
    """Whether a named volume holds a file database: a file named as one (FILE_DATABASES) or starting with SQLite's
    header, whatever its name; read in a throwaway container without a network. Reads 15 bytes per file at most, never a
    file's contents."""
    names = " -o ".join("-name '" + pattern + "'" for pattern in FILE_DATABASES)
    script = ('if [ -n "$(find /volume -type f \\( ' + names + ' \\) 2>/dev/null | head -n 1)" ]; then echo found; exit 0; fi; '
              'find /volume -type f -size +99c 2>/dev/null | while IFS= read -r file; do '
              'if [ "$(head -c 15 "$file" 2>/dev/null)" = "' + SQLITE_HEADER + '" ]; then echo found; break; fi; done')
    output = docker.execute(["docker", "run", "--rm", "--network", "none", "-v", volume + ":/volume:ro", image,
                             "sh", "-c", script], "inspect " + volume, timeout=120)
    return output.strip() == "found"


def writers_of(docker, project, volume):
    """The project's running containers that mount a volume: the ones to pause while it is archived."""
    output = docker.execute(["docker", "ps", "--quiet", "--filter", "status=running",
                             "--filter", "label=com.docker.compose.project=" + project, "--filter", "volume=" + volume],
                            "docker ps", timeout=30)
    return sorted(set(output.split()))


@contextlib.contextmanager
def paused_containers(docker, containers):
    """Pauses containers for the duration, then resumes them, retrying a refused resume; a container that cannot be
    resumed fails the snapshot, so the rollout stops and names it."""
    paused = []
    try:
        for container in containers:
            docker.execute(["docker", "pause", container], "docker pause", timeout=60)
            paused.append(container)
        yield
    finally:
        stuck = 0
        for container in paused:
            for attempt in range(3):
                try:
                    docker.execute(["docker", "unpause", container], "docker unpause", timeout=60)
                    break
                except CommandFailed:
                    if attempt == 2:
                        stuck += 1
                    else:
                        time.sleep(1)
        if stuck:
            raise CommandFailed("docker unpause failed for " + plural(stuck, "container") + "; resume it by hand")


def prune_snapshots(folder, keep, current):
    """Keeps the newest `keep` complete snapshots and the current one; removes the rest and any partial one."""
    def taken_at(path):
        try:
            return (path / "snapshot.json").stat().st_mtime
        except OSError:
            return 0.0
    entries = sorted((path for path in folder.iterdir() if path.is_dir() and not path.is_symlink()),
                     key=taken_at, reverse=True)
    kept = {path.name for path in entries[:keep]} | {current}
    for path in entries:
        if path.name not in kept:
            shutil.rmtree(path, ignore_errors=True)


def modified_at(path, name=None):
    try:
        return (path / name if name else path).stat().st_mtime
    except OSError:
        return 0.0


def prune_releases(base, keep_ids, keep=KEEP_RELEASE_FOLDERS):
    """Removes the release folders beyond the newest `keep` whose images are already gone (marked), never one of
    `keep_ids` (the running, rollback and active releases). Returns how many went."""
    releases = base / "releases"
    if not releases.is_dir():
        return 0
    ordered = sorted((path for path in releases.iterdir() if path.is_dir() and not path.is_symlink()),
                     key=lambda path: modified_at(path, "release.json"), reverse=True)
    removed = 0
    for path in ordered[keep:]:
        if path.name in keep_ids or not (path / PRUNED).exists():
            continue
        shutil.rmtree(path, ignore_errors=True)
        removed += 1
    return removed


def prune_incoming(root, project, keep=KEEP_INCOMING, clock=time.time):
    """Removes this project's uploaded submissions (<root>/incoming/<request>/, named by their target.json) beyond the
    newest `keep` once each is a day old; another project's are never touched. A launched worker reads its upload only
    as it starts. Returns how many went."""
    incoming = root / "incoming"
    mine = []
    for path in incoming.iterdir() if incoming.is_dir() else ():
        if not path.is_dir() or path.is_symlink():
            continue
        try:
            target = read_json(path / "target.json")
        except (OSError, ValueError):
            continue
        if isinstance(target, dict) and str(target.get("product")) + "-" + str(target.get("environment")) == project:
            mine.append(path)
    mine.sort(key=modified_at, reverse=True)
    removed = 0
    for path in mine[keep:]:
        if clock() - modified_at(path) < INCOMING_GRACE_SECONDS:
            continue
        shutil.rmtree(path, ignore_errors=True)
        removed += 1
    return removed


def kept_releases(base):
    """The release folders a target must keep: the running release, its rollback release and the active rollout's."""
    def record(name):
        try:
            value = read_json(base / name)
            return value if isinstance(value, dict) else {}
        except (OSError, ValueError):
            return {}
    current, active = record("current.json"), record("active.json")
    running = record("jobs/" + str(current.get("id")) + ".json") if current.get("id") else {}
    return {value for value in (current.get("id"), running.get("previous"), active.get("id"), active.get("previous"))
            if isinstance(value, str)}


def prune(target):
    """The daily retention on one target, under its deployment lock: old release folders and old uploads. A target in
    the middle of a rollout is left for the next run."""
    root, project = validate_target(target)
    base = root / project
    if not base.is_dir():
        return {"project": project, "releases": 0, "incoming": 0}
    try:
        with deployment_lock(base):
            return {"project": project, "releases": prune_releases(base, kept_releases(base)),
                    "incoming": prune_incoming(root, project)}
    except Rejected:
        return {"project": project, "busy": True}


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


def renders(target):
    """Whether Wheelhouse renders the target's settings files at each deploy (its control plane marks the target);
    otherwise each service mounts the host file its target names, as CI's own targets still do."""
    return target.get("render") is True


def settings_folder(target):
    """Where a rendering target's settings live: <root>/config/<product>-<environment>/, or under the target's
    `settingsRoot` where the Docker daemon resolves bind sources elsewhere (the local server)."""
    root, project = validate_target(target)
    base = target.get("settingsRoot")
    if base is not None:
        require(isinstance(base, str) and Path(base).is_absolute() and Path(base) != Path("/"), "Invalid settings root")
        return Path(base) / project
    return root / SETTINGS_FOLDER / project


def rendered_path(target, service):
    return settings_folder(target) / (service + RENDERED)


def settings_file(target, service):
    """The settings file a service mounts: the rendered one on a rendering target, else the host file its target names
    (None when it names none)."""
    if renders(target):
        return rendered_path(target, service)
    path = target.get("settings", {}).get(service)
    return Path(path) if path else None


def private_settings(path, service):
    """Reads a settings file that only its owner can change: absolute, regular, never group- or world-writable, and
    private itself or inside a private folder."""
    require(path.is_absolute() and path.is_file() and not path.is_symlink(),
            "Settings must be an absolute regular file: " + service)
    require(path.stat().st_mode & 0o022 == 0 and
            (path.stat().st_mode & 0o077 == 0 or path.parent.stat().st_mode & 0o077 == 0),
            "Settings files must be private or inside a private directory: " + service)
    try:
        configuration = read_json(path)
    except ValueError:
        raise Rejected("Settings are not valid JSON: " + service) from None
    require(isinstance(configuration, dict), "Settings are not a JSON object: " + service)
    return configuration


def validate_settings(target, service, required):
    path = settings_file(target, service)
    require(path is not None, "Provide settings for every service")
    configuration = private_settings(path, service)
    for key in required:
        require(configuration_value(configuration, key) not in (None, "", []), "Missing required setting: " + service + ":" + key)
    hosts = configuration.get(HOSTS)
    if isinstance(hosts, str):
        # Health checks and smoke probes reach every service as http://localhost:<its port>.
        entries = {host.strip().lower() for host in hosts.split(";") if host.strip()}
        require(not entries or "*" in entries or "localhost" in entries,
                "AllowedHosts must include localhost for health probes: " + service)
    # ASP.NET binds only a JSON array here; a plain string silently trusts no proxy.
    proxies = configuration_value(configuration, PROXIES)
    require(proxies is None or isinstance(proxies, list), PROXIES + " must be a JSON array: " + service)
    return path


def settings_variable(service):
    """The Compose variable a service's settings file is interpolated through: `<SERVICE>_SETTINGS`."""
    return service.upper().replace("-", "_") + "_SETTINGS"


def environment_for(target, manifest):
    environment = base_environment(target)
    required = manifest["requiredConfiguration"]
    if renders(target):
        # Host files are an optional layer of the render; a service without one renders from Wheelhouse alone.
        require(set(target.get("settings", {})) <= set(required), "Settings name a service without a settings file")
    else:
        require(target.get("settings", {}).keys() == required.keys(), "Provide settings for every service that takes them")
    for service, keys in required.items():
        environment[settings_variable(service)] = str(validate_settings(target, service, keys))
    return environment


def mounted_file(path):
    """Whether a settings path can be a bind source: absolute, a regular file, not a link."""
    return path.is_absolute() and path.is_file() and not path.is_symlink()


def control_environment(target, manifest, bundle):
    """What Compose interpolates for a control of the release a target runs: each service's settings file as that
    release's rollout mounted it (`settings-paths.json` beside the release), else the rendered file, else the host file
    the target names — a release applied before its target rendered mounted that one. Neither renders nor checks a
    file's contents: a control changes no settings, it only needs the running containers' own files."""
    environment = base_environment(target)
    try:
        recorded = read_json(Path(bundle) / SETTINGS_PATHS)
    except (OSError, ValueError):
        recorded = {}
    recorded = recorded if isinstance(recorded, dict) else {}
    for service in sorted(manifest.get("requiredConfiguration") or {}):
        host = target.get("settings", {}).get(service)
        candidates = [recorded.get(service), rendered_path(target, service) if renders(target) else None, host]
        path = next((Path(item) for item in candidates if isinstance(item, (str, Path)) and mounted_file(Path(item))),
                    None)
        require(path is not None, "No settings file for " + service + " is left on this target; deploy the release again")
        environment[settings_variable(service)] = str(path)
    return environment


# ---- Settings render ----

def is_secret_key(key):
    """Whether a key reads as a secret by its name (`Billing:SecretKey`, `ConnectionStrings:Database`): its value
    belongs in the vault. A release's own declaration wins over this guess."""
    parts = key.split(":")
    return parts[0].lower() == "connectionstrings" or SECRET_KEY.fullmatch(parts[-1]) is not None


def connection_key(key):
    """A key holding a connection string: `ConnectionStrings:<name>` or `<Section>:ConnectionString`."""
    parts = key.split(":")
    return (len(parts) == 2 and parts[0].lower() == "connectionstrings") or parts[-1].lower() == "connectionstring"


def database_key(key, needs):
    """A connection key the target's own PostgreSQL database fills: any connection key of a service that needs only
    PostgreSQL among the platform services, else one that names a database."""
    if "postgres" not in needs or not connection_key(key):
        return False
    return not (set(needs) - {"postgres"}) or re.search(r"database|postgres|npgsql|sql|db", key, re.IGNORECASE) is not None


def service_needs(manifest, service):
    needs = manifest.get("needs", {})
    wanted = needs.get(service, []) if isinstance(needs, dict) else []
    return [need for need in wanted if need in PLATFORM_SERVICES] if isinstance(wanted, list) else []


def derived_settings(target, project, manifest, service):
    """What a render derives for one service from its target and release, whatever any layer says: `AllowedHosts` (the
    hosts of the sites it serves here, its platform name and localhost) and `Sites:<site>:Url` (the origin of every site
    the product publishes here). Deployment:TrustedProxies comes from the ingress on the target, at deploy."""
    _, published = ingress_routes(target, project, manifest)
    hosts, sites = [], {}
    for site in published:
        parts = urllib.parse.urlsplit(site["url"])
        if site["service"] == service and parts.hostname not in hosts:
            hosts.append(parts.hostname)
        sites.setdefault(site["name"], parts.scheme + "://" + parts.netloc)
    # The platform name lets another service reach this one by its internal address; localhost serves the probes.
    values = {HOSTS: ";".join(hosts + [project + "-" + service, "localhost"])}
    for name in sorted(sites):
        values["Sites:" + name + ":Url"] = sites[name]
    return values


def with_hosts(derived, kept):
    """The derived AllowedHosts, keeping any other host a host file lists (so a target moving to rendered settings answers
    every host it did), localhost last."""
    entries = [entry for entry in derived.split(";") if entry and entry != "localhost"]
    seen = {entry.lower() for entry in entries}
    for entry in (kept.split(";") if isinstance(kept, str) else []):
        entry = entry.strip()
        if entry and entry.lower() not in seen and entry.lower() != "localhost":
            entries.append(entry)
            seen.add(entry.lower())
    return ";".join(entries + ["localhost"])


def settings_declarations(target, manifest):
    """Every setting a release's services take on this target, without a value that is secret: each required key, each
    optional one with its default, and what the render derives (`hosts`, `proxies`, `sites`, `database`) with the value
    when the control plane knows it. `secret` is the release's declaration, else a guess from the key's name."""
    _, project = validate_target(target, manifest)
    declared = release_settings(manifest)
    network = target.get("variables", {}).get("PLATFORM_NETWORK")
    services = []
    for service, required in sorted((manifest.get("requiredConfiguration") or {}).items()):
        entry = declared.get(service, {"optional": {}, "secrets": [], "allowed": {}, "types": {}})
        needs = service_needs(manifest, service)
        derived = derived_settings(target, project, manifest, service)
        keys = {}
        # A typed key the release names neither required nor optional is an optional key without a default.
        for key in [*required, *entry["optional"], *entry["secrets"], *entry["types"]]:
            if key in keys:
                continue
            typed = entry["types"].get(key)
            item = {"key": key, "required": key in required,
                    "secret": key in entry["secrets"]
                    or (key not in entry["optional"] and typed is None and is_secret_key(key))}
            if key in entry["optional"]:
                item["default"] = entry["optional"][key]
            if key in entry["allowed"]:
                item["allowed"] = list(entry["allowed"][key])
            if typed is not None:
                item["type"] = typed["type"]
                if typed.get("format"):
                    item["format"] = typed["format"]
            keys[key] = item
        for key, value in derived.items():
            keys.setdefault(key, {"key": key, "required": False, "secret": False})
            keys[key].update(derived="hosts" if key == HOSTS else "sites", value=value, secret=False)
        if network:
            keys.setdefault(PROXIES, {"key": PROXIES, "required": False, "secret": False})
            keys[PROXIES].update(derived="proxies", secret=False)
        for key, item in keys.items():
            if "derived" not in item and database_key(key, needs):
                item["derived"] = "database"
        services.append({"name": service, "hostFile": bool(target.get("settings", {}).get(service)),
                         "keys": sorted(keys.values(), key=lambda item: (not item["required"], item["key"]))})
    return {"product": target["product"], "environment": target["environment"], "project": project,
            "services": services}


def set_value(document, key, value):
    """Sets a `Section:Key` path in a nested settings document, replacing a value that stood where a section goes."""
    *sections, last = key.split(":")
    node = document
    for section in sections:
        if not isinstance(node.get(section), dict):
            node[section] = {}
        node = node[section]
    node[last] = value


def merge(document, layer):
    """Lays one settings layer over a document: sections merge, values replace."""
    for key, value in layer.items():
        if isinstance(value, dict) and isinstance(document.get(key), dict):
            merge(document[key], value)
        else:
            document[key] = json.loads(json.dumps(value))


def flat_keys(document, prefix=""):
    keys = []
    for key, value in document.items():
        path = prefix + key
        keys.extend(flat_keys(value, path + ":") if isinstance(value, dict) and value else [path])
    return keys


def provided_values(provided):
    """The control plane's layer: stored settings and vault secrets of the target's environment, by key (`values`), as
    plain strings. Validated before any of it is used; never written anywhere but the rendered files."""
    values = (provided or {}).get("values") or {}
    require(isinstance(values, dict) and len(values) <= 500, "Invalid provided settings")
    for key, value in values.items():
        require(isinstance(key, str) and SETTING_KEY.fullmatch(key) and isinstance(value, str) and len(value) <= 65536,
                "Invalid provided settings")
    return values


def delivered_to(service, key, required, declared):
    """Whether a provided value goes to a service: to each service that declares its key, and a key no service declares
    to every service that takes a settings file."""
    declaring = {name for name, keys in required.items() if key in keys} | {
        name for name, entry in declared.items() if key in entry["optional"] or key in entry["secrets"]}
    return service in declaring or not declaring


def ingress_address(docker, network):
    """The ingress's address on the platform network: the container of the host kit's `ingress` service (a
    `wheelhouse-*` Compose project). None when the network has none."""
    if not network:
        return None
    try:
        ids = docker.execute(["docker", "ps", "--quiet", "--filter", "network=" + network,
                              "--filter", "label=com.docker.compose.service=" + INGRESS_SERVICE], "docker ps", timeout=30).split()
        if not ids:
            return None
        details = json.loads(docker.execute(["docker", "inspect", *ids[:10]], "docker inspect", timeout=30))
    except (CommandFailed, ValueError):
        return None
    for item in details if isinstance(details, list) else []:
        labels = (item.get("Config") or {}).get("Labels") or {}
        if not str(labels.get("com.docker.compose.project", "")).startswith("wheelhouse-"):
            continue
        address = (((item.get("NetworkSettings") or {}).get("Networks") or {}).get(network) or {}).get("IPAddress")
        if isinstance(address, str) and re.fullmatch(r"[0-9]{1,3}(\.[0-9]{1,3}){3}", address):
            return address
    return None


def target_database(folder):
    """The connection string of the target's own database: database.json in its settings folder, written once by the
    host kit (`prepare.py --database`). None when the product has no database there."""
    path = folder / DATABASE_FILE
    if not path.is_file() or path.is_symlink():
        return None
    require(path.stat().st_mode & 0o077 == 0 or folder.stat().st_mode & 0o077 == 0, "database.json must be private")
    try:
        value = read_json(path).get("ConnectionString")
    except (ValueError, AttributeError):
        raise RenderRefused("database.json is not readable") from None
    require(isinstance(value, str) and value, "database.json names no connection string")
    return value


def private_folder(folder):
    """The settings folder, created mode 0700 when missing; an existing one must be a private directory."""
    if not folder.exists():
        folder.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        folder.mkdir(mode=0o700)
    require(folder.is_dir() and not folder.is_symlink() and folder.stat().st_mode & 0o077 == 0,
            "The settings folder must be a private directory: " + SETTINGS_FOLDER + "/" + folder.name)


def write_settings(path, document):
    """Publishes a rendered settings file atomically, mode 0444 inside its mode-0700 folder (the container's own user
    reads the bind-mounted file); an identical file stays as it is. Returns whether it changed."""
    content = (json.dumps(document, indent=2) + "\n").encode()
    require(not path.is_symlink(), "A rendered settings file cannot be a symlink")
    if path.is_file() and path.read_bytes() == content and path.stat().st_mode & 0o777 == 0o444:
        return False
    temporary = path.with_name("." + path.name + "." + uuid.uuid4().hex + ".tmp")
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temporary, 0o444)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)
    directory = os.open(path.parent, os.O_RDONLY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)
    return True


def render_settings(target, manifest, project, provided, docker):
    """Renders every settings file a release mounts on this target, in layers: the host file the target names (if any),
    then the control plane's stored settings and vault secrets, then what the render derives — AllowedHosts, site URLs
    and the ingress as Deployment:TrustedProxies — and, for a required connection key no layer sets, the target's own
    database. Refuses a required key no layer sets, before writing anything. Returns each service's keys by source; no
    value leaves the target."""
    required = manifest.get("requiredConfiguration") or {}
    declared = release_settings(manifest)
    values = provided_values(provided)
    folder = settings_folder(target)
    proxy = ingress_address(docker, target.get("variables", {}).get("PLATFORM_NETWORK"))
    database = target_database(folder) if folder.is_dir() else None
    documents, sources = {}, {}
    for service in sorted(required):
        document, used = {}, {"host": [], "provided": [], "derived": [], "database": []}
        host = target.get("settings", {}).get(service)
        if host and Path(host) != rendered_path(target, service) and Path(host).exists():
            layer = private_settings(Path(host), service)
            merge(document, layer)
            used["host"] = sorted(flat_keys(layer))
        for key in sorted(values):
            if delivered_to(service, key, required, declared):
                set_value(document, key, values[key])
                used["provided"].append(key)
        derived = derived_settings(target, project, manifest, service)
        if proxy:
            derived[PROXIES] = [proxy]
        derived[HOSTS] = with_hosts(derived[HOSTS], document.get(HOSTS))
        for key, value in derived.items():
            set_value(document, key, value)
            used["derived"].append(key)
        needs = service_needs(manifest, service)
        for key in [*required[service], *declared.get(service, {}).get("optional", {})]:
            if database and database_key(key, needs) and configuration_value(document, key) in (None, "", []):
                set_value(document, key, database)
                used["database"].append(key)
        for key in required[service]:
            if configuration_value(document, key) in (None, "", []):
                raise RenderRefused("Missing required setting: " + service + ":" + key)
        documents[service], sources[service] = document, used
    if documents:
        private_folder(folder)
    changed = [service for service in sorted(documents) if write_settings(rendered_path(target, service), documents[service])]
    return {"services": sources, "changed": changed, "proxy": proxy is not None}


def render_summary(rendered):
    """One line on a render: how many services and keys, by source; never a value."""
    services = rendered["services"]
    count = {source: sum(len(used[source]) for used in services.values())
             for source in ("derived", "provided", "host", "database")}
    parts = [plural(count["derived"], "derived key")]
    if count["provided"]:
        parts.append(str(count["provided"]) + " from Wheelhouse")
    if count["database"]:
        parts.append(str(count["database"]) + " from the target's database")
    if count["host"]:
        parts.append(str(count["host"]) + " from host files")
    changed = len(rendered["changed"])
    return (plural(len(services), "service") + ": " + ", ".join(parts)
            + ("; " + str(changed) + " changed" if changed else "; unchanged"))


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

    def run(self, arguments, step, secrets=None, timeout=1800):
        """A long command whose environment also carries `secrets` (never its arguments); output is discarded."""
        try:
            result = subprocess.run(arguments, env={**self.environment, **(secrets or {})}, capture_output=True,
                                    text=True, timeout=timeout)
        except subprocess.TimeoutExpired:
            raise CommandFailed(step + " timed out after " + str(timeout) + "s") from None
        if result.returncode:
            raise CommandFailed(step + " failed (exit " + str(result.returncode) + "); inspect the target privately")

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
        override = environment_override(bundle, self.target["environment"])
        return self.execute(["docker", "compose", "--project-name", self.project,
                             "--env-file", "/dev/null", "-f", str(Path(bundle) / "compose.json"), "-f", str(override),
                             *arguments],
                            "compose " + arguments[0])

    def containers(self, bundle):
        """The release's containers as `compose ps` reports them: service names, states and health only, never logs."""
        try:
            output = self.compose(bundle, "ps", "--all", "--format", "json").strip()
            rows = json.loads(output) if output.startswith("[") else [json.loads(line) for line in output.splitlines()]
        except (CommandFailed, ValueError):
            return []
        return [row for row in rows if isinstance(row, dict)]

    def unhealthy(self, bundle):
        # Diagnosis after a failed wait: service names and container states only, never logs.
        return sorted(row.get("Service", "unknown") + " (" + (row.get("Health") or row.get("State") or "unknown") + ")"
                      for row in self.containers(bundle) if row.get("Health") != "healthy")

    def service_health(self, bundle):
        """Each service's health now: `healthy`; `unhealthy` when its check fails or its container exited, died or
        keeps restarting; a service without a container, or still starting, is left out (unknown)."""
        result = {}
        for row in self.containers(bundle):
            service, health, state = row.get("Service"), row.get("Health"), row.get("State")
            if isinstance(service, str):
                result[service] = ("healthy" if health == "healthy" else
                                   "unhealthy" if health == "unhealthy" or state in ("exited", "dead", "restarting")
                                   else "unknown")
        return result

    def pull(self, bundle, registry=None, unreadable=()):
        """Fetches a release's images that the host lacks; digests make one already present exact. A missing image on
        `registry`'s server (of its `repositories`, when it names them) pulls under a sign-in made for this pull only, in a
        temporary Docker config that is signed
        out of and deleted straight after, so no registry credential stays on the host. A missing image of a repository
        the control host found nothing could read (`unreadable`) fails the pull before any sign-in, naming its package
        and what to place. Returns how many images were fetched and the credential a sign-in used, if any."""
        self.compose(bundle, "config", "--quiet")
        images = sorted(set(read_json(Path(bundle) / "release.json")["images"].values()))
        missing = [image for image in images if not self.present(image)]
        blocked = sorted({image_repository(image) for image in missing} & set(unreadable))
        if blocked:
            raise CommandFailed("No credential on the control host can read " + ", ".join(blocked) + ": "
                                + UNREADABLE_HINT)
        signed = None
        # A sign-in that names its repositories covers only those: another owner's public pinned image (M77) pulls
        # anonymously with the rest.
        private = [image for image in missing if registry and image.startswith(registry["server"] + "/")
                   and (registry.get("repositories") is None or image_repository(image) in registry["repositories"])]
        if private:
            with registry_session(self, registry) as config:
                for image in private:
                    self.execute(["docker", "--config", config, "pull", "--quiet", image], "docker pull", timeout=1800)
            signed = registry["credential"]
        self.compose(bundle, "pull", "--policy", "missing")
        return {"images": len(images), "fetched": len(missing), "signedIn": signed}

    def present(self, image):
        try:
            self.execute(["docker", "image", "inspect", "--format", "{{.Id}}", image], "docker image inspect", timeout=30)
            return True
        except CommandFailed:
            return False

    def sign_in(self, config, registry):
        """`docker login` into a temporary config with the password on stdin; its output is discarded."""
        try:
            result = subprocess.run(["docker", "--config", config, "login", registry["server"], "--username",
                                     registry["username"], "--password-stdin"], env=self.environment,
                                    input=registry["password"], capture_output=True, text=True, timeout=60)
        except subprocess.TimeoutExpired:
            raise CommandFailed("registry sign-in timed out") from None
        if result.returncode:
            raise CommandFailed("registry sign-in to " + registry["server"] + " failed (exit " + str(result.returncode)
                                + "); the credential was refused")

    def check(self, arguments, variables, timeout, container):
        """Runs a one-off check container whose environment also carries `variables` (passed by name, never as
        arguments). Returns its exit code and its output's last nonblank line, cut short; a run past `timeout` removes
        `container` and returns None for the code. The rest of the output is discarded."""
        try:
            result = subprocess.run(arguments, env={**self.environment, **variables}, stdout=subprocess.PIPE,
                                    stderr=subprocess.STDOUT, text=True, errors="replace", timeout=timeout)
        except subprocess.TimeoutExpired:
            with contextlib.suppress(Exception):
                subprocess.run(["docker", "rm", "--force", container], env=self.environment, capture_output=True,
                               timeout=60)
            return None, ""
        lines = [line.strip() for line in (result.stdout or "").splitlines() if line.strip()]
        return result.returncode, lines[-1][:VERIFY_SUMMARY_LIMIT] if lines else ""

    def migrate(self, arguments, variables, timeout, container):
        """Runs a one-off migrate container whose environment also carries `variables` (the migrate key, passed by
        name, never as an argument). Returns its exit code and the last stdout line that starts with `migrate-result `
        (None when it printed none); a run past `timeout` stops and removes `container` and returns None for the code.
        stderr and every other line are discarded unread: they may name the database."""
        try:
            result = subprocess.run(arguments, env={**self.environment, **variables}, stdout=subprocess.PIPE,
                                    stderr=subprocess.DEVNULL, text=True, errors="replace", timeout=timeout)
        except subprocess.TimeoutExpired:
            for command in (["docker", "stop", "--time", "10", container], ["docker", "rm", "--force", container]):
                with contextlib.suppress(Exception):
                    subprocess.run(command, env=self.environment, capture_output=True, timeout=60)
            return None, None
        output = (result.stdout or "")[-MIGRATE_OUTPUT_LIMIT:]
        line = next((line.strip() for line in reversed(output.splitlines()) if line.startswith(MIGRATE_RESULT)), None)
        return result.returncode, line

    def health_timeout(self):
        timeout = self.target.get("healthTimeoutSeconds", 120)
        require(type(timeout) is int and 10 <= timeout <= 300, "Invalid health timeout")
        return timeout

    def up(self, bundle, recreate=None):
        """The whole release: removes services it dropped and waits for every health check. `recreate` `never` leaves
        every existing container as it is (creating only a missing one, starting a stopped one), `force` recreates every
        service, and None (a roll back) recreates what differs from the release."""
        require(recreate in (None, "never", "force"), "Invalid recreate mode")
        mode = {"never": ["--no-recreate"], "force": ["--force-recreate"]}.get(recreate, [])
        self.compose(bundle, "up", "--detach", "--wait", "--wait-timeout", str(self.health_timeout()),
                     "--remove-orphans", "--pull", "never", *mode)

    def up_services(self, bundle, services):
        """Recreates only `services` — never their dependencies or dependents, whatever Compose's own config hash
        says, so a settings-only change applies — and waits for their health checks."""
        require(services and all(isinstance(name, str) and SLUG.fullmatch(name) for name in services), "Invalid services")
        self.compose(bundle, "up", "--detach", "--wait", "--wait-timeout", str(self.health_timeout()),
                     "--no-deps", "--force-recreate", "--pull", "never", *services)

    def stop(self, bundle, services=()):
        """Stops `services`, or the whole release without any: the containers, volumes and routes stay."""
        self.compose(bundle, "stop", "--timeout", str(STOP_GRACE_SECONDS), *services)

    def start(self, bundle, services=()):
        """Starts `services` (never their dependencies), or the whole release without any, as the containers the
        release created — nothing is pulled or recreated — and waits for their health checks."""
        self.compose(bundle, "up", "--detach", "--wait", "--wait-timeout", str(self.health_timeout()),
                     "--no-recreate", "--pull", "never", *(["--no-deps", *services] if services else []))

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


def registry_login(provided):
    """The registry sign-in the control plane sent for one deploy: server, username, password and which credential it
    is. None when it sent none (public images, or images the host already has)."""
    login = (provided or {}).get("registry")
    if login is None:
        return None
    require(isinstance(login, dict) and REGISTRY_HOST.fullmatch(str(login.get("server", "")))
            and REGISTRY_USER.fullmatch(str(login.get("username", ""))) and login.get("credential") in CREDENTIALS
            and isinstance(login.get("password"), str) and 0 < len(login["password"]) <= 4096
            and not any(character.isspace() for character in login["password"]), "Invalid registry sign-in")
    result = {key: login[key] for key in ("server", "username", "password", "credential")}
    # The repositories the sign-in is for, when a release also runs another owner's public pinned image (M77).
    repositories = login.get("repositories")
    if repositories is not None:
        require(isinstance(repositories, list) and 0 < len(repositories) <= 100
                and all(isinstance(item, str) and IMAGE_REPOSITORY.fullmatch(item) for item in repositories),
                "Invalid registry sign-in")
        result["repositories"] = list(repositories)
    return result


def registry_unreadable(provided):
    """The image repositories the control plane found nothing could read for one deploy; empty when it sent none."""
    unreadable = (provided or {}).get("unreadable", [])
    require(isinstance(unreadable, list) and len(unreadable) <= 100
            and all(isinstance(item, str) and IMAGE_REPOSITORY.fullmatch(item) for item in unreadable),
            "Invalid unreadable images")
    return tuple(unreadable)


def image_repository(image):
    """An image reference's repository: no tag, no digest, lowercase (`ghcr.io/<owner>/<path>`)."""
    name = str(image).split("@", 1)[0]
    head, _, last = name.rpartition("/")
    return ((head + "/" if head else "") + last.split(":", 1)[0]).lower()


@contextlib.contextmanager
def registry_session(docker, registry):
    """A temporary Docker config signed in to the registry, in memory where the host offers /dev/shm; signed out of and
    deleted on leaving, whatever happened inside."""
    memory = "/dev/shm" if os.path.isdir("/dev/shm") and os.access("/dev/shm", os.W_OK) else None
    config = tempfile.mkdtemp(prefix="wheelhouse-registry-", dir=memory)
    try:
        docker.sign_in(config, registry)
        yield config
    finally:
        with contextlib.suppress(Exception):
            docker.execute(["docker", "--config", config, "logout", registry["server"]], "docker logout", timeout=30)
        shutil.rmtree(config, ignore_errors=True)


def pull_summary(manifest, pulled):
    """The pull step's detail: the images, how many were fetched and how a sign-in was made; never a credential."""
    detail = plural(len(manifest["images"]), "image")
    if isinstance(pulled, dict):
        detail += "; " + (str(pulled["fetched"]) + " fetched" if pulled.get("fetched") else "all on the host")
        if pulled.get("signedIn") in CREDENTIALS:
            detail += ", signed in for this pull with " + CREDENTIALS[pulled["signedIn"]]
    return detail


def service_environment(environment, dotnet=False):
    """What the runner sets on every service of a release: DEPLOY_ENVIRONMENT, the environment it runs in; and for a
    release that opts in (`dotnet`, its `dotnetEnvironment`), ASPNETCORE_ENVIRONMENT and DOTNET_ENVIRONMENT as .NET
    names it — dev Development, prod Production."""
    values = {"DEPLOY_ENVIRONMENT": environment}
    if dotnet and environment in DOTNET_ENVIRONMENTS:
        values["ASPNETCORE_ENVIRONMENT"] = values["DOTNET_ENVIRONMENT"] = DOTNET_ENVIRONMENTS[environment]
    return values


def environment_override(bundle, environment):
    """Writes, beside a release's compose.json, the Compose override that gives every service DEPLOY_ENVIRONMENT — the
    environment it runs in (`dev`, `prod`) — so a product reads it without declaring it, and the .NET environment when
    the release opts in (`dotnetEnvironment`). Rewritten only when it differs; a release copied by an older runner gains
    it the first time it is applied or rolled back to. Returns its path."""
    require(SLUG.fullmatch(str(environment)), "Invalid target environment")
    bundle = Path(bundle)
    services = read_json(bundle / "compose.json").get("services")
    require(isinstance(services, dict) and services, "Compose declares no services")
    manifest = read_json(bundle / "release.json")
    dotnet = dotnet_services(manifest, services)
    override = {"services": {name: {"environment": service_environment(environment, name in dotnet)}
                             for name in sorted(services)}}
    path = bundle / ENVIRONMENT_OVERRIDE
    try:
        current = read_json(path)
    except (OSError, ValueError):
        current = None
    if current != override:
        write_json(path, override)
    return path


def apply(bundle, target, actor, job_id=None, docker_factory=Docker, prober=probe_sites, provided=None):
    """Applies a release to its target under the deployment lock. `provided` is what the control plane sent for this
    deploy alone, never stored: its stored settings and vault secrets (`values`) for a rendering target, and a registry
    sign-in for private images (`registry`)."""
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
        # What the owner stopped stays stopped: a deploy, automatic or not, never starts it again on its own.
        stopped = stopped_of(root)
        require(stopped is None, stopped_refusal(project, stopped))
        registry = registry_login(provided)
        unreadable = registry_unreadable(provided)
        rendered, render_started = None, now()
        if renders(target):
            # Every settings file the release mounts, rendered before anything else reads it; a missing required key
            # refuses the deploy here, before any file or container changes.
            rendered = render_settings(target, manifest, project, provided,
                                       docker_factory(target, base_environment(target)))
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
        environment_override(destination, target["environment"])
        write_json(destination / SETTINGS_PATHS,
                   {service: environment[settings_variable(service)] for service in manifest["requiredConfiguration"]})
        previous_path = root / "current.json"
        previous = read_json(previous_path) if previous_path.exists() else None
        record = {"id": job_id, "project": project, "release": manifest["release"],
                  "sourceCommit": manifest["sourceCommit"], "kind": manifest.get("kind", "release"),
                  "actor": actor, "status": "running", "startedAt": now(),
                  "previous": previous.get("id") if previous else None}
        for key in ("branch", "versions"):
            if manifest.get(key):
                record[key] = manifest[key]
        # Service by service: what the release changes against the release the target runs, and in which waves.
        compose = read_json(destination / "compose.json")
        settings = settings_fingerprints(target, compose, manifest)
        write_json(destination / FINGERPRINTS, {"services": settings})
        plan = plan_against(root, previous, compose, target, manifest, settings)
        record["interpolation"] = interpolation_inputs(target, manifest)
        record["serviceRollout"] = {
            "staged": plan["staged"], "waves": plan["waves"], "removed": plan["removed"],
            "services": [{**service, "outcome": "unchanged" if service["change"] == "unchanged" else "pending",
                          "health": "unknown", "at": None} for service in plan["services"]]}
        starting = None
        def save():
            write_json(root / "jobs" / (job_id + ".json"), record)
            write_json(active_path, record)
        steps = Steps(record, save)
        if rendered is not None:
            # Key names by source, never a value: what the render derived, what Wheelhouse and the host files gave.
            record["settings"] = {service: {source: keys for source, keys in used.items() if keys}
                                  for service, used in rendered["services"].items()}
            steps.add("Render settings", "succeeded", render_started, render_summary(rendered))
        steps.add("Check target", "succeeded", started, "Settings, disk, Docker and platform verified")
        mutation_started = False
        if target["environment"] == "prod":
            # A restore point at the exact cutover; a failed snapshot fails the rollout before anything changes.
            try:
                with steps.run("Snapshot data") as step:
                    taken = take_snapshot(root, job_id, target, manifest, read_json(destination / "compose.json"),
                                          project, docker)
                    if taken is None:
                        step.update(status="skipped", detail="No database or volume to snapshot")
                    else:
                        record["snapshot"] = taken
                        paused = [volume for volume in taken["volumes"] if volume.get("consistency") == "paused"]
                        step["detail"] = (plural(len(taken["databases"]), "database") + " and "
                                          + plural(len(taken["volumes"]), "volume") + " in " + taken["path"]
                                          + ("; " + plural(len(paused), "file-database volume")
                                             + " archived with its writers paused" if paused else ""))
            except (Exception, KeyboardInterrupt) as error:
                record.update(status="failed", failure=type(error).__name__, completedAt=now())
                record["reason"] = "Snapshot failed: " + (reason(error) or "the snapshot could not be taken")
                settle_services(record, None)
                save()
                return record
        try:
            with steps.run("Pull images") as step:
                pulled = (docker.pull(destination, registry, unreadable) if registry or unreadable
                          else docker.pull(destination))
                step["detail"] = pull_summary(manifest, pulled)
            # Every declared item, on every forward deploy, before any service restarts; a return to an older release
            # runs its rollback gate instead. A failure here leaves the previous release serving.
            migrate_release(root, target, previous, manifest, compose, project, environment, docker, steps, record,
                            job_id)
            mutation_started = True
            record["mutationStarted"] = True
            save()
            if plan["staged"]:
                # Changed services only, each recreated alone, in dependency order: a wave starts after the waves it
                # depends on are healthy, and a failing wave leaves the waves after it unstarted.
                for wave in plan["waves"]:
                    starting = wave
                    with steps.run("Start " + ", ".join(wave)) as step:
                        docker.up_services(destination, wave)
                        step["detail"] = plural(len(wave), "service") + " healthy"
                    settle_services(record, None, wave)
            starting = [service["name"] for service in record["serviceRollout"]["services"] if service["outcome"] == "pending"]
            with steps.run("Start containers") as step:
                # Converges the whole release: removed services go and every health check is awaited. Once the waves
                # ran, or when nothing changed, it recreates nothing, so an unchanged service keeps running even when a
                # dependency it uses was replaced; when every service changed in one wave, it recreates them all.
                docker.up(destination, "force" if starting else "never")
                step["detail"] = rollout_summary(record["serviceRollout"])
            settle_services(record, None, starting, kept=True)
            starting = None
            with steps.run("Verify services") as step:
                docker.verify(destination, manifest)
                record_health(record, {name: "healthy" for name in manifest["images"]})
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
                    if target.get("probeGate") is True and (not probed or failed):
                        # A gated target (a Wheelhouse console) that its ingress cannot reach rolls back: healthy
                        # containers no one can open are no better than failed ones.
                        raise Unreachable("The site probe could not run" if probed is None else
                                            "No probe address for this ingress" if not probed else
                                            "Site " + failed[0]["name"] + " did not answer through the ingress: "
                                            + failed[0]["probe"]["detail"])
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
            # Dev only, once the release is recorded: the release's own end-to-end check. Its result gates auto-promote
            # and never changes the outcome; a failure is a warning, since every container is healthy.
            verification = release_verification(manifest) if target["environment"] == "dev" else None
            if verification:
                with contextlib.suppress(Exception), steps.run("Verify end to end") as step:
                    result = verify_release(target, manifest, record, verification, docker, project)
                    record["verification"] = result
                    if result["status"] == "passed":
                        step["detail"] = "Passed in " + str(result["seconds"]) + "s"
                    else:
                        step.update(status="warning", detail=result["detail"])
                        record.setdefault("warnings", []).append(
                            "The end-to-end check failed: " + result["detail"] + "; auto-promote holds this release")
            # Housekeeping after the release is recorded; it never changes the outcome.
            with contextlib.suppress(Exception), steps.run("Remove unused images") as step:
                removed = prune_images(root, record, docker, KEEP_RELEASES)
                step.update(status="succeeded" if removed else "skipped",
                            detail=plural(removed, "image") + " from older releases" if removed
                            else "No image only older releases used")
            with contextlib.suppress(Exception), steps.run("Remove old files") as step:
                folders = prune_releases(root, {record["id"], record.get("previous")})
                uploads = prune_incoming(root.parent, project)
                step.update(status="succeeded" if folders or uploads else "skipped",
                            detail=plural(folders, "release folder") + " and " + plural(uploads, "upload")
                            if folders or uploads else "Nothing past the retention")
        except (Exception, KeyboardInterrupt) as error:
            record["status"] = "failed"
            # Only safe diagnostic categories; exception messages may include input/credential values.
            record["failure"] = type(error).__name__
            if reason(error):
                record["reason"] = reason(error)
            if mutation_started and isinstance(error, CommandFailed) and not isinstance(error, Unreachable):
                with contextlib.suppress(Exception):
                    states = docker.unhealthy(destination)
                    if states:
                        record["reason"] += "; not healthy: " + ", ".join(states)
            settle_services(record, starting if mutation_started else None)
            # A release that declares it, or a target that does: the previous images run on whatever schema this release
            # left. A release that ran its migrate step asks the previous images' own `migrate check` first (the
            # rollback gate, M15); a gated return restores the release it left, which ran on this very database.
            if mutation_started and previous and restore_allowed(
                    root, target, previous, record, steps, job_id, project,
                    manifest["rollbackCompatible"] or target.get("rollbackCompatible") is True, docker_factory):
                try:
                    with steps.run("Roll back to " + str(previous.get("release") or "the previous release")):
                        prior_bundle = root / "releases" / previous["id"]
                        prior_manifest = validate_bundle(prior_bundle)
                        prior_environment = environment_for(target, prior_manifest)
                        prior_docker = docker_factory(target, prior_environment)
                        if registry or unreadable:
                            prior_docker.pull(prior_bundle, registry, unreadable)
                        else:
                            prior_docker.pull(prior_bundle)
                        # The whole previous release: Compose replaces only what differs, so exactly the services
                        # this rollout touched return to their previous images.
                        prior_docker.up(prior_bundle)
                        prior_docker.verify(prior_bundle, prior_manifest)
                    record["status"] = "rolled_back"
                    settle_services(record, None, rolled_back=True)
                    # Every service of the previous release passed its check; one this release added is gone.
                    record_health(record, {name: "healthy" for name in prior_manifest["images"]})
                except Exception:
                    record["status"] = "rollback_failed"
            if mutation_started and record["status"] != "rolled_back":
                with contextlib.suppress(Exception):
                    record_health(record, docker.service_health(destination))
        record["completedAt"] = now()
        save()
        return record


def upper_name(name):
    return re.sub(r"[^A-Z0-9]", "_", name.upper())


def verify_release(target, manifest, record, verification, docker, project, clock=time.monotonic):
    """Runs a release's end-to-end check after its dev rollout succeeded: the declared command in a one-off container
    from the service's release image, on the platform network. Its environment holds the environment, the release, the
    published sites (`VERIFY_SITES` as JSON, `VERIFY_SITE_<NAME>` each) and each service's address on the platform
    network (`VERIFY_SERVICE_<NAME>`) — no settings file and no secret. Never raises; returns what the job keeps:
    status (`passed` | `failed`), service, timings, exit code, an operator-safe detail and the output's last line."""
    started, begun = now(), clock()
    timeout, command = verification["timeoutSeconds"], verification["command"]
    sites = [{key: site[key] for key in ("name", "service", "exposure", "url") if key in site}
             for site in record.get("sites") or [] if isinstance(site, dict) and isinstance(site.get("url"), str)]
    variables = {"DEPLOY_ENVIRONMENT": target["environment"], "VERIFY_RELEASE": manifest["release"],
                 "VERIFY_SITES": json.dumps(sites)}
    for site in sites:
        variables.setdefault("VERIFY_SITE_" + upper_name(site["name"]), site["url"])
    for service in sorted(manifest["images"]):
        variables["VERIFY_SERVICE_" + upper_name(service)] = ("http://" + project + "-" + service + ":"
                                                              + str(service_port(manifest, service)))
    network = target.get("variables", {}).get("PLATFORM_NETWORK")
    container = project + "-verify-" + record["id"][:8]
    arguments = ["docker", "run", "--rm", "--name", container, "--pull", "never",
                 *(["--network", network] if network else []), "--security-opt", "no-new-privileges:true",
                 "--cap-drop", "ALL", "--memory", VERIFY_MEMORY, "--label", "wheelhouse.verify=" + project,
                 *[part for name in sorted(variables) for part in ("-e", name)],
                 "--entrypoint", command[0], manifest["images"][verification["service"]], *command[1:]]
    try:
        code, summary = docker.check(arguments, variables, timeout, container)
    except Exception as error:
        code, summary = -1, ""
        detail = "The check could not run (" + type(error).__name__ + ")"
    else:
        detail = ("Timed out after " + str(timeout) + "s" if code is None else
                  "Passed" if code == 0 else
                  "Docker could not start the check (exit " + str(code) + ")" if code in (125, 126, 127) else
                  "Exited " + str(code))
    result = {"status": "passed" if code == 0 else "failed", "service": verification["service"],
              "startedAt": started, "completedAt": now(), "seconds": int(clock() - begun), "exitCode": code,
              "detail": detail}
    if summary:
        result["summary"] = summary
    return result


# ---- Migrate step and rollback gate (migrations spec § 3.4) ----

class MigrateFailed(CommandFailed):
    """A migrate item or a rollback gate that stops a rollout before any service restarts; its message is operator-safe
    and names the item, never a connection value."""


INTERPOLATION = re.compile(r"\$\$|\$\{([A-Za-z_][A-Za-z0-9_]*)(?:(:?[-?])([^}]*))?\}|\$([A-Za-z_][A-Za-z0-9_]*)")
# What a result line may carry that the runner never keeps: a password pair, whatever casing.
PASSWORD_PAIR = re.compile(r"(?i)\b(password|pwd)\s*=\s*[^;\s]*")


def interpolate(value, variables):
    """Compose's interpolation of one string from `variables`: `${NAME}`, `$NAME`, `${NAME:-default}`,
    `${NAME-default}`, `${NAME:?message}`, `${NAME?message}` and `$$`. A required variable that is unset refuses."""
    if not isinstance(value, str):
        return value

    def replace(match):
        if match.group(0) == "$$":
            return "$"
        name = match.group(1) or match.group(4)
        operator, argument, present = match.group(2), match.group(3), variables.get(name)
        if operator == ":-" and not present:
            return argument
        if operator == "-" and present is None:
            return argument
        if operator in (":?", "?") and (present is None or (operator == ":?" and present == "")):
            raise Rejected("Compose needs " + name + ", which this target does not set")
        return present or ""
    return INTERPOLATION.sub(replace, value)


def migrate_key(target, manifest, service):
    """The migrate key (M8): the connection string of the target's own database (database.json in its settings folder)
    for an item whose service needs PostgreSQL, the runtime role in v1; None otherwise, and the image's sets connect
    through the service's own settings. Never logged, never an argument."""
    if "postgres" not in service_needs(manifest, service):
        return None
    folder = settings_folder(target)
    return target_database(folder) if folder.is_dir() else None


def migrate_mounts(compose, definition, project, variables, docker):
    """The service's mounts for its migrate container, as `-v` arguments: each bind (its settings file among them) with
    its source interpolated as Compose would, and each named volume that already exists. A volume not created yet holds
    nothing to migrate, and creating it here would leave it without Compose's labels."""
    declared = compose.get("volumes") or {}
    arguments = []
    for mount in definition.get("volumes") or []:
        if isinstance(mount, str):
            parts = interpolate(mount, variables).split(":")
            require(2 <= len(parts) <= 3, "Invalid service mount")
            source, path = parts[0], parts[1]
            kind = "bind" if source.startswith(("/", ".", "~")) else "volume"
            read_only = len(parts) == 3 and "ro" in parts[2].split(",")
        else:
            require(isinstance(mount, dict), "Invalid service mount")
            kind, source, path = mount.get("type", "volume"), interpolate(mount.get("source"), variables), mount.get("target")
            read_only = mount.get("read_only") is True
        require(kind in ("bind", "volume") and isinstance(source, str) and source and isinstance(path, str)
                and path.startswith("/") and not set(":,\n") & set(source + path), "Invalid service mount")
        if kind == "bind":
            require(Path(source).is_absolute(), "A migrate container mounts absolute bind sources only")
        else:
            options = declared.get(source) if isinstance(declared.get(source), dict) else {}
            full = options.get("name") or (source if options.get("external") else project + "_" + source)
            try:
                docker.execute(["docker", "volume", "inspect", "--format", "{{.Name}}", full], "docker volume inspect",
                               timeout=30)
            except CommandFailed:
                continue
            source = full
        arguments += ["-v", source + ":" + path + (":ro" if read_only else "")]
    return arguments


def migrate_arguments(target, compose, manifest, project, service, command, key, variables, docker, container):
    """The one-off migrate container (M8), modelled on the dev check: the service's release image, `--pull never`, the
    command as its entrypoint, the service's environment, mounts and memory, the platform network, every capability
    dropped and the image's own user unless the service names one. The migrate key travels by name only."""
    definition = compose["services"][service]
    declared = definition.get("environment") or {}
    if isinstance(declared, list):
        declared = dict(entry.split("=", 1) if "=" in entry else (entry, None) for entry in declared
                        if isinstance(entry, str))
    require(isinstance(declared, dict), "Invalid service environment")
    environment = {str(name): str(interpolate(str(value), variables)) for name, value in declared.items()
                   if value is not None}
    environment.update(service_environment(target["environment"],
                                           service in dotnet_services(manifest, compose["services"])))
    environment.pop(MIGRATE_KEY, None)
    network = target.get("variables", {}).get("PLATFORM_NETWORK")
    memory = definition.get("mem_limit")
    return ["docker", "run", "--rm", "--name", container, "--pull", "never",
            *(["--network", network] if network else []), "--security-opt", "no-new-privileges:true",
            "--cap-drop", "ALL", "--label", "wheelhouse.migrate=" + project,
            *(["--memory", str(memory)] if memory else []),
            *(["--user", str(definition["user"])] if definition.get("user") else []),
            *(["--init"] if definition.get("init") is True else []),
            *[part for name in sorted(environment) for part in ("-e", name + "=" + environment[name])],
            *(["-e", MIGRATE_KEY] if key else []),
            *migrate_mounts(compose, definition, project, variables, docker),
            "--entrypoint", command[0], manifest["images"][service], *command[1:]]


def short(value, limit):
    return value[:limit] if isinstance(value, str) and value else None


def count(value):
    return value if type(value) is int and value >= 0 else None


def scrubbed(value, secret=None):
    """One operator-safe line of a result's message: password pairs and the migrate key itself removed, cut short."""
    if not isinstance(value, str) or not value.strip():
        return None
    if secret:
        value = value.replace(secret, "***")
    return PASSWORD_PAIR.sub(lambda match: match.group(1) + "=***", " ".join(value.split()))[:300]


def migration_labels(value):
    return [{"label": item["label"][:100], "kind": item.get("kind") if item.get("kind") in MIGRATION_KINDS else None}
            for item in (value if isinstance(value, list) else [])[:200]
            if isinstance(item, dict) and isinstance(item.get("label"), str) and item["label"]]


def migrate_result(line, secret=None):
    """What the job keeps of a `migrate-result` line (M37): the verb, its outcome, a scrubbed message and per set its
    status, state, levels before and after, the labels it applied with their kind, the rollback floor, background
    migrations and lock wait. None when the line is missing or malformed: the exit code alone decides then."""
    if not isinstance(line, str) or not line.startswith(MIGRATE_RESULT):
        return None
    try:
        value = json.loads(line[len(MIGRATE_RESULT):])
    except ValueError:
        return None
    if not isinstance(value, dict) or not isinstance(value.get("sets"), list):
        return None
    sets = []
    for item in value["sets"][:50]:
        if not isinstance(item, dict) or not short(item.get("name"), 64):
            continue
        background = item.get("background") if isinstance(item.get("background"), list) else []
        sets.append({"name": short(item["name"], 64), "schema": short(item.get("schema"), 64),
                     "status": short(item.get("status"), 20), "state": short(item.get("state"), 30),
                     "levelBefore": count(item.get("levelBefore")), "levelAfter": count(item.get("levelAfter")),
                     "applied": migration_labels(item.get("applied")),
                     "rollbackFloor": short(item.get("rollbackFloor"), 64),
                     "background": [name[:100] for name in background if isinstance(name, str)][:20],
                     "lockWaitMs": count(item.get("lockWaitMs"))})
    result = {"verb": short(value.get("verb"), 20), "outcome": short(value.get("outcome"), 20), "sets": sets}
    message = scrubbed(value.get("message"), secret) or next(
        (scrubbed(item.get("message"), secret) for item in value["sets"] if isinstance(item, dict) and item.get("message")),
        None)
    if message:
        result["message"] = message
    return result


def migrate_status(code, run):
    """An item's status from its exit (M37): `0` applied (some set applied a migration) or current, `1` refused, `3`
    busy (after its retry), None (past its timeout) timed out, and anything else — `2`, a crash, Docker unable to start
    it — failed."""
    if code is None:
        return "timed out"
    if code == 0:
        return "applied" if run and any(item["applied"] for item in run["sets"]) else "current"
    return {1: "refused", 3: "busy"}.get(code, "failed")


def set_summary(item):
    """One set's line: `default 2 → 3: 1 applied (003-notes, expand)`, or `default at 3, current`."""
    applied = item["applied"]
    if applied:
        newest = applied[-1]
        label = newest["label"] + (", " + newest["kind"] if newest["kind"] else "")
        levels = (" " + str(item["levelBefore"] if item["levelBefore"] is not None else 0) + " → "
                  + str(item["levelAfter"]) if item["levelAfter"] is not None else "")
        return (item["name"] + levels + ": " + (plural(len(applied), "migration") + " applied, newest " + label
                                                 if len(applied) > 1 else "1 applied (" + label + ")"))
    level = item["levelAfter"] if item["levelAfter"] is not None else item["levelBefore"]
    return item["name"] + (" at " + str(level) if level is not None else "") + (", " + item["state"] if item["state"] else "")


def migrate_detail(entry, timeout, problem=None):
    """An item's operator-safe line, as its step reads it."""
    code, status = entry["exitCode"], entry["status"]
    sets = "; ".join(set_summary(item) for item in entry["sets"])
    took = " in " + str(entry["seconds"]) + "s"
    if problem:
        return "It could not run: " + problem
    if status == "timed out":
        return "Timed out after " + str(timeout) + "s; the container was stopped"
    if status in ("applied", "current"):
        return (sets or "Exit 0, no migrate-result line") + took
    if code in (125, 126, 127):
        return "Docker could not start it (exit " + str(code) + ")"
    head = ("Busy: the set's lock stayed held on both attempts" if status == "busy" else
            "Refused" if status == "refused" else "Failed" if code == 2 else "Crashed")
    message = entry.get("message") or sets
    return head + " (exit " + str(code) + ")" + (": " + message if message else "") + took


def run_migrate_item(target, manifest, compose, project, item, environment, docker, container, verb="migrate",
                     timeout=None, clock=time.monotonic):
    """Runs one migrate item in its one-off container: its command (`migrate`), or the same command's `check --json`
    for a rollback gate or a check now. An exit 3 retries once. Never raises for what the container did; returns the
    item's record entry — status, exit code, seconds, attempts, an operator-safe detail and each set its result line
    reported."""
    service = item["service"]
    command = item["command"] + (["check", "--json"] if verb == "check" else [])
    timeout = timeout or item["timeoutSeconds"]
    started, begun = now(), clock()
    code, line, attempts, problem, key = -1, None, 0, None, None
    try:
        key = migrate_key(target, manifest, service)
        arguments = migrate_arguments(target, compose, manifest, project, service, command, key, environment, docker,
                                      container)
        while True:
            attempts += 1
            code, line = docker.migrate(arguments, {MIGRATE_KEY: key} if key else {}, timeout, container)
            if code != 3 or attempts > 1:
                break
            time.sleep(MIGRATE_BUSY_PAUSE)
    except Rejected as error:
        code, problem = -1, reason(error)
    except Exception as error:
        code, problem = -1, type(error).__name__
    run = migrate_result(line, key)
    entry = {"service": service, "verb": verb, "release": manifest["release"],
             "status": "failed" if problem else migrate_status(code, run),
             "exitCode": code if type(code) is int and code >= 0 else None, "seconds": int(clock() - begun),
             "attempts": attempts, "startedAt": started, "completedAt": now(), "reported": run is not None,
             "migrateKey": key is not None, "sets": run["sets"] if run else []}
    if run and run.get("message"):
        entry["message"] = run["message"]
    entry["detail"] = migrate_detail(entry, timeout, problem)
    return entry


def known_migrations(base):
    """What the target last learned about each migration set (migrations.json): None before any migrate run."""
    try:
        value = read_json(Path(base) / MIGRATIONS_FILE)
    except (OSError, ValueError):
        return None
    return value if isinstance(value, dict) and isinstance(value.get("sets"), list) else None


def known_levels(base):
    """Each set's level the target's database last reported, by set name."""
    return {item["name"]: item["level"] for item in (known_migrations(base) or {}).get("sets", [])
            if isinstance(item, dict) and isinstance(item.get("name"), str) and type(item.get("level")) is int}


def remember_migrations(base, entries, source, job_id=None, snapshot=None):
    """Folds what migrate runs reported into the target's migrations.json (M36, M38): per set its level now, its state,
    rollback floor, the newest label it applied with its kind, and the release, job and source (`migrate`, `gate` or
    `check`) that last reported it; a set a run applied to also keeps that job's pre-deploy snapshot (`appliedBy`), the
    one a later rollback gate names. Returns the document, unchanged when no run reported a set."""
    known = known_migrations(base) or {"updatedAt": None, "sets": []}
    sets = {item["name"]: item for item in known["sets"] if isinstance(item, dict) and isinstance(item.get("name"), str)}
    at, changed = now(), False
    for entry in entries:
        for reported in entry.get("sets") or []:
            prior = sets.get(reported["name"], {})
            level = reported["levelAfter"] if reported["levelAfter"] is not None else reported["levelBefore"]
            item = {"newestLabel": None, "newestKind": None, **prior, "name": reported["name"],
                    "schema": reported["schema"] or prior.get("schema"), "service": entry["service"],
                    "level": level if level is not None else prior.get("level"), "state": reported["state"],
                    "rollbackFloor": reported["rollbackFloor"], "release": entry.get("release"), "job": job_id,
                    "source": source, "at": at}
            if reported["applied"]:
                newest = reported["applied"][-1]
                item.update(newestLabel=newest["label"], newestKind=newest["kind"],
                            appliedBy={"job": job_id, "snapshot": snapshot})
            sets[reported["name"]] = item
            changed = True
    if not changed:
        return known
    document = {"updatedAt": at, "sets": [sets[name] for name in sorted(sets)]}
    write_json(Path(base) / MIGRATIONS_FILE, document)
    return document


def snapshot_hint(path):
    """Where a refused return leaves the owner: the pre-deploy snapshot that holds the older schema, else fix forward."""
    return ("restore the pre-deploy snapshot " + path + " or deploy forward" if path else
            "no pre-deploy snapshot holds the older schema (dev takes none); deploy forward")


def running_manifest(base, previous):
    """The manifest of the release the target runs, from its kept release folder; None when unreadable."""
    identifier = (previous or {}).get("id")
    if not isinstance(identifier, str) or not re.fullmatch(r"[0-9a-f-]{36}", identifier):
        return None
    try:
        return validate_bundle(Path(base) / "releases" / identifier)
    except Exception:
        return None


def migrate_mode(base, previous, manifest):
    """How a rollout treats migrations (M13, M15): `migrate` runs every item forward on every deploy; `gate`, a return
    to a release whose sets sit below the level the target's database reported, runs that release's `check` instead;
    `trust`, leaving a release that migrates for one that declares no migrate item, falls back to rollbackCompatible;
    None when neither release migrates. Returns the mode and the sets the release is behind on."""
    known = known_levels(base)
    if release_migrate(manifest):
        behind = sorted(name for name, level in release_levels(manifest).items() if name in known and level < known[name])
        return ("gate" if behind else "migrate"), behind
    running = running_manifest(base, previous)
    if running is not None and release_migrate(running):
        return "trust", sorted(known)
    return None, []


def gate_release(base, target, manifest, compose, project, environment, docker, steps, record, job_id, hint,
                 source="gate"):
    """The rollback gate (M15): each migrate item's `check --json` from `manifest`'s images, one step each. Raises
    MigrateFailed, naming `hint`, at the first that does not exit 0; what each check reported joins the record and the
    target's migrations.json."""
    for item in release_migrate(manifest):
        with steps.run("Rollback gate " + item["service"]) as step:
            entry = run_migrate_item(target, manifest, compose, project, item, environment, docker,
                                     project + "-check-" + item["service"] + "-" + job_id[:8], verb="check")
            entry["purpose"] = source
            record.setdefault("migrations", []).append(entry)
            with contextlib.suppress(Exception):
                remember_migrations(base, [entry], "gate", job_id)
            if entry["exitCode"] != 0:
                raise MigrateFailed("Rollback gate: " + manifest["release"] + "'s " + item["service"]
                                    + " image may not run on this database (" + entry["detail"] + "); " + hint)
            step["detail"] = manifest["release"] + " may run on this database: " + entry["detail"]


def migrate_release(base, target, previous, manifest, compose, project, environment, docker, steps, record, job_id):
    """The migrate step of a rollout, after the pull and before the first wave (M13, M14): every item forward, in
    order, whatever changed; or, on a return to an older release, the rollback gate. Any failure raises MigrateFailed
    before a service restarts, so the previous release keeps serving."""
    mode, behind = migrate_mode(base, previous, manifest)
    if mode is None:
        return
    known = {item["name"]: item for item in (known_migrations(base) or {}).get("sets", []) if isinstance(item, dict)}
    applied_by = next((((known.get(name) or {}).get("appliedBy") or {}).get("snapshot") for name in behind
                       if ((known.get(name) or {}).get("appliedBy") or {}).get("snapshot")), None)
    if mode == "gate":
        gate_release(base, target, manifest, compose, project, environment, docker, steps, record, job_id,
                     snapshot_hint(applied_by))
        return
    if mode == "trust":
        running = running_manifest(base, previous)
        label = str((previous or {}).get("release") or "the running release")
        if not (running["rollbackCompatible"] is True or target.get("rollbackCompatible") is True):
            steps.add("Rollback gate", "failed", now(), manifest["release"] + " declares no migrate item")
            raise MigrateFailed("Rollback gate: " + manifest["release"] + " declares no migrate item to check, and "
                                + label + " does not declare earlier releases compatible; " + snapshot_hint(applied_by))
        warning = (manifest["release"] + " declares no migrate item to check; " + label
                   + "'s rollback compatibility lets it run")
        steps.add("Rollback gate", "warning", now(), warning)
        record.setdefault("warnings", []).append("Rollback gate: " + warning)
        return
    snapshot = (record.get("snapshot") or {}).get("path")
    for item in release_migrate(manifest):
        with steps.run("Migrate " + item["service"]) as step:
            entry = run_migrate_item(target, manifest, compose, project, item, environment, docker,
                                     project + "-migrate-" + item["service"] + "-" + job_id[:8])
            entry["purpose"] = "apply"
            record.setdefault("migrations", []).append(entry)
            with contextlib.suppress(Exception):
                remember_migrations(base, [entry], "migrate", job_id, snapshot)
            if entry["status"] not in ("applied", "current"):
                raise MigrateFailed("Migrate " + item["service"] + ": " + entry["detail"]
                                    + "; no service was restarted")
            step["detail"] = entry["detail"]


def restore_allowed(base, target, previous, record, steps, job_id, project, compatible, docker_factory):
    """Whether the automatic restore after a failed rollout may run (M15). A rollout that migrated nothing keeps the
    rule it had: the release's rollback compatibility. One that ran its migrate step asks the previous release's own
    `migrate check` first: every item must exit 0, else the restore is refused, naming the pre-deploy snapshot; a
    previous release with no migrate item falls back to rollbackCompatible with a warning. A gated return (a return to
    an older release that passed its rollback gate) checked and changed nothing in the database, and the release it
    leaves ran on that very database: it restores that release whatever the rollback compatibility says, through the
    same check when that release declares migrate."""
    entries = record.get("migrations") or []
    migrated = any(entry.get("verb") == "migrate" for entry in entries)
    gated = not migrated and any(entry.get("purpose") == "gate" for entry in entries)
    if not migrated and not gated:
        return compatible
    hint = snapshot_hint((record.get("snapshot") or {}).get("path"))
    prior = running_manifest(base, previous)
    label = str(previous.get("release") or "the previous release")
    if gated and prior is not None and not release_migrate(prior):
        steps.add("Rollback gate", "succeeded", now(), label + " ran on this database, and the return changed nothing "
                                                           "in it")
        return True
    if prior is None or not release_migrate(prior):
        if compatible:
            warning = label + " declares no migrate item to check; this release's rollback compatibility lets it return"
            steps.add("Rollback gate", "warning", now(), warning)
            record.setdefault("warnings", []).append("Rollback gate: " + warning)
        return compatible
    try:
        bundle = Path(base) / "releases" / previous["id"]
        prior_environment = environment_for(target, prior)
        gate_release(base, target, prior, read_json(bundle / "compose.json"), project, prior_environment,
                     docker_factory(target, prior_environment), steps, record, job_id, hint, source="restore")
    except Exception as error:
        record["reason"] = ((record.get("reason") or "The rollout failed") + "; the roll back was refused: "
                            + (reason(error) or "the rollback gate could not run"))[:600]
        return False
    return True


def migrate_check(target, docker_factory=None):
    """"Check now" (M36): `migrate check --json` from each migrate item of the release the target runs, under the
    deployment lock (refused while a rollout or a control runs). Writes nothing but the target's migrations.json; returns
    each item's result and what the target now knows about every set."""
    root, project = validate_target(target)
    base = root / project
    with deployment_lock(base):
        current_path = base / "current.json"
        require(current_path.is_file(), project + " runs no release yet; there is nothing to check")
        current = read_json(current_path)
        require(isinstance(current, dict) and isinstance(current.get("id"), str)
                and str(uuid.UUID(current["id"])) == current["id"], "The release this target runs is unreadable")
        bundle = base / "releases" / current["id"]
        manifest = validate_bundle(bundle)
        result = {"project": project, "release": manifest["release"], "checkedAt": now(), "items": []}
        items = release_migrate(manifest)
        if items:
            environment = control_environment(target, manifest, bundle)
            docker = (docker_factory or Docker)(target, environment)
            compose = read_json(bundle / "compose.json")
            run = uuid.uuid4().hex[:8]
            for item in items:
                entry = run_migrate_item(target, manifest, compose, project, item, environment, docker,
                                         project + "-check-" + item["service"] + "-" + run, verb="check",
                                         timeout=min(item["timeoutSeconds"], MIGRATE_CHECK_TIMEOUT))
                entry["purpose"] = "check"
                result["items"].append(entry)
            remember_migrations(base, result["items"], "check")
        result["migrations"] = known_migrations(base)
        return result


def plan_against(root, previous, compose, target, manifest, settings=None):
    """The rollout plan of `compose` against the release the target runs (`previous`, its current.json): that release's
    compose.json, the environment override it ran with, its settings fingerprints (`settings` are this release's), its
    interpolation inputs and its service versions. Without a readable previous release every service is added."""
    prior = prior_environment = prior_settings = None
    same_inputs = False
    identifier = (previous or {}).get("id")
    if isinstance(identifier, str) and re.fullmatch(r"[0-9a-f-]{36}", identifier):
        folder = root / "releases" / identifier
        with contextlib.suppress(OSError, ValueError):
            prior = read_json(folder / "compose.json")
        with contextlib.suppress(OSError, ValueError):
            prior_environment = read_json(folder / ENVIRONMENT_OVERRIDE)
        with contextlib.suppress(OSError, ValueError, AttributeError):
            prior_settings = read_json(folder / FINGERPRINTS).get("services")
        with contextlib.suppress(OSError, ValueError, AttributeError):
            same_inputs = read_json(root / "jobs" / (identifier + ".json")).get("interpolation") == \
                interpolation_inputs(target, manifest)
    return rollout_plan(compose, target["environment"], prior if isinstance(prior, dict) else None, prior_environment,
                        same_inputs, dotnet_services(manifest, compose["services"]), settings, prior_settings,
                        manifest.get("versions"), (previous or {}).get("versions"))


def settle_services(record, starting, done=None, rolled_back=False, kept=False):
    """Moves services on in a rollout's record, stamping `at` on each one it moves: `done` ones to updated, and with
    `kept` every unchanged one too, as the step that kept it running and checked it ends; after a failure (`done` and
    `rolled_back` unset) the ones `starting` to failed and every other pending one to not started; after a roll back
    every updated or failed one to rolled back."""
    at = now()
    for service in record.get("serviceRollout", {}).get("services", []):
        outcome = service["outcome"]
        if rolled_back:
            if outcome in ("updated", "failed"):
                service["outcome"] = "rolled_back"
        elif done is not None:
            if service["name"] in done and outcome == "pending":
                service["outcome"] = "updated"
            elif kept and outcome == "unchanged":
                service["at"] = at
        elif outcome == "pending":
            service["outcome"] = "failed" if starting and service["name"] in starting else "not_started"
        if service["outcome"] != outcome:
            service["at"] = at


def record_health(record, health):
    """Each service's health at the end of a rollout, from `health` by service; a service it leaves out is unknown."""
    for service in record.get("serviceRollout", {}).get("services", []):
        value = health.get(service["name"]) if isinstance(health, dict) else None
        service["health"] = value if value in HEALTH else "unknown"


def rollout_summary(rollout):
    """One line on what a rollout changed: `2 of 3 services changed; 1 kept running`."""
    services = rollout.get("services", [])
    changed = sum(1 for service in services if service["change"] != "unchanged")
    kept = len(services) - changed
    return (plural(changed, "service") + " changed" if kept == 0 else
            str(changed) + " of " + plural(len(services), "service") + " changed; " + str(kept) + " kept running") + (
        "; removed " + ", ".join(rollout["removed"]) if rollout.get("removed") else "")


def status(target, job_id):
    root, project = validate_target(target)
    require(str(uuid.UUID(job_id)) == job_id, "Invalid deployment id")
    return read_json(root / project / "jobs" / (job_id + ".json"))


def launch(bundle, target_path, actor, provided=None):
    """Queues a rollout and starts its worker detached. `provided` (the control plane's settings values and registry
    sign-in for this deploy) reaches the worker over a pipe only: it is never written to the target's disk."""
    target = read_json(target_path)
    manifest = validate_bundle(bundle)
    root, project = validate_target(target, manifest)
    if provided is not None:
        provided_values(provided)
        registry_login(provided)
        registry_unreadable(provided)
    # Refused here, before a job exists, so the control plane can tell the owner at once; the worker checks again.
    stopped = stopped_of(root / project)
    require(stopped is None, stopped_refusal(project, stopped))
    job_id = str(uuid.uuid4())
    jobs = root / project / "jobs"
    jobs.mkdir(parents=True, exist_ok=True, mode=0o700)
    write_json(jobs / (job_id + ".json"), {"id": job_id, "status": "queued", "actor": actor, "queuedAt": now()})
    # The target process owns the rollout across SSH/control-plane disconnects.
    worker = subprocess.Popen([sys.executable, str(Path(__file__).resolve()), "apply", "--bundle", str(Path(bundle).resolve()),
                               "--target", str(Path(target_path).resolve()), "--actor", actor, "--job", job_id,
                               *(["--stdin"] if provided is not None else [])],
                              stdin=subprocess.PIPE if provided is not None else subprocess.DEVNULL,
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True, close_fds=True)
    if provided is not None:
        with worker.stdin:
            worker.stdin.write(json.dumps(provided).encode())
    return {"id": job_id, "status": "queued"}


# ---- Service controls ----

def stopped_of(base):
    """What the owner stopped on a target, as its `stopped.json` says: `{environment, services, at, actor}`, the whole
    environment or the services named; None while nothing is. A marker that cannot be read counts as the whole
    environment, so a deploy never starts what may have been stopped."""
    path = Path(base) / STOPPED
    if not path.is_file():
        return None
    try:
        value = read_json(path)
    except (OSError, ValueError):
        value = None
    if not isinstance(value, dict):
        return {"environment": True, "services": [], "at": None, "actor": None}
    services = value.get("services") if isinstance(value.get("services"), list) else []
    return {"environment": value.get("environment") is not False,
            "services": sorted({name for name in services if isinstance(name, str) and SLUG.fullmatch(name)}),
            "at": value.get("at") if isinstance(value.get("at"), str) else None,
            "actor": value.get("actor") if isinstance(value.get("actor"), str) else None}


def stopped_refusal(project, stopped):
    """Why a deploy is refused while the owner keeps something stopped; it names what to start."""
    if stopped is None:
        return ""
    if stopped["environment"]:
        return project + " is stopped; start it before deploying"
    names = stopped["services"]
    return (", ".join(names) + (" is" if len(names) == 1 else " are") + " stopped on " + project
            + "; start " + ("it" if len(names) == 1 else "them") + " before deploying")


def write_stopped(base, stopped, actor):
    """Saves what stays stopped, or removes the marker when nothing does."""
    path = Path(base) / STOPPED
    if stopped is None or (not stopped["environment"] and not stopped["services"]):
        path.unlink(missing_ok=True)
        return None
    value = {"environment": stopped["environment"], "services": sorted(stopped["services"]), "at": now(), "actor": actor}
    write_json(path, value)
    return value


def control_label(operation, services):
    """A control's one-line name, as its step and history read it: `Stop management`, `Start the environment`."""
    return operation.capitalize() + " " + (", ".join(services) if services else "the environment")


def control_plan(base, operation, services):
    """Checks one control against the target as it is and returns the release it acts on (`current.json`), its bundle
    folder and manifest, and what stays stopped afterwards. A control needs a release to act on; a service must be one
    of the release's; the whole environment only stops and starts, a service also restarts; a stopped environment
    starts whole, and a stopped service starts rather than restarts."""
    require(operation in OPERATIONS, "Unknown control: " + str(operation))
    require(isinstance(services, list) and len(services) <= 1
            and all(isinstance(name, str) and SLUG.fullmatch(name) for name in services),
            "A control names one service, or none for the whole environment")
    require(operation != "restart" or services, "Restart one service at a time")
    current_path = base / "current.json"
    require(current_path.is_file(), base.name + " runs no release yet; there is nothing to stop or start")
    current = read_json(current_path)
    require(isinstance(current, dict) and isinstance(current.get("id"), str)
            and str(uuid.UUID(current["id"])) == current["id"], "The release this target runs is unreadable")
    bundle = base / "releases" / current["id"]
    manifest = validate_bundle(bundle)
    for name in services:
        require(name in manifest["images"], "The release " + manifest["release"] + " has no service " + name)
    stopped = stopped_of(base) or {"environment": False, "services": []}
    whole = stopped["environment"]
    if operation == "stop":
        require(not whole, base.name + " is already stopped")
        require(not set(services) & set(stopped["services"]), ", ".join(services) + " is already stopped")
        after = ({"environment": True, "services": []} if not services else
                 {"environment": False, "services": sorted(set(stopped["services"]) | set(services))})
    elif operation == "start":
        require(not (whole and services), base.name + " is stopped; start the environment")
        after = (None if not services else
                 {"environment": False, "services": sorted(set(stopped["services"]) - set(services))})
    else:
        require(not whole, base.name + " is stopped; start the environment")
        require(services[0] not in stopped["services"], services[0] + " is stopped; start it instead")
        after = stopped if stopped["services"] else None
    return current, bundle, manifest, after


def control_ready(base):
    """Refuses a control while a rollout or another control runs, or one stopped mid-way waits for reconciliation."""
    require(not lock_held(base), "A rollout or another control is running on " + base.name)
    active = read_json(base / "active.json") if (base / "active.json").is_file() else None
    require(not active or active.get("status") in TERMINAL, "Interrupted deployment requires reconciliation")
    require(not active or not (active.get("mutationStarted") and active["status"] in ("failed", "rollback_failed")),
            "Failed mutation requires reconciliation")


def control_launch(target_path, operation, services, actor):
    """Queues a control of the release a target runs and starts its worker detached, as a deploy's launch does; every
    rule is checked here first, so a refusal reaches the owner before any job exists."""
    target = read_json(target_path)
    root, project = validate_target(target)
    base = root / project
    require(isinstance(actor, str) and 0 < len(actor) <= 160 and "\n" not in actor, "Invalid actor")
    control_ready(base)
    current, bundle, manifest, _ = control_plan(base, operation, services)
    control_environment(target, manifest, bundle)
    job_id = str(uuid.uuid4())
    jobs = base / "jobs"
    jobs.mkdir(parents=True, exist_ok=True, mode=0o700)
    write_json(jobs / (job_id + ".json"), {"id": job_id, "status": "queued", "actor": actor, "queuedAt": now(),
                                           "operation": {"kind": operation, "services": services},
                                           "release": current.get("release")})
    subprocess.Popen([sys.executable, str(Path(__file__).resolve()), "control", "--target",
                      str(Path(target_path).resolve()), "--operation", operation, "--actor", actor,
                      "--job", job_id, *[argument for name in services for argument in ("--service", name)]],
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     start_new_session=True, close_fds=True)
    return {"id": job_id, "status": "queued", "release": current.get("release")}


def control(target, operation, services, actor, job_id, docker_factory=Docker):
    """Stops, starts or restarts the release a target runs, under the deployment lock, as a job in the target's journal
    and its durable intent (`active.json`) like a rollout. A stop marks what it stops before stopping it; a start
    clears its mark only once its services are healthy, so a control cut short leaves the target refusing deploys and a
    second start finishes it. The release, its containers, volumes and routes never change."""
    root, project = validate_target(target)
    base = root / project
    require(isinstance(actor, str) and 0 < len(actor) <= 160 and "\n" not in actor, "Invalid actor")
    require(str(uuid.UUID(job_id)) == job_id, "Invalid deployment id")
    with deployment_lock(base):
        active_path = base / "active.json"
        active = read_json(active_path) if active_path.exists() else None
        require(not active or active["status"] in TERMINAL, "Interrupted deployment requires reconciliation")
        require(not active or not (active.get("mutationStarted") and active["status"] in ("failed", "rollback_failed")),
                "Failed mutation requires reconciliation")
        current, bundle, manifest, after = control_plan(base, operation, services)
        record = {"id": job_id, "project": project, "operation": {"kind": operation, "services": services},
                  "release": current.get("release"), "sourceCommit": current.get("sourceCommit"),
                  "kind": current.get("kind", "release"), "actor": actor, "status": "running", "startedAt": now(),
                  "previous": current["id"]}

        def save():
            write_json(base / "jobs" / (job_id + ".json"), record)
            write_json(active_path, record)

        steps = Steps(record, save)
        docker = None
        names = services or sorted(manifest["images"])
        try:
            docker = docker_factory(target, control_environment(target, manifest, bundle))
            environment_override(bundle, target["environment"])
            if operation == "stop":
                # Marked first: a stop cut short still keeps deploys from starting what it reached.
                write_stopped(base, after, actor)
                with steps.run(control_label("stop", services)) as step:
                    docker.stop(bundle, services)
                    step["detail"] = plural(len(names), "service") + " stopped; containers, volumes and routes kept"
            elif operation == "start":
                with steps.run(control_label("start", services)) as step:
                    docker.start(bundle, services)
                    step["detail"] = plural(len(names), "service") + " healthy on " + str(current.get("release"))
                write_stopped(base, after, actor)
            else:
                with steps.run(control_label("restart", services)) as step:
                    docker.stop(bundle, services)
                    docker.start(bundle, services)
                    step["detail"] = services[0] + " healthy on " + str(current.get("release"))
            record["status"] = "succeeded"
        except (Exception, KeyboardInterrupt) as error:
            record["status"] = "failed"
            record["failure"] = type(error).__name__
            if reason(error):
                record["reason"] = reason(error)
            if docker is not None and operation != "stop" and isinstance(error, CommandFailed):
                with contextlib.suppress(Exception):
                    states = docker.unhealthy(bundle)
                    if states:
                        record["reason"] = record.get("reason", "The control failed") + "; not healthy: " + ", ".join(states)
        record["stopped"] = stopped_of(base)
        if docker is not None:
            # A service the owner keeps stopped has no health to report.
            with contextlib.suppress(Exception):
                record["health"] = {name: value for name, value in docker.service_health(bundle).items()
                                    if value in HEALTH and not (record["stopped"] and (
                                        record["stopped"]["environment"] or name in record["stopped"]["services"]))}
        record["completedAt"] = now()
        save()
        return record


def passed(target, release):
    """The newest rollout that ran `release` to success on this target, read from its journal, whoever submitted it (a
    control plane, CI or the host's own self-deploy): its id and completion, or None when none did."""
    root, project = validate_target(target)
    require(isinstance(release, str) and RELEASE.fullmatch(release), "Invalid release")
    newest = None
    for path in (root / project / "jobs").glob("*.json"):
        try:
            job = read_json(path)
        except (OSError, ValueError):
            continue
        # A control's record names the release it acted on, never one it ran to success.
        if (isinstance(job, dict) and job.get("release") == release and job.get("status") == "succeeded"
                and "operation" not in job and isinstance(job.get("completedAt"), str)
                and (newest is None or job["completedAt"] > newest["completedAt"])):
            newest = job
    return None if newest is None else {"id": newest.get("id"), "release": release, "completedAt": newest["completedAt"]}


def acknowledge(target, job_id):
    root, project = validate_target(target)
    with deployment_lock(root / project):
        active = read_json(root / project / "active.json")
        require(active["id"] == job_id and active["status"] in ("running", "failed", "rollback_failed"),
                "No matching interrupted deployment")
        require(active["status"] == "running" or active.get("mutationStarted"),
                "The deployment changed no containers; nothing to reconcile")
        # Acknowledgement does not assert that previous images still match the observed schema. A control cut short
        # changed no release, so the verified one stays; what it stopped stays marked until a start.
        current = root / project / "current.json"
        if current.exists() and "operation" not in active:
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
              "completedAt", "failure", "reason", "mutationStarted", "previous", "steps", "warnings", "snapshot",
              "verification", "operation", "stopped", "health", "migrations")
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
    return {"project": project, "condition": condition, "current": summary(current), "active": summary(active),
            "stopped": stopped_of(base), "migrations": known_migrations(base)}


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
    the sites it serves, one entry per path, with the URL and probe outcome the verified rollout recorded. A service's
    paths on one site share its published host and its probe."""
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
            origin = None
            if isinstance(url, str) and url.startswith(("http://", "https://")):
                parts = urllib.parse.urlsplit(url)
                origin = parts.scheme + "://" + parts.netloc
            probe = record.get("probe") if isinstance(record.get("probe"), dict) else {}
            for path in site["paths"]:
                service["sites"].append({"name": site["site"], "path": path, "port": site["port"],
                                         "exposure": site["exposure"],
                                         "url": origin + ("" if path == "/" else path) if origin else None,
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


def check(target, manifest=None, docker_factory=None, provided=None):
    """Reads the target without changing it; every detail is operator-safe text. On a rendering target, `provided`
    names the setting keys the control plane would send (`keys`); without it, required keys are left to the deploy."""
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
    if renders(target):
        keys = set((provided or {}).get("keys") or []) if isinstance(provided, dict) else set()
        require(all(isinstance(key, str) for key in keys), "Invalid provided keys")
        proxy = ingress_address(docker, target.get("variables", {}).get("PLATFORM_NETWORK")) if services else None
        for service, required in services.items():
            probe("Settings: " + service, lambda service=service, required=required:
                  render_readiness(target, manifest, project, service, required, keys if provided is not None else None,
                                   proxy))
    else:
        for service, required in services.items():
            probe("Settings: " + service, lambda service=service, required=required:
                  str(validate_settings(target, service, required).name) + " valid")
    current = state(target)
    checks.append({"name": "State", "ok": current["condition"] != "needs_reconciliation",
                   "detail": current["condition"].replace("_", " ")})
    if current["stopped"] is not None:
        checks.append({"name": "Stopped", "ok": False, "detail": stopped_refusal(project, current["stopped"])})
    return {"project": project, "ok": all(item["ok"] for item in checks), "checks": checks, "state": current}


def render_readiness(target, manifest, project, service, required, provided, proxy):
    """Whether a service's settings would render here: its host file (if named) is valid, and every required key has a
    source — the host file, Wheelhouse (`provided` key names; None when unknown), what the render derives, or the
    target's database. Reads files only; renders nothing."""
    covered, sources = set(), []
    host = target.get("settings", {}).get(service)
    if host and Path(host).exists():
        covered.update(flat_keys(private_settings(Path(host), service)))
        sources.append("its host file")
    if provided:
        covered.update(provided)
        sources.append("Wheelhouse")
    derived = set(derived_settings(target, project, manifest, service)) if manifest else {HOSTS}
    if proxy:
        derived.add(PROXIES)
    covered.update(derived)
    folder = settings_folder(target)
    if folder.exists():
        require(folder.is_dir() and not folder.is_symlink() and folder.stat().st_mode & 0o077 == 0,
                "The settings folder must be a private directory: " + SETTINGS_FOLDER + "/" + folder.name)
    needs = service_needs(manifest, service) if manifest else []
    database = target_database(folder) if folder.is_dir() and "postgres" in needs else None
    if database:
        covered.update(key for key in required if database_key(key, needs))
        sources.append("its database")
    if provided is not None:
        for key in required:
            require(key in covered or any(other.startswith(key + ":") for other in covered),
                    "Missing required setting: " + service + ":" + key)
    return "rendered at deploy from " + ", ".join(["derived keys", *sources]) + (
        "" if provided is not None else "; required keys are checked at deploy")


SIZE_UNITS = {"B": 1, "kB": 1000, "KB": 1000, "MB": 1000 ** 2, "GB": 1000 ** 3, "TB": 1000 ** 4,
              "KiB": 1024, "MiB": 1024 ** 2, "GiB": 1024 ** 3, "TiB": 1024 ** 4}


def size_bytes(text):
    match = re.fullmatch(r"\s*([0-9.]+)\s*([A-Za-z]+)\s*", text or "")
    return int(float(match.group(1)) * SIZE_UNITS[match.group(2)]) if match and match.group(2) in SIZE_UNITS else None


def percent(text):
    match = re.fullmatch(r"\s*([0-9.]+)%\s*", text or "")
    return float(match.group(1)) if match else None


def host_vitals(roots):
    """Load, memory, disks and uptime from /proc and statvfs; a field stays None where the host hides it. `roots` are
    the deployment roots whose filesystems the disks cover, beside Docker's data root."""
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
    # The deployment roots and Docker's data root, once per filesystem. Container mounts give one disk several
    # device ids, so identical capacity counts as the same disk too.
    for path in (*roots, Path("/var/lib/docker")):
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


def host(document):
    """Reads the host's own vitals without a target, so a server reads the same before and after it runs one. The
    document names the deployment roots whose disks to cover; every field is operator-safe."""
    roots = document.get("roots") if isinstance(document, dict) else None
    require(isinstance(roots, list) and 0 < len(roots) <= 32
            and all(isinstance(root, str) and Path(root).is_absolute() for root in roots), "Invalid host roots")
    try:
        return {"host": host_vitals([Path(root) for root in roots]), "problems": []}
    except (OSError, ValueError):
        return {"host": None, "problems": ["Host vitals were unreadable"]}


def vitals(target, docker_factory=None):
    """Reads container vitals without changing the target; every field is operator-safe. The host is read once per
    server (`host`), never through its targets."""
    _, project = validate_target(target)
    docker = (docker_factory or Docker)(target, base_environment(target))
    before = state(target)
    result = {"project": project, "containers": None, "problems": []}
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
    result.update(condition=current["condition"], release=(current["current"] or {}).get("release") if stable else None,
                  stopped=current.get("stopped"))
    if before != current:
        result["problems"].append("Deployment changed during observation; release attribution was withheld.")
    elif not stable:
        result["problems"].append("Target is not ready; release attribution was withheld.")
    return result


def watch(target, docker_factory=None, prober=probe_sites):
    """One look at a target after a rollout: its containers (state, health, restarts) and every site that answered at
    the deploy, requested again through the ingress. Read-only; the control plane judges regressions."""
    root, project = validate_target(target)
    reading = vitals(target, docker_factory)
    base = root / project
    current = read_json(base / "current.json") if (base / "current.json").is_file() else {}
    answered = [{key: site[key] for key in ("name", "service", "exposure", "url") if key in site}
                for site in current.get("sites") or []
                if isinstance(site, dict) and isinstance(site.get("probe"), dict) and site["probe"].get("ok")]
    try:
        probed = prober(target, answered, deadline_seconds=10) if answered else []
    except Exception:
        probed = None
    return {"project": project, "currentId": current.get("id"), "release": current.get("release"),
            "condition": reading["condition"], "containers": reading["containers"], "problems": reading["problems"],
            "stopped": reading.get("stopped"),
            "sites": None if probed is None else [{"name": site["name"], "service": site.get("service"),
                                                   "url": site.get("url"), "ok": site["probe"]["ok"],
                                                   "detail": site["probe"].get("detail")}
                                                  for site in probed]}


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


def read_provided(stream):
    """The document a deploy's stdin carries, bounded; it holds secret values, so it is parsed and kept in memory."""
    text = stream.read(4 * 1024 * 1024 + 1)
    require(len(text) <= 4 * 1024 * 1024, "The provided settings are too large")
    try:
        document = json.loads(text) if text.strip() else {}
    except ValueError:
        raise Rejected("The provided settings are not JSON") from None
    require(isinstance(document, dict), "The provided settings are not a JSON object")
    return document


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["validate", "apply", "launch", "status", "acknowledge", "state", "check",
                                           "host", "vitals", "topology", "logs", "watch", "prune", "passed",
                                           "control-launch", "control", "migrate-check"])
    parser.add_argument("--bundle")
    parser.add_argument("--target")
    parser.add_argument("--target-json")
    parser.add_argument("--release-json")
    parser.add_argument("--release", help="passed: the release label to look for in the target's journal")
    parser.add_argument("--actor", default="operator")
    parser.add_argument("--job")
    parser.add_argument("--service", help="logs: the service to read; control: the service, or none for the environment")
    parser.add_argument("--operation", choices=OPERATIONS, help="control: stop, start or restart")
    parser.add_argument("--tail", type=int, default=200)
    parser.add_argument("--stdin", action="store_true",
                        help="launch, apply: read this deploy's settings values and registry sign-in from stdin")
    parser.add_argument("--provided-json", help="check: the setting keys the control plane provides, base64 JSON")
    args = parser.parse_args()
    try:
        target = decode(args.target_json) if args.target_json else None
        provided = read_provided(sys.stdin) if args.stdin else None
        if args.action == "validate":
            result = validate_bundle(args.bundle)
        elif args.action == "launch":
            result = launch(args.bundle, args.target, args.actor, provided)
        elif args.action == "status":
            result = status(target or read_json(args.target), args.job)
        elif args.action == "acknowledge":
            result = acknowledge(target or read_json(args.target), args.job)
        elif args.action == "state":
            result = state(target or read_json(args.target))
        elif args.action == "topology":
            result = topology(target or read_json(args.target))
        elif args.action == "check":
            result = check(target or read_json(args.target), decode(args.release_json) if args.release_json else None,
                           provided=decode(args.provided_json) if args.provided_json else None)
        elif args.action == "host":
            result = host(target)
        elif args.action == "vitals":
            result = vitals(target or read_json(args.target))
        elif args.action == "logs":
            result = logs(target or read_json(args.target), args.service, args.tail)
        elif args.action == "watch":
            result = watch(target or read_json(args.target))
        elif args.action == "prune":
            result = prune(target or read_json(args.target))
        elif args.action == "passed":
            result = passed(target or read_json(args.target), args.release)
        elif args.action == "migrate-check":
            result = migrate_check(target or read_json(args.target))
        elif args.action == "control-launch":
            result = control_launch(args.target, args.operation, [args.service] if args.service else [], args.actor)
        elif args.action == "control":
            result = control(read_json(args.target), args.operation, [args.service] if args.service else [],
                             args.actor, args.job)
        else:
            result = apply(args.bundle, read_json(args.target), args.actor, args.job, provided=provided)
        print(json.dumps(result))
        return 1 if args.action in ("apply", "control") and result.get("status") != "succeeded" else 0
    except Exception as error:
        # A launched worker must not leave its own job queued if preflight rejected it.
        if args.action in ("apply", "control") and args.job:
            try:
                root, project = validate_target(read_json(args.target))
                refused = rejection(error)
                failed_step = {"name": getattr(error, "step", "Check target"), "status": "failed", "completedAt": now()}
                if refused.get("reason"):
                    failed_step["detail"] = refused["reason"]
                kept = {"operation": {"kind": args.operation, "services": [args.service] if args.service else []}} \
                    if args.action == "control" else {}
                write_json(root / project / "jobs" / (args.job + ".json"),
                           {"id": args.job, **kept, **refused, "completedAt": now(), "steps": [failed_step]})
            except Exception:
                pass
        print(json.dumps(rejection(error)), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
