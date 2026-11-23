# PC-F77: Client flake: TmDisplayNames flush after its injector is destroyed (NG0205)

## Implementer notes

### Root cause

`TmDisplayNames.resolve` queues ids and schedules one flush for the next animation frame (or a
`setTimeout` while the tab is hidden). Nothing cancelled that flush. `#flush` resolves
`TmCacheRefresher` lazily through `this.#injector.get`, so a flush that runs after the root
injector is destroyed throws NG0205. `#flush` is async and is started with `void`, so the throw
became the unhandled rejection that failed pc run 45146193126.

- **In the test.** `tm-value-editor.spec.ts` "keeps a related row's key property read-only, by its
  ungrouped id" renders a foreign key, whose display name calls `resolve`. The test can end before
  the next frame. TestBed's teardown then destroys the root injector, and the frame fires
  afterwards. Whether a frame lands inside the test depends on the runner's timing, which is why
  PC-117's run on the same code passed.
- **In a real app.** The root injector is destroyed by `ApplicationRef.destroy()`: an app unmounted
  from a host page, a hot reload, or a platform teardown. A flush queued in the app's last frame
  then throws the same way. The cause is in the product, so the fix is in the product.

### What was built

Every guard reads Angular's own `DestroyRef.destroyed` on the root injector (`#app`). Each service
keeps no flag of its own. That costs the primary fewest bytes, and the primary is at its ceiling
(see Budgets).

- `src/entity/tm-display-names.ts`.
  - `#flush` returns before it resolves anything once the app has ended.
  - (Review 1 removed the `resolve` guard and `#read`'s two post-await guards: `#read`'s
    `catch {}` already swallows a late NG0205, and nothing reaches `resolve` after the end.)
  - The pending frame or timer is not cancelled, because a flush after the end does nothing.
    Cancelling would need the handle and an `onDestroy` callback, which cost more bytes.
- Siblings found by grepping `src/entity` and `src/state` for `injector.get` or lazily resolved
  getters reached from a frame, an idle callback, a timer, a microtask or a promise continuation:
  - `src/entity/tm-entities.ts`. `load()`'s `.then` calls `injector.get(TmLaunchCache)` once the
    chunk and the model's import land. Because of the destroyed injector, the load rejected with
    NG0205. After the end it now resolves without setting a model.
  - `src/state/prefs/tm-preferences.ts`. A set queues its flush with `queueMicrotask`.
    - If the app ended in the same task, `#fire` → `#frozen` → `#partition` →
      `injector.get(TmSession)` threw NG0205 in the microtask, as an uncaught error.
    - A write that landed after the end reached `#schedule` → `#frozen` and threw as an unhandled
      rejection.
    - `#fire` and `#schedule` now do nothing after the end. The microtask cannot be cancelled, so
      the check is the guard.
  - `src/state/launch/tm-launch-cache.ts`. `entitySet`'s computed queues `#load` with
    `queueMicrotask`.
    - `#load` called `injector.get(TmClientMetadataStore)` after its refresh await. A list read
      that landed after the end wrote through the store's guard, which calls
      `injector.get(TmSession)`. Both were caught and reported to telemetry as spurious NG0205
      errors.
    - `#load` now checks `#life.signal.aborted` (the existing end-of-app abort) at entry and after
      the refresh await. (Review 1 removed the check after the rows' read: the refresher's abort
      cancels that read, and the continuation case could not be tested.)
  - `src/state/sync/tm-cache-refresher.ts`.
    - A boundary sync in flight at the end re-armed `#timer` from its `.finally`. That timer fired
      after the end, and `refresh` → `#partitionOf` → `injector.get(TmSession)` threw in the
      timer.
    - `#arm` and `#later` now arm nothing after the end.
    - (Review 1) `#partitionOf` returns `null` after the end, so a late `adopt`, `refresh` or
      `observe` does nothing. This stops the preferences' late write from reporting NG0205.
    - The end also aborts every tenant's `ɵsignal`, so the tenants' in-flight syncs and background
      reads end as `cancelled`, which nothing reports. Without the abort, the landed sync's store
      write reported a spurious NG0205 to telemetry. The display names and the launch cache's
      lists read with that signal too.
- (Review 1) `src/l10n/tm-locale-write.ts`, in the lazy locale-write chunk. A write that lands
  after the end is not adopted, an `offline` failure after the end arms no retry timer, and
  `#fire` sends nothing after the end, including the members queued behind a landed write.
- `TmClientStore`'s idle other-sessions sweep has the same shape. My grep found it, I fixed it and
  wrote a failing test for it, but I reverted the change on the coordinator's instruction, because
  PC-109 lands the same fix (`#ended`) and this branch must not conflict with it.
- Checked and left as they are:
  - `TmTabBus` and the leader election already keep a destroyed flag.
  - `TmDrafts`, `TmTreeExpansion` and `TmRecents` clear their only timers on destroy and arm none
    afterwards.
  - The `./entity` entry point's `TmUploads` aborts its uploads on destroy.
  - `TmPermissions`, `TmFormats` and the value editor resolve nothing from deferred callbacks.
- `src/routes/tm-app.testing.util.ts`. The new `tmUnhandled(work)` collects every error event and
  unhandled rejection raised while the work runs and keeps them from the runner, so a spec asserts
  on them itself. It ends a real task after the work, using `setTimeout` captured before any spec
  fakes the timers, because the page reports a rejection in a task of its own.
- The core README's "Client state" section has a new paragraph, "The app's end", which states the
  rule for services that defer work, lists what each service does after the end, and names the
  HTTP layer's response observers as not yet covered.

### Tests

Each new test failed before its fix with NG0205, and passes after it.

- `tm-display-names.spec.ts`: "reads nothing once the app that queued the ids is destroyed".
  `resolve`, then `TestBed.resetTestingModule()`, then a frame passes. Before the fix it failed
  every run with the CI's exact rejection: `get #refresher` ← `#flush` ← `flush`.
- `tm-entities.spec.ts`: "keep no model when the app ends while the chunk loads".
- `tm-preferences.spec.ts`, "the app's end": "sends nothing when the app ends with a flush queued
  for the end of its task", and "arms nothing when a write lands after the app has ended".
- `tm-launch-cache.spec.ts`: "loads nothing when the app ends before the load it queued starts",
  and "stores nothing when the app ends while the rows are read". The second now covers the
  refresher's abort of the read. (Review 1) "reads no rows when the app ends while the sync for
  the list's tag is in flight" fails with NG0205 reported to telemetry without the post-refresh
  check.
- `tm-cache-refresher.spec.ts`: "arms nothing when the boundary's sync lands after the app has
  ended". This also asserts that no error and no `tm.sync.failed` is reported. It waits for the
  first sync with `vi.waitFor`. A bare `flush()` after `setUp` was itself timing-dependent: it
  failed once in four local runs alongside other specs, and I fixed that before committing.
  (Review 1) It now also asserts that no timer is left, which fails without the `#arm` guard.
  "arms no jitter timer for a hub event that arrives after the app has ended" fails without the
  `#later` guard.
- (Review 1) `tm-preferences.spec.ts` "arms nothing when a write lands after the app has ended"
  now also asserts no telemetry error and no `tm.prefs.flush-failed`. It fails with NG0205
  without the `#partitionOf` guard.
- (Review 1) `tm-locale-write.spec.ts`, "the app's end": "adopts nothing when a write lands after
  the app has ended", "plans no further try when an offline failure lands after the app has
  ended" and "sends nothing queued behind a write that lands after the app has ended". Each fails
  without its own guard (the `#fire` guard is also load-bearing for the offline test).
- A second flake surfaced while I ran the full core project repeatedly. In one of three runs,
  `tm-localization.spec.ts` "announces a switch to the other tabs, never a preference that settles
  the language" failed with no `l10n.switched` post. The test asserted after one `tmSettle()` (five
  tasks), but the switch posts only after its loads land, and a module loaded for the first time
  can take longer while the whole suite runs. It now waits for the post with `vi.waitFor`. This is
  a test-only fix of a fixed-tick wait, the same shape as PC-117's group-slot test.

### Budgets

The primary measures 44.97 KB on this branch after review 1, against 44.87 KB on the base
(24b907bd). That is +0.10 KB, within the 45 KB ceiling, so the ceiling is not raised. The
locale-write chunk's guards cost the primary nothing.
- **Before the refactor.** A first version, with a destroyed flag per service, an `onDestroy` per
  service and a cancellable frame, measured 45.00. Its constructors also changed the API report
  (`TmDisplayNames` and `TmEntities` gained `constructor()`). The `#app` form leaves the API reports
  unchanged.
- **R1-3.** The `TmHttpApi` guards (a `cancelled` attempt from `#conclude` after the end, and the
  end checked in the version-tags timer) measured 45.01, so they are not in this item.
- **Headroom.** The primary has 0.03 KB of headroom left, so the next primary checkpoint must raise
  it under D-031 or take bytes out.

### Deviations

None from the specs. The cancellation on the app's end is lifecycle behaviour that the specs do
not describe.

### Open risks

- The HTTP layer (R1-3) still resolves through the injector when a response lands after the end:
  `#check`, `#launch.settle`, and the version-tags timer, which throws NG0205 as an uncaught error.
  It did not fit the primary's ceiling and needs a follow-up item. The session and the shell were
  outside this item's grep.

### What reviewers should examine hardest

- `TmCacheRefresher`'s new abort of every tenant's signal on destroy. It is a behaviour change: any
  read holding `ɵsignal(t)` rejects as `cancelled` when the app ends.
- `TmCacheRefresher.#partitionOf`'s guard makes every public entry a no-op after the end. A cold
  `ɵenter` after the end now finds no partition rather than throwing NG0205.
- Whether `tmUnhandled`'s single real task after the work is enough for Chromium to dispatch
  `unhandledrejection`. The display-names test also waits a frame plus 50 ms inside the work.

## Review 1

Reviewed commit 09a5053c against the LEDGER entry for PC-F77, spec 0025 (§3.2 cold entry, §5.3,
§9.4, §14) and spec 0029 (§4, display names), PLAN §7.1 and the live DECISIONS.md (D-247, D-248).
What I ran myself on 09a5053c:
- `ng test core` on the six touched spec files: 201 tests, all green.
- Each product file's fix reverted alone, with its own spec run:
  - display names, entities, preferences (both tests) and the refresher each fail as claimed;
  - in the launch cache, only "loads nothing when the app ends before the load it queued starts"
    fails. "stores nothing when the app ends while the rows are read" still passes, because the
    refresher's abort cancels the read first (R1-4).
- Single guards removed, one at a time:
  - covered: the refresher's abort, its `#arm` guard, the preferences `#schedule` guard and
    `#fire` guard;
  - not covered: the refresher's `#later` guard, and the display names' `resolve` guard with both
    `#read` guards (R1-4).
- Temporary probes, all reverted. They found the preferences telemetry error (R1-2) and the
  locale write's unhandled rejection (R1-1).
- `ng build core` and `size:check`: "." 44.98 of 45, matching the commit. The locale-write chunk
  is 1.49 of 2.
- The worktree was restored to the commit, and dist/core was rebuilt from it.
- CI on pc/PC-F77 was skipped on push, as the process expects.

Checked and sound:
- **The right DestroyRef.** In a root service, `inject(DestroyRef)` resolves through
  `DestroyRef.__NG_ENV_ID__ = injector => injector`, so it is the root `R3Injector`: the injector
  that `TestBed.resetTestingModule()` and `ApplicationRef.destroy()` destroy. No component's
  DestroyRef is involved. `R3Injector.destroy()` sets `_destroyed = true` before it runs any
  `onDestroy` hook, so a check made from another service's hook already reads `true`.
- **Root cause.** The display names' `#flush` resolved `get #refresher` outside `#read`'s
  try/catch, which matches the CI stack. The `#flush` guard is the essential fix. The frame left
  pending runs once and returns, and re-arms nothing.
- **Timers.**
  - The preferences guards return before `clearTimeout` and before arming anything, and
    `onDestroy` has already cleared every tenant's timer.
  - The refresher's `#arm` still clears `#timer` and arms nothing.
  - The throttle wait inside the lock listens to the same signal, so the abort rejects it and
    clears its timer.
  - Nothing re-arms in the services this commit changed (but see R1-1 for `TmLocaleWrite.#retry`).
- **Behaviour change (c).** The refresher's new abort on the app's end creates no
  user-visible state and reports nothing:
  - `#failed` drops `cancelled` from `tm.sync.failed` and from the presenter.
  - Non-cold joins swallow it.
  - A cold run rethrows it to `ɵenter`. Both of `ɵenter`'s callers handle that: the effect's
    `.catch`, and the route host's `.then(_, …)`. The route host's `current()` is false once it is
    destroyed, so it shows no error view.
  - `TmLeadership`'s refreshes are `void` and non-cold, and the election keeps its own destroyed
    flag.
  - The display names and the launch cache treat a `TmApiError` silently.
  - `pagehide` already aborts the same controllers, so every consumer already handled this
    cancellation.
  - Spec 0025 §5.3 step 5 names only two abort triggers: leaving the tenant, and `pagehide`. But
    §14's "aborted with their owner" covers the app's end, so I flag no deviation.
  - Not clearing `#aborts` on the end is right: an `ɵsignal` asked for after the end comes back
    already aborted.
- **The localization wait (d).** `vi.waitFor` uses its defaults: a 1000 ms limit, checked every
  50 ms. The spec runs real timers. The condition is exact equality on the posted messages, so a
  duplicate post or a post from the settled preference still fails the test. What it waits for is
  the writer chunk's first dynamic `import()` in `#apply`'s step 1, which is real async work. It
  hides no product race.
- **Live behaviour.** In a live app every new check is false, and `TmEntities.load` is unchanged
  until the end. After the end, `load()` now resolves instead of rejecting. Its only post-end
  consumer, `TmRouteHost.load`, is already guarded by `#run` and `#destroyed`.
- **Bytes (e).** Measured 44.98. The remaining primary cost is the four `inject(DestroyRef)` fields
  and the guards that the tests above prove. I found no materially cheaper shape. Removing the
  three untested display-names guards (R1-4) measures 44.97, so it saves only 0.01 KB.
- **Other services checked for the same shape (f).** These already guard their deferred work:
  `TmLocalization` (`#destroyed`), the app's init (`alive`), `TmRouteHost` (`#run`/`#destroyed`),
  the leader election, `TmDrafts`, `TmTreeExpansion` and `TmRecents`. `TmSessionPaths` defers
  only user-driven flows. The unfixed siblings are R1-1 and R1-3, plus `TmClientStore`'s sweep,
  which PC-109 fixes.

### Findings

**R1-1 (major): `TmLocaleWrite` has the same defect and raises the same unhandled NG0205
rejection.**
`client/projects/core/tellma-core/src/l10n/tm-locale-write.ts:312-342`.

When a locale write lands after the app's end, its fulfilled handler calls
`this.#refresher.adopt(tenantId, …)` (line 319). `adopt` resolves `TmSession` through the
destroyed injector: `#partitionOf` calls `get #session`, which calls `injector.get`. The
preferences probe in R1-2 shows that stack. The chain is `.then(ok, fail).finally(…)` with no
`.catch`, so the NG0205 becomes an unhandled rejection. That is the exact symptom that failed pc
run 45146193126.

I checked this with a temporary test in `tm-locale-write.spec.ts`:
1. Hold a write.
2. Call `TestBed.resetTestingModule()`, then land the write. The fake `adopt` resolves through
   the destroyed injector, as the real `adopt` does.
3. `tmUnhandled` collected `[NG0205]`.

The same file has two more post-end paths:
- An `offline` failure that lands after the end calls `#retry`, which arms a fresh `setTimeout`
  (line 368) after `onDestroy` has already cleared the queue's timers. That is a re-armed timer.
- `.finally` calls `#fire(tenantId)` (line 335), which can send queued members after the end.

Failure scenario: a test drives a tenant locale switch through the real refresher, such as
tm-localization.spec, or later the Language & region page tests. If that test ends while the
write is in flight, the client job fails as this item's run did. `ApplicationRef.destroy()` in a
host page does the same.

Fix: guard the handlers with `inject(DestroyRef).destroyed`. Return before `#rebase` or `adopt`,
and in `#retry` and `#fire`. This code lives in the lazy `tm-locale-write` chunk (1.49 of 2), so
it costs the primary nothing. Alternatively, R1-2's refresher-side guard covers the `adopt` throw,
but not the re-armed timer. Add a test: a write in flight, the end, the write lands (and one
`offline` variant), and `tmUnhandled` is empty with no further write sent.

**R1-2 (minor): a preferences write that lands after the end still reports a spurious NG0205 to
telemetry, and the new test does not check for it.**
`client/projects/core/tellma-core/src/state/prefs/tm-preferences.ts:555-557`.
`client/projects/core/tellma-core/src/state/prefs/tm-preferences.spec.ts`, "arms nothing when a
write lands after the app has ended".

`#write` awaits the server, then calls `this.#refresher.adopt(...)` inside its try. After the end,
`adopt` throws NG0205: `adopt`, then `#partitionOf`, then `get #session`, then `R3Injector.get`.
The catch sends it to `#failed`, which records `telemetry.error(NG0205, { source: 'tm-preferences'
})` and a `tm.prefs.flush-failed` event with `kind: 'server'`.

I checked this by adding `rig.telemetry.errors()` to the new test. It held exactly that NG0205,
with that stack, and the two items `tm.error.captured` and `tm.prefs.flush-failed`. The commit
fixed this same symptom in `TmLaunchCache` ("reported to telemetry as spurious NG0205 errors"),
and the commit message says the preferences' post-end write is handled.

Fix, either of:
- return from `#write` when `this.#app.destroyed` right after the await;
- or guard `TmCacheRefresher.#partitionOf` to return `null` once the app has ended. `adopt`,
  `refresh` and `observe` then become no-ops after the end. That one primary guard also covers
  R1-1's `adopt` throw and `ɵenter`'s post-end `refresh`.

Either costs about 0.01 KB, within the 0.02 KB of headroom, or within the 0.01 KB that R1-4's
removals free. Add `expect(rig.telemetry.errors()).toEqual([])` and assert that no
`tm.prefs.flush-failed` event was recorded.

**R1-3 (minor): the HTTP layer still resolves through the injector when a response lands after
the end, including from a timer.**
`client/projects/core/tellma-core/src/api/tm-http-api.ts:1040-1058`.

This is the implementer's open risk 1, recorded here as an unfixed sibling. For a response that
lands after the end, `#conclude` calls:
- `#check`, which calls `get #session`. The NG0205 is caught and returned cast as a `TmApiError`,
  although it is not one.
- `this.#launch.settle`, which calls `injector.get(TmLaunch)` and makes the call reject.
- for a response that carries `Tellma-Version-Tags`, `setTimeout(() =>
  this.#cache.observe(...), 0)`. The `#cache` getter throws NG0205 inside that timer, as an
  uncaught error.

Writes carry no signal, so the refresher's new abort does not cover them. In no current spec
does a tagged response land after a teardown, so this is latent. A future test that answers a
write with version tags after teardown would flake the same way.

Fix: `TmHttpApi` reads the root `DestroyRef`. Once it is destroyed, `#conclude` skips the
observers and returns a `cancelled` attempt. Alternatively, abort `#cancellable` on destroy and
check the end in the timer. If the primary bytes are not available, raise a follow-up fix item
rather than leave it unrecorded.

**R1-4 (minor): five of the new guards have no test that fails without them; two of those are
redundant.**
- `client/projects/core/tellma-core/src/state/sync/tm-cache-refresher.ts:540-542` (`#later`).
  Removing it leaves the refresher spec green.
- `client/projects/core/tellma-core/src/entity/tm-display-names.ts:485` (`resolve`), `:693-696`
  and `:703-705` (`#read`). Removing all three leaves the display-names spec green. They are
  redundant:
  - `#read`'s bare `catch {}` already swallows an NG0205 from `#entities.get`, `#plan` or
    `injector.get(TmApi)`;
  - no deferred caller reaches `resolve` after the end.
- `client/projects/core/tellma-core/src/state/launch/tm-launch-cache.ts:308-310` (after the
  refresh) and `:329-331` (after the read).
  - The post-refresh check is untested, as disclosed.
  - "stores nothing when the app ends while the rows are read" passes with `tm-launch-cache.ts`
    reverted alone, because the refresher's abort cancels the read first. So the post-read check
    has no test of its own either. The implementer's note says only that the test failed before
    *either* fix.
  - The case the post-read check uniquely covers is a read that resolved before the end and
    continues after it.

Fix:
- drop the three display-names guards, which saves 0.01 KB of primary;
- add a refresher test in which a hub-driven `#later` would arm after the end;
- for the launch cache, test the continuation case: land the read, then end the app in the same
  turn, before the continuation runs. Or remove the post-read check, if it cannot be tested.

**R1-5 (minor): the README's new paragraph overstates what the change does.**
`client/projects/core/tellma-core/README.md:147-153`.
- "The display names' frame flush is cancelled" is not true. The frame still runs, and its flush
  returns at once. The implementer's notes say so.
- "So none of their deferred work outlives the app" is not true while R1-1 and R1-3 stand.

Fix: say that the flush does nothing after the end, and correct the general claim once R1-1 and
R1-3 are settled.

### Responses

- **R1-1:** fixed in the "PC-F77: review 1 fixes" commit (child of 09a5053c). `TmLocaleWrite` reads
  the root `DestroyRef` as `#app`: the fulfilled handler returns before `#rebase` and `adopt`,
  `#retry` plans nothing, and `#fire` sends nothing once the app has ended. Three tests in
  `tm-locale-write.spec.ts`, "the app's end", one per path; each fails without its guard. The
  guards live in the lazy chunk.
- **R1-2:** fixed in the same commit, with the refresher-side guard: `#partitionOf` returns `null`
  once the app has ended. The preferences test now asserts no telemetry error and no
  `tm.prefs.flush-failed`, and it fails with that NG0205 without the guard.
- **R1-3:** not fixed. The guards (a `cancelled` attempt at the start of `#conclude` after the end,
  and the end checked in the version-tags timer) measured "." 45.01 against the 45 ceiling, after
  R1-4's savings, so I reverted them. It needs the follow-up item. The README names the gap.
- **R1-4:** fixed in the same commit.
  - The display names' `resolve` guard and both `#read` guards are removed.
  - New refresher test "arms no jitter timer for a hub event that arrives after the app has
    ended"; it fails without the `#later` guard.
  - New launch-cache test "reads no rows when the app ends while the sync for the list's tag is in
    flight"; it fails without the post-refresh check (NG0205 to telemetry).
  - The post-read check is removed as redundant: the refresher's abort cancels the read, and the
    continuation case could not be driven deterministically. Its test stays, as a test of that
    abort.
  - The new `#partitionOf` guard made the refresher's `#arm` guard untested: without it the
    existing boundary test stayed green. That test now also asserts that no timer is left after
    the late sync settles, and it fails without the `#arm` guard (one timer left).
- **R1-5:** fixed in the same commit. The paragraph now says the display names' flush still runs
  and returns, lists what each service does after the end, and names the HTTP layer's response
  observers as not yet covered.
