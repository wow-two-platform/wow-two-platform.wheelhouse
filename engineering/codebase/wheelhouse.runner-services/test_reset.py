"""The database reset on the service-control path (v0.9): a snapshot, every service stopped, the database dropped and
created again through the platform PostgreSQL, the migration levels at 0, and the next deploy's migrate step building
the schema again. The migrate rig's fake database stands in for PostgreSQL; the platform's container answers `docker ps`
and `docker inspect` the way Docker does."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import runner
import test_controls
import test_migrate
import transport
from test_migrate import MigrateDocker, Rig
from test_runner import FakeDocker

CANARY = "Host=wheelhouse-postgres;Port=5432;Database=pilot_dev;Username=pilot_dev;Password=DO_NOT_LOG"
POSTGRES_ID = "f" * 64


class ResetDocker(MigrateDocker):
    """The migrate rig's Docker, plus what a reset asks of the host: the platform PostgreSQL's container on `platform`,
    pg_dump into the snapshot folder, the superuser's psql and Compose's stop and start. `owner` is the role pg_database
    names for the database (None: no such database) and `roles` the roles the cluster holds."""
    owner, roles, aliases, psql_calls, dumps, fail_psql, fail_stop = None, (), [], [], [], None, False

    def execute(self, arguments, step, timeout=600):
        if arguments[:2] == ["docker", "ps"] and "label=com.docker.compose.service=postgres" in arguments:
            self.events.append("find postgres")
            return POSTGRES_ID + "\n"
        if arguments[:2] == ["docker", "inspect"] and arguments[2] == POSTGRES_ID:
            return json.dumps([{"Id": POSTGRES_ID, "Config": {"Labels": {
                "com.docker.compose.project": "wheelhouse-platform", "com.docker.compose.service": "postgres"}},
                "NetworkSettings": {"Networks": {"platform": {"IPAddress": "172.30.0.3", "Aliases": self.aliases}}}}])
        return super().execute(arguments, step, timeout)

    def run(self, arguments, step, secrets=None, timeout=1800):
        assert "pg_dump" in arguments and secrets == {"PGPASSWORD": "DO_NOT_LOG"}
        assert not any("DO_NOT_LOG" in argument for argument in arguments)
        folder = Path(arguments[arguments.index("-v") + 1].split(":", 1)[0])
        (folder / arguments[-1].rsplit("/", 1)[1]).write_text("dump")
        self.dumps.append(arguments)
        self.events.append("pg_dump " + arguments[arguments.index("--dbname") + 1])

    def psql(self, container, database, sql, step, timeout=300):
        assert container == POSTGRES_ID
        self.psql_calls.append((database, sql))
        if self.fail_psql and self.fail_psql in sql:
            raise runner.CommandFailed(step + " failed (exit 1); inspect the platform PostgreSQL privately")
        if sql.startswith("SELECT (SELECT count(*) FROM pg_roles"):
            role = sql.split("rolname = '", 1)[1].split("'", 1)[0]
            return [("1" if role in self.roles else "0") + "|" + (self.owner or "")]
        if "DROP DATABASE" in sql:
            self.events.append("drop " + sql.split('"')[1])
            Rig.history = []  # the fake database holds no migration any more
        return []

    def stop(self, bundle, services=()):
        self.events.append("stop:" + ",".join(services))
        if self.fail_stop:
            raise runner.CommandFailed("compose stop failed (exit 1); inspect target containers privately")

    def start(self, bundle, services=()):
        self.events.append("start:" + ",".join(services))


class ResetTests(unittest.TestCase):
    # The migrate step's rig: a release whose image embeds the default set's migrations, applied by the runner itself.
    setUp_rig = test_migrate.MigrateStepTests.setUp
    tearDown = test_migrate.MigrateStepTests.tearDown
    base = test_migrate.MigrateStepTests.base
    release = test_migrate.MigrateStepTests.release
    apply = test_migrate.MigrateStepTests.apply
    flow = test_migrate.MigrateStepTests.flow

    def setUp(self):
        self.setUp_rig()
        ResetDocker.owner, ResetDocker.roles, ResetDocker.fail_psql = "pilot_dev", ("pilot_dev",), None
        ResetDocker.aliases, ResetDocker.psql_calls, ResetDocker.dumps = ["postgres", "wheelhouse-postgres"], [], []
        ResetDocker.fail_stop = False
        # The canary's way: no database.json; the migrate item's service names its database in its own settings.
        self.settings.write_text(json.dumps({"Database": {"Connection": "x"},
                                             "DatabaseSettings": {"ConnectionString": CANARY},
                                             "Cache": {"ConnectionString": "localhost:6379"}}))
        self.release("v1", "1", [(1, "001-pings", "expand"), (2, "002-notes", "expand")])
        first = self.apply(ResetDocker)
        self.assertEqual("succeeded", first["status"], first.get("reason"))
        FakeDocker.events = []

    def reset(self, job="11111111-1111-4111-8111-111111111190", target=None):
        return runner.control(target or self.target, "reset", [], "max", job, docker_factory=ResetDocker)

    def known(self):
        return runner.known_migrations(self.base)["sets"]

    def running(self, **changes):
        """The release the target runs, redefined in place (its kept folder too), as if it had been deployed so."""
        self.manifest.update(changes)
        self.release("v1", "1", [(1, "001-pings", "expand"), (2, "002-notes", "expand")])
        current = runner.read_json(self.base / "current.json")["id"]
        for name in ("release.json", "compose.json"):
            (self.base / "releases" / current / name).write_bytes((self.bundle / name).read_bytes())

    def test_a_reset_snapshots_drops_and_recreates_and_the_next_deploy_rebuilds_the_schema(self):
        record = self.reset()
        self.assertEqual("succeeded", record["status"], record.get("reason"))
        self.assertEqual({"kind": "reset", "services": []}, record["operation"])
        self.assertEqual(["Snapshot the database", "Stop the environment", "Reset pilot_dev", "Migration levels to 0"],
                         [step["name"] for step in record["steps"]])
        # Snapshot first, then the stop, then the drop through the platform PostgreSQL as the superuser.
        flow = [event for event in FakeDocker.events if event.split(" ")[0] in ("pg_dump", "drop") or ":" in event]
        self.assertEqual(["pg_dump pilot_dev", "stop:", "drop pilot_dev"], flow)
        self.assertEqual(['DROP DATABASE IF EXISTS "pilot_dev" WITH (FORCE);\nCREATE DATABASE "pilot_dev" OWNER '
                          '"pilot_dev";\n'], [sql for _, sql in ResetDocker.psql_calls if "DROP" in sql])
        snapshot = record["snapshot"]
        self.assertEqual(["pilot_dev"], [database["database"] for database in snapshot["databases"]])
        self.assertTrue((Path(snapshot["path"]) / "snapshot.json").is_file())
        self.assertEqual("1 database in " + snapshot["path"], record["steps"][0]["detail"])
        self.assertEqual({"databases": ["pilot_dev"]}, record["reset"])
        # Every set at level 0, read as behind from the reset; nothing stays marked stopped, and no health is claimed.
        self.assertEqual([("default", 0, "behind", "reset", 2, None)],
                         [(item["name"], item["level"], item["state"], item["source"], item["resetFrom"],
                           item["newestLabel"]) for item in self.known()])
        self.assertNotIn("appliedBy", self.known()[0])
        self.assertIsNone(record["stopped"])
        self.assertNotIn("health", record)
        self.assertNotIn("DO_NOT_LOG", json.dumps(record))
        self.assertEqual(record["id"], runner.state(self.target)["active"]["id"])
        # The next deploy of the same release migrates forward from 0, never through a rollback gate, and starts the
        # stopped containers without recreating them.
        FakeDocker.events, FakeDocker.modes = [], []
        result = self.apply(ResetDocker)
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertEqual(["pull", "migrate:v1", "up:v1", "verify:v1"], self.flow())
        self.assertEqual(["never"], FakeDocker.modes)
        self.assertEqual([1, 2], [ordinal for ordinal, _, _ in Rig.history])
        self.assertEqual([("default", 2, "migrate")], [(item["name"], item["level"], item["source"])
                                                        for item in self.known()])

    def test_a_placed_database_and_its_extensions_come_back_as_they_were(self):
        folder = self.root / "state" / "config" / "pilot-dev"
        folder.mkdir(parents=True, mode=0o700)
        (folder / "database.json").write_text(json.dumps({"ConnectionString": CANARY.replace("pilot_dev", "pilot_app")}))
        self.running(extensions=["pg_trgm", "uuid-ossp"])
        ResetDocker.owner, ResetDocker.roles = "pilot_app", ("pilot_app",)
        record = self.reset()
        self.assertEqual("succeeded", record["status"], record.get("reason"))
        self.assertEqual({"databases": ["pilot_app"]}, record["reset"])
        self.assertEqual(('pilot_app', 'CREATE EXTENSION IF NOT EXISTS "pg_trgm";\n'
                                       'CREATE EXTENSION IF NOT EXISTS "uuid-ossp";\n'), ResetDocker.psql_calls[-1])
        self.assertIn("extensions pg_trgm, uuid-ossp created as superuser", record["steps"][2]["detail"])

    def test_wheelhouse_and_its_databases_are_never_reset(self):
        for database in ("wheelhouse_prod", "wheelhouse_dev", "postgres"):
            with self.subTest(database=database):
                self.settings.write_text(json.dumps({"Database": {"Connection": "x"}, "DatabaseSettings": {
                    "ConnectionString": CANARY.replace("Database=pilot_dev", "Database=" + database)}}))
                record = self.reset("11111111-1111-4111-8111-1111111111{:02d}".format(len(database)))
                self.assertEqual("failed", record["status"])
                self.assertIn(database + " is never reset", record["reason"])
                self.assertFalse(any(event.startswith(("stop", "pg_dump", "drop")) for event in FakeDocker.events))

        self.running(product="wheelhouse")
        with self.assertRaisesRegex(ValueError, "Wheelhouse's own databases are never reset"):
            runner.control_plan(self.base, "reset", [])

    def test_a_reset_takes_the_whole_environment_of_a_release_that_migrates(self):
        with self.assertRaisesRegex(ValueError, "never one service"):
            runner.control_plan(self.base, "reset", ["api"])
        self.manifest.update(release="v1")
        self.release("v1", "1", [], migrate=False)
        current = runner.read_json(self.base / "current.json")["id"]
        for name in ("release.json", "compose.json"):
            (self.base / "releases" / current / name).write_bytes((self.bundle / name).read_bytes())
        with self.assertRaisesRegex(ValueError, "declares no migrate item, so no deploy would rebuild"):
            runner.control_plan(self.base, "reset", [])

    def test_a_database_elsewhere_is_refused_before_anything_stops(self):
        self.settings.write_text(json.dumps({"Database": {"Connection": "x"}, "DatabaseSettings": {
            "ConnectionString": CANARY.replace("Host=wheelhouse-postgres", "Host=pilot-db")}}))
        record = self.reset()
        self.assertEqual("failed", record["status"])
        self.assertIn("pilot_dev is on pilot-db, not the platform PostgreSQL", record["reason"])
        self.assertTrue(record["reason"].endswith("; nothing was dropped"))
        self.assertEqual([], [event for event in FakeDocker.events if event.startswith(("stop", "pg_dump"))])
        self.assertEqual("default", self.known()[0]["name"])
        self.assertEqual(2, self.known()[0]["level"])

    def test_a_database_another_role_owns_or_a_missing_owner_is_refused_before_anything_changes(self):
        for owner, roles, message in (("someone_else", ("pilot_dev",), "pilot_dev belongs to someone_else, not pilot_dev"),
                                      ("pilot_dev", (), "pilot_dev's owner pilot_dev is not a role")):
            with self.subTest(message=message):
                ResetDocker.owner, ResetDocker.roles = owner, roles
                FakeDocker.events = []
                record = self.reset("11111111-1111-4111-8111-1111111111" + str(len(roles)) + "1")
                self.assertEqual("failed", record["status"])
                self.assertIn(message, record["reason"])
                self.assertFalse(any(event.startswith(("pg_dump", "stop", "drop")) for event in FakeDocker.events))
                self.assertEqual(2, self.known()[0]["level"])

    def test_a_stop_cut_short_starts_the_environment_again_on_its_database(self):
        ResetDocker.fail_stop = True
        record = self.reset()
        self.assertEqual("failed", record["status"])
        self.assertIn("nothing was dropped, and the environment started again on its database", record["reason"])
        self.assertEqual(["stop:", "start:"], [event for event in FakeDocker.events if event.startswith(("stop", "start"))])
        self.assertEqual(2, self.known()[0]["level"])

    def test_a_failure_after_the_drop_leaves_the_environment_stopped_and_names_the_snapshot(self):
        self.running(extensions=["pg_trgm"])
        ResetDocker.fail_psql = "CREATE EXTENSION"
        record = self.reset()
        self.assertEqual("failed", record["status"])
        self.assertIn("Extensions in pilot_dev failed (exit 1)", record["reason"])
        self.assertIn("; the environment stays stopped; its data is in " + record["snapshot"]["path"], record["reason"])
        self.assertFalse(any(event.startswith("start") for event in FakeDocker.events))

    def test_a_stopped_environment_resets_and_its_mark_goes_so_the_next_deploy_starts_it(self):
        runner.write_stopped(self.base, {"environment": True, "services": []}, "max")
        record = self.reset()
        self.assertEqual("succeeded", record["status"], record.get("reason"))
        self.assertIsNone(runner.stopped_of(self.base))
        self.assertEqual("succeeded", self.apply(ResetDocker)["status"])

    def test_a_reset_launches_like_any_control(self):
        target_path = self.root / "target.json"
        runner.write_json(target_path, self.target)
        with patch.object(runner.subprocess, "Popen") as popen:
            queued = runner.control_launch(target_path, "reset", [], "max")
        self.assertEqual(["control", "--target", str(target_path.resolve()), "--operation", "reset", "--actor", "max",
                          "--job", queued["id"]], popen.call_args.args[0][2:])
        self.assertEqual({"kind": "reset", "services": []},
                         runner.read_json(self.base / "jobs" / (queued["id"] + ".json"))["operation"])


class TransportResetTests(unittest.TestCase):
    setUp = test_controls.TransportControlTests.setUp
    tearDown = test_controls.TransportControlTests.tearDown
    fake_ssh = test_controls.TransportControlTests.fake_ssh

    def test_a_reset_names_no_service_and_never_reaches_wheelhouse(self):
        with self.assertRaisesRegex(ValueError, "A reset takes the environment's database, never one service"):
            transport.control(self.root, "pilot-dev", "reset", "api", "max")
        fake, commands = self.fake_ssh()
        with patch.object(transport.fleet, "resolve_target", return_value={**self.config, "self": True}), \
                patch.object(transport, "Ssh", fake), \
                self.assertRaisesRegex(ValueError, "never stopped, restarted or reset from the console"):
            transport.control(self.root, "wheelhouse-dev", "reset", None, "max")
        self.assertEqual([], commands)
        with patch.object(transport.fleet, "resolve_target", return_value=self.config), \
                patch.object(transport, "Ssh", fake):
            submitted = transport.control(self.root, "pilot-dev", "reset", None, "max", reason="schema drifted")
        self.assertEqual({"kind": "reset", "services": []}, submitted["operation"])
        self.assertIn("--operation reset", commands[-1])
