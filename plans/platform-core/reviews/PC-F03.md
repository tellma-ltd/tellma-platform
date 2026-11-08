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
   activate nothing (pinned by a unit test since Review 1). Before, aria ignored them without
   consuming them, so a held Enter could submit a form. This matches §4.3 "with the popup open,
   Enter is always consumed".

### Open risks

- **Batched arrow keys: tracked as a separate fix item (orchestrator ruling after Review 1).**
  aria relays ArrowUp/Down through a single-slot signal after render. A burst of arrows collapses
  to the last one and moves among the previous rows, and a pending highlight then overrides it at
  Enter (first result, never a stale row). Paced "type, ArrowDown, Enter" picks the second row,
  and the same keys in one burst pick the first. Scanners and expanders do not send arrows.
- IME: since Review 1, the Enter that confirms a composition (`isComposing`, or WebKit's 229) is
  left to the IME while the popup is open: no pick, no commit, no `preventDefault`.
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

## Review 1

### Findings

**R1-1 (blocking): a first-open burst that ends in Enter opens and closes the popup in one task,
which desyncs aria's `expanded` model from the picker and skips the close reset. The combobox stays
`aria-expanded="true"` with no popup, the next Enter is swallowed, and reopening shows the stale
result set with an auto-highlight.**
`client/projects/core/tellma-core-ui/entity-picker/tm-entity-picker.ts:1818-1836` (the Enter
branch, closing through `activateActive` at `:2124`), with the `[(expanded)]` binding at `:184` and
Effect 6 at `:998-1007`.
Reproduced on the dev showcase (entity-picker story, sync search, `picker-field`). I emptied the
field and pressed Enter, so the popup was closed. Then `A`, `l`, Enter went in one task, keydown,
input and keyup per key, as `pressInOneTask` sends them. The value became "Alice Green" (correct),
but:
- The input kept `aria-expanded="true"` with no panel, and it stayed that way.
- A later plain Enter keydown came back `defaultPrevented === true`, so the form cannot submit.
  Paced (`A`, render, `l`, render, Enter, render, Enter), the second Enter is not prevented, as
  §4.3 requires ("With the popup closed, Enter is not consumed").
- A click to reopen showed "Alice Green, Alan Grey" (the stale `Al` set) with "Alice Green" active
  on pristine text. Paced, the reopen shows the browse list with nothing highlighted (§4.3: a
  pristine browse list highlights nothing).

Two mechanisms cause this, and both come from the open and the close landing in the same task:
1. aria's `onInput` sets its own `expanded` model to true, and the two-way binding copies that into
   the picker. The pick then sets the picker's signal back to false before any change detection.
   The template's bound value is false at both checks, so the false is never pushed to aria. aria
   keeps its expanded-mode keydown map, which relays and `preventDefault`s Enter.
2. Effect 6 compares the value against `wasExpanded` and sees no transition, so neither
   `onPopupOpened` nor `onPopupClosed` runs. `searchState` stays `results`, the debounce and abort
   reset is skipped, and `highlightRequest` stays pending. The next open returns early on the
   non-idle state. The stale request then auto-highlights after render, and before that render
   `keyboardActiveKey()` answers its first row.

Before this commit the first-open Enter was dropped, so this exact state was unreachable through
Enter. It is now reached by the scanner-into-an-empty-field case the fix targets. A standalone
first-open Tab burst has the same stale-state half. A real Tab's blur heals aria's model, but the
close reset is still skipped, so a return visit gets the stale rows. Neither new test catches it:
the unit test "Enter in the same task as the first keystroke commits the first result" and the e2e
test "the first keystrokes and Enter in one burst…" only assert that the panel is gone.
Suggested fix: route the picker's own closes through one helper. It writes aria's `Combobox`
`expanded` model directly, through a `viewChild(Combobox)`, so the binding cannot be skipped, and it
runs the close reset synchronously when the open was never observed. Alternatively, drop Effect 6's
boolean edge detection in favour of explicit open and close calls. Extend the unit and e2e
first-open tests: after settling, assert `aria-expanded="false"`, assert that a following Enter is
not prevented (e2e: `submit-count` becomes 1), and assert that reopening shows the browse list with
no active option. Do the same for the first-open Tab burst.

**R1-2 (minor): deviation 3 (a held Enter's repeats are consumed and activate nothing) has no
test.**
`client/projects/core/tellma-core-ui/entity-picker/tm-entity-picker.ts:1829-1835`. No new or
existing test dispatches Enter with `repeat: true`; the only "repeat" in the spec file is the
search-coalescing test. If the `!event.repeat` guard were dropped, a held Enter on a fail-fast or
loading popup would re-run `failResolution` or `resolveText` on every repeat, or launch a footer
modal twice. If the guard moved above `preventDefault`, repeats would submit the form. The suite
would stay green in both cases. Suggested fix: add a unit test that opens a typed empty set (or
highlights Create…), dispatches Enter with `repeat: true`, and asserts `defaultPrevented === true`,
no pick, no modal launch, and an unchanged error state.

**R1-3 (minor): the Enter branch the picker now owns acts on an IME composition-confirming Enter.**
`client/projects/core/tellma-core-ui/entity-picker/tm-entity-picker.ts:1818`. Chromium and Firefox
with macOS and Linux IMEs deliver the keystroke that confirms a composition as `key === 'Enter'`
with `isComposing === true`, and Safari delivers it with `keyCode === 229`. The composition's
`input` events have already opened the popup and searched the preedit text. That Enter now commits
the first result (or, in a cell, picks it as the edit) and consumes the key, when the user only
meant to confirm the conversion. aria's relay did the same before, so this is not a regression.
But the commit moves the key into the picker's own handler and lists it as an open risk, while
§7.3 says composition happens inside the editor. The guard is one line in code the picker now owns.
Suggested fix: at the top of the Enter branch, when `event.isComposing || event.keyCode === 229`,
call `stopPropagation()` (so aria cannot relay it) and return, with no `preventDefault` and no
activation. Add a unit test that dispatches Enter with `isComposing: true` while open and asserts
that nothing is picked.

Checked and found sound: the pending-request rule in `keyboardActiveKey()` (only `applyResults`
writes a `results` state, and it always issues a new request, so the `state.query ===
request.query` guard cannot reject a set on screen; a superseded or loading set answers `undefined`;
a pristine or empty set highlights nothing). Also sound: the cell no-highlight clone, the
`preventDefault` coverage, the disabled and read-only stand-down, Escape, Home and End, ArrowUp
from no highlight, modified Enter (still aria's, which binds none), footer-row activation, and the
removed `(keydown.enter)` and `onListboxEnter`. Deviations 1 and 2 are justified by §4.2 and §4.3.
The burst helpers really deliver keys with no render in between, because they are synchronous
inside one `evaluate`, with only microtasks after the release. The async-landed standalone test is
not vacuous: `Al` is ambiguous, so the resolution fallback would fail it. The new e2e tests use
web-first waits and `appStable` before each burst, with no timing races. I re-ran the two touched
unit files (170/170) and the 11 new e2e tests on Chromium with CI=1, `--repeat-each 3 --retries 0`
(33/33).

### Responses

R1-1: fixed in bcba0eb. Both mechanisms were as described. (1) Every write the picker makes
to its open state now goes through `setExpanded()`. It writes aria's `Combobox.expanded` model as
well as the picker's signal (`viewChild(Combobox)`), so an open by aria and a close by the picker
in the same task can no longer leave the two-way binding unpushed. This covers every close and open
site (pick, Tab, cell clone, cancel, commit, modal launch, outside press, disabled/read-only, seed,
openDropdown, input click), not only the burst paths. (2) Effect 6 now treats ANY re-run that reads
"closed" as a close, observed open or not. It runs only when the value changed, and it skips the
construction-time run. So an open and close that both land between two of its runs still get
`onPopupClosed()`: debounce cancel, abort, idle state, and the highlight request cleared. It stays
an effect, not a synchronous reset in the close helper. A synchronous reset would abort the
in-flight search a cell commit is about to adopt (the grid adopts after `commit()` closes the
dropdown). The extended tests now assert all three symptoms. Unit "Enter in the same task as the
first keystroke…" checks `aria-expanded="false"`, that the next Enter is not prevented, and that a
reopen is the full pristine browse with no active row. The new unit test "Tab in the same task as
the first keystroke commits and leaves nothing of the open behind" covers the Tab burst. The e2e
"the first keystrokes and Enter in one burst…" adds `aria-expanded="false"`, a following Enter
that makes `submit-count` 1, and a reopen showing the capped browse list with no active option.
The new e2e "the first keystrokes and Tab in one burst…" covers Tab. On af35d27 these fail: the
unit Enter and Tab tests, and both e2e tests on `aria-expanded`. They pass now.

R1-2: fixed in bcba0eb. New unit test "a held Enter is consumed on every repeat but
activates only on the first press". On a fresh empty typed set, a `repeat: true` Enter is
`defaultPrevented` and shows no fail-fast error. With Create… highlighted, a `repeat: true` Enter
is `defaultPrevented`, launches no modal, keeps the popup open, and picks nothing. It passes on
af35d27 too (the guard was already there): it pins the behaviour against both regressions the
finding names.

R1-3: fixed in bcba0eb. The Enter branch now starts with an IME guard. When
`event.isComposing` or `keyCode === 229` (read through a non-deprecated view, as `grid-core.ts`
does), it stops propagation, so aria cannot relay the key, and returns with no `preventDefault`
and no activation. New unit test "the Enter that confirms an IME composition picks nothing and is
left to the IME": a composing Enter with a highlight is not prevented, picks nothing and leaves the
popup open; the following plain Enter commits. It fails on af35d27 and passes now. The open-risk
entry is updated.

Orchestrator ruling: the arrow-key burst gap is recorded under Open risks as "tracked as a
separate fix item" and is not fixed here.

Round-1 verification (local Windows, CI=1 prod builds, 2 workers):
- Gates: lint, typecheck, lint:test, tokens:check, build, test (core-ui 1165, tokens 14,
  locale-ar 8, showcase 1), api:check, docs:build and size:check all pass. Size: entity-picker
  11.28 of 12 KB; grid 38.00 of 38 KB, unchanged.
- The two touched unit spec files run 10 times: 10 × 173/173.
- PR-tier e2e (chromium + touch, `--grep-invert "@nightly|@perf|@quarantine"`): 451 passed and
  1 flaky, so the run exits 1 under `failOnFlakyTests`. The flaky test is touch "a double-tap
  opens the editor; the handles hide while it is open" (grid-touch.spec.ts:206). It double-taps
  the text column of grid-editable, which the picker never mounts in, and it passed on retry. It
  is a wall-clock double-tap threshold under load, not related to this change. Rerun in isolation
  with `--repeat-each 20 --retries 0`, it passed 20/20.
- entity-picker.spec + grid-entity.spec in full, `--repeat-each 10 --retries 0`: 679/680. The one
  failure was a 30 s test timeout in grid-entity "axe & RTL › no violations with the in-cell
  editor and dropdown open (dark)". It made no assertion failure, and the run was busy (23.9 min,
  against 13.3 min for the same run before this round, with other slots building). The test only
  opens the dropdown (Alt+ArrowDown) and runs axe. The axe & RTL describe rerun alone with
  `--repeat-each 20 --retries 0` passed 60/60.
