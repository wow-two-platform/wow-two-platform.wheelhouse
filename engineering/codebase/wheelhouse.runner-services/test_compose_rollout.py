"""Service rollouts on a real Docker Compose: a service restarts only when its own image, definition or settings change,
and never because a service it depends on was replaced. Opt-in and local: set WHEELHOUSE_COMPOSE_REHEARSAL=1 with
Docker running and the busybox and alpine images present (nothing is pulled). Each step compares container IDs and
start times, so a restart without a recreate counts too.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import runner

PRODUCT = "rolloutcheck"
PROJECT = PRODUCT + "-dev"
ENABLED = os.environ.get("WHEELHOUSE_COMPOSE_REHEARSAL") == "1" and shutil.which("docker") is not None


def docker(*arguments):
    return subprocess.run(["docker", *arguments], capture_output=True, text=True, timeout=120)


def local_digest(image):
    result = docker("image", "inspect", "--format", "{{index .RepoDigests 0}}", image)
    return result.stdout.strip() if result.returncode == 0 else None


class LocalDocker(runner.Docker):
    """The runner's Docker adapter without registry traffic: the images are local, and no image is ever removed."""

    def pull(self, bundle):
        self.compose(bundle, "config", "--quiet")

    def execute(self, arguments, step, timeout=600):
        if arguments[:3] == ["docker", "image", "rm"]:
            raise runner.CommandFailed("docker image rm is skipped in the rehearsal")
        return super().execute(arguments, step, timeout)


@unittest.skipUnless(ENABLED, "set WHEELHOUSE_COMPOSE_REHEARSAL=1 to run against the local Docker")
class ComposeRolloutRehearsal(unittest.TestCase):
    def setUp(self):
        self.busybox, self.alpine = local_digest("busybox:latest"), local_digest("alpine:3")
        if not (self.busybox and self.alpine):
            self.skipTest("busybox:latest and alpine:3 must be present locally")
        info = docker("info", "--format", "{{.OSType}}/{{.Architecture}}").stdout.strip()
        self.platform = info.replace("x86_64", "amd64").replace("aarch64", "arm64")
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.settings = self.root / "c-settings.json"
        self.write_settings("one")
        self.target = {"product": PRODUCT, "environment": "dev", "root": str(self.root / "state"),
                       "settings": {"c": str(self.settings)}, "minimumFreeBytes": 0, "healthTimeoutSeconds": 60}
        self.number = 0

    def tearDown(self):
        docker("compose", "--project-name", PROJECT, "down", "--timeout", "1")
        self.temporary.cleanup()

    def write_settings(self, label):
        self.settings.write_text(json.dumps({"Label": label}))
        self.settings.chmod(0o600)

    def service(self, image, **extra):
        return {"image": image, "platform": self.platform, "init": True, "command": ["sleep", "86400"],
                "stop_grace_period": "1s",
                "healthcheck": {"test": ["CMD", "true"], "interval": "1s", "timeout": "2s", "retries": 3}, **extra}

    def release(self, c_image, b_extra=None):
        """c takes a settings file; b depends on c once it is healthy, as a rendered `dependsOn` does."""
        self.number += 1
        bundle = self.root / ("bundle-" + str(self.number))
        bundle.mkdir()
        compose = {"services": {
            "c": self.service(c_image, volumes=[{"type": "bind", "source": "${C_SETTINGS:?Set C_SETTINGS}",
                                                 "target": "/settings.json", "read_only": True}]),
            "b": self.service(self.busybox, depends_on={"c": {"condition": "service_healthy"}}, **(b_extra or {}))}}
        compose_bytes = json.dumps(compose).encode()
        (bundle / "compose.json").write_bytes(compose_bytes)
        manifest = {"schemaVersion": 1, "product": PRODUCT, "release": "v" + str(self.number),
                    "sourceCommit": "a" * 40, "platform": self.platform, "rollbackCompatible": False,
                    "composeSha256": hashlib.sha256(compose_bytes).hexdigest(),
                    "images": {"c": c_image, "b": self.busybox}, "requiredConfiguration": {"c": []}}
        (bundle / "release.json").write_text(json.dumps(manifest))
        return runner.apply(bundle, self.target, "rehearsal", docker_factory=LocalDocker)

    def containers(self):
        """Each service's container ID and start time."""
        result = {}
        for service in ("b", "c"):
            container = docker("compose", "--project-name", PROJECT, "ps", "--all", "--quiet", service).stdout.strip()
            details = json.loads(docker("inspect", container).stdout)[0]
            result[service] = (details["Id"], details["State"]["StartedAt"])
        return result

    def rollout(self, result):
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        return {service["name"]: (service["change"], service["reason"])
                for service in result["serviceRollout"]["services"]}

    def test_a_service_restarts_only_when_its_own_image_definition_or_settings_change(self):
        self.rollout(self.release(self.busybox))
        first = self.containers()

        # c's image changes: c is recreated, b (which depends on it) keeps its container and its start time.
        self.assertEqual({"c": ("updated", "image"), "b": ("unchanged", None)}, self.rollout(self.release(self.alpine)))
        second = self.containers()
        self.assertNotEqual(first["c"][0], second["c"][0])
        self.assertEqual(first["b"], second["b"])

        # Only c's settings file changes: Compose sees the same definition, the runner still recreates c alone.
        self.write_settings("two")
        self.assertEqual({"c": ("updated", "settings"), "b": ("unchanged", None)},
                         self.rollout(self.release(self.alpine)))
        third = self.containers()
        self.assertNotEqual(second["c"][0], third["c"][0])
        self.assertEqual(first["b"], third["b"])

        # b's own definition changes: b is recreated, c (its dependency) is left alone.
        self.assertEqual({"c": ("unchanged", None), "b": ("updated", "definition")},
                         self.rollout(self.release(self.alpine, {"mem_limit": "64m"})))
        fourth = self.containers()
        self.assertEqual(third["c"], fourth["c"])
        self.assertNotEqual(third["b"][0], fourth["b"][0])

        # Nothing changes: nothing restarts.
        self.assertEqual({"c": ("unchanged", None), "b": ("unchanged", None)},
                         self.rollout(self.release(self.alpine, {"mem_limit": "64m"})))
        self.assertEqual(fourth, self.containers())


if __name__ == "__main__":
    unittest.main()
