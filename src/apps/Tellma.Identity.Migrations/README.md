# Tellma.Identity.Migrations

The EF Core **migrations assembly** for the Tellma Identity Server database: the committed migration
chain for `TellmaIdentityDbContext` (ASP.NET Core Identity tables including passkeys, OpenIddict's
four tables, and the engine's own tables — sessions, single-use codes, temporary access passes, audit
events) plus the design-time context factory.

All tables live in the dedicated SQL schema `idsvr`, so the in-proc hosting shape can share a
distribution's database without collision. The schema name is baked into the migrations; a
per-deployment schema override is deliberately not supported.

Add a migration (run from the repo root):

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

The design-time factory reads `TELLMA_IDENTITY_MIGRATIONS_SQL` for the connection string, falling
back to a LocalDB default. It replicates the runtime Identity store options that shape the model
(schema version 3 for the passkeys table) — keep it in sync through `TellmaIdentityModelDefaults`.

One thing in `Initial` is **not** scaffolded and does not survive regeneration on its own:
`IX_OpenIddictTokens_CreationDate` is raw SQL at the end of `Up`. OpenIddict owns the token entity,
so no model configuration describes that index and `dotnet ef migrations add` cannot emit it.
Anyone who regenerates this migration has to carry it across by hand — the schema comparison in the
integration suite is what catches its absence.
