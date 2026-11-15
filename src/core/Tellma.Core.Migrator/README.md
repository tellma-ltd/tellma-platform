# Tellma.Core.Migrator

The migrator of `Tellma.Core`: the single executable that changes a database's schema. A
distribution's Migrator project references this one and its `Program.cs` makes one call:

```csharp
return await TellmaMigrator.RunAsync(args, "acme", AcmeComposition.Compose, TellmaIdentityStore.MigrateAsync);
```

The last argument is passed only in in-proc identity mode (`Tellma:Identity:Mode = InProc`), where
the catalog also holds the identity store; `migrate` without it exits 1, naming
`Tellma.Identity.Migrations`.

This project also carries the platform-owned **catalog migrations**. Every distribution's catalog
is identical, so no distribution contributes to the catalog schema. It brings the distribution's
Migrator project the EF Design packages and the table-type design-time services its own
tenant-model migrations are scaffolded with.

## Commands

| Command | Does |
|---|---|
| `emit-client-assets <dir>` | Realises the composition with no host or database, runs only the startup checks that read the composition alone, runs every `IClientAssetContributor`, and writes the client metadata and the string packs into the generated folder `<dir>`. |
| `migrate [--catalog-only] [--tenant <id>]... [--all-tenants] [--parallelism <n>]` | Creates a missing catalog, migrates it (and the in-proc identity store), grants `tellma_app`, registers and provisions the `Tellma:Seed:Tenants`, then migrates every tenant in parallel and prints one line per tenant. |
| `provision [--tenant <id>] [--name …] [--category Live\|Sandbox] [--live-tenant <id>] [--server …] [--database …] [--admin-email …] [--admin-subject …]` | Registers a tenant (or resumes a `Provisioning` one) and brings it to `Active`, printing `provision: registered tenant <id>` as soon as the row exists. A line with `--tenant` converges when rerun: an id the catalog holds with the same registration (servers compared canonically) is resumed, and an active tenant is reported `provisioned (already)`. A line without `--tenant` draws a new id from the sequence on every run, so it is not rerun: the resume is `provision --tenant <id>`, which a failed run prints. `--admin-subject` is accepted in Development only. |
| `set-state --tenant <id> --state <state> [--reason …]` | Moves a tenant to another state, as the actor `migrator`. |
| `status` | Prints the catalog's pending migrations, each tenant's state, recorded schema fingerprints against the model's and pending migrations, then the composition's problems. Writes nothing. |

Exit codes: `0` converged, `1` usage error, `2` partial failure (the report says which database or
tenant failed and how to resume it), `3` composition problems, a composition method that throws
included. `emit-client-assets` uses the same codes: a folder it cannot write is `2`.

Every command but `emit-client-assets` runs inside a started generic host built from the
distribution's composition, with `Tellma:Jobs:Enabled = false` as the last configuration source,
so the startup gate checks the deployment before the command runs and no job worker runs beside a
schema change. The host turns the registry's startup load off; each command reloads the registry
when it needs it, because `migrate` may have to create the catalog first.

### What `migrate` does, in order

1. Creates the catalog when it is missing (platform collation; read-committed snapshot on a fresh
   one, ending any other connection already open in it, such as a web host's that started first)
   and refuses an existing catalog in another collation.
2. Takes the catalog lock (`sp_getapplock 'tellma:migrate:catalog'`, a session lock held in the
   catalog). Under it: the `tellma_app` role (and, in a fresh catalog, the user of
   `Tellma:Sql:ApplicationPrincipal`), the catalog's migrations, the identity store's, the role's
   grants, the registry's reload, and the registration of every seed tenant the catalog lacks.
3. Provisions every seed tenant still `Provisioning`, each under its tenant lock
   (`tellma:migrate:tenant:<id>`).
4. Migrates every `Active`, `ReadOnly` and `Suspended` tenant (or the named ones) under its tenant
   lock, reading the row again once the lock is held: the role, `Migrate()`, the schema
   fingerprint row, the grants, the version-tag seed, the due provisioning steps and the drift
   scan. A tenant whose database is ahead of this migrator is reported `ahead`; a `Suspended` one
   stops after the version-tag seed (`suspended-steps-deferred`); a `Frozen` one is skipped
   (`frozen`), even when named or frozen while the run waited for its lock; a non-seed
   `Provisioning` row is left to `provision` (`provisioning`).

Each lock is a session lock on a catalog connection of its own. Before the fingerprint, before each
provisioning step, before the invitation and before a tenant turns `Active`, the run confirms that
its session still holds the lock (`APPLOCK_MODE`); a session the server ended, on a failover for
instance, fails that tenant instead of letting two runs interleave. Only the run's own cancellation
stops a run; any other failure, a timeout inside a step included, is that tenant's and the others go
on.

A tenant run that applied a migration or ran a step moves every version tag at its end, and also
on the way out when it fails after doing either, since what it committed stays. That last move is
bounded by `Tellma:Migrator:VersionTagBumpTimeout` (a `TimeSpan`, 30 seconds by default) and is not
stopped by the run's cancellation; when it fails or runs out of time, the run logs
`MigratorVersionTagBumpFailed` and reports the original failure.

### Statement bounds and the model lock

Every statement the migrator's own code sends runs under an explicit bound from
`Tellma:Migrator` (each a `TimeSpan` from 1 second to 1 day, validated by the startup gate, so a bad
value exits `3`):

| Key | Default | Bounds |
|---|---|---|
| `CommandTimeout` | 5 minutes | Reads of the server and the database (edition, existence, collation, freshness), the role, the application principal, the grants, the fingerprint row, the migration locks' checks and releases, and `status`'s own reads (the fingerprints and the pending migrations). |
| `DatabaseCommandTimeout` | 30 minutes | `CREATE DATABASE` and the `ALTER DATABASE` of read-committed snapshot and the query store, which wait for the database's other sessions and, on Azure SQL Database, for the service. |
| `MigrationCommandTimeout` | 1 hour | Each statement of the catalog's, the identity store's and a tenant's migrations. The identity store creates its own commands, so it is given the catalog's connection string with this bound as its `Command Timeout`. |
| `VersionTagBumpTimeout` | 30 seconds | The version-tag move on the way out of a failed tenant run. |

A migration lock's take waits up to 10 minutes for another run, under a command bound just past
that wait. The statements the migrator runs through `Tellma.Core`'s services keep those services'
own bounds: the catalog store's (the tenant listing `status` and `migrate` read, registrations and
state transitions), the membership hints, the provisioning records and steps, the version-tag
seeder (its move on a failed run aside) and the registry's refresh.

Nothing in the migrator reads the catalog before the command has created it: its host loads no
snapshot at startup, and the registry's refresh loop lets its ticks pass until the command's own
first load. A read of a missing catalog would fail, and on SQL Server and LocalDB SqlClient then
fails the next opens of the same connection string with that cached error for up to a minute (its
`PoolBlockingPeriod`), the command's own open of the catalog it has just created among them.

`CREATE DATABASE` copies `model` under an exclusive lock, so creations on one server queue on it,
as `migrate --parallelism` provisioning several tenants does. A creation that cannot take the lock
fails with error 1807 (followed by 1802) having created nothing, and is run again on the same
connection: at most `Tellma:Migrator:CreateDatabaseAttempts` runs (5 by default, from 1 to 20),
waiting 2, 4, 8 and then 15 seconds between them, each wait jittered by 25 % so parallel creations
spread out. Each retry logs the warning `MigratorDatabaseCreationRetrying` and running out of
attempts logs `MigratorDatabaseCreationGaveUp`; the creation's own error then fails that database
like any other. The run's cancellation stops a wait at once.

Nothing else is run again. A timeout above all is not: the server may still finish a statement
whose client stopped waiting, so a creation that timed out or was cancelled may have created its
database. The next run finds it, or is told it exists (error 1801), checks its collation and
prepares it as a fresh one, which every preparation statement tolerates. A creation that failed on
leftover files of an earlier database surfaces for an operator to clear.

### The application role's grants

After every migration the migrator recomputes `tellma_app`'s grants from the model: `SELECT`,
`INSERT`, `UPDATE` and `DELETE` on every schema but `dbo`, `UPDATE` on every sequence, `SELECT`
alone on `dbo.__TellmaSchema`, and, in the catalog, a `DENY` of every write to the catalog's own
migration history. The role exists before the first migration runs, because every table type's
migration grants it `EXECUTE`.

## What lives here

| Item | Purpose |
|---|---|
| `TellmaMigrator` | The entry point: parses the command line, hosts the composition, runs one command. |
| `Commands/` | One class per command. The seed tenants and their administrator come from `Tellma:Seed` (`TellmaSeedOptions` in `Tellma.Core`, validated at startup). |
| `CommandLine/` | The command line's parser and its usage errors. |
| `Emit/` | The client metadata and string-pack files: canonical JSON, the content hash in each file name, the loaders module with its `strings` map. |
| `MigratorOptions` | The `Tellma:Migrator` bounds: each statement class's command timeout, the creation's attempts and the version-tag move's bound. |
| `Sql/` | The database statements: creation (with the model-lock retry, `CreateDatabaseRetry`) and collation checks, the migration locks, the grants, the schema fingerprint rows. |
| `TenantMigration` | One tenant's provisioning and migration, each a converging run under the tenant lock. |
| `TellmaDesignTimeDbContextFactory<TContext>` | The base of a distribution's tenant design-time factory: `dotnet ef` builds the tenant context from the distribution's composition. |
| `Migrations/` and the snapshot under `Tellma/Core/Migrator/Migrations/` | The catalog chain for `CatalogDbContext` (`Tellma.Core.Tenancy`): schema `catalog` with `Tenants`, `CatalogState` (one row, seeded), `TenantMemberships` and `Sessions`, the tenant id sequence `sq_Tenants` (from 1000) and the membership table type `TenantMembershipList`. History: `catalog.__EFMigrationsHistory`. |
| `CatalogDesignTimeFactory` | The `dotnet ef` entry for the catalog: builds the context exactly as a host does. |
| `ApplicationRoleInterceptor` | Used by the design-time factories only: creates the `tellma_app` role in the target database (never `master`) before a migration runs there, because the table-type migrations grant `EXECUTE` to it. |

## Design-time connections

Each design-time factory takes its connection string from `--connection` (after `--` on the
`dotnet ef` command line), else from an environment variable, else from a LocalDB database:

- the tenant factory: `TELLMA_TENANT_MIGRATIONS_SQL`, else `TellmaTenantDesign`;
- the catalog factory: `TELLMA_CATALOG_MIGRATIONS_SQL`, else `TellmaCatalogDesign`.

A database `dotnet ef database update` creates serves migration authoring only: it has the
`tellma_app` role but no application principal, schema grants, fingerprint row or catalog row, so no
host can serve it. Real databases are created and migrated by `migrate` and `provision`.

## The platform collation

The catalog model declares the platform collation (`DatabaseCollations.Platform`,
`Latin1_General_100_CI_AS`); the first migration carries no collation operation, and **no later
migration may change the model's collation**, which Azure SQL Database would refuse. The collation pin
in `Tellma.Core.Tests` fails on any migration that tries, and a second pin keeps
`CatalogMigrations.Latest` (in `Tellma.Core`) equal to the last migration here: a web host checks
the catalog's history against that constant at startup, so **a new migration must move it**.

## Adding a catalog migration

Run from the repository root, after `dotnet tool restore`:

```bash
dotnet ef migrations add <Name> --project src/core/Tellma.Core.Migrator --startup-project src/core/Tellma.Core.Migrator --namespace Tellma.Core.Migrator.Migrations --output-dir Migrations
```

The two trailing flags are not optional: without them the scaffolder derives another namespace.
Migrations are regenerated with the pinned `dotnet-ef`, never edited by hand; only the generated
migration file's byte-order mark is removed, because the format gate refuses it.

Then point `CatalogMigrations.Latest` at the new migration's id.

## Testing

`Tellma.Core.Tests` covers the command line, the grant statements, the design-time connections,
the emit's files, the bounds, the model-lock retry policy and the entry point's exit codes without
a database. `Tellma.Core.IntegrationTests`
drives every command in process against a SQL Server through the test distribution in
`test/core/assets/Tellma.Core.Migrator.TestDistribution`. `TellmaMigrator.RunAsync` has a public
overload taking `MigratorRunSettings` (the output writers, the environment, the content root and
the configuration and service hooks), so a distribution's suite runs its own migrator in process
the same way.
