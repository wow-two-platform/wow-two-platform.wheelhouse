"""Host upkeep on an enrolled products host, against a fake SSH host: a scratch /srv/wheelhouse, a fake `docker` that
logs each call and stands in for the kit's root phase, and the public entry point check's answer given by the test."""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import textwrap
import unittest
from unittest.mock import patch
import fleet
import infra_jobs
import kit
import upkeep
from runner import Rejected

KIT = Path(__file__).resolve().parents[2] / "deployment" / "vps"
PUBLIC = "203.0.113.20"

# Docker on the products host: the root phase reports the host's address (or fails), Compose succeeds, and
# `docker ps` is never reached because the test answers the public check itself.
FAKE_DOCKER = textwrap.dedent("""\
    #!/usr/bin/env python3
    import json, os, sys
    from pathlib import Path
    with open(Path(os.environ["HOST_ROOT"]) / "docker.log", "a") as log:
        log.write(" ".join(sys.argv[1:]) + "\\n")
    if sys.argv[1] == "run" and "nsenter" in sys.argv:
        if os.environ.get("ROOT_PHASE_FAILS"):
            print("This bootstrap is reviewed only for Ubuntu 26.04 amd64.", file=sys.stderr)
            sys.exit(1)
        print("Kit laid out; firewall: 80 and 443 public, SSH exposure kept.")
        print(json.dumps({"hostAddress": os.environ.get("HOST_ADDRESS", "")}))
    """)


class UpkeepTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        base = Path(self.temporary.name)
        self.root, self.host = base / "control", base / "host"
        self.server = fleet.Server("ocharo-vps", "Ocharo VPS", fleet.VpsProvider.CONTABO, "100.101.7.8", "eu",
                                   "wheelhouse-deploy")
        for patcher in (patch.dict(os.environ, {"WHEELHOUSE_REHEARSAL": ""}),
                        patch.object(fleet, "SERVERS", (self.server,)), patch.object(upkeep, "remote", self.remote)):
            patcher.start()
            self.addCleanup(patcher.stop)
        identity = self.root / "ssh" / "ocharo-vps"
        identity.mkdir(parents=True)
        for name in ("identity", "known_hosts"):
            (identity / name).write_text("x")
        # The products host as an enrollment before the public ingress left it.
        for folder in ("bootstrap", "platform", "config", "private", "ingress"):
            (self.host / folder).mkdir(parents=True, mode=0o700)
        self.host.chmod(0o700)
        shutil.copy(KIT / "prepare.py", self.host / "bootstrap" / "prepare.py")
        shutil.copy(KIT / "products.compose.yml", self.host / "platform" / "products.compose.yml")
        bin_dir = base / "bin"
        bin_dir.mkdir()
        (bin_dir / "docker").write_text(FAKE_DOCKER)
        (bin_dir / "docker").chmod(0o755)
        self.environment = {**os.environ, "PATH": str(bin_dir) + os.pathsep + os.environ["PATH"],
                            "HOST_ROOT": str(self.host), "HOST_ADDRESS": "10.0.0.5"}
        self.check = {"http": [308, "https://wheelhouse-upkeep.invalid/"], "https": [404, ""],
                      "published": [["wheelhouse-products-ingress-1", "127.0.0.1:18080"],
                                    ["wheelhouse-products-ingress-1", "0.0.0.0:80"],
                                    ["wheelhouse-products-ingress-1", "[::]:80"],
                                    ["wheelhouse-products-ingress-1", "0.0.0.0:443"],
                                    ["wheelhouse-products-ingress-1", "[::]:443"]]}
        self.commands = []

    def remote(self, ssh, command, stdin=None, timeout=30):
        self.assertEqual(("100.101.7.8", "wheelhouse-deploy"), (ssh.config["host"], ssh.config["user"]))
        self.commands.append(command)
        if command == "python3 -":
            self.assertEqual(upkeep.CHECK, stdin)
            return json.dumps(self.check) + "\n"
        result = subprocess.run(["bash", "-c", command.replace(upkeep.ROOT, str(self.host))],
                                input=stdin.encode() if isinstance(stdin, str) else stdin, capture_output=True,
                                env=self.environment)
        if result.returncode:
            raise infra_jobs.ssh_failure(result.returncode, result.stderr.decode())
        return result.stdout.decode()

    def enrolled(self, address=PUBLIC):
        folder = self.root / "infra" / "enrollments"
        folder.mkdir(parents=True, exist_ok=True)
        # An enrollment the console recorded long ago, before the public ingress existed.
        (folder / "ocharo-vps.json").write_text(json.dumps({
            "kind": "enrollment", "status": "succeeded", "recordedAt": "2026-10-19T08:00:00Z", "facts": {},
            "request": {"publicAddress": address}}))

    def upkeep(self):
        job = upkeep.start(self.root, {"serverId": "ocharo-vps"}, "sulton-max", launch=False)
        self.assertEqual(("queued", "upkeep"), (job["status"], job["kind"]))
        return upkeep.run(self.root, job["id"])

    def docker_calls(self):
        log = self.host / "docker.log"
        return log.read_text().splitlines() if log.exists() else []

    def test_an_enrolled_host_comes_to_this_kit_with_live_steps(self):
        self.enrolled()
        result = self.upkeep()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertEqual(["kit", "host", "platform", "public"], [step["key"] for step in result["steps"]])
        self.assertEqual(["succeeded"] * 4, [step["status"] for step in result["steps"]])
        # The kit went up as the deploy account; its root phase ran through Docker before PostgreSQL and Traefik.
        self.assertEqual(sorted(kit.FILES), sorted(path.name for path in (self.host / "bootstrap" / "kit").iterdir()))
        helper, platform = self.docker_calls()
        self.assertEqual("run --rm --privileged --pid=host --userns=host --network=none " + upkeep.HELPER
                         + " nsenter -t 1 -m -u -i -n -p -- bash " + str(self.host)
                         + "/bootstrap/kit/enroll-products.sh upkeep", helper)
        self.assertIn("up -d --wait postgres ingress", platform)
        self.assertTrue((self.host / "platform" / "compose.yml").is_file())
        self.assertTrue((self.host / "platform" / "acme").is_dir())
        facts = result["facts"]
        self.assertEqual((kit.digest(), PUBLIC, "enrollment", kit.PUBLIC_INGRESS, "10.0.0.5"),
                         (facts["kitDigest"], facts["publicAddress"], facts["addressSource"], facts["ingress"],
                          facts["hostAddress"]))
        self.assertIn("public address " + PUBLIC + " (enrollment)", result["steps"][3]["detail"])
        listed = upkeep.listing(self.root, "ocharo-vps")
        self.assertEqual((kit.digest(), [result["id"]]), (listed["kit"], [job["id"] for job in listed["jobs"]]))

    def test_the_address_on_the_server_wins_and_a_public_one_the_host_reports_comes_last(self):
        self.enrolled()
        recorded = fleet.Server("ocharo-vps", "Ocharo VPS", fleet.VpsProvider.CONTABO, "100.101.7.8", "eu",
                                "wheelhouse-deploy", ingress=fleet.Ingress(public_address="198.51.100.4"))
        with patch.object(fleet, "SERVERS", (recorded,)):
            self.assertEqual(("198.51.100.4", "server"), upkeep.public_address(self.root, "ocharo-vps", "8.8.4.4"))
        self.assertEqual((PUBLIC, "enrollment"), upkeep.public_address(self.root, "ocharo-vps", "8.8.4.4"))
        (self.root / "infra" / "enrollments" / "ocharo-vps.json").unlink()
        self.assertEqual(("8.8.4.4", "host"), upkeep.public_address(self.root, "ocharo-vps", "8.8.4.4"))
        # A private or tailnet address the host reports names no public host; only the rig takes a private one.
        for reported in ("10.0.0.5", "100.101.7.8", "127.0.0.1", None):
            self.assertEqual((None, None), upkeep.public_address(self.root, "ocharo-vps", reported))
        with patch.dict(os.environ, {"WHEELHOUSE_REHEARSAL": "1"}):
            self.assertEqual(("10.0.0.5", "host"), upkeep.public_address(self.root, "ocharo-vps", "10.0.0.5"))

    def test_without_a_public_address_the_job_fails_at_its_check_and_records_no_ingress(self):
        result = self.upkeep()
        self.assertEqual("failed", result["status"])
        self.assertEqual(["succeeded", "succeeded", "succeeded", "failed"], [step["status"] for step in result["steps"]])
        self.assertIn("No public address is recorded", result["reason"])
        self.assertNotIn("ingress", result["facts"])

    def test_a_failed_root_phase_stops_before_the_platform(self):
        self.enrolled()
        self.environment["ROOT_PHASE_FAILS"] = "1"
        result = self.upkeep()
        self.assertEqual("failed", result["status"])
        self.assertEqual("host", next(step["key"] for step in result["steps"] if step["status"] == "failed"))
        self.assertIn("reviewed only for Ubuntu 26.04", result["reason"])
        self.assertEqual(1, len(self.docker_calls()))

    def test_port_80_must_redirect_to_https_and_443_must_answer_tls(self):
        self.enrolled()
        for answer, message in (({"http": [404, ""]}, "Port 80 does not redirect to https (404)"),
                                ({"http": [0, "ConnectionRefusedError"]}, "Port 80 does not redirect"),
                                ({"https": [0, "SSLError"]}, "Port 443 does not answer TLS")):
            with self.subTest(answer=answer):
                self.check.update(answer)
                result = self.upkeep()
                self.assertEqual("failed", result["status"])
                self.assertIn(message, result["reason"])
                self.check.update(http=[308, "https://wheelhouse-upkeep.invalid/"], https=[404, ""])

    def test_a_container_published_beyond_loopback_fails_the_check(self):
        self.enrolled()
        self.check["published"] += [["ocharo-platform-prod-desk-1", "0.0.0.0:8080"],
                                    ["wheelhouse-products-ingress-1", "0.0.0.0:8081"],
                                    ["ocharo-platform-dev-desk-1", "127.0.0.1:15432"]]
        result = self.upkeep()
        self.assertEqual("failed", result["status"])
        self.assertEqual("Published beyond loopback: ocharo-platform-prod-desk-1 0.0.0.0:8080, "
                         "wheelhouse-products-ingress-1 0.0.0.0:8081", result["reason"])

    def test_a_finished_upkeep_reaches_the_follower_once(self):
        self.enrolled()
        result = self.upkeep()
        finished = infra_jobs.finished(self.root)
        self.assertEqual([(result["id"], "upkeep", "succeeded", PUBLIC)],
                         [(item["id"], item["kind"], item["status"], item["facts"]["publicAddress"])
                          for item in finished])
        infra_jobs.acknowledge(self.root, result["id"])
        self.assertEqual([], infra_jobs.finished(self.root))
        with self.assertRaisesRegex(Rejected, "already run"):
            upkeep.run(self.root, result["id"])

    def test_requests_are_checked_first(self):
        local = fleet.Server("local", "Local server", fleet.VpsProvider.LOCAL, "127.0.0.1", "local")
        with patch.object(fleet, "SERVERS", (self.server, local)):
            with self.assertRaisesRegex(Rejected, "rig's own layout"):
                upkeep.start(self.root, {"serverId": "local"}, "sulton-max", launch=False)
            with self.assertRaisesRegex(Rejected, "not in the inventory"):
                upkeep.start(self.root, {"serverId": "missing"}, "sulton-max", launch=False)
            with self.assertRaisesRegex(Rejected, "Invalid actor"):
                upkeep.start(self.root, {"serverId": "ocharo-vps"}, "a\nb", launch=False)
        with infra_jobs.server_lock(self.root, "ocharo-vps"):
            with self.assertRaisesRegex(Rejected, "Another job is running"):
                upkeep.start(self.root, {"serverId": "ocharo-vps"}, "sulton-max", launch=False)

    def test_only_the_upkeep_phase_runs_as_root(self):
        with self.assertRaisesRegex(Rejected, "Unknown root phase"):
            upkeep.root_phase(None, "seal")
        self.assertEqual("203.0.113.9", upkeep.host_address('Kit laid out.\n{"hostAddress": "203.0.113.9"}\n'))
        for output in ('{"hostAddress": ""}', "no json", '{"hostAddress": "2001:db8::1"}', ""):
            self.assertIsNone(upkeep.host_address(output))


class ImageTests(unittest.TestCase):
    def test_the_console_image_carries_every_module_the_infrastructure_cli_reaches(self):
        here = Path(__file__).resolve().parent
        dockerfile = (here.parents[1] / "deployment" / "Dockerfile").read_text()
        copied = set(re.findall(r"wheelhouse\.runner-services/([a-z_]+)\.py", dockerfile))
        reached, pending = set(), ["infra", "transport"]
        while pending:
            module = pending.pop()
            reached.add(module)
            source = (here / (module + ".py")).read_text()
            for name in re.findall(r"^(?:import|from) ([a-z_]+)", source, re.M):
                if (here / (name + ".py")).is_file() and name not in reached:
                    pending.append(name)
        self.assertEqual(set(), reached - copied)


if __name__ == "__main__":
    unittest.main()
