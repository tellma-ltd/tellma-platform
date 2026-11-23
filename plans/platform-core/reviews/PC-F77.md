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
  - `resolve` queues nothing after the end.
  - `#read` checks the end after its two awaits (the Entities chunk's load and the
    `tm-entity-model` import). A read in flight at the end therefore never calls
    `injector.get(TmApi)`.
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
      each await.
  - `src/state/sync/tm-cache-refresher.ts`.
    - A boundary sync in flight at the end re-armed `#timer` from its `.finally`. That timer fired
      after the end, and `refresh` → `#partitionOf` → `injector.get(TmSession)` threw in the
      timer.
    - `#arm` and `#later` now arm nothing after the end.
    - The end also aborts every tenant's `ɵsignal`, so the tenants' in-flight syncs and background
      reads end as `cancelled`, which nothing reports. Without the abort, the landed sync's store
      write reported a spurious NG0205 to telemetry. The display names and the launch cache's
      lists read with that signal too.
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
  rule for services that defer work.

### Tests

Each new test failed before its fix with NG0205, and passes after it.

- `tm-display-names.spec.ts`: "reads nothing once the app that queued the ids is destroyed".
  `resolve`, then `TestBed.resetTestingModule()`, then a frame passes. Before the fix it failed
  every run with the CI's exact rejection: `get #refresher` ← `#flush` ← `flush`.
- `tm-entities.spec.ts`: "keep no model when the app ends while the chunk loads".
- `tm-preferences.spec.ts`, "the app's end": "sends nothing when the app ends with a flush queued
  for the end of its task", and "arms nothing when a write lands after the app has ended".
- `tm-launch-cache.spec.ts`: "loads nothing when the app ends before the load it queued starts",
  and "stores nothing when the app ends while the rows are read".
- `tm-cache-refresher.spec.ts`: "arms nothing when the boundary's sync lands after the app has
  ended". This also asserts that no error and no `tm.sync.failed` is reported. It waits for the
  first sync with `vi.waitFor`. A bare `flush()` after `setUp` was itself timing-dependent: it
  failed once in four local runs alongside other specs, and I fixed that before committing.
- A second flake surfaced while I ran the full core project repeatedly. In one of three runs,
  `tm-localization.spec.ts` "announces a switch to the other tabs, never a preference that settles
  the language" failed with no `l10n.switched` post. The test asserted after one `tmSettle()` (five
  tasks), but the switch posts only after its loads land, and a module loaded for the first time
  can take longer while the whole suite runs. It now waits for the post with `vi.waitFor`. This is
  a test-only fix of a fixed-tick wait, the same shape as PC-117's group-slot test.

### Budgets

The primary measures 44.98 KB on this branch, against 44.87 KB on the base (24b907bd). That is
+0.11 KB, within the 45 KB ceiling, so the ceiling is not raised.
- **Before the refactor.** A first version, with a destroyed flag per service, an `onDestroy` per
  service and a cancellable frame, measured 45.00. Its constructors also changed the API report
  (`TmDisplayNames` and `TmEntities` gained `constructor()`). The `#app` form leaves the API reports
  unchanged.
- **Headroom.** The primary has 0.02 KB of headroom left, so the next primary checkpoint must raise
  it under D-031 or take bytes out.

### Deviations

None from the specs. The cancellation on the app's end is lifecycle behaviour that the specs do
not describe.

### Open risks

- I audited the code that defers work. Continuations of HTTP calls in other layers (the HTTP layer,
  the session, the shell) that call `injector.get` after a response lands are outside this item's
  grep (entity and state code). A response that lands after `ApplicationRef.destroy()` could still
  report a spurious error there. The refresher's abort now cancels the tenants' background reads,
  which covers most of them.
- The post-refresh-await check in `TmLaunchCache.#load` has no test of its own. Its entry check and
  its post-read check are tested.

### What reviewers should examine hardest

- `TmCacheRefresher`'s new abort of every tenant's signal on destroy. It is a behaviour change: any
  read holding `ɵsignal(t)` rejects as `cancelled` when the app ends.
- `TmLaunchCache.#load`'s check after the rows' read now overlaps the refresher's abort. That
  read carries the tenant's signal, so the end cancels it first. I kept the check so that the load
  does not depend on the order the two services' destroy callbacks run in. Its test failed before
  either fix.
- Whether `tmUnhandled`'s single real task after the work is enough for Chromium to dispatch
  `unhandledrejection`. The display-names test also waits a frame plus 50 ms inside the work.
