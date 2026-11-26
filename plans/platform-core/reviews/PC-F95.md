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
  | Through the script (evidence on) | 164 s | 120 s, 115 s (about +5-7%, within noise) |
  | Script + `-nodeReuse:false` | 228 s | 156 s, 192 s (+40% to +80%) |
  | `-p:UseSharedCompilation=false` | 589 s (about 2.5x to 5x) | not repeated |

  The script's own cost is the file logger, the binlog and the compiler logs: about 5-7%, within
  the noise here (the integration CI run records the real cost), and about
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

## Review 1

Reviewed 619f9041 (the only commit). Run by the reviewer, in scratch projects outside the repo with
`-nodeReuse:false` so no other slot's nodes took part: the script on Windows (Git Bash) on a
two-TFM library (exit 0, empty unanswered and unfinished lists on the complete logs, the compiler
log formats match the parser), on a project with a compile error (exit 1, both CS0029 errors in the
step output, no summary), on a single-project `ping -n 600` stall with `--timeout 15s` (exit 124 in
18 s) and on a three-project `-m:3` stall (exit 124 in 27 s); on Linux
(`mcr.microsoft.com/dotnet/sdk:10.0`, mawk) on the same three-project stall (exit 124 in 20 s,
binlog complete) and on a passing build under a TTY (`docker run -t`, no SIGTTOU hang from
timeout's background process group). The parsers were also run on a log cut at line 60 (two
unfinished keys found, one unanswered request found). The CI-side behaviour (`if:`, step limit)
cannot run before integration (D-144). No blocking or major finding: a stall now fails at about
12.5 min with its text log, compiler logs and node traces kept and uploaded on failure or
cancellation, a real build failure still fails the step with its compiler errors in the step log,
and no gate is loosened.

### Findings

**R1-1 (minor). On Linux the stop signal reaches every process of the build, not only MSBuild, so
the README describes the wrong path and the summary's two lists stay empty for the stalls they are
meant to show.** `eng/dotnet-build/build-with-evidence.sh:77-86`, `eng/dotnet-build/README.md:48-50`.
GNU `timeout` without `--foreground` sends the signal to the command's whole process group. In the
reviewer's Linux run the stalled tool died of the SIGINT itself (`error MSB3073: The command "sleep
600" exited with code 130`), with no MSB4220, and the build then failed on its own; the worker
nodes the build started, and a compiler server it started, get the same SIGINT. So on both OSes a
stall in a tool, and on Linux a stall in a compiler server this build started (the server dies,
the client logs a fallback or failure line, so the request counts as answered), ends with
"Projects started and not finished: (none)" and "Compilations requested and not answered: (none)";
all three reviewer stalls printed both as "(none)", and only the tail's MSB3073 named the project.
The evidence is still in the artifact, but the step-log summary, which the item and
ARCHITECTURE.md:1232 present as the place that says where a stall sat, says nothing.
Fix: correct the README's Linux paragraph (the signal reaches the tools, nodes and compiler server
directly), and have the summary also print what the stop itself ended: the MSB3073/MSB4220/MSB4166
errors and warnings logged after the limit, and every `CompilerServer:` answer whose outcome is
not `server` (a fallback or failure flags a dead or stuck server whichever signal path ended it).
Alternatively `--foreground` on Linux sends SIGINT to MSBuild alone so it cancels in order, at the
cost of orphaned nodes after a kill; that is a choice to state either way.

**R1-2 (minor). A parser miss reads the same as "nothing pending".**
`eng/dotnet-build/build-with-evidence.sh:159-231,251`. Both lists match English MSBuild and
compiler messages (`Done Building Project`, ` is building `, ` on node `, `Compilation request`)
and the binlog verdict greps `Time Elapsed`. On a machine whose .NET UI language is not English
(the README offers the script for local use) or after an SDK changes a message, every stall prints
"(none)" twice and calls a complete binlog "truncated", with nothing to say the parser matched no
line at all. Fix: export `DOTNET_CLI_UI_LANGUAGE=en` in the script (which MSBuild and the CLI
honour), and print how many project starts and compilation requests were parsed beside each list,
for example "(none of 412 started)", so a parser that matched nothing shows as 0.

**R1-3 (minor). A project still open in one of several overlapping instances is listed with
another instance's time and node, and without its count.** `eng/dotnet-build/build-with-evidence.sh:181,188,192-195`.
`open[key]` counts instances, but `started[key]` is overwritten by each start, and the END block
prints one line per key. On the reviewer's log of a two-TFM library cut mid-build, the
`(Build target(s))` key had two instances on nodes 1 and 2 and printed once, as node 2's. When the
instance that finished is the one started last, the line names the wrong node and time. No
solution project multi-targets today, but the notes claim the keying handles this. Fix: keep the
stamps per instance (append to a list on start, drop one on finish) or print `open[key]` and every
start's stamp and node.

**R1-4 (minor). The Acme e2e host is touched, but no Acme e2e spec ran.** PLAN.md §7.1 item 4 asks
for the Chromium e2e specs covering the touched code; the notes ("The e2e suite itself was not run
locally") and the commit's Tests line list only the package's lint, typecheck and unit tests. The
env addition is low risk and the integration run's acme-client job will exercise it, but nothing
before that proves the host still migrates and serves with it. Fix: run one Acme Chromium spec
locally (any spec that starts the host) and add it to the Tests line, or record its omission as a
deviation.

**R1-5 (minor). The "about +5%" cost is weaker than stated.** Review notes, "The build switch"
table; commit message. The quieter round gives 114 and 106 s against 120 and 115 s, a mean of
+6.8%, and round 1 has the script both faster and slower than its baselines (164 s against 246 and
120 s), so on a shared machine the figure is "about 5-7%, within noise", not a measured 5%. Fix:
say so in the notes and commit message, and record at integration the CI build step's duration on
both OSes against the 3-4 minutes the comments cite, which is the cost that matters.

**R1-6 (minor). CONTRIBUTING offers only the costly remedy for the cross-worktree MSB4166.**
`CONTRIBUTING.md:110-118`. The root cause is sound and reproduced (idle nodes left as children of a
process whose tree is later ended, reused by another worktree's build). `-nodeReuse:false` or
`MSBUILDDISABLENODEREUSE=1` protects only the builds that set it, at +40-80%; applied to gate
builds alone it leaves a slot's `dotnet test` (which builds, then holds its idle nodes as children
while the tests run, and is what an agent most often stops) both a victim and a source. MSBuild's
node handshake carries a salt from `MSBUILDNODEHANDSHAKESALT` (the reviewer's comm trace logs
"Handshake salt is"); a distinct salt per worktree keeps reuse within a worktree and makes
worktrees' nodes unusable to each other, which removes the cross-worktree case for every
`dotnet` command at about no cost. Fix: verify the salt against the SDK 9 reproduction and, if it
holds, document it as the remedy for several worktrees on one machine (and offer it to the
orchestrator for the slots' environments), keeping `-nodeReuse:false` as the per-build fallback.

### Responses

Rulings D-363; all six fixed in the fix1 commit.

- **R1-1, fixed.**
  - README: the Linux paragraph now says `timeout` signals the build's whole process group. The
    tools and worker nodes the build started, and a compiler server it started in that group, get
    the SIGINT directly, so a stalled tool usually dies of it first (MSB3073, exit 130). The
    script's comment says the same.
  - Two new step-log groups. The first, "Errors and warnings logged since <stop time>", lists the
    time-stamped MSB3073, MSB4220 and MSB4166 lines at or after the stop (from the build's start
    when a kill came before the limit). It reads the text log's local clock against `date`, and a
    line up to 12 h later counts, midnight included. The second, "Compilations the compiler server
    did not complete", lists every `CompilerServer:` answer whose outcome is not `server`.
  - Kept: `timeout` without `--foreground`.
  - Verified with the three-project `-m:3` stall (`--timeout 15s`).
    - Windows: exit 124 in 19 s. The new group shows
      `c.proj(6,24): error MSB3073: The command "ping -n 600 ..." exited with code 143`.
    - Linux (dotnet/sdk:10.0, mawk): exit 124 in 15 s. The group shows `/w/multi/c.proj(6,24):
      error MSB3073 ... exited with code 130`.
    - The stall summary now names the stalled project on both OSes.
  - The midnight wrap and the filter's boundary were checked on saved logs.
- **R1-2, fixed.**
  - The script exports `DOTNET_CLI_UI_LANGUAGE=en`. Without it, a build run with
    `DOTNET_CLI_UI_LANGUAGE=de` writes a German text log ("Projekt ... auf Knoten", "Verstrichene
    Zeit"). With `de` set in the caller's environment, a build through the script writes an English
    log with its `Time Elapsed`, on both OSes.
  - Each parser now returns its counts with its list. The groups open with "From N project starts
    and M finishes in the text log:" and "From N compilation requests and M answers in the
    compiler logs:", so a parser that matched nothing shows as 0.
  - On saved logs: 89/89 starts and finishes and 87/87 requests and answers on a full solution
    rebuild; 4/4 on the stall.
- **R1-3, fixed.**
  - Each key keeps every start's time and node, and a key whose instances have all finished
    starts over.
  - A key with several open instances prints once, sorted by its first start, with "(k of n
    instances not finished; requested at T node a, T node b)". Done lines carry neither node nor
    instance, so which instance finished cannot be told.
  - On the reviewer's two-TFM log cut mid-build (`r1f95/trunc.log`): `ok.csproj (Build target(s))
    (2 of 2 instances not finished; requested at 04:52:48.026 node 1, 04:52:48.026 node 2)` plus
    the `(default targets)` key. The parser read 7 starts and 4 finishes.
- **R1-4, fixed.** Ran the Acme Chromium specs that start the e2e host on this branch (localdb, cold
  client build, host built with `MSBUILDDISABLENODEREUSE=1`):
  `node .../scripts/e2e.mjs --project chromium --grep-invert "@nightly|@perf|@quarantine"
  specs/tenant-shell.spec.ts specs/launch.spec.ts` gave **16 passed (8.8 min)**, exit 0. The host
  migrated, served and stopped cleanly.
- **R1-5, fixed.** The overhead now reads "about 5-7%, within noise" in the notes' table and text,
  in the README (new paragraph) and in the fix1 commit message. The integration CI run will record
  the real cost: the build step's duration on both OSes against the 3-4 minutes the comments cite.
- **R1-6, fixed: a per-worktree salt works.**
  - Verification, on the SDK 9 reproduction (`PC-F95-reuse/run-salt.sh`). Each run used a fresh
    salt, so no leftover node could take part.
    - The first control, both builds unsalted, did not reproduce: B reused 11 idle SDK 9 nodes
      outside A's tree, and stayed green.
    - One salt for A and B: B connected to 11 of A's nodes, and the tree kill failed B with
      MSB4166 (22 lines). Reuse works under a salt.
    - Distinct salts, run twice: B launched its own 11 nodes, connected to none of A's, and the
      same kill left B green both times.
  - Cost: full Release rebuilds (`--no-incremental`) of the solution, interleaved:

    | Run | Nodes | Time |
    | --- | --- | --- |
    | Unsalted | 1 launched, 18 reused | 175 s |
    | Salted, first build | 19 launched | 177 s |
    | Salted, second build | 19 reused | 150 s |
    | Unsalted | 19 reused | 133 s |
    | Salted, third build | 19 reused | 134 s |
    | Unsalted | 19 reused | 161 s |

    - There is no measurable cost, even for the first salted build, which starts every node cold.
    - The other cost is memory: each worktree keeps its own idle nodes, up to one per core.
      Process checks are refused here, so it is unmeasured.
    - This sits oddly with the +40-80% measured earlier for `-nodeReuse:false`, which also starts
      every node cold. That round ran through the script on a busier machine, and the gap is not
      explained.
  - What a salt does not cover:
    - Two builds in one worktree still share nodes.
    - The compiler server is shared across worktrees whatever the salt. Not verified: whether a
      kill that ends it fails another build or only makes that build compile without the server.
  - Documented in CONTRIBUTING as the cheaper remedy, with
    `export MSBUILDNODEHANDSHAKESALT="$(git rev-parse --show-toplevel)"`, keeping
    `-nodeReuse:false` / `MSBUILDDISABLENODEREUSE=1` as the per-build fallback. The Acme e2e host
    keeps `MSBUILDDISABLENODEREUSE=1`, so its stop leaves no reusable nodes in the tree it ends.
- Also: ARCHITECTURE.md's CI/CD bullet and CONTRIBUTING's CI paragraph now say the step log lists
  what the stop ended.

Re-run for fix1:
- The script, on Windows (Git Bash) and Linux (dotnet/sdk:10.0, mawk), with
  `DOTNET_CLI_UI_LANGUAGE=de` set in the caller's environment:
  - a passing build: exit 0, English log;
  - a compile error: exit 1, both CS0029 lines, no summary groups;
  - the stall: exit 124, as above.
- `dotnet build Tellma.slnx` (Debug): "Build succeeded", 0 warnings, 0 errors.
- `dotnet format Tellma.slnx --verify-no-changes`: clean (only the workspace-loading warnings line).
- `bash -n` on the script.
