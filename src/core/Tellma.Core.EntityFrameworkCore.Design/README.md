# Tellma.Core.EntityFrameworkCore.Design

The design-time companion of
[Tellma.Core.EntityFrameworkCore](../Tellma.Core.EntityFrameworkCore/README.md), mirroring EF
Core's own `Microsoft.EntityFrameworkCore` / `.Design` split. It contains the only Tellma types
that reference the `Microsoft.EntityFrameworkCore.Design` package and its dependency tree
(Roslyn, templating):

- [TableTypesCSharpMigrationOperationGenerator](TableTypesCSharpMigrationOperationGenerator.cs) —
  scaffolds `migrationBuilder.CreateTableType(...)` / `.DropTableType(...)` into migration files.
- [TableTypesCSharpMigrationsGenerator](TableTypesCSharpMigrationsGenerator.cs) — adds the
  Tellma namespaces to a migration file's `using` directives when table-type operations are
  present (EF's own namespace collection never covers custom operations).
- [TableTypesCSharpSnapshotGenerator](TableTypesCSharpSnapshotGenerator.cs) +
  [TableTypesSqlServerAnnotationCodeGenerator](TableTypesSqlServerAnnotationCodeGenerator.cs) —
  render each table-type definition in model snapshots as a readable, multi-line
  `HasTableTypeDefinition(...)` fluent call instead of a raw JSON `HasAnnotation` string
  (per-line column diffs in PRs); the pair is registered together so a definition is never
  silently dropped.
- [TableTypesDesignTimeServices](TableTypesDesignTimeServices.cs) — the `IDesignTimeServices`
  registration EF tooling loads.

## Who references this

**Only migrator projects** — the per-distribution console host that is the `dotnet ef`
design-time target and owns the scaffolded `Migrations/` folder. Never the web host: a runtime
assembly containing these types would carry IL references to the Design tree, which either drags
Roslyn into the web server's publish output or plants a latent `ReflectionTypeLoadException`.
CI publishes a representative host and asserts no Design-tree assembly is present
(`eng/check-publish-boundary.ps1`).

Because `Microsoft.EntityFrameworkCore.Design` is `developmentDependency=true`, NuGet applies
`PrivateAssets=all` to our reference to it — so a migrator referencing this package must also
reference `Microsoft.EntityFrameworkCore.Design` directly (the standard EF requirement).

## How discovery works (no manual wiring)

EF tooling scans **only the startup assembly and the migrations assembly** for
`[assembly: DesignTimeServicesReference]` — never referenced libraries. So this package ships
MSBuild targets ([build/net10.0/](build/net10.0/)) that inject the attribute into the consuming
project's assembly at build time via `WriteCodeFragment` — the same mechanism EF's own extension
packages (e.g. `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId`) use. Referencing the
package is sufficient.

Two flows, both covered by CI's `design-e2e` job:

- **Package flow**: NuGet imports the packaged `build/` props+targets automatically.
- **Phase-1 in-repo flow** (`ProjectReference`): MSBuild does not flow `build/` targets across
  project references, so the repository's root `Directory.Build.targets` imports the same file;
  its target self-gates on a `ProjectReference` to this project and is a no-op everywhere else.
