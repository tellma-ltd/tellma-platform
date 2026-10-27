# Verify: measures cluster (V1)

Decisions 1–5, design-measures.md, rulings R-5, R-6, R-14, R-21, R-22 (and R-27 where the
summary-widget check met it). Findings in fix-notes.md §3 format, grouped by owner group
(editor-notes.md §5). Checked and clean, no finding: 0010 §F.0, §F.14, §F.15 against design §7
(subset, types, rank statement, partial kinds, `MeasureContext`, evaluator API, QX4010/QX4011/
QX6001/QX6002, §7.5 tests), Part F Testing/DoD/Decisions, flags 7–9 (F1–F3); 0012 §3.3, §6.4 (the
8134/8115 row, the eight statement exceptions), §11.1–§11.4, §13, DoD, decision 36; 0015 (no F5
change, level refusal gone); 0016 `QueryColumn` converter; 0019 §12.3; 0013 (`Entity_Level` gone,
§F.11 cites); 0029 §9, §10.1–§10.2, Context; 0033 §8.4–§8.6, §8.8, §8.10, §8.11, §9.2–§9.4, §12,
§14, §15, decisions 6/15/16/17, flags 2/5/11/12/13; 0034 §3.3 calculation-error copy (label-free
"A calculation" for `pointer` ""), §5.1, §5.3 drill rule, §6.5, §8.1, §12.3, §12.7, §12.8, flag 13,
decision 19; ARCHITECTURE.md line 738. Every "§F.n" citation in 0010–0035 and ARCHITECTURE.md
points at the right item under the final numbering. No removed name survives outside a Decisions
row's rejected alternative (0019 §5.3's manifest `Truncated` row is an unrelated export field).
R-5 (no other spec describes the `'uq'` forms) and R-6 (no GUID `min`/`max` claim) hold.

## 0010-F

- [V1-01] (should) 0010 §F.14, Example — "a pivot's leaf statement over `acme.SalesInvoiceLine` by
  `Center`. … `OrderBy = "Center.Id"`, `Take = 1001`" → replace `Take = 1001` with `Take = 250001`
  (spec 0033 §8.5's `MaxPivotRows + 1`), or drop "a pivot's" and call it "a leaf statement".
  Why: spec 0033 §8.5 and design §2.4 take `MaxPivotRows + 1` (250,000 + 1) for a pivot's leaf
  statement; the same number must appear everywhere.
- [V1-02] (should) 0010 §F.15 — "**Requested by:** spec 0033 §8.4, §8.6." and, under Errors, "The
  caller reports it (spec 0033 owns the report error)." → Requested by: "spec 0033 §8.4, §8.6,
  §8.11"; the Errors bullet: "The caller reports it: spec 0033 §8.11 answers `422
  Report.MeasureFailed`." Why: request from group 0033 (applied-0033.md, "0010 §F.14 / §F.15");
  0033 §8.11 owns the mapping and is a designed spec, so it can be cited by section.

## 0033

- [V1-03] (should) 0033 §8.1, Example — "`→ POST /17/api/web/reports/run { "id": 42, "arguments":
  {}, "take": 200, "includeCount": true }`" → `"take": 1000`. Why: report 42 is a summary, and the
  same first open in spec 0034 §3.2 sends `take: 1000` (0034 §3.1, §5.1: a summary pages at 1,000;
  design §6, flag F4).
- [V1-04] (should) 0033 Goals — "Four kinds, each compiled to one Queryex statement (§8)." → "Four
  kinds, each compiled to one Queryex statement, plus one member statement per tree dimension of a
  pivot (§8)." Why: §8 intro and §8.5 step 5 say a pivot adds member statements; Goals contradicts
  them.
- [V1-05] (should) 0033 §3.4 rule 6 — "and a measure that reads a path outside an aggregation is
  QX4011" → "and a measure that reads a path outside an aggregation, or has no aggregation at all,
  is QX4011". Why: spec 0010 §F.14 (and §F.0's code table) raise QX4011 for both cases; §15's
  corpus pins QX4011 for a constant measure via 0010.

## 0034-35

- [V1-06] (must) 0034 §3.3, "The notice strip" bullet — "a refused drill (§8.2), "Preview is out
  of date" (§12.8), or, in `widget` mode, a summary's omitted totals (§5.3)." → delete ", or, in
  `widget` mode, a summary's omitted totals (§5.3)" (end the list at "(§12.8)"). Why: R-27: no
  overlay strip on a tile.
- [V1-07] (must) 0034 §5.3, "Omitted totals" — "`widget` mode has no footer, so the notice strip
  (§3.3) says it." → "In `widget` mode the tile's status line carries a short note (spec 0035
  §8.5); a tile without a status line shows no message, and the full report view explains." Why:
  R-27.
- [V1-08] (must) 0034 §14, Renderers — "the `totalsOmitted` footer and its `widget`-mode notice" →
  "the `totalsOmitted` footer, no notice strip in `widget` mode". Why: R-27; the test must pin the
  ruled behaviour, not the strip.
- [V1-09] (must) 0035 §9.1, "Totals are the report's" — "otherwise the host's notice strip says why
  (spec 0034 §5.3)." → "otherwise the status line (§8.5) adds a short note after the time, such as
  "Updated 10:42 · Totals in the full report", with the footer's sentence of spec 0034 §5.3 as its
  tooltip; the tile shows no strip." Add the same note to §8.5's list of what the status line
  shows. Why: R-27 (report widgets always have a status line, §8.5).

## 0023-29

- [V1-10] (must) 0029 §6.2 — "`options?: { aggregate?: boolean }): TmSelectPlan;`" and the bullet
  "**Aggregate mode** serves report previews (spec 0034). It expands navigations the same way, each
  term a grouping key that depends on the id." → delete the `options` parameter (signature
  `tmSelectPlan(root, keys): TmSelectPlan`) and delete the bullet. Why: R-14. Grepping 0030–0035
  finds no `aggregate: true` and no aggregate-mode caller: 0030 §2.5 and §5.1 call `tmSelectPlan`
  in plain mode, and 0034 builds no select plan (previews run `reports/run` on the server). The bullet also
  contradicts 0033 §8.4, where navigation labels are `max(...)`, not grouping keys. No 0029 §17 row
  tests the mode, so no test row changes.
