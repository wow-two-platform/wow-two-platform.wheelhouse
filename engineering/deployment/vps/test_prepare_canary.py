"""Canary settings lifecycle; never contacts a host or Docker."""
import json
from pathlib import Path
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
        self.assertEqual(["wheelhousecanary_dev", "wheelhousecanary_test", "wheelhousecanary_prod"],
                         [name for name, _ in self.created])
        for environment, node in (("dev", "canary-dev"), ("test", "canary-test"), ("prod", "canary")):
            path = self.root / "config" / ("canary-" + environment + ".json")
            settings = json.loads(path.read_text())
            self.assertEqual(node + ".test-tailnet.ts.net;localhost", settings["AllowedHosts"])
            self.assertEqual(environment, settings["Canary"]["Label"])
            self.assertIn("Database=wheelhousecanary_" + environment + ";", settings["DatabaseSettings"]["ConnectionString"])
            self.assertEqual(0o444, path.stat().st_mode & 0o777)
        passwords = {password for _, password in self.created}
        self.assertEqual(3, len(passwords))
        self.assertEqual(0o700, (self.root / "config").stat().st_mode & 0o777)

    def test_existing_settings_are_never_replaced(self):
        self.run_prepare()
        original = (self.root / "config/canary-dev.json").read_bytes()
        with self.assertRaises(ValueError):
            self.run_prepare()
        self.assertEqual(3, len(self.created))
        self.assertEqual(original, (self.root / "config/canary-dev.json").read_bytes())

    def test_a_wrong_tailnet_name_writes_nothing(self):
        with self.assertRaises(ValueError):
            prepare_canary.prepare_canary(self.root, "example.com", lambda *args: self.created.append(args))
        self.assertEqual([], self.created)
        self.assertFalse(self.root.exists())


if __name__ == "__main__":
    unittest.main()
