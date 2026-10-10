# Luna catalog API investigation

## Status

Investigated October 10, 2026 using anonymous requests from the managed Linux environment. The existing n8n workflow and fetch-notes raw URLs returned HTTP 404. The plan's request shape nevertheless produced working responses from Amazon's first-party endpoint.

Milestone A remains **partially complete**: retrieval and normalization work, but full catalog completeness has not been demonstrated. No WPF development or production cache implementation should assume these observations establish complete coverage.

## Working request

`POST https://proxy-prod.eu-west-1.tempo.digital.a2z.com/getPage`

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

This is an observed working combination, not a claim that every header is required. Header minimization and browser request parity remain future checks.

```json
{
  "timeout": 10000,
  "searchContext": {
    "query": "",
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
| All Games | empty string | `browse_results_all_games` | 118 |
| Standard | `included_with_prime` | `browse_results_included_with_prime` | 90 |
| Premium | `luna_premium` | `browse_results_luna_premium` | 118 |
| GameNight | `game_night` | `browse_results_game_night` | 50 |

The three category sets have a union of 118 native game IDs, equal to the All Games set in both directions. Standard's 90 IDs are a subset of Premium in this observation. This overlap is preserved; Premium membership is not modeled as exclusive or as an account entitlement.

Every observed tile had a native game ID, a relative Luna game-page action, and a product URL with `territory=US`. The anonymous homepage also advertised locale `en_US` and marketplace `ATVPDKIKX0DER`. These facts support the requested marketplace, but independent website verification of US coverage remains outstanding. The server's geographic location and the endpoint's `eu-west-1` name do not establish the catalog's region.

Alphabetical boundaries observed:

| Query | First title | Last title |
| --- | --- | --- |
| All Games / Standard / Premium | Alan Wake 2 | WRC Generations Fully Loaded Edition |
| GameNight | Angry Birds Flock Party | Worms W.M.D |

Counts are observations of one investigation, not guaranteed current totals.

## Response structure and identity

Responses contain nested `pageMemberGroups.mainContent.widgets` collections with `type: "GAME_TILE"` children. The parser traverses objects and arrays rather than assuming a fixed nesting depth.

`presentationData` is a **JSON-encoded string** in the recorded responses. It includes `gameId`, `title`, `releaseYear`, `imagePortrait`, `imageLandscape`, `genreTags`, and `hoverDetails`. The parser also accepts an object representation for defensive compatibility.

Observed `gameId` values use both `amzn1.adg.product.<uuid>` and `amzn1.luna.product.<uuid>`. The latter appears on eight games in the captured All Games response. Most tiles have an action target such as `/game/alan-wake-2/B0GFWDM8NP`. Fortnite instead uses `/game/fortnite`; its ASIN is present in `hoverDetails.productUrl`. The parser retains the actual Luna page action and derives an ASIN from the path or that first-party product URL. Some product URLs point to `play.amazon.com`; those are not substituted for the Luna page action. Reduced recorded regression samples for these two cases are in `tests/Fixtures/EdgeCases.json`.

Canonical identity is `(region, gameId)`, falling back to the action's ASIN if the native ID is absent. Both identifiers are retained. Repeated tiles and overlapping memberships merge by identity, never by title. Tiles missing a title or safe Luna page action invalidate their query; incomplete parse results remain diagnostic observations.

All observed tiles had game IDs and game-page actions. A rendered-page check is still needed to rule out promotions, non-playable editions, or storefront items masquerading as game tiles.

## Additional anonymous detail checks

The same endpoint accepts `pageContext.pageType = "game_detail"`, `pageContext.pageId = <native product ID>`, `encryptPageId = false`, and `productStage = "Release"`. Omit `searchContext`; the other client/input/feature fields and headers remain as above. This navigation context was read from the catalog tile's action. No service token, cookie, or account authentication was needed in the reproduced request.

Five representative requests returned HTTP 200 with response page type `game_detail_anonymous`. Each main `PRODUCT_DETAIL` had a product ID and ASIN matching the catalog tile, and `SEO_DETAIL.canonicalUrl` matched its Luna action. GET requests to the five actual Luna action URLs also returned HTTP 200 without redirecting elsewhere.

| Sample | Purpose |
| --- | --- |
| Alan Wake 2 | Standard / Premium overlap |
| Alien: Isolation | Premium outside the observed Standard set |
| Angry Birds Flock Party | GameNight membership |
| Batman Caped Crusader - Chronicles | `amzn1.luna.product` namespace |
| Fortnite | Slug-only Luna action |

The detail response distinguishes the main product from recommendation cards (`GAME_TILE_VERTICAL`) and DLC (`ADD_ON_TILE`). Recommendations observed across these five pages introduced no native game IDs outside the captured All Games set. This is another coverage cross-check, not a completeness proof.

All five primary buttons reported **"Not Available"** in this anonymous environment. The cause has not been established; the requested US catalog, regional availability, and anonymous client state still need browser verification. Do not discard these entries, infer a removal, or claim global unavailability from this text. Retrieval of a public catalog record does not establish that this client or a particular user can play it.

`--live --verify-details` makes up to five distinct sample requests after the four browse queries pass, records identity/canonical URL checks and button text in `detailChecks`, and keeps `isComplete: false`. Any requested detail check that fails prevents replacement of the output; diagnostics are written separately. Reduced Alien and Fortnite detail fixtures cover main product identity, recommendations/DLC, canonical links, and the slug-only case.

## Pagination and completeness

No continuation token, next-page cursor, `hasMore`, pagination object, or total-count field was observed in the four responses. Each request returned a single collection extending from the first to the last title listed above.

This does **not** prove the browser makes no subsequent requests or that the API returns all available games. Query-union equality is useful evidence, but both query mechanisms could share the same server limit.

The parser flags recognized continuation markers as `UnresolvedContinuation`. It deliberately does not fabricate a pagination request protocol. A future observed protocol must be implemented and tested before such responses can be validated. Absence of markers is reported as `NoContinuationObserved`, never "all pages verified."

The Phase 0 snapshot always exposes `isComplete: false` and `coverageStatus: "Unverified"`. Successful HTTP requests, valid parsing, and equal category sets cannot change those fields.

## Reliability behavior

- Sequential queries, with 500 ms between categories.
- 30-second local timeout, caller cancellation, one delayed retry for HTTP 5xx.
- No automatic retry on 401, 403, or 429; no authentication or anti-bot bypass.
- Malformed JSON, unexpected page types, rejected tiles, unresolved pagination, and zero tiles invalidate a query.
- Failed runs write a separate diagnostics report and preserve an existing export.
- Successful research exports use a temporary file and atomic replacement; this is not a production cache of a complete catalog.

## Reproduce

From the repository root, use the commands in [README](../README.md). Fixture replay needs no Amazon access. Live probing needs access to `proxy-prod.eu-west-1.tempo.digital.a2z.com` and normal system TLS trust. The source tree retains only sanitized, reduced response samples; the full live dataset is not bundled.

## Remaining blockers and acceptance checks

The current session's cloud allowlist permits `luna.amazon.com` and the tested proxy endpoint, but blocks `m.media-amazon.com`, where the homepage loads its JavaScript. A direct request to that asset host was rejected by the environment's network proxy. The user added the host to the configuration and reported that a new chat is needed for it to take effect. Clean Chromium navigation also requires its certificate store to trust the environment proxy CA; TLS verification was not disabled. See [the next-session handoff](next-session.md).

To finish Phase 0:

1. Resolve frontend asset access and Chromium CA trust, using normal environment configuration.
2. Capture actual anonymous requests for `/browse` and all three quick-search pages.
3. Scroll to the bottom; record any additional requests and implement their observed pagination protocol.
4. Compare IDs/counts, boundary titles, search results, and category overlaps against the rendered website. Check whether other catalog groups exist.
5. Check representative Luna game-page actions in a clean browser and verify US catalog behavior.
6. Repeat in a new anonymous session and retain sanitized evidence of coverage and termination.

The working implementation is a research probe, not a complete-catalog claim or a usable Playnite extension package.
