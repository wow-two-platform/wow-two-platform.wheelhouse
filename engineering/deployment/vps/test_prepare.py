"""Credential lifecycle and existing runner-contract checks; never contacts a host or Docker."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import prepare


class PrepareTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.root = self.directory / "wheelhouse"
        self.identity = self.directory / "identity.json"
        self.identity.write_text(json.dumps({"Identity": {
            "GitHub": {"ClientId": "test-client", "ClientSecret": "test-secret-do-not-print"},
            "AllowedGitHubLogins": ["test-owner"]}}))
        self.identity.chmod(0o600)

    def run_prepare(self):
        prepare.prepare(self.root, self.identity, "wheelhouse-dev.test-tailnet.ts.net")

    def test_generated_target_satisfies_runner_contract_and_preserves_secrets(self):
        self.run_prepare()
        target = json.loads((self.root / "config/wheelhouse-dev.json").read_text())
        config = json.loads((self.root / "config/console.json").read_text())
        runner_path = Path(__file__).resolve().parents[2] / "codebase/wheelhouse.runner-services/runner.py"
        spec = importlib.util.spec_from_file_location("vps_runner_contract", runner_path)
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        self.assertEqual((self.root, "wheelhouse-dev"), runner.validate_target(target))
        runner.validate_settings(target, "console", ["ConnectionStrings:Wheelhouse", "Identity:GitHub:ClientId",
                                                    "Identity:GitHub:ClientSecret", "Identity:AllowedGitHubLogins"])
        self.assertEqual("platform", runner.base_environment(target)["PLATFORM_NETWORK"])
        self.assertEqual("", config["Deployment"]["Rehearsal"])
        self.assertEqual(["172.30.0.2"], config["Deployment"]["TrustedProxies"])
        self.assertEqual("http://127.0.0.1:18080", target["ingress"]["privateProbe"])
        self.assertEqual(target["ingress"]["probe"], target["ingress"]["privateProbe"])
        connection = config["ConnectionStrings"]["Wheelhouse"]
        password = connection.rsplit("Password=", 1)[1]
        sql = (self.root / "platform/.secrets/wheelhouse-dev.sql").read_text()
        self.assertIn("NOSUPERUSER NOCREATEDB NOCREATEROLE", sql)
        self.assertIn("PASSWORD '" + password + "'", sql)
        self.assertNotEqual(password, (self.root / "platform/.secrets/postgres-password").read_text().strip())
        self.assertEqual(0o700, (self.root / "config").stat().st_mode & 0o777)
        self.assertEqual(0o444, (self.root / "config/console.json").stat().st_mode & 0o777)
        self.assertEqual(0o600, (self.root / "config/wheelhouse-dev.json").stat().st_mode & 0o777)

    def test_repeat_and_partial_state_never_replace_credentials(self):
        self.run_prepare()
        original = (self.root / "config/console.json").read_bytes()
        with self.assertRaises(ValueError):
            self.run_prepare()
        (self.root / "config/wheelhouse-dev.json").unlink()
        with self.assertRaises(ValueError):
            self.run_prepare()
        self.assertEqual(original, (self.root / "config/console.json").read_bytes())

    def test_public_or_symlinked_input_is_refused_before_output(self):
        self.identity.chmod(0o644)
        with self.assertRaises(ValueError):
            self.run_prepare()
        self.assertFalse(self.root.exists())
        self.identity.chmod(0o600)
        alias = self.directory / "alias.json"
        alias.symlink_to(self.identity)
        with self.assertRaises(ValueError):
            prepare.prepare(self.root, alias, "wheelhouse-dev.test-tailnet.ts.net")

    def test_empty_identity_and_public_hostname_are_refused(self):
        with self.assertRaises(ValueError):
            prepare.prepare(self.root, self.identity, "wheelhouse.example.com")
        self.identity.write_text('{"Identity":{"GitHub":{"ClientId":"","ClientSecret":""}}}')
        with self.assertRaises(ValueError):
            self.run_prepare()
        self.assertFalse(self.root.exists())

    def test_cli_never_prints_private_input_on_parse_failure(self):
        self.identity.write_text('malformed test-secret-do-not-print')
        result = subprocess.run([sys.executable, str(Path(prepare.__file__)), "--root", str(self.root),
                                 "--identity-file", str(self.identity), "--hostname",
                                 "wheelhouse-dev.test-tailnet.ts.net"], capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertNotIn("test-secret-do-not-print", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
