# Milestone A architecture

`src/LunaCatalog` builds for `net462` and `net8.0`, with no Playnite or WPF dependencies. It owns request construction, normalized models, recursive response parsing, region-scoped identity, category-set merging, and diagnostics. The HTTP client is injected for testable transport behavior.

`tools/LunaCatalogProbe` runs on .NET 8 and provides live anonymous retrieval and offline fixture replay. The optional `--verify-details` mode additionally checks up to five main-product identities and canonical Luna URLs using anonymous detail responses. It writes a research snapshot atomically after all four queries and any requested detail checks pass basic response validation. Failures produce a separate diagnostics file, preserving any prior export. Cancellation returns exit code 130. A successfully retrieved research snapshot returns 2 because full coverage remains unverified.

`tests/LunaCatalog.Tests` tests recorded wire data and failure cases through an injected HTTP handler. No automated test needs live Amazon access. `tests/Fixtures` contains explicitly reduced, sanitized wire samples.

The next production layer should separate query/page traversal evidence from independent catalog coverage evidence. Only a validated complete snapshot may replace the last successful production cache or participate in removal detection. No production cache, import mapping, database mutation, Playnite plugin, or UI exists in Milestone A.

After the API acceptance gate passes, add the WPF GenericPlugin from Playnite's Toolbox template and consume the catalog provider through `ILunaCatalogProvider`. Keep imports behind explicit user actions and reconcile `(region, LunaId)` mappings with Playnite's database. Windows is required for Playnite runtime validation and the final `.pext` packaging check.
