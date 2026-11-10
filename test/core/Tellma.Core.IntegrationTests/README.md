# Tellma.Core.IntegrationTests

SQL Server tests (`Category=Integration`) for `Tellma.Core`'s tenancy runtime against real
catalogs. Each test creates its own catalog the way the migrator creates a fresh one — in the
platform collation (`Latin1_General_100_CI_AS`), with read-committed snapshot on and the
`tellma_app` role in place before the first migration, since the membership table type's migration
grants that role — and applies the platform-owned catalog migrations of `Tellma.Core.Migrator`. The
tenancy runtime is then composed over it through `AddTellma`, as a distribution composes it, without
starting a host.

| Folder | Covers |
|---|---|
| `Catalog/CatalogMigrationTests` | The migrations from an empty database (tables, sequence from 1000, the seeded state row, no identity columns, binary-collated identifiers, the membership table type binary-collated and granted to `tellma_app`); a second run as a no-op; the idempotent script run twice; `dotnet ef database update` through the design-time factory onto a database that does not exist, which creates the database in the platform collation and the role before migrating; a migration without the role failing; the migration state the registry's startup check reads (current, behind, never migrated, missing). |
| `Catalog/TenantCatalogTests` | Registration from 1000 and by requested id, the default location, refusals (an id or a location already taken, a sandbox naming a sandbox or nothing); a new catalog version on every write; transitions recording who and why; an illegal transition changing nothing; two instances racing on one transition (one wins, the other is refused); a transition whose state moved under it inside a held transaction, refused by the statement's own precondition; renames; relocations only of frozen or provisioning tenants, seen by a second instance on its next refresh, and refused to a server outside the allowed list; the listing from the store, a refused row shown without a connection string. |
| `Catalog/MembershipDirectoryTests` | Hints listed by tenant id with their descriptors, retired tenants left out; subjects compared byte for byte, and subjects with spaces refused; upserts and duplicates in one call; a failed write logged and metered, never thrown; reconciliation of one tenant (update, insert, delete) leaving every other tenant's hints alone, an empty reconciliation, and a reconciliation carrying another tenant's record refused before it writes. |
| `Catalog/RegistryCatalogTests` | A second instance converging on its next refresh and an unchanged catalog not reloaded; a tenant's connection reaching that tenant's database alone, under the deployment's application name; a host refusing to start over a catalog behind the platform; a missing catalog failing startup in Development and starting not ready elsewhere. |

## Running

By default the suite starts a `mcr.microsoft.com/mssql/server:2022-latest` container through
Testcontainers, identical locally and in CI; **Docker must be running**.

```bash
dotnet test test/core/Tellma.Core.IntegrationTests --filter "Category=Integration"
```

For a faster inner loop against an existing server, point `TELLMA_TEST_SQL` at it; every database
the suite creates there is uniquely named and dropped when the run ends:

```powershell
$env:TELLMA_TEST_SQL = "Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true"
```

If the default image exits 255 on your Docker Desktop/WSL2 kernel, set
`TELLMA_TEST_SQL_IMAGE=mcr.microsoft.com/mssql/server:2025-latest`.
