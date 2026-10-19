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
import placement
import tailnet
from runner import Rejected

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "deployment" / "rehearsal"))
import fake_tailscale  # noqa: E402

KIT = Path(__file__).resolve().parents[2] / "deployment" / "vps"
CLIENT = {"clientId": "kWheelhouseCNTRL", "clientSecret": "tskey-client-kWheelhouseCNTRL-secretvalue"}

# Stands in for Docker on the products host: logs every call; `compose up` of a site node reads that node's env file
# and joins the fake tailnet with its key, as the tailscale container would at first login.
FAKE_DOCKER = textwrap.dedent("""\
    #!/usr/bin/env python3
    import json, os, sys, urllib.request
    from pathlib import Path
    root = Path(os.environ["HOST_ROOT"])
    with open(root / "docker.log", "a") as log:
        log.write(" ".join(sys.argv[1:]) + "\\n")
    if "exec" in sys.argv:
        sys.stdin.read()
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
                        patch.object(placement, "remote", self.remote)):
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
                            "HOST_ROOT": str(self.host), "FAKE_TAILSCALE": api}
        self.stdins = []

    def remote(self, ssh, command, stdin=None, timeout=30):
        self.assertEqual(("100.101.7.8", "wheelhouse-deploy"), (ssh.config["host"], ssh.config["user"]))
        self.stdins.append(stdin)
        result = subprocess.run(["bash", "-c", command.replace(placement.ROOT, str(self.host))], input=stdin,
                                capture_output=True, text=True, env=self.environment)
        if result.returncode:
            raise infra_jobs.ssh_failure(result.returncode, result.stderr)
        return result.stdout

    def place(self, database=True, sites=(("app", "ocharo-desk"),)):
        job = placement.start(self.root, {"serverId": "ocharo-products", "product": "ocharo-platform",
                                          "database": database,
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
        self.assertIn("up -d --wait postgres ingress", self.docker_calls()[0])

    def test_a_rerun_keeps_databases_and_joined_nodes_and_mints_nothing(self):
        self.place()
        minted = len(self.fake.keys)
        result = self.place()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertIn("Kept ocharo_platform_dev and ocharo_platform_prod", result["steps"][1]["detail"])
        self.assertIn("Every node had already joined", result["steps"][3]["detail"])
        self.assertEqual(minted, len(self.fake.keys))

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
        carried = [stdin for stdin in self.stdins if stdin]
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
                       **changes}
            with self.subTest(changes=changes), self.assertRaisesRegex(Rejected, message):
                placement.start(self.root, request, "sulton-max", launch=False)

    def test_sites_need_the_tailscale_client(self):
        (self.root / tailnet.CLIENT_FILE).unlink()
        with self.assertRaisesRegex(Rejected, "Connect Tailscale first"):
            placement.start(self.root, {"serverId": "ocharo-products", "product": "ocharo-platform",
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
