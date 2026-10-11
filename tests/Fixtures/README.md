# Recorded response samples

These fixtures test the C# parser/provider against representative first-party
response shapes and edge cases. A complete saved API response or catalog is not
required for testing. The production browser will download current API data at
runtime; these samples are test inputs, not its catalog source.

These are reduced samples of first-party anonymous `getPage` responses from the initial October 10, 2026 investigation. Browse requests used the historical EU endpoint, including an empty query for All Games; the live provider now uses the frontend-selected US endpoint and explicit `all_games`. See [the API investigation](../../docs/luna-api.md#working-request) for the current request and its historical differences.

`AllGames.json` retains five games; the three category fixtures retain two each. `EdgeCases.json` separately retains Batman's `amzn1.luna.product` namespace and Fortnite's slug-only action. The samples exercise overlapping identities, exclusive observed memberships, nested collections, encoded presentation data, images, and direct game-page actions. They do not represent complete catalog sets or prove pagination termination. The CLI replays the four category fixtures; the edge fixture is used by regression tests.

`AlienDetail.json` and `FortniteDetail.json` retain reduced anonymous detail responses with their original page context and nested widget positions. Only main product IDs/ASINs/titles, canonical links, primary button labels, and one recommendation/DLC example are retained. The observed "Not Available" button text is deliberately tested without converting it to a catalog removal or entitlement claim.

Sanitization removes service tokens, analytics, last-played fields, cache policy, and internal type annotations. `presentationData` remains encoded as a string, matching the observed wire format. No cookies, request authentication, session IDs, or raw homepage configuration are retained. Source releases contain these minimal regression samples rather than a bulk catalog database.
