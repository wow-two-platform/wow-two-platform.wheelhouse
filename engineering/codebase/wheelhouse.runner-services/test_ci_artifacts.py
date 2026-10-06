import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import Mock, patch

import artifacts
import ci_artifacts as ci
from runner import CommandFailed, Rejected

HERE = Path(__file__).resolve().parent
PIPELINES = Path(os.environ.get("WHEELHOUSE_PIPELINES") or HERE.parents[3] / "wow-two-platform.pipelines")
GENERATOR = PIPELINES / "generator" / "release.py"


class CiArtifactTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.destination = self.root / "resolved"
        self.generator = self.root / "release.py"
        self.generator.touch()
        self.commit = "b" * 40
        self.image = ci.IMAGE + "@sha256:" + "a" * 64
        self.compose = {"services": {"console": {"image": self.image, "platform": "linux/amd64",
                                               "healthcheck": {"test": ["CMD", "true"]}}}}
        self.manifest = {"schemaVersion": 1, "product": "wheelhouse", "release": "v0.3.5", "kind": "release",
                         "sourceCommit": self.commit, "platform": "linux/amd64", "rollbackCompatible": False,
                         "images": {"console": self.image}, "requiredConfiguration": {"console": []}}
        self.release = {"tag_name": "v0.3.5", "draft": False, "prerelease": False,
                        "published_at": "2026-10-06T00:00:00Z",
                        "assets": [{"name": ci.ASSET, "id": 123, "state": "uploaded"}]}
        self.tag_commit = self.commit
        self.pack()

    def pack(self, extra=None):
        compose = json.dumps(self.compose).encode()
        self.manifest["composeSha256"] = hashlib.sha256(compose).hexdigest()
        self.manifest_bytes = json.dumps(self.manifest).encode()
        data = io.BytesIO()
        with tarfile.open(fileobj=data, mode="w:gz") as package:
            entries = [("release.json", self.manifest_bytes), ("compose.json", compose)]
            if extra:
                entries.append(extra)
            for name, payload in entries:
                member = tarfile.TarInfo(name)
                member.size = len(payload)
                package.addfile(member, io.BytesIO(payload))
        self.payload = data.getvalue()
        self.release["assets"][0].update(size=len(self.payload),
                                         digest="sha256:" + hashlib.sha256(self.payload).hexdigest())

    def fetch(self, url):
        if "/releases?" in url:
            return json.dumps([self.release]).encode()
        if "/commits/" in url:
            return json.dumps({"sha": self.tag_commit}).encode()
        return self.payload

    def resolve(self, commit=None):
        with patch.object(artifacts, "fetch", side_effect=self.fetch):
            return ci.resolve_bundle(ci.REPOSITORY, commit or self.commit, self.destination,
                                     self.root, self.generator)

    def test_exact_source_preserves_original_bundle(self):
        with patch.object(ci, "equivalent_bundle") as equivalent:
            result = self.resolve()
        self.assertEqual(self.commit, result["sourceCommit"])
        self.assertEqual(self.manifest_bytes, (self.destination / "release.json").read_bytes())
        equivalent.assert_not_called()

    def test_docs_only_push_reuses_ancestor_without_relabelling_source(self):
        head = "c" * 40
        with patch.object(ci, "is_ancestor", return_value=True), \
                patch.object(ci, "equivalent_bundle", return_value=True) as equivalent:
            result = self.resolve(head)
        self.assertEqual(self.commit, result["sourceCommit"])
        self.assertEqual(head, equivalent.call_args.args[2])
        self.assertEqual(self.manifest_bytes, (self.destination / "release.json").read_bytes())

    def test_changed_inputs_require_a_new_publication(self):
        with patch.object(ci, "is_ancestor", return_value=True), \
                patch.object(ci, "equivalent_bundle", return_value=False):
            with self.assertRaisesRegex(CommandFailed, "No published release"):
                self.resolve("c" * 40)
        self.assertFalse(self.destination.exists())

    def test_nonancestor_is_not_used_even_when_code_looks_equivalent(self):
        with patch.object(ci, "is_ancestor", return_value=False), \
                patch.object(ci, "equivalent_bundle") as equivalent:
            with self.assertRaises(CommandFailed):
                self.resolve("c" * 40)
        equivalent.assert_not_called()

    def test_exact_source_precedes_newer_equivalent_release(self):
        newer = {"release": "v0.3.6", "sourceCommit": "a" * 40}
        exact = {"release": "v0.3.5", "sourceCommit": self.commit}
        ancestor, equivalent = Mock(return_value=True), Mock(return_value=True)
        self.assertIs(exact, ci.select_release([newer, exact], self.commit, ancestor, equivalent))
        ancestor.assert_not_called()
        equivalent.assert_not_called()

    def test_newest_usable_fallback_is_selected(self):
        candidates = [{"release": "v0.3.6", "sourceCommit": "a" * 40},
                      {"release": "v0.3.5", "sourceCommit": "c" * 40}]
        chosen = ci.select_release(candidates, self.commit, lambda *_: True,
                                   lambda candidate: candidate["release"] == "v0.3.5")
        self.assertEqual("v0.3.5", chosen["release"])

    def test_changed_asset_is_rejected_before_extraction(self):
        self.release["assets"][0]["digest"] = "sha256:" + "0" * 64
        with self.assertRaisesRegex(Rejected, "Release asset changed"):
            self.resolve()
        self.assertFalse(self.destination.exists())

    def test_tag_must_match_manifest_source(self):
        self.manifest["sourceCommit"] = "a" * 40
        self.pack()
        with self.assertRaisesRegex(Rejected, "source commit mismatch"):
            self.resolve()

    def test_archive_path_traversal_is_rejected(self):
        self.pack(("../escape", b"forbidden"))
        with self.assertRaisesRegex(Rejected, "Archive must contain only"):
            self.resolve()
        self.assertFalse(self.destination.exists())
        self.assertFalse((self.root / "escape").exists())

    def test_unapproved_image_is_rejected(self):
        self.manifest["images"]["console"] = "ghcr.io/other/repo/console@sha256:" + "a" * 64
        self.compose["services"]["console"]["image"] = self.manifest["images"]["console"]
        self.pack()
        with self.assertRaisesRegex(Rejected, "approved repository"):
            self.resolve()

    def test_unapproved_repository_never_calls_network(self):
        with patch.object(artifacts, "fetch") as fetch:
            with self.assertRaisesRegex(Rejected, "Unapproved deployment repository"):
                ci.resolve_bundle("other/repo", self.commit, self.destination, self.root, self.generator)
        fetch.assert_not_called()

    def test_architecture_and_candidate_releases_are_rejected(self):
        for update in ({"kind": "candidate"}, {"platform": "linux/arm64"}):
            with self.subTest(update=update):
                saved = dict(self.manifest)
                self.manifest.update(update)
                self.compose["services"]["console"]["platform"] = self.manifest["platform"]
                self.pack()
                with self.assertRaisesRegex(Rejected, "Unexpected deployment release"):
                    self.resolve()
                self.manifest = saved

    def test_missing_digests_are_rejected(self):
        self.release["assets"][0].pop("digest")
        with self.assertRaisesRegex(Rejected, "Invalid release asset metadata"):
            self.resolve()

    def test_invalid_archive_reports_static_failure(self):
        self.payload = b"PRIVATE malformed archive"
        self.release["assets"][0]["digest"] = "sha256:" + hashlib.sha256(self.payload).hexdigest()
        with self.assertRaisesRegex(CommandFailed, "^Release bundle resolution failed$"):
            self.resolve()

    def test_release_scan_is_bounded(self):
        with patch.object(artifacts, "fetch", return_value=json.dumps([self.release] * 101).encode()):
            with self.assertRaisesRegex(Rejected, "Invalid release listing"):
                ci.resolve_bundle(ci.REPOSITORY, self.commit, self.destination, self.root, self.generator)

    def test_release_versions_sort_numerically(self):
        newer = {**self.release, "tag_name": "v0.3.10"}
        with patch.object(artifacts, "fetch", side_effect=[json.dumps([self.release, newer]).encode(),
                                                          json.dumps({"sha": self.commit}).encode(),
                                                          json.dumps({"sha": self.commit}).encode()]):
            candidates = ci.release_candidates(ci.REPOSITORY)
        self.assertEqual(["v0.3.10", "v0.3.5"], [candidate["release"] for candidate in candidates])

    def test_drafts_and_prereleases_are_not_candidates(self):
        for key in ("draft", "prerelease"):
            with self.subTest(key=key):
                self.release[key] = True
                with self.assertRaises(CommandFailed):
                    self.resolve()
                self.release[key] = False

    def test_planner_requires_real_unchanged_service_contract(self):
        plan = {"product": "wheelhouse", "sourceCommit": self.commit, "base": self.manifest["release"],
                "descriptorChanged": False, "services": {"console": {"build": False, "image": self.image}}}
        completed = lambda value: subprocess.CompletedProcess([], 0, json.dumps(value), "")
        with patch.object(ci.subprocess, "run", return_value=completed(plan)) as run:
            self.assertTrue(ci.equivalent_bundle(self.root, self.generator, self.commit, self.root, self.manifest))
        self.assertIn("--base", run.call_args.args[0])
        for change in ({"descriptorChanged": True}, {"services": {"console": {"build": True}}}):
            with self.subTest(change=change), \
                    patch.object(ci.subprocess, "run", return_value=completed({**plan, **change})):
                self.assertFalse(ci.equivalent_bundle(self.root, self.generator, self.commit, self.root, self.manifest))
        for change in ({"services": {}}, {"descriptorChanged": None}, {"sourceCommit": "c" * 40},
                       {"services": {"console": {"build": False, "image": "wrong"}}}):
            with self.subTest(change=change), \
                    patch.object(ci.subprocess, "run", return_value=completed({**plan, **change})):
                with self.assertRaises(Rejected):
                    ci.equivalent_bundle(self.root, self.generator, self.commit, self.root, self.manifest)

    def test_planner_failure_does_not_relay_private_output(self):
        result = subprocess.CompletedProcess([], 1, "PRIVATE", "SECRET")
        with patch.object(ci.subprocess, "run", return_value=result):
            with self.assertRaisesRegex(Rejected, "^Release equivalence planning failed$"):
                ci.equivalent_bundle(self.root, self.generator, self.commit, self.root, self.manifest)

    @unittest.skipUnless(GENERATOR.is_file() and importlib.util.find_spec("yaml"),
                         "Shared pipelines checkout and PyYAML are required")
    def test_real_shared_generator_distinguishes_docs_code_and_descriptor_changes(self):
        # CI checks out the workflow's pinned pipelines tag; locally use the same sibling as bundle contracts.
        repo = self.root / "repository"
        repo.mkdir()

        def git(*arguments):
            return subprocess.run(["git", "-C", str(repo), "-c", "user.name=Test",
                                   "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false",
                                   "-c", "core.hooksPath=/dev/null", *arguments],
                                  check=True, capture_output=True, text=True).stdout.strip()

        def commit(message):
            git("add", "-A")
            git("commit", "-q", "-m", message)
            return git("rev-parse", "HEAD")

        git("init", "-q", "-b", "main")
        descriptor = {"descriptor": 1, "product": "wheelhouse", "platform": "linux/amd64",
                      "services": {"console": {"build": {"dockerfile": "deployment/Dockerfile"},
                                                "paths": ["codebase/wheelhouse.backend-services/**"],
                                                "health": "/api/system/ready"}}}
        descriptor_path = repo / "engineering/deployment/deploy.yml"
        descriptor_path.parent.mkdir(parents=True)
        descriptor_path.write_text(json.dumps(descriptor))
        (descriptor_path.parent / "Dockerfile").write_text("FROM scratch\n")
        source = repo / "engineering/codebase/wheelhouse.backend-services/Program.cs"
        source.parent.mkdir(parents=True)
        source.write_text("// base\n")
        base_commit = commit("base")
        base = self.root / "base-bundle"
        base.mkdir()
        manifest = {**self.manifest, "sourceCommit": base_commit}
        (base / "release.json").write_text(json.dumps(manifest))

        (repo / "README.md").write_text("Documentation changed.\n")
        docs_commit = commit("docs")
        self.assertTrue(ci.equivalent_bundle(repo, GENERATOR, docs_commit, base, manifest))

        source.write_text("// changed application code\n")
        code_commit = commit("code")
        self.assertFalse(ci.equivalent_bundle(repo, GENERATOR, code_commit, base, manifest))

        # Return the code to the published tree, leaving only a descriptor change from the base.
        source.write_text("// base\n")
        descriptor["services"]["console"]["memory"] = "768m"
        descriptor_path.write_text(json.dumps(descriptor))
        descriptor_commit = commit("descriptor")
        self.assertFalse(ci.equivalent_bundle(repo, GENERATOR, descriptor_commit, base, manifest))


if __name__ == "__main__":
    unittest.main()
