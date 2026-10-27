from dataclasses import replace
import hashlib
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
import zipfile
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.request import Request
import artifacts
import runner
import transport
import catalog

# ForeverPin's release source as the local server's fixture product defines it.
FOREVERPIN = next(artifacts.Source(item.slug, item.repository, item.release.asset, item.release.images,
                                   workflow=item.release.workflow)
                  for item in catalog.LOCAL_PRODUCTS if item.slug == 'foreverpin')


class ArtifactTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        # Published releases only; CandidateTests covers the per-commit builds a workflow adds.
        self.source = replace(FOREVERPIN, workflow=None)
        self.sources = patch.object(artifacts, 'sources', lambda: (self.source,))
        self.sources.start()
        products = patch.object(catalog, 'PRODUCTS', catalog.LOCAL_PRODUCTS)
        products.start()
        self.addCleanup(products.stop)
        images = {service: image + '@sha256:' + 'a' * 64 for service, image in self.source.images}
        compose = {'services': {name: {'image': image, 'platform': 'linux/amd64',
                    'healthcheck': {'test': ['CMD', 'true']}} for name, image in images.items()}}
        self.compose_bytes = json.dumps(compose).encode()
        self.manifest = {'schemaVersion': 1, 'product': 'foreverpin', 'release': 'v0.9.0',
                         'sourceCommit': 'b' * 40, 'platform': 'linux/amd64', 'rollbackCompatible': False,
                         'composeSha256': hashlib.sha256(self.compose_bytes).hexdigest(),
                         'images': images, 'requiredConfiguration': {name: [] for name in images}}
        self.release = {'tag_name': 'v0.9.0', 'draft': False, 'published_at': '2026-09-19T00:00:00Z',
                        'assets': [{'name': self.source.asset_name, 'id': 123, 'state': 'uploaded',
                                    'digest': 'sha256:' + 'a' * 64, 'size': 500}]}
        self.pack()

    def tearDown(self):
        self.sources.stop()
        self.temp.cleanup()

    def pack(self):
        data = io.BytesIO()
        with tarfile.open(fileobj=data, mode='w:gz') as package:
            for name, payload in [('release.json', json.dumps(self.manifest).encode()), ('compose.json', self.compose_bytes)]:
                member = tarfile.TarInfo(name)
                member.size = len(payload)
                package.addfile(member, io.BytesIO(payload))
        self.archive = data.getvalue()
        self.release['assets'][0]['digest'] = 'sha256:' + hashlib.sha256(self.archive).hexdigest()

    def fetch(self, url):
        if '/releases?' in url:
            return json.dumps([self.release]).encode()
        if '/commits/' in url:
            return json.dumps({'sha': 'b' * 40}).encode()
        return self.archive

    def test_catalog_hides_drafts_and_incomplete_uploads(self):
        for change in ({'draft': True}, {'assets': []}, {'published_at': None}, {'tag_name': 'main'}):
            with self.subTest(change=change):
                release = {**self.release, **change}
                with patch.object(artifacts, 'fetch', return_value=json.dumps([release]).encode()):
                    self.assertEqual([], artifacts.available())
        self.release['assets'][0]['state'] = 'new'
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            self.assertEqual([], artifacts.available())

    def test_publication_is_listed_without_build_status_queries(self):
        with patch.object(artifacts, 'fetch', side_effect=self.fetch) as fetch:
            result = artifacts.available()
            self.assertEqual('foreverpin-gh-123', result[0]['id'])
            self.assertEqual(1, fetch.call_count)
            self.assertNotIn('actions', fetch.call_args.args[0])

    def test_the_watcher_reads_each_sources_newest_published_release_only(self):
        newer = {**self.release, 'tag_name': 'v0.10.0', 'published_at': '2026-09-20T00:00:00Z',
                 'assets': [{**self.release['assets'][0], 'id': 124}]}
        pre = {**self.release, 'tag_name': 'v0.11.0-rc.1', 'published_at': '2026-09-21T00:00:00Z', 'prerelease': True,
               'assets': [{**self.release['assets'][0], 'id': 125}]}
        with patch.object(artifacts, 'fetch', return_value=json.dumps([pre, self.release, newer]).encode()) as fetch:
            result = artifacts.latest()
        self.assertEqual([{'product': 'foreverpin', 'ok': True,
                           'release': {'id': 'foreverpin-gh-124', 'release': 'v0.10.0',
                                       'publishedAt': '2026-09-20T00:00:00Z'}}], result)
        self.assertIn('per_page=10', fetch.call_args.args[0])

    def test_the_watcher_names_a_source_github_cannot_serve(self):
        with patch.object(artifacts, 'fetch', side_effect=runner.CommandFailed('GitHub request failed (HTTP 403)')):
            self.assertEqual([{'product': 'foreverpin', 'ok': False, 'reason': 'GitHub request failed (HTTP 403)'}],
                             artifacts.latest())
        with patch.object(artifacts, 'fetch', return_value=b'[]'):
            self.assertEqual([{'product': 'foreverpin', 'ok': True, 'release': None}], artifacts.latest())

    def changelog_fetch(self, compare=None):
        def release(tag, published, body):
            return {**self.release, 'tag_name': tag, 'published_at': published, 'body': body, 'name': tag}
        listing = [release('v0.9.0', '2026-09-19T00:00:00Z', 'first'), release('v0.9.1', '2026-09-20T00:00:00Z', 'fixed login'),
                   release('v0.9.2', '2026-09-21T00:00:00Z', 'x' * 20001), {**release('v0.9.3', '2026-09-22T00:00:00Z', ''), 'draft': True}]

        def fetch(url, limit=artifacts.MAX_ARCHIVE):
            if '/releases?' in url:
                return json.dumps(listing).encode()
            if '/compare/' in url:
                if compare is None:
                    raise runner.CommandFailed('GitHub request failed (HTTP 404)')
                return json.dumps(compare).encode()
            raise AssertionError(url)
        return fetch

    def test_a_changelog_lists_the_release_bodies_since_what_prod_runs_newest_first_with_the_commits(self):
        commit = {'sha': 'c' * 40, 'commit': {'message': 'feat: billing\n\nbody', 'author': {'name': 'Max', 'date': '2026-09-21T00:00:00Z'}}}
        compare = {'total_commits': 2, 'commits': [{'sha': 'd' * 40, 'commit': {'message': 'fix: login'}}, commit],
                   'files': [{'filename': 'engineering/codebase/x/Persistence/Migrations/014-billing/Apply.sql'}, {'filename': 'README.md'}]}
        with patch.object(artifacts, 'fetch', side_effect=self.changelog_fetch(compare)):
            result = artifacts.changelog('foreverpin', 'v0.9.0', 'v0.9.2')
        self.assertEqual(['v0.9.2', 'v0.9.1'], [item['release'] for item in result['releases']])
        self.assertTrue(result['releases'][0]['truncated'])
        self.assertEqual(20000, len(result['releases'][0]['body']))
        self.assertEqual('fixed login', result['releases'][1]['body'])
        self.assertEqual(['feat: billing', 'fix: login'], [item['message'] for item in result['commits']])
        self.assertEqual(('Max', 2), (result['commits'][0]['author'], result['totalCommits']))
        self.assertEqual(['engineering/codebase/x/Persistence/Migrations/014-billing/Apply.sql'], result['migrations'])
        self.assertTrue(result['compareUrl'].endswith('/compare/v0.9.0...v0.9.2'))

    def test_a_changelog_keeps_the_bodies_when_the_compare_fails_and_takes_recent_ones_for_a_first_promotion(self):
        with patch.object(artifacts, 'fetch', side_effect=self.changelog_fetch()):
            failed = artifacts.changelog('foreverpin', 'v0.9.0', 'v0.9.1')
            first = artifacts.changelog('foreverpin', None, 'v0.9.1')
        self.assertEqual((['v0.9.1'], None, 'GitHub request failed (HTTP 404)'),
                         ([item['release'] for item in failed['releases']], failed['commits'], failed['reason']))
        self.assertEqual(['v0.9.1', 'v0.9.0'], [item['release'] for item in first['releases']])
        self.assertIsNone(first['compareUrl'])

    def test_a_changelog_runs_between_published_release_tags_only(self):
        with patch.object(artifacts, 'fetch', side_effect=self.changelog_fetch()):
            for start, end in (('main', 'v0.9.1'), ('v0.9.0', 'v0.9.3'), ('v0.9.0', None), ('v0.9.0', 'v1.0.0;id')):
                with self.subTest(start=start, end=end), self.assertRaises(ValueError):
                    artifacts.changelog('foreverpin', start, end)

    def test_a_failed_main_build_shows_until_a_later_run_succeeds_and_reads_are_kept_two_minutes(self):
        self.source = replace(FOREVERPIN)
        runs = {'workflow_runs': [
            {'id': 7, 'status': 'completed', 'conclusion': 'success', 'created_at': '2026-10-18T08:00:00Z', 'head_sha': 'a' * 40},
            {'id': 9, 'status': 'completed', 'conclusion': 'failure', 'created_at': '2026-10-18T09:00:00Z',
             'updated_at': '2026-10-18T09:05:00Z', 'head_sha': 'b' * 40, 'display_title': 'feat: billing',
             'html_url': 'https://evil.example/run'},
            {'id': 10, 'status': 'in_progress', 'conclusion': None, 'created_at': '2026-10-18T10:00:00Z'}]}
        with patch.object(artifacts, 'fetch', return_value=json.dumps(runs).encode()) as fetch:
            [result] = artifacts.builds(self.root, clock=lambda: 1000.0)
            self.assertIn('/actions/workflows/publish-docker-image.yml/runs?', fetch.call_args.args[0])
            self.assertIn('branch=main', fetch.call_args.args[0])
            self.assertEqual({'runId': 9, 'conclusion': 'failure', 'commit': 'b' * 40, 'title': 'feat: billing',
                              'at': '2026-10-18T09:05:00Z',
                              'url': 'https://github.com/sulton-max/10x-venture-forever-pin/actions/runs/9'}, result['failing'])
            artifacts.builds(self.root, clock=lambda: 1100.0)
            self.assertEqual(1, fetch.call_count, 'a read is kept two minutes')
        runs['workflow_runs'].append({'id': 11, 'status': 'completed', 'conclusion': 'success', 'created_at': '2026-10-18T11:00:00Z'})
        with patch.object(artifacts, 'fetch', return_value=json.dumps(runs).encode()):
            self.assertIsNone(artifacts.builds(self.root, clock=lambda: 1200.0)[0]['failing'])

    def test_a_product_github_cannot_serve_says_so_and_one_without_a_workflow_is_left_out(self):
        self.source = replace(FOREVERPIN)
        with patch.object(artifacts, 'fetch', side_effect=runner.CommandFailed('GitHub request failed (HTTP 404)')):
            self.assertEqual([{'product': 'foreverpin', 'ok': False, 'failing': None, 'reason': 'GitHub request failed (HTTP 404)'}],
                             artifacts.builds(self.root, clock=lambda: 1.0))
        self.source = replace(FOREVERPIN, workflow=None)
        self.assertEqual([], artifacts.builds(self.root, clock=lambda: 500.0))

    def test_usage_sums_thirty_days_of_actions_minutes_and_counts_package_versions_hourly(self):
        runs = {'workflow_runs': [
            {'status': 'completed', 'name': 'publish', 'run_started_at': '2026-10-18T08:00:00Z', 'updated_at': '2026-10-18T08:06:00Z'},
            {'status': 'completed', 'name': 'ci', 'run_started_at': '2026-10-17T08:00:00Z', 'updated_at': '2026-10-17T08:03:00Z'},
            {'status': 'in_progress', 'name': 'publish', 'run_started_at': '2026-10-18T09:00:00Z', 'updated_at': '2026-10-18T09:01:00Z'}]}

        def fetch(url, limit=artifacts.MAX_ARCHIVE):
            if '/actions/runs?' in url:
                self.assertIn('created=%3E%3D2026-09-18', url)
                return json.dumps(runs).encode()
            if '/orgs/' in url:
                raise runner.CommandFailed('GitHub request failed (HTTP 404)')
            if '/users/sulton-max/packages/container/' in url:
                return json.dumps([{}, {}, {}]).encode()
            raise AssertionError(url)
        now = 1_792_310_400.0  # 2026-10-18T00:00:00Z
        with patch.object(artifacts, 'fetch', side_effect=fetch) as fetched:
            result = artifacts.usage(self.root, 'foreverpin', clock=lambda: now)
            calls = fetched.call_count
            self.assertEqual(result, artifacts.usage(self.root, 'foreverpin', clock=lambda: now + 600))
            self.assertEqual(calls, fetched.call_count, 'a read is kept an hour')
        self.assertEqual((3, 9.0, True), (result['actions']['runs'], result['actions']['minutes'], result['actions']['complete']))
        self.assertEqual([{'name': 'publish', 'minutes': 6.0}, {'name': 'ci', 'minutes': 3.0}], result['actions']['workflows'])
        self.assertEqual([{'service': 'management', 'package': '10x-venture-forever-pin/management', 'versions': 3},
                          {'service': 'redirect', 'package': '10x-venture-forever-pin/redirect', 'versions': 3}], result['packages'])
        self.assertIsNone(result['storageBytes'])
        with self.assertRaises(ValueError):
            artifacts.usage(self.root, '../x')

    def test_selected_bundle_is_checked_and_cached(self):
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            bundle = artifacts.prepare(self.root, 'foreverpin-gh-123', transport.import_bundle)
            self.assertTrue((bundle / 'source.json').is_file())
            self.assertEqual(bundle, artifacts.prepare(self.root, 'foreverpin-gh-123', transport.import_bundle))

    def test_changed_archive_never_enters_the_cache(self):
        self.release['assets'][0]['digest'] = 'sha256:' + '0' * 64
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            with self.assertRaisesRegex(ValueError, 'asset changed'):
                artifacts.prepare(self.root, 'foreverpin-gh-123', transport.import_bundle)
        self.assertFalse((self.root / 'bundles/foreverpin-gh-123').exists())

    def test_source_commit_must_match_the_tag(self):
        self.manifest['sourceCommit'] = 'c' * 40
        self.pack()
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            with self.assertRaisesRegex(ValueError, 'source commit mismatch'):
                artifacts.prepare(self.root, 'foreverpin-gh-123', transport.import_bundle)

    def test_removed_release_cannot_be_submitted_from_cache(self):
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            artifacts.prepare(self.root, 'foreverpin-gh-123', transport.import_bundle)
            self.release['draft'] = True
            with self.assertRaisesRegex(ValueError, 'no longer available'):
                artifacts.prepare(self.root, 'foreverpin-gh-123', transport.import_bundle)

    def test_wrong_service_registry_is_rejected(self):
        self.manifest['images']['management'] = 'ghcr.io/other/api@sha256:' + 'a' * 64
        compose = json.loads(self.compose_bytes)
        compose['services']['management']['image'] = self.manifest['images']['management']
        self.compose_bytes = json.dumps(compose).encode()
        self.manifest['composeSha256'] = hashlib.sha256(self.compose_bytes).hexdigest()
        self.pack()
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            with self.assertRaisesRegex(ValueError, 'approved repository'):
                artifacts.prepare(self.root, 'foreverpin-gh-123', transport.import_bundle)

    def services(self, images):
        """Repacks the release with these service images, each pinned to a digest."""
        self.manifest['images'] = {service: image + '@sha256:' + 'a' * 64 for service, image in images.items()}
        compose = {'services': {name: {'image': image, 'platform': 'linux/amd64', 'healthcheck': {'test': ['CMD', 'true']}}
                                for name, image in self.manifest['images'].items()}}
        self.compose_bytes = json.dumps(compose).encode()
        self.manifest['composeSha256'] = hashlib.sha256(self.compose_bytes).hexdigest()
        self.manifest['requiredConfiguration'] = {name: [] for name in images}
        self.pack()

    def test_a_service_the_release_adds_in_the_repositorys_own_namespace_validates(self):
        # deploy.yml gained `worker` after the catalog approved management and redirect.
        self.services({**dict(self.source.images), 'worker': 'ghcr.io/sulton-max/10x-venture-forever-pin/worker'})
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            bundle = artifacts.prepare(self.root, 'foreverpin-gh-123', transport.import_bundle)
        self.assertEqual({'management', 'redirect', 'worker'}, set(runner.validate_bundle(bundle)['images']))

    def test_a_release_without_an_approved_service_validates(self):
        # An older release, or one whose deploy.yml removed a service: it is compared with its own services.
        self.services({'management': dict(self.source.images)['management']})
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            bundle = artifacts.prepare(self.root, 'foreverpin-gh-123', transport.import_bundle)
        self.assertEqual({'management'}, set(runner.validate_bundle(bundle)['images']))

    def test_a_new_service_outside_the_repositorys_namespace_is_refused(self):
        for image in ('ghcr.io/other/10x-venture-forever-pin/worker', 'ghcr.io/sulton-max/another-repo/worker',
                      'ghcr.io/sulton-max/10x-venture-forever-pin/redirect', 'docker.io/sulton-max/worker'):
            self.services({**dict(self.source.images), 'worker': image})
            with patch.object(artifacts, 'fetch', side_effect=self.fetch):
                with self.assertRaisesRegex(ValueError, 'Image is outside the approved repository'):
                    artifacts.prepare(self.root, 'foreverpin-gh-123', transport.import_bundle)
            self.assertFalse((self.root / 'bundles/foreverpin-gh-123').exists())

    def test_an_image_the_catalog_approved_for_a_service_validates_and_only_for_that_service(self):
        self.source = replace(self.source, images=self.source.images + (('edge', 'ghcr.io/shared/edge-proxy'),))
        self.services({**dict(self.source.images)})
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            artifacts.prepare(self.root, 'foreverpin-gh-123', transport.import_bundle)
        self.source = replace(self.source, images=self.source.images[:2])
        self.release['assets'][0]['id'] = 124
        self.services({**dict(self.source.images), 'worker': 'ghcr.io/shared/edge-proxy'})
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            with self.assertRaisesRegex(ValueError, 'Image is outside the approved repository'):
                artifacts.prepare(self.root, 'foreverpin-gh-124', transport.import_bundle)

    def test_imported_bundles_deploy_only_in_rehearsal(self):
        archive = self.root / 'release.tar.gz'
        archive.write_bytes(self.archive)
        transport.import_bundle(self.root, archive, 'foreverpin-local')
        offline = artifacts.CommandFailed('GitHub request failed (network)')
        with patch.object(artifacts, 'fetch', side_effect=offline):
            with patch.dict('os.environ', {'WHEELHOUSE_REHEARSAL': '1'}):
                listed = artifacts.available(self.root)
                self.assertEqual([('foreverpin-local', 'LocalImport')], [(item['id'], item['provider']) for item in listed])
                self.assertEqual(self.root / 'bundles/foreverpin-local',
                                 artifacts.prepare(self.root, 'foreverpin-local', transport.import_bundle))
            with patch.dict('os.environ', {'WHEELHOUSE_REHEARSAL': '0'}):
                with self.assertRaises(artifacts.CommandFailed):
                    artifacts.available(self.root)

    def test_github_errors_report_only_the_status(self):
        error = HTTPError('https://api.github.com/repos', 403, 'rate limit exceeded', {}, io.BytesIO(b'PRIVATE'))
        with patch.object(artifacts, 'build_opener') as opener:
            opener.return_value.open.side_effect = error
            with self.assertRaisesRegex(artifacts.CommandFailed, r'^GitHub request failed \(HTTP 403\)$'):
                artifacts.fetch('https://api.github.com/repos/owner/repo/releases')

    def test_api_token_cannot_follow_a_cdn_redirect(self):
        request = Request('https://api.github.com/asset', headers={'Authorization': 'Bearer PRIVATE'})
        redirected = artifacts.ApiRedirect().redirect_request(request, None, 302, 'Found', {},
                                                               'https://release-assets.githubusercontent.com/asset')
        self.assertFalse(redirected.has_header('Authorization'))
        with self.assertRaisesRegex(ValueError, 'Insecure'):
            artifacts.ApiRedirect().redirect_request(request, None, 302, 'Found', {}, 'http://example.net/asset')


class CandidateTests(unittest.TestCase):
    """Per-commit builds published as `bundle-<sha>` Actions artifacts."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.commit = 'c' * 40
        base = FOREVERPIN
        self.source = artifacts.Source(base.product, base.repository, base.asset_name, base.images,
                                       workflow='publish-docker-image.yml')
        images = {service: image + '@sha256:' + 'a' * 64 for service, image in self.source.images}
        compose = {'services': {name: {'image': image, 'platform': 'linux/amd64',
                    'healthcheck': {'test': ['CMD', 'true']}} for name, image in images.items()}}
        self.compose_bytes = json.dumps(compose).encode()
        self.manifest = {'schemaVersion': 1, 'product': 'foreverpin', 'release': 'sha-ccccccc', 'kind': 'candidate',
                         'branch': 'main', 'sourceCommit': self.commit, 'platform': 'linux/amd64',
                         'rollbackCompatible': False, 'composeSha256': hashlib.sha256(self.compose_bytes).hexdigest(),
                         'images': images, 'requiredConfiguration': {}}
        self.artifacts = {'artifacts': [
            {'id': 7, 'name': 'bundle-' + self.commit, 'expired': False, 'size_in_bytes': 900,
             'created_at': '2026-09-27T10:00:00Z', 'expires_at': '2026-10-11T10:00:00Z',
             'workflow_run': {'head_branch': 'main'}},
            {'id': 6, 'name': 'bundle-' + self.commit, 'expired': False, 'size_in_bytes': 900},
            {'id': 5, 'name': 'bundle-' + 'd' * 40, 'expired': True, 'size_in_bytes': 900},
            {'id': 4, 'name': 'coverage', 'expired': False, 'size_in_bytes': 900}]}
        self.posts = []
        self.sources = patch.object(artifacts, 'sources', lambda: (self.source,))
        self.sources.start()
        products = patch.object(catalog, 'PRODUCTS', catalog.LOCAL_PRODUCTS)
        products.start()
        self.addCleanup(products.stop)

    def tearDown(self):
        self.sources.stop()
        self.temp.cleanup()

    def zipped(self, extra=None):
        data = io.BytesIO()
        with zipfile.ZipFile(data, 'w') as package:
            package.writestr('release.json', json.dumps(self.manifest))
            package.writestr('compose.json', self.compose_bytes)
            if extra:
                package.writestr(extra, b'x')
        return data.getvalue()

    def fetch(self, url):
        if '/releases?' in url:
            return b'[]'
        if url.endswith('/actions/artifacts?per_page=100'):
            return json.dumps(self.artifacts).encode()
        if url.endswith('/actions/artifacts/7/zip'):
            return self.zipped()
        if '/commits?' in url:
            return json.dumps([{'sha': self.commit, 'commit': {'message': 'feat: pins\n\nbody',
                                                              'author': {'name': 'Max', 'date': '2026-09-27T09:00:00Z'}}},
                               {'sha': 'e' * 40, 'commit': {'message': 'fix: other', 'author': {'name': 'Max'}}}]).encode()
        if '/commits/' in url:
            return json.dumps({'sha': url.rsplit('/', 1)[1]}).encode()
        raise AssertionError(url)

    def test_one_live_build_per_commit_is_listed_newest_first(self):
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            listed = artifacts.available()
        self.assertEqual([('foreverpin-ci-7', 'candidate', self.commit, 'main', 'sha-ccccccc')],
                         [(item['id'], item['kind'], item['commit'], item['branch'], item['release']) for item in listed])

    def test_a_candidate_is_imported_from_its_artifact_and_checked_against_its_commit(self):
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            bundle = artifacts.prepare(self.root, 'foreverpin-ci-7', transport.import_bundle)
        self.assertEqual('candidate', runner.read_json(bundle / 'release.json')['kind'])
        self.manifest['sourceCommit'] = 'f' * 40
        with patch.object(artifacts, 'fetch', side_effect=self.fetch), \
                self.assertRaisesRegex(ValueError, 'Candidate source commit mismatch'):
            artifacts.prepare(Path(self.temp.name) / 'other', 'foreverpin-ci-7', transport.import_bundle)

    def test_a_candidate_artifact_holds_only_the_bundle(self):
        with self.assertRaisesRegex(ValueError, 'only release.json and compose.json'):
            artifacts.candidate_archive(self.zipped(extra='notes.txt'))

    def test_a_build_is_requested_only_for_a_commit_without_one(self):
        with patch.object(artifacts, 'fetch', side_effect=self.fetch), \
                patch.object(artifacts, 'post', side_effect=lambda url, body: self.posts.append((url, body))):
            with self.assertRaisesRegex(ValueError, 'already exists'):
                artifacts.request_build('foreverpin', self.commit)
            result = artifacts.request_build('foreverpin', 'e' * 40)
        self.assertEqual('requested', result['status'])
        self.assertEqual([('https://api.github.com/repos/' + self.source.repository
                           + '/actions/workflows/publish-docker-image.yml/dispatches',
                           {'ref': 'main', 'inputs': {'commit': 'e' * 40}})], self.posts)

    def test_a_product_without_a_workflow_cannot_build(self):
        with patch.object(artifacts, 'sources', lambda: (artifacts.Source('foreverpin', 'o/r', 'a.tar.gz', ()),)):
            with self.assertRaisesRegex(ValueError, 'no build workflow'):
                artifacts.request_build('foreverpin', self.commit)

    def test_starting_a_build_needs_a_token(self):
        with patch.dict('os.environ', {}, clear=False):
            import os
            os.environ.pop('WHEELHOUSE_GITHUB_TOKEN_FILE', None)
            with self.assertRaisesRegex(ValueError, 'Actions write access'):
                artifacts.post('https://api.github.com/repos/o/r/actions/workflows/w.yml/dispatches', {})

    def test_commits_show_which_ones_are_built(self):
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            listed = artifacts.commits('foreverpin', 'main')
        self.assertEqual([(self.commit, 'feat: pins', 'foreverpin-ci-7'), ('e' * 40, 'fix: other', None)],
                         [(item['sha'], item['message'], item['buildId']) for item in listed])

    def test_commits_carry_a_permalink_and_whether_a_build_can_start(self):
        with patch.object(artifacts, 'fetch', side_effect=self.fetch):
            listed = artifacts.commits('foreverpin', 'main')
        source = artifacts.source_of('foreverpin')
        self.assertEqual('https://github.com/' + source.repository + '/commit/' + self.commit, listed[0]['url'])
        self.assertEqual(source.workflow is not None, listed[0]['canBuild'])

    def test_an_unreachable_build_listing_leaves_releases_usable(self):
        def flaky(url):
            if 'actions' in url:
                raise artifacts.CommandFailed('GitHub request failed (HTTP 502)')
            return self.fetch(url)
        with patch.object(artifacts, 'fetch', side_effect=flaky):
            self.assertEqual([], artifacts.available())


class GitHubAccessTests(unittest.TestCase):
    """Which credential each GitHub call carries, how a release archive is downloaded, and the per-owner access notes
    the console's Issues panel reads."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        directory = Path(self.temp.name)
        self.tokens = directory / 'tokens.json'
        self.fallback = directory / 'token'
        self.fallback.write_text('FALLBACK\n')
        self.access = directory / 'github' / 'access.json'
        soon = '2099-01-01T00:00:00Z'  # The API writes UTC with a Z.
        self.tokens.write_text(json.dumps({'owners': {
            'ocharo-hq': {'token': 'INSTALLED', 'expiresAt': soon},
            'stale': {'token': 'EXPIRED', 'expiresAt': '2000-01-01T00:00:00+00:00'}},
            'any': {'token': 'BORROWED', 'expiresAt': soon}}))
        environment = patch.dict('os.environ', {'WHEELHOUSE_GITHUB_TOKENS_FILE': str(self.tokens),
                                                'WHEELHOUSE_GITHUB_TOKEN_FILE': str(self.fallback),
                                                'WHEELHOUSE_GITHUB_ACCESS_FILE': str(self.access)})
        environment.start()
        self.addCleanup(environment.stop)
        self.requests = []
        self.failure = None

    def open(self, request, timeout=None):
        self.requests.append(request)
        if self.failure is not None:
            raise self.failure
        response = io.BytesIO(b'{}')
        response.url, response.status = request.full_url, 200
        return response

    def opener(self):
        opener = patch.object(artifacts, 'build_opener')
        built = opener.start()
        self.addCleanup(opener.stop)
        built.return_value.open.side_effect = self.open

    def test_each_owner_gets_its_installation_token_and_others_the_fallback(self):
        self.assertEqual('INSTALLED', artifacts.credential('https://api.github.com/repos/Ocharo-HQ/site/releases'))
        self.assertEqual('FALLBACK', artifacts.credential('https://api.github.com/repos/sulton-max/app/releases'))
        self.assertEqual('FALLBACK', artifacts.credential('https://api.github.com/repos/stale/app/releases'),
                         'an expired installation token is never sent')
        self.assertEqual('FALLBACK', artifacts.credential(
            'https://api.github.com/orgs/ocharo-hq/packages/container/site/versions'),
            'packages take the fallback token, which may read them')
        self.assertIsNone(artifacts.credential('https://github.com/ocharo-hq/site/releases/download/v1/a.tar.gz'))
        self.assertIsNone(artifacts.credential('http://api.github.com/repos/ocharo-hq/site'))

    def test_without_any_credential_the_call_is_anonymous(self):
        import os
        os.environ.pop('WHEELHOUSE_GITHUB_TOKENS_FILE')
        os.environ.pop('WHEELHOUSE_GITHUB_TOKEN_FILE')
        self.opener()
        artifacts.fetch('https://api.github.com/repos/ocharo-hq/site/releases?per_page=10')
        self.assertFalse(self.requests[0].has_header('Authorization'))

    def test_a_malformed_tokens_file_falls_back_instead_of_failing(self):
        self.tokens.write_text('{not json')
        self.assertEqual('FALLBACK', artifacts.credential('https://api.github.com/repos/ocharo-hq/site'))

    def test_an_authenticated_asset_comes_through_the_api_as_octet_stream(self):
        self.opener()
        artifacts.release_asset('ocharo-hq/site', 'v1.2.3', 'site-release.tar.gz', 77)
        request = self.requests[0]
        self.assertEqual('https://api.github.com/repos/ocharo-hq/site/releases/assets/77', request.full_url)
        self.assertEqual('application/octet-stream', request.get_header('Accept'))
        self.assertEqual('Bearer INSTALLED', request.get_header('Authorization'))

    def test_an_anonymous_asset_comes_from_the_public_download_url_without_a_credential(self):
        import os
        os.environ.pop('WHEELHOUSE_GITHUB_TOKENS_FILE')
        os.environ.pop('WHEELHOUSE_GITHUB_TOKEN_FILE')
        self.opener()
        artifacts.release_asset('ocharo-hq/site', 'v1.2.3', 'site-release.tar.gz', 77)
        request = self.requests[0]
        self.assertEqual('https://github.com/ocharo-hq/site/releases/download/v1.2.3/site-release.tar.gz',
                         request.full_url)
        self.assertFalse(request.has_header('Authorization'))

    def test_a_build_dispatch_carries_the_owners_installation_token(self):
        self.opener()
        artifacts.post('https://api.github.com/repos/ocharo-hq/site/actions/workflows/w.yml/dispatches', {'ref': 'main'})
        self.assertEqual('Bearer INSTALLED', self.requests[0].get_header('Authorization'))

    def test_a_refused_call_is_noted_per_owner_and_a_later_success_clears_it(self):
        self.opener()
        headers = {'X-RateLimit-Remaining': '0'}
        self.failure = HTTPError('https://api.github.com/repos', 403, 'rate limit', headers, io.BytesIO(b'PRIVATE'))
        with self.assertRaises(artifacts.CommandFailed):
            artifacts.fetch('https://api.github.com/repos/Ocharo-HQ/site/releases')
        note = json.loads(self.access.read_text())['owners']['ocharo-hq']
        self.assertEqual((403, True, True), (note['status'], note['authenticated'], note['rateLimited']))
        self.assertNotIn('PRIVATE', self.access.read_text())
        self.assertNotIn('INSTALLED', self.access.read_text())

        self.failure = HTTPError('https://api.github.com/repos', 404, 'missing', {}, io.BytesIO(b''))
        with self.assertRaises(artifacts.CommandFailed):
            artifacts.fetch('https://api.github.com/repos/ocharo-hq/site/releases')
        with self.assertRaises(artifacts.CommandFailed):
            artifacts.fetch('https://api.github.com/repos/sulton-max/app/contents/engineering/deployment/deploy.yml?ref=main')
        self.assertEqual({'ocharo-hq'}, set(json.loads(self.access.read_text())['owners']),
                         'a missing file, or a repository its own installation cannot find, needs no App')

        self.failure = None
        artifacts.fetch('https://api.github.com/repos/ocharo-hq/site/releases')
        self.assertEqual({}, json.loads(self.access.read_text())['owners'])

    def test_package_reads_are_never_noted(self):
        self.opener()
        self.failure = HTTPError('https://api.github.com/orgs', 403, 'forbidden', {}, io.BytesIO(b''))
        with self.assertRaises(artifacts.CommandFailed):
            artifacts.fetch('https://api.github.com/orgs/ocharo-hq/packages/container/site/versions')
        self.assertFalse(self.access.exists())

    def test_an_owner_without_the_app_reads_public_repositories_with_another_installations_token(self):
        import os
        os.environ.pop('WHEELHOUSE_GITHUB_TOKEN_FILE')
        self.assertEqual(('BORROWED', artifacts.Credential.BORROWED),
                         artifacts.access('https://api.github.com/repos/wow-two/wow-two-ws/releases'))
        self.assertEqual(('INSTALLED', artifacts.Credential.INSTALLATION),
                         artifacts.access('https://api.github.com/repos/ocharo-hq/site/releases'))
        self.assertEqual((None, None), artifacts.access('https://api.github.com/orgs/wow-two/packages/container/x/versions'),
                         'an installation token never reads packages')

    def test_a_borrowed_token_github_refuses_is_retried_anonymously(self):
        import os
        os.environ.pop('WHEELHOUSE_GITHUB_TOKEN_FILE')
        self.opener()
        refusals = [HTTPError('https://api.github.com/repos', 403, 'forbidden', {}, io.BytesIO(b''))]

        def open_once(request, timeout=None):
            self.requests.append(request)
            if refusals:
                raise refusals.pop()
            response = io.BytesIO(b'[]')
            response.url, response.status = request.full_url, 200
            return response
        artifacts.build_opener.return_value.open.side_effect = open_once

        self.assertEqual(b'[]', artifacts.fetch('https://api.github.com/repos/wow-two/wow-two-ws/releases'))
        self.assertEqual(['Bearer BORROWED', None], [request.get_header('Authorization') for request in self.requests])
        self.assertFalse(self.access.exists(), 'the anonymous retry succeeded')

    def test_a_build_dispatch_never_borrows_another_owners_installation(self):
        import os
        os.environ.pop('WHEELHOUSE_GITHUB_TOKEN_FILE')
        with self.assertRaisesRegex(ValueError, 'GitHub App installed'):
            artifacts.post('https://api.github.com/repos/wow-two/wow-two-ws/actions/workflows/w.yml/dispatches', {})

    def test_a_repository_unseen_without_its_owners_app_is_noted_until_it_is_read(self):
        self.opener()
        self.failure = HTTPError('https://api.github.com/repos', 404, 'missing', {}, io.BytesIO(b''))
        with self.assertRaises(artifacts.CommandFailed):
            artifacts.fetch('https://api.github.com/repos/Sulton-Max/Private-App/commits/main')
        note = json.loads(self.access.read_text())['owners']['sulton-max']
        self.assertEqual((404, 'sulton-max/private-app', False), (note['status'], note['repository'], note['installed']))

        self.failure = None
        artifacts.fetch('https://api.github.com/repos/sulton-max/public-app/releases')
        self.assertIn('sulton-max', json.loads(self.access.read_text())['owners'], 'another repository says nothing')
        artifacts.fetch('https://api.github.com/repos/sulton-max/private-app/git/trees/' + 'a' * 40)
        self.assertEqual({}, json.loads(self.access.read_text())['owners'])

    def test_without_the_app_an_unseen_repository_is_not_noted(self):
        import os
        os.environ.pop('WHEELHOUSE_GITHUB_TOKENS_FILE')
        self.opener()
        self.failure = HTTPError('https://api.github.com/repos', 404, 'missing', {}, io.BytesIO(b''))
        with self.assertRaises(artifacts.CommandFailed):
            artifacts.fetch('https://api.github.com/repos/sulton-max/private-app/releases')
        self.assertFalse(self.access.exists())
