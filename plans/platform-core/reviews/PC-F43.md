# PC-F43: CI-mode e2e web server builds the showcase inside its startup timeout

## Implementer notes

### What was built

- **The cause.** In CI mode (`CI` set) Playwright's webServer ran `serve.mjs showcase --prod`. When `dist/showcase/browser` was older than any source, that command ran a full `ng build showcase` before it listened, all inside the webServer's 300 s timeout. Two facts make that build slow locally:
  - Angular disables its persistent build cache whenever `CI` is `1` or `true` (`@angular/build` `normalize-cache.js`, default `cli.cache.environment: 'local'`), and Playwright passes the caller's `CI` to the webServer. So the in-server build was always fully cold.
  - On this loaded host a cold or cached build measured 151 s, 192 s, 486 s and 921 s in four runs (below). 300 s is inside that spread. A timeout sized to a cold build would need well over fifteen minutes, and a hung server would then also go unnoticed that long.
- **The fix: build before the server, not inside it.**
  - `client/scripts/prod-build.mjs` (new): the freshness rule that was private to `serve.mjs` (`BUILD_INPUTS`, the mtime walk that skips `node_modules`/`dist`/`.angular`/`*.tsbuildinfo`), moved unchanged and exported as `isProdBuildFresh(project, root?)`. Also `prodBuildDir(project, root?)` and `ensureProdBuild(project, { logPrefix })`, which reuses a fresh bundle or runs `ng build`, then always runs the idempotent font-preload injection, exactly as `build:showcase` does. It returns the failing step's exit code instead of exiting, so each caller decides.
  - `client/scripts/e2e.mjs`: in CI mode it calls `ensureProdBuild('showcase')` before it picks the port and before Playwright starts. A build failure stops the run with that exit code. `--list`, `--help` and `-h` skip it, because Playwright starts no web server for them (checked in `playwright@1.61.1` `runAllTestsWithConfig`: list mode runs no global-setup or plugin tasks). Choosing the port after the build also keeps a multi-minute build out of the window between picking a free port and binding it.
  - `client/scripts/serve.mjs`: `--prod` uses `ensureProdBuild` (same behaviour as before when run on its own). The new `--prod --no-build` serves the existing bundle as it is and exits 1 at once with a clear message when there is none. `--no-build` without `--prod` is refused. The `ng` path is resolved only for the dev server.
  - `client/e2e/playwright.config.ts`: the CI webServer command adds `--no-build`, and its timeout drops from 300 s to 60 s. That timeout now times a static server's start alone, measured at 0.45-0.54 s on this loaded host, so a hung server fails within a minute. The local (non-CI) `ng serve` path and its 180 s are unchanged.
- **CI jobs.** Only the `client` job (ci.yml) and nightly-tier's `client-e2e` and `client-e2e-quarantine` run client e2e. Each already runs `build:showcase` first, so `e2e.mjs` finds the bundle fresh and prints `[e2e] reusing the existing showcase build`. It adds a freshness walk of about 1 s and an idempotent preload injection, and no second build. `design-e2e`, `identity-e2e` and `client-windows` start no Playwright webServer and are unaffected. I kept the explicit `build:showcase` steps, because they are gates in their own right (`client-windows` has no e2e) and report a build failure as its own step. Only their comments changed.
  - Before this change the webServer's output was hidden in CI (Playwright drops webServer stdout by default), so the reuse could not be seen in a log. It now prints in the e2e step's own output. Evidence that reuse already worked in CI: in run 43728242154 the e2e step began at 15:15:12.98 and the first test passed at 15:15:18.7, while a cold build there takes about 19 s.
- **docs:build writes only on change.** `client/tools/docs/build-docs.mts` rewrote its three generated files under `projects/core/tellma-core-ui-mcp/generated` on every run, and `lint:test` runs it as a prebuild. That moved mtimes under `projects/`, so any `lint:test` or `docs:build` after `build:showcase` made the bundle look stale and the next CI-mode e2e rebuild it for nothing. (I saw this here: the bundle went stale after my first `lint:test`.) It now writes only on a real change, as `tokens:build-css` and the boot template already do. In the ci.yml `client` job, `docs:build` and `lint:test` run before `build:showcase`, so CI was not affected.
- **Tests.**
  - `client/tools/tests/prod-build.spec.mts` (8 tests, in `lint:test`) pins the freshness rule on a temp workspace: missing bundle, fresh bundle, changed source, deleted source (the folder mtime), changed lockfile one level up, changed config, build output inside the source tree ignored, and an absent input skipped. Mutation-checked: dropping folder mtimes, the excluded folders, the lockfile input or the `.tsbuildinfo` exclusion each fails exactly one test.
  - `client/tools/tests/serve-no-build.spec.mts` (2 tests) runs the real `serve.mjs`. `--prod --no-build` on a never-built project exits 1 with the "no production build" message and never prints "building". `--no-build` without `--prod` is refused. Each run takes about 0.5 s and has a 60 s limit, so machine load cannot time it out.
- **Docs.** CONTRIBUTING.md gains a paragraph after the PR-tier e2e command: in CI mode the script builds the bundle unless it is already current. ARCHITECTURE.md does not describe the e2e web server, so nothing in it disagrees, and it is unchanged.

### Measurements (this Windows host, 20 logical CPUs, other slots building and testing)

- Cold `ng build showcase` with `CI=1` (cache off): 151 s wall (139 s bundle generation). Log `.tmp/f43-measure-cold-ci1.log`.
- With the persistent cache (CI unset), two runs in a row: 192 s and 486 s wall. Host load, not the cache, dominates. So `e2e.mjs` does not strip `CI` from the build to win the cache: the cache bought nothing measurable, and the build stays identical to the CI one. Logs `.tmp/f43-measure-cached-{1,2}.log`.
- Linux and Windows CI (cold, cache off): `build:showcase` took 17-22 s on ubuntu-latest and 19-32 s on windows-latest across four runs (43728242154, 43716020236, 43704425075, 43693705719).
- Freshness walk: 0.83-1.37 s here. `serve.mjs --prod --no-build` answering HTTP 200: 453, 538 and 527 ms. Missing bundle: exit 1 after 0.49 s. Log `.tmp/f43-server-start-timing.log`.

### Tests run

All on the final tree. Logs are under `.tmp/f43-*`.

- **.NET** (nothing .NET changed): `dotnet build Tellma.slnx`: Build succeeded, 0 warnings. `dotnet format --verify-no-changes`: clean, empty output. PR-tier unit filter: 20 assemblies, 9,494 passed, 0 failed. No integration suite is touched.
- **Client:** `lint` clean, `typecheck` clean, `lint:test` 244 of 244 (39 files, the two new specs included). The other package gates (`build`, `test`, `api:check`, `size:check`, `tokens:check`) cover `projects/`, which this change does not touch. `docs:build` ran as `lint:test`'s prebuild. Its generated files kept their 09:14:55 mtimes across that run, so the write-on-change works.
- **The two new specs, repeated:** 5 runs of both files together while the e2e suite below was running: 10 of 10 each time.
- **Entry-point behaviour:** with `CI=1`, `e2e.mjs --list` and `--help` print no `[e2e]` line and do not build.
- **e2e:** I ran the full PR tier once in CI mode, from a deleted `client/dist/showcase`: `CI=1 node client/scripts/e2e.mjs --project chromium --project touch --grep-invert "@nightly|@perf|@quarantine" --workers 2`. `e2e.mjs` built the showcase first, cold, in **921 s** on this loaded host, three times the old 300 s webServer limit. Playwright then started the `--no-build` server at once. 1090 of 1090 passed in 29.7 min, with no retries and no flakes (`.tmp/f43-e2e.log`). Under the old config this run would have failed at the webServer before a single test.
- **Reuse:** right after that run I ran `pnpm run docs:build`, then `CI=1 node client/scripts/e2e.mjs --project chromium --grep "alert story is axe-clean"`. It printed `[e2e] reusing the existing showcase build`, and 3 of 3 passed in 39 s wall. So a bundle already built is served without a rebuild, and `docs:build` no longer makes it look stale (`.tmp/f43-e2e-reuse.log`).

### Deviations

- None from the item's statement. It allowed either fix, and this is its first option, taken in the entry point rather than as a separate step. The webServer timeout also shrinks (300 s → 60 s), because it no longer times a build.
- Beyond the item: `docs:build` writes only on change (see above), so that a gate run after `build:showcase` does not force a needless rebuild.

### Open risks

- A source edited during a CI-mode run, after `e2e.mjs` built, is not picked up: `--no-build` serves the bundle `e2e.mjs` made. That is the same snapshot semantics as running `build:showcase` by hand first.
- The freshness rule is mtime-based, as before. A tool that writes into `projects/` outside the excluded folders after `build:showcase` makes `e2e.mjs` rebuild. That costs time, never correctness. I checked that lint and typecheck leave the bundle fresh. `tokens:build-css`, the boot template and now `docs:build` write only on change.
- The non-CI `ng serve` path keeps its 180 s timeout with an in-server dev compile. It is out of this item's scope and has not been reported failing.

### What reviewers should examine hardest

- That no CI job now builds twice: the freshness check must judge a CI `build:showcase` bundle fresh (the same rule as before, now in `prod-build.mjs`), and the preload injection is idempotent.
- `--no-build`'s contract: it must never build, and it must fail fast (not time out) when the bundle is missing.
- The `--list`/`--help`/`-h` skip list in `e2e.mjs`, against Playwright 1.61.1's runner.

## Review 1

Scope: commit a7ab2bed only. Checked conformance (the item allows either fix; building in `e2e.mjs`
and cutting the `--no-build` webServer timeout to 60 s are both justified). Checked every CI job that
runs client e2e: ci.yml `client`, and nightly `client-e2e` and `client-e2e-quarantine`. Each runs
`build:showcase` as the last step before e2e and nothing writes under `projects/` in between, so no
job builds twice. A missing bundle makes `--no-build` exit 1 on stderr, which Playwright shows, so
the 60 s timeout hides nothing. `SHOWCASE_PORT` is required, so Playwright cannot be run around
`e2e.mjs`.

Reviewer runs (logs `.tmp/r1f43-*`):
- The two new specs: 10/10 (`r1f43-specs.log`).
- Probe (`r1f43-probe.log`): the bundle is fresh; touching `projects/core/tellma-core-ui/public-api.ts`
  makes it stale; restoring the file's mtime makes it fresh again. A run of the idempotent
  `inject-font-preloads.mjs` that adds nothing still moved `index.html`'s mtime from 18:39:42 to
  18:47:31.
- CI-mode reuse run: `CI=1 node scripts/e2e.mjs --project chromium --grep "alert story is axe-clean"
  --workers 2` printed `[e2e] reusing the existing showcase build` and passed 3/3
  (`r1f43-e2e-reuse.log`).
- Fixture probe (`r1f43-probe-fixtures.log`): one lint:test spec that copies the tooling fixture app
  (`emit.spec.ts`) turned the bundle from fresh to stale (see R1-2). I then restored the folder's
  mtime.

### Findings

**R1-1 (minor). A source saved during a long build is silently left out of a bundle that later runs
count as fresh.** `client/scripts/prod-build.mjs:101-108` with `:149-171`, and
`client/projects/core/tellma-core-tools/src/fonts/index.ts:145`.
- The cause: the freshness marker is `index.html`'s mtime. `ng build` writes that file only at the
  end of the build, and the font-preload injection then rewrites it on every call, even when it adds
  no link (shown in the probe).
- Scenario: a developer starts a CI-mode run (`CI=1 node scripts/e2e.mjs …`). Its cold build takes
  151-921 s on this host. During it they save a fix to a file that `ng build` has already read. The
  marker is written after the save, so this run and every later run judge the bundle fresh. They test
  the old code and can report green on a fix that was never compiled. That is exactly the stale reuse
  that the rule's own doc comment says it never allows.
- This was already latent in `serve.mjs`. But the old 300 s limit killed long builds before they
  wrote a marker. Now such builds run to completion, so the window is the whole build.
- Fix:
  - In the build branch of `ensureProdBuild`, after the injection, set `index.html`'s mtime back to
    `started` (the value already taken at `:155`), so the marker stands for the moment the build
    began reading sources.
  - Make `tmInjectFontPreloads` skip the write when `links` is empty, so a reuse no longer moves the
    marker.
  - The CI `build:showcase` path keeps working unchanged: no edits happen there.
  - Add a `prod-build.spec` case: a source mtime between the build's start and `index.html`'s write
    must read as stale.

**R1-2 (minor). lint:test after build:showcase still makes the bundle look stale, though the commit
message and notes say it no longer does.** `client/scripts/prod-build.mjs:42`.
- The cause: `copyFixtureApp` (`projects/core/tellma-core-tools/fixtures/fake-emit.ts:155`, used by
  the build, serve, emit, app-project and build.fixture specs) creates and then removes
  `fixtures/run-*` copies. That moves the `fixtures/` folder's mtime, and the walk counts it.
- The evidence: the probe turned a fresh bundle stale with one such spec. The folder's mtime on this
  slot (09:50:16.90) is also exactly when the implementer's last lint:test ended. The notes check
  only lint, typecheck and docs:build afterwards, not lint:test.
- The cost: no correctness loss, but a needless full rebuild (151-921 s here) on the next local
  CI-mode e2e after any lint:test. CI is unaffected, since lint:test runs before build:showcase there.
- Fix: add `fixtures` to `NON_SOURCE_DIRS`. Both folders of that name under `projects/` are test-only
  (`tellma-core-tools/fixtures` and `tellma-core-tools/src/eslint/fixtures`), and no showcase input
  lives in either. Add a "build output inside the source tree" case for a `fixtures/run-x` entry. If
  you keep the current rule instead, correct the claim in the commit message and in "What was built".

**R1-3 (minor). The e2e production build does not regenerate the tokens CSS, so a token edit is
compiled against the old stylesheet.** `client/scripts/e2e.mjs:62-68` and
`client/scripts/prod-build.mjs:149-158`.
- The cause: `tellma-default.css` (`projects/core/tellma-core-ui-tokens/css/`) is generated and
  gitignored. Only the `pree2e` hook, `pnpm run build` and an explicit `tokens:build-css` regenerate
  it. CONTRIBUTING.md and the PR-tier command call `node scripts/e2e.mjs` directly, which skips the
  hook.
- Scenario: a developer edits a token in `projects/core/tellma-core-ui-tokens/src`, then runs
  `CI=1 node scripts/e2e.mjs …`. The walk sees the edit and rebuilds, but the build uses the old
  generated CSS. The run tests stale styles, and the new bundle then reads as fresh until something
  else changes. On a clean checkout the same path fails instead, with an unresolved stylesheet.
- This was already true of `serve.mjs --prod`. But `e2e.mjs` now owns "make the bundle current", and
  CI never meets this because it generates the CSS in an earlier step.
- Fix: run `tools/tokens/build-css.mts` at the start of `ensureProdBuild`, before the freshness check.
  It writes only on a change, so a CI bundle stays fresh. Alternatively, state in CONTRIBUTING.md
  that a CI-mode run needs `tokens:build-css` first.

### Responses

- R1-1: fixed in "PC-F43: review 1 fixes" (this commit, child of a7ab2bed), as suggested.
  - The new `markBuildStart(project, startedMs, root?)` in `client/scripts/prod-build.mjs` sets `index.html`'s mtime back to the moment `ng build` was started. `ensureProdBuild` calls it last, after the preload injection, and only on the build branch. A source saved while the build ran is therefore newer than the marker, and the next run rebuilds.
  - `tmInjectFontPreloads` (`projects/core/tellma-core-tools/src/fonts/index.ts`) now skips the write when it adds no link. A reuse, or the injection run by `build:showcase` and then by `e2e.mjs`, no longer moves the marker.
  - Tests:
    - `prod-build.spec.mts` "rebuilds when a source was saved while the build that the marker dates ran": a source between the build's start and `index.html`'s write reads fresh until it is marked, and stale after.
    - Also "still reuses a build dated by its start when every source predates it".
    - `fonts/index.spec.ts` "leave index.html untouched, its modification time included, when every preload is already there".
    - Mutation-checked: removing the `links !== ''` guard fails that test.
  - Not changed: the npm `build:showcase` script still stamps the bundle when `ng build` ends. It is the CI gate and always builds, and CI checkouts see no edits while it runs. Locally, an edit made during a manual `build:showcase` can still be missed by a later reuse. Routing that script through `ensureProdBuild` would let a gate skip compiling, so I left it.
- R1-2: fixed in the same commit. `fixtures` joins `NON_SOURCE_DIRS`, and the comment there says why: the folders of that name hold test-only data, and the tooling specs create and remove `fixtures/run-*` copies. The two such folders under `projects/` are test-only, and no application input may live in one. Test: "ignores a fixture copy that a test creates and removes inside the source tree". Mutation-checked: dropping `fixtures` fails it. The claim in commit 1's message ("a lint:test … run after build:showcase no longer makes the bundle look stale") was premature. It now holds, shown by the reuse run after `lint:test` below.
- R1-3: fixed in the same commit, as suggested. `ensureProdBuild` first runs `tools/tokens/build-css.mts` (through `tsx/cli`, resolved from client/), before the freshness check. It writes only on a change, so on CI, where the job already generated the CSS, the bundle stays fresh. A token edit now reaches the build, and a fresh checkout gets its stylesheet. `serve.mjs --prod` gets the same step, since it shares `ensureProdBuild`. A failing step stops the run with its exit code.
- Verification, all on the final tree (logs `.tmp/f43-r1-*`):
  - The touched specs (`prod-build.spec.mts`, `serve-no-build.spec.mts` and core-tools `fonts/index.spec.ts`): 17 of 17. eslint on the touched files is clean, and so are `tsc -p tools/tsconfig.json --noEmit` and the core-tools spec typecheck.
  - Stale run: I touched `projects/internal/showcase/src/main.ts`, then ran `CI=1 node client/scripts/e2e.mjs --project chromium --grep "alert story is axe-clean" --workers 2`.
    - The tokens CSS was not rewritten.
    - The run built for 302 s and passed 3 of 3.
    - `index.html`'s mtime equals the moment the build started (10:59:22.21), and the bundle read fresh afterwards, so the build writes nothing into its own inputs.
  - `lint:test`: 248 of 248 (39 files). During it the `fixtures/` folder's mtime moved to 11:10:09, past the marker, and the bundle still read fresh afterwards.
  - Reuse run after that, with the same command:
    - It printed `[e2e] reusing the existing showcase build` and passed 3 of 3.
    - The injection added its preloads idempotently and left `index.html`'s mtime at 10:59:22.21.
