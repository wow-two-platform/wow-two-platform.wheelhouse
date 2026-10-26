import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import runner
import transport


class ControlDocker:
    """Records each Compose call a control makes; `fail` names an operation whose call fails."""
    events = []
    fail = None
    health = {}
    # The environment each Docker the runner made was given: what Compose would interpolate.
    environments = []

    def __init__(self, target, environment):
        self.environments.append(environment)

    # What a rollout needs, so a release can be applied before it is controlled.
    def preflight(self, manifest):
        pass

    def pull(self, bundle):
        pass

    def up(self, bundle, recreate=None):
        self.events.append("up")

    def up_services(self, bundle, services):
        self.events.append("up:" + ",".join(services))

    def verify(self, bundle, manifest):
        pass

    def execute(self, arguments, step, timeout=600):
        return ""

    def stop(self, bundle, services=()):
        self.events.append("stop:" + ",".join(services))
        if self.fail == "stop":
            raise runner.CommandFailed("compose stop failed (exit 1); inspect target containers privately")

    def start(self, bundle, services=()):
        self.events.append("start:" + ",".join(services))
        if self.fail == "start":
            raise runner.CommandFailed("compose up failed (exit 1); inspect target containers privately")

    def unhealthy(self, bundle):
        return ["api (unhealthy)"]

    def service_health(self, bundle):
        return dict(self.health)


class RunnerControlTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        settings = self.root / "settings.json"
        settings.write_text(json.dumps({"Database": {"Connection": "DO_NOT_LOG"}}))
        settings.chmod(0o600)
        self.target = {"product": "pilot", "environment": "dev", "root": str(self.root / "state"),
                       "settings": {"api": str(settings)}, "minimumFreeBytes": 0}
        self.base = self.root / "state" / "pilot-dev"
        self.bundle = self.root / "bundle"
        self.bundle.mkdir()
        image = "ghcr.io/owner/{}@sha256:" + "a" * 64
        manifest = {"schemaVersion": 1, "product": "pilot", "release": "v1", "sourceCommit": "a" * 40,
                    "platform": "linux/amd64", "rollbackCompatible": False,
                    "images": {"api": image.format("api"), "worker": image.format("worker")},
                    "requiredConfiguration": {"api": ["Database:Connection"]}}
        health = {"test": ["CMD", "true"]}
        compose = json.dumps({"services": {name: {"image": manifest["images"][name], "platform": "linux/amd64",
                                                  "healthcheck": health} for name in ("api", "worker")}}).encode()
        (self.bundle / "compose.json").write_bytes(compose)
        manifest["composeSha256"] = hashlib.sha256(compose).hexdigest()
        (self.bundle / "release.json").write_text(json.dumps(manifest))
        ControlDocker.events, ControlDocker.fail, ControlDocker.health, ControlDocker.environments = [], None, {}, []
        self.settings = settings

    def tearDown(self):
        self.temporary.cleanup()

    def deployed(self):
        record = runner.apply(self.bundle, self.target, "max", docker_factory=ControlDocker)
        self.assertEqual("succeeded", record["status"])
        ControlDocker.events = []
        return record

    def control(self, operation, services=(), job=None):
        job = job or "11111111-1111-4111-8111-1111111111{:02d}".format(len(ControlDocker.events))
        return runner.control(self.target, operation, list(services), "max", job, docker_factory=ControlDocker)

    def test_a_target_without_a_release_has_nothing_to_stop(self):
        with self.assertRaisesRegex(ValueError, "runs no release yet"):
            self.control("stop")

    def test_stopping_a_service_marks_it_and_keeps_the_release(self):
        deployed = self.deployed()
        record = self.control("stop", ["worker"])
        self.assertEqual(["stop:worker"], ControlDocker.events)
        self.assertEqual("succeeded", record["status"])
        self.assertEqual({"kind": "stop", "services": ["worker"]}, record["operation"])
        self.assertEqual(["Stop worker"], [step["name"] for step in record["steps"]])
        self.assertEqual({"environment": False, "services": ["worker"]},
                         {key: record["stopped"][key] for key in ("environment", "services")})
        current = runner.read_json(self.base / "current.json")
        self.assertEqual(deployed["id"], current["id"])
        self.assertEqual("v1", record["release"])
        state = runner.state(self.target)
        self.assertEqual(["worker"], state["stopped"]["services"])
        self.assertEqual(record["id"], state["active"]["id"])
        self.assertEqual("ready", state["condition"])

    def test_every_deploy_is_refused_while_anything_stays_stopped(self):
        self.deployed()
        self.control("stop", ["worker"])
        with self.assertRaisesRegex(ValueError, "worker is stopped on pilot-dev; start it before deploying"):
            runner.apply(self.bundle, self.target, "automation", docker_factory=ControlDocker)
        target_path = self.root / "target.json"
        runner.write_json(target_path, self.target)
        with self.assertRaisesRegex(ValueError, "start it before deploying"):
            runner.launch(self.bundle, target_path, "automation")
        self.assertFalse(any("up" in event for event in ControlDocker.events))
        check = runner.check(self.target, docker_factory=ControlDocker)
        self.assertIn({"name": "Stopped", "ok": False, "detail": "worker is stopped on pilot-dev; start it before deploying"},
                      check["checks"])

    def test_a_stopped_environment_starts_whole_and_then_deploys_again(self):
        self.deployed()
        self.control("stop")
        self.assertEqual(["stop:"], ControlDocker.events)
        self.assertTrue(runner.stopped_of(self.base)["environment"])
        with self.assertRaisesRegex(ValueError, "pilot-dev is stopped; start it before deploying"):
            runner.apply(self.bundle, self.target, "max", docker_factory=ControlDocker)
        for operation, services, refusal in (("start", ["api"], "start the environment"),
                                             ("restart", ["api"], "start the environment"),
                                             ("stop", [], "already stopped")):
            with self.subTest(operation=operation), self.assertRaisesRegex(ValueError, refusal):
                self.control(operation, services)
        started = self.control("start")
        self.assertEqual("start:", ControlDocker.events[-1])
        self.assertEqual(["Start the environment"], [step["name"] for step in started["steps"]])
        self.assertIsNone(started["stopped"])
        self.assertFalse((self.base / runner.STOPPED).exists())
        self.assertEqual("succeeded", runner.apply(self.bundle, self.target, "max", docker_factory=ControlDocker)["status"])

    def test_a_restart_stops_and_starts_one_service_on_its_release(self):
        self.deployed()
        ControlDocker.health = {"api": "healthy", "worker": "healthy"}
        record = self.control("restart", ["api"])
        self.assertEqual(["stop:api", "start:api"], ControlDocker.events)
        self.assertEqual("api healthy on v1", record["steps"][0]["detail"])
        self.assertEqual({"api": "healthy", "worker": "healthy"}, record["health"])
        self.assertIsNone(record["stopped"])
        with self.assertRaisesRegex(ValueError, "Restart one service at a time"):
            self.control("restart")
        self.control("stop", ["api"])
        with self.assertRaisesRegex(ValueError, "api is stopped; start it instead"):
            self.control("restart", ["api"])

    def test_a_service_must_be_one_the_release_runs(self):
        self.deployed()
        with self.assertRaisesRegex(ValueError, "has no service billing"):
            self.control("stop", ["billing"])
        with self.assertRaisesRegex(ValueError, "Unknown control"):
            self.control("pause")

    def test_a_failed_start_keeps_the_mark_so_deploys_stay_refused(self):
        self.deployed()
        self.control("stop", ["api"])
        ControlDocker.fail = "start"
        record = self.control("start", ["api"])
        self.assertEqual("failed", record["status"])
        self.assertIn("not healthy: api (unhealthy)", record["reason"])
        self.assertEqual(["api"], record["stopped"]["services"])
        ControlDocker.fail = None
        self.assertIsNone(self.control("start", ["api"])["stopped"])

    def test_a_stop_cut_short_still_marks_what_it_reached(self):
        self.deployed()
        ControlDocker.fail = "stop"
        record = self.control("stop")
        self.assertEqual("failed", record["status"])
        self.assertTrue(record["stopped"]["environment"])
        self.assertNotIn("mutationStarted", record)
        # A failed control needs no reconciliation: a start finishes it.
        self.assertEqual("ready", runner.state(self.target)["condition"])

    def test_a_control_never_runs_beside_a_rollout_or_after_an_interrupted_one(self):
        self.deployed()
        target_path = self.root / "target.json"
        runner.write_json(target_path, self.target)
        with runner.deployment_lock(self.base):
            with self.assertRaisesRegex(ValueError, "A rollout or another control is running"):
                runner.control_launch(target_path, "stop", [], "max")
            with self.assertRaisesRegex(ValueError, "Another deployment owns this target"):
                self.control("stop")
        active = runner.read_json(self.base / "active.json")
        runner.write_json(self.base / "active.json", {**active, "status": "running"})
        with self.assertRaisesRegex(ValueError, "Interrupted deployment requires reconciliation"):
            runner.control_launch(target_path, "stop", [], "max")

    def test_a_control_cut_short_reconciles_without_dropping_the_verified_release(self):
        deployed = self.deployed()
        record = self.control("stop", ["api"])
        runner.write_json(self.base / "active.json", {**record, "status": "running"})
        self.assertEqual("needs_reconciliation", runner.state(self.target)["condition"])
        acknowledged = runner.acknowledge(self.target, record["id"])
        self.assertEqual("interrupted", acknowledged["status"])
        self.assertEqual(deployed["id"], runner.read_json(self.base / "current.json")["id"])

    def test_launch_queues_the_job_in_the_journal_and_starts_the_worker(self):
        self.deployed()
        target_path = self.root / "target.json"
        runner.write_json(target_path, self.target)
        with patch.object(runner.subprocess, "Popen") as popen:
            queued = runner.control_launch(target_path, "stop", ["worker"], "max")
        self.assertEqual({"status": "queued", "release": "v1"}, {key: queued[key] for key in ("status", "release")})
        journal = runner.read_json(self.base / "jobs" / (queued["id"] + ".json"))
        self.assertEqual({"kind": "stop", "services": ["worker"]}, journal["operation"])
        arguments = popen.call_args.args[0]
        self.assertEqual(["control", "--target", str(target_path.resolve()), "--operation", "stop", "--actor", "max",
                          "--job", queued["id"], "--service", "worker"], arguments[2:])

    def test_a_control_is_no_dev_pass_for_the_release_it_acted_on(self):
        self.deployed()
        record = self.control("stop", ["worker"])
        found = runner.passed(self.target, "v1")
        self.assertNotEqual(record["id"], found["id"])

    def test_vitals_and_watch_name_what_the_owner_stopped(self):
        self.deployed()
        self.control("stop", ["worker"])

        class Reading:
            def __init__(self, target, environment):
                pass

            def execute(self, arguments, step, timeout=600):
                return ""

        self.assertEqual(["worker"], runner.vitals(self.target, Reading)["stopped"]["services"])
        self.assertEqual(["worker"], runner.watch(self.target, Reading, prober=lambda *a, **k: [])["stopped"]["services"])

    def test_a_release_applied_from_its_host_file_restarts_on_that_file(self):
        # The canary on wheelhouse-dev: Wheelhouse renders the target's settings now, but the release it runs was applied
        # from the host file the target names, before it rendered, so no rendered file exists.
        self.deployed_from_host_file()
        (self.base / "releases" / runner.read_json(self.base / "current.json")["id"] / runner.SETTINGS_PATHS).unlink()
        rendering = {**self.target, "render": True, "settingsRoot": str(self.root / "rendered")}
        self.assertFalse(runner.rendered_path(rendering, "api").exists())
        record = runner.control(rendering, "restart", ["api"], "max", "11111111-1111-4111-8111-111111111150",
                                docker_factory=ControlDocker)
        self.assertEqual(("succeeded", None), (record["status"], record.get("reason")))
        self.assertEqual(str(self.settings), ControlDocker.environments[-1]["API_SETTINGS"])

    def test_a_control_mounts_the_files_its_release_was_applied_with(self):
        self.deployed_from_host_file()
        current = runner.read_json(self.base / "current.json")["id"]
        self.assertEqual({"api": str(self.settings)},
                         runner.read_json(self.base / "releases" / current / runner.SETTINGS_PATHS))
        # A later render (a deploy that failed before it changed anything) leaves a rendered file the containers never
        # mounted; the control keeps to the recorded one.
        rendering = {**self.target, "render": True, "settingsRoot": str(self.root / "rendered")}
        rendered = runner.rendered_path(rendering, "api")
        rendered.parent.mkdir(parents=True, mode=0o700)
        rendered.write_text("{}")
        self.assertEqual("succeeded", runner.control(rendering, "stop", ["api"], "max",
                                                     "11111111-1111-4111-8111-111111111151",
                                                     docker_factory=ControlDocker)["status"])
        self.assertEqual(str(self.settings), ControlDocker.environments[-1]["API_SETTINGS"])

    def test_a_control_whose_settings_files_are_gone_is_refused_before_a_job(self):
        self.deployed_from_host_file()
        current = runner.read_json(self.base / "current.json")["id"]
        (self.base / "releases" / current / runner.SETTINGS_PATHS).unlink()
        self.settings.unlink()
        target_path = self.root / "target.json"
        runner.write_json(target_path, {**self.target, "render": True, "settingsRoot": str(self.root / "rendered")})
        jobs = sorted((self.base / "jobs").glob("*.json"))
        with patch.object(runner.subprocess, "Popen") as popen, \
                self.assertRaisesRegex(ValueError, "No settings file for api is left on this target; deploy the release again"):
            runner.control_launch(target_path, "restart", ["api"], "max")
        popen.assert_not_called()
        self.assertEqual(jobs, sorted((self.base / "jobs").glob("*.json")))

    def deployed_from_host_file(self):
        """Applies the release as a target that mounts its host settings file, as every target did before Wheelhouse
        rendered them."""
        record = runner.apply(self.bundle, {**self.target, "render": False}, "max", docker_factory=ControlDocker)
        self.assertEqual("succeeded", record["status"])
        ControlDocker.events, ControlDocker.environments = [], []

    def test_an_unreadable_mark_counts_as_a_stopped_environment(self):
        self.base.mkdir(parents=True)
        (self.base / runner.STOPPED).write_text("{")
        self.assertTrue(runner.stopped_of(self.base)["environment"])


class TransportControlTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        for name in ("key", "known_hosts"):
            (self.root / name).touch()
        self.ssh = {"host": "vps.example.net", "user": "deploy", "port": 22,
                    "keyFile": str(self.root / "key"), "knownHostsFile": str(self.root / "known_hosts")}
        self.config = {"serverId": "pilot-host", "provider": "Hetzner", "ssh": self.ssh, "acceptsCandidates": True,
                       "needsConfirmation": False, "requiresDevPass": False,
                       "target": {"product": "pilot", "environment": "dev", "root": "/srv/wheelhouse"}}

    def tearDown(self):
        self.temporary.cleanup()

    def fake_ssh(self, answer=None, refusal=None):
        commands = []

        class FakeSsh:
            def __init__(self, ssh_config):
                pass

            def run(self, command, stdin=None, timeout=30):
                commands.append(command)
                if "control-launch" not in command:
                    return ""
                if refusal:
                    raise runner.Rejected(refusal)
                return json.dumps(answer or {"id": "22222222-2222-4222-8222-222222222222", "status": "queued",
                                             "release": "v1"})

            def copy(self, files, destination):
                commands.append("copy " + " ".join(Path(file).name for file in files))

        return FakeSsh, commands

    def test_a_control_is_submitted_like_a_deploy_and_listed_with_its_operation(self):
        fake, commands = self.fake_ssh()
        with patch.object(transport.fleet, "resolve_target", return_value=self.config), \
                patch.object(transport, "Ssh", fake):
            submitted = transport.control(self.root, "pilot-dev", "restart", "api", "max", reason=" stuck cache ")
        self.assertEqual({"kind": "restart", "services": ["api"]}, submitted["operation"])
        self.assertEqual("copy runner.py target.json", commands[1])
        self.assertIn("control-launch", commands[2])
        self.assertIn("--service api", commands[2])
        record = json.loads((self.root / "jobs" / (submitted["id"] + ".json")).read_text())
        self.assertEqual(("22222222-2222-4222-8222-222222222222", "v1", "stuck cache"),
                         (record["remoteJobId"], record["release"], record["deployReason"]))
        self.assertEqual([], transport.jobs(self.root), "rollout readings pass controls over")
        listed = transport.jobs(self.root, controls=True)[0]
        self.assertEqual({"kind": "restart", "services": ["api"]}, listed["operation"])

    def test_a_refused_control_leaves_no_record(self):
        fake, _ = self.fake_ssh(refusal="A rollout or another control is running on pilot-dev")
        with patch.object(transport.fleet, "resolve_target", return_value=self.config), \
                patch.object(transport, "Ssh", fake), \
                self.assertRaisesRegex(ValueError, "another control is running"):
            transport.control(self.root, "pilot-dev", "stop", None, "max")
        self.assertEqual([], list((self.root / "jobs").glob("*.json")))

    def test_wheelhouse_never_controls_its_own_environments(self):
        config = {**self.config, "self": True}
        fake, commands = self.fake_ssh()
        with patch.object(transport.fleet, "resolve_target", return_value=config), \
                patch.object(transport, "Ssh", fake), \
                self.assertRaisesRegex(ValueError, "never stopped or restarted from the console"):
            transport.control(self.root, "wheelhouse-dev", "stop", None, "max")
        self.assertEqual([], commands)

    def test_a_restart_names_one_service(self):
        with self.assertRaisesRegex(ValueError, "Restart one service at a time"):
            transport.control(self.root, "pilot-dev", "restart", None, "max")
        with self.assertRaisesRegex(ValueError, "Unknown control"):
            transport.control(self.root, "pilot-dev", "pause", None, "max")

    def finished(self, job_id, submitted, stopped, operation="stop"):
        transport.write_json(self.root / "jobs" / (job_id + ".json"), {
            "id": job_id, "targetId": "pilot-dev", "operation": {"kind": operation, "services": []},
            "remoteJobId": job_id, "status": "queued", "submittedAt": submitted})
        transport.write_json(self.root / "observed" / (job_id + ".json"), {
            "id": job_id, "status": "succeeded", "completedAt": submitted, "stopped": stopped})

    def test_a_stopped_target_refuses_a_deploy_before_anything_is_downloaded(self):
        stopped = {"environment": True, "services": [], "at": None, "actor": "max"}
        self.finished("33333333-3333-4333-8333-333333333331", "2026-10-26T10:00:00+00:00", stopped)
        self.assertEqual({"pilot-dev": stopped}, transport.stops(self.root))
        with patch.object(transport.fleet, "resolve_target", return_value=self.config), \
                patch.object(transport.artifacts, "prepare") as prepare, \
                self.assertRaisesRegex(ValueError, "pilot-dev is stopped; start it before deploying"):
            transport.submit(self.root, "pilot-dev", "pilot-gh-1", "automation")
        prepare.assert_not_called()
        self.finished("33333333-3333-4333-8333-333333333332", "2026-10-26T11:00:00+00:00", None, "start")
        self.assertEqual({}, transport.stops(self.root))
        self.assertIsNone(transport.recorded_stop(self.root, "pilot-dev"))

    def test_a_watch_passes_over_what_the_owner_stopped(self):
        reading = {"containers": [{"service": "worker", "state": "exited"}, {"service": "api", "state": "running",
                                                                           "health": "healthy"}],
                   "sites": [{"name": "app", "service": "worker", "ok": False}],
                   "stopped": {"environment": False, "services": ["worker"]}}
        self.assertEqual([], transport.regressions(reading, {}))
        self.assertEqual([], transport.regressions({**reading, "stopped": {"environment": True, "services": []}}, {}))
        self.assertEqual(["worker is exited", "Site app stopped answering"],
                         transport.regressions({**reading, "stopped": None}, {}))


if __name__ == "__main__":
    unittest.main()
