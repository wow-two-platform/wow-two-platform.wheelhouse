"""Durable, bounded observation of target journals without a browser or another deployment."""
from concurrent.futures import ThreadPoolExecutor
import datetime
import json
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch
import runner
import transport


class FollowingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.clock = 100.0

    def tearDown(self):
        self.temporary.cleanup()

    def job(self, number=1, receipt=True):
        identifier = "00000000-0000-0000-0000-" + str(number).zfill(12)
        record = {"id": identifier, "targetId": "pilot", "release": "v1", "status": "queued",
                  "submittedAt": datetime.datetime.fromtimestamp(number, datetime.timezone.utc).isoformat(),
                  "ssh": {}, "remote": "/srv/wheelhouse/incoming/" + identifier}
        if receipt:
            record["remoteJobId"] = identifier
        transport.write_json(self.root / "jobs" / (identifier + ".json"), record)
        return identifier

    def observed(self, job, status, complete=True):
        value = {"id": job, "status": status}
        if complete:
            value["completedAt"] = "2026-10-06T00:00:00+00:00"
        transport.write_json(self.root / "observed" / (job + ".json"), value)
        return value

    def follow(self):
        return transport.follow(self.root, clock=lambda: self.clock)

    @staticmethod
    def succeeded(command):
        return json.dumps({"id": shlex.split(command)[-1], "status": "succeeded",
                           "completedAt": "2026-10-06T00:00:00+00:00"})

    def test_new_process_discovers_a_pending_job_older_than_historys_fifty_rows(self):
        oldest = self.job()
        for number in range(2, 56):
            self.observed(self.job(number), "succeeded")
        self.assertNotIn(oldest, [job["id"] for job in transport.jobs(self.root)])
        script = """
import json, shlex, sys
from pathlib import Path
from unittest.mock import patch
import transport
root = Path(sys.argv[1])
def read(command):
    assert ' status ' in command
    return json.dumps({'id': shlex.split(command)[-1], 'status': 'succeeded',
                       'completedAt': '2026-10-06T00:00:00+00:00'})
with patch.object(transport, 'Ssh') as ssh, patch.object(transport, 'submit') as submit:
    ssh.return_value.run.side_effect = read
    print(json.dumps(transport.follow(root, clock=lambda: 100)))
    submit.assert_not_called()
"""
        command = [sys.executable, "-c", script, str(self.root)]
        first = subprocess.run(command, cwd=Path(__file__).parent, check=True, capture_output=True, text=True)
        self.assertEqual(1, json.loads(first.stdout)["checked"])
        self.assertEqual("succeeded", transport.read_json(self.root / "observed" / (oldest + ".json"))["status"])
        second = subprocess.run(command, cwd=Path(__file__).parent, check=True, capture_output=True, text=True)
        self.assertEqual(0, json.loads(second.stdout)["checked"])

    def test_transient_ssh_failure_backs_off_without_replacing_the_last_target_status(self):
        job = self.job()
        self.observed(job, "running", complete=False)
        with patch.object(transport, "Ssh") as ssh:
            ssh.return_value.run.side_effect = transport.CommandFailed("SSH operation timed out")
            self.assertEqual(1, self.follow()["unavailable"])
            self.assertEqual("running", transport.jobs(self.root)[0]["status"])
            self.assertEqual(0, self.follow()["checked"])
            self.assertEqual(1, ssh.return_value.run.call_count)
            self.clock = 110
            self.follow()
            schedule = transport.read_json(self.root / "following" / (job + ".json"))
            self.assertEqual({"nextAttemptAt": 130, "failures": 2, "hasReceipt": True}, schedule)
            self.clock = 129
            self.follow()
            self.assertEqual(2, ssh.return_value.run.call_count)
            self.clock = 130
            ssh.return_value.run.side_effect = self.succeeded
            self.assertEqual(1, self.follow()["checked"])
            self.assertEqual("succeeded", transport.jobs(self.root)[0]["status"])

    def test_backoff_is_capped_and_survives_another_pass(self):
        job = self.job()
        transport.write_json(self.root / "following" / (job + ".json"), {"nextAttemptAt": 0, "failures": 20})
        with patch.object(transport, "Ssh") as ssh:
            ssh.return_value.run.side_effect = transport.CommandFailed("SSH operation timed out")
            self.follow()
            self.assertEqual(400, transport.read_json(self.root / "following" / (job + ".json"))["nextAttemptAt"])
            self.clock = 399
            self.follow()
            self.assertEqual(1, ssh.return_value.run.call_count)

    def test_terminal_status_without_completion_continues_through_rollback_and_housekeeping(self):
        for provisional, final in (("failed", "rolled_back"), ("succeeded", "succeeded")):
            with self.subTest(provisional=provisional):
                job = self.job()
                self.observed(job, provisional, complete=False)
                (self.root / "following" / (job + ".json")).unlink(missing_ok=True)
                with patch.object(transport, "Ssh") as ssh:
                    ssh.return_value.run.return_value = json.dumps({"id": job, "status": final,
                        "completedAt": "2026-10-06T00:00:00+00:00", "steps": [{"name": "Final step"}]})
                    self.assertEqual(1, self.follow()["checked"])
                    self.assertEqual(final, transport.jobs(self.root)[0]["status"])
                    self.assertEqual([{ "name": "Final step" }], transport.status(self.root, job)["steps"])
                    self.assertEqual(2, ssh.return_value.run.call_count)

    def test_browser_returns_last_observation_while_follower_reads_and_final_cannot_regress(self):
        job = self.job()
        self.observed(job, "running", complete=False)
        started, release = threading.Event(), threading.Event()
        def read(command):
            started.set()
            self.assertTrue(release.wait(3))
            return self.succeeded(command)
        with patch.object(transport, "Ssh") as ssh, ThreadPoolExecutor(max_workers=1) as executor:
            ssh.return_value.run.side_effect = read
            pending = executor.submit(self.follow)
            self.assertTrue(started.wait(3))
            try:
                began = time.monotonic()
                self.assertEqual("running", transport.status(self.root, job)["status"])
                self.assertLess(time.monotonic() - began, 1)
            finally:
                release.set()
            self.assertEqual(1, pending.result(timeout=3)["checked"])
            ssh.return_value.run.side_effect = lambda _: json.dumps({"id": job, "status": "running"})
            self.assertEqual("succeeded", transport.status(self.root, job)["status"])

    def test_explicit_read_discovers_reconciliation_performed_directly_on_the_target(self):
        job = self.job()
        for completed in ("2026-10-06T00:01:00+00:00", "2026-10-05T23:59:00+00:00"):
            with self.subTest(completed=completed):
                self.observed(job, "failed")
                with patch.object(transport, "Ssh") as ssh:
                    ssh.return_value.run.return_value = json.dumps({"id": job, "status": "interrupted",
                                                                  "completedAt": completed})
                    self.assertEqual(0, self.follow()["checked"])
                    ssh.assert_not_called()
                    self.assertEqual("interrupted", transport.status(self.root, job)["status"])
                self.assertEqual("interrupted", transport.jobs(self.root)[0]["status"])

    def test_reconciliation_receipt_wins_a_concurrent_older_observation(self):
        job = self.job()
        self.observed(job, "running", complete=False)
        def read(command):
            transport.write_json(self.root / "reconciled" / (job + ".json"),
                {"id": job, "status": "interrupted", "completedAt": "2026-10-06T00:00:00+00:00",
                 "reconciledBy": "operator"})
            return json.dumps({"id": job, "status": "running"})
        with patch.object(transport, "Ssh") as ssh:
            ssh.return_value.run.side_effect = read
            self.follow()
        self.assertEqual("interrupted", transport.jobs(self.root)[0]["status"])
        # Even a stale writer publishing after the receipt cannot mask the operator's reconciliation.
        self.observed(job, "running", complete=False)
        self.assertEqual("interrupted", transport.jobs(self.root)[0]["status"])

    def test_lost_receipt_stays_unknown_without_launching_and_a_late_receipt_can_resume(self):
        job = self.job(receipt=False)
        with patch.object(transport, "Ssh") as ssh, patch.object(transport, "submit") as submit:
            self.follow()
            self.assertEqual("unknown", transport.jobs(self.root)[0]["status"])
            ssh.assert_not_called()
            submit.assert_not_called()
            self.clock += 10
            self.follow()
            self.assertEqual(130, transport.read_json(self.root / "following" / (job + ".json"))["nextAttemptAt"])
            self.job(receipt=True)
            self.clock += 1  # A newly persisted receipt bypasses the older unknown-receipt backoff.
            ssh.return_value.run.side_effect = self.succeeded
            self.follow()
            self.assertEqual("succeeded", transport.jobs(self.root)[0]["status"])
            submit.assert_not_called()

    def test_pass_is_bounded_and_does_not_starve_older_jobs(self):
        identifiers = [self.job(number) for number in range(1, 5)]
        with patch.object(transport, "Ssh") as ssh:
            ssh.return_value.run.side_effect = self.succeeded
            self.assertEqual(2, self.follow()["checked"])
            self.assertEqual(2, ssh.return_value.run.call_count)
            self.assertEqual(2, self.follow()["checked"])
            self.assertEqual(set(identifiers), {job["id"] for job in transport.jobs(self.root) if job["status"] == "succeeded"})

    def test_another_pass_does_not_wait_on_a_running_pass(self):
        self.job()
        with transport.observation_lock(self.root / "following" / "pass.lock"):
            with patch.object(transport, "Ssh") as ssh:
                self.assertEqual({"checked": 0, "unavailable": 0, "busy": 1}, self.follow())
                ssh.assert_not_called()

    def test_corrupt_observation_and_schedule_do_not_hide_a_submission(self):
        job = self.job()
        for folder in ("observed", "following"):
            (self.root / folder).mkdir()
            (self.root / folder / (job + ".json")).write_text("{")
        with patch.object(transport, "Ssh") as ssh:
            ssh.return_value.run.side_effect = self.succeeded
            self.assertEqual(1, self.follow()["checked"])
        self.assertEqual("succeeded", transport.jobs(self.root)[0]["status"])

    def test_non_object_and_invalid_job_records_do_not_starve_valid_submissions(self):
        job = self.job()
        for index, value in enumerate(([], None, {"id": []}, {"id": None}, "invalid"), start=2):
            path = self.root / "jobs" / ("00000000-0000-0000-0000-" + str(index).zfill(12) + ".json")
            transport.write_json(path, value)
        with patch.object(transport, "Ssh") as ssh:
            ssh.return_value.run.side_effect = self.succeeded
            self.assertEqual(1, self.follow()["checked"])
        self.assertEqual([(job, "succeeded")], [(row["id"], row["status"]) for row in transport.jobs(self.root)])

    def test_malformed_status_does_not_abort_following_other_submissions(self):
        for number, status in enumerate((["running"], {"invalid": True}), start=1):
            self.observed(self.job(number), status)
        with patch.object(transport, "Ssh") as ssh:
            ssh.return_value.run.side_effect = self.succeeded
            self.assertEqual(2, self.follow()["checked"])
        self.assertTrue(all(row["status"] == "succeeded" for row in transport.jobs(self.root)))

    def test_malformed_receipt_does_not_abort_following_or_invent_completed_reconciliation(self):
        invalid = self.job(1)
        record_path = self.root / "jobs" / (invalid + ".json")
        record = transport.read_json(record_path)
        record["remoteJobId"] = ["invalid"]
        transport.write_json(record_path, record)
        valid = self.job(2)
        transport.write_json(self.root / "reconciled" / (valid + ".json"),
                             {"id": valid, "status": "interrupted", "completedAt": ["invalid"]})
        with patch.object(transport, "Ssh") as ssh:
            ssh.return_value.run.side_effect = self.succeeded
            self.assertEqual({"checked": 1, "unavailable": 1, "busy": 0}, self.follow())
            self.assertEqual(1, ssh.return_value.run.call_count)
        self.assertEqual("succeeded", next(row for row in transport.jobs(self.root) if row["id"] == valid)["status"])

    def test_infinite_schedule_values_do_not_abort_following_other_submissions(self):
        for number, schedule in enumerate(({"nextAttemptAt": float("inf")},
                                          {"nextAttemptAt": 0, "failures": float("inf")}), start=1):
            job = self.job(number)
            transport.write_json(self.root / "following" / (job + ".json"), schedule)
        with patch.object(transport, "Ssh") as ssh:
            ssh.return_value.run.side_effect = self.succeeded
            self.assertEqual(2, self.follow()["checked"])

    def test_unexpected_remote_identity_cannot_complete_another_job(self):
        self.job()
        with patch.object(transport, "Ssh") as ssh:
            ssh.return_value.run.return_value = json.dumps({"id": "wrong", "status": "succeeded",
                                                          "completedAt": "2026-10-06T00:00:00+00:00"})
            self.assertEqual(1, self.follow()["unavailable"])
        self.assertEqual("queued", transport.jobs(self.root)[0]["status"])

    def test_atomic_publishers_own_distinct_temporary_files(self):
        path = self.root / "observed.json"
        barrier = threading.Barrier(2)
        original = json.dump
        def dump(value, stream, **kwargs):
            barrier.wait(timeout=3)
            return original(value, stream, **kwargs)
        with patch.object(runner.json, "dump", side_effect=dump), ThreadPoolExecutor(max_workers=2) as executor:
            writes = [executor.submit(runner.write_json, path, {"writer": index}) for index in range(2)]
            for write in writes:
                write.result(timeout=3)
        self.assertIn(runner.read_json(path)["writer"], (0, 1))
        self.assertEqual(0o600, path.stat().st_mode & 0o777)
        self.assertEqual([], list(self.root.glob("*.tmp")))
