# Fix log — specs 0033, 0034 and 0035

## 0033 (`docs/specs/0033-report-definitions-and-engine.md`)

- K-reports-1 / F-10 (K48) — §4.3 Grammar: `startOfMonth(today(), 'uq')` added as a default example; "a table calendar is QX3104 (§8.7)" removed. §8.7: operand rules and QX3104 cited to spec 0010 §F.13; the path-free refusal removed. Review flag 6 (`'uq'` only over columns) deleted.
- K-reports-2 / F-1 (K50) — §4.3 Evaluation and §8.3 step 3 run defaults through `IDataBatch.EvaluateConstant` (spec 0012 §5.2). Context: 0012 line gains §5.2. Conflict: F-1 names the member `Constant`; took K-reports-2's `EvaluateConstant`, as K50 says "name it to match §F.11" and 0012 §5.2 (already fixed) declares `EvaluateConstant`.
- K-reports-3 / F-22 (K-0035a) — §4.3 When it applies: "Dashboards hide parameters…" → spec 0035 §5.3's full rule (a default, a non-null fixed-value binding or a required dashboard parameter).
- K-reports-4 / F-11 (K-0035b) — §5.6: "after a warning that reads the audience" → "after the host's plain confirmation (spec 0034 §10.3)". Decision 12: "after a warning" → "after the host's confirmation".
- K-reports-7 / F-2 (addendum 3) — §5.2: the `permissions` extra cites `DetailsPlan.AddComputedExtra` (spec 0015 §5.3). Context: 0015 line gains §5.3, computed extras. Conflict: K-reports-7 cites the section only, F-2 the member; took F-2, since both name it `AddComputedExtra`.
- K-reports-9 (ruling 2) — §9.4: drill URL and example drop `&from=…`.
- K-reports-10 — §3.1: `dateStyle` is `'numeric' | 'medium' | 'long'`, spec 0005 §2.2's names.
- K-reports-23 — §6.2 step 2: permissions on the resource (`core.ReportDefinition`, `core.Dashboard`) or `*`; step 3 runs for `core.ReportDefinition` only. Step 2 reflowed.
- K-reports-24 — §9.2: `ReportRowIdentity` gains `int? ChildIdOrdinal`; Identity bullet states the grandchild URL. §8.4: a grandchild selects its parent's key and its top-level owner's.
- K-reports-25 — §12.4: "Four styles plus the phantom" → "The row styles are spec 0019 §4.4's `ExcelRowStyle`".
- K-reports-33 — §4.4: the omitted-parameter bullet and its six-row table → one bullet citing spec 0010 §F.10 with the measure and `if` examples. §8.5 step 5 cites spec 0010 §F.8, restatement cut. Review flag 3 (`Having` finest set) deleted.
- K-reports-1 / K-reports-33 — Review flags renumbered: old 4, 5, 7–12 → 3–10. New flag 3 (omitted parameter in `or`) cites "§4.4, spec 0010 §F.10", since §4.4's table moved out. No spec cites 0033's flag numbers.
- K-reports-34 — §4.4 URL form: "Spec 0034 §4.4 carries arguments in the URL."
- K-reports-35 — §7 opener: the catalogue row's restatement → "The catalogue row is spec 0021 §2.3's; this section states what the raiser decides."

## 0034 (`docs/specs/0034-report-renderers-and-studio.md`)

- K-0034a / F-6 / K-reports-5 — one save path for the studio. Conflict: K-reports-5 kept two save paths pending a ruling; took F-6, as K-0034a says "so there is one save path". Changes:
  - §2.3: `details` gains `edit: 'page'` with a comment; the studio `tmPage` loads `TmReportStudioPage`.
  - §1: `/report-studio` exports `TmReportStudio`, `TmReportStudioPage`.
  - §10.2: "No in-place edit" bullet cites `edit: 'page'` (spec 0031 §3.7) instead of review flag 11.
  - §12 opener: the studio is the details host's body in both modes.
  - §12.1 (renamed "Routes and hosting"): the host owns header, Save, Cancel, drafts, leave guard, conflict and deleted dialogs in both modes. The edit page renders `tm-details-page` with `TmReportStudio` as its layout, over `tmEntitySource` with the studio draft key (kept, so spec 0032 §8's pointer holds). The studio calls `edit()` whenever the host is in view mode, `ready` and `can.edit`. Read-only stays in view mode; Clone makes the copy.
  - §12.9 (renamed "Save, Cancel, Share"): the studio's own `reports/save`, Save as and Discard removed. Save and Cancel are the host's. "Report saved." on a create's save (rulings' toast policy; spec 0028 §8.1 cites §12.9). 422 at `DefinitionJson` mapped by `pointer` through `registerField`. Share notes the `ModifiedAt` stamp and the concurrency dialog.
  - §10.4 Close: the report page's host adopts the outcome (spec 0031 §10.4); "an open studio" dropped.
  - §13: the studio registers `studio` in both modes; actions `studio.preview` and `studio.share` only. `studio.save`, `studio.save-as` and `studio.discard` removed.
  - §14 studio harness row: create and edit through the host, 422 by `pointer`, edit mode re-entered.
  - §15 telemetry: `tm.studio.save` removed (the host records `tm.details.save`).
  - Context: 0031 line cites §3.1, §3.7, §8, §11.
  - Decision 17 rewritten. Review flag 11 rewritten: the alternative is the studio as the report page's edit mode.
- K-reports-9 / K-crud-10 (ruling 2) — §8.1 step 4: no `from` in the URL. §8.2: state carries `{ from, fromUrl, fromTitle, chips }`. §8.3 opener: the source report comes from `history.state.tmDrill`. §8.4: a bare link's crumb reads "Reports" and links to `/{t}/reports`. §13 `search` row: `from` from `history.state.tmDrill`. Conflict: K-crud-10 keeps a "Report" crumb; took K-reports-9's "Reports" link, which has a target.
- K-reports-12 — §12.3: "rule 9's range" → "rule 8's range".
- K-reports-13 — §6.3: the data cell's style order stated in place (`total`, `subtotal-1`, `subtotal-2`, `leaf`), not "spec 0033 §12.3's ladder".
- K-reports-15 — §5.1: bare `§C.6`, `§C.7` → "spec 0010 §C.6", "spec 0010 §C.7".
- K-reports-17 — §15 Non-blocking row: "spec 0033's `run-cancel.spec`" → "spec 0033 §15's `runs` suite ("an abort reaches SQL")".
- K-reports-18 — §3.5: `display` and `format` through `TmL10n` (spec 0005 §2.2's `formatNumber`, `formatDate`; spec 0010 §D.4). The Values bullet split into sub-bullets.
- K-reports-19 — §3.2 example echoes `From` (`Default`) and `To` (`Omitted`). §4.4 example → `?p.To=2026-06-30`.
- K-reports-20 — §7.9: "A chart type the facade does not draw (only a coded report can return one)".
- K-reports-24 — §5.2: "(a grandchild: the intermediate child's id)".
- K-reports-27 — §1 `/reports` row gains `TmReportOverlay`.
- K-reports-34 — §4.4 Text: the platform scalar encoding (spec 0016 §3.2) without JSON quotes; §4.1's table shows each type.
- K-reports-37 / K-crud-19 — §8.3 Back: "Pins and recents: spec 0030 §17."
- K-reports-38 — Decision 12: "as the design, Excel and AG Charts do" → "as Excel and AG Charts do".
- K-foundation-26 — §4.2: fields are "46 px (`lg`)" tall (spec 0010 §B.6).
- K-reports-21 — skipped: §12.9's own save is gone (F-6); the host's save calls `markStale({ tenantId, entity })` (spec 0031 §8.3).

## 0035 (`docs/specs/0035-dashboards-and-home-page.md`)

- K-reports-4 (K-0035b) — Review flag 11: alternative cites spec 0034's review flag 12, not 0033 §5.6.
- K-reports-7 / F-2 (addendum 3) — §6.1: the `view` extra cites `DetailsPlan.AddComputedExtra` (spec 0015 §5.3). Context gains "spec 0015 §5.3 — computed extras".
- K-reports-16 — §15.6 → §15.5; Non-goals pointer follows.
- K-reports-26 — §15.2 Date line: `formatDateTime(new Date(now).toISOString(), …)`.
- K-reports-27 — §12.1 table: `tmParseDashboard` → `tmDashboardModel` (§8.1). §1.1 main module exports `tmDashboardModel`, `TmTileState`, `TmPlacement`.
- K-reports-31 — §1.1 Server list and §4.1 "The emit" row name `WidgetTypeClientAssetContributor` (spec 0024 §3.2). Context 0024 line gains §3.2.
- K-reports-35 — §7.2 opener: cites spec 0033 §7 and spec 0021 §2.3 without restating the row.
- K-reports-36 — §10.3 The bar and Precedence rows: restatements of spec 0034 §4.2 and spec 0025 Appendix A cut.
- K-reports-38 — Decision 6 drops "Dashboards Q1:"; §8.1 band drops "as the design's home body"; §15.2 "No location suffix follows the date; a `home` hatch can add one"; §15.3 "two columns (7 : 5)"; Decision 13 "Alternative: 5 and 2 rows."; §18 "the parts the specs under Context carry are in place".
- K-foundation-26 — §15.3: "(spec 0010 §B.6)" dropped from the 44 px list-row rule (§B.6 covers controls, not list rows).
- K-reports-28 — skipped: F-18 is applied in spec 0031 §3.7 (`page` renders create mode when there is no `create`), so §12.1's declaration and its "view, edit and create" comment stand.

## Checked, no change needed in these files

- K20 (0034 §4.5, 0035 §10.3 cite spec 0025 Appendix A), K49 / addendum 4 (0033 §13), addenda 1, 8, 9: already resolved.
- K26: 0034 declares no `confirm`; `studio.discard` is gone.
- Fixes owned by other files: K-reports-6, -8, -11, -14, -22, -29, -30, -32; K-crud-4 (0031, 0029); K-shell-entity-5, -37; F-16, F-17, F-24.

## Notes for other owners

- Spec 0015 §5.3 must declare `DetailsPlan.AddComputedExtra` (addendum 3); 0033 §5.2 and 0035 §6.1 cite it by name. It was not declared when this pass ran.
- Spec 0031's sketch comments on `TmDetailsDialogs` (§9) and `tmLeaveGuard` (§11.3) say the report studio calls them directly; it now gets both through `tm-details-page` (0034 §12.1).
