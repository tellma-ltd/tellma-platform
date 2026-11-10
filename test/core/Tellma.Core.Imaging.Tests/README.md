# Tellma.Core.Imaging.Tests

The image processor's unit suite, over real SkiaSharp, real PDFium and real PDF worker processes on
Windows and Linux. Every fixture is generated in code, honest or hostile, so no binary file is
committed: Skia encodes the honest images, and the hostile ones are honest files with their headers
patched or segments spliced in ([ImageFixtures](Fixtures/ImageFixtures.cs)); the PDFs are written
with correct cross-reference tables, including one encrypted with RC4 under an empty user password,
computed here ([DocumentFixtures](Fixtures/DocumentFixtures.cs)).

Two test assets run as processes of their own, started with this suite's runtime configuration and
dependency manifest: [the misbehaving stand-in worker](../assets/Tellma.Core.Imaging.TestWorker/README.md)
and [a host-shaped application](../assets/Tellma.Core.Imaging.SmokeHost/README.md).

| Folder | Covers |
|---|---|
| `Processing/` | Each accepted format; `Contain` and `CoverSquare` dimensions and crops, never upscaled; thumbnail sizes from the normalised image; output content type per format; alpha kept in WebP and PNG, opaque inputs opaque, JPEG over white; an animated GIF and an animated WebP (at a reduced decode scale) reduced to their first frame; all eight Exif orientations applied; no Exif, comment, text or colour-profile container in any output; a JPEG/HTML polyglot re-encoded with no `<` in its first 8 KiB; headers declaring 60 megapixels refused before decode in all four formats; other formats refused with no native call; malformed and truncated files refused as undecodable. |
| `Thumbnails/` | Image thumbnails fitted and oriented; PDF page 1 at the thumbnail size, from a multi-page PDF too, rendered in a worker byte for byte as it was rendered in-process; password-protected, unparsable, empty and truncated PDFs, OOXML, zip and text yielding null, while a PDF encrypted under an empty user password renders; the worker's walk (an image declaring too many pixels, fewer for JPEG 2000 and JBIG2, an oversized page and an oversized file each yield null, refused before the render is ever called); the image budgets and slots on a fake clock; and the PDF budget: a worker past it is killed, the call returns null at once and the slot comes back only once the worker is gone (on Windows a crashed worker that Windows Error Reporting still holds included), a PDF waiting for a slot starts no worker, a call without a budget waits for a slot and renders, the caller's cancellation and the processor's disposal kill the worker; PDF workers hold slots of their own, so image work never waits behind them; and on a host that renders no PDF thumbnails a PDF gets none, with no worker started and no failure counted. |
| `Workers/` | The worker failing every way (exits and abrupt ends, as a crash ends a process, before and after it is ready, a broken ready or result frame, an oversized or truncated answer, stray bytes, a non-PNG, a PNG of the wrong size, with an extra chunk or corrupt data, a reported failure, no worker assembly), each a null thumbnail, a `BlobPdfWorkerFailed` warning, a slot back and a host still serving; concurrent PDFs in separate workers within the PDF slots; the sandbox probed from inside (the memory limit stops native and managed allocation, and on Linux the address-space limit stops shared mappings while an honest render maps under half its slack; the CPU limit ends a spinning worker the host waits on forever; no process can be started; on Linux no socket is opened, no signal, limit, priority, scheduling change, pidfd or SysV object reaches the host or its process group while the same calls on the worker itself work, no capability is held, and a thread older than the filter is filtered too; the host's process cannot be opened to read or change it, through its memory file on Linux and by every right but ending it on Windows; a file write is refused on Windows and ends the worker on Linux; the environment is exactly the launcher's; no handle the host left inheritable reaches the worker; a worker whose input closes exits at once; a worker that ignores its input still dies with its host; on Windows a killed worker ends its call even while another process holds its output open); hostile fan-outs of forms stopped by the memory limit or the budget with the host's memory unmoved, the worker's memory sampled at its limit, and the walk's object cap when memory allows; the protocol's frames and caps, the PNG check, the launcher's command line and its environment per operating system, the seccomp program run through a BPF interpreter (every refused call, every call allowed on the worker's own process only, and every call it does not name allowed); and the host-shaped asset rendering PDFs without ever loading PDFium, in Development on every system and in Production on Linux only. |
| `Observability/` | Both instruments with their closed tag sets, and the render, failure and unexpected-failure events. |
| `Composition/` | `AddTellmaSkia` registers one processor and one check however often it is called; both concurrencies and the worker memory limit bind from `Tellma:Blobs`, default, and refuse bad values, at the `core.imaging` check too; the check passes, and fails naming the fix when no worker can start or a worker does not render the probe; which hosts render PDF thumbnails (Linux with glibc always, Windows and macOS in Development only, another C library never), a host that renders none passing its check with one warning and no worker started, and the check's warning on a Linux kernel that lets a worker open its host's memory. |
| `Packaging/` | The package's references (the abstractions, its worker and SkiaSharp only; the worker's, PDFtoImage and SkiaSharp only), both native libraries present for win-x64, linux-x64 and linux-arm64, no native symbol file in the output or the deps.json, and the worker beside the host's assemblies, listed in its manifest, with no runtime files of its own. |
| `Internals/` | The fit and orientation arithmetic, the gated input view, and no native Skia object left alive after any path, an abandoned decode included. |

Suites that load PDFium into this process (it is not thread-safe), count live native objects,
measure memory or time, or run processes whose timing matters run in one non-parallel collection.
The Linux-only sandbox tests skip on Windows, with the reason. On Linux a worker dies with the
thread that started it, so a test that starts a worker itself keeps that thread until the worker
is gone: it sleeps, and awaits nothing meanwhile.

## Running

```bash
dotnet test test/core/Tellma.Core.Imaging.Tests
```
