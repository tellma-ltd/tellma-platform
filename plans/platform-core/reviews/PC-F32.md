# PC-F32: hold the boot step idle sweep in the client store spec

## Review 1

Commit reviewed: `00fc427`, parent `70326bb`.

Verdict: the diagnosis is right and the fix removes the race by construction. The fix holds the
idle callback; it does not widen the assertion. No blocking or major findings. One minor finding.

What I checked and found sound:

- **The diagnosis reproduces.** I took the parent's spec and added a 500 ms wait before the
  strict `bus.posted` assertion of "sweeps every user first when the boot record names no user".
  - On the parent, the test fails (60/61 pass). The extra post is
    `{ type: 'store.changed', partition: 'u:0b4f/s', names: null }`: the `sessionOtherThan` sweep.
    Log: `.tmp/pcf32-r1-repro-parent.log`.
  - With the same probe on `00fc427`, all 61 tests pass. Log: `.tmp/pcf32-r1-repro-fix.log`.
  - I reverted the probe, and `git status` is clean.
- **The chain is as described.** `ɵstart` registers `afterNextRender`
  (`tm-client-store.ts:577-590`). The zoneless scheduler's tick runs it, and it calls
  `this.#window.requestIdleCallback`. `#window` is `inject(DOCUMENT).defaultView`, which is the same
  `window` the spy patches. The lookup happens at call time, so the spy holds it.
- **Production is correct per spec 0025 §2.6.** Every sweep posts `store.changed` with its prefix.
  The idle `sessionOtherThan` sweep runs "at idle after first render". So the cold path posts two
  messages with distinct prefixes (`u:` and then `u:0b4f/s`), and that is specified.
  - A receiving tab re-reads only what it mirrors under each prefix, so the second message costs
    at most one readonly transaction there and changes nothing.
  - On the cold path, the idle sweep is redundant after the backstop, but harmless.
  - Ordering or de-duplication would be an optimisation, not a fix. It is out of scope for a
    test-only stop-the-line item.
- **Held callbacks do not leak.**
  - The hold is a `vi.spyOn` inside `describe('TmClientStore')`, and `vi.restoreAllMocks()` in the
    same suite's `afterEach` restores it. Held callbacks live in a per-test array that nobody keeps,
    so they are dropped.
  - Vitest 4.1.9 runs `afterEach` hooks inside-out: this suite's restore first, then the Angular
    builder's file-level TestBed cleanup hook. Only microtasks separate them
    (`@vitest/runner` `callSuiteHook`). So an `afterNextRender` still pending at a test's end is
    destroyed with the environment injector before any scheduler macrotask can call the restored
    `requestIdleCallback`.
  - Nothing reaches the next test or the next file of the shared, non-isolated browser page.
- **No test lost the sweep it needs.**
  - Only "sweeps the user's other sessions at idle after the first render" asserts the idle
    sweep. It still holds the callback itself, ticks, runs `idle[0]`, and checks the keys left.
  - The second `spyOn` there re-targets the existing spy, so `idle` receives the callback.
  - Every other sweep assertion calls `store.sweep` directly.
  - "reads a warm launch in one readonly transaction, and nothing else" had held its callbacks
    explicitly before this commit. It now gets the same hold from the `beforeEach`.
- **Comments are in order under AGENTS.md.** `holdIdleCallbacks` has TSDoc. The new inline
  comment explains the hold and cites no spec, PC id or plan. No test name changed.
- **The gates pass.**
  - `ng test core`: 484/484 on three runs (`.tmp/pcf32-r1-core-{1,2,3}.log`; runs 1 and 2
    overlapped, run 3 ran alone).
  - eslint on the spec file is clean.
  - `pnpm run lint:test` does not include the core project's specs
    (`tools/vitest.config.mts:15`), so this commit cannot affect it.
    - Both of my runs failed only in `tellma-core-tools`. Run 1 hit my 600 s cap mid-build of the
      fixture. Run 2 had 228/229 passing, and the failure was a 5 s timeout in `common.spec.ts`
      "starts a package manager's shim…", on a machine loaded by parallel suites.
    - That file passes alone (7/7).
    - Logs: `.tmp/pcf32-r1-lint-test{,-2}.log`, `.tmp/pcf32-r1-common-alone.log`.
    - This is environmental, not a finding.

### Findings

- **R1-1 (minor): a `vi.restoreAllMocks()` in the middle of a test silently lifts the new hold,
  and in one test it does so after `ɵstart`.**
  - Location: `client/projects/core/tellma-core/src/state/store/tm-client-store.spec.ts:619`, in
    "starts cold in memory, and never rejects, when a transaction fails after the open". The same
    pattern is at `:608`, in "drops a write the quota refuses", which does not call `ɵstart`.
  - Scenario: the call at `:619` is meant to undo `loseTheConnection()`. It also restores
    `requestIdleCallback`, after `ɵstart(17)` has already registered `afterNextRender`. If the
    scheduler's tick runs during the rest of the test, the real `requestIdleCallback` queues the
    `sessionOtherThan` sweep, which can then run during a later test.
  - Impact today: none. The store is in memory mode by then, so the sweep touches no disk and
    posts nothing. But it contradicts the new comment at `:210-214` ("Only a spec that runs a held
    callback sees it"). A future test that copies this pattern on an IndexedDB-backed store would
    bring back the cross-test leak this commit closes.
  - Suggested fix: at `:619` (and `:608`), restore only the transaction spy. Have
    `loseTheConnection` and `failTransactions` return their spy and call `.mockRestore()` on it.
    Alternatively, call `holdIdleCallbacks()` again right after the mid-test restore.

### Responses

- R1-1: fixed in the "PC-F32: review 1 fixes" commit (child of 00fc427), per orchestrator ruling.
  `failTransactions` and `loseTheConnection` return their `IDBDatabase.transaction` spy, and the
  two tests call `.mockRestore()` on it instead of `vi.restoreAllMocks()`, so the idle hold stays
  in place to the end of each test. No mid-test `vi.restoreAllMocks()` is left in the spec; the
  only one is the `afterEach`.
