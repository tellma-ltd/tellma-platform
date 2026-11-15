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

## Review 1

Commit reviewed: `af494fef`, parent `75047909`.

Verdict: approve. The fix does what the item asks, and deviation 1 is right. There are no
blocking or major findings and two minor ones.

What I checked and found sound:

- **The rule matches the item.** `long-press.ts:137-158` cancels for a Document target, a
  non-node target, a scroller that contains the observed element (an ancestor or the element
  itself), and a scroller that contains the pressed node. Every other scroll leaves the press
  alive. Window capture does receive non-bubbling element scrolls, so the listener sees them all.
- **Deviation 1 is right, and "any scroll inside the element cancels" would be wrong.** None of
  the three consumers re-reads the content beside the pressed node. The grid
  (`grid-core.ts:6192`) hit-tests the start point, `tmContextMenuTrigger`
  (`tm-context-menu-trigger.ts:41-45`) opens at the start point, and the tooltip
  (`tm-tooltip-controller.ts:90`) shows for its host. A scroller beside the pressed node moves
  nothing under the finger, so cancelling there would bring back the bug for a scroller inside the
  element. A scroller that holds the pressed node does move it, so cancelling there is the
  correctness case PC-F33 R1-1 named. Deviation 2 (a non-node target cancels) is harmless.
- **`pressNode` lifecycle.** It is set only after the `enabled` gate passes, and `cancel()` clears
  it. `cancel()` runs on a fire, a release, a move past the slop, a cancelling scroll and cleanup.
  So the early return makes idle observers (one per tooltip host) free on every scroll.
  `pressedTarget` keeps its own lifetime for suppression.
- **Spec conformance.** Spec 0004 §8.6, 0005 §12 and 0010 §C.14 say nothing about scroll
  cancellation beyond native pan, so nothing in the specs is contradicted. A native touch pan
  still ends in `pointercancel`.
- **Tests.**
  - They use real `scrollTop`/`scrollTo` scrolls and await the browser's own scroll event. Only
    `setTimeout`/`clearTimeout` are faked. The one-frame wait in `beforeEach` and the document
    test's scroll back to 0 keep events from leaking between tests.
  - `afterEach` order is safe. The inner `useRealTimers` runs before the outer `stop()`, which
    only clears fake ids that no longer exist.
  - `firedGestures` is left holding pointer 1 after a firing test. The next `touchDown` deletes
    it.
- **AGENTS.md / PLAN §7.1.** The TSDoc now sits on the function. The new `let` and the helpers
  have TSDoc. No TSDoc, test name or comment cites a spec, PC id or `plans/`.
- **Gate evidence matches the commit message.**
  - lint:test fails only on the `common.spec.ts` shim timeout (`.tmp/pcf34-gate-lint-test.log`,
    228/229), which D-078 covers.
  - size `./menu` is 7.82 of 7.86 KB.
  - The e2e run had 1066 passed.
- **My run.** The menu spec file alone, `ng test core-ui --include .../menu/tm-menu.spec.ts`:
  34/34 passed (`.tmp/r1f34-menu1.log`).

### Findings

- **R1-1 (minor).** `client/projects/core/tellma-core-ui/menu/internal/long-press.ts:150-155`.
  - The ancestor test uses `Node.contains`, which does not cross shadow roots. Before this change,
    every scroll cancelled.
  - Failure scenario: a consumer puts `tmContextMenuTrigger` or `tmTooltip` inside a component
    with `ViewEncapsulation.ShadowDom`, inside a light-DOM scroller (a dialog body or a side
    panel). A scroll of that scroller during a hold arrives at window capture with the scroller
    as its target. The scroller does not contain the shadow-tree element or `pressNode`, so the
    press survives. It then opens the menu at a start point that the content has scrolled away
    from.
  - No component in the repo uses Shadow DOM today, so this is a latent regression for consumers
    only. The implementer's open-risks note covers scrollers inside a shadow root (never seen,
    unchanged), but not this case (ancestors outside the shadow root, newly missed).
  - Suggested fix: walk the composed ancestry instead of calling `contains`. For example, a helper
    that climbs `node.parentNode` and steps from a `ShadowRoot` to its `host`, and returns true
    when it reaches `scrolled`. Apply it to both `element` and `pressNode`. Add a unit test that
    hosts the observed element in an attached shadow root under the ancestor scroller and expects
    the ancestor's scroll to cancel. Alternatively, state the limit in the TSDoc.
- **R1-2 (minor).** `client/projects/core/tellma-core-ui/menu/tm-menu.spec.ts:806-812, 835-843` and
  `long-press.ts:153`.
  - `pressNode` is always the element or a descendant of it. While it stays attached,
    `scrolled.contains(pressNode)` holds whenever `scrolled.contains(element)` does. So in the
    new suite, the ancestor test and the element-itself test pass with the `contains(element)`
    clause deleted.
  - The item's central rule ("an ancestor of the observed element") is therefore not pinned on
    its own. That clause matters only once the pressed node has been detached. That is exactly
    the case the implementer's open-risks note relies on: a grid row re-rendered during the hold,
    then a scroll of the grid's scroller.
  - Failure scenario: a later edit drops the clause as redundant. All 34 tests still pass, and a
    hold on a cell whose row was re-rendered survives a scroll of the grid. The long-press then
    hit-tests the start point on whatever row has scrolled there.
  - Suggested fix: add a test where `touchDown(50, 60, pressed)` is followed by
    `pressed.remove()`, then `await scrollBox(ancestor, 50)`, then advancing the timers, and
    expect no points. Optionally add a second test that scrolls `element` itself after the
    detach.

### Responses

- R1-1: fixed in the "PC-F34: review 1 fixes" commit (child of af494fe). Both ancestry tests now go
  through a `composedContains` helper. It climbs `parentNode` and steps from a `ShadowRoot` to its
  `host`, so a light-DOM scroller around a shadow-hosted element (or pressed node) cancels the
  press. New test: "a scroll of a light-DOM ancestor cancels the press on an element inside a
  shadow root" (ancestor > shadow host > open shadow root > element). Mutation: with plain
  `Node.contains` restored, that test fails and the others pass (`.tmp/pcf34-fix1-mut-plain-contains.log`).
  The walk took ./menu to 7.87 KB, over its 7.86 ceiling. The separate Document arm was dropped as
  the saving: the document is a composed ancestor of every connected element, so the walk from the
  element reaches it, and the "a scroll of the document cancels the press" test still passes. That
  brings ./menu to 7.84 KB, under the ceiling with no raise. A scroll target that is not a node
  still cancels (deviation 2).
- R1-2: fixed in the same commit. Two new tests detach the pressed node during the hold, then scroll
  an ancestor, or the observed element itself, and expect no fire. Mutation: with the
  `contains(element)` clause deleted, both fail and the others pass (`.tmp/pcf34-fix1-mut-no-element.log`).
  The inline comment now says the element test is the one that holds once the pressed node is
  detached.
