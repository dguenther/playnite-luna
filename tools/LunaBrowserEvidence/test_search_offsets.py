"""Search termination, unstable pages, reference disagreement, and export protection."""
import argparse
import json
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import AsyncMock, patch

import search_offsets as offsets


def response(ids=(), terminal=False):
    widgets = [{'type':'GAME_TILE', 'presentationData':{'gameId':i}} for i in ids]
    if terminal:
        widgets.insert(0, {'type':'SEARCH_NO_RESULTS_FOUND'})
    return {'pageContext':{'pageType':'search_results_empty_jumanji' if terminal else 'search_results_no_prime'},
            'pageMemberGroups':{'mainContent':{'widgets':widgets}}}


class SearchOffsetsTests(unittest.IsolatedAsyncioTestCase):
    def test_terminal_recommendations_are_excluded_from_matches(self):
        summary, ids = offsets.search_page(response(['recommendation'], True), 'TITLE_A_TO_Z',50,100)
        self.assertTrue(summary['terminal'])
        self.assertEqual([], ids)
        self.assertEqual(0, summary['matching_count'])
        self.assertEqual(1, summary['recommendation_count'])

    def test_unknown_empty_or_inconsistent_response_is_rejected(self):
        for changed in [response(), response(['game']) | {'pageContext':{'pageType':'unknown'}},
                        response(['game'], True) | {'pageContext':{'pageType':'search_results_no_prime'}}]:
            with self.subTest(response=changed), self.assertRaises(RuntimeError):
                offsets.search_page(changed, 'TITLE_A_TO_Z',50,0)

    async def test_short_page_does_not_stop_or_change_offset_step(self):
        fetch = AsyncMock(side_effect=[response(['one']), response(['two']), response(['promo'], True)])
        with patch.object(offsets.asyncio, 'sleep', new=AsyncMock()):
            result, ids = await offsets.traverse(fetch, 'a')
        self.assertEqual([0,50,100], [call.args[3] for call in fetch.await_args_list])
        self.assertEqual(['one','two'], ids)
        self.assertEqual(2, result['matching_count'])

    async def test_safety_limit_without_terminal_is_failure(self):
        with patch.object(offsets, 'MAX_PAGES',2), patch.object(offsets.asyncio, 'sleep', new=AsyncMock()):
            with self.assertRaises(RuntimeError):
                await offsets.traverse(AsyncMock(return_value=response(['game'])), 'a')

    async def test_repeat_and_larger_page_disagreements_reject_validation(self):
        for issue in ['repeat','reference']:
            with self.subTest(issue=issue):
                responses = [response(['one']), response(terminal=True),
                    response(['other'] if issue == 'repeat' else ['one']), response(terminal=True)]
                responses += [response(['other'] if issue == 'reference' else ['one']) for _ in range(3)]
                with patch.object(offsets.asyncio, 'sleep', new=AsyncMock()):
                    result = await offsets.inspect_query(AsyncMock(side_effect=responses), 'e', {'one','other'})
                self.assertFalse(result['validated'])

    async def test_failure_preserves_output_and_omits_raw_error(self):
        context = SimpleNamespace(dispose=AsyncMock())
        class FakePlaywright:
            async def __aenter__(self):
                return SimpleNamespace(request=SimpleNamespace(new_context=AsyncMock(return_value=context)))
            async def __aexit__(self, *args):
                pass
        with tempfile.TemporaryDirectory() as directory:
            source, output = Path(directory)/'probe.json', Path(directory)/'evidence.json'
            source.write_text('{"allRequestedChecksSucceeded":true,"games":[{"id":"one"}]}')
            output.write_text('previous evidence')
            args = argparse.Namespace(probe_snapshot=str(source), output=str(output))
            with patch.object(offsets, 'async_playwright', return_value=FakePlaywright()), \
                 patch.object(offsets, 'inspect_context', new=AsyncMock(side_effect=RuntimeError('private-sentinel'))), \
                 patch.dict(offsets.os.environ, {'HTTPS_PROXY':'http://proxy:8080'}):
                self.assertEqual(1, await offsets.main(args))
            self.assertEqual('previous evidence', output.read_text())
            diagnostics = Path(str(output)+'.diagnostics.json').read_text()
            self.assertNotIn('private-sentinel', diagnostics)
            self.assertEqual('Incomplete', json.loads(diagnostics)['coverage_status'])


if __name__ == '__main__':
    unittest.main()
