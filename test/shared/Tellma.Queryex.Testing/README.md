# Tellma.Queryex.Testing

The parts of the Queryex test apparatus that both suites — and the inspection playground — need to
share. Not shipped, and not a test project itself: it contains no assertions and references no test
framework, so `dotnet test` never tries to run it.

## What lives here

| Folder | Contents |
|---|---|
| `Probe/` | A public mirror of the engine's internal intermediate representations: tokens, the parse tree, the bound tree with types and nullities, the function registry, the findings a lowered plan yields, and what a cache key tells apart. |
| `Schema/` | The fixture schema (`LedgerFixture`) and the DDL that deploys it (`LedgerDdl`), including its declared collation. |
| `Corpus/` | The conformance corpus: every expression case and every whole-query case, with what each is expected to produce. |
| `Semantics/` | The runtime value model, the collation table, the fixture rows, and the reference interpreter — a second implementation of the language, expression by expression and whole query by whole query. |

## Why one shared library rather than two copies

Three things have to agree or the suites prove nothing: the schema the corpus binds against, the
schema the differential suite deploys as real tables, and the schema the playground shows a human.
Duplicating any of them is exactly the drift the differential testing exists to catch. The fixture
rows are declared once and read two ways — as objects the interpreter evaluates, and as the
`INSERT` statements a server receives — for the same reason.

It also keeps the engine honest. The engine grants `InternalsVisibleTo` to **this project alone**;
everything else consumes the public mirror. So no intermediate representation becomes public API,
and the bound tree a reviewer eyeballs in the playground is literally the same object the
interpreter evaluates and the soundness sweep walks.

## The interpreter is written from the semantics, not from the emitter

That is the whole point of it. It never consults the lowering or the SQL — it uses the engine only
to bind, so that it is reading the same expression, and everything it then concludes about that
expression it concludes on its own. Two consequences worth knowing before changing it:

- Numbers are `SqlDecimal`, because it is the framework's implementation of the backend's own
  precision and scale algebra. Hand-rolling that would make the interpreter a second unverified
  implementation of the rules it exists to check.
- String comparison uses a hand-rolled weight table over a declared character repertoire rather than
  the platform's collation services, because ICU and NLS do not agree everywhere and a test that
  means different things on two operating systems proves nothing. A guard test keeps every fixture
  and corpus string inside that repertoire.

Two of the backend's habits are reproduced deliberately rather than idealised, because the language
adopts them: a difference between two dates counts boundaries crossed rather than time elapsed, and
adding a fractional number of units drops the fraction.
