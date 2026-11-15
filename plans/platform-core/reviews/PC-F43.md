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
