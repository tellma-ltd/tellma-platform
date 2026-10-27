# Final fix pass, key `client-b` (specs 0030–0035)

Findings applied from `verify-fable-reports.md` (0033, 0034, 0035) and `verify-fable-data.md`
(0032). No finding in the four files targets 0030 or 0031. No file of 0010–0022 or
ARCHITECTURE.md was edited.

## 0010–0022 and ARCHITECTURE.md

- None.

## 0030, 0031

- No change.

## 0032

- §4.2 digits row: the native option is named by the active language's system, else, under
  `offered`, by the first offered language's that has one (`arab` in Acme) (FC-07).
- §Testing, Locale fields: pins the native option's system under each scope (FC-07).

## 0033

- §8.4 Totals: an export's pages carry `Measures` whatever their `Skip`; export pages leave
  `TotalsOmitted` false and the last page carries the totals (FA-01).
- §9.2: the `Totals` comment reads "one row over Rows' columns"; new bullet "The totals row": same
  columns as `Rows`, read through `ColumnOrdinals`, measure cells hold the totals, every other
  cell null, "Total" written in the first key column by the renderer and the Excel writer (FA-02).
- §8.11 `row`/`column`: one caption per dimension down to the cell's own member, so an outer
  subtotal lists fewer captions; new "Captions" bullet (navigation and text: display text in the
  content slot; phantom: its node's; value or expression: the value as text in spec 0016 §3.2's
  encoding; `null` only for (None)); the example adds an outer subtotal with `column` `["3"]`
  (FA-03).
- §8.10: the `MeasureContext` a run prepares its evaluator from is spelled out (reader precision
  and scale, effective arguments with omitted ones absent, the batch's `Today`/`Now`/`UserId`),
  so the evaluator and the statement share one `today()`, `me()` and argument set (FA-07).
- §15 Runs: a summary total read through `ColumnOrdinals` with null key cells; an outer
  subtotal's shorter `row` and a value member's caption; a measure over `today()` and an argument
  (FA-02, FA-03, FA-07).

## 0034

- §3.1 contract: new input `document: InputSignal<TmReportDocument | undefined>`, the stored
  document when the page holds it, used to label a calculation error (FA-04).
- §3.3 calculation error: captions render as the server sends them; examples "Riyadh × 3",
  "Total × 3" (FA-03); `{measure}` is read from the draft, the stored document (`document` input
  or the definition the host read, §3.1) or the held result (FA-04).
- §5.3 Totals: the totals row's cells read through the same `columnOrdinals`; key cells null
  (FA-02).
- §10.1 Body: the hatch parses `definitionJson` and passes the parameters as `declarations` and
  the document as `document` (FA-04).
- §14 Host harness: the calculation error from an outer subtotal, and on a stored report's first
  run labelled from `document` (FA-03, FA-04).

## 0035

- §9.3 `report(state)` gains `totalsOmitted?: boolean` (FA-05).
- §8.5 Omitted totals: the note shows when the body reports `totalsOmitted`; the report widget
  reports its summary result's `table.totalsOmitted` (FA-05).
- §8.5: new bullet: when the first run fails (`status: 'error'`, no `executedAt`), the skeleton
  bar goes and the line stays empty; the body shows the failure (FA-06).
- §17 Harnesses: pins the empty line after a failed first run (FA-06).

## Skipped findings

- None.

## Choices to confirm

- **Totals row wire shape (FA-02).** `Totals` has the same columns as `Rows` with null
  non-measure cells, so `ColumnOrdinals` index it. Alternative: a separate `TotalsOrdinals`
  list over a measures-only row set.
- **Value captions (FA-03).** A value or expression member's caption is its raw wire text, so the
  client shows "Riyadh × 3", not "Riyadh × Q3". Alternative: the client formats captions by the
  dimension's display, which needs the dimension types on a failed run.
- **Host `document` input (FA-04).** Added beside `declarations`. Alternative: replace
  `declarations` with one `document` input and read the bar's fields from
  `document.parameters`.
- **Native digits label under `offered` (FC-07).** Named by the active language's system, else
  the first offered language's that has one. Alternative: a generic "Native digits" label.

## Unresolved for other ranges

- None.
