"""The migrate step and the rollback gate (migrations spec § 3.4): the rig as runner tests.

A fake PostgreSQL keeps one set's history; each image embeds the migrations of its release and answers `migrate` and
`migrate check --json` the way the be sdk's verb tree does: exits 0 done or current, 1 refused, 2 failed, 3 busy, and a
`migrate-result` line last. The rollout around it is the runner's own, on the fake Docker the other runner tests use.
"""
import base64
import contextlib
import hashlib
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import history
import runner
import transport
from test_runner import FakeDocker

DATABASE = "Host=wheelhouse-postgres;Port=5432;Database=pilot_dev;Username=pilot_dev;Password=DO_NOT_LOG"
COMMAND = ["dotnet", "Pilot.Api.dll", "migrate"]


def result_line(verb, outcome, state, before, after, applied=(), message=None):
    sets = [{"name": "default", "schema": "public", "status": outcome, "state": state, "levelBefore": before,
             "levelAfter": after, "applied": [{"label": label, "kind": kind} for label, kind in applied],
             "rollbackFloor": None, "background": [], "lockWaitMs": 0}]
    if message:
        sets[0]["message"] = message
    return "migrate-result " + json.dumps({"verb": verb, "outcome": outcome, "sets": sets})


class Rig:
    """The fake database and the images' embedded migrations: `images` maps an image to its release's migrations
    [(ordinal, label, kind)], `history` is what the database applied, `broken` names scripts that fail and `held` how
    many runs find the set's lock held. `forced` answers the next runs as given, before any of that."""
    images, labels, history, broken, held, forced, calls = {}, {}, [], set(), 0, [], []

    @classmethod
    def reset(cls):
        cls.images, cls.labels, cls.history, cls.broken, cls.held, cls.forced, cls.calls = {}, {}, [], set(), 0, [], []

    @classmethod
    def run(cls, image, check):
        embedded = cls.images[image]
        level = max((ordinal for ordinal, _, _ in embedded), default=0)
        applied = {ordinal: (label, kind) for ordinal, label, kind in cls.history}
        before = max(applied, default=0)
        ahead = [kind for ordinal, (_, kind) in applied.items() if ordinal > level]
        incompatible = any(kind != "expand" for kind in ahead)
        if check:
            if before < level:
                return 1, result_line("check", "refused", "behind", before, before)
            if ahead:
                state = "ahead-incompatible" if incompatible else "ahead-compatible"
                return (1, result_line("check", "refused", state, before, before, message="003 is a contract above "
                                       "this release; Password=DO_NOT_LOG")) if incompatible else (
                    0, result_line("check", "current", state, before, before))
            return 0, result_line("check", "current", "current", before, before)
        if cls.held:
            cls.held -= 1
            return 3, result_line("migrate", "busy", None, before, before)
        if incompatible:
            return 1, result_line("migrate", "refused", "ahead-incompatible", before, before)
        pending = [entry for entry in embedded if entry[0] not in applied]
        if any(label in cls.broken for _, label, _ in pending):
            return 2, result_line("migrate", "failed", "behind", before, before,
                                  message="Migration 002-broken failed; rolled back 002-broken")
        cls.history += pending
        return 0, result_line("migrate", "done" if pending else "current", "current", before,
                              max(applied | {ordinal: None for ordinal, _, _ in pending}, default=0),
                              [(label, kind) for _, label, kind in pending])


class MigrateDocker(FakeDocker):
    def migrate(self, arguments, variables, timeout, container):
        image = arguments[arguments.index("--entrypoint") + 2]
        check = arguments[-2:] == ["check", "--json"]
        Rig.calls.append({"arguments": arguments, "variables": variables, "timeout": timeout, "container": container})
        self.events.append(("check:" if check else "migrate:") + Rig.labels[image])
        if Rig.forced:
            return Rig.forced.pop(0)
        return Rig.run(image, check)


class MigrateStepTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        settings = self.root / "settings.json"
        settings.write_text(json.dumps({"Database": {"Connection": "DO_NOT_LOG"}}))
        settings.chmod(0o600)
        self.settings = settings
        self.target = {"product": "pilot", "environment": "dev", "root": str(self.root / "state"),
                       "settings": {"api": str(settings)}, "minimumFreeBytes": 0,
                       "variables": {"PLATFORM_NETWORK": "platform"}}
        self.bundle = self.root / "bundle"
        self.bundle.mkdir()
        self.manifest = {"schemaVersion": 1, "product": "pilot", "release": "v1", "sourceCommit": "a" * 40,
                         "platform": "linux/amd64", "rollbackCompatible": False, "images": {},
                         "requiredConfiguration": {"api": ["Database:Connection"]}}
        self.compose = {"services": {"api": {
            "image": None, "platform": "linux/amd64", "mem_limit": "256m", "init": True,
            "healthcheck": {"test": ["CMD", "curl", "-f", "http://localhost/health"]},
            "volumes": [{"type": "bind", "source": "${API_SETTINGS:?Set API_SETTINGS}",
                         "target": "/app/appsettings.Target.json", "read_only": True},
                        {"type": "volume", "source": "keys", "target": "/data/keys"}]}},
            "volumes": {"keys": {}}}
        FakeDocker.events, FakeDocker.fail, FakeDocker.modes, FakeDocker.health = [], None, [], {}
        Rig.reset()
        pause = patch.object(runner, "MIGRATE_BUSY_PAUSE", 0)
        pause.start()
        self.addCleanup(pause.stop)

    def tearDown(self):
        self.temporary.cleanup()

    @property
    def base(self):
        return self.root / "state" / ("pilot-" + self.target["environment"])

    def release(self, label, letter, migrations, compatible=False, migrate=True):
        """A release of the api whose image embeds `migrations` [(ordinal, label, kind)] of the default set."""
        image = "ghcr.io/owner/api@sha256:" + letter * 64
        Rig.images[image], Rig.labels[image] = list(migrations), label
        self.manifest.update(release=label, rollbackCompatible=compatible, images={"api": image})
        for key in ("migrate", "migrationSets", "migrations"):
            self.manifest.pop(key, None)
        if migrate:
            self.manifest.update(migrate=[{"service": "api", "command": COMMAND, "timeoutSeconds": 120}],
                                 migrationSets={"api": [{"name": "default", "schema": "public", "ownerKind": "product",
                                                         "level": max((o for o, _, _ in migrations), default=0)}]},
                                 migrations=True)
        self.compose["services"]["api"]["image"] = image
        compose = json.dumps(self.compose).encode()
        (self.bundle / "compose.json").write_bytes(compose)
        self.manifest["composeSha256"] = hashlib.sha256(compose).hexdigest()
        (self.bundle / "release.json").write_text(json.dumps(self.manifest))

    def apply(self, docker=MigrateDocker):
        return runner.apply(self.bundle, self.target, "test-operator", docker_factory=docker)

    def flow(self):
        return [event for event in FakeDocker.events
                if event.split(":")[0] in ("pull", "migrate", "check", "up", "verify")]

    def step(self, result, name):
        return next(step for step in result["steps"] if step["name"] == name)

    def current(self):
        return runner.read_json(self.base / "current.json")["release"]

    # ---- the rig ----

    def test_a_release_adding_a_migration_deploys(self):
        self.release("v1", "1", [(1, "001-pings", "expand")])
        first = self.apply()
        self.assertEqual("succeeded", first["status"])
        self.release("v2", "2", [(1, "001-pings", "expand"), (2, "002-notes", "expand")])
        result = self.apply()
        self.assertEqual("succeeded", result["status"])
        self.assertEqual([1, 2], [ordinal for ordinal, _, _ in Rig.history])
        names = [step["name"] for step in result["steps"]]
        # After the pull, before the first wave.
        self.assertEqual(names.index("Pull images") + 1, names.index("Migrate api"))
        self.assertLess(names.index("Migrate api"), names.index("Start containers"))
        self.assertEqual("default 1 → 2: 1 applied (002-notes, expand) in 0s", self.step(result, "Migrate api")["detail"])
        [entry] = result["migrations"]
        self.assertEqual(("api", "migrate", "applied", 0, 1, True), tuple(
            entry[key] for key in ("service", "verb", "status", "exitCode", "attempts", "reported")))
        self.assertEqual([{"label": "002-notes", "kind": "expand"}], entry["sets"][0]["applied"])
        known = runner.state(self.target)["migrations"]["sets"]
        self.assertEqual([("default", 2, "002-notes", "expand", "v2", "migrate")],
                         [(item["name"], item["level"], item["newestLabel"], item["newestKind"], item["release"],
                           item["source"]) for item in known])

    def test_a_broken_script_fails_while_the_old_release_serves(self):
        self.release("v1", "1", [(1, "001-pings", "expand")])
        self.apply()
        Rig.broken.add("002-broken")
        self.release("v2", "2", [(1, "001-pings", "expand"), (2, "002-broken", "expand")])
        FakeDocker.events = []
        result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertFalse(result.get("mutationStarted", False))
        self.assertEqual(["pull", "migrate:v2"], self.flow())
        self.assertEqual("v1", self.current())
        self.assertTrue(result["reason"].startswith("Migrate api: Failed (exit 2): Migration 002-broken failed"))
        self.assertTrue(result["reason"].endswith("no service was restarted"))
        self.assertEqual("failed", self.step(result, "Migrate api")["status"])
        self.assertEqual({"api": ("updated", "not_started")},
                         {service["name"]: (service["change"], service["outcome"])
                          for service in result["serviceRollout"]["services"]})
        # Nothing to reconcile: no container changed, so the next deploy goes ahead.
        Rig.broken.clear()
        self.assertEqual("succeeded", self.apply()["status"])

    def test_a_rollback_onto_an_expand_boots(self):
        self.release("v1", "1", [(1, "001-pings", "expand")])
        self.apply()
        self.release("v2", "2", [(1, "001-pings", "expand"), (2, "002-notes", "expand")])
        self.apply()
        self.release("v1", "1", [(1, "001-pings", "expand")])
        FakeDocker.events = []
        result = self.apply()
        self.assertEqual("succeeded", result["status"])
        # The older image's check, never its apply; the database keeps 002.
        self.assertEqual(["pull", "check:v1", "up:v1", "verify:v1"], self.flow())
        self.assertEqual([1, 2], [ordinal for ordinal, _, _ in Rig.history])
        gate = self.step(result, "Rollback gate api")
        self.assertEqual("succeeded", gate["status"])
        self.assertEqual("v1 may run on this database: default at 2, ahead-compatible in 0s", gate["detail"])
        self.assertEqual(["dotnet", "Pilot.Api.dll", "migrate", "check", "--json"],
                         Rig.calls[-1]["arguments"][-6:-5] + Rig.calls[-1]["arguments"][-4:])
        self.assertEqual("pilot-dev-check-api-" + result["id"][:8], Rig.calls[-1]["container"])

    def test_a_rollback_onto_a_contract_is_refused_naming_the_snapshot(self):
        self.target["environment"] = "prod"
        self.release("v1", "1", [(1, "001-pings", "expand")])
        snapshots = iter([None, {"path": "/srv/state/pilot-prod/snapshots/before-v2", "databases": [], "volumes": []},
                          {"path": "/srv/state/pilot-prod/snapshots/before-return", "databases": [], "volumes": []}])
        with patch.object(runner, "take_snapshot", side_effect=lambda *arguments: next(snapshots)):
            self.apply()
            self.release("v2", "2", [(1, "001-pings", "expand"), (2, "002-drop-notes", "contract")])
            self.assertEqual("succeeded", self.apply()["status"])
            self.release("v1", "1", [(1, "001-pings", "expand")])
            FakeDocker.events = []
            result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertEqual(["pull", "check:v1"], self.flow())
        self.assertEqual("v2", self.current())
        self.assertIn("Rollback gate: v1's api image may not run on this database (Refused (exit 1): 003 is a contract",
                      result["reason"])
        # The snapshot taken before the rollout that applied the contract, not this one's.
        self.assertTrue(result["reason"].endswith(
            "restore the pre-deploy snapshot /srv/state/pilot-prod/snapshots/before-v2 or deploy forward"))
        self.assertIn("Password=***", result["migrations"][0]["message"])
        self.assertNotIn("DO_NOT_LOG", json.dumps(result))

    def test_a_held_lock_exits_3_retries_once_then_fails_busy(self):
        self.release("v1", "1", [(1, "001-pings", "expand")])
        Rig.held = 2
        result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertEqual(["pull", "migrate:v1", "migrate:v1"], self.flow())
        [entry] = result["migrations"]
        self.assertEqual(("busy", 3, 2), (entry["status"], entry["exitCode"], entry["attempts"]))
        self.assertTrue(result["reason"].startswith("Migrate api: Busy: the set's lock stayed held on both attempts"))
        self.assertFalse((self.base / "current.json").exists())
        # One held run only: the retry applies.
        Rig.held = 1
        retried = self.apply()
        self.assertEqual("succeeded", retried["status"])
        self.assertEqual(2, retried["migrations"][0]["attempts"])

    # ---- acceptance ----

    def test_the_step_runs_on_every_forward_deploy_even_when_no_image_changed(self):
        self.release("v1", "1", [(1, "001-pings", "expand")])
        self.apply()
        FakeDocker.events = []
        result = self.apply()
        self.assertEqual("succeeded", result["status"])
        self.assertEqual("unchanged", result["serviceRollout"]["services"][0]["change"])
        self.assertEqual(["pull", "migrate:v1", "up:v1", "verify:v1"], self.flow())
        self.assertEqual(("current", "default at 1, current in 0s"),
                         (result["migrations"][0]["status"], self.step(result, "Migrate api")["detail"]))

    def test_items_run_in_declared_order_before_the_first_wave(self):
        self.release("v1", "1", [(1, "001-pings", "expand")])
        worker = "ghcr.io/owner/worker@sha256:" + "9" * 64
        Rig.images[worker], Rig.labels[worker] = [(1, "001-jobs", "expand")], "worker-v1"
        self.manifest["images"]["worker"] = worker
        self.compose["services"]["worker"] = {"image": worker, "platform": "linux/amd64", "depends_on": ["api"],
                                              "healthcheck": {"test": ["CMD", "true"]}}
        self.manifest["migrate"] = [{"service": "worker", "command": ["worker", "migrate"], "timeoutSeconds": 30},
                                    {"service": "api", "command": COMMAND, "timeoutSeconds": 120}]
        compose = json.dumps(self.compose).encode()
        (self.bundle / "compose.json").write_bytes(compose)
        self.manifest["composeSha256"] = hashlib.sha256(compose).hexdigest()
        (self.bundle / "release.json").write_text(json.dumps(self.manifest))
        result = self.apply()
        self.assertEqual("succeeded", result["status"])
        self.assertEqual(["pull", "migrate:worker-v1", "migrate:v1", "up:v1[api]", "up:v1[worker]", "up:v1"],
                         [event for event in self.flow() if not event.startswith("verify")])
        self.assertEqual([30, 120], [call["timeout"] for call in Rig.calls])
        names = [step["name"] for step in result["steps"]]
        self.assertLess(names.index("Migrate api"), names.index("Start api"))

    def test_a_pinned_image_of_another_owner_deploys_and_migrates_first_like_any_service(self):
        # M77: a per-product Identity from its own repository, pinned by tag; the bundle names it by digest and versions
        # it by the tag, and its item runs before the items whose sets key into its set.
        self.release("v1", "1", [(1, "001-pings", "expand")])
        identity = "ghcr.io/wow-two-sdk-beta/wow-two-sdk-beta.identity/identity@sha256:" + "e" * 64
        Rig.images[identity], Rig.labels[identity] = [], "identity-0.3.0"
        self.manifest["images"]["identity"] = identity
        self.manifest["versions"] = {"identity": {"version": "0.3.0", "changedIn": "v1"},
                                     "api": {"version": "1.0.0", "changedIn": "v1"}}
        self.compose["services"]["identity"] = {"image": identity, "platform": "linux/amd64",
                                                "healthcheck": {"test": ["CMD", "curl", "-f", "http://localhost/health"]}}
        self.compose["services"]["api"]["depends_on"] = {"identity": {"condition": "service_healthy"}}
        self.manifest["migrate"].insert(0, {"service": "identity", "command": ["dotnet", "Identity.Api.dll", "migrate"],
                                            "timeoutSeconds": 300})
        self.manifest["migrationSets"]["identity"] = [{"name": "identity", "schema": "identity", "level": 0}]
        compose = json.dumps(self.compose).encode()
        (self.bundle / "compose.json").write_bytes(compose)
        self.manifest["composeSha256"] = hashlib.sha256(compose).hexdigest()
        (self.bundle / "release.json").write_text(json.dumps(self.manifest))
        result = self.apply()
        self.assertEqual("succeeded", result["status"])
        self.assertEqual(["pull", "migrate:identity-0.3.0", "migrate:v1", "up:v1[identity]", "up:v1[api]", "up:v1"],
                         [event for event in self.flow() if not event.startswith("verify")])
        self.assertEqual("0.3.0", next(service["to"]["version"] for service in result["serviceRollout"]["services"]
                                       if service["name"] == "identity"))
        self.assertEqual(identity, Rig.calls[0]["arguments"][Rig.calls[0]["arguments"].index("--entrypoint") + 2])

    def test_the_container_carries_the_service_and_the_migrate_key_by_name_only(self):
        self.target.update(render=True, settings={})
        self.manifest["requiredConfiguration"] = {"api": ["ConnectionStrings:Default"]}
        self.manifest["needs"] = {"api": ["postgres"]}
        self.manifest["dotnetEnvironment"] = True
        folder = self.base.parent.parent / "state" / "config" / "pilot-dev"
        folder.mkdir(parents=True, mode=0o700)
        (folder / "database.json").write_text(json.dumps({"ConnectionString": DATABASE}))
        (folder / "database.json").chmod(0o600)
        self.release("v1", "1", [(1, "001-pings", "expand")])
        result = self.apply()
        self.assertEqual("succeeded", result["status"])
        [call] = Rig.calls
        arguments = call["arguments"]
        self.assertEqual(["docker", "run", "--rm", "--name", "pilot-dev-migrate-api-" + result["id"][:8], "--pull", "never",
                          "--network", "platform", "--security-opt", "no-new-privileges:true", "--cap-drop", "ALL",
                          "--label", "wheelhouse.migrate=pilot-dev", "--memory", "256m", "--init"], arguments[:18])
        self.assertEqual(["-e", "ASPNETCORE_ENVIRONMENT=Development", "-e", "DEPLOY_ENVIRONMENT=dev",
                          "-e", "DOTNET_ENVIRONMENT=Development", "-e", "Migrations__ConnectionString"], arguments[18:26])
        self.assertEqual(["-v", str(folder / "api.target.json") + ":/app/appsettings.Target.json:ro",
                          "-v", "pilot-dev_keys:/data/keys"], arguments[26:30])
        self.assertEqual(["--entrypoint", "dotnet", self.manifest["images"]["api"], "Pilot.Api.dll", "migrate"],
                         arguments[30:])
        # The key's value travels in the docker client's environment only; the record never holds it.
        self.assertEqual({"Migrations__ConnectionString": DATABASE}, call["variables"])
        self.assertNotIn("DO_NOT_LOG", json.dumps(arguments))
        self.assertTrue(result["migrations"][0]["migrateKey"])
        self.assertNotIn("DO_NOT_LOG", (self.base / "jobs" / (result["id"] + ".json")).read_text())
        self.assertNotIn("DO_NOT_LOG", (self.base / "migrations.json").read_text())

    def test_without_a_database_or_postgres_need_the_sets_connect_through_their_settings(self):
        self.release("v1", "1", [(1, "001-pings", "expand")])
        result = self.apply()
        [call] = Rig.calls
        self.assertEqual({}, call["variables"])
        self.assertNotIn("Migrations__ConnectionString", call["arguments"])
        self.assertFalse(result["migrations"][0]["migrateKey"])
        # The host file stays the service's settings file.
        self.assertIn(str(self.settings) + ":/app/appsettings.Target.json:ro", call["arguments"])

    def test_a_volume_not_created_yet_is_left_out(self):
        class NoVolumes(MigrateDocker):
            def execute(self, arguments, step, timeout=600):
                if arguments[:3] == ["docker", "volume", "inspect"]:
                    raise runner.CommandFailed("docker volume inspect failed (exit 1)")
                return super().execute(arguments, step, timeout)

        self.release("v1", "1", [(1, "001-pings", "expand")])
        self.assertEqual("succeeded", self.apply(NoVolumes)["status"])
        self.assertNotIn("pilot-dev_keys:/data/keys", Rig.calls[0]["arguments"])

    def test_each_failure_stops_before_any_restart_with_its_reason(self):
        cases = [
            ((1, result_line("migrate", "refused", "drifted", 1, 1, message="001-pings drifted")), "refused",
             "Migrate api: Refused (exit 1): 001-pings drifted in 0s; no service was restarted"),
            ((2, result_line("migrate", "failed", "behind", 1, 1, message="42P01: relation \"x\" does not exist")),
             "failed", "Migrate api: Failed (exit 2): 42P01: relation \"x\" does not exist in 0s; no service was restarted"),
            ((139, None), "failed", "Migrate api: Crashed (exit 139) in 0s; no service was restarted"),
            ((125, None), "failed", "Migrate api: Docker could not start it (exit 125); no service was restarted"),
            ((None, None), "timed out",
             "Migrate api: Timed out after 120s; the container was stopped; no service was restarted"),
            ((2, "migrate-result {not json"), "failed", "Migrate api: Failed (exit 2) in 0s; no service was restarted"),
        ]
        self.release("v1", "1", [(1, "001-pings", "expand")])
        self.apply()
        self.release("v2", "2", [(1, "001-pings", "expand"), (2, "002-notes", "expand")])
        for answer, status, message in cases:
            with self.subTest(status=status, answer=answer[0]):
                Rig.forced = [answer]
                FakeDocker.events = []
                result = self.apply()
                self.assertEqual("failed", result["status"])
                self.assertEqual(message, result["reason"])
                self.assertEqual(status, result["migrations"][0]["status"])
                self.assertFalse(result.get("mutationStarted", False))
                self.assertEqual(["pull", "migrate:v2"], self.flow())
                self.assertEqual("v1", self.current())
                self.assertEqual("ready", runner.state(self.target)["condition"])

    def test_a_malformed_or_missing_result_line_is_tolerated_on_success(self):
        self.release("v1", "1", [(1, "001-pings", "expand")])
        for line in (None, "migrate-result {broken", "migrate-result []"):
            with self.subTest(line=line):
                Rig.forced = [(0, line)]
                result = self.apply()
                self.assertEqual("succeeded", result["status"])
                [entry] = result["migrations"]
                self.assertEqual(("current", False, []), (entry["status"], entry["reported"], entry["sets"]))
                self.assertEqual("Exit 0, no migrate-result line in 0s", self.step(result, "Migrate api")["detail"])

    def test_the_gate_guards_the_automatic_restore(self):
        for kind, outcome in (("expand", "rolled_back"), ("contract", "failed")):
            with self.subTest(kind=kind):
                Rig.reset()
                self.release("v1", "1", [(1, "001-pings", "expand")])
                self.apply()
                # The release does not declare its predecessors compatible; the gate decides alone.
                self.release("v2", "2", [(1, "001-pings", "expand"), (2, "002-notes", kind)])
                FakeDocker.fail = "v2"
                FakeDocker.events = []
                result = self.apply()
                FakeDocker.fail = None
                self.assertEqual(outcome, result["status"])
                gate = self.step(result, "Rollback gate api")
                if outcome == "rolled_back":
                    self.assertEqual(["pull", "migrate:v2", "up:v2", "check:v1", "pull", "up:v1", "verify:v1"],
                                     self.flow())
                    self.assertEqual("succeeded", gate["status"])
                    self.assertEqual("v1", self.current())
                else:
                    self.assertEqual(["pull", "migrate:v2", "up:v2", "check:v1"], self.flow())
                    self.assertEqual("failed", gate["status"])
                    self.assertIn("; the roll back was refused: Rollback gate: v1's api image may not run", result["reason"])
                    self.assertTrue(result["reason"].endswith("(dev takes none); deploy forward"))
                    self.assertNotIn("Roll back to v1", [step["name"] for step in result["steps"]])
                    self.assertEqual("restore", result["migrations"][-1]["purpose"])
                    runner.acknowledge(self.target, result["id"])
                    (self.base / "current.json").unlink(missing_ok=True)

    def test_a_previous_release_without_migrate_falls_back_to_rollback_compatibility(self):
        for compatible, outcome in ((True, "rolled_back"), (False, "failed")):
            with self.subTest(compatible=compatible):
                Rig.reset()
                self.release("v1", "1", [], migrate=False)
                self.apply()
                self.release("v2", "2", [(1, "001-pings", "expand")], compatible=compatible)
                FakeDocker.fail = "v2"
                result = self.apply()
                FakeDocker.fail = None
                self.assertEqual(outcome, result["status"])
                self.assertEqual(0, sum(event.startswith("check:") for event in FakeDocker.events))
                if compatible:
                    self.assertEqual("warning", self.step(result, "Rollback gate")["status"])
                    self.assertIn("Rollback gate: v1 declares no migrate item to check; this release's rollback "
                                  "compatibility lets it return", result["warnings"])
                else:
                    runner.acknowledge(self.target, result["id"])

    def test_leaving_a_migrating_release_for_one_without_migrate_trusts_rollback_compatibility(self):
        for compatible, outcome in ((False, "failed"), (True, "succeeded")):
            with self.subTest(compatible=compatible):
                Rig.reset()
                (self.base / "migrations.json").unlink(missing_ok=True)
                self.release("v2", "2", [(1, "001-pings", "expand")], compatible=compatible)
                self.apply()
                self.release("v1", "1", [], migrate=False)
                FakeDocker.events = []
                result = self.apply()
                self.assertEqual(outcome, result["status"])
                gate = self.step(result, "Rollback gate")
                if compatible:
                    self.assertEqual("warning", gate["status"])
                    self.assertEqual("v1", self.current())
                else:
                    self.assertEqual("failed", gate["status"])
                    self.assertEqual(["pull"], self.flow())
                    self.assertTrue(result["reason"].startswith(
                        "Rollback gate: v1 declares no migrate item to check, and v2 does not declare earlier releases "
                        "compatible"))
                    self.assertFalse(result.get("mutationStarted", False))

    def test_a_target_that_never_migrated_deploys_a_release_without_migrate_as_before(self):
        self.release("v1", "1", [], migrate=False)
        result = self.apply()
        self.assertEqual("succeeded", result["status"])
        self.assertEqual(["pull", "up:v1", "verify:v1"], self.flow())
        self.assertNotIn("migrations", result)
        self.assertIsNone(runner.state(self.target)["migrations"])

    # ---- check now ----

    def test_check_now_runs_the_running_images_check_and_refreshes_what_the_target_knows(self):
        self.release("v1", "1", [(1, "001-pings", "expand")])
        self.apply()
        Rig.history.append((2, "002-hand", "expand"))
        result = runner.migrate_check(self.target, docker_factory=MigrateDocker)
        self.assertEqual(("pilot-dev", "v1"), (result["project"], result["release"]))
        [entry] = result["items"]
        self.assertEqual(("check", "check", "current", 0), (entry["verb"], entry["purpose"], entry["status"],
                                                            entry["exitCode"]))
        self.assertEqual(runner.MIGRATE_CHECK_TIMEOUT, Rig.calls[-1]["timeout"])
        self.assertTrue(Rig.calls[-1]["container"].startswith("pilot-dev-check-api-"))
        [known] = result["migrations"]["sets"]
        self.assertEqual((2, "ahead-compatible", "check", "001-pings"),
                         (known["level"], known["state"], known["source"], known["newestLabel"]))
        self.assertEqual(result["migrations"], runner.state(self.target)["migrations"])

    def test_check_now_is_refused_while_a_rollout_runs_and_before_any_release(self):
        with self.assertRaisesRegex(runner.Rejected, "runs no release yet"):
            runner.migrate_check(self.target, docker_factory=MigrateDocker)
        self.release("v1", "1", [(1, "001-pings", "expand")])
        self.apply()
        with runner.deployment_lock(self.base):
            with self.assertRaisesRegex(runner.Rejected, "Another deployment"):
                runner.migrate_check(self.target, docker_factory=MigrateDocker)

    def test_check_now_on_a_release_without_migrate_reports_no_items(self):
        self.release("v1", "1", [], migrate=False)
        self.apply()
        result = runner.migrate_check(self.target, docker_factory=MigrateDocker)
        self.assertEqual(([], None), (result["items"], result["migrations"]))
        self.assertEqual([], Rig.calls)

    # ---- the bundle ----

    def test_a_bundle_declaring_migrate_validates_and_a_bad_one_refuses_before_docker(self):
        self.release("v1", "1", [(1, "001-pings", "expand")])
        self.manifest["extensions"] = ["postgres_fdw", "uuid-ossp"]
        self.release("v1", "1", [(1, "001-pings", "expand")])
        validated = runner.validate_bundle(self.bundle)
        self.assertEqual([{"service": "api", "command": COMMAND, "timeoutSeconds": 120}],
                         runner.release_migrate(validated))
        self.assertEqual({"default": 1}, runner.release_levels(validated))
        self.assertEqual(["postgres_fdw", "uuid-ossp"], runner.release_extensions(validated))
        defaults = {"service": "api", "command": COMMAND}
        cases = [({"migrate": [{**defaults, "service": "worker"}]}, "unknown service"),
                 ({"migrate": [defaults, defaults]}, "Two migrate items"),
                 ({"migrate": [{**defaults, "command": []}]}, "Invalid migrate command"),
                 ({"migrate": [{**defaults, "command": "migrate"}]}, "Invalid migrate command"),
                 ({"migrate": [{**defaults, "timeoutSeconds": 5}]}, "migrate timeout"),
                 ({"migrate": []}, "Invalid migrate list"),
                 ({"migrationSets": {"worker": []}}, "Migration sets name an unknown service"),
                 ({"migrationSets": {"api": [{"name": "Bad Name"}]}}, "Invalid migration sets"),
                 ({"extensions": ["DROP TABLE"]}, "Invalid extensions")]
        for change, message in cases:
            with self.subTest(message=message):
                manifest = {**self.manifest, **change}
                (self.bundle / "release.json").write_text(json.dumps(manifest))
                with self.assertRaisesRegex(runner.Rejected, message):
                    runner.validate_bundle(self.bundle)
        self.assertEqual([], FakeDocker.events)
        self.assertEqual(60 * 10, runner.release_migrate(
            {"images": {"api": "x"}, "migrate": [{"service": "api", "command": COMMAND}]})[0]["timeoutSeconds"])


    def test_check_now_runs_through_the_runner_and_the_transport(self):
        self.release("v1", "1", [(1, "001-pings", "expand")])
        self.apply()
        encoded = base64.b64encode(json.dumps(self.target).encode()).decode()
        with patch.object(runner, "Docker", MigrateDocker), contextlib.redirect_stdout(io.StringIO()) as stdout, \
                patch("sys.argv", ["runner.py", "migrate-check", "--target-json", encoded]):
            self.assertEqual(0, runner.main())
        self.assertEqual("current", json.loads(stdout.getvalue())["items"][0]["status"])
        config = {"serverId": "pilot", "provider": "Hetzner", "ssh": {}, "target": self.target}
        with patch.object(transport.fleet, "resolve_target", return_value=config), \
                patch.object(transport, "Ssh"), \
                patch.object(transport, "run_remote", return_value={"items": [], "migrations": None}) as remote:
            result = transport.target_migrations(self.root, "pilot-dev")
        self.assertEqual(("migrate-check", self.target), remote.call_args.args[1:3])
        self.assertEqual(100, remote.call_args.kwargs["timeout"])
        self.assertEqual("pilot-dev", result["targetId"])

    def test_the_job_record_reaches_the_history_observation(self):
        self.release("v1", "1", [(1, "001-pings", "expand")])
        result = self.apply()
        self.assertIn("migrations", history.OBSERVED)
        self.assertEqual(result["migrations"], runner.state(self.target)["active"]["migrations"])


class MigrateDockerTests(unittest.TestCase):
    def test_the_last_result_line_is_read_from_stdout_alone(self):
        output = "report\nmigrate-result {\"verb\":\"check\"}\nnoise\nmigrate-result {\"verb\":\"apply\"}\ntrailer\n"
        seen = []

        def fake_run(arguments, **options):
            seen.append(options)
            return subprocess.CompletedProcess(arguments, 0, output, None)

        with patch.object(runner.subprocess, "run", side_effect=fake_run):
            code, line = runner.Docker({"product": "pilot", "environment": "dev"}, {"PATH": "/bin"}).migrate(
                ["docker", "run"], {runner.MIGRATE_KEY: DATABASE}, 60, "box")
        self.assertEqual((0, "migrate-result {\"verb\":\"apply\"}"), (code, line))
        self.assertEqual(subprocess.DEVNULL, seen[0]["stderr"])
        self.assertEqual(DATABASE, seen[0]["env"][runner.MIGRATE_KEY])

    def test_a_run_past_its_timeout_stops_and_removes_the_container(self):
        calls = []

        def fake_run(arguments, **options):
            calls.append(arguments)
            if arguments[:2] == ["docker", "run"]:
                raise subprocess.TimeoutExpired(arguments, options["timeout"])
            return subprocess.CompletedProcess(arguments, 0, "", "")

        with patch.object(runner.subprocess, "run", side_effect=fake_run):
            self.assertEqual((None, None), runner.Docker({"product": "pilot", "environment": "dev"}, {}).migrate(
                ["docker", "run"], {}, 10, "box"))
        self.assertEqual([["docker", "stop", "--time", "10", "box"], ["docker", "rm", "--force", "box"]], calls[1:])

    def test_interpolation_follows_compose(self):
        variables = {"A": "one", "EMPTY": ""}
        self.assertEqual("one/x/$/two/", runner.interpolate("${A}/${B:-x}/$$/${EMPTY:-two}/${EMPTY-three}", variables))
        with self.assertRaisesRegex(runner.Rejected, "Compose needs B"):
            runner.interpolate("${B:?Set B}", variables)

    def test_a_result_message_never_keeps_a_password_or_the_key(self):
        line = result_line("migrate", "failed", "behind", 1, 1,
                           message="could not connect: " + DATABASE + " password=hunter2")
        self.assertEqual("could not connect: *** password=***", runner.migrate_result(line, DATABASE)["message"])


if __name__ == "__main__":
    unittest.main()
