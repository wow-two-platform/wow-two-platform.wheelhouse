"""The runner half of Wheelhouse's Files migration job (dev slice): Files' mover, once per manifest source, in one-off
containers on the source target's host, as a control beside stop, start, restart, reset and run.

Beside runner.py on a target (transport uploads both) and bound to the runner's own helpers (`bind`), since the runner
may run as `__main__` or be streamed over stdin. The payload — the import token and each source's connection string
among it — reaches the worker on stdin only and lives in memory: no job file, history, argv, environment variable or
log holds it. The job keeps the import id, one step per source and each source's result."""
import contextlib
import json
import re
import subprocess
import sys
import time
import urllib.parse
import uuid
from pathlib import Path

OPERATION = "files-migration"
STEPS = ("plan", "push", "verify")
KINDS = ("volume-folder", "postgres-column", "sqlite-column")
# The mover's own limits: how long one source may run, how often progress is written to the job, its memory.
TIMEOUT = 6 * 60 * 60
PROGRESS_SECONDS = 2
MEMORY = "512m"
MOUNT = "/source"
SOURCES_LIMIT = 200
READ_ONLY_OPTION = "Options=-c default_transaction_read_only=on"
CONNECTION_KEY = "ConnectionStrings:Database"
REFUSAL = "files-migration runs on dev targets only in this version"
SELF_REFUSAL = "Wheelhouse's own targets never run a files migration"

SOURCE_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}")
IMPORT_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,99}")
IMAGE = re.compile(r"[a-z0-9][a-z0-9.-]{0,252}(?::[0-9]{1,5})?/[a-z0-9][a-z0-9./_-]{0,200}"
                   r"(?::[A-Za-z0-9][A-Za-z0-9._-]{0,127})?(?:@sha256:[a-f0-9]{64})?")
VOLUME_NAME = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}")
TOP_LEVEL = {"step", "image", "files", "import", "token", "sources", "registry", "unreadable"}
# What each kind needs (text fields), and what it may also carry; `key` may be a list on a PostgreSQL column.
REQUIRED = {"volume-folder": ("volume", "path", "reference"),
            "postgres-column": ("database", "table", "key", "content", "reference"),
            "sqlite-column": ("volume", "database", "table", "key", "content", "reference")}
OPTIONAL = {"volume-folder": (), "postgres-column": ("name", "type"), "sqlite-column": ("type",)}
RELATIVE = {"volume-folder": ("path",), "postgres-column": (), "sqlite-column": ("database",)}

# The runner's helpers, bound by `bind`.
NAMES = ("Rejected", "CommandFailed", "SLUG", "SELF_PRODUCT", "TERMINAL", "Docker", "require", "reason", "read_json",
         "write_json", "now", "validate_target", "validate_bundle", "deployment_lock", "control_ready", "stopped_of",
         "mounted_settings", "private_settings", "configuration_value", "base_environment", "registry_login",
         "registry_unreadable", "cancel_requested", "finish_cancelled", "CANCELLED")


def bind(namespace):
    """Takes the runner's helpers from its namespace; the runner calls this before every use."""
    globals().update({name: namespace[name] for name in NAMES})


def text(value, limit=500):
    return isinstance(value, str) and 0 < len(value) <= limit and not any(ord(c) < 32 or ord(c) == 127 for c in value)


def relative(value):
    return text(value) and not value.startswith(("/", "~")) and "\\" not in value and ".." not in value.split("/")


def https_url(value):
    if not (text(value, 2000) and value.endswith("/")):
        return False
    try:
        parts = urllib.parse.urlsplit(value)
        parts.port
    except ValueError:
        return False
    return (parts.scheme == "https" and bool(parts.hostname) and re.fullmatch(r"[A-Za-z0-9.-]+", parts.hostname) is not None
            and "@" not in parts.netloc and not parts.query and not parts.fragment and ";" not in value)


def validate_source(source, seen):
    require(isinstance(source, dict), "A source is a JSON object")
    name = source.get("id")
    require(isinstance(name, str) and SOURCE_ID.fullmatch(name), "A source needs an id of letters, digits, . _ -")
    require(name not in seen, "Duplicate source id: " + name)
    seen.add(name)
    kind = source.get("kind")
    require(kind in KINDS, name + ": kind is " + ", ".join(KINDS))
    for field in REQUIRED[kind]:
        value = source.get(field)
        if field == "key" and kind == "postgres-column" and isinstance(value, list):
            require(0 < len(value) <= 16 and all(text(item, 200) for item in value), name + ": key names 1 to 16 columns")
        else:
            require(text(value), name + ": " + kind + " needs " + field)
    for field in OPTIONAL[kind]:
        require(source.get(field) is None or text(source[field]), name + ": invalid " + field)
    for field in RELATIVE[kind]:
        require(relative(source[field]), name + ": " + field + " is a path inside the volume")
    if "volume" in REQUIRED[kind]:
        require(not set(":,") & set(source["volume"]), name + ": invalid volume")
    if kind == "postgres-column":
        require(SLUG.fullmatch(source["database"]), name + ": database names the service whose settings hold the connection")


def validate(document):
    """The payload, checked whole before any job exists. Returns it as it will be used: the step, the image, the
    Files URL, the import id and token (push and verify only) and the sources as written, `wave` and the rest kept."""
    require(isinstance(document, dict), "The files migration takes a JSON object")
    extra = sorted(set(document) - TOP_LEVEL)
    require(not extra, "Unknown files migration field: " + (extra[0][:40] if extra else ""))
    step = document.get("step")
    require(step in STEPS, "step is plan, push or verify")
    image = document.get("image")
    require(isinstance(image, str) and IMAGE.fullmatch(image) and ".." not in image, "Invalid image reference")
    require(https_url(document.get("files")), "files is an https URL with a trailing slash")
    imported, token = document.get("import"), document.get("token")
    if step == "plan":
        require(imported is None and token is None, "A plan takes no import and no token")
    else:
        require(isinstance(imported, str) and IMPORT_ID.fullmatch(imported), "A " + step + " needs the import id")
        require(isinstance(token, str) and 0 < len(token) <= 4096 and not any(c.isspace() for c in token),
                "A " + step + " needs the import token")
    sources = document.get("sources")
    require(isinstance(sources, list) and 0 < len(sources) <= SOURCES_LIMIT, "A files migration names 1 to "
            + str(SOURCES_LIMIT) + " sources")
    seen = set()
    for source in sources:
        validate_source(source, seen)
    registry_login(document)
    registry_unreadable(document)
    return {"step": step, "image": image, "files": document["files"], "import": imported, "token": token,
            "sources": sources}


def operation_of(payload):
    """The job's `operation`: the kind, the step and the source ids; never a value."""
    return {"kind": OPERATION, "step": payload["step"], "sources": [source["id"] for source in payload["sources"]]}


def operation_safe(document):
    """`operation_of` for a document that may not be valid (a worker that failed before it had a job record)."""
    document = document if isinstance(document, dict) else {}
    sources = document.get("sources") if isinstance(document.get("sources"), list) else []
    return {"kind": OPERATION, "step": document.get("step") if document.get("step") in STEPS else None,
            "sources": [item["id"] for item in sources if isinstance(item, dict) and isinstance(item.get("id"), str)
                        and SOURCE_ID.fullmatch(item["id"])]}


def release_of(base):
    """The release a target runs: its `current.json`, bundle folder, manifest and Compose document."""
    current_path = Path(base) / "current.json"
    require(current_path.is_file(), Path(base).name + " runs no release yet; there is no host to migrate on")
    current = read_json(current_path)
    require(isinstance(current, dict) and isinstance(current.get("id"), str) and str(uuid.UUID(current["id"])) == current["id"],
            "The release this target runs is unreadable")
    bundle = Path(base) / "releases" / current["id"]
    manifest = validate_bundle(bundle)
    return current, bundle, manifest, read_json(bundle / "compose.json")


def volume_name(compose, project, name):
    """A Compose volume's name on the host, as a snapshot names it: its declared `name`, else `<project>_<volume>`; an
    external one keeps its key. The full name itself is accepted too."""
    declared = compose.get("volumes") or {}
    require(isinstance(declared, dict), "Invalid release volumes")
    names = {}
    for key, value in declared.items():
        value = value if isinstance(value, dict) else {}
        names[key] = value.get("name") or (key if value.get("external") else project + "_" + key)
    full = names.get(name) or (name if name in names.values() else None)
    require(full is not None, "The release declares no volume " + name)
    require(VOLUME_NAME.fullmatch(full), "Invalid volume name")
    return full


def connection_for(target, manifest, bundle, source):
    """The source's PostgreSQL connection string, read on the target from the rendered settings of the service
    `source.database` names (`ConnectionStrings:Database`), read-only for the session. It goes to the mover's stdin only."""
    service = source["database"]
    require(service in manifest["images"], "The release " + manifest["release"] + " has no service " + service)
    path = mounted_settings(target, manifest, bundle).get(service)
    require(path is not None, service + " has no settings file on this target")
    value = configuration_value(private_settings(path, service), CONNECTION_KEY)
    require(isinstance(value, str) and value.strip() and not any(ord(c) < 32 for c in value),
            service + " settings hold no " + CONNECTION_KEY)
    return value.strip().rstrip(";") + ";" + READ_ONLY_OPTION


def prepare(target, payload):
    """Every precondition of one files migration against a target as it is; the one place both launch and worker ask."""
    require(target.get("environment") == "dev", REFUSAL)
    require(target.get("product") != SELF_PRODUCT, SELF_REFUSAL)
    root, project = validate_target(target)
    base = root / project
    current, bundle, manifest, compose = release_of(base)
    require(manifest["product"] != SELF_PRODUCT, SELF_REFUSAL)
    network = target.get("variables", {}).get("PLATFORM_NETWORK")
    for source in payload["sources"]:
        if source["kind"] == "postgres-column":
            require(network, source["id"] + ": the target names no platform network")
            connection_for(target, manifest, bundle, source)
        else:
            volume_name(compose, project, source["volume"])
    return base, project, current, bundle, manifest, compose


def container_name(project, job_id):
    return project + "-files-migration-" + job_id


def arguments_for(target, project, compose, manifest, bundle, payload, source, job_id):
    """One source's container and its stdin document. The mover gets no settings file, no variable and no secret in
    its arguments: every capability dropped, the image's own user, 512 MB; a volume read-only at /source, or the
    platform network and a read-only connection."""
    network, mounts, connection = [], [], None
    if source["kind"] == "postgres-column":
        network = ["--network", target["variables"]["PLATFORM_NETWORK"]]
        connection = connection_for(target, manifest, bundle, source)
    else:
        mounts = ["-v", volume_name(compose, project, source["volume"]) + ":" + MOUNT + ":ro"]
    arguments = ["docker", "run", "--rm", "-i", "--name", container_name(project, job_id), "--pull", "never", *network,
                 "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--memory", MEMORY,
                 "--label", "wheelhouse.files-migration=" + project, *mounts,
                 "--entrypoint", "dotnet", payload["image"], "Files.Api.dll", "migrate", payload["step"]]
    document = {"files": payload["files"], "import": payload["import"], "token": payload["token"], "source": source,
                "connection": connection, "root": MOUNT}
    return arguments, json.dumps(document).encode() + b"\n"


def count(value):
    return value if type(value) is int and value >= 0 else None


def detail_of(progress):
    return str(progress["files"]) + " files · " + str(progress["bytes"]) + " bytes"


class Reader:
    """Reads the mover's stdout, one JSON line at a time: progress throttled into the step, the result kept. Any other
    line is ignored unread and never stored."""

    def __init__(self, step, save, clock):
        self.step, self.save, self.clock = step, save, clock
        self.progress = {"files": 0, "bytes": 0}
        self.result = None
        self.last = None

    def __call__(self, line):
        try:
            value = json.loads(line)
        except ValueError:
            return
        if not isinstance(value, dict):
            return
        progress, result = value.get("progress"), value.get("result")
        if isinstance(progress, dict) and count(progress.get("files")) is not None and count(progress.get("bytes")) is not None:
            self.progress = {"files": progress["files"], "bytes": progress["bytes"]}
            moment = self.clock()
            if self.last is None or moment - self.last >= PROGRESS_SECONDS:
                self.last = moment
                self.step["detail"] = detail_of(self.progress)
                self.save()
        elif isinstance(result, dict) and all(count(result.get(key)) is not None for key in ("files", "bytes")):
            self.result = {key: count(result.get(key)) or 0 for key in ("files", "bytes", "skipped", "failed")}


def launch(target_path, actor, document, runner_file):
    """Queues a files migration and starts its worker detached, as a control's launch does; every rule is checked here
    first, so a refusal reaches the owner before a job exists. The payload reaches the worker on its stdin only."""
    target = read_json(target_path)
    root, project = validate_target(target)
    require(isinstance(actor, str) and 0 < len(actor) <= 160 and "\n" not in actor, "Invalid actor")
    payload = validate(document)
    base = root / project
    prepare(target, payload)
    control_ready(base)
    current = release_of(base)[0]
    job_id = str(uuid.uuid4())
    jobs = base / "jobs"
    jobs.mkdir(parents=True, exist_ok=True, mode=0o700)
    write_json(jobs / (job_id + ".json"), {"id": job_id, "status": "queued", "actor": actor, "queuedAt": now(),
                                           "operation": operation_of(payload), "release": current.get("release")})
    worker = subprocess.Popen([sys.executable, str(runner_file), "control", "--target", str(Path(target_path).resolve()),
                               "--operation", OPERATION, "--actor", actor, "--job", job_id, "--stdin"],
                              stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                              start_new_session=True, close_fds=True)
    # A worker that died before reading fails its own job; the payload lands nowhere else.
    with contextlib.suppress(OSError), worker.stdin:
        worker.stdin.write(json.dumps(document).encode())
    return {"id": job_id, "status": "queued", "release": current.get("release")}


def work(target, actor, job_id, document, docker_factory=None, clock=time.monotonic):
    """Runs the migration under the deployment lock, as a job in the target's journal with durable intent (`active.json`)
    like a control. One step per source, in order; a failed or refused source stops the job, a cancel kills the named
    container and ends it `cancelled`. The product's services are never touched, nor `stopped.json`."""
    docker_factory = docker_factory or Docker
    root, project = validate_target(target)
    base = root / project
    require(isinstance(actor, str) and 0 < len(actor) <= 160 and "\n" not in actor, "Invalid actor")
    require(str(uuid.UUID(job_id)) == job_id, "Invalid deployment id")
    payload = validate(document)
    registry, unreadable = registry_login(document), registry_unreadable(document)
    with deployment_lock(base):
        active_path = base / "active.json"
        active = read_json(active_path) if active_path.exists() else None
        require(not active or active["status"] in TERMINAL, "Interrupted deployment requires reconciliation")
        require(not active or not (active.get("mutationStarted") and active["status"] in ("failed", "rollback_failed")),
                "Failed mutation requires reconciliation")
        base, project, current, bundle, manifest, compose = prepare(target, payload)
        names = [source["id"] for source in payload["sources"]]
        record = {"id": job_id, "project": project, "operation": operation_of(payload), "release": current.get("release"),
                  "sourceCommit": current.get("sourceCommit"), "kind": current.get("kind", "release"), "actor": actor,
                  "status": "running", "startedAt": now(), "previous": current["id"], "steps": [],
                  "filesMigration": {"import": payload["import"], "results": {}}}

        def save():
            write_json(base / "jobs" / (job_id + ".json"), record)
            write_json(active_path, record)

        try:
            if cancel_requested(base, job_id):
                finish_cancelled(record, actor)
            else:
                run_sources(target, base, project, compose, manifest, bundle, payload, names, record, save, job_id, actor,
                            docker_factory, registry, unreadable, clock)
                if record["status"] == "running":
                    record["status"] = "succeeded"
        except (Exception, KeyboardInterrupt) as error:
            record["status"] = "failed"
            record["failure"] = type(error).__name__
            if reason(error):
                record["reason"] = reason(error)
        record["stopped"] = stopped_of(base)
        record["completedAt"] = now()
        save()
        with contextlib.suppress(OSError):
            (base / "cancel" / (job_id + ".json")).unlink()
        return record


def run_sources(target, base, project, compose, manifest, bundle, payload, names, record, save, job_id, actor, docker_factory,
                registry, unreadable, clock):
    docker = docker_factory(target, base_environment(target))
    docker.fetch(payload["image"], registry, unreadable)
    results = record["filesMigration"]["results"]
    for source in payload["sources"]:
        if cancel_requested(base, job_id):
            finish_cancelled(record, actor)
            return
        step = {"name": source["id"], "status": "running", "startedAt": now()}
        record["steps"].append(step)
        save()
        reader = Reader(step, save, clock)
        try:
            if source["kind"] != "postgres-column":
                full = volume_name(compose, project, source["volume"])
                try:
                    docker.execute(["docker", "volume", "inspect", "--format", "{{.Name}}", full],
                                   "docker volume inspect", timeout=30)
                except CommandFailed:
                    raise CommandFailed(source["id"] + ": the volume " + source["volume"] + " does not exist on this target") from None
            arguments, stdin = arguments_for(target, project, compose, manifest, bundle, payload, source, job_id)
            code = docker.stream(arguments, stdin, TIMEOUT, container_name(project, job_id), reader,
                                 lambda: cancel_requested(base, job_id))
            if code == CANCELLED:
                finish_cancelled(record, actor)
                return
            if code == 1:
                raise Rejected("the mover refused " + source["id"])
            if code is None:
                raise CommandFailed("the mover timed out on " + source["id"] + " after " + str(TIMEOUT) + "s")
            if code != 0:
                raise CommandFailed("the mover failed on " + source["id"] + " (exit " + str(code) + ")")
        except BaseException as error:
            step.update(status="failed", completedAt=now())
            if reason(error):
                step["detail"] = reason(error)
            save()
            raise
        if reader.result is not None:
            results[source["id"]] = reader.result
            step["detail"] = detail_of(reader.result)
        else:
            step["detail"] = detail_of(reader.progress)
        step.update(status="succeeded", completedAt=now())
        save()
