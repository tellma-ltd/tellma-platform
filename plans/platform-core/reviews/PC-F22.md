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
