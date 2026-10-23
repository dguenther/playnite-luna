# Playnite catalog browser preview

Milestones C and D add a Playnite 10 `LibraryPlugin`, inline sidebar and optional
WPF catalog window, and library imports in preview `0.2.6`. The project started from the official
[10.62 GenericPlugin template](https://github.com/JosefNemec/Playnite/tree/10.62/source/Tools/Playnite.Toolbox/Templates/Extensions/GenericPlugin)
and now registers as a game library to provide **Library → Amazon Luna**,
targeting `net462` and SDK `6.17.0`. The preview packaging target is Playnite
10.62; older host versions have not been verified. The user reports that `0.2.0`
opens in the host and imports appear. **Gate C remains pending the remaining
host checks, including theme readability in the updated preview.**

## Installation and use

Download the `luna-playnite-preview` artifact from a successful Windows CI run,
extract its `.pext`, and open that file with Playnite. Restart if Playnite asks.
The extension adds the monochrome Luna symbol titled **Browse Amazon Luna Catalog** to the
Desktop sidebar. Clicking it displays the catalog inline in Playnite's main
content area. The Desktop main-menu item **Amazon Luna Catalog Browser** opens
an optional separate window; opening that item again activates the same window.
Fullscreen mode exposes neither WPF entry point.

## Sidebar and window lifecycle

The sidebar is a native SDK `SidebarItem` with `SiderbarItemType.View`; its
`Opened` callback returns the existing catalog UI. Registration creates no
catalog services or requests. Services and the shared browser session initialize
on first opening. The exact standalone symbol path from Luna's official website
is embedded in the DLL and parsed locally as vector geometry, without a circle.
Its monochrome fill inherits the sidebar's foreground, following theme and
selection changes like the native icons.
No network icon request or separate runtime file is needed. See
[the asset's source and SHA-256](../src/LunaPlaynite/Resources/README.md).

The sidebar keeps its control while switching tabs. The separate window has its
own control; both bind to one `CatalogBrowserSession` and model. Search, category,
sorting, selection, import status/summaries, and busy state stay synchronized.
Refresh/import clicks cannot queue duplicate operations from the other view.
Filters wrap onto additional lines when the available inline width is narrow.

Switching away stops that view's timer, subscriptions, and artwork requests.
If another catalog view remains open, its current work continues. Leaving the
last view cancels unfinished catalog/import work and saves preferences; committed
imports remain. Reopening waits for cancellation to settle, then restores cached
data and reconciles library status without a catalog API request. Search and
selection survive reopening during the current Playnite session; only category,
sort, and separate-window size persist across restarts. Shutdown cancels the
shared session and hides its sidebar registration.

## Catalog behavior

Opening the browser loads the last validated US catalog from the plugin data
directory without requesting API data. With no saved catalog, choose **Refresh**.
Refresh downloads the four required browse queries through C#, validates and
saves the result, then publishes it. Saved games remain usable during refresh
and after network, validation, storage, or cancellation failures. **Cancel**
stops a pending shared operation. Hiding/closing the last view also requests cancellation.
See [the runtime contract](catalog-data-layer.md) for acceptance and recovery.

Search matches titles without case sensitivity. All, Standard, Premium, and
GameNight filters combine with search. Title sort defaults to ascending, with
stable ID ordering for equal titles; descending is available. Filtering and
sorting change the displayed items, never the cached snapshot, and make no API
requests. A game appears once with all observed memberships. Memberships do not
establish account entitlement.

The window starts with search/category/sort controls and shows visible/total
counts, saved/stale/loading/error state, and the
last successful refresh. Timestamps display in the Windows user's local time;
research evidence remains explicitly UTC. Age is rechecked every minute while
the view is open. The default stale threshold is 24 hours. Select a game and
choose **Open in Luna**, or double-click a game row, to open its validated Luna
page in the default browser. Double-clicking blank space does nothing.

Category, sort, and window size are saved through Playnite's plugin-settings
API. Search and selection last for the current Playnite session. The plugin's stable GUID is
`6f4229ca-70a9-4c65-ae9f-047a367beda9`; catalog files and settings belong to
Playnite's corresponding extension data directory. Unsupported future preference
schemas use defaults without overwriting those settings.

Artwork loads independently and asynchronously from `m.media-amazon.com`.
Requests use no cookies or account authentication, with normal system proxy/TLS
trust, redirects disabled, at most four concurrent downloads, a ten-second
deadline, and a 2 MiB per-image limit. Images decode on workers, become frozen
WPF bitmaps, and are retained in a bounded 96-image memory cache. Recycled or
unloaded rows cancel their requests and cannot display a previous row's cover.
Missing/invalid/offline images use a placeholder. Artwork is not persisted to
disk, so an offline restart may show placeholders while keeping all saved games.

Viewing and refreshing do not create games or update game metadata. Opening the
browser migrates earlier marked manual imports to the Amazon Luna library by
changing only library ownership. Explicit **Import Selected** and confirmed
**Import All (N)** create Luna library entries.
Ctrl/Shift-click selects multiple rows; each row's import status comes from
stable identity markers and database reconciliation. Import All covers the
complete catalog, even while filtered. See [import behavior and recovery](library-imports.md).

## Build and package

Run the [repository build and tests](../README.md#build-and-test). The full
solution compiles on Linux and Windows. `src/LunaCatalogBrowser` is independent
of WPF/Playnite and shares the provider's `net462;net8.0` targets.

On Windows, exercise the WPF bindings and dispatcher independently of the host:

```powershell
& tools/LunaPlayniteSmoke/bin/Release/net462/LunaPlayniteSmoke.exe
```

Package with an installed compatible Playnite 10 Toolbox after locked restore:

```powershell
tools/PackageLuna.ps1 -ToolboxPath 'C:\Playnite\Toolbox.exe'
```

The build stages the plugin, browser model, catalog library,
manifest, and licenses under `src/LunaPlaynite/bin/Release/net462/extension`.
The script checks staging, checks the Toolbox host's SDK and JSON versions,
calls official `Toolbox.exe pack`, and checks the final archive. The package
contains exactly the three Luna DLLs, `extension.yaml`, `LICENSE`, and
`THIRD-PARTY-NOTICES.txt`. Playnite supplies both its SDK and Newtonsoft.Json
10.0.3. The `net462` catalog builds against that JSON version and reuses the
host assembly, with no bundled JSON DLL, private loader, binding redirects,
or resolver handlers. Standalone `net8.0` tools/tests use JSON 13.0.3.
There are no source files, tests, API fixtures, raw catalog responses, or saved
user data in the package.

CI downloads the official Playnite 10.62 portable archive and checks SHA-256
`a31cd00e9942619a1efdc5c8474c3eed20de5f8f1c31f0b1352e336e941e0d1f` before using
its Toolbox. The resulting `.pext` is uploaded as a CI preview artifact, not
published as a release. A generated package alone does not establish host load
or clean-install compatibility.

CI also extracts that exact package and runs a separate .NET Framework process
with no build-time references to the plugin, SDK, or JSON. It preloads the
official host SDK/JSON 10, loads the packaged module, validates representative
API data, and round-trips the production cache. It confirms a single JSON
assembly comes from the host and none is bundled. Cache checks reject duplicate
properties (including nested and escaped names), trailing data, and excessive
nesting; API checks also reject excessive nesting with the actual JSON 10 DLL.
This checks host dependency reuse before host UI validation.

All JSON input uses an explicit depth limit of 64; cache serialization and
deserialization also set `MaxDepth = 64` and disable type-name handling.
This applies the mitigation documented in [the JSON 10 nesting advisory](https://github.com/advisories/GHSA-5crp-9r3c-p9vr).
The catalog project suppresses that specific mitigated NuGet advisory while
retaining other dependency audits. Cache duplicate-property checks run in our
bounded reader because JSON 10 lacks the newer duplicate-property API.

## Outstanding Windows host checks

Record these results before declaring Gate C passed:

- Install the preview on a clean Playnite 10.62 installation; confirm the plugin
  loads without dependency errors and its Desktop main-menu item opens the view.
- Select the Luna logo sidebar icon and confirm the catalog renders inside Playnite.
  Switch between Library and Luna repeatedly, retaining search and selection.
  Open the separate window alongside it; confirm shared filters, selection,
  and operations without control-parent errors. Check a narrow content area.
- Start a slow refresh/import and leave the last view; verify cancellation and
  retained commits. With the other view still open, confirm work continues there.
- Start with no cache, refresh the live US catalog, and confirm all validated
  games appear once, counts are correct, and overlapping memberships remain.
- Search, combine each category filter, switch title ordering, and open a Luna
  page using both the button and a row double-click.
- Repeat opening while offline, cancel/close during a slow refresh, and confirm
  responsiveness, retained data, unchanged timestamps, and clear diagnostics.
- Restart Playnite; verify cached data, preferences, backup recovery, and single
  window behavior. Check the default browser, keyboard focus, DPI scaling, and
  light/dark/custom-theme readability.
- Confirm the package installs without copying or replacing Playnite's SDK/root
  JSON assembly and contains no JSON DLL. Browsing/refresh must preserve existing
  library entries; explicit imports follow [the import checklist](library-imports.md#verification-and-remaining-host-checks).

The automated harness opens a real WPF window and exercises representative
data, bindings, selection, refresh/cancellation, artwork decoding, dispatcher
affinity, stale-failure behavior on recycled artwork rows, and restart persistence.
It also uses real SDK models and an isolated API for production import-adapter checks.
Sidebar checks invoke the production plugin callbacks, host the returned control
inline, switch away/back, open the window alongside it, verify shared filters and
selection, save preferences, reject Fullscreen registration, and dispose cleanly.
Rows extend the host's implicit `ListBoxItem` style, keeping its normal/selected
text colors, templates, and dynamic theme resources. Detail text uses full opacity.
It does not load `LunaPlugin` in Playnite or replace these host checks. Import
acceptance belongs to Milestone D.
