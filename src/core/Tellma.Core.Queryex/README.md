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
| `KeySetRestriction` | Restricts a query's root rows to keys a host holds in a table of its own. |
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
| Key-set restrictions (§F.1) | `QuerySpec.Restrictions`: each `KeySetRestriction` is one more conjunct of `WHERE`, after the filter, of the form `<column> IN (SELECT [Id] FROM @table)`. Its path shares the query's joins and counts toward `MaxJoins`; a path or table name that does not fit is a caller error. |
| `level(node)` (§F.2) | The depth of a hierarchy position, as the backend's `GetLevel()` reports it. |
| Prologue and body (§F.3) | `CompiledQuery.Prologue` (the hoisted declarations and an empty line) and `CompiledQuery.Body` (the statement without its terminator); `Sql` is always `Prologue + Body + ";\n"`. |
| Paging slots (§F.4) | `Skip` and `Take` bind through `@qx{b}_skip` and `@qx{b}_take`, the last parameters, skip before take. A compilation is cached on whether a query pages, never on which page, and a hit re-stamps the two slots' values. |
| Versioned leaves (§F.6) | `FilterTree.Leaf(text, queryexVersion)`: a stored criterion binds under the version stamped beside it. |
| `FilterTree.Via` (§F.7) | `Via(navigation, inner)`: `inner` binds against the target of a dotted to-one path and joins through the query's shared joins. Behind an optional step the target's key is tested first, so the node stays two-valued under `not`. Discovery reports the criterion's paths with the navigation's segments in front. |
| `FilterTree.Exists` (§F.12) | `Exists(entity, navigation, inner)`: an `EXISTS` subquery over `entity`'s rows whose `navigation` points at the current root, with its own joins and bindings and the statement's parameters. Its aliases continue the statement's (`[X1]`, `[X2]`, … in pre-order, then the next `[P{n}]` and `[B{n}]`), its source and joins count toward `MaxJoins`, and discovery reports no path from inside it. |
| Undeclared columns (§F.16) | `QuerySpec.UndeclaredColumns`: root columns the schema omits on purpose, read as nullable text after the select list. |
| Current and legacy zone ids (§F.22) | The zone table holds current ids only — the time-zone database's locations and `UTC` — plus the nineteen older ids browsers report, each resolving through its current id. Retired spellings such as `US/Alaska` and the `Etc/GMT±n` zones are QX3101. `QueryexTimeZones` exposes both lists. |

The embedded zone table follows the time-zone database's `zone.tab` of release 2026b; the copy of
its location ids that the tests check the table against lives beside `TimeZoneTableTests`, and the
two are refreshed together.
