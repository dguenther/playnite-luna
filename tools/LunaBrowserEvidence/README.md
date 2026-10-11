# Anonymous streaming evidence tools

These optional Python tools compare the public US streaming website and API with
a C# probe snapshot. The full audit supports the recorded
[Gate A verdict](../../docs/streaming-completeness.md) and can export a verified
snapshot. The smaller tools check individual sources and retain unverified
coverage. These are research tools, separate from the browser's planned runtime
C# downloads and representative fixture tests. They are not required on every
runtime refresh. None runs Playnite.

| Tool | Checks | Successful exit |
| --- | --- | --- |
| `streaming_audit.py` | Public route graph, every carousel, advertised genres, 36 paginated searches, every product detail, final All rechecks | 0; scoped verified evidence and optional catalog JSON |
| `probe.py` | All four rendered browse grids, IDs/titles/links, terminal scrolling | 2; unverified research evidence |
| `discovery.py` | Eight browser searches, loaded Home/GameNight groups, five rendered action pages | 2; unverified research evidence |
| `search_offsets.py` | Repeated `a`/`e` alphabetical traversals, explicit termination, larger/reverse references | 2; unverified research evidence |

## Setup

Python Playwright 1.62.0 and system Chromium 154.0.8037.57 were used for the
recorded investigation. Install Playwright into a task-local virtual environment
if needed; do not change an unrelated user profile. The browser tools default to
`/usr/bin/chromium`; use `--chromium` for another installed executable. Installing
the Python dependency does not install Chromium.

Install the pinned Python dependency with
`python3 -m pip install -r tools/LunaBrowserEvidence/requirements.txt`.
In a managed environment, shell commands need the supported network permission
even though they retain the configured proxy. A refusal without that permission
does not diagnose proxy health.

The environment must allow `luna.amazon.com`, `m.media-amazon.com`,
`proxy-prod.tempo.digital.a2z.com`, and
`proxy-prod.us-east-1.tempo.digital.a2z.com`. Keep inherited `HTTPS_PROXY` and CA
trust. `probe.py` tests native Chromium navigation separately. Browser requests
are forwarded using Playwright's Node `APIRequestContext.fetch` with the configured
proxy and TLS verification. `NODE_EXTRA_CA_CERTS` must reference the configured
proxy CA if Node needs it. No certificate-error override is used. Forwarding does
**not** repair or validate native Chromium certificate trust. Contexts have no
persistent profile; guest cookies and frontend-generated tokens may exist in
memory, but no account login is performed.

Only selected public request fields, counts, hashes, and boundary titles are
saved. No HAR, cookies, authentication headers, session/service tokens, browser
storage state, raw response bodies, or bulk game list is saved in evidence files.
The optional verified catalog export is a full dataset and stays in ignored
`artifacts/`. Inspect generated evidence before committing it. The committed
[capture](../../docs/evidence/2026-10-11-anonymous-browser.json)
reduces detail-call entries to status and page type. The October 11 rerun passed
every rendered link comparison in both fresh contexts. Catalog requests must
finish within 30 seconds before evaluating evidence. Cleanup uses
`unroute_all(behavior='ignoreErrors')` so outstanding background telemetry cannot
hang the next context; this does not excuse failed or pending catalog calls.

## Browse and sampled discovery checks

`probe.py` selects Standard, deselects it to reach All Games, then selects Premium
and GameNight in two fresh contexts. It records ID hashes, counts, boundaries,
scroll traces, and sanitized request fields.

From the repository root, after [building the C# probe](../../README.md#build-and-test):

```sh
dotnet run --project tools/LunaCatalogProbe -c Release --no-build -- --live --output artifacts/us-catalog.json
python3 tools/LunaBrowserEvidence/probe.py --probe-snapshot artifacts/us-catalog.json --output artifacts/browser-evidence.json
```

Both commands return **2** for successful research results with unverified full
coverage. The browser tool returns 1 for request/scroll failures, ID/title/link
mismatches, or catalog calls requiring further investigation. It preserves an
existing output and writes a separate `.diagnostics.json` report on failure.
The C# snapshot contains the full research dataset and belongs in ignored
`artifacts/`; do not commit it.

`probe.py` checks the browse grids only; the full audit supplies the broader scoped
coverage check. A finite scroll safety limit or an absent/failed view fails the
run instead of being treated as terminal catalog evidence.

Two additional tools check search/discovery and explore the observed search
index fields. The discovery tool requires a successful live C# snapshot with all
five detail checks. It compares eight search queries, loaded Home/GameNight
groups, and five rendered action pages in two fresh contexts. It records only
counts, hashes, request fields, and the five representative titles/labels.

```sh
dotnet run --project tools/LunaCatalogProbe -c Release --no-build -- --live --verify-details --output artifacts/us-catalog.json
python3 tools/LunaBrowserEvidence/discovery.py --probe-snapshot artifacts/us-catalog.json --output artifacts/discovery-evidence.json
python3 tools/LunaBrowserEvidence/search_offsets.py --probe-snapshot artifacts/us-catalog.json --output artifacts/search-offsets.json
```

All three commands return 2 for successful, **unverified** research and preserve earlier output
on failed checks. Finite search samples and loaded carousels are not universal
coverage. The offset tool manually varies `startIndex`; these calls were not
generated by scrolling. It now validates `a` and `e` using `TITLE_A_TO_Z`, two
traversals in each of two independent request contexts, and larger/reverse-sorted
reference requests. Advance by the requested size, even after short pages. Stop
only on the recognized empty-search page with a main-content
`SEARCH_NO_RESULTS_FOUND` widget; exclude its recommendation tiles from matches.
Unknown states, unstable repeats, overlaps, reference mismatches, and exhausted
safety limits fail the run. For the recorded `a` sample, offsets 0/50/100 give
50/32/0 matches, with 20 recommendations on the terminal page. All 82 matches
agree with size-100/200 and reverse-title requests. These findings apply to the
sampled anonymous queries; they do not certify every query or full catalog
coverage. Claims/PC giveaways are outside the streaming investigation. See the committed
[search/discovery](../../docs/evidence/2026-10-11-search-discovery.json) and
[validated pagination](../../docs/evidence/2026-10-11-stable-search-pagination.json)
evidence. The [earlier relevance experiment](../../docs/evidence/2026-10-11-search-offsets.json)
remains historical evidence of the ambiguity.

## Offline checks

Twenty-four offline checks cover token/header omission, incomplete comparisons, region
selection, bounded catalog draining, background-route cleanup, unverified
success, explicit search termination, short pages, unstable/reference-disagreeing
results, full-audit requirements, snapshot/receipt binding, output-path collisions,
and preservation of prior evidence and verified exports after coverage failures:

```sh
python3 -m unittest discover -s tools/LunaBrowserEvidence -v
```

## Full public streaming audit

`streaming_audit.py` closes the advertised streaming route graph, visits every
loaded carousel slide, replays every advertised genre filter, traverses all 36
letter/digit searches to explicit terminal states, checks empty-query discovery,
validates every main product, and rechecks All in both title orders. It uses two
fresh browser contexts and two anonymous request contexts; contexts are
independent, with sequential API requests and modest spacing within each.
Counts are derived from the input snapshot rather than fixed at 118.

```sh
python3 tools/LunaBrowserEvidence/streaming_audit.py --probe-snapshot artifacts/us-catalog.json --output artifacts/streaming-evidence.json --catalog-output artifacts/verified-us-catalog.json
```

It returns 0 only when every required source passes and all discovered streaming
IDs are accounted for. Its optional complete catalog export carries `Verified`,
`isComplete: true`, and a `PublicUSStreamingCatalog` receipt bound to the source
file hash, ID-set hash, and verification time. The raw C# fetch stays unverified.
Unknown states, unresolved browse continuations/truncation, unstable search,
missing sources, failed requests, outside-catalog discoveries, exhausted limits,
and identity/canonical mismatches fail the audit. Existing evidence and complete
exports are preserved when coverage checks fail; diagnostics omit raw error
messages. Invalid inputs are rejected before live checks. Source, output,
diagnostics, and all temporary paths must resolve to distinct locations; this
check runs before reading the snapshot or starting live requests.
The current C# model cannot retain the
Python export's verified state or receipt. Milestone B adds runtime C# refresh
validation; importing this optional research export is not required.

Claims/PC giveaways, account libraries, entitlement, and individual streaming
eligibility are outside the scope. See the [verdict](../../docs/streaming-completeness.md)
and [reduced capture](../../docs/evidence/2026-10-11-public-streaming-audit.json).
Full datasets remain in ignored `artifacts/`. Native Chromium's trust limitation
still applies; configured proxy routing and verified Node TLS are retained.
