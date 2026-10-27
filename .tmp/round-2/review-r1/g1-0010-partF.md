# Review r1, group 1: spec 0010 Part F (items 1-3)

Three items on Queryex and the pivot engine. They belong together: item 3 (visual totals) is what
keeps item 1 (leaf rows + C# rollup) simple. No spec was edited.

| # | Item | Call |
|---|---|---|
| 1 | Leaf rows from SQL, every rollup in C#; cut §F.8 and §F.9 | **Agree, with two changes**: pivot measures must be a single decomposable aggregation, and the engine must tell the compiler a measure's shape |
| 2 | `'uq'` emission per the benchmark | **Agree**: path-free operands take a prologue variable. Decide now; run the join-shape check before implementation |
| 3 | `having` on the finest cells in SQL, visual totals in C# | **Agree**. One follow-up: should a summary's totals row follow the same rule? |

---

## Item 1 — Leaf rows in SQL, every rollup in C#

### Current design

- **Goal (0033 Goals):** "Pivot subtotals and tree rollups come from one `GROUPING SETS` statement
  with no fan-out."
- **§F.8 (0010):** `QuerySpec.KeyGroups`, `QuerySpec.GroupingSets`, an appended `GROUPING_ID`
  column (`QueryexColumn.IsGroupingLevel`), code QX5008 (32 keys, 31 groups), and an engine rule
  that `Having` applies to the finest set only.
- **§F.9 (0010):** `EntityBuilder.TreeLevels(maxDepth)`; `Level{j}` resolves as a path segment
  "usable wherever a navigation is: every clause". One `LEFT JOIN` per level with
  `GetLevel()`/`GetAncestor()`.
- **0033 §8.5:** one key group per level (`[Center.Level{j}.Id]` per tree level), one grouping set
  per (row level, column level) pair, labels as `max(...)` per level, a truncation probe, and
  `MaxPivotRows + 1` rows.
- **0033 §8.6:** decodes rows by the "m, j" table (member, phantom, (None), redundant); `levels`
  (1-10, default `min(MaxDepth, 6)`) and `CubeTruncation` for deeper facts.
- **0033 decision 6:** "trees use level navigations" because a closure join counts each fact once
  per ancestor and "client aggregation is forbidden".
- **Spread:** 0012 §11.1, §11.4; 0015 §5.2; 0013 §10.1; 0029 §2.2, §4.1, §4.3, §6.2, §7, §11.2;
  0034 §3.3, §12.3; ARCHITECTURE.md (listed in the edit plan).

### Analysis

**Ahmad is right on speed and on simplicity.** From `.tmp/tree-rollup-analysis/benchmark.md`:

- §F.9 as specced takes 27-169 s at 1M facts. `hierarchyid` calls per fact row kill it.
- Grouping sets over facts take 3-20 s at 1M. Pre-aggregating first (Lpre) fixes that, but still
  takes 11-44 s at 10M on the hard shapes and hits the ~1.5 GB memory-grant cap.
- Leaf + C# takes 0.8-2.1 s end to end at 10M on the four hardest shapes (4-21x faster). Its
  memory grant stays at 80-105 MB everywhere. On easy shapes it ties.
- It needs no engine contract: a plain `GROUP BY`, which 0008 already emits.
- None of decision 6's objections applies: no query per level, no closure fan-out, and its ban is
  on *client* aggregation, while the cube builder is server code.

**What we give up, and one thing the benchmark missed.**

1. **Non-decomposable aggregates.** Queryex has none today: 0008 §10.2 lists only `count`, `sum`,
   `avg`, `min`, `max`. So nothing is lost now. Count distinct, median and percentile would need
   special handling later, as Ahmad says.
2. **Composite measures (missed by the benchmark).** Today a measure is "the expression ... a
   measure as written" (0033 §8.4), and the studio's item editor lets users type any aggregate
   expression. So `sum(Amount) / sum(Quantity)` (average price) or a margin percent is legal in a
   pivot. Under Leaf + C#, the parent's value is *not* the sum of the children's ratios. The outer
   expression must be re-evaluated from the rolled-up parts, in C#. That needs a production
   evaluator for the group-level part of Queryex, with SQL's decimal scale rules (0008 §13.6). The
   only evaluator today is the test-only reference interpreter (0008 §18.3).
   - Note: differences still work, because they can move inside: `sum(Amount - Cost)`.
3. **`min`/`max` over `String` and `Guid`.** Combining them in C# needs SQL Server's collation and
   `uniqueidentifier` ordering. Refuse them in pivot measures. Numbers, dates, instants and Bool are
   safe.
4. **"Ancestor at depth j" as a flat dimension.** `path: "Center.Level1"` (sales by region, also in
   a chart) dies with `Level{j}` (complexity.md, "Feature lost under C"). A pivot can still show
   the top level collapsed (`expand: false`). The C# rollup could later serve a chart dimension
   "rolled up to depth j" cheaply. Out of scope now.
5. **App-server work.** CPU and memory move from SQL to the web tier. Numbers below.
6. **Ancestor labels need their own read.** Level joins brought them free (0033 §8.6 "No ancestor
   query"). Now a member statement must supply ids, parents and labels.

**How the compiler sees a measure's shape.** To split `avg(x, c)` into `sum(x, c)` and
`count(x, c)`, and to refuse composites, the compiler needs the measure's outermost call and its
argument texts. The engine exposes no syntax tree. `Discover` gives function names and
`UsesAggregation`, which cannot tell `avg(x)` from `avg(x) * 2`. Two ways out:

- **(A, recommended)** A small additive Part F item: `DiscoveryResult.Aggregation` (function name,
  argument span, condition span) when the whole text is one aggregation call; null otherwise. It
  is syntactic, like `ComparedWith` (§F.15). It also gives the validator an exact error span.
- **(B)** A structured measure in the document (`{ aggregate: 'avg', expr: 'Amount', condition }`).
  No engine change, but it breaks decision 5 ("terms are column keys or expressions") and makes
  pivot measures differ from summary and chart measures.

### The redesign, concretely

**Part F keeps** §F.1-§F.7 and §F.10-§F.15 (renumbered §F.8-§F.13); §F.2 `level()` stays for
spec 0012 §10.1. **It cuts** all of §F.8 (`KeyGroups`, `GroupingSets`, `IsGroupingLevel`, QX5008,
the `Having` rule) and all of §F.9 (`TreeLevels`, `Level{j}`, the reserved `Level<n>` name).
**It adds** (A) above.

**Execution (0033 §8.5-§8.6 become one section, "Pivot: leaf rows and the cube").** One Read batch:

1. **The leaf statement.** A plain aggregate query, the same shape as a summary:
   - keys: one per dimension. A tree dimension groups by the fact's own node (`Center.Id`), never
     its ancestors. Text terms keep their slot keys.
   - measures: the partials of the table below.
   - labels: `max(...)` for flat navigation dimensions only. Tree labels come from step 2.
   - `having`: the document's, as a plain SQL `HAVING` (item 3).
   - `Take = MaxPivotRows + 1`, ordered by the first key (0008 QX4006 needs an order to page).
2. **One member statement per tree dimension**, in the same batch: `Id`, `ParentId`, the display
   columns and the attribute paths of the whole tree entity, with no row filter. These are read
   the way a navigation's columns are (spec 0015 §5.2's traversal rule), which is what level
   navigations exposed. Only ancestors-or-self of leaf rows reach the result.
   - Trees are master data (0008 §10.10 says so), so this is thousands of rows. The benchmark's
     largest tree (37K members) is the stress case.
   - Flip: if trees grow far larger, restrict by the leaf ids in a second round trip with spec 0012
     §11.3's `IncludeAncestors`.
3. **The cube builder** (C#, `src/core/Tellma.Core/Reports/CubeBuilder`, already in 0033 §1; no new
   project or package):
   1. Build each tree's parent map. Mark every node that has a leaf row on itself and every node
      with a leaf row below it.
   2. Give each leaf row its **chain** per axis: its deepest member, then each ancestor, then the
      axis `Total`. The deepest member is the node's **phantom** (its own facts) when the node also
      has data below it, else the node itself. A null key maps to "(None)". With several
      dimensions on an axis, members nest as 0033 §9.3 says, so the chain climbs the inner
      dimension, then the outer ones, then `Total`.
   3. Add the leaf row's partials into every (row chain member, column chain member) cell.
   4. Finish each cell (the table below). Sort members per 0033 §9.3, assign ids after sorting, and
      emit cells in (row id, column id) order. The output is byte-identical whatever order SQL
      returned the rows in, because decimal sums are exact.
   5. Check the caps while building (below). Observe the request's cancellation token every few
      thousand rows (rule 2C).

**How measures decompose** (a whitelist, so a future non-decomposable aggregate is refused by
default):

| Measure | Leaf columns from SQL | Combined in C# | Final value |
|---|---|---|---|
| `sum(x[, c])` | `sum(x, c)` | add the non-null partials | null when every partial is null (SQL's rule) |
| `count(...)` | `count(...)` | add | as is |
| `avg(x[, c])` | `sum(x, c)`, `count(x, c)` | add both | sum / count; null when the count is 0; scale `max(s, 6)` like SQL's `AVG` (verify rounding with a differential test) |
| `min`/`max(x[, c])` | `min`/`max(x, c)` | min/max of the non-null partials | as is; `Numeric`, `Date`, `DateTime`, `DateTimeOffset`, `Bool` only |
| anything else (composite, string min/max) | — | — | save error, e.g. `ReportDefinition.MeasureNotDecomposable` |

**What becomes of each piece:**

- **Totals.** Free: the `Total` member is just the end of every chain. `rowTotals` and
  `columnTotals` only decide whether it is emitted. They no longer shape SQL.
- **The phantom ("self twin").** A member, not a twin column. It exists only when a node has facts
  of its own *and* facts below it. Same look as today ("(direct)", 0034 §6.3; Excel `Phantom`
  style, 0019 §4.4). §8.6's m/j table, the redundant rows and the truncation probe all go.
- **Depth.** Full `MaxDepth`, always. `levels`, `CubeTruncation`, `ReportCube.Truncated`, the
  studio's Levels field and the "Center shows 4 of 6 levels" notice all go. `expand` stays.
  `CubeLevel` stays but lists the levels present in the data.
- **Kinds.** Summary and chart already run a plain `GROUP BY` (0033 §8.4); they do not change.
  Only pivots use the cube builder. Charts keep "tree dimensions are flat".
- **Labels and attributes.** Flat navigations: `max(...)` in the leaf statement, as today. Trees:
  from the member statement, with attribute paths made relative to the tree entity
  (`Center.Code` → `Code`).
- **Limits (0033 §8.8).**
  - `MaxPivotRows` (250,000) now caps leaf rows. Note: this is *looser* than today's cap on output
    rows, because leaf rows are never more than output rows (the benchmark's "at least as tight"
    is backwards). The cell cap still bounds the output.
  - `MaxPivotCells`, `MaxPivotRowMembers`, `MaxPivotColumnMembers` are checked during the rollup,
    so the run fails fast with 413.
  - The two grouping limits and `ReportDefinition.TooManyLevels` go. SQL Server's 32-expression
    limit applies only to `ROLLUP`, `CUBE` and `GROUPING SETS`; a plain `GROUP BY` is bounded by
    an 8,060-byte worktable row, far above 0033 §3.4's 5 + 3 dimensions.
  - Suggestion: count `MaxPivotCells` as cells x measures (values), default 1,000,000. Memory and
    the JSON payload both scale with values, not cells.
- **Excel export (0033 §12.3-§12.4).** Unchanged: it reads the cube. Trees are now full depth;
  `Subtotal3` already covers depth 3 and deeper.
- **Drill-through (0033 §9.4).** Unchanged conditions: `descendantOf(Center.Id, 11)` for a tree
  member, `CenterId = 10` for a phantom. See item 3 for one new mismatch.

**Memory and CPU (rough estimates, not measured beyond the benchmark):**

- **Leaf rows** sit in spec 0012's columnar `QueryRowSet` anyway: about 15 MB at 250,000 rows, 2
  measures.
- **Accumulators:** about 60 B per cell plus 24 B per measure. At the cap (500,000 cells) that is
  about 55 MB with 2 measures and about 270 MB with 20, hence the values cap. A typical pivot
  (2,000 leaf rows, a 4-level tree, quarters) needs under 2 MB.
- **Today** already holds up to 250,000 grouping-set rows and the same output cube, so the net
  cost is the accumulators: roughly +50 MB at the cap.
- **CPU** (benchmark, one thread, sum and count): 8 ms (D4-quarter), 78 ms (T3), 298 ms (W5 x
  quarter), 786 ms (B6). It grows with measure count; the `tellma-reports` policy (8 runs per user,
  spec 0016 §8.2) bounds it.
- **Caveats:** laptop, uniform data, only `sum` and `count`, no `having`, no label building in the
  rollup (the same cost in both designs). None is likely to flip the ranking.

**Tests (0033 §15).** Units: chains, phantom on/off, (None), two trees, each combine rule, null
partials, `avg` scale, caps, byte-identical output for shuffled rows. Integration: every cell of a
two-tree pivot equals a brute-force `descendantOf` query (the benchmark's validation). Keep the
nightly perf test.

### Recommendation

Agree. Leaf + C# is faster on every hard shape, removes two engine amendments (§F.8's members and
about nine level-navigation surfaces across 0012-0034, per complexity.md), and keeps `Level{j}` out
of user-typeable syntax for good. Add two things: pivot measures must be one decomposable
aggregation call (composite ratios wait, like distinct count), and a small discovery item (A) so
the compiler can split `avg` and refuse the rest.

### Edit plan (if approved)

**Spec 0010**
- Front matter: the Goals bullet on Part F drops grouping sets and level navigations and cites
  §F.8-§F.14; the "Other specs" bullet for 0012 §11.1 drops `TreeLevels`; the bullet "spec 0012
  §11.4, spec 0015 §5.2 — the refusal of level paths" goes.
- Part F intro: "eight more for pivots, tree rollups, ..." → six items, no rollups.
- §F.0: drop the four grouping/level vocabulary terms, the `Level2` compatibility bullet, the
  grouping and level cache-key mentions, QX5008, and the fixture's `TreeLevels(4)`.
- §F.2 drops requester 0033 §8.6; §F.7 drops "or a level navigation (§F.9)"; §F.14 drops the
  "level navigation" caller error; §F.13 drops `GROUPING_ID` (text and golden).
- §F.8, §F.9: delete. Renumber §F.10-§F.15 → §F.8-§F.13. Append (A) as §F.14, with corpus vectors.
- Testing, DoD: drop every grouping-set, level-join and indicator item and "spec 0012 declaring
  `TreeLevels`". Decisions 6-9 and flags 5, 6 go; flag 7 keeps only the `EvaluateConstant` name.

**Spec 0033**
- Context list and Goals: the new Part F numbers; the goal becomes "pivots group at leaf grain in
  SQL; the cube builder rolls up trees, subtotals and totals".
- §1 vocabulary: *Level* = a depth in the member tree; *Phantom child* stays.
- §3.1: delete `levels`. §3.5 example: drop `"levels": 4`.
- §3.4 rule 6: pivot measures follow the decomposition whitelist (new code); rule 12: drop
  `TooManyLevels`.
- §8 intro and §8.3 step 6: a pivot batch holds the leaf statement and one member statement per
  tree dimension.
- §8.4: the `max(...)` rationale becomes "one key per dimension" (no 32 limit to respect).
- §8.5 + §8.6 → one section "Pivot: leaf rows and the cube" (leaf statement, member statements,
  the chain algorithm, the decomposition table, the head-office example).
- §8.8: `MaxPivotRows` caps leaf rows; drop the two grouping rows; optionally count values.
- §9.3: delete `Truncated` and `CubeTruncation`; `CubeLevel` is data-dependent.
- §15: the tests above. Decision 6 rewritten (Leaf + C#, benchmark numbers, what it gives up).
  Decision 5's "one grouping expression per level" reworded.
- Flags: delete flag 2 (depth); add "pivot measures are single decomposable aggregations"
  (alternative: C# evaluation of composites; flips when users need ratios in pivots).

**Other specs**
- 0012: §11.1 drop the `TreeLevels` bullet text and "tree levels" from `Fingerprint`; §11.2 list;
  §11.4 drop the compose refusal; the unit-test row and the DoD/Goals mentions.
- 0015: §5.2 drop the level-path rule; §7.3 drop `QueryLevelNotSelectable`.
- 0013: §10.1 drop `Entity_Level`.
- 0029: vocabulary, Context line, §2.2 `treeLevels`, §4.1 `level?` and the `navigation()`
  comment, §4.3 bullet, §6.2 "keeps a key through a level navigation", §7 "a level navigation
  included", §11.2 hidden list, §17 test row.
- 0034: §3.3 drop the truncation notice; §12.3 drop the Levels field; §12.8 drop "levels" from
  the list of edits; the studio shows the new measure error like any validation error.
- Citations of §F.10-§F.15 to renumber: 0012 (§F.11, §F.13), 0013 (§F.13), 0029 (§F.15), 0033
  (all six), 0035 (§F.14). About 20 lines outside 0010, 36 inside.
- 0019, 0035: no edit (0019 §4.4's `Phantom` style stays; 0035 never touches pivot internals).
- ARCHITECTURE.md line on report definitions: misaligned; replace the grouping-sets and
  level-navigation clause with the leaf + C# sentence.

### Questions back

1. **Composite measures in pivots** (`sum(a) / sum(b)`): refuse for now (recommended), or build a
   C# evaluator for group-level expressions now? Summary and chart keep them either way.
2. **Measure shape:** (A) a discovery item in Part F (recommended), or (B) a structured measure in
   the document?

---

## Item 2 — `'uq'` emission forms

### Current design

- **§F.13 (0010):** two forms. A row-grain operand takes the lateral `OUTER APPLY` seek. "Any
  other operand is path-free ... or aggregated ... It takes the scalar form." Inside an
  aggregation argument or a grouping key, such an operand is **QX3104**.
- **§F.11:** "A `'uq'` call emits §F.13's scalar form" (constant statements).
- **Decision 13:** the scalar form runs `startOfMonth(today(), 'uq')` "with no prologue variable".
- **Flag 4:** alternative "the lateral form only"; flips "if the scalar form's plans measure
  poorly over large fact tables".
- **History:** Q-22 cut prologue variables because "path-free operands come only from F.11"
  (quality.md). Wrong: filters, search tabs and permission criteria hold them too. K48 restored
  them through the scalar form.

### Analysis

**The benchmark answers flag 4's own flip condition.** From `.tmp/uq-emission-bench/results.md`
(10M rows, server duration in ms):

| Index | Range | S (scalar, today) | V (prologue variable) | G (Gregorian baseline) |
|---|---|---|---|---|
| covering | MTD / YTD | 59 / 425 | 66 / 197 | 42 / 169 |
| non-covering | MTD / YTD | **405** / **4,303** | 661 / 630 | 626 / 836 |
| none | MTD / YTD | 852 / 941 | 516 / 597 | 382 / 593 |

- **V tracks the Gregorian baseline** (within 0.75-1.6x in every cell). That is the right bar:
  a Hijri filter should cost what a Gregorian one costs.
- **S is erratic**: from 0.65x of G (non-covering MTD, a lucky plan) to 5.1x (non-covering YTD);
  2.5x on covering YTD; 1.6-2.2x with no index. A row-mode nested-loops join pulls the date
  predicate out of the batch-mode scan.

**It fits the existing design.**
- 0008 §12.4 already hoists row-invariant lookups (the hierarchy node lookups) into prologue
  variables. A path-free `'uq'` lookup is the same kind of thing, so this adds one entry to §12.4's
  list, not a mechanism. §F.3's `Prologue` already carries them, and hosts already write it first.
- §F.11's constant statement keeps the scalar form (no row source to scan; nothing hoists).
- Aggregated operands (`month(max(PostingDate), 'uq')`) keep the scalar form (measured: +0-21%).

**A bonus: QX3104 probably disappears.**
- SQL Server refuses a *subquery* in `GROUP BY` (error 144) and inside an aggregate (error 130). It
  does not refuse a variable (Microsoft's GROUP BY page lists the forbidden elements; variables are
  not among them).
- With V, `sum(Amount, Invoice.PostingDate >= startOfMonth(today(), 'uq'))` becomes legal: "sales
  this Hijri month" as a conditional measure.
- An aggregated operand can never sit inside an aggregation (nesting is already QX4003) or in a
  grouping key. So no case is left for QX3104. Confirm with two goldens before deleting the code.

**Costs.**
- Three emission forms instead of two.
- The variable's estimate is blind. 0008 §12.4 says so itself, and the benchmark saw it once
  (covering MTD: same duration, 2x CPU). `OPTION (RECOMPILE)` fixes it but compiles every run; do
  not use it by default.

**Decide now, or extend the benchmark?** Decide now. Run the join-shape extension as a check before
Part F is implemented, not as a gate for the spec edit.
- The untested shape (lines reaching `PostingDate` through the invoice header) moves the date
  predicate to the header table. That changes how badly S does; it does not change that V behaves
  like a bound parameter (V within 25% of P, the bound-parameter variant, in every measured cell;
  the noise floor is about 20%).
- The residual risk is V's blind estimate picking a poor join order. The check: V, P and S on the
  join shape, with a covering and a non-covering header date index. The harness exists; adding an
  invoices table takes an hour or two. Flip if V runs more than about 1.5x slower than P.
- The choice is emission-internal: reverting changes goldens, not contracts.

### Recommendation

Agree. Emit path-free `'uq'` operands as hoisted prologue variables (0008 §12.4's mechanism), keep
the lateral seek for row-grain operands and the scalar form for aggregated ones and for constant
statements. It makes Hijri filters cost what Gregorian ones do and likely removes QX3104.

### Edit plan (if approved)

- 0010 §F.13 (new §F.11): target adds 0008 §12.4; "Two emission forms" → three (row-grain:
  lateral; path-free: a hoisted variable `@qx{b}_v{n}` per distinct operand per statement;
  aggregated: scalar); delete the QX3104 bullet; tests gain goldens for a variable in `WHERE`, in
  an aggregation's condition and in a grouping key, and lose the QX3104 vectors.
- 0010 §F.11 (new §F.9): keep "the scalar form"; add "(a constant statement hoists nothing)".
- 0010 §F.0 codes table: drop QX3104 (after the goldens confirm).
- 0010 decision 13: rewrite for three forms, citing the measured cost (S up to 5x the Gregorian
  baseline; V within 1.6x).
- 0010 flag 4: alternative "the scalar form for path-free operands"; flips if the join-shape check
  shows V's blind estimate costing more than about 1.5x against a bound parameter.
- 0033 §8.7: "Its operand rules, QX3104 included, are spec 0010 §F.13's" → drop "QX3104
  included" and use the new number.

---

## Item 3 — `having` on the finest cells, visual totals

### Current design

- **§F.8 (0010):** "`Having` applies only to the rows in which every group is present ... Subtotals
  and totals include every row the `WHERE` admitted. A visible subtotal can therefore exceed the
  sum of its visible children."
- **0033 §8.5 rule 5:** having "filters only the finest set's rows ... so a collapsed member never
  shows an empty total".
- **0010 flag 6** flips "when ... report users expect totals to drop with filtered leaves".
- **0033 §8.4 (summary):** totals use "no grouping keys, no `having`, no `top`".
- **0033 §9.4, flag 5:** drill ignores measure conditions and `having`.

### Analysis

**Ahmad is right, and it is what makes item 1 simple.** With visual totals, SQL applies `having`
as a plain `HAVING` on the leaf rows, and failing rows never leave SQL. With today's rule, Leaf +
C# would need every leaf row back plus a pass/fail column, and the cube would show subtotals that
exceed their visible children.

**Precedent.** Power BI's matrix counts only the combinations that pass a measure filter in its
totals (SQLBI). Excel PivotTables can total with or without filtered items. For Tableau,
third-party sources disagree on whether measure filters change grand totals, and Tableau's own
order-of-operations page is silent, so I would not cite Tableau.

**The definition, precisely.**

1. **The finest cell** is one leaf row: one key per row and column dimension, where a tree
   dimension's key is the fact's own node. This matches today's "finest set", minus the depth cap.
2. **`having` runs in SQL on leaf rows.** A failing leaf row is gone. It counts nowhere.
3. **Every other number is the measure recomputed over the surviving leaf rows in its scope**:
   what you would get by re-running it over exactly the facts behind the visible leaf cells. So
   `sum` and `count` add up what is shown below; `min` and `max` take its min or max; `avg` is
   weighted (total sum / total count), never an average of the shown averages.
4. **Members.** A member exists only if at least one surviving leaf row sits under it. An ancestor
   whose whole subtree failed disappears, so a collapsed member still never shows an empty total.
5. **The phantom (self twin)** is decided on surviving rows. A node gets a phantom only if its own
   row survived and something below it survived. If its own row fails, it has no phantom and its
   subtotal is the sum of its surviving children.
6. **Row and column totals always reconcile.** The row totals, the column totals and the grand
   total all come from one set of surviving rows.

**Example** (0033 §8.6's numbers), `having: "sum(Amount) >= 5000"`. Head office holds 2,000 of its
own; Riyadh 30,000; Jeddah 20,000.

| Row | Today's rule | Visual |
|---|---|---|
| Head office | 52,000 | 50,000 |
| — Head office (direct) | hidden | gone (no phantom) |
| — Riyadh / Jeddah | 30,000 / 20,000 | 30,000 / 20,000 |
| Total | 52,000 | 50,000 |

**With columns, cells can drop inside a row.** Customers x quarters with `sum(Amount) > 1000`: a
customer's weak quarter blanks out, and the row total is the sum of the other quarters. That is
the finest-cell rule Ahmad chose. Power BI's matrix instead filters at the row grain (a whole
customer stays or goes). Row grain would need a second grouped statement per filter, so the
finest cell is the simpler choice. Just state it in the spec.

**Cascades.**
- **Drill-through (0033 §9.4).** A leaf cell drill is exact (it passed `having`). A *subtotal*
  drill (`descendantOf(Center.Id, 10)`) now also lists facts from hidden cells, so it can total more
  than the number clicked. Accept it: `having` is group-level and cannot become a row filter.
  Extend flag 5 to say so.
- **Summary totals (0033 §8.4)** ignore `having`, so pivot and summary totals would differ. See the
  question.
- **Charts** (no totals) and **Excel export** (reads the cube): no change.

### Recommendation

Agree. Apply `having` as a plain SQL `HAVING` on the leaf rows. Every subtotal and total is the
measure recomputed from the surviving leaf rows, so totals always equal what the visible cells
add up to. Adopt it together with item 1.

### Edit plan (if approved)

- 0010 §F.8, decision 9, flag 6: deleted with item 1 (no engine rule is needed).
- 0033 §8.5 rule 5 → the new pivot section states points 1-6 above and the example.
- 0033 §9.3: "Which members exist" → only members with surviving rows; the phantom rule on
  surviving rows.
- 0033 §9.4 and flag 5: subtotal and total drills include rows hidden by `having`.
- 0033 decisions: a new decision "pivot totals are visual" (alternative: SQL's semantics, where
  subtotals include every filtered row).
- 0034 §6.x: nothing changes in `tm-pivot`. Optionally, the renderer's notice strip says "Totals
  include only the cells shown" when the definition has `having`.

### Question back

**Summary totals.** Should a summary's totals row also drop the groups that fail `having`?
- **Recommended: not now.** Today the row means "every filtered row" and also ignores `top`, so it
  is already a population total, not a sum of the page. Making it visual needs a derived-table
  emission, which is an engine amendment.
- **Otherwise:** the summary totals statement runs over the grouped rows that pass `having`.

---

## Sources

- Repo: `.tmp/tree-rollup-analysis/` (benchmark.md, complexity.md, bench/rollup.cs);
  `.tmp/uq-emission-bench/` (results.md, uq.ps1); `.tmp/round-2/reviews/` (rulings.md Q-22,
  C-0033-1; known-issues.md K48; quality.md Q-22).
- [GROUP BY (Transact-SQL)](https://learn.microsoft.com/en-us/sql/t-sql/queries/select-group-by-transact-sql):
  32 expressions and 4,096 sets only with ROLLUP/CUBE/GROUPING SETS; plain GROUP BY bounded by an
  8,060-byte worktable; no subquery in a GROUP BY expression (error 144).
- [AVG (Transact-SQL)](https://github.com/MicrosoftDocs/sql-docs/blob/live/docs/t-sql/functions/avg-transact-sql.md):
  decimal(p, s) returns decimal(38, max(s, 6)).
- [SQLBI, Applying a measure filter in Power BI](https://www.sqlbi.com/articles/applying-a-measure-filter-in-power-bi/);
  [Excel, Subtotal and total fields in a PivotTable](https://support.microsoft.com/en-US/excel/subtotal-and-total-fields-in-a-pivottable).
- Tableau (sources disagree): [order of operations](https://help.tableau.com/current/pro/desktop/en-us/order_of_operations.htm),
  [The Bricks](https://www.thebricks.com/resources/guide-tableau-keep-total-when-filtering),
  [The Data School](https://www.thedataschool.co.uk/zdravka-bratuhtcheva/tabeau-order-of-opearations).
