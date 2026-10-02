import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import artifacts
import catalog
import fleet
import transport

RIG = {'WHEELHOUSE_REHEARSAL': '1'}
NO_RIG = {'WHEELHOUSE_REHEARSAL': ''}


class CatalogTests(unittest.TestCase):
    def test_products_refuse_a_duplicate_slug_or_an_invalid_repository(self):
        product = catalog.Product('pilot', 'Pilot', 'A pilot product.', 'owner/pilot')
        with patch.dict(os.environ, NO_RIG):
            with patch.object(catalog, 'PRODUCTS', (product, product)):
                with self.assertRaisesRegex(ValueError, 'Duplicate product slug'):
                    catalog.products()
            with patch.object(catalog, 'PRODUCTS', (catalog.Product('pilot', 'Pilot', 'A pilot.', 'not a repository'),)):
                with self.assertRaisesRegex(ValueError, 'Invalid product repository'):
                    catalog.products()

    def test_a_release_names_an_asset_images_and_a_workflow_it_can_trust(self):
        for release in (catalog.Release('../escape', ()), catalog.Release('a.tar.gz', (('api', 'NOT AN IMAGE'),)),
                        catalog.Release('a.tar.gz', (), workflow='build.sh')):
            product = catalog.Product('pilot', 'Pilot', '', 'owner/pilot', release=release)
            with patch.dict(os.environ, NO_RIG), patch.object(catalog, 'PRODUCTS', (product,)):
                with self.assertRaisesRegex(ValueError, 'Invalid'):
                    catalog.products()

    def test_the_local_server_adds_its_fixture_products_only_where_the_inventory_lacks_them(self):
        renamed = catalog.Product('foreverpin', 'ForeverPin (database)', '', 'owner/foreverpin')
        with patch.object(catalog, 'PRODUCTS', (renamed,)):
            with patch.dict(os.environ, NO_RIG):
                self.assertEqual(['foreverpin'], [item.slug for item in catalog.products()])
            with patch.dict(os.environ, RIG):
                listed = catalog.products()
        self.assertEqual(['foreverpin', 'wheelhouse'], [item.slug for item in listed])
        self.assertEqual('ForeverPin (database)', listed[0].name)

    def test_every_fixture_target_names_a_product(self):
        with patch.dict(os.environ, RIG), patch.object(catalog, 'PRODUCTS', ()):
            slugs = {product.slug for product in catalog.products()}
            for binding in fleet.active_targets():
                self.assertIn(binding.product, slugs, binding.id)

    def test_a_target_for_an_unknown_product_is_refused(self):
        server = fleet.Server('pilot', 'Pilot', fleet.VpsProvider.HETZNER, 'vps.example.net', 'hel1')
        target = fleet.Target('ghost-dev', 'pilot', 'ghost', fleet.DeploymentEnvironment.DEV, (), 'platform')
        with patch.dict(os.environ, NO_RIG), patch.object(fleet, 'SERVERS', (server,)), \
                patch.object(fleet, 'TARGETS', (target,)):
            with self.assertRaisesRegex(ValueError, 'Product is not in the inventory'):
                fleet.resolve_target(Path('/data/deployments'), 'ghost-dev')

    def test_release_sources_come_from_products_that_publish(self):
        with patch.dict(os.environ, RIG), patch.object(catalog, 'PRODUCTS', ()):
            publishing = [product for product in catalog.products() if product.release is not None]
            sources = artifacts.sources()
        self.assertEqual([product.slug for product in publishing], [source.product for source in sources])
        for product, source in zip(publishing, sources):
            self.assertEqual((product.repository, product.release.asset, product.release.images),
                             (source.repository, source.asset_name, source.images))

    def test_a_product_without_a_release_source_still_lists_its_commits(self):
        def fake_fetch(url, *args, **kwargs):
            self.assertIn('wow-two-platform/wow-two-platform.wheelhouse/commits', url)
            return json.dumps([{'sha': 'a' * 40, 'commit': {'message': 'feat: x', 'author': {'name': 'Max'}}}])
        with patch.dict(os.environ, RIG), patch.object(catalog, 'PRODUCTS', ()), patch.object(artifacts, 'fetch', fake_fetch):
            listed = artifacts.commits('wheelhouse', 'main')
        self.assertEqual([(None, False)], [(item['buildId'], item['canBuild']) for item in listed])
        self.assertTrue(listed[0]['url'].startswith('https://github.com/wow-two-platform/wow-two-platform.wheelhouse/'))


class SitesTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        (self.root / 'jobs').mkdir()
        (self.root / 'observed').mkdir()

    def tearDown(self):
        self.directory.cleanup()

    def record(self, job_id, target_id, status, submitted, sites):
        (self.root / 'jobs' / (job_id + '.json')).write_text(json.dumps(
            {'id': job_id, 'targetId': target_id, 'status': 'submitted', 'submittedAt': submitted}))
        (self.root / 'observed' / (job_id + '.json')).write_text(json.dumps({'status': status, 'sites': sites}))

    def test_sites_come_from_the_newest_succeeded_rollout_and_drop_unsafe_addresses(self):
        self.record('00000000-0000-0000-0000-000000000001', 'foreverpin-dev', 'succeeded', '2026-09-28T00:00:00+00:00',
                    [{'name': 'app', 'url': 'http://app.old.example', 'exposure': 'public'}])
        self.record('00000000-0000-0000-0000-000000000002', 'foreverpin-dev', 'succeeded', '2026-09-29T00:00:00+00:00',
                    [{'name': 'app', 'url': 'https://app.example.com', 'exposure': 'public'},
                     {'name': 'admin', 'url': 'https://admin.example.com/ops', 'exposure': 'private'},
                     {'name': 'bad', 'url': 'javascript:alert(1)'}])
        self.record('00000000-0000-0000-0000-000000000003', 'foreverpin-dev', 'failed', '2026-09-30T00:00:00+00:00',
                    [{'name': 'app', 'url': 'https://broken.example.com'}])
        self.assertEqual({'foreverpin-dev': [{'name': 'app', 'url': 'https://app.example.com', 'exposure': 'public'},
                                             {'name': 'admin', 'url': 'https://admin.example.com/ops',
                                              'exposure': 'private'}]},
                         transport.latest_sites(self.root))

    def test_a_target_without_a_succeeded_rollout_has_no_sites(self):
        self.record('00000000-0000-0000-0000-000000000004', 'foreverpin-test', 'failed', '2026-09-30T00:00:00+00:00',
                    [{'name': 'app', 'url': 'https://broken.example.com'}])
        self.assertEqual({}, transport.latest_sites(self.root))


if __name__ == '__main__':
    unittest.main()
