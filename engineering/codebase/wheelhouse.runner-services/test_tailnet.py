"""Tailscale through the owner's OAuth client, against the rig's fake Tailscale API over real HTTP."""
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import fleet
import tailnet
from runner import CommandFailed, Rejected

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "deployment" / "rehearsal"))
import fake_tailscale  # noqa: E402

KIT = Path(__file__).resolve().parents[2] / "deployment" / "vps"
CLIENT = {"clientId": "kWheelhouseCNTRL", "clientSecret": "tskey-client-kWheelhouseCNTRL-secretvalue"}


def server(slug, host, provider=fleet.VpsProvider.CONTABO):
    return fleet.Server(slug, slug, provider, host, "eu", "wheelhouse-deploy")


class FakeApi(unittest.TestCase):
    """A temporary runner root with the OAuth client placed, and the fake API it talks to."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.fake = fake_tailscale.Tailnet(CLIENT["clientId"], CLIENT["clientSecret"], "tail1234.ts.net",
                                           (KIT / "tailnet-policy.hujson").read_text())
        self.server = fake_tailscale.serve(self.fake)
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        environment = patch.dict(os.environ, {"WHEELHOUSE_REHEARSAL": "1", "WHEELHOUSE_TAILSCALE_API":
                                              "http://127.0.0.1:" + str(self.server.server_address[1])})
        environment.start()
        self.addCleanup(environment.stop)
        self.place_client(CLIENT)
        servers = patch.object(fleet, "SERVERS", ())
        servers.start()
        self.addCleanup(servers.stop)

    def place_client(self, value, mode=0o600):
        path = self.root / "providers" / "tailscale" / "oauth-client.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value))
        path.chmod(mode)
        return path

    def device(self, hostname, address, tags, endpoints=()):
        self.fake.devices.append({"id": hostname, "nodeId": "n" + hostname, "name": hostname + ".tail1234.ts.net",
                                  "hostname": hostname, "addresses": [address, "fd7a:115c:a1e0::1"], "tags": list(tags),
                                  "connectedToControl": True, "keyExpiryDisabled": True,
                                  "expires": "0001-01-01T00:00:00Z", "created": "2026-10-19T00:00:00Z",
                                  "clientConnectivity": {"endpoints": list(endpoints)}, "machineKey": "mkey:secret"})


class ClientFileTests(FakeApi):
    def test_without_the_file_tailscale_reads_as_not_connected(self):
        (self.root / tailnet.CLIENT_FILE).unlink()
        result = tailnet.status(self.root)
        self.assertEqual((False, False), (result["configured"], result["connected"]))
        self.assertIn("oauth-client.json", result["reason"])

    def test_a_readable_or_symlinked_file_is_refused_never_used(self):
        self.place_client(CLIENT, 0o644)
        with self.assertRaisesRegex(Rejected, "mode 0600"):
            tailnet.read_client(self.root)
        path = self.root / tailnet.CLIENT_FILE
        target = self.root / "elsewhere.json"
        target.write_text(json.dumps(CLIENT))
        target.chmod(0o600)
        path.unlink()
        path.symlink_to(target)
        with self.assertRaisesRegex(Rejected, "regular file"):
            tailnet.read_client(self.root)
        self.assertEqual([], self.fake.calls)

    def test_a_secret_that_is_no_client_secret_is_refused(self):
        self.place_client({"clientId": "k1", "clientSecret": "tskey-auth-abc"})
        result = tailnet.status(self.root)
        self.assertFalse(result["connected"])
        self.assertIn("clientSecret", result["reason"])

    def test_a_refused_client_reads_as_configured_not_connected(self):
        self.place_client({**CLIENT, "clientSecret": "tskey-client-wrong"})
        result = tailnet.status(self.root)
        self.assertEqual((True, False), (result["configured"], result["connected"]))
        self.assertIn("refused the OAuth client", result["reason"])
        self.assertNotIn("wrong", json.dumps(result))

    def test_the_api_address_changes_only_on_the_rig(self):
        with patch.dict(os.environ, {"WHEELHOUSE_REHEARSAL": ""}):
            self.assertEqual("https://api.tailscale.com", tailnet.api_base())


class NodesTests(FakeApi):
    def test_each_server_shows_its_node_and_people_s_devices_stay_out(self):
        self.device("ocharo-vps", "100.101.1.2", ["tag:products"], ["203.0.113.5:41641"])
        self.device("ocharo-desk", "100.101.1.3", ["tag:product-site"])
        self.device("owner-laptop", "100.101.1.9", [])
        with patch.object(fleet, "SERVERS", (server("ocharo-products", "100.101.1.2"),)):
            result = tailnet.status(self.root)
        self.assertTrue(result["connected"])
        self.assertEqual("tail1234.ts.net", result["tailnet"])
        self.assertEqual([{"serverId": "ocharo-products", "nodeId": "nocharo-vps"}], result["servers"])
        nodes = {node["hostname"]: node for node in result["nodes"]}
        self.assertEqual({"ocharo-vps", "ocharo-desk"}, set(nodes))
        self.assertEqual(("ocharo-products", "host", True, True),
                         (nodes["ocharo-vps"]["serverId"], nodes["ocharo-vps"]["role"], nodes["ocharo-vps"]["online"],
                          nodes["ocharo-vps"]["keyExpiryDisabled"]))
        self.assertEqual("site", nodes["ocharo-desk"]["role"])
        self.assertNotIn("mkey:secret", json.dumps(result))

    def test_a_server_matches_the_node_its_enrollment_recorded(self):
        self.device("fresh", "100.101.1.4", ["tag:products"])
        record = self.root / "infra" / "enrollments" / "fresh-host.json"
        record.parent.mkdir(parents=True)
        record.write_text(json.dumps({"facts": {"nodeId": "nfresh"}}))
        with patch.object(fleet, "SERVERS", (server("fresh-host", "203.0.113.9"),)):
            result = tailnet.status(self.root)
        self.assertEqual("fresh-host", result["nodes"][0]["serverId"])


class MintTests(FakeApi):
    def test_a_key_is_single_use_pre_approved_tagged_and_short_lived(self):
        record, key = tailnet.Tailscale(self.root).mint(tailnet.HOST_TAG, "wheelhouse enroll ocharo-products")
        minted = next(iter(self.fake.keys.values()))
        self.assertEqual((False, True, False, ["tag:products"], 900),
                         (minted["reusable"], minted["preauthorized"], minted["ephemeral"], minted["tags"],
                          minted["expirySeconds"]))
        self.assertTrue(key.startswith("tskey-auth-"))
        self.assertEqual(minted["id"], record["keyId"])
        self.assertNotIn(key, json.dumps(record))
        self.assertNotIn("tskey", json.dumps(record))

    def test_only_products_and_site_tags_are_minted(self):
        with self.assertRaisesRegex(Rejected, "products hosts and site nodes"):
            tailnet.Tailscale(self.root).mint("tag:control", "x")
        self.assertEqual({}, self.fake.keys)

    def test_a_used_key_cannot_join_twice(self):
        _, key = tailnet.Tailscale(self.root).mint(tailnet.SITE_TAG, "site")
        self.assertEqual(200, self.fake.join({"authKey": key, "hostname": "a", "tags": ["tag:product-site"]})[0])
        self.assertEqual(401, self.fake.join({"authKey": key, "hostname": "b", "tags": ["tag:product-site"]})[0])


class PolicyTests(FakeApi):
    def setUp(self):
        super().setUp()
        self.device("ocharo-vps", "100.101.1.2", ["tag:products"])
        servers = patch.object(fleet, "SERVERS", (server("ocharo-products", "100.101.1.2"),
                                                   server("control", "172.30.0.1", fleet.VpsProvider.OVHCLOUD)))
        servers.start()
        self.addCleanup(servers.stop)

    def test_the_review_shows_the_inventory_s_hosts_and_tests_as_a_diff(self):
        review = tailnet.policy_review(self.root)
        self.assertTrue(review["changed"])
        self.assertEqual([{"serverId": "ocharo-products", "address": "100.101.1.2"}], review["hosts"])
        added = [line["text"].strip() for hunk in review["diff"] for line in hunk["lines"] if line["kind"] == "add"]
        self.assertIn('"ocharo-products": "100.101.1.2",', added)
        self.assertIn('{"src": "tag:control", "accept": ["ocharo-products:22"]},', added)
        self.assertEqual({"ok": True, "problems": []}, review["validation"])
        self.assertTrue(review["guard"]["ok"])
        self.assertEqual(self.fake.etag(), review["etag"])

    def test_apply_saves_the_reviewed_policy_and_keeps_the_one_before(self):
        before = self.fake.policy
        review = tailnet.policy_review(self.root)
        result = tailnet.apply_policy(self.root, review["etag"], review["renderedDigest"], "owner")
        self.assertTrue(result["applied"])
        self.assertEqual(review["rendered"], self.fake.policy)
        self.assertEqual(before, (self.root / tailnet.POLICY_DIR / "previous.hujson").read_text())
        self.assertFalse(tailnet.policy_review(self.root)["changed"])
        reverted = tailnet.revert_policy(self.root, self.fake.etag(), "owner")
        self.assertTrue(reverted["reverted"])
        self.assertEqual(before, self.fake.policy)
        self.assertEqual(review["rendered"], (self.root / tailnet.POLICY_DIR / "previous.hujson").read_text())

    def test_a_policy_changed_since_the_review_is_refused(self):
        review = tailnet.policy_review(self.root)
        self.fake.policy += "\n"
        with self.assertRaisesRegex(Rejected, "changed since this review"):
            tailnet.apply_policy(self.root, review["etag"], review["renderedDigest"], "owner")

    def test_an_inventory_changed_since_the_review_is_refused(self):
        review = tailnet.policy_review(self.root)
        with patch.object(fleet, "SERVERS", ()):
            with self.assertRaisesRegex(Rejected, "inventory changed"):
                tailnet.apply_policy(self.root, review["etag"], review["renderedDigest"], "owner")

    def test_tailscale_s_failing_tests_block_the_apply(self):
        self.fake.devices.clear()  # the products host's node is gone, so its control-path test fails
        review = tailnet.policy_review(self.root)
        self.assertFalse(review["validation"]["ok"])
        self.assertIn("ocharo-products:22", review["validation"]["problems"][-1])
        before = self.fake.policy
        with self.assertRaisesRegex(Rejected, "Tailscale refused"):
            tailnet.apply_policy(self.root, review["etag"], review["renderedDigest"], "owner")
        self.assertEqual(before, self.fake.policy)

    def test_a_policy_that_cuts_the_control_path_is_refused_even_where_tailscale_would_save_it(self):
        template = (KIT / "tailnet-policy.hujson").read_text().replace(
            '{"src": "tag:control", "accept": ["tag:products:22"],', '{"src": "tag:control",')
        self.assertFalse(tailnet.control_path_kept(template))
        self.assertTrue(tailnet.control_path_kept((KIT / "tailnet-policy.hujson").read_text()))
        (self.root / tailnet.POLICY_DIR).mkdir(parents=True)
        (self.root / tailnet.POLICY_DIR / "previous.hujson").write_text(template)
        (self.root / tailnet.POLICY_DIR / "previous.json").write_text("{}")
        with self.assertRaisesRegex(Rejected, "cut tag:control"):
            tailnet.revert_policy(self.root, self.fake.etag(), "owner")

    def test_hujson_comments_inside_strings_survive(self):
        self.assertEqual({"a": "http://x//y", "b": [1]}, tailnet.parse_policy('{"a": "http://x//y", // c\n "b": [1,],}'))

    def test_the_template_needs_each_marker_once(self):
        with self.assertRaisesRegex(Rejected, "marker"):
            tailnet.render("{}\n", [])


class UnreachableTests(unittest.TestCase):
    def test_an_unreachable_api_is_a_transient_failure(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / tailnet.CLIENT_FILE
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps(CLIENT))
            path.chmod(0o600)
            with patch.dict(os.environ, {"WHEELHOUSE_REHEARSAL": "1", "WHEELHOUSE_TAILSCALE_API": "http://127.0.0.1:9"}):
                with self.assertRaises(CommandFailed):
                    tailnet.Tailscale(temporary).devices()


if __name__ == "__main__":
    unittest.main()
