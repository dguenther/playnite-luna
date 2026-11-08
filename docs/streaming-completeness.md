# Public US streaming catalog completeness

**Gate A passes for the public US streaming catalog captured October 11, 2026.**
The audit found 118 distinct games and no omissions or extra streaming IDs across
the site's advertised public discovery mechanisms. Claims and PC giveaways are
outside this scope, as are account libraries, account entitlement, and individual
streaming eligibility. No login or streaming launch was attempted.

## Evidence and acceptance

The [reduced audit capture](evidence/2026-10-11-public-streaming-audit.json) records
the source-snapshot hash, ID-set hash, capture window, scope, and these checks:

| Check | Recorded result across two contexts of the relevant type |
| --- | --- |
| Advertised streaming route graph | All 13 pages visited; all discovered outgoing catalog routes accounted for |
| Horizontal collections | All 27 carousel/swiper instances traversed, covering 229 slide positions |
| All / Standard / Premium / GameNight grids | 118 / 90 / 118 / 50; All equals category union |
| Genre filters | All 26 advertised All Games/GameNight filters; no IDs outside All |
| Alphabetical search sweep | All 36 letter/digit queries traversed to explicit no-results states; union equals All |
| Search stability | Matching page sets/order across contexts; reverse-title references agree |
| Empty-query search landing | Recommendation IDs checked as discovery, never counted as query matches |
| Main product details | All 118 IDs, ASINs, titles, and canonical URLs match |
| Detail recommendations | 111 distinct game IDs, all inside All; DLC stays separate |
| Final All Games rechecks | Both title orders return the same original set |

The route graph includes Home, GameNight, all four browse queries, the Luna
Standard subscription listing, the GameNight browse/family-friendly views, and
the clash-and-compete, quickplay, and working-together collections. Home's genre
links default to account-only My Games; the same observed genre filter is applied
to All Games for anonymous verification. There are 14 All Games genre filters
and 12 GameNight filters.

Routes and carousels were visited in two fresh browser contexts; genre filters,
search, product details, and final browse rechecks used two anonymous API contexts.
The separate rendered browse probe also matched all four membership grids with
the C# snapshot in two browser contexts.

The character sweep is an omission check, rather than a mathematical assumption
about search indexing. Its observed union reproduces every All Games ID, including
queries returning all 118 IDs through multiple pages. The website route graph,
genre filters, terminal grids, every product detail, and unchanged final rechecks
provide the additional support for the scoped verdict. No trustworthy independent
Amazon total was observed. The verdict describes the current public listing; it
does not assert access to hidden or account-specific inventories.

## Repeat and export

This is optional research verification of the API's public catalog coverage.
The production browser will download data through C# and validate the established
browse contract at runtime. It will not run this full audit on every refresh or
load a bundled research catalog. C# tests use reduced representative API samples;
they do not need a complete saved API response.

Install a .NET 10 SDK, [build the C# probe](../README.md#build-and-test), and complete
the [browser tool setup](../tools/LunaBrowserEvidence/README.md#setup).
Retain the configured proxy and TLS verification. From the repository root:

```sh
dotnet run --project tools/LunaCatalogProbe -c Release --no-build -- --live --verify-details --output artifacts/us-catalog.json
python3 tools/LunaBrowserEvidence/streaming_audit.py --probe-snapshot artifacts/us-catalog.json --output artifacts/streaming-evidence.json --catalog-output artifacts/verified-us-catalog.json
```

The raw C# probe returns **2** and deliberately exports `Unverified` /
`isComplete: false`. The full audit returns **0** only when every required check
passes. Its optional catalog output contains `Verified` / `isComplete: true`
and `coverageEvidence` with `PublicUSStreamingCatalog`, the source-file hash,
ID-set hash, and verification time. This receipt applies to the exact checked
snapshot. A later fetch does not inherit it, and the historical count 118 is
never a fixed acceptance threshold.

The verified output is a Python-generated research JSON artifact. The current C#
`CatalogSnapshot` keeps `IsComplete` false and has no `coverageEvidence` property;
it cannot retain a verified receipt through deserialization/reserialization.
Milestone B's `CatalogService` validates live API responses against the established
browse contract and caches `Validated` / complete runtime catalogs separately.
Importing the research artifact/receipt is not a production requirement. See
[the runtime contract](catalog-data-layer.md).

Unknown search/detail states, unresolved browse continuation, reported
truncation, exhausted safety limits, failed requests, missing sources, unstable
search pages, mismatched identities/URLs, or outside-catalog discoveries fail
coverage validation. Those failures write separate diagnostics and preserve
prior evidence and verified catalog outputs. Invalid inputs are rejected before
live checks. Source, evidence, catalog, derived diagnostics, and temporary paths
must all resolve to distinct locations. Each successful output is replaced
atomically; the evidence/catalog pair is not a transaction.

Full datasets stay in ignored `artifacts/`; only reduced evidence is committed.
The production cache and refresh contract are implemented in Milestone B. The
Milestone C [browser preview](playnite-browser.md) consumes that service at
runtime; Windows host checks remain pending. The optional audit retains its
research role and does not implement automatic removal handling.
