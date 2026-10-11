"""Validate sampled search offsets independently of browse and browser scrolling."""
import argparse
import asyncio
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path

from playwright.async_api import async_playwright
from probe import catalog, fingerprint, walk


PAGE_SIZE = 50
MAX_PAGES = 20
QUERIES = ('a', 'e')
ENDPOINT = 'https://proxy-prod.us-east-1.tempo.digital.a2z.com/getPage'


def search_page(data, sort, size, start):
    """Recommendations on an explicit no-results page are never query matches."""
    page_type = data.get('pageContext', {}).get('pageType')
    widgets = data.get('pageMemberGroups', {}).get('mainContent', {}).get('widgets', [])
    no_results = any(n.get('type') == 'SEARCH_NO_RESULTS_FOUND' for n in walk(widgets))
    games = catalog(data)
    if page_type == 'search_results_empty_jumanji' and no_results:
        ids, terminal = [], True
    elif page_type == 'search_results_no_prime' and not no_results and games:
        ids, terminal = list(games), False
    else:
        raise RuntimeError('Unrecognized or malformed search state')
    summary = {'sort':sort, 'page_size':size, 'start_index':start,
        'response_page_type':page_type, 'terminal':terminal,
        'matching_count':len(ids), 'recommendation_count':len(games) if terminal else 0,
        'id_set_sha256':fingerprint(ids),
        'id_order_sha256':hashlib.sha256('\n'.join(ids).encode()).hexdigest()}
    return summary, ids


async def traverse(fetch, query):
    pages, ids, seen, overlap = [], [], set(), 0
    for start in range(0, MAX_PAGES * PAGE_SIZE, PAGE_SIZE):
        summary, current = search_page(await fetch(query, 'TITLE_A_TO_Z', PAGE_SIZE, start),
                                       'TITLE_A_TO_Z', PAGE_SIZE, start)
        pages.append(summary)
        overlap += len(seen & set(current))
        seen.update(current)
        ids.extend(current)
        if summary['terminal']:
            return {'pages':pages, 'matching_count':len(seen),
                    'id_set_sha256':fingerprint(seen), 'page_overlap':overlap}, ids
        # Advance by requested size, not the number of unique rendered tiles.
        await asyncio.sleep(0.5)
    raise RuntimeError('Search safety limit reached without an explicit terminal state')


async def inspect_query(fetch, query, expected):
    first, ids = await traverse(fetch, query)
    second, repeated = await traverse(fetch, query)
    result = {'query':query, 'traversals':[first, second],
        'repeat_set_difference':len(set(ids) ^ set(repeated)),
        'repeat_order_matches':ids == repeated,
        'outside_browse_count':len(set(ids) - expected), 'references':[]}
    for sort, size in [('TITLE_A_TO_Z',100), ('TITLE_A_TO_Z',200), ('TITLE_Z_TO_A',100)]:
        summary, reference = search_page(await fetch(query, sort, size, 0), sort, size, 0)
        summary['paged_set_difference'] = len(set(ids) ^ set(reference))
        result['references'].append(summary)
        await asyncio.sleep(0.5)
    result['validated'] = (not result['repeat_set_difference'] and result['repeat_order_matches']
        and not result['outside_browse_count'] and not first['page_overlap']
        and not second['page_overlap']
        and all(not p['paged_set_difference'] for p in result['references']))
    if query == 'a':
        result['relevance_repeats'] = []
        previous = None
        for _ in range(3):
            summary, current = search_page(await fetch(query, 'RELEVANCE',50,0), 'RELEVANCE',50,0)
            summary['same_request_set_difference'] = (len(set(current) ^ set(previous))
                if previous is not None else None)
            summary['same_request_order_matches'] = current == previous if previous is not None else None
            result['relevance_repeats'].append(summary)
            previous = current
            await asyncio.sleep(0.5)
    return result


async def inspect_context(context, expected):
    async def fetch(query, sort, size, start):
        body = {'timeout':10000, 'featureScheme':'WEB_V1',
            'searchContext':{'query':query, 'sort':sort,
                             'pagination':{'startIndex':start, 'pageSize':size}},
            'pageContext':{'pageType':'multistate_search_results', 'pageId':'default'},
            'inputContext':{'gamepadTypes':[]}, 'dynamicFeatures':[]}
        response = await context.post(ENDPOINT, data=json.dumps(body), timeout=30000)
        if response.status != 200:
            raise RuntimeError('Search request failed')
        return await response.json()
    return [await inspect_query(fetch, query, expected) for query in QUERIES]


async def main(args):
    probe = json.loads(Path(args.probe_snapshot).read_text())
    if not probe.get('allRequestedChecksSucceeded') or not probe.get('games'):
        raise RuntimeError('A successful live probe snapshot is required')
    expected = {g['id'] for g in probe['games']}
    report = {'observed_at_utc':datetime.now(timezone.utc).isoformat(),
        'method':'Two independent anonymous Node request contexts; configured proxy and verified TLS',
        'endpoint':ENDPOINT, 'contexts':[], 'is_complete':False,
        'limitation':'Manual search requests, not frontend continuation. Validates sampled queries only; browse uses a separate single-response contract.'}
    unsuccessful = False
    async with async_playwright() as pw:
        for index in range(1,3):
            context = await pw.request.new_context(proxy={'server':os.environ['HTTPS_PROXY']},
                extra_http_headers={'content-type':'text/plain;charset=UTF-8',
                    'origin':'https://luna.amazon.com', 'referer':'https://luna.amazon.com/',
                    'x-amz-locale':'en_US', 'x-amz-marketplace-id':'ATVPDKIKX0DER',
                    'x-amz-platform':'web', 'x-amz-device-type':'browser', 'accept-language':'en-US'})
            result = {'context':index}
            report['contexts'].append(result)
            try:
                result['queries'] = await inspect_context(context, expected)
                unsuccessful |= any(not q['validated'] for q in result['queries'])
            except Exception as exc:
                result['failure_type'] = type(exc).__name__
                unsuccessful = True
            finally:
                await context.dispose()
    if not unsuccessful:
        for first, second in zip(report['contexts'][0]['queries'], report['contexts'][1]['queries']):
            second['cross_context_order_matches'] = (first['traversals'][0]['pages'] == second['traversals'][0]['pages'])
            unsuccessful |= not second['cross_context_order_matches']
    report['sampled_search_pagination_validated'] = not unsuccessful
    report['coverage_status'] = 'Incomplete' if unsuccessful else 'Unverified'
    destination = Path(args.output + ('.diagnostics.json' if unsuccessful else ''))
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(destination.suffix + '.tmp')
    temporary.write_text(json.dumps(report, indent=2) + '\n')
    temporary.replace(destination)
    print('Wrote ' + str(destination))
    return 1 if unsuccessful else 2


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--probe-snapshot', required=True)
    parser.add_argument('--output', required=True)
    raise SystemExit(asyncio.run(main(parser.parse_args())))
