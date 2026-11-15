# PC-F34: long-press cancels only on a scroll that moves the pressed content

## Implementer notes

### What was built, and where

- **`client/projects/core/tellma-core-ui/menu/internal/long-press.ts`.**
  - The window-capture `scroll` listener now cancels a timed press only when the scroll moves the
    pressed content:
    - the event's target is not a node (a scroll dispatched at the window), or is a Document;
    - the target contains the observed element (an ancestor scroller, or the element itself);
    - the target contains the node the press landed on (a scroller inside the element that holds
      the pressed node).
    A scroll anywhere else (an auto-scrolling log, a carousel, a scroller inside the element
    beside the pressed node) leaves the press alive.
  - A new `pressNode` holds the press's target while its timer runs. It is set in `pointerdown`
    and cleared in `cancel()`, so it is non-null exactly while a press is being timed. The scroll
    listener returns at once when it is null, so the many observers a page holds (one per tooltip
    host) do no `contains` walks on ordinary scrolling. `pressedTarget` (the suppression's node)
    is unchanged; it outlives the press, so it is not reused.
  - The function's TSDoc sat above the `firedGestures` constant, so `tmObserveLongPress` itself
    had none. It now sits on the function and states the scroll rule.
- **`client/projects/core/tellma-core-ui/menu/tm-menu.spec.ts`**, `ɵtmObserveLongPress` >
  `scrolling` (6 tests). Layout: ancestor scroller > observed element > (scroller holding the
  pressed span, scroller beside it); an unrelated scroller beside the ancestor.
  - an unrelated scroller's scroll leaves the press alive;
  - an ancestor's scroll cancels;
  - a document scroll cancels (a 300vh spacer makes the document scrollable for that test only);
  - the observed element's own scroll cancels;
  - a scroller inside the element that holds the pressed node cancels;
  - a scroller inside the element beside the pressed node leaves the press alive.
  - The scrolls are real (`scrollTop`, `window.scrollTo`), and each test awaits the browser's own
    `scroll` event on the scroller; the observer's window-capture listener runs before that
    target-phase listener. Only `setTimeout`/`clearTimeout` are faked
    (`vi.useFakeTimers({ toFake: [...] })`), so frames and scroll dispatch stay real and the press
    timer advances only by `vi.advanceTimersByTime`. No wall-clock waits.
  - `beforeEach` waits one animation frame before the press, so a scroll still pending from an
    earlier test lands first. The document test scrolls back to 0 and awaits that event before it
    ends, so it cannot reach a later test's press.
  - The existing `touchDown` helper gained an optional target (default: the observed element).

### Mutation checks

- The parent's always-cancel listener: the two "leaves the press alive" tests fail, the other 32
  pass (`.tmp/pcf34-mut-always.log`).
- The narrower rule suggested in PC-F33 R1-1 (document or `contains(element)` only): the
  "holds the pressed node" test fails (`.tmp/pcf34-mut-narrow.log`).

### Verification

- The menu spec file alone: 34/34 (`.tmp/pcf34-menu-spec1.log`).
- Client gates (logs `.tmp/pcf34-gate-*.log`): lint, typecheck, tokens:check, build, api:check
  (no golden change: the observer is internal), size:check, docs:build, test (core 484, core-testing
  22, core-ui 2389, core-ui test-csp 2, core-ui-tokens 27, locale-ar 9, showcase 1) and
  build:showcase pass.
- lint:test: 228/229. The one failure is the tellma-core-tools `common.spec.ts` package-manager shim
  test timing out at 5.1 s under load (the D-078 case); the file alone passes 7/7
  (`.tmp/pcf34-shim-alone.log`).
- Bundle (D-067, linked measure): ./menu 7.82 KB against its 7.86 ceiling; no raise.
- PR-tier e2e in CI mode after build:showcase
  (`CI=1 node scripts/e2e.mjs --project chromium --project touch --grep-invert "@nightly|@perf|@quarantine" --workers 2`):
  1066 passed, none flaky, 28.2 min (`.tmp/pcf34-e2e.log`). It includes the touch long-press
  specs (grid-touch, grid-touch-search-page, tooltip-touch). No e2e spec was added or changed.
- The menu spec file three more times after the gates: 34/34 each (`.tmp/pcf34-menu-rep{1,2,3}.log`).

### Deviations

1. **The rule is wider than the item's wording by one case.** The item says to cancel only for the
   document or an ancestor of the observed element. A scroller inside the observed element that
   holds the pressed node also cancels. The grid observes its own scroller, which the ancestor
   rule covers (an element contains itself), but a `tmContextMenuTrigger` area or a tooltip host
   can contain a scroller of its own. A scroll there moves the content under a still finger, and
   the grid and menu hit-test or open at the press's start point when the timer fires, so it
   would open on whatever has scrolled under it (the correctness case PC-F33 R1-1's analysis
   names). A scroller inside the element that does not hold the pressed node still leaves the
   press alive.
2. **A non-node scroll target cancels.** A `scroll` dispatched at the window (only script does
   this; the browser fires viewport scrolls at the Document) is treated as a document scroll.

### Open risks

- A pressed node detached during the press (a virtualised row recycled by a scroll) is still
  covered when its scroller is the observed element or an ancestor of it, which is the grid's
  case. A detached node inside a scroller nested in the element is not: that scroller no longer
  contains it, so its scroll leaves the press alive. No current consumer has that shape.
- Scroll events inside a shadow root do not cross it (they are not composed), so window capture
  never saw them before this change either. Unchanged.

### What reviewers should examine hardest

- Whether deviation 1 (a scroller inside the element that holds the pressed node) is right, or
  whether any scroll inside the observed element should cancel.
- Whether the new tests are deterministic under CI load: they wait only on real scroll events and
  one animation frame, never on time.
