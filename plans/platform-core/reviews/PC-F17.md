# PC-F17: grid scroll budget: no long task over 50 ms on the flat grid and the 21,000-row tree

## Implementer notes

### What was built

Profiling first: Chromium traces and CPU profiles of the probe's 80 wheel steps on `grid-readonly`
(100,000 rows, twelve columns) and `tree-grid-large` (21,000 rows, expanded), scratch harness under
`.tmp/f17/` (not committed). Each screen-sized wheel step brings a whole window of new rows. The
grid tracked its rows by row key, so every step destroyed about 14 row views and created 14 new
ones, each with its 12 cell views, display-template views and row-end view. Angular view creation
and destruction (template creation, view queries re-traversing, listeners, attribute writes) was
about half of each long task; the rest was native work on the fresh nodes (layout-tree rebuild,
style, layout, paint). JS self time inside long tasks over one probe run: base 1137 ms, row reuse
630 ms. The tree differs only in the hierarchy cell; `tm-icon` was not a dominant cost once rows
stopped being re-created.

Three changes, each measured below, the first two in the pattern the tiles took for the same cost:

- **Rows reused by slot** (`grid/internal/row-slots.ts`, `grid/internal/grid-core.ts`,
  `grid/internal/grid-view.ts`).
  - `ɵtmGridAssignRowSlots(keys, previous, retired)` keeps each staying row's slot and gives the
    rows that enter the slots of those that left, the last to leave first. That order is the one
    Angular's `@for` reconciler applies by moving only the re-bound elements, in either direction
    (a unit test counts row-element moves scrolling down and back up).
  - Every row view model carries `slot`; the window's `@for` tracks `row.slot`. `rowKey` stays the
    row's identity (`data-tm-key`, flash, find, and the keys below).
  - The last render's active row never gives its slot to an entering row (the `retired` argument),
    so focus never stays on an element re-bound in place to another row.
  - Everything in a row and its cells is a binding (ids, `aria-rowindex`, `aria-level`,
    `aria-expanded`, classes, text, indent, expander, row header), so a re-bound row re-renders its
    row. The unit tests check every attribute of every rendered row through sweeps and jumps, on the
    flat grid and on a 900-node tree of chains.
- **Consumer views stay with their row** (`grid-view.ts`, `grid/internal/grid-end-cell.ts`). A
  `*tmGridDisplay` view and the `tmGridRowEnd` view are wrapped in a one-entry
  `@for (key of [row.rowKey]; track key)`. A re-bound row element therefore gets fresh consumer
  views; a staying row keeps its views. Consumer views can hold state, and `NgTemplateOutlet` swaps
  its context in place, so without the key one row's view would be handed to another row.
- **Fixed-length window and sliced scroll frames** (`grid-core.ts`).
  - The window always holds as many rows as the viewport can show, plus one for the part-rows at
    its edges and the overscan on both sides, whatever the offset's alignment. A row count that
    changes from one scroll to the next would create or destroy a row element where a re-bind does.
  - `onScroll` records the offset the first scroll of a task started from (`sliceFrom`). It is held
    under `PendingTasks` and cleared by a `setTimeout`. In that task the window moves from where it
    was only as far as the visible rows need, keeping its length. The scroll's own frame therefore
    re-binds only the rows now visible, and the overscan follows in the next task. A programmatic
    scroll sets the offset signal before its event arrives, so it slices nothing.
- **Probes** (`e2e/specs/grid-scroll-budget.spec.ts`, `e2e/support/scroll-probe.ts`).
  - The tree probe moves out of `grid-glyphs.spec.ts` into its own spec, beside a new flat-grid
    probe (100,000 rows, twelve columns). Both are `@perf`, Chromium-only, 40 screens each way, and
    assert no long task over 50 ms. `@quarantine` is removed.
  - `wheelScreensForLongTasks` is the one probe loop that the grid probes and the tiles probe
    (`tiles.spec.ts`) now share.
- **Size**: `./grid` raised 40.73 -> 41.23 (see Deviations).
- **ARCHITECTURE.md**: one sentence in the core-ui paragraph. The grid windows its rows with
  re-bound row slots, and consumer views are keyed by row id.

### Measurements (interleaved, same host)

These are the scratch probe's numbers, the e2e probe loop run against three production showcase
builds with source maps. The builds ran interleaved, run by run, so host load hits each alike:
base (HEAD before this change), v1 (row reuse and keyed consumer views only), and v2 (v1 plus the
fixed-length window and sliced frames, the shipped code). "Over 50" is the PerformanceObserver
long tasks over 50 ms, which the probe asserts on. "Task max" is the longest main-thread task in
the trace.

`grid-readonly`, 4 interleaved runs:

| build | over 50 per run | median over 50 (ms) | max long task (ms) | task max (ms) |
|---|---|---|---|---|
| base | 2, 15, 78, 76 | 56, 55, 67, 61 | 56, 62, 89, 125 | 57, 62, 90, 125 |
| v1 | 0, 3, 0, 3 | -, 52, -, 70 | -, 54, -, 84 | 44, 54, 49, 85 |
| v2 | 0, 0, 0, 0 | - | - | 41, 47, 47, 45 |

`tree-grid-large`, the same 4 runs:

| build | over 50 per run | task max (ms) |
|---|---|---|
| base | 0, 0, 0, 0 | 39, 48, 45, 47 |
| v1 | 0, 0, 0, 0 | 36, 39, 41, 31 |
| v2 | 0, 0, 0, 0 | 27, 30, 46, 29 |

A second interleaved set ran later, on the final code's production bundle (`final`, the e2e
build) against base, with the host more loaded than before. "Task CPU max" is the longest task's
thread time.

| story | build | over 50 per run | median over 50 (ms) | task max wall (ms) | task CPU max (ms) |
|---|---|---|---|---|---|
| grid-readonly | base | 44, 17, 28 | 57, 55, 55 | 118, 93, 108 | 65, 57, 67 |
| grid-readonly | final | 6, 20, 5 | 92, 65, 76 | 114, 191, 119 | 43, 45, 118 |
| tree-grid-large | base | 2, 3, 0 | 65, 62, - | 66, 72, 45 | 44, 44, 44 |
| tree-grid-large | final | 0, 0, 0 | - | 39, 45, 31 | 25, 30, 24 |

- Under that load the final flat grid still had long tasks, but they are mostly wall-time
  stalls, not work. Their median is 65-92 ms wall time, while the longest task's thread time is
  43-45 ms in two of the three runs, so the thread was descheduled. One task in the third run
  took 118 ms of thread time. Base meanwhile had 17-44 tasks of real work over 50 ms.
- A trace of the final flat grid breaks a typical step's task (25-45 ms) down as script about
  7-10 ms, paint 4-8, layout 2-6, pre-paint and compositor update about 2-3 each, style and text
  shaping about 1-2 each. The script share is no longer dominant.
- The same load failed the unchanged tiles probe too. The e2e `@perf` run here (Chromium, one
  worker, each probe three times with one retry) passed the tree 3 of 3. The flat grid passed 1
  of 3 outright and 1 on retry (its first attempt had 5 tasks of 54-211 ms). It failed 1: 3
  tasks of 52-86 ms, then 1 of 163 ms on retry. Tiles, whose code this
  change does not touch (only its probe loop moved into the shared helper), passed 0 of 3, with
  up to 30 tasks over 50 ms.
- Locally the tree already passes at base. When it was quarantined, the fix item (from the PC-045 review) recorded
  2-18 long tasks of 52-132 ms for it, and 46-48 of up to 94 ms for the flat grid. Since then
  the tree passed 30 of 30 in the nightly quarantine job's runs 44892028327, 44847843416 and
  44745573605, so the flat grid was the failing case. The flat probe is new.
- v1 alone left 0-3 long tasks per flat run under load. The sliced frames halve each frame's work:
  v2's p50/p97 main-thread task is about 26-35 / 38-45 ms, against v1's 28-41 / 40-52 and base's
  26-67 / 50-84.
- Total main-thread CPU across the probe went up with v2 (cpuSum of tasks of 4 ms or more, flat:
  base 2484-5106, v1 2211-3247, v2 2949-5423). The overscan's own task re-renders the whole row set
  a second time, so a step is two tasks of about half the work each. The budget is per task, and no
  task is longer, but the total is not smaller.

### Deviations

1. **Rows are tracked by slot, not by `rowId(row)`.** Spec 0004 §4 says the window's `@for`
   tracks by `rowId(row)` "so scrolling reuses row views". It does not: tracking by row id destroys
   the views of the rows that leave and creates those that enter, and that is the cost this fix
   removes. Rows are tracked by slot, and only the consumer's views are keyed by row id.
2. **Fixed-length window and sliced scroll frames.** Neither is in the spec. They are what takes
   the flat grid from 0-3 long tasks per run to none (table above).
3. **A new flat-grid probe.** The quarantined probe covered only the tree. The flat 100,000-row
   grid is the case that failed, so it gets its own probe. Both probes now live in
   `grid-scroll-budget.spec.ts` instead of `grid-glyphs.spec.ts`.
4. **Shared e2e helper.** `e2e/support/scroll-probe.ts` replaces the copied probe loop. The tiles
   probe uses it unchanged in behaviour.
5. **Bundle ceiling `./grid` 40.73 -> 41.23** under the raise rule. Linked measure, clean library
   builds of HEAD and of this change: 40.71 before (41,690 B) and 41.14 after (42,129 B; +439 B).
   The raise is what the change adds rounded up to 0.5 KB, with 90 B of headroom and 879 B left
   under the 42 KB hard cap. Savings taken: -25 B (the window's end is its start plus its length;
   one pass collects the rendered rows' keys). This is the first raise since the re-split lowered
   the ceiling. The budgetNotes entry gives the breakdown. The review 1 fixes add 136 B (the
   checkbox press guard and the window's equality test), so the ceiling is now 41.37: 41.27
   measured (42,265 B; +575 B over the parent), within +575 B rounded up to 1 KB, with 97 B of
   headroom and 743 B under the hard cap.
6. **The probes measure unthrottled** (added with the review 1 fixes). 0004 §16 sets the 50 ms
   scroll budget "measured in CI (Playwright traces on a mid-tier mobile CPU throttle profile)".
   The grid probes, like the tree and tiles probes before them, apply no CPU throttle. This
   checkpoint covers §16 unthrottled. On this host the flat grid's longest task takes 43-45 ms of
   thread time, so it would not meet 50 ms under a 4x throttle. Whether §16 keeps the throttle is
   for the orchestrator to decide.

### Open risks

- **CI is the arbiter, and the margin is thin.** The scratch probe met the budget in 8 of 8 v2
  runs, with a worst task of 47 ms. On the later, more loaded host, the e2e flat probe failed 1 of
  3 (above), from descheduling stalls rather than work. The probes are unquarantined because CI
  should pass. The final flat grid's longest task takes 43-45 ms of thread time on this host. That
  is the same as the tree at base (44 ms), which passed 30 of 30 in CI's quarantine job, and below
  the flat grid at base (57-67 ms). A red nightly would mean the runners have less headroom than
  that comparison suggests. The next cut would be the native work of each frame (paint 4-8 ms,
  layout 2-6, pre-paint and compositor update 2-3 each).
- **Press then release on a re-bound row** (the analog of the tiles' press/release issue). Take a
  pointer press on a row element that a scroll (a wheel during the press) re-binds before the
  release. The `click` now fires on that element, which shows another row. Before this change the
  element was destroyed, and the click went to the common ancestor instead. Most press paths
  resolve their target at the press: row activation captures the row id, and the press drag
  captures the pointer on the scroller. `onClick`'s own branches (the expander, the row checkbox,
  a touch tap) read `data-row` at click time, so they act on the row the element now shows. No test
  covers a scroll between a press and its release.
- **The active-row retire is defensive for the grid.** The grid renders a newly active row before it
  reveals it, so the old and new active rows never swap in one render through the keyboard or
  `scrollToRow`. The focus-on-jump unit test passes with or without the retire; the slot unit tests
  pin the retire itself. It still matters when a data change swaps the row at the active index in
  one render.
- **A second render per scroll step.** The overscan's task re-renders every row. Its cost is in the
  table (it is the cpuSum increase), and it stays well under budget on the flat grid and the tree.

### What reviewers should examine hardest

- The window arithmetic in `grid-core.ts` (`this.window`). It must still cover every visible row
  in a sliced frame, at both ends of the data, with a short or empty view, and when the row count
  changes while a slice is open.
- `onScroll`'s slice: one pending task per task, cleared by its timeout. Check that
  `whenStable` and the restore and reveal paths that set the scroll offset are not affected.
- Focus across re-binds: the active row's element is kept while it is rendered (outlier or not),
  and the keyed consumer views. Check that a re-bound row never shows a stale view, flash, find
  highlight or error state (`tm-grid-reuse.spec.ts`, and the tree-chains test in
  `tm-tree-grid.spec.ts`).
- The size measurement: re-measure HEAD and this change with the linked measure.

## Review 1

Scope: commit 73069c02 (the grid's row slots, the fixed-length window and sliced scroll frames, the
keyed consumer views, the moved and new `@perf` probes with their shared loop, the `./grid` raise,
the ARCHITECTURE.md sentence). Judged against spec 0004 §4, §5.2, §8.3, §8.8 and §16, 0010 §C.13,
the datagrid's existing behaviour, and the tiles' line slots, which solved the same problem first.

What I checked and found sound:

- **Per-row state across a re-bind.** Every attribute, class and text on the row, its cells, the
  row header, the checkbox cell and the row-end cell is a binding off the row view model, so a
  re-bound element re-renders all of it (ids, `aria-rowindex`/`level`/`expanded`/`posinset`/
  `setsize`/`selected`, `aria-invalid`/`readonly`/`describedby`, tabindex, active, selected, cut,
  find, flash, error, pending, outlier transform). The only imperative DOM write in the grid's rows
  is the per-render tabindex pass over projected interactive content, which re-runs on every render.
  The grid chrome has no CSS transitions, and a re-bound element gains the flash class the way a
  new one did. The editor, the draft input, the error popover and the popup pane all live on the
  active row, which keeps its slot while it is rendered, and the active row is always rendered.
  Signal Forms state is read into the view model; no field directive sits on a row element. Tree
  state (expanded glyph, indent, child spinner) is bound through the affordance template's context.
  The consumer's `*tmGridDisplay` and row-end views are keyed by row id (one-entry `@for`), so they
  never move to another row, and the tiles take the same approach.
- **Window arithmetic.** Unsliced, `start + size` covers `tmComputeAxisWindow`'s own end, because
  its visible span is at most `ceil(viewport / row) + 1` rows. Sliced, `start <= first` and
  `start >= last - size`, so every visible row is in the window, at either end of the data and for
  an empty view. The fixed length breaks only at the end of the data (`end` clamps), which costs
  element churn there and nothing else.
- **The slice's ordering in production.** Angular's listener marks the view dirty before
  `onScroll` runs, so the change-detection callback is queued ahead of the slice's `setTimeout`.
  The sliced frame therefore renders first and the overscan follows, under zoneless and zone.js
  alike. Programmatic scrolls (reveal, find, restore, drag auto-scroll) set `scrollTop` before
  their event arrives, so they never slice.
- **Probes.** The shared loop is the old loop verbatim: 40 screen-sized wheel steps each way, one
  animation frame between steps, `appStable`, and a 50 ms filter. The tiles probe calls it with the
  same locator and step count as before. The touch project's `testMatch` keeps the probes out of
  it, and the PR tier's `--grep-invert` keeps `@perf` out of PR runs.
- **ARCHITECTURE.md** matches the code: slots that a scroll re-binds, and consumer views keyed by
  row id. The rule this deviates from is 0004 §4's "`@for` over the window tracks by `rowId(row)`
  (§5.2)", not a §5 sentence. The deviation text cites §5, but its substance is right.
- **Tests run here.** core-ui unit specs `grid/**`, `tree-grid/**` and `tiles/**`: 630 passed.
  The Chromium and touch PR-tier e2e for `grid-*`, `tree-grid*` and `tiles*` in CI mode: 412
  passed. After a clean `ng build core-ui` at this commit, `size:check` reports `./grid` at 41.14
  against 41.23, the implementer's figure. The +439 B rounds up to the 0.5 KB that D-031 allows,
  and the budgetNotes entry names the feature, the bytes and the savings. I did not re-measure
  the parent commit. No CI run exists for pc/PC-F17: the push run 44986364564 was
  skipped. I did not run the `@perf` probes.

### Findings

**R1-1 (major): a checkbox click after a scroll during the press toggles another row's selection.**
`client/projects/core/tellma-core-ui/grid/internal/grid-core.ts:3084-3091` (the press on
`[data-tm-checkcell]` returns without activating the row or capturing the pointer) and `:3333-3338`
(`onClick` reads `data-row` from the checkbox cell at click time). Unlike every other press in the
body, a press on the checkbox does not make its row active, so its row element is not protected
from re-binding. Take a mouse press on row R's checkbox. Wheel the grid while holding the button,
then release without moving the mouse. If the slot cycle has put R's old element back under the
pointer, bound to row R + k, the press and the release share a target. The browser then fires
`click` on it, and `toggleRowCheckAt` toggles R + k. With Shift held, it checks the range from the
anchor to R + k. Before this change R's element was destroyed, the click went to the common
ancestor, and nothing toggled.

This is reachable. I replayed `ɵtmGridAssignRowSlots` with the shipped window arithmetic (sliced
then settled frame per step, a 288 px body, every pointer row, 1-60 wheel steps). The release
lands on the pressed element, now another row, in 155 of 2,160 cases with 100 px notches (about
7%). With screen-sized steps it is 600 of 2,160 (about 28%). For example, a press 128 px into the
body lands on the element again after 10, 27, 32 and 42 notches. `selectedIds` is what bulk
actions read (0004 §8.8), so an unseen extra check can reach a bulk delete. The tiles hit the same
case and guard it: `tm-tiles.ts:1009-1013`, with `pressedElsewhere` at `:1154`, ignores a click
whose row differs from the press's. The grid's port of the slot pattern left that guard out. The
expander is not affected: its press activates the row (`:3141-3146`), and the active row keeps its
element.

Fix: on a checkbox-cell press, record the pressed row's key (`closest('[data-tm-key]')`). In
`onClick`'s check-cell branch, ignore the click when the cell's row key differs, as the tiles do
(skip the check for `event.detail === 0` keyboard or synthetic clicks). Add a unit test. Dispatch
`pointerdown` on row r's `.tm-grid__check`, scroll so that element re-binds to another row, then
dispatch `click` on the same element. Assert that `selectedIds` is unchanged.

**R1-2 (minor): every scroll renders the whole row set twice, even when the window does not move.**
`grid-core.ts:1468-1499` builds `window` as a plain `computed` that returns a fresh object on
every evaluation. `onScroll`'s timeout (`:2373`, `sliceFrom.set(null)`) re-evaluates it in the next
task. `renderRows` (`:1658`) then runs `buildRenderRows` again for every row and cell, and the
template checks every binding again. This happens when the sliced window already equals the
settled one, and for every scroll smaller than the overscan, whose sliced frame keeps the old
window. The review file's own cpuSum figures show it: v2 2,949-5,423 against v1 2,211-3,247. The
budget is per task, so this is not a breach. It is avoidable main-thread work on every scroll
step, and it is the first thing a slower device pays for. Fix: give the `window` computed an
`equal` that compares `start`, `end`, `leadOffset` and `totalSize`. Then a scroll that needs no
new rows re-renders nothing in its own task, and an overscan task with nothing to add is free.
Re-measure cpuSum after the change.

**R1-3 (minor): no grid-level test pins the active-row slot retire.**
`grid-core.ts:4447-4449` passes the last render's active key to `ɵtmGridAssignRowSlots`. Only
`row-slots.spec.ts` pins the function's contract. The review file says the focus-on-jump test
in `tm-grid-reuse.spec.ts` passes with or without the retire. So if the core passed the current
key or `null`, no grid test would fail. One realistic case needs the retire: the consumer
replaces the active row's temporary id with its saved id. Only that key leaves, and only its
replacement enters. Without the retire, the replacement takes the focused element in place, with
no focus change for assistive technology to report. That differs from the grid's behaviour before
this change, and from what the retire's TSDoc promises. Fix: add a `tm-grid-reuse.spec.ts` case.
Focus a cell of row r, replace row r's object with one of a new id in `rows`, and settle. Assert
that the old cell element is detached, that `document.activeElement` is the new row's cell, and
that a `focusin` reached it. Check that the case fails with the retire argument forced to `null`.

**R1-4 (minor): the commit claims 0004 §16 coverage, but its new probe measures unthrottled.**
`client/e2e/specs/grid-scroll-budget.spec.ts:22-31` and `client/e2e/support/scroll-probe.ts:30-58`.
0004 §16 sets the 50 ms scroll budget "measured in CI (Playwright traces on a mid-tier mobile
CPU throttle profile)". The probes apply no CPU throttle. The review file's thread-time figures
(43-45 ms for the longest flat-grid task on this host) say the flat grid would not meet 50 ms
under a 4x throttle. The tree and tiles probes had the same gap before this change, but this
commit adds the flat probe and claims §16 in its "Spec coverage" line. The gap is not recorded.
Fix: list the unthrottled measurement under Deviations as a spec misalignment for the
orchestrator. Do not add a throttle in this item.

### Responses

R1-1: fixed in the "PC-F17: review 1 fixes" commit (child of 73069c02). A press on a row checkbox records its row's key (`checkPressKey`). The
click consumes it and toggles nothing when the checkbox's row key differs, which covers the Shift
range too, since it is the same branch. A click with no press of its own (`detail` 0) is always
its row's, as in the tiles (`pressedElsewhere`). The new unit test
`ignores a checkbox click whose press was on the row its element showed before a scroll` presses
the first row's checkbox, scrolls three rows so that element re-binds to another row (asserted),
releases plainly and then with Shift, and asserts the selection is still empty. It also checks
that a press and click on one row still toggles it. With the guard removed, the test fails
(`expected [ 36 ] to deeply equal []`).

R1-2: fixed in the "PC-F17: review 1 fixes" commit (child of 73069c02). The `window` computed has an `equal` that compares `start`, `end`,
`leadOffset` and `totalSize`, so a scroll that keeps the window renders nothing. Re-measured with
3 interleaved runs on this host: base, the build before the fix (`v2`) and the build with it
(`r1`).

| story | build | over 50 per run | cpuSum (tasks of 4 ms or more) | tasks of 16 ms or more | longest task's thread time (ms) |
|---|---|---|---|---|---|
| grid-readonly | base | 20, 18, 25 | 3643, 3536, 3883 | 83, 83, 83 | 66, 68, 61 |
| grid-readonly | v2 | 0, 2, 4 | 4088, 4166, 4153 | 157, 158, 157 | 43, 40, 43 |
| grid-readonly | r1 | 5, 2, 3 | 3992, 4260, 4124 | 155, 159, 148 | 42, 39, 42 |
| tree-grid-large | base | 1, 0, 0 | 2337, 2384, 2157 | 81, 83, 82 | 52, 44, 37 |
| tree-grid-large | v2 | 0, 0, 1 | 2495, 2197, 2581 | 88, 76, 88 | 30, 26, 27 |
| tree-grid-large | r1 | 2, 0, 2 | 2350, 2546, 2938 | 73, 80, 113 | 32, 28, 31 |

The fix does not move cpuSum or the task count on the probe. Each screen-sized wheel step moves
the window in both of its tasks, so the probe never takes the path the fix saves. The saving is
on scrolls smaller than the overscan, whose sliced frame keeps the window, and on an overscan
task with nothing to add. Every over-50 task of v2 and r1 here is a wall-time stall on a loaded
host: the longest task's thread time is 39-43 ms on the flat grid and 26-32 on the tree.

R1-3: fixed in the "PC-F17: review 1 fixes" commit (child of 73069c02). The new unit test `keeps a replaced active row off its element, so focus
moves with a focusin` focuses row 0's cell and replaces row 0's object with one of a new id, so
only that key leaves and only its replacement enters. It asserts that the replacement renders as
`#100000` and that the old cell element is detached. It also asserts that focus is on the grid's
active cell, a different element, and that a `focusin` reached it. With the retire argument
forced to `null`, the test fails on the detach (`expected true to be false`). One difference
from the suggested assertions: when the active row's id disappears, the grid moves the active
cell to the next row (row 1), not onto the replacement. So the test asserts focus on the active
cell, not on the replacement's cell.

R1-4: fixed in the "PC-F17: review 1 fixes" commit (child of 73069c02) (recorded, not changed, per the orchestrator's ruling). Deviation 6 records
that the probes measure unthrottled against 0004 §16's mid-tier mobile CPU throttle profile, and
the commit says §16 is covered unthrottled. The probes stay unthrottled, like the existing tree
and tiles probes.

Also corrected: deviation 1 now cites 0004 §4, not §5.

Local runs for these fixes: core-ui unit tests for `grid/**`, `tree-grid/**` and `tiles/**` (632
passed); size:check (`./grid` 41.27 against 41.37); the Chromium PR-tier e2e for `grid-*`,
`tree-grid*` and `tiles` in CI mode (394 passed); `dotnet build Tellma.slnx` (0 warnings) and
`dotnet format --verify-no-changes`. The `@perf` probes ran on Chromium with one worker, three
times each, and are indicative only: flat grid 3 of 3, tree 3 of 3 and tiles 3 of 3, all passing
outright on a quieter host than the earlier runs.
