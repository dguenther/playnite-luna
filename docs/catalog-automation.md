# Catalog update automation

Preview `0.2.7` adds four settings under **Add-ons → Extension settings → Libraries
→ Amazon Luna**. Saving settings does not itself fetch, import, tag, or remove
games. Manual **Refresh** and enabled Playnite library updates use the same flow.

| Setting | Default |
| --- | --- |
| Update catalog when Playnite updates libraries | On |
| Tag games no longer available on Luna as expired | On |
| Add newly added catalog games to the library | Off |
| Remove expired games that have not been played | Off |

The existing **Mark Luna games as installed** setting remains on by default.
All five choices support Save/Cancel, survive restart, and preserve browser
category/sort/window preferences. Older settings files acquire these defaults;
newer schemas remain read-only to this version.

## Refresh and coordination

The SDK's customized `ImportGames` library-update callback refreshes the public
US catalog asynchronously through the shared browser session and existing C#
provider. It returns newly created games to Playnite. Disabling the library-update
setting suppresses its API requests; manual Refresh remains available. Opening
the browser alone still loads cached data without requesting the API.

A background session host keeps library-update work alive when a sidebar/window
closes. The two visible views share busy/cancel state and cannot start another
import or refresh in parallel. A library update waits for existing work; cancelling
that wait does not cancel an open view's operation. Playnite's cancellation token
and application shutdown cancel the update. Already committed changes remain.

Only successfully validated, saved network snapshots drive automation. Failed,
incomplete, wrong-region, or cancelled-before-acceptance refreshes preserve the
cache and do not advance expiry observations or mutate the library. A library
automation failure leaves the accepted catalog available and reports a separate
error. Background failures reach Playnite's library-update notification handler.

## Expiry and removal

Products are compared by ASIN within US scope, so ASIN-to-native-ID migration is
not a disappearance or arrival. A game absent from the **first successfully
validated and saved refresh** is tagged or considered for removal according to
the enabled settings. No second refresh or waiting period is required. Failed
refreshes and opening saved data alone do not trigger expiry actions. Reappearance
clears the missing state and only expiry tags recorded as extension-managed.
The tag is **Luna: Expired**. Existing user tags and edited metadata are preserved;
disabling tagging leaves existing tags in place.

Before adding a tag, the adapter rechecks live ownership and tag membership on
the dispatcher, persists managed-tag intent, then updates the library entry.
A failed intent save prevents the tag write. An interrupted library write can
be retried after restart, and managed tags can still clear on reappearance.
Tags already present at the live check are not claimed by the extension.

Removal additionally requires its opt-in setting, a unique owned identity, no
recorded Playnite playtime/play count/last activity, no active game operation,
and no recorded website-opening attempt through this extension. Ownership,
history, activity, and launch protection are checked again immediately before
the SDK deletion. Other libraries and unmarked/manual entries are excluded.
Deletion removes the library entry; there is no local Luna installation to remove.

The extension records Play/Install attempts, game-start callbacks, and catalog
Open in Luna actions for owned games as removal protection, without inventing
streaming duration. Activity from external browsers or direct additional URL
actions may not be observable; removal uses the history/protection actually
recorded in Playnite and the extension. Existing play counts and playtime are
not rewritten. Once automation is initialized, a protection-write failure
immediately blocks automatic removal for the rest of that plugin session,
including an update already in progress. Opening attempts made before
initialization must be persisted before automation can proceed.

## Arrivals, persistence, and recovery

Auto-import adds arrivals after an established baseline, independent of local
search/category filters. A first-ever download without a previous catalog
establishes the baseline and does not import the whole catalog. Enabling the
setting later does not backfill missing library entries; use explicit Import All
for that. Manually deleted entries are not reimported unless they leave and
subsequently reappear as catalog arrivals. Failed arrivals remain pending for
retry while the option stays enabled; disabling it clears pending arrivals on
the next refresh. Successful/previously imported entries are skipped using existing durable
identity/mapping reconciliation.

`catalog-automation-US.json` stores the baseline ASIN set, pending arrivals,
missing-product observations, managed expiry tags, and website-open protection.
It contains normalized state, not raw API responses. Writes use unique temporary
files, disk flush, and atomic replacement with a 4 MiB limit. Strict JSON parsing
rejects duplicate properties, excessive depth, invalid scope/schema, identities,
and inconsistent state. Corrupt/newer journals block automatic mutations rather
than restoring potentially stale launch protection from a backup. The existing
validated catalog cache and import mappings retain their separate recovery rules.

The .NET suite includes 19 automation/session regressions; the Windows harness
checks the actual SDK library-update callback, opt-out, failure/cancellation,
settings defaults, expiry-tag preservation, recorded-history guards, and SDK
removal through an isolated database. It reproduces a failed protection save
during an active update with a readable journal, and checks tag-intent ordering
and aborted SDK writes when the ownership save fails. Actual Playnite update/notification and
clean-install behavior remain manual host checks.
