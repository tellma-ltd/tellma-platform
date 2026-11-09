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
| `exit-after-request`, `crash-after-request`, `hang-after-request`, `spin` | fails, hangs or burns CPU after reading the request |
| `unknown-status`, `oversized`, `truncated`, `stray-bytes` | breaks the result frame |
| `not-png`, `wrong-size`, `extra-chunk`, `corrupt-png` | answers with a malformed or deceptive image |
| `good`, `failed`, `real` | answers with a blank page, reports a failure, or runs the real worker |
| `allocate-native`, `allocate-managed`, `socket`, `process`, `environment`, `write-file` | probes a limit or the sandbox and reports what it could do as a refusal's detail |
