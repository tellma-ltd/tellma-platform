# PC-F99: tmSettleUntil for entity specs

## Implementer notes

PC-F99 has no CHECKPOINTS.md entry. It is a fix item, scoped by its LEDGER row, D-303, D-398 and
the brief. No spec section is in scope, and no product code changed.

### What changed

Every `vi.waitFor` in the three files PC-F97 left to this item, on the paths PC-126 landed, is now a
`tmSettleUntil` wait with a named condition, followed by the exact assertion the old wait made
(client CONTRIBUTING.md's rule for waits across a chunk load). No timeout was raised and no retry
was added: the waits move from `vi.waitFor`'s 1 s budget to the shared 4 s `TM_WAIT_DEADLINE_MS`
that the rule prescribes.

- `client/projects/core/tellma-core/entity/src/tm-value.spec.ts`, "reads an unknown related row's
  name in the background". The read is queued on a frame, and `TmDisplayNames.#read` awaits
  `import('./tm-entity-model')` (a delayed site) before it queries. The wait is now on the state
  the read produces, `rig.names.label('gl.Center', 9)() !== null` ("the unknown center is named"),
  then `fixture.whenStable()` and the old DOM assertion (`'W-09'`). A failed read names the row
  `#9`, which ends the wait and fails the assertion with the wrong text, not with a timeout.
- `client/projects/core/tellma-core/entity-editing/src/tm-entity-field.spec.ts`, "shows a chosen
  row by its display name once read". The same display-name read. The wait is on
  `rig.names.label('gl.Center', 3)() !== null` ("the chosen center is named"), then the file's own
  `settle(fixture)` and the harness assertion (`'C'`). The harness's `getText()` is async, so it
  cannot be the condition itself; the label is the state it renders.
- `client/projects/core/tellma-core/entity-editing/src/tm-entity-pickers.spec.ts`:
  - "names a chosen id once its row is read, reading an unknown one" (the one that failed a full
    run under the leaky harness). The same read, through the binding: the wait is on
    `binding.displayWith(9) !== null` ("the chosen customer is named"), then the old
    `toBe('1109 - Bank')`. Calling `displayWith` while it is null asks for the row again, which is
    what the old loop did on every try too; the display names dedupe the ask.
  - "reads nothing once the app ends…". The wait for the search's read to reach the fake server is
    now `answer !== undefined` ("the search's read is sent"). With the rig's Entities chunk already
    loaded, this wait crosses no chunk today: the read reaches the fake on a microtask. It is
    converted anyway, so the file has one wait form and the wait stays right if the rig stops
    loading the chunk first (a search before the chunk waits for `TmEntities.load()`).

### Scope extension: tm-error-state.spec.ts

`client/projects/core/tellma-core/errors/src/tm-error-state.spec.ts` was on PC-F97's skip list as
a pc/PC-126 file, but not among the three it named as chunk-crossing. Its "switches tenant from a
suspended tenant's page…" test waits with `vi.waitFor` for `router.url` to become `/tenants`. The
picker's route is `loadComponent: tmShellRoute('TmTenantPicker')`, and on a first entry
`tmShellRoute` loads the shell chunk through `tm-route-hosts.ts`'s `import()` (a delayed site), so
the router URL changes only after that chunk lands. That is the rule's case, so it is converted:
the wait is on the navigation ending (`router.url !== before`, "the navigation ends"), then the
exact `toBe('/tenants')`, so a navigation that ends elsewhere fails with the URL it reached. The
same test's step-up wait (`TmFakeSession.signIns()`) crosses no chunk; it is converted to
`signIns().length > 0` ("the sign-in starts") plus the old `toEqual`, for one form in the test.

The other `vi.waitFor`s left in client/projects/core are the ones PC-F97 read and left (no
delayed `import()` on their path) and tm-tab-bus.spec (PC-F87's). I did not re-sweep them.

### Runs

Harness: PC-F86's postTask scripts (the gate harness since D-314), `PC-F86-inject_lazy_delay.py`
on `client/projects/core`: 53 sites in 35 files. Removed with `PC-F86-remove_lazy_delay.py` on the
files the injector printed (53 sites); `git status` then showed only the four specs. Logs are in
the shared scratchpad as `PC-F99-*.log`.

- The four files (94 tests), under the harness, before the change: 3/3 runs green. The failure the
  LEDGER row cites was under the retired leaky harness, in a full core run; it did not reproduce
  here in the four files alone.
- The four files, under the harness, after: 5/5 runs at 94/94.
- The entity suites (`entity/**`, `entity-editing/**`, `src/entity/**`, plus tm-error-state;
  20 files, 639 tests), under the harness, after: 2/2 runs at 639/639.
- With no harness, after: the four files 3/3 runs at 94/94, and the entity suites 2/2 runs at
  639/639.
- Gates: eslint on the four files is clean. `tsc -p tellma-core/tsconfig.spec.json --noEmit` shows
  only the 15 TS6059 rootDir notices that platform-core shows without the change. lint:test passes
  563/563. `dotnet build Tellma.slnx` succeeded with 0 warnings, and `dotnet format
  --verify-no-changes` printed only the workspace-load notice (no .NET change).

### Deviations

None from a spec. The tm-error-state conversion goes beyond the three files the LEDGER row names
(above).

### Open risks and what to examine hardest

- Each condition is the display-name state, not the rendered text, and the render is then settled
  once (`whenStable` / `settle`). Check that one settle after the label lands always renders it:
  the label is a signal the view reads, so a tick re-renders it with no chunk in between.
- The non-chunk conversions (the pickers search read and the step-up sign-in) are consistency
  changes, not fixes. Judge whether they belong here.
- The before runs did not reproduce the cited failure, so the evidence for the fix is the rule
  (a wait across a chunk load waits on state with the 4 s deadline), not a red-to-green run.
