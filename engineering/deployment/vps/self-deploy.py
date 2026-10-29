#!/usr/bin/env python3
"""Wheelhouse's break-glass deploy on its control host: deploy, roll back or read wheelhouse-dev and wheelhouse-prod when
no console answers, and the first console on a fresh or rebuilt host. Run it as wheelhouse-deploy, from a tailnet SSH
session or the OVH KVM console; never from CI. vps.md § Break-glass self-deploy.

  status   [--env dev|prod]                         what each console runs, its newest rollouts, whether one is running
  deploy   --env dev|prod --release vX.Y.Z           a published release, read anonymously from GitHub; prod takes only
           [--skip-dev-pass] [--reason TEXT]         a release that succeeded on wheelhouse-dev
  rollback --env dev|prod [--release vX.Y.Z]         an earlier release from releases/<job>/ on this host: no network

It uses the same runner as every console: the release's own runner.py, copied out of its console image, validates the
bundle, checks the target, takes the target's lock and launches the detached rollout, whose journal every console's
history reads. So it never races a console rollout, and the rollout survives this session. Prod asks for its ID typed
and a reason, which a local log keeps (<root>/self-deploy/log.jsonl) beside the job's actor `self-deploy:<login>`.
Only the Python standard library and the Docker CLI are needed."""
import argparse
import datetime
import fcntl
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import pwd
import re
import shutil
import subprocess
import sys
import tarfile
import time
from urllib.error import HTTPError, URLError
from urllib.parse import quote
from urllib.request import Request, urlopen

REPOSITORY = "wow-two-platform/wow-two-platform.wheelhouse"
ASSET = "wheelhouse-release.tar.gz"
# The only image repository a downloaded release may run; a bundle given by hand (the rig) is the operator's own.
IMAGE = "ghcr.io/" + REPOSITORY + "/console"
API = "https://api.github.com/repos/" + REPOSITORY
DOWNLOAD = "https://github.com/" + REPOSITORY + "/releases/download/"
ROOT = Path("/srv/wheelhouse")
ENVIRONMENTS = ("dev", "prod")
RUNNER_IN_IMAGE = "/app/runner/runner.py"
TAG = re.compile(r"v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)")
LOGIN = re.compile(r"[A-Za-z0-9._-]{1,40}")
MAX_ARCHIVE = 3 * 1024 * 1024
MAX_JSON = 1024 * 1024
TERMINAL = {"succeeded", "failed", "rolled_back", "rollback_failed", "interrupted", "rejected"}
KEEP_RUNS = 10
FOLLOW_SECONDS = 1800


class Refused(ValueError):
    """A refusal whose message names a rule, never a value from a host file or a response."""


def require(condition, message):
    if not condition:
        raise Refused(message)


def read_json(path):
    with open(path) as stream:
        return json.load(stream)


def target_path(root, environment, override=None):
    """The console's target as the kit wrote it: <root>/config/wheelhouse-<environment>.json (prepare.py)."""
    return Path(override) if override else Path(root) / "config" / ("wheelhouse-" + environment + ".json")


def load_target(path, environment):
    require(Path(path).is_file(), "No target file at " + str(path) + "; prepare.py writes it")
    try:
        target = read_json(path)
    except ValueError:
        raise Refused("The target file is not JSON: " + str(path)) from None
    require(isinstance(target, dict) and target.get("product") == "wheelhouse"
            and target.get("environment") == environment,
            "The target file is not wheelhouse-" + environment + "'s: " + str(path))
    return target


def project_of(target):
    return Path(target["root"]) / ("wheelhouse-" + target["environment"])


def jobs_of(project):
    """The target's journal, newest first; unreadable entries are skipped."""
    result = []
    for path in (project / "jobs").glob("*.json"):
        try:
            job = read_json(path)
        except (OSError, ValueError):
            continue
        if isinstance(job, dict) and isinstance(job.get("id"), str):
            result.append(job)
    return sorted(result, key=lambda job: str(job.get("completedAt") or job.get("startedAt") or job.get("queuedAt")
                                               or ""), reverse=True)


def lock_held(project):
    """True while a rollout holds the target's lock, as the runner reads it; never creates the lock file."""
    try:
        with (project / "lock").open("r") as stream:
            fcntl.flock(stream, fcntl.LOCK_SH | fcntl.LOCK_NB)
            fcntl.flock(stream, fcntl.LOCK_UN)
            return False
    except FileNotFoundError:
        return False
    except BlockingIOError:
        return True


def status_of(root, environment, override=None, limit=3):
    """One console's state from its files alone, so it reads even when Docker or the network is down."""
    path = target_path(root, environment, override)
    if not path.is_file():
        return {"environment": environment, "target": str(path), "present": False}
    target = load_target(path, environment)
    project = project_of(target)
    current = read_json(project / "current.json") if (project / "current.json").is_file() else None
    active = read_json(project / "active.json") if (project / "active.json").is_file() else None
    condition = "ready"
    if active and active.get("status") == "running":
        condition = "running" if lock_held(project) else "needs reconciliation"
    elif active and active.get("mutationStarted") and active.get("status") in ("failed", "rollback_failed"):
        condition = "needs reconciliation"
    keys = ("id", "release", "status", "actor", "startedAt", "completedAt", "reason")
    return {"environment": environment, "target": str(path), "present": True, "condition": condition,
            "current": None if current is None else {key: current.get(key) for key in ("id", "release", "sourceCommit")},
            "recent": [{key: job.get(key) for key in keys if job.get(key) is not None}
                       for job in jobs_of(project)[:limit]]}


def fetch(url, accept="application/vnd.github+json", limit=MAX_ARCHIVE, opener=urlopen):
    """An anonymous GET: Wheelhouse's repository and releases are public, so no credential ever reaches this host."""
    request = Request(url, headers={"Accept": accept, "User-Agent": "wheelhouse-self-deploy",
                                    "X-GitHub-Api-Version": "2022-11-28"})
    try:
        with opener(request, timeout=60) as response:
            payload = response.read(limit + 1)
    except HTTPError as error:
        raise Refused("GitHub answered " + str(error.code) + " for " + url) from None
    except (URLError, OSError):
        raise Refused("GitHub did not answer; rollback needs no network") from None
    require(len(payload) <= limit, "The download is larger than a release bundle")
    return payload


def download_release(tag, destination, fetch=fetch):
    """The published release's bundle, its asset digest verified, as release.json and compose.json in `destination`."""
    require(isinstance(tag, str) and TAG.fullmatch(tag), "Give a published release tag such as v0.5.20")
    try:
        release = json.loads(fetch(API + "/releases/tags/" + quote(tag, safe=""), limit=MAX_JSON))
    except ValueError:
        raise Refused("GitHub's release answer is not JSON") from None
    require(isinstance(release, dict) and release.get("tag_name") == tag and release.get("draft") is False
            and release.get("prerelease") is False, "Not a published Wheelhouse release: " + tag)
    assets = [asset for asset in release.get("assets") or [] if isinstance(asset, dict) and asset.get("name") == ASSET]
    require(len(assets) == 1 and re.fullmatch(r"sha256:[a-f0-9]{64}", str(assets[0].get("digest", ""))),
            "The release has no single verifiable " + ASSET)
    payload = fetch(DOWNLOAD + quote(tag, safe="") + "/" + ASSET, accept="application/octet-stream")
    require("sha256:" + hashlib.sha256(payload).hexdigest() == assets[0]["digest"], "The release asset changed")
    extract(payload, destination)


def extract(payload, destination):
    """Exactly release.json and compose.json, regular files, each at most 1 MiB; nothing else is written."""
    with tarfile.open(fileobj=io.BytesIO(payload), mode="r:gz") as package:
        members = package.getmembers()
        require(len(members) == 2 and {member.name for member in members} == {"release.json", "compose.json"}
                and all(member.isfile() and member.size <= MAX_JSON for member in members),
                "The archive must hold only release.json and compose.json")
        for member in members:
            with package.extractfile(member) as stream:
                (destination / member.name).write_bytes(stream.read())


def docker(*arguments, timeout=600):
    result = subprocess.run(["docker", *arguments], capture_output=True, text=True, timeout=timeout)
    require(result.returncode == 0, "docker " + arguments[0] + " failed (exit " + str(result.returncode) + ")")
    return result.stdout


def runner_from_image(image, destination, run=docker):
    """The release's own runner.py, copied out of its console image (pulled first when the host lacks it, under the
    deploy account's registry sign-in)."""
    try:
        run("image", "inspect", "--format", "{{.Id}}", image, timeout=60)
    except Refused:
        run("pull", "--quiet", image, timeout=1800)
    container = run("create", "--entrypoint", "true", image, timeout=120).strip()
    try:
        run("cp", container + ":" + RUNNER_IN_IMAGE, str(destination), timeout=120)
    finally:
        run("rm", "--force", container, timeout=120)
    require(destination.is_file(), "The image holds no runner at " + RUNNER_IN_IMAGE)
    return destination


def load_runner(path):
    spec = importlib.util.spec_from_file_location("wheelhouse_runner", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def new_run(root):
    """A private folder for one deploy: the bundle and the runner its detached worker reads. The ten newest stay."""
    base = Path(root) / "self-deploy" / "runs"
    base.mkdir(parents=True, exist_ok=True, mode=0o700)
    folder = base / (datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + os.urandom(3).hex())
    folder.mkdir(mode=0o700)
    for old in sorted((path for path in base.iterdir() if path.is_dir()), key=lambda path: path.name)[:-KEEP_RUNS]:
        shutil.rmtree(old, ignore_errors=True)
    return folder


def gated(folder, target):
    """The target as a console's inventory resolves Wheelhouse's own: rolled back when its ingress cannot reach the new
    console, and for any release, since Wheelhouse's migrations only expand (decision 65). Written beside the run.
    A return between releases that declare `migrate` passes the rollback gate, the older image's `migrate check`,
    whatever this says; it still decides a return to a release from before the step and the restore after a failed
    return, so it stays forced until neither console keeps such a release (backlog § Hosting)."""
    target = {**target, "probeGate": True, "rollbackCompatible": True}
    path = folder / "target.json"
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w") as stream:
        json.dump(target, stream, indent=2)
    return target, path


def actor():
    """The job's actor: self-deploy:<who signed in>, the administrator behind sudo when there is one."""
    login = os.environ.get("SUDO_USER") or os.environ.get("USER") or pwd.getpwuid(os.getuid()).pw_name
    return "self-deploy:" + (login if LOGIN.fullmatch(login or "") else "unknown")


def log(root, entry):
    path = Path(root) / "self-deploy" / "log.jsonl"
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_APPEND, 0o600)
    with os.fdopen(descriptor, "a") as stream:
        stream.write(json.dumps({"at": datetime.datetime.now(datetime.timezone.utc).isoformat(), **entry}) + "\n")


def confirm(environment, assume_yes, ask=input):
    """Prod is typed out, as in the console; dev and --yes go on."""
    if environment != "prod" or assume_yes:
        return
    require(ask("Type wheelhouse-prod to continue: ").strip() == "wheelhouse-prod", "Not confirmed; nothing changed")


def follow(runner, target, job_id, output, clock=time.monotonic, sleep=time.sleep, timeout=FOLLOW_SECONDS):
    """Prints each step as the rollout records it, until its record is final."""
    deadline, shown = clock() + timeout, set()
    while True:
        try:
            record = runner.status(target, job_id)
        except (OSError, ValueError):
            record = {"status": "queued"}
        for step in record.get("steps") or []:
            key = (step.get("name"), step.get("status"))
            if step.get("status") != "running" and key not in shown:
                shown.add(key)
                print("  " + str(step.get("status")).ljust(9) + " " + str(step.get("name"))
                      + (" — " + step["detail"] if step.get("detail") else ""), file=output)
        if record.get("status") in TERMINAL and record.get("completedAt"):
            return record
        if clock() > deadline:
            print("Still running; the rollout continues on its own. Read it with: self-deploy.py status", file=output)
            return record
        sleep(3)


def ready(runner, target):
    state = runner.state(target)
    require(state["condition"] != "running", "A rollout is running on wheelhouse-" + target["environment"]
            + "; wait for it, then read status")
    require(state["condition"] == "ready", "wheelhouse-" + target["environment"] + " needs reconciliation first"
            " (a console's Reconcile, or runner.py acknowledge); nothing changed")
    return state


def launch(runner, root, target, path, bundle, environment, release, reason, action, output, wait, extra=None):
    actor_name = actor()
    job = runner.launch(str(bundle), str(path), actor_name)
    log(root, {"action": action, "environment": environment, "release": release, "job": job["id"],
               "actor": actor_name, **({"reason": reason} if reason else {}), **(extra or {})})
    print("Launched " + job["id"] + ": " + release + " to wheelhouse-" + environment + " as " + actor_name, file=output)
    if not wait:
        return 0
    record = follow(runner, target, job["id"], output)
    print(json.dumps({key: record.get(key) for key in ("id", "status", "release", "reason", "warnings")
                      if record.get(key) is not None}), file=output)
    return 0 if record.get("status") == "succeeded" else 1


def deploy(arguments, output=sys.stdout, ask=input, fetch=fetch, run=docker):
    environment = arguments.env
    path = target_path(arguments.root, environment, arguments.target)
    target = load_target(path, environment)
    require(environment != "prod" or (arguments.reason or "").strip(), "Give a --reason to deploy prod")
    require(not arguments.skip_dev_pass or environment == "prod", "Only prod has a dev pass to skip")
    require(not arguments.skip_dev_pass or (arguments.reason or "").strip(), "Give a --reason to skip the dev pass")
    folder = new_run(arguments.root)
    target, path = gated(folder, target)
    bundle = folder / "bundle"
    bundle.mkdir(mode=0o700)
    if arguments.bundle:
        for name in ("release.json", "compose.json"):
            shutil.copyfile(Path(arguments.bundle) / name, bundle / name)
    else:
        download_release(arguments.release, bundle, fetch)
    manifest = read_json(bundle / "release.json")
    require(manifest.get("product") == "wheelhouse" and manifest.get("kind", "release") == "release",
            "The bundle is not a Wheelhouse release")
    if not arguments.bundle:
        require(manifest.get("release") == arguments.release, "The bundle names another release")
        require(set(manifest.get("images") or {}) == {"console"}
                and str(manifest["images"]["console"]).split("@")[0] == IMAGE, "The image is outside " + IMAGE)
    release = str(manifest.get("release"))
    runner = load_runner(Path(arguments.runner) if arguments.runner else
                         runner_from_image(str((manifest.get("images") or {}).get("console")), folder / "runner.py", run))
    manifest = runner.validate_bundle(bundle)
    runner.validate_target(target, manifest)
    state = ready(runner, target)
    running = state.get("current") or {}
    if running.get("release") == release and running.get("sourceCommit") == manifest["sourceCommit"]:
        print("wheelhouse-" + environment + " already runs " + release + "; nothing changed", file=output)
        return 0
    if environment == "prod" and not arguments.skip_dev_pass:
        dev = load_target(target_path(arguments.root, "dev"), "dev")
        require(runner.passed(dev, release), release + " never succeeded on wheelhouse-dev; deploy it there first,"
                " or pass --skip-dev-pass with a --reason")
    checked = runner.check(target, manifest)
    for item in checked["checks"]:
        print("  " + ("ok" if item["ok"] else "FAILED").ljust(9) + " " + item["name"] + " — " + item["detail"],
              file=output)
    require(checked["ok"], "The target check failed; nothing changed")
    print("Deploying " + release + " to wheelhouse-" + environment + " (runs " + str(running.get("release")) + ")",
          file=output)
    confirm(environment, arguments.yes, ask)
    return launch(runner, arguments.root, target, path, bundle, environment, release, arguments.reason, "deploy",
                  output, not arguments.no_wait, {"skipDevPass": True} if arguments.skip_dev_pass else None)


def rollback_source(project, current, release=None):
    """The newest earlier success to return to: of `release` when given, else of any release but the running one,
    whose bundle this host still keeps under releases/<job>/."""
    for job in jobs_of(project):
        if job.get("status") != "succeeded" or not job.get("completedAt"):
            continue
        if (job.get("release") != release) if release else (job.get("release") == (current or {}).get("release")):
            continue
        folder = project / "releases" / job["id"]
        if (folder / "release.json").is_file() and (folder / "compose.json").is_file():
            return job, folder
    raise Refused("No earlier release " + (release + " " if release else "") + "is kept on this host to roll back to")


def rollback(arguments, output=sys.stdout, ask=input, run=docker):
    environment = arguments.env
    path = target_path(arguments.root, environment, arguments.target)
    target = load_target(path, environment)
    require(environment != "prod" or (arguments.reason or "").strip(), "Give a --reason to roll prod back")
    project = project_of(target)
    current = read_json(project / "current.json") if (project / "current.json").is_file() else None
    require(current, "wheelhouse-" + environment + " runs no recorded release; deploy one instead")
    require(not arguments.release or arguments.release != current.get("release"),
            "wheelhouse-" + environment + " already runs " + str(arguments.release))
    job, bundle = rollback_source(project, current, arguments.release)
    release = str(job["release"])
    folder = new_run(arguments.root)
    target, path = gated(folder, target)
    images = read_json(bundle / "release.json").get("images") or {}
    runner = load_runner(Path(arguments.runner) if arguments.runner else
                         runner_from_image(str(images.get("console")), folder / "runner.py", run))
    manifest = runner.validate_bundle(bundle)
    runner.validate_target(target, manifest)
    ready(runner, target)
    print("Rolling wheelhouse-" + environment + " back from " + str(current.get("release")) + " to " + release
          + " (job " + job["id"] + ")", file=output)
    confirm(environment, arguments.yes, ask)
    return launch(runner, arguments.root, target, path, bundle, environment, release, arguments.reason, "rollback",
                  output, not arguments.no_wait, {"from": current.get("release"), "source": job["id"]})


def status(arguments, output=sys.stdout):
    environments = [arguments.env] if arguments.env else list(ENVIRONMENTS)
    readings = [status_of(arguments.root, name, arguments.target if arguments.env else None) for name in environments]
    if arguments.json:
        print(json.dumps({"kit": kit_version(), "consoles": readings}, indent=2), file=output)
        return 0
    print("self-deploy.py " + kit_version(), file=output)
    for reading in readings:
        if not reading["present"]:
            print("wheelhouse-" + reading["environment"] + ": no target at " + reading["target"], file=output)
            continue
        current = reading["current"] or {}
        print("wheelhouse-" + reading["environment"] + ": " + reading["condition"] + ", runs "
              + str(current.get("release")) + " (job " + str(current.get("id")) + ")", file=output)
        for job in reading["recent"]:
            print("  " + str(job.get("status")).ljust(15) + " " + str(job.get("release")).ljust(12) + " "
                  + str(job.get("completedAt") or job.get("startedAt") or "") + " " + str(job.get("actor") or ""),
                  file=output)
    return 0


def kit_version():
    """This script's own digest, as the kit version a host reports."""
    return "sha256:" + hashlib.sha256(Path(__file__).read_bytes()).hexdigest()[:12]


def parser():
    result = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    result.add_argument("action", choices=["status", "deploy", "rollback"])
    result.add_argument("--env", choices=ENVIRONMENTS)
    result.add_argument("--release", help="deploy: the published tag; rollback: an earlier release kept on this host")
    result.add_argument("--reason", help="why, kept in the local log; required for prod")
    result.add_argument("--skip-dev-pass", action="store_true", help="deploy prod: a release dev never ran")
    result.add_argument("--yes", action="store_true", help="skip the typed confirmation")
    result.add_argument("--no-wait", action="store_true", help="launch and return; read the outcome with status")
    result.add_argument("--json", action="store_true", help="status: machine-readable")
    result.add_argument("--root", type=Path, default=ROOT)
    result.add_argument("--target", help="the target file (default <root>/config/wheelhouse-<env>.json)")
    result.add_argument("--runner", help="a runner.py to use instead of the release image's")
    result.add_argument("--bundle", help="deploy: a bundle folder (release.json, compose.json) instead of a download")
    return result


def main(argv=None, output=sys.stdout):
    arguments = parser().parse_args(argv)
    try:
        require(os.geteuid() != 0, "Run as the deploy account: sudo -u wheelhouse-deploy python3 " + sys.argv[0]
                + " ...; files written as root would lock its rollouts out")
        if arguments.action == "status":
            return status(arguments, output)
        require(arguments.env, "Name the console: --env dev or --env prod")
        if arguments.action == "deploy":
            require(arguments.release or arguments.bundle, "Give --release vX.Y.Z")
            return deploy(arguments, output)
        return rollback(arguments, output)
    except (Refused, ValueError) as error:
        print("Refused: " + str(error), file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        print("Stopped; a launched rollout continues on its own. Read it with: self-deploy.py status", file=sys.stderr)
        return 130


if __name__ == "__main__":
    sys.exit(main())
