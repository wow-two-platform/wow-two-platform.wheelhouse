"""The database copy on the service-control path (v0.15): another environment's database, stopped, into this one's
through the same platform PostgreSQL. Its tables counted, this database snapshotted, every service stopped, the source
dumped as its owner, this database dropped and created again and the dump restored as its owner, every table's rows
compared, the migration levels read as behind; the next deploy migrates and starts. The reset rig stands in for the
host: the platform PostgreSQL's container, pg_dump and the superuser's psql."""
import json
from pathlib import Path
import unittest
from unittest.mock import patch
import runner
import test_controls
import test_migrate
import test_reset
import transport
from test_migrate import Rig
from test_reset import CANARY, POSTGRES_ID, ResetDocker
from test_runner import FakeDocker

SOURCE = "Host=wheelhouse-postgres;Port=5432;Database=pilotold_dev;Username=pilotold_dev;Password=SOURCE_SECRET"
LISTED = ["; Archive created at 2026-11-12", "; Selected TOC Entries:", ";",
          "2; 3079 16385 EXTENSION - pg_trgm", "4012; 0 0 COMMENT - EXTENSION pg_trgm",
          "6; 2615 16400 SCHEMA - harvest pilotold_dev", "220; 1259 16401 TABLE harvest posts pilotold_dev",
          "3990; 0 16401 TABLE DATA harvest posts pilotold_dev"]


class CopyDocker(ResetDocker):
    """The reset rig's Docker, plus what a copy asks: the source project's running containers, each database's owner
    and tables, the source's extensions, pg_restore's list and the restore. `counts` holds each database's rows by table;
    a restore copies the source's into this one, with `restore_drift` added to one table."""
    running, counts, extensions_held, restores, restore_drift = "", {}, [], [], 0

    def execute(self, arguments, step, timeout=600):
        if arguments[:2] == ["docker", "ps"] and any(item.startswith("label=com.docker.compose.project=")
                                                     for item in arguments):
            self.events.append("ps " + next(item for item in arguments if item.startswith("label=")).split("=")[-1])
            return self.running
        return super().execute(arguments, step, timeout)

    def run(self, arguments, step, secrets=None, timeout=1800):
        folder = Path(arguments[arguments.index("-v") + 1].split(":", 1)[0])
        if "sh" in arguments:
            assert secrets is None and "pg_restore --list /snapshot/copy-pilotold_dev.dump" in arguments[-1]
            (folder / "copy-pilotold_dev.dump.list").write_text("\n".join(LISTED) + "\n")
            self.events.append("list")
            return
        if "pg_restore" in arguments:
            assert secrets == {"PGPASSWORD": "DO_NOT_LOG"} and not any("DO_NOT_LOG" in item for item in arguments)
            self.restores.append(arguments)
            self.events.append("restore " + arguments[arguments.index("--dbname") + 1])
            restored = dict(self.counts["pilotold_dev"])
            if self.restore_drift:
                restored["harvest.posts"] += self.restore_drift
            self.counts["pilot_dev"] = restored
            return
        database = arguments[arguments.index("--dbname") + 1]
        assert secrets == {"PGPASSWORD": "SOURCE_SECRET" if database == "pilotold_dev" else "DO_NOT_LOG"}
        assert not any("SECRET" in item or "DO_NOT_LOG" in item for item in arguments)
        (folder / arguments[-1].rsplit("/", 1)[1]).write_text("dump")
        self.dumps.append(arguments)
        self.events.append("pg_dump " + database)

    def psql(self, container, database, sql, step, timeout=300):
        assert container == POSTGRES_ID
        self.psql_calls.append((database, sql))
        if self.fail_psql and self.fail_psql in sql:
            raise runner.CommandFailed(step + " failed (exit 1); inspect the platform PostgreSQL privately")
        if sql.startswith("SELECT (SELECT count(*) FROM pg_roles"):
            role = sql.split("rolname = '", 1)[1].split("'", 1)[0]
            name = sql.split("datname = '", 1)[1].split("'", 1)[0]
            return [("1" if role in self.roles else "0") + "|" + {"pilot_dev": self.owner or "",
                                                                  "pilotold_dev": "pilotold_dev"}.get(name, "")]
        if sql == runner.TABLE_ROWS:
            self.events.append("count " + database)
            return [name + "|" + str(rows) for name, rows in sorted(self.counts.get(database, {}).items())]
        if sql.startswith("SELECT extname"):
            return list(self.extensions_held)
        if "DROP DATABASE" in sql:
            self.events.append("drop " + sql.split('"')[1])
            Rig.history = []
        return []


class CopyTests(unittest.TestCase):
    # The reset tests' rig, borrowed rather than inherited, so their tests do not run twice.
    setUp_rig = test_migrate.MigrateStepTests.setUp
    setUp_reset = test_reset.ResetTests.setUp
    tearDown = test_reset.ResetTests.tearDown
    base = test_reset.ResetTests.base
    release = test_reset.ResetTests.release
    apply = test_reset.ResetTests.apply
    flow = test_reset.ResetTests.flow
    known = test_reset.ResetTests.known

    def setUp(self):
        self.setUp_reset()
        CopyDocker.roles = ("pilot_dev", "pilotold_dev")
        CopyDocker.running, CopyDocker.extensions_held, CopyDocker.restores, CopyDocker.restore_drift = "", [], [], 0
        CopyDocker.counts = {"pilotold_dev": {"harvest.posts": 41, "harvest.sources": 2, "public.schema_versions": 6},
                             "pilot_dev": {"public.schema_versions": 2}}
        self.source = {**self.target, "product": "pilotold", "settings": {}}
        folder = self.root / "state" / "config" / "pilotold-dev"
        folder.mkdir(parents=True, mode=0o700)
        (folder / "database.json").write_text(json.dumps({"ConnectionString": SOURCE}))

    def copy(self, job="11111111-1111-4111-8111-111111111191", source=None):
        return runner.control(self.target, "copy", [], "max", job, docker_factory=CopyDocker,
                              source=self.source if source is None else source)

    def test_a_copy_restores_the_stopped_source_and_finds_every_table_as_it_was(self):
        CopyDocker.extensions_held = ["pg_trgm"]
        record = self.copy()
        self.assertEqual("succeeded", record["status"], record.get("reason"))
        self.assertEqual({"kind": "copy", "services": [], "from": "pilotold-dev"}, record["operation"])
        self.assertEqual(["Check the copy", "Snapshot the database", "Stop the environment", "Dump pilotold_dev",
                          "Copy into pilot_dev", "Compare row counts", "Migration levels to behind"],
                         [step["name"] for step in record["steps"]])
        # The source is checked and counted first; this database is kept before anything stops; the drop comes after
        # the source's dump, the restore after the drop.
        flow = [event for event in FakeDocker.events
                if event.split(" ")[0] in ("ps", "count", "pg_dump", "drop", "list", "restore") or ":" in event]
        self.assertEqual(["ps pilotold-dev", "count pilotold_dev", "pg_dump pilot_dev", "stop:", "pg_dump pilotold_dev",
                          "drop pilot_dev", "list", "restore pilot_dev", "count pilot_dev"], flow)
        self.assertIn(('pilot_dev', 'CREATE EXTENSION IF NOT EXISTS "pg_trgm";\n'), CopyDocker.psql_calls)
        restore = CopyDocker.restores[0]
        for flag in ("--no-owner", "--no-privileges", "--single-transaction", "--no-password"):
            self.assertIn(flag, restore)
        self.assertEqual(("pilot_dev", "/snapshot/copy-pilotold_dev.dump.restore"),
                         (restore[restore.index("--username") + 1], restore[restore.index("--use-list") + 1]))
        kept = (Path(record["snapshot"]["path"]) / "copy-pilotold_dev.dump.restore").read_text()
        self.assertNotIn("EXTENSION", kept)
        self.assertIn("TABLE DATA harvest posts", kept)
        self.assertEqual({"from": "pilotold-dev", "source": "pilotold_dev", "database": "pilot_dev", "tables": 3,
                          "rows": 49, "dump": record["snapshot"]["path"] + "/copy-pilotold_dev.dump"}, record["copy"])
        self.assertEqual("3 tables, 49 rows, each as in pilotold_dev", record["steps"][5]["detail"])
        self.assertIn("extensions pg_trgm created as superuser, 2 of the dump's entries left to them",
                      record["steps"][4]["detail"])
        self.assertEqual([("default", 0, "behind", "copy")],
                         [(item["name"], item["level"], item["state"], item["source"]) for item in self.known()])
        self.assertIsNone(record["stopped"])
        self.assertNotIn("health", record)
        self.assertNotIn("SECRET", json.dumps(record))
        self.assertNotIn("DO_NOT_LOG", json.dumps(record))
        # The next deploy of the same release migrates and starts the stopped containers without recreating them.
        FakeDocker.events, FakeDocker.modes = [], []
        result = self.apply(CopyDocker)
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertEqual(["pull", "migrate:v1", "up:v1", "verify:v1"], self.flow())

    def test_a_source_that_still_runs_is_refused_before_anything_changes(self):
        CopyDocker.running = "abc\ndef\n"
        record = self.copy()
        self.assertEqual("failed", record["status"])
        self.assertIn("pilotold-dev still runs 2 containers; stop it first, so nothing writes during the copy",
                      record["reason"])
        self.assertTrue(record["reason"].endswith("; nothing was dropped"))
        self.assertFalse(any(event.startswith(("stop", "pg_dump", "drop")) for event in FakeDocker.events))
        self.assertEqual(2, self.known()[0]["level"])

    def test_rows_that_differ_fail_the_copy_and_leave_the_environment_stopped_with_its_snapshot(self):
        CopyDocker.restore_drift = -1
        record = self.copy()
        self.assertEqual("failed", record["status"])
        self.assertIn("The copy's rows differ from pilotold_dev's in 1 table: harvest.posts", record["reason"])
        self.assertIn("; the environment stays stopped; its data is in " + record["snapshot"]["path"], record["reason"])
        self.assertFalse(any(event.startswith("start") for event in FakeDocker.events))

    def test_a_source_without_its_own_database_or_sharing_this_one_is_refused(self):
        (self.root / "state" / "config" / "pilotold-dev" / "database.json").unlink()
        record = self.copy()
        self.assertEqual("failed", record["status"])
        self.assertIn("pilotold-dev has no database.json", record["reason"])
        (self.root / "state" / "config" / "pilotold-dev" / "database.json").write_text(json.dumps(
            {"ConnectionString": CANARY}))
        record = self.copy("11111111-1111-4111-8111-111111111192")
        self.assertIn("pilotold-dev and pilot-dev share pilot_dev; there is nothing to copy", record["reason"])
        self.assertFalse(any(event.startswith(("stop", "pg_dump", "drop")) for event in FakeDocker.events))

    def test_only_a_copy_names_a_source_and_never_its_own_or_wheelhouse(self):
        with self.assertRaisesRegex(ValueError, "never one service's"):
            runner.control_plan(self.base, "copy", ["api"])
        with self.assertRaisesRegex(ValueError, "only a copy, names the environment it reads"):
            runner.copy_target("reset", "/tmp/source.json")
        with self.assertRaisesRegex(ValueError, "only a copy, names the environment it reads"):
            runner.copy_target("copy", None)
        record = self.copy(source=dict(self.target))
        self.assertIn("A copy reads another environment, never its own", record["reason"])
        source = self.root / "wheelhouse.json"
        source.write_text(json.dumps({**self.target, "product": "wheelhouse"}))
        with self.assertRaisesRegex(ValueError, "Wheelhouse's own databases are never copied"):
            runner.copy_target("copy", str(source))
        self.assertEqual("Copy the database from pilotold-dev",
                         runner.control_label("copy", [], source="pilotold-dev"))


class CopyTransportTests(unittest.TestCase):
    """A copy is submitted like any control, with the source's target file beside this one's; a source on another server
    or Wheelhouse's own is refused before anything is uploaded."""
    setUp = test_controls.TransportControlTests.setUp
    tearDown = test_controls.TransportControlTests.tearDown
    fake_ssh = test_controls.TransportControlTests.fake_ssh

    def resolve(self, server="pilot-host", own=False):
        configs = {"pilot-dev": self.config,
                   "pilotold-dev": {**self.config, "serverId": server, "self": own,
                                    "target": {**self.config["target"], "product": "pilotold"}}}
        return lambda root, target_id: configs[target_id]

    def test_a_copy_uploads_its_source_and_names_it_on_the_job(self):
        fake, commands = self.fake_ssh()
        with patch.object(transport.fleet, "resolve_target", side_effect=self.resolve()), \
                patch.object(transport, "Ssh", fake):
            submitted = transport.control(self.root, "pilot-dev", "copy", None, "max", source_id="pilotold-dev")
        self.assertEqual({"kind": "copy", "services": [], "from": "pilotold-dev"}, submitted["operation"])
        self.assertEqual("copy runner.py target.json source.json", commands[1])
        self.assertIn("--operation copy", commands[2])
        self.assertIn("/source.json", commands[2].split("--source ", 1)[1])

    def test_a_source_elsewhere_or_wheelhouse_is_refused_before_any_upload(self):
        for resolve, message in ((self.resolve(server="other-host"), "pilotold-dev is on another server"),
                                 (self.resolve(own=True), "never copied from the console")):
            fake, commands = self.fake_ssh()
            with self.subTest(message=message), \
                    patch.object(transport.fleet, "resolve_target", side_effect=resolve), \
                    patch.object(transport, "Ssh", fake), self.assertRaisesRegex(ValueError, message):
                transport.control(self.root, "pilot-dev", "copy", None, "max", source_id="pilotold-dev")
            self.assertEqual([], commands)
        with self.assertRaisesRegex(ValueError, "only a copy, names the environment it reads"):
            transport.control(self.root, "pilot-dev", "copy", None, "max")
        with self.assertRaisesRegex(ValueError, "never its own"):
            with patch.object(transport.fleet, "resolve_target", side_effect=self.resolve()):
                transport.control(self.root, "pilot-dev", "copy", None, "max", source_id="pilot-dev")


if __name__ == "__main__":
    unittest.main()
