# Critique: design-measures.md (C# measure evaluator)

- **Of:** `.tmp/round-2/r3/design-measures.md`, 26 October 2026. **Against:** `decisions.md` items 1,
  3, 4; spec 0008 §7, §9, §10, §11.3, §13; spec 0033 §3, §8, §9, §12; spec 0012 §3.3, §11.3; the
  emitter (`Emit/PlanEmitter.cs`, `Emit/SqlTypes.cs`, `Lowering/StoreTypes.cs`, `Lexing/Lexer.cs`).
- **Method.** Every numeric claim marked (verified) in the design was re-measured here, side by
  side, on SQL Server 2019 LocalDB 15.0.4382 and .NET 10.0.401 `System.Data.SqlTypes.SqlDecimal`,
  plus 60 further vectors chosen to break it (precision-39 additions, unions needing more than 38
  digits, the sign defect through `ROUND`/`CASE`/division, whole-type overflow, parameter
  inference, a `DENSE_RANK` statement in the exact emitted shape, `ARITHABORT OFF`). Vector ids
  (A1 … Q1) below refer to that run; the program is in the session scratchpad (`sqlcheck/`).
- **Verdict.** The numeric core is sound: `SqlDecimal` reproduced SQL Server's value *and* type in
  every vector except the multiplication sign defect the design already names. No blocker. Five
  majors: two are wrong-or-inconsistent numbers a user can see (whole-type family ambiguity;
  `HierarchyId` partials), two are inconsistencies between what SQL and C# show side by side
  (`and`/`or` evaluation making C# totals fail where SQL rows succeed; the Total drill contradicting
  the Total cell under `top`), and one is a memory/CPU trap at the published caps.

Severity: **blocker** = ships wrong numbers or cannot be implemented as written; **major** = a
user-visible wrong or inconsistent value, or a failure mode at the published limits; **minor** =
gap, hazard or wording that the implementation should close.

---

## 1. (major, wrong totals) Whole-number partials cannot be typed from precision and scale alone

**Problem.** §5.1 types every partial from `QueryColumn.Precision`/`Scale` read off the reader, and
§8.5 defines `Precision` as "3, 5, 10 or 19 for the integer families, the decimal's precision".
That makes `int` indistinguishable from `decimal(10,0)`, `bigint` (every `count` partial) from
`decimal(19,0)`, and `tinyint` from `decimal(3,0)`. SQL Server does not treat them alike, and the
design's own rule ("whole `+ - *` whole → the wider whole type; overflow at that width") needs the
family to apply.

**Evidence (measured).**

| Vector | SQL Server | Note |
|---|---|---|
| O1 `int 5 + int 3` | `8` **int** (10,0) | O3 `(10,0) 5 + (10,0) 3` → `8` **decimal (11,0)** |
| O4 `int 50000 * int 50000` | **error 8115** (int overflow) | O5 `(10,0) * (10,0)` → `2500000000` (21,0) |
| O8 `tinyint 200 + tinyint 100` | **error 8115** (tinyint) | `min`/`max` keep the column's whole type (emitter: `WholeWideningOf` returns null) |
| O12 `CASE int \| bigint` | `bigint` | the union of whole types is the wider whole, not a decimal |
| P2 reader schema | `int` → `DataTypeName="int"`, `NumericPrecision=10`, **`NumericScale=255`** | the family is available; the scale for `int` is a sentinel the reader must not copy |

Consequences a user sees: `max(Qty) * max(Qty)` over an `int` column overflows in a summary's SQL
rows (8115 → F5's 422) but the C# total, typed `decimal(10,0)`, happily shows 2,500,000,000; the
reverse happens for a pivot, where SQL never evaluates the measure. `max(a) + max(b)` over
`tinyint`/`smallint` columns overflows in SQL at 255/32,767 and would not in C#. These are edge
cases, but they are exactly the class identity 1 (§7.5) promises never happens.

**Fix.** Carry the family, not only the digits. Either (a) `QueryColumn` gains
`QueryexStoreFamily? StoreFamily` (from `DbColumn.DataTypeName`; spec 0012 §11.3 already reads the
column schema) and `MeasureContext.PartialTypes` becomes `(family, precision, scale)`; or (b) the
engine types whole partials at compile time from the plan (`PlanAggregate.Argument.StoreType` is
exact for a whole column: `SqlTypes.IsWhole`), and reads precision/scale from the reader only for
decimals, where the static store type can be `Decimal` with no facets (`SqlTypes.ArithmeticOf`).
(b) is smaller and also makes `MeasureProgram.ResultType` computable at save time for the common
case. Either way, a `count` partial is `bigint`, never `decimal(19,0)`, and the §5.1 table should
say so explicitly. Add O1/O3/O4/O5/O8/O12 to the unit vectors.

## 2. (major, wrong totals) `HierarchyId` is Ordered, so `max(Center.Node)` is a legal partial the combine cannot do

**Problem.** `QueryexTypeFacts.IsOrdered` excludes only `Geography` and `Guid`, so `min`/`max` over
a `HierarchyId` path binds (0008 §7.1). §5.3 lists how every type combines (numbers, dates,
`DateTimeOffset`, `Bool`, text by rank) and omits `HierarchyId`. Spec 0012 §11.3 surfaces a
`hierarchyid` as its path string (`/1/10/`), and ordinal string order is not hierarchyid order:
`/1/10/` < `/1/2/` as text, `/1/2/` < `/1/10/` in SQL Server.

**Fix.** Rank it exactly as text (§4.1 works for any orderable column: `DENSE_RANK() OVER (ORDER BY
MAX([T].[Node]))`), or refuse it with QX4010 (`construct: hierarchy`). The same choice applies to
`DateTimeOffset` if §5.3's "instant then offset" tie-break is ever questioned; ranking is the
general exact mechanism and costs one sort per partial. State in §5.3 which types rank and which
compare in C#, and add a corpus entry.

## 3. (major, inconsistency) `and`/`or` evaluate every operand, so C# fails a guard that SQL rows pass

**Problem.** §5.4: "`and`, `or` and `not` stay two-valued and evaluate every operand; 0008 §9.5
promises no short-circuit." 0008 §9.5 actually says the engine *is not required* to short-circuit;
it does not forbid it. A measure like `if(sum(b) != 0 and sum(a) / sum(b) > 1, 'High', 'Low')` is
the natural way to write a guard. In a summary, SQL Server evaluates the rows and in practice skips
the division when the left conjunct is false; the C# totals row then raises QX6001 and the whole
report answers `422 Report.MeasureFailed`: the user sees rows that computed fine and a report that
refuses to render. In a pivot the report never renders at all.

**Fix.** The evaluator short-circuits `and`/`or` left to right and `if`/`coalesce` already lazily
(§5.4). Both are permitted by 0008 §9.5 and §9.6 ("may evaluate any subexpression zero or more
times"). C# then never fails where SQL would have succeeded; where SQL fails (8134 in a summary
row) the run fails anyway through F5. Also: `Report.MeasureFailed` carries only `pointer` and
`reason`; for a pivot with 100,000 cells the author cannot find the offending cell. Add the cell's
coordinates (the row and column member keys, or "Total") to the error's arguments so the studio can
say "`Margin` divides by zero at Riyadh × Q3".

## 4. (major, inconsistency) The summary Total drill contradicts the Total cell under `top`

**Problem.** §6: the totals row covers "the rows `top` kept", but "the drill on Total lists every
group's filtered rows, those `top` or `having` hid included". A top-50 customers summary shows
`Total = 1,000,000` and its drill opens a list whose amounts sum to 1,500,000. 0033 flag 5 accepts
this for `having` (facts inside a surviving group), where the gap is subtle; under `top` whole
groups are missing and the gap is the headline number.

**Fix.** For `top`, the Total drill filter conjoins the returned rows' grouping keys:
`Invoice.CustomerId in (1012, 1044, …)` for a navigation or value key, the `=` forms of 0033 §9.4 for
text and expression members. The list is at most `top` (≤ 1,000) keys; at ~8 bytes per id that is
under the URL budget spec 0030 allows, and the cube's own drill already builds the same conditions
per member. Keep the `having` caveat (flag 5) and say both things in §9.4 and the footer.

## 5. (major, performance/memory) Per-leaf fan-out and per-cell partials at the published caps

**Problem.** §2.2 step 7 "adds each leaf row's partials into every cell above it". With a 6-deep
tree plus two flat row dimensions and three column dimensions, a leaf row has up to 9 × 4 = 36
ancestor cells; at `MaxPivotRows` 250,000 and 20 measures (`MaxPivotCells` allows 500,000 cells)
that is 250,000 × 36 × ~30 partials ≈ 270 M combines. Worse is the residency: every cell holds its
partials until evaluation. A `PartialValue` as sketched (`object?` value + `Int128` mantissa +
`long` count + rank) is ~40 bytes; 500,000 cells × 30 partials ≈ 600 MB per run, with 8 concurrent
runs per user allowed (0033 §8.8). `PartialValue.From(kind, object?, rank)` and
`Evaluate(…) → object?` also box once per row per partial and once per cell per measure (≈ 20 M
allocations at the cap).

**Fix.**
- **Roll up bottom-up, not per leaf.** Accumulate each leaf row into its leaf cell only; then
  combine cells into their parents in post-order over the row-member tree and the column-member
  tree (every combine is associative and commutative, which §7.5's "shuffled rows" test already
  relies on). Cost becomes O(cells × partials) and a child's partials can be released once folded.
- **Columnar, typed storage.** One `Int128[]` per `sum` partial, `long[]` per `count`, a typed
  array per `min`/`max` (`decimal[]`, `DateOnly[]`, `int[]` ranks), indexed by cell; no `object?`.
  Expose typed `From`/`Combine` overloads (or an accumulator bound at `Prepare`) instead of the
  boxing ones in §7.2.
- **Budget it.** Count `leafRows × partials` (and `cells × partials`) against a limit alongside
  `MaxPivotCells`, failing with `413 Report.TooManyCells`; the design's §8.2 "caps checked while
  building" should name this budget.
- **Say the CPU cost.** The design's own measurement (45 ns per `SqlDecimal` add) puts evaluation
  at roughly 0.2 µs per (cell, measure): ≈ 2–3 s of CPU at 500,000 cells × 20 measures, before
  JSON. Acceptable at the cap only if the cancellation token is honoured inside evaluation, not only
  "every few thousand rows" of reading.

## 6. (minor, wrong-looking totals) Scale collapses to 0 when a `decimal(38,0)` partial meets a decimal one

**Problem.** The emitter widens every integer-column `sum` to `decimal(38,0)` (`WholeWideningOf`),
and every `sum` is precision 38. SQL Server then drops *all* decimals whenever a (38,0) operand
meets a (38,s) one in `+`, `-`, `if` or `coalesce`, because 38 integer digits leave no room for a
scale. C# agrees exactly (so this is not a mismatch) but the design's §5.1 row "past 38 digits the
scale shrinks, with no floor" understates it, and a user sees money rounded to whole units.

**Evidence (measured, SQL = NET).** L6 `(38,4) 1.1234 + (38,0) 1` → `2 (38,0)`; L7
`(38,0) 1 + (38,10) 0.5` → `2 (38,0)`; L2 `CASE (38,0) 1 | (38,4) 2.5` → `3 (38,0)`; L3
`COALESCE((38,4) NULL, (38,0) 1)` → `1 (38,0)`; L10 `(38,2) + (38,4)` → `(38,2)`; L11
`(38,6) − (38,4)` → `(38,4)`. Multiplication and division are unaffected (L8 → (38,4), L9 → (38,6)).

**Fix.** Document the rule in §5.1 with these vectors ("`sum(IntColumn) + sum(Amount)` is a whole
number"; "`coalesce(sum(Qty), sum(Amount))` likewise"). The studio should show the measure's
`ResultType` scale next to the expression (it already has `MeasureProgram.ResultType`), and flag
scale 0 when an operand had a scale. Do not "fix" it in the evaluator: identity 1 must hold.

## 7. (minor, implementation hazard) `SqlDecimal(decimal)` is not the column's type

**Problem.** Partial values arrive as `System.Decimal` (§5.6). `new SqlDecimal(1.5000m)` is
`(5,4)`, `new SqlDecimal(0m)` is `(1,0)` (P1). Evaluating at those types gives different results
from SQL: `(5,4) / (5,4)` is `(15,10)` where `(38,4) / (38,4)` is `(38,6)` truncated. The design
says the right thing ("at each value's real SQL type") but the API offers nothing that enforces
it: `PartialValue.From(kind, object? value, …)` has no type, and `Combine` is static and typeless.

**Fix.** Bind the accumulator to `MeasureContext.PartialTypes` at `Prepare` and convert every
incoming value with `SqlDecimal.ConvertToPrecScale` to the partial's (p, s) before any arithmetic;
or better, read partial columns with `SqlDataReader.GetSqlDecimal` (P2: `(38,4)` straight from the
reader, and its `Data` words load an `Int128` mantissa directly). That also removes F5's
28-digit reader failure for partial columns. Add a unit vector whose inputs are `1.5000m` and
`0m` and whose expected output is `(38,6)`.

## 8. (minor, subset) Refusals that cost more than they save

- **Identity casts.** `cast(null, 'numeric')` is the idiom 0008 §7.1 prescribes for a typed null,
  and `cast(count(), 'numeric')` is a common habit; both are the diagonal of 0008 §10.8 and emit
  their argument unchanged. Refusing every `cast` with QX4010 makes them errors for no gain. Allow
  the diagonal and the `null`-literal casts (zero evaluator work: the node is its operand).
- **Date distance over `Date` aggregates.** "Days since last sale" (`diffDays(max(PostingDate),
  today())`) and "days active" (`diffDays(min(d), max(d))`) are ordinary ERP recency/aging measures,
  not rare ones (F1). Over `Date` operands the emitter's rule (count in hours, divide by 24, 0008
  §10.5) is `DayNumber` arithmetic, exact in C#. Support the four `diff` functions for `Date`
  operands now; keep `DateTime`/`DateTimeOffset` operands refused (DATEDIFF_BIG boundary counting in
  UTC is the part worth deferring). Same for `year(max(d))`/`month(max(d))` under `'gc'`: the
  calendar functions are refused for the `'uq'` month table, but the Gregorian ones over `Date` need
  no zone and no table.
- Everything else refused (`%`, `||`, text comparison, text functions) is the right call; none is a
  rollup measure in practice.

## 9. (minor, API) The totals row's partial and rank columns reach the client unless stripped

§7.1 appends partials and rank columns after the select items; 0033 §9.2's `ReportTable.Rows` is
the row set itself. Say that the report engine projects the row set to the term columns (or that
`ColumnOrdinals` is authoritative and the client ignores the rest), and that the appended columns
are requested only when the totals rule can apply (`totals: true`, `skip = 0`), so page 2 of a
summary does not pay for columns it will not use.

## 10. (minor, API) Summary export cap and paging

§6 says "an export is never paged"; 0033 §12.1 pages background list exports by `MaxTake` up to
`MaxExportRows`, and a summary without `top` can have a million groups. Define the summary export
as the same paged loop, accumulating partials across pages and writing the Total last, with
`MaxExportRows` as the cap; note that a text `min`/`max` rank is comparable only within one
execution, so across pages the text total is "a value seen", exact only when the data did not
change between pages (acceptable; say it).

## 11. (minor, session settings) `ARITHABORT OFF` turns SQL's division by zero into NULL

Measured (I1): with `ARITHABORT OFF; ANSI_WARNINGS OFF`, `1 / 0` returns NULL. A summary's SQL rows
would then show blanks while the C# totals throw QX6001. 0008 §13.6 already makes the defaults a
host obligation; §5.5 should cite it, and spec 0012's Read batch should assert (or `SET`) the four
settings once per connection, since this is the first feature where the two engines disagree
visibly when a host gets it wrong.

## 12. (minor, API) Typeless `PartialValue` and boxing results

Beyond finding 5: `PartialValue.Combine(kind, left, right)` has to compare `object?` values whose
runtime type it must rediscover per call (`decimal`, `DateOnly`, `DateTime`, `DateTimeOffset`,
`bool`, rank), and `MeasureProgram.Evaluate` returns a boxed `SqlDecimal` that the cube writer then
formats. Prefer an accumulator object created by `Prepare` (knows every partial's kind and type),
with `Add(row)`/`Merge(other)` and `Evaluate(measure, out SqlDecimal / out object?)` typed
overloads. Keep `MeasureProgram` immutable and thread-safe as stated; the accumulator is per run.

## 13. (minor, validation) Upgrade and the `totals: true` refusal

Stored summaries saved with `totals: true` and a measure outside §3's subset (`cast`, a date
function) will answer `400 query-invalid` at run after this ships (0033 §8.3 step 5). Add a
migration note (a one-time validation sweep that logs them), and consider the softer rule as a
flag: keep such a summary valid, show null in that measure's totals cell with the footer "not
totalled: {label}". That keeps every existing report rendering and still gives C# totals for the
measures it can do.

## 14. (minor, tests) Gaps in §7.5

Add, with expected values:

- **Precision-39 additions keep their scale** (A2 `(38,4) + (38,4)` → `(38,4)`; A6 with a `(1,0)`
  literal; A8 with a `bigint`): the most common shape, `sum(a) − sum(b)`, is absent from §5.2.
- **Scale collapse** (finding 6: L1, L2, L3, L6, L7, L10, L11).
- **Literal scale growth** (L14 `(38,4) × (1,1)` → `(38,5)`; L15 `× (3,2)` → `(38,6)`; L17 vs L18:
  `(a/b)*100` = `33.333300` but `100*a/b` = `33.333333`, both sides agree) so `ResultType` is pinned
  per literal shape.
- **The sign defect's exact scope.** K1–K7 show `Round`, `ConvertToPrecScale` (the union), division
  and `Ceiling` keep the sign; C2, C7, K8 show multiplication loses it, also with a `(3,0)` literal
  multiplier (`-0.0000005 (38,7) × 1 (3,0)`). Pin all of them so the magnitude-multiply workaround is
  not widened or narrowed later.
- **Whole-type family** (finding 1: O1, O3, O4, O5, O8, O12) and `int.MinValue` negation/`abs`.
- **Reader-shaped inputs** (finding 7: P1/P2).
- **Rank statement** with a NULL-only group surviving `having` (ranks 1, skipped), ties under
  `_CI_` (N1: SQL's `MAX` returned `SALES` for {`sales`, `SALES`}; C# must return the ordinal
  minimum deterministically), `top` smaller than the group count, `skip > 0` (totals omitted).
- **The totals rule boundary**: exactly `take` rows (totals), `take + 1` (omitted), `top = take`,
  `top < take`, `skip > 0`; export totals equal screen totals whenever both exist.
- **Error parity**: a zero denominator in a summary (SQL 8134 → F5 → 422) and in a pivot (QX6001 →
  422) produce the same `pointer`, `reason` and (finding 3) cell coordinates.
- **A load test at the caps** (250,000 leaf rows, 20 measures, a 6-deep tree × 3 column dimensions)
  with a memory ceiling, once finding 5's structure is chosen.
- **`Prepare` idempotence**: two runs of one `CompiledMeasure` with different reader types (a
  computed `sum(Amount * Rate)` whose scale differs by tenant) yield different `ResultType`s.

## 15. (minor, wording) Small corrections to the design text

- §1 answer 2 and §4: "`Guid` was never Ordered" is right; decision 1's "exact for GUIDs" can be
  closed as "no case exists" (the design says so in §4.3; make it the headline deviation note).
- §5.1 `floor`/`ceiling`: measured `(38,0)` from `(38,6)` in both engines (E1, E5); keep "p", not
  "p − s". E4 shows `floor(x) + 0.5` is then `(38,0)` = `3`, a surprise worth one example.
- §5.2 row 7 (`AVG` of `(10,0)`) is `AVG(decimal(19,0))` after the emitter's widening; say which
  type the vector exercises.
- §5.3: "`count`s add as `long`" — also bound them: a checked `long` add matches `COUNT_BIG`'s
  overflow at 2⁶³ (never reached, but `checked` costs nothing and keeps QX6002 honest). `Int128`
  holds 39 digits (Q1), so a 38-digit sum fits but two near-maximum partials overflow it; `checked`
  there too (M1 shows SQL raises 8115 at the same point).
- §7.1: say that QX4011 (a stray path) is reported instead of QX4007 for `Measures` items, so the
  two codes never both fire on one node.
- §4.1 "Scope (verified)": add that ranks are per statement execution (finding 10).

---

## What was verified and holds (no finding)

For the record, so these are not re-litigated: division truncates (B1–B13, including `2.0 / 3.0` →
`0.666666 (8,6)`); `(38,4) / bigint` and `AVG` agree in type and truncation for scales 0, 4, 7 and
10 (B9, B10, F1–F6); multiplication rounds (C1, C6, L12, L16); `CASE`/`COALESCE` unions round to
the union scale (D1, D3, D6, D7, K3, K4) and the union of a whole type with a decimal is the decimal
(D2, D5, L5); `ROUND` keeps the argument's type and rounds half away from zero at any digit count
(E2, E3, E7, E8, K1, K2); 38-digit overflow raises on both sides (M1, M2); SqlClient infers a
`decimal` parameter's `(p, s)` exactly as `new SqlDecimal(value)` does (H1: `0.5m` → (1,1),
`1234.5600m` → (8,4), `100m` → (3,0)); a `DENSE_RANK() OVER (ORDER BY MAX(Name COLLATE …))` column
in a `GROUP BY … HAVING … ORDER BY … OFFSET/FETCH` statement is legal and ranks after `HAVING` (N1).
The lexer types `0.0015` as `(4,4)` and `100` as `(3,0)`, matching SQL Server's literal typing (J4).
