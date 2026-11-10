# Tellma.Core.Imaging.PdfWorker

The PDF render worker of [Tellma.Core.Imaging](../Tellma.Core.Imaging/README.md): an executable
assembly that runs only as a process the image processor starts, once per PDF thumbnail, so that
PDFium, which parses hostile documents in native code no budget can interrupt, never runs inside a
host. It depends on no Tellma package; it comes with `Tellma.Core.Imaging` and is never referenced
directly.

It has no app host, runtime configuration or dependency manifest of its own: the processor starts
it as

```
dotnet exec --runtimeconfig <host>.runtimeconfig.json --depsfile <host>.deps.json Tellma.Core.Imaging.PdfWorker.dll --host-pid <pid>
```

so it resolves exactly the host's framework, PDFium and Skia natives, in whatever layout the host
was built or published in. Run on its own on Windows or macOS, it would only wait for a request on
standard input; on Linux it refuses to run unless the process that started it is the host it names.

## One worker's life

1. **Sandbox** ([WorkerSandbox](WorkerSandbox.cs)), before anything of the upload is read: it
   closes each descriptor the host named as inherited by accident (`--close-fd <n> <target>`, only
   when it still refers to that target), and keeps its protocol output on a private duplicate of
   standard output while pointing standard output at standard error, so a printing native library
   cannot corrupt a frame. On Linux it then asks the kernel to kill it when the host thread that
   started it ends, stops if its parent is not the host named by `--host-pid`, drops every
   capability, and installs its [SeccompFilter](SeccompFilter.cs) on every thread. Without the
   filter it does not run on Linux.
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

On Linux (x64 and arm64) the worker refuses, with `EPERM`, what rendering a PDF never needs and
code that took over the renderer would want:

- **The network**: `socket` and `socketpair`, so nothing is reachable, the cloud metadata endpoint
  included.
- **Programs and processes**: `execve`, `execveat`, `fork`, `vfork` and a `clone` that is not a
  thread.
- **Other processes**: `ptrace`, `process_vm_readv`/`writev`, `kcmp`, the pidfd calls
  (`pidfd_open`, `pidfd_send_signal`, `pidfd_getfd`), `tkill`, `ioprio_set`, `migrate_pages` and
  `move_pages` are refused outright. `kill`, `tgkill`, `rt_sigqueueinfo` and `rt_tgsigqueueinfo` are
  allowed only on the worker's own process id; `prlimit64`, `sched_setaffinity`,
  `sched_setscheduler`, `sched_setparam` and `sched_setattr` only on that id or 0 (the caller);
  `setpriority` only for that one process, never a process group or a user. The id is fixed into
  the filter when it is installed, since a worker never forks, so the runtime's own signals and
  the C library's limit queries keep working while the host, its process group and every other
  process are out of reach.
- **SysV shared memory, semaphores and message queues**, whose objects outlive the process and
  escape its memory limits.
- **The parent-death signal**: `prctl(PR_SET_PDEATHSIG)`, which would withdraw the worker's request
  to die with its host. Every other `prctl` is allowed.
- **Kernel interfaces exploits favour**: `io_uring`, `userfaultfd`, `perf_event_open`, `bpf`,
  `unshare`, `setns` and the key store.

`clone3` answers `ENOSYS`, so the C library creates threads with `clone`, whose flags the filter can
read. A call through another architecture's entry point kills the process. Everything else is
allowed: the filter is a list of what is refused, and it does not confine the worker's view of the
filesystem (the [residual risks](../Tellma.Core.Imaging/README.md#residual-risks) say what that
leaves open). The .NET runtime signals its threads with `tgkill` on glibc and with `tkill` on musl,
which is why a host on musl renders no PDF thumbnails.

## Exit codes

0 once a result is written; 64 for an argument it does not know, 65 when the sandbox could not be
put in place (the reason on standard error), 66 for a malformed request, 67 when the host closed its
input, 68 for a failure it could not report. A worker the host killed reports 137 on Linux
(SIGKILL) and on Windows. One a limit stopped reports the signal's code on Linux (137 at the CPU
limit, 153 for a file write) and the system's status on Windows; an allocation the memory limit
refused surfaces as a reported failure, or as the native library's abort (134 on Linux).
