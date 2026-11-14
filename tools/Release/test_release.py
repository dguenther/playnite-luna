import copy
import json
import os
import io
import zipfile
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from release import METADATA, SafeRedirect, body_with_state, digest, identity, marker, prepare, publish
import urllib.request

SHA = 'a' * 40
NAME = 'Luna_1_2_3.pext'


class FakeGitHub:
    def __init__(self):
        self.current = None
        self.tag = None
        self.data = {}
        self.calls = []
        self.next_id = 1
        self.fail_upload = None
        self.run = dict(run_number=10, run_attempt=1, event='workflow_dispatch', path='.github/workflows/prepare-release.yml', head_sha=SHA, status='completed', conclusion='success')

    def source_identity(self, sha, version):
        if sha not in (SHA, 'b' * 40) or version != '1.2.3':
            raise ValueError('identity mismatch')
        return NAME

    def optional(self, path):
        return self.tag

    def release(self, tag):
        return copy.deepcopy(self.current) if self.current and self.current['tag_name'] == tag else None

    def request(self, method, path, data=None):
        self.calls.append((method, path, copy.deepcopy(data)))
        if path == 'releases/generate-notes':
            return dict(body='Generated initial notes')
        if path.startswith('actions/runs/'):
            return self.run
        if method == 'POST' and path == 'releases':
            self.current = dict(data, id=1, html_url='https://github.invalid/draft/1', assets=[], upload_url='https://uploads.invalid', prerelease=False)
        elif method == 'PATCH':
            # GitHub resets an untagged draft's intended tag when PATCH omits it.
            if self.current['draft'] and 'tag_name' not in data:
                self.current['tag_name'] = 'untagged-test'
            self.current.update(data)
            if data.get('draft') is False:
                self.tag = dict(object=dict(type='commit', sha=self.current['target_commitish']))
        elif method == 'DELETE':
            asset_id = int(path.rsplit('/', 1)[1])
            self.current['assets'] = [a for a in self.current['assets'] if a['id'] != asset_id]
        return copy.deepcopy(self.current)

    def upload(self, release, name, data):
        if name == self.fail_upload:
            raise RuntimeError('upload failed')
        asset = dict(id=self.next_id, name=name, size=len(data), state='uploaded')
        self.next_id += 1
        self.current['assets'].append(asset)
        self.data[asset['id']] = data
        return copy.deepcopy(asset)

    def download(self, asset):
        return self.data[asset['id']]


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        environment = patch.dict(os.environ, {'GITHUB_STEP_SUMMARY': ''})
        environment.start()
        self.addCleanup(environment.stop)
        self.api = FakeGitHub()
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, 'w') as archive:
            archive.writestr('extension.yaml', 'Id: Luna\nVersion: 1.2.3\n')
        Path(self.directory.name, NAME).write_bytes(buffer.getvalue())

    def prepare(self, run=10, attempt=1):
        return prepare(self.api, '1.2.3', SHA, run, attempt, self.directory.name)

    def test_publish_exact_bytes_and_idempotent_retry(self):
        candidate = self.prepare()
        self.assertIsNone(self.api.tag)
        before = copy.deepcopy(self.api.data)
        publish(self.api, '1.2.3', candidate)
        self.assertEqual(before, self.api.data)
        mutations = len(self.api.calls)
        self.assertIn('Already published', publish(self.api, '1.2.3', candidate))
        self.assertTrue(all(method == 'GET' for method, _, _ in self.api.calls[mutations:]))
        with self.assertRaisesRegex(ValueError, 'Tag already exists'):
            self.prepare(11)

    def test_draft_updates_and_publication_preserve_intended_tag(self):
        self.prepare()
        self.assertEqual('v1.2.3', self.api.current['tag_name'])
        candidate = self.prepare(11)
        self.api.run['run_number'] = 11
        publish(self.api, '1.2.3', candidate)
        patches = [data for method, _, data in self.api.calls if method == 'PATCH']
        self.assertTrue(patches)
        self.assertTrue(all(data.get('tag_name') == 'v1.2.3' for data in patches))
        self.assertEqual('v1.2.3', self.api.current['tag_name'])

    def test_failed_replacement_invalidates_previous_candidate(self):
        candidate = self.prepare()
        self.api.current['body'] = 'Hand-edited notes\n' + self.api.current['body']
        self.api.fail_upload = METADATA
        with self.assertRaises(RuntimeError):
            self.prepare(11)
        self.assertEqual('preparing', marker(self.api.current)['status'])
        with self.assertRaises(ValueError):
            publish(self.api, '1.2.3', candidate)
        with self.assertRaisesRegex(ValueError, 'Stale'):
            self.prepare(10)
        self.api.fail_upload = None
        replacement = self.prepare(12)
        self.assertIn('Hand-edited notes', self.api.current['body'])
        self.assertNotEqual(candidate, replacement)

    def test_rejects_incomplete_failed_and_rerun_checks(self):
        candidate = self.prepare()
        for change in ({'status': 'in_progress', 'conclusion': None}, {'conclusion': 'failure'}, {'run_attempt': 2}, {'path': '.github/workflows/check.yml'}, {'head_sha': 'b'*40}):
            with self.subTest(change=change), patch.dict(self.api.run, change):
                with self.assertRaises(ValueError):
                    publish(self.api, '1.2.3', candidate)
                self.assertTrue(self.api.current['draft'])

    def test_rejects_replaced_assets_and_wrong_candidate(self):
        candidate = self.prepare()
        for candidate_id in ('10.2.' + SHA, '11.1.' + SHA):
            with self.assertRaises(ValueError):
                publish(self.api, '1.2.3', candidate_id)
        package = next(a for a in self.api.current['assets'] if a['name'] == NAME)
        self.api.data[package['id']] = b'compromised bytes'
        with self.assertRaisesRegex(ValueError, 'hash mismatch'):
            publish(self.api, '1.2.3', candidate)

    def test_rejects_target_or_tag_changes(self):
        candidate = self.prepare()
        self.api.tag = dict(object=dict(type='commit', sha='b'*40))
        with self.assertRaisesRegex(ValueError, 'tag points'):
            publish(self.api, '1.2.3', candidate)
        self.api.tag = None
        self.api.current['target_commitish'] = 'main'
        with self.assertRaisesRegex(ValueError, 'target/type'):
            publish(self.api, '1.2.3', candidate)

    def test_retry_after_tag_created_but_release_still_draft(self):
        candidate = self.prepare()
        self.api.tag = dict(object=dict(type='commit', sha=SHA))
        publish(self.api, '1.2.3', candidate)
        self.assertFalse(self.api.current['draft'])

    def test_new_attempt_supersedes_old_attempt(self):
        self.prepare()
        candidate = self.prepare(attempt=2)
        with self.assertRaisesRegex(ValueError, 'Stale'):
            self.prepare(attempt=1)
        self.api.run['run_attempt'] = 2
        publish(self.api, '1.2.3', candidate)

    def test_identity_validation(self):
        manifest = 'Id: Luna\nVersion: 1.2.3\n'
        project = '<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>'
        self.assertEqual(NAME, identity(manifest, project, '1.2.3'))
        for version in ('v1.2.3', '01.2.3', '1.2.4', '1.2.3\n'):
            with self.assertRaises(ValueError):
                identity(manifest, project, version)
        with self.assertRaises(ValueError):
            identity(manifest, project.replace('1.2.3', '1.2.4'), '1.2.3')

    def test_summary_is_explicit_and_contains_test_identity(self):
        summary = Path(self.directory.name, 'summary.md')
        with patch.dict(os.environ, {'GITHUB_STEP_SUMMARY': str(summary)}):
            candidate = self.prepare()
        text = summary.read_text()
        for expected in (candidate, SHA, NAME, 'https://github.invalid/draft/1', 'SHA256', 'replaces'):
            self.assertIn(expected, text)

    def test_new_source_same_version_replaces_candidate(self):
        old = self.prepare()
        self.api.current['body'] = 'My release notes\n' + self.api.current['body']
        new_sha = 'b' * 40
        candidate = prepare(self.api, '1.2.3', new_sha, 11, 1, self.directory.name, 11)
        self.assertEqual(new_sha, self.api.current['target_commitish'])
        self.assertIn('My release notes', self.api.current['body'])
        self.assertNotEqual(candidate, old)
        with self.assertRaises(ValueError):
            publish(self.api, '1.2.3', old)
        self.api.run.update(head_sha=new_sha, run_number=11)
        publish(self.api, '1.2.3', candidate)
        self.assertEqual(new_sha, self.api.tag['object']['sha'])

    def test_package_manifest_must_match_version_before_mutation(self):
        with zipfile.ZipFile(Path(self.directory.name, NAME), 'w') as archive:
            archive.writestr('extension.yaml', 'Id: Luna\nVersion: 9.9.9\n')
        with self.assertRaisesRegex(ValueError, 'Manifest version'):
            self.prepare()
        self.assertFalse(self.api.calls)

    def test_metadata_and_asset_identity_tampering_is_rejected(self):
        candidate = self.prepare()
        original = copy.deepcopy(self.api.current)
        metadata = next(a for a in self.api.current['assets'] if a['name'] == METADATA)
        self.api.data[metadata['id']] += b' '
        with self.assertRaisesRegex(ValueError, 'Metadata hash'):
            publish(self.api, '1.2.3', candidate)
        self.api.data[metadata['id']] = self.api.data[metadata['id']][:-1]
        metadata['id'] += 100
        with self.assertRaisesRegex(ValueError, 'Metadata asset replaced'):
            publish(self.api, '1.2.3', candidate)
        self.api.current = original
        package = next(a for a in self.api.current['assets'] if a['name'] == NAME)
        package['id'] += 100
        with self.assertRaisesRegex(ValueError, 'Package asset replaced'):
            publish(self.api, '1.2.3', candidate)

    def test_redirect_strips_credentials_off_host(self):
        request = urllib.request.Request('https://api.github.com/asset', headers={'Authorization': 'Bearer secret'})
        redirected = SafeRedirect().redirect_request(request, None, 302, '', {}, 'https://storage.invalid/file')
        self.assertIsNone(redirected.get_header('Authorization'))
        same = SafeRedirect().redirect_request(request, None, 302, '', {}, 'https://api.github.com/next')
        self.assertEqual('Bearer secret', same.get_header('Authorization'))


if __name__ == '__main__':
    unittest.main()
