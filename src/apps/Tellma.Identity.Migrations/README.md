# Tellma.Identity.Migrations

The EF Core **migrations assembly** for the Tellma Identity Server database: the committed migration
chain for `TellmaIdentityDbContext` (ASP.NET Core Identity tables including passkeys, OpenIddict's
four tables, and the engine's own tables — sessions, single-use codes, temporary access passes, audit
events), the design-time context factory, and `TellmaIdentityStore`, the entry that applies the chain
from outside the engine's host.

All tables live in the dedicated SQL schema `idsvr`, so the in-proc hosting shape can share a
distribution's database without collision. The schema name is baked into the migrations; a
per-deployment schema override is deliberately not supported. The migration history is EF's default
`dbo.__EFMigrationsHistory`.

## The platform collation

The engine's model declares the platform collation (`DatabaseCollations.Platform`,
`Latin1_General_100_CI_AS`), so a database EF creates from it — `dotnet ef database update` or
`MigrateAsync` against a missing development or test database — is created in that collation, with
read-committed snapshot on. The chain starts with a single `Initial` scaffolded from that model. An
initial migration carries no collation operation, and **no later migration may change the
model's collation**: it would scaffold an `ALTER DATABASE … COLLATE`, which Azure SQL Database
refuses. The collation pin in the unit suite fails on any migration that tries.

Every later migration follows the N−1 rule: it lands before the engine release that needs it, so the
previous release keeps running on the migrated store during a rollout. When one is added,
`TellmaIdentityConstants.LatestMigration` in the engine must name it — the engine's store check
requires that row at every start, and a unit test keeps the two equal.

## Applying the migrations

- **A distribution's migrator** (the in-proc store, schema `idsvr` in the catalog) calls
  `TellmaIdentityStore.MigrateAsync(connectionString)`. It builds the context exactly as the
  design-time factory does and applies every pending migration.
- **A standalone operator** sets up the shared database once — created in the platform collation,
  with read-committed snapshot on — and then, from a checkout after `dotnet tool restore`, applies the
  chain before each engine release that adds a migration:

  ```bash
  dotnet ef database update --project src/apps/Tellma.Identity.Migrations --startup-project src/apps/Tellma.Identity.Migrations --connection "<connection string>"
  ```

  `dotnet ef migrations script --idempotent` writes the same changes as a script.
- **Development and test hosts** turn `TellmaIdentity:Seed:ApplyMigrations` on and let the engine
  migrate its own store at startup; that is the only reason a host references this project.

## Adding a migration

Run from the repo root:

```bash
dotnet ef migrations add <Name> --project src/apps/Tellma.Identity.Migrations --startup-project src/apps/Tellma.Identity.Migrations --namespace Tellma.Identity.Migrations --output-dir Migrations
```

The two trailing flags are not optional. Without them the scaffolder derives the namespace from
the project name and produces `Tellma.Identity.Migrations.Migrations`, which is what every existing
file here would then disagree with.

This project is its own startup project. It ships the design-time factory, and its
`Microsoft.EntityFrameworkCore.Design` reference is `PrivateAssets=all` so it does not flow to the
hosts — naming a host as the startup project fails with "doesn't reference
Microsoft.EntityFrameworkCore.Design".

The design-time factory takes its connection string from `--connection`, else from
`TELLMA_IDENTITY_MIGRATIONS_SQL`, else from a LocalDB default. It replicates the runtime Identity
store options that shape the model (schema version 3 for the passkeys table) through
`TellmaIdentityModelDefaults`, and builds the same context `TellmaIdentityStore` does.

One thing in `Initial` is **not** scaffolded and does not survive regeneration on its own:
`IX_OpenIddictTokens_CreationDate` is raw SQL at the end of `Up`. OpenIddict owns the token entity,
so no model configuration describes that index and `dotnet ef migrations add` cannot emit it.
Anyone who regenerates this migration has to carry it across by hand — the schema comparison in the
integration suite is what catches its absence.
