# PC-F18: client test infra under parallel slot load

## Implementer notes

### What and where

**(a) Unit: the menu-Paste wait** (`client/projects/core/tellma-core-ui/grid/tm-grid-clipboard.spec.ts`).

- `eventually` was local to this file, not shared, and had exactly one caller: "menu Paste reads
  the async clipboard and drives the same ladder". It looped at most 20 `stable()` turns, each one
  `whenStable` + one `setTimeout(0)` + `whenStable`, so it gave up after roughly 100 ms of wall
  clock and returned silently. The real `Blob.text()` that `pasteFromAsyncClipboard` awaits
  settles on its own task queue; on a loaded machine that took longer, so the following `expect`
  saw `Alpha` and the test went red (CI on ef67f47).
- It is now deadline-based: it polls with the same `stable()` turn until the predicate holds or
  `timeoutMs` (default 2 s, a named constant, overridable per call) passes. On expiry it throws
  `Timed out after 2000 ms waiting for <what>.`, where `<what>` is a required description
  argument. A condition that already holds returns without waiting. 2 s sits well inside
  vitest's 5 s per-test default, so a condition that never holds fails with that message rather
  than as an anonymous test timeout.
- I verified the failure path by temporarily pointing the predicate at a value that never
  arrives: the test failed after 2 s with
  `Error: Timed out after 2000 ms waiting for the menu paste to reach the model.` (reverted).
- I kept the mock's real `Blob` rather than making `text()` resolve at once. The test is meant to
  exercise the grid's real read path (`getType()` → `Blob.text()`), and the deadline alone
  removes the race.
- Other turn-capped waits: I grepped the client for `for (… i < N && !…)` loops and found no other.
  `tm-file-preview.spec.ts` and `tm-image.spec.ts` each have their own `until`, which is already
  deadline-based (15 s, throws). The neighbouring "degrades the menu Paste item" test waits two
  `stable()` turns for a `Promise.reject` from the mock. That rejection settles within microtasks
  that a single `setTimeout(0)` drains, so it does not race, and I left it alone.

**(b) E2E: the real-clipboard lock** (`client/e2e/support/clipboard-lock.ts`; the API
`useExclusiveClipboard()` and its three callers in `grid-clipboard.spec.ts`, `tree-grid.spec.ts`
and `copy.spec.ts` are unchanged).

- **Per checkout.** The lock moved from `os.tmpdir()/tm-e2e-clipboard.lock`, which every slot on
  the machine shared, to `client/.artifacts/e2e/clipboard.lock`. That path is resolved from the
  module's own URL, sits beside the run's report and results, and is git-ignored by the root
  `.artifacts/` rule. Playwright cleans only `results/` and `report/`, never the lock.
- **The wait no longer counts against the test timeout.** The `beforeEach` still sets the
  test's 60 s budget. It then calls `acquire(test.info())`, which after every failed poll calls
  `testInfo.setTimeout(timeout + time since the last credit)`, and once more after it acquires.
  Playwright 1.61's `TimeoutManager.setTimeout` re-arms the running slot's deadline at once
  (`deadline = start + timeout - elapsed`), so the deadline moves forward while the test waits,
  and the test body keeps its full 60 s.
- **Fails clearly instead of hanging.** After 10 minutes of waiting (`MAX_WAIT_MS`), the test
  throws an error naming the lock path, the holder's pid and when it took the lock, and saying
  to delete the directory if no run is in progress.
- **Faster recovery from a crash.** The owner record now carries the holder's pid. A lock whose
  owner process is gone (`process.kill(pid, 0)`, where only ESRCH counts as gone, on Windows and
  Linux) is reclaimed at once. The age fallback (`STALE_MS`) rose from 120 s to 300 s. The pid
  check covers crashes, and a live holder can keep the lock for up to its 60 s budget plus a
  teardown that Playwright bounds by the same budget again, which the old 120 s could cut close.
  A lock directory without a readable owner record (a holder that died between `mkdir` and
  `writeFile`) is reclaimed once its mtime is more than 10 s old. Before, it waited for ever.
- **Purpose kept.** Within one run, tests that use the real clipboard still never overlap.

### Deviations and findings

- **The cross-slot clobbering the old temp-dir lock seemed to guard against does not happen in
  headless runs.** On this Windows machine I launched two Playwright 1.61.1 Chromium browsers
  (default headless, the chromium-headless-shell 149 that the e2e config uses) with clipboard
  permissions. A `writeText` in one was invisible to `readText` in the other, and a fresh
  browser read an empty clipboard. So in headless runs each browser process (that is, each
  worker) has its own clipboard, and a per-checkout lock loses nothing against other slots.
  A headed run (`--headed`, debugging) writes the OS clipboard, which every browser shares. The
  per-checkout lock still serializes such a run's own workers, but no longer guards it against
  a second checkout's headed run at the same moment. That is an interactive, single-developer
  situation. The doc comment says what was observed, without claiming more.
- Following from the above: in the default headless runs, the lock appears to guard nothing
  even within a run, because each worker has its own browser. I kept it as asked: it is cheap,
  it covers headed runs and any switch to Chrome's new headless mode, and removing it would need
  a Linux check that I could not run here.

### Worker count under load (PC-069's 33 timeouts): recommendation, no change

- Local (non-CI) runs use `max(2, floor(cpus / 3))` workers. That is 6 on this 20-thread
  machine, so four slots running e2e at once start 24 Chromium workers, plus four dev servers
  and any builds. CI keeps Playwright's default (half the vCPUs) on a runner it has to itself.
- I see no clear and safe automatic fix. `os.loadavg()` is always `[0, 0, 0]` on Windows. Load
  sampled at start-up predicts nothing about slots that start a minute later. Dropping the
  local default would slow every single-checkout run.
- Recommendation: when several slots run the PR-tier e2e at once, add `--workers 2` to the
  command (`node scripts/e2e.mjs … --workers 2`). `e2e.mjs` already passes it through to
  Playwright, and PC-069's rerun with it passed. If you want it automatic, a later change could
  read an opt-in env var (for example `TM_E2E_WORKERS`) in the config, set by the orchestrator
  for slot runs. That is a process change, so I left it to the orchestrator.

### Risks

- `testInfo.setTimeout` called from inside a running hook is documented Playwright behaviour
  (the "extend the timeout in beforeEach" pattern). The live re-arm I rely on is what 1.61.1
  does. A future Playwright that only applies `setTimeout` at the next slot would make a wait
  longer than 60 s time out again. The e2e check below exercises this path.
- Reclaiming a lock is still check-then-remove, not atomic. Two waiters that both judge the
  same dead lock stale could race. This was already true before, and with the pid check it can
  happen only after a worker crash.
- pid reuse: if a dead holder's pid is reused by an unrelated process, the lock waits out the
  300 s age fallback instead of being reclaimed at once.

### What to examine

- `acquire()`'s credit arithmetic: each wait interval is credited exactly once, and the last
  poll before success is credited too.
- That `test.info()` in the `beforeEach` refers to the current test (Playwright's documented
  accessor inside hooks).
- That the eslint and typecheck runs cover `e2e/` (`tsc -p e2e/tsconfig.json`).
- My e2e check does not exercise `MAX_WAIT_MS` (a 10-minute wait).

### Tests run

All on the branch fast-forwarded to origin/platform-core 9007c81.

- `pnpm run lint`, `pnpm run typecheck`, `pnpm run lint:test` (23 files, 90 tests): pass.
- `ng test core-ui --include grid/tm-grid-clipboard.spec.ts`: 30/30 pass. A temporary
  never-holds predicate failed after 2 s with the message above (reverted).
- **Lock wait excluded from the timeout.** I ran `grid-clipboard.spec` on chromium with
  `--workers 2` while a separate live process took the lock as soon as the run started and held
  it for 75 s. All 17 tests passed. The two real-clipboard tests queued behind it took 1.3 min
  each, longer than their 60 s budget, so the wait was credited. The lock was gone afterwards.
- **Dead-owner reclaim.** Before the full run below, I planted a lock owned by a nonexistent pid
  (`process.kill` → ESRCH) with a fresh timestamp. The first real-clipboard test reclaimed it.
- **PR-tier e2e** (`node scripts/e2e.mjs --project chromium --project touch --grep-invert
  "@nightly|@perf|@quarantine" --workers 2`, the orchestrator's cap while other slots build):
  779/779 passed in 13.6 min, the real-clipboard tests included. No lock was left behind.

## Review 1

### Findings

Scope: e32415c against 9007c81. Runs, all on e32415c: `pnpm run lint`, `pnpm run typecheck`
(it covers `e2e/tsconfig.json`) and `pnpm run lint:test` (23 files, 90 tests) pass.
`ng test core-ui --include grid/tm-grid-clipboard.spec.ts` passed 30/30 five times out of five.
`node scripts/e2e.mjs grid-clipboard.spec.ts --project=chromium --workers=2 --repeat-each=3` passed
51/51 in 2.7 min, and no `clipboard.lock` was left in `client/.artifacts/e2e/`. Logs are in
`.tmp/r1-pcf18-*`.

Checked and found sound (no finding):
- **Credit arithmetic.** `credited` starts at `start` and moves to `now` on every credit, so the
  total added is `end - start`, with each interval counted once. A `continue` after a reclaim does
  not credit, but the next credit picks up that interval. The time added beyond real waiting is
  only the lock's own mkdir/writeFile I/O, a few milliseconds.
- **Live re-arm in Playwright 1.61.1.** `TimeoutManager.setTimeout` writes the running slot and
  calls `_updateTimeout`, which re-arms with `deadline = start + timeout - elapsed`. `beforeEach`
  hooks run with no slot of their own (`_runEachHooksForSuites(suites, "beforeEach", testInfo)`),
  so they use the default slot. The `testInfo.timeout` getter (`defaultSlot().timeout`) and the
  slot that `setTimeout` writes are therefore the same slot, and the read-modify-write is
  consistent. The after-hooks slot is `max(project timeout, testInfo.timeout)`, so `release()`
  always gets at least the extended budget.
- **Release paths.** A test failure or timeout still runs `afterEach`. If `afterEach` is skipped
  because its slot is exhausted, the test fails and the worker is replaced; the pid then dies and
  the lock is reclaimed. A crashed worker's lock is reclaimed through the pid check.
- **`process.kill(pid, 0)`.** On Windows, libuv opens the process with `OpenProcess`.
  `ERROR_INVALID_PARAMETER`, or an exit code other than `STILL_ACTIVE`, maps to ESRCH, and access
  denied maps to EPERM. That matches the `isAlive` reading. On Linux, the Playwright runner reaps
  its workers, so a dead holder does not linger as a zombie that `kill(0)` reports alive. A reused
  pid is bounded by the 300 s age fallback. A leftover lock from an earlier day is reclaimed at
  once by age, whatever its pid now names.
- **Paths and ignore rules.** `fileURLToPath(new URL(..., import.meta.url))` works on both OSes,
  and `client/package.json` is `"type": "module"`. `.artifacts/` is ignored by both the root and
  `client/` `.gitignore`. Playwright cleans only `results/` and `report/`.
- **Unit helper.** The mock still returns a real `Blob`, so the grid's `getType()` ->
  `Blob.text()` path is exercised. The failure message names what was awaited, and a predicate
  that already holds returns at once.
- **AGENTS.md.** Every new declaration has TSDoc, and the non-trivial logic has inline comments.
  No spec refs, PC ids or `plans/` paths appear in the code, the test names or the messages.
- Note on the implementer notes only: there are three callers of `useExclusiveClipboard()`, not
  two. `client/e2e/specs/copy.spec.ts:128` is the third, and it is unaffected.

**R1-1 (minor): in headless runs the lock serializes tests that cannot interfere. Gate it on
headed runs instead of keeping it as unconditional machinery.**
`client/e2e/support/clipboard-lock.ts:189-199` (`useExclusiveClipboard`), and the module doc at
`:12-34`.

The implementer's observation is plausible, and the evidence for it goes further than the notes
say:
- Chromium's headless modes install an in-memory, per-browser-process clipboard (Chromium's
  `HeadlessClipboard`). The headless shell that the e2e config uses does this.
- On Linux CI there is no display server, so a headless browser has no OS clipboard to reach.
  That settles the "would need a Linux check" caveat.
- Playwright launches one browser per worker, and the tests within one worker are sequential.
- The lock's original motivation never held up. In the July CI triage that introduced it,
  `workers=1` and `fullyParallel:false` failed the same way. The real causes were the
  gesture-focus and announcement races, which were fixed separately.

So in every default and CI run the lock guards nothing, yet it still has costs:
- Every real-clipboard test across all workers waits behind one queue. In the implementer's run,
  queued tests took 1.3 min each.
- About 200 lines of cross-process concurrency code are kept for every run. This lock is the code
  that has produced the lock-related local failures in the LEDGER.

Scenario: a PR-tier run on 6 local workers queues every `useExclusiveClipboard` test behind one
holder. That stretches wall time under exactly the slot load this item is about, and it buys no
isolation.

Suggested fix: make the hooks take the `headless` fixture, and skip both acquire and release when
it is true, for example `test.beforeEach(async ({ headless }, testInfo) => { if (headless) return;
... })`. `release()` is already a no-op when nothing is held. Reword the module doc to say the
lock exists for headed runs, where the OS clipboard is shared. Keep the setTimeout(60 s) outside
the gate if the 60 s budget is still wanted for these tests. If the gate is adopted, R1-2 and
R1-3 matter only for headed debugging, and they can be fixed or documented accordingly.

**R1-2 (minor): two waiters can reclaim the same stale lock and both end up holding it, and an
`rm` failure on the reclaim path now fails the test instead of retrying.**
`client/e2e/support/clipboard-lock.ts:141-145`.

Scenario: a previous run was interrupted (Ctrl+C or an OOM kill) while a worker held the lock.
The next run starts its first real-clipboard tests on workers W1 and W2 at the same moment:
1. Both fail `mkdir`, read the same dead-pid record, and judge it abandoned.
2. W1 runs `rm`, `mkdir` and `writeFile`, and holds the lock.
3. W2's `rm`, decided on the old record, then deletes W1's fresh lock, and W2 `mkdir`s.

Both now believe they hold the lock. The window is small, but it opens at exactly the moment
several workers start together. The old code had the same check-then-remove race.

The `rm` at `:144` is also no longer inside a try/catch. The old code swallowed a failed reclaim
and retried. Now a transient Windows EPERM/EBUSY (a delete-pending directory, a scanner holding
`owner`) rejects `acquire()` and fails the test with a raw fs error.

Suggested fix: reclaim by atomic rename rather than in-place `rm`. `rename(LOCK_DIR, <LOCK_DIR>.<token>.stale)`
succeeds for only one waiter. Then read the tombstone's owner, and remove it only if its token is
the stale token that was judged abandoned; otherwise rename it back. At minimum, re-read the owner
and compare tokens immediately before `rm`, and wrap the `rm` in try/catch -> `continue`. If R1-1
is adopted, a sentence in the module doc naming the residual race is enough.

**R1-3 (minor): `mkdir` errors other than EEXIST are treated as "held", so a missing parent
directory spins for 10 minutes and then blames a phantom holder.**
`client/e2e/support/clipboard-lock.ts:135-140`, `:112-118`.

The parent is created once, before the loop, and the `catch` at `:139` handles every `mkdir`
error the same way. Scenario: `client/.artifacts/` is removed mid-run, for example by
`git clean -fdX` in a second terminal or by a script that wipes `.artifacts/`. Every later
`mkdir(LOCK_DIR)` throws ENOENT. `readOwner()` returns null, and `isOrphaned()`'s `stat` throws,
so it returns false. Each real-clipboard test then waits the full 10 minutes, with that wait
credited, before failing with "held by an owner whose record could not be read". The suite stalls
for 10 minutes per test, and the message points at the wrong cause.

Suggested fix: inspect `(error as NodeJS.ErrnoException).code`. On ENOENT, re-create the parent
(`mkdir(dirname(LOCK_DIR), { recursive: true })`) and `continue`. Treat EEXIST, plus EPERM/EACCES
for a delete-pending directory on Windows, as held. Rethrow anything else.

**R1-4 (minor): the unit wait's 2 s budget is sized against the wrong runner timeout.**
`client/projects/core/tellma-core-ui/grid/tm-grid-clipboard.spec.ts:199-206`.

The notes justify 2 s as "well inside vitest's 5 s per-test default". These specs run in browser
mode (`chromiumHeadless`), where vitest 4.1.9 defaults `testTimeout` to 15 s
(`testTimeout ??= browser.enabled ? 15e3 : 5e3`). The budget is spent only when the wait fails.
The flake it replaces was a load-induced stall of the `Blob.text()` task.

Scenario: under the same four-slot load, a starved renderer delays the paste chain past 2 s, and
the test fails with the new message instead of passing at 2.5 s. That is less likely than before,
but nothing is gained by cutting the budget that short.

Suggested fix: raise `EVENTUALLY_TIMEOUT_MS` to about 10 s, which is still inside the 15 s
browser-mode timeout so the named failure still wins. Correct the doc comment's rationale to
state the browser-mode timeout it is sized against.

### Responses

All four fixed in the fix-round commit; the caller list in the implementer notes now names
`copy.spec.ts` as the third caller.

- **R1-1: fixed.** `useExclusiveClipboard()`'s `beforeEach` takes the `headless` fixture and
  acquires only when it is false; the 60 s `setTimeout` stays outside the gate, and `release()`
  is a no-op when nothing is held. The module doc now says headless Chromium keeps a
  per-browser-process clipboard and each worker has its own browser, so headless runs (every
  default and CI run) take no lock, and the lock exists for headed runs, where the OS clipboard
  is shared. The PR-tier e2e below left no `clipboard.lock` behind, as expected.
- **R1-2: fixed with rename plus a token check.** A waiter that judges the lock abandoned calls
  `reclaim(staleToken, token)`. That renames `clipboard.lock` to `clipboard.lock.<token>.stale`,
  which only one waiter can do. It then reads the owner of the renamed directory. If the token
  matches the stale token (for an ownerless lock: still ownerless and still older than the 10 s
  grace), it removes the directory; otherwise it renames it back. Every failure inside
  `reclaim` is swallowed, and the caller always sleeps and retries, so a transient EPERM or
  EBUSY no longer fails the test and the loop never spins hot.
  - Residual: if a third waiter creates a fresh lock in the instant between a wrong rename and
    the rename back, the rename back fails and two holders believe they hold it. This needs
    three-way timing after a crash, and only in headed runs.
- **R1-3: fixed.** The `mkdir` catch switches on the error code:
  - ENOENT re-creates the parent (`recursive`) and retries at once, so the up-front parent
    `mkdir` is gone.
  - EEXIST, EPERM and EACCES count as held (the last two cover a Windows delete-pending
    directory).
  - Anything else is rethrown.
  - The 10-minute error now carries the last `mkdir` error as `cause`; lint's
    `preserve-caught-error` asked for it.
- **R1-4: fixed.** `EVENTUALLY_TIMEOUT_MS` is 10 s. The doc now says the budget is spent only
  when the wait fails or the machine is starved, and that it stays inside the browser-mode
  runner's 15 s per-test timeout. I confirmed `testTimeout ??= browser.enabled ? 15e3 : 5e3` in
  vitest 4.1.9.

Verification (logs in `.tmp/pcf18-fix-*`):
- **Lock harness.** A throwaway harness under the ignored `client/.tmp/` raced a copy of the
  module across 6 processes × 15 acquisitions each, checking the critical section with an
  exclusive-create marker file. It ran three scenarios: a planted lock with a dead pid, an
  ownerless lock 60 s old, and a missing output directory. Result: 0 overlaps in 270
  acquisitions, all exits 0, and no `.stale` directories or locks left behind.
- **Gates.**
  - `pnpm run lint`, `pnpm run typecheck`: pass.
  - `pnpm run lint:test`: 23 files, 90 tests, pass.
  - `ng test core-ui --include grid/tm-grid-clipboard.spec.ts`: 30/30 pass.
  - `node scripts/e2e.mjs grid-clipboard.spec copy.spec --project chromium --workers 2
    --grep-invert "@nightly|@perf|@quarantine"`: 27/27 pass in 2.9 min, and no lock directory
    was created.
