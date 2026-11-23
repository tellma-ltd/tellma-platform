# @tellma/core

The Tellma app framework: the runtime a distribution's single-page app imports. It splits into
entry points along its lazy-loading lines, so the warm-launch bundle carries only what every page
needs, and each lazy boundary (a page family, the hub, the support tools) is an entry point of its
own.

| Entry point | Holds | Loaded |
|---|---|---|
| `@tellma/core` | The distribution API: `bootstrapTellma`, `provideTellma`, `tmFeature`, the declaration functions (`tmEntity`, `tmPage`, `tmCodedReport`, `tmWidget`, `tmOverride`) and `TM_APP_DECLARATIONS`, the merged declarations; the client metadata's types, its loaders (`tmMetadataLoaders`) and the loaded Core chunk (`TM_CLIENT_METADATA`); the HTTP layer (`TmApi`, `TmApiError`, `TmProblem`), its plain-JSON value types (`TmJson`, `TmDecimal`, `TmId`) and the exact decimal helpers `TmDecimals`; the seams through which telemetry (`TmTelemetry`) and the request log (`TmRequestLog`) are provided; the page context and page actions (`TmPageContexts`, `TmPageActions`, `provideTmPageScope`) and the help seam (`TM_HELP_ARTICLES`); the session (`TmSession`, `tmSessionGuard`, `tmParseProfileCookie`), the launch mark (`TmLaunch`) and the single-membership rule (`tmSingleLiveTenant`); the client store (`TmClientStore`, `TmStoreRefusedError`, `TM_DOC_FORMATS`) and the tab bus (`TmTabBus`) with its message catalogue; the leader tab (`TmLeader`: whether this tab leads, the hub's status and the tenant's hub events) and the documents only the leader refreshes (`TM_LEADER_DOCUMENTS`); the boot script's result types and the app clock (`TM_NOW`); the derived router's pieces: the root component (`TmApp`), the route data (`TmRouteData`), the title strategy (`TmTitleStrategy`), the URL builders and classifier (`TmUrls`) with the search query's types; the entity registry (`TmEntities`, with its descriptors once the entity model loads, and the declaration types `tmEntity` takes, typed paths `TmPath` included), display names (`TmDisplayNames`, `tm-display-name`), client permissions (`TmPermissions`) and `tmRowsToObjects`; localization: `TmLocalization` (the language, the regional formats, the calendar and the zones, and their switches with the coalesced locale write), `TmServerText` (server codes and keys through the string packs) and `tmEnglish()`, the platform's English; the app-level keyboard shortcuts (`TmShortcuts`, `TmShortcut`, `TM_RESERVED_SHORTCUTS`, `TM_BROWSER_KEYS`, `TmShortcutConflictError`) and the shell frame's seams (`TmShell` with immersive mode, `TmShellMain`, `TM_SHELL_LAYOUT`) | eager |
| `@tellma/core/shell` | The tenant shell, the tenant picker and the signed-out page, which the derived routes load; until their owners arrive they are plain stand-ins, beside one placeholder page for the platform's page families | eager, through `import()` |
| `@tellma/core/memory` | The tab's memory of its pages: page state (`TmPageState`, `tmPageStateKey`), drafts (`TmDrafts`) and tree expansion (`TmTreeExpansion`), which the page hosts load with them | lazy |
| `@tellma/core/page-snapshot` | `tmSerializePageContext` and `TmPageContextSnapshot`: a page context read once into plain JSON, for the support dump and a future assistant | lazy, through `import()` by its callers |
| `@tellma/core/recents` | The recently visited pages (`TmRecents`), which the launcher and the home page list; the app loads it with `import()` after its first render, when visits start to count | lazy, through `import()` after first render |
| `@tellma/core/entity` | What the search and details pages share over the entity model: select plans (`tmSelectPlan`), which turn column keys into one query's terms and type its rows; value formats (`TmFormats`); the typed value in view mode (`tm-value`) and in edit mode (`tm-value-editor`), with a form's uploads (`TmUploads`); a plan column's grid column (`tmGridColumnFor`); and the client check of a picked file against its blob kind (`tmCheckBlobFile`) | lazy, with the page families |
| `@tellma/core/leader` | The leader election of the routed tenant over a Web Lock, and the leader tab's work: its realtime connection, the relay of hub events, the leader documents' reads and the polls of degraded mode; the app loads it with `import()` after its first render, and `TmLeader` reports what it decides | lazy, through `import()` after first render |
| `@tellma/core/config-check` | `tmCheckAppConfig`, the check of an app's configuration against the client metadata, which the build runs in Node and a development app at startup, and `tmCheckEntityDeclaration`, the check of one entity declaration against the Entities chunk | lazy |
| `@tellma/core/search-normalize` | `tmSearchNormalize`, the one text folding the launcher, the shortcuts filter, the field tree and the reports list match with | with the code that imports it |
| `@tellma/core/private` | Wiring shared by the platform's own entry points and packages; no API goldens, no stability guarantee, never imported by app code | — |

The other entry points of the framework arrive with the features they hold, each in the folder of
its name. The package also ships plain assets: `boot/tm-boot.template.js`, the boot script's
template, `styles.css`, the app's global stylesheet, which a distribution's `src/styles.css`
imports first (the tokens, the Latin fonts, the directive stylesheets, `styles/tm-frame.css`, the
shell frame's root variables and grid, and `styles/tm-boot-page.css`, the boot page's styles), and the default brand files
`brand/{wordmark,mark}-on-{dark,light}.svg`.

The platform's icon names live in `src/icons/platform-icons.ts`, which `provideTellma` registers
before each feature's `icons`: a platform surface that draws a new name adds it there.

The brand files are the Tellma logo: the wordmark, and the mark (the wordmark's round "a" with its
teal dot), each in ink for light backgrounds and in white for dark ones. They are cleaned from the
logo's SVG exports: one `<path>` per glyph with its fill, no clip paths, ids or editor metadata,
so two of them inline on one page cannot clash. The app renders them inline and gives them their
accessible name. `tellma-client build` copies each to `brand/` in the output unless the
distribution's `public/brand/` has a file of the same name.

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
reports an expiry that led there and drops a previous user's drafts. Until the drafts and the
session-ended dialog arrive, the paths beyond the tab's own state are tab-local: an expiry sweeps
every user's stored data (in a 1 s box; a sweep cut short, failed or unable to reach the disk
clears the boot record's user, so the next launch sweeps again), deletes the stale profile cookie and signs in again, returning to the
same page, so the user's data leaves the disk and the screen and the expiry is reported once. An
expiry within a minute of coming back from a sign-in shows the sign-in-failed boot page instead, so
a server that keeps refusing fresh sessions cannot loop the tab through sign-ins; the sweep runs at
the expiry either way.

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

The HTTP layer's response observers are not yet covered: an answer that lands after the end can
still throw from its user check or its version-tags observer.

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

### The tab's memory and recent pages

`@tellma/core/memory` holds what a tab remembers of its pages beyond the store, and loads with the
page hosts that use it, not with the app; the drafts' storage and keys stay in the primary
(`src/state/memory/tm-draft-keys.ts`), since the session's sign-in sweep removes a previous user's
drafts.

- **`TmPageState`** remembers, in memory, up to 20 pages' state by canonical path
  (`tmPageStateKey`), least recently used out first. `markStale({ tenantId, entity? })` marks the
  pages `TmUrls.describe` maps to the entity (a hatch's extra segment maps by its host), or every page
  of the tenant; a move of the tenant's held `permissions` tag does the same, and a change of its
  languages forgets the tenant's pages. `remember(key, state, { keepStale: true })` keeps a stale
  mark a write set while the page was away.
- **`TmDrafts`** keeps unsaved form copies in session storage under `tm.draft:<sub>:<t>:<entity>:<id>`,
  as `{ v: 1, at, data }`: written 300 ms after the last save and at once when the page hides, refused
  for another user's key, and counted in `dirtyCount`. A full storage moves this tab's oldest other
  drafts into memory and retries once; failing that, the draft lives in memory
  (`tm.drafts.quota-exceeded`).
- **`TmTreeExpansion`** keeps a tree page's expanded ids in the session partition's `tree:<page>`,
  oldest first and at most 500, written 250 ms after the last change; each write carries only the
  changes no earlier write has in flight, and settles only its own. The boot step reads the
  session partition with the others, so a tree's first load finds them.

`@tellma/core/recents` holds **`TmRecents`**, which lists the routed tenant's ten most recent pages
from the write-behind key `nav.recent`. The app loads it with `import()` after its first render.
A visit is a search, report, dashboard, settings, access or custom page, not the drill-through
list (route template `/:t/list/:segment`), whose context (`TmPageContexts.current`) stays current
for 2 s while visible; the visit stores the router's path and the context's kind and title. Until
the launcher lands, titles, icons and access come from `ɵTM_RECENTS_NAVIGATION`; the access filter
runs on read.

**The dump.** `ɵTmStateDump.read()` (in the primary) builds the state's support-dump section: the
mirrored documents' partition kinds, stored names and tags, the last sync, the unsaved
preference, draft and page-state keys, never a value, and whether the tab leads with the hub's
status as it sees it. The preferences, the drafts and the page
state report their keys to it through `ɵTM_STATE_PARTS` when they are created, so reading the
section creates none of them, and a tab that never loaded `@tellma/core/memory` reports no drafts.

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
  background sync decides what follows.
- **Degraded mode.** With realtime off, or a hub that has not connected for a minute, the leader
  polls (`tm.realtime.polling { cause }`): a sync of the tenant and a read of every leader document,
  each at most once a minute, while the leader is visible or a visible follower's probe asks. A
  leader document whose stored copy passes its `pollWhile` is re-read the same way while the hub is
  up.

A feature declares a leader document with a `TM_LEADER_DOCUMENTS` multi provider: its stored name,
the route the leader reads it from, the events that re-read it, its client format version, and an
optional `pollWhile`. Every tab reads it from the store; none but the leader fetches it.

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

`provideTellma` points `tm-image`'s blob fetcher at `TmApi.fetchBlob`, so an image request gets the
user check, the session's headers and the caller's signal, whose reason an aborted fetch rejects
with; under `provideTellmaTesting()`, `TmFakeApi` answers it.

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
shows `client-outdated` or `offline`. Each page gets its own page scope and leave checks, which a
navigation away runs, a move between My settings sections or into another tenant included; a
settings tab change keeps the page and runs none. On every page route, the host registers a
default page context of the route's kind, titled as the document is, whose route template names
the entity's segment (`/:t/centers`, `/:t/centers/:id`; the drill-through list keeps
`/:t/list/:segment`): `loading` until the page or an error view renders, then `ready`, or `error`
with the view's code (`forbidden`, `offline`, `client-outdated`) among its errors. A reused route
that shows another entity registers it anew. A page, or its family's host, that registers its own
context replaces it and keeps its actions. After a path change, focus moves to the page's
`[tmPageTitle]`, except on a settings tab change, where it stays on the tab list. The document's
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

`tmSearchNormalize` (`@tellma/core/search-normalize`) folds a query and the terms it matches the
same way: bidi controls stripped, NFKD with Latin and Arabic marks removed, lowercased, the
interchangeable Arabic letters folded and tatweel dropped, Arabic-Indic digits as 0-9, and
punctuation and symbols as single spaces. Nothing on the first frame matches text, so it is an
entry point of its own rather than part of the primary. The size gate's static-graph check
(`"tellma".staticGraphExcludes`) proves that neither the primary nor the eager `/shell` reaches it
through static imports, so the shell's deferred launcher panel and Shortcuts dialog load it with
`import()`.

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

## Entities, display names and permissions

`TmEntities` answers route matching, URLs and the shells from the Core chunk's entity summaries and
the merged declarations: `summary`, `bySegment`, `declaration` and `label`. `load()` fetches the
Entities chunk once, with the entity model, a lazy chunk of its own (`tm-entity-model`) that
weighs nothing on the first frame. After it, `get` returns an entity's descriptor gated by the
routed tenant's languages: a twin beyond them is no property, and with two or three languages each
twin's label carries its language's symbol ("Name (ع)") and its `contentDir` its direction.
`enum` labels a named enum's values, and `resolve` reads a column key or path: a scalar or twin
(`value`), a group anchor (`text`), a navigation, or a blob navigation (`image`, `file`), with a
slash label and the foreign key's path. Before the load a development app throws; a production
app gets a descriptor of the summary alone.

```ts
await entities.load();
const name2 = entities.get('acme.Customer').property('Name2'); // label "Name (ع)", contentDir "rtl"
const manager = entities.resolve('gl.Center', 'Parent.Manager'); // a navigation end, fkPath "Parent.ManagerId"
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
`tm-value-editor` takes the control a value's type takes inside a `tm-form-field`, bound to a
Signal Forms field: text, a textarea beyond 255 characters, numbers (decimals and longs held as
exact text; text that reads as no number, or as more digits than a number holds, keeps the
field's value and holds the field invalid, so a form's save waits), a checkbox, a date picker, a select per enum, and the image and file pickers, which
check the file against its kind, crop a cover-square image to its square, and upload as soon as
it is picked. A value the server owns, an instant, a related row and a type with no control show
read-only; a write-once value edits only while the record is created. A form that provides
`TmUploads` sees every upload in flight, to hold its save until they settle.

`tmGridColumnFor(column)` makes the grid column of a plan's column: a search grid's reads the
plan's rows by column key and sorts when the column orders; a form's child grid (`editable`)
reads the model's rows by their JSON names and edits only the root values the user writes. A
multilingual group there edits the active language's slot, so a host that makes the config in a
computed follows a language switch.

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
registers `tm.value` when `TmFormats` is first created; the entity picker's, the filters' and the
expression editor's strings follow the same seam. The seam itself costs the primary one method
and a set of the scopes registered.

## Decimals

`decimal` and `long` values travel as JSON strings and stay strings: a `TmDecimal` is the value's
exact wire text, so nothing is lost to a binary double. Arithmetic and comparison go through
`TmDecimals`; the lint rule `tm/no-decimal-operators` refuses `+`, `+=`, `<`, `<=`, `>` and `>=` on a
`TmDecimal`, which would join or compare its text.

```ts
const total = TmDecimals.add(line.amount, line.tax);
if (TmDecimals.compare(total, creditLimit) > 0) { … }
```

## Seams

`TmTelemetry` and `TmRequestLog` are abstract classes provided in root with quiet defaults that
keep nothing and send nothing. Code records
against them today; the observability features replace the defaults without changing a call
site. Tests replace them through `@tellma/core-testing`, which also puts `TmFakeApi` behind
`TmApi`.

The HTTP layer reaches three services that arrive later through internal seams in `src/api/`, each
with a default that is correct until its owner replaces it: the request locale (the document's
language and no calendar, until the localization service), the cache refresher (nothing cached, so
nothing to revalidate), and the worker's update check (`disabled`, as no worker controls the
page yet).

The route host reads one more: the platform's text, which reads the loaded string packs and
client tables until the localization service replaces it. A page's `securable` gate asks
`TmPermissions`.

## Import rules

Static imports between entry points follow one graph, always through `@tellma/core/<entry point>`
and never a relative path: the primary imports no other entry point, `/attention` only the
primary, `/shell` the primary and `/attention`, and no eager entry point imports a lazy one. A
shared entry point (`/private`, `/search-normalize`) loads with whichever code imports it: every
entry point but the primary and the shared ones may import it, so the primary never carries it.
Dynamic `import()` and `import type` cross any boundary, and a spec may reach another entry
point's test rigs (`*.testing.util.ts`) by a relative path, since no build ships either. The lint
rule `tm/core-import-boundary` enforces the graph, and ng-packagr refuses a cycle between entry
points.

The primary reaches `/shell`, `/recents` and `/leader` only through `import()` with a specifier TypeScript
does not follow, since ng-packagr would count a followed one as a cycle. The components it loads
from the shell are named in an internal contract that the shell's component map `satisfies`, so
either side's rename fails the build. A build or test that compiles the primary from sources in
this repository compiles only what its program holds, so its tsconfig also extends
`tsconfig.lazy-entry-points.json`, which lists the lazily loaded entry points.

## Budgets

Each entry point's gzipped weight, as a production app ships it after the Angular linker, has a
ceiling in `package.json` (`"tellma".budgetsInKb`), checked by `pnpm run size:check`; every raise
carries a note beside it.
