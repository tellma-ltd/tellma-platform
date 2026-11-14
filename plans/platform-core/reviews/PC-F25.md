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

## Review 1

Scope checked: the race fix (cellPaint/appStable against the zoneless scheduler's pending render
task), the forced-colors cascade under emulated encapsulation (fill `.tm-grid__scroller
.tm-grid__row > .tm-grid__cell--selected` = 0,6,0 against readonly 0,5,0, zebra/placeholder/
checked/active-row 0,4,0, error/find 0,3,0; no other stylesheet targets grid cells), and each new
assertion against the old CSS. The root cause is removed, not masked: no retries, timeouts or
skips; `appStable` waits on `ApplicationRef.whenStable()`, which holds the scheduled render, and
the tests also wait on `aria-selected` / `data-tm-cut`, which the same render binds as the class.
Mutant reasoning agrees with the implementer: 3 of 4 tests fail on the old CSS, the lone-active
test pins existing behaviour.

Runs (CI=1, production showcase build, chromium, `--workers 2`):
- The four forced-colors tests, `--repeat-each 15`: 56 of 56 passed, no retries (the last 4 were
  cut by my own 590 s wrapper, not failures). Log: `.tmp/pcf25r1-e2e-forced.log`.
- A throwaway probe (`.tmp/pcf25r1-probe/`), forced colors: tree grid range (fill on hierarchy
  cells, HighlightText ring), checked rows (lone active: SelectedItemText ring on SelectedItem;
  inside a range: the fill wins), RTL readonly range, and the non-forced range (background-image
  layer unchanged) all behave as intended. Two combinations fail; see R1-1 and R1-2.
- eslint (3 changed e2e files), stylelint (grid-view.css), e2e tsc: clean. size:check on the
  current dist: ./grid 39.04 of 39.25 KB.

### Findings

**R1-1 (major; not a regression).** `client/projects/core/tellma-core-ui/grid/internal/grid-view.css:808-812`
(with `.tm-grid__editor`, line 533, which has no forced-colors rule). Scenario: forced colors,
editable grid, select a multi-cell range, press F2 (or type) on the active cell. The range stays
selected (`--selected --editing` on the cell), so the cell's `color: HighlightText` is inherited
by the editor's input, while `.tm-grid__editor`'s `var(--field-bg)` is forced to Canvas. Measured
in Chromium: editor background `rgb(255, 255, 255)`, input ink `rgb(255, 255, 255)`: the text
being edited is invisible. The lazy-load draft input (`all: unset`, so `color` inherits) is hit
the same way. The old CSS had the same defect on non-zebra rows, so it is not introduced here,
but this fix redefines the range's ink and claims "text stays legible on the fill", and its
tests only check the cell's own ink. Suggested fix: in the forced-colors block add
`.tm-grid__editor { background: Field; color: FieldText; }` (the editor then also stops reading
as part of the fill), and extend the editable forced-colors test: press F2 on the active cell of
the range and assert the editor input's ink differs from the editor's background. If the
orchestrator would rather not hold the stop-the-line fix for it, file it as a follow-up fix item.

**R1-2 (minor; pre-existing).** `client/projects/core/tellma-core-ui/grid/internal/grid-view.css:788-792`.
The forced-colors find rule (`outline: … dotted currentColor`, 0,3,0) is declared after the
active rule (745-749, same specificity), so on an active cell that is a find match but not the
active match it replaces the 2px solid ring with a 1px dotted outline. Reachable: with the find
bar open, click any other match (clicking does not move the find index). Measured on
grid-list-screen: active cell (3,1) `dotted 1px`, the other matches `dotted 1px`, so the active
cell no longer has a ring; this breaks the "activity still wins the outline" rule the error block
states. The same ordering also lets the find dots replace an errored cell's dashes. Suggested
fix (no bytes): move the `--find` block above the `--active` block (keep `--find-active` after
both), and add an assertion that an active non-active-match cell keeps `outline: solid`.

**R1-3 (minor).** `client/e2e/specs/grid-a11y-editable.spec.ts:94`. The new describe title
`'forced colors (§14)'` puts a spec section reference in a test name, which AGENTS.md forbids
(the sibling title in grid-a11y.spec.ts:96 predates this change). Suggested fix: title it
`'forced colors'`.

### Responses

- R1-1: fixed in the review 1 fix commit (see `git log`; the fix commit cannot cite its own sha).
  The forced-colors block now gives `.tm-grid__editor` `background: Field; color: FieldText`,
  so the editor (and the draft input, which inherits through `all: unset`) is a field of its own,
  not part of the fill. The editable forced-colors test now presses F2 on the active cell of the
  range, checks the range stays selected, and asserts the input's ink differs from the nearest
  painted background under it. On the previous CSS it fails (ink
  `rgb(255, 255, 255)` on `rgb(255, 255, 255)`); it passes with the fix.
- R1-2: fixed in the same commit. The `--find` block now comes first of the cell outlines (before
  `--error` and `--active`), and `--find-active` stays after `--active`. An active cell on a
  non-active match keeps its solid ring, and an errored match keeps its dashes; no bytes added. New
  test "the active cell keeps its solid ring on a find match that is not the active match"
  (grid-list-screen, query "Item", click a rendered non-active match): it fails on the previous CSS
  (received `dotted`) and passes with the fix.
- R1-3: fixed in the same commit. The new describe in grid-a11y-editable.spec.ts is titled
  'forced colors'. The pre-existing sibling titles in grid-a11y.spec.ts are left as they were.
- Evidence: component CSS 13.30 kB (error budget 13.5 kB); the five forced-colors tests in CI mode,
  `--repeat-each 25 --workers 6`: 125 of 125. Gates and the integration run are listed in the
  commit messages.
