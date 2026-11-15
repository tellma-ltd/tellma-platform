# Tellma.Core.Analyzers.Probe.Package

The package-flow twin of [Tellma.Core.Analyzers.Probe](../Tellma.Core.Analyzers.Probe). It
compiles the same sources, one violation of each platform analyzer rule, against the `Tellma.Core`
package alone, so it reaches `Tellma.Core.Abstractions` only as a transitive package dependency.

NuGet excludes a dependency's analyzer assets by default. The contract package's
`buildTransitive` targets add the analyzers back, and this probe's build must fail with exactly
`TELLMA0001` to `TELLMA0006` to prove they did.

It is not in `Tellma.slnx`. Its own `nuget.config` routes the `Tellma.*` packages to `artifacts/pkg`
at the repository root, which the CI design-e2e job fills with `dotnet pack` first. To run it
locally, pack the libraries the same way, then:

```bash
bash test/core/assets/Tellma.Core.Analyzers.Probe/verify-probe.sh test/core/assets/Tellma.Core.Analyzers.Probe.Package artifacts/analyzer-probe-package.log
```
