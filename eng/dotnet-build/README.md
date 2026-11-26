# dotnet build with evidence

`build-with-evidence.sh` runs `dotnet build` under a time limit and keeps the diagnostics a build
stopped from outside would otherwise lose. CI builds the solution through it, so a build that
stalls fails at a known time, well inside the job's own limit, rather than being cancelled with the
job, and leaves logs that show where it stopped.

## Running

Pass the limit, an evidence folder that is absent or empty, then `--`, then the arguments for
`dotnet build`:

```bash
# From the repository root, on Linux or on Windows under Git Bash:
bash eng/dotnet-build/build-with-evidence.sh --timeout 12m --evidence artifacts/build-evidence -- Tellma.slnx -c Release --no-restore
```

The limit is a whole number of seconds, minutes or hours (`90s`, `12m`, `1h`). The script exits
with the build's own exit code, or 2 when its arguments are wrong. Besides `dotnet` it needs only
GNU `timeout`, which Linux and Git for Windows both carry.

On a developer machine the script also helps with a build that ends in error MSB4166 ("Child node
exited prematurely"): a node that failed with an exception leaves a report in `msbuild/`, and the
connection trace shows which node went and when. A node ended from outside, by a tool that ends
another process's tree, leaves no report; CONTRIBUTING.md explains that case.

## The evidence folder

| File | What it shows | Survives a kill |
| --- | --- | --- |
| `build.binlog` | MSBuild's binary log: every project, target, task, property and message. Open it in the MSBuild Structured Log Viewer, or replay it with `dotnet msbuild build.binlog`. | No: it is buffered and compressed, so it is complete only when the build ends on its own or cancels. |
| `build.log` | MSBuild's text log at normal verbosity, each line stamped with its time and prefixed with the build node. Projects that start and never reach `Done Building Project` are the ones a stall held. | Yes: it is written line by line. |
| `msbuild/MSBuild_*.failure.txt` | MSBuild's report on a worker node that failed (error MSB4166): the exception that ended it. | Yes: it is written when the node fails. |
| `msbuild/MSBuild_CommTrace_PID_*.txt` | How each MSBuild process connected to its worker nodes (`MSBUILDDEBUGCOMM`). | Yes. |
| `roslyn/server.<pid>.log` | The compiler's own log (`RoslynCommandLineLogFile`), one file per process: each compilation the build asked the shared compiler server for, its connection, and its answer; and the server's side, when this build started the server. | Yes. |

MSBuild and the compiler reuse processes an earlier build left running, which keep part of the
settings they started with. The compiler client runs inside the worker nodes and reads its setting
on every build, so it logs from reused nodes too; a reused compiler server logs nothing here. A
reused worker node writes no connection trace, and writes a failure report into MSBuild's own
temporary folders (`MSBuildTemp*` under the system's temporary folder): the script copies every
report written there during the build into `msbuild/`. With node reuse off (`-nodeReuse:false`)
every node is the build's own and writes everything here.

## When the build passes its limit

The script first asks the build to stop, then kills it 30 seconds later if it is still running.
On Linux it sends SIGINT, which MSBuild takes as a Ctrl+C: it cancels the build, logs a warning
MSB4220 for each task it is still waiting on, and, once every task has stopped, writes the binary
log out in full. `timeout` sends the signal to the build's whole process group, though, so the
tools and worker nodes the build started, and a compiler server it started in that group, get the
SIGINT directly too: a stalled tool usually dies of it before MSBuild cancels it (error MSB3073,
exit code 130). A task that ignores the cancellation keeps the build running until the kill.
Git Bash on Windows cannot send a Ctrl+C to a native program (SIGINT does nothing there), so the
script sends SIGTERM, which ends the build's processes outright. On both, a build stalled on a tool
it started can lose only that tool and then fail on its own, with a complete binary log.

It then fails with exit code 124 (the build stopped when asked) or 137 (it had to be killed), and
writes into the step log, with no artifact to download:

- an `::error` annotation that says which of the two happened, and whether the binary log is
  complete;
- the errors and warnings the text log shows since the stop that name what the stop ended: MSB3073
  (a tool failed), MSB4220 (a task was cancelled) and MSB4166 (a worker node ended), each with its
  project;
- the projects the text log shows starting and not finishing, with the time each was requested and
  its node, and with how many of its instances are still open when one project builds several
  times at once (once per target framework, say);
- the compilations the compiler logs show requested and never answered: a compile waiting on the
  compiler server;
- the compilations the compiler server did not complete: a compile it failed or refused, after
  which the client compiled it itself, which points at a stopped or stuck server;
- the last 40 lines of the text log.

Each list says how many starts, finishes, requests and answers its parser read, so that a log it
could not read shows as 0 rather than as nothing pending. The script sets
`DOTNET_CLI_UI_LANGUAGE=en`, since the parsers read MSBuild's English messages.

A 137 before the limit means something else killed the build, such as the out-of-memory killer;
the script reports the same evidence for it, counting from the build's start.

The extra logging made a full Release rebuild of the solution about 5 to 7% slower on a
development machine, within the noise of the measurement; the build step's duration on CI is the
cost that counts.

CI uploads the evidence folder as an artifact when the build step fails or is cancelled. Set the
step's own `timeout-minutes` a little above the script's limit plus the 30-second grace, so that
the script, not the runner, stops the build and writes its report.
