"""Repeat anonymous search, discovery, and game-page checks without bulk exports."""
import argparse
import asyncio
from collections import Counter
from datetime import datetime, timezone
import json
import os
from pathlib import Path
from urllib.parse import urlsplit

from playwright.async_api import async_playwright
from probe import CARDS, SCROLLER, Observation, catalog, close_context, fingerprint, walk


def presentation(node):
    value = node.get('presentationData', {})
    return json.loads(value) if isinstance(value, str) else value


def request_summary(entry):
    body = entry['request']
    search = body.get('searchContext', {})
    pagination = search.get('pagination', {})
    return {'path': entry['path'], 'status': entry['status'],
            'request_page_type': body.get('pageContext', {}).get('pageType'),
            'response_page_type': entry['response_page_type'],
            'query': search.get('query'), 'sort': search.get('sort'),
            'start_index': pagination.get('startIndex'), 'page_size': pagination.get('pageSize')}


async def wait_response(page, obs, start, predicate):
    for _ in range(180):
        for entry, data in reversed(obs.responses[start:]):
            if predicate(entry, data):
                if entry['status'] != 200:
                    raise RuntimeError('Catalog response was unsuccessful')
                return entry, data
        await page.wait_for_timeout(250)
    raise RuntimeError('Expected catalog response was not observed')


async def scroll(page, obs):
    trace = []
    stable = 0
    for _ in range(100):
        position = await page.evaluate(f"""() => {{const el=({SCROLLER})();
            return {{top:el.scrollTop,height:el.scrollHeight,client:el.clientHeight,
                at_bottom:el.scrollTop+el.clientHeight>=el.scrollHeight-2}};}}""")
        trace.append(position)
        stable = stable + 1 if position['at_bottom'] and len(trace)>1 and position==trace[-2] else 0
        if stable >= 3:
            await page.wait_for_timeout(2500)
            await obs.drain_catalog_requests()
            return trace
        await page.evaluate(f"() => {{const el=({SCROLLER})();el.scrollTop+=el.clientHeight*0.8;}}")
        await page.wait_for_timeout(700)
    raise RuntimeError('Scroll termination not verified')


async def search_view(page, obs, query, expected):
    before = len(obs.responses)
    failed_before = obs.blocked.copy()
    await page.get_by_role('textbox', name='Search', exact=True).fill(query)
    entry, data = await wait_response(page, obs, before,
        lambda e,d: e['request'].get('searchContext', {}).get('query') == query)
    initial = catalog(data)
    await page.wait_for_function("n => document.querySelectorAll('[data-test-id=game_tile]').length===n",
                                 arg=len(initial), timeout=20000)
    trace = await scroll(page, obs)
    matches = [(e,d) for e,d in obs.responses[before:]
        if e['request'].get('searchContext', {}).get('query') == query]
    games = {}
    for _, response in matches:
        games.update(catalog(response))
    cards = {c['id']:c for c in await page.locator('[data-test-id="game_tile"]').evaluate_all(CARDS)}
    result = {'query': query, 'api_count':len(games), 'api_id_set_sha256':fingerprint(games),
        'rendered_count':len(cards), 'rendered_id_set_sha256':fingerprint(cards),
        'api_rendered_id_difference':len(set(games)^set(cards)),
        'title_mismatches':sum(cards[k]['title']!=games[k]['title'] for k in set(cards)&set(games)),
        'outside_browse_count':len(set(games)-set(expected)),
        'probe_link_mismatches':sum(c['href'] != urlsplit(expected[k]['url']).path
            for k,c in cards.items() if k in expected),
        'scroll_trace':trace, 'requests':[request_summary(e) for e,_ in matches],
        'widget_types':dict(Counter(n['type'] for n in walk(data) if isinstance(n.get('type'),str))),
        'failed_catalog_requests':{k:v for k,v in (obs.blocked-failed_before).items() if k.startswith('proxy-prod.')},
        'recognizable_title_found': all(any(g['title']==title for g in games.values())
            for title in {'Alan Wake 2':['Alan Wake 2'], 'WRC Generations':['WRC Generations Fully Loaded Edition'],
                          'Batman':['Batman Caped Crusader - Chronicles'], 'Fortnite':['Fortnite']}.get(query,[]))}
    print(json.dumps({'context_check':'search','query':query,'count':len(games),
                      'outside_browse':result['outside_browse_count']}),flush=True)
    return result


async def discovery_view(page, obs, path, expected):
    before = len(obs.responses)
    failed_before = obs.blocked.copy()
    await page.goto('https://luna.amazon.com'+path, wait_until='domcontentloaded')
    await wait_response(page, obs, before, lambda e,d:
        e['response_page_type'] == ('landing_home_anonymous' if path=='/' else 'landing_game_night_anonymous'))
    await page.get_by_text('Top New Games' if path=='/' else 'Trending GameNight Games',exact=True).wait_for(timeout=30000)
    trace = await scroll(page,obs)
    # Discovery has carousels and promotional banners as well as ordinary tiles.
    # Compare every visible game-page link and all loaded tile identities.
    paths = await page.locator('a[href^="/game/"]').evaluate_all('es=>[...new Set(es.map(e=>e.getAttribute("href")))]')
    expected_paths = {urlsplit(g['url']).path for g in expected.values()}
    games = set()
    types = Counter()
    for entry, data in obs.responses[before:]:
        if entry['path'] not in ['/getPage','/getWidget']:
            continue
        for node in walk(data):
            kind = node.get('type')
            if isinstance(kind,str):
                types[kind]+=1
                pd = presentation(node)
                if isinstance(pd,dict) and isinstance(pd.get('gameId'),str):
                    games.add(pd['gameId'])
    result = {'path':path,'loaded_game_count':len(games),'loaded_id_set_sha256':fingerprint(games),
        'outside_browse_id_count':len(games-set(expected)), 'rendered_game_link_count':len(paths),
        'outside_browse_link_count':len(set(paths)-expected_paths), 'widget_types':dict(types),
        'scroll_trace':trace,'requests':[request_summary(e) for e,d in obs.responses[before:]],
        'failed_catalog_requests':{k:v for k,v in (obs.blocked-failed_before).items() if k.startswith('proxy-prod.')},
        'limitation':'Vertical scrolling and loaded carousels; does not exhaust every horizontal carousel or account-only view.'}
    print(json.dumps({'context_check':'discovery','path':path,'game_links':len(paths),
        'outside_browse_links':result['outside_browse_link_count']}),flush=True)
    return result


async def game_page(page, obs, game, check):
    before = len(obs.responses)
    failed_before = obs.blocked.copy()
    response = await page.goto(game['url'],wait_until='domcontentloaded')
    entry,data = await wait_response(page,obs,before,lambda e,d:
        e['response_page_type']=='game_detail_anonymous' and any(
            n.get('type')=='PRODUCT_DETAIL' and presentation(n).get('productId')==game['id'] for n in walk(d)))
    await page.get_by_text(check['primaryButtonText'],exact=True).first.wait_for(timeout=20000)
    await page.get_by_text(game['title'],exact=True).first.wait_for(timeout=20000)
    await obs.drain_catalog_requests()
    product = next(presentation(n) for n in walk(data) if n.get('type')=='PRODUCT_DETAIL')
    canonical = next(presentation(n).get('canonicalUrl') for n in walk(data) if n.get('type')=='SEO_DETAIL')
    types = Counter(n['type'] for n in walk(data) if isinstance(n.get('type'),str))
    result = {'title':game['title'],'id_namespace':'.'.join(game['id'].split('.')[:3]),
        'slug_only_action':len(urlsplit(game['url']).path.strip('/').split('/'))==2,
        'document_http_status':response.status if response else None,'detail_http_status':entry['status'],
        'identity_matches':product.get('productId')==game['id'] and product.get('asin')==game['asin'],
        'canonical_url_matches':canonical==game['url'],'rendered_url_matches':page.url==game['url'],
        'rendered_title_matches':await page.get_by_text(game['title'],exact=True).count()>0,
        'rendered_primary_button':check['primaryButtonText'],'widget_types':dict(types),
        'failed_catalog_requests':{k:v for k,v in (obs.blocked-failed_before).items() if k.startswith('proxy-prod.')}}
    print(json.dumps({'context_check':'game_page','title':game['title'],'identity_matches':result['identity_matches']}),flush=True)
    return result


async def investigate(browser, probe, index, report):
    context = await browser.new_context(locale='en-US',service_workers='block',viewport={'width':1365,'height':900})
    obs = Observation(context)
    await context.route('**/*',obs.forward)
    page = await context.new_page()
    expected = {g['id']:g for g in probe['games']}
    result = {'context':index,'search':[],'discovery':[],'game_pages':[]}
    report['contexts'].append(result)
    try:
        await page.goto('https://luna.amazon.com/search',wait_until='domcontentloaded')
        await wait_response(page,obs,0,lambda e,d:e['request'].get('searchContext',{}).get('query')=='')
        for query in ['a','e','Alan Wake 2','WRC Generations','Batman','Fortnite','Ubisoft','GOG']:
            result['search'].append(await search_view(page,obs,query,expected))
        for path in ['/','/gamenight']:
            result['discovery'].append(await discovery_view(page,obs,path,expected))
        if len(probe['detailChecks'])!=5:
            raise RuntimeError('Five successful probe detail checks are required')
        for check in probe['detailChecks']:
            result['game_pages'].append(await game_page(page,obs,expected[check['requestedId']],check))
        result['config']=obs.config
    finally:
        await close_context(context)


def failed(report):
    if len(report['contexts']) != 2:
        return True
    for context in report['contexts']:
        if len(context['search']) != 8 or len(context['discovery']) != 2 or len(context['game_pages']) != 5:
            return True
        if context.get('config') != {'defaultLocale':'en_US','marketplaceId':'ATVPDKIKX0DER',
                                   'clientProxy':'https://proxy-prod.us-east-1.tempo.digital.a2z.com'}:
            return True
        for s in context['search']:
            if any(s[k] for k in ['api_rendered_id_difference','title_mismatches','outside_browse_count',
                                  'probe_link_mismatches','failed_catalog_requests']) or not s['recognizable_title_found']:
                return True
        for d in context['discovery']:
            if d['outside_browse_id_count'] or d['outside_browse_link_count'] or d['failed_catalog_requests']:
                return True
        for g in context['game_pages']:
            if g['document_http_status'] != 200 or g['detail_http_status'] != 200 or any(
                    not g[k] for k in ['identity_matches','canonical_url_matches','rendered_url_matches','rendered_title_matches']) or g['failed_catalog_requests']:
                return True
    return False


async def main(args):
    probe = json.loads(Path(args.probe_snapshot).read_text())
    if not probe['allRequestedChecksSucceeded']:
        raise ValueError('A successful live probe including detail checks is required')
    report = {'observed_at_utc':datetime.now(timezone.utc).isoformat(), 'probe_retrieved_at':probe['retrievedAt'],
        'method':'Chromium DOM; configured proxy and verified Playwright Node TLS forwarding',
        'contexts':[], 'is_complete':False,'coverage_status':'Unverified',
        'limitations':['Finite search samples and loaded discovery groups do not establish universal catalog coverage.',
                       'Account entitlement and streaming eligibility are not tested.']}
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path=args.chromium,headless=True,args=['--no-sandbox'],
            proxy={'server':os.environ['HTTPS_PROXY']})
        report['browser_version']=browser.version
        try:
            for index in range(1,3):
                await investigate(browser,probe,index,report)
            unsuccessful = failed(report)
        except Exception as exc:
            report['failure_type']=type(exc).__name__
            unsuccessful=True
        finally:
            await browser.close()
    if unsuccessful:
        report['coverage_status']='Incomplete'
    destination = Path(args.output + ('.diagnostics.json' if unsuccessful else ''))
    destination.parent.mkdir(parents=True,exist_ok=True)
    temporary = destination.with_suffix(destination.suffix+'.tmp')
    temporary.write_text(json.dumps(report,indent=2)+'\n')
    temporary.replace(destination)
    print('Wrote '+str(destination),flush=True)
    return 1 if unsuccessful else 2


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--probe-snapshot',required=True)
    parser.add_argument('--output',required=True)
    parser.add_argument('--chromium',default='/usr/bin/chromium')
    raise SystemExit(asyncio.run(main(parser.parse_args())))
