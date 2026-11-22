# PC-F11: PDF worker filesystem confinement

## Implementer notes

### What was built, and where

A PDF worker now reads only what it runs on and writes no file at all, on Linux through Landlock
plus the seccomp filter, on Windows through an AppContainer of its own. The existing protections
(seccomp with TSYNC, `RLIMIT_DATA`/`RLIMIT_AS`/`RLIMIT_CPU`, non-dumpable host and worker, `setsid`,
the parent-death signal, dropped capabilities, the Job Object) are unchanged and still run.

- **`src/core/Tellma.Core.Imaging.PdfWorker/Landlock.cs`** (new): the three Landlock calls (444, 445,
  446 on x64 and arm64 alike), the ABI query, the rights by ABI (1: the thirteen filesystem rights;
  2: refer; 3: truncate; 4: TCP bind and connect; 5: ioctl on devices; 6: the signal and abstract
  Unix socket scopes), the ruleset attribute sized by ABI, and `Grantable` (a rule carries only the
  rights its ABI knows, and a file's rule only file rights, which the kernel requires).
- **`WorkerConfinement.cs`** (new), in two stages:
  - **Stage 1** (`--confine` with `--read`, `--read-beneath` and `--execute` paths) runs first in
    `WorkerSandbox.Enter`, before the upload is read. It builds a ruleset that handles every right
    the ABI knows and grants read beneath the allowed paths and execute on the muxer and the ELF
    interpreter. It grants `/proc/self` through an `O_PATH` anchor descriptor. It reads
    `/proc/self/exe`, `cmdline` and `environ`, checks that the command line ends with the worker's
    own arguments, and marks every descriptor from 3 close-on-exec except the anchor
    (`close_range(CLOSE_RANGE_CLOEXEC)`). Then it sets `no_new_privs`, calls
    `landlock_restrict_self` on its one thread, and `execve`s the same muxer with the same runtime
    prefix and the remaining arguments plus `--confined <anchor>`.
  - **Why it starts over.** A restriction binds only the calling thread and what that thread
    starts. The runtime has threads of its own before any worker code runs, and code that took over
    one thread could have another act for it. The program executed anew inherits the restriction
    from its first instruction.
  - **Stage 2** (`TakeConfined`) checks that the anchor is `/proc/<own pid>`, marks it close-on-exec,
    and checks that `open("/")` fails with `EACCES`. Otherwise it stops with exit 65.
  - **Windows.** `InAppContainer()` reads `TokenIsAppContainer`.
- **`WorkerSandbox.cs`**: it calls the two stages on Linux. The ready frame reports the new
  `WorkerCapabilities.FilesystemConfinement` (`WorkerProtocol.cs`, value 2) on Linux when stage 2
  held, and on Windows when the token is an AppContainer's.
- **`SeccompFilter.cs`**: the filter now refuses every change to a file whatever the kernel's
  Landlock ABI is (`EPERM`):
  - `open`/`openat` with any of `O_WRONLY|O_RDWR|O_CREAT|O_TRUNC` (flags mask `0x243`, low 32 bits),
    and `creat`, `truncate`, `open_by_handle_at`;
  - `rename*`, `link*`, `symlink*`, `unlink*`, `mkdir*`, `rmdir`, `mknod*`;
  - `chmod`/`fchmod*`, `chown`/`fchown*`/`lchown`, `utime*`/`futimesat`/`utimensat`, the `*setxattr*`
    and `*removexattr*` calls (the `*at` variants included), and `file_setattr` (469);
  - `ioctl` `FS_IOC_SETFLAGS`/`FS_IOC32_SETFLAGS`/`FS_IOC_SETVERSION`/`FS_IOC32_SETVERSION`/
    `FS_IOC_FSSETXATTR`.

  `openat2` answers `ENOSYS`, like `clone3`, because its flags sit behind a pointer the filter cannot
  read. The C library and the runtime fall back to `openat`.
- **`src/core/Tellma.Core.Imaging/Workers/WorkerAllowance.cs`** (new) is what the host computes.
  - **Every OS**: the runtime's `host` and `shared` directories, read beneath. Each of the host's
    own files outside the installation by name, never their directory, which holds
    `appsettings*.json` and secrets too. These are the runtime configuration (and
    `.runtimeconfig.dev.json` when present), the dependency manifest, the worker assembly, every
    trusted platform assembly, and every native library in `NATIVE_DLL_SEARCH_DIRECTORIES`.
  - **Linux**: the C library's directory beneath (where Skia's and PDFium's dependencies live),
    any other mapped library by name (`/proc/self/maps`), `/etc/ld.so.cache` and `ld.so.preload`,
    and the font directories that exist. The muxer and its `PT_INTERP` are execute.
  - **Windows**: the host's own files (`WindowsFiles`), which get an ACE.
- **`WindowsAppContainer.cs`** (new):
  - `GrantRead` adds an explicit `ReadAndExecute` ACE for the capability SID derived from
    `tellmaPdfWorker` (`DeriveCapabilitySidsFromName`) to each of the host's files, once per process
    and only where it is missing.
  - `CreateToken` turns the existing restricted low-integrity token into a lowbox token with
    `NtCreateLowBoxToken`. Each worker gets a fresh package SID (`DeriveAppContainerSidFromAppContainerName`
    on a GUID name, which registers nothing) and holds the one capability, enabled, and no network
    capability.
  - The new P/Invokes are in `WindowsInterop.cs`. `WindowsWorkerProcess.Start` takes the files to
    grant and swaps the token before `CreateProcessAsUserW`.
- **`PdfWorkerLauncher.cs`**:
  - `CanConfine` is true on Windows, and on Linux when `Landlock.Version() >= 1` (cached).
  - `Confines` (constructor `confine`, default `CanConfine`).
  - `Allowance` (lazy). The Linux arguments sit before the launcher's own.
  - `PdfWorkerFiles` gains `Root`, the muxer's directory.
- **`PdfWorkerClient.cs`**: a worker the launcher confines that does not report
  `FilesystemConfinement` is refused (`Protocol`).
- **`PdfThumbnailSupport.cs`**: new `DisabledWithoutLandlock` (Linux with glibc, no Landlock,
  outside Development), with its fix text. The text of `DisabledOutsideDevelopment` now gives the
  current Windows reason.
- **`ImagingStartupCheck.cs`** and **`ImagingLog.cs`**:
  - New `BlobPdfWorkerUnconfined` warning, when the launcher does not confine.
  - The ptrace-scope warning now fires only for an unconfined Linux worker.
  - The Windows start fix names the ACE requirement, and the worker-error fix names Landlock.
- **Tests and assets**:
  - New `Workers/WorkerConfinementTests.cs` (below).
  - `Tellma.Core.Imaging.TestWorker` gains the `files`, `files-before-filter`, `proc-reads`,
    `open-process` and `connect` probes, a read in `early-thread`, and query-limited in the
    open-process rights, with new libc/Win32 calls in `Native.cs`.
  - `SeccompFilterTests` gains every new refusal, the open-flags table for x64's `open` and both
    arches' `openat`, and `openat2`.
  - `PdfWorkerLauncherTests` gains the allowance (composition on both OSes, argument round trip,
    malformed arguments, `Grantable`).
  - The policy, composition, sandbox and smoke-host tests are updated.
  - The smoke host reports `pdf.workers-confined`.
- **Docs**:
  - Both Imaging READMEs: the worker's life, a new "Filesystem confinement" section, "What a worker
    may read", the policy table, residual risks and telemetry.
  - `ARCHITECTURE.md` (the PdfWorker package row, the Imaging bullet, the hosting bullet).
  - `CONTRIBUTING.md` and `ci.yml`: `TELLMA_REQUIRE_LANDLOCK=1` on Linux CI makes a skipped
    confinement test a failure.

### Design choices worth knowing

- **No temporary directory.** The brief allows the worker to write to its own temp directory, and
  D-043 says it keeps one. The worker writes nothing: it renders from a `byte[]` read off stdin and
  answers on a pipe. So it gets no writable path at all, which is strictly narrower. Its only
  writable descriptors are its pipes and the runtime's anonymous `memfd`s; `/tmp` and `/dev/shm` are
  refused, and the probes check both.
- **Two layers on Linux.**
  - **Landlock** governs reads, listing, every name change, truncation (ABI 3+) and device ioctls
    (ABI 5+).
  - **The filter** refuses every write-path call regardless of ABI. It covers what Landlock never
    governs (mode, owner, times, xattrs, `file_setattr`) and kernels whose Landlock predates
    truncate.
  - `files-before-filter` proves each Landlock refusal on its own, before the filter is installed.
- **D-040's in-place overwrite through a shared mapping.** Every descriptor the worker can open on
  a file is read-only, both layers say so. The kernel refuses `MAP_SHARED|PROT_WRITE`, and an
  `mprotect` to write on a read-only shared mapping, on such a descriptor: `EACCES`, probed both
  ways.
- **`/proc` (D-043).** Only `/proc/<pid>` of the worker itself (`self` and `thread-self` resolve
  into it) is granted. Every other process's entries (status, stat, cmdline, comm, environ, maps,
  cgroup, task) and the listing of `/proc` are `EACCES`, probed against the host, a sibling worker
  and pid 1. Landlock also refuses `ptrace`-mode access to any process outside the domain,
  whatever Yama's `ptrace_scope` is. That is why the `BlobPdfWorkerPtraceUnrestricted` warning now
  fires only when workers run unconfined.
- **Linux without Landlock** (the brief: "fail closed or degrade as D-030 directs"). D-030 names no
  rule for this case, so I followed the pattern of addendum 5 for Windows:
  - outside Development, PDF thumbnails are disabled (`DisabledWithoutLandlock`, logged
    `BlobPdfThumbnailsDisabled` with the fix);
  - in Development they render unconfined, and the startup check logs `BlobPdfWorkerUnconfined`;
  - a host that asked for confinement and got a worker that does not report it refuses the worker.
- **Windows: AppContainer, not only low integrity.**
  - Low integrity alone still lets a worker read every file its account can, so it does not
    confine reads.
  - The documented path (`PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES`) needs a registered
    per-user AppContainer profile (`CreateAppContainerProfile`, tens to hundreds of ms per create
    and delete), and it refused the worker's minimal environment block. The lowbox token over the
    existing restricted token needs none of that. It is what the system's own AppContainer start
    calls, and Chromium uses it the same way.
  - The cost: `NtCreateLowBoxToken` is undocumented.
- **Windows reads.** The runtime's installation and the system grant `ALL APPLICATION PACKAGES`
  read, so `dotnet` under Program Files needs nothing. The host's own files get a persistent ACE
  for the capability SID. It is left in place, so restarts pay nothing and a file a later build
  writes anew is granted again on the next start.
- **Startup cost**, smoke host `--measure 20`, Development, a worker started and one page rendered
  per sample.
  - **Windows** is dominated by machine load from the other slots, so before and after were
    interleaved, three rounds each, from a build of the base commit in the scratchpad and from this
    slot:

    | Side | Median ms per round | Min–max ms over the rounds |
    | --- | --- | --- |
    | Before | 557, 163, 221 | 142–810 |
    | After | 246, 162, 192 | 106–377 |

    There is no measurable cost (the first round's before is load).
  - **Linux** (sdk:10.0 container, kernel ABI 7, interleaved the same way, the base commit built in
    a work volume of its own):

    | Side | Median ms per round | Min ms per round |
    | --- | --- | --- |
    | Before | 141, 179, 162 | 113, 129, 120 |
    | After | 229, 260, 293 | 201, 230, 213 |

    About 0.1 s more per worker, which is the runtime's second start in the restart. The Imaging
    README and `ARCHITECTURE.md` say so beside the "about 0.2 to 0.5 s" start cost.

### Deviations (also in the commit message)

- The worker writes no file, not even to its own temporary directory, which the brief and D-043
  allow. Narrower, and nothing needs one.
- Linux without Landlock: PDF thumbnails are disabled outside Development (new
  `PdfThumbnailSupport.DisabledWithoutLandlock`). Development renders unconfined, with the new
  `BlobPdfWorkerUnconfined` warning. This is my reading of D-030, which names no rule for it.
- Windows confinement is an AppContainer, made by the undocumented `NtCreateLowBoxToken`. Windows
  stays Development-only (`DisabledOutsideDevelopment` unchanged), now for that reason rather than
  addendum 5's.
- The Windows host adds a persistent ACE (read and execute for the `tellmaPdfWorker` capability SID)
  to each of its own files in the allowance. A deployment must let the host change its files' DACLs.
- Beyond the brief, the seccomp filter refuses every file change whatever the Landlock ABI is:
  write-flag opens, `openat2` (`ENOSYS`), and the setattr family Landlock does not govern.
- The `BlobPdfWorkerPtraceUnrestricted` warning fires only for unconfined Linux workers.
- The existing `proc-files` probe now opens read-only. With write-flag opens refused by the filter,
  a write open would pass for the wrong reason and no longer test the host's non-dumpable state.
- New log event `BlobPdfWorkerUnconfined`. New value `PdfThumbnailSupport.DisabledWithoutLandlock`.
  New protocol capability bit 2.

### Residual risk

- **Linux reads.** A worker that runs exploit code can read what it runs on:
  - the runtime's installation;
  - the host's assemblies and runtime files, each by name;
  - the system library directory (all of it, not only the libraries loaded);
  - the fonts and the loader cache.

  None of the host's configuration or secrets is among them.
- **Linux metadata.** Landlock does not govern path lookup, so `stat`, `access`, `readlink` and
  `statx` still tell whether any path exists, with its size, owner and times. Listing and reading
  are refused. `inotify_add_watch` is not among the rights Landlock lists either (not probed), so a
  worker may be able to watch a path for events without reading it. Both leak metadata only.
- **Shared memory** up to the address-space limit, as before (D-040 (1), unchanged).
- **Unconfined Development worker** (no Landlock, or `confine: false` in tests): as before F11, it
  reads what the account can, and the ptrace warning applies.
- **Windows.**
  - A worker reads what grants `ALL APPLICATION PACKAGES` read: system files, Program Files, and
    the registry keys that grant it.
  - It can create a socket but connects nowhere (loopback included, measured).
  - The ACEs persist on the host's files. They grant only processes holding the capability, and
    the account's normal DACL check still applies.
  - The AppContainer is undocumented API, unverified under App Service or IIS hosting.
- **CI** assumes the `ubuntu-latest` runner's kernel lists `landlock` in its LSMs.
  `TELLMA_REQUIRE_LANDLOCK=1` turns a skip there into a failure, so a runner without it shows red
  rather than passing silently.

### Verification

- **Linux** (mcr.microsoft.com/dotnet/sdk:10.0, Docker Desktop's WSL2 kernel, Landlock ABI 7): the
  whole `Tellma.Core.Imaging.Tests` assembly, 491 total, 0 failed, 2 skipped (Windows-only).
  - `files` (confined): every read and listing `EACCES`, and every change `EPERM` (filter first).
    The shared-write `mmap` and `mprotect` give `EACCES`, and `/dev/shm` create gives `EPERM`.
  - `files-before-filter`: every governed attempt is `EACCES` from Landlock alone. `chmod` and
    `utimes` are "done" there, which is why the filter refuses them, and the victim's content is
    unchanged.
  - `proc-reads` against the host, a sibling worker and pid 1: all `EACCES`, while the worker's own
    entries open.
  - `early-thread`: a thread started before the sandbox is refused the socket, the process and the
    file read.
  - The unconfined control reads the victim, the file beside the assembly, `/etc/passwd`, `/` and
    the folder.
  - Rendering works: `A_confined_worker_still_renders`, and every thumbnail, budget and hostile
    document suite runs confined.
- **Windows** (this machine), 384 total, 0 failed, 21 skipped (Linux-only), across the `Workers`,
  `Thumbnails`, `Composition` and `Packaging` namespaces of the same assembly.
  - `files` (confined): all 17 attempts refused.
  - The unconfined control reads.
  - `open-host` and `open-process` (another worker): refused, query-limited included.
  - `connect` to a loopback listener: refused (no answer), while the unconfined control gets
    `connected`.
  - The early thread's file read: refused.
  - The smoke host reports `pdf.workers-confined=yes`.
- **Gates**: `dotnet build Tellma.slnx` and `dotnet format Tellma.slnx --verify-no-changes` (see
  the commit).

### What reviewers should examine hardest

1. **The restart.** Between `landlock_restrict_self` and `execve`, the runtime's other threads are
   unrestricted. Nothing of the upload has been read yet (it arrives after the ready frame, in
   stage 2), and `execve` ends them. Check that nothing reaches stage 2 without `--confined`, and
   that the command-line reconstruction (`cmdline` prefix plus the remaining arguments) cannot be
   steered.
2. **Descriptor hygiene at the restart.** `close_range(3, ~0, CLOEXEC)` then clearing `FD_CLOEXEC`
   on the anchor. Then stage 2's check that the anchor is its own `/proc/<pid>` and that `/`
   answers `EACCES`.
3. **The filter's open-flag test.** `0x243` on the low 32 bits of `open`'s second argument and
   `openat`'s third. `O_TMPFILE` needs write access, so it is refused. Also check the arm64 table,
   which has no legacy name calls.
4. **ABI handling.** Handled rights and the ruleset size per ABI, and file rules masked to file
   rights. An old kernel (ABI 1–2) gets no truncate in Landlock, and the filter covers it; that was
   not exercised on a real old kernel.
5. **The Windows lowbox token**: handle lifetimes in `CreateToken` and `WindowsWorkerProcess.Start`,
   the per-worker package SID, and the ACE edit's failure mode (a `PdfWorkerException` at start).
6. **The policy matrix** in `PdfThumbnailSupport.For` and the smoke-host test that pins it.
