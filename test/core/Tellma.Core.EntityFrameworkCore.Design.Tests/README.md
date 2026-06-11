# Tellma.Core.EntityFrameworkCore.Design.Tests

Design-time tests — the suites that need the EF Design package and Roslyn:

- `Scaffolding/OperationScaffoldingTests` — golden C# for the scaffolded
  `CreateTableType`/`DropTableType` calls, plus the full round-trip: generated migration code is
  compiled in memory with Roslyn, executed against a `MigrationBuilder`, and the rebuilt
  operations compared with the originals.
- `Scaffolding/SnapshotRoundTripTests` — the standard EF technique: model → snapshot C# →
  compile → diff against the live model must be **empty**. Proves the table-type annotations
  (including column order) survive model snapshots with stock snapshot code generation.
- `DesignTime/DesignTimeServicesTests` — the MSBuild-injected
  `[assembly: DesignTimeServicesReference]` on the MigrationsHost asset resolves to
  `TableTypesDesignTimeServices` (the Phase-1 `ProjectReference` discovery flow; the package
  flow runs in CI's `design-e2e` job).
- `Guards/PersistedModuleTripwireTests` — spec Rule 5 layer 3: scans every `SqlOperation`
  across the MigrationsHost migrations for `CREATE/ALTER PROCEDURE|FUNCTION` batches that
  mention a generated type name.

`Infrastructure/DesignTestHelpers` builds the design-time service provider the way EF tooling
does (referenced services first, then the SQL Server provider's design services, then EF's
TryAdd defaults) and compiles scaffolded sources into a collectible `AssemblyLoadContext`.
