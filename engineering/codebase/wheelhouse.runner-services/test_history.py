"""The history projection the API filters: bundle facts, live steps and one submission's local observation."""
import json
from pathlib import Path
import tempfile
import unittest
import history
import runner
import transport


class HistoryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)

    def tearDown(self):
        self.temporary.cleanup()

    def bundle(self, identifier, images, versions=None, kind=None, published=None, compatible=True):
        bundle = self.root / "bundles" / identifier
        manifest = {"schemaVersion": 1, "product": "pilot", "release": identifier, "images": images,
                    "rollbackCompatible": compatible}
        if versions:
            manifest["versions"] = versions
        if kind:
            manifest["kind"] = kind
        runner.write_json(bundle / "release.json", manifest)
        if published:
            runner.write_json(bundle / "source.json", {"publishedAt": published})

    def job(self, number, status, bundle="pilot-v1", target="pilot-dev"):
        identifier = "00000000-0000-4000-8000-" + str(number).zfill(12)
        runner.write_json(self.root / "jobs" / (identifier + ".json"),
                          {"id": identifier, "targetId": target, "bundleId": bundle, "release": bundle,
                           "actor": "max", "status": status, "submittedAt": f"2026-10-0{number}T10:00:00+00:00",
                           "ssh": {"host": "vps.example.net"}, "remote": "/srv/wheelhouse/incoming/x",
                           "remoteJobId": identifier})
        return identifier

    def test_records_carry_bundle_services_versions_and_publication(self):
        image = "ghcr.io/o/api@sha256:" + "a" * 64
        self.bundle("pilot-v1", {"api": image, "web": "ghcr.io/o/web@sha256:" + "b" * 64},
                    versions={"api": {"version": "1.2.0", "changedIn": "v1.2.0"}},
                    published="2026-10-01T09:00:00Z")
        self.job(1, "succeeded")
        [record] = history.records(self.root, transport.jobs(self.root, limit=None))
        self.assertEqual("pilot", record["product"])
        self.assertEqual("release", record["kind"])
        self.assertTrue(record["rollbackCompatible"])
        self.assertIsNone(record["migrations"], "a bundle that does not say carries no claim either way")
        self.assertEqual({"image": image, "version": "1.2.0"}, record["services"]["api"])
        self.assertIsNone(record["services"]["web"]["version"])
        self.assertEqual("2026-10-01T09:00:00Z", record["publishedAt"])
        self.assertNotIn("currentStep", record)
        self.assertNotIn("/srv/wheelhouse", json.dumps(record))
        self.assertNotIn("vps.example.net", json.dumps(record))

    def test_a_missing_bundle_leaves_product_and_services_unknown(self):
        self.job(1, "failed", bundle="pilot-gone")
        [record] = history.records(self.root, transport.jobs(self.root, limit=None))
        self.assertIsNone(record["product"])
        self.assertIsNone(record["services"])

    def test_a_live_rollout_names_its_running_step(self):
        self.bundle("pilot-v1", {"api": "ghcr.io/o/api@sha256:" + "a" * 64})
        live = self.job(2, "queued")
        runner.write_json(self.root / "observed" / (live + ".json"),
                          {"status": "running", "steps": [
                              {"name": "Check target", "status": "succeeded", "startedAt": "2026-10-02T10:00:01+00:00"},
                              {"name": "Pull images", "status": "running", "startedAt": "2026-10-02T10:00:02+00:00"}]})
        [record] = history.records(self.root, transport.jobs(self.root, limit=None))
        self.assertEqual("running", record["status"])
        self.assertEqual({"name": "Pull images", "status": "running", "startedAt": "2026-10-02T10:00:02+00:00",
                          "index": 2}, record["currentStep"])

    def test_a_queued_rollout_without_steps_has_no_current_step(self):
        self.job(3, "queued")
        [record] = history.records(self.root, transport.jobs(self.root, limit=None))
        self.assertIsNone(record["currentStep"])

    def test_observation_reads_steps_sites_and_warnings_locally(self):
        job = self.job(4, "queued")
        runner.write_json(self.root / "observed" / (job + ".json"),
                          {"status": "succeeded", "completedAt": "2026-10-04T10:02:00+00:00",
                           "steps": [{"name": "Probe sites", "status": "warning"}],
                           "sites": [{"name": "app", "url": "https://dev.pilot.example", "probe": {"ok": False}}],
                           "warnings": ["Site app did not answer through the ingress"], "remote": "/srv/x"})
        result = history.observation(self.root, job, transport.observed_outcome)
        self.assertEqual("succeeded", result["status"])
        self.assertEqual("warning", result["steps"][0]["status"])
        self.assertEqual("app", result["sites"][0]["name"])
        self.assertEqual(1, len(result["warnings"]))
        self.assertNotIn("remote", result)

    def test_observation_refuses_an_unknown_or_malformed_id(self):
        with self.assertRaisesRegex(runner.Rejected, "Unknown deployment"):
            history.observation(self.root, "00000000-0000-4000-8000-000000000099", transport.observed_outcome)
        with self.assertRaises(ValueError):
            history.observation(self.root, "../jobs/x", transport.observed_outcome)


if __name__ == "__main__":
    unittest.main()
