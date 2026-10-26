# Adversarial review of `00-architecture.md`

Reviewer: principal-engineer pass before the thirteen designers start. Scope: the skeleton as of
2026-10-20 12:45, checked against the brief, the digests (`facts/f01–f11`), specs 0008, 0011–0016,
0019–0021 and web facts verified today (URLs at the end). The skeleton is strong: the ownership map
is complete against the brief, every open question has an owner, the amendments to frozen specs are
routed to 0010, and the distribution API meets rule 1. The findings below are what would hurt if
the designers copied them as written.

Counts: **2 blockers, 7 major, 10 minor.**

---

## Blockers

### 1. The "no network before chrome, ever" guarantee is not held by the service-worker config as written

- **Severity:** blocker (brief rule 2A, marked non-negotiable)
- **Section:** §6.6 (service worker row), §7.2, §5.2 "Loading", §2.2
- **Problem.** §6.6 puts only `index.html`, `tm-boot.js`, the *initial* chunks, styles, fonts,
  metadata and strings in the `shell` asset group and "every other chunk" in `pages`, described as
  "lazy, prefetched at idle". In the Angular service worker, `installMode: lazy` caches **nothing**
  up front: a resource is cached only when first requested, and `updateMode: lazy` leaves changed
  resources unfetched until next requested (verified, angular.dev). "Prefetched at idle" is not an
  SW mode; it can only mean the *app* dynamically imports every chunk at idle so the SW caches them
  as a side effect. So the warm launch of any page whose chrome needs a lazy chunk — `/dashboards`
  (home, the default landing), `/search` + `/entity` + the `entities.json` chunk (every search page
  deep link or pin), `/details`, `/admin`, the calendar entry point `@tellma/core-ui/calendar-uq`
  (needed to format the greeting date and the `Tellma-Calendar` header on a `uq` tenant) — waits
  on the network unless the idle prefetch finished during a previous visit. On the "slow and spotty"
  links the brief names, the idle prefetch of every chunk can take minutes and is interrupted by
  tab close. After a deployment the same hole reopens for every lazily updated chunk. §7.2's table
  row "index.html, JS, CSS, fonts, metadata, strings ← shell group ← no network" therefore
  overstates: "JS" is only the initial bundle. The Playwright pin says "a warmed context", which
  quietly assumes the prefetch ran.
- **Evidence.** §6.6: "asset group `shell` (prefetch: index.html, tm-boot.js, initial chunks, …)
  and `pages` (lazy, prefetched at idle: every other chunk)". §5.2: "`entities` loads with `/entity`
  and is prefetched at idle". §3.2: "the platform lazy-imports `@tellma/core-ui/calendar-*` by the
  tenant's codes". angular.dev/ecosystem/service-workers/config: lazy "caches nothing up front";
  `updateMode: lazy` "changed resources are not fetched … updated only when next requested".
- **Fix.** (a) Define *warm* operationally in 0024: "the SW has activated a version and its
  `prefetch` groups are complete"; the first-render guarantee holds from that moment, not from
  sign-in. (b) Put every chunk the chrome of a routable page can need into a `prefetch` group
  (`installMode: prefetch`, `updateMode: prefetch`): all route entry points, `/entity`,
  `entities.json`, every calendar entry point the metadata lists, every language's strings. Keep
  `lazy` only for things no chrome needs (`core-ui/chart`, `/report-studio`, `/support`). Measure
  the install cost in the budgets table (it is a one-time background download; the SW installs
  after first render, so it does not slow the cold launch). (c) Make the Playwright pin honest: warm
  the context by waiting for the SW `prefetch` to complete, then hold every network request and
  assert chrome on *each* route family (home, search, details, report, admin), not one. (d) Add the
  calendar codes to the §5.2 build-time drift rules (a registered calendar without a client entry
  point must fail the build, like languages).

### 2. A different user signing in to the same browser can poison the previous user's cache and tabs

- **Severity:** blocker (security: cross-user leakage in caches and tabs on shared machines)
- **Section:** §5.5, §5.6, §5.8, §7.6, §7.7 step 4
- **Problem.** Three things combine. (1) The tab bus channel is `tm:<slug>:<sub>`, so tabs of the
  previous user never hear `session.started` posted by a new `sub`; §7.7 step 4 says "every tab
  sweeps the old user's partitions and reloads", but the only path that can reach an old-sub tab is
  the `visibilitychange`/`pageshow` cookie re-check (§7.6), which a hidden tab never runs. (2) A
  hidden old-sub tab keeps making requests on timers and resources (dashboard refresh, preference
  flush, `client/sync` on a hub event, a `resource()` reload): the browser attaches the **new**
  user's session cookie, the server answers with the new user's `me`, inbox, assignments or rows,
  and `TmCacheRefresher`/`TmClientStore` write them into the partition keyed by the tab's
  in-memory `sub` — the old user's partition. Nothing in `TmApi` checks that the cookie's `sub`
  still equals the tab's. (3) Drafts are keyed `tm.draft:<t>:<entity>:<id|new>` with no `sub`, so a
  new user opening the same URL in that tab (sessionStorage is per tab, not per user) restores the
  previous user's unsaved form. The §7.6 "signed out elsewhere" path does not fire here because the
  old session may simply have expired rather than been terminated through the hub.
- **Evidence.** §5.6 "channel `BroadcastChannel('tm:<slug>:<sub>')`"; §5.8 `TmDrafts.save(key)`
  "'tm.draft:<t>:<entity>:<id|new>'"; §5.3 observers write every response's tags into the
  refresher; §7.7 step 4 names the outcome but no mechanism.
- **Fix.** (a) Add a device-level channel `tm:<slug>` for session messages (`session.started`,
  `session.signed-out`, `session.changed`) and keep the per-sub channel for data messages. (b)
  `TmApi` reads the profile cookie before every request and after every response; a `sub` different
  from the tab's `TmSession.profile().sub` (or a missing cookie) rejects with kind
  `'unauthenticated'`, never feeds the refresher or the store, and posts `session.changed` so the
  tab sweeps and reloads at `/`. This also closes the gap for a tab that was in the back/forward
  cache. (c) Key drafts by `sub`: `tm.draft:<sub>:<t>:<entity>:<id|new>`, and clear every `tm.draft:*`
  key in every tab on `session.signed-out`/`session.changed`, not only in the signing-out tab (§7.6
  lists "drafts" only under tab A's sweep). (d) `TmClientStore.put` refuses a write whose partition
  `sub` differs from the live cookie's `sub` (cheap, and the last line of defence).

---

## Major

### 3. `TmBanners` in `/attention` creates an ng-packagr entry-point cycle with the primary entry point

- **Severity:** major (infeasible as drawn; the build fails)
- **Section:** §2.2, §5.14, §7.2, §7.5, §4.1
- **Problem.** `TmBanners` lives in `@tellma/core/attention` (0028). Its consumers include
  `TmUpdates` (§7.5 "the banner … shows") and the offline "Connection lost" banner driven by
  `TmApi` (§7.2 pin), both in the **primary** entry point. `/attention` depends on the primary
  (`TmLeader`, `TmUrls`, `TmDisplayNames`, …). ng-packagr builds entry points in dependency order
  and refuses a circular dependency between them, so primary → `/attention` → primary cannot ship.
  The §2.2 lint rule does not catch it because `/attention` is "eager", and the rule only forbids
  importing *lazy* entry points.
- **Evidence.** §2.2 table: `/attention` "bell and tray buttons with badges, toast policy,
  `TmBanners`"; §4.1 "Banner host `TmBanners` and precedence | 0028 (new) | 0024, 0025"; §5.14
  `declare class TmBanners` "0028"; §7.5 step 2 and §7.2 "the 'Connection lost' banner".
- **Fix.** Keep 0028 as the owner of precedence and copy, but place the *host* (`TmBanners`, the
  `TmBanner` type, the precedence levels 1–5) in the primary entry point — it is as generic as
  `TmToasts` — or declare an abstract `TmBanners` in the primary that `/attention` provides.
  Extend the ESLint boundary rule to forbid the primary from importing *any* other entry point,
  eager or lazy, and let `tellma-client build` run ng-packagr's cycle check on the showcase so the
  error is caught in CI, not by the first distribution.

### 4. Tenant content stays on disk after the session has ended

- **Severity:** major (security: cached data on shared machines)
- **Section:** §5.5, §5.7, §7.2 "A dead session never renders", §7.6, §12
- **Problem.** The design sweeps IndexedDB only on sign-out, on a different `sub` at boot, and on
  `session.signed-out` over the hub. When the 7-day idle session simply expires (the common end of
  a session on a shared or borrowed machine), nothing sweeps: the `inbox` summary (notification
  titles and actors), the `assignments` summary (document names), every cached `entity:*` list
  (centers, users, …), the avatar cache `tm-images-v1`, `me` (name, email, preferences, access
  summary) and `tm.boot` (`sub`, `lastTenantId`) remain readable to anyone with the device until
  someone signs in with another `sub`. "A dead session never renders" is true but beside the point:
  DevTools renders it. The §5.5 line "Page data is never written to disk" is also contradicted by
  the `entity:*` documents, which are rows. §12 lists no risk for this.
- **Evidence.** §5.5 `sweep(rule: 'sign-out' | 'other-users' | { sessionOtherThan })` — no expiry
  rule; §7.2 "An expired session has no profile cookie, so `tm-boot.js` redirects to `/bff/login`
  first" (and does nothing else); §5.7 `inbox?`, `assignments?`, `[list: entity:…]`.
- **Fix.** (a) Add a boot rule in 0025: profile cookie absent and `tm.boot.sub` set ⇒ the session
  ended ⇒ before redirecting to `/bff/login`, `tm-boot.js` (or a tiny pre-bootstrap step) sweeps
  the *content* documents of that user (`inbox`, `assignments`, `entity:*`, the session partition,
  `tm-images-v1`, every `tm.draft:*` key in this tab) and keeps `settings`, `me`, `tenant`, so a
  re-sign-in still launches warm. (b) Do the same on `session.expired` (§7.7 step 1) in every tab.
  (c) Put the residual (settings and `me` survive expiry; a full sweep on expiry would cost the
  warm relaunch) in §12 as a review flag with the alternative, and name the eviction age: a
  `TmStoredDoc.savedAt` older than the idle window is swept at boot regardless. (d) Reword §5.5 to
  "page data is never written to disk; cacheable lists are, under `entity:*`".

### 5. `reportMenu` and `home` have no fetch path, and the `reports` tag as designed storms the tenant

- **Severity:** major (a seam whose owner and consumer disagree; a scaling hazard)
- **Section:** §5.7, §6.1, §6.2, §7.3
- **Problem.** `TmLaunchDocs.reportMenu` and `home` are "tag `reports`" documents, but
  `ClientSyncResult` returns only `Tenant`, `Tags`, `Settings`, `Me` and `StaleEntities`; the
  `reports/…` route list has no `menu`, and `dashboards/home` is listed without a tag contract. So
  the launcher's Reports bucket, the pins that point at report definitions and the home layout have
  no revalidation path: a changed `reports` tag arrives, the refresher knows the doc is stale, and
  there is nothing to call. Separately, `core.ReportDefinitions` carries
  `[BumpsVersionTag("reports")]` and "every user can create an ad-hoc report" with a studio that
  previews as the author types: every author's save bumps a **tenant-wide** tag, which §7.4 fans out
  as `cache.changed` to every connected user, each of whom then runs `client/sync` within 0–3 s.
  A tenant with a few hundred users and a handful of active report authors will sync continuously.
- **Evidence.** §5.7 `reportMenu?: TmReportMenu; home?: TmHomeResolution; // tag reports`; §6.1
  `ClientSyncResult(TenantFacts Tenant, Tags, Settings?, Me?, StaleEntities)`; §6.2
  "`[BumpsVersionTag("reports")]` so published launcher entries cache like chrome"; §7.4 valued vs
  name-only events.
- **Fix.** (a) Add `POST /{t}/api/web/reports/menu` (0033) returning the caller's visible report
  entries (own, shared, published) and `POST dashboards/home` (0035) as named launch documents, and
  extend `ClientSyncResult` with `ReportMenu?` and `Home?` returned when their tags differ, so the
  one-round-trip promise of §0 decision 5 stays true. (b) Make the `reports` wire tag composite like
  `permissions`: a tenant part bumped only by `publish`, `transfer`, `delete` of a published
  definition and dashboard publication, and a user part bumped by the author's own saves and by
  shares addressed to that user. The studio's saves then reach only the author (and the shared-with
  users) and never the tenant group. (c) Say explicitly in 0034 that `/{t}/reports` is a custom
  page (the `search: () => import(…)` hatch), since a generic search page cannot merge client-side
  coded-report entries into server rows.

### 6. Decision 8 contradicts §7.6 on drafts, and the signed-out page is unreachable as specified

- **Severity:** major (the one-page decision list is what designers copy)
- **Section:** §0 decision 8, §7.6, §7.1 step 1, §8
- **Problem.** §0 decision 8 says drafts "persist in session storage across both" sign-out and
  re-authentication. §7.6 asks every tab for its dirty count, confirms, then sweeps "drafts" in tab
  A; other tabs "drop memory state" but their sessionStorage drafts are not mentioned (see finding
  2). Across sign-out, drafts must *not* persist (they are the signed-out user's data); across
  re-authentication they must. Also, `tm-boot.js` (§7.1 step 1) redirects to `/bff/login` whenever
  the profile cookie is absent, but `/signed-out` "renders without a session" (§8) — the page the
  sign-out flow lands on would bounce straight back to sign-in. `/tenants` is fine (it needs a
  session).
- **Evidence.** Quoted above; §8 row `/signed-out`: "renders without a session".
- **Fix.** Rewrite decision 8: "Sign-out is a broadcast that sweeps drafts in every tab after one
  confirmation; re-authentication and step-up are same-tab redirects across which drafts persist."
  Add to §7.6: other tabs clear their `tm.draft:*` keys on `session.signed-out`. Add to §7.1: the
  `tm-boot.js` cookie check exempts `/signed-out` (and nothing else).

### 7. Timer-driven leader heartbeats make leadership ping-pong on every tab switch

- **Severity:** major (connection churn, double connections during steals, cost)
- **Section:** §7.8, §5.6, §6.3 (0021 §9.2 row), §12 row 5
- **Problem.** The leader heartbeats "every 10 s" on a timer; a visible follower steals after 30 s
  of silence. Chrome's intensive throttling aligns a hidden page's chained timers to once a minute
  after 5 minutes hidden (verified). So: the user works in tab A (leader), switches to tab B for
  six minutes, B steals and reconnects; the user returns to A, B is now hidden, after five more
  minutes A steals and reconnects — and so on, every switch costing a negotiate, a connect, a
  `client/sync`, an `inbox/summary` and an `assignments/summary`, with two live connections while
  the old leader has not yet observed the steal (Azure SignalR bills on peak connections). The
  premise is only half right: Chromium throttles timers, but **not** WebSocket event handlers
  (Chromium's own `AllowAggressiveThrottlingWithWebSocket` comment: "aggressive throttling does not
  affect the execution of WebSocket event handlers"). A hidden leader therefore still *receives* the
  server's keep-alive every 15 s; only its own timer-driven pings slow down, which the 150 s
  `ClientTimeoutInterval` already covers. There is no need to steal from a healthy hidden leader.
- **Evidence.** §7.8 "heartbeat every 10 s", "no heartbeat for 30 s and this tab visible? → steal";
  Verified facts row on Chrome 88 throttling; Chromium source comment (URL below).
- **Fix.** (a) Emit the bus heartbeat from hub traffic, not a timer: post `leader.heartbeat` on
  every received hub message (the server keep-alive guarantees one every 15 s) and on every
  `hub.status` change. (b) A follower steals only when the mirrored `hub.status` has not been
  `connected` for 60 s **and** no heartbeat arrived in that window **and** it is visible; when the
  leader is healthy nothing moves. (c) Keep the 150 s `ClientTimeoutInterval` (the client's own
  pings are still timer-driven) and keep release-on-`freeze`/`pagehide`. (d) Stealing tabs add a
  0–2 s jitter so two visible followers do not race. Update §12 row 5 accordingly.

### 8. The write-behind preference flush cannot take its lock on `pagehide`, and `send` discards the tag it needs

- **Severity:** major (lost preferences; a spurious `client/sync` after every flush)
- **Section:** §5.7 "Flush", §5.3 `send`, §7.3 "Own writes need no sync"
- **Problem.** Flushes go "through `TmApi.send`" (fire-and-forget keepalive) and "one tab flushes
  at a time (`…:prefs`)". On `pagehide` a page cannot wait for a Web Lock; if another tab holds it
  the closing tab's unflushed keys (column choices, recents, pins folder order) are lost. And
  because `send` returns nothing, the new `preferences` tag that `me/preferences/set` returns is
  never adopted; the next response header carries a tag the tab does not hold, so §7.3 runs a
  `client/sync` that downloads the whole `Me` (bag + access summary) after every write-behind flush
  — the opposite of "own writes need no sync".
- **Evidence.** §5.7: "flush 2 s after the last change … and on `visibilitychange: hidden` and
  `pagehide` through `TmApi.send`; one tab flushes at a time (`…:prefs`)". §5.3: `send(route, body):
  void // fetch keepalive`. f03 §2.19: a write of its own returns the new `preferences` tag.
- **Fix.** (a) Flush with `TmApi.write` while the page is alive (adopt the returned bag and tag,
  then post `prefs.changed`), and use `send` only on `pagehide`, where the response cannot be
  awaited; mark the tags returned by own writes as "adopted" so the refresher ignores an equal or
  older header tag. (b) On `pagehide`, skip the lock: last-writer-wins per key makes a concurrent
  flush harmless, and a `pagehide` flush carries only this tab's dirty keys. (c) State the cap:
  a flush body stays under 16 KB so several closing tabs fit the 64 KiB keepalive budget.

### 9. The import flow has no owner

- **Severity:** major (a brief requirement without an owner)
- **Section:** §1, §4.1, §13, §3.3
- **Problem.** The brief's search page row 2 has "the import button, if the entity supports it", and
  0019 defines a multi-step client flow: stage the file (`import-file` blob), `inspect-import` →
  `ImportPlan` with sheet and column mappings and statuses, then `import` or `import/start`, then
  `ImportOutcome` or a 422 with per-row `ImportError`s. The skeleton derives "Import" from the
  operations (§3.3) and gives 0032 the *history* pages (`core.Imports`), but no spec owns the import
  dialog: file drop, mapping review, synchronous-vs-background choice, progress, and the error
  table. §13's search-page row lists "export menu" only; §4.1 row for 0030 says "export menu".
- **Evidence.** f05 §"Import flow and plan (0019 §7.1, §8.1)"; §4.1 "Search page host, …, export
  menu | 0030"; §13 "Search page | … trees, Trees Q1 | 0030".
- **Fix.** Give 0030 the import dialog (it sits on the search page toolbar and reuses the grid for
  the plan and the errors), with 0023 supplying a generic `file-drop`/upload control if none exists,
  and 0028 the background-import toast and bell rows. Add the row to §4.1, §13 and the §8 route
  grammar (`/{t}/{segment}?import=1` or a modal; decide).

---

## Minor

### 10. `TmSearchQuery` and its URL codec are owned by 0030 but must live in the primary entry point

- **Section:** §5.13, §8 "Query state … (0030 owns the details)"
- **Problem.** `TmUrls.search(entity, query?: TmSearchQuery)` is in the primary (0024) and is used
  by pins, recents, notifications and `TmUrls.target`, yet the query-string grammar's "details" are
  0030's, whose code is the lazy `/search` entry point. Either the codec is in the primary (then
  0024 owns it) or `TmUrls.search` cannot encode it.
- **Fix.** 0024 owns `TmSearchQuery` and the codec (`parse`/`format`), with the §8 table as its
  contract; 0030 consumes it and owns only the page's behaviour.

### 11. Two column defaults: server `DefaultSelect` and client `columns`

- **Section:** §3.3, §6.3 (0015 §1.2, §2.2 row), §10.1 Visibility Q3
- **Problem.** `StackDescriptor.DefaultSelect` ("the columns pickers use", gains `DisplayName` and
  `DefaultOrderBy`) and the declaration's `columns` both define "the default columns"; Visibility Q3
  says "the root's declared default columns" without saying which. Exports, drill-through lists,
  pickers and grids can drift apart.
- **Fix.** State the rule: the client `columns` is the one default for every grid-like surface
  (search, drill-through, picker advanced search, Excel export); the server `DefaultSelect` serves
  API consumers only. Or emit `DefaultSelect` into the entity metadata and let `columns` default to
  it, so a declaration without `columns` is the common case (rule 1).

### 12. The boot record has writers but no writer API, and `TmTheme` in the library would be one of them

- **Section:** §5.5, §5.14, §6.4 Part B, §11 "Storage names"
- **Problem.** `TmBootRecord` is a type; nothing in the sketches writes `tm.boot`, yet
  `TmLocalization.setLanguage` must update `lang`/`dir`, `TmTheme.set` must update `theme`, and the
  tenant route must update `lastTenantId`. §11 says only `TmClientStore`, `TmDrafts`,
  `TmPreferences` and `tm-boot.js` touch storage; `TmTheme` (0010 Part B, `@tellma/core-ui`) is then
  forbidden from persisting, but Part B says nothing about that boundary, so the library designer
  may make it write `localStorage`.
- **Fix.** Add `TmClientStore.boot: { read(): TmBootRecord; patch(delta): void }` to §5.5 (0025);
  state in Part B that `TmTheme` only writes `data-theme` and exposes `setting`, and `@tellma/core`
  persists it.

### 13. §6.4 is binding but still carries conditionals and one undefined concept

- **Section:** §6.4 Parts D, E, F; §9
- **Problem.** Part E reads "multi-select only if 0029 needs it" — a conditional in a binding list.
  §9 moves the modal's raw 600 px media query to `sm`, but Part D's rows do not include it. Part F
  says "a comparison that names a parameter in `OmittedParameters` folds to true" as if the set
  existed; 0008 has no such member (grep: only the omitted time-zone slot), so Part F must define
  `QuerySpec.OmittedParameters` (or a request member) before using it.
- **Fix.** Decide Part E now (0029's navigation `in:` filter needs multi-select; say yes). Add the
  600 px → `sm` row to Part D. Reword Part F: "adds `OmittedParameters: IReadOnlySet<string>` to
  the request; a comparison whose parameter is in the set folds to true".

### 14. The banner row reflows the page on state changes

- **Section:** §9 "Shell constants", "Stable layout"
- **Problem.** A 32 px banner row inserted between the app bar and the pinned bar pushes every page
  down when "Connection lost" or "New version" appears — while the user may be mid-keystroke in a
  grid cell. §9 excuses it ("never in response to a click or key press"), but the brief's rule 2D is
  about the interactive layout not moving under the user at all.
- **Fix.** Render banners as an overlay at the top of `main` (absolutely positioned under the pinned
  bar, `--tm-shell-top` unchanged) or reserve the row permanently when any banner level is
  registered. State the choice as a review flag.

### 15. Hub loss is presented as connectivity loss

- **Section:** §7.8 "After 5 s disconnected every tab shows 'Connection lost. Reconnecting…'"
- **Problem.** The hub can be down (Azure SignalR incident, a proxy that breaks WebSockets and
  SSE) while the API works; a permanent precedence-1 banner then shouts at a working user for the
  whole session. Realtime is "an optimisation, never a dependency" (0021).
- **Fix.** Tie the banner to `navigator.onLine === false` or to failed API calls (`offline`
  kind); on hub loss, degrade silently to the §7.8 polling mode after the reconnect ladder and show
  nothing (or a low-precedence "Live updates paused" chip in the bell).

### 16. Telemetry principles forbid free text, but `TmTelemetry.error` carries it

- **Section:** §5.9, §10.1 Observability Q1
- **Problem.** `error(error: unknown)` ships `Error.message`, which for a `TmApiError` from a 422 or
  a `query-invalid` includes the user's filter text or field values, contrary to "no tenant content
  or free text".
- **Fix.** Specify the error envelope: error class, `kind`, `code`, `status`, `traceId`, route
  template, a stack with URLs stripped to chunk names; never `message` for `TmApiError`, and a
  200-char cap plus a redaction pass for render errors.

### 17. Two missing escape hatches under rule 1

- **Section:** §3.6
- **Problem.** (a) A distribution cannot replace the home page: `/{t}` is 0035's and the "home
  resolution seam" is deferred, but a custom landing page is the single most common single-tenant
  request. (b) The tile layout is fixed ("image and four corners"); a custom tile component has no
  hatch, unlike cells (`display`).
- **Fix.** Add `home: () => import(…)` to `TmFeatureDefinition` (last feature wins; 0035 resolves
  it before its default) and `tile: { component: () => import(…) }` to `tmEntity` (0030).

### 18. The drill-through list takes its title from the URL

- **Section:** §8 `/{t}/list/{segment}?filter=&cols=&title=`
- **Problem.** Free text in a shareable URL becomes the page heading (and the recents/pins label);
  Angular escapes it, but a link can still carry a misleading title, and the title is not localized
  on a language switch.
- **Fix.** `?from={reportId|key}&cell=…` and derive the title from the definition and the clicked
  cell; keep `filter`/`cols` as the escape hatch with a generic heading.

### 19. Background requests must also stop cookie renewal, or the profile cookie outlives the session again

- **Section:** §6.3 (0011 §5.5 profile cookie row; 0014 §7 / 0016 §6.1 row)
- **Problem.** The v2 profile cookie is "re-issued whenever the session cookie is (sliding
  renewal)". The ASP.NET cookie handler slides on *any* authenticated request, including the ones
  carrying `Tellma-Activity: background`, which the skeleton says "does not stamp activity". A
  dashboard tab left open then keeps both cookies sliding while the server-side idle expiry is not
  extended, so after 7 days the cookie says "signed in" and the first call answers 401 — the very
  mismatch v2 was meant to remove (just reversed).
- **Fix.** State in the 0011 edit that a background request sets `ShouldRenew = false` on the
  session scheme (no cookie re-issue), so cookie life and server idle life are extended by the
  same requests.

### 20. Corrections to "Verified facts"

- **Section:** Verified facts; §2.2
- **Problem.** (a) `Clear-Site-Data` is Baseline as a header, but the `"cache"` directive was
  unsupported in Firefox 94–137 (supported again from 138) and in Safari before 17; the design does
  not depend on it (the client deletes `tm-images-v1` itself), so say "belt and braces" rather than
  rely on it. (b) "An ng-packagr entry point is one FESM file, hence at most one chunk" is not
  true: `import()` and `@defer` inside an entry point emit extra chunks (the skeleton's own `/shell`
  defers its panels). The entry-point split is a good discipline for *goldens and budgets*, not a
  guarantee about chunks; reword so a designer does not avoid `@defer` to honour it.
- **Fix.** Edit the two rows; keep the design.

---

## Checks that passed (so designers do not re-litigate them)

- Every brief open question has exactly one owner and a recommended answer (§10.1); every
  must-address finding of the architecture brief has an owner (§4.3).
- Every change to a frozen spec is routed to a 0010 part (§6.4); 0012 §11.2's seven amendments are
  real (0012 lines 1702–1764) and move to Part F.
- `/bff/user`, step-up as `401 step-up-required` with `WWW-Authenticate`, `Tellma-Version-Tags`
  on every tenant response (with `entities=`), `NoActivityStampMetadata`, `core.Exports` and
  `core.Imports`, hub groups `t{tenantId}` / `t{tenantId}.u{userId}` / `x{handle}`, and
  `KeepAliveInterval 15 s` / `ClientTimeoutInterval 30 s` all exist as the skeleton cites them.
- `/api/strings/{language}` and `/api/notification-catalogue` have no consumer other than the SPA
  (0011 §8, 0016 §2.1, 0013 §10.4, 0021 §2.5), so emitting them at build and removing the routes
  is safe.
- `Tellma-Time-Zone` is an optional request header with a server fallback chain, so dropping it
  from the client needs no 0016 edit (risk 12 stands as a review flag).
- The Navigation API (`canGoBack`) is Baseline newly available since January 2026 (Firefox 147,
  Safari 26.2), as cited.
- The CSP is coherent with a cached `index.html` (`'unsafe-inline'` styles, external
  `tm-boot.js`, Trusted Types policy names Angular uses), and `display-capture=(self)` matches the
  `getDisplayMedia` screenshot answer.
- The distribution API meets rule 1: a new entity is one declaration plus a markup-only layout;
  the common case is 7 + 17 lines; the escape-hatch table covers cells, bodies, filters, pages,
  actions, reports, menus, settings tabs, app-bar items, theming, providers and telemetry.

## Web facts verified for this review

| Fact | Source |
|---|---|
| Angular SW `installMode: lazy` caches nothing up front; `updateMode: lazy` fetches changed resources only when next requested; `navigationUrls` accepts negative patterns | https://angular.dev/ecosystem/service-workers/config |
| Chrome intensive throttling: hidden > 5 min, chain ≥ 5, silent 30 s, no WebRTC; wakes once per minute | https://developer.chrome.com/blog/timer-throttling-in-chrome-88 |
| Chromium: "aggressive throttling does not affect the execution of WebSocket event handlers" (`AllowAggressiveThrottlingWithWebSocket`, enabled by default) | https://redirect.github.com/electron/electron/commit/2a6d10c1592f83d2c57301f3c6bf4dcdaaeaac47 (restores the Chromium feature definition and its comment) |
| `Clear-Site-Data: "cache"`: Firefox 63–93 and 138+, Safari 17+ | https://caniuse.com/mdn-http_headers_clear-site-data_cache |
| Navigation API Baseline newly available January 2026; Firefox 147, Safari 26.2 | https://developer.mozilla.org/docs/Web/API/Navigation/canGoBack , https://caniuse.com/wf-navigation |
