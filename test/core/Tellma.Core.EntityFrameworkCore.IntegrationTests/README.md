# Tellma.Core.EntityFrameworkCore.IntegrationTests

Containerized SQL Server tests (`Category=Integration`): the committed MigrationsHost migration
chain is applied to a fresh database, then the deployed types are asserted through the catalog
views.

- `ApplyMigrationsTests` — types exist with correct columns **in order**, primary keys, grants;
  zero `sys.sql_expression_dependencies` rows (spec Rule 5 layer 2); UDTT/table **column-order
  parity** (pins the library's public-API ordering rule against EF's private CREATE TABLE
  sorting); `Migrate()` re-run no-op; the `--idempotent` script runs twice cleanly;
  `EnsureCreated()` creates types too.
- `DropGuardTests` — a planted procedure referencing a type makes the drop fail with error
  53102 naming the procedure; after removing it, drop + recreate succeed.
- `MemoryOptimizedTests` — a memory-optimized type deploys with `is_memory_optimized = 1` on
  XTP-capable hosts (skipped otherwise, e.g. LocalDB).

## Running

By default the suite starts a `mcr.microsoft.com/mssql/server:2022-latest` container through
Testcontainers — identical locally and in CI. **Docker must be running**; if it is not, the
fixture fails fast with an actionable message.

For a faster inner loop against an existing server:

```powershell
$env:TELLMA_TEST_SQL = "Server=localhost;Integrated Security=true;TrustServerCertificate=true"
dotnet test test/core/Tellma.Core.EntityFrameworkCore.IntegrationTests
```

Each test creates its own uniquely named database; databases created on a `TELLMA_TEST_SQL`
server are dropped when the fixture disposes.

If the default `mcr.microsoft.com/mssql/server:2022-latest` image crashes on your Docker
Desktop/WSL2 kernel (exits 255 at startup), pick another image:

```powershell
$env:TELLMA_TEST_SQL_IMAGE = "mcr.microsoft.com/mssql/server:2025-latest"
```
