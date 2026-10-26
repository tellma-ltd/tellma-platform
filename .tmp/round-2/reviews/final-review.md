# Final holistic review — round-2 spec family (0010 B–F, 0023–0035, edits to 0011–0022)

Reviewed 2026-10-20 against the brief, `design/00-architecture.md`, `reviews/rulings.md`,
`reviews/addenda.md` and `reviews/known-issues.md` (precedence as stated). Every new spec was read
in full; 0010 Parts B–F in full (Part A skimmed); 0011–0022 consulted where the new specs cite them.

The nine end-to-end scenarios were walked through the specs as written. Findings are numbered
F-<n>, ordered by severity, then by the scenario that exposes them. Severity: **blocker** = the specs,
read together, cannot be implemented or would not work; **major** = a binding ruling is unapplied,
or two specs disagree on a contract an implementer must build; **minor** = a wording or
cross-reference defect an implementer could resolve alone, but should not have to.

Counts: 3 blockers, 10 majors, 14 minors (27 findings).

---

## Blockers

### F-1 (blocker) — The report run path needs an `IDataBatch` member nobody defines (K50 unapplied)

**Specs:** `docs/specs/0012-data-access-layer.md` §5.2; `docs/specs/0033-report-definitions-and-engine.md`
§4.3, §8.3; `docs/specs/0010-amendments-to-implemented-specs.md` §F.11.

**Problem.** A stored report whose parameters have defaults runs a *constant statement* first. The
engine only compiles it; the host must execute it inside the Read batch. `IDataBatch` offers no
member that runs a compiled constant statement, and the two members it has cannot: `Rows` takes a
`QuerySpec` (a root query, not a path-free statement) and `Sql` takes host `FormattableString` text
and cannot bind the engine's context slots (`@qx1_p0` = `Today`, `@qx1_p1` = `UserId`). Scenario 5
(a pivot with `"default": "year(today())"`) therefore has no implementable run path.

**Evidence.**
- 0012 §5.2 lists only `BatchResult<RowPage> Rows(QuerySpec spec, QueryArguments? arguments, RowQueryOptions? options);`
  and `BatchResult<RawResult> Sql(FormattableString sql, SqlOptions? options);` — no constant member
  (grep for `EvaluateConstant|Constant` in 0012 returns nothing).
- 0033 §4.3: "the run executes one constant statement (spec 0010 §F.11) holding every applied
  default, binds the values as ordinary arguments and echoes them" — no member named.
- 0033 §8.3 step 3: "compile them with `EvaluateConstant` … and run the statement in one Read round
  trip."
- 0010 §F.11: "The engine compiles it and the host executes it, as with `CompileQuery`." and "A host
  binds the context slots from the same values as the query that uses the result".
- known-issues K50: "Spec 0012 §5.2's `IDataBatch` gains the member … name it to match §F.11 and use it
  in 0033 §4.3." — not done.

**Fix.**
1. 0012 §5.2, `IDataBatch` sketch: add
   `BatchResult<RawResult> Constant(IReadOnlyList<ConstantItem> items, ConstantCompilationOptions? options);`
   with one paragraph: compiles through `QueryexEngine.EvaluateConstant` under `BatchOrdinal = b`
   and the batch's `LanguageVersion` (or the options'), binds the `Today`, `Now`, `UserId` and
   `TimeZone` slots exactly as `Rows` binds them (§11.3), appends the one-row `SELECT`, result set
   = one row of the items' values.
2. 0033 §4.3 "Evaluation" bullet and §8.3 step 3: "through `IDataBatch.Constant` (spec 0012 §5.2)".
3. 0010 §F.11 "Slots" bullet: cite `IDataBatch.Constant` as the host member that binds them.

### F-2 (blocker) — Computed extras are relied on by three specs but 0015 §5.3 does not admit them (addendum 3 unapplied)

**Specs:** `docs/specs/0015-crud-service-pipeline.md` §5.3; `docs/specs/0033-report-definitions-and-engine.md`
§5.2; `docs/specs/0035-dashboards-and-home-page.md` §6.1; `docs/specs/0032-stock-pages.md` §5.5, §12.2;
`docs/specs/0018-core-and-gl-reference-stacks.md` §4.3.

**Problem.** The `permissions` extra on `reports/get` and `dashboards/get`, the dashboard `view`
extra and the Role stack's `problems` extra are all described as computed in C# from rows the read
already loaded. Spec 0015 §5.3 defines extras only as SQL statements appended to the details plan
(`DetailsPlan.AddExtra(name, sql | spec)`); there is no contract for a contributor that returns a
value computed after materialisation. Scenario 5's share dialog, the report page's `permissions.save`
gate (Edit button) and the dashboard page cannot be built against 0015 as written.

**Evidence.**
- 0015 §5.3 step 3: "append statements through `DetailsPlan.AddExtra` (raw SQL … or a `QuerySpec`;
  either may join `IdsSource`) … the result set becomes `EntitiesResult.Extras[name]`" — SQL only;
  the `DetailsPlan` sketch has only the two `AddExtra` overloads.
- 0033 §5.2: "It is a computed extra (spec 0015 §5.3): `Unrestricted` is true and `Denied` false for
  every row; each `Filtered` decision adds one details-batch statement".
- 0035 §6.1: "That is two statements on the details batch, shaped in C# as a computed extra (spec
  0015 §5.3)".
- 0032 §5.5: "The eager `problems` extra (spec 0018 §4.3) lists the role's drifted permissions with
  the details read, computed from rows the read already loads."
- addenda 3: "Spec 0015 §5.3 admits an extra computed in C# by the stack's service from the loaded
  rows, when SQL cannot express it. It costs no round trip unless it reads." — not applied.

**Fix.**
1. 0015 §5.3: add a step 3b "**Computed extras.** `DetailsPlan.AddComputedExtra<TValue>(string name,
   Func<DetailsResult<TEntity>, TValue> compute)` registers a value computed after the batch
   materialises, from the entities, their related rows and any result sets the same contributor
   appended with `AddExtra`; it adds no round trip. It becomes `EntitiesResult.Extras[name]`
   serialised like any value." Add the member to the `DetailsPlan<TEntity>` sketch and state that
   `IDetailsContributor<T>.Extras` covers both forms.
2. 0018 §4.3 (`members`, `problems`) and 0033 §5.2, 0035 §6.1: cite the new member by name.

### F-3 (blocker) — On a cold tenant entry, pages evaluate permissions and shape before `me` and `settings` exist

**Specs:** `docs/specs/0024-client-app-foundation.md` §4.4; `docs/specs/0025-client-state-and-cross-tab-sync.md`
§3.2; `docs/specs/0027-shell-launcher-and-tenant-picker.md` §7.5; `docs/specs/0029-client-entity-model.md`
§4.1, §7; `docs/specs/0031-details-page.md` §3.1, §3.5; `docs/specs/0030-search-page.md` §5.4.

**Problem.** Scenario 1 (cold launch → Connect → `/{t}` and every cold deep link). The route host
creates the page host as soon as the Entities chunk is loaded; the cold `client/sync` runs *beside*
the page. But every permission signal is false until the first `me` arrives, and descriptors read the
tenant's shape from `settings`. So on a cold entry:
- a details route evaluates `allows(entity, 'details')` → false → renders the `forbidden` state and
  never starts its read; nothing in 0031 re-triggers the read when `me` lands, so a cold deep link to
  `/17/centers/5` sticks on "You don't have access" until the user presses Refresh;
- a search route drops every column as `unreachable`, sends an `Id`-only select, then re-plans and
  re-reads when the tags move (0030 §5.4) — a wasted read and a visible flash on every cold launch;
- a `tmPage` with a `securable` renders the forbidden view first (0024 §2.4).

**Evidence.**
- 0029 §7: "Before the first `me`, all is false."
- 0029 §4.1: "`get()` reads the shape from `TmLaunchCache.docs.settings`".
- 0025 §3.2 cold row: "a foreground `refresh(t, ['settings', 'me', 'tenant'])` (§5.3) starts beside
  the page's data request; the shell shows final-geometry skeletons until the documents are written".
- 0027 §7.5 cold row, `main`: "the route host; page data starts beside the sync".
- 0024 §4.4 Entity routes: "awaits `TmEntities.load()` beside the page chunk … and loads
  `TmSearchHost` or `TmDetailsHost`" — no wait on the launch documents; `TmLaunchCache.warm` is
  defined in 0025 §3.1 and read by no spec.
- 0031 §3.1: "a user without `Read` sees the `forbidden` state (§3.5)"; §3.5 lists no recovery when
  access arrives later.
- 0029 §6.4: "A key that does not resolve (`unknown`) or that `TmPermissions.reachable` refuses
  (`unreachable`) stays out of `select`".

**Fix.**
1. 0024 §4.4 "Entity routes" and "Page scope" rows: "On a cold tenant entry (`TmLaunchCache.warm()`
   false) the route host keeps the skeleton and awaits the cold refresh of 0025 §3.2 before creating
   the page host; the page's data read starts after it. On a warm entry nothing is awaited." Add the
   same sentence for `tmPage` routes with a `securable` (§2.4).
2. 0025 §3.2 cold row: "starts before the page host is created; the page's data request follows the
   documents (spec 0024 §4.4)"; the failure rows stand.
3. 0027 §7.5 cold row, `main`: "the route host's skeleton until the sync answers, then the page".
4. 0029 §7: after "Before the first `me`, all is false." add "The route host never creates a page
   host before the first `me` and `settings` (spec 0024 §4.4), so hosts never see that state."
5. 0024 §14 warm-launch DoD: keep; add a cold-launch pin: "a cold deep link to a details page renders
   the record, never a forbidden flash" (`cold-deep-link.spec`).

---

## Majors

### F-4 (major) — The user partition is never read on a warm launch, so `distribution` and `tenants` are absent from the mirror

**Specs:** `docs/specs/0025-client-state-and-cross-tab-sync.md` §2.1, §2.3, §2.6; `docs/specs/0024-client-app-foundation.md`
§4.3, §9.3; `docs/specs/0027-shell-launcher-and-tenant-picker.md` §6.2, §10.1, §11.1, §12.2;
`docs/specs/0026-client-observability-and-support.md` §3.4, §12.1; `docs/specs/0032-stock-pages.md` §8.

**Problem.** The mirror that `doc()` signals read is filled only by `readAll`, and the boot step runs
`readAll` for the tenant partition only ("the warm path's only store access"). The `distribution`
and `tenants` documents live in the user partition, so on a warm launch `TmLaunchCache.distribution()`
and `.tenants()` are `undefined` until the idle revalidation fetches them from the network. That
breaks rule 2A for: the session menu's "My account" row (hidden until then), About's links and
server version, My profile's account link, `TmSupport.canSend` and the telemetry defaults, the launch
redirect's "stale while revalidating" read, and the picker's "cached list in the first frame".

**Evidence.**
- 0025 §2.1: "**The mirror** is filled by `readAll`, `put` (before it commits), `patch` and other
  tabs' `store.changed`".
- 0025 §2.6: "Then, on a tenant path, it runs `readAll` of P's tenant partition, the warm path's only
  store access".
- 0025 §2.3: `tenants` and `distribution` are in the **user** partition, "stale-while-revalidate".
- 0027 §10.1 row 3 "My account … Shown when: the distribution document is cached"; §12.2 "The About
  dialog reads them from `TmLaunchCache.distribution`"; §6.2 "A cached list: the list in the first
  frame".
- 0024 §9.3: "read 0025's `tenants` document (`/bff/user`, stale while revalidating)".
- 0026 §12.1 `canSend`: "support is enabled …"; §14 DoD: "telemetry settings come from the cached
  `distribution` document".

**Fix.**
1. 0025 §2.6, the boot step: "Then it runs one `readAll` of P's user partition and, on a tenant
   path, one of P's tenant partition, in one `readonly` transaction (two key ranges), the warm path's
   only store access." Either extend the sketch to `readAll(partitions: readonly TmPartition[])` or
   state that the two reads share one transaction.
2. 0025 §2.3 `tenants`/`distribution` rows: add "read by the boot step (§2.6)".
3. 0024 §4.3 step 2: "runs its `readAll` of the user partition and, on a tenant path, the tenant
   partition (0025 §2.6)".

### F-5 (major) — K43 (Notifications page selection actions and the `/notifications/{id}/open` route) is unapplied in 0028, 0030 and 0024

**Specs:** `docs/specs/0028-assignments-and-attention-surfaces.md` §6.2, §6.5, §10.2, Review flag 12;
`docs/specs/0030-search-page.md` §3.1, §12.2; `docs/specs/0024-client-app-foundation.md` §9.2.

**Problem.** The ruling makes a notification row and a bell item real links that mark read even when
opened in a new tab, and gives the Notifications page selection-scoped "Mark as read" and "Clear".
The specs still describe the opposite: opening marks nothing, the bell calls `read([id])` in the
click (so Mod+click/middle-click opens the target unread), the page has only read-all/clear-read,
`TmSearchPageActionDeclaration` has no `selection` member, and the route is nowhere. Scenario 6's
"open from a new tab" path is lost.

**Evidence.**
- known-issues K43: "a `pageActions` entry may declare `selection: true`; … The Notifications page
  uses it for 'Mark as read' and 'Clear' over `inbox/read` and `inbox/clear`. Opening a notification
  … goes through the route `/{t}/notifications/{id}/open` (a `tmPage` owned by spec 0028) … so a row
  and a bell item are real links that mark read even when opened in a new tab. → 0030, 0028 (flag 12
  resolved), 0024 §9 route table".
- 0028 §6.5: "There is none. Visiting a notification's target never marks it read".
- 0028 §6.2: "**Activation** (click, Enter) calls `read([id])` when the row is unread, closes the
  panel and navigates to `TmUrls.target(…)`".
- 0028 §10.2: "Opening a row follows its link and marks nothing read … so the page has no … bulk
  actions"; flag 12: "has no bulk Mark as read or Clear".
- 0030 §3.1 `TmSearchPageActionDeclaration`: `id`, `labelKey`, `icon`, `group`, `order`,
  `destructive`, `confirm`, `requires`, `run(search)` — no `selection`.
- 0024 §9.2 route table: no `notifications/:id/open`.

**Fix.**
1. 0030 §3.1: add `readonly selection?: boolean; // true: shown in the selection bar's Actions menu; run receives the selected ids`
   and `run(search: TmSearchContext, ids?: readonly number[]): Promise<void>`; §12.2 Actions-menu
   table gains a row "page actions with `selection`".
2. 0028 §10.2: replace the two actions with `attention.notifications.read` and
   `attention.notifications.clear` (`selection: true`, over `inbox/read { ids }` and
   `inbox/clear { ids }`), keep `read-all` and `clear-read`; `rowLink` returns
   `urls.page('notifications/' + id + '/open')`; add the core `tmPage('notifications/:id/open', …)`
   whose component calls `TmApi.write('inbox/read', { ids: [id] })` (ignoring a 404) and replaces the
   URL with `TmUrls.target(…)`, else the Notifications page. §6.2 "Activation": rows are
   `<a href>` to that route; §6.5 becomes "a row is read when opened through its link"; flag 12 goes.
3. 0024 §9.2: list `notifications/:id/open` (0028) among the explicit tenant routes, before
   `:segment/:id`; `TmUrls.describe` kind `page`, not pinnable.

### F-6 (major) — K-0034a (`edit: 'page'` on the `page` hatch; one save path for the studio) is unapplied

**Specs:** `docs/specs/0031-details-page.md` §3.7; `docs/specs/0034-report-renderers-and-studio.md`
§2.3, §10.2, §12.1, §12.9, Review flag 11.

**Problem.** The report page is a `page` hatch, yet it must not show the host's `details.edit`
(definitions are edited in the studio). 0031 gives a hatch no way to say so; 0034 §10.2 says "the
host must omit it here" and flag 11 is still open. The studio also keeps two save paths (create
through the host, edit through its own `reports/save`), which the ruling closed. Scenario 5's
"author … in the studio" has an Edit button on the report page whose behaviour depends on an
undefined host rule.

**Evidence.**
- known-issues K-0034a: "there is one save path. Spec 0031 §3.7 gains one rule: a `page` hatch
  declares `edit: 'page'` to say the page itself is the editor, which hides the host's
  `details.edit` action and in-place edit mode. → 0031, 0034".
- 0031 §3.7: "`details: { page, create? }` … The host keeps the header, toolbar, pager, page actions,
  drafts, leave guard and page context." — no `edit` member.
- 0034 §10.2: "**No in-place edit:** definitions are edited in the studio, so the host's
  `details.edit` must not show on this hatch (review flag 11)".
- 0034 flag 11: "yet spec 0031 §3.7 keeps the host's `details.edit` on every `page` hatch; the host
  must omit it here."
- 0034 §12.1 table, Edit column: "the studio's own … Save calls `TmApi.write('reports/save', …)`".

**Fix.**
1. 0031 §3.7: `details: { page, create?, edit?: 'page' }`; "**`edit: 'page'`.** The page is its own
   editor: the host registers no `details.edit`, `details.save` or `details.cancel`, never enters
   edit mode, and the hatch supplies its editor route through its own actions." Update 0029 §3's
   `details` member type accordingly.
2. 0034 §2.3: `details: { page: TmReportPage, create: TmReportStudio, edit: 'page' }`; §10.2 cite the
   rule; flag 11 goes.
3. 0034 §12.1 edit column: make the studio page host `<tm-details-page [source]="tmEntitySource(…)" [layout]="TmReportStudio">`
   in edit mode from entry, so Save, conflicts, drafts and the leave guard are 0031's in both modes;
   §12.9 "In edit mode the studio saves itself" and the `TmApi.write('reports/save', …)` bullet go.

### F-7 (major) — Report seeds run at Order 30, before the roles they publish to exist (K49 / addendum 4 unapplied in 0011)

**Specs:** `docs/specs/0011-distribution-host-and-multitenancy.md` §6.2; `docs/specs/0033-report-definitions-and-engine.md` §13.

**Problem.** The seed's grants are written on insert only and a missing role is skipped. At Order 30
the Acme seed (`PublishTo` the role `Sales`, created by a later distribution step) inserts with no
grant and never gets one. Scenario 5's "Shared with me" for a Sales member is empty forever on a new
tenant.

**Evidence.**
- 0011 §6.2: "`20 core.blob-container` …; `30 core.report-seeds` (spec 0033 §13 upserts the seeded
  report definitions). Spec 0018 adds `100 gl.sample-centers`."
- 0033 §13: "`core.report-seeds` runs at `Order` 900, after every pack and distribution step, so the
  roles a seed names exist." and "**Grants** are written on insert only … a named role the tenant
  lacks is skipped with the warning `ReportEvents.SeedRoleMissing`."
- addenda 4: "`core.report-seeds` has provisioning `Order` 900 … spec 0011 §6.2's step list follows."

**Fix.** 0011 §6.2: move the step to `900 core.report-seeds` and add "The band 900 and above is
reserved for platform steps that must run after every pack and distribution step."

### F-8 (major) — K-0032b (built-in schedules hide Delete through a `permissions` extra) is unapplied in 0020 and 0032

**Specs:** `docs/specs/0020-background-jobs-and-scheduler.md` §7.8; `docs/specs/0032-stock-pages.md` §9,
Review flag 16; `docs/specs/0031-details-page.md` §4.1.

**Problem.** 0031's `can` reads a `permissions` extra "when the stack declares it", but the Schedule
stack declares only `state`; 0032 keeps Delete on the menu for a built-in and lets the server refuse
it, which the ruling rejected.

**Evidence.**
- known-issues K-0032b: "the Schedule stack's `permissions` extra reports `Delete = false` for
  built-ins and 0031's `can` reads it (spec 0031 §4), which hides Delete. → 0032, 0020 §7 if the
  extra is not declared there."
- 0020: the only details extra on schedules is `state` ("reads a schedule's state through the details
  extra `state` (§7.8)"); grep for `permissions` extra finds none.
- 0032 §9: "Delete is an operation, not an action, so it stays in the menu; the server refuses it on a
  built-in (`Schedules.BuiltInImmutable`)"; flag 16: "Delete stays offered on a built-in schedule".
- 0031 §4.1: "`can` … when the record's extras hold `permissions` (spec 0033 §5.2), its `save`,
  `delete` or `activate` member".

**Fix.** 0020 §7.8: declare the eager extra `permissions` on the Schedule stack, one row per visible
id `{ id, save: true, delete: Id > 999, activate: Id > 999 }` (a computed extra, F-2). 0032 §9: "Delete
and Deactivate are hidden on a built-in through the `permissions` extra (spec 0020 §7.8, spec 0031
§4.1)"; drop the `visible` on `deactivate` in §3's declaration or keep it as belt and braces; flag 16
goes. 0031 §4.1: cite 0020 §7.8 beside 0033 §5.2.

### F-9 (major) — K-0032a (`TmDetailsSource.validate`) is unapplied; Settings fakes a 422 from `write`

**Specs:** `docs/specs/0031-details-page.md` §3.1, §8.1; `docs/specs/0032-stock-pages.md` §7.6, Review flag 6.

**Evidence.**
- known-issues K-0032a: "spec 0031 §3.1's `TmDetailsSource` gains an optional `validate?(value):
  TmValidationProblem[]` used before `write`; Settings' checks use it."
- 0031 §3.1 `TmDetailsSource` sketch has `read`, `write`, `can`, `errorSegments`, `changes` — no
  `validate`.
- 0032 §7.6 step 2: "`write` rejects with the 422 shape the server would answer, at `entries.<key>`,
  so the host shows it like one"; flag 6: "since spec 0031's `TmDetailsSource` has no schema member".

**Fix.** 0031 §3.1: add `validate?(model: T): readonly TmValidationProblem[]; // { path, code, arguments? }, run by §8.1 step 3 before submit; problems map through §8.5`
and the type sketch; §8.1 step 3 names it. 0032 §7.6 step 2: "the source's `validate` reports …";
flag 6 goes.

### F-10 (major) — K48 (`'uq'` over path-free operands) is unapplied in 0010 §F.13 and 0033

**Specs:** `docs/specs/0010-amendments-to-implemented-specs.md` §F.0 (codes table), §F.13 (Operands,
Tests), Part F decision 13, Part F flag 4; `docs/specs/0033-report-definitions-and-engine.md` §4.3,
§8.7, Review flag 6.

**Problem.** A Hijri default such as `startOfMonth(today(), 'uq')` — the obvious default for an
Umm al-Qura tenant in scenario 5 — is still refused with QX3104 everywhere.

**Evidence.**
- known-issues K48: "a path-free operand … compiles to the same `TOP (1)` month-map seek as a scalar
  subquery … `QX3104` applies only to a `'uq'` operand inside an aggregate's argument or a grouping
  key that is not a column path. So `startOfMonth(today(), 'uq')` works as a parameter default.
  → 0010 §F.13, 0033 (flag 6 goes)."
- 0010 §F.13: "Anything else is QX3104: a path-free operand (`startOfMonth(today(), 'uq')`) or an
  aggregated one"; §F.0 code table: "QX3104 | A table calendar needs a row-grain operand that reads a
  path"; Tests: "QX3104 for both refused shapes and inside a constant statement"; decision 13; flag 4.
- 0033 §4.3: "a table calendar is QX3104 (§8.7)"; §8.7: "a path-free operand
  (`startOfMonth(today(), 'uq')`) is QX3104"; flag 6.

**Fix.** 0010 §F.13 Operands bullet: "A date operand that reads no path (a constant, `today()`, a
constant-statement item) is looked up by a scalar `(SELECT TOP (1) … WHERE [M].[Calendar] = <slot> AND
[M].[StartDate] <= <d> AND <d> < [M].[EndDate] ORDER BY [M].[StartDate] DESC)` in SELECT, WHERE and
HAVING; QX3104 applies only to an aggregated operand, or to a `'uq'` operand in a grouping key that
is not a column path." Update the §F.0 code row, the Tests line, decision 13 and flag 4. 0033 §4.3
and §8.7: delete the QX3104 sentences; flag 6 goes.

### F-11 (major) — Report delete: 0033 §5.6 still reads the audience, against K-0035b, 0034 §10.3 and 0035 flag 11

**Specs:** `docs/specs/0033-report-definitions-and-engine.md` §5.6, Decisions record 12; `docs/specs/0034-report-renderers-and-studio.md` §10.3.

**Evidence.**
- known-issues K-0035b: "Report and dashboard delete use the host's plain delete confirmation; spec
  0033 §5.6's 'reads the audience' sentence goes."
- 0033 §5.6: "**Delete removes the definition for everyone,** shares included, after a warning that
  reads the audience (spec 0034 §10.3)"; decision 12: "Delete removes for everyone after a warning".
- 0034 §10.3: "**Delete** is the host's (spec 0031 §10.2): its confirmation, then 'Report deleted.'";
  0035 flag 11: "Alternative: name the reach, as spec 0033 §5.6 describes for reports."

**Fix.** 0033 §5.6: "after the host's delete confirmation (spec 0034 §10.3)"; decision 12: drop
"after a warning". 0035 flag 11: drop "as spec 0033 §5.6 describes for reports".

### F-12 (major) — 0024's route assembly defines no `canDeactivate`, so the leave guard has no route hook (K39 unapplied)

**Specs:** `docs/specs/0024-client-app-foundation.md` §9.2; `docs/specs/0031-details-page.md` §11.3;
`docs/specs/0034-report-renderers-and-studio.md` §12.1.

**Problem.** Scenario 3 ("edit … back") and scenario 7 depend on "Leave without saving?" running on
every router navigation. 0031 says the route's `canDeactivate` runs the page scope's checks; 0024,
which builds every route, never adds one.

**Evidence.**
- known-issues K39: "Spec 0024 §9.2's route assembly sets a `canDeactivate` that runs the checks
  registered in the page scope (spec 0031 §11.3)".
- 0031 §11.3: "The page route's `canDeactivate` runs the checks registered in its page scope on every
  router navigation away".
- 0024 §9.2 "Page routes" row: "every one has `TmRouteHost` as its component and a `TmRouteData`" —
  no guard; grep `canDeactivate` in 0024 returns nothing.

**Fix.** 0024 §9.2 "Page routes" row: "… and `canDeactivate: [tmPageScopeCanDeactivate]`, which runs
every check registered in the page scope through `tmLeaveGuard` (spec 0031 §11.3); a refusal keeps
the history index (`canceledNavigationResolution: 'computed'`)." Add `tmPageScopeCanDeactivate` to
the primary's exports in §1.

### F-25 (major) — Acme's assignable sales invoice is cited by 0028 and 0024 but absent from 0018 §1.4 (K44 / addendum 5 unapplied)

**Specs:** `docs/specs/0018-core-and-gl-reference-stacks.md` §1.4, §6.3; `docs/specs/0028-assignments-and-attention-surfaces.md`
§3.5, §13; `docs/specs/0024-client-app-foundation.md` §14.

**Problem.** Scenario 6 is pinned by 0028's e2e ("Sara assigns invoice 23 to Farah …"), which needs
`[Assignable]` on `acme.SalesInvoice`, its `assign` and `post` entity actions and seeded assignments.
0018 §1.4 lists `set-credit-limit`, `close` and `related-documents` only, with no `[Assignable]` and no
assignments in the development data, so the reference distribution has no server side for the
scenario and 0028's example "Acme's sales invoice (spec 0018 §1.4)" cites nothing.

**Evidence.**
- known-issues K44 / addenda 5: "Spec 0018 §1.4 lists Acme's `[Assignable]` on `acme.SalesInvoice`,
  its `assign` and `post` entity actions, and a few demo assignments in the sample-data step."
- 0018 §1.4 `SalesInvoice` row: "`[DisplayNameSource(nameof(SerialNumber))]` … child `Lines` … child
  `Attachments`" and the bullets "`set-credit-limit`", "`close`", "`related-documents`", "Reports",
  "Development data" — grep for `Assignable|assign` in 0018 finds only `AssignmentsSeenAt` and the
  migration table list.
- 0028 §3.5: "**Example —** Acme's sales invoice (spec 0018 §1.4): … `[EntityAction("assign")]` … in
  the post action"; §13: "**Acme** (spec 0018 §1.4) adds `[Assignable]` on `acme.SalesInvoice`, its
  `assign` and `post` actions, the channel-only type `acme.invoice.assigned` (§3.6), and development
  data that gives the demo users two open assignments and one return."

**Fix.** 0018 §1.4: add `[Assignable]` to the `SalesInvoice` row's annotations; add bullets
"**`assign`.** `[EntityAction("assign")]` with `AssignInvoiceArguments([EntityRef("core.User")] int AssigneeId, string? Comment)`,
writing through `context.Assignments.Assign` (spec 0028 §3.5)" and "**`post`.** `[EntityAction("post")]`,
no arguments, sets `IsPosted` and completes the open assignment"; register the channel-only
notification type `acme.invoice.assigned`; §6.3's `AcmeSampleDataStep` seeds two open assignments and
one return among the demo users. Add `IsPosted` (server-owned) to the row if `post` needs a column.

---

## Minors

### F-13 (minor) — K17/K18: 0024 does not state the observer ordering or the locale adoption

**Specs:** `docs/specs/0024-client-app-foundation.md` §7.2 step 3, §10.3 step 5; `docs/specs/0025-client-state-and-cross-tab-sync.md` §5.3, §9.5; `docs/specs/0027-shell-launcher-and-tenant-picker.md` §12.1.

**Evidence.** 0025 §5.3: "`TmApi` calls it before settling the call (spec 0024 §10.3), so by then a
write's caller has passed its result to `adopt`"; 0024 §10.3 step 5 only: "`Tellma-Version-Tags` go to
`TmCacheRefresher.observe(t, tags)`". 0024 §7.2 step 3: "`TmApi.write('users/me/locale', { language })`"
with no adoption; 0027 §12.1 and 0025 §9.5 say the caller passes `MeLocaleResult` to `adopt`.

**Fix.** 0024 §10.3 step 5: "the body is delivered to the caller first; `observe` compares one task
later (0025 §5.3)". §7.2 step 3: "… and passes the `MeLocaleResult` to `TmCacheRefresher.adopt(t, { locale })` (0025 §5.3)".

### F-14 (minor) — K38: a `404 not-found` on a save is the 400 dialog in 0024 but the deletion dialog in 0031

**Specs:** `docs/specs/0024-client-app-foundation.md` §10.6; `docs/specs/0031-details-page.md` §8.6, §9.2.

**Evidence.** 0024 §10.6: "404 `not-found` | `not-found` | a read: inline (0031 §3.5); a write: 400
dialog". 0031 §8.6: "404 `not-found` on an update | the deletion dialog (§9.2)".

**Fix.** 0024 §10.6 row: "a write: 0031 §9.2's deletion dialog on a details save, else the 400 dialog".

### F-15 (minor) — K31/K32: `TmLocalization.translate` still declared; the `tmOverride` example moves Administration the wrong way

**Specs:** `docs/specs/0024-client-app-foundation.md` §7.1, §2.3.

**Evidence.** 0024 §7.1 sketch: "`translate(key: string, code: string): string; // a key in a loaded,
inactive language`" (K32: "has no consumer; delete it"). §2.3 example: "`tmOverride('section:administration', { order: 90 })` … moves the Administration section to the end" — Administration is 900 (0027 §3.2); 90 moves it before Analysis (800), not to the end.

**Fix.** Delete `translate` (and `load` unless §7.3 names its consumer). Example → `{ order: 10 }`
"moves the Administration section to the front".

### F-16 (minor) — The `:segment` search route matches child-entity segments

**Specs:** `docs/specs/0024-client-app-foundation.md` §9.2, §9.4; `docs/specs/0029-client-entity-model.md` §2.2; `docs/specs/0033-report-definitions-and-engine.md` §10.

**Evidence.** 0024 §9.2: "`':segment'` search (0030); canMatch: `TmEntities.bySegment`". 0029 §2.2:
`segment` is "a child's query segment" too (`kind: 'child'`). 0033 §10: "the drill-through list is
the only page over it". So `/17/sales-invoice-lines` would open a search page over lines.

**Fix.** 0024 §9.2: "canMatch: `TmEntities.bySegment(segment)?.kind === 'stack'`"; `list/:segment`
admits `stack` and `child`; `TmUrls.describe` follows.

### F-17 (minor) — `'reports/new'` is listed as a separate route, against 0034's create path

**Specs:** `docs/specs/0024-client-app-foundation.md` §9.2; `docs/specs/0034-report-renderers-and-studio.md` §12.1.

**Evidence.** 0024 §9.2: "`'reports/:key', 'reports/:id/studio', 'reports/new'` coded reports and the
studio (0034)". 0034 §12.1: create is "the details host's create mode, with the studio as the `create`
hatch", i.e. the generic `:segment/new` route.

**Fix.** Drop `'reports/new'` from the explicit list (it is `:segment/new` with the `create` hatch).

### F-18 (minor) — A `page` hatch without `create` has no stated create-mode rendering; 0035 relies on it

**Specs:** `docs/specs/0031-details-page.md` §3.7; `docs/specs/0035-dashboards-and-home-page.md` §12.1.

**Evidence.** 0031 §3.7: "`page` renders as the whole body region … in view and edit mode;
`create`, when given, renders create mode." 0035 §12.1: `details: { page: … tmDashboardPage() } // view, edit and create`.

**Fix.** 0031 §3.7: "Without `create`, `page` renders create mode too."

### F-19 (minor) — Paging from a browse-tree snapshot would re-issue `get-by-parent-ids` with `skip`/`take`

**Specs:** `docs/specs/0030-search-page.md` §15.1, §16.2; `docs/specs/0031-details-page.md` §12.1, §12.2.

**Evidence.** 0030 §15.1: "`request`: the last rows read; null with `searchRead`". §16.2: the browse
tree reads `get-by-parent-ids`, unpaged. 0031 §12.2: "At an edge, when a neighbour page exists … the
host reads `snapshot.request.route` with `{ ...request.body, skip, take: size, includeCount: true }`".
A 40-node tree with `size` 50 shows `1–40 / 40`, but with more rows than `size` the edge rule fires
against a route that has no paging.

**Fix.** 0030 §15.1: "`request` … null with `searchRead` and for a browse tree (§16.2), which pages
within `ids` only (spec 0031 §12.1)".

### F-20 (minor) — A2: 0014 §5.3's self-scope sentence omits support requests

**Specs:** `docs/specs/0014-users-roles-and-permissions.md` §5.3 (line "Users, jobs, exports, imports, notifications and assignments are self-scoped …"); `docs/specs/0026-client-observability-and-support.md` §12.6.

**Evidence.** addenda 2: "spec 0014 §5.3's self-scope list gains `core.SupportRequest` beside
`core.Assignment`". 0014 §5.3 lists "Users, jobs, exports, imports, notifications and assignments"
with specs "0018 §3.2, 0020 §11.2, 0019 §12.1, 0021 §2.2, 0028 §2.2" — no support requests, no
0026 §12.6 (though `CoreResources.SupportRequest` carries "Read, Delete; spec 0026 §12.6").

**Fix.** Add "support requests (spec 0026 §12.6)" to the sentence.

### F-21 (minor) — The support dialog has no state for a `/support` chunk that cannot load (scenario 9 offline)

**Specs:** `docs/specs/0026-client-observability-and-support.md` §12.1, §12.2 (contrast §8.4).

**Evidence.** §12.1: "`open` shows the `TmModal` in the click's frame with a final-geometry
placeholder, snapshots the contexts, then imports `/support`." §12.2 lists Compose, Download only,
Sending, Sent, Failed. §8.4 handles the same failure for the Diagnostics tab ("Diagnostics aren't
available offline. Press {keys} to copy them.") but §12.2 does not; `/support` is in the `on-demand`
worker group (0024 §5.3), so it is absent until first use.

**Fix.** §12.2: add a state "Unavailable | the `/support` import failed | 'This isn't available
offline. Press {keys} to copy diagnostics.' with `tm-kbd`; Close". Note that `copyDiagnostics` lives in
the primary and still works.

### F-22 (minor) — K-0035a: 0033 §4.3 cites a narrower dashboard rule than 0035 §5.3

**Specs:** `docs/specs/0033-report-definitions-and-engine.md` §4.3; `docs/specs/0035-dashboards-and-home-page.md` §5.3.

**Evidence.** 0033 §4.3: "Dashboards hide parameters, so there a required one needs a default (spec
0035 §5.3)". 0035 §5.3 rule 3: "bound to a non-null fixed value or fed by a required dashboard
parameter, else `Dashboard.WidgetParameterRequired`".

**Fix.** 0033 §4.3: "… so there a required one needs a default, a fixed-value binding or a required
dashboard parameter (spec 0035 §5.3)".

### F-23 (minor) — 0024 §8.4 blocks every `TmApi` call during sign-out, the sign-out call included

**Specs:** `docs/specs/0024-client-app-foundation.md` §8.4.

**Evidence.** "every tab of that `sub` aborts its reads and shows the blocking 'Signing out…' overlay
(0023 `busy-overlay`); `TmApi` rejects new calls with `cancelled`" — the acting tab's own
`TmTelemetry.flush()` (a `send`), `POST /bff/logout` and the preference `pagehide` flush are new calls.

**Fix.** "… rejects new calls with `cancelled`, except the acting tab's `/bff/logout` write and
`send` calls".

### F-24 (minor) — The launcher's recents band has no icon rule for recents that match no entry

**Specs:** `docs/specs/0027-shell-launcher-and-tenant-picker.md` §9.3; `docs/specs/0025-client-state-and-cross-tab-sync.md` §13; `docs/specs/0035-dashboards-and-home-page.md` §9.2.

**Evidence.** 0025 §13: "`icon`: the launcher entry's icon, else null: the consumer picks one by
kind". 0035 §9.2 picks "the entry's icon, else by kind (`list`, `chart-column`, `layout-dashboard`,
`settings`, `file`)". 0027 §9.3 band 2 ("up to ten chips from `TmRecents.pages`") names no fallback,
so a stored report or a `tmPage` recent has no icon rule in the launcher.

**Fix.** 0027 §9.3 band 2: "icons: the entry's, else 0035 §9.2's map by kind".

### F-26 (minor) — Three server-side metadata edits the new specs cite are not in their owners (K11, K14, K7)

**Specs:** `docs/specs/0013-settings-localization-and-cache.md` §7.6, §10.1; `docs/specs/0015-crud-service-pipeline.md`
§11.2; `docs/specs/0011-distribution-host-and-multitenancy.md` §8; `docs/specs/0029-client-entity-model.md`
§2.4; `docs/specs/0024-client-app-foundation.md` §3.1.

**Evidence.**
- K11: "Spec 0013 §10.1 (and §7.6 …) gains the keys spec 0029 defines: `<Schema>_<Entity>_New` with
  the Core fallback `Entity_New`, `Entity_Level`, and the action-field label chain". 0013 §10.1 lists
  `<Schema>_<Entity>_Action_<Name>` → `Action_<Name>` but no `_New`, `Entity_New`, `Entity_Level` or
  `<Schema>_<Entity>_Action_<Name>_<Field>`; 0029 §2.4 defines the two chains itself and §4.3 uses
  "Core `Entity_Level`".
- K14: "Spec 0015 §11.2's `[EntityRef]` sentence says the client metadata carries the target per
  argument field (spec 0029's `TmActionMetadata.fields[].value.target`)". 0015 §11.2 declares
  `EntityRefAttribute` and says it "is metadata only" (decision 27) with no client-metadata sentence.
- K7: "`Tellma:Copyright` … is a row in spec 0011 §8's key table". 0011 §8 has `Links:Terms`,
  `Links:Privacy`, `ClientTelemetry:*` but no `Copyright`; 0024 §3.1 reads "`Tellma:DisplayName` and
  `Tellma:Copyright` from the Migrator's `appsettings.json`".

**Fix.** 0013 §10.1: add the create key and its Core fallback, `Entity_Level`, and the action-field
chain, each citing 0029 §2.4 as the resolver. 0015 §11.2: one sentence after the `[EntityRef]`
example: "The client metadata carries the referenced entity per argument field (spec 0029 §2.2
`TmActionField.value.target`)." 0011 §8: a `Copyright` row ("the About dialog's copyright line, read
at emit; spec 0024 §3.1").

### F-27 (minor) — Chip-row focus order and the touch-row rule's home disagree with K21 and K3

**Specs:** `docs/specs/0010-amendments-to-implemented-specs.md` §E.2 (Keyboard table), §B.6, §C.10;
`docs/specs/0023-component-library-additions.md` §2.1.

**Evidence.**
- K21: "its Backspace/Delete focus order follows 0023 §4 (next chip, else previous, else the
  input)". 0010 §E.2: "Backspace / Delete | chip | removes it; focus moves to the next chip, else the
  input" — no "else previous"; 0023 §2.1: "Delete or Backspace removes, then focuses the next chip,
  else the previous, else emits `exit('end')`".
- K3: "§B.6's menu-row rule (≥ 44 px below `sm`) belongs to the menu, which spec 0004 owns: move that
  half to §C.10 and leave §B.6 the form-field size rule, each citing the other." §B.6 still carries
  the "**Menu rows.**" bullet and the `--menu-item-height-touch` token; §C.10's "Touch rows" cites
  §B.6 for it.

**Fix.** 0010 §E.2 keyboard row: "…the next chip, else the previous, else the input". Move the
"Menu rows" bullet and its token from §B.6 to §C.10 ("Touch rows" becomes the rule), §B.6 citing §C.10.

---

## Scenario walk-throughs (what holds, what each finding breaks)

| # | Scenario | Result |
|---|---|---|
| 1 | Cold launch after sign-in → picker → home | Works end to end except: F-3 (a cold deep link to a details page sticks on forbidden; search chrome flashes), F-4 (the launch redirect always waits on `/bff/user`; the picker never has a cached list). |
| 2 | Warm launch, network hanging | Works: `tm-boot.js`, the `shell` group, one `readAll`, chrome and badges from the store; the page's loading state shows; the watchdog ends the read in `offline` with Retry; the banner follows `TmApi.online`. F-4 only hides "My account" and About's links. |
| 3 | Search Centers → filter → columns → open → edit → 422 → fix → save → next → back | Works: one leaf per filter, the live column editor, `tmFrom`, the inline summary, `markStale`, `supersedes`. Broken: F-12 (no route hook for the leave guard on Back while dirty), F-19 (next/previous from a browse-tree snapshot), F-14 (404 on save shows two different dialogs depending on the spec read). |
| 4 | Admin changes Farah's role, three tabs open | Works: the user-addressed `cache.changed { tag: 'permissions', value }` → leader `observe` → one `client/sync` → `store.changed` → `TmPermissions`, navigation, pins, page state and display names all follow in every tab; the search page re-reads. |
| 5 | Author a pivot → share with a role → dashboard → silent refresh → drill to a child row | Broken by F-1 (defaults cannot run), F-2 (`permissions`/`view` extras have no contract), F-7 (seed grants lost), F-10 (Hijri defaults refused), F-6 (Edit on the report page), F-11 (delete copy disagrees). Otherwise the drill path (`/list/sales-invoice-lines` → owner's key → `?child=Lines:42` → `reveal`) is consistent across 0033 §9.2, 0034 §8.3, 0030 §17 and 0031 §14. |
| 6 | Assignment handed to Farah: tray, focused-tab toast, home list | Works: `assignments.changed` to assignee and assigner → leader refresh → document copy → `TmArrivals` toasts in the focused tab → tray badge and home list update in place. F-25 leaves Acme with no `assign` action to raise it; F-5 only affects notifications opened in a new tab. |
| 7 | A deployment lands while editing | Works: `VERSION_READY` → `tm.update` banner (idle-gated) → 412 on save → "Tellma was updated … Reload" → draft restored in edit mode; the `savingAt` notice is cleared by the 412 answer as §11.1 says. |
| 8 | Sign-out in one tab | Works: drafts query, busy wait, broadcast, logout, sweep, `clearAll({ keep })`, every tab to `/signed-out`; the leader releases the hub lock on the state change. F-23 is a wording defect. |
| 9 | Support request with a screenshot | Works: Report a problem → instant modal → snapshot → `/support` → `getDisplayMedia` → bundle upload (`Tellma-Client` required, 0017 §8.1) → `submit` → receipt. F-21 (offline chunk), F-4 (`canSend` warm). |

## Design rules and brief coverage

- **Plumbing in the platform (rule 1):** holds; the Acme examples across 0024 §2, 0029 §3, 0030 §2,
  0031 §2, 0034 §2, 0035 §2 are declarations plus markup. No finding.
- **Instant warm launch (2A):** holds for the shell, badges, search and details chrome, home and
  dashboards; F-4 breaks it for the session menu's "My account", About and `canSend`.
- **Instant feedback (2B), non-blocking (2C):** hold (skeletons in the click's frame, one `resource()`
  per read, aborts to SQL, writes never aborted). F-3's cold-only await is the one sequential wait
  this review proposes, and it is cold-only.
- **Stable layout (2D):** holds (status strips overlay, banners idle-gated, reserved slots, fixed
  tile heights, counters with fixed boxes).
- **RTL (2E), WCAG AA (2F):** hold; every page spec runs its e2e in `ar` at four widths and pins a
  keyboard-only path; the chart's table view and the pivot's `treegrid` are consistent with 0023
  §2.1. No finding.
- **AI-native (rule 3):** holds; one registry (0026 §9), every host publishes, actions share one
  dispatcher; `propose` reaches 0031's form.
- **Brief coverage:** every heading of the brief has an owner and an answer in a Decisions record
  (tenant picker Q1/Q2 in 0024; search Q1 and trees Q1 in 0030; details Q1–Q3 in 0031/0029; SignalR Q1
  in 0025; stock pages Q1 in 0029; the reports questions in 0033/0034; dashboards Q1 in 0035;
  observability and supportability Q1–Q4 in 0026). The documented deviations (inline 422 summary,
  installability gate instead of Lighthouse PWA, the picker from seven memberships) are flagged in
  their owners. No uncovered requirement found.

## Known-issue and addenda items still open after this pass

Unapplied: K17, K18, K31, K32, K38, K39 (0024); K43 (0024, 0028, 0030); K48 (0010 §F.13, 0033);
K49/addendum 4 (0011 §6.2); K50 (0012, 0033); K-0032a (0031, 0032); K-0032b (0020, 0032);
K-0034a (0031, 0034); K-0035a (0033); K-0035b (0033); K44/addendum 5 (0018 §1.4); K11 (0013);
K14 (0015 §11.2); K7 (0011 §8); K21 and K3 (0010 §E.2, §B.6); addendum 2 (0014 §5.3's prose);
addendum 3 (0015 §5.3). Checked and applied: K5 (0011 §5.1), A7 (0016 §6.2), K43a, K22, K23, K15,
K37, K42, K30, K26, K12, K20, K24, K33, K1, addenda 6–10. Not verified here: K13, K-F.
