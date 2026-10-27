# Applied: group 0010-F

File: `docs/specs/0010-amendments-to-implemented-specs.md`: Context, Goals/Non-goals, Part F and
Part F's Testing, Definition of done, Decisions record and Review flags. Parts A–E untouched.
Section numbers below are the final ones (decision 5).

## Spec 0010

**Front matter**

- Context, "Other specs": the 0012 §11.1–§11.2 bullet drops `TreeLevels` (d1); the bullet "spec
  0012 §11.4, spec 0015 §5.2 — the refusal of level paths" is deleted (d1); new bullet "spec 0012
  §3.3, §11.3 — `QueryColumn.Precision`, which fills §F.15's `MeasureContext`" (d1); the consumer
  bullet (0029 §9, 0033, 0035 §6.3) now names `queryex/discover` "with its `measures` clause" (d1;
  folded into the existing bullet instead of a second 0029 §9 bullet).
- Goals, Part F: grouping sets and level navigations dropped; adds "measure partials with their C#
  evaluation (§F.8–§F.15)" (d1, d5).
- Non-goals, Part F: new bullet: non-decomposable aggregates (distinct count, median, percentile)
  are out of scope; the closed decomposition list refuses any future one (§F.14) (d1).

**Part F**

- Intro: the eight report-engine items are "optional parameters, defaults, drill-through, Hijri
  grouping, sharing, parameter pickers, and pivot and summary rollups (§F.8–§F.15)" (d1, d5).
- §F.0 Vocabulary: deleted *Key group*, *Grouping set*, *Level indicator*, *Level navigation*;
  added *Partial*, *Combine*, *Rank column* (d1).
- §F.0 Compatibility: `Level2` bullet deleted; "Version 1" list gains "`Measures` is a new clause;
  with it empty, the engine emits today's SQL"; "Records keep their constructors" now names only
  `ParameterUse` and `CompiledQuery` (no `QueryexColumn` member remains) and drops the
  `NavigationDescriptor.ForeignKey` sentence (d1).
- §F.0 Cache keys: L3 drops key groups and grouping sets, gains the `Measures` text; "Level
  navigations and calendar maps add nothing" → "Calendar maps add nothing" (d1).
- §F.0 Diagnostic codes: QX5008 and QX3104 rows deleted (d1, d2); QX4010 ("This cannot be
  computed over rolled-up values", argument `construct`, §F.15) and QX4011 ("A measure must
  aggregate…", §F.14) added; new bullet "Evaluation codes": QX6001/QX6002 are
  `QueryexEvaluationException.Code`, never diagnostics; new location `Measures[<i>]` (d1).
- §F.0 Reference interpreter: drops grouping sets with the indicator and level navigations; adds
  measures, evaluated directly over a group's rows, never through partials (d1).
- §F.0 `LedgerFixture`: `TreeLevels(4)` deleted; adds nullable numeric columns of every width
  (`tinyint` … `decimal(38,10)`) near their bounds with zero denominators, tying text rows, and
  `hierarchyid` nodes whose paths sort differently as text (d1).
- §F.2: "Requested by" drops spec 0033 §8.6 (d1).
- §F.7 Binding: segments resolve to foreign-key navigations only (level navigation dropped) (d1).
- §F.CUT-8 (grouping sets and the level indicator) and §F.CUT-9 (tree level navigations) deleted
  whole, with `KeyGroups`, `GroupingSets`, `QueryexColumn.IsGroupingLevel`, `GROUPING_ID`, QX5008,
  the finest-set `Having` rule, `EntityBuilder.TreeLevels`, `EntityDescriptor.TreeLevels` and
  `Level{j}` (d1, d3). Every `§F.CUT-` marker in the file is gone.
- §F.9 (constant statements): the `'uq'` sentence moves into the Emission bullet: "Nothing hoists,
  so `Prologue` is empty and a `'uq'` call takes §F.11's scalar form" (d2).
- §F.11 (`'uq'`): Target adds 0008 §12.4. "Two emission forms" → three, chosen by the call's
  operands (its dates and `n` of `addYears`/`addMonths`): row grain → lateral seek; path-free →
  hoisted prologue variable `DECLARE @qx{b}_v{n} <type> = (<scalar form>)`, one per distinct call
  per statement; aggregated → scalar form in place (only in a grouped `SELECT`, `HAVING`,
  `ORDER BY`). QX3104 bullet replaced by "No position refuses a `'uq'` call". "Lookup results"
  loses `GROUPING_ID`. Scalar form now also names its two other uses (constant statement, variable
  initializer). New hoisted example (sales of the current Hijri month per center). Tests: lateral
  goldens lose `GROUPING_ID` and gain a mixed-operand call; new hoisted goldens in `WHERE`,
  `HAVING`, an aggregation's condition and a grouping key, each also run on SQL Server (the
  confirmation decision 2 asks for before QX3104 goes); scalar goldens for aggregated operands
  and a constant statement; QX3104 vectors removed (d2).
- §F.12 (`Exists`): caller errors drop "is a level navigation" (d1).
- New §F.14 `QuerySpec.Measures` and partial columns: `QuerySpec.Measures`,
  `QueryDiscoverySpec.Measures`, `CompiledQuery.Measures`, `MeasurePartial`, `MeasurePartialKind`;
  binding (QX4011 replaces QX4007; QX4010 per §F.15's subset), the `avg` split, the closed
  aggregate list, deduplication, select-item reuse, column order, rank columns (ties, scope,
  nulls, cost, hierarchy order), `Guid` not Ordered, unchanged `Having`/`OrderBy`/paging,
  discovery, caches, caller error; leaf-statement example; tests incl. rank exactness under
  seven collations (d1, design-measures §7.1, §4.1, §7.5).
- New §F.15 Measure evaluation in C#: `CompiledMeasures`, `CompiledMeasure`, `MeasureContext`,
  `QueryexNumericType`, `MeasureEvaluator`, `PartialTable`, `MeasureValue`,
  `QueryexEvaluationException`; the subset table (QX4010 rows with reasons; QX4011 for
  `descendantOf`/`ancestorOf`/`level`); the SQL type of each node; whole partials typed from the
  plan; scale collapse; rounding; the `SqlDecimal` sign defect; 38 digits; accumulation; nulls and
  laziness; errors (QX6001/QX6002, the run fails, session settings); lifetimes; the rollup example
  (Head office / Riyadh / Jeddah); unit vectors and the three-identity differential suite (d1,
  design-measures §3, §5, §7.2, §7.5).
- Part F — Testing: grouping sets, level joins, the indicator, null keys under grouping sets,
  levels at every depth, the grouping limits and the cut example removed; L3 splits on measures;
  goldens for hoisted `'uq'` lookups and partial/rank columns; evaluator vectors; measure
  identities and rank exactness; new one-off "the `'uq'` join-shape benchmark, before §F.11 is
  built" (d1, d2).
- Part F — Definition of done: tool accepts measures and shows partial and rank columns; new
  "Benchmark" bullet (join-shape benchmark before §F.11, result recorded against flag 4); Docs
  name the `'uq'` month table, `Exists` and the measure evaluator; Not in scope drops `TreeLevels`
  and adds 0012 reporting `QueryColumn.Precision` and 0033's cube builder (d1, d2).
- Part F — Decisions: 3 drops key groups and the cut section; old 6–9 (level navigations, counted
  levels, grouping sets, finest-set `Having`) deleted; old 10–14 renumbered 6–10; 9 (`'uq'`)
  rewritten for three forms with the measured cost (variable within 1.6× of Gregorian, scalar up
  to 5×); new 11 (measures decompose into partials, evaluated in C# by the engine), 12
  (`SqlDecimal` at each node's SQL type), 13 (text and hierarchy partials combine by an SQL rank)
  (d1, d2, d3).
- Part F — Review flags: 2 drops the cut section; 4 rewritten: path-free `'uq'` calls as hoisted
  variables, alternative the scalar subquery, flips if the join-shape benchmark finds the
  variable > 1.5× slower than a bound parameter (d2); old 5 (counted level navigations) and old 6
  (`Having` on the finest set: the having/visual-totals flag, resolved by d3) deleted; old 7 keeps
  only the `EvaluateConstant` name and becomes 5; old 8 becomes 6; new 7 = design flag F1 (the
  evaluator's subset), new 8 = F2 (exact text and hierarchy `min`/`max` by rank, not an
  approximate comparer), new 9 = F3 (evaluation errors fail the run) (d1).

**Numbering map for citations of Part F decisions and flags** (no other spec cites them today):
decisions 10→6, 11→7, 12→8, 13→9, 14→10; flags 7→5, 8→6; design flags F1→7, F2→8, F3→9.

## Requests for other files

- **0033 (group 0033):** §8.7 "Its operand rules, QX3104 included, are spec 0010 §F.11's" → drop
  "QX3104 included" (QX3104 no longer exists; §F.11 refuses no position). Keep §8.4 as the place
  that sends summary measures as `Measures`, §8.5 as the leaf statement and §8.6 as the rollup:
  0010 §F.14 cites "spec 0033 §8.4, §8.5" and §F.15 "spec 0033 §8.4, §8.6", and Part F decision
  11 cites "spec 0033 §8.6" for the C# rollup. §F.15 says "spec 0033 maps it to a report error"
  for `QueryexEvaluationException`; 0033 must own the `422 Report.MeasureFailed` mapping.
- **0012 (group 0011-14):** §3.3 `QueryColumn` gains `int? Precision` and §11.3 reads it from
  `DbColumn.NumericPrecision`: 0010's Context and §F.15 (`MeasureContext.DecimalTypes`, "spec 0012
  §3.3's `QueryColumn.Precision` and `Scale`") cite it. §11.2's list: drop "tree level
  navigations (§F.CUT-9)". §11.1: delete the `TreeLevels` sentence. §11.4: delete the level-path
  sentence (0010 Context no longer cites it).
- **0015 (group 0015-22):** §5.2's level-path refusal and §7.3 `QueryLevelNotSelectable`: delete
  (0010 Context no longer cites them).
- **0029 (group 0023-29):** §9 `QueryexDiscoverRequest` gains `string? Measures` (clause
  `measures`), passed to `DiscoverQuery` as `QueryDiscoverySpec.Measures` (0010 §F.14); the
  Context bullet citing "§F.CUT-9" drops it and keeps §F.13.
- **ARCHITECTURE.md (group 0015-22):** where it describes Queryex, Part F's DoD expects it to name
  the `'uq'` month table, `Exists` and the measure evaluator, and no longer grouping sets or level
  navigations.

## Open questions

1. **`'uq'` form chosen by all operands, not the date operand alone (§F.11).** Decision 2 speaks of
   "operands". I classify a call by its dates *and* the `n` of `addYears`/`addMonths`: a call
   with a path-free date but a row-grain `n` (`addMonths(today(), Qty, 'uq')`) takes the lateral
   form, because its second lookup depends on the row and cannot be hoisted. Without this, that
   case would need QX3104 again. Please confirm.
2. **QX3104 "goes once goldens confirm".** The spec states the design with QX3104 gone; the
   confirmation is §F.11's hoisted goldens, each also run on SQL Server in an aggregation's
   condition and a grouping key. If SQL Server refused a variable there, the spec would need a
   revision (flag 4 names the diagnostic that would return with the scalar alternative).
3. **Flag 8 (design F2) deviates from decision 1.** Decision 1 asked for "approximate for text";
   the spec makes text and hierarchy `min`/`max` exact by an SQL `DENSE_RANK` (editor-notes §2).
   The spec words the flag as "Exact … by an SQL rank, not an approximate .NET comparer" without
   citing the review; the alternative is the comparer with word-sort emulation.
4. **"Exact for GUIDs" has no case.** Queryex's `Guid` is not Ordered (0008 §7.1), so `min`/`max`
   over a `Guid` is QX3201 already; §F.14 says so in one line and builds nothing for it.
5. **Size.** Part F grows by about 235 lines net (the two cut sections were about 157 lines;
   §F.14 and §F.15 add about 340). The growth is the evaluator's design: its subset, types,
   semantics and test vectors, which the spec must carry since the design note is not a doc.

## Fix pass

### Spec 0010 (Context, Goals/Non-goals, Part F)

- §F.14, Example (the pivot leaf statement): `Take = 1001` → `Take = MaxPivotRows + 1` (spec
  0033 §8.5), so the example states the pivot cap by pointer, the way 0033 §8.5's own example
  does, instead of a number that disagreed with it (V1-01).
- §F.15, labels: "Requested by" gains spec 0033 §8.11: "spec 0033 §8.4, §8.6, §8.11" (V1-02,
  V8-01; request from group 0033).
- §F.15, Errors, "The run fails" bullet: "The caller reports it (spec 0033 owns the report
  error)" → "The caller reports it: spec 0033 §8.11 answers `422 Report.MeasureFailed`" (V1-02,
  V8-01).

No other change. Context, Goals/Non-goals, Part F Testing, Definition of done, Decisions record
and Review flags needed none: no design changed, and no section, decision or flag number moved.

Checks after the edits: the diff hunks of Context, Goals and Part F re-read; the file has no
`§F.CUT-` marker, and of editor-notes §3's Removed names only "Grouping sets with tree level
navigations were the alternative" survives, as Part F decision 11's rejected alternative.

### Skipped findings

- None. V1-02 and V8-01 are the same request; both are applied by the two §F.15 edits above.
- Not added: spec 0033 §12.2 (export totals, which also use `PartialTable`) to §F.15's
  "Requested by". No finding or request asks for it, and §F.15's Lifetimes already name "one
  export".

### Unresolved for other groups

- None.

## Readability pass

No meaning changes; no section, decision or flag renumbered.

- Goals, Part C: the em-dash list split into a summary sentence and a sentence listing the grid
  features.
- Goals, Part D: the semicolon list becomes sub-bullets.
- §F.0 Compatibility, "Version 1": the two-clause sentence split; the `Measures` bullet loses its
  semicolon.
- §F.0 Diagnostic codes, QX2009 bullet: "It now also covers" → "It also covers" (narration cut).
- §F.11, emission forms: the three nested form bullets become a table (form, operands, emission,
  example), followed by two bullets "Hoisted variable" and "Where an aggregated call sits".
- §F.11, "Scalar form": its three uses become a sub-list.
- §F.14, "Partials": "Every aggregate call is an atom" (a clash with §F.0's *Atom*) reworded as a
  leaf of its measure, with `sum(Amount - Cost)` from the section's example; "structurally equal
  atoms" → "structurally equal partials"; the two sentences of the first sub-bullet swapped.
- §F.14, "Rank columns": "produced" → "produces"; "surfaces" → "returns"; "the superset" spelled
  out as ranking more groups than a page returns.
- §F.14, Example: the spec's inputs become a bullet list.
- §F.14, Tests: the rank-exactness bullet split into its assertion plus Collations, Data and Nulls
  sub-bullets.
- §F.15, "The subset": the select-item sentence reworded in the active voice.
- §F.15, Types table: the `year` … `day` / `diffDays` … `diffMinutes` / `diffSeconds` row split
  into three rows, one per type.
- §F.15, "Scale collapse" and "Rounding": the examples and rules become sub-lists.
- §F.15, Accumulation "Inputs" and Nulls "Laziness": a semicolon split; the two guard examples
  become a sub-list.
- §F.15, Tests, "The sign defect's scope": "against …" reworded as control vectors.
- Part F — Decisions 9 and 11: one long why-sentence split in each.
- Part F — Review flag 8: the three-part alternative split into two sentences.
