"""The products-host kit: shell syntax, the rendered Compose file, the tailnet policy and the SSH-session guard.
Never contacts a host; the Compose check uses the local Docker CLI when there is one."""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest

import prepare

KIT = Path(__file__).resolve().parent


def hujson(text):
    """The policy as JSON: line comments and trailing commas removed (the policy has no // inside a string)."""
    text = re.sub(r"^\s*//.*$|\s//.*$", "", text, flags=re.M)
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", text))


class ScriptTests(unittest.TestCase):
    def test_every_script_parses(self):
        for script in sorted(KIT.glob("*.sh")):
            with self.subTest(script=script.name):
                subprocess.run(["bash", "-n", str(script)], check=True)

    def test_sourcing_the_products_bootstrap_defines_its_steps_without_running_them(self):
        result = subprocess.run(["bash", "-c", '. "$1"; declare -F | awk "{print \\$3}"', "_",
                                 str(KIT / "bootstrap-products.sh")], capture_output=True, text=True, check=True)
        steps = set(result.stdout.split())
        self.assertLessEqual({"ensure_deploy_account", "lay_out_kit", "ensure_platform_network", "write_ssh_policy",
                              "harden_ssh", "enable_security_upgrades", "join_tailnet", "configure_firewall"}, steps)

    def tailnet_session(self, lines):
        return subprocess.run(["bash", "-c", '. "$1"; tailnet_session', "_", str(KIT / "bootstrap-products.sh")],
                              input=lines, capture_output=True, text=True).returncode == 0

    def test_public_ssh_closes_only_beside_an_open_tailnet_session(self):
        public = "0 0 203.0.113.10:22 198.51.100.7:51544\n"
        self.assertFalse(self.tailnet_session(""))
        self.assertFalse(self.tailnet_session(public))
        self.assertTrue(self.tailnet_session(public + "0 0 100.101.102.103:22 100.64.0.9:50122\n"))
        self.assertTrue(self.tailnet_session("0 36 [fd7a:115c:a1e0::5]:22 [fd7a:115c:a1e0::9]:61000\n"))
        self.assertFalse(self.tailnet_session("0 0 [2001:db8::1]:22 [2001:db8::2]:61000\n"))

    def test_the_products_bootstrap_keeps_80_and_443_closed_and_passwords_off(self):
        script = (KIT / "bootstrap-products.sh").read_text()
        self.assertNotRegex(script, r"ufw allow[^\n]*\b(80|443)\b")
        for line in ("PasswordAuthentication no", "KbdInteractiveAuthentication no", "PermitRootLogin no"):
            self.assertIn("\n" + line + "\n", script)
        self.assertIn('--advertise-tags="$tailnet_tag"', script)
        self.assertIn("tailnet_tag=tag:products", script)


class ComposeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "wheelhouse"
        (self.root / "platform").mkdir(parents=True, mode=0o700)
        self.root.chmod(0o700)
        shutil.copy(KIT / "products.compose.yml", self.root / "platform" / "products.compose.yml")
        shutil.copy(KIT / "tailscale-serve-products.json", self.root / "platform" / "tailscale-serve.json")
        prepare.prepare_products(self.root, "ocharo-platform", sites=[("app", "ocharo-desk")],
                                 tailnet="tail1234.ts.net")
        self.compose = (self.root / "platform" / "compose.yml").read_text()

    def test_the_products_platform_runs_no_console_and_publishes_only_loopback(self):
        services = re.findall(r"^  ([a-z-]+):$", self.compose.split("\nservices:\n", 1)[1], re.M)
        self.assertEqual(["postgres", "ingress", "ocharo-desk-dev", "ocharo-desk"], services)
        definitions = re.sub(r"#.*", "", self.compose)
        self.assertNotIn("console", definitions)
        self.assertNotIn("control", definitions)
        published = re.findall(r"^      - ([0-9.]+):[0-9]+:[0-9]+$", self.compose, re.M)
        self.assertEqual(["127.0.0.1"], published)
        self.assertIn("ipv4_address: 172.30.0.3\n        aliases: [wheelhouse-postgres]", self.compose)
        self.assertIn("--entryPoints.private.address=:8080", self.compose)
        self.assertEqual(1, self.compose.count("--entryPoints.private.address"))

    def test_site_nodes_proxy_to_the_private_entry_point(self):
        serve = json.loads((KIT / "tailscale-serve-products.json").read_text())
        self.assertEqual({"${TS_CERT_DOMAIN}:443": {"Handlers": {"/": {"Proxy": "http://172.30.0.2:8080"}}}},
                         serve["Web"])
        self.assertIn("ipv4_address: 172.30.0.2", self.compose)
        self.assertIn("--advertise-tags=tag:product-site", self.compose)

    @unittest.skipUnless(shutil.which("docker"), "needs the Docker CLI")
    def test_the_rendered_file_is_valid_compose(self):
        result = subprocess.run(["docker", "compose", "-f", str(self.root / "platform" / "compose.yml"),
                                 "config", "--quiet"], capture_output=True, text=True,
                                env={**os.environ, "WHEELHOUSE_DEPLOY_UID": "1000", "WHEELHOUSE_DEPLOY_GID": "1000"})
        self.assertEqual(0, result.returncode, result.stderr)


class TailnetPolicyTests(unittest.TestCase):
    def setUp(self):
        self.policy = hujson((KIT / "tailnet-policy.hujson").read_text())

    def test_the_only_tagged_path_is_control_to_products_ssh(self):
        tagged = [grant for grant in self.policy["grants"] if any(src.startswith("tag:") for src in grant["src"])]
        self.assertEqual([{"src": ["tag:control"], "dst": ["tag:products"], "ip": ["tcp:22"]}], tagged)
        self.assertEqual({"tag:control", "tag:canary", "tag:products", "tag:product-site"},
                         set(self.policy["tagOwners"]))

    def test_owner_devices_reach_products_ssh_and_the_product_sites(self):
        owner = {grant["dst"][0]: grant["ip"] for grant in self.policy["grants"]
                 if grant["src"] == ["autogroup:member"]}
        self.assertEqual(["tcp:22"], owner["tag:products"])
        self.assertEqual(["tcp:443"], owner["tag:product-site"])

    def test_the_policy_tests_keep_tagged_nodes_off_the_control_host(self):
        tests = {test["src"]: test for test in self.policy["tests"]}
        self.assertEqual(["tag:products:22"], tests["tag:control"]["accept"])
        for source in ("tag:canary", "tag:products", "tag:product-site"):
            self.assertLessEqual({"tag:control:443", "tag:control:8443", "tag:control:22"},
                                 set(tests[source]["deny"]), source)
        self.assertIn("tag:product-site:443", tests["tag:control"]["deny"])


if __name__ == "__main__":
    unittest.main()
