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

## Review 1

Reviewed 2c91df43c9d9c1b06935da778842838b5891b9b5. Verified independently:

- Stability: the five spec files (grid-keyboard, modal, entity-picker, grid-entity, popover; all nine
  flaky tests and the five unquarantined ones), chromium, CI=1 prod build, `--workers 2
  --retries 0 --repeat-each 10`, Playwright pinned to 4 CPUs: **960/960 passed** (12.6 min).
- The new unit test `tm-grid.spec.ts` "focus that leaves before the next render …" fails on a
  HEAD^ copy (`expect(document.activeElement).toBe(outside)`, focus back on the cell) and passes
  at HEAD.
- (a) No assertion was loosened, no retry, fixed timeout or skipped step was added; every held-
  response conversion still asserts its in-flight state, now before release (stronger than the
  old delay race). `waitForTimeout(600)` was removed. (b) `HeldResponses`, the controls and the
  `tmShowcaseWhenStable` hook live only in the internal showcase; nothing reaches the library
  bundles. `releaseResponses` refuses to release an empty gate, `appStable` throws on a missing
  hook, and `animationsSettled` sees the panel's CSS enter animation (set on the panel from
  insertion), so none of the three passes vacuously. (e) The 5 B grid fix is product behaviour and
  belongs in the library; nothing test-only was added to the grid. (g) No PC ids, plans/ paths or
  spec citations in the new code, comments or test names.

### Findings

**R1-1 (blocking): the grid focus fix drops the deferred refocus after a pointer-driven menu
"Delete rows"; focus falls to `<body>`.**
`client/projects/core/tellma-core-ui/grid/internal/grid-core.ts:4024`.
`requestFocusActive()` now clears `pendingGestureFocus` whenever focus is inside the host after
the synchronous attempt. But that attempt runs before the render, against the old DOM. When the
render that follows destroys the node that holds focus, only the pending request recovered focus.
Example: the menu's Delete-rows action (`grid-core.ts:3936-3941`, whose comment says it exists
"so focus would otherwise fall out of the grid"). The sync `focus()` lands on the doomed row's
cell, the flag clears, and the menu's `restoreFocus` also targets a deleted cell. The render then
removes the rows and focus drops to `<body>`. The effect's `droppedByDomMove` reclaim cannot cover
this, because the pointerdown on the menu item (a CDK overlay, outside the host) set
`lastOutsidePointerDown` milliseconds earlier and the 200 ms guard vetoes the reclaim.
Measured on prod builds (grid-editable story: click row 1, Shift-click row 3, right-click row 2,
click "Delete 3 rows", read `document.activeElement` 500 ms later): HEAD^ left focus on a grid
cell 5/5 times; HEAD left it on `BODY` 5/5 times. The keyboard path (Shift+F10, Enter) still
reclaims, because no outside pointerdown precedes it. After a mouse delete, arrows and Ctrl+Z go to
the page instead of the grid, the regression that comment guards against. No test caught it:
`grid-menu.spec.ts:145` and `tm-grid-editing.spec.ts:759` never assert focus. The comment at
`:4021-4023` is also inaccurate: the check uses `host.contains`, but the effect's carry branch uses
`scroller.contains`, and the find bar and status bar are in the host but outside the scroller.
Suggested fix: restore the unconditional pending flag and cancel it only on a real departure. In
`onDocumentFocusIn` (`grid-core.ts:1283`), after computing `gridOwnsFocus`, add
`if (!this.gridOwnsFocus) this.pendingGestureFocus = false;`. A Tab or other move to an element
outside the host and its overlays fires `focusin` and cancels the request. A DOM-removal drop
fires none, so the pending refocus survives. This should cost about the same bytes as the current
line. Keep the new unit test, add a unit test for the pointer path of menu Delete rows (pointerdown
plus click on the panel item, then assert the surviving active cell is focused), and add a
`toBeFocused()` check on the surviving cell to the `grid-menu.spec.ts:145` e2e.

**R1-2 (major): the entity-picker "same-frame Enter" race is a reproducible product bug that
changes the committed value. Fix it, or track it as a product defect, not as an open risk.**
`client/projects/core/tellma-core-ui/entity-picker/tm-entity-picker.ts:1804-1818` (the Enter
"no highlight" test), with `queueSearch` → `clearHighlight()` (`:1317`) and the after-render
highlight effect (`:1025-1044`).
Reproduced on the prod build (grid-entity story, cell 2,1, press `A`, let it settle). Then the
next keystroke and Enter were sent back-to-back over CDP, as a scanner, text expander, remote-
desktop client or busy main thread delivers them. `l`+Enter committed the raw text "Al" as an
errored cell and moved down (3/3). The same keys with a render between them picked the
highlighted "Alice Green" and stayed (3/3). `ice`+Enter after "Al" gives the right value but moves
down instead of staying (6/6, idle or with a 300 ms main-thread block). So batched input commits
different data than paced input. In an ERP, a barcode scanner (characters a few ms apart, then
Enter) is a normal input device, so "users cannot type that fast" does not bound the risk. The
form (non-cell) path drops the Enter instead.
Suggested fix: record the `seq` that the highlight effect last applied. In the capture keydown
Enter path (and the cell Tab path), when `highlightRequest()` is newer than that `seq`, has
`query !== ''` and `count > 0`, commit `settledResult.items[0]` through the normal pick path
instead of treating the key as "no highlight". Add a unit test that dispatches `input` and Enter
in the same task with no render between them. If it is not fixed in PC-F02, it needs a tracked
PC-Fnn or LEDGER entry (see decisions).

**R1-3 (minor): `appStable` has no bound of its own, so a page that never stabilises fails as an
anonymous 30 s test timeout.**
`client/e2e/support/settle.ts:33-45`. `ApplicationRef.whenStable()` waits on every pending
task, including `resource()`, HttpClient and router navigation. `modelJson` (used by every grid
spec), `searchCalls` and `model` now call it on every read. If a story ever keeps a task pending,
for example a held or slow loader that registers one, every oracle read hangs until the test
times out, and the report never names the hook. Suggested fix: race the hook against a timeout
(for example 10 s) inside the `evaluate` and throw "the showcase app did not become stable within
10 s (a pending task or a render loop)" so the cause is named.

**R1-4 (minor): `CLOCK_PAUSE` has no TSDoc of its own.**
`client/e2e/specs/entity-picker.spec.ts:93-94`. One comment covers both constants, but AGENTS.md
requires TSDoc on every declaration. Suggested fix: give each constant its own one-line TSDoc
(start instant; pause instant, jumped to after setup).

### Responses

R1-1: fixed in 4c36d45. I took the reviewer's direction. `requestFocusActive()` is back to its
original form, with the pending flag set unconditionally. A real departure now cancels the pending
request in two ways. (1) `onDocumentFocusIn` cancels it when focus lands outside the host and its
overlays (`pendingGestureFocus &&= gridOwnsFocus`). A drop to `<body>` when the render removes the
focused row fires no focusin, so the refocus after a menu Delete rows survives. (2) A Tab keydown
reaching the grid's keydown handler cancels it. The focusin alone does not cover the Tab tests:
the grid is the last focusable element in those stories, and Tab past it leaves the document with
no focusin at all (probed: `activeElement` is BODY, `hasFocus()` is false, the only event is a
focusout with a null `relatedTarget`). A Tab that stays in the grid requests focus again. To fit
the 38 KB ceiling, the roving-focus effect now consumes the flag once at the top instead of
clearing it in three branches; behaviour is the same. Grid: 38912 B, which is 38.00 of 38 KB.
The inaccurate comment is gone along with the code it described. Regression tests: the new unit
test `tm-grid-editing.spec.ts` "a pointer Delete rows from the menu leaves focus on the surviving
active cell" dispatches pointerdown on the menu item, then a click. It fails on 2c91df4 (focus is
`<body>`) and passes now. `grid-menu.spec.ts` "Delete rows removes the selected rows …" now ends
with `expect(activeCell(page)).toBeFocused()`. The earlier unit test (focus that leaves before
the next render) still passes.

R1-2: pushed back: tracked as a separate fix item (orchestrator ruling). The e2e tests no longer
depend on the race. Every test that types and then presses a key acting on the highlight first
waits for the render: `typeInEditor()` (appStable) in grid-entity, or a web-first wait for the
fresh highlight or status in entity-picker, where the panel was closed before the `fill()`, so no
stale highlight can satisfy it. This round adds the two sites still missing that wait: entity-picker
"clearing the text stops the field claiming a selection…" (its setup did `fill` + Enter) and
grid-entity "a no-resolver entity column records the invalid input directly".

R1-3: fixed in 4c36d45. `appStable` races the hook against `APP_STABLE_TIMEOUT_MS` (10 s) inside
the page and then throws "The showcase app did not become stable within 10000 ms (a pending task
that never ends, or a render loop)." The timer is cleared on success.

R1-4: fixed in 4c36d45. `CLOCK_START` and `CLOCK_PAUSE` each have their own TSDoc.

Round-1 verification (local, CI=1 prod build, `--retries 0`, 2 workers, 4 CPUs pinned): all
client gates pass (lint, typecheck, lint:test, tokens:check, build, test with 1149 core-ui
tests, api:check, docs:build, size:check with grid at 38.00 of 38). The PR tier (chromium +
touch) passed 440/440. The five affected spec files (all nine formerly flaky tests) plus
grid-menu, at repeat-each 10, passed 1060/1060.
