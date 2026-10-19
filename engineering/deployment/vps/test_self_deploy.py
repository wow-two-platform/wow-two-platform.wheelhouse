"""self-deploy.py's own decisions: which release, which gates, what it launches and logs. The runner is a stand-in here;
`rehearse.py self-deploy` runs the script with the release image's real runner on the local rig. Never contacts GitHub,
Docker or a host."""
import argparse
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest import mock

KIT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("self_deploy", KIT / "self-deploy.py")
self_deploy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(self_deploy)

IMAGE = self_deploy.IMAGE + "@sha256:" + "a" * 64


class FakeRunner:
    """The runner calls self-deploy makes, recorded; `condition`, `passes` and `checks` set the target's answers."""

    def __init__(self, condition="ready", current=None, passes=True, checks=True):
        self.condition, self.current, self.passes, self.checks = condition, current, passes, checks
        self.launched = []

    def validate_bundle(self, bundle):
        return json.loads((Path(bundle) / "release.json").read_text())

    def validate_target(self, target, manifest=None):
        return Path(target["root"]), "wheelhouse-" + target["environment"]

    def state(self, target):
        return {"condition": self.condition, "current": self.current}

    def passed(self, target, release):
        assert target["environment"] == "dev"
        return {"id": "dev-job", "release": release} if self.passes else None

    def check(self, target, manifest):
        return {"ok": self.checks, "checks": [{"name": "Docker", "ok": self.checks, "detail": "linux/amd64"}]}

    def launch(self, bundle, target_path, actor):
        self.launched.append((bundle, target_path, actor))
        return {"id": "11111111-1111-4111-8111-111111111111", "status": "queued"}

    def status(self, target, job):
        return {"id": job, "status": "succeeded", "completedAt": "2026-10-19T12:00:00+00:00", "release": "v0.5.21",
                "steps": [{"name": "Pull images", "status": "succeeded"}, {"name": "Probe sites", "status": "succeeded",
                                                                           "detail": "1 site answered"}]}


def archive(manifest, compose=b'{"services": {}}', extra=None):
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode="w:gz") as package:
        for name, content in (("release.json", json.dumps(manifest).encode()), ("compose.json", compose),
                              *((extra,) if extra else ())):
            info = tarfile.TarInfo(name)
            info.size = len(content)
            package.addfile(info, io.BytesIO(content))
    return buffer.getvalue()


class SelfDeployTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for environment in ("dev", "prod"):
            (self.root / "config").mkdir(exist_ok=True)
            (self.root / "config" / ("wheelhouse-" + environment + ".json")).write_text(json.dumps(
                {"product": "wheelhouse", "environment": environment, "root": str(self.root)}))
        self.manifest = {"product": "wheelhouse", "release": "v0.5.21", "kind": "release", "sourceCommit": "b" * 40,
                         "images": {"console": IMAGE}}
        self.output = io.StringIO()

    def arguments(self, action="deploy", **overrides):
        values = {"action": action, "env": "prod", "release": "v0.5.21", "reason": "console down", "skip_dev_pass": False,
                  "yes": False, "no_wait": False, "json": False, "root": self.root, "target": None,
                  "runner": "stand-in", "bundle": None}
        return argparse.Namespace(**{**values, **overrides})

    def fetcher(self, manifest=None, digest=None, payload=None):
        payload = payload or archive(manifest or self.manifest)
        release = {"tag_name": "v0.5.21", "draft": False, "prerelease": False,
                   "assets": [{"name": self_deploy.ASSET,
                               "digest": digest or "sha256:" + hashlib.sha256(payload).hexdigest()}]}
        calls = []

        def fetch(url, accept="application/vnd.github+json", limit=None):
            calls.append(url)
            return json.dumps(release).encode() if "/releases/tags/" in url else payload
        fetch.calls = calls
        return fetch

    def deploy(self, runner, typed="wheelhouse-prod", fetch=None, **overrides):
        with mock.patch.object(self_deploy, "load_runner", return_value=runner):
            return self_deploy.deploy(self.arguments(**overrides), self.output, ask=lambda prompt: typed,
                                      fetch=fetch or self.fetcher())

    def log(self):
        path = self.root / "self-deploy" / "log.jsonl"
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []

    def test_prod_deploys_a_verified_published_release_after_its_dev_pass_and_the_typed_id(self):
        runner = FakeRunner(current={"release": "v0.5.20", "sourceCommit": "c" * 40})
        fetch = self.fetcher()
        with mock.patch.dict(os.environ, {"SUDO_USER": "ops"}):
            self.assertEqual(0, self.deploy(runner, fetch=fetch))
        self.assertEqual(1, len(runner.launched))
        bundle, target, actor = runner.launched[0]
        self.assertEqual((str(Path(bundle).parent / "target.json"), "self-deploy:ops"), (target, actor))
        # The kit's target, gated as a console's inventory gates Wheelhouse's own.
        self.assertEqual({"product": "wheelhouse", "environment": "prod", "root": str(self.root), "probeGate": True,
                          "rollbackCompatible": True}, json.loads(Path(target).read_text()))
        self.assertEqual(0o600, Path(target).stat().st_mode & 0o777)
        self.assertEqual(self.manifest, json.loads((Path(bundle) / "release.json").read_text()))
        self.assertTrue(fetch.calls[0].endswith("/repos/wow-two-platform/wow-two-platform.wheelhouse/releases/tags/v0.5.21"))
        self.assertTrue(fetch.calls[1].endswith("/releases/download/v0.5.21/wheelhouse-release.tar.gz"))
        entry = self.log()[0]
        self.assertEqual(("deploy", "prod", "v0.5.21", "console down", "self-deploy:ops"),
                         (entry["action"], entry["environment"], entry["release"], entry["reason"], entry["actor"]))
        self.assertEqual(0o600, (self.root / "self-deploy/log.jsonl").stat().st_mode & 0o777)
        self.assertIn("succeeded Probe sites — 1 site answered", " ".join(self.output.getvalue().split()))

    def test_a_download_is_refused_unless_it_is_exactly_the_published_release(self):
        for fetch, refusal in (
                (self.fetcher(digest="sha256:" + "0" * 64), "The release asset changed"),
                (self.fetcher(manifest={**self.manifest, "release": "v0.5.20"}), "names another release"),
                (self.fetcher(manifest={**self.manifest, "images": {"console": "ghcr.io/evil/console@sha256:" + "a" * 64}}),
                 "outside"),
                (self.fetcher(manifest={**self.manifest, "product": "foreverpin"}), "not a Wheelhouse release"),
                (self.fetcher(payload=archive(self.manifest, extra=("run.sh", b"rm -rf /"))), "only release.json")):
            with self.subTest(refusal=refusal):
                runner = FakeRunner()
                with self.assertRaisesRegex(self_deploy.Refused, refusal):
                    self.deploy(runner, fetch=fetch)
                self.assertEqual([], runner.launched)

    def test_prod_needs_a_reason_its_dev_pass_and_the_typed_id(self):
        for overrides, runner, typed, refusal in (
                ({"reason": ""}, FakeRunner(), "wheelhouse-prod", "Give a --reason to deploy prod"),
                ({}, FakeRunner(passes=False), "wheelhouse-prod", "never succeeded on wheelhouse-dev"),
                ({}, FakeRunner(), "wheelhouse-dev", "Not confirmed"),
                ({}, FakeRunner(checks=False), "wheelhouse-prod", "The target check failed"),
                ({}, FakeRunner(condition="running"), "wheelhouse-prod", "A rollout is running"),
                ({}, FakeRunner(condition="needs_reconciliation"), "wheelhouse-prod", "needs reconciliation"),
                ({"env": "dev", "skip_dev_pass": True}, FakeRunner(), "", "Only prod has a dev pass")):
            with self.subTest(refusal=refusal):
                with self.assertRaisesRegex(self_deploy.Refused, refusal):
                    self.deploy(runner, typed=typed, **overrides)
                self.assertEqual([], runner.launched)
        runner = FakeRunner(passes=False)
        self.assertEqual(0, self.deploy(runner, skip_dev_pass=True))
        self.assertTrue(self.log()[-1]["skipDevPass"])

    def test_dev_deploys_without_a_typed_id_and_a_running_release_is_left_alone(self):
        runner = FakeRunner()
        self.assertEqual(0, self.deploy(runner, typed="", env="dev", reason=None))
        self.assertEqual(1, len(runner.launched))
        unchanged = FakeRunner(current={"release": "v0.5.21", "sourceCommit": "b" * 40})
        self.assertEqual(0, self.deploy(unchanged))
        self.assertEqual([], unchanged.launched)
        self.assertIn("already runs v0.5.21; nothing changed", self.output.getvalue())

    def test_a_bundle_folder_deploys_without_a_download(self):
        folder = self.root / "given"
        folder.mkdir()
        local = {**self.manifest, "release": "sha-1234567", "images": {"console": "localhost:15000/wheelhouse/console@x"}}
        (folder / "release.json").write_text(json.dumps(local))
        (folder / "compose.json").write_text("{}")
        runner = FakeRunner()
        fetch = self.fetcher()
        self.assertEqual(0, self.deploy(runner, fetch=fetch, env="dev", bundle=str(folder), release=None, typed=""))
        self.assertEqual([], fetch.calls)
        self.assertEqual("sha-1234567", self.log()[0]["release"])

    def journal(self, environment, jobs, current):
        project = self.root / ("wheelhouse-" + environment)
        for job_id, release, status, completed, kept in jobs:
            (project / "jobs").mkdir(parents=True, exist_ok=True)
            (project / "jobs" / (job_id + ".json")).write_text(json.dumps(
                {"id": job_id, "release": release, "status": status, "completedAt": completed,
                 "actor": "github:wheelhouse:1"}))
            if kept:
                folder = project / "releases" / job_id
                folder.mkdir(parents=True)
                (folder / "release.json").write_text(json.dumps({**self.manifest, "release": release}))
                (folder / "compose.json").write_text("{}")
        (project / "current.json").write_text(json.dumps(current))
        return project

    def rollback(self, runner, typed="wheelhouse-prod", **overrides):
        with mock.patch.object(self_deploy, "load_runner", return_value=runner):
            return self_deploy.rollback(self.arguments("rollback", **{"release": None, **overrides}), self.output,
                                        ask=lambda prompt: typed)

    def test_rollback_returns_to_the_newest_earlier_release_this_host_keeps_with_no_network(self):
        project = self.journal("prod", (("j1", "v0.5.18", "succeeded", "2026-10-10", True),
                                        ("j2", "v0.5.19", "succeeded", "2026-10-11", False),
                                        ("j3", "v0.5.20", "rolled_back", "2026-10-12", True),
                                        ("j4", "v0.5.20", "succeeded", "2026-10-13", True),
                                        ("j5", "v0.5.21", "succeeded", "2026-10-14", True)),
                                {"id": "j5", "release": "v0.5.21"})
        runner = FakeRunner()
        self.assertEqual(0, self.rollback(runner))
        self.assertEqual(str(project / "releases" / "j4"), runner.launched[0][0])
        entry = self.log()[0]
        self.assertEqual(("rollback", "v0.5.20", "v0.5.21", "j4"),
                         (entry["action"], entry["release"], entry["from"], entry["source"]))
        # A named release: the newest success of it whose bundle is kept (v0.5.19's folder is gone).
        runner = FakeRunner()
        self.assertEqual(0, self.rollback(runner, release="v0.5.18"))
        self.assertEqual(str(project / "releases" / "j1"), runner.launched[0][0])
        for release, refusal in (("v0.5.19", "No earlier release v0.5.19 is kept"),
                                 ("v0.5.21", "already runs v0.5.21")):
            with self.subTest(release=release), self.assertRaisesRegex(self_deploy.Refused, refusal):
                self.rollback(FakeRunner(), release=release)
        with self.assertRaisesRegex(self_deploy.Refused, "Give a --reason to roll prod back"):
            self.rollback(FakeRunner(), reason="")

    def test_rollback_needs_a_recorded_release(self):
        with self.assertRaisesRegex(self_deploy.Refused, "runs no recorded release"):
            self.rollback(FakeRunner(), env="dev")

    def test_status_reads_each_console_from_its_files_alone(self):
        self.journal("prod", (("j5", "v0.5.21", "succeeded", "2026-10-14", True),), {"id": "j5", "release": "v0.5.21"})
        (self.root / "wheelhouse-prod" / "active.json").write_text(json.dumps({"id": "j6", "status": "running"}))
        output = io.StringIO()
        self.assertEqual(0, self_deploy.status(self.arguments("status", env=None, json=True), output))
        consoles = {item["environment"]: item for item in json.loads(output.getvalue())["consoles"]}
        # A running record whose lock nobody holds is a crashed rollout: it needs reconciliation.
        self.assertEqual(("needs reconciliation", "v0.5.21", "j5"),
                         (consoles["prod"]["condition"], consoles["prod"]["current"]["release"],
                          consoles["prod"]["recent"][0]["id"]))
        self.assertEqual((True, None), (consoles["dev"]["present"], consoles["dev"]["current"]))

    def test_the_runner_comes_from_the_release_image_pulled_when_missing(self):
        calls = []

        def run(*arguments, timeout=600):
            calls.append(arguments[0])
            if arguments[0] == "image":
                raise self_deploy.Refused("docker image failed (exit 1)")
            if arguments[0] == "create":
                return "container-id\n"
            if arguments[0] == "cp":
                Path(arguments[2]).write_text("# runner")
            return ""
        destination = self.root / "runner.py"
        self.assertEqual(destination, self_deploy.runner_from_image(IMAGE, destination, run))
        self.assertEqual(["image", "pull", "create", "cp", "rm"], calls)

    def test_it_never_runs_as_root(self):
        with mock.patch.object(self_deploy.os, "geteuid", return_value=0), \
                mock.patch("sys.stderr", new_callable=io.StringIO) as error:
            self.assertEqual(2, self_deploy.main(["status"]))
        self.assertIn("Run as the deploy account", error.getvalue())


if __name__ == "__main__":
    unittest.main()
