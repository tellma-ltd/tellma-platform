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
