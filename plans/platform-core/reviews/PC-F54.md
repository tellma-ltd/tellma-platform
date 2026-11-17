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
- **Select and entity-picker consumer content (accepted risk, per the orchestrator after Review
  1).** A `tm-option`'s projected content inherits the opt-out. A consumer element that sets its
  own colour (a coloured chip or badge inside an option) now shows that author colour on
  Highlight in forced colours, where before it was forced to CanvasText (and invisible on its
  plate anyway). tm-menu's active item already behaves the same way, and the showcase has no
  such option. Accepted as is.
- **Grid glyphs in highlighted cells (closed in Review 1, R1-1).** The first commit left
  `currentColor` glyphs at CanvasText on the fill, about 1.4:1. Glyphs now take the paired ink.
- **Disabled selected date cell (closed in Review 1, R1-4).** It is GrayText on its plate again.
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

Scope: commit dbadf1ac. I read the six stylesheets, the option/entity/date/tree-grid templates,
`e2e/support/ink.ts`, `e2e/specs/forced-colors-ink.spec.ts` and the grid-search-page edit. I
grepped the library for `HighlightText`, `SelectedItem(Text)`, `Highlight`, `Mark`, `Canvas` and
`forced-color-adjust`. A scratch Chromium probe (outside the repo) under `forcedColors: 'active'`
checked two things. A non-system author `color` inside a forced block computes to CanvasText
whatever its parent's colour. A disabled `<button>` opted out with HighlightText on Highlight
computes HighlightText, with no GrayText. The library patch is byte-identical to
`.tmp/pcf54-fix.patch`, which the `size:check` log measured. Run: `CI=1 node scripts/e2e.mjs
--project=chromium` on the two touched specs, logged to `.tmp/pcf54r1-e2e2.log`: 63 passed,
including all 14 forced-colors-ink tests. This was Windows only; Linux was not run.

Verdict on the fixes:

- **Pickers and date popup.** These are correct on both palettes. The text lives in the line boxes
  of the opted-out parts. In both templates every text-bearing part (label with its `<bdi>` runs
  and the separator, description, check, option content, multi-select box) is a direct child of
  the row. The row keeps `auto`, so its accent and ring shadows are still stripped. On Linux's
  translucent Highlight, the fill blends over the Canvas panel, which makes it darker under white
  HighlightText. The multi-select box's second Highlight layer only darkens it further.
- **The date-popup focus ring.** The selected cell's CanvasText outline sits 2 to 4 px outside the
  cell, on the Canvas popup, so it shows.
- **The grid's plate choice.** It meets spec 0004's forced-colors rule, because the selection,
  active and error states stay distinct. The fill still shows around each plate, and the rings,
  bar and outlines keep the paired ink. The change of the find and error outlines to
  `--tm-grid-overlay-ink` is behaviour-neutral: that variable is unset on plain cells (fallback
  CanvasText) and equals the old `currentColor` on the range fill and on checked rows. The
  consumer-content risk in tm-option is acceptable: tm-menu already opts its active item out the
  same way, and before this change that content was invisible anyway. It should be recorded,
  though (see the summary).

### Findings

**R1-1 (major): the grid fix trades legible cell glyphs for legible cell text, and the commit's
Deviations leave that trade out.**

- **Where:** `grid/internal/grid-view.css:631-670`, `grid/internal/grid-end-cell.css:115-125`.
- **Scenario:**
  - Before, a `currentColor` glyph inside a highlighted cell inherited HighlightText or
    SelectedItemText and drew it straight on the fill, with no plate, so it showed. It now
    inherits CanvasText.
  - In the range fill that is about 1.4:1 on Windows light (black on `rgb(55,0,110)`) and on
    Windows dark (white on `rgb(26,235,255)`). On Linux it is about 1.8:1 (black on
    `rgba(5,0,73,0.8)` over white). That fails WCAG 1.4.11 for any meaningful glyph.
  - Affected glyphs: a status, flag or attachment `tm-icon` in a display template, which is a
    common ERP column, and the library's own sort arrow in a hit column header.
  - The sort arrow inherits `.tm-grid__colhdr--hit`'s colour. Its contrast drops from about 5.4:1
    to 3.9:1 on Windows light: still above 3:1, but lower.
  - This is a regression of behaviour that was correct. The notes list it only under "Open
    risks", and the commit message's Deviations list does not have it. PLAN.md §7.1 item 1 needs
    it there.
- **Fix:** either one closes it.
  - (a) Give glyphs the paired ink through an inherited custom property. Encapsulation cannot
    stop it: the grid already sets `--tm-grid-overlay-ink` on every highlighted cell, and
    tm-icon's own forced-colors rule can read it, for example
    `:host { color: var(--tm-grid-overlay-ink, inherit) }` (inside the media block). The open
    editor resets it, for example `.tm-grid__editor { --tm-grid-overlay-ink: initial; }`, so an
    icon in an editor keeps FieldText. This may need a grid ceiling raise under the budget
    process, since only 20 B are free.
  - (b) Keep the code. Add the icon regression to the commit's Deviations, add a LEDGER follow-up
    item, and refer the spec 0004 trade-off to Ahmad.

**R1-2 (minor): the grid "on its plate" checks do not prove a plate, so they pass for CanvasText
drawn straight on Highlight.**

- **Where:** `e2e/support/ink.ts:150` (plate branch) and its callers at
  `e2e/specs/forced-colors-ink.spec.ts:160-185`.
- **Scenario:**
  - With `plateColour`, each changed pixel's share is measured against Canvas, not against the
    pixel actually beneath it.
  - Suppose a later change opts the cells out (`forced-color-adjust: none`) and keeps
    `color: CanvasText`. Text then draws black on purple at about 1.4:1, which is illegible.
  - Its solid glyph pixels are still exactly CanvasText, so each carries share 1. Only the
    anti-aliased edges fall off the Canvas-to-CanvasText line.
  - A 13 px label therefore still clears `minInk` 8 easily, and every grid case passes. The test
    names promise "text on its plate", but nothing asserts the plate.
- **Fix:** in the plate branch, also assert that the plate is there. One way is
  `fillBetween < span / 4`, the inverse of `expectInkOnFill`, since a plate leaves no fill between
  the strokes. Another is to read each pixel's share against the hidden shot wherever that pixel
  is not Canvas.

**R1-3 (minor): the small-tick threshold has about one anti-aliased pixel of margin and has not
been measured on a Linux rasteriser.**

- **Where:** `e2e/specs/forced-colors-ink.spec.ts:136`.
- **Scenario:**
  - `.tmp/pcf54-postfix2.log` reads the tree and tile ticks at ink 5 (14 changed pixels). The
    threshold is `minInk: 4`.
  - These are sub-pixel strokes of about 0.7 px, read as blends. A different anti-aliasing or
    hinting result on the Linux CI runner can move the sum by one, which turns this test red
    there. That stops the line for a flake.
- **Fix:** read the two small ticks with more pixels, for example with `deviceScaleFactor: 2` in a
  `test.use` for that test or describe block. Mass scales by about 4, to roughly 20, while the
  mutants stay at 0. Then set the threshold well clear of both, for example 10.

**R1-4 (minor): a disabled committed day loses its disabled cue under forced colours, and the
reason given (saving bytes) does not apply.**

- **Where:** `date-picker/internal/tm-date-popup.css:242-247`.
- **Scenario:**
  - When the committed date is outside `tmMinDate`/`tmMaxDate`, the selected day is also disabled.
  - Outside forced colours it shows a muted figure on the primary fill, because
    `.tm-date-popup__day:disabled` (0,2,0) outranks `--selected` (0,1,0).
  - Under forced colours the opt-out now paints it exactly like an enabled selection: HighlightText
    on Highlight. The probe confirmed that a disabled button that has opted out gets no GrayText.
  - Spec 0005 requires calendar states to survive forced colours through system colours. The
    stated reason is bytes, but date-picker is at 9.43 of 12.84 KB.
- **Fix:** after the hover rule, put the disabled selected cell back on its plate:
  `.tm-date-popup__cell[aria-selected='true']:disabled { forced-color-adjust: auto; color: GrayText; }`.
  - That restores the pre-fix rendering, GrayText on a Canvas plate inside the Highlight fill,
    which was legible.
  - The rule is (0,3,0), placed later, so it also wins over the `:hover` rule.
  - Do not use GrayText on the fill under `none`: that is about 2.9:1 on Windows light and green
    on cyan on Windows dark.

**R1-5 (minor, pre-existing; the sweep missed it): the tree grid's expander chevron and its
lazy-load spinner draw CanvasText on the range fill.**

- **Where:** `tree-grid/tm-tree-grid.css:63-73`. The file has no forced-colors block.
- **Scenario:**
  - `.tm-grid__expander` sets `color: var(--text-secondary)` and the spinner sets
    `color: var(--color-primary)`. Forced colours turn both into CanvasText (probe confirmed),
    whatever the cell's colour.
  - In a hierarchy cell inside the range fill, the chevron is therefore about 1.4:1 on Windows
    (both palettes), before and after this change.
  - This contradicts the rule this commit writes into grid-view.css, "the rings and glyphs ... keep
    the paired ink". The sweep was asked to cover the whole library.
- **Fix:** add this rule:
  `@media (forced-colors: active) { .tm-grid__expander, .tm-grid__childspin-spinner { color: var(--tm-grid-overlay-ink, ButtonText); } }`
  - The variable inherits from the cell, and tree-grid has about 100 B of headroom (2.94 of
    3.04 KB).
  - Add an ink case on a tree-grid range. Alternatively, defer the fix to a follow-up item with a
    LEDGER note.

### Responses

- R1-1: fixed in the "PC-F54: review 1 fixes" commit (child of dbadf1ac).
  - **Glyph rule.** tm-icon's forced-colors block sets
    `.tm-icon > .tm-icon__svg { color: var(--tm-grid-overlay-ink, currentColor) }`. It sits on the
    glyph rather than the host, so a consumer's colour class on the icon (forced to CanvasText
    anyway) cannot undo it. Outside a highlighted grid surface the fallback keeps the host's colour
    exactly as before.
  - **Spinner.** tm-spinner's arc reads the same ink (`:host { color: var(--tm-grid-overlay-ink,
    CanvasText) }`; CanvasText is what the brand colour was forced to). This also covers the
    grid's own cell spinner.
  - **Resets.** Surfaces that paint their own fill inside a cell reset the ink with
    `--tm-grid-overlay-ink: initial`, so their glyphs keep their own ink instead of drawing
    HighlightText on their Canvas or ButtonFace:
    - the open editor (`.tm-grid__editor`, as suggested);
    - `.tm-button`;
    - tm-chip and tm-badge hosts.
    Without these the change would have turned a remove glyph in a chip, or a button's icon in a
    highlighted cell, invisible.
  - **Size.** No budget raise was needed. grid is 40.71 / 40.73 (+0.01, the editor reset), icon
    2.38 / 2.6, spinner 0.65 / 0.82, chip 5.23 / 5.29, badge 1.81 / 1.92, button 1.48 / 1.65.
  - **New check.** "a sort arrow on a hit column header shows on the highlight" (SelectedItemText
    on the fill; the library's own sort glyph). On dbadf1ac's CSS it reads ink 0 on the light
    palette and fails. On the dark palette CanvasText and SelectedItemText are both white, so it
    passes there, correctly. The tree-grid chevron check under R1-5 covers a cell glyph in the
    range fill. No story has a consumer icon in a cell, so that path is covered by the same rule
    through those two library glyphs.
- R1-2: fixed in the "PC-F54: review 1 fixes" commit.
  - **Change.** `readInk` also reports `plateBetween`: the pixels inside the ink's span that show
    the plate colour as painted. When a plate colour is named, `expectInkShows` also requires it
    to exceed a quarter of the span.
  - **After.** Plate reads give 37–943 of 98–1292.
  - **Mutant.** I added `forced-color-adjust: none` to the hit-header, checked-row and range-fill
    rules, keeping `color: CanvasText`. Every plate case failed the new assert with
    plateBetween 0 in both palettes, including the link. The ink-mass assert alone passed most of
    them (ink 43–154), as the finding predicted.
- R1-3: fixed in the "PC-F54: review 1 fixes" commit.
  - **Change.** The ticks test moved into a describe with `test.use({ deviceScaleFactor: 2 })`.
    Its threshold is `TICK_INK = 10` for all three ticks.
  - **At 2x.** Tree and tile read 28 each (45 changed pixels); the checkbox reads 57–58.
  - **Mutants.** With the CanvasText tick mutants all three read 0.
- R1-4: fixed in the "PC-F54: review 1 fixes" commit.
  - **Rule.** `.tm-date-popup__cell[aria-selected='true']:disabled { forced-color-adjust: auto;
    color: GrayText; }` goes after the focus rule, so it also outweighs hover by order.
  - **Bounds reality.** The day view clamps its view into the bounds, so a disabled selected day
    is not reachable there. A disabled selected month is reachable: commit 2026-04-10 in the
    March-only picker and open the month view. The rule covers every cell.
  - **New check.** "a selected month outside the bounds shows its name in GrayText on its plate"
    asserts GrayText ink on a Canvas plate. After: ink 27 / 54, plate 206 / 218. On dbadf1ac's CSS:
    ink 4 / 5 and plate 33 / 36 of 264–276, so it fails both asserts.
- R1-5: fixed in this item, in the "PC-F54: review 1 fixes" commit.
  - **No separate tree-grid rule.** The expander's glyph is a tm-icon and the lazy-load spinner a
    tm-spinner, so the R1-1 rules give both the paired ink. tm-tree-grid.css is unchanged.
  - **New check.** "the chevron of a tree row in the range fill shows on the highlight" reads the
    first row's chevron inside a 2 x 2 range. After: ink 12. On dbadf1ac's CSS: ink 1 in both
    palettes, which fails.
  - **Spinner not pixel-read.** The spinner only shows during a load, so it is not read from
    pixels. Its colour comes from the same rule.
- Open risk (consumer colours in a selected tm-option): left as is, per the orchestrator, and
  recorded under the implementer notes' open risks as an accepted risk.
- Gates for this round:
  - lint, typecheck, prettier, build, size:check, api:check and `pnpm run test`: green. The unit
    runs had 2541 + 484 + 28 + 22 + 2 tests.
  - forced-colors-ink: 20 passed after the fix. On dbadf1ac's CSS plus the plate and tick mutants
    (`.tmp/pcf54-r1-mutant.log`), 11 of 20 failed, and every new or tightened assert failed.
  - Touched e2e specs with `--project=chromium --project=touch` and the PR grep: 248 passed,
    0 failed. The specs were forced-colors-ink, grid-search-page, additions-forced-colors, badge,
    button, chip, date-picker, icon, grid-a11y(-editable), grid-glyphs, tree-grid(-restore),
    basics-glyphs and glyphs.
