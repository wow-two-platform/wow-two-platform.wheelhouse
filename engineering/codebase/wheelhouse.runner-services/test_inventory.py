import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import catalog
import fleet
import inventory

RIG = {'WHEELHOUSE_REHEARSAL': '1'}
NO_RIG = {'WHEELHOUSE_REHEARSAL': ''}

SNAPSHOT = {
    'version': 1,
    'products': [{'slug': 'pilot', 'name': 'Pilot', 'description': 'A pilot product.', 'repository': 'owner/pilot',
                  'defaultBranch': 'main',
                  'release': {'asset': 'pilot-release.tar.gz', 'workflow': 'publish.yml',
                              'images': [{'service': 'api', 'image': 'ghcr.io/owner/pilot/api'}]}}],
    'servers': [{'id': 'hel1', 'name': 'Helsinki', 'provider': 'hetzner', 'host': 'vps.example.net', 'region': 'hel1',
                 'sshUser': 'deploy', 'sshPort': 22,
                 'ingress': {'scheme': 'https', 'port': None, 'entryPoints': ['websecure'], 'privateEntryPoints': [],
                             'certResolver': 'letsencrypt', 'pattern': None, 'probe': None, 'privateProbe': None}}],
    'targets': [{'id': 'pilot-prod', 'serverId': 'hel1', 'product': 'pilot', 'environment': 'prod',
                 'network': 'platform', 'root': '/srv/wheelhouse',
                 'settings': [{'service': 'api', 'path': '/srv/settings/pilot/api.json'}],
                 'smoke': [{'service': 'api', 'path': '/health', 'status': 200}],
                 'sites': [{'site': 'app', 'host': 'pilot.example.com'}]}],
    'vaults': [{'id': 'hel1-vault', 'name': 'Helsinki vault', 'serverId': 'hel1', 'url': 'http://vault:8080'}],
}


class InventoryTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        self.saved = (catalog.PRODUCTS, fleet.SERVERS, fleet.TARGETS, fleet.VAULTS)

    def tearDown(self):
        catalog.PRODUCTS, fleet.SERVERS, fleet.TARGETS, fleet.VAULTS = self.saved
        self.directory.cleanup()

    def write(self, document):
        (self.root / inventory.SNAPSHOT).write_text(json.dumps(document))

    def test_without_a_snapshot_the_inventory_is_empty(self):
        catalog.PRODUCTS = (catalog.Product('stale', 'Stale', '', 'owner/stale'),)
        inventory.install(self.root)
        self.assertEqual(((), (), (), ()), (catalog.PRODUCTS, fleet.SERVERS, fleet.TARGETS, fleet.VAULTS))

    def test_a_snapshot_installs_products_servers_targets_and_vaults(self):
        self.write(SNAPSHOT)
        inventory.install(self.root)
        with patch.dict(os.environ, NO_RIG):
            self.assertEqual(['pilot'], [item.slug for item in catalog.products()])
            self.assertEqual([{'id': 'hel1', 'name': 'Helsinki', 'provider': 'hetzner', 'host': 'vps.example.net',
                               'region': 'hel1', 'sshUser': 'deploy'}], fleet.servers())
            self.assertEqual(['hel1-vault'], [item['id'] for item in fleet.vaults()])
            resolved = fleet.resolve_target(self.root, 'pilot-prod')
        self.assertEqual({'app': 'pilot.example.com'}, resolved['target']['ingress']['hosts'])
        self.assertEqual({'api': '/srv/settings/pilot/api.json'}, resolved['target']['settings'])
        self.assertTrue(resolved['requiresTestPass'])
        self.assertEqual(str(self.root / 'ssh' / 'hel1' / 'identity'), resolved['ssh']['keyFile'])

    def test_a_malformed_snapshot_is_refused_whole(self):
        catalog.PRODUCTS = ()
        broken = json.loads(json.dumps(SNAPSHOT))
        broken['targets'][0]['smoke'][0]['status'] = '200'
        for document in (broken, {**SNAPSHOT, 'version': 2}, {**SNAPSHOT, 'servers': {}}, ['not', 'an', 'object']):
            self.write(document)
            with self.assertRaises(ValueError):
                inventory.install(self.root)
        self.assertEqual((), catalog.PRODUCTS)

    def test_fixtures_exist_only_on_the_local_server_and_round_trip(self):
        with patch.dict(os.environ, NO_RIG):
            with self.assertRaisesRegex(ValueError, 'only on the local server'):
                inventory.fixtures()
        with patch.dict(os.environ, RIG):
            exported = inventory.fixtures()
            self.write(exported)
            inventory.install(self.root)
        self.assertEqual(catalog.LOCAL_PRODUCTS, catalog.PRODUCTS)
        self.assertEqual((fleet.LOCAL_SERVERS, fleet.LOCAL_TARGETS, fleet.LOCAL_VAULTS),
                         (fleet.SERVERS, fleet.TARGETS, fleet.VAULTS))

    def test_a_database_row_wins_over_a_fixture_with_the_same_id(self):
        moved = json.loads(json.dumps(SNAPSHOT))
        moved['servers'][0].update({'id': 'local', 'name': 'Local server (database)'})
        moved['targets'], moved['vaults'] = [], []
        self.write(moved)
        inventory.install(self.root)
        with patch.dict(os.environ, RIG):
            servers = fleet.servers()
        self.assertEqual([('local', 'Local server (database)')], [(item['id'], item['name']) for item in servers])

    def test_the_cli_reads_the_snapshot_under_its_root(self):
        self.write(SNAPSHOT)
        result = subprocess.run([sys.executable, str(Path(__file__).with_name('transport.py')), 'servers',
                                 '--root', str(self.root)], capture_output=True, text=True,
                                env={**os.environ, **NO_RIG})
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(['hel1'], [item['id'] for item in json.loads(result.stdout)])


if __name__ == '__main__':
    unittest.main()
