"""Offline checks for partial comparisons, sanitized evidence, and failed exports."""
import argparse
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import AsyncMock, patch

import discovery
from test_probe import FakePlaywright


class DiscoveryTests(unittest.IsolatedAsyncioTestCase):
    @staticmethod
    def matched_context():
        search = {k:0 for k in ['api_rendered_id_difference','title_mismatches',
                               'outside_browse_count','probe_link_mismatches']} | {
                    'recognizable_title_found':True,'failed_catalog_requests':{}}
        view = {'outside_browse_id_count':0,'outside_browse_link_count':0,'failed_catalog_requests':{}}
        game = {k:True for k in ['identity_matches','canonical_url_matches',
                                'rendered_url_matches','rendered_title_matches']} | {
                    'failed_catalog_requests':{},'document_http_status':200,'detail_http_status':200}
        return {'search':[search.copy() for _ in range(8)],'discovery':[view.copy() for _ in range(2)],
                'game_pages':[game.copy() for _ in range(5)],
                'config':{'defaultLocale':'en_US','marketplaceId':'ATVPDKIKX0DER',
                          'clientProxy':'https://proxy-prod.us-east-1.tempo.digital.a2z.com'}}

    async def run_tool(self, directory, investigate):
        source = Path(directory)/'probe.json'
        source.write_text('{"allRequestedChecksSucceeded":true,"retrievedAt":"test"}')
        args = argparse.Namespace(probe_snapshot=str(source),output=str(Path(directory)/'evidence.json'),chromium='test')
        with patch.object(discovery,'async_playwright',return_value=FakePlaywright()), \
             patch.object(discovery,'investigate',investigate), \
             patch.dict(discovery.os.environ,{'HTTPS_PROXY':'http://proxy:8080'}):
            return await discovery.main(args)

    async def test_successful_sample_comparisons_remain_unverified(self):
        async def investigate(browser,probe,index,report):
            report['contexts'].append(self.matched_context())
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(2,await self.run_tool(directory,investigate))
            report=json.loads((Path(directory)/'evidence.json').read_text())
            self.assertFalse(report['is_complete'])
            self.assertEqual('Unverified',report['coverage_status'])

    async def test_exception_preserves_existing_evidence_and_omits_raw_error(self):
        with tempfile.TemporaryDirectory() as directory:
            output=Path(directory)/'evidence.json'
            output.write_text('previous evidence')
            self.assertEqual(1,await self.run_tool(directory,AsyncMock(side_effect=RuntimeError('private-sentinel'))))
            self.assertEqual('previous evidence',output.read_text())
            diagnostics=Path(str(output)+'.diagnostics.json').read_text()
            self.assertNotIn('private-sentinel',diagnostics)
            self.assertEqual('Incomplete',json.loads(diagnostics)['coverage_status'])

    def test_missing_views_region_mismatch_and_failed_links_reject_comparison(self):
        report={'contexts':[self.matched_context(),self.matched_context()]}
        self.assertFalse(discovery.failed(report))
        for kind in ['missing_context','missing_search','wrong_marketplace','bad_http','bad_link']:
            with self.subTest(kind=kind):
                changed=json.loads(json.dumps(report))
                if kind=='missing_context':
                    changed['contexts'].pop()
                elif kind=='missing_search':
                    changed['contexts'][0]['search'].pop()
                elif kind=='wrong_marketplace':
                    changed['contexts'][0]['config']['marketplaceId']='other'
                elif kind=='bad_http':
                    changed['contexts'][0]['game_pages'][0]['document_http_status']=404
                else:
                    changed['contexts'][0]['game_pages'][0]['canonical_url_matches']=False
                self.assertTrue(discovery.failed(changed))

    def test_request_summary_drops_nested_tokens_and_headers(self):
        entry={'path':'/getPage','status':200,'response_page_type':'search_results_no_prime',
            'headers':{'authorization':'private-sentinel'},
            'request':{'pageContext':{'pageType':'multistate_search_results','token':'private-sentinel'},
                'searchContext':{'query':'a','sort':'RELEVANCE',
                    'pagination':{'startIndex':0,'pageSize':50,'nextToken':'private-sentinel'}}}}
        summary=discovery.request_summary(entry)
        self.assertEqual(0,summary['start_index'])
        self.assertEqual(50,summary['page_size'])
        self.assertNotIn('private-sentinel',json.dumps(summary))


if __name__=='__main__':
    unittest.main()
