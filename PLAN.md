# Amazon Luna Catalog Browser — implementation plan

Reconciled October 11, 2026 (UTC) against PR
[#5](https://github.com/dguenther/playnite-luna/pull/5) in
`dguenther/playnite-luna`. Evidence timestamps use UTC.

This is the working checklist reconciled with the uploaded implementation plan.
[docs/implementation-plan.md](docs/implementation-plan.md) already contains that
plan; its text matches the attachment after whitespace normalization. Keep it as
the original specification and update this file as milestones gain evidence.

**Current state: Gates A and B pass for the public US streaming investigation
and production data layer.** The repository has a reusable provider, validated
runtime refreshes, versioned cache/recovery, identity-aware comparisons, optional
research tools, representative fixtures, and CI. It has no Playnite extension,
browser UI, imports, or `.pext`. Proceed to Milestone C using the
[runtime data-layer contract](docs/catalog-data-layer.md).

A checked item below means implementation or linked evidence exists in this
working tree. Phase verdicts are stated separately; see
[the validation record](docs/validation.md) for the completed scoped audit and
earlier research runs.

## Goal and constraints

Build an open-source Playnite 10.x extension for Windows that browses the complete
US Amazon Luna cloud catalog and optionally imports selected games without an
Amazon account connection.

- Use C#, a `GenericPlugin`, WPF, and .NET Framework 4.6.2 for the extension.
  Retain the existing provider's `net462;net8.0` targets and .NET 8 research tooling.
- Obtain data directly from Amazon Luna; do not introduce third-party catalog
  providers. The undocumented first-party API is a dependency, not a stable contract.
- Show Standard, Premium, and GameNight as overlapping observed memberships,
  without claiming account entitlement.
- Use stable region-scoped identity, local search/filtering, explicit imports,
  and plugin-local versioned JSON storage.
- Preserve validated cached data and user metadata. Empty, failed, or incomplete
  responses must never imply mass removals.
- Keep Playnite 11, account authentication/entitlements, playtime synchronization,
  embedded streaming, PC giveaways, favorites/history sync, and discontinued
  Bring Your Own Library integrations outside MVP scope.

## A — Finish catalog proof of concept (Phase 0)

### Already present

- [x] Sequential anonymous `getPage` requests for All Games (`all_games`),
  Standard (`included_with_prime`), Premium (`luna_premium`), and GameNight
  (`game_night`), requesting `en_US` / marketplace `ATVPDKIKX0DER`.
- [x] Nested `GAME_TILE` parsing within the direct
  `pageMemberGroups.mainContent.widgets` collection; JSON-encoded or object
  `presentationData`; exclusion of cards in other collections.
- [x] Native `amzn1.adg.product` and `amzn1.luna.product` identifiers, ASIN fallback,
  region-scoped merging, category sets, artwork, genres, release year, publisher
  text, and direct Luna actions including Fortnite's slug-only URL.
- [x] Safe Luna URLs and first-party play-link ASIN/territory validation;
  conflicting identities invalidate the query or snapshot.
- [x] All Games versus category-union comparison, counts, pagination diagnostics,
  and up to five optional anonymous main-product/canonical-link detail checks.
- [x] Sanitized reduced response fixtures and repeatable CLI/test commands.
  Fixture replay contains five distinct games; it is not a full catalog database.
- [x] Fail closed for empty/malformed responses, unexpected page types, rejected
  tiles, unresolved continuation markers/actions, and reported totals exceeding
  observed tiles. Preserve existing exports and write separate failure diagnostics.
- [x] Confirm applied policy allows the frontend configuration host; distinguish
  anonymous My Games from public catalog tabs. No login was needed for public grids.
- [x] Capture the frontend-selected US endpoint and explicit `all_games` query;
  align provider requests with those observed fields.
- [x] Compare all four rendered browse grids in two fresh anonymous contexts:
  counts, IDs, titles, alphabetical boundaries, overlaps, and terminal scrolling.
  [Sanitized evidence](docs/evidence/2026-10-11-anonymous-browser.json) records no
  further browse-results calls; extra calls returned anonymous game details.
- [x] Commit an optional reproducible browser-evidence tool using normal proxy
  routing and verified Node TLS. Native Chromium certificate trust remains unfixed.

### Latest continuation

- [x] Recover normal proxy egress using supported command network permission;
  rerun the cookie-free US CLI with all five detail checks.
- [x] Repair browser cleanup hanging on background routes; require catalog
  requests to drain within 30 seconds. Rerun the exact final tool in two contexts,
  including every rendered card's probe link.
- [x] Repeat eight nonempty searches and loaded Home/GameNight discovery groups
  in two fresh contexts; compare rendered IDs/titles/links with API and All.
- [x] Render all five representative action pages independently twice, including
  Batman's `amzn1.luna.product` ID and Fortnite's slug-only URL. All document/detail
  requests returned 200 and identity/canonical/title/primary-label checks passed.
- [x] Distinguish main products, recommendation cards, DLC, editions, and marketing
  placement in observed views. Alien has eight `ADD_ON_TILE` widgets; Tetris's
  GameNight landing link does not imply filtered GameNight membership.
- [x] Commit [search/discovery](docs/evidence/2026-10-11-search-discovery.json)
  and the historical [search offset experiment](docs/evidence/2026-10-11-search-offsets.json).
- [x] Resolve the observed search ambiguity: frontend scrolling slices one loaded
  response; repeated `TITLE_A_TO_Z` offsets give 82 `a` and 39 `e` matches,
  identical to larger/reverse-sorted requests in two anonymous contexts.
  Exclude recommendations on explicit no-results pages. Commit
  [source evidence](docs/evidence/2026-10-11-search-frontend.json) and
  [validated traversals](docs/evidence/2026-10-11-stable-search-pagination.json).
  Retain pagination/export regression checks in CI.
- [x] Exclude Claims assets and PC giveaways from the streaming gate, per the
  clarified scope. Historical Claims failures do not block streaming work.

### Full streaming audit and verdict

- [x] Close the advertised route graph at 13 streaming pages in two fresh browser
  contexts, including the subscription listing, family-friendly filter, and all
  three GameNight collections. Visit every carousel slide and terminal grid.
- [x] Apply all 26 advertised genre filters anonymously; Home's My Games genre
  links are tested with the same filter applied to All Games.
- [x] Traverse all 36 letter/digit searches to explicit terminal states in two
  anonymous request contexts. Each union equals All's 118 IDs; reverse-title
  references match. Check the empty-query discovery recommendations separately.
- [x] Validate all 118 main products twice: native ID, ASIN, title, and canonical
  Luna URL. All 111 distinct recommendation IDs remain inside All. Final All
  rechecks in both title orders confirm the catalog stayed unchanged.
- [x] Commit a repeatable scoped audit and reduced evidence. Only a successful
  full audit can export the checked snapshot as `Verified` / `isComplete: true`;
  bind it to the source hash, scope, and verification time. Coverage failures
  preserve existing evidence and verified exports. The Python
  offline suite contains 24 tests, including eight full-audit regression checks.

Native Chromium trust remains unresolved; verified Node forwarding works and
does not repair it. This environment limitation does not block the scoped audit.

**Gate A:** programmatic anonymous retrieval, all necessary pages, demonstrated US
website coverage, stable identities, usable Luna links, explained memberships,
and committed repeatable evidence must all pass. The current evidence supports
the observed public US streaming catalog across the advertised route graph,
genre filters, search, and product details. **Gate A passes for this scope.**
Claims, PC giveaways, account libraries, and entitlement/playability are excluded.
The raw research fetch remains unverified; the audited research export carries
its specific receipt. Runtime downloads are accepted separately through the
Milestone B browse validation contract.
See [the verdict and evidence](docs/streaming-completeness.md).

## B — Complete the production data layer (Phase 1)

Depends on Gate A. Reuse existing models, provider abstraction, parsing, and
transport; do not restart them.

- [x] Replace the research-only `Unverified` / `IsComplete: false` contract with
  production refresh validation. Download the current API data through C# at
  runtime using the browse protocol established by Gate A; validate required
  queries, region, response structure, identities, and continuation/total fields.
  Retain retrieval time, marketplace, queried memberships, diagnostics, and schema
  version. The Python audit and its dated receipt are optional research artifacts;
  neither a full audit per refresh nor receipt import is a production prerequisite.
- [x] Reconcile the current strict All Games/category-union equality rule with
  production refresh validation. Gate A found equal sets, so the observed
  contract supports the current rule. If future evidence establishes legitimate
  unclassified games, retain them in All without inventing memberships.
- [x] Preserve native-ID-first identity with ASIN fallback. Define migration or
  reconciliation if an ASIN-only entry later gains a native ID; never merge by
  title or resolve conflicting identities silently.
- [x] Keep uncertain records diagnostic and non-importable. The source plan allows
  display-only records with unreliable identity; the current parser rejects them
  and invalidates the query. Add display-only handling only if observed records
  require it, without weakening the complete-snapshot gate.
- [x] Add a versioned validated JSON cache under the directory supplied by the host,
  through a storage abstraction that keeps the catalog library independent of
  Playnite. Support restart reload, schema checks, and malformed-file recovery.
- [x] Atomically replace the cache only after validation succeeds; preserve the
  previous snapshot after HTTP/parse/pagination/cancellation/write failures.
  Reuse the CLI's export approach where compatible, while implementing storage
  with APIs available in `net462`.
- [x] Expose last successful refresh, stale cached data, no cache yet, and refresh
  errors as distinct states. Load cached data when Luna is unavailable.
- [x] Add tests for validated-cache reload, atomic replacement and recovery,
  rejected partial/zero snapshots, region mismatch, and failed-refresh preservation;
  extend traversal/category tests according to Gate A. Continue using reduced,
  representative first-party API samples; no complete response/catalog fixture
  or fixed historical game count is required. Cache normalized runtime data.

**Gate B:** retrieval still works independently of Playnite; overlapping games
normalize once; a validated cache survives restart; failed refreshes preserve it;
and the data-layer acceptance tests pass. **Gate B passes.** The 85 production cases
use reduced first-party samples and real temporary cache files. A live runtime
download passed validation, reloaded all 118 games after restart, and retained
cache bytes after a simulated network failure. See the
[contract](docs/catalog-data-layer.md) and [validation](docs/validation.md).
The host will connect its plugin data directory in C; the research CLI remains
unverified. No new full audit or bulk fixture is required.

## C — Build the Playnite catalog browser (Phase 2)

Depends on Gate B; Windows is required for Playnite runtime verification.

- [ ] Add `src/LunaPlaynite` using the compatible Playnite 10 Toolbox template,
  targeting `net462`. Add the extension manifest, stable plugin ID, settings/data
  integration, and dependency packaging; verify loading inside the host.
- [ ] Add the main-menu entry **Amazon Luna Catalog Browser** and a WPF window.
  A sidebar shortcut is optional.
- [ ] Display the entire validated cached catalog with cover artwork, titles,
  multiple membership indicators, visible-game count, and last successful refresh.
- [ ] Add local title search, All/Standard/Premium/GameNight filters, and a stable
  default title sort. Filtering/sorting must not mutate the snapshot.
- [ ] Load/fetch asynchronously with cancellation and asynchronous image loading;
  keep the UI responsive and show cached/offline, loading, empty-cache, and
  refresh-failure states clearly.
- [ ] Add manual Refresh and Open in Luna using the default browser.
- [ ] Add Not imported / Imported indicators backed by the mapping and actual
  Playnite database reconciliation from Milestone D. Same-title Steam/GOG games
  must not count as Luna imports.
- [ ] Provide selection, Import Selected, and explicit Import All controls wired
  to Milestone D. Browser viewing must not require importing.

**Gate C:** the browser opens in Playnite Desktop Mode, shows all validated games
once, preserves membership overlap, searches/filters/sorts locally, opens Luna
pages, and stays responsive during refresh and offline use. The import-status
and import-control acceptance checks finish with Milestone D.

## D — Implement optional library imports (Phase 3)

Depends on the validated data and browser; finish the status integration in C.

- [ ] Add single and multi-selection imports through the Playnite SDK database API.
  Produce added/skipped/failed counts and actionable per-entry errors.
- [ ] Provide Import All with explicit confirmation of the scope and count.
  Define complete-catalog versus filtered-view scope clearly in the UI.
- [ ] Create entries with the correct title, an `Amazon Luna` source, a direct Luna
  link, an `Open in Luna` URL game action, relevant managed membership tags, and
  optional Luna artwork.
- [ ] Keep cloud games uninstalled by default and avoid assigning PC Windows solely
  because the extension runs on Windows. Use `GameActionType.URL`; defer any native
  Play-button/installed-state option until verified. Do not infer session duration.
- [ ] Persist versioned `(region, LunaId) -> Playnite game GUID` mappings atomically.
  Reconcile deleted entries before status checks/imports; where practical, recover
  mappings from trustworthy stored Luna URLs after plugin-state restoration.
- [ ] Make repeated imports idempotent, including bulk retries and partial failures.
  Never overwrite another library's same-title game or use titles as identity.
- [ ] Update only reliably extension-managed properties. Preserve title overrides,
  favorites, completion, playtime, notes, manual artwork, and user tags/categories;
  skip automatic updates when ownership cannot be established.
- [ ] Never automatically delete imported games for MVP. Removal detection/tagging
  remains deferred and must require complete successful same-region snapshots and
  confirmation across refreshes.
- [ ] Add tests for identity/mapping reconciliation, deleted games, repeated/bulk
  imports, partial failures, untouched non-Luna copies, and preserved user edits.
  Validate persistence and working URL actions inside Playnite.

**Gate D:** single/bulk imports persist across restart, repeated imports create no
duplicates, links work, existing libraries and user edits remain intact, and
failures produce useful summaries.

## E — Harden, verify, and package (Phase 4)

Depends on Gates A–D. Retain the existing offline suite and CI.

| Original automated-test requirement | Coverage already present | Remaining verification |
| --- | --- | --- |
| Recorded responses, nested tiles, malformed presentation data | Parser/detail fixtures and tests; canonical browse-collection restrictions | Extend fixtures for any newly observed contract. |
| Membership overlap, region identity, duplicate ASINs, title collisions | Parser/model/validation tests and production identity migration/conflict checks | Import behavior. |
| Pagination and incomplete results | Runtime browse validation, marker/action/total rejection, category-set disagreement, and 36-query alphabetical research traversal | Future browse continuation, if observed. |
| 401/403/429/500, timeouts, cancellation | Provider/transport tests; bounded retry/deferral, body deadlines/limits, UTF-8/depth checks | Regressions across any new request types. |
| Cache recovery and atomic replacement | Production restart, corruption/backup recovery, schema validation, cancellation, failed replacement, and byte preservation | Windows Playnite host integration. |
| Additions/removals across snapshots | Validated same-scope comparisons, migration, metadata changes, and failed-refresh safeguards | Import integration; no MVP deletion. |
| Import idempotency and stale mappings | No import implementation | Milestone D service tests plus Playnite database integration. |

- [x] .NET 8 SDK selection, committed NuGet lock files, locked restore, Release
  build, and offline tests in GitHub Actions.
- [x] Milestone B passes 182 .NET cases and Release builds for
  `net462` and `net8.0` with zero warnings/errors. CI covers Linux and Windows.
- [x] Twenty-four Python offline checks cover evidence safety, incomplete comparisons,
  context cleanup, bounded catalog draining, search termination, and unstable
  pages/reference disagreement, complete audit requirements, snapshot binding,
  and export protection; both offline suites run in CI.
- [ ] Complete the outstanding automated coverage above and retain sanitized,
  minimal fixtures rather than shipping a bulk catalog database.
- [ ] Run and record the source plan's manual scenarios on Windows/Playnite 10:
  first launch with no cache; offline opening; all-category search/filtering;
  successful refresh; single import; multi-import; repeated import; manual edits
  and deletion; restart/persistence; and clean-install package testing.
- [ ] Build a `.pext` using Playnite's supported Toolbox workflow; verify bundled
  `net462` dependencies, installation, plugin load, browser, URL actions, and
  imports on a clean Playnite installation. Linux compilation cannot close this gate.
- [ ] Update README with installation, supported versions, catalog/region scope,
  refresh/import behavior, undocumented API dependency, and known limitations.
  Update architecture/API/validation docs as production behavior changes.
- [ ] Review Amazon's applicable terms before public distribution. Include no
  credentials, unauthorized authentication bypass, or bulk catalog database.

**Gate E:** automated checks pass, manual results and limitations are recorded, and
a working `.pext` is generated and manually tested on a clean Playnite 10 install.

## MVP definition of done

These track the original final acceptance criteria, not just source scaffolding.

- [ ] Playnite installs and loads the extension.
- [ ] The extension retrieves a demonstrably complete US Luna cloud catalog.
- [ ] Standard, Premium, and GameNight memberships can be browsed.
- [ ] Every catalog game has a stable identity and a usable Luna link.
- [ ] Search and category filters work.
- [ ] Users can import individual games or multiple games.
- [ ] Repeated imports are idempotent.
- [ ] Existing non-Luna library entries remain untouched.
- [ ] The last successful catalog remains accessible offline.
- [ ] Failed or incomplete fetches never trigger mass removals.
- [x] Documentation and automated tests are included; remaining production
  coverage and release documentation are tracked in E.
- [ ] A working `.pext` is generated and manually tested.

## Reproduction and next increment

Use an installed .NET 8 SDK selected by `global.json`:

```sh
dotnet restore AmazonLuna.sln --locked-mode
dotnet build AmazonLuna.sln -c Release --no-restore
dotnet test tests/LunaCatalog.Tests/LunaCatalog.Tests.csproj -c Release --no-build
dotnet run --project tools/LunaCatalogProbe -c Release --no-build -- --fixtures tests/Fixtures --output artifacts/fixture-catalog.json
```

Fixture replay should return **2**, export five games, and retain
`isComplete: false` / `coverageStatus: "Unverified"`. It does not validate coverage.
The optional live research command is:

```sh
dotnet run --project tools/LunaCatalogProbe -c Release --no-build -- --live --verify-details --output artifacts/checked-us-catalog.json
```

A successful current live run also returns 2. Failures return 1 and write separate
diagnostics without replacing the existing export; invalid arguments return 64
and cancellation returns 130.

For the full scoped audit and optional verified export, follow
[README's streaming verification commands](README.md#verify-a-streaming-snapshot).
That audit returns 0 on success; the C# probe keeps its research exit code 2.

**Next increment:** Milestone C's Playnite 10 WPF browser, consuming the production
catalog service for cached startup and explicit runtime refresh. Keep search and
filters local, and validate the host on Windows. Follow C → D → E.

## Deferred improvements

After MVP: additional marketplaces, configurable automatic refresh, addition/
removal notifications, opt-in automatic new-game imports, departed-game status
tags, informational matches in other libraries, optional metadata enrichment, and
additional sorting/genre filters. None is required to close the MVP gates.

Reference URLs for Playnite SDK, Game Pass Catalog Browser, the n8n research
starting point, Luna, and its terms are retained in
[the source plan](docs/implementation-plan.md#12-reference-material).
