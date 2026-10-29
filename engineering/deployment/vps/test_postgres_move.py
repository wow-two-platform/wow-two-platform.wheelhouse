"""postgres-move.py on a stand-in host: Docker is a model of volumes holding clusters, containers and the platform's
Compose, so every step, refusal and recovery runs without a daemon. The kit scripts' guard runs against a stand-in
`docker` binary that answers `volume inspect` with exit 0, as the runner's fakes do. Never contacts Docker or a host."""
import fcntl
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

KIT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("postgres_move", KIT / "postgres-move.py")
move = importlib.util.module_from_spec(spec)
spec.loader.exec_module(move)

PASSWORD = "pw-never-printed-0123456789abcdef"


def sixteen(template):
    """A kit Compose template as the host ran it before 18: PostgreSQL 16 on the `postgres` volume."""
    text = (KIT / template).read_text()
    text = text.replace("image: postgres:18-alpine", "image: postgres:16-alpine")
    text = text.replace("- postgres-pg18:/var/lib/postgresql\n", "- postgres:/var/lib/postgresql/data\n")
    return text.replace("\n  postgres-pg18:\n", "\n  postgres:\n")


class Result:
    def __init__(self, returncode=0, stdout=""):
        self.returncode, self.stdout = returncode, stdout


class Host:
    """Docker on a Wheelhouse host. Volumes hold clusters ({databases, roles, tables}); containers carry a project,
    service, image, state, volume and networks. `fail` names what goes wrong: `restore-differs` loses a row in the
    restore, `restore-error` stops psql, `up-18` keeps 18 from coming up, `dump` fails pg_dumpall."""

    def __init__(self, root, project, networks):
        self.root, self.project, self.calls, self.environments, self.fail = Path(root), project, [], [], set()
        self.cluster = {"databases": ["postgres", "wheelhouse_dev", "wheelhouse_prod", "wheelhousecanary_dev"],
                        "roles": ["postgres", "wheelhouse_dev", "wheelhouse_prod", "wheelhousecanary_dev"],
                        "tables": {"wheelhouse_prod/public.products": 7, "wheelhouse_prod/public.audit_entries": 120,
                                   "wheelhouse_dev/public.products": 3, "wheelhousecanary_dev/public.pings": 40}}
        self.volumes = {project + "_postgres": json.loads(json.dumps(self.cluster)), project + "_canary": None}
        self.containers, self.serial = {}, 0
        self.add(project, "postgres", "postgres:16-alpine", networks, volume=project + "_postgres")
        self.add(project, "ingress", "traefik:v3.5", networks)
        self.ready_after = 1

    def add(self, project, service, image, networks, state="running", volume=None, health="healthy", name=None):
        self.serial += 1
        identifier = "%064x" % self.serial
        self.containers[identifier] = {"name": name or project + "-" + service + "-1", "project": project,
                                       "service": service, "image": image, "state": state, "volume": volume,
                                       "networks": list(networks), "health": health}
        return identifier

    def named(self, reference):
        return next((identifier for identifier, container in self.containers.items()
                     if reference in (identifier, container["name"])), None)

    def postgres(self):
        return next(container for container in self.containers.values()
                    if container["project"] == self.project and container["service"] == "postgres")

    @staticmethod
    def major(image):
        found = re.match(r"postgres:(\d+)", image)
        return int(found.group(1)) if found else None

    # ---- Docker.run ----

    def run(self, arguments, timeout=120, stdin=None, environment=None, check=True):
        self.calls.append(list(arguments))
        self.environments.append(dict(environment or {}))
        code, out = self.answer(list(arguments), stdin, environment or {})
        if check and code:
            raise move.Refused("docker " + " ".join(arguments[:2]) + " failed (exit " + str(code) + ")")
        return Result(code, out)

    def answer(self, arguments, stdin, environment):
        verb = arguments[0]
        if arguments[:2] == ["volume", "inspect"]:
            name = arguments[-1]
            return (0, name + "\n") if name in self.volumes else (1, "")
        if arguments[:2] == ["volume", "create"]:
            self.volumes[arguments[-1]] = None
            return 0, arguments[-1] + "\n"
        if arguments[:2] == ["volume", "rm"]:
            if any(container["volume"] == arguments[-1] for container in self.containers.values()):
                return 1, ""
            self.volumes.pop(arguments[-1], None)
            return 0, ""
        if verb == "ps":
            filters = [arguments[index + 1] for index, value in enumerate(arguments) if value == "--filter"]
            found = []
            for identifier, container in self.containers.items():
                if "--all" not in arguments and container["state"] != "running":
                    continue
                labels = {"label=com.docker.compose.project=" + container["project"],
                          "label=com.docker.compose.service=" + container["service"]}
                labels |= {"network=" + network for network in container["networks"]}
                if all(item in labels for item in filters):
                    found.append(identifier)
            return 0, "\n".join(found) + ("\n" if found else "")
        if verb == "inspect":
            container = self.containers.get(self.named(arguments[-1]) or "")
            if container is None:
                return 1, ""
            template = arguments[2]
            if template.startswith("{{.Config.Image}}"):
                return 0, container["image"] + "|" + container["state"] + "\n"
            if template.startswith("{{.Name}}"):
                return 0, "/" + container["name"] + "|" + container["project"] + "\n"
            return 0, container["state"] + "|" + (container["health"] or "") + "\n"
        if verb in ("pull", "info"):
            return 0, str(self.root) + "\n" if verb == "info" else ""
        if verb == "stop":
            for reference in arguments[3:]:
                self.containers[self.named(reference)]["state"] = "exited"
            return 0, ""
        if verb == "start":
            for reference in arguments[1:]:
                self.containers[self.named(reference)]["state"] = "running"
            return 0, ""
        if verb == "rm":
            identifier = self.named(arguments[-1])
            if identifier is None:
                return 1, ""
            del self.containers[identifier]
            return 0, ""
        if verb == "run":
            return self.one_off(arguments, environment)
        if verb == "exec":
            return self.execute(arguments, stdin)
        if verb == "compose":
            return self.compose(arguments)
        raise AssertionError("unexpected docker call: " + " ".join(arguments))

    def one_off(self, arguments, environment):
        if "du" in arguments:
            return 0, "2048\t/data\n"
        if "pg_dumpall" in arguments:
            assert environment.get("PGPASSWORD") == PASSWORD and "PGPASSWORD" in arguments
            if "dump" in self.fail:
                return 1, ""
            source = self.containers[arguments[arguments.index("--network") + 1].split(":", 1)[1]]
            folder = Path(arguments[arguments.index("-v") + 1].split(":", 1)[0])
            (folder / "all.sql").write_text(json.dumps(self.volumes[source["volume"]]) + "\n" + move.OWN_ROLE
                                            + "ALTER ROLE postgres WITH SUPERUSER;\n")
            return 0, ""
        if "--detach" in arguments:
            volume = next(value.split(":", 1)[0] for value in arguments if value.endswith(":/var/lib/postgresql"))
            if self.volumes.get(volume) is None:
                self.volumes[volume] = {"databases": ["postgres"], "roles": ["postgres"], "tables": {}}
            self.add("", "move", arguments[-1], [], volume=volume, health=None,
                     name=arguments[arguments.index("--name") + 1])
            return 0, ""
        raise AssertionError("unexpected docker run: " + " ".join(arguments))

    def execute(self, arguments, stdin):
        container = self.containers[self.named(arguments[2] if arguments[1] == "-i" else arguments[1])]
        if "pg_isready" in arguments:
            self.ready_after -= 1
            return (0 if self.ready_after < 0 else 2), ""
        cluster = self.volumes[container["volume"]]
        database = arguments[arguments.index("-d") + 1]
        if stdin.startswith(move.DATABASES):
            return 0, "\n".join(cluster["databases"]) + "\n"
        if stdin.startswith(move.ROLE_NAMES):
            return 0, "\n".join(cluster["roles"]) + "\n"
        if stdin.startswith("SHOW server_version_num"):
            return 0, str(self.major(container["image"]) * 10000 + 4) + "\n"
        if stdin == move.COUNTS:
            return 0, "".join(name.split("/", 1)[1] + "|" + str(rows) + "\n" for name, rows in
                              sorted(cluster["tables"].items()) if name.startswith(database + "/"))
        raise AssertionError("unexpected psql: " + stdin)

    def compose(self, arguments):
        path = Path(arguments[arguments.index("-f") + 1])
        action = arguments[arguments.index("-f") + 2:]
        if action == ["config", "--quiet"]:
            return 0, ""
        assert action == ["up", "--detach", "--wait", "postgres"], action
        text = path.read_text()
        eighteen = "image: postgres:18-alpine" in text
        if eighteen and "up-18" in self.fail:
            return 1, ""
        old = self.postgres()
        identifier = self.named(old["name"])
        volume = self.project + ("_postgres-pg18" if eighteen else "_postgres")
        if self.volumes.get(volume, "missing") == "missing":
            self.volumes[volume] = {"databases": ["postgres"], "roles": ["postgres"], "tables": {}}
        del self.containers[identifier]
        self.add(self.project, "postgres", "postgres:18-alpine" if eighteen else "postgres:16-alpine", old["networks"],
                 volume=volume)
        return 0, ""

    # ---- Docker.stream: the restore ----

    def stream(self, arguments, lines, timeout):
        self.calls.append(list(arguments))
        content = "".join(lines)
        assert move.OWN_ROLE not in content, "the dump's own CREATE ROLE postgres reaches the new cluster"
        if "restore-error" in self.fail:
            return 3
        cluster = json.loads(content.splitlines()[0])
        if "restore-differs" in self.fail:
            cluster["tables"]["wheelhouse_prod/public.audit_entries"] -= 1
        container = self.containers[self.named(arguments[2])]
        self.volumes[container["volume"]] = cluster
        return 0


class MoveTests(unittest.TestCase):
    project, role, template, networks = "wheelhouse-platform", "control", "platform.compose.yml", ("control", "platform")

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "wheelhouse"
        self.platform = self.root / "platform"
        (self.platform / ".secrets").mkdir(parents=True, mode=0o700)
        (self.platform / ".secrets" / "postgres-password").write_text(PASSWORD + "\n")
        (self.platform / "host.env").write_text("WHEELHOUSE_DEPLOY_UID=1000\nWHEELHOUSE_DEPLOY_GID=1000\n")
        self.lay_out()
        self.host = Host(self.root, self.project, self.networks)
        self.workloads = self.add_workloads()
        self.before = (self.platform / "compose.yml").read_text()

    def lay_out(self):
        (self.platform / "compose.yml").write_text(sixteen(self.template))
        for target in ("wheelhouse-prod", "wheelhouse-dev", "wheelhousecanary-dev"):
            (self.root / target).mkdir()
            (self.root / target / "lock").touch()

    def add_workloads(self):
        return {"prod": self.host.add("wheelhouse-prod", "console", "ghcr.io/wow-two-platform/wheelhouse@sha256:1",
                                      ["control"]),
                "dev": self.host.add("wheelhouse-dev", "console", "ghcr.io/wow-two-platform/wheelhouse@sha256:2",
                                     ["control"]),
                "canary": self.host.add("wheelhousecanary-dev", "canary", "ghcr.io/wow-two-platform/canary@sha256:3",
                                        ["platform"]),
                # Stopped by its owner: the move leaves it as it is.
                "stopped": self.host.add("wheelhousecanary-prod", "canary", "ghcr.io/wow-two-platform/canary@sha256:3",
                                         ["platform"], state="exited")}

    def moved(self, reason="PostgreSQL 18 everywhere"):
        job = move.new_job(self.root, "postgres-move:admin", reason)
        with mock.patch.object(move.time, "sleep", lambda seconds: None):
            return move.run_job(self.root, job["id"], docker=self.host, kit=KIT)

    def state(self, key):
        return self.host.containers[self.workloads[key]]["state"]

    def steps(self, job):
        return {step["key"]: step["status"] for step in job["steps"]}

    def test_the_move_keeps_every_row_switches_to_18_and_starts_what_it_stopped(self):
        self.assertIn("PostgreSQL 16 data waits in " + self.project + "_postgres",
                      move.guard(self.root, docker=self.host))
        job = self.moved()
        self.assertEqual("succeeded", job["status"], job.get("reason"))
        self.assertEqual({key: "succeeded" for key, _ in move.STEPS}, self.steps(job))
        postgres = self.host.postgres()
        self.assertEqual(("postgres:18-alpine", self.project + "_postgres-pg18", "running"),
                         (postgres["image"], postgres["volume"], postgres["state"]))
        # Every database, role and row moved; 16's volume is kept exactly as it was.
        self.assertEqual(self.host.cluster, self.host.volumes[self.project + "_postgres-pg18"])
        self.assertEqual(self.host.cluster, self.host.volumes[self.project + "_postgres"])
        self.assertEqual(4, job["facts"]["tables"])
        self.assertEqual(170, job["facts"]["rows"])
        # The consoles and the canary stopped and started again; the platform's own containers and a stopped one stayed.
        stops = [call for call in self.host.calls if call[0] == "stop" and call[:3] == ["stop", "--time", "30"]]
        self.assertEqual(sorted([self.workloads["prod"], self.workloads["dev"], self.workloads["canary"]]),
                         sorted(stops[0][3:]))
        self.assertEqual(["running"] * 3 + ["exited"], [self.state(key) for key in ("prod", "dev", "canary", "stopped")])
        self.assertIn("3 workloads stopped: ", job["steps"][1]["detail"])
        # The kit's 18 Compose file runs now; 16's is kept beside it, and the move is recorded for the guard.
        self.assert_switched()
        marker = json.loads((self.platform / "postgres-move" / "moved.json").read_text())
        self.assertEqual((16, 18, self.project + "_postgres-pg18", self.project + "_postgres", job["id"]),
                         (marker["from"], marker["to"], marker["volume"], marker["kept"], marker["job"]))
        self.assertIsNone(move.guard(self.root, docker=self.host))
        self.assertEqual("moved", move.status(self.root, docker=self.host)["state"])
        # The dump went once the rows matched; the password travelled by name in the client's environment only.
        self.assertFalse((self.platform / "postgres-move" / "runs" / job["id"]).exists())
        self.assertFalse(any(PASSWORD in argument for call in self.host.calls for argument in call))
        self.assertNotIn(PASSWORD, json.dumps(job))
        self.assertFalse(any(container["service"] == "move" for container in self.host.containers.values()))

    def assert_switched(self):
        self.assertEqual((KIT / self.template).read_text(), (self.platform / "compose.yml").read_text())
        self.assertEqual(self.before, (self.platform / "compose.pg16.yml").read_text())

    def test_a_restore_that_differs_puts_16_back_and_starts_every_workload(self):
        self.host.fail.add("restore-differs")
        job = self.moved()
        self.assertEqual("failed", job["status"])
        self.assertEqual("failed", self.steps(job)["restore"])
        self.assertIn("The restored data differs from 16's: wheelhouse_prod/public.audit_entries", job["reason"])
        self.assertIn("recovered: 16 serves again with every stopped workload started", job["reason"])
        self.assertIn("the dump stays in " + str(self.platform / "postgres-move" / "runs" / job["id"]), job["reason"])
        self.assertNotIn(self.project + "_postgres-pg18", self.host.volumes)
        self.assertEqual(("postgres:16-alpine", "running"), (self.host.postgres()["image"], self.host.postgres()["state"]))
        self.assertEqual(self.before, (self.platform / "compose.yml").read_text())
        self.assertFalse((self.platform / "postgres-move" / "moved.json").exists())
        self.assertEqual(["running"] * 3, [self.state(key) for key in ("prod", "dev", "canary")])
        self.assertFalse(any(container["service"] == "move" for container in self.host.containers.values()))
        self.assertIn("PostgreSQL 16 data waits", move.guard(self.root, docker=self.host))

    def test_18_that_does_not_come_up_returns_the_platform_to_16(self):
        self.host.fail.add("up-18")
        job = self.moved()
        self.assertEqual("failed", job["status"])
        self.assertEqual("failed", self.steps(job)["switch"])
        self.assertIn("recovered", job["reason"])
        self.assertEqual(self.before, (self.platform / "compose.yml").read_text())
        self.assertFalse((self.platform / "postgres-move" / "moved.json").exists())
        postgres = self.host.postgres()
        self.assertEqual(("postgres:16-alpine", self.project + "_postgres"), (postgres["image"], postgres["volume"]))
        self.assertNotIn(self.project + "_postgres-pg18", self.host.volumes)
        self.assertEqual(["running"] * 3, [self.state(key) for key in ("prod", "dev", "canary")])

    def test_a_failed_dump_stops_nothing_more_and_restarts_what_it_stopped(self):
        self.host.fail.add("dump")
        job = self.moved()
        self.assertEqual(("failed", "failed"), (job["status"], self.steps(job)["dump"]))
        self.assertEqual(["running"] * 3, [self.state(key) for key in ("prod", "dev", "canary")])
        self.assertFalse(any(call[:2] == ["volume", "create"] for call in self.host.calls))

    def test_the_check_refuses_a_host_that_is_not_ready_and_stops_nothing(self):
        cases = []

        def eighteen():
            self.host.postgres()["image"] = "postgres:18-alpine"
        cases.append((eighteen, "runs postgres:18-alpine, not 16"))

        def leftover():
            self.host.volumes[self.project + "_postgres-pg18"] = None
        cases.append((leftover, self.project + "_postgres-pg18 exists already; review it"))

        def no_sixteen():
            del self.host.volumes[self.project + "_postgres"]
        cases.append((no_sixteen, "No 16 data in " + self.project + "_postgres"))

        def down():
            self.host.postgres()["state"] = "exited"
        cases.append((down, "The platform PostgreSQL does not run"))
        for arrange, message in cases:
            with self.subTest(message=message):
                self.host = Host(self.root, self.project, self.networks)
                self.workloads = self.add_workloads()
                arrange()
                job = self.moved()
                self.assertEqual("failed", job["status"])
                self.assertIn(message, job["reason"])
                self.assertEqual(["running"] * 3, [self.state(key) for key in ("prod", "dev", "canary")])
                self.assertFalse(any(call[0] in ("stop", "pull") for call in self.host.calls))

    def test_a_running_rollout_holds_the_move_back(self):
        with open(self.root / "wheelhouse-prod" / "lock", "a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            job = self.moved()
        self.assertEqual("failed", job["status"])
        self.assertIn("A rollout or a control runs on wheelhouse-prod", job["reason"])
        self.assertEqual(["running"] * 3, [self.state(key) for key in ("prod", "dev", "canary")])

    def test_a_move_starts_once_and_never_on_a_moved_host(self):
        job = self.moved()
        self.assertEqual("succeeded", job["status"])
        with self.assertRaisesRegex(move.Refused, "moved to 18 already"):
            move.new_job(self.root, "postgres-move:admin", "again")
        with self.assertRaisesRegex(move.Refused, "ran already"):
            move.run_job(self.root, job["id"], docker=self.host, kit=KIT)
        for reason in ("", "two\nlines", "x" * 301):
            with self.assertRaisesRegex(move.Refused, "one-line --reason"):
                move.new_job(self.root, "postgres-move:admin", reason)

    def test_status_reads_names_and_states_only(self):
        reading = move.status(self.root, docker=self.host)
        self.assertEqual(("needs-move", self.role, 16), (reading["state"], reading["role"], reading["postgres"]["major"]))
        self.assertEqual({"previous": {"name": self.project + "_postgres", "present": True},
                          "current": {"name": self.project + "_postgres-pg18", "present": False}}, reading["volumes"])
        # A journal no process carries any more reads as interrupted, with what to do.
        job = move.new_job(self.root, "postgres-move:admin", "PostgreSQL 18")
        path = self.platform / "postgres-move" / "jobs" / (job["id"] + ".json")
        path.write_text(json.dumps({**job, "status": "running"}))
        self.assertEqual("interrupted", move.status(self.root, docker=self.host)["moves"][0]["status"])


class ProductsMoveTests(MoveTests):
    """A products host: its Compose file is rendered from the template and its site nodes, so the move renders it again
    from the kit's 18 template and keeps both 16 files."""
    project, role, template, networks = "wheelhouse-products", "products", "products.compose.yml", ("platform",)

    def lay_out(self):
        (self.platform / "products.compose.yml").write_text(sixteen(self.template))
        (self.platform / "sites.json").write_text(json.dumps({"nodes": [
            {"name": "ocharo-desk-dev", "address": "172.30.0.65", "product": "ocharo-platform", "site": "app",
             "environment": "dev"}]}))
        move.prepare.render_products_compose(self.root)
        for target in ("wheelhouse-prod", "wheelhouse-dev", "wheelhousecanary-dev"):
            (self.root / target).mkdir()
            (self.root / target / "lock").touch()

    def add_workloads(self):
        names = {"prod": ("ocharo-platform-prod", "desk"), "dev": ("ocharo-platform-dev", "desk"),
                 "canary": ("ocharo-studio-dev", "studio"), "stopped": ("ocharo-studio-prod", "studio")}
        return {key: self.host.add(project, service, "ghcr.io/ocharo-hq/" + service + "@sha256:1", ["platform"],
                                   state="exited" if key == "stopped" else "running")
                for key, (project, service) in names.items()}

    def assert_switched(self):
        # Rendered again from the kit's 18 template, with the host's site node kept; both 16 files stay beside it.
        compose = (self.platform / "compose.yml").read_text()
        self.assertTrue(compose.startswith((KIT / self.template).read_text().rstrip("\n") + "\n"))
        self.assertIn("  ocharo-desk-dev:\n", compose)
        self.assertEqual((KIT / self.template).read_text(), (self.platform / "products.compose.yml").read_text())
        self.assertEqual(sixteen(self.template), (self.platform / "products.compose.pg16.yml").read_text())
        self.assertEqual(self.before, (self.platform / "compose.pg16.yml").read_text())


class GuardCliTests(unittest.TestCase):
    """`postgres-move.py check`, as bootstrap.sh, retire-test.sh and migrate-control-network.sh run it as root before
    they install the platform Compose file, against a stand-in Docker binary."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        base = Path(self.temporary.name)
        self.root, self.bin = base / "wheelhouse", base / "bin"
        self.bin.mkdir()
        self.answer("exit 0")

    def answer(self, script):
        (self.bin / "docker").write_text("#!/bin/sh\n" + script + "\n")
        (self.bin / "docker").chmod(0o755)

    def check(self, project="wheelhouse-platform"):
        return subprocess.run([sys.executable, str(KIT / "postgres-move.py"), "check", "--root", str(self.root),
                               "--project", project], capture_output=True, text=True,
                              env={**os.environ, "PATH": str(self.bin) + os.pathsep + os.environ["PATH"]})

    def test_a_fresh_host_and_a_stand_in_that_answers_exit_0_may_start_18(self):
        result = self.check()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("PostgreSQL 18 may start on this host.", result.stdout)

    def test_16_data_without_a_recorded_move_refuses_until_the_move_is_recorded(self):
        self.answer('[ "$1 $2" = "volume inspect" ] && echo "$5"')
        result = self.check()
        self.assertEqual(3, result.returncode)
        self.assertIn("Refused: PostgreSQL 16 data waits in wheelhouse-platform_postgres; 18 would start empty beside "
                      "it. Move it first: postgres-move.py move (vps.md § PostgreSQL 18)", result.stderr)
        products = self.check("wheelhouse-products")
        self.assertIn("wheelhouse-products_postgres", products.stderr)
        folder = self.root / "platform" / "postgres-move"
        folder.mkdir(parents=True)
        (folder / "moved.json").write_text(json.dumps({"from": 16, "to": 18}))
        self.assertEqual(0, self.check().returncode)

    def test_every_kit_script_asks_before_it_installs_the_platform_compose_file(self):
        guard = 'python3 "$script_dir/postgres-move.py" check --root "$deploy_root" --project wheelhouse-platform || exit 1'
        for script in ("bootstrap.sh", "retire-test.sh", "migrate-control-network.sh"):
            with self.subTest(script=script):
                text = (KIT / script).read_text()
                self.assertIn(guard, text)
                self.assertLess(text.index(guard), text.index('"$script_dir/platform.compose.yml"'))
        self.assertIn("bootstrap-products.sh postgres-move.py; do", (KIT / "bootstrap.sh").read_text())

    @unittest.skipIf(os.geteuid() == 0, "running as root would pass the deploy-account check")
    def test_a_move_needs_a_reason_and_the_project_typed(self):
        platform = self.root / "platform"
        platform.mkdir(parents=True)
        (platform / "compose.yml").write_text(sixteen("platform.compose.yml"))
        errors = io.StringIO()
        with mock.patch.object(sys, "stderr", errors):
            self.assertEqual(2, move.main(["move", "--root", str(self.root)], io.StringIO()))
        self.assertIn("Give a one-line --reason", errors.getvalue())
        with mock.patch.object(move.subprocess, "Popen") as spawned, mock.patch.object(sys, "stderr", errors), \
                mock.patch("builtins.input", lambda prompt: "wheelhouse"):
            self.assertEqual(2, move.main(["move", "--root", str(self.root), "--reason", "PostgreSQL 18"],
                                          io.StringIO()))
        spawned.assert_not_called()
        self.assertIn("Not confirmed; nothing changed", errors.getvalue())
        self.assertEqual([], list((platform / "postgres-move" / "jobs").iterdir()))


if __name__ == "__main__":
    unittest.main()
