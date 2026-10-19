import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import runner
import transport


class TopologyTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.target = {"product": "pilot", "environment": "dev", "root": str(self.root / "state"),
                       "settings": {"api": "/DO_NOT_READ/private.json"}, "variables": {"SECRET": "DO_NOT_LOG"}}
        self.base = self.root / "state/pilot-dev"
        self.job = "00000000-0000-0000-0000-000000000001"
        self.bundle = self.base / "releases" / self.job
        self.bundle.mkdir(parents=True)
        self.image = "ghcr.io/owner/api@sha256:" + "a" * 64
        self.compose = {"services": {
            name: {"image": self.image, "platform": "linux/amd64", "healthcheck": {"test": ["CMD", "true"]}}
            for name in ("api", "worker")}}
        self.manifest = {"schemaVersion": 1, "product": "pilot", "release": "v1", "sourceCommit": "a" * 40,
                         "platform": "linux/amd64", "rollbackCompatible": True,
                         "images": {name: self.image for name in self.compose["services"]},
                         "requiredConfiguration": {name: [] for name in self.compose["services"]}}
        runner.write_json(self.base / "current.json", {"id": self.job, "release": "v1"})
        self.save()

    def tearDown(self):
        self.temporary.cleanup()

    def save(self):
        contents = json.dumps(self.compose).encode()
        (self.bundle / "compose.json").write_bytes(contents)
        self.manifest["composeSha256"] = hashlib.sha256(contents).hexdigest()
        runner.write_json(self.bundle / "release.json", self.manifest)

    def read(self):
        before = {str(path): path.read_bytes() for path in self.root.rglob("*") if path.is_file()}
        with patch.object(runner.subprocess, "run", side_effect=AssertionError("No Docker or shell execution")), \
             patch.object(runner, "environment_for", side_effect=AssertionError("No configuration reads")):
            result = runner.topology(self.target)
        after = {str(path): path.read_bytes() for path in self.root.rglob("*") if path.is_file()}
        self.assertEqual(before, after)
        self.assertNotIn("DO_NOT_LOG", json.dumps(result))
        return result

    def assert_unavailable(self, result):
        self.assertEqual("unavailable", result["availability"])
        self.assertIsNone(result["release"])
        for key in ("services", "networks", "volumes", "dependencies"):
            self.assertEqual([], result[key])
        self.assertTrue(result["warnings"])

    def test_current_saved_snapshot_is_the_only_source(self):
        newer = self.base / "releases/00000000-0000-0000-0000-000000000002"
        newer.mkdir()
        runner.write_json(newer / "release.json", {"release": "v9"})
        runner.write_json(self.base / "active.json", {"id": newer.name, "status": "failed", "mutationStarted": True})
        result = self.read()
        self.assertEqual("available", result["availability"])
        self.assertEqual("v1", result["release"])
        self.assertEqual(["api", "worker"], [service["name"] for service in result["services"]])
        self.assertTrue(any("last successful" in warning for warning in result["warnings"]))

    def test_no_current_record_is_not_deployed_even_with_saved_bundles(self):
        (self.base / "current.json").unlink()
        result = self.read()
        self.assertEqual("not-deployed", result["availability"])
        self.assertIsNone(result["release"])
        self.assertEqual([], result["services"])

    def test_current_identity_cannot_escape_releases(self):
        for identifier in ("../DO_NOT_LOG", "", "not-a-uuid"):
            with self.subTest(identifier=identifier):
                runner.write_json(self.base / "current.json", {"id": identifier, "release": "v1"})
                self.assert_unavailable(self.read())

    def test_current_release_must_match_validated_snapshot(self):
        runner.write_json(self.base / "current.json", {"id": self.job, "release": "DO_NOT_LOG"})
        self.assert_unavailable(self.read())

    def test_hash_mismatch_and_wrong_product_are_unavailable(self):
        (self.bundle / "compose.json").write_text("{}")
        self.assert_unavailable(self.read())
        self.manifest["product"] = "another"
        self.save()
        self.assert_unavailable(self.read())

    def test_missing_malformed_and_symlinked_files_are_unavailable(self):
        compose_path = self.bundle / "compose.json"
        compose_path.unlink()
        self.assert_unavailable(self.read())
        self.save()
        (self.bundle / "release.json").write_text("DO_NOT_LOG")
        self.assert_unavailable(self.read())
        self.save()
        source = self.root / "outside.json"
        source.write_bytes(compose_path.read_bytes())
        compose_path.unlink()
        compose_path.symlink_to(source)
        self.assert_unavailable(self.read())

    def test_metadata_is_allowlisted_and_never_interpolated(self):
        self.compose.update(
            networks={"private": {"external": True, "name": "DO_NOT_LOG", "driver_opts": {"secret": "DO_NOT_LOG"}}},
            volumes={"database": {"external": {"name": "DO_NOT_LOG"}, "driver_opts": {"device": "/DO_NOT_LOG"}}})
        self.compose["services"]["api"].update(
            environment={"PASSWORD": "DO_NOT_LOG"}, env_file="/DO_NOT_READ",
            command="DO_NOT_LOG", entrypoint="DO_NOT_LOG", labels={"secret": "DO_NOT_LOG"},
            networks={"private": {"aliases": ["DO_NOT_LOG"], "ipv4_address": "DO_NOT_LOG"}},
            volumes=["database:/DO_NOT_LOG", "/DO_NOT_LOG:/private", "./DO_NOT_LOG:/private", "/anonymous",
                     {"type": "bind", "source": "/DO_NOT_LOG", "target": "/private"},
                     {"type": "volume", "source": "database", "target": "/DO_NOT_LOG"}],
            ports=["127.0.0.1:8080:80", "[::1]:8443:443/tcp",
                   {"target": 53, "published": "5353", "protocol": "udp", "host_ip": "DO_NOT_LOG",
                    "name": "DO_NOT_LOG", "app_protocol": "DO_NOT_LOG"},
                   "${DO_NOT_LOG}:80", "80/${DO_NOT_LOG}"])
        self.save()
        result = self.read()
        service = result["services"][0]
        self.assertEqual(["private"], service["networks"])
        self.assertEqual(["database"], service["volumes"])
        self.assertEqual(["5353:53/udp", "8080:80/tcp", "8443:443/tcp"], service["ports"])
        self.assertIn({"name": "private", "external": True}, result["networks"])
        self.assertEqual([{"name": "database", "external": True}], result["volumes"])
        self.assertEqual({"name", "image", "networks", "volumes", "ports", "version", "needs", "sites"}, set(service))

    def test_services_carry_their_version_platform_needs_and_sites(self):
        self.manifest.update(versions={"api": {"version": "1.2.0", "changedIn": "v1.2.0"}},
                             needs={"api": ["postgres", "mainframe"], "worker": ["valkey"]},
                             sites={"api": {"app": {}, "admin": {"path": "/admin", "exposure": "private"}}})
        self.save()
        runner.write_json(self.base / "current.json", {"id": self.job, "release": "v1", "sites": [
            {"name": "app", "service": "api", "exposure": "public", "url": "http://app-pilot.dev.localhost:18080",
             "probe": {"ok": False, "status": 404, "detail": "The ingress has no route for this host"}},
            {"name": "admin", "service": "api", "exposure": "private", "url": "javascript:alert(1)"}]})
        api, worker = self.read()["services"]
        self.assertEqual({"version": "1.2.0", "changedIn": "v1.2.0"}, api["version"])
        self.assertEqual((["postgres"], ["valkey"]), (api["needs"], worker["needs"]))
        self.assertEqual([
            {"name": "admin", "path": "/admin", "port": 8080, "exposure": "private", "url": None, "reachable": None},
            {"name": "app", "path": "/", "port": 8080, "exposure": "public",
             "url": "http://app-pilot.dev.localhost:18080", "reachable": False}], api["sites"])
        self.assertEqual((None, []), (worker["version"], worker["sites"]))

    def test_a_schema_2_service_lists_one_site_entry_per_path_on_its_published_host(self):
        self.manifest.update(schemaVersion=2, sites={"app": {"exposure": "public", "primary": True,
                                                             "environments": ["dev", "prod"]}},
                             routes={"api": {"app": {"port": 8080, "paths": ["/api", "/"]}}})
        self.save()
        runner.write_json(self.base / "current.json", {"id": self.job, "release": "v1", "sites": [
            {"name": "app", "service": "api", "exposure": "public", "url": "https://app.pilot.example",
             "probe": {"ok": True, "status": 200}}]})
        api = self.read()["services"][0]
        self.assertEqual([
            {"name": "app", "path": "/", "port": 8080, "exposure": "public", "url": "https://app.pilot.example",
             "reachable": True},
            {"name": "app", "path": "/api", "port": 8080, "exposure": "public", "url": "https://app.pilot.example/api",
             "reachable": True}], api["sites"])

    def test_implicit_default_is_not_added_for_explicit_network_or_network_mode(self):
        self.compose["networks"] = {"private": None}
        self.compose["services"]["api"]["networks"] = ["private"]
        self.compose["services"]["worker"]["network_mode"] = "service:api"
        self.save()
        result = self.read()
        self.assertEqual([{"name": "private", "external": False}], result["networks"])
        self.assertEqual([], result["services"][1]["networks"])
        for value in (None, [], {}):
            with self.subTest(value=value):
                self.compose["services"]["api"]["networks"] = value
                self.save()
                result = self.read()
                self.assertEqual(["default"], result["services"][0]["networks"])
                self.assertIn({"name": "default", "external": False}, result["networks"])

    def test_explicit_default_resource_keeps_external_flag(self):
        self.compose["networks"] = {"default": {"external": True, "name": "DO_NOT_LOG"}}
        self.save()
        self.assertEqual([{"name": "default", "external": True}], self.read()["networks"])

    def test_explicit_default_membership_uses_implicit_default_definition(self):
        self.compose["services"]["api"]["networks"] = ["default"]
        self.compose["services"]["worker"]["networks"] = {"default": None}
        self.save()
        result = self.read()
        self.assertEqual([{"name": "default", "external": False}], result["networks"])
        self.assertTrue(all(service["networks"] == ["default"] for service in result["services"]))

    def test_invalid_explicit_default_cannot_be_recreated_as_internal(self):
        self.compose["networks"] = {"default": {"external": "DO_NOT_LOG"}}
        self.save()
        result = self.read()
        self.assertEqual([], result["networks"])
        self.assertTrue(all(service["networks"] == [] for service in result["services"]))
        self.assertTrue(result["warnings"])

    def test_invalid_collection_shapes_cannot_invent_default_relationships(self):
        for field in ("networks", "volumes", "ports", "depends_on"):
            with self.subTest(field=field):
                self.compose["services"]["api"][field] = False
                self.save()
                self.assert_unavailable(self.read())
                del self.compose["services"]["api"][field]

    def test_dependencies_support_short_and_long_forms_without_traffic_inference(self):
        self.compose["services"]["worker"]["depends_on"] = ["api", "api"]
        self.save()
        result = self.read()
        self.assertEqual([{"from": "worker", "to": "api", "condition": "service_started", "required": True}],
                         result["dependencies"])
        self.compose["services"]["worker"]["depends_on"] = {
            "api": {"condition": "service_healthy", "required": False, "restart": True, "extra": "DO_NOT_LOG"}}
        self.save()
        self.assertEqual([{"from": "worker", "to": "api", "condition": "service_healthy", "required": False}],
                         self.read()["dependencies"])

    def test_unsupported_and_dangling_relationships_are_omitted_with_safe_warning(self):
        self.compose["networks"] = {"${DO_NOT_LOG}": {}}
        self.compose["services"]["api"].update(
            networks=["missing", "${DO_NOT_LOG}"],
            depends_on={"worker": {"condition": "DO_NOT_LOG"}, "missing": {"condition": "service_started"}})
        self.save()
        result = self.read()
        self.assertEqual([], result["dependencies"])
        self.assertEqual([], result["services"][0]["networks"])
        self.assertTrue(result["warnings"])

    def test_snapshot_change_during_projection_returns_unavailable(self):
        project = runner.compose_topology
        def change_current(compose, manifest):
            result = project(compose, manifest)
            runner.write_json(self.base / "current.json", {"id": self.job, "release": "v2"})
            return result
        with patch.object(runner, "compose_topology", side_effect=change_current):
            self.assert_unavailable(runner.topology(self.target))

    def test_ports_accept_only_bounded_numeric_ports_ranges_and_known_protocols(self):
        for value, expected in [(80, "80/tcp"), ("8000-8002:80-82/udp", "8000-8002:80-82/udp"),
                                ({"target": 90, "protocol": "sctp"}, "90/sctp")]:
            with self.subTest(value=value):
                self.assertEqual(expected, runner.topology_port(value))
        for value in (True, 0, 65536, -1, "900-800", "${PORT}:80", "80/secret",
                      {"target": True}, {"target": 80, "published": "DO_NOT_LOG"}):
            with self.subTest(value=value):
                self.assertIsNone(runner.topology_port(value))

    def test_transport_streams_read_only_runner_without_preparing_artifacts(self):
        config = {"target": self.target, "ssh": {"private": "DO_NOT_LOG"}}
        with patch.object(transport.fleet, "resolve_target", return_value=config), \
             patch.object(transport, "Ssh"), patch.object(transport, "run_remote", return_value=self.read()) as remote, \
             patch.object(transport.artifacts, "prepare", side_effect=AssertionError("No artifact catalog reads")):
            result = transport.target_topology(self.root, "pilot-dev")
            self.assertEqual("pilot-dev", result["targetId"])
            self.assertEqual(("topology", self.target), remote.call_args.args[1:])

    def test_transport_unavailable_is_safe_and_keeps_target_identity(self):
        config = {"target": self.target, "ssh": {}}
        with patch.object(transport.fleet, "resolve_target", return_value=config), \
             patch.object(transport, "Ssh"), \
             patch.object(transport, "run_remote", side_effect=runner.CommandFailed("DO_NOT_LOG")):
            result = transport.target_topology(self.root, "pilot-dev")
            self.assert_unavailable(result)
            self.assertEqual("pilot-dev", result["targetId"])
            self.assertNotIn("DO_NOT_LOG", json.dumps(result))


if __name__ == "__main__":
    unittest.main()
