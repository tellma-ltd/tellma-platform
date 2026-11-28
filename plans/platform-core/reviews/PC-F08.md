# PC-F08: out-of-process PDF rendering

## Implementer notes

### What was built, and where

PDFium no longer runs in the host. Each PDF thumbnail, and the PDFium preflight with it, is rendered
by a new, disposable worker process under operating-system limits; a hostile PDF can stop or starve
its own worker, never the host, and a worker that fails in any way is a missing thumbnail
(`decode-failed`, plus a `BlobPdfWorkerFailed` warning), never a failed upload.

- **`src/core/Tellma.Core.Imaging.PdfWorker/`** (new project and package, in `Tellma.slnx`): an
  `OutputType=Exe` assembly with `UseAppHost`, `GenerateRuntimeConfigurationFiles` and
  `GenerateDependencyFile` all false. `Program` -> `WorkerSession` (sandbox, ready, one request,
  render, one result, exit); `WorkerProtocol` (the three frames, shared with the host through
  `InternalsVisibleTo`); `WorkerSandbox` (closes host-named inherited descriptors that still refer to
  the same target, moves the protocol to a private `dup` of stdout and points fd 1 at stderr,
  installs the seccomp filter, exits the moment stdin yields anything after the request, `_exit` /
  `TerminateProcess` so no exit handler runs while a thread is inside PDFium); `SeccompFilter`
  (hand-built BPF for x64 and arm64, installed with `SECCOMP_FILTER_FLAG_TSYNC`);
  `PdfPreflight` and `PdfiumRasterizer` (moved unchanged in substance from the host, now over a
  `byte[]`); `PdfRenderer` (walk, render, lossless PNG); `WorkerFailure`. README.
- **`src/core/Tellma.Core.Imaging/Workers/`** (new): `PdfWorkerLauncher` (locates the muxer of the
  running runtime, the host's `.deps.json` from `APP_CONTEXT_DEPS_FILES` and the `.runtimeconfig.json`
  beside it, and the worker assembly; builds the least environment and `dotnet exec` arguments);
  `WorkerProcess` + `ErrorTail` (stderr drained on its own thread, first 2 KiB kept as printable
  ASCII); `WindowsWorkerProcess` + `WindowsInterop` + `SafeJobHandle` (CreateProcessW suspended with
  `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` of exactly the three pipe ends, `AssignProcessToJobObject`,
  `ResumeThread`; job limits below); `Win32Text` (command-line quoting and environment block, tested
  on both OSes); `UnixWorkerProcess` (`Process.Start` with a cleared environment, the inheritable-fd
  scan from `/proc/self/fdinfo`, `prlimit` with read-back); `PdfWorkerClient` (one render: start,
  ready, limits, request, result, check; kill on cancellation and always at the end; slot returned by
  the caller only after the process is gone; `Dispose` kills live workers); `WorkerPng` (managed PNG
  structure check); `PdfWorkerException`.
- **`SkiaImageProcessor`**: the PDF path takes an image slot (no process-wide PDF gate any more),
  reads the gated input as before, calls `PdfWorkerClient.Render` on a `LongRunning` task, and encodes
  the checked page with the new `SkiaPipeline.EncodeRenderedPage` (Skia PNG decode into the same
  BGRA-premul bitmap PDFium renders into, then the existing `EncodeBitmap`). New catch for
  `PdfWorkerException` (counted `decode-failed`, logged `BlobPdfWorkerFailed` at Warning). Now
  `IDisposable` (kills live workers; DI disposes the singleton). Removed: `PdfRenderGate`,
  `PdfRasterizer.cs`, the host's `PdfPreflight.cs`, the abandoned-render fail-fast.
- **`SkiaImageProcessorOptions.PdfWorkerMemoryLimit`** (`Tellma:Blobs:PdfWorkerMemoryLimit`, bytes,
  default 256 MiB, at least 32 MiB, validated with `ImageConcurrency` in one validator).
- **`ImagingStartupCheck`**: still probes Skia in-process; instead of PDFium in-process it starts a
  real worker (under the configured limit) and renders a 16-pixel probe page within 30 s, mapping
  start, exit and render failures to problems with fixes.
- **Test assets** (in `Tellma.slnx` under `/test/assets/`): `Tellma.Core.Imaging.TestWorker`, a stand-in
  that enters the real sandbox and then misbehaves or probes per mode; `Tellma.Core.Imaging.SmokeHost`,
  a generic-host application composing `AddTellmaSkia` (the publish demonstration, and run by the suite
  as its own process). READMEs for both.
- **Suite** (`test/core/Tellma.Core.Imaging.Tests`, 314 tests): new `Workers/` folder
  (`WorkerFailureTests`, `WorkerSandboxTests`, `HostileDocumentTests`, `SmokeHostTests`,
  `WorkerProtocolTests`, `WorkerPngTests`, `PdfWorkerLauncherTests`, `SeccompFilterTests` with a small BPF
  interpreter), `Thumbnails/PdfWorkerBudgetTests`, `Thumbnails/PdfRendererTests` (in-process walk and
  the byte-for-byte golden comparison), `Packaging/WorkerPackagingTests`; reworked `PdfPreflightTests`,
  `TimeBudgetTests` (PDF gate tests replaced), `ResilienceTests`, `ImagingCompositionTests`,
  `NativeIsolationTests`. README rewritten.
- **`ARCHITECTURE.md`**: repo layout and package table gain the worker; the optional-packages rule
  allows Imaging's own worker; a new Hosting bullet states the deployment consequence (framework-
  dependent on an installed runtime, not single-file or self-contained; seccomp and `prlimit` on
  Linux; the startup check catches a deployment that cannot).

### Design

**Launch.** `dotnet exec --runtimeconfig <host>.runtimeconfig.json --depsfile <host>.deps.json
Tellma.Core.Imaging.PdfWorker.dll` on the muxer three levels above `RuntimeEnvironment.GetRuntimeDirectory()`
(`DOTNET_HOST_PATH` as fallback). This is the vstest testhost pattern. It means the worker resolves
exactly the host's closure in whatever layout the host was built or published in (RID-less with
`runtimes/<rid>/native`, or RID-specific with flattened natives), needs no files of its own beyond its
dll, and runs on the same runtime. I first tried a referenced Exe with its own runtimeconfig/deps:
`-r win-x64` publish fails with NETSDK1152 (duplicate apphost/deps/runtimeconfig), which is why the
worker generates none. Environment overrides beat the host's runtimeconfig for the CLR knobs: verified
`DOTNET_gcServer=0` against a host with `ServerGarbageCollection=true`, and
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.

**Environment.** Exactly `DOTNET_gcServer=0`, `DOTNET_gcConcurrent=0`, `DOTNET_EnableDiagnostics=0`
(no debugger, profiler or EventPipe attach), `DOTNET_DbgEnableMiniDump=0`,
`DOTNET_EnableCrashReport=0`, `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, `DOTNET_TieredPGO=0`, and on
Windows `SystemRoot`. No PATH, HOME, TEMP or any host variable (so no App Service secrets). Working
directory: `/` or System32. A test reads the worker's own environment and asserts the exact set.

**Handles.** Windows: pipes are created non-inheritable, the three child ends are made inheritable,
`CreateProcessW(…, bInheritHandles=TRUE, EXTENDED_STARTUPINFO_PRESENT|CREATE_SUSPENDED|…)` with a
handle list of exactly those three, then the host closes its copies at once. Linux: .NET opens every
fd close-on-exec, so only accidental non-CLOEXEC fds can leak; the host lists them (fd + link target)
from `/proc/self/fdinfo` flags and passes `--close-fd n target`; the worker closes each that still
reads the same link target. The test opens a deliberately inheritable marker (Windows:
`SetHandleInformation(INHERIT)`; Linux: raw `open(2)`), and inspects the running worker's handle
table from outside (Windows: `DuplicateHandle` of the same value + `GetFinalPathNameByHandle`; Linux:
`/proc/<pid>/fd`).

**Limits, enforced from outside, after the ready frame and before the request** (the worker has run
only its own trusted startup and sandbox code by then):

| | Windows (Job Object) | Linux (`prlimit` from the host, soft = hard) |
|---|---|---|
| memory | `JOB_OBJECT_LIMIT_PROCESS_MEMORY` = committed at ready + limit | `RLIMIT_DATA` = `VmData` at ready + limit; `RLIMIT_AS` = `VmSize` + 2 x limit (backstop) |
| CPU | `PerProcessUserTimeLimit` = budget + 1 s | `RLIMIT_CPU` = used so far + budget + 1 s (SIGKILL) |
| processes | `ACTIVE_PROCESS` = 1 | seccomp: no `execve`/`execveat`/`fork`/`vfork`/non-thread `clone` |
| network | not enforceable unprivileged | seccomp: no `socket`/`socketpair` |
| other | `KILL_ON_JOB_CLOSE`, `DIE_ON_UNHANDLED_EXCEPTION`, UI limits all | `RLIMIT_CORE` = 0, `RLIMIT_FSIZE` = 0 (SIGXFSZ on a file write), seccomp: no ptrace / process_vm / kcmp / pidfd_getfd / io_uring / userfaultfd / perf / bpf / unshare / setns / key store |

Why `RLIMIT_DATA` and not only `RLIMIT_AS`: the .NET runtime reserves about 66 GB of address space at
start (`VmSize` 66,529 MB in the container) and the GC commits inside that reservation, which
`RLIMIT_AS` no longer sees. Measured: with only `RLIMIT_AS` = `VmSize` + 512 MB, a managed allocation
loop was not stopped by it at all (it ran until `RLIMIT_CPU` killed it); with `RLIMIT_DATA` = `VmData`
(about 40 MB) + 256 MB both native and managed loops stopped at the limit, because making a reserved
page writable counts against `RLIMIT_DATA` (`mprotect_fixup`'s `may_expand_vm`).

**IPC.** Little-endian frames with magic numbers: ready `TPW1` + capabilities; request `TPRQ` + size
(1..2048) + length (1..32 MiB) + bytes; result `TPRS` + status (rendered / refused / failed) + length
(capped at `MaxImageBytes(size)`, 266 KiB at 256 px, or 1 KiB for a detail) + bytes. Every cap is
checked before allocation. The PNG is then checked in managed code (`WorkerPng`: signature; IHDR
first, 8-bit RGB/RGBA, no interlace, longer side exactly the requested size; only `sBIT` (all 8s, as
Skia writes it) and `IDAT` before an empty final `IEND`, nothing after), decoded by Skia at exactly
those dimensions, and re-encoded. Worker detail text becomes one printable-ASCII line before logging.

**Lifecycle: per-request spawn, measured.** Whole PDF thumbnail including process start, from a
`dotnet publish` output (`--measure 10` of the smoke host, letter page, 256 px WebP), on this shared
and often loaded machine: Windows win-x64 min 316 / median 399 / max 608 ms; sdk:10.0 linux-x64 min
225 / median 274 / max 320 ms in one run and min 438 / median 556 / max 881 ms in another under load;
sdk:10.0-resolute min 258 / median 311 / max 802 ms. In-worker render alone is 90-230 ms; the rest is
process and runtime start (a hello-world `dotnet exec` takes 0.5 s on this machine through Git Bash). Against a 5 s budget, on a path taken once per PDF upload, a pool
of warm workers would save about 0.2 s per PDF and would either break single-use isolation (one
tenant's document per process) or, as pre-started single-use spares, add idle processes and refill
logic. Per-request it is; a warm single-use spare is the optimisation if PDF upload latency ever
matters. Concurrency: PDF work takes one of the existing `ImageConcurrency` slots, released only when
the worker process has exited (a test asserts at most `Slots` workers at once, and every failure
path returns the slot).

**Orphans.** Windows: the job's `KILL_ON_JOB_CLOSE` kills the worker when the host's last handle
closes, however the host dies (test: kill the smoke host, its worker dies). Linux: after the request
the worker watches stdin on its own thread and `_exit`s the moment it yields anything; the host's pipe
end closes when the host dies (test: the same kill-the-host test, and a direct close mid-render: exit
67 within 24-46 ms); `RLIMIT_CPU` bounds a worker that ignored even that. Graceful shutdown: the
processor's `Dispose` kills live workers (test).

**Packaging.** `Tellma.Core.Imaging` references the worker project normally, so in-repo consumers
get its dll beside theirs and listed in their deps.json; packed, it is a package dependency
(`Tellma.Core.Imaging.PdfWorker`, `lib/net10.0/Tellma.Core.Imaging.PdfWorker.dll`, depending on
PDFtoImage, SkiaSharp and the pinned natives). Verified: (a) the smoke host published `-r win-x64`
(Windows), `-r linux-x64` in sdk:10.0 and in sdk:10.0-resolute, each run from the publish output
(`result=held`: check passed, honest page rendered, FormFanOut(7) stopped, host memory +0-3 MiB,
PDFium and PDFtoImage never loaded into the host); (b) all four packages packed to a local feed and
consumed by a scratch app via `PackageReference Tellma.Core.Imaging`, published `-r win-x64` on
Windows and `-r linux-x64` in sdk:10.0 and run from the publish output: check passed, thumbnail
rendered, PDFium not in the host. I first tried packing the worker inside
Imaging's nupkg (`PrivateAssets=all` + `BuildOutputInPackage`): the package was right but in-repo
consumers then lost the worker, so the separate package is the honest shape.

### Measurements (hostile documents)

- FormFanOut(6, 10), 1,702 bytes: unconstrained (scratch experiment, no preflight) it peaks at
  565 MB working set in 2.2 s on Windows and 573-589 MB in 4-6.5 s in sdk:10.0. Now, through the
  processor at the default limit and 5 s budget: Windows answered at 5.06 s (timeout), worker gone at
  5.26 s, host memory -2.2 MiB; sdk:10.0 5.00 s / 5.13 s / -3.7 MiB; resolute 5.00 s / 5.07 s /
  -3.7 MiB. (One earlier sdk:10.0 run, at normal verbosity, finished both cases in about 2 s, which
  fits the limit ending the worker first.)
- FormFanOut(8, 10), 2,148 bytes: the same (answered 5.00-5.06 s, gone by 5.15 s, host within
  +3.6 MiB).
- The worker's own memory, sampled every 25 ms while it expands FormFanOut(8) under the default
  256 MiB: Windows committed memory grew 256 MiB and stopped; `VmData` grew 255 MiB and stopped in
  both containers. Observed behaviour at the cap: PDFium mostly keeps running rather than dying at
  the first refused allocation (in a scratch run some cases ended in `std::bad_alloc` -> SIGABRT after
  5-10 s), so most such documents end at the budget kill. Either way the host is untouched and the
  slot comes back within the budget plus about 0.25 s.
- Probes under a 128 MiB limit: 112 MiB of 16 MiB blocks native and managed, both OSes.
- CPU limit 1 s on a spinning worker the host waits on forever: Windows ended at 3.8 s
  (`0xC0000044` STATUS_QUOTA_EXCEEDED), Linux at 2.4 s (137).
- With 1 GiB to spare, FormFanOut(6, 10) is refused by the walk's object cap (`objects`).

### Deviations (also in the commit message)

1. Spec 0017 §3.6 (one process-wide PDFium lock, held past the timeout) and review flag 23 are
   superseded: PDF thumbnails render in a killable, OS-limited worker per document, per D-030
   addendum 2. PDF work now takes an `ImageConcurrency` slot; the D-030 addendum-1 fail-fast while an
   abandoned render holds the gate is gone, because a worker past its budget is killed.
2. Spec 0017 §1.1: `Tellma.Core.Imaging` references `Tellma.Core.Abstractions`, the new
   `Tellma.Core.Imaging.PdfWorker` and SkiaSharp (+ NoDependencies); PDFtoImage moved to the worker.
3. New option `Tellma:Blobs:PdfWorkerMemoryLimit` (not in §1.3/§3.5).
4. New log event `BlobPdfWorkerFailed` (Warning; kind, failure, exitCode, detail) beside §10.2's.
5. PDF thumbnails are at most 2048 px on a side (larger yields null, `decode-failed`); the 32 MiB
   input cap is unchanged.
6. `core.imaging` starts a worker: a host that cannot run one (single-file, self-contained, no
   installed runtime, a kernel or runtime that refuses seccomp) fails startup with the fix.
7. On Linux a worker without its seccomp filter is refused (fail closed); the filter is written for
   x64 and arm64 only. macOS (developers only) gets the wall-clock kill and no limits.
8. `SkiaImageProcessor` is `IDisposable`.

### Residual risk

- **A compromised worker runs as the host's user.** PDFium RCE gives code execution that can read
  whatever the host account can (app settings, and on Linux `/proc/<host>/environ` of a same-uid
  parent unless the host is non-dumpable; Yama `ptrace_scope` was 1 in both containers, which stops
  ptrace but not that read) and can smuggle it out in the thumbnail's pixels, the one output the host
  accepts. No network on Linux; on Windows the network is open too. Landlock (Linux) or an
  AppContainer / low-IL restricted token (Windows) would close the filesystem path; I did not add
  either: both are environment-sensitive (Landlock availability in App Service and Docker; AppContainer
  needs ACLs on the app directory) and beyond this item's brief. Recommend a follow-up.
- **Windows network**: no unprivileged mechanism; documented, the socket test skips on Windows with
  that reason.
- **Memory is per worker**: `ImageConcurrency` x (`PdfWorkerMemoryLimit` + ~25 MB runtime) is the
  PDF bound; with the default half-the-cores slots on an 8-core host that is about 1.1 GB. Size both.
- **PDFium spins at the cap** until the budget kill (5 s) for many expansion bombs, holding a slot and
  a core for the budget. Bounded, measured above.
- **Windows pipe race**: our child pipe ends are inheritable for the instant around `CreateProcessW`; a
  concurrent `Process.Start` elsewhere in the host (which inherits every inheritable handle) could
  inherit one. Consequence: that host-trusted child holds a pipe end. .NET's own lock for this is
  private.
- **Linux fd race**: a non-CLOEXEC fd a native library opens between the host's scan and the fork
  reaches the worker unclosed. None exist in a .NET host today (the test's marker is the only one).
- **Unverified on App Service**: nested Job Objects (Windows App Service sandbox) and seccomp/`prlimit`
  (Linux App Service) are expected to work but untested here; the startup check fails loudly where
  they do not.
- Process start is ~0.2-0.5 s per PDF thumbnail on the upload's critical path.

### Verification

- Gate on Windows (PROCESS.md §11): `dotnet build Tellma.slnx` succeeded with 0 warnings;
  `dotnet format Tellma.slnx --verify-no-changes` clean (0 error lines, twice); the PR-tier unit
  filter over `Tellma.slnx` green in every assembly (Imaging 312 passed, 2 skipped: the Linux-only
  socket and file-write probes). Integration suites, one at a time: Queryex 221/221, EntityFrameworkCore 10/10, AcsEmail, MarminAe and SendGrid (no test matches the filter), Identity 140/140 (36 min under shared load).
- `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu 24.04, glibc 2.39, SDK 10.0.400, Yama 1), from the
  slot's tree: Imaging 314/314 in Release; smoke host published `-r linux-x64` and run from the
  output, `result=held`; the packed packages consumed and published `-r linux-x64`, thumbnail
  rendered. (A first run was 311/313 on two test bugs, both fixed: the file-write test expected the
  wrong exception shape, and the stand-in did not watch its input as the real worker does.)
- `mcr.microsoft.com/dotnet/sdk:10.0-resolute` (Ubuntu 26.04, glibc 2.43, SDK 10.0.403): Imaging
  314/314; smoke host from `-r linux-x64` publish, `result=held` (median 381 ms per thumbnail).
- The seccomp filter is installed with `TSYNC` in both containers (Docker Desktop's default profile),
  and the worker's sockets, process starts and file writes are refused there.
- Suite cost: the Imaging suite now takes about 3 minutes on this Windows machine (it was 14-24 s),
  dominated by some 150 worker process starts; worth watching against CI `build-test`'s 20-minute
  timeout.
- One command was refused by a built-in safety check (`rm -rf out-resolute/*` of an empty scratch
  folder after a `cd`); it was not retried and nothing depended on it.

### What reviewers should examine hardest

1. `WindowsWorkerProcess.Start`/`CreateSuspendedProcess`: the handle list and every handle's close on
   every path; suspended creation, job assignment and resume order; `TerminateProcess` on a failed
   assignment; the struct layouts in `WindowsInterop` (x64 and arm64).
2. `SeccompFilter.Build`: the syscall numbers per architecture, the jump offsets, the architecture and
   x32 checks, the `clone` flag test; `TSYNC`; that nothing the runtime or PDFium needs is refused.
3. `UnixWorkerProcess.ApplyLimits`: the `RLIMIT_DATA`/`RLIMIT_AS` baselines, `RLIMIT_CPU` from
   `/proc/<pid>/stat`, the read-back; `InheritableDescriptors` and the worker's `CloseIfStill`.
4. `PdfWorkerClient.Render`/`Talk`: cancellation versus death versus protocol violation, the kill on
   every path, and the slot coming back only after the process is gone.
5. `WorkerPng.Check` and `SkiaPipeline.EncodeRenderedPage`: everything the host takes from a possibly
   compromised worker.
6. `WorkerSession`/`WorkerSandbox`: nothing of the upload is read before the sandbox is in place and
   the host's limits applied; the stdin watchdog and `_exit`.
7. The launch model (host runtimeconfig + deps.json, the muxer location) against hosting shapes I could
   not run here: IIS in-process on Windows App Service, the App Service Linux image.

## Review 1

Reviewed commit da3714f (`git diff da3714f^ da3714f`) against 0017 §1.1, §1.3, §3.5, §3.6, §9,
§10.1, §10.2 and §11, review flag 23, D-030 with addenda 1-3, and the PC-053 reviews.

What I ran:
- `Tellma.Core.Imaging.Tests` on Windows, Release, PR-tier filter: 312 passed, 2 skipped (the
  Linux-only socket and file-write probes), 314 total.
  - Test run 2 min 59 s, or 5 min 01 s with the build.
  - The machine was shared: other slots were running SQL Server containers.
- The same suite in `mcr.microsoft.com/dotnet/sdk:10.0`, from a `git archive` of da3714f: 314/314.
  - Test run 1 min 32 s, or 256 s with restore and build.
  - 20 cores, Yama `ptrace_scope` 1.
- The same suite in `mcr.microsoft.com/dotnet/sdk:10.0-resolute`: 314/314, test run 1 min 48 s (364 s
  with restore and build).
- A scratch probe in sdk:10.0, outside the repo, for R1-1.
  - It puts on itself exactly the limits `UnixWorkerProcess.ApplyLimits` puts on a worker: soft =
    hard, `RLIMIT_DATA` = VmData + cap, `RLIMIT_AS` = VmSize + 2 x cap, with a 64 MiB cap.
  - It then allocates in the ways the filter allows.
- The CI budget: `build-test` (20 min, both OSes) builds the solution and runs every unit assembly.
  - The Imaging assembly alone now takes about 3 min of test time on Windows here, and 1.5 min on
    Linux.
  - It fits. It is still worth watching: it is by far the slowest unit assembly, and it grows with
    every worker test. No finding.

What holds, checked line by line:
- **Windows launch.**
  - Order: `CREATE_SUSPENDED`, then `AssignProcessToJobObject`, then `ResumeThread`. The worker runs
    no instruction outside its job, and a failed assignment terminates it.
  - Handles: the handle list is exactly the three child pipe ends, and the parent ends are never
    inheritable. Every handle is closed on every path.
  - Layouts: `STARTUPINFOW`, `JOBOBJECT_EXTENDED_LIMIT_INFORMATION`, `PROCESS_MEMORY_COUNTERS_EX` and
    `PROCESS_INFORMATION` match the headers on x64 and arm64.
  - Limits: `ProcessMemoryLimit` is per process. `ACTIVE_PROCESS` = 1 means no grandchild, so
    `TerminateJobObject` and `KILL_ON_JOB_CLOSE` end everything.
- **Limits come before untrusted bytes.** On both OSes the request is written only after
  `ApplyLimits` returns. Before that, the worker has run only its own startup and sandbox code.
- **The seccomp program.**
  - The x64 and arm64 numbers in the deny list are right.
  - So are the opcodes, the return values, the `AUDIT_ARCH` values, the x32 check and every jump
    offset.
  - So are the `clone` test for `CLONE_THREAD` and the `clone3` `ENOSYS` fallback.
  - The `sock_fprog` layout is right, and `no_new_privs` plus `TSYNC` is the correct installation.
- **`RLIMIT_DATA` is the right binding limit for PDFium's own behaviour.**
  - Private writable mappings, glibc's mmap'd chunks and the GC's commits through `mprotect` all
    count against it.
  - My probe confirmed it: a private mapping past the cap is refused with `ENOMEM`.
- **Kill at the budget.**
  - The budget token kills the worker, which breaks the pipe a read is blocked on.
  - The slot comes back only after `Kill` and `WaitForExit`, on every path in `RunPdfInSlot`.
  - Grandchildren are impossible on both OSes.
- **IPC.**
  - Every length is capped before allocation.
  - The PNG check fixes the signature, IHDR, dimensions and chunk set before Skia decodes the image
    at those dimensions.
  - Detail and stderr text become printable ASCII.
  - A partial frame followed by a hang ends at the budget kill.
- **Lifecycle.** The startup check catches all of these:
  - a missing or corrupt worker dll;
  - a missing deps.json or runtimeconfig;
  - a single-file or self-contained host;
  - a refused filter.
- **No regression.** The golden test shows that normal PDFs render byte-for-byte as before. Every
  worker failure yields null, so the upload path is unchanged.
- **Packaging.** Packing and consuming the package are documented, and so is the framework-dependent
  rule, in ARCHITECTURE.md and both READMEs.
- **AGENTS.md.** XML docs and section comments are in place. All three new projects have READMEs.
  No code, message or test name references `docs/` or a spec.

I accept deviations 1-8. Deviation 7 (macOS without limits) is acceptable for an OS used only in
development.

### Findings

**R1-1 (major): On Linux, a worker running exploit code can escape its memory cap and act on the host. The filter and the limits cover how PDFium behaves, not what an attacker does.**
- Where:
  - `src/core/Tellma.Core.Imaging.PdfWorker/SeccompFilter.cs:108-124` is a deny list. It leaves
    SysV IPC, signals and `prlimit64` open.
  - `src/core/Tellma.Core.Imaging/Workers/UnixWorkerProcess.cs:185-186`: the `RLIMIT_AS` backstop is
    VmSize + 2 x limit, and VmSize is about 66 GB of runtime reservation.
  - These claims overstate the isolation:
    - `ARCHITECTURE.md:1212`: "no access to other processes" and "can therefore only stop or starve
      its own worker";
    - `src/core/Tellma.Core.Imaging/SkiaImageProcessor.cs:34` and `Workers/PdfWorkerClient.cs:21-23`:
      "never the host".
- Threat: a PDFium memory-corruption bug gives an attacker code execution in the worker. That is the
  threat this item moved the work out of process for.
- Scenario 1: memory beyond the cap, which outlives the worker.
  - `RLIMIT_DATA` counts only private writable mappings, so only `RLIMIT_AS` limits
    `MAP_SHARED|MAP_ANONYMOUS` memory.
  - The worker can `munmap` the GC's 66 GB `PROT_NONE` reservation. That frees the same amount of
    `RLIMIT_AS` room, in which it can map and touch tens of GB of shared memory.
  - SysV shared memory is worse. While detached, `shmget` segments count against neither limit, and
    they survive the process. The worker attaches 32 MiB, touches it, detaches it, and repeats.
  - Measured in sdk:10.0 under the exact worker limit shape with a 64 MiB cap:
    - a 96 MiB private mapping was refused with `ENOMEM`;
    - a 96 MiB shared anonymous mapping was mapped and touched;
    - 16 SysV segments of 32 MiB were touched, 512 MiB in all, and `ipcs -m` still listed all of
      them after the probe had exited.
  - Only `kernel.shmall` and the container's memory cgroup bound that loop. The host container is
    OOM-killed, and the memory stays held after every worker is gone.
- Scenario 2: killing or starving the host. The kernel's checks for these calls pass for the same
  uid and gid.
  - `kill(getppid(), SIGKILL)` ends the host. So does `kill(0, SIGKILL)`, because the worker shares
    the host's process group.
  - `prlimit64(getppid(), RLIMIT_NOFILE | RLIMIT_AS | RLIMIT_CPU, …)` lowers the host's own limits,
    with the same call the host uses on the worker.
    - A tiny `RLIMIT_NOFILE` stops the host accepting connections.
    - A 1 s `RLIMIT_CPU` kills it a second later.
  - My probe read the parent's limit through `prlimit` unhindered.
- Scenario 3: a host that runs as root. Several container images run the app as root.
  - The worker is then root as well, with the container's capabilities.
  - Under Docker's defaults those include `DAC_OVERRIDE`.
  - In a privileged or bare-VM deployment they include `CAP_SYS_RESOURCE`, with which it can raise
    its own hard limits again.
- Fix:
  1. Shrink the worker's address-space reservation so that `RLIMIT_AS` becomes a real cap, one that
     also covers shared mappings.
     - Set `DOTNET_GCHeapHardLimit` (to the limit), `DOTNET_GCRegionRange`, or both in
       `PdfWorkerLauncher.RuntimeSettings`.
     - Then set `RLIMIT_AS` = VmSize + limit + a measured slack.
     - Keep `RLIMIT_DATA` as well.
  2. In the filter, deny these calls:
     - `shmget`, `msgget` and `semget` (x64 29, 68 and 64; arm64 194, 186 and 190);
     - `tkill`, `pidfd_open` and `pidfd_send_signal`.
  3. In the filter, allow these calls only on the worker itself:
     - `kill`, `tgkill`, `rt_sigqueueinfo` and `rt_tgsigqueueinfo` only when the first argument is
       the worker's own pid;
     - `prlimit64` only when the first argument is 0 or the worker's own pid.
     - The pid can be baked into the program when the filter is installed, since the worker never
       forks.
     - The runtime's own signals (`tgkill(getpid(), tid, …)`) and glibc's `getrlimit` keep working.
  4. In `WorkerSandbox.Enter`, drop every capability (`capset` to empty sets) before installing the
     filter.
  5. Add tests:
     - stand-in probes that a SysV segment, a `kill` of the parent and a `prlimit` on the parent are
       all refused on Linux;
     - a shared-mapping probe that is stopped at the limit;
     - the matching cases in the BPF interpreter.
  6. Until this lands, correct the three claims above and the README's list of residual risks.

**R1-2 (major): On Windows, a worker running exploit code can take over the host process. The recorded residual risk names only file reads leaked through pixels and the open network.**
- Where:
  - `src/core/Tellma.Core.Imaging/Workers/WindowsWorkerProcess.cs:146-147` and `:401`: the worker is
    created with the host's own token.
  - The residual-risk record: D-030 addendum 3, `src/core/Tellma.Core.Imaging/README.md:135-145`,
    and the implementer notes.
  - The "never the host" claims cited in R1-1.
- Scenario:
  1. The worker runs as the same user, at the same integrity level, as the host. The default process
     DACL gives that user full access, so `OpenProcess(PROCESS_ALL_ACCESS, hostPid)` succeeds.
  2. `WriteProcessMemory` and `CreateRemoteThread` then give it code execution inside the host:
     the host's secrets, its database connections and its tenants' data.
  3. It can also `DuplicateHandle` the job handle out of the host and outlive it, and it can write
     any file the host can write.
  4. Neither the Job Object nor its UI restrictions prevent any of this.
- Unverified: I believe pagefile-backed sections (`CreateFileMapping(INVALID_HANDLE_VALUE, …)`) are
  not charged to the process's private commit. If so, they also bypass `ProcessMemoryLimit`; this
  needs checking.
- Why it matters despite the ruling:
  - Linux is the production host, so this matters for development, test, and any Windows App
    Service deployment.
  - Even so, the record should say what it is: on Windows, a PDFium code-execution exploit is a host
    compromise.
- Fix (the orchestrator's choice):
  1. Start the worker on a restricted, low-integrity copy of the host's token, as Chromium's sandbox
     does:
     - Steps: `CreateRestrictedToken(DISABLE_MAX_PRIVILEGE)`, then
       `SetTokenInformation(TokenIntegrityLevel, Low)`, then `CreateProcessAsUserW`.
     - A restricted copy of the caller's own primary token needs no
       `SeAssignPrimaryTokenPrivilege`.
     - The mandatory no-write-up policy then denies the worker write, thread-creation and VM access
       to the medium-IL host, and writes to medium-IL files.
     - It can still read the app directory and the runtime. That read access is the file-read
       residual risk already recorded.
     - Verify it under IIS/App Service, where the identity differs.
  2. Or record exactly this risk in D-030 addendum 3, the README and ARCHITECTURE.md, and keep PDF
     thumbnails off every Windows production deployment.

**R1-3 (minor): PDF work now takes `ImageConcurrency` slots, so a burst of hostile PDFs can make other users' image uploads fail with 503.**
- Where: `src/core/Tellma.Core.Imaging/SkiaImageProcessor.cs:308-314`.
- Before this change, hostile PDFs queued only behind the PDF gate and failed fast. Only PDF
  thumbnails suffered.
- Scenario:
  1. A fan-out PDF holds a slot for its whole budget plus the exit wait: about 5.1-5.3 s, measured.
  2. A member keeps `ImageConcurrency` such uploads in flight (4 on an 8-core host) and so fills
     the FIFO slot queue.
  3. `ProcessAsync` waits behind them for up to its 15 s budget, then throws
     `ImageProcessingBusyException`.
  4. Avatar and photo uploads from every tenant on that process get 503.
  5. The staged-rows cap per user limits how long one account can keep this up. It does not limit
     the effect while it lasts.
- Fix, either of:
  - Give PDF workers their own, smaller bound, `PdfWorkerConcurrency` for example, defaulting to
    max(1, `ImageConcurrency` / 2), taken in addition to an image slot or instead of one.
    - PDFs then never hold every image slot.
    - The PDF memory bound gets its own knob.
  - Record the trade-off in D-030 addendum 3.

**R1-4 (minor): "Its CPU limit bounds it regardless" is not true in two cases.**
- Where:
  - `src/core/Tellma.Core.Imaging/README.md:94`, and the notes' "Orphans" paragraph;
  - `src/core/Tellma.Core.Imaging/SkiaImageProcessor.cs:445-448`.
- Case 1: an infinite budget.
  - The contract allows `Timeout.InfiniteTimeSpan`. For it, `CpuLimit` returns null, and neither OS
    gets a CPU limit.
  - A worker spinning at its memory cap then holds its slot until the caller cancels. The notes say
    that is how expansion bombs usually end.
- Case 2: a host crash on Linux.
  - Orphan cleanup is then the worker's own thread watching stdin, which only cooperative code
    honours.
  - A compromised worker that sleeps uses no CPU, so `RLIMIT_CPU` never ends it. It lives on, with
    its memory and its file access.
- Fix:
  - Always set a CPU ceiling: min(budget + 1 s, 60 s), and 60 s for an infinite budget.
  - Reword the README and the notes: on Linux, orphan cleanup relies on the worker's cooperation.
    Only a PID namespace or a cgroup would remove that reliance.

**R1-5 (minor): The Windows pipe-inheritance race can hold an image slot indefinitely, which is worse than recorded.**
- Where:
  - `src/core/Tellma.Core.Imaging/Workers/WindowsWorkerProcess.cs:137-143`;
  - the read in `PdfWorkerClient.Talk` (`Workers/PdfWorkerClient.cs:117`);
  - the "Windows pipe race" bullet in the notes, which says only that another child may hold a pipe
    end.
- Scenario:
  1. A `Process.Start` elsewhere in the host lands inside the window. Nothing in `src` does this
     today, but third-party code may.
  2. Its child inherits the worker's stdout write end (`childOutput`).
  3. When the worker is killed at the budget, the host's synchronous `ReadFile` on the read end sees
     no broken pipe, because another process still holds a writer.
  4. `Render` never reaches its `finally`, so the `RunPdfInSlot` thread and its image slot stay held
     for as long as that unrelated child lives.
  5. After a few such events every image upload is refused as busy until restart.
- Fix, either of:
  - Make the kill also cancel the host's pending pipe I/O: `CancelSynchronousIo` on the conversation
    thread, or overlapped pipe ends with `CancelIoEx`.
  - Correct the recorded consequence.

**R1-6 (minor): No test proves that the filter covers the threads that existed before it was installed (`TSYNC`).**
- Where:
  - `test/core/Tellma.Core.Imaging.Tests/Workers/WorkerSandboxTests.cs:70-77`;
  - the stand-in's `socket` probe, `test/core/assets/Tellma.Core.Imaging.TestWorker/Program.cs:153`.
- Scenario:
  - Every probe runs on the worker's main thread, which is the thread that installed the filter.
  - A mutation that passes flags 0 instead of `SECCOMP_FILTER_FLAG_TSYNC` would leave the runtime's
    other threads unfiltered, and the suite would stay green.
  - Exploit code can run on any of those threads, or hand work to them.
- Fix: have the stand-in start a thread before `WorkerSandbox.Enter`, then probe the socket and
  process calls from that thread once the filter is in place.

### Responses

This round was started by one implementer and finished by a second, who reviewed the half-done work,
kept what was right, corrected what was wrong, and verified the whole on both operating systems.

- R1-1: fixed in 7ea007f. One part of fix step 1 falls short of what the finding
  hoped for; it is stated under "What the address-space limit still leaves" and goes to the
  orchestrator.
  - **Filter** (steps 2 and 3; `SeccompFilter.Build(x64, processId)`).
    - Refused outright now: the whole SysV family (`shmget`, `shmat`, `shmctl`, `semget`, `semop`,
      `semctl`, `semtimedop`, `msgget`, `msgsnd`, `msgrcv`, `msgctl`); `tkill`, `pidfd_open`,
      `pidfd_send_signal`; and `ioprio_set`, `migrate_pages`, `move_pages`.
    - Allowed only on the worker's own pid, which is fixed into the program when it is installed:
      `kill`, `tgkill`, `rt_sigqueueinfo`, `rt_tgsigqueueinfo`. On that pid or 0: `prlimit64`,
      `sched_setaffinity`, `sched_setscheduler`, `sched_setparam`, `sched_setattr`. `setpriority`
      only for `PRIO_PROCESS` and that pid or 0. `prctl(PR_SET_PDEATHSIG)` is refused.
    - This is more than the finding and D-030 addendum 4 list: the scheduling, priority and
      page-placement calls reach the host exactly as `kill` and `prlimit64` do, and the prctl would
      undo the parent-death signal of R1-4.
    - The filter compares the low 32 bits of a pid argument, which is all the kernel reads of it.
  - **Capabilities** (step 4). `WorkerSandbox.Enter` empties the permitted, effective and
    inheritable sets with `capset` and clears the ambient set, before it installs the filter (which
    sets `no_new_privs`).
  - **Address space** (step 1). Linux workers start with `DOTNET_GCHeapHardLimit` = the memory limit
    and `MALLOC_ARENA_MAX=1`; `RLIMIT_AS` = VmSize + limit + 128 MiB
    (`UnixWorkerProcess.AddressSpaceSlack`); `RLIMIT_DATA` is unchanged. Windows workers get neither
    setting (D-030 addendum 5).
    - Measured in sdk:10.0 (x64, glibc 2.39, SDK 10.0.400) at the default 256 MiB: a ready worker
      holds 3,328,784 kB of address space, where it held about 66.5 GB, and 32,020 kB of data.
    - The slack is measured: an honest render maps 17,372 kB that `RLIMIT_DATA` does not count
      (library code, shared mappings, guard pages), for the page and the text document alike.
      128 MiB is seven times that, and
      `An_honest_render_maps_less_than_half_the_address_space_slack_on_linux` fails when a render
      needs more than half of it.
    - `DOTNET_GCRegionRange` is deliberately not set. The first half of this round set it to twice
      the limit. With that, the managed-allocation probe died of SIGSEGV (exit 139) inside the
      collector, which ran out of regions before it reached the heap limit. With the heap limit
      alone the runtime reserves five times the limit, and the same probe ends in an ordinary
      refusal at 112 of 128 MiB.
    - **What the address-space limit still leaves.** Of the 3.17 GiB a ready worker holds,
      1,883 MiB is the runtime's `PROT_NONE` reservation for executable memory and 1,235 MiB is the
      collector's range. Exploit code can unmap both and map shared memory in their place, up to
      `RLIMIT_AS`: about 3.55 GiB per worker at the default limit, times `PdfWorkerConcurrency`,
      until each worker is killed at its budget. The limit is a real one now (it was 66 GB), but it
      is not "limit + slack". The executable reservation shrinks only when `RLIMIT_AS` is already
      finite as the runtime starts (`DOTNET_InitialExecMemoryPercent` then applies; measured under a
      4 GiB soft limit: 798 MiB by default, 102 MiB at 3 percent), which `Process.Start` cannot
      arrange. A worker that re-executes itself under a soft limit could, for one more runtime start
      per PDF, which I estimate at about a quarter more latency (not built, so not measured). I did
      not take that step on my own: the ruling fixes the formula, and a memory cgroup around each
      worker would bound all of it, kernel memory included. The README's residual risks and
      ARCHITECTURE.md state the 3.5 GiB figure.
  - **Tests** (step 5), all in `Workers/`.
    - `A_worker_cannot_act_on_its_host_or_create_sysv_objects_on_linux`: from inside the sandbox,
      `kill` and `tgkill` of the host, `kill(0)`, `tkill`, `pidfd_open`, `prlimit64`, `setpriority`
      and `sched_setaffinity` on the host, the parent-death prctl, `shmget`, `msgget` and `semget`
      all answer `EPERM`; `kill(self, 0)` and `prlimit64` on 0 and on the worker's own pid work.
    - `A_worker_holds_no_capability_on_linux`: `CapInh`, `CapPrm`, `CapEff` and `CapAmb` are all 0.
      The containers run the suite as root, so this is the root case of scenario 3.
    - `The_operating_system_stops_a_worker_at_its_address_space_limit_on_linux_shared_memory_included`:
      shared anonymous mappings stop at 240 MiB under a 128 MiB limit (limit + slack). The probe
      does not unmap the runtime's reservations; what happens when exploit code does is the
      paragraph above.
    - `SeccompFilterTests` (the BPF interpreter):
      `Signals_limits_and_scheduling_reach_the_workers_own_process_only`,
      `Setpriority_reaches_the_workers_own_process_only`,
      `Prctl_cannot_change_the_parent_death_signal`, `The_refused_calls_are_refused`, and
      `Every_call_the_filter_does_not_name_is_allowed_and_every_named_one_keeps_its_own_verdict`,
      for both architectures.
  - **Claims** (step 6). ARCHITECTURE.md's bullet, the remarks of `SkiaImageProcessor` and
    `PdfWorkerClient`, and both READMEs now say what the limits cover and what they leave open.
    "Never the host" and "no access to other processes" are gone.
  - **A residual risk wider than D-030 addendum 3 records**, found while checking the README's
    claims. Checked in sdk:10.0 as uid 1000 with no capability, `ptrace_scope` 1, under
    `RLIMIT_FSIZE` 0:
    - a worker can read `/proc/<pid>/environ` of a same-uid process that is not its descendant (the
      host's environment, connection strings included); `/proc/<pid>/mem` is refused;
    - an append dies of SIGXFSZ as intended, but truncating a file, deleting it, and overwriting it
      in place through a shared mapping all succeed.
    So a compromised worker can damage any file the host's account may write, the application's own
    files included where the account owns them, not only read. The README now says so and tells
    operators to run the host as a user that cannot write its application files. Closing it needs
    the filesystem sandbox already tracked as PC-F11 (Landlock), or a seccomp refusal of write-opens,
    `truncate`, `unlink` and `rename`, which I did not add unruled because a native library that
    opens a scratch file would then break.

- R1-2: fixed in 7ea007f, by fix 1, with the rule of fix 2 on top, as D-030
  addendum 5 rules.
  - **Token.** A Windows worker is created on a restricted, low-integrity copy of the host's token:
    `CreateRestrictedToken(DISABLE_MAX_PRIVILEGE)`, `SetTokenInformation(TokenIntegrityLevel,
    S-1-16-4096)`, `CreateProcessAsUserW`; still suspended, with the three-handle list, and in its
    job before it runs.
  - **What it closes.** `A_worker_cannot_open_its_hosts_process_to_read_or_change_it` has the
    stand-in call `OpenProcess` on the host for each of `PROCESS_VM_READ`, `VM_WRITE`,
    `VM_OPERATION`, `CREATE_THREAD`, `DUP_HANDLE`, `SET_INFORMATION` and `TERMINATE`. Only
    `TERMINATE` is granted. `A_file_write_by_a_worker_is_refused_on_windows_and_ends_the_worker_on_linux`
    shows a write into a folder of the host's refused.
  - **What it leaves**, as the ruling says: a worker can still end its host, and can open other
    low-integrity processes of the account, other workers among them. So a host on Windows renders
    PDF thumbnails only in the Development environment (`PdfThumbnailPolicy`). Outside it the
    processor returns no thumbnail for a PDF, starts no worker and counts no failure, and
    `core.imaging` logs `BlobPdfThumbnailsDisabled` once, at Warning, and probes no worker.
    `A_production_host_renders_pdf_thumbnails_on_linux_only_and_says_so_elsewhere` runs the smoke
    host in Production and sees exactly that.
  - **The unverified point is verified.** Pagefile-backed sections are not charged to the job's
    process memory limit: a scratch stand-in mapped and touched 768 MiB of them under a 128 MiB
    limit. The experiment is not in the suite (it was reverted); the README records the number.
  - **Not verified:** the token under IIS or App Service. Windows production hosts render no PDF
    thumbnails, so nothing depends on it.

- R1-3: fixed in 7ea007f. `Tellma:Blobs:PdfWorkerConcurrency`, default
  max(1, `ProcessorCount` / 4) as D-030 addendum 4 rules, bounds PDF workers with slots of their
  own, taken instead of an image slot. A PDF that finds every slot taken waits within its own
  budget and gets no thumbnail when that runs out (counted `timeout`); it never fails an upload.
  The option is validated with the others, by the validator and by `core.imaging`. Tests:
  `Image_work_never_waits_behind_pdf_workers`,
  `A_pdf_still_waiting_for_a_slot_when_the_budget_runs_out_yields_null_and_starts_no_worker`,
  `An_infinite_budget_waits_for_a_slot_and_then_renders`,
  `Concurrent_pdf_thumbnails_run_in_separate_workers_bounded_by_the_slots`, and the binding, default
  and validation cases in `ImagingCompositionTests`.

- R1-4: fixed in 7ea007f, both cases, and case 2 by more than a rewording.
  - **Case 1.** Every worker has a CPU limit: the budget plus a second, at most 60 s, and 60 s for
    a call without a budget (`SkiaImageProcessor.CpuLimit`; `WorkerLimits.CpuTime` is no longer
    nullable). Tests: `A_worker_always_has_a_cpu_limit_of_its_budget_and_a_second_at_most_a_minute`,
    `A_worker_of_an_infinite_budget_still_has_a_cpu_limit_of_a_minute`.
  - **Case 2.** On Linux the worker asks the kernel for SIGKILL when its host dies
    (`PR_SET_PDEATHSIG`), then checks that its parent is the host named by the new `--host-pid`
    argument and stops if it is not, which closes the window in which the host died first. The
    filter refuses the prctl that would withdraw the request. Orphan cleanup therefore no longer
    relies on the worker's cooperation. `A_worker_dies_with_its_host` now uses a stand-in that
    ignores its input: the suite kills the smoke host and the worker is gone, on both operating
    systems.
  - **One consequence to know.** The kernel sends that signal when the host *thread* that started
    the worker ends, not only when the process does. The processor starts each worker on a thread
    that lives exactly as long as the worker's call, and the callers' continuations no longer run
    on that thread (`RunContinuationsAsynchronously`, in the processor and in `core.imaging`). Two
    tests that start a worker themselves were made synchronous for the same reason; one of them
    had failed once in a full Linux run (the worker killed after 0.03 s).

- R1-5: fixed in 7ea007f, by the first option. The host's pipe ends are
  overlapped, and every read and write on them goes through `KillableStream`, which a kill cancels.
  A call therefore ends with its worker even while another process holds the worker's output open.
  `A_killed_worker_ends_its_call_even_while_another_process_holds_its_output_open` duplicates the
  worker's output handle, as a child started at the wrong moment would hold it, and sees the call
  end at the kill. The window itself remains (the child's pipe ends must be inheritable for
  `CreateProcess`); what it costs now is one late answer, never a held slot.

- R1-6: fixed in 7ea007f. The stand-in's `early-thread` mode starts a thread
  before `WorkerSandbox.Enter` and runs the socket and process probes on it once the filter is in
  place; `The_filter_covers_threads_that_existed_before_it_on_linux` asserts both refused. The
  finding's mutation, checked in sdk:10.0: with the thread-synchronising flag set to 0, the early
  thread reports "socket created, process started" and this test fails, while the two main-thread
  probes still pass.

#### D-030 addendum 5, item by item

1. Windows workers run under the restricted low-integrity token, and Windows hosts render PDF
   thumbnails only in Development: R1-2.
2. The heap hard limit is set on Linux only:
   `On_linux_the_managed_heap_is_capped_at_the_memory_limit_and_its_address_range_is_left_to_the_runtime`,
   `On_windows_the_managed_heap_is_left_to_the_job_objects_memory_limit`.
3. `RLIMIT_AS` = VmSize + limit + measured slack: R1-1.
4. `core.imaging` logs `BlobPdfWorkerPtraceUnrestricted` at Warning when `kernel.yama.ptrace_scope`
   is 0, and also when Yama is absent or its setting cannot be read, which leave the host's memory
   as open as 0 does
   (`On_linux_the_startup_check_warns_when_the_kernel_lets_a_worker_open_the_hosts_memory`).
5. A Linux host whose C library is not glibc renders no PDF thumbnails (`DisabledOnMusl`). It is
   detected as "not glibc": only glibc exports `gnu_get_libc_version`. **Not exercised on a real
   musl host:** no Alpine image is on this machine and I pulled none; the decision table and the
   disabled path are covered by `PdfThumbnailPolicyTests` and the composition tests.

#### Also changed, beyond the findings

- **macOS follows Windows**: a macOS host renders PDF thumbnails only in Development, because a
  macOS worker runs under no operating-system limit at all. The ruling names Windows only.
- **A crashed worker on Windows, and the suite's reliance on it.** A real crash on Windows goes to
  Windows Error Reporting, which keeps the dying process and its pipes until its report is written:
  on this machine 6 to 8 s in one run of the suite and 18 to 30 s in two later runs under heavier
  load (Application event log, events 1000 and 1001). The suite's crash cases depended on that time,
  and the startup-check case failed twice at the check's 30 s probe budget. Now the stand-in's
  `crash-` modes end the process at once on Windows, with an access violation's exit code, and
  still crash for real on Linux. One Windows-only test,
  `A_crashed_worker_that_windows_error_reporting_still_holds_is_killed_at_the_budget`, keeps the
  real crash and accepts either order of exit and budget. The product's own remarks said a crash
  "ends the process at once"; they and the README now say what happens: such a worker's call can
  end at its budget instead, counted as `timeout`.
- **The smoke host** gives its honest pages a 30 s budget and keeps the platform's 5 s for the
  hostile one: under load a worker's start alone outlasted 5 s once (5,427 ms), which is not what
  that host is there to show.
- **Zombies**: the suite's "is the process gone" check counts a zombie as gone on Linux, since a
  container whose PID 1 reaps nothing keeps a killed orphan's entry.

#### Deviations (also in the commit message)

9. `Tellma:Blobs:PdfWorkerConcurrency` (D-030 addendum 4) is not in spec 0017 §1.3 or §3.5.
10. PDF thumbnails render on Linux with glibc only; on Windows and macOS only in Development; on
    musl never (D-030 addendum 5, extended to macOS). `SkiaImageProcessor`'s public constructor
    gains an optional `IHostEnvironment? environment`, and `Tellma.Core.Imaging` references
    `Microsoft.Extensions.Hosting.Abstractions`. Spec 0017 says nothing of this.
11. Two log events beside §10.2's: `BlobPdfThumbnailsDisabled` and `BlobPdfWorkerPtraceUnrestricted`,
    both Warning, each at most once per host start.
12. The filter refuses more than addendum 4 lists (R1-1), and the Yama warning covers an absent or
    unreadable setting as well as 0.
13. `DOTNET_GCRegionRange` is not set (R1-1): the reviewer's "or both" crashed the collector.
14. Every PDF worker has a CPU limit of at most 60 s, a call without a budget included (R1-4), and
    the worker takes a `--host-pid` argument, required on Linux.

#### For the orchestrator

- **Address space (R1-1).** Exploit code in a worker can hold about 3.55 GiB of shared memory at
  the default limit until its worker is killed. Accept and record it, or rule one of: a re-executing
  worker under a soft `RLIMIT_AS` with `DOTNET_InitialExecMemoryPercent` (about a quarter more
  latency per PDF), or a memory cgroup per worker (PC-F11's neighbourhood).
- **Filesystem (R1-1).** D-030 addendum 3 records reads only. A compromised worker can also
  overwrite in place, truncate, rename and delete what the host's account may write. PC-F11 should
  cover writes, and the deployment guidance (the host cannot write its own files) should reach the
  hosting spec.
- **Spec 0017** carries neither `PdfWorkerConcurrency` nor the host rule for PDF thumbnails;
  `docs/` is not mine to edit.
- **musl** is detected but was not exercised on a real musl host.
- **macOS** is treated as Windows is (Development only); the ruling names Windows.

#### Verification of the fixes

All of it on the final tree. The machine was shared with other slots throughout, and often loaded.

- **Gate on Windows** (PLAN.md §7.1 items 2-4): `dotnet build Tellma.slnx` printed "Build
  succeeded" with 0 warnings; `dotnet format Tellma.slnx --verify-no-changes` is clean (its output
  read, not its exit code: it printed nothing, and it changed no file); the PR-tier unit filter
  over `Tellma.slnx` is green in every assembly (16 assemblies, 4,111 passed, 18 skipped, 0 failed;
  the 6 integration-only assemblies match no test; Imaging 439 passed and 11 skipped of 450). The
  only test project whose reference closure includes a changed project is
  `Tellma.Core.Imaging.Tests`, so no integration suite is touched; no shared build file,
  `Tellma.Core.Abstractions` or `test/shared` file changed.
- **Imaging suite on Windows** (Debug): 450 tests, 439 passed, 11 skipped (the Linux-only probes), 0
  failed, 3.5 min. The CPU-limit probe ended after 2.4 s with `0xC0000044`; the input-closed worker
  exited after 0.05 s; native and managed allocation each stopped at 112 of 128 MiB.
- **`mcr.microsoft.com/dotnet/sdk:10.0`** (Ubuntu 24.04, glibc 2.39, SDK 10.0.400, Yama 1, uid 0),
  from the slot's tree, Release: 450 tests, 448 passed, 2 skipped (the two Windows-only tests), 0
  failed, 1.8 min.
  - Probes: native and managed allocation each stopped at 112 of 128 MiB; shared mappings at 240 MiB
    under 128 MiB (limit + slack); the CPU-limit probe ended after 2.0 s with 137; the input-closed
    worker exited after 0.01 s; the fan-out worker grew 255 MiB under 256 MiB.
  - Footprint: ready 3,328,784 kB of address space and 32,020 kB of data; 17,372 kB outside the data
    limit after a render.
  - Smoke host, published `-r linux-x64` and run from the output. Production:
    `pdf.thumbnails=Enabled`, honest page 130 bytes, hostile page none after 5,029 ms, host memory
    +2 MiB, PDFium never loaded, `result=held`, `--measure 10` min 344 / median 648 / max 894 ms
    (the Windows solution build ran beside it; a quieter run earlier in the round measured min 191 /
    median 214 / max 366 ms). Development: the same, `result=held`.
  - Packages: the four packages packed to a local feed and consumed by a scratch application
    through `PackageReference Tellma.Core.Imaging`, published `-r linux-x64` and run from the
    output: check passed, thumbnail rendered (74 bytes), PDFium not in the host. The Imaging nuspec
    lists `Microsoft.Extensions.Hosting.Abstractions`.
- **`mcr.microsoft.com/dotnet/sdk:10.0-resolute`** (Ubuntu 26.04, glibc 2.43, SDK 10.0.403, Yama 1,
  uid 0): 450 tests, 448 passed, 2 skipped, 0 failed, 1.7 min. Footprint: ready 3,329,276 kB and
  32,036 kB; 17,368 kB outside the data limit. Smoke host from the `-r linux-x64` publish, in
  Production and in Development: `result=held` (`--measure 10` min 361 / median 693 / max 831 ms,
  beside other work).
- **Earlier in the round**, after the collector fix: three consecutive full runs of the suite in
  sdk:10.0, each 448 passed and 1 skipped of the 449 tests there were then.
- **Failures on the way, each understood and fixed:**
  - Linux: the managed-allocation probe died of SIGSEGV with `DOTNET_GCRegionRange` at twice the
    limit (the setting is gone).
  - Linux: the fan-out memory test lost its worker after 0.03 s once, to the parent-death signal of
    a borrowed thread (R1-4).
  - Linux: `A_worker_dies_with_its_host` in a container whose PID 1 reaps nothing (zombies).
  - Windows: the startup-check crash case twice, and the smoke host's honest page once, under heavy
    load (Windows Error Reporting; the 5 s budget).
- **Mutation check of R1-6** in sdk:10.0: with `SECCOMP_FILTER_FLAG_TSYNC` replaced by 0,
  `The_filter_covers_threads_that_existed_before_it_on_linux` fails ("socket created, process
  started") and the main-thread socket and process probes still pass.
- **Not verified:** a real musl host; the restricted token under IIS or App Service; arm64 (the
  filter's arm64 program is checked by the interpreter tests only, as before).
- No command was blocked by the permission system.

## Review 2

Reviewed commits da3714f..bfa9e4a together (`git diff da3714f^ bfa9e4a`, `git show --stat` per
commit, every changed file read in full) against D-030 with addenda 1-5, D-040, spec 0017 §1.1,
§3.5, §3.6, §10, the PC-053 CHECKPOINTS entry and WATCHLIST, PLAN.md §6-§7, and AGENTS.md. Review 1
and its responses were read first; I did not see the implementation discussion.

What I ran:
- `Tellma.Core.Imaging.Tests` on Windows, Debug, PR-tier filter: 439 passed, 11 skipped
  (the Linux-only probes), 0 failed, 450 total. Build succeeded, 0 warnings.
- The same suite in `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu 24.04, glibc 2.39, 10.0.11 runtime,
  `ptrace_scope` 1, uid 0), from a `git archive` of bfa9e4a, Release: 448 passed, 2 skipped (the two
  Windows-only tests), 0 failed. Both OS runs match the implementer's record.
- A safe, own-process observation in sdk:10.0 of what a .NET 10 host keeps open (see R2-1): a plain
  managed process holds `fd -> /memfd:doublemapper (deleted)`, mapped `rw-s` in `/proc/self/maps`,
  with `DOTNET_EnableWriteXorExecute` at its default (on). I ran no cross-process memory-access code.

Residual judgment (D-040):
- **(1) RLIMIT_AS (about 3.55 GiB shared memory per worker until it is killed): holds** as accepted,
  with the memory-cgroup deployment guidance. It is a bounded DoS reachable only after a worker is
  already compromised, and R2-1 below is a strictly worse consequence of the same precondition, so
  the address-space residual does not move the needle on its own.
- **(2) filesystem (writes as well as reads, PC-F11): holds**, but see R2-1: PC-F11 as scoped
  (Landlock over the app's own files) does not by itself close the `/proc` path R2-1 uses.
- **(3) macOS as Windows, (4) musl detected-not-exercised, (5) `DOTNET_GCRegionRange` unset: hold.**
- **Must escalate:** the host-memory vector. D-030 addendum 5 added the Yama warning to close it;
  R2-1 shows the warning's own premise is wrong, so the residual is mis-stated, not merely accepted.

What holds, checked against the change as it now stands:
- Limits are applied after the ready frame and before the request on both OSes
  (`PdfWorkerClient.Render`), so untrusted bytes are parsed only under the caps; the seccomp filter
  and the dropped capabilities are installed before the ready frame (`WorkerSandbox.Enter`).
- The IPC caps every length before allocating (`WorkerProtocol.ReadRequest`/`ReadResult`), bounds a
  PNG by `MaxImageBytes(size)` for the size the host itself chose, and `WorkerPng.Check` fixes the
  signature, a first `IHDR`, the dimensions, 8-bit RGB/RGBA, no interlace and the chunk set before
  Skia re-decodes at exactly those dimensions.
- Slots: PDF work takes its own `PdfWorkerConcurrency` slot, released only once the worker process
  is gone (`RunPdfInSlot`/`Render` finally), and image work never waits behind it (R1-3);
  `PdfWorkerBudgetTests` covers death-before-slot-return, cancellation, dispose, the CPU-limit math,
  the WER-held crash and zombies. The Linux `RLIMIT_DATA`/`RLIMIT_AS`/`RLIMIT_CPU` and the Windows
  job limits read back correctly; `PR_SET_PDEATHSIG` plus the `--host-pid` re-check and the stdin
  watchdog cover the orphan cases (R1-4).
- The host rule is right where it is enforced: Linux+glibc enabled, Windows/macOS Development-only,
  musl never (`PdfThumbnailPolicy`), and the launcher refuses single-file/self-contained hosts.
- AGENTS.md: XML docs and section comments are present; three new `src` projects and both test
  assets carry READMEs; no code, message, log or test name cites `docs/` or a spec. PLAN.md §7.1:
  build clean, format clean (per the record), the only test closure touched is the Imaging suite,
  which CI runs on both OSes (`ci.yml` build-test matrix). ARCHITECTURE.md is updated.

### Findings

**R2-1 (major): On Linux a compromised worker can write the host's executable memory by reopening the runtime's W^X "doublemapper" memfd through `/proc/<host-pid>/fd`. Neither the seccomp filter, the dropped capabilities, nor `kernel.yama.ptrace_scope` closes this path, and the startup check and ARCHITECTURE.md tell operators it does.**
- Premise: a PDFium memory-corruption exploit gives an attacker code execution inside the worker.
  That is the exact threat this item moved rendering out of process to contain, and the one the
  recorded residuals (D-030 addendum 3/5, the README, the implementer notes) assume.
- The host keeps the door open. A .NET host on Linux runs with Write-xor-Execute on by default and
  keeps a writable, shared `memfd` named `doublemapper` open for its whole life, as a file
  descriptor and as an `rw-s` mapping that aliases its executable code pages. I confirmed this on
  `sdk:10.0`: a plain managed process shows `fd -> /memfd:doublemapper (deleted)` and
  `... rw-s ... /memfd:doublemapper (deleted)` in its own `/proc/self/maps`.
- The worker can reach it. The worker shares the host's PID namespace (nothing creates a new one)
  and is told the host's pid (`--host-pid`, `WorkerSandbox.cs:44-45`; `getppid` also works). It can
  therefore `open("/proc/<host-pid>/fd/<n>", O_RDWR)` on that memfd and
  `mmap(MAP_SHARED, PROT_READ|PROT_WRITE)` it, then overwrite the host's JIT/precompiled code — full
  code execution in the host process: every tenant's data, the database connection strings, the
  app's secrets.
- Nothing in the sandbox stops it:
  - `SeccompFilter.cs:125-151` is a deny list; `open`, `openat`, `mmap`, `readlink` and `pread` are
    all allowed (the runtime needs them). The filter cannot inspect the path string.
  - `RLIMIT_FSIZE` 0 (`UnixWorkerProcess.cs:207`) limits only file-size growth, not writes through a
    shared mapping.
  - The dropped capabilities (`WorkerSandbox.DropCapabilities`) and `no_new_privs` are irrelevant:
    following `/proc/<pid>/fd` for a same-uid, dumpable target needs no capability.
  - `kernel.yama.ptrace_scope` gates only `PTRACE_MODE_ATTACH` (and `/proc/<pid>/mem`), not the
    `PTRACE_MODE_READ` check that guards `/proc/<pid>/fd` reopening. So `ptrace_scope` 1 — the value
    in both test containers, and the value `ImagingStartupCheck.UnrestrictedPtrace` treats as safe —
    does not block it.
- It is untested and the suite would stay green: the stand-in's only host-memory probe opens
  `/proc/<host>/mem` (`test/core/assets/Tellma.Core.Imaging.TestWorker/Program.cs:438-446`), which is
  `ATTACH`-gated and refused; nothing exercises a `/proc/<host>/fd` reopen.
- The guidance is actively wrong, not merely incomplete:
  - `src/core/Tellma.Core.Imaging/ImagingLog.cs:110-114` tells the operator a worker "could read and
    write this host's memory" only when `ptrace_scope` is 0, and to "Set the kernel.yama.ptrace_scope
    sysctl to 1 or higher" as the fix.
  - `ARCHITECTURE.md:1212`: "the host's memory is closed to it only where `kernel.yama.ptrace_scope`
    is 1 or more".
  - D-030 addendum 5 added this warning specifically to close the host-memory vector; it does not.
  - PC-F11 as scoped (Landlock over the app's own files, D-040 item 2) would not close it either
    unless it explicitly denies opening under `/proc`.
- Why major, and why it must be escalated: it defeats the item's central goal ("never the host") on
  the one supported production OS, under the configuration the system blesses, and the planned
  follow-up does not address it. I did not rate it blocking because the orchestrator has already
  accepted that a PDFium RCE is "not fully contained" on Linux until later confinement work
  (D-030 addendum 3), and host code execution is the extreme of that accepted residual; but the
  residual as written claims the opposite of what holds, so this needs an orchestrator ruling.
- Fix (any one closes it; the first is cheapest):
  1. Make the host non-dumpable before it starts any worker: `prctl(PR_SET_DUMPABLE, 0)` on the host
     process at startup. With the worker's already-empty capabilities, a non-dumpable host's `/proc`
     entries become inaccessible to the worker even when both run as root, closing the memfd reopen
     **and** the `/proc/<host>/environ` secret read (R2-2) in one step. It does not affect the host's
     own reads of the worker's `/proc` or its `prlimit` calls on the worker.
  2. Or run the worker under a distinct uid, or in its own PID namespace.
  3. Correct `ImagingLog.PtraceUnrestricted`, `ImagingStartupCheck` and `ARCHITECTURE.md:1212` to
     state that `ptrace_scope` raises the bar for `PTRACE_ATTACH`/`/proc/<pid>/mem` only, not for
     same-uid `/proc` reads or the memfd reopen; and make PC-F11's scope explicitly cover `/proc`.
  4. Add a stand-in probe that reopens a descriptor from `/proc/<host>/fd` and asserts it is refused.

**R2-2 (minor): The ptrace-scope guidance overstates what it buys even for reads; the host's environment (its secrets) is readable at `ptrace_scope` 1.**
- Where: `src/core/Tellma.Core.Imaging/ImagingLog.cs:110-114`,
  `src/core/Tellma.Core.Imaging/ImagingStartupCheck.cs:118-139`, `ARCHITECTURE.md:1212`.
- The implementer's own R1-1 record measured `/proc/<host>/environ` (the host's environment, its
  connection strings among them) readable by a same-uid worker at `ptrace_scope` 1, because Yama
  gates only `ATTACH`-mode access. Yet the shipped warning and ARCHITECTURE.md present
  `ptrace_scope >= 1` as closing host-memory read and write. An operator who sets it to 1 and reads
  "the host's memory is closed" is misled: the worker can still read host secrets (environ) and,
  per R2-1, write host code.
- This is the same root cause as R2-1 and is fixed by R2-1's step 3 (and closed outright by step 1);
  it is filed separately so the documentation correction is not lost if R2-1's vulnerability is
  accepted as a residual rather than fixed now.

### Responses

The orchestrator ruled on R2-1 before this round (D-041): close the vector now, by the host
making itself non-dumpable unless verification shows that does not close both paths; verify
first, on fixtures only; measure and document the cost. This round does that, and closes four
further ways in that the verification turned up.

- R2-1: fixed in e123782. The finding is right for a host that runs as an unprivileged user or
  as root without capabilities. A root host that keeps its capabilities was
  closed to this one path already, though not to the others below.
  - **Verified first, before and after.** Every target was a fixture of mine: a lab program that
    plays both roles, and then the suite's own test host with the stand-in worker under a worker's
    real limits (no capability, the seccomp filter, the `RLIMIT_*` limits). No code read or wrote
    another process's memory: a probe opens a file and closes it again, untouched.
    - In `mcr.microsoft.com/dotnet/sdk:10.0` (kernel 6.18.54 under WSL2, `ptrace_scope` 1) the
      stand-in opened, for reading and writing, `/proc/<host>/fd/<n>` for a file the host holds
      open and for the descriptor its runtime writes compiled code through
      (`/memfd:doublemapper`, descriptor 8 in every run), then the host's `environ`, `mem` and
      `maps`. "Before" is the final tree with this round's fixes taken out:

      | Host | Before | After |
      |---|---|---|
      | uid 1654, no capability | `fd120=opened,fd8=opened,environ=opened,mem=EACCES,maps=opened` | `fd151=EACCES,fd8=EACCES,environ=EACCES,mem=EACCES,maps=EACCES` |
      | uid 0, `--cap-drop ALL` | `fd117=opened,fd8=opened,environ=opened,mem=EACCES,maps=opened` | `fd137=EACCES,fd8=EACCES,environ=EACCES,mem=EACCES,maps=EACCES` |
      | uid 0, Docker's default capabilities | refused already: the test passes | `fd137=EACCES,fd8=EACCES,environ=EACCES,mem=EACCES,maps=EACCES` |

    - The lab program agrees (a bare process with empty capability sets and `no_new_privs`
      against a host of its account). Before, `open(/proc/<host>/fd/8, O_RDWR)` succeeded and the
      environment read back a planted variable, as uid 1654 and as uid 0 with an empty bounding
      set. After `prctl(PR_SET_DUMPABLE, 0)` in the host each answered `EACCES`, and `maps` and
      `cwd` with them. `mem` was `EACCES` throughout, which is all that `ptrace_scope` 1 buys.
    - A root host that keeps its capabilities is closed because the kernel also asks that the
      caller hold every capability its target holds, and a worker holds none.
  - **The fix (step 1).** `HostIsolation.CloseToWorkers()` is the first thing
    `UnixWorkerProcess.Start` does on Linux: before every worker start, the startup check's own
    included, in every environment.
    - It makes the host non-dumpable, reads the flag back, and throws a `Start` failure when the
      kernel did not comply: no worker starts beside an open host.
    - It needs no privilege, and it holds for a host of uid 0: a process that is not dumpable is
      closed to every process without `CAP_SYS_PTRACE`, and a worker has dropped every capability.
    - It runs on every start, not once. It costs a few system calls and a listing of the
      temporary directory, and the debugger pipes below can come back.
  - **The probes (step 4).**
    - The stand-in's `proc-files` mode, and
      `A_worker_cannot_open_its_hosts_descriptors_environment_memory_or_map_on_linux`, which
      asserts a refusal for each file in the table above.
    - `A_worker_cannot_open_its_hosts_process_to_read_or_change_it` no longer skips where
      `ptrace_scope` is 0 or absent: the host's memory is closed by the host now.
    - `HostIsolationTests.Every_worker_start_closes_the_host_to_its_own_account_again_on_linux`
      makes the test host dumpable again and plants a pipe-shaped file, starts a worker, and
      asserts the host is closed again and the file gone.
    - The smoke host, a published host as a deployment runs one, reports `host.dumpable=no` and
      `host.debugger-pipes=0`, or its test fails.
  - **The guidance (step 3)** is R2-2. Fix 2 (another uid, or a PID namespace) was not needed.
  - **Four more ways in, found while verifying, each closed in this round.** The first three
    are the finding's own threat (code running in a worker reaches its host),
    `PR_SET_DUMPABLE` closes none of them, and a root host with capabilities is open to them
    too.
    1. *The runtime's debugger pipes.* A .NET process listens for a managed debugger on two
       FIFOs, `/tmp/clr-debug-pipe-<pid>-<start time>-in` and `-out`, mode 0600 under its own
       account.
       - A process of that account opens them like any file. In the lab,
         `open(<pipe>-in, O_WRONLY)` succeeded for a process without capabilities against hosts
         of uid 1654 and of uid 0 with capabilities. The protocol behind them reads and writes
         the debuggee's memory by design; I opened a pipe, wrote nothing, and did not exercise
         the protocol.
       - The host now removes its own pipes before every worker start. Beside a pipe it cannot
         remove but can still open it starts no worker, and the failure names the pipe and
         `DOTNET_EnableDiagnostics_Debugger=0`.
       - Before, the probe lists both pipes (`host-debugger-pipes:
         clr-debug-pipe-504-107440445-…`); after, `host-debugger-pipes: none`
         (`A_worker_finds_no_debugger_pipe_of_its_host_on_linux`). The smoke host reports
         `host.debugger-pipes-at-start=2`, which shows that the runtime names its pipes as the
         host expects, and then `host.debugger-pipes=0`.
    2. *Signals the kernel sends on a worker's word.* Review 1's filter confines `kill` and its
       relatives to the worker's own pid. But `fcntl(F_SETOWN)` names any process as a
       descriptor's owner, and with `O_ASYNC` the kernel signals that owner whenever the
       descriptor is ready, asking only that it be of the caller's account.
       - In the lab a process without capabilities named the host as the owner of a pipe of its
         own, asked for signal-driven I/O and wrote a byte: the host received `SIGIO`, whose
         default action ends a process.
       - A terminal needs no naming, because its owner is its foreground job. A child that only
         set `O_ASYNC` on the terminal its host was started from had the host killed at the next
         keystroke (exit 157), from a session of its own too.
       - The filter now refuses `fcntl` with `F_SETOWN`, `F_SETOWN_EX`, `F_SETSIG`, and `F_SETFL`
         with `O_ASYNC`; and `ioctl` with `FIOASYNC`, `FIOSETOWN` and `SIOCSPGRP`.
    3. *The host's controlling terminal.* A worker of a host started from a terminal shared its
       session: it opened `/dev/tty` and changed the terminal's foreground job (`TIOCSPGRP`
       succeeded in the lab).
       - On kernels that still allow it, `TIOCSTI` pushes input into the terminal for the
         host's shell to run. This kernel answers `EIO`, so that one was not reproduced.
       - The worker now calls `setsid` before anything else of its sandbox, and the filter
         refuses `TIOCSTI` and `TIOCLINUX` outright.
       - Under a pseudo-terminal, before: `session: inherited,terminal=34816,dev-tty=opened`;
         after: `session: own,terminal=0,dev-tty=errno6`
         (`A_worker_leads_a_session_of_its_own_without_a_controlling_terminal_on_linux`).
    4. *Another worker of the same host.* Workers were dumpable, so a compromised worker could
       open a concurrent worker's `/proc/<pid>/fd/0`, the pipe its upload arrives on (another
       tenant's document, perhaps), and its output, where its thumbnail leaves.
       - Before, for all three kinds of host:
         `fd0=opened,fd1=opened,fd2=opened,environ=opened,mem=EACCES,maps=opened`. After: each
         `EACCES` (`A_worker_cannot_open_another_worker_of_its_host_on_linux`).
       - A worker now makes itself non-dumpable in its sandbox, before it drops its
         capabilities: until then those keep a worker without any out of it.
  - **What non-dumpable costs (measured, as ruled).** In the lab: a host that fails fast, and a
    host asked for a dump and a trace over its diagnostics port; as uid 1654, as uid 0, and as
    uid 0 in a container started with `--cap-add SYS_PTRACE`.
    - The kernel's core dump: none. The ruling accepts that.
    - The runtime's crash dump (`DOTNET_DbgEnableMiniDump=1`): 6.7 MB before. After,
      `createdump` reports `open(/proc/<pid>/mem) FAILED Permission denied (13)` and writes
      nothing, unless the container holds `CAP_SYS_PTRACE`, where the dump is written as before.
    - `dotnet-dump collect` (the port's dump command): `hresult 0` and a 6.9 MB file before;
      `0x80004005` and no file after; with `CAP_SYS_PTRACE`, as before.
    - `dotnet-counters` and `dotnet-trace` (the port's process-information and tracing
      commands): unchanged. A 19 kB nettrace stream came back in every case. The port is a
      socket, which a worker cannot create, so the host leaves it alone.
    - A managed debugger cannot attach once a worker has started, because the pipes are gone.
      That is the price of item 1 above, not of the ruling, and the first point for the
      orchestrator.
    - A host that removed its pipes still exits normally, and ends on `SIGTERM` as before.
    - I ran none of the three tools, nor a managed debugger: my session may not download
      anything without the user's permission, so the lab program spoke the port's protocol.
    - The README carries all of this as a table for operators, and ARCHITECTURE.md as a sentence.
  - **What it leaves.** Each is in the README's residual risks, and the first four are points
    for the orchestrator.
    - *The account's other processes.* The host closes itself and each worker closes itself; any
      other process of the same account does neither, and a worker can read its environment and
      reopen its files at any `ptrace_scope`.
    - *A worker while it starts.* The kernel makes every newly started program dumpable. From
      its start until it enters its sandbox, a worker of an unprivileged host can be opened by a
      compromised worker that runs at that moment, its pipes included. A root host with
      capabilities has no such moment. No cheap fix closes this one (below).
    - *A root host.* A worker of uid 0 without capabilities can still raise its host's
      `/proc/<pid>/oom_score_adj` (measured, before and after), and reach root's files. For an
      unprivileged host the fix closes that file as well.
    - *Files.* A terminal the host was started from can still be opened by its path, read and
      written, though not controlled or made to signal; the host's command line is
      world-readable, as every process's is.
    - The filesystem residual and the address-space residual of D-040 stand as they were.

- R2-2: fixed in e123782. Nothing claims any longer that `ptrace_scope` closes the host's
  memory, or anything else of the host: the host does that itself now.
  - `ImagingLog.PtraceUnrestricted` (`BlobPdfWorkerPtraceUnrestricted`) says that the host closes
    itself to its workers, that at scope 0 a worker could read and write the memory of any
    *other* process of the host's account, and that even at scope 1 or more it can read those
    processes' environment and open files, "so run nothing else under the host's account".
  - `ImagingStartupCheck`, its remarks and its section comment say the same: the scope closes
    memory alone, and the host needs no such setting.
  - ARCHITECTURE.md's bullet drops "the host's memory is closed to it only where
    `kernel.yama.ptrace_scope` is 1 or more". It states what the host closes of itself and what
    that costs, and records the account's other processes as the residual.
  - The README's "What a Linux host closes of itself" says of the two paths: "`ptrace_scope`
    guards neither: it covers `ptrace` and `/proc/<pid>/mem` only".
  - `ImagingCompositionTests` asserts the new wording.

#### Also changed, beyond the findings

- **`UnixWorkerProcess.InheritableDescriptors`** lists `/proc/self/fd` and asks `fcntl(F_GETFD)`
  where it read `/proc/self/fdinfo`. Kernels before about 5.14 give a non-dumpable process's
  `fdinfo` to root and refuse an unprivileged process its own, which would have failed every
  worker start there. This is from the kernel's history and was not reproduced: kernel 6.18
  allows both.
- **The inheritance test** no longer looks into a Linux worker's descriptors from the test host,
  which a non-dumpable worker refuses: the stand-in's `descriptors` mode looks through its own.
  `InheritableMarker.IsHeldBy` is Windows-only, and the test skips on macOS, where it could never
  have run.
- **`HostIsolation.RemoveDebuggerPipes`** takes the paths, so that its refusal is tested on a
  folder of the test's own and no other suite's worker meets a pipe that cannot be removed.

#### Deviations (also in the commit message)

15. A Linux host makes itself non-dumpable (D-041) and removes its runtime's debugger pipes (mine)
    before every worker start, in every environment. It leaves no core dump, a crash dump or
    `dotnet-dump collect` of it needs `CAP_SYS_PTRACE`, and no managed debugger can attach once
    a worker has started. Spec 0017 says nothing of this.
16. A Linux worker leaves its host's session and makes itself non-dumpable, and its filter
    refuses kernel-sent signals (`fcntl` owner, signal and `O_ASYNC`; `ioctl` `FIOASYNC`,
    `FIOSETOWN`, `SIOCSPGRP`) and terminal input (`TIOCSTI`, `TIOCLINUX`): more than D-030
    addendum 4 and D-041 list.
17. `BlobPdfWorkerPtraceUnrestricted` now warns about the other processes of the host's account,
    not about the host (R2-2).

#### For the orchestrator

- **The debugger pipes (deviation 15).** Removing them is my addition to the ruling, and it is
  what makes a managed debugger unable to attach to a Linux host once a worker has started,
  Development included; a debugger that started the host, or attached earlier, stays attached
  (the runtime's design; not exercised). The alternatives: exempt Development, which leaves a
  Development host open to its workers through the pipes; or require
  `DOTNET_EnableDiagnostics_Debugger=0` of every Linux host and fail the startup check without
  it, which costs the same attach and a deployment setting besides. I recommend keeping the
  removal: a pipe that reads and writes host memory is the finding's own harm by another door.
- **A worker while it starts.** Closing it needs either a filesystem sandbox that denies `/proc`
  (PC-F11) or sockets in place of the worker's pipes, which `/proc` cannot reopen; the second is
  a protocol-transport change of a day or so. Accept until PC-F11, or rule the sockets.
- **PC-F11's scope** should deny a worker `/proc` beyond its own entries and the temporary
  directory, as the review asks. That closes the account's other processes, the starting worker,
  the terminal by its path, and any debugger pipe of another .NET process of the account.
- **Deployment guidance for the hosting spec:** run the host as an unprivileged user (a root
  host lends its worker `oom_score_adj` and root's files), with nothing else under its account,
  as a service and not from a terminal; grant `CAP_SYS_PTRACE` only where crash dumps are wanted.
- **Spec 0017** records none of deviations 15-17; `docs/` is not mine to edit.
- **The diagnostics tools were not run** (no downloads in my session). If operators rely on
  `dotnet-dump`, `dotnet-counters` or `dotnet-trace`, a run of the real tools against a staging
  host would confirm what the protocol showed.

#### Verification of the fixes

All of it on the final tree. The container runs built from an archive of the working tree, which
differs from the commit in one phrase of ARCHITECTURE.md and in this file, neither of which a
build reads. The machine was shared with other slots throughout.

- **Gate on Windows** (PLAN.md §7.1 items 2-4): `dotnet build Tellma.slnx` printed "Build
  succeeded" with 0 warnings; `dotnet format Tellma.slnx --verify-no-changes` is clean (its output
  read, not its exit code: it printed nothing, and it changed no file); the PR-tier unit filter
  over `Tellma.slnx` is green in every assembly (16 assemblies, 4,115 passed, 24 skipped, 0 failed;
  the 6 integration-only assemblies match no test; Imaging 443 passed and 17 skipped of 460). The
  only test project whose reference closure includes a changed project is
  `Tellma.Core.Imaging.Tests`, so no integration suite is touched; no shared build file,
  `Tellma.Core.Abstractions` or `test/shared` file changed.
- **Imaging suite on Windows** (Debug): 460 tests, 443 passed, 17 skipped (the Linux-only probes,
  this round's six among them), 0 failed, 1.2 min. The CPU-limit probe ended after 3.1 s with
  `0xC0000044`; the input-closed worker exited after 0.04 s; native and managed allocation each
  stopped at 112 of 128 MiB.
- **`mcr.microsoft.com/dotnet/sdk:10.0`** (Ubuntu 24.04, glibc 2.39, SDK 10.0.400, kernel 6.18.54,
  Yama 1), from the slot's tree, Release, the whole suite under the PR-tier filter, for three
  kinds of host: uid 0 with Docker's default capabilities, uid 1654 without any (`--user
  1654:1654`), and uid 0 with `--cap-drop ALL`. Each: 460 tests, 458 passed, 2 skipped (the two
  Windows-only tests), 0 failed.
  - Probes, the same in each: the host's and another worker's files under `/proc` all `EACCES`;
    `session: own,terminal=0,dev-tty=errno6`; native and managed allocation at 112 of 128 MiB;
    shared mappings at 240 MiB under 128 MiB; the CPU-limit probe ended after 2.0 s with 137; the
    input-closed worker exited after 0.02 s.
  - Footprint, unchanged: ready 3,328,940 kB of address space and 32,172 kB of data; 17,376 kB
    outside the data limit after a render.
  - Smoke host, published `-r linux-x64` and run from the output, in Production and in
    Development, in each of the three: `host.debugger-pipes-at-start=2`, `pdf.thumbnails=Enabled`,
    honest page 130 bytes, hostile page none, PDFium never loaded, `host.dumpable=no`,
    `host.debugger-pipes=0`, `result=held`.
  - Start cost, `--measure 10`, two containers side by side each time: root min 127 / median 149
    / max 205 ms; uid 1654 min 193 / median 249 / max 279 ms; root without capabilities min 207 /
    median 293 / max 333 ms. Review 1's quiet run measured a median of 214 ms: closing the host
    adds nothing that shows.
  - Packages (the root run): the four packages packed to a local feed and consumed by a scratch
    application through `PackageReference Tellma.Core.Imaging`, published `-r linux-x64` and run
    from the output: check passed, thumbnail rendered (74 bytes), PDFium not in the host.
- **`mcr.microsoft.com/dotnet/sdk:10.0-resolute`** (Ubuntu 26.04, glibc 2.43, SDK 10.0.403, Yama 1,
  uid 0): 460 tests, 458 passed, 2 skipped, 0 failed. Footprint: ready 3,329,408 kB and 32,164 kB;
  17,372 kB outside the data limit. Smoke host from the `-r linux-x64` publish, in Production and
  in Development: `result=held`, not dumpable, no pipe (`--measure 10` min 263 / median 339 / max
  441 ms, beside the other run).
- **Before the fixes** (the mutation check), in sdk:10.0: the final tree with
  `HostIsolation.CloseToWorkers()`, `LeaveSession()` and `CloseToAccount()` taken out and the
  filter's `fcntl` and `ioctl` blocks matching no call; `WorkerSandboxTests`,
  `HostIsolationTests`, `SmokeHostTests` and `SeccompFilterTests`, 128 tests.
  - uid 1654: 12 failed. uid 0 with `--cap-drop ALL`: 12 failed. uid 0 with default capabilities:
    11 failed (the host's `/proc` test passes there, as the table under R2-1 says).
  - What failed: both new filter theories on both architectures; both smoke-host tests
    (`host.dumpable=yes`, `host.debugger-pipes=2`, `result=failed`);
    `A_worker_cannot_act_on_its_host_or_create_sysv_objects_on_linux` (the four `fcntl` calls and
    `FIOASYNC` "allowed"; the other four `ioctl` requests reached the kernel, which answered
    `ENOTTY` for a pipe); the tests of another worker, the debugger pipes and the session;
    `Every_worker_start_closes_the_host_to_its_own_account_again_on_linux`; and, except for root
    with capabilities, the host's descriptors.
  - Under a pseudo-terminal (`script`), the session and host-access tests: both failed before
    (`session: inherited,terminal=34816,dev-tty=opened`) and both pass after.
- **Failures on the way, each understood and fixed:**
  - The first mutation run did not fail the host-isolation test: another test of the run had
    closed the test host already. The test now opens the host again first, so it stands alone.
  - A session of its own did not stop the terminal's `O_ASYNC` in the lab (item 2 under R2-1);
    the filter does.
  - One build broke on an unused `using` in `InheritableMarker.cs` (IDE0005).
- **Not verified:**
  - `dotnet-dump`, `dotnet-counters`, `dotnet-trace` and a managed debugger themselves (the
    diagnostics port's protocol was spoken instead; the debugger pipes were opened, not spoken to).
  - `ptrace_scope` 0, a machine setting that is not mine to change. The host's closing rests on
    the kernel's dumpable check, which does not ask Yama.
  - Kernels before 5.14 (the `fdinfo` change) and kernels that still allow `TIOCSTI`.
  - A real musl host and arm64 (the filter's arm64 program is checked by the interpreter tests
    only), as before.
- **One command was blocked by the permission system** and not retried: a lab script run through
  `docker exec <lab container> bash -c '…'` whose loop began with `rm -f /tmp/crash-*.dmp`. The
  built-in safety check could not resolve the removal's target. The measurement was run again
  with a file name of its own per run and no removal.
- The lab's containers are removed; nothing of this round is left running.
