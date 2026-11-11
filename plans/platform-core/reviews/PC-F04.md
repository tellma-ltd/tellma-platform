# PC-F04: entity-picker arrow keys in a burst

## Implementer notes

### The defect, root-caused

PC-F03 made the commit keys (Enter, Tab) render-independent and left the arrows with aria. While
the popup is open, aria's `ComboboxPattern` does not handle ArrowUp/ArrowDown itself: it stores the
event in a **single-slot** signal (`keyboardEventRelay`) and re-dispatches it onto the listbox from
an `afterRenderEffect`. Three consequences when keys arrive with no render between them (a fast
typist on a stalled main thread, a keyboard wedge, test automation):

1. **Enter never sees the arrow.** `Al`, ArrowDown, Enter paced picks the second result; in one
   task Enter is decided synchronously (PC-F03) while the arrow is still parked in the relay, so
   the FIRST result is committed. Different data for the same keys.
2. **Several arrows collapse into one.** The slot holds the last arrow only, so three ArrowDowns
   in one task move one row.
3. **The arrow steps among the previous rows.** The relay fires after the render that the
   keystroke before it caused, but aria steps from the active option of the rows it had, and a
   pending first-result highlight is applied in the same pass.

Reproducing those exposed two more members of the same family. Both come from the popup's
open/close being acted on lazily (by an effect), while keys are decided at once:

4. **A key in the same task as the one that opened the popup met no rows.** A collapsed ArrowDown
   opens the popup synchronously (aria), but the initial search was issued by the transition effect,
   later. ArrowDown, ArrowDown, Enter in one task therefore picked nothing; paced, it picks the
   first browse row.
5. **A reopen in the same task as a close inherited the closed popup's rows.** The close reset
   also ran in the effect. "Pick, ArrowDown, ArrowDown, Enter" and "Escape, ArrowDown, Enter" in
   one task acted on the rows (and, for Escape, the highlight) of the popup that had just closed.

### Semantics chosen

**Every key acts on the picker's own state at the moment it is pressed, never on what has been
painted.** PC-F03 established that for the commit keys: the highlight is a fact of the result set
(spec 0006 §4.2 "same turn", §4.3 auto-highlight). This item extends it to the keys that MOVE the
highlight and to the popup's own lifecycle, so a burst and the same keys paced always end on the
same option:

- An arrow steps from the highlight the state has decided (a pending request, else aria's active
  option) over the options the state lists, wrapping at both ends; from no highlight ArrowDown
  takes the first option and ArrowUp the last (§4.3). A held arrow repeats (aria: `ignoreRepeat:
  false`).
- A fresh result set still resets the highlight (first result for a typed query, nothing for a
  browse list or an empty set), whether it lands before or after an arrow in the same task.
- An arrow while nothing is listed (an async search unanswered) moves nothing, exactly as paced:
  Enter then follows §4.3/§7.3 (resolution standalone, the grid's commit in a cell).
- A popup that opened in this task has its initial search issued before the first key that needs
  its rows is decided; a popup that reopens gets a clean state first.

The alternatives were rejected for the reasons PC-F03 recorded: deferring Enter until the relay
has run reintroduces the dropped/late Enter; draining aria's relay synchronously is not possible
from outside (the slot and its effect are private) and would still step over unrendered rows.

### What was built

`client/projects/core/tellma-core-ui/entity-picker/tm-entity-picker.ts`:

- **The capture-phase layer owns plain ArrowUp/ArrowDown while the popup is open** (it already
  owned Enter). It stops propagation, so aria's relay never sees the key, prevents the default
  (caret stays, §4.3), and steps synchronously: `keys = optionKeys()`, from
  `keys.indexOf(keyboardActiveKey())`, `+1` or `-1` modulo the count, with ArrowUp from "none"
  landing on the last option. The step issues a highlight request for that option's key. The old
  special case (ArrowUp from no highlight → `listBehavior.last()`) is subsumed and removed.
- **`optionKeys()`** is the listbox order derived from the picker's state: the result ids, then the
  footer sentinels (advanced search, create, edit) under the same conditions the template uses.
- **Highlight requests carry the option key** (`{ key, seq }`, replacing `{ query, count, seq }`).
  `applyResults` computes the key (first id for a typed non-empty set, else `undefined`); the
  after-render effect applies a request with `listBehavior.goto(option._pattern)` for the rendered
  option whose value is that key, or `unfocus()` when there is none. One path serves result sets
  and arrows.
- **`keyboardActiveKey()`** returns a pending request's key, else aria's active option, and in both
  cases only if `optionKeys()` still lists it. That replaces PC-F03's `state.query ===
  request.query` guard and also covers an applied highlight on a row the state has since dropped.
- **`clearHighlight()` also drops a pending request.** Before, a keystroke cleared aria's active
  option but a request not yet applied (now possibly an arrow's) would have outlived its rows.
- **Popup lifecycle.** The combobox binding is now explicit (`[expanded]` +
  `(expandedChange)="onExpandedChange($event)"`), so every transition, aria's included, is recorded
  synchronously with one flag, `popupPending`. `syncPopup()` runs what the latest transition owes,
  once: the initial search of an open (`onPopupOpened`) or the reset after a close
  (`onPopupClosed`). It is called by the transition effect (as before, in the next change
  detection) and, earlier, by Enter, the arrows and Tab before they decide. A reopen that finds an
  unprocessed close runs the close reset first (`onExpandedChange`). The close itself is still not
  acted on synchronously: a cell commit closes the dropdown first and the grid adopts the in-flight
  search after.
- `onInput` records the open itself (`setExpanded(true)`) before it queues the search. aria's own
  `input` listener does the same and today runs first (host listener before template listener), so
  this line changes nothing observable now; it is there so that "close, then type" in one task
  cannot reset the keystroke's own search if that listener order ever changes.
- An arrow during an IME composition (`isComposing`, or keyCode 229) is left to the IME: no move,
  no `preventDefault`. Its propagation is still stopped, as for Enter, so aria does not relay it.

No grid source changed. `e2e/support/keyboard-burst.ts`: a key may carry `+`-joined modifiers
(`'Alt+ArrowDown'`), so the dropdown chord can be part of a burst; text is inserted only for keys
without Alt/Control/Meta.

### Tests

All are deterministic: keys are dispatched back to back in one task (`pressInOneTask` in e2e,
synchronous `dispatchEvent`s in unit tests), with no waits or thresholds between them.

- Unit, `tm-entity-picker.spec.ts`, "arrow keys delivered faster than the page renders"
  (standalone, 19): keystroke + ArrowDown + Enter → second result; first-keystroke variant; Tab
  variant; several arrows; the painted highlight (`aria-activedescendant`) equals the paced one;
  held-key repeats; wrap in both directions; ArrowUp from none; a footer row + Enter opens its
  page; a keystroke after an arrow resets to the fresh first result; async answer landed but
  unrendered; async unanswered; ArrowDown that opens + ArrowDown + Enter → first browse row, one
  search; reopen + Enter; pick + reopen + arrow + Enter → picks from the new list; Escape + reopen
  + Enter commits nothing; arrows visit every option in rendered order (the `optionKeys()` mirror);
  pick + keystroke searches once, unaborted; IME arrow.
- Unit, the same file under "as a grid cell editor" (5): keystroke + ArrowDown + Enter; seed +
  ArrowDown + Enter; Tab; the dropdown chord + ArrowDown + Enter; async unanswered.
- Unit, `tm-grid-editing.spec.ts` (3), through the real grid: type-to-edit + text + ArrowDown +
  Enter commits the second result with no move; the Tab variant moves; Alt+ArrowDown from
  navigation + two arrows + Enter commits the second browse row with one search.
- e2e, `entity-picker.spec.ts` (8) and `grid-entity.spec.ts` (6): the same matrix on the showcase
  with sync and async (held) sources.

### Verification

- **Before the fix** (picker source reverted to d554208, tests kept):
  - `tm-entity-picker.spec.ts`: 19 of the 24 new tests fail. The five that pass are pins of
    behaviour that was already right and must stay so: "a keystroke after an arrow in the same
    task resets the highlight", the two "an arrow while an async search is unanswered moves
    nothing" tests, "arrows visit every option in the order it is rendered" (paced; it ties
    `optionKeys()` to the template), and "a pick and a further keystroke in one task search the new
    text once, unaborted" (guards the explicit open in `onInput`).
  - `tm-grid-editing.spec.ts`: 3 of 3 fail.
  - e2e: 12 of 14 fail; the two "an arrow before the async search answers moves nothing" tests are
    pins. Each failure was the reported behaviour: the first result where the second was due, one
    move for several arrows, or nothing picked.
- **After the fix:**
  - Unit, full client suites (`pnpm run test`): core 32, core-testing 3, core-ui 1414 (the 27 new
    ones included), core-ui CSP 1, core-ui-tokens 20, locale-ar 8, showcase 1, all passing.
  - e2e PR tier (`--project chromium --project touch --grep-invert "@nightly|@perf|@quarantine"`,
    CI=1 prod build, 2 workers): **549/549 passed**, none on retry.
  - Stability, e2e: the two touched spec files in full (90 tests, the 14 new ones included),
    Chromium, CI=1 prod build, `--workers 2 --retries 0 --repeat-each 10`: **900/900 passed**
    (35 min on a loaded local Windows machine).
  - Stability, unit: the two touched spec files (`tm-entity-picker.spec.ts`,
    `tm-grid-editing.spec.ts`) run 10 times: 10 × 200/200 passed.
- **Gates** (from the repo root, D-032): lint, typecheck, lint:test, tokens:check, build, test,
  api:check (after `api:approve`; the only change is the added protected member
  `onExpandedChange(open: boolean): void`), docs:build and size:check all pass. Size:
  entity-picker **11.41 of 12 KB** (11.28 before); grid 38.00 of 38 KB, unchanged, because no grid
  source changed. The first lint:test run had one 5 s timeout in an untouched stylelint-rule test
  (`tools/tests/tm-no-hardcoded-sizing.spec.mts`) while the machine was loaded by other lanes; the
  rerun passed 73/73.
- No .NET file changed, so `dotnet build` / `dotnet format` / the dotnet suites were not run.
- ARCHITECTURE.md needs no change: it mentions the picker only generically.

### Deviations

1. **Spec 0006 §4.3 gives the arrows to aria** ("aria owns the keyboard model"; "three of these
   override aria's own bindings, through one capture-phase listener"). Plain ArrowUp/ArrowDown
   while the popup is open are now handled in that capture-phase layer, over the picker's own
   option list. The observable keyboard model is the spec's (wrap, ArrowUp-from-none → last,
   caret untouched, repeat); only the owner changed, because aria's relay is deferred and holds one
   key.
2. **§4.3 says the auto-highlight uses "the public listbox first-item navigation API".** The
   highlight is now applied by key through `listBehavior.goto(option._pattern)`, so that one
   mechanism applies both a result set's first row and an arrow's target. Same result for the
   auto-highlight; one more aria-internal seam (see risks).
3. **Arrows during an IME composition are left to the IME.** Before, aria consumed them and moved
   the listbox highlight while the IME's candidate window was open. The spec is silent; this is
   PC-F03's rule for the composing Enter applied to the arrows.
4. **Popup lifecycle timing.** The spec is silent on keys that arrive in the task that opened or
   reopened the popup. The initial search of an open is now issued by the first key that needs the
   rows (else by the next change detection, as before), and a reopen runs the previous close's
   reset first. Visible effect: "ArrowDown, ArrowDown, Enter", "pick, ArrowDown, ArrowDown, Enter"
   and "Escape, ArrowDown, Enter" in one task now do what they do paced.

### Open risks

- **Reliance on aria internals changed**: `listBehavior.goto()` and `Option._pattern` are new,
  `listBehavior.last()` is gone, `listBehavior.unfocus()` and `hasBeenInteracted` stay. All are
  version-locked to `@angular/aria` 22.0.3 and exercised by the paint and wrap tests; an aria
  upgrade must re-run them.
- **`optionKeys()` duplicates the template's option order.** If a footer row is added or reordered
  in the template and not there, arrows would skip or misorder it. Pinned by "arrows visit every
  option in the order it is rendered" (all three footer rows present).
- **A pointer press between an arrow and the next render.** The pending request is applied after
  that render and would move the highlight off the pressed row; the click's pick reads aria's
  active option. This needs a mouse press and a key within one frame, and aria's own deferred relay
  had the same window; not addressed.
- **PageUp/PageDown** still go through aria's relay (the listbox binds neither, so they do nothing,
  paced or not). **Home/End** are the caret's (unchanged).
- `onPopupOpened()` can now run inside a keydown (the first arrow/Enter/Tab after a same-task
  open) instead of in the next change detection. With a synchronous source that means the consumer's
  `search` callback runs inside that keydown.

### What the reviewer should examine hardest

- **`popupPending` / `onExpandedChange` / `syncPopup`**: every interleaving of open and close
  within one task, from both writers (the picker's `setExpanded`, aria's own `expanded.set`), and
  the constraint that a close must NOT reset synchronously (grid adoption after `commit()`). In
  particular: open → close → open, close → open → close, and an open whose search was never issued
  before the close.
- **The explicit binding that replaced `[(expanded)]`**: that aria's model and the picker's signal
  cannot drift (PC-F03 R1-1), including when aria closes on blur from its own after-render effect.
- **`optionKeys()` against the template**, including `showsFooterRows()` / `showsEditRow()` in
  every search state (idle, loading, results, empty, error), and duplicate or non-primitive ids.
- **`keyboardActiveKey()`'s validation**: whether it can reject an option the user is looking at
  (rows rendered from a state that `resultItems()` no longer reports), or accept one that is gone.
- **`clearHighlight()` nulling the request**: every caller, and whether any of them relied on a
  pending request surviving.
- **The arrow branch's guard order** in `onCaptureKeydown` (modifiers, read-only, disabled,
  composition, `syncPopup`) and that Alt+ArrowDown/ArrowUp, the read-only arrows and the collapsed
  arrows still take their previous paths.
- **That the burst tests are not vacuous**: each fails on d554208 except the pins listed under
  Verification, and the helper really runs every key in one task.

## Review 1

### Findings

**R1-1 (minor): `optionKeys()` is a second, hand-kept copy of the template's option list, and
every keyboard decision now rests on it.**
`client/projects/core/tellma-core-ui/entity-picker/tm-entity-picker.ts:1563-1579` against the
template at `:277-349`. Before this change the list only had to be right in the template: aria
stepped over the rendered options. Now the arrows step over `optionKeys()` (`:1953-1958`) and
`keyboardActiveKey()` refuses any option it does not list (`:2206`), for Enter and Tab too. The two
copies agree today; I checked every search state (idle, loading, results, empty, error) and the
three footer conditions. Failure scenario: a later change adds, reorders or re-conditions an option
row in the template and misses `optionKeys()`. The row is then unreachable by arrow keys (for a
footer command the arrows are the only keyboard path, the one §8 relies on to justify the
magnifier's `tabindex="-1"`), or the highlight visits rows in an order other than the drawn one.
Nothing fails at build time, and the one guard,
`tm-entity-picker.spec.ts:1485` ("arrows visit every option in the order it is rendered"), covers
a single configuration (a browse list with all three pages set): a row that depends on another
input or state never appears in it. Suggested fix: give the footer rows one source. A
`footerKeys` computed (the sentinels under today's conditions, in order) that the template renders
with `@for (key of footerKeys(); track key)` and that `optionKeys()` appends to the result ids. If
restructuring the template is not wanted, the cheaper guard is a dev-mode check in the highlight
after-render effect (`:1046`), where state and DOM agree: compare
`ariaOptions().map((o) => o.value())` with `optionKeys()` and `console.error` on a difference, so
every popup test in the suite becomes a guard.

Checked and found sound:

- **aria internals, against the installed 22.0.3 source** (`client/node_modules/@angular/aria/fesm2022`).
  `ComboboxPattern` parks ArrowUp/ArrowDown in the single-slot `keyboardEventRelay` and re-dispatches
  from an `afterRenderEffect` (`private.mjs`), as the notes say. `List.goto(item)` reaches
  `ListFocus.focus`, which sets `activeItem` with no selection change when called without options
  (`_list-chunk.mjs`, `_list-navigation-chunk.mjs`): the same effect as `gotoFirst()` for the first
  row. `Option._pattern` is the very object in `inputs.items()` (`listbox.mjs`). Applying by key is
  in fact sturdier than `gotoFirst()`: aria's item order is refreshed by a `MutationObserver`, so it
  lags a re-ordered `@for`, and the picker no longer depends on it. `_pattern`, `listBehavior` and
  `goto` are typed in aria's `.d.ts`, so a rename fails `typecheck`; a change of meaning fails the
  paint and wrap tests. The catalog pins `@angular/aria` to exactly 22.0.3.
- **Deviations 1 to 4.** Each keeps §4.3's observable model. Plain arrows were already consumed by
  aria while open (`preventDefault` and `stopPropagation` at the input), so nothing new is hidden
  from the grid or from host listeners. Wrap, first-on-ArrowDown and last-on-ArrowUp from no
  highlight, key repeat, the untouched caret, scroll-into-view and `aria-activedescendant` (same
  number of render passes as the relay took) are unchanged. Home/End, PageUp/PageDown, Shift- and
  Alt-modified arrows, the read-only and collapsed-in-a-cell arrows and Escape take the paths they
  took before; no option is ever disabled, so skipping disabled rows is moot. Vertical arrows are
  direction-neutral (the RTL e2e passes). The IME rule matches the one Review 1 of PC-F03 asked for
  on Enter, and the grid ignores a composing key on its own.
- **No double step, skip or desync.** The capture listener stops the key before aria's input
  listener, so the relay never holds an arrow. A pending request answers until the effect applies
  it, then aria's active option does; both are validated against the state's options, so a row the
  state dropped commits nothing. Every result set either clears the highlight first (`queueSearch`)
  or issues a fresh request (`applyResults`), so an arrow's target never outlives its rows.
- **Popup lifecycle.** I walked open→close→open, close→open→close, an open whose search was never
  issued, both writers (`setExpanded`, aria's `expanded.set` from a key, from Escape and from its
  blur effect), and the one-way binding (every write reaches both models at once, so the binding
  can only push a value aria already holds). Keys call `syncPopup()` only while expanded, so a
  close is still never reset synchronously and the grid's adoption after the picker's
  close-then-clone Enter still finds the in-flight search. The initial search inside a keydown
  re-enters nothing: no path from `onPopupOpened()` writes `expanded`.
- **The pointer window listed under Open risks is not reachable.** aria activates the pressed
  option on `click`, in its host listener, which runs before `onListboxClick` in the same event; a
  pending request cannot come between them. Arrows and a click dispatched in one task picked the
  clicked row.
- **Tests.** The helper runs every key in one synchronous loop inside one `evaluate`. I did not
  rerun the suites on d554208: that needs the picker source reverted and this review may modify no
  file. Traced against the pre-fix code instead, these fail as claimed: keystroke/ArrowDown/Enter,
  the held arrow, ArrowUp from no highlight, the IME arrow, Escape then reopen, and the grid chord
  with two arrows. The five unit pins and two e2e pins pass before by construction. The
  grid-hosted picker is covered at all three levels (5 cell-editor tests, 3 through the real grid,
  6 e2e).
- **Rules.** No spec, PC-id or `docs/` reference in TSDoc, test names or messages; the one spec
  citation added is an inline comment. Every new member and helper is documented. The API golden
  gains only the protected `onExpandedChange`. The commit message lists the suites run and all
  four deviations; no .NET file changed.

Runs (local Windows, 20 cores):

- Unit, `tm-entity-picker.spec.ts` + `tm-grid-editing.spec.ts`: six runs, each 200/200. Two of
  them ran their tests beside 20 CPU-burning threads, the other four without that load.
- e2e Chromium, CI=1 production build, `--retries=0`: `entity-picker.spec.ts` + `grid-entity.spec.ts`
  in full, 90/90 (2 workers). Both burst describes of both files (26 tests) with
  `--repeat-each=20 --workers=4` and 20 CPU-burning threads for the first 9 of 13 minutes: 520/520.
- `typecheck` and `lint` pass.
- By hand on the production showcase, keys dispatched in one task: Escape + ArrowDown (reopens on
  the stored answer, first result highlighted, no request); Escape + ArrowDown + Escape, then a
  paced reopen; Escape + a keystroke; a pick + three ArrowDowns (second browse row highlighted, one
  search); in a grid cell, Escape + Alt+ArrowDown + ArrowDown + Enter (second result committed, no
  move, no second search). Each ended where the paced keys end.

### Responses

R1-1: fixed in cafad9e, with the preferred fix (one source). The footer rows are now one computed,
`footerRows`: the rows that render, in order, each with its option key (the sentinel) and its
caption, under the conditions the three `@if` blocks used to spell out (not while loading; a page
configured; Edit… only while the field names an entity). The template draws the footer from it
(`@for (row of footerRows(); track row.key)`, with the separator shown when it is non-empty) and
`optionKeys()` is now `resultItems()` mapped through `idOf`, followed by `footerRows()` mapped to
their keys. Both halves of the list are thus read from the same two signals the template iterates,
in the same order, so a row that is added, reordered or re-conditioned changes the drawn list and
the keyboard's list together. What a later change could still get wrong is the order of the two
`@for` blocks themselves (results, then footer), which the existing "arrows visit every option in
the order it is rendered" test pins. Removed as dead: `showsFooterRows`, `showsEditRow`, the three
`action*` sentinel fields and the three `*RowLabel` computeds (all protected, template-only). The
effect that re-applies the listbox value on footer-row turnover now reads `footerRows()`. Rendering
is unchanged: same elements, classes, values and labels, and the rows still leave and return as a
group around the loading state.

The reviewer's check that the pointer window listed under Open risks is not reachable (aria
activates the pressed option in its own `click` listener, before `onListboxClick` in the same
event) is accepted: that risk is withdrawn. The "`optionKeys()` duplicates the template's option
order" risk is closed by this fix.

Round-1 verification (local Windows, CI=1 prod builds, 2 workers):

- Gates: lint, typecheck, lint:test, tokens:check, build, test (core 32, core-testing 3, core-ui
  1414, core-ui CSP 1, core-ui-tokens 20, locale-ar 8, showcase 1), api:check (after
  `api:approve`: the eight removed protected members, and `footerRows` added), docs:build and
  size:check all pass. Size: entity-picker **11.28 of 12 KB** (11.41 before this round, 11.28
  before PC-F04); grid 38.00 of 38 KB, unchanged.
- PR-tier e2e (chromium + touch, `--grep-invert "@nightly|@perf|@quarantine"`): 549/549 passed,
  none on retry.
- entity-picker.spec + grid-entity.spec in full, Chromium, `--repeat-each 3 --retries 0`: 270/270
  passed. The two touched unit spec files: 200/200.
- No .NET file changed; the dotnet gates were not run.
