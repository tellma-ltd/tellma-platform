# Verify (Fable, key: reports) — scenario walk of the report engine specs

Scenarios walked as an implementer: (1) pivot "customer group (tree) × month" with `sum(Amount)`,
`sum(Amount - Cost) / sum(Amount)`, `max(Customer.Name)` and `having sum(Amount) > 0`; (2) the
same ratio dividing by zero in SQL (`having`, a summary row) and in a C# subtotal; (3) a summary
with `top 10` and totals, then 5,000 rows; (4) the Excel export of the 5,000-row summary; (5)
`sum(Amount, Invoice.PostingDate >= addMonths(today(), 1, 'uq'))` in a pivot. Judged against
decisions.md, editor-notes.md, fix-notes.md (R-21, R-22, R-26, R-27) and design-measures.md.

What held up end to end (no finding): save-time QX4010/QX4011 (0033 §3.4 rule 6, 0010 §F.14,
0029 §9, 0034 §12.7); the leaf statement, its partials, the `avg` split, select-item reuse and the
`DENSE_RANK` column (0010 §F.14, 0033 §8.5); `QueryColumn.Precision` and the typed buffers (0012
§3.3, §11.3 → `MeasureContext.DecimalTypes`); the fold order, phantom rule and visual totals
(0033 §8.6 ≡ decision 3; its worked numbers check out); the caps and their 413s (0033 §8.8, 0034
§3.3); the 8134/8115 path (0012 §6.4 → `ArithmeticFailureException` → 0033 §8.11 → 0034 §3.3
label-free copy); the summary totals rule and probe (0033 §8.4 ≡ design §6; 0034 §5.1/§5.3; 0035
§8.5/§9.1 per R-27); `PagesAsync` carrying the totals on the last page (0033 §8.1, §12.2; 0019
§12.3 per R-22); and the `'uq'` call inside an aggregate's condition taking the hoisted form while
staying out of the C# subset (0010 §F.11, §F.14, §F.15; 0033 §8.7 per R-5).

## 0033

- [FA-01] (must) 0033 §8.4 — "When `Skip` is 0, the statement also carries the measures as
  `Measures` (spec 0010 §F.14)." -> "When `Skip` is 0, and on every page of an export (§12.2)
  whatever its `Skip`, the statement also carries the measures as `Measures` (spec 0010 §F.14)."
  Add to the same bullet: "An export's pages leave `TotalsOmitted` false; the totals ride on the
  last page (§8.1)." Why: scenario 4, step "partials across pages". §12.2 says "The engine adds
  each page's partials as it reads", but pages 2+ of a 5,000-row export have `Skip` ≥ `MaxTake`,
  so under §8.4's rule they compile without `Measures` and return no partials to add; the Total
  row would cover page 1 only. §9.2's `TotalsOmitted` ("totals asked for, but the result is
  paged") would also read true on every export page.
- [FA-02] (must) 0033 §9.2 — "QueryRowSet? Totals, // Summary with totals, when the page holds
  every row (§8.4): one row, measures only" -> "one row with the same `Columns` as `Rows`, so
  `ColumnOrdinals` reads it too; every non-measure cell is null (the renderer writes "Total"
  there)". Alternative if "measures only" is kept: add `IReadOnlyList<int>? TotalsOrdinals`
  (parallel to `Columns`, null for non-measures) and say the writer and renderer index through it.
  Why: scenario 3, "the totals rule … the footer" and scenario 4, "the Total row written last".
  0034 §5.3 shows "Total" in the first key column and formats each measure cell "through
  `tmReportFormat`" by column, and §12.2's writer emits the Total row under the same columns as
  the rows; neither can find measure i inside a "measures only" row set because the only index
  given (`ColumnOrdinals`) is defined over `Rows`. The client and server must agree on this wire
  shape, and no test of the Total row can be written from the text.
- [FA-03] (must) 0033 §8.11 — "the cell's member on each axis, one caption per dimension in the
  request's content slot, outermost first, `null` for (None), and empty for the axis total" ->
  "the captions of the cell's member and its ancestors on that axis, outermost dimension first,
  stopping at the cell's member (a subtotal on an outer dimension lists fewer captions than the
  axis has dimensions); empty for the axis total. A navigation or text member's caption is its
  display text in the request's content slot, a phantom's is its node's, a value or expression
  member's is its value's platform scalar text (spec 0016 §3.2), and `null` stands only for
  (None)." Why: scenario 2, the C# subtotal. With two row dimensions (`Center`, then `Customer`),
  a divide by zero in Riyadh's subtotal cell has no `Customer` member: "one caption per dimension"
  forces either a shorter list, which nothing allows, or a `null` filler, which 0034 §3.3 renders
  as "(None)" ("Riyadh × (None) × Q3"). The caption of a month or quarter member is also undefined
  (0034's "Q3" exists nowhere for `quarter(…)` = 3), so the server and client tests cannot agree
  on the captions.
- [FA-07] (should) 0033 §8.10 — "each run prepares its own `MeasureEvaluator` from the reader's
  decimal types" -> "each run prepares its own `MeasureEvaluator` from a `MeasureContext` holding
  the reader's precision and scale of each partial column (spec 0012 §3.3), the run's effective
  arguments (supplied and defaulted; an omitted one absent, §4.4), and the `Today`, `Now` and
  `UserId` the batch bound its context slots with (spec 0012 §5.1, §11.3)". Why: scenario 1 (a
  measure such as `if(sum(Amount) > @Limit, …)` or `diffDays(max(PostingDate), today())`) and
  scenario 5. Spec 0010 §F.15's `MeasureContext` needs `Arguments`, `Today`, `Now` and `UserId`
  "the values the statement's context slots were bound with"; 0033 names only the decimal types,
  so nothing in 0033 says the evaluator and the SQL statement see one `today()` and one argument
  set, the property §4.3 ("One clock, one user") promises for defaults.

## 0034

- [FA-02] (should) 0034 §5.3 — "`table.totals` is the last row when the page holds the whole
  result (spec 0033 §8.4)." -> add "Its cells are read through the same `columnOrdinals` as the
  rows; the key cells are null, and the renderer writes "Total" in the first key column." Why:
  the companion of 0033 FA-02 (scenario 3); today the renderer has no stated way to locate a
  measure inside the totals row set.
- [FA-03] (should) 0034 §3.3 — ""Riyadh × Q3", "Total × Q3", or "Total" for a summary's totals
  row" -> ""Riyadh × 3", "Total × 3", or "Total" for a summary's totals row; captions render as
  the server sends them (spec 0033 §8.11)". Why: the companion of 0033 FA-03 (scenario 2). A
  `quarter(Invoice.PostingDate)` member's caption is its value, and the client formats nothing
  here; "Q3" implies a formatting step that neither spec defines.
- [FA-04] (should) 0034 §3.3 — "`{measure}` is the label (§3.5) of the term at `pointer`, read
  from the draft or the held result." -> "…read from the draft, the held result, or the stored
  definition the report page already holds (the hatch parses `definitionJson` from
  `injectTmDetails()`, §10.1)." Why: scenario 2 in the report page. A stored report's first run
  has no held result, yet the page reads the record with `reports/get` in the same frame (§3.2
  step 2); as written the first failure reads "A calculation divides by zero at Riyadh × Q3",
  which defeats decision 20 ("names the measure and the cell") exactly where authors meet it
  first. R-26 accepted client-side resolution; this only adds the source the page has.

## 0035

- [FA-05] (should) 0035 §9.3 — "report(state: { readonly status: 'loading' | 'ready' | 'error';
  readonly executedAt?: string | null; readonly error?: TmApiError | null }): void;" -> add
  "readonly totalsOmitted?: boolean; // a summary whose totals row was omitted (spec 0034 §5.3)"
  and in §8.5 "Omitted totals" say the note shows when the body reports `totalsOmitted`. Why:
  scenario 3 in a tile. §8.5 shows "Updated 10:42 · Totals not shown" "when a summary's result
  has `totalsOmitted`", and §9.3 says the body's `report` calls are what "feed the status line",
  but the state carries no such member and the tile never sees the host's result, so the note
  (R-27's chosen channel) has no stated source.
- [FA-06] (should) 0035 §8.5 — "Before the first result, a skeleton bar of the text's final width
  holds its place." -> add "When the first run fails (`status: 'error'` with no `executedAt`),
  the line is empty; the body shows the failure (§8.6)." Why: scenario 2 in a tile. A first-load
  `422 Report.MeasureFailed` leaves the line in a state the text never resolves (the skeleton bar
  would stay until a later success), and the stale marker rule covers only a failure "over held
  data".
