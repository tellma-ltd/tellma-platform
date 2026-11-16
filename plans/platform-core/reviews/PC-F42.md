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
  box (`.tm-grid-bool--on`, which shares the checkbox's rule) gets the same paired-ink edge.

### Open risks / for reviewers

- The two-band ring relies on Highlight/HighlightText being a contrasting pair and on
  SelectedItem equalling Highlight where the two are not told apart (Windows). Where a custom
  theme makes SelectedItem distinct from Highlight, neither band is guaranteed to contrast with
  SelectedItem, though in practice one does (Chromium's emulated palette: HighlightText on
  SelectedItem 5.4:1). Only Chromium's emulated palette was exercised.
- The ring is 2 × `--focus-ring-width` deep (4 px) on a ~32 px row for the flash's 1.2 s; it covers
  the top and bottom of the active cell's ring and the accent bar's ends while it lasts.
- The ring spans the row box; on a row wider than the view (fixed columns, sideways scroll) its
  inline ends are off screen, as the old outline's were.
- Examine hardest: (1) that `position: relative` on a flashing row can never land on the outlier
  or disturb the sticky row-end cell, the accent bar or the editor overlay; (2) the pixel
  sampling in `ringPaint` (band middles, `Math.floor`, device pixel snapping) for flakiness on
  other machines; (3) whether `animation: none` under forced colors loses anything a forced-colors
  user relied on.
