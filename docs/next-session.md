# Continue Milestone A in the next chat

## Project state

Repository: `dguenther/playnite-luna`; working directory in this environment: `/workspace/playnite-luna`.

The initial probe and detail verification were merged in PR #1. The follow-up preserves that implementation and adds transport limits, stricter identity/category/pagination validation, locked dependencies, and CI. Confirm that the PR is merged before assuming these changes exist on `main` in a new environment.

The current provider builds for `net462` and `net8.0`. The .NET 8 console probe retrieves first-party catalog data anonymously, merges stable region-scoped identities, preserves overlapping memberships, and writes research JSON exports. The test suite has 97 cases. No WPF plugin, import implementation, production cache, or `.pext` exists yet; full catalog retrieval is the prerequisite.

## Verified observations

- All Games: 118 native product IDs; Standard: 90; Premium: 118; GameNight: 50.
- The category union matches the All Games set in both directions in the recorded investigation.
- Both `amzn1.adg.product` and `amzn1.luna.product` IDs occur. Fortnite's Luna action omits the ASIN from its path.
- All four browse responses returned one collection with no recognized continuation marker. This does not establish pagination termination.
- Five anonymous detail samples matched product ID, ASIN, and canonical URL. Their actual Luna action URLs also returned HTTP 200.
- Those detail responses report "Not Available" in this environment. The reason is unknown. Do not interpret it as removal or global unavailability.
- Every research snapshot still has `isComplete: false`. Usable reports have `coverageStatus: "Unverified"`; failed checks produce `"Incomplete"` diagnostics.

## Environment setup

The resumed session can access `m.media-amazon.com`. Start new managed-environment work by reading the cloud environment runtime skill, inspecting environment status, and checking `/etc/codex/network-policy.json` in the new instance.

Clean signed-out browse navigation redirected to Luna's login route, and the subsequent Amazon sign-in destination was denied by the proxy. No browse grid or browse request was observed. Forwarding browser requests through Playwright's Node transport with configured proxy/CA trust preserved TLS verification but did not resolve this blocker or validate normal Chromium navigation. Do not count those attempts as website comparison evidence.

The investigation environment had Chromium 154 and Python Playwright. .NET 8 and 10 SDKs are installed under `/workspace/.dotnet`; the activation script is `/workspace/work/dotnet-setup/activate.sh`, and NuGet packages are under `/workspace/work/nuget`. These are environment-local paths, not project dependencies or guaranteed state in a new instance. Install a .NET 8 SDK if needed to match `global.json`.

Chromium failed initial HTTPS navigation with `ERR_CERT_AUTHORITY_INVALID`. The configured proxy CA files were in `/usr/local/share/ca-certificates/`; Python and .NET used the system CA bundle successfully. The task-local NSS experiment did not work because the existing legacy NSS database in the execution user's home takes precedence over the new XDG location. Resolve Chromium trust through the environment's normal supported setup. Do not disable TLS verification, alter proxy routing, or write to unrelated user profiles.

The anonymous homepage references several backend hostnames, including `proxy-prod.tempo.digital.a2z.com` and `proxy-prod.us-east-1.tempo.digital.a2z.com`. Their appearance in HTML does not prove they are required. Inspect actual network failures and request only the destinations the current frontend uses through the normal environment configuration.

## Reproduce the code checks

From the repository root with `dotnet` available:

```sh
dotnet restore AmazonLuna.sln --locked-mode
dotnet build AmazonLuna.sln -c Release --no-restore
dotnet test tests/LunaCatalog.Tests/LunaCatalog.Tests.csproj -c Release --no-build
dotnet run --project tools/LunaCatalogProbe -c Release -- --fixtures tests/Fixtures --output artifacts/fixture-catalog.json
dotnet run --project tools/LunaCatalogProbe -c Release -- --live --verify-details --output artifacts/checked-us-catalog.json
```

Exit code 2 means requests/checks passed and completeness remains unverified. Fixture replay contains only five distinct catalog games. Any requested check failure returns 1 and writes a separate `.diagnostics.json` report, preserving the prior output.

## Next investigation

1. Open `https://luna.amazon.com/browse` in a clean anonymous browser context. Capture `getPage` and any subsequent catalog requests, omitting cookies/session IDs/tokens from saved evidence.
2. Inspect Standard (`quick_search=included_with_prime`), Premium (`quick_search=luna_premium`), and GameNight (`quick_search=game_night`) requests. Compare the exact query/body/header shape with `docs/luna-api.md`.
3. Scroll each page to the bottom and record whether additional requests occur. If a continuation protocol appears, implement the observed protocol and test incomplete/failed traversal.
4. Cross-check IDs, trustworthy visible totals, alphabetical boundaries, category overlaps, and search results. Check for catalog groups outside the three filters and for promotions/non-playable items.
5. Investigate the served US marketplace and the sample detail pages' "Not Available" state without disguising geographic location or bypassing authentication.
6. Repeat in a new anonymous context. Only then decide whether evidence supports a complete US catalog. If not, document the exact blocker.

Keep the provider separate from Playnite. Continue to the production data layer and WPF browser only when the Phase 0 acceptance gate is satisfied. Do not enable removal detection using research snapshots.
