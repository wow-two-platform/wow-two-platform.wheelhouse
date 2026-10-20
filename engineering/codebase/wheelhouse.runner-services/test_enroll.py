"""Enrolling a fresh server end to end against a fake SSH host and the rig's fake Tailscale API: the one-time key, the
first-seen host key, the single-use tailnet key, the second path that proves the host, and the seal."""
import io
import json
import os
from pathlib import Path
import shlex
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import enroll
import fleet
import infra_jobs
import kit
import placement
import tailnet
from runner import CommandFailed, Rejected

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "deployment" / "rehearsal"))
import fake_tailscale  # noqa: E402

CLIENT = {"clientId": "kWheelhouseCNTRL", "clientSecret": "tskey-client-kWheelhouseCNTRL-secretvalue"}
PUBLIC = "203.0.113.20"
TAILNET_ADDRESS = "100.101.7.8"
OWNER_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOwnerKeyOwnerKeyOwnerKeyOwnerKeyOwnerKey owner@laptop"


def host_key():
    with tempfile.TemporaryDirectory() as temporary:
        subprocess.run(["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", temporary + "/key"], check=True)
        return " ".join(Path(temporary + "/key.pub").read_text().split()[:2])


class FakeHost:
    """A fresh Ubuntu 26.04 VPS as the enrollment sees it over SSH: root trusts the one-time key, the kit's phases
    record what they were asked, and the tailnet join spends the minted key at the fake Tailscale API.

    `public_key` is the host key the public path presents (an attacker's, in a relayed first contact); `real_key`
    is the one the tailnet path presents. `endpoint` is where the joined node says it lives."""

    def __init__(self, test):
        self.test = test
        self.real_key = host_key()
        self.public_key = self.real_key
        self.endpoint = PUBLIC + ":41641"
        self.os = "ubuntu 26.04 amd64 0"
        self.phases, self.uploaded, self.runner_keys = [], [], []
        self.sealed = False
        self.joined_keys = []
        self.fail_phase = None

    def public(self, session, command, stdin=b"", timeout=120):
        request = session.request
        self.test.assertEqual((PUBLIC, "root"), (request["publicAddress"], request["loginUser"]))
        if self.sealed:
            raise CommandFailed("SSH connection timed out")
        pinned = session.known_hosts.read_text().split() if session.known_hosts.is_file() else []
        if not pinned:
            self.test.assertTrue(session.first_contact, "only the first contact may accept a host key")
            session.known_hosts.write_text(PUBLIC + " " + self.public_key + "\n")
        elif " ".join(pinned[1:3]) != self.public_key:
            raise CommandFailed("The host presented another SSH host key than the one pinned")
        authorized = Path(str(session.identity) + ".pub").read_text().split()[1]
        self.test.assertIn(authorized, self.root_keys)
        if "/etc/os-release" in command:
            return self.os + "\n"
        if "tar -xzf" in command:
            with tarfile.open(fileobj=io.BytesIO(stdin), mode="r:gz") as package:
                self.uploaded = package.getnames()
            return ""
        words = shlex.split(command.split("enroll-products.sh\" ", 1)[1])
        self.phases.append(words)
        if words[0] == self.fail_phase:
            raise CommandFailed("Review conflicting installed package manually: docker.io (exit 1)")
        if words[0] == "admin":
            self.test.assertEqual(OWNER_KEY + "\n", stdin.decode())
            return "Administrator ops is ready.\n  passwd ops\n"
        if words[0] == "join":
            key = stdin.decode().strip()
            self.joined_keys.append(key)
            status, _ = self.test.fake.join({"authKey": key, "hostname": words[1], "tags": ["tag:products"],
                                             "address": TAILNET_ADDRESS, "endpoints": [self.endpoint]})
            if status != 200:
                raise CommandFailed("tailscale up failed; the key may have expired. (exit 1)")
            return json.dumps({"address": TAILNET_ADDRESS, "hostKey": self.real_key}) + "\n"
        if words[0] == "accept-runner":
            self.runner_keys.append(stdin.decode().split()[1])
        if words[0] == "seal":
            self.test.assertEqual(authorized, stdin.decode().split()[1])
            self.sealed = True
        return ""

    def tailnet(self, ssh, command, stdin=None, timeout=30):
        config = ssh.config
        self.test.assertEqual((TAILNET_ADDRESS, "wheelhouse-deploy", 22), (config["host"], config["user"], config["port"]))
        pin = Path(config["knownHostsFile"]).read_text().split()
        if pin != [TAILNET_ADDRESS, *self.real_key.split()]:
            raise CommandFailed("The host presented another SSH host key than the one pinned")
        if Path(config["keyFile"] + ".pub").read_text().split()[1] not in self.runner_keys:
            raise CommandFailed("SSH refused the key")
        return ""

    @property
    def root_keys(self):
        return self.test.root_keys


class EnrollmentTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.fake = fake_tailscale.Tailnet(CLIENT["clientId"], CLIENT["clientSecret"], "tail1234.ts.net")
        server = fake_tailscale.serve(self.fake)
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        for patcher in (patch.dict(os.environ, {"WHEELHOUSE_REHEARSAL": "1", "WHEELHOUSE_TAILSCALE_API":
                                                "http://127.0.0.1:" + str(server.server_address[1])}),
                        patch.object(fleet, "SERVERS", (fleet.Server("ocharo-products", "Ocharo products",
                                                                     fleet.VpsProvider.CONTABO, PUBLIC, "eu",
                                                                     "wheelhouse-deploy"),)),
                        patch.object(enroll, "NODE_SECONDS", 0),
                        patch.object(enroll.time, "sleep", lambda seconds: None)):
            patcher.start()
            self.addCleanup(patcher.stop)
        client = self.root / tailnet.CLIENT_FILE
        client.parent.mkdir(parents=True)
        client.write_text(json.dumps(CLIENT))
        client.chmod(0o600)
        self.host = FakeHost(self)
        self.converged = []
        for patcher in (patch.object(enroll.EnrollSsh, "run", lambda session, command, stdin=b"", timeout=120:
                                     self.host.public(session, command, stdin, timeout)),
                        patch.object(enroll.Ssh, "run", lambda ssh, command, stdin=None, timeout=30:
                                     self.host.tailnet(ssh, command, stdin, timeout)),
                        patch.object(placement, "converge_platform", lambda ssh: self.converged.append(
                            ssh.config["host"]) or "PostgreSQL and the private ingress run")):
            patcher.start()
            self.addCleanup(patcher.stop)
        self.key = enroll.prepare_key(self.root, "ocharo-products")
        self.root_keys = [self.key["publicKey"].split()[1]]

    def request(self, **changes):
        return {"serverId": "ocharo-products", "publicAddress": PUBLIC, "sshPort": 22, "loginUser": "root",
                "admin": "ops", "adminKey": OWNER_KEY, "tailnetHostname": "ocharo-vps", "expectedHostKey": None,
                **changes}

    def enroll(self, **changes):
        enroll.start(self.root, self.request(**changes), "sulton-max", launch=False)
        return enroll.run(self.root, "ocharo-products")

    def journal_text(self):
        return (self.root / "infra" / "enrollments" / "ocharo-products.json").read_text()

    def test_a_fresh_root_only_host_enrolls_end_to_end(self):
        result = self.enroll()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertEqual(["succeeded"] * 11, [step["status"] for step in result["steps"]])
        self.assertEqual([["admin", "ops"], ["packages"], ["host"], ["join", "ocharo-vps"], ["accept-runner"],
                          ["seal", "ops"]], self.host.phases)
        self.assertEqual(sorted(kit.FILES), sorted(self.host.uploaded))
        facts = result["facts"]
        self.assertEqual((TAILNET_ADDRESS, self.host.real_key, "tailnet", True),
                         (facts["tailnetAddress"], facts["hostKey"], facts["hostKeyConfirmedBy"],
                          facts["publicSshClosed"]))
        self.assertEqual(kit.digest(), facts["kitDigest"])
        self.assertTrue(facts["nodeId"])
        self.assertEqual([TAILNET_ADDRESS], self.converged)
        # What the console records on the server: the public address it enrolled on and the kit's public ingress.
        self.assertEqual((PUBLIC, "enrollment", kit.PUBLIC_INGRESS),
                         (facts["publicAddress"], facts["addressSource"], facts["ingress"]))
        # The minted key was single-use and tag:products, spent by the host, and recorded by ID only.
        state = list(self.fake.keys.values())
        self.assertEqual([(["tag:products"], True, False, True, 900)],
                         [(key["tags"], key["used"], key["reusable"], key["preauthorized"], key["expirySeconds"])
                          for key in state])
        self.assertEqual([state[0]["id"]], [mint["keyId"] for mint in result["mints"]])
        self.assertNotIn("tskey-auth", self.journal_text())
        self.assertNotIn("tskey-client", self.journal_text())
        # Prod's runner identity is pinned to the tailnet address and the first-seen key; the one-time key is gone.
        pin = (self.root / "ssh" / "ocharo-products" / "known_hosts").read_text().strip()
        self.assertEqual(TAILNET_ADDRESS + " " + self.host.real_key, pin)
        self.assertFalse((self.root / "infra" / "enrollment-keys" / "ocharo-products" / "identity").exists())
        self.assertNotIn("oneTimeKey", enroll.show(self.root, "ocharo-products"))
        with self.assertRaisesRegex(Rejected, "already enrolled"):
            enroll.start(self.root, self.request(), "sulton-max", launch=False)

    def test_the_owner_s_fingerprint_is_checked_before_anything_runs(self):
        wrong = "SHA256:" + "A" * 43
        result = self.enroll(expectedHostKey=wrong)
        self.assertEqual("failed", result["status"])
        self.assertEqual("failed", result["steps"][0]["status"])
        self.assertIn("not the fingerprint you gave", result["reason"])
        self.assertEqual([], self.host.phases)
        self.assertFalse((self.root / "infra" / "enrollment-keys" / "ocharo-products" / "known_hosts").exists())
        right = enroll.fingerprint(self.host.real_key)
        self.assertEqual("succeeded", self.enroll(expectedHostKey=right)["status"])
        self.assertEqual("owner", enroll.show(self.root, "ocharo-products")["facts"]["hostKeyConfirmedBy"])

    def test_a_node_at_a_foreign_endpoint_stops_the_enrollment_before_any_trust(self):
        self.host.endpoint = "198.51.100.66:41641"  # someone in the middle joined their own machine with the key
        result = self.enroll()
        self.assertEqual("failed", result["status"])
        self.assertEqual("node", next(step["key"] for step in result["steps"] if step["status"] == "failed"))
        self.assertIn("another machine may hold the key", result["reason"])
        self.assertFalse((self.root / "ssh" / "ocharo-products").exists())
        self.assertFalse(self.host.sealed)

    def test_a_relayed_first_contact_fails_the_tailnet_proof_and_leaves_no_runner_identity(self):
        self.host.public_key = host_key()  # the public path answered with another machine's key
        result = self.enroll()
        self.assertEqual("failed", result["status"])
        self.assertEqual("runner", next(step["key"] for step in result["steps"] if step["status"] == "failed"))
        self.assertIn("another SSH host key", result["reason"])
        self.assertFalse((self.root / "ssh" / "ocharo-products" / "identity").exists())
        self.assertFalse(self.host.sealed)

    def test_a_rerun_continues_from_the_failed_step(self):
        self.host.fail_phase = "packages"
        failed = self.enroll()
        self.assertEqual(("failed", "Review conflicting installed package manually: docker.io (exit 1)"),
                         (failed["status"], failed["reason"]))
        self.host.fail_phase = None
        self.host.phases.clear()
        result = self.enroll()
        self.assertEqual("succeeded", result["status"])
        self.assertEqual(["packages", "host", "join", "accept-runner", "seal"], [phase[0] for phase in self.host.phases])
        self.assertEqual(2, result["attempt"])

    def test_a_rerun_keeps_the_address_the_enrollment_already_reached(self):
        self.host.fail_phase = "host"
        self.enroll()
        with self.assertRaisesRegex(Rejected, "same address"):
            enroll.start(self.root, self.request(publicAddress="203.0.113.99"), "sulton-max", launch=False)

    def test_a_job_left_running_without_its_process_reads_interrupted(self):
        enroll.start(self.root, self.request(), "sulton-max", launch=False)
        record = json.loads(self.journal_text())
        record["status"] = "running"
        (self.root / "infra" / "enrollments" / "ocharo-products.json").write_text(json.dumps(record))
        self.assertEqual("interrupted", enroll.show(self.root, "ocharo-products")["status"])
        with infra_jobs.server_lock(self.root, "ocharo-products"):
            self.assertEqual("running", enroll.show(self.root, "ocharo-products")["status"])
            with self.assertRaisesRegex(Rejected, "Another job"):
                enroll.start(self.root, self.request(), "sulton-max", launch=False)

    def test_requests_are_checked_before_anything_is_recorded(self):
        cases = {"publicAddress": ("100.101.1.1", "not a tailnet one"), "loginUser": ("Root", "account"),
                 "admin": ("wheelhouse-deploy", "something other"), "tailnetHostname": ("Ocharo", "lowercase"),
                 "adminKey": ("ssh-ed25519 AAAA\nssh-ed25519 BBBB", "one SSH public key"),
                 "expectedHostKey": ("MD5:aa", "SHA256")}
        for field, (value, message) in cases.items():
            with self.subTest(field=field), self.assertRaisesRegex(Rejected, message):
                enroll.start(self.root, self.request(**{field: value}), "sulton-max", launch=False)
        with self.assertRaisesRegex(Rejected, "public IP"):
            enroll.start(self.root, self.request(publicAddress="ocharo.example"), "sulton-max", launch=False)
        self.assertFalse((self.root / "infra" / "enrollments" / "ocharo-products.json").exists())

    def test_without_the_oauth_client_nothing_starts(self):
        (self.root / tailnet.CLIENT_FILE).unlink()
        with self.assertRaisesRegex(Rejected, "Connect Tailscale first"):
            enroll.start(self.root, self.request(), "sulton-max", launch=False)

    def test_the_one_time_key_is_shown_until_enrolled_and_is_stable(self):
        again = enroll.prepare_key(self.root, "ocharo-products")
        self.assertEqual(self.key, again)
        self.assertEqual(self.key["publicKey"], enroll.show(self.root, "ocharo-products")["oneTimeKey"]["publicKey"])
        self.assertTrue(self.key["publicKey"].endswith("wheelhouse-enroll-ocharo-products"))
        mode = (self.root / "infra" / "enrollment-keys" / "ocharo-products" / "identity").stat().st_mode & 0o777
        self.assertEqual(0o600, mode)

    def test_the_local_server_is_never_enrolled(self):
        with patch.object(fleet, "SERVERS", (fleet.Server("ocharo-products", "x", fleet.VpsProvider.LOCAL, PUBLIC,
                                                          "local", "deploy"),)):
            with self.assertRaisesRegex(Rejected, "local server"):
                enroll.prepare_key(self.root, "ocharo-products")


class EnrollSshTests(unittest.TestCase):
    def test_the_public_session_pins_ed25519_and_ignores_user_config(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary) / "infra" / "enrollment-keys" / "fresh"
            folder.mkdir(parents=True)
            (folder / "identity").write_text("key")
            request = {"serverId": "fresh", "publicAddress": "2001:db8::5", "sshPort": 2222, "loginUser": "ubuntu"}
            first = enroll.EnrollSsh(temporary, request, first_contact=True).arguments("true")
            later = enroll.EnrollSsh(temporary, request).arguments("true")
        self.assertEqual(["-F", "/dev/null"], first[1:3])
        for option in ("BatchMode=yes", "IdentitiesOnly=yes", "HostKeyAlgorithms=ssh-ed25519",
                       "GlobalKnownHostsFile=/dev/null", "UpdateHostKeys=no"):
            self.assertIn(option, first)
        self.assertIn("StrictHostKeyChecking=accept-new", first)
        self.assertIn("StrictHostKeyChecking=yes", later)
        self.assertEqual(["-p", "2222", "ubuntu@2001:db8::5", "true"], later[-4:])

    def test_a_sudo_image_runs_each_phase_through_sudo(self):
        session = enroll.EnrollSsh.__new__(enroll.EnrollSsh)
        session.request = {"loginUser": "ubuntu"}
        commands = []
        session.run = lambda command, stdin=b"", timeout=120: commands.append(command) or ""
        session.phase("join", "ocharo-vps", stdin=b"key")
        self.assertEqual(['sudo -n bash "$HOME/.wheelhouse-kit/enroll-products.sh" join ocharo-vps'], commands)

    def test_a_failed_host_step_relays_the_kit_s_line_but_never_a_key(self):
        error = infra_jobs.ssh_failure(1, "noise\ntskey-auth-abc leaked\nThe node lacks tag:products.\n")
        self.assertEqual("The node lacks tag:products. (exit 1)", str(error))
        error = infra_jobs.ssh_failure(255, "x\ntskey-auth-abc\n")
        self.assertNotIn("tskey", str(error))


if __name__ == "__main__":
    unittest.main()
