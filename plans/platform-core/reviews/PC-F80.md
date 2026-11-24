# PC-F80: Two client core specs flake under load across first-use dynamic imports

## Implementer notes

### The defect

Several `core` client specs waited a fixed number of task turns (`tmSettle()`: five
`setTimeout(0)` turns; `flush()` in the HTTP spec: five `vi.advanceTimersByTimeAsync(0)`) across
work that first loads a lazily loaded chunk with `import()`. Under the unit-test builder each
spec file runs in its own page, and none of the affected spec files holds the chunk in its static
import graph (checked with a throwaway graph script over every `core` spec), so the first such
call in a file fetches the chunk from the busy dev server. Under load that fetch outlasts the
turns, and the assertion reads the state from before the work.

Reproduced before the fix, `ng run core:test` under 20 busy-loop CPU processes (as PC-155 R1 did):
the first run passed 1489/1492. The two named specs failed, plus a third of the same shape:

- `TmNavigation` "labels and re-sorts the entries when the language switches" (`en` for `ar`:
  the switch waits on the `tm-locale-write` chunk).
- `TmHttpApi` "blames the client when the server is newer, and asks the worker to update"
  (`write.error` undefined: the verdict waits on `tm-api-verdict`).
- `TmHttpApi` "aborts the reads, downloads, uploads and blob fetches in flight once signing-out"
  (the upload was not yet sent: it waits on `tm-api-transfer`).

### Finding every exposed spec

Load only exposes the first crossing in each file, in run order. To find every spec that counts
turns across one of these imports, whatever the order or filter, I injected a delay locally (never
committed): each `import()` of `tm-locale-write`, `tm-api-verdict`, `tm-api-transfer` and
`tm-entity-model` waited a real 300 ms first. On the unchanged specs that failed 43 of 1492 tests
in four files: `tm-http-api.spec.ts` (27), `tm-localization.spec.ts` (13), `tm-navigation.spec.ts`
(1) and `tm-route-host.spec.ts` (2, through the route host's load of `tm-entity-model`). With the
fix, the same injection passes 1492/1492.

A second injection, 2000 ms on each module's first import only, failed four display-name specs
(`tm-display-names`, `tm-entity-field`, `tm-entity-pickers`, `tm-value`). That is an artefact of
the injection: their rig awaits `TmEntities.load()`, which loads `tm-entity-model`, before the
test, so the display names' own `import('./tm-entity-model')` later finds the module already
evaluated, and only the injected delay (counted per importing file) outlasted their `vi.waitFor`.
I left them as they are.

`tm-session-flows` does not exist on this branch; no spec crosses it.

### What was built

Specs and one test helper only. No product code changed, and nothing reaches a bundle.

- `tmSettleUntil(condition)` in `src/routes/tm-app.testing.util.ts`: `tmSettle()`, then a task
  and a render at a time until the condition holds. The test's own timeout bounds it; it adds no
  retry and no fixed wait.
- `tm-http-api.spec.ts`, fake timers: `flushUntil(condition)` flushes, then advances by zero
  until the condition holds, so fake time never moves while the chunk loads.
  - `verdictOf` is split into `refused` (sends the write once the request is out and answers it)
    and `verdictOf` (also waits until the write settles). The 3 s worker-timeout spec and the
    post-end refusal spec wait until the worker is asked (`updates.checks === 1`), which happens
    only after the verdict's rules load and leaves the write unsettled.
  - The slot-swap and server-outdated-retry specs wait on the recorded verdicts before advancing
    the clock, so the 30 s retry wait starts before they count it.
  - Uploads wait until the XHR exists (`sent(rig)`); the signing-out abort spec waits for it too.
  - Downloads wait until the download settles; the watchdog spec now passes `onProgress` and
    waits for each chunk to be read before advancing 50 s again.
- `tm-localization.spec.ts`: every switch in a tenant waits on the member it changes
  (`language()`, `calendarCode()`, `digits()`, `locale()`, `localeSave()`). `afterTheWrite(api)`
  no longer sleeps 700 ms (the locale write's 600 ms coalescing delay starts only once the change
  applies, after the chunk loads); it waits until the next `users/me/locale` call is made, then
  settles. A `localeWrites(api)` helper replaces the three inline filters.
- `tm-navigation.spec.ts`: the language-switch spec waits until `language()` is `ar`.
- `tm-route-host.spec.ts`: the two specs that first enter an entity's page wait until the title
  is focused or the context is ready.
- Two existing `vi.waitFor` wrappers around `tmSettle` (the tab-switch announcement in
  `tm-localization.spec.ts`, the column rebuild in `tm-grid-column-for.spec.ts`) were bounded by
  `vi.waitFor`'s 1 s default; they now use `tmSettleUntil` too.
- CONTRIBUTING.md: a paragraph stating the rule (wait on state, never on a count of turns).

### Deviations

None from a spec. The fix waits on state rather than preloading the chunks in the specs'
set-up (the reviewer's other option), as the orchestrator asked: a preloaded chunk still leaves
the specs counting turns across a cached `import()`, whose resolution Chromium schedules on its
own task queue.

### Evidence

Every run is `ng run core:test --watch=false` over the whole `core` project (85 files, 1492 tests)
under 20 busy-loop node processes on this 20-core machine.

- Before (the specs as on platform-core):
  - run 1 (cold vite cache): 1489/1492, the three failures above;
  - run 2 (warm): 1490/1492, the two named specs;
  - run 3: no test ran (vitest could not connect to the browser session under the load); I did
    not count it.
- After, three runs: 1492/1492 each (vitest-reported setup, import and test times comparable to
  the warm before run).
- Injection without load, 300 ms on every call: 43 failures before, 0 after.

### Open risks

- A spec whose wait condition never holds now fails on the test timeout (5 s) rather than at
  its assertion, so the failure message is "Test timed out". The assertions after each wait still
  report the specifics when the state arrives but is wrong.
- Negative checks after a turn-counted settle ("nothing changes until the strings land") stay as
  they were: under a slow chunk they pass for the wrong reason, but each such test then waits for
  and asserts the full sequence of states (`new Set(seen)`), so no intermediate state slips by.

### What reviewers should examine hardest

- Each wait condition: that it is the state the original assertion meant, and that it cannot
  hold early (for example the route host's focused-title condition, or `afterTheWrite` counting
  calls).
- The fake-timer changes in `tm-http-api.spec.ts`: that no `flushUntil` moves fake time, so the
  3 s, 29.999 s and 30 s boundaries keep their meaning.
- Whether any other core spec counts turns across a lazily loaded chunk that the injection did
  not cover (for example `@tellma/core-ui/confirm` from the page registry, or the calendar entry
  points, which load through `import()` as well).

## Review 1

### Findings

What I ran, on ec93b432 in this worktree, every change reverted afterwards (`git status` clean):

- `ng run core:test --watch=false` with no load: 1492/1492. Under 20 busy-loop node processes: 1492/1492.
  The parent's specs under the same load passed 1492/1492 in my one run (warm cache), so load alone
  did not reproduce it here; the injection below does, deterministically.
- A real 300 ms delay injected before every `import()` of the implementer's four chunks: the parent's
  specs fail 43/1492 (27 `tm-http-api`, 13 `tm-localization`, 1 `tm-navigation`, 2 `tm-route-host`),
  exactly the implementer's figure; this commit's specs pass all of them.
- The same delay extended to every other lazy `import()` the core specs reach (the `uq` and `et`
  calendar entry points, `@tellma/core-ui/confirm`, `tm-entity-paste`, `@tellma/core/shell`,
  `/recents`, `/leader`, core-ui's `grid-editing`, `chart-engine` and the blob fetcher's
  `@angular/common/http`): 4 failures, all outside the implementer's set (R1-2, R1-3). The calendars
  and the rest add none.
- eslint on the six touched client files and `tsc -p projects/core/tellma-core/tsconfig.spec.json`: clean.
- Each wait condition, read against the product code: every one holds only after the work under test
  (I checked `digits() === 'Latin'`, the one that looked vacuous: digits is `null` before the change,
  and the write is `saving` once it holds). No `flushUntil` moves fake time: the worker-timeout spec
  starts its 2 999 + 1 ms count once `updates.checks === 1`, which `#askWorker` reaches only after it
  arms the 3 s cap; the 29.999 s, 30 s and 30 s-retry specs start counting once the verdict is recorded,
  and the retry timer is armed in the microtasks that `advanceTimersByTimeAsync`'s first real task
  drains. The boundaries keep their meaning.

1. **R1-1 (major) A wait that never ends keeps running after its test times out, and breaks the
   next tests.** `client/projects/core/tellma-core/src/routes/tm-app.testing.util.ts:317-324`
   (`tmSettleUntil`) and `client/projects/core/tellma-core/src/api/tm-http-api.spec.ts:152-157`
   (`flushUntil`) loop until the condition holds, with no bound of their own. When a condition never
   holds (a regression, or state that arrives in a shape the condition does not match), the test
   fails with only "Test timed out in 5000ms": no word of which wait or which condition. Worse, the
   loop is never stopped. Checked with a throwaway two-test spec: a `tmSettleUntil(() => false)` test
   with a 300 ms timeout, then a test that waits 200 ms and calls `TestBed.configureTestingModule`.
   The condition was checked 41 more times during the second test, and its `configureTestingModule`
   threw "Cannot configure the test module when the test module has already been instantiated": the
   orphaned loop's `TestBed.tick()` had instantiated the next test's module. So one stuck wait turns
   into failures across the rest of its file, which hides the real one. In `tm-http-api.spec.ts`, the
   orphaned `flushUntil` keeps driving the next test's fake clock, or rejects once real timers are
   back. CONTRIBUTING.md now makes these helpers the standard wait, so the failure mode will spread.
   Fix: give both helpers a real deadline below the test timeout (say 4 s, measured with
   `performance.now()`, which neither the `tm-http-api` spec's `toFake` list nor real timers touch).
   Past it, throw an `Error` that names the wait (an optional description argument, or
   `String(condition)`) and the time waited. That ends the loop and reports the condition.

2. **R1-2 (major) Three specs still count turns across the shell chunk's first load.**
   `client/projects/core/tellma-core/src/launch/tm-launch-redirect.spec.ts:60` and `:71`, and
   `client/projects/core/tellma-core/src/routes/tm-route-host.spec.ts:228`. Each opens `/` with
   no cached memberships, so the redirect to `/17` (or `/tenants`) happens only after the memberships
   read. That is outside the awaited `navigateByUrl('/')`, and it loads the tenant shell (or the
   picker) through `import('@tellma/core/shell')` (`tm-route-hosts.ts:50`). One `tmSettle()` then
   reads `router.url`. With a 300 ms delay on that one import, all three fail (`expected '/' to be
   '/17'`, `'/tenants'`). In the default order, an earlier test in each file has already loaded the
   shell inside an awaited navigation, so they pass today. Under load they fail whenever they run
   first: a `-t` filter, a reordering, or a new test above them. That is the criterion the implementer
   applied ("whatever the order or filter"), and it is the same shape as the specs this commit fixed.
   Fix: `await tmSettleUntil(() => rig.router.url !== '/')` before each assertion. The assertion still
   names the wrong target if the redirect goes astray.

3. **R1-3 (minor) The real-confirmation spec counts turns across `@tellma/core-ui/confirm`.**
   `client/projects/core/tellma-core/src/context/tm-page-actions.spec.ts:504`: `ask()` invokes the
   action, runs one `tmSettle()`, then `loader.getHarness(TmConfirmHarness)`, which does not wait.
   The registry first loads the dialog with `await import('@tellma/core-ui/confirm')`
   (`tm-page-registry.ts:444`). The spec imports `TmConfirm` statically (line 11), so the module is
   already evaluated and only a cached `import()`'s resolution is at risk. The implementer's own
   reason for waiting on state rather than preloading names that as a hazard too. With the 300 ms
   delay, it fails ("Failed to find element matching one of the following queries"). Fix:
   `await tmSettleUntil(() => document.querySelector(TmConfirmHarness.hostSelector) !== null)` in
   `ask()`, before `getHarness`.

4. **R1-4 (minor) The CONTRIBUTING.md rule says more than the code base keeps.** `CONTRIBUTING.md:17-21`:
   "waits on the state it expects, never on a number of task turns". `tmSettle()` (a fixed count of
   turns) stays the core specs' ordinary wait, in dozens of places this commit rightly leaves alone,
   negative checks among them. "The test's own timeout bounds the wait" does not hold for the loop
   itself (R1-1). A reader cannot tell which turn-counted settles are now violations. Fix: scope the
   rule to work that may go through a lazily loaded chunk, for example "Across work that may load a
   chunk with `import()`, a client unit spec waits on the state the work produces, never on a number
   of task turns". Say that the helper's own deadline bounds the wait once R1-1 lands. Give the
   helper's full path (`client/projects/core/tellma-core/src/routes/tm-app.testing.util.ts`).

### Responses

Implementer, fix1 ("PC-F80: fix review 1").

- **R1-1: fixed.** `tm-app.testing.util.ts` gains `tmWaitUntil(condition, step, description?)`,
  which runs a step until the condition holds and throws past `TM_WAIT_DEADLINE_MS` (4 000 ms of
  real time, measured with `performance.now()`, which the HTTP spec's `toFake` list leaves alone).
  The error names the description, else the condition's source, and the time waited; the throw
  ends the loop, so nothing ticks into the next test. `tmSettleUntil(condition, description?)`
  and the HTTP spec's `flushUntil` are now that loop with a real task and render, or a zero
  advance of faked timers, as the step. The new `tm-app.testing.util.spec.ts` shows a wait that
  returns, one that fails past the deadline naming `state.ready` and is never checked again
  after the throw, and one under faked timers that keeps the real deadline without moving fake
  time. A wait started late in a test can still outlast the test's own 5 s timeout by up to its
  own deadline; it stops at the deadline, which bounds the spill.
- **R1-2: fixed.** `tm-launch-redirect.spec.ts` (the memberships read, and the unreadable
  memberships) waits until `router.url` is no longer `/`. In `tm-route-host.spec.ts` "times a
  launch at /", the URL alone was not enough: with the shell's chunk delayed, the URL moved before
  the home page painted and timed the launch, so that spec waits until a `tm.launch.*` record
  exists, then asserts the URL and the record.
- **R1-3: fixed.** `ask()` in `tm-page-actions.spec.ts` waits until
  `TmConfirmHarness.hostSelector` matches before `getHarness`; its now-unused `tmSettle` import
  is gone.
- **R1-4: fixed.** CONTRIBUTING.md scopes the rule to work that may load a chunk with
  `import()`, names both helpers with the full path
  `client/projects/core/tellma-core/src/routes/tm-app.testing.util.ts`, states the 4 s deadline
  below the 5 s test timeout and that a failed wait stops, and keeps `tmSettle()` for work that
  loads no chunk.

Verification (fix1): the 300 ms delay injected before every lazy `import()` the core specs reach
(the four chunks, the `uq` and `et` calendars, `@tellma/core-ui/confirm`, `tm-entity-paste`,
`@tellma/core/shell`, `/recents`, `/leader`, core-ui's `grid-editing` and `chart-engine`, the
blob fetcher's `@angular/common/http`): 1495/1495 (the first pass failed only the launch-timing
spec above, fixed before the second pass); reverted. `ng run core:test` in full: no load
1495/1495; under 20 busy-loop processes 1495/1495. eslint on the touched files, `tsc -p
projects/core/tellma-core/tsconfig.spec.json`, and `pnpm run lint:test`: clean (lint:test 480/480).
