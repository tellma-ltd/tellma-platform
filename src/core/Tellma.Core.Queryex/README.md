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
| `FilterTree` | Structural composition of predicates — the alternative to concatenating text. |
| `QuerySpec`, `CompiledQuery` | One query in, SQL plus parameter slots and result columns out. |
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
