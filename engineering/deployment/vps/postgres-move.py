#!/usr/bin/env python3
"""Moves a Wheelhouse host's platform PostgreSQL from 16 to 18 by dump and restore, and reads where it stands. Runs on
the control host or a products host as wheelhouse-deploy, from the kit folder (it installs the kit's Compose template
beside it and imports prepare.py from there). vps.md § PostgreSQL 18.

  status [--json]                        the platform PostgreSQL's image, its 16 and 18 volumes, the move and its runs
  check [--project NAME]                 exit 0 when 18 may start; 3 while 16 data waits for the move or a move runs;
                                         the kit's scripts ask it before they install the platform Compose file
  move   --reason TEXT [--actor NAME]    starts the move detached and prints its job; --wait follows it instead.
         [--wait] [--yes] [--json]       Asks for the platform's project typed unless --yes

The move, each step recorded in <root>/platform/postgres-move/jobs/<job>.json as it runs:
  check    16 runs on <project>_postgres, no move ran, every target unlocked (held to the end), room on disk
  stop     every running workload on the host's `control` and `platform` networks: consoles, products, the canary
  dump     pg_dumpall with PostgreSQL 18's client, and every table's row count in every database
  restore  into 18 on a new volume, <project>_postgres-pg18, in a temporary container; databases, roles and every
           table's rows must match
  switch   the 18 Compose file and the move marker; PostgreSQL recreated on 18 at the same addresses and aliases
  start    the workloads it stopped, each until healthy

The 16 volume is never touched, so the move can be undone (vps.md). A failure before the workloads start again removes
the new volume, puts the 16 Compose file back and starts the stopped workloads on 16 again. Nothing here prints a
password or any row; database output is discarded. Only the Python standard library and the Docker CLI are needed."""
import argparse
import datetime
import fcntl
import hashlib
import json
import os
from pathlib import Path
import pwd
import re
import shutil
import subprocess
import sys
import tempfile
import time
import uuid

# prepare.py, beside this script in the kit: the guard every kit script shares, and the products host's Compose render.
sys.path.insert(0, str(Path(__file__).resolve().parent))
import prepare  # noqa: E402

ROOT = Path("/srv/wheelhouse")
IMAGE = "postgres:18-alpine"
MAJOR = 18
# The Compose project each kit names, its host role and the kit template the move installs for it.
ROLES = {prepare.CONTROL_PROJECT: ("control", "platform.compose.yml"),
         prepare.PRODUCTS_PROJECT: ("products", "products.compose.yml")}
# 16 kept its cluster in the `postgres` volume at /var/lib/postgresql/data; 18 keeps it in `postgres-pg18` at
# /var/lib/postgresql (the cluster in 18/docker) and refuses a 16 data directory.
PREVIOUS = prepare.PREVIOUS_VOLUME
CURRENT = "postgres-pg18"
NETWORKS = ("control", "platform")
FOLDER = prepare.MOVE_FOLDER
MARKER = prepare.MOVE_MARKER
STEPS = (("check", "Check the host"), ("stop", "Stop the workloads"), ("dump", "Dump PostgreSQL 16"),
         ("restore", "Restore into PostgreSQL 18"), ("switch", "Switch the platform to 18"),
         ("start", "Start the workloads"))
STOP_SECONDS = 30
READY_SECONDS = 180
HEALTH_SECONDS = 300
KEEP_JOBS = 20
ACTOR = re.compile(r"[A-Za-z0-9][A-Za-z0-9._:@-]{0,159}")
JOB = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
# The line pg_dumpall writes for the role it connects as, which initdb already made in the new cluster.
OWN_ROLE = "CREATE ROLE postgres;\n"
# Every table's exact row count in one database, `schema.table|rows` a line; a materialized view may hold no data.
COUNTS = ("SELECT format('SELECT %L, count(*) FROM %I.%I', n.nspname || '.' || c.relname, n.nspname, c.relname) "
          "FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind IN ('r', 'p') "
          "AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname !~ '^pg_toast' ORDER BY 1\n\\gexec\n")
DATABASES = "SELECT datname FROM pg_database WHERE datallowconn AND NOT datistemplate ORDER BY 1"
ROLE_NAMES = "SELECT rolname FROM pg_roles WHERE rolname !~ '^pg_' ORDER BY 1"


class Refused(prepare.Refused):
    """A refusal whose message names a rule, a volume, a container or a table, never a password or a row; prepare.py's
    own refusals (its guard, its render) read the same way."""


def require(condition, message):
    if not condition:
        raise Refused(message)


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds")


def read_json(path):
    with open(path) as stream:
        return json.load(stream)


def write_json(path, value):
    """Publishes a whole mode-0600 file at once, so a reader never sees half of it."""
    path = Path(path)
    with tempfile.NamedTemporaryFile("w", dir=path.parent, prefix="." + path.name + ".", delete=False) as stream:
        os.chmod(stream.name, 0o600)
        json.dump(value, stream, indent=2)
        stream.write("\n")
    os.replace(stream.name, path)


def private_directory(path):
    path = Path(path)
    require(not path.is_symlink(), "Refusing a symlinked folder: " + str(path))
    path.mkdir(mode=0o700, parents=True, exist_ok=True)
    return path


class Docker:
    """The Docker CLI as the deploy account. Its output is read only where a step needs a name or a number; nothing it
    prints reaches a record."""

    def run(self, arguments, timeout=120, stdin=None, environment=None, check=True):
        try:
            result = subprocess.run(["docker", *arguments], input=stdin, capture_output=True, text=True,
                                    timeout=timeout, env={**os.environ, **(environment or {})})
        except subprocess.TimeoutExpired:
            raise Refused("docker " + arguments[0] + " did not finish within " + str(timeout) + "s") from None
        except FileNotFoundError:
            raise Refused("The Docker CLI is not installed") from None
        if check and result.returncode:
            raise Refused("docker " + " ".join(arguments[:2]) + " failed (exit " + str(result.returncode) + ")")
        return result

    def stream(self, arguments, lines, timeout):
        """Pipes `lines` into a docker command whose output is discarded; returns its exit code."""
        process = subprocess.Popen(["docker", *arguments], stdin=subprocess.PIPE, stdout=subprocess.DEVNULL,
                                   stderr=subprocess.DEVNULL, text=True)
        deadline = time.monotonic() + timeout
        try:
            for line in lines:
                process.stdin.write(line)
                if time.monotonic() > deadline:
                    raise Refused("The restore did not finish within " + str(timeout) + "s")
            process.stdin.close()
            return process.wait(timeout=max(1, deadline - time.monotonic()))
        except BrokenPipeError:
            return process.wait(timeout=60)
        except (subprocess.TimeoutExpired, Refused):
            process.kill()
            raise Refused("The restore did not finish within " + str(timeout) + "s") from None


def folder_of(root):
    return Path(root) / "platform" / FOLDER


def project_of(root):
    """The platform's Compose project, from the `name:` its compose.yml declares (or, before a products host rendered
    one, its template's)."""
    platform = Path(root) / "platform"
    for name in ("compose.yml", "products.compose.yml"):
        path = platform / name
        if path.is_file() and not path.is_symlink():
            declared = next((line.split(":", 1)[1].strip() for line in path.read_text().splitlines()
                             if line.startswith("name:")), None)
            require(declared in ROLES, "The platform Compose file names no Wheelhouse platform")
            return declared
    raise Refused("No platform Compose file in " + str(platform))


def volume_present(docker, name):
    result = docker.run(["volume", "inspect", "--format", "{{.Name}}", name], timeout=30, check=False)
    return result.returncode == 0 and result.stdout.strip() == name


def marker_of(root):
    return prepare.move_recorded(Path(root))


def busy(root):
    """Whether a live move holds the host's move lock; the lock file is never created here."""
    return prepare.move_running(Path(root))


def guard(root, project=None, docker=None):
    """Why PostgreSQL 18 must not start on this host now, or None when it may (prepare.postgres_guard): a move runs, or
    16 data waits in the previous volume with no move recorded, where 18 would start empty beside it. bootstrap.sh,
    retire-test.sh and migrate-control-network.sh ask it through `check` before they install the platform Compose
    file; prepare.py --server products asks it before it renders one."""
    project = project or project_of(root)
    require(project in ROLES, "Unknown platform project: " + str(project))
    # Without a Docker of the caller's, prepare.py's own reading: a host without the Docker CLI holds no volume yet.
    present = (lambda name: volume_present(docker, name)) if docker else None
    return prepare.postgres_guard(Path(root), project, present)


def postgres_container(docker, project):
    """The platform's PostgreSQL container: its ID, image, major version and state; None when there is none."""
    listing = docker.run(["ps", "--all", "--quiet", "--filter", "label=com.docker.compose.project=" + project,
                          "--filter", "label=com.docker.compose.service=postgres"], timeout=30).stdout.split()
    if not listing:
        return None
    image, state = (docker.run(["inspect", "--format", "{{.Config.Image}}|{{.State.Status}}", listing[0]],
                               timeout=30).stdout.strip().split("|", 1) + [""])[:2]
    found = re.match(r"(?:[^/]+/)*postgres:(\d+)", image)
    return {"id": listing[0], "image": image, "major": int(found.group(1)) if found else None, "state": state}


def jobs_of(root, limit=KEEP_JOBS):
    folder = folder_of(root) / "jobs"
    result = []
    for path in folder.glob("*.json") if folder.is_dir() else ():
        try:
            job = read_json(path)
        except (OSError, ValueError):
            continue
        if isinstance(job, dict) and isinstance(job.get("id"), str):
            result.append(job)
    return sorted(result, key=lambda job: str(job.get("createdAt") or ""), reverse=True)[:limit]


def shown(job, running):
    """A journal as a reader sees it: `interrupted` once no process carries a running or queued job."""
    result = dict(job)
    if result.get("status") in ("queued", "running") and not running:
        queued = result.get("status") == "queued"
        try:
            fresh = queued and datetime.datetime.fromisoformat(str(result.get("createdAt"))) + datetime.timedelta(
                minutes=2) > datetime.datetime.now(datetime.timezone.utc)
        except ValueError:
            fresh = False
        if not fresh:
            result["status"] = "interrupted"
            result["reason"] = result.get("reason") or ("The move stopped before it finished; read its steps, start "
                                                        "what it stopped and remove what it created (vps.md)")
    return result


def status(root, docker=None):
    """Where the host's platform PostgreSQL stands: `needs-move` (16 data, no move yet), `moving`, `moved` (18 on the
    moved data, 16 kept) or `current` (18 from the start). Names, images and states only."""
    docker = docker or Docker()
    project = project_of(root)
    role = ROLES[project][0]
    running = busy(root)
    container = postgres_container(docker, project)
    previous, current = project + "_" + PREVIOUS, project + "_" + CURRENT
    volumes = {"previous": {"name": previous, "present": volume_present(docker, previous)},
               "current": {"name": current, "present": volume_present(docker, current)}}
    marker = marker_of(root)
    state = ("moving" if running else "moved" if marker else "needs-move" if volumes["previous"]["present"]
             else "current")
    return {"project": project, "role": role, "state": state,
            "postgres": None if container is None else {key: container[key] for key in ("image", "major", "state")},
            "volumes": volumes, "moved": marker, "running": running, "target": MAJOR, "image": IMAGE,
            "script": "sha256:" + hashlib.sha256(Path(__file__).read_bytes()).hexdigest()[:12],
            "moves": [shown(job, running) for job in jobs_of(root, 10)]}


def compose_command(root, *arguments):
    platform = Path(root) / "platform"
    return ["compose", "--project-directory", str(platform), "--env-file", str(platform / "host.env"),
            "-f", str(platform / "compose.yml"), *arguments]


def psql(docker, container, database, sql, timeout=600):
    """Rows of one query as the container's superuser over its local socket; the text never reaches a record."""
    result = docker.run(["exec", "-i", container, "psql", "-U", "postgres", "-d", database, "-X", "-q", "-A", "-t",
                         "-v", "ON_ERROR_STOP=1"], timeout=timeout, stdin=sql, check=False)
    require(result.returncode == 0, "psql in " + database + " failed (exit " + str(result.returncode) + ")")
    return [line for line in result.stdout.splitlines() if line]


def inventory(docker, container):
    """What a cluster holds, to compare before and after: its databases, its roles and every table's row count."""
    databases = psql(docker, container, "postgres", DATABASES + ";\n")
    tables = {}
    for database in databases:
        for line in psql(docker, container, database, COUNTS):
            name, _, count = line.rpartition("|")
            require(count.isdigit(), "A row count in " + database + " was unreadable")
            tables[database + "/" + name] = int(count)
    return {"databases": databases, "roles": psql(docker, container, "postgres", ROLE_NAMES + ";\n"), "tables": tables}


def differences(before, after):
    """The databases, roles and tables whose presence or row count differ, by name only."""
    found = sorted(set(before["databases"]) ^ set(after["databases"]))
    found += sorted("role " + name for name in set(before["roles"]) ^ set(after["roles"]))
    found += sorted(name for name in set(before["tables"]) | set(after["tables"])
                    if before["tables"].get(name) != after["tables"].get(name))
    return found


def restore_lines(path):
    with open(path) as stream:
        for line in stream:
            if line != OWN_ROLE:
                yield line


class Move:
    """One move, run as its own process under the host's move lock."""

    def __init__(self, root, job, docker=None, kit=None, clock=None, sleep=None):
        self.root, self.job, self.docker = Path(root), job, docker or Docker()
        self.kit = Path(kit) if kit else Path(__file__).resolve().parent
        self.clock, self.sleep = clock or time.monotonic, sleep or time.sleep
        self.project = job["project"]
        self.role, self.template = ROLES[self.project]
        self.platform = self.root / "platform"
        self.folder = folder_of(self.root)
        self.path = self.folder / "jobs" / (job["id"] + ".json")
        self.previous = self.project + "_" + PREVIOUS
        self.current = self.project + "_" + CURRENT
        self.temporary = self.project + "-postgres-move-" + job["id"][:8]
        self.run_folder = self.folder / "runs" / job["id"]
        self.locks, self.stopped, self.before = [], [], None
        self.created = self.switched = self.restarted = False
        self.backups = []

    # ---- the journal ----

    def save(self):
        write_json(self.path, self.job)

    def step(self, key):
        return next(item for item in self.job["steps"] if item["key"] == key)

    def run_step(self, key, action):
        item = self.step(key)
        item.update(status="running", detail=None, startedAt=now(), completedAt=None)
        self.save()
        outcome = action()
        detail, state = outcome if isinstance(outcome, tuple) else (outcome, "succeeded")
        item.update(status=state, detail=str(detail)[:400] if detail else None, completedAt=now())
        self.save()

    # ---- the steps ----

    def check(self):
        container = postgres_container(self.docker, self.project)
        require(container is not None and container["state"] == "running",
                "The platform PostgreSQL does not run; start it with Compose first")
        require(container["major"] == 16, "The platform PostgreSQL runs " + str(container["image"]) + ", not 16")
        require(marker_of(self.root) is None, "A move to 18 is recorded already (" + MARKER + ")")
        require(volume_present(self.docker, self.previous), "No 16 data in " + self.previous)
        require(not volume_present(self.docker, self.current),
                self.current + " exists already; review it and remove it before moving")
        require((self.kit / self.template).is_file(), "The kit beside this script lacks " + self.template)
        self.source = container["id"]
        self.lock_targets()
        self.docker.run(["pull", "--quiet", IMAGE], timeout=1800)
        size = int(self.docker.run(["run", "--rm", "--network", "none", "-v", self.previous + ":/data:ro", IMAGE,
                                    "du", "-sk", "/data"], timeout=600).stdout.split()[0]) * 1024
        needed = 2 * size + 512 * 1024 ** 2
        for path in (self.root, self.docker_root()):
            try:
                free = shutil.disk_usage(path).free
            except OSError:
                continue
            require(free >= needed, "Only " + gib(free) + " free under " + str(path) + "; the move needs " + gib(needed))
        self.job["facts"].update(bytes=size, source=container["image"])
        return ("16 runs on " + self.previous + " (" + gib(size) + "); " + str(len(self.locks))
                + " targets locked; " + IMAGE + " pulled")

    def docker_root(self):
        try:
            return Path(self.docker.run(["info", "--format", "{{.DockerRootDir}}"], timeout=30).stdout.strip())
        except Refused:
            return self.root

    def lock_targets(self):
        """Every target's deployment lock, held until the move ends: no rollout or control starts meanwhile."""
        for path in sorted(self.root.glob("*/lock")):
            stream = path.open("a")
            try:
                fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                stream.close()
                raise Refused("A rollout or a control runs on " + path.parent.name + "; wait for it, then move again")
            self.locks.append(stream)

    def workloads(self):
        """Running containers on the host's networks outside the platform's own project, by ID and name."""
        found = {}
        for network in NETWORKS:
            for identifier in self.docker.run(["ps", "--quiet", "--no-trunc", "--filter", "network=" + network],
                                              timeout=30).stdout.split():
                found[identifier] = None
        result = []
        for identifier in sorted(found):
            name, project = (self.docker.run(["inspect", "--format",
                                              '{{.Name}}|{{index .Config.Labels "com.docker.compose.project"}}',
                                              identifier], timeout=30).stdout.strip().split("|", 1) + [""])[:2]
            if project != self.project:
                result.append({"id": identifier, "name": name.lstrip("/")})
        return result

    def stop(self):
        self.stopped = self.workloads()
        self.job["facts"]["stopped"] = [item["name"] for item in self.stopped]
        self.save()
        if self.stopped:
            self.docker.run(["stop", "--time", str(STOP_SECONDS), *[item["id"] for item in self.stopped]],
                            timeout=STOP_SECONDS * len(self.stopped) + 120)
        return (plural(len(self.stopped), "workload") + " stopped: " + ", ".join(self.job["facts"]["stopped"])
                if self.stopped else "No workload runs on the host's networks")

    def dump(self):
        private_directory(self.run_folder)
        password = (self.platform / ".secrets" / "postgres-password").read_text().strip()
        require(password, "The platform's PostgreSQL password file is empty")
        self.docker.run(["run", "--rm", "--network", "container:" + self.source,
                         "--user", str(os.getuid()) + ":" + str(os.getgid()), "-e", "PGPASSWORD",
                         "-v", str(self.run_folder) + ":/dump", IMAGE, "pg_dumpall", "--host", "127.0.0.1",
                         "--username", "postgres", "--no-password", "--file", "/dump/all.sql"],
                        timeout=3600, environment={"PGPASSWORD": password})
        dump = self.run_folder / "all.sql"
        require(dump.is_file() and dump.stat().st_size > 0, "pg_dumpall wrote nothing")
        dump.chmod(0o600)
        self.before = inventory(self.docker, self.source)
        facts = self.job["facts"]
        facts.update(databases=self.before["databases"], roles=len(self.before["roles"]),
                     tables=len(self.before["tables"]), rows=sum(self.before["tables"].values()),
                     dumpBytes=dump.stat().st_size)
        return (plural(len(self.before["databases"]), "database") + ", " + plural(facts["tables"], "table") + ", "
                + plural(facts["rows"], "row") + " and " + plural(facts["roles"], "role") + " dumped ("
                + gib(facts["dumpBytes"]) + ")")

    def restore(self):
        self.docker.run(["volume", "create", "--label", "com.docker.compose.project=" + self.project,
                         "--label", "com.docker.compose.volume=" + CURRENT, self.current], timeout=60)
        self.created = True
        self.docker.run(["run", "--detach", "--name", self.temporary, "--network", "none",
                         "--label", "wheelhouse.postgres-move=" + self.job["id"],
                         "-e", "POSTGRES_PASSWORD_FILE=/run/move/postgres-password",
                         "-e", "POSTGRES_INITDB_ARGS=--auth-host=scram-sha-256",
                         "-v", str(self.platform / ".secrets" / "postgres-password") + ":/run/move/postgres-password:ro",
                         "-v", self.current + ":/var/lib/postgresql", IMAGE], timeout=120)
        self.ready(self.temporary)
        code = self.docker.stream(["exec", "-i", self.temporary, "psql", "-U", "postgres", "-d", "postgres", "-X", "-q",
                                   "-v", "ON_ERROR_STOP=1"], restore_lines(self.run_folder / "all.sql"), 3600)
        require(code == 0, "The restore stopped at an error (psql exit " + str(code) + "); 16 is untouched")
        after = inventory(self.docker, self.temporary)
        differ = differences(self.before, after)
        require(not differ, "The restored data differs from 16's: " + ", ".join(differ[:8])
                + (" and " + str(len(differ) - 8) + " more" if len(differ) > 8 else ""))
        self.docker.run(["stop", "--time", str(STOP_SECONDS), self.temporary], timeout=STOP_SECONDS + 60)
        self.docker.run(["rm", self.temporary], timeout=60)
        return ("Restored into " + self.current + " on " + IMAGE + "; " + plural(len(after["databases"]), "database")
                + ", " + plural(len(after["tables"]), "table") + " and every row count match 16's")

    def ready(self, container):
        deadline = self.clock() + READY_SECONDS
        # Over TCP: while initdb runs, the image's temporary server listens on its socket only.
        while self.docker.run(["exec", container, "pg_isready", "--host", "127.0.0.1", "--username", "postgres"],
                              timeout=30, check=False).returncode:
            require(self.clock() < deadline, "PostgreSQL 18 did not get ready within " + str(READY_SECONDS) + "s")
            self.sleep(2)

    def switch(self):
        files = ["compose.yml"] + (["products.compose.yml"] if self.role == "products" else [])
        for name in files:
            source = self.platform / name
            backup = self.platform / name.replace(".yml", ".pg16.yml")
            shutil.copyfile(source, backup)
            backup.chmod(0o600)
            self.backups.append((backup, source))
        write_json(self.folder / MARKER, {"from": 16, "to": MAJOR, "image": IMAGE, "volume": self.current,
                                          "kept": self.previous, "job": self.job["id"], "at": now(),
                                          "compose": [str(backup.name) for backup, _ in self.backups]})
        self.switched = True
        self.save()
        if self.role == "control":
            install(self.kit / self.template, self.platform / "compose.yml")
        else:
            # A products host runs the file prepare.py renders from the template and its registered site nodes.
            install(self.kit / self.template, self.platform / "products.compose.yml")
            prepare.render_products_compose(self.root)
        self.docker.run(compose_command(self.root, "config", "--quiet"), timeout=60)
        self.docker.run(compose_command(self.root, "up", "--detach", "--wait", "postgres"), timeout=600)
        container = postgres_container(self.docker, self.project)
        require(container is not None and container["major"] == MAJOR,
                "PostgreSQL did not come back on " + str(MAJOR))
        version = psql(self.docker, container["id"], "postgres", "SHOW server_version_num;\n")
        require(version and version[0].isdigit() and int(version[0]) // 10000 == MAJOR,
                "PostgreSQL did not answer as " + str(MAJOR))
        databases = psql(self.docker, container["id"], "postgres", DATABASES + ";\n")
        require(databases == self.before["databases"], "PostgreSQL 18 holds other databases than 16 did")
        return ("PostgreSQL " + str(MAJOR) + " serves " + self.current + " at the same addresses; " + self.previous
                + " kept; the 16 Compose " + ("files" if len(files) > 1 else "file") + " kept as "
                + ", ".join(backup.name for backup, _ in self.backups))

    def start(self):
        if not self.stopped:
            return "Nothing to start"
        self.docker.run(["start", *[item["id"] for item in self.stopped]], timeout=120 + 30 * len(self.stopped))
        self.restarted = True
        deadline, waiting = self.clock() + HEALTH_SECONDS, []
        while True:
            waiting = []
            for item in self.stopped:
                state, health = (self.docker.run(["inspect", "--format",
                                                  "{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{end}}",
                                                  item["id"]], timeout=30, check=False).stdout.strip().split("|", 1)
                                 + [""])[:2]
                if state != "running" or health not in ("", "healthy"):
                    waiting.append(item["name"] + " (" + (health or state or "gone") + ")")
            if not waiting or self.clock() >= deadline:
                break
            self.sleep(3)
        started = plural(len(self.stopped), "workload") + " started"
        if waiting:
            self.job.setdefault("warnings", []).append("Not healthy within " + str(HEALTH_SECONDS) + "s: "
                                                       + ", ".join(waiting))
            return started + "; not healthy yet: " + ", ".join(waiting), "warning"
        return started + ", each running and healthy"

    # ---- recovery ----

    def recover(self):
        """After a failure: back to 16 as it was, with every stopped workload started again. Each part runs whatever
        the others did, and what could not be undone joins the job's reason."""
        problems = []
        attempts = [("the temporary container", lambda: self.docker.run(["rm", "--force", self.temporary],
                                                                        timeout=120, check=False))]
        if self.switched:
            def back():
                for backup, source in self.backups:
                    shutil.copyfile(backup, source)
                    source.chmod(0o600)
                (self.folder / MARKER).unlink(missing_ok=True)
                self.docker.run(compose_command(self.root, "up", "--detach", "--wait", "postgres"), timeout=600)
            attempts.append(("PostgreSQL 16 back on " + self.previous, back))
        if self.created:
            attempts.append((self.current, lambda: self.docker.run(["volume", "rm", self.current], timeout=120)))
        if self.stopped and not self.restarted:
            attempts.append(("the stopped workloads", lambda: self.docker.run(
                ["start", *[item["id"] for item in self.stopped]], timeout=120 + 30 * len(self.stopped))))
        for name, action in attempts:
            try:
                action()
            except (prepare.Refused, OSError, subprocess.SubprocessError):
                problems.append(name)
        return problems

    def finish(self, outcome, why=None):
        self.job.update(status=outcome, completedAt=now(), **({"reason": why[:600]} if why else {}))
        self.save()
        for stream in self.locks:
            stream.close()

    def run(self):
        self.job.update(status="running", startedAt=now())
        self.save()
        try:
            for key, action in (("check", self.check), ("stop", self.stop), ("dump", self.dump),
                                ("restore", self.restore), ("switch", self.switch), ("start", self.start)):
                self.run_step(key, action)
        except BaseException as error:  # noqa: BLE001 - a record must end whatever stopped it
            message = (str(error) if isinstance(error, prepare.Refused) else
                       "The step failed unexpectedly (" + type(error).__name__ + ")")
            running = next((item for item in self.job["steps"] if item["status"] == "running"), None)
            if running:
                running.update(status="failed", detail=message[:400], completedAt=now())
            problems = self.recover() if self.step("start")["status"] == "pending" else []
            why = message + ("; recovered: 16 serves again with every stopped workload started" if not problems
                             else "; not recovered: " + ", ".join(problems) + " — vps.md § PostgreSQL 18, Undo")
            if self.run_folder.is_dir():
                why += "; the dump stays in " + str(self.run_folder)
            self.finish("failed", why)
            if not isinstance(error, Exception):
                raise
            return self.job
        shutil.rmtree(self.run_folder, ignore_errors=True)
        self.finish("succeeded")
        return self.job


def install(source, destination):
    with tempfile.NamedTemporaryFile("wb", dir=destination.parent, prefix="." + destination.name + ".",
                                     delete=False) as stream:
        os.chmod(stream.name, 0o600)
        stream.write(Path(source).read_bytes())
    os.replace(stream.name, destination)


def gib(count):
    return ("%.1f GiB" % (count / 1024 ** 3)) if count >= 1024 ** 3 else ("%d MiB" % max(1, round(count / 1024 ** 2)))


def plural(count, noun):
    return str(count) + " " + noun + ("" if count == 1 else "s")


def actor_of(value):
    if value:
        require(ACTOR.fullmatch(value), "Invalid --actor")
        return value
    login = os.environ.get("SUDO_USER") or os.environ.get("USER") or pwd.getpwuid(os.getuid()).pw_name
    return "postgres-move:" + (login if re.fullmatch(r"[A-Za-z0-9._-]{1,40}", login or "") else "unknown")


def new_job(root, actor, reason):
    """A queued move, refused before any job exists when the host is not ready for one."""
    require(isinstance(reason, str) and 0 < len(reason.strip()) <= 300 and "\n" not in reason,
            "Give a one-line --reason of 300 characters at most")
    project = project_of(root)
    require(not busy(root), "A PostgreSQL move runs on this host already")
    require(marker_of(root) is None, "This host moved to 18 already")
    folder = private_directory(folder_of(root))
    private_directory(folder / "jobs")
    private_directory(folder / "runs")
    lock = folder / "lock"
    lock.touch(mode=0o600, exist_ok=True)
    identifier = str(uuid.uuid4())
    job = {"id": identifier, "kind": "postgres-move", "project": project, "role": ROLES[project][0],
           "from": 16, "to": MAJOR, "actor": actor, "reason": None, "why": reason.strip(), "status": "queued",
           "createdAt": now(), "startedAt": None, "completedAt": None, "facts": {},
           "steps": [{"key": key, "title": title, "status": "pending", "detail": None, "startedAt": None,
                      "completedAt": None} for key, title in STEPS]}
    write_json(folder / "jobs" / (identifier + ".json"), job)
    # The newest journals stay; job IDs are random, so their age is their modification time.
    for old in sorted((folder / "jobs").glob("*.json"), key=lambda path: path.stat().st_mtime)[:-KEEP_JOBS]:
        old.unlink(missing_ok=True)
    return job


def run_job(root, identifier, docker=None, kit=None):
    """The detached worker: takes the move lock for the whole run, then moves."""
    require(JOB.fullmatch(identifier or ""), "Invalid job")
    folder = folder_of(root)
    path = folder / "jobs" / (identifier + ".json")
    job = read_json(path)
    with (folder / "lock").open("a") as stream:
        try:
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            job.update(status="failed", reason="Another PostgreSQL move runs on this host", completedAt=now())
            write_json(path, job)
            return job
        require(job.get("status") == "queued", "This move ran already; start a new one")
        return Move(root, job, docker, kit).run()


def follow(root, identifier, output, clock=time.monotonic, sleep=time.sleep):
    seen = set()
    while True:
        job = shown(read_json(folder_of(root) / "jobs" / (identifier + ".json")), busy(root))
        for item in job["steps"]:
            key = (item["key"], item["status"])
            if item["status"] not in ("pending", "running") and key not in seen:
                seen.add(key)
                print("  " + item["status"].ljust(9) + " " + item["title"]
                      + (" — " + item["detail"] if item.get("detail") else ""), file=output)
        if job["status"] in ("succeeded", "failed", "interrupted"):
            return job
        sleep(2)


def confirm(project, assume_yes, ask=None):
    if assume_yes:
        return
    try:
        typed = (ask or input)("Every workload on this host stops during the move. Type " + project + " to continue: ")
    except EOFError:
        typed = ""
    require(typed.strip() == project, "Not confirmed; nothing changed")


def main(argv=None, output=sys.stdout):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("action", choices=["status", "check", "move", "run"])
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--project", choices=sorted(ROLES),
                        help="check: the platform's project, for a host whose compose.yml does not exist yet")
    parser.add_argument("--reason", help="move: why, kept on the job")
    parser.add_argument("--actor", help="move: who, as the console names its user")
    parser.add_argument("--job", help="run: the queued move to run")
    parser.add_argument("--wait", action="store_true", help="move: follow the move here instead of detaching it")
    parser.add_argument("--yes", action="store_true", help="move: skip the typed confirmation")
    parser.add_argument("--json", action="store_true", help="status, move: machine-readable")
    arguments = parser.parse_args(argv)
    try:
        if arguments.action == "status":
            reading = status(arguments.root)
            if arguments.json:
                print(json.dumps(reading), file=output)
            else:
                running = reading["postgres"] or {}
                print(reading["project"] + ": " + reading["state"] + "; PostgreSQL " + str(running.get("major"))
                      + " (" + str(running.get("image")) + ", " + str(running.get("state")) + ")", file=output)
                for job in reading["moves"][:3]:
                    print("  " + str(job.get("status")).ljust(11) + " " + str(job.get("createdAt")) + " "
                          + str(job.get("actor")) + (" — " + job["reason"] if job.get("reason") else ""), file=output)
            return 0
        if arguments.action == "check":
            message = guard(arguments.root, arguments.project)
            if message:
                print("Refused: " + message, file=sys.stderr)
                return 3
            print("PostgreSQL " + str(MAJOR) + " may start on this host.", file=output)
            return 0
        require(os.geteuid() != 0, "Run as the deploy account: sudo -u wheelhouse-deploy python3 " + sys.argv[0]
                + " ...; files written as root would lock its rollouts out")
        if arguments.action == "run":
            run_job(arguments.root, arguments.job)
            return 0
        job = new_job(arguments.root, actor_of(arguments.actor), arguments.reason or "")
        try:
            confirm(job["project"], arguments.yes)
        except Refused:
            (folder_of(arguments.root) / "jobs" / (job["id"] + ".json")).unlink(missing_ok=True)
            raise
        # The move runs as its own process, so it outlives this session: a dropped SSH connection never cuts it short.
        subprocess.Popen([sys.executable, str(Path(__file__).resolve()), "run", "--root", str(arguments.root),
                          "--job", job["id"]], stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                         stderr=subprocess.DEVNULL, start_new_session=True, close_fds=True)
        if arguments.wait:
            print("Moving " + job["project"] + " to PostgreSQL " + str(MAJOR) + " (job " + job["id"] + ")", file=output)
            ended = follow(arguments.root, job["id"], output)
            print(json.dumps({key: ended.get(key) for key in ("id", "status", "reason", "warnings") if ended.get(key)}),
                  file=output)
            return 0 if ended["status"] == "succeeded" else 1
        if arguments.json:
            print(json.dumps(job), file=output)
        else:
            print("Started " + job["id"] + ": " + job["project"] + " moves to PostgreSQL " + str(MAJOR)
                  + " detached; every workload on the host stops for a few minutes. Read it with: postgres-move.py "
                  "status", file=output)
        return 0
    except prepare.Refused as error:
        print("Refused: " + str(error), file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        print("Stopped; a started move continues on its own. Read it with: postgres-move.py status", file=sys.stderr)
        return 130


if __name__ == "__main__":
    sys.exit(main())
