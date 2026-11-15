# PC-F26: forced colors: the row-activation accent bar is Highlight on the first painted cell and may match a hit row header or a checked checkcell wherever SelectedItem equals Highlight (Windows); found by PC-F25

## Implementer notes

### Problem

- Under `forced-colors: active` the active row's accent bar
  (`.tm-grid__row--active > :first-child::before`, `forced-color-adjust: none`) was always
  `Highlight`. The first painted cell under it can be:
  - a row header, which is **hit** (`SelectedItem`) whenever the active cell is in that row, so
    on any grid with row numbers the bar sits on SelectedItem nearly always;
  - a **checked** checkbox cell (`SelectedItem`);
  - the first data cell, which inside the **range fill** paints `Highlight` itself: there the bar
    was invisible on every platform, not only Windows.
- Where SelectedItem and Highlight resolve to one colour (Windows), the first two cases hide the
  bar too.
- The same defect class, found while tracing the overlay-ink scheme PC-F25 introduced:
  - a focused **hit column header** (ArrowUp from row 0 keeps the body selection, so the header
    is hit and focused at once) drew its inset `Highlight` focus ring on SelectedItem;
  - the **row-end cell**'s forced-colors active ring stayed `Highlight` on a checked row
    (SelectedItem) and on the whole-row fill (Highlight). PC-F25 fixed this for data cells only.

### Fix (system colours only)

- `client/projects/core/tellma-core-ui/grid/internal/grid-view.css` (forced-colors block):
  - The bar draws in `var(--tm-grid-overlay-ink, Highlight)`. A pseudo-element inherits custom
    properties from its originating element, so the bar takes the first painted cell's overlay
    ink: Highlight on a plain cell, SelectedItemText on a checked checkbox cell (already set by
    PC-F25), HighlightText on a filled data cell (already set by PC-F25).
  - `.tm-grid__colhdr--hit, .tm-grid__rowhdr--hit` now also set
    `--tm-grid-overlay-ink: SelectedItemText`, so a hit row header gets a SelectedItemText bar.
  - The column-header / select-all focus ring (`outline-color`) uses the same variable: a focused
    hit column header gets a SelectedItemText ring; a checkbox focused inside a checked checkbox
    cell would get SelectedItemText too (on the cell, outside its box); elsewhere Highlight, as
    before.
  - Comment of the overlay-ink block updated to list the bar and the header ring.
- `client/projects/core/tellma-core-ui/grid/internal/grid-end-cell.css` (forced-colors block):
  the checked-row and selected rules set `--tm-grid-overlay-ink` (SelectedItemText /
  HighlightText) and the active ring draws in `var(--tm-grid-overlay-ink, Highlight)`, matching
  the data cells. The end cell is a direct child of the row and nothing sets the property on the
  row, so no ink leaks into it from the data cells.

### Tests

- Showcase: `grid-search-page-story.ts` gains two toggles under the grid
  (`toggle-row-numbers`, `toggle-checkboxes`; defaults unchanged: no row numbers, checkboxes on)
  so each chrome column, or neither, can start the row. Placed below the grid so the toolbar's
  tab order and the rows' page position (which other specs rely on) do not change.
- `client/e2e/specs/grid-search-page.spec.ts`, new describe "the active row under forced colors"
  (LTR; the colours do not depend on direction):
  - "the accent bar takes the ink of whichever cell starts the row": plain checkbox cell →
    Highlight bar; checked checkbox cell → background SelectedItem, bar SelectedItemText (= the
    cell's ink); row numbers on → hit row header SelectedItem, bar SelectedItemText; no chrome
    column → plain first cell Highlight bar, then inside a 2×3 range fill → background
    Highlight, bar HighlightText.
  - "a focused header keeps a visible ring over its selected column": hit, focused colhdr →
    background SelectedItem, solid ring in SelectedItemText.
  - System colours are resolved through a probe element (`forced-color-adjust: none`), so the
    tests pin the **pairing** (mark = paired ink of the surface), which is what holds on Windows,
    rather than a mere difference that Chromium's emulated palette (Highlight rgb(55, 0, 110),
    SelectedItem distinct from it) would satisfy even with the old CSS on the SelectedItem cases.
- `client/projects/core/tellma-core-ui/grid/tm-grid-header-nav.spec.ts`, new unit test "keeps its
  active ring in the ink paired with a checked row under forced colors": the search-page story's
  row-end cells are an empty, aria-hidden band with no row-end body template, which never takes
  focus (End lands on the last data column there), and no showcase story has a row-end body
  template; the unit fixture does. It emulates forced colors through the Playwright provider's CDP
  session (`cdp().send('Emulation.setEmulatedMedia', …)`, reset in `finally` because the unit
  runner shares one page between files) and sets `--focus-ring-width`, which a unit page does
  not load. This is the first unit test to emulate forced colors; e2e stays the main gate.
- Mutant checks: with the old `grid-end-cell.css` the unit test fails (ring `rgb(55, 0, 110)`
  where `rgb(255, 255, 255)` is expected) and passes with the fix. For the e2e tests the old
  values are known from the same palette: the old bar/ring is Highlight `rgb(55, 0, 110)`, which
  differs from the asserted SelectedItemText and HighlightText, and on the fill it equals the
  background; so each of the four non-plain assertions fails on the old CSS. (A dedicated e2e
  mutant run was not made: it needs a second production showcase build.)

### Gates run (local, this slot)

- Client: lint, typecheck, tokens:check, build, size:check (`./grid` 40.91 of 41.01, unchanged
  to two decimals), api:check (no API change), docs:build (no generated diff) all pass.
- test: 2383 of 2384 on the full run; the one failure, `l10n/tm-instant.spec.ts` "warning once
  in development" (a console.warn spy counted 2 calls), is unrelated and passed on a rerun
  together with the changed header-nav spec (82 of 82). The header-nav spec alone: 31 of 31.
- lint:test: only the tellma-core-tools `common.spec.ts` shim timeout (5 s) failed in the full
  run; the file passes alone (7 of 7). D-078 applies.
- e2e, once, CI mode after `build:showcase`, chromium + touch, `--grep-invert
  "@nightly|@perf|@quarantine" --workers 2`: 1067 passed, 1 flaky: `locale-ar.spec.ts:22`
  timed out on `waitForLoadState('networkidle')` and passed on retry. It touches no grid code;
  the machine was loaded by other slots. Both new forced-colors tests passed.
- No .NET file changed, so the dotnet build/format/test gates were not run.

### Deviations

- None from spec 0004 §7/§14 or spec 0010 §C.1's visual rules; the bar stays the inline-start
  2 px accent and Highlight on a plain row.
- Scope widened beyond the title to the same defect in the hit column header's focus ring and the
  row-end cell's active ring (same file family, same mechanism, a few bytes).

### Open risks / for reviewers

- **Not changed: the scrollToRow flash outline** (`grid-flash.css`, `.tm-grid__row--flash`,
  Highlight outline drawn over the whole row). On a checked row it is Highlight over SelectedItem
  cells (invisible on Windows) and over a filled range it is Highlight on Highlight. No single
  system colour fits a row that mixes Canvas, SelectedItem and Highlight cells (SelectedItemText
  would vanish on the Canvas row header in a dark theme), and the flash is a transient cue. A
  follow-up could outline per cell in each cell's overlay ink. Worth an orchestrator call.
- **Not changed: the checked checkbox box** (`.tm-grid__check--on`: Highlight box and border) on a
  checked checkbox cell (SelectedItem): on Windows the box outline merges with the cell, leaving
  the HighlightText glyph as the only mark. The check glyph still carries the state.
- Only Chromium's emulated palette was exercised; the Windows equality of SelectedItem and
  Highlight is the item's premise, not measured here.
- Examine hardest: that every element that can be a row's first child (row header, checkbox
  cell, data cell in each state: zebra, checked, error, find, readonly, fill) resolves
  `--tm-grid-overlay-ink` to the ink paired with its own background, and that the new header
  focus-ring value cannot pick up an ink from an unrelated ancestor.
