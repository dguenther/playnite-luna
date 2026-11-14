# Luna catalog API investigation

## Status

Validated October 11, 2026 (UTC) using anonymous requests from the managed Linux environment. Earlier n8n workflow and fetch-notes raw URLs returned HTTP 404. Anonymous first-party requests nevertheless produced working responses.

**The October 11 audit supports completeness for the public US streaming catalog.**
The full audit covers all 13 discovered streaming pages, every carousel, all 26
advertised genre filters, 36 paginated searches, and every one of the 118 main
product details in two fresh contexts. No omissions or extra streaming IDs were
found. Claims/PC giveaways and account-specific inventories/entitlements are
excluded. See [the completeness verdict and export contract](streaming-completeness.md).
The October 11 snapshot was fully audited and exported as `Verified` /
`isComplete: true`, with a scoped, hash-bound research receipt. The current C#
research CLI defaults to unverified exports. The production browser downloads
API data through C# at runtime and validates the established browse contract on
each refresh; it does not require a full Python audit per download. C# tests use
reduced representative API samples, not a complete saved response or catalog.

## Earlier October 11 sampled search and discovery evidence

These browser samples preceded the full streaming audit above. They explain
frontend behavior and representative rendered action pages; the broader coverage
verdict uses the subsequent full audit.

The final browse tool completed both contexts after repairing a cleanup hang.
Catalog requests must finish before evidence is evaluated; late background
telemetry cannot keep context cleanup waiting indefinitely. All eight grid
comparisons matched IDs, titles, and probe action links. The latest
[browse capture](evidence/2026-10-11-anonymous-browser.json) replaces the earlier
capture with this exact live rerun; no browse continuation was observed.

The [discovery tool](../tools/LunaBrowserEvidence/README.md) checked `a`, `e`,
`Alan Wake 2`, `WRC Generations`, `Batman`, `Fortnite`, `Ubisoft`, and `GOG` in
two fresh contexts. Counts were 49/39/8/2/2/7/4/1. API/rendered IDs, titles, and
probe links matched in every view; no search ID fell outside All Games. These
are finite search samples, not a proof that every search page has been fetched.

Home loaded 36 unique tile IDs and rendered 38 distinct game links; GameNight
loaded 50 IDs and rendered 51 links. All were in the 118-game All set. Discovery
uses `GAME_TILE_VERTICAL`, featured products, carousels, and party packs as well
as promotional widgets. Tetris® Effect: Connected appears as an additional
GameNight landing link despite lacking filtered GameNight membership. Marketing
placement must not redefine the query-derived memberships. The WRC edition
remains a catalog game; its edition name is not grounds for exclusion.

All five representative action pages returned document/detail HTTP 200 in both
contexts with matching ID/ASIN/canonical URL, visible title, and expected primary
label. Alien's detail page contained eight `ADD_ON_TILE` widgets, separate from
its main `PRODUCT_DETAIL` and `GAME_TILE_VERTICAL` recommendations. No stream or
account login was attempted. The [sanitized search/discovery evidence](evidence/2026-10-11-search-discovery.json)
records these checks, the US CLI summary, and the separate Claims attempt.

Claims HTML returned 200, but JS/CSS required from `d2u4zldaqlyj2w.cloudfront.net`
and, in the second context, `d27meqdi87hhw.cloudfront.net` was denied by the
current destination policy with HTTP 403. The page stayed blank and issued no
catalog request. PC-platform/giveaway classification was not verified; no wider
allowance or routing bypass was used. Downloadable giveaways remain outside MVP.
Per the clarified streaming-only scope, this historical Claims attempt is not a
coverage blocker and requires no further asset investigation.

## Working request

`POST https://proxy-prod.us-east-1.tempo.digital.a2z.com/getPage`

The anonymous frontend's `getAppConfig` selected this US endpoint, locale `en_US`, and marketplace `ATVPDKIKX0DER`. The provider now follows that observed endpoint and the explicit `all_games` query. Earlier research used `proxy-prod.eu-west-1.tempo.digital.a2z.com` and an empty query; it returned the same four browse sets but different anonymous detail-button labels. Endpoint location alone is not proof of catalog region.

No Cookie or Authorization header was sent. The CLI disables the cookie jar so requests remain anonymous even if a response sets cookies. The tested headers were:

```text
Content-Type: text/plain; charset=utf-8
Origin: https://luna.amazon.com
Referer: https://luna.amazon.com/
x-amz-locale: en_US
x-amz-marketplace-id: ATVPDKIKX0DER
x-amz-platform: web
x-amz-device-type: browser
Accept-Language: en-US
User-Agent: actual browser user agent
```

This is an observed working combination, not a claim that every header is required. An earlier anonymous experiment at the EU endpoint omitted `timeout`, `clientContext`, `inputContext`, and `dynamicFeatures` and returned the same 118 IDs using only `searchContext`, `featureScheme`, and `pageContext`. The provider retains those fields for compatibility with detail verification. Captured browser headers match the locale/marketplace/platform fields above. Browser metadata identifies `Chrome Headless` / `154.0.0.0` / Linux `0`, and includes null client referrer/marker fields that the provider omits. Cookie-free US browse retrieval succeeded without those extra fields. Header minimization remains open.

```json
{
  "timeout": 10000,
  "searchContext": {
    "query": "all_games",
    "sort": "TITLE_A_TO_Z",
    "filtersList": []
  },
  "featureScheme": "WEB_V1",
  "pageContext": {
    "pageType": "multistate_browse_results",
    "pageId": "default"
  },
  "clientContext": {
    "browserMetadata": {
      "browserClientRole": "browser",
      "browserType": "Chrome",
      "browserVersion": "154.0.8037.57",
      "deviceModel": "unknown",
      "deviceType": "unknown",
      "osName": "Linux",
      "osVersion": "unknown"
    }
  },
  "inputContext": { "gamepadTypes": [] },
  "dynamicFeatures": []
}
```

The current provider selects Windows or Linux OS metadata at runtime. The first exploratory Standard request used older browser metadata; subsequent category requests and the C# probe use the installed Chromium version.

## Queries and observed results

| Membership | `searchContext.query` | Response page type | Unique games observed |
| --- | --- | --- | ---: |
| All Games | `all_games` (earlier: empty string) | `browse_results_all_games` | 118 |
| Standard | `included_with_prime` | `browse_results_included_with_prime` | 90 |
| Premium | `luna_premium` | `browse_results_luna_premium` | 118 |
| GameNight | `game_night` | `browse_results_game_night` | 50 |

The three category sets have a union of 118 native game IDs, equal to the All Games set in both directions. Standard's 90 IDs are a subset of Premium in this observation. This overlap is preserved; Premium membership is not modeled as exclusive or as an account entitlement.

Every observed tile had a native game ID, a relative Luna game-page action, and a product URL with `territory=US`. The frontend configuration and captured requests independently advertised `en_US` / `ATVPDKIKX0DER`, and the rendered grids matched the requested US sets. This supports US marketplace interpretation; other discovery/search coverage comes from the full audit. These fields do not establish account entitlement or streaming eligibility, and the endpoint's geographic name alone does not establish catalog region.

Alphabetical boundaries observed:

| Query | First title | Last title |
| --- | --- | --- |
| All Games / Standard / Premium | Alan Wake 2 | WRC Generations Fully Loaded Edition |
| GameNight | Angry Birds Flock Party | Worms W.M.D |

Counts are repeated observations from this investigation, not guaranteed future totals.

## Rendered anonymous browse evidence

The runtime reported current observations, enforced network policy, and the newly allowed `proxy-prod.tempo.digital.a2z.com` host. `/etc/codex/network-policy.json` agreed; VPN was not configured. `getAppConfig` and `getWidget` became accessible. The earlier sign-in redirect did not recur. `/browse` selected `quick_search=my_games`, displayed **"Please login to see your games"**, and rendered zero personal games. This is a personal-library state, not an empty public catalog.

Selecting Luna Standard produced public cards without signing in. Selecting the same pill again deselected it and sent `searchContext.query = "all_games"`; the route became `/browse?quick_search=all_games`. Premium and GameNight sent the known `luna_premium` and `game_night` queries. All four requests used `multistate_browse_results`, `pageId: default`, `TITLE_A_TO_Z`, and an empty `filtersList`.

In **two fresh anonymous contexts**, every rendered card's native ID and image title matched the corresponding browser API set and the C# probe set. Each card had a `/game/` link. Counts were 118 / 90 / 118 / 50, with no set differences; no independent numeric total label was visible. The Standard/Premium intersection was 90; Standard/GameNight and Premium/GameNight were both 50. The filter menu's Subscription group listed only Luna Standard and Luna Premium. This does not prove no other discovery groups exist.

At a 1365×900 viewport, the scroll container reached stable bottom positions of 2450/3350 pixels for Standard, 3320/4220 for All Games/Premium, and 1292/2192 for GameNight (scrollTop/scrollHeight; clientHeight 900). The complete card set was already present in the DOM at the first measurement. Each view required three repeated stable-bottom measurements plus a final 2.5-second observation period. Additional calls while scrolling returned `game_detail_anonymous`: 3 / 5 / 5 / 2 for Standard / All / Premium / GameNight in each context. No additional browse-results request or continuation was observed.

[Sanitized evidence](evidence/2026-10-11-anonymous-browser.json) retains counts, set hashes, boundary titles, scroll traces, request fields, and blocked destination summaries. [The browser probe](../tools/LunaBrowserEvidence/README.md) makes the checks reproducible. Browser requests were forwarded through Playwright's Node HTTPS transport with the inherited proxy and configured CA, **retaining TLS verification**. Native Chromium still reported `ERR_CERT_AUTHORITY_INVALID`; forwarding did not fix its trust store. No home/profile certificate database was changed. Guest state remained in memory; no cookies, account authentication headers, session/service tokens, raw responses, or bulk game lists are committed.

Telemetry/advertising requests to Sentry, `s.amazon-adsystem.com`, the metrics S3 host, and `unagi-na.amazon.com` received proxy HTTP 403s while all four public grids rendered. They were not needed for these checks; no wider host allowance was requested.

## Response structure and identity

Responses contain nested `pageMemberGroups.mainContent.widgets` collections with `type: "GAME_TILE"` children. The parser traverses objects and arrays rather than assuming a fixed nesting depth.

`presentationData` is a **JSON-encoded string** in the recorded responses. It includes `gameId`, `title`, `releaseYear`, `imagePortrait`, `imageLandscape`, `genreTags`, and `hoverDetails`. The parser also accepts an object representation for defensive compatibility.

Observed `gameId` values use both `amzn1.adg.product.<uuid>` and `amzn1.luna.product.<uuid>`. The latter appears on eight games in the captured All Games response. Most tiles have an action target such as `/game/alan-wake-2/B0GFWDM8NP`. Fortnite instead uses `/game/fortnite`; its ASIN is present in `hoverDetails.productUrl`. The parser retains the actual Luna page action and derives an ASIN from the path or that first-party product URL. Some product URLs point to `play.amazon.com`; those are not substituted for the Luna page action. Reduced recorded regression samples for these two cases are in `tests/Fixtures/EdgeCases.json`.

Canonical identity is `(region, gameId)`, falling back to the verified ASIN if the native ID is absent. Both identifiers are retained. Repeated tiles and overlapping memberships merge by identity, never by title. Only tiles under the direct `pageMemberGroups.mainContent.widgets` array are collected. Tiles missing a title, safe Luna action, or first-party `play.amazon.com/play` link invalidate their query. The play-link ASIN must match any ASIN in the Luna action and exactly one `territory` parameter must be present and match the requested region. Conflicting product ID/ASIN mappings within or across queries invalidate the snapshot; incomplete parse results remain diagnostic observations.

All observed browse tiles have matching rendered game cards, native IDs, titles, and game-page links. The full audit checks every main product, all 36 paginated search queries, advertised discovery routes, genres, and recommendation IDs against All. Recommendations/DLC remain distinct from main products. Claims is outside the streaming scope. Advertised editions must not be discarded solely because their titles contain edition names.

## Additional anonymous detail checks

The same endpoint accepts `pageContext.pageType = "game_detail"`, `pageContext.pageId = <native product ID>`, `encryptPageId = false`, and `productStage = "Release"`. Omit `searchContext`; the other client/input/feature fields and headers remain as above. This navigation context was read from the catalog tile's action. No service token, cookie, or account authentication was needed in the reproduced request.

The earlier EU-endpoint investigation returned HTTP 200 for five representative requests with response page type `game_detail_anonymous`. Each main `PRODUCT_DETAIL` had a product ID and ASIN matching the catalog tile, and `SEO_DETAIL.canonicalUrl` matched its Luna action. Earlier GET requests to the five actual Luna action URLs also returned HTTP 200 without redirecting elsewhere.

| Sample | Purpose |
| --- | --- |
| Alan Wake 2 | Standard / Premium overlap |
| Alien: Isolation | Premium outside the observed Standard set |
| Angry Birds Flock Party | GameNight membership |
| Batman Caped Crusader - Chronicles | `amzn1.luna.product` namespace |
| Fortnite | Slug-only Luna action |

The detail response distinguishes the main product from recommendation cards (`GAME_TILE_VERTICAL`) and DLC (`ADD_ON_TILE`). The earlier five-page sample introduced no recommendation IDs outside All. The full audit subsequently validated every one of the 118 main products twice and checked all 111 distinct recommendation IDs, all inside All. These detail checks support the broader scoped verdict alongside route, genre, and search evidence.

The earlier EU-endpoint primary buttons reported **"Not Available"**. The earlier US continuation passed four checks before Batman returned 503 and the outbound tunnel failed. On October 11, all five US CLI checks passed; both browser contexts rendered Alan Wake 2, Angry Birds Flock Party, Batman, and Fortnite with **"Play free with Prime"**, and Alien: Isolation with **"Included in Luna Premium"**. The endpoint-dependent labels explain why old EU observations must not be treated as US removals or global unavailability. They do not prove individual entitlement or streaming eligibility.

`--live --verify-details` makes up to five distinct sample requests after the four browse queries pass, records identity/canonical URL checks and button text in `detailChecks`, and keeps `isComplete: false`. Any requested detail check that fails prevents replacement of the output; diagnostics are written separately. Reduced Alien and Fortnite detail fixtures cover main product identity, recommendations/DLC, canonical links, and the slug-only case.

## Pagination and completeness

No continuation token, next-page cursor, `hasMore`, pagination object, or total-count field was observed in the four browse responses. Each request returned a single collection extending from the first to the last title listed above. The two-context rendered scroll checks now independently confirm that those four grids reached stable bottoms without further browse-results calls.

Earlier All Games experiments set `searchContext.pagination` to `{ "pageSize": 12, "startIndex": 0 }` and then start index 12. Both returned the same 118 identifiers. `TITLE_Z_TO_A` returned the same set in reverse title order. The inspected frontend browse grid slices an already fetched widget array for scrolling. These experiments support a single-response grid; live scrolling is separately verified by the two-context browse capture.

Query-union equality alone could share a server limit. The rendered comparison
establishes the advertised browse grids; the subsequent full streaming audit
adds the route graph, every carousel, advertised genres, a broad paginated search
sweep, every main product, and unchanged final All rechecks. Together these
support the [scoped public-catalog verdict](streaming-completeness.md).

Search uses a distinct `multistate_search_results` request, `sort: RELEVANCE`, and
`pagination: {startIndex: 0, pageSize: 50}`. The empty-query response is
`search_results_no_query_jumanji`. Nonempty browser searches and scrolling now
work, but scrolling `a` stops at 49 cards without another search request.

The optional [offset experiment](evidence/2026-10-11-search-offsets.json) manually
varied that observed index field in two independent anonymous request contexts.
Indices 0/50/100 returned 49/33/20 tiles. Index 50 introduced 32 IDs in each
recorded context; all were already in All. Index 100 was
`search_results_empty_jumanji`, with the same 20 recommended IDs as the
empty-query landing view. Those are not query matches. Relevance ordering and
set hashes varied between calls, and the offset union included recommendations.
Those initial experiments did not establish a stable traversal or termination protocol.
“Fewer than 50” and a stable scroll bottom cannot establish search completion.

### Resolution: alphabetical offsets and an explicit empty-search state

The [public frontend source evidence](evidence/2026-10-11-search-frontend.json)
explains the UI behavior. The search handler requests index 0 / size 50 and
chooses `RELEVANCE`. `VirtualInfiniteScrollableWidgets.handleLoadMore` only
updates the local page counter; `getWidgets` slices the fetched array. Reaching
the UI bottom therefore says nothing about the backend's remaining matches.

The revised [offset tool](../tools/LunaBrowserEvidence/search_offsets.py) uses
`TITLE_A_TO_Z`, advances by the **requested** page size, and continues even after
a short page. It accepts termination only when both
`pageContext.pageType == search_results_empty_jumanji` and a
`SEARCH_NO_RESULTS_FOUND` widget in main content are present. Its `GAME_TILE`
recommendations count as zero query matches. Unknown states, a safety-limit
exhaustion, unstable repeated pages, overlaps, or reference disagreement fail
validation and preserve prior evidence.

The [final capture](evidence/2026-10-11-stable-search-pagination.json) records two
independent anonymous request contexts, with two traversals per query:

| Query | Index 0 | Index 50 | Index 100 | Unique matches |
| --- | --- | --- | --- | --- |
| `a` | 50 matches | 32 matches | Explicit no-results; 20 recommendations | 82 |
| `e` | 39 matches | Explicit no-results; 20 recommendations | Not needed | 39 |

Repeated alphabetical pages have identical ID sets and ordering, including across
contexts, with no page overlaps or IDs outside All. Each traversal's union equals
single requests with page sizes 100 and 200 and a reverse-title request of size
100. Repeated relevance requests still change first-page sets. Thus the observed
ambiguity is resolved for these samples without interpreting relevance offsets
as a stable traversal. This is an observed undocumented protocol, not a guarantee
for every future query or account state.

The subsequent [full streaming audit](evidence/2026-10-11-public-streaming-audit.json)
applies this traversal to all 36 letter/digit queries in two API contexts, with
reverse-title references of size 200. Each search union reproduces all 118 IDs;
page sets/order match across contexts. The smaller offset tool still checks only
`a` and `e` and does not itself certify full catalog coverage.

Catalog ingestion continues to use the four browse queries; Playnite search
filters that normalized snapshot locally. Remote search
is a coverage cross-check. No search pagination was added to the C# catalog
provider, and no browse continuation was warranted by actual browse requests.

The parser flags recognized continuation markers as `UnresolvedContinuation`. It deliberately does not fabricate a pagination request protocol. A future observed protocol must be implemented and tested before such responses can be validated. Absence of markers is reported as `NoContinuationObserved`, never "all pages verified."

The unaudited C# snapshot always exposes `isComplete: false`. Usable research
reports have `coverageStatus: "Unverified"`; failed validation produces
`"Incomplete"` diagnostics. HTTP success, valid parsing, and equal category sets
alone cannot establish completeness. The full audit now accepts a specific
snapshot only after every scoped coverage check passes; its optional catalog
export carries `Verified` / `isComplete: true` and a hash-bound, dated receipt.
Runtime refreshes validate newly returned data against the established
browse contract; the research audit is separate from that runtime validation.

The Python audit writes a research receipt into JSON. The current C# model still
computes an unverified state and has no `coverageEvidence` property. A separate
`ValidatedCatalog` and `CatalogService` provide production refresh
validation and caching of live C# downloads; importing the Python receipt is not
a prerequisite. See [the runtime contract](catalog-data-layer.md).

## Reliability behavior

- Sequential queries, with 500 ms between categories.
- 30-second local timeout covering response headers and body reads, caller cancellation, strict UTF-8 decoding, and an 8 MiB response limit.
- One delayed retry for HTTP 500/502/503/504, honoring `Retry-After`. A delay above 30 seconds defers the run and stops remaining calls. A terminal failure with `Retry-After` also stops further calls.
- No automatic retry on 401, 403, or 429; these failures stop remaining queries or detail samples. Cookies and automatic redirects are disabled.
- Malformed/deep JSON, unexpected page types, rejected tiles, unresolved pagination actions or markers, totals above observed tile counts, and zero tiles invalidate a query.
- Identity conflicts and disagreement between All Games and the category union prevent a successful snapshot.
- Failed runs write a separate diagnostics report and preserve an existing export.
- Successful research exports use a temporary file and atomic replacement; this is not a production cache of a complete catalog.

## Reproduce

From the repository root, first run the [build and tests](../README.md#build-and-test).
Replay the reduced fixtures without Amazon access:

```sh
dotnet run --project tools/LunaCatalogProbe -c Release --no-build -- --fixtures tests/Fixtures --output artifacts/fixture-catalog.json
```

Fixture replay exports five games with `coverageStatus: "Unverified"` and
`isComplete: false`. To fetch the current US catalog with sampled detail checks:

```sh
dotnet run --project tools/LunaCatalogProbe -c Release --no-build -- --live --verify-details --output artifacts/checked-us-catalog.json
```

Both modes return **2** for successful unverified research. Failed checks return
1 and write separate diagnostics while preserving the existing export; invalid
arguments return 64 and cancellation returns 130.

The live probe needs `proxy-prod.us-east-1.tempo.digital.a2z.com` and normal
proxy/system TLS trust. Browser checks additionally need the frontend and asset
hosts listed in [the browser tool instructions](../tools/LunaBrowserEvidence/README.md).
For the full scoped audit and optional verified export, follow
[the streaming verification commands](streaming-completeness.md#repeat-and-export).
The source tree retains only sanitized reduced fixtures and evidence; the full
live dataset is not bundled.

## Scope and limitations

The earlier global Envoy tunnel outage did not recur on October 11. Supported
network permission made Git, NuGet, Luna, and US API requests work through the
inherited proxy. Environment readiness and actual egress should both be checked
in a new session. Native Chromium's CA error remains; verified Node forwarding
works without repairing that trust store.

**The completeness verdict applies to the recorded public US streaming scope.** The full
[audit capture](evidence/2026-10-11-public-streaming-audit.json) covers the closed
advertised route graph, every loaded carousel slide, all advertised genres,
36 paginated search queries, and all 118 product identities/canonical URLs in
two fresh contexts. Search discovery and all 111 detail recommendation IDs remain
inside All; end-of-run All rechecks in both title orders agree with the source.
Claims is excluded and account-specific playability is not part of this verdict.

The data layer implements production validation and caching for runtime C# downloads
using the browse protocol established by this investigation. Every refresh must
pass its response checks; its game set need not equal the historical capture.
The current research CLI's unverified flag does not make a full Python audit a
runtime requirement. Native Chromium trust remains an environment limitation
for the optional research tools.

The [Playnite browser](playnite-browser.md) integrates the production service
with local search/filtering and runtime refreshes. Its
[Windows host checklist](playnite-browser.md#outstanding-windows-host-checks)
covers integration checks that the research tools cannot establish. The optional
research CLI retains its existing behavior.
