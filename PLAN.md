# Amazon Luna Catalog Browser — implementation plan

Reconciled October 18, 2026 (America/New_York) after merged PRs
[#5](https://github.com/dguenther/playnite-luna/pull/5) and
[#6](https://github.com/dguenther/playnite-luna/pull/6), and
[#7](https://github.com/dguenther/playnite-luna/pull/7), and
[#8](https://github.com/dguenther/playnite-luna/pull/8), and the inline sidebar
follow-up [PR #9](https://github.com/dguenther/playnite-luna/pull/9), merged as
`77196e0`. The Play/Install settings follow-up [PR #10](https://github.com/dguenther/playnite-luna/pull/10)
merged as `4e6180d`. Catalog automation is on `feature/luna-catalog-updates` in
`dguenther/playnite-luna`. Evidence timestamps use UTC.

This is the working checklist reconciled with the uploaded implementation plan.
[docs/implementation-plan.md](docs/implementation-plan.md) already contains that
plan; its text matches the attachment after whitespace normalization. Keep it as
the original specification and update this file as milestones gain evidence.

**Current state: Gates A and B pass for the public US streaming investigation
and production data layer.** The repository has a reusable provider, validated
runtime refreshes, versioned cache/recovery, identity-aware comparisons, optional
research tools, representative fixtures, and CI. Milestone C now adds a Playnite
WPF browser, a testable local browsing model, and Toolbox preview packaging.
Milestone D adds reconciled import status, single/multi-selection imports, and
confirmed full-catalog imports in preview `0.2.7`, with **Library → Amazon Luna**
ownership and migration from marked manual imports. Initial host opening/imports
were reported working; the remaining host checks stay open.
The sidebar follow-up shares browsing state between an inline Desktop view and
the optional separate window, with coordinated cancellation and lazy startup.
The sidebar uses Luna's exact standalone official symbol as an embedded offline
vector resource, without a circle and with the host's theme/selection foreground.
The user screenshot confirms basic inline host rendering; broader checks remain.
The default-enabled installed-state setting marks owned imports installed and
shows Play; disabling it shows Install. Both buttons open the stored Luna page,
and the setting applies to existing/new entries. Host checks cover both modes.
**Gates C and D remain pending Windows Playnite host validation.** See
[browser behavior and host checks](docs/playnite-browser.md),
[library imports](docs/library-imports.md), and the
[runtime data-layer contract](docs/catalog-data-layer.md).

A checked item below means implementation or linked evidence exists in this
working tree. Phase verdicts are stated separately; see
[the validation record](docs/validation.md) for the completed scoped audit and
earlier research runs.

## Goal and constraints

Build an open-source Playnite 10.x extension for Windows that browses the complete
US Amazon Luna cloud catalog and optionally imports selected games without an
Amazon account connection.

- Use C#, a `LibraryPlugin`, WPF, and .NET Framework 4.6.2 for the extension.
  The original specification used `GenericPlugin`; host feedback requested an
  Amazon Luna Library group, which requires library registration. Imports remain
  explicit browser actions plus optional arrival-only automatic imports; library
  updates refresh by default.
  Keep the provider's `net462;net10.0` targets and .NET 10 research tooling.
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
The Milestone C host connects its plugin data directory; the research CLI remains
unverified. No new full audit or bulk fixture is required.

## C — Build the Playnite catalog browser (Phase 2)

Depends on Gate B; Windows is required for Playnite runtime verification.

- [x] Add `src/LunaPlaynite` using the compatible Playnite 10 Toolbox template,
  targeting `net462`. Add the extension manifest, stable plugin ID, settings/data
  integration, and dependency packaging. SDK 6.17.0 matches the official template.
- [x] Verify basic loading inside the Playnite Desktop host on Windows (user report).
  Theme readability, restart, and clean-install checks remain pending.
- [x] Add the main-menu entry **Amazon Luna Catalog Browser** and a WPF window.
- [x] Add the Desktop sidebar entry **Browse Amazon Luna Catalog** opening the
  browser inline. Keep the optional window, share its model, and manage view lifetime.
- [x] Display the entire validated cached catalog with cover artwork, titles,
  multiple membership indicators, visible-game count, and last successful refresh.
- [x] Add local title search, All/Standard/Premium/GameNight filters, and a stable
  default title sort. Filtering/sorting must not mutate the snapshot.
- [x] Load/fetch asynchronously with cancellation and asynchronous image loading;
  keep the UI responsive and show cached/offline, loading, empty-cache, and
  refresh-failure states clearly.
- [x] Add manual Refresh and Open in Luna using the default browser.
- [x] Add Not imported / Imported indicators backed by the mapping and actual
  Playnite database reconciliation from Milestone D. Same-title Steam/GOG games
  must not count as Luna imports.
- [x] Provide selection, Import Selected, and explicit Import All controls wired
  to Milestone D. Browser viewing must not require importing.

**Gate C:** the browser opens in Playnite Desktop Mode, shows all validated games
once, preserves membership overlap, searches/filters/sorts locally, opens Luna
pages, and stays responsive during refresh and offline use. The import-status
and import-control acceptance checks finish with Milestone D.

**Implementation status:** the browser and full solution compile on Linux and
Windows targets. Twenty-six new model/artwork cases use representative API data.
The Windows WPF smoke harness checks actual bindings, selection, cancellation,
dispatcher notifications, image decoding, and cache restart without live Amazon
requests. CI packages a preview with official Playnite 10.62 Toolbox and checks
staging/archive dependencies. These checks do not establish Playnite host loading;
the [manual checklist](docs/playnite-browser.md#outstanding-windows-host-checks)
still gates C. Milestone D implements import/status controls and extends the
Windows harness with SDK database adapter checks; actual host checks remain open.

## D — Implement optional library imports (Phase 3)

Depends on the validated data and browser; finish the status integration in C.

- [x] Add single and multi-selection imports through the Playnite SDK database API.
  Produce added/skipped/failed counts and actionable per-entry errors.
- [x] Provide Import All with explicit confirmation of the scope and count.
  Define complete-catalog versus filtered-view scope clearly in the UI.
- [x] Create entries with the correct title, an `Amazon Luna` source, a direct Luna
  link, an `Open in Luna` URL game action, relevant managed membership tags, and
  optional Luna artwork.
- [x] Provide a default-enabled setting that marks owned Luna entries installed
  and supplies a native Play controller. Disabled entries remain uninstalled
  with an Install controller. Both open the stored website URL, clear transient
  states, and infer no session duration. Apply changes to existing and new imports,
  preserving descriptive metadata and saved actions. Infer no Windows platform.
- [x] Persist versioned `(region, LunaId) -> Playnite game GUID` mappings atomically.
  Reconcile deleted entries before status checks/imports; where practical, recover
  mappings from validated durable Luna identity markers after plugin-state
  restoration. Unmarked URLs alone cannot establish ownership safely.
- [x] Make repeated imports idempotent, including bulk retries and partial failures.
  Never overwrite another library's same-title game or use titles as identity.
- [x] Update only reliably extension-managed properties. Preserve title overrides,
  favorites, completion, playtime, notes, manual artwork, and user tags/categories;
  skip automatic updates when ownership cannot be established. This implementation
  preserves existing metadata. The `0.2.1` upgrade changes only library ownership
  for earlier manual entries with a valid Luna marker.
- [x] Keep automatic deletion off by default. The authorized automation follow-up
  adds expired tags by default and opt-in unplayed removal on the first complete
  successful same-region refresh, requiring ownership/history/activity/open protection,
  and safe persisted state. Add new-arrival import as a separate off-by-default option.
- [x] Add tests for identity/mapping reconciliation, deleted games, repeated/bulk
  imports, partial failures, untouched non-Luna copies, and preserved user edits.
- [ ] Validate persistence and working URL actions inside Playnite.

**Implementation status:** 51 new import/mapping/browser cases bring the suite
to 262 .NET tests; five sidebar/session lifecycle cases bring the current total
to 267. The Windows WPF harness uses real SDK models and the production
adapter through an isolated database API. The package compatibility process checks
mapping persistence with host Newtonsoft JSON 10. These checks support the
implementation; [actual host checks](docs/library-imports.md#verification-and-remaining-host-checks)
still gate D.

**Gate D:** single/bulk imports persist across restart, repeated imports create no
duplicates, links work, existing libraries and user edits remain intact, and
failures produce useful summaries.

## E — Harden, verify, and package (Phase 4)

Depends on Gates A–D. Retain the existing offline suite and CI.

| Original automated-test requirement | Coverage already present | Remaining verification |
| --- | --- | --- |
| Recorded responses, nested tiles, malformed presentation data | Parser/detail fixtures and tests; canonical browse-collection restrictions | Extend fixtures for any newly observed contract. |
| Membership overlap, region identity, duplicate ASINs, title collisions | Parser/model/validation tests and production/import identity migration/conflict checks | Windows Playnite host integration. |
| Pagination and incomplete results | Runtime browse validation, marker/action/total rejection, category-set disagreement, and 36-query alphabetical research traversal | Future browse continuation, if observed. |
| 401/403/429/500, timeouts, cancellation | Provider/transport tests; bounded retry/deferral, body deadlines/limits, UTF-8/depth checks | Regressions across any new request types. |
| Cache recovery and atomic replacement | Production restart, corruption/backup recovery, schema validation, cancellation, failed replacement, and byte preservation | Windows Playnite host integration. |
| Additions/removals across snapshots | Validated same-scope comparisons, migration, metadata changes, failed-refresh safeguards, and no deletion of imported entries | Windows Playnite host integration. |
| Import idempotency and stale mappings | Milestone D service, mapping-file, browser, and SDK adapter checks | Actual Playnite database persistence, restart, and action behavior. |

- [x] .NET 10 SDK selection, committed NuGet lock files, locked restore, Release
  build, and offline tests in GitHub Actions.
- [x] Milestone B passes 182 .NET cases and Release builds for
  `net462` and `net8.0` with zero warnings/errors. CI covers Linux and Windows.
- [x] Milestone C expands the suite to 211 .NET cases after review, adds full WPF compilation,
  Windows dispatcher/binding smoke checks, and official Toolbox preview packaging.
- [x] Milestone D expands the suite to 262 .NET cases with 51 import cases and
  extends Windows SDK/WPF and host JSON compatibility checks for imports.
- [x] The inline sidebar follow-up adds five session lifecycle regressions (267
  .NET total) and Windows checks for real sidebar callbacks and shared WPF hosts.
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
- [x] Add `.pext` generation using Playnite's supported Toolbox workflow, with
  checks for staged dependencies and shared host assemblies.
- [ ] Verify installation, plugin load, browser, URL actions, and imports on a
  clean Playnite installation. Automated compilation/packaging cannot close this gate.
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

Use an installed .NET 10 SDK selected by `global.json`:

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

**Next increment:** finish the actual Windows Playnite host checks for browser
and imports, then Milestone E hardening, clean-install validation, and packaging.
Keep runtime fetching and representative tests; no bulk catalog fixture is needed.

## Deferred improvements

After MVP: additional marketplaces, scheduled refresh beyond library updates,
addition/removal notifications, informational matches in other libraries,
optional metadata enrichment, and
additional sorting/genre filters. None is required to close the MVP gates.

Reference URLs for Playnite SDK, Game Pass Catalog Browser, the n8n research
starting point, Luna, and its terms are retained in
[the source plan](docs/implementation-plan.md#12-reference-material).

## Authorized catalog automation follow-up

Preview `0.2.7` implements default-enabled library-update refresh and expiry
tagging, plus off-by-default arrival imports and unplayed expired removals.
Nineteen new regressions bring the .NET total to 286. The SDK/WPF harness checks
real callbacks and adapter mutations with injected fixtures. State/journal failure,
wrong/invalid snapshots, cancellation, repeat observations, reappearance, play
history, opened games, ownership, identity migrations, and retries must preserve
library safety. See [the automation contract and remaining host checks](docs/catalog-automation.md).
