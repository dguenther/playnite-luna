# Continue the Luna production data layer

## Current state

Repository: `dguenther/playnite-luna`; PR [#5](https://github.com/dguenther/playnite-luna/pull/5),
branch `research/anonymous-frontend-retest`. Gate A evidence was captured October
11, 2026 (UTC). Check current Git/PR state and preserve existing edits before
resuming. No applicable `AGENTS.md` was found during the recorded investigation.

The earlier environment-wide Envoy outage did **not** recur. Supported network
permission was needed for shell commands, including .NET build/test IPC. An
initial command without that permission reported a connection refusal; with it,
Git, NuGet, Luna, and the US API worked through the inherited proxy. This was not
a reason to change routing, disable TLS checks, or request account login.

The provider targets `net462;net8.0`, uses the frontend-selected US endpoint and
`all_games`, and exports anonymous research snapshots. There is no production
cache, Playnite UI/plugin, import implementation, or `.pext`. **Gate A passes for
the recorded public US streaming scope.** The October 11 snapshot was fully
audited and exported as `Verified` / complete with a scoped, hash-bound receipt.
The current research CLI exports `Unverified` / `IsComplete: false` by default.
The production browser will download current API data through C# and validate
each refresh against the established browse contract. The full Python audit is
optional research tooling, not a requirement for every runtime download. Failed
checks preserve prior outputs. Proceed to Milestone B; see
[the verdict](streaming-completeness.md).

## Completed streaming audit

- Cookie-free C# requests returned All 118, Standard 90, Premium 118, GameNight 50;
  category union equals All. Separate browse comparisons matched every rendered
  ID, title, and action link in all four grids in two fresh browser contexts.
  Stable bottoms triggered detail prefetches, with no further browse results.
- The full audit closed the advertised graph at 13 streaming pages and visited
  all 27 carousel/swiper instances (229 slide positions) in each of two fresh
  browser contexts. All 26 advertised genre filters were checked in two anonymous
  API contexts, with matching sets and no IDs outside All.
- All 36 letter/digit searches were traversed to explicit terminal states in
  both API contexts. Page IDs/order and reverse-title references agree; each
  union equals All's 118 IDs. Empty-query recommendations were checked separately.
  Use `TITLE_A_TO_Z`, advance by requested size even after a short page, and stop
  only on the explicit empty-search state with `SEARCH_NO_RESULTS_FOUND`.
  The frontend's relevance search scrolls a single loaded page.
- Every one of the 118 main products passed ID/ASIN/title/canonical checks twice;
  all 111 distinct recommendation IDs remain inside All. Final All rechecks in
  both title orders confirm no catalog change. Detail labels were 90 “Play free
  with Prime” and 28 “Included in Luna Premium”; these are anonymous observations,
  not entitlement or streaming eligibility.
- The exact final full audit returned 0 and exported its checked snapshot as
  verified research JSON. Runtime C# refresh validation remains Milestone B work;
  it does not require importing this receipt. Existing checks comprise 97 .NET
  tests and 24 Python offline tests, all included in CI.
- Claims assets and PC giveaways are excluded from the streaming gate. No account
  login or streaming launch was attempted.

Earlier [sampled browser checks](evidence/2026-10-11-search-discovery.json) also
rendered eight searches and five representative action pages twice, including
Batman and Fortnite. That capture recorded Tetris® Effect: Connected as an extra
GameNight marketing link outside filtered membership; marketing placements do
not redefine memberships. Alien's `ADD_ON_TILE` DLC remains separate from main
products. Keep advertised editions, including the WRC sample. The repaired
browser cleanup ignores late background-route errors while requiring catalog
requests to drain within 30 seconds.

See [validation](validation.md), [API findings](luna-api.md), and the reduced
[browse](evidence/2026-10-11-anonymous-browser.json),
[search/discovery](evidence/2026-10-11-search-discovery.json), and
[initial offset experiment](evidence/2026-10-11-search-offsets.json),
[frontend source](evidence/2026-10-11-search-frontend.json), and
[validated search pagination](evidence/2026-10-11-stable-search-pagination.json),
and [full streaming audit](evidence/2026-10-11-public-streaming-audit.json)
evidence. No raw
responses, bulk catalog, cookies, account headers, guest tokens, or storage state
are committed.

## Next work

1. **Start Milestone B.** Build the versioned production cache, refresh/recovery,
   and snapshot comparison behavior. Download API data directly through C# at
   runtime, validate the established browse contract on each refresh, and cache
   normalized data. Preserve prior cached data on failed validation; HTTP success
   alone is insufficient. Keep tests on reduced representative API samples and
   edge cases; no complete saved response/catalog fixture is needed. Python audit
   receipts are optional research evidence, not runtime inputs or prerequisites.
   Keep Playnite search local. The count 118 is historical, not a permanent
   threshold. Claims/account inventories remain out of scope. See `PLAN.md` for
   independently testable next increments.
2. Native Chromium still reports `ERR_CERT_AUTHORITY_INVALID`. Verified Node
   forwarding works but does not repair native trust. Keep configured proxy/CA
   settings and certificate verification; do not change unrelated home profiles.

## Reproduce

The SDK is at `/workspace/.dotnet/dotnet` in this environment. These paths are
environment state, not repository dependencies. Use task-local
`DOTNET_CLI_HOME=/workspace/work/dotnet-cli` and
`NUGET_PACKAGES=/workspace/.nuget/packages` when using that SDK. Read the cloud
runtime skill, environment status, and executor policy first. Grant commands the
supported network permission; preserve inherited proxy variables and CA trust.

```sh
dotnet restore AmazonLuna.sln --locked-mode
dotnet build AmazonLuna.sln -c Release --no-restore
dotnet test tests/LunaCatalog.Tests/LunaCatalog.Tests.csproj -c Release --no-build
dotnet run --project tools/LunaCatalogProbe -c Release --no-build -- --fixtures tests/Fixtures --output artifacts/fixture-catalog.json
dotnet run --project tools/LunaCatalogProbe -c Release --no-build -- --live --verify-details --output artifacts/us-catalog.json
python3 -m unittest discover -s tools/LunaBrowserEvidence -v
python3 tools/LunaBrowserEvidence/probe.py --probe-snapshot artifacts/us-catalog.json --output artifacts/browser-evidence.json
python3 tools/LunaBrowserEvidence/discovery.py --probe-snapshot artifacts/us-catalog.json --output artifacts/discovery-evidence.json
python3 tools/LunaBrowserEvidence/search_offsets.py --probe-snapshot artifacts/us-catalog.json --output artifacts/search-offsets.json
python3 tools/LunaBrowserEvidence/streaming_audit.py --probe-snapshot artifacts/us-catalog.json --output artifacts/streaming-evidence.json --catalog-output artifacts/verified-us-catalog.json
```

Research probes return 2 on successful checks, 1 on failures. The full scoped
audit returns 0 only when every required check passes. Full snapshots
belong in ignored `artifacts/`. See [browser tool setup](../tools/LunaBrowserEvidence/README.md)
for pinned dependencies. CI now runs the Python offline checks without installing
or launching a browser. Read `PLAN.md` as the reconciled checklist and
`docs/implementation-plan.md` as the original specification.
