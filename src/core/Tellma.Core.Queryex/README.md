# Tellma.Core.Queryex

Queryex ("query expression") is the platform's typed query language: a small, statically typed,
side-effect-free expression language that users and stored configuration supply as **text**, and
that this library compiles against an entity schema into parameterized T-SQL.

**Zero dependencies** — no packages, no project references. Everything it needs is in the shared
framework, which is what lets `Tellma.Core`, pack code, the test suites, and the inspection tool all
reference it the way they would reference a `System.*` library.

**It compiles; it never executes.** No connection is opened, no SQL is run, no result set is read,
and no value a host binds at execution time is ever seen. The output is SQL text plus a description
of the parameter slots and result columns the caller needs in order to run it.

## The pipeline

```
text → lexer → parser → binder → nullity → lowering → emitter → SQL + parameter slots
```

Each stage has its own data structure, and no stage reaches across: type checking never emits SQL,
and emission never makes a typing decision. Every intermediate representation is `internal` —
the public surface is text in, results out. Handing a bound tree to a caller would freeze the
tree's shape against future binding changes and would let a caller hold a tree bound against a
stale schema; composition is expressed through `FilterTree` instead.

## The public surface

| Type | Purpose |
|---|---|
| `QueryexEngine` | The entry point. Thread-safe; hosts register one instance and share it. |
| `QueryexSchema`, `QueryexSchemaBuilder` | The host-built description of the entities a root can reach. |
| `FilterTree` | Structural composition of predicates — the alternative to concatenating text. A leaf may carry the Queryex version stored beside it; `Via` rebases a criterion onto a to-one navigation's target, and `Exists` searches another entity's rows that point at this one. |
| `QuerySpec`, `CompiledQuery` | One query in, SQL plus parameter slots and result columns out — and the prologue and body a host embeds in SQL of its own. |
| `ConstantItem`, `ConstantCompilationOptions` | Expressions that read no row in, one statement that answers them as one row out (`QueryexEngine.EvaluateConstant`), compiled into the same `CompiledQuery`. |
| `KeySetRestriction` | Restricts a query's root rows to keys a host holds in a table of its own. |
| `ParameterBindingOptions`, `MeasureRows` | Arguments printed into a text as literals (`QueryexEngine.BindParameters`), and the rows a measure aggregates as one predicate (`QueryexEngine.MeasureCondition`). |
| `FilterConjunct`, `FilterConjunctOperator` | A filter's top-level restrictions as discovery reads them, when `QueryDiscoverySpec.Conjuncts` asks. |
| `QueryexTimeZones` | The time-zone ids `local` compiles, and the older ids browsers report for them, for a host's list of zones. |
| `QueryexDiagnostic` | A machine-readable compilation problem. Hosts localize; the engine composes no prose. |

## Rules this project lives by

- **Every failure on user input is a diagnostic, never an exception.** An exception escaping the
  engine on user text is a defect by definition. Exceptions are reserved for *caller* errors —
  a malformed schema, contradictory options — which are host bugs, not user input.
- **No dependencies, ever.** A dependency here reaches every host that queries anything.
- **Deterministic output.** The same text, schema, and options produce byte-identical SQL with
  identically named parameters. Hash-ordered collections, culture-sensitive formatting, and
  unstable alias assignment are defects, not stylistic choices.
- **Two names for everything in the schema, never interchangeable.** Expression authors write
  *logical* names and the binder resolves them; only the emitter reads *physical* names, and they
  are the only host-supplied text that ever reaches emitted SQL. Everything else is engine-authored,
  which is what makes injection through an expression structurally impossible rather than a matter
  of discipline.
- **Bounded caches.** The engine accepts untrusted input, so every cache has a ceiling and an
  eviction policy; an attacker feeding distinct texts must age entries out, never grow memory.

The language, its semantics, and the compiler's obligations are specified in
[spec 0008](../../../docs/specs/0008-queryex.md).

## Amendments

Spec 0010 Part F amends spec 0008 for the hosts that compose queries: the data access layer, the
permission evaluator and the report engine. Every amendment is additive — with its new members at
their defaults the engine emits what it emitted before — except the renamed version stamp, the
paging slot names and the retired zone ids below. `QueryexLanguage.Version` stays 1. Implemented so
far:

| Amendment | What it adds |
|---|---|
| The `QueryexVersion` stamp (§F.18) | `ValidationOptions`, `QueryCompilationOptions` and `DiscoveryOptions` name the stamp `QueryexVersion` (formerly `LanguageVersion`), so beside a report's multilingual titles it cannot read as a translation revision. |
| Key-set restrictions (§F.1) | `QuerySpec.Restrictions`: each `KeySetRestriction` is one more conjunct of `WHERE`, after the filter, of the form `<column> IN (SELECT [Id] FROM @table)`. Against a text property that declares its own collation (`EntityBuilder.Property(..., collation:)`), the table's keys are converted to it, `IN (SELECT [Id] COLLATE <collation> FROM @table)`: the column stays bare for its index and the text stays the same per schema. Its path shares the query's joins and counts toward `MaxJoins`; a path or table name that does not fit is a caller error. |
| `level(node)` (§F.2) | The depth of a hierarchy position, as the backend's `GetLevel()` reports it. |
| Prologue and body (§F.3) | `CompiledQuery.Prologue` (the hoisted declarations and an empty line) and `CompiledQuery.Body` (the statement without its terminator); `Sql` is `Prologue + Body`, then `OPTION (RECOMPILE)` and a line feed when `CompiledQuery.Recompile` is set, then `";\n"`. The hint is never in `Body`: a host that embeds the body writes `RECOMPILE` into the one `OPTION` clause of its own statement. |
| Paging slots (§F.4) | `Skip` and `Take` bind through `@qx{b}_skip` and `@qx{b}_take`, the last parameters, skip before take. A compilation is cached on whether a query pages, never on which page, and a hit re-stamps the two slots' values. |
| Versioned leaves (§F.6) | `FilterTree.Leaf(text, queryexVersion)`: a stored criterion binds under the version stamped beside it. |
| `FilterTree.Via` (§F.7) | `Via(navigation, inner)`: `inner` binds against the target of a dotted to-one path and joins through the query's shared joins. Behind an optional step the target's key is tested first, so the node stays two-valued under `not`. Discovery reports the criterion's paths with the navigation's segments in front. |
| Omitted parameters (§F.8) | `QueryCompilationOptions.OmittedParameters`: declared parameters a compilation leaves out. Folded on the parse tree before anything binds: a bare omitted element leaves an `in` list or the key list of `descendantOf` and `ancestorOf`; an atom of a filter's, a group-level predicate's or an aggregation condition's skeleton that still mentions one, or whose list is left empty, becomes `true` under an even number of negations (`FilterTree.Not` included) and `false` under an odd one; the connectives absorb it to a fixed point. Anywhere else the parameter is `cast(null, '<type>')`. What folds away is never bound, an omitted parameter takes no slot, and a name that is not declared is a caller error. An ordering term that folding leaves equal to an earlier one is left out of the statement rather than reported as a duplicate; two terms written the same are still QX4008. An absent value counts against `MaxTypedNodes` as the one node the parameter was, so a query that fits its ceilings fits them whatever is left out. |
| Parameter binding (§F.10) | `QueryexEngine.BindParameters(text, ParameterBindingOptions)`: the omitted parameters fold as a compilation folds them, then every valued parameter is written in as a literal of its declared type — a number, a string, `true`/`false`, `cast('…', 'date' \| 'datetime' \| 'datetimeoffset' \| 'guid')`, or `cast(null, '<type>')` for null — and the tree is printed in canonical text, which needs no declaration to compile. A whole text that folds is `true` or `false`. A valued name that is not declared, one both valued and omitted, a name given twice, or a value whose CLR type does not fit its declaration is a caller error. |
| Constant statements (§F.9) | `QueryexEngine.EvaluateConstant`: one to `MaxListItems` expressions, each with the type it has to have, compile into `SELECT <e0> AS [c0], …` with no source — always one row. A path is QX4009, an aggregation QX4002, a parameter QX3007, a list QX2009, each at `Constant[<i>]`. A written string is read at the item's type. Nothing hoists, so the prologue is empty, and an operand a guard would repeat is written in the form that states it once. |
| The `uq` calendar (§F.11) | `QueryexSchemaBuilder.CalendarMap(calendar, source)` names the host's month table for a table calendar (columns `Calendar`, `Year`, `Month`, `StartDate`, `EndDate`, the end exclusive); `uq` is accepted only where the schema maps it, and is QX3101 elsewhere. Every `uq` answer may be absent: a date or month the table does not hold has none. A call that reads a row finds its month through an `OUTER APPLY` lookup beside the statement's joins (`[C1]`, `[C2]`, …), shared by every call on the same date; a call that reads no row is computed once into a declared variable (`@qx{b}_v{n}`), one per distinct call, and a statement whose `WHERE` or `HAVING` reads such a variable sets `CompiledQuery.Recompile`, so the backend plans it knowing the value; a call over an aggregate, or in a constant statement, is a scalar subquery in place, nesting one level per month it reads (`[M]`, `[M2]`, …, naming its own values `[V1]`, `[V2]`, …). A date is sought as the last month that began on or before it, and kept only when it falls before that month's end. |
| `FilterTree.Exists` (§F.12) | `Exists(entity, navigation, inner)`: an `EXISTS` subquery over `entity`'s rows whose `navigation` points at the current root, with its own joins and bindings and the statement's parameters. Its aliases continue the statement's (`[X1]`, `[X2]`, … in pre-order, then the next `[P{n}]`, `[B{n}]` and `[C{n}]`), its source and joins count toward `MaxJoins`, and discovery reports no path from inside it. |
| `ParameterUse.ComparedWith` (§F.13) | The path a parameter is compared with, rooted at the query's root: by a comparison in either order, as an `in` list's element, or as a key of `descendantOf`/`ancestorOf`. Null when it is compared with none, or with two different paths. |
| Undeclared columns (§F.16) | `QuerySpec.UndeclaredColumns`: root columns the schema omits on purpose, read as nullable text after the select list. |
| `startOfQuarter` (§F.17) | `startOfQuarter(d[, calendar])`: the first day of the quarter `d` falls in, as a date, in every calendar. |
| The `et` calendar (§F.19) | Every calendar function counts in the Ethiopian calendar inline, with no table: twelve months of thirty days and a thirteenth, Pagume, of five or six, in quarters of three months with Pagume in the fourth. The arithmetic agrees with ICU on every boundary day from 1900 to 2100. Where no binding may name a date the pattern reads many times (at group grain, in a constant statement, in a declaration) the call is a scalar subquery that names it once (`[N1]`, `[N2]`, …), so nested calls grow the statement linearly. |
| `MeasureCondition` (§F.20) | `QueryexEngine.MeasureCondition(measure, ParameterBindingOptions)`: the rows a measure's aggregations read, as one predicate — each aggregation's condition, folded and bound as `BindParameters` does, deduplicated by canonical text and joined by `or`. `MeasureRows.Condition` is null when some aggregation reads every row (no condition, or one that is `true`), and `false` when every condition is. |
| Filter conjuncts (§F.21) | `QueryDiscoverySpec.Conjuncts`: `DiscoveryResult.Conjuncts` lists the atoms reached from the filter's root through `and` alone — tree nodes and a leaf's own `and` — that compare a path or a parameter-free expression with a literal (`=`, `!=`, `<`, `<=`, `>`, `>=`, `is [not] null`, or `descendantOf` with one key; the absent value only with `=`, `!=` and `is [not] null`), the literal on either side. Atoms under `or`, `not`, `Via` or `Exists` yield none; the paths inside `Via` still appear in `Paths`, those inside `Exists` do not. |
| Current and legacy zone ids (§F.22) | The zone table holds current ids only — the time-zone database's locations and `UTC` — plus the nineteen older ids browsers report, each resolving through its current id. Retired spellings such as `US/Alaska` and the `Etc/GMT±n` zones are QX3101. `QueryexTimeZones` exposes both lists. |
| Optional navigations (spec 0012 §11.1) | `EntityBuilder.Navigation(name, target, foreignKey, isOptional: true)`: a step whose target row may be missing although its foreign key holds a value — a one-to-one extension table keyed by the declaring entity's own key. It joins outer and makes every path through it nullable, exactly as a nullable foreign key does, so an owner without the row keeps its row and reads absent values. |
| Collation conflicts (QX3203) | A text property that declares its own collation carries it into every expression built from it; a property that declares none carries the database's, and literals, parameters and computed text carry none. An operation whose text operands carry two different collations, such as `Subject = Email` with a binary-collated subject, is QX3203 at that operation, however deep the columns sit below it, in a filter, a select item or an ordering, rather than a failure of the backend when the statement runs. |

The embedded zone table follows the time-zone database's `zone.tab` of release 2026b; the copy of
its location ids that the tests check the table against lives beside `TimeZoneTableTests`, and the
two are refreshed together.
