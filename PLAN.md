# Amazon Luna Catalog Browser — implementation plan

Reviewed October 10, 2026 against `dguenther/playnite-luna` main at
[`f309b5b`](https://github.com/dguenther/playnite-luna/commit/f309b5bb7ba836b1bcfb37f978c871982876dece).

This is the working checklist reconciled with the uploaded implementation plan.
[docs/implementation-plan.md](docs/implementation-plan.md) already contains that
plan; its text matches the attachment after whitespace normalization. Keep it as
the original specification and update this file as milestones gain evidence.

**Current state: Milestone A / Phase 0 is partially complete. Full US catalog
completeness is unverified.** The repository has a reusable provider, a research
CLI, fixtures, validation, and CI. It has no production cache, Playnite extension,
browser UI, imports, or `.pext`. Resume the coverage investigation before advancing
to those features.

A checked item below means the implementation or recorded evidence exists at the
reviewed commit. It does not imply a whole phase has passed. Live findings and
previous test results are attributed to repository records; this planning review
did not repeat live retrieval or execute .NET tests.

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

- [x] Sequential anonymous `getPage` requests for All Games (empty query),
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

### Remaining investigation

1. **Reproduce and resolve the browser evidence blocker.** The latest handoff
   reports that frontend assets are accessible, but clean signed-out browse
   navigation redirects to Luna's login route and the next Amazon sign-in
   destination is denied by the proxy. Normal Chromium CA trust also remains to
   be verified. Record environment, route, and observed failures using supported
   network/TLS configuration. Do not assume the earlier asset-host blocker persists.
2. **Capture actual browse requests.** Visit `https://luna.amazon.com/browse` and
   the three `quick_search` URLs from the source plan in a clean anonymous context.
   Compare headers, body, marketplace, query selection, and response structure with
   [docs/luna-api.md](docs/luna-api.md). Determine necessary request fields.
3. **Verify termination.** Scroll all four views to the end and record every
   subsequent request. Implement a continuation protocol only if observed;
   test traversal, repeated cursors, cancellation, and failed intermediate pages.
   If no continuation exists, retain repeatable evidence of terminal scrolling.
4. **Verify full coverage.** Compare native IDs, trustworthy website counts,
   alphabetical endpoints, recognizable games from each membership, category
   overlaps, search results, and catalog groups outside the three filters.
   Confirm whether `GAME_TILE` records include promotions, DLC, editions, or other
   non-playable storefront items.
5. **Verify US interpretation and usable links.** Compare the rendered region and
   representative game pages, including both ID namespaces and slug-only actions.
   Explain the recorded anonymous "Not Available" detail buttons without treating
   them as removals or proof of entitlement. Record any regional differences
   relevant to US completeness; additional-region product support stays deferred.
6. **Repeat independently and record the verdict.** Repeat anonymous availability
   checks in a fresh context, commit sanitized evidence/fixtures, and update the
   API documentation with exact request requirements, coverage, termination,
   limitations, and reproduction commands.

**Gate A:** programmatic anonymous retrieval, all necessary pages, demonstrated US
website coverage, stable identities, usable Luna links, explained memberships,
and committed repeatable evidence must all pass. The existing absence of cursors,
118-game count, query equality, frontend code inspection, and sample detail checks
do not satisfy this gate. If verification remains blocked, retain a limited
research result with an explicit blocker and leave Gate A open.

## B — Complete the production data layer (Phase 1)

Depends on Gate A. Reuse existing models, provider abstraction, parsing, and
transport; do not restart them.

- [ ] Separate request success, page-traversal completion, and independently
  supported catalog coverage in the snapshot contract. Retain retrieval time,
  region/marketplace, queried memberships, diagnostics, and schema version.
  Current `IsComplete` is always false; `CoverageStatus` is `Unverified` for usable
  research results and `Incomplete` for failed checks. Replace that research
  contract only with evidence-backed production validation.
- [ ] Reconcile the current strict All Games/category-union equality rule with
  Gate A findings. If legitimate games exist outside those memberships, retain
  them in All rather than fabricating a category or rejecting a complete catalog.
- [ ] Preserve native-ID-first identity with ASIN fallback. Define migration or
  reconciliation if an ASIN-only entry later gains a native ID; never merge by
  title or resolve conflicting identities silently.
- [ ] Keep uncertain records diagnostic and non-importable. The source plan allows
  display-only records with unreliable identity; the current parser rejects them
  and invalidates the query. Add display-only handling only if observed records
  require it, without weakening the complete-snapshot gate.
- [ ] Add a versioned validated JSON cache under Playnite's plugin data directory,
  through a storage abstraction that keeps the catalog library independent of
  Playnite. Support restart reload, schema checks, and malformed-file recovery.
- [ ] Atomically replace the cache only after validation succeeds; preserve the
  previous snapshot after HTTP/parse/pagination/cancellation/write failures.
  Reuse the CLI's export approach where compatible, while implementing storage
  with APIs available in `net462`.
- [ ] Expose last successful refresh, stale cached data, no cache yet, and refresh
  errors as distinct states. Load cached data when Luna is unavailable.
- [ ] Add tests for validated-cache reload, atomic replacement and recovery,
  rejected partial/zero snapshots, region mismatch, and failed-refresh preservation;
  extend traversal/category tests according to Gate A.

**Gate B:** retrieval still works independently of Playnite; overlapping games
normalize once; a validated cache survives restart; failed refreshes preserve it;
and the data-layer acceptance tests pass.

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
| Membership overlap, region identity, duplicate ASINs, title collisions | Parser/model/validation tests, including identity conflicts | Production identity migration and import behavior. |
| Pagination and incomplete results | Marker/action/total rejection and category-set disagreement | Actual traversal and termination protocol, if discovered. |
| 401/403/429/500, timeouts, cancellation | Provider/transport tests; bounded retry/deferral, body deadlines/limits, UTF-8/depth checks | Regressions across any new request types. |
| Cache recovery and atomic replacement | Research-export success/failure tests only | Production cache restart, corruption, interrupted replacement, schema handling. |
| Additions/removals across snapshots | No snapshot-diff implementation | Same-region complete-snapshot comparisons and failure safeguards; no MVP deletion. |
| Import idempotency and stale mappings | No import implementation | Milestone D service tests plus Playnite database integration. |

- [x] .NET 8 SDK selection, committed NuGet lock files, locked restore, Release
  build, and offline tests in GitHub Actions.
- [x] Repository validation record reports 97 passing cases and builds for
  `net462` and `net8.0` with zero warnings/errors. This is prior evidence,
  not a new test run from this planning change.
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

**Next increment:** reproduce the signed-out browse/TLS blocker, obtain normal
browser request/scroll evidence where feasible, and commit the Gate A verdict
with sanitized supporting evidence. Keep increments independently testable and
follow A → B → C → D → E. If Gate A cannot close, report the reproducible blocker
and working limited probe without labeling it a complete catalog.

## Deferred improvements

After MVP: additional marketplaces, configurable automatic refresh, addition/
removal notifications, opt-in automatic new-game imports, departed-game status
tags, informational matches in other libraries, optional metadata enrichment, and
additional sorting/genre filters. None is required to close the MVP gates.

Reference URLs for Playnite SDK, Game Pass Catalog Browser, the n8n research
starting point, Luna, and its terms are retained in
[the source plan](docs/implementation-plan.md#12-reference-material).
