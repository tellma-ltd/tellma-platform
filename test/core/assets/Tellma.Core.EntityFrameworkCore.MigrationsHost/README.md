# Tellma.Core.EntityFrameworkCore.MigrationsHost (test asset)

A migrator-shaped console project, mirroring a distribution's `Tellma.Distro.<Slug>.Migrator`:
it references the runtime **and** Design libraries plus `Microsoft.EntityFrameworkCore.Design`
directly, owns the committed scaffolded `Migrations/` folder, and is the `dotnet ef`
design-time target of CI's `design-e2e` job (the Phase-1 `ProjectReference` discovery flow).

Its model is a representative sample of the table-types feature surface: fluent and attribute
opt-ins, pack→leaf attribute inheritance, column exclusion, rowversion, computed-column
exclusion, grants, built-in primitive types, per-table `sq_*` sequences starting above the
reserved seed band, and in-band `HasData` rows. There is deliberately **no IDENTITY column
anywhere** (IDs are app-assigned from sequences per the architecture).

Used by:

- the **Design.Tests** suite (injected-attribute assertion, persisted-module tripwire);
- the **IntegrationTests** suite (`Database.Migrate()` against a containerized SQL Server);
- CI's `design-e2e` job (`migrations add` / `script --idempotent` / `bundle`).

Regenerate the migration after model changes:

```powershell
dotnet ef migrations add <Name> `
  --project test/core/assets/Tellma.Core.EntityFrameworkCore.MigrationsHost `
  --startup-project test/core/assets/Tellma.Core.EntityFrameworkCore.MigrationsHost
```

The sibling `...MigrationsHost.Package` project consumes the same sources but references the
libraries as **packages** from a local feed (`dotnet pack` → `artifacts/pkg`) — the
published-package discovery flow. It is intentionally excluded from `Tellma.slnx`; CI builds it
explicitly after packing.

The other asset, `...BoundaryHost`, is the web-host stand-in for the publish boundary check:
it references the runtime library only, and CI asserts its publish output contains no
Design-tree assemblies (`eng/check-publish-boundary.ps1`).
