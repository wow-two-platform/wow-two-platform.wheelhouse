"""Pre-deploy snapshots on prod: what they cover, that they come before any change, that a failed one stops the
rollout, and their retention. Docker is scripted; the files a dump or archive would write are created by the fake."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import runner


class SnapshotDocker:
    events = []
    volumes = {"pilot-prod_keys"}
    fail = None
    # Volumes whose files start with SQLite's header, and the containers writing to them.
    sqlite = set()
    writers = ["c0ffee"]
    refuse_unpause = 0

    def __init__(self, target, environment):
        pass

    def preflight(self, manifest):
        self.events.append("preflight")

    def pull(self, bundle):
        self.events.append("pull")

    def up(self, bundle, recreate=None):
        self.events.append("up")

    def verify(self, bundle, manifest):
        self.events.append("verify")

    def execute(self, arguments, step, timeout=600):
        if arguments[:3] == ["docker", "volume", "inspect"]:
            if arguments[-1] not in self.volumes:
                raise runner.CommandFailed("docker volume inspect failed (exit 1)")
            return arguments[-1]
        if step.startswith("inspect "):
            # The file-database probe mounts the volume read-only, without a network, and reads headers only.
            self.assertions(arguments)
            return "found\n" if step.removeprefix("inspect ") in self.sqlite else ""
        if arguments[:2] == ["docker", "ps"]:
            self.events.append("ps " + next(item for item in arguments if item.startswith("volume=")))
            return "\n".join(self.writers) + "\n"
        if arguments[1] in ("pause", "unpause"):
            if arguments[1] == "unpause" and SnapshotDocker.refuse_unpause:
                SnapshotDocker.refuse_unpause -= 1
                raise runner.CommandFailed("docker unpause failed (exit 1)")
            self.events.append(arguments[1] + " " + arguments[2])
            return ""
        self.events.append(" ".join(arguments[:3]))
        return ""

    @staticmethod
    def assertions(arguments):
        assert "--network" in arguments and arguments[arguments.index("--network") + 1] == "none"
        assert any(item.endswith(":/volume:ro") for item in arguments)
        assert runner.SQLITE_HEADER in arguments[-1] and "-name '*.litedb'" in arguments[-1]

    def run(self, arguments, step, secrets=None, timeout=1800):
        self.events.append(("run", step, arguments, dict(secrets or {})))
        if self.fail and self.fail in step:
            raise runner.CommandFailed(step + " failed (exit 1); inspect the target privately")
        folder = Path(next(item for item in arguments if item.endswith(":/snapshot")).rsplit(":", 1)[0])
        written = next((item for item in arguments if item.startswith("/snapshot/")), None)
        if written is None:  # tar runs in a shell: the archive is named inside its script
            written = "/snapshot/" + arguments[-1].split("/snapshot/")[1].split(" ")[0]
        (folder / written.removeprefix("/snapshot/")).write_bytes(b"x" * 10)


class SnapshotTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        settings = self.root / "settings.json"
        settings.write_text(json.dumps({"ConnectionStrings": {
            "Pilot": "Host=platform-postgres;Port=5432;Database=pilot_prod;Username=pilot;Password=DO_NOT_LOG",
            "Same": "Host=platform-postgres;Database=pilot_prod;Username=pilot;Password=DO_NOT_LOG",
            "Cache": "valkey:6379,password=DO_NOT_LOG"}, "Database": {"Connection": "x"}}))
        settings.chmod(0o600)
        self.target = {"product": "pilot", "environment": "prod", "root": str(self.root / "state"),
                       "settings": {"api": str(settings)}, "minimumFreeBytes": 0,
                       "variables": {"PLATFORM_NETWORK": "platform"}}
        self.bundle = self.root / "bundle"
        self.bundle.mkdir()
        image = "ghcr.io/owner/api@sha256:" + "a" * 64
        self.manifest = {"schemaVersion": 1, "product": "pilot", "release": "v1", "sourceCommit": "a" * 40,
                         "platform": "linux/amd64", "rollbackCompatible": False, "images": {"api": image},
                         "requiredConfiguration": {"api": ["Database:Connection"]}, "needs": {"api": ["postgres"]}}
        self.compose = {"services": {"api": {"image": image, "platform": "linux/amd64",
                                             "healthcheck": {"test": ["CMD", "true"]},
                                             "volumes": ["keys:/data/keys", "cache:/cache"]}},
                        "volumes": {"keys": {}, "cache": None, "shared": {"external": True}}}
        self.save()
        SnapshotDocker.events = []
        SnapshotDocker.fail = None
        SnapshotDocker.sqlite = set()
        SnapshotDocker.refuse_unpause = 0

    def tearDown(self):
        self.temporary.cleanup()

    def save(self):
        compose = json.dumps(self.compose).encode()
        (self.bundle / "compose.json").write_bytes(compose)
        self.manifest["composeSha256"] = hashlib.sha256(compose).hexdigest()
        (self.bundle / "release.json").write_text(json.dumps(self.manifest))

    def apply(self):
        return runner.apply(self.bundle, self.target, "test-operator", docker_factory=SnapshotDocker)

    def runs(self):
        return [event for event in SnapshotDocker.events if isinstance(event, tuple)]

    def test_prod_snapshots_each_database_once_and_each_existing_volume_before_pulling(self):
        result = self.apply()
        self.assertEqual("succeeded", result["status"])
        order = [event if isinstance(event, str) else event[1] for event in SnapshotDocker.events]
        self.assertEqual(["preflight", "pg_dump pilot_prod", "tar pilot-prod_keys", "pull", "up", "verify"], order)
        dump, archive = self.runs()
        # The password reaches pg_dump through the environment only.
        self.assertEqual({"PGPASSWORD": "DO_NOT_LOG"}, dump[3])
        self.assertNotIn("DO_NOT_LOG", " ".join(dump[2]))
        self.assertIn("--network", dump[2])
        self.assertIn("pilot-prod_keys:/volume:ro", archive[2])
        snapshot = result["snapshot"]
        folder = Path(snapshot["path"])
        self.assertEqual(self.root / "state/pilot-prod/snapshots" / result["id"], folder)
        self.assertEqual(["platform-postgres-pilot_prod.dump"], [item["file"] for item in snapshot["databases"]])
        self.assertEqual([("pilot-prod_keys", 10)], [(item["volume"], item["bytes"]) for item in snapshot["volumes"]])
        self.assertTrue((folder / "snapshot.json").is_file())
        self.assertEqual(3, len(snapshot["restore"]))
        self.assertTrue(snapshot["restore"][0].startswith("docker stop"))
        self.assertIn("pg_restore", snapshot["restore"][1])
        self.assertNotIn("DO_NOT_LOG", json.dumps(result))
        step = next(item for item in result["steps"] if item["name"] == "Snapshot data")
        self.assertEqual("succeeded", step["status"])
        self.assertIn("1 database and 1 volume", step["detail"])

    def test_a_volume_holding_sqlite_is_archived_with_its_writers_paused(self):
        SnapshotDocker.sqlite = {"pilot-prod_keys"}
        result = self.apply()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        order = [event if isinstance(event, str) else event[1] for event in SnapshotDocker.events]
        # Paused just around its archive, resumed before anything else happens.
        self.assertEqual(["preflight", "pg_dump pilot_prod", "ps volume=pilot-prod_keys", "pause c0ffee",
                          "tar pilot-prod_keys", "unpause c0ffee", "pull", "up", "verify"], order)
        volume = result["snapshot"]["volumes"][0]
        self.assertEqual(("paused", 1), (volume["consistency"], volume["pausedContainers"]))
        self.assertIsInstance(volume["pausedSeconds"], float)
        step = next(item for item in result["steps"] if item["name"] == "Snapshot data")
        self.assertIn("1 file-database volume archived with its writers paused", step["detail"])

    def test_a_volume_without_a_file_database_is_archived_live(self):
        result = self.apply()
        self.assertEqual("live", result["snapshot"]["volumes"][0]["consistency"])
        self.assertFalse(any(isinstance(event, str) and event.startswith("pause") for event in SnapshotDocker.events))

    def test_a_failed_archive_still_resumes_its_paused_writers(self):
        SnapshotDocker.sqlite = {"pilot-prod_keys"}
        SnapshotDocker.fail = "tar"
        result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertIn("unpause c0ffee", SnapshotDocker.events)
        # A refused resume is retried; one that keeps failing fails the snapshot and says so.
        SnapshotDocker.fail, SnapshotDocker.events, SnapshotDocker.refuse_unpause = None, [], 1
        self.assertEqual("succeeded", self.apply()["status"])
        SnapshotDocker.refuse_unpause = 3
        with patch.object(runner.time, "sleep"):
            result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertIn("docker unpause failed for 1 container", result["reason"])

    def test_a_failed_snapshot_fails_the_rollout_before_anything_changes(self):
        SnapshotDocker.fail = "tar"
        result = self.apply()
        self.assertEqual("failed", result["status"])
        self.assertNotIn("mutationStarted", result)
        self.assertTrue(result["reason"].startswith("Snapshot failed: tar pilot-prod_keys failed"))
        self.assertNotIn("pull", SnapshotDocker.events)
        self.assertEqual([], list((self.root / "state/pilot-prod/snapshots").iterdir()), "the partial snapshot goes")
        self.assertFalse((self.root / "state/pilot-prod/current.json").exists())
        # Nothing changed, so the target takes the next rollout without a reconcile.
        SnapshotDocker.fail = None
        self.assertEqual("succeeded", self.apply()["status"])

    def test_dev_takes_no_snapshot_and_a_release_with_nothing_to_keep_skips_the_step(self):
        self.target["environment"] = "dev"
        self.apply()
        self.assertEqual([], self.runs())
        self.target["environment"] = "prod"
        self.manifest.pop("needs")
        SnapshotDocker.volumes = set()
        self.save()
        try:
            result = self.apply()
        finally:
            SnapshotDocker.volumes = {"pilot-prod_keys"}
        step = next(item for item in result["steps"] if item["name"] == "Snapshot data")
        self.assertEqual(("skipped", "No database or volume to snapshot"), (step["status"], step["detail"]))
        self.assertNotIn("snapshot", result)

    def test_a_target_keeps_its_newest_snapshots(self):
        self.target["snapshot"] = {"keep": 2}
        ids = [self.apply()["id"] for _ in range(3)]
        kept = sorted(path.name for path in (self.root / "state/pilot-prod/snapshots").iterdir())
        self.assertEqual(sorted(ids[1:]), kept)

    def test_a_connection_string_reads_as_parts_and_never_as_an_option(self):
        self.assertEqual({"host": "db", "port": 6432, "database": "app", "username": "u", "password": "p"},
                         runner.connection_of("Server=db;Port=6432;Initial Catalog=app;User ID=u;Password=p"))
        self.assertIsNone(runner.connection_of("valkey:6379,password=x"))
        for text in ("Host=--file=/etc/passwd;Database=app", "Host=db;Database=app x", "Host=db;Database=app;Port=99999"):
            with self.subTest(text=text), self.assertRaisesRegex(ValueError, "dump safely"):
                runner.connection_of(text)
        for settings in ({"image": "evil image"}, {"keep": 0}, "five"):
            with self.subTest(settings=settings), self.assertRaises(ValueError):
                runner.snapshot_settings({"snapshot": settings})


if __name__ == "__main__":
    unittest.main()
