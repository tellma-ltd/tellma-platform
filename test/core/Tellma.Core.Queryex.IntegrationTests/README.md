# Tellma.Core.Queryex.IntegrationTests

The half of the Queryex suite that needs a real SQL Server. It deploys the fixture schema as actual
tables, executes the SQL the engine emitted, and compares the result row by row against the
reference interpreter — which was written from the language semantics alone and has never seen the
emitter.

That comparison is the primary defence for the null semantics and the null-guard emission: two-valued
comparison over absent values is easy to get subtly wrong and very hard to notice by reading.

| Suite | What it proves |
|---|---|
| `DifferentialTests` | The rows on the server are the rows in memory, and every corpus query answers the same on both — restrictions to key sets included, with each table of keys declared ahead of the statement the way a host would. A query compiled with parameters left out is answered by the interpreter applying the omission rule itself, at evaluation time and over the text as written, with a value supplied for every parameter all the same. |
| `ConstantDifferentialTests` | Every constant statement of the corpus answers with exactly one row, each value the one the interpreter computes with no row to read and each column's promise about absence kept; and a statement and a query bound from the same values see the same day. |
| `CalendarSweepTests` | Every calendar function, in the Gregorian, Ethiopian and Umm al-Qura calendars, answers the same as the interpreter on every day from 1900 to 2100. The days are generated on the server and in memory by the same rules, and run past both ends of the month table, where every Umm al-Qura answer is absent. |
| `FilterStructureTests` | A rebase onto a navigation's target and a semi-join over another entity's rows are each the exact complement of their negation — the rows whose navigation reaches nothing, and the rows nothing points at, included — and one compiled statement answers for every user. |
| `EmbeddingTests` | A compiled statement embeds in SQL of a host's own: its body counts as a derived table, unordered or paged, with its prologue written first; and keys one statement captures into a table restrict a second one in the same batch. |
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

The connection string may name any database; the fixture creates and uses `TellmaQueryexTests`, in
the platform collation (`Latin1_General_100_CI_AS`), and rebuilds its tables on every run. The
fixture's text columns keep the collation they declare; the database's is what literals and table
variables get. A `TellmaQueryexTests` left by an older run in another collation fails the fixture
with a message asking for it to be dropped.

Every class here is tagged `Category=Integration`, which is how CI both excludes it from the pull-request
run and includes it in the integration job. A class without the trait runs in neither.
