# Tellma.Core.Imaging.PdfWorker

The PDF render worker of [Tellma.Core.Imaging](../Tellma.Core.Imaging/README.md): an executable
assembly that runs only as a process the image processor starts, once per PDF thumbnail, so that
PDFium, which parses hostile documents in native code no budget can interrupt, never runs inside a
host. It depends on no Tellma package; it comes with `Tellma.Core.Imaging` and is never referenced
directly.

It has no app host, runtime configuration or dependency manifest of its own: the processor starts
it as

```
dotnet exec --runtimeconfig <host>.runtimeconfig.json --depsfile <host>.deps.json Tellma.Core.Imaging.PdfWorker.dll
```

so it resolves exactly the host's framework, PDFium and Skia natives, in whatever layout the host
was built or published in. Run on its own, it would only wait for a request on standard input.

## One worker's life

1. **Sandbox** ([WorkerSandbox](WorkerSandbox.cs)), before anything of the upload is read: it
   closes each descriptor the host named as inherited by accident (`--close-fd <n> <target>`, only
   when it still refers to that target), keeps its protocol output on a private duplicate of
   standard output while pointing standard output at standard error, so a printing native library
   cannot corrupt a frame, and on Linux installs its [SeccompFilter](SeccompFilter.cs) on every
   thread. Without the filter it does not run on Linux.
2. **Ready**: it writes the ready frame, with what it enforces on itself. The host now applies its
   memory and CPU limits from outside.
3. **Request**: it reads one request ([WorkerProtocol](WorkerProtocol.cs)): the thumbnail size and
   the PDF, at most 32 MiB, refused before allocation when out of range. From then on, its input
   ending means the host is gone or gave up, and it exits at once, mid-render or not.
4. **Render** ([PdfRenderer](PdfRenderer.cs)): [PdfPreflight](PdfPreflight.cs) walks page 1 against
   the caps (page side, image declarations, object count) through PDFium's object API, then
   [PdfiumRasterizer](PdfiumRasterizer.cs) renders it through PDFtoImage, fitted to the square on
   white without annotations or form fields, and Skia encodes it as a lossless PNG.
5. **Answer**: one result frame, the PNG, a refusal (the document is at fault: encrypted, malformed,
   over a cap) or a failure (the worker is: out of memory under its cap, say), and it exits.

## The system-call filter

On Linux (x64 and arm64) the worker refuses, with `EPERM`, what rendering a PDF never needs and a
compromised renderer would want: `socket` and `socketpair` (so no network, the cloud metadata
endpoint included), `execve`, `execveat`, `fork`, `vfork` and a `clone` that is not a thread, the
calls that reach other processes (`ptrace`, `process_vm_readv`/`writev`, `kcmp`, `pidfd_getfd`), and
`io_uring`, `userfaultfd`, `perf_event_open`, `bpf`, `unshare`, `setns` and the key store. `clone3`
answers `ENOSYS`, so the C library creates threads with `clone`, whose flags the filter can read. A
call through another architecture's entry point kills the process. Everything else is allowed.

## Exit codes

0 once a result is written; 64 for an argument it does not know, 65 when the sandbox could not be
put in place (the reason on standard error), 66 for a malformed request, 67 when the host closed its
input, 68 for a failure it could not report. A worker the host or a limit killed reports the
signal's code (137 for SIGKILL) on Linux, and 137 on Windows too.
