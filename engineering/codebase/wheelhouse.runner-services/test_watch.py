"""The watch window after a successful rollout: the transport's bounded passes judge each look against the first, and
the target's read-only look reports containers and the sites that answered at the deploy."""
import datetime
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import history
import runner
import transport

JOB = "11111111-1111-4111-8111-111111111111"
REMOTE = "22222222-2222-4222-8222-222222222222"
DONE = datetime.datetime(2026, 10, 18, 10, 0, tzinfo=datetime.timezone.utc)


class WatchPassTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        transport.write_json(self.root / "jobs" / (JOB + ".json"),
                             {"id": JOB, "targetId": "pilot-dev", "release": "v1.0.0", "status": "running",
                              "remoteJobId": REMOTE, "submittedAt": "2026-10-18T09:58:00+00:00"})
        transport.write_json(self.root / "observed" / (JOB + ".json"),
                             {"status": "succeeded", "completedAt": DONE.isoformat()})
        self.readings = []
        self.config = {"ssh": {}, "target": {"product": "pilot", "environment": "dev", "root": "/srv/wheelhouse"}}

    def tearDown(self):
        self.temporary.cleanup()

    def look(self, restarts=0, state="running", health="healthy", sites=(("app", True),), current=REMOTE):
        self.readings.append({"currentId": current, "containers": [{"service": "api", "state": state, "health": health,
                                                                    "restarts": restarts}],
                              "sites": [{"name": name, "ok": ok, "detail": None if ok else "The ingress did not answer"}
                                        for name, ok in sites]})

    def run_pass(self, minutes_after):
        def remote(ssh, action, target, *arguments, release=None, timeout=30):
            self.assertEqual("watch", action)
            if not self.readings:
                raise transport.CommandFailed("SSH connection refused")
            return self.readings.pop(0)
        with patch.object(transport.fleet, "resolve_target", return_value=self.config), \
                patch.object(transport, "Ssh"), patch.object(transport, "run_remote", side_effect=remote):
            return transport.watch(self.root, 10, clock=lambda: DONE.timestamp() + minutes_after * 60)

    def record(self):
        return json.loads((self.root / "watching" / (JOB + ".json")).read_text())

    def test_a_clean_window_passes_after_its_minutes(self):
        self.look(restarts=2)
        self.run_pass(1)
        self.look(restarts=2)
        self.run_pass(3)
        self.assertEqual(("watching", 2, {"api": 2}), (self.record()["status"], self.record()["checks"], self.record()["baseline"]))
        self.assertEqual(1, self.run_pass(11)["passed"])
        self.assertEqual("passed", self.record()["status"])
        self.assertEqual("passed", history.watch_of(self.root, JOB)["status"])

    def test_a_restart_or_a_silent_site_degrades_the_rollout(self):
        self.look(restarts=0)
        self.run_pass(1)
        self.look(restarts=3, sites=(("app", False),))
        self.assertEqual(1, self.run_pass(3)["degraded"])
        record = self.record()
        self.assertEqual("degraded", record["status"])
        self.assertEqual(["api restarted 3 times since the deploy",
                          "Site app stopped answering: The ingress did not answer"], record["findings"])
        self.run_pass(20)
        self.assertEqual("degraded", self.record()["status"], "a degraded rollout stays degraded")

    def test_an_unhealthy_container_on_the_first_look_degrades_too(self):
        self.look(state="exited", health=None)
        self.run_pass(1)
        self.assertEqual(["api is exited"], self.record()["findings"])

    def test_a_rollout_another_one_replaced_reads_replaced_and_an_unreadable_target_unverified(self):
        self.look(current="33333333-3333-4333-8333-333333333333")
        self.run_pass(1)
        self.assertEqual("replaced", self.record()["status"])
        (self.root / "watching" / (JOB + ".json")).unlink()
        self.run_pass(2)
        self.run_pass(4)
        self.assertEqual(2, self.record()["unreadable"])
        self.run_pass(12)
        self.assertEqual("unverified", self.record()["status"])

    def test_a_rollout_finished_before_its_window_is_never_watched_and_looks_are_paced(self):
        self.assertEqual(0, self.run_pass(30)["checked"])
        self.assertFalse((self.root / "watching" / (JOB + ".json")).exists())
        self.look()
        self.look()
        self.run_pass(1)
        self.assertEqual(0, self.run_pass(1.5)["checked"], "one look a minute")
        self.assertEqual(1, self.run_pass(2.1)["checked"])


class TargetWatchTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.target = {"product": "pilot", "environment": "dev", "root": str(self.root)}
        runner.write_json(self.root / "pilot-dev" / "current.json", {
            "id": REMOTE, "release": "v1.0.0",
            "sites": [{"name": "app", "service": "api", "exposure": "public", "url": "http://app.example",
                       "probe": {"ok": True}},
                      {"name": "admin", "service": "api", "exposure": "private", "url": "http://admin.example",
                       "probe": {"ok": False, "detail": "no route"}}]})

    def tearDown(self):
        self.temporary.cleanup()

    def test_a_look_reads_containers_and_requests_only_the_sites_that_answered_at_the_deploy(self):
        asked = []

        def prober(target, sites, deadline_seconds):
            asked.extend(site["name"] for site in sites)
            for site in sites:
                site["probe"] = {"ok": False, "detail": "The ingress did not answer (timeout)"}
            return sites

        reading = {"containers": [{"service": "api", "restarts": 1}], "problems": [], "condition": "ready", "release": "v1.0.0"}
        with patch.object(runner, "vitals", return_value=reading):
            result = runner.watch(self.target, prober=prober)
        self.assertEqual(["app"], asked)
        self.assertEqual((REMOTE, "v1.0.0"), (result["currentId"], result["release"]))
        self.assertEqual([{"name": "app", "service": "api", "url": "http://app.example", "ok": False,
                           "detail": "The ingress did not answer (timeout)"}], result["sites"])


if __name__ == "__main__":
    unittest.main()
