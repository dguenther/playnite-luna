# Optional Playnite library imports

Milestone D adds explicit single/bulk imports to the Playnite 10 browser preview
(`0.2.7`). Catalog viewing and runtime refresh remain independent of importing.
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
an **Open in Luna** URL action. The saved URL action remains an additional action;
the library supplies the native Play controller when enabled. Entries follow
the installed-state setting below, have no assumed Windows platform,
and belong to the **Amazon Luna** library. The extension registers as a
`LibraryPlugin` so Playnite's **Library** filter uses that name. Source and
platform are separate fields; no PC Windows platform is inferred. URL opening
does not claim entitlement or track a play session.

## Play, Install, and installed-state setting

Under **Add-ons → Extension settings → Libraries → Amazon Luna**, **Mark Luna
games as installed** is enabled by default, including upgrades from
preferences files without that property. Saving applies to existing owned,
marked imports; new imports use the latest saved choice. Startup, library updates,
and opening the catalog also reconcile existing entries. Cancel leaves the
saved choice and library unchanged. Browser preferences share the existing
settings file, and saving them preserves this choice.

Enabled entries have `IsInstalled` and `IncludeLibraryPluginAction` set, show
**Play**, and appear in Installed filters. The SDK Play controller opens their
Luna page and stops immediately with zero session duration, without a Started
event or browser-process tracking. It therefore does not claim a streaming
session, add playtime, or update Play count/last-played through a Started event.
No local installation is performed.

Disabled entries have those two flags cleared and show **Install**. The SDK
Install controller opens the same page. Both controllers prefer the stored
**Open in Luna** URL action and fall back to the **Amazon Luna** link. Existing
imports need no reimport, URL/action rewrite, or catalog request. Titles, artwork,
notes, favorites, playtime, and other edited metadata remain intact.

The Install handoff uses the SDK's installation-cancelled completion event to clear
Playnite's temporary installing state without changing installed state. It
never signals installation success or starts playtime tracking. Playnite logs
the completion as cancellation, and extensions/scripts listening for installation
cancellation may receive that event. Browser-launch failures reach Playnite's
normal installation error handler after cleanup. Other libraries, unmarked
entries, unsafe URLs, and URLs with conflicting ASINs receive no Luna controller.

Playnite library updates refresh the catalog by default. The independent
[automation settings](catalog-automation.md) control expiry tagging (on), new
arrival imports (off), and unplayed expired-game removal (off). Manual Refresh
uses those same optional actions. Legacy marked imports migrate to this library
and follow the installed-state setting; GUIDs, mappings, actions, artwork, and
edited metadata are retained. Other libraries and unmarked games are excluded.

Optional covers use the same first-party downloader and concurrency/size/deadline
bounds as browser artwork. The adapter decodes bounded thumbnails on a worker and
adds PNG files through Playnite's `AddFile` API for new entries. Invalid or failed
covers are omitted. No catalog API requests are needed for an import.

Repeated imports and catalog refreshes do not update existing descriptive metadata.
Title overrides, favorites, completion, playtime, notes, custom artwork, user
tags/categories, links, and actions remain as edited. Metadata ownership beyond
the installed-state/library-action setting and managed expiry tag is intentionally
not inferred. Removal requires its separate opt-in setting, absence from the first
successfully validated and saved refresh, and live ownership/history/activity/open-protection checks.

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

- Import one and several games; confirm source, memberships, artwork, installed
  default, and working Open in Luna actions through the host's action menu.
- Confirm the installed-state setting defaults on for an upgrade and new setup.
  Click **Play** on earlier/new imports and check their Luna pages and immediate
  launch-state cleanup, without fabricated playtime. Uncheck the setting and
  Cancel once, then Save: only Save should change all owned entries to uninstalled.
  Click **Install** repeatedly and check website handoff/cleanup. Re-enable,
  import another game, restart, and verify flags, preferences, edited metadata,
  and URL actions persist in both modes. Other libraries must remain untouched.
- Upgrade from `0.2.0`, restart/open the catalog, and confirm earlier marked
  imports appear under **Library → Amazon Luna** with their edits and GUIDs intact.
  Confirm new imports use the same group. With default settings, library updates
  refresh without adding games; test the optional automation separately.
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

For preview `0.2.7`, also test library-update refresh enabled/disabled, automatic
expiry tagging and enabled removal on the first valid refresh, tag clearing on reappearance,
arrival-only imports/retries when enabled, and opt-in removal that retains played,
busy, opened, foreign, and ambiguous entries. Use an isolated portable library
for deletion/failure scenarios. Verify failures/cancellation retain the saved
catalog and do not count as expiry observations. See [the automation contract](catalog-automation.md).
