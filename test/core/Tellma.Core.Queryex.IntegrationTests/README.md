# Tellma.Core.Queryex.IntegrationTests

The half of the Queryex suite that needs a real SQL Server. It deploys the fixture schema as actual
tables, executes the SQL the engine emitted, and compares the result row by row against the
reference interpreter — which was written from the language semantics alone and has never seen the
emitter.

That comparison is the primary defence for the null semantics and the null-guard emission: two-valued
comparison over absent values is easy to get subtly wrong and very hard to notice by reading.

| Suite | What it proves |
|---|---|
| `DifferentialTests` | The rows on the server are the rows in memory, and every corpus query answers the same on both. |
| `BackendFactsTests` | Every function that promises a value produces one even when everything it reads is missing; division keeps the fractional digits the language promises; emitted SQL means the same thing under hostile connection settings; and every zone the embedded table resolves to is one the server has. |

Absence comes from **nullable columns with real NULL rows**, never from a `null` literal: a literal
folds away during lowering, which would test the fold and leave the emission untested.

A query that asks for no order gets none, so the two answers are compared as sets rather than
position by position wherever the case declares no ordering. Comparing them in order would be
asserting something the language does not promise.

## Running

By default the fixture starts a SQL Server container through Testcontainers, so it behaves the same
locally and in CI. Point it at an existing server instead for a faster inner loop:

```powershell
$env:TELLMA_TEST_SQL = "Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true"
dotnet test test/core/Tellma.Core.Queryex.IntegrationTests
```

```bash
export TELLMA_TEST_SQL='Server=localhost;Integrated Security=true;TrustServerCertificate=true'
dotnet test test/core/Tellma.Core.Queryex.IntegrationTests
```

The connection string may name any database; the fixture creates and uses `TellmaQueryexTests`, and
rebuilds its tables on every run.

Every class here is tagged `Category=Integration`, which is how CI both excludes it from the pull-request
run and includes it in the integration job. A class without the trait runs in neither.
