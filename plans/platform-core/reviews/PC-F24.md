# PC-F24: keys queued behind a consumer write to activeRowId act on the stale active row

## Implementer notes

### Root cause: the pending write is not in the grid yet

- **Symptom.**
  1. A page writes the grid's `activeRowId`. Spec 0030 does this when results land and it seeds
     the first row. The story's Jump button does the same.
  2. The user's next ArrowDown or Enter arrives before the next render. Queued input runs ahead
     of the zoneless scheduler's timer task, so this happens on a busy page.
  3. The key acts on the old active cell. With no active row, ArrowDown goes to row 0 and Enter
     opens row 0.
  4. The render then applies the write and moves the cell to the seeded row. So the move is lost,
     and the row that opened is not the one highlighted.
- **Why the suggested fix cannot work alone.** PC-F22 R1-2 suggested applying the pending write
  at the top of `moveActiveRow` and `activateRow`, comparing the model with the last synced value.
  - That assumes the write has already reached the grid's model signal.
  - With the two-way binding every consumer uses (`[(activeRowId)]="highlight"`, spec 0010 §C.2
    and spec 0030), the consumer writes its own signal. The grid's model input only changes when the
    consumer's template refreshes, which is during the render.
  - Until then, neither the grid nor the driver can see the write.
  - I built that fix first (a field for the synced id, the effect body factored into a method,
    and calls from `moveActiveRow`, `activateRow`, `onKeydown` and `onPointerDown`). All four new
    tests failed with it, with the bug's own signature, because the model they read was still
    `null`.
- **What can apply the write: a render.** Only a change-detection pass carries the consumer's
  write into the grid. So the fix renders pending changes at the input boundary, before the key
  acts.

### Fix

- **Driver.** `grid/tm-grid-driver.ts`, `TmGridDriver.onKeydown`.
  - Before a driving key acts (ArrowDown, ArrowUp, PageDown, PageUp or Enter, after the existing
    IME and modifier filters), it calls `ApplicationRef.tick()`.
  - Typed characters, Home, End and the horizontal arrows stay untouched and render nothing
    (`DRIVING_KEYS`).
  - This is the driver's fix, not the grid's: the driver sits on the consumer's input. `tm-tiles`
    (spec 0023 §8) gets the fix for free through the same directive.
- **Grid's own keys.** `grid/internal/grid-core.ts`, `onKeydown`.
  - The same tick runs after the overlay check, unless an editor session is open. An open editor
    owns its keys; this leaves the editing and keymap paths alone, which the popup-editors slot is
    changing now.
  - Focus can sit in the grid when the write lands. ArrowDown or Enter then starts from the
    written row.
- **Re-entrancy.** DOM handlers run outside a tick in zoneless Angular. The core already relies on
  this for synchronous ticks in `openFind`, the draft input and the editor mount. So the call
  cannot hit NG0101. With nothing dirty, a production tick does no work: `synchronize()` loops
  only while `dirtyFlags !== 0`. Dev mode also runs its `checkNoChanges` pass.
- **The API methods do not tick.** `moveActiveRow` and `activateRow` stay as they were. A consumer
  may call them from an effect, which runs inside a tick, and a nested tick would throw NG0101.
  - So code that writes the bound signal and then calls `moveActiveRow` in the same task still
    sees the old row.
  - That is the binding's ordinary semantics. The TSDoc now states when a write takes effect.
- **No pointer flush. This is a deliberate scope limit.** I tried a tick at the top of
  `onPointerDown` as well, and rejected it. The tick rendered the write, which scrolled the grid
  (the reveal), so the element under the pointer belonged to another row by the time the handler
  resolved the press. The press opened nothing. Without the tick, the press lands where the user
  aimed, and the render that follows moves the highlight to the written row.
  - That is the pre-existing behaviour for a press within the same frame as a write. It is
    narrow: a person cannot press within a frame of results landing in any reliable way.
  - It is not a lost key, so it is out of this item's scope.
- **TSDoc.**
  - `TmGridBase.activeRowId`: a write reaches the grid with the next render, and a key in the
    grid or a driver's driving key renders pending changes first.
  - `TmGridDriver`: a driving key renders pending changes before it acts.
  - The Enter comment in the driver was reworded. The model is still never read there.
- **Size.** `./grid` measures 40.91 → 40.97 KB on the linked measure (D-067), against its ceiling
  of 41.01. The ceiling is unchanged. `tree-grid` and `grid-engine` are unchanged.
- **No public API change.** The API goldens are unchanged.

### Tests

- **Unit.** `grid/tm-grid-list.spec.ts`, "tm-grid active row model and driver". The new helper
  `holdRenders` fakes `setTimeout`, `clearTimeout`, `requestAnimationFrame`,
  `cancelAnimationFrame` and `Date`, so the zoneless scheduler's raced render cannot run until the
  test releases it, even across an awaited microtask. That is "a busy page", made deterministic.
  - "starts a driver key from a consumer write the page has not rendered yet". The model is set
    to 5, then ArrowDown and Enter go to the search field. Row 6 must open, the model must read 6,
    and focus must stay in the field.
  - "starts the grid's own keys from a consumer write the page has not rendered yet". Focus is in
    the grid at row 1. The model is set to 20, then ArrowDown and Enter. Row 21 must open, in the
    same column.
  - "applies an unrendered consumer write over rows that changed in the same task". New rows and
    the seeded id land together, then ArrowDown. The rows reconcile before the id is looked up,
    and the cell lands on 111.
- **Mutation check.** With both fixes reverted, all three fail with the bug's signature:
  - `[[0,'keyboard']]` instead of `[[6,'keyboard']]`;
  - `[[2,'keyboard']]` instead of `[[21,'keyboard']]`;
  - 110 instead of 111.

  With the fixes, the file passes 39 of 39.
- **E2E.** `e2e/specs/grid-search-page.spec.ts`, "the consumer-written active row" › "keys queued
  right behind the write start from the written row", in both directions. In one task, it clicks
  Jump (the model goes to 40) and dispatches ArrowDown and Enter on the search field. It expects
  "41 via keyboard", one activation, the active row 41 with its class, and focus still in the
  field. The wait is deterministic: all the input is in a single `evaluate`, and no render can
  run inside it.

### Evidence

Logs are under `.tmp/pcf24-*`.

- **Client gates.** lint, typecheck, tokens:check, build, size:check, api:check (no drift),
  docs:build, test and build:showcase all pass. Core-ui runs 2386 of 2386.
- **lint:test.** 226 of 229 pass under load from the parallel slots. The three timeouts are in the
  tools common, build and serve specs (the D-078 class). They pass 25 of 25 when run alone.
- **PR-tier e2e.** `CI=1`, chromium and touch, `--workers 2`: 1068 of 1068 passed, none flaky.
- **Changed spec.** `grid-search-page.spec.ts`, chromium, `--repeat-each 10 --retries 0`: 440 of
  440 passed, including 20 runs of the new test.

### For reviewers: examine hardest

1. **Is a synchronous `ApplicationRef.tick()` on every grid keydown (no editor open) acceptable?**
   - In production it is free when nothing is dirty.
   - When something is dirty, it is the same render, only earlier.
   - In dev mode it adds a `checkNoChanges` pass per key.
   - A synthetic keydown dispatched from inside a tick (an effect or an `afterRender` hook calling
     `dispatchEvent`) would now throw NG0101. The existing `openFind`, draft and mount ticks
     already carry this exposure for their keys.
2. **Does skipping the tick while an editor is open leave a real gap?** A consumer write during
   an edit still waits for the render, as before.
3. **Is the pointer limit acceptable?** A press within a frame of a write keeps the old outcome.
4. **Do header keys depend on `event.target` after the tick?** A pending column change could
   re-create the header cells before `headerSlotOf` reads the target's slot attributes. I judged
   this harmless: the attributes still name the slot.

## Review 1

Scope: commit bc96fd7d only. I re-ran `ng test core-ui --include grid/tm-grid-list.spec.ts`: 39/39
pass (`.tmp/r1f24-unit-list.log`). `.tmp/pcf24-unit-mutant.log` confirms all three new tests fail
with the fix reverted.

Judgement on the fix direction. The deviation holds. With `[(activeRowId)]="highlight"` the
consumer writes its own signal, and the grid's model input changes only when the consumer's
template refreshes. So a check inside `moveActiveRow`/`activateRow` cannot see the write, and a
render at the input boundary is the only place that can.

Judgement on the examine-hardest points:
- NG0101 is acceptable. No in-repo code dispatches a keydown to a grid or a driver from inside a
  tick: the entity picker's `redispatchAbove` runs inside an editor session, where the grid skips
  the tick, and `tm-select`'s relays target its listbox. A consumer that does it gets the
  `RuntimeError(101)`, routed to the ErrorHandler by the listener wrapper, and the key is dropped.
  That is the same exposure the existing `openFind` and draft ticks already have.
- The editor-open gap and the pointer limit are acceptable as documented scope limits, apart from
  the TSDoc wording in R1-3.
- Header keys reading `event.target` after the tick are harmless. `closest()` still resolves on a
  detached node, and the slot is re-queried by number.
- The real costs are R1-1 and R1-2.

### Findings

**R1-1 (major): the "nothing pending renders nothing" premise is false. Every grid key and every
driving key now pays an extra synchronous view refresh.**
- **Where.** `client/projects/core/tellma-core-ui/grid/internal/grid-core.ts:2299-2304`,
  `client/projects/core/tellma-core-ui/grid/tm-grid-driver.ts:114-116`. The same claim appears
  in the commit message and under "Re-entrancy" in the implementer notes.
- **Why it is false.** Angular 22.0.5's template listener wrapper,
  `wrapListenerIn_markDirtyAndPreventDefault` (`@angular/core/fesm2022/_debug_node-chunk.mjs:8136-8137`),
  calls `markViewDirty(startView, 5)` before it runs the handler. That marks the listener's view
  and every ancestor Dirty, and sets `appRef.dirtyFlags |= 4` (`notify`, line 18734).
  `(keydown)="core().onKeydown($event)"` is a template listener in `tm-grid-view`
  (grid-view.ts:92), and the driver's `'(keydown)'` is a host listener in the consumer's view. So
  when `tick()` runs inside these handlers, something is always dirty.
- **What each tick costs.**
  - It re-renders all of `tm-grid-view`: the header `@for`, every rendered row and every cell's
    embedded view.
  - For a driving key, it re-renders the consumer's page template and its ancestors instead.
  - Dev mode adds a `checkNoChanges` pass over every root view.
- **What changed.**
  - A key that acts (an arrow, PageDown, Enter, Space, Delete) used to cost one coalesced
    scheduled render. It now costs two: the forced one, then the render for the signals the key
    changed.
  - Keys that do nothing still render once, now synchronously inside the handler. That covers a
    bare Shift, Control or Alt press, every character typed into a header form field (the
    `inField` path, which keeps every key), and keys on links or buttons inside cell templates.
  - A held ArrowDown pays the extra full grid-view refresh on every repeat, inside the keydown.
- **Failure scenario.** A wide editable grid on a slow device. Each key repeat does about twice
  the render work it did before, and the rationale that justified this ("free") is wrong in the
  source comments.
- **Suggested fix.**
  - At minimum, correct both comments and the TSDoc rationale. Say the tick always refreshes the
    listener's view path, plus a `checkNoChanges` pass in dev mode.
  - Skip the tick where no key can act. Return before it for pure modifier keys (`Shift`,
    `Control`, `Alt`, `Meta`). Move the grid's tick below the `headerSlotOf`/`inField` branch, or
    scope it to the keys the R1-2 fix keeps.
  - Optionally, bind the two keydowns natively (an `addEventListener` registered with
    `DestroyRef` cleanup) rather than through Angular listeners. Then nothing marks the views
    dirty, a tick with nothing pending really is a no-op in production, and the key's own signal
    writes still schedule its render.

**R1-2 (minor): the grid's tick also retargets editing, clipboard and row-structure keys to a cell
the user never saw active.**
- **Where.** `client/projects/core/tellma-core-ui/grid/internal/grid-core.ts:2302-2304`. The tick
  runs for every body key with no editor open, not only for the keys the item names.
- **What else it covers.** On an editable grid that includes:
  - Delete and Backspace (`clear`);
  - Fill down, Delete rows and Insert rows above (`fillDown`, `deleteRows`, `insertRowsAbove`);
  - type-to-edit;
  - Undo and Redo;
  - Ctrl+C, Ctrl+X and Ctrl+V. Their `copy`, `cut` and `paste` events fire after the keydown, so
    after the tick.
- **Failure scenario.**
  1. A master-detail page writes `activeRowId` (say, from a selection in another panel) in the
     same frame the user presses Delete, Ctrl+V or Delete rows on the range they can see.
  2. The tick applies the write first, which moves the active cell and collapses the range to the
     written row.
  3. The destructive action lands on that row, which was never shown highlighted.
  - Before this commit the key acted on the visible target. The later render then only moved the
    highlight, and no data changed outside what the user saw.
  - The item is about navigation and activation ("an ArrowDown or Enter"). For a key that edits,
    acting on the visible target is the safer reading of user intent.
- **Suggested fix.** Tick only for the navigation and activation keys: the `move` intents, Enter
  in row mode (`activateRow`/`enter`), PageDown/PageUp, Home/End. One way is to decide from
  `event.key` and modifiers before resolving, since the resolver needs the post-tick active cell
  for `activeIsBoolean`. Leave editing and clipboard keys on the rendered state. This also cuts
  R1-1's cost for typing.

**R1-3 (minor): the public `activeRowId` TSDoc promises more than the code does.**
- **Where.** `client/projects/core/tellma-core-ui/grid/tm-grid-base.ts:162-165`. It says "a key in
  the grid … renders pending changes first, so a key that follows a write starts from the written
  row".
- **The gap.** The code skips the tick while an editor session is open (grid-core.ts:2302) and
  for keys inside an overlay pane (grid-core.ts:2287-2289).
- **Failure scenario.** A consumer reading the contract expects Enter or Tab in an open editor to
  commit and move from the written row. It does not: the editor's cell decides, and the later
  render moves the cell.
- **Suggested fix.** Add "(while no editor is open)", or "a navigation key" if R1-2 narrows the
  set.

**R1-4 (minor): the grid-keys unit test never checks where focus ends, so a lost-key regression in
the very flow it covers would pass.**
- **Where.** `client/projects/core/tellma-core-ui/grid/tm-grid-list.spec.ts:725-741`.
- **The gap.** The first key renders the write, and the test comments that this scrolls the
  pressed cell away, so the focused element is destroyed. The test then dispatches Enter on
  `scroller` ("the scroller takes the grid's keys wherever they land in it"), not on
  `document.activeElement`. The final assertions check only `tabindex`.
- **Failure scenario.** In a browser the second key goes to the focused element. If the tick's
  recycle dropped focus to `<body>` and the reclaim did not land it on the moved cell, the user's
  Enter would be lost. The test would still pass, and no e2e covers the grid-focus path: the new
  e2e covers only the driver.
- **Suggested fix.**
  - Dispatch the second key on `document.activeElement`.
  - Add `expect(document.activeElement).toBe(cellAt(scroller, 21, 1))` after `holdRenders`.
  - Or add an e2e twin of the driver test with focus in a grid cell.

### Responses

- **R1-1: fixed in "PC-F24: review 1 fixes".** The finding is right. I confirmed it in the Angular 22.0.5 source: the
  listener wrapper calls `markViewDirty(startView, 5)` before the handler runs. So a tick from
  inside either handler always refreshed the listener's view path, even with nothing pending.
  - **Measured.** A probe counted the page template's refreshes inside one held-render task.
    - On the previous commit, a grid ArrowDown and a driver ArrowDown, with nothing pending, each
      refreshed the page once synchronously.
    - With this fix, neither does.
  - **Fix.** I took the native-listener route, but only for the flush. The keys' behaviour stays on
    the Angular listeners, so listener order, error routing and what the key's own work marks are
    unchanged.
    - **Grid.** The scroller's viewport effect also registers a capture-phase native `keydown`
      listener on the scroller. For every target inside the scroller, it runs ahead of the
      template's `(keydown)`. `onKeydown` no longer ticks.
    - **Driver.** The constructor registers a native `keydown` listener on its input. A
      directive's constructor runs before its host listeners are registered, so this listener runs
      first.
    - **API golden.** The driver's constructor adds `constructor();` to `TmGridDriver` in the
      API golden (approved). Nothing else in the surface changes.
    - **No removal.** Both listeners sit on an element that lives exactly as long as their owner,
      so neither needs removing.
  - **Result.** With nothing pending, a production tick does no work. Dev mode still runs its
    no-changes check.
  - **Comments.** Both code comments now give this reasoning instead of the false "renders nothing".
  - **Keys that no longer tick.** The flush no longer runs for pure modifier keys, typing, or the
    other non-navigation keys. The grid flush is also narrowed (R1-2).
  - **One shared pattern.** Both flushes test the same key pattern, `ɵTM_GRID_FLUSH_KEY`: the
    arrows, PageUp, PageDown, Home, End and Enter.
    - The driver therefore also flushes for Home, End and the horizontal arrows, and for a driving
      key with a modifier, even though it leaves those keys to the input.
    - With the native listener, that only renders sooner what was pending anyway.
    - It is the price of one pattern instead of two under the size ceiling (below).
  - **New test.** "refreshes no page ahead of a navigation key while nothing is pending" counts
    the page's refreshes through an attribute binding on the search field. It fails on the previous
    commit.
  - **Old at-target order.** Under the old event order at the target, the capture listener runs
    after the template listener when the key's target is the scroller itself. That case keeps
    working; it only misses the saving.
- **R1-2: fixed in "PC-F24: review 1 fixes".** Agreed: a key that edits should act on the cell the user sees.
  - **Which keys flush.** The grid flush now decides from `event.key` alone, before the resolver
    runs.
    - It flushes for the arrows, PageUp, PageDown, Home and End (`ɵTM_GRID_FLUSH_KEY`).
    - It flushes for Enter while row activation is on. Row activation is always readonly, so Enter
      there never toggles a boolean.
    - It skips the flush while an editor session is open.
  - **Which keys keep the rendered cell.** Delete, Backspace, Space, typed characters, Mod+D, undo,
    redo, the row operations and the clipboard keys act on the rendered active cell, as before this
    item. So do Enter and Tab in an editable grid: Enter there can toggle a boolean cell, and Tab
    is outside the item's navigation-and-activation scope.
  - **Alt and the review's suggested cut.** Alt with an arrow does flush: it moves, expands,
    collapses or opens a dropdown on the written cell. None of those changes data. The size budget
    (below) ruled out the extra checks that the review's "decide before resolving" cut needed.
  - **Keys in a header or in-place overlay.** They also flush. That only renders pending changes
    sooner, and `onKeydown` handles them as before (the review found header targets harmless after
    a tick).
  - **New test.** "leaves an editing key on the active cell as rendered" uses a new editable host
    with `[(activeRowId)]`.
    - Row 1 is pressed, then the page writes 3 and Delete arrives in the same task.
    - Row 1 is cleared, row 3 is intact, and the highlight moves to 3.
    - It fails on the previous commit. It fails again when Delete is added to the flushed keys.
- **R1-3: fixed in "PC-F24: review 1 fixes".** The `activeRowId` TSDoc now says which keys render pending changes
  first and which do not.
  - Those that do: a navigation key in the grid while no editor is open (an arrow, PageUp,
    PageDown, Home or End, and Enter under row activation), and a driving key of a `tmGridDriver`.
  - Any other key acts on the active cell as rendered.
- **R1-4: fixed in "PC-F24: review 1 fixes".** The grid-keys test now sends Enter to `document.activeElement`.
  - Inside the held task, it asserts that focus is still inside the scroller.
  - After the render, it asserts that `document.activeElement` is cell (21, 1).
  - Both pass: focus stays on an element inside the scroller through the first key's render, so
    the queued Enter reaches the grid. I asserted the observed outcome and did not trace which path
    keeps focus there.
  - I added no e2e twin. The unit test runs in a real Chromium (vitest browser mode) and covers the
    focus path. The driver's e2e is unchanged.

**Size.** Moving the flush and adding its filter took `./grid` to 41.17 KB, over its 41.01
ceiling. I raised no ceiling. I trimmed the fix instead:
- folded the listener into the existing viewport effect;
- used one key pattern, shared by the grid and the driver, instead of key sets;
- dropped the separate method and the redundant checks;
- dropped the listener removals, which were redundant.

The linked measure is now 41.00 KB of 41.01 (the first commit measured 40.97, and the parent
40.91).

**Evidence.**
- `tm-grid-list.spec.ts` passes 41 of 41 (`.tmp/pcf24-r1-unit*.log`).
- On the previous commit (`.tmp/pcf24-r1-mutant-old.log`), the two new tests fail.
- With Delete added to the flushed keys (`.tmp/pcf24-r1-mutant-delete.log`), the editing-key test
  fails.
- With both flushes disabled (`.tmp/pcf24-r1-mutant-noflush.log`), exactly the three original
  tests fail.

- The mutation runs predate the size trims. On the final code, the file passes 41 of 41
  (`.tmp/pcf24-r1-unit-final.log`).
- `dotnet build` (0 warnings) and `dotnet format --verify-no-changes` were green on this branch.
  No .NET file changed after that.
