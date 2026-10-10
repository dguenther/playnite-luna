# Amazon Luna Catalog Browser for Playnite

Milestone A research implementation for a future Playnite 10 extension. It contains a standalone C# catalog probe, a reusable provider that builds for .NET Framework 4.6.2 and .NET 8, reduced recorded fixtures, and automated tests. It does not yet contain the WPF plugin or a `.pext` package.

## Current findings

Anonymous first-party requests on October 10, 2026 returned 118 distinct games through the All Games query, with 90 Standard, 118 Premium, and 50 GameNight memberships. The union of the three category results matched the All Games IDs. Memberships overlap and do not establish a user's entitlement.

**Full catalog completeness is not demonstrated.** No continuation fields were observed in these responses, but browser scrolling and website coverage still need verification. Every export carries `isComplete: false` and `coverageStatus: "Unverified"`. Do not use these research exports to detect removals, replace a validated cache, or claim complete coverage.

Read [the API investigation](docs/luna-api.md) for the request shape, evidence, blockers, and remaining acceptance checks.

## Build and test

Install the .NET 8 SDK or a compatible SDK. Reference assemblies for `net462` are restored from NuGet; Linux can compile the provider but cannot run the future Windows WPF plugin.

```sh
dotnet restore AmazonLuna.sln
dotnet build AmazonLuna.sln -c Release --no-restore
dotnet test tests/LunaCatalog.Tests/LunaCatalog.Tests.csproj -c Release --no-restore
```

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

The probe sends sequential anonymous requests, disables cookies, honors the system proxy and TLS trust, and queries only the investigated US marketplace. It does not connect an Amazon account. The default browser metadata matches the investigation environment; `--user-agent` and `--browser-version` allow recording the actual browser used in further investigations. The suite currently has 51 passing tests, including executable fixture replay and preservation of prior exports on failure.

Exit codes:

| Code | Meaning |
| --- | --- |
| 2 | All queries returned parseable results; coverage remains unverified |
| 1 | A request, parse, pagination, or file operation failed |
| 64 | Invalid arguments |
| 130 | Cancelled |

Failed queries produce `OUTPUT.diagnostics.json` instead of overwriting `OUTPUT`. Successful research exports are replaced atomically. Raw response bodies, cookies, session IDs, and service tokens are not exported. Live catalog exports stay in the ignored `artifacts/` directory and are not included in source releases.

## Next milestone gate

Capture a clean anonymous browser session for All Games and all three categories, inspect scroll requests and any continuation protocol, compare the website's IDs/counts and alphabetical boundaries, validate the requested US catalog, and check representative game-page links. Then implement any observed pagination protocol and record coverage evidence before advancing to production caching or the Playnite UI.

See [the next-session handoff](docs/next-session.md) to resume browser verification after the environment's network configuration takes effect.

The API is undocumented and may change. Review Amazon's applicable terms before public distribution. See [LICENSE](LICENSE).
