# Tellma.Core.Imaging.TestWorker

A **test asset** (not an xunit project): a stand-in for the
[PDF worker](../../../../src/core/Tellma.Core.Imaging.PdfWorker/README.md) that misbehaves on
purpose, one way per mode, for the imaging suite. It enters the real worker's sandbox exactly as the
real worker does and is started the way the processor starts the real one (`dotnet exec` with the
suite's runtime configuration and dependency manifest, confined to the allowance the launcher
computes for it), so every misbehaviour happens under the very sandbox, filesystem confinement and
operating-system limits a real worker gets.

| Mode | Does |
|---|---|
| `exit-before-ready`, `crash-before-ready`, `hang-before-ready`, `bad-ready` | fails before its ready frame |
| `exit-after-request`, `crash-after-request`, `fail-fast-after-request`, `hang-after-request`, `hang-ignoring-input`, `spin` | fails, hangs (exiting when its input ends as the real worker does, or ignoring its input so that only the operating system can end it) or burns CPU after reading the request |
| `unknown-status`, `oversized`, `truncated`, `stray-bytes` | breaks the result frame |
| `not-png`, `wrong-size`, `extra-chunk`, `corrupt-png` | answers with a malformed or deceptive image |
| `good`, `failed`, `real` | answers with a blank page, reports a failure, or runs the real worker |
| `ready-unconfined` | leaves its filesystem confinement out of its ready frame, whether or not it is confined, then answers with a blank page |
| `allocate-native`, `allocate-managed`, `map-shared` | takes and touches memory in 16 MiB blocks until refused (native allocations, managed arrays, and on Linux shared anonymous mappings, which only the address-space limit counts) and reports how much it got |
| `socket`, `process`, `environment`, `write-file <directory>` | tries to open a socket, start a process or write a file in the directory, or lists its environment |
| `host-access`, `capabilities`, `open-host`, `open-process <pid>` | on Linux tries every call that would signal the host, change its limits, priority or scheduling, open a handle to it, undo the parent-death signal, create a SysV object, name a descriptor's owner or signal, ask for signal-driven I/O or push input into a terminal (the last on a pipe of its own, where an unfiltered kernel does nothing), then the same calls on itself; reports its capability sets; tries to open the host's process, or another (its memory file on Linux, each access right on Windows, down to merely asking after it) |
| `proc-files <pid>:<n>,<n>`, `proc-reads <pid>`, `host-debugger-pipes <directory>`, `descriptors <path>`, `session` | on Linux tries to open a process through `/proc` for reading (the named descriptors, its environment, its memory and its map), closing at once whatever opens; tries to read what `/proc` tells of a process (its status, stat, command line, name, environment, map, cgroup and threads), to list `/proc`, and to read its own entries; looks in the directory and in `/tmp` for the host's debugger pipes and asks whether it could open each; reports whether any of its own descriptors refers to the path; reports whether it leads a session of its own, which terminal controls it, and whether it can open a controlling terminal |
| `files <directory> <beside>` | tries to read a file of the host's in the directory (`victim.txt`), the file beside its own assembly and, on Linux, `/etc/passwd`, and to list `/` and the directory; to write the host's file in place (opened for writing, reading and writing, appending or truncation), truncate it by name, map it or its own assembly shared for writing (on Linux also by changing a read-only shared mapping's protection), rename, link to or delete it; to create a file, a directory and, on Linux, a symbolic link and a named pipe beside it, a file in `/tmp` and in `/dev/shm`, and to remove the directory's empty subdirectory `empty`; and to change the file's permissions, attributes and times; on Linux also to watch the file with inotify, start fanotify, read and list its extended attributes by name, make and remove a POSIX message queue (removing it again at once, since a kernel may make it before it refuses to open it), lock its own assembly with `flock`, and ask after or change its own assembly's attributes through `ioctl`s on a descriptor opened for reading (`FS_IOC_GETFLAGS`, `FS_IOC_ENABLE_VERITY`, `FS_IOC_FSGETXATTR`, `FICLONE`). Each attempt is reported as done or with its refusal, `EACCES` or `EPERM` on Linux, `refused` for a denial of access on Windows |
| `file-streams` | on Linux locks its own assembly with `flock`, shared and exclusive, and opens and reads it as .NET does (a `FileStream` shared and unshared, `File.ReadAllBytes`), each of which takes such a lock beside its open |
| `files-before-filter <directory> <beside>` | on Linux makes the `files` attempts once it has started over confined, before it enters its sandbox: under Landlock alone, with no system-call filter yet; reports `not confined` and changes nothing when the confinement does not hold |
| `connect <port>` | tries to connect to a TCP listener on the loopback interface, giving up after 5 s |
| `early-thread [<path>]` | runs the socket and process probes, and a read of the file, on a thread it started before entering the sandbox |
| `footprint` | renders the request as the real worker does and reports its address space, data and peak sizes |

Every mode from `allocate-native` down is a probe: it reports what it could do as a refusal's detail,
which the host reads as an ordinary answer.

The `crash-` modes end the process as a crash ends one, at once: on Linux by a real crash (the
runtime aborts), on Windows by ending the process with an access violation's exit code. A real
crash on Windows goes to Windows Error Reporting first, which keeps the dying process and its
pipes until its report is written, seconds on a quiet machine and half a minute on a loaded one;
`fail-fast-after-request` is that real crash, for the one test of it.
