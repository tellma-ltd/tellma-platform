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

The CSS paints correctly. On Windows, Highlight is opaque `rgb(55, 0, 110)`, so the old read
happened to be right there.

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

### The fix (e2e only, no CSS change)

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
  - The label and knob checks composite HighlightText over the Highlight fill.
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
- **The new spec on the real Windows palette:** `--repeat-each 3 --retries 0`, 18 of 18 passed.

### Gates run (Windows)

- typecheck and lint: green.
- `additions-forced-colors.spec.ts`, Chromium, CI mode, `--repeat-each 3 --retries 0`: 18 of 18
  passed.
- Full PR-tier e2e, `CI=1 node scripts/e2e.mjs --project chromium --project touch --grep-invert
  "@nightly|@perf|@quarantine"`: 1315 passed, 0 failed, 0 flaky.
- Not verified on Linux. The Linux evidence is the simulation above, which reproduces CI's
  numbers; the first Linux run is this push's CI.
