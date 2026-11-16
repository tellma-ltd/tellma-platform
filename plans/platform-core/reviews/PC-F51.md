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
  and touch:
  - `--repeat-each 4`: 28/28.
  - `--repeat-each 5` after the full run: 35/35.
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
