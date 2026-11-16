# PC-F46: tiles: cut the 10,000-tile scroll cost (reused tiles, sliced jumps, shared image URLs)

## Implementer notes

### What was built

Profiling first (Chromium traces and CPU profiles of the @perf probe's 80 wheel steps on
`tiles-large`, scratch harness under `.tmp/f46-pw/`) showed that the cost left after fix 1 was
creating about 36 tiles per step: each new tile made its views, its `tm-image`, an object URL and
a decode. Three changes remove it, and the measurements below show that each one alone is not
enough.

- **Reused tiles** (`tiles/tm-tiles.ts`, `tiles/internal/tm-tiles-lattice.ts`).
  - The rendered lines are tracked by slot. `ɵtmTilesSpan` returns the span to render: as many
    lines as the viewport shows, one more for the part-lines at its edges, and three of overscan
    on each side. `ɵtmTilesAssignSlots` (from the last render's slots, kept on the component) keeps each
    staying line's slot and gives the lines that enter the slots of those that left, the last to
    leave first. A jump therefore re-binds lines in place instead of destroying and creating views.
  - The last-out-first-in order matters as much as the reuse. The lines that enter at one end take
    the slots of those that left the other end in mirror order, which is the order Angular's `@for`
    reconciler can apply by moving only the re-bound elements, in either direction. An earlier
    version of this change used `line % slots`, which is smaller, but on every scroll **up** the
    reconciler detached and re-inserted the lines that stayed (10 of 16 per screen) instead of the
    ones re-bound: the probe measured layout and style work per frame at about twice the
    last-out-first-in version, and 40-80 tasks over 50 ms, worse than the base. A new unit test
    counts line moves in both directions so that cannot come back.
  - The active line keeps its slot wherever it is (it stays rendered off-window, as before).
  - Lines are tracked by slot. Built-in tiles are tracked by their position in the line (which is their column, so
    `aria-colindex` now reads `$index`), so a line's tiles re-bind
    too. A custom body (`tmTileContent`) is tracked by row id: its views can hold state, so they
    are never re-bound to another row.
  - The tile `tm-image` is in a one-entry `@for` keyed by its source, with the placeholder as
    `@empty`. A tile re-bound to another row's image gets a fresh `tm-image`, so it never shows
    the previous row's picture while the new one loads.
  - Everything else in a tile is a binding, so a re-bound tile re-renders its row: title, href,
    corners, classes, `aria-selected`, `aria-colindex`, the checkbox, the tree attributes on the
    line, and the element id (`…-t<index>`, which `aria-activedescendant` names).
  - DOM order stays line order (the `@for` keeps the order of `lines()`), so the reading order and
    the roving tab stop are unchanged.
- **Focus follows the row, not the element.** `onFocusIn` records the focused row. After each
  render, a focused tile now showing another row moves focus to the active tile, exactly like a
  render that drops focus onto `<body>` already did. Focus therefore stays on its row when rows are
  inserted above it or the page moves the active row far away, and scrolling the focused tile away
  and back keeps it on the same element (the active line keeps its slot).
  - With slot keys, the `@for` can reorder line views: when the off-window active line comes back
    into the window, the reconciler detaches and re-attaches its view, and Chromium blurs the
    focused tile on the way (a `focusout` with no `relatedTarget`). `onFocusOut` therefore no
    longer ends the focus reclaim at once: it decides in a microtask, from where focus actually
    is. The render's own `afterRender` runs first, in the same task, and puts focus back on the
    active tile. A real blur (a click outside, Tab away) still ends it before any later render.
- **Sliced jumps.** `onScroll` records the offset the scroll started from for one task (held under
  `PendingTasks`, cleared by a `setTimeout`). In that task the span moves from where it was only as
  far as the visible lines need, keeping its length, so the scroll's own frame re-binds only lines
  it already has. The overscan follows in the next task. A screen-sized jump becomes two tasks of
  about half the work.
- **Shared object URLs** (`image/internal/blob-cache.ts`, `image/tm-image.ts`).
  - `ɵTmImageBlobCache.show(src, blob, version)` and `hide(shown)` put a blob on screen with one
    object URL and one off-DOM decode for every instance. They are reference-counted, and the last
    `hide` revokes the URL.
  - Instances share when they show the same `Blob`, or the same sized URL under the same known
    stamp (one stamp is one set of bytes, whichever storage read produced the `Blob`).
  - Under an unknown stamp, the blob on screen for the URL stands in for the stored body, so its
    URL is shared. The background revalidation still runs with the stored ETag, and a refreshed blob
    gets its own URL while the old one lives until its last instance lets go.
  - `tm-image` keeps its hold in `onScreen`. `setOnScreen` replaces the old `setDisplayUrl`, so
    the destroy, failure and empty-src paths release their hold instead of revoking.
- **Smaller template** (to fit the unchanged 10.32 KB ceiling).
  - The title is one `<a class="tm-tiles__title">`. With an href it is the link (`tabindex="-1"`,
    as before). Without one it has no href and no tabindex, which makes it a plain inline element.
    The separate `tm-tiles__link` class is gone; the CSS underlines `.tm-tiles__title[href]:hover`.
    The `<a>` is a grid item, so it would stretch across the title's column; `justify-self: start`
    with `max-inline-size: 100%` keeps it as wide as its text (the old inline link's hit area) and
    still ellipsized at the column. Without it the PR-tier touch test failed: Chromium's touch
    adjustment snapped a tap on the code corner 10 px up towards the wider link, the click landed
    beyond the 4 px tap slop, and the tile did not open.
  - The three corners render from one `content.corners` list in a `@for` (each entry carries its
    `place`, which sets its `tm-tiles__corner--<place>` class), instead of three copies of the
    markup.
  - `tmComputeAxisWindow` is no longer imported: the span arithmetic is the lattice's own
    (it needs a fixed line count and the scroll-start offset).
  - `ɵtmTilesSpan` takes its geometry as plain arguments, the corner helper normalizes a string
    to the object form once, and the stacked layouts' `column-gap: 0` rule is gone (stacked
    layouts always have one column, so it changed nothing).
- **ARCHITECTURE.md**: the tiles sentence said tiles "shares the windowing" of `grid-engine`. It now
  says tiles shares the tree resolution and windows its own lines with re-bound slots.

### Tests

- Lattice: `ɵtmTilesSpan` (lines per viewport, overscan, clipping at both ends, the scroll-start
  span moving only as far as needed) and `ɵtmTilesAssignSlots` (slots kept, last-out-first-in in
  both directions, new slots past every slot in use, the active line kept far off-window).
- `tm-tiles.spec.ts`, new `reused tiles` block. Each test fails if reuse breaks the property it
  names:
  - every rendered tile true to its row (title, code corner, `aria-selected`, `aria-rowindex`,
    `aria-colindex`, id, tab stop) across a sweep of jumps over 10,000 rows with a selection, and a
    selection toggled on a re-bound tile;
  - the same line and tile elements before and after scrolls of 2, 9 and 1,900 lines (reuse
    itself);
  - at most six line elements moved per six-line scroll, down and back up (the seven that stay
    never move);
  - the sliced frame: lines 54-66 from the same elements, then 57-69 after the next task;
  - focus kept on its row scrolling away and back, then ArrowRight moving on from it;
  - focus moving with its row when rows are inserted above it;
  - focus never left on a re-bound tile when the page moves the active row 8,000 rows away;
  - a fresh `tm-image` (instance compared) for a tile re-bound to another row's image;
  - a custom body never re-bound;
  - a tree sweep: level, position, set size and expansion true to each row.
- `tm-tiles.spec.ts`: the title link is as wide as its text, and a long title still ends at its
  column with an ellipsis.
- `blob-cache.spec.ts`, new `blobs on screen` block: one URL per blob revoked on the last hide;
  sharing under the same stamp across storage reads but not across stamps or unknown stamps; the
  stand-in under an unknown stamp still revalidating with the stored ETag; a refreshed blob taking
  over the URL while the old one waits for its own hide.
- `tm-image.spec.ts`: two boxes showing one blob share one object URL, revoked with the last.
- e2e `tiles.spec.ts` (PR tier): wheel 12 screens down and back on `tiles-large` with two rows
  selected and tile 3 focused. After each step every gridcell's title, code and `aria-selected` must
  match its own row and focus must still be on Customer 3; then ArrowRight focuses tile 4.

### Measurements

All on the same Windows host, which other worktrees kept busy throughout (the quarantined grid
`@perf` probe, which this change does not touch, failed alongside with 7-9 tasks over 50 ms, and
single 2.2-2.9 s stalls appear in base and final runs alike). Counts are tasks over 50 ms per run
of the probe's 80 wheel steps on `tiles-large`.

**Scratch harness, interleaved base and final runs** (`.tmp/f46-pw/probe.mjs`, the probe's steps
against two production showcase builds served side by side, alternating per run):

| Build | over 50 ms per run | max (ms) | median of those over 50 | CPU-time p97 (ms) |
| --- | --- | --- | --- | --- |
| base (before this change) | 37, 35, 35, 67 | 127, 85, 90, 115 | 69, 65, 61, 72 | 88, 78, 74, 93 |
| final | 7, 1, 4, 2 | 75, 57, 90, 55 | 61, 57, 76, 55 | 48, 46, 46, 47 |

The median task fell from 32-56 ms to 26-31 ms and the 90th percentile from 66-84 to 39-47.

**The `@perf` probe itself** (`CI=1 node scripts/e2e.mjs --project chromium --grep @perf --retries 0`):
fails in all three runs with 4, 19 and 26 tasks over 50 ms (51-85 ms). That command runs the grid
probe in parallel on the second worker. Run alone (`--grep "@perf scrolling 10,000 tiles" --workers 1`)
it fails with 2, 10 and 7 (51-89 ms, plus one 2,529 ms stall). The base under the same command
was 6-28 on a quieter host before this change.

**So the bound is not yet shown to hold stably on this host.** The work per step is now about half
the 50 ms budget at the median, but the tail on a loaded host still crosses it. In a quieter
window earlier in this work the same design (before the size trims, which change no runtime path)
measured 0 and 1 over 50 ms against 42 and 40 for the modular-slot version and 23-65 for the base.

**Which change does what** (earlier interleaved runs of builds with each change behind a flag;
r = reused tiles, s = sliced jumps, u = shared object URLs):

| Variant | over 50 ms per run |
| --- | --- |
| base | 23, 13, 57, 25 / 46, 58, 38, 65 |
| r | 2, 18, 12, 46 |
| rs | 3, 0, 11, 7 |
| ru | 1, 0, 45, 66 |
| rsu | 0, 16, 33, 22 / 1, 0, 5 / 3, 7 / 0, 3 |
| modular slots (`line % slots`) | 62, 65, 68, 79 / 80, 101, 68 / 44, 59 / 42, 40 |
| last-out-first-in slots | 0, 1 |
| title wrapped in a span (no CSS change) | 61, 59 |
| title link stretched (CSS reverted) | 72, 69, 73 |

The modular-slot regression was traced (Chromium traces) to layout and style work at about twice
the last-out-first-in version (1,621 ms against 826 ms of layout; 635 against 324 dirty objects per
layout): the reconciler moved 235 line elements over ten upward steps against 86.

### Gates

- `pnpm run` lint, typecheck, lint:test, tokens:check, build, test (full run earlier; the tiles
  suites, 80 tests, re-run after the last change), api:check (one approved change:
  `protected onFocusOut(): void`), docs:build, size:check (`./tiles` 10,561 B = 10.31 under 10.32,
  `./image` 9,927 B = 9.69 under 9.72): all pass.
- The move-count test fails against modular slots (20 moves against at most 6), as does the
  focus-kept test; both pass with the shipped assignment.
- PR-tier e2e (`chromium` + `touch`, `--workers 2`, after `build:showcase`): 1,109 passed.
- No .NET code changed.

### Deviations

1. **Tiles are tracked by line slot and position in the line, not row id** (spec 0023's tiles keep their views
   per row by implication of the grid-like `@for`; nothing in §12 requires it). Custom bodies stay
   tracked by row. D-092 allows this if focus, selection and a11y hold, which the tests above pin.
2. **A scroll's own frame holds back the overscan** to the next task (one more task per scroll,
   under `PendingTasks`, so `whenStable` and the harness still see the settled render).
3. **One extra rendered line**: the span holds the lines the viewport can show plus one, plus the
   overscan (13 lines for a 420 px viewport at a 70 px pitch, against 12 before), so the rendered
   line count stays fixed as the offset moves and every scroll is a pure re-bind.
4. **Spec 0005 §7.4** (each instance creates and revokes its own object URL): object URLs are shared
   per blob, or per sized URL and stamp, and revoked by the last instance showing it.
5. **Spec 0005 §7.2** (an unknown stamp serves the stored body): when the URL's blob is on screen it
   stands in for the stored body. The revalidation is unchanged.
6. **The title and its link are one `<a>`.** Without an href it is a plain inline element with no
   tabindex. The `tm-tiles__link` class is removed.

### Open risks

- **The 50 ms bound on a loaded host.** The probe counts wall-clock long tasks, and on this busy
  host the final build still produces 1-26 tasks over 50 ms per run (see Measurements). It needs
  a run on a quiet host or the CI runner to settle whether the remaining tail is host noise or
  work still to remove. The next lever would be re-binding `tm-image` in place instead of a fresh
  instance per re-bound image, which needs its own budget (the tiles entry point has 6 B left).
- **Same source, different rows.** The image is keyed by its source: two rows with the same image
  URL keep one `tm-image` across a re-bind. That is the same image, so nothing wrong shows, but an
  `etag` that differs between them reloads it in place.
- **Unknown-stamp stand-in.** Under a null stamp the on-screen blob may be newer than storage (a
  refresh landed and was put). That is the intent; reviewers should confirm no path serves an older
  on-screen blob than storage holds.

### What reviewers should examine hardest

- `ɵtmTilesAssignSlots` and the `@for` keys: uniqueness when the span shrinks and grows (a
  viewport resize, the lattice ends), with the active line outside the window, and the
  last-out-first-in order under the sliced frames (the move-count test pins six-line scrolls only).
- `afterRender`'s stale-focus check: every path that re-binds a focused tile (scroll, insert above,
  data replaced, active moved by the page) must end with focus on the active tile, and no path may
  steal focus from a tile that still shows its row.
- `ɵTmImageBlobCache.show`/`hide` reference counting through `swapIn` (stale tokens, decode
  failure, refreshes, destroy mid-decode) and the `onScreen` map entry when a refreshed blob
  replaces it.
