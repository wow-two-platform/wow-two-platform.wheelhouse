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
import subprocess
import sys
import tarfile
import tempfile
import time
import uuid
import fleet
import artifacts
import inventory
from runner import (PROXIES, SLUG, TERMINAL, CommandFailed, Rejected, now, reason, rejection, require, read_json,
                    write_json, validate_bundle, validate_target, empty_topology)

RUNNER = Path(__file__).with_name("runner.py")
# A site address a rollout recorded: http(s), a host, an optional port and path.
SITE_URL = re.compile(r"https?://[A-Za-z0-9.-]+(:[0-9]{1,5})?(/[^\s]*)?")
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
                       "acceptsTestBuilds": config["acceptsTestBuilds"],
                       "needsConfirmation": config["needsConfirmation"],
                       "requiresTestPass": config["requiresTestPass"]})
    return result


def latest_sites(root):
    """Target ID -> the sites of the newest rollout this control plane saw succeed there; no target is contacted."""
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
        sites = []
        for site in observed.get("sites") or []:
            if (isinstance(site, dict) and SLUG.fullmatch(str(site.get("name", "")))
                    and SITE_URL.fullmatch(str(site.get("url", "")))):
                sites.append({"name": site["name"], "url": site["url"],
                              "exposure": "private" if site.get("exposure") == "private" else "public"})
        result[target_id] = sites
    return result


def test_pass(root, product, release):
    """Where this release succeeded on one of the product's test targets: local records first, then each test
    target's verified release. None when it never has."""
    tests = [binding.id for binding in fleet.active_targets()
             if binding.product == product and binding.environment is fleet.DeploymentEnvironment.TEST]
    for job in jobs(root, limit=None):
        if job.get("targetId") in tests and job.get("release") == release and job.get("status") == "succeeded":
            return {"targetId": job["targetId"], "at": job.get("completedAt") or job["submittedAt"]}
    for target_id in tests:
        try:
            current = target_state(root, target_id).get("current") or {}
        except (Rejected, CommandFailed, ValueError, OSError):
            continue  # an unreachable test target proves nothing
        if current.get("release") == release:
            return {"targetId": target_id, "at": current.get("completedAt")}
    return None


def admits(config, manifest):
    """Dev takes a build of any commit or branch; test also takes a `test` branch build; prod takes releases only."""
    if config["acceptsCandidates"] or manifest.get("kind", "release") == "release":
        return
    require(config.get("acceptsTestBuilds") and manifest.get("channel") == "test",
            "Only dev takes a build that is not a release; test also takes a `test` branch build")


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


def submit(root, target_id, bundle_id, actor, confirm=None, skip_test_pass=False):
    config = fleet.resolve_target(root, target_id)
    require(not config["needsConfirmation"] or confirm == target_id,
            "Type the target ID to deploy prod to the local server")
    require(not skip_test_pass or confirm == target_id, "Type the target ID to deploy without a test pass")
    bundle = artifacts.prepare(root, bundle_id, import_bundle)
    manifest = validate_bundle(bundle)
    admits(config, manifest)
    if config["requiresTestPass"] and not skip_test_pass:
        require(test_pass(root, config["target"]["product"], manifest["release"]),
                "Deploy this release to test first, or type the target ID to skip the test pass")
    target_root, _ = validate_target(config["target"], manifest)
    require(re.fullmatch(r"/[A-Za-z0-9/_-]+", str(target_root)), "SSH root requires a simple absolute path")
    require(isinstance(actor, str) and 0 < len(actor) <= 160 and "\n" not in actor, "Invalid actor")
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
    record = {"id": request_id, "targetId": target_id, "bundleId": bundle_id, "release": manifest["release"],
              "remote": remote, "actor": actor, "status": "submitting", "submittedAt": now(),
              "ssh": config["ssh"], "serverId": config["serverId"]}
    write_json(root / "jobs" / (request_id + ".json"), record)
    command = shlex.join(["python3", remote + "/runner.py", "launch", "--bundle", remote,
                          "--target", remote + "/target.json", "--actor", actor])
    remote_job = json.loads(ssh.run(command))
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
            remote = record["remote"]
            command = shlex.join(["python3", remote + "/runner.py", "status", "--target", remote + "/target.json",
                                  "--job", record["remoteJobId"]])
            result = json.loads(Ssh(ssh_config).run(command))
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


def jobs(root, limit=50):
    # Submission records plus the last observed target outcome; SSH details and remote paths stay local.
    result = []
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
        item = {"id": record["id"], "targetId": record.get("targetId"), "bundleId": record.get("bundleId"),
                "release": record.get("release") or observed.get("release") or record.get("bundleId"),
                "actor": record.get("actor"), "submittedAt": submitted,
                "status": observed.get("status") or record.get("status")}
        for key in ("reason", "failure", "startedAt", "completedAt", "mutationStarted", "sourceCommit", "warnings"):
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


def check(root, target_id, bundle_id=None):
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
    try:
        result = run_remote(ssh, "check", config["target"], release=manifest, timeout=90)
    except Rejected as error:
        checks.append({"name": "Target runner", "ok": False, "detail": str(error)})
        return {"targetId": target_id, "ok": False, "checks": checks}
    except CommandFailed as error:
        checks.append({"name": "SSH connection", "ok": False, "detail": str(error)})
        return {"targetId": target_id, "ok": False, "checks": checks}
    result["checks"] = checks + [{"name": "SSH connection", "ok": True,
                                  "detail": config["ssh"]["user"] + "@" + config["ssh"]["host"]}] + result["checks"]
    if manifest is not None and config.get("requiresTestPass"):
        passed = test_pass(root, config["target"]["product"], manifest["release"])
        result["checks"].append({"name": "Test pass", "ok": passed is not None,
                                 "detail": "succeeded on " + passed["targetId"] if passed
                                 else "not deployed to test yet; deploy it there first or skip by typing the target ID"})
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
    """Reads every target's (or one target's) host and containers in parallel; a failing target reports why."""
    bindings = [binding for binding in fleet.active_targets() if target_id in (None, binding.id)]
    require(target_id is None or bindings, "Target is not in the inventory")

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
        collected = list(pool.map(collect, bindings))
    return {"collectedAt": now(), "targets": collected}


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
    earlier = [job for job in records if before <= submitted_on(job) < first]
    return {"windowDays": days, "since": first.isoformat(), **current,
            # The window before, measured the same way, so each figure can show its trend; null without records.
            "previous": {"since": before.isoformat(), **measure(earlier)[0]} if earlier else None,
            "daily": [{"date": date, **values} for date, values in daily.items()],
            "targets": sorted(per_target.values(), key=lambda item: item["targetId"])}


def summary_of(record):
    return {key: record[key] for key in ("id", "release", "status", "completedAt", "reason") if key in record}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["import", "sites", "fixtures", "servers", "targets", "vaults", "releases", "template", "submit",
                                           "status", "jobs", "follow", "state", "check", "reconcile", "vitals", "stats", "topology",
                                           "branches", "commits", "build", "logs"])
    parser.add_argument("--root", required=True)
    parser.add_argument("--target")
    parser.add_argument("--bundle")
    parser.add_argument("--service")
    parser.add_argument("--tail", type=int, default=200, help="logs: lines to read, 1-1000")
    parser.add_argument("--actor", default="operator")
    parser.add_argument("--job")
    parser.add_argument("--archive")
    parser.add_argument("--days", type=int, default=30)
    parser.add_argument("--product")
    parser.add_argument("--branch")
    parser.add_argument("--commit")
    parser.add_argument("--confirm", help="submit: the target ID, typed, for prod on the local server")
    parser.add_argument("--skip-test-pass", action="store_true",
                        help="submit: deploy to prod without a test pass; needs --confirm with the target ID")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    try:
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
        elif args.action == "template":
            print(json.dumps(template(root, args.bundle, args.service), indent=2))
            return 0
        elif args.action == "submit":
            result = submit(root, args.target, args.bundle, args.actor, args.confirm, args.skip_test_pass)
        elif args.action == "branches":
            result = artifacts.branches(args.product)
        elif args.action == "commits":
            result = artifacts.commits(args.product, args.branch)
        elif args.action == "build":
            result = artifacts.request_build(args.product, args.commit)
        elif args.action == "jobs":
            result = jobs(root)
        elif args.action == "follow":
            result = follow(root)
        elif args.action == "state":
            result = target_state(root, args.target)
        elif args.action == "topology":
            result = target_topology(root, args.target)
        elif args.action == "check":
            result = check(root, args.target, args.bundle)
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
