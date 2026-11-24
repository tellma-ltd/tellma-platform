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
