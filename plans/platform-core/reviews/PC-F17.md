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

1. **Rows are tracked by slot, not by `rowId(row)`.** Spec 0004 §5 says the window's `@for`
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
   the ceiling. The budgetNotes entry gives the breakdown.

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
