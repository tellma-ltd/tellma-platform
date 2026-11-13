# PC-F25: flaky grid-a11y forced-colors e2e (CI red on ddb8259)

## Implementer notes

### Symptom

- CI run 43325841371 (PR-tier e2e, Chromium) failed `grid-a11y.spec.ts:95`, "selected and active
  cells stay distinguishable from normal cells", on its first attempt at line 113:
  `expect(selected.background).not.toBe(plainEven.background)`, with both reading
  `rgba(255, 255, 255, 0)`. The retry passed. PC-036 touched no client code.
- `rgba(255, 255, 255, 0)` is the paint of a cell with no state class at all: cell (2,2) did not
  yet carry `tm-grid__cell--selected` when it was read.

### Root cause 1: the test read paint before the render (test race)

- The test Shift+clicked (2,2) and read `getComputedStyle` at once.
- The grid updates its engine synchronously in the pointerdown handler, but the cells' classes
  only change on the next zoneless render (a `setTimeout`/`requestAnimationFrame` race). Playwright's
  `click()` resolves once the input is dispatched, not after that render. Chromium can run the
  test's next `evaluate` ahead of the timer task, and on a loaded runner the gap grows, so the read
  saw the cell before the range was drawn.
- **Reproduced.** 200 plain repeats on this machine passed (the window is narrow locally). A probe
  that delays the page's zero-delay timers and animation frames by 120 ms (a starved event loop)
  ran the original test body 30 times: 4 failed with CI's exact signature (class absent, both
  backgrounds `rgba(255, 255, 255, 0)`). The fixed body under the same starvation: 30 of 30 passed.
  (The probe was a throwaway spec, not committed.)

### Root cause 2: real product bugs in the forced-colors styles

Investigating the paint showed the test asserted too little, and the styles were broken in ways a
settled read exposes (Chromium, `forcedColors: 'active'`):

- **The range fill lost to the zebra stripe.** The forced-colors fill rule
  `.tm-grid__cell.tm-grid__cell--selected` was declared before the zebra/placeholder/checked rules,
  and view encapsulation adds an attribute per compound, so `.tm-grid__row--zebra > .tm-grid__cell`
  (0,4,0) beat the fill (0,3,0) whatever the order. Selected cells on every odd row of a readonly
  grid painted Canvas with HighlightText ink: white text on white, the selection invisible. The
  same held on checked rows (SelectedItem won) and, at (0,5,0), on the editable grid's readonly
  cells (`.tm-grid__scroller:not(--readonly) .tm-grid__cell--readonly`).
- **Error and find backgrounds also beat the fill** (declared after it at equal specificity): a
  selected errored or find-matched cell lost its selection.
- **The active cell of a range was indistinguishable from the rest of it.** Its Highlight ring sat
  on the Highlight fill. The same applies to the active find match on a fill, and to the clipboard
  marquee (Highlight dashes) over a filled range. On a checked row the ring and marquee were
  Highlight on SelectedItem, which Windows maps to the same color.

### Fix

- `client/projects/core/tellma-core-ui/grid/internal/grid-view.css` (forced-colors block only):
  - The fill rule is now `.tm-grid__scroller .tm-grid__row > .tm-grid__cell--selected`
    (three classes plus three encapsulation attributes), so it outranks every cell background
    rule, the readonly tint included, wherever the rules sit. Declared last as well.
  - The error (dashed) and find (dotted) outlines draw in `currentColor`, the ink paired with
    whatever background the cell ends up with (CanvasText on plain cells, as before).
  - The active ring, the active find match and the marquee draw in
    `var(--tm-grid-overlay-ink, Highlight)`: Highlight on plain cells, as before; the fill sets the
    property to HighlightText and checked rows to SelectedItemText.
  - Component CSS grows 13.07 → 13.25 kB (budget warning 12 kB, error 13.5 kB; the warning
    predates this change). An earlier draft with separate overlay selectors reached 13.48 kB, so
    the custom property was chosen to keep headroom.
- `client/e2e/support/grid.ts`: a `cellPaint(page, row, col)` helper that settles the app
  (`appStable`) before reading computed paint, so a paint read can no longer race a render.
- `client/e2e/specs/grid-a11y.spec.ts`: the test waits for the 2×2 range
  (`selectedCells` count 4) and reads through `cellPaint`; it now also checks the zebra-row
  selected cells, the ink against the fill, and the active ring against the fill. Two new tests: a
  lone active cell keeps the plain background and a Highlight ring; the copy marquee shows over the
  fill.
- `client/e2e/specs/grid-a11y-editable.spec.ts`: a new forced-colors test: an errored and a readonly
  cell inside a range keep the fill, legible ink, the dashed error outline and a visible active
  ring.
- No retries, timeouts or skips were added.

### Evidence

- **Mutant check.** With the old CSS and the new tests, 3 of 4 forced-colors tests fail: the
  zebra-row/active cell paints `rgb(255, 255, 255)` instead of the fill, the errored/readonly
  cells likewise, and the marquee equals the fill. The lone-active test passes on both (it pins
  the plain-cell behaviour the fix must keep).
- **Race.** Starved-loop probe: original body 4/30 failed (CI signature), fixed body 30/30.
- **Repeats.** The four forced-colors tests, CI mode (production build), `--repeat-each 40
  --workers 8`: 160 of 160 passed, on a machine loaded by other slots.
- **Full PR-tier e2e in CI mode** (after `build:showcase`; `CI=1`, chromium + touch,
  `--grep-invert "@nightly|@perf|@quarantine" --workers 2`): 851 of 851 passed, none flaky
  (CI ran 848; the three new tests make 851).
- **Client gates.** lint, typecheck, build, size:check (`./grid` 39.04 of 39.25 KB) and test
  (core-ui 1927 of 1927, every project green) pass. No public API change, so api:check was not
  run.

### Open risks / for reviewers

- Only Chromium's emulated forced colors were exercised (Canvas white, Highlight purple,
  SelectedItem blue). The fix relies on system-color pairs, not on those values.
- Not changed: the row-activation accent bar (`.tm-grid__row--active > :first-child::before`,
  Highlight with `forced-color-adjust: none`) sits on the first painted cell; where that cell is a
  hit row header (SelectedItem) or a checked checkbox cell, the bar can match its background on
  platforms where SelectedItem equals Highlight. Out of this fix's scope; worth a look.
- Examine hardest: the specificity reasoning in the fill rule's comment, and that no other cell
  background rule (in any grid CSS) outweighs (0,6,0).
