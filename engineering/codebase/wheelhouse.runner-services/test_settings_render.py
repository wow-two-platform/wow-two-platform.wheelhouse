"""Settings Wheelhouse renders at deploy and the per-deploy registry sign-in: layers and precedence, derived keys, the
required-key refusal before any write, file modes, the declarations the console reads, the target check, and that no
secret value reaches a job record, an argument or the disk outside the rendered file."""
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
import urllib.parse
import registry
import runner
import transport

INGRESS = [{"Id": "ing", "Config": {"Labels": {"com.docker.compose.project": "wheelhouse-products",
                                               "com.docker.compose.service": "ingress"}},
            "NetworkSettings": {"Networks": {"platform": {"IPAddress": "172.30.0.2"}}}}]


class RenderDocker:
    """Answers the ingress lookup and records the rollout; `ingress` False hides the ingress."""
    events = []
    ingress = True
    pulled = None
    unreadable = ()

    def __init__(self, target, environment):
        self.environment = environment

    def execute(self, arguments, step, timeout=600):
        if arguments[:2] == ["docker", "ps"] and any("service=ingress" in item for item in arguments):
            return "ing\n" if self.ingress else ""
        if arguments[:2] == ["docker", "inspect"]:
            return json.dumps(INGRESS)
        self.events.append(" ".join(arguments[:3]))
        return ""

    def preflight(self, manifest):
        self.events.append("preflight")

    def pull(self, bundle, registry=None, unreadable=()):
        RenderDocker.pulled = registry
        RenderDocker.unreadable = unreadable
        self.events.append("pull")
        return {"images": 1, "fetched": 1, "signedIn": registry["credential"] if registry else None}

    def up(self, bundle, recreate=None):
        self.events.append("up")
        # The environment points each service at its rendered file.
        RenderDocker.settings = {name: value for name, value in self.environment.items() if name.endswith("_SETTINGS")}

    def up_services(self, bundle, services):
        self.events.append("up:" + ",".join(services))

    def verify(self, bundle, manifest):
        self.events.append("verify")

    def unhealthy(self, bundle):
        return []

    def service_health(self, bundle):
        return {}


class RenderTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.config = self.root / "srv" / "config" / "pilot-dev"
        self.config.mkdir(parents=True, mode=0o700)
        (self.root / "srv" / "config").chmod(0o700)
        host = self.config / "api.json"
        host.write_text(json.dumps({"Database": {"Connection": "HOST_SECRET"}, "AllowedHosts": "old.example;localhost",
                                    "Logging": {"Level": "Information"}}))
        host.chmod(0o600)
        self.target = {"product": "pilot", "environment": "dev", "root": str(self.root / "srv"), "render": True,
                       "settings": {"api": str(host)}, "minimumFreeBytes": 0,
                       "variables": {"PLATFORM_NETWORK": "platform"},
                       "ingress": {"scheme": "https", "entryPoints": ["web"], "privateEntryPoints": ["private"],
                                   "hosts": {"app": "app.pilot.example", "admin": "admin.pilot.example"}}}
        self.bundle = self.root / "bundle"
        self.bundle.mkdir()
        image = "ghcr.io/owner/pilot/api@sha256:" + "a" * 64
        worker = "ghcr.io/owner/pilot/worker@sha256:" + "b" * 64
        self.manifest = {"schemaVersion": 2, "product": "pilot", "release": "v1", "sourceCommit": "a" * 40,
                         "platform": "linux/amd64", "rollbackCompatible": False,
                         "images": {"api": image, "worker": worker},
                         "requiredConfiguration": {"api": ["Database:Connection", "Billing:SecretKey", HOSTS,
                                                           runner.PROXIES],
                                                   "worker": ["ConnectionStrings:Database"]},
                         "needs": {"worker": ["postgres"]},
                         "sites": {"app": {"primary": True}, "admin": {"exposure": "private"}},
                         "routes": {"api": {"app": {"paths": ["/api"]}, "admin": {"paths": ["/"]}}}}
        self.compose = {"services": {name: {"image": value, "platform": "linux/amd64",
                                            "healthcheck": {"test": ["CMD", "true"]}}
                                     for name, value in self.manifest["images"].items()}}
        self.save()
        RenderDocker.events, RenderDocker.ingress, RenderDocker.pulled = [], True, None

    def tearDown(self):
        self.temporary.cleanup()

    def save(self):
        compose = json.dumps(self.compose).encode()
        (self.bundle / "compose.json").write_bytes(compose)
        self.manifest["composeSha256"] = hashlib.sha256(compose).hexdigest()
        (self.bundle / "release.json").write_text(json.dumps(self.manifest))

    def database(self):
        path = self.config / "database.json"
        path.write_text(json.dumps({"ConnectionString": "Host=wheelhouse-postgres;Database=pilot_dev;Password=DB_SECRET"}))
        path.chmod(0o600)

    def apply(self, values=None, registry_login=None, unreadable=None):
        provided = {"values": values if values is not None else {"Billing:SecretKey": "VAULT_SECRET"}}
        if registry_login:
            provided["registry"] = registry_login
        if unreadable is not None:
            provided["unreadable"] = unreadable
        return runner.apply(self.bundle, self.target, "max", docker_factory=RenderDocker, provided=provided)

    def rendered(self, service):
        return json.loads((self.config / (service + ".target.json")).read_text())

    def test_a_render_layers_host_file_wheelhouse_values_and_derived_keys(self):
        self.database()
        result = self.apply()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        api = self.rendered("api")
        # The host file is the bottom layer; Wheelhouse's values and the derived keys go over it.
        self.assertEqual("HOST_SECRET", api["Database"]["Connection"])
        self.assertEqual("Information", api["Logging"]["Level"])
        self.assertEqual("VAULT_SECRET", api["Billing"]["SecretKey"])
        # The derived hosts first, then any other host the host file listed, localhost last.
        self.assertEqual("admin.pilot.example;app.pilot.example;pilot-dev-api;old.example;localhost", api["AllowedHosts"])
        self.assertEqual(["172.30.0.2"], api["Deployment"]["TrustedProxies"])
        self.assertEqual({"admin": {"Url": "https://admin.pilot.example"}, "app": {"Url": "https://app.pilot.example"}},
                         api["Sites"])
        # The worker serves no site: only its platform name and localhost; its database fills its connection key.
        worker = self.rendered("worker")
        self.assertEqual("pilot-dev-worker;localhost", worker["AllowedHosts"])
        self.assertEqual("Host=wheelhouse-postgres;Database=pilot_dev;Password=DB_SECRET",
                         worker["ConnectionStrings"]["Database"])
        # A key no service declares goes to every service: Billing:SecretKey is declared by api only.
        self.assertNotIn("Billing", worker)
        for service in ("api", "worker"):
            path = self.config / (service + ".target.json")
            self.assertEqual(0o444, stat.S_IMODE(path.stat().st_mode))
            self.assertEqual(str(path), RenderDocker.settings[service.upper() + "_SETTINGS"])
        self.assertEqual(0o700, stat.S_IMODE(self.config.stat().st_mode))
        step = result["steps"][0]
        self.assertEqual(("Render settings", "succeeded"), (step["name"], step["status"]))
        self.assertIn("2 services", step["detail"])
        self.assertEqual({"host": ["AllowedHosts", "Database:Connection", "Logging:Level"],
                          "provided": ["Billing:SecretKey"],
                          "derived": ["AllowedHosts", "Sites:admin:Url", "Sites:app:Url", runner.PROXIES]},
                         result["settings"]["api"])
        self.assertEqual(["ConnectionStrings:Database"], result["settings"]["worker"]["database"])
        record = json.dumps(result)
        for secret in ("HOST_SECRET", "VAULT_SECRET", "DB_SECRET"):
            self.assertNotIn(secret, record)
            self.assertNotIn(secret, json.dumps(json.loads((self.root / "srv/pilot-dev/active.json").read_text())))

    def test_a_missing_required_key_refuses_the_deploy_before_any_file_changes(self):
        with self.assertRaisesRegex(runner.RenderRefused, "Missing required setting: worker:ConnectionStrings:Database"):
            self.apply()
        self.assertFalse((self.config / "api.target.json").exists())
        self.assertEqual([], RenderDocker.events)
        self.database()
        with self.assertRaisesRegex(ValueError, "Missing required setting: api:Billing:SecretKey"):
            self.apply(values={})

    def test_a_required_key_set_by_wheelhouse_wins_over_the_database_and_values_reach_declaring_services(self):
        self.database()
        self.manifest["requiredConfiguration"]["worker"].append("Shared:Key")
        self.manifest["requiredConfiguration"]["api"].append("Shared:Key")
        self.save()
        self.apply(values={"Billing:SecretKey": "x", "Shared:Key": "both", "ConnectionStrings:Database": "STORED",
                           "Feature:Flag": "true"})
        worker, api = self.rendered("worker"), self.rendered("api")
        self.assertEqual("STORED", worker["ConnectionStrings"]["Database"])
        self.assertEqual(("both", "both"), (worker["Shared"]["Key"], api["Shared"]["Key"]))
        self.assertEqual(("true", "true"), (worker["Feature"]["Flag"], api["Feature"]["Flag"]))

    def test_an_identical_render_keeps_the_file_and_a_changed_one_restarts_its_service(self):
        self.database()
        self.apply()
        path = self.config / "api.target.json"
        inode = path.stat().st_ino
        second = self.apply()
        self.assertEqual(inode, path.stat().st_ino)
        self.assertIn("unchanged", second["steps"][0]["detail"])
        third = self.apply(values={"Billing:SecretKey": "rotated"})
        self.assertNotEqual(inode, path.stat().st_ino)
        changed = {service["name"]: service["reason"] for service in third["serviceRollout"]["services"]}
        self.assertEqual({"api": "settings", "worker": None}, changed)

    def test_a_target_without_an_ingress_or_host_file_still_renders(self):
        self.database()
        self.target.pop("ingress")
        self.target["settings"] = {}
        RenderDocker.ingress = False
        self.manifest["requiredConfiguration"]["api"] = ["Billing:SecretKey"]
        self.save()
        result = self.apply()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        api = self.rendered("api")
        self.assertEqual("pilot-dev-api;localhost", api["AllowedHosts"])
        self.assertNotIn("Deployment", api)

    def test_a_folder_others_can_read_is_refused(self):
        self.database()
        self.config.chmod(0o755)
        with self.assertRaisesRegex(ValueError, "settings folder must be a private directory"):
            self.apply()

    def test_a_target_that_does_not_render_mounts_its_host_file(self):
        self.target.pop("render")
        self.manifest["requiredConfiguration"] = {"api": ["Database:Connection"]}
        self.manifest.pop("needs")
        self.save()
        self.target["settings"]["worker"] = self.target["settings"]["api"]
        self.manifest["requiredConfiguration"]["worker"] = []
        self.save()
        result = self.apply(values={})
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertNotIn("settings", result)
        self.assertEqual(self.target["settings"]["api"], RenderDocker.settings["API_SETTINGS"])
        self.assertFalse((self.config / "api.target.json").exists())

    def test_the_registry_sign_in_reaches_the_pull_and_never_the_record(self):
        self.database()
        login = {"server": "ghcr.io", "username": "x-access-token", "password": "ghs_TOKEN", "credential": "installation"}
        result = self.apply(registry_login=login)
        self.assertEqual(login, RenderDocker.pulled)
        step = next(item for item in result["steps"] if item["name"] == "Pull images")
        self.assertIn("signed in for this pull with the GitHub App's token", step["detail"])
        self.assertNotIn("ghs_TOKEN", json.dumps(result))
        for bad in ({**login, "password": "two words"}, {**login, "credential": "borrowed"}, {**login, "server": "x y"}):
            with self.subTest(bad=bad), self.assertRaisesRegex(ValueError, "Invalid registry sign-in"):
                self.apply(registry_login=bad)

    def test_what_nothing_could_read_reaches_the_pull_and_bad_lists_are_refused(self):
        self.database()
        result = self.apply(unreadable=["ghcr.io/ocharo-hq/ocharo-assets/motion"])
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertEqual(("ghcr.io/ocharo-hq/ocharo-assets/motion",), RenderDocker.unreadable)
        for bad in ("ghcr.io/x", ["two words"], ["ghcr.io/Upper/Case"], [5]):
            with self.subTest(bad=bad), self.assertRaisesRegex(ValueError, "Invalid unreadable images"):
                self.apply(unreadable=bad)

    def test_declarations_name_required_optional_secret_and_derived_keys(self):
        self.manifest["settings"] = {"api": {"optional": {"Feature:Flag": "false"}, "secrets": ["Database:Connection"],
                                             "allowed": {"Feature:Flag": ["true", "false"]}}}
        self.save()
        declared = runner.settings_declarations(self.target, runner.validate_bundle(self.bundle))
        api = {item["key"]: item for item in declared["services"][0]["keys"]}
        self.assertEqual("api", declared["services"][0]["name"])
        self.assertTrue(declared["services"][0]["hostFile"])
        self.assertEqual({"key": "Database:Connection", "required": True, "secret": True}, api["Database:Connection"])
        self.assertTrue(api["Billing:SecretKey"]["secret"])
        self.assertEqual({"key": "Feature:Flag", "required": False, "secret": False, "default": "false",
                          "allowed": ["true", "false"]}, api["Feature:Flag"])
        self.assertEqual(("hosts", True, "admin.pilot.example;app.pilot.example;pilot-dev-api;localhost"),
                         (api[HOSTS]["derived"], api[HOSTS]["required"], api[HOSTS]["value"]))
        self.assertEqual(("proxies", False), (api[runner.PROXIES]["derived"], api[runner.PROXIES]["secret"]))
        self.assertEqual(("sites", "https://app.pilot.example"), (api["Sites:app:Url"]["derived"], api["Sites:app:Url"]["value"]))
        worker = {item["key"]: item for item in declared["services"][1]["keys"]}
        self.assertEqual("database", worker["ConnectionStrings:Database"]["derived"])
        # Required keys come first.
        self.assertEqual([True] * 4 + [False] * 3, [item["required"] for item in declared["services"][0]["keys"]])

    def test_typed_keys_carry_their_type_and_format_and_count_as_declared(self):
        self.manifest["settings"] = {"api": {
            "optional": {"Billing:Mode": "test"}, "secrets": ["Stripe:ApiKey"], "allowed": {"Billing:Mode": ["test", "live"]},
            "types": {"Billing:Mode": {"type": "choice"}, "Stripe:ApiKey": {"type": "text"},
                      "Promo:EndsOn": {"type": "date", "format": "dd.MM.yyyy"}, "Support:TokenUrl": {"type": "url"}}}}
        self.save()
        declared = runner.settings_declarations(self.target, runner.validate_bundle(self.bundle))
        api = {item["key"]: item for item in declared["services"][0]["keys"]}
        self.assertEqual({"key": "Billing:Mode", "required": False, "secret": False, "default": "test",
                          "allowed": ["test", "live"], "type": "choice"}, api["Billing:Mode"])
        self.assertEqual({"key": "Promo:EndsOn", "required": False, "secret": False, "type": "date", "format": "dd.MM.yyyy"},
                         api["Promo:EndsOn"])
        # A declared secret stays one; a typed key the release calls plain is not guessed a secret from its name.
        self.assertEqual((True, "text"), (api["Stripe:ApiKey"]["secret"], api["Stripe:ApiKey"]["type"]))
        self.assertFalse(api["Support:TokenUrl"]["secret"])
        # An untyped required key keeps the name guess and carries no type.
        self.assertTrue(api["Billing:SecretKey"]["secret"])
        self.assertNotIn("type", api["Billing:SecretKey"])

    def test_setting_types_are_validated(self):
        for types in ({"A:B": {"type": "colour"}}, {"A:B": "text"}, {"Bad Key": {"type": "text"}},
                      {"A:B": {"type": "text", "format": "yyyy"}}, {"A:B": {"type": "date", "format": "%Y"}},
                      {"A:B": {"type": "date", "extra": 1}}):
            with self.subTest(types=types), self.assertRaises(ValueError):
                runner.release_settings({**self.manifest, "settings": {"api": {"types": types}}})

    def test_a_baseline_host_reaches_the_derived_hosts_and_site_urls(self):
        self.database()
        self.target["ingress"] = {"scheme": "https", "entryPoints": ["websecure"], "privateEntryPoints": ["private"],
                                  "certResolver": "letsencrypt", "hosts": {"admin": "admin.pilot.example"},
                                  "publicAddress": "203.0.113.10"}
        result = self.apply()
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        api = self.rendered("api")
        self.assertEqual("admin.pilot.example;pilot-dev.203-0-113-10.sslip.io;pilot-dev-api;old.example;localhost",
                         api["AllowedHosts"])
        self.assertEqual({"admin": {"Url": "https://admin.pilot.example"},
                          "app": {"Url": "https://pilot-dev.203-0-113-10.sslip.io"}}, api["Sites"])
        declared = {item["key"]: item for item in runner.settings_declarations(self.target, self.manifest)["services"][0]["keys"]}
        self.assertIn("pilot-dev.203-0-113-10.sslip.io", declared[HOSTS]["value"])

    def test_derived_hosts_keep_a_host_files_other_hosts_once(self):
        self.assertEqual("a.example;p-api;B.example;localhost",
                         runner.with_hosts("a.example;p-api;localhost", "B.example;a.example;localhost; b.example"))
        self.assertEqual("p-api;localhost", runner.with_hosts("p-api;localhost", None))

    def test_release_settings_declarations_are_validated(self):
        for declared in ({"ghost": {}}, {"api": {"optional": {"Bad Key": "x"}}}, {"api": {"secrets": "Key"}},
                         {"api": {"allowed": {"Key": []}}}, {"api": {"extra": 1}}):
            with self.subTest(declared=declared), self.assertRaises(ValueError):
                runner.release_settings({**self.manifest, "settings": declared})

    def test_the_secret_guess_reads_the_last_part_of_a_key(self):
        for key in ("Billing:SecretKey", "ImageWorker:Key", "Auth:ClientSecret", "Smtp:Password", "ConnectionStrings:Db",
                    "Database:ConnectionString", "Github:Token"):
            self.assertTrue(runner.is_secret_key(key), key)
        for key in ("DataProtection:KeysPath", "DataProtection:ApplicationName", "OcharoPlatform:BaseUrl",
                    "Auth:Google:ClientId", "Billing:Prices:Solo"):
            self.assertFalse(runner.is_secret_key(key), key)

    def test_the_check_names_a_required_key_no_layer_sets(self):
        self.database()
        manifest = runner.validate_bundle(self.bundle)
        result = runner.check(self.target, manifest, docker_factory=CheckDocker, provided={"keys": []})
        failing = {item["name"]: item["detail"] for item in result["checks"] if not item["ok"]}
        self.assertEqual({"Settings: api": "Missing required setting: api:Billing:SecretKey"}, failing)
        passed = runner.check(self.target, manifest, docker_factory=CheckDocker, provided={"keys": ["Billing:SecretKey"]})
        details = {item["name"]: item["detail"] for item in passed["checks"]}
        self.assertTrue(passed["ok"], details)
        self.assertEqual("rendered at deploy from derived keys, Wheelhouse, its database", details["Settings: worker"])
        unknown = runner.check(self.target, manifest, docker_factory=CheckDocker)
        self.assertTrue(unknown["ok"])
        self.assertIn("required keys are checked at deploy",
                      next(item["detail"] for item in unknown["checks"] if item["name"] == "Settings: api"))

    def test_launch_hands_the_values_to_its_worker_over_a_pipe(self):
        target = self.root / "target.json"
        target.write_text(json.dumps(self.target))
        spawned = {}

        class FakePipe(io.BytesIO):
            def close(self):
                spawned["stdin"] = self.getvalue()
                super().close()

        class FakeWorker:
            def __init__(self, arguments, **options):
                spawned.update(arguments=arguments, stdin_mode=options["stdin"])
                self.stdin = FakePipe()

        provided = {"values": {"Billing:SecretKey": "VAULT_SECRET"}}
        with patch.object(runner.subprocess, "Popen", FakeWorker):
            queued = runner.launch(self.bundle, target, "max", provided)
        self.assertEqual(subprocess.PIPE, spawned["stdin_mode"])
        self.assertIn("--stdin", spawned["arguments"])
        self.assertNotIn("VAULT_SECRET", " ".join(spawned["arguments"]))
        self.assertEqual(provided, json.loads(spawned["stdin"]))
        on_disk = [path for path in (self.root / "srv").rglob("*") if path.is_file()
                   and "VAULT_SECRET" in path.read_text(errors="replace")]
        self.assertEqual([], on_disk)
        self.assertEqual("queued", queued["status"])


class CheckDocker:
    def __init__(self, target, environment):
        pass

    def execute(self, arguments, step, timeout=600):
        if arguments[:2] == ["docker", "info"]:
            return "linux/amd64\n"
        if arguments[:3] == ["docker", "compose", "version"]:
            return "2.29.0\n"
        if arguments[:2] == ["docker", "ps"]:
            return "ing\n"
        if arguments[:2] == ["docker", "inspect"]:
            return json.dumps(INGRESS)
        return "platform\n"


HOSTS = runner.HOSTS


class RegistryPullTests(unittest.TestCase):
    """The real Docker adapter's pull, with each command scripted: the sign-in is made only for a missing private image,
    in a temporary config that is signed out of and deleted, with the password on stdin."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.bundle = Path(self.temporary.name)
        self.images = {"api": "ghcr.io/owner/pilot/api@sha256:" + "a" * 64,
                       "edge": "localhost:15000/pilot-edge@sha256:" + "b" * 64}
        (self.bundle / "release.json").write_text(json.dumps({"images": self.images}))
        (self.bundle / "compose.json").write_text(json.dumps({"services": {"api": {}, "edge": {}}}))
        self.calls, self.present = [], set()
        self.login = {"server": "ghcr.io", "username": "x-access-token", "password": "ghs_TOKEN",
                      "credential": "installation"}

    def tearDown(self):
        self.temporary.cleanup()

    def fake_run(self, arguments, **options):
        self.calls.append((arguments, options.get("input")))
        if "--config" in arguments:
            config = Path(arguments[arguments.index("--config") + 1])
            self.assertTrue(config.is_dir())
            self.assertEqual(0o700, stat.S_IMODE(config.stat().st_mode))
            self.configs.add(config)
        code = 0
        if arguments[1:3] == ["image", "inspect"]:
            code = 0 if arguments[-1] in self.present else 1
        return subprocess.CompletedProcess(arguments, code, "", "")

    def pull(self, login):
        self.configs = set()
        docker = runner.Docker({"product": "pilot", "environment": "dev"}, {})
        with patch.object(runner.subprocess, "run", side_effect=self.fake_run):
            return docker.pull(self.bundle, login)

    def test_a_missing_private_image_pulls_under_a_one_pull_sign_in(self):
        result = self.pull(self.login)
        self.assertEqual({"images": 2, "fetched": 2, "signedIn": "installation"}, result)
        commands = [" ".join(arguments[:2] + [arguments[3]] if arguments[1] == "--config" else arguments[:3])
                    for arguments, _ in self.calls]
        self.assertIn("docker --config login", commands)
        login = next((arguments, given) for arguments, given in self.calls if "login" in arguments)
        self.assertEqual("ghs_TOKEN", login[1])
        self.assertNotIn("ghs_TOKEN", " ".join(login[0]))
        self.assertIn("--password-stdin", login[0])
        order = [arguments[3] if arguments[1] == "--config" else arguments[1] for arguments, _ in self.calls]
        self.assertEqual(["login", "pull", "logout"], [item for item in order if item in ("login", "pull", "logout")])
        # Only the GHCR image pulls signed in; Compose fetches the rest, and the temporary config is gone.
        signed = [arguments[-1] for arguments, _ in self.calls if arguments[1] == "--config" and arguments[3] == "pull"]
        self.assertEqual([self.images["api"]], signed)
        self.assertTrue(any(arguments[-2:] == ["--policy", "missing"] for arguments, _ in self.calls))
        self.assertTrue(self.configs and all(not config.exists() for config in self.configs))

    def test_images_on_the_host_need_no_sign_in(self):
        self.present = set(self.images.values())
        self.assertEqual({"images": 2, "fetched": 0, "signedIn": None}, self.pull(self.login))
        self.assertFalse(any("login" in arguments for arguments, _ in self.calls))

    def test_a_missing_image_nothing_can_read_fails_the_pull_naming_its_package(self):
        self.configs = set()
        docker = runner.Docker({"product": "pilot", "environment": "dev"}, {})
        with patch.object(runner.subprocess, "run", side_effect=self.fake_run), \
                self.assertRaisesRegex(runner.CommandFailed, "No credential on the control host can read "
                                                             "ghcr.io/owner/pilot/api: .*read:packages.*GitHubTokenFile") \
                as raised:
            docker.pull(self.bundle, self.login, ("ghcr.io/owner/pilot/api",))
        self.assertLessEqual(len(runner.reason(raised.exception)), 300)
        self.assertFalse(any("login" in arguments or "pull" in arguments for arguments, _ in self.calls))
        # On the host already, it needs no pull, so nothing fails.
        self.calls, self.present = [], {self.images["api"]}
        with patch.object(runner.subprocess, "run", side_effect=self.fake_run):
            result = docker.pull(self.bundle, None, ("ghcr.io/owner/pilot/api",))
        self.assertEqual({"images": 2, "fetched": 1, "signedIn": None}, result)

    def test_an_image_reference_names_its_repository(self):
        for image, repository in (("ghcr.io/Owner/pilot/api@sha256:" + "a" * 64, "ghcr.io/owner/pilot/api"),
                                  ("ghcr.io/owner/api:1.2.0", "ghcr.io/owner/api"),
                                  ("localhost:15000/pilot-edge:v1@sha256:" + "b" * 64, "localhost:15000/pilot-edge")):
            self.assertEqual(repository, runner.image_repository(image))

    def test_a_refused_sign_in_fails_the_pull_and_still_cleans_up(self):
        def refuse(arguments, **options):
            result = self.fake_run(arguments, **options)
            return subprocess.CompletedProcess(arguments, 1, "", "") if "login" in arguments else result
        self.configs = set()
        docker = runner.Docker({"product": "pilot", "environment": "dev"}, {})
        with patch.object(runner.subprocess, "run", side_effect=refuse), \
                self.assertRaisesRegex(runner.CommandFailed, "sign-in to ghcr.io failed"):
            docker.pull(self.bundle, self.login)
        self.assertTrue(all(not config.exists() for config in self.configs))


class FakeResponse:
    def __init__(self, status, body=b""):
        self.status, self.body = status, body

    def read(self, limit=-1):
        return self.body

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False


class GhcrFake:
    """GHCR as the 2026-10-24 deploy met it: the token service grants every credential a pull of any repository of its
    owner, and the registry answers a manifest HEAD only where the grant can read: a public package for anyone, a private
    repository's package for the classic token only (the App's token gets a 404)."""

    def __init__(self, private=("ocharo-hq/ocharo-assets/motion",), down=False):
        self.private, self.down, self.requests = set(private), down, []

    def open(self, request, timeout=None):
        self.requests.append(request)
        url = urllib.parse.urlparse(request.full_url)
        if self.down:
            raise urllib.error.URLError("unreachable")
        if url.path == "/token":
            scope = urllib.parse.parse_qs(url.query)["scope"][0]
            basic = request.get_header("Authorization")
            who = base64.b64decode(basic.split(" ", 1)[1]).decode().split(":", 1)[1] if basic else "anonymous"
            return FakeResponse(200, json.dumps({"token": "grant:" + who + ":" + scope.split(":")[1]}).encode())
        _, who, repository = request.get_header("Authorization").split(" ", 1)[1].split(":", 2)
        if repository in self.private and who != "ghp_FILE":
            raise urllib.error.HTTPError(request.full_url, 404, "Not Found", {}, None)
        return FakeResponse(200)


class RegistryChoiceTests(unittest.TestCase):
    """The control host's choice of credential: GHCR is asked first, the App's token before the token file, and a
    credential counts only when it can read each image's manifest."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        root = Path(self.temporary.name)
        tokens = root / "tokens.json"
        tokens.write_text(json.dumps({"owners": {"ocharo-hq": {"token": "ghs_APP", "expiresAt": "2999-01-01T00:00:00Z"}}}))
        self.fallback = root / "token"
        self.fallback.write_text("ghp_FILE\n")
        self.environment = patch.dict(os.environ, {"WHEELHOUSE_GITHUB_TOKENS_FILE": str(tokens),
                                                   "WHEELHOUSE_GITHUB_TOKEN_FILE": str(self.fallback)})
        self.environment.start()
        self.images = ["ghcr.io/ocharo-hq/ocharo-platform/desk@sha256:" + "a" * 64,
                       "ghcr.io/Ocharo-HQ/ocharo-platform/jobs:1.2.0"]
        self.ocharo = self.images + ["ghcr.io/ocharo-hq/ocharo-assets/motion@sha256:" + "8" * 64]

    def tearDown(self):
        self.environment.stop()
        self.temporary.cleanup()

    def test_the_app_token_goes_first_when_it_reads_every_manifest(self):
        asked = []

        def check(image, user, password):
            asked.append((image, user))
            return True, 200
        login, attempt = registry.sign_in_for(self.images, check=check, login=lambda token: "machine")
        self.assertEqual({"server": "ghcr.io", "username": "x-access-token", "password": "ghs_APP",
                          "credential": "installation"}, login)
        self.assertEqual({"server": "ghcr.io", "credential": "installation", "refused": []}, attempt)
        self.assertEqual([(("ocharo-hq/ocharo-platform/desk", "sha256:" + "a" * 64), "x-access-token"),
                          (("ocharo-hq/ocharo-platform/jobs", "1.2.0"), "x-access-token")], asked)

    def test_a_granted_pull_that_cannot_read_the_manifest_does_not_count(self):
        # The token service says yes to the App's token for the private package; only the manifest HEAD says no.
        ghcr = GhcrFake()
        with patch.object(registry, "OPENER", ghcr):
            self.assertEqual((False, 404), registry.accepts(("ocharo-hq/ocharo-assets/motion", "sha256:" + "8" * 64),
                                                            "x-access-token", "ghs_APP"))
            self.assertEqual((True, 200), registry.accepts(("ocharo-hq/ocharo-assets/motion", "sha256:" + "8" * 64),
                                                           "machine", "ghp_FILE"))
            self.assertEqual((True, 200), registry.accepts(("ocharo-hq/ocharo-studio/studio", "v1"), None, None))
            # An inventory image names no digest or tag: the repository's tag listing answers instead.
            self.assertEqual((False, 404), registry.accepts(("ocharo-hq/ocharo-assets/motion", None), "x-access-token",
                                                            "ghs_APP"))
            self.assertTrue(ghcr.requests[-1].full_url.endswith("/v2/ocharo-hq/ocharo-assets/motion/tags/list?n=1"))
            login, attempt = registry.sign_in_for(self.ocharo, login=lambda token: "machine")
        self.assertEqual(("file", "machine", "ghp_FILE"), (login["credential"], login["username"], login["password"]))
        self.assertEqual({"server": "ghcr.io", "credential": "file", "refused": ["installation"]}, attempt)
        head = next(request for request in ghcr.requests if request.get_method() == "HEAD" and "/desk/" in request.full_url)
        self.assertEqual("https://ghcr.io/v2/ocharo-hq/ocharo-platform/desk/manifests/sha256:" + "a" * 64, head.full_url)
        self.assertIn("application/vnd.oci.image.index.v1+json", head.get_header("Accept"))

    def test_a_private_package_nothing_can_read_is_named_and_the_rest_pull_anonymously(self):
        self.fallback.unlink()
        with patch.object(registry, "OPENER", GhcrFake()):
            login, attempt = registry.sign_in_for(self.ocharo, login=lambda token: "machine")
        self.assertIsNone(login)
        self.assertEqual({"server": "ghcr.io", "credential": None, "refused": ["installation"],
                          "unreadable": [{"repository": "ocharo-hq/ocharo-assets/motion", "status": 404}]}, attempt)
        self.assertEqual(["ghcr.io/ocharo-hq/ocharo-assets/motion"], registry.unreadable_images(attempt))
        line = registry.describe(attempt)
        self.assertIn("ghcr.io/ocharo-hq/ocharo-assets/motion", line)
        self.assertIn("read:packages", line)
        self.assertNotIn("ghs_APP", json.dumps(attempt) + line)

    def test_no_answer_from_ghcr_names_nothing_unreadable(self):
        with patch.object(registry, "OPENER", GhcrFake(down=True)):
            login, attempt = registry.sign_in_for(self.ocharo, login=lambda token: "machine")
        self.assertIsNone(login)
        self.assertEqual({"server": "ghcr.io", "credential": None, "refused": ["installation", "file"]}, attempt)
        self.assertEqual([], registry.unreadable_images(attempt))

    def test_a_refused_app_token_falls_back_to_the_token_file(self):
        login, attempt = registry.sign_in_for(
            self.images, check=lambda image, user, password: (password == "ghp_FILE", 200 if password == "ghp_FILE" else 403),
            login=lambda token: "machine")
        self.assertEqual(("file", "machine", "ghp_FILE"), (login["credential"], login["username"], login["password"]))
        self.assertEqual(["installation"], attempt["refused"])
        self.assertNotIn("ghp_FILE", json.dumps(attempt))

    def test_public_or_foreign_images_need_no_sign_in(self):
        # Nothing signed reads them, yet an anonymous pull does: public packages, no sign-in, nothing unreadable.
        login, attempt = registry.sign_in_for(self.images, check=lambda image, user, password: (user is None, 403),
                                              login=lambda token: None)
        self.assertIsNone(login)
        self.assertEqual({"server": "ghcr.io", "credential": None, "refused": ["installation", "file"]}, attempt)
        self.assertEqual((None, None), registry.sign_in_for(["localhost:15000/api@sha256:" + "a" * 64]))
        # Images of two owners skip the App: one sign-in covers one registry.
        mixed = self.images + ["ghcr.io/other/api@sha256:" + "c" * 64]
        _, attempt = registry.sign_in_for(mixed, check=lambda *_: (True, 200), login=lambda token: None)
        self.assertEqual("file", attempt["credential"])

    def test_the_packages_nothing_reads_are_noted_for_the_issues_panel_and_cleared_once_read(self):
        root = Path(self.temporary.name)
        attempt = {"server": "ghcr.io", "credential": None, "refused": ["installation"],
                   "unreadable": [{"repository": "ocharo-hq/ocharo-assets/motion", "status": 404}]}
        registry.note_packages(root, "ocharo", self.ocharo, attempt)
        notes = json.loads((root / "github" / "packages.json").read_text())["packages"]
        self.assertEqual({"ocharo-hq/ocharo-assets/motion"}, set(notes))
        self.assertEqual((404, "ocharo"), (notes["ocharo-hq/ocharo-assets/motion"]["status"],
                                           notes["ocharo-hq/ocharo-assets/motion"]["product"]))
        registry.note_packages(root, "ocharo", self.ocharo, {"server": "ghcr.io", "credential": "file", "refused": []})
        self.assertEqual({}, json.loads((root / "github" / "packages.json").read_text())["packages"])

    def test_the_probe_reports_statuses_per_product_and_never_a_token(self):
        product = transport.inventory.catalog.Product(
            "ocharo-platform", "Ocharo", "", "ocharo-hq/ocharo-platform", "main",
            transport.inventory.catalog.Release("asset.tar.gz", (("desk", "ghcr.io/ocharo-hq/ocharo-platform/desk"),), None))
        with patch.object(registry.catalog, "products", return_value=(product,)):
            result = registry.probe(self.temporary.name, check=lambda image, user, password: (
                password == "ghs_APP", 200 if password == "ghs_APP" else 403), login=lambda token: "machine")
        item = result["products"][0]
        credentials = item["credentials"]
        self.assertEqual([("installation", True), ("file", False), ("anonymous", False)],
                         [(entry["credential"], entry["accepted"]) for entry in credentials])
        self.assertEqual((403, None), (credentials[1]["repositories"][0]["status"],
                                       credentials[1]["repositories"][0]["reference"]))
        self.assertEqual("installation", item["deploy"]["credential"])
        self.assertEqual("GHCR images read with the GitHub App's token", item["summary"])
        self.assertNotIn("ghs_APP", json.dumps(result))
        self.assertNotIn("ghp_FILE", json.dumps(result))


class SubmitTests(unittest.TestCase):
    """The control host's submission: values and the sign-in ride the launch's stdin; the job keeps neither."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        for name in ("key", "known_hosts"):
            (self.root / name).touch()
        bundle = self.root / "bundles" / "pilot-1"
        bundle.mkdir(parents=True)
        image = "ghcr.io/o/api@sha256:" + "a" * 64
        compose = json.dumps({"services": {"api": {"image": image, "platform": "linux/amd64",
                                                   "healthcheck": {"test": ["CMD", "true"]}}}}).encode()
        (bundle / "compose.json").write_bytes(compose)
        (bundle / "release.json").write_text(json.dumps({
            "schemaVersion": 1, "product": "pilot", "release": "v1", "sourceCommit": "a" * 40, "platform": "linux/amd64",
            "rollbackCompatible": False, "composeSha256": hashlib.sha256(compose).hexdigest(), "images": {"api": image},
            "requiredConfiguration": {"api": ["Billing:SecretKey"]}}))
        self.bundle = bundle
        self.config = {"serverId": "pilot-host", "provider": "ovhcloud", "acceptsCandidates": True,
                       "needsConfirmation": False, "requiresDevPass": False,
                       "ssh": {"host": "vps.example.net", "user": "deploy", "port": 22, "keyFile": str(self.root / "key"),
                               "knownHostsFile": str(self.root / "known_hosts")},
                       "target": {"product": "pilot", "environment": "dev", "root": "/srv/wheelhouse", "render": True,
                                  "settings": {}, "variables": {"PLATFORM_NETWORK": "platform"}}}

    def tearDown(self):
        self.temporary.cleanup()

    def test_values_and_the_sign_in_travel_on_stdin_and_stay_out_of_the_job(self):
        sent = []

        class FakeSsh:
            def __init__(self, ssh_config):
                pass

            def run(self, command, stdin=None, timeout=30):
                sent.append((command, stdin))
                return json.dumps({"id": "55555555-5555-4555-8555-555555555555", "status": "queued"}) if "launch" in command else ""

            def copy(self, files, destination):
                sent.append(("copy", [Path(item).name for item in files]))

        login = {"server": "ghcr.io", "username": "x-access-token", "password": "ghs_APP", "credential": "installation"}
        attempt = {"server": "ghcr.io", "credential": "installation", "refused": []}
        with patch.object(transport.fleet, "resolve_target", return_value=self.config), \
                patch.object(transport.artifacts, "prepare", return_value=self.bundle), \
                patch.object(transport.registry, "sign_in_for", return_value=(login, attempt)), \
                patch.object(transport, "Ssh", FakeSsh):
            submitted = transport.submit(self.root, "pilot-dev", "pilot-1", "max",
                                         provided={"values": {"Billing:SecretKey": "VAULT_SECRET"}})
        command, stdin = sent[-1]
        self.assertIn("--stdin", command)
        self.assertNotIn("VAULT_SECRET", command)
        self.assertEqual({"values": {"Billing:SecretKey": "VAULT_SECRET"}, "registry": login}, json.loads(stdin))
        record = (self.root / "jobs" / (submitted["id"] + ".json")).read_text()
        self.assertEqual(attempt, json.loads(record)["registry"])
        self.assertNotIn("VAULT_SECRET", record)
        self.assertNotIn("ghs_APP", record)
        self.assertEqual(["runner.py", "release.json", "compose.json", "target.json"], sent[1][1])

    def test_an_image_nothing_can_read_travels_on_stdin_and_fails_the_readiness_check(self):
        sent = []

        class FakeSsh:
            def __init__(self, ssh_config):
                pass

            def run(self, command, stdin=None, timeout=30):
                sent.append((command, stdin))
                return json.dumps({"id": "55555555-5555-4555-8555-555555555555", "status": "queued"}) if "launch" in command else ""

            def copy(self, files, destination):
                pass

        attempt = {"server": "ghcr.io", "credential": None, "refused": ["installation"],
                   "unreadable": [{"repository": "o/api", "status": 404}]}
        with patch.object(transport.fleet, "resolve_target", return_value=self.config), \
                patch.object(transport.artifacts, "prepare", return_value=self.bundle), \
                patch.object(transport.registry, "sign_in_for", return_value=(None, attempt)), \
                patch.object(transport, "Ssh", FakeSsh):
            transport.submit(self.root, "pilot-dev", "pilot-1", "max", provided={"values": {"Billing:SecretKey": "S"}})
            check = transport.registry_check(self.root, "pilot", transport.validate_bundle(self.bundle))
        self.assertEqual({"values": {"Billing:SecretKey": "S"}, "unreadable": ["ghcr.io/o/api"]}, json.loads(sent[-1][1]))
        self.assertEqual(("Registry access", False), (check["name"], check["ok"]))
        self.assertIn("No credential can read ghcr.io/o/api", check["detail"])
        notes = json.loads((self.root / "github" / "packages.json").read_text())["packages"]
        self.assertEqual("pilot", notes["o/api"]["product"])

    def test_invalid_values_are_refused_before_anything_is_sent(self):
        with patch.object(transport.fleet, "resolve_target", return_value=self.config), \
                patch.object(transport.artifacts, "prepare", return_value=self.bundle), \
                patch.object(transport, "Ssh") as ssh:
            for provided in ({"values": {"Bad Key": "x"}}, {"values": {"Key": 5}}, {"values": "x"}):
                with self.subTest(provided=provided), self.assertRaisesRegex(ValueError, "Invalid provided settings"):
                    transport.submit(self.root, "pilot-dev", "pilot-1", "max", provided=provided)
            ssh.assert_not_called()

    def test_the_settings_plan_reads_the_release_without_a_target(self):
        with patch.object(transport.fleet, "resolve_target", return_value=self.config), \
                patch.object(transport.artifacts, "prepare", return_value=self.bundle), \
                patch.object(transport, "Ssh") as ssh:
            plan = transport.settings_plan(self.root, "pilot-dev", "pilot-1")
        ssh.assert_not_called()
        self.assertEqual(("pilot-dev", "v1", "pilot", "dev"), (plan["targetId"], plan["release"], plan["product"],
                                                                plan["environment"]))
        keys = {item["key"]: item for item in plan["services"][0]["keys"]}
        self.assertTrue(keys["Billing:SecretKey"]["required"] and keys["Billing:SecretKey"]["secret"])
        self.assertEqual("pilot-dev-api;localhost", keys["AllowedHosts"]["value"])


if __name__ == "__main__":
    unittest.main()
