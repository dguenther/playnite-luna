# Validate the Playnite browser and continue with imports

## Current state

Repository: `dguenther/playnite-luna`. PR
[#5](https://github.com/dguenther/playnite-luna/pull/5) merged into `main` as
`b3b29fa`; [PR #6](https://github.com/dguenther/playnite-luna/pull/6) merged as
`abd2260`. Milestone C is in draft
[PR #7](https://github.com/dguenther/playnite-luna/pull/7), branch
`feature/playnite-catalog-browser`. Check current
Git/PR state and preserve existing edits before resuming. No applicable
`AGENTS.md` was found during this work.

**Gates A and B pass** for the scoped public US streaming investigation and
production data layer. `src/LunaCatalog` targets `net462;net8.0` with no Playnite
dependencies. It now includes `CatalogValidator`, `ValidatedCatalog`,
`CatalogService`, `ICatalogCache` / `JsonCatalogCache`, and `CatalogComparer`.
See [the runtime contract](catalog-data-layer.md) and [architecture](architecture.md).
`src/LunaCatalogBrowser` provides a read-only local browsing model and bounded
artwork downloads. `src/LunaPlaynite` is a `net462` WPF `GenericPlugin` using
SDK 6.17.0, matching the official Playnite 10 template. CI generates a Toolbox
preview package. **Gate C still needs Windows Playnite Desktop host checks.**
There is no import service or import-status mapping yet.

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

The Desktop main menu opens one catalog window with cached startup, local title
search, All/Standard/Premium/GameNight filters, deterministic title/ID sorting,
membership overlap, counts, refresh time, and Open in Luna. Refresh/cancel leave
browsing available; failed refreshes retain previous data. Preferences save
category/sort/window size through Playnite's settings API. Artwork is optional,
bounded, decoded on workers, and cached only in memory; an offline restart may
show placeholders. Stable plugin GUID:
`6f4229ca-70a9-4c65-ae9f-047a367beda9`. See [browser details](playnite-browser.md).

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
  warnings/errors, including WPF and the Windows smoke harness. All 211 .NET
  tests and 24 Python offline checks pass.
- The 88 production data-layer cases cover validation, restart reload, malformed/schema
  checks, backup recovery, atomic replacement, cancellation/write failures,
  serialized refreshes, metadata differences, and identity migration/conflicts.
  HTTP-provider regressions cover connection/body failures, timeouts, error
  statuses, deferred retries, invalid responses, and caller cancellation while
  preserving the prior cache bytes and timestamp.
  Twenty-six browser/artwork cases cover representative local views, selection,
  cancellation, real-provider offline retention, bounded artwork, and concurrency.
  CI covers Linux and Windows, with actual WPF bindings/dispatcher smoke checks
  and official Toolbox packaging on Windows. It does not load inside Playnite.
- The `net462` build shares Playnite 10.62's JSON 10.0.3; no JSON DLL or private
  loader is packaged. Standalone .NET 8 keeps JSON 13.0.3. The independent
  installed-package check verifies host reuse and duplicate/depth rejection.
  The WPF harness checks that a late obsolete artwork failure cannot clear a
  recycled row's replacement image.
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

**Finish Gate C's Windows host checks, then start Milestone D.** Download the
`luna-playnite-preview` CI artifact from PR #7, or use `tools/PackageLuna.ps1` with
an installed Playnite 10 Toolbox. Follow the [host checklist](playnite-browser.md#outstanding-windows-host-checks),
record actual loading/theme/browser/offline/restart results, and keep Gate C
pending until those pass. Playnite 10.62 is the preview packaging target.

Milestone D adds optional selected/bulk imports and Imported/Not imported status,
backed by versioned `(region, LunaId) -> Playnite GUID` mappings reconciled with
the real database. Same-title copies in another library never establish Luna
identity. Preserve user edits, never delete games automatically, and keep catalog
viewing independent of importing. Follow [PLAN.md](../PLAN.md).

Windows Playnite is required to validate SDK integration, host loading, responsive
UI, and clean-install packaging. Linux builds, WPF smoke checks, and generated
packages alone do not close Gate C. Keep representative fixtures and verify import
seams separately from API downloads. Native Chromium trust remains unresolved in this cloud
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
