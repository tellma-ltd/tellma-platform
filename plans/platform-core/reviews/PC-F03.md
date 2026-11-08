# PC-F03: entity-picker same-frame Enter race

## Implementer notes

### The defect, root-caused

PC-F02 R1-2 reproduced it in the grid. Two mechanisms decide a commit key against DOM state that
lags the picker's own state by one render.

1. **The highlight is applied after render.** Every keystroke supersedes the result set and clears
   aria's active option at once (`queueSearch` → `clearHighlight`). A sync source then answers in
   the same task (`applyResults` → `scheduleHighlight`), but the first-result highlight is applied
   by an `afterRenderEffect`. Until that render, aria's active option is `undefined`.
2. **aria relays Enter after render too.** While the popup is open, `ComboboxPattern` stores the
   Enter in a signal and re-dispatches it onto the listbox from an `afterRenderEffect`
   (`keyboardEventRelayEffect`). If the listbox has not mounted yet (first open; aria's deferred
   content renders it a pass after the overlay attaches), `popup.controlTarget()` is undefined and
   the Enter is dropped.

Effects of a burst (keys plus Enter or Tab with no render in between):

- **Grid cell, Enter.** The capture layer saw no active option and handed Enter to the grid as a
  typed commit, so the cell moved down. `Al` went through the resolver to an ambiguous error, and
  `Alice` was adopted unique but moved.
- **Grid cell and standalone, Tab.** `handleTab` saw no active option and closed without picking.
  In the grid the typed text then took the resolver path.
- **Standalone, Enter.** A first-open Enter was dropped (no commit, no fail-fast, no resolution).
  Even with the listbox mounted, a narrowing burst failed in the unit suite.
- **Async.** An answer that landed (promise continuation) but had not rendered behaved the same
  way, since it waits on the same after-render highlight.

### Semantics chosen

**A commit key acts on the highlight the fresh result set has already decided, whether or not it
has been painted.** Spec 0006 §4.3 makes the first-result auto-highlight a function of the fresh
result set for a typed query ("After every fresh result render … the first result is
auto-highlighted"). §4.2 says a synchronous set renders "in the same turn". So the highlight is a
fact of the result set, and the paint only shows it. Where results have not arrived (an async
request in flight, or a query inside the coalescing window), §4.3 and §7.3 already decide the
outcome without reference to rendering: resolution standalone, and the grid's typed-commit chain in
a cell. That path was already render-independent and is unchanged. The other options are worse.
Resolving the pending query synchronously ignores the highlight the user would have seen and turns
multi-result sets into ambiguity errors. Deferring the commit until the search settles contradicts
§4.3's Enter-while-loading rule. Treating the text as typed commits different data from paced
input, which is the bug itself.

### What was built

`client/projects/core/tellma-core-ui/entity-picker/tm-entity-picker.ts`:

- `appliedHighlightSeq` is set by the highlight `afterRenderEffect` when it applies a request.
  A request with any other sequence is pending.
- `keyboardActiveKey()` returns the key Enter and Tab act on. When a highlight request is pending,
  it returns the typed query's first result (only while the search state still holds that result
  set), or `undefined` for a pristine browse, an empty set, or a superseded set. Otherwise it
  returns aria's active option (`domActiveKey()`).
- The capture-phase layer now owns **Enter while the popup is open**, in both modes. It stops
  propagation (aria's deferred relay never sees the key), then does one of two things. In a cell
  with no highlight, it closes the dropdown and hands the grid a clean clone, as before. Otherwise
  it calls `preventDefault` and activates `keyboardActiveKey()` synchronously through
  `activateActive(key, enterFallbacks)`. That covers the pick, a footer-row modal, and the §4.3
  fallbacks: resolution while loading, fail-fast on an empty set, and a plain close when pristine.
  Key repeats are consumed but activate nothing, matching aria's ignore-repeat binding. A disabled
  or read-only picker is left to aria.
- `handleTab` uses `keyboardActiveKey()`. Pointer clicks keep aria's active option (the pressed row,
  which is on screen).
- The listbox `(keydown.enter)` binding and `onListboxEnter()` are removed: with the capture layer
  owning Enter they were dead. The API golden drops that protected member (reviewed diff, one
  line).
- In a cell, the pick goes through `applyPick` → `ɵcellActivate` inside the keydown, so the grid
  commits and closes with no move before the key could reach it. No grid code changed, so the
  grid bundle is untouched.

Tests (each new test fails on 4c89ae1 and passes now, except the two semantic pins noted):

- Unit, `tm-entity-picker.spec.ts` "input delivered faster than the page renders" (standalone, 6
  tests): first-keystroke Enter, narrowing Enter, Tab, empty-set fail-fast, async answer landed but
  unrendered (released response, microtasks only, then Enter), and async unanswered (resolution,
  `source: 'auto'`).
- Unit, the same describe under "as a grid cell editor" (5 tests): narrowing Enter (activation, no
  key to the grid), seed plus keys plus Enter in one task, Tab (picked, key reaches the grid, no
  activation), async landed but unrendered, and async unanswered. The last is a **semantic pin**:
  the grid commit, which passed before too.
- Unit, `tm-grid-editing.spec.ts`: type-to-edit key, text and Enter in one task commit with no
  move; the Tab variant commits and moves. These are end to end through the real grid.
- e2e, `e2e/support/keyboard-burst.ts`: `pressInOneTask(page, keys)` dispatches keydown, the
  default text insertion with an `input` event, and keyup for every key inside ONE `evaluate`, so
  no render can run between keys. `releaseAndPressInOneTask` first releases a held-response gate
  and runs microtasks only, so the answer is in state but not on screen. I chose this over CDP
  `Input.dispatchKeyEvent`: trusted events sent back to back may or may not get a frame between
  them, so a CDP test would exercise the race only sometimes.
- e2e, `entity-picker.spec.ts` (6) and `grid-entity.spec.ts` (5): the same matrix on the
  showcase, with sync and async (held) sources. The grid's "unanswered" test is the second
  semantic pin.
- CONTRIBUTING.md "Flaky tests" points at `pressInOneTask` for input-faster-than-render tests.

### Verification

- Before the fix (picker file reverted to 4c89ae1, tests kept): the unit suites failed 12 of the 13
  new tests (the cell "unanswered" pin passed). The new e2e failed 10 of 11 (the grid
  "unanswered" pin passed). Each failure was the reported behaviour: a move, a null value, a
  dropped Enter, or an open panel.
- After the fix, see the stability and gate results below.

- Unit, the two touched spec files (`tm-entity-picker.spec.ts`, `tm-grid-editing.spec.ts`) run
  10 times: 10 × 170/170 passed.
- e2e, the two touched spec files in full (all 67 tests, the 11 new ones included), Chromium,
  CI=1 prod build, `--workers 2 --retries 0 --repeat-each 10`: **670/670 passed** (13.3 min,
  local Windows).
- e2e PR tier (`--project chromium --project touch --grep-invert "@nightly|@perf|@quarantine"`,
  CI=1, 2 workers): **451/451 passed** (440 before plus the 11 new ones).
- Gates: lint, typecheck, lint:test, tokens:check, build, test (core-ui 1162, tokens 14,
  locale-ar 8, showcase 1), api:check (after `api:approve`; the only change is the removed
  `onListboxEnter` line), docs:build and size:check all pass. Size: entity-picker 11.19 of
  12 KB; grid 38.00 of 38 KB, unchanged, because no grid code changed.

### Deviations

1. Spec 0006 §4.1 and §4.3 say aria owns the listbox keyboard model and commit rides
   `(keydown.enter)` on the listbox. Enter while the popup is open is now handled in the
   capture-phase layer instead. It is still activation-driven, never `valueChange`, because aria's
   relay runs after render and drops the key when the listbox has not mounted.
2. The spec is silent on keys that arrive before the fresh rows render. A pending highlight request
   is honoured as the highlight (see "Semantics chosen"), on the basis of §4.3 and §4.2's "same
   turn" fast path.
3. A held Enter's repeats while the popup is open are now consumed (never a form submit) and still
   activate nothing. Before, aria ignored them without consuming them, so a held Enter could
   submit a form. This matches §4.3 "with the popup open, Enter is always consumed".

### Open risks

- **Batched arrow keys are not covered.** aria relays ArrowUp/Down through a single-slot signal
  after render. A burst of arrows collapses to the last one and moves among the previous rows,
  and a pending highlight then overrides it at Enter (first result, never a stale row). Paced
  "type, ArrowDown, Enter" picks the second row, and the same keys in one burst pick the first.
  Scanners and expanders do not send arrows. Fixing it means owning arrow navigation in the
  capture layer (an upstream aria limitation). It is not done here.
- IME: a composition-confirming keydown whose `key` is `Enter` while the popup is open commits, as
  it did through aria's relay. The behaviour is unchanged, but it now runs synchronously.
- Enter's commit in a cell now happens inside the keydown, not in the next render's relay. The
  editor is torn down synchronously within the handler. Every grid entity suite passes, but this
  is a timing change for anything observing that keydown later in the same task (the keyup lands
  on the cell).

### What the reviewer should examine hardest

- `keyboardActiveKey()`: whether any path leaves a request "pending" while the DOM highlight is in
  fact authoritative (a listbox remount, a reopen, `resolveText`'s `applyResults` on the Enter
  path), or the reverse. Also whether the `state.kind === 'results' && state.query ===
  request.query` guard can ever reject a set the user is looking at.
- The capture Enter branch against everything aria used to do with that key: `preventDefault`
  coverage (form submit), the footer-row modal launches, the cell no-highlight clone, the
  disabled/read-only stand-down, and repeats.
- That removing `(keydown.enter)` cannot drop an Enter. The only Enter keydowns that still reach
  aria are modified ones (aria has no binding for those) and those on a disabled or read-only
  picker.
- That the e2e burst helper exercises the race rather than passing vacuously. Each new test failed
  before the fix for the reported reason, and the async-landed tests first assert that the
  response is held and the spinner is up.
