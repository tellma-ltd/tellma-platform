# PC-F33: long-press in view and after pending scrolls in the row-end spec

## Review 1

Commit reviewed: `d6e699d5`, parent `362aabcb`.

Verdict: approve. The diagnosis matches both CI logs and reproduces locally, and the fix removes
both causes by construction. It does not weaken the tests' assertions. There are no blocking or
major findings and two minor ones. R1-1 is pre-existing production code, for a follow-up only.

What I checked and found sound:

- **The CI failures are the two modes the commit names.** Both CI logs
  (`.tmp/pcf33-ci-43491332443.log`, `.tmp/pcf33-ci-43546459441.log`) show the same pair. The
  template test fails with "expected false to be true": the menu never opened. The no-template
  test fails with "expected { row: 3, col: 2 } to deeply equal { row: 5, col: 2 }": the press hit
  nothing, so the active cell stayed put. The brief said both failed with "expected false to be
  true". That is only true of the first.
- **The order dependence reproduces.** I ran the no-template test alone with `--filter`.
  - With the parent's spec file (checked out temporarily, then restored, and `git status` is
    clean), it fails with `{ row: 3, col: 2 }`, the CI failure. Log: `.tmp/pcf33-r1-alone-parent.log`.
  - On `d6e699d5` it passes. Log: `.tmp/pcf33-r1-alone-fix.log`.
  - Alone on the fix, the template test and the row-end drag test (which also hit-tests with
    `elementFromPoint`) pass too. Logs: `.tmp/pcf33-r1-alone-holding-the-ring-with-a-template.log`
    and `.tmp/pcf33-r1-alone-a-drag-from-the-row-end-cell-selects.log`.
- **The mechanism is as described.**
  - `tm-menu.ts:398-399` restores focus with a plain `focus()`, so the browser scrolls the window
    to an off-screen invoker.
  - `long-press.ts:157` listens for `scroll` at window capture, and `onScroll` cancels the timer
    (`long-press.ts:130-132`).
  - `grid-core.ts:3157` hit-tests the press point. It returns null for a point outside the
    viewport, and `selectMenuTarget(null)` returns early (`grid-core.ts:3172-3174`). So the menu
    opens on the old active cell.
  - On a touch pointerdown, `onPointerDown` returns before doing anything (`grid-core.ts:2740-2746`).
    So the grid itself starts no scroll during a press.
- **The helper's frame wait is sound.** The rendering update runs the scroll steps before the
  animation-frame callbacks. So after `nextFrame()`, the events of both the focus-restore scroll
  and the helper's own `scrollIntoView` have been dispatched before the press starts.
- **Production is fine for this path.**
  - A real touch point is always inside the viewport, so the null hit-test cannot happen.
  - A focus-restore scroll after a menu closes is not smooth: no `scroll-behavior: smooth` or
    `behavior: 'smooth'` exists in the client projects. Its event lands one frame after the item
    tap, before any new finger-down is physically possible.
  - Cancelling on a scroll that moves the pressed content is also required for correctness, not
    only for pans. `onLongPress` hit-tests the press's start point when the timer fires. After the
    content scrolls under a still finger, that point is a different cell. The observer therefore
    should not ignore scrolls it did not cause. R1-1 covers the one over-broad case.
  - Spec 0010 §C.14 and 0004 §8.6 say nothing about scroll cancellation beyond "native pan
    scrolling". The observer's documented contract ("cancels ... on scrolling") is what the code
    does.
- **The tests still prove their claims.**
  - The new hit-test assertion makes the no-template test prove that the press landed on row 5's
    end cell. Before, that was down to luck.
  - The 5 s `vi.waitFor` fits inside the test's budget: Vitest defaults `testTimeout` to 15 s in
    browser mode (`vitest/dist/chunks/coverage.DM_a_rWm.js:538`), so a failure shows the helper's
    message.
  - The grid's own hold threshold is still bounded elsewhere: `tm-grid-list.spec.ts:418-429` and
    `:554-560` press the same scroller observer with a fixed 650 ms hold. So the looser bound here
    hides no threshold regression. A lower bound (a press released just short of the default
    500 ms opens nothing) is asserted nowhere for the default delay. The parent did not assert it
    either, so this commit did not open that gap.
- **AGENTS.md is met.** `nextFrame` and `longPress` have TSDoc, the logic carries inline comments,
  and no name, message or comment cites a spec, PC id or `plans/`.
- **Runs.**
  - The spec file alone, three times: 30/30 passed each time. Logs:
    `.tmp/pcf33-r1-file{1,2,3}.log`.
  - `ng test core-ui`, twice: 2053/2053 passed each time. Logs: `.tmp/pcf33-r1-full{1,2}.log`.

### Findings

- **R1-1 (minor, pre-existing, follow-up; not blocking this item).**
  `client/projects/core/tellma-core-ui/menu/internal/long-press.ts:130-132, 157`.
  - The observer cancels a press on every `scroll` dispatched anywhere in the document. Window
    capture also receives element scrolls, including those of scrollers that neither contain nor
    move the pressed element.
  - Failure scenario: a page with an auto-scrolling region beside the grid, such as a live log, a
    notifications list that follows new items, or a carousel. Each of its scrolls silently cancels
    any long-press in progress on the grid (or on any `tmContextMenuTrigger` or tooltip host). The
    user's hold opens nothing, although nothing under the finger moved.
  - Suggested fix: cancel only when the scrolled node is the document or an ancestor of the
    observed element, so that the press point may have moved:
    `const t = event.target; if (t === document || (t instanceof Node && t.contains(element))) cancel();`.
    Add a unit test in `tm-menu.spec.ts` beside "movement beyond the slop cancels the press": a
    sibling scroller's scroll leaves the press alive, and an ancestor's scroll cancels it.
- **R1-2 (minor).**
  `client/projects/core/tellma-core-ui/grid/tm-grid-header-nav.spec.ts:363-373`.
  - The helper's doc says it holds the press "until the long-press menu opens". Its `vi.waitFor`
    only checks that some `.tm-menu__panel` is open, and nothing checks the menu was closed before
    the press.
  - Failure scenario: if a preceding Escape ever fails to close the menu, the wait resolves at
    once. This can happen in the no-template test, which never asserts the menu closed before
    `longPress` (lines 840-843), and after the template test's second Escape (lines 821-824). The
    helper then releases before the 500 ms threshold, which cancels the long-press. The test
    then fails on the active-cell assertion, which points the reader away from the real cause. A
    future caller that relies on the helper alone gets a false pass.
  - Suggested fix: before the `pointerdown`, assert
    `expect(menuOpen(), 'no menu is open before the press').toBe(false)`. That way, the wait
    proves this press opened the menu.

### Responses

- R1-1: deferred to a fix item by the orchestrator. It changes production long-press behaviour
  (cancelling only on a scroll of the document or of an ancestor of the observed element), so it
  is out of scope for this test-only fix.
- R1-2: fixed in the "PC-F33: review 1 fixes" commit (child of d6e699d). Before the
  `pointerdown`, the long-press helper asserts `expect(menuOpen(), 'no menu is open before the
  press').toBe(false)`, so its `vi.waitFor` proves that this press opened the menu.
