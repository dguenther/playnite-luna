# Catalog validation follow-up

Validated October 11, 2026 (UTC), in the managed Debian Linux environment with .NET SDK 8.0.425. This follow-up is based on the probe and detail verification already merged in PR #1.

| Check | Result |
| --- | --- |
| Locked dependency restore | Passed |
| Release library build, `net462` and `net8.0` | Passed |
| Release CLI and test build, `net8.0` | Passed |
| Build warnings/errors | 0 / 0 |
| Offline automated tests | 97 passed, 0 failed, 0 skipped |
| Fresh anonymous browse requests | All four returned usable HTTP 200 responses |
| All Games / category union | Both 118 distinct native IDs, no set differences |
| Standard / Premium / GameNight memberships | 90 / 118 / 50 |
| Representative anonymous detail checks | Five passed product ID, ASIN, and canonical URL comparison |
| Live CLI exit | Expected 2; coverage remained `Unverified`, `isComplete: false` |
| Failure export protection | Malformed queries and mismatched category sets preserved prior exports and wrote separate diagnostics |
| Rendered website comparison and scrolling | Blocked by signed-out login navigation; not passed |
| Windows, Playnite host loading, and `.pext` | Not performed; extension implementation is pending |

The 46 additional cases cover response limits with and without `Content-Length`, cancellation and deadlines during streamed body reads, invalid UTF-8, session-wide authentication/rate-limit failures, delta/date `Retry-After` deferral, extra continuation markers/actions, totals above observed tiles, conflicting identity mappings, unsafe or mismatched play links, category-set disagreement, and export preservation. Existing fixtures, detail verification, command-line behavior, and native identities are retained.

Eight regression cases address the PR review findings: a play link must carry exactly one matching territory, and only the direct `pageMemberGroups.mainContent.widgets` collection supplies browse tiles. They cover missing, empty, mismatched, and duplicate territory parameters, nested sidebar collections with and without valid main content, and preservation of prior exports when either finding occurs. Nested widgets inside the canonical collection remain supported. The new tests reproduced both findings before the fixes; all 97 tests now pass.

Reproduce the offline checks:

```sh
dotnet restore AmazonLuna.sln --locked-mode
dotnet build AmazonLuna.sln --no-restore --configuration Release
dotnet test tests/LunaCatalog.Tests/LunaCatalog.Tests.csproj --no-build --configuration Release
```

Reproduce the live research probe (requires normal proxy and TLS access to the investigated endpoint):

```sh
dotnet run --project tools/LunaCatalogProbe --no-build --configuration Release -- --live --verify-details --output artifacts/checked-us-catalog.json
```

The live command intentionally returns 2 when research checks pass because coverage remains unverified. Detail button text was still `Not Available`; it does not establish entitlement, playability, or removal. Compilation for .NET Framework on Linux does not demonstrate Playnite runtime compatibility. See [API findings](luna-api.md) and [the remaining investigation](next-session.md).
