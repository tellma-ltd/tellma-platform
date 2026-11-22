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

## Review 1

Scope: commit c4a8cac1 against the LEDGER PC-F11 row, D-030 (addenda 3-5), D-040 (2), D-043 (2)-(4)
and D-195. Runs by the reviewer:

- Windows, `dotnet build` of `Tellma.Core.Imaging.Tests` (Build succeeded, 0 warnings), then the
  `WorkerConfinementTests`, `PdfWorkerLauncherTests`, `SeccompFilterTests`, `PdfThumbnailPolicyTests`,
  `ImagingCompositionTests`, `WorkerSandboxTests`, `SmokeHostTests` and `HostIsolationTests` classes:
  231 total, 0 failed, 21 skipped (Linux-only).
- Linux, `mcr.microsoft.com/dotnet/sdk:10.0` on Docker Desktop's WSL2 kernel 6.18 (Landlock ABI 7),
  from `git archive c4a8cac1` in a volume of its own, `TELLMA_REQUIRE_LANDLOCK=1`: the same classes
  plus `HostileDocumentTests`, 235 total, 0 failed, 1 skipped.
- Linux, the same image: a scratch program that restricts its thread under a ruleset handling all
  16 filesystem rights with no rule, then calls `inotify_add_watch` and `listxattr` (R1-4).
- CI: the pc/PC-F11 push run was skipped (CI runs at integration), so nothing to cite. Whether
  `ubuntu-latest`'s kernel lists `landlock` among its LSMs I cannot tell from here. Ubuntu's own
  kernels have carried `landlock` first in `CONFIG_LSM` since 22.04 as far as I know, but the Azure
  flavour was not checked. The integration CI run settles it, and `TELLMA_REQUIRE_LANDLOCK=1` makes
  a missing Landlock show red.

Checked, with no finding:

- **Landlock ABI handling.** The rights per ABI (13 at ABI 1, then refer, truncate, device
  `ioctl`), the ruleset sizes (8/16/24), TCP bind and connect from ABI 4, the signal and
  abstract-socket scopes from ABI 6, and file rules masked to file rights. All match
  `linux/landlock.h`.
- **Ordering.** The ruleset is in place, by restrict-then-`execve`, before any of the upload is
  read. Nothing reaches stage 2 without `--confined`, and stage 2 proves the domain by `/` →
  `EACCES`. Then come the descriptor close, the parent-death signal, `setsid`, non-dumpable, the
  capability drop and the TSYNC filter, in the same order as before. `RLIMIT_*` still come after
  the ready frame.
- **Command-line reconstruction.** It reads `/proc/self/cmdline`, which the host's `execve` wrote,
  before any hostile input, and checks it against the worker's own arguments.
- **The `/proc` anchor.** It is the process's own `/proc/<pid>`. Magic links (`root`, `cwd`, `fd/*`)
  are checked against their resolved paths. Landlock's ptrace hook closes every process outside
  the domain, which also closes D-043 (2)'s dumpable window against other confined workers.
- **Production confinement.** No configuration runs an unconfined worker in production.
  `PdfThumbnailPolicy.For` and `PdfWorkerLauncher.Confines` read the same cached `CanConfine`. The
  `confine` and `pdf` seams are internal. The client refuses a worker that lacks the bit.
- **Windows confinement.** The capability SID comes from `DeriveCapabilitySidsFromName`, and every
  SID it returns is freed. The `OBJECT_ATTRIBUTES` layout and the `NtCreateLowBoxToken` signature
  match Chromium's use. The restricted token is closed on every path, including a failure in
  `GrantRead` or `CreateToken`. The handle list is unchanged.
- **The Windows ACE.** It grants read and execute only, and only to tokens that hold the capability.
  The account's own DACL check still applies, so it gives nothing to anyone the account could not
  already reach. Its idempotency check counts explicit rules only, which is harmless.
- **The write-path syscall numbers.** The refused numbers and the `0x243` open-flag test are right
  on both architectures, and the BPF jump offsets are right.
- **ARCHITECTURE.md and CONTRIBUTING.md.** Both match the code, apart from the overstatements
  noted in R1-1 and R1-4.

### Findings

**Blocking:** none.

**Major**

- **R1-1. The filter's `ioctl` deny-list leaves ioctls that change a file through a read-only
  descriptor.**
  - **Where:** `src/core/Tellma.Core.Imaging.PdfWorker/SeccompFilter.cs:226`.
  - **The gap:** the list refuses `FS_IOC_SETFLAGS`/`SETVERSION`/`FSSETXATTR` but not
    `FS_IOC_ENABLE_VERITY` (`0x40806685`). That ioctl needs only an `O_RDONLY` descriptor plus DAC
    write permission (`file_permission(MAY_WRITE)`). Landlock governs `ioctl` only on devices, and
    it has no `file_permission` hook.
  - **Failure scenario:** the host runs under an account that owns its installation, as a
    systemd-style deployment does (not the root-owned `/app` of the .NET images). It runs on a
    filesystem with fs-verity: btrfs needs no mkfs flag, ext4 and f2fs need the feature. A
    compromised worker opens one of its allowance's files (a host assembly, `.deps.json`,
    `.runtimeconfig.json`) read-only and enables verity on it. The file then refuses every later
    open for writing, for good. The next in-place deploy over it fails, and only deleting the file
    undoes it.
  - **Stated guarantee broken:** this is the same immutability that `FS_IOC_SETFLAGS` was refused
    for. It contradicts "no change to any file, whatever the kernel's Landlock governs"
    (`ARCHITECTURE.md`, the READMEs) and D-040 (2).
  - **Related ioctls:** `FS_IOC_SET_ENCRYPTION_POLICY` (`0x800C6613`, an owned empty directory) and
    `BTRFS_IOC_SUBVOL_SETFLAGS` (`0x4008941A`, which checks no `FMODE_WRITE`) are in the same class.
  - **Fix:** refuse by ioctl type rather than by request. `(request >> 8) & 0xFF` equal to `0x66`
    (`'f'`), `0x58` (`'X'`) or `0x94` (btrfs) returns `EPERM`. The worker needs none of them;
    `FIONREAD`, `FIOCLEX`, `FIONBIO` and `TCGETS` are type `'T'`. At the least, add the three
    requests above. Then extend `SeccompFilterTests` to cover them.

**Minor**

- **R1-2. Nothing tests that the client refuses a confined launcher's worker that does not report
  `FilesystemConfinement`.**
  - **Where:** `src/core/Tellma.Core.Imaging/Workers/PdfWorkerClient.cs:104-107`.
  - **The gap:** this check is what makes the new capability bit mean anything, and the
    implementer notes and `A_confined_worker_still_renders` rely on it. Deleting it fails no test.
  - **Fix:** add a stand-in mode that writes a ready frame with `SystemCallFilter` only, after it
    has started over confined on Linux, or as is on Windows. Assert that a `confine: true` launcher
    gets `PdfWorkerException(Protocol)` from it, while `confine: false` accepts it.

- **R1-3. A Windows Development host whose .NET runtime is not under Program Files can no longer
  render.**
  - **Where:** `src/core/Tellma.Core.Imaging/Workers/WorkerAllowance.cs:124`, and `ExitFix` at
    `src/core/Tellma.Core.Imaging/ImagingStartupCheck.cs:322`.
  - **The gap:** `WindowsFiles` leaves out everything beneath `files.Root`, relying on the
    installer's `ALL APPLICATION PACKAGES` grant. A runtime installed by `dotnet-install.ps1` into
    `%USERPROFILE%\.dotnet` or a repo-local `.dotnet` has no such grant. The AppContainer then
    cannot load `hostfxr` or the shared framework, and every worker exits.
  - **Effect:** `core.imaging` fails the Development host's startup. The fix text shown is
    `ExitFix`, which names only the Linux causes. `StartFix`, which mentions Program Files, applies
    only to a start failure. Before PC-F11 such a host rendered. This is by reading; I did not
    exercise it.
  - **Fix:** at launcher construction, check whether `Root\host\fxr` grants `ALL APPLICATION
    PACKAGES` read, from its DACL.
    - If it does not, either grant the capability on the `host` and `shared` trees, or, since
      Windows is Development-only, start unconfined with the `BlobPdfWorkerUnconfined` warning
      and a detail that says why.
    - Either way, name the requirement in `ExitFix` and in the README's "What a worker may read".

- **R1-4. The inotify residual can be closed cheaply, and the xattr reads are not recorded.**
  - **Where:** `src/core/Tellma.Core.Imaging.PdfWorker/SeccompFilter.cs:149-195`, and the Imaging
    README "Residual risks" (`:266-269`).
  - **Measured:** under a Landlock domain that handles every filesystem right and grants none,
    `open("/etc/passwd")` fails, but `inotify_add_watch` on `/etc/passwd` and on `/etc` succeeds.
    `listxattr` by path also works. `getxattr` goes through the same ungoverned path; I did not
    run it.
  - **Effect:** a compromised worker can time the host's file accesses and reads anywhere, and can
    read any `user.*` xattr payload.
  - **Fix:** the worker needs none of these calls.
    - Refuse `inotify_init`, `inotify_init1`, `inotify_add_watch`, `fanotify_init` and
      `fanotify_mark` (x64 253/294/254/300/301; arm64 26/27/262/263).
    - Refuse the path forms `getxattr`, `lgetxattr`, `listxattr`, `llistxattr`, `getxattrat` and
      `listxattrat` (x64 191/192/194/195/464/465; arm64 8/9/11/12/464/465). Keep the `f*`
      descriptor forms in case the runtime needs one.
    - Otherwise record xattrs beside `stat` in the README and `ARCHITECTURE.md`.
    - `stat`, `access` and `readlink` stay. Closing those needs a mount namespace, which is not
      cheap.

- **R1-5. `close_range` comes through glibc's wrapper, which needs glibc 2.34.**
  - **Where:** `src/core/Tellma.Core.Imaging.PdfWorker/WorkerConfinement.cs:383-386`.
  - **The gap:** a host on a kernel that offers Landlock but with an older glibc (Debian 11 with a
    backports kernel, SLES 15 SP4/SP5 at glibc 2.31) passes the policy, because `CanConfine` is
    true.
  - **Effect:** every worker then throws `EntryPointNotFoundException` in stage 1, and
    `core.imaging` fails startup. That is fail-closed, but avoidable.
  - **Fix:** call it through `syscall` with number 436 (the same on x64 and arm64), as `Landlock`
    already does for its calls.

- **R1-6. Nothing checks that the host's own directory lies outside every read-beneath
  directory.**
  - **Where:** `src/core/Tellma.Core.Imaging/Workers/WorkerAllowance.cs:139-144`.
  - **The gap:** the system directory is wherever libc is mapped from. That is `/usr/lib` on Arch,
    and on distributions that put 64-bit libraries there. The installation's `host` and `shared`
    directories and the font directories are also granted beneath.
  - **Effect:** a host installed beneath any of them, such as `/usr/lib/<app>/`, which is a common
    packaging layout, hands its `appsettings*.json` to the worker. That contradicts "none of the
    host's configuration or secrets".
  - **Fix:** in `Compose`, if the directory of `files.RuntimeConfiguration` (or
    `AppContext.BaseDirectory`) is beneath any `ReadBeneath` entry, either fail with a
    `PdfWorkerException(Start)` that names the layout, or replace that entry with per-file rules.
    Add a `Compose` unit case for it.

- **R1-7. No one has verified that the production target offers Landlock.**
  - **Where:** `ARCHITECTURE.md`, "Hosting on Azure"; the policy's `DisabledWithoutLandlock`.
  - **The gap:** before PC-F11, every glibc Linux host rendered PDF thumbnails. Now an App Service
    Linux instance without Landlock, or whose sandbox's seccomp profile refuses the calls, renders
    none in production, and says so only in a log line. `ARCHITECTURE.md` already says the App
    Service path is unverified, but no task tracks it.
  - **Fix:** record a staging task: check `/sys/kernel/security/lsm` and the startup log on the
    first App Service deployment. If Landlock is absent there, raise it with Ahmad before GA.

### Responses

The orchestrator ruled on every finding before this round (D-204). This round follows those
rulings. Runs, on the final tree:

- Windows: `dotnet build` of `Tellma.Core.Imaging.Tests` (Build succeeded, 0 warnings), then the
  `Workers`, `Thumbnails`, `Composition` and `Packaging` namespaces: 394 total, 0 failed, 21
  skipped (Linux-only).
- Linux: `mcr.microsoft.com/dotnet/sdk:10.0` on Docker Desktop's WSL2 kernel, with
  `TELLMA_REQUIRE_LANDLOCK=1`, the whole Imaging test assembly: 501 total, 0 failed, 3 skipped
  (Windows-only).

- R1-1: fixed in 9446fd17.
  - **The refusal.** The filter now refuses every `ioctl` of types `'f'` (0x66), `'X'` (0x58) and
    `0x94`, whatever the request, by masking the request's type byte (a BPF `AND` of the low word
    of argument 1 with `0xFF00`). Reads of those types are refused too. It also refuses `0xF5`,
    f2fs's type, which the ruling did not name; it falls in the same class (pinning a file, atomic
    writes and compression through a descriptor). `FS_IOC_SETVERSION` (`'v'`) stays refused by
    request. The type-wide rule covers `FS_IOC_SETFLAGS` and `FS_IOC_FSSETXATTR`, which were
    refused by request before, so they left the request list.
  - **The stated check.** Rendering and the runtime still work with every one of these types
    refused. The Linux run renders every PDF thumbnail suite of the assembly (the hostile
    documents, the budgets, the composition and the smoke host) through confined workers under
    this filter, with 0 failures. No type had to be kept, so the fallback (refusing only the three
    named requests) was not needed.
  - **Tests.**
    - `SeccompFilterTests.Ioctl_makes_no_request_of_the_families_that_change_a_files_attributes`
      runs the filter's program in its BPF interpreter on both architectures. It checks the three
      named requests, `FICLONE`, `FIDEDUPERANGE` and `F2FS_IOC_SET_PIN_FILE`, with and without a
      high half. It checks every number 0-255 of each refused type in five direction and size
      forms. It checks that neighbouring types (`'T'` among them) are allowed. The interpreter
      gained the `AND` instruction.
    - The stand-in's `files` probe now tries `FS_IOC_GETFLAGS`, `FS_IOC_ENABLE_VERITY`,
      `FS_IOC_FSGETXATTR` and `FICLONE` on a descriptor of its own assembly opened only for
      reading. On Linux the confined worker gets `EPERM` for all four. Under Landlock alone,
      before the filter, the kernel answers each: `done`, `EOPNOTSUPP` (no verity on that
      filesystem), `done` and `EBADF`. That shows the filter, not Landlock, refuses them.
  - **Docs.** The "no change to any file" wording now states what is refused: the calls that change
    a file, and the `ioctl` types above. Another filesystem's own `ioctl` types on a file of the
    allowance rest on that filesystem's checks, and that is recorded as residual in the Imaging
    README and `ARCHITECTURE.md`. The worker README, the Imaging README and the filter's XML docs
    match.
- R1-2: fixed in 9446fd17.
  - The stand-in has a new mode, `ready-unconfined`. It masks `FilesystemConfinement` out of its
    ready frame, whether or not it is confined, and then answers with a good page.
  - `WorkerConfinementTests.A_worker_its_launcher_confines_that_does_not_report_its_confinement_is_refused`
    covers it. A confining launcher gets `PdfWorkerException` with `Protocol`. A `confine: false`
    launcher takes the same worker's 64x64 page.
  - It passes on Windows (AppContainer) and on Linux (Landlock). With the client's check disabled
    it fails on Windows ("No exception was thrown"); I restored the check afterwards.
- R1-3: fixed in 9446fd17.
  - **The grant.** On Windows, `PdfWorkerLauncher.CanConfine` now asks
    `WindowsAppContainer.GrantRuntime(muxer)` once, lazily. It grants the workers' capability read
    and execute on the muxer and on the installation's `host` and `shared` directories, with
    inheritance, so what lies beneath them is covered. A directory that already lets every
    AppContainer or the capability read and execute it, and what lies beneath it, is left alone.
    `Grants` accepts both forms an access list holds: the split entries of Program Files (`RX` on
    the folder, and inherit-only generic read and execute), and a single inheritable entry. A
    Program Files installation is therefore never touched.
  - **The fallback.** When the account cannot change those permissions, `CanConfine` is false. The
    launcher's `Confines` is false then, and the policy runs Development workers unconfined. The
    check's `BlobPdfWorkerUnconfined` warning names the cause through the new
    `PdfWorkerLauncher.Unconfinable`: the installation's path, and the error of the grant that
    failed.
  - **Laziness.** `Confines` is now evaluated when first asked, not when the launcher is made, so
    a host that starts no worker never grants anything. A Windows production host renders no PDF
    thumbnails.
  - **Messages.** `ExitFix` now names the Windows causes: what the AppContainer must read, which
    entries grant it, and that `icacls` lists them. `StartFix` names the grant and the fallback.
    The Imaging README's "What a worker may read" and the worker README match.
  - **Test.**
    `WorkerConfinementTests.A_directory_granted_to_the_workers_appcontainer_is_read_by_a_confined_worker_and_still_changed_by_none_on_windows`
    starts from a temporary directory that grants no AppContainer anything, as an installation in
    a user's profile does not. After `GrantReadAndExecute`, a confined worker reads the file and
    lists the directory, every other attempt is still refused, and nothing changed. It also checks
    that this machine's own installation needs nothing it cannot grant.
  - **Not exercised.** A host on an installation in `%USERPROFILE%\.dotnet` was not run end to
    end, because this machine's runtime is under Program Files. Neither was the fallback branch,
    where the account cannot change the installation's permissions.
- R1-4: fixed in 9446fd17.
  - **Refused.** The filter refuses `inotify_init`, `inotify_init1`, `inotify_add_watch`,
    `fanotify_init` and `fanotify_mark`, and the path forms `getxattr`, `lgetxattr`, `listxattr`,
    `llistxattr`, `getxattrat` and `listxattrat`. The numbers are those of the finding on both
    architectures; arm64 has no `inotify_init`. The runtime and PDFium need none of them: the whole
    Linux run passes with all of them refused.
  - **Kept.** `fgetxattr` and `flistxattr` stay open. They need a descriptor that Landlock already
    let the worker open.
  - **Tests.** `SeccompFilterTests` lists each call as refused on both architectures. The `files`
    probe now tries `watch` (inotify on the victim), `notify` (fanotify), `get-xattr` and
    `list-xattr`.
    - Confined, the worker gets `EPERM` for all four.
    - Under Landlock alone, `inotify_add_watch` succeeds, as the reviewer measured. `getxattr`
      reaches the file (`ENODATA`), and `listxattr` succeeds. That confirms the gap was real.
    - `fanotify_init` is `EPERM` there too: the container or the kernel refuses it to a process
      without `CAP_SYS_ADMIN`. The filter refuses it whatever either allows.
  - **Residual.** `stat`, `access` and `readlink` stay open. A worker can learn that a path
    exists, and its size, owner, times and link target. Closing that needs a mount namespace. It
    is recorded here, in the Imaging README's "Residual risks", and in `ARCHITECTURE.md`.
- R1-5: fixed in 9446fd17. `close_range` is now called as
  `syscall(436, 3, ~0U, CLOSE_RANGE_CLOEXEC)` through libc's `syscall` entry point, which every
  glibc has. The number is the same on x64 and arm64. Every confined Linux worker in the run takes
  this path, and every one passes. No host on glibc older than 2.34 was run.
- R1-6: fixed in 9446fd17.
  - **The guard.** `WorkerAllowance.Compose` refuses, with `PdfWorkerException(Start)`, a host
    whose runtime configuration or dependency manifest lies beneath a directory the worker reads
    in full: the runtime's `host` and `shared` directories, the system's library directory, or a
    font directory. The message names the directory and the fix. The startup check reports it as
    a start failure.
  - **Tests.**
    `PdfWorkerLauncherTests.A_host_installed_beneath_a_directory_a_worker_reads_in_full_cannot_have_its_workers_confined`
    covers five layouts: the library directory itself, beneath it, beneath a font directory,
    beneath `shared`, and a Windows path that differs in case.
    `A_host_beside_a_directory_a_worker_reads_in_full_but_not_beneath_it_has_its_workers_confined`
    covers a sibling whose name only starts with the library directory's.
  - **Recorded, not guarded.** A Windows worker also reads whatever grants every AppContainer read,
    and Program Files does so throughout. A Development host installed under Program Files
    therefore hands its configuration to its workers. I tried an access-list check that refused
    such a host, then took it out: R1-3's ruling is that a Windows Development host must not fail
    to start where it rendered before, and the alternative, running unconfined, reads more. It is
    recorded in the Imaging README and `ARCHITECTURE.md`. A build's output and a user's profile
    grant AppContainers nothing.
- R1-7: no code change, as ruled. The orchestrator records the App Service Linux Landlock check as
  a merge-time task for Ahmad.

## Review 2

Scope: commits c4a8cac1 and 9446fd17 together (the tree at 25cd8cb3), against the LEDGER PC-F11
row, D-030 (addenda 3-5), D-040 (2), D-043 (2)-(4), D-195 and D-204. PC-F11 is a fix item, so
CHECKPOINTS.md has no entry for it; the LEDGER row and those decisions are the scope.

Runs by the reviewer, all on 25cd8cb3:

- Windows: `dotnet build` of `Tellma.Core.Imaging.Tests` (Build succeeded, 0 warnings), then its
  `Workers` and `Composition` namespaces: 295 total, 0 failed, 21 skipped (Linux-only).
- Linux: `mcr.microsoft.com/dotnet/sdk:10.0` on Docker Desktop's WSL2 kernel 6.18 (Landlock ABI 7),
  from `git archive` in a volume of its own, `TELLMA_REQUIRE_LANDLOCK=1`, the same namespaces: 295
  total, 0 failed, 2 skipped (Windows-only). The container runs as root.
- Linux, the same image, three scratch programs:
  1. A thread restricted under a ruleset that handles every right and grants none, then `mq_open`,
     and `fgetxattr`, `flistxattr`, `fcntl(F_NOTIFY)` and `flock` on an `O_PATH` descriptor (R2-1).
  2. The same with the worker's one rule on `/proc/self`, then opens under `/proc/self/net` and of
     `mountinfo`, and `mq_unlink` of a queue made before the restriction (R2-1, R2-3).
  3. A console application built under `/usr/lib/x86_64-linux-gnu` and started through `/lib/...`
     and through a symbolic link to its directory, printing `APP_CONTEXT_DEPS_FILES` (R1-6's guard).
- CI: both pc/PC-F11 pushes show as skipped (CI runs at integration), so there is no run to cite.

Checked, with no finding:

- **The `ioctl` type filter.** The mask is `0xFF00` on the low word of argument 1, which is where
  the kernel reads the request's type. I recomputed every jump of the block: a refused request
  skips `requests - index + types` to the refusal, a refused type `types - 1 - index`, the last
  type steps over the refusal to the allowance, and any other call skips `requests + types + 4`.
  All land where they should. `FIONREAD`, `FIOCLEX`, `FIONBIO` and the terminal calls are type
  `'T'` and stay allowed. Types `'f'`, `'X'`, `0x94` and `0xF5` cover ext4, XFS, Btrfs, f2fs and
  overlayfs (which passes the `'f'` and `'X'` attribute calls down). The residual for other
  filesystems is stated in both READMEs and `ARCHITECTURE.md` as it is.
- **The refused numbers.** I checked every number added in fix1 against both tables, the x64 and
  the arm64: inotify, fanotify, the path forms of the xattr reads, and the earlier write-path
  calls. All are right.
- **The xattr descriptor forms that stay open.** `fgetxattr` and `flistxattr` answer `EBADF` on an
  `O_PATH` descriptor (measured), and a readable descriptor needs Landlock's leave. So they give
  nothing the path forms were refused for. `fcntl(F_NOTIFY)` and `flock` on an `O_PATH`
  descriptor are `EBADF` too, so a directory the worker cannot list cannot be watched that way.
- **`close_range` as call 436.** The arguments match the kernel's (`unsigned`, `unsigned`,
  `unsigned`), and `CLOSE_RANGE_CLOEXEC` (5.11) predates Landlock (5.13).
- **The restart.** Stage 1 reads nothing of the upload. The command line comes from
  `/proc/self/cmdline` and is checked against the worker's own arguments. No allowance path or
  `--close-fd` target can be taken for an argument name: paths must start with `/`, and names
  match whole. Stage 1 drops the `--close-fd` triples because every descriptor from 3 up is marked
  close-on-exec, which is stronger.
- **The Compose guard.** The comparison is by path segment, so `/usr/lib64/app` is not beneath
  `/usr/lib`, and the test pins that. The guard compares text, not inodes, but the muxer resolves
  symbolic links before it names the host's files: started through `/lib/...` or through a link to
  its directory, the application reported its `.deps.json`, base directory and assemblies under
  the resolved `/usr/lib/x86_64-linux-gnu/...` (measured). The kernel reports libc's path resolved
  too. So a linked layout does not get past the guard.
- **Production confinement.** The policy and the launcher read the same cached answer. On Windows
  the policy never asks `CanConfine` (`linux &&` comes first), and the startup check returns
  before `Confines` on a host that renders none. So a Windows production host changes no access
  list.
- **The ready-unconfined refusal.** The test passes on both systems here, and R1 saw it fail with
  the client's check removed.
- **The Windows fallback.** It is reached only when the runtime grant throws, and only a
  Development host renders then. An unconfined Windows worker is what every Windows worker was
  before this checkpoint.
- **The runtime ACE.** It grants read and execute only, to the one capability, on the muxer and
  the `host` and `shared` directories. It persists by design, and the READMEs say so.
- **Residual statements.** `stat`, `access`, `readlink`, other filesystems' `ioctl`s, shared
  memory and the Windows reads are stated as they are. Two statements are not accurate; see R2-2
  and R2-3.

Review 1's findings, as they now stand:

- R1-1, R1-2, R1-4, R1-5 and R1-6 are closed. I found no way around any of them.
- R1-3 is closed for the runtime's installation, by reading; the implementer did not run it end
  to end and says so. The same failure remains for the host's own files (R2-2).
- **The pushback on a Development host under Program Files** (recorded, not guarded) holds in
  part. Not refusing such a host is right: Windows is Development-only, and before this checkpoint
  the worker read everything the account could. But the README's claim that it "is not refused" is
  true only when the account may rewrite those files' access lists, and a warning would cost
  nothing where a refusal was ruled out. R2-2 covers both.

### Findings

**Blocking:** none.

**Major:** none.

**Minor**

- **R2-1. The filter leaves POSIX message queues open, and `flock`.**
  - **Where:** `src/core/Tellma.Core.Imaging.PdfWorker/SeccompFilter.cs:158-210` (the refused
    calls), and the worker README's "SysV shared memory, semaphores and message queues" line
    (`src/core/Tellma.Core.Imaging.PdfWorker/README.md:118`).
  - **Measured.** Under a Landlock domain that handles every right and grants none:
    - `mq_open(name, O_CREAT|O_RDWR)` answers `EACCES`, but the queue is created first and stays
      in `/dev/mqueue` after the process exits. The seccomp open-flag test does not see it: it is
      another call (x64 240, arm64 180).
    - `mq_unlink` (x64 241, arm64 181) of a queue the account already had succeeds.
  - **Failure scenario.** A compromised worker leaves named kernel objects that outlive it. Each
    is charged to the account's `RLIMIT_MSGQUEUE`, so about nine exhaust the default for the
    host's account until a reboot. It can also delete any queue another process of the account
    uses. This is what the filter refuses SysV objects for, and it contradicts "it may write,
    create, rename or delete nothing".
  - **`flock`, by reading.** `flock(LOCK_EX)` needs no write access, so a worker can lock every
    file of its allowance exclusively for as long as it lives. .NET's `FileStream` takes a shared
    `flock` on every open and throws a sharing violation on a conflict. So a .NET process that
    opens one of those files meanwhile fails: the host reading its `.deps.json`, or a second
    instance starting on the same files. It ends with the worker, and a new upload starts another.
  - **Fix.** Add `mq_open`, `mq_unlink`, `mq_timedsend`, `mq_timedreceive`, `mq_notify` and
    `mq_getsetattr` (x64 240-245, arm64 180-185) and `flock` (x64 73, arm64 32) to the refused
    calls. Check that the Linux suite still renders: .NET ignores a failed `flock` unless the
    error is `EWOULDBLOCK`. Add `mq-create` and `lock` attempts to the `files` probe, and the
    numbers to `SeccompFilterTests`. If `flock` must stay, record it as a residual.

- **R2-2. A Windows host's own files have no fallback, and files that already grant every
  AppContainer read are still rewritten.**
  - **Where:** `src/core/Tellma.Core.Imaging/Workers/WindowsAppContainer.cs:179-215` (`GrantRead`),
    and the Imaging README (`src/core/Tellma.Core.Imaging/README.md:209-212`).
  - **The gap.** `GrantRead` looks only for an explicit entry for the capability. It does not use
    `Grants`, which `GrantReadAndExecute` uses and which accepts an inherited entry and
    `ALL APPLICATION PACKAGES`. A file that lacks the explicit entry gets `SetAccessControl`, and
    a failure there is a `PdfWorkerException(Start)`, with no fallback.
  - **Failure scenarios**, by reading:
    - A Development host under Program Files, run by an account that is not elevated. Its files
      already grant every AppContainer read, yet the host tries to add an entry, is denied, and
      `core.imaging` fails startup. The README says such a host "is not refused". Elevated, it
      starts and stamps every file.
    - A build made from an elevated shell outside the user's profile and run unelevated. The
      account can modify the files but does not own them, so it cannot change their access lists.
  - **Why it matters.** Both hosts rendered before PC-F11. D-204's ruling on R1-3 is that a
    Windows Development host must not fail startup where it rendered before. Fix1 applied that to
    the runtime's installation only.
  - **Also.** `GrantRuntime` and `GrantRead` catch `UnauthorizedAccessException`, `IOException`
    and `PrivilegeNotHeldException`. The access-control classes also throw
    `InvalidOperationException` and `NotSupportedException`, for a volume without access lists,
    say. Either escapes `CanConfine` unwrapped. `Lazy` then caches it, and the startup check's
    `_launcher.Confines` (`ImagingStartupCheck.cs:129`) rethrows it outside the probe's handling.
    Not exercised.
  - **Fix.**
    - In `GrantRead`, skip a file that `Grants` already accepts.
    - Treat a failed grant on the host's own files like a failed runtime grant: `CanConfine`
      false with the cause in `Unconfinable`, so a Development host runs unconfined with the
      warning. A sturdier form is to start one confined worker when `CanConfine` is first asked,
      and fall back when it does not reach its ready frame. That also covers a `Grants` that is
      wrong about what lies beneath a directory.
    - Catch the wider exception types in both methods.
    - For the Program Files host: log a warning when `AppContext.BaseDirectory` grants every
      AppContainer read, and correct the README's sentence.
    - `ImagingLog.PdfWorkerUnconfined`'s summary (`ImagingLog.cs:104-108`) names only Linux
      without Landlock and macOS; add the Windows cause.

- **R2-3. A worker's own `/proc` entries tell more than the residuals say.**
  - **Where:** `src/core/Tellma.Core.Imaging.PdfWorker/WorkerConfinement.cs:143-149` (the rule on
    `/proc/self`), and the Imaging README's residual risks
    (`src/core/Tellma.Core.Imaging/README.md:270-290`).
  - **Measured.** With that one rule, a restricted process opens `/proc/self/net/tcp`,
    `/proc/net/tcp`, `/proc/self/net/arp`, `/proc/self/net/unix` and `/proc/self/mountinfo`, while
    `/proc/1/status` is `EACCES`.
  - **Effect.** Those files describe the namespaces the worker shares with its host, not the
    worker. A compromised worker can read the address and port of every TCP connection the host
    holds (its SQL Server, its cache, its identity provider), the neighbours and Unix socket
    paths, and the mount table, which on a share names the storage account. None is a credential.
    It is more than the "existence, sizes, owners, times and link targets" the residuals list, and
    the README says the own entries hold only the worker's own.
  - **Fix.** Landlock cannot carve these out of a directory it grants. Record them beside `stat`
    in the Imaging README and `ARCHITECTURE.md`. Closing them takes a network and mount namespace
    of the worker's own, which is the same work as closing `stat`.

- **R2-4. Three rules have no test that would fail if they broke.**
  - **Where:** `WorkerConfinement.TakeConfined` (`WorkerConfinement.cs:226-246`),
    `PdfWorkerLauncher.CanConfine` and `Unconfinable` (`PdfWorkerLauncher.cs:61-85`), and
    `WindowsAppContainer.Grants` (`WindowsAppContainer.cs:141-160`).
  - **The gaps.**
    - Stage 2's two checks: the anchor is the worker's own `/proc/<pid>`, and `/` is closed.
      Removing either fails nothing, because every test that passes `--confined` is really
      confined.
    - The Windows fallback: a failed runtime grant makes `CanConfine` false and the warning names
      the installation. The implementer notes say this branch was not run.
    - `Grants` is tested only through this machine's Program Files and one directory the test
      grants itself. Nothing pins an inherit-only entry alone (not the directory itself), an
      entry with `NoPropagateInherit`, or generic rights.
  - **Fix.**
    - On Linux, start the real worker unconfined with `--confined 3` appended and assert exit 65
      with the anchor message.
    - Unit-test `Grants` over `DirectorySecurity` objects built in memory, one per form.
    - Give `GrantRuntime` a seam for the grant, so a test can make it throw and assert
      `Unconfinable` and the warning's detail.

### Responses

The orchestrator ruled on every finding before this round (D-207). This round follows those
rulings. Runs, on the final tree:

- Windows: `dotnet build Tellma.slnx` (Build succeeded, 0 warnings), `dotnet format Tellma.slnx
  --verify-no-changes` (clean), then the `Workers`, `Composition` and `Thumbnails` namespaces of
  `Tellma.Core.Imaging.Tests`: 408 total, 0 failed, 28 skipped (Linux-only).
- Linux: `mcr.microsoft.com/dotnet/sdk:10.0` on Docker Desktop's WSL2 kernel, with
  `TELLMA_REQUIRE_LANDLOCK=1`, the same namespaces: 408 total, 0 failed, 27 skipped
  (Windows-only).

- R2-1: fixed in the review 2 fixes commit.
  - **Refused.** The filter refuses `mq_open`, `mq_unlink`, `mq_timedsend`, `mq_timedreceive`,
    `mq_notify` and `mq_getsetattr` (x64 240-245, arm64 180-185), and `flock` (x64 73, arm64 32).
    `SeccompFilterTests` lists every one as refused on both architectures.
  - **The `flock` measurement.** A new stand-in mode, `file-streams`, runs under the filter. It
    calls `flock` on its own assembly, shared and exclusive, then opens it as .NET does: a
    `FileStream` with `FileShare.Read` (which takes `LOCK_SH`), one with `FileShare.None`
    (`LOCK_EX`), and `File.ReadAllBytes`. On Linux both locks answer `EPERM`, and all three opens
    succeed and read. .NET treats only `EWOULDBLOCK` as a sharing violation, as expected. The
    runtime's own opens and PDFium's run under the same filter in every confined test of the run,
    the hostile documents and the budgets among them, with 0 failures. So `flock` is refused, and
    no sharing-violation residual remains. `fcntl`'s record locks stay open: .NET takes none on
    opening a file. That is stated in the worker README.
  - **Probes.** The `files` probe adds three attempts:
    - `mq-create`: `mq_open` with `O_CREAT|O_EXCL`, which always unlinks the queue afterwards,
      since Landlock lets the kernel make it before refusing the open;
    - `mq-unlink`;
    - `lock`: `flock(LOCK_EX)` on the worker's own assembly, opened for reading.

    Confined, all three are `EPERM`. Under Landlock alone, before the filter, `lock` is `done`,
    which shows the filter is what refuses it.
  - **Docs.** Updated to match: the worker README (the POSIX queues beside SysV, and a "Locking a
    file" bullet), the Imaging README, `ARCHITECTURE.md` and the filter's XML docs.
- R2-2: fixed in the review 2 fixes commit.
  - **The host's own files.** `PdfWorkerLauncher.CanConfine` on Windows now asks
    `WindowsBlocker`. It grants the runtime as before, then the host's own files of the PDF
    worker's allowance, through the new `WindowsAppContainer.GrantRead(files, grant)`.
    - When either grant fails, `CanConfine` is false. `Unconfinable` names the installation, or
      the first host file that could not be granted, with the error. A Development host then runs
      its workers unconfined and logs `BlobPdfWorkerUnconfined` with that cause; startup does not
      fail.
    - A file `Grants` already accepts (every AppContainer, or the capability, by an inherited
      entry or an explicit one) is left alone. So an unelevated host under Program Files writes
      nothing.
    - `GrantRead(files)`, at a worker's start, keeps its `Start` failure, for what `CanConfine`
      did not cover: a launcher told to confine, or one starting another worker, as tests do.
      The default launcher has already passed through the fallback by then.
  - **The exceptions.** One predicate, `IsGrantFailure`, covers what reading or changing an
    access list throws, in `GrantRuntime`, `GrantRead` and `ReadsInFull`:
    `UnauthorizedAccessException` (with `PrivilegeNotHeldException`), `IOException`,
    `InvalidOperationException`, `NotSupportedException` and `PdfWorkerException` (the capability
    not derived). `WindowsBlocker` returns null when a file is missing, so starting a worker
    reports that. The `Lazy` behind it therefore never throws and never caches a failure. The
    startup check now reads the launcher's new `Unconfined` (the reason, or null), so it no longer
    reads `Confines` and `Unconfinable` separately.
  - **Program Files.** The README's "is not refused" sentence now says why such a host is not
    refused. The `core.imaging` check logs a new warning, `BlobPdfWorkerReadsHostDirectory`,
    naming the directory, when the host's directory lets every AppContainer (or the capability)
    read what lies beneath it. `PdfWorkerUnconfined`'s summary names the Windows cause.
    `ARCHITECTURE.md` matches.
  - **The probe-start form was not taken.** The ruling prescribes the grant-based fallback, and
    `Grants` is now pinned form by form (R2-4). A worker that cannot read its files still fails
    the startup probe with `ExitFix`'s Windows causes, never silently.
  - **Tests.**
    - `WindowsAppContainerTests`: the fallback for the installation and for the host's own files,
      the message of each grant failure type, success when every grant is made, a file that
      already grants every AppContainer left alone, and `ReadsInFull`.
    - In `ImagingCompositionTests`:
      - `A_windows_host_whose_account_may_not_grant_its_workers_read_runs_them_unconfined_and_says_why`
        starts and logs one `BlobPdfWorkerUnconfined` naming the installation and the refusal.
      - `A_windows_host_in_a_directory_every_appcontainer_reads_says_its_workers_read_its_configuration`
        logs one `BlobPdfWorkerReadsHostDirectory`.
  - **Not exercised.** An unelevated host under Program Files, and a build owned by another
    account, were not run end to end. Their branches are driven through the grant seam.
- R2-3: fixed in the review 2 fixes commit, with no code change, as ruled. The worker's own
  `/proc/<pid>` entries that describe the network and mount namespaces it shares with its host
  (`net/tcp`, `net/tcp6`, `net/udp`, `net/unix`, `net/arp`, `mountinfo`) are recorded as a
  residual beside `stat`, `access` and `readlink`. They appear in:
  - `ARCHITECTURE.md`;
  - the Imaging README, both under "What a worker may read" and as a new "What its own `/proc`
    entries tell of its host" residual;
  - the worker README;
  - the XML docs of `SeccompFilter` and `WorkerConfinement`.

  The record states what they show (the host's connections and Unix sockets, its neighbours, the
  mount table), that none is a credential, and that closing them takes namespaces of the worker's
  own.
- R2-4: fixed in the review 2 fixes commit.
  - **Stage 2's checks, on Linux.**
    - In process: `PdfWorkerLauncherTests.The_second_stage_refuses_an_anchor_that_is_not_the_workers_own_proc_directory_on_linux`
      passes an `O_PATH` descriptor of `/tmp`.
    - In process: `The_second_stage_refuses_a_process_the_root_directory_is_open_to_on_linux`
      passes the process's own `/proc/self`, with `/` still open to it.
    - In process: `A_malformed_second_stage_argument_is_refused_on_linux` covers a descriptor below
      3, a negative one, and a non-number.
    - End to end: `WorkerConfinementTests.A_worker_told_it_was_started_over_confined_when_it_was_not_refuses_to_run_on_linux`
      starts the real worker unconfined with `--confined 3`, and asserts exit 65 with the anchor
      message.

    Removing either check fails one of these.
  - **The Windows fallback.** `PdfWorkerLauncher` has a new `unconfinable` seam, and
    `WindowsBlocker` takes the grant as a parameter. With them, the tests above drive a failed
    grant on the installation and on a host file. They assert `Unconfinable`'s text and the
    warning's detail.
    `A_launcher_confines_its_workers_where_the_operating_system_can_and_says_why_not_where_it_cannot`
    pins `Confines` and `Unconfined` for `confine: true`, `confine: false` and the seam.
  - **`Grants`.**
    `WindowsAppContainerTests.Only_an_access_list_that_lets_every_appcontainer_or_the_capability_read_and_execute_counts`
    builds 13 access lists in memory and checks each as a directory's and as a file's.
    - Accepted: the Program Files split, a single `OICI` entry with `RX` or `GA`, and the
      capability.
    - Refused: the folder alone (a file only), inherit-only alone, `NP`, `CI` and `OI` alone,
      `GR`, `FR`, a deny, and another trustee.
