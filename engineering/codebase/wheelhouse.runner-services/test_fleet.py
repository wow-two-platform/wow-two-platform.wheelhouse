import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import catalog
import fleet
import transport


class FleetTests(unittest.TestCase):
    def setUp(self):
        # Targets name products; these tests take the local server's fixture products as the inventory's.
        products = patch.object(catalog, 'PRODUCTS', catalog.LOCAL_PRODUCTS)
        products.start()
        self.addCleanup(products.stop)

    def test_external_json_cannot_register_a_host(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'targets').mkdir()
            (root / 'targets/injected.json').write_text(json.dumps({'ssh': {'host': 'unreviewed'}}))
            with patch.object(fleet, 'SERVERS', ()), patch.object(fleet, 'TARGETS', ()):
                self.assertEqual([], transport.targets(root))
                with self.assertRaisesRegex(ValueError, 'not in the inventory'):
                    fleet.resolve_target(root, 'injected')

    def test_provider_and_environment_require_supported_enums(self):
        server = fleet.Server('pilot', 'Pilot', 'CustomProvider', 'vps.example.net', 'hel1')
        with patch.object(fleet, 'SERVERS', (server,)):
            with self.assertRaisesRegex(ValueError, 'Unsupported server'):
                fleet.servers()

    def test_code_binding_selects_host_and_secret_references(self):
        server = fleet.Server('pilot', 'Pilot', fleet.VpsProvider.HETZNER, 'vps.example.net', 'hel1')
        target = fleet.Target('foreverpin-test', 'pilot', 'foreverpin', fleet.DeploymentEnvironment.TEST,
                              (('management', '/srv/secrets/management.json'), ('redirect', '/srv/secrets/redirect.json')),
                              'platform')
        with patch.object(fleet, 'SERVERS', (server,)), patch.object(fleet, 'TARGETS', (target,)):
            config = fleet.resolve_target(Path('/data/deployments'), target.id)
            self.assertEqual('hetzner', config['provider'])
            self.assertEqual('vps.example.net', config['ssh']['host'])
            self.assertEqual('/data/deployments/ssh/pilot/identity', config['ssh']['keyFile'])
            self.assertEqual('test', config['target']['environment'])
            self.assertEqual('platform', config['target']['variables']['PLATFORM_NETWORK'])
            self.assertEqual((False, False), (config['acceptsCandidates'], config['needsConfirmation']))

    def test_local_targets_exist_only_behind_the_switch(self):
        with patch.dict('os.environ', {}, clear=False):
            import os
            os.environ.pop('WHEELHOUSE_REHEARSAL', None)
            self.assertNotIn('foreverpin-dev', [target.id for target in fleet.active_targets()])
            with self.assertRaisesRegex(ValueError, 'not in the inventory'):
                fleet.resolve_target(Path('/data/deployments'), 'foreverpin-dev')
        with patch.dict('os.environ', {'WHEELHOUSE_REHEARSAL': '1'}):
            self.assertEqual(['foreverpin-dev', 'foreverpin-test', 'foreverpin-prod', 'wheelhouse-dev'],
                             [target.id for target in fleet.active_targets()])
            config = fleet.resolve_target(Path('/data/deployments'), 'foreverpin-dev')
            self.assertEqual(('local', 2222, 'dev', 'local'),
                             (config['provider'], config['ssh']['port'], config['target']['environment'],
                              config['serverId']))

    def test_local_prod_needs_typed_confirmation_and_only_dev_takes_candidates(self):
        with patch.dict('os.environ', {'WHEELHOUSE_REHEARSAL': '1'}):
            policy = {environment: (config['acceptsCandidates'], config['needsConfirmation'], config['requiresTestPass'])
                      for environment in ('dev', 'test', 'prod')
                      for config in [fleet.resolve_target(Path('/data'), 'foreverpin-' + environment)]}
            self.assertEqual({'dev': (True, False, False), 'test': (False, False, False), 'prod': (False, True, True)},
                             policy)
            self.assertEqual({'dev': False, 'test': True, 'prod': False},
                             {environment: fleet.resolve_target(Path('/data'), 'foreverpin-' + environment)
                              ['acceptsTestBuilds'] for environment in ('dev', 'test', 'prod')})

    def test_local_sites_follow_the_localhost_pattern_on_every_environment(self):
        with patch.dict('os.environ', {'WHEELHOUSE_REHEARSAL': '1'}):
            ingress = fleet.resolve_target(Path('/data'), 'foreverpin-prod')['target']['ingress']
            self.assertEqual(('http', 18080, ['web'], '{site}-{product}.{environment}.localhost'),
                             (ingress['scheme'], ingress['port'], ingress['entryPoints'], ingress['pattern']))

    def test_prod_on_a_vps_names_its_own_hosts_instead_of_the_preview_pattern(self):
        server = fleet.Server('pilot', 'Pilot', fleet.VpsProvider.HETZNER, 'vps.example.net', 'hel1',
                              ingress=fleet.Ingress(pattern='{site}-{product}.{environment}.preview.example'))
        prod = fleet.Target('foreverpin-prod', 'pilot', 'foreverpin', fleet.DeploymentEnvironment.PROD, (),
                            'platform', sites=(('app', 'app.foreverpin.example'),))
        dev = fleet.Target('foreverpin-dev', 'pilot', 'foreverpin', fleet.DeploymentEnvironment.DEV, (), 'platform')
        with patch.object(fleet, 'SERVERS', (server,)), patch.object(fleet, 'TARGETS', (prod, dev)):
            prod_ingress = fleet.resolve_target(Path('/data'), 'foreverpin-prod')['target']['ingress']
            dev_ingress = fleet.resolve_target(Path('/data'), 'foreverpin-dev')['target']['ingress']
        self.assertEqual((None, {'app': 'app.foreverpin.example'}), (prod_ingress['pattern'], prod_ingress['hosts']))
        self.assertEqual('{site}-{product}.{environment}.preview.example', dev_ingress['pattern'])
        self.assertEqual(('https', ['websecure'], 'letsencrypt'),
                         (prod_ingress['scheme'], prod_ingress['entryPoints'], prod_ingress['certResolver']))
        # A VPS ingress publishes on the host's own ports, so the runner probes loopback by default.
        self.assertEqual(('https://127.0.0.1', None), (prod_ingress['probe'], prod_ingress['privateProbe']))

    def test_local_sites_are_probed_through_the_rig_ingress(self):
        with patch.dict('os.environ', {'WHEELHOUSE_REHEARSAL': '1'}):
            ingress = fleet.resolve_target(Path('/data'), 'foreverpin-dev')['target']['ingress']
        self.assertEqual(('http://ingress:80', 'http://ingress:80'), (ingress['probe'], ingress['privateProbe']))

    def test_site_hosts_must_be_valid_names(self):
        server = fleet.Server('pilot', 'Pilot', fleet.VpsProvider.HETZNER, 'vps.example.net', 'hel1')
        broken = fleet.Target('foreverpin-prod', 'pilot', 'foreverpin', fleet.DeploymentEnvironment.PROD, (),
                              'platform', sites=(('app', 'App.Example'),))
        with patch.object(fleet, 'SERVERS', (server,)), patch.object(fleet, 'TARGETS', (broken,)):
            with self.assertRaisesRegex(ValueError, 'Invalid site host'):
                fleet.resolve_target(Path('/data'), 'foreverpin-prod')

    def test_console_inside_the_rig_reaches_services_by_name_with_host_settings_paths(self):
        import importlib
        try:
            with patch.dict('os.environ', {'WHEELHOUSE_REHEARSAL': 'network', 'REHEARSAL_STATE': '/host/state'}):
                importlib.reload(fleet)
                config = fleet.resolve_target(Path('/data/deployments'), 'foreverpin-test')
                self.assertEqual(('target', 22), (config['ssh']['host'], config['ssh']['port']))
                self.assertEqual('http://vault:8080', fleet.vaults()[0]['url'])
                self.assertEqual('/host/state/secrets/test/management.json', config['target']['settings']['management'])
        finally:
            importlib.reload(fleet)  # later tests expect the host view

    def test_vaults_come_only_from_code_and_keep_urls_server_side(self):
        server = fleet.Server('pilot', 'Pilot', fleet.VpsProvider.HETZNER, 'vps.example.net', 'hel1')
        vault = fleet.Vault('pilot-vault', 'Pilot vault', 'pilot', 'http://secrets-vault:8080')
        with patch.object(fleet, 'SERVERS', (server,)), patch.object(fleet, 'VAULTS', (vault,)):
            self.assertEqual([{'id': 'pilot-vault', 'name': 'Pilot vault', 'serverId': 'pilot',
                               'url': 'http://secrets-vault:8080'}], fleet.vaults())
        for url in ('http://vault:8080/path', 'file:///etc/passwd', 'http://vault:8080?x=1'):
            broken = fleet.Vault('pilot-vault', 'Pilot vault', 'pilot', url)
            with patch.object(fleet, 'SERVERS', (server,)), patch.object(fleet, 'VAULTS', (broken,)):
                with self.assertRaisesRegex(ValueError, 'Unsupported vault'):
                    fleet.vaults()

    def test_vault_needs_a_host_defined_in_code(self):
        orphan = fleet.Vault('pilot-vault', 'Pilot vault', 'missing', 'http://secrets-vault:8080')
        with patch.object(fleet, 'SERVERS', ()), patch.object(fleet, 'VAULTS', (orphan,)):
            with self.assertRaisesRegex(ValueError, 'not in the inventory'):
                fleet.vaults()

    def test_duplicate_server_ids_are_rejected(self):
        server = fleet.Server('pilot', 'Pilot', fleet.VpsProvider.HETZNER, 'vps.example.net', 'hel1')
        with patch.object(fleet, 'SERVERS', (server, server)):
            with self.assertRaisesRegex(ValueError, 'Duplicate server'):
                fleet.servers()
