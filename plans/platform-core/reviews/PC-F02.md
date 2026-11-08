# PC-F02: systemic e2e flakiness on CI

## Implementer notes

### Evidence

- CI runs 42451990728 (8a1ebf8) and 42456030333 (5250c6c) both ran the PR tier with
  "using 2 workers" on `ubuntu-latest` (the repo is public, so 4 vCPUs). Each first attempt that
  failed passed `retry #1`. The second run changed only plans/ files, so none of the nine flakes is
  a regression.
- Every one of the nine is the same kind of failure: **the test read or acted on a state the page
  had not reached yet**. The test was quicker than the page's next render, effect or timer. A
  starved runner widens that gap from about a frame to hundreds of milliseconds. Neither
  parallelism nor shared state between tests was involved. Each test has its own page, and the
  focus checks do not depend on `document.hasFocus()` across pages.
- The worker count fits the runner. Playwright's default (half the cores) gives 2 workers on 4
  vCPUs. Lowering it would only shrink the timing gaps, not close them, so the config is unchanged.
  `retries: 1` and `failOnFlakyTests` are also unchanged.

### Causes and fixes, per test

1. **grid-keyboard "a readonly grid is a single tab stop" and ":258 link column"**. This was a
   product bug. `requestFocusActive()` focuses the active cell at once but left
   `pendingGestureFocus` set. The roving-focus `afterRenderEffect` then re-focused the cell on the
   next render, even after the user had tabbed out. A Tab pressed between a click and that render
   left the grid and was pulled straight back. Fix (`grid-core.ts`): the request stays pending only
   while focus is not inside the grid after the synchronous attempt. Focus that stays in the grid
   is still carried by the effect's "focus is in the scroller" branch, and DOM-move drops by its
   reclaim. New unit test `tm-grid.spec.ts` "focus that leaves before the next render is not
   pulled back into the grid": it fails without the fix (focus lands back on the cell) and passes
   with it. The three Tab-out e2e tests now read focus through `focusInsideGrid()`, which first
   waits for `appStable` and so samples after any pending render (a one-shot read straight after
   the Tab could pass before the pull-back). The link-column test also waits for the cell's focus
   before pressing Tab.
2. **modal "focus is trapped … restored to the opener"**. The CDK dialog moves initial focus on
   the render after it attaches, which can be after the shell is visible. The one-shot
   `page.evaluate` became `expect.poll`. The Tab-cycle checks stay one-shot, because the trap
   redirects focus within the key's own task.
3. **entity-picker "modal round-trips › the create page …"**. The picker writes the label into the
   input at once, and the model dump renders on the next pass, so a one-shot `model()` read saw
   `null`. The spec's `model()` and `searchCalls()` helpers, and the shared grid `modelJson()`
   oracle (`support/grid.ts`, used by every grid spec), now call `appStable(page)` first.
4. **entity-picker "modal round-trips › the edit page …"**. The click opens the browse list, whose
   rows (and the listbox the ArrowUp handler needs) mount a render later, so the ArrowUp was
   dropped. The test now waits for the `Edit…` row before each ArrowUp. The same race in
   grid-entity "a mid-edit modal dismissal …" (Alt+ArrowDown, then ArrowUp) gets the same wait.
5. **grid-entity ":58 type-to-edit … Enter … NO move"** (and the Tab variant). The search is
   synchronous, but each keystroke clears the highlight at once and re-applies it in an
   after-render effect. 'Alic' and 'Alice' highlight the same row, so `toHaveText('Alice Green')`
   passed on stale DOM. Enter then met no highlight, took the typed-commit path and moved down.
   The new `typeInEditor()` types and then waits for `appStable`.
6. **grid-entity "unresolved commit text … AFTER editor teardown"** (5 searches, not 4). It is the
   same stale-DOM read: the snapshot read the `search-calls` text before the last keystroke's
   render. `searchCalls()` now waits for `appStable`. Its resolver spinner window (600 ms) is held
   open too (item 7).
7. **entity-picker ":83 … async shows the immediate spinner"**. The spinner lived only for the
   400 ms search delay, and the first assertion poll came after it ended. This is the
   transient-window class. The baseline stress runs below also caught it in grid-entity ":273" and
   ":166" (700 ms windows). Fix: a held-response gate in the showcase
   (`app/held-responses.ts`: `HeldResponses` plus `<app-held-responses>` toolbar controls). While
   it holds, the story's demo search or resolver waits for an explicit release instead of a delay.
   The tests use it through `support/held-responses.ts`. The release is a custom `release` DOM
   event, because any click, even a synthetic one, is an outside press to an open CDK overlay and
   closes the dropdown. Converted: entity-picker :83, the blur-resolution "lands after the
   departure", the two panel-placement "while loading" measurements, the size-stability loading
   and resolving states, and the axe loading state; grid-entity "unresolved commit …", "a commit
   while the search is in flight …" and "a cell awaiting resolution …".
8. **popover "block-end/start alignment mirrors under RTL"** (0.11 px short). The panel has a
   `translateY(-4px)` enter animation and was measured mid-slide. The new
   `animationsSettled(locator)` (`support/settle.ts`) waits for every finite animation on the
   panel and is now called before each geometry read in the positioning describe. I did not turn
   motion off globally (`reducedMotion: 'reduce'`): the default motion path is what users see, and
   the reduced-motion gates already cover the other one.
9. Two timing claims, entity-picker "a held key costs ONE trailing request" and "normal typing pays
   no added latency", rested on real-time gaps between key presses. One of them failed once
   locally (4 calls instead of 2), and the first also used `waitForTimeout(600)`. Both now run on
   Playwright's fake clock (`pauseClockAfter()` / `typeOnClock()`): page time advances only when
   the test says so, so a stalled runner cannot stretch a 33 ms repeat past the 300 ms window. This
   follows ARCHITECTURE.md's "wall-clock waits are driven by fake clocks".

The supporting change is `appStable(page)`, which awaits `window.tmShowcaseWhenStable`. That hook
is installed in the showcase's `main.ts` and is `ApplicationRef.whenStable()`: no scheduled change
detection and no pending task. It throws if the hook is missing rather than skipping the wait.
CONTRIBUTING.md "Flaky tests" now lists these patterns.

### Quarantine

All five PC-F01 tests are fixed, and `@quarantine` and its comment are removed from each. No
quarantined test remains. The nightly quarantine job selects nothing, which `--pass-with-no-tests`
already allows.

### Stability numbers (local, Windows, CI=1 prod build, `--retries 0`, 2 workers)

- Baseline, original code, the five spec files, 4 CPUs pinned (`start /affinity F`), repeat-each
  10: 959/960 passed (grid-entity ":273" resolver-spinner window failed once).
- Baseline, original code, the same five files with every page CPU-throttled 4x over CDP
  (temporary harness, not committed), repeat-each 5: 471/480. That is 1 real flake (grid-entity
  ":166", the in-flight spinner window) and 8 test timeouts in the axe "popup states" test, which
  runs five axe scans and cannot finish in 30 s at 4x.
- Fixed code, the same throttled run: 470/480. All 10 failures are that axe test's 30 s timeout
  under 4x throttling (it passes unthrottled). There were no other failures.
- Fixed code, the five files including the five formerly quarantined tests, 4 CPUs pinned, 2
  workers, repeat-each 20: **1920/1920 passed** (37.2 min). This run built the first version of
  the grid fix, which is logically the same as the final one: it cleared the flag when
  `document.activeElement` was the focused cell. The final version, which tests
  `host.contains(activeElement)`, was smaller for the bundle budget.
- Fixed code, final, full PR tier (chromium + touch, `--grep-invert "@nightly|@perf|@quarantine"`),
  4 CPUs pinned, 2 workers, repeat-each 2: **880/880 passed** (440 tests, which is the 435
  before plus the 5 unquarantined, run twice; 14.0 min).
- The local races reproduced only rarely, even under pinning and throttling. The CI failures
  depend on render/input ordering on the Linux runner. So the evidence is per cause: the grid bug
  has a deterministic unit regression test, and every other fix replaces a race with a wait on a
  condition (render settled, rows present, animation finished, response held).

### Gates

lint, typecheck, tokens:check, build, test (all unit suites), api:check, docs:build, size:check:
pass. lint:test passed on a rerun. The first run timed out in two tools tests (vitest import took
173 s) while the stress run loaded the machine.

### Deviations

- none

### Open risks

- **The grid bundle is now exactly at its 38 KB ceiling** (38912 B gzipped). Before this change
  it was 38907 B, so the fix costs 5 B. I chose the smallest correct form; the first form was
  38920 B. The next grid change will need a budget decision. The package.json rationale already
  says the ceiling is "worth a dedicated pass rather than another increment".
- The entity picker has a real, narrow product race. Enter (or Tab) pressed in the same frame as
  the last keystroke meets a cleared highlight, because the sync search clears it at once and
  re-applies it after render. In a cell, that commits through the typed-commit resolver and moves
  instead of picking and staying. Users cannot type that fast, but a barcode scanner can. The
  tests now wait for the render. A product fix would let Enter apply a pending "highlight the
  first result" request. That is a change to the highlight protocol and is not done here.
- Other specs still assert in-flight states bounded by a wall-clock delay. None has flaked yet:
  tree-grid lazy loading (600-800 ms), file-preview slow loader, grid-paste-resolution
  (1500 ms windows). The held-response gate is the pattern to move them to if they ever flake.

### What the reviewer should examine hardest

- `grid-core.ts` `requestFocusActive()`: that clearing the pending flag once focus is inside the
  grid loses no case the deferred effect used to cover. Look at off-window moves, editor close
  (`closeEditor` focuses first, then requests), menu actions, and DOM-move reclaim.
- That `appStable` (`ApplicationRef.whenStable`) cannot hang. No showcase story under these specs
  holds a pending task. Only input-story uses `resource()`, and input.spec does not call it.
- That each held-response conversion still asserts what the test name claims, especially the
  blur-resolution "lands after the departure" and the size-stability sequence.
