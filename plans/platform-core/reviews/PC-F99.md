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

## Review 1

Scope: commit b5b403e1 only. Read: the LEDGER row (CHECKPOINTS.md has no PC-F99 entry), D-011, D-303
and D-398, CONTRIBUTING.md's rule for waits across a chunk load, PLAN.md §6-§7, the diff and the
changed specs, `tmSettleUntil`/`tmWaitUntil`, and the product paths each wait crosses
(`TmDisplayNames.resolve`/`#read`, the pickers binding's `displayWith` and search, `TmErrorState.act`,
the `/tenants` route and `tmShellRoute`).

What I checked and found sound:

- The named conditions. Each one is the state the old assertion reads, and none passes early. The
  three `label(...)() !== null` / `displayWith(9) !== null` conditions turn true only when `#hold`
  stores the row (or marks it missing, in which case the exact assertion that follows fails with
  `#9`, not with a timeout). `#hold` runs only after `#read` has awaited both `TmEntities.load()` and
  the entity model's `import()`, so the plan already comes from the descriptor. `tmWaitUntil`'s step
  ticks after each task, so the view has rendered the label by the time the condition is checked, and
  the `whenStable()` / `settle()` that follows is extra margin. `router.url` changes only when the
  router commits the final URL (after `loadComponent` and the guards), and a guard redirect cancels
  and starts a new navigation rather than committing a URL in between, so `url !== before` cannot
  pass on an intermediate URL. `signIns().length > 0` followed by the exact `toEqual` is equivalent to
  the old wait.
- `binding.displayWith(9)` as a condition calls `resolve` while the label is null. `#reading` blocks
  a second ask while the read is in flight, and the old loop asked in the same way, so it adds no
  retry. No timeout was raised: the waits move from `vi.waitFor`'s 1 s to the shared 4 s deadline
  that CONTRIBUTING.md prescribes (D-011 holds).
- Item (5): the search-read conversion (`answer !== undefined`) is acceptable. The extra
  `tmSettle()` at the start of `tmSettleUntil` runs before the paste resolver is created and the app
  is reset, so it changes nothing the test asserts.
- The tm-error-state scope extension is the same class: on a first entry, `/tenants` loads through
  `tmShellRoute`'s `import('@tellma/core/shell')`.
- No `vi.waitFor` remains under entity/, entity-editing/ or src/entity/. The fixed `tmSettle()` waits
  left in the asserting paths pass under the harness, which a fixed wait across a delayed import
  would not.
- Grounding (item 4): the cited failure is real and recurring. It appears in three earlier delay-harness
  logs: PC-F80 (null), PC-F87 ("Timed out in waitFor") and PC-F97 (null). It did not reproduce for me
  either, so the fix rests on the rule, as the notes say. That is sound, since the old wait had a 1 s
  budget across a chunk load.

My runs, under the postTask lazy-delay harness (53 sites in 35 files, injected and then reverted;
`git status` was clean afterwards):

- The four specs at b5b403e1^ (the old waits), with a 20-thread CPU burner running: 3/3 runs at
  94/94.
- The entity suites (20 files) at b5b403e1^, with a 40-thread burner: 639/639.
- The entity suites at b5b403e1, with a 40-thread burner: 638/639. The one failure is tm-error-state's
  unchanged `setUp` wait, "the search page lands", which did not hold within 4000 ms on the file's
  first, cold entry. That line predates this commit and is the starvation class (D-294). It is not a
  regression from this change, so it is not a finding here.
- CI on pc/PC-F99 was skipped (run 45931269101), as expected before integration.

### Findings

**R1-1 (minor): the `frame()` cleanup waits still count turns across the display-name read's chunk.**

- Where:
  - `client/projects/core/tellma-core/entity-editing/src/tm-entity-field.spec.ts:146-155`, used at
    :218.
  - `client/projects/core/tellma-core/entity-editing/src/tm-value-editor.spec.ts:171-181`, used at
    :397 and :408, and possibly :419 (I did not trace whether the read-only input there asks for its
    name).
- What they do: both helpers say they wait for "the reads it starts", which "must not outlive the
  test". Each is one `requestAnimationFrame` plus `tmSettle()` (five tasks) plus `whenStable()`.
- Why that is a fixed count across a chunk: in each of those tests the user may query the target,
  and the id is queued before it is known. This holds for centerId 1 at :218. At :397 and :408 the field's
  `displayWith` queues the id through `resolve` on first render, before `remember` runs, and `#flush`
  does not check the queue again. So the read flushed on that frame awaits `import('./tm-entity-model')` before it sends its
  query, and five turns do not bound that.
- Failure scenario: under the harness (300 ms per import) or under load, the read is still pending
  when the test ends, which is exactly what the helper says it prevents. It is harmless today only
  because the read's later `injector.get(TmApi)` on the destroyed app throws into `#read`'s `catch`.
  If that guard changes, a stale read reaches into the next test. The uses at :269 and :322 do not
  cross the chunk, because a user who may not query returns before the import.
- Neither file's notes list these as left. tm-entity-field.spec is one of the three files this item
  names.
- Suggested fix: at each call site that crosses the chunk, wait on the read's state with
  `tmSettleUntil` and a named condition, for example `rig.names.label('gl.Center', 1)() !== null`, or
  the route's call count in `rig.api.calls()` together with the label. The alternative is to drop the
  "must not outlive the test" claim and say why a late read is harmless. Either way, record the
  choice in the notes.

### Responses

R1-1: fixed in fix1 (the commit titled "PC-F99: fix1 (R1-1)"). The finding holds, including :419.

- A probe confirmed it. A temporary spec edit, reverted, logged the fake server's calls right after
  each `frame()` and again 1.5 s later.
  - With no harness, every crossing read had already been sent before `frame()`:
    - centers `Id in (1)` at tm-entity-field :218;
    - currencies `Id in (3)` at tm-value-editor :397 and :408;
    - users `Id in (42)` at :419. The read-only input's `TmFormats.text` asks for the name on
      first render, before `remember`.
  - Under the postTask harness, none of the four had been sent when `frame()` returned. Each was
    sent within the next 1.5 s. tm-entity-field :269 and :322 sent nothing in either mode, as the
    review says.
- The fix:
  - A new shared helper in `client/projects/core/tellma-core/src/entity/tm-entity.testing.util.ts`,
    `tmDisplayNameRead(rig, route, id)`. It waits with `tmSettleUntil` for the read's own query
    (the route, with an `Id in (<id>)` filter leaf), named "the display-name read of <id> on
    <route> is sent". It then runs one `tmSettle()`. Once the query is recorded, the fake answers
    on a microtask, and the rest of the read loads no chunk, so a fixed settle bounds it. CONTRIBUTING
    allows that.
  - It replaces `frame()` at all four crossing sites.
  - The condition is the query, not the label. The label is no help at these sites:
    - at :218, the 'Nowhere' search has already remembered row 1;
    - at :397, :408 and :419, the spec remembers the row itself;
    - the :408 and :419 reads have no responder, so they are refused and never set a label.
- tm-value-editor.spec no longer uses `frame()`, so the helper is gone.
  - tm-entity-field.spec keeps `frame()` for :269 and :322, whose user may not query: their read
    ends at the permission check, before the entity model's chunk.
  - Its TSDoc now says it is only for that case.
- Runs: the entity suites (20 files, 639 tests, which include both changed specs), each 3 times:
  - under the postTask harness: 3/3 at 639/639. The harness was reverted (53 sites), and
    `git status` then showed only this change.
  - plain: 5 runs. tm-entity-field.spec, tm-value-editor.spec and every other entity spec passed
    in all 5. In 4 of the 5, one test failed: tm-error-state's first test, at its unchanged
    `setUp` wait ("the search page lands", did not hold within 4 s). That is the line this review
    saw fail under the burner. It is not from fix1. With fix1 stashed, at b5b403e1, the same set
    failed it in 3 of 4 runs.
    - The file passes alone (6/6).
    - In the set, its first test is the first entry into `@tellma/core/search`. That entry pays the
      chunk's cold load while the other 19 files load in parallel. The failure screenshot shows the
      page landing after the deadline.
    - PC-F99 changed only the file's later test, not `setUp`, and my earlier plain runs at b5b403e1
      passed 2/2. So this is pre-existing and depends on load. I reported it to the orchestrator
      rather than widening this fix.
  - eslint is clean on the three changed files. lint:test passed. tsc tellma-core spec shows
    only the existing TS6059 notices.
- The screenshot those failures write is gitignored, and I deleted it.

Found during fix1: tm-error-state's setUp wait paid the cold load of the search chunk. Fixed in fix2
(the commit titled "PC-F99: fix2 (tm-error-state cold search chunk)").

- The cause:
  - The first test's `setUp` navigates to `/17/centers`. `TmRouteHost.load()` only starts
    `import('@tellma/core/search')` after the navigation has ended, so the cold load of that chunk
    falls inside the 4 s "the search page lands" wait. The same is true of the tenant entry and
    the Entities chunk.
  - In the 20-file run, the other files load in parallel. Timing probes (temporary, reverted)
    measured the first `setUp` wait at 3388 ms, or past 4 s (failed), against about 60 ms for each
    later test.
  - The cold import alone took 1.0-1.5 s in the 20-file run and 0.5 s with the file alone.
- The fix: a `beforeAll` in the spec awaits `import('@tellma/core/search' as string)`, with a
  comment saying why. This follows tm-entity-actions.spec's warm-up of its dialog chunks. The route
  still loads the chunk with its own `import()`, and the module graph under test is unchanged.
  - No deadline was raised and no retry was added.
  - With the warm-up, the first wait measured 338-839 ms in three 20-file runs.
- Runs:
  - Reproduced first, at d560f34c, plain: 2 of 7 valid runs failed (an eighth run was set aside
    because my edit landed while it was starting). Before that, 7 of 9 failed.
  - After the fix, plain: 5/5 at 639/639, and the file alone 6/6.
  - After the fix, under the postTask harness: 3/3 at 639/639 (53 sites injected). The harness was
    reverted, and git status was clean.
  - One no-warm-up probe run also had a load stall that timed out three unrelated entity tests at
    15 s. That is the starvation class, not this item's.
  - eslint is clean on the spec. lint:test passed 563/563 on the second run.
    - The first lint:test run failed two tests in tools/tests/vitest-instances.spec.mts ("run client
      on one copy of vitest and one of vite…", for client and for Acme's Client).
    - Run alone right after, a third test in the same file timed out at 5 s (14.9 s), under the
      same machine load.
    - That spec checks the installed node_modules, which this item does not touch. I reported it
      as a load-sensitive candidate flake.
