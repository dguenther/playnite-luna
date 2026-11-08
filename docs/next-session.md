# Validate the Playnite browser and library imports

## Current state

Repository: `dguenther/playnite-luna`. PR
[#5](https://github.com/dguenther/playnite-luna/pull/5) merged into `main` as
`b3b29fa`; [PR #6](https://github.com/dguenther/playnite-luna/pull/6) merged as
`abd2260`. [PR #7](https://github.com/dguenther/playnite-luna/pull/7) merged as
`f85bde1`. Milestone D [PR #8](https://github.com/dguenther/playnite-luna/pull/8)
merged as `7c21d4f`. The inline sidebar follow-up
[PR #9](https://github.com/dguenther/playnite-luna/pull/9) merged as `77196e0`.
The Play/Install settings follow-up [PR #10](https://github.com/dguenther/playnite-luna/pull/10)
merged as `4e6180d`. Catalog automation is on `feature/luna-catalog-updates`, preview `0.2.7`. Check current
Git/PR state and preserve existing edits before resuming. No applicable
`AGENTS.md` was found during this work.

**Gates A and B pass** for the scoped public US streaming investigation and
production data layer. `src/LunaCatalog` targets `net462;net10.0` with no Playnite
dependencies. It now includes `CatalogValidator`, `ValidatedCatalog`,
`CatalogService`, `ICatalogCache` / `JsonCatalogCache`, and `CatalogComparer`.
See [the runtime contract](catalog-data-layer.md) and [architecture](architecture.md).
`src/LunaCatalogBrowser` provides a read-only local browsing model and bounded
artwork downloads. `src/LunaPlaynite` is a `net462` WPF `LibraryPlugin` using
SDK 6.17.0, matching the official Playnite 10 template. CI generates a Toolbox
preview package. Milestone D adds optional selected/bulk imports, atomically
persisted mappings, durable host recovery markers, and a production SDK adapter.
Host feedback confirmed basic opening and imported entries in `0.2.0`. The
`0.2.1` follow-up removes the heading row, inherits host list-item/theme colors,
registers **Amazon Luna** as a library, and migrates only marked manual imports
by changing library ownership. Explicit imports remain available. Preview `0.2.7` also refreshes on library
updates by default and offers independently controlled automatic library actions. Automatic metadata download has no public
SDK trigger for these imports; use Playnite's manual Download Metadata command.
The `0.2.2` follow-up adds a sidebar tab for inline browsing; `0.2.3` replaces its
placeholder moon with Luna's official circular logo. Preview `0.2.4` uses the
exact standalone official symbol path without a circle, embedded for offline
use, and inherits the sidebar's theme/selection foreground.
The user screenshot confirms basic inline rendering in Playnite. The sidebar and the
optional window have separate controls sharing one `CatalogBrowserSession`/model;
last-host departure cancels work, and reopening restores offline data and status.
**Gates C and D still need Windows Playnite host checks.**
See [the import contract](library-imports.md).

Preview `0.2.6` adds **Mark Luna games as installed**, enabled by
default, through SDK extension settings (merged PR #10). Save reconciles existing
owned marked games' `IsInstalled`/`IncludeLibraryPluginAction`; new creates read
the saved choice. Startup/open/library update also reconcile. Cancel preserves
the saved choice. The preferences file retains category/sort/window size and
backfills true when an older file lacks the new property. Future schemas block
settings writes and installed-state rewrites.

Enabled games use a native website Play controller; disabled games use the
Install website controller. Both share safe stored-URL/identity validation and
require no API fetch. Play completes with Stopped(0), without Started or stream
tracking; Install completes through SDK cancellation. Browser failures still
clear transient states. Saved URL actions and descriptive metadata are preserved.
Windows checks cover settings UI Save/Cancel, restart and save failure, old/new
imports, untouched other libraries, both handoffs, and browser preference saves.
Actual host behavior remains part of the user's C/D/E manual checks. Regional
endpoint discovery remains a researched proposal; runtime scope is still US.

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

The Desktop sidebar opens an inline catalog and the main menu opens an optional
separate window. Both share cached startup, local title
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
downgrades fail validation; titles never define identity. The import service
reconciles stable IDs and ASIN metadata without deleting Playnite games based on
a single observed removal.

## Verification

- Locked restore and Release builds for both library targets pass with zero
  warnings/errors, including WPF and the Windows smoke harness. All 286 .NET
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
- Fifty-one import/mapping/browser cases cover representative single/bulk
  imports, repeated/partial/concurrent retries, deletion/recovery, migration and
  conflicts, metadata preservation, cancellation, bounded state parsing, and
  confirmed full-catalog scope. Windows exercises the production adapter with
  actual SDK models and an isolated database API. Real-host persistence is pending.
  The current adapter recognizes both modern library-owned and legacy manual
  markers. Windows also checks theme resource changes and migration without
  overwriting edits or adopting foreign/unmarked games.
- Five session lifecycle cases cover reopening, shared hosts, rapid cancellation,
  duplicate import prevention, and retaining commits during shutdown. Windows
  invokes actual sidebar callbacks and checks inline/window coexistence,
  selection/filters, settings persistence, Desktop-only registration, and shutdown.
  Its icon checks cover offline vector loading, transparent surroundings/center,
  and inherited theme/selection colors; the installed archive checks the exact
  geometry resource hash.
- The `net462` build shares Playnite 10.62's JSON 10.0.3; no JSON DLL or private
  loader is packaged. Standalone .NET 10 keeps JSON 13.0.3. The independent
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

**Finish Gates C and D's Windows host checks, then proceed to Milestone E.** Download the
`luna-playnite-preview` CI artifact for `feature/luna-catalog-updates`, or use `tools/PackageLuna.ps1` with
an installed Playnite 10 Toolbox. Follow the [host checklist](playnite-browser.md#outstanding-windows-host-checks),
record actual loading/theme/browser/offline/restart results, and keep Gate C
pending until those pass. Also follow [the import checklist](library-imports.md#verification-and-remaining-host-checks).
Playnite 10.62 is the preview packaging target.

Milestone D implements optional selected/bulk imports and Imported/Not imported status,
backed by versioned `(region, LunaId) -> Playnite GUID` mappings reconciled with
the real database. Same-title copies in another library never establish Luna
identity. Preserve user edits and keep catalog viewing independent of importing.
Automatic removal is now explicitly authorized through an off-by-default setting,
subject to absence from the first validated refresh and ownership/history/open-protection checks. Follow [PLAN.md](../PLAN.md).

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

Current automation defaults: refresh on library update and expired tagging on;
new-arrival imports and unplayed expired-game removal off, per user clarification.
First download establishes a baseline; do not auto-import the entire catalog.
Opening via Play/Install/catalog records removal protection without adding playtime.
The strict `catalog-automation-US.json` journal must be writable before mutations;
corrupt/newer state blocks them. Windows checks invoke the actual background SDK
callback with an injected first-party fixture provider and exercise API opt-out,
cancellation, failure retention, tagging metadata, history guards, and SDK deletion.
Actual Playnite behavior still requires the user's host checks. Regional endpoint
discovery remains a proposal; these settings retain the US catalog scope.
