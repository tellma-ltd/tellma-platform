# @tellma/core

The Tellma app framework: the runtime a distribution's single-page app imports. It splits into
entry points along its lazy-loading lines, so the warm-launch bundle carries only what every page
needs, and each lazy boundary (a page family, the hub, the support tools) is an entry point of its
own.

| Entry point | Holds | Loaded |
|---|---|---|
| `@tellma/core` | The distribution API: `bootstrapTellma`, `provideTellma`, `tmFeature`, the declaration functions (`tmEntity`, `tmPage`, `tmCodedReport`, `tmWidget`, `tmOverride`) and `TM_APP_DECLARATIONS`, the merged declarations; the client metadata's types, its loaders (`tmMetadataLoaders`) and the loaded Core chunk (`TM_CLIENT_METADATA`); the HTTP layer (`TmApi`, `TmApiError`, `TmProblem`), its plain-JSON value types (`TmJson`, `TmDecimal`, `TmId`) and the exact decimal helpers `TmDecimals`; telemetry (`TmTelemetry`) and the app's error handler (`TmErrorHandler`, with its records' type `TmErrorRecord`), the request log with Copy diagnostics (`TmRequestLog`), the running build's identity (`TmAppVersion`) and the support facade (`TmSupport`: Report a problem, the diagnostic file and the dump's sections); the page context and page actions (`TmPageContexts`, `TmPageActions`, `provideTmPageScope`) and the help seam (`TM_HELP_ARTICLES`); the session (`TmSession`, `tmSessionGuard`, `tmParseProfileCookie`), the launch mark (`TmLaunch`) and the single-membership rule (`tmSingleLiveTenant`); the client store (`TmClientStore`, `TmStoreRefusedError`, `TM_DOC_FORMATS`) and the tab bus (`TmTabBus`) with its message catalogue; the leader tab (`TmLeader`: whether this tab leads, the hub's status and the tenant's hub events) and the documents only the leader refreshes (`TM_LEADER_DOCUMENTS`); the boot script's result types and the app clock (`TM_NOW`); the derived router's pieces: the root component (`TmApp`), the route data (`TmRouteData`), the title strategy (`TmTitleStrategy`), the URL builders and classifier (`TmUrls`) with the search query's types; the entity registry (`TmEntities`, with its descriptors once the entity model loads, and the declaration types `tmEntity` takes, typed paths `TmPath` included), display names (`TmDisplayNames`, `tm-display-name`), client permissions (`TmPermissions`) and `tmRowsToObjects`; localization: `TmLocalization` (the language, the regional formats, the calendar and the zones, and their switches with the coalesced locale write), `TmServerText` (server codes and keys through the string packs) and `tmEnglish()`, the platform's English; the device time zone (`TmDeviceZone`: the per-device option that makes a change of the device's zone automatic, and the check that offers, once per change, to follow it; its zone document's types `TmZoneDoc` and `TmZoneUser`), whose check, prompt and document writes are lazily loaded chunks; the app-level keyboard shortcuts (`TmShortcuts`, `TmShortcut`, `TM_RESERVED_SHORTCUTS`, `TM_BROWSER_KEYS`, `TmShortcutConflictError`) and the shell frame's seams (`TmShell` with immersive mode, `TmShellMain`, `TM_SHELL_LAYOUT`); the banners (`TmBanners`) with the input-idle clock (`TmInputIdle`), the attention documents (`TM_ATTENTION_DOCUMENTS`, `TmInboxSummary`, `TmAssignmentsSummary`) and the toast durations (`TM_TOAST_FEEDBACK_MS`, `TM_TOAST_ARRIVAL_MS`); the navigation registry (`TmNavigation`: the launcher's sections and entries, and a stored URL's state), the pins model (`tmPinsKey`, `tmPinnedBarVisibleKey` and the pin types) and the launcher's provider seam (`TM_LAUNCHER_PROVIDERS`) | eager |
| `@tellma/core/attention` | The attention surfaces every page shows: the banner host (`tm-banner-host`), which renders the banner row from `TmBanners` and announces each banner as it is queued; the attention model (`TmAttention`: the bell's and the tray's badges, the open panel, the verbs with their same-frame overlays); the arrival engine (`TmArrivals`, with the `tmNotificationToastsKey` preference); the outcome toasts (`TmOutcomeToasts`, whose toasts load as a chunk with the first outcome); and the bell's and the tray's app-bar buttons (`TmAttentionButton`, under `tm-bell-button` and `tm-tray-button`), whose panels load as a chunk of their own on first intent | eager, preloaded with the app |
| `@tellma/core/attention-rows` | The attention rows the panels, the arrival toasts and the home page share: the server text of notifications, jobs and assignment captions (`TmAttentionText`), the assignment list (`tm-assignment-list`, in its tray and home variants, either party), `tmIcuArguments`, and the row anatomy and keyboard model the attention lists share | with the code that imports it |
| `@tellma/core/shell` | The shell frame both shells render (the skip link, the `header` with the app bar, banner row and pinned bar, the one scrolling `main`, and the reserved `aside`); the tenant shell with its app bar (the wordmark, Menu and search, the installed-mode Back and Forward, a lazy chunk loaded only once the app runs installed, the distribution's slots, the reserved bell and tray boxes, the markers and the session button) and its states; the session menu, the markers' popovers and the slots' panels, a lazy chunk the app bar loads at idle; the About and Shortcuts dialogs, a lazy chunk each; the tenantless shell with the tenant picker at `/tenants` and the signed-out page, a lazy chunk of this entry point that their routes load; and one placeholder page for the platform's page families | eager, through `import()`; the overlays, the dialogs, the installed-mode history and the tenantless pages lazy |
| `@tellma/core/memory` | The tab's memory of its pages: page state (`TmPageState`, `tmPageStateKey`) and tree expansion (`TmTreeExpansion`), which the page hosts load with them | lazy |
| `@tellma/core/drafts` | A tab's unsaved form copies (`TmDrafts`), which the hosts that edit records (the details page, the stock pages, the report studio) load with them | lazy |
| `@tellma/core/page-snapshot` | `tmSerializePageContext` and `TmPageContextSnapshot`: a page context read once into plain JSON, for the support dump and a future assistant | lazy, through `import()` by its callers |
| `@tellma/core/recents` | The recently visited pages (`TmRecents`), which the launcher and the home page list; the app loads it with `import()` after its first render, when visits start to count | lazy, through `import()` after first render |
| `@tellma/core/entity` | The view side the search and details pages share over the entity model: select plans (`tmSelectPlan`), which turn column keys into one query's terms and type its rows; value formats (`TmFormats`); the typed value in view mode (`tm-value`); a plan column's read-only grid column (`tmGridColumnFor`); the context of the pages an entity picker opens (`TmEntityPickerContext`); the client check of a picked file against its blob kind (`tmCheckBlobFile`); the entity actions (`TmEntityActions`) with their dialogs (`injectTmActionDialog`) and the confirmation loop (`TmConfirmations`), whose dialogs load on first use; the server error paths a form's errors land by (`tmSplitErrorPath`, `tmWalkErrorPath`); client Queryex: the lexer (`tmQxLex`), the tree builder (`tmQx`) and the canonical printer (`tmQxPrint`); and column filters: the `f.` value (`tmParseFilter`, `tmFormatFilter`), its leaf (`tmFilterLeaf`) and its chip sentence (`tmDescribeFilter`, `tm-filter-description`) | lazy, with the page families |
| `@tellma/core/entity-editing` | The edit side over the entity model, which a page or dialog loads when it edits and a read-only page never reaches statically: the typed value in edit mode (`tm-value-editor`); the edit mode of the details layout kit's fields, which `tm-field` loads with `import()` on the first edit or once the record may be edited; the entity picker binding (`TmEntityPickers`), the entity fields over it (`tm-entity-field`, `tm-entity-multi-field`) and the page their pickers open (`TmLazyPickerPage`); and the column of a form's child grid that edits its cells (`tmEditableGridColumnFor`), whose paste resolver loads on the first paste. `@tellma/core/entity`'s generated action dialog loads it with `import()` | lazy, with the editing pages and dialogs |
| `@tellma/core/uploads` | A form's uploads (`TmUploads`): the paths with an upload in flight and the blob each staged, which a form's host provides (a details record does in every mode) and the value editor lists its uploads in; an entry point of its own so that the search page, which imports `@tellma/core/entity`, never carries it | lazy, with the details page or the edit side |
| `@tellma/core/details` | An entity record's details page: the routed host (`TmDetailsHost`) behind every `/{t}/{segment}/{id}` and `new` route, which reads, views, edits and creates the record; the source page (`tm-details-page`, `TmDetailsPage`) over any other `TmDetailsSource`; the entity source (`tmEntitySource`); the record's context a layout reads with `injectTmDetails()`; the layout kit a layout is written in (`TmDetailsKit`: `tm-details-card` and its sections, `tm-field` with its own edit and view templates, `TmFieldGroup`, `tm-details-tabs`, `tm-details-aside`, `tm-details-audit`, `*tmDetailsExtra` and `tmDetailsTitleExtra`); and the validation every record's form gets (`tmDetailsPlatformSchema`) | lazy, with the first details route |
| `@tellma/core/dashboards` | The dashboard grid (`tm-dashboard-grid`, `TmDashboardGrid`) with its tiles (`TmDashboardTile`) and its layout (`tmPackDashboard`); the tenant home: the host the home route renders (`TmHomeHost`), which renders a feature's `home` hatch in place of the platform's home page (`TmHomePage`), its greeting and date line (`TmHomeGreeting`) and its widgets (`TM_HOME_WIDGETS`); and the bodies of the core widget types, which their declarations load | lazy, with the home route |
| `@tellma/core/column-filter` | `tm-column-filter`, the control that edits one column's filter: the operator, the values each column kind takes (a text input, exact number inputs, a date or a date range, an enum's checkboxes, multi-select or searchable multi-picker, true or false, related rows), Clear and Apply | lazy, when a column header's filter popover first opens |
| `@tellma/core/expression-editor` | `tm-expression-editor`, the editor of one Queryex clause: a fixed-height box with the code drawn in colour under a native textarea, a status line with the problems list (F8 and Shift+F8 step through them), the clause's parameter chips, an insertion at the caret as one undo step (`insertAtCaret`) and a dropped path re-spaced, a form field control and a grid's popup cell editor; the draft its editors of one query share (`injectTmQueryexDraft`), which checks every clause with `queryex/discover` once the typing pauses; and the Queryex text it reads and writes beside the lexer: a text's parameters (`tmQxParameters`) and an insertion that never splits a token (`tmQxInsert`) | lazy, with the search page's advanced filter, the role editor's permission filters, the report studio and report parameters |
| `@tellma/core/path-picker` | `tm-path-picker`, a root entity's fields as a tree: column keys or Queryex paths, navigations as branches, the multilingual twins, image and file navigations, date parts in the active calendar, a search three navigation steps deep, `tm.path` drags whose text is valid Queryex, activation and selection; paths the user cannot reach never show | lazy, with the search page's column chooser and advanced filter, the role editor and the report studio |
| `@tellma/core/search` | An entity's search page and the drill-through list, one host (`tm-search-host`, the entity routes' component): the page header with Create, the declared page actions, Refresh and the pager; the grid over one read per query, with its links, skeleton rows and the results region's states; the host's page context and page actions. A declaration's results component and display cells reach the page through `injectTmSearch` and the `TmSearchCell` base. Its query model (where a page's query state comes from, its canonical URL, how a change reaches history and the preferences, the rows request and its diagnostics) is internal; the types a declaration and the details page name (`TmTileDeclaration`, `TmCustomFilterDeclaration`, `TmSearchPageActionDeclaration`, `TmSearchContext`, `TmColumnPreference`, `TmSearchSnapshot`) are the primary's, types only | lazy, with the route |
| `@tellma/core/leader` | The leader election of the routed tenant over a Web Lock, and the leader tab's work: its realtime connection, the relay of hub events, the leader documents' reads and the polls of degraded mode; the app loads it with `import()` after its first render, and `TmLeader` reports what it decides | lazy, through `import()` after first render |
| `@tellma/core/realtime` | The leader tab's realtime connection to its tenant's hub over `@microsoft/signalr`, which nothing else imports, with its own reconnect loop; platform-internal | lazy, through `import()` once a tab leads |
| `@tellma/core/updates` | The app's updates through its service worker (`TmUpdates`): the worker's registration through the `tellma` Trusted Types policy, the update checks, the reload into a new version and the update banner; the app loads it with `import()` after its first render, and the HTTP layer's update check loads it on demand | lazy, through `import()` after first render |
| `@tellma/core/errors` | Error presentation: `TmErrorPresenter`, whose one table turns a `TmApiError` into an error view, and which shows a write's or an action's error in the shared 5xx or 400 dialog, asks before a step-up, and handles a tenant that is gone or suspended; `tm-error-state` (`TmErrorState`), the inline state a page renders for a failed read | lazy, through `import()` with the first error presented |
| `@tellma/core/support` | The support tools: the Diagnostics panel (`tm-diagnostics`, `TmDiagnosticsPanel`: the builds, the tenant, the tab and its connection, the recent requests with their references, Copy diagnostics, the diagnostic file and Report a problem), the support dialog (`TmSupportDialog`) and the support dump (`tmBuildSupportDump`, `tmDownloadSupportDump`, `tmSupportDumpFileName`), which `TmSupport` loads on first use | lazy, through `import()` by `TmSupport` |
| `@tellma/core/config-check` | `tmCheckAppConfig`, the check of an app's configuration against the client metadata, which the build runs in Node and a development app at startup, and `tmCheckEntityDeclaration`, the check of one entity declaration against the Entities chunk | lazy |
| `@tellma/core/error-paths` | `tmSplitErrorPath` and `tmWalkErrorPath`, the server's error paths onto a Signal Forms tree, which an entity action's dialog and the details page's save land their errors by | with the code that imports it |
| `@tellma/core/search-normalize` | `tmSearchNormalize`, the one text folding the launcher, the shortcuts filter, the field tree and the reports list match with | with the code that imports it |
| `@tellma/core/private` | Wiring shared by the platform's own entry points and packages; no API goldens, no stability guarantee, never imported by app code | — |

The other entry points of the framework arrive with the features they hold, each in the folder of
its name. The package also ships plain assets: `boot/tm-boot.template.js`, the boot script's
template, `styles.css`, the app's global stylesheet, which a distribution's `src/styles.css`
imports first (the tokens, the Latin fonts, the directive stylesheets, `styles/tm-frame.css`, the
shell frame's root variables and grid, and `styles/tm-boot-page.css`, the boot page's styles), and the default brand files
`brand/{wordmark,mark}-on-{dark,light}.svg`.

The platform's icon names live in `src/icons/platform-icons.ts`, which `provideTellma` registers
before each feature's `icons`: a platform surface that draws a new name adds it there. A lazy
entry point whose names the first frame never draws registers them from its own chunk instead,
each name only when the app has not registered it, so a feature's own icon wins: the launcher's
in `/shell`, and the tiles' states and the recent pages' launcher icons in `/dashboards`.

The brand files are the Tellma logo: the wordmark, and the mark (the wordmark's round "a" with its
teal dot), each in ink for light backgrounds and in white for dark ones. They are cleaned from the
logo's SVG exports: one `<path>` per glyph with its fill, no clip paths, ids or editor metadata,
so two of them inline on one page cannot clash. The app renders them inline and gives them their
accessible name. `tellma-client build` copies each to `brand/` in the output unless the
distribution's `public/brand/` has a file of the same name. It also reads each file's `viewBox` and
defines its ratio for the app's code; the shell frame writes them on the root element as
`--tm-brand-<name>` (such as `--tm-brand-wordmark-on-dark: 3.5384615384615383`, the viewBox's
width over its height), and every image of a brand file takes its width from its height times that
ratio, so its box is final whether its file has loaded, is still loading or failed.

## Boot

Before Angular starts, a blocking classic script, `tm-boot.js`, decides the session, the language,
the direction, the theme and the launch target with no network. Its logic is the pure function
`tmBoot(env)` in `src/boot/`, over an injected environment; `src/boot/tm-boot.script.ts` calls it
with the real globals. The package build bundles that entry with esbuild (one IIFE, ES2017, so a
browser too old for the app still parses it and shows the unsupported page) into
`boot/tm-boot.template.js`, generated and not checked in. Its first statement is
`var TM_BOOT=/*TM_BOOT*/null;`: a distribution's build replaces the marker with its boot
configuration (the offered languages and each one's boot strings, which Core's resources carry as
`Boot_*`) and serves the result as `tm-boot.js`, the first script of `index.html`. Rendered with
the platform's English strings, which every distribution embeds as the fallback, it stays within
3 KB gzipped, and each further string table may add 0.4 KB; a test in `client/tools/tests/` holds
English-only, English and Arabic, and Arabic-only configurations to that budget.

In order, the script:

1. shows the boot page (`unsupported`) when `navigator.locks`, `BroadcastChannel` or IndexedDB is
   missing;
2. runs `/signed-out` with no session;
3. without a valid profile cookie: shows the boot page when cookies are off (`cookies`) or when a
   sign-in came back without a session less than 60 s ago (`sign-in-failed`); deletes an expired
   user's stored data (their key range in `tm-app` and the `tm-images-v1` cache, in a 1 s box,
   never creating the database) after clearing the boot record's user; then writes the sign-in mark
   (`sessionStorage['tm.login']`) and leaves for `/bff/login`;
4. with one: consumes the mark, restores the fragment no server hop returns, decides warm or cold
   against the boot record (`localStorage['tm.boot']`), picks the language, rewrites a warm `/` to
   the last tenant, and works out whether the entry is automatic.

It sets `<html lang dir data-theme>` and the title before the first paint, and leaves its decision
on `window.__tmBoot`, which the app reads once. The boot page is built with DOM calls only, so a
Trusted Types policy needs nothing, inside `<tm-app>`.

The profile cookie parser, the sign-in mark and the boot record are shared by the script and the
app, so both agree on what counts as a session. The client store's frozen layout
(`src/state/store/`) is pinned by `tm-store-layout-vectors.json`, which the expiry delete and the
client store test against.

## Session and launch

`TmSession` starts as the user the boot script read from the profile cookie, and reads the cookie
again on every `check()`, never the network: `same`, `missing` (the expiry path) or `changed` (the changed-user path, which restarts
the app at `/`). `tmSessionGuard` runs the check on every route but `/signed-out`. `signIn()` writes
the sign-in mark and leaves for `/bff/login` in the document's language; a step-up adds
`acr_values` and `max_age`. After a sign-in it consumes the mark the boot script reported: it
reports an expiry that led there and drops a previous user's drafts, and tells the user's other
tabs that the session started.

The paths beyond the tab's own state reach every tab of the user over the tab bus. The primary
holds the session's state (`active`, `expired` or `signing-out`), the expiry's first step (the
state turns `expired` at once, after the expiry is reported and flushed) and the routing of the
other tabs' session messages; the flows themselves are a chunk of their own, which loads after the
first render. A chunk that cannot load falls back to what the boot script does: an expiry or a
session signed out elsewhere deletes the stale cookie and reloads into the boot's expiry branch,
and a changed user restarts the app at `/`.

- **Sign-out** (`TmSession.signOut()`) shows the busy overlay in the click's frame, asks every
  tab of the user for its unsaved drafts and running saves (`drafts.query`, 300 ms to answer),
  and waits up to 60 s while any tab saves, saying so. With nothing at risk it signs out with no
  question; otherwise it asks once, and a confirmed sign-out never asks again. It reports and
  flushes the sign-out, turns every tab `signing-out`, writes `/bff/logout`, runs the session
  clean-up (every user's stored data, every other `tm` store but the store's database and the
  boot record, the boot record's user and last tenant, and the user's drafts), tells the other
  tabs, which follow to `/signed-out`, and follows the identity server's sign-out. A failed
  sign-out restores the session and says so in a sticky toast; the other tabs lift their overlay
  after 15 s.
- **The expiry path** runs the same clean-up but keeps the drafts and the boot record's user and
  last tenant, deletes the stale cookie, tells the other tabs, and shows the session-ended alert
  dialog in every tab, whose only way on is Sign in. An expiry within a minute of coming back from
  a sign-in shows the sign-in-failed boot page instead, so a server that keeps refusing fresh
  sessions cannot loop the tab through sign-ins. A clean-up sweep cut short (1 s), failed or
  unable to reach the disk clears the boot record's user, so the next launch sweeps again.
- **A changed user** drops the previous user's drafts, tells the other tabs and restarts at `/`.
- **Signed out elsewhere**, which the leader tab hears from the hub, cleans up for every tab and
  sends them all to `/signed-out`.
- **Re-checks** run the cookie check when the tab comes into view and when it returns from the
  back-forward cache.

`TmLaunch` holds the tenant this document entered without the user choosing it, seeded from the
boot script and set by the launch redirect; it ends at the tenant's first 2xx, at a route into
another tenant, or when a `404 tenant-not-found` takes it. `tmSingleLiveTenant` is the
single-membership rule, run over `src/launch/launch-landing-vectors.json`, which the server's
landing test links too.

## Client state

`TmClientStore` (`src/state/store/`) is the only owner of IndexedDB and of the boot record. One
database, `tm-app`, holds every cached document in one object store, `docs`, keyed `[p, n]`: the
partition's key (`device`, `u:<sub>/`, `u:<sub>/t<tenantId>` or `u:<sub>/s<sessionHandle>`) and the
stored name `<name>@<format>`, whose format comes from `TM_DOC_FORMATS`. A partition is a key
range, so a user's, a tenant's or a session's data goes in one delete, and each build reads and
writes only its own formats' copies, so tabs of two builds run side by side after a deployment.
The layout is frozen: the boot script's expiry delete uses it too, and both test against
`tm-store-layout-vectors.json`.

- **The mirror.** `doc()` returns a signal over this tab's copy, which `readAll` (one readonly
  transaction over the partitions' ranges), `put` and `patch` fill. A tenant's documents read as
  absent unless its `sync` document is within the idle window (the stored distribution's
  `session.idleDays`, else 7 days), and the user's `tenants` and `distribution` once older than it.
- **Writes.** `put` keeps a stored copy saved later; `patch` reads and writes in one readwrite
  transaction, and `undefined` deletes. The cache refresher's internal writes store several
  documents of a partition in one transaction with one `store.changed`, and show a write's result in
  the mirror ahead of the write that stores it. Each commit posts `store.changed` with the stored name, and
  other tabs re-read what they mirror of it. A write is refused with `TmStoreRefusedError`
  (`other-user`, `cookie` or `session`) unless the session is the partition's user's and active.
- **Sweeps.** `session-ended` deletes every user's partitions and the image cache;
  `{ sessionOtherThan }` the user's other sessions; `{ tenant }` one tenant. The device partition,
  which holds only `zone`, survives every sweep. A sweep posts its key prefix, and every tab
  re-reads its mirror under it. A sweep wins over every write called before it, on disk and in the
  mirror; a write called after it lands after the delete. It resolves `true` once its delete
  committed (or nothing is on disk), and `false` when the store runs in memory while a database it
  could not reach may hold documents.
- **The boot step**, `ɵstart(tenantId)`, an initializer of the app's start: it waits for the tab
  bus, sweeps every user when the boot record names none or another user, reads the device, user,
  current session and routed tenant partitions in one readonly transaction (a warm launch's only store access),
  records the user in the boot record (unless its sweep could not reach the disk, so the next launch
  sweeps again), and sweeps the user's other sessions at idle after the first render. It never
  rejects: a storage fault after the open puts the tab in memory, cold.
- **Fallbacks.** A quota refusal drops the write and keeps it in the mirror. When IndexedDB fails to
  open within 3 s, or a transaction fails later for a cause other than the caller's (a lost
  connection, a failing backing store), the store reports it and runs in memory (`mode` is
  `memory`): the write stays in the mirror and reads resolve with what the mirror holds, so no read
  or write rejects for the storage's sake. When local storage refuses, the boot record lives in
  memory. Each reports `tm.store.unavailable` once. A store host marked
  `memory`, as in tests, runs in memory by design and reports nothing. A `versionchange` closes the connection, which reopens on next use.

`TmTabBus` (`src/state/bus/`) carries every cross-tab message over two `BroadcastChannel`s: the
device channel `tm:<slug>` carries the `session.*` messages, which every user of the browser hears,
and the user channel `tm:<slug>:<sub>` the rest. Each message travels in envelope `v: 1` with the
posting tab's id; a tab ignores an unknown version or type and never hears its own posts. The
channels open once the Core chunk names the slug (earlier posts wait for them), close while the
page sits in the back-forward cache, and reopen when it comes back, when the store re-reads every
mirrored document.

**The app's end.** The root services resolve some collaborators through the injector when they use
them, and a destroyed injector throws on every lookup. So a service that defers work, to a frame, an
idle moment, a timer, a microtask or the end of a read, checks `DestroyRef.destroyed` before it
resolves anything:

- The display names' frame flush still runs after the end, and returns without reading.
- The cache refresher's timers stop, and its tenants' syncs and background reads are cancelled.
  After the end it arms no timer and knows no partition, so a late `adopt`, `refresh` or `observe`
  does nothing.
- The preferences send no flush and arm no timer after the end; a write that lands then is dropped
  without a failure.
- The launch cache loads no list whose load the end overtook before its read; a read in flight is
  cancelled with the refresher's background reads. The Entities chunk keeps no model that lands
  after the end.
- The locale write's timers stop. A write that lands after the end is not adopted, an offline
  failure plans no further try, and nothing queued behind it is sent.
- The HTTP layer sends no call made after the end, and no retry whose wait the end overtook; each
  rejects `cancelled`. A call whose answer lands after the end rejects `cancelled` too, because the
  answer can no longer be checked against the session: the launch is not settled and its version
  tags are not reported. So does a call that fails at the network level or times out after the
  end: it plans no retry and does not mark the server unreachable. So does an outdated-client
  refusal whose verdict the end overtook, while its build rules loaded or the worker's update check
  ran, without its verdict event. Version tags due one
  task after an answer are dropped if the app ends in between. A write cancelled this way was
  still sent, and it may have completed on the server.
- The reachability probe arms nothing after the end: its timer stops, and a probe in flight at the
  end that fails is the last one.

### Sync and the launch documents

`TmCacheRefresher` (`src/state/sync/`) is the one path to `client/sync`, which answers a tenant's
`settings`, `me`, `tenant` and lists' tags by the tags the tab holds. `provideTellma` makes it the
HTTP layer's cache seam, so every response's version-tags header reaches `observe`.

- **`observe(t, tags, trigger)`** syncs when a reported `settings`, `preferences`, `permissions` or
  `entities` tag differs from the held one; a list's tag only marks the held list stale.
- **`refresh(t, docs, trigger)`** syncs unless a sync sent after the call made it needless (a
  `sync.at` ahead of the clock, which was set back since, counts as unknown); `['access']` ends
  early while the held envelope's `validUntil` is ahead, and holds no `permissions` tag so the
  server decides the grants; `['entities']` and a list's `['entity:<Name>']` hold no composite, so
  the answer reports every list's tag, and a list's call ends early once its tag is known; a
  `cold` call ends early once `settings`, `me` and `tenant` are all present. It rejects only for
  `cold`.
- **A run.** Calls join the tenant's pending run (a `cold` call makes it cold). The run queues on
  the Web Lock `tm:<slug>:<sub>:<t>:sync`, re-reads `settings`, `me`, `tenant` and `sync`, and ends
  there when every joined call is needless; otherwise it waits out a 2 s throttle since the tenant's
  last sync in any tab (2 s at most), then sends one background read (a cold one goes in the
  foreground, which renews the session; the server never stamps activity for `client/sync`) and
  stores the answer in one transaction, `sync.at` being the send time. A list the server calls stale while the held tags
  say otherwise loses its tag and the composite, so the next sync reports every list's.
- **`adopt(t, docs)`** stores a write's own results (a settings save, a profile save, a preferences
  or locale write) as server truth: the tab shows them at once and the store writes them under the
  sync lock, so the write's own header finds its tags held. A sync stored in between keeps them
  shown: the store makes the adoption's change again on the sync's copy.
- **The boundary.** At the routed tenant's `validUntil`, plus a 0-2 s jitter and only while the page
  is visible, the refresher runs `refresh(['access'], 'boundary')`; a boundary the server keeps (a
  clock running ahead) backs off from 30 s, doubling to 10 min.
- **Failures.** `tenant-not-found` and `tenant-unavailable` go to the error presenter seam, even from
  a background sync; a cancel, a session end and an outdated client are left to their owners;
  anything else is reported as `tm.sync.failed`. Leaving the tenant or the page aborts its syncs,
  and a call after that starts a new one.
  Each sync records `tm.sync.completed` with its trigger and what it changed.

`TmLaunchCache` (`src/state/launch/`) serves what the chrome renders from: the routed tenant's
`docs` (`settings`, `me`, `tenant`) and `warm` (all three present), the user's `tenants` and the
distribution's facts. Entering a tenant reads its partition once and runs a cold sync when a launch
document is missing; a failed entry rejects, and every entry after one settles checks the
documents again. `entitySet(name)` reads an entity's whole list: its first read while the list is
absent or stale loads `{segment}/all` (a sync first when the tab holds no tag for it, unless a sync
in flight reports it), and a stale list keeps serving until the newer one
lands. A refused list stores nothing and is tried again once the caller's grants move. The user's
tenants and the distribution are read again in the background once per launch, at idle, and the
tenants on every tenantless route.

### Preferences

`TmPreferences` (`src/state/prefs/`) gives pages typed per-tenant preferences, each declared once
with `tmPreferenceKey(key, kind, fallback, validate?)`. In development the declaration checks the
server's key grammar, and the first `get` or `set` checks that the first segment is a platform
segment (`nav`, `search`, `report`, `dashboard`, `home`, `announcements`, `notifications`, `tm`) or
a composed feature's key.

- **Reading.** `get(key)` follows the routed tenant: the overlay (the last value this browser set
  that this tab has seen), else the stored bag `me.preferences`, else the fallback. Values are
  stored as `JSON.stringify`; each distinct string is parsed once, and one that is not JSON, or
  fails `validate`, reads as the fallback with `tm.prefs.invalid { family }`.
- **Setting.** `set` changes the overlay in the same frame, posts `prefs.changed` with the stored
  string, and schedules the flush; a value deep-equal to the fallback deletes the key, and `reset`
  does the same. A stored form over 32 KB is refused before it applies (`tm.prefs.dropped
  too-large`; it throws in development). Outside a tenant, `get` returns the fallback and `set` does
  nothing. Another tab's value replaces the overlay entry and drops this tab's unsent one; while
  this tab's write of the key is in flight, the later value stays dirty and is saved after it, so the
  bag ends on the later choice. An overlay entry ends once the bag holds its string and this tab has
  nothing unsaved for it.
- **Flushing.** Curated keys flush at the end of the task, coalesced; write-behind keys 2 s after
  their last change and at most once per 10 s, at once when the page hides or the stored form
  exceeds 8 KB; `nav.recent` at most every 5 min, the hidden flush included. A flush writes
  `users/me/preferences/set` and `/delete` through `TmApi.write` and adopts each result. On
  `pagehide`, every unsaved key, in-flight ones included, goes through `TmApi.send`, newest first,
  both bodies within 16 KB (`pagehide-cap` drops the rest). Nothing flushes, and `pagehide` sends
  nothing, while the tenant's stored state is `Frozen`; the keys flush once it leaves it.
- **Failures** are silent. `offline`, `unavailable` and `server` keep the keys and retry after 10 s,
  or at once when `TmApi.online` turns true; `read-only` drops them (`read-only`); the bag's key cap
  drops the write (`key-cap`); another validation code drops the keys it names (`rejected`) and saves
  the rest again; a dropped key leaves the overlay and every tab hears its stored string again.

### The tab's memory, drafts and recent pages

`@tellma/core/memory` holds what a tab remembers of its pages beyond the store, and
`@tellma/core/drafts` its unsaved form copies. Each loads with the page hosts that use it, not with
the app. They are two entry points because an app's bundler places the code its routes use from
one entry point in chunks that every route importing that entry point loads: in one entry point,
the details page's drafts would load with the search page, which keeps none. The size gate's
static-graph check (`"tellma".staticGraphExcludes`) proves that neither the primary nor `/search`
reaches `/drafts` through static imports. The drafts' storage and keys stay in the primary
(`src/state/memory/tm-draft-keys.ts`), since the session's sign-in sweep removes a previous user's
drafts.

- **`TmPageState`** remembers, in memory, up to 20 pages' state by canonical path
  (`tmPageStateKey`), least recently used out first. `markStale({ tenantId, entity? })` marks the
  pages `TmUrls.describe` maps to the entity (a hatch's extra segment maps by its host), or every page
  of the tenant; a move of the tenant's held `permissions` tag does the same, and a change of its
  languages forgets the tenant's pages. `remember(key, state, { keepStale: true })` keeps a stale
  mark a write set while the page was away.
- **`TmTreeExpansion`** keeps a tree page's expanded ids in the session partition's `tree:<page>`,
  oldest first and at most 500, written 250 ms after the last change; each write carries only the
  changes no earlier write has in flight, and settles only its own. The boot step reads the
  session partition with the others, so a tree's first load finds them.
- **`TmDrafts`** (in `@tellma/core/drafts`) keeps unsaved form copies in session storage under
  `tm.draft:<sub>:<t>:<entity>:<id>`, as `{ v: 1, at, data }`: written 300 ms after the last save and
  at once when the page hides, refused for another user's key, and counted in `dirtyCount`. A full
  storage moves this tab's oldest other drafts into memory and retries once; failing that, the draft
  lives in memory (`tm.drafts.quota-exceeded`).

`@tellma/core/recents` holds **`TmRecents`**, which lists the routed tenant's ten most recent pages
from the write-behind key `nav.recent`. The app loads it with `import()` after its first render.
A visit is a search, report, dashboard, settings, access or custom page, not the drill-through
list (route template `/:t/list/:segment`), whose context (`TmPageContexts.current`) stays current
for 2 s while visible; the visit stores the router's path and the context's kind and title. A
stored page takes the title and icon of the launcher entry at its URL from `TmNavigation`, and the
list keeps only the pages whose URL `TmNavigation.resolve` finds `ok`; the access filter runs on
read.

**The dump.** The support host chunk builds the state's support-dump section (`state`, typed
`ɵTmStateDumpSection`): the mirrored documents' partition kinds, stored names and tags, the last
sync, the unsaved preference, draft and page-state keys, never a value, and whether the tab leads
with the hub's status as it sees it. On a tenant's routes, the keys are that tenant's and those of
no tenant. The builder lives in that lazy chunk alone, so the first frame does not carry it. The
preferences, the drafts and the page
state report their keys to it through `ɵTM_STATE_PARTS` when they are created, so reading the
section creates none of them, and a tab that never loaded `@tellma/core/drafts` reports no drafts.

### The leader tab

One tab per tenant per browser leads: it holds the tenant's realtime connection and relays its
events to the other tabs, and it alone refreshes the leader documents. `TmLeader` (in the primary)
tells every tab whether it leads (`isLeader`), the leader's connection as it last reported it
(`hubStatus`: the state, since when, and whether the leader is visible) and the tenant's hub events
by name (`events(name)`), whichever tab received them. The election itself is
`@tellma/core/leader`'s, which the startup loads with `import()` after the first render;
`provideTellmaStateTesting()` replaces the loader, so a test app has no election and every tab
follows.

- **Election.** While a tab shows tenant `t` to an active session, it queues on the exclusive Web
  Lock `tm:<slug>:<sub>:<t>:hub`, and the holder leads (`tm.realtime.leader-acquired { how }`).
  Leaving the tenant, `pagehide`, `freeze` and a session that is no longer active release the lock
  or abort the queued request (`tm.realtime.leader-released { why }`); `pageshow` and `resume`
  queue again. The browser hands the lock to the next queued tab when the leader closes, crashes
  or is discarded, so the healthy case runs no timer. Where the page has no Web Locks, each tab
  leads alone.
- **Probes and steals.** A follower probes the leader (`leader.probe`, answered by `hub.status`)
  when it starts and when it is shown, and every minute while visible if the leader's hub is not
  connected or a leader document asks for polling. Only a visible follower steals the lock: from a
  leader that has not answered within 5 s, or from a hidden one whose hub has been down for over
  30 s; never from a visible leader, nor from one whose realtime is off. It waits a random 0–2 s
  first, and steals only if the holder is still the one it probed; a new holder gets a new probe.
- **The leader's work.** On election it reports `connecting`, revalidates the tenant and reads every
  leader document. It connects the hub when the tenant facts say `realtime`, and reports the
  connection's states to every tab; after every connect it catches up the same way. It relays every
  event of its tenant on the bus; an event this build lists in the Core chunk's `clientEvents` reaches
  `TmLeader.events`, in every tab, and the leader acts on it: `cache.changed` goes to the cache
  refresher, and a leader document's event re-reads the document, coalesced over 250 ms, one read in
  flight and one queued at most. A read's stored copy is stamped with the time it was sent, so it
  never replaces a later copy. A refusal or the end of the user's access stops the connection, and a
  background sync decides what follows: the first sync that lands afterwards, the leader's own or a
  later one such as a poll's, starts the connection again, unless it turns realtime off. When the
  server ends the user's access to the tenant, every follower that hears the relayed event syncs
  too; when it ends the session, the leader signs every tab out.
- **The connection.** `@tellma/core/realtime` holds it, and the leader loads it with `import()` once
  it leads, so the SignalR client never reaches a follower. It negotiates at `/{t}/hub` with the
  session cookie, the tab's user check before every request, the JSON protocol and the library's
  transports in its own order, with the server's timeout at 30 s and the keep-alive at 15 s. The
  library reconnects nothing itself: each attempt is a fresh connection. The first start goes at
  once; after a drop or a failed start, the first attempt goes at once too, then after 2 s, 10 s
  and 30 s, then doubling to 5 minutes, each but the first with ±20 % jitter, and the count starts
  over after a minute connected. A probe or the server becoming reachable cuts
  a wait short; while the server is unreachable it waits in `offline`. An attempt that has not
  connected in 100 s is abandoned, and a stop aborts the requests in flight and settles within 5 s,
  so a hung negotiate or transport never holds anything up. Only a negotiate refused with 401, 403
  or 404, a failed user check (reported as 401) and the server's `session.ended` stop it; it never
  gives up on a network error, a 429 or a 5xx. It reports `tm.realtime.connected { attempt }` with
  its timing, `tm.realtime.disconnected { reason }` and `tm.realtime.refused { status }`, and the
  library's warnings and errors as errors from `tm-hub`. In a test, `provideTellmaStateTesting()`
  puts `TmFakeHub` in its place.
- **Degraded mode.** With realtime off, or a hub that has not connected for a minute, the leader
  polls (`tm.realtime.polling { cause }`): a sync of the tenant and a read of every leader document,
  each at most once a minute, while the leader is visible or a visible follower's probe asks. A
  leader document whose stored copy passes its `pollWhile` is re-read the same way while the hub is
  up.

A feature declares a leader document with a `TM_LEADER_DOCUMENTS` multi provider: its stored name,
the route the leader reads it from, the events that re-read it, its client format version, and an
optional `pollWhile`. Every tab reads it from the store; none but the leader fetches it.

`provideTellma` registers the platform's own, `TM_ATTENTION_DOCUMENTS`: for now only `inbox`
(`inbox/summary`, re-read on `inbox.changed` and `job.changed`, and polled while a job is `Pending`
or `Running`, so a lost job event never strands the bell's progress ring). Its client mirror is
`TmInboxSummary`, and `TmLaunchCache.docs.inbox` reads it for the routed tenant, so the bell and
the home lists render warm with no request. `assignments` (`assignments/summary`, mirrored by
`TmAssignmentsSummary`) joins it once the server maps its route; until then a registered read
would fail on every leader refresh. Meanwhile it is a plain store format in `TM_DOC_FORMATS`, so
`TmLaunchCache.docs.assignments` reads `undefined` and the tray and the home widgets show their
no-data state; do not register it in `TM_LEADER_DOCUMENTS` elsewhere. Each name is registered once:
a second registration of a name throws in development, and production keeps the first.

## The HTTP layer

Every request the app makes goes through `TmApi`, over `fetch`; an upload, the one call that
reports its progress, goes over `XMLHttpRequest`. A route such as `centers/query` goes to the
routed tenant's API (`/17/api/web/centers/query`), or to the tenant a call names; a route with a
leading `/` is tenantless and sent as is. A tenant route with no tenant throws in development, and so
does any URL that is not a path on this origin (`//host`, `/\host`, a `.` or `..` segment); in
production both reject `server` unsent.

- **Reads** take a signal and are retried: a network failure twice (1 s, then 3 s, ±25 %), a `429`
  and a `503` with `Retry-After` twice after the wait it asks for (capped at 10 s and 30 s), and a
  build mismatch blamed on the server once after 30 s. An abort ends a call or a wait at once. A
  read of a route the Contracts chunk describes but does not list as idempotent throws in
  development and runs once in production.
- **Writes** are never aborted or retried, a write while the browser is offline is not sent, and
  `writesInFlight` counts the unsettled ones.
- **`download`** streams the body, reporting bytes; **`upload`** posts the raw file to
  `/{t}/blobs/{kind}`; **`fetchBlob`** fetches an image's bytes for `tm-image`, revalidating with
  `If-None-Match`; **`send`** is a keepalive `POST` of at most 16 KB whose answer is never read;
  **`fingerprint`** names a route's contract fingerprint for drafts.
- **Every call** carries `Tellma-Client`, `Accept-Language` and a fresh `traceparent`; tenant calls
  add `Tellma-Calendar` and, for described routes, `Tellma-Contract`. Reads may add
  `Tellma-Activity: background` and `Tellma-Priority: low`, writes `Tellma-Confirm`.
- **Around every call** the profile cookie is checked before the send and again on the answer:
  no session runs the expiry path and rejects `unauthenticated`; another user runs the
  changed-user path and rejects `cancelled`, discarding the answer. The sign-out's answer, which
  clears the cookie, is not checked, and a retry is checked again before it is sent. A `401` for a
  session the cookie no longer names ends nothing and rejects `cancelled`. Once the session leaves
  the tab (an expiry or a sign-out) only the sign-out write and sends go out, and the reads,
  downloads, uploads and blob fetches in flight are aborted.
- **A watchdog** ends a call that hangs for 60 s, or 6 min for a long route, as `offline`
  (`outcome-unknown`); a download's watchdog restarts at each chunk, an upload's at each progress
  event.
- **Every refusal** is a `TmApiError` whose `kind` says how the platform presents it, whose `code`
  names the cause and whose `traceId` is the reference support quotes. A `412 client-outdated` or a
  `404 route-not-found` is judged by comparing the server's `Tellma-Build` with the UI version by
  SemVer precedence; when the builds do not say, the worker's update check decides, capped at 3 s.
- **`online`** is false while the browser is offline or after a network failure; a visible tab then
  probes `GET /health/live` every 5 s, backing off to 30 s, until any answer.

Two parts of the layer are chunks of their own, loaded with `import()`, because no first-frame
call needs them: the outdated-client verdict's rules (`tm-api-verdict`), loaded with the first
such answer, which order the builds through `TmAppVersion.compare`, and the file transfers (`tm-api-transfer`), a download's streamed read
and an upload's send, loaded with the first download's answer or the first upload. A verdict whose
chunk cannot load leaves the builds to the worker's update check; a transfer whose chunk cannot load
fails as a network failure would.

`provideTellma` points `tm-image`'s blob fetcher at `TmApi.fetchBlob`, so an image request gets the
user check, the session's headers and the caller's signal, whose reason an aborted fetch rejects
with; under `provideTellmaTesting()`, `TmFakeApi` answers it.

## Service worker updates

`@tellma/core/updates` registers Angular's service worker once the app settles after its first
render, or after 30 s, at the root scope with every update check bypassing the HTTP cache. The CSP
enforces Trusted Types, so the script URL goes through the `tellma` policy, created once per
document, which admits only the same-origin `/ngsw-worker.js`. Angular's worker client runs in an
injector of its own, outside development, whose own registration never runs.

`TmUpdates.check()` answers `ready`, `none`, `failed`, `unreachable` or `disabled` from the
worker's event during the check; a worker that already holds a newer version answers "no new
version" naming it, which reads as `ready`. `update()` checks and reloads into the new version, or
reloads plainly when no worker is in the way. The service checks once the worker is registered, so
a version announced before the entry point loaded still shows, when the tab comes back into view
after an hour hidden, and every 6 hours while visible, reloads at once when the worker's cache is
broken, and shows the update banner, dismissible for its version, while a newer version waits.

## Error presentation

`@tellma/core/errors` holds `TmErrorPresenter`. `describe(error, context)` words an error by one
table: its tone, icon, title and message, the server's items, the reference, the actions, and for
a `forbidden` refusal the permission it needs by its label, with the raw resource and action pair
for support. A page renders a failed read inline with `tm-error-state`. `present(error, context)`
shows a write's or an action's error in the shared 5xx dialog (Try again, or Reload, which gets the
update first and offers Reload anyway from the second failed download; with no worker in the way
it reloads at once, without loading the update service) or the 400 dialog, and
registers the open dialog as an overlay page context. A step-up asks before it signs in again. On
any call, a tenant that is gone sweeps its data and goes to the picker, and a suspended tenant
replaces the shell's main area with the tenant-state page. A cancelled call and an ended session
show nothing.

## Features and the merge rule

A distribution's app is one `provideTellma` call: the emitted metadata loaders, the languages it
ships client strings for, and its features, packs first and the distribution last. Each feature is
plain data. The merge rule unites the features in order after the platform's implicit `core`
feature, and `TM_APP_DECLARATIONS` holds the result:

- each declaration has one key: an entity's name, a page's path, a section's or entry's id, a
  settings tab's category, a coded report's or widget type's key; adding a taken key is a problem;
- a later feature changes an earlier key only through `overrides`, member by member (a member it
  sets replaces the earlier one whole, arrays included), and `hide`, which drops a page, section,
  launcher entry, settings tab or app-bar slot;
- coded reports are the server's: every `tmCodedReport` patch merges into the server's entry of its
  key in feature order, and every entry appears in `TM_APP_DECLARATIONS.reports`, patched or not.

```ts
export const acmeFeature = tmFeature({
  key: 'acme',
  entities: [customers],
  pages: [tmPage('credit-review', { component: () => import('./credit-review'), titleKey: 'Acme_CreditReview' })],
  overrides: [tmOverride('section:administration', { order: 10 })],
  hide: ['entry:core.Role'],
});
```

`tmCheckAppConfig` (in `@tellma/core/config-check`) merges the same features against the emitted
Core and Entities chunks and returns every problem: duplicate and dangling keys, page paths outside
the grammar or on a reserved word, declarations naming what the server lacks, and label
fallbacks, which are warnings. It reads declarations as data and never calls a lazy member, so it
runs in Node; `tmAppConfigOf` takes the config back out of an `ApplicationConfig`.

## Bootstrap, routes and URLs

`bootstrapTellma(appConfig)` starts the app zoneless in `<tm-app>`, only when the boot script left
`state: 'run'` on `window.__tmBoot`; otherwise its promise never settles. `provideTellma` derives
everything else from the features: the router and its route table, the startup initializers (the
Core chunk, the string packs of the active language and English, the session and the client
store's boot step, none of which waits for the API), the title strategy and the URL serializer,
which serves a path with a trailing slash at its canonical form. After the first render, at idle,
the HTTP layer loads the Contracts chunk, so the first API call does not wait for it.

The route table is fixed: `/` (the launch redirect), `/tenants`, `/signed-out`, and under `/{t}`
the home, `settings`, `me`, `list/{segment}`, `reports/{key}`, the features' pages, and an
entity's search (`/{t}/{segment}`), create (`new`) and details (`/{t}/{segment}/{id}`) pages. The
entity routes match through `TmEntities.bySegment`, so a stack whose declaration has no `details`
page has no details route, and a child entity has a list but no search page. Anything else shows
the not-found page in place, never a redirect. A route into another tenant is created afresh, its
shell and page included; within a tenant the shell stays.

Every page route renders `TmRouteHost`. It draws the page family's skeleton in the navigation's own
frame, loads the page's chunk beside a cold tenant entry's refresh, and creates the page once the
tenant's launch documents are current. A page that names a `securable` the user lacks shows the
forbidden view in place. A chunk that fails to load asks the service worker for an update and
shows `client-outdated` or `offline`. The `client-outdated` view loads `@tellma/core/errors` at
once and renders its `tm-error-state` in its place, whose Reload gets the update first; when that
entry point cannot load, Reload reloads plainly, which no worker is in the way of. Each page gets its own page scope and leave checks, which a
navigation away runs, a move between My settings sections or into another tenant included; a
settings tab change keeps the page and runs none. On every page route, the host registers a
default page context of the route's kind, titled as the document is, whose route template names
the entity's segment (`/:t/centers`, `/:t/centers/:id`; the drill-through list keeps
`/:t/list/:segment`): `loading` until the page or an error view renders, then `ready`, or `error`
with the view's code (`forbidden`, `offline`, `client-outdated`) among its errors. A reused route
that shows another entity registers it anew. A page, or its family's host, that registers its own
context replaces it and keeps its actions. After a path change, focus moves to the page's
`[tmPageTitle]`, except on a settings tab change, where it stays on the tab list. A family's host
that loads its own page holds the move until that page renders (`ɵTmPageFocus`). The document's
title is the page's title and the display name. A stored report's and a dashboard's details pages
draw the `report` and `dashboard` skeletons.

The launch redirect at `/` leads to the last tenant when the boot record names one for this user,
else to the user's one live tenant, else to the picker; each tenant it picks marks the entry
automatic. At launch it replaces `/`; a link to `/` inside the app pushes, so Back returns to the
page it left. Entering a tenant records it as the boot record's `lastTenantId`.

`TmUrls` builds every URL of the routed tenant (`forTenant` builds another's), parses a search or
list page's query, classifies a stored URL (`describe`) and loads a route's chunks at idle
(`preload`, running no guard). The search query codec prints its canonical form: defaults left out,
`q` stripped of bidi controls, and the free filter in the fragment as `#d=`, base64url JSON, which
no server log sees.

## Shortcuts and the shell frame

`TmShortcuts` is the one keyboard listener for app-level shortcuts: a bubble-phase `keydown`
listener on the document, so a focused grid, editor, menu or dialog handles its own keys first and
wins them by preventing the event. It ignores a prevented key, a composition, an auto-repeat, and
every key while the busy overlay runs or the session is not active. It matches with `tmKeysMatch`,
so `Mod+K` works while an Arabic layout types `ن`. In an editable field only `inFields` shortcuts
run (the default for keys with `Mod` or `Ctrl`), and on an Apple platform a key that Option turns
into another character is typing. While a modal is open only `overModals` shortcuts run, besides
the page shortcuts of the top dialog's own page context, which the page actions register through
the internal `ɵregisterPage`. A shortcut that runs is prevented, stopped and reported as the
sampled `tm.shortcut.used`.

```ts
inject(TmShortcuts).register({ id: 'acme.credit-review.approve-next', keys: 'Mod+Shift+M',
  labelKey: 'Acme_CreditReview_ApproveNext', scope: 'page', run: () => this.approveNext() });
```

The platform and pages draw keys from disjoint families. A `tm.` id registers only its keys in
`TM_RESERVED_SHORTCUTS`; every other id takes `Mod+<key>` or `Mod+Shift+<key>` (except `Mod+K`
and `Mod+/`), `F2`, `Alt+PageUp` or `Alt+PageDown`, and only `dashboard.exit-presentation` takes
`Escape`. The registry also refuses the keys a focused grid handles with a modifier (all but
`Mod+Enter`, which the grid prevents when it acts), `TM_BROWSER_KEYS`, and the keys of a live
shortcut, comparing `Mod` as both Ctrl and ⌘ so a conflict fails on every platform. Development
throws `TmShortcutConflictError`; production drops the registration and reports
`tm.shortcut.conflict` with the id and the reason. The registry's copy of the grid's modified keys
keeps the grid's binding table off the first frame; a unit test holds it equal to `TM_GRID_KEYS`.

The shell frame's rows are root CSS variables in `styles/tm-frame.css`: the app bar (40 px), the
banner row (0, or 32 px while a banner shows) and the pinned bar (32 px), summed as
`--tm-shell-top`, which pages, popovers and the toast host read. `TM_SHELL_LAYOUT` mirrors the
heights, and a tooling test holds the two equal. `TmShell.setImmersive(true, destroyRef)` takes
every row away through an attribute on the root element until the caller gives them back or its
`destroyRef` ends. `TmShellMain` says what the tenant shell's `main` shows: the route host, or a
component the error presenter puts in its place (the tenant-state page), until `restore()` or a
navigation to another path.

Both shells render `tm-shell-frame` from `@tellma/core/shell`: the skip link first, then a
`header` with the projected app bar (`[tmShellBar]`), the banner row and the projected pinned bar
(`[tmShellPinnedBar]`), then `main#tm-main`, the one region that scrolls, and an empty, hidden
`aside`. The frame writes the heights of the rows it shows on the root element while it lives (the
newest frame wins while two overlap in a navigation) and an immersive frame renders no `header`.
The tenantless shell (`/tenants` and `/signed-out`) puts an ink app bar with the wordmark, the
language menu (with two languages or more) and the account menu (My account while the
distribution's identity facts are stored, and Sign out) in it; the signed-out page shows the
wordmark alone. Its pages are a lazy chunk of `/shell`, so a tenant's first frame carries none of
them; `tellma-client build` prefetches that chunk, so a launch to either page fetches it beside
`/shell` rather than after it. Their menus and the avatar's image stay out of the first frame too.
An app's bundler puts the code a lazy chunk uses from a file into the first frame's chunks
whenever the first frame reaches that file through static imports. So the size gate's
static-graph check (`"tellma".staticGraphExcludes`) fails if any chain of static imports from
the primary reaches `@tellma/core-ui/menu`, `@tellma/core-ui/image`,
`@tellma/core-ui/l10n-input` (the parsers a lazily loaded grid or form uses) or
`@tellma/core-ui/kbd` (the key caps a lazily loaded menu or dialog draws), even one the primary
never uses; the same check keeps `@tellma/core-ui/kbd` off the eager `/shell` and `/attention`.
The primary takes `TM_BLOB_FETCHER` from `@tellma/core-ui/blob-fetcher`, and the shortcut
registry and the app bar take the keys grammar from `@tellma/core-ui/keys`, for this reason. The tenant picker renders the stored memberships in its first frame and reads them again with
`TmLaunchCache.revalidateUser()` only after that frame: live tenants by their name in the active
language, each sandbox indented under its live tenant, the badges and the reason a tenant cannot be
opened, a search from seven memberships kept in `?q=` (Enter follows the only enabled match, Mod+K
focuses it), the skeleton, the error state of a failed read with nothing stored, the empty state,
and the notice of a tenant the user can no longer open after an entry they chose.

The tenant shell (`:t`) puts a 40 px ink app bar in the frame and the routed page in `main`. The
bar renders from the launch documents, so a warm launch's first frame is complete with no request:
the wordmark (the mark below md) linking to the tenant's home; Menu and search, grouped from md,
with the search field from lg and its `Ctrl K` keycap from xl, which call the launcher; Back and
Forward in installed mode, from the Navigation API or, without it, the router's page ids, a chunk
of their own that loads only once the app runs installed into the box the bar reserves; each
distribution slot's button with its badge's count overlaid, which opens the slot's panel in a
popover from lg and in an end drawer below; fixed 30 px boxes for the bell and the tray and
zero-width ones for help and the assistant; the Sandbox, Read-only and Maintenance markers, each
with its popover; and the session button, with the user's initials on the tone of their record key
and their name and the tenant's as the width allows. While a cold launch's sync runs, the names come
from the profile cookie and the tenant's name is a skeleton; a failed sync shows the error views'
error state with Retry, and `TmShellMain` can put the tenant-state page in place of the route host.
The session menu (the tenant card, My settings, the language and calendar switches with two choices
or more, the theme, Keyboard shortcuts, About, Report a problem, the slots below md and Sign out), the popovers and
the drawers are a chunk the bar loads at idle or on the first hover, focus or press of their
buttons, and that chunk's avatar, which can show the user's photo, replaces the first frame's
initials. The About dialog (an About tab with the wordmark, the distribution's description, the
versions, Reload while the server is newer, the copyright and the legal links, and a Diagnostics tab
that loads the Diagnostics panel from `@tellma/core/support` on its first selection, or shows the
offline line with Copy diagnostics' shortcut when that cannot load) and the Shortcuts dialog (`Mod+/`: the
general, page, grid and launcher keys with a filter) are a chunk each, which open over a placeholder
in their final geometry. A tenant switch creates a new shell and clears the launcher's text. While
the launcher is open, a page that renders after a navigation leaves focus in it: the page's focus
move waits, and runs as the launcher closes, before the launcher gives focus back.

`tmSearchNormalize` (`@tellma/core/search-normalize`) folds a query and the terms it matches the
same way: bidi controls stripped, NFKD with Latin and Arabic marks removed, lowercased, the
interchangeable Arabic letters folded and tatweel dropped, Arabic-Indic digits as 0-9, and
punctuation and symbols as single spaces. Nothing on the first frame matches text, so it is an
entry point of its own rather than part of the primary. The size gate's static-graph check
(`"tellma".staticGraphExcludes`) proves that neither the primary nor the eager `/shell` and
`/attention` reach it through static imports, so the shell's deferred launcher panel and Shortcuts dialog load it with
`import()`.

## Banners and the input-idle clock

`TmBanners` holds the conditions the shell's 32 px banner row shows, one at a time: the head by
level (1 connection lost, 2 reserved for critical announcements, 3 update, 4 warning, 5 info), then
by the order `show` first ran, or the banner the user paged to, until it closes or is dismissed. A
banner of a higher level takes the row at once; one of a lower or equal level adds to the pager's
"1 of 3". `show` with a live id updates it, and the handle's `update` and `close` change or remove
it. Levels 1-3 are the platform's: a distribution's banner there throws in development, as does a
dismissible banner at level 1 or 2. A dismissal holds for the rest of the tab's life, and the update
banner's (shown through the internal `ɵshow` with the version as its scope) for that version only.

```ts
const ref = inject(TmBanners).show({ id: 'acme.period-closing', level: 4, tone: 'warning', dismissible: true,
  message: computed(() => t('Acme_Banner_PeriodClosing')) });
// When the condition ends:
ref.close();
```

The row never moves the page under the user's hands. `current` (what the row renders) and `visible`
(the row is open; the shell sets `--tm-banner-height` from it) change the row's height only at an
idle moment: `TmInputIdle`, the tab's one clock, turns `idle` 2 s after the last `keydown`,
`pointerdown`, `pointermove`, `wheel` or `touchstart` on the window, heard by passive capture
listeners, and `whenIdle()` waits for it; spec 0024's time-zone prompt waits on the same clock. A
message swap in an open row, the user's own dismiss and any change while the page is hidden apply at
once, and a row whose last banner closes while it waits stays open and empty until the gate. The
internal `ɵhead` follows every change at once, so the banner host announces a banner when it is
queued, before the row opens. Each banner's time from queued to shown is the timing
`tm.attention.banner { id, level }`.

`provideTellma` starts the platform's offline banner, `tm.offline`, in every shell: it shows once
`TmApi.online()` has been false for 5 s, and closes when it turns true. The update banner,
`tm.update`, is the update service's.

`@tellma/core/attention` holds `tm-banner-host`, which the shells place in the banner row. It renders
`tm-banner-bar` from `current`, wires its action, pager and dismiss buttons back, never takes focus,
and owns the one visually hidden live region: assertive for level 1, polite otherwise, written once
per banner that becomes the row's. A dismiss that empties the row while focus was in it moves focus
to the page's `main`, so it never falls to the document's body.

## Attention: the badges, the verbs and the arrivals

`TmAttention` reads the routed tenant's stored `inbox` and `assignments` and renders the bell's and
the tray's badges from them: the bell's count of unseen notifications (`99+` from 100), its
`attention` tone while a warning or an error is unseen and its count of jobs that are pending or
running, the tray's count of open assignments and its accent while some are new, each with its
accessible name. `toggle` opens a panel or closes it; opening shows the stored document at once,
quiets the badge, marks the document seen when it shows something new, then reads it once.

```ts
const attention = inject(TmAttention);
const unseen = computed(() => attention.bell().count);
attention.toggle('bell', 'click');
await attention.read([4102]);
```

The verbs (`read`, `readAll`, `clear`, `clearRead`, `stopJob`) apply in the same frame through the
tab's overlay, then call the server; an inbox verb's overlay drops when a copy arrives whose inbox
version is at or above the one the verb returned, so a copy read before the verb committed never
undoes it, whatever order the copies were saved in. A failed verb drops its overlay at once and
toasts sticky with "Try again". The verbs do nothing on a frozen tenant or offline (`writable`). The
server calls, and how the overlays change a copy, load as a chunk of their own with the first call,
which opening a panel makes; so the first frame carries only the overlays' bookkeeping, and a verb
made before that chunk ever loaded applies as the chunk lands.

`TmArrivals`, which the tenant shell creates, toasts what arrives in the visible, focused tab: a
notification of a toasting type (by severity, with Download or View), one assignment (with its
caption and the assigner's comment, and Open) or several grouped (View opens the tray), and a tenant
that turned read-only or frozen. A tab's baseline is the first copy written after it started, and
each later copy's arrivals are the rows newer than the previous copy's newest, so nothing that
arrived before pops up and a tab that gains focus replays nothing. The user's
`notifications.toasts` preference (`tmNotificationToastsKey`) turns the notification and assignment
toasts off. Inside a tenant the toasts' overflow pill reads "{n} more · Open notifications" and
opens the notifications. What arrived and how it toasts, with its strings (`tm.arrivals`), load as
a chunk with the first copy handed on.

`@tellma/core/attention-rows`' `TmAttentionText` renders server text in the reader's language, following every language switch
with no request: a notification's title and detail from its type's keys, a job's label (its own key,
then its handler's, then the handler's fallback, an `entity` argument naming an entity rendering as
its plural label), a job's message, and an assignment's caption (the title with its code split out
for an isolate, the detail, and the amount at its own scale with its currency's code).
`TmOutcomeToasts` raises the outcomes the platform's own pages toast: "Started in the background."
with Track, and the sticky "Exporting…" with Cancel. Each method returns the toast's handle at once,
while the toasts load as a chunk of their own with the first outcome: an update made meanwhile
applies as the toast shows, a toast closed meanwhile never shows, and a chunk that cannot load is
reported to telemetry and closes the handle.

## Attention: the bell, the tray and their panels

The app bar places `tm-bell-button` and `tm-tray-button`, 30 px buttons with the badge's overlay
counter, its accessible name and `aria-keyshortcuts`, and, on the bell, a ring while a job runs. A
press, Alt+Shift+U or Alt+Shift+Q toggles the model's panel; pointer hover, focus and the first idle
moment load the panels' chunk ahead of the press, and the panels' chunk loads the server calls'
chunk in turn, so the first panel's verbs apply in their click's frame. The panel opens in a
popover from lg and in the end drawer below it, which gives focus back to its button however it
opened, renders from the stored document with the tab's overlays in the frame it opens, focuses its first row, and publishes an overlay page context, `panel:bell` or `panel:tray`,
with its `attention.*` actions. Each list is one tab stop with roving rows (the arrows, Home and End;
Delete clears a notification).

The bell's panel holds Mark all as read and More options (the settings and a confirmed Clear read),
the newest three running jobs with a row by status (a progress bar, Held, Awaiting confirmation
with Review, Stopping…) and Stop, which asks first in the job's kind's words with "Keep running"
to back out, and shows Stopping… without waiting for the server, then
the notification rows: each a link to its open route with its severity's icon, Download for a
finished export, its time and Clear. Rows that arrive while the list is scrolled down wait under a
pinned "{n} new" button; a status line with Refresh shows while live updates are paused. A frozen
tenant shows no verb; offline, each verb stays, disabled with "You're offline". Mark all as read and
Stop run through the page actions' dispatcher, as the page context's actions do; a stop naming no
job the bell shows running is refused before its question. A verb that removes
its own control keeps focus in the panel: Mark all as read moves it to More options, a stopped job's
row to its title, a row that leaves to the row in its place, and a list that empties to the heading,
so the popover stays open. The tray's panel lists the open assignments through `tm-assignment-list`,
marking those assigned since the tray was last opened "New", every one for a user who never opened
it. The panels' chunk also registers the support dump's `attention` section: the open
panel, the badges and the shown banners.

The toast policy's durations are `TM_TOAST_FEEDBACK_MS` (4 s, an outcome the acting tab raises) and
`TM_TOAST_ARRIVAL_MS` (6 s, an arriving notification or assignment).

## Page context and page actions

Every routed page and context-bearing dialog publishes one `TmPageContext`: its kind, route
template, title, data status, kind-specific state, visible rows, form, errors and actions, each a
signal read only when a reader asks. `TmPageContexts` holds them: `current` is the newest routed
context, `overlays` the dialogs' (`overlay: true`) in registration order, and `top` the topmost
dialog's, else `current`. `provideTmPageScope()` gives a page or a dialog its own scope, with its
own `TmPageContexts` and `TmPageActions` bound to it: inside it, a second routed context replaces
the first and keeps the first one's actions after its own, and everything registered in it ends
with it or with its `DestroyRef`.

```ts
inject(TmPageActions).register({
  id: 'acme.credit-review.suggest-limits', labelKey: 'Acme_CreditReview_SuggestLimits', icon: 'wand',
  group: 'primary', shortcut: 'Mod+Shift+A',
  invoke: async () => { await this.suggestLimits(); return { outcome: 'done' }; },
});
```

`TmPageActions.register` adds an action to its scope's context, even before that context registers;
outside any scope, to `current`. A context's published `actions()` are its own, those of the
contexts it replaced, then its scope's, ordered by group (toolbar first), `order` (default 100) and
registration. While a context is `top`, its actions' shortcuts are live in `TmShortcuts` (scope
`page`); a dialog's run while that dialog is the top modal. Every origin invokes through
`TmPageActions.invoke(id, args, origin)`: it finds the action in `top`, the lower dialogs, then
`current`; refuses a disabled one with its `disabledReason` as the message, announced politely to a
user or a shortcut; refuses one already running or asking; asks `TmConfirm` (loaded on the first
question) with the danger choice for a destructive one, whoever invokes it, and checks it again
once answered; then runs it while `running` holds its id. A throw becomes `failed`, and one that is
not a `TmApiError` is reported to telemetry. Each found action's invocation emits the sampled
`tm.action.invoked`. Development throws for a malformed id, a destructive action without
`confirm`, and a shortcut on an action whose `params` require members.

`tmSerializePageContext(context, { includeData })`, in `@tellma/core/page-snapshot`, reads a
context once into plain JSON for the support dump and a future assistant. Nothing on the first
frame reads it, so its callers load it with `import()`. Without page data it leaves out the rows, the form's values
and the errors' messages, masks every string of the search query's `q`, `filter`, `columnFilters`
and `custom` and of `parameters` as `"…"`, and titles a details page by its entity's singular
label, which alone needs an injection context or an `injector`. `TM_HELP_ARTICLES` is the help drawer's
seam, declared and never provided here. `@tellma/core-testing`'s `TmPageContextHarness` reads the
registry and invokes actions in tests.

## Navigation and pins

`TmNavigation` (in the primary) derives the launcher's entries for the routed tenant from the merged
declarations, after `hide`: every stack with a `query` operation and a segment (its plural label,
`file-text`, and a create page when the user may save one that has a details page), every page
without a path parameter (`square-dashed`, behind its securable), every coded report
(`chart-column`, behind its securable) and every feature entry (`link`, behind any one of its
`requires`). An entry with `visibleWhen` shows only while the distribution document is present and
the predicate returns true; a predicate that throws hides its entry and is reported once as
`tm.nav.visible-when`. `sections()` lists the sections with a visible entry, each ordered by `order`
(100 by default), then by the label in the active language; `items()` adds the search-only entries,
which name no shown section. The core feature contributes Analysis (800) and Administration (900),
Settings under Administration (shown with `Read` on any settings category the Core chunk lists, so
to no one when it lists none, unless a distribution gives it `requires`) and the search-only My
settings entries, whose labels are platform strings and whose synonyms are the launcher's own
`tm.launcher` strings, which load with the launcher.

```ts
const navigation = inject(TmNavigation);
navigation.byHref('/17/centers')?.label; // 'Centers'
navigation.resolve('/17/customers?f.CustomerType=in:Corporate'); // 'ok', 'no-access' or 'unavailable'
```

`resolve` judges a stored URL by the route table: `unavailable` when no route matches or
`visibleWhen` hides the declaration that owns its route, `no-access` when the route's permission
check fails, `ok` otherwise; a stored report's or dashboard's page reports a refusal itself.

The pins model's types and keys live here too: `tmPinsKey` (`nav.pinned`, curated, per tenant)
holds `{ v: 1, items }` of pins and one-level folders, and a value of another shape reads as no
pins; `tmPinnedBarVisibleKey` (`nav.pinnedBarVisible`) is the bar's switch, which My settings
writes. The rules the pinned bar applies to the items (8-character ids, URLs of at most 512
characters, titles of at most 80, labels and folder names of 1 to 40, 30 pins and 10 folders per
tenant, one level; what a pin captures of the routed URL; the current pin and a chip's label) live
with the bar in `/shell`, as do the terms the launcher's search matches an entry by.
`TM_LAUNCHER_PROVIDERS` is the multi token through which a feature adds a bucket of results below
the launcher's pages.

The registry adds no icon glyphs to the first frame: the default and core entry icons are names,
which the launcher's own chunks register with their glyphs.

## Entities, display names and permissions

`TmEntities` answers route matching, URLs and the shells from the Core chunk's entity summaries and
the merged declarations: `summary`, `bySegment`, `declaration` and `label`. `load()` fetches the
Entities chunk once, with the entity model, a lazy chunk of its own (`tm-entity-model`) that
weighs nothing on the first frame. After it, `get` returns an entity's descriptor gated by the
routed tenant's languages: a twin beyond them is no property, and with two or three languages each
twin's label carries its language's symbol ("Name (ع)") and its `contentDir` its direction. An
action's argument fields are gated and labelled the same way, so the generated dialog never shows a
twin the tenant has no language for.
`enum` labels a named enum's values, and `resolve` reads a column key or path: a scalar or twin
(`value`), a group anchor (`text`), a navigation, or a blob navigation (`image`, `file`), with a
slash label and the foreign key's path. Before the load a development app throws; a production
app gets a descriptor of the summary alone.

```ts
await entities.load();
const name2 = entities.get('acme.Customer').property('Name2'); // label "Name (ع)", contentDir "rtl"
const editor = entities.resolve('core.RoleMembership', 'Role.ModifiedBy'); // a navigation end, fkPath "Role.ModifiedById"
```

`TmDisplayNames` renders any row's display name by the rule the server declares, in the active
content slot: integers ungrouped, dates in the calendar's numeric style, the fixed " - " between a
prefix and a name, "#7" when the row has neither, and no bidi control in the text.
`tm-display-name` renders the parts, each in a `<bdi>`. By id, `label` answers from a cacheable
entity's list, else from the rows the tab has read (`remember`, `rememberRelated`: at most 5,000
per entity, least recently used first), and `resolve` fetches the unknown ids of a frame in one
background query per 100, which a tenant change aborts. The held rows are dropped when the user,
the tenant or the tenant's content languages change, and kept through any other move.

`TmPermissions` answers from the access summary: `can` and `filtered` per securable, `allows` per
entity operation (the details page and the tenant's `ReadOnly` or `Frozen` state included), the
`disabledReason` a refused action shows, `label` in the Access tab's words, `sensitive`, and
`reachable`, the traversal rule a query's paths obey.

### Select plans, formats and typed values

`@tellma/core/entity` holds what the page families build on the registry. `tmSelectPlan(root,
keys)` reads each column key against the root entity and returns one column per key the user may
read: its kind (a value, a multilingual group, a navigation, an image or a file), its label and
type, its filter kind and the query's ordering for it (`orderBy('asc')`). The plan's `select`
holds every term the columns read, the root key first, each once: a group reads each of its slots, a navigation its
key and its target's display-name members, an image its `Variants` and a file its `FileName`.
Every term is checked against `TmPermissions.reachable`, and a key that names nothing, or that
reaches a term the user may not read, is dropped and reported with the reason. `rows(response)`
reads a query's rows into `TmPlanRow`s, keyed by column key and by term; an expression column
takes its type from the response.

```ts
const plan = tmSelectPlan(entities.get('gl.Center'), ['Code', 'Name', 'Parent']);
// plan.select === 'Id,Code,Name,Name2,Parent.Id,Parent.Code' in a two-language tenant
const rows = plan.rows(result); // result: the query's row set
```

`TmFormats` writes a value as text by its type facts: decimals and longs exactly from their
decimal text, in the locale's digits and with no bidi control; dates in the calendar; an instant
in the display zone, labelled with its offset when the device's zone differs; enums and related
rows by their labels; an id, a foreign key's or an entity's own key, is written ungrouped. A
grid's cell leaves the zone label to the column header (`zoneSuffix`). `tm-value` renders the
same text with the markup some types take: a check for a set flag in a grid, a badge per enum
value, a
related row's display name as a link to its page when the user may open it, with its avatar, a
thumbnail asked for only when the blob lists one, and a file's name as its download link.

`@tellma/core/entity-editing` holds the edit side, which `@tellma/core/entity`, the search page
and the details page's read mode never reach statically. `tm-value-editor` takes the control a value's type takes inside a `tm-form-field`, bound to a
Signal Forms field: text, a textarea beyond 255 characters, numbers (decimals and longs held as
exact text; text that reads as no number, or as more digits than a number holds, keeps the
field's value and holds the field invalid, so a form's save waits), a checkbox, a date picker, a select per enum, and the image and file pickers, which
check the file against its kind, crop a cover-square image to its square, and upload as soon as
it is picked. A related row, by its navigation or a value that names a target, takes the entity
field, and a list of them the entity list field. A value the server owns (a navigation carries its
key's ownership), an instant and a type with no control show read-only; a write-once value edits
only while the record is created. A form that provides `TmUploads` sees every upload in flight, to
hold its save until they settle.

`tmGridColumnFor(column)` makes the read-only grid column of a plan's column: a search grid's
reads the plan's rows by column key and sorts when the column orders; a form's grid that does not
edit (`rows: 'model'`) reads the model's rows by their JSON names. `@tellma/core/entity-editing`'s
`tmEditableGridColumnFor(column)` makes the column of a form's child grid, which reads the model's
rows by their JSON names and edits only the root values the user writes. A
multilingual group there edits the active language's slot, so a host that makes the config in a
computed follows a language switch. A root navigation there edits its key through the target's
picker binding with the column's filter: the cell shows the display name, its prefix and name
each in its own isolate, and a paste resolves its names in one query, or in as few as the server's
filter limits allow (its length, 512 items in a list, and the parameters its literals take), four
at a time at most; the paste's code loads on the first paste. A name no filter can carry (longer
than the filter, or a number longer than the server reads) is not found without a query; a
failed query leaves only its own names to retry, by pasting again; and a full page of rows
decides only the names it shows several rows for, the others being read again in smaller
queries, so a name whose own page is full is ambiguous rather than guessed.

### Entity pickers

`@tellma/core/entity-editing`'s `TmEntityPickers.binding(entity, options)` turns `@tellma/core-ui`'s entity picker into a picker
of an entity: every member binds to the picker's input of the same name, or to a grid entity
column's.

- **Search.** It reads `{segment}/query` 20 rows at a time (with one more to know whether there
  are more): the key, the display name's and the description's columns, the text without bidi
  controls, trimmed and capped at the entity's longest search, the declared picker filter and the
  field's as separate leaves, ordered by the declared order or the display name. A cacheable
  entity with no filter searches its cached list instead, at once and offline: its active rows
  whose display name, natural keys or searchable columns hold the folded text (a column the
  server searches from its start, from its start; any other anywhere), or that the findable rule
  finds (`1101 - Cash`, an integer source or prefix in any digits, `#7`, or the id),
  by display name. While the list is absent, the server answers.
- **Rows.** `itemLabel` is the display name, `itemLabelParts` its prefix and name apart, and
  `itemDescription` the second line: the declared description paths, else the natural keys the
  name does not show and a tree's parent. A target with nothing to describe shows one line.
  `displayWith` names a chosen id and reads an unknown one.
- **Pages.** Advanced search is offered when the user may query the entity; Create and Edit when
  they may save it and it has a details page, Create unless turned off (by the options or the
  declaration's `picker.create`), Edit only to a single picker. Each opens `TmLazyPickerPage`,
  which loads the search page's or the details page's picker mode inside the modal, with
  `{ entity, filter, kind }` as its context: advanced search the search family's, Create and Edit
  the details family's. Each family adds its own half to the internal `ɵTM_PICKER_PAGES`
  multi-provider (`@tellma/core/entity`'s, so a family provides it without reaching the editors), so neither replaces the other, with an `import()` of its own entry point, which
  this one never reaches statically; a later provider of the same half wins. Both halves are
  provided at the root environment injector, in the app's providers: `TmModal` opens its dialogs
  without an injector of their own, so a provider on a route or a component is never seen. Until
  a family ships, its page says it is not available.

```ts
readonly parent = inject(TmEntityPickers).binding('gl.Center', { filter: signal("CenterType = 'Abstract'") });
```

Forms rarely bind it by hand: `tm-entity-field` and `tm-entity-multi-field` are Signal Forms value
controls over the picker and the multi-picker, which `tm-form-field` labels and describes as it
does the bare picker. Bound to a form field, the entity field's text that names no row holds the
field invalid; a required entity list field holds an empty list invalid, which Signal Forms'
`required()` does not. Where the user may not query the entity, either field is read-only with
its rows' names. `@tellma/core-testing` has `TmEntityFieldHarness` and
`TmEntityMultiFieldHarness`.

```html
<tm-form-field label="Parent">
  <tm-entity-field entity="gl.Center" [formField]="form.parentId" filter="CenterType = 'Abstract'" />
</tm-form-field>
```

### Entity actions

`TmEntityActions` runs an entity's actions from their metadata, so the details page and the
search page's bulk actions build the same requests. `run(entity, action, ids, options)` posts to
`{segment}/{action}` through `TmApi.write`: an action that returns entities takes `{ ids,
returnEntities, include, arguments }` (`include`, the entity's extras by default, only while
`returnEntities`, which is true by default), a single-target one `{ id, arguments }`, any other
`{ ids, arguments }`. It resolves to the outcome: the entities, the action's own result, a
background job (an own result that is exactly `{ jobId, resourceId }`, the server's 202
`JobAccepted`), or `declined`. The run's code, like the dialogs', loads on first use: a page's
first frame only lists the actions. A chunk that cannot load rejects as an `offline` `TmApiError`
(code `network`), as a call that never reached the server. The busy overlay and a destructive action's confirmation are the
caller's, which knows whether its trigger is still on screen; `confirmation(entity, action, n)`
gives the confirmation to ask, the declaration's or "Run “{action}” on {n} items?" with "This
can't be undone.".

```ts
const outcome = await inject(TmEntityActions).dialog('acme.Customer', 'set-credit-limit', [77]);
// posts customers/set-credit-limit { ids: [77], returnEntities: true, include: ['permissions'], arguments: { creditLimit: '250000' } }
```

- **Dialogs.** `dialog(...)` opens the dialog an action with arguments takes, loaded on first use:
  the declaration's `dialog`, else one generated from the metadata's `fields`, each in a
  `tm-form-field` with its `tm-value-editor` (a picker for a field with a `target`), required per
  the field, and Cancel beside the action's button (`danger` when destructive). An action with no
  arguments and no declared dialog runs at once. A declared dialog injects its context with
  `injectTmActionDialog()`: the rows, the invoker's `initial` arguments, `running`, `close`, and
  `run(args, { form, keepOpen, ids, rethrow })`, which runs under the busy overlay, shows the
  action's button pending, and closes with the outcome unless `keepOpen`; `dialog()` resolves to
  the last successful run's outcome however the dialog then closes, and `null` only when nothing
  ran. The server's errors at `arguments.*` land on `form`'s fields by the error-path walk and
  clear as each field is edited; the generated form records each with the value it was sent for,
  so it shows while the field holds that value and every one clears as the next run starts. A
  required yes/no argument takes "no" as an answer. Every other error lists in an alert atop the
  dialog with the call's reference, a row's (`ids[i]`) under its display name, except the kinds
  `rethrow` names, which reject for the dialog to read; a write whose answer never came reads as
  unconfirmed, apart from one never sent. A declined warning keeps the dialog open.
- **Which actions appear.** `offered(entity, { record, bulk })` lists the entity's own actions,
  not built in, that take no arguments, declare a dialog, or list fields that each take a control,
  that the declaration's `visible` admits for the record, those the declaration lists first, in its
  order, then the rest in the metadata's; a selection (`bulk`) leaves out
  single-target actions and those declared `bulk: false`, and reads no `visible`. Whether the user
  may run each stays the caller's to show, disabled with `TmPermissions.disabledReason`. In
  development the actions no page can offer are named once per entity, unless the declaration
  lists them (`{ name: 'preferences/set', visible: () => false }` leaves one out on purpose).
- **Warnings.** `TmConfirmations.run(send, { entity, ids, confirmations })` sends a write and,
  on a 409 `confirmation-required`, loads its alert dialog: "Confirm to continue", each warning's
  message through `TmServerText` with up to five of its rows by display name, Cancel (focused),
  and Continue, which resends with the confirmed keys as `Tellma-Confirm`. When the warnings name
  fewer rows than `ids` holds, "Continue without these {n}" resends the others through the
  attempt's `skip`. Cancel resolves `null`; a fifth warning in one run, or more than 16 keys,
  fails as a `server` error. Every action runs through it.
- **Error paths** (`@tellma/core/error-paths`). `tmSplitErrorPath('entities[0].lines[1].amount')` splits a server path into
  `['entities', 0, 'lines', 1, 'amount']`; `tmWalkErrorPath(form, segments, { entity, entities })`
  follows the segments through a Signal Forms tree by the wire's names, each child collection
  giving the next entity, and returns the field, the PascalCase path and the entity, or the
  deepest field reached when a segment does not resolve.

### The details page

`@tellma/core/details` holds the page every entity record opens in. The route host loads it with
the first details route and renders `TmDetailsHost` with the entity, the route's id (or none on
`new`) and `?clone=`; the host loads the entity's declared `details` layout and renders it in the
page's band, under a header the host owns: the crumb back to the entity's search page, the
record's display name (the entity's singular while it loads, "New {entity}" in create mode), the
Active or Inactive badge of an activatable entity, Edit, New, Refresh and the actions menu, and
Save and Cancel in edit mode. Edit and New are disabled with `TmPermissions.disabledReason` when the
user lacks Save, and Edit is disabled again by the record's own refusals in the `permissions` extra.

```ts
tmEntity('acme.Customer', { details: () => import('./customers/customer-details') });

// customer-details.ts: the body alone; the host owns the record, its modes and its header.
export default class CustomerDetails {
  protected readonly details = injectTmDetails<Customer>();
  // details.entity(), details.model(), details.form, details.mode(), details.can.edit() …
}
```

- **The read.** One read per record key and refresh, `{segment}/get` with `include` naming every
  extra the entity declares. While a read is out and a record is already held (a refresh, a step
  to another record, a return to a stale one), the held record stays on screen under a progress
  bar, the band is `aria-busy` and Edit is disabled; `refresh({ quiet: true })` reads with neither.
  With nothing held the layout renders over an empty record, busy. A read that fails replaces the
  body with `tm-error-state`, whose way out to the entity's search page is named by its plural.
- **The record.** One Signal Forms form over the working copy, per record. Edit sends nothing: it
  keeps the server copy as the base, makes it the working copy and focuses the first registered
  field. A write through the form or `update(recipe)` copies the objects on its path, so the
  record is dirty exactly when the working copy is another object than the base. Cancel asks
  "Discard changes?" when dirty, then puts the base back and sends nothing.
- **A new record.** `new` starts in create mode from the entity's defaults (every child
  collection empty, every non-nullable flag the client writes `false`, then the declaration's
  `defaults()`), sending nothing; Cancel leaves the page, Back when the app opened another page
  before it, else to the search page. `new?clone={id}` reads that record and starts from its
  copy, stripped of its key, the members the server writes and its blobs, its child rows numbered
  as new rows, then passed through the declaration's `clone(copy, source)`.
- **The form's own rules.** The declaration's `validate` is a loader, such as
  `() => import('./customer-rules')`, of a Signal Forms schema function. The page loads it beside
  the layout and renders the layout once both are in hand; the form applies it after the
  platform's rules, so it adds rules and never weakens one, and its imports of
  `@angular/forms/signals` stay out of the first load. A failed load is the layout's failure.
- **Other sources.** A settings page renders `<tm-details-page [source]="source" [layout]="layout" />`
  over a `TmDetailsSource` of its own, with the same lifecycle and header, unrouted.
- **Testing.** `TmDetailsPageHarness` in `@tellma/core-testing` reads and drives the header, the
  mode, the progress bar and the error state; `tmFakeDetails()` gives a layout test a real record
  state over a fake host, with `providers` to give the layout and `rules` for the form's own rules.

### Strings of a lazy entry point

Strings that only a lazily loaded entry point reads ship with it, never in the primary's English
table, so they weigh nothing on the first frame. The entry point registers its English table
under a scope of `tm` once, before its first message resolves:

```ts
inject(TmLocalization).ɵregisterStrings('value', { none: '—', fileTooLarge: 'This file is too large. The limit is {max}.' });
// l10n.ɵtext('tm.value.fileTooLarge', { max: '100 MB' }) formats as the platform's own strings do.
```

The strings merge into the English translation, so they format through the same ICU interpreter,
and a language's table that holds the same keys (a locale pack's `tm.value`) wins, English being
the fallback. A scope registers once and belongs to one entry point. `@tellma/core/entity`
registers `tm.value` when `TmFormats` is first created, and `@tellma/core/entity-editing`
registers `tm.picker` when `TmEntityPickers` is;
`TmEntityActions` merges its default confirmation's two keys into `tm.entity` (a scope no other
entry point registers), and the confirmation and action dialogs register `tm.confirmations` and
`tm.actionDialog` from their own chunks; the filters' and the expression editor's strings follow
the same seam. `@tellma/core/details` registers `tm.details` when its first page is created, and
`@tellma/core/dashboards` registers `tm.dashboard` and its keys of `tm.home` (the greeting and the
grid's label, beside the title the route reads from the primary) when its first component is. `@tellma/core/attention`
registers `tm.attention` when its first service is created, its arrival toasts' chunk registers
`tm.arrivals` as it loads, and its panels' chunk registers `tm.attentionPanels`;
`@tellma/core/attention-rows` registers `tm.attentionRows` with its first service or list. The seam itself costs the primary one method
and a set of the scopes registered.

## Dashboards and the home page

A widget type is a `tmWidget(key, …)` declaration in a feature's `widgets`: its label, icon, body
(a lazy component), default and minimum sizes, and the permission its tiles require. The core
feature declares `core.recents`, the recently visited pages. The app's configuration check holds
the declarations to the server's emitted widget types, so a type the server does not register
fails the check. A widget (`TmDashboardWidgetModel`) places one type on a dashboard: its key, its
title, its box on twelve columns of 30 px rows, and its settings.

`tm-dashboard-grid` lays its widgets out once per set, in reading order, with `tmPackDashboard`:
it moves each tile up into the first free rows, groups the tiles that share rows into bands, and
reads each band left to right, so Tab, a screen reader and the narrow layout share one order. From
768 px of its own width the grid keeps the stored columns; below, it stacks the tiles in one
column at their stored heights; printing keeps the wide layout. A resize runs no script, since
the geometry is the global stylesheet's `styles/tm-dashboard.css`, which the home skeleton shares.
A tile is a labelled section on a flush card whose body fills it and scrolls inside it. A type no
declaration knows renders "This widget isn't available."; a type whose permission the user lacks
leaves the grid, or shows its lock with `denied="show"`, and never loads its body; a body loads
behind five skeleton rows in a busy section (`aria-busy`), and one whose chunk fails offers to try
again. A tile takes what the grid resolved for it (`TmTileModel`) and shows one view of it
(`TmTileView`); the grid reports each tile's state in `tiles`.

The home route renders `TmHomeHost`, which renders the last `home` hatch a feature sets, else
`TmHomePage`: the greeting by the hour in the display zone with the user's name, today's date in
the user's calendar, and the grid of `TM_HOME_WIDGETS` whose types the app declares. The
greeting's one timer waits for the next boundary in the zone, an hour at most, so a night that
springs forward delays it by no more than one wait. A hatch loads behind the home skeleton, holding
the arrival focus until it renders, and one whose chunk fails leaves the platform's home page in
its place. Hovering or focusing a recent page preloads it; preloading the home route preloads the
hatch.

## The search page's query model

`@tellma/core/search` keeps a page's query state as plain data: a setting (the mode, the entity's
descriptor and declaration, the width class and the user's three preferences) and a state (the
canonical `TmSearchQuery`, the shown columns and their source). Pure functions over them, in
`search/src/`, give the host what it needs:

- **Opening.** `tmSearchResolve` takes the state from a snapshot that supersedes the URL, the URL,
  the page state, then the preferences and the code defaults. Back and Forward skip the page
  state. A URL cannot tell a value left out from one at its code default, so both read as left
  out, and a canonical URL reopens the state it was printed from. A drill-through list whose `#d=`
  payload is missing or malformed prints no URL and builds no request.
- **The canonical query.** `tmSearchCanonical` drops what the entity cannot have (an inactive flag
  on an entity that is not activatable, an unknown tab or custom value, a column key that does not
  resolve or that the user may not read, an `f.` value its control rejects, a size the page does
  not offer, a page past the skip window, an order on an expression column) and leaves out every
  code default. A dropped column key is reported once as `tm.entity.column-dropped`, where it
  enters the state.
- **Changes.** `tmSearchUpdate`, `tmSearchSetColumns`, `tmSearchApply` and `tmSearchResize` return
  the new state, its URL, whether the URL is pushed (a page change alone) or replaced, and the
  preferences to write.
- **The request.** `tmSearchPlan` and `tmSearchRequest` build the select plan (the shown columns,
  with the tile's paths and the tree's keys beside them) and the rows request, its leaves and
  named filters in a fixed order with their origins; `tmSearchDiagnose` maps a refused query's
  diagnostics back to those origins.

## The device time zone

`TmDeviceZone` follows the device's time zone for the signed-in user. Its check runs after the
first render at idle, on each tenant entry and whenever the tab comes into view, in a visible tab
of a tenant whose launch documents are present. It compares the device's zone, mapped through the
`core.TimeZone` aliases of the Entities chunk (`Asia/Calcutta` becomes `Asia/Kolkata`), with the
one the tenant last handled on this device. A first run, a zone that already is the display zone,
a frozen tenant and a zone at the display zone's offset now are recorded silently. Any other change
either updates the user's zone in the background, with a toast, when the per-device option is on,
or asks once in a small dialog, once the user's hands are off the keyboard and pointer (two
seconds without input) and no other modal is open. Only one tab of the user asks or updates: each
takes the user's `zone-prompt` lock only if no tab holds it, and a tab that finds it taken checks
again once it frees, in case the holder gave up without asking.

```ts
// My settings' "Change my time zone automatically" checkbox.
const zone = inject(TmDeviceZone);
const checked = zone.auto; // this user's option on this device
await zone.setAuto(true, 'settings');
```

The option and what each tenant last handled live in one document of the store's device partition
(`zone`), which keeps at most twenty users, so they outlive a sign-out. The primary holds the
option's reading and the doors alone: the check (`tm-zone-check`), the dialog (`tm-zone-prompt`)
and the document's writes (`tm-zone-doc`) are lazy chunks. A test app checks nothing: the state
testing providers replace the check's loader with one that loads none. Telemetry names the step
and the answer, never a zone or an offset.

## Decimals

`decimal` and `long` values travel as JSON strings and stay strings: a `TmDecimal` is the value's
exact wire text, so nothing is lost to a binary double. Arithmetic and comparison go through
`TmDecimals`; the lint rule `tm/no-decimal-operators` refuses `+`, `+=`, `<`, `<=`, `>` and `>=` on a
`TmDecimal`, which would join or compare its text.

```ts
const total = TmDecimals.add(line.amount, line.tax);
if (TmDecimals.compare(total, creditLimit) > 0) { … }
```

## Telemetry and error logging

Code anywhere records events, timings and errors through `TmTelemetry`, and nothing awaits it.
Names read `<scope>.<area>.<event>` (`tm.api.slow`, `acme.credit-exposure.run`); properties are at
most 16 flat numbers, booleans and codes. In development a malformed name or property throws a
`TmTelemetryError`, and every item is written to `console.debug('[tm]', …)`; in production the
item or property is dropped and counted into the next `tm.telemetry.dropped`. The lint rule
`tm/telemetry-names` checks the literal names and page action ids where they are written.

The root service is a thin front that stamps each call with the time and the tenant, page and
recent requests it saw, and holds it. The pipeline behind it is a
lazy chunk of the primary (`tm-telemetry-pipeline`), which the app's startup loads at idle after
the first render and which takes the held calls in order. It adds the envelope (the UI version,
the tab, each item's tenant and the routed page's `route` and `pageKind`), samples per tab by the
distribution's `sampleRate`, keeps the last 100 items in `recent`, and queues the rest within a
cap of 200, dropping the oldest item that is not an error first. The queue flushes at 50 items,
30 s after its first item, 1 s after an error, when the tab hides or on `pagehide` (one batch for
both), on a
tenant switch, when a hold lifts and on `flush()`. Each flush sends one tenant's items per batch,
at most 100 items and 16 KB, one batch a second, through `TmApi.send`. It holds while offline,
without an active session or before the UI version is known, and sends nothing when the
distribution turns telemetry off.

`provideTellma` installs `TmErrorHandler` as Angular's `ErrorHandler`. Every error it receives, and
every `TmTelemetry.error`, is logged to the console and becomes one `tm.error.captured` item whose
`source` is `render`, `global` (the browser's uncaught errors and rejections) or `api` (the error
presenter). Global noise is filtered (a cancelled or unauthenticated request, the `ResizeObserver`
loop notice) and an error from another origin's script counts as dropped. A failed dynamic import
is a `ChunkLoadError`. Messages pass five masks (URLs, e-mail addresses, quoted text that is not an
identifier, digits, non-ASCII text) and a cap of 200 characters; a request error ships no message.
Stacks keep 30 frames and 4 KB, each URL reduced to its file name. Each fingerprint
(`TypeError@chunk-K3J7Q2M9.js:1:2345`, or `TmApiError@server.internal` for a request error, by
its kind and code) is sent once per 10 minutes, and 30 error items at most per
10 minutes; the last 50 records are kept for the support dump.

An injector whose app never starts the pipeline, a unit test's, keeps at most 1,000 calls and sends
nothing. Tests replace the service with `@tellma/core-testing`'s `TmFakeTelemetry`.

The platform measures itself through the same service:

- **Page visits.** Each routed page is one sampled `tm.page.visit` timing, from its navigation (or
  the document's time origin for the first route) to the next navigation or `pagehide`. Its `ms`
  runs to the page context's first status away from `loading`, its `chromeMs` to the first paint
  after the context registers; `entry` is `load` or `navigation`, and `outcome` is `ready`,
  `error`, or `left` for a page left before its data arrived. A navigation that renders a route
  but registers no context, such as the launch page's redirect, passes its start to the next
  visit; one abandoned in a guard or a resolver is reported as `left`.
- **The load's paints.** A lazy chunk (`tm-load-metrics`), loaded at idle, observes the paint
  timeline with `buffered: true` and emits one sampled `tm.vitals.load` (`ttfbMs`, `fcpMs`,
  `lcpMs`, `navigationType`, and `controlled` when a service worker handled the navigation's
  request) at the first input or when the page is hidden. A metric the browser lacks is left out,
  never sent as zero.
- **Request outcomes.** The pipeline reads the request log on every change. Each flush sends one
  unsampled `tm.api.requests` per tenant and route for the 20 busiest routes of its window (counts
  by outcome, the sum and maximum of the durations, the calls over 1 s and 3 s, the background
  ones); a request that reached 3 s becomes one `tm.api.slow` with its trace id as it settles, or
  as the page is hidden while it is pending, at most 20 a tab per 10 minutes.

## The request log and Copy diagnostics

`TmRequestLog` keeps the tab's last 100 requests, newest first, in `recent`: the HTTP layer begins
each entry as it sends and ends it as the request settles, and a pending request turns `slow` at
3 s. Each answer's `Tellma-Build` goes to `TmAppVersion`, which holds the running build's identity
(the UI version from the client metadata, its `display` form and `label` with the abbreviated
commit, the platform's and the features' versions) and orders another build against it by SemVer
precedence, ignoring the build metadata; `serverNewer` holds while the last server build seen is
newer.

`diagnosticsText()` is the plain-text block support reads: the versions, the tenant, tab, locale,
calendar and connection, the routed page and its dialogs, and the last requests with their trace
ids. It is English with Latin digits in every UI language, with local times and the offset in its
header, and holds no bidi control. `copy()` writes it to the clipboard synchronously, so call it in
the user's gesture, and toasts the outcome once; `Mod+Alt+Shift+D` copies it from anywhere, in
fields and over modals, from the app's first page on.

## Report a problem and the support dump

`TmSupport` is the support tools' facade, in the primary so that an error dialog, the session menu
and About can call it before the tools load. `open()` takes the snapshot of the page and its
dialogs and opens the support dialog in the caller's frame, with a placeholder in the dialog's
final size until `@tellma/core/support` lands; when that entry point cannot load, the dialog says
so and names Copy diagnostics' shortcut, which still works. `downloadDump()` downloads the
diagnostic file, never with page data, and settles once the download has started. Presses made
before the tools land collapse to one of each kind, the last. `canSend` holds while a request could reach support:
support enabled in the distribution, a live member of an active tenant online in a live session.
The app's startup preloads the small host chunk that opens the dialog when the browser first goes
idle, so even the first press opens the dialog in its own frame.

The dump is one JSON document, at most 2 MB: the builds and the browser, the tenant, the page's
address and its contexts (with page data only when the user includes it), the storage estimate,
the last requests, telemetry events and errors, and the registered sections. Built on a tenant's
routes, it keeps that tenant's requests, telemetry, errors, draft keys and page-state keys, and
those that name no tenant, so a tab that visited another tenant never hands its records over. Over the cap it
drops, in order, the visible rows, the telemetry, the page data and the sections, each only while
the dump is still over, and names each step that dropped something. It never holds the stored documents, cookies, web storage or the user's
profile. A feature adds a section with `registerDumpSection(name, read, destroyRef)`: a short
lowercase name, a read that returns plain JSON within 64 KB, which the dump calls once (a read
that throws is recorded as `{ "error": "<class>" }` under the section's name, and one over 64 KB as
`{ "truncated": true }`), and the `DestroyRef` that ends it; without one, the section lasts as long as the
app. The client state's section, `state`, is the platform's: the facade adds it when the host
chunk lands, and no feature may take its name.

## Seams

`TmRequestLog` is an abstract class provided in root, whose default is the app's ring of the last
100 requests; a spec may provide a recording log in its place. `@tellma/core-testing` puts
`TmFakeApi` behind `TmApi` and `TmFakeTelemetry` behind `TmTelemetry`.

The HTTP layer reaches three services that arrive later through internal seams in `src/api/`, each
with a default that is correct until its owner replaces it: the request locale (the document's
language and no calendar, until the localization service), the cache refresher (nothing cached, so
nothing to revalidate), and the worker's update check, which loads `@tellma/core/updates` on its
first call (`disabled` in an app without it, as a test app, and `unreachable` when it cannot load).
The sync's refusals that end a tenant's use go through one more to the error presenter, which
they load through the same loader as the route's `client-outdated` view.

The route host reads one more: the platform's text, which reads the loaded string packs and
client tables until the localization service replaces it. A page's `securable` gate asks
`TmPermissions`.

## Import rules

Static imports between entry points follow one graph, always through `@tellma/core/<entry point>`
and never a relative path: the primary imports no other entry point, `/attention` only the
primary, `/shell` the primary and `/attention`, and no eager entry point imports a lazy one. A
shared entry point (`/private`, `/search-normalize`, `/error-paths`, `/attention-rows`) loads with whichever code imports it: every
entry point but the primary and the shared ones may import it, so the primary never carries it.
Dynamic `import()` and `import type` cross any boundary, and a spec may reach another entry
point's test rigs (`*.testing.util.ts`) by a relative path, since no build ships either. The lint
rule `tm/core-import-boundary` enforces the graph, and ng-packagr refuses a cycle between entry
points.

The primary reaches `/shell`, `/recents`, `/leader`, `/updates`, `/errors`, `/page-snapshot`, `/support` and `/dashboards` only through `import()` with a specifier TypeScript
does not follow, since ng-packagr would count a followed one as a cycle. The components it loads
from the shell are named in an internal contract that the shell's component map `satisfies`, so
either side's rename fails the build. A build or test that compiles the primary from sources in
this repository compiles only what its program holds, so its tsconfig also extends
`tsconfig.lazy-entry-points.json`, which lists the lazily loaded entry points.

## Budgets

Each entry point's gzipped weight, as a production app ships it after the Angular linker, has a
ceiling in `package.json` (`"tellma".budgetsInKb`), checked by `pnpm run size:check`; every raise
carries a note beside it.

The primary entry point also has the platform's cap of 50 KB at gzip level 9 on that same weight,
which `size:check` holds it to: the whole entry point, every member counted whether or not an app
uses it, its `import()`-only chunks apart. Raising it takes `"tellma".capsInKb` with a note,
`{ ".": { "error": 52, "note": "…" } }`; the product owner has raised this package's to 55 KB. A distribution's `tellma-client build` also prints the
primary's code on its own app's first frame (`corePrimaryFirstFrame`), held to the same 55 KB: a
lower bound of this weight, since an app ships only the members it uses.
