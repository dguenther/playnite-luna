"""Scoped completeness requires every source; failed audits cannot bless exports."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import AsyncMock, patch

import streaming_audit as audit
from test_probe import FakePlaywright
from probe import fingerprint


def matched_report():
    browser = {'config':{'defaultLocale':'en_US','marketplaceId':'ATVPDKIKX0DER',
                'clientProxy':audit.ENDPOINT.removesuffix('/getPage')},
        'genre_routes':['/browse?genre=ACTION&quick_search=all_games'],
        'pages':[{'path':p,'document_http_status':200,'scroll_terminal':True,'carousels':[]} for p in audit.SEEDS]}
    browser['pages'][2].update({'all_games_id_difference':0,'all_games_rendered_id_difference':0,
        'loaded_count':1,'card_count':1,'loaded_id_set_sha256':fingerprint(['one'])})
    api = {'genres':[{'path':browser['genre_routes'][0],'outside_browse_count':0,'response_page_type':'browse_results_all_games'}],
        'search':[{'query':q,'outside_browse_count':0,'reverse_reference_difference':0,'page_overlap':0,
                   'pages':[{'terminal':True}]} for q in audit.SEARCH_QUERIES],
        'search_union_difference':0,'search_landing':{'count':1,'outside_browse_count':0},
        'details':{'checked':1,'failures':[],'recommendations_outside_browse_count':0},
        'browse_rechecks':[{'difference':0,'response_page_type':'browse_results_all_games'} for _ in range(2)]}
    return {'browser_contexts':[copy.deepcopy(browser) for _ in range(2)],
            'api_contexts':[copy.deepcopy(api) for _ in range(2)]}


class CoverageTests(unittest.IsolatedAsyncioTestCase):
    def test_genre_routes_retain_filter_and_drop_tracking(self):
        self.assertEqual('/browse?genre=ACTION&quick_search=my_games',
            audit.public_path('/browse?quick_search=my_games&genre=ACTION&session=private-sentinel'))
        self.assertIsNone(audit.public_path('/claims'))
        self.assertIsNone(audit.public_path('https://other.example/browse'))

    def test_browse_continuation_and_truncation_are_rejected(self):
        good = {'pageContext':{'pageType':'browse_results_all_games'},
                'pageMemberGroups':{'mainContent':{'widgets':[{'type':'GAME_TILE'}]}}}
        audit.assert_browse_complete(good)
        for extra in [{'nextOffset':50},{'totalCount':2},{'widget':{'type':'LOAD_MORE'}}]:
            with self.subTest(extra=extra), self.assertRaises(RuntimeError):
                audit.assert_browse_complete(good | extra)
        with self.assertRaises(RuntimeError):
            audit.assert_browse_complete({'pageContext':good['pageContext']})

    def test_missing_source_or_difference_cannot_pass(self):
        probe = {'games':[{'id':'one'}]}
        self.assertTrue(audit.audit_passes(matched_report(),probe))
        for issue in ['context','route','genre','search','details','recommendation','continuation','region','all_grid','landing']:
            r = matched_report()
            if issue == 'context': r['api_contexts'].pop()
            elif issue == 'route': r['browser_contexts'][0]['pages'].pop()
            elif issue == 'genre': r['api_contexts'][0]['genres'][0]['outside_browse_count']=1
            elif issue == 'search': r['api_contexts'][0]['search_union_difference']=1
            elif issue == 'details': r['api_contexts'][0]['details']['checked']=0
            elif issue == 'recommendation': r['api_contexts'][0]['details']['recommendations_outside_browse_count']=1
            elif issue == 'continuation': r['browser_contexts'][0]['pages'][0]['carousels']=[{'terminal':False}]
            elif issue == 'all_grid': r['browser_contexts'][0]['pages'][2].pop('all_games_id_difference')
            elif issue == 'landing': r['api_contexts'][0].pop('search_landing')
            else: r['browser_contexts'][0]['config']['marketplaceId']='other'
            with self.subTest(issue=issue): self.assertFalse(audit.audit_passes(r,probe))

    def test_unstable_search_pages_cannot_pass(self):
        r = matched_report()
        r['api_contexts'][1]['search'][0]['pages'][0]['different_hash']='other'
        self.assertFalse(audit.audit_passes(r,{'games':[{'id':'one'}]}))

    async def run_audit(self, directory, failure=False):
        snapshot = Path(directory)/'probe.json'
        snapshot.write_text('{"region":"US","marketplaceId":"ATVPDKIKX0DER","allRequestedChecksSucceeded":true,"games":[{"id":"one"}]}')
        output, catalog = Path(directory)/'audit.json', Path(directory)/'catalog.json'
        args = argparse.Namespace(probe_snapshot=str(snapshot),output=str(output),catalog_output=str(catalog),chromium='test')
        r = matched_report()
        with patch.object(audit,'async_playwright',return_value=FakePlaywright()), \
             patch.object(audit,'browser_context',new=AsyncMock(side_effect=r['browser_contexts'])), \
             patch.object(audit,'api_context',new=AsyncMock(side_effect=RuntimeError('private-sentinel') if failure else r['api_contexts'])), \
             patch.dict(audit.os.environ,{'HTTPS_PROXY':'http://proxy:8080'}):
            result = await audit.main(args)
        return result,snapshot,output,catalog

    async def test_verified_export_is_bound_to_exact_source_and_scope(self):
        with tempfile.TemporaryDirectory() as directory:
            result,source,output,catalog = await self.run_audit(directory)
            self.assertEqual(0,result)
            evidence = json.loads(output.read_text())
            exported = json.loads(catalog.read_text())
            self.assertTrue(exported['isComplete'])
            self.assertEqual('Verified',exported['coverageStatus'])
            self.assertEqual(audit.SCOPE,exported['coverageEvidence']['scope'])
            self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(),evidence['source_snapshot_sha256'])
            self.assertEqual(evidence['source_snapshot_sha256'],exported['coverageEvidence']['sourceSnapshotSha256'])

    async def test_failed_audit_preserves_both_outputs_and_omits_raw_error(self):
        with tempfile.TemporaryDirectory() as directory:
            output, catalog = Path(directory)/'audit.json', Path(directory)/'catalog.json'
            output.write_text('previous evidence'); catalog.write_text('previous catalog')
            result,_,_,_ = await self.run_audit(directory,True)
            self.assertEqual(1,result)
            self.assertEqual('previous evidence',output.read_text())
            self.assertEqual('previous catalog',catalog.read_text())
            diagnostics = Path(str(output)+'.diagnostics.json').read_text()
            self.assertNotIn('private-sentinel',diagnostics)
            self.assertFalse(json.loads(diagnostics)['is_complete'])

    async def test_wrong_marketplace_rejected_before_network(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory)/'probe.json'
            source.write_text('{"region":"US","marketplaceId":"other","allRequestedChecksSucceeded":true,"games":[{"id":"one"}]}')
            args = argparse.Namespace(probe_snapshot=str(source),output=str(Path(directory)/'audit.json'),catalog_output=None)
            with patch.object(audit,'async_playwright') as playwright, self.assertRaises(RuntimeError):
                await audit.main(args)
            playwright.assert_not_called()

    async def test_output_path_collisions_preserve_files_before_network(self):
        cases = [
            ('source/evidence', 'probe.json', 'probe.json', None),
            ('source/catalog', 'probe.json', 'audit.json', 'probe.json'),
            ('evidence/catalog', 'probe.json', 'audit.json', 'audit.json'),
            ('source/diagnostics', 'audit.json.diagnostics.json', 'audit.json', None),
            ('source/evidence temporary', 'audit.json.tmp', 'audit.json', None),
            ('source/diagnostics temporary', 'audit.json.diagnostics.json.tmp', 'audit.json', None),
            ('source/catalog temporary', 'catalog.json.tmp', 'audit.json', 'catalog.json'),
            ('catalog/diagnostics', 'probe.json', 'audit.json', 'audit.json.diagnostics.json'),
            ('catalog/evidence temporary', 'probe.json', 'audit.json', 'audit.json.tmp'),
            ('catalog/diagnostics temporary', 'probe.json', 'audit.json', 'audit.json.diagnostics.json.tmp'),
            ('evidence/catalog temporary', 'probe.json', 'catalog.json.tmp', 'catalog.json'),
            ('normalized source/temporary', 'audit.json.tmp', 'sub/../audit.json', None),
        ]
        for name, source_name, output_name, catalog_name in cases:
            with self.subTest(collision=name), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                source, output = root/source_name, root/output_name
                catalog = root/catalog_name if catalog_name else None
                for path in [p for p in [source, output, catalog] if p is not None]:
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(b'previous output')
                source.write_bytes(b'{"region":"US","marketplaceId":"ATVPDKIKX0DER",'
                    b'"allRequestedChecksSucceeded":true,"games":[{"id":"one"}]}')
                before = {p.relative_to(root):p.read_bytes() for p in root.rglob('*') if p.is_file()}
                args = argparse.Namespace(probe_snapshot=str(source), output=str(output),
                    catalog_output=str(catalog) if catalog else None, chromium='test')
                with patch.object(audit, 'async_playwright', side_effect=AssertionError('Unexpected live call')) as playwright:
                    with self.assertRaisesRegex(RuntimeError, 'paths must be distinct'):
                        await audit.main(args)
                    playwright.assert_not_called()
                self.assertEqual(before, {p.relative_to(root):p.read_bytes() for p in root.rglob('*') if p.is_file()})


if __name__ == '__main__':
    unittest.main()
