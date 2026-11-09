"""Files migration (dev slice): Files' mover once per manifest source in one-off containers on the source target's host,
streamed into the job journal, cancellable. Fake docker throughout, as the run and reset tests use."""
import copy
import hashlib
import json
import subprocess
import sys
import unittest
from unittest.mock import patch
import files_migration
import runner
import test_controls
import transport
from test_controls import ControlDocker

IMAGE = "ghcr.io/wow-two-sdk-beta/wow-two-sdk-beta.files/files:0.3.1"
TOKEN = "import-token-SECRET-123"
PASSWORD = "PGPASSWORD-SECRET-456"
CONNECTION = "Host=postgres;Port=5432;Database=desk;Username=desk;Password=" + PASSWORD
IMPORT = "imp-2026-11-08"
JOB = "11111111-1111-4111-8111-111111111190"
FILES = "https://files.ocharo.example/"

FOLDER = {"id": "brand-media", "kind": "volume-folder", "volume": "media", "path": "uploads/brand",
          "reference": "brand:{path}", "wave": 1}
POSTGRES = {"id": "desk-media", "kind": "postgres-column", "database": "api", "table": "media", "key": ["id"],
            "content": "bytes", "reference": "desk:{id}", "name": "file_name", "type": "mime"}
SQLITE = {"id": "forms-sqlite", "kind": "sqlite-column", "volume": "data", "database": "forms/app.db", "table": "photos",
          "key": "id", "content": "blob", "reference": "forms:{id}"}


def payload(step="push", sources=None, **changes):
    document = {"step": step, "image": IMAGE, "files": FILES, "import": None if step == "plan" else IMPORT,
                "token": None if step == "plan" else TOKEN, "sources": copy.deepcopy([FOLDER, POSTGRES, SQLITE] if sources is None else sources)}
    document.update(changes)
    return document


class MoverDocker(ControlDocker):
    """Answers each source's container as `script` says: (stdout lines, exit code); records what ran."""
    script = []
    runs = []
    fetched = []
    missing = ()
    during = None

    def fetch(self, image, registry=None, unreadable=(), timeout=1800):
        self.fetched.append((image, registry, unreadable))
        return True

    def execute(self, arguments, step, timeout=600):
        if arguments[:3] == ["docker", "volume", "inspect"] and arguments[-1] in self.missing:
            raise runner.CommandFailed("docker volume inspect failed (exit 1); inspect target containers privately")
        return ""

    errors = []

    def stream(self, arguments, document, timeout, container, on_line, cancelled, on_error=None):
        self.runs.append({"arguments": arguments, "stdin": json.loads(document), "timeout": timeout, "container": container,
                          "environment": self.environments[-1]})
        lines, code = self.script[len(self.runs) - 1] if len(self.runs) <= len(self.script) else ([], 0)
        if on_error is not None:
            on_error(self.errors[len(self.runs) - 1] if len(self.runs) <= len(self.errors) else [])
        for line in lines:
            if self.during:
                self.during(len(self.runs))
            on_line(line)
            if cancelled():
                return runner.CANCELLED
        if self.during:
            self.during(len(self.runs))
        return runner.CANCELLED if cancelled() else code


def result(files, size, skipped=0, failed=0):
    return json.dumps({"result": {"files": files, "bytes": size, "skipped": skipped, "failed": failed}})


def progress(files, size):
    return json.dumps({"progress": {"files": files, "bytes": size}})


class Clock:
    def __init__(self):
        self.now = 0.0

    def __call__(self):
        return self.now


class Rig(unittest.TestCase):
    setUp_rig = test_controls.RunnerControlTests.setUp
    tearDown = test_controls.RunnerControlTests.tearDown
    deployed = test_controls.RunnerControlTests.deployed

    def setUp(self):
        self.setUp_rig()
        self.target["variables"] = {"PLATFORM_NETWORK": "platform"}
        self.settings.write_text(json.dumps({"Database": {"Connection": "x"},
                                             "ConnectionStrings": {"Database": CONNECTION + ";"}}))
        self.settings.chmod(0o600)
        compose = json.loads((self.bundle / "compose.json").read_text())
        compose["volumes"] = {"media": {}, "data": {"name": "pilot-data-volume"}, "shared": {"external": True}}
        raw = json.dumps(compose).encode()
        (self.bundle / "compose.json").write_bytes(raw)
        manifest = json.loads((self.bundle / "release.json").read_text())
        manifest["composeSha256"] = hashlib.sha256(raw).hexdigest()
        (self.bundle / "release.json").write_text(json.dumps(manifest))
        MoverDocker.script, MoverDocker.runs, MoverDocker.fetched = [], [], []
        MoverDocker.missing, MoverDocker.during = (), None

    def work(self, document=None, job=JOB, clock=None):
        return files_migration_module().work(self.target, "max", job, document or payload(), docker_factory=MoverDocker,
                                             clock=clock or Clock())

    def everything_saved(self):
        return "".join(path.read_text() for path in self.root.rglob("*.json") if path.is_file() and path != self.settings)


def files_migration_module():
    return runner.files_migration()


class ValidationTests(Rig):
    def test_a_payload_is_checked_whole_before_any_job_exists(self):
        module = files_migration_module()
        cases = [
            ("step", payload(step="delete"), "step is plan, push or verify"),
            ("image", payload(image="Files:latest"), "Invalid image reference"),
            ("image tag only", payload(image="files"), "Invalid image reference"),
            ("http url", payload(files="http://files.example/"), "https URL with a trailing slash"),
            ("no slash", payload(files="https://files.example"), "trailing slash"),
            ("query", payload(files="https://files.example/?a=1"), "https URL"),
            ("userinfo", payload(files="https://a:b@files.example/"), "https URL"),
            ("push without import", payload(**{"import": None}), "needs the import id"),
            ("push without token", payload(token=None), "needs the import token"),
            ("token on plan", payload("plan", token=TOKEN), "takes no import and no token"),
            ("no sources", payload(sources=[]), "1 to 200 sources"),
            ("unknown field", payload(extra=1), "Unknown files migration field"),
            ("source id", payload(sources=[{**FOLDER, "id": "bad id"}]), "needs an id"),
            ("duplicate id", payload(sources=[FOLDER, FOLDER]), "Duplicate source id"),
            ("kind", payload(sources=[{**FOLDER, "kind": "s3"}]), "kind is volume-folder"),
            ("folder volume", payload(sources=[{k: v for k, v in FOLDER.items() if k != "volume"}]), "needs volume"),
            ("folder path up", payload(sources=[{**FOLDER, "path": "../etc"}]), "path inside the volume"),
            ("folder path abs", payload(sources=[{**FOLDER, "path": "/etc"}]), "path inside the volume"),
            ("pg table", payload(sources=[{k: v for k, v in POSTGRES.items() if k != "table"}]), "needs table"),
            ("pg key empty list", payload(sources=[{**POSTGRES, "key": []}]), "1 to 16 columns"),
            ("pg database not a service", payload(sources=[{**POSTGRES, "database": "Not A Service"}]), "names the service"),
            ("sqlite key list", payload(sources=[{**SQLITE, "key": ["id"]}]), "needs key"),
            ("sqlite db path", payload(sources=[{**SQLITE, "database": "../x.db"}]), "path inside the volume"),
            ("bad registry", payload(registry={"server": "bad server"}), "Invalid registry sign-in"),
        ]
        for name, document, message in cases:
            with self.subTest(name), self.assertRaisesRegex(ValueError, message):
                module.validate(document)

    def test_a_valid_payload_keeps_unknown_source_fields_and_every_kind(self):
        checked = files_migration_module().validate(payload())
        self.assertEqual(1, checked["sources"][0]["wave"])
        self.assertEqual(["brand-media", "desk-media", "forms-sqlite"], files_migration_module().operation_of(checked)["sources"])
        files_migration_module().validate(payload("plan"))
        files_migration_module().validate(payload("verify"))

    def test_an_invalid_launch_queues_nothing(self):
        self.deployed()
        path = self.root / "target.json"
        runner.write_json(path, self.target)
        with patch.object(runner.subprocess, "Popen") as popen, self.assertRaisesRegex(ValueError, "https URL"):
            files_migration_module().launch(path, "max", payload(files="http://x/"), runner.__file__)
        popen.assert_not_called()
        self.assertFalse([p for p in (self.base / "jobs").glob("*.json") if "files-migration" in p.read_text()])


class RefusalTests(Rig):
    def test_prod_and_wheelhouses_own_targets_are_refused(self):
        self.deployed()
        prod = dict(self.target, environment="prod")
        with self.assertRaisesRegex(ValueError, "files-migration runs on dev targets only in this version"):
            files_migration_module().work(prod, "max", JOB, payload(), docker_factory=MoverDocker)
        own = dict(self.target, product="wheelhouse")
        with self.assertRaisesRegex(ValueError, "never run a files migration"):
            files_migration_module().work(own, "max", JOB, payload(), docker_factory=MoverDocker)
        path = self.root / "target.json"
        runner.write_json(path, prod)
        with patch.object(runner.subprocess, "Popen") as popen, self.assertRaisesRegex(ValueError, "dev targets only"):
            files_migration_module().launch(path, "max", payload(), runner.__file__)
        popen.assert_not_called()
        self.assertEqual([], MoverDocker.runs)

    def test_the_manifests_product_cannot_be_wheelhouse_either(self):
        manifest = json.loads((self.bundle / "release.json").read_text())
        manifest["product"] = "wheelhouse"
        (self.bundle / "release.json").write_text(json.dumps(manifest))
        self.target["product"] = "wheelhouse"
        with self.assertRaisesRegex(ValueError, "never run a files migration"):
            files_migration_module().work(self.target, "max", JOB, payload(), docker_factory=MoverDocker)

    def test_a_target_with_no_release_refuses(self):
        with self.assertRaisesRegex(ValueError, "runs no release yet"):
            self.work()

    def test_a_running_rollout_or_control_refuses_the_launch(self):
        self.deployed()
        path = self.root / "target.json"
        runner.write_json(path, self.target)
        with runner.deployment_lock(self.base), patch.object(runner.subprocess, "Popen") as popen, \
                self.assertRaisesRegex(ValueError, "A rollout or another control is running"):
            files_migration_module().launch(path, "max", payload(), runner.__file__)
        popen.assert_not_called()

    def test_unresolvable_sources_are_refused_before_a_job_exists(self):
        self.deployed()
        for source, message in (({**FOLDER, "volume": "nope"}, "declares no volume nope"),
                                ({**POSTGRES, "database": "billing"}, "has no service billing"),
                                ({**POSTGRES, "database": "worker"}, "worker has no settings|No settings file")):
            with self.subTest(message), self.assertRaisesRegex(ValueError, message):
                self.work(payload(sources=[source]))
        self.settings.write_text(json.dumps({"Database": {"Connection": "x"}}))
        with self.assertRaisesRegex(ValueError, "hold no ConnectionStrings:Database"):
            self.work(payload(sources=[POSTGRES]))
        self.target["variables"] = {}
        with self.assertRaisesRegex(ValueError, "no platform network"):
            self.work(payload(sources=[POSTGRES]))


class RunTests(Rig):
    def test_each_kind_gets_its_container_mounts_network_and_stdin(self):
        self.deployed()
        MoverDocker.script = [([result(3, 30)], 0), ([result(2, 20)], 0), ([result(1, 10)], 0)]
        record = self.work()
        self.assertEqual("succeeded", record["status"], record.get("reason"))
        self.assertEqual([(IMAGE, None, ())], MoverDocker.fetched)
        folder, pg, sqlite = MoverDocker.runs
        image_tail = ["--entrypoint", "dotnet", IMAGE, "Files.Api.dll", "migrate", "push"]
        common = ["--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--memory", "512m"]
        name = "pilot-dev-files-migration-" + JOB
        self.assertEqual(["docker", "run", "--rm", "-i", "--name", name, "--pull", "never", *common,
                          "--label", "wheelhouse.files-migration=pilot-dev", "-v", "pilot-dev_media:/source:ro",
                          *image_tail], folder["arguments"])
        self.assertEqual(["docker", "run", "--rm", "-i", "--name", name, "--pull", "never", "--network", "platform",
                          *common, "--label", "wheelhouse.files-migration=pilot-dev", *image_tail], pg["arguments"])
        self.assertEqual(["docker", "run", "--rm", "-i", "--name", name, "--pull", "never", *common,
                          "--label", "wheelhouse.files-migration=pilot-dev", "-v", "pilot-data-volume:/source:ro",
                          *image_tail], sqlite["arguments"])
        for run in MoverDocker.runs:
            self.assertEqual((files_migration.TIMEOUT, name), (run["timeout"], run["container"]))
            self.assertNotIn("--user", run["arguments"])
            self.assertNotIn("--privileged", run["arguments"])
        self.assertEqual({"files": FILES, "import": IMPORT, "token": TOKEN, "source": FOLDER, "connection": None,
                          "root": "/source"}, folder["stdin"])
        self.assertEqual(POSTGRES, pg["stdin"]["source"])
        self.assertEqual(CONNECTION + ";Options=-c default_transaction_read_only=on", pg["stdin"]["connection"])
        self.assertIsNone(sqlite["stdin"]["connection"])
        self.assertEqual(["brand-media", "desk-media", "forms-sqlite"], [step["name"] for step in record["steps"]])
        self.assertEqual(["3 files · 30 bytes", "2 files · 20 bytes", "1 files · 10 bytes"],
                         [step["detail"] for step in record["steps"]])
        self.assertEqual({"import": IMPORT, "results": {"brand-media": {"files": 3, "bytes": 30, "skipped": 0, "failed": 0},
                                                        "desk-media": {"files": 2, "bytes": 20, "skipped": 0, "failed": 0},
                                                        "forms-sqlite": {"files": 1, "bytes": 10, "skipped": 0, "failed": 0}}},
                         record["filesMigration"])
        self.assertEqual({"kind": "files-migration", "step": "push", "sources": ["brand-media", "desk-media", "forms-sqlite"]},
                         record["operation"])
        self.assertEqual([], ControlDocker.events)

    def test_an_external_volume_keeps_its_key_and_a_full_name_is_accepted(self):
        self.deployed()
        MoverDocker.script = [([], 0), ([], 0)]
        self.work(payload("plan", sources=[{**FOLDER, "volume": "shared"}, {**FOLDER, "id": "b", "volume": "pilot-dev_media"}]))
        self.assertEqual(["-v", "shared:/source:ro"], MoverDocker.runs[0]["arguments"][MoverDocker.runs[0]["arguments"].index("-v"):][:2])
        self.assertIn("pilot-dev_media:/source:ro", MoverDocker.runs[1]["arguments"])

    def test_a_missing_volume_fails_that_step(self):
        self.deployed()
        MoverDocker.missing = ("pilot-dev_media",)
        record = self.work(payload(sources=[FOLDER]))
        self.assertEqual("failed", record["status"])
        self.assertIn("volume media does not exist", record["reason"])
        self.assertEqual([], MoverDocker.runs)

    def test_secrets_reach_no_job_file_argv_environment_or_detail(self):
        self.deployed()
        MoverDocker.script = [([progress(1, 2), "token " + TOKEN, result(1, 2)], 0)] * 3
        record = self.work()
        self.assertEqual("succeeded", record["status"], record.get("reason"))
        saved = self.everything_saved() + json.dumps(record)
        for secret in (TOKEN, PASSWORD, CONNECTION):
            self.assertNotIn(secret, saved)
            for run in MoverDocker.runs:
                self.assertNotIn(secret, " ".join(run["arguments"]))
                self.assertNotIn(secret, json.dumps(run["environment"]))
        self.assertIn(IMPORT, saved)
        self.assertNotIn(runner.MIGRATE_KEY, MoverDocker.runs[0]["arguments"])

    def test_an_ordinary_plan_carries_no_import_and_no_token(self):
        self.deployed()
        MoverDocker.script = [([result(4, 40)], 0)]
        record = self.work(payload("plan", sources=[FOLDER]))
        self.assertEqual("succeeded", record["status"])
        run = MoverDocker.runs[0]
        self.assertEqual("plan", run["arguments"][-1])
        self.assertEqual((None, None), (run["stdin"]["import"], run["stdin"]["token"]))
        self.assertIsNone(record["filesMigration"]["import"])

    def test_the_registry_sign_in_lends_the_pull_only(self):
        self.deployed()
        login = {"server": "ghcr.io", "username": "x-access-token", "password": "REGISTRY-SECRET", "credential": "installation"}
        MoverDocker.script = [([result(0, 0)], 0)]
        record = self.work(payload(sources=[FOLDER], registry=login, unreadable=[]))
        self.assertEqual((IMAGE, login, ()), MoverDocker.fetched[0])
        self.assertNotIn("REGISTRY-SECRET", self.everything_saved() + json.dumps(record))
        self.assertNotIn("REGISTRY-SECRET", json.dumps(MoverDocker.runs))


class ProgressTests(Rig):
    def test_progress_writes_at_most_once_every_two_seconds_and_the_result_closes_the_step(self):
        self.deployed()
        clock = Clock()
        seen = []

        def tick(number):
            clock.now += 0.7
            path = self.base / "jobs" / (JOB + ".json")
            if path.is_file():
                steps = runner.read_json(path).get("steps") or []
                seen.append(steps[-1].get("detail") if steps else None)

        MoverDocker.during = staticmethod(tick)
        lines = [progress(1, 10), progress(2, 20), progress(3, 30), progress(4, 40), progress(5, 50), "not json",
                 json.dumps(["x"]), json.dumps({"other": 1}), json.dumps({"progress": {"files": -1, "bytes": 1}}),
                 result(6, 60, 1, 0)]
        MoverDocker.script = [(lines, 0)]
        record = self.work(payload(sources=[FOLDER]), clock=clock)
        self.assertEqual("succeeded", record["status"])
        # Writes land at 0.7 (first), then not before 2 s later: progress 1 and 4 only.
        written = [detail for detail in dict.fromkeys(seen) if detail]
        self.assertEqual(["1 files · 10 bytes", "4 files · 40 bytes"], written)
        self.assertEqual("6 files · 60 bytes", record["steps"][0]["detail"])
        self.assertEqual({"files": 6, "bytes": 60, "skipped": 1, "failed": 0}, record["filesMigration"]["results"]["brand-media"])

    def test_a_step_without_a_result_keeps_the_last_progress_and_no_result(self):
        self.deployed()
        MoverDocker.script = [([progress(7, 70)], 0)]
        record = self.work(payload(sources=[FOLDER]))
        self.assertEqual("7 files · 70 bytes", record["steps"][0]["detail"])
        self.assertEqual({}, record["filesMigration"]["results"])


class ExitTests(Rig):
    def test_exit_codes_decide_the_step_and_the_job(self):
        self.deployed()
        seen = []
        for code, status, expected in ((0, "succeeded", None), (1, "failed", "the mover refused brand-media"),
                                       (2, "failed", "the mover failed on brand-media (exit 2)"),
                                       (137, "failed", "the mover failed on brand-media (exit 137)"),
                                       (None, "failed", "the mover timed out on brand-media after 21600s")):
            with self.subTest(code=code):
                MoverDocker.script, MoverDocker.runs = [([], code)], []
                seen.append(code)
                record = self.work(payload(sources=[FOLDER]), job="11111111-1111-4111-8111-1111111112{:02d}".format(len(seen)))
                self.assertEqual(status, record["status"])
                self.assertEqual(expected, record.get("reason"))
                if expected:
                    self.assertEqual(("failed", expected), (record["steps"][0]["status"], record["steps"][0]["detail"]))
        self.assertEqual("ready", runner.state(self.target)["condition"], "a failed migration needs no reconciliation")

    def test_the_movers_last_error_line_ends_the_reason_scrubbed_and_without_the_token(self):
        self.deployed()
        said = "The source folder 'blobs/media' does not exist; token " + TOKEN + " at https://files.example.test/imports?sig=abc"
        MoverDocker.script, MoverDocker.runs, MoverDocker.errors = [([], 1)], [], [[said]]
        try:
            record = self.work(payload(sources=[FOLDER]), job="11111111-1111-4111-8111-111111111299")
        finally:
            MoverDocker.errors = []
        self.assertTrue(record["reason"].startswith("the mover refused brand-media: The source folder 'blobs/media' does not exist"))
        self.assertNotIn(TOKEN, json.dumps(record))
        self.assertNotIn("sig=abc", record["reason"])
        self.assertEqual(record["reason"], record["steps"][0]["detail"])

    def test_the_reason_skips_the_counts_the_mover_writes_after_its_error(self):
        self.deployed()
        MoverDocker.script, MoverDocker.runs = [([], 2)], []
        MoverDocker.errors = [["The source could not be read (SqliteException 14).", "plan: files=0 bytes=0 skipped=0 failed=0"]]
        try:
            record = self.work(payload(sources=[FOLDER]), job="11111111-1111-4111-8111-111111111298")
        finally:
            MoverDocker.errors = []
        self.assertEqual("the mover failed on brand-media (exit 2): The source could not be read (SqliteException 14).",
                         record["reason"])

    def test_the_counts_stand_as_the_reason_when_the_mover_said_nothing_else(self):
        self.assertEqual("plan: files=0 bytes=0 skipped=0 failed=0",
                         files_migration.mover_error(["", "plan: files=0 bytes=0 skipped=0 failed=0"]))
        self.assertEqual("failed blobs/a.png: missing", files_migration.mover_error(
            ["failed blobs/a.png: missing", "verify: files=1 bytes=4 skipped=0 failed=1"]))
        self.assertIsNone(files_migration.mover_error([]))

    def test_a_failed_or_refused_source_stops_the_job_and_later_sources_do_not_run(self):
        self.deployed()
        for code, reason in ((2, "the mover failed on desk-media"), (1, "the mover refused desk-media")):
            MoverDocker.script, MoverDocker.runs = [([result(1, 1)], 0), ([], code), ([result(1, 1)], 0)], []
            record = self.work(job="11111111-1111-4111-8111-11111111130" + str(code))
            self.assertEqual("failed", record["status"])
            self.assertTrue(record["reason"].startswith(reason))
            self.assertEqual(2, len(MoverDocker.runs))
            self.assertEqual(["brand-media", "desk-media"], [step["name"] for step in record["steps"]])
            self.assertEqual(["succeeded", "failed"], [step["status"] for step in record["steps"]])
            self.assertEqual(["brand-media"], list(record["filesMigration"]["results"]))

    def test_the_services_and_the_stopped_marker_are_untouched(self):
        self.deployed()
        test_controls.RunnerControlTests.control(self, "stop", ["worker"])
        ControlDocker.events = []
        MoverDocker.script = [([result(1, 1)], 0)]
        record = self.work(payload(sources=[FOLDER]))
        self.assertEqual("succeeded", record["status"], record.get("reason"))
        self.assertEqual([], ControlDocker.events)
        self.assertEqual(["worker"], runner.stopped_of(self.base)["services"])
        self.assertEqual(["worker"], record["stopped"]["services"])


class CancelTests(Rig):
    def cancel(self, job=JOB):
        return runner.control_cancel(self.target, job, "max", docker_factory=MoverDocker)

    def test_a_cancel_during_a_source_kills_the_container_and_ends_the_job_cancelled(self):
        self.deployed()
        MoverDocker.script = [([result(1, 1)], 0), ([progress(1, 1), progress(2, 2)], 0), ([result(1, 1)], 0)]

        def during(number):
            if number == 2 and not runner.cancel_requested(self.base, JOB):
                answered = self.cancel()
                self.assertTrue(answered["cancelRequested"])

        MoverDocker.during = staticmethod(during)
        record = self.work()
        self.assertEqual("cancelled", record["status"])
        self.assertNotIn("failure", record)
        self.assertEqual(["succeeded", "cancelled"], [step["status"] for step in record["steps"]])
        self.assertEqual(["brand-media", "desk-media"], [step["name"] for step in record["steps"]])
        self.assertEqual(2, len(MoverDocker.runs), "later sources are not run")
        self.assertEqual("max", record["cancelledBy"])
        saved = runner.read_json(self.base / "jobs" / (JOB + ".json"))
        self.assertEqual("cancelled", saved["status"])
        self.assertEqual("cancelled", runner.read_json(self.base / "active.json")["status"])
        self.assertFalse(runner.cancel_requested(self.base, JOB), "the marker goes with the job")
        self.assertEqual("ready", runner.state(self.target)["condition"])

    def test_a_cancel_between_sources_ends_the_job_with_the_running_step_done(self):
        self.deployed()
        MoverDocker.script = [([result(1, 1)], 0), ([], 0)]

        def during(number):
            if number == 1:
                runner.write_json(self.base / runner.FILES_CANCEL / (JOB + ".json"), {"actor": "max"})

        MoverDocker.during = staticmethod(during)
        record = self.work()
        self.assertEqual("cancelled", record["status"])
        self.assertEqual(["cancelled"], [step["status"] for step in record["steps"]])

    def test_a_cancel_before_the_worker_starts_ends_the_job_without_running_anything(self):
        self.deployed()
        runner.write_json(self.base / "jobs" / (JOB + ".json"), {"id": JOB, "status": "queued", "operation": {"kind": "files-migration"}})
        runner.write_json(self.base / runner.FILES_CANCEL / (JOB + ".json"), {"actor": "max"})
        record = self.work()
        self.assertEqual(("cancelled", []), (record["status"], MoverDocker.runs))
        self.assertEqual([], record["steps"])

    def test_a_cancel_with_no_live_worker_ends_the_job_itself_and_removes_the_container(self):
        self.deployed()
        removed = []
        original = MoverDocker.execute
        MoverDocker.execute = lambda self, arguments, step, timeout=600: removed.append(arguments) or ""
        self.addCleanup(setattr, MoverDocker, "execute", original)
        record = {"id": JOB, "status": "running", "operation": {"kind": "files-migration", "step": "push", "sources": ["a"]},
                  "steps": [{"name": "a", "status": "running", "startedAt": runner.now()}]}
        runner.write_json(self.base / "jobs" / (JOB + ".json"), record)
        runner.write_json(self.base / "active.json", record)
        answered = self.cancel()
        self.assertEqual({"id": JOB, "status": "cancelled", "cancelRequested": True}, answered)
        self.assertIn(["docker", "rm", "--force", "pilot-dev-files-migration-" + JOB], removed)
        saved = runner.read_json(self.base / "jobs" / (JOB + ".json"))
        self.assertEqual(("cancelled", "cancelled"), (saved["status"], saved["steps"][0]["status"]))
        self.assertIn("completedAt", saved)
        self.assertEqual("cancelled", runner.read_json(self.base / "active.json")["status"])

    def test_only_a_queued_or_running_files_migration_cancels(self):
        self.deployed()
        for record, message in (({"id": JOB, "status": "succeeded", "operation": {"kind": "files-migration"}}, "already finished"),
                                ({"id": JOB, "status": "running", "operation": {"kind": "run"}}, "Only a files migration")):
            runner.write_json(self.base / "jobs" / (JOB + ".json"), record)
            with self.subTest(message), self.assertRaisesRegex(ValueError, message):
                self.cancel()
        with self.assertRaisesRegex(ValueError, "No such job"):
            self.cancel("11111111-1111-4111-8111-111111111999")
        self.assertFalse(runner.cancel_requested(self.base, JOB))

    def test_a_cancel_while_the_worker_holds_the_lock_leaves_the_job_to_it(self):
        self.deployed()
        record = {"id": JOB, "status": "running", "operation": {"kind": "files-migration"}, "steps": []}
        runner.write_json(self.base / "jobs" / (JOB + ".json"), record)
        with runner.deployment_lock(self.base):
            self.cancel()
        self.assertEqual("running", runner.read_json(self.base / "jobs" / (JOB + ".json"))["status"])
        self.assertTrue(runner.cancel_requested(self.base, JOB))


class LaunchTests(Rig):
    def test_a_launch_queues_the_job_and_sends_the_payload_on_the_workers_stdin_only(self):
        self.deployed()
        path = self.root / "target.json"
        runner.write_json(path, self.target)
        with patch.object(runner.subprocess, "Popen") as popen:
            queued = files_migration_module().launch(path, "max", payload(), runner.__file__)
        argv = popen.call_args.args[0]
        self.assertEqual(["control", "--target", str(path.resolve()), "--operation", "files-migration", "--actor", "max",
                          "--job", queued["id"], "--stdin"], argv[2:])
        self.assertEqual(subprocess.PIPE, popen.call_args.kwargs["stdin"])
        self.assertEqual(payload(), json.loads(popen.return_value.stdin.write.call_args.args[0]))
        job = runner.read_json(self.base / "jobs" / (queued["id"] + ".json"))
        self.assertEqual(("queued", {"kind": "files-migration", "step": "push", "sources": ["brand-media", "desk-media", "forms-sqlite"]}),
                         (job["status"], job["operation"]))
        for secret in (TOKEN, PASSWORD):
            self.assertNotIn(secret, json.dumps(job))
            self.assertNotIn(secret, " ".join(argv))

    def test_the_worker_cli_reads_the_payload_from_stdin_and_a_failed_start_writes_the_operation(self):
        self.deployed()
        path = self.root / "target.json"
        runner.write_json(path, dict(self.target, environment="prod", root=self.target["root"]))
        with patch.object(sys, "argv", ["runner.py", "control", "--target", str(path), "--operation", "files-migration",
                                        "--actor", "max", "--job", JOB, "--stdin"]), \
                patch.object(sys, "stdin", __import__("io").StringIO(json.dumps(payload()))), \
                patch("builtins.print"), patch.object(sys, "stderr"):
            self.assertEqual(1, runner.main())
        refused = runner.read_json(self.base.parent / "pilot-prod" / "jobs" / (JOB + ".json"))
        self.assertEqual("rejected", refused["status"])
        self.assertEqual("files-migration", refused["operation"]["kind"])
        self.assertEqual(["brand-media", "desk-media", "forms-sqlite"], refused["operation"]["sources"])
        self.assertNotIn(TOKEN, json.dumps(refused))


class StreamTests(unittest.TestCase):
    """The real Docker.stream over an ordinary process in place of `docker run`."""

    def stream(self, code, document=b"hello\n", timeout=20, cancelled=lambda: False):
        lines = []
        docker = runner.Docker({"product": "pilot", "environment": "dev"}, {"PATH": "/usr/bin:/bin"})
        return docker.stream([sys.executable, "-c", code], document, timeout, "box", lines.append, cancelled), lines

    def test_lines_arrive_as_printed_stdin_is_passed_and_the_exit_code_returned(self):
        code, lines = self.stream("import sys; d=sys.stdin.read(); print('a'); print('{\"echo\":\"'+d.strip()+'\"}'); "
                                  "sys.stderr.write('SECRET'); sys.stdout.write('tail'); sys.exit(2)")
        self.assertEqual(2, code)
        self.assertEqual(["a", "{\"echo\":\"hello\"}", "tail"], lines)

    def test_on_error_gets_the_stderr_lines_and_stdout_stays_apart(self):
        said = []
        docker = runner.Docker({"product": "pilot", "environment": "dev"}, {"PATH": "/usr/bin:/bin"})
        code = docker.stream([sys.executable, "-c", "import sys; print('out'); sys.stderr.write('first\\nlast line\\n\\n'); sys.exit(1)"],
                             b"", 20, "box", (lines := []).append, lambda: False, on_error=said.append)
        self.assertEqual((1, ["out"], [["first", "last line"]]), (code, lines, said))

    def test_a_run_past_its_timeout_returns_none_and_a_cancel_returns_cancelled(self):
        code, _ = self.stream("import time; time.sleep(30)", timeout=0.5)
        self.assertIsNone(code)
        flags = iter([False, False, True])
        code, _ = self.stream("import time; time.sleep(30)", cancelled=lambda: next(flags, True))
        self.assertEqual(runner.CANCELLED, code)


class TransportTests(unittest.TestCase):
    setUp = test_controls.TransportControlTests.setUp
    tearDown = test_controls.TransportControlTests.tearDown

    def fake_ssh(self):
        commands, inputs, copies = [], [], []

        class FakeSsh:
            def __init__(self, ssh_config):
                pass

            def run(self, command, stdin=None, timeout=30):
                commands.append(command)
                inputs.append(stdin)
                return json.dumps({"id": "22222222-2222-4222-8222-222222222222", "status": "queued",
                                   "release": "v1"}) if "control-launch" in command else ""

            def copy(self, files, destination):
                copies.append([Path(file).name for file in files])

        return FakeSsh, commands, inputs, copies

    def submit(self, document=None, config=None):
        fake, commands, inputs, copies = self.fake_ssh()
        login = {"server": "ghcr.io", "username": "x", "password": "REGISTRY-SECRET", "credential": "installation"}
        with patch.object(transport.fleet, "resolve_target", return_value=config or self.config), \
                patch.object(transport, "Ssh", fake), \
                patch.object(transport.registry, "sign_in_for", return_value=(login, None)), \
                patch.object(transport.registry, "note_packages"), \
                patch.object(transport.registry, "unreadable_images", return_value=[]):
            submitted = transport.files_migration(self.root, "pilot-dev", "max", "first move", document or payload())
        return submitted, commands, inputs, copies

    def test_the_payload_travels_on_ssh_stdin_with_the_pull_sign_in_and_only_ids_are_recorded(self):
        submitted, commands, inputs, copies = self.submit()
        self.assertEqual({"kind": "files-migration", "step": "push", "sources": ["brand-media", "desk-media", "forms-sqlite"]},
                         submitted["operation"])
        self.assertTrue(commands[-1].endswith("--operation files-migration --actor max --stdin"), commands[-1])
        sent = json.loads(inputs[-1])
        self.assertEqual((TOKEN, "REGISTRY-SECRET"), (sent["token"], sent["registry"]["password"]))
        self.assertEqual(payload()["sources"], sent["sources"])
        self.assertEqual([["runner.py", "files_migration.py", "target.json"]], copies)
        for command in commands:
            for secret in (TOKEN, PASSWORD, "REGISTRY-SECRET"):
                self.assertNotIn(secret, command)
        record = (self.root / "jobs" / (submitted["id"] + ".json")).read_text()
        for secret in (TOKEN, "REGISTRY-SECRET"):
            self.assertNotIn(secret, record)
        self.assertEqual("files-migration", transport.jobs(self.root, controls=True)[0]["operation"]["kind"])

    def test_prod_wheelhouse_and_a_bad_payload_are_refused_before_ssh(self):
        prod = {**self.config, "target": {**self.config["target"], "environment": "prod"}}
        own = {**self.config, "self": True}
        for config, document, message in ((prod, None, "dev targets only"), (own, None, "never run a files migration"),
                                          (None, payload(step="x"), "step is plan")):
            with self.subTest(message), self.assertRaisesRegex(ValueError, message):
                self.submit(document, config)
        self.assertFalse((self.root / "jobs").exists() and list((self.root / "jobs").glob("*.json")))

    def test_a_cancel_asks_the_target_runner_by_its_job_and_an_unknown_job_is_refused(self):
        local, remote = "33333333-3333-4333-8333-333333333333", "44444444-4444-4444-8444-444444444444"
        transport.write_json(self.root / "jobs" / (local + ".json"), {
            "id": local, "targetId": "pilot-dev", "remoteJobId": remote, "status": "running",
            "operation": {"kind": "files-migration", "step": "push", "sources": ["a"]}})
        calls = []

        def fake_remote(ssh, action, target, *arguments, **options):
            calls.append((action, arguments))
            return {"id": remote, "status": "running", "cancelRequested": True}

        with patch.object(transport.fleet, "resolve_target", return_value=self.config), \
                patch.object(transport, "run_remote", side_effect=fake_remote), \
                patch.object(transport, "refresh_status", return_value=None):
            self.assertEqual({"id": local, "status": "running", "cancelRequested": True},
                             transport.control_cancel(self.root, "pilot-dev", local, "max"))
            for target, job in (("pilot-prod", local), ("pilot-dev", remote)):
                with self.subTest(target=target), self.assertRaisesRegex(ValueError, "No files migration of"):
                    transport.control_cancel(self.root, target, job, "max")
        self.assertEqual([("control-cancel", ("--job", remote, "--actor", "max"))], calls)

    def test_the_cli_takes_the_new_actions(self):
        with patch.object(sys, "argv", ["transport.py", "--root", str(self.root), "control-cancel", "--target", "pilot-dev",
                                        "--job", "x", "--actor", "max"]), patch("builtins.print"), patch.object(sys, "stderr"):
            self.assertEqual(1, transport.main())


from pathlib import Path  # noqa: E402

if __name__ == "__main__":
    unittest.main()
