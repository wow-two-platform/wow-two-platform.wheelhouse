import base64
import contextlib
import copy
import hashlib
import http.server
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import tarfile
import threading
import unittest
from unittest.mock import patch
import runner
import transport


class FakeDocker:
    events = []
    fail = None

    def __init__(self, target, environment):
        pass

    def preflight(self, manifest):
        self.events.append("preflight")

    def pull(self, bundle):
        self.events.append("pull")
        if self.fail == "pull":
            raise RuntimeError("password=DO_NOT_LOG")

    def up(self, bundle):
        release = runner.read_json(Path(bundle) / "release.json")["release"]
        self.events.append("up:" + release)
        if self.fail == release:
            raise RuntimeError("password=DO_NOT_LOG")

    def verify(self, bundle, manifest):
        self.events.append("verify:" + manifest["release"])

    def unhealthy(self, bundle):
        return ["api (unhealthy)"]

    def execute(self, arguments, step, timeout=600):
        self.events.append(" ".join(arguments[:3]) + ":" + arguments[-1].rsplit(":", 1)[-1][:4])
        return ""


class CheckDocker:
    architecture = "linux/amd64"

    def __init__(self, target, environment):
        pass

    def execute(self, arguments, step, timeout=600):
        if arguments[:2] == ["docker", "info"]:
            return self.architecture + "\n"
        if arguments[:3] == ["docker", "compose", "version"]:
            return "2.29.0\n"
        return "platform\n"


class VitalsDocker:
    fail = False

    def __init__(self, target, environment):
        pass

    def execute(self, arguments, step, timeout=600):
        if self.fail:
            raise runner.CommandFailed("docker ps failed (exit 1); inspect target containers privately")
        if arguments[:2] == ["docker", "ps"]:
            return "aaa111\nbbb222\n"
        if arguments[:2] == ["docker", "inspect"]:
            return json.dumps([
                {"Id": "aaa111", "Name": "/pilot-test-api-1", "RestartCount": 3,
                 "Config": {"Env": ["Database__Connection=DO_NOT_LOG"],
                            "Labels": {"com.docker.compose.service": "api", "secret": "DO_NOT_LOG"}},
                 "State": {"Status": "running", "Running": True, "ExitCode": 0,
                           "StartedAt": "2026-09-26T08:00:00Z", "Health": {"Status": "healthy", "Log": ["DO_NOT_LOG"]}}},
                {"Id": "bbb222", "Name": "/pilot-test-worker-1", "RestartCount": 0,
                 "Config": {"Labels": {"com.docker.compose.service": "worker"}},
                 "State": {"Status": "exited", "Running": False, "ExitCode": 137, "StartedAt": "2026-09-26T07:00:00Z"}}])
        if arguments[:2] == ["docker", "stats"]:
            self.stats_ids = arguments[6:]
            return json.dumps({"ID": "aaa111", "CPUPerc": "12.50%", "MemUsage": "256MiB / 1GiB"}) + "\n"
        raise AssertionError(arguments)


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        settings = self.root / "settings.json"
        settings.write_text(json.dumps({"Database": {"Connection": "DO_NOT_LOG"}}))
        settings.chmod(0o600)
        self.target = {"product": "pilot", "environment": "test", "root": str(self.root / "state"),
                       "settings": {"api": str(settings)}, "minimumFreeBytes": 0}
        self.bundle = self.root / "bundle"
        self.bundle.mkdir()
        self.manifest = {"schemaVersion": 1, "product": "pilot", "release": "v1",
                         "sourceCommit": "a" * 40, "platform": "linux/amd64", "rollbackCompatible": False,
                         "images": {"api": "ghcr.io/owner/api@sha256:" + "a" * 64},
                         "requiredConfiguration": {"api": ["Database:Connection"]}}
        self.compose = {"services": {"api": {"image": self.manifest["images"]["api"], "platform": "linux/amd64",
                        "healthcheck": {"test": ["CMD", "curl", "-f", "http://localhost/health"]}}}}
        self.save()
        FakeDocker.events = []
        FakeDocker.fail = None

    def tearDown(self):
        self.temporary.cleanup()

    def save(self):
        compose = json.dumps(self.compose).encode()
        (self.bundle / "compose.json").write_bytes(compose)
        self.manifest["composeSha256"] = hashlib.sha256(compose).hexdigest()
        (self.bundle / "release.json").write_text(json.dumps(self.manifest))

    def apply(self):
        return runner.apply(self.bundle, self.target, "test-operator", docker_factory=FakeDocker)

    def test_success_saves_auditable_current_and_redacts_settings(self):
        result = self.apply()
        self.assertEqual("succeeded", result["status"])
        self.assertEqual(["preflight", "pull", "up:v1", "verify:v1"], FakeDocker.events)
        current = runner.read_json(self.root / "state/pilot-test/current.json")
        self.assertEqual(result["id"], current["id"])
        self.assertNotIn("DO_NOT_LOG", json.dumps(result))

    def test_hash_mismatch_fails_before_docker(self):
        (self.bundle / "compose.json").write_text("{}")
        with self.assertRaisesRegex(ValueError, "hash"):
            self.apply()
        self.assertEqual([], FakeDocker.events)

    def test_mutable_image_rejected(self):
        self.manifest["images"]["api"] = "ghcr.io/owner/api:latest"
        self.compose["services"]["api"]["image"] = self.manifest["images"]["api"]
        self.save()
        with self.assertRaisesRegex(ValueError, "immutable"):
            self.apply()

    def test_wrong_product_rejected(self):
        self.target["product"] = "other"
        with self.assertRaisesRegex(ValueError, "product mismatch"):
            self.apply()

    def test_settings_permissions_rejected(self):
        self.root.chmod(0o755)
        Path(self.target["settings"]["api"]).chmod(0o644)
        with self.assertRaisesRegex(ValueError, "private"):
            self.apply()

    def test_missing_configuration_rejected(self):
        self.manifest["requiredConfiguration"]["api"] = ["Missing"]
        self.save()
        with self.assertRaisesRegex(ValueError, "Missing required"):
            self.apply()

    def test_failure_before_mutation_keeps_previous(self):
        prior = self.apply()
        FakeDocker.fail = "pull"
        self.manifest["release"] = "v2"
        self.save()
        result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertEqual(prior["id"], runner.read_json(self.root / "state/pilot-test/current.json")["id"])
        self.assertNotIn("DO_NOT_LOG", json.dumps(result))
        self.assertEqual(1, FakeDocker.events.count("up:v1"))

    def test_compatible_failure_restores_previous_images(self):
        prior = self.apply()
        self.manifest.update(release="v2", rollbackCompatible=True)
        self.save()
        FakeDocker.fail = "v2"
        result = self.apply()
        self.assertEqual("rolled_back", result["status"])
        self.assertEqual(prior["id"], runner.read_json(self.root / "state/pilot-test/current.json")["id"])
        self.assertEqual(["up:v2", "pull", "up:v1", "verify:v1"], FakeDocker.events[-4:])

    def test_incompatible_failure_never_rolls_back(self):
        self.apply()
        self.manifest["release"] = "v2"
        self.save()
        FakeDocker.fail = "v2"
        self.assertEqual("failed", self.apply()["status"])
        self.assertEqual("up:v2", FakeDocker.events[-1])

    def test_interrupted_rollout_requires_explicit_acknowledgement(self):
        job = "00000000-0000-0000-0000-000000000001"
        runner.write_json(self.root / "state/pilot-test/active.json", {"id": job, "status": "running"})
        with self.assertRaisesRegex(ValueError, "reconciliation"):
            self.apply()
        with self.assertRaisesRegex(ValueError, "matching"):
            runner.acknowledge(self.target, "wrong")
        runner.acknowledge(self.target, job)
        self.assertEqual("succeeded", self.apply()["status"])

    def test_concurrent_lock_rejects_second_operator(self):
        with runner.deployment_lock(self.root / "state/pilot-test"):
            with self.assertRaisesRegex(ValueError, "Another deployment"):
                self.apply()

    def test_missing_health_check_rejected(self):
        self.compose["services"]["api"].pop("healthcheck")
        self.save()
        with self.assertRaisesRegex(ValueError, "health gate"):
            self.apply()

    def test_environment_excludes_ambient_compose_and_application_secrets(self):
        with patch.dict(os.environ, {"COMPOSE_FILE": "/tmp/evil", "DB_CONNECTION": "DO_NOT_LOG"}):
            environment = runner.environment_for(self.target, self.manifest)
        self.assertNotIn("COMPOSE_FILE", environment)
        self.assertNotIn("DB_CONNECTION", environment)

    def test_target_cannot_override_compose_controls(self):
        self.target["variables"] = {"COMPOSE_FILE": "/tmp/evil"}
        with self.assertRaisesRegex(ValueError, "Invalid target variable"):
            self.apply()

    def test_source_copy_is_validated_again(self):
        original = runner.validate_bundle
        calls = []
        def validate(path):
            calls.append(Path(path))
            return original(path)
        with patch.object(runner, "validate_bundle", side_effect=validate):
            self.apply()
        self.assertEqual(2, len(calls))
        self.assertNotEqual(calls[0], calls[1])

    def test_unrecovered_mutation_blocks_future_rollouts(self):
        self.apply()
        self.manifest["release"] = "v2"
        self.save()
        FakeDocker.fail = "v2"
        failed = self.apply()
        with self.assertRaisesRegex(ValueError, "reconciliation"):
            self.apply()
        runner.acknowledge(self.target, failed["id"])
        self.assertFalse((self.root / "state/pilot-test/current.json").exists())

    def test_rollback_failure_never_reports_recovered(self):
        self.apply()
        self.manifest.update(release="v2", rollbackCompatible=True)
        self.save()
        with patch.object(FakeDocker, "up", side_effect=RuntimeError("failure")):
            self.assertEqual("rollback_failed", self.apply()["status"])

    def test_cli_rollback_is_a_failed_requested_deployment(self):
        target = self.root / "target.json"
        runner.write_json(target, self.target)
        with patch("sys.argv", ["runner.py", "apply", "--target", str(target), "--bundle", str(self.bundle)]):
            with patch.object(runner, "apply", return_value={"status": "rolled_back"}):
                self.assertEqual(1, runner.main())

    def test_archive_import_validates_and_never_overwrites_a_release(self):
        archive = self.root / "release.tar.gz"
        with tarfile.open(archive, "w:gz") as package:
            for name in ("release.json", "compose.json"):
                package.add(self.bundle / name, arcname=name)
        inventory = self.root / "inventory"
        result = transport.import_bundle(inventory, archive, "pilot-v1")
        self.assertEqual("pilot-v1", result["id"])
        self.assertEqual("v1", runner.validate_bundle(inventory / "bundles/pilot-v1")["release"])
        with self.assertRaisesRegex(ValueError, "already exists"):
            transport.import_bundle(inventory, archive, "pilot-v1")

    def test_archive_import_rejects_links(self):
        archive = self.root / "release.tar.gz"
        with tarfile.open(archive, "w:gz") as package:
            member = tarfile.TarInfo("release.json")
            member.type = tarfile.SYMTYPE
            member.linkname = "/etc/passwd"
            package.addfile(member)
            package.add(self.bundle / "compose.json", arcname="compose.json")
        with self.assertRaisesRegex(ValueError, "Invalid archive"):
            transport.import_bundle(self.root / "inventory", archive, "pilot-v1")

    def write_settings(self, value):
        Path(self.target["settings"]["api"]).write_text(json.dumps(value))

    def test_restrictive_allowed_hosts_without_localhost_rejected_before_docker(self):
        self.write_settings({"Database": {"Connection": "DO_NOT_LOG"}, "AllowedHosts": "manage.example.test"})
        with self.assertRaisesRegex(runner.Rejected, "AllowedHosts must include localhost for health probes: api"):
            self.apply()
        self.assertEqual([], FakeDocker.events)

    def test_allowed_hosts_accepts_localhost_or_wildcard(self):
        for hosts in ("manage.example.test; LOCALHOST", "*", ""):
            self.write_settings({"Database": {"Connection": "DO_NOT_LOG"}, "AllowedHosts": hosts})
            self.assertEqual("succeeded", self.apply()["status"])

    def test_trusted_proxies_must_be_an_array(self):
        self.write_settings({"Database": {"Connection": "DO_NOT_LOG"}, "Deployment": {"TrustedProxies": "172.18.0.2"}})
        with self.assertRaisesRegex(runner.Rejected, "Deployment:TrustedProxies must be a JSON array: api"):
            self.apply()
        self.write_settings({"Database": {"Connection": "DO_NOT_LOG"}, "Deployment": {"TrustedProxies": ["172.18.0.2"]}})
        self.assertEqual("succeeded", self.apply()["status"])

    def test_cli_rejection_records_the_missing_key_but_no_values(self):
        self.manifest["requiredConfiguration"]["api"] = ["Database:Connection", "Billing:SecretKey"]
        self.save()
        target = self.root / "target.json"
        runner.write_json(target, self.target)
        job = "00000000-0000-0000-0000-000000000003"
        argv = ["runner.py", "apply", "--target", str(target), "--bundle", str(self.bundle), "--job", job]
        with patch("sys.argv", argv), contextlib.redirect_stderr(io.StringIO()) as stderr:
            self.assertEqual(1, runner.main())
        record = runner.read_json(self.root / "state/pilot-test/jobs" / (job + ".json"))
        self.assertEqual(("rejected", "Rejected"), (record["status"], record["failure"]))
        self.assertEqual("Missing required setting: api:Billing:SecretKey", record["reason"])
        self.assertEqual(record["reason"], json.loads(stderr.getvalue())["reason"])
        self.assertNotIn("DO_NOT_LOG", json.dumps(record) + stderr.getvalue())

    def test_failed_wait_reports_the_step_and_unhealthy_services(self):
        with patch.object(FakeDocker, "up", side_effect=runner.CommandFailed("compose up failed (exit 1)")):
            result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertEqual("compose up failed (exit 1); not healthy: api (unhealthy)", result["reason"])

    def test_unsafe_failure_messages_never_become_reasons(self):
        FakeDocker.fail = "pull"
        result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertNotIn("reason", result)

    def test_docker_failure_names_the_step_without_output(self):
        failed = subprocess.CompletedProcess([], 1, stdout="", stderr="password=DO_NOT_LOG")
        with patch.object(runner.subprocess, "run", return_value=failed):
            with self.assertRaisesRegex(runner.CommandFailed, r"^compose up failed \(exit 1\)") as raised:
                runner.Docker(self.target, {}).up(self.bundle)
        self.assertNotIn("DO_NOT_LOG", str(raised.exception))

    def test_state_reports_ready_running_and_reconciliation(self):
        base = self.root / "state/pilot-test"
        self.assertEqual("ready", runner.state(self.target)["condition"])
        job = "00000000-0000-0000-0000-000000000004"
        runner.write_json(base / "active.json", {"id": job, "status": "running", "release": "v1"})
        with runner.deployment_lock(base):
            self.assertEqual("running", runner.state(self.target)["condition"])
        interrupted = runner.state(self.target)
        self.assertEqual("needs_reconciliation", interrupted["condition"])
        self.assertEqual(job, interrupted["active"]["id"])

    def test_state_does_not_lock_a_failure_that_changed_nothing(self):
        FakeDocker.fail = "pull"
        self.apply()
        self.assertEqual("ready", runner.state(self.target)["condition"])
        with self.assertRaisesRegex(runner.Rejected, "nothing to reconcile"):
            runner.acknowledge(self.target, runner.state(self.target)["active"]["id"])

    def test_check_names_problems_without_changing_the_target(self):
        self.manifest["requiredConfiguration"]["api"] = ["Database:Connection", "Billing:SecretKey"]
        with patch.object(CheckDocker, "architecture", "linux/arm64"):
            result = runner.check(self.target, self.manifest, docker_factory=CheckDocker)
        failed = {item["name"]: item["detail"] for item in result["checks"] if not item["ok"]}
        self.assertFalse(result["ok"])
        self.assertEqual("Target is linux/arm64; release needs linux/amd64", failed["Docker"])
        self.assertEqual("Missing required setting: api:Billing:SecretKey", failed["Settings: api"])
        self.assertFalse((self.root / "state/pilot-test").exists())
        self.assertNotIn("DO_NOT_LOG", json.dumps(result))

    def test_check_passes_a_ready_target(self):
        result = runner.check(self.target, self.manifest, docker_factory=CheckDocker)
        self.assertTrue(result["ok"], result["checks"])
        self.assertEqual("ready", result["state"]["condition"])

    def test_cli_state_accepts_an_inline_target(self):
        encoded = base64.b64encode(json.dumps(self.target).encode()).decode()
        with patch("sys.argv", ["runner.py", "state", "--target-json", encoded]), \
                contextlib.redirect_stdout(io.StringIO()) as stdout:
            self.assertEqual(0, runner.main())
        self.assertEqual("ready", json.loads(stdout.getvalue())["condition"])

    def test_every_read_only_cli_action_runs_through_main(self):
        # Each action must resolve its handler inside main(); a local name there would shadow it.
        encoded = base64.b64encode(json.dumps(self.target).encode()).decode()
        with patch.object(runner, "Docker", CheckDocker):
            for action in ("state", "check", "topology"):
                with self.subTest(action=action), patch("sys.argv", ["runner.py", action, "--target-json", encoded]), \
                        contextlib.redirect_stdout(io.StringIO()) as stdout:
                    self.assertEqual(0, runner.main())
                    self.assertIsInstance(json.loads(stdout.getvalue()), dict)

    def test_unhealthy_reads_both_compose_ps_formats(self):
        rows = [{"Service": "api", "State": "running", "Health": "unhealthy"},
                {"Service": "web", "State": "running", "Health": "healthy"},
                {"Service": "worker", "State": "exited", "Health": ""}]
        for output in (json.dumps(rows), "\n".join(map(json.dumps, rows))):
            with patch.object(runner.Docker, "compose", return_value=output):
                self.assertEqual(["api (unhealthy)", "worker (exited)"],
                                 runner.Docker(self.target, {}).unhealthy(self.bundle))


    def test_vitals_read_containers_without_secrets_and_leave_the_host_to_the_server(self):
        VitalsDocker.fail = False
        result = runner.vitals(self.target, docker_factory=VitalsDocker)
        api, worker = result["containers"]
        self.assertEqual(("api", "running", "healthy", 3, 12.5, 256 * 1024 ** 2, 1024 ** 3, None),
                         (api["service"], api["state"], api["health"], api["restarts"], api["cpuPercent"],
                          api["memoryBytes"], api["memoryLimitBytes"], api["exitCode"]))
        self.assertEqual(("worker", "exited", 137, None), (worker["service"], worker["state"], worker["exitCode"],
                                                           worker["cpuPercent"]))
        self.assertEqual(("pilot-test", "ready", None, []),
                         (result["project"], result["condition"], result["release"], result["problems"]))
        self.assertNotIn("host", result)
        self.assertNotIn("DO_NOT_LOG", json.dumps(result))
        self.assertFalse((self.root / "state/pilot-test").exists())

    def test_host_reads_the_server_without_a_target(self):
        result = runner.host({"roots": [str(self.root / "not-yet-created"), "/srv/wheelhouse"]})
        self.assertEqual([], result["problems"])
        self.assertGreaterEqual(len(result["host"]["disks"]), 1)
        self.assertTrue(all(Path(disk["path"]).is_absolute() for disk in result["host"]["disks"]))
        for document in (None, {}, {"roots": []}, {"roots": ["relative"]}, {"roots": [7]}, {"roots": ["/srv"] * 33}):
            with self.subTest(document=document), self.assertRaisesRegex(ValueError, "Invalid host roots"):
                runner.host(document)
        with patch.object(runner, "host_vitals", side_effect=OSError):
            self.assertEqual({"host": None, "problems": ["Host vitals were unreadable"]}, runner.host({"roots": ["/srv"]}))

    def test_vitals_report_a_docker_failure_as_a_problem(self):
        VitalsDocker.fail = True
        try:
            result = runner.vitals(self.target, docker_factory=VitalsDocker)
        finally:
            VitalsDocker.fail = False
        self.assertIsNone(result["containers"])
        self.assertEqual(["docker ps failed (exit 1); inspect target containers privately"], result["problems"])

    def test_vitals_correlate_only_a_stable_ready_release(self):
        observed = {"project": "pilot-test", "condition": "ready",
                    "current": {"id": "first", "release": "v1"}, "active": {"id": "first", "status": "succeeded"}}
        with patch.object(runner, "state", return_value=observed), \
             patch.object(runner, "container_vitals", return_value=[{"service": "api"}]):
            result = runner.vitals(self.target, docker_factory=VitalsDocker)
        self.assertEqual("v1", result["release"])
        self.assertEqual([], result["problems"])

    def test_vitals_withhold_release_when_deployment_changes_during_collection(self):
        before = {"project": "pilot-test", "condition": "ready",
                  "current": {"id": "first", "release": "v1"}, "active": {"id": "first", "status": "succeeded"}}
        for after in (
            {**before, "current": {"id": "second", "release": "v2"}},
            {**before, "current": {"id": "second", "release": "v1"}},
            {**before, "active": {"id": "second", "status": "rolled_back"}},
        ):
            with self.subTest(after=after), patch.object(runner, "state", side_effect=[before, after]), \
                 patch.object(runner, "container_vitals", return_value=[{"service": "api"}]):
                result = runner.vitals(self.target, docker_factory=VitalsDocker)
                self.assertIsNone(result["release"])
                self.assertEqual([{"service": "api"}], result["containers"])
                self.assertTrue(any("changed during observation" in problem for problem in result["problems"]))

    def test_vitals_withhold_release_while_target_is_not_ready(self):
        for condition in ("running", "needs_reconciliation"):
            observed = {"project": "pilot-test", "condition": condition,
                        "current": {"id": "first", "release": "v1"}, "active": {"id": "second", "status": "running"}}
            with self.subTest(condition=condition), patch.object(runner, "state", return_value=observed), \
                 patch.object(runner, "container_vitals", return_value=[{"service": "api"}]):
                result = runner.vitals(self.target, docker_factory=VitalsDocker)
                self.assertIsNone(result["release"])
                self.assertEqual(condition, result["condition"])
                self.assertTrue(any("not ready" in problem for problem in result["problems"]))

    def test_docker_sizes_and_percentages_parse(self):
        self.assertEqual((1536, 2 * 1000 ** 3, None), (runner.size_bytes("1.5KiB"), runner.size_bytes(" 2GB "),
                                                        runner.size_bytes("12 parsecs")))
        self.assertEqual((0.25, None), (runner.percent("0.25%"), runner.percent("--")))

    def local_ingress(self, **overrides):
        return {"scheme": "http", "port": 18080, "entryPoints": ["web"], "privateEntryPoints": ["web"],
                "certResolver": None, "pattern": "{site}-{product}.{environment}.localhost", "hosts": {}, **overrides}

    def test_success_publishes_routes_and_remembers_sites_and_versions(self):
        self.target["ingress"] = self.local_ingress()
        self.manifest.update(kind="candidate", branch="main", release="sha-aaaaaaa",
                             versions={"api": {"version": "1.2.0+aaaaaaa", "changedIn": "sha-aaaaaaa"}},
                             sites={"api": {"app": {"port": 8080}}})
        self.save()
        result = self.apply()
        self.assertEqual("succeeded", result["status"])
        route = runner.read_json(self.root / "state/ingress/pilot-test.yml")
        self.assertEqual({"pilot-test-app-api": {"rule": "Host(`app-pilot.test.localhost`)",
                                                 "service": "pilot-test-api-8080", "entryPoints": ["web"]}},
                         route["http"]["routers"])
        self.assertEqual("http://pilot-test-api:8080",
                         route["http"]["services"]["pilot-test-api-8080"]["loadBalancer"]["servers"][0]["url"])
        current = runner.state(self.target)["current"]
        self.assertEqual([{"name": "app", "service": "api", "exposure": "public",
                           "url": "http://app-pilot.test.localhost:18080"}], current["sites"])
        self.assertEqual(("candidate", "main", "1.2.0+aaaaaaa"),
                         (current["kind"], current["branch"], current["versions"]["api"]["version"]))

    def test_https_routes_carry_the_resolver_and_named_hosts_win_over_the_pattern(self):
        self.target["ingress"] = self.local_ingress(scheme="https", port=None, entryPoints=["websecure"],
                                                    certResolver="letsencrypt", hosts={"app": "app.pilot.example"})
        self.manifest["sites"] = {"api": {"app": {"path": "/api"}}}
        self.save()
        self.apply()
        route = runner.read_json(self.root / "state/ingress/pilot-test.yml")["http"]["routers"]["pilot-test-app-api"]
        self.assertEqual(("Host(`app.pilot.example`) && PathPrefix(`/api`)", {"certResolver": "letsencrypt"}),
                         (route["rule"], route["tls"]))
        self.assertEqual("https://app.pilot.example/api", runner.state(self.target)["current"]["sites"][0]["url"])

    def test_private_sites_route_only_on_private_entry_points(self):
        self.target["ingress"] = self.local_ingress(privateEntryPoints=[])
        self.manifest["sites"] = {"api": {"admin": {"exposure": "private"}}}
        self.save()
        self.assertEqual([], self.apply()["sites"])
        self.assertFalse((self.root / "state/ingress/pilot-test.yml").exists())

    def test_a_release_without_sites_removes_the_previous_routes(self):
        self.target["ingress"] = self.local_ingress()
        self.manifest["sites"] = {"api": {"app": {}}}
        self.save()
        self.apply()
        del self.manifest["sites"]
        self.manifest["release"] = "v2"
        self.save()
        self.assertEqual("succeeded", self.apply()["status"])
        self.assertFalse((self.root / "state/ingress/pilot-test.yml").exists())

    def test_invalid_sites_are_refused_before_docker(self):
        for sites, message in (({"worker": {"app": {}}}, "unknown service"), ({"api": {"App": {}}}, "site declaration"),
                               ({"api": {"app": {"port": 0}}}, "site port"), ({"api": {"app": {"path": "api"}}}, "site path"),
                               ({"api": {"app": {"exposure": "world"}}}, "site exposure")):
            with self.subTest(message=message):
                self.manifest["sites"] = sites
                self.save()
                with self.assertRaisesRegex(ValueError, message):
                    self.apply()
                self.assertEqual([], FakeDocker.events)

    def test_two_services_may_share_a_site_only_by_path(self):
        self.manifest["images"]["web"] = "ghcr.io/owner/web@sha256:" + "b" * 64
        self.compose["services"]["web"] = {**self.compose["services"]["api"], "image": self.manifest["images"]["web"]}
        self.manifest["sites"] = {"web": {"app": {}}, "api": {"app": {}}}
        self.save()
        with self.assertRaisesRegex(ValueError, "same site path"):
            runner.validate_bundle(self.bundle)
        self.manifest["sites"]["api"]["app"] = {"path": "/api"}
        self.save()
        self.assertEqual(["api", "web"], [site["service"] for site in runner.release_sites(runner.validate_bundle(self.bundle))])

    def test_a_host_pattern_producing_an_invalid_host_refuses_before_docker(self):
        self.target["ingress"] = self.local_ingress(pattern="{site}_{product}.localhost")
        self.manifest["sites"] = {"api": {"app": {}}}
        self.save()
        with self.assertRaisesRegex(ValueError, "invalid host"):
            self.apply()
        self.assertEqual([], FakeDocker.events)

    def test_a_service_without_settings_needs_no_settings_file(self):
        self.manifest["images"]["edge"] = "ghcr.io/owner/edge@sha256:" + "c" * 64
        self.compose["services"]["edge"] = {**self.compose["services"]["api"], "image": self.manifest["images"]["edge"]}
        self.save()
        self.assertEqual("succeeded", self.apply()["status"])
        self.manifest["requiredConfiguration"]["ghost"] = []
        self.save()
        with self.assertRaisesRegex(ValueError, "unknown service"):
            runner.validate_bundle(self.bundle)

    def test_steps_record_each_stage_of_a_successful_rollout(self):
        self.target["smoke"] = [{"service": "api", "path": "/health"}]
        result = self.apply()
        self.assertEqual([("Check target", "succeeded"), ("Pull images", "succeeded"), ("Start containers", "succeeded"),
                          ("Verify services", "succeeded"), ("Publish sites", "succeeded"),
                          ("Remove unused images", "skipped")],
                         [(step["name"], step["status"]) for step in result["steps"]])
        self.assertEqual("1 service healthy; 1 smoke check passed", result["steps"][3]["detail"])
        self.assertTrue(all(step["completedAt"] >= step["startedAt"] for step in result["steps"]))
        saved = runner.read_json(self.root / "state/pilot-test/jobs" / (result["id"] + ".json"))
        self.assertEqual(result["steps"], saved["steps"])

    def test_steps_mark_the_failed_stage_and_the_rollback(self):
        self.apply()
        self.manifest.update(release="v2", rollbackCompatible=True)
        self.save()
        FakeDocker.fail = "v2"
        result = self.apply()
        self.assertEqual("rolled_back", result["status"])
        names = [(step["name"], step["status"]) for step in result["steps"]]
        self.assertEqual([("Check target", "succeeded"), ("Pull images", "succeeded"), ("Start containers", "failed"),
                          ("Roll back to v1", "succeeded")], names)
        self.assertNotIn("detail", result["steps"][2])  # an unsafe exception message never becomes a detail
        self.assertNotIn("DO_NOT_LOG", json.dumps(result))

    def test_a_success_removes_images_only_older_releases_used(self):
        shared = "ghcr.io/owner/edge@sha256:" + "e" * 64
        self.manifest["images"]["edge"] = shared
        self.compose["services"]["edge"] = {**self.compose["services"]["api"], "image": shared}
        for number in range(1, 6):
            image = "ghcr.io/owner/api@sha256:" + str(number) * 64
            self.manifest.update(release="v" + str(number), images={"api": image, "edge": shared})
            self.compose["services"]["api"]["image"] = image
            self.save()
            FakeDocker.events = []
            result = self.apply()
            # Distinct mtimes order the releases the way their rollouts ran.
            manifest = self.root / "state/pilot-test/releases" / result["id"] / "release.json"
            os.utime(manifest, (number * 1000, number * 1000))
        removed = [event for event in FakeDocker.events if event.startswith("docker image rm")]
        # v3-v5 stay; v1's image went on the v4 rollout and v2's now; the shared edge image stays with v5.
        self.assertEqual(["docker image rm:2222"], removed)
        self.assertEqual(("Remove unused images", "succeeded", "1 image from older releases"),
                         tuple(result["steps"][-1][key] for key in ("name", "status", "detail")))

    def test_a_site_that_does_not_answer_warns_without_failing_the_rollout(self):
        self.target["ingress"] = self.local_ingress(probe="http://ingress:80")
        self.manifest["sites"] = {"api": {"app": {}}}
        self.save()

        def prober(target, sites):
            sites[0]["probe"] = {"ok": False, "status": 404, "detail": "The ingress has no route for this host"}
            return sites

        result = runner.apply(self.bundle, self.target, "test-operator", docker_factory=FakeDocker, prober=prober)
        self.assertEqual("succeeded", result["status"])
        probe = next(step for step in result["steps"] if step["name"] == "Probe sites")
        self.assertEqual(("warning", "0 of 1 site answered"), (probe["status"], probe["detail"]))
        self.assertEqual(["Site app did not answer through the ingress: The ingress has no route for this host"],
                         result["warnings"])
        self.assertFalse(runner.state(self.target)["current"]["sites"][0]["probe"]["ok"])

    def test_a_failing_prober_never_fails_a_healthy_rollout(self):
        self.target["ingress"] = self.local_ingress(probe="http://ingress:80")
        self.manifest["sites"] = {"api": {"app": {}}}
        self.save()

        def prober(target, sites):
            raise RuntimeError("password=DO_NOT_LOG")

        result = runner.apply(self.bundle, self.target, "test-operator", docker_factory=FakeDocker, prober=prober)
        probe = next(step for step in result["steps"] if step["name"] == "Probe sites")
        self.assertEqual(("succeeded", "warning"), (result["status"], probe["status"]))
        self.assertNotIn("DO_NOT_LOG", json.dumps(result))

    def test_cli_rejection_records_a_failed_check_step(self):
        (self.root / "settings.json").write_text(json.dumps({}))
        job = "22222222-2222-4222-8222-222222222222"
        target = self.root / "target.json"
        target.write_text(json.dumps(self.target))
        with patch("sys.argv", ["runner.py", "apply", "--bundle", str(self.bundle), "--target", str(target),
                                "--job", job]), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(1, runner.main())
        record = runner.read_json(self.root / "state/pilot-test/jobs" / (job + ".json"))
        self.assertEqual("rejected", record["status"])
        self.assertEqual(("Check target", "failed", "Missing required setting: api:Database:Connection"),
                         tuple(record["steps"][0][key] for key in ("name", "status", "detail")))

    def test_ports_are_validated_and_smoke_checks_use_the_declared_port(self):
        self.manifest["ports"] = {"api": 70000}
        self.save()
        with self.assertRaisesRegex(ValueError, "service ports"):
            runner.validate_bundle(self.bundle)
        self.manifest["ports"] = {"api": 80}
        self.save()
        calls = []

        class SmokeDocker(runner.Docker):
            def compose(self, bundle, *arguments):
                calls.append(arguments)
                return "cid\n" if arguments[0] == "ps" else "200"

            def execute(self, arguments, step, timeout=600):
                return json.dumps([{"Config": {"Image": self.image}, "State": {"Health": {"Status": "healthy"}}}])

        SmokeDocker.image = self.manifest["images"]["api"]
        self.target["smoke"] = [{"service": "api", "path": "/health"}]
        SmokeDocker(self.target, {}).verify(self.bundle, runner.validate_bundle(self.bundle))
        self.assertEqual("http://localhost:80/health", calls[-1][-1])

    def test_logs_read_one_service_bounded_and_refuse_a_missing_container(self):
        class LogDocker:
            containers = "abc123\n"

            def __init__(self, target, environment):
                pass

            def execute(self, arguments, step, timeout=600):
                self.filters = [argument for argument in arguments if argument.startswith("label=")]
                return self.containers

            def output(self, arguments, step, timeout=60):
                return "2026-09-28T10:00:00Z started\n2026-09-28T10:00:01Z " + "x" * 3000 + "\n"

        result = runner.logs(self.target, "api", 2, docker_factory=LogDocker)
        self.assertEqual(("pilot-test", "api", 2, True), (result["project"], result["service"], result["tail"],
                                                          result["truncated"]))
        self.assertEqual(runner.LOG_LINE_LIMIT, len(result["lines"][1]))
        for service, tail, message in (("API", 10, "Invalid service"), ("api", 0, "1-1000"), ("api", 1001, "1-1000")):
            with self.subTest(service=service, tail=tail), self.assertRaisesRegex(ValueError, message):
                runner.logs(self.target, service, tail, docker_factory=LogDocker)
        LogDocker.containers = ""
        with self.assertRaisesRegex(ValueError, "No container"):
            runner.logs(self.target, "api", 10, docker_factory=LogDocker)

    def test_versions_and_kind_are_validated(self):
        for field, value, message in (("kind", "nightly", "release kind"),
                                      ("versions", {"api": {"version": "1.0", "changedIn": "no spaces"}}, "service version"),
                                      ("versions", {"worker": {"version": "1.0", "changedIn": "v1"}}, "unknown service"),
                                      ("branch", "feature x", "Invalid branch")):
            with self.subTest(field=field, message=message):
                manifest = copy.deepcopy(self.manifest)
                self.manifest[field] = value
                self.save()
                with self.assertRaisesRegex(ValueError, message):
                    runner.validate_bundle(self.bundle)
                self.manifest = manifest


class IngressHandler(http.server.BaseHTTPRequestHandler):
    """A stand-in ingress: routes by Host header, answering Traefik's own 404 for an unknown host."""
    routes = {}
    seen = []

    def do_GET(self):
        self.seen.append((self.headers["Host"], self.path))
        status = self.routes.get(self.headers["Host"])
        if callable(status):
            status = status()
        body = b"404 page not found\n" if status is None else b"{}"
        self.send_response(404 if status is None else status)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *arguments):
        pass


class ProbeTests(unittest.TestCase):
    def setUp(self):
        IngressHandler.routes, IngressHandler.seen = {}, []
        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), IngressHandler)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.address = "http://127.0.0.1:" + str(self.server.server_address[1])
        self.target = {"product": "pilot", "environment": "test", "root": "/srv/pilot",
                       "ingress": {"scheme": "http", "port": 18080, "entryPoints": ["web"], "privateEntryPoints": [],
                                   "probe": self.address, "hosts": {}}}

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()

    def sites(self, *names):
        return [{"name": name, "service": "api", "exposure": "public",
                 "url": "http://" + name + "-pilot.test.localhost:18080/api"} for name in names]

    def test_a_site_answers_through_the_ingress_by_its_host_name(self):
        IngressHandler.routes = {"app-pilot.test.localhost": 200}
        probed = runner.probe_sites(self.target, self.sites("app"), deadline_seconds=1, interval=0.05)
        self.assertEqual({"ok": True, "status": 200}, probed[0]["probe"])
        self.assertEqual([("app-pilot.test.localhost", "/api")], IngressHandler.seen)

    def test_an_application_404_answers_but_the_ingress_404_does_not(self):
        IngressHandler.routes = {"app-pilot.test.localhost": 404}
        probed = runner.probe_sites(self.target, self.sites("app", "go"), deadline_seconds=0.2, interval=0.05)
        self.assertTrue(probed[0]["probe"]["ok"])
        self.assertEqual({"ok": False, "status": 404, "detail": "The ingress has no route for this host"},
                         probed[1]["probe"])

    def test_a_route_that_appears_within_the_deadline_passes(self):
        answers = iter([None, None, 200])
        IngressHandler.routes = {"app-pilot.test.localhost": lambda: next(answers, 200)}
        probed = runner.probe_sites(self.target, self.sites("app"), deadline_seconds=2, interval=0.05)
        self.assertTrue(probed[0]["probe"]["ok"])
        self.assertEqual(3, len(IngressHandler.seen))

    def test_gateway_errors_and_an_unreachable_ingress_fail(self):
        IngressHandler.routes = {"app-pilot.test.localhost": 502}
        self.assertEqual("The ingress could not reach the service",
                         runner.probe_sites(self.target, self.sites("app"), 0.1, 0.05)[0]["probe"]["detail"])
        self.target["ingress"]["probe"] = "http://127.0.0.1:9"
        self.assertIn("did not answer", runner.probe_sites(self.target, self.sites("app"), 0.1, 0.05)[0]["probe"]["detail"])

    def test_private_sites_without_a_private_probe_address_stay_unprobed(self):
        sites = self.sites("admin")
        sites[0]["exposure"] = "private"
        self.assertEqual([], runner.probe_sites(self.target, sites, 0.1, 0.05))
        self.assertNotIn("probe", sites[0])

    def test_the_target_check_reports_whether_the_ingress_answers(self):
        def ingress_row():
            result = runner.check(self.target, docker_factory=CheckDocker)
            return next(item for item in result["checks"] if item["name"] == "Ingress")

        row = ingress_row()
        self.assertEqual((True, "answers at " + self.address), (row["ok"], row["detail"]))
        self.assertEqual([("wheelhouse-check.invalid", "/")], IngressHandler.seen)
        self.target["ingress"]["probe"] = "http://127.0.0.1:9"
        row = ingress_row()
        self.assertFalse(row["ok"])
        self.assertIn("did not answer at http://127.0.0.1:9", row["detail"])

    def test_invalid_probe_addresses_are_refused(self):
        self.target["ingress"]["probe"] = "ftp://ingress"
        with self.assertRaisesRegex(ValueError, "probe address"):
            runner.validate_target(self.target)


if __name__ == "__main__":
    unittest.main()
