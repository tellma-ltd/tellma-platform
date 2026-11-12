# PC-F22: search-page Enter lost behind queued arrow keys (CI red on 9007c81)

## Implementer notes

### Root cause: a product race in `tmGridDriver` (product bug: yes)

- **Symptom.** CI run 43190228814 failed the PR-tier e2e job on one flaky test,
  `grid-search-page.spec.ts:124` (ltr), "Enter opens the active row; Mod+Enter opens it in a new
  tab".
  - The first attempt pressed ArrowDown, ArrowDown, Enter, then waited 5 s for `activated` to read
    "1 via keyboard". It kept reading "none": the Enter opened nothing.
  - The retry passed in 0.9 s, and the RTL variant passed.
- **Cause.** The driver's Enter branch gated on `target.activeRowId() === null`.
  - `activeRowId` is the grid's two-way model. The grid writes it back from an effect, which runs
    with the next change detection.
  - `moveActiveRow` moves the engine's active cell synchronously, but the model does not follow
    until that render.
  - Chromium dispatches queued input ahead of timer tasks. Under load (a cold CI worker), the
    keydowns for ArrowDown, ArrowDown and Enter can all run before the zoneless scheduler renders.
  - Enter then saw `activeRowId() === null` and left the key to the input. The row the arrows had
    reached was never opened, and nothing was prevented.
- **Users are affected, not only tests.** A fast typist, or anyone on a busy page, who presses
  ArrowDown then Enter loses the Enter the same way.
- **Not the cause.** The R1-4 repeat guard and the 400 ms double-activation guard.
  - Neither applies to the first Enter on a row.
  - The guard returns `true`, which would have consumed the key, not passed it through.
  - The test already waits on real state: the readouts are driven by the grid's events.

### Fix

- `grid/tm-grid-driver.ts`. The Enter branch no longer reads `activeRowId`.
  - A plain Enter calls `activateRow()` directly. It resolves the row from the engine's active
    cell synchronously and returns `false` when there is none. In that case the key stays the
    input's own, as before.
  - A repeat (a held Enter) is consumed only when `activeDescendantId()` is non-null. That is a
    `computed` over the engine's active cell, so it is current as soon as `moveActiveRow` returns.
- **Contract in TSDoc.** The `TmGridDriverTarget` TSDoc now states it:
  - `activeRowId` may trail a move by a render, and the driver's key handling never reads it;
  - `activeDescendantId` and `activateRow` reflect `moveActiveRow` as soon as it returns.
- **No public API change.** The API golden is unchanged.
- **Size.** `./grid` still measures 42.00 against its ceiling of 42, so size:check passes.

### Tests

- **Unit.** `tm-grid-list.spec.ts`, "opens the row an arrow just reached, before the page renders
  again".
  - It dispatches ArrowDown, ArrowDown, Enter, ArrowDown, Mod+Enter and a repeating Enter in one
    task, with no render in between.
  - Expected: activations `[1, keyboard, false]` and `[2, keyboard, true]`, the repeat consumed,
    and the model at 2 after the render.
  - It fails on the old driver ("expected false to be true" on the first Enter's
    `defaultPrevented`) and passes with the fix.
- **E2E.** `grid-search-page.spec.ts`, "an Enter queued right behind the arrows opens the row they
  reached", in both directions.
  - It sends the same three keydowns to the search field in one task: the CI condition, made
    deterministic.
  - It fails on the old driver in both directions with CI's exact signature (expected "1 via
    keyboard", received "none") and passes with the fix.
- **No sleeps or retries added.** The existing test is unchanged; it was correct.

### Evidence

**The new tests reproduce the bug.** With the driver fix stashed:
- the unit test fails on the first Enter;
- the new e2e test fails in both directions with "Received: none", the CI signature.

**With the fix:**
- **The spec file, 20 times.** `grid-search-page.spec.ts --project chromium --repeat-each 20
  --workers 2` passed 840 of 840.
- **The full suite in CI mode.** `CI=1` serves the production build, as CI does, and fails on
  flaky tests: `--project chromium --project touch --grep-invert "@nightly|@perf|@quarantine"
  --workers 2` passed 781 of 781, with none flaky and none retried.
- **Unit tests.** `pnpm run test` passed every project (core-ui 1821 of 1821).
- **Other client gates.** lint, typecheck, lint:test, build and size:check are green.
- **api:check.** Green on a clean build; the golden is unchanged.
  - A first local run reported drift: `rowActivation` printed as `"none" | "row"` from a warm
    `.angular/cache`.
  - With the cache moved aside, the rebuilt d.ts prints `"row" | "none"` and api:check passes.
  - CI's clean build also passed api:check on 9007c81 with the committed golden.
  - The union order in the d.ts depends on the build cache. That is worth knowing when a local
    api:check reports this one line as drift.
- **No backend changes.**

## Review 1

### Findings

Checked and found sound (no finding):
- **The root cause is right, and the gate was the whole of it.** Before the fix, the Enter branch
  gated on `activeRowId`, which only the effect at `grid/internal/grid-core.ts:4950-4988` writes,
  during change detection. `moveActiveRow` (`grid-core.ts:1574-1601`) and `activateRow`
  (`grid-core.ts:1609-1630`) both read `engine.nav.activeCell()` synchronously. So the old driver
  always opened the engine's row: the model was only a gate, and a stale `null` gate is exactly the
  CI signature ("none", nothing prevented).
- **The fix changes one case only: model `null`, engine row present.** That case now activates, as
  it should. Every other combination behaves as before:
  - no engine row: `activateRow` returns `false`, so the key stays the input's own (§C.2);
  - Mod: `newTab: mod`, unchanged;
  - 400 ms guard: still returns `true`, so the key is consumed;
  - IME: the 229 / `isComposing` early return is untouched.
- **R1-4's repeat rule is kept.** `activeDescendantId` (`grid-core.ts:1157-1162`) is a `computed`
  over `activeCell` and `rowAt`. That is the same null condition as the model's write-back
  (`rowAt(...)?.id ?? null`), but read fresh. The grid's own repeat consumption
  (`grid-core.ts:1785-1790`) is unchanged.
- **No double activation.** The driver listens on its own input and the grid on its scroller, so
  one keydown reaches only one of them. No caller places a driver input inside a grid.
- **No other driver key reads the model.** ArrowUp/Down and PageUp/Down go through
  `moveActiveRow`. `pageStep` is the core's `pageSize`, which does not depend on the model. Home and
  End stay with the input. After the fix, the driver reads `activeRowId` nowhere.
- **Event ordering is benign.** In the race, `rowActivate` reaches the consumer before
  `activeRowIdChange` does. The payload carries `rowId` and `row`, and the only handler (the story)
  uses the payload.
- **Both new tests fail on the old driver.** No render runs between the synthetic keydowns, so the
  old gate read `null`: the first Enter's `defaultPrevented` would be false in the unit test, and
  `activated` stays "none" in the e2e test. The unit test also covers a repeat on a row reached
  before the render.
- **AGENTS.md is met.** TSDoc is on every touched declaration, and no spec, PC-id or `docs/`
  reference appears in TSDoc or test names.
- **A note, not a finding.** `TmGridDriverTarget.activeRowId` now has no reader through the
  interface; its TSDoc says so. Spec 0010 §C.2 still lists it, and `tm-tiles` (spec 0023 §8) will
  have to implement it. Removing it would be an API and spec change, outside a stop-the-line fix.

Run by the reviewer (logs in `.tmp/r1-pcf22-*`):
- The grid unit specs (`ng test core-ui --include "grid/**/*.spec.ts"`): 7 files, 386 of 386
  passed.
- `CI=1 node scripts/e2e.mjs e2e/specs/grid-search-page.spec.ts --project chromium --project touch
  --repeat-each 10 --workers 2`: 420 of 420 passed in 6.9 min, with no retries and none flaky.
  The touch project does not match this spec.
- `api:check`: all 45 reports `ok`. There was no `rowActivation` drift, so moving the cache aside
  was not needed.

- **R1-1 (minor). No test pins the new repeat branch's "no active row" side.**
  - **Where:** `grid/tm-grid-driver.ts:113-114`, and the gap at `grid/tm-grid-list.spec.ts:617`.
  - **What is uncovered:** a repeating Enter with no active row must stay the input's own (§C.2:
    "otherwise the input's own Enter"). Every existing repeat assertion (`tm-grid-list.spec.ts:443`,
    `:445`, `:448`, `:675`) has a row active.
  - **Failure scenario:** a mutation that consumes every repeat, such as
    `event.repeat ? false : …`, passes the whole suite. A held Enter in an empty search field would
    then lose its repeats to `preventDefault`.
  - **Fix:** next to line 617, add
    `expect(keydown(input, 'Enter', { repeat: true }).defaultPrevented).toBe(false);`.
- **R1-2 (minor; pre-existing, a follow-up is acceptable). The mirror race: a consumer write trails
  the engine too, so keys queued behind it act on the stale active row, and the effect then
  discards the arrow.**
  - **Where:** `grid/internal/grid-core.ts:4957-4975`, with `moveActiveRow` at `:1574` and
    `activateRow` at `:1609`.
  - **Failure scenario:** a consumer write lands, but the effect has not yet applied it.
    1. In the story, Jump sets the model to 40. Spec 0030 does the same when it seeds the first row
       as results land.
    2. ArrowDown and Enter are queued before the next render.
    3. ArrowDown moves the engine from its old active row (none, so row 0), and Enter opens row 0.
    4. The render runs the effect. It sees `requested (40) !== synced` and treats the write as the
       consumer's, so it moves the active cell to 40.
    5. The user opened a row that is no longer highlighted, and the arrow is lost.
  - **Not a regression:** the old gate passed here too (model non-null) and also opened the
    engine's row.
  - **Fix:** factor the consumer-write branch of the effect into a method, for example
    `applyPendingActiveRowWrite()`. Call it from the effect, and also at the top of
    `moveActiveRow` and `activateRow` whenever `untracked(deps.activeRowId) !== synced` (with
    `synced` moved to a field). The driver then always starts from the row the consumer asked for.
    A unit test: `host.active.set(5)`, then ArrowDown and Enter with no render in between, should
    open row 6.

### Responses

**R1-1: fixed.**
- "drives the active row from a search field, which keeps focus" in `tm-grid-list.spec.ts` now also
  asserts that a repeating Enter with no active row is not prevented.
- Mutation check: with the driver's repeat branch changed to consume every repeat
  (`event.repeat ? false : …`), that test fails. With the real driver it passes (36 of 36).

**R1-2: pushed back as a follow-up.**
- It is pre-existing, as the finding says. The old gate opened the engine's stale row in this case
  too.
- The line is stopped on the flaky test only, and that test is fixed.
- The suggested fix is not small enough to fold in here:
  - it moves the consumer-write branch of the active-row effect into a shared method;
  - it promotes `synced` to a field;
  - it calls the method from `moveActiveRow` and `activateRow`.
  That touches the core's two-way model logic. Its bytes would also land in `./grid`, which measures
  exactly 42.00 against its ceiling of 42, so it needs a trim or a budget decision of its own.
- To be tracked as a separate fix item, with the unit test the review proposes (`host.active.set(5)`,
  then ArrowDown and Enter with no render in between, opens row 6).
