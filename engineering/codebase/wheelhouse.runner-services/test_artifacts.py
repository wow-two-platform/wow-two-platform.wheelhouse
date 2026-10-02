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
