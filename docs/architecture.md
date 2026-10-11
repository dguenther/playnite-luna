# Catalog architecture

Milestone A / Gate A passes for the public US streaming catalog captured October
11, 2026. The [scoped verdict](streaming-completeness.md) records the evidence and
exclusions. Milestone B / Gate B passes for the production data layer; Milestone
C's Playnite browser is next.

`src/LunaCatalog` builds for `net462` and `net8.0`, with no Playnite or WPF
dependencies. It owns request construction, normalized models, recursive
response parsing, region-scoped identity, category-set merging, and diagnostics.
The HTTP client is injected for testable transport behavior. Requests use the
frontend-selected US endpoint and explicit `all_games` browse query.

`tools/LunaCatalogProbe` runs on .NET 8 and provides live anonymous retrieval and
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
the directory supplied by the future host. Versioned normalized games and query
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

`global.json`, committed NuGet lock files, pinned Python dependencies, and GitHub
Actions make offline validation repeatable: locked restore, Release builds for
both library targets, 182 .NET tests, and 24 Python tests on Linux and Windows.
Library compilation/testing does not validate Playnite host loading.

Milestone B provides versioned caching, validated refreshes, recovery, and snapshot
comparisons while preserving the last validated snapshot after failures. With
Gate B passed, Milestone C adds the WPF `GenericPlugin` using Playnite's Toolbox
template and consumes `CatalogService`. Milestone D adds explicit imports
and reconciles `(region, LunaId)` mappings with Playnite's database. Windows is
required for host validation and final `.pext` packaging. No import service,
Playnite plugin, or UI exists yet.
