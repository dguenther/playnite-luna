"""Sanitized anonymous website comparison; requires Python Playwright + Chromium.

No HAR, storage state, raw response, credentials, or catalog export is saved.
Requests retain the configured proxy and TLS verification. Chromium's native
certificate trust is tested separately and is not repaired by forwarding.
"""
import argparse
import asyncio
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
from urllib.parse import urlsplit

from playwright.async_api import async_playwright


def fingerprint(ids):
    return hashlib.sha256('\n'.join(sorted(ids)).encode()).hexdigest()


def walk(value):
    if isinstance(value, dict):
        yield value
        for child in value.values():
            yield from walk(child)
    elif isinstance(value, list):
        for child in value:
            yield from walk(child)


def catalog(data):
    widgets = data.get('pageMemberGroups', {}).get('mainContent', {}).get('widgets', [])
    games = {}
    for node in walk(widgets):
        if node.get('type') != 'GAME_TILE':
            continue
        pd = node.get('presentationData', {})
        if isinstance(pd, str):
            pd = json.loads(pd)
        if pd.get('gameId'):
            games[pd['gameId']] = pd
    return games


def summary(games):
    values = list(games.values())
    return {'count': len(games), 'id_set_sha256': fingerprint(games),
            'first': values[0].get('title') if values else None,
            'last': values[-1].get('title') if values else None}


SCROLLER = """() => {
 const card=document.querySelector('[data-test-id="game_tile"]');
 let el=card;
 while(el && !(el.scrollHeight>el.clientHeight &&
   ['auto','scroll'].includes(getComputedStyle(el).overflowY))) el=el.parentElement;
 return el || document.scrollingElement;
}"""
CARDS = """els => els.map(e => ({id:e.id.replace(/^game_tile_/,''),
 title:e.querySelector('img')?.alt,
 href:e.closest('a')?.getAttribute('href')}))"""


async def close_context(context):
    # Waiting for background telemetry route handlers can hang cleanup.
    # Catalog requests are drained separately before evaluating the evidence.
    await context.unroute_all(behavior='ignoreErrors')
    await context.close()


class Observation:
    def __init__(self, context):
        self.context = context
        self.calls = []
        self.responses = []  # In memory only; never serialized.
        self.blocked = Counter()
        self.config = {}
        self.pending_catalog_requests = 0

    async def drain_catalog_requests(self):
        for _ in range(300):
            if not self.pending_catalog_requests:
                return
            await asyncio.sleep(0.1)
        raise RuntimeError('Catalog requests did not finish within 30 seconds')

    async def forward(self, route):
        req = route.request
        parsed = urlsplit(req.url)
        is_catalog = parsed.path in ['/getPage', '/getWidget', '/getAppConfig']
        if is_catalog:
            self.pending_catalog_requests += 1
        try:
            response = await self.context.request.fetch(req, timeout=30000, max_redirects=0)
            if response.status >= 400:
                self.blocked[f'{parsed.hostname}{parsed.path} HTTP {response.status}'] += 1
            if parsed.path in ['/getPage', '/getWidget', '/getAppConfig']:
                body = json.loads(req.post_data or '{}')
                data = await response.json()
                headers = await req.all_headers()
                entry = {'host': parsed.hostname, 'path': parsed.path, 'status': response.status,
                         'headers': {k: headers[k] for k in ['content-type', 'origin', 'referer',
                            'x-amz-locale', 'x-amz-marketplace-id', 'x-amz-platform',
                            'x-amz-device-type', 'accept-language'] if k in headers},
                         'request': {k: body[k] for k in ['timeout', 'featureScheme', 'searchContext',
                            'pageContext', 'inputContext', 'dynamicFeatures', 'widgetId'] if k in body},
                         'response_page_type': data.get('pageContext', {}).get('pageType')}
                # Only public static browser metadata; omit client referrers/markers.
                if 'browserMetadata' in body.get('clientContext', {}):
                    entry['request']['clientContext'] = {
                        'browserMetadata': {k:v for k,v in body['clientContext']['browserMetadata'].items()
                            if k in ['browserClientRole','browserType','browserVersion','deviceModel',
                                     'deviceType','osName','osVersion']}}
                if 'referer' in entry['headers']:
                    ref = urlsplit(entry['headers']['referer'])
                    entry['headers']['referer'] = f'{ref.scheme}://{ref.netloc}{ref.path}'
                self.calls.append(entry)
                self.responses.append((entry, data))
                if parsed.path == '/getAppConfig':
                    self.config = {k: data.get(k) for k in ['defaultLocale', 'marketplaceId']}
                    self.config['clientProxy'] = data.get('endpoints', {}).get('clientProxy')
            await route.fulfill(response=response)
        except Exception as exc:
            self.blocked[f'{parsed.hostname}{parsed.path} {type(exc).__name__}'] += 1
            try:
                await route.abort()
            except Exception:
                pass
        finally:
            if is_catalog:
                self.pending_catalog_requests -= 1

    def latest(self, query):
        for entry, data in reversed(self.responses):
            if entry['request'].get('searchContext', {}).get('query') == query:
                return entry, data
        raise RuntimeError(f'No browser response observed for {query}')


async def scroll_view(page, observation, query, expected):
    await page.locator('[data-test-id="game_tile"]').first.wait_for(timeout=20000)
    await page.wait_for_timeout(1200)
    entry, data = observation.latest(query)
    games = catalog(data)
    before = len(observation.calls)
    before_blocked = observation.blocked.copy()
    cards = {}
    trace = []
    stable = 0
    for _ in range(100):
        for card in await page.locator('[data-test-id="game_tile"]').evaluate_all(CARDS):
            cards[card['id']] = card
        position = await page.evaluate(f"""() => {{const el=({SCROLLER})();
          return {{top:el.scrollTop,height:el.scrollHeight,client:el.clientHeight,
            at_bottom:el.scrollTop+el.clientHeight>=el.scrollHeight-2}};}}""")
        trace.append({**position, 'rendered_ids': len(cards)})
        if position['at_bottom'] and len(trace)>1 and trace[-1] == trace[-2]:
            stable += 1
        else:
            stable = 0
        if stable >= 3:
            break
        await page.evaluate(f"() => {{const el=({SCROLLER})(); el.scrollTop+=el.clientHeight*0.8;}}")
        await page.wait_for_timeout(700)
    else:
        raise RuntimeError('Scroll safety limit reached; termination not verified')
    await page.wait_for_timeout(2500)
    await observation.drain_catalog_requests()
    expected_ids = set(expected)
    observed_ids = set(games)
    rendered_ids = set(cards)
    result = {'query': query, 'api': summary(games), 'rendered_count': len(cards),
              'rendered_id_set_sha256': fingerprint(cards),
              'api_minus_rendered': len(observed_ids-rendered_ids),
              'rendered_minus_api': len(rendered_ids-observed_ids),
              'probe_minus_api': len(expected_ids-observed_ids),
              'api_minus_probe': len(observed_ids-expected_ids),
              'title_mismatches': sum(cards[k]['title'] != games[k]['title'] for k in rendered_ids & observed_ids),
              'missing_game_links': sum(not (v['href'] or '').startswith('/game/') for v in cards.values()),
              'probe_link_mismatches': sum(cards[k]['href'] != urlsplit(expected[k]['url']).path
                    for k in rendered_ids & expected_ids),
              'widget_types': dict(Counter(n['type'] for n in walk(data.get('pageMemberGroups', {}).get('mainContent', {}).get('widgets', []))
                    if isinstance(n.get('type'), str))),
              'scroll_trace': trace,
              'catalog_calls_during_scroll': [
                    {k:c[k] for k in ['path','status','response_page_type']} |
                    {'search_query':c['request'].get('searchContext', {}).get('query'),
                     'request_page_type':c['request'].get('pageContext', {}).get('pageType')}
                    for c in observation.calls[before:] if c['path'] in ['/getPage', '/getWidget']],
              'failed_requests_during_scroll':dict(observation.blocked-before_blocked),
              'request': entry}
    print(json.dumps({'query': query, 'api_count': len(games), 'rendered_count':len(cards),
                      'probe_difference':result['probe_minus_api']+result['api_minus_probe'],
                      'scroll_calls':len(result['catalog_calls_during_scroll'])}), flush=True)
    return result, games


async def investigate(browser, probe, index):
    context = await browser.new_context(locale='en-US', service_workers='block',
                                         viewport={'width':1365, 'height':900})
    obs = Observation(context)
    await context.route('**/*', obs.forward)
    page = await context.new_page()
    views = []
    sets = {}
    try:
        await page.goto('https://luna.amazon.com/browse', wait_until='domcontentloaded')
        await page.get_by_text('Please login to see your games', exact=True).wait_for()
        default = {'quick_search':'my_games', 'message':'Please login to see your games',
                   'rendered_game_count':await page.locator('[data-test-id="game_tile"]').count()}
        # Exercise real tab selection, including deselection to the unfiltered catalog.
        for query, name in [('included_with_prime','Standard'), ('all_games','AllGames'),
                            ('luna_premium','Premium'), ('game_night','GameNight')]:
            await page.locator('#item_browse_pill_filter_'+
                  {'included_with_prime':'included_with_prime','all_games':'included_with_prime',
                   'luna_premium':'luna_premium','game_night':'game_night_games'}[query]).click()
            await page.wait_for_url('**quick_search='+query)
            await page.wait_for_timeout(1500)
            result, games = await scroll_view(page, obs, query,
                                  {g['id']:g for g in next(q for q in probe['queries'] if q['name']==name)['games']})
            views.append(result)
            sets[name] = set(games)
        await page.get_by_role('button', name='Filters', exact=True).click()
        await page.wait_for_timeout(500)
        filter_text = await page.locator('body').inner_text()
        subscriptions = filter_text.split('Subscription\n',1)[1].split('\nAge Rating',1)[0].splitlines()
        await page.keyboard.press('Escape')
        await obs.drain_catalog_requests()
        return {'context':index,'default_view':default,'config':obs.config,'views':views,
                'subscriptions_in_filter_menu':subscriptions,
                'overlaps':{a+' & '+b:len(sets[a]&sets[b]) for a,b in
                            [('Standard','Premium'),('Standard','GameNight'),('Premium','GameNight')]},
                'all_minus_category_union':len(sets['AllGames']-set.union(*(sets[k] for k in ['Standard','Premium','GameNight']))),
                'category_union_minus_all':len(set.union(*(sets[k] for k in ['Standard','Premium','GameNight']))-sets['AllGames']),
                'blocked_requests':dict(obs.blocked)}
    finally:
        await close_context(context)


async def main(args):
    probe = json.loads(Path(args.probe_snapshot).read_text())
    report = {'observed_at_utc':datetime.now(timezone.utc).isoformat(),
              'method':'Chromium DOM with Playwright Node APIRequestContext forwarding; configured HTTPS_PROXY and CA; TLS verification enabled',
              'probe_retrieved_at':probe['retrievedAt'], 'contexts':[],
              'is_complete':False, 'coverage_status':'Unverified'}
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(executable_path=args.chromium, headless=True,
                    args=['--no-sandbox'], proxy={'server':os.environ['HTTPS_PROXY']})
        report['browser_version'] = browser.version
        native = await browser.new_context()
        try:
            page = await native.new_page()
            try:
                await page.goto('https://luna.amazon.com/browse', timeout=20000)
                report['native_navigation'] = 'succeeded'
            except Exception as exc:
                report['native_navigation'] = 'ERR_CERT_AUTHORITY_INVALID' if 'ERR_CERT_AUTHORITY_INVALID' in str(exc) else type(exc).__name__
        finally:
            await native.close()
        try:
            for index in range(1,3):
                report['contexts'].append(await investigate(browser,probe,index))
        except Exception as exc:
            # Preserve earlier evidence; save only a sanitized failure type.
            report['failure_type'] = type(exc).__name__
            report['coverage_status'] = 'Incomplete'
            destination = Path(args.output + '.diagnostics.json')
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(json.dumps(report,indent=2)+'\n')
            print('Browser comparison failed: '+type(exc).__name__)
            return 1
        finally:
            await browser.close()
    failed = any(v['api_minus_rendered'] or v['rendered_minus_api'] or
                 v['probe_minus_api'] or v['api_minus_probe'] or
                 v['title_mismatches'] or v['missing_game_links'] or
                 v['probe_link_mismatches'] or
                 any(k.startswith('proxy-prod.') for k in v['failed_requests_during_scroll']) or
                 any(c['response_page_type'] != 'game_detail_anonymous'
                     for c in v['catalog_calls_during_scroll'])
                 for context in report['contexts'] for v in context['views'])
    if failed:
        report['coverage_status'] = 'Incomplete'
    destination = Path(args.output + ('.diagnostics.json' if failed else ''))
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(destination.suffix+'.tmp')
    temporary.write_text(json.dumps(report,indent=2)+'\n')
    temporary.replace(destination)
    return 1 if failed else 2


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--probe-snapshot', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--chromium', default='/usr/bin/chromium')
    raise SystemExit(asyncio.run(main(parser.parse_args())))
