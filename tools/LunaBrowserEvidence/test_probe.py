"""Offline checks for research evidence safety and export preservation."""
import argparse
import asyncio
import json
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import AsyncMock, patch

import probe


class FakePlaywright:
    async def __aenter__(self):
        page = SimpleNamespace(goto=AsyncMock())
        context = SimpleNamespace(new_page=AsyncMock(return_value=page), close=AsyncMock())
        browser = SimpleNamespace(version='test', new_context=AsyncMock(return_value=context), close=AsyncMock())
        return SimpleNamespace(chromium=SimpleNamespace(launch=AsyncMock(return_value=browser)))

    async def __aexit__(self, *args):
        pass


class EvidenceTests(unittest.IsolatedAsyncioTestCase):
    async def test_context_cleanup_finishes_with_unresolved_background_route(self):
        unresolved = asyncio.Event()

        async def unroute_all(*, behavior):
            if behavior == 'wait':
                await unresolved.wait()

        context = SimpleNamespace(unroute_all=AsyncMock(side_effect=unroute_all),
                                  close=AsyncMock())
        await asyncio.wait_for(probe.close_context(context), timeout=0.2)
        context.close.assert_awaited_once()

    async def test_pending_catalog_requests_cannot_be_treated_as_terminal(self):
        observation = probe.Observation(None)
        observation.pending_catalog_requests = 1
        with patch.object(probe.asyncio, 'sleep', new=AsyncMock()):
            with self.assertRaises(RuntimeError):
                await observation.drain_catalog_requests()
        observation.pending_catalog_requests = 0
        await observation.drain_catalog_requests()

    async def run_probe(self, directory, observation):
        source = Path(directory)/'probe.json'
        source.write_text('{"retrievedAt":"test"}')
        args = argparse.Namespace(probe_snapshot=str(source), output=str(Path(directory)/'evidence.json'), chromium='test')
        with patch.object(probe, 'async_playwright', return_value=FakePlaywright()), \
             patch.object(probe, 'investigate', observation), \
             patch.dict(probe.os.environ, {'HTTPS_PROXY':'http://proxy:8080'}):
            return await probe.main(args)

    @staticmethod
    def matched_view():
        return {k:0 for k in ['api_minus_rendered','rendered_minus_api','probe_minus_api',
                    'api_minus_probe','title_mismatches','missing_game_links','probe_link_mismatches']} | {
                    'catalog_calls_during_scroll':[], 'failed_requests_during_scroll':{}}

    async def test_success_remains_unverified_and_records_two_contexts(self):
        with tempfile.TemporaryDirectory() as directory:
            result = await self.run_probe(directory, AsyncMock(return_value={'views':[self.matched_view()]}))
            report = json.loads((Path(directory)/'evidence.json').read_text())
            self.assertEqual(2,result)
            self.assertEqual(2,len(report['contexts']))
            self.assertFalse(report['is_complete'])
            self.assertEqual('Unverified',report['coverage_status'])

    async def test_mismatch_or_failed_catalog_call_preserves_previous_output(self):
        for changed in [{'probe_minus_api':1}, {'failed_requests_during_scroll':{'proxy-prod.us-east-1.tempo.digital.a2z.com/getPage HTTP 503':1}}]:
            with self.subTest(changed=changed), tempfile.TemporaryDirectory() as directory:
                output = Path(directory)/'evidence.json'
                output.write_text('previous evidence')
                view = self.matched_view() | changed
                result = await self.run_probe(directory, AsyncMock(return_value={'views':[view]}))
                self.assertEqual(1,result)
                self.assertEqual('previous evidence',output.read_text())
                report = json.loads(Path(str(output)+'.diagnostics.json').read_text())
                self.assertEqual('Incomplete',report['coverage_status'])

    async def test_exception_preserves_previous_output_without_raw_error(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)/'evidence.json'
            output.write_text('previous evidence')
            result = await self.run_probe(directory, AsyncMock(side_effect=RuntimeError('private-error-detail')))
            self.assertEqual(1,result)
            self.assertEqual('previous evidence',output.read_text())
            diagnostics = Path(str(output)+'.diagnostics.json').read_text()
            self.assertNotIn('private-error-detail',diagnostics)
            self.assertEqual('RuntimeError',json.loads(diagnostics)['failure_type'])

    async def test_saved_request_omits_credentials_and_guest_tokens(self):
        secret = 'private-sentinel'
        headers = {'cookie':secret,'authorization':secret,'x-amz-service-token':secret,
                   'referer':'https://luna.amazon.com/browse?session='+secret,
                   'x-amz-marketplace-id':'ATVPDKIKX0DER'}
        body = {'pageContextToken':secret,'featureScheme':'WEB_V1',
                'pageContext':{'pageType':'multistate_browse_results','pageId':'default'},
                'searchContext':{'query':'all_games','sort':'TITLE_A_TO_Z','filtersList':[]},
                'clientContext':{'refMarker':secret,'browserMetadata':{'browserType':'Chrome','sessionToken':secret}}}
        request = SimpleNamespace(url='https://proxy-prod.us-east-1.tempo.digital.a2z.com/getPage',
                    post_data=json.dumps(body), all_headers=AsyncMock(return_value=headers))
        response = SimpleNamespace(status=200,json=AsyncMock(return_value={
                    'pageContext':{'pageType':'browse_results_all_games'},'serviceToken':secret}))
        context = SimpleNamespace(request=SimpleNamespace(fetch=AsyncMock(return_value=response)))
        route = SimpleNamespace(request=request, fulfill=AsyncMock(),abort=AsyncMock())
        observation = probe.Observation(context)
        await observation.forward(route)
        serialized = json.dumps(observation.calls)
        self.assertEqual(1,len(observation.calls))
        self.assertNotIn(secret,serialized)
        self.assertIn('ATVPDKIKX0DER',serialized)
        route.fulfill.assert_awaited_once()


if __name__ == '__main__':
    unittest.main()
