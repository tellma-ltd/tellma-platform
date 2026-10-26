# Consistency review — cluster "shell-entity" (specs 0027, 0028, 0029)

Inputs: `docs/specs/0027-shell-launcher-and-tenant-picker.md`, `0028-assignments-and-attention-surfaces.md`,
`0029-client-entity-model.md`, read in full; every outbound pointer opened and checked (about 660
pointers, all section targets exist except the two listed in K-shell-entity-24); inbound pointers
from 0010–0035 into the three specs spot-checked. Binding sources applied in precedence order:
`known-issues.md`, `addenda.md`, `rulings.md`, `design/00-architecture.md`; readability rules from
`style-guide.md`. No spec was edited.

Severity: **blocker** — the text as written cannot be built or contradicts a binding rule in a
way an implementer would follow; **major** — a contract, behaviour or binding item is wrong or
missing; **minor** — a pointer, wording, copy or duplication fix.

Quotes are verbatim from the current files ("…" elides text that stays unchanged).

## Status of the known issues and addenda that concern this cluster

| Item | Concerns | Status | Finding |
|---|---|---|---|
| K3 | 0027 §10.4 cites §B.6 for 44 px menu rows | not resolved: 0010 §B.6 still holds the menu-row rule | 12 |
| K7 | 0027 §11.1 copyright line | not resolved in 0011 §8; 0027 itself is consistent | 13 |
| K11 | 0029 §2.4, §4.3 keys | not resolved in 0013 §7.6, §10.1 | 8 |
| K12 | 0029 §11.3 | resolved: 0029 §11.3 cites 0027 §9.4, and 0027 §1 places `tmSearchNormalize` in the primary | — |
| K13 | 0029 §2.1, Context | not resolved; 0029 and 0016 both state the opposite of the ruling | 3 |
| K14 | 0029 §2.2 `fields[].value.target` | not resolved in 0015 §11.2 | 10 |
| K15 | 0029 §12.1 | resolved: 0031 §7.1 has the "upload pending" row | — |
| K18 | 0027 §12.1 | not resolved in 0024 §7.2 | 7 |
| K24 | 0028 §9.4 | resolved (0028 §9.4; 0023 §5.2 "Not a live region") | — |
| K26 | 0028 §10.2 `confirm` | resolved | — |
| K30 | 0029 §18 | not resolved | 9 |
| K31 | 0027 §3.2 order 900 | not resolved in 0024 §2.3 | 11 |
| K32 | 0027 §9.4 (English labels) | conflicts: 0027 is a consumer of `translate` | 6 |
| K33 | 0028 §8.1, 0027 §8.5 | half: the sign-out row lines up with 0024 §8.4; the pin row does not | 4 |
| K43 | 0028 §6.2, §6.5, §10.2, flag 12 | not resolved (0030 also not resolved) | 1 |
| K44 / addendum 5 | 0028 Context, §3.5, §13 cite 0018 §1.4 | not resolved in 0018 §1.4 | 2 |
| K-0032b | 0029 §3 | resolved: no `details.canDelete` | — |
| K-0034a | 0029 §3 owns the `details` shape | not resolved; 0029 must change with 0031 and 0034 | 5 |
| K-F | 0010 Context must cite 0029 §9 among Part F's consumers | not resolved | 14 |
| Addendum 2 | 0028 cites 0014 §5.3's self-scope list | the assignments half is present; `core.SupportRequest` is 0026's cluster | — |
| Addendum 6 | 0028 §10.3 channels `{ key, defaultEnabled }` | resolved (0028 §10.3; 0024 §3.1) | — |

---

## 1. Known issues and addenda not yet resolved

### K-shell-entity-1 — K43 is not applied to spec 0028

- **Severity:** major. **Where:** 0028 §6.2, §6.5, §10.1, §10.2, §12, Review flag 12.
- **Problem.** K43 rules that opening a notification from the page or the bell goes through the
  route `/{t}/notifications/{id}/open` (a `tmPage` owned by 0028), which calls `inbox/read` and
  replaces the URL with the target; and that the Notifications page gains `selection: true` page
  actions "Mark as read" and "Clear". 0028 still says the opposite: §10.2 "Opening a row follows its
  link and marks nothing read", `rowLink` goes to `TmUrls.target(…)`, §6.2's activation is a
  scripted `read([id])` plus navigation (no real link, so a new tab never marks read), §6.5 still
  exists although ruling 2.7 (Q-14) cut read-on-visit, and Review flag 12 argues the rejected side.
- **Fix (0028).**
  - §10.1 table, add a row after Notifications:
    `| Open a notification | /{t}/notifications/{id}/open | none (a path parameter) | tmPage('notifications/:id/open', …) |`
    (spec 0024 §9.2 admits a page under the segment of an entity the same feature declares).
  - §10.2, replace
    > `rowLink: (row) => row.get('TargetResource') == null ? null`
    > `: inject(TmUrls).target(row.get('TargetResource') as string, row.get('TargetId') as number),`

    with
    > `rowLink: (row) => inject(TmUrls).page(\`notifications/${row.id}/open\`),`

    and add two entries to `pageActions`:
    ```ts
    { id: 'attention.notifications.read', labelKey: 'tm.attention.markRead', icon: 'check', selection: true,
      run: async (search, ids) => { await inject(TmApi).write('inbox/read', { ids }); search.refresh(); } },
    { id: 'attention.notifications.clear', labelKey: 'tm.attention.clear', selection: true, destructive: true,
      confirm: { titleKey: 'tm.attention.clear.title', messageKey: 'tm.attention.clear.message' },
      run: async (search, ids) => { await inject(TmApi).write('inbox/clear', { ids }); search.refresh(); } },
    ```
    (the `run(search, ids)` signature is spec 0030's to state; see Files).
  - §10.2 prose, replace
    > "Opening a row follows its link and marks nothing read. The stack has no save, delete, actions
    > or Excel (spec 0021 §2.2), so the page has no Create, Export, Import or bulk actions."

    with
    > "A row links to `/{t}/notifications/{id}/open`. That page reads the row from the stored `inbox`
    > document, else through `notifications/get`, calls `inbox/read` with `{ ids: [id] }` (not on a
    > read-only tenant, §5.4) and replaces the URL with `TmUrls.target(targetResource, targetId)`, or
    > with `/{t}/notifications` for a row without a target. A row opened in a new tab is therefore
    > marked read too. The stack has no save, delete, actions or Excel (spec 0021 §2.2), so the page
    > has no Create, Export or Import; "Mark as read" and "Clear" act on the selection (spec 0030 §12)."
  - §6.2, replace the **Activation** bullet ("calls `read([id])` when the row is unread, closes the
    panel and navigates to `TmUrls.target(targetResource, targetId)`. A row without a target is only
    marked read; …") with
    > "- **Activation.** A row is a real link to `/{t}/notifications/{id}/open` (§10.2). A click or
    > Enter closes the panel and follows it; Ctrl, ⌘ and middle-click open a new tab, where the route
    > marks the row read the same way. A target the user can no longer open shows that page's
    > not-found or forbidden state."
  - Delete §6.5 "Read on visit" (its last sentence now contradicts the route).
  - Delete Review flag 12 and renumber flag 13.
  - §12 Playwright row: add "a bell row opened in a new tab is marked read; Mark as read and Clear
    over a selection".
- **Files:** 0028; **0030** §3.1 (`TmSearchPageActionDeclaration` gains `selection?: true` and
  `run(search, ids?)`; its Notifications example uses the open route) and §12.2 (the Actions menu
  lists selection page actions) — owned by the search cluster.

### K-shell-entity-2 — K44 / addendum 5: spec 0018 §1.4 does not list Acme's assignment additions

- **Severity:** major. **Where:** 0028 Context ("spec 0018 §1.4 — Acme's assignable sales invoice
  with its `assign` and `post` actions"), §3.5, §13; target 0018 §1.4.
- **Problem.** 0018 §1.4 has no `[Assignable]`, no `assign` or `post` action (it has `close`), no
  `acme.invoice.assigned` type and no demo assignments, so all three 0028 pointers land on text that
  does not say what they claim. 0028 §13 restates the additions instead of citing them.
- **Fix.**
  - **0018 §1.4**, table row `SalesInvoice` (`acme.SalesInvoice`): change the first cell to
    "`SalesInvoice` (`acme.SalesInvoice`), `[DisplayNameSource(nameof(SerialNumber))]`, `[Assignable]`"
    and append "assignments (spec 0028)" to its Exercises cell.
  - **0018 §1.4**, after the **`close`** bullet add:
    > "- **`assign` and `post`.** `SalesInvoiceService` declares `[EntityAction("assign")]` with
    > `AssignInvoiceArguments([property: EntityRef("core.User")] int AssigneeId, string? Comment)`
    > (spec 0015 §11.2), which hands each invoice on through `context.Assignments.Assign`, and
    > `[EntityAction("post")]`, which ends the work through `context.Assignments.Complete` (spec 0028
    > §3.5). `AcmeFeature` registers the channel-only notification type `acme.invoice.assigned`
    > (spec 0028 §3.6)."
  - **0018 §1.4**, replace "- **Development data.** `AcmeSampleCenters` and `AcmeSampleDataStep` (§6.3)."
    with "- **Development data.** `AcmeSampleCenters` and `AcmeSampleDataStep` (§6.3). The step also
    writes two open assignments and one return for the demo users."
  - **0028 §13**, replace the bullet "- **Acme** (spec 0018 §1.4) adds `[Assignable]` on
    `acme.SalesInvoice`, its `assign` and `post` actions, the channel-only type `acme.invoice.assigned`
    (§3.6), and development data that gives the demo users two open assignments and one return. Its
    client adds nothing." with "- **Acme.** Spec 0018 §1.4 lists Acme's assignment additions. Its
    client adds nothing."
- **Files:** 0018, 0028.

### K-shell-entity-3 — K13: `tellma_describe` is still said to render from the client entity model

- **Severity:** major. **Where:** 0029 §2.1 and Context; 0016 §11.5.
- **Problem.** K13 rules that `tellma_describe` renders from the server's entity metadata (store
  types and the default select included), not from the client entity model. 0029 §2.1 says
  "`tellma_describe` renders from the same model (spec 0016 §11.5)" and its Context says
  "`tellma_describe` over this model"; 0016 §11.5 says "the entity's `IClientEntityModel` description
  (spec 0029 §2.1)" and then lists "store type" and "default select", which 0029 §2.3 says the client
  model omits — the contradiction K13 settles.
- **Fix.**
  - **0029 §2.1**, delete the sentence "`tellma_describe` renders from the same model (spec 0016 §11.5)."
  - **0029 Context**, replace "- spec 0016 §2.4, §3.5, §11.5 — the route, diagnostic messages,
    `tellma_describe` over this model." with "- spec 0016 §2.4, §3.4, §3.5 — the `queryex/discover`
    route, `QueryDiagnostic` and `AccessSummary`, the row set."
  - **0016 §11.5**, `tellma_describe` row: replace "with `entity`: the entity's `IClientEntityModel`
    description (spec 0029 §2.1) gated by the tenant's shape" with "with `entity`: the entity's server
    metadata (spec 0012 §3.1's `EntityMetadata` with spec 0015's `StackDescriptor`, store types and the
    default select included) gated by the tenant's shape".
- **Files:** 0029, 0016.

### K-shell-entity-4 — K33: the pinned-toast row and copy do not line up

- **Severity:** major. **Where:** 0028 §8.1 table "Outcomes with no inline home"; 0027 §8.5.
- **Problem.** 0028 admits "A pin added below `md`", but below `md` the bar still shows pills
  (0027 §8.7), and 0027 §8.5 toasts only when the new pin has **no visible chip** (the bar hidden, or
  the pin in More). K33 also fixes the copy as "Pinned {label}." with a full stop. 0027 §8.5 also
  "announces and toasts", which double-announces: the toast host already announces every toast
  (0023 §5.1).
- **Fix.**
  - **0028 §8.1**, replace the row "| A pin added below `md` | "Pinned {label}" | — | spec 0027 §8.5 |"
    with "| A pin added with no visible chip (the bar hidden, or the pin in More) | "Pinned {label}." | — | spec 0027 §8.5 |".
  - **0027 §8.5**, replace "no popover. The shell announces and toasts "Pinned {label}" once, by spec
    0028 §8.1's rule for outcomes with no inline home." with "no popover. The shell toasts "Pinned
    {label}." once (spec 0028 §8.1); the toast host announces it."
- **Files:** 0028, 0027.

### K-shell-entity-5 — K-0034a needs a member in the `tmEntity` shape that 0029 owns

- **Severity:** major. **Where:** 0029 §3 (`details` member); consequence of K-0034a.
- **Problem.** K-0034a has a `page` hatch declare `edit: 'page'`, which hides the host's
  `details.edit` action and in-place edit mode. 0029 §3 "owns the shape" of every declaration member,
  and its `details` type is `{ readonly page; readonly create? }`, so a declaration with `edit: 'page'`
  would not type-check. K-0034a names only 0031 and 0034.
- **Fix (0029 §3).** Replace
  > `details?: TmLazyComponent | { readonly page: TmLazyComponent; readonly create?: TmLazyComponent }; // details page (spec 0031)`

  with
  > `details?: TmLazyComponent | { readonly page: TmLazyComponent; readonly create?: TmLazyComponent; readonly edit?: 'page' }; // details page (spec 0031 §3.7); edit 'page': the page is the editor`
- **Files:** 0029; with K-0034a's own edits to 0031 §3.7 and 0034 §2.3.

### K-shell-entity-6 — K32 deletes the API the launcher needs for English labels

- **Severity:** major (needs a ruling). **Where:** 0027 §3.3, §9.4; 0024 §7.1; known-issues K32.
- **Problem.** K32 deletes `TmLocalization.translate(key, code)` as having no consumer. 0027 is a
  consumer: §9.4 indexes "its label and synonyms in the active language and in English", and §3.3
  matches synonyms "in the active language and English". An Arabic user's index needs the English
  label of `Acme_Customer_Plural`, and 0024 §7.1 offers no other way to read a key in an inactive
  language. 0027 never names the API it uses, so the conflict went unnoticed.
- **Fix.** Ask the orchestrator to amend K32 to "keep `translate`; delete `load(code)`" (English loads
  at boot, 0024 §7.3, so `load` stays unused). Then:
  - **0024 §7.1**, replace the comment of `translate(key: string, code: string): string;` "// a key in a
    loaded, inactive language" with "// a key in a loaded, inactive language: the launcher's English
    terms (spec 0027 §9.4)", and delete the `load(code: string)` line.
  - **0027 §9.4**, candidates table, replace "| every `TmNavItem` | its label and synonyms in the active
    language and in English |" with "| every `TmNavItem` | its label and synonyms in the active language,
    and in English through `TmLocalization.translate(key, 'en')` (English loads at boot, spec 0024 §7.3) |".
  - If the orchestrator keeps K32 as written, 0027 §3.3, §9.4, Decision 4 and Review flag 17 must drop
    English matching instead.
- **Files:** known-issues.md (K32), 0024, 0027.

### K-shell-entity-7 — K18: spec 0024 §7.2 does not adopt the `users/me/locale` result that 0027 says it does

- **Severity:** major. **Where:** 0027 §12.1; 0024 §7.2 step 3.
- **Problem.** 0027 §12.1 says "The caller is spec 0024 §7.2's switch, … which passes the result to
  `TmCacheRefresher.adopt(t, { locale })` (spec 0025 §5.3)". 0024 §7.2 step 3 only writes and retries;
  it never adopts, so the stored `me` keeps the old language and `ModifiedAt` until the next sync, and
  0032 §8's conflict handling misfires. 0024 §7.2 also cites 0018 §3.1 where the action is §3.5.
- **Fix.**
  - **0024 §7.2**, step 3, replace "in a tenant, `TmApi.write('users/me/locale', { language })` (spec
    0018 §3.1); on failure …" with "in a tenant, `TmApi.write('users/me/locale', { language })` (spec
    0018 §3.5), whose `MeLocaleResult` the switch passes to `TmCacheRefresher.adopt(t, { locale })`
    (spec 0025 §5.3); on failure …".
  - **0027 §12.1** then shrinks to one line (see K-shell-entity-35).
- **Files:** 0024, 0027.

### K-shell-entity-8 — K11: spec 0013 lacks the label keys spec 0029 defines

- **Severity:** major. **Where:** 0013 §7.6 table, §10.1; defined by 0029 §2.4 and §4.3.
- **Problem.** 0013 §7.6 and §10.1 list no `<Schema>_<Entity>_New` / `Entity_New` chain, no
  action-field chain and no `Entity_Level`, although 0013 §7.6 says "Spec 0029's emit writes these
  keys". Resource authors read 0013 for key conventions.
- **Fix (0013).**
  - §7.6 table, after the `ActionLabel(T, a)` row add:
    "| `NewLabel(T)` (spec 0029 §2.4) | `<Schema>_<Entity>_New` in the declaring assembly → base assemblies → `Entity_New` in Core's `Strings` ("New") |"
    and "| action field `f` of action `a` (spec 0029 §2.4) | `<Schema>_<Entity>_Action_<Name>_<Field>` → `<Schema>_<Entity>_<Field>` → `<Field>` in Core's `Strings` → the humanized name; `<Field>` drops the trailing `Id` of an `[EntityRef]` property |".
  - §10.1, after the **Display names** bullet add: "- **Entity client keys** (spec 0029 §2.4, §4.3).
    `<Schema>_<Entity>_New` with Core's `Entity_New` ("New"); `<Schema>_<Entity>_Action_<Name>_<Field>`;
    Core's `Entity_Level` ("Level {j}")."
- **Files:** 0013.

### K-shell-entity-9 — K30: telemetry values in 0029 §18 use en dashes

- **Severity:** minor. **Where:** 0029 §18 Telemetry bullet.
- **Problem.** "`tm.entity.names-resolved { count: 1 | 2–10 | 11+ }`" and "`tm.queryex.discover
  { clauses: 1–4, …`" use U+2013, which spec 0026 §3.2's value grammar `^[A-Za-z0-9_.:/@+-]*$` drops.
- **Fix (0029 §18).** Replace "`2–10`" with "`2-10`" and "`1–4`" with "`1-4`".
- **Files:** 0029.

### K-shell-entity-10 — K14: spec 0015 §11.2 does not name `fields[].value.target`

- **Severity:** minor. **Where:** 0015 §11.2 "Entity references in arguments"; target 0029 §2.2.
- **Problem.** 0015 says "The action's argument schema marks the property with that entity (spec 0029
  §2.2)". 0029 carries the entity per argument as `TmActionMetadata.fields[].value.target`, which
  0031 §10.4's generated dialog reads; the JSON Schema is not marked.
- **Fix (0015 §11.2).** Replace "- **Metadata only.** The action's argument schema marks the property
  with that entity (spec 0029 §2.2), so the generated action dialog renders an entity picker for it
  (spec 0031 §10.4)." with "- **Metadata only.** The client metadata carries the entity per argument
  field as `TmActionMetadata.fields[].value.target` (spec 0029 §2.2), which the generated action dialog
  reads to render an entity picker (spec 0031 §10.4)."
- **Files:** 0015.

### K-shell-entity-11 — K31: spec 0024 §2.3's example contradicts the Administration order of 0027 §3.2

- **Severity:** minor. **Where:** 0024 §2.3 example; 0027 §3.2 (Administration 900).
- **Problem.** `tmOverride('section:administration', { order: 90 })` "moves the Administration section
  to the end of the launcher", but its default is already 900 (0027 §3.2), so 90 moves it forward.
- **Fix (0024 §2.3).** Replace "`tmOverride('section:administration', { order: 90 })]` patches the
  Centers declaration and moves the Administration section to the end of the launcher." with
  "`tmOverride('section:administration', { order: 10 })]` patches the Centers declaration and moves the
  Administration section to the front of the launcher (its default order is 900, spec 0027 §3.2)."
- **Files:** 0024.

### K-shell-entity-12 — K3: the 44 px menu-row rule still lives in 0010 §B.6

- **Severity:** minor. **Where:** 0027 §10.4 "with 44 px rows (spec 0010 §B.6)"; 0010 §B.6, §C.10.
- **Problem.** K3 moves the menu-row half of §B.6 to §C.10. Until it does, 0027's pointer is right
  but §C.10 says "Rows are 44 px tall below `sm` (§B.6)". After the move, 0027 must cite §C.10.
- **Fix.**
  - **0010:** move the §B.6 bullet "- **Menu rows.** Below `sm`, `tm-menu` rows are
    `--menu-item-height-touch` …" into §C.10's **Touch rows** bullet; leave §B.6 the form-field rule,
    each citing the other.
  - **0027 §10.4:** replace "with 44 px rows (spec 0010 §B.6)" with "with 44 px rows (spec 0010 §C.10)".
- **Files:** 0010, 0027.

### K-shell-entity-13 — K7: `Tellma:Copyright` is missing from spec 0011 §8

- **Severity:** minor. **Where:** 0011 §8 key table; read by 0024 §3.1 and shown by 0027 §11.1.
- **Problem.** 0024 §3.1 reads `Tellma:Copyright` at emit and 0027 §11.1 shows it, but 0011 §8's
  configuration JSON and key table have no `Copyright`.
- **Fix (0011 §8).** Add `"Copyright": null,` beside `"DisplayName"` in the JSON block, and the table
  row "| `Copyright` | The About dialog's copyright line (spec 0027 §11.1), read at emit into
  `TmClientMetadata.distribution.copyright` (spec 0024 §3.1); null omits it. |".
- **Files:** 0011.

### K-shell-entity-14 — K-F: spec 0010's Context does not cover Part F's consumers, 0029 §9 among them

- **Severity:** minor. **Where:** 0010 Context and Goals; 0029 §9 consumes §F.15.
- **Problem.** 0010's Context says Part F is "Needed by the data access layer (spec 0012) and the
  report engine (spec 0033)", its Goals list Parts A–E only, and "Other specs that carry parts of this
  design" has no Part F line.
- **Fix (0010).** Context Part F bullet: append "and the client's `queryex/discover` (spec 0029 §9)".
  Goals: add "- **Part F.** The Queryex amendments the data access layer, the report engine and
  `queryex/discover` need (§F.1–§F.15)." Other specs: add "- spec 0012 §11.1–§11.2, spec 0013 §8.2,
  spec 0015 §5.2, spec 0029 §9, spec 0033, spec 0035 §6.3 — the hosts and consumers of Part F."
- **Files:** 0010.

---

## 2. Contradictions between specs

### K-shell-entity-15 — Notification settings is declared as a `tmPage` under the reserved segment `me`

- **Severity:** blocker. **Where:** 0028 §10.1 table, §10.3 first sentence; 0024 §2.4, §9.1, §9.2;
  0027 §3.2.
- **Problem.** 0028 declares the page as "`tmPage('me/notifications', …)`" and "A core-feature `tmPage`
  (spec 0024 §2.4)". `me` is a reserved SPA tenant segment (`TellmaRoutes.SpaTenantSegments`:
  settings, me, list; 0024 §3.2, 0011 §5.3), so `tmPage` refuses it as `reserved-path`
  (0024 Appendix A) and the build fails. 0024 §9.2 already lists `'me/notifications'` as a platform
  route ("0032 §7, §8; 0028 §10.3"), and 0027 §3.2 gives it the core entry `core.me.notifications`. A
  `tmPage` would also derive a second, search-only entry with the id `me/notifications`.
- **Fix (0028).**
  - §10.1 table, replace the Declaration cell "`tmPage('me/notifications', …)`" with "a platform route
    (spec 0024 §9.2)".
  - §10.3, replace "A core-feature `tmPage` (spec 0024 §2.4) reads `notification-preferences/get`" with
    "The platform route `/{t}/me/notifications` (spec 0024 §9.2) has route data `{ kind: 'settings',
    skeleton, titleKey, load }`, whose `load` imports `TmNotificationSettingsPage` from
    `/attention-pages`; the launcher entry is spec 0027 §3.2's `core.me.notifications`. The page reads
    `notification-preferences/get`" (the author picks `skeleton` from spec 0024 §4.4's list).
- **Files:** 0028.

### K-shell-entity-16 — `kind: 'level'` contradicts the ruling on level navigations

- **Severity:** major. **Where:** 0029 §4.1 (`TmNavigationDescriptor`), §4.3 Level navigations.
- **Problem.** Ruling 2.8 (M-47, R-0010F-2) settles "`Level{j}` resolved as a self-navigation …
  0010-F's model, no `kind: 'level'`"; 0010 §F.9 resolves levels by name and keeps them out of
  `Navigations`. 0029 §4.1 widens the kind to `TmNavigationMetadata['kind'] | 'level'` and §4.3 says
  "`Level{j}` is a nullable `kind: 'level'` navigation".
- **Fix (0029).**
  - §4.1, replace
    > `interface TmNavigationDescriptor extends Omit<TmNavigationMetadata, 'kind'> { readonly kind: TmNavigationMetadata['kind'] | 'level'; readonly label: Signal<string> }`

    with
    > `interface TmNavigationDescriptor extends TmNavigationMetadata { readonly label: Signal<string>; readonly level?: number } // level: j for Level{j} (§4.3)`
  - §4.3, replace "With `treeLevels ≥ j`, `Level{j}` is a nullable `kind: 'level'` navigation to the
    entity itself, `foreignKey` `Level{j}.Id`, label "Level {j}" (Core `Entity_Level`)." with "With
    `treeLevels ≥ j`, `Level{j}` resolves by name (spec 0010 §F.9) to a nullable self-navigation:
    `kind: 'reference'`, `target` the entity itself, `foreignKey` `Level{j}.Id`, `level` `j`, label
    "Level {j}" (Core `Entity_Level`). It is never listed in `navigations`."
  - §6.2, §7 and §11.2 keep their wording ("a level navigation" = one with `level` set).
- **Files:** 0029.

### K-shell-entity-17 — `core.export.ready`'s arguments omit `fileId` in spec 0021

- **Severity:** minor. **Where:** 0021 §2.3 catalogue row; 0019 §12.3 step 6 adds `fileId`; 0028 §6.2
  and Context rely on it for the Download link.
- **Problem.** 0021 lists the arguments as "`{ fileName, rowCount }`"; 0019 raises "arguments
  `fileName`, `rowCount` and `fileId`".
- **Fix (0021 §2.3).** In the `core.export.ready` row replace "`{ fileName, rowCount }`" with
  "`{ fileName, rowCount, fileId }`".
- **Files:** 0021.

### K-shell-entity-18 — the test-delivery address differs from spec 0021's rule

- **Severity:** minor. **Where:** 0028 §10.3 "Test delivery" row; 0021 §4.4.
- **Problem.** 0028 shows "`me`'s contact email, else "No address""; the server sends to
  `ContactEmail ?? Email` (0021 §4.4), so the page says "No address" for a user the test reaches.
- **Fix (0028 §10.3).** Replace "with the address it would use (`me`'s contact email, else "No
  address")" with "with the address it would use (`me`'s contact email, else its email, as spec 0021
  §4.4 resolves it; "No address" when neither exists)".
- **Files:** 0028.

### K-shell-entity-19 — the "Couldn't copy" toast copy differs from its raiser

- **Severity:** minor. **Where:** 0028 §8.1 "Outcomes with no inline home"; 0026 §8.3.
- **Problem.** 0026 §8.3 toasts "Couldn't copy. Open About, then Diagnostics, to see the details.";
  0028's row says "Couldn't copy". K33 sets the convention that this table's copy lines up with the
  raiser's.
- **Fix (0028 §8.1).** In the row "| Diagnostics copied, or not | "Diagnostics copied" / "Couldn't
  copy" | — | spec 0026 §8.3 |" replace "Couldn't copy" with "Couldn't copy. Open About, then
  Diagnostics, to see the details.".
- **Files:** 0028.

### K-shell-entity-20 — the `QueryexProfile` comment implies `me()` is barred from queries

- **Severity:** minor. **Where:** 0029 §9 (`QueryexProfile`).
- **Problem.** "`// Permission: one filter, no parameters, me() allowed`" implies `me()` is not allowed
  under `Query`, but filters use `me()` everywhere (0028 §10.4 tabs, 0030 §2 "CreatedById = me()",
  0008 §10.6).
- **Fix (0029 §9).** Replace the comment with "`// Permission: one filter, no parameters, validated as
  role save validates it (step 3)`".
- **Files:** 0029.

---

## 3. Name and shape mismatches with the owning spec

### K-shell-entity-21 — 0027 needs a `lang` member that 0010 §C.10's menu items do not have

- **Severity:** major. **Where:** 0027 §10.1 row 4 ("submenu of radio items with `lang`") and §6.1's
  language menu; owner 0010 §C.10 `TmMenuItem`.
- **Problem.** The session and tenantless language menus list native names ("العربية" inside an
  English menu) and must mark each row's language (WCAG 3.1.2). `TmMenuItem` in §C.10 has no `lang`
  member, so 0027 cannot be built on the data-driven menu as written.
- **Fix.** **0010 §C.10**, add to `TmMenuItem` "`readonly lang?: string; // new: the label's BCP 47
  language, set as lang on the row (native language names)`". 0027 keeps its text. (Alternative if 0010's owner declines: 0027 §10.1 drops "with `lang`" and
  records the WCAG 3.1.2 gap as a review flag.)
- **Files:** 0010 (or 0027).

### K-shell-entity-22 — 0027 §7.4 reads the client documents in C# casing

- **Severity:** minor. **Where:** 0027 §7.4.
- **Problem.** "`TmUrls.blob('user-image', me.User.ImageId, …)`", "`me.User`'s name" and
  "`settings.Names` (spec 0013 §5.6)" use the server's PascalCase; the client documents are camelCase
  (0025 §5.3 `me.user.preferredLanguage`, 0032 §8 `me.user.id`).
- **Fix (0027 §7.4).** Replace "`me.User.ImageId`" with "`me.user.imageId`", "`me.User`'s name" with
  "`me.user`'s name", and "`settings.Names` (spec 0013 §5.6)" with "`settings.names` (spec 0013 §5.6's
  `Names`)".
- **Files:** 0027.

### K-shell-entity-23 — 0028 names its leader-document constant two ways

- **Severity:** minor. **Where:** 0028 §1 primary row; §5.1.
- **Problem.** §1 lists "the two `TM_LEADER_DOCUMENTS` entries (§5.1)"; §5.1 declares the exported
  constant `TM_ATTENTION_DOCUMENTS` (0025 §8.6 owns `TM_LEADER_DOCUMENTS`, the token).
- **Fix (0028 §1).** Replace "the two `TM_LEADER_DOCUMENTS` entries (§5.1)" with
  "`TM_ATTENTION_DOCUMENTS`, the two `TM_LEADER_DOCUMENTS` entries (§5.1)".
- **Files:** 0028.

---

## 4. Dangling or wrong pointers

### K-shell-entity-24 — 0027 cites a non-existent spec 0023 §6.4

- **Severity:** minor. **Where:** 0027 §6.2 (line 384) and §9.6 (line 833).
- **Problem.** 0023 has no §6.4; `tm-search-field`, `TmSearch.value` and the debounced `search` output
  are §6.2.
- **Fix (0027).** §6.2: "(`value`, spec 0023 §6.4)" → "(`value`, spec 0023 §6.2)". §9.6: "debounced
  `search` output (spec 0023 §6.4)" → "debounced `search` output (spec 0023 §6.2)".
- **Files:** 0027.

### K-shell-entity-25 — 0027 §11.1 cites the splitter for the description list

- **Severity:** minor. **Where:** 0027 §11.1 item 3.
- **Problem.** "`dl[tmDescriptionList]` (spec 0023 §7.3)": §7.3 is `splitter`; the description list is §7.1.
- **Fix (0027 §11.1).** "(spec 0023 §7.3)" → "(spec 0023 §7.1)".
- **Files:** 0027.

### K-shell-entity-26 — 0027 §5.1 places the idle gate in 0028 §9.1

- **Severity:** minor. **Where:** 0027 §5.1 table, Banner row.
- **Problem.** "(spec 0028 §9.1, whose idle gate times entries and exits)": the gate is 0028 §9.3.
- **Fix (0027 §5.1).** Replace with "(spec 0028 §9.1; its idle gate, §9.3, times entries and exits)".
- **Files:** 0027.

### K-shell-entity-27 — 0028 §6.1 and §6.4 cite sections that do not state the rule

- **Severity:** minor. **Where:** 0028 §6.1, §6.4.
- **Problem.** §6.1 "static under `prefers-reduced-motion` (spec 0023 §4.4)": the reduced-motion rule
  is 0023 §2.2 ("No enter or exit animation…"). §6.4 "rows are 44 px tall below `sm` (spec 0010 §B.6)":
  §B.6 covers form controls and menu rows, not panel rows; the 44 px panel row is 0028's own rule.
- **Fix (0028).** §6.1: "(spec 0023 §4.4)" → "(spec 0023 §2.2)". §6.4: "rows are 44 px tall below `sm`
  (spec 0010 §B.6)" → "rows are 44 px tall below `sm`, the touch height of spec 0010 §B.6".
- **Files:** 0028.

### K-shell-entity-28 — 0028 §9.2 cites 0024 §5.5 for a rule 0024 does not state

- **Severity:** minor. **Where:** 0028 §9.2 **Dismiss**.
- **Problem.** "the update banner stays dismissed for that version (spec 0024 §5.5)": 0024 §5.5
  defines `TmUpdates.available`, not the per-version dismissal, which is 0028's own (§9.5).
- **Fix (0028 §9.2).** Replace with "the update banner stays dismissed for that version, the value of
  `TmUpdates.available()` (spec 0024 §5.5)".
- **Files:** 0028.

### K-shell-entity-29 — 0028 cites 0027 §4.3 for shortcut ids listed in 0027 Appendix A

- **Severity:** minor. **Where:** 0028 Context ("spec 0027 §3.2, §4.3, §5.1, §7.2 — … the two
  shortcuts …"), §6.1 ("`tm.attention.notifications` (spec 0027 §4.3)"), §7.1 ("`Alt+Shift+A`, spec
  0027 §4.3").
- **Problem.** The ids and keys are in 0027 Appendix A (§4.3 holds the families and says "Appendix A
  holds the closed lists").
- **Fix (0028).** In all three places replace "spec 0027 §4.3" with "spec 0027 Appendix A" (Context:
  "spec 0027 §3.2, §5.1, §7.2, Appendix A").
- **Files:** 0028.

### K-shell-entity-30 — the "Exporting…" row omits one of its raisers

- **Severity:** minor. **Where:** 0028 §8.1, row "A synchronous export in progress".
- **Problem.** Raised by "spec 0030 §13.2" only; 0034 §9 raises the same sticky toast with Cancel.
- **Fix (0028 §8.1).** "| spec 0030 §13.2 |" → "| specs 0030 §13.2, 0034 §9 |" in that row.
- **Files:** 0028.

### K-shell-entity-31 — 0029 §2.1 cites spec 0012's sections as if they were local

- **Severity:** minor. **Where:** 0029 §2.1 **Sources** (listed by the mechanical check as "§3.1").
- **Problem.** "Spec 0012's metadata and `All`-shape schema (§3.1, §11.1)" reads as 0029 §3.1 (absent)
  and §11.1 (the path picker).
- **Fix (0029 §2.1).** Replace with "Spec 0012's metadata and `All`-shape schema (spec 0012 §3.1, §11.1)".
- **Files:** 0029.

### K-shell-entity-32 — 0029 cites 0024 §2.2 for the configuration codes

- **Severity:** minor. **Where:** 0029 Context ("spec 0024 §2.2, §3.2, §4.4 — config problem codes,
  …"), §2.4 ("warns `label-missing` (spec 0024 §2.2)"), §3 ("reporting spec 0024 §2.2's codes").
- **Problem.** The codes are in 0024 Appendix A; `label-missing`'s handling is 0024 §3.6.
- **Fix (0029).** Context: "spec 0024 §2.2, §3.2, §4.4" → "spec 0024 §3.2, §4.4, Appendix A". §2.4:
  "(spec 0024 §2.2)" → "(spec 0024 §3.6)". §3: "spec 0024 §2.2's codes" → "spec 0024 Appendix A's
  codes".
- **Files:** 0029.

### K-shell-entity-33 — inbound: 0023 §4.1 cites 0028 §6.1 for the badge's accessible name

- **Severity:** minor. **Where:** 0023 §4.1 **Overlay counters**; target 0028 §5.2.
- **Problem.** "(the button's name carries the count, "Notifications, 3 new", spec 0028 §6.1)": the
  naming rule is 0028 §5.2's **Accessible names**.
- **Fix (0023 §4.1).** "spec 0028 §6.1" → "spec 0028 §5.2".
- **Files:** 0023.

### K-shell-entity-34 — inbound: 0010 §E.1 cites 0029 §14.3 for the description line

- **Severity:** minor. **Where:** 0010 §E.1 ("**Requested by:** spec 0029 §14.3" and "What the
  description holds is the binding's choice (spec 0029 §14.3)"); target 0029 §14.2 step 4.
- **Problem.** 0029 §14.3 is Labels; the second line is defined in §14.2 step 4 ("Description").
- **Fix (0010 §E.1).** Both "spec 0029 §14.3" → "spec 0029 §14.2".
- **Files:** 0010.

---

## 5. Repetition

### K-shell-entity-35 — 0027 §12 restates spec 0018's and spec 0011's contracts

- **Severity:** minor. **Where:** 0027 §12.1, §12.2 (ruling 2.6: "§12.1 cite 0018").
- **Problem.** §12.1 restates the action's route, body, result, round trips, `ModifiedAt` stamp and
  tag bump (0018 §3.5); §12.2 restates `DistributionLinks(Terms, Privacy)`, its URL rule and its keys
  (0011 §5.9, §8). Both owners already state them.
- **Fix (0027 §12).** Replace §12.1's two bullets with: "The session menu's language and calendar rows
  call spec 0024 §7.2's switch, which writes `users/me/locale` (spec 0018 §3.5) and adopts the result
  (spec 0025 §5.3). A My profile form opened earlier meets the concurrency dialog on save (spec 0032
  §8)." Replace §12.2's paragraph with: "The About dialog reads `links.terms` and `links.privacy` of
  `DistributionInfo` (spec 0011 §5.9) from `TmLaunchCache.distribution` (spec 0025 §3.1), so legal
  pages move without a rebuild." (Depends on K-shell-entity-7 for the adopt sentence.)
- **Files:** 0027 (0024 per K-shell-entity-7).

### K-shell-entity-36 — 0029 §3 restates the condition table of spec 0024 Appendix A

- **Severity:** minor. **Where:** 0029 §3 **Build check** and its four-row code table.
- **Problem.** The four rows (`unknown-path`, `unknown-action`, `operation-missing`,
  `entity-not-routable`) and their conditions are also in 0024 Appendix A, the central closed list
  (ruling 2.3: "Config problem codes centralised here"). A change needs two edits.
- **Fix (0029 §3).** Replace the bullet's tail "…against the Entities chunk, reporting spec 0024
  §2.2's codes:" and the table with "…against the Entities chunk. It reports `unknown-path` (paths
  resolve by §4.3), `unknown-action`, `operation-missing` and `entity-not-routable`, whose conditions
  spec 0024 Appendix A lists." (also resolves the §3 half of K-shell-entity-32).
- **Files:** 0029.

### K-shell-entity-37 — 0028 §7.4 restates spec 0035's home widgets and cites the wrong section

- **Severity:** minor. **Where:** 0028 §7.4; owner 0035 §9.2 (the widget table with counts, View all
  and empty states).
- **Problem.** §7.4 restates the counts (`open`, `openByMe`), the View all URLs and the refresh call,
  which 0035 §9.2 owns, and cites §15.3, which only places the widgets.
- **Fix (0028 §7.4).** Replace the paragraph with "Spec 0035 §9.2's widgets `core.assignments-to-me` and
  `core.assignments-by-me` render `latest` and `latestByMe` through `tm-assignment-list variant="home"`
  from the stored document, up to `SummaryLatestCount` rows each."
- **Files:** 0028.

---

## 6. Process words in the specs

### K-shell-entity-38 — 0027 cites the brief's rule and question ids and "the design"

- **Severity:** minor. **Where:** 0027 §6.2 "(rule 2D)", §8.7 "(rule 2D)", §9.6 "(rule 2C)"; §14
  "(2A)" … "(2F)"; §3.2 "as the design draws"; Decision 3 "the design draws flat sections";
  Decision 18 "(Q1 format, Q2 merging with the assistant)" and "(prejudges Q2)"; Review flag 5 "the
  design's `?`"; Review flag 9 "the design's currency and location" and "the design's sub-line".
- **Problem.** "2A–2F" and "Q1/Q2" are the brief's item ids; "the design" points at mockups no reader
  of the frozen spec has. The style guide bars both.
- **Fix (0027).**
  - §6.2 "Recency never moves a row (rule 2D)." → "Recency never moves a row, so the layout stays stable."
  - §8.7 "so nothing reflows (rule 2D)." → "so nothing reflows."
  - §9.6 "each aborted when superseded (rule 2C)." → "each aborted when superseded."
  - §14: drop "(2A)", "(2B)", "(2C)", "(2D)", "(2E)", "(2F)" after the bold leads.
  - §3.2 "so Analysis and Administration close the launcher, as the design draws." → "so Analysis and
    Administration close the launcher."
  - Decision 3 "— the design draws flat sections of up to 30 entries —" → "— the launcher shows flat
    sections of up to 30 entries —".
  - Decision 18 "**User help (Q1 format, Q2 merging with the assistant) is deferred; its seams are
    here:**" → "**User help (its format, and whether it merges with the assistant) is deferred; its
    seams are here:**"; "merging into the assistant slot now (prejudges Q2)" → "merging into the
    assistant slot now (prejudges the merge)".
  - Flag 5 "**`Mod+/`, not the design's `?`**" → "**`Mod+/`, not `?`**"; flag 9 "the design's currency
    and location via a feature hook. Flips if the design's sub-line is wanted." → "the tenant's currency
    and location via a feature hook. Flips if that sub-line is wanted."
- **Files:** 0027.

### K-shell-entity-39 — 0029's Decisions record cites brief question ids

- **Severity:** minor. **Where:** 0029 Decisions 3 ("**Details Q2: the display name is server metadata
  rendered alike on both sides**") and 11 ("**Stock pages Q1: one generic picker binding**").
- **Problem.** "Details Q2" and "Stock pages Q1" are the brief's item ids.
- **Fix (0029).** Decision 3 → "**The display name is server metadata rendered alike on both sides**";
  Decision 11 → "**One generic picker binding**".
- **Files:** 0029.

### K-shell-entity-40 — 0028 narrates spec edits and future edits

- **Severity:** minor. **Where:** 0028 §8.1 "each toasting once (a spec admits another by an edit
  here)"; §13 "the edits in the specs listed under Context are in place".
- **Problem.** Both describe the editing process; the spec is frozen once merged.
- **Fix (0028).** §8.1: "**Outcomes with no inline home**, each toasting once (a spec admits another
  by an edit here):" → "**Outcomes with no inline home**, each toasting once; this list is closed:".
  §13: "registered; the edits in the specs listed under Context are in place." → "registered; the
  members the Context list names in other specs exist."
- **Files:** 0028.
