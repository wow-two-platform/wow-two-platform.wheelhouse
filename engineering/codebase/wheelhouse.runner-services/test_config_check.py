"""The config check step (hub deploy-descriptor § Config check): the migrate rig as runner tests.

Each image answers `config check` the way the be sdk's verb does: exit 0 consistent, 1 refused, 2 failed, and a
`config-result` line last. The rollout around it is the runner's own, on the migrate rig's fake Docker and database, so
the step runs after migrate and before the first wave, on a settings apply and in the rollback gate as it does on a
target.
"""
import hashlib
import json
import shutil
import subprocess
import unittest
from unittest.mock import patch
import history
import runner
import test_migrate
from test_migrate import COMMAND, DATABASE, MigrateDocker, Rig
from test_runner import FakeDocker

CHECK = ["dotnet", "Pilot.Api.dll", "config", "check"]


def config_line(service="api", invalid=(), unknown=(), stranded=(), error=None, exit_code=None):
    """A `config-result` line as the be sdk prints it; `invalid` holds (code, setting, message) and `stranded` (code,
    setting, count)."""
    value = {"verb": "check", "exit": 0, "service": service, "durationMs": 380}
    if error is not None:
        value.update(exit=2, error=error)
    else:
        value.update(invalid=[{"code": code, "setting": setting, "message": message} for code, setting, message in invalid],
                     unknown=list(unknown),
                     stranded=[{"code": code, "setting": setting, "count": count} for code, setting, count in stranded])
        value["exit"] = 1 if invalid or unknown or stranded else 0
    if exit_code is not None:
        value["exit"] = exit_code
    return "config-result " + json.dumps(value)


REFUSAL = (1, config_line(invalid=[("range", "Identity:Password:MinLength", "20 exceeds MaxLength 16; was DO_NOT_LOG")],
                          unknown=["Identity:SignUp:Enabled"],
                          stranded=[("only-method-removed", "Identity:Methods:Password:IsEnabled", 3)]))


class ConfigRig:
    """What each image's `config check` answers: `answers` maps a release label to (exit, line), consistent otherwise;
    `forced` answers the next runs as given, before that. `calls` records each run."""
    answers, forced, calls = {}, [], []

    @classmethod
    def reset(cls):
        cls.answers, cls.forced, cls.calls = {}, [], []


class ConfigDocker(MigrateDocker):
    def config_check(self, arguments, timeout, container):
        image = arguments[arguments.index("--entrypoint") + 2]
        label = Rig.labels[image]
        ConfigRig.calls.append({"arguments": arguments, "timeout": timeout, "container": container, "image": image})
        self.events.append("config:" + label)
        if ConfigRig.forced:
            return ConfigRig.forced.pop(0)
        return ConfigRig.answers.get(label, (0, config_line()))


class ConfigCheckStepTests(unittest.TestCase):
    setUp_rig = test_migrate.MigrateStepTests.setUp
    tearDown = test_migrate.MigrateStepTests.tearDown
    base = test_migrate.MigrateStepTests.base
    step = test_migrate.MigrateStepTests.step
    current = test_migrate.MigrateStepTests.current
    migrate_release = test_migrate.MigrateStepTests.release

    def setUp(self):
        self.setUp_rig()
        ConfigRig.reset()

    def release(self, label, letter, migrations=((1, "001-pings", "expand"),), compatible=False, migrate=True,
                check=True, timeout=None):
        """A release of the api, as the migrate rig builds it, that declares a config check item unless `check` is
        false."""
        self.manifest.pop("configCheck", None)
        if check:
            item = {"service": "api", "command": CHECK}
            if timeout is not None:
                item["timeoutSeconds"] = timeout
            self.manifest["configCheck"] = [item]
        self.migrate_release(label, letter, list(migrations), compatible=compatible, migrate=migrate)

    def worker(self, items):
        """Adds a worker service beside the api, its own image embedding no migration, and declares `items`."""
        worker = "ghcr.io/owner/worker@sha256:" + "9" * 64
        Rig.images[worker], Rig.labels[worker] = [], "worker-v1"
        self.manifest["images"]["worker"] = worker
        self.compose["services"]["worker"] = {"image": worker, "platform": "linux/amd64", "depends_on": ["api"],
                                              "healthcheck": {"test": ["CMD", "true"]}}
        self.manifest["configCheck"] = items
        compose = json.dumps(self.compose).encode()
        (self.bundle / "compose.json").write_bytes(compose)
        self.manifest["composeSha256"] = hashlib.sha256(compose).hexdigest()
        (self.bundle / "release.json").write_text(json.dumps(self.manifest))
        return worker

    def apply(self, docker=ConfigDocker):
        return runner.apply(self.bundle, self.target, "test-operator", docker_factory=docker)

    def flow(self):
        return [event for event in FakeDocker.events
                if event.split(":")[0] in ("pull", "migrate", "check", "config", "up", "verify")]

    def names(self, result):
        return [step["name"] for step in result["steps"]]

    # ---- a bundle without the key ----

    def test_a_bundle_without_config_check_deploys_exactly_as_before(self):
        for migrate in (True, False):
            with self.subTest(migrate=migrate):
                Rig.reset()
                shutil.rmtree(self.base, ignore_errors=True)
                self.release("v1", "1", migrate=migrate, check=False)
                FakeDocker.events = []
                result = self.apply()
                self.assertEqual("succeeded", result["status"], result.get("reason"))
                self.assertEqual((["pull", "migrate:v1"] if migrate else ["pull"]) + ["up:v1", "verify:v1"],
                                 self.flow())
                self.assertNotIn("configChecks", result)
                self.assertFalse([name for name in self.names(result) if "onfig check" in name])
                self.assertEqual([], ConfigRig.calls)
                self.assertNotIn("warnings", result)

    # ---- every release deploy ----

    def test_each_item_runs_after_migrate_and_before_the_first_wave(self):
        self.release("v1", "1")
        result = self.apply()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertEqual(["pull", "migrate:v1", "config:v1", "up:v1", "verify:v1"], self.flow())
        names = self.names(result)
        self.assertEqual(names.index("Migrate api") + 1, names.index("Config check api"))
        self.assertLess(names.index("Config check api"), names.index("Start containers"))
        step = self.step(result, "Config check api")
        self.assertEqual(("succeeded", "Consistent in 0s"), (step["status"], step["detail"]))
        self.assertNotIn("findings", step)
        [entry] = result["configChecks"]
        self.assertEqual(("api", "v1", "consistent", 0, True, [], 0, "deploy"), tuple(
            entry[key] for key in ("service", "release", "status", "exitCode", "reported", "findings", "findingCount",
                                   "purpose")))
        self.assertEqual(runner.CONFIG_CHECK_TIMEOUT, ConfigRig.calls[0]["timeout"])

    def test_the_container_carries_the_rendered_settings_and_never_the_migrate_key(self):
        self.target.update(render=True, settings={})
        self.manifest["requiredConfiguration"] = {"api": ["ConnectionStrings:Default"]}
        self.manifest["needs"] = {"api": ["postgres"]}
        self.manifest["dotnetEnvironment"] = True
        folder = self.base.parent.parent / "state" / "config" / "pilot-dev"
        folder.mkdir(parents=True, mode=0o700)
        (folder / "database.json").write_text(json.dumps({"ConnectionString": DATABASE}))
        (folder / "database.json").chmod(0o600)
        self.release("v1", "1", timeout=30)
        result = self.apply()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        [call] = ConfigRig.calls
        arguments = call["arguments"]
        self.assertEqual(["docker", "run", "--rm", "--name", "pilot-dev-config-api-" + result["id"][:8], "--pull",
                          "never", "--network", "platform", "--security-opt", "no-new-privileges:true", "--cap-drop",
                          "ALL", "--label", "wheelhouse.config-check=pilot-dev", "--memory", "256m", "--init"],
                         arguments[:18])
        self.assertEqual(["-e", "ASPNETCORE_ENVIRONMENT=Development", "-e", "DEPLOY_ENVIRONMENT=dev",
                          "-e", "DOTNET_ENVIRONMENT=Development"], arguments[18:24])
        # The rendered settings file (secrets included) and the service's volume, both read-only: the check writes
        # nothing.
        self.assertEqual(["-v", str(folder / "api.target.json") + ":/app/appsettings.Target.json:ro",
                          "-v", "pilot-dev_keys:/data/keys:ro"], arguments[24:28])
        self.assertEqual(["--entrypoint", "dotnet", self.manifest["images"]["api"], "Pilot.Api.dll", "config", "check"],
                         arguments[28:])
        self.assertNotIn(runner.MIGRATE_KEY, arguments)
        self.assertEqual(30, call["timeout"])
        # The migrate container still takes its key; the config check never does.
        self.assertIn(runner.MIGRATE_KEY, Rig.calls[0]["variables"])
        self.assertNotIn("DO_NOT_LOG", json.dumps(result))

    def test_items_run_in_declared_order_each_from_its_own_image(self):
        self.release("v1", "1")
        worker = self.worker([{"service": "worker", "command": ["worker", "config", "check"], "timeoutSeconds": 15},
                              {"service": "api", "command": CHECK}])
        result = self.apply()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertEqual(["pull", "migrate:v1", "config:worker-v1", "config:v1", "up:v1[api]", "up:v1[worker]", "up:v1"],
                         [event for event in self.flow() if not event.startswith("verify")])
        self.assertEqual([(worker, 15), (self.manifest["images"]["api"], 120)],
                         [(call["image"], call["timeout"]) for call in ConfigRig.calls])

    # ---- stopping the rollout ----

    def test_a_refusal_stops_before_any_restart_and_names_each_finding_without_a_value(self):
        self.release("v1", "1")
        self.apply()
        self.release("v2", "2", [(1, "001-pings", "expand"), (2, "002-notes", "expand")])
        ConfigRig.answers["v2"] = REFUSAL
        FakeDocker.events = []
        result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertFalse(result.get("mutationStarted", False))
        self.assertEqual(["pull", "migrate:v2", "config:v2"], self.flow())
        self.assertEqual("v1", self.current())
        self.assertEqual("ready", runner.state(self.target)["condition"])
        step = self.step(result, "Config check api")
        self.assertEqual(("failed", "Refused (exit 1): 3 findings in 0s"), (step["status"], step["detail"]))
        self.assertEqual([{"kind": "invalid", "setting": "Identity:Password:MinLength", "code": "range"},
                          {"kind": "unknown", "setting": "Identity:SignUp:Enabled", "code": None},
                          {"kind": "stranded", "setting": "Identity:Methods:Password:IsEnabled",
                           "code": "only-method-removed", "count": 3}], step["findings"])
        named = ("Refused (exit 1): 3 findings: invalid Identity:Password:MinLength [range]; unknown "
                 "Identity:SignUp:Enabled; stranded Identity:Methods:Password:IsEnabled [only-method-removed]: 3 people "
                 "in 0s")
        self.assertEqual(named, result["configChecks"][0]["detail"])
        self.assertEqual("Config check api: " + named + "; no service was restarted", result["reason"])
        self.assertEqual({"api": ("updated", "not_started")},
                         {service["name"]: (service["change"], service["outcome"])
                          for service in result["serviceRollout"]["services"]})
        # A rule's message may quote the value it rejected: no step, record or journal keeps it.
        self.assertNotIn("DO_NOT_LOG", json.dumps(result))
        self.assertNotIn("exceeds", (self.base / "jobs" / (result["id"] + ".json")).read_text())
        # Nothing to reconcile: no container changed, so the next deploy goes ahead.
        ConfigRig.answers.clear()
        self.assertEqual("succeeded", self.apply()["status"])

    def test_every_item_runs_even_after_one_refuses(self):
        self.release("v1", "1")
        self.worker([{"service": "api", "command": CHECK},
                     {"service": "worker", "command": ["worker", "config", "check"]}])
        for answers, named in (({"v1": REFUSAL}, ["api"]),
                               ({"v1": REFUSAL, "worker-v1": (2, config_line("worker", error="database unreachable"))},
                                ["api", "worker"])):
            with self.subTest(named=named):
                ConfigRig.reset()
                ConfigRig.answers.update(answers)
                FakeDocker.events = []
                result = self.apply()
                self.assertEqual("failed", result["status"])
                self.assertEqual(["pull", "migrate:v1", "config:v1", "config:worker-v1"], self.flow())
                self.assertEqual({"Config check api": "failed",
                                  "Config check worker": "failed" if "worker" in named else "succeeded"},
                                 {step["name"]: step["status"] for step in result["steps"]
                                  if step["name"].startswith("Config check")})
                self.assertEqual(named, [entry["service"] for entry in result["configChecks"]
                                         if entry["status"] != "consistent"])
                for service in named:
                    self.assertIn(service + ": ", result["reason"])
                if "worker" in named:
                    self.assertIn("worker: Failed (exit 2): database unreachable in 0s", result["reason"])

    def test_each_failure_and_the_timeout_stop_the_rollout_with_their_reason(self):
        cases = [
            ((2, config_line(error="database unreachable")), "failed",
             "Config check api: Failed (exit 2): database unreachable in 0s; no service was restarted"),
            ((139, None), "failed", "Config check api: Crashed (exit 139) in 0s; no service was restarted"),
            ((125, None), "failed", "Config check api: Docker could not start it (exit 125); no service was restarted"),
            ((None, None), "timed out",
             "Config check api: Timed out after 120s; the container was stopped; no service was restarted"),
            ((1, None), "refused", "Config check api: Refused (exit 1): no finding reported in 0s; no service was "
                                   "restarted"),
            ((2, "config-result {not json"), "failed", "Config check api: Failed (exit 2) in 0s; no service was "
                                                       "restarted"),
        ]
        self.release("v1", "1")
        self.apply()
        for answer, status, message in cases:
            with self.subTest(status=status, answer=answer[0]):
                ConfigRig.forced = [answer]
                FakeDocker.events = []
                result = self.apply()
                self.assertEqual("failed", result["status"])
                self.assertEqual(message, result["reason"])
                self.assertEqual(status, result["configChecks"][0]["status"])
                self.assertFalse(result.get("mutationStarted", False))
                self.assertEqual(["pull", "migrate:v1", "config:v1"], self.flow())
                self.assertEqual("ready", runner.state(self.target)["condition"])

    def test_a_long_refusal_is_cut_to_fit_the_reason_and_still_says_nothing_restarted(self):
        self.release("v1", "1")
        ConfigRig.forced = [(1, config_line(invalid=[("required", "Identity:Server:Clients:" + str(index)
                                                      + ":RedirectUris:0", "x") for index in range(12)]))]
        result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertLessEqual(len(result["reason"]), 300)
        self.assertTrue(result["reason"].endswith("…; no service was restarted"), result["reason"])
        self.assertEqual(12, len(self.step(result, "Config check api")["findings"]))

    def test_a_missing_result_line_is_tolerated_on_a_zero_exit(self):
        self.release("v1", "1")
        ConfigRig.forced = [(0, None)]
        result = self.apply()
        self.assertEqual("succeeded", result["status"])
        self.assertEqual("Consistent; exit 0, no config-result line in 0s", self.step(result, "Config check api")["detail"])
        self.assertFalse(result["configChecks"][0]["reported"])

    def test_a_check_that_cannot_be_built_fails_before_any_restart(self):
        self.release("v1", "1", migrate=False)
        self.apply()
        self.compose["services"]["api"]["volumes"][0]["source"] = "relative/settings.json"
        self.release("v1", "1", migrate=False)
        FakeDocker.events = []
        result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertEqual("It could not run: A one-off container mounts absolute bind sources only",
                         self.step(result, "Config check api")["detail"])
        self.assertEqual(["pull"], self.flow())
        self.assertEqual("v1", self.current())

    # ---- every settings apply ----

    def test_a_settings_apply_checks_with_the_running_image_and_a_refusal_leaves_the_services_untouched(self):
        self.release("v1", "1")
        first = self.apply()
        self.assertEqual("succeeded", first["status"])
        running = runner.read_json(self.base / "releases" / first["id"] / "release.json")["images"]["api"]
        # The owner's edited value: the running release deploys again with it, as the console's Apply does.
        self.settings.write_text(json.dumps({"Database": {"Connection": "DO_NOT_LOG"}, "Identity": {"SignUp": True}}))
        ConfigRig.answers["v1"] = (1, config_line(unknown=["Identity:SignUp"]))
        FakeDocker.events = []
        result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertEqual(running, ConfigRig.calls[-1]["image"])
        self.assertEqual(["pull", "migrate:v1", "config:v1"], self.flow())
        self.assertFalse([event for event in FakeDocker.events if event.startswith("up")])
        self.assertEqual(first["id"], runner.read_json(self.base / "current.json")["id"])
        self.assertEqual("Config check api: Refused (exit 1): 1 finding: unknown Identity:SignUp in 0s; no service was "
                         "restarted", result["reason"])
        # Corrected, the same apply restarts the service whose settings changed.
        ConfigRig.answers.clear()
        FakeDocker.events = []
        retried = self.apply()
        self.assertEqual("succeeded", retried["status"])
        self.assertEqual(["pull", "migrate:v1", "config:v1", "up:v1", "verify:v1"], self.flow())

    # ---- the rollback gate ----

    def test_the_restore_runs_the_older_images_check_after_its_migrate_check(self):
        for answer, outcome in (((0, config_line()), "rolled_back"), (REFUSAL, "failed")):
            with self.subTest(outcome=outcome):
                Rig.reset()
                ConfigRig.reset()
                self.release("v1", "1")
                self.apply()
                self.release("v2", "2", [(1, "001-pings", "expand"), (2, "002-notes", "expand")])
                ConfigRig.answers["v1"] = answer
                FakeDocker.fail = "v2"
                FakeDocker.events = []
                result = self.apply()
                FakeDocker.fail = None
                self.assertEqual(outcome, result["status"])
                gate = self.step(result, "Rollback gate config check api")
                names = self.names(result)
                self.assertEqual(names.index("Rollback gate api") + 1, names.index("Rollback gate config check api"))
                self.assertEqual("restore", result["configChecks"][-1]["purpose"])
                self.assertTrue(ConfigRig.calls[-1]["container"].startswith("pilot-dev-config-gate-api-"))
                if outcome == "rolled_back":
                    self.assertEqual(["pull", "migrate:v2", "config:v2", "up:v2", "check:v1", "config:v1", "pull",
                                      "up:v1", "verify:v1"], self.flow())
                    self.assertEqual(("succeeded", "v1's check: Consistent in 0s"), (gate["status"], gate["detail"]))
                    self.assertEqual("v1", self.current())
                else:
                    self.assertEqual(["pull", "migrate:v2", "config:v2", "up:v2", "check:v1", "config:v1"], self.flow())
                    self.assertEqual("failed", gate["status"])
                    self.assertEqual(3, len(gate["findings"]))
                    self.assertIn("; the roll back was refused: Rollback gate config check: v1's api: Refused (exit 1): "
                                  "3 findings: invalid Identity:Password:MinLength [range]", result["reason"])
                    self.assertNotIn("no service was restarted", result["reason"])
                    self.assertNotIn("Roll back to v1", names)
                    self.assertNotIn("DO_NOT_LOG", json.dumps(result))
                    runner.acknowledge(self.target, result["id"])
                    (self.base / "current.json").unlink(missing_ok=True)

    def test_a_restore_without_migrate_still_runs_the_older_images_check(self):
        self.release("v1", "1", migrate=False)
        self.apply()
        self.release("v2", "2", migrate=False, compatible=True)
        FakeDocker.fail = "v2"
        FakeDocker.events = []
        result = self.apply()
        FakeDocker.fail = None
        self.assertEqual("rolled_back", result["status"])
        self.assertEqual(["pull", "config:v2", "up:v2", "config:v1", "pull", "up:v1", "verify:v1"], self.flow())

    def test_an_incompatible_release_never_reaches_the_config_gate(self):
        self.release("v1", "1", migrate=False)
        self.apply()
        self.release("v2", "2", migrate=False, compatible=False)
        FakeDocker.fail = "v2"
        FakeDocker.events = []
        result = self.apply()
        FakeDocker.fail = None
        self.assertEqual("failed", result["status"])
        self.assertEqual(["pull", "config:v2", "up:v2"], self.flow())
        runner.acknowledge(self.target, result["id"])

    def test_an_older_release_without_items_skips_the_gate_with_a_warning_only_when_the_newer_one_checked(self):
        for newer_checks in (True, False):
            with self.subTest(newer_checks=newer_checks):
                Rig.reset()
                ConfigRig.reset()
                (self.base / "current.json").unlink(missing_ok=True)
                self.release("v1", "1", migrate=False, check=False)
                self.apply()
                self.release("v2", "2", migrate=False, compatible=True, check=newer_checks)
                FakeDocker.fail = "v2"
                result = self.apply()
                FakeDocker.fail = None
                self.assertEqual("rolled_back", result["status"])
                skipped = [step for step in result["steps"] if step["name"] == "Rollback gate config check"]
                if newer_checks:
                    self.assertEqual([("warning", "v1 declares no config check, so the settings it returns to go "
                                                  "unchecked")], [(step["status"], step["detail"]) for step in skipped])
                    self.assertIn("Rollback gate: v1 declares no config check, so the settings it returns to go "
                                  "unchecked", result["warnings"])
                else:
                    self.assertEqual([], skipped)
                    self.assertNotIn("warnings", result)

    def test_a_deploy_returning_to_an_older_release_checks_it_after_its_rollback_gate(self):
        self.release("v1", "1")
        self.apply()
        self.release("v2", "2", [(1, "001-pings", "expand"), (2, "002-notes", "expand")])
        self.apply()
        self.release("v1", "1")
        FakeDocker.events = []
        result = self.apply()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertEqual(["pull", "check:v1", "config:v1", "up:v1", "verify:v1"], self.flow())
        names = self.names(result)
        self.assertEqual(names.index("Rollback gate api") + 1, names.index("Config check api"))
        # The same return to a v1 built before its config check: skipped, with a warning.
        self.release("v2", "2", [(1, "001-pings", "expand"), (2, "002-notes", "expand")])
        self.apply()
        self.release("v1", "1", check=False)
        result = self.apply()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertEqual("warning", self.step(result, "Rollback gate config check")["status"])

    # ---- the record ----

    def test_the_job_record_reaches_the_history_observation(self):
        self.release("v1", "1")
        result = self.apply()
        self.assertIn("configChecks", history.OBSERVED)
        self.assertEqual(result["configChecks"], runner.state(self.target)["active"]["configChecks"])


class ConfigCheckBundleTests(unittest.TestCase):
    MANIFEST = {"images": {"api": "x", "worker": "y"}}

    def items(self, items):
        return runner.release_config_check({**self.MANIFEST, "configCheck": items})

    def test_a_declared_item_validates_with_its_default_timeout(self):
        self.assertEqual([{"service": "api", "command": CHECK, "timeoutSeconds": 120}],
                         self.items([{"service": "api", "command": CHECK}]))
        self.assertEqual([], runner.release_config_check(self.MANIFEST))
        self.assertEqual([600, 10], [item["timeoutSeconds"] for item in self.items(
            [{"service": "api", "command": CHECK, "timeoutSeconds": 600},
             {"service": "worker", "command": ["worker", "config", "check"], "timeoutSeconds": 10}])])

    def test_a_bad_item_refuses_the_bundle(self):
        item = {"service": "api", "command": CHECK}
        cases = [([{**item, "service": "edge"}], "unknown service"),
                 ([item, item], "Two configCheck items"),
                 ([{**item, "command": ["config", "check"]}], "ending in config check"),
                 ([{**item, "command": ["dotnet", "Pilot.Api.dll", "migrate"]}], "ending in config check"),
                 ([{**item, "command": "dotnet Pilot.Api.dll config check"}], "ending in config check"),
                 ([{**item, "timeoutSeconds": 5}], "timeout must be 10-600"),
                 ([{**item, "timeoutSeconds": 601}], "timeout must be 10-600"),
                 ([], "Invalid configCheck list"),
                 ({"service": "api"}, "Invalid configCheck list")]
        for items, message in cases:
            with self.subTest(message=message, items=items):
                with self.assertRaisesRegex(runner.Rejected, message):
                    self.items(items)


class ConfigResultTests(unittest.TestCase):
    def test_findings_keep_setting_and_code_never_a_message(self):
        run = runner.config_result(REFUSAL[1])
        self.assertEqual((1, 3), (run["exit"], run["findingCount"]))
        self.assertNotIn("message", json.dumps(run))
        self.assertNotIn("DO_NOT_LOG", json.dumps(run))

    def test_an_unreadable_name_or_code_is_dropped_and_the_list_is_bounded(self):
        line = config_line(invalid=[("Not Kebab", "Has a space=DO_NOT_LOG", "x")],
                           unknown=["Key:" + str(index) for index in range(80)])
        run = runner.config_result(line)
        self.assertEqual({"kind": "invalid", "setting": None, "code": None}, run["findings"][0])
        self.assertEqual((runner.FINDINGS_LIMIT, 81), (len(run["findings"]), run["findingCount"]))
        entry = {"exitCode": 1, "status": "refused", "seconds": 1, "reported": True, **run}
        self.assertTrue(runner.config_detail(entry, 120).startswith(
            "Refused (exit 1): 81 findings: invalid a setting; unknown Key:0; unknown Key:1; unknown Key:2; "
            "unknown Key:3; 76 more"))

    def test_an_error_is_scrubbed_and_a_malformed_line_reads_as_none(self):
        run = runner.config_result(config_line(error="could not connect: Host=db;Password=DO_NOT_LOG"))
        self.assertEqual((2, "could not connect: Host=db;Password=***"), (run["exit"], run["error"]))
        for line in (None, "config-result {broken", "config-result []", "migrate-result {}"):
            self.assertIsNone(runner.config_result(line))


class ConfigCheckDockerTests(unittest.TestCase):
    def test_the_last_result_line_is_read_from_stdout_alone_with_no_variable(self):
        output = "report\nconfig-result {\"exit\":1}\nnoise\nconfig-result {\"exit\":0}\ntrailer\n"
        seen = []

        def fake_run(arguments, **options):
            seen.append(options)
            return subprocess.CompletedProcess(arguments, 0, output, None)

        with patch.object(runner.subprocess, "run", side_effect=fake_run):
            code, line = runner.Docker({"product": "pilot", "environment": "dev"}, {"PATH": "/bin"}).config_check(
                ["docker", "run"], 60, "box")
        self.assertEqual((0, "config-result {\"exit\":0}"), (code, line))
        self.assertEqual(subprocess.DEVNULL, seen[0]["stderr"])
        self.assertEqual({"PATH": "/bin"}, seen[0]["env"])

    def test_a_run_past_its_timeout_stops_and_removes_the_container(self):
        calls = []

        def fake_run(arguments, **options):
            calls.append(arguments)
            if arguments[:2] == ["docker", "run"]:
                raise subprocess.TimeoutExpired(arguments, options["timeout"])
            return subprocess.CompletedProcess(arguments, 0, "", "")

        with patch.object(runner.subprocess, "run", side_effect=fake_run):
            self.assertEqual((None, None), runner.Docker({"product": "pilot", "environment": "dev"}, {}).config_check(
                ["docker", "run"], 10, "box"))
        self.assertEqual([["docker", "stop", "--time", "10", "box"], ["docker", "rm", "--force", "box"]], calls[1:])


if __name__ == "__main__":
    unittest.main()
