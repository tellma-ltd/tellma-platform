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
worker cannot start in, fails startup with the fix, not the first upload.

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
2. **Handles and identity.** On Windows the worker is created suspended on a restricted copy of the
   host's token (no privilege, low integrity), put in its own Job Object, then resumed, and a
   handle list lets it inherit its three pipes and nothing else. On Linux .NET opens every
   descriptor close-on-exec; the host lists any descriptor some native library left inheritable,
   and the worker closes each before it reads anything. A Linux worker runs as the host's user.
3. **Sandbox and limits.** The worker reports ready once its runtime is up. On Linux it has by then
   asked the kernel to kill it when its host dies (and has stopped if the host was already gone),
   left its host's session, so that a terminal the host was started from is not the worker's to
   control, made itself non-dumpable, which closes its entries under `/proc` (the upload on its
   input among them) to the host's other workers, dropped every capability, and installed a
   seccomp filter on all its threads that refuses sockets, new programs and processes, SysV
   objects, every call that would signal, trace, read or re-limit another process or have the
   kernel signal one on the worker's word, input pushed into a terminal, and the kernel interfaces
   exploits favour (the host refuses a Linux worker without the filter). Only then does the host
   cap it from outside: on Windows, the job's
   committed-memory limit (the baseline plus `PdfWorkerMemoryLimit`), user-mode CPU time, one
   process, no crash dialog, no desktop or clipboard, and death with the host's handle; on Linux,
   `prlimit` sets `RLIMIT_DATA` (the private memory the worker holds once started plus
   `PdfWorkerMemoryLimit`: the cap a document's memory meets), `RLIMIT_AS` (its address space once
   started plus that limit and 128 MiB of slack, for what the data limit does not count),
   `RLIMIT_CPU`, and zero core-dump and file sizes, soft and hard alike. The CPU limit is the
   call's budget plus a second, at most 60 s, and 60 s for a call without a budget, so a worker
   always ends on its own. Then the host sends the document.
4. **Protocol.** Three length-prefixed, size-capped frames over the worker's standard input and
   output ([WorkerProtocol](../Tellma.Core.Imaging.PdfWorker/WorkerProtocol.cs)): ready, request
   (thumbnail size and the PDF, at most 32 MiB), result (a PNG, or a refusal or failure detail). Each
   reader refuses a wrong magic number or a length beyond its cap before allocating for it.
5. **Answer.** The worker's PNG is treated like an upload: [WorkerPng](Workers/WorkerPng.cs) checks
   its signature, its header (8-bit RGB or RGBA, no interlace, the longer side exactly the thumbnail
   size) and that nothing but image data follows; Skia's PNG decoder then decodes it at those
   dimensions, and it is encoded in the policy's format exactly as a page rendered in-process was.
   A worker's detail text is reduced to one line of printable ASCII before it is logged.
6. **End.** The worker is killed when the budget runs out or the caller cancels, and always when
   the call ends; the call's PDF slot comes back only once the worker's process is gone. On Windows
   the kill also cancels the host's pending reads and writes on the worker's pipes, so a call ends
   with its worker even when some other process inherited one of the worker's pipe ends. A worker
   whose input closes (its host died, or gave up) exits at once even mid-render, which only a
   worker still running its own code does. The operating system needs no such cooperation: on
   Windows the job kills the worker with the host, and on Linux the kernel kills it when the host
   thread that started it ends, a request the filter keeps the worker from withdrawing; the CPU
   limit stands behind both.

A worker renders one document and exits, so no upload ever shares a process with another tenant's.
Starting one costs about 0.2 to 0.5 s; PDF thumbnails are rare (one per PDF upload) and the budget
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

## Where PDF thumbnails render

A worker is only as contained as its operating system's hold on it, so a host renders PDF
thumbnails only where that hold is good enough for production, and for developers elsewhere:

| Host | PDF thumbnails |
|---|---|
| Linux with glibc | Rendered, in every environment. This is the production host. |
| Windows, macOS | Rendered only in the Development environment (`IHostEnvironment.IsDevelopment()`). A Windows worker can still end its host's process and open other workers of the same account; a macOS worker runs under no operating-system limit. |
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

- **A compromised worker runs as the host's user and reaches its files.** It holds no capability
  and can gain no privilege, and it cannot reach the network, start a program, or signal, trace or
  re-limit another process. But it can read what the host's account can read, the host's
  configuration files included, and encode what it reads into the pixels of the thumbnail it
  returns, the one output the host accepts. The host's environment and memory it cannot read, for
  the host closes them; the host's command line it can, as every process on the machine can, so
  pass the host no secret there. The zero file-size limit stops it creating file content or
  growing a file, not changing files the account may already write: it can overwrite them in place
  through a mapping, and truncate, rename or delete them, the application's own files included
  where the account owns them. A terminal the host was started from is such a file: a worker
  cannot control it, push input into it or have the host signalled through it, but it can open the
  device and read what is typed there, or write to it. A filesystem sandbox (Landlock) over the
  worker would close all of this, `/proc` and the temporary directory with it; none is in place.
  Until then, run the host as a service, not from a terminal, and as a user that cannot write its
  own application files.
- **The account's other processes are open to a worker.** The host closes itself, and each worker
  closes itself; any other process of the same account does neither. A worker can read such a
  process's environment and reopen its open files and, if it is a .NET process, its code memory
  and debugger pipes, whatever `kernel.yama.ptrace_scope` is. Where that is 0, or the kernel has no
  Yama, it can open such a process's memory too, and the `core.imaging` check logs
  `BlobPdfWorkerPtraceUnrestricted` at Warning. Run nothing else under the host's account: give
  the host a container, or a service account, of its own.
- **A worker is open to its host's other workers while it starts.** The kernel makes every newly
  started program dumpable, so from its start until it enters its sandbox (the runtime's own
  startup, some tens of milliseconds, before the host sends it anything) a worker of an
  unprivileged host can be opened through `/proc` by a compromised worker that runs at that
  moment: its pipes among them, through which the upload and the thumbnail later pass. The same
  filesystem sandbox would close this, as would sockets in place of the pipes, which `/proc` does
  not reopen. A worker of a root host that keeps its capabilities has no such moment.
- **A host that runs as root lends its worker root's files.** The worker holds no capability, but
  what root owns is its own. Beside the files, it can raise the host's `/proc/<pid>/oom_score_adj`
  (measured), which makes the host the kernel's first choice when memory runs out; an unprivileged
  host's is closed to it. Run the host as an unprivileged user, as the `app` user of the .NET
  container images is.
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
  not as a single file or self-contained, on Linux with glibc, seccomp and `prlimit`; the startup
  check fails where a worker cannot start.

On Windows and macOS, where PDF thumbnails render only in Development:

- **Windows.** The worker's low integrity level keeps it out of the host's memory, threads and
  handles, and from writing the host's files. It can still end the host's process and open the
  account's other low-integrity processes, other workers among them; it can read the account's
  files; nothing keeps it off the network (no unprivileged Windows mechanism denies one process
  the network); and memory mapped from pagefile-backed sections is not counted by the Job Object's
  limit (768 MiB mapped and touched under a 128 MiB limit, measured).
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
`BlobPdfThumbnailsDisabled` (warning) on a host that renders no PDF thumbnails, with the reason, and
`BlobPdfWorkerPtraceUnrestricted` (warning) on a Linux machine whose kernel lets a worker open the
memory of the other processes of the host's account.

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
