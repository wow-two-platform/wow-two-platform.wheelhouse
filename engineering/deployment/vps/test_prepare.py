"""Credential lifecycle and existing runner-contract checks; never contacts a host, GitHub or Docker."""
import importlib.util
import ipaddress
import json
import os
from pathlib import Path
import pwd
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

import prepare

KIT = Path(__file__).resolve().parent
TOKEN = "ghp_" + "T0kenValueNeverPrinted" + "x" * 14


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
        config = json.loads((self.root / "config/console-dev.json").read_text())
        runner_path = Path(__file__).resolve().parents[2] / "codebase/wheelhouse.runner-services/runner.py"
        spec = importlib.util.spec_from_file_location("vps_runner_contract", runner_path)
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        self.assertEqual((self.root, "wheelhouse-dev"), runner.validate_target(target))
        runner.validate_settings(target, "console", ["ConnectionStrings:Wheelhouse", "Identity:GitHub:ClientId",
                                                    "Identity:GitHub:ClientSecret", "Identity:AllowedGitHubLogins"])
        self.assertEqual("control", runner.base_environment(target)["PLATFORM_NETWORK"])
        self.assertEqual("", config["Deployment"]["Rehearsal"])
        self.assertEqual(["172.30.1.2"], config["Deployment"]["TrustedProxies"])
        self.assertEqual("http://127.0.0.1:18082", target["ingress"]["privateProbe"])
        self.assertEqual((8443, ["dev"]), (target["ingress"]["port"], target["ingress"]["privateEntryPoints"]))
        self.assertEqual(target["ingress"]["probe"], target["ingress"]["privateProbe"])
        connection = config["ConnectionStrings"]["Wheelhouse"]
        password = connection.rsplit("Password=", 1)[1]
        sql = (self.root / "platform/.secrets/wheelhouse-dev.sql").read_text()
        self.assertIn("NOSUPERUSER NOCREATEDB NOCREATEROLE", sql)
        self.assertIn("PASSWORD '" + password + "'", sql)
        self.assertNotEqual(password, (self.root / "platform/.secrets/postgres-password").read_text().strip())
        self.assertEqual(0o700, (self.root / "config").stat().st_mode & 0o777)
        self.assertEqual(0o444, (self.root / "config/console-dev.json").stat().st_mode & 0o777)
        self.assertEqual(0o600, (self.root / "config/wheelhouse-dev.json").stat().st_mode & 0o777)

    def test_prod_joins_the_running_database_with_its_own_credentials(self):
        self.run_prepare()
        created = []
        prepare.prepare(self.root, self.identity, "wheelhouse.test-tailnet.ts.net", "prod",
                        lambda root, name, password: created.append((name, password)))
        target = json.loads((self.root / "config/wheelhouse-prod.json").read_text())
        config = json.loads((self.root / "config/console-prod.json").read_text())
        dev = json.loads((self.root / "config/console-dev.json").read_text())
        self.assertEqual("prod", target["environment"])
        self.assertEqual(str(self.root / "config/console-prod.json"), target["settings"]["console"])
        self.assertEqual((None, ["private"], "http://127.0.0.1:18080"),
                         (target["ingress"]["port"], target["ingress"]["privateEntryPoints"],
                          target["ingress"]["privateProbe"]))
        self.assertEqual([("wheelhouse_prod", config["ConnectionStrings"]["Wheelhouse"].rsplit("Password=", 1)[1])],
                         created)
        self.assertIn("Database=wheelhouse_prod;Username=wheelhouse_prod;", config["ConnectionStrings"]["Wheelhouse"])
        self.assertNotEqual(dev["ConnectionStrings"]["Wheelhouse"], config["ConnectionStrings"]["Wheelhouse"])
        self.assertEqual(0o444, (self.root / "config/console-prod.json").stat().st_mode & 0o777)
        self.assertEqual(0o600, (self.root / "config/wheelhouse-prod.json").stat().st_mode & 0o777)
        with self.assertRaises(ValueError):
            prepare.prepare(self.root, self.identity, "wheelhouse.test-tailnet.ts.net", "prod",
                            lambda root, name, password: created.append((name, password)))
        self.assertEqual(1, len(created))

    def test_failed_prod_database_creation_writes_no_files(self):
        def refused(root, name, password):
            raise ValueError("Database creation failed")
        with self.assertRaises(ValueError):
            prepare.prepare(self.root, self.identity, "wheelhouse.test-tailnet.ts.net", "prod", refused)
        self.assertFalse((self.root / "config/console-prod.json").exists())
        self.assertFalse((self.root / "platform/.secrets/postgres-password").exists())

    def test_repeat_and_partial_state_never_replace_credentials(self):
        self.run_prepare()
        original = (self.root / "config/console-dev.json").read_bytes()
        with self.assertRaises(ValueError):
            self.run_prepare()
        (self.root / "config/wheelhouse-dev.json").unlink()
        with self.assertRaises(ValueError):
            self.run_prepare()
        self.assertEqual(original, (self.root / "config/console-dev.json").read_bytes())

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

    def test_control_cli_refuses_products_options(self):
        result = subprocess.run([sys.executable, str(Path(prepare.__file__)), "--root", str(self.root),
                                 "--identity-file", str(self.identity), "--hostname",
                                 "wheelhouse-dev.test-tailnet.ts.net", "--database"], capture_output=True, text=True)
        self.assertEqual(2, result.returncode)
        self.assertFalse(self.root.exists())


class ProductsPrepareTests(unittest.TestCase):
    """A products host: PostgreSQL password, per-product databases, site nodes, rendered Compose, GHCR sign-in."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.root = self.directory / "wheelhouse"
        # What bootstrap-products.sh installs before prepare.py runs.
        (self.root / "platform").mkdir(parents=True, mode=0o700)
        self.root.chmod(0o700)
        shutil.copy(KIT / "products.compose.yml", self.root / "platform" / "products.compose.yml")
        self.created = []

    def database(self, root, name, password):
        self.created.append((name, password))

    def products(self, **arguments):
        return prepare.prepare_products(self.root, create=self.database, **arguments)

    def compose(self):
        return (self.root / "platform" / "compose.yml").read_text()

    def test_platform_password_is_created_once_and_kept(self):
        self.products()
        password = self.root / "platform/.secrets/postgres-password"
        original = password.read_text()
        self.assertEqual(65, len(original))
        self.assertEqual(0o444, password.stat().st_mode & 0o777)
        self.assertEqual(0o700, (self.root / "platform/.secrets").stat().st_mode & 0o777)
        self.products()
        self.assertEqual(original, password.read_text())
        self.assertEqual((KIT / "products.compose.yml").read_text(), self.compose())
        self.assertEqual(0o600, (self.root / "platform/compose.yml").stat().st_mode & 0o777)

    def test_each_environment_gets_its_own_database_role_and_private_credentials(self):
        report = self.products(product="ocharo-platform", database=True)
        self.assertEqual(["ocharo_platform_dev", "ocharo_platform_prod"], [name for name, _ in self.created])
        self.assertEqual(2, len({password for _, password in self.created}))
        for environment, (name, password) in zip(("dev", "prod"), self.created):
            folder = self.root / "config" / ("ocharo-platform-" + environment)
            credentials = json.loads((folder / "database.json").read_text())
            self.assertEqual(0o700, folder.stat().st_mode & 0o777)
            self.assertEqual(0o600, (folder / "database.json").stat().st_mode & 0o777)
            self.assertEqual(("wheelhouse-postgres", name, name, password),
                             (credentials["Host"], credentials["Database"], credentials["Username"],
                              credentials["Password"]))
            self.assertEqual("Host=wheelhouse-postgres;Port=5432;Database=" + name + ";Username=" + name
                             + ";Password=" + password, credentials["ConnectionString"])
            self.assertIn("Settings for ocharo-platform-" + environment + ": " + str(folder) + "/<service>.json",
                          report)
            self.assertFalse(any(password in line for line in report))

    def test_declared_extensions_are_created_as_superuser_in_both_databases(self):
        made = []

        def extend(root, name, extensions):
            made.append((name, extensions))
        with self.assertRaisesRegex(prepare.Refused, "databases before its extensions"):
            self.products(product="ocharo-platform", extensions=["postgres_fdw"], extend=extend)
        report = self.products(product="ocharo-platform", database=True, extensions=["postgres_fdw", "uuid-ossp"],
                               extend=extend)
        self.assertEqual([("ocharo_platform_dev", ["postgres_fdw", "uuid-ossp"]),
                          ("ocharo_platform_prod", ["postgres_fdw", "uuid-ossp"])], made)
        self.assertIn("Extensions postgres_fdw, uuid-ossp in ocharo_platform_dev and ocharo_platform_prod, created as "
                      "superuser.", report)
        # A rerun on kept databases creates only what is missing; a name that is not one refuses before any SQL.
        self.products(product="ocharo-platform", extensions=["postgres_fdw"], extend=extend)
        self.assertEqual(4, len(made))
        with self.assertRaisesRegex(prepare.Refused, "extension name"):
            self.products(product="ocharo-platform", extensions=['x"; DROP DATABASE y; --'], extend=extend)
        self.assertEqual(4, len(made))

    def test_extension_sql_quotes_each_name_and_runs_as_superuser_in_one_database(self):
        calls = []

        def run(arguments, **options):
            calls.append((arguments, options["input"]))
            return subprocess.CompletedProcess(arguments, 0, "", "")
        with mock.patch.object(prepare.subprocess, "run", side_effect=run):
            prepare.create_extensions(self.root, "ocharo_platform_dev", ["pg_trgm", "uuid-ossp"])
        [(arguments, sql)] = calls
        self.assertEqual(["psql", "-U", "postgres", "-d", "ocharo_platform_dev", "-v", "ON_ERROR_STOP=1", "-q"],
                         arguments[-8:])
        self.assertEqual('CREATE EXTENSION IF NOT EXISTS "pg_trgm";\nCREATE EXTENSION IF NOT EXISTS "uuid-ossp";\n', sql)

    def test_existing_database_credentials_are_never_replaced(self):
        self.products(product="ocharo-platform", database=True)
        original = (self.root / "config/ocharo-platform-dev/database.json").read_bytes()
        with self.assertRaises(prepare.Refused):
            self.products(product="ocharo-platform", database=True)
        self.assertEqual(2, len(self.created))
        self.assertEqual(original, (self.root / "config/ocharo-platform-dev/database.json").read_bytes())

    def test_a_failed_database_keeps_the_created_one_s_password_and_refuses_a_rerun(self):
        def prod_fails(root, name, password):
            if name.endswith("_prod"):
                raise ValueError("Database creation failed")
            self.created.append((name, password))
        with self.assertRaises(prepare.Refused):
            prepare.prepare_products(self.root, "ocharo-studio", database=True, create=prod_fails)
        dev = json.loads((self.root / "config/ocharo-studio-dev/database.json").read_text())
        self.assertEqual(self.created[0][1], dev["Password"])
        self.assertFalse((self.root / "config/ocharo-studio-prod/database.json").exists())
        with self.assertRaises(prepare.Refused):
            prepare.prepare_products(self.root, "ocharo-studio", database=True, create=prod_fails)
        self.assertEqual(1, len(self.created))

    def test_sites_get_one_node_per_environment_at_stable_trusted_addresses(self):
        report = self.products(product="ocharo-platform", sites=[("app", "ocharo-desk")], tailnet="tail1234.ts.net")
        self.products(product="ocharo-studio", sites=[("app", "ocharo-studio")], tailnet="tail1234.ts.net")
        self.products(product="ocharo-platform", sites=[("app", "ocharo-desk")], tailnet="tail1234.ts.net")
        nodes = {node["name"]: node for node in json.loads((self.root / "platform/sites.json").read_text())["nodes"]}
        self.assertEqual({"ocharo-desk-dev": "172.30.0.65", "ocharo-desk": "172.30.0.66",
                          "ocharo-studio-dev": "172.30.0.67", "ocharo-studio": "172.30.0.68"},
                         {name: node["address"] for name, node in nodes.items()})
        self.assertEqual(("ocharo-platform", "app", "dev"), tuple(nodes["ocharo-desk-dev"][key]
                                                                  for key in ("product", "site", "environment")))
        self.assertIn("Site app (dev): https://ocharo-desk-dev.tail1234.ts.net on node ocharo-desk-dev at 172.30.0.65",
                      report)
        compose = self.compose()
        rendered = dict(re.findall(r"^  ([a-z-]+):\n    <<: \*site-node\n    hostname: \1\n"
                                   r"    env_file:\n      - \{ path: \.\./private/tailscale-sites\.env, required: false \}\n"
                                   r"      - \{ path: \.\./private/sites/\1\.env, required: false \}\n.*\n"
                                   r"    volumes: \[\./sites/\1:/var/lib/tailscale, .*\]\n"
                                   r"    networks: \{ platform: \{ ipv4_address: ([0-9.]+) \} \}$", compose, re.M))
        self.assertEqual({name: node["address"] for name, node in nodes.items()}, rendered)
        trusted = re.search(r"--entryPoints\.private\.forwardedHeaders\.trustedIPs=(\S+)", compose).group(1).split(",")
        self.assertTrue(all(any(ipaddress.ip_address(address) in ipaddress.ip_network(network) for network in trusted)
                            for address in rendered.values()))
        self.assertTrue((self.root / "platform/sites/ocharo-desk-dev").is_dir())
        self.assertEqual(0o700, (self.root / "platform/sites/ocharo-desk-dev").stat().st_mode & 0o777)

    def test_a_node_name_serves_one_site_only(self):
        self.products(product="ocharo-platform", sites=[("app", "ocharo-desk")], tailnet="tail1234.ts.net")
        for product, sites in (("ocharo-studio", [("app", "ocharo-desk")]), ("ocharo-platform", [("app", "desk")]),
                               ("ocharo-studio", [("app", "postgres")]), ("ocharo-studio", [("app", "Bad_Name")])):
            with self.assertRaises(prepare.Refused):
                self.products(product=product, sites=sites, tailnet="tail1234.ts.net")
        with self.assertRaises(prepare.Refused):
            self.products(product="ocharo-studio", sites=[("app", "ocharo-studio")], tailnet="example.com")
        self.assertEqual(2, len(json.loads((self.root / "platform/sites.json").read_text())["nodes"]))

    def test_a_tampered_registry_never_reaches_compose(self):
        self.products()
        (self.root / "platform/sites.json").write_text(json.dumps({"nodes": [
            {"name": "evil\n  injected", "address": "172.30.0.65", "product": "x", "site": "app", "environment": "dev"}]}))
        with self.assertRaises(prepare.Refused):
            self.products()
        (self.root / "platform/sites.json").write_text(json.dumps({"nodes": [
            {"name": "outside", "address": "172.30.0.3", "product": "x", "site": "app", "environment": "dev"}]}))
        with self.assertRaises(prepare.Refused):
            self.products()
        self.assertNotIn("injected", self.compose())

    def token_file(self, value=TOKEN, mode=0o600):
        path = self.directory / "ghcr-token"
        path.write_text(value + "\n")
        path.chmod(mode)
        return path

    def test_ghcr_sign_in_takes_only_a_read_only_classic_token(self):
        signed = []
        prepare.ghcr_login(self.token_file(), "test-owner", lambda token: {"read:packages"},
                           lambda user, token: signed.append((user, token)))
        self.assertEqual([("test-owner", TOKEN)], signed)
        refusals = [
            (TOKEN, 0o600, "test-owner", {"read:packages", "write:packages"}),
            (TOKEN, 0o600, "test-owner", {"read:packages", "repo"}),
            (TOKEN, 0o600, "test-owner", set()),
            ("github_pat_" + "x" * 70, 0o600, "test-owner", {"read:packages"}),
            (TOKEN, 0o640, "test-owner", {"read:packages"}),
            (TOKEN, 0o600, "not a login", {"read:packages"}),
        ]
        for value, mode, user, scopes in refusals:
            with self.assertRaises(prepare.Refused):
                prepare.ghcr_login(self.token_file(value, mode), user, lambda token, scopes=scopes: scopes,
                                   lambda user, token: signed.append((user, token)))
        alias = self.directory / "alias"
        alias.symlink_to(self.token_file())
        with self.assertRaises(prepare.Refused):
            prepare.ghcr_login(alias, "test-owner", lambda token: {"read:packages"}, lambda *args: signed.append(args))
        self.assertEqual(1, len(signed))

    def test_docker_login_gets_the_token_on_stdin_and_writes_the_account_s_own_config(self):
        bin_directory = self.directory / "bin"
        bin_directory.mkdir()
        record = self.directory / "docker-call.json"
        fake = bin_directory / "docker"
        fake.write_text("#!" + sys.executable + "\nimport json, os, sys\n"
                        "json.dump({'argv': sys.argv[1:], 'stdin': sys.stdin.read(), 'home': os.environ.get('HOME'),"
                        " 'config': os.environ.get('DOCKER_CONFIG')}, open(" + repr(str(record)) + ", 'w'))\n"
                        "print('Login Succeeded')\n")
        fake.chmod(0o755)
        with mock.patch.dict(os.environ, {"PATH": str(bin_directory) + os.pathsep + os.environ["PATH"],
                                          "DOCKER_CONFIG": "/elsewhere"}):
            prepare.docker_login("test-owner", TOKEN)
        call = json.loads(record.read_text())
        self.assertEqual(["login", "ghcr.io", "--username", "test-owner", "--password-stdin"], call["argv"])
        self.assertEqual(TOKEN, call["stdin"])
        self.assertIsNone(call["config"])
        self.assertEqual(pwd.getpwuid(os.getuid()).pw_dir, call["home"])
        fake.write_text("#!/bin/sh\necho 'denied: " + TOKEN + "' >&2\nexit 1\n")
        with mock.patch.dict(os.environ, {"PATH": str(bin_directory) + os.pathsep + os.environ["PATH"]}):
            with self.assertRaises(prepare.Refused) as refused:
                prepare.docker_login("test-owner", TOKEN)
        self.assertNotIn(TOKEN, str(refused.exception))

    def test_products_cli_never_prints_a_token_or_password(self):
        token = self.token_file("ghp_" + "Wr0ngShapeNeverPrinted")
        result = subprocess.run([sys.executable, str(Path(prepare.__file__)), "--server", "products",
                                 "--root", str(self.root), "--ghcr-user", "test-owner",
                                 "--ghcr-token-file", str(token)], capture_output=True, text=True)
        self.assertEqual(1, result.returncode)
        self.assertIn("Use a classic token with read:packages only", result.stderr)
        self.assertNotIn("Wr0ngShapeNeverPrinted", result.stdout + result.stderr)
        password = (self.root / "platform/.secrets/postgres-password").read_text().strip()
        self.assertNotIn(password, result.stdout + result.stderr)

    def test_products_cli_refuses_control_options(self):
        result = subprocess.run([sys.executable, str(Path(prepare.__file__)), "--server", "products",
                                 "--root", str(self.root), "--hostname", "wheelhouse.test-tailnet.ts.net"],
                                capture_output=True, text=True)
        self.assertEqual(2, result.returncode)
        self.assertFalse((self.root / "platform/.secrets").exists())


if __name__ == "__main__":
    unittest.main()
