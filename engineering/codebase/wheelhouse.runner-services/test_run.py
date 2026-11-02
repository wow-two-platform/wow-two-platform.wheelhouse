"""One-off commands (v0.13): the owner's command, run once in a container of a service's running release image with
its settings, mounts and the platform network, as a control beside stop, start, restart and reset.

The job keeps the command's label, never an argument, and the exit code with the last stdout line scrubbed; nothing
stops, and nothing the owner stopped changes.
"""
import json
import subprocess
import unittest
from unittest.mock import patch
import history
import runner
import test_controls
import transport
from test_controls import ControlDocker

PHONE = "+998901234567"
ORGANIZATION = "b8c672a6-1234-4567-8901-123456789012"
OWNER = "11111111-2222-4333-8444-555555555555"
COMMAND = ["dotnet", "Identity.Api.dll", "admin", "create", "--slug", "ocharo", "--name", "Ocharo", "--display-name",
           "Lola Karimova", "--phone", PHONE, "--organization-id", ORGANIZATION, "--user-id", OWNER]
LABEL = ("dotnet Identity.Api.dll admin create --slug … --name … --display-name … --phone … --organization-id … "
         "--user-id …")
RESULT = ("admin-result {\"verb\":\"create\",\"exit\":0,\"status\":\"created\",\"organizationId\":\"" + ORGANIZATION
          + "\",\"userId\":\"" + OWNER + "\",\"durationMs\":42}")


class RunDocker(ControlDocker):
    """Answers each one-off as `answer` says, (exit, last line); records its arguments."""
    answer = (0, RESULT)
    runs = []

    def one_off(self, arguments, timeout, container):
        self.runs.append({"arguments": arguments, "timeout": timeout, "container": container})
        self.events.append("run")
        return self.answer


class RunTests(unittest.TestCase):
    setUp_rig = test_controls.RunnerControlTests.setUp
    tearDown = test_controls.RunnerControlTests.tearDown
    deployed = test_controls.RunnerControlTests.deployed

    def setUp(self):
        self.setUp_rig()
        self.target["variables"] = {"PLATFORM_NETWORK": "platform"}
        RunDocker.answer, RunDocker.runs = (0, RESULT), []

    def run_command(self, command=COMMAND, service="api", job="11111111-1111-4111-8111-111111111180"):
        return runner.control(self.target, "run", [service], "max", job, docker_factory=RunDocker, command=command)

    def test_a_run_executes_once_in_the_running_image_and_keeps_only_its_label_and_scrubbed_line(self):
        self.deployed()
        record = self.run_command()
        self.assertEqual("succeeded", record["status"], record.get("reason"))
        self.assertEqual(["run"], ControlDocker.events, "nothing stops or starts")
        [call] = RunDocker.runs
        arguments = call["arguments"]
        self.assertEqual(["docker", "run", "--rm", "--name", "pilot-dev-run-api-11111111", "--pull", "never",
                          "--network", "platform", "--security-opt", "no-new-privileges:true", "--cap-drop", "ALL",
                          "--label", "wheelhouse.run=pilot-dev"], arguments[:15])
        image = json.loads((self.bundle / "release.json").read_text())["images"]["api"]
        self.assertEqual(["--entrypoint", "dotnet", image, *COMMAND[1:]], arguments[-len(COMMAND) - 2:])
        self.assertNotIn(runner.MIGRATE_KEY, arguments)
        self.assertEqual((runner.RUN_TIMEOUT, "pilot-dev-run-api-11111111"), (call["timeout"], call["container"]))
        self.assertEqual({"kind": "run", "services": ["api"], "command": LABEL}, record["operation"])
        self.assertEqual(["Run admin create on api"], [step["name"] for step in record["steps"]])
        run = record["run"]
        self.assertEqual(("api", LABEL, "v1", "succeeded", 0, RESULT), tuple(
            run[key] for key in ("service", "command", "release", "status", "exitCode", "lastLine")))
        self.assertEqual("Exit 0 in 0s: " + RESULT, record["steps"][0]["detail"])
        saved = (self.base / "jobs" / (record["id"] + ".json")).read_text()
        for value in (PHONE, "Lola Karimova", "Ocharo\"", "DO_NOT_LOG"):
            self.assertNotIn(value, saved)
            self.assertNotIn(value, json.dumps(record))
        self.assertIsNone(record["stopped"])

    def test_a_non_zero_exit_a_timeout_and_a_container_docker_cannot_start_fail_the_job(self):
        self.deployed()
        refused = "admin-result {\"verb\":\"create\",\"exit\":1,\"status\":\"refused\",\"phone\":\"" + PHONE + "\"}"
        for answer, status, detail in (((1, refused), "failed", "Exit 1 in 0s: admin-result {\"verb\":\"create\","
                                        "\"exit\":1,\"status\":\"refused\",\"phone\":\"…\"}"),
                                       ((None, None), "timed out", "Timed out after 600s; the container was stopped"),
                                       ((125, None), "failed", "Docker could not start it (exit 125)"),
                                       ((2, None), "failed", "Exit 2 in 0s; no output")):
            with self.subTest(status=status, answer=answer[0]):
                RunDocker.answer = answer
                record = self.run_command(job="11111111-1111-4111-8111-1111111111{:02d}".format(len(RunDocker.runs)))
                self.assertEqual("failed", record["status"])
                self.assertEqual((status, detail), (record["run"]["status"], record["run"]["detail"]))
                self.assertEqual(detail, record["reason"])
                self.assertEqual(("failed", detail), (record["steps"][0]["status"], record["steps"][0]["detail"]))
                self.assertNotIn(PHONE, json.dumps(record))
                self.assertNotIn("not healthy", record["reason"])
        # A failed run needs no reconciliation: the next control or deploy goes ahead.
        self.assertEqual("ready", runner.state(self.target)["condition"])

    def test_a_run_needs_one_service_of_the_release_and_a_command_only_a_run_takes(self):
        self.deployed()
        for operation, services, command, refusal in (
                ("run", [], COMMAND, "Run a command in one service"),
                ("run", ["billing"], COMMAND, "has no service billing"),
                ("run", ["api"], None, "takes its command as 1 to 40 arguments"),
                ("run", ["api"], [], "takes its command as 1 to 40 arguments"),
                ("run", ["api"], ["dotnet", "a\nb"], "each one line"),
                ("run", ["api"], ["x"] * 41, "1 to 40 arguments"),
                ("run", ["api"], ["x" * 501], "at most 500 characters"),
                ("stop", ["api"], COMMAND, "Only a run takes a command")):
            with self.subTest(refusal=refusal), self.assertRaisesRegex(ValueError, refusal):
                runner.control(self.target, operation, services, "max", "11111111-1111-4111-8111-111111111181",
                               docker_factory=RunDocker, command=command)
        self.assertEqual([], RunDocker.runs)

    def test_a_run_goes_ahead_while_something_is_stopped_and_changes_nothing_stopped(self):
        self.deployed()
        test_controls.RunnerControlTests.control(self, "stop", ["worker"])
        record = self.run_command(service="worker")
        self.assertEqual("succeeded", record["status"], record.get("reason"))
        self.assertEqual(["worker"], record["stopped"]["services"])
        self.assertEqual(["worker"], runner.stopped_of(self.base)["services"])

    def test_wheelhouse_never_runs_a_one_off_on_itself(self):
        self.manifest_product("wheelhouse")
        with self.assertRaisesRegex(ValueError, "never run a one-off"):
            runner.control(self.target, "run", ["api"], "max", "11111111-1111-4111-8111-111111111182",
                           docker_factory=RunDocker, command=COMMAND)

    def manifest_product(self, product):
        """Deploys the release as `product`'s."""
        self.target["product"] = product
        manifest = json.loads((self.bundle / "release.json").read_text())
        manifest["product"] = product
        (self.bundle / "release.json").write_text(json.dumps(manifest))
        self.base = self.root / "state" / (product + "-dev")
        self.deployed()

    def test_a_run_launches_with_its_command_on_the_workers_stdin_never_as_an_argument(self):
        self.deployed()
        target_path = self.root / "target.json"
        runner.write_json(target_path, self.target)
        with patch.object(runner.subprocess, "Popen") as popen:
            queued = runner.control_launch(target_path, "run", ["api"], "max", command=COMMAND)
        argv = popen.call_args.args[0]
        self.assertEqual(["control", "--target", str(target_path.resolve()), "--operation", "run", "--actor", "max",
                          "--job", queued["id"], "--service", "api", "--stdin"], argv[2:])
        self.assertEqual(subprocess.PIPE, popen.call_args.kwargs["stdin"])
        written = popen.return_value.stdin.write.call_args.args[0]
        self.assertEqual({"command": COMMAND}, json.loads(written))
        job = runner.read_json(self.base / "jobs" / (queued["id"] + ".json"))
        self.assertEqual({"kind": "run", "services": ["api"], "command": LABEL}, job["operation"])
        self.assertNotIn(PHONE, json.dumps(job))
        with patch.object(runner.subprocess, "Popen") as popen, self.assertRaisesRegex(ValueError, "one service"):
            runner.control_launch(target_path, "run", [], "max", command=COMMAND)
        popen.assert_not_called()

    def test_the_run_reaches_the_history_observation(self):
        self.assertIn("run", history.OBSERVED)


INVITE = ("admin-result {\"verb\":\"invite\",\"exit\":0,\"link\":\"https://id.example.com/sign-up?invitation="
          + "Tk9" * 12 + "\",\"expiresAt\":\"2026-11-09T10:00:00Z\"}")


class RunRevealTests(unittest.TestCase):
    """A run whose last line the scrub cut keeps it as printed for one read, never in the job."""
    setUp_rig = test_controls.RunnerControlTests.setUp
    tearDown = RunTests.tearDown
    deployed = RunTests.deployed
    run_command = RunTests.run_command

    def setUp(self):
        RunTests.setUp(self)
        RunDocker.answer = (0, INVITE)

    def test_a_cut_line_is_kept_privately_and_read_once(self):
        self.deployed()
        record = self.run_command()
        self.assertEqual("succeeded", record["status"], record.get("reason"))
        self.assertIn("sign-up…", record["run"]["lastLine"])
        self.assertNotIn("Tk9Tk9", json.dumps(record))
        self.assertIn("expiresAt", record["run"]["reveal"])
        kept = self.base / runner.RUN_REVEALS / (record["id"] + ".json")
        self.assertEqual((0o600, 0o700), (kept.stat().st_mode & 0o777, kept.parent.stat().st_mode & 0o777))
        self.assertNotIn("Tk9Tk9", (self.base / "jobs" / (record["id"] + ".json")).read_text())
        self.assertEqual({"line": INVITE}, runner.reveal(self.target, record["id"]))
        self.assertFalse(kept.exists())
        with self.assertRaisesRegex(ValueError, "shown once already or expired"):
            runner.reveal(self.target, record["id"])

    def test_a_line_the_scrub_left_whole_keeps_nothing_to_reveal(self):
        self.deployed()
        RunDocker.answer = (0, RESULT)
        record = self.run_command()
        self.assertNotIn("reveal", record["run"])
        self.assertFalse((self.base / runner.RUN_REVEALS).exists() and any((self.base / runner.RUN_REVEALS).iterdir()))
        with self.assertRaisesRegex(ValueError, "kept no line"):
            runner.reveal(self.target, record["id"])

    def test_a_kept_line_expires_and_the_next_run_or_the_retention_removes_it(self):
        self.deployed()
        record = self.run_command()
        kept = self.base / runner.RUN_REVEALS / (record["id"] + ".json")
        later = runner.time.time() + runner.RUN_REVEAL_SECONDS + 1
        with self.assertRaisesRegex(ValueError, "expired"):
            runner.reveal(self.target, record["id"], clock=lambda: later)
        self.assertFalse(kept.exists())
        record = self.run_command(job="11111111-1111-4111-8111-111111111183")
        self.assertEqual(1, runner.prune_reveals(self.base / runner.RUN_REVEALS, clock=lambda: later))
        self.assertEqual(0, runner.prune(self.target)["reveals"])


class TransportRevealTests(unittest.TestCase):
    setUp = test_controls.TransportControlTests.setUp
    tearDown = test_controls.TransportControlTests.tearDown

    def test_only_a_run_of_the_named_target_is_read_and_its_line_goes_only_to_the_caller(self):
        local, remote = "33333333-3333-4333-8333-333333333333", "44444444-4444-4444-8444-444444444444"
        transport.write_json(self.root / "jobs" / (local + ".json"), {
            "id": local, "targetId": "pilot-dev", "remoteJobId": remote, "status": "succeeded",
            "operation": {"kind": "run", "services": ["api"], "command": "admin invite --role …"}})
        calls = []

        def fake_remote(ssh, action, target, *arguments, **options):
            calls.append((action, arguments))
            return {"line": INVITE}

        with patch.object(transport.fleet, "resolve_target", return_value=self.config), \
                patch.object(transport, "run_remote", side_effect=fake_remote):
            self.assertEqual({"line": INVITE}, transport.reveal(self.root, "pilot-dev", local))
            for target, job in (("pilot-prod", local), ("pilot-dev", remote)):
                with self.subTest(target=target, job=job), self.assertRaisesRegex(ValueError, "No run of"):
                    transport.reveal(self.root, target, job)
        self.assertEqual([("reveal", ("--job", remote))], calls)
        self.assertNotIn("Tk9Tk9", "".join(path.read_text() for path in self.root.rglob("*.json")))


class RunLabelTests(unittest.TestCase):
    def test_a_label_keeps_leading_words_and_option_names_never_a_value(self):
        self.assertEqual(LABEL, runner.run_label(COMMAND))
        self.assertEqual("admin … --x=… --flag", runner.run_label(["admin", PHONE, "--x=1", "--flag"]))
        self.assertEqual("app admin invite --role … --phone …",
                         runner.run_label(["app", "admin", "invite", "--role", "admin", "--phone", PHONE]))
        self.assertEqual("…", runner.run_label(["lola@example.com"]))
        self.assertEqual("admin create", runner.run_verb(COMMAND))
        self.assertEqual("app admin invite", runner.run_verb(["app", "admin", "invite", "--role", "admin"]))
        self.assertEqual("", runner.run_verb([PHONE]))

    def test_a_last_line_keeps_ids_and_cuts_phones_addresses_links_and_tokens(self):
        self.assertEqual(RESULT, runner.run_line(RESULT))
        line = runner.run_line("link https://id.example.com/admin/sign-in#token=abcdefghijklmnopqrstuvwxyz0123456789 "
                               "for +998 90 123-45-67 lola@example.com chat 123456789 at 2026-11-02T10:00:00Z "
                               "Password=hunter2 key " + "Ab9_" * 10)
        self.assertEqual("link https://id.example.com/admin/sign-in… for … …@example.com chat … at "
                         "2026-11-02T10:00:00Z Password=*** key …", line)
        self.assertIsNone(runner.run_line("  "))
        self.assertEqual(runner.RUN_LINE_LIMIT, len(runner.run_line("word " * 200)))


class RunDockerTests(unittest.TestCase):
    def test_the_last_nonblank_stdout_line_is_read_and_stderr_is_discarded(self):
        seen = []

        def fake_run(arguments, **options):
            seen.append(options)
            return subprocess.CompletedProcess(arguments, 0, "report\n" + RESULT + "\n\n   \n", None)

        with patch.object(runner.subprocess, "run", side_effect=fake_run):
            code, line = runner.Docker({"product": "pilot", "environment": "dev"}, {"PATH": "/bin"}).one_off(
                ["docker", "run"], 60, "box")
        self.assertEqual((0, RESULT), (code, line))
        self.assertEqual(subprocess.DEVNULL, seen[0]["stderr"])
        self.assertNotIn(runner.MIGRATE_KEY, seen[0]["env"])


class TransportRunTests(unittest.TestCase):
    setUp = test_controls.TransportControlTests.setUp
    tearDown = test_controls.TransportControlTests.tearDown

    def test_a_run_is_submitted_with_its_command_on_ssh_stdin_and_recorded_by_label(self):
        commands, inputs = [], []

        class FakeSsh:
            def __init__(self, ssh_config):
                pass

            def run(self, command, stdin=None, timeout=30):
                commands.append(command)
                inputs.append(stdin)
                return json.dumps({"id": "22222222-2222-4222-8222-222222222222", "status": "queued",
                                   "release": "v1"}) if "control-launch" in command else ""

            def copy(self, files, destination):
                pass

        with patch.object(transport.fleet, "resolve_target", return_value=self.config), \
                patch.object(transport, "Ssh", FakeSsh):
            submitted = transport.control(self.root, "pilot-dev", "run", "api", "max", reason="first owner",
                                          command=COMMAND)
        self.assertEqual({"kind": "run", "services": ["api"], "command": LABEL}, submitted["operation"])
        self.assertTrue(commands[-1].endswith("--operation run --actor max --service api --stdin"), commands[-1])
        self.assertEqual({"command": COMMAND}, json.loads(inputs[-1]))
        for command in commands:
            self.assertNotIn(PHONE, command)
        record = (self.root / "jobs" / (submitted["id"] + ".json")).read_text()
        self.assertNotIn(PHONE, record)
        self.assertEqual(LABEL, transport.jobs(self.root, controls=True)[0]["operation"]["command"])

    def test_a_run_names_one_service_and_a_command(self):
        for service, command, message in ((None, COMMAND, "Run a command in one service"),
                                          ("api", None, "takes its command"),
                                          ("api", ["a\nb"], "each one line")):
            with self.subTest(message=message), self.assertRaisesRegex(ValueError, message):
                transport.control(self.root, "pilot-dev", "run", service, "max", command=command)
        with self.assertRaisesRegex(ValueError, "Only a run takes a command"):
            transport.control(self.root, "pilot-dev", "restart", "api", "max", command=COMMAND)


if __name__ == "__main__":
    unittest.main()
