# Tellma.Core.Migrator

The migrator adapter of `Tellma.Core`: the platform-owned **catalog migrations** and their
design-time factory. Every distribution's catalog is identical, so no distribution contributes to
the catalog schema; its migrations ship here, not in the distribution's Migrator project. A
distribution's Migrator project references this one, which brings it the EF Design packages and the
table-type design-time services its own tenant-model migrations are scaffolded with.

The migrator commands (`migrate`, `provision`, `set-state`, `status`, `emit-client-assets`) land in
this project next; today it carries the catalog's migrations alone.

## What lives here

| Item | Purpose |
|---|---|
| `Migrations/` and the snapshot under `Tellma/Core/Migrator/Migrations/` | The catalog chain for `CatalogDbContext` (`Tellma.Core.Tenancy`): schema `catalog` with `Tenants`, `CatalogState` (one row, seeded), `TenantMemberships` and `Sessions`, the tenant id sequence `sq_Tenants` (from 1000) and the membership table type `TenantMembershipList`. History: `catalog.__EFMigrationsHistory`. |
| `CatalogDesignTimeFactory` | The `dotnet ef` entry for the catalog: builds the context exactly as a host does. |
| `ApplicationRoleInterceptor` | Used by the design-time factory only: creates the `tellma_app` role in the target database (never `master`) before a migration runs there, because the table-type migration grants `EXECUTE` to it. |

## The platform collation

The catalog model declares the platform collation (`DatabaseCollations.Platform`,
`Latin1_General_100_CI_AS`); the first migration carries no collation operation, and **no later
migration may change the model's collation**, which Azure SQL Database would refuse. The collation pin
in `Tellma.Core.Tests` fails on any migration that tries, and a second pin keeps
`CatalogMigrations.Latest` (in `Tellma.Core`) equal to the last migration here: a web host checks
the catalog's history against that constant at startup, so **a new migration must move it**.

## Adding a migration

Run from the repository root, after `dotnet tool restore`:

```bash
dotnet ef migrations add <Name> --project src/core/Tellma.Core.Migrator --startup-project src/core/Tellma.Core.Migrator --namespace Tellma.Core.Migrator.Migrations --output-dir Migrations
```

The two trailing flags are not optional: without them the scaffolder derives another namespace.
Migrations are regenerated with the pinned `dotnet-ef`, never edited by hand; only the generated
migration file's byte-order mark is removed, because the format gate refuses it.

Then point `CatalogMigrations.Latest` at the new migration's id.

## Applying the migrations by hand

For migration authoring only. The design-time factory takes its connection string from
`--connection`, else from `TELLMA_CATALOG_MIGRATIONS_SQL`, else from the LocalDB database
`TellmaCatalogDesign`:

```bash
dotnet ef database update --project src/core/Tellma.Core.Migrator --startup-project src/core/Tellma.Core.Migrator --connection "<connection string>"
```

A database updated this way has the `tellma_app` role (the factory creates it) but no application
principal, no schema grants and no tenant rows, so no host can serve it. Real catalogs are created
and migrated by the migrator's `migrate` command.
