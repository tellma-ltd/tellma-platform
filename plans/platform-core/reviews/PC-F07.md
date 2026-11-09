# PC-F07: wait for the grid menu's items before reading them

## Implementer notes

### Root cause

CI runs 42532168842 (a6c928d) and the run on 35f367b each had one flaky test: grid-menu "items ›
a 3-row selection pluralizes the row items". It failed first, then passed on retry, and
`failOnFlakyTests` fails such a run. The helper `menuLabels` read the labels with
`allInnerTexts()`, a one-shot snapshot that does not wait. The right-click opens the menu from
its `contextmenu` handler by setting a signal. The overlay and its items render on the next
change-detection pass, which can land after `click()` resolves. The snapshot then read `[]`.
The first items test ("the localized built-in items render …") had the same race after
Shift+F10.

### Product check

The product renders correctly. A probe (scratch spec, not committed) watched the DOM with a
MutationObserver. When the `.tm-menu__panel` was first inserted, it already held all seven
labels: Cut, Copy, Copy with headers, Paste, Insert 3 rows above, Insert 3 rows below and Delete
3 rows. That held in 10/10 runs. The panel and its items come from one embedded view, created in
one pass, with labels computed from the current selection. Rendering a pass after the gesture is
how a signal-driven overlay renders, so there is nothing to make synchronous. No product code
changed.

### What changed

- `grid-menu.spec.ts`: `menuLabels(page)` is now a locator (TSDoc says why). Both items tests
  assert `await expect(menuLabels(page)).toHaveText([...])`, which retries until the open menu
  shows the items. The pluralization test now pins the full seven-label list instead of three
  `toContain`s. That is stronger: it also proves the singular labels are gone.
- Sweep for the same pattern, a one-shot DOM read right after an action:
  - `support/grid.ts` `cellText()` (innerText snapshot, 63 call sites). It now waits for
    `appStable` before reading, as `modelJson()` already does, so value captures read the settled
    text. A new `expectCellText(page, row, col, text)` asserts web-first
    (`toHaveText(text, { useInnerText: true })`). All 39 assertion sites of the form
    `expect(await cellText(…)).toBe(…)` now use it, across grid-clipboard-synthetic,
    grid-editing, grid-entity, grid-errors, grid-find, grid-keyboard, grid-paste-resolution,
    grid-virtualization and tree-grid. The one `toContain` site (grid-keyboard) is now
    `toContainText(…, { useInnerText: true })`. The conversion was mechanical, by a script that
    matched only that form. Imports were updated, and unused `cellText` imports dropped.
  - `files.spec.ts` `readout()`: a `textContent` JSON snapshot read straight after a synthetic
    drop. A read before the render saw `none`, and `JSON.parse('none')` throws. The two drop tests
    now `expect.poll(() => readout(page)).toEqual({...})`. `readout()` returns `null` while the
    output reads `none`, so the poll retries instead of throwing.
  - `entity-picker.spec.ts`:
    - "the portaled ARIA id chain resolves …" and "an arrowed row Tab-commits the row the user
      chose …" read `aria-activedescendant`, which the highlight's after-render effect sets. Each
      now waits for `appStable` before reading the id.
    - In the second test, a premature `null` would have made
      `not.toHaveAttribute('aria-activedescendant', '')` pass at once and let Tab race the arrow.
  - `select.spec.ts`: three `aria-activedescendant` reads, after open (two) and after typeahead
    (one). Each now waits for `appStable` before reading.
  - `grid-virtualization.spec.ts` (four sites) and `tree-grid.spec.ts` (one) count the rendered
    window after a row-count change. Each now waits for `appStable` before counting.
  - `grid-mouse.spec.ts`: the drag's selected-cell count is polled
    (`expect.poll(…).toBeGreaterThan(1)`).
  - `alert.spec.ts` and `button.spec.ts` forced-colors loops iterated over `count()` read right
    after `goto`. An unrendered story gave zero iterations, a vacuous pass. Each now first asserts
    `toHaveCount(4)`.
- tree-grid lazy loading, found by the stress run below and fixed the same way as in PC-F02.
  "expanding the lazy root shows the reserved-slot spinner …" failed 2/20. Both failures were the
  transient-window class:
  - once the spinner was never seen inside the 800 ms delay;
  - once the load had landed before "not expanded while the load is in flight" was asserted.

  The fix:
  - The tree-grid story's loader now waits on a held-response gate
    (`<app-held-responses name="loader">`, the same `HeldResponses` the picker stories use).
  - That test and "re-collapsing during the load wins …" hold the load, assert the in-flight state
    (or re-collapse during it), then release it.
  - The re-collapse test used to rely on its second click landing inside a 600 ms delay. It could
    pass vacuously by collapsing a node that had already loaded. Now the click provably lands
    during the load.
  - PC-F02's open risk named this spec as a candidate. The two other candidates it named,
    file-preview's slow loader and grid-paste-resolution's 1500 ms windows, did not fail here and
    are unchanged.
- Checked and left:
  - date-picker's heading and roving-day reads, which come after web-first barriers;
  - file-preview's `count() === 0` absence checks, which come after a positive wait on the same
    render;
  - grid-a11y-editable's and input's `aria-describedby` reads, which come after attribute waits
    or point at persistent elements;
  - grid-errors' `overlayMessage`, which is poll-friendly and polled;
  - `modelJson`, `model` and `searchCalls`, which already call `appStable`;
  - the remaining `cellText` value captures, which now settle first.

### Verification (local Windows, CI=1 prod build)

- One pass over the 293 Chromium tests in the 18 touched or `cellText`-dependent specs: 293/293.
- Those specs, plus grid-selectable (which the `select` filter also matched), `--repeat-each 20
  --retries 0`, default 10 workers, with traces: **5856/5860 passed** (1.1 h). The 4 failures:
  - tree-grid lazy loading ×2: fixed above, then re-run as below.
  - grid-clipboard "two Ctrl-selected full rows compact into one aligned copy" ×2. One timed out
    in `beforeEach` behind the cross-worker real-clipboard lock. The other timed out waiting for
    the OS clipboard write. These tests drive the real Windows clipboard under a global lock, and
    20 repeats at 10 workers queue on that lock past the 60 s budget. PROCESS.md §11 records the
    real-clipboard tests as unreliable on local Windows and passing on Linux CI. The spec only
    changed by the `cellText` settle, and the failing test does not call `cellText`, so this is
    unrelated and not chased.
- tree-grid after the gate, `--repeat-each 20 --retries 0`: **380/380 passed** (7.2 min).
- PR tier (`--project chromium --project touch --grep-invert "@nightly|@perf|@quarantine"`, CI
  retries 1 and `failOnFlakyTests`), after a genuine build:showcase: **493/493 passed, 0 flaky**
  (4.4 min).
- lint, typecheck, lint:test, test and build:showcase pass. Besides the e2e files, only the internal showcase's
  tree-grid story changed, so no library gate (build, test, api:check, docs:build, size:check) is
  affected.

### Deviations

- none

### Open risks

- `cellText()` now waits for `appStable`. A story that holds a pending task would make each read
  wait up to the hook's 10 s bound and then fail with a named error. No current story does that
  under these specs, and the stress runs above exercise every caller.

### What the reviewer should examine hardest

- The mechanical `cellText` conversion. Each converted site asserted a state the test had already
  reached by a wait, or one that is stable (a held response). So the web-first form waits for
  the same text and never accepts a later one that the snapshot would have refused. Look
  especially at grid-entity's mid-flight checks ("Dana Ree", "Zebra" while the spinner shows,
  with responses held).
- That the full seven-label list in the pluralization test is the menu's real contents for a
  3-row selection, and not an over-pin.
