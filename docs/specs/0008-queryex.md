# Spec: Queryex — Query Language and Compiler

- **Author:** Ahmad Akra
- **Date:** 20 August 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Queryex ("query expression") is the platform's typed query language: a small, statically-typed,
side-effect-free expression language that users and stored configuration supply as text, and that
the platform compiles against an entity schema into parameterized T-SQL. It is the substrate under
every surface where a query is *data* rather than code:

- **The public REST API** — `filter=`, `select=`, `orderby=` query strings on entity endpoints.
- **Ad-hoc report definitions** — stored measure, dimension, filter, and ordering expressions,
  authored in a designer and compiled long after they were written.
- **Row-level security** — filter criteria attached to permissions, conjoined with the user's own
  filter on every read.
- **Business logic** — bulk context loading in pack and distribution services (entities needed for
  validation and posting).
- **Document templates** — templates that pull the data they print by issuing queries.

The consumers themselves (the CRUD stack, the report runtime, the permissions model, the template
engine) are future specs. This spec ships the engine they will all share: the language, the
compiler, its public API, and the test infrastructure that pins its semantics.

The engine is a redesign of the Queryex engine proven in the Tellma monolith. It keeps what worked
— entity paths compiled through a join trie, one compilation mode per clause, deterministic
aliasing — and redesigns what did not: the type system exposes a single `Bool` type instead of the
monolith's bit/boolean split, comparison is total and two-valued instead of inheriting SQL's
three-valued surprises, filter composition is structural instead of textual, and functions are
registry entries instead of code paths through the binder. The two languages are deliberately not
compatible; nothing in the platform parses monolith expressions.

**The engine is a library, not a service.** It compiles text; it never opens a connection, never
executes SQL, and never sees a value the host binds at execution time. It is deterministic,
allocation-conscious, and safe to call with fully untrusted input: every failure on user input is a
diagnostic, never an exception. The only backend dialect is SQL Server — the platform's only
database — but emission is a distinct final stage, so the dialect boundary is a real seam even with
one implementation behind it.

## Goals / Non-goals

**Goals**

- Ship `Tellma.Core.Queryex`: the language (lexical structure, grammar, type system, nullity
  analysis, semantics, function library), the compiler pipeline (lexer → parser → binder → nullity
  → lowering → emitter), bounded caching, limits, and diagnostics — production-grade,
  XML-documented, dependency-free.
- Ship the composition surface security depends on: `FilterTree`, with its identity elements and
  degenerate-shape semantics pinned by tests.
- Ship the authoring surface: `Validate` for binding without SQL, `Discover` and `DiscoverQuery`
  for enumerating parameters, paths, and functions — including parameter type inference, shared
  across a query's clauses — over input that need not fully compile.
- Ship the conformance corpus, golden SQL snapshots, the differential in-memory interpreter, and
  the property test suites that pin the semantics.
- Ship a small inspection playground under `eng/queryex-inspection/` with a built-in test
  schema, for humans to explore the compiler live (§19).

**Non-goals (explicitly out of scope)**

- **Execution and materialization** — running compiled SQL, reading result sets, assembling entity
  graphs, `expand=` semantics. The CRUD-stack spec owns them; this spec only guarantees that
  `CompiledQuery` carries everything a materializer needs.
- **The EF-model schema adapter** — building a `QueryexSchema` from a distribution's EF Core model.
  It lands with the CRUD stack, its first consumer; this spec fixes the schema contract it targets.
- **REST query-string binding, the permissions model, the report definition model, template-engine
  integration** — each arrives with its own spec, consuming this engine's public API.
- **A client-side evaluator** — the monolith evaluated some expressions in TypeScript on the
  client; whether and how the platform does is a client-spec question.
- **Non-Gregorian calendar codes** — the calendar-argument surface ships now, with `'gc'` alone
  implemented; `'uq'` and `'et'` are reserved and their emission strategies recorded (§10.4), to
  land with the Locale packs as additive registry changes.
- **Multi-valued parameters** (`x in (@Centres)`) — deferred. Adding them later is additive:
  parameter types come from caller declarations, so stored expressions cannot change meaning when
  list declarations become possible.
- **Write operations of any kind** — the engine emits `SELECT` statements only. The bulk save path
  is separate machinery.

## 1. Placement and architecture

### 1.1 Projects and packages

| Piece | Location | Notes |
|---|---|---|
| The engine | `src/core/Tellma.Core.Queryex/` | Published NuGet `Tellma.Core.Queryex`, namespace `Tellma.Core.Queryex`. References **no** package — no Tellma package, no third party. |
| Tests | `test/core/Tellma.Core.Queryex.Tests/` | Offline, hermetic, cross-platform: corpus, golden snapshots, interpreter, property tests. |
| Differential suite | `test/core/Tellma.Core.Queryex.IntegrationTests/` | Executes emitted SQL against the developer/CI SQL Server and compares with the interpreter (§18.3), following the repository's `*.IntegrationTests` convention for suites needing external resources. |
| Inspection tool | `eng/queryex-inspection/` | Self-hosted playground web app + README (§19). In the solution so CI keeps it compiling; never packed. |

`Tellma.Core.Queryex` is a Core-layer library, but not one of the composed-only optional packages
(`Tellma.Core.Email`, `Tellma.Core.Webhooks`, `Tellma.Core.Testing`): the CRUD stack and report
base inside `Tellma.Core` are its primary consumers, so `Tellma.Core` will take a direct reference
when they land. That is acceptable precisely because the engine is dependency-free — referencing it
costs a host nothing beyond the engine itself. Pack code that needs to build queries may reference
it directly the way it would a `System.*` library; whether any of its types additionally surface on
`Tellma.Core.Abstractions` seams is the CRUD-stack spec's decision.

**The code in this spec is normative for shape, not for formatting.** Snippets are written
compactly, and several of those conventions are build errors under the repository's style gates.
What a snippet fixes is the type, its members, and their contracts; how it is laid out is the
repository's business, not this spec's.

### 1.2 Design principles

Six principles govern every decision below. Where a later section appears to offer a choice,
resolve it in favour of these.

1. **Separate the phases.** Lexing, parsing, binding, nullity analysis, lowering, and emission are
   distinct stages with distinct data structures. No stage reaches forward or backward: type
   checking never emits SQL, and emission never makes typing decisions.
2. **Analysis is pure and total.** Binding and type checking produce a value or a diagnostic. They
   never mutate shared state, never partially succeed, and never throw for user input.
3. **Two-valued predicates.** A predicate evaluates to exactly `true` or `false`. There is no
   `unknown`. Every construct defines its result for absent operands (§9).
4. **The surface language does not leak the backend.** Users never work around the absence of a
   T-SQL boolean type, never learn which operations are cheap in T-SQL, and never see a backend
   error message.
5. **Deterministic output.** The same input text, schema, and options must produce byte-identical
   SQL with identical parameter names — the property that makes plan caching, output caching, and
   golden-file testing possible.
6. **Declarative extension.** Adding a function is a registry entry and an emit template, never a
   change to the parser, binder, or nullity pass.

### 1.3 The pipeline

```
  text
    │
    ▼
┌──────────┐   tokens (kind, span, value)
│  Lexer   │
└──────────┘
    │
    ▼
┌──────────┐   SyntaxTree — untyped, spans, no schema          cache: text (L1)
│  Parser  │
└──────────┘
    │
    ▼
┌──────────┐   TypedExpr — types, resolved descriptors,
│  Binder  │   no backend concepts                             cache: + schema, mode, … (L2)
└──────────┘
    │
    ▼
┌──────────┐   TypedExpr + nullity annotations
│ Nullity  │
└──────────┘
    │
    ▼
┌──────────┐   RelationalPlan — joins, value bindings,
│ Lowering │   positions, explicit guards
└──────────┘
    │
    ▼
┌──────────┐   SQL text + parameter slots                      cache: full query shape (L3)
│ Emitter  │
└──────────┘
```

| Stage | Input | Output | Knows about |
|---|---|---|---|
| Lexer | text | token list | characters, keywords |
| Parser | tokens | `SyntaxTree` | grammar only |
| Binder | `SyntaxTree`, schema, mode | `TypedExpr` | schema, types, function registry |
| Nullity | `TypedExpr` | nullity per node | the nullity lattice |
| Lowering | `TypedExpr` | `RelationalPlan` | joins, positions, binding hoists |
| Emitter | `RelationalPlan` | SQL + parameter slots | the T-SQL dialect |

Each stage is independently testable, and each stage's output is printable to a stable textual form
for golden-file tests. Every intermediate representation is `internal`: the public surface is text
in, results out. Handing a bound tree to a caller would freeze the IR's shape against future
binding changes and would let a caller hold a tree bound against a stale schema; composition is
expressed through `FilterTree` (§2.3) instead.

### 1.4 The public API

```csharp
namespace Tellma.Core.Queryex;

/// <summary>
///     Compiles Queryex text against an entity schema into parameterized T-SQL. Thread-safe;
///     hosts register one instance and share it. The engine only compiles — executing the SQL,
///     binding parameter values, and reading results are the caller's business.
/// </summary>
public sealed class QueryexEngine
{
    public QueryexEngine();
    public QueryexEngine(QueryexEngineOptions options);

    /// <summary>Parses and binds an expression list without emitting SQL — the authoring and
    ///     validation entry point for stored expressions.</summary>
    public QueryexResult<ValidatedExpression> Validate(string text, ValidationOptions options);

    /// <summary>Reports what an expression refers to — parameters (with inferred types), paths,
    ///     functions — over input that need not fully bind.</summary>
    public DiscoveryResult Discover(string text, DiscoveryOptions options);

    /// <summary>Reports what a whole query's clauses refer to, with parameter inference shared
    ///     across clauses (§2.4) — the entry point for authoring stored definitions.</summary>
    public DiscoveryResult DiscoverQuery(QueryDiscoverySpec spec, DiscoveryOptions options);

    /// <summary>Compiles a full query — the only entry point that produces SQL.</summary>
    public QueryexResult<CompiledQuery> CompileQuery(QuerySpec spec, QueryCompilationOptions options);
}

/// <summary>The outcome of a compilation: a value or diagnostics, never an exception.</summary>
public sealed class QueryexResult<T> where T : class
{
    /// <summary>True when compilation produced a value; <see cref="Diagnostics"/> is then empty.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    public bool Succeeded { get; }

    /// <summary>The compiled value, when <see cref="Succeeded"/>.</summary>
    public T? Value { get; }

    /// <summary>The diagnostics, when not <see cref="Succeeded"/>. As complete as the input
    ///     allows: independent problems are all reported, not just the first.</summary>
    public IReadOnlyList<QueryexDiagnostic> Diagnostics { get; }
}

/// <summary>A half-open character range in the input text of one expression.</summary>
public readonly record struct QueryexSpan(int Start, int Length);

/// <summary>One compilation problem, machine-readable. Hosts localize display text from
///     <see cref="Code"/> and <see cref="Arguments"/>; the engine composes no prose.</summary>
/// <param name="Code">The stable diagnostic code, e.g. "QX3001" (Appendix A).</param>
/// <param name="Span">The offending range within the expression text named by
///     <paramref name="Location"/>.</param>
/// <param name="Location">Which input the span indexes into: a clause and item such as
///     "Select[2]" or "OrderBy[0]", a filter-tree path such as "Filter.Or[1]" (§2.3), or
///     null when validating a single expression list.</param>
/// <param name="Arguments">Named values for message composition ("name", "entity", "type", …).</param>
public sealed record QueryexDiagnostic(
    string Code,
    QueryexSpan Span,
    string? Location,
    IReadOnlyList<KeyValuePair<string, string>> Arguments);
```

Every diagnostic is an error; a severity axis is added when the first warning exists, not before.
User input produces diagnostics, never exceptions. Exceptions are reserved for **caller errors** —
malformed schemas, contradictory options, `Having` without `Aggregate` — which are host bugs, not
user input, and throw `ArgumentException` at the API boundary.

Options and engine options:

```csharp
namespace Tellma.Core.Queryex;

/// <summary>Engine-wide options. All caches are bounded: the engine accepts untrusted input,
///     and an uncapped text-keyed cache is a memory-exhaustion vector.</summary>
public sealed record QueryexEngineOptions
{
    /// <summary>Maximum cached parse results, keyed by text (§16). Default 4096.</summary>
    public int MaxCachedSyntaxTrees { get; init; }

    /// <summary>Maximum cached bound expressions (§16). Default 4096.</summary>
    public int MaxCachedBoundExpressions { get; init; }

    /// <summary>Maximum cached SQL templates (§16). Default 1024.</summary>
    public int MaxCachedQueryTemplates { get; init; }
}

/// <summary>Options for <see cref="QueryexEngine.Validate"/>.</summary>
public sealed record ValidationOptions
{
    /// <summary>The schema to bind against.</summary>
    public required QueryexSchema Schema { get; init; }

    /// <summary>The root entity paths resolve from; must belong to <see cref="Schema"/>.</summary>
    public required EntityDescriptor Root { get; init; }

    /// <summary>The position the expression is being validated for (§11).</summary>
    public required QueryexMode Mode { get; init; }

    /// <summary>Whether <c>asc</c>/<c>desc</c> suffixes are accepted. Only meaningful when
    ///     <see cref="Mode"/> has value shape; a predicate mode with directions is a caller
    ///     error (§11.4).</summary>
    public bool Directions { get; init; }

    /// <summary>The declared parameters; an undeclared parameter in the text is a diagnostic.</summary>
    public IReadOnlyList<QueryexParameterDeclaration> Parameters { get; init; } = [];

    /// <summary>Whether execution will have a signed-in user; drives <c>me()</c> (§10.6).</summary>
    public bool HasUser { get; init; } = true;

    /// <summary>The resource limits for this call site (§15).</summary>
    public QueryexLimits Limits { get; init; } = QueryexLimits.Default;
}

/// <summary>Options for <see cref="QueryexEngine.Discover"/>. All optional: discovery works on
///     anything that lexes and parses, and reports more the more it is given.</summary>
public sealed record DiscoveryOptions
{
    /// <summary>The schema, when available; without it, paths are not resolved and parameter
    ///     types are not inferred.</summary>
    public QueryexSchema? Schema { get; init; }

    /// <summary>The root entity, when a schema is supplied.</summary>
    public EntityDescriptor? Root { get; init; }

    /// <summary>The intended mode, when known; enables mode diagnostics. Consulted by
    ///     <see cref="QueryexEngine.Discover"/> only — <see cref="QueryexEngine.DiscoverQuery"/>
    ///     derives each clause's mode itself.</summary>
    public QueryexMode? Mode { get; init; }

    /// <summary>The resource limits for this call site (§15).</summary>
    public QueryexLimits Limits { get; init; } = QueryexLimits.Default;
}

/// <summary>A declared parameter: its name, type, and whether a value is guaranteed present.</summary>
public sealed record QueryexParameterDeclaration(string Name, QueryexType Type, bool IsNotNull);
```

Validation results:

```csharp
namespace Tellma.Core.Queryex;

/// <summary>The outcome of validating an expression list without emitting SQL.</summary>
public sealed record ValidatedExpression(IReadOnlyList<ValidatedItem> Items);

/// <summary>One validated item of an expression list.</summary>
/// <param name="Span">The item's range in the input text.</param>
/// <param name="Type">The static type (§7.1).</param>
/// <param name="Nullity">The nullity (§8).</param>
/// <param name="Direction">The direction suffix, when directions are enabled and one was written.</param>
/// <param name="UsesAggregation">True when the item contains an aggregation.</param>
public sealed record ValidatedItem(
    QueryexSpan Span,
    QueryexType Type,
    QueryexNullity Nullity,
    QueryexDirection Direction,
    bool UsesAggregation);

/// <summary>An ordering direction suffix.</summary>
public enum QueryexDirection
{
    /// <summary>No suffix written.</summary>
    None,
    /// <summary>The <c>asc</c> suffix.</summary>
    Ascending,
    /// <summary>The <c>desc</c> suffix.</summary>
    Descending,
}
```

`Validate` exists so that stored expressions (report definitions, permission criteria) are checked
at save time with the same binder that will compile them at run time; there is no separate,
drift-prone validation path. It deliberately returns no SQL: fragments compiled in isolation
cannot share joins, aliases, or parameters with the query they would eventually join (§2.2), so a
public fragment-SQL API would be an invitation to concatenate — the exact failure `FilterTree`
exists to prevent. `CompileQuery` is the only door to SQL.

## 2. Queries and composition

### 2.1 `CompileQuery`

```csharp
namespace Tellma.Core.Queryex;

/// <summary>One query over one root entity, clause by clause.</summary>
public sealed record QuerySpec
{
    /// <summary>The root entity; must belong to the compilation's schema.</summary>
    public required EntityDescriptor Root { get; init; }

    /// <summary>The select list: comma-separated value expressions. Binds in Value mode, or in
    ///     Aggregate mode when <see cref="Aggregate"/> is set.</summary>
    public required string Select { get; init; }

    /// <summary>True for a grouped query: the select and order-by bind on the Group axis, paths
    ///     outside aggregations become grouping keys, and <see cref="Having"/> is permitted (§11).</summary>
    public bool Aggregate { get; init; }

    /// <summary>The row-level predicate; binds in Filter mode. Row-level always — in an
    ///     aggregate query it filters the rows that enter the groups.</summary>
    public FilterTree? Filter { get; init; }

    /// <summary>The group-level predicate; binds in AggregateFilter mode. Caller error unless
    ///     <see cref="Aggregate"/> is set.</summary>
    public FilterTree? Having { get; init; }

    /// <summary>The ordering list, directions enabled; binds in Value or Aggregate mode per
    ///     <see cref="Aggregate"/>.</summary>
    public string? OrderBy { get; init; }

    /// <summary>Rows to skip. Paging requires <see cref="OrderBy"/> (§13.4).</summary>
    public int? Skip { get; init; }

    /// <summary>Maximum rows to return. Paging requires <see cref="OrderBy"/> (§13.4).</summary>
    public int? Take { get; init; }
}

/// <summary>Options for <see cref="QueryexEngine.CompileQuery"/>.</summary>
public sealed record QueryCompilationOptions
{
    /// <summary>The schema to bind against.</summary>
    public required QueryexSchema Schema { get; init; }

    /// <summary>The declared parameters, shared by every clause.</summary>
    public IReadOnlyList<QueryexParameterDeclaration> Parameters { get; init; } = [];

    /// <summary>Whether execution will have a signed-in user; drives <c>me()</c> (§10.6).</summary>
    public bool HasUser { get; init; } = true;

    /// <summary>The zero-based position of this query among the compiled queries the host will
    ///     execute as one T-SQL command (batch fetching). Namespaces every engine-emitted
    ///     parameter and variable name so compiled queries — and the host's own raw SQL —
    ///     compose side by side without collisions (§13.1). Default 0 for a query executed
    ///     alone.</summary>
    public int BatchOrdinal { get; init; }

    /// <summary>The resource limits for this call site (§15).</summary>
    public QueryexLimits Limits { get; init; } = QueryexLimits.Default;
}

/// <summary>A compiled query: SQL text plus everything the host needs to execute it and to
///     interpret its result set.</summary>
public sealed record CompiledQuery
{
    /// <summary>The T-SQL batch. Executes as command text with the parameters of
    ///     <see cref="Parameters"/> bound; never string-interpolated further.</summary>
    public required string Sql { get; init; }

    /// <summary>The parameter slots, in declaration order (§13.1).</summary>
    public required IReadOnlyList<QueryexParameterSlot> Parameters { get; init; }

    /// <summary>The result-set columns, positional to the select list.</summary>
    public required IReadOnlyList<QueryexColumn> Columns { get; init; }
}

/// <summary>One result-set column.</summary>
/// <param name="Ordinal">The zero-based column position.</param>
/// <param name="Text">The select item's source text, for display and diagnostics.</param>
/// <param name="Type">The static type (§7.1).</param>
/// <param name="Nullity">The nullity (§8): whether the column can carry absent values.</param>
/// <param name="Path">The resolved logical path segments when the item is a bare path — what an
///     entity materializer keys on; null for computed expressions.</param>
/// <param name="IsGroupingKey">True when the query is aggregate and this column is one of its
///     grouping keys.</param>
public sealed record QueryexColumn(
    int Ordinal,
    string Text,
    QueryexType Type,
    QueryexNullity Nullity,
    IReadOnlyList<string>? Path,
    bool IsGroupingKey);
```

`Skip`/`Take` are caller-supplied integers, not expression text; negative values are caller errors.
Their *values* still reach SQL as parameters, so paging does not fragment the plan cache.

A count query needs no dedicated API: `Select = "count()"` with `Aggregate = true` and no grouping
keys is a grand total. Likewise `DISTINCT` needs no keyword: an aggregate query whose select list
is all paths and no aggregations returns the distinct combinations by construction.

### 2.2 One emit context

Clauses must not be compiled independently and concatenated. `CompileQuery` binds every clause
against one shared emit context — one join trie, one alias sequence, one parameter table, one set
of value bindings — so a navigation referenced from both `Filter` and `Select` resolves to one join
with one alias, and a value binding required by the `WHERE` fragment is known before the `FROM`
clause is written.

Clauses bind in a fixed order — `Filter`, `Select`, `Having`, `OrderBy` — and the leaves of a
`FilterTree` in pre-order, so alias and parameter assignment are deterministic (§13.5) regardless
of which clauses are present or how a filter tree is shaped.

### 2.3 Filter composition — `FilterTree`

A predicate clause is not a single string. It is a tree whose leaves are independent expressions
and whose interior nodes are logical connectives. The engine parses and binds each leaf itself,
then combines the bound trees structurally; a caller never produces or consumes a bound tree, and
never concatenates text.

```csharp
namespace Tellma.Core.Queryex;

/// <summary>
///     A predicate composed structurally from independent expression leaves. Plain data:
///     serializable, loggable, diffable. The conjunction of complete trees at the root is what
///     makes composition safe — no operator inside a leaf can bind more tightly than the
///     connective above it, which textual concatenation cannot guarantee.
/// </summary>
public abstract class FilterTree
{
    private protected FilterTree();

    /// <summary>A single expression. Empty or whitespace text is a caller error: "empty means
    ///     unrestricted" is the right reading for a user filter and a catastrophic one for a
    ///     permission criterion, and the type cannot tell them apart — a caller with nothing to
    ///     contribute omits the node instead.</summary>
    public static FilterTree Leaf(string text);

    /// <summary>The conjunction of <paramref name="children"/>. Empty is <c>true</c>.</summary>
    public static FilterTree And(IReadOnlyList<FilterTree> children);

    /// <summary>The disjunction of <paramref name="children"/>. Empty is <c>false</c>: an empty
    ///     set of permissions must deny access, not grant it. A fold with the wrong identity is a
    ///     privilege-escalation bug, which is why the value is fixed here rather than left to a
    ///     helper.</summary>
    public static FilterTree Or(IReadOnlyList<FilterTree> children);

    /// <summary>The negation of <paramref name="operand"/>.</summary>
    public static FilterTree Not(FilterTree operand);
}
```

The concrete node types are public sealed types nested under `FilterTree`, so callers can pattern-
match, serialize, and log trees.

The motivating case is access control — a user's filter conjoined with the disjunction of the
criteria attached to their permissions:

```csharp
FilterTree filter = FilterTree.And([
    FilterTree.Leaf(userFilter),
    FilterTree.Or([.. criteria.Select(FilterTree.Leaf)]),
]);
```

Concatenating the same thing textually is not merely inelegant, it is wrong: appending a criterion
`C` onto a user filter of `A or B` yields `A or B and C`, which grants access to every row matching
`A`.

Requirements:

- **Every leaf binds in the clause's mode.** A `Filter` tree binds every leaf as `Filter`; a
  `Having` tree binds every leaf as `AggregateFilter`. Access-control criteria are row-level and
  therefore belong in `Filter`, never in `Having`.
- **Diagnostics carry a tree location.** A diagnostic from a leaf reports its path within the tree
  (`Filter.Or[1]`) in `QueryexDiagnostic.Location`, so a failure is attributable to a specific
  criterion rather than to "the filter".
- **Leaves bind in pre-order**, so alias and parameter assignment stay deterministic regardless of
  tree shape.
- **Each leaf caches on its own text** (§16), so a criterion shared across requests is parsed and
  bound once.

### 2.4 `Discover`, `DiscoverQuery`, and parameter inference

Authoring tools need to know what an expression *refers to* before, and independently of, whether
it compiles. `Discover` succeeds on any input that lexes and parses: `@Name` is lexically
identifiable, so a report designer can enumerate and type parameters for an expression that still
has an unresolved property name elsewhere in it.

```csharp
namespace Tellma.Core.Queryex;

/// <summary>What an expression refers to. Populated as far as the input and options allow:
///     lexical facts always; paths and inferred types only when a schema was supplied.</summary>
public sealed record DiscoveryResult(
    IReadOnlyList<ParameterUse> Parameters,
    IReadOnlyList<PathUse> Paths,
    IReadOnlyList<string> Functions,
    bool UsesAggregation,
    IReadOnlyList<QueryexDiagnostic> Diagnostics);

/// <summary>A span within one of a compilation's input texts, named the way diagnostics are:
///     <see cref="Location"/> is null for a single expression list, or a clause-and-item path
///     such as "Select[2]" or "Filter.Or[1]".</summary>
public sealed record QueryexTextSite(string? Location, QueryexSpan Span);

/// <summary>One named parameter's uses across the input.</summary>
/// <param name="Name">The parameter name, without the <c>@</c>.</param>
/// <param name="Occurrences">Every occurrence's site.</param>
/// <param name="InferredType">The solved type when inference found exactly one (§6.4); null when
///     unconstrained or conflicting, or when no schema was supplied.</param>
/// <param name="Conflicts">When uses demand incompatible types: each demanded type with the site
///     that demanded it. Empty otherwise.</param>
public sealed record ParameterUse(
    string Name,
    IReadOnlyList<QueryexTextSite> Occurrences,
    QueryexType? InferredType,
    IReadOnlyList<TypeConflict> Conflicts);

/// <summary>One incompatible type demand on a parameter.</summary>
public sealed record TypeConflict(QueryexType Type, QueryexTextSite Site);

/// <summary>One resolved path use.</summary>
public sealed record PathUse(IReadOnlyList<string> Segments, QueryexTextSite Site);
```

A stored definition's parameters are referenced across its clauses, and inference must see every
use together. Per-clause discovery is not merely inconvenient, it is wrong in one case:
variable-to-variable links. With `@a = @b` in the filter and `@a > PostingDate` in the ordering,
`@b` is only datable *through* `@a` — a link that per-clause results discard, so no caller-side
merge can recover it. `DiscoverQuery` therefore runs one shared inference context over all the
clauses of a query under authoring:

```csharp
namespace Tellma.Core.Queryex;

/// <summary>The clauses of a query under authoring — a lax mirror of <see cref="QuerySpec"/>
///     for <see cref="QueryexEngine.DiscoverQuery"/>. Every clause is optional, so a
///     half-written definition still discovers.</summary>
public sealed record QueryDiscoverySpec
{
    /// <summary>The select list, when present.</summary>
    public string? Select { get; init; }

    /// <summary>True when the query under authoring is aggregate; fixes each clause's mode.</summary>
    public bool Aggregate { get; init; }

    /// <summary>The filter, when present.</summary>
    public FilterTree? Filter { get; init; }

    /// <summary>The aggregate filter, when present; caller error unless
    ///     <see cref="Aggregate"/>.</summary>
    public FilterTree? Having { get; init; }

    /// <summary>The ordering list, when present.</summary>
    public string? OrderBy { get; init; }
}
```

Each clause binds in the mode `CompileQuery` would give it, and every occurrence, conflict, and
path in the result carries its site. The designer flow: `DiscoverQuery` proposes declarations,
the author confirms or overrides them, and the definition is stored with its declarations — from
then on `Validate` checks each clause independently and inference plays no further part.

Inference semantics are specified in §6.4. Inference is for authoring only: `Validate` and
`CompileQuery` require every parameter declared (QX3007) — an expression must not acquire a type by
accident at run time.

## 3. The schema contract

The engine binds against a `QueryexSchema`: an immutable, host-built description of the entities a
root can reach. The future EF-adapter builds one per distribution model; the inspection tool builds
one by hand; tests build fixtures.

Every schema member carries two names: a **logical name**, written by expression authors and
resolved by the binder, and a **physical source**, used only by the emitter. The two are never
interchangeable, and keeping them as distinct fields makes leaking a logical name into SQL
structurally impossible rather than a discipline to maintain.

```csharp
namespace Tellma.Core.Queryex;

/// <summary>An immutable entity schema. Built once per model version via
///     <see cref="QueryexSchemaBuilder"/>, which validates the host's input and resolves
///     cross-references; a malformed schema throws at build time and never becomes a user
///     diagnostic.</summary>
public sealed class QueryexSchema
{
    /// <summary>An opaque version discriminator that participates in every cache key. Must
    ///     change whenever any logical name, physical name, type, nullability, uniqueness, or
    ///     relationship changes — a content hash of the model is the natural choice.</summary>
    public string Version { get; }

    /// <summary>The entities, in registration order.</summary>
    public IReadOnlyList<EntityDescriptor> Entities { get; }

    /// <summary>Finds an entity by logical name (case-insensitive); null when absent.</summary>
    public EntityDescriptor? FindEntity(string name);
}

/// <summary>One entity: a queryable root or navigation target.</summary>
public sealed class EntityDescriptor
{
    /// <summary>The logical entity name, e.g. "Invoice".</summary>
    public string Name { get; }

    /// <summary>The physical source: a bracket-quoted, schema-qualified table or view name,
    ///     e.g. "[gl].[Invoices]". Host-authored; emitted verbatim.</summary>
    public string Source { get; }

    /// <summary>The single-column surrogate key. Every platform entity has one; it anchors join
    ///     conditions and the deterministic paging tiebreaker (§13.4).</summary>
    public PropertyDescriptor Key { get; }

    /// <summary>The scalar properties, <see cref="Key"/> included.</summary>
    public IReadOnlyList<PropertyDescriptor> Properties { get; }

    /// <summary>The navigations (each a many-to-one reference to another entity).</summary>
    public IReadOnlyList<NavigationDescriptor> Navigations { get; }

    /// <summary>The hierarchy-node property (type <see cref="QueryexType.HierarchyId"/>) when
    ///     the entity is hierarchical; enables <c>descendantOf</c> (§10.10). Null otherwise.</summary>
    public PropertyDescriptor? TreeNode { get; }
}

/// <summary>One scalar property.</summary>
public sealed class PropertyDescriptor
{
    /// <summary>The logical property name, e.g. "PostingDate".</summary>
    public string Name { get; }

    /// <summary>The physical column name, unquoted; the emitter bracket-quotes it.</summary>
    public string Column { get; }

    /// <summary>The Queryex type (§7.1).</summary>
    public QueryexType Type { get; }

    /// <summary>True when the column cannot hold NULL. The nullity analysis (§8) builds on it;
    ///     it must be truthful.</summary>
    public bool IsNotNull { get; }

    /// <summary>True when the column is backed by a unique constraint or index. Must be
    ///     truthful; <c>descendantOf</c> (§10.10) depends on it for correctness.</summary>
    public bool IsUnique { get; }

    /// <summary>The physical column type, structured — never free text. Consulted only to type
    ///     parameter slots whose values are compared against this column (§13.1); the language
    ///     semantics never read it. Null applies the default parameter mapping.</summary>
    public QueryexStoreType? StoreType { get; }
}

/// <summary>One navigation: a foreign key on this entity referencing the target's key.</summary>
public sealed class NavigationDescriptor
{
    /// <summary>The logical navigation name, e.g. "Customer".</summary>
    public string Name { get; }

    /// <summary>The target entity. The join condition equates <see cref="ForeignKey"/> with the
    ///     target's <see cref="EntityDescriptor.Key"/>.</summary>
    public EntityDescriptor Target { get; }

    /// <summary>The foreign-key property on the declaring entity. Its
    ///     <see cref="PropertyDescriptor.IsNotNull"/> drives join kind (§12.2) and path nullity
    ///     (§6.2).</summary>
    public PropertyDescriptor ForeignKey { get; }
}

/// <summary>A SQL Server column type as a closed, structured set — a family plus its facets.
///     Structured rather than textual so no host-supplied type text can reach emitted SQL.
///     Carries static factory helpers (<c>QueryexStoreType.Int</c>,
///     <c>QueryexStoreType.NVarChar(255)</c>, <c>QueryexStoreType.Decimal(19, 4)</c>, …).</summary>
/// <param name="Family">The type family.</param>
/// <param name="Size">Length for the character families (null = max), precision for
///     <see cref="QueryexStoreFamily.Decimal"/>, fractional-second scale for the time families;
///     null where the family has no such facet.</param>
/// <param name="Scale">The scale, for <see cref="QueryexStoreFamily.Decimal"/> only.</param>
public readonly record struct QueryexStoreType(
    QueryexStoreFamily Family,
    int? Size = null,
    int? Scale = null);

/// <summary>The closed set of SQL Server type families a column may declare.</summary>
public enum QueryexStoreFamily
{
    Bit, TinyInt, SmallInt, Int, BigInt, Decimal, Float, Real,
    Char, VarChar, NChar, NVarChar,
    UniqueIdentifier, Date, DateTime, DateTime2, DateTimeOffset,
    HierarchyId, Geography,
}
```

Descriptors reference each other directly (cycles included — self-referencing hierarchies are
ordinary), so the graph is built in two phases through `QueryexSchemaBuilder`: declare entities,
properties, and navigations by name; `Build()` links names to descriptors and validates. In
sketch:

```csharp
var b = new QueryexSchemaBuilder(version: modelHash);
var account = b.Entity("Account", source: "[gl].[Accounts]");
account.Key("Id", QueryexType.Numeric, column: "Id");
account.Property("Concept", QueryexType.String, column: "Concept", isNotNull: true, isUnique: true);
account.Property("Node", QueryexType.HierarchyId, column: "Node", isNotNull: true);
account.TreeNode("Node");
account.Navigation("Parent", target: "Account", foreignKey: "ParentId");
QueryexSchema schema = b.Build();
```

Rules, enforced by `Build()` as caller errors:

- Logical property and navigation names are unique within an entity **case-insensitively** (path
  resolution is case-insensitive, so a case-only collision would be ambiguous).
- Every entity has a `Key`; every navigation's foreign key unifies in type with its target's key;
  a `TreeNode` property has type `HierarchyId`.
- A declared `StoreType` must belong to its property's Queryex type (`VarChar` under `String`,
  `Int` under `Numeric`, …).
- `Source` is non-empty and `Column` values are non-empty and unique per entity.

**The injection boundary.** `Source` and `Column` are host-authored and are the only host-supplied
text that ever appears in emitted SQL. Neither may be derived from expression text, parameter
values, or any other user input. Everything else in the SQL is engine-authored; logical names never
appear in it — the binder resolves a path to descriptors, and the emitter reads only `Column` and
`Source` from them.

**Deliberately absent from the contract:**

- **Collections.** Platform entities have no parent-to-child collection navigations by design, so
  the schema has no collection concept and no path can traverse one. This is also what makes
  aggregation sound with no cardinality analysis: every navigation is many-to-one, so the join
  tree cannot fan out and aggregates over the root grain never double-count.
- **Computed properties.** Every property maps to exactly one physical column. A property whose
  value is an expression over other columns is out of scope; a host that needs one exposes it as a
  column of a view named by `Source`.
- **Unmappable columns.** A column whose store type has no Queryex type (`time`, `varbinary`,
  `json`, `xml`, …) is simply not declared; the property does not exist to the language. Enum
  properties are declared as their store type: `Numeric` when stored as integers, `String` when
  stored as strings.

## 4. Lexical structure

### 4.1 General

Input is Unicode text. Whitespace (Unicode `White_Space`) separates tokens and is otherwise
insignificant except inside string literals. There are no comments.

Lexing is **maximal munch**: at each position the lexer produces the longest token that matches.
Every token carries a `QueryexSpan` used by every downstream diagnostic. Token kinds: `Number`,
`String`, `Identifier`, `Keyword`, `Parameter`, `Punctuator`, `EndOfInput`.

### 4.2 Identifiers and keywords

```
Identifier      ::= IdentStart { IdentPart } | "[" { any char except "]" } "]"
IdentStart      ::= UnicodeLetter | "_"
IdentPart       ::= UnicodeLetter | UnicodeDigit | "_"
```

The lexer scans an identifier, then looks the text up in the keyword table; a hit reclassifies the
token as `Keyword`. There is no character-level lookaround, so `Notes`, `Ordering`, and `Internal`
are ordinary identifiers with no special handling.

**Reserved keywords** (case-insensitive):

```
and   or   not   in   is   null   true   false   asc   desc
```

**Bracketed identifiers** escape the reserved list and any future keyword: `[not]`, `[Order]`. The
brackets are not part of the name. An unterminated bracket is QX1002; an empty bracket pair is
QX1005.

Function names are **not** reserved. An identifier is a function name only when the next token is
`(` (§5.3), so an entity may have a property named `Count`.

### 4.3 Numeric literals

```
Number ::= digit { digit } [ "." digit { digit } ]
```

A digit is required on both sides of the decimal point: `1`, `0`, `3.14`, `0.500` are valid; `.5`,
`5.`, `1e3`, `0x0A`, `1_000` are not (QX1003).

- Numbers denote **exact decimals**, never binary floating point.
- The written scale is retained on the literal and participates in result-type reasoning (§9.1).
- A literal exceeding 38 significant digits — the backend's decimal domain — is QX1004.

Negative values are written with the unary minus operator; there is no negative literal.

### 4.4 String literals

Delimited by `'`. A literal quote is written `''`. No other escape mechanism exists; backslash is
an ordinary character.

```
'Hello'          ⇒ Hello
''               ⇒ (empty)
'It''s fine'     ⇒ It's fine
```

No lexical analysis occurs between the delimiters. An unterminated literal is QX1001, whose span
covers from the opening quote to end of input.

### 4.5 Parameters

```
Parameter ::= "@" Identifier
```

One token. Parameter names match declarations case-insensitively; an undeclared parameter is
QX3007 (outside `Discover`, §6.4). A `@` not followed by an identifier start is QX1005.

*Rationale.* Named parameters exist so that stored, reusable expressions can be given values at
run time without any caller splicing values into expression text.

### 4.6 Punctuators

```
!=   <=   >=   ||   =   <   >   +   -   *   /   %   (   )   ,   .
```

Any other character outside a string literal is QX1005.

## 5. Syntax

### 5.1 Input shape

```
Input          ::= ExpressionList
ExpressionList ::= Item { "," Item }
Item           ::= Expression [ "asc" | "desc" ]
```

A parse yields an ordered list of items. **Empty items are errors** (QX2003) — a leading, trailing,
or doubled comma hides typos in stored configuration.

A direction suffix is accepted only when directions are enabled for the position (QX2007
otherwise), must be the final token of its item, and must sit at parenthesis depth zero (QX2008).

Predicate-shaped positions (§11) — filter-tree leaves — take exactly one item; a comma at depth
zero there is QX2009.

### 5.2 Grammar and precedence

```
Expression     ::= OrExpr

OrExpr         ::= AndExpr { "or" AndExpr }
AndExpr        ::= NotExpr { "and" NotExpr }
NotExpr        ::= { "not" } Comparison

Comparison     ::= Additive [ CompOp Additive
                            | "in" "(" Additive { "," Additive } ")"
                            | "is" [ "not" ] "null" ]

Additive       ::= Multiplicative { ("+" | "-" | "||") Multiplicative }
Multiplicative ::= Unary { ("*" | "/" | "%") Unary }
Unary          ::= [ "-" ] Primary

Primary        ::= Number | String | "null" | "true" | "false"
                 | Parameter
                 | Call
                 | Path
                 | "(" Expression ")"

Call           ::= Identifier "(" [ Expression { "," Expression } ] ")"
Path           ::= Identifier { "." Identifier }

CompOp         ::= "=" | "!=" | "<" | "<=" | ">" | ">="
```

Implemented as a Pratt (precedence-climbing) parser driven by this table; adding an operator must
require only a new row.

| Binding power | Operators | Form | Associativity |
|---|---|---|---|
| 90 | `(` (call), `.` (path) | postfix | left |
| 80 | `-` | prefix | — |
| 70 | `*` `/` `%` | infix | left |
| 60 | `+` `-` `\|\|` | infix | left |
| 50 | `=` `!=` `<` `<=` `>` `>=` `in` `is [not] null` | infix / postfix | **non-associative** |
| 40 | `not` | prefix | — |
| 30 | `and` | infix | left |
| 20 | `or` | infix | left |

Consequences, all intentional:

| Expression | Parses as |
|---|---|
| `-a * b` | `(-a) * b` |
| `a + b * c` | `a + (b * c)` |
| `not a = b` | `not (a = b)` |
| `a = b and c = d` | `(a = b) and (c = d)` |
| `a or b and c` | `a or (b and c)` |
| `a = b = c` | **QX2006** — comparison is non-associative |

There is exactly one spelling of each operator: no symbolic synonyms for `and`/`or`/`not`, no word
synonyms for the comparison operators.

*Rationale.* `not` binds looser than comparison, matching SQL and Python, so `not a = b` reads as
it looks; and because no `!` operator exists, the C-family reading is never available to be
mistaken for this one.

### 5.3 Calls, paths, and parentheses

- An `Identifier` immediately followed by `(` is a **call**; otherwise it begins a **path**. No
  other rule is consulted.
- Every call requires parentheses, including zero-argument calls: `today()`, `me()`.
- `()` not preceded by an identifier is QX2005.
- Empty arguments (`f(1,,2)`, `f(1,)`) are QX2004; write `null` explicitly.
- `.` is a token, so `a..b` and `a.` are syntax errors, and each path segment carries its own span
  for precise diagnostics.

### 5.4 Syntax errors and recovery

The parser reports the first syntax error of an item with a span and an expected-token set, then
attempts one recovery to the next `,` at depth zero, so a list can report more than one problem per
compile. Recovered regions become `Error` nodes that suppress downstream diagnostics; recovery must
never produce a node the binder could mistake for well-formed input.

## 6. Paths and parameters

### 6.1 Path resolution

A path is a sequence of one or more segments resolved from the root entity. All segments but the
last must resolve to navigations; the last must resolve to a scalar property.

- Resolution is by logical name, case-insensitive.
- An unknown segment is QX3001, reported with the span of that segment, not the whole path.
- A path terminating at a navigation is QX3002, reported with the span of the final segment.

Resolution yields the `PropertyDescriptor` and the chain of `NavigationDescriptor`s. Everything
downstream refers to those descriptors; the text the author wrote is retained only for
diagnostics. Because bound trees hold descriptors rather than names, a host rename can never leave
a cached tree emitting a stale column — the schema version changes, and the cache entry dies with
it (§16).

### 6.2 Path type and nullity

The type of a path is the `Type` of the resolved final property. A path is `NotNull` iff every
navigation step's foreign key is `IsNotNull` **and** the final property is `IsNotNull`; otherwise
`Nullable`.

### 6.3 Declared parameters

A parameter's type and nullity come from its declaration. Parameters are opaque to constant
folding: the engine must not assume anything about their values. A parameter is scalar; declaring
one as `HierarchyId` or `Geography` is a caller error (neither has a parameter representation,
§13.1).

### 6.4 Parameter type inference

`Discover` (§2.4) runs the binder in an inference mode where undeclared parameters are not errors:
each is assigned a fresh type variable, and checking (§7.2) solves it from the positions the
parameter appears in.

```
PostingDate >= @From        →  @From   : Date
Amount * @Rate              →  @Rate   : Numeric
contains(Memo, @Search)     →  @Search : String
```

Three outcomes are reported distinctly:

| Outcome | Condition | Result |
|---|---|---|
| **Solved** | exactly one type satisfies every use | `InferredType` set; the tool offers it as a default the author may override |
| **Unconstrained** | no use determines a type — e.g. `@a = @b` | `InferredType` null. Not an error; the author must choose. |
| **Conflicting** | uses demand incompatible types | `Conflicts` lists each type with the span that demanded it; QX3400 |

Inference never guesses nullity: nothing in an expression constrains whether a value may be
absent. Inferred parameters default to `Nullable` — the safe direction — and the author marks a
parameter required.

## 7. Type system

### 7.1 Types

```csharp
namespace Tellma.Core.Queryex;

/// <summary>The static type of an expression.</summary>
public enum QueryexType
{
    /// <summary>true / false. The one boolean type: storable, selectable, comparable — whether
    ///     it is realised in SQL as a predicate or a value is an emission concern (§13.2),
    ///     invisible in the language.</summary>
    Bool,
    /// <summary>Exact decimal, at most 38 significant digits.</summary>
    Numeric,
    /// <summary>Unicode text.</summary>
    String,
    /// <summary>A globally unique identifier.</summary>
    Guid,
    /// <summary>A calendar date.</summary>
    Date,
    /// <summary>A date and time without offset.</summary>
    DateTime,
    /// <summary>An instant with a UTC offset.</summary>
    DateTimeOffset,
    /// <summary>A node in a hierarchy.</summary>
    HierarchyId,
    /// <summary>A spatial value.</summary>
    Geography,
}
```

Two internal type properties gate operations:

- **Equatable** — every type except `Geography`. Required by `=`, `!=`, `in`, and grouping keys
  (SQL Server cannot equate or group spatial values; the language surfaces that as a bind-time
  QX3201 rather than a backend error).
- **Ordered** — every type except `Geography` and `Guid`. Required by the ordering comparisons,
  `min`/`max`, and ordering items. `Guid` is Equatable but not Ordered: uniqueidentifier order is
  a backend byte-shuffling accident, not a meaning.

An approximate-precision column (`float`, `real`) is declared `Numeric` like any other numeric
column; arithmetic over it is the backend's floating arithmetic. `HierarchyId` and `Geography`
have no literal form and no parameter form; they enter expressions through columns only.

Internally there is one more type, `Null` — the type of the `null` literal alone. It never appears
in a public result: a well-typed expression whose only possible value is absent has the type
demanded by its context, with nullity `Null` (§8). A root-level value item whose type would be
bare `Null` (e.g. `select=null`) has no determinable column type and is QX3202; the author writes
`cast(null, 'numeric')`.

### 7.2 Bidirectional checking

The binder implements two mutually recursive judgements:

```
Synth(node)        -> Type     "what type does this have on its own?"
Check(node, Type)  -> bool     "can this be read at this type?"
```

Default relationship: `Check(e, T)` succeeds iff `Synth(e) = T` or a coercion in §7.3 applies. The
following nodes are **checkable** — they propagate `Check` into their operands rather than deciding
independently:

| Node | Propagation under `Check(·, T)` |
|---|---|
| `null` | succeeds for every `T` except `Bool` |
| string literal | succeeds when `T` is a date type or `Guid` and the text parses (§7.3) |
| `if(c, a, b)` | `Check(a, T)` and `Check(b, T)` |
| `coalesce(a₁, …)` | `Check(aᵢ, T)` for all `i` |
| `min(x [, c])`, `max(x [, c])` | `Check(x, T)` |
| parenthesised expression | `Check(inner, T)` |

**Memoization is mandatory.** The binder maintains a table keyed by (node identity, requested
type). Without it, checkable nodes nested inside checkable nodes cause repeated traversal of the
same subtree and binding time becomes exponential in nesting depth; with it, binding is linear in
node count. This is a design requirement, not an optimization — the conformance corpus includes
deeply nested checkable expressions (§18.1) that only a memoized binder compiles within limits.

### 7.3 Coercions

The complete set of implicit coercions:

| From | To | Where |
|---|---|---|
| `null` literal | any type except `Bool` | checking position |
| string **literal** | `Date`, `DateTime`, `DateTimeOffset` | checking position, when the text parses |
| string **literal** | `Guid` | checking position, when the text parses |

Nothing else converts implicitly: no numeric/string coercion in either direction, and **no
implicit conversion among the three date types** — comparing a `Date` to a `DateTimeOffset` is an
error the author resolves with `cast` or `local`. Coercion applies only to a literal token, never
to a `String`-typed column, parameter, or computed value.

String-literal date parsing accepts ISO 8601 only, culture-invariant:

```
Date            YYYY-MM-DD
DateTime        YYYY-MM-DD[T ]HH:MM[:SS[.fff]]
DateTimeOffset  <DateTime>(Z | ±HH:MM)
```

String-literal GUID parsing accepts the hyphenated form only (`8-4-4-4-12` hex digits,
case-insensitive, no braces).

### 7.4 Unification

Constructs whose operands must agree (`if`, `coalesce`, comparison, `in`) unify:

```
Unify(a, b):
    Ta ← Synth(a); Tb ← Synth(b)
    if Ta = Tb          -> Ta
    if Check(b, Ta)     -> Ta
    if Check(a, Tb)     -> Tb
    otherwise           -> QX3200
```

For *n* operands, fold left. The order is fixed, so the result is deterministic.

### 7.5 Overload resolution

Every function is a registry entry (§10.1) with one or more signatures. Resolution:

1. Select candidates by name (case-insensitive). None → QX3003 (unknown function).
2. Filter by arity. None left → QX3004, reported distinctly from unknown name.
3. For each candidate, `Check` each argument against the declared parameter type, binding type
   variables as first encountered.
4. Score each surviving candidate: an argument satisfied without coercion scores 2, with coercion
   scores 1.
5. The unique highest-scoring candidate wins. No survivor → QX3005, listing every candidate
   signature and the first failing argument position of each. A tie → QX3006 — and a registry
   that permits a tie is a registry defect, caught by a test over the registry itself.

## 8. Nullity analysis

### 8.1 Lattice

```
NotNull  ⊏  Nullable  ⊐  Null
```

```csharp
namespace Tellma.Core.Queryex;

/// <summary>Whether an expression can evaluate to an absent value.</summary>
public enum QueryexNullity
{
    /// <summary>Provably present for every database state the schema permits.</summary>
    NotNull,
    /// <summary>May be absent.</summary>
    Nullable,
    /// <summary>Provably absent.</summary>
    Null,
}
```

`Union(a, b)` is the wider. Nullity is a separate annotation pass over the bound tree, not a field
computed during binding or emission: it must be independently inspectable, because the emitter's
correctness depends on it and because it is the subject of a dedicated soundness test (§18.3).

### 8.2 The soundness obligation

> If the analysis assigns `NotNull` to a node, that node must not be capable of evaluating to
> absent for any database state permitted by the schema.

`Nullable` is always a safe answer; `NotNull` is a claim the emitter relies on to omit guards
(§13.3). Where presence cannot be proven, the analysis returns `Nullable`.

### 8.3 Rules

| Construct | Nullity |
|---|---|
| numeric / string / boolean literal | `NotNull` |
| `null` | `Null` |
| path | §6.2 |
| parameter | from its declaration |
| `today()`, `now()` | `NotNull` |
| `me()` | `NotNull` when `HasUser`, else `Null` |
| unary `-` | operand's |
| `+ - * / % \|\|` | `Union(left, right)` |
| comparison, `in`, `is null`, `and`, `or`, `not`, and every `Bool`-returning function | **`NotNull`** (§9.2) |
| `if(c, a, b)` | `NotNull` if both branches `NotNull`; `Null` if both `Null`; else `Nullable`. If `c` folds to a constant, the selected branch's. |
| `coalesce(a₁…aₙ)` | `NotNull` if any `aᵢ` is `NotNull`; `Null` if all are; else `Nullable` |
| scalar function | per the registry's declared nullity rule (§10.1) |
| `count(…)` | always `NotNull` |
| `sum` `avg` `min` `max` | `NotNull` iff the argument is `NotNull` **and** no condition argument is present **and** the enclosing query has at least one grouping key; otherwise `Nullable` |

*Rationale for the aggregate rule.* A filtered aggregate over a group in which no row satisfies
the condition yields no value even though the group produces a row; an ungrouped aggregate over a
filter matching nothing likewise yields one absent value. Both are ordinary in reporting; a rule
that ignored them would make negated comparisons on measures silently drop rows.

### 8.4 Folding

An expression whose nullity is `Null` in a value position emits as the typed absent value. A
predicate whose operand nullity makes it constant folds at lowering (to the fixed tokens `1 = 1` /
`1 = 0` in SQL). Folding is an optimization; the results specified in §9 hold whether or not it is
performed.

## 9. Expression semantics

"Absent" means the value is null at evaluation time.

### 9.1 Arithmetic and concatenation

| Operator | Operands | Result |
|---|---|---|
| `+` `-` `*` `/` `%` | `Numeric`, `Numeric` | `Numeric` |
| unary `-` | `Numeric` | `Numeric` |
| `\|\|` | `String`, `String` | `String` |

- `+` is arithmetic only; it is never string concatenation, so no operand is ever typed
  speculatively. `%` is the remainder.
- `+ - * %` follow the backend's exact-decimal result rules over the operand types. **`/` and
  `avg` guarantee at least six fractional digits**: the emitter widens operands so that integer
  division never truncates and `avg` over integers is not an integer (§13.6). Beyond that
  guarantee, result scale follows the backend's decimal rules.
- Division by zero is an execution-time error, not a compile error.
- **Nullity is `Union`**: if either operand is absent the result is absent — including `||`, which
  propagates absence rather than treating it as an empty string. Use `coalesce(s, '')` for the
  other behaviour.

### 9.2 Comparison

`= != < <= > >=` take two operands unified per §7.4 — Equatable for `=`/`!=`, Ordered for the rest
(QX3201 otherwise) — and produce `Bool` with nullity `NotNull`.

Comparison is **total**: defined for every combination of present and absent operands.

| | `=` | `!=` | `<` `<=` `>` `>=` |
|---|---|---|---|
| both present | ordinary comparison | complement of `=` | ordinary comparison |
| exactly one absent | `false` | `true` | `false` |
| both absent | `true` | `false` | `false` |

Consequences to document for users:

- `not (a = b)` and `a != b` always agree: `!=` is the exact complement of `=`.
- `x = null` works as an absence test, though `x is null` is the preferred spelling.
- Ordering comparisons are false when either side is absent, so `a < b or a >= b` is **not** a
  tautology, and `not (a < b)` is not `a >= b`. A range filter `x >= @from and x <= @to` excludes
  rows where `x` is absent; "before this date, or no date" is written explicitly as
  `x < @d or x is null`.

> **Implementation constraint.** No pass may rewrite a negated ordering comparison by flipping the
> operator: `not (a < b)` → `a >= b` is invalid for any operand that is not `NotNull`, and the
> same applies to De Morgan rewrites that flip ordering operators as a side effect. Only the
> `=`/`!=` complement may be used this way.

`Bool` operands compare like any other type: `IsPosted = IsApproved` is well-typed. String
comparison follows the column's backend collation; the engine neither normalises nor overrides.

### 9.3 `is null` / `is not null`

Postfix, one operand of any type, result `Bool`/`NotNull`. `x is null` is `true` exactly when `x`
is absent — equivalent to `x = null`, provided because it states the intent directly and reads
better under `not`.

### 9.4 `in`

```
value in (e₁, e₂, …, eₙ)      n ≥ 1
```

All operands unify per §7.4 (Equatable). Result `Bool`/`NotNull`: `true` iff `value` equals some
`eᵢ` under the **total** equality of §9.2 — an absent value matches an absent element and nothing
else.

*Guidance.* `in` exists so set membership reaches the backend as one `IN` predicate rather than a
chain of disjunctions, which materially affects plan quality on indexed columns.

### 9.5 Logical operators

`and`, `or`, `not` take `Bool` operands and produce `Bool`/`NotNull`. Ordinary two-valued truth
tables apply; §8.3 guarantees there are no absent cases to define. Operands are pure, so the
engine may reorder them and fold constants, and is not required to short-circuit.

### 9.6 Evaluation model

Expressions are pure and total apart from backend arithmetic faults. The engine may evaluate any
subexpression zero or more times, in any order; nothing in the language may be given semantics
that depend on evaluation count or order.

## 10. Function library

### 10.1 Registry

Functions are declarations, not code paths in the binder:

```csharp
namespace Tellma.Core.Queryex;

// Internal: the registry is not public API. Shown because the shape is normative — adding a
// function must touch a registry entry and an emit template, nothing else.
internal sealed record FunctionDefinition(
    string Name,                                  // matched case-insensitively
    FunctionCategory Category,                    // Scalar | Aggregate
    IReadOnlyList<FunctionSignature> Signatures);

internal sealed record FunctionSignature(
    IReadOnlyList<FunctionParameter> Parameters,
    TypeSpec Returns,                             // concrete type or a bound type variable
    NullityRule Nullity,                          // AlwaysNotNull | Union(indices) | Propagate(index)
    EmitTemplate Emit);

internal sealed record FunctionParameter(
    TypeSpec Type,                                // concrete type, OneOf(types), or Var(name, bound)
    bool Optional,
    ParameterConstraint Constraint);              // None | LiteralOnly | MemberOf(set)
```

Type variables bind on first use within a signature; the bound `Ordered` admits the Ordered types
(§7.1), `Any` admits all. `LiteralOnly` arguments are compile-time selectors: they choose an emit
template and never reach the backend as data (QX3100 when not a literal; QX3101 when outside a
`MemberOf` set). Registry-declared `AlwaysNotNull` obliges the emit template to be **total** —
false (or a value) for absent operands — which the differential suite verifies per function
(§18.3).

### 10.2 Aggregations

```
count()                         -> Numeric      rows in the group
count(x: Any)                   -> Numeric      rows where x is present
count(x: Any, c: Bool)          -> Numeric      rows where c and x is present
sum(x: Numeric [, c: Bool])     -> Numeric
avg(x: Numeric [, c: Bool])     -> Numeric
min(x: T: Ordered [, c: Bool])  -> T
max(x: T: Ordered [, c: Bool])  -> T
```

- The optional final argument is a per-row `Bool` filter; rows failing it contribute nothing
  (emitted as `CASE WHEN c THEN x END` inside the aggregate).
- Aggregations may not nest at any depth (QX4003, on the inner call's span).
- Aggregations are permitted only where the mode's grouping axis is `Group` (QX4002).
- Nullity per §8.3.

### 10.3 Date and time — the instant/calendar split

Date operations divide into two kinds, and conflating them is the most common source of silently
wrong reports.

**Instant operations** — elapsed time, ordering, adding fixed-length units — are independent of
any time zone and accept all three date types.

**Calendar operations** — extracting a year or month, truncating to a period boundary, adding
variable-length units — depend on a time zone, because a calendar boundary is a position in
*someone's* local time: `2024-01-01T00:30+03:00` is in one year by its own offset and another in
UTC.

**Rule.** Calendar operations do **not** accept `DateTimeOffset`. The zone is made explicit first:

```
local(d: DateTimeOffset [, tz: String])  -> DateTime
```

`tz` is `LiteralOnly`: an IANA zone id (`'Africa/Nairobi'`), resolved at compile time to the
backend's zone name and bound as a parameter; an unknown id is QX3101. Omitted, the tenant zone is
bound at execution through a `TimeZone` parameter slot (§13.1). Passing a `DateTimeOffset` to a
calendar function is QX3103, whose diagnostic names `local` as the fix — a targeted overload
diagnostic: wherever a `Cal`-typed parameter receives a `DateTimeOffset`, the binder reports
QX3103 in place of the generic no-matching-overload QX3005.

```
year(PostedAt)          →  QX3103
year(local(PostedAt))   →  the year in the tenant's zone
diffHours(PostedAt, now())  →  fine: instant operation, no zone needed
```

Type aliases used below:

```
Instant   = OneOf(Date, DateTime, DateTimeOffset)
Cal       = OneOf(Date, DateTime)     // zone-resolved
TimeOfDay = DateTime                  // has a time component and a zone
```

*Rationale.* This turns a data-dependent wrong answer — one that varies row by row with the stored
offset and is nearly invisible in a report — into a compile error with one obvious fix.

### 10.4 Date and time — calendar operations

```
year(d: Cal [, cal])     -> Numeric
quarter(d: Cal [, cal])  -> Numeric
month(d: Cal [, cal])    -> Numeric
day(d: Cal [, cal])      -> Numeric
weekday(d: Cal)          -> Numeric      1 = Monday … 7 = Sunday
hour(d: TimeOfDay)       -> Numeric
minute(d: TimeOfDay)     -> Numeric
second(d: TimeOfDay)     -> Numeric

startOfYear(d: Cal [, cal])   -> Date
startOfMonth(d: Cal [, cal])  -> Date
startOfDay(d: Cal)            -> Date

addYears(d: Cal, n: Numeric [, cal])   -> Cal    // variable-length: calendar-aware
addMonths(d: Cal, n: Numeric [, cal])  -> Cal    // clamps to the target month's last valid day

diffYears(d1: Cal, d2: Cal [, cal])   -> Numeric   whole calendar years,  d2 − d1
diffMonths(d1: Cal, d2: Cal [, cal])  -> Numeric   whole calendar months, d2 − d1
```

`cal` is `String`, `LiteralOnly`, `MemberOf(CalendarCodes)` — a compile-time selector that chooses
an emit template. **This version implements `'gc'` (Gregorian, the default) only**; `'uq'`
(Umm al-Qura) and `'et'` (Ethiopian) are reserved codes that today produce QX3101.

The reserved codes land with the Locale packs as additive registry changes, and their emission
strategies are recorded here so the language surface is known not to change: `'et'` is
algorithmic and emits as inline date arithmetic; `'uq'` is table-based (no closed formula exists)
and emits lookups against a host-provided Hijri month-map table named through the schema contract,
seeded from .NET's Umm al-Qura data — with `Nullable` results, since dates outside the mapped
range have no answer.

Nullity: `Propagate(0)` for extraction and truncation, `Union(0, 1)` for `add` and `diff`.

### 10.5 Date and time — instant operations

```
addDays(d: Instant, n: Numeric)     -> Instant    // fixed-length units
addHours(d: Instant, n: Numeric)    -> Instant
addMinutes(d: Instant, n: Numeric)  -> Instant
addSeconds(d: Instant, n: Numeric)  -> Instant

diffDays(d1: Instant, d2: Instant)     -> Numeric   fractional
diffHours(d1: Instant, d2: Instant)    -> Numeric   fractional
diffMinutes(d1: Instant, d2: Instant)  -> Numeric   fractional
diffSeconds(d1: Instant, d2: Instant)  -> Numeric   whole seconds
```

The date is the first argument in every case; `n` may be negative; the return type equals the
first argument's type. `addMonths`/`addYears` on a `DateTimeOffset` is QX3103 — a variable-length
unit is calendar-dependent — while `addDays(PostedAt, 1)` is fine.

The `diff` functions measure **elapsed time**, not calendar distance: each is computed in the next
finer unit and divided, so `diffDays` has hour resolution. For calendar distance — "how many day
boundaries lie between these values" — truncate first, which forces the zone question into the
open: `diffDays(startOfDay(local(a)), startOfDay(local(b)))`.

Nullity: `Union(0, 1)`.

### 10.6 Context

```
today()  -> Date
now()    -> DateTimeOffset
me()     -> Numeric
```

Zero arguments, parentheses required. Each compiles to a parameter slot the host binds at
execution (§13.1): `today()` the current date in the tenant's zone, `now()` the current instant,
`me()` the current user's id — one slot each, so every use in a statement sees the same value.

`me()` has nullity `NotNull` when the compilation's `HasUser` is set, else `Null` — making
`CreatedById = me()` a provably false predicate for an anonymous caller, which folds at lowering.
`HasUser` therefore participates in the cache keys (§16).

### 10.7 Conditional

```
if(c: Bool, a: T: Any, b: T)    -> T
coalesce(x₁: T: Any, …, xₙ: T)  -> T        n ≥ 2
```

`if` requires exactly three arguments. `coalesce` is variadic and returns the first present
argument. Both are checkable nodes (§7.2). Nullity per §8.3.

### 10.8 Conversion

```
cast(x: Any, type: String) -> <the named type>
```

`type` is `LiteralOnly`, `MemberOf({'numeric','string','bool','guid','date','datetime',
'datetimeoffset'})` and determines the static result type.

| From \ To | Numeric | String | Bool | Guid | Date | DateTime | DateTimeOffset |
|---|---|---|---|---|---|---|---|
| Numeric | — | ✓ | ✓ (0 ⇒ false) | ✗ | ✗ | ✗ | ✗ |
| String | ✓ | — | ✗ | ✓ | ✓ | ✓ | ✓ |
| Bool | ✓ | ✓ | — | ✗ | ✗ | ✗ | ✗ |
| Guid | ✗ | ✓ | ✗ | — | ✗ | ✗ | ✗ |
| Date | ✗ | ✓ | ✗ | ✗ | — | ✓ | ✗ |
| DateTime | ✗ | ✓ | ✗ | ✗ | ✓ | — | ✗ |
| DateTimeOffset | ✗ | ✓ | ✗ | ✗ | ✓ | ✓ | — |

A conversion marked ✗ is QX3102. A conversion marked ✓ that fails at run time
(`cast('abc', 'numeric')`) is an execution-time error; the engine does not guarantee a value —
and the emitter must use the failing conversion (`CAST`), never `TRY_CAST`, whose silent NULL on
failure would violate the `Propagate(0)` nullity rule (§8.2). `DateTimeOffset` to `Date`/
`DateTime` reads the value at its own offset; for a zone-aware reading use `local` first.

String rendering by `cast(·, 'string')` is culture-invariant and stable across releases: dates
render ISO 8601, numerics render with their natural scale and no group separators, booleans render
`true`/`false`, GUIDs render lowercase hyphenated.

### 10.9 Numeric and string

```
abs(x: Numeric)                              -> Numeric
round(x: Numeric, digits: Numeric)           -> Numeric      digits: LiteralOnly
floor(x: Numeric)                            -> Numeric
ceiling(x: Numeric)                          -> Numeric

length(s: String)                            -> Numeric
left(s: String, n: Numeric)                  -> String
right(s: String, n: Numeric)                 -> String
substring(s: String, start: Numeric [, len: Numeric]) -> String
trim(s: String)                              -> String
upper(s: String)                             -> String
lower(s: String)                             -> String
replace(s: String, old: String, new: String) -> String
contains(s: String, part: String)            -> Bool
startsWith(s: String, prefix: String)        -> Bool
endsWith(s: String, suffix: String)          -> Bool
```

- `substring` is 1-based; `len` omitted means "to the end".
- The three `Bool`-returning functions are **total**: absent argument ⇒ `false`.
- Their second argument is matched **literally** — `contains(Name, '100%')` matches the characters
  `100%`. The emitter must realise this with a mechanism that has no pattern metacharacters at all
  (`CHARINDEX`-style emission), never by escaping `LIKE` patterns: escaping computed patterns
  inside SQL is exactly the fragile rewriting this rule exists to rule out. Matching sensitivity
  follows the backend collation, like comparison (§9.2).
- Nullity: `Propagate(0)` for the string-returning functions and `length`; `AlwaysNotNull` for the
  three predicates.

### 10.10 Hierarchy

```
descendantOf(key: <path>, ancestorKey: Any) -> Bool
```

True when the row's node in the hierarchy is at or below the node of the ancestor row identified
by `ancestorKey`. Descendancy is reflexive. Constraints, checked at bind time:

1. `key` must be a bare path (QX3300), and the entity it resolves through must expose a `TreeNode`
   (QX3301).
2. The property named by `key` must be `IsUnique` on that entity, so the ancestor lookup
   identifies at most one row (QX3302).
3. `ancestorKey` must contain no path — literals, parameters, and context functions only
   (QX3303) — and must unify with `key`'s type.

```
descendantOf(Account.Concept, 'Assets')
```

reads: the row's `Account` is at or below the account whose (unique) `Concept` is `'Assets'`.

When no ancestor is identified — `ancestorKey` is absent, or no row matches it — the result is
**`false`**; there is no value of `ancestorKey` that makes the predicate universally true. The
function is `Bool`/`NotNull`, so its emission is guarded to stay total: the hoisted ancestor
lookup (§12.4) and the row's own node are both null-checked, and `not descendantOf(…)` is `true`
in exactly the rows where the positive form is `false`.

## 11. Modes

### 11.1 Two axes

A mode is the pair of two independent, binary properties of the position an expression occupies:

```csharp
namespace Tellma.Core.Queryex;

/// <summary>What the result of an expression must be.</summary>
public enum QueryexShape
{
    /// <summary>A truth value: the expression must have type Bool and is emitted in predicate
    ///     position.</summary>
    Predicate,
    /// <summary>A scalar: any type, emitted in value position.</summary>
    Value,
}

/// <summary>Whether the expression sees rows or groups.</summary>
public enum QueryexGrouping
{
    /// <summary>Row by row; aggregations are forbidden.</summary>
    Row,
    /// <summary>Over groups; aggregations permitted, ungrouped paths become grouping keys.</summary>
    Group,
}

/// <summary>The position an expression is compiled for — a pair of independent axes. The named
///     accessors exist for call-site readability; they add no information.</summary>
public readonly record struct QueryexMode(QueryexShape Shape, QueryexGrouping Grouping)
{
    /// <summary>Row-level predicate — a WHERE clause.</summary>
    public static QueryexMode Filter => new(QueryexShape.Predicate, QueryexGrouping.Row);

    /// <summary>Row-level value — a SELECT or ORDER BY item.</summary>
    public static QueryexMode Value => new(QueryexShape.Value, QueryexGrouping.Row);

    /// <summary>Group-level value — a SELECT or ORDER BY item of a grouped query.</summary>
    public static QueryexMode Aggregate => new(QueryexShape.Value, QueryexGrouping.Group);

    /// <summary>Group-level predicate — a HAVING clause.</summary>
    public static QueryexMode AggregateFilter => new(QueryexShape.Predicate, QueryexGrouping.Group);
}
```

Every rule below is a consequence of one axis; no rule may depend on the combination.

### 11.2 Rules from the `Shape` axis

- `Predicate` — the expression must have type `Bool` (QX4001) and the root is emitted in
  predicate position (§12.5). Exactly one item (QX2009).
- `Value` — any type; the root is emitted in value position. A `Bool` result is permitted and is
  realised as a value (§13.2).

### 11.3 Rules from the `Grouping` axis

- `Row` — aggregations are forbidden (QX4002). Every path reads the current row.
- `Group` — aggregations are permitted. A path **enclosed in** an aggregation reads the rows of
  the group; a path **not enclosed in** one is a **grouping key** and must be of an Equatable type
  (QX3201).

There is no `group by` construct in the surface language: grouping is derived from which paths
appear outside aggregations, so a select list and its `GROUP BY` cannot disagree. The derived keys
are reported on `CompiledQuery.Columns` (`IsGroupingKey`).

`AggregateFilter` additionally requires every path to be enclosed in an aggregation (QX4004): a
`HAVING` clause that reads an ungrouped column is not expressible, which is the correct outcome —
the author meant either a grouping key or the filter.

### 11.4 Directions

Directions are constrained by `Shape`, not by `Grouping`: an ordering term may read rows or groups
equally, but only a value can be ordered by. `Directions = true` with a predicate shape is a
**caller error** at the API boundary — the two predicate modes correspond to `WHERE` and `HAVING`,
and neither clause has an ordering. Ordering *by* a boolean remains legal: in `IsPosted desc` the
expression is a value to sort on.

Through `CompileQuery` the flag is implicit — the `OrderBy` clause enables directions and no other
clause does; `ValidationOptions.Directions` exists for validating an ordering list standalone. An
ordering item must be of an Ordered type (QX3201).

## 12. Lowering

### 12.1 Intermediate representations

All internal. Their required properties:

- **`SyntaxTree`** — untyped, schema-free, one node per source construct plus `Error`; every node
  carries a span. Supports a printer to canonical text such that
  `parse(print(parse(t))) = parse(t)` (§18.4).
- **`TypedExpr`** — the bound tree: type, nullity, span, resolved descriptors or function
  signatures, children. Contains **no SQL, no aliases, no parameter names**; it is the unit of
  caching (§16) and of `FilterTree` composition.
- **`RelationalPlan`** — the lowered form: plan nodes with explicit positions (§12.5) and explicit
  null-guard nodes, the join trie, value bindings, hoisted invariants, and the parameter table.
  The emitter makes no decisions beyond dialect syntax.

### 12.2 Join planning

Collect every distinct navigation-path prefix across all clauses of the query and build a trie;
each trie node becomes one join.

- Aliases are assigned by a deterministic pre-order walk (`[P1]`, `[P2]`, …; the root is `[T]`).
- The join condition equates the child's foreign-key column with the target entity's key column.
- Join kind: a trie node joins `INNER` iff its navigation's foreign key is `IsNotNull` **and its
  parent join is `INNER`** (the root counts as `INNER`); otherwise `LEFT`. The propagation clause
  matters: behind a `LEFT` join, even a non-null foreign key may have no row — an `INNER` join
  there would silently drop rows whose optional ancestor is absent.
- Joins no surviving node references — because a subtree folded to a constant — are pruned.
- Exceeding `MaxJoins` is QX5006, not a truncation.

### 12.3 Value bindings

> The emitter must never emit a subexpression more than once unless it is a column reference or a
> parameter.

Null-guarded comparison (§13.3) naturally wants its operands twice or more. Lowering therefore
hoists any non-atomic operand of a guarded construct into a **value binding**, which the emitter
realises as a single-row lateral join — `CROSS APPLY (VALUES (…)) AS [B1](v)` — and references by
alias. One exception: inside aggregate arguments a lateral binding is not expressible, and the
backend optimizer recognises identical aggregate expressions and computes them once; textual
repetition of an identical aggregate expression is permitted there, and only there.

*Rationale.* Repetition is not a style problem: re-evaluating a non-deterministic-cost
subexpression multiplies its cost, and keeping guards correct across divergent copies is exactly
the class of bug golden files cannot catch until it ships.

### 12.4 Invariant hoisting

Subexpressions with no dependency on the current row — the ancestor lookup of `descendantOf`,
parameter-only arithmetic — are hoisted into variables declared in a prologue
(`DECLARE @qx0_v0 … = (SELECT …)`; naming per §13.1) and evaluated once per statement. Context functions need no
hoisting; they are already single parameter slots (§10.6).

### 12.5 Position assignment

**Position** is whether a node's result is consumed as a truth value or as a scalar — a property
of the node's context, not of its type, assigned by a top-down walk. The root's position comes
from the mode's `Shape` axis.

| Node | Position given to operands |
|---|---|
| `and`, `or`, `not` | `Predicate` |
| `if` | condition: `Predicate`; **both branches: `Value`** |
| comparison, `in`, `is null` | `Value` |
| everything else | `Value` |

The same node emits differently depending only on position:

| Expression | Root position | Emission of the `Bool` node |
|---|---|---|
| `filter = IsPosted` | Predicate | `[T].[IsPosted] = 1` |
| `select = IsPosted` | Value | `[T].[IsPosted]` |
| `filter = Amount > 100` | Predicate | `[T].[Amount] > @qx0_p0` |
| `select = Amount > 100` | Value | `CASE WHEN [T].[Amount] > @qx0_p0 THEN 1 ELSE 0 END` |

**The `if` rule.** `if` evaluates its branches in `Value` position regardless of its own position;
a `Bool`-typed `if` in predicate position is emitted as its `CASE` compared against 1:

```
filter = if(Amount > 0, IsPosted, false)
  →  (CASE WHEN [T].[Amount] > @qx0_p0 THEN [T].[IsPosted] ELSE 0 END) = 1
```

The alternative expansion — `(c AND a) OR (NOT c AND b)` — would emit the condition twice,
violating §12.3, and would need a value binding to repair. Comparing the `CASE` against 1 costs
nothing and evaluates the condition once.

## 13. SQL emission

### 13.1 Parameterization

Every literal, every context value, and every declared parameter reaches the backend as a bound
parameter — no user-derived value is ever interpolated into SQL text. Three deliberate exceptions
are engine-authored tokens, not data: `true`/`false` and the 0/1 of Bool realisation; the `NULL`
keyword; and `LiteralOnly` selector arguments, which choose emit templates (§10.1).

```csharp
namespace Tellma.Core.Queryex;

/// <summary>Where a parameter slot's execution-time value comes from.</summary>
public enum QueryexParameterOrigin
{
    /// <summary>A literal from the expression text; <see cref="QueryexParameterSlot.Value"/>
    ///     carries it.</summary>
    Literal,
    /// <summary>The current date in the tenant's time zone — <c>today()</c>.</summary>
    Today,
    /// <summary>The current instant — <c>now()</c>.</summary>
    Now,
    /// <summary>The current user's id — <c>me()</c>.</summary>
    UserId,
    /// <summary>The tenant's time zone, as the backend zone name — implicit <c>local()</c>.</summary>
    TimeZone,
    /// <summary>A declared parameter; <see cref="QueryexParameterSlot.DeclaredName"/> names it.</summary>
    Declared,
}

/// <summary>One parameter of a compiled query. The host binds each slot before executing:
///     literals from <see cref="Value"/>, context slots from its clock/user/tenant, declared
///     slots from the values supplied for <see cref="DeclaredName"/>.</summary>
/// <param name="Name">The parameter name as it appears in the SQL ("@qx0_p0", "@qx0_p1", …;
///     see the naming rules below).</param>
/// <param name="Type">The Queryex type.</param>
/// <param name="StoreType">The SQL type to bind the parameter as: the store type of the column
///     the value is compared against, or the default mapping for <paramref name="Type"/> when
///     no column informs the slot (see below).</param>
/// <param name="Origin">Where the value comes from.</param>
/// <param name="Value">The value, for <see cref="QueryexParameterOrigin.Literal"/> slots.</param>
/// <param name="DeclaredName">The declared parameter's name, for
///     <see cref="QueryexParameterOrigin.Declared"/> slots.</param>
public sealed record QueryexParameterSlot(
    string Name,
    QueryexType Type,
    QueryexStoreType StoreType,
    QueryexParameterOrigin Origin,
    object? Value = null,
    string? DeclaredName = null);
```

**Slot store types.** A parameter compared against a column must bind as the column's own type,
or the backend converts the *column* side of the comparison and the predicate stops being
sargable — the classic case being an `nvarchar` parameter against a `varchar` column, which
turns an index seek into a scan. A slot whose value flows into comparison or unification with a
path therefore takes that column's declared `StoreType` (§3). The default mapping applies only
to slots no column informs (literal against literal, computed positions):

| `QueryexType` | Default SQL parameter type |
|---|---|
| `Bool` | `bit` |
| `Numeric` | `decimal` with the value's own precision and scale |
| `String` | `nvarchar(4000)`; `nvarchar(max)` beyond 4000 characters |
| `Guid` | `uniqueidentifier` |
| `Date` | `date` |
| `DateTime` | `datetime2(7)` |
| `DateTimeOffset` | `datetimeoffset(7)` |

Slots are assigned in a deterministic order (clause order, pre-order within each clause).
Identical literal values sharing a store type share one slot; a declared parameter used against
columns of different store types yields one slot per store type, each carrying the same
`DeclaredName`, and the host binds them all from the one supplied value. `Skip`/`Take` values
bind through two further engine-authored slots when paging is present.

**Naming and batch composition.** Every engine-emitted name lives in a reserved namespace:
parameters are `@qx{b}_p{n}` and hoisted variables (§12.4) are `@qx{b}_v{n}`, where `{b}` is
`QueryCompilationOptions.BatchOrdinal` and `{n}` a deterministic sequence number. The host may
therefore concatenate several compiled queries — and its own raw SQL — into one command and walk
the result sets with `NextResult()`: distinct ordinals keep the engine's names disjoint, table
aliases need no namespacing because they are statement-scoped, and raw SQL composed alongside
compiled queries must avoid `@qx`-prefixed names — `@qx` is the platform's reserved parameter
namespace. The ordinal is part of the compilation's identity: same spec, same ordinal,
byte-identical SQL (§13.5).

### 13.2 Bool realisation

The single `Bool` type is realised according to position:

| Position | Node | Emission |
|---|---|---|
| `Predicate` | predicate-producing node | the predicate itself |
| `Predicate` | `Bool` value, `NotNull` | `<value> = 1` |
| `Predicate` | `Bool` value, `Nullable` | `<value> IS NOT NULL AND <value> = 1` |
| `Value` | predicate-producing node | `CASE WHEN <predicate> THEN 1 ELSE 0 END` |
| `Value` | `Bool` value | the value |

The `Nullable` predicate form is required by §9.2: an absent boolean must read as `false` even
under `not`, which a bare `<value> = 1` would get wrong once negated.

### 13.3 Null guards

Let `L` and `R` be the operand nullities. Emission of comparison:

| L | R | `=` | `!=` | ordering `op` |
|---|---|---|---|---|
| NotNull | NotNull | `l = r` | `l <> r` | `l op r` |
| NotNull | Nullable | `r IS NOT NULL AND l = r` | `r IS NULL OR l <> r` | `r IS NOT NULL AND l op r` |
| Nullable | NotNull | symmetric | symmetric | symmetric |
| Nullable | Nullable | `(l IS NULL AND r IS NULL) OR (l IS NOT NULL AND r IS NOT NULL AND l = r)` | `NOT` of the `=` form | `l IS NOT NULL AND r IS NOT NULL AND l op r` |
| Null | any | `r IS NULL` | `r IS NOT NULL` | `1 = 0` |
| any | Null | symmetric | symmetric | `1 = 0` |

Every form above is two-valued — it never evaluates to SQL `UNKNOWN` — which is what makes a plain
`NOT (…)` wrapper valid for negation and `!=`. Operands repeat only because §12.3 guarantees they
are atomic by the time they reach the emitter. Rows where an operand's *nullity* is `Null` fold at
lowering and reach this table only in the two constant rows shown.

### 13.4 Statement assembly

`CompileQuery` lays the fragments out as:

```
DECLARE @qx0_v0 …                              -- hoisted invariants (§12.4)

SELECT   <select items, in source order>
FROM     <root Source> AS [T]
         <joins from the merged trie, alias order>
         CROSS APPLY (VALUES (…)) AS [B1](v)   -- value bindings (§12.3)
WHERE    <filter>
GROUP BY <grouping keys>                       -- iff Aggregate and at least one key derived
HAVING   <aggregate filter>
ORDER BY <ordering items, tiebreakers, direction>
OFFSET <skip slot> ROWS FETCH NEXT <take slot> ROWS ONLY   -- iff paging
```

Requirements:

- An aggregate query with no grouping keys is a grand total and emits no `GROUP BY` — precisely
  the case that makes its aggregates `Nullable` (§8.3).
- **An ordering term must not alter the grouping** (QX4005). In `Group` grouping, every `ORDER BY`
  term must be an aggregation or a grouping key the select list already produces. Grouping keys
  are derived from the select list alone (§11.3); if the ordering could contribute keys, changing
  a sort would silently widen the `GROUP BY` and return more rows than were selected.
- **Paging requires an explicit ordering** (QX4006): an unordered page is not reproducible.
- **Paging appends deterministic tiebreakers.** A user ordering rarely reaches a total order, and
  paging over a partial order returns overlapping pages. When paging is present the emitter
  appends, after the user's terms: the root key ascending (`Row` grouping), or every grouping key
  not already ordered, in select order (`Group` grouping).
- **Null placement**: `asc` places absent values first, `desc` places them last. These are SQL
  Server's own defaults, so no `CASE` wrapper is emitted — but they are normative, not
  incidental: sort order is a total order even where comparison (§9.2) is not, and paging depends
  on it being stable.

### 13.5 Determinism

Given the same query spec text, schema version, options (batch ordinal included), and engine
version, emission produces byte-identical SQL with identically named parameters; only parameter
*values* vary per request.
Hash-ordered collections, culture-sensitive formatting, or unstable alias assignment anywhere in
the pipeline are defects.

### 13.6 Session-settings independence

Emitted SQL must not change meaning with session state: no bare `DATEPART(WEEKDAY, …)` (which
reads `SET DATEFIRST`), no `CONVERT` styles or date-string literals that read `SET LANGUAGE`/
`SET DATEFORMAT` (dates travel as typed parameters, §13.1), no reliance on session collation.
`weekday` emits a `DATEFIRST`-independent formula; `/` and `avg` emit the operand widening that
delivers the ≥ 6 fractional digits of §9.1. The golden corpus (§18.2) pins each of these shapes.

## 14. Diagnostics

- Every diagnostic carries a machine-readable `Code`, a `Span`, and a `Location` naming which
  input the span indexes (§1.4). Hosts localise display text from the code and arguments; the
  engine composes no user-facing prose, so nothing here constrains the platform's localization
  design.
- No backend error text ever reaches a caller through the engine, and no user input ever surfaces
  as an exception: an exception escaping `Validate`, `Discover`, or `CompileQuery` on user text is
  a defect by definition.
- Compilation reports as many independent diagnostics as the input allows — per list item, per
  filter-tree leaf, per argument position — rather than stopping at the first.
- Every code in Appendix A is reachable by at least one conformance-corpus entry asserting its
  code and span (§18.1).

## 15. Limits and safety

Enforced during lexing, parsing, and binding — never later, because bounded input bounds
everything downstream: value bindings keep guard operands atomic, so emitted SQL grows linearly
with typed-node count and needs no output-side limit.

| Limit | Default | Diagnostic |
|---|---|---|
| Input length (chars, per expression) | 8 192 | QX5001 |
| Token count | 4 096 | QX5002 |
| Syntax depth | 64 | QX5003 |
| Typed node count (per query) | 4 096 | QX5004 |
| List items | 128 | QX5005 |
| Joins (per query) | 32 | QX5006 |
| Parameter slots (per query) | 512 | QX5007 |

```csharp
namespace Tellma.Core.Queryex;

/// <summary>Resource ceilings for one compilation. Configurable per call site: an interactive
///     filter box and a stored report definition warrant different ceilings.</summary>
public sealed record QueryexLimits
{
    /// <summary>The default ceilings.</summary>
    public static QueryexLimits Default { get; }

    public int MaxInputLength { get; init; }
    public int MaxTokens { get; init; }
    public int MaxSyntaxDepth { get; init; }
    public int MaxTypedNodes { get; init; }
    public int MaxListItems { get; init; }
    public int MaxJoins { get; init; }
    public int MaxParameters { get; init; }
}
```

Additional obligations:

- Binding time is linear in typed-node count — the memoization requirement of §7.2. Limits are a
  backstop, not a substitute.
- The engine allocates no unbounded intermediate structures for input within limits.
- Compilation is thread-safe and free of shared mutable state; the caches are the only shared
  structures and are concurrent.

## 16. Caching

Three layers inside the engine instance, each keyed on exactly the inputs its stage depends on:

| Layer | Key | Value |
|---|---|---|
| L1 | expression text | `SyntaxTree` |
| L2 | text hash, schema version, root entity, mode, directions, `HasUser`, limits, language version | bound tree + nullity |
| L3 | the L2 keys of every clause, filter-tree shape, paging shape | SQL template + slot map |

With L3 warm, per-request work is reduced to binding parameter values into a cached template.
Rules:

- **Every cache is bounded** (§1.4 engine options) with an LRU-flavoured eviction policy. The
  engine accepts untrusted input; an attacker feeding distinct texts must age entries out, never
  grow memory without bound.
- The **limits profile participates in the key**: a text admitted under one call site's generous
  ceilings must not satisfy a stricter call site from cache.
- The **schema version** is the invalidation lever: bound trees hold descriptors, not names, so a
  host rename without a version change would keep emitting a stale column. The schema contract
  (§3) makes the version's obligations explicit.

*Guidance.* Access-control criteria and stored report filters recompile on essentially every
request with unchanging text; these layers are where the engine earns its performance, not
micro-optimisation inside the emitter.

## 17. Language versioning

```csharp
namespace Tellma.Core.Queryex;

/// <summary>Facts about the Queryex language this engine implements.</summary>
public static class QueryexLanguage
{
    /// <summary>The language version. Incremented only by a change that alters the meaning of
    ///     some currently-valid expression; additive changes (new functions, new calendar codes)
    ///     do not.</summary>
    public const int Version = 1;
}
```

Queryex text is persisted in configuration — report definitions, saved filters, permission
criteria — and recompiled long after it was written. Obligations, on hosts and on any future
version-2 work:

- Hosts persist stored expressions alongside the language version they were validated under.
- The version participates in every cache key (§16).
- A meaning-altering change requires a new version, an engine that can compile both, and a
  migration that compiles every stored expression under both versions and reports those whose
  bound tree or nullity differs, for human review.

*Rationale.* Expressions used as permission criteria are a security boundary. A semantic change
that quietly turns a restrictive criterion into a permissive one is not a compatibility
inconvenience; it is a data breach. The version stamp and the differential migration report make
that class of change impossible to ship unnoticed.

## 18. Testing

Everything except SQL execution lives in `test/core/Tellma.Core.Queryex.Tests` — offline,
hermetic, cross-platform; the differential suite's SQL-executing half lives in
`test/core/Tellma.Core.Queryex.IntegrationTests` (§18.3), with the reference interpreter shared
between them from the test tree (`test/shared/` if both need it), never shipped. There is no
performance or benchmark suite: the complexity obligations (§7.2, §15) are design requirements
enforced by review and exercised for correctness by the corpus's deep-nesting entries, not
measured in CI.

### 18.1 Conformance corpus

A data-driven suite of `(expression, mode, schema fixture, options) → expected outcome` entries,
where the outcome is a bound-tree summary (type, nullity, direction, aggregation) or an expected
diagnostic code and span. It covers: every operator, every function signature, every coercion,
every diagnostic code in Appendix A, and every row of the tables in §9.2, §12.5, §13.2, and
§13.3.

Fixture properties the corpus requires — each easy to omit and each load-bearing:

- A schema whose **logical and physical names differ** for at least one entity, one property, and
  one navigation. Golden SQL must show the physical name; no test may pass because the two
  happened to match.
- A `DateTimeOffset` column, so the zone rules of §10.3 are exercised rather than assumed; a
  `Guid` column; a nullable navigation chain in front of a non-nullable one, so the join-kind
  propagation of §12.2 is observable.
- A `varchar` column beside the `nvarchar` ones, so store-type slot typing (§13.1) is visible in
  the slot snapshots.
- A non-unique property on a hierarchical entity, so QX3302 is reachable.

`Discover` and inference get their own entries: solved, unconstrained, and conflicting
parameters; discovery over input that parses but does not bind; and cross-clause inference
through `DiscoverQuery`, including a variable-to-variable link that only resolves across clauses
(§2.4). `FilterTree` composition gets the
security-critical degenerate shapes: `Or([])` denies, `And([])` permits, and a leaf whose own text
contains a top-level `or` must not escape the conjunction above it.

### 18.2 Golden SQL snapshots

Emitted SQL and parameter slots are snapshotted per corpus entry. Emission is deterministic
(§13.5), so any change in output is a reviewable diff, not a flake.

### 18.3 Differential testing against a reference interpreter

The test tree implements a second backend: an in-memory evaluator over small materialised
datasets, written directly from §9 with no reference to the SQL emitter. For every corpus
expression, both backends evaluate the same null-rich fixture rows — the interpreter directly and
offline; the emitter by deploying the fixture as real tables on the developer/CI SQL Server and
executing the compiled SQL (the `IntegrationTests` project) — and the results must agree. This is
the primary defence for the null semantics of §9.2 and §13.3, which are otherwise easy to get
subtly wrong and hard to notice.

The offline half of the same harness asserts interpreter results against hand-written expected
values, and asserts the soundness obligation of §8.2: for every node the analysis marked
`NotNull`, the interpreter must never observe an absent value across the whole fixture.

### 18.4 Property-based tests

- **Round-trip:** `parse(print(parse(t))) = parse(t)` for generated inputs.
- **No duplication:** emitted SQL contains no non-atomic subexpression more than once (§12.3).
- **Determinism:** compiling the same input twice, on fresh engine instances, yields identical
  text and slots.
- **Robustness:** the lexer and parser survive arbitrary generated input without exceptions,
  hangs, or limit violations — user input yields diagnostics, never throws.

## 19. The inspection tool — `eng/queryex-inspection/`

A small, self-hosted web playground (checked into the solution so CI keeps it compiling;
`IsPackable` false; never shipped) that lets a human reviewer explore the compiler live,
following the folder precedent of `eng/identity-ui-inspection/`. A minimal ASP.NET Core host
serves a single static page — plain HTML and vanilla JavaScript, no build step and no Angular
workspace involvement — with input fields mirroring `QuerySpec` (select, aggregate, filter,
having, order by, paging) plus a single-expression mode, and a JSON endpoint that compiles on
every keystroke. The page renders each stage's view as the user types: tokens, syntax tree,
bound tree with types and nullities, diagnostics with carets, and the emitted SQL with its
parameter slots.

It ships with a built-in fixture schema — several entities with navigations, a hierarchy,
divergent logical/physical names, and every Queryex type — and, optionally, given a connection
string to a developer SQL Server, creates and seeds the fixture schema and executes compiled
queries so results can be eyeballed against expectations.

Two repo rules bind it: it binds an ephemeral port and prints/opens its URL, so parallel
worktrees can run it simultaneously without mutating tracked files; and transient output goes to
the gitignored `.tmp` directory. The rest — page layout, endpoint shape, seeding — is the
implementer's, with a README at the folder root and everything working on Windows and Linux.

## 20. Definition of done

- **Projects**: `src/core/Tellma.Core.Queryex` (published package),
  `test/core/Tellma.Core.Queryex.Tests`, `test/core/Tellma.Core.Queryex.IntegrationTests`, and
  `eng/queryex-inspection/` — each with a README stating purpose and usage, XML docs on every
  public member, building and testing on Windows and Linux under the repo's warnings-as-errors
  gates, wired into `Tellma.slnx`.
- **Behavior**: the grammar and precedence of §5, the type and nullity systems of §7–§8, the
  semantics tables of §9, the full §10 function library (calendar code `'gc'`), the mode rules of
  §11, lowering per §12 (join-kind propagation included), and emission per §13 (parameterization
  with store-typed slots and batch-composable naming, Bool realisation, null guards, assembly,
  tiebreakers, session-settings independence) — all
  implemented and pinned by the suites of §18: corpus, golden snapshots, differential
  interpreter, and property tests, green in CI.
- **API discipline**: the public surface is exactly §1.4, §2, §3, and the supporting records —
  every IR internal, no fragment-SQL entry point, all user-input failures as diagnostics.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the `Tellma.Core.Queryex`
  package in the layout, naming, and dependency sections (a Core-layer library that `Tellma.Core`
  will reference, unlike the composed-only optional packages); the glossary entry pointing here;
  and the reports-tier wording corrected so Queryex compiles through its own emitter while EF
  LINQ remains a separate tier-1 authoring path for pack code, not a Queryex backend. Public XML
  docs and error messages reference no `docs/` paths, per repo rule.
- **Not in scope of done**: the EF-model schema adapter, executing hosts, and every consumer
  surface (REST binding, permissions, report definitions, templates) — each lands with its own
  spec against this engine's frozen public API.

## Appendix A — Diagnostic codes

**Lexical**

| Code | Condition |
|---|---|
| QX1001 | Unterminated string literal |
| QX1002 | Unterminated bracketed identifier |
| QX1003 | Malformed number |
| QX1004 | Number exceeds the supported decimal precision |
| QX1005 | Unexpected character |

**Syntax**

| Code | Condition |
|---|---|
| QX2001 | Unexpected token; expected one of … |
| QX2002 | Unbalanced parenthesis |
| QX2003 | Empty list item |
| QX2004 | Empty argument |
| QX2005 | Empty parentheses |
| QX2006 | Chained comparison (non-associative operator) |
| QX2007 | Direction keyword not permitted here |
| QX2008 | Direction keyword must terminate the item at depth zero |
| QX2009 | A predicate is a single expression; list not permitted |

**Binding and types**

| Code | Condition |
|---|---|
| QX3001 | Unknown property on entity |
| QX3002 | Path terminates at a navigation property |
| QX3003 | Unknown function |
| QX3004 | No overload accepts this argument count |
| QX3005 | No overload matches these argument types |
| QX3006 | Ambiguous overload |
| QX3007 | Undeclared parameter |
| QX3100 | Argument must be a literal |
| QX3101 | Argument must be one of a fixed set of values |
| QX3102 | Unsupported cast |
| QX3103 | Calendar operation requires a zone-resolved value; wrap the argument in `local` |
| QX3200 | Incompatible operand types |
| QX3201 | Operand type not valid for this operator or position |
| QX3202 | Expression has no determinable type; cast the null |
| QX3300 | `descendantOf`: first argument must be a path |
| QX3301 | `descendantOf`: entity is not hierarchical |
| QX3302 | `descendantOf`: property is not unique |
| QX3303 | `descendantOf`: second argument must not contain a path |
| QX3400 | Parameter is used at conflicting types (inference, §6.4) |

**Mode and usage**

| Code | Condition |
|---|---|
| QX4001 | Expression must be a predicate in this mode |
| QX4002 | Aggregations are not permitted in this mode |
| QX4003 | Aggregation within an aggregation |
| QX4004 | Path outside an aggregation in an aggregate filter |
| QX4005 | Ordering term would alter the grouping of a grouped statement |
| QX4006 | Paging requires an explicit ordering |

**Limits** — QX5001–QX5007 per §15.

## Appendix B — Conformance vectors (extract)

Parsing:

| Input | Result |
|---|---|
| `-a * b` | `(-a) * b` |
| `not a = b` | `not (a = b)` |
| `a or b and c` | `a or (b and c)` |
| `a = b = c` | QX2006 |
| `a..b` | QX2001 |
| `f(1,)` | QX2004 |
| `Notes` | path `Notes` |
| `[not]` | path `not` |
| `count` | path `count` |
| `count()` | call `count`, zero arguments |

Typing, against a fixture with `PostingDate : Date`, `PostedAt : DateTimeOffset`,
`Memo : String`, `ExternalId : Guid`:

| Input | Type / diagnostic |
|---|---|
| `'2024-01-01' = PostingDate` | `Bool` — literal coerced to `Date` |
| `Memo = PostingDate` | QX3200 |
| `now() >= PostingDate` | QX3200 — no implicit date widening |
| `cast(Memo, 'date') = PostingDate` | `Bool` |
| `ExternalId = 'a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11'` | `Bool` — literal coerced to `Guid` |
| `IsPosted = IsApproved` | `Bool` |
| `if(c, 1, null)` | `Numeric`, `Nullable` |
| `1 \|\| 'a'` | QX3201 |
| `year(PostedAt)` | QX3103 |
| `year(local(PostedAt))` | `Numeric` |
| `addDays(PostedAt, 1)` | `DateTimeOffset` — fixed-length unit |
| `addMonths(PostedAt, 1)` | QX3103 — variable-length unit is calendar-dependent |
| `year(PostingDate)` | `Numeric` — a `Date` is already zone-resolved |

Parameter inference, no declarations supplied:

| Input | Result |
|---|---|
| `PostingDate >= @From` | `@From : Date` |
| `contains(Memo, @Q)` | `@Q : String` |
| `@a = @b` | both unconstrained |
| `PostingDate >= @X and Amount > @X` | QX3400, with both spans |

Nullity, with `W` non-nullable and grouping present:

| Input | Nullity |
|---|---|
| `sum(W)` | `NotNull` |
| `sum(W, Gender = 'F')` | `Nullable` |
| `count(W, Gender = 'F')` | `NotNull` |
| `sum(W)` with no grouping key | `Nullable` |
| `coalesce(NullableCol, 0)` | `NotNull` |

Null semantics, `A` absent and `B = 5`:

| Input | Result |
|---|---|
| `A = B` | false |
| `A != B` | true |
| `A < B` | false |
| `A is null` | true |
| `A in (1, null)` | true |
| `not (A = B)` | true |
| `contains(A, 'x')` | false |
| `A \|\| 'x'` | absent |

## Appendix C — Reserved words

```
and   or   not   in   is   null   true   false   asc   desc
```

Any other identifier, including every function name, may be used as a property name. Bracketed
identifiers (`[and]`) escape this list and any word reserved in a future version.

## Decisions record

The load-bearing decisions, where not already evident above:

1. **A dependency-free `Tellma.Core.Queryex` package under `src/core/`** — the engine is a
   compiler library like a raw connector client: no Tellma references, no third-party references,
   consumable by `Tellma.Core`, pack code, tests, and the inspection tool alike (§1.1).
2. **The engine compiles; the host executes** — no connection, no evaluation context interface.
   Context values (`today()`, `now()`, `me()`, the tenant zone) are parameter slots with declared
   origins that the host binds at execution; the one compile-time context fact is `HasUser`,
   which participates in cache keys (§10.6, §13.1, §16).
3. **`CompileQuery` is the only door to SQL** — `Validate` returns types and diagnostics without
   SQL, because independently compiled fragments cannot share joins or parameters and a public
   fragment API would invite the concatenation `FilterTree` exists to prevent (§1.4, §2.2).
4. **`FilterTree` identities are fixed at the API**: `And([])` is true, `Or([])` is false, empty
   leaves are caller errors — an empty permission set must deny, and a fold with the wrong
   identity is a privilege escalation (§2.3).
5. **One `Bool` type; realisation is positional** — predicate vs value is decided during
   emission and is invisible in the language, so users never meet the backend's boolean/bit split
   (§7.1, §13.2).
6. **Comparison is total and two-valued** — absence compares equal to absence, ordering against
   absence is false, `!=` is the exact complement of `=`; negated-ordering rewrites are banned
   because they are unsound for nullable operands (§9.2).
7. **Schema misconfiguration throws at build; user mistakes are diagnostics** — the schema is
   host-authored, so its validation lives in `QueryexSchemaBuilder.Build()`, keeping the
   diagnostic space purely about user input (§3).
8. **No collections in the schema contract** — the platform's entity model has no parent-to-child
   navigations, which also makes aggregation over many-to-one join trees sound with no
   cardinality analysis (§3).
9. **Join kind propagates** — `INNER` only where the foreign key is non-null *and* the parent
   join is `INNER`; anything behind a `LEFT` join stays `LEFT`, or absent optional ancestors
   would silently drop rows (§12.2).
10. **No subexpression is emitted twice unless atomic** — null guards reference operands through
    `CROSS APPLY (VALUES …)` bindings; the sole exception is identical aggregate expressions,
    where a lateral binding is inexpressible and the backend deduplicates (§12.3).
11. **`Guid` is Equatable but not Ordered; `Geography` is neither** — uniqueidentifier ordering
    is a backend accident, and spatial equality does not exist in the backend; both surface as
    bind-time diagnostics instead of backend errors (§7.1).
12. **`CAST`, never `TRY_CAST`** — a silent NULL on conversion failure would falsify the nullity
    analysis the emitter's guards rely on (§10.8).
13. **String predicates match literally via metacharacter-free emission** — `CHARINDEX`-style
    templates rather than `LIKE` with escaping, so computed patterns need no in-SQL rewriting
    (§10.9).
14. **Calendar codes are compile-time selectors; v1 implements `'gc'`** — `'uq'` and `'et'` are
    reserved with their emission strategies recorded (inline arithmetic; host-provided month
    map), landing with the Locale packs as additive registry changes (§10.4).
15. **`local` gates calendar operations on `DateTimeOffset`** — the zone question becomes a
    compile error with a named fix instead of a row-by-row wrong answer; IANA ids in the
    language, resolved to backend zone names at compile time (§10.3).
16. **Grouping is derived, never declared** — paths outside aggregations are the keys, so a
    select list and its `GROUP BY` cannot disagree; ordering terms may not widen the grouping,
    and `DISTINCT` falls out as an aggregate query with no aggregations (§11.3, §13.4).
17. **Paging appends deterministic tiebreakers and fixes null placement** — the root key or the
    remaining grouping keys complete the total order; `asc` nulls-first / `desc` nulls-last are
    normative and happen to be the backend's defaults, so they cost nothing (§13.4).
18. **Limits are pre-emission only, and caches are bounded with limits in the key** — bounded
    input bounds output by construction; unbounded text-keyed caches are a memory-exhaustion
    vector, and a permissive call site's cache entry must not leak past a stricter one (§15,
    §16).
19. **Emitted SQL is session-settings-independent** — no `DATEFIRST`-, `LANGUAGE`-, or
    `DATEFORMAT`-sensitive constructs; dates and literals travel as typed parameters (§13.6).
20. **Multi-valued parameters are deferred, safely** — parameter types come from caller
    declarations, so introducing list declarations later cannot change the meaning of any stored
    expression (Non-goals).
21. **A differential in-memory interpreter, no performance suite** — the interpreter is the
    defence for null semantics and nullity soundness; complexity obligations are design
    requirements exercised by deep-nesting corpus entries, not benchmarked in CI (§18).
22. **Engine names live in a reserved `@qx{ordinal}_` namespace** — `BatchOrdinal` namespaces
    every emitted parameter and variable per compiled query, so the CRUD stack can concatenate
    compiled queries and its own raw SQL into one round trip (`NextResult()` batch fetching)
    without collisions; raw SQL stays out of the `@qx` prefix, and determinism is untouched
    because the ordinal is part of the compilation's identity (§13.1).
23. **Cross-clause inference is engine-owned** — `DiscoverQuery` runs one inference context over
    all of a query's clauses, because per-clause results discard variable-to-variable links
    (`@a = @b` in the filter, `@a` dated in the ordering) that no caller-side merge can recover
    (§2.4).
24. **Parameter slots bind as the compared column's store type** — schema properties carry a
    structured store-type facet consulted only for slot typing, because a mistyped parameter
    (`nvarchar` against a `varchar` column) forces the column-side conversion and kills the
    index seek; the facet is a closed enum plus sizes, so no host type text reaches SQL through
    a new door (§3, §13.1).
25. **The inspection tool is a live web playground, not a console** — a compiler is explored by
    watching every stage react as the input changes; a minimal Kestrel host with one static page
    delivers that cross-platform with no build step, on an ephemeral port so parallel worktrees
    coexist (§19).
