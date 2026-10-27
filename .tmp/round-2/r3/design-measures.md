# Design: measures over aggregates, evaluated in C#

- **For:** decisions 1, 4 and 5 of `.tmp/round-2/r3/decisions.md`: spec 0010 Part F, specs 0033
  and 0034, and the level-navigation cascade.
- **Date:** 26 October 2026. **Status:** design note; it wins over
  `.tmp/round-2/review-r1/g1-0010-partF.md` on its topic. No spec was edited.

**One deviation from decision 1's wording, flagged for Ahmad.** Decision 1 asks for "a practical C#
comparison (exact for GUIDs, approximate for text)". This note makes text **exact** instead, at
about the same cost: SQL ranks the text partials and C# compares ranks (§4.1). The approximate
comparer is measured in §4.2 and stays the fallback (flag F2). "Exact for GUIDs" has no case to
cover: Queryex has no `min`/`max` over `Guid` (0008 §7.1), so nothing is built for them.

**(verified)** marks a fact measured on 26 October 2026: SQL Server 2019 LocalDB (15.0.4382)
against .NET 10.0.401 with ICU. Decimal typing has not changed since SQL Server 2005, so the
results carry over to Azure SQL.

---

## 1. Answers to Ahmad's three questions

**"What subset of Queryex can we support in a C# evaluator without substantial implementation
complexity?"**

- **Around the aggregates:** constants, parameters, `today()`, `now()`, `me()`; `-`, `+`, `*`, `/`;
  comparisons and `in` over numbers, booleans and dates; `is null`; `and`, `or`, `not`; `if`,
  `coalesce`; `abs`, `round`, `floor`, `ceiling`; identity `cast`s; over `Date` values, the
  Gregorian `year`, `quarter`, `month`, `day` and the `diff` functions of elapsed time. The
  aggregates themselves: all five, any type.
- **Refused at save (QX4010):** `%`, `||`, converting `cast`s, the other date functions, the text
  functions, comparisons of text and hierarchy paths.
- **Coverage:** ratios, percentages, margins, growth, conditional and signed sums, null guards and
  recency, such as `if(sum(Amount) = 0, null, sum(Amount - Cost) / sum(Amount))` and "days since
  the last sale", `diffDays(max(PostingDate), today())`.
- **Cost:** about 800 lines in `Tellma.Core.Queryex`, plus tests (§3.4). SQL Server's decimal
  algebra ships in .NET as `SqlDecimal`, which the engine already uses for literals.

**"If we can't have a perfect match between C# and SQL Server's collations, can we get a practically
useful approximation where the differences don't matter much in business reports?"**

- **Yes, and better: exact, without matching collations.** The statement that returns a text
  partial also returns its rank, `DENSE_RANK() OVER (ORDER BY MAX(Name))`. C# compares integers, so
  the combined `max` is SQL Server's own under any collation (§4.1).
- **The approximation also works:** a .NET comparer disagrees with SQL Server on 1.7–2.5% of
  deliberately awkward pairs (hyphens, symbols, Arabic hamza spellings, ligatures; §4.2), and only
  text `min`/`max` across subtotals would see it.
- **Numbers are exact.** The evaluator runs SQL Server's decimal rules at each value's real SQL
  type, so every digit matches (§5).

**"I presume the implementation now would extract the aggregation functions from all the measures,
query those in SQL, then evaluate the expressions surrounding those aggregation functions in C#?"**

Yes, with four refinements:

1. **`avg` splits** into `sum` and `count`; every other aggregate is its own partial.
2. **Partials are shared** across measures, and with equal select items.
3. **Pivots evaluate every cell in C#, leaf cells included.** SQL returns partials only.
4. **Summaries keep SQL for their rows** (it orders, ranks and filters them anyway) and use C# only
   for the totals row. Charts never use C#.

---

## 2. The pipeline

### 2.1 Who computes what

SQL computes everything at the grain of its own `GROUP BY`. C# computes everything above it.

| Kind | SQL returns | SQL evaluates | C# evaluates |
|---|---|---|---|
| Pivot | one row per leaf combination: keys, labels, partials | `having` on leaf rows | every cell: leaves, subtotals, totals |
| Summary | one row per group: keys, labels, measures; partials when totals apply | measures, `having`, `orderBy`, `top`, paging | the totals row only (§6) |
| Chart | one row per point: keys, labels, the measure | measure, `having`, ordering, `top` | nothing |

**Why every pivot cell comes from C#:** one code path, so a parent with one child shows exactly the
child's number; fewer columns, since SQL computes partials only; and no exactness lost, since the
evaluator reproduces SQL Server's value and scale (§5), proven per measure (§7.5).

### 2.2 Steps

1. **Bind.** The compiler passes the measures to the engine as `QuerySpec.Measures` (§7.1), which
   binds each one on the Group axis like a select measure.
2. **Check.** The engine walks each tree outside the aggregates. Anything outside §3's subset is
   QX4010 at the node's span, at save and in the studio.
3. **Extract atoms.** Every aggregate call is an atom. `avg(x, c)` becomes `sum(x, c)` and
   `count(x, c)`.
4. **Deduplicate.** Atoms with structurally equal bound trees (0008 §11.3) are one partial. A
   partial equal to a select item reuses that column; the rest are appended.
5. **Rank.** A `String` or `HierarchyId` `min` or `max` partial gets a `DENSE_RANK()` column
   (§4.1).
6. **Run.** SQL groups at the statement's grain and applies `having`, as today.
7. **Combine (C#).** Each leaf row is one leaf cell. The cube builder folds cells into their parents
   bottom-up, first along the row tree, then along the column tree: `sum` and `count` add, `min`
   and `max` keep the least or greatest, nulls are skipped. Every combine is associative and
   commutative, so the cost is one fold per cell and partial, whatever the depth.
8. **Evaluate (C#).** Each cell evaluates each measure's outer expression over its combined
   partials, at SQL Server's types (§5).

### 2.3 `having`, `orderBy` and `top`

- **`having` runs in SQL only,** on the statement's own rows: leaf rows in a pivot (decision 3),
  groups in a summary or chart. A failing row never reaches C#. Every subtotal and total is the
  measure recomputed over the surviving rows, so totals always match the visible cells.
- **`orderBy` and `top` run in SQL only,** over the measure value SQL computes. Only summaries and
  charts have them. C# never reorders and never re-applies `top`, so a summary's totals cover the
  rows `top` kept (decision 4).
- **Pivots have neither.** Members follow 0033 §9.3's order (labels and values, by culture), which
  never reads a measure.
- **They cannot disagree.** The value SQL ranks or filters by equals the value C# computes for that
  row (§7.5, identity 1), so no row is kept for a number it does not show.

### 2.4 Worked example

A pivot over `acme.SalesInvoiceLine` with rows by `Center` (a tree). For this example the line also
has `Cost` and `Discount`, both `decimal(19,4)`.

```text
Select    = Center.Id
Measures  = sum(Amount), sum(Amount - Cost) / sum(Amount), avg(Discount)
Aggregate = true, OrderBy = Center.Id, Take = MaxPivotRows + 1
```

The engine finds four partials. `sum(Amount)` appears twice and is one column:

```sql
SELECT [P1].[Id] AS [c0], SUM([T].[Amount]) AS [c1], SUM([T].[Amount] - [T].[Cost]) AS [c2],
       SUM([T].[Discount]) AS [c3], COUNT_BIG([T].[Discount]) AS [c4]
FROM [acme].[SalesInvoiceLines] AS [T]
LEFT JOIN [gl].[Centers] AS [P1] ON [P1].[Id] = [T].[CenterId]
GROUP BY [P1].[Id]
ORDER BY [c0] ASC OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY;
```

Head office (10) has facts of its own; its children Riyadh (11) and Jeddah (12) have the rest.

| Leaf row | `c1` sum(Amount) | `c2` sum(Amount − Cost) | `c3` sum(Discount) | `c4` count(Discount) |
|---|---|---|---|---|
| 10 | 2,000.0000 | 500.0000 | 0.0500 | 1 |
| 11 | 30,000.0000 | 9,000.0000 | 0.3000 | 4 |
| 12 | 20,000.0000 | 4,000.0000 | 0.1000 | 2 |

The cube builder folds the three leaf cells into Head office and the total, then evaluates. The
sums are `decimal(38,4)` and the count `bigint`; both divisions give `decimal(38,6)`, truncated as
SQL does:

| Row | Sales | Margin % | Avg discount |
|---|---|---|---|
| Head office | 52,000.0000 | 13,500 / 52,000 = 0.259615 | 0.4500 / 7 = 0.064285 |
| — Head office (direct) | 2,000.0000 | 0.250000 | 0.050000 |
| — Riyadh | 30,000.0000 | 0.300000 | 0.075000 |
| — Jeddah | 20,000.0000 | 0.200000 | 0.050000 |
| Total | 52,000.0000 | 0.259615 | 0.064285 |

A naive rollup is wrong twice: averaging the margins gives 0.25, averaging the averages 0.058333.
The C# total equals SQL's own answer with no `GROUP BY`, because SQL's `AVG` truncates exactly as
the evaluator does **(verified)**.

---

## 3. The evaluator subset

### 3.1 What may appear outside the aggregates

A measure reads every path inside an aggregation (0008 §11.3). Outside them there are only
operators, functions, constants and context values:

| Construct | Status | Reason |
|---|---|---|
| number literal | supported | `decimal(digits, written scale)`, exactly as the engine binds it |
| `null`, `true`, `false` | supported | |
| string literal | supported as a value; as a date when it coerces (0008 §7.3) | `if(c, 'High', 'Low')` only selects |
| parameter `@p` | supported | from the run's arguments; omitted means absent (spec 0010 §F.8) |
| `today()`, `now()`, `me()` | supported | the values the statement's slots were bound with |
| unary `-`; `+` `-` `*` `/` | supported | SQL Server's decimal rules (§5) |
| `%` | refused, QX4010 | no reporting use; its own type rule; `SqlDecimal` has none |
| `\|\|` | refused, QX4010 | building text belongs in dimensions and attributes |
| `=` `!=` `<` `<=` `>` `>=`, `in` over Numeric, Bool, Date, DateTime, DateTimeOffset | supported | exact in C# |
| the same over String, Guid or HierarchyId | refused, QX4010 | text equality depends on the collation; a path's text order is not the hierarchy's; a Guid can only be a constant here |
| `is null`, `is not null` | supported, any type | |
| `and`, `or`, `not` | supported | two-valued; `and` and `or` stop at the first operand that decides (§5.4) |
| `if`, `coalesce` | supported, any type | lazy: only the chosen branch runs, as SQL's `CASE` |
| `abs`, `round`, `floor`, `ceiling` | supported | `SqlDecimal` has all four, with SQL's types **(verified)** |
| `cast` to its operand's own type; `cast(null, …)` | supported | the identity: emits its operand unchanged (0008 §10.8), so the node is its operand |
| any other `cast` | refused, QX4010 | text rendering and parsing rules are large; no ratio needs them |
| `year`, `quarter`, `month`, `day` over `Date`, calendar `'gc'` | supported | `DateOnly`'s parts, typed `int` as the emitter's `DATEPART` |
| `diffDays`, `diffHours`, `diffMinutes`, `diffSeconds` over `Date` | supported | whole days, exact: `DayNumber` difference × 24 hours, then the emitter's division to `decimal(26,6)`; `diffSeconds` is `bigint` |
| other calendar functions, `'uq'`, `local`; the same functions over `DateTime` or `DateTimeOffset`; `addDays` … `addSeconds` | refused, QX4010 | need the tenant zone, the `'uq'` month table or `DATEDIFF_BIG`'s boundary counting in UTC |
| text functions (`length` … `endsWith`) | refused, QX4010 | collation-dependent, or text measures |
| `descendantOf`, `ancestorOf`, `level` | unreachable | each needs a path: QX4011 outside an aggregate |
| `count`, `sum`, `avg`, `min`, `max` | atoms | decomposed (§2.2); text and hierarchy `min`/`max` ranked (§4.1) |

Two new engine codes:

- **QX4010** — "This cannot be computed over rolled-up values". Argument `construct`: the operator
  or function name, or `text comparison`. Span: the node.
- **QX4011** — "A measure must aggregate": a path outside an aggregation, or no aggregation at all.
  In a `Measures` item it replaces QX4007 for a stray path, so the two never fire on one node.

**Example —** `diffDays(min(PostingDate), max(PostingDate))` over the `Date` column is supported.
`diffHours(min(PostedAt), max(PostedAt))` over a `DateTimeOffset` is QX4010 on `diffHours`; the
author shows `min(PostedAt)` and `max(PostedAt)` as two measures instead.

### 3.2 Which measures need the subset

Only measures C# evaluates: **every pivot measure**, and **a summary's measures when `totals` is
true**. A chart measure, and a summary measure without totals, keep the whole language, since SQL
evaluates them.

### 3.3 The tree the evaluator runs

The engine lowers each bound measure into a small tree. Like every engine IR (0008 §1.3), it is
internal: callers see `CompiledMeasure` (§7.2), never the nodes.

```csharp
internal abstract record MeasureNode(QueryexType Type);
internal sealed record PartialNode(int Partial) : MeasureNode;                     // a sum, count, min or max column
internal sealed record AverageNode(int Sum, int Count) : MeasureNode;              // sum / count at decimal(38, max(s, 6)), truncated
internal sealed record ConstantNode(object? Value, byte Precision, byte Scale) : MeasureNode; // a literal; null is a typed absent value
internal sealed record SlotNode(QueryexParameterOrigin Origin, string? Name) : MeasureNode;  // @p, today(), now(), me()
internal sealed record NegateNode(MeasureNode Operand) : MeasureNode;
internal sealed record ArithmeticNode(ArithmeticOperator Op, MeasureNode Left, MeasureNode Right) : MeasureNode; // + - * /
internal sealed record CompareNode(ComparisonOperator Op, MeasureNode Left, MeasureNode Right) : MeasureNode;
internal sealed record InNode(MeasureNode Value, IReadOnlyList<MeasureNode> Elements) : MeasureNode;
internal sealed record IsNullNode(MeasureNode Operand, bool Negated) : MeasureNode;
internal sealed record LogicalNode(bool IsAnd, IReadOnlyList<MeasureNode> Operands) : MeasureNode; // left to right; stops at the first deciding operand
internal sealed record NotNode(MeasureNode Operand) : MeasureNode;
internal sealed record IfNode(MeasureNode Condition, MeasureNode WhenTrue, MeasureNode WhenFalse) : MeasureNode;
internal sealed record CoalesceNode(IReadOnlyList<MeasureNode> Operands) : MeasureNode;
internal sealed record NumericCallNode(NumericFunction Function, MeasureNode Argument, int Digits) : MeasureNode; // abs, round, floor, ceiling
internal sealed record DateCallNode(DateFunction Function, MeasureNode First, MeasureNode? Second) : MeasureNode; // year … day; diffDays … diffSeconds over Date
```

### 3.4 Size

| Piece | Approximate lines |
|---|---|
| `Measures` clause: binding, QX4010/QX4011 walk, discovery | 120 |
| Decomposition: atoms, `avg` split, deduplication, select-item reuse | 120 |
| Emission: appended partial and rank columns | 60 |
| Evaluator: lowering and type preparation (§5.1), date parts and distances | 250 |
| Evaluator: evaluation; `PartialTable`'s typed columns, accumulation and merge | 250 |
| **Total** | **about 800**, plus about 1,400 lines of tests |

The refused constructs would add thousands of lines (calendars with `'uq'`, text functions per
collation, `cast`'s rendering), and text would still be inexact.

---

## 4. Text, hierarchy and GUID `min`/`max`

### 4.1 Text and hierarchy paths: an exact rank from SQL

For each `String` or `HierarchyId` `min` or `max` partial, the engine appends one column to the
same statement:

```sql
SELECT [P1].[Id] AS [c0], MAX([P2].[Name]) AS [c1],
       DENSE_RANK() OVER (ORDER BY MAX([P2].[Name]) ASC) AS [c2]
FROM … GROUP BY [P1].[Id] HAVING … ORDER BY [c0] ASC OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY;
```

- **Combine.** C# keeps the highest rank for `max` and the lowest for `min`. One sort under the
  column's real collation produced the ranks, so the winner is SQL Server's.
- **Hierarchy paths.** The reader surfaces a `HierarchyId` as its path string (0012 §11.3), whose
  text order is not the hierarchy's: `/1/10/` sorts before `/1/2/` as text and after it in SQL
  Server. The rank carries SQL Server's order, as for text.
- **Ties.** Values the collation calls equal share a rank ('Sales' and 'SALES' under `_CI_`). C#
  keeps the ordinally smaller text, so the output never depends on row order. SQL's own `MAX`
  returns either one.
- **Scope (verified).** SQL ranks after `HAVING` and before `ORDER BY` and paging, over every
  surviving group. Ranks are relative, so the superset is harmless. They compare only within one
  execution: two statements (an export's pages, §6) agree when the data did not change between
  them.
- **Cost.** One extra sort of the statement's rows per ranked partial, only when such a measure
  exists. Pivot leaf rows are capped at 250,000.
- **Nulls.** A null partial ranks first and is skipped by the combine.

### 4.2 The approximation, measured

The alternative is a .NET comparer: `CompareInfo` with flags from the collation name (`_CI` →
`IgnoreCase`, `_AI` → `IgnoreNonSpace`, no `_KS` → `IgnoreKanaType`, no `_WS` → `IgnoreWidth`),
trailing spaces trimmed (SQL pads), ordinal for `_BIN` and `_BIN2`.

**Measured (verified):** 147 adversarial strings (hyphens, apostrophes, symbols, ligatures, Arabic,
kana, business names), all 10,731 pairs compared by SQL Server and by .NET.

| Collation | Pairs that differ | With word-sort emulation |
|---|---|---|
| `Latin1_General_100_CI_AS` | 268 (2.5%) | 181 (1.7%) |
| `Latin1_General_100_CS_AS`, `_CI_AI` | 268 | 179, 177 |
| `SQL_Latin1_General_CP1_CI_AS`, `nvarchar` | 217 (2.0%) | 130 (1.2%) |
| `SQL_Latin1_General_CP1_CI_AS`, `varchar` | 260 | 369: word sort does not apply |
| `Arabic_100_CI_AS`, culture `ar-SA` / invariant | 2,026 (19%) / 268 | — / 179 |
| `Latin1_General_100_BIN2` | 0 | 0 |

*Word-sort emulation* compares first with `-` and `'` removed, then puts the string with more of
them after. Windows collations ignore hyphens and apostrophes at the first level.

- **What still differs:** hyphens and apostrophes without emulation (`Al-Futtaim` vs `AlFuttaim`,
  `CC-001` vs `CC001`); symbol against symbol (`_` vs `.`); Arabic hamza carriers (`أحمد` vs
  `احمد`, where Windows treats the hamza as a mark and ICU as its own letter; under `ar-SA`, ICU also
  sorts Arabic before Latin); expansions SQL calls equal (`Æ`/`AE`, `ß`/`ss`); rare characters.
- **Why ICU differs:** by design, it sorts "bill's" before "bills" and keeps ligatures distinct
  ([.NET globalization and ICU](https://learn.microsoft.com/dotnet/core/extensions/globalization-icu)).
  .NET uses ICU on Windows and Linux alike, so at least the comparer is the same on both.
- **Plumbing it would need (verified):** SqlClient does not report a column's collation.
  `GetSqlString` returned LCID 1033 with case-insensitive flags for every collation tried, `_BIN2`
  and Arabic included, so the name would come from `DATABASEPROPERTYEX`.

### 4.3 GUIDs

- **Nothing to roll up.** 0008 §7.1 makes `Guid` Equatable but not Ordered: `min(ExternalId)` is
  QX3201 today. Decision 1's "GUIDs stay" has no case to cover.
- **If `Guid` ever becomes Ordered,** `SqlGuid.CompareTo` reproduces SQL Server's order exactly
  **(verified):** 332 GUIDs, with one-byte probes in each of the 16 positions, sorted identically.
  `System.Guid` sorts differently ([Comparing GUID and uniqueidentifier values](https://learn.microsoft.com/dotnet/framework/data/adonet/sql/comparing-guid-and-uniqueidentifier-values)).

### 4.4 What needs no comparer

**Member order** stays by display text in the viewer's culture (0033 §9.3, 0034 §6.6), a
presentation choice. **Navigation labels** (`max(Customer.Name)`) are equal on every leaf row of one
member, so the cube builder takes one without comparing.

---

## 5. Numeric and null semantics

**The principle.** Every value is a `SqlDecimal` at the SQL type SQL Server would give that node.
`SqlDecimal` implements SQL Server's precision and scale rules, so each operation lands on the same
type and digits as the backend.

### 5.1 The SQL type of each node

SQL types are static per statement, so the engine fixes them once per run (`Prepare`, §7.2). A
partial's family comes from the plan, where it is exact: precision 10 alone cannot tell `int` from
`decimal(10,0)`, and SQL Server treats them differently (`int` 50,000 × 50,000 overflows,
`decimal(10,0)` gives `decimal(21,0)`). A decimal partial's precision and scale come from the reader
(new `QueryColumn.Precision`, §8.5), since SQL Server derives a computed argument's facets.

| Node | SQL type |
|---|---|
| `sum` partial | `decimal(38, s)`, the argument's scale; an integer argument is widened to `decimal(38, 0)` (0008 §10.2) |
| `count` partial | `bigint`, never `decimal(19, 0)` |
| `min`/`max` partial | a whole argument's own type (`tinyint` … `bigint`); a decimal's precision and scale from the reader |
| `avg` | `sum ÷ count`, which lands on `decimal(38, max(s, 6))`, SQL's `AVG` type |
| number literal | `decimal(digits, written scale)`, as the engine binds it (0008 §13.1) |
| numeric parameter | `new SqlDecimal(value)`, which equals SqlClient's inference **(verified)** |
| `me()` | the `UserId` slot's store type |
| `year` … `day`; `diffDays` … `diffMinutes`; `diffSeconds` | `int`; `decimal(26, 6)`; `bigint`, as the emitter types them |
| whole `+ - *` whole; unary `-`, `abs` | the wider whole type; overflow at that width, negating the minimum included |
| whole `/` whole | the dividend becomes `decimal(19, 0)`: the emitter's widening (0008 §9.1) |
| whole with decimal | the whole side becomes `decimal(3, 5, 10 or 19, 0)` |
| decimal with decimal | `SqlDecimal`'s operators |
| `if`, `coalesce` | two whole types: the wider one. Otherwise the union: scale `max(s)`, integer digits `max(p − s)`; past 38 digits the scale shrinks, with no floor **(verified)** |
| `abs`, `round` | the argument's type |
| `floor`, `ceiling` | `decimal(p, 0)` **(verified:** `(38,6)` gives `(38,0)`), or the same whole type |

**Example —** `if(c, sum(a) / sum(b), sum(a))` with `decimal(38,4)` partials: the branches are
`decimal(38,6)` and `decimal(38,4)`, so the union keeps 34 integer digits and 4 decimals. SQL
rounds the ratio to 4 places, and so does the evaluator.

**Scale collapse (verified).** Every `sum` is `decimal(38, s)`, and an integer `sum` is
`decimal(38, 0)`. Where `+`, `-`, `if` or `coalesce` combine a `decimal(38, s)` operand with one of
a larger scale, 38 digits leave no room for more, and SQL Server keeps scale s:
`sum(Qty) + sum(Amount)` and `coalesce(sum(Qty), sum(Amount))` are whole numbers, `(38,2) + (38,4)`
keeps 2 decimals, and `floor(x) + 0.5` is whole. Multiplication and division keep theirs. The
evaluator does the same, so identity 1 (§7.5) holds. To keep the decimals, scale the coarser
operand first: `sum(Qty) * 1.0000 + sum(Amount)` is `decimal(38, 4)`.

### 5.2 Rounding and digits (verified)

Same inputs and types in SQL Server and in `SqlDecimal`:

| Case | SQL Server | `SqlDecimal` |
|---|---|---|
| `decimal(38,4)` 2 / 3; −2 / 3 | 0.666666; −0.666666 `(38,6)`: division **truncates** | same |
| `decimal(19,0)` 5 / `bigint` 3 | 1.6666666666666666666 `(38,19)` | same |
| `decimal(1,0)` 1 / `decimal(38,4)` 3 | 33 decimals `(38,33)` | same |
| 0.0015 × 0.0005, both `(38,4)` | 0.000001 `(38,6)`: multiplication **rounds** | same |
| −0.0015 × 0.0005 | −0.000001 | **+0.000001**: sign lost |
| 1 − `decimal(38,7)` 0.1234565 | 0.8765435 `(38,7)` | same |
| `sum(a) − sum(b)`, both `(38,4)`; `(38,4)` + `bigint` | `(38,4)`: the scale holds at precision 39 | same |
| `AVG` of `int` 1, 2, 2, emitted as `AVG(CAST(x AS decimal(19,0)))` | 1.666666 `(38,6)`: `AVG` truncates | `sum` `(38,0)` ÷ `count`: same |
| `ROUND(2.345, 2)`; `FLOOR(−2.345)`, both `(38,6)` | 2.350000, half away from zero; −3 `(38,0)` | same |
| `CASE` of `(38,8)` 1.23956789 and `(38,2)` | 1.24 `(38,2)` | `ConvertToPrecScale`: same |

- **The one defect.** `SqlDecimal` drops the sign of a negative product that rounds to one unit of
  the last place, a whole multiplier included (`−0.0000005 (38,7) × 1 (3,0)`). Rounding,
  `ConvertToPrecScale`, division and `Ceiling` keep the sign **(verified)**. The evaluator
  multiplies magnitudes and applies the sign itself.
- **Integer division never happens.** Two whole operands are widened first, as the emitter does.

### 5.3 Accumulation

- **Storage.** A run's partials live in a `PartialTable` (§7.2): one typed array per partial, one
  slot per cell (`Int128` mantissas for sums, `long` counts and ranks, the column's own type for
  `min`/`max`), so nothing boxes.
- **Inputs** take the partial's SQL type on arrival. The reader delivers `System.Decimal`, which
  carries the value's own scale (`1.5000m` is `(5,4)`, `0m` is `(1,0)`), so the table rescales it
  to the partial's (p, s) before any arithmetic.
- **Sums** add exactly at the partial's scale, as a 128-bit integer mantissa: SQL's own
  `decimal(38, s)` sum. The add is checked, and a result past 38 digits is QX6002 where SQL raises
  8115. The mantissa becomes a `SqlDecimal` once per cell. (Measured: a `SqlDecimal` add costs
  45 ns, a `decimal` add 11 ns; an `Int128` add is cheaper still.)
- **Counts** add as checked `long`, like `COUNT_BIG`.
- **`min`/`max`** compare numbers by value at any scale, dates by value, `DateTimeOffset` by instant
  and then offset (a deterministic tie), `Bool` with false < true, and text and hierarchy paths by
  rank (§4.1).
- **A bare measure** such as `sum(Amount)` skips `SqlDecimal`: the cell is the partial.

### 5.4 Nulls

0008 §9 applies unchanged:

- **Partials.** A `sum`, `min` or `max` partial is null when no contributing row has a value, as
  in SQL. A `count` is never null. `avg` is null when its count is 0.
- **Arithmetic** propagates null. **Comparisons are total** (0008 §9.2): `null = null` is true, an
  ordering against null is false. `and`, `or` and `not` stay two-valued.
- **`if` and `coalesce` are lazy,** as SQL's `CASE` is after aggregation, and **`and` and `or`
  stop** at the first operand that decides, left to right; 0008 §9.5 and §9.6 allow both. Guards
  then work in either form: `if(sum(b) = 0, null, sum(a) / sum(b))` and
  `if(sum(b) != 0 and sum(a) / sum(b) > 1, 'High', 'Low')`. So C# never fails on a group whose
  SQL row succeeded.

### 5.5 Errors

0008 §9.1 makes division by zero an execution-time error, and the evaluator follows it:

- **QX6001 division by zero** and **QX6002 arithmetic overflow** throw `QueryexEvaluationException`.
  Overflow is a result past 38 digits, a whole result past its type, or a sum past 38 digits: the
  cases where SQL Server raises 8134 or 8115.
- **The run fails,** as a SQL statement fails when one group divides by zero. The report engine
  answers `422 Report.MeasureFailed` with the measure's `pointer`, a `reason`, and the cell C#
  failed on: its row and column members, outermost first, or the axis total (§8.2). The renderer
  says where and how to guard the measure: "`Margin` divides by zero at Riyadh × Q3" (§8.3). An
  error SQL raised in a summary's rows (flag F5) names no cell; SQL does not say which group.
- **Session settings.** SQL Server raises 8134 and 8115 only under the backend's default
  `ANSI_WARNINGS` and `ARITHABORT`; with both off it returns null **(verified)**, and a summary
  would show blank rows beside a failing C# total. Keeping the defaults is already a host
  obligation (0008 §13.6), and SqlClient's defaults meet it.

### 5.6 38 digits against 28, and what still differs

- **Evaluation** runs in `SqlDecimal`: 38 digits, as SQL. **Inputs** arrive through spec 0012's
  `QueryRowSet` as `System.Decimal` (28–29 digits): a SQL value too large for it already fails in
  the reader, for every query (flag F5), and every value that arrives is exact.
- **Outputs.** Cube values travel as JSON strings (decision 18), all 38 digits. A summary's totals
  row has its column's SQL type, so it fits the `decimal` buffers whenever the rows do.
- **Two edge differences.** Overflow inside a pivot (`max(Qty) * max(Qty)` past the `int` range)
  fails only in C#, since SQL never evaluates a pivot measure; in a summary SQL fails alike.
  Legacy `datetime` ticks round to 100 ns in the reader; Tellma's own columns are `datetimeoffset`
  or `datetime2` (0012 §4).

---

## 6. Summary totals (decision 4)

**The rule.** A summary shows its totals row when all three hold:

1. the document says `totals: true`;
2. the request's `skip` is 0;
3. the statement returned every row of the result. When `top` is absent or larger than the page,
   the compiler asks SQL for `take + 1` rows; an extra row proves there is more and is dropped.
   When `top ≤ take`, nothing needs probing.

**What the row shows:** each measure evaluated over the combined partials of the returned rows,
after `having` and `top`, and "Total" in the first key column. **Example —** 0033 §3.5's top 50
customers total those 50 rows. Its `sum(Amount)` and `count()` are select items already, so their
partials add no column (§2.2 step 4).

**Otherwise** `ReportTable.Totals` is null and `ReportTable.TotalsOmitted` is true:

- **The renderer** shows no totals row; its footer adds "Totals show when all rows fit on one page.
  Export includes them."
- **Summaries page at 1,000 rows** (0034 §5.1), not 200, so every summary with `top` (at most
  1,000) fits. `tm-grid` windows its rows, so the larger page costs nothing on screen.

**Excel export always has totals** (flag F6). An export reads every row, in pages, and fails above
its cap rather than truncate (0019 §4.2). The writer adds each page's partials into one totals cell
and writes the bold "Total" row last, so the Total covers exactly the exported rows. A text or
hierarchy `min`/`max` total is exact when the data did not change between pages (§4.1).

**The partial columns** are asked for only when a Total can follow (`totals: true` and `skip` 0, or
an export), so a later page pays nothing for them. The report engine drops them, with the rank
columns, from `ReportTable.Rows`; `ColumnOrdinals` index what remains.

**The drill on Total.** Without `top`, it lists every group's filtered rows, those `having` hid
included (0033 flag 5). Under `top`, the Total row has no drill: whole groups are missing, so the
list would not add up to the Total.

---

## 7. The engine API: spec 0010 §F.14 and §F.15

Two items, numbered after decision 5's renumbering. Both are additive: with `Measures` empty, the
engine emits today's bytes.

### 7.1 §F.14 `QuerySpec.Measures` and partial columns

**Target:** 0008 §2.1, §2.4, §11.3, §13.4. **Requested by:** spec 0033 §8.4, §8.5; spec 0029 §9.
**Why:** a pivot rolls up, and a summary totals, in C#. That needs each measure's aggregate parts
as columns, and its outer expression in a form C# can run.

```csharp
public sealed record QuerySpec
{
    public string? Measures { get; init; }      // new; comma-separated measures on the Group axis; requires Aggregate
}

public sealed record QueryDiscoverySpec
{
    public string? Measures { get; init; }      // new; checked as CompileQuery checks it, QX4010 and QX4011 included
}

public sealed record CompiledQuery
{
    public CompiledMeasures? Measures { get; init; }   // new; null when QuerySpec.Measures is empty (§F.15)
}

public sealed record MeasurePartial(
    int Ordinal,                    // the result column: a reused select item, or an appended one
    MeasurePartialKind Kind,
    QueryexType Type,               // the aggregate's type; Numeric for sum and count
    int? RankOrdinal);              // a String or HierarchyId Min or Max: its DENSE_RANK column

public enum MeasurePartialKind { Sum, Count, Min, Max }
```

- **Binding.** Each item binds in Aggregate mode at location `Measures[i]`. QX4011 when it reads a
  path outside an aggregation or has none, in place of QX4007; QX4010 per §3.1.
- **Partials.** `avg(x[, c])` → `sum(x[, c])` and `count(x[, c])`. Structurally equal atoms are one
  partial, in first-use order. A partial structurally equal to a select item reuses its column.
- **Columns.** After the select items come the appended partials, then the rank columns
  (`DENSE_RANK() OVER (ORDER BY <partial> ASC)`). Grouping keys still come from the select list
  only (0008 §11.3).
- **Omitted parameters** (§F.8) fold inside partial conditions as anywhere else.
- **Caches.** L3 gains the measures text; L2 caches each item. **Caller error:** `Measures` without
  `Aggregate`.

**Example —** §2.4's statement. With `Select = "Center.Id, sum(Amount)"`, `sum(Amount)` reuses
`[c1]` and three partial columns are appended.

### 7.2 §F.15 Measure evaluation in C#

**Target:** 0008 §1.4, §9. **Requested by:** spec 0033 §8.4, §8.6. **Why:** one owner for the
numbers on both sides: the emitter writes SQL Server's arithmetic, the evaluator reproduces it.

```csharp
public sealed class CompiledMeasures                          // immutable and thread-safe; cached with the plan
{
    public IReadOnlyList<MeasurePartial> Partials { get; }   // deduplicated across all measures (§F.14)
    public IReadOnlyList<CompiledMeasure> Items { get; }     // parallel to the Measures items
    public MeasureEvaluator Prepare(MeasureContext context);  // once per run: fixes every partial's and node's SQL type (§5.1)
}

public sealed record CompiledMeasure(string Text, QueryexType Type, QueryexNullity Nullity);

public sealed record MeasureContext(
    IReadOnlyList<QueryexNumericType?> DecimalTypes,          // parallel to Partials: the reader's precision and scale for a decimal partial; null otherwise
    IReadOnlyDictionary<string, object?> Arguments,           // declared parameters by name, ignoring case; an omitted one is absent
    DateOnly Today, DateTimeOffset Now, object? UserId);      // the values the statement's context slots were bound with

public readonly record struct QueryexNumericType(byte Precision, byte Scale);

public sealed class MeasureEvaluator                          // one run's prepared measures; immutable once prepared
{
    public IReadOnlyList<QueryexNumericType?> ResultTypes { get; } // parallel to Items: a Numeric measure's SQL type; feeds ReportColumn.Scale
    public PartialTable CreateTable(int capacity);            // capacity: the expected cell count
    public MeasureValue Evaluate(PartialTable table, int cell, int measure);
}

public sealed class PartialTable                              // columnar: one typed array per partial, one slot per cell; not thread-safe
{
    public int AddCell();                                     // an empty cell: sums and min/max null, counts 0
    public void Add(int cell, int partial, Array buffer, int row, Array? ranks = null); // one row's non-null value from a reader buffer; ranks for a ranked partial
    public void Merge(int target, int source);                // §5.3's combine, partial by partial
}

public readonly struct MeasureValue                           // typed: no boxing
{
    public QueryexType Type { get; }
    public bool IsNull { get; }
    public SqlDecimal Number { get; }                         // Numeric, at the measure's ResultType
    public bool Boolean { get; }
    public DateOnly Date { get; }
    public DateTime DateTime { get; }
    public DateTimeOffset DateTimeOffset { get; }
    public string? Text { get; }                              // String, and a HierarchyId's path
}

public sealed class QueryexEvaluationException : Exception
{
    public string Code { get; }                               // QX6001 division by zero; QX6002 arithmetic overflow
}
```

- **Semantics** are §5's: types, rounding, nulls, laziness, errors.
- **`Add` and `Merge`** are §5.3: inputs rescaled to the partial's type, exact 38-digit sums,
  checked `long` counts, typed `min`/`max`, ranks for text and hierarchy paths. `buffer` is one of
  the reader's typed column arrays (0012 §11.3); the engine references no data library.
- **Lifetimes.** `CompiledMeasures` is cached with the plan. A `MeasureEvaluator` serves one run,
  because the decimal types come from that run's reader: one plan can meet two decimal types (a
  computed `sum(Amount * Rate)` whose scale differs by tenant). A `PartialTable` serves one cube,
  one summary totals row or one export.
- **No IR leaks.** §3.3's nodes stay internal; `MeasureEvaluator` is opaque.

### 7.3 §F.0 additions

- **Vocabulary.** *Partial*: a `sum`, `count`, `min` or `max` column a measure is computed from.
  *Combine*: merging two partials of one kind. *Rank column*: a text or hierarchy partial's
  `DENSE_RANK`.
- **Codes.** QX4010 and QX4011 are diagnostics (§3.1), at location `Measures[<i>]`. QX6001 and
  QX6002 are evaluation codes on `QueryexEvaluationException`, never diagnostics.
- **Interpreter and fixture.** The interpreter evaluates each measure directly over a group's rows
  and ranks under its collation table. `LedgerFixture` gains nullable numeric columns of every
  width (`tinyint` to `decimal(38,10)`) with values near each type's bounds, zero denominators,
  text rows that tie, and `hierarchyid` nodes whose paths sort differently as text.

### 7.4 Save-time validation

QX4010 and QX4011 come from `CompileQuery` and `DiscoverQuery`. 0033 §3.4 rule 5 already turns
engine codes into `ReportDefinition.InvalidExpression` (`code`, `start`, `length`); rule 6 adds
"pivot measures, and summary measures when `totals` is true, compile as `Measures` (spec 0010
§F.14)". The studio's draft sends them as `measures` to `queryex/discover`, so QX4010 shows on the
keystroke. No new report code.

### 7.5 Tests

**Unit, `Tellma.Core.Queryex.Tests`, PR:**

- **Corpus.** An entry per §3.1 row: QX4010 with span and `construct`; QX4011, not QX4007, for a
  stray path, and QX4011 for a constant measure; a hierarchy `max` ranked; identity `cast`s.
- **Goldens.** The `avg` split, deduplication, select-item reuse, conditional partials, folding
  inside a partial, rank columns.
- **Type vectors,** with value and (p, s), for every row of §5.1 and §5.2. Whole families: `int`
  5 + 3 is `int`, `decimal(10,0)` 5 + 3 is `(11,0)`; `int` 50,000 × 50,000 and `tinyint`
  200 + 100 are QX6002, `decimal(10,0)` 50,000 × 50,000 is `(21,0)`; `if` over `int` and `bigint`
  is `bigint`; negating and `abs` of `int`'s minimum are QX6002. Scale collapse: `(38,4)` 1.1234 +
  `(38,0)` 1 is 2 `(38,0)`; `(38,0)` 1 + `(38,10)` 0.5 is 2; `if` over `(38,0)` and `(38,4)`, and
  `coalesce` likewise, are `(38,0)`; `(38,2)` + `(38,4)` is `(38,2)`, `(38,6)` − `(38,4)` is
  `(38,4)`; `sum(Qty) * 1.0000 + sum(Amount)` is `(38,4)`. Literal scales: `(38,4)` × 0.5 is
  `(38,5)`, × 1.25 is `(38,6)`; with `(38,4)` a = 1 and b = 3, `(a / b) * 100` is 33.333300 and
  `100 * a / b` 33.333333. Dates: `diffDays` and `diffSeconds` across a leap day and a year end;
  `year` … `day` of `Date` partials.
- **The sign defect's exact scope:** negative products rounding to one unit, with a decimal and a
  `(3,0)` multiplier, against rounding, `ConvertToPrecScale`, division and `Ceiling`, which keep
  the sign.
- **Reader-shaped inputs.** Partials loaded as `1.5000m` and `0m` into `(38,4)` partials give
  `(38,6)` on division, not the `(15,10)` of their own types.
- **Laziness and short-circuit.** A guarded ratio over a zero denominator is null, unguarded it is
  QX6001; `sum(b) != 0 and sum(a) / sum(b) > 1` is false, not QX6001.
- **Accumulation.** `Add` and `Merge` per kind and type; text ties return the ordinally smallest
  value; the 38-digit sum boundary and two near-maximum partials are QX6002; shuffled rows give
  identical results.
- **Preparation per run.** One `CompiledMeasures` prepared with two decimal types for one partial
  gives two `ResultTypes`.

**Differential, `Tellma.Core.Queryex.IntegrationTests`, PR:** hand-written measures plus 500
generated ones (seeded, depth ≤ 4, §3.1's supported constructs). Each case compiles one statement
whose `Select` holds the measure (SQL's value) and whose `Measures` holds it too (the partials).
Three identities hold exactly, in value and scale:

1. **Leaf.** Per group, `Evaluate(partials of the row)` equals SQL's value in the same row.
2. **Rollup.** Combining the fine groups by a coarser key, then evaluating, equals SQL's value
   grouped by that key.
3. **Grand total.** Combining every row equals SQL's value with no `GROUP BY`.

A zero denominator raises SQL error 8134 and QX6001 alike. Like every Part F suite, it runs on
every pull request; none is nightly.

**Rank exactness, integration, PR:** under `Latin1_General_100_CI_AS`, `_CS_AS`, `_CI_AI`,
`SQL_Latin1_General_CP1_CI_AS` (`varchar` and `nvarchar`), `Arabic_100_CI_AS` and `_BIN2`, over
§4.2's 147 strings, and over `hierarchyid` paths such as `/1/2/` and `/1/10/`, the rank combine of
per-group `max` equals SQL's `MAX` over the union, up to collation-equal ties. The rank statement
keeps its shape when a group whose partial is null survives `having` (rank 1, skipped by the
combine).

---

## 8. Edit plan

**Renumbering, every citation:** §F.10 → §F.8, §F.11 → §F.9, §F.12 → §F.10, §F.13 → §F.11, §F.14 →
§F.12, §F.15 → §F.13; then the new §F.14 and §F.15 (§7). This plan replaces g1's item 1 plan. g1's
item 2 (`'uq'`, now §F.11) and item 3 (visual totals) plans stand, renumbered.

### 8.1 Spec 0010

- **Goals, Part F:** "…the seven amendments moved from spec 0012 (§F.1–§F.7), omitted parameters,
  constant statements, parameter binding, `'uq'` through a month table, `Exists`, `ComparedWith`,
  and measure partials with their C# evaluation (§F.8–§F.15)."
- **Other specs:** the 0012 §11.1–§11.2 bullet drops `TreeLevels`; delete "spec 0012 §11.4, spec
  0015 §5.2 — the refusal of level paths"; the 0013 bullet cites §F.11; add "spec 0012 §3.3, §11.3
  — `QueryColumn.Precision`, which §F.15 reads" and "spec 0029 §9 — `measures` on
  `queryex/discover`".
- **Part F intro:** "…eight more for optional parameters, defaults, drill-through, Hijri grouping,
  sharing, parameter pickers, and pivot and summary rollups (§F.8–§F.15)."
- **§F.0:** Order: "§F.8–§F.15 ship before spec 0033". Vocabulary: delete *Key group*, *Grouping
  set*, *Level indicator*, *Level navigation*; add §7.3's terms. Compatibility: delete the `Level2`
  bullet; add "an empty `Measures` emits today's SQL". Cache keys: L3 drops "key groups, grouping
  sets" and gains "measures"; "Level navigations and calendar maps add nothing" → "Calendar maps add
  nothing". Codes: delete QX5008; add QX4010, QX4011 and the evaluation codes; QX3104 per g1 item 2.
  Locations: add `Measures[<i>]`. Interpreter and fixture: delete "grouping sets with the
  indicator, level navigations" and `TreeLevels(4)`; add §7.3's lines.
- **§F.2:** "Requested by: spec 0012 §10.1."
- **§F.7:** Binding: "a foreign-key navigation" (drop "or a level navigation (§F.9)").
- **§F.8, §F.9:** delete.
- **§F.10–§F.15:** renumber, internal citations too. New §F.11: "legal in `GROUP BY` and
  `GROUPING_ID`" → "legal in `GROUP BY`"; its test "a grouping key and a `GROUPING_ID` over a
  `'uq'` key" → "a grouping key over a `'uq'` key". New §F.12: caller errors drop "is a level
  navigation".
- **New §F.14, §F.15:** §7.1 and §7.2.
- **Testing:** drop grouping sets, level joins, the indicator, "null keys under grouping sets",
  "levels at every depth", "the grouping limits", "§F.9's example"; "L3 splits on … grouping" →
  "… measures"; add §7.5.
- **Definition of done:** "§F.1–§F.4 and §F.6–§F.15"; the tool "accepts restrictions, measures,
  omitted parameters and constant items, and shows partial and rank columns"; Docs "name the `'uq'`
  month table, `Exists` and the measure evaluator"; Not in scope drops "declaring `TreeLevels`".
- **Decisions:** delete 6–9 and renumber; decision 3 drops "key groups". Add:
  - **Measures decompose into `sum`, `count`, `min` and `max` partials, evaluated in C# by the
    engine** — subtotals need the outer expression over rolled-up parts, and one owner keeps both
    arithmetics in step; a non-decomposable aggregate is refused by default (§F.14, §F.15).
  - **The evaluator runs `SqlDecimal` at each node's SQL type** — it reproduces SQL Server's value
    and scale; `System.Decimal` with six-digit agreement was the alternative (§F.15).
  - **Text and hierarchy partials combine by an SQL rank** — exact under every collation and in
    hierarchy order, with no collation plumbing; a .NET comparer disagreed on 1.7–2.5% of awkward
    pairs (§F.14).
- **Flags:** delete 5 and 6; flag 2 drops "(§F.8)"; flag 7 keeps only the `EvaluateConstant` name;
  flag 4 per g1 item 2; add F1–F3 of §9.

### 8.2 Spec 0033

- **Context, other specs:** "spec 0010 §F.8–§F.15 — omitted parameters, constant statements,
  `BindParameters`, `'uq'`, `Exists`, `ComparedWith`, measure partials and their evaluation"; the
  0012 bullet → "spec 0012 §3.3, §5.2, §11.1 — `QueryColumn.Precision`;
  `IDataBatch.EvaluateConstant`; the `'uq'` calendar map".
- **Goals:** "Four kinds, each one Queryex statement (§8). A pivot groups at leaf grain in SQL; the
  cube builder rolls up trees, subtotals and totals in C# with the engine's measure evaluator."
- **§1 vocabulary:** *Level* → "one depth of an axis's member tree"; add *Leaf row* ("one row of a
  pivot's statement") and *Partial* (spec 0010 §F.14).
- **§3.1:** delete `levels?`; the `totals` comment → "a totals row when the whole result fits on one
  page (§8.4)". **§3.4:** rule 6 adds §7.4's sentence; rule 12 deletes the `TooManyLevels` bullet.
  **§3.5:** the pivot example drops `"levels": 4`.
- **§8 intro:** "Each kind runs one Queryex statement." **§8.2:** "A summary's grand total therefore
  equals the sum of the list rows the same viewer sees." → "Every subtotal and total counts only
  rows the viewer may read." **§8.3 step 6:** "The batch holds the kind's statement, with
  `CountCap` when `IncludeCount`, and a pivot's member statements."
- **§8.4:** the labels bullet → "keeps one grouping key per dimension"; the totals bullet → §6's
  rule, with `Measures` and the `take + 1` probe when `skip` is 0.
- **§8.5 and §8.6:** replace with:
  - **§8.5 Pivot: the leaf statement.** One key per dimension (a tree groups by the fact's own
    node), label items for flat navigations, the measures as `Measures`, `having` as a plain SQL
    `HAVING`, ordered by the first key, `Take = MaxPivotRows + 1`; one member statement per tree
    dimension in the same batch (`Id`, `ParentId`, display columns, attribute paths relative to the
    tree entity, no row filter). This is g1's "The redesign, concretely", steps 1–2.
  - **§8.6 The rollup.** Chains (the deepest member or its phantom, ancestors, `Total`); each leaf
    row loaded into its leaf cell of a `PartialTable`, cells folded bottom-up along the row tree,
    then the column tree, and evaluated (§2.2 steps 7–8); g1 item 3's six points on `having` and
    visual totals; caps checked while building, cells × partials included; the cancellation token
    every 4,096 rows or cells, in reading, folding and evaluation alike; §2.4's example.
- **§8.7:** "Its operand rules are spec 0010 §F.11's." **§8.8:** "pivot statement rows" → "pivot
  leaf rows"; delete the two grouping rows; add "pivot cell partials | `MaxPivotCellPartials`
  5,000,000 (cells × partials: the cell cap with 10 partials) | 413 `Report.TooManyCells`" and
  "summary totals | the first page, when it holds every row (§8.4) | the row is omitted".
  **§8.10:** the plan cache holds the compiled measures.
- **§9.1:** the `Scale` comment adds "pivot measures: `MeasureEvaluator.ResultTypes`". **§9.2:**
  `Rows` holds the term columns only (partial and rank columns dropped); the `Totals` comment →
  "Summary with totals, when the page holds every row (§8.4)"; add `bool TotalsOmitted` ("totals
  asked for, but the result is paged").
- **§9.3:** delete `Truncated` and `CubeTruncation`; `CubeLevel` lists the levels present in the
  data; "Which members exist" → members with surviving leaf rows, the phantom per g1 item 3 point
  5; cell values are the measures evaluated over the cell's combined partials.
- **§9.4:** subtotal, total and summary-Total drills may include rows that `having` hid; under
  `top`, a summary's Total row has no drill (§6).
- **New error:** `422 Report.MeasureFailed`, arguments `pointer`, `reason` (`divide-by-zero` or
  `overflow`) and, when C# evaluated the cell, `row` and `column`: each axis's member captions in
  the request's content slot, outermost dimension first, empty for the axis total. Raised for
  `QueryexEvaluationException`; logged as `ReportEvents.MeasureFailed`.
- **§12.1:** a summary exports in pages of `MaxTake`, as a list does. **§12.2:** "A summary with
  `totals: true` ends with a bold "Total" row over every exported row: the writer adds each page's
  partials as it goes."
- **§14:** `ReportOptions` gains `MaxPivotCellPartials`.
- **§15:** cube units (chains, phantom on and off, (None), two trees, each combine rule, §2.4's
  composite measures, text ties, null partials, caps, shuffled rows); goldens for the leaf, member,
  partial and rank columns; runs where every cell equals a brute-force `descendantOf` query per
  measure; §6's rule at its boundary (exactly `take` rows, `take + 1`, `top = take`, `top < take`,
  `skip > 0`), export totals equal to screen totals whenever both exist, and a multi-page export's
  totals; the 422 from a summary row (8134) and from a pivot cell (QX6001) with the same `pointer`
  and `reason`, the pivot's with `row` and `column`; a load test at the caps (250,000 leaf rows, a
  6-deep tree × 3 column dimensions, cells × partials at `MaxPivotCellPartials`) under a memory
  ceiling; "equals the viewer's list rows" → "equals its rows".
- **Decisions:** decision 5 → "…labels read as `max(...)` — one grouping key per dimension".
  Rewrite decision 6: **Pivots group at leaf grain in SQL and roll up in C#** — 4–21× faster on the
  hard shapes at 10M facts (0.8–2.1 s), a constant ~100 MB memory grant, full depth, no language
  surface; grouping sets and level navigations were the alternative. Add **Pivot totals are
  visual** (g1 item 3) and **Summary totals come from C# over the returned rows, only when the page
  holds the whole result** (a SQL totals statement over the survivors needs a derived-table
  emission).
- **Flags:** delete flag 2; extend flag 5 per §9.4; add "Summary totals only on one page" (flips if
  users want totals on long summaries), "Every pivot cell from C#" (alternative: leaf cells from
  SQL; flips if the differential suite finds a class it cannot fix) and F6 of §9.

### 8.3 Spec 0034

- **§3.3:** the notice strip drops the truncated-levels example. New state row: "calculation error
  | `422 Report.MeasureFailed` | `circle-alert`, "A measure couldn't be calculated", "{label}
  divides by zero at {cell}. Guard it with if(…)." (`overflow`: "{label} is too large at {cell}.")
  | Edit, with `permissions.save`", where {cell} is "Riyadh × Q3" from `row` and `column` ("Total"
  for an empty one), or "in some rows" when the error names no cell.
- **§5.1:** a summary pages at 1,000 rows; a list stays at 200.
- **§5.3:** "`table.totals` is the last row when the page holds the whole result (spec 0033 §8.4).
  When `table.totalsOmitted`, the footer adds "Totals show when all rows fit on one page. Export
  includes them.""
- **§12.3:** the dimension editor drops "Levels (1–10, default `min(maxDepth, 6)`)".
- **§12.7 step 2:** the draft sends pivot measures, and summary measures when "Show totals row" is
  on, as `measures` (joined by ", ", spans mapped back by offset) instead of in `select`.
- **§12.8:** drop "levels" from the query changes. **Flags:** add F4 of §9.

### 8.4 Spec 0019

No edit. §4.4's `Total`, `Subtotal1`–`Subtotal3` and `Phantom` styles serve the full-depth cube as
they are (`Subtotal3` covers depth 3 and deeper), and the summary's Total row is the caller's
(0033 §12.2). Nothing in 0019 names levels or truncation.

### 8.5 Specs 0012, 0013, 0015, 0029, 0035 and ARCHITECTURE.md

- **0012:** line 71 drops "tree level navigations and"; §11.1 deletes the `TreeLevels` sentence of
  the Tree node bullet and "tree levels" from `Fingerprint`; §11.2 drops "and tree level navigations
  (§F.9)" and cites §F.9 and §F.11; line 828 and §11.3 cite §F.9; line 1777 cites §F.11; §11.4
  deletes the level-path sentence; the unit-test row drops "a `Select` through `Level{j}` refused at
  compose time"; the DoD drops "tree levels and". **Add:** §3.3 `QueryColumn` gains `int? Precision`
  ("a `decimal` result's precision; null otherwise"); §11.3 reads it from
  `DbColumn.NumericPrecision` beside the scale; spec 0016's converter does not write it.
- **0013:** §10.1 deletes the `Entity_Level` bullet; lines 85, 1400 and 1417 cite §F.11.
- **0015:** §5.2 "Two kinds of select item are refused" → "One kind…", and delete the level bullet;
  §7.3 deletes `QueryLevelNotSelectable`.
- **0029:** Context drops "§F.9" and "level navigations" and cites §F.13 for `ComparedWith`;
  vocabulary deletes *Level navigation*; §2.2 deletes `treeLevels`; §4.1 drops `level?` and "level
  navigations included"; §4.3 deletes the bullet; §6.2 deletes "Only it keeps a key through a level
  navigation…"; §7 drops "a level navigation included"; §9's `ComparedWith` comment cites §F.13;
  §11.2's hidden list and §17's row drop "level navigations". **Add:** §9 `QueryexDiscoverRequest`
  gains `string? Measures` (clause `measures`), passed to `DiscoverQuery` and `CompileQuery`;
  §10.1 `TmQxClause` gains `'measures'`.
- **0035:** line 468 cites §F.12.
- **ARCHITECTURE.md**, the report-definitions line: replace "Pivot subtotals and totals come from
  one `GROUPING SETS` statement; tree dimensions aggregate through to-one level navigations" with
  "A pivot groups at leaf grain in SQL; the cube builder rolls up trees, subtotals and totals in C#,
  evaluating each measure from its aggregate partials with the engine's evaluator".

### 8.6 Corrections to g1's plan

| g1 said | Now |
|---|---|
| pivot measures: one decomposable call; `MeasureNotDecomposable` | any measure in §3.1's subset; QX4010 refuses the rest |
| discovery item (A), `DiscoveryResult.Aggregation` | `QuerySpec.Measures` and the evaluator (§F.14, §F.15) |
| refuse `min`/`max` over `String` and `Guid` | text and hierarchy paths exact by rank; `Guid` was never Ordered |
| `avg` scale: "verify rounding" | verified: truncation at `decimal(38, max(s, 6))` |
| summary totals: "not now" | C# over the returned rows, one page only (decision 4) |
| 0012 and 0029: removals only | plus `QueryColumn.Precision` and discover `measures` |

---

## 9. Flags for Ahmad

- **F1. The subset** (§3). Alternative: more date functions over aggregates (`startOfMonth`,
  `addDays`, distances over `DateTime` and `DateTimeOffset`). Flips when a real report needs one;
  each is an additive evaluator node.
- **F2. Exact text by rank** (§4.1). Alternative: §4.2's .NET comparer with word-sort emulation and
  the collation name from `DATABASEPROPERTYEX`. Flips if the rank sort measures costly or a window
  function in emitted SQL is unwanted. This deviates from decision 1's "approximate".
- **F3. Evaluation errors fail the run** (§5.5), as 0008 §9.1 says. Alternative: a null cell with a
  warning. Flips if users prefer blanks to guarding measures with `if`.
- **F4. Summaries page at 1,000 rows** (§6). Alternative: keep 200 and show totals less often.
  Flips if 1,000-row pages measure slow on phones.
- **F5. Arithmetic errors in SQL rows** (outside this note). SQL errors 8134 and 8115 map to 500
  today (0012 §6.4), and a decimal too large for `System.Decimal` fails in the reader. Recommended:
  map both to `422 Report.MeasureFailed`, so a summary and a pivot fail alike.
- **F6. Exports always total** (§6). Decision 4 limits totals to a result that returns without
  paging; this note reads that as the screen's rule, since an export reads every row anyway.
  Alternative: the screen's rule for exports too (a Total only when the first page held every row).
  Flips if Ahmad meant decision 4 for exports as well.

---

## Sources

- [Globalization and ICU](https://learn.microsoft.com/dotnet/core/extensions/globalization-icu) ·
  [SqlDecimal](https://learn.microsoft.com/dotnet/api/system.data.sqltypes.sqldecimal) (38 digits;
  `System.Decimal` keeps at most 28 decimals) ·
  [Comparing GUID and uniqueidentifier values](https://learn.microsoft.com/dotnet/framework/data/adonet/sql/comparing-guid-and-uniqueidentifier-values) ·
  [Windows collations and word sort](https://learn.microsoft.com/archive/blogs/chadboyd/sorting-and-comparing-unicode-and-binary-data-why-do-i-get-a-weird-sort-order) ·
  [dotnet/runtime #102579](https://github.com/dotnet/runtime/issues/102579) (`StringSort` inert under ICU).
- Local measurements: SQL Server 2019 LocalDB 15.0.4382 and .NET 10.0.401 (ICU), 26 October 2026;
  the scripts are in the session scratchpad.
