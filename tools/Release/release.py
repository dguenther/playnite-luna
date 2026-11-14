"""Fail-closed release candidate protocol. Only the CLI performs network I/O."""
import argparse
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import re
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

MARKER = re.compile(r'<!-- luna-candidate:(.*?) -->', re.DOTALL)
METADATA = 'luna-candidate.json'
VERSION = re.compile(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\Z')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':')).encode()


def identity(manifest, project, expected):
    require(VERSION.fullmatch(expected), 'Expected version must be X.Y.Z without v or leading zeroes')
    version = re.search(r'^Version: (.+)$', manifest, re.MULTILINE)
    extension = re.search(r'^Id: ([A-Za-z0-9_-]+)$', manifest, re.MULTILINE)
    require(version and version[1] == expected and extension, 'Manifest version/ID mismatch')
    require(ET.fromstring(project).findtext('./PropertyGroup/Version') == expected, 'Project version mismatch')
    return extension[1] + '_' + expected.replace('.', '_') + '.pext'


def package_identity(data, version, name):
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        require(archive.namelist().count('extension.yaml') == 1, 'Package manifest missing or duplicated')
        manifest = archive.read('extension.yaml').decode('utf-8-sig').replace('\r\n', '\n')
    project = '<Project><PropertyGroup><Version>' + version + '</Version></PropertyGroup></Project>'
    require(identity(manifest, project, version) == name, 'Packaged manifest identity mismatch')


def marker(release):
    matches = MARKER.findall(release.get('body') or '')
    require(len(matches) <= 1, 'Ambiguous candidate marker')
    return json.loads(matches[0]) if matches else None


def body_with_state(release, state):
    body = release.get('body') or ''
    marker(release)  # Reject ambiguous state before editing notes.
    body = MARKER.sub('', body).rstrip()
    return body + '\n\n<!-- luna-candidate:' + encoded(state).decode() + ' -->'


def candidate_order(state):
    return int(state['run_number']), int(state['run_attempt'])


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        redirected = super().redirect_request(request, fp, code, msg, headers, newurl)
        require(urllib.parse.urlsplit(newurl).scheme == 'https', 'Refusing non-HTTPS redirect')
        if urllib.parse.urlsplit(request.full_url).netloc != urllib.parse.urlsplit(newurl).netloc:
            redirected.remove_header('Authorization')
        return redirected


class GitHub:
    def __init__(self):
        self.root = os.environ['GITHUB_API_URL'].rstrip('/')
        self.repo = os.environ['GITHUB_REPOSITORY']
        self.token = os.environ['GH_TOKEN']

    def request(self, method, path, data=None, raw=False):
        url = path if path.startswith('https://') else f'{self.root}/repos/{self.repo}/{path}'
        headers = {'Authorization': 'Bearer ' + self.token, 'Accept': 'application/octet-stream' if raw else 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28'}
        if data is not None:
            headers['Content-Type'] = 'application/octet-stream' if isinstance(data, bytes) else 'application/json'
            data = data if isinstance(data, bytes) else encoded(data)
        request = urllib.request.Request(url, data=data, headers=headers, method=method)
        with urllib.request.build_opener(SafeRedirect()).open(request, timeout=120) as response:
            result = response.read()
        return result if raw else json.loads(result) if result else None

    def optional(self, path):
        try:
            return self.request('GET', path)
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            raise

    def release(self, tag):
        # Drafts are not reliably addressable through the release-by-tag endpoint.
        found = []
        page = 1
        while True:
            batch = self.request('GET', f'releases?per_page=100&page={page}')
            found.extend(r for r in batch if r['tag_name'] == tag)
            if len(batch) < 100:
                break
            page += 1
        require(len(found) <= 1, 'Multiple releases use this tag')
        return found[0] if found else None

    def source_identity(self, sha, expected):
        require(re.fullmatch(r'[0-9a-f]{40}', sha), 'Source must be full SHA')
        def contents(path):
            item = self.request('GET', f'contents/{path}?ref={sha}')
            return base64.b64decode(item['content']).decode().replace('\r\n', '\n')
        return identity(contents('src/LunaPlaynite/extension.yaml'), contents('src/LunaPlaynite/LunaPlaynite.csproj'), expected)

    def download(self, asset):
        require(asset['state'] == 'uploaded', 'Asset upload incomplete')
        data = self.request('GET', f"releases/assets/{asset['id']}", raw=True)
        require(len(data) == asset['size'], 'Asset size mismatch')
        return data

    def upload(self, release, name, data):
        url = release['upload_url'].split('{')[0] + '?name=' + urllib.parse.quote(name, safe='')
        return self.request('POST', url, data)


def prepare(api, version, sha, run_id, attempt, directory, run_number=None):
    name = api.source_identity(sha, version)
    package = Path(directory, name).read_bytes()
    package_identity(package, version, name)
    tag = 'v' + version
    require(api.optional('git/ref/tags/' + tag) is None, 'Tag already exists; never replace it')
    release = api.release(tag)
    state = dict(schema=1, run_id=int(run_id), run_number=int(run_number if run_number is not None else run_id), run_attempt=int(attempt), source_sha=sha, status='preparing')
    state['candidate_id'] = f'{run_id}.{attempt}.{sha}'
    if release:
        require(release['draft'], 'Published releases cannot be modified')
        old = marker(release)
        require(old is None or candidate_order(state) >= candidate_order(old), 'Stale prepare cannot replace a newer attempt')
        # A new state is persisted BEFORE any asset mutation. Failure leaves an unpublishable draft.
        release = api.request('PATCH', f"releases/{release['id']}", dict(target_commitish=sha, body=body_with_state(release, state)))
    else:
        notes = api.request('POST', 'releases/generate-notes', dict(tag_name=tag, target_commitish=sha))
        release = api.request('POST', 'releases', dict(tag_name=tag, target_commitish=sha, name=tag, draft=True, body=body_with_state(dict(body=notes['body']), state)))
    for asset in release['assets']:
        api.request('DELETE', f"releases/assets/{asset['id']}")
    asset = api.upload(release, name, package)
    require(asset['state'] == 'uploaded' and asset['size'] == len(package), 'Package upload incomplete')
    metadata = dict(state, version=version, tag=tag, package=dict(id=asset['id'], name=name, size=len(package), sha256=digest(package)))
    metadata['status'] = 'ready'
    payload = encoded(metadata)
    uploaded = api.upload(release, METADATA, payload)
    require(api.download(asset) == package and api.download(uploaded) == payload, 'Uploaded bytes differ')
    state.update(status='ready', metadata_sha256=digest(payload), metadata_asset_id=uploaded['id'])
    # Read fresh notes so edits made during uploads are preserved.
    fresh = api.request('GET', f"releases/{release['id']}")
    require(fresh['draft'] and marker(fresh)['candidate_id'] == state['candidate_id'], 'Draft changed during preparation')
    api.request('PATCH', f"releases/{release['id']}", dict(body=body_with_state(fresh, state)))
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a', encoding='utf-8') as output:
            output.write(f"### Prepared v{version}\n\n[Open draft]({fresh['html_url']})\n\n- Candidate ID: `{state['candidate_id']}`\n- Source SHA: `{sha}`\n- Package: `{name}`\n- SHA256: `{digest(package)}`\n\nThis candidate replaces any previous unpublished candidate for this version. Test these attached bytes, then pass this candidate ID to Publish release after this entire run succeeds.\n")
    return state['candidate_id']


def publish(api, version, candidate_id):
    require(VERSION.fullmatch(version), 'Expected version must be X.Y.Z')
    release = api.release('v' + version)
    require(release, 'Release does not exist')
    state = marker(release)
    require(state and state['schema'] == 1 and state['status'] == 'ready' and state['candidate_id'] == candidate_id, 'Draft is not the requested ready candidate')
    sha = state['source_sha']
    require(candidate_id == f"{state['run_id']}.{state['run_attempt']}.{sha}", 'Malformed candidate ID')
    require(release['target_commitish'] == sha and not release['prerelease'], 'Release target/type changed')
    assets = {a['name']: a for a in release['assets']}
    require(len(assets) == len(release['assets']) == 2 and METADATA in assets, 'Unexpected release assets')
    meta_asset = assets[METADATA]
    require(meta_asset['id'] == state['metadata_asset_id'], 'Metadata asset replaced')
    raw = api.download(meta_asset)
    require(digest(raw) == state['metadata_sha256'], 'Metadata hash mismatch')
    meta = json.loads(raw)
    for key in ('schema', 'run_id', 'run_number', 'run_attempt', 'source_sha', 'status', 'candidate_id'):
        require(meta[key] == state[key], 'Candidate metadata mismatch: ' + key)
    require(meta['version'] == version and meta['tag'] == release['tag_name'], 'Version/tag mismatch')
    package = meta['package']
    require(api.source_identity(sha, version) == package['name'], 'Package/source identity mismatch')
    attached = assets.get(package['name'])
    require(attached and attached['id'] == package['id'] and attached['size'] == package['size'], 'Package asset replaced')
    package_bytes = api.download(attached)
    require(digest(package_bytes) == package['sha256'], 'Package hash mismatch')
    package_identity(package_bytes, version, package['name'])
    run = api.request('GET', f"actions/runs/{state['run_id']}")
    require(run['run_number'] == state['run_number'] and run['run_attempt'] == state['run_attempt'], 'Prepare was rerun; prepare a new candidate')
    require(run['event'] == 'workflow_dispatch' and run['path'] == '.github/workflows/prepare-release.yml' and run['head_sha'] == sha and run['status'] == 'completed' and run['conclusion'] == 'success', 'Prepare workflow did not succeed for this source')
    tag = api.optional('git/ref/tags/v' + version)
    if tag:
        # This protocol creates lightweight tags only. Never move or reinterpret existing tags.
        require(tag['object']['type'] == 'commit' and tag['object']['sha'] == sha, 'Existing tag points elsewhere')
    if not release['draft']:
        require(tag, 'Published release has no matching tag')
        return 'Already published this exact candidate; no changes made'
    # Publishing creates a missing tag at the recorded full SHA. No build or re-upload occurs.
    api.request('PATCH', f"releases/{release['id']}", dict(draft=False, target_commitish=sha))
    return 'Published ' + candidate_id


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('command', choices=['identity', 'prepare', 'publish'])
    parser.add_argument('--version', required=True)
    parser.add_argument('--directory', default='artifacts/packages')
    args = parser.parse_args()
    if args.command == 'identity':
        print(identity(Path('src/LunaPlaynite/extension.yaml').read_text(), Path('src/LunaPlaynite/LunaPlaynite.csproj').read_text(), args.version))
    elif args.command == 'prepare':
        print(prepare(GitHub(), args.version, os.environ['SOURCE_SHA'], os.environ['GITHUB_RUN_ID'], os.environ['GITHUB_RUN_ATTEMPT'], args.directory, os.environ['GITHUB_RUN_NUMBER']))
    else:
        print(publish(GitHub(), args.version, os.environ['CANDIDATE_ID']))


if __name__ == '__main__':
    main()
