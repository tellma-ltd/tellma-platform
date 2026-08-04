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
dotnet ef migrations add <Name> --project src/apps/Tellma.Identity.Migrations --startup-project src/apps/Tellma.Identity.Web
```

The design-time factory reads `TELLMA_IDENTITY_MIGRATIONS_SQL` for the connection string, falling
back to a LocalDB default. It replicates the runtime Identity store options that shape the model
(schema version 3 for the passkeys table) — keep it in sync through `TellmaIdentityModelDefaults`.

> **Note (pre-release only):** the `Initial` migration was revised in place before first release —
> notably `IX_OpenIddictTokens_CreationDate` changed from a filtered `(Status, ExpirationDate)` index
> to an unfiltered `CreationDate`-leading one with `Status`, `ExpirationDate`, and `AuthorizationId`
> included (on a 50k-row table shaped the way the prune job finds one — a small tail past the
> threshold — the plan seeks only when `AuthorizationId` covers the authorization-status join;
> plan choice is statistics-dependent, so a mostly-prunable store may still scan). A database migrated from an earlier draft of this branch (a dev
> LocalDB, a reused CI database) keeps the old index shape because `__EFMigrationsHistory` already
> records `Initial`; drop and recreate such databases rather than benchmarking or debugging against
> the stale shape. Once this branch ships, migrations are append-only.
