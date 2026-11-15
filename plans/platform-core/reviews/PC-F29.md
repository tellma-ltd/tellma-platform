# PC-F29: make the load-sensitive tellma-core-tools tests robust

## Implementer notes

### Diagnosis

- **The shim test can never be reliable at 5 s on this machine.** `pnpm --version` alone, from a
  Bash shell with no test running, took 2.9-12.7 s here (six samples: 12750, 6230, 9317, 4453,
  7905 and 6979 ms; 2920 ms for `node pnpm.cjs --version` without the shim). Inside the full
  suite it took 10.6-15.2 s. The old test also ran `node -e` (about 1.1-1.9 s loaded) in the same
  5 s. So the failures at 5016-5121 ms were the test's own cost, not a rare spike. It even failed
  alone once (`pc-1/.tmp/pc030-shim.log`, 5.12 s).
- **The fixture build and the rest of the suite slowed each other.** Vitest ran
  `build.fixture.spec.ts` beside the 36 other files. Its cold `ng build` uses every core for
  minutes, and the typed-lint specs do the same. In the logged history (26 local lint:test logs
  under the slots' `.tmp/`):
  - the build hook hit its 1200 s limit 3 times (runs of 1215-1222 s);
  - `core-lint-scope.spec.mts` hit its 120 s limit once while the build ran;
  - `build.spec.ts` "stops with exit 3 on an index.html problem" hit 5 s once (5.3 s, g8 of
    PC-031) while the build ran;
  - the shim test failed in all the rest.
  On CI the same file takes 50 s on Windows (ng build 36 s) and the whole suite 52 s, so CI has
  stayed green.

### What changed

1. **`client/tools/vitest.config.mts`: two groups, run one after the other.** The suite now has two
   vitest projects. `tools` holds every spec file but the fixture build and runs in parallel as
   before. `fixture-build` holds `build.fixture.spec.ts` alone, with `sequence.groupOrder: 1`, so
   it starts once the `tools` group has finished (vitest 4.1.9 `groupSpecs`, verified in
   `node_modules/vitest/dist/chunks/cli-api.*.js`). `pnpm run lint:test` and the CI steps are
   unchanged, and a single-file run (`pnpm run lint:test <file>`) still works. The `tools` project
   keeps vitest's default excludes next to its own.
   - The fixture build keeps its full coverage and stays in the PR tier. I did not move it to
     nightly. PLAN.md §6 sends a test to nightly only when its wait cannot be driven by a fake
     clock, and this is not a wait: it is the only end-to-end test of the real build pipeline (the
     worker config, ngsw hashes, preloads, source-map move, and the output checks on a real
     output). It takes 50 s on CI.
   - Its 1200 s `beforeAll` limit stays. It now covers only load from outside the suite: alone, the
     cold build took 419-479 s on this machine with other slots building, one run with a
     `dotnet build Tellma.slnx` beside it.
   - Cost: the suite's wall time grows by up to the `tools` group's time, because the build no
     longer overlaps it: 66-99 s here, and at most 36 s on Windows CI, where the other files took
     36 s beside the build in run 43636806694.
2. **`common/process.ts`: the launch decision is a pure function.** `tmProcessLaunch(command, args,
   platform, env)` returns the program to spawn and its arguments. `tmNodeProcessRunner` calls it,
   so its behaviour is unchanged (it still reads `process.platform` and `process.env.ComSpec`).
   The module is internal to the package (not in `exports`), so there is no public-API change.
3. **`common.spec.ts`: one test became six.**
   - Three pure tests cover the Windows shim translation (`pnpm`, `npm` and `npx` through ComSpec
     with `/d /s /c`), the `cmd.exe` fallback when ComSpec is unset, and the pass-through of other
     programs on Windows and of every program on Linux and macOS. They run on every OS, so Linux CI
     now covers the Windows branch, which it never did before. They spawn nothing and keep the
     default timeout.
   - Three real-spawn tests keep the old assertions, one each, under a describe-level 120 s
     timeout with a comment giving the measured cost: the real `pnpm --version` shim start with no
     DEP0190 warning, the exit code of `node -e "process.exit(3)"`, and the rejection of a missing
     program. 120 s is about 8x the worst duration seen in the suite (15.2 s), and it matches the
     suite's other heavy budgets (the typed-lint scopes and the real config-check cases).
   - No assertion was removed or weakened. The real pnpm spawn stays, because only a real `.cmd`
     shim proves that Node starts it without the deprecated shell option.
4. **Sibling: `build.spec.ts`.** Its two describes now carry a 60 s timeout. Each case copies the
   fixture app and runs the build's whole first step on it. The cases took 0.2-2.4 s here, and one
   failed at 5.3 s under load (above). The two real config-check cases keep their own 120 s.
5. **Other siblings checked and left alone**, because each already has an explicit timeout sized
   for load: `tellma-client.spec.ts` bin-script test (60 s; 8.8-12.8 s here), `eslint/index.spec.ts`
   (120 s), `allowed-breakpoints.spec.ts` outside-the-workspace (60 s; up to 15.8 s here),
   `mcp-server.spec.mts` beforeAll (60 s; its calls take under 0.5 s), `union-order.spec.mts`
   goldens (60 s), and the ESLint scope specs (60-120 s). `serve.spec.ts`, `emit.spec.ts` and
   `app-project.spec.ts` copy the fixture too, but start no real program. Their cases took at most
   1.4 s here, and none failed in the logged history, so they keep the default.
6. READMEs: `client/README.md` and `client/projects/core/tellma-core-tools/README.md` say that
   lint:test runs the fixture build spec alone, after the rest.

### Verification

All on this Windows machine, with other slots building in parallel throughout (logs and JSON
reports in `.tmp/pcf29-*`):

| Run | Load | Result | Wall | Shim test | `node -e` | Tools group ends | Cold ng build |
|---|---|---|---|---|---|---|---|
| pre-check | other slots, plus my lint/typecheck | 233/234 (my own pure-test bug, then fixed) | 593 s | 15.2 s | 1.4 s | 56 s | 419 s |
| 1 | `dotnet build Tellma.slnx` for all of it (22:45, Build succeeded, 0 warnings) | 234/234 | 630 s | 10.8 s | 1.1 s | 66 s | 479 s |
| 2 | other slots | 234/234 | 624 s | 13.8 s | 1.2 s | 99 s | 431 s |
| 3 | other slots | 234/234 | 601 s | 10.6 s | 1.9 s | 99 s | 419 s |

- `pnpm run lint` and `pnpm run typecheck` pass. `pnpm run test` passes (all suites).
- `common.spec.ts` alone: 12/12 in 8.4 s.

### For the orchestrator

- D-078's waiver is no longer needed: the test it names is now split, and each real spawn has a
  limit sized to its measured cost.
- The LEDGER "Flaky" row for `build.fixture.spec.ts` (the 1200 s hook timeout) is addressed by the
  same change.

## Review 1

Scope: commit 1 (6a5ebb36). What I checked, and found sound:

- **Coverage.** All three assertions of the old test remain, one per test, and nothing was
  weakened. The real `.cmd` shim launch (`pnpm --version`, with no DEP0190) still runs on every
  OS, so the `client-windows` job's `pnpm run lint:test` covers it on `windows-latest`. The
  fixture build fakes pnpm, so this is the only real shim launch in the suite, as before.
  `tmNodeProcessRunner.run` calls `tmProcessLaunch(command, args)` with its defaults
  (`process.platform`, and `process.env`, whose case-insensitive `ComSpec` lookup on Windows is
  unchanged). The pure function is therefore the production code path, not a copy of it. On
  Windows the wiring is also proven end to end, because a bare `spawn('pnpm')` would fail with
  ENOENT. vitest's `suppress-warnings.cjs` does not filter DEP0190, so the warning check still works.
- **Two projects and groupOrder** (vitest 4.1.9, read in `cli-api.24X8XwN1.js`):
  - `groupSpecs` puts `fixture-build` in group 1, and `executeTests` runs the groups one after the
    other with `Promise.allSettled`. A failure in the `tools` group therefore does not skip the
    fixture build. Both projects inherit the same `maxWorkers`, but their `groupOrder`s differ, so
    the "different maxWorkers but same groupOrder" error cannot fire.
  - `vitest list --filesOnly` lists 37 files: 36 in `[tools]` and 1 in `[fixture-build]`. No file
    appears twice, and the count equals the tracked spec files under both include roots.
  - Filters still work: a name filter (`common.spec`), `--project fixture-build`, and an exact
    path each select the expected file.
  - No coverage config exists, and the script and both CI steps are unchanged. The default and
    JSON reporters both worked with the project labels.
  - Watch mode behaves as before. A change to a module that the fixture spec imports reruns the
    cold build, but that was already true.
- **Timeouts.** The real spawns' 120 s is about 8x the worst measured pnpm cost (15.2 s; 14.7 s in
  my run). The two cheap spawns share that budget, but a hang still fails, only later. The fixture
  hook keeps 1200 s, about 2.8x my 430 s for the file run alone. `build.spec.ts` gets 60 s for
  cases that took at most 2.2 s here. None of these hides a real hang. The other real-process
  siblings all have explicit budgets (checked: `extract-examples`, `no-decimal-operators`,
  `eslint/index`, `allowed-breakpoints`, and the fixture rebuild cases at 300 s). Every test over
  2 s in my run sits in one of those budgeted describes.
- **CI cost.** Run 43636806694 spent 73 s (Windows) and 33 s (Linux) on the whole lint:test step,
  `prelint` included. Serializing adds at most the `tools` group's time, about 36 s on Windows and
  less on Linux. That is small against the jobs' 45-minute limits. No CI run exists yet for
  6a5ebb36, so the split is verified locally on Windows only.
- **AGENTS.md.** `FIXTURE_BUILD_SPEC`, `TmProcessLaunch` and its members, and `tmProcessLaunch`
  carry TSDoc. The config, the specs and `process.ts` carry inline comments on the non-trivial
  parts. Both READMEs describe the grouping. The config and the pure tests are OS-neutral (POSIX
  path in the exclude, explicit platform arguments). The test names cite no spec, PC id or
  decision. The changed files pass ESLint and both relevant `tsc` projects.

My full run of `pnpm run lint:test` (Windows, with other slots building): 234/234 tests in 37
files, exit 0. Wall time was 810 s, of which `prelint:test` took about 285 s and vitest 524.5 s.
The `tools` group finished at 88.4 s. The fixture-build file took 430.3 s alone (the cold ng
build's bundle generation took 354.6 s). The shim test took 14.7 s and `node -e` took 1.6 s. Log
and JSON report: `.tmp/pcf29-r1-linttest.log` and `.tmp/pcf29-r1-report.json`.

### Findings

R1-1 (minor): `client/tools/tests/core-lint-scope.spec.mts:28`. This sibling has the same
load-sensitive pattern, and it keeps only 2x headroom after the change. The implementer notes
record that it once hit its 120 s limit while the fixture build ran beside it. They then leave
it alone as "already sized for load". In my run the fixture build no longer overlapped it, yet
"refuses text arithmetic on a TmDecimal in the primary entry point" still took 60.2 s. That is
half the budget, spent on the first typed lint that loads the workspace config and a TypeScript
program. The failure scenario is a busier slot machine, or a `dotnet build` running beside
lint:test, which is the load that commit 1 measured against. That load can push the first lint
past 120 s and give the next D-078-style local flake. The serialization does not remove this
contention, because the typed-lint scope specs still run in parallel with one another inside the
`tools` group. Suggested fix: either give the describe a budget sized like the fixture rebuild
cases (300 s), with the measured cost in its comment, or record the measured cost (up to 60 s
locally) in the implementer notes to justify keeping 120 s. The other scope specs peaked at 33 s
(`no-platform-sniffing`) and 13.7 s (`csp-only-in-csp-specs`), so this file is the only one near
its limit.

No blocking or major findings.

### Responses

- R1-1: fixed in the review 1 fixes commit. `core-lint-scope.spec.mts`'s describe now has 300 s,
  the same budget as the fixture rebuild cases. Its comment gives the measured cost. Its first
  test took 36.6, 39.9 (with `dotnet build Tellma.slnx` running beside it), 66.8 and 54.1 s in my
  four runs, and 60.2 s in the reviewer's. So it peaked at 67 s against the old 120 s, and it hit
  120 s once in the logged history. The other typed-lint scope specs keep their budgets.
  `no-platform-sniffing.spec.mts` peaked at 37.3 s against 120 s (3.2x), and
  `csp-only-in-csp-specs.spec.mts` at 13.7 s against 60 s (4.4x). Neither has failed in the
  logged history.
