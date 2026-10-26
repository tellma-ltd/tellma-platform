# Known cross-spec issues after authoring, with rulings

Binding, like `rulings.md` and `addenda.md`. The authors reported these while writing the specs.
Each line names the issue, the ruling, and the file(s) to change. The consistency checkers verify
each is resolved after the fix pass.

## Spec 0010

- **K1.** §D.7 quarter label through a `quarterPattern` option filled by `tmUi.l10n.quarter`;
  Ethiopic month 13 counts as quarter 4; §D.8 `timeStyle: 'none'`. Keep as written, with flags.
- **K3.** §B.6's menu-row rule (≥ 44 px below `sm`) belongs to the menu, which spec 0004 owns: move
  that half to §C.10 and leave §B.6 the form-field size rule, each citing the other. → 0010.
- **K21.** §E.2's multi-picker chip row is built from 0023's `tm-chip` and `TmChip.focus()`; its
  Backspace/Delete focus order follows 0023 §4 (next chip, else previous, else the input). → 0010.
- **K-F.** Part F's Goals/Non-goals bullets and Context "Other specs" list must cover Part F
  (spec 0012 §11.1–§11.2, 0013 §8.2, 0015 §5.2, 0029 §9, 0033, 0035 §6.3). → 0010.
- **K48.** `'uq'` operands: a path-free operand (a constant statement, a filter or select term
  without a column) compiles to the same `TOP (1)` month-map seek as a scalar subquery, which SQL
  Server allows in SELECT, WHERE and HAVING. `QX3104` applies only to a `'uq'` operand inside an
  aggregate's argument or a grouping key that is not a column path. So
  `startOfMonth(today(), 'uq')` works as a parameter default. → 0010 §F.13, 0033 (flag 6 goes).

## Specs 0011–0022 (existing)

- **K5.** Metadata chunks are `private, max-age=31536000, immutable` and require the session cookie
  (spec 0024 §6). Every mention in 0011 and 0016 matches. → 0011, 0016.
- **K7.** `Tellma:Copyright` (the About dialog's copyright line, read at emit) is a row in spec 0011
  §8's key table. → 0011.
- **A7 (addendum 7).** Spec 0011 §5.1's pipeline stamps `Tellma-Trace-Id` and `Tellma-Build` before
  any early refusal. → 0011.
- **K49 (addendum 4).** `core.report-seeds` is `Order` 900 in spec 0011 §6.2, with one sentence
  making 900+ the band for platform steps that must run after packs and the distribution. → 0011.
- **K11.** Spec 0013 §10.1 (and §7.6 where it lists label keys) gains the keys spec 0029 defines:
  `<Schema>_<Entity>_New` with the Core fallback `Entity_New`, `Entity_Level`, and the
  action-field label chain (spec 0029 §2.4, §4.3). → 0013.
- **K50.** Spec 0012 §5.2's `IDataBatch` gains the member that runs a constant statement compiled by
  `EvaluateConstant` (spec 0010 §F.11) and returns its one row; name it to match §F.11 and use it
  in 0033 §4.3. → 0012, 0033, 0010 §F.11 if the name is cited there.
- **A2 (addendum 2).** Spec 0014 §5.3's self-scope list gains `core.SupportRequest`; spec 0017 §5.2's
  `support-bundle` read rule follows (the submitter or a holder of `core.SupportRequest × Read`).
  → 0014, 0017.
- **A3 (addendum 3).** Spec 0015 §5.3 admits a computed extra (C#, from the loaded rows). → 0015.
- **K14.** Spec 0015 §11.2's `[EntityRef]` sentence says the client metadata carries the target per
  argument field (spec 0029's `TmActionMetadata.fields[].value.target`), which 0031 §10.4's
  generated dialog reads. → 0015.
- **K13.** Spec 0016 §11.5's `tellma_describe` renders from the server's entity metadata (store
  types and the default select included), not from the client entity model. → 0016.
- **K44 (addendum 5).** Spec 0018 §1.4 lists Acme's `[Assignable]` on `acme.SalesInvoice`, its
  `assign` and `post` entity actions, and a few demo assignments in the sample-data step. → 0018.
- **K43a.** Spec 0021's `core.Notification` stack: the inbox verbs stay on `InboxService`; nothing
  changes there (see K43 for the page). → none.

## New specs

- **K17.** Spec 0024 §10.3: `TmApi` delivers the body to the caller first and feeds observers one
  task later, as spec 0025 §5 relies on (own writes adopt before `observe` sees the header). → 0024.
- **K18.** Spec 0024 §7.2: after `users/me/locale` answers, the result is adopted into the `me`
  document through `TmCacheRefresher.adopt` (spec 0025 §5, §9.5). → 0024.
- **K31.** Spec 0024 §2.3's `tmOverride` example: Administration has order 900 (spec 0027 §3.2), so
  the example moves it to the front with `{ order: 10 }` and says so. → 0024.
- **K32.** Spec 0024 §7.1: `TmLocalization.translate(key, code)` has no consumer; delete it. Keep
  `load(code)` only if `setLanguage` or a named consumer uses it; otherwise delete it too. → 0024.
- **K38.** Spec 0024 §10.6: a `404 not-found` on a details save shows the deletion dialog of spec
  0031 §9.2, not the 400 dialog. → 0024.
- **K39.** Spec 0024 §9.2's route assembly sets a `canDeactivate` that runs the checks registered
  in the page scope (spec 0031 §11.3); the details host and the report studio use it. → 0024.
- **K33.** Spec 0028 §8.1's admitted toasts list "Pinned {label}." when the new pin has no visible
  chip (spec 0027 §8.5) and "Couldn't sign out…" (spec 0024 §8.4); both citations line up. → 0028,
  0027, 0024.
- **K24.** `tm-banner-bar` has no live role and no `announce` input; the banner host announces
  (spec 0028 §9). → 0028.
- **K22.** `tm-tiles` uses the grid's names: `rowId`, `activeRowId`, `rowActivate`, `rowClass`,
  `scrollToRow` (spec 0023 §8). → 0030 uses them everywhere.
- **K23.** `tm-pager` item mode takes `[total]` (with `totalIsLowerBound`). → 0031.
- **K15.** Spec 0031 §7.1 states that a path in `TmUploads.running` reports `pending`, so Save waits
  for uploads (spec 0029 §12.1). → 0031.
- **K37.** Spec 0031 §12.2 guards `snapshot.request === null` (a `searchRead` page): paging then
  stays within the snapshot's ids. → 0031.
- **K42.** Spec 0030 §7.2 states that the search host calls `rowLink` and `pageActions[].run` inside
  the page's injection context, so they may `inject()`. → 0030.
- **K43.** Non-entity bulk verbs: a `pageActions` entry may declare `selection: true`; it then shows
  in the selection bar's Actions menu and `run` receives the selected ids (spec 0030 §3, §12). The
  Notifications page uses it for "Mark as read" and "Clear" over `inbox/read` and `inbox/clear`.
  Opening a notification from the page or the bell goes through the route
  `/{t}/notifications/{id}/open` (a `tmPage` owned by spec 0028), which calls `inbox/read` for that
  id and replaces the URL with the notification's target; so a row and a bell item are real links
  that mark read even when opened in a new tab. → 0030, 0028 (flag 12 resolved), 0024 §9 route
  table if it lists 0028's routes.
- **K30.** Spec 0029 §18 telemetry values use ASCII hyphens (`2-10`, `1-4`) to fit spec 0026 §3.2's
  value grammar. → 0029.
- **K26.** `TmPageAction.confirm` is `{ titleKey, messageKey, args? }` (spec 0026 §9.4); 0024, 0031
  and 0034 use that shape. → as needed.
- **K12.** `tmSearchNormalize` and `TM_LAUNCHER_PROVIDERS` live in the `@tellma/core` primary
  (spec 0027); 0029's path picker cites it there. → 0029.
- **K-0032a.** Settings validation: spec 0031 §3.1's `TmDetailsSource` gains an optional
  `validate?(value): TmValidationProblem[]` used before `write`; Settings' checks use it. The
  step-up notice comes from `TmPermissions.sensitive` as on any details page. → 0031, 0032.
- **K-0032b.** Built-in schedules: `TmEntityDeclaration.details.canDelete?(row): boolean` is not
  added; instead the Schedule stack's `permissions` extra reports `Delete = false` for built-ins and
  0031's `can` reads it (spec 0031 §4), which hides Delete. → 0032, 0020 §7 if the extra is not
  declared there.
- **K-0034a.** The report studio is the details host's `page` hatch for both create and edit
  (`details: { page, create }`), so there is one save path. Spec 0031 §3.7 gains one rule: a `page`
  hatch declares `edit: 'page'` to say the page itself is the editor, which hides the host's
  `details.edit` action and in-place edit mode. → 0031, 0034.
- **K-0035a.** Spec 0033 §4.3's sentence on dashboards cites spec 0035 §5.3's full rule (a default,
  a fixed-value binding, or a required dashboard parameter). → 0033.
- **K-0035b.** Report and dashboard delete use the host's plain delete confirmation; spec 0033 §5.6's
  "reads the audience" sentence goes. → 0033.
- **K20.** Report and dashboard parameter preferences fall back to `{ v: 1, args: {} }` (spec 0025
  Appendix A); 0034 and 0035 use that. → 0034, 0035.
