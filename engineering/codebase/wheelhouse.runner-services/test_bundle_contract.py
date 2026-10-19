"""The bundle contract with the shared release generator: every bundle it writes, the runner accepts.

The generator lives in wow-two-platform.pipelines. CI checks it out at the tag the publish workflow pins and names it in
WHEELHOUSE_PIPELINES; locally the sibling checkout in the workbench serves. Without either, the tests skip.
"""
import copy
import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import runner

HERE = Path(__file__).resolve().parent
PIPELINES = Path(os.environ.get("WHEELHOUSE_PIPELINES") or HERE.parents[3] / "wow-two-platform.pipelines")
GENERATOR = PIPELINES / "generator" / "release.py"
release = None
if GENERATOR.is_file():
    spec = importlib.util.spec_from_file_location("release", GENERATOR)
    release = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(release)

try:
    import yaml  # noqa: F401
    HAS_YAML = True
except ImportError:
    HAS_YAML = False

DIGEST = "@sha256:" + "a" * 64
DESCRIPTOR = {
    "descriptor": 1, "product": "pilot",
    "services": {
        "api": {"build": {"dockerfile": "deployment/backend.Dockerfile"},
                "paths": ["codebase/pilot.backend-services/**"], "health": "/health",
                "settings": {"required": ["Database:Connection"]}, "volumes": {"keys": "/data/keys"},
                "sites": {"app": {"path": "/api"}}, "needs": ["postgres"]},
        "edge": {"build": {"dockerfile": "deployment/edge.Dockerfile"},
                 "paths": ["codebase/pilot.frontend-services/**"], "port": 80,
                 "health": {"command": ["wget", "-q", "-O-", "http://localhost/healthz"]},
                 "sites": {"app": None}},
    },
}


def git(repo, *arguments):
    return subprocess.run(["git", "-C", str(repo), *arguments], check=True, capture_output=True, text=True).stdout.strip()


@unittest.skipIf(release is None, "wow-two-platform.pipelines is not checked out beside Wheelhouse")
class BundleContractTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.repo = Path(self.temporary.name)
        git(self.repo, "init", "-q", "-b", "main")
        git(self.repo, "config", "user.email", "test@example.invalid")
        git(self.repo, "config", "user.name", "Test")
        git(self.repo, "config", "commit.gpgsign", "false")
        for path in ("engineering/codebase/pilot.backend-services/Program.cs",
                     "engineering/planning/version-track/v0.4/v0.4.md"):
            (self.repo / path).parent.mkdir(parents=True, exist_ok=True)
            (self.repo / path).write_text("one")
        git(self.repo, "add", "-A")
        git(self.repo, "commit", "-q", "-m", "first")
        self.commit = git(self.repo, "rev-parse", "HEAD")
        self.descriptor = release.validate(copy.deepcopy(DESCRIPTOR))

    def tearDown(self):
        self.temporary.cleanup()

    def bundle(self, branch):
        identity = release.build_identity(self.repo, self.commit, branch)
        tag = identity["release"] if identity["kind"] == "release" else None
        planned = release.plan(self.descriptor, self.repo, self.commit, None, tag, identity["version"])
        images = {name: "ghcr.io/o/pilot/" + name + DIGEST for name in self.descriptor["services"]}
        compose_bytes, manifest = release.render(self.descriptor, planned, images, branch, identity=identity)
        output = self.repo / ("bundle-" + (branch or "dispatch"))
        release.write_bundle(output, compose_bytes, manifest)
        return runner.validate_bundle(output)

    def test_a_release_bundle_passes_the_runner(self):
        validated = self.bundle("main")
        self.assertEqual(("v0.4.0", "release", "0.4.0"), (validated["release"], validated["kind"], validated["version"]))
        self.assertEqual({"api": {"version": "0.4.0", "changedIn": "v0.4.0"},
                          "edge": {"version": "0.4.0", "changedIn": "v0.4.0"}}, validated["versions"])
        self.assertEqual({"api": ["Database:Connection"]}, validated["requiredConfiguration"])
        self.assertEqual(["api", "edge"], [site["service"] for site in runner.release_sites(validated)])

    def test_candidate_bundles_pass_the_runner(self):
        # Products work on `main` only; a build of any other branch is a candidate that only dev takes, whatever
        # channel the pinned generator names it (v0.1.0 still says `dev` for the dev branch).
        for branch in ("dev", "feat/login", None):
            with self.subTest(branch=branch):
                validated = self.bundle(branch)
                self.assertEqual(("candidate", "sha-" + self.commit[:7]), (validated["kind"], validated["release"]))
                self.assertNotEqual("release", validated["channel"])

    @unittest.skipUnless(release is not None and hasattr(release, "verify_of"),
                         "the pinned generator predates dotnetEnvironment and verify")
    def test_the_environment_mapping_and_the_dev_check_pass_the_runner(self):
        declared = copy.deepcopy(DESCRIPTOR)
        declared.update(dotnetEnvironment=True,
                        verify={"service": "api", "command": ["sh", "-c", "curl --fail $VERIFY_SITE_APP"], "timeout": "2m"})
        self.descriptor = release.validate(declared)
        validated = self.bundle("main")
        self.assertTrue(validated["dotnetEnvironment"])
        self.assertEqual({"service": "api", "command": ["sh", "-c", "curl --fail $VERIFY_SITE_APP"], "timeoutSeconds": 120},
                         runner.release_verification(validated))

    @unittest.skipUnless(release is not None and hasattr(release, "dependency_order"),
                         "the pinned generator predates dependsOn")
    def test_declared_dependencies_order_the_runner_waves(self):
        declared = copy.deepcopy(DESCRIPTOR)
        declared["services"]["edge"]["dependsOn"] = ["api"]
        self.descriptor = release.validate(declared)
        self.bundle("main")
        compose = runner.read_json(self.repo / "bundle-main" / "compose.json")
        dependencies = runner.service_dependencies(compose)
        self.assertEqual({"api": set(), "edge": {"api"}}, dependencies)
        self.assertEqual([["api"], ["edge"]], runner.dependency_waves(compose["services"], dependencies))
        # Health-gated and never `restart: true`, so an api update leaves a running edge alone.
        self.assertEqual({"api": {"condition": "service_healthy"}}, compose["services"]["edge"]["depends_on"])

    @unittest.skipUnless(release is not None and hasattr(release, "sites_of"), "the pinned generator predates descriptor 2")
    def test_a_descriptor_2_bundle_routes_each_service_path(self):
        declared = copy.deepcopy(DESCRIPTOR)
        declared.update(descriptor=2, sites={"app": {"primary": True}, "admin": {"exposure": "private"}})
        declared["services"]["api"]["sites"] = {"app": {"paths": ["/api", "/hubs"]}, "admin": {"paths": ["/"]}}
        declared["services"]["edge"]["sites"] = {"app": None}
        self.descriptor = release.validate(declared)
        validated = self.bundle("main")
        self.assertEqual(2, validated["schemaVersion"])
        self.assertEqual([("api", "admin", ["/"], "private"), ("api", "app", ["/api", "/hubs"], "public"),
                          ("edge", "app", ["/"], "public")],
                         [(row["service"], row["site"], row["paths"], row["exposure"]) for row in runner.release_sites(validated)])
        target = {"product": "pilot", "environment": "dev",
                  "ingress": {"scheme": "https", "entryPoints": ["websecure"], "privateEntryPoints": ["private"],
                              "pattern": "{site}-{product}.{environment}.example"}}
        config, published = runner.ingress_routes(target, "pilot-dev", validated)
        self.assertEqual({"pilot-dev-admin-api-root": 101, "pilot-dev-app-api-api": 104, "pilot-dev-app-api-hubs": 105,
                          "pilot-dev-app-edge-root": 101},
                         {name: router["priority"] for name, router in config["http"]["routers"].items()})
        self.assertEqual(["https://admin-pilot.dev.example", "https://app-pilot.dev.example",
                          "https://app-pilot.dev.example/api"], [site["url"] for site in published])

    @unittest.skipUnless(HAS_YAML, "PyYAML is not installed for this interpreter")
    def test_wheelhouse_own_descriptor_parses(self):
        path = HERE.parents[1] / "deployment" / "deploy.yml"
        descriptor = release.validate(release.parse(path.read_text()))
        self.assertEqual(["console"], list(descriptor["services"]))
        self.assertEqual("private", descriptor["services"]["console"]["sites"]["console"]["exposure"])
        # Deployed dev runs the console as Development: its appsettings.Development.json holds defaults only.
        self.assertTrue(descriptor["dotnetEnvironment"])


if __name__ == "__main__":
    unittest.main()
