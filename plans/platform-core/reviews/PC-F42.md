# PC-F42: forced colors: scrollToRow flash outline and checked checkbox box

## Implementer notes

### Problem (as found, wider than the item states)

- **The flash outline never showed over a positioned cell, on any platform.** The flash drew
  `outline: … Highlight` on the row element. Data cells are `position: relative`, so Chromium
  paints them after the row's own outline (an element's outline is painted with its normal-flow
  content, before its positioned descendants): any opaque data cell (zebra Canvas, checked
  SelectedItem, the Highlight range fill) covered it. Only the unpositioned chrome cells (row
  header, checkbox cell) and transparent cells let it through. Verified with a standalone page
  under Chromium's forced-colors emulation before the fix.
- **The flash tint wiped the row's states for its run.** The tint animation (`background-color`
  from `--grid-flash-bg`) kept running under forced colors. Forced colors paint an author colour as
  Canvas, so for the 1.2 s of the flash every cell of the row lost its checked SelectedItem, the
  range fill's Highlight and the hit row header's SelectedItem. Spec 0010 §C.8 says the outline
  *replaces* the tint under forced colors.
- The item's own two defects: no single system colour fits a row that mixes Canvas, SelectedItem
  and Highlight cells; and the checked checkbox box (`Highlight` box and border) on a checked
  checkbox cell (SelectedItem) has no edge wherever SelectedItem equals Highlight (Windows).

### Fix (system colours only)

- `client/projects/core/tellma-core-ui/grid/internal/grid-flash.css`, forced-colors block:
  - `.tm-grid__row--flash > * { animation: none }`: no tint under forced colors, so every cell
    keeps its own surface while it flashes.
  - The ring is the flashing row's `::after`: `position: absolute; inset: 0; z-index: 3;
    pointer-events: none`, a `--focus-ring-width` Highlight border and, just inside it, a
    `--focus-ring-width` HighlightText outline (`outline-offset: -2 × width`). z-index 3 with a
    later tree position puts it above every layer a cell paints (the editor 1, the cut marquee 1,
    the accent bar 1, the spinner 2) and above the sticky row-end cell (3). The window's
    `will-change: transform` makes it a stacking context, so the ring never rises above the
    sticky header.
  - The flashing row gets `position: relative` as the ring's containing block, except the outlier
    row (`:not(.tm-grid__row--outlier)`), which is absolutely positioned already and must stay
    so: an active row scrolled away during its flash becomes the outlier.
  - Why two bands: Highlight/HighlightText is a pair the theme guarantees to contrast. The outer
    Highlight band shows on Canvas; the inner HighlightText band shows on Highlight (the fill)
    and, where SelectedItem equals Highlight (Windows), on SelectedItem. Per-cell inks (the
    overlay-ink scheme PC-F26 suggested) were rejected: a pseudo-element per cell collides with
    the cut marquee (`::after`) and the active row's accent bar (`::before`, on the very row
    `scrollToRow` activates), the chrome cells would need positioning that overrides the sticky
    row-end cell, and a per-cell outline is exactly the active cell's ring.
- `client/projects/core/tellma-core-ui/grid/internal/grid-view.css`, forced-colors block: the
  `.tm-grid-bool--on, .tm-grid__check--on, .tm-grid__check--mixed` border draws in
  `var(--tm-grid-overlay-ink, Highlight)`. A checked checkbox cell already sets the ink to
  SelectedItemText (PC-F25), so the Highlight box gets a SelectedItemText edge there; the select-all
  box on the plain header keeps Highlight. The same rule gives an "on" boolean glyph box a
  HighlightText edge inside the range fill and a SelectedItemText edge in a checked row (same
  defect, same declaration).
- `grid/tm-grid-base.ts`: the `scrollToRow` TSDoc says "a ring" instead of "an outline" under
  forced colors.

### Size

- `./grid`, measured with the gate's own measurer (linked production, gzip): 41633 B (40.66 KB)
  at 7fef6b6c → 41670 B (40.69 KB) with the fix, against the 40.73 KB (41707 B) ceiling: +37 B,
  37 B of headroom left. No ceiling change.

### Tests

- Showcase: `grid-search-page-story.ts` gains a "Flash the active row" button (`flash-active`)
  under the grid, next to PC-F26's chrome toggles: `scrollToRow(activeRowId, { flash: true,
  activate: false })`, so the active cell and the range survive the flash.
- `client/e2e/specs/grid-search-page.spec.ts`, new describe "the row flash and the row checkbox
  under forced colors" (LTR; the colours do not depend on direction):
  - "the flash rings a mixed row above every cell and keeps the cells painted": row numbers on,
    row 2 checked, a 2×3 range from (2,0); flash row 2. Its cells are hit row header
    (SelectedItem), checked checkbox cell (SelectedItem), three filled cells (Highlight), two
    checked cells (SelectedItem) and the checked row-end cell (SelectedItem); none animates. The
    test **screenshots the page and reads the pixels** at the middle of each band, on the top and
    bottom edge over every cell and on both inline ends: outer Highlight, inner HighlightText.
    Then an unchecked row 4 with a range over its last two columns (plain cells beside the fill
    and the hit row header), same assertions. Pixels, not styles, because the original defect was
    paint order: a styles-only test passes a ring the cells cover.
  - "a checked row's checkbox keeps its edge over the checked cell": cell SelectedItem, box
    Highlight, edge SelectedItemText, glyph HighlightText; the mixed select-all box keeps a
    Highlight edge on its plain header.
- `client/e2e/specs/tree-grid-restore.spec.ts`: the forced-colors flash test read the row's
  `outlineStyle`; it now reads the ring (`::after` border style), and asserts no animation runs
  and the first cell is not painted the tint. The reduced-motion test asserts the ring is off
  outside forced colors.
- Unit (forced colors over CDP, as PC-F26's header-nav test does, reset in `finally`):
  - `tm-grid-scroll-to-row.spec.ts` "rings the flashing row above its cells, untinted, under
    forced colors": a mixed row's surfaces survive the flash with no animation; the ring's
    computed geometry, z-index and colours; the row is `relative`, and an active row scrolled out
    while flashing stays the `absolute` outlier with its ring.
  - `tm-grid-selectable.spec.ts` "keeps a checked box's edge in its cell's paired ink under forced
    colors".
- Mutant checks, old CSS against the new tests: both unit tests fail (the flash test on the
  wiped surfaces, the checkbox test on `rgb(55, 0, 110)` where `rgb(255, 255, 255)` is expected).
  E2E mutants: the old CSS fails both new e2e tests and the updated tree-grid flash test; the old
  row outline with only the tint suppressed still fails the pixel test (inner-band samples read
  the cells' SelectedItem/Highlight, not HighlightText), so the pixel test catches the
  paint-order defect, not just the missing tint suppression.

### Gates run (local, this slot)

- `pnpm run lint`, `typecheck`, `tokens:check`, `build`: pass.
- `size:check`: pass, `./grid` 40.69 KB / 40.73 KB (37 B headroom).
- `api:check`: pass, no API report change. `docs:build`: OK (87 components), no tracked diff.
- `lint:test`: 248/248.
- `test`: core 484/484, core-testing 22/22, core-ui 2501/2503 in the full run. The two
  `tm-tree.spec.ts` failures are a cross-file `console.warn` leak from the pre-existing
  `scrollToRow` unknown-column test ("no column 'nope'") reaching tm-tree's no-warning assertion;
  `tm-tree.spec.ts` passes alone (23/23) and together with `tm-grid-scroll-to-row.spec.ts`
  (35/35). Not caused by this change (the leaking test is untouched); flagged as a flake.
- PR-tier e2e (`CI=1`, chromium + touch, `--grep-invert "@nightly|@perf|@quarantine"`, 3 workers):
  1117 passed, 0 failed, 0 flaky (23.8 min).
- No .NET gate (client-only change). Nightly-tier tests added: none.

### Deviations

- Spec 0010 §C.8 says "under forced colors an outline replaces the tint". The mark is a two-band
  ring drawn by a pseudo-element, not the row's `outline`, and the tint is now actually off under
  forced colors (it was running and being painted as Canvas). Same intent; the literal outline
  could not be seen over positioned cells.
- Scope widened beyond the item's title: the tint suppression above, and the "on" boolean glyph
  box (`.tm-grid-bool--on`, which shares the checkbox's rule) gets the same paired-ink edge. That
  box exists only on an editable boolean, so the edge matters in the range fill (HighlightText);
  a checked row is readonly-only, and since review 1 its booleans are boxless (see R1-3).

### Open risks / for reviewers

- The two-band ring relies on Highlight/HighlightText being a contrasting pair and on
  SelectedItem equalling Highlight where the two are not told apart (Windows). Where a custom
  theme makes SelectedItem distinct from Highlight, neither band is guaranteed to contrast with
  SelectedItem, though in practice one does (Chromium's emulated palette: HighlightText on
  SelectedItem 5.4:1). Only Chromium's emulated palette was exercised.
- The ring is 2 × `--focus-ring-width` deep (4 px) on a ~32 px row for the flash's 1.2 s; it covers
  the top and bottom of the active cell's ring and the accent bar's ends while it lasts.
- (Superseded by R1-1: the claim was wrong; without a row-end column the row box was only
  view-wide. Since review 1 the flashing row spans its tracks.)
- Examine hardest: (1) that `position: relative` on a flashing row can never land on the outlier
  or disturb the sticky row-end cell, the accent bar or the editor overlay; (2) the pixel
  sampling in `ringPaint` (band middles, `Math.floor`, device pixel snapping) for flakiness on
  other machines; (3) whether `animation: none` under forced colors loses anything a forced-colors
  user relied on.

## Review 1

Scope checked: spec 0004 §7 (forced colors: states survive via system colours, Playwright-gated)
and §14; spec 0010 §C.8 (the flash, "an outline replaces the tint"); PLAN.md §6-§7; D-057,
D-067 and D-094 (./grid budget); the PC-F26 precedent and its review. Read in full: both CSS
hunks against all of `grid-view.css`, `grid-flash.css` and `grid-end-cell.css`, the view
template (`grid-view.ts`), `scrollToRow`/`flash` in `grid-core.ts`, the two unit tests, the
three e2e tests and the showcase button.

Traced and found sound:

- Outside forced colors nothing changes: every new rule sits inside `@media (forced-colors:
  active)`, and the reduced-motion e2e now pins the ring off there.
- Stacking: the row gains `position: relative` with no z-index, so no stacking context; the ring
  joins the window's (the `will-change: transform` window) at z-index 3 after the sticky row-end
  cell (z-index 3, earlier in the tree), above the editor (1), the cut marquee (1), the accent bar
  (1) and the spinner (2). The sticky header (2), the touch handles (1), the notice (3), the find
  bar and the overlay (4) all sit outside the window's context, so they stay above the ring.
  `pointer-events: none` keeps clicks on the cells.
- `position: relative` on the row moves no geometry: no grid code reads `offsetTop`,
  `offsetLeft` or `offsetParent`, the data cells are positioned already, and the chrome cells
  hold no absolutely positioned children.
- The outlier keeps `position: absolute` (and its transform), so the ring follows it; rows are
  tracked by row key and the flash by row id, so recycling cannot carry the ring to another row.
  RTL is symmetric (`inset: 0`, a uniform border).
- `animation: none` (forced colors) is later than the reduced-motion and `--reflash` rules at the
  same specificity, so it wins both; the flash still ends on its timer, not on `animationend`.
- The checkbox edge: every rule that paints SelectedItem or Highlight sets the overlay ink in the
  same block (PC-F26), so the box's border follows the cell; the select-all header never sets it.
- The deviation from §C.8 (a two-band pseudo-element ring, tint actually off) is justified: the
  row's own outline is painted before its positioned cells and cannot show.

Runs (this review, this slot):

- `pnpm run build` then `pnpm run size:check`: gate OK, `./grid` 40.69 / 40.73 KB (logs
  `.tmp/pcf42-r1-build.log`, `.tmp/pcf42-r1-size.log`).
- `pnpm run lint`, `pnpm run typecheck`: pass.
- Full `ng run core-ui:test`: 2503/2503 passed (`.tmp/pcf42-r1-coreui-test.log`). This run's
  cached order put `tm-tree.spec.ts` before `tm-grid-scroll-to-row.spec.ts`; see R1-2 for the
  order that fails.
- `tm-grid-scroll-to-row.spec.ts` then `tm-tree.spec.ts`, serial (`fileParallelism: false`, a
  sequencer forcing that order, via `--runner-config`): 2 failed, 33 passed, with the
  implementer's failure messages (`.tmp/pcf42-r1-order2.log`). Run in parallel, the same pair
  passes 35/35 (`.tmp/pcf42-r1-order.log`).
- e2e, CI=1 (production showcase), chromium, 3 workers, `grid-search-page.spec.ts` and
  `tree-grid-restore.spec.ts`: 65/65 passed (`.tmp/pcf42-r1-e2e.log`).
- A standalone Chromium page with the commit's ring CSS on a row of twenty 150 px tracks in a
  400 px scroller (`.tmp/pcf42-r1-wide.log`, for R1-1).

### Findings

**R1-1 (major): in a grid wider than its view, the ring is only as wide as the view and scrolls
off with the first screen of columns.** `client/projects/core/tellma-core-ui/grid/internal/grid-flash.css:56-77`.
The ring is `inset: 0` on the row box. Without a row-end column a row is only as wide as the
scroller (`grid-view.css:98-110`; the comment at 104-106 says so), and its tracks overflow it.
The implementer's open risk ("its inline ends are off screen") is wrong about the geometry.
Measured with the commit's CSS: twenty 150 px columns in a 400 px scroller, scrolled to the last
column (`scrollLeft` 2600). The row box is 400 px wide and spans x = -2592 to -2192, while the
last cell sits at 258 to 408. Nothing of the ring is on screen.

Scenarios under forced colors, LTR and RTL alike:

- `scrollToRow(id, { column: 'c19', flash: true })` on a wide list (`WideHost`'s shape in
  `tm-grid-scroll-to-row.spec.ts`) shows no flash mark at all.
- A user who has scrolled a wide list sideways, then follows a flash deep link, sees a partial
  ring or none. Where it does show, its inline-end band cuts through the middle of a cell.

This is the item's own defect ("the flash outline vanishes"), and no test covers a sideways-scrolled
grid.

Suggested fix: in the forced-colors block, give the flashing row
`min-inline-size: min-content`, as row-end mode already does for every row
(`grid-view.css:107-110`). That leaves the cells where they are: overflowing tracks already sit at
their minimums, and a grid that fits keeps its view width. Measured on the same page, the row
becomes 3000 px wide, its end aligns with the last cell's (408), and the top and bottom bands
cover every cell. Add a forced-colors unit test on the wide host, in both directions: flash with
`column` set to the last key, then assert that the row's rect contains the last cell's rect.
Re-measure `./grid`, which has 37 B of headroom.

**R1-2 (major): the unit gate fails when the scroll-to-row spec runs before tm-tree's.** This is
a leaked spy, not a flake.

- **The leak.** `client/projects/core/tellma-core-ui/grid/tm-grid-scroll-to-row.spec.ts:251-256`
  spies on `console.warn` and never restores it. The Angular unit-test builder runs Vitest
  non-isolated, so one browser page serves several files. Vitest 4's `vi.spyOn` returns the
  existing mock, with its recorded calls, when the method is already spied. So when
  `tm-tree.spec.ts` runs later on the same page, its warn tests (`tm-tree.spec.ts:403-432`) see
  the stale "no column 'nope'" call. The first fails `not.toHaveBeenCalled()`. It never reaches
  its `mockRestore()`, so the second sees 2 calls instead of 1.
- **Reproduction.** Forcing that order serially gives exactly the implementer's 2 failures
  (`.tmp/pcf42-r1-order2.log`).
- **Exposed, not introduced.** The leaking test is unchanged from the parent. File order comes
  from Vitest's cache: previously failed files go first, then the longest. After a run where the
  scroll-to-row spec failed, as this item's unit mutant runs did, it goes first, and the next full
  run fails the gate. That is what the implementer saw. On a cold cache (CI), files go largest
  first: this commit grows the scroll-to-row spec from 12,591 to 17,645 B, 379 B short of
  `tm-tree.spec.ts` (18,024 B). Today it still sorts after tm-tree; one more test would flip
  the CI order.
- **Fix.** Wrap the unknown-column assertions in `try { … } finally { warn.mockRestore(); }`.
  Harden tm-tree's two warn tests the same way: restore in `finally`, or `warn.mockClear()` right
  after `spyOn`. Then rerun the full core-ui `test` with no "known failures" carve-out.

**R1-3 (minor): the widened `.tm-grid-bool--on` edge is untested, and its checked-row case lands
on boxes that should not exist.**
`client/projects/core/tellma-core-ui/grid/internal/grid-view.css:675-680`.

- **No test.** Neither the unit nor the e2e tests cover a boolean glyph. The case where an
  "on" box really exists and gains an edge is an editable grid's boolean in the range fill
  (HighlightText edge). A later split of the shared selector would regress it silently.
- **The checked-row case.** It can only meet a readonly boolean, since the checkbox column is
  readonly-only (`grid-view.css:141`). By design a readonly boolean has no box (`grid-view.css:453-460`).
  Under forced colors, though, the earlier same-specificity `.tm-grid-bool--readonly` loses to
  the forced `.tm-grid-bool` (665-669) and `.tm-grid-bool--on` (675-680) rules. So every readonly
  boolean in a list draws a checkbox lookalike, and the widened rule now also gives it an edge.
  That part is pre-existing and inferred from the cascade, not measured.

Suggested fix: add a forced-colors unit assertion that an "on" boolean in an editable grid's
range fill has a HighlightText border. Then either restore the boxless readonly glyph under
forced colors (with the glyph drawn in the cell's system ink, since its `--text-strong` fill is
forced) or record that as a follow-up fix item. Also narrow the deviation's wording, which
claims a benefit in checked rows.

### Responses

- R1-1: fixed in the review 1 fix commit (see `git log`; the fix commit cannot cite its own sha).
  The forced-colors block of `grid-flash.css` gives every flashing row (the outlier included)
  `min-inline-size: min-content`, as row-end mode does for every row. Overflowing tracks already
  sit at their minimums and a row that fits keeps the view's width, so no cell moves. New unit
  test "rings every column of a flashing row in a wide grid under forced colors" (ltr and rtl, on
  `WideHost`, which now carries the flash duration and ring-width tokens): `scrollToRow(180,
  { column: 'c19', flash: true })` scrolls the row sideways; the row's rect then contains every
  rendered cell, the last of which is `aria-colindex` 21, and is wider than the view. Mutant run:
  with the commit-1 CSS both directions fail: rendered cells lie past the view-wide row's inline
  end (ltr: a cell's right edge at -2150 against the row's at -2255; rtl mirrored).
- R1-2: fixed in the review 1 fix commit. `tm-grid-scroll-to-row.spec.ts` restores its
  `console.warn` spy in `finally`. Both warn tests in `tm-tree.spec.ts` call `warn.mockClear()`
  right after `spyOn`, so they start from no calls whatever an earlier file left spied, and restore
  in `finally`. Proof: the reviewer's forced serial order (scroll-to-row first,
  `fileParallelism: false`, same sequencer config) now passes 37/37 (35 plus the two new wide
  tests); log `.tmp/pcf42-r2-order.log`.
- R1-3: fixed in the review 1 fix commit, both halves, within budget.
  - The edge test: new unit test in `tm-grid-editing.spec.ts`, "keeps an on boolean box's edge in
    the range fill, and readonly booleans boxless, under forced colors". With the border tokens set
    on the grid, an editable "on" boolean in the range fill is a Highlight box with a solid
    HighlightText border and a HighlightText check.
  - Readonly booleans, measured before the fix (not just inferred): under forced colors a readonly
    "on" boolean drew a Highlight box (the forced `.tm-grid-bool--on` rule beat the
    same-specificity `.tm-grid-bool--readonly`), and a readonly "off" one drew a Field box with a
    border. Even with the readonly rule winning, a `transparent` border is forced to the text
    colour (measured: `rgb(255, 255, 255)` in the fill), so `border-color: transparent` alone
    cannot make it boxless.
  - Fix: the readonly rule is now `.tm-grid-bool.tm-grid-bool--readonly` (two classes, so it
    outranks the forced-colors box rules) and uses `border: 0` instead of a transparent border.
    The box is border-box, so it keeps its size, and the glyph is centred in the content box, so
    the check does not move outside forced colors. A new forced-colors rule draws the readonly
    check in `var(--tm-grid-overlay-ink, CanvasText)`: the fill's HighlightText, a checked row's
    SelectedItemText, and CanvasText on a plain cell. Before, its `--text-strong` was forced to
    Canvas.
  - The same test switches the grid to readonly and checks rows 0 to 2 of the boolean column. All
    three have a transparent background (zero alpha) and no border. Row 0, in the fill, has a
    HighlightText check; row 1 (false) has none; row 2 has a CanvasText check. Mutant run with the
    commit-1 CSS: this fails, because the readonly box is `rgb(55, 0, 110)` (Highlight).
  - Deviation wording narrowed (Implementer notes, Deviations).
- Size: `./grid` is 41683 B (40.71 KB) against the 41707 B ceiling, measured with the gate's own
  measurer: commit 1 was 41670 B; R1-1 adds 7 B (41677 B) and R1-3 6 B. That leaves 24 B of
  headroom. No ceiling change.
- Gates rerun (this round):
  - build, `size:check`, lint, typecheck and `api:check` (no change) pass.
  - Full core-ui `test`, first run: 2505/2506. The one failure, in `tm-grid-list.spec.ts`
    "announces the count here and in all when other pages are checked", is a 200 ms timing wait
    that missed the first announcement. That file is untouched, and it passes alone (41/41).
    Second full run: 2506/2506, 120 files, no carve-out.
  - e2e (CI=1, chromium and touch) on `grid-search-page`, `tree-grid-restore`, `grid-editing` and
    `basics-glyphs`: 106/106.
