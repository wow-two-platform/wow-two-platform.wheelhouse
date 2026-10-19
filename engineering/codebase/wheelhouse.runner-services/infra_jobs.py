"""Infrastructure jobs: a server's enrollment and a product's placement on a server. Each is a journal under
<root>/infra/ with live steps, written as the job moves, the way a deployment's record is; the console reads it.

A job runs as its own detached process holding its server's lock, so the console's request returns at once and two
jobs never act on one server together. A journal left `running` by a process that no longer holds the lock (the
console restarted) reads `interrupted`; every step converges, so a rerun is safe."""
from __future__ import annotations
import contextlib
import datetime
import fcntl
import os
from pathlib import Path
import subprocess
import sys
import uuid
from runner import SLUG, CommandFailed, Rejected, now, read_json, reason, require, write_json

INFRA = "infra"
FINISHED = {"succeeded", "failed"}


def folder(root, name):
    path = Path(root) / INFRA / name
    path.mkdir(parents=True, exist_ok=True, mode=0o700)
    return path


def lock_path(root, server):
    require(SLUG.fullmatch(server or ""), "Invalid server")
    return folder(root, "locks") / (server + ".lock")


@contextlib.contextmanager
def server_lock(root, server):
    """Held for a whole job; never waited for. Kernel locks die with their process."""
    path = lock_path(root, server)
    with path.open("a") as stream:
        os.chmod(path, 0o600)
        try:
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise Rejected("Another job is running on this server; wait for it to finish") from None
        try:
            yield
        finally:
            fcntl.flock(stream, fcntl.LOCK_UN)


def busy(root, server):
    """Whether a live process holds the server's lock."""
    path = lock_path(root, server)
    if not path.exists():
        return False
    with path.open("a") as stream:
        try:
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return True
        fcntl.flock(stream, fcntl.LOCK_UN)
        return False


def new_steps(definitions):
    return [{"key": key, "title": title, "status": "pending", "detail": None, "startedAt": None, "completedAt": None}
            for key, title in definitions]


class Journal:
    """A job's record on disk; every change is saved at once, so a reader sees live steps."""

    def __init__(self, path, record):
        self.path, self.record = Path(path), record

    @classmethod
    def load(cls, path):
        record = read_json(path)
        require(isinstance(record, dict) and isinstance(record.get("steps"), list), "Invalid job record")
        return cls(path, record)

    def save(self):
        write_json(self.path, self.record)

    def step(self, key):
        return next(item for item in self.record["steps"] if item["key"] == key)

    def done(self, key):
        return self.step(key)["status"] in ("succeeded", "skipped")

    def run(self, key, action):
        """Runs one step: marked running, then succeeded with the detail the action returns, or failed with a
        display-safe reason. Returns whether it succeeded."""
        item = self.step(key)
        item.update(status="running", detail=None, startedAt=now(), completedAt=None)
        self.save()
        try:
            detail = action()
        except (Rejected, CommandFailed) as error:
            item.update(status="failed", detail=reason(error), completedAt=now())
            self.finish("failed", reason(error))
            return False
        except Exception as error:  # noqa: BLE001 - any other message may echo a secret or an input
            item.update(status="failed", detail="The step failed unexpectedly (" + type(error).__name__ + ")",
                        completedAt=now())
            self.finish("failed", item["detail"])
            return False
        item.update(status="succeeded", detail=(str(detail)[:300] if detail else None), completedAt=now())
        self.save()
        return True

    def skip(self, key, detail):
        self.step(key).update(status="skipped", detail=detail, completedAt=now())
        self.save()

    def finish(self, status, why=None):
        self.record.update(status=status, reason=why, completedAt=now())
        self.save()


QUEUED_GRACE = datetime.timedelta(minutes=2)


def shown(record, running):
    """A journal as the console reads it: `interrupted` when no process carries a running job any more, or a queued
    one never started."""
    result = dict(record)
    queued = result.get("status") == "queued"
    try:
        waiting = datetime.datetime.fromisoformat(str(result.get("queuedAt"))) + QUEUED_GRACE > datetime.datetime.now(
            datetime.timezone.utc)
    except ValueError:
        waiting = False
    if (result.get("status") == "running" or (queued and not waiting)) and not running:
        result["status"] = "interrupted"
        result["reason"] = result.get("reason") or "The job stopped before it finished; rerun it, every step converges"
    return result


def spawn(script, *arguments):
    """Starts a job in its own session, detached from the console's request: no shared output, so the console's read
    of this CLI returns at once, and the job outlives the call."""
    subprocess.Popen([sys.executable, str(script), *arguments], stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                     stderr=subprocess.DEVNULL, start_new_session=True, close_fds=True, env=os.environ.copy())


def job_id():
    return str(uuid.uuid4())


def valid_id(value):
    require(isinstance(value, str) and str(uuid.UUID(value)) == value, "Invalid job id")
    return value


def finished(root):
    """Finished jobs the console has not recorded yet, oldest first: what its follower audits and applies."""
    result = []
    for kind, name in (("enrollment", "enrollments"), ("placement", "placements")):
        directory = Path(root) / INFRA / name
        for path in directory.glob("*.json") if directory.is_dir() else ():
            try:
                record = read_json(path)
            except (ValueError, OSError):
                continue
            if isinstance(record, dict) and record.get("status") in FINISHED and not record.get("recordedAt"):
                result.append({key: record.get(key) for key in (
                    "id", "kind", "serverId", "product", "status", "reason", "actor", "completedAt", "mints", "facts",
                    "request")} | {"kind": kind})
    return sorted(result, key=lambda item: item.get("completedAt") or "")


def acknowledge(root, identifier):
    """Marks a finished job recorded by the console, so its follower records it once."""
    valid_id(identifier)
    for name in ("enrollments", "placements"):
        directory = Path(root) / INFRA / name
        for path in directory.glob("*.json") if directory.is_dir() else ():
            try:
                record = read_json(path)
            except (ValueError, OSError):
                continue
            if isinstance(record, dict) and record.get("id") == identifier:
                record["recordedAt"] = now()
                write_json(path, record)
                return {"id": identifier, "recorded": True}
    raise Rejected("No such job")


# Known SSH failures by name; anything else relays the kit's own last line, whose messages hold no secret.
SSH_FAILURES = (("Host key verification failed", "The host presented another SSH host key than the one pinned"),
                ("REMOTE HOST IDENTIFICATION HAS CHANGED", "The host presented another SSH host key than the one pinned"),
                ("Permission denied", "SSH refused the key"),
                ("Connection timed out", "SSH connection timed out"),
                ("Connection refused", "SSH connection refused"),
                ("No route to host", "No route to the host"))


def ssh_failure(code, stderr, denied=None):
    known = next((message for marker, message in SSH_FAILURES if marker in stderr), None)
    if known:
        return CommandFailed(denied if denied and known == "SSH refused the key" else known)
    lines = [line.strip() for line in stderr.splitlines() if line.strip() and "tskey" not in line]
    return CommandFailed((lines[-1][:240] if lines else "The host step failed") + " (exit " + str(code) + ")")


def remote(ssh, command, stdin=None, timeout=30):
    """One command over a runner SSH session (transport.Ssh's pinned options), relaying the kit's own refusal."""
    arguments = ["ssh", *ssh.options(), "-p", str(ssh.config.get("port", 22)), ssh.destination, command]
    try:
        result = subprocess.run(arguments, input=stdin, capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        raise CommandFailed("The host did not finish within " + str(timeout) + " seconds") from None
    if result.returncode:
        raise ssh_failure(result.returncode, result.stderr)
    return result.stdout
