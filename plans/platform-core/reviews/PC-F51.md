# PC-F51: forced-colors flash ring e2e test fails on Linux Chromium

## Implementer notes

### Cause: the test, not the ring

CI run 43938757831 (sha e4338cb3, Linux `client` job, chromium) failed
`grid-search-page.spec.ts` "the flash rings a mixed row above every cell and keeps the cells
painted" on the first try and on the retry. The failure was on the first cell (the hit row
header, SelectedItem), top edge, outer band:

- expected `["rgba(5, 0, 73, 0.8)", "rgb(255, 255, 255)"]`;
- received `["rgb(9, 20, 100)", "rgb(255, 255, 255)"]`.

The CI trace (artifact `client-e2e-report`, downloaded to `.tmp/pcf51-artifacts`) shows the Linux
forced-colors palette: Highlight `rgba(5, 0, 73, 0.8)`, which is **translucent**, SelectedItem
`rgb(25, 103, 210)` and HighlightText `rgb(255, 255, 255)`. On Windows, Highlight is opaque
(`rgb(55, 0, 110)` in Chromium's emulated palette).

- The test compared a screen pixel with the band's computed colour string. A translucent band
  composites over the cell beneath it. Highlight over SelectedItem is 0.8 × (5, 0, 73) +
  0.2 × (25, 103, 210) = (9, 20.6, 100.4): exactly the received `rgb(9, 20, 100)`. An `rgba(…, 0.8)`
  string can never equal an `rgb(…)` pixel.
- The ring itself painted correctly on Linux. The outer band is Highlight composited over the cell,
  as CSS specifies. The inner band is HighlightText, which matched. Every style-level assertion in
  the same test also passed on Linux: the cell surfaces and the absence of a tint animation.
- Visibility on Linux is intact: HighlightText on SelectedItem is about 5.4:1, and the composited
  outer band on SelectedItem about 3:1. The two-band design guarantees one contrasting band on
  every surface. No CSS change is needed, and none was made.
- Windows passed because an opaque Highlight replaces the pixel, so pixel and string agree.

### Fix: compare like with like

`client/e2e/specs/grid-search-page.spec.ts`:

- `ringPaint(page, viewIndex, before)` reads each band's pixel from two screenshots:
  - `before`, taken just before the flash;
  - one taken with the ring on.

  It computes the expected pixel the way the screen does. The band's system colour, alpha
  included (resolved in the page by the same probe as before), is composited source-over on the
  pre-flash pixel, and the result must match the ring-on pixel within 2 per channel (compositing
  rounding). For an opaque system colour this reduces to the old exact check. For a translucent
  one it is the blend the compositor paints.
- `beforeFlash(page)` takes the reference screenshot with the pointer on the flash button and the
  focus in it, as they will be after the click. That way nothing but the flash differs between
  the two shots: focus leaving the grid, for example, would otherwise change its chrome.
- The test asserts that the flashed row's bounding box is the same before and after the flash, so
  the two screenshots line up.
- Failures name the point and band, and give the actual and expected pixels.
- Showcase `grid-search-page-story.ts`: the "Flash the active row" button now passes
  `align: 'nearest'`, so a flash on a visible row never scrolls. The default `'center'` may scroll
  to centre the row, which would misalign the two screenshots. The test asserts the unchanged box.

### Why the test still catches the original defect

- No ring, or a ring the cells cover (the pre-ring CSS, which outlined the row under its
  positioned cells), leaves the ring-on pixels equal to the ring-off ones. At any point, one of the
  two bands then fails. Both can pass only if the pixel beneath matches Highlight composited on
  itself and HighlightText composited on itself at once, and the two are a contrasting pair.
- The tint wipe (the old CSS ran the tint under forced colors, painted as Canvas) changes the
  pixels to Canvas, not to the blend. The style-level assertions (cell surfaces, no animation) are
  unchanged and also catch it.
- Mutant runs (Windows, CI=1 production showcase, chromium, the ring test alone); each fails:
  - (a) The flash sheet before the ring (row outline, tint running under forced colors). It fails
    at the cell-surface assertion, because the running tint repaints the cells.
  - (b) The current sheet with the ring removed (`content: none`, tint still suppressed). It fails
    on the pixels: "cell 0 top outer: rgb(25, 103, 210), expected rgb(55, 0, 110)".
  - (c) The tint suppressed, but with the row's own outline as the mark: the paint-order defect
    PC-F42 fixed. It fails on the pixels with the same message.

  Logs: `.tmp/pcf51-mutant-{a,b,c}.log`.

### Gates run (local, this slot)

- `pnpm run lint`, `typecheck`, `build`: pass.
- `size:check`: pass, `./grid` 40.71 / 40.73 KB (41683 B, unchanged: no library change).
- `test`: core 484, core-testing 22, core-ui 2536, plus 2, 28, 9 and 1 in the smaller projects;
  all passed.
- PR-tier e2e (CI=1, chromium and touch, `--grep-invert "@nightly|@perf|@quarantine"`,
  3 workers): 1119 passed, 0 failed, 0 flaky.
- Stability, the forced-colors describes in `grid-search-page` and `tree-grid-restore`, chromium
  only (the touch project's `testMatch` does not match these specs; corrected in review 1):
  - `--repeat-each 4`: 28/28 (7 tests × 4).
  - `--repeat-each 5` after the full run: 35/35 (7 × 5).
- Not reproduced on Linux locally (see Open risks). The CI Linux job is the confirmation.

### Deviations

None.

### Open risks / for reviewers

- The Linux palette was not reproduced locally: Chromium's emulated forced-colors palette follows
  the host platform. The blend model was checked against the CI numbers (it predicts the failing
  pixel exactly), and the opaque path was run locally.
- The tolerance of 2 per channel absorbs 8-bit compositing rounding. A real ring/no-ring
  difference is tens of units per channel.
- The reference screenshot relies on nothing but the flash changing between the two shots. The
  box assertion guards scrolling; focus and pointer are pinned by `beforeFlash`.

## Review 1

Scope checked: commit 5ebb8fbb only (the e2e spec hunk and the showcase button). Read against
`grid-flash.css`, the forced-colors block of `grid-view.css`, `e2e/playwright.config.ts`,
`e2e/support/settle.ts`, the CI log of run 43938757831 and the downloaded trace and
error-context (`.tmp/pcf51-artifacts`); PC-F42's review for context; spec 0004 §7 and 0010 §C.8.

Diagnosis, verified:

- The CI trace's `systemColor` results are SelectedItem `rgb(25, 103, 210)`, HighlightText
  `rgb(255, 255, 255)` and Highlight `rgba(5, 0, 73, 0.8)`. The failing sample is cell 0 (the hit
  row header, SelectedItem), top edge, outer band.
- Reproduced the blend in a standalone Chromium page on this host
  (`.tmp/pcf51-r1-blend.cjs`): `rgba(5, 0, 73, 0.8)` over `rgb(25, 103, 210)` paints exactly
  `rgb(9, 20, 100)`, the CI pixel. The test's `Math.round` model gives `rgb(9, 21, 100)`, 1 off
  in green. Over five other surfaces the model is exact. A tolerance of 2 is justified, and real
  ring/no-ring differences are tens of units.
- The CI trace's last screenshot shows the ring on row 2 on Linux: a dark outer band and a white
  inner band around the whole row.
- Contrast on Linux: the composited outer band against SelectedItem is 3.0:1, and the inner
  HighlightText band against SelectedItem is 5.4:1. On the range fill (Highlight over Canvas)
  and on Canvas, one band is 11.3:1. Every surface has a band at 3:1 or more, which meets the
  non-text contrast bar, and spec 0004 §7 asks only that states stay distinguishable through
  system colours. Chromium has no OS forced-colors mode on Linux, so this palette exists only
  under emulation. It is not a product defect, and the test was wrong to compare a pixel with an
  `rgba(…)` string.
- On this host's emulated palette (`.tmp/pcf51-r1-palette.cjs`): Canvas `rgb(255, 255, 255)`,
  Highlight `rgb(55, 0, 110)`, HighlightText `rgb(255, 255, 255)`, SelectedItem
  `rgb(25, 103, 210)`. All are opaque, so the Windows path is the old check within 2.

The new test, traced:

- It cannot pass a missing ring in either scenario. Each cell kind has a band whose expected
  pixel differs from its pre-flash pixel:
  - SelectedItem cells (hit row header, checked cells): the outer band.
  - The range fill: the inner band.
  - Canvas cells: the outer band.

  This holds on both palettes. On Linux, a ring under a translucent range-fill cell gives
  exactly the expected outer pixel, but the opaque inner band still fails it.
- Before and after shots: the flash duration is pinned to 60 s in `beforeEach`, so no timer
  races either screenshot. The pointer and focus are on the button in both shots, and the button
  sits below the grid. The box assertion covers page and grid scroll and the
  `min-inline-size: min-content` the flash adds. In the second scenario, row 2's ring is still on
  in the before shot, but it is inset to row 2 and never reaches row 4. No caret, hover or
  animation sits in a sampled pixel.
- `align: 'nearest'` is needed: centring row 4 would scroll the grid. `flash-active` is used only
  by this spec, and no story test references `flashActive`.
- AGENTS.md: the new interfaces, members and functions carry TSDoc, the non-trivial logic has
  inline comments, and no test names or docs carry PC ids or `docs/` references.

Runs (this review, this slot, Windows, CI=1 production showcase):

- Forced-colors describes, `--project chromium --project touch --repeat-each 5 --workers 3`,
  `grid-search-page.spec.ts` and `tree-grid-restore.spec.ts`: 35/35 passed
  (`.tmp/pcf51-r1-forced-1.log`). Every run was chromium; see R1-2.
- Mutants, the ring test alone, `--retries 0` (`.tmp/pcf51-r1-mutants.sh`,
  `.tmp/pcf51-r1-mutant-*.log`):
  - (a) The pre-ring sheet fails at the cell-surface assertion (every cell Canvas).
  - (b) Ring removed: fails, "cell 0 top outer: rgb(25, 103, 210), expected rgb(55, 0, 110)".
  - (c) Row outline with the tint suppressed: fails, same message.
  - (d) Outer band `transparent`: fails, "cell 0 top outer: rgb(0, 0, 0), expected rgb(55, 0, 110)"
    (forced colors repaint the transparent border as CanvasText).
  - (e) Inner band removed (`outline: none`): fails, "cell 0 top inner: rgb(25, 103, 210),
    expected rgb(255, 255, 255)".

  The sheet was restored and `cmp`-verified afterwards; `git status` shows only this file.
- Then, on the restored sheet, the `grid-search-page` forced-colors describes `--repeat-each 10
  --retries 0`: 50/50 passed (`.tmp/pcf51-r1-forced-2.log`).
- `pnpm run typecheck`: pass. `eslint` and `prettier --check` on both changed files: clean.
  `eslint .` and `stylelint "projects/**/*.css"` (after the restore): pass.

### Findings

**R1-1 (minor): the "a missing ring cannot pass both bands at one point" argument is not true
per point, and nothing checks that a sample can tell a ring from no ring.**
`client/e2e/specs/grid-search-page.spec.ts:893-903` (the `expectRinged` TSDoc) and `:837-850`
(`read`).

The two bands are read at two different pixels, `band / 2` and `band * 1.5` from the edge. A
missing ring passes both wherever the pre-flash shot already holds Highlight in the outer band
over a HighlightText-coloured surface. The grid draws exactly that:

- the active cell's forced-colors outline is Highlight, inset by one focus-ring width
  (`grid-view.css`, `.tm-grid__cell--active`);
- on a plain cell, that outline sits over Canvas;
- on Chromium's emulated palette on this host, Canvas and HighlightText are both
  `rgb(255, 255, 255)`.

So a plain active cell passes both bands with no ring. Today's two scenarios put the active cell
in the range fill, where the overlay ink is HighlightText, so no live gap exists, and the other
cells would fail anyway. But a future edit of the scenario, such as making the active cell a
plain one, or another platform palette, can silently turn samples into tautologies. The Linux
palette cannot be run locally, so the argument for it rests on reasoning alone.

Suggested fix: reword the TSDoc to the guarantee that actually holds (per cell, at least one
band's expected pixel differs from its pre-flash pixel). Better, make it checked: in `read`,
record `Math.max(|expected − base|)`, and in `expectRinged`, assert per cell (and per side) that
at least one band differs by well over the tolerance (say > 8). Then a sample that cannot
discriminate fails loudly on whatever palette CI uses.

**R1-2 (minor): the stability runs are reported as "chromium and touch", but touch runs none of
these specs.** `plans/platform-core/reviews/PC-F51.md:82-85` and the commit message's "Tests:"
line.

The `touch` project's `testMatch` (`client/e2e/playwright.config.ts:88-89`) is
`grid-touch|tooltip-touch|splitter-touch|form-controls-touch|tiles-touch`, so
`grid-search-page` and `tree-grid-restore` run on chromium only. The 28/28 and 35/35 counts are
7 chromium tests × 4 and × 5. This is reporting accuracy only, not a test gap, but the ledger
should not claim touch coverage for forced colors. Suggested fix: say "chromium (the touch
project does not match these specs)" in the notes, and in the message of the next commit.

### Responses

- R1-1: fixed in the review 1 fix commit (see `git log`; the fix commit cannot cite its own sha).
  - The `expectRinged` TSDoc no longer claims that a missing ring cannot pass both bands at one
    point. It now states the guarantee the test checks.
  - Each `BandSample` records `over`, the cell or row side it was read over, and `change`, the
    largest channel difference between its expected pixel and its pre-flash pixel.
  - `expectRinged` asserts, per cell and per row side, that at least one read changes by more
    than 8: four times the compositing tolerance. A read that cannot tell a ring from no ring
    therefore fails loudly ("cell n: no read the ring would visibly change") on whatever palette
    runs it. Once that holds, a missing ring fails at every cell and side.
  - The current scenarios pass the guard on this host's palette: the forced-colors describes,
    chromium, CI=1 production showcase, `--repeat-each 5 --retries 0`, passed 35/35
    (`.tmp/pcf51-r1fix-forced.log`).
  - lint and typecheck pass.
- R1-2: fixed in the review 1 fix commit. The stability lines in Gates now say chromium only:
  the touch project's `testMatch` does not match these specs, so the counts are 7 tests × 4 and
  × 5. The review 1 fix commit's message says the same.
