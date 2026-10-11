"""Audit the public US streaming catalog; save scoped evidence, never raw responses."""
import argparse
import asyncio
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import string
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit

from playwright.async_api import async_playwright
from discovery import presentation, request_summary, scroll, wait_response
from probe import CARDS, Observation, catalog, close_context, fingerprint, walk
from search_offsets import ENDPOINT, search_page, traverse


SCOPE = 'PublicUSStreamingCatalog'
SEARCH_QUERIES = string.ascii_lowercase + string.digits
HEADERS = {'content-type':'text/plain;charset=UTF-8', 'origin':'https://luna.amazon.com',
    'referer':'https://luna.amazon.com/', 'x-amz-locale':'en_US',
    'x-amz-marketplace-id':'ATVPDKIKX0DER', 'x-amz-platform':'web',
    'x-amz-device-type':'browser', 'accept-language':'en-US'}
SEEDS = ['/', '/gamenight'] + ['/browse?quick_search='+q for q in
    ['all_games','included_with_prime','luna_premium','game_night']]


def public_path(value):
    if not isinstance(value,str) or not value.startswith(('/', 'https://luna.amazon.com/')):
        return None
    p = urlsplit(value)
    if p.netloc and p.netloc != 'luna.amazon.com':
        return None
    if p.path != '/' and not p.path.startswith(('/home','/browse','/gamenight','/game/','/subscription/','/genre/','/playlist/','/store','/sale')):
        return None
    query = [(k,v) for k,v in parse_qsl(p.query) if k in ['quick_search','genre','sort']]
    return urlunsplit(('', '', '/' if p.path == '/home' else p.path, urlencode(sorted(query)), ''))


def strings(value):
    if isinstance(value,dict):
        for child in value.values():
            yield from strings(child)
    elif isinstance(value,list):
        for child in value:
            yield from strings(child)
    elif isinstance(value,str):
        yield value


def page_observations(responses):
    routes, ids = set(), set()
    for _,data in responses:
        for node in walk(data):
            pd = presentation(node)
            for item in walk(pd):
                if isinstance(item.get('gameId'),str):
                    ids.add(item['gameId'])
            for value in list(strings(pd)) + [node.get(k) for k in ['target','uri','url','href']]:
                path = public_path(value)
                if path:
                    routes.add(path)
    return routes, ids


CAROUSEL_STATE = """e => {const s=e.swiper; if(!s) throw Error('Missing swiper');
 s.autoplay?.stop(); return {loop:!!s.params.loop,
 count:s.params.loop?s.slides.length-2*s.loopedSlides:s.slides.length,
 active:s.realIndex,end:!!s.isEnd};}"""


async def sweep_carousels(page, obs):
    summaries = []
    locators = page.locator('.swiper-container')
    for index in range(await locators.count()):
        element = locators.nth(index)
        state = await element.evaluate(CAROUSEL_STATE)
        visited = 0
        while visited < state['count']:
            if visited >= 200:
                raise RuntimeError('Carousel safety limit exhausted')
            await element.evaluate("(e,i)=>e.swiper.params.loop?e.swiper.slideToLoop(i,0):e.swiper.slideTo(i,0)", visited)
            await page.wait_for_timeout(100)
            visited += 1
            state = await element.evaluate(CAROUSEL_STATE)
        await obs.drain_catalog_requests()
        if not state['loop'] and state['count'] and not state['end']:
            raise RuntimeError('Carousel did not reach its end')
        summaries.append({'slide_count':state['count'], 'slides_visited':visited,
                          'loop':state['loop'], 'terminal':state['loop'] or state['end'] or not state['count']})
    return summaries


async def browser_context(browser, probe, index):
    expected = {g['id']:g for g in probe['games']}
    expected_paths = {urlsplit(g['url']).path for g in expected.values()}
    ctx = await browser.new_context(locale='en-US', service_workers='block', viewport={'width':1365,'height':900})
    obs = Observation(ctx)
    await ctx.route('**/*',obs.forward)
    page = await ctx.new_page()
    result = {'context':index, 'pages':[], 'genre_routes':[], 'account_routes_excluded':[]}
    queue, visited, genres = list(SEEDS), set(), set()
    try:
        while queue:
            if len(visited) >= 60:
                raise RuntimeError('Public route safety limit exhausted')
            path = queue.pop(0)
            if path in visited:
                continue
            visited.add(path)
            before, blocked_before = len(obs.responses), obs.blocked.copy()
            document = await page.goto('https://luna.amazon.com'+path, wait_until='domcontentloaded')
            if not document or document.status != 200:
                raise RuntimeError('Public page document failed')
            is_browse = urlsplit(path).path in ['/browse','/gamenight/browse']
            await wait_response(page,obs,before,lambda e,d:e['path']=='/getPage' and (
                (e['request'].get('pageContext',{}).get('pageType')=='multistate_browse_results')
                if is_browse else e['response_page_type'] not in [None,'navigation_anonymous','filters_dialog']))
            await page.wait_for_timeout(600)
            trace = await scroll(page,obs)
            carousels = await sweep_carousels(page,obs)
            routes, ids = page_observations(obs.responses[before:])
            if not ids:
                raise RuntimeError('Public streaming view returned no game identities')
            links = await page.locator('a[href]').evaluate_all('es=>es.map(e=>e.getAttribute("href"))')
            routes.update(p for p in map(public_path,links) if p)
            game_paths = {p for p in routes if p.startswith('/game/')}
            cards = {c['id']:c for c in await page.locator('[data-test-id="game_tile"]').evaluate_all(CARDS)}
            failures = {k:v for k,v in (obs.blocked-blocked_before).items() if k.startswith('proxy-prod.')}
            item = {'path':path, 'document_http_status':document.status,
                'loaded_count':len(ids), 'loaded_id_set_sha256':fingerprint(ids),
                'outside_browse_count':len(ids-set(expected)),
                'game_link_count':len(game_paths), 'outside_browse_link_count':len(game_paths-expected_paths),
                'card_count':len(cards), 'card_link_mismatches':sum(c['href'] != urlsplit(expected[k]['url']).path
                    for k,c in cards.items() if k in expected),
                'card_title_mismatches':sum(c['title'] != expected[k]['title'] for k,c in cards.items() if k in expected),
                'scroll_terminal':trace[-1]['at_bottom'], 'carousels':carousels,
                'requests':[request_summary(e) for e,_ in obs.responses[before:]], 'failed_catalog_requests':failures}
            if path == '/browse?quick_search=all_games':
                item['all_games_id_difference'] = len(ids ^ set(expected))
                item['all_games_rendered_id_difference'] = len(set(cards) ^ set(expected))
            result['pages'].append(item)
            print(json.dumps({'context':index,'page':path,'count':len(ids),'outside':item['outside_browse_count']}),flush=True)
            for route in sorted(routes):
                if route.startswith('/game/'):
                    continue
                p = urlsplit(route)
                q = dict(parse_qsl(p.query))
                if 'genre' in q:
                    # Home genre links default to account-only My Games. Applying
                    # the same observed filter to All is supported anonymously.
                    if q.get('quick_search') == 'my_games':
                        result['account_routes_excluded'].append(route)
                        q['quick_search'] = 'all_games'
                    genres.add(urlunsplit(('', '', p.path, urlencode(sorted(q.items())), '')))
                elif q.get('quick_search') == 'my_games':
                    result['account_routes_excluded'].append(route)
                else:
                    if route == '/browse':
                        route = '/browse?quick_search=all_games'
                    if route not in visited and route not in queue:
                        queue.append(route)
        result['genre_routes'] = sorted(genres)
        result['account_routes_excluded'] = sorted(set(result['account_routes_excluded']))
        result['config'] = obs.config
    finally:
        await close_context(ctx)
    return result


async def post(context, body):
    response = await context.post(ENDPOINT,data=json.dumps(body),headers=HEADERS,timeout=30000)
    if response.status != 200:
        raise RuntimeError('Catalog API request failed')
    return await response.json()


def assert_browse_complete(data):
    widgets = data.get('pageMemberGroups',{}).get('mainContent',{}).get('widgets')
    page_type = data.get('pageContext',{}).get('pageType')
    if not isinstance(widgets,list) or not isinstance(page_type,str) or not page_type.startswith('browse_results_'):
        raise RuntimeError('Malformed browse response')
    markers = {'nexttoken','nextpagetoken','continuationtoken','continuation','nextcursor','cursor',
        'nextpage','nextpageurl','nextpageindex','nextoffset','hasmore','hasnextpage','pagination','paginationcontext','loadmore'}
    count = sum(n.get('type')=='GAME_TILE' for n in walk(widgets))
    for node in walk(data):
        if node.get('type') in ['PAGINATION','LOAD_MORE','NEXT_PAGE']:
            raise RuntimeError('Unresolved browse continuation')
        for key,value in node.items():
            if key.lower() in markers and value not in (None,False,'',[],{}):
                raise RuntimeError('Unresolved browse continuation')
            if key.lower() in ['totalcount','totalresults'] and isinstance(value,int) and value > count:
                raise RuntimeError('Browse total exceeds observed tiles')


def body(page_type, page_id='default', search=None):
    value = {'timeout':10000, 'featureScheme':'WEB_V1',
        'pageContext':{'pageType':page_type,'pageId':page_id},
        'inputContext':{'gamepadTypes':[]}, 'dynamicFeatures':[]}
    if search is not None:
        value['searchContext'] = search
    return value


async def api_context(pw, probe, genres, index):
    expected = {g['id']:g for g in probe['games']}
    ctx = await pw.request.new_context(proxy={'server':os.environ['HTTPS_PROXY']})
    result = {'context':index, 'genres':[], 'search':[], 'details':{'checked':0,'failures':[], 'primary_labels':{}}, 'browse_rechecks':[]}
    try:
        for path in genres:
            q = dict(parse_qsl(urlsplit(path).query))
            query = q.get('quick_search') or ('game_night' if path.startswith('/gamenight') else 'all_games')
            data = await post(ctx,body('multistate_browse_results',search={'query':query,'sort':'TITLE_A_TO_Z',
                    'filtersList':[{'key':'GENRE','value':q['genre']}]}))
            assert_browse_complete(data)
            games = catalog(data)
            result['genres'].append({'path':path, 'response_page_type':data.get('pageContext',{}).get('pageType'),
                'count':len(games), 'id_set_sha256':fingerprint(games), 'outside_browse_count':len(set(games)-set(expected))})
            await asyncio.sleep(0.5)
        async def fetch(query,sort,size,start):
            return await post(ctx,body('multistate_search_results',search={'query':query,'sort':sort,
                         'pagination':{'startIndex':start,'pageSize':size}}))
        landing = await fetch('', 'RELEVANCE',50,0)
        if landing.get('pageContext',{}).get('pageType') != 'search_results_no_query_jumanji':
            raise RuntimeError('Unrecognized search landing state')
        landing_games = catalog(landing)
        result['search_landing'] = {'count':len(landing_games), 'id_set_sha256':fingerprint(landing_games),
            'outside_browse_count':len(set(landing_games)-set(expected))}
        seen = set()
        for query in SEARCH_QUERIES:
            summary, ids = await traverse(fetch,query)
            _, reference = search_page(await fetch(query,'TITLE_Z_TO_A',200,0),'TITLE_Z_TO_A',200,0)
            summary.update({'query':query, 'outside_browse_count':len(set(ids)-set(expected)),
                            'reverse_reference_difference':len(set(ids)^set(reference))})
            result['search'].append(summary)
            seen.update(ids)
            await asyncio.sleep(0.5)
        result['search_union_difference'] = len(seen ^ set(expected))
        result['search_union_count'] = len(seen)
        result['search_union_id_set_sha256'] = fingerprint(seen)
        print(json.dumps({'context':index,'search_union':len(seen),'difference':result['search_union_difference']}),flush=True)
        labels = Counter()
        recommendations = set()
        for game in expected.values():
            request = body('game_detail',game['id'])
            request['pageContext'].update({'encryptPageId':False,'productStage':'Release'})
            data = await post(ctx,request)
            products = [presentation(n) for n in walk(data) if n.get('type')=='PRODUCT_DETAIL']
            seo = [presentation(n) for n in walk(data) if n.get('type')=='SEO_DETAIL']
            valid = (data.get('pageContext',{}).get('pageType')=='game_detail_anonymous' and len(products)==len(seo)==1
                and products[0].get('productId')==game['id'] and products[0].get('asin')==game['asin']
                and products[0].get('title')==game['title'] and seo[0].get('canonicalUrl')==game['url'])
            if not valid:
                result['details']['failures'].append({'title':game['title'],'identity_and_canonical_match':bool(valid)})
            for node in walk(data):
                if node.get('type')=='PRODUCT_DETAIL_PRIMARY_BUTTON':
                    labels[presentation(node).get('primaryText','Missing')] += 1
                if node.get('type') in ['GAME_TILE','GAME_TILE_VERTICAL']:
                    value = presentation(node)
                    if isinstance(value.get('gameId'),str):
                        recommendations.add(value['gameId'])
            result['details']['checked'] += 1
            if result['details']['checked'] % 20 == 0:
                print(json.dumps({'context':index,'details_checked':result['details']['checked'],
                                  'failures':len(result['details']['failures'])}),flush=True)
            await asyncio.sleep(0.5)
        result['details']['primary_labels'] = dict(labels)
        result['details']['recommendation_count'] = len(recommendations)
        result['details']['recommendations_outside_browse_count'] = len(recommendations-set(expected))
        for sort in ['TITLE_A_TO_Z','TITLE_Z_TO_A']:
            data = await post(ctx,body('multistate_browse_results',search={'query':'all_games','sort':sort,'filtersList':[]}))
            assert_browse_complete(data)
            games = catalog(data)
            result['browse_rechecks'].append({'sort':sort, 'count':len(games), 'id_set_sha256':fingerprint(games),
                'difference':len(set(games)^set(expected)), 'response_page_type':data.get('pageContext',{}).get('pageType')})
    finally:
        await ctx.dispose()
    return result


def audit_passes(report, probe):
    expected = len(probe['games'])
    if len(report['browser_contexts']) != 2 or len(report['api_contexts']) != 2:
        return False
    for browser in report['browser_contexts']:
        if browser.get('config') != {'defaultLocale':'en_US','marketplaceId':'ATVPDKIKX0DER','clientProxy':ENDPOINT.removesuffix('/getPage')}:
            return False
        if not set(SEEDS).issubset({p['path'] for p in browser['pages']}) or not browser['genre_routes']:
            return False
        all_page = next(p for p in browser['pages'] if p['path']=='/browse?quick_search=all_games')
        if (all_page.get('all_games_id_difference') != 0 or all_page.get('all_games_rendered_id_difference') != 0
                or all_page.get('loaded_count') != expected or all_page.get('card_count') != expected
                or all_page.get('loaded_id_set_sha256') != fingerprint(g['id'] for g in probe['games'])):
            return False
        for page in browser['pages']:
            if page['document_http_status'] != 200 or not page['scroll_terminal'] or any(page.get(k,0) for k in
                ['outside_browse_count','outside_browse_link_count','card_link_mismatches','card_title_mismatches',
                 'failed_catalog_requests','all_games_id_difference','all_games_rendered_id_difference']):
                return False
            if any(not c['terminal'] for c in page['carousels']):
                return False
    if report['browser_contexts'][0]['genre_routes'] != report['browser_contexts'][1]['genre_routes']:
        return False
    if {p['path'] for p in report['browser_contexts'][0]['pages']} != {p['path'] for p in report['browser_contexts'][1]['pages']}:
        return False
    for api in report['api_contexts']:
        if not api.get('search_landing',{}).get('count') or api['search_landing']['outside_browse_count']:
            return False
        if {g['path'] for g in api['genres']} != set(report['browser_contexts'][0]['genre_routes']):
            return False
        if any(g['outside_browse_count'] or not str(g['response_page_type']).startswith('browse_results_') for g in api['genres']):
            return False
        if ''.join(q['query'] for q in api['search']) != SEARCH_QUERIES or api['search_union_difference']:
            return False
        if any(q['outside_browse_count'] or q['reverse_reference_difference'] or q['page_overlap']
               or not q['pages'][-1]['terminal'] for q in api['search']):
            return False
        if api['details']['checked'] != expected or api['details']['failures'] or api['details']['recommendations_outside_browse_count']:
            return False
        if len(api['browse_rechecks']) != 2 or any(p['difference'] or p['response_page_type']!='browse_results_all_games' for p in api['browse_rechecks']):
            return False
    for first,second in zip(report['api_contexts'][0]['search'],report['api_contexts'][1]['search']):
        if first['pages'] != second['pages']:
            return False
    return report['api_contexts'][0]['genres'] == report['api_contexts'][1]['genres']


def temporary_path(path):
    path = Path(path)
    return path.with_suffix(path.suffix+'.tmp')


def validate_output_paths(args):
    destinations = [Path(args.output), Path(args.output+'.diagnostics.json')]
    if args.catalog_output:
        destinations.append(Path(args.catalog_output))
    paths = [Path(args.probe_snapshot)]
    for destination in destinations:
        paths.extend([destination, temporary_path(destination)])
    resolved = [path.resolve() for path in paths]
    if len(resolved) != len(set(resolved)):
        raise RuntimeError('Source, evidence, catalog, diagnostics, and temporary paths must be distinct')


def atomic_write(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True,exist_ok=True)
    temporary = temporary_path(path)
    temporary.write_text(json.dumps(value,indent=2)+'\n')
    temporary.replace(path)


async def checked_gather(*tasks):
    # Independent fresh contexts; each context still sends API requests serially.
    results = await asyncio.gather(*tasks,return_exceptions=True)
    for result in results:
        if isinstance(result,BaseException):
            raise result
    return results


async def main(args):
    validate_output_paths(args)
    raw = Path(args.probe_snapshot).read_bytes()
    probe = json.loads(raw)
    if (not probe.get('allRequestedChecksSucceeded') or not probe.get('games') or probe.get('region') != 'US'
            or probe.get('marketplaceId') != 'ATVPDKIKX0DER'):
        raise RuntimeError('Successful US probe required')
    report = {'schema_version':1,'scope':SCOPE,'observed_at_utc':datetime.now(timezone.utc).isoformat(),
        'source_snapshot_sha256':hashlib.sha256(raw).hexdigest(), 'catalog_id_set_sha256':fingerprint(g['id'] for g in probe['games']),
        'catalog_count':len(probe['games']), 'browser_contexts':[], 'api_contexts':[],
        'exclusions':['Claims/PC giveaways','Account library and entitlements','Account-specific streaming eligibility'],
        'method':'Public route graph and all carousel slides in two fresh Chromium contexts; advertised genre filters, 36 paginated searches, every product detail and final browse rechecks in two anonymous Node contexts. Configured proxy and verified TLS.'}
    try:
        async with async_playwright() as pw:
            browser = await pw.chromium.launch(executable_path=args.chromium,headless=True,args=['--no-sandbox'],proxy={'server':os.environ['HTTPS_PROXY']})
            try:
                report['browser_contexts'] = await checked_gather(*(browser_context(browser,probe,index) for index in [1,2]))
            finally:
                await browser.close()
            report['api_contexts'] = await checked_gather(*(api_context(pw,probe,report['browser_contexts'][0]['genre_routes'],index) for index in [1,2]))
        successful = audit_passes(report,probe)
    except Exception as exc:
        report['failure_type'] = type(exc).__name__
        successful = False
    report.update({'completed_at_utc':datetime.now(timezone.utc).isoformat(),
                   'is_complete':successful, 'coverage_status':'Verified' if successful else 'Incomplete'})
    atomic_write(args.output if successful else args.output+'.diagnostics.json',report)
    if successful and args.catalog_output:
        probe.update({'isComplete':True,'coverageStatus':'Verified', 'coverageEvidence':{
            'scope':SCOPE,'sourceSnapshotSha256':report['source_snapshot_sha256'],
            'catalogIdSetSha256':report['catalog_id_set_sha256'], 'verifiedAt':report['completed_at_utc']}})
        atomic_write(args.catalog_output,probe)
    print('Public streaming audit '+('passed' if successful else 'failed'),flush=True)
    return 0 if successful else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--probe-snapshot',required=True)
    parser.add_argument('--output',required=True)
    parser.add_argument('--catalog-output')
    parser.add_argument('--chromium',default='/usr/bin/chromium')
    raise SystemExit(asyncio.run(main(parser.parse_args())))
