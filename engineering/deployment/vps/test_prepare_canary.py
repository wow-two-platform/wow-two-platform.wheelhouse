"""Canary settings lifecycle; never contacts a host or Docker."""
import json
from pathlib import Path
import re
import tempfile
import unittest

import prepare_canary


class PrepareCanaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "wheelhouse"
        self.created = []

    def run_prepare(self):
        prepare_canary.prepare_canary(self.root, "test-tailnet.ts.net",
                                      lambda root, name, password: self.created.append((name, password)))

    def test_each_environment_gets_its_own_database_host_and_label(self):
        self.run_prepare()
        self.assertEqual(["wheelhousecanary_dev", "wheelhousecanary_prod"], [name for name, _ in self.created])
        self.assertFalse((self.root / "config" / "canary-test.json").exists())
        for environment, node in (("dev", "canary-dev"), ("prod", "canary")):
            path = self.root / "config" / ("canary-" + environment + ".json")
            settings = json.loads(path.read_text())
            self.assertEqual(node + ".test-tailnet.ts.net;localhost", settings["AllowedHosts"])
            self.assertEqual(environment, settings["Canary"]["Label"])
            self.assertIn("Database=wheelhousecanary_" + environment + ";", settings["DatabaseSettings"]["ConnectionString"])
            self.assertEqual(0o444, path.stat().st_mode & 0o777)
        passwords = {password for _, password in self.created}
        self.assertEqual(2, len(passwords))
        self.assertEqual(0o700, (self.root / "config").stat().st_mode & 0o777)

    def test_existing_settings_are_never_replaced(self):
        self.run_prepare()
        original = (self.root / "config/canary-dev.json").read_bytes()
        with self.assertRaises(ValueError):
            self.run_prepare()
        self.assertEqual(2, len(self.created))
        self.assertEqual(original, (self.root / "config/canary-dev.json").read_bytes())

    def test_the_platform_runs_one_tailnet_node_per_environment_and_trusts_only_those(self):
        compose = Path(__file__).with_name("platform.compose.yml").read_text()
        nodes = dict(re.findall(r"^  ([a-z-]+):\n    <<: \*canary-node\n(?:    .*\n)*?"
                                r"    networks: \{ platform: \{ ipv4_address: ([0-9.]+) \} \}$", compose, re.M))
        self.assertEqual({"canary-dev": "172.30.0.10", "canary": "172.30.0.12"}, nodes)
        self.assertEqual(sorted(prepare_canary.NODES.values()), sorted(nodes))
        trusted = re.search(r"--entryPoints\.canary\.forwardedHeaders\.trustedIPs=(\S+)", compose).group(1)
        self.assertEqual(["127.0.0.1/32", "172.30.0.1/32"] + [address + "/32" for address in nodes.values()],
                         trusted.split(","))
        self.assertNotIn("canary-test:", compose)

    def test_a_wrong_tailnet_name_writes_nothing(self):
        with self.assertRaises(ValueError):
            prepare_canary.prepare_canary(self.root, "example.com", lambda *args: self.created.append(args))
        self.assertEqual([], self.created)
        self.assertFalse(self.root.exists())


if __name__ == "__main__":
    unittest.main()
