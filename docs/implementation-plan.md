# Amazon Luna Catalog Browser for Playnite
## Implementation Plan

### 1. Project overview

Build an open-source Playnite extension that allows users to browse the complete Amazon Luna cloud gaming catalog and optionally import games into their Playnite libraries.

The extension should be conceptually similar to the existing **Game Pass Catalog Browser** extension for Playnite.

**Primary goal:** Display every game currently available on Amazon Luna, including Luna Standard, Luna Premium, and GameNight titles.

**Secondary goal:** Allow users to add selected games, or the entire catalog, to Playnite without requiring an Amazon account connection.

The catalog must be obtained directly from Amazon Luna. Do not depend on third-party catalog providers such as Cloud Dosage.

### 2. Technical decisions

| Component | Decision |
|---|---|
| Platform | Playnite 10.x, Windows |
| Language | C# |
| Framework | .NET Framework 4.6.2 |
| Extension type | `GenericPlugin` |
| Interface | WPF catalog browser |
| Catalog source | Amazon Luna first-party backend |
| Authentication | Not required for MVP |
| Initial region | United States (`en_US`) |
| Storage | Plugin-local JSON cache and import mappings |
| Distribution | Playnite `.pext` package |

Use the currently compatible Playnite 10 SDK, and keep the catalog provider separate from the Playnite-specific implementation.

Do not target Playnite 11 initially; its SDK has material architectural differences.

### 3. MVP features

**Required**

- Browse the full Amazon Luna catalog in a dedicated Playnite window.
- Display game title, cover artwork, and known catalog memberships.
- Search games by title.
- Filter games by Standard, Premium, and GameNight.
- Display whether a game has already been imported into Playnite.
- Open a game's Luna page in the user's default browser.
- Import individual games or a multi-selection into Playnite.
- Provide an explicit "Import All" operation with confirmation.
- Manually refresh the catalog.
- Cache the most recent successful catalog.
- Avoid duplicate entries when importing repeatedly.
- Support the US catalog initially, without Amazon login.

**Nice to have, after MVP**

- Additional countries and marketplace selection.
- Automatic refresh on a configurable interval.
- Notifications for newly added or removed games.
- Automatically import newly added games (opt-in).
- Mark imported games that have left Luna.
- Detect games already present in other Playnite libraries.
- Optional automatic metadata enrichment.
- Additional sorting and genre filters.

**Out of scope**

- Amazon account authentication or entitlement detection.
- Synchronizing Luna playtime.
- Streaming gameplay inside Playnite.
- Importing Amazon's downloadable PC game giveaways.
- Synchronizing personal favorites or play history.
- Supporting discontinued Bring Your Own Library integrations.

### 4. Phase 0 — Investigate the Amazon Luna catalog API

**This phase is a hard prerequisite for the rest of the project.**

Amazon Luna's frontend uses an undocumented backend API. An existing automation demonstrates fetching games included with Prime from this endpoint:

`POST https://proxy-prod.eu-west-1.tempo.digital.a2z.com/getPage`

Reference implementation:

https://github.com/paoloronco/n8n-templates/tree/main/free-templates/1-amazonluna-fetch

The implementation uses a request containing:

- `searchContext.query = "included_with_prime"`
- `searchContext.sort = "TITLE_A_TO_Z"`
- `searchContext.filtersList = []`
- `featureScheme = "WEB_V1"`
- `pageContext.pageType = "multistate_browse_results"`
- `pageContext.pageId = "default"`

It also supplies `clientContext` browser metadata, `inputContext`, `dynamicFeatures`, and locale/marketplace headers.

The response contains nested `GAME_TILE` objects. Their `presentationData` field can contain metadata including title, ASIN, artwork, release year, publisher, genres, and URLs.

This endpoint and response structure are starting points, not confirmed stable API contracts.

#### 4.1 Discover the full catalog

Investigate the following Luna browse URLs:

- Standard: https://luna.amazon.com/browse?quick_search=included_with_prime
- Premium: https://luna.amazon.com/browse?quick_search=luna_premium
- GameNight: https://luna.amazon.com/browse?quick_search=game_night

Use Chromium developer tools to inspect the actual requests made when visiting these pages.

Determine:

1. Whether the API works without authentication.
2. Whether changing `searchContext.query` retrieves the other catalogs.
3. Whether Amazon exposes a dedicated "All Games" query.
4. Whether all results are returned in one request or through pagination.
5. Whether scrolling triggers additional requests.
6. Whether game metadata contains unique identifiers and launch URLs.
7. Whether different regions produce different catalogs.
8. Whether Luna's catalog includes titles outside the three initial categories.

Do **not** assume that the union of three filtered queries equals the full catalog without verifying it.

Inspect whether `GAME_TILE` entries represent actual playable games versus promotions, editions, DLC, or other storefront content.

Use identifiers such as ASINs rather than game titles for deduplication.

#### 4.2 Verify pagination and completeness

The existing n8n example parses the result of a single `getPage` request. It does not prove that one response contains every available title.

Investigate cursor-based pagination, continuation tokens, offsets, or additional page requests.

Compare the resulting dataset against the visible Luna website:

- Total number of games, where a trustworthy count is available.
- Several recognizable titles from each category.
- Titles near both ends of alphabetical results.
- Titles discovered through category browsing and search.
- Overlapping games that belong to multiple catalog categories.

Repeat the check with a clean browser session to test anonymous availability.

A category must not be considered successfully fetched if results are truncated, pagination fails, or the response is malformed.

#### 4.3 Produce a standalone proof of concept

Before writing the Playnite plugin, implement a small command-line program that:

- Fetches the catalog.
- Normalizes the response.
- Deduplicates by stable game identifier.
- Exports the catalog to JSON.
- Reports counts per category and unique-game totals.
- Reports pagination and completeness diagnostics.

Prefer C# to allow reuse of the implementation.

Record the exact request requirements, response structure, pagination behavior, and observed limitations in `docs/luna-api.md`.

Use recorded, sanitized response fixtures for subsequent development.

#### Phase 0 acceptance criteria

- The catalog can be retrieved programmatically from Amazon Luna.
- Complete results can be retrieved, including all necessary pages.
- Coverage has been compared against the Luna website.
- Stable game identifiers are available.
- Each importable game has a valid Luna URL.
- The agent can explain how to distinguish Standard, Premium, and GameNight results.
- API response fixtures and a repeatable test command are committed.

**If complete catalog retrieval is not feasible, document the blocker rather than pretending partial results are complete.** A limited proof of concept is acceptable, but must not be labeled a complete catalog.

### 5. Phase 1 — Implement the catalog data layer

Create a standalone library responsible for communicating with Luna, without dependencies on Playnite UI components.

#### 5.1 Data model

Suggested normalized model:

`LunaGame`

- `Id`: Stable Luna identifier, preferably ASIN.
- `Title`: Display title.
- `Url`: Direct Luna game page.
- `Region`: Marketplace or region.
- `Categories`: Set of observed memberships (Standard, Premium, GameNight).
- `CoverUrl`: Portrait artwork.
- `BackgroundUrl`: Landscape artwork.
- `ReleaseYear`: Optional.
- `Publishers`: Optional.
- `Genres`: Optional.

Use `(region, LunaId)` as the canonical identity where needed.

Do not use normalized title strings as permanent identifiers. The same title may represent different editions, and titles can change.

If a game lacks a reliable identifier, it may be displayed but should not be automatically imported until a stable identity can be established.

Store catalog memberships as a set rather than one tier enum: GameNight and other categories can overlap.

Do not claim that an observed membership establishes an individual user's entitlement.

#### 5.2 Catalog provider abstraction

Implement an interface equivalent to:

`ILunaCatalogProvider.FetchAsync(region, cancellationToken) -> CatalogSnapshot`

`CatalogSnapshot` should contain:

- Normalized game collection.
- Target region.
- Retrieval timestamp.
- Successfully queried catalog categories.
- Pagination/completeness status.
- Error and diagnostic information.

The implementation should support pagination, cancellation, request timeouts, modest retry/backoff behavior, and useful error logging.

Avoid parallel bursts of requests and do not attempt to bypass authentication or anti-bot protections.

#### 5.3 Caching and reliability

Persist the last validated catalog as a versioned JSON snapshot in Playnite's plugin data directory.

Requirements:

- Continue showing cached data when Luna is unavailable.
- Display when data was last successfully refreshed.
- Distinguish stale data from an empty catalog.
- Write cache files atomically.
- Reject malformed or demonstrably incomplete snapshots.
- Preserve the previous cache when a fetch fails.

**Critical rule:** A zero-game response, HTTP failure, or pagination error must never be interpreted as Amazon removing all games.

#### Phase 1 acceptance criteria

- Catalog retrieval works independently of Playnite.
- The cache survives process restarts.
- Overlapping categories produce one normalized entry per game.
- A failed refresh preserves existing data.
- Unit tests cover pagination, deduplication, malformed data, and category normalization.

### 6. Phase 2 — Implement the Playnite catalog browser

Create a Playnite `GenericPlugin` with a WPF window.

Expose the browser through a main-menu item named **Amazon Luna Catalog Browser**. A sidebar shortcut is optional.

Use the Game Pass Catalog Browser's user experience as a reference, but implement the UI independently where appropriate.

#### 6.1 User interface

The browser should include:

- Search field.
- Category filters (All, Standard, Premium, GameNight).
- Scrollable game grid or list.
- Cover artwork and game titles.
- Indicators for multiple catalog memberships.
- Indicator for already imported games.
- Total visible game count.
- Last successful refresh timestamp.
- Refresh button.
- Open in Luna action.
- Import Selected and Import All actions.

Use asynchronous fetching and image loading. Network requests must not block Playnite's UI thread.

Search and filtering should operate locally on the cached catalog without re-querying Amazon for each interaction.

#### 6.2 Import status

Determine import status using the mapping maintained by the plugin, reconciled with Playnite's current database.

Display at least:

- Not imported.
- Imported into Playnite.

Do not mark a game as already imported solely because a game with the same title exists through Steam, GOG, or another provider.

An existing non-Luna copy can be shown as a separate informational match in a future release.

#### Phase 2 acceptance criteria

- Browser opens successfully in Playnite Desktop Mode.
- Entire validated cached catalog is visible.
- Category filtering, search, and sorting do not alter the data.
- All categories are represented without duplicate tiles.
- Games can be opened on Luna.
- UI remains responsive during refresh.
- Offline mode clearly displays cached results.

### 7. Phase 3 — Optional Playnite library imports

Use the Playnite SDK database API to add imported titles.

Do not edit Playnite's underlying database files directly.

#### 7.1 Import behavior

Users should be able to:

- Import one game.
- Select and import multiple games.
- Import every game in the filtered view or complete catalog, with an explicit confirmation of the scope.
- See a summary of added, skipped, and failed entries.

Imported games should:

- Have the correct title.
- Use an `Amazon Luna` source.
- Have a direct link to their Luna page.
- Have an `Open in Luna` URL action.
- Include relevant catalog tags where available.
- Optionally use Luna-supplied cover artwork.
- Retain a mapping to the stable Luna identifier.

Avoid assigning a generic PC Windows platform merely because the extension runs on Windows. These are cloud-streamed games.

For launch behavior, use Playnite's `GameActionType.URL` where appropriate. Keep games uninstalled by default rather than implying a local installation. Provide an explicit Open in Luna action. Optionally support marking entries as cloud-playable/installed to enable Playnite's native Play button, after verifying that behavior.

Do not claim accurate session duration or playtime tracking from opening a URL.

#### 7.2 Import identity

Persist a mapping between:

- Region.
- Luna game identifier.
- Playnite game GUID.

The extension should use this mapping when refreshing imported games and checking for duplicates.

Reconcile the mapping against the actual Playnite database because users may delete games manually.

Where practical, recover mappings from stored Luna URLs after a plugin-state restore.

#### 7.3 Repeated imports

Imports must be idempotent.

For the same stable identifier, a second import should not create another game.

Importing a Luna title must not overwrite an existing Steam/GOG/etc. library entry with the same title.

#### 7.4 Preserve user metadata

During subsequent refreshes, update only extension-managed properties as necessary.

Do not overwrite user-controlled metadata such as:

- Title overrides.
- Favorite status.
- Completion status.
- Playtime.
- Personal notes.
- Manually edited artwork.
- User-selected categories or tags.

If the implementation cannot reliably distinguish its metadata from user modifications, prefer not to update that field automatically.

#### 7.5 Handling games removed from Luna

For MVP, do not automatically delete imported games.

After a verified catalog refresh, the extension may calculate additions and removals. However, actual removal detection and status tagging can remain a post-MVP feature.

Future removal detection must only operate after a complete, successful catalog fetch for the same region, and should require confirmation across refreshes before reporting a game as removed.

#### Phase 3 acceptance criteria

- A single game can be imported successfully.
- Bulk importing works.
- Repeated imports do not create duplicates.
- Imported games have working Luna links.
- Imported games survive a Playnite restart.
- Existing Steam/GOG library entries are untouched.
- User edits are preserved during refresh.
- Failed imports provide actionable errors.

### 8. Phase 4 — Testing, hardening, and packaging

#### Automated tests

Include tests for:

- Parsing real recorded Luna API responses.
- Handling nested `GAME_TILE` structures.
- Malformed or missing `presentationData`.
- Multiple catalog memberships.
- Pagination and incomplete results.
- Region-scoped identifiers.
- Duplicate ASINs and title collisions.
- API 401/403/429/500 responses and timeouts.
- Cache recovery and atomic replacement.
- Catalog additions/removals across snapshots.
- Import idempotency and stale mappings.

#### Manual Playnite testing

Test at least:

1. First launch with an empty cache.
2. Opening the browser while offline.
3. Searching and filtering all categories.
4. Refreshing after a successful fetch.
5. Importing a single game.
6. Importing multiple games.
7. Repeating an import.
8. Changing or deleting an imported game manually.
9. Restarting Playnite and checking persistence.
10. Using the packaged `.pext` on a clean Playnite installation.

#### Packaging

Produce a working `.pext` using Playnite's supported Toolbox packaging workflow.

Provide a README explaining installation, supported Playnite versions, catalog scope, region limitations, the undocumented API dependency, and known issues.

Review Amazon's applicable terms before public distribution. Do not embed user credentials, implement unauthorized authentication bypasses, or redistribute a bulk catalog database as part of the release.

### 9. Suggested repository structure

```text
amazon-luna-playnite/
├── src/
│   ├── LunaCatalog/
│   │   ├── Models/
│   │   ├── Api/
│   │   ├── Parsing/
│   │   └── Caching/
│   └── LunaPlaynite/
│       ├── Plugin/
│       ├── Views/
│       ├── ViewModels/
│       ├── Import/
│       └── Settings/
├── tests/
│   ├── LunaCatalog.Tests/
│   └── Fixtures/
├── tools/
│   └── LunaCatalogProbe/
├── docs/
│   ├── luna-api.md
│   └── architecture.md
├── README.md
└── LICENSE
```

Adapt the structure if the Playnite Toolbox template requires a different layout. Keep independent catalog code separate from Playnite SDK dependencies.

### 10. Implementation sequence

Execute milestones in this order:

**Milestone A — Catalog proof of concept**

Discover and validate Amazon Luna's catalog endpoint, pagination, and full category coverage. Export a complete JSON catalog.

**Milestone B — Data layer**

Implement a reusable catalog client, normalized data model, regional handling, caching, and tests.

**Milestone C — Playnite browser**

Create the extension, display the catalog, and implement search, filtering, status, and Open in Luna.

**Milestone D — Imports**

Implement single/bulk imports, stable mappings, duplicate prevention, and preservation of user edits.

**Milestone E — Release**

Harden failures, complete automated/manual testing, write documentation, and package a `.pext`.

Work in independently testable increments. Do not invest heavily in UI polish before confirming the catalog extraction is reliable.

### 11. Definition of done

The MVP is complete when:

- [ ] Playnite installs and loads the extension.
- [ ] The extension retrieves a demonstrably complete US Luna cloud catalog.
- [ ] Standard, Premium, and GameNight membership can be browsed.
- [ ] Every catalog game has a stable identity and a usable Luna link.
- [ ] Search and category filters work.
- [ ] Users can import individual games or multiple games.
- [ ] Repeated imports are idempotent.
- [ ] Existing non-Luna library entries remain untouched.
- [ ] The last successful catalog remains accessible offline.
- [ ] Failed or incomplete fetches never trigger mass removals.
- [ ] Documentation and automated tests are included.
- [ ] A working `.pext` package is generated and manually tested.

### 12. Reference material

**Existing Playnite extension**

Game Pass Catalog Browser:

https://github.com/darklinkpower/PlayniteExtensionsCollection/wiki/Game-Pass-Catalog-Browser

Source collection:

https://github.com/darklinkpower/PlayniteExtensionsCollection

**Playnite SDK**

Generic plugins:

https://api.playnite.link/docs/tutorials/extensions/genericPlugins.html

Custom menus:

https://api.playnite.link/docs/tutorials/extensions/menus.html

Custom UI and sidebar:

https://api.playnite.link/docs/tutorials/extensions/ui.html

Game actions:

https://api.playnite.link/docs/manual/library/games/gameActions.html

**Amazon Luna reverse-engineering reference**

https://github.com/paoloronco/n8n-templates/tree/main/free-templates/1-amazonluna-fetch

Pay particular attention to its `workflow.json` request and `GAME_TILE` parsing logic. Treat its request parameters as a research reference, not a guaranteed current contract.

**Amazon Luna**

https://luna.amazon.com/

Luna Terms of Use:

https://digprjsurvey.amazon.com/csad/help/node/G5FYRVVJK7KFGQQN

### 13. Instructions to the implementing agent

Start with Milestone A immediately.

Do not rely on Cloud Dosage or other third-party catalog providers.

Make reasonable implementation decisions independently. Prefer simple, maintainable code over additional features.

If the undocumented Luna API differs from the supplied references, investigate the current frontend requests and update the design accordingly.

Do not silently downgrade the objective from "complete catalog" to "whatever one request happens to return."

Clearly report whether full catalog completeness was demonstrated. If a blocker prevents completion, provide the reproducible investigation findings and the best working implementation achieved.

Deliver source code, tests, documentation, and a working Playnite extension package where the development environment permits.
