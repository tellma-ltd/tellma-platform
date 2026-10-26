# Fix log — specs 0031 and 0032

## 0031 (`docs/specs/0031-details-page.md`)

- K-0032a / K-crud-2 / F-9 — §3.1 sketch: added `validate?(model)`, `saveSecurable?` (a `TmSecurableRef`, spec 0024 §2.2) and `interface TmValidationProblem { path, code, arguments? }`; entity-source bullet: its `saveSecurable` is `[entity, 'Save']`. Deviation from K-crud-2: absent or null means no notice (not a default of `[entity, 'Save']`), so My profile, whose `me/save` is self-service while `core.User × Save` is sensitive, shows no false notice.
- K-0032a / K-crud-2 — §5.3: the strip's priority list gains the step-up notice; new bullet "The step-up notice" (edit and create modes, `TmPermissions.sensitive` on the source's `saveSecurable`).
- K-0032a / K-crud-2 / F-9 — §8.1: diagram step "clear errors, source checks, submit"; step 3 runs `source.validate?.(model())`, records problems as §8.5 does, then `submit`; §7.1 table row "server errors" → "server and source errors" (§8.5; §8.1 step 3).
- K-crud-12 — §5.3: a notice is spec 0005 §13's `tm-alert` plus the strip's own "Dismiss" icon button (`tm-alert` has none); never focused.
- K-0034a / K-crud-4 / F-6 — §3.7 opener `details: { page, create?, edit? }`; new bullet `edit: 'page'` (no `details.edit`, no in-place edit mode, create mode unchanged, `can.edit` still reaches the page; the report page declares it, spec 0034 §2.3); §10.1 `details.edit` row "never registered with `edit: 'page'` (§3.7)". Conflict: F-6 also drops `details.save`/`details.cancel`; not taken, because K-0034a names only `details.edit` and in-place edit, and create mode saves through the host.
- F-18 — §3.7 `page` bullet: `page` renders create mode when there is no `create`.
- K-reports-29 — §3.7: a hatch may register `dashboard`, `report` or `studio`.
- F-8 — §4.1 `can` bullet: cites the Schedule stack's `permissions` extra (spec 0020 §7.8) beside spec 0033 §5.2.
- K-crud-11 — Non-goals: "per-row capability flags (the server exposes none)" → "beyond a stack's `permissions` extra (§4.1)".
- K-crud-13 — §3.1 sketch: added `category?: Signal<string>`; §16 opener: a source page publishes `settings` with `category` from the source.
- K-crud-30 — §16 table `state` row cites spec 0026 §9.3's `details` minimum and defines only `tab` and `child`.
- K-crud-21 — §6.9 `tm-details-audit` renumbered §6.8 (0023 §7.1 already cites §6.8).
- K23 / K-crud-24 — §12.1 step 3: `tm-pager mode="item"` (spec 0023 §8.1) with `[index]`, `[total]="result.total"`, `[totalIsLowerBound]="result.capped"`.
- K37 / K-crud-5 (covers F-19 on the 0031 side) — §12.2: edge rule only when `snapshot.request` is not null; new bullet "Without a request" (pages within `ids`, end arrows disabled); step 3 pointer "spec 0030 §15.3" → "spec 0030 §4.2".
- K39 / K-crud-7 — §11.3: "The page route's `canDeactivate` (spec 0024 §9.2)"; Context's 0024 bullet names the page route's `canDeactivate`.
- K-crud-8 — §18 What Acme adds: "`my-center-details` (spec 0032 §6)".
- K14 — §10.4 generated dialog: "a picker when `value.target` is set" (spec 0029 §2.2's field descriptor); 0015's half is another fixer's.
- K-crud-34 — Decisions 1, 9, 12 and Review flags 1, 7, 12: question ids, "the brief" and "the design's" removed; flags 1, 7, 12 state plain alternatives.
- §17 — unit/harness rows pin `request: null` paging, a source page's `validate` problems and step-up notice, and an `edit: 'page'` hatch (keeps Testing in line with the body).
- K15, K26, K38 — verified consistent; no change.

## 0032 (`docs/specs/0032-stock-pages.md`)

- K-0032a / K-crud-2 / F-9 — §7.2 table: rows `validate` (§7.6 step 2), `saveSecurable` (`['core.Settings.General', 'Save']` while General is selected, else `null`); §7.5 Step-up notice → spec 0031 §5.3's, through `saveSecurable` (spec 0013 §5.5); §7.6 step 2 → the source's `validate` reports `Required` and `Settings.EntryTypeMismatch` at `entries.<key>`; Review flag 6 deleted, flags 7–15 renumbered 6–14.
- K-0032a (consequence) — §8 Header bullet: `me/save` is self-service (spec 0018 §3.5), so the source declares no `saveSecurable`; §14 Stable layout row: no reserved step-up box, the notice sits in spec 0031's overlaying strip.
- K-0032b / K-crud-3 / F-8 — §9 opener hides Delete too; Built-in bullet: Delete hides through the stack's `permissions` extra (spec 0020 §7.8) read by spec 0031 §4.1's `can`; §12.3 new bullet for the `permissions` extra; Review flag 16 deleted; §13 e2e row pins the hidden Delete. Conflict: F-8 also sets `activate` false and drops `visible` on `deactivate`; not taken, since K-0032b names Delete only; §3's `visible` keeps hiding Deactivate.
- A3 / K-crud-28 / F-2 (0032 part) — §5.5: `problems` is a computed extra (spec 0015 §5.3), computed in C#.
- K-crud-8 — §6: `Manager` sentence removed from Data; new Example of Acme's `centersOverride` and `my-center-details` over `MyCenter.ManagerId` (spec 0018 §1.4); §14 pointer → §6; §2 table row cites §6 beside spec 0024 §2.3.
- K-crud-9 — §14 What Acme adds: e2e fixtures create the users and roles; drift from a `Name3` filter saved under three languages before the tertiary language is removed.
- K-crud-13 — §7.2 table `category` row (the selected tab, §7.1); §8 table `category` row (`'profile'`, spec 0026 §9.3); §8 Header's page-context sentence removed (now in the table); §14 Page context bullet cites spec 0031 §16 and §7.2, §8 instead of restating.
- K-crud-14 — §8 `draftKey` → `TmDrafts.key(t, 'core.User/me', me.user.id)`.
- K-crud-22 — §3 `visible` bullet: "spec 0030 §12.3" → "spec 0030 §12.2".
- K-crud-23 — §8 Language bullet: "spec 0024 §7.2" → "spec 0024 §7.1".
- K-crud-26 — §5.3: `rows="4"` → `[rows]="4"`; Save errors at `entities[0].permissions[j]…` (spec 0016 §7.3; codes per spec 0014 §3.7).
- K-crud-27 — §4.3 502 row: `ids: errorDetails.failed.map(Number)`, with the strings note (spec 0018 §3.3).
- K-crud-29 — Context: the six server-spec bullets (0013, 0014, 0016, 0018, 0020, 0022) collapse to one pointing at §12; the 0031 bullet adds §5.3's step-up notice.
- A10 — verified (`activity` extra on the users stack, §12.2); no change.
