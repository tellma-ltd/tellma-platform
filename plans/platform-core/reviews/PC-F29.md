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
