# Consistency check — cluster "foundation"

Scope: specs 0023, 0024, 0025, 0026 and spec 0010 Parts A–E (plus 0010's shared front matter).
Binding sources applied in order: `known-issues.md`, `addenda.md`, `rulings.md`,
`design/00-architecture.md`; readability per `style-guide.md`. No spec was edited.

## Status of known issues and addenda that concern this cluster

| Item | Status | Finding |
|---|---|---|
| K1 (0010 §D.7, §D.8 quarter pattern, Ethiopic Q4, `timeStyle: 'none'`) | resolved (Part D flags 4, 5) | — |
| K3 (0010 §B.6 menu half → §C.10) | **not resolved** | K-foundation-9 |
| K21 (0010 §E.2 chips from `tm-chip`, focus order) | **not resolved** | K-foundation-10 |
| K-F (0010 front matter covers Part F) | **not resolved** | K-foundation-14 |
| K5 (metadata chunk caching in 0011, 0016) | resolved (both cite 0024 §6) | — |
| K7 (`Tellma:Copyright` in 0011 §8) | **not resolved** | K-foundation-13 |
| A7 / addendum 7 (early `Tellma-Trace-Id`, `Tellma-Build` in 0011 §5.1) | **not resolved** | K-foundation-7 |
| A2 / addendum 2 (support requests self-scoped in 0014 §5.3, 0017 §5.2) | **not resolved** (0026 itself is right) | K-foundation-8 |
| Addendum 6 (`notificationChannels` as `{ key, defaultEnabled }[]`) | resolved in 0024 §3.1 and 0028 §10.3 | — |
| K17 (0024 §10.3 body first, observers one task later) | **not resolved** | K-foundation-2 |
| K18 (0024 §7.2 adopts `users/me/locale`) | **not resolved** | K-foundation-3 |
| K31 (0024 §2.3 `tmOverride` example) | **not resolved** | K-foundation-11 |
| K32 (0024 §7.1 `translate`, `load`) | **not resolved** | K-foundation-12 |
| K38 (0024 §10.6 404 on a details save) | **not resolved** | K-foundation-4 |
| K39 (0024 §9.2 `canDeactivate`) | **not resolved** | K-foundation-5 |
| K33 (sign-out toast lines up, 0024 §8.4 ↔ 0028 §8.1) | resolved | — |
| K24 (`tm-banner-bar` has no live role) | resolved in 0023 §5.2 and 0028 §9.4 | — |
| K22, K23 (tiles names; pager `[total]`) | resolved on the owner side (0023 §8.1, §8.5) | — |
| K26 (`TmPageAction.confirm` shape) | resolved in 0026 §9.4; 0024 does not use it | — |
| K20 (parameter preference fallbacks) | resolved on the owner side (0025 Appendix A) | — |
| K30 (0029 §18 values fit 0026 §3.2's grammar) | **not resolved** in 0029 | K-foundation-15 |
| K43 (the open route, as it touches 0024 §9) | blocked by a 0024 rule conflict | K-foundation-6 |

## Findings

### K-foundation-1 — major — contradiction and repetition: the session clean-up deletes drafts on the expiry path

- **Where:** spec 0025 §2.6 ("The session clean-up"), spec 0025 §11 (Lifetime), spec 0024 §8.4
  (Clean-up row), spec 0024 §8.6 step 2; spec 0010 §D.5 for the area rule.
- **Problem:** 0025 §2.6 owns the clean-up and defines it with `keep` = `tm-app` and `tm.boot` only.
  Spec 0010 §D.5's `clearAll` deletes this tab's `sessionStorage` keys `tm.*`, so that clean-up
  deletes `tm.draft:*` (and `tm.login`). 0025 §2.6's own justification ("Sweeps never touch session
  storage, so drafts survive the expiry path") is about `sweep`, not `clearAll`, so the owner text
  breaks 0025 §11 ("drafts survive the expiry path"). 0024 §8.6 patches it with its own keep set,
  and 0024 §8.4 restates the whole sequence: three places for one rule, two of them wrong.
- **Fix:**
  - 0025 §2.6, replace "**The session clean-up**, made by spec 0024 §8.4 (and §8.5, §8.6), is three
    calls in order: `sweep('session-ended')`; `TmClientCache.clearAll({ keep })` keeping `tm-app`
    and `tm.boot` (spec 0010 §D.5); `boot.patch({ sub: null, lastTenantId: null })`. Sweeps never
    touch session storage, so drafts survive the expiry path (§11)." with:
    "**The session clean-up**, run by spec 0024 §8.4, §8.5 and §8.6, is three calls in order:
    1. `sweep('session-ended')`;
    2. `TmClientCache.clearAll({ keep })` (spec 0010 §D.5), keeping `tm-app` and `tm.boot`, and on
       the expiry path also every `tm.draft:*` key, so drafts survive it (§11);
    3. `boot.patch({ sub: null, lastTenantId: null })`."
  - 0024 §8.4 Clean-up row, replace "in order: `TmClientStore.sweep('session-ended')` (0025 §2.6);
    `TmClientCache.clearAll({ keep })` keeping `tm-app` and `tm.boot` (0010 §D.5);
    `boot.patch({ sub: null, lastTenantId: null })`; `TmDrafts.clearAll(sub)`" with "spec 0025
    §2.6's session clean-up, then `TmDrafts.clearAll(sub)`".
  - 0024 §8.6 step 2, replace "the tab runs §8.4's clean-up except the drafts (`clearAll` also keeps
    `tm.draft:*`), deletes …" with "the tab runs spec 0025 §2.6's session clean-up for the expiry
    path (drafts kept), deletes …".
  - Files: `0025-client-state-and-cross-tab-sync.md`, `0024-client-app-foundation.md`.

### K-foundation-2 — major — K17 not resolved: who defers the header comparison

- **Where:** spec 0024 §10.3 step 5; spec 0025 §5.3 (`observe` paragraph).
- **Problem:** K17 rules that `TmApi` delivers the body to the caller first and feeds observers one
  task later. 0024 §10.3 lists "5. Observers" before "6. Status" with no ordering rule, and 0025 §5.3
  says the opposite mechanism ("`TmApi` calls it before settling the call"), with `observe` itself
  deferring. 0026 §8.1 cites "steps 3 and 5" for `begin`/`end`, so step 5 must keep `end`.
- **Fix:**
  - 0024 §10.3, replace the row "| 5. Observers | `Tellma-Version-Tags` go to
    `TmCacheRefresher.observe(t, tags)` (0025 §5.3); the request-log handle's `end` records outcome,
    status, code and `Tellma-Build` |" with "| 5. Observers | the request-log handle's `end` records
    outcome, status, code and `Tellma-Build`. `Tellma-Version-Tags` go to
    `TmCacheRefresher.observe(t, tags)` (0025 §5.3) one task after step 6 delivers the body, so a
    write's caller adopts its own result first |".
  - 0025 §5.3, replace "**`observe(t, tags)`** compares one task later: `TmApi` calls it before
    settling the call (spec 0024 §10.3), so by then a write's caller has passed its result to
    `adopt`." with "**`observe(t, tags)`** compares at once. `TmApi` calls it one task after
    delivering the body (spec 0024 §10.3 step 5), so a write's caller has already passed its result
    to `adopt`."
  - Files: `0024-client-app-foundation.md`, `0025-client-state-and-cross-tab-sync.md`.

### K-foundation-3 — major — K18 not resolved: the language switch never adopts its result

- **Where:** spec 0024 §7.2 step 3; spec 0027 §12.1 ("The caller").
- **Problem:** 0025 §5.3, §9.5 and 0027 §12.1 all say the `users/me/locale` caller passes its
  `MeLocaleResult` to `TmCacheRefresher.adopt(t, { locale })` and name 0024 §7.2 as that caller;
  0024 §7.2 does not do it, so the tab's own write triggers a `client/sync`.
- **Fix:**
  - 0024 §7.2, replace step 3 "| 3 | `boot.patch({ lang, dir })` (0025 §2.1); in a tenant,
    `TmApi.write('users/me/locale', { language })` (spec 0018 §3.1); on failure the switch stands,
    and the write is retried once when `TmApi.online` turns true |" with "| 3 |
    `boot.patch({ lang, dir })` (0025 §2.1); in a tenant, `TmApi.write('users/me/locale',
    { language })` (spec 0018 §3.1), whose `MeLocaleResult` goes to `TmCacheRefresher.adopt(t,
    { locale })` (0025 §5.3, §9.5); on failure the switch stands, and the write is retried once when
    `TmApi.online` turns true |".
  - 0027 §12.1, the "The caller" bullet then cites instead of restating: "**The caller** is spec
    0024 §7.2's switch, from rows 4 and 5. A My profile form opened earlier meets the concurrency
    dialog on save (spec 0032 §8), instead of restoring the old language."
  - Files: `0024-client-app-foundation.md`, `0027-shell-launcher-and-tenant-picker.md`.

### K-foundation-4 — major — K38 not resolved: 404 on a details save

- **Where:** spec 0024 §10.6, row "404 `not-found`"; owner of the dialog: spec 0031 §8 (row "404
  `not-found` on an update") and §9.2.
- **Problem:** 0024 sends a write's 404 to the 400 dialog; 0031 shows the deletion dialog.
- **Fix:** 0024 §10.6, replace "| 404 `not-found` | `not-found` | a read: inline (0031 §3.5); a
  write: 400 dialog |" with "| 404 `not-found` | `not-found` | a read: inline (0031 §3.5); a details
  save: the deletion dialog (0031 §9.2); another write: 400 dialog |". File:
  `0024-client-app-foundation.md`.

### K-foundation-5 — major — K39 not resolved: route assembly has no `canDeactivate`

- **Where:** spec 0024 §9.2, "Page routes" row; consumers spec 0031 §11.3 (`tmLeaveGuard`, "The
  page route's `canDeactivate` runs the checks registered in its page scope") and spec 0034 §12
  (studio uses `tmLeaveGuard`).
- **Fix:** 0024 §9.2, replace "| Page routes | every one has `TmRouteHost` as its component and a
  `TmRouteData` (§4.4); …" with "| Page routes | every one has `TmRouteHost` as its component, a
  `TmRouteData` (§4.4) and a `canDeactivate` that runs the leave checks registered in the page scope
  (`tmLeaveGuard`, 0031 §11.3); …". File: `0024-client-app-foundation.md`.

### K-foundation-6 — major — contradiction inside 0024: page paths under an own entity segment

- **Where:** spec 0024 §2.4 (`path` row), §9.2 ("Under an entity segment" row), Appendix A
  (`reserved-path`); consumers: K43's `/{t}/notifications/{id}/open` `tmPage` (spec 0028), 0034's
  studio route.
- **Problem:** §2.4 says "the first segment is no entity segment, reserved word (§9.1) or `new`" and
  Appendix A makes any such path `reserved-path` (citing §9.1), while §9.2 admits "the pages a
  feature declares under the segment of an entity it declares itself". The config check would
  refuse K43's binding open route (the core feature declares both `core.Notification` and the page).
- **Fix:**
  - 0024 §2.4 `path` row, replace "the first segment is no entity segment, reserved word (§9.1) or
    `new`" with "the first segment is no reserved word (§9.1) or `new`, and is an entity segment only
    when the same feature declares that entity (§9.2)".
  - 0024 Appendix A, replace "| `reserved-path` | a page path or coded-report key whose first segment
    is an entity segment, a routing word or `new` | §9.1 |" with "| `reserved-path` | a page path or
    coded-report key whose first segment is a routing word, `new`, or the segment of an entity
    another feature declares | §9.1, §9.2 |".
  - File: `0024-client-app-foundation.md`. (0028 still has to add the K43 route; that is the
    attention cluster's finding.)

### K-foundation-7 — major — addendum 7 not resolved: early headers in 0011 §5.1

- **Where:** spec 0011 §5.1 (`UseTellma()` bullet). Depended on by spec 0024 §5.6 (the `disabled`
  verdict compares the 412's `Tellma-Build`) and §10.6 (trace id from `Tellma-Trace-Id`); spec 0016
  §6.2 already says `UseTellma` stamps them first.
- **Problem:** 0011 §5.1's fixed pipeline order has no header stamp at all.
- **Fix:** 0011 §5.1, replace "**`UseTellma()`** installs the pipeline in this fixed order and nothing
  else: forwarded headers" with "**`UseTellma()`** installs the pipeline in this fixed order and
  nothing else: the stamp of `Tellma-Trace-Id` and `Tellma-Build` on every response, so early
  refusals (CSRF 403, 412, 429, tenant 404) carry them (spec 0016 §6.2); forwarded headers". File:
  `0011-distribution-host-and-multitenancy.md`.

### K-foundation-8 — major — addendum 2 not resolved: support requests are not in the self-scope list

- **Where:** spec 0014 §5.3 (self-scope bullet); spec 0017 §5.2 (`support-bundle` sentence).
  Depended on by spec 0026 §12.3 (`SupportRequestAccessCriteria`), §12.6.
- **Fix:**
  - 0014 §5.3, replace "Users, jobs, exports, imports, notifications and assignments are self-scoped
    by a bespoke criterion that their owning spec's `IAccessCriteriaProvider` supplies (spec 0018
    §3.2, spec 0020 §11.2, spec 0019 §12.1, spec 0021 §2.2, spec 0028 §2.2)" with "Users, jobs,
    exports, imports, notifications, assignments and support requests are self-scoped by a bespoke
    criterion that their owning spec's `IAccessCriteriaProvider` supplies (spec 0018 §3.2, spec 0020
    §11.2, spec 0019 §12.1, spec 0021 §2.2, spec 0028 §2.2, spec 0026 §12.3)".
  - 0017 §5.2, replace "`support-bundle` is `OwnerRead` through `core.SupportRequest`, which only
    holders of `core.SupportRequest × Read` read (spec 0026 §12.6)." with "`support-bundle` is
    `OwnerRead` through `core.SupportRequest`, which its submitter and holders of
    `core.SupportRequest × Read` read (spec 0026 §12.6)."
  - Files: `0014-users-roles-and-permissions.md`, `0017-blob-storage.md`.

### K-foundation-9 — minor — K3 not resolved: the menu-row touch rule still lives in §B.6

- **Where:** spec 0010 §B.6 (Target, Why, "Menu rows" bullet, Compatibility/Tests, Part B DoD
  "Touch"), §C.10 ("Touch rows" bullet); spec 0027 §10.4.
- **Fix:**
  - 0010 §B.6: Target becomes "0002 §5 (`TM_FORM_FIELD_DEFAULTS`)"; Requested by becomes "spec 0023
    §2"; Why becomes "controls in page content reach a 46 px touch height on phones; menu rows
    follow §C.10."; delete the "**Menu rows.**" bullet; in Tests replace "a field is 46 px tall and a
    menu row 44 px" with "a field is 46 px tall"; in Part B DoD replace "§B.6 shipped; the menu token
    in the preset;" with "§B.6 shipped;".
  - 0010 §C.10, replace "- **Touch rows.** Rows are 44 px tall below `sm` (§B.6)." with "- **Touch
    rows.** Below `sm`, rows are `--menu-item-height-touch` (a new token, 44 px) tall; a
    `(width < 576px)` media query applies it, because the panel lives in the overlay container. Type
    sizes are unchanged; the grid's context menu follows. Controls follow §B.6." Add "spec 0027
    §10.4" to §C.10's Requested by, the token to Part C's DoD Tokens bullet and "a menu row is 44 px
    at 390 px" to Part C Testing.
  - 0027 §10.4, replace "with 44 px rows (spec 0010 §B.6)" with "with 44 px rows (spec 0010 §C.10)".
  - Files: `0010-amendments-to-implemented-specs.md`, `0027-shell-launcher-and-tenant-picker.md`.

### K-foundation-10 — minor — K21 not resolved: the multi-picker's chips and their focus order

- **Where:** spec 0010 §E.2 (Composition, keyboard table, ARIA); spec 0023 Decision 4.
- **Problem:** §E.2 names `tm-chip` but not `TmChip.focus()`; its Backspace/Delete row says "focus
  moves to the next chip, else the input", missing "else the previous" (owner: 0023 §2.1's chips
  row); it defines its own remove-button string `tmUi.entityPicker.removeChip`, which duplicates the
  composed chip's `tmUi.chips.remove` (0023 §10). 0023 Decision 4 says "the multi-selects render their
  own values (spec 0010 §B.4, §E.2)", which contradicts §E.2's composition.
- **Fix:**
  - 0010 §E.2, replace "**Composition.** The field holds a chip row of spec 0023's `tm-chip` (spec
    0023 §4) and an inner" with "**Composition.** The field holds a chip row of spec 0023's removable
    `tm-chip`s, focused through `TmChip.focus()` (spec 0023 §4.3), and an inner".
  - 0010 §E.2 keyboard table, replace "| Backspace / Delete | chip | removes it; focus moves to the
    next chip, else the input |" with "| Backspace / Delete | chip | removes it; focus moves to the
    next chip, else the previous, else the input (spec 0023 §2.1) |".
  - 0010 §E.2 ARIA, replace "each with a remove button named `tmUi.entityPicker.removeChip`
    ("Remove {label}")" with "each a `tm-chip` whose remove button carries its own name (spec 0023
    §10)", and drop `removeChip` from Part E's strings.
  - 0023 Decision 4, replace "the multi-selects render their own values (spec 0010 §B.4, §E.2)" with
    "`tm-multi-select` renders joined text and the multi-picker composes `tm-chip` in its own
    scrolling row (spec 0010 §B.4, §E.2)".
  - Files: `0010-amendments-to-implemented-specs.md`, `0023-component-library-additions.md`.

### K-foundation-11 — minor — K31 not resolved: the `tmOverride` example

- **Where:** spec 0024 §2.3 example; owner of the order: spec 0027 §3.2 (Administration 900).
- **Fix:** replace "`overrides: [centersOverride, tmOverride('section:administration', { order: 90 })]`
  patches the Centers declaration and moves the Administration section to the end of the launcher."
  with "`overrides: [centersOverride, tmOverride('section:administration', { order: 10 })]` patches
  the Centers declaration and moves the Administration section (order 900, spec 0027 §3.2) to the
  front of the launcher." File: `0024-client-app-foundation.md`.

### K-foundation-12 — minor — K32 not resolved: unused `TmLocalization` members

- **Where:** spec 0024 §7.1 sketch; §7.3 Sources row. No spec calls `TmLocalization.load` or
  `.translate` (searched 0011–0035).
- **Fix:** delete the two lines "`load(code: string): Promise<void>; // loads a language's dictionary
  without activating it`" and "`translate(key: string, code: string): string; // a key in a loaded,
  inactive language`"; in §7.3 replace "another on its first switch or `load`." with "another on
  its first switch." File: `0024-client-app-foundation.md`.

### K-foundation-13 — minor — K7 not resolved: `Tellma:Copyright` has no key row

- **Where:** spec 0011 §8 (jsonc and key table); read by spec 0024 §3.1 (`copyright`).
- **Fix:** in the jsonc add `"Copyright": null,` after `"DisplayName": "Acme",`; in the table add the
  row "| `Copyright` | The About dialog's copyright line; `emit-client-assets` copies it into the client
  metadata's `distribution.copyright` (spec 0024 §3.1); null omits the line. |". File:
  `0011-distribution-host-and-multitenancy.md`.

### K-foundation-14 — minor — K-F not resolved: 0010's front matter omits Part F

- **Where:** spec 0010 Goals (bullets Part A–E only) and Context "Other specs that carry parts of this
  design" (no Part F entry). Shared with the Part F cluster.
- **Fix:** add to Goals "- **Part F.** The Queryex changes the data access layer and the report engine
  need: the seven moved amendments, grouping sets, level navigations, omitted parameters, constant
  statements, parameter binding, `'uq'`, `Exists` and `ComparedWith`."; add to Context "- spec 0012
  §11.1–§11.2, spec 0013 §8.2, spec 0015 §5.2, spec 0029 §9, spec 0033, spec 0035 §6.3 — the
  consumers of Part F." File: `0010-amendments-to-implemented-specs.md`.

### K-foundation-15 — minor — K30 not resolved: en dashes in telemetry values

- **Where:** spec 0029 §18 (lines with `tm.entity.names-resolved { count: 1 | 2–10 | 11+ }` and
  `tm.queryex.discover { clauses: 1–4, … }`); the grammar's owner is spec 0026 §3.2
  (`^[A-Za-z0-9_.:/@+-]*$`), which drops `2–10` in production.
- **Fix:** replace "`2–10`" with "`2-10`" and "`1–4`" with "`1-4`". File:
  `0029-client-entity-model.md`.

### K-foundation-16 — minor — wrong pointers from 0010 into 0024

- **Where:** spec 0010 Context; §B.3 Requested by; §D.6 Requested by.
- **Problem:** the translations loader is 0024 §7.3 and `TM_BLOB_FETCHER` is 0024 §10.5; §10.1 holds
  neither.
- **Fix:** Context, replace "- spec 0024 §7.1, §7.6, §10.1 — `@tellma/core` fills the Part B and Part
  D seams" with "- spec 0024 §7.1, §7.3, §7.6, §10.5 — `@tellma/core` fills the Part B and Part D
  seams"; §B.3, replace "**Requested by:** spec 0024 §7.1, §7.2." with "**Requested by:** spec 0024
  §7.1–§7.3."; §D.6, replace "**Requested by:** spec 0024 §10.1." with "**Requested by:** spec 0024
  §10.5.". File: `0010-amendments-to-implemented-specs.md`.

### K-foundation-17 — minor — dangling pointer in 0026 §9.8

- **Where:** spec 0026 §9.8 (xref list line 677). 0026 has no §7.2 on slots; the slot is 0027's.
  Also 0024 §11 cites `TmContextError` at "0026 §9.2"; its declaration is §9.1.
- **Fix:** 0026 §9.8, replace "fills spec 0027's `help` slot (§7.2)" with "fills spec 0027's `help`
  slot (spec 0027 §7.2)"; 0024 §11 Assistant seam, replace "its `TmContextError` (0026 §9.2)" with
  "its `TmContextError` (0026 §9.1, §9.2)". Files: `0026-client-observability-and-support.md`,
  `0024-client-app-foundation.md`.

### K-foundation-18 — minor — wrong pointers from 0027 into 0023

- **Where:** spec 0027 §6.2 and §9.6 (xref lines 384 and 833: "spec 0023 §6.4", which does not
  exist; the search field is §6.2); spec 0027 §11.1 ("`dl[tmDescriptionList]` (spec 0023 §7.3)";
  §7.3 is the splitter, the description list is §7.1).
- **Fix:** replace both "spec 0023 §6.4" with "spec 0023 §6.2", and "(spec 0023 §7.3)" with "(spec
  0023 §7.1)". File: `0027-shell-launcher-and-tenant-picker.md`.

### K-foundation-19 — minor — pointers that name the wrong section

- **Where and fix:**
  - 0023 §4.1: the bell's name "Notifications, 3 new" is defined in spec 0028 §5.2, not §6.1. Replace
    "spec 0028 §6.1)" (in "Overlay counters …") with "spec 0028 §5.2)".
  - 0023 §5.1 Focus: replace "`focus()`, bound to a shortcut (spec 0027 §4.3)" with "`focus()`, bound
    to `tm.toasts.focus` (spec 0027 Appendix A)".
  - 0026 §8.3 Triggers: replace "(`tm.support.copy-diagnostics`, reserved in spec 0027 §4.3)" with
    "(`tm.support.copy-diagnostics`, spec 0027 Appendix A)".
  - Files: `0023-component-library-additions.md`, `0026-client-observability-and-support.md`.

### K-foundation-20 — minor — 0023 §8.5 cites spec 0004 for a function 0004 never names

- **Where:** spec 0023 §8.5 Virtualization ("`tmComputeAxisWindow` (spec 0004)"). The function is a
  public export of `@tellma/core-ui/grid-engine`, absent from 0004's text.
- **Fix:** replace "Lattice lines window through `tmComputeAxisWindow` (spec 0004)" with "Lattice
  lines window through `@tellma/core-ui/grid-engine`'s `tmComputeAxisWindow`, the grid's own
  windowing". File: `0023-component-library-additions.md`.

### K-foundation-21 — minor — Appendix A points at a section that lacks the rule

- **Where:** spec 0024 Appendix A `string-key-collision` → "§7.3"; §7.3 never states it.
- **Fix:** in §7.3's Sources row append "A server pack key that begins `tm.` or `tmUi.` is
  `string-key-collision`." File: `0024-client-app-foundation.md`.

### K-foundation-22 — minor — code mismatch: `blob-rejected` statuses in 0024 §10.6

- **Where:** spec 0024 §10.6; owners spec 0016 §7.1 (`BlobRejectedException`: 404, 411, 413, 415,
  422) and spec 0017's code table.
- **Problem:** only 413 and 422 `blob-rejected` are mapped; 404 `Blob.UnknownKind`, 411 and 415
  `Blob.UnsupportedType` fall to "415; … anything else → `server`", so a wrong file type shows the 5xx
  dialog "Something went wrong".
- **Fix:** replace "| 400 `bad-request`; 403 `tenant-read-only`; 413 `limit-exceeded`, `blob-rejected`
  | `bad-request`; `read-only`; `limit` | 400 dialog |" with "| 400 `bad-request`; 403
  `tenant-read-only`; 413 `limit-exceeded` | `bad-request`; `read-only`; `limit` | 400 dialog |", and
  "| 422 `validation`, `blob-rejected` | `validation` | 0031 §8.5's fields, else 400 dialog |" with
  "| 422 `validation`; `blob-rejected` at any status | `validation` | 0031 §8.5's fields or the
  uploading field, else 400 dialog |". File: `0024-client-app-foundation.md`.

### K-foundation-23 — minor — shape gap: `TmKeys` cannot spell `+` or `-`

- **Where:** spec 0023 §3.2 (`TmKeys` grammar: "'+'-joined … then one KeyboardEvent.key"); consumer
  spec 0027 Appendix A (`TM_BROWSER_KEYS`: `Mod+Plus`, `Mod+Minus`).
- **Fix:** 0023 §3.2, replace the `TmKeys` comment "// '+'-joined: modifiers Mod | Ctrl | Alt | Shift
  | Meta, then one KeyboardEvent.key: 'Mod+K', 'Shift+?', 'F2'" with "// '+'-joined: modifiers Mod |
  Ctrl | Alt | Shift | Meta, then one KeyboardEvent.key, with 'Plus', 'Minus' and 'Space' for '+',
  '-' and ' ': 'Mod+K', 'Shift+?', 'F2', 'Mod+Plus'". File: `0023-component-library-additions.md`.

### K-foundation-24 — minor — shape mismatch: 0030 activates a chip label that `TmChip` cannot activate

- **Where:** spec 0030 §10.1 ("A chip's label opens that column's `tm-column-filter` in place");
  owner spec 0023 §4.3 (`TmChip` has `href` or `remove`, no activation output).
- **Fix (the owner adds what the consumer needs):** 0023 §4.3 `TmChip`, add "`readonly activate:
  OutputEmitterRef<void>; // bound without href: the label renders a button`" and in §2.1's chips row
  add "Enter or Space on a label button emits `activate`". If the library should not grow, 0030 §10.1
  instead drops "A chip's label opens that column's `tm-column-filter` in place, with Back;". Files:
  `0023-component-library-additions.md` (or `0030-search-page.md`).

### K-foundation-25 — minor — shape mismatch: 0030 puts an external input "in `tm-search-field`'s chrome"

- **Where:** spec 0030 §6.1 Parts; owner spec 0023 §6.2 (`tm-search-field` owns its input; custom
  chrome uses `input[tmSearch]`).
- **Fix:** replace "in `tm-search-field`'s chrome: magnifier, reserved clear slot." with "inside a
  `tm-form-field` that draws the magnifier and the reserved clear slot as `tm-search-field` does
  (spec 0023 §6.2's custom-chrome case)." File: `0030-search-page.md`.

### K-foundation-26 — minor — value mismatch with 0010 §B.6: 44 px versus `lg` (46 px)

- **Where:** owner spec 0010 §B.6 (controls resolve to `lg`, 46 px; 0002 §5's ladder). Consumers:
  spec 0030 §3.2 ("Below `sm` controls are 44 px (spec 0010 §B.6)"), spec 0034 §4.2 ("fields are
  44 px tall (spec 0010 §B.6)"), spec 0028 §6.4 ("rows are 44 px tall below `sm` (spec 0010 §B.6)")
  and spec 0035 §15.3 ("Below `sm` rows are 44 px (spec 0010 §B.6)"); §B.6 covers neither panel rows
  nor list rows.
- **Fix:** 0030 and 0034, replace "44 px" with "46 px (`lg`)" in those sentences; 0028 §6.4 and 0035,
  keep "44 px" as their own row rule and drop "(spec 0010 §B.6)" (or cite §C.10's touch rows after
  K-foundation-9 as the precedent). Files: `0030-search-page.md`,
  `0034-report-renderers-and-studio.md`, `0028-assignments-and-attention-surfaces.md`,
  `0035-dashboards-and-home-page.md`.

### K-foundation-27 — minor — copy mismatch: the diagnostics failure toast

- **Where:** spec 0026 §8.3 Clipboard ("failure toasts "Couldn't copy. Open About, then Diagnostics,
  to see the details.""); owner of toast copy spec 0028 §8.1 ("Diagnostics copied" / "Couldn't copy").
- **Fix (owner wins):** 0026 §8.3, replace "Success toasts "Diagnostics copied" (spec 0028 §8.1);
  failure toasts "Couldn't copy. Open About, then Diagnostics, to see the details."" with "Success
  and failure toast once (spec 0028 §8.1)." If the longer hint is wanted, 0028 §8.1's row takes
  "Couldn't copy. Open About, then Diagnostics, to see the details." instead. Files:
  `0026-client-observability-and-support.md` (and `0028-assignments-and-attention-surfaces.md` only
  for the alternative).

### K-foundation-28 — minor — 0023's toast example contradicts 0028's 4 s outcome duration

- **Where:** spec 0023 §5.1 example ("what spec 0028's policy writes after a 202"); owner spec 0028
  §8.1 (outcomes use `duration: TM_TOAST_FEEDBACK_MS`).
- **Fix:** replace "`inject(TmToasts).show({ tone: 'accent', title: t('Started in the background.'),
  key: `job:${jobId}`,`" with "`inject(TmToasts).show({ tone: 'accent', title: t('Started in the
  background.'), key: `job:${jobId}`, duration: TM_TOAST_FEEDBACK_MS,`". File:
  `0023-component-library-additions.md`.

### K-foundation-29 — minor — 0028 claims a rule 0024 §5.5 does not state

- **Where:** spec 0028 §9.2 Dismiss ("the update banner stays dismissed for that version (spec 0024
  §5.5)"); 0024 §5.5 says nothing about dismissal; 0028 §9.5 owns it.
- **Fix:** replace "(spec 0024 §5.5)" with "(§9.5)". File:
  `0028-assignments-and-attention-surfaces.md`.

### K-foundation-30 — minor — types used but never declared

- **Where:** spec 0025 §3.1 (`TmMembership`, `TmDistributionInfo`: no spec declares them); spec 0024
  §2.2 (`TmAppDeclarations`: read by 0027 §3.1, 0029 §4, 0035 §15 by member, never sketched).
- **Fix:**
  - 0025 §3.1, replace "`readonly tenants: Signal<readonly TmMembership[] | undefined>; // /bff/user's
    tenants (spec 0011 §5.5)`" with "`readonly tenants: Signal<readonly TmMembership[] | undefined>;
    // /bff/user's tenants: spec 0011 §3.8's TenantMembership as camelCase JSON`" and the
    `distribution` line's comment with "// /api/distribution-info: spec 0011 §5.9's DistributionInfo as
    camelCase JSON".
  - 0024 §2.2, add after the `TM_APP_DECLARATIONS` line: "`interface TmAppDeclarations { entities:
    ReadonlyMap<string, TmEntityDeclaration>; pages: readonly TmPageDeclaration[]; reports: readonly
    TmCodedReportDeclaration[]; widgets: readonly TmWidgetTypeDeclaration[]; sections: readonly
    TmNavSection[]; entries: readonly TmNavEntry[]; settingsTabs: readonly TmSettingsTabDeclaration[];
    appBar: readonly TmShellSlot[]; features: readonly string[]; home: TmLazyComponent | null } //
    merged, hide-applied; features: core first`", and shorten the member-table row to "the one token
    through which shells and hosts read declarations".
  - Files: `0025-client-state-and-cross-tab-sync.md`, `0024-client-app-foundation.md`.

### K-foundation-31 — minor — the `Boot_*` server keys have no owner

- **Where:** spec 0024 §4.2 (`TM_BOOT` "Boot_* keys copied from the emitted packs"; the boot page
  table gives English); spec 0013 §10.1 lists every Core key family but not `Boot_*`.
- **Fix:** 0024 §4.2, after the boot-page table add "Core's resx carries the strings as `Boot_Title`,
  `Boot_Unsupported`, `Boot_Cookies`, `Boot_SignInFailed`, `Boot_LoadFailed` and `Boot_TryAgain`; the
  build copies them into `TM_BOOT`."; 0013 §10.1 adds the bullet "- **The boot page.** `Boot_*`, in
  Core (spec 0024 §4.2)." Files: `0024-client-app-foundation.md`,
  `0013-settings-localization-and-cache.md`.

### K-foundation-32 — minor — 0025 undercounts session-storage writers

- **Where:** spec 0025 §1 ("Browser storage has three owners: …"), §11 last bullet; spec 0024 §8.3
  (`signIn()` sets `tm.login`).
- **Fix:** 0025 §1, replace "Browser storage has three owners: `TmClientStore` (IndexedDB, `tm.boot`),
  `TmDrafts` (session storage) and `tm-boot.js`." with "Browser storage has four writers:
  `TmClientStore` (IndexedDB, `tm.boot`), `TmDrafts` (`tm.draft:*`), `TmSession` and `tm-boot.js`
  (`tm.login`, spec 0024 §4.2, §8.3)."; §11, replace "**Session storage holds only drafts and
  `tm.login`,** the boot script's sign-in loop guard." with "**Session storage holds only drafts and
  `tm.login`,** the sign-in loop guard (spec 0024 §4.2)." File:
  `0025-client-state-and-cross-tab-sync.md`.

### K-foundation-33 — minor — 0024's worker telemetry differs between §5.5 and §14

- **Where:** spec 0024 §5.5 (`unrecoverable` and `VERSION_INSTALLATION_FAILED` both emit
  `tm.sw.install-failed`; `VERSION_READY` emits nothing) versus §14 Telemetry (`.update-ready
  (fromVersion)`, `.unrecoverable`).
- **Fix:** in §5.5, `VERSION_READY` row append "; emits `tm.sw.update-ready { fromVersion }`"; split
  the last row into "| `unrecoverable` | reloads at once, drafts being in session storage; emits
  `tm.sw.unrecoverable` |" and "| `VERSION_INSTALLATION_FAILED` | emits `tm.sw.install-failed`; the
  next check retries |". File: `0024-client-app-foundation.md`.

### K-foundation-34 — minor — repetition: 0024 and 0023 restate 0028-owned copy and options

- **Where:** spec 0024 §2.2 `provideTellma` row ("`provideTellmaToasts({ maxVisible: 3, overflow })`
  with the pill navigating to `/{t}/notifications` (0028 §8)"); 0024 §5.5 `VERSION_READY` row
  (quotes "A new version is available." while saying 0028 owns it); 0023 §5.2 ("A message fits one line
  at 390 px, about 45 characters (spec 0028 owns the copy)"; the cap is 0028 §9.1's).
- **Fix:** 0024 §2.2, replace that clause with "`provideTellmaToasts` with spec 0028 §8.1's options";
  0024 §5.5, replace "shows the banner `tm.update` through `TmBanners`: "A new version is available."
  with Reload (0028 §9.5 owns copy and level)" with "shows the banner `tm.update` (0028 §9.5)"; 0023
  §5.2, replace "A message fits one line at 390 px, about 45 characters (spec 0028 owns the copy)."
  with "The copy and its length cap are spec 0028 §9.1's." Files: `0024-client-app-foundation.md`,
  `0023-component-library-additions.md`.

### K-foundation-35 — minor — repetition: 0025 restates `tm-boot.js`'s delete

- **Where:** spec 0025 §2.2 bullet "**`tm-boot.js`'s delete**"; owner spec 0024 §4.2 step 3c.
- **Fix:** replace the bullet with "- **`tm-boot.js` deletes by this layout** (spec 0024 §4.2 step 3c),
  so a change to it is a change to the boot script." File:
  `0025-client-state-and-cross-tab-sync.md`.

### K-foundation-36 — minor — repetition: 0023 restates two 0010 rules

- **Where:** spec 0023 Vocabulary ("**Resolved size** — an explicit `size`, else
  `TM_FORM_FIELD_DEFAULTS.size` from `sm` up and `lg` below (spec 0010 §B.6)"); 0023 §4.5 Text
  ("Intl's "now" under a minute, then minutes, hours and calendar days up to 7, then the `medium`
  date"); owners 0010 §B.6 and §D.4.
- **Fix:** Vocabulary, replace with "**Resolved size** — `ɵtmControlSize`'s answer (spec 0010
  §B.6)."; §4.5, replace "in the display zone, `TM_TIME_ZONE`: Intl's "now" under a minute, then
  minutes, hours and calendar days up to 7, then the `medium` date." with "in the display zone,
  `TM_TIME_ZONE`, with §D.4's thresholds." File: `0023-component-library-additions.md`.

### K-foundation-37 — minor — repetition around the telemetry relay

- **Where:** spec 0026 §6.2 ("per user, 60 batches a minute, a bucket apart from `tellma-user`, so
  telemetry never starves user calls"; owner 0016 §8.2); spec 0011 §5.1 ("The client telemetry
  relay … policy `Tellma.Web`, `AsTenantlessEndpoint("client-telemetry")`, rate policy
  `tellma-telemetry`, a 32 KB body limit, excluded from request tracing (§9)"; owner 0026 §6.1).
- **Fix:** 0026 §6.2, replace that bullet's text with "**Rate policy `tellma-telemetry`** (spec 0016
  §8.2): ten open tabs use a third of it."; 0011 §5.1, replace the relay bullet with "**The client
  telemetry relay** `POST /api/client-telemetry`, mapped as spec 0026 §6.1 defines it." Files:
  `0026-client-observability-and-support.md`, `0011-distribution-host-and-multitenancy.md`.

### K-foundation-38 — minor — process words: question ids in Decisions records

- **Where:** spec 0024 Decisions 13, 14 ("Tenant picker Q1", "Q2"); spec 0025 Decision 12 ("SignalR
  Q1"); spec 0026 Decisions 1–6 ("Observability Q1/Q2", "Supportability Q1–Q4"). These are the
  brief's item ids.
- **Fix:** drop the ids and keep the question as words, for example 0024 #13 "**The picker is
  `/tenants`, and `/` is the launch redirect**", 0025 #12 "**On `cache.changed`, the leader also
  refreshes settings, permissions and entities, with no machinery of its own.**", 0026 #1 "**What to
  collect: errors, …**", #3 "**Slow-request trace ids: …**". Files: `0024-client-app-foundation.md`,
  `0025-client-state-and-cross-tab-sync.md`, `0026-client-observability-and-support.md`.

### K-foundation-39 — minor — process words in 0023

- **Where:** spec 0023 §9 ("Values come from the design canvases"); Decision 18 ("toasts as
  designed").
- **Fix:** §9, replace "come from the design canvases, snapped to the 4 px grid" with "snap to the
  4 px grid"; Decision 18, replace "toasts as designed (top inline-end, …)" with "toasts at the top
  inline-end, …". File: `0023-component-library-additions.md`.

### K-foundation-40 — minor — ruling mismatch: the chip entry point's name

- **Where:** spec 0023 §1 table (`chips`); rulings Part 1 (§2.3 0023 list: "`chips` → "`chip`
  (`tm-chip`, `tm-chip-set`)""). The architecture text still says `chips`.
- **Fix:** rename the entry point to `chip` in 0023 §1 (table row, `@tellma/core-ui/chip`) and §9 if
  named; nothing else cites the entry point by path. File: `0023-component-library-additions.md`.

## Pointers in `xref1.txt` that belong to this cluster

| Line | Verdict |
|---|---|
| 0023:30 `§C.10` | false positive: continues "spec 0010 §C.1, §C.2, §C.9" |
| 0024:1128 `§6.1`, `§3.10` | false positives: "spec 0011's … (§6.1)", "spec 0016's … (§3.10)", both exist |
| 0025:26 `§5.9`, 0025:28 `§6.4` | false positives: continue spec 0011 and spec 0013, both exist |
| 0026:677 `§7.2` | real: K-foundation-17 |
| 0027:384, 0027:833 `spec 0023 §6.4` | real: K-foundation-18 |

Every other cross-spec pointer in 0023–0026 and 0010 Parts A–E resolves to an existing section; the
semantic mismatches found are the findings above.
