# Consistency check — cluster "crud" (specs 0030, 0031, 0032)

Scope: `docs/specs/0030-search-page.md`, `0031-details-page.md`, `0032-stock-pages.md`, against
`known-issues.md`, `addenda.md`, `rulings.md`, `design/00-architecture.md` (in that precedence) and
every spec they point at. Every `spec NNNN §x` pointer in the three specs was resolved
mechanically (817 pointers; all targets exist except the two bare `§C.x` pointers of K-crud-20) and
the claim each makes was read against the target.

Known issues and addenda that concern these specs, and their state:

| Item | State | Finding |
|---|---|---|
| K15 (0031 §7.1 `TmUploads.running` → `pending`) | resolved (§7.1 table, §6.3 step 2) | — |
| K22 (`tm-tiles` uses the grid's names) | resolved (§8, §16.4) | — |
| K42 (`rowLink`, `pageActions[].run` in the injection context) | resolved (§3.1 sketch comment, §7.2) | — |
| A10 (`activity` extra is the users stack's) | resolved (0032 Context, §12.2) | — |
| K43 (`selection: true` page actions; notification open route) | **not resolved** | K-crud-1 |
| K-0032a (`TmDetailsSource.validate`; step-up notice) | **not resolved** | K-crud-2 |
| K-0032b (built-in schedules: `permissions` extra hides Delete) | **not resolved**; 0032 states the opposite | K-crud-3 |
| K-0034a (`edit: 'page'` on a `page` hatch) | **not resolved** in 0031 | K-crud-4 |
| K37 (`snapshot.request === null` guard in 0031 §12.2) | **not resolved** | K-crud-5 |
| K38 (404 on a details save → deletion dialog, in 0024 §10.6) | **not resolved**; contradicts 0031 §8.6 | K-crud-6 |
| K39 (`canDeactivate` in 0024 §9.2's route assembly) | **not resolved**; 0031 §11.3 relies on it | K-crud-7 |
| K23 (`tm-pager` item mode `[total]`, `totalIsLowerBound`) | partly (0031 names neither input) | K-crud-24 |
| K26 (`confirm` is `{ titleKey, messageKey, args? }`) | 0030's declaration type lacks `args` | K-crud-25 |
| A3 (0032 cites 0015 §5.3 for computed extras) | **not resolved** | K-crud-28 |

---

## Major

### K-crud-1 — major — known issue K43 — 0030 §3.1, §12.2, §19

**Problem.** K43 is binding: a `pageActions` entry may declare `selection: true`, then shows in the
selection bar's Actions menu and its `run` receives the selected ids; Notifications opens rows
through `/{t}/notifications/{id}/open`. 0030's `TmSearchPageActionDeclaration` has no `selection`
member and `run(search)` takes no ids; §12.2's Actions menu has no page-action group. The §3.1
example is also a copy of 0028 §10.2's declaration (repetition) and still links rows straight to
the target, so it now contradicts K43.

**Fix — 0030 §3.1 sketch.** Replace

```ts
  readonly requires?: TmSecurableRef;           // shown only while TmPermissions.can(resource, action)
  run(search: TmSearchContext): Promise<void>;  // in the host's injection context; refreshes what it changes
```

with

```ts
  readonly requires?: TmSecurableRef;           // shown only while TmPermissions.can(resource, action)
  readonly selection?: boolean;                 // true: an item of the selection bar's Actions menu (§12.2), not a toolbar button
  run(search: TmSearchContext, ids: readonly number[]): Promise<void>; // ids: the selection when selection is true, else []; in the host's injection context; refreshes what it changes
```

**Fix — 0030 §3.1 example.** Replace the caption "**Example —** spec 0028's Notifications page, with
its `rowLink` (§7.2) and "Mark all as read":" and its five-line block with:

> **Example —** spec 0028 §10.2's Notifications page opens each row through spec 0028's open route
> and marks the selected rows read:
>
> ```ts
> rowLink: (row) => inject(TmUrls).page(`notifications/${row.id}/open`),
> pageActions: [{ id: 'attention.notifications.read', labelKey: 'tm.attention.markRead', icon: 'check', selection: true,
>   run: async (search, ids) => { await inject(TmApi).write('inbox/read', { ids }); search.refresh(); } }],
> ```

**Fix — 0030 §12.2 table.** Insert after the "entity actions" row:

`| page actions | each `pageActions` entry with `selection: true`, in declaration order (§3.1) | its `run` with the selected ids; the entry reports its own outcome |`

**Fix — 0030 §19, Page actions bullet.** Replace "Each `pageActions` entry keeps its own id and
`group`." with "Each `pageActions` entry keeps its own id and `group`; a `selection` entry is a
`menu` action over the selection."

**Other files.** 0028 §10.2 (add "Mark as read" and "Clear" with `selection: true` over
`inbox/read` and `inbox/clear`; `rowLink` to the open route; the `/{t}/notifications/{id}/open`
`tmPage`; delete "Opening a row follows its link and marks nothing read" and "or bulk actions");
0024 §9.2's route list (add `'notifications/:id/open'   0028 §10.2`).

### K-crud-2 — major — known issue K-0032a — 0031 §3.1, §5.3, §8.1; 0032 §7.2, §7.5, §7.6, Review flags

**Problem.** K-0032a is binding: `TmDetailsSource` gains `validate?(value): TmValidationProblem[]`,
run before `write`, and Settings' checks use it; the step-up notice comes from
`TmPermissions.sensitive` as on any details page. 0031 §3.1 has no `validate`, no spec defines
`TmValidationProblem`, and 0031 has no step-up notice at all. 0032 §7.6 step 2 does the checks
inside `write`, and Review flag 6 says the opposite of the ruling ("`TmDetailsSource` has no schema
member"). 0032 §7.5 builds its own reserved-box notice.

**Fix — 0031 §3.1 sketch.** After the `changes?` line add:

```ts
  validate?(model: T): readonly TmValidationProblem[]; // client checks the schema cannot express; run before write (§8.1)
  readonly saveSecurable?: Signal<TmSecurableRef | null>; // the step-up notice's securable (§5.3); default [entity, 'Save']
}
interface TmValidationProblem { readonly path: string; readonly code: string; readonly arguments?: TmJsonObject } // a 422 errors[] item's shape (spec 0016 §7.3)
```

**Fix — 0031 §8.1 step 3.** Replace "The host clears its server errors and calls `submit(tree, {
action, onInvalid })`; `onInvalid` opens the summary (§8.4)." with "The host clears its server
errors, runs `source.validate?.(model())` and records its problems as §8.5 records server errors,
then calls `submit(tree, { action, onInvalid })`; any problem or `onInvalid` opens the summary
(§8.4) and nothing is sent."

**Fix — 0031 §5.3, strip bullet.** Replace "in priority: the error summary (§8.4), the draft notice
(§11.2), a deep-link notice (§14.1)." with "in priority: the error summary (§8.4), the draft notice
(§11.2), a deep-link notice (§14.1), the step-up notice. The step-up notice shows in edit and create
modes while `TmPermissions.sensitive(...source.saveSecurable())` holds: "Saving asks you to confirm
it's you.""

**Fix — 0032 §7.6 step 2.** Replace the step with: "2. **Client checks.** The source's `validate`
(spec 0031 §3.1) reports a non-nullable key whose value is `null` (`Required`) and an invalid marker
(`Settings.EntryTypeMismatch`) at `entries.<key>`, so the host shows them before any request."

**Fix — 0032 §7.2 table.** Add rows "| `validate` | §7.6 step 2 |" and "| `saveSecurable` |
`['core.Settings.General', 'Save']` while the General tab is selected, else `null` |".

**Fix — 0032 §7.5.** Replace the "**Step-up notice.**" bullet with "**Step-up notice.** Spec 0031
§5.3's, through the source's `saveSecurable`: only the General tab's securable is sensitive (spec
0013 §5.5)."

**Fix — 0032 Review flags.** Delete flag 6 and renumber 7–16 as 6–15.

### K-crud-3 — major — known issue K-0032b; contradiction — 0032 §9, Review flag 16; 0020 §7.8

**Problem.** K-0032b is binding: the Schedule stack's `permissions` extra reports `Delete = false`
for built-ins and 0031's `can` hides Delete. 0032 §9 states the rejected design ("Delete is an
operation, not an action, so it stays in the menu; the server refuses it") and Review flag 16
defends it. 0020 §7.8 declares only `Extras => ["state"]`. (Addendum 10's "spec 0020 §7.8 has only
the Schedule `state` extra" concerned `activity`; K-0032b, which takes precedence, adds this one.)

**Fix — 0032 §9 opener.** Replace "and hides Deactivate and Take over." with "and hides
Deactivate, Take over and Delete."

**Fix — 0032 §9, Built-in bullet.** Replace "Delete is an operation, not an action, so it stays in
the menu; the server refuses it on a built-in (`Schedules.BuiltInImmutable`), shown by spec 0031
§10.2's failure path." with "Delete hides because the stack's `permissions` extra (spec 0020 §7.8)
answers `delete: false` for a built-in, which spec 0031 §4.1's `can` reads."

**Fix — 0032 Review flags.** Delete flag 16.

**Fix — 0032 §12.3.** Add "- **The `permissions` extra** on the schedules stack (spec 0020 §7.8):
`delete` is false for a built-in."

**Fix — 0020 §7.8.** Change `Extras => ["state"];   // the details extra below` to
`Extras => ["state", "permissions"];   // the details extras below`, and add after the `state`
paragraph: "**The `permissions` extra.** A computed extra (spec 0015 §5.3) in spec 0033 §5.2's
shape, one row per visible id `{ id, save, delete, activate }`, from the caller's decisions as spec
0033 §5.2 computes them, with `delete` false when `Id <= 999`. The Schedules page hides Delete from
it (spec 0032 §9)."

### K-crud-4 — major — known issue K-0034a — 0031 §3.7, §10.1; 0029 §3

**Problem.** K-0034a: "Spec 0031 §3.7 gains one rule: a `page` hatch declares `edit: 'page'` to say
the page itself is the editor, which hides the host's `details.edit` action and in-place edit
mode." 0031 §3.7 has no such rule, and the declaration shape that owns `details` (0029 §3) has no
`edit` member.

**Fix — 0029 §3 sketch.** Replace
`details?: TmLazyComponent | { readonly page: TmLazyComponent; readonly create?: TmLazyComponent }; // details page (spec 0031)`
with
`details?: TmLazyComponent | { readonly page: TmLazyComponent; readonly create?: TmLazyComponent; readonly edit?: 'page' }; // details page (spec 0031 §3.7)`.

**Fix — 0031 §3.7.** Replace the opener's "`details: { page, create? }` (spec 0029 §3)" with
"`details: { page, create?, edit? }` (spec 0029 §3)", and add a bullet after **`page`**:
"- **`edit: 'page'`.** A hatch whose page is itself the editor declares it. The host then registers
no `details.edit` action and has no in-place edit mode; `can.edit` still reaches the page through
`injectTmDetails()`. The report studio is such a hatch (spec 0034 §12.1)."

**Fix — 0031 §10.1 table.** In the `details.edit` row, replace "view, `ready`, `can.edit`" with
"view, `ready`, `can.edit`; never registered with `edit: 'page'` (§3.7)".

**Other files.** 0034 §2.3 and §10.2 (declare `edit: 'page'`; cite 0031 §3.7 instead of "review
flag 11"), owned by the reports cluster.

### K-crud-5 — major — known issue K37; repetition — 0031 §12.2; 0030 §15.3

**Problem.** K37: 0031 §12.2 must guard `snapshot.request === null` (a `searchRead` page), so paging
stays within the snapshot's ids. 0031 §12.2's edge step reads `snapshot.request.route` with no
guard. 0030 §15.3 states the guard instead, in a garbled sentence ("pages within `ids` only; no
`tmFrom`, no paging"), and restates 0031 §12.2's edge flow, which 0031 owns.

**Fix — 0031 §12.2.** Replace "- **At an edge**, when a neighbour page exists:" with "- **At an
edge**, when `snapshot.request` is not null and a neighbour page exists:"; after step 4 add "With
`request: null` (a `searchRead` page, spec 0030 §15.1) paging stays within `ids`: the arrow at
either end is disabled." In step 3 replace "(spec 0030 §15.3)" with "(spec 0030 §4.2)".

**Fix — 0030 §15.3.** Replace both bullets with the one line: "Spec 0031 §12 steps through `ids`,
reads `request` past an edge, and writes the next page back with `supersedes` (§4.2 step 1)."

### K-crud-6 — major — known issue K38; contradiction — 0024 §10.6 vs 0031 §8.6, §9.2

**Problem.** 0031 §8.6 sends a 404 `not-found` on a details save to the deletion dialog (§9.2);
0024 §10.6 still says every write's 404 goes to the 400 dialog. K38 puts the fix in 0024.

**Fix — 0024 §10.6.** Replace the row
"| 404 `not-found` | `not-found` | a read: inline (0031 §3.5); a write: 400 dialog |" with
"| 404 `not-found` | `not-found` | a read: inline (0031 §3.5); a details save: the deletion dialog (0031 §9.2); any other write: 400 dialog |".

### K-crud-7 — major — known issue K39; dangling premise — 0031 §11.3 → 0024 §9.2

**Problem.** 0031 §11.3 says "The page route's `canDeactivate` runs the checks registered in its
page scope". 0024 §9.2's route assembly sets no `canDeactivate`; K39 puts it there.

**Fix — 0024 §9.2, "Page routes" row.** Replace "every one has `TmRouteHost` as its component and
a `TmRouteData` (§4.4);" with "every one has `TmRouteHost` as its component, a `TmRouteData` (§4.4)
and a `canDeactivate` that runs the leave checks registered in its page scope (0031 §11.3), which
the details host and the report studio register;". In 0031 §11.3 add "(spec 0024 §9.2)" after
"The page route's `canDeactivate`".

### K-crud-8 — major — wrong pointer; unowned artifact — 0032 §2, §6, §14; 0031 §18; 0024 §2.3

**Problem.** Acme's Centers override (a `Manager` column and `my-center-details`) is used by 0030
§19, 0031 §18 and 0032 §14, but no spec defines it. 0032 §6 says "Acme's override adds `Manager`
(spec 0024 §2.3)" and 0032 §14 cites 0024 §2.3; 0024 §2.3 only names `centersOverride` in an
example. 0030 §19 cites 0032 §6, which cites back to 0024. 0032 owns the Centers page, so it must
own the example.

**Fix — 0032 §6.** Replace "- **Data.** Sample centers exist in Development only (spec 0018
§5.6). Acme's override adds `Manager` (spec 0024 §2.3)." with "- **Data.** Sample centers exist in
Development only (spec 0018 §5.6)." and add:

> **Example —** Acme's override, §2's first row, over `MyCenter.ManagerId` (spec 0018 §1.4):
>
> ```ts
> export const centersOverride = tmEntity('gl.Center', { columns: ['Code', 'Name', 'CenterType', 'Manager', 'IsActive'],
>   details: () => import('./my-center-details') });
> @Component({ selector: 'acme-my-center-details', imports: [TmDetailsKit, TmGlCenterFields], template: `
>   <tm-details-card><tm-gl-center-fields /><tm-field path="Manager" /></tm-details-card><tm-details-audit />` })
> export default class MyCenterDetails {}
> ```

**Fix — 0032 §14.** Replace "The centers override and `my-center-details` (spec 0024 §2.3)" with
"The centers override and `my-center-details` (§6)". **0031 §18:** replace "`my-center-details`
with `TmGlCenterFields` and `Manager`" with "`my-center-details` (spec 0032 §6) with
`TmGlCenterFields` and `Manager`". **0024 §2.3:** replace "patches the Centers declaration" with
"patches the Centers declaration (spec 0032 §6)".

### K-crud-9 — major — contradiction — 0032 §14 vs 0018 §6.3, 0014 §3.7

**Problem.** 0032 §14 says "The seeded users (New, Invited, Joined, a service account) and roles
exercise invite, credentials and access, and a seeded role with a filter on `Name3` drifts on the
two-language tenant." 0018 §6.3, which owns Acme's development data, seeds no users and only the
role `Sales`. A seed cannot create the drifting role either: a role save validates its filter
against the current schema (0014 §3.7 → `Permissions.FilterInvalid`), so a `Name3` filter on a
two-language tenant is refused.

**Fix — 0032 §14.** Replace that sentence with "The e2e fixtures create the users (New, Invited,
Joined, a service account) and roles they exercise; drift comes from a role whose `Name3` filter
was saved while the tenant had three languages, before the tertiary language is removed." (If
seeds are preferred instead, 0018 §6.3's `acme.sample-data` bullet must list them, and the drifting
row needs a raw insert, not a stack save.)

### K-crud-10 — major — contradiction — 0030 §4.3, §17 vs 0034 §8.3; ruling 2

**Problem.** In list mode (`/{t}/list/{segment}`) 0030 §4.3 still writes the canonical URL through
`TmUrls.search(entity, state)`, which yields the search page's path and drops anything outside
`TmSearchQuery`. 0034 §8.3 adds a URL parameter `from` (the source report), which the rewrite would
strip, and which ruling 2 forbids ("No spec adds a parameter outside `x.{key}` and `p.{Key}`"; also
0024 §9.5).

**Fix — 0030 §4.3, first bullet.** After "…when they differ," add "in list mode with the list path
`/{t}/list/{segment}` and its fixed `filter` (§17),".

**Other files.** 0034 §8.3 drops `from` and reads the source report from `history.state.tmDrill`
only (a bare link's crumb then reads "Report", as §8.4 already allows); owned by the reports
cluster.

---

## Minor

### K-crud-11 — minor — contradiction — 0031 Non-goals vs 0031 §4.1, 0033 §5.2

**Problem.** "per-row capability flags (the server exposes none)" contradicts §4.1, where `can`
reads the per-row `permissions` extra (0033 §5.2; the Schedule stack per K-crud-3).

**Fix.** Replace "per-row capability flags (the server exposes none);" with "per-row capability
flags beyond a stack's `permissions` extra (§4.1);".

### K-crud-12 — minor — contradiction — 0031 §5.3 vs 0005 §13

**Problem.** "A notice is a 0005 `tm-alert` (`kind="info"`), closable, never focused." 0005 §13's
`tm-alert` has "No dismiss affordance", and no amendment adds one.

**Fix.** Replace with "A notice is spec 0005 §13's `tm-alert` (`kind="info"`) followed by the
strip's own icon button "Dismiss" (`tm-alert` has none); it is never focused."

### K-crud-13 — minor — shape gap — 0031 §3.1, §16; 0032 §7.2, §8; 0026 §9.3

**Problem.** 0026 §9.3's `settings` kind requires `state` `{ category, mode, dirty }`; 0032 §14
publishes `category` and 0032 §8 uses `category: 'profile'`. `TmDetailsSource` has no member that
supplies `category`, so the host (0031 §16, "the same way") cannot publish it. 0026 §9.3's owner
column omits `'profile'`.

**Fix — 0031 §3.1 sketch.** Add `readonly category?: Signal<string>; // kind 'settings': the page
context's state.category (spec 0026 §9.3)`. **0031 §16:** replace "A source page publishes kind
`settings` the same way." with "A source page publishes kind `settings` the same way, with `state`
`{ category, mode, dirty }` and `category` from the source." **0032 §7.2 table:** add
"| `category` | the selected tab (`injectTmSettings().category`) |". **0032 §8 table:** add
"| `category` | `'profile'` |". **0026 §9.3**, `settings` row owner: replace "spec 0032 §7; spec
0028 §10.3 with `category: 'notifications'`" with "spec 0032 §7, §8 with `category: 'profile'`;
spec 0028 §10.3 with `category: 'notifications'`".

### K-crud-14 — minor — shape mismatch — 0032 §8 vs 0025 §11

**Problem.** `TmDrafts.key(t, 'core.User', 'me')`, but 0025 §11 types the id as `number | 'new'`.

**Fix — 0032 §8 table.** Replace "`TmDrafts.key(t, 'core.User', 'me')`" with
"`TmDrafts.key(t, 'core.User/me', me.user.id)`, apart from the user's own `/users/{id}` draft, as
spec 0034 §12.1 keys the studio".

### K-crud-15 — minor — contradiction — 0032 §7.7 vs 0028 §8.1

**Problem.** Refresh caches "toasts "Caches refreshed."", but 0028 §8.1's list of admitted outcome
toasts is closed ("a spec admits another by an edit here") and lacks it.

**Fix — 0028 §8.1, outcomes table.** Add "| Caches refreshed | "Caches refreshed." | — | spec 0032
§7.7 |".

### K-crud-16 — minor — shape mismatch — 0030 §19 vs 0026 §3.2

**Problem.** Telemetry values `1–10`, `1–5`, `6–10` use en dashes, which fail 0026 §3.2's value
pattern `^[A-Za-z0-9_.:/@+-]*$` and are dropped in production (the reason for K30 in 0029).

**Fix.** In `tree-load { ids: 0 | 1–10 | 11+` write `0 | 1-10 | 11+`; in `columns-changed { count:
1–5 | 6–10 | 11+` write `1-5 | 6-10 | 11+`.

### K-crud-17 — minor — shape mismatch — 0030 §5.1, §16.2 vs 0012 §2.1

**Problem.** §5.1: "The tree keys are `ParentId` and `SubtreeCount`, or `ActiveSubtreeCount` while
inactive rows are excluded." §16.2's example sends both counts. `ActiveSubtreeCount` exists only on
`ActivatableTreeEntity` (0012 §2.1), so selecting it on a non-activatable tree fails.

**Fix — 0030 §5.1.** Replace the sentence with "The tree keys are `ParentId`, `SubtreeCount` and,
on an activatable tree, `ActiveSubtreeCount` (spec 0012 §2.1)." **§16.2:** replace "or
`ActiveSubtreeCount > 1` while inactive rows are excluded" with "or, on an activatable tree,
`ActiveSubtreeCount > 1` while inactive rows are excluded".

### K-crud-18 — minor — shape mismatch — 0030 §3.4, §16.1 vs 0025 Appendix A

**Problem.** `search.<entity>.view` is one value `{ view?, list? }`, but §3.4 writes `{ view }` and
§16.1 writes `{ list: true }`; each write would erase the other field.

**Fix.** §3.4: replace "and `{ view }` to `search.<entity>.view`" with "and the merged `{ view, list
}` to `search.<entity>.view`". §16.1: replace "and `{ list: true }` to `search.<entity>.view`" with
"and the merged `{ view, list: true }` to `search.<entity>.view`".

### K-crud-19 — minor — contradiction; repetition — 0030 §4.3, §17 vs 0025 §13; 0034 §8.3

**Problem.** §4.3: "pins and recents store canonical strings (spec 0027 §8.2, spec 0025 §13)".
0025 §13 stores recents with no query ("the page restores its own state"). §17 says the list is
"never a recent (spec 0024 §9.4)", but 0024 §9.4 covers pinning only; recents are 0025 §13. 0034
§8.3 repeats §17's sentence word for word.

**Fix — 0030 §4.3.** Replace "and pins and recents store canonical strings (spec 0027 §8.2, spec
0025 §13)." with "and pins store canonical strings (spec 0027 §8.2); recents keep the path only
(spec 0025 §13)." **0030 §17:** replace "(spec 0024 §9.4)" with "(spec 0024 §9.4, spec 0025 §13)".
**0034 §8.3:** replace "The list is never pinned and never a recent (spec 0024 §9.4)." with "Pins
and recents: spec 0030 §17."

### K-crud-20 — minor — dangling pointer (xref list) — 0030 §6.4

**Problem.** The grid row cites bare "§C.5" and "§C.4"; read in 0030 they name sections 0030 does
not have (the mechanical list's two hits). They mean spec 0010.

**Fix.** Replace "Ctrl+Shift+Space select (§C.5)" with "Ctrl+Shift+Space select (spec 0010 §C.5)"
and "Alt+ArrowDown filters (§C.4)" with "Alt+ArrowDown filters (spec 0010 §C.4)".

### K-crud-21 — minor — numbering gap; pointer — 0031 §6; 0023 §7.1

**Problem.** §6 runs §6.1–§6.7, then §6.9 (`tm-details-audit`); §6.8 was cut. 0023 §7.1 cites
"spec 0031 §6.9".

**Fix.** Rename "### 6.9 `tm-details-audit`" to "### 6.8 `tm-details-audit`"; in 0023 §7.1 replace
"("Audit info", spec 0031 §6.9)" with "("Audit info", spec 0031 §6.8)".

### K-crud-22 — minor — wrong pointer — 0032 §3

**Problem.** "search-page bulk actions ignore it: the server refuses per row (spec 0030 §12.3)".
The rule is 0030 §12.2's last line; §12.3 is the run flow.

**Fix.** Replace "(spec 0030 §12.3)" with "(spec 0030 §12.2)".

### K-crud-23 — minor — wrong pointer — 0032 §8

**Problem.** "A saved language re-renders the app through the adopted `me` (spec 0024 §7.2)."
0024 §7.2 is the session-menu switch (`setLanguage`); the rule that `TmLocalization` follows the
launch documents is 0024 §7.1.

**Fix.** Replace "(spec 0024 §7.2)" with "(spec 0024 §7.1)".

### K-crud-24 — minor — known issue K23 — 0031 §12.1

**Problem.** K23: `tm-pager` item mode takes `[total]` with `totalIsLowerBound` (0023 §8.1). 0031
names neither input, nor maps the snapshot's `capped`.

**Fix.** Replace "shown with `result.total` by spec 0023's `tm-pager mode="item"`: "52 / 293", or
"52 / 10,000+" when capped." with "shown by spec 0023 §8.1's `tm-pager mode="item"` with `[index]`
the position, `[total]="result.total"` and `[totalIsLowerBound]="result.capped"`: "52 / 293", or
"52 / 10,000+"."

### K-crud-25 — minor — known issue K26 — 0030 §3.1

**Problem.** `TmSearchPageActionDeclaration.confirm` is `{ titleKey, messageKey }`; the owner shape
(0026 §9.4, K26) is `{ titleKey, messageKey, args? }`, so a declaration cannot pass "{count}".

**Fix.** Replace
`readonly confirm?: { readonly titleKey: string; readonly messageKey: string }; // required when destructive`
with
`readonly confirm?: { readonly titleKey: string; readonly messageKey: string; readonly args?: Signal<TmJsonObject> }; // spec 0026 §9.4's shape; required when destructive`.

### K-crud-26 — minor — shape mismatch — 0032 §5.3 vs 0029 §10.1, 0016 §7.3

**Problem.** (a) `tm-expression-editor clause="filter" rows="4"` binds the string "4" to
`rows: InputSignal<number>` (0030 writes `[rows]="3"`). (b) "Save errors at `[0].Permissions[j]…`
(spec 0014 §3.7)" quotes the server's internal path; the client receives 0016 §7.3's wire form.

**Fix.** (a) Replace `rows="4"` with `[rows]="4"`. (b) Replace "**Save errors** at
`[0].Permissions[j]…` (spec 0014 §3.7)" with "**Save errors** at `entities[0].permissions[j]…`
(spec 0016 §7.3; codes per spec 0014 §3.7)".

### K-crud-27 — minor — shape mismatch — 0032 §4.3 vs 0018 §3.3

**Problem.** "Try again for these" runs `run({}, { keepOpen: true, ids: errorDetails.failed })`;
0018 §3.3 sends `failed` as strings, while `run`'s `ids` is `readonly number[]` (0031 §10.4).

**Fix.** Replace `ids: errorDetails.failed` with `ids: errorDetails.failed.map(Number)` and add
"(`failed` carries the ids as strings, spec 0018 §3.3)".

### K-crud-28 — minor — addendum 3 — 0032 §5.5

**Problem.** Addendum 3: "the Role stack's eager `problems` extra … 0032 cites 0015 §5.3". 0032
§5.5 cites only 0018 §4.3.

**Fix.** Replace "computed from rows the read already loads." with "computed in C# from rows the
read already loads (a computed extra, spec 0015 §5.3)." (0015 §5.3 itself still lacks A3's rule;
that fix belongs to the existing-specs cluster.)

### K-crud-29 — minor — repetition — 0032 Context vs §12.1–§12.3

**Problem.** The Context's bullets for specs 0013, 0014, 0016, 0018, 0020 and 0022 list the same
server members as §12.1–§12.3, member for member; a change needs two edits.

**Fix — 0032 Context.** Replace those six bullets with one: "- specs 0013, 0014, 0016, 0018, 0020,
0022 — the server members these pages read, listed in §12."

### K-crud-30 — minor — repetition — 0030 §19, 0031 §16 vs 0026 §9.3

**Problem.** Both restate 0026 §9.3's minimum `state` for their kind.

**Fix — 0030 §19.** Replace "`state` is `{ query, view, list, selectedIds, selectedCount }` with
at most 200 ids;" with "`state` is spec 0026 §9.3's `search` minimum;". **0031 §16 table**, `state`
row: replace "`{ id, mode, dirty, tab?: { [tabsId]: tabId }, child?: 'Lines:42' }`" with "spec
0026 §9.3's `details` minimum; `tab` maps a tabs id to its selected tab, `child` is `'Lines:42'`".

### K-crud-31 — minor — contradiction; repetition — 0030 §2 vs 0029 §3

**Problem.** Both show "Acme's customers" declaration, with different members: 0029 has `order:
10`, `icon`, a plain `CreditLimit` column and `picker`; 0030 has `orderBy`, a `display` hatch and
`filters`. Acme has one declaration.

**Fix — 0030 §2.** Replace the caption "**Example —** Acme's whole customers search page:" with
"**Example —** the search members of Acme's customers declaration (spec 0029 §3 shows its picker):".
**0029 §3:** replace the caption "**Example —** Acme's customers, with a picker tweak:" with
"**Example —** Acme's customers, launcher and picker members (spec 0030 §2 shows the search
members):" and delete its `columns:` and `tile:` lines.

### K-crud-32 — minor — contradiction — 0024 §9.5 vs 0030 §4.1

**Problem.** 0030 §4.1 omits `size` from the URL at its default (50); 0024 §9.5's canonical rule
omits `page=1`, `inactive=0`, `list=0`, empty `q`, `tab`, `view`, `cols` but not `size`, and its
example prints `size=50`.

**Fix — 0024 §9.5.** In the "Canonical order" row add "`size` when it equals the page's default"
after "`cols`"; change the example `page=2&size=50` to `page=2&size=100`.

### K-crud-33 — minor — process words — 0030 §7.2, §11.1, §13.2, Decisions 1–2, Review flag 11

**Problem.** Item ids and design-file names leak in: headings "(Search Q1)", "(Trees Q1)",
"(`choose-columns.png`)"; Decisions "**Search Q1: …", "**Trees Q1: …"; flag 11 "**Three
departures from the design**".

**Fix.** Headings: "### 7.2 Opening rows and links", "### 11.1 Layout", "### 13.2 Export".
Decision 1: delete "Search Q1: "; Decision 2: delete "Trees Q1: " (capitalize the next word).
Flag 11: replace "**Three departures from the design** (§3.2, §5.3, §14): … not in the field." with
"**Three layout choices** (§3.2, §5.3, §14): … not in the field. Alternatives: 20 rows; hiding the
toggle while rows are selected; the error in the field."

### K-crud-34 — minor — process words — 0031 Decisions 1, 9, 12; Review flags 1, 7, 12

**Problem.** "Details Q1", "Details Q3", "Non-aggregate Q1", "the boilerplate the question wants
gone", "the brief rules it out", "the brief's 422 modal", "the design's 768", "the design's filled
workflow button".

**Fix.** Decision 1: replace "**Details Q1: the host owns…" with "**The host owns…", "(the
boilerplate the question wants gone)" with "(boilerplate in every layout)", and "(the brief rules
it out)" with "(a layout is the distribution's own markup)". Decisions 9 and 12: delete "Details
Q3: " and "Non-aggregate Q1: ". Flag 1: replace "not the brief's 422 modal** (§8.4). Alternative:
the modal." with "not a 422 modal** (§8.4). Alternative: a modal listing the errors." Flag 7:
replace "Alternative: the design's 768." with "Alternative: from `md` (768 px)." Flag 12: replace
"against the design's filled workflow button and 26 px values." with "Alternatives: a filled
workflow button; 26 px view values."

---

## Counts

| Kind | Findings | Count |
|---|---|---|
| 1. Dangling or wrong pointers | 8, 20, 21, 22, 23 | 5 |
| 2. Name and shape mismatches | 13, 14, 16, 17, 18, 26, 27 | 7 |
| 3. Repetition (primary kind) | 29, 30, 31 | 3 (plus repetition inside 1, 5, 19) |
| 4. Contradictions | 9, 10, 11, 12, 15, 19, 32 | 7 |
| 5. Known issues and addenda unresolved | 1, 2, 3, 4, 5, 6, 7, 24, 25, 28 | 10 |
| 6. Process words | 33, 34 | 2 |
| **Total** | 10 major, 24 minor, 0 blocker | 34 |
