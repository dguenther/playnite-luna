# Catalog validation record

## Configurable Play/Install state — preview 0.2.6

The user's requested extension setting is enabled by default. It applies to
existing owned, marked imports on Save/startup/open/library update, and new
imports use the latest saved choice. Enabled games are installed and expose a
native Play controller; disabled games are uninstalled and retain the Install
website controller. Both open the stored Luna page. Play signals Stopped(0)
without Started, so no streaming duration or play-count/last-played update is
inferred. Settings changes manage only installed state and inclusion of the
library's Play action, preserving GUIDs, URLs/actions, and descriptive metadata.

The Windows SDK/WPF harness exercises the settings checkbox, enabled default
when upgrading an older preferences file, Save/Cancel, existing/legacy/new
entries, untouched other libraries, restart reload, browser preference retention,
locked-file save rollback, future-schema rejection, and both website handoffs.
Captured openers and SDK event subscriptions verify repeat/failure cleanup and
no fabricated session tracking. The offline suites remain 267 .NET and 24 Python
checks. Actual Playnite settings/Play/Install behavior remains a manual host check.

## Install-button website handoff — preview 0.2.5

The requested follow-up uses the SDK's public `GetInstallActions` callback and
an `InstallController` to open an existing Luna import's stored game-page URL.
The controller never signals installation success: the SDK cancellation event
clears Playnite's installing state and keeps cloud games uninstalled. This
completion behavior was checked against Playnite 10.62's `GamesEditor` and
controller-factory source. Browser errors propagate to the host after cleanup.

The Windows SDK/WPF harness covers registration for existing owned imports,
foreign/manual/unmarked exclusions, unsafe URLs, ASIN conflicts, action/link
fallback, slug-only URLs, repeated handoffs, preserved metadata, no installed
event, failure cleanup, and disposed-plugin exclusion. It uses a captured opener
and isolated SDK event subscriptions, with no actual browser launch or catalog
requests. The offline suites remain 267 .NET and 24 Python checks. Actual button
behavior in Playnite, restart persistence, and the broader C/D/E host checklists
remain pending; the user is performing those checks.

## Monochrome sidebar symbol — preview 0.2.4

The user screenshot shows the official circular purple favicon in Playnite and
requests a monochrome symbol matching the other sidebar icons. This preview
uses the exact standalone path from the official website's `TempoLogoSVG`
component. WPF renders the embedded geometry without a circle and inherits the
host sidebar foreground, including theme and selection changes. Coordinates,
source, conversion details, and SHA-256 are recorded in the
[resource notes](../src/LunaPlaynite/Resources/README.md) and packaged notices.

The Windows WPF harness checks offline vector parsing, transparent surroundings
and center, and foreground updates inherited from the parent button. The
installed-archive check verifies the embedded geometry resource's SHA-256.
The .NET/Python suites remain 267/24. Actual host appearance across themes and
DPI settings remains a manual check.

## Official sidebar logo — preview 0.2.3

The user screenshot confirms that the sidebar catalog renders inside Playnite.
The requested branding update replaces the placeholder moon with the unmodified
128×128 PNG linked as Luna's official website icon. The asset is embedded in
`LunaPlaynite.dll`, loaded through a local WPF resource URI, and rendered with
high-quality scaling at sidebar size. No network icon request or extra runtime
file is introduced; source, SHA-256, and attribution are recorded in the
resource notes and packaged notices at that version. Preview `0.2.4` replaces
that asset with the standalone symbol described above.

The existing Windows sidebar check now verifies offline logo decoding and
dimensions. The installed-archive check verifies the embedded PNG's SHA-256
against the original. The .NET/Python suites remain 267/24; host logo appearance
at different DPI/theme settings still requires manual checking.

## October 18 inline sidebar follow-up — preview 0.2.2

Merged Milestone D PR #8 is the baseline (`7c21d4f`). The extension now offers a
Desktop sidebar moon icon titled **Browse Amazon Luna Catalog**. It uses the SDK's
native view callback to return the catalog control inline, with no eager catalog
initialization or network request during sidebar registration. The optional
main-menu window remains available. Separate controls share one browsing model,
service/cache pair, artwork cache, import service, and busy state. Filters wrap
to fit narrower inline content areas.

`CatalogBrowserSession` coordinates active hosts. Leaving the last view cancels
unfinished work, preserves committed games, and saves preferences. Another open
host keeps work running. Reopening waits for cancellation to settle, then loads
cached state and reconciles imports without an API fetch. Hidden controls stop
their timers/handlers and unloaded artwork requests; shutdown hides the sidebar
registration and cancels the shared session.

Five new session regressions bring the .NET total to **267**, alongside the
existing **24 Python checks**. They cover offline restoration, filters/selection,
one/last-host cancellation, rapid reopening, duplicate imports across hosts, and
retaining committed entries on disposal. Windows smoke checks invoke production
sidebar and main-menu callbacks with actual SDK/WPF controls, switch inline
content away/back, open both hosts, check shared selection/filters/preferences,
verify Fullscreen exclusion, and dispose without new library entries or changed
catalog cache bytes. The preview remains the same six runtime files, with a
vector icon in the DLL and shared host JSON 10.

These checks do not establish actual Playnite sidebar/theme/keyboard/DPI behavior.
Complete [the sidebar host checks](playnite-browser.md#outstanding-windows-host-checks)
and the remaining C/D checklists before closing those gates. No new live catalog
audit or full saved API fixture is needed.

## Host feedback follow-up — preview 0.2.1

The user reports that the `0.2.0` browser opens in Playnite and imported games
appear in the library. This establishes basic host loading/use, but does not
close the broader C/D host checklists. They identified an unnecessary heading,
low-contrast list text, and Luna games grouped under **Library → Playnite**.

The `0.2.1` follow-up removes the heading and extends Playnite's implicit
`ListBoxItem` style, preserving normal/selected theme colors and templates;
detail text uses full opacity. The extension now registers as a `LibraryPlugin`
named **Amazon Luna**. New entries belong to it; earlier marked manual imports
migrate by changing only `PluginId`, retaining GUIDs and edited metadata.
Foreign/unmarked entries remain untouched. Global library updates perform no
catalog fetching or automatic importing.

The suite now contains **262 .NET tests**, including **51 import cases**. New
regressions cover current and legacy ownership. Windows harness checks also
cover dynamic theme colors, library registration, an empty automatic import
enumeration, repeated legacy migration, and preservation of edited fields.
Package checks require `LibraryPlugin`/`GameLibrary` with the actual host SDK.

The public SDK has no direct trigger for Playnite's built-in metadata-download
workflow, and `Database.ImportGame` only creates a game. Library registration
does not trigger that workflow for explicit browser imports. The supported
workflow is selecting the imported games and using **Download Metadata** in
Playnite; [the import documentation](library-imports.md#metadata-downloads)
records this limit. Updated theme/grouping/migration and broader persistence,
actions/restart/clean-install results still require actual host checks.

## October 11 library imports implementation — Gates C/D host checks pending

Milestone D implements optional single/multi-selection imports and confirmed
complete-catalog Import All. The import service reconciles versioned mappings
with durable host markers, prunes deleted/foreign mappings, recovers missing
state, supports ASIN-to-native-ID migration, and rejects conflicts. SDK creates
are manual/uninstalled games with Luna source/tags, direct links, additional URL
actions, and optional artwork through host storage. Refresh/reimport preserve
existing user metadata, and catalog removals never delete library entries.

The **260 .NET tests** include **49 new import/mapping/browser cases** using the
existing reduced API samples and real temporary mapping files. They cover
partial/concurrent retries, before/after-commit write failures, cancellation,
malformed/future/oversized/duplicate/deep state, backup recovery, foreign entries,
deleted-game reconciliation, migration/conflicts, metadata preservation,
full-catalog scope while filtered, and a confirmed import awaiting activation
reconciliation. Cancelling/closing during that check also discards the pending
import; the regression reproduced unwanted creates before the fix. The known
JSON 10 nesting advisory remains mitigated by explicit
depth 64 in catalog and mapping readers/serializers.

Windows CI extends the real WPF harness to the production import adapter using
SDK models and an isolated database API: extended selection, rejected/confirmed
Import All, source/tags/actions/manual flags, optional file storage, unchanged
user edits after repeats, deletion/reimport, and mapping reload. The extracted
`.pext` process checks mapping round-trip and duplicate rejection using only the
actual host JSON 10 DLL. The package version is `0.2.0`; no new dependency DLL,
full catalog response, or API fixture is bundled.

**Gates C and D remain pending actual Playnite host tests.** Automated SDK/WPF
checks do not establish real database persistence, action menu behavior, host
themes, restart behavior, or clean installation. Complete the
[browser](playnite-browser.md#outstanding-windows-host-checks) and
[import](library-imports.md#verification-and-remaining-host-checks) checklists
before final release validation. The following sections preserve earlier results.

## October 11 Playnite browser implementation — Gate C host checks pending

Milestone C adds the official-template `net462` / SDK 6.17.0 GenericPlugin,
main-menu WPF browser, plugin-local cache/preferences, local search/category/title
sorting, overlapping membership display, counts, refresh age/error state,
asynchronous refresh/cancellation and artwork, selection, and Open in Luna.
It does not import or modify Playnite library entries.

Locked restore and full Release builds, including compiled WPF XAML and the
Windows smoke executable, pass with zero warnings/errors. All **211 .NET tests**
and **24 Python offline checks** pass. The **26 new browser/artwork cases** use
the existing five-game API fixtures and injected failures: cached startup makes
no requests; filters/sorting retain cache bytes; selection follows stable IDs;
cancelled/failed refreshes retain games; the real HTTP provider feeds the browser;
and optional images enforce host/size/deadline/concurrency/cancellation bounds.

Windows CI runs a real WPF window/dispatcher harness checking bindings, overlapping
memberships, sort/selection, Open in Luna callback identity, responsive browsing
during refresh, cancellation, committed metadata/selection updates, frozen image
decoding, UI-thread notifications, and offline cache restart. The executable has
an explicit `win-x86` target so locked restore agrees across Linux and Windows.
It also starts an obsolete artwork request, loads its replacement, then fails
the old request after cancellation and confirms the replacement remains visible.
The queue cancellation test also exposed a released-slot race on Windows;
downloads recheck cancellation after acquiring a slot before starting HTTP work.

CI uses the official Playnite 10.62 Toolbox to generate a preview `.pext` after
checking the portable archive checksum and host SDK version. Staging and
archive checks verify the three Luna DLLs, manifest, and licenses, with no JSON
DLL bundled. The `net462` catalog uses the official host's JSON 10.0.3 without
an assembly loader or binding changes; standalone .NET 8 uses JSON 13.0.3.
An independent process preloads official host SDK/JSON 10,
loads the extracted package, and validates/caches/reloads representative data
using only that host JSON assembly. It also rejects duplicate properties
(nested and escaped), trailing cache data, and overly deep cache/API responses
with the actual host DLL. Three additional .NET cache cases cover nested and
escaped duplicates and excessive depth. All catalog JSON readers use depth 64;
see [the compatibility and advisory mitigation](playnite-browser.md#build-and-package).
No raw catalog, source, test fixtures, or user data is packaged.
The preview is a CI artifact, not a
published release.

**Gate C remains pending:** these automated checks do not load `LunaPlugin`
inside Playnite Desktop Mode, validate host themes/default-browser launching, or
establish clean-install behavior. Follow [the Windows host checklist](playnite-browser.md#outstanding-windows-host-checks)
before marking C passed. Import/status acceptance remains Milestone D. No new
complete Luna response fixture or repeated Python catalog audit is needed.
The following records preserve earlier milestone results and test counts.

## October 11 review fix: classify provider transport failures

The production HTTP provider now retains a typed network failure on browse and
detail observations. `CatalogService` checks that cause before generic snapshot
validation, including when a session-wide HTTP error leaves fewer than four
queries. Connection errors, header/body timeouts, response-stream I/O errors,
unsuccessful HTTP statuses, and long retry deferrals report `Network`.
Malformed JSON/UTF-8 and oversized responses remain `Validation`; caller
cancellation remains `Cancelled` and propagates.

The new service regressions reproduced ten transport cases misclassified as
`Validation` before the fix. All thirteen classification cases now pass through
the real `LunaCatalogProvider` with injected HTTP responses and a real temporary
cache, preserving previous games, timestamp, and cache bytes with no change list.
The other five new cases cover real-provider cancellation, typed detail failures,
and rejection of transport failures even when success flags are set.

Locked restore and Release `net462`/`net8.0` builds pass with zero warnings/errors.
All **182 .NET tests**, including **85 production data-layer cases**, and all
**24 Python offline checks** pass. All 81 relative documentation links and anchors
pass. Tests use reduced representative API fixtures and injected failures; no new
live catalog audit or complete response fixture is needed for this fix.

## October 11 production data layer — Gate B passed

Locked restore and Release `net462`/`net8.0` builds pass with zero warnings/errors.
All **164 .NET tests** and **24 Python offline checks** pass. The 67 new production
cases cover validation against representative API samples, normalized cache
reload, malformed/schema/scope checks, backup recovery, atomic replacement,
byte preservation, cancelled/failed refreshes, serialized refreshes, metadata
comparisons, and ASIN-to-native-ID migration/conflicts. CI now covers Linux and
Windows; these builds/tests do not validate loading inside Playnite.
All 81 current relative documentation links and anchors also pass validation.
The `net462` reference-assembly packages are explicit so the committed lock file
works consistently on Linux and Windows, regardless of locally installed targets.

A live `CatalogService` smoke test retrieved the public US catalog at
**16:45:02 UTC**, accepted it as `Validated` / `isComplete: true` under
`PublicUSStreamingBrowseV1`, and saved a normalized cache. All four queries
returned HTTP 200 with no continuation: All 118, Standard 90, Premium 118,
GameNight 50. A new service instance loaded all 118 games without networking.
A deliberately failing transport then reported stale cached data and a network
error, with no change list and byte-for-byte preservation of the cache.
The [reduced result](evidence/2026-10-11-runtime-cache.json) includes counts and
outcomes only. The smoke harness and full runtime cache remain ignored; fixtures
still contain five representative games, not a complete catalog response.

This uses the Gate A browse contract, not a new full Python research audit or
receipt import. The research CLI still exports unverified snapshots with exit 2.
**Gate B passes.** Continue with the Playnite browser in Milestone C; see
[the runtime contract](catalog-data-layer.md) and [handoff](next-session.md).
The following sections preserve earlier research history and its test counts.

## October 11 review fix: derived output path collisions

The streaming audit now checks the source, evidence, optional verified catalog,
derived diagnostics, and every temporary target for collisions before reading
the snapshot or starting live requests. Validation and atomic writes share the
same temporary-path helper.

The expanded regression covers 12 collision scenarios, including the three
literal-path conflicts, nine derived/normalized-path conflicts, and omission of
the optional catalog output. The original code failed all nine derived cases;
the fix rejects all 12 before network work and preserves existing files byte for
byte. All 24 Python offline tests and all 72 local documentation links/anchors
pass. The C# provider and representative API fixtures are unchanged.

## October 11 full public streaming audit — Gate A passed

The exact final audit tool completed at **15:24:11 UTC**, after an independent
initial full run also passed. The recorded scope is `PublicUSStreamingCatalog`.
Claims/PC giveaways, account libraries, entitlement, and individual streaming
eligibility are excluded; no login or streaming launch was attempted.

| Check | Final result |
| --- | --- |
| Fresh raw C# fetch | 118 All / 90 Standard / 118 Premium / 50 GameNight; all five sample details pass |
| Public streaming pages | All 13 discovered pages visited in each of two fresh browser contexts |
| Horizontal collections | All 27 carousel/swiper instances, 229 slide positions in each context |
| Advertised genre filters | All 26 checked in two anonymous API contexts; matching sets, no outside IDs |
| Search | All 36 letter/digit queries fully traversed; identical page IDs/order across contexts; reverse references agree |
| Search union | Exactly All's 118 IDs in both contexts; zero omissions/extras |
| Empty-query discovery | 20 recommendation IDs, all inside All; excluded from query matches |
| Every main product | 118 ID/ASIN/title/canonical checks pass in each API context |
| Detail recommendations | 111 distinct game IDs; all inside All; DLC excluded |
| Anonymous detail labels | 90 “Play free with Prime”, 28 “Included in Luna Premium” per context |
| Final All rechecks | Both title orders match the source snapshot in both contexts |
| Snapshot-bound complete export | Produced `Verified` / `isComplete: true`, with scope, source hash, ID-set hash, and verification time |
| Final audit exit | 0 |
| Offline CI | Locked restore and Release net462/net8.0 build passed; 97 .NET and 24 Python tests passed |

The [reduced evidence](evidence/2026-10-11-public-streaming-audit.json) retains
counts, hashes, route/filter manifests, and comparison outcomes. Raw responses,
guest credentials, and full datasets are not committed. The full results and
verified catalog remain in ignored `artifacts/` and can be regenerated with the
[repeatable command](streaming-completeness.md#repeat-and-export).

Eight new regression checks cover complete-source requirements, cross-context
search stability, browse continuation/truncation, preserved genre parameters,
region/marketplace checks, source/evidence/output collisions, scoped snapshot
binding, and preservation of prior evidence/complete exports on failure. C# code
is unchanged by the full-audit follow-up. The raw C# fetch still returns 2 with
unverified coverage; a full audit is required for a checked complete export.
The count 118 is historical.

**Gate A passes for this recorded scope.** At this stage, production cache and
refresh validation were the next increment; they are now covered by Gate B above.
The following sections preserve earlier run history; their open-gate statements
describe those earlier stages.

## Historical: October 11 live rerun before the full audit

Continued PR #5 on October 11, 2026 (America/New_York) from `fc3b8f8`.
The prior global proxy outage did not recur. Commands using the supported network
permission reached GitHub, NuGet, Luna, and the US API through the inherited proxy
with TLS verification. An initial sandboxed refusal was resolved by that command
permission; no destination policy or proxy route was changed.

| Check | Rerun result |
| --- | --- |
| Locked restore | Passed; SDK 8.0.425 |
| Release build | `net462` and `net8.0`; 0 warnings/errors |
| .NET tests | 97 passed; 0 failed/skipped |
| Python offline checks | 16 passed after the search follow-up; included in CI |
| Fixture replay | Five games; expected exit 2 |
| Live CLI with all five detail checks | Passed; expected exit 2; Batman recovered |
| All / Standard / Premium / GameNight | 118 / 90 / 118 / 50; category union equals All |
| Final browse tool | Expected exit 2; two contexts, eight matched ID/title/action sets |
| Browse scrolling | Stable bottoms; only detail prefetches, no browse continuation |
| Search/discovery tool | Expected exit 2 after waiting for visible detail titles; two contexts |
| Eight search queries | Counts 49/39/8/2/2/7/4/1; no API/rendered differences or IDs outside All |
| Home / GameNight discovery | 36 / 50 loaded unique tile IDs; 38 / 51 rendered links, all in All |
| Five representative game pages | Ten successful rendered checks; document/detail HTTP 200; ID/ASIN/canonical/title/label matched |
| Search offset experiment | Expected exit 2; indices 0/50/100 yield 49/33/20 tiles; all in All; ordering varies |
| Search completion | Follow-up validates sampled alphabetical traversal and explicit termination; see below |
| Claims classification | Outside streaming scope; historical CloudFront failures are not a gate blocker |
| Native Chromium trust | Still `ERR_CERT_AUTHORITY_INVALID`; Node forwarding retains verified TLS |
| Gate A / export completeness | Open / false; research remains `Unverified` |

The first exact browse-tool run hung waiting for background route handlers during
context cleanup. Cleanup now ignores late background errors while catalog routes
must drain within 30 seconds before evidence is accepted. The repaired exact tool
completed both contexts. Regression checks cover the hang, outstanding catalog
requests, partial/incorrect-region comparisons, sensitive-field omission, and
preservation of existing evidence. A first discovery run caught two detail-title
rendering races; waiting for each title resolved them and the final rerun passed.

The [browse](evidence/2026-10-11-anonymous-browser.json),
[search/discovery](evidence/2026-10-11-search-discovery.json), and
[offset experiment](evidence/2026-10-11-search-offsets.json) records contain
sanitized counts, set hashes, public request fields, and limited representative
titles. Full snapshots stay in ignored `artifacts/`. Search offsets are exploratory
requests; the follow-up below validates a separate alphabetical traversal. GameNight's extra
landing link is Tetris® Effect: Connected, outside its filtered membership;
marketing links do not redefine category memberships. Alien's detail page exposed
eight `ADD_ON_TILE` widgets, distinct from its main product and recommendations.

See [the current handoff](next-session.md) for exact remaining limitations and
reproduction commands. The following sections retain earlier run history; their
outage and pending-rerun statements describe those earlier sessions.

## Historical: October 11 sampled search pagination resolution

Public frontend source shows a fixed size-50 relevance request; infinite scrolling
only reveals widgets from that response. Replaying identical frontend requests
still changed relevance sets, so missing client metadata did not explain the
initial ambiguity. The final offset tool instead uses alphabetical sorting and
requires an explicit no-results page with `SEARCH_NO_RESULTS_FOUND`. Its 20
recommendations are excluded from matches; short pages do not terminate traversal.

The [final capture](evidence/2026-10-11-stable-search-pagination.json) passed in two
independent anonymous request contexts, with two traversals per query. `a` yielded
50/32/0 matches at offsets 0/50/100 (82 distinct); `e` yielded 39/0 at 0/50.
Repeated pages and cross-context ID order matched, with no overlaps or IDs outside
All. Size-100, size-200, and reverse-title references returned identical sets.
The exact final tool returned the expected research exit 2. Six new regression
checks cover recommendation exclusion, malformed states, short nonterminal pages,
bounded failure, unstable/reference-disagreeing sets, and protected exports;
all 16 Python checks passed. C# code is unchanged by this follow-up.

Claims is excluded from the streaming gate. These checks resolved the sampled
search protocol ambiguity; exports at this stage remained `Unverified`. The full
audit recorded above subsequently supported a scoped verified export.

## Historical: initial anonymous frontend continuation

Continued October 10, 2026 in America/New_York (October 11 UTC) from current main
`7b81bf6`. The clean restored checkout was fast-forwarded without discarding local
work. No applicable `AGENTS.md` was present. SDK 8.0.425, Chromium 154.0.8037.57,
and Python Playwright 1.62.0 were available.

| Check | Continuation result |
| --- | --- |
| Applied host policy | Current observations; enforced; new global frontend host present in runtime and executor policy |
| Locked restore | Passed |
| Release build before and after US endpoint/query alignment | Both targets passed; 0 warnings/errors |
| Automated tests after request alignment | 97 passed; 0 failed/skipped |
| Optional browser-tool offline tests | Four passed; sensitive fields omitted, mismatches/failures preserve prior evidence |
| Fixture replay | Five games; exit 2; `Unverified`, `isComplete: false` |
| Anonymous rendered browse grids | Two fresh contexts; All 118 / Standard 90 / Premium 118 / GameNight 50 |
| Rendered IDs/titles versus browser API and prior C# probe | No differences in any of the eight view checks |
| Membership overlaps | Standard/Premium 90; Standard/GameNight 50; Premium/GameNight 50 |
| Terminal scrolling | Stable bottoms in every view; extra calls returned game details, no further browse results |
| Frontend-selected request | US endpoint, `all_games`, `en_US`, marketplace `ATVPDKIKX0DER` |
| Cookie-free US CLI browse checks | All four HTTP 200; 118/90/118/50; no recognized continuation |
| US detail checks | Four identity/ASIN/canonical checks passed; Batman returned 503 after bounded retry |
| US detail labels | Prime/Premium labels replace the EU endpoint's prior `Not Available` observations |
| Full US live CLI with requested details | Exit 1; `Incomplete`; separate diagnostics because Batman failed |
| Later US live CLI during proxy outage | All browse calls failed with 503; exit 1; no successful export written |
| Native Chromium trust | Still `ERR_CERT_AUTHORITY_INVALID`; no trust-store repair claimed |
| Node-forwarded browser TLS | Configured proxy/CA and TLS verification retained |
| Nonempty search/discovery and representative rendered link checks | Blocked by environment-wide Envoy outbound tunnel failure; not passed |
| Final browser-tool revision | Syntax/help and fixture parsing checks passed; added per-card link/failure handling could not be rerun live |
| Phase 0 verdict | Open; full catalog completeness remains unverified |

The original capture compared rendered link prefixes; at this stage the final
tool's additional per-card link assertion still needed a live rerun. The later
[committed capture](evidence/2026-10-11-anonymous-browser.json) replaces that
initial capture and passes the assertion in both contexts. This subsection
records the earlier state before that rerun.

No continuation code was added because browse did not show one. Search landing
did supply start-index/page-size fields, but no nonempty search traversal was
captured; applying those fields to browse would be speculative.

The new endpoint and explicit All Games query are covered by the existing
request-contract test, updated to assert the concrete US destination and
`all_games`. Other parser/transport/identity safeguards remain intact. The
research snapshot contract is unchanged; no production cache or UI was added.

The blocker at this stage was the Envoy tunnel failure; it was resolved during
the later rerun. Readiness remained enforced/current even while Luna and GitHub
returned Envoy
503 with `Invalid argument` at `envoy://cloudflare_https_tunnel/`. The user cannot
control this infrastructure and asked to finish available work.

## Historical: validation before rendered website coverage

Validated October 11, 2026 (UTC), in the managed Debian Linux environment with .NET SDK 8.0.425. This follow-up is based on the probe and detail verification already merged in PR #1.

| Check | Result |
| --- | --- |
| Locked dependency restore | Passed |
| Release library build, `net462` and `net8.0` | Passed |
| Release CLI and test build, `net8.0` | Passed |
| Build warnings/errors | 0 / 0 |
| Offline automated tests | 97 passed, 0 failed, 0 skipped |
| Fresh anonymous browse requests | All four returned usable HTTP 200 responses |
| All Games / category union | Both 118 distinct native IDs, no set differences |
| Standard / Premium / GameNight memberships | 90 / 118 / 50 |
| Representative anonymous detail checks | Five passed product ID, ASIN, and canonical URL comparison |
| Live CLI exit | Expected 2; coverage remained `Unverified`, `isComplete: false` |
| Failure export protection | Malformed queries and mismatched category sets preserved prior exports and wrote separate diagnostics |
| Rendered website comparison and scrolling | Blocked by signed-out login navigation; not passed |
| Windows, Playnite host loading, and `.pext` | Not performed; extension implementation is pending |

The 46 additional cases cover response limits with and without `Content-Length`, cancellation and deadlines during streamed body reads, invalid UTF-8, session-wide authentication/rate-limit failures, delta/date `Retry-After` deferral, extra continuation markers/actions, totals above observed tiles, conflicting identity mappings, unsafe or mismatched play links, category-set disagreement, and export preservation. Existing fixtures, detail verification, command-line behavior, and native identities are retained.

Eight regression cases address the PR review findings: a play link must carry exactly one matching territory, and only the direct `pageMemberGroups.mainContent.widgets` collection supplies browse tiles. They cover missing, empty, mismatched, and duplicate territory parameters, nested sidebar collections with and without valid main content, and preservation of prior exports when either finding occurs. Nested widgets inside the canonical collection remain supported. The new tests reproduced both findings before the fixes; all 97 tests now pass.

Reproduce the offline checks:

```sh
dotnet restore AmazonLuna.sln --locked-mode
dotnet build AmazonLuna.sln --no-restore --configuration Release
dotnet test tests/LunaCatalog.Tests/LunaCatalog.Tests.csproj --no-build --configuration Release
```

Reproduce the live research probe (requires normal proxy and TLS access to the investigated endpoint):

```sh
dotnet run --project tools/LunaCatalogProbe --no-build --configuration Release -- --live --verify-details --output artifacts/checked-us-catalog.json
```

The live command intentionally returns 2 when research checks pass because the C# fetch remains unverified. Historical EU-endpoint detail button text was `Not Available`; subsequent US checks recorded Prime/Premium labels. None establishes entitlement, playability, or removal. Compilation for .NET Framework on Linux does not demonstrate Playnite runtime compatibility. See [API findings](luna-api.md) and [the current handoff](next-session.md).
