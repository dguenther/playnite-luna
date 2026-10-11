# Continue with the Playnite catalog browser

## Current state

Repository: `dguenther/playnite-luna`. PR
[#5](https://github.com/dguenther/playnite-luna/pull/5) merged into `main` as
`b3b29fa`. Milestone B is in
[PR #6](https://github.com/dguenther/playnite-luna/pull/6), branch
`feature/catalog-cache-refresh`. Check current
Git/PR state and preserve existing edits before resuming. No applicable
`AGENTS.md` was found during this work.

**Gates A and B pass** for the scoped public US streaming investigation and
production data layer. `src/LunaCatalog` targets `net462;net8.0` with no Playnite
dependencies. It now includes `CatalogValidator`, `ValidatedCatalog`,
`CatalogService`, `ICatalogCache` / `JsonCatalogCache`, and `CatalogComparer`.
See [the runtime contract](catalog-data-layer.md) and [architecture](architecture.md).
There is no Playnite plugin/UI, import service, or `.pext` yet.

The browser will download current API data directly through C# at runtime.
`CatalogService.LoadAsync` opens the cache without networking;
`RefreshAsync` fetches, validates, compares, saves, and then publishes a candidate.
It retains previous games and timestamps after failures, with no removal list.
Typed provider failures preserve the distinction between network/HTTP errors
and invalid catalog data, including partial fetches stopped by 401/403/429.
Cancellation propagates without discarding cached data. Unique temporary files
and atomic replacement protect writes; a validated backup recovers corrupt or
missing primary data. The host supplies the plugin data directory and owns a
single service/cache pair. Cross-process writers are not coordinated.

Runtime catalogs are `Validated` / complete under `PublicUSStreamingBrowseV1`.
The research CLI remains `Unverified` / false, with successful exit 2. Python's
full audit remains optional research verification, producing a `Verified` receipt
for its exact checked capture. Neither a full audit on each refresh nor receipt
import is required. Tests use reduced first-party samples; no complete API
response/catalog fixture or bundled catalog is needed.

The parser/validator retain All/category-union equality and reject empty queries,
unknown browse continuation, wrong scope, unsafe links, and conflicting identities.
There is no historical game-count threshold. ASIN-only IDs gaining native IDs
produce migration records rather than additions/removals. Native-ID conflicts and
downgrades fail validation; titles never define identity. The future import
service must consume/reconcile stable IDs and ASIN metadata without deleting
Playnite games based on a single observed removal.

## Verification

- Locked restore and Release builds for both library targets pass with zero
  warnings/errors. All 182 .NET tests and 24 Python offline checks pass.
- The 85 production cases cover validation, restart reload, malformed/schema
  checks, backup recovery, atomic replacement, cancellation/write failures,
  serialized refreshes, metadata differences, and identity migration/conflicts.
  HTTP-provider regressions cover connection/body failures, timeouts, error
  statuses, deferred retries, invalid responses, and caller cancellation while
  preserving the prior cache bytes and timestamp.
  CI now covers Linux and Windows. It does not test loading inside Playnite.
- A live runtime download at October 11, 2026 **16:45:02 UTC** returned All 118,
  Standard 90, Premium 118, and GameNight 50, all HTTP 200 without continuation.
  Validation and cache save passed. A restarted service loaded 118 games, and a
  simulated offline refresh preserved the cache byte for byte and marked it stale.
  See [reduced results](evidence/2026-10-11-runtime-cache.json).
- Gate A's full research audit completed at **15:24:11 UTC**: two contexts checked
  all 13 streaming pages, 27 carousels, 26 advertised genres, 36 paginated searches,
  and 118 main products. No streaming omissions/extras were found. See
  [the verdict](streaming-completeness.md) and
  [audit receipt](evidence/2026-10-11-public-streaming-audit.json).
- Claims/PC giveaways, account libraries, entitlement, and streaming eligibility
  remain excluded. No login or streaming launch was attempted. Full runtime caches,
  research snapshots, and the smoke harness remain ignored; only reduced results
  and existing representative fixtures are committed.

## Next work

**Start Milestone C.** Use the compatible Playnite 10 Toolbox template for a
`GenericPlugin` and WPF catalog browser. Connect `CatalogService` to the plugin
data directory, show cached data immediately, and expose explicit asynchronous
refresh. Keep title search, category filters, and sorting local. Show overlapping
memberships, artwork, visible counts, stale/offline/error state, last successful
refresh, and Open in Luna actions. Preserve imported/user data; import operations
belong to Milestone D. Follow the checklist in [PLAN.md](../PLAN.md).

Windows Playnite is required to validate SDK integration, host loading, responsive
UI, and final packaging. Linux builds and Windows .NET tests alone do not close
Gate C. Retain representative fixture tests and validate new UI seams separately
from API downloads. Native Chromium trust remains unresolved in this cloud
environment; verified Node forwarding works for optional research tools and does
not repair native trust. It is not a production C# refresh dependency.

## Reproduce

Read the managed cloud runtime skill, environment status, and executor policy
before network work. Supported network permission is needed for shell networking
and .NET build/test IPC in this environment. Preserve the inherited proxy and
TLS/CA trust. The SDK is at `/workspace/.dotnet/dotnet`; use task-local
`DOTNET_CLI_HOME=/workspace/work/dotnet-cli` and
`NUGET_PACKAGES=/workspace/.nuget/packages`. These are environment paths, not
repository dependencies.

```sh
dotnet restore AmazonLuna.sln --locked-mode
dotnet build AmazonLuna.sln -c Release --no-restore
dotnet test tests/LunaCatalog.Tests/LunaCatalog.Tests.csproj -c Release --no-build
python3 -m unittest discover -s tools/LunaBrowserEvidence -v
```

Research replay/live commands and expected exit codes are in
[README](../README.md); optional browser setup is in
[the tool instructions](../tools/LunaBrowserEvidence/README.md).
[Validation](validation.md) retains earlier research history. Keep
`docs/implementation-plan.md` as the original specification and update `PLAN.md`
as production milestones gain evidence.
