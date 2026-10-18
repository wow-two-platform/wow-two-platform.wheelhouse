"""Retention: old release folders and uploads on a target, and old bundles on the control host, in bounded daily runs."""
import json
import os
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import patch
import runner
import transport


class TargetRetentionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.base = self.root / "pilot-prod"
        self.target = {"product": "pilot", "environment": "prod", "root": str(self.root)}

    def tearDown(self):
        self.temporary.cleanup()

    def release(self, name, age, pruned=True):
        folder = self.base / "releases" / name
        folder.mkdir(parents=True)
        (folder / "release.json").write_text("{}")
        os.utime(folder / "release.json", (1000 - age, 1000 - age))
        if pruned:
            (folder / runner.PRUNED).touch()

    def upload(self, name, project, age_seconds):
        folder = self.root / "incoming" / name
        folder.mkdir(parents=True)
        product, environment = project.split("-")
        (folder / "target.json").write_text(json.dumps({"product": product, "environment": environment}))
        moment = time.time() - age_seconds
        os.utime(folder, (moment, moment))

    def test_release_folders_beyond_the_newest_go_once_their_images_are_gone_never_the_running_or_rollback_one(self):
        for number in range(14):
            self.release("r" + str(number).zfill(2), age=number, pruned=number != 11)
        removed = runner.prune_releases(self.base, {"r13", "r12"}, keep=10)
        left = sorted(path.name for path in (self.base / "releases").iterdir())
        # r10 goes; r11 still has its images; r12 and r13 are the running and rollback releases.
        self.assertEqual(1, removed)
        self.assertEqual(["r" + str(number).zfill(2) for number in (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13)], left)

    def test_uploads_beyond_the_newest_go_once_a_day_old_and_another_projects_never(self):
        for number in range(13):
            self.upload("p" + str(number).zfill(2), "pilot-prod", age_seconds=number * 8_000)
        self.upload("other", "pilot-dev", age_seconds=10 ** 7)
        self.assertEqual(2, runner.prune_incoming(self.root, "pilot-prod", keep=10))
        left = {path.name for path in (self.root / "incoming").iterdir()}
        # p10 is not a day old yet; p11 and p12 are.
        self.assertEqual({"p" + str(number).zfill(2) for number in range(11)} | {"other"}, left)

    def test_a_prune_keeps_what_the_target_runs_and_skips_a_target_mid_rollout(self):
        runner.write_json(self.base / "current.json", {"id": "r13"})
        runner.write_json(self.base / "jobs" / "r13.json", {"id": "r13", "previous": "r12"})
        for number in range(14):
            self.release("r" + str(number).zfill(2), age=number)
        self.assertEqual({"r13", "r12"}, runner.kept_releases(self.base))
        result = runner.prune(self.target)
        self.assertEqual(("pilot-prod", 2), (result["project"], result["releases"]))
        self.assertTrue((self.base / "releases" / "r13").is_dir())
        with runner.deployment_lock(self.base):
            self.assertEqual({"project": "pilot-prod", "busy": True}, runner.prune(self.target))


class ControlRetentionTests(unittest.TestCase):
    DAY = 24 * 60 * 60

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)

    def tearDown(self):
        self.temporary.cleanup()

    def bundle(self, name, age_days, downloaded=True):
        folder = self.root / "bundles" / name
        folder.mkdir(parents=True)
        (folder / "release.json").write_text("{}")
        if downloaded:
            (folder / "source.json").write_text("{}")
            moment = time.time() - age_days * self.DAY
            os.utime(folder / "source.json", (moment, moment))

    def job(self, number, bundle, status="succeeded"):
        identifier = "00000000-0000-4000-8000-" + str(number).zfill(12)
        transport.write_json(self.root / "jobs" / (identifier + ".json"),
                             {"id": identifier, "targetId": "pilot-dev", "bundleId": bundle, "status": status,
                              "submittedAt": "2026-10-%02dT10:00:00+00:00" % number})
        if status == "succeeded":
            transport.write_json(self.root / "observed" / (identifier + ".json"),
                                 {"status": "succeeded", "completedAt": "2026-10-%02dT10:05:00+00:00" % number})

    def test_an_unused_downloaded_bundle_goes_after_a_week_and_a_used_or_imported_one_stays(self):
        self.bundle("used", 30)
        self.bundle("fresh", 2)
        self.bundle("old", 30)
        self.bundle("imported", 30, downloaded=False)
        self.job(1, "used")
        with patch.object(transport.fleet, "active_targets", return_value=()):
            self.assertEqual(1, transport.prune_bundles(self.root, time.time()))
        self.assertEqual({"used", "fresh", "imported"}, {path.name for path in (self.root / "bundles").iterdir()})

    def test_a_daily_cycle_prunes_two_targets_a_pass_and_bundles_after_each_rollout(self):
        bindings = tuple(transport.fleet.Target("t" + str(number), "s", "pilot", transport.fleet.DeploymentEnvironment.DEV,
                                                (), "platform") for number in range(3))
        pruned = []

        def remote(ssh, action, target, *arguments, release=None, timeout=30):
            self.assertEqual("prune", action)
            pruned.append(target["id"])
            return {"releases": 1, "incoming": 0}
        config = lambda root, identifier: {"ssh": {}, "target": {"id": identifier}}
        with patch.object(transport.fleet, "active_targets", return_value=bindings), \
                patch.object(transport.fleet, "resolve_target", side_effect=config), \
                patch.object(transport, "Ssh"), patch.object(transport, "run_remote", side_effect=remote), \
                patch.object(transport, "prune_bundles", return_value=0) as bundles:
            first = transport.retention(self.root, clock=lambda: 10 * self.DAY)
            second = transport.retention(self.root, clock=lambda: 10 * self.DAY + 120)
            third = transport.retention(self.root, clock=lambda: 10 * self.DAY + 240)
            self.assertEqual(["t0", "t1", "t2"], pruned)
            self.assertEqual((2, 1, 0), (len(first["targets"]), len(second["targets"]), len(third["targets"])))
            self.assertEqual(1, bundles.call_count, "bundles are pruned once a day")
            self.job(1, "x")  # a rollout finished since
            transport.retention(self.root, clock=lambda: 10 * self.DAY + 360)
            self.assertEqual(2, bundles.call_count, "and after each rollout")
            transport.retention(self.root, clock=lambda: 11 * self.DAY + 1)
            self.assertEqual(["t0", "t1", "t2", "t0", "t1"], pruned, "a new cycle starts a day later")


if __name__ == "__main__":
    unittest.main()
