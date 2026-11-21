# Tellma.Core.Migrator.TestDistribution (test asset)

A distribution's Migrator project in miniature, for the migrator's suites. It has what a real
distribution's Migrator project has, and nothing more:

- a composition (`TestDistribution.Compose`) with one feature, `ProbeFeature`, whose table
  `probe.Gadgets` gives the tenant model a schema, a key sequence and a table type beyond the core
  feature's;
- a design-time factory, `TestDistributionDesignTimeFactory`, deriving
  `TellmaDesignTimeDbContextFactory<TellmaDbContext>`;
- the tenant migrations `dotnet ef` scaffolds from that factory, under `Migrations/`;
- a `Program.cs` that makes the one `TellmaMigrator.RunAsync` call.

`Tellma.Core.IntegrationTests` references it and drives the migrator's commands in process with
this assembly as the tenant migrations' assembly. It references no test framework, so `dotnet test`
never runs it.

The slug is the reference distribution's, `acme`, because the Development startup gate refuses a
slug the repository's `taxonomy.json` does not list.

## Regenerating the migration

The tenant model includes every table the core feature contributes, so a checkpoint that changes a
core table must regenerate this migration, or `migrate` refuses the pending model changes. Delete
`Migrations/` and `Tellma/Core/Migrator/TestDistribution/Migrations/`, then run from the repository
root, after `dotnet tool restore`:

```bash
dotnet ef migrations add Initial --project test/core/assets/Tellma.Core.Migrator.TestDistribution --startup-project test/core/assets/Tellma.Core.Migrator.TestDistribution --namespace Tellma.Core.Migrator.TestDistribution.Migrations --output-dir Migrations
```

Then remove the byte-order mark from the three generated files: the format gate refuses it.
Scaffolding never connects to a database.
