# Tellma.Core.Analyzers.Probe

A library whose build must fail. It holds one violation of each platform analyzer rule,
`TELLMA0001` to `TELLMA0006`, in [Violations.cs](Violations.cs). CI builds it on Linux and on
Windows through [verify-probe.sh](verify-probe.sh), which expects exactly the six errors and no
other error code. That proves the analyzers load, on each operating
system, in a project that reaches `Tellma.Core.Abstractions` only transitively, through its project
reference to `Tellma.Core`.

The probe is deliberately not in `Tellma.slnx`, whose build must stay clean. Build it on its own:

```bash
bash test/core/assets/Tellma.Core.Analyzers.Probe/verify-probe.sh test/core/assets/Tellma.Core.Analyzers.Probe artifacts/analyzer-probe.log
```

Its package-flow twin, [Tellma.Core.Analyzers.Probe.Package](../Tellma.Core.Analyzers.Probe.Package),
compiles the same sources against the `Tellma.Core` package alone.

The suite that pins each rule's conditions and negatives is `test/core/Tellma.Core.Analyzers.Tests`.
