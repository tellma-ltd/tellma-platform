# Tellma.Core.Imaging

The platform's image processor: [SkiaImageProcessor](SkiaImageProcessor.cs) implements
`IImageProcessor` on SkiaSharp, and renders PDF first-page thumbnails with PDFium in a disposable
worker process, [Tellma.Core.Imaging.PdfWorker](../Tellma.Core.Imaging.PdfWorker/README.md), so
that PDFium never runs inside the host. With its worker it is the only Tellma package that carries
native binaries, and it depends on no Tellma package but `Tellma.Core.Abstractions` and that
worker. `Tellma.Core` binds to the `IImageProcessor` contract and never references this package, so
a host that adds no processor loads neither Skia nor PDFium, and a distribution that prefers another
imaging library registers its own processor and drops this package.

```csharp
builder.Services.AddTellmaSkia();
```

`AddTellmaSkia` registers the processor (once, however often it is called), binds its options from
`Tellma:Blobs` (`ImageConcurrency`, default half the processor count, at least one;
`PdfWorkerConcurrency`, default a quarter of the processor count, at least one;
`PdfWorkerMemoryLimit`, default 256 MiB, at least 32 MiB), and registers the `core.imaging` startup
check, which loads Skia and, on a host that renders PDF thumbnails, starts a PDF worker that renders
a probe page before the host serves traffic: a missing or mismatched native asset, or a deployment a
worker cannot start in, fails startup with the fix, not the first upload. The probe is bounded (30
seconds), so it fails startup rather than hangs it. The check needs infrastructure, so a build step
without a deployment (the client-asset emission) neither loads Skia nor starts a worker.

## What it does

**Normalises uploads** (`ProcessAsync`). Re-encoding is a security control: polyglot files,
embedded metadata and decompression bombs never reach storage.

1. The bytes' signature must be JPEG, PNG, GIF or WebP, checked in managed code; anything else is
   refused before native code sees it, which keeps Skia's other decoders (BMP, ICO, WBMP, camera
   raw) out of reach. Skia's codec must then agree with the signature.
2. The header's width times height must not exceed the policy's pixel cap. A decoder allocates
   from the header, so the cap is checked on the header alone, before any pixel is decoded, and it
   is the bound on decode memory.
3. JPEG and WebP decode at the smallest scale that still covers the output; only the first frame of
   an animation is decoded; colour profiles are converted to sRGB.
4. The encoded orientation is applied, since Skia does not orient decoded images, and the image is
   fitted: `Contain` within the maximum dimension, `CoverSquare` to the centred square. Neither
   upscales.
5. The result is encoded in the policy's format (WebP and JPEG lossy, PNG lossless) from untagged
   pixels, so no metadata or profile is written. WebP and PNG keep transparency and an opaque input
   stays opaque; JPEG composites transparency over white.

Refusals are `ImageRejectedException` with code `Image.Rejected` and a `reason` of `format`,
`too-many-pixels`, `decode-failed` or `timeout`. An exception the processor does not anticipate is
refused as `decode-failed` too, never surfaced as a server error.

**Renders thumbnails of stored files** (`ThumbnailAsync`): an image under the default 50-megapixel
cap, fitted and oriented the same way, or a PDF's first page fitted to the thumbnail square on
white. Anything else, a PDF that needs a password or is unparsable, an image over the cap, a render
past its time budget, a PDF worker that fails, or a PDF on a host that renders no PDF thumbnails
yields null, which is never a rejection of the file.

## PDF thumbnails in a worker process

PDFium parses hostile documents in native code that no budget can interrupt: loading page 1 alone
expands nested forms into one object per draw, so a document of a kilobyte or two grows to millions
of objects, seconds of CPU and gigabytes of memory before any cap can count them. So each PDF
thumbnail is rendered by a new worker process, and the operating system, not cooperative code,
bounds what it can take.

1. **Start.** The [PdfWorkerLauncher](Workers/PdfWorkerLauncher.cs) runs
   `dotnet exec --runtimeconfig <host>.runtimeconfig.json --depsfile <host>.deps.json
   Tellma.Core.Imaging.PdfWorker.dll --host-pid <pid>`, with the muxer of the runtime the host runs
   on, so the worker resolves exactly the host's framework, PDFium and Skia in whatever layout the
   host was built or published in. Its environment is only the worker's runtime settings
   (workstation GC, no diagnostics, debugger or dumps, invariant globalization), on Linux a managed
   heap capped at `PdfWorkerMemoryLimit` and a single malloc arena, and on Windows `SystemRoot`:
   nothing of the host's, its secrets included. Its working directory is a system one. Before
   every start, a Linux host first closes itself to its own account
   ([below](#what-a-linux-host-closes-of-itself)).
2. **Files.** The worker is confined to reading what it runs on
   ([below](#what-a-worker-may-read)) and writes no file. On Linux the host passes the allowance on
   its command line, and the worker restricts itself with Landlock and starts over as the same
   program, which the kernel then runs restricted on every thread. On Windows the host starts it
   in an AppContainer of its own, whose capability the host's files of the allowance grant read to.
3. **Handles and identity.** On Windows the worker is created suspended on a restricted copy of the
   host's token (no privilege, low integrity), made a lowbox token in its AppContainer, put in its
   own Job Object, then resumed, and a handle list lets it inherit its three pipes and nothing
   else. On Linux .NET opens every descriptor close-on-exec; the host lists any descriptor some
   native library left inheritable, and the worker closes each before it reads anything. A Linux
   worker runs as the host's user.
4. **Sandbox and limits.** The worker reports ready once its runtime is up, with its confinement:
   the host refuses a worker it confines that does not report it. On Linux it has by then
   asked the kernel to kill it when its host dies (and has stopped if the host was already gone),
   left its host's session, so that a terminal the host was started from is not the worker's to
   control, made itself non-dumpable, which closes its entries under `/proc` (the upload on its
   input among them) to the host's other workers, dropped every capability, and installed a
   seccomp filter on all its threads that refuses sockets, new programs and processes, SysV
   objects, every call that would signal, trace, read or re-limit another process or have the
   kernel signal one on the worker's word, input pushed into a terminal, the calls that change or
   lock a file, POSIX message queues, watching a file or reading its extended attributes by name,
   and the kernel interfaces exploits favour (the host refuses a Linux worker without the filter).
   Only then does the host cap it from outside: on Windows, the job's
   committed-memory limit (the baseline plus `PdfWorkerMemoryLimit`), user-mode CPU time, one
   process, no crash dialog, no desktop or clipboard, and death with the host's handle; on Linux,
   `prlimit` sets `RLIMIT_DATA` (the private memory the worker holds once started plus
   `PdfWorkerMemoryLimit`: the cap a document's memory meets), `RLIMIT_AS` (its address space once
   started plus that limit and 128 MiB of slack, for what the data limit does not count),
   `RLIMIT_CPU`, and zero core-dump and file sizes, soft and hard alike. The CPU limit is the
   call's budget plus a second, at most 60 s, and 60 s for a call without a budget, so a worker
   always ends on its own. Then the host sends the document.
5. **Protocol.** Three length-prefixed, size-capped frames over the worker's standard input and
   output ([WorkerProtocol](../Tellma.Core.Imaging.PdfWorker/WorkerProtocol.cs)): ready, request
   (thumbnail size and the PDF, at most 32 MiB), result (a PNG, or a refusal or failure detail). Each
   reader refuses a wrong magic number or a length beyond its cap before allocating for it.
6. **Answer.** The worker's PNG is treated like an upload: [WorkerPng](Workers/WorkerPng.cs) checks
   its signature, its header (8-bit RGB or RGBA, no interlace, the longer side exactly the thumbnail
   size) and that nothing but image data follows; Skia's PNG decoder then decodes it at those
   dimensions, and it is encoded in the policy's format exactly as a page rendered in-process was.
   A worker's detail text is reduced to one line of printable ASCII before it is logged.
7. **End.** The worker is killed when the budget runs out or the caller cancels, and always when
   the call ends; the call's PDF slot comes back only once the worker's process is gone. On Windows
   the kill also cancels the host's pending reads and writes on the worker's pipes, so a call ends
   with its worker even when some other process inherited one of the worker's pipe ends. A worker
   whose input closes (its host died, or gave up) exits at once even mid-render, which only a
   worker still running its own code does. The operating system needs no such cooperation: on
   Windows the job kills the worker with the host, and on Linux the kernel kills it when the host
   thread that started it ends, a request the filter keeps the worker from withdrawing; the CPU
   limit stands behind both.

A worker renders one document and exits, so no upload ever shares a process with another tenant's.
Starting one costs about 0.2 to 0.5 s (on Linux about 0.1 s of it is the runtime's second start,
which the worker's confinement takes); PDF thumbnails are rare (one per PDF upload) and the budget
is 5 s, so a pool would buy little and lose that isolation. PDF workers have slots of their own,
`PdfWorkerConcurrency`, apart from the image slots: at most that many run at once, each under its
memory limit, and image uploads never wait behind them. A PDF that finds every slot taken waits for
one within its own budget, and gets no thumbnail when the budget runs out.

A worker that fails in any way (it cannot start, crashes, is stopped by a limit, breaks the protocol
or answers with a malformed image) is counted as `decode-failed` and logged as `BlobPdfWorkerFailed`
at Warning; its document gets no thumbnail and its upload proceeds. On Windows a crashed worker
stays alive, its pipes open, until Windows Error Reporting has written its report (seconds on a
quiet machine, half a minute on a loaded one); a call whose budget runs out first ends there, with
the worker killed, and is counted as `timeout`.

## What a Linux host closes of itself

A worker runs under its host's account, and Linux and the .NET runtime both let a process into
another process of its account through ordinary files, which no filter on the worker can tell from
the files a render must open. So before it starts a worker, every time, a Linux host closes those
ways from its own side ([HostIsolation](Workers/HostIsolation.cs)), and starts no worker when it
cannot:

- **It stops being dumpable** (`prctl(PR_SET_DUMPABLE, 0)`). The kernel then refuses a process
  without `CAP_SYS_PTRACE`, which a worker never holds, every entry under `/proc/<host>` that shows
  or changes the host: its descriptors (`fd/<n>`), its environment, its memory and its memory map.
  Without this a worker can reopen any file the host holds open, and one descriptor of a .NET
  process is the memory its compiled code is written through, so reopening that one is rewriting
  the host's code; it can also read the host's environment, secrets included.
  `kernel.yama.ptrace_scope` guards neither: it covers `ptrace` and `/proc/<pid>/mem` only.
- **It removes its runtime's debugger pipes.** Unless it was started with
  `DOTNET_EnableDiagnostics_Debugger=0`, the runtime listens for a managed debugger on two named
  pipes in the temporary directory, `clr-debug-pipe-<pid>-<start time>-in` and `-out`, open to the
  account: whoever opens them is the host's debugger, and reads and writes its memory. A host that
  cannot remove one it can still open starts no worker, and its failure says so.

Measured in `mcr.microsoft.com/dotnet/sdk:10.0` (kernel 6.18, `ptrace_scope` 1) with a process under
a worker's limits against a host of its account. Before: it opened the host's code descriptor for
writing and read the host's environment, as an unprivileged user and as root without capabilities,
and reached the debugger pipes. After: each is refused, with the memory and its map, and no pipe is
left. A root host that keeps its capabilities was closed to a worker's `/proc` access already (the
kernel asks that the caller hold the target's capabilities), not to the pipes.

The price is the operators':

| Diagnostic | On a Linux host that has started a worker |
|---|---|
| The kernel's core dump of a crashed host | None. |
| The runtime's crash dump (`DOTNET_DbgEnableMiniDump=1`) | None, unless the host's container or service holds `CAP_SYS_PTRACE`: the dump writer is refused `/proc/<pid>/mem`. With the capability the dump is written. Measured. |
| `dotnet-dump collect` | The same: the runtime reports a failure and writes no file, unless the host holds `CAP_SYS_PTRACE`. Measured through the diagnostics port's dump command; the tool itself was not run. |
| `dotnet-counters`, `dotnet-trace` | Work. The diagnostics port is a socket, which a worker cannot create, so the host leaves it alone. Its process-information command and an event stream were measured through the port; the tools themselves were not run. |
| Attaching a managed debugger | Not possible once a worker has started, and the startup check starts one. A debugger that started the host, or attached before that, holds the pipes open and stays attached (by the runtime's design; not exercised). |
| `strace`, `gdb`, `lldb`, `lsof`, reading `/proc/<pid>` | Only for a process with `CAP_SYS_PTRACE`, as root on the machine has. |

## What a worker may read

The host computes a worker's allowance ([WorkerAllowance](Workers/WorkerAllowance.cs)) once per
launcher, from what it runs on itself:

- the runtime's installation: on Linux its `host` and `shared` directories, with everything beneath
  them; on Windows the installer grants every AppContainer read on it under Program Files, and on
  one installed elsewhere (a user's profile, a repository's own `.dotnet`) the host grants the
  workers' capability read and execute on its muxer and its `host` and `shared` directories, where
  its account may change their permissions; a Development host that may not runs its workers
  unconfined and logs `BlobPdfWorkerUnconfined` with the cause;
- the host's runtime configuration, dependency manifest, trusted assemblies and native libraries
  (PDFium and Skia among them), each by name: never their directory, which holds the host's
  configuration and secrets too;
- on Linux, the system's library directory (the C library's), any other library the host has
  mapped, by name, the dynamic loader's cache, and the font directories PDFium looks in
  (`/usr/share/fonts`, `/usr/local/share/fonts`, `/usr/share/X11/fonts/Type1` and `TTF`); and the
  worker's own `/proc/<pid>`, which the runtime reads, and whose `net/` and `mountinfo` entries
  describe the network and mount namespaces it shares with its host;
- on Windows, the system's files, which grant every AppContainer read.

Nothing else: not the host's configuration files or temporary directory, not the account's home,
not another process's `/proc` entries, not `/proc` itself, not a terminal. A Windows host adds an
access-control entry granting the workers' capability (`tellmaPdfWorker`) read and execute to each
of its own files in the allowance, once, and leaves it in place; only processes that hold that
capability, its workers, gain anything by it. A file that already lets every AppContainer read it,
as one under Program Files does, is left alone. The host must therefore be allowed to change the
other files' permissions, as the account that built or deployed them is; a Development host whose
account may not (a build made by another account, say) runs its workers unconfined, and the
`core.imaging` check logs `BlobPdfWorkerUnconfined` naming the file.

A worker reads the runtime's `host` and `shared` directories, the system's library directory and
the font directories in full, so a host installed beneath one of them (a package under
`/usr/lib/<app>`, say, where the system's libraries are in `/usr/lib`) would hand its configuration
to its workers. Such a host starts no confined worker, and the `core.imaging` check fails at
startup with the fix: install the host in a directory of its own. A Windows worker also reads
whatever grants every AppContainer read, which Program Files does throughout, so a Development host
installed under Program Files hands its configuration to its workers. It is not refused, since
before its workers were confined they read all the account could; the `core.imaging` check logs
`BlobPdfWorkerReadsHostDirectory` at Warning instead, naming the directory. A build's output and a
user's profile grant AppContainers nothing.

## Where PDF thumbnails render

A worker is only as contained as its operating system's hold on it, so a host renders PDF
thumbnails only where that hold is good enough for production, and for developers elsewhere:

| Host | PDF thumbnails |
|---|---|
| Linux with glibc, on a kernel that offers Landlock | Rendered, in every environment. This is the production host. |
| Linux with glibc, without Landlock | Rendered only in the Development environment, with unconfined workers; the check logs `BlobPdfWorkerUnconfined` at Warning. Landlock is in Linux 5.13 and later, enabled where it is listed in `/sys/kernel/security/lsm`; a container's system-call filter must allow its three calls, as Docker's default profile did when measured. |
| Windows | Rendered only in the Development environment (`IHostEnvironment.IsDevelopment()`). The worker is confined in an AppContainer, made through `NtCreateLowBoxToken`, an undocumented system call, and not yet verified under the Windows hosting environments. Where the host's account may not grant the workers' capability read on the .NET installation or on one of its own files, workers run unconfined and the check logs `BlobPdfWorkerUnconfined` at Warning, naming the installation or the file. |
| macOS | Rendered only in the Development environment. A macOS worker runs under no operating-system limit, and the check logs `BlobPdfWorkerUnconfined`. |
| Linux with another C library (musl, as on Alpine) | Never. The .NET runtime there signals its threads with `tkill`, which the worker's filter refuses. |

A host that renders none starts no worker: a PDF there gets no thumbnail, as a file of any other
type gets none, which is no failure and is counted as none. Its `core.imaging` check passes and
logs `BlobPdfThumbnailsDisabled` once, at Warning, with the reason. A processor constructed without
a host environment is not in Development.

## Hostile input and native code

- **Bounded image work.** A native decode cannot be stopped mid-call, so normalisations and image
  thumbnails run in `ImageConcurrency` slots, each released only when its native work really ends.
  A hostile image wastes one slot for a while; it never starves the host, and memory is bounded by
  the slots times one decoded frame (four bytes per pixel under the cap, a third more for the
  mipmaps of the downscaling draw). PDF workers run in `PdfWorkerConcurrency` slots of their own,
  each released only when its worker's process is gone, so a burst of hostile PDFs delays other
  PDF thumbnails and never an upload.
- **Budgets.** `ProcessAsync`'s budget bounds the wait for a slot and, separately, the work: no slot
  in time is `ImageProcessingBusyException` (the server is busy; 503 with `Retry-After`), work past
  its budget is a `timeout` rejection (the input is at fault). A thumbnail's budget covers the wait
  for its slot and the render, and running out yields null and kills a PDF worker.
- **PDF preflight, in the worker.** A PDF over 32 MiB is not read. Otherwise the worker walks page 1
  through PDFium's object API before rendering, and refuses it when a page side exceeds 14,400
  points, an image declares more than 50 megapixels (16 for JPEG 2000 and JBIG2, whose decoders
  hold the whole image), or the page holds more than 100,000 objects counted through every form.
  The walk keeps honest renders cheap; what it cannot see (the expansion inside the page load,
  memory for non-image content, PDFium bugs) the worker's limits contain.
- **A thumbnail never fails an upload.** Every refusal, failure, timeout, worker failure and
  unanticipated exception of a thumbnail yields null; only the caller's own cancellation and invalid
  arguments throw.
- **The time budget cancels for real.** Native decoders in this process read their input on demand
  through a gated view; when the budget runs out the call returns null at once and the view reads
  as end of input, so a streaming decode in flight stops at its next read. One lock covers the gate
  check and each read of the caller's stream, and closing takes it, so once the call has returned
  the processor never touches that stream again.
- **No managed exception reaches native code.** Every member of the gated view that a native reader
  calls returns end of input instead of throwing.
- **No native handle outlives a call.** Every Skia object is disposed in the method that creates
  it, on every path; the suite counts SkiaSharp's live wrappers to prove it.

## Residual risks

What a document does through PDFium's ordinary behaviour (taking memory and CPU, crashing) ends in
its worker. The risks below need more: an exploit of PDFium that runs an attacker's code in the
worker. On Linux, the production host:

- **A compromised worker reads what it runs on.** It holds no capability and can gain no
  privilege; it cannot reach the network, start a program, signal, trace or re-limit another
  process, or change a file through any call that changes one, nor through any `ioctl` of the
  VFS's, ext4's, XFS's, Btrfs's or f2fs's. What it can read is its allowance: the runtime, the
  host's assemblies, dependency manifest and runtime configuration (code and its wiring, no secrets
  unless the host puts some there), the system's libraries, the loader's cache, the fonts, and its
  own `/proc` entries. It could encode what it reads into the pixels of the thumbnail it returns,
  the one output the host accepts; none of it is the host's configuration, secrets, temporary files
  or uploads. The host's command line it can read in its own `/proc` entries only as its own; the
  host's it cannot. A terminal the host was started from is closed to it: it cannot open the
  device.
- **What its own `/proc` entries tell of its host.** Some of the worker's own entries describe
  the namespaces it shares with its host, not the worker: `net/tcp`, `net/tcp6`, `net/udp`,
  `net/unix` and `net/arp` list the address and port of every connection the host holds (its
  database, its cache, its identity provider), its Unix sockets and its network neighbours, and
  `mountinfo` the mount table, which on a file share names the storage account. None is a
  credential. Landlock cannot refuse them beneath a directory it grants, and closing them takes a
  network and mount namespace of the worker's own, the same work as closing `stat`.
- **What the kernel's Landlock does not govern.** Landlock decides on opening, listing, executing
  and changing files, not on looking them up: a worker can still learn whether a path exists, and
  a file's size, owner and times (`stat`, `access`), and a link's target (`readlink`). It cannot
  read a byte of such a file, watch it (the filter refuses inotify and fanotify) or read its
  extended attributes (the filter refuses their path forms). Nor does Landlock govern `ioctl` on a
  file the worker may open: the filter refuses every `ioctl` of the VFS's, ext4's, XFS's, Btrfs's
  and f2fs's types, and another filesystem's own `ioctl`s on a file of the allowance rest on that
  filesystem's own checks.
- **An unconfined worker.** On a Development host without Landlock, a worker can read what the
  host's account can read, its configuration files included; its filter still refuses the calls
  that change a file. The account's other processes are open to it as well: it can read such a
  process's environment and reopen its open files for reading and, if it is a .NET process, its
  code memory and debugger pipes, whatever `kernel.yama.ptrace_scope` is; where that is 0, or the
  kernel has no Yama, it can open such a process's memory too, and the `core.imaging` check logs
  `BlobPdfWorkerPtraceUnrestricted` at Warning beside `BlobPdfWorkerUnconfined`. A confined worker
  reaches no other process's `/proc` entries, and the kernel refuses it the `ptrace` family of
  access to every process outside its confinement, whatever that setting; it also reaches no
  other worker while that one starts, the moment a worker is still dumpable.
- **A host that runs as root.** The worker holds no capability, and what root owns is closed to it
  beyond its allowance, as anything else is. An unconfined worker of a root host would read root's
  files and could raise the host's `/proc/<pid>/oom_score_adj`; a confined one can open neither.
  Run the host as an unprivileged user all the same, as the `app` user of the .NET container
  images is.
- **The memory limit binds a document, not exploit code.** `RLIMIT_DATA` counts private memory,
  which is what PDFium, Skia and the runtime allocate. Shared mappings are held only by the
  address-space limit, and a started worker holds about 3.2 GiB of address space at the default
  limit, nearly all of it the runtime's own reservations (just under 2 GiB for compiled code, and
  five times the limit for the managed heap). Code in control of a worker could unmap those and
  map shared memory in their place: about 3.5 GiB in all at the default limit, not 256 MiB. Kernel
  memory held through descriptors (pipe buffers) and threads is counted by neither limit. All of
  it is released when the worker dies, at the latest when the call's budget kills it. Only a memory
  cgroup around each worker would bound it tighter; until one is in place, the container's own
  memory limit is the outer bound.
- **Memory is capped per worker, not per host.** `PdfWorkerConcurrency` workers may each take
  `PdfWorkerMemoryLimit`; size both for the host.
- **Deployment.** A worker needs the host to run framework-dependent on an installed .NET runtime,
  not as a single file or self-contained, on Linux with glibc, seccomp, Landlock and `prlimit`; the
  startup check fails where a worker cannot start.

On Windows and macOS, where PDF thumbnails render only in Development:

- **Windows.** The worker's AppContainer keeps it out of the host's process (ending it included)
  and every other worker's, off the network (the loopback interface included), and away from every
  file but the system's, the runtime's and those of its allowance; its low integrity level and
  the AppContainer each keep it from writing any of the account's files. It rests on
  `NtCreateLowBoxToken`, which Windows' own AppContainer start calls and Chromium's sandbox calls
  likewise, but which is undocumented; the documented start needs an AppContainer profile
  registered per user and per worker. The host's own files keep the access-control entries it adds
  for the workers' capability. Memory mapped from pagefile-backed sections is not counted by the Job
  Object's limit (768 MiB mapped and touched under a 128 MiB limit, measured). Production on
  Windows waits on verifying the AppContainer under the Windows hosting environments.
- **macOS.** A worker runs under no operating-system limit; only the budget's kill ends it.

## Telemetry

The processor records `tellma.blobs.image.duration` (by `kind` and `operation`) and
`tellma.blobs.image.failures` (by `kind`, `operation` and `reason`, the reasons above) on the
`Tellma.Core` meter, because only it can tell a render that ran out of time from a file it cannot
render. The `BlobImageRender` debug event is written immediately before native code first touches
an input (or a PDF worker is started); `BlobImageFailed` (debug) records each failure,
`BlobImageUnexpectedFailure` (warning) an exception the processor does not anticipate,
`BlobPdfWorkerFailed` (warning) a PDF worker that failed rather than refusing its document, and
`BlobImageBusy` (warning) an upload that found every slot busy. The startup check writes
`BlobPdfThumbnailsDisabled` (warning) on a host that renders no PDF thumbnails, with the reason,
`BlobPdfWorkerUnconfined` (warning) on a Development host whose workers run without filesystem
confinement, and `BlobPdfWorkerPtraceUnrestricted` (warning) on a Linux machine whose kernel lets
such an unconfined worker open the memory of the other processes of the host's account.

## Native assets

The package pins SkiaSharp centrally, with the Skia natives on the same milestone (SkiaSharp
refuses a mismatched one); the worker pins PDFtoImage and the PDFium builds PDFtoImage is compiled
against. Linux uses SkiaSharp's NoDependencies build, so no fontconfig is needed on a server or in a
container. A framework-dependent build carries the natives for win-x64, win-arm64, linux-x64,
linux-arm64 (glibc and musl) and macOS; a runtime-specific publish carries its runtime's only. The
worker runs on the same files: its seccomp filter is written for x64 and arm64, the server
architectures the platform deploys to. The package's `buildTransitive` targets (imported by the
repository's `Directory.Build.targets` for in-repo consumers) drop SkiaSharp's Windows native
symbol files, about 100 MB per runtime, from every consumer's build output, deps.json and publish.
Licences: SkiaSharp and PDFtoImage MIT, the PDFium builds Apache-2.0 (PDFium itself BSD-3-Clause).
