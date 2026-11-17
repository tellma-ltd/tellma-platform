# PC-F54: forced-colors HighlightText legibility sweep

## Implementer notes

### The mechanism, verified

A scratch page (`.tmp/pcf54-scratch/`, never committed) under Playwright's Chromium with
`forcedColors: 'active'` showed the following.

- Chromium paints an opaque Canvas backplate behind each line of text in a block that keeps
  `forced-color-adjust: auto`. It does so whatever the block's background, even a system colour.
- `HighlightText` on `Highlight` therefore draws on Canvas:
  - light palette (Canvas `#fff`, HighlightText `#fff`): white on white;
  - dark palette (Canvas `#000`, HighlightText `#000`): black on black.
  - `SelectedItemText` on `SelectedItem` is invisible in light (`#fff` on `#fff`). In dark it is
    legible but plated (`#fff` on `#000`).
- The plate belongs to the block that holds the line.
  - An inline child of a `none` block gets no plate.
  - A block or flex child that keeps `auto` gets its own plate.
  - A `none` flex child of an `auto` block has none.
  - So `forced-color-adjust: none` must sit on the element whose line boxes hold the text, or
    on an ancestor it inherits from.
- SVG glyphs (`tm-icon` strokes in `currentColor`) and backgrounds (masked pseudo-element ticks)
  get no plate.
- A transparent author outline is forced to CanvasText (checked on a scratch page). The date
  popup relies on this for focused cells.

Palettes on this host (Windows, emulated): light Canvas `#fff`, Highlight `rgb(55,0,110)`,
HighlightText `#fff`, SelectedItem `rgb(25,103,210)`, SelectedItemText `#fff`. Dark Canvas
`#000`, Highlight `rgb(26,235,255)`, HighlightText `#000`, SelectedItem `rgb(25,103,210)`,
SelectedItemText `#fff`. Linux CI, from the PC-F51 trace: Highlight `rgba(5,0,73,0.8)`,
HighlightText `#fff`, SelectedItem `rgb(25,103,210)`. Linux Canvas is white, so a HighlightText
label on its plate is white on white there too.

### Findings per stylesheet

| Stylesheet | Text or glyph on a system fill | Before | Ruling |
| --- | --- | --- | --- |
| `date-picker/internal/tm-date-popup.css` | selected day, month, year figures (button's own text) on Highlight | figure in HighlightText on its plate: invisible in both palettes | **fixed** |
| `entity-picker/tm-entity-picker.css` | committed row's label, description, check on Highlight | label and description invisible; the check (SVG) showed | **fixed** |
| `select/tm-select.css` | selected option's content and check on Highlight | content invisible; the check was forced to CanvasText (brand colour), 1.4:1 on the fill | **fixed** |
| `select/tm-multi-select.css` | selected row's box tick and label | label as tm-select; the box's tick (SVG, HighlightText) showed | **fixed** with tm-select; disabled selected box aligned |
| `grid/internal/grid-view.css`, `grid-end-cell.css` | range fill, hit headers, checked rows on Highlight / SelectedItem; text in HighlightText / SelectedItemText | cell text invisible in light (white plate), legible in dark; ink on fill unreachable for consumer content | **fixed** differently: text in CanvasText on its plate (see below) |
| `checkbox/tm-checkbox.css` | checked box's tick on Highlight | the tick is an SVG in HighlightText: no plate, shows | correct, left; check added |
| `tree/tm-tree.css` | multiple-selection row's check box | SVG tick, as the checkbox | correct, left; check added |
| `tiles/tm-tiles.css` | selected tile's box; active tile bar | SVG tick; the bar holds no text | correct, left; check added |
| `menu/tm-menu.css`, `counter/tm-counter.css` | checked item, attention count | already `forced-color-adjust: none` with system colours | correct, left |
| `segmented/tm-segmented.css` | checked segment label | invisible | out of scope: the sibling fix owns it |
| switch, radio, progress, splitter, reorder, touch handles, tab indicator, drop target, dropzone, badge and card dots | fills only, no text on them | nothing to plate | correct, left |
| `button/tm-button.css` | ButtonText on ButtonFace | ButtonFace is Canvas in every palette, so the plate matches the fill | correct, left |

The sweep grepped every `Highlight`, `HighlightText`, `SelectedItem`, `SelectedItemText`, `Mark`,
`ButtonText` and `forced-color-adjust` in `client/projects/core/tellma-core-ui`.

### Fixes

- **Date popup.** The day, month and year cells are buttons whose figure is their own text, so the
  opt-out sits on the selected cell itself: `forced-color-adjust: none` with Highlight and
  HighlightText given explicitly, for its rest and hover states. Opting out drops the forced
  outline repaint, so the focused selected cell's ring is set to CanvasText explicitly and its
  token box-shadow cleared (a `none` element keeps author shadows). The old
  `.tm-date-popup__day--selected` and month/year rules were folded into the one
  `[aria-selected='true']` selector. A disabled selected cell (a committed date outside the
  bounds) now paints the same as an enabled one under forced colours. Before, it was GrayText on
  its plate, which was legible.
- **Entity picker, select, multi-select.** The row stays forced, so its accent and ring
  box-shadows are still stripped. Its direct parts (`> *`: label, description, check, option
  content, multi-select box) opt out and take HighlightText. With `none` on the row, the shadows
  would have needed `box-shadow: none`. The shared entity-picker sheet is counted twice in the
  budget (picker and multi-picker), and that pushed it over at 14.04 KB. The parts-level rule fits.
  The check takes HighlightText rather than its brand colour forced to CanvasText. A disabled
  selected multi-select box takes the same paired colours, since its muted author colours would
  otherwise show through the opt-out.
- **Grid.** The highlighted cells, the hit headers and the checked rows now set `color:
  CanvasText` under forced colours. Their text keeps its plate and shows as CanvasText on Canvas
  inside the Highlight / SelectedItem fill: a framed label rather than ink on the fill. The rings
  and glyphs the grid draws (the active ring, the find outline, the error outline) use the paired
  overlay ink through `--tm-grid-overlay-ink`. The find and error outlines changed from
  `currentColor` to that variable, since `currentColor` is now CanvasText. Opting out was rejected
  for these reasons:
  - cell content is the consumer's (links, templates, chips, an open editor during range
    editing), and emulated encapsulation cannot reach it to set its colours;
  - under `none` a link would paint its author colour on Highlight, and an editor its author
    field colours;
  - the grid budget has 20 B of headroom.

### Tests

`client/e2e/support/ink.ts` (new) reads a box twice:

- as painted;
- with a scoped sheet that makes its ink transparent and opts the box out of forced colours. That
  keeps the fill and drops any plate.

It then reports two things:

- **ink mass.** For each changed pixel, the share of the ink colour it carries along the line from
  what is under it to the ink, summed, so anti-aliased strokes count in part.
- **fill between.** The unchanged pixels inside the changed pixels' bounding box. A plate changes
  its whole line box and leaves none.

`expectInkOnFill` requires mass ≥ 8 and fill between > a quarter of the span.
`expectInkShows(…, { plateColour: 'Canvas' })` reads the ink against the plate colour instead, for
the grid. Both use soft asserts, so one run reports every box.

`client/e2e/specs/forced-colors-ink.spec.ts` (new) runs each case in both palettes:

- date popup: day, month, year;
- entity picker: label, check;
- select: content, check;
- multi-select: content, box;
- ticks: checkbox, tree, tiles;
- grid range: two cells, a hit row header, a hit column header label, and a record link (LinkText
  on its plate);
- grid list: a checked row cell.

### Proof the checks bite

I ran the spec against the pre-fix library CSS (`git checkout -- client/projects`), with three
mutants on the correct tick rules: `color: HighlightText` → `CanvasText` in the checkbox, the
tree check and the tiles box. The log is `.tmp/pcf54-mutant.log`. Result: 13 of 14 tests failed
and 27 soft assertions failed. Readings are ink / fillBetween / span. "Pre-fix" means plain
pre-fix CSS; "mutant" means pre-fix CSS plus the tick mutants.

**Picker text, pre-fix.** Fill between collapsed to the plate, so all fail in both palettes:

| Case | Pre-fix | After |
| --- | --- | --- |
| day | 120 / 0 / 144 | 23 / 27 / 80 |
| month | 414 / 17 / 468 | 61 / 88 / 250 |
| year | 486 / 1 / 540 | 85 / 82 / 290 |
| entity label | 1145 / 0 / 1216 | 127 / 290 / 704 |
| select label | 460 / 0 / 494 | 62 / 103 / 286 |
| multi label | 460 / 0 / 468 | 48 / 118 / 250 |

The ink mass is high pre-fix because the whole plate is Canvas, and Canvas equals HighlightText.
The fill-between test is the one that separates the plate from ink on the fill.

**Ink mass.**

| Case | Pre-fix or mutant | After |
| --- | --- | --- |
| select check, pre-fix (CanvasText) | 0 | 18 |
| checkbox tick, mutant | 0 | 13 |
| tree tick, mutant | 0 | 5 |
| tile tick, mutant | 0 | 5 |

The tree and tile ticks are about five pixels of ink, so they use `minInk: 4`. Everything else
uses the default of 8.

**Grid, light palette, pre-fix.** HighlightText on a white plate:

| Case | Pre-fix ink | After |
| --- | --- | --- |
| range cells | 5, 1 | 160, 63 |
| hit row header | 0 | 13 |
| hit column header | 4 | 26 |
| checked row cell (SelectedItemText) | 0 | 178 |

The dark-palette grid cases fail only for the range cells (0 / 0). The hit headers and checked
row were already legible there (white SelectedItemText on a black plate). The dark checked-row
test is the one test that passed pre-fix, as it should.

**Correct pre-fix, still guarded.** Entity check 18, multi-select tick 13 and the grid link
(LinkText 126 light, 167 dark) pass both before and after. They guard against a regression: the
check catches an opt-out that would leave a link in its author colour, or a box in its brand
colour.

After the fix I restored the patch and confirmed it matched byte for byte. The post-fix run
(`.tmp/pcf54-postfix2.log`) passed everything except the two small ticks under the old threshold
of 8. Those now carry `minInk: 4`, and the final PR-tier run covers them.

### Existing test changed

`grid-search-page.spec.ts`, "the accent bar takes the ink of whichever cell starts the row",
pinned `bar === cell colour`. That was the old contract, where cell text drew in the paired ink.
The bar still takes the paired ink (SelectedItemText / HighlightText, still asserted), but the
cell's text is now CanvasText on its plate. The three `bar === ink` asserts now read
`ink === CanvasText`.

### Gates

- lint, typecheck and prettier: green.
- Touched unit suites (date-picker, entity-picker, select, grid, checkbox, tree, tiles): 24 files,
  915 tests, green.
- Full PR-tier e2e (`--project chromium --project touch`, PR grep): 1327 passed, 2 failed.
  - `grid-search-page` accent bar: fixed as above; solo rerun green (180 passed).
  - `additions-axe` registry, "the library folder pivot is classified exactly once": environmental.
    This reused slot holds an ignored `client/projects/core/tellma-core-ui/pivot/__screenshots__/`
    folder, left over from the PC-193 branch. `origin/platform-core` has no `pivot` folder. The
    failure is unrelated to this change and does not occur on a clean checkout.

### Sizes

`size:check` after the fix: entity-picker 13.99 / 14 (the tightest), grid 40.70 / 40.73, select
7.26 / 7.59, date-picker 9.43 / 12.84. All are within budget.

### Open risks

- **Linux not run locally.** Linux's Highlight is translucent (`rgba(5,0,73,0.8)`). The reference
  shot keeps the fill as painted, over whatever lies beneath it, and the reads are differential,
  so they should hold. HighlightText is white on Linux, the same as the Windows light palette,
  which the tests cover. CI is the first real Linux run.
- **Select and entity-picker consumer content.** A `tm-option`'s projected content inherits the
  opt-out. A consumer element that sets its own colour (a coloured chip or badge inside an option)
  now shows that author colour on Highlight in forced colours, where before it was forced to
  CanvasText (and invisible on its plate anyway). The showcase has none of these.
- **Grid icons in highlighted cells.** A consumer SVG icon in `currentColor` inside a highlighted
  cell now draws CanvasText on the fill with no plate, at about 1.4:1 (an icon has no plate).
  Before, it drew HighlightText on the fill, which was legible. Text wins over icons here, but a
  status-icon column would lose contrast inside a range. A later fix could plate icons or give
  the grid a hook for cell glyphs.
- **Disabled selected date cell.** It now paints as an enabled selected cell under forced colours
  (HighlightText on Highlight). Before, it was GrayText on a plate. That is a deliberate
  simplification to save bytes: disabled is still announced, and the cell is not actionable.
- **Helper divergence from the sibling.** The sibling's segmented spec carries spec-local
  `showsThrough` / `backdropOf` helpers and adds `paintOver` / `paintedOver` to `pixels.ts`. This
  change uses neither, and builds its own differential read in `support/ink.ts` on the existing
  `pixels.ts` exports (`pixelsOf`, `paintedColour`, `near`). The two do not conflict, but a later
  pass could fold the segmented check onto `expectInkOnFill`.

### What reviewers should examine hardest

1. **The grid design.** It keeps the plate with CanvasText ink instead of opting out. Check the
   reasoning about unreachable consumer content and the editor shown during range editing, and
   whether the overlay-ink change to the find and error outlines is right in both palettes.
2. **The `> *` parts-level opt-out** in `tm-select.css` and `tm-entity-picker.css`. Confirm every
   text-bearing part of a selected row is a direct child of the row in both templates, including
   the entity description and the multi-picker. Confirm the row's forced box-shadow stripping
   still hides the accent and ring.
3. **The date-popup focus and hover rules** on a selected cell. Under `none` the outline is no
   longer forced, so the explicit CanvasText outline colour must stay visible on the Highlight
   ring offset in both palettes.
4. **The ink metric's thresholds.** Check minInk 8 (4 for small ticks), fill between > span / 4,
   and the 24-per-channel line tolerance, for flake risk on another rasteriser.

## Review 1

### Findings

### Responses
