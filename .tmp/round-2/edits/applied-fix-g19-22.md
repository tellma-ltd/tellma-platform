# Fix pass — specs 0019, 0020, 0021, 0022

One line per change, in the form "§target — what changed, in plain words — why (needed by spec 00NN)".
Finding ids follow each line.

## Spec 0019 (`docs/specs/0019-excel-codec.md`)

- Addendum 9 — verified resolved (§4.4 is the table writer in `Tellma.Core.Excel`; §12.2's label table gives `reports/export/start` the key `Core_Job_ExportReport` with `{ title }`); no change.
- §12.3 step 6 — verified it already raises `core.export.ready` with `fileName`, `rowCount` and `fileId`; the catalogue side is fixed in 0021 §2.3 (K-shell-entity-17); no change.

## Spec 0020 (`docs/specs/0020-background-jobs-and-scheduler.md`)

- §7.8 `ScheduleService` sketch — `Extras` is now `["state", "permissions"]` and `ContributeDetails` adds each extra when requested — needed by specs 0031 §4.1, 0032 §9, §12.3 (K-0032b, K-crud-3, F-8).
- §7.8 — new paragraph "The `permissions` extra": a computed extra (`DetailsPlan.AddComputedExtra`, spec 0015 §5.3) built in C# from the loaded rows, one row `{ id, save, delete, activate }` per visible id; all true on a user schedule; on a built-in `save` true and `delete`, `activate` false, matching `Schedules.BuiltInImmutable` and `Schedules.BuiltInAlwaysActive`; the Schedules page hides Delete from it — needed by spec 0032 §9, which hides Delete on a built-in through this extra (K-0032b, K-crud-3, F-8).
- §16 scheduler tests — the list gains the `permissions` extra, with `delete` and `activate` false on a built-in — needed by spec 0032 §9 (K-0032b).
- Decisions record 12 — says the band that marks a built-in is reported to the client through the `permissions` extra, so the record matches §7.8 — needed by spec 0032 §9 (K-0032b).
- Conflict: K-crud-3 derives `save`, `delete`, `activate` from the caller's decisions as 0033 §5.2 computes them (one statement per filtered decision); F-8 gives constants from the id (`save: true`, `delete` and `activate` = `Id > 999`). Followed F-8: addendum 3 defines a computed extra as C# from the loaded rows that costs no round trip, spec 0031 §4.1's `can` already ANDs the extra with the caller's grants, and K-0032b asks only for the built-in rule (a filtered schedule grant behaves as on any stack: the server refuses). `activate` is false on a built-in because §7.8 refuses `deactivate` there (`Schedules.BuiltInAlwaysActive`).
- Addendum 10 — verified resolved (no `activity` extra in 0020; §7.8 now holds `state` and, per K-0032b, which takes precedence, `permissions`).

## Spec 0021 (`docs/specs/0021-notifications-inbox-and-hub.md`)

- §2.3 Core catalogue, `core.export.ready` row — the arguments are now `{ fileName, rowCount, fileId }` — needed by specs 0019 §12.3 (step 6 raises `fileId`) and 0028 §6.2 (the Download link) (K-shell-entity-17).
- §5.4 `inbox/read` bullet — it no longer says opening a row navigates to its target or "nowhere for a target-less type"; a row now opens through spec 0028 §10.2's open route, which marks it read through `inbox/read` — needed by spec 0028 §10.2 (K43; the route also sends a target-less row to the Notifications page, which the old sentence contradicted).
- Addendum 6 — verified resolved (§2.5 serialises `notificationChannels` as `{ key, defaultEnabled }[]`); no change.
- K43a — verified: the inbox verbs stay on `InboxService` (§5.2); no change.
- K-reports-35 — verified the `core.report.shared` and `core.dashboard.shared` rows exist in §2.3, so 0033 §7 and 0035 §7.2 can cite them; no change here.

## Spec 0022 (`docs/specs/0022-email-outbox.md`)

- No known issue, addendum or review finding names a fix in 0022 (K-crud-29 touches only 0032's Context); no change.

## ARCHITECTURE.md

- No change: it states none of the facts changed above.
