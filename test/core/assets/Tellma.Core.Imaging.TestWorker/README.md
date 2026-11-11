# Tellma.Core.Imaging.TestWorker

A **test asset** (not an xunit project): a stand-in for the
[PDF worker](../../../../src/core/Tellma.Core.Imaging.PdfWorker/README.md) that misbehaves on
purpose, one way per mode, for the imaging suite. It enters the real worker's sandbox exactly as the
real worker does and is started the way the processor starts the real one (`dotnet exec` with the
suite's runtime configuration and dependency manifest), so every misbehaviour happens under the very
sandbox and operating-system limits a real worker gets.

| Mode | Does |
|---|---|
| `exit-before-ready`, `crash-before-ready`, `hang-before-ready`, `bad-ready` | fails before its ready frame |
| `exit-after-request`, `crash-after-request`, `fail-fast-after-request`, `hang-after-request`, `hang-ignoring-input`, `spin` | fails, hangs (exiting when its input ends as the real worker does, or ignoring its input so that only the operating system can end it) or burns CPU after reading the request |
| `unknown-status`, `oversized`, `truncated`, `stray-bytes` | breaks the result frame |
| `not-png`, `wrong-size`, `extra-chunk`, `corrupt-png` | answers with a malformed or deceptive image |
| `good`, `failed`, `real` | answers with a blank page, reports a failure, or runs the real worker |
| `allocate-native`, `allocate-managed`, `map-shared` | takes and touches memory in 16 MiB blocks until refused (native allocations, managed arrays, and on Linux shared anonymous mappings, which only the address-space limit counts) and reports how much it got |
| `socket`, `process`, `environment`, `write-file <directory>` | tries to open a socket, start a process or write a file in the directory, or lists its environment |
| `host-access`, `capabilities`, `open-host` | on Linux tries every call that would signal the host, change its limits, priority or scheduling, open a handle to it, undo the parent-death signal, create a SysV object, name a descriptor's owner or signal, ask for signal-driven I/O or push input into a terminal (the last on a pipe of its own, where an unfiltered kernel does nothing), then the same calls on itself; reports its capability sets; tries to open the host's process (its memory file on Linux, each access right on Windows) |
| `proc-files <pid>:<n>,<n>`, `host-debugger-pipes <directory>`, `descriptors <path>`, `session` | on Linux tries to open a process through `/proc` (the named descriptors, its environment, its memory and its map), closing at once whatever opens; looks in the directory and in `/tmp` for the host's debugger pipes and asks whether it could open each; reports whether any of its own descriptors refers to the path; reports whether it leads a session of its own, which terminal controls it, and whether it can open a controlling terminal |
| `early-thread` | runs the socket and process probes on a thread it started before entering the sandbox |
| `footprint` | renders the request as the real worker does and reports its address space, data and peak sizes |

Every mode from `allocate-native` down is a probe: it reports what it could do as a refusal's detail,
which the host reads as an ordinary answer.

The `crash-` modes end the process as a crash ends one, at once: on Linux by a real crash (the
runtime aborts), on Windows by ending the process with an access violation's exit code. A real
crash on Windows goes to Windows Error Reporting first, which keeps the dying process and its
pipes until its report is written, seconds on a quiet machine and half a minute on a loaded one;
`fail-fast-after-request` is that real crash, for the one test of it.
