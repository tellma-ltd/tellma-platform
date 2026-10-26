# f08 — Queryex (spec 0008, engine code, 0012 §11 host integration)

## 1. Sources read

- `docs/specs/0008-queryex.md`: 2856 lines, read in full (frozen, implemented).
- `src/core/Tellma.Core.Queryex/`: 73 `.cs` files, about 18,900 lines. Read: every public type's
  members, `QueryexEngine.cs`, `CompiledQuery.cs`, `Emit/StatementEmitter.cs` (264),
  `Emit/PlanEmitter.cs` (set-membership and hierarchy parts), `Pipeline/QueryClauses.cs`
  (key derivation, select/order lowering), `QueryexSchemaBuilder.cs` (builder signatures),
  `Diagnostics/DiagnosticCodes.cs` (codes, argument names), `Functions/FunctionLibrary.cs`
  (names), `README.md`. Grepped the emitter for advanced SQL constructs and `src/**/*.ts` for
  Queryex.
- `docs/specs/0012-data-access-layer.md` §11 (lines 1651–1842), plus §2.5–§2.6 for the related
  projection and multilingual twins.
- For orientation only: 0015 §4.3, 0016 §2.1, §3.3, §3.5, §11 (the wire touch points).
- Memory notes `queryex-implementation.md` (61 lines) and `queryex-spec-0008.md` (67 lines).

Section numbers like "§10.4" refer to 0008 unless another spec is named.

## 2. Key facts

### 2.1 Public API (0008 §1.4)

- `QueryexEngine` (thread-safe, one per host): `Validate(text, ValidationOptions)`,
  `Discover(text, DiscoveryOptions)`, `DiscoverQuery(QueryDiscoverySpec, DiscoveryOptions)`,
  `CompileQuery(QuerySpec, QueryCompilationOptions)`. `CompileQuery` is the only door to SQL; no
  fragment-SQL API exists by design (§1.4 last para).
- `QueryexResult<T>`: `Succeeded`, `Value`, `Diagnostics` (all independent problems reported).
- `QueryexDiagnostic(Code, Span, Location, Arguments)`; `QueryexSpan(Start, Length)` half-open
  character range in one expression's text. Engine composes **no prose**; hosts localize from
  `Code` + `Arguments` (named pairs "name", "entity", "type", ...). `Location` e.g. `"Select[2]"`,
  `"OrderBy[0]"`, `"Filter.Or[1]"`, or null for single-expression validation.
- Every diagnostic is an error (no severity axis). Caller errors throw `ArgumentException` family.
- `QueryexEngineOptions`: `MaxCachedSyntaxTrees` 4096, `MaxCachedBoundExpressions` 4096,
  `MaxCachedQueryTemplates` 1024.
- `ValidationOptions`: `Schema`, `Root`, `Mode` (required), `LanguageVersion` (required),
  `Directions` (bool), `Parameters` (`QueryexParameterDeclaration` list, default empty),
  `HasUser` (default true), `HasGroupingKeys` (default false), `Limits` (default
  `QueryexLimits.Default`).
- `DiscoveryOptions`: all optional — `Schema?`, `Root?`, `Mode?`, `LanguageVersion` (default
  current), `Limits`.
- `QueryexParameterDeclaration(string Name, QueryexType Type, bool IsNotNull)`.
- `ValidatedExpression(Items)`; `ValidatedItem(Span, Type, Nullity, Direction, UsesAggregation)`;
  `QueryexDirection { None, Ascending, Descending }`.

### 2.2 `QuerySpec`, `CompileQuery`, `CompiledQuery` (0008 §2.1–§2.2)

- `QuerySpec` (one query over one root): `Root` (required `EntityDescriptor`), `Select` (required
  string, comma list), `Aggregate` (bool), `Filter` (`FilterTree?`, row-level always, even in an
  aggregate query), `Having` (`FilterTree?`, caller error unless `Aggregate`), `OrderBy`
  (string?, directions enabled), `Skip` (int?), `Take` (int?). Paging requires `OrderBy`. Negative
  Skip/Take or `Take = 0` is a caller error. Skip/Take values are emitted as parameters.
- `QueryCompilationOptions`: `Schema` (required), `LanguageVersion` (required), `Parameters`
  (shared by every clause), `HasUser` (default true), `BatchOrdinal` (default 0; namespaces
  emitted names for multi-query batches), `Limits`.
- `CompiledQuery`: `Sql` (T-SQL batch, never interpolated further), `Parameters`
  (`QueryexParameterSlot` list, declaration order), `Columns` (`QueryexColumn` list positional to
  select list).
- `QueryexColumn(Ordinal, Text, Type, Nullity, Path, IsGroupingKey)`: `Text` = the select item's
  source text; `Path` = resolved logical segments when the item is a bare path, null for computed
  expressions; `IsGroupingKey` true for derived keys in aggregate queries.
- **No column label/alias concept.** Columns are positional; the only name is the item's source
  text. (No `as` keyword exists; see 2.6.)
- Count query: `Select = "count()"`, `Aggregate = true`, no keys ⇒ grand total. DISTINCT: aggregate
  query with an aggregation-free select list returns distinct combinations.
- Degenerate shape `Select = "1"` with `Aggregate = true` (no aggregation, no key) emits one row
  per underlying row; left undiagnosed (§2.1).
- **One emit context** (§2.2): clauses share one join trie, alias sequence, parameter table, and
  value-binding set. Fixed bind order `Filter`, `Select`, `Having`, `OrderBy`; filter-tree leaves
  pre-order.
- Grouping-key existence is decided **syntactically before binding** from the select list parse:
  a key is an item with no aggregation that mentions at least one path (§2.2).

### 2.3 `FilterTree` (0008 §2.3)

- Abstract class; factories `Leaf(string text)` (empty/whitespace = caller error),
  `And(children)` (empty = true), `Or(children)` (empty = **false**), `Not(operand)`;
  `MaxDepth = 64` (caller error at the factory crossing it). Concrete node types are public sealed
  nested types (pattern-matchable, serializable).
- Every leaf binds in the clause's mode (Filter → `Filter`, Having → `AggregateFilter`).
  Diagnostics carry a tree location like `Filter.Or[1]`. Each leaf caches on its own text.
- Motivating use: `And([Leaf(userFilter), Or(criteria.Select(Leaf))])` for RLS. Textual
  concatenation is explicitly wrong (`A or B and C`).
- Implication for round 2: a search page's column filters, search box, advanced free-text filter
  and "active only" toggle should become separate `FilterTree` leaves on the server, never one
  concatenated string. Where the tree is formed from the REST query string is 0012/0016 business
  (see 2.17).

### 2.4 Discovery and parameter inference (0008 §2.4, §6.4)

- `Discover` succeeds on any input that lexes and parses (paths/types only with a schema).
- `DiscoveryResult(Parameters: ParameterUse[], Paths: PathUse[], Functions: string[],
  UsesAggregation: bool, Diagnostics)`.
- `ParameterUse(Name /*no @*/, Occurrences: QueryexTextSite[], InferredType: QueryexType?,
  Conflicts: TypeConflict[])`; `TypeConflict(Type, Site)`; `PathUse(Segments, Site)`;
  `QueryexTextSite(Location, Span)`.
- `DiscoverQuery(QueryDiscoverySpec, DiscoveryOptions)`: `QueryDiscoverySpec` is a lax mirror of
  `QuerySpec` with all-optional `Select`, `Aggregate`, `Filter`, `Having` (caller error unless
  Aggregate), `OrderBy`. **No `Root` on the spec**: root comes from `DiscoveryOptions.Root`.
  One shared inference context over all clauses (needed for `@a = @b` links).
- Inference outcomes: Solved (`InferredType` set), Unconstrained (null, not an error, e.g.
  `@a = @b`), Conflicting (QX3400; `Conflicts` lists each demanded type + site). Inferred
  parameters default to Nullable; inference never guesses nullity.
- Examples: `PostingDate >= @From` → Date; `Amount * @Rate` → Numeric;
  `contains(Memo, @Search)` → String. Outcome is order-independent
  (`coalesce(@p, '2024-01-01') = PostingDate` → Date).
- Designer flow (normative, §2.4): DiscoverQuery proposes declarations → author confirms or
  overrides → definition stored with declarations → `Validate` checks each clause;
  `Validate`/`CompileQuery` require every parameter declared (QX3007).
- Discovery is exactly the report-studio "auto-populate parameters as the user types" capability
  the brief asks for, but it is a server-side .NET API only (see 2.16 on the client side).

### 2.5 Schema contract (0008 §3)

- `QueryexSchema` (immutable, identified by reference, rebuilt on model change):
  `Entities`, `FindEntity(name)` (case-insensitive).
- `EntityDescriptor`: `Name` (logical), `Source` (bracket-quoted physical, e.g. `[gl].[Invoices]`),
  `Key` (single-column surrogate key), `Properties` (scalars, Key included), `Navigations`
  (many-to-one only), `TreeNode` (`PropertyDescriptor?` of type `QxHierarchyId`; enables
  `descendantOf`/`ancestorOf`).
- `PropertyDescriptor`: `Name`, `Column`, `Type`, `IsNotNull`, `IsUnique`, `StoreType?`.
- `NavigationDescriptor`: `Name`, `Target`, `ForeignKey` (PropertyDescriptor on declaring entity).
- `QueryexStoreType(Family, Size?, Scale?)`; `QueryexStoreFamily { QxBit, QxTinyInt, QxSmallInt,
  QxInt, QxBigInt, QxDecimal, QxChar, QxVarChar, QxNChar, QxNVarChar, QxUniqueIdentifier, QxDate,
  QxDateTime, QxDateTime2, QxDateTimeOffset, QxHierarchyId, QxGeography }`.
- Built via `QueryexSchemaBuilder` (`Entity(name, source)`, `.Key(...)`, `.Property(name, type,
  column, isNotNull, isUnique)`, `.TreeNode(name)`, `.Navigation(name, target, foreignKey)`,
  `Build()`); misconfiguration throws at `Build()`.
- Build rules: names unique case-insensitively; key Equatable and implicitly unique; FK type
  unifies with the target key; TreeNode is HierarchyId; StoreType belongs to the Queryex type;
  Source is bracket-quoted parts; Column unique per entity; no control chars or U+2028/2029.
- **Referential integrity assumed**: non-null FK ⇒ INNER join; unused joins pruned.
- **Deliberately absent** (§3): **collections** (no parent-to-child navigation, hence no fan-out
  and no double counting under aggregation), **computed properties** (expose via a view named by
  `Source`), unmappable columns (`time`, `varbinary`, `json`, `xml`, `float`, `real`). Enums are
  declared as their store type (`Numeric` or `String`): **no enum type, no enum value list, no
  labels in the schema.**
- **No display metadata in the schema at all**: no labels, no multilingual names, no "name
  property" designation, no display-name concept, no default select, no descriptions, no
  formatting hints. `PathUse` and `QueryexColumn.Path` give logical segments only.

### 2.6 Lexical structure (0008 §4)

- No comments. Whitespace = Unicode `White_Space`. Maximal munch. Token kinds: `Number`,
  `String`, `Identifier`, `Keyword`, `Parameter`, `Punctuator`, `EndOfInput`.
- Identifier: `(UnicodeLetter|_){UnicodeLetter|UnicodeDigit|_}` or `[ ... ]` bracketed
  (brackets escape keywords: `[not]`, `[Order]`). Unterminated bracket QX1002; empty `[]` QX1005.
- Reserved keywords (case-insensitive): `and or not in is null true false asc desc`. **No `as`,
  `between`, `like`, `case`, `group`, `by`, `over`, `distinct`, `exists`, `select`.**
  Function names are not reserved (a property may be named `Count`).
- Numbers: `digit{digit}[.digit{digit}]`; exact decimals; scale retained; >38 significant digits
  QX1004; `.5`, `5.`, `1e3`, `0x0A`, `1_000` are QX1003. No negative literal (unary minus).
- Strings: `'...'`, `''` escapes a quote; no other escapes; unterminated QX1001 (span to end).
- Parameters: `@Identifier`, one token, matched case-insensitively; undeclared QX3007; bare `@`
  QX1005.
- Punctuators: `!= <= >= || = < > + - * / % ( ) , .`; anything else QX1005.

### 2.7 Grammar (0008 §5)

- `Input ::= Item {"," Item}`; `Item ::= Expression ["asc"|"desc"]`. Empty items QX2003.
  Direction suffix only when enabled (QX2007), must be the final token at depth zero (QX2008).
  Predicate positions take exactly one item (comma at depth zero QX2009).
- Precedence (Pratt): 90 call `(` / path `.`; 80 prefix `-`; 70 `* / %`; 60 `+ - ||`;
  50 `= != < <= > >= in is [not] null` (non-associative; `a = b = c` QX2006); 40 `not`; 30 `and`;
  20 `or`.
- `Comparison ::= Additive [CompOp Additive | "in" "(" Additive {"," Additive} ")" | "is" ["not"] "null"]`.
  No `not in` form (write `not (x in (...))`). No `between`. No `like`.
- `Primary ::= Number | String | null | true | false | Parameter | Call | Path | "(" Expression ")"`.
- Call iff identifier followed by `(`; zero-arg calls need `()` (`today()`, `me()`); `()` alone
  QX2005; empty args QX2004.
- Path `Identifier {"." Identifier}`; each segment has its own span.
- Recovery (§5.4): first syntax error per item reported with span and expected-token set, then one
  recovery to the next depth-zero `,`, so several items can each report.
- **No `select`, `group by`, `having`, `order by`, `top`, `expand` keywords**: each clause is a
  separate text field of `QuerySpec`; grouping is implicit (§11.3).

### 2.8 Paths and parameters (0008 §6)

- Path: all segments but the last resolve to navigations; the last to a scalar. Case-insensitive.
  Unknown segment QX3001 (segment span); path ending at a navigation **QX3002** (final-segment
  span). So `Center` alone is not a legal select item; only `Center.Id`, `Center.Name`, ... or
  `CenterId`.
- Path type = final property type; NotNull iff every FK step IsNotNull and the final property
  IsNotNull (§6.2).
- Declared parameters: type and nullity from the declaration; scalar only (declaring HierarchyId
  or Geography is a caller error). **Declaring a name no expression references is a caller error
  (§6.3)**: a host must not pass unused declarations (matters for dashboards passing a shared
  parameter set to every report).

### 2.9 Type system (0008 §7)

- `QueryexType { QxBool, QxNumeric, QxString, QxGuid, QxDate, QxDateTime, QxDateTimeOffset,
  QxHierarchyId, QxGeography }`. Internal `Null` type never public; bare `select=null` QX3202.
- Equatable: all but Geography. Ordered: all but Geography and Guid.
- HierarchyId/Geography: no literal or parameter form; columns only.
- Bidirectional Synth/Check with mandatory memoization; checkable nodes: `null`, string literal
  (to date types / Guid), `if`, `coalesce`, `min`/`max`, parens.
- Implicit coercions only: `null` → any but Bool; string **literal** → Date/DateTime/
  DateTimeOffset (ISO 8601, culture-invariant) or Guid (hyphenated 8-4-4-4-12). Nothing else:
  no numeric/string, none among date types. Literal formats: `YYYY-MM-DD`,
  `YYYY-MM-DD[T ]HH:MM[:SS[.fff]]`, `<DateTime>(Z|±HH:MM)` (offset ≤ 14:00).
- Unification left-fold (QX3200 on failure). Overload resolution: name (QX3003) → arity (QX3004)
  → Check args → score (2 exact, 1 coerced) → unique max wins; none QX3005 (lists candidates),
  tie QX3006.

### 2.10 Nullity (0008 §8)

- `QueryexNullity { NotNull, Nullable, Null }`. Separate annotation pass. Soundness obligation:
  NotNull and Null are claims; Nullable is always safe.
- Rules: literals NotNull; `null` Null; path per §6.2; parameter per declaration; `today()`,
  `now()` NotNull; `me()` NotNull iff `HasUser`, else Null; arithmetic/`||` Union; every Bool
  result NotNull; `if`/`coalesce` per table; `count` NotNull; `sum avg min max` NotNull iff the
  arg is NotNull **and** no condition arg **and** the query has ≥1 grouping key.
- Folding: a Null value emits as a typed absent; constant predicates fold to `1 = 1` / `1 = 0`.
  Suppressed for grouping keys.

### 2.11 Expression semantics (0008 §9)

- Arithmetic `+ - * / %` Numeric only; `||` String only (`+` never concatenates). `/` and `avg`
  guarantee ≥ 6 fractional digits (widening only when both operands are integer-family, or avg of
  an integer). Division by zero is an execution-time error. `||` propagates absence (use
  `coalesce(s, '')`) and never truncates.
- **Comparison is total and two-valued** (§9.2): one side absent ⇒ `=` false, `!=` true,
  ordering false; both absent ⇒ `=` true, `!=` false, ordering false. `x = null` is an absence
  test. `not (a < b)` is not `a >= b`. Range filter `x >= @from and x <= @to` excludes absent x.
  No pass may flip negated ordering operators.
- String comparison follows column collation; trailing spaces insignificant for `=`/ordering;
  `contains`/`startsWith`/`endsWith` match trailing spaces; `length` excludes them.
- `in`: `value in (e1..en)`, n ≥ 1, total equality (absent matches absent). Emitted as one `IN`.
- `and or not` two-valued; no short-circuit guarantee; evaluation order and count unspecified.

### 2.12 Function library (0008 §10)

Registry (§10.1, internal): `FunctionDefinition(Name, Category: Scalar|Aggregate, Signatures)`;
`FunctionSignature(Parameters, Returns, Nullity: AlwaysNotNull|Union(idx)|Propagate(idx), Emit)`;
`FunctionParameter(Type, Optional, Rest, Constraint)`; constraints `None | LiteralOnly |
MemberOf(set) | TreeNodeKeyPath | PathFree`, each with a `Consumed` flag. Adding a function =
registry entry + emit template only. **The registry is internal: there is no public function
catalog API** (only `DiscoveryResult.Functions`, which lists the names used in one text).

Aggregations (§10.2):
- `count()`, `count(x)`, `count(x, c)`, `sum(x: Numeric [, c])`, `avg(x: Numeric [, c])`,
  `min(x: Ordered [, c])`, `max(x: Ordered [, c])`. Optional final `c: Bool` is a per-row filter
  (`CASE WHEN c THEN x END`). No nesting (QX4003). Only on the Group axis (QX4002).
- Emission: `COUNT_BIG`; `sum` widens integer args; `min`/`max` over Bool via tinyint.
- **Absent: `countDistinct`/`count(distinct)`, `stdev`, `var`, `median`, `string_agg`, `first`/
  `last`, any window or analytic function (`over`, running sum, rank, lag), `grouping()`.**

Date/time (§10.3–§10.5):
- Calendar ops reject `DateTimeOffset` (QX3103, which names `local` as the fix). `local(d:
  DateTimeOffset [, tz: String])` → DateTime; `tz` LiteralOnly IANA id via an embedded CLDR map
  (unknown QX3101); omitted ⇒ tenant zone bound via a `TimeZone` parameter slot.
- Aliases: `Instant` = type variable over Date/DateTime/DateTimeOffset (shared binding);
  `Cal` = OneOf(Date, DateTime); `TimeOfDay` = DateTime; `SubDay` = variable over DateTime and
  DateTimeOffset.
- Calendar: `year`, `quarter`, `month`, `day` (each `(d: Cal [, cal])`), `weekday(d)` (1=Mon ..
  7=Sun), `week(d [, cal])` ('gc' = ISO 8601 week), `hour`, `minute`, `second` (TimeOfDay);
  `startOfYear(d [, cal])`, `startOfMonth(d [, cal])`, `startOfWeek(d)` (Monday),
  `startOfDay(d)` → Date; `addYears(d, n [, cal])`, `addMonths(d, n [, cal])` (clamps) → Cal;
  `diffYears(d1, d2 [, cal])`, `diffMonths(d1, d2 [, cal])` count **completed** units, d2 − d1.
  **No `startOfQuarter`**, no `dateFromParts`/`makeDate`, no `endOfMonth`.
- `cal` is `LiteralOnly`, `MemberOf(CalendarCodes)`; **only `'gc'` is implemented**; `'uq'` and
  `'et'` are reserved and produce **QX3101** today. Recorded strategies: `'et'` inline arithmetic;
  `'uq'` a host-provided Hijri month-map table via the schema contract, with Nullable results.
- Week-keyed reports should group by `startOfWeek`, not by (`year`, `week`).
- Instant: `addDays(d: Instant, n)`, `addHours/addMinutes/addSeconds(d: SubDay, n)`;
  `diffDays/diffHours/diffMinutes` fractional, `diffSeconds` whole; `DATEDIFF_BIG`; mixed date
  types in a diff QX3200; `addHours(PostingDate, ...)` on a Date is QX3005.

Context (§10.6): `today()` → Date (tenant zone), `now()` → DateTimeOffset, `me()` → Numeric
(current user id); each is one parameter slot per statement.

Conditional (§10.7): `if(c, a, b)` (exactly 3 args), `coalesce(x1, ..., xn)` n ≥ 2.

Conversion (§10.8): `cast(x, type)` with type `LiteralOnly` in `'numeric' 'string' 'bool' 'guid'
'date' 'datetime' 'datetimeoffset'`. Matrix: Numeric→String/Bool; String→all; Bool→Numeric/
String; Guid→String; Date→String/DateTime; DateTime→String/Date; DateTimeOffset→String/Date/
DateTime; ✗ is QX3102. String→numeric targets `decimal(38, 6)`. Never `TRY_CAST`. String
rendering is culture-invariant (ISO dates, `true`/`false`, lowercase GUIDs).

Numeric/string (§10.9): `abs`, `round(x, digits /*LiteralOnly*/)` (half away from zero), `floor`,
`ceiling`, `length`, `left`, `right`, `substring(s, start [, len])` (1-based), `trim`, `upper`,
`lower`, `replace`, `contains`, `startsWith`, `endsWith` (total; literal match via CHARINDEX-style
emission, never LIKE, so `startsWith` cannot seek an index). **No `format`, no `concat`
function (use `||`), no `nullif`, no `isnull`, no `power`/`sqrt`, no regex.**

Hierarchy (§10.10):
- `descendantOf(key: <path>, k1, ..., kn)` and `ancestorOf(key: <path>, k1, ..., kn)` → Bool,
  n ≥ 1, **reflexive**, any-of semantics. Constraints: `key` a bare path (QX3300) through an
  entity with a `TreeNode` (QX3301); the property `IsUnique` (QX3302); each `ki` path-free
  (QX3303) and unifying with key's type.
- Examples: `descendantOf(Account.Concept, 'Assets')`; `ancestorOf(Id, @k0, @k1, @k2)` is the
  tree-loading shape (query a page, then query the ancestors of the page's rows, union
  client-side).
- Unmatched keys contribute nothing; all unmatched ⇒ false. One hoisted node-lookup variable per
  key; n bounded by `MaxParameters`. `descendantOf` sargable (n seeks); `ancestorOf` residual.
- **Absent: `childOf`/`parentOf`, `level`/`depth`, `isLeaf`, `hasChildren`, a "node's ancestor at
  level k" function, any tree-rollup grouping function.** Depth/level and parent are reachable
  only as ordinary properties and navigations a host declares (e.g. a `Parent` navigation).

### 2.13 Modes (0008 §11)

- `QueryexShape { Predicate, Value }`, `QueryexGrouping { Row, Group }`,
  `QueryexMode(Shape, Grouping)` with named statics `Filter`, `Value`, `Aggregate`,
  `AggregateFilter`.
- Predicate: type Bool (QX4001), one item (QX2009). Value: any type; Bool realised as a value.
- Row: aggregations forbidden (QX4002). Group: each item is either a **grouping key** (no
  aggregation; the whole item expression goes into GROUP BY; must be Equatable, QX3201) or a
  **measure** (every path enclosed in an aggregation); mixed `Price * sum(Qty)` is QX4007. An item
  with no path is row-invariant and is neither a key nor a GROUP BY member.
- **No `group by` in the language**: keys are derived from the select list, one per distinct
  bound tree; structural equality (descriptors, functions, literals) is used for key matching
  (QX4005, tiebreakers).
- AggregateFilter: every path inside an aggregation (QX4004).
- Directions: Value shape only; `OrderBy` enables them in `CompileQuery`; an ordering item must
  be Ordered (QX3201). Directions on a predicate shape = caller error.

### 2.14 Lowering and SQL shapes (0008 §12–§13)

- IRs (§12.1, all internal): `SyntaxTree` (printable; `parse(print(parse(t))) = parse(t)`),
  `TypedExpr` (types, nullity, descriptors; no SQL), `RelationalPlan` (positions, guards, join
  trie, value bindings, hoists, parameter table).
- Join planning (§12.2): one trie of navigation-path prefixes across **all** clauses; aliases
  pre-order `[P1]`, `[P2]`, ...; root `[T]`; `INNER` iff FK `IsNotNull` and parent join `INNER`,
  else `LEFT`. Pruned by reachability (joins, bindings, variables, slots). `MaxJoins` counts
  survivors (QX5006).
- Value bindings (§12.3): non-atomic operands of guarded constructs hoisted into
  `CROSS APPLY (VALUES (...)) AS [B1](v)`; group-grain positions use single-emission forms
  `EXISTS (SELECT l INTERSECT SELECT r)` / `CASE ... = 1`. No non-atomic subexpression emitted
  twice.
- Invariant hoisting (§12.4): only hierarchy node lookups, as `DECLARE @qx0_v0 ... = (SELECT ...)`
  in a prologue.
- Position (§12.5): `filter = IsPosted` → `[T].[IsPosted] = 1`; `select = Amount > 100` →
  `CASE WHEN [T].[Amount] > @qx0_p0 THEN 1 ELSE 0 END`.
- Parameterization (§13.1): every literal, context value and declared parameter is a bound
  parameter. `QueryexParameterOrigin { Literal, Today, Now, UserId, TimeZone, Declared }`.
  `QueryexParameterSlot(Name, Type, StoreType, Origin, Value?, DeclaredName?)`. Names
  `@qx{b}_p{n}`, variables `@qx{b}_v{n}`, `{b}` = `BatchOrdinal`. Identical literals sharing a
  store type share a slot; one declared parameter compared against columns of different store
  types yields **several slots with the same `DeclaredName`** (host binds all from one value).
  Skip/Take are two further slots.
- Slot store types: adopt only the compared column's **family** (never size/scale); adoption only
  for ASCII literal-origin strings; declared/context slots never adopt; default mapping Bool→
  `bit`, Numeric→`decimal` (value's own p/s, or none), String→`nvarchar(4000)`/`nvarchar(max)`/
  unsized, Guid→`uniqueidentifier`, Date→`date`, DateTime→`datetime2(7)`, DateTimeOffset→
  `datetimeoffset(7)`.
- Bool realisation (§13.2) and null-guard tables (§13.3) for comparison and `in` (elements
  partitioned: NotNull ones stay in one `IN`).
- **Statement assembly (§13.4), the only shape emitted:**

  ```
  DECLARE @qx0_v0 …
  SELECT   <select items, source order>
  FROM     <root Source> AS [T] <joins> CROSS APPLY (VALUES (…)) AS [B1](v)
  WHERE    <filter>
  GROUP BY <grouping keys>        -- iff Aggregate and ≥1 key
  HAVING   <aggregate filter>
  ORDER BY <items, tiebreakers>
  OFFSET <skip> ROWS FETCH NEXT <take> ROWS ONLY   -- iff paging
  ```

  **Exactly one SELECT per compiled query; plain `GROUP BY` only.** No `GROUPING SETS`, `ROLLUP`,
  `CUBE`, `GROUPING()`/`GROUPING_ID()`, `OVER (...)` window clauses, CTEs, `UNION`, subqueries
  (except the internal `EXISTS ... INTERSECT` guard form and hoisted lookups), `TOP`, or
  `DISTINCT` keyword. (Confirmed in code, see 2.15.)
- Assembly rules: grand total (aggregate, no keys) emits no GROUP BY; grouping keys lowered
  without folding; ordering terms that read nothing are dropped; Group-axis ordering terms must
  be a measure or structurally equal to a key (QX4005); duplicate ordering terms QX4008; paging
  requires an ordering that survives lowering (QX4006); paging appends tiebreakers (root key, or
  every grouping key in select order, ascending, skipping already-ordered ones); null placement
  normative: `asc` nulls first, `desc` nulls last.
- **Result columns carry the Queryex type, not a SQL type** (§13.4 end): `year(x)` is int,
  `count()` bigint, `sum` decimal; hosts read tolerantly within the Queryex type.
- Determinism (§13.5): byte-identical SQL for same spec/schema/options/engine version.
- Session-settings independence (§13.6): no `DATEFIRST`/`LANGUAGE`/`DATEFORMAT` sensitivity; host
  must keep `CONCAT_NULL_YIELDS_NULL`, `ANSI_WARNINGS`, `ARITHABORT`, `NUMERIC_ROUNDABORT` at
  defaults.

### 2.15 Diagnostics, limits, caching, versioning (0008 §14–§17, App. A)

Diagnostics (§14):
- Machine-readable `Code` + `Span` + `Location` + `Arguments`; **no prose from the engine**.
  The code catalogue is internal; **Appendix A is the contract a host message catalogue is
  written against**. All independent diagnostics reported (per item, leaf, argument). Dedup only
  when code, span, location, and arguments all agree. No backend error text ever reaches callers.
- Spans are relative to **one expression's text** (one select list string, one filter-tree leaf),
  identified by `Location`. A UI that shows the free-text filter in a text box can underline
  `Span` directly **only if** the box's text is passed unchanged as one leaf, and only when the
  server returns the diagnostics verbatim (see gaps).
- Appendix A codes: lexical QX1001–QX1005; syntax QX2001 (unexpected token; expected one of ...),
  QX2002 (unbalanced paren), QX2003–QX2009; binding QX3001–QX3007, QX3100–QX3103, QX3200–QX3202,
  QX3300–QX3303, QX3400; mode QX4001–QX4008; limits QX5001–QX5007.

Limits (§15), `QueryexLimits` record (`Default`, all `init`):

| Member | Default | Code |
|---|---|---|
| `MaxInputLength` (chars per expression) | 8192 | QX5001 |
| `MaxTokens` | 4096 | QX5002 |
| `MaxSyntaxDepth` | 64 | QX5003 |
| `MaxTypedNodes` (per query) | 4096 | QX5004 |
| `MaxListItems` (per list; also `in` elements and variadic args) | 128 | QX5005 |
| `MaxJoins` (after pruning) | 32 | QX5006 |
| `MaxParameters` (slots after pruning) | 512 | QX5007 |

- Configurable per call site ("an interactive filter box and a stored report definition warrant
  different ceilings"). Limits apply to `Discover`/`DiscoverQuery` too. `FilterTree.MaxDepth` 64
  is separate. Practical consequence: a select list is capped at 128 items, so a pivot with many
  measures × (Id + 3 names per navigation dimension) can hit `MaxListItems`; `ancestorOf` keys are
  capped by `MaxListItems` (128 args) and `MaxParameters`.

Caching (§16): L1 text+limits → SyntaxTree; L2 text, schema, root, mode, directions, `HasUser`,
`HasGroupingKeys`, referenced declarations, limits, version → bound tree; L3 schema, every
clause's text + filter-tree shape, version, options, declarations → SQL template + slot map.
Keys hold text (never only a hash); schema by identity; bounded LRU; **a rebuilt schema is the
only invalidation lever.**

Versioning (§17): `QueryexLanguage.Version = 1`, `Minimum = 1`, `IsSupported(int)`. Hosts must
store the version beside every stored expression (minted at `Validate`) and pass it back at
`CompileQuery`; unsupported version is a caller error. Additive changes (new functions, new
calendar codes) do not bump the version; meaning-altering changes need a new version, dual
compilation, and a migration report.

### 2.16 Testing, inspection tool, client-side status (0008 §18–§19, Non-goals)

- Conformance corpus, golden SQL snapshots (`test/core/Tellma.Core.Queryex.Tests/Goldens/`,
  rewritten with `TELLMA_UPDATE_GOLDENS=1`), differential in-memory interpreter (shared from
  `test/shared/Tellma.Queryex.Testing`, never shipped), property tests (round-trip,
  no-duplication, determinism, robustness). No performance suite.
- `eng/queryex-inspection/` (`Tellma.Queryex.Inspection`): Kestrel + one static vanilla-JS page;
  compiles on every keystroke; shows tokens, syntax tree, bound tree, diagnostics with carets, SQL
  and slots; built-in fixture schema; ephemeral port. A precedent for keystroke validation UX,
  not a reusable component.
- **No TypeScript/client-side parser, evaluator, builder, or printer exists** (Non-goals: "A
  client-side evaluator ... is a client-spec question"). Repo search confirms no `.ts` file
  mentions Queryex (see 2.18). The inspection page is vanilla JS that calls the server.
- Test projects: `test/core/Tellma.Core.Queryex.Tests`,
  `test/core/Tellma.Core.Queryex.IntegrationTests` (needs `TELLMA_TEST_SQL`).

### 2.17 Host integration (0012 §11; touch points in 0015/0016)

Schema provider (0012 §11.1):
- `IQueryexSchemaProvider` (namespace `Tellma.Core.Abstractions.Data`, singleton):
  `QueryexSchema GetSchema(MultilingualShape shape)`; `string Fingerprint { get; }` (hex SHA-256 of
  every entity's `(Name, Source, Key, properties, navigations)` in model order).
- **One schema per `MultilingualShape` (`Primary | PrimaryAndSecondary | All`, spec 0013)**,
  lazily built and cached for the process. `P2`/`P3` twins of a `[Multilingual]` group exist in
  the schema **only for shapes that admit them**: on a monolingual tenant `Center.Name2` is
  QX3001 (unknown property), not a null column.
- Entities are declared under `EntityMetadata.Name` (`gl.Center`, `core.RoleMembership`), i.e.
  logical entity names carry a schema prefix; `Source` = `[schema].[Table]`; `Key` = PK.
- Properties: every mapped scalar with a Queryex type; `IsUnique` from PK or single-column unique
  index (filtered included); `StoreType` from EF mapping; enums as `String` with `VarChar(n)`
  (0012 §2.6: enum stored as `varchar(n)`). Absent: `[NotMapped]`, period columns,
  `[JsonColumn]`, `varbinary`, `time`, `float`/`real`, `Blob.StorageKey`, `Blob.Sha256`,
  `Job.StateJson`, `Job.LeaseToken`.
- Navigations: every many-to-one FK, named by the CLR navigation or by the FK column minus `Id`
  (`ParentId` → `Parent`, `CreatedById` → `CreatedBy`, ...); `[ParentKey]` gives the child its
  navigation to the parent; `[Sibling(navigation)]` gives the owner a one-to-one left-joined
  navigation. **No collections.**
- Tree node: shadow `Node` of every tree entity declared `HierarchyId` and registered as
  `TreeNode`, so `descendantOf`, `ancestorOf`, and `level(Node)` bind.
- Read-only roots: `core.UserStamp` (over `core.UserStamps`, navigation `User`), `core.Blob`.
- RLS composition and child rebasing are request-time `FilterTree` constructions (spec 0014); a
  `map<EntityDescriptor, EntityMetadata>` beside each schema maps descriptors back to CLR types.

Engine amendments **documented in 0012 §11.2** (additive; `QueryexLanguage.Version` stays 1;
**not implemented in the engine code today** — no `KeySetRestriction`, `Prologue`, `Via`, or
`level` exists under `src/core/Tellma.Core.Queryex/`):
1. `KeySetRestriction(string Path, string TableSource)` and `QuerySpec.Restrictions`
   (default empty): emits `AND <column> IN (SELECT [Id] FROM <TableSource>)`; the host binds an
   `IdList`/`BigIdList`/`GuidList`/`StringList`/`DateList` TVP or names a `@tb{b}_` table.
   Part of the cache key by shape. Consumers: get-by-ids, child loading, ancestors, deletes, ...
2. `level(node: HierarchyId) -> Numeric` (`.GetLevel()`), NotNull when its argument is.
3. `CompiledQuery.Prologue` (hoisted DECLAREs) + `Body` (the SELECT); `Sql = Prologue + Body`.
   Body embeddable as a derived table (`SELECT COUNT(*) FROM (<body>) AS q`).
4. `Skip`/`Take` as parameter slots with `Origin = Declared`, names `@qx{b}_skip` /
   `@qx{b}_take`, so page changes reuse the plan.
5. `today()` and `TimeZone` bind the tenant zone via `QueryexContextValues` (0012 §5.1);
   `now()` → `Now`; `me()` → `UserId`.
6. Versioned leaves: `FilterTree.Leaf(string text, int languageVersion)`;
   `LeafNode.LanguageVersion` (int?).
7. `FilterTree.Via(string navigation, FilterTree inner)` + `ViaNode { Navigation, Inner }`:
   rebase a subtree onto a to-one navigation target; diagnostics located `Filter.Via[<nav>]`.
- Explicitly still unbuilt: language-level list parameters (`x in (@ids)`) and `descendantOf`
  over a TVP.

Rows, Count, entity queries (0012 §11.3–§11.5):
- `Rows(spec, arguments, options)`: binds `Literal` slots from the compiled query, context slots
  from `QueryexContextValues`, `Declared` slots from `QueryArguments` (missing argument =
  compose-time `ArgumentException`).
- Column buffers chosen by (Queryex type, reader field type): Numeric → `int[]`/`long[]`/
  `short[]`/`byte[]`/`decimal[]`/`double[]`; Bool → `bool[]`; String → `string?[]`; Guid;
  Date → `DateOnly[]`; DateTime; DateTimeOffset; HierarchyId → `string?[]` (path string).
  Numeric `Scale` from the reader's column schema (so `sum(Amount)` reports SQL's computed scale).
- `RowQueryOptions`: `CountCap` (capped count in the same statement), `IncludeAncestors` (tree
  roots only: captures page keys into `@tb{b}_keys`, inserts ancestor ids into `@tb{b}_anc` via
  `IsDescendantOf`, then re-runs the same select restricted to them under `AncestorsFilter` — the
  access filter alone, not the user's filter — ordering kept, no paging → `RowPage.Ancestors`),
  `CaptureKeys`.
- `Query<TEntity>(EntityQuery)` restricts `Select` to bare paths, runs `DiscoverQuery` on the
  select to learn paths, appends root `Id`, each navigation prefix's key path (`Customer.Id`) and
  its FK (`CustomerId`); `Children` add one query per collection via `KeySetRestriction`.
- `EntityMaterializer` walks `QueryexColumn.Path`; depth-1 → root setter; deeper → related
  instance keyed (entity name, id) in `EntityQueryResult.Related`, never assigned to navigation
  properties.
- Related projection (0012 §2.5): default `Id`, the `[Multilingual]` `Name` group, `Code` when
  present, `Avatar` blob refs; `[RelatedSelect]` replaces it. 0015 §5.2 confines a traversal into
  `E` to that projection unless the caller's `Read` on `E` is unrestricted (`CreatedBy.Email`
  → `ForbiddenException`). **This is the server-side analogue of "nav property ⇒ Id + names".**

Wire touch points (outside the assigned sources, recorded for orientation):
- 0016 §3.3 `QueryRequest`: `Select`, `Filter` (**one string**; RLS conjoined server-side; "the
  FilterTree never travels"), `Having`, `OrderBy`, `Skip` (0), `Take` (absent = 50), `Search`,
  `Arguments` (JSON scalars, "types inferred with DiscoverQuery before CompileQuery"),
  `IncludeCount`, `IncludeAncestors`, `IncludeInactive`, `Aggregate`.
- 0015 §4.3: argument types come from **`DiscoverQuery` inference**, not declarations; unmentioned
  arguments ignored; mentioned-but-missing bound `null`.
- 0016 §3.5: rows serialised row-major with `columns[]` of `QueryColumn(Name, Type, Scale,
  Nullable, Path, GroupingKey)`; `Type` written without the `Qx` prefix. Diagnostics are 400
  `query-invalid` with `errorDetails.diagnostics[]` carrying the `QueryexDiagnostic` code,
  position, and arguments verbatim plus the clause name, and `errors[]` one localized item per
  diagnostic (`path` = clause name).
- 0016 §11 MCP: `tellma_describe` returns per-entity properties (type, store type, nullable,
  enum values, multilingual group, navigation target and reachable columns, default select, three
  example filters) and a `tellma://queryex/syntax` resource; `tellma_aggregate` uses 0008's
  aggregate mode. This is the only existing machine-readable schema description; there is no
  equivalent web (`/api/web`) endpoint named in the sources read.

### 2.18 Code-level facts verified (src/core/Tellma.Core.Queryex, ~18.9k lines)

- Public types exactly as the spec: `QueryexEngine`, `QueryexResult<T>`, `QueryexDiagnostic`,
  `QueryexSpan` (adds `End => Start + Length`), `QuerySpec`, `QueryDiscoverySpec`,
  `QueryCompilationOptions`, `ValidationOptions`, `DiscoveryOptions`, `QueryexEngineOptions`,
  `CompiledQuery`, `QueryexColumn`, `QueryexParameterSlot`, `QueryexParameterOrigin`,
  `QueryexParameterDeclaration`, `DiscoveryResult`, `ParameterUse`, `PathUse`, `TypeConflict`,
  `QueryexTextSite`, `ValidatedExpression`, `ValidatedItem`, `QueryexDirection`,
  `QueryexMode`/`QueryexShape`/`QueryexGrouping`, `QueryexNullity`, `QueryexType`,
  `QueryexStoreType`/`QueryexStoreFamily`, `QueryexLimits`, `QueryexLanguage`, `QueryexSchema`,
  `QueryexSchemaBuilder` (+ nested `EntityBuilder`), `EntityDescriptor`, `PropertyDescriptor`,
  `NavigationDescriptor`, `FilterTree` (+ `LeafNode.Text`, `AndNode.Children`,
  `OrNode.Children`, `NotNode.Operand`).
- `EntityBuilder.Key(name, type, column, storeType?)`, `Property(name, type, column,
  isNotNull = false, isUnique = false, storeType = null)`, `TreeNode(name)`,
  `Navigation(name, target, foreignKey)`.
- `StatementEmitter.Write` emits, in order: DECLAREs, `SELECT <item> AS [c0], ...`, `FROM`,
  `INNER JOIN`/`LEFT JOIN ... ON [Pn].[Key] = [parent].[FK]`, `CROSS APPLY (VALUES (...)) AS
  [Bn] ([v])`, `WHERE`, `GROUP BY`, `HAVING`, `ORDER BY ... ASC|DESC` (direction always written),
  `OFFSET ... ROWS [FETCH NEXT ... ROWS ONLY]`, `;`. **Result columns are aliased `[c0]`, `[c1]`,
  ... (prefix `"c"`), not by author text.** Skip without Take emits `OFFSET n ROWS` alone; Take
  without Skip emits `OFFSET 0 ROWS FETCH NEXT ...`.
- Grep of `Emit/`, `Lowering/`, `Functions/` finds no `ROLLUP`, `GROUPING`, `CUBE`, `OVER`,
  `ROW_NUMBER`, `WITH` (CTE), `TOP`, or `DISTINCT`. The only `UNION ALL` is inside the internal
  `EXISTS (SELECT v INTERSECT (SELECT e1 UNION ALL SELECT e2 ...))` set-membership guard
  (`PlanEmitter.WriteSetMembership`). Hierarchy test uses `.IsDescendantOf(`.
- Order-by items that structurally equal a select item are emitted **by the select alias**
  (`ORDER BY [c2] DESC`), and tiebreakers reuse aliases (`QueryClauses.LowerOrdering`).
- Paging slots are created by `Lowerer.PagingSlot(value)` as **Literal-origin** slots, and
  `CacheKeys` puts `spec.Skip`/`spec.Take` values into the L3 key, so each distinct page compiles
  and caches separately (the 0012 §11.2 item 4 amendment exists to fix this).
- `QueryexColumn.Text` is `Select.Substring(item.Span)` — the author's raw item text; `Path` is
  the bound `TypedPath.Segments` for a bare path.
- Diagnostic argument keys (internal `DiagnosticArgumentNames`): `name`, `entity`, `type`,
  `otherType`, `function`, `expected` (machine-readable list), `actual`, `limit`, `operator`,
  `signatures`, `accepted`. Host message catalogues key on these.
- QX4006 (paging without ordering) is reported with a default span (0, 0) and the OrderBy clause
  location.
- Function registry contains exactly the §10 set plus nothing else (`count sum avg min max local
  year quarter month day week weekday hour minute second startOfYear startOfMonth startOfWeek
  startOfDay addYears addMonths diffYears diffMonths addDays addHours addMinutes addSeconds
  diffDays diffHours diffMinutes diffSeconds today now me if coalesce cast abs floor ceiling round
  length trim upper lower left right substring replace contains startsWith endsWith descendantOf
  ancestorOf`); the only calendar constant is `Gregorian = "gc"`.
- **No TypeScript anywhere under `src/` mentions Queryex** (grep of `*.ts`): no client lexer,
  parser, printer, builder, or evaluator exists.
- `QueryClauses.DeriveKeys` marks every aggregation-free, path-reading item as a key **without
  deduplicating** structurally equal items (the spec's "one key per distinct bound tree"); a select
  list that repeats a dimension gets two `IsGroupingKey` columns and two GROUP BY entries.

## 3. Gaps for round 2

Each is a capability round 2 needs and the sources do not provide. "Amend 0008" items are
candidate parts of spec 0010 (frozen spec ⇒ amendment); others belong to round-1 or round-2 specs.

### 3.1 Report engine (0033/0034) gaps in the language and emitter

1. **Subtotals and grand totals at every pivot level (brief: "aggregated in SQL for every (row,
   column) pair at every level").** The emitter has exactly one `GROUP BY <keys>` shape (§13.4);
   no `GROUPING SETS`/`ROLLUP`/`CUBE`, no `GROUPING()`/`GROUPING_ID()`. Today a pivot needs one
   `CompileQuery` per grouping level — (r+1)×(c+1) queries for r row and c column dimensions
   (e.g. 3×2 dims ⇒ 12 SELECTs) — which can share one round trip via `BatchOrdinal` (§13.1) but
   each re-scans and re-filters the fact rows. A `GROUPING SETS` amendment also needs a
   per-row "which set" indicator, because grouping keys are nullable (a "no Center" member and a
   "Center subtotal" row are both NULL without `GROUPING()`), and needs aggregate nullity
   (§8.3, `HasGroupingKeys`) revisited because the `()` set is a grand total inside a grouped
   query. Non-additive measures (`avg`, `min`, `max`, conditional `count`) are correct at every
   level either way, which is why the client must not roll them up.
2. **Tree-dimension rollups (parent = aggregate of its subtree, plus a phantom child for the
   parent's own rows).** The phantom child is expressible today: grouping by `Account.Id` yields
   each node's own rows, parents included. The rollup is not: there is no way to attribute a fact
   row to each ancestor of its dimension node. The schema has only many-to-one navigations (§3:
   "no collections", the no-fan-out invariant that makes aggregation sound), there is no
   non-FK join, no `ancestorAt(node, level)` function, and `level()` exists only as an unbuilt
   0012 §11.2 amendment. One workaround appears available but does not scale: one conditional
   measure per parent node, `sum(Amount, descendantOf(Account.Id, @k1))`. It reads as legal under
   §10.2 + §10.10, but no test exercises it; it costs one hoisted lookup and one slot per key, and
   is bounded by `MaxListItems` 128 and `MaxParameters` 512.
   A real fix needs an engine construct (a "tree rollup" grouping key that fans out over the
   ancestor closure via `IsDescendantOf`, or a host-provided closure source) plus the rule that
   only that key may fan out.
3. **Running totals (brief open question 5).** No window functions, no `over`, no correlated
   subqueries (§10.2, §13.4). Needs an amendment emitting `SUM(x) OVER (PARTITION BY ... ORDER BY
   ... ROWS UNBOUNDED PRECEDING)`; T-SQL evaluates the window before `OFFSET/FETCH`, so the totals
   stay correct across pages. For aggregate reports it needs `SUM(SUM(x)) OVER (...)`, which the
   language's "aggregations may not nest" rule (QX4003) forbids today, so it must be a distinct
   construct, not a nested call. The alternative that needs no amendment is an "opening balance"
   grand-total query (`sum(x)` with `PostingDate < @From`), with the client adding the rows up,
   which is client-side aggregation.
4. **Navigation property as a select item ⇒ Id + its three name columns.** `Center` alone is
   QX3002 (§6.1); the language has no `path.*` or nav-expansion (Ahmad declined `*`/`path.*` when
   0008 was authored, per the spec-0008 memory note). Expansion must happen before the engine,
   in the host or the client, and must be **shape-aware**: `Name2`/`Name3` exist in the schema
   only for tenants whose `MultilingualShape` admits them (0012 §11.1), and asking for an absent
   twin is QX3001. The natural expansion target is the related projection (0012 §2.5: `Id`, the
   `Name` group, `Code`, avatar blob), which is also what 0015 §5.2's traversal rule allows. In an
   aggregate query each expanded column becomes its own grouping key (harmless, since they
   depend on the Id). Nothing maps the expanded columns back to "one nav column" in the
   response: `QueryexColumn.Path` gives `["Center","Name2"]`, so the client must group columns
   by path prefix itself.
5. **Ordering by a navigation or name in the user's language.** The engine has no notion of
   language; ordering by `Center.Name` sorts by the primary language. Server-sorted results go
   stale on a language switch that the brief says must not reload. Either the client re-sorts
   (sorting, not aggregation) or the order term is rewritten per language and the data re-queried.
6. **Dimension attributes** (e.g. Account code on an Account dimension). Expressible today as
   extra aggregation-free select items, which become grouping keys (§11.3). But the engine cannot
   tell an attribute from a dimension: both come back `IsGroupingKey = true`, so the report
   definition (0033) must carry that role. With per-level queries (gap 1), the attributes of a
   dimension that a level omits must be omitted from that level's select. `min(Account.Code)` as
   a measure is the other spelling.
7. **Optional parameters do not drop out.** Comparison is total and two-valued (§9.2): with an
   absent `@From`, `PostingDate >= @From` is **false for every row**, so an optional report
   parameter left blank returns nothing instead of "no restriction". 0015 §4.3 binds a missing
   argument as `null`, which turns this into zero rows on the wire. Authors must write
   `(@From is null or PostingDate >= @From)`, or the definition model or an engine feature must
   supply "drop the atom when the parameter is absent". This bears directly on the brief's
   "Requirement Level (Required/Optional)".
8. **Parameter default expressions** (brief: each report parameter has a "Default Expression",
   e.g. `startOfMonth(today())`). There is no root-less "evaluate a constant expression" entry
   point: `CompileQuery` always selects from a root entity, and an aggregate query with neither a
   key nor an aggregation emits one row per underlying row (§2.1). Evaluating defaults needs an
   engine entry point (e.g. compile a path-free expression to `SELECT <expr>` with no `FROM`), a
   host trick, or client-side evaluation (and no client evaluator exists).
9. **Parameter control types.** Inference gives only a `QueryexType` (`CenterId = @c` ⇒
   `Numeric`). Nothing says "this parameter is compared against `Center.Id`/`CenterId`", which
   the studio needs to offer an entity picker. `DiscoveryResult` lists `ParameterUse` sites and
   `PathUse` sites separately, with no relation between them. A "compared-with path" fact on
   `ParameterUse` would be an additive amendment.
10. **Calendars in reports.** Only `'gc'` is implemented; `'uq'` (Umm al-Qura, which the reference
    distro configures) and `'et'` produce QX3101 (§10.4). `'uq'` also needs a host-provided Hijri
    month-map table "named through the schema contract", which `QueryexSchema` has no member for.
    The calendar is a compile-time literal in the expression text, so the session-menu
    "calendar toggle" cannot switch a stored report's `month(PostingDate)` without a new feature,
    such as a compile-time "current calendar" option that would join `HasUser` in the cache keys.
11. **Aggregations missing for reporting**: no distinct count, `string_agg`, stdev, var,
    median, first/last; no `startOfQuarter`; no `nullif`; no `format`/`concat` function.
12. **Multi-valued parameters** (`x in (@Centres)`) are deferred (Non-goals). A report parameter
    that takes several centers cannot be expressed; 0012 §11.2 covers only host
    `KeySetRestriction`s on `Id`/`ParentId` paths, not user expressions.
13. **Shared dashboard parameters.** Declaring a parameter that no expression references is a
    caller error (§6.3), so a dashboard host must project its parameter set onto the names each
    report's clauses actually mention (lexically identifiable via `Discover`).
14. **Having vs subtotals consistency.** `Having` filters each group of one query. If subtotals
    are computed by separate queries (gap 1), a subtotal still includes the leaf groups that
    `Having` removed, so it no longer equals the sum of its visible children. `GROUPING SETS`
    has the same property. 0033 must define the semantics.

### 3.2 Client (0029/0030/0034) gaps

15. **No client-side Queryex code at all.** The 0029 "client Queryex builder" starts from zero:
    no TS lexer (needed for client-side `@Name` extraction, syntax highlighting, and drop-at-caret
    insertion in the studio), no printer, no builder, no evaluator (§Non-goals). A client lexer
    must reproduce §4 exactly: `'...'` with `''` escapes, `[...]` brackets, `@Identifier`,
    Unicode letter classes, no comments. Otherwise `'a@b'` yields a phantom parameter.
16. **No web endpoint for `Validate`/`Discover`/`DiscoverQuery`.** The engine APIs exist, and
    0015/0016 use `DiscoverQuery` internally to type arguments, but no `/api/web` route found in
    the sources exposes them. The studio's "parameters auto-populate as the user types",
    keystroke validation, and path-tree browsing need one: a POST endpoint returning
    `DiscoveryResult` / diagnostics for a draft definition, cancellable per keystroke (brief rule
    2C).
17. **No schema description for the client.** `QueryexSchema` carries no labels, no multilingual
    names, no enum values, no display-name property, no related projection, and no searchable
    flags. The column editor's infinite nav tree, the report studio's field list, and typed
    column filters need entity metadata from elsewhere: 0012 `EntityMetadata` §3.1 and
    `tellma_describe` (0016 §11, MCP-only today). There is also no public function catalogue (the
    registry is internal, §10.1), so studio autocomplete for functions must be hand-maintained or
    newly exposed.
18. **Diagnostic display in a text filter box.** The pieces exist: `Span` is relative to one
    expression; 0016 §3.5 returns code, position, arguments, and clause name verbatim. Two
    obstacles remain. (a) The wire `Filter` is **one string** (0016 §3.3), so a search page that
    combines column filters, the search box, and the advanced free-text filter must concatenate
    them client-side, and spans then index into the concatenated text. The client must keep an
    offset map, or the wire must carry separate leaves. (b) Display text needs a host message
    catalogue keyed on Appendix A codes plus the argument keys in 2.18; no localized catalogue is
    specified in the sources read.
19. **Client-side filter composition hazard.** Because the wire filter is one string, the client
    builder must parenthesise every fragment (`(a) and (b)`). 0008 §2.3 shows textual
    concatenation is wrong otherwise (`A or B and C`). For the user's own filter this is a
    correctness issue, not a security one, since RLS is still conjoined structurally on the
    server.
20. **Search box semantics are not Queryex.** `QueryRequest.Search` is carried unchanged and
    expanded server-side via `SearchFilter` / the `[Searchable]` disjunction (0015); the client
    must not build a `contains(...)` filter for it. Note that `contains`/`startsWith` emit
    CHARINDEX-style and never seek (§10.9).
21. **Typed column filters vs total comparison.** The "between" filter is `x >= @a and x <= @b`
    and excludes absent x (§9.2). The "not equals" filter (`x != v`) **includes** rows where x is
    absent (`!=` is true when one side is absent), which differs from SQL users' intuition. There
    is no `not in` (write `not (x in (...))`), no `between`, no `like`, no case-insensitive
    compare beyond the collation, and no "ends with" index seek.
22. **Tree search ancestors.** Covered server-side by 0012 `IncludeAncestors` (`RowPage.Ancestors`)
    for search pages. A pivot or tree report has no equivalent "ancestors of the dimension
    members that appear" option: it needs an extra `ancestorOf(Id, @k0, ...)` query on the
    dimension entity (≤ 128 keys per `MaxListItems`) or a `KeySetRestriction`-based ancestor
    load.

## 4. Likely change points

- **Spec 0010 (new Part for 0008)**: the home for every engine amendment above (grouping sets
  with a grouping indicator, tree rollup grouping, window or running measures, a constant-
  expression entry point, parameter-to-path relations in discovery, maybe distinct count,
  `startOfQuarter`, a calendar compile option, list parameters). Each must state whether it bumps
  `QueryexLanguage.Version` (additive ⇒ no, §17) and how it enters the L2/L3 cache keys (§16).
- **0012 §11.2 "Engine amendments"**: these seven amendments to frozen 0008 live in a round-1
  spec. Under this round's README rule, amendments to 0001–0009 belong in 0010, so §11.2 either
  moves to 0010's 0008 part (0012 then cites it) or is explicitly accepted as an exception. The
  round-2 amendments should sit beside them, wherever they land, to avoid two amendment homes.
- **0012 §11.1 schema provider**: may need to declare the `'uq'` month-map table, any closure
  source for tree rollups, and possibly display metadata (or a separate metadata seam), if 0029
  wants a schema-shaped description.
- **0012 §11.3 `Rows`/`RowQueryOptions`**: a pivot needs a multi-statement or grouping-sets
  result; the row set (`QueryRowSet`, `QueryColumn`) may need a grouping-level indicator column
  and the ancestor-loading option for dimensions.
- **0015 §4.3 Arguments**: typing arguments by inference conflicts with stored report definitions
  whose declarations are authoritative (§2.4 designer flow). 0033 must decide whether report
  execution passes declarations (`QueryCompilationOptions.Parameters`) instead of re-inferring,
  and what happens to an unconstrained parameter (`InferredType` null) on the web path. The
  "missing argument ⇒ null" rule interacts with gap 7.
- **0016 §3.3 `QueryRequest` / §3.5 diagnostics**: possible additions — separate filter leaves
  (or a client offset map), a discover/validate route, an aggregate grouping-sets request shape,
  a report-execution route (0033).
- **0016 §11 `tellma_describe`**: candidate source for the client entity model (0029) so the web
  client and MCP describe entities from one place.

## 5. Contradictions and surprises

1. **0016 §3.5's example filter `"Name contains @q"` is not valid Queryex.** `contains` is a
   function (`contains(Name, @q)`, §10.9); there is no infix `contains`. Copying the example would
   return QX2001.
2. **Skip/Take slot origin conflicts.** 0008 §13.1 makes paging values `Literal`-origin slots
   ("a value fixed at compile time ... or a paging value"), and the code also keys L3 on the
   Skip/Take values, so each page is a separate L3 entry. 0012 §11.2 item 4 redefines them as
   `Declared`-origin slots named `@qx{b}_skip`/`@qx{b}_take`, and 0016 §3.5 already states "every
   page shares one plan" as if this were built. It is not. (0008 §2.1's "paging does not fragment
   the plan cache" is true of SQL Server's plan cache, not of the engine's L3.)
3. **0008 §1.4 says the public surface is "exactly" §1.4, §2, §3**, yet 0012 §11.2 adds public
   API (`KeySetRestriction`, `Prologue`/`Body`, `Via`, versioned `Leaf`, `level`) to the frozen
   engine, and 0012 calls it "additive; Version stays 1". These are amendments to a frozen spec
   living outside 0010. Round 2 should consolidate them.
4. **No column aliases or labels.** Emitted columns are `[c0]`, `[c1]`, ...; `QueryexColumn.Text`
   is the raw item text and 0016's `QueryColumn.Name` is derived from it. Report labels
   (Label/Label2/Label3) and the column editor's "Center / Code" labels are entirely a host or
   client concern. A designer may assume an `as` keyword; there is none.
5. **Grouping is implicit.** There is no `group by` clause: every aggregation-free, path-reading
   select item is a key, and an ordering term may not add one (QX4005). A pivot's "rows",
   "columns", and "attributes" all collapse into one select list; the definition must keep the
   roles. Mixing `Price * sum(Qty)` is QX4007.
6. **`Having` cannot read dimensions** (QX4004: every path must be inside an aggregation). A
   "having Account.Code starts with 1" is a `Filter`, not a `Having`.
7. **Optional-parameter trap** (gap 7). It is easy to assume SQL-style "NULL parameter ⇒ ignore"
   or the monolith's behaviour; Queryex returns no rows.
8. **`!=` includes absent values; `<`/`>` exclude them** (§9.2). `not (a < b)` ≠ `a >= b`. A
   typed "not equals" column filter shows rows with no value; a "between" filter hides them.
9. **Calendar operations reject `DateTimeOffset`** (QX3103). Reports on `CreatedAt`/`ModifiedAt`
   style columns (if `DateTimeOffset`) must wrap them in `local(...)`, which uses the **tenant**
   zone, not the user's browser zone. `today()` is also the tenant-zone date.
10. **`Date` vs `DateTimeOffset` never mix** (`now() >= PostingDate` is QX3200). A report
    parameter's type must match the column family exactly; string literals coerce, parameters do
    not.
11. **`descendantOf`/`ancestorOf` are reflexive and key on a unique property**, not on the node.
    `descendantOf(Id, @k)` includes the `@k` row itself. The key can be `Code`/`Concept` only if
    that column is `IsUnique` (QX3302).
12. **Parameters are scalar only**: a HierarchyId or Geography parameter is a caller error, so a
    "node" cannot be passed; pass the key and use `descendantOf`.
13. **Declaring an unreferenced parameter throws** (§6.3), a caller error, not a diagnostic. Report
    and dashboard hosts that pass a superset of declarations will crash rather than get a
    diagnostic.
14. **Logical entity names carry the schema prefix** (`gl.Center`, 0012 §11.1). The client builder
    and report definitions name roots that way, while paths use bare navigation and property
    names.
15. **`startsWith` never seeks an index** (CHARINDEX emission by decision, §10.9). Prefix-search
    UX over big tables is slow by design, and 0008 says such workloads need "a dedicated path".
16. **Brief vs language on aggregation**: the brief wants "the Sum of its path (by default)" for a
    dragged measure. `sum` accepts only `Numeric` (§10.2), so the studio must pick `count(x)` or
    `max(x)` for non-numeric fields and must never wrap a navigation (not a legal path, QX3002).
17. **Limits a pivot can hit**: `MaxListItems` 128 per select list (nav expansion ×4 per
    dimension), `MaxJoins` 32 after pruning, `MaxParameters` 512. The limits are configurable per
    call site, and a report host may want a different `QueryexLimits` profile than the search
    box; each profile fragments the caches (limits are in every key, §16).
18. **Code vs spec nit**: structurally equal select items are not deduplicated as keys
    (2.18), unlike §11.3's "one key per distinct bound tree".
