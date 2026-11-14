# Tellma.Core.Analyzers.Tests

The PR-tier suite of the platform analyzers, `TELLMA0001` to `TELLMA0006`. Each rule has tests for
every condition it refuses and for the code it must leave alone. Each test compiles a small source
with Microsoft.CodeAnalysis.Testing. The expected diagnostics are written as markup in that source
(`{|TELLMA0003:text|}`), and any diagnostic the markup does not name fails the test. Every negative
is therefore pinned as silence.

## How a test compiles

[AnalyzerHarness](Infrastructure/AnalyzerHarness.cs) references these assemblies:

- the framework assemblies this suite runs on, so no reference pack is downloaded and the suite
  runs offline;
- the real `Tellma.Core.Abstractions` and `Tellma.Core.Queryex`;
- the packages the rules name, EF Core and SqlClient.

The runtime itself is left out, so a test compilation may take the runtime's assembly name
(`Tellma.Core`) to check the exemptions. [PlatformStubs](Infrastructure/PlatformStubs.cs) declares
the few types a rule matches that the referenced assemblies do not supply: the tenant context and
the service pipeline's contract types. The rules match by namespace and metadata name, which the
stubs share with the real types.

[NamePinTests](NamePinTests.cs) holds every matched name to the real type it stands for. It also
holds the analyzer's reserved-name rule to the contract's `ReservedSqlNames.IsReserved`, so a
rename or a rule change on either side fails here.

## Running

```
dotnet test test/core/Tellma.Core.Analyzers.Tests
```

The suite needs no database and no network.
