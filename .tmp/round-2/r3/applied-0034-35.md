# Applied: group 0034-35

Files: `docs/specs/0034-report-renderers-and-studio.md` (1,279 → 1,340 lines) and
`docs/specs/0035-dashboards-and-home-page.md` (1,266 → 1,271 lines). Baseline `063e313`; 0035
already carried the script's Part F citation fix. No section was renumbered. No `levels`,
truncation, `§F.CUT-`, `aging`, `long: true` on a projected route or "150 ms" hit remains.

## 0034 changes by decision

- **1 (leaf grain) and design-measures.md §8.3**
  - §3.3: the notice strip loses the truncated-levels example; it now lists a refused drill,
    "Preview is out of date", or a summary's omitted totals in `widget` mode.
  - §3.3: new state row "calculation error" for `422 Report.MeasureFailed` (spec 0033 §8.11):
    `circle-alert`, "A measure couldn't be calculated", Edit with `permissions.save`. New bullet
    with the reason copy: "{measure} divides by zero at {cell}. Guard it with if(…)." /
    "{measure} is too large at {cell}."; `{cell}` joins `row` then `column` captions with " × "
    (empty list "Total", null "(None)"); no `row` (SQL raised it) → "in some rows"; `{measure}` is
    the label of the term at `pointer` from the draft or held result, else "A calculation" (covers
    0033's request for a label-free variant when `pointer` is empty).
  - §12.3: the dimension editor drops "Levels (1–10, default `min(maxDepth, 6)`)"; a tree always
    shows its full depth.
  - §12.7 step 2: pivot measures, and a summary's measures when "Show totals row" is on, go to the
    draft as `measures` (spec 0029 §10.2) joined by ", ", spans mapped by offset; QX4010 shows
    (spec 0010 §F.14, §F.15). Other terms stay in `select`.
  - §12.8: "levels" dropped from the query changes.
  - Context: the 0033 bullet adds §8.11 (measure errors).
  - §14: host harness (calculation error from a pivot cell, a summary's totals row, an SQL row);
    studio harness (`measures` clause and QX4010 mapping).
  - Decision 20 (calculation error names the measure and the cell).
- **3 (visual totals)**
  - §6.5: new "Totals are the server's" bullet (measures over leaf rows that passed `having`,
    spec 0033 §8.6; the renderer never computes one).
  - §8.1: a leaf cell's or summary row's drill holds exactly its rows; a subtotal's or total's may
    hold rows `having` hid (spec 0033 §9.4).
  - §15 Acme e2e: "drill a pivot leaf cell and check the drilled rows sum to it".
- **4 (summary totals) and flag F4**
  - §3.1: `take` is 200 for a list and 1,000 for a summary. §3.2's example (report 42, a summary)
    sends `take: 1000`.
  - §5.1: new "Pages" bullet (list 200, summary 1,000; every summary with `top` fits); footer
    "Past one page" adds the pager.
  - §5.3: totals row only when the page holds the whole result (spec 0033 §8.4); no Total drill
    under `top` (0033's request); new "Omitted totals" bullet with the footer copy "Totals show
    when all rows fit on one page. Export includes them." and the notice strip in `widget` mode.
  - §14: new "Renderers" bullet (single-page totals, `totalsOmitted` footer and widget notice, no
    Total drill under `top`).
  - Decision 19; Review flag 13 (F4: summaries page at 1,000 rows).
- **6 (icons):** §6.2 the pivot twisty is a `tm-icon` with `mirrorInRtl` (spec 0023 §3.1).
- **7 (keys):** §6.4 new bullet: the pivot's keys match through `kbd` (spec 0023 §3.2).
- **8 (categorical tokens)**
  - §7.7: `series1`–`series8` read `categorical.1.mark` … `categorical.8.mark` (spec 0010 §B.8);
    the per-scheme literals moved out (0010 §B.8 holds the same values in the same order); the
    validation runs over the series colours in that order. Context adds 0010 §B.8.
  - §14 conformance: §7.7's checks over the `categorical` marks.
  - Decision 13 rewritten in place.
- **8 (avatar `colorKey`):** §10.4 share dialog avatars pass `core.User:{id}` as `colorKey`. The
  reports page's Owner column goes through `tm-value` (spec 0029 §12.2), which passes it already.
- **10 (reloading bar):** §3.3 reloading row no longer says "after 150 ms"; new bullet: the bar
  follows the search page's rule (spec 0030 §3.3), shows in the frame that starts the run, holds
  while the held output is stale (never blinks between superseding runs), at least 250 ms (spec
  0023 §4.4); `aria-busy` follows the run. §14 host harness and §15 "Instant feedback" pin it.
- **13 (no client hide):** no hit in 0034.
- **17 (`TmUrls`):** no tenant argument anywhere in 0034; no change.
- **18 (decimals as strings)**
  - §3.5: a `decimal` or `long` value formats from its exact string (spec 0024 §10.4).
  - §4.4: new "Numbers" bullet: the codec and the bar hold a `Numeric` value as a `number`
    (`p.Year=2026` → `2026`), text past 15 significant digits as a `TmDecimal`; echoes arrive as
    `TmDecimal` strings and a field shows them through `TmDecimals.toNumber`.
  - §7.9: chart values convert with `TmDecimals.toNumber`.
  - §14: codec vector for a value past 15 digits; renderer formatting beyond 15 digits.
- **19 (time zones)**
  - §5.1: new "Instant columns" bullet: header appends `TmFormats.zoneSuffix` (spec 0029 §12.1),
    as spec 0030 §7.1 does; cells in `'column'` mode (spec 0010 §D.4).
  - §3.6: "Updated {time}" carries the zone label when offsets differ ("Updated 10:42 GMT+3").
  - §14 renderers bullet pins the header suffix and `'column'` cells.
- **20 (long routes)**
  - §2.2: `TmApi.read(route, args, { signal })`; new "Long by declaration" bullet: the coded
    route's action declares `Long = true` (spec 0033 §11), the Contracts chunk lists it (spec 0024
    §3.4); a `run` hatch calling a hand-mapped endpoint (spec 0016 §4.5) passes `long: true`.
  - §3.1 and §9 step 1: `long: true` dropped; the Contracts chunk marks `reports/run` and
    `reports/export` long.
  - §14 host harness: no `long` option on runs and exports.
- **23 (credit control):** §2.1 card (`Acme_Customer_CreditExposure`, key
  `acme-credit-exposure`); §2.2 example (`credit-exposure.report.ts`, `creditExposureReport`,
  `Acme_Report_CreditExposure`, `acme.CreditExposureReport`, `acme-reports/credit-exposure`);
  §15 warm-launch path `/17/reports/acme-credit-exposure` and the Acme bullet.
- **25 (white labelling):** no user-facing copy says "Tellma" ("the Tellma facade" in Context is
  developer text); no change.
- **2, 5 (Part F numbering):** 0034 cites only §F.14 and §F.15, both new numbers.

## 0035 changes by decision

- **1 (citations):** §6.3 cites spec 0010 §F.12 (`Exists`); no other Part F citation.
- **3 and 4 (pivot and summary widgets):** §9.1 new "Totals are the report's" bullet: a pivot
  tile's totals cover only the cells it shows (spec 0033 §8.6); a summary tile shows its totals
  row only when its first page holds every row, else the host's notice strip says why (spec 0034
  §5.3).
- **13:** no `hide` of `widget:` or `report:` in 0035; `widget-missing` stays a build check.
- **17:** no tenant argument; no change.
- **18:** no change needed: §10.2's examples send numbers, which a `decimal` and a coded `int`
  both accept (spec 0034 §4.4); echoes compared in §10.3 are strings and compare as text.
- **20:** §3.2 `audience` row declares `Idempotent = true`, `Long = true`; Context's 0015 bullet
  adds §2.3 (the `Long` and `Idempotent` flags); §17 Web suite checks `dashboards/audience` in
  the Contracts chunk's `long` and `idempotent` lists.
- **23:** §2.3 hatch comment and widget (`credit-exposure`, `acme-credit-exposure`, wrapped to two
  lines); §3.1 `ReportKey` comment; §10.2 table row and the sentence below ("Credit exposure");
  §18 Acme bullet.
- **25:** no "Tellma" in user copy (`document.title` "Home · Acme" is the display name).

## Requests for other files

- **0024 (group 0024), §14 last Acme bullet:** "the hand-mapped `acme-reports/credit-exposure`,
  called through `TmApi.read` with `long`" contradicts spec 0033 §11 (an `[ApiRoute]` service's
  `[ApiAction]` with `Long = true`, projected per spec 0016 §2.4 and §4.4) and 0034 §2.2. Change
  to: "the coded route `acme-reports/credit-exposure` (spec 0033 §11), which the Contracts chunk
  lists as long and idempotent, so `TmApi.read` passes no `long`."
- **0030 (group 0028-32), §3.3:** 0034 §3.3 now cites "the search page's rule (spec 0030 §3.3)"
  for the reloading bar; §3.3 must state it (bar on when `search` fires, held while the result is
  stale, at least 250 ms per spec 0023 §4.4), replacing "150 ms after a read starts". **§7.1:**
  0034 §5.1 cites "`header(col)` appends `zoneSuffix(col)`, as spec 0030 §7.1 does".
- **0016 (group 0015-22), §2.4 and §8.3:** `dashboards/audience` declares `Long = true` (0035
  §3.2); the §2.4 row should read "idempotent, long, not a mutation" and §8.3's long list follows
  the `Long` flag.

## Open questions

1. **Sequential chart ramp per scheme.** 0034 §7.7 still lists `sequential1`–`sequential7` as
   teal-100 → teal-800 in light and teal-900 → teal-200 in dark. Spec 0010 §B.8 states that a
   component group is emitted once and only `TmSchemeColors` switches by scheme, so these two
   values cannot be expressed. Options: a `sequential` ramp beside `categorical` in §B.8, or one
   ramp for both schemes. Decision 8 covers only the series, so I left it.
2. **`Numeric` arguments as numbers.** The codec and bar hold `Numeric` values as `number` (a
   `TmDecimal` past 15 digits), not as `TmDecimal` strings. Reason: a coded report's body record
   may declare `int` members (spec 0033 §11's `int? Customer`), and decision 18 makes only
   `decimal` and `long` accept strings. Alternative: `TmDecimal` everywhere, which requires coded
   report bodies to type `Numeric` parameters as `decimal` or `long`.
3. **The measure label in the calculation error.** 0034 resolves `{measure}` on the client from
   the draft or the held result, so a first run with no held result (or a widget) says "A
   calculation". Alternative: spec 0033 §8.11 adds a `label` argument in the request's content
   slot, as it does for `row` and `column` captions.
4. **Omitted totals in `widget` mode** use the notice strip, which overlays the tile's top 32 px
   until closed. Alternative: the tile's status line tooltip, or no message in a tile.
5. **Coded routes and `long`.** The brief said to keep `long: true` on hand-mapped coded routes.
   A coded report's `route` is always a projected `[ApiAction]` (spec 0033 §11, §12.1's
   allow-list), so 0034 §2.2 drops it; only a `run` hatch can reach a hand-mapped endpoint, and
   that hatch passes `long: true` itself.

## Fix pass

No file of 0010–0022 or `ARCHITECTURE.md` belongs to this group. Greps of both files for the
editor-notes.md §3 Removed names and `§F.CUT-` find nothing.

### 0034

- §3.3 "The notice strip": the `widget`-mode omitted-totals case removed; the strip holds a refused
  drill or "Preview is out of date" (V1-06, V8-13; R-27).
- §5.3 "Omitted totals": `widget` mode puts a short note on the tile's status line (spec 0035
  §8.5); a `widget`-mode host without one shows no message, and the report page explains (V1-07,
  V8-13; R-27).
- §14 Renderers: pins "the `totalsOmitted` footer and no notice strip for it in `widget` mode"
  (V1-08, V8-13; R-27).
- §7.7: the ramp reads the `sequential` group of spec 0010 §B.8 beside the series' `categorical`
  group; the `sequential1`–`sequential7` row reads `sequential.1` … `sequential.7` (step 1 the
  faintest) and lists no values; re-branding and fixed order cover the ramp (V2-08, V8-12; R-24).
- Context: the 0010 bullet names the `sequential` steps (R-24).
- Decision 13: the ramp reads the seven `sequential` steps; why reworded for both groups (R-24).
- §11 Columns: Owner's avatar `colorKey` is the record key, citing spec 0029 §12.2 (V2-09;
  decision 8).

### 0035

- §8.5: new "Omitted totals" bullet: "Updated 10:42 · Totals not shown"; the line's tooltip adds
  spec 0034 §5.3's footer sentence after the description; the header keeps its height (V1-09,
  V8-14; R-27). Chose "Totals not shown" over "Totals in the full report": the report page pages a
  summary at the same 1,000 rows, so it omits the same totals.
- §9.1 "Totals are the report's": the summary case cites the status line (§8.5) and states no
  notice strip on the tile (V1-09; R-27).
- §14 Rows: Owner's avatar `colorKey` is the record key, citing spec 0029 §12.2 (V2-09; decision 8).
- §17 Harnesses row: pins §8.5's status line with its omitted-totals note (R-27).

### Rulings confirmed with no edit

- R-6: neither file claims `min`/`max` over GUIDs.
- R-21: §3.3's calculation-error copy is label-free for an empty `pointer` ("A calculation", "in
  some rows").
- R-22: 0034 states nothing about the server's export streaming; nothing to align.
- R-28: 0034 §2.2 already says the coded route is `Long = true` and only a hand-mapped `run` hatch
  passes `long: true`.
- R-13, R-11, R-15: 0034 §3.5 and §4.4 treat `decimal` and `long` as strings; no generic id list
  is typed `number[]`; 0035 uses `TmLocalization.timeZone` as 0024 §7.1 defines it.

### Skipped findings

None.

### Unresolved for other groups

None. 0010 §B.8 already defines the `sequential` group that 0034 §7.7 now reads. 0024 §14's
coded-route wording (V8-03) is the 0024 group's finding.

## Readability pass

### 0034

- Context: the 0023 and 0010 citations split into two bullets.
- §3.1: "Transport" split into Transport, Paging and Long reads; `take` cites §5.1's page sizes.
- §3.2: the example split into two sentences.
- §3.3: the reloading bar's rule and the calculation error's reason as one-idea sub-bullets.
- §3.5: the `decimal` and `long` formatting bullet shortened.
- §4.4: "Numbers" split into sub-bullets.
- §5.3: "Totals" and "Omitted totals" split into sub-bullets.
- §6.5: "Totals are the server's" split into shorter sentences.
- §7.7: the opener keeps the groups; re-branding and fixed order move to a "Re-branding" bullet.
- §8.1: the leaf and subtotal drill lists as two bullets.
- §10.1: the hatch's inputs to the host get their own bullet.
- §11: the Owner avatar's `colorKey` leaves the column parenthesis for its own sentence.
- §12.7 step 2: the draft's four parts as a list.
- §14: Host harness and Renderers as sub-bullet lists; Codecs bullet wording.
- Decision 13: the why clause reworded.

### 0035

- §8.5: the failed-first-run bullet split; "Omitted totals" as sub-bullets.
- §9.1: "Totals are the report's" as pivot and summary sub-bullets, citing spec 0034 §5.3.
- §14: the Owner avatar's `colorKey` leaves the column parenthesis for its own sentence.
- §17: the Harnesses row's status-line clause reworded.
