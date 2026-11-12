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
`useExclusiveClipboard()` and its two callers in `grid-clipboard.spec.ts` and `tree-grid.spec.ts`
are unchanged).

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
