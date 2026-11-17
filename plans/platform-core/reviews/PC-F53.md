# PC-F53: forced-colors battery reads Highlight against what it covers (PC-151)

## Implementer notes

### The failure

CI run 43994567778 (99123772, the PC-151 integration) went red. The cause was the Linux PR-tier
e2e on Chromium: four tests in `client/e2e/specs/additions-forced-colors.spec.ts` failed on both
attempts. client-windows was green.

| Test | Expected | CI got |
|---|---|---|
| Checked segment, highlight pixels | > 440 | 130 |
| Switch turned on, track highlight pixels | > 149.3 | 39 |
| `counter-capped`, highlight pixels | 0 | 2 |
| Drop target, outline-ring highlight pixels | > 300 | 125 |

### The cause: the test, not the CSS

This has the same cause as PC-F51. Linux Chromium's forced-colors Highlight is translucent,
`rgba(5, 0, 73, 0.8)` (PC-F51 measured it), so a Highlight fill or outline blends with what it
covers.

The battery read `paintedColour(page, 'Highlight')`, which drops the alpha. It then counted
pixels within 24 of `rgb(5, 0, 73)`. Over the white Canvas a fill paints
`rgb(55, 51, 109)`, which is 50 away on red, so almost nothing matched. Only these pixels did:

- the 1 px Highlight border over the element's own Highlight background, a double blend of
  `rgb(15, 10, 80)`. That ring is the 130 and the 39;
- the parts of an outline that lie over a CanvasText border (`rgb(4, 0, 58)`). That is the 125.

The fills and outlines paint correctly. On Windows, Highlight is opaque `rgb(55, 0, 110)`, so
the old read happened to be right there. (Review 1 found that the checked segment's label did not
paint correctly: see R1-1.)

I cannot run Linux Chromium locally, so I reproduced the failure on Windows. A scratch spec
painted the same five states in `rgba(5, 0, 73, 0.8)`:
- each fill and outline was set with `forced-color-adjust: none`;
- the tile and drop well were given their forced CanvasText border on Canvas, which is what
  Linux paints beneath.

The old spec under that simulation failed with CI's own numbers, and passed the tile test, as CI
did:

| Test | Old spec, simulated | CI |
|---|---|---|
| Segment | 132 | 130 |
| Switch | 39 | 39 |
| `counter-capped` | 3 | 2 |
| Drop target | 125 | 125 |

### The fix (e2e only in the first commit; Review 1 added one CSS rule, R1-1)

- **`e2e/support/pixels.ts`:**
  - `resolvedColour()` reads a colour with its alpha.
  - `paintOver()` composites layers source-over.
  - `TmPixels.paintedOver(before, layers)` counts the pixels that show the layers composited
    over the same pixel of a reference shot, within 3 per channel. A pixel the top layer would
    move by no more than 24 on every channel is not counted: there, paint and no paint look
    alike. An optional `where` limits the read to the edge ring.
  - `paintedColour()` stays for Canvas, and its TSDoc now says it drops alpha.
- **`additions-forced-colors.spec.ts`:** every highlight is read with `paintedOver` against a
  reference shot of the same box.
  - For the fills (segment, switch track, counters), the reference is the box with the element
    hidden (`visibility: hidden`, layout kept). That shows what the fill covers.
  - For the outlines (tile, drop well), the reference is the same box with the state off. The
    outline covers the border and surface that shot shows.
  - The label and knob checks composite HighlightText over the Highlight fill. (Review 1
    replaced both with reads in the mark's own box: R1-1, R1-2.)
  - The "none" checks (unselected tile, unchecked segment, switch off, the neutral counters,
    the idle well) use the same read against the hidden-element shot.
  - On an opaque palette the read reduces to an exact match of the system colour. The
    thresholds (a third of the box, half the ring, a fifth for the dashed ring) are unchanged.
  - I converted the tile test as well. It passed on Linux only because half its ring lies over
    the CanvasText border.

### Proofs (scratch in `.tmp/pcf53-scratch/`, never committed; logs `.tmp/pcf53-break*.log`)

- **Pre-fix CSS mutant (Windows palette):**
  - The library's forced-colors rules were overridden to what the base styles force to, so the
    states lose their Highlight:
    - the selected tile has no outline;
    - the checked segment, the switch track and the attention counter show Canvas and
      CanvasText;
    - the drop outline is CanvasText.
  - All five highlight tests in the new spec go red, each on its main assertion:
    - the tile ring: 0 painted, against more than 592 needed;
    - the segment, switch and attention counter: their "filled" checks fail;
    - the drop ring: 0 painted, against more than 300 needed.
  - The avatar test, which this change does not touch, stays green.
- **Linux simulation:** the new spec is green for all six tests, and the old spec is red for four
  (the numbers are in the table above).
  - Correction (R1-3): the simulation gave the checked segment `forced-color-adjust: none`,
    which removed the label's backplate. It did not model the segment test against the CSS as
    committed. On real Linux, with the backplate and the double-blended border ring, the segment
    fill read would have been about 645 − 128 ≈ 517 against more than 448 needed, a margin of
    about 15%. The simulation's 918 held only for CSS without the backplate, which Review 1's
    CSS fix now makes true (R1-1).
- **The new spec on the real Windows palette:** `--repeat-each 3 --retries 0`, 18 of 18 passed.

### Gates run (Windows)

- typecheck and lint: green.
- `additions-forced-colors.spec.ts`, Chromium, CI mode, `--repeat-each 3 --retries 0`: 18 of 18
  passed.
- Full PR-tier e2e, `CI=1 node scripts/e2e.mjs --project chromium --project touch --grep-invert
  "@nightly|@perf|@quarantine"`: 1315 passed, 0 failed, 0 flaky.
- Not verified on Linux. The Linux evidence is the simulation above, which reproduces CI's
  numbers; the first Linux run is this push's CI.

## Review 1

Scope checked: commit 6b9bea3e only (`client/e2e/support/pixels.ts` and
`client/e2e/specs/additions-forced-colors.spec.ts`), read in full against the five forced-colors
blocks (`tm-tiles.css`, `tm-segmented.css`, `tm-switch.css`, `tm-counter.css`,
`tm-drop-target.css`), the stories, `e2e/playwright.config.ts`, spec 0023 §2.2 (forced colors) and
§12, spec 0005's forced-colors rules, PLAN.md §6-§7, PC-F51's review, and the CI log of run
43994567778 (`.tmp/pcf53r1-ci-failed.log`).

Diagnosis, checked:

- CI's four failures and line numbers match the old spec (segment :108 130 > 440, switch :123
  39 > 149.3, `counter-capped` :143 2 = 0, drop ring :169 125 > 300). The double-blend reading
  holds. A 1 px translucent border over the element's own translucent fill is
  rgb(15, 10, 80), within 24 of rgb(5, 0, 73). The implementer's simulation paints that ring
  as exactly 48 + 48 + 16 + 16 = 128 pixels on the 56×24 segment, which is CI's 130 with the
  corner anti-aliasing.
- The composite model is right for every case read here. The fills are one layer over the
  backdrop, the border ring is a double blend that `paintedOver([highlight])` excludes, and an
  outline over the CanvasText border is rgb(4, 0, 58), which the 24-unit change guard accepts.
- A reference shot that already holds the highlight cannot make a check pass by default. On an
  opaque palette the guard excludes the pixel, and on a translucent one the double blend
  mismatches. The `= 0` checks can only fail, never pass by default.
- The Desktop Chrome project runs at device scale factor 1. Each reference and real pair is
  shot from the same bounding box, and the size guard throws on a mismatch. Nothing animates
  between the pairs.

Runs (Windows, CI=1 production showcase, chromium):

- Committed spec, `--repeat-each 3 --retries 0`: 18/18 passed (`.tmp/pcf53r1-spec.log`).
- Probes of the new reads (`.tmp/pcf53r1-scratch/probe*.spec.ts`, logs
  `.tmp/pcf53r1-probe*.log`):

  | Read | Real palette | Implementer's Linux simulation | Needed |
  |---|---|---|---|
  | Segment fill | 645/1344 | 918/1344 | > 448 |
  | Switch fill | 225/448 | 175/448 | > 149.3 |
  | `counter-attention` fill | 228/368 | 183/368 | > 122.7 |

  The `counter-capped` and `counter-accent` glyph fringes come no closer than 28 to the Linux
  expected colour, so their `= 0` checks have room.
- Mutant run of the committed spec, with the label set to `CanvasText` and the knob set to
  `CanvasText`: both tests pass (`.tmp/pcf53r1-mutant-label-knob.log`).

### Findings

**R1-1 (major): the checked segment's label is invisible under forced colors on every palette,
and the test meant to catch this passes.** `client/e2e/specs/additions-forced-colors.spec.ts:130-133`.
The CSS involved is in `client/projects/core/tellma-core-ui/segmented/tm-segmented.css`, in the
forced-colors block.

Scenario:

- `.tm-segmented__segment--checked` sets `background: Highlight; color: HighlightText` and keeps
  `forced-color-adjust: auto`. Chromium therefore paints its readability backplate behind the
  label text, in Canvas.
- Canvas and HighlightText are both rgb(255, 255, 255) on Windows and on Linux. In Windows' real
  High Contrast Black theme they are both black. Either way the label is drawn in the
  backplate's own colour.
- A 4× screenshot under emulation shows a solid white box where "Week" should be
  (`.tmp/pcf53r1-seg-forced.png`). The 1× pixel map
  (`.tmp/pcf53r1-probe2-real.log`) shows 33×17 pure-white pixels, with no ink, inside the fill.
- The assertion at :131 counts pixels that are HighlightText composited over the fill. The blank
  backplate supplies about 560 of them, and the backdrop corners supply more. With the label
  forced to `CanvasText` (illegible), the read still reports 344 on the real palette and 2 on
  the Linux simulation, and the test passes. The pre-fix assertion had the same hole.

So the implementer's notes say "The CSS paints correctly", but this rule is broken, and the test
named "its label legible on it" cannot see it. The implementer's simulation hid the backplate,
because it gave the segment `forced-color-adjust: none` (`.tmp/pcf53-scratch/make.py`).

Suggested fix:

- CSS:
  - In the forced-colors block, add
    `.tm-segmented__segment--checked .tm-segmented__label { forced-color-adjust: none; color: HighlightText; }`.
    Opting out at the label keeps the segment's own border, outline and focus forced, and drops
    only the backplate.
  - Verified: "Week" then renders HighlightText on Highlight
    (`.tmp/pcf53r1-seg-forced-fixed.png`).
  - Mind the `./segmented` size budget.
- Test: read the label in its own box, not the segment's.
  - Shoot `.tm-segmented__label` of the checked segment, and its backdrop (the label hidden,
    which also removes any backplate, so the fill shows).
  - Require some pixels that are HighlightText over that backdrop.
  - Also require that a large share of the label box (say over 40%) is still unchanged from the
    backdrop, that is, the fill showing between glyphs. A backplate or a missing fill fails the
    second check, and illegible ink fails the first.
- If CSS is out of PC-F53's scope, ledger the CSS defect as its own fix item and mark the label
  assertion as not yet proving legibility. Do not ship it as green proof.

**R1-2 (minor): the knob check, and the multi-layer read in general, count backdrop pixels that
already equal the top layer, so "the knob stands out on the fill" passes with no knob.**
`client/e2e/specs/additions-forced-colors.spec.ts:150-151`, and `client/e2e/support/pixels.ts:101-111`
and `:136-141`.

Scenario:

- For `[highlight, highlightText]`, the change guard compares the expected pixel with *the lower
  layers composited on the reference* (`covered`). It never compares it with the reference pixel
  itself.
- The track is a stadium, and its clipped corners show the white backdrop, which equals
  HighlightText. Every such corner pixel passes the guard (white versus Highlight over white) and
  the 3-unit match (white versus white).
- With the knob forced to `CanvasText`, the read still finds 42 pixels on the real palette and 34
  on the Linux simulation, and the test passes.
- The TSDoc's "is not counted, since it cannot tell paint from no paint" is untrue for these
  pixels.

Adding `!near(expected, under, minChange)` does not fix it. Where HighlightText equals Canvas, real
ink also looks like the backdrop, so the guard would reject every ink pixel.

Suggested fix: read the knob geometrically. Shoot `.tm-switch__knob` and its backdrop (the knob
hidden, showing the fill), and assert `filled(knob.paintedOver(knobBackdrop, [highlightText]))`.
Then reword the `paintedOver` TSDoc so it states the guard's real limit: lower layers are assumed
present at every read pixel, so callers must confine multi-layer reads to where those layers are
painted.

**R1-3 (minor): the Linux simulation behind "the new spec is green on Linux" does not model the
segment test, which leaves a thinner margin on Linux than the notes imply.**
`plans/platform-core/reviews/PC-F53.md` ("Linux simulation"); `.tmp/pcf53-scratch/make.py`
`SIM_CSS`.

Scenario:

- The simulation set `forced-color-adjust: none` on the checked segment. That removed the
  backplate (R1-1), so the simulated fill read 918/1344.
- On real Linux, the backplate stays and the 128-pixel border ring double-blends. The fill read
  should be about 645 − 128 ≈ 517, against more than 448 needed: a margin of about 15%, not
  about 100%.
- The switch (175 against more than 149.3) and attention-counter simulations have no text, so
  they are faithful.

I expect the Linux run to pass, but this is reasoning, not a run.

Suggested fix:

- Correct the simulation note, giving the expected Linux figure.
- If R1-1's CSS fix lands, the backplate disappears and the margin returns.
- Otherwise, confirm the segment read on the first Linux CI run before the item is closed.

### Responses

**R1-1: fixed** in the "PC-F53: review 1 fixes (PC-151)" commit.

- CSS (`tm-segmented.css`, forced-colors block):
  `.tm-segmented__segment--checked:not(.tm-segmented__segment--disabled) .tm-segmented__label { forced-color-adjust: none; color: HighlightText; }`.
  - The `:not(--disabled)` is a narrowing of the suggested selector. A disabled group can still
    show its checked value. That label keeps the adjustment and its GrayText, which reads on the
    backplate as before, rather than turning HighlightText.
  - Size: `./segmented` measures 3.53 KB against its 3.61 ceiling, so no raise was needed.
- Test: the label is read in its own box (34×15 on the story), against the same box with the
  label hidden, which shows the fill. Two checks:
  - Ink: some pixels are HighlightText over that backdrop. The tolerance is 24, because
    anti-aliased strokes rarely reach the exact colour: 54 pixels at 24 against 13 at 3.
    CanvasText ink on the fill never comes within 24 of white.
  - Show-through: more than 40% of the box is unchanged from the backdrop, the reviewer's
    figure. The fixed label shows the fill in 267/510 pixels, 274/510 in the Linux simulation.
    I did not use "over half": that leaves a 12-pixel margin, too thin for another platform's
    font rendering.
- Proof (scratch in `.tmp/pcf53-scratch/`, logs `.tmp/pcf53-r1-*.log`):
  - The new spec run on the pre-fix showcase build (before the CSS changed, so no rebuild) went
    red at the show-through check. The backplate's white satisfies the ink check alone, which is
    why the second check is there (`.tmp/pcf53-r1-prefix.log`).
  - On the fixed build:
    - a mutant that puts the label back under the adjustment (`forced-color-adjust: auto`, the
      pre-fix CSS) goes red at the show-through check;
    - your mutant, the label in CanvasText with no backplate, goes red at the ink check
      (`.tmp/pcf53-r1-mutants-final.log`).
  - The fixed CSS on the real palette: segment fill 1061/1344 (it was 645 with the backplate).

**R1-2: fixed** in the "PC-F53: review 1 fixes (PC-151)" commit.

- The knob is read in its own box, `.tm-switch__knob`, against the same box with the knob hidden,
  which shows the track's fill. The check is `filled(paintedOver(backdrop, [highlightText]))`.
  The knob reads 88/144 on the real palette and in the Linux simulation, against more than 48
  needed.
- With the knob in CanvasText, the knob check goes red, and every other assertion in the spec
  stays green (`.tmp/pcf53-r1-mutants-final.log`).
- The `paintedOver` TSDoc now states the limit:
  - every layer is assumed painted at every pixel read, and only the top one is tested;
  - so pass more than one layer only where the lower ones are known to be painted;
  - to read a mark on a fill, shoot the mark's own box against the same box with the mark
    hidden.

  No caller passes more than one layer now.

**R1-3: fixed** in the "PC-F53: review 1 fixes (PC-151)" commit.

- The implementer notes now carry the correction under "Linux simulation", with the expected
  pre-fix Linux figure (about 517 against more than 448).
- With R1-1's CSS, the label opts out of the adjustment, so the segment's backplate is gone on
  every platform. The simulation's opt-out now matches the CSS it models.
- On the fixed build the simulation is green for all six tests: segment fill 918/1344, label
  ink 82 at tolerance 24, show-through 274/510.

Gates after the fixes (Windows):

- `additions-forced-colors.spec.ts` and `segmented.spec.ts`, Chromium, CI mode,
  `--repeat-each 3 --retries 0`: 42 of 42 passed (`.tmp/pcf53-r1-fc-final.log`).
- Library build and `size:check`: green.
- e2e typecheck, prettier and eslint on the touched files: green.
- The full gate runs at integration.
