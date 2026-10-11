# Amazon Luna Catalog Browser for Playnite

Milestones A and B implementation for a future Playnite 10 extension. It contains a reusable C# provider, validated runtime refreshes, a versioned cache with recovery, catalog comparisons, and optional research tools. The library builds for .NET Framework 4.6.2 and .NET 8. It does not yet contain the WPF plugin or a `.pext` package.

See [PLAN.md](PLAN.md) for the implementation checklist reconciled with the original plan and the current repository.

## Current findings

Anonymous first-party requests repeated on October 11, 2026 returned 118 distinct games through the All Games query, with 90 Standard, 118 Premium, and 50 GameNight memberships. The union of the three category results matched the All Games IDs. Memberships overlap and do not establish a user's entitlement.

**Gate A passes for the public US streaming catalog captured October 11, 2026.** Two fresh browser contexts visited all 13 discovered streaming pages and every carousel. Two anonymous API contexts checked all 26 advertised genre filters, 36 fully paginated searches, and all 118 main product details. No extra streaming IDs or omissions were found. Claims and PC giveaways are excluded. See the [completeness verdict](docs/streaming-completeness.md).

**The October 11 snapshot was fully audited and exported as `Verified` / `isComplete: true`.** Its receipt records the public US streaming scope, source-file hash, ID-set hash, and verification time.

The catalog browser will download current Luna API data at runtime through the C# provider. Tests use reduced, representative API samples; a complete saved API response or catalog is not required for testing. The Python audit is optional research tooling used to establish the browse protocol's coverage, not a step required on every runtime refresh.

The research CLI still exports `Unverified` / `isComplete: false` by default. The production `CatalogService` validates runtime downloads against the established browse contract and caches accepted data as `Validated` / `isComplete: true`. It preserves previous data after failed refreshes and reloads it offline. The October 11 receipt identifies that research capture; runtime downloads may contain a different set of games, and 118 is not a fixed requirement. See [the data-layer contract](docs/catalog-data-layer.md) for usage, cache recovery, state, and identity migration.

The provider uses the frontend-selected US endpoint and explicit `all_games`
query. Rendered browse grids match the retrieved catalog and reach stable scroll
bottoms. Remote search uses stable alphabetical offsets and explicit no-results
states for the audit; Playnite search will filter the downloaded catalog locally.
Read [the API investigation](docs/luna-api.md) for the request shape and findings,
and [the evidence tools](tools/LunaBrowserEvidence/README.md) for reproduction and
proxy/TLS setup.

## Build and test

Install a .NET 8 SDK. `global.json` selects the latest installed .NET 8 feature band. Reference assemblies for `net462` are restored from NuGet; Linux can compile the provider but cannot run the future Windows WPF plugin.

```sh
dotnet restore AmazonLuna.sln --locked-mode
dotnet build AmazonLuna.sln -c Release --no-restore
dotnet test tests/LunaCatalog.Tests/LunaCatalog.Tests.csproj -c Release --no-build
```

The suite has 182 .NET tests and 24 Python offline checks. After installing the
[pinned Python dependency](tools/LunaBrowserEvidence/README.md#setup), run:

```sh
python3 -m unittest discover -s tools/LunaBrowserEvidence -v
```

GitHub Actions runs locked restore, Release build, and both offline suites on Linux and Windows.
See [validation](docs/validation.md).

## Replay recorded fixtures

```sh
dotnet run --project tools/LunaCatalogProbe -c Release -- --fixtures tests/Fixtures --output artifacts/fixture-catalog.json
```

The fixtures intentionally retain only five distinct sample games. Replay counts are not catalog totals. Exit code **2** is expected: the requests parsed successfully, but completeness is unverified.

## Query Amazon directly

```sh
dotnet run --project tools/LunaCatalogProbe -c Release -- --live --output artifacts/us-catalog.json
```

For up to five additional anonymous detail-page checks covering memberships and the observed ID/link variants:

```sh
dotnet run --project tools/LunaCatalogProbe -c Release -- --live --verify-details --output artifacts/checked-us-catalog.json
```

Detail checks compare the main product ID, ASIN, and canonical Luna URL. They also record the anonymous page's button text, which must not be interpreted as verified entitlement, playability, or removal status. A failed requested detail check writes a separate diagnostics report and preserves the existing export. The option is available only with `--live`.

The probe sends sequential anonymous requests, disables cookies and redirects, honors the system proxy and TLS trust, and queries only the investigated US marketplace. Each response is limited to 8 MiB with a deadline covering the body read; JSON nesting is limited to 64 levels. Authentication failures, rate limits, and deferred retry requests stop further calls. The default browser metadata records the investigation's Chromium version; `--user-agent` and `--browser-version` allow supplying metadata for another investigation browser.

Catalog normalization requires a safe Luna page action and a first-party play link with a matching ASIN and an explicit matching territory. Conflicting product ID/ASIN mappings and a mismatch between All Games and the category union fail validation. These checks support reliable research records; they do not establish entitlement or complete catalog coverage.

C# probe exit codes:

| Code | Meaning |
| --- | --- |
| 2 | All four queries and any requested detail checks passed research validation; coverage remains unverified |
| 1 | A request, parse, pagination, or file operation failed |
| 64 | Invalid arguments |
| 130 | Cancelled |

Failed queries produce `OUTPUT.diagnostics.json` instead of overwriting `OUTPUT`. Successful research exports are replaced atomically. Raw response bodies, cookies, session IDs, and service tokens are not exported. Live catalog exports stay in the ignored `artifacts/` directory and are not included in source releases.

## Verify a streaming snapshot

After building the C# probe and completing the [browser tool setup](tools/LunaBrowserEvidence/README.md#setup), fetch a new snapshot and audit that exact file:

```sh
dotnet run --project tools/LunaCatalogProbe -c Release --no-build -- --live --verify-details --output artifacts/us-catalog.json
python3 tools/LunaBrowserEvidence/streaming_audit.py --probe-snapshot artifacts/us-catalog.json --output artifacts/streaming-evidence.json --catalog-output artifacts/verified-us-catalog.json
```

The C# command returns **2** on success. The full audit returns **0** when every
required check passes and **1** when coverage checks fail. Its verified JSON
export applies only to the checked research snapshot. This command is optional
research verification, separate from runtime fetching and representative fixture
tests. The production browser will fetch directly through C# and validate each
refresh; it will not require Python or load a bundled catalog export.
See [the export contract](docs/streaming-completeness.md#repeat-and-export).

## Next milestone gate

Gates A and B pass for the scoped investigation and production data layer.
The next milestone is the Playnite 10 WPF catalog browser, consuming
`CatalogService` for runtime downloads, refresh state, and cached offline display.
Search, category filters, and sorting will operate locally on the downloaded
catalog. Windows Playnite host validation remains required.

See [the next-session handoff](docs/next-session.md) for the current evidence and
limits, and [the original implementation plan](docs/implementation-plan.md) for
the remaining milestone gates.

The API is undocumented and may change. Review Amazon's applicable terms before public distribution. See [LICENSE](LICENSE).
