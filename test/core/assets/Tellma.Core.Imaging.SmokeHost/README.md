# Tellma.Core.Imaging.SmokeHost

A **test asset** (not an xunit project): a host-shaped console application that composes
[`Tellma.Core.Imaging`](../../../../src/core/Tellma.Core.Imaging/README.md) on the generic host, as
a distribution does, and checks it from outside the suite's own process. It runs the startup checks,
renders an honest PDF and a hostile one (nested forms that expand to millions of objects inside
PDFium), and reports one `key=value` line each, ending with `result=held` (exit code 0) when the
check passed, the honest page rendered, the hostile one was stopped with the host's memory unmoved,
and neither PDFium nor PDFtoImage was ever loaded into the host.

- `--measure N` also renders a letter page N more times and reports the cost of a whole thumbnail,
  worker start included.
- `--orphan <stand-in>` instead starts one thumbnail in a worker that never answers, reports the
  worker's process id, and waits: the suite kills this host and sees the worker die with it.

The suite starts it with its own runtime configuration and dependency manifest. To check a
`dotnet publish` output instead:

```bash
dotnet publish test/core/assets/Tellma.Core.Imaging.SmokeHost -c Release -r linux-x64 --self-contained false -o out
dotnet out/Tellma.Core.Imaging.SmokeHost.dll --measure 10
```
