# Tellma.Core.Analyzers.Probe

A library whose build must fail. It holds one violation of each platform analyzer rule,
`TELLMA0001` to `TELLMA0006`, in [Violations.cs](Violations.cs). CI builds it on Linux and on
Windows and expects every one of the six errors. That proves the analyzers load, on each operating
system, in a project that reaches `Tellma.Core.Abstractions` only transitively, through its project
reference to `Tellma.Core`.

The probe is deliberately not in `Tellma.slnx`, whose build must stay clean. Build it on its own:

```bash
dotnet build test/core/assets/Tellma.Core.Analyzers.Probe
```

The service pipeline's contract types that `TELLMA0005` matches are declared in
[PipelineStubs.cs](PipelineStubs.cs) until the contract ships them.

The suite that pins each rule's conditions and negatives is `test/core/Tellma.Core.Analyzers.Tests`.
