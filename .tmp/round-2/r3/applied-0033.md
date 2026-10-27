# Applied: group 0033

File: `docs/specs/0033-report-definitions-and-engine.md` (1,401 → 1,559 lines; the growth is the
leaf statement, the rollup, the summary-totals rule, `422 Report.MeasureFailed` and export paging).
No `§F.CUT-` marker, `levels`, `TreeLevels`, `Level{j}`, grouping-set, `CubeTruncation`,
`Truncated`, `TooManyLevels`, QX5008, QX3104 or `aging` hit remains; "grouping sets over level
navigations" survives only as the rejected alternative in decision 6.

## Changes by decision

**Decisions 1–3 (leaf grain, `'uq'`, visual totals) and design-measures.md §8.2**

- Context: the 0010 bullet lists §F.8–§F.15 with measure partials and their evaluation; the 0012
  bullet is §3.3, §5.2, §6.4, §11.1 (`QueryColumn.Precision`, `EvaluateConstant`,
  `ArithmeticFailureException`, the `'uq'` calendar map; `TreeLevels` and level paths gone); the
  0029 bullet names `queryex/discover`'s `measures` clause.
- Goals: "A pivot groups at leaf grain in SQL; the cube builder rolls up trees, subtotals and
  totals in C# with the engine's measure evaluator."
- Non-goals: distinct count, median, percentile (refused by §F.14); a tree cut at one depth as a
  flat dimension (the feature `Level{j}` gave), later.
- §1 vocabulary: *Level* redefined (depth of an axis's member tree); added *Leaf row*, *Partial*.
- §3.1: `levels` deleted; `tree` comment says full depth; `totals` comment per §8.4.
- §3.4 rule 6: pivot measures, and summary measures when `totals` is true, compile as `Measures`
  (QX4010, QX4011). Rule 12: the `TooManyLevels` bullet deleted.
- §3.5: the pivot example drops `"levels": 4`.
- §8 intro: one statement per kind; a pivot adds one member statement per tree dimension.
- §8.2: "A summary's grand total equals the list rows…" deleted; "total" added to the list of
  numbers that count only readable rows.
- §8.3 step 6: the batch holds the kind's statement and a pivot's member statements; shaping
  names the cube builder and the C# totals row.
- §8.4: the text row loses "one key group"; the labels bullet says "one grouping key per
  dimension".
- §8.5 rewritten as "Pivot: the leaf statement" (keys, labels, `Measures`, plain `HAVING`, member
  statements, cap) with the compiled `QuerySpec` of §3.5's pivot.
- §8.6 rewritten as "The rollup": trees, chains, leaf cells in a `PartialTable`, fold row then
  column, evaluate, emit; g1 item 3's visual-totals rules; caps (incl. `MaxPivotCellPartials`),
  cancellation every 4,096 rows or cells, errors; design §2.4's worked example re-cast on Acme's
  real columns (corporate share `sum(Amount, Invoice.Customer.CustomerType = 'Corporate') /
  sum(Amount)` and `avg(Amount)`, since Acme lines have no `Cost` or `Discount`) plus the
  `having: sum(Amount) >= 5000` outcome.
- §8.7: "QX3104 included" deleted (decision 2); cites §F.11.
- §8.8: "pivot statement rows" → "pivot leaf rows"; the two grouping rows deleted; added
  `MaxPivotCellPartials` 5,000,000 → 413 `Report.TooManyCells`, and "summary totals".
- §8.10: the plan holds `CompiledMeasures`; each run prepares its `MeasureEvaluator`.
- §9.1: `ReportColumn.Scale` comment names `MeasureEvaluator.ResultTypes` for pivot measures.
- §9.3: `Truncated` and `CubeTruncation` deleted; `CubeLevel` lists the levels present in the data;
  member existence and phantom on surviving rows; nesting under deepest members; cell values are
  the measures over combined partials.
- §9.4: leaf-cell drills exact; subtotal, total and summary-totals drills may include rows `having`
  hid (flag 5).
- §15: corpus (QX4010/QX4011), goldens (leaf, member, partial, rank columns; summary probe), cube
  units (chains, phantom, (None), two trees, combines, composite measures, ties, null partials,
  `having`, caps, shuffled rows), runs (brute-force `descendantOf` per cell), nightly load test at
  the caps.
- Decisions: 5 → "one grouping key per dimension"; 6 rewritten (leaf grain + C# rollup, benchmark
  numbers, grouping sets over level navigations as the alternative); new 15 "Pivot totals are
  visual".
- Flags: flag 2 (tree depth) deleted and its slot reused for "Summary totals only when the page
  holds every row", so flags 3–10 keep their numbers (decisions.md cites "0033 flag 5"); flag 5
  extended for `having`; new 11 "Every pivot cell comes from C#" (design asked for it as a flag).

**Decision 4 (summary totals) and flag F6**

- §8.4 Totals bullet: the rule (`Skip` 0; every row returned, probed with `take + 1` unless `top`
  fits the page); `Measures` carried when `Skip` is 0; C# over the returned rows after `having` and
  `top`; otherwise `Totals` null and `TotalsOmitted` true; summaries page at 1,000 rows (0034
  §5.1); the §3.5 example reuses select items.
- §9.2: `ReportTable.TotalsOmitted` added; `Rows` holds the terms' columns only; new "Partial
  columns" bullet (dropped after the totals row; `ColumnOrdinals` index what remains).
- §8.1: `IReportEngine.PagesAsync(source, arguments, countCap)` added: export pages of `MaxTake`,
  first page carries `Count`, the last page's `Table.Totals` covers every page. The example
  response shows `totalsOmitted`.
- §12.1: new "Pages" bullet (both routes read through `PagesAsync`; list or summary up to the caps
  in pages; pivot or chart one page). §12.2: the export Total is always written over every
  exported row; text/hierarchy `min`/`max` exact when data did not change between pages.
- §15: totals-rule boundary cases; export Total equals screen Total; multi-page export totals.
- Decision 16 "Summary totals come from C# over the returned rows…"; flag 2 (above); flag 12 F6
  "Exports always total".

**Decision 5 (Part F numbering)**

- Every citation checked: §F.8 omitted parameters, §F.9 `EvaluateConstant`, §F.10
  `BindParameters`, §F.11 `'uq'`, §F.12 `Exists`, §F.13 `ComparedWith`, §F.14 `Measures`, §F.15 the
  evaluator. No `§F.CUT-` remains.

**Measure errors (design §5.5, editor-notes F5 narrow mapping)**

- New §8.11 "Measure errors": `422 Report.MeasureFailed` from C# (`QueryexEvaluationException`,
  QX6001/QX6002) and from SQL (8134/8115 → `ArithmeticFailureException(Reason)`, 0012 §6.4, mapped
  only by the report engine); arguments `reason`, `pointer`, `row`, `column`; first failing cell in
  member order; example.
- §14 logs: `ReportEvents.MeasureFailed` (information; id, pointer, reason).
- §15 runs: the 422 from a summary row (8134) and from a pivot cell (QX6001).
- Decision 17 "Arithmetic failures answer 422 Report.MeasureFailed, from C# and SQL alike"; flag 13
  "Only the report engine maps SQL's arithmetic errors to a 422" (the CRUD pipeline could too).

**Decision 18 (decimals and longs as JSON strings)**

- §4.4 Values: a `Numeric` argument is `"5000"`; a JSON number is accepted inbound; the echo is the
  string.
- §9 intro: `decimal` and `long` travel as strings in row sets, the totals row, cube cells and
  argument echoes; a pivot measure keeps up to 38 digits.
- Context: the 0016 bullet adds §3.2.

**Decision 20 (`Long`, `Idempotent`)**

- §2.2: `run`, `export`, `audience` declare `Long = true` (the `tellma-long` timeout and the
  contracts chunk's long list); the table says so. Sketches: `run` (§8.1) and `export` (§12.1)
  gain `Long = true`, and so does `audience` (§6.1).
- §11: the coded-report contract adds `Long = true` (an `[ApiRoute]` action is projected, 0016
  §2.4, so the chunk carries it); Acme's action declares it.
- Context: 0015 bullet names the `Long` flag; 0016 bullet adds §3.10.

**Decision 21 (`forbidden` reasons)**

- §8.2: the root refusal carries `no-grant`; the traversal refusal `restricted-path` with `path`
  (0015 §14.1). §15 runs: traversal 403 with `restricted-path`. Context: 0015 bullet adds §14.1.

**Decision 23 (credit control)**

- §11: `acme-reports/aging` → `acme-reports/credit-exposure`; `AgingAsync(AgingArguments)` →
  `CreditExposureAsync(CreditExposureArguments)`; `acme.AgingReport` →
  `acme.CreditExposureReport`; the sketch comment describes the report; the body example renamed.
- §12.1 `Route` comment, §15 child-routes suite, §16 Acme bullet renamed.

**Decision 25 (white labelling)** — no user-facing copy in 0033 says "Tellma"; nothing changed.

## Requests for other files

- **0034 (group 0034-35), §3.3 calculation-error state:** `pointer` is `""` (the whole document)
  when SQL raised the error, so there is no measure label; the copy needs a label-free variant,
  e.g. "A calculation divides by zero in some rows." (`overflow`: "A calculation is too large in
  some rows.").
- **0034 §9 (drill) / §5.3:** under `top`, a summary's totals row has no drill (0033 §9.4); the
  renderer must not offer one.
- **0034 §2.2 and 0024 §14 (groups 0034-35, 0024):** the coded report route
  `acme-reports/credit-exposure` is an `[ApiRoute]` action, which spec 0016 §2.4 projects, and it
  declares `Long = true`; the contracts chunk therefore lists it, and `long: true` at the call site
  is unnecessary. g4 called it hand-mapped; please confirm against 0016 §2.4 before keeping `long`.
- **0019 (group 0015-22), §12.3 "Report case":** "A list streams in engine pages of `MaxTake` rows"
  → "A list or a summary streams through `IReportEngine.PagesAsync` in pages of `MaxTake` rows
  (spec 0033 §12.1); a summary with totals writes its Total row last, from the last page."
- **0012 (group 0011-14), §6.4:** confirm the reason values `divide-by-zero` and `overflow` on
  `ArithmeticFailureException(Reason)`, as 0033 §8.11 uses them.
- **0010 (group 0010-F), §F.14 / §F.15:** "Requested by" cites 0033 §8.4 (summary totals), §8.5
  (leaf statement), §8.6 (rollup) and §8.11 (errors); §7.4 of the design lands in 0033 §3.4 rule 6.
- **0016 (group 0015-22), §8.3:** `reports/run`, `reports/export` and `reports/audience` declare
  `Long = true` in 0033 (§2.2, §8.1, §12.1, §6.1).

## Open questions

1. **`pointer` for an error SQL raised.** SQL names neither the term nor the group (8134 can come
   from a row-level filter as well as a measure), so 0033 sends the empty pointer `""`. Design
   §8.2's test wording ("same `pointer` and `reason`") became "same `reason`". Alternative: name
   the first measure that can fail that way, which needs the engine to report operators per item.
2. **`IReportEngine.PagesAsync`.** Design §6 says the writer adds each page's partials, but
   `ReportTable.Rows` drops partial columns. 0033 has the engine add them inside a new
   `PagesAsync`, keeping partials internal.
3. **Load-test ceiling.** Design §8.2 asks for "a memory ceiling"; 0033 says a managed heap under
   512 MB (g1 estimated about 150 MB at 500,000 cells × 10 partials).

## Fix pass

File: `docs/specs/0033-report-definitions-and-engine.md` (0023–0035 range; short list).

- Goals, kinds bullet: "each compiled to one Queryex statement, plus one member statement per tree
  dimension of a pivot (§8)", matching §8's intro and §8.5 step 5 (V1-04).
- §3.4 rule 6: QX4011 covers a measure that reads a path outside an aggregation "or has no
  aggregation at all", as spec 0010 §F.14 and §F.0's code table state (V1-05).
- §8.1 example: the summary's first open sends `"take": 1000`, as spec 0034 §3.2 and §5.1 page a
  summary (V1-03).
- §9.3 "Which members exist": the restated existence and phantom rules replaced by a pointer to
  §8.6; the `Total` member sentence kept (V7-03).
- Rulings R-5 (no description of the `'uq'` forms; §8.7 cites spec 0010 §F.11) and R-6 (no GUID
  `min`/`max` claim; only text and hierarchy `min`/`max` by rank) confirmed met; no edit. R-28
  confirmed: §11's coded report is an `[ApiAction]` on an `[ApiRoute]` class with `Long = true`.

Post-fix grep: no `§F.CUT-` marker and no Removed name (`TreeLevels`, `Level{j}`,
`LevelNotSelectable`, `Entity_Level`, QX5008, the `levels` field, `CubeTruncation`, `Truncated`,
`TooManyLevels`, `GROUPING_ID`, `treeLevels`). "Grouping sets" survives only as decision 6's
rejected alternative, and "the levels present in the data" is `CubeLevel`'s comment (a depth of
the member tree, §1 vocabulary), not the removed field.

### Skipped findings

- None.

### Unresolved for other groups

- None. Every request from this group's log is met per verify-requests.md's ledger, except two
  that other groups own and were already raised as their findings: 0024 §14's "hand-mapped …
  with `long`" (V8-03 / V4-08, group 0024) and 0010 §F.15's "Requested by" and Errors bullet
  citing 0033 §8.11 (V1-02 / V8-01, group 0010-F).

## Readability pass

File: `docs/specs/0033-report-definitions-and-engine.md`. No meaning changes; no renumbering.

- Goals: the kinds bullet split into two plain sentences; non-goal on distinct counts split.
- §1 Vocabulary: *Leaf row* names the "leaf statement", the term §8.5 uses.
- §2.2: the opening paragraph became bullets (retry classes, long routes, sensitive pairs).
- §3.4 rule 6: the QX4010 / QX4011 bullet split into sentences.
- §4.4 Values: split; the echo encoding points at §9 instead of restating it.
- §8.3 step 6: the shaping clause split into a sentence.
- §8.4 Totals: the dense paragraph became bold-led bullets (partials, evaluation, omission, `top`
  fits, citing §3.4 rule 8); the conditions list and example kept.
- §8.5: "rides" → "runs"; steps 1, 3 and 5 split into shorter sentences.
- §8.6: step 4's combine rules became sub-bullets; step 5 and the `having` bullets split; the
  worked example names the conditional-sum partial and spells out the `having` outcome.
- §8.10: the context-slot bullet and the closing "So …" sentence reworded.
- §8.11: `pointer` and `row`/`column` arguments restructured; captions split; SQL examples
  labelled; the example in plain present tense.
- §9 intro, §9.2 totals row, §12.1 Pages: semicolons and parentheses split into sentences.
- §15 Runs: the run-on bullet became sub-bullets; Performance's long parenthetical moved after a
  colon.
- Decisions 6, 15, 16 and flags 5, 12, 13: split into shorter sentences; each alternative named
  as such.
