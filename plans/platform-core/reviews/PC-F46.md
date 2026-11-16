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

## Review 1

### Findings

Gates I ran on 9645e838 (logs under `.tmp/r1f46-*`): typecheck; the tiles and image unit specs
(`ng test core-ui --include tiles/** --include image/**`, 6 files, 138 tests); size:check
(`./tiles` 10.31 under 10.32, `./image` 9.69 under 9.72) and api:check; `build:showcase`, then the
tiles e2e on Chromium with `--workers 2` (18 passed) and the tiles touch spec (1 passed). All pass.
The implementer's interleaved harness (`.tmp/f46-pw/probe.mjs`), run against their base build (it
still ships `tm-tiles__link`) and my final build, with api:check loading the host: tasks over 50 ms
base 55, 42, 67 against final 16, 3, 1. The direction of the claim holds. Per D-100 that says
nothing about the 50 ms bar itself.

The move-count test is sound, and no regression hides behind it. I replayed Angular 22's
`reconcile` (copied from `@angular/core`'s `_debug_node-chunk.mjs`) over `ɵtmTilesSpan` and
`ɵtmTilesAssignSlots` in `.tmp/r1f46-sim/`. Over wheel steps of 53-1,000 px in both directions,
alternating steps, sliced and unsliced frames and an off-window active line, the reconciler
detaches exactly the re-bound lines. There is at most one extra detach per render, at line 0
(where the span shrinks and three views are destroyed) or when the active line leaves the
window. `line % slots` fails the test because it reorders the staying lines, which is the
regression the test is meant to catch. Random walks with 20 % large jumps
detach the active line's view in 1,138 of 3,000 walks, against 1,067 of 3,000 for the base's
`track line.index`. So the blur-and-reclaim path that `onFocusOut` now defers to a microtask is
not new to this change.

**R1-1 (major): a focused tile re-bound to the next row in place gets no focus event, so the
keyboard move is silent to assistive technology. Focus can then also be moved by API calls that
must never move it.**
- Where: `client/projects/core/tellma-core-ui/tiles/tm-tiles.ts:798-805` (slot hand-off),
  `:1593-1598` (`afterRender` skips the focus call when the target is already `activeElement`),
  `:1221` (`focusedId` is set only in `onFocusIn`).
- Scenario: focus a tile, wheel the list away (the focused line stays rendered off-window), then
  press ArrowDown or ArrowUp. Rendered lines: the window plus the new active line. The old active
  line leaves, so its slot is the only free one, and `free.pop()` hands it to the new active line.
  Tiles are tracked by `$index` and Up/Down keep the column, so the focused element itself is
  re-bound to the new row. `afterRender` finds `#t<new>` already focused and calls nothing. No
  `focus`/`focusin` event fires, and a screen reader does not announce the new tile.
- Measured with `.tmp/r1f46-pw/focus.mjs` on `tiles-large`, focusing Customer 3 and wheeling 8
  screens away:
  - final: ArrowDown moves to Customer 7 on the same element with no focus events; ArrowUp after
    another wheel-away does the same.
  - base: `out:Customer 3, in:Customer 7` on a new element.
  - This is exactly D-092's condition (focus and a11y semantics must hold under recycling).
- Second effect: `focusedId` stays on the old row, so every later render computes `stale = true`.
  While focus sits on that element, a page's `scrollToRow(x)` or `activeRowId` write (both
  documented as never moving focus) then makes `afterRender` move focus to x's tile.
  - Measured with `.tmp/r1f46-pw/steal.mjs` on the `tiles` story: focus Customer 33, wheel away,
    press ArrowDown (Customer 37, same element), then a script click on "Activate the 41st"
    (`active.set(41)`).
  - final: focus moves to Customer 41.
  - base: focus stays on Customer 37.
- Tests: no test covers it. The unit test and the e2e scroll back into view before pressing an
  arrow, and ArrowRight lands on another element anyway.
- Fix: never hand the focused line's slot to another line in the same render. For example, keep
  the previous active line's slot out of `free` while `focusWithin`, so the entering line takes a
  new slot. Or, in `afterRender`, when the target already has focus but `focusedId` differs,
  blur and re-focus it and set `focusedId`. Add a unit test (focus, scroll away, ArrowDown, then
  assert a `focusin` on the new row's tile) and the same step in the e2e. Every option costs
  bytes, and `./tiles` has 6 B left. This needs a D-031 raise or a saving.

**R1-2 (major): under an unknown stamp the on-screen stand-in can serve bytes older than storage
holds, and the background revalidation then confirms the stored ETag, not the bytes shown, so it
never heals.**
- Where: `client/projects/core/tellma-core-ui/image/internal/blob-cache.ts:259-266`, with `show`
  at `:196-211` and `put`'s `onScreen.delete` at `:353-355`.
- Scenario A, cross-tab:
  - Tab 1 shows `/img/1?size=128` (blob1, stored with `x-tm-etag` e1).
  - Tab 2's revalidation gets a 200 and puts blob2/e2 into the shared, origin-scoped
    `tm-images-v1`. Tab 2's `put` cannot clear tab 1's in-memory `onScreen`.
  - A new tab-1 instance under a null stamp matches the e2 entry but is served blob1 from
    `onScreen`, then revalidates with `If-None-Match: e2`, gets a 304 and keeps showing blob1.
  - Every further instance of that URL in tab 1 also shares blob1, until the last one is hidden.
    Before this change it showed blob2.
- Scenario B, one tab: a known-stamp read's `match.blob()` resolves after a revalidation's `put`
  has cleared `onScreen`. Its `show` then re-registers the older blob as the URL's stand-in.
- This breaks spec 0005 §7.2's healing property ("the next mismatch/revalidation heals"), which
  deviation 5 says is unchanged. This is the open risk the implementer asked reviewers to check.
- Tests: none. The stand-in tests only cover same-tab writes made through this cache.
- Fix: stand in only for bytes that match what storage holds. One way: record the stored ETag
  each served blob was read or written with (a `WeakMap<Blob, string | null>` filled in
  `getImage` and `put`), and use `onScreen.get(url)` only when that ETag equals the match's
  `x-tm-etag`. The other way: drop the stand-in and keep the per-blob and per-known-stamp
  sharing. Add a test that writes the cache behind the instance's back (another tab) and expects
  the stored bytes.

**R1-3 (minor): the perf evidence for shared URLs and src-keyed images comes from a story where
every photo is the same URL.**
- Where: `client/projects/internal/showcase/src/app/tiles/tiles-story.ts:48`. Every third row's
  image is `/test-image.png`.
- On `tiles-large`, two of this change's levers are therefore at their best case:
  - a re-bound tile between two photo rows keeps its `tm-image` (`track src`);
  - every load after a settle shares one object URL, one decode and, through the stand-in, skips
    `match.blob()`.
- With per-row image URLs (the spec's own example, `urls.blob(..., c.imageId, ...)`), each of
  those loads is a CacheStorage read, an object URL and a decode again. The measured share of
  "u" (and part of "r") need not carry over.
- A later quiet-host pass of the nightly probe (D-100) would then not show §12 for a real list.
- Fix: give the large story per-row URLs (for example `/test-image.png?row=${id}`; the static
  server ignores the query) and re-measure base and final interleaved. Showcase only, so no
  budget cost.

**R1-4 (minor): a click whose tile is re-bound in place between press and release opens the
row that is now in that element.**
- Where: `client/projects/core/tellma-core-ui/tiles/tm-tiles.ts:1125-1126` (the press records
  only coordinates) and `:1153` (the click reads the row from the element's current
  `data-index`).
- Scenario: a data change between mousedown and mouseup shifts the rows, for example a live
  refresh that inserts or re-sorts rows above. Lines keep their slots, so the pressed element
  stays where it is and shows another row. The unit test "moves focus with its row when rows
  arrive above the focused tile" shows exactly this re-bind in place. The click still targets
  that element, the slop check passes (the pointer did not move) and the other row opens.
- In the base the pressed row's element moved with its row, so mousedown and mouseup had
  different targets and the click landed on an ancestor that opens nothing.
- Not affected: a scroll between press and release. I checked a 2-line scroll and sliced
  1,000-line jumps from every line with `.tmp/r1f46-pw/press.mjs` and `jump.mjs`. In both final
  and base the release lands on another element.
- Fix: record the pressed tile's row id in `onPointerDown`, and open in `onClick` only when the
  click's row matches it. It costs bytes, so weigh it against R1-1's.

**R1-5 (minor): the Deviations list misses two spec 0023 §8.5 departures.**
- "Lattice lines are windowed by `tmComputeAxisWindow` from `@tellma/core-ui/grid-engine`, the
  grid's own windowing": the lines are now windowed by `ɵtmTilesSpan`
  (`tm-tiles.ts:711-722`). Deviation 3 names only the extra line. The ARCHITECTURE.md sentence was
  changed to match the code, so the spec is now the side out of line and needs a line for Ahmad.
- "Corners are static DOM": the corners are now a `@for` of embedded views (`tm-tiles.ts:398`),
  and the image is a one-entry `@for`. The re-binding makes the cost argument moot, but it is a
  stated spec rule.
- Fix: add both to the commit message's Deviations in the fix commit, with the one-line reason
  each.

The stated deviations, as I judged them:
- **1 (recycled views).** Accepted, subject to R1-1. Stale content is ruled out for every bound
  attribute, and I found no unbound state that survives a re-bind. Flash, the checkbox glyph,
  twisty, spinner, corners and the image are all keyed or bound.
- **2 (sliced jumps).** Accepted.
  - Keyboard paging never slices: `afterRender` sets `scrollTop` and the signal before the scroll
    event, so `sliceFrom` equals the offset.
  - A key pressed while a wheel slice is pending still covers the visible lines: the span is
    clamped to `[first - 6, first]`.
  - A resize, or a data swap that shrinks the count, mid-slice only changes `size` or clips `end`.
- **3 (extra line).** Accepted.
- **4 (shared object URLs).** Accepted. The lifetime is sound:
  - only the last `hide` revokes;
  - a replaced entry leaves the map but lives until its own last `hide`;
  - destroy or a stale token mid-decode releases through `swapIn`'s token check;
  - `setOnScreen` with the same entry nets to zero.
- **5 (stand-in under an unknown stamp).** Not acceptable as it is: R1-2.
- **6 (title `<a>`, `tm-tiles__link` removed).** Accepted.
  - Nothing else in the repo references the class; the harness moved to
    `a.tm-tiles__title[href]`.
  - The marked and context colour rules still win on specificity.
  - The RTL and touch e2e pass.
- **ARCHITECTURE.md sentence.** Accurate for the code. The spec side is R1-5.

No blocking finding. AGENTS.md and PLAN.md §7.1 hold for the changed code: TSDoc on every new
declaration and member, inline section comments, and no spec, `docs/` or PC references in public
text or test names. The gate list in the commit matches what I could re-run. The size
measurement matches. The dist FESMs are newer than every non-spec source in `tiles/` and
`image/`, and they hold the final code (no `tm-tiles__link`, `ɵtmTilesAssignSlots` present). The
gate's own measurer gives `./tiles` 10,561 B against a ceiling of 10,567 B (10.32 × 1024, so 6 B
spare) and `./image` 9,927 B against 9,953 B. The ceilings are unchanged, so there is no D-031
raise. Any fix for R1-1 or R1-4 will need one, or a saving.

### Responses

- **R1-1: fixed** (the second option). In `afterRender`, when the active tile is already focused but
  is stale (it shows another row than the one focus entered on), it is blurred and focused again.
  The move therefore fires `focusout`/`focusin` on the tile, assistive technology reads the new row,
  and `onFocusIn` records it in `focusedId`, so a later page write no longer counts the tile as
  stale. I chose this over keeping the focused line's slot out of `free`. That option needs the
  focused line inside the pure slot assignment, and the same in-place case can still arise from a
  data change, which the `afterRender` check covers too.
  - Unit test "announces an arrow move that re-binds the focused tile in place, and keeps focus
    there": focus tile 5, scroll 1,000 lines away, ArrowDown. It asserts the same element now
    shows the next line's row, that a `focusin` reached it, and that a following
    `activeRowId` write leaves focus on it.
  - e2e step "an arrow move that re-binds the focused tile fires focus, and a page write leaves it"
    (`tiles` story): focus Customer 33, wheel to the end, ArrowDown, record `focusin`, then a
    script click on "Activate the 41st". Focus must stay on the new tile.
  - Both fail without the fix: the unit test was run against the pre-fix code (no `focusin`).
- **R1-2: fixed by dropping the stand-in** (the second option). Under an unknown stamp the stored
  body is always read from storage, as before this change. Per-blob sharing and sharing across reads
  under one known stamp stay. Deviation 5 no longer exists.
  - I first built the ETag-matched stand-in (a `WeakMap<Blob, string | null>` filled on reads and
    puts). It passed its tests, but it cost `./image` 58 B: 9,985 B against 9,953 B.
  - With per-row image URLs (R1-3), the stand-in only helps two instances showing one URL at the
    same moment. That is not worth a raise or a second freshness rule.
  - New tests, both failing against the old stand-in:
    - "leave bytes stored behind the cache (another tab) to an unknown stamp, and revalidate
      those": a direct `put` into `tm-images-v1`, then a null-stamp read must serve those bytes and
      revalidate with their ETag.
    - "never serve bytes read before storage took others, whenever they reach the screen": your
      scenario B.
  - The existing stand-in tests now assert the stored bytes.
- **R1-3: fixed.** `CUSTOMER_TILE` gives each photo its own URL (`/test-image.png?row=<id>`; the
  static server ignores the query), so both tiles stories load one image per row. I re-measured
  base and final interleaved, each built with that story (see "Re-measured after review 1" below).
- **R1-4: fixed**, for the middle click too. `onPointerDown` records the pressed tile's row id with
  the coordinates. `onClick` (open, checkbox and twisty) and `onAuxClick` do nothing when a pointer
  click's row is not the pressed row. A click with no press of its own (`detail` 0) is unaffected.
  - Unit test "opens nothing when the pressed tile shows another row by the release": a row
    arrives above between press and release, for a left click and a middle click. Without anything
    in between, the same taps open the row.
- **R1-5: fixed.** Both departures are now deviations 7 and 8 in the fix commit (below), with their
  reasons.
  7. Spec 0023 §8.5 says lines are windowed by `tmComputeAxisWindow`. They are now windowed by
     `ɵtmTilesSpan`, because the span needs a fixed line count, so every scroll is a pure re-bind,
     and the scroll-start offset for the sliced frame.
  8. Spec 0023 §8.5 says corners are static DOM. The corners are now a `@for` over the corners
     present, and the image a one-entry `@for` keyed by its source. With tiles re-bound in place,
     the views are created once per slot rather than once per row, so the cost argument for static
     DOM no longer applies, and the loop paid for the bytes.

**Re-measured after review 1** (R1-3). Both builds use the per-row image story. Base is
`03d9664a`'s tiles and image with that story; final is `2eb74f98`. The runs are interleaved with
the implementer harness on the same loaded host. Counts are tasks over 50 ms per run of the
probe's 80 wheel steps on `tiles-large`.

| Build | over 50 ms per run | max (ms) | median of those over 50 | CPU-time p97 (ms) |
| --- | --- | --- | --- | --- |
| base | 76, 24, 34, 32 | 3,644, 86, 2,585, 143 | 79, 72, 67, 69 | 97, 77, 78, 83 |
| final | 9, 5, 16, 7 | 74, 73, 136, 72 | 55, 54, 58, 60 | 55, 52, 57, 50 |

With one image per row, final is worse than with the shared picture (1-7 before), as you
expected: each photo row a scroll brings in is a storage read, an object URL and a decode again.
The reduction against base still holds at 3-8x. The 50 ms bar itself is still not shown to hold
on this host, as already reported. The next lever is unchanged: re-bind `tm-image` in place.
`./tiles` now has 22 B left under 10.35.
