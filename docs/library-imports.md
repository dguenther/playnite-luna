# Optional Playnite library imports

Milestone D adds explicit single/bulk imports to the Playnite 10 browser preview
(`0.2.4`). Catalog viewing and runtime refresh remain independent of importing.
The public US streaming scope and [runtime acceptance contract](catalog-data-layer.md)
are unchanged. No account connection or complete saved API fixture is required.
**Gates C and D still require actual Windows Playnite host validation.**

## Selection and scope

Each row shows **Not imported**, **Imported**, **Import conflict**, or **Import
status unavailable**. Status comes from the current Playnite database and Luna
identity markers; a same-title Steam/GOG game does not establish Luna identity.

Select one row, or use Ctrl/Shift-click for multiple rows, and choose **Import
Selected**. Selection follows stable IDs through title sorting and successful
refreshes. Rows hidden by search/category filtering are deselected, so an import
of selected rows uses the visible selection at the time of the click.

**Import All (N)** always means the complete validated US catalog, including rows
hidden by search/category filters. The Playnite confirmation states that scope
and count and requires Yes. Declining creates no entries. Existing imports are
skipped. Imports can use saved offline catalog data; unavailable optional covers
do not prevent game creation.

Local search/filter/sort remain usable during imports. Refresh and additional
imports are disabled while a batch runs. **Cancel** or hiding/closing the last
catalog view stops remaining work; already committed games stay in the library.
The sidebar and separate window share a session, so closing one leaves a batch
running when the other remains open. A summary reports
added, already imported, failed, and cancelled counts, with per-entry errors or
warnings. Retrying the batch skips committed entries and retries failures.

## Created entries and user edits

New entries use the catalog title, **Amazon Luna** source, observed `Luna:
Standard`, `Luna: Premium`, and/or `Luna: GameNight` tags, a direct Luna link, and
an **Open in Luna** URL action. The action is an additional action rather than a
native Play action. Entries remain uninstalled, have no assumed Windows platform,
and belong to the **Amazon Luna** library. The extension registers as a
`LibraryPlugin` so Playnite's **Library** filter uses that name. Source and
platform are separate fields; no PC Windows platform is inferred. URL opening
does not claim entitlement or track a play session.

Updating Playnite's libraries does not fetch or automatically import the Luna
catalog. Its library-import hook returns no new games; additions remain explicit
browser actions. On startup, library update, or opening the browser, earlier
manual imports with a valid Luna marker move to this library by changing only
`PluginId`. Their GUID, mapping, actions, artwork, and edited metadata are retained.
Unmarked manual games and games belonging to other libraries are not migrated.

Optional covers use the same first-party downloader and concurrency/size/deadline
bounds as browser artwork. The adapter decodes bounded thumbnails on a worker and
adds PNG files through Playnite's `AddFile` API for new entries. Invalid or failed
covers are omitted. No catalog API requests are needed for an import.

Repeated imports and catalog refreshes do not update existing game metadata.
Title overrides, favorites, completion, playtime, notes, custom artwork, user
tags/categories, links, and actions remain as edited. Metadata ownership beyond
the initial creation is intentionally not inferred. No game is deleted when it
disappears from a runtime catalog, and departed-game tagging remains deferred.

## Metadata downloads

After importing, select the games in Playnite and use **Download Metadata** from
their context menu. Playnite then uses the configured metadata providers and
download settings. Luna import itself supplies source, memberships, links/actions,
and optional cover artwork.

The public Playnite 10 SDK exposes the `DownloadMetadataOnImport` setting but no
method to trigger its built-in metadata download workflow for an explicitly
created batch. `Database.ImportGame` only creates the entry; it does not launch
that workflow. Registering as a library plugin supplies the Library group, and
does not trigger metadata downloads for browser imports. Automatic enrichment
would require a separate metadata-provider integration or a redesigned import
workflow. This preview uses the supported manual command.

## Mapping, recovery, and concurrency

`src/LunaCatalog` contains a host-independent import service and mapping store;
`src/LunaPlaynite` supplies the SDK adapter. All database reads/creates use the
SDK on the WPF dispatcher, while orchestration, JSON I/O, and image work run on
workers. The service serializes its operations. The adapter also checks identity
and creates within one dispatcher call, preventing a duplicate when a game is
added elsewhere while an optional cover downloads.

`imports-US.json` lives beside the catalog cache in the plugin data directory.
Schema 1 stores `(region, LunaId, ASIN, Playnite GUID)` mappings. Writes use a
unique temporary file, disk flush, and atomic replacement, retaining a valid
primary as `.bak`. Limits are 4 MiB, 10,000 entries, and JSON depth 64; duplicate
properties, duplicate identities/GUIDs, malformed records, and trailing data
are rejected. A corrupt primary may recover from its valid backup. A newer
schema is not overwritten or downgraded to an older backup. Unreadable state
disables imports and leaves catalog browsing available.

Each created game also carries `LunaPlaynite:1|US|<LunaId>|<ASIN>` in its provider
`GameId`. This durable marker establishes ownership and supports reconstruction
after a missing mapping file, backup restoration, or a save failure after a host
commit. Plain Luna URLs or matching titles in unmarked games are not adopted.
The marker retains the ASIN even for slug-only Luna links. An ASIN-only marker
can map to a newly observed native ID without rewriting host metadata. Native-ID
conflicts, ASIN conflicts, and multiple owned matches block that game's import.

Both current library-owned markers and legacy manual markers are recognized.
Reconciliation removes mappings to deleted or foreign entries, reconstructs
missing mappings from owned entries, and retains mappings for owned games absent
from the current catalog. It runs on opening, refresh, before every create, after
each batch, on database changes once idle, on activation, and on the view's age
timer. Mapping storage is checked before creating any game. If a later mapping
save fails, the committed game is reported as added with a warning and the batch
stops; markers make subsequent retries safe. A host exception after a committed
create is likewise recognized without creating a second entry.

## Verification and remaining host checks

Fifty-one import/mapping/browser regressions use the existing five-game API
samples, real temporary mapping files, and injected host failures. They cover
idempotency, concurrency, stale/deleted/foreign mappings, recovery, identity
migration/conflicts, metadata preservation, partial commits, cancellation,
corruption/size/depth/schema checks, explicit complete-catalog scope, and an
import confirmed while activation starts reconciliation, and cancellation before
that pending import starts.

The Windows WPF harness exercises the production adapter with real SDK models
and an isolated database API: multi-selection, rejected/confirmed Import All,
source/tags/URL actions, library ownership/uninstalled flags, artwork storage, repeated
imports, user edits, deletion/reimport, and mapping reload. The extracted-package
process round-trips and rejects corrupt import mappings using only Playnite's
JSON 10 assembly. The harness also checks marked manual-game migration, untouched
foreign/unmarked entries, and preserved user metadata. Neither harness establishes
actual host database persistence.

On a clean Playnite 10.62 installation, record these results before closing D:

- Import one and several games; confirm source, memberships, artwork, uninstalled
  status, and working Open in Luna actions through the host's action menu.
- Upgrade from `0.2.0`, restart/open the catalog, and confirm earlier marked
  imports appear under **Library → Amazon Luna** with their edits and GUIDs intact.
  Confirm new imports use the same group and library updates import no extra games.
- Run Playnite's manual Download Metadata command for selected Luna games and
  verify the configured providers work; browser imports do not invoke it.
- Filter to a subset, reject Import All, then confirm it and verify the full
  catalog scope and counts. Repeat imports and confirm no duplicates.
- Edit metadata and repeat imports/refresh; confirm all edits remain. Delete an
  imported entry and explicitly import it again.
- Restart Playnite and confirm entries, mappings, and statuses persist. Restore
  a missing mapping file from the owned entries and check conflict diagnostics.
- Cancel/close during bulk work, simulate a host/storage failure, retry, and
  verify useful summaries and preserved committed entries.

Complete [the browser host checklist](playnite-browser.md#outstanding-windows-host-checks)
alongside these checks before Milestone E release validation.
