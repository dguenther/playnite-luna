# Catalog architecture

Milestone A / Gate A passes for the public US streaming catalog captured October
11, 2026. The [scoped verdict](streaming-completeness.md) records the evidence and
exclusions. Milestone B / Gate B passes for the production data layer; Milestone
C's Playnite browser is implemented, with Windows host validation still pending.

`src/LunaCatalog` builds for `net462` and `net10.0`, with no Playnite or WPF
dependencies. It owns request construction, normalized models, recursive
response parsing, region-scoped identity, category-set merging, and diagnostics.
The HTTP client is injected for testable transport behavior. Requests use the
frontend-selected US endpoint and explicit `all_games` browse query.

`tools/LunaCatalogProbe` runs on .NET 10 and provides live anonymous retrieval and
offline fixture replay. The optional `--verify-details` mode checks up to five
main-product identities and canonical Luna URLs. It writes a research snapshot
atomically after all four queries and any requested detail checks pass response
validation. Failures produce separate diagnostics, preserving any prior export.
Cancellation returns 130; successful research retrieval returns 2. The C# model
reports `Unverified` / `IsComplete: false` for usable snapshots and `Incomplete`
for failed checks.

`tools/LunaBrowserEvidence` contains optional Python research tools. The browse,
discovery, and sampled search tools retain an unverified result and return 2 on
success. `streaming_audit.py` checks the advertised public route graph and every
carousel in two fresh browser contexts, then advertised genres, 36 paginated
searches, every main product, and final All Games rechecks in two anonymous API
contexts. Coverage failures return 1 and write sanitized diagnostics without
replacing prior evidence or verified exports.

Only a full passing audit returns 0 and can export the exact checked snapshot as
`Verified` / `isComplete: true`. Its `coverageEvidence` records the
`PublicUSStreamingCatalog` scope, source-file hash, ID-set hash, and verification
time. This Python-generated JSON is a research artifact; the current C# snapshot
model has no receipt property or verified-state support. Importing that artifact
or running the full Python audit is not a runtime requirement.

`CatalogValidator` applies the established browse contract to current C# downloads:
required queries, expected page types, region/marketplace, safe identities/links,
continuation/total checks, category-union equality, and normalization consistency.
It produces a separate `ValidatedCatalog` (`Validated` / complete) rather than
changing the research CLI's `CatalogSnapshot`. The research receipt identifies
the October 11 capture; runtime downloads may legitimately return changed game
sets. The historical 118-game count is not an acceptance threshold.

`CatalogService` serializes cache loads and explicit refreshes, publishes candidates
only after successful storage, and exposes data source, age/staleness, timestamp,
errors, progress, and changes. `JsonCatalogCache` implements `ICatalogCache` using
the directory supplied by the Playnite host. Versioned normalized games and query
summaries are validated on read/write; bounded UTF-8/JSON reads reject malformed
data. Atomic replacement retains a validated backup, and backup recovery never
replaces good data with a corrupt primary. State copies protect the accepted
baseline from consumer edits.

`CatalogComparer` reports same-scope additions/removals and metadata changes.
ASIN-only records gaining native IDs migrate without becoming new/removed games.
Conflicting or downgraded identities invalidate refreshes; titles never identify
games. Failed/cancelled refreshes retain previous data and produce no changes.
These observations do not delete imported games. See
[the runtime contract](catalog-data-layer.md) for exact behavior and service usage.

The C# transport caps responses at 8 MiB and applies its 30-second deadline to
headers and streamed body reads. JSON and encoded presentations use a 64-level
depth limit. Only browse tiles under the direct
`pageMemberGroups.mainContent.widgets` array enter the catalog. Continuation
markers/actions, totals above observed tiles, conflicting identities, mismatched
play links, and disagreement between All Games and the category union invalidate
an export. Python browser requests use the inherited proxy and verified Node TLS;
this supports the audit without repairing native Chromium's certificate trust.

`tests/LunaCatalog.Tests` covers recorded wire data and transport failures through
an injected HTTP handler. Python offline checks cover evidence comparisons,
cleanup, traversal, source binding, and export preservation. Neither suite needs
live Amazon access. `tests/Fixtures` contains reduced, sanitized samples;
`docs/evidence` contains reduced research receipts. Tests need representative API
shapes and edge cases, not a complete raw response or saved catalog. Optional
full research snapshots remain in ignored `artifacts/` and are not bundled with
the browser.

`src/LunaCatalogBrowser` targets `net462;net10.0` without Playnite or WPF. Its
`CatalogBrowserModel` publishes read-only display items, combines local title
search/category filters with deterministic title/ID sorting, and retains selection
by ID when accepted metadata changes. It runs service work on workers and returns
notifications to the caller's UI context. Duplicate refresh clicks do not enqueue
work; cancellation and unexpected errors leave prior data available. Its optional
`CatalogArtworkClient` bounds anonymous first-party downloads independently of
catalog validation.

`CatalogBrowserSession` gives multiple hosts one browsing model and tracks active
hosts on the UI context. Detaching the last host cancels unfinished work; rapid
reopening waits for that work to settle before restoring cache/import state.
Closing one host leaves another host's operation running. This coordination has
no WPF or SDK dependency.

`src/LunaPlaynite` began with the official Playnite 10 GenericPlugin template and
now registers as a `LibraryPlugin` named **Amazon Luna**, using
SDK 6.17.0 and `net462` WPF. A stable plugin GUID owns one service/session pair;
the Desktop sidebar opens an inline catalog and the main-menu entry opens an
optional themed Playnite-created window. Each has its own WPF control and shares
the session's model. Initialization is lazy, supplying
`GetPluginUserDataPath()` to the cache. Browser preferences use Playnite's settings
API. The virtualized list cancels recycled-row cover requests, decodes images on
workers, and retains bounded frozen bitmaps in memory. Open in Luna launches a
validated page through the default browser. Opening loads cached data. Refresh runs optional catalog automation after a
validated save; library updates refresh by default. Startup/open/library update
migrate marked legacy imports and reconcile installed state.
See [the browser contract](playnite-browser.md).

Milestone D's `CatalogImportService` orchestrates explicit imports through
`ICatalogImportDatabase`, with a bounded atomic `JsonImportMappingStore` and
durable host `GameId` markers. The Playnite adapter reads/creates SDK entries on
the dispatcher and checks identity atomically before creation. Its library entries
carry the Luna source, memberships, direct link and additional URL action, and
optional covers through host storage. Existing metadata is never updated by
refresh/reimport, apart from the explicit installed-state/library-action setting,
and removal requires the separate opt-in automation policy. The local browser model provides
multi-selection, import status, confirmed complete-catalog imports, cancellation,
and partial-failure summaries. See [the import contract](library-imports.md).

The plugin exposes SDK `ISettings` and a WPF settings view. Its default-enabled
installed-state setting shares the versioned preferences file, loads once per
plugin lifetime, and applies only to owned marked imports. The adapter changes
`IsInstalled`/`IncludeLibraryPluginAction` on existing entries and reads the saved
choice for new creates. Save/Cancel and persistence failure rollback retain the
current effective choice. Newer schemas are not overwritten or used to rewrite
installed flags. Browser preference saves retain the setting.

`GetPlayActions` and `GetInstallActions` supply website controllers sharing
`LunaGamePage` URL/ownership validation. Play completes with a zero-duration
Stopped event and no Started event; Install completes through cancellation.
Both clear host transient states even after browser failure, without tracking
streaming sessions or modifying saved URL actions. Windows checks cover both
handoffs, settings UI/persistence, old/new imports, and preserved metadata.

`global.json`, committed NuGet lock files, pinned Python dependencies, and GitHub
Actions make offline validation repeatable: locked restore, Release builds for
both library targets plus the WPF plugin, 297 .NET tests, and 24 Python tests on
Linux and Windows. Windows additionally opens the actual WPF view in a dispatcher
smoke harness and packages a checked preview with the official Playnite 10.62
Toolbox. Playnite supplies both its SDK and JSON 10.0.3; the `net462` catalog
reuses that host assembly, with no JSON DLL or assembly loader bundled.
Standalone `net10.0` tools/tests retain JSON 13.0.3. Both targets bound JSON
depth to 64 and reject duplicate cache properties through a compatible reader.
A separate process loads the extracted package beside the official host assemblies
and round-trips representative data through validation/cache, checking a single
host JSON assembly and rejecting duplicate/deep cache input. These dependency
checks, compilation, WPF smoke checks, and packaging do not validate
loading inside Playnite.

Milestone B provides versioned caching, validated refreshes, recovery, and snapshot
comparisons while preserving the last validated snapshot after failures. With
Gate B passed, Milestone C's WPF browser consumes `CatalogService` and
has a documented Windows host checklist. Milestone D implements explicit imports
and reconciles `(region, LunaId)` mappings with Playnite's database. Windows is
required for host validation, actual library persistence, URL action behavior,
and final clean-install package testing. Gates C and D remain pending those checks.

`CatalogAutomation` is host-independent and runs after accepted network refreshes,
using `ICatalogAutomationDatabase` and the existing import service. Its strict,
atomic journal tracks ASIN baselines, retries, missing products, managed
tags, and website-open protection. The first successful refresh detecting an
absence can tag or remove the entry according to settings. Mutation methods recheck live ownership,
identity, history, and active state on the dispatcher. Cached/failed refreshes do
not invoke it. A background session host integrates the SDK library-update callback
with the same model busy/cancel state; it returns created games to Playnite.
See [automation and its regression/host checks](catalog-automation.md).
