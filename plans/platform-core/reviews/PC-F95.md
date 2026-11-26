# PC-F95: Windows CI build stall: evidence and fail fast

## Implementer notes

### What was built

Fix item from PC-F82's integrate run 45524650837 (D-288): build-test on windows-latest sat in
`dotnet build Tellma.slnx -c Release --no-restore` until the job's 30-minute limit cancelled it,
with no log to root-cause from. The item makes a stall fail fast, inside the job, and leave
evidence.

- `eng/dotnet-build/build-with-evidence.sh` (new, with `README.md`): runs
  `dotnet build <args>` under GNU `timeout` and keeps every diagnostic that survives a kill.
  - `--timeout <N>{s|m|h}`, `--evidence <absent or empty dir>`, `--`, then the build arguments.
    Exit code: the build's own; 124 when the limit stopped it, 137 when it had to be killed; 2 for
    bad arguments.
  - Evidence: `build.binlog` (`-bl`), `build.log` (file logger, normal verbosity, `ShowTimestamp`,
    flushed line by line), `msbuild/` (`MSBUILDDEBUGPATH` for node failure reports,
    `MSBUILDDEBUGCOMM=1` for node-connection traces) and `roslyn/` (`RoslynCommandLineLogFile`, a
    log per compiler-client process, and the server's when the build starts it).
  - Stop: SIGINT on Linux (MSBuild's Ctrl+C: graceful cancel, MSB4220 per task still running,
    complete binlog), SIGTERM under Git Bash on Windows (SIGINT does nothing to a native program
    there), then SIGKILL 30 s later.
  - After the build: any node failure report is printed (and, from reused nodes, collected from
    `MSBuildTemp*` under the temp folders). On 124/137 it prints an `::error` annotation (stopped,
    killed after the grace, or killed early by something else such as the OOM killer; binlog
    complete or truncated, read from whether the text log has its closing `Time Elapsed`), the
    projects requested and not finished (from the text log), the compilations requested and never
    answered (from the compiler logs), and the text log's last 40 lines. So `gh run view
    --log-failed` alone says where a stall sat.
- `.github/workflows/ci.yml` build-test (both OSes) and `.github/workflows/nightly-tier.yml`
  `dotnet` and `dotnet-windows`: the solution build runs through the script with
  `--timeout 12m` under a step `timeout-minutes: 14` backstop (job limits 30 and 120), followed by
  an `upload-artifact` step, `build-evidence-<runner.os>`, when the build step's outcome is
  `failure` or `cancelled`.
- `CONTRIBUTING.md` (PR tier), `ARCHITECTURE.md` (repo layout, CI/CD bullet): the build's CI form
  and the evidence; CONTRIBUTING also explains the local MSB4166 below.
- `distributions/acme/test/Tellma.Distro.Acme.E2E/host/acme-e2e-host.mjs` (+ README step 3): its
  `dotnet run`s build with `MSBUILDDISABLENODEREUSE=1` (see "MSB4166 locally").

### Why a script, and why the text log

Measured locally before choosing:

- A killed build leaves an empty binary log. `-bl` is buffered and compressed, and
  `timeout --signal=KILL` on a stalled build left `build.binlog` at 0 bytes on both OSes, while the
  file logger had every line up to the stall. A step killed by the runner at its
  own `timeout-minutes` is the same case, so `-bl` plus an upload alone would most likely have
  uploaded nothing useful from the very stall this item is about.
- Linux: SIGINT to a build stalled in a tool (`sleep 600` under Exec) cancelled it, logged the
  failure and wrote a complete binary log (exit 124 after the limit). A build stalled in a task
  that ignores cancellation logged MSB4220 "Waiting for the currently executing task "Hang" to
  cancel" and was killed after the grace (137, empty binlog, text log intact).
- Windows Git Bash: SIGINT did nothing (killed after the grace). SIGTERM ended the stalled tool
  (`ping` exited 143), the build failed on its own with a complete binlog (exit 124, 21 s). A
  stalled in-proc task: SIGTERM ended the build at once (124, empty binlog, text log intact).
- Both OSes were exercised with the script itself (Windows natively, Linux in
  `mcr.microsoft.com/dotnet/sdk:10.0`, whose `awk` is mawk) on a stall project in each mode, plus a
  full-solution text log through the unfinished-projects summary (0 unfinished on the complete
  log, 46 on its first 2 MB) and a synthetic compiler log with an unanswered request.

### The build switch: none added

Neither `-nodeReuse:false` nor `UseSharedCompilation=false` is added to CI.

- There is no evidence for either: the one stall left no log (D-288). Adding a switch now would
  change the conditions the next stall would be diagnosed under, and if the stall then never came
  back nobody could say why. The evidence above distinguishes the two cases directly: a compile
  waiting on the compiler server shows as an unanswered request in `roslyn/`; a node-connection or
  node-death stall shows in the comm trace, the failure reports and the unfinished projects.
- Measured cost (full Release rebuild, `--no-incremental`, this slot, other slots building at the
  same time, so the spread is wide):

  | Run | Round 1 (busy machine) | Round 2 (quieter, interleaved, x2) |
  | --- | --- | --- |
  | Baseline `dotnet build` | 246 s, 120 s | 114 s, 106 s |
  | Through the script (evidence on) | 164 s | 120 s, 115 s (about +5%) |
  | Script + `-nodeReuse:false` | 228 s | 156 s, 192 s (+40% to +80%) |
  | `-p:UseSharedCompilation=false` | 589 s (about 2.5x to 5x) | not repeated |

  The script's own cost is the file logger, the binlog and the compiler logs: about 5%, and about
  29 MB of evidence (binlog 9.1 MB, text log 16 MB, compiler logs 3.4 MB) before the artifact's
  zip. The `-nodeReuse:false` cost here is exaggerated against CI's: locally the baseline reuses
  warm nodes that other builds left, whereas on a fresh runner only the restore's nodes would be
  reused. `UseSharedCompilation=false` starts a cold compiler per project and is out of the
  question as a default.
- Both switches stay ready for the follow-up: if the evidence from a recurrence points at the
  compiler server, the switch is `-p:UseSharedCompilation=false` (costly, as above); if at node
  reuse, `-nodeReuse:false` on the build step's arguments.

### MSB4166 locally

Root-caused, and it is not what stalled CI.

- Local evidence: no `MSBuild_*.failure.txt` exists in any of the 654 `MSBuildTemp*` folders under
  the user temp folder, and the Application event log has no dotnet.exe crash on 2026-11-25 or
  -26 (the earlier stack-overflow and fail-fast crashes, 11-16 to 11-22, are test processes). So
  the nodes behind those MSB4166s did not fail: they were ended from outside.
- Mechanism: with node reuse (the default) a build's worker nodes stay alive for any later build
  of the same user and SDK, here every slot, and stay children of the process that started them.
  The comm trace of one Release build here considered 45 candidate `dotnet` processes and connected
  to 19 nodes it did not start. A tool that ends a process tree (Playwright's webServer stop is
  `taskkill /pid <p> /T /F` on Windows; the Acme e2e host's `stopTree` is the same; an agent
  stopping a background build or test) ends nodes another slot's build is using.
- Reproduced on SDK 9 (pinned by a scratch global.json so no slot's SDK 10 nodes could take part):
  build A is `dotnet run` of an app with twelve project references, which stays alive as the
  app's parent; build B is twelve parallel 40-second Execs through the script. B connected to 11
  existing nodes, 3 of them A's children; `taskkill /T /F` of A's tree 15 s in failed B with
  MSB4166 and no failure report. With `-nodeReuse:false` on B, the same kill (nine
  of A's nodes ended) left B green. With `MSBUILDDISABLENODEREUSE=1` on A, A's tree held only
  the app and B, reusing, stayed green.
- So `-nodeReuse:false` (or `MSBUILDDISABLENODEREUSE=1`) addresses these MSB4166s, on either side.
  CI is unaffected: each job runs its builds one after another on its own VM, so a reused node is
  only the restore's, and nothing ends trees under a running build. This does not explain the CI
  stall.
- Changed here: the Acme e2e host runs its two `dotnet run`s with `MSBUILDDISABLENODEREUSE=1`
  (`distributions/acme/test/Tellma.Distro.Acme.E2E/host/acme-e2e-host.mjs` and its README), so a
  local e2e run no longer leaves reusable nodes inside the tree its stop ends. `CONTRIBUTING.md`
  explains the failure and the switch.
- Not changeable in the repo: an agent tool ending a background build's or test's tree. The
  orchestrator could set `MSBUILDDISABLENODEREUSE=1` in the slots' environment, at the measured
  +40% to +80% measured on a full local rebuild (incremental builds not measured).

### Deviations

- Scope: the nightly tier's two solution builds (`dotnet`, `dotnet-windows`) get the same
  treatment as build-test. The item names build-test, but they run the identical command under a
  120-minute job limit, where a stall would cost two hours with no log.
- The item asked for "a binary log (-bl) uploaded as an artifact". The binlog is uploaded, but it
  is not what a stall leaves (it is empty after a kill, measured above); the text log, the
  compiler logs and the node diagnostics are what survive, so the script collects all of them and
  prints the summary in the step log.
- The script's own limit (12 min) stops the build, not the step's `timeout-minutes` (14): the
  runner's kill would leave no summary.
- Beyond the item: the Acme e2e host's `MSBUILDDISABLENODEREUSE=1`, the root fix for one source of
  the local MSB4166s the orchestrator asked about (reproduced above). It changes no CI behaviour
  that matters: each e2e run's two `dotnet run` builds start their own nodes (not measured; on CI
  the web host is built by the publish step before them).

### Open risks

- If the runner VM itself hangs (rather than the build), no step-level mechanism helps: neither the
  summary nor the upload runs. The job limit is the only bound then, as before.
- A stall in `dotnet restore` (before the build) is still bounded only by the job limit. The stall
  seen was in the build, so the restore is unchanged.
- On Windows, SIGTERM through Git Bash terminates processes outright; the binlog is complete only
  when the build survives losing the tool it was stalled on. The text log is the evidence that
  always survives.
- Reused worker nodes (on CI, the restore's) keep the settings they started with: no comm trace
  from them, and their failure reports go to MSBuild's temp folders, which the script searches.
  The compiler client logs from them (verified: 20 per-process compiler logs from a build whose
  19 worker nodes were reused).
- The `if:` on the upload steps and the step-limit behaviour cannot run before integration (D-144).
  The workflows were parsed with PyYAML and checked by a script for step ids, `steps.build`
  references, step limits against job limits and the upload following each build step; no
  actionlint is installed here.

### Where reviewers should look hardest

- The awk in `unfinished_projects` and `unanswered_compilations`: mawk-compatible (POSIX `index`,
  `substr`, `match`/`RSTART` only), CRLF-tolerant, and keyed so a project built several times at
  once is counted, not overwritten.
- The signal choice per OS and the 124/137/early-137 classification.
- The Windows path handling: the evidence path is made absolute and converted with
  `cygpath -m` before it goes into environment variables that other processes (nodes, the
  compiler server) read from their own working folders.
- Whether the upload condition `always() && (steps.build.outcome == 'failure' ||
  steps.build.outcome == 'cancelled')` uploads after a step `timeout-minutes` kill and after a
  manual run cancel.

### Gates

- No C# changed. `dotnet build Tellma.slnx` (Debug): "Build succeeded", 0 warnings, 0 errors.
  Twelve Release rebuilds of the solution (the timings above) all succeeded with 0 warnings. Six
  went through the script: five exited 0; the sixth (round 1) built fine but its exit was spoiled
  by an edit to the script while it ran.
- `dotnet format Tellma.slnx --verify-no-changes`: clean (only the workspace-loading warnings line).
- Acme E2E package: `run lint` (prettier), `run typecheck`, `run test`: all pass. The e2e suite
  itself was not run locally; the env change is covered by the SDK 9 reproduction above.
- Workflows: PyYAML parse plus a structural check (step ids, `steps.build` references, step
  limits under job limits, an upload after each build step, no solution build outside the
  script). `bash -n` on the script. No actionlint or shellcheck installed.
- Script behaviour: Windows (Git Bash) and Linux (dotnet/sdk:10.0, mawk) stall runs in both modes,
  argument errors (exit 2), a non-empty evidence folder, a Windows backslash evidence path, a failed
  build's exit code (1), and a planted node report under the temp folder being collected.
- No unit or integration tests touched; no nightly-tier tests. CI exercises the workflow change
  only at integration (D-144).
