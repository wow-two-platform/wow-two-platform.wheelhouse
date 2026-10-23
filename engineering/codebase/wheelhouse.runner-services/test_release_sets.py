"""A product built from several repositories (decision 70): its sources, release sets, composition and deploys."""
import hashlib
import io
import json
import os
from pathlib import Path
import tarfile
import tempfile
import unittest
import zipfile
from unittest.mock import patch
import artifacts
import catalog
import history
import inventory
import release_sets
import runner
import transport

APP, STUDIO = 'owner/pilot-app', 'owner/pilot-studio'
NO_RIG = {'WHEELHOUSE_REHEARSAL': ''}


def product(components=True, studio_images=(('studio', 'ghcr.io/owner/pilot-studio/studio'),)):
    app = catalog.Release('pilot-release.tar.gz', (('api', 'ghcr.io/owner/pilot-app/api'),
                                                   ('web', 'ghcr.io/owner/pilot-app/web')),
                          workflow='publish-docker-image.yml')
    studio = catalog.Release('pilot-release.tar.gz', studio_images, workflow='publish-docker-image.yml')
    return catalog.Product('pilot', 'Pilot', 'One product, two repositories.', APP, release=app,
                           components=(catalog.Component(STUDIO, studio),) if components else ())


def bundle(release, images, commit, kind='release', sites=None, schema=1, volumes=None, depends=None, **extra):
    """One repository's bundle: release.json and compose.json bytes, as the shared publish workflow writes them."""
    services = {}
    for name, image in images.items():
        definition = {'image': image, 'platform': 'linux/amd64', 'healthcheck': {'test': ['CMD', 'true']},
                      'networks': {'platform': {'aliases': ['pilot-${DEPLOY_ENVIRONMENT}-' + name]}}}
        for volume, path in (volumes or {}).get(name, {}).items():
            definition.setdefault('volumes', []).append({'type': 'volume', 'source': volume, 'target': path})
        if name in (depends or {}):
            definition['depends_on'] = {other: {'condition': 'service_healthy'} for other in depends[name]}
        services[name] = definition
    compose = {'services': services, 'networks': {'platform': {'external': True, 'name': '${PLATFORM_NETWORK}'}}}
    named = sorted({volume for mounts in (volumes or {}).values() for volume in mounts})
    if named:
        compose['volumes'] = {volume: {} for volume in named}
    compose_bytes = json.dumps(compose).encode()
    manifest = {'schemaVersion': schema, 'product': 'pilot', 'release': release, 'kind': kind, 'sourceCommit': commit,
                'platform': 'linux/amd64', 'rollbackCompatible': True,
                'composeSha256': hashlib.sha256(compose_bytes).hexdigest(), 'images': images,
                'requiredConfiguration': {name: ['AllowedHosts'] for name in images},
                'versions': {name: {'version': release.lstrip('v'), 'changedIn': release} for name in images},
                'ports': {name: 8080 for name in images}, 'sites': sites or {}, **extra}
    if kind == 'candidate':
        manifest['branch'] = 'main'
    return manifest, compose_bytes


def digested(images):
    return {service: image + '@sha256:' + str(index) * 64 for index, (service, image) in enumerate(images, 1)}


def archive(manifest, compose_bytes):
    data = io.BytesIO()
    with tarfile.open(fileobj=data, mode='w:gz') as package:
        for name, payload in (('release.json', json.dumps(manifest).encode()), ('compose.json', compose_bytes)):
            member = tarfile.TarInfo(name)
            member.size = len(payload)
            package.addfile(member, io.BytesIO(payload))
    return data.getvalue()


def zipped(manifest, compose_bytes):
    data = io.BytesIO()
    with zipfile.ZipFile(data, 'w') as package:
        package.writestr('release.json', json.dumps(manifest))
        package.writestr('compose.json', compose_bytes)
    return data.getvalue()


class GitHub:
    """Two repositories' releases and commit builds, served as GitHub's API serves them."""

    def __init__(self):
        self.releases = {APP: [], STUDIO: []}
        self.archives, self.commits, self.builds = {}, {}, {APP: [], STUDIO: []}
        self.zips = {}

    def release(self, repository, tag, published, asset, manifest, compose_bytes):
        payload = archive(manifest, compose_bytes)
        self.archives[(repository, tag)] = payload
        self.commits[(repository, tag)] = manifest['sourceCommit']
        self.releases[repository].insert(0, {
            'tag_name': tag, 'draft': False, 'published_at': published, 'name': tag, 'body': 'notes for ' + tag,
            'assets': [{'name': 'pilot-release.tar.gz', 'id': asset, 'state': 'uploaded', 'size': len(payload),
                        'digest': 'sha256:' + hashlib.sha256(payload).hexdigest()}]})

    def build(self, repository, artifact, manifest, compose_bytes, created='2026-10-20T12:00:00Z'):
        self.zips[artifact] = zipped(manifest, compose_bytes)
        self.builds[repository].insert(0, {'id': artifact, 'name': 'bundle-' + manifest['sourceCommit'],
                                           'expired': False, 'size_in_bytes': 900, 'created_at': created,
                                           'expires_at': '2026-11-03T12:00:00Z',
                                           'workflow_run': {'head_branch': 'main'}})

    def fetch(self, url, limit=artifacts.MAX_ARCHIVE, accept=None):
        for repository in self.releases:
            api = artifacts.API + repository
            if url.startswith(api + '/releases?'):
                return json.dumps(self.releases[repository]).encode()
            if url.startswith(api + '/actions/artifacts?'):
                return json.dumps({'artifacts': self.builds.get(repository, [])}).encode()
            if url.startswith(api + '/actions/artifacts/') and url.endswith('/zip'):
                return self.zips[int(url.split('/')[-2])]
            if url.startswith(api + '/commits/'):
                tag = url.rsplit('/', 1)[1]
                return json.dumps({'sha': self.commits.get((repository, tag), tag)}).encode()
            if url.startswith('https://github.com/' + repository + '/releases/download/'):
                return self.archives[(repository, url.split('/')[-2])]
            if url.startswith(api + '/compare/'):
                return json.dumps({'commits': [{'sha': 'f' * 40, 'commit': {'message': 'feat: ' + repository,
                                                                            'author': {'name': 'Max'}}}],
                                   'total_commits': 1, 'files': [{'filename': 'Migrations/002/Apply.sql'}]}).encode()
        raise AssertionError(url)


class SourceTests(unittest.TestCase):
    def test_each_repository_of_a_product_is_its_own_source_named_after_the_product(self):
        with patch.dict(os.environ, NO_RIG), patch.object(catalog, 'PRODUCTS', (product(),)):
            sources = artifacts.sources()
        self.assertEqual([(APP, 'app'), (STUDIO, 'studio')], [(item.repository, item.component) for item in sources])
        self.assertEqual({'pilot'}, {item.product for item in sources})
        with patch.dict(os.environ, NO_RIG), patch.object(catalog, 'PRODUCTS', (product(components=False),)):
            self.assertEqual([None], [item.component for item in artifacts.sources()])

    def test_a_repository_whose_descriptor_names_no_service_yet_holds_up_no_set(self):
        with patch.dict(os.environ, NO_RIG), patch.object(catalog, 'PRODUCTS', (product(studio_images=()),)):
            self.assertEqual([APP], [item.repository for item in artifacts.sources()])

    def test_a_service_two_repositories_declare_or_a_repeated_repository_is_refused(self):
        clash = product(studio_images=(('api', 'ghcr.io/owner/pilot-studio/api'),))
        with patch.dict(os.environ, NO_RIG), patch.object(catalog, 'PRODUCTS', (clash,)):
            with self.assertRaisesRegex(ValueError, 'Service api is in both owner/pilot-app and owner/pilot-studio'):
                catalog.products()
        twice = catalog.Product('pilot', 'Pilot', '', APP, components=(catalog.Component(APP.upper()),))
        with patch.dict(os.environ, NO_RIG), patch.object(catalog, 'PRODUCTS', (twice,)):
            with self.assertRaisesRegex(ValueError, 'A product names each repository once'):
                catalog.products()

    def test_short_names_drop_the_product_prefix(self):
        self.assertEqual('studio', catalog.short_name('ocharo', 'ocharo-hq/ocharo-studio'))
        self.assertEqual('wheelhouse', catalog.short_name('wheelhouse', 'wow-two-platform/wow-two-platform.wheelhouse'))
        self.assertEqual('assets', catalog.short_name('ocharo', 'ocharo-hq/Ocharo.Assets'))
        self.assertEqual('r2d2', catalog.short_name('pilot', 'owner/2d2'))

    def test_the_snapshot_carries_a_products_further_repositories(self):
        written = inventory.product_json(product())
        self.assertEqual([STUDIO], [item['repository'] for item in written['components']])
        self.assertEqual(product(), inventory.product_of(json.loads(json.dumps(written))))
        single = inventory.product_json(product(components=False))
        self.assertNotIn('components', single)
        self.assertEqual((), inventory.product_of(single).components)


class ComposeTests(unittest.TestCase):
    def part(self, short, repository, manifest, compose_bytes):
        return short, repository, manifest, json.loads(compose_bytes)

    def parts(self, **studio):
        app = bundle('v0.1.2', digested([('api', 'ghcr.io/owner/pilot-app/api'), ('web', 'ghcr.io/owner/pilot-app/web')]),
                     'a' * 40, sites={'web': {'app': {'port': 8080, 'path': '/', 'exposure': 'public'}},
                                      'api': {'app': {'port': 8080, 'path': '/api/', 'exposure': 'public'}}},
                     volumes={'api': {'data': '/data'}}, migrations=False)
        other = bundle('v0.3.0', digested([('studio', 'ghcr.io/owner/pilot-studio/studio')]), 'b' * 40,
                       sites=studio.pop('sites', {'studio': {'studio': {'port': 8080, 'path': '/', 'exposure': 'public'}}}),
                       volumes={'studio': {studio.pop('volume', 'studio-data'): '/app/data'}}, **studio)
        return [self.part('app', APP, *app), self.part('studio', STUDIO, *other)]

    def test_a_set_keeps_every_name_its_repositories_give(self):
        manifest, compose_bytes = release_sets.compose('pilot', 'pilot-set-0123456789ab', self.parts())
        compose = json.loads(compose_bytes)
        self.assertEqual('app-v0.1.2_studio-v0.3.0', manifest['release'])
        self.assertEqual({'app', 'studio'}, set(manifest['sites']))
        self.assertTrue(manifest['sites']['app']['primary'])
        self.assertFalse(manifest['sites']['studio']['primary'])
        self.assertEqual({'studio': {'paths': ['/'], 'port': 8080}}, manifest['routes']['studio'])
        self.assertEqual({'app': {'paths': ['/api'], 'port': 8080}}, manifest['routes']['api'])
        self.assertEqual({'data', 'studio-data'}, set(compose['volumes']))
        self.assertEqual('studio-data', compose['services']['studio']['volumes'][0]['source'])
        self.assertEqual('data', compose['services']['api']['volumes'][0]['source'])
        # Each repository's services come over as its bundle wrote them, so a set restarts nothing a release did not.
        self.assertEqual(self.parts()[1][3]['services']['studio'], compose['services']['studio'])
        self.assertEqual(2, manifest['schemaVersion'])
        self.assertEqual('a' * 40, manifest['sourceCommit'])
        self.assertEqual(['app', 'studio'], [item['name'] for item in manifest['components']])
        self.assertEqual(['studio'], manifest['components'][1]['services'])
        self.assertEqual(hashlib.sha256(compose_bytes).hexdigest(), manifest['composeSha256'])
        # Rollback holds only when every repository's release allows it; migrations are unknown while one says nothing.
        self.assertTrue(manifest['rollbackCompatible'])
        self.assertNotIn('migrations', manifest)
        with tempfile.TemporaryDirectory() as folder:
            release_sets.write(folder, manifest, compose_bytes)
            sites = runner.release_sites(runner.validate_bundle(folder))
        self.assertEqual({('api', 'app', ('/api',)), ('web', 'app', ('/',)), ('studio', 'studio', ('/',))},
                         {(site['service'], site['site'], tuple(site['paths'])) for site in sites})

    def test_a_volume_two_repositories_name_is_refused_rather_than_renamed(self):
        with self.assertRaisesRegex(ValueError, 'Volume data is in both owner/pilot-app and owner/pilot-studio'):
            release_sets.compose('pilot', 'pilot-set-0123456789ab', self.parts(volume='data'))

    def test_a_site_two_repositories_name_is_refused_naming_both(self):
        parts = self.parts(sites={'studio': {'app': {'port': 8080, 'path': '/', 'exposure': 'private'}}})
        with self.assertRaisesRegex(ValueError, 'Site app is in both owner/pilot-app and owner/pilot-studio'):
            release_sets.compose('pilot', 'pilot-set-0123456789ab', parts)

    def test_the_primary_repositorys_front_door_is_the_products_and_another_primary_is_ordinary(self):
        schema2 = bundle('v0.3.0', digested([('studio', 'ghcr.io/owner/pilot-studio/studio')]), 'b' * 40, schema=2,
                         sites={'studio': {'exposure': 'private', 'primary': True, 'environments': ['dev', 'prod']}},
                         routes={'studio': {'studio': {'paths': ['/'], 'port': 8080}}})
        manifest, _ = release_sets.compose('pilot', 'pilot-set-0123456789ab',
                                           [self.parts()[0], self.part('studio', STUDIO, *schema2)])
        self.assertTrue(manifest['sites']['app']['primary'])
        self.assertEqual({'exposure': 'private', 'primary': False, 'environments': ['dev', 'prod']},
                         manifest['sites']['studio'])
        alone = release_sets.compose('pilot', 'pilot-set-0123456789ab', [self.part('studio', STUDIO, *schema2)])[0]
        self.assertTrue(alone['sites']['studio']['primary'])

    def test_a_repository_may_depend_on_another_repositorys_service_but_not_in_a_cycle(self):
        parts = self.parts()
        parts[1][3]['services']['studio']['depends_on'] = {'api': {'condition': 'service_healthy'}}
        _, compose_bytes = release_sets.compose('pilot', 'pilot-set-0123456789ab', parts)
        compose = json.loads(compose_bytes)
        self.assertEqual([['api', 'web'], ['studio']],
                         runner.dependency_waves(compose['services'], runner.service_dependencies(compose)))
        parts[0][3]['services']['api']['depends_on'] = {'studio': {'condition': 'service_healthy'}}
        with self.assertRaisesRegex(ValueError, 'cycle'):
            release_sets.compose('pilot', 'pilot-set-0123456789ab', parts)
        parts[0][3]['services']['api']['depends_on'] = {'ghost': {'condition': 'service_healthy'}}
        with self.assertRaisesRegex(ValueError, 'unknown service'):
            release_sets.compose('pilot', 'pilot-set-0123456789ab', parts)

    def test_a_set_refuses_a_shared_service_another_product_or_mixed_platforms(self):
        parts = self.parts()
        clash = bundle('v0.3.0', digested([('api', 'ghcr.io/owner/pilot-studio/api')]), 'b' * 40)
        with self.assertRaisesRegex(ValueError, 'Service api is in both owner/pilot-app and owner/pilot-studio'):
            release_sets.compose('pilot', 'pilot-set-0123456789ab', [parts[0], self.part('studio', STUDIO, *clash)])
        parts[1][2]['product'] = 'pilot-studio'
        with self.assertRaisesRegex(ValueError, 'owner/pilot-studio releases product pilot-studio, not pilot'):
            release_sets.compose('pilot', 'pilot-set-0123456789ab', parts)
        parts = self.parts()
        parts[1][2]['platform'] = 'linux/arm64'
        with self.assertRaisesRegex(ValueError, 'different platforms'):
            release_sets.compose('pilot', 'pilot-set-0123456789ab', parts)

    def test_dotnet_environments_stay_with_the_repositories_that_opted_in(self):
        parts = self.parts(dotnetEnvironment=True)
        manifest, compose_bytes = release_sets.compose('pilot', 'pilot-set-0123456789ab', parts)
        self.assertEqual(['studio'], manifest['dotnetEnvironment'])
        with tempfile.TemporaryDirectory() as folder:
            release_sets.write(folder, manifest, compose_bytes)
            runner.validate_bundle(folder)
            override = runner.read_json(runner.environment_override(folder, 'dev'))
        self.assertEqual('Development', override['services']['studio']['environment']['ASPNETCORE_ENVIRONMENT'])
        self.assertNotIn('ASPNETCORE_ENVIRONMENT', override['services']['api']['environment'])
        plan = runner.rollout_plan(json.loads(compose_bytes), 'dev', json.loads(compose_bytes), override, True,
                                   runner.dotnet_services(manifest, json.loads(compose_bytes)['services']))
        self.assertEqual({'unchanged'}, {service['change'] for service in plan['services']})

    def test_a_label_too_long_falls_back_to_the_sets_digest_and_parses_back(self):
        self.assertEqual('app-v1.0.0_studio-sha-abcdef1',
                         release_sets.label('pilot-set-0123456789ab', [('app', 'v1.0.0'), ('studio', 'sha-abcdef1')]))
        self.assertEqual({'app': 'v1.0.0', 'studio': 'sha-abcdef1'},
                         release_sets.label_parts('app-v1.0.0_studio-sha-abcdef1'))
        self.assertEqual({'brand-desk': 'v0.1.0-rc.1'}, release_sets.label_parts('brand-desk-v0.1.0-rc.1'))
        self.assertEqual('set-0123456789ab', release_sets.label('pilot-set-0123456789ab',
                                                                [('repository' + str(n), 'v10.20.30') for n in range(6)]))
        self.assertIsNone(release_sets.label_parts('set-0123456789ab'))
        self.assertIsNone(release_sets.label_parts('v1.0.0'))
        first = release_sets.identifier('pilot', ['pilot-gh-1', 'pilot-gh-2'])
        self.assertEqual(first, release_sets.identifier('pilot', ['pilot-gh-1', 'pilot-gh-2']))
        self.assertNotEqual(first, release_sets.identifier('pilot', ['pilot-gh-2', 'pilot-gh-1']))
        self.assertTrue(release_sets.is_set(first))
        with self.assertRaisesRegex(ValueError, '31 characters'):
            release_sets.identifier('p' * 40, ['x'])


class ReleaseSetTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for patcher in (patch.dict(os.environ, NO_RIG), patch.object(catalog, 'PRODUCTS', (product(),))):
            patcher.start()
            self.addCleanup(patcher.stop)
        self.github = GitHub()
        fetch = patch.object(artifacts, 'fetch', side_effect=self.github.fetch)
        fetch.start()
        self.addCleanup(fetch.stop)
        self.app_images = digested([('api', 'ghcr.io/owner/pilot-app/api'), ('web', 'ghcr.io/owner/pilot-app/web')])
        self.studio_images = digested([('studio', 'ghcr.io/owner/pilot-studio/studio')])
        app_sites = {'web': {'app': {'port': 8080, 'path': '/', 'exposure': 'public'}}}
        self.github.release(APP, 'v0.1.0', '2026-10-01T00:00:00Z', 11, *bundle('v0.1.0', self.app_images, '1' * 40,
                                                                                 sites=app_sites))
        self.github.release(STUDIO, 'v0.2.0', '2026-10-02T00:00:00Z', 21, *bundle('v0.2.0', self.studio_images,
                                                                                    '2' * 40))
        self.github.release(APP, 'v0.1.1', '2026-10-03T00:00:00Z', 12, *bundle('v0.1.1', self.app_images, '3' * 40,
                                                                                 sites=app_sites))

    def test_each_repository_release_makes_a_set_with_the_others_newest_release(self):
        listed = artifacts.available(self.root)
        # The first app release came before studio's, so its set holds app alone.
        self.assertEqual(['app-v0.1.1_studio-v0.2.0', 'app-v0.1.0_studio-v0.2.0', 'app-v0.1.0'],
                         [item['release'] for item in listed])
        newest = listed[0]
        self.assertEqual(release_sets.identifier('pilot', ['pilot-gh-12', 'pilot-gh-21']), newest['id'])
        self.assertEqual([], newest['notYet'])
        self.assertEqual(('release', 'ReleaseSet', '2026-10-03T00:00:00Z', False),
                         (newest['kind'], newest['provider'], newest['publishedAt'], newest['prerelease']))
        self.assertEqual([('app', APP, 'v0.1.1'), ('studio', STUDIO, 'v0.2.0')],
                         [(part['name'], part['repository'], part['release']) for part in newest['components']])
        # A repository's own releases never list alone: deploying one would drop the other repository's services.
        self.assertFalse(any(item['id'].startswith('pilot-gh-') for item in listed))
        self.assertTrue((self.root / 'sets' / (newest['id'] + '.json')).is_file())
        self.assertEqual([{'product': 'pilot', 'ok': True, 'release': {key: newest[key] for key in
                                                                        ('id', 'release', 'publishedAt')}}],
                         artifacts.latest())

    def test_a_repository_with_no_release_yet_is_left_out_until_it_releases(self):
        self.github.releases[STUDIO] = []
        listed = artifacts.available(self.root)
        self.assertEqual(['app-v0.1.1', 'app-v0.1.0'], [item['release'] for item in listed])
        newest = listed[0]
        # The set, its ID and its label cover the released repositories only; the rest are named as not in it yet.
        self.assertEqual(release_sets.identifier('pilot', ['pilot-gh-12']), newest['id'])
        self.assertEqual(['app'], [part['name'] for part in newest['components']])
        self.assertEqual([{'name': 'studio', 'repository': STUDIO}], newest['notYet'])
        self.assertEqual([{'product': 'pilot', 'ok': True, 'release': {key: newest[key] for key in
                                                                        ('id', 'release', 'publishedAt')}}],
                         artifacts.latest())
        manifest = runner.validate_bundle(artifacts.prepare(self.root, newest['id'], transport.import_bundle))
        self.assertEqual(('app-v0.1.1', newest['id'], {'api', 'web'}, True),
                         (manifest['release'], manifest['set'], set(manifest['images']), manifest['rollbackCompatible']))
        self.assertEqual(['app'], [part['name'] for part in history.bundle_facts(self.root, newest['id'])['components']])
        changes = artifacts.changelog('pilot', 'app-v0.1.0', 'app-v0.1.1', self.root)
        self.assertEqual((['app'], [{'name': 'studio', 'repository': STUDIO}]),
                         ([item['name'] for item in changes['components']], changes['notYet']))

        # Once studio releases, later sets take it and the watcher's newest set is the whole product.
        self.github.release(STUDIO, 'v0.2.0', '2026-10-04T00:00:00Z', 21, *bundle('v0.2.0', self.studio_images,
                                                                                    '2' * 40))
        listed = artifacts.available(self.root)
        self.assertEqual(['app-v0.1.1_studio-v0.2.0', 'app-v0.1.1', 'app-v0.1.0'], [item['release'] for item in listed])
        self.assertEqual((release_sets.identifier('pilot', ['pilot-gh-12', 'pilot-gh-21']), []),
                         (listed[0]['id'], listed[0]['notYet']))
        self.assertEqual(listed[0]['id'], artifacts.latest()[0]['release']['id'])
        joined = artifacts.changelog('pilot', 'app-v0.1.1', 'app-v0.1.1_studio-v0.2.0', self.root)
        self.assertEqual((['studio'], ['studio v0.2.0'], []),
                         ([item['name'] for item in joined['components']], [item['name'] for item in joined['releases']],
                          joined['notYet']))
        # The dev-proven set without studio still promotes as it was, from what was remembered.
        self.github.releases[APP] = self.github.releases[APP][:1]
        older = release_sets.identifier('pilot', ['pilot-gh-11'])
        self.assertNotIn(older, [item['id'] for item in artifacts.available(self.root)])
        self.assertEqual('app-v0.1.0', runner.validate_bundle(
            artifacts.prepare(self.root, older, transport.import_bundle))['release'])

    def test_a_commit_build_joins_the_released_repositories_as_a_candidate_set(self):
        self.github.releases[STUDIO] = []
        manifest, compose_bytes = bundle('sha-5555555', self.studio_images, '5' * 40, kind='candidate')
        self.github.build(STUDIO, 77, manifest, compose_bytes)
        manifest, compose_bytes = bundle('sha-6666666', self.app_images, '6' * 40, kind='candidate',
                                         sites={'web': {'app': {'port': 8080, 'path': '/', 'exposure': 'public'}}})
        self.github.build(APP, 78, manifest, compose_bytes)
        candidates = {item['release']: item for item in artifacts.available(self.root) if item['kind'] == 'candidate'}
        self.assertEqual({'app-v0.1.1_studio-sha-5555555', 'app-sha-6666666'}, set(candidates))
        self.assertEqual([], candidates['app-v0.1.1_studio-sha-5555555']['notYet'])
        self.assertEqual(['studio'], [item['name'] for item in candidates['app-sha-6666666']['notYet']])

    def test_no_set_exists_before_the_primary_repository_releases(self):
        self.github.releases[APP] = []
        manifest, compose_bytes = bundle('sha-5555555', self.studio_images, '5' * 40, kind='candidate')
        self.github.build(STUDIO, 77, manifest, compose_bytes)
        self.assertEqual([], artifacts.available(self.root))
        self.assertEqual([{'product': 'pilot', 'ok': True, 'release': None}], artifacts.latest())
        group = artifacts.product_sources('pilot')
        with self.assertRaisesRegex(ValueError, "owner/pilot-app serves the product's front door and has no release yet"):
            artifacts.set_item(group, [None, artifacts.published(group[1])[0]])
        # A remembered set must hold the primary repository's release, in repository order.
        studio_only = {'id': release_sets.identifier('pilot', ['pilot-gh-21']), 'product': 'pilot', 'release': 'studio-v0.2.0',
                       'components': [{'name': 'studio', 'repository': STUDIO, 'id': 'pilot-gh-21'}]}
        release_sets.remember(self.root, studio_only)
        with self.assertRaisesRegex(ValueError, 'repositories changed'):
            artifacts.prepare(self.root, studio_only['id'], transport.import_bundle)

    def test_a_repository_without_services_yet_is_named_as_not_in_the_set(self):
        marketing = catalog.Component('owner/pilot-marketing', catalog.Release('pilot-release.tar.gz', ()))
        three = catalog.Product('pilot', 'Pilot', '', APP, release=product().release,
                                components=product().components + (marketing,))
        with patch.object(catalog, 'PRODUCTS', (three,)):
            newest = artifacts.available(self.root)[0]
        self.assertEqual(('app-v0.1.1_studio-v0.2.0', release_sets.identifier('pilot', ['pilot-gh-12', 'pilot-gh-21'])),
                         (newest['release'], newest['id']))
        self.assertEqual([{'name': 'marketing', 'repository': 'owner/pilot-marketing'}], newest['notYet'])

    def test_a_repository_release_that_adds_a_service_composes_before_and_after_the_catalog_approves_it(self):
        older = artifacts.available(self.root)[0]
        jobs = digested([('api', 'ghcr.io/owner/pilot-app/api'), ('web', 'ghcr.io/owner/pilot-app/web'),
                         ('jobs', 'ghcr.io/owner/pilot-app/jobs')])
        self.github.release(APP, 'v0.1.2', '2026-10-05T00:00:00Z', 13, *bundle(
            'v0.1.2', jobs, '7' * 40, sites={'web': {'app': {'port': 8080, 'path': '/', 'exposure': 'public'}}}))
        newest = artifacts.available(self.root)[0]
        self.assertEqual('app-v0.1.2_studio-v0.2.0', newest['release'])
        manifest = runner.validate_bundle(artifacts.prepare(self.root, newest['id'], transport.import_bundle))
        self.assertEqual({'api', 'web', 'jobs', 'studio'}, set(manifest['images']))
        self.assertEqual(['api', 'jobs', 'web'], manifest['components'][0]['services'])
        # Once the control plane has read the new deploy.yml, the older set, without the service, still deploys.
        approved = catalog.Release('pilot-release.tar.gz', product().release.images
                                   + (('jobs', 'ghcr.io/owner/pilot-app/jobs'),), workflow='publish-docker-image.yml')
        grown = catalog.Product('pilot', 'Pilot', '', APP, release=approved, components=product().components)
        with patch.object(catalog, 'PRODUCTS', (grown,)):
            self.assertEqual('app-v0.1.1_studio-v0.2.0', runner.validate_bundle(
                artifacts.prepare(self.root, older['id'], transport.import_bundle))['release'])
            # A repository may not take another's service, whatever namespace its image is in.
            clash = digested([('studio', 'ghcr.io/owner/pilot-studio/studio'), ('jobs', 'ghcr.io/owner/pilot-studio/jobs')])
            self.github.release(STUDIO, 'v0.2.1', '2026-10-06T00:00:00Z', 22, *bundle('v0.2.1', clash, '8' * 40))
            with self.assertRaisesRegex(ValueError, 'Service jobs is in both owner/pilot-app and owner/pilot-studio'):
                artifacts.prepare(self.root, artifacts.available(self.root)[0]['id'], transport.import_bundle)

    def test_a_repository_that_gains_its_first_deploy_yml_joins_the_sets_once_its_services_are_approved(self):
        marketing = 'owner/pilot-marketing'
        self.github.releases[marketing] = []
        site = {'marketing': {'marketing': {'port': 8080, 'path': '/', 'exposure': 'public'}}}
        images = digested([('marketing', 'ghcr.io/owner/pilot-marketing/marketing'),
                           ('blog', 'ghcr.io/owner/pilot-marketing/blog')])
        self.github.release(marketing, 'v0.1.0', '2026-10-05T00:00:00Z', 31, *bundle('v0.1.0', images, '9' * 40,
                                                                                       sites=site))

        def product_with(marketing_images):
            return catalog.Product('pilot', 'Pilot', '', APP, release=product().release,
                                   components=product().components + (catalog.Component(marketing, catalog.Release(
                                       'pilot-release.tar.gz', marketing_images, workflow='publish-docker-image.yml')),))
        # Imported before it had a deploy.yml: no services approved, so its release waits outside the sets.
        with patch.object(catalog, 'PRODUCTS', (product_with(()),)):
            before = artifacts.available(self.root)[0]
        self.assertEqual(('app-v0.1.1_studio-v0.2.0', ['marketing']),
                         (before['release'], [item['name'] for item in before['notYet']]))
        # The control plane read its deploy.yml: the newest set holds every repository and deploys.
        approved = (('marketing', 'ghcr.io/owner/pilot-marketing/marketing'), ('blog', 'ghcr.io/owner/pilot-marketing/blog'))
        with patch.object(catalog, 'PRODUCTS', (product_with(approved),)):
            listed = artifacts.available(self.root)
            newest = listed[0]
            self.assertEqual(('app-v0.1.1_studio-v0.2.0_marketing-v0.1.0', []), (newest['release'], newest['notYet']))
            self.assertEqual(newest['id'], artifacts.latest()[0]['release']['id'])
            manifest = runner.validate_bundle(artifacts.prepare(self.root, newest['id'], transport.import_bundle))
            self.assertEqual({'api', 'web', 'studio', 'marketing', 'blog'}, set(manifest['images']))
            # The set dev ran before marketing joined still promotes.
            self.assertEqual('app-v0.1.1_studio-v0.2.0', runner.validate_bundle(
                artifacts.prepare(self.root, before['id'], transport.import_bundle))['release'])

    def test_a_set_composes_from_each_repositorys_checked_release_and_deploys_as_one_bundle(self):
        newest = artifacts.available(self.root)[0]
        bundle_path = transport.artifacts.prepare(self.root, newest['id'], transport.import_bundle)
        manifest = runner.validate_bundle(bundle_path)
        self.assertEqual(('pilot', 'app-v0.1.1_studio-v0.2.0', newest['id']),
                         (manifest['product'], manifest['release'], manifest['set']))
        self.assertEqual({'api', 'web', 'studio'}, set(manifest['images']))
        self.assertEqual(newest, json.loads((bundle_path / 'source.json').read_text()))
        for component in ('pilot-gh-12', 'pilot-gh-21'):
            self.assertTrue((self.root / 'bundles' / component / 'source.json').is_file())
        facts = history.bundle_facts(self.root, newest['id'])
        self.assertEqual(['app', 'studio'], [part['name'] for part in facts['components']])
        self.assertEqual('2026-10-03T00:00:00Z', facts['publishedAt'])
        # A second read is the cached set; a set that left the listing still composes from what was remembered.
        self.assertEqual(bundle_path, artifacts.prepare(self.root, newest['id'], transport.import_bundle))
        older = artifacts.available(self.root)[1]
        self.github.releases[APP] = self.github.releases[APP][:1]
        self.assertNotIn(older['id'], [item['id'] for item in artifacts.available(self.root)])
        self.assertEqual('app-v0.1.0_studio-v0.2.0',
                         runner.validate_bundle(artifacts.prepare(self.root, older['id'], transport.import_bundle))['release'])

    def test_a_repository_whose_descriptor_still_names_its_old_product_is_named(self):
        manifest, compose_bytes = bundle('v0.2.1', self.studio_images, '4' * 40)
        manifest['product'] = 'pilot-studio'
        self.github.release(STUDIO, 'v0.2.1', '2026-10-04T00:00:00Z', 22, manifest, compose_bytes)
        newest = artifacts.available(self.root)[0]
        with self.assertRaisesRegex(ValueError, "owner/pilot-studio's release names product pilot-studio"):
            artifacts.prepare(self.root, newest['id'], transport.import_bundle)

    def test_a_set_of_a_changed_product_or_a_bare_repository_release_is_refused(self):
        with self.assertRaisesRegex(ValueError, 'no longer available'):
            artifacts.prepare(self.root, 'pilot-gh-12', transport.import_bundle)
        newest = artifacts.available(self.root)[0]
        with patch.object(catalog, 'PRODUCTS', (product(components=False),)):
            with self.assertRaisesRegex(ValueError, "repositories changed"):
                artifacts.prepare(self.root, newest['id'], transport.import_bundle)
        with self.assertRaisesRegex(ValueError, 'no longer available'):
            artifacts.prepare(self.root, 'pilot-set-000000000000', transport.import_bundle)

    def test_a_commit_build_of_one_repository_is_a_candidate_set_beside_the_others_releases(self):
        manifest, compose_bytes = bundle('sha-5555555', self.studio_images, '5' * 40, kind='candidate')
        self.github.build(STUDIO, 77, manifest, compose_bytes)
        listed = artifacts.available(self.root)
        candidate = next(item for item in listed if item['kind'] == 'candidate')
        self.assertEqual('app-v0.1.1_studio-sha-5555555', candidate['release'])
        self.assertEqual(('5' * 40, 'main', True), (candidate['commit'], candidate['branch'], candidate['prerelease']))
        prepared = runner.validate_bundle(artifacts.prepare(self.root, candidate['id'], transport.import_bundle))
        self.assertEqual(('candidate', 'main'), (prepared['kind'], prepared['branch']))
        commits = json.dumps([{'sha': '5' * 40, 'commit': {'message': 'feat: x', 'author': {'name': 'Max'}}}]).encode()
        original = self.github.fetch
        with patch.object(artifacts, 'fetch', side_effect=lambda url, *a, **k: commits if '/commits?' in url
                          else original(url, *a, **k)):
            listing = artifacts.commits('pilot', 'main', STUDIO)
            self.assertEqual(candidate['id'], listing[0]['buildId'])
            self.assertIn('/pilot-studio/commit/', listing[0]['url'])
            with self.assertRaisesRegex(ValueError, "not one of the product's"):
                artifacts.commits('pilot', 'main', 'owner/elsewhere')

    def test_a_build_is_requested_in_the_named_repository(self):
        posted = []
        with patch.object(artifacts, 'post', side_effect=lambda url, body: posted.append(url)), \
                patch.object(artifacts, 'fetch', side_effect=lambda url, *a, **k: json.dumps(
                    {'sha': '9' * 40} if '/commits/' in url else {'artifacts': []}).encode()):
            artifacts.request_build('pilot', '9' * 40, STUDIO)
            artifacts.request_build('pilot', '9' * 40)
        self.assertIn('/owner/pilot-studio/actions/workflows/', posted[0])
        self.assertIn('/owner/pilot-app/actions/workflows/', posted[1])

    def test_a_sets_changelog_compares_each_repository_that_changed(self):
        self.github.release(STUDIO, 'v0.2.1', '2026-10-04T00:00:00Z', 22, *bundle('v0.2.1', self.studio_images, '4' * 40))
        result = artifacts.changelog('pilot', 'app-v0.1.0_studio-v0.2.0', 'app-v0.1.1_studio-v0.2.1', self.root)
        self.assertEqual(['app', 'studio'], [item['name'] for item in result['components']])
        self.assertEqual(['studio v0.2.1', 'app v0.1.1'], [item['name'] for item in result['releases']])
        self.assertEqual({APP, STUDIO}, {item['repository'] for item in result['releases']})
        self.assertEqual(['app: feat: owner/pilot-app', 'studio: feat: owner/pilot-studio'],
                         [commit['message'] for commit in result['commits']])
        self.assertEqual(2, result['totalCommits'])
        self.assertEqual(['app/Migrations/002/Apply.sql', 'studio/Migrations/002/Apply.sql'], result['migrations'])
        unchanged = artifacts.changelog('pilot', 'app-v0.1.1_studio-v0.2.0', 'app-v0.1.1_studio-v0.2.1', self.root)
        self.assertEqual(['studio'], [item['name'] for item in unchanged['components']])
        with self.assertRaisesRegex(ValueError, 'two release sets'):
            artifacts.changelog('pilot', None, 'v0.1.1', self.root)

    def test_a_failing_build_names_its_repository(self):
        runs = {APP: [], STUDIO: [{'id': 9, 'status': 'completed', 'conclusion': 'failure', 'head_sha': '6' * 40,
                                   'display_title': 'feat: break', 'created_at': '2026-10-05T00:00:00Z',
                                   'updated_at': '2026-10-05T00:01:00Z'}]}

        def fetch(url, *args, **kwargs):
            repository = APP if '/pilot-app/' in url else STUDIO
            return json.dumps({'workflow_runs': runs[repository]}).encode()
        with patch.object(artifacts, 'fetch', side_effect=fetch):
            result = artifacts.builds(self.root)
        self.assertEqual(1, len(result))
        self.assertEqual(('pilot', 'studio: feat: break', STUDIO), (result[0]['product'], result[0]['failing']['title'],
                                                                     result[0]['failing']['repository']))
        self.assertIn('/owner/pilot-studio/actions/runs/9', result[0]['failing']['url'])


class LocalSetTests(unittest.TestCase):
    def test_the_local_server_composes_imported_bundles_into_a_set(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            first = bundle('v0.1.0', digested([('api', 'localhost:15000/pilot/api')]), 'a' * 40,
                           sites={'api': {'app': {'port': 8080, 'path': '/', 'exposure': 'public'}}})
            second = bundle('v0.2.0', digested([('studio', 'localhost:15000/pilot/studio')]), 'b' * 40,
                            sites={'studio': {'studio': {'port': 8080, 'path': '/', 'exposure': 'public'}}})
            for name, (manifest, compose_bytes) in (('pilot-a', first), ('pilot-b', second)):
                path = Path(folder) / (name + '.tar.gz')
                path.write_bytes(archive(manifest, compose_bytes))
                transport.import_bundle(root, path, name)
            with patch.dict(os.environ, NO_RIG):
                with self.assertRaisesRegex(ValueError, 'only on the local server'):
                    transport.compose_set(root, 'pilot', ['app=pilot-a', 'studio=pilot-b'])
            with patch.dict(os.environ, {'WHEELHOUSE_REHEARSAL': '1'}), patch.object(catalog, 'PRODUCTS', ()):
                made = transport.compose_set(root, 'pilot', ['app=pilot-a', 'studio=pilot-b'])
                self.assertEqual('app-v0.1.0_studio-v0.2.0', made['release'])
                self.assertEqual({'app', 'studio'}, set(made['sites']))
                self.assertIn(made['id'], [item['id'] for item in artifacts.imported(root)])
                self.assertEqual(root / 'bundles' / made['id'],
                                 artifacts.prepare(root, made['id'], transport.import_bundle))
                self.assertEqual([], made['notYet'])
            # A product of three repositories whose third has not released: the set is the same one, named so.
            marketing = catalog.Component('owner/pilot-marketing', catalog.Release(
                'pilot-release.tar.gz', (('marketing', 'ghcr.io/owner/pilot-marketing/marketing'),)))
            three = catalog.Product('pilot', 'Pilot', '', APP, release=product().release,
                                    components=product().components + (marketing,))
            with patch.dict(os.environ, {'WHEELHOUSE_REHEARSAL': '1'}), patch.object(catalog, 'PRODUCTS', (three,)):
                partial = transport.compose_set(root, 'pilot', ['app=pilot-a', 'studio=pilot-b'])
                self.assertEqual((made['id'], made['release']), (partial['id'], partial['release']))
                self.assertEqual([STUDIO], [item['repository'] for item in partial['components'][1:]])
                self.assertEqual([{'name': 'marketing', 'repository': 'owner/pilot-marketing'}], partial['notYet'])
                alone = transport.compose_set(root, 'pilot', ['app=pilot-a'])
                self.assertEqual(('app-v0.1.0', ['studio', 'marketing']),
                                 (alone['release'], [item['name'] for item in alone['notYet']]))
                with self.assertRaisesRegex(ValueError, "owner/pilot-app serves the product's front door"):
                    transport.compose_set(root, 'pilot', ['studio=pilot-b'])
                with self.assertRaisesRegex(ValueError, "repository order: app, studio, marketing"):
                    transport.compose_set(root, 'pilot', ['app=pilot-a', 'marketing=pilot-b', 'studio=pilot-b'])
                with self.assertRaisesRegex(ValueError, "go is not one of the product's repositories"):
                    transport.compose_set(root, 'pilot', ['app=pilot-a', 'go=pilot-b'])


if __name__ == '__main__':
    unittest.main()
