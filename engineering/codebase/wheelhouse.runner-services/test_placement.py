"""Placing a product's databases and private sites on a products host, against a fake SSH host: a scratch
/srv/wheelhouse laid out as bootstrap-products.sh lays it out, the kit's real prepare.py, and a fake `docker` whose
site nodes spend their single-use keys at the rig's fake Tailscale API."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest
from unittest.mock import patch
import catalog
import fleet
import infra_jobs
import kit
import placement
import tailnet
import upkeep
from runner import Rejected

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "deployment" / "rehearsal"))
import fake_tailscale  # noqa: E402

KIT = Path(__file__).resolve().parents[2] / "deployment" / "vps"
CLIENT = {"clientId": "kWheelhouseCNTRL", "clientSecret": "tskey-client-kWheelhouseCNTRL-secretvalue"}
# What the console read from a deploy.yml whose `app` site is private and whose services need PostgreSQL.
DECLARED = {"sites": {"app": "private", "www": "public"}, "postgres": True}

# Stands in for Docker on the products host: logs every call; `compose up` of a site node reads that node's env file
# and joins the fake tailnet with its key, as the tailscale container would at first login.
FAKE_DOCKER = textwrap.dedent("""\
    #!/usr/bin/env python3
    import json, os, sys, urllib.request
    from pathlib import Path
    root = Path(os.environ["HOST_ROOT"])
    with open(root / "docker.log", "a") as log:
        log.write(" ".join(sys.argv[1:]) + "\\n")
    if sys.argv[1:3] == ["volume", "inspect"] and sys.argv[-1] in os.environ.get("VOLUMES", "").split():
        print(sys.argv[-1])
    if sys.argv[1] == "run" and "nsenter" in sys.argv:
        # The kit's root phase through the deploy account's Docker access: here it only reports the host's address.
        print("Kit laid out; firewall: 80 and 443 public, SSH exposure kept.")
        print(json.dumps({"hostAddress": os.environ.get("HOST_ADDRESS", "")}))
        sys.exit(0)
    if "exec" in sys.argv:
        sql = sys.stdin.read()
        if "EXTENSION" in sql:
            with open(root / "extensions.log", "a") as log:
                log.write(sys.argv[sys.argv.index("-d") + 1] + " " + sql.replace("\\n", " ").strip() + "\\n")
        sys.exit(int(os.environ.get("DATABASE_FAILS", "0")))
    if "up" in sys.argv and "--wait" not in sys.argv:
        for node in sys.argv[sys.argv.index("-d") + 1:]:
            env = root / "private" / "sites" / (node + ".env")
            state = root / "platform" / "sites" / node / "tailscaled.state"
            if state.exists():
                continue
            key = env.read_text().strip().split("=", 1)[1]
            body = json.dumps({"authKey": key, "hostname": node, "tags": ["tag:product-site"]}).encode()
            request = urllib.request.Request(os.environ["FAKE_TAILSCALE"] + "/rig/join", data=body, method="POST")
            urllib.request.urlopen(request).read()
            state.write_text("joined")
    """)


class PlacementTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        base = Path(self.temporary.name)
        self.root, self.host = base / "control", base / "host"
        self.fake = fake_tailscale.Tailnet(CLIENT["clientId"], CLIENT["clientSecret"], "tail1234.ts.net")
        self.fake.devices.append({"id": "1", "nodeId": "nvps", "name": "ocharo-vps.tail1234.ts.net",
                                  "hostname": "ocharo-vps", "addresses": ["100.101.7.8"], "tags": ["tag:products"]})
        server = fake_tailscale.serve(self.fake)
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        api = "http://127.0.0.1:" + str(server.server_address[1])
        products = (catalog.Product("ocharo-platform", "Ocharo Platform", "", "ocharo-hq/ocharo-platform"),
                    catalog.Product("ocharo-studio", "Ocharo Studio", "", "ocharo-hq/ocharo-studio"))
        servers = (fleet.Server("ocharo-products", "Ocharo products", fleet.VpsProvider.CONTABO, "100.101.7.8", "eu",
                                "wheelhouse-deploy"),)
        for patcher in (patch.dict(os.environ, {"WHEELHOUSE_REHEARSAL": "1", "WHEELHOUSE_TAILSCALE_API": api}),
                        patch.object(catalog, "PRODUCTS", products), patch.object(fleet, "SERVERS", servers),
                        patch.object(placement, "NODE_SECONDS", 0),
                        patch.object(placement.time, "sleep", lambda seconds: None),
                        patch.object(placement, "remote", self.remote), patch.object(upkeep, "remote", self.remote)):
            patcher.start()
            self.addCleanup(patcher.stop)
        identity = self.root / "ssh" / "ocharo-products"
        identity.mkdir(parents=True)
        for name in ("identity", "known_hosts"):
            (identity / name).write_text("x")
        client = self.root / tailnet.CLIENT_FILE
        client.parent.mkdir(parents=True)
        client.write_text(json.dumps(CLIENT))
        client.chmod(0o600)
        # The products host as bootstrap-products.sh leaves it.
        for folder in ("bootstrap", "platform", "config", "private", "ingress"):
            (self.host / folder).mkdir(parents=True, mode=0o700)
        self.host.chmod(0o700)
        shutil.copy(KIT / "prepare.py", self.host / "bootstrap" / "prepare.py")
        shutil.copy(KIT / "products.compose.yml", self.host / "platform" / "products.compose.yml")
        shutil.copy(KIT / "tailscale-serve-products.json", self.host / "platform" / "tailscale-serve.json")
        bin_dir = base / "bin"
        bin_dir.mkdir()
        (bin_dir / "docker").write_text(FAKE_DOCKER)
        (bin_dir / "docker").chmod(0o755)
        self.environment = {**os.environ, "PATH": str(bin_dir) + os.pathsep + os.environ["PATH"],
                            "HOST_ROOT": str(self.host), "FAKE_TAILSCALE": api, "HOST_ADDRESS": "203.0.113.10"}
        self.stdins = []

    def remote(self, ssh, command, stdin=None, timeout=30):
        self.assertEqual(("100.101.7.8", "wheelhouse-deploy"), (ssh.config["host"], ssh.config["user"]))
        self.stdins.append(stdin)
        result = subprocess.run(["bash", "-c", command.replace(placement.ROOT, str(self.host))],
                                input=stdin.encode() if isinstance(stdin, str) else stdin, capture_output=True,
                                env=self.environment)
        if result.returncode:
            raise infra_jobs.ssh_failure(result.returncode, result.stderr.decode())
        return result.stdout.decode()

    def place(self, database=True, sites=(("app", "ocharo-desk"),)):
        job = placement.start(self.root, {"serverId": "ocharo-products", "product": "ocharo-platform",
                                          "database": database, "declared": DECLARED,
                                          "sites": [{"site": site, "node": node} for site, node in sites]},
                              "sulton-max", launch=False)
        return placement.run(self.root, job["id"])

    def docker_calls(self):
        log = self.host / "docker.log"
        return log.read_text().splitlines() if log.exists() else []

    def test_databases_and_sites_land_on_the_host_with_single_use_keys(self):
        result = self.place()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertEqual(["succeeded"] * 4, [step["status"] for step in result["steps"]])
        for environment in ("dev", "prod"):
            credentials = self.host / "config" / ("ocharo-platform-" + environment) / "database.json"
            self.assertEqual(0o600, credentials.stat().st_mode & 0o777)
        self.assertEqual(["ocharo_platform_dev", "ocharo_platform_prod"], result["facts"]["databases"])
        sites = {site["node"]: site for site in result["facts"]["sites"]}
        self.assertEqual({"ocharo-desk-dev", "ocharo-desk"}, set(sites))
        self.assertEqual("https://ocharo-desk-dev.tail1234.ts.net", sites["ocharo-desk-dev"]["url"])
        self.assertTrue(all(site["joined"] and site["tailnetAddress"] for site in sites.values()))
        # One single-use tag:product-site key per node, each spent by its own node; only IDs are recorded.
        keys = list(self.fake.keys.values())
        self.assertEqual([(["tag:product-site"], True, False)] * 2,
                         [(key["tags"], key["used"], key["reusable"]) for key in keys])
        self.assertEqual({"ocharo-desk-dev", "ocharo-desk"}, {mint["node"] for mint in result["mints"]})
        record = (self.root / "infra" / "placements" / (result["id"] + ".json")).read_text()
        self.assertNotIn("tskey-auth", record)
        self.assertEqual([], list((self.host / "private" / "sites").iterdir()))
        compose = (self.host / "platform" / "compose.yml").read_text()
        self.assertIn("../private/sites/ocharo-desk.env", compose)
        self.assertEqual("volume inspect --format {{.Name}} wheelhouse-products_postgres", self.docker_calls()[1])
        self.assertIn("up -d --wait postgres ingress", self.docker_calls()[2])

    def test_the_platform_step_brings_the_host_to_this_kit_first(self):
        result = self.place(database=False)
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        # The kit went up as the deploy account, then its root phase ran through Docker, before PostgreSQL and Traefik.
        self.assertEqual(sorted(kit.FILES), sorted(path.name for path in (self.host / "bootstrap" / "kit").iterdir()))
        helper, guard, platform = self.docker_calls()[:3]
        self.assertEqual("volume inspect --format {{.Name}} wheelhouse-products_postgres", guard)
        # The rig's switch (set for these tests) lets the kit accept an arm64 host; a real host runs without it.
        self.assertTrue(helper.startswith("run --rm --privileged --pid=host --userns=host --network=none "
                                          "-e WHEELHOUSE_REHEARSAL=1 " + upkeep.HELPER
                                          + " nsenter -t 1 -m -u -i -n -p -- bash "), helper)
        self.assertTrue(helper.endswith("/bootstrap/kit/enroll-products.sh upkeep"), helper)
        self.assertIn("up -d --wait postgres ingress", platform)
        facts = result["facts"]
        self.assertEqual((kit.digest(), "203.0.113.10", "host", "websecure"),
                         (facts["kitDigest"], facts["publicAddress"], facts["addressSource"],
                          facts["ingress"]["entryPoints"][0]))
        self.assertIn("Kit " + kit.digest()[:12] + " laid out", result["steps"][0]["detail"])

    def test_a_host_still_on_postgresql_16_places_nothing_until_its_move(self):
        # The platform step converges to the kit's PostgreSQL 18 (postgres-pg18); with 16 data waiting beside it and no
        # move recorded, prepare.py refuses before compose.yml is rendered, so no database or node is created.
        self.environment["VOLUMES"] = "wheelhouse-products_postgres"
        result = self.place()
        self.assertEqual("failed", result["status"])
        self.assertEqual(["failed", "pending", "pending", "pending"], [step["status"] for step in result["steps"]])
        self.assertIn("PostgreSQL 16 data waits in wheelhouse-products_postgres", result["reason"])
        self.assertFalse((self.host / "config" / "ocharo-platform-dev").exists())
        self.assertEqual(0, len(self.fake.keys))

    def test_a_rerun_keeps_databases_and_joined_nodes_and_mints_nothing(self):
        self.place()
        minted = len(self.fake.keys)
        result = self.place()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertIn("Kept ocharo_platform_dev and ocharo_platform_prod", result["steps"][1]["detail"])
        self.assertIn("Every node had already joined", result["steps"][3]["detail"])
        self.assertEqual(minted, len(self.fake.keys))

    def test_declared_extensions_are_created_as_superuser_and_a_rerun_brings_new_ones(self):
        declared = {**DECLARED, "extensions": ["postgres_fdw"]}
        job = placement.start(self.root, {"serverId": "ocharo-products", "product": "ocharo-platform", "database": True,
                                          "declared": declared, "sites": []}, "sulton-max", launch=False)
        result = placement.run(self.root, job["id"])
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertEqual(["postgres_fdw"], result["facts"]["extensions"])
        self.assertTrue(result["steps"][1]["detail"].endswith("; extensions postgres_fdw created as superuser in both"))
        log = (self.host / "extensions.log").read_text().splitlines()
        self.assertEqual(['ocharo_platform_dev CREATE EXTENSION IF NOT EXISTS "postgres_fdw";',
                          'ocharo_platform_prod CREATE EXTENSION IF NOT EXISTS "postgres_fdw";'], log)
        # deploy.yml gained one: a rerun keeps the databases and creates what is missing.
        declared["extensions"] = ["postgres_fdw", "pg_partman"]
        job = placement.start(self.root, {"serverId": "ocharo-products", "product": "ocharo-platform", "database": True,
                                          "declared": declared, "sites": []}, "sulton-max", launch=False)
        rerun = placement.run(self.root, job["id"])
        self.assertEqual("succeeded", rerun["status"], rerun.get("reason"))
        self.assertIn("Kept ocharo_platform_dev and ocharo_platform_prod", rerun["steps"][1]["detail"])
        self.assertEqual(4, len((self.host / "extensions.log").read_text().splitlines()))
        with self.assertRaisesRegex(Rejected, "Invalid declared extensions"):
            placement.start(self.root, {"serverId": "ocharo-products", "product": "ocharo-platform", "database": True,
                                        "declared": {**DECLARED, "extensions": ["Bad Name"]}, "sites": []},
                            "sulton-max", launch=False)

    def test_the_cli_passes_the_declared_extensions_along(self):
        import contextlib
        import io
        import infra
        seen = {}
        argv = ["infra.py", "place", "--root", str(self.root), "--server", "ocharo-products", "--product",
                "ocharo-platform", "--database", "--needs-postgres", "--declared-extension", "postgres_fdw",
                "--declared-extension", "pg_trgm", "--no-launch"]
        with patch.object(infra.inventory, "install"), patch("sys.argv", argv), \
                patch.object(infra.placement, "start", side_effect=lambda root, request, actor, launch: seen.update(
                    request) or {"id": "x"}), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, infra.main())
        self.assertEqual({"sites": {}, "postgres": True, "extensions": ["postgres_fdw", "pg_trgm"]}, seen["declared"])

    def test_a_half_created_database_pair_is_refused_for_review(self):
        folder = self.host / "config" / "ocharo-platform-dev"
        folder.mkdir(mode=0o700)
        (folder / "database.json").write_text("{}")
        result = self.place(sites=())
        self.assertEqual("failed", result["status"])
        self.assertIn("Only dev's database exists", result["reason"])

    def test_a_refusal_on_the_host_reaches_the_owner(self):
        self.place(database=False)
        studio = placement.start(self.root, {"serverId": "ocharo-products", "product": "ocharo-studio",
                                             "declared": {"sites": {"app": "private"}},
                                             "sites": [{"site": "app", "node": "ocharo-desk"}]}, "sulton-max",
                                 launch=False)
        result = placement.run(self.root, studio["id"])
        self.assertEqual("failed", result["status"])
        self.assertEqual("Products configuration refused: Tailnet node ocharo-desk-dev already serves another site. "
                         "(exit 1)", result["reason"])

    def test_a_node_that_never_joins_fails_the_job_and_its_key_file_still_goes(self):
        self.environment["FAKE_TAILSCALE"] = "http://127.0.0.1:9"
        result = self.place(database=False)
        self.assertEqual("failed", result["status"])
        self.assertEqual("nodes", next(step["key"] for step in result["steps"] if step["status"] == "failed"))
        self.assertEqual([], list((self.host / "private" / "sites").iterdir()))

    def test_a_site_key_travels_on_stdin_only(self):
        self.place(database=False)
        carried = [stdin for stdin in self.stdins if stdin and isinstance(stdin, str)]
        self.assertEqual(2, len(carried))
        self.assertTrue(all(stdin.startswith("TS_AUTHKEY=tskey-auth-") for stdin in carried))
        self.assertFalse(any("tskey" in line for line in self.docker_calls()))

    def test_requests_are_checked_first(self):
        cases = ((dict(product="unknown"), "Product is not in the inventory"),
                 (dict(sites=[{"site": "App", "node": "x1"}]), "site's name"),
                 (dict(sites=[{"site": "app", "node": "ingress"}]), "node name"),
                 (dict(database=False, sites=[]), "databases, sites or both"),
                 (dict(serverId="missing"), "Server is not in the inventory"))
        for changes, message in cases:
            request = {"serverId": "ocharo-products", "product": "ocharo-platform", "database": True, "sites": [],
                       "declared": DECLARED, **changes}
            with self.subTest(changes=changes), self.assertRaisesRegex(Rejected, message):
                placement.start(self.root, request, "sulton-max", launch=False)

    def test_a_placement_asks_only_for_what_deploy_yml_calls_for(self):
        app = [{"site": "app", "node": "ocharo-platform-app"}]
        cases = ((dict(declared=None), "follows the product's deploy.yml"),
                 (dict(declared={"sites": {"app": "secret"}}), "Invalid declared sites"),
                 (dict(sites=[{"site": "www", "node": "ocharo-platform-www"}]), "www is public"),
                 (dict(sites=[{"site": "admin", "node": "ocharo-platform-admin"}]), "declares no site admin"),
                 (dict(declared={**DECLARED, "postgres": False}), "needs PostgreSQL"),
                 (dict(declared={"sites": {"app": "private"}}), "needs PostgreSQL"))
        for changes, message in cases:
            request = {"serverId": "ocharo-products", "product": "ocharo-platform", "database": True, "sites": app,
                       "declared": DECLARED, **changes}
            with self.subTest(changes=changes), self.assertRaisesRegex(Rejected, message):
                placement.start(self.root, request, "sulton-max", launch=False)
        # A private site alone, for a product that needs no database; the journal keeps what deploy.yml declared.
        job = placement.start(self.root, {"serverId": "ocharo-products", "product": "ocharo-studio", "database": False,
                                          "sites": app, "declared": {"sites": {"app": "private"}}},
                              "sulton-max", launch=False)
        self.assertEqual({"sites": {"app": "private"}, "postgres": False}, job["request"]["declared"])

    def test_sites_need_the_tailscale_client(self):
        (self.root / tailnet.CLIENT_FILE).unlink()
        with self.assertRaisesRegex(Rejected, "Connect Tailscale first"):
            placement.start(self.root, {"serverId": "ocharo-products", "product": "ocharo-platform", "declared": DECLARED,
                                        "sites": [{"site": "app", "node": "ocharo-desk"}]}, "sulton-max", launch=False)

    def test_listing_and_the_follower_s_finished_jobs(self):
        result = self.place(database=True, sites=())
        listed = placement.listing(self.root, product="ocharo-platform")
        self.assertEqual([result["id"]], [item["id"] for item in listed])
        finished = infra_jobs.finished(self.root)
        self.assertEqual([(result["id"], "placement", "succeeded")],
                         [(item["id"], item["kind"], item["status"]) for item in finished])
        infra_jobs.acknowledge(self.root, result["id"])
        self.assertEqual([], infra_jobs.finished(self.root))


if __name__ == "__main__":
    unittest.main()
