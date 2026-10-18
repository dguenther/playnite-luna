# Runtime catalog data layer

Milestone B validates and caches current public US streaming data independently
of Playnite. `CatalogService` downloads through the existing C# provider;
the [Playnite browser](playnite-browser.md) supplies the plugin data directory
and calls the service.
The [Gate A investigation](streaming-completeness.md) established the browse
protocol. The Python audit remains optional research tooling.

## Validation contract

`CatalogValidator.ValidateSnapshot` creates a `ValidatedCatalog` only after:

- All Games, Standard, Premium, and GameNight each pass response validation with
  the expected page type, one browse page, no unresolved continuation, and
  observed tile counts covering the normalized games.
- Every query contains usable US identities, first-party links, and the correct
  membership observations; the marketplace is `ATVPDKIKX0DER`.
- All Games equals the category union, identities do not conflict, and the
  normalized snapshot matches the per-query observations. Requested research
  detail checks must also have passed if supplied.
- The snapshot is nonempty and its retrieval/validation timestamps are valid.

The resulting schema-1 catalog carries `validationContract:
"PublicUSStreamingBrowseV1"`, `coverageStatus: "Validated"`, and `isComplete:
true`. Completeness here means acceptance under the established runtime browse
contract. It does not mean the optional website/genre/search/product audit ran
again, or establish account entitlement. The research CLI retains its existing
`Unverified` / false export and exit 2; Python's `Verified` receipt remains bound
to its specific audited capture. No receipt is imported by the runtime service.

There is no fixed game-count threshold. Unknown browse pagination and empty
categories fail closed under the observed contract. Retain All/category-union
equality until new first-party evidence establishes legitimate unclassified
games. Unreliable records remain diagnostic and non-importable; the current
evidence does not require display-only records.

## Startup and refresh

The host constructs one service/cache pair for its plugin data directory:

```csharp
var cache = new JsonCatalogCache(pluginDataDirectory);
var catalogs = new CatalogService(provider, cache);
var startup = await catalogs.LoadAsync(cancellationToken);
var refreshed = await catalogs.RefreshAsync(cancellationToken);
```

`provider` is an `ILunaCatalogProvider`; use `LunaCatalogProvider` with an injected
`HttpClient`. Configure its handler with cookies and automatic redirects disabled,
an infinite client timeout (the provider applies its own deadlines), and normal
system proxy/TLS trust, as the research CLI already does.

`LoadAsync` performs no network requests. `RefreshAsync` first loads the cache if
necessary, fetches all required queries sequentially, validates and compares the
candidate, saves it, and only then publishes it. Overlapping operations on the
same service are serialized. A single service should own writes to a data
directory; cross-process writers are not coordinated.

`CatalogState` exposes data source (`None`, `Cache`, or `Network`), the catalog,
last successful retrieval time, refresh progress, cache recovery diagnostics,
refresh error, and optional observed changes. No cache yet is distinct from an
invalid cache and from retained stale data. Data becomes stale after 24 hours by
default (configurable), after a refresh error, or after backup recovery. Age is
evaluated when reading `State`; going stale never discards games. State returns a
copy of the catalog so callers cannot mutate the accepted baseline.

Provider observations carry a typed `FailureKind` (`None` or `Network`). The
service reports connection errors, request/body timeouts, response-stream I/O
errors, unsuccessful HTTP statuses, and deferred retries as
`CatalogErrorKind.Network` before validating the snapshot. This also handles
session-wide HTTP failures that stop the fetch before all four queries return.
Malformed JSON/UTF-8, response size violations, and catalog contract failures
remain `Validation`; `None` alone does not establish a valid response.

Validation, HTTP/transport, and storage failures retain previous games and their
timestamp and yield no changes. Caller cancellation remains `Cancelled`, retains
prior data, and propagates `OperationCanceledException`. An atomic replacement that already
committed counts as success even if cancellation arrives afterward.

## Cache and recovery

`ICatalogCache` separates storage from the provider and host. `JsonCatalogCache`
stores `catalog-US.json` with normalized games once, query summaries/ID sets,
retrieval and validation times, region/marketplace, and sanitized diagnostics.
It stores no raw API bodies, account credentials, research receipt, or import
mappings. Runtime caches are downloaded user data; no catalog is bundled.

Reads are capped at 8 MiB, JSON depth at 64, and strict UTF-8. Duplicate JSON
properties, trailing content, unsupported schemas/contracts, wrong scope, unsafe
links, conflicting IDs, missing query coverage, and malformed/empty data are
rejected. Deserialized status flags do not confer validity; the normalized data
and query summaries are validated again.

Writes validate before filesystem changes, write and flush a unique sibling
temporary file, and use `File.Move` for the first save or `File.Replace` for
replacement. These APIs are available in `net462`. A valid prior primary becomes
`catalog-US.json.bak`; load falls back to that validated backup if the primary is
missing or invalid. A later successful save never replaces a good backup with an
invalid primary. Failed/cancelled saves clean up their temporary file and retain
the accepted in-memory baseline. Orphan temporary files from a process crash are
ignored on load. Schema changes require an explicit future migration; unknown
versions are not rewritten during load or interpreted as games. A successful
runtime download can replace an invalid primary while retaining a valid backup.

## Comparisons and identity migration

`CatalogComparer` compares only validated catalogs for the same scope, rejecting
older candidates. It reports additions, removals, metadata/membership changes,
and identity migrations. Titles never define identity.

An ASIN-only ID gaining a native product ID with the same ASIN is one retained
game. `IdentityMigrations` records the old/new IDs for the future import service.
Both snapshots retain ASIN metadata, allowing later reconciliation after a
restart. A native ID changing its ASIN, a conflicting native ID for the same
ASIN, or a downgrade from native ID to ASIN-only is rejected pending evidence.
Mixed/conflicting identities within a single fetch still fail validation.

Reported removals are observations between accepted snapshots, not permission
to delete Playnite entries or a confirmed removal notification. Explicit
[library imports](library-imports.md) preserve those entries. Cross-refresh
removal confirmation and departed-game tags remain deferred.

## Verification and next step

The suite contains 262 .NET tests, including 88 production data-layer cases,
26 browser/artwork cases, and 51 import/mapping/browser cases, plus 24 Python checks. Production tests use the existing
five-game API samples
and targeted edge cases, with an injected HTTP handler and real temporary cache
files. CI builds both library targets and runs both suites on Linux and Windows.
See [validation](validation.md) for the live runtime smoke test and offline
failure/restart results.

Gate B passes for this data layer. Milestone C connects `CatalogService` to
a Playnite 10 WPF `LibraryPlugin`, with local search/filtering and cached offline
display. Windows CI checks WPF bindings and produces a Toolbox preview package;
host loading, real-host UI behavior, and clean installation remain Windows
Playnite checks before Gate C passes.
