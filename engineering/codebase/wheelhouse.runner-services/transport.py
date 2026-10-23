#!/usr/bin/env python3
"""SSH adapter used by Wheelhouse and the operator CLI. Inventory and bundles are trusted local files."""
import argparse
import base64
import contextlib
from concurrent.futures import ThreadPoolExecutor
import datetime
import fcntl
import json
import math
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.parse
import uuid
import fleet
import artifacts
import catalog
import history
import inventory
import registry
import release_sets
from fleet import rehearsal
from runner import (PROXIES, SLUG, TERMINAL, CommandFailed, Rejected, now, reason, rejection, require, read_json,
                    write_json, validate_bundle, validate_target, empty_topology, provided_values, settings_declarations)

RUNNER = Path(__file__).with_name("runner.py")
# A site address a rollout recorded: http(s), a host, an optional port and path.
SITE_URL = re.compile(r"https?://[A-Za-z0-9.-]+(:[0-9]{1,5})?(/[^\s]*)?")
# A fleet rollout's identifier, as the control plane writes a GUID.
FLEET_ID = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
FOLLOW_SECONDS = 10
FOLLOW_MAX_BACKOFF = 300
FOLLOW_BATCH_SIZE = 2  # Each SSH read is bounded at 30 seconds; leave room inside the API's 110-second budget.

SSH_FAILURES = (("Host key verification failed", "SSH host key verification failed"),
                ("Permission denied", "SSH authentication failed"),
                ("Could not resolve hostname", "SSH host name did not resolve"),
                ("Connection timed out", "SSH connection timed out"),
                ("Connection refused", "SSH connection refused"))


def child(root, folder, identifier, suffix=""):
    require(SLUG.fullmatch(identifier), "Invalid identifier")
    path = (root / folder / (identifier + suffix)).resolve()
    require(path.is_relative_to((root / folder).resolve()), "Path escaped inventory")
    return path


class Ssh:
    def __init__(self, config):
        self.config = config
        require(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.-]*", config.get("host", "")), "Invalid SSH host")
        require(re.fullmatch(r"[a-z_][a-z0-9_-]*", config.get("user", "")), "Invalid SSH user")
        require(type(config.get("port", 22)) is int and 1 <= config.get("port", 22) <= 65535, "Invalid SSH port")
        for key in ("keyFile", "knownHostsFile"):
            require(Path(config.get(key, "")).is_absolute() and Path(config[key]).is_file(), "Missing SSH identity file")
        self.destination = config["user"] + "@" + config["host"]

    def options(self):
        return ["-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes",
                "-o", "IdentitiesOnly=yes", "-o", "ConnectTimeout=10",
                "-o", "UserKnownHostsFile=" + self.config["knownHostsFile"], "-i", self.config["keyFile"]]

    def run(self, command, stdin=None, timeout=30):
        return self.execute(["ssh", *self.options(), "-p", str(self.config.get("port", 22)),
                             self.destination, command], "SSH operation", stdin, timeout)

    def copy(self, files, destination):
        self.execute(["scp", *self.options(), "-P", str(self.config.get("port", 22)),
                      *map(str, files), self.destination + ":" + destination + "/"], "SSH transfer")

    @staticmethod
    def execute(arguments, step, stdin=None, timeout=30):
        try:
            result = subprocess.run(arguments, input=stdin, capture_output=True, text=True, timeout=timeout)
        except subprocess.TimeoutExpired:
            raise CommandFailed(step + " timed out") from None
        if result.returncode:
            raise ssh_failure(step, result)
        return result.stdout


def encode(value):
    return base64.b64encode(json.dumps(value).encode()).decode()


def run_remote(ssh, action, target, *arguments, release=None, timeout=30):
    # Streams this runner over stdin, so read-only calls leave no files on the target.
    command = ["python3", "-", action, "--target-json", encode(target), *arguments]
    if release is not None:
        command += ["--release-json", encode(release)]
    return json.loads(ssh.run(shlex.join(command), RUNNER.read_text(), timeout))


def ssh_failure(step, result):
    # Relay the target runner's own safe reason, or name a known client failure; never echo raw output.
    lines = result.stderr.strip().splitlines()
    try:
        remote = json.loads(lines[-1]) if lines else None
    except ValueError:
        remote = None
    if isinstance(remote, dict) and isinstance(remote.get("reason"), str):
        return (Rejected if remote.get("failure") == "Rejected" else CommandFailed)(remote["reason"][:300])
    known = next((message for marker, message in SSH_FAILURES if marker in result.stderr), step + " failed")
    return CommandFailed(known + " (exit " + str(result.returncode) + "); inspect the target privately")


def targets(root):
    result = []
    for binding in fleet.active_targets():
        config = fleet.resolve_target(root, binding.id)
        validate_target(config["target"])
        result.append({"id": binding.id, "product": config["target"]["product"],
                       "environment": config["target"]["environment"],
                       "serverId": config["serverId"], "provider": config["provider"], "host": config["ssh"]["host"],
                       "acceptsCandidates": config["acceptsCandidates"],
                       "needsConfirmation": config["needsConfirmation"],
                       "requiresDevPass": config["requiresDevPass"], "self": config.get("self", False)})
    return result


def latest_sites(root):
    """Target ID -> the sites of the newest rollout this control plane saw succeed there, one address per site; no
    target is contacted."""
    result = {}
    for job in jobs(root, limit=None):
        target_id = job.get("targetId")
        if target_id in result or job.get("status") != "succeeded":
            continue
        observed_path = root / "observed" / (job["id"] + ".json")
        try:
            observed = read_json(observed_path) if observed_path.is_file() else {}
        except (ValueError, OSError):
            continue
        sites = {}
        for site in observed.get("sites") or []:
            if (isinstance(site, dict) and SLUG.fullmatch(str(site.get("name", "")))
                    and SITE_URL.fullmatch(str(site.get("url", "")))):
                # One address per site: where services share a site by path, the one nearest its root.
                kept = sites.get(site["name"])
                path, kept_path = (urllib.parse.urlsplit(url).path for url in (site["url"], (kept or site)["url"]))
                if kept is None or len(path) < len(kept_path):
                    sites[site["name"]] = {"name": site["name"], "url": site["url"],
                                           "exposure": "private" if site.get("exposure") == "private" else "public"}
        result[target_id] = list(sites.values())
    return result


def dev_pass(root, product, release):
    """Where this release succeeded on one of the product's dev targets: local records first, then each dev target's
    verified release, then each dev target's journal, which also holds rollouts another submitter ran (Wheelhouse's own
    CI-era deploys, the host's self-deploy). None when it never has."""
    devs = [binding.id for binding in fleet.active_targets()
            if binding.product == product and binding.environment is fleet.DeploymentEnvironment.DEV]
    for job in jobs(root, limit=None):
        if job.get("targetId") in devs and job.get("release") == release and job.get("status") == "succeeded":
            return {"targetId": job["targetId"], "at": job.get("completedAt") or job["submittedAt"]}
    for target_id in devs:
        try:
            current = target_state(root, target_id).get("current") or {}
            if current.get("release") == release:
                return {"targetId": target_id, "at": current.get("completedAt")}
            found = journal_pass(root, target_id, release)
        except (Rejected, CommandFailed, ValueError, OSError):
            continue  # an unreachable dev target proves nothing
        if isinstance(found, dict) and found.get("release") == release:
            return {"targetId": target_id, "at": found.get("completedAt")}
    return None


def journal_pass(root, target_id, release):
    """A dev target's own journal entry for a release that succeeded there, over one SSH read; None when it has none."""
    config = fleet.resolve_target(root, target_id)
    return run_remote(Ssh(config["ssh"]), "passed", config["target"], "--release", release)


def admits(config, manifest):
    """Dev takes a build of any commit or branch; prod takes published releases only."""
    require(config["acceptsCandidates"] or manifest.get("kind", "release") == "release",
            "Only dev takes a build that is not a release")


def releases(root):
    return artifacts.available(root)


def import_bundle(root, archive, bundle_id):
    destination = child(root, "bundles", bundle_id)
    require(not destination.exists(), "Bundle ID already exists")
    expected = {"release.json", "compose.json"}
    with tempfile.TemporaryDirectory() as temporary:
        source = Path(temporary)
        with tarfile.open(archive, "r:gz") as package:
            members = package.getmembers()
            require(len(members) == 2 and {member.name for member in members} == expected,
                    "Archive must contain only release.json and compose.json")
            for member in members:
                require(member.isfile() and member.size <= 1024 * 1024, "Invalid archive member")
                with package.extractfile(member) as stream:
                    (source / member.name).write_bytes(stream.read())
        manifest = validate_bundle(source)
        destination.mkdir(parents=True, mode=0o700)
        # Publish the manifest last so discovery cannot observe an incomplete bundle.
        for name in ("compose.json", "release.json"):
            (destination / name).write_bytes((source / name).read_bytes())
    return {"id": bundle_id, "product": manifest["product"], "release": manifest["release"]}


def compose_set(root, product, parts):
    """The local server's release set: imported bundles of a product's repositories (`parts`, each `name=bundle` in
    repository order, the primary first) composed into one bundle as a product built from several repositories
    releases (decision 70). When the inventory names the product's repositories, a part is given for each that has
    released, as the catalog's sets take them: the primary one always, the others in repository order, and the rest
    named in `notYet`. Listed and deployed like any imported bundle; never on a deployed control plane."""
    require(rehearsal(), "Release sets compose from imported bundles only on the local server")
    require(isinstance(product, str) and SLUG.fullmatch(product), "Invalid product")
    named = [str(part).partition("=")[::2] for part in parts or []]
    require(named and all(SLUG.fullmatch(name) for name, _ in named), "Name each part name=bundle")
    found = next((item for item in catalog.products() if item.slug == product and item.components), None)
    if found is None:
        require(len(named) >= 2, "A release set takes a bundle of each repository: name=bundle, twice or more")
        repositories = {name: "local/" + name for name, _ in named}
    else:
        repositories = {catalog.short_name(product, repository): repository for repository, _ in found.repositories()}
        order = list(repositories)
        given = [name for name, _ in named]
        unknown = next((name for name in given if name not in repositories), None)
        require(unknown is None, str(unknown) + " is not one of the product's repositories (" + ", ".join(order) + ")")
        require(given[0] == order[0], repositories[order[0]] + " serves the product's front door and has no release "
                "yet; the product's release sets start with its first release")
        require(given == [name for name in order if name in given], "Give the parts in the product's repository order: "
                + ", ".join(order))
    pieces, identifiers = [], []
    for name, bundle_id in named:
        bundle = child(root, "bundles", bundle_id)
        manifest = validate_bundle(bundle)
        pieces.append((name, repositories[name], manifest, read_json(bundle / "compose.json")))
        identifiers.append(bundle_id)
    set_id = release_sets.identifier(product, identifiers)
    destination = child(root, "bundles", set_id)
    manifest, compose_bytes = release_sets.compose(product, set_id, pieces)
    with tempfile.TemporaryDirectory() as temporary:
        staged = Path(temporary) / "set"
        release_sets.write(staged, manifest, compose_bytes)
        validate_bundle(staged)
    # The same parts always make the same set; an older composition of them is written over.
    release_sets.write(destination, manifest, compose_bytes)
    return {"id": set_id, "product": product, "release": manifest["release"], "components": manifest["components"],
            "sites": manifest["sites"], "notYet": artifacts.not_yet(product, {name for name, _ in named})
            if found is not None else []}


def facts(root, bundle_id):
    """What a release's bundle declares that a deploy should weigh: whether a failed rollout can restore the previous
    images, and whether it carries schema migrations (null while the bundle does not say). Downloads and validates the
    bundle into the local cache the way a submission would; no target is contacted."""
    manifest = validate_bundle(artifacts.prepare(root, bundle_id, import_bundle))
    return {"id": bundle_id, "product": manifest["product"], "release": manifest["release"],
            "kind": manifest.get("kind", "release"), "sourceCommit": manifest["sourceCommit"],
            "rollbackCompatible": manifest["rollbackCompatible"],
            "migrations": manifest["migrations"] if type(manifest.get("migrations")) is bool else None}


def template(root, bundle_id, service=None):
    bundle = child(root, "bundles", bundle_id)
    if not bundle.exists():
        bundle = artifacts.prepare(root, bundle_id, import_bundle)
    result = {}
    for name, fields in validate_bundle(bundle)["requiredConfiguration"].items():
        # Nest each Section:Key path the way the service's JSON configuration binds it.
        settings = {}
        for field in fields:
            *sections, key = field.split(":")
            node = settings
            for section in sections:
                node = node.setdefault(section, {})
                require(isinstance(node, dict), "Conflicting configuration keys")
            require(not isinstance(node.get(key), dict), "Conflicting configuration keys")
            node[key] = [] if field == PROXIES else ""
        result[name] = settings
    if service is None:
        return result
    require(service in result, "Unknown release service")
    return result[service]


def deploy_reason(value):
    """The owner's one-line why for a deploy, kept on the job; None when none was given."""
    if value is None:
        return None
    require(isinstance(value, str) and 0 < len(value.strip()) <= 300 and not any(ord(c) < 32 or ord(c) == 127 for c in value),
            "A deploy reason is one line of 300 characters at most")
    return value.strip()


def settings_plan(root, target_id, bundle_id):
    """Every setting a release's services take on a target: required and optional keys, which read as secret, and what
    the render derives (with the value where it is known here). No target is contacted and no value is secret."""
    config = fleet.resolve_target(root, target_id)
    manifest = validate_bundle(artifacts.prepare(root, bundle_id, import_bundle))
    return {"targetId": target_id, "release": manifest["release"], **settings_declarations(config["target"], manifest)}


def submit(root, target_id, bundle_id, actor, confirm=None, skip_dev_pass=False, reason=None, fleet_id=None,
           provided=None):
    """`fleet_id` names the fleet rollout this submission belongs to; it is kept on the job and listed in history.
    `provided` is the control plane's settings layer for this deploy ({"values": {key: value}}: stored settings and
    vault secrets); it travels to the target on SSH stdin with the registry sign-in, and is never written here."""
    reason = deploy_reason(reason)
    values = provided_values(provided)
    require(fleet_id is None or (isinstance(fleet_id, str) and FLEET_ID.fullmatch(fleet_id)), "Invalid fleet id")
    config = fleet.resolve_target(root, target_id)
    if config.get("self"):
        # Wheelhouse's own targets: the console replaces itself or its sibling, so never as one step of a fleet, and
        # its prod only after the owner types the ID and says why.
        require(fleet_id is None, "Wheelhouse's own targets are never part of a fleet")
        require(config["target"]["environment"] != "prod" or reason,
                "Give a reason to promote Wheelhouse prod")
        require(not config["needsConfirmation"] or confirm == target_id,
                "Type " + target_id + " to promote Wheelhouse prod")
    require(not config["needsConfirmation"] or confirm == target_id,
            "Type the target ID to deploy prod to the local server")
    require(not skip_dev_pass or confirm == target_id, "Type the target ID to deploy without a dev pass")
    bundle = artifacts.prepare(root, bundle_id, import_bundle)
    manifest = validate_bundle(bundle)
    admits(config, manifest)
    if config["requiresDevPass"] and not skip_dev_pass:
        require(dev_pass(root, config["target"]["product"], manifest["release"]),
                "Deploy this release to dev first, or type the target ID to skip the dev pass")
    target_root, _ = validate_target(config["target"], manifest)
    require(re.fullmatch(r"/[A-Za-z0-9/_-]+", str(target_root)), "SSH root requires a simple absolute path")
    require(isinstance(actor, str) and 0 < len(actor) <= 160 and "\n" not in actor, "Invalid actor")
    # The pull's registry sign-in, picked and checked here: the products host keeps no registry token. An image nothing
    # can read goes with it, so the target's pull fails on it, if the host lacks it, saying what to place.
    login, attempt = registry.sign_in_for(manifest["images"].values())
    registry.note_packages(root, config["target"]["product"], manifest["images"].values(), attempt)
    unreadable = registry.unreadable_images(attempt)
    ssh = Ssh(config["ssh"])
    request_id = str(uuid.uuid4())
    remote = str(target_root / "incoming" / request_id)
    ssh.run("umask 077; mkdir -p " + shlex.quote(remote))
    runner_path = Path(__file__).with_name("runner.py")
    with tempfile.TemporaryDirectory() as temporary:
        target_path = Path(temporary) / "target.json"
        write_json(target_path, config["target"])
        ssh.copy([runner_path, bundle / "release.json", bundle / "compose.json", target_path], remote)
    # Record the submission before launching; remote state remains recoverable if the response is lost.
    record = {"id": request_id, "targetId": target_id, "environment": config["target"]["environment"],
              "bundleId": bundle_id, "release": manifest["release"], "remote": remote, "actor": actor, "status": "submitting", "submittedAt": now(),
              "ssh": config["ssh"], "serverId": config["serverId"]}
    if reason:
        record["deployReason"] = reason
    if fleet_id:
        record["fleetId"] = fleet_id
    if attempt:
        record["registry"] = attempt
    write_json(root / "jobs" / (request_id + ".json"), record)
    command = shlex.join(["python3", remote + "/runner.py", "launch", "--bundle", remote,
                          "--target", remote + "/target.json", "--actor", actor, "--stdin"])
    document = {"values": values, **({"registry": login} if login else {}),
                **({"unreadable": unreadable} if unreadable else {})}
    remote_job = json.loads(ssh.run(command, json.dumps(document)))
    record.update(status=remote_job["status"], remoteJobId=remote_job["id"])
    write_json(root / "jobs" / (request_id + ".json"), record)
    return {"id": request_id, "targetId": target_id, "bundleId": bundle_id, "status": record["status"]}


@contextlib.contextmanager
def observation_lock(path):
    """Never wait behind another observer, including a stalled SSH read. Kernel locks die with the process."""
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    with path.open("a") as stream:
        os.chmod(path, 0o600)
        try:
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            yield False
            return
        try:
            yield True
        finally:
            fcntl.flock(stream, fcntl.LOCK_UN)


def observed_outcome(root, record):
    """The target journal supplies outcomes; an explicit reconciliation receipt wins concurrent older reads."""
    try:
        observed = read_json(root / "observed" / (record["id"] + ".json"))
        if not isinstance(observed, dict):
            observed = {}
    except (ValueError, OSError):
        observed = {}
    remote_id = record.get("remoteJobId")
    if isinstance(remote_id, str):
        try:
            require(str(uuid.UUID(remote_id)) == remote_id, "Invalid deployment receipt")
            reconciled = read_json(root / "reconciled" / (remote_id + ".json"))
            if (isinstance(reconciled, dict) and reconciled.get("id") == remote_id
                    and reconciled.get("status") == "interrupted" and finalized(reconciled)):
                observed.update(reconciled, id=record["id"], remoteJobId=remote_id, targetId=record.get("targetId"))
        except (ValueError, OSError):
            pass
    return observed


def finalized(record):
    # The worker saves failed while rolling back, and succeeded while pruning, before its final save.
    return (isinstance(record.get("status"), str) and record["status"] in TERMINAL
            and instant(record.get("completedAt")) is not None)


def refresh_status(root, job_id, force=False):
    """Observe one target journal. None means another observer owns this job; never submit or reconcile it."""
    require(str(uuid.UUID(job_id)) == job_id, "Invalid deployment id")
    with observation_lock(root / "following" / (job_id + ".lock")) as acquired:
        if not acquired:
            return None
        record = read_json(root / "jobs" / (job_id + ".json"))
        observed = observed_outcome(root, record)
        if finalized(observed) and not force:
            return observed
        if "remoteJobId" not in record:
            result = {"id": job_id, "status": "unknown", "targetId": record["targetId"]}
        else:
            require(isinstance(record["remoteJobId"], str)
                    and str(uuid.UUID(record["remoteJobId"])) == record["remoteJobId"], "Invalid deployment receipt")
            ssh_config = record.get("ssh")
            if ssh_config is None:
                ssh_config = fleet.resolve_target(root, record["targetId"])["ssh"]
            result = remote_status(root, record, ssh_config)
            require(isinstance(result, dict) and result.get("id") == record["remoteJobId"],
                    "Unexpected deployment status identity")
            require(isinstance(result.get("status"), str) and result["status"] in TERMINAL | {"queued", "running", "unknown"},
                    "Unexpected deployment status")
            result.update(remoteJobId=result["id"], id=job_id, targetId=record["targetId"])
        # Reconciliation may have completed during the SSH read. Its durable target receipt wins.
        latest = observed_outcome(root, record)
        if finalized(latest) and (not finalized(result)
                or latest.get("reconciledBy")
                or (latest.get("status") == "interrupted" and result.get("status") != "interrupted")
                or (result.get("status") != "interrupted"
                    and instant(latest["completedAt"]) > instant(result["completedAt"]))):
            result = latest
        write_json(root / "observed" / (job_id + ".json"), result)
        return result


def remote_status(root, record, ssh_config):
    """One job's journal entry on its target. Streams this runner with the inventory's target, so the upload under
    incoming/<request>/ may have gone with the retention; a target the inventory no longer names is read through the
    runner it was submitted with."""
    try:
        target = fleet.resolve_target(root, record["targetId"])["target"]
    except (Rejected, KeyError):
        target = None
    if target is not None:
        return run_remote(Ssh(ssh_config), "status", target, "--job", record["remoteJobId"])
    remote = record["remote"]
    command = shlex.join(["python3", remote + "/runner.py", "status", "--target", remote + "/target.json",
                          "--job", record["remoteJobId"]])
    return json.loads(Ssh(ssh_config).run(command))


def status(root, job_id):
    # Explicit reads can discover operator recovery performed directly on the target, outside this control plane.
    result = refresh_status(root, job_id, force=True)
    if result is not None:
        return result
    # A browser never waits for the follower's SSH call. It receives the last complete observation.
    record = read_json(root / "jobs" / (job_id + ".json"))
    return observed_outcome(root, record) or {"id": job_id, "targetId": record["targetId"],
                                             "status": record.get("status", "unknown")}


def follow(root, clock=time.time):
    """A bounded, restartable observation pass over all local submissions; never execute a deployment."""
    result = {"checked": 0, "unavailable": 0, "busy": 0}
    with observation_lock(root / "following" / "pass.lock") as acquired:
        if not acquired:
            return {**result, "busy": 1}
        due = []
        for job in jobs(root, limit=None):
            if finalized(job):
                continue
            path = root / "following" / (job["id"] + ".json")
            try:
                record = read_json(root / "jobs" / (job["id"] + ".json"))
                has_receipt = isinstance(record, dict) and bool(record.get("remoteJobId"))
            except (ValueError, OSError):
                continue
            try:
                previous = read_json(path)
                next_attempt = float(previous["nextAttemptAt"])
                require(math.isfinite(next_attempt), "Invalid observation schedule")
                failures = min(30, max(0, int(previous.get("failures", 0))))
                if has_receipt and previous.get("hasReceipt") is False:
                    next_attempt, failures = 0, 0
            except (ValueError, KeyError, TypeError, OverflowError, OSError):
                next_attempt, failures = 0, 0
            if next_attempt <= clock():
                due.append((next_attempt, job["submittedAt"], job["id"], failures, has_receipt))
        for _, _, job_id, failures, has_receipt in sorted(due)[:FOLLOW_BATCH_SIZE]:
            path = root / "following" / (job_id + ".json")
            # Persist a short lease before I/O so repeated process crashes cannot hammer an unreachable target.
            write_json(path, {"nextAttemptAt": clock() + 90, "failures": failures, "hasReceipt": has_receipt})
            try:
                outcome = refresh_status(root, job_id)
                if outcome is None:
                    result["busy"] += 1
                else:
                    result["checked"] += 1
                if outcome is not None and outcome.get("status") == "unknown":
                    failures += 1
                    delay = min(FOLLOW_MAX_BACKOFF, FOLLOW_SECONDS * 2 ** min(failures - 1, 5))
                else:
                    failures, delay = 0, FOLLOW_SECONDS
            except (ValueError, OSError, KeyError, TypeError, CommandFailed):
                # Preserve the last target observation; a transport failure is not a deployment failure.
                result["unavailable"] += 1
                failures += 1
                delay = min(FOLLOW_MAX_BACKOFF, FOLLOW_SECONDS * 2 ** min(failures - 1, 5))
            write_json(path, {"nextAttemptAt": clock() + delay, "failures": failures, "hasReceipt": has_receipt})
    return result


WATCH_MINUTES = 10
WATCH_INTERVAL = 60
WATCH_BATCH_SIZE = 2  # each look is one SSH read bounded at 45 seconds, inside the API's 110-second budget
# `superseded` is the word records written before the rename keep; a new one says `replaced`.
WATCH_FINAL = {"passed", "degraded", "replaced", "superseded", "unverified"}


def watch_record(root, job_id):
    try:
        record = read_json(root / "watching" / (job_id + ".json"))
        return record if isinstance(record, dict) else None
    except (ValueError, OSError):
        return None


def regressions(reading, baseline):
    """What a look found wrong against the rollout's first look: a container that stopped, turned unhealthy or
    restarted since, or a site that answered at the deploy and no longer does."""
    found = []
    for container in reading.get("containers") or []:
        service = str(container.get("service") or "a container")
        if container.get("state") not in (None, "running"):
            found.append(service + " is " + str(container.get("state")))
        elif container.get("health") not in (None, "healthy", "starting"):
            found.append(service + " is " + str(container.get("health")))
        restarts, before = container.get("restarts"), (baseline or {}).get(service)
        if type(restarts) is int and type(before) is int and restarts > before:
            found.append(service + " restarted " + plural_of(restarts - before, "time") + " since the deploy")
    for site in reading.get("sites") or []:
        if not site.get("ok"):
            found.append("Site " + str(site.get("name")) + " stopped answering" +
                         (": " + str(site["detail"]) if site.get("detail") else ""))
    return found


def plural_of(count, noun):
    return str(count) + " " + noun + ("" if count == 1 else "s")


def watch(root, minutes=WATCH_MINUTES, clock=time.time):
    """A bounded pass over the watch windows: for `minutes` after each target's latest successful rollout, looks at its
    containers and sites once a minute (two looks per pass at most) and records `watching/<job>.json`: `passed` when
    the window ends clean, `degraded` with what regressed, `replaced` when another rollout replaced it, `unverified`
    when no look could read the target. Never deploys, retries or rolls back."""
    require(type(minutes) is int and 1 <= minutes <= 120, "A watch window is 1-120 minutes")
    result = {"checked": 0, "passed": 0, "degraded": 0, "busy": 0}
    with observation_lock(root / "watching" / "pass.lock") as acquired:
        if not acquired:
            return {**result, "busy": 1}
        now = clock()
        latest, budget = {}, WATCH_BATCH_SIZE
        for job in jobs(root, limit=None):
            if job.get("status") == "succeeded" and job.get("targetId") and job["targetId"] not in latest:
                latest[job["targetId"]] = job
        for target_id, job in latest.items():
            completed = instant(job.get("completedAt"))
            if completed is None:
                continue
            path = root / "watching" / (job["id"] + ".json")
            record = watch_record(root, job["id"])
            if record is None:
                if now - completed.timestamp() > minutes * 60:
                    continue  # finished before its window could be watched
                record = {"id": job["id"], "targetId": target_id, "release": job.get("release"), "status": "watching",
                          "startedAt": completed.isoformat(),
                          "until": datetime.datetime.fromtimestamp(completed.timestamp() + minutes * 60,
                                                                   datetime.timezone.utc).isoformat(),
                          "checks": 0, "unreadable": 0, "baseline": None, "findings": [], "nextCheckAt": 0}
            if record.get("status") in WATCH_FINAL:
                continue
            if now >= instant(record["until"]).timestamp():
                record["status"] = "passed" if record.get("checks") else "unverified"
                record["completedAt"] = datetime.datetime.fromtimestamp(now, datetime.timezone.utc).isoformat()
                result["passed"] += record["status"] == "passed"
                write_json(path, record)
                continue
            if float(record.get("nextCheckAt") or 0) > now or budget <= 0:
                continue
            budget -= 1
            record["nextCheckAt"] = now + WATCH_INTERVAL
            try:
                job_record = read_json(root / "jobs" / (job["id"] + ".json"))
                config = fleet.resolve_target(root, target_id)
                reading = run_remote(Ssh(config["ssh"]), "watch", config["target"], timeout=45)
            except (Rejected, CommandFailed, ValueError, OSError, KeyError):
                record["unreadable"] = int(record.get("unreadable") or 0) + 1
                write_json(path, record)
                continue
            result["checked"] += 1
            if reading.get("currentId") and reading["currentId"] != job_record.get("remoteJobId"):
                record["status"] = "replaced"
                write_json(path, record)
                continue
            found = regressions(reading, record.get("baseline"))
            if record.get("baseline") is None:
                record["baseline"] = {str(item.get("service")): item.get("restarts")
                                      for item in reading.get("containers") or [] if type(item.get("restarts")) is int}
            record["checks"] = int(record.get("checks") or 0) + 1
            record["lastCheckedAt"] = datetime.datetime.fromtimestamp(now, datetime.timezone.utc).isoformat()
            if found:
                record.update(status="degraded", findings=found[:10], degradedAt=record["lastCheckedAt"])
                result["degraded"] += 1
            write_json(path, record)
    return result


RETENTION_SECONDS = 24 * 60 * 60
RETENTION_BATCH = 2  # each target's prune is one SSH read bounded at 45 seconds, inside the API's 110-second budget
KEEP_BUNDLE_JOBS = 20
BUNDLE_GRACE_SECONDS = 7 * 24 * 60 * 60


def prune_bundles(root, now):
    """Removes downloaded bundles (bundles/<id>/ with a source.json) that none of each target's newest 20 submissions,
    no unfinished one and no target's latest two successes use, once a week old; GitHub serves them again. An imported
    bundle, the local server's own, stays. Returns how many went."""
    referenced, per_target, successes = set(), {}, {}
    for job in jobs(root, limit=None):
        target = job.get("targetId")
        per_target[target] = per_target.get(target, 0) + 1
        succeeded = job.get("status") == "succeeded"
        if succeeded:
            successes[target] = successes.get(target, 0) + 1
        if per_target[target] <= KEEP_BUNDLE_JOBS or not finalized(job) or (succeeded and successes[target] <= 2):
            referenced.add(job.get("bundleId"))
    removed = 0
    bundles = root / "bundles"
    for path in bundles.iterdir() if bundles.is_dir() else ():
        receipt = path / "source.json"
        if (path.is_symlink() or not path.is_dir() or path.name in referenced or not receipt.is_file()
                or now - receipt.stat().st_mtime < BUNDLE_GRACE_SECONDS):
            continue
        shutil.rmtree(path, ignore_errors=True)
        removed += 1
    return removed


def retention(root, clock=time.time):
    """The bounded retention pass the control plane runs often: prunes this host's bundles once a day and after each
    finished rollout, and runs each target's prune once a day, two targets a pass. Progress survives restarts in
    <root>/retention.json; a target that cannot be reached waits for the next day."""
    result = {"bundles": 0, "targets": [], "busy": 0}
    with observation_lock(root / "following" / "retention.lock") as acquired:
        if not acquired:
            return {**result, "busy": 1}
        now = clock()
        try:
            state = read_json(root / "retention.json")
            state = state if isinstance(state, dict) else {}
        except (ValueError, OSError):
            state = {}
        finished = max((instant(job.get("completedAt")).timestamp() for job in jobs(root, limit=None)
                        if instant(job.get("completedAt"))), default=0)
        last = float(state.get("bundlesAt") or 0)
        if now - last >= RETENTION_SECONDS or finished > last:
            result["bundles"] = prune_bundles(root, now)
            state["bundlesAt"] = now
        pending = [item for item in state.get("pending") or [] if isinstance(item, str)]
        if not pending and now - float(state.get("cycleAt") or 0) >= RETENTION_SECONDS:
            pending, state["cycleAt"] = [binding.id for binding in fleet.active_targets()], now
        for target_id in pending[:RETENTION_BATCH]:
            try:
                config = fleet.resolve_target(root, target_id)
                outcome = run_remote(Ssh(config["ssh"]), "prune", config["target"], timeout=45)
                result["targets"].append({"targetId": target_id, **{key: outcome.get(key) for key in ("releases", "incoming", "busy")
                                                                    if key in outcome}})
            except (Rejected, CommandFailed, ValueError, OSError, KeyError) as error:
                result["targets"].append({"targetId": target_id, "reason": reason(error) or "unreachable"})
        state["pending"] = pending[RETENTION_BATCH:]
        write_json(root / "retention.json", state)
    return result


def recorded_environment(record, observed, inventory):
    """The environment a job ran in: as submitted, else the target journal's project (<product>-<environment>), else
    the target's current one. A retired environment reads marked "(retired)"; an unknown one reads null."""
    value = record.get("environment")
    if not isinstance(value, str):
        project = observed.get("project")
        value = project.rsplit("-", 1)[-1] if isinstance(project, str) and "-" in project else None
    if not isinstance(value, str):
        value = inventory.get(record.get("targetId"))
    return fleet.environment_label(value) if isinstance(value, str) else None


def jobs(root, limit=50):
    # Submission records plus the last observed target outcome; SSH details and remote paths stay local.
    result = []
    inventory = {binding.id: binding.environment.value for binding in fleet.active_targets()}
    for path in (root / "jobs").glob("*.json") if (root / "jobs").is_dir() else ():
        try:
            record = read_json(path)
            require(isinstance(record, dict) and isinstance(record.get("id"), str), "Invalid job record")
            require(str(uuid.UUID(record["id"])) == record["id"] == path.stem, "Invalid job record")
        except (ValueError, KeyError, TypeError, OSError):
            continue
        observed = observed_outcome(root, record)
        submitted = record.get("submittedAt")
        if not isinstance(submitted, str) or not submitted:
            submitted = datetime.datetime.fromtimestamp(path.stat().st_mtime, datetime.timezone.utc).isoformat()
        item = {"id": record["id"], "targetId": record.get("targetId"),
                "environment": recorded_environment(record, observed, inventory), "bundleId": record.get("bundleId"),
                "release": record.get("release") or observed.get("release") or record.get("bundleId"),
                "actor": record.get("actor"), "deployReason": record.get("deployReason"), "submittedAt": submitted,
                "status": observed.get("status") or record.get("status")}
        if isinstance(record.get("fleetId"), str):
            item["fleetId"] = record["fleetId"]
        for key in ("reason", "failure", "startedAt", "completedAt", "mutationStarted", "sourceCommit", "warnings",
                    "verification"):
            if key in observed:
                item[key] = observed[key]
        result.append(item)
    return sorted(result, key=lambda item: item["submittedAt"], reverse=True)[:limit]


def target_state(root, target_id):
    config = fleet.resolve_target(root, target_id)
    validate_target(config["target"])
    result = run_remote(Ssh(config["ssh"]), "state", config["target"])
    result["targetId"] = target_id
    return result


def target_topology(root, target_id):
    config = fleet.resolve_target(root, target_id)
    validate_target(config["target"])
    try:
        result = run_remote(Ssh(config["ssh"]), "topology", config["target"])
    except (OSError, ValueError, CommandFailed):
        result = empty_topology("unavailable", "The target topology could not be collected.")
    result["targetId"] = target_id
    return result


def logs(root, target_id, service, tail=200):
    """One service's recent container output from its target; passed to the caller, never written here."""
    config = fleet.resolve_target(root, target_id)
    validate_target(config["target"])
    require(isinstance(service, str) and SLUG.fullmatch(service), "Invalid service")
    require(type(tail) is int and 1 <= tail <= 1000, "Tail must be 1-1000 lines")
    result = run_remote(Ssh(config["ssh"]), "logs", config["target"], "--service", service, "--tail", str(tail),
                        timeout=60)
    result["targetId"] = target_id
    return result


def registry_check(root, product, manifest):
    """Whether the control host can lend the release's pull a credential that reads each GHCR image, asked of GHCR now:
    a package nothing can read fails the check, naming it and what to place. The target may hold the image already, so
    the deploy itself fails only when it must pull one."""
    _, attempt = registry.sign_in_for(manifest["images"].values())
    registry.note_packages(root, product, manifest["images"].values(), attempt)
    return {"name": "Registry access", "ok": not registry.unreadable_images(attempt), "detail": registry.describe(attempt)}


def check(root, target_id, bundle_id=None, provided=None):
    """`provided` names the setting keys Wheelhouse would send ({"keys": [...]}), so the target can tell which required
    keys no layer sets."""
    config = fleet.resolve_target(root, target_id)
    manifest = validate_bundle(artifacts.prepare(root, bundle_id, import_bundle)) if bundle_id else None
    validate_target(config["target"], manifest)
    if manifest is not None:
        try:
            admits(config, manifest)
        except Rejected as error:
            return {"targetId": target_id, "ok": False, "checks": [{"name": "Release kind", "ok": False,
                                                                     "detail": str(error)}]}
    try:
        ssh = Ssh(config["ssh"])
    except Rejected as error:
        return {"targetId": target_id, "ok": False, "checks": [{"name": "SSH identity", "ok": False, "detail": str(error)}]}
    checks = [{"name": "SSH identity", "ok": True, "detail": "pinned key and known host present"}]
    if manifest is not None and registry.repositories(manifest["images"].values()):
        checks.append(registry_check(root, config["target"]["product"], manifest))
    try:
        keys = (provided or {}).get("keys") if isinstance(provided, dict) else None
        require(keys is None or (isinstance(keys, list) and all(isinstance(key, str) for key in keys)),
                "Invalid provided keys")
        extra = ["--provided-json", encode({"keys": sorted(set(keys))})] if keys is not None else []
        result = run_remote(ssh, "check", config["target"], *extra, release=manifest, timeout=90)
    except Rejected as error:
        checks.append({"name": "Target runner", "ok": False, "detail": str(error)})
        return {"targetId": target_id, "ok": False, "checks": checks}
    except CommandFailed as error:
        checks.append({"name": "SSH connection", "ok": False, "detail": str(error)})
        return {"targetId": target_id, "ok": False, "checks": checks}
    result["checks"] = checks + [{"name": "SSH connection", "ok": True,
                                  "detail": config["ssh"]["user"] + "@" + config["ssh"]["host"]}] + result["checks"]
    result["ok"] = result.get("ok", False) and all(item["ok"] for item in checks)
    if manifest is not None and config.get("requiresDevPass"):
        passed = dev_pass(root, config["target"]["product"], manifest["release"])
        result["checks"].append({"name": "Dev pass", "ok": passed is not None,
                                 "detail": "succeeded on " + passed["targetId"] if passed
                                 else "not deployed to dev yet; deploy it there first or skip by typing the target ID"})
        result["ok"] = result.get("ok", False) and passed is not None
    result["targetId"] = target_id
    return result


def reconcile(root, target_id, job_id, actor):
    require(str(uuid.UUID(job_id)) == job_id, "Invalid deployment id")
    require(isinstance(actor, str) and 0 < len(actor) <= 160 and "\n" not in actor, "Invalid actor")
    config = fleet.resolve_target(root, target_id)
    validate_target(config["target"])
    result = summary_of(run_remote(Ssh(config["ssh"]), "acknowledge", config["target"], "--job", job_id))
    result.update(targetId=target_id, reconciledBy=actor, reconciledAt=now())
    # The acknowledgement is an operator decision; keep who made it beside the submission history.
    write_json(root / "reconciled" / (job_id + ".json"), result)
    for path in (root / "jobs").glob("*.json") if (root / "jobs").is_dir() else ():
        record = read_json(path)
        if record.get("remoteJobId") == job_id:
            observed_path = root / "observed" / path.name
            observed = read_json(observed_path) if observed_path.is_file() else {}
            write_json(observed_path, {**observed, "status": result["status"], "reconciledBy": actor})
    return result


def vitals(root, target_id=None):
    """Reads every server's host once and every target's containers, in parallel; a failing read reports why. A server
    reads its own host, so one that runs no target yet reports its vitals too. With a target, reads that target and
    the server it runs on."""
    bindings = [binding for binding in fleet.active_targets() if target_id in (None, binding.id)]
    require(target_id is None or bindings, "Target is not in the inventory")
    hosts = [server for server in fleet.active_servers()
             if target_id is None or server.id == bindings[0].server_id]

    def read_host(server):
        try:
            config = fleet.resolve_server(root, server.id)
            result = run_remote(Ssh(config["ssh"]), "host", {"roots": config["roots"]}, timeout=60)
            return {"serverId": server.id, "ok": True, "host": result.get("host"), "problems": result.get("problems", [])}
        except (Rejected, CommandFailed) as error:
            return {"serverId": server.id, "ok": False, "reason": reason(error)}
        except (ValueError, AttributeError):
            return {"serverId": server.id, "ok": False, "reason": "Host vitals were unreadable"}

    def collect(binding):
        try:
            config = fleet.resolve_target(root, binding.id)
            validate_target(config["target"])
            result = run_remote(Ssh(config["ssh"]), "vitals", config["target"], timeout=60)
            return {**result, "targetId": binding.id, "serverId": binding.server_id, "ok": True}
        except (Rejected, CommandFailed) as error:
            return {"targetId": binding.id, "serverId": binding.server_id, "ok": False, "reason": reason(error)}
        except ValueError:
            return {"targetId": binding.id, "serverId": binding.server_id, "ok": False,
                    "reason": "Target vitals were unreadable"}

    with ThreadPoolExecutor(max_workers=8) as pool:
        servers = pool.map(read_host, hosts)
        collected = pool.map(collect, bindings)
        servers, collected = list(servers), list(collected)
    return {"collectedAt": now(), "servers": servers, "targets": collected}


FAILED = {"failed", "rolled_back", "rollback_failed", "interrupted"}


def instant(value):
    try:
        moment = datetime.datetime.fromisoformat(value)
    except (TypeError, ValueError):
        return None
    return moment if moment.tzinfo else moment.replace(tzinfo=datetime.timezone.utc)


def median(values):
    ordered = sorted(values)
    if not ordered:
        return None
    middle = len(ordered) // 2
    return ordered[middle] if len(ordered) % 2 else (ordered[middle - 1] + ordered[middle]) / 2


def outcome_of(status):
    return "succeeded" if status == "succeeded" else "failed" if status in FAILED else \
        "refused" if status == "rejected" else "pending"


def measure(window, daily=None):
    """Outcome counts, success rate and median rollout and recovery times of jobs in submission order, with each
    target's tally; `daily` gathers finished outcomes by UTC day when given."""
    counts = {"succeeded": 0, "failed": 0, "refused": 0, "pending": 0}
    rollouts, recoveries, per_target = [], [], {}
    for job in window:
        status = job.get("status")
        outcome = outcome_of(status)
        counts[outcome] += 1
        day = (daily or {}).get(instant(job["submittedAt"]).astimezone(datetime.timezone.utc).date().isoformat())
        if day is not None and outcome != "pending":
            day[outcome] += 1
        began, ended = instant(job.get("startedAt")) or instant(job["submittedAt"]), instant(job.get("completedAt"))
        if outcome in ("succeeded", "failed") and ended and ended >= began:
            rollouts.append((ended - began).total_seconds())
        key = job.get("targetId") or "unknown"
        target = per_target.setdefault(key, {"targetId": key, "deploys": 0, "succeeded": 0, "failed": 0,
                                             "failingSince": None})
        target["deploys"] += 1
        target.update(lastStatus=status, lastRelease=job.get("release"), lastDeployAt=job["submittedAt"])
        if outcome == "succeeded":
            target["succeeded"] += 1
            since = instant(target["failingSince"])
            if since and ended and ended >= since:
                recoveries.append((ended - since).total_seconds())
            target["failingSince"] = None
        elif outcome == "failed":
            target["failed"] += 1
            target["failingSince"] = target["failingSince"] or job.get("completedAt") or job["submittedAt"]
    finished = counts["succeeded"] + counts["failed"]
    return {"deploys": len(window), **counts,
            "successRate": counts["succeeded"] / finished if finished else None,
            "medianRolloutSeconds": median(rollouts), "medianRecoverySeconds": median(recoveries)}, per_target


def stats(root, days=30, moment=None):
    """Deployment metrics over the last `days` UTC calendar days from local submission records, beside the same
    measures for the `days` before them; no SSH."""
    require(type(days) is int and 1 <= days <= 90, "Window must be 1-90 days")
    today = (moment or datetime.datetime.now(datetime.timezone.utc)).astimezone(datetime.timezone.utc).date()
    first = today - datetime.timedelta(days=days - 1)
    before = first - datetime.timedelta(days=days)
    daily = {(first + datetime.timedelta(days=offset)).isoformat(): {"succeeded": 0, "failed": 0, "refused": 0}
             for offset in range(days)}

    def submitted_on(job):
        return instant(job["submittedAt"]).astimezone(datetime.timezone.utc).date()

    records = sorted((job for job in jobs(root, limit=None) if instant(job["submittedAt"])),
                     key=lambda job: instant(job["submittedAt"]))
    current, per_target = measure([job for job in records if submitted_on(job) >= first], daily)
    listed = {binding.id: index for index, binding in enumerate(fleet.active_targets())}
    earlier = [job for job in records if before <= submitted_on(job) < first]
    return {"windowDays": days, "since": first.isoformat(), **current,
            # The window before, measured the same way, so each figure can show its trend; null without records.
            "previous": {"since": before.isoformat(), **measure(earlier)[0]} if earlier else None,
            "daily": [{"date": date, **values} for date, values in daily.items()],
            # Inventory targets in their listing order; a removed target's history follows by ID.
            "targets": sorted(per_target.values(), key=lambda item: (listed.get(item["targetId"], len(listed)),
                                                                    item["targetId"]))}


def summary_of(record):
    return {key: record[key] for key in ("id", "release", "status", "completedAt", "reason") if key in record}


def read_stdin(stream):
    """A JSON document from the control plane's stdin, bounded; it may hold secret values, so it stays in memory."""
    text = stream.read(4 * 1024 * 1024 + 1)
    require(len(text) <= 4 * 1024 * 1024, "The provided document is too large")
    try:
        document = json.loads(text) if text.strip() else {}
    except ValueError:
        raise Rejected("The provided document is not JSON") from None
    require(isinstance(document, dict), "The provided document is not a JSON object")
    return document


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["import", "sites", "fixtures", "servers", "targets", "vaults", "releases", "template", "submit",
                                           "status", "jobs", "follow", "state", "check", "reconcile", "vitals", "stats", "topology",
                                           "branches", "commits", "build", "logs", "history", "observation",
                                           "latest", "facts", "changelog", "watch", "builds", "retention", "usage",
                                           "settings", "registry", "compose"])
    parser.add_argument("--root", required=True)
    parser.add_argument("--target")
    parser.add_argument("--bundle")
    parser.add_argument("--service")
    parser.add_argument("--tail", type=int, default=200, help="logs: lines to read, 1-1000")
    parser.add_argument("--actor", default="operator")
    parser.add_argument("--job")
    parser.add_argument("--archive")
    parser.add_argument("--days", type=int, default=30)
    parser.add_argument("--minutes", type=int, default=WATCH_MINUTES, help="watch: how long a rollout is watched")
    parser.add_argument("--product")
    parser.add_argument("--branch")
    parser.add_argument("--commit")
    parser.add_argument("--repository", help="branches, commits, build: one of the product's repositories (owner/name)")
    parser.add_argument("--part", action="append", default=[],
                        help="compose: name=bundle, one per repository of the product that has released, in order (the local server only)")
    parser.add_argument("--confirm", help="submit: the target ID, typed, for prod on the local server")
    parser.add_argument("--reason", help="submit: the owner's one-line why, kept on the job")
    parser.add_argument("--fleet", help="submit: the fleet rollout the submission belongs to, kept on the job")
    parser.add_argument("--from", dest="start", help="changelog: the release the target runs now")
    parser.add_argument("--to", dest="end", help="changelog: the release to promote")
    parser.add_argument("--skip-dev-pass", action="store_true",
                        help="submit: deploy to prod without a dev pass; needs --confirm with the target ID")
    parser.add_argument("--stdin", action="store_true",
                        help="submit: the settings values ({\"values\": …}) on stdin; check: the provided keys ({\"keys\": …})")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    try:
        provided = read_stdin(sys.stdin) if args.stdin else None
        inventory.install(root)
        if args.action == "import":
            result = import_bundle(root, args.archive, args.bundle)
        elif args.action == "sites":
            result = latest_sites(root)
        elif args.action == "fixtures":
            result = inventory.fixtures()
        elif args.action == "servers":
            result = fleet.servers()
        elif args.action == "targets":
            result = targets(root)
        elif args.action == "vaults":
            result = fleet.vaults()
        elif args.action == "releases":
            result = releases(root)
        elif args.action == "latest":
            result = artifacts.latest()
        elif args.action == "facts":
            result = facts(root, args.bundle)
        elif args.action == "watch":
            result = watch(root, args.minutes)
        elif args.action == "builds":
            result = artifacts.builds(root)
        elif args.action == "retention":
            result = retention(root)
        elif args.action == "usage":
            result = artifacts.usage(root, args.product)
        elif args.action == "changelog":
            result = artifacts.changelog(args.product, args.start, args.end, root)
        elif args.action == "compose":
            result = compose_set(root, args.product, args.part)
        elif args.action == "template":
            print(json.dumps(template(root, args.bundle, args.service), indent=2))
            return 0
        elif args.action == "submit":
            result = submit(root, args.target, args.bundle, args.actor, args.confirm, args.skip_dev_pass, args.reason,
                            args.fleet, provided)
        elif args.action == "settings":
            result = settings_plan(root, args.target, args.bundle)
        elif args.action == "registry":
            result = registry.probe(root, args.product)
        elif args.action == "branches":
            result = artifacts.branches(args.product, args.repository)
        elif args.action == "commits":
            result = artifacts.commits(args.product, args.branch, args.repository)
        elif args.action == "build":
            result = artifacts.request_build(args.product, args.commit, args.repository)
        elif args.action == "jobs":
            result = jobs(root)
        elif args.action == "history":
            result = history.records(root, jobs(root, limit=None))
        elif args.action == "observation":
            result = history.observation(root, args.job, observed_outcome)
        elif args.action == "follow":
            result = follow(root)
        elif args.action == "state":
            result = target_state(root, args.target)
        elif args.action == "topology":
            result = target_topology(root, args.target)
        elif args.action == "check":
            result = check(root, args.target, args.bundle, provided)
        elif args.action == "reconcile":
            result = reconcile(root, args.target, args.job, args.actor)
        elif args.action == "vitals":
            result = vitals(root, args.target)
        elif args.action == "stats":
            result = stats(root, args.days)
        elif args.action == "logs":
            result = logs(root, args.target, args.service, args.tail)
        else:
            result = status(root, args.job)
        print(json.dumps(result))
        return 0
    except Exception as error:
        print(json.dumps(rejection(error)), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
