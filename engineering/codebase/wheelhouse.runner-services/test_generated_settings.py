"""Generated and linked setting keys (deploy descriptor § Generated and linked keys): the bundle's `generate` and `links`
parts, a linked key rendered from its source across services, the provided values and the derived site URLs, the
readiness check, the declarations the console reads, and the `generate-setting` verb on the target and through the
transport: its container's confinement, its one-line answer, and that its standard output reaches nothing but that
answer."""
import base64
import contextlib
import copy
import hashlib
import io
import json
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import runner
import transport
from test_settings_render import CheckDocker, RenderDocker

SIGNING = "Identity:Server:Keys:Signing:0"
CLIENT = "Identity:Server:Clients:0:ClientSecret"
ISSUER = "Identity:Server:Issuer"
COMMAND = ["dotnet", "Identity.Api.dll", "keys", "generate", "--role", "signing"]
IDENTITY_IMAGE = "ghcr.io/wow-two-sdk-beta/wow-two-sdk-beta.identity/identity@sha256:" + "c" * 64
DESK_IMAGE = "ghcr.io/owner/pilot/desk@sha256:" + "d" * 64


def release_manifest():
    """A product pinning Identity: two generated secrets and a site-linked issuer, and a desk that takes Identity's
    client secret and authority by link."""
    return {"schemaVersion": 2, "product": "pilot", "release": "v1", "sourceCommit": "a" * 40,
            "platform": "linux/amd64", "rollbackCompatible": False,
            "images": {"identity": IDENTITY_IMAGE, "desk": DESK_IMAGE},
            "requiredConfiguration": {"identity": [SIGNING, CLIENT], "desk": []},
            "settings": {
                "identity": {"secrets": [SIGNING, CLIENT],
                             "types": {SIGNING: {"type": "text"}, CLIENT: {"type": "text"}},
                             "generate": {SIGNING: {"command": COMMAND, "timeoutSeconds": 120},
                                          CLIENT: {"random": 32, "encoding": "base64url"}},
                             "links": {ISSUER: "Sites:identity:Url"}},
                "desk": {"links": {"Auth:ClientSecret": CLIENT, "Auth:Authority": "Sites:identity:Url"}}},
            "sites": {"desk": {"primary": True}, "identity": {}},
            "routes": {"desk": {"desk": {"paths": ["/"]}}, "identity": {"identity": {"paths": ["/"]}}}}


class LinkedSettingsTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.config = self.root / "srv" / "config" / "pilot-dev"
        self.config.mkdir(parents=True, mode=0o700)
        (self.root / "srv" / "config").chmod(0o700)
        self.target = {"product": "pilot", "environment": "dev", "root": str(self.root / "srv"), "render": True,
                       "settings": {}, "minimumFreeBytes": 0, "variables": {"PLATFORM_NETWORK": "platform"},
                       "ingress": {"scheme": "https", "entryPoints": ["web"],
                                   "hosts": {"desk": "desk.pilot.example", "identity": "id.pilot.example"}}}
        self.bundle = self.root / "bundle"
        self.bundle.mkdir()
        self.manifest = release_manifest()
        self.save()
        RenderDocker.events, RenderDocker.ingress, RenderDocker.pulled = [], True, None

    def tearDown(self):
        self.temporary.cleanup()

    def save(self):
        compose = json.dumps({"services": {name: {"image": image, "platform": "linux/amd64",
                                                  "healthcheck": {"test": ["CMD", "true"]}}
                                           for name, image in self.manifest["images"].items()}}).encode()
        (self.bundle / "compose.json").write_bytes(compose)
        self.manifest["composeSha256"] = hashlib.sha256(compose).hexdigest()
        (self.bundle / "release.json").write_text(json.dumps(self.manifest))

    def apply(self, values):
        return runner.apply(self.bundle, self.target, "max", docker_factory=RenderDocker, provided={"values": values})

    def rendered(self, service):
        return json.loads((self.config / (service + ".target.json")).read_text())

    def host_file(self, service, document):
        path = self.config / (service + ".json")
        path.write_text(json.dumps(document))
        path.chmod(0o600)
        self.target["settings"][service] = str(path)

    # ---- the bundle's parts

    def test_generate_and_links_parse_with_their_defaults_filled(self):
        self.manifest["settings"]["identity"]["generate"] = {SIGNING: {"command": COMMAND}, CLIENT: {"random": 48}}
        declared = runner.release_settings(self.manifest)
        self.assertEqual({SIGNING: {"command": COMMAND, "timeoutSeconds": 120},
                          CLIENT: {"random": 48, "encoding": "base64url"}}, declared["identity"]["generate"])
        self.assertEqual({ISSUER: "Sites:identity:Url"}, declared["identity"]["links"])
        self.assertEqual({"Auth:ClientSecret": CLIENT, "Auth:Authority": "Sites:identity:Url"}, declared["desk"]["links"])
        # A service that declares neither part carries both, empty.
        del self.manifest["settings"]["desk"]
        self.manifest["settings"]["desk"] = {"optional": {"Feature:Flag": "on"}}
        self.assertEqual(({}, {}), (runner.release_settings(self.manifest)["desk"]["generate"],
                                    runner.release_settings(self.manifest)["desk"]["links"]))

    def test_invalid_generate_specs_are_refused(self):
        for spec, message in (({"random": 15}, "Invalid generated setting"), ({"random": 65}, "Invalid generated setting"),
                              ({"random": "32"}, "Invalid generated setting"),
                              ({"random": 32, "encoding": "base32"}, "Invalid generated setting"),
                              ({"random": 32, "command": COMMAND}, "Invalid generated setting"),
                              ({}, "Invalid generated setting"), ({"command": []}, "Invalid generate command"),
                              ({"command": ["dotnet", ""]}, "Invalid generate command"),
                              ({"command": "dotnet keys"}, "Invalid generate command"),
                              ({"command": COMMAND, "timeout": "2m"}, "Invalid generate command"),
                              ({"command": COMMAND, "timeoutSeconds": 5}, "generate timeout must be 10-600"),
                              ({"command": COMMAND, "timeoutSeconds": 601}, "generate timeout must be 10-600")):
            with self.subTest(spec=spec), self.assertRaisesRegex(runner.Rejected, message):
                manifest = copy.deepcopy(self.manifest)
                manifest["settings"]["identity"]["generate"][CLIENT] = spec
                runner.release_settings(manifest)

    def test_a_generated_key_must_be_a_secret_text_key_without_a_default(self):
        for change in (lambda entry: entry["secrets"].remove(CLIENT),
                       lambda entry: entry["types"].__setitem__(CLIENT, {"type": "url"}),
                       lambda entry: entry.setdefault("optional", {}).__setitem__(CLIENT, "x")):
            manifest = copy.deepcopy(self.manifest)
            change(manifest["settings"]["identity"])
            with self.subTest(entry=manifest["settings"]["identity"]), \
                    self.assertRaisesRegex(runner.Rejected, "secret text key without a default: " + CLIENT):
                runner.release_settings(manifest)

    def test_invalid_links_are_refused(self):
        for links, message in (({"Bad Key": CLIENT}, "Invalid linked settings"), ({"Auth:Id": 5}, "Invalid linked settings"),
                               ({"Auth:Id": "Not A Path"}, "Invalid linked settings"),
                               ({"Auth:Id": "Auth:Id"}, "Invalid linked settings"), ([CLIENT], "Invalid linked settings")):
            with self.subTest(links=links), self.assertRaisesRegex(runner.Rejected, message):
                manifest = copy.deepcopy(self.manifest)
                manifest["settings"]["desk"]["links"] = links
                runner.release_settings(manifest)

    def test_a_linked_key_stands_alone_and_links_one_hop(self):
        manifest = copy.deepcopy(self.manifest)
        manifest["settings"]["desk"]["secrets"] = ["Auth:ClientSecret"]
        with self.assertRaisesRegex(runner.Rejected, "A linked setting stands alone: Auth:ClientSecret"):
            runner.release_settings(manifest)
        manifest = copy.deepcopy(self.manifest)
        manifest["settings"]["desk"]["links"]["Auth:Issuer"] = ISSUER
        with self.assertRaisesRegex(runner.Rejected, r"source is linked itself \(one hop only\): Auth:Issuer"):
            runner.release_settings(manifest)

    # ---- the render

    def test_linked_keys_take_their_sources_value_across_services_and_from_the_site_urls(self):
        result = self.apply({SIGNING: "SIGNING_PEM", CLIENT: "CLIENT_SECRET"})
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        identity, desk = self.rendered("identity"), self.rendered("desk")
        self.assertEqual(("SIGNING_PEM", "CLIENT_SECRET"), (identity["Identity"]["Server"]["Keys"]["Signing"]["0"],
                                                             identity["Identity"]["Server"]["Clients"]["0"]["ClientSecret"]))
        self.assertEqual("https://id.pilot.example", identity["Identity"]["Server"]["Issuer"])
        self.assertEqual({"ClientSecret": "CLIENT_SECRET", "Authority": "https://id.pilot.example"}, desk["Auth"])
        # The source reaches only the service declaring it; the desk holds it under its own key alone.
        self.assertNotIn("Identity", desk)
        self.assertEqual(["Auth:Authority", "Auth:ClientSecret"], result["settings"]["desk"]["linked"])
        self.assertEqual([ISSUER], result["settings"]["identity"]["linked"])
        self.assertIn("3 linked", result["steps"][0]["detail"])
        record = json.dumps(result) + (self.root / "srv/pilot-dev/active.json").read_text()
        for secret in ("SIGNING_PEM", "CLIENT_SECRET"):
            self.assertNotIn(secret, record)

    def test_a_linked_key_takes_the_declaring_services_rendered_value_before_any_provided_one(self):
        # Identity's host file sets the source; Wheelhouse provides nothing for it, so the desk takes the host value.
        self.host_file("identity", {"Identity": {"Server": {"Clients": {"0": {"ClientSecret": "HOST_CLIENT"}}}}})
        self.apply({SIGNING: "SIGNING_PEM"})
        self.assertEqual("HOST_CLIENT", self.rendered("desk")["Auth"]["ClientSecret"])

    def test_a_source_no_service_declares_falls_back_to_the_provided_value(self):
        self.manifest["settings"]["desk"]["links"]["Billing:Plan"] = "Billing:DefaultPlan"
        self.save()
        result = self.apply({SIGNING: "S", CLIENT: "C", "Billing:DefaultPlan": "solo"})
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        self.assertEqual("solo", self.rendered("desk")["Billing"]["Plan"])

    def test_a_linked_key_never_joins_the_required_list(self):
        # A release that lists the linked key as required still renders it from its source, and an optional source
        # with no value leaves it unset instead of refusing the deploy.
        self.manifest["requiredConfiguration"]["desk"] = ["Auth:ClientSecret", "Auth:Plan"]
        self.manifest["settings"]["identity"]["types"]["Plan:Unset"] = {"type": "text"}
        self.manifest["settings"]["desk"]["links"]["Auth:Plan"] = "Plan:Unset"
        self.save()
        result = self.apply({SIGNING: "S", CLIENT: "C"})
        self.assertEqual("succeeded", result["status"], result.get("reason"))
        desk = self.rendered("desk")
        self.assertEqual("C", desk["Auth"]["ClientSecret"])
        self.assertNotIn("Plan", desk["Auth"])

    def test_a_missing_required_source_refuses_the_render_by_its_own_name(self):
        with self.assertRaisesRegex(runner.RenderRefused, "Missing required setting: identity:" + CLIENT + "$"):
            self.apply({SIGNING: "S"})
        self.assertFalse((self.config / "desk.target.json").exists())
        self.assertEqual([], RenderDocker.events)

    # ---- readiness

    def test_readiness_counts_a_linked_key_ready_whenever_its_source_is(self):
        manifest = runner.validate_bundle(self.bundle)
        result = runner.check(self.target, manifest, docker_factory=CheckDocker, provided={"keys": [SIGNING]})
        failing = {item["name"]: item["detail"] for item in result["checks"] if not item["ok"]}
        self.assertEqual({"Settings: identity": "Missing required setting: identity:" + CLIENT,
                          "Settings: desk": "Missing required setting: identity:" + CLIENT
                                            + ", the source of Auth:ClientSecret"}, failing)
        ready = runner.check(self.target, manifest, docker_factory=CheckDocker, provided={"keys": [SIGNING, CLIENT]})
        self.assertTrue(ready["ok"], ready["checks"])
        # A source the declaring service's host file sets is ready too, though Wheelhouse provides none.
        self.host_file("identity", {"Identity": {"Server": {"Clients": {"0": {"ClientSecret": "HOST_CLIENT"}}}}})
        hosted = runner.check(self.target, manifest, docker_factory=CheckDocker, provided={"keys": [SIGNING]})
        self.assertTrue(hosted["ok"], hosted["checks"])

    def test_readiness_never_requires_a_linked_key_itself(self):
        self.manifest["requiredConfiguration"]["desk"] = ["Auth:ClientSecret", "Auth:Authority"]
        self.save()
        manifest = runner.validate_bundle(self.bundle)
        result = runner.check(self.target, manifest, docker_factory=CheckDocker, provided={"keys": [SIGNING, CLIENT]})
        self.assertTrue(result["ok"], result["checks"])

    # ---- declarations

    def test_declarations_carry_each_generate_spec_and_each_links_source(self):
        self.manifest["requiredConfiguration"]["desk"] = ["Auth:ClientSecret"]
        self.save()
        declared = runner.settings_declarations(self.target, runner.validate_bundle(self.bundle))
        services = {service["name"]: {item["key"]: item for item in service["keys"]} for service in declared["services"]}
        identity, desk = services["identity"], services["desk"]
        self.assertEqual({"key": SIGNING, "required": True, "secret": True, "type": "text",
                          "generate": {"command": COMMAND, "timeoutSeconds": 120}}, identity[SIGNING])
        self.assertEqual({"random": 32, "encoding": "base64url"}, identity[CLIENT]["generate"])
        # A linked key carries its source and its source's secrecy; no type, default or requirement of its own.
        self.assertEqual({"key": ISSUER, "required": False, "secret": False, "from": "Sites:identity:Url"}, identity[ISSUER])
        self.assertEqual({"key": "Auth:ClientSecret", "required": False, "secret": True, "from": CLIENT},
                         desk["Auth:ClientSecret"])
        self.assertEqual({"key": "Auth:Authority", "required": False, "secret": False, "from": "Sites:identity:Url"},
                         desk["Auth:Authority"])
        self.assertNotIn("generate", desk["Sites:identity:Url"])
        self.assertNotIn("CLIENT", json.dumps(declared))

    def test_a_linked_keys_secrecy_follows_its_source(self):
        declared = runner.release_settings(self.manifest)
        self.assertTrue(runner.linked_secret(CLIENT, declared))
        self.assertFalse(runner.linked_secret("Sites:identity:Url", declared))
        self.manifest["settings"]["identity"]["optional"] = {"Billing:Plan": "solo"}
        declared = runner.release_settings(self.manifest)
        self.assertFalse(runner.linked_secret("Billing:Plan", declared))
        # An undeclared source falls back to the name guess.
        self.assertTrue(runner.linked_secret("Stripe:ApiKey", declared))
        self.assertFalse(runner.linked_secret("Support:Url", declared))


class GenerateDocker:
    """Answers a generate run as scripted (`answer`: exit code, standard output bytes, last standard-error line) and
    records the image fetch and the run."""
    answer = (0, b"VALUE\n", "")
    runs = []
    fetched = []

    def __init__(self, target, environment):
        self.environment = environment

    def fetch(self, image, registry=None, unreadable=(), timeout=1800):
        GenerateDocker.fetched.append((image, registry, tuple(unreadable), timeout))
        return False

    def generate(self, arguments, timeout, container):
        GenerateDocker.runs.append((arguments, timeout, container))
        return GenerateDocker.answer


class GenerateSettingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.target = {"product": "pilot", "environment": "dev", "root": str(self.root / "srv"), "render": True,
                       "variables": {"PLATFORM_NETWORK": "platform"}}
        self.manifest = release_manifest()
        GenerateDocker.answer, GenerateDocker.runs, GenerateDocker.fetched = (0, b"VALUE\n", ""), [], []

    def tearDown(self):
        self.temporary.cleanup()

    def generate(self, key=SIGNING, provided=None):
        return runner.generate_setting(self.target, self.manifest, "identity", key, provided, docker_factory=GenerateDocker)

    def main(self, key=SIGNING):
        """The runner's CLI as transport calls it: (exit code, stdout, stderr)."""
        encode = lambda value: base64.b64encode(json.dumps(value).encode()).decode()
        argv = ["runner.py", "generate-setting", "--target-json", encode(self.target),
                "--release-json", encode(self.manifest), "--service", "identity", "--key", key]
        out, err = io.StringIO(), io.StringIO()
        with patch.object(sys, "argv", argv), patch.object(runner, "Docker", GenerateDocker), \
                contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = runner.main()
        return code, out.getvalue(), err.getvalue()

    def test_the_command_runs_confined_from_the_services_pinned_image(self):
        self.assertEqual({"value": "VALUE"}, self.generate())
        [(arguments, timeout, container)] = GenerateDocker.runs
        self.assertEqual(120, timeout)
        self.assertTrue(container.startswith("pilot-dev-generate-"))
        self.assertEqual(["docker", "run", "--rm", "--name", container, "--pull", "never"], arguments[:7])
        for flag, value in (("--network", "none"), ("--cap-drop", "ALL"), ("--memory", "512m"),
                            ("--security-opt", "no-new-privileges:true")):
            self.assertEqual(value, arguments[arguments.index(flag) + 1], flag)
        self.assertEqual(["--entrypoint", "dotnet", IDENTITY_IMAGE, *COMMAND[1:]], arguments[-8:])
        # As the image's user, with no settings file, no secret and no variable.
        for flag in ("--user", "-u", "-e", "--env", "--env-file", "-v", "--volume", "--mount", "-i", "--interactive"):
            self.assertNotIn(flag, arguments)
        self.assertEqual([(IDENTITY_IMAGE, None, (), runner.GENERATE_PULL_TIMEOUT)], GenerateDocker.fetched)

    def test_the_value_drops_only_its_trailing_newline(self):
        for output, value in ((b"VALUE", "VALUE"), (b"VALUE\n", "VALUE"), (b"-----BEGIN-----\nab\n-----END-----\n\n",
                                                                           "-----BEGIN-----\nab\n-----END-----\n")):
            with self.subTest(output=output):
                GenerateDocker.answer = (0, output, "warming up")
                self.assertEqual({"value": value}, self.generate())

    def test_failures_keep_the_last_standard_error_line_and_never_the_output(self):
        for answer, error in (((1, b"LEAKED_VALUE\n", "error: unknown role"), "error: unknown role"),
                              ((3, b"LEAKED_VALUE", ""), "Exited 3"),
                              ((0, b"", "nothing to print"), "nothing to print"),
                              ((0, b"\n", ""), "The command printed nothing"),
                              ((0, b"\xffLEAKED_VALUE", ""), "The command printed something other than UTF-8 text"),
                              ((None, b"", ""), "Timed out after 120s"),
                              ((125, b"", "docker: Error response from daemon: no such image"),
                               "docker: Error response from daemon: no such image")):
            with self.subTest(answer=answer):
                GenerateDocker.answer = answer
                with self.assertRaises(runner.GenerateFailed) as raised:
                    self.generate()
                self.assertEqual(error, runner.reason(raised.exception))
                self.assertNotIn("LEAKED", str(raised.exception))

    def test_an_overlong_value_fails(self):
        GenerateDocker.answer = (0, b"x" * (runner.GENERATE_VALUE_LIMIT + 1), "")
        with self.assertRaisesRegex(runner.GenerateFailed, "more than 65536 characters"):
            self.generate()

    def test_the_spec_timeout_bounds_the_run(self):
        self.manifest["settings"]["identity"]["generate"][SIGNING]["timeoutSeconds"] = 30
        GenerateDocker.answer = (None, b"", "")
        with self.assertRaisesRegex(runner.GenerateFailed, "Timed out after 30s"):
            self.generate()
        self.assertEqual(30, GenerateDocker.runs[0][1])

    def test_only_a_declared_command_key_runs(self):
        for service, key, message in (("identity", CLIENT, "random bytes, which Wheelhouse makes itself"),
                                      ("identity", ISSUER, "identity declares no generated value for " + ISSUER),
                                      ("desk", "Auth:ClientSecret", "desk declares no generated value"),
                                      ("ghost", SIGNING, "Unknown release service"),
                                      ("identity", "Bad Key", "Invalid setting key")):
            with self.subTest(key=key), self.assertRaisesRegex(runner.Rejected, message):
                runner.generate_setting(self.target, self.manifest, service, key, docker_factory=GenerateDocker)
        self.manifest["images"]["identity"] = "ghcr.io/wow-two-sdk-beta/identity:0.3.6"
        with self.assertRaisesRegex(runner.Rejected, "immutable image digests"):
            self.generate()
        self.assertEqual([], GenerateDocker.runs)

    def test_a_registry_sign_in_reaches_only_the_pull(self):
        login = {"server": "ghcr.io", "username": "x-access-token", "password": "ghs_TOKEN", "credential": "installation"}
        self.generate(provided={"registry": login, "unreadable": ["ghcr.io/owner/other"]})
        self.assertEqual([(IDENTITY_IMAGE, login, ("ghcr.io/owner/other",), runner.GENERATE_PULL_TIMEOUT)],
                         GenerateDocker.fetched)
        self.assertNotIn("ghs_TOKEN", json.dumps(GenerateDocker.runs))
        with self.assertRaisesRegex(runner.Rejected, "Invalid registry sign-in"):
            self.generate(provided={"registry": {"server": "ghcr.io"}})

    def test_the_cli_prints_one_value_line_on_success(self):
        code, out, err = self.main()
        self.assertEqual((0, '{"value": "VALUE"}\n', ""), (code, out, err))

    def test_the_cli_prints_one_error_line_and_nothing_else_on_failure(self):
        GenerateDocker.answer = (1, b"LEAKED_VALUE\n", "error: unknown role")
        code, out, err = self.main()
        self.assertEqual((1, ""), (code, out))
        self.assertEqual([{"error": "error: unknown role"}], [json.loads(line) for line in err.splitlines()])
        # A refusal is one error line too.
        code, out, err = self.main(key=CLIENT)
        self.assertEqual((1, "", {"error": CLIENT + " is random bytes, which Wheelhouse makes itself"}),
                         (code, out, json.loads(err)))

    def test_a_run_writes_nothing_on_the_target(self):
        GenerateDocker.answer = (0, b"LEAKED_VALUE\n", "")
        self.main()
        GenerateDocker.answer = (2, b"LEAKED_VALUE\n", "boom")
        self.main()
        self.assertEqual([], [path for path in self.root.rglob("*")])


class GenerateDockerRunTests(unittest.TestCase):
    """The real Docker adapter's generate run and one-image fetch, over a faked subprocess."""

    def setUp(self):
        self.calls = []
        self.docker = runner.Docker({"product": "pilot", "environment": "dev"}, {"PATH": "/usr/bin"})

    def test_a_run_reads_stdout_apart_and_only_the_last_stderr_line(self):
        def fake_run(arguments, **options):
            self.calls.append((arguments, options))
            return subprocess.CompletedProcess(arguments, 0, b"VALUE\n", b"step one\nstep two\n\n")
        with patch.object(runner.subprocess, "run", side_effect=fake_run):
            self.assertEqual((0, b"VALUE\n", "step two"), self.docker.generate(["docker", "run"], 120, "c1"))
        [(_, options)] = self.calls
        self.assertEqual((subprocess.DEVNULL, subprocess.PIPE, subprocess.PIPE, 120, {"PATH": "/usr/bin"}),
                         (options["stdin"], options["stdout"], options["stderr"], options["timeout"], options["env"]))

    def test_a_run_past_its_timeout_removes_its_container(self):
        def fake_run(arguments, **options):
            self.calls.append(arguments)
            if arguments[:2] == ["docker", "run"]:
                raise subprocess.TimeoutExpired(arguments, 30, output=b"PARTIAL_VALUE", stderr=b"slow")
            return subprocess.CompletedProcess(arguments, 0, b"", b"")
        with patch.object(runner.subprocess, "run", side_effect=fake_run):
            self.assertEqual((None, b"", ""), self.docker.generate(["docker", "run"], 30, "pilot-dev-generate-1"))
        self.assertEqual(["docker", "rm", "--force", "pilot-dev-generate-1"], self.calls[-1])

    def fetch(self, present, registry=None, unreadable=()):
        def fake_run(arguments, **options):
            self.calls.append(arguments)
            code = 0 if arguments[1:3] != ["image", "inspect"] or present else 1
            return subprocess.CompletedProcess(arguments, code, "", "")
        with patch.object(runner.subprocess, "run", side_effect=fake_run):
            return self.docker.fetch(IDENTITY_IMAGE, registry, unreadable, timeout=600)

    def test_an_image_on_the_host_is_not_pulled(self):
        self.assertFalse(self.fetch(present=True))
        self.assertFalse(any("pull" in arguments for arguments in self.calls))

    def test_a_missing_public_image_pulls_anonymously(self):
        self.assertTrue(self.fetch(present=False))
        self.assertEqual(["docker", "pull", "--quiet", IDENTITY_IMAGE], self.calls[-1])

    def test_a_missing_image_the_sign_in_covers_pulls_signed_in(self):
        login = {"server": "ghcr.io", "username": "x-access-token", "password": "ghs_TOKEN", "credential": "installation"}
        self.assertTrue(self.fetch(present=False, registry=login))
        commands = [arguments[3] if arguments[1] == "--config" else arguments[1] for arguments in self.calls]
        self.assertEqual(["image", "login", "pull", "logout"], commands)
        # A sign-in for other repositories leaves another owner's pinned image anonymous.
        self.calls = []
        self.fetch(present=False, registry={**login, "repositories": ["ghcr.io/owner/pilot/desk"]})
        self.assertEqual(["docker", "pull", "--quiet", IDENTITY_IMAGE], self.calls[-1])

    def test_a_missing_image_nothing_can_read_fails_before_any_pull(self):
        repository = runner.image_repository(IDENTITY_IMAGE)
        with self.assertRaisesRegex(runner.CommandFailed, "No credential on the control host can read " + repository):
            self.fetch(present=False, unreadable=(repository,))
        self.assertFalse(any("pull" in arguments or "login" in arguments for arguments in self.calls))


class GenerateTransportTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        for name in ("key", "known_hosts"):
            (self.root / name).touch()
        self.bundle = self.root / "bundle"
        self.bundle.mkdir()
        self.manifest = release_manifest()
        compose = json.dumps({"services": {name: {"image": image, "platform": "linux/amd64",
                                                  "healthcheck": {"test": ["CMD", "true"]}}
                                           for name, image in self.manifest["images"].items()}}).encode()
        (self.bundle / "compose.json").write_bytes(compose)
        self.manifest["composeSha256"] = hashlib.sha256(compose).hexdigest()
        (self.bundle / "release.json").write_text(json.dumps(self.manifest))
        self.config = {"serverId": "pilot-host", "provider": "ovhcloud", "acceptsCandidates": True,
                       "needsConfirmation": False, "requiresDevPass": False,
                       "ssh": {"host": "vps.example.net", "user": "deploy", "port": 22, "keyFile": str(self.root / "key"),
                               "knownHostsFile": str(self.root / "known_hosts")},
                       "target": {"product": "pilot", "environment": "dev", "root": str(self.root / "srv"),
                                  "render": True, "settings": {}, "variables": {"PLATFORM_NETWORK": "platform"}}}
        self.login = {"server": "ghcr.io", "username": "x-access-token", "password": "ghs_APP", "credential": "installation"}
        for item in (patch.object(transport.fleet, "resolve_target", return_value=self.config),
                     patch.object(transport.artifacts, "prepare", return_value=self.bundle),
                     patch.object(transport.registry, "sign_in_for",
                                  return_value=(self.login, {"server": "ghcr.io", "credential": "installation",
                                                             "refused": []}))):
            self.start(item)

    def tearDown(self):
        self.temporary.cleanup()

    def start(self, item):
        """Starts a patch that stops after the test, the last started first."""
        item.start()
        self.addCleanup(item.stop)

    def test_the_verb_streams_the_runner_and_its_sign_in_on_stdin_and_returns_only_the_value(self):
        with patch.object(transport.Ssh, "execute", return_value='{"value": "VALUE"}') as execute:
            result = transport.generate_setting(self.root, "pilot-dev", "pilot-1", "identity", SIGNING)
        self.assertEqual({"value": "VALUE"}, result)
        arguments, step, stdin, timeout = execute.call_args.args
        command = shlex.split(arguments[-1])
        self.assertEqual(["python3", "-c", transport.BOOTSTRAP, "generate-setting", "--target-json"], command[:5])
        self.assertEqual(["identity", SIGNING], [command[command.index("--service") + 1], command[command.index("--key") + 1]])
        self.assertIn("--stdin", command)
        self.assertEqual(self.manifest, json.loads(base64.b64decode(command[command.index("--release-json") + 1])))
        # The sign-in travels on stdin after the runner's own source, never as an argument.
        self.assertNotIn("ghs_APP", arguments[-1])
        size, _, rest = stdin.partition("\n")
        source = transport.RUNNER.read_text()
        self.assertEqual(len(source.encode()), int(size))
        self.assertEqual(source, rest[:len(source)])
        self.assertEqual({"registry": self.login}, json.loads(rest[len(source):]))
        self.assertEqual(120 + runner.GENERATE_PULL_TIMEOUT + 60, timeout)
        self.assertFalse(any("VALUE" in path.read_text() for path in self.root.rglob("*.json")))

    def test_a_random_key_is_refused_before_ssh(self):
        with patch.object(transport.Ssh, "execute") as execute, \
                self.assertRaisesRegex(transport.Rejected, "random bytes, which Wheelhouse makes itself"):
            transport.generate_setting(self.root, "pilot-dev", "pilot-1", "identity", CLIENT)
        execute.assert_not_called()

    def local(self, arguments, step, stdin=None, timeout=30):
        """The target's shell, here: runs the SSH command line locally."""
        result = subprocess.run(["sh", "-c", arguments[-1]], input=stdin, capture_output=True, text=True, timeout=timeout)
        if result.returncode:
            raise transport.ssh_failure(step, result)
        return result.stdout

    def test_the_streamed_runner_reads_its_document_from_the_rest_of_stdin(self):
        # A real runner, started through BOOTSTRAP, decodes the release and refuses the bad sign-in it read from stdin:
        # no docker command runs before that, and its error line is relayed as the reason.
        self.login = {"server": "not a host"}
        self.start(patch.object(transport.registry, "sign_in_for", return_value=(self.login, None)))
        with patch.object(transport.Ssh, "execute", side_effect=self.local), \
                self.assertRaisesRegex(transport.CommandFailed, "^Invalid registry sign-in$"):
            transport.generate_setting(self.root, "pilot-dev", "pilot-1", "identity", SIGNING)
        with patch.object(transport.Ssh, "execute", side_effect=self.local):
            state = transport.run_remote(transport.Ssh(self.config["ssh"]), "state", self.config["target"],
                                         document={"values": {}})
        self.assertIn("condition", state)

    def test_the_transport_cli_answers_one_line_either_way(self):
        def run(execute):
            argv = ["transport.py", "generate-setting", "--root", str(self.root), "--target", "pilot-dev",
                    "--bundle", "pilot-1", "--service", "identity", "--key", SIGNING]
            out, err = io.StringIO(), io.StringIO()
            with patch.object(sys, "argv", argv), patch.object(transport.inventory, "install"), \
                    patch.object(transport.Ssh, "execute", side_effect=execute), \
                    contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
                code = transport.main()
            return code, out.getvalue(), err.getvalue()

        self.assertEqual((0, '{"value": "VALUE"}\n', ""), run(lambda *arguments: '{"value": "VALUE"}'))

        def failing(arguments, step, stdin=None, timeout=30):
            failed = subprocess.CompletedProcess(arguments, 1, "LEAKED_VALUE\n", '{"error": "error: unknown role"}\n')
            raise transport.ssh_failure(step, failed)
        code, out, err = run(failing)
        self.assertEqual((1, ""), (code, out))
        [line] = err.splitlines()
        self.assertEqual({"error": "error: unknown role", "status": "rejected", "failure": "CommandFailed",
                          "reason": "error: unknown role"}, json.loads(line))
        self.assertNotIn("LEAKED", err)


if __name__ == "__main__":
    unittest.main()
