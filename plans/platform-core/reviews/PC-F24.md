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
