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
`PdfWorkerMemoryLimit`, default 256 MiB, at least 32 MiB), and registers the `core.imaging` startup
check, which loads Skia and starts a PDF worker that renders a probe page before the host serves
traffic: a missing or mismatched native asset, or a deployment a worker cannot start in, fails
startup with the fix, not the first upload.

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
past its time budget, or a PDF worker that fails yields null, which is never a rejection of the
file.

## PDF thumbnails in a worker process

PDFium parses hostile documents in native code that no budget can interrupt: loading page 1 alone
expands nested forms into one object per draw, so a document of a kilobyte or two grows to millions
of objects, seconds of CPU and gigabytes of memory before any cap can count them. So each PDF
thumbnail is rendered by a new worker process, and the operating system, not cooperative code,
bounds what it can take.

1. **Start.** The [PdfWorkerLauncher](Workers/PdfWorkerLauncher.cs) runs
   `dotnet exec --runtimeconfig <host>.runtimeconfig.json --depsfile <host>.deps.json
   Tellma.Core.Imaging.PdfWorker.dll`, with the muxer of the runtime the host runs on, so the worker
   resolves exactly the host's framework, PDFium and Skia in whatever layout the host was built or
   published in. Its environment is only the worker's runtime settings (workstation GC, no
   diagnostics, debugger or dumps, invariant globalization) and, on Windows, `SystemRoot`: nothing
   of the host's, its secrets included. Its working directory is a system one.
2. **Handles.** On Windows the worker is created suspended, put in its own Job Object, then
   resumed, and a handle list lets it inherit its three pipes and nothing else. On Linux .NET opens
   every descriptor close-on-exec; the host lists any descriptor some native library left
   inheritable, and the worker closes each before it reads anything.
3. **Sandbox and limits.** The worker reports ready once its runtime is up; on Linux it has then
   installed a seccomp filter on all its threads that refuses sockets, new programs and processes,
   access to other processes and the kernel interfaces exploits favour (the host refuses a Linux
   worker without it). Only then does the host cap it from outside: on Windows, the job's
   committed-memory limit (the baseline plus `PdfWorkerMemoryLimit`), user-mode CPU time (the
   budget plus a second), one process, no crash dialog, no desktop or clipboard, and death with the
   host's handle; on Linux, `prlimit` sets `RLIMIT_DATA` (the binding memory cap: the runtime
   reserves tens of gigabytes of address space at start, so `RLIMIT_AS` alone would not cap the
   managed heap), a looser `RLIMIT_AS`, `RLIMIT_CPU`, and zero core-dump and file sizes, soft and
   hard alike. Then the host sends the document.
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
   the call ends; the call's image slot comes back only once the worker's process is gone. A worker
   whose input closes (its host died, or gave up) exits at once even mid-render; on Windows the job
   kills it with the host anyway, and on Linux its CPU limit bounds it regardless.

A worker renders one document and exits, so no upload ever shares a process with another tenant's.
Starting one costs about 0.2 to 0.5 s; PDF thumbnails are rare (one per PDF upload) and the budget
is 5 s, so a pool would buy little and lose that isolation. PDF work takes an image slot like any
other image work, so at most `ImageConcurrency` workers run at once, each under its memory limit.

A worker that fails in any way (it cannot start, crashes, is stopped by a limit, breaks the protocol
or answers with a malformed image) is counted as `decode-failed` and logged as `BlobPdfWorkerFailed`
at Warning; its document gets no thumbnail and its upload proceeds.

## Hostile input and native code

- **Bounded image work.** A native decode cannot be stopped mid-call, so normalisations and
  thumbnails run in `ImageConcurrency` slots, each released only when its native work really ends:
  a decode's, or a PDF worker's process. A hostile file wastes one slot for a while; it never
  starves the host, and memory is bounded by the slots times one decoded frame (four bytes per pixel
  under the cap, a third more for the mipmaps of the downscaling draw) or one worker's limit.
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

- **A compromised worker runs as the host's user.** An exploit of PDFium gains code execution in a
  worker that can read what the host's account can read, the host's own `/proc` entries and
  environment included where the kernel allows a same-user read, and can encode what it reads into
  the pixels of the thumbnail it returns, the one output the host accepts. A filesystem sandbox
  (Landlock on Linux, an AppContainer on Windows) would close that; neither is in place.
- **Windows does not keep a worker off the network.** No unprivileged Windows mechanism denies one
  process the network; the Linux worker's seccomp filter does.
- **Memory is capped per worker, not per host.** `ImageConcurrency` workers may each take
  `PdfWorkerMemoryLimit`; size both for the host.
- **Deployment.** A worker needs the host to run framework-dependent on an installed .NET runtime,
  not as a single file or self-contained, and on Linux needs seccomp and `prlimit`; the startup
  check fails where they are missing.

## Telemetry

The processor records `tellma.blobs.image.duration` (by `kind` and `operation`) and
`tellma.blobs.image.failures` (by `kind`, `operation` and `reason`, the reasons above) on the
`Tellma.Core` meter, because only it can tell a render that ran out of time from a file it cannot
render. The `BlobImageRender` debug event is written immediately before native code first touches
an input (or a PDF worker is started); `BlobImageFailed` (debug) records each failure,
`BlobImageUnexpectedFailure` (warning) an exception the processor does not anticipate,
`BlobPdfWorkerFailed` (warning) a PDF worker that failed rather than refusing its document, and
`BlobImageBusy` (warning) an upload that found every slot busy.

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
