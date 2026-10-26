# Consolidated edits to existing specs (round 2)

Every edit (to 0010–0022) and every amendment (to 0001–0009, written into spec 0010) that the
round-2 design asks for, grouped by target spec, then target section, in section order.

- **Sources.** `N23`…`N35` = `design/00NN.md` (section 5 = edits `E<n>`, section 6 = amendments);
  `BE` = `design/0010-BE.md` (Parts B–E, items `B.1`…`E.2`); `F` = `design/0010-F.md` (Part F,
  items `F.0`…`F.16`, edits `E<n>`); `A§6.3 rN` / `A§6.4 pX` = a row of the skeleton
  `design/00-architecture.md` §6.3 / §6.4 (index below). `N27 E1a`… splits a note row that reuses
  one id for several targets.
- **Verification.** Every target § was checked in the current `docs/specs/` file; "Current" quotes
  the text the item changes (line numbers are today's, for orientation only).
- **Flags.** `CONFLICT` — two sources ask for different changes to the same text, or an item
  contradicts a skeleton row (stated exactly). `DUPLICATE` — the same change asked twice (merged
  here into one item). `CASCADE` — the change forces another section to change (named; the ones
  no note lists are also in `gaps.md`). `UNDESIGNED` — a consumer note asks 0010-BE for an
  amendment that `0010-BE.md` does not design (ruling A§14.2: the part's designer defines it).
  `TARGET` — the § a source names is wrong; the correct § is given. Nothing here decides a
  conflict.

## Skeleton row index

**A§6.3 (edits to 0010–0022)**

| r | Target cell | r | Target cell |
|---|---|---|---|
| r1 | 0011 §3.2, §5.5 (landing, tenant picker) | r24 | 0015 §5.2, 0016 §3.3 (`Filters`) |
| r2 | 0011 §5.5 profile cookie v2 | r25 | 0015 §5.4, 0016 §3.3 (`ParentIdsRequest`) |
| r3 | 0011 §5.5, §4.5 logout | r26 | 0016 §2.1, §2.2, §2.4 (routes) |
| r4 | 0011 §5.5 client-caching sentence | r27 | 0016 §3.10 (fingerprints) |
| r5 | 0011 §5.1–§5.3 hosting | r28 | 0016 §6.2, §7.5 (trace id, 401, 404) |
| r6 | 0011 §5.9, §8 (`DistributionInfo`) | r29 | 0016 §8.2 (`tellma-telemetry`) |
| r7 | 0011 §1.2, §6.6 (client tree) | r30 | 0017 §2.3, §3 (kind policies, `support-bundle`) |
| r8 | 0011 §9 (`traceparent`) | r31 | 0018 §3.1 (`users/me/locale`) |
| r9 | 0012 §2.2, §2.5 (`[DisplayNameSource]`) | r32 | 0018 §3.5 (preference key grammar) |
| r10 | 0012 §6.1, 0015 §14.1 (cancellation) | r33 | 0018 §4 (`members` extra) |
| r11 | 0012 §11.2 (moves to 0010 Part F) | r34 | 0018 §1.2, §6 (development seed) |
| r12 | 0013 §2.3, §2.4, 0021 §6.3 (`cache.changed`) | r35 | 0019 §2.1, §4.1 (`ExportColumn`) |
| r13 | 0013 §2.7, 0016 §6.2 (composite `permissions`) | r36 | 0020 §2.3 (job label) |
| r14 | 0013 §2.8 (`migrate` bumps) | r37 | 0020 §5.2, §10 (`job.changed`) |
| r15 | 0013 §5.3, §10.1 (label keys) | r38 | 0020 §7.2 (`state` extra) |
| r16 | 0013 §6.1 (`settings/client`) | r39 | 0021 Context, §5.4, §8 |
| r17 | 0013 §6.3 (settings stamps) | r40 | 0021 §5.3 (`Related`) |
| r18 | 0013 §10.4, 0021 §2.5 (emitted packs) | r41 | 0021 §6.3 (`assignments.changed`) |
| r19 | 0014 §2 (`CoreResources`, `AccessActions`) | r42 | 0021 §9.2 (`ClientTimeoutInterval`) |
| r20 | 0014 §3.4 (write-behind) | r43 | 0021 §2.3 (`core.report.shared`) |
| r21 | 0014 §7, 0016 §4.3, §6.1 (`Tellma-Activity`) | r44 | 0022 §4, 0018 §3.3 (deep links) |
| r22 | 0014 §10, 0016 §3.4 (securables, fingerprint) | r45 | 0022 §8 (Outgoing emails page) |
| r23 | 0015 §1.2, §2.2 (SPA schema, descriptor) | | |

**A§6.4 (amendments, written into 0010)**

| p | Part, target | p | Part, target |
|---|---|---|---|
| pA | A, 0003 §11.2 | pD1 | D, 0005 §2.1 (overlay) |
| pB1 | B, 0002 §4, §8 (breakpoints) | pD2 | D, 0005 §10, §11 (modal, popover) |
| pB2 | B, 0002 §4 (`TmTheme`) | pD3 | D, 0005 §2.2 (formats) |
| pB3 | B, 0002 §5, §7 (translations) | pD4 | D, 0005 §2.3, §7 (`clearAll`, fetcher) |
| pB4 | B, 0002 §3.4 (multi-select, groups) | pE1 | E, 0006 §2 (two-line, multi) |
| pB5 | B, 0002 §1 (lint) | pF1 | F, from 0012 §11.2 (seven) |
| pC1 | C, 0004 §5, §6, §8.2, §8.3, §8.8, Dec 18 | pF2 | F, 0008 §2, §13.4 (grouping sets) |
| pC2 | C, 0004 §6.1, §14 (sort, header slot) | pF3 | F, 0008 §10.10, §13 (tree rollup) |
| pC3 | C, 0004 §8.8 (select-all) | pF4 | F, 0008 §6, §9 (omitted) |
| pC4 | C, 0004 §4, §7, §12, §13, §16 | pF5 | F, 0008 §1.4 (`EvaluateConstant`) |
| pC5 | C, 0004 §8.5 (menu) | pF6 | F, 0008 §2.4 (`ComparedWith`) |
| | | pF7 | F, 0008 §10.4 (`'uq'`) |

---

# 0010 — `0010-identity-server-amendments.md` → `0010-amendments-to-implemented-specs.md`

## Whole spec

### X-0010-1 — rename and restructure into parts
- **Current:** title "Identity Server Amendments for Distributions, MCP and Sandboxes"; Context
  "This spec collects the changes: eight deltas to spec 0003"; one Definition of done (§11).
- **Delta:** rename the file and title to "Amendments to Implemented Specs"; widen Context and
  Goals to one part per frozen spec; Part A = the existing text (0003); add Parts B (0002), C
  (0004), D (0005), E (0006), F (0008), each with its own Definition of done, Decisions and Review
  flags, so Part A (blocking 0011) never waits for the others.
- **From:** BE §5; README spec map; A§6.4 lead sentence.
- **Flags:**
  - `CASCADE` → existing citations by section number stay valid only if Part A keeps §2–§9:
    0011 (lines 44, 91, 1343 "spec 0010 §6", 1347 "§7", 1394, 1481, 1968, 2082, 2102), 0016
    (78, 1651, 1809), 0018 (199 "§8", 210 "§9", 373, 975, 1010, 1011, 1081).
  - `CASCADE` → 0011 §5.9's closing paragraph enumerates "the identity-server changes … specified
    by spec 0010" (eight items); it must add the account URL (X-0010A-1) and the new title.
  - `CASCADE` → `ARCHITECTURE.md` line 817 links the old file name (A§6.5 scope).
  - `CONFLICT` (numbering convention, all sources): Part A's existing items are §2–§9; N24 numbers
    its new Part A items `A1`–`A3`; BE uses `B.1`…`E.2`; F uses `§F.0`…`§F.16` and already writes
    citations as "spec 0010 §F.7" into its edits (X-0012-*, X-0014-*, X-0015-*); N34 continues
    BE's letters as `C.12`, `C.13`, `D.7`. One scheme must be chosen before the citations land.

## Part A — identity server (0003)

### X-0010A-1 — 0003 §11.2: Account & Security entry URL
- **Current (0003 §11.2):** "The distribution surfaces Account & Security … that deep-links to
  these pages over SSO (no re-login) with a `return_url`" — no stable entry URL.
- **Delta:** `GET {issuer}/account` (in-proc `/id/account`); query `client_id`, `return_url`
  (absolute), `section` ∈ `profile | passkeys | authenticator | external-logins | sessions`
  (default and unknown → `profile`), `ui_locales`; requires the identity cookie (normal challenge,
  then back); `302` to that section's Manage page carrying `client_id`, `return_url`,
  `ui_locales`; `Cache-Control: no-store`.
- **From:** N24 §6 A1; A§6.4 pA.
- **Flags:** `CASCADE` → 0011 §5.9 `IdentityInfo.AccountUrl` (X-0011-15).

### X-0010A-2 — 0003 §11.2: the return link honours `return_url`
- **Current:** the back link is the client's registered origin root.
- **Delta:** `ApplicationLinkResolver.ResolveAsync(clientId, returnUrl)`: for a first-party client
  with a registered `Origin`, the back link is `return_url` when same-origin with it
  (`InvitationReturnUrlValidator.IsSameOrigin`: `http`/`https`, scheme, host, port equal, no
  userinfo), else the origin root; every Manage navigation link carries `return_url` forward.
- **From:** N24 §6 A2; pA.

### X-0010A-3 — 0003 §11.2 / sign-in: `ui_locales` through the authorization request
- **Current:** only a direct query on the sign-in page itself sets its language.
- **Delta:** `ui_locales` on the authorization request (query or PAR) reaches the sign-in
  interaction, so the login page renders in that language.
- **From:** N24 §6 A3 (not in pA's row; it serves 0024's `/bff/login?ui_locales`).
- **Flags:** `CASCADE` ← 0011 §5.5 `/bff/login` forwards `ui_locales` (X-0011-11 (b)).

### X-0010A-4 — Part A tests and Definition of done
- **Current (0010 §10, §11):** suites and done cover §2–§9 only.
- **Delta:** add resolver vectors (same origin, other port, userinfo, `javascript:`, unknown
  client); `/account` per section with and without a session; `ui_locales` through PAR renders
  Arabic (identity e2e). Not amended: Manage page content, post-logout URIs.
- **From:** N24 §6 "Tests", "Not amended".

## Part B — component library foundation (0002)

### X-0010B-1 — 0002 §1 (lint): `tm/prefix-exports` scope
- **Current (0002 §1):** "an ESLint selector rule enforcing the `tm-`/`Tm…` prefix on selectors
  and exported symbols (D3)".
- **Delta:** `VALUE_LIKE` becomes `/^(tm[A-Z0-9]|provideTellma[A-Z0-9]|injectTm[A-Z0-9])/`; `files`
  add `projects/module/**/*.ts`; `ignores` add `projects/core/tellma-core-tools/**`; `lint:test`
  cases.
- **From:** BE B.5; A§6.4 pB5.

### X-0010B-2 — 0002 §3.4: multi-select and option groups
- **Current (0002 §3.4, line 621):** "Forward-compat (not Phase 1, not precluded): multi-select
  (aria multiselect mode, value → array), option groups"; Non-goals line 138 "Phase-1 Select is
  single-select".
- **Delta:** new sibling `tm-multi-select<T>` (`value = model<readonly T[]>`, in option order;
  panel stays open on toggle; one-line trigger of joined labels via `Intl.ListFormat`, ellipsized)
  and `tm-option-group` (`label`; one level; headings skipped by navigation) usable in both selects;
  harnesses; `./select` ceiling 8 → 9 KB.
- **From:** BE B.4; A§6.4 pB4; consumers N29, N30, N32.
- **Flags:**
  - `CONFLICT` with A§6.4 pB4: the row says "`tm-select` multi-select"; BE ships a separate
    component (BE decision 6, review flag 9).
  - `CONFLICT` with N23 §6 A6 (part B): A6 asks that "the multi-select `tm-select` … render chosen
    values with `tm-chip-set overflow="collapse"` and removable `tm-chip`s, calling `focusLast()`
    on Backspace"; BE B.4's trigger renders the selected labels as one joined, ellipsized text
    line with no chips.

### X-0010B-3 — 0002 §4 (new subsection): breakpoints
- **Current:** 0002 has no breakpoint constants.
- **Delta:** `TmBreakpoint`, `TM_BREAKPOINTS` (`sm` 576, `md` 768, `lg` 992, `xl` 1200, `xxl`
  1440), `TmBreakpoints` (`current`, `atLeast`) in the primary entry point; components adapt with
  container queries; stylelint `tm/allowed-breakpoints` (range syntax `>=`/`<` only, values from the
  set; parity test with `TM_BREAKPOINTS`); the modal's `max-width: 600px` migrates (X-0010D-10).
- **From:** BE B.1; A§6.4 pB1.
- **Flags:** `TARGET` — pB1 names "0002 §4, §8"; 0002 §8 is the performance budget. BE targets §4
  (new subsection) and §1 (lint) (BE challenge 4). `CONFLICT` with A§9 "tokens" for breakpoints:
  BE ships none (custom properties cannot appear in media queries; BE challenge 3).

### X-0010B-4 — 0002 §4: `TmTheme` and the System theme
- **Current (0002 §4):** dark mode is `[data-theme=dark]`; no theme service.
- **Delta:** `TmThemeSetting = 'system' | 'light' | 'dark'`; `TmTheme { setting, effective,
  set() }` writes `<html data-theme>` (always `light`/`dark`) in the same task, follows
  `prefers-color-scheme` under `system`; `TmUiOptions.theme { initial }` seeds it; it never
  persists; the tokens emitter adds `color-scheme: light|dark` to the two blocks.
- **From:** BE B.2; A§6.4 pB2.

### X-0010B-5 — 0002 §4 or §2.1 (unassigned): a shared Trusted Types policy
- **Current:** none.
- **Delta:** one lazily created Trusted Types policy named `tellma` in the core-ui primary
  (`createHTML` for sanitized SVG, `createScriptURL` allowing same-origin `/ngsw-worker.js` only),
  shared by 0023's icon registry and 0024's service-worker registration.
- **From:** N24 §6 "Needs from other parts", Part B "new request".
- **Flags:** `UNDESIGNED` (BE has no item). `CASCADE` → A§6.6 CSP `trusted-types angular
  angular#bundler tellma` depends on it.

### X-0010B-6 — 0002 §5: `provideTellmaUi({ translations })` loader seam
- **Current (0002 §5, line 860):** "`provideTellmaUi()` — the umbrella a distribution calls:
  composes `provideTellmaForms()` + …"; options `{ forms?, availableLangs? }`.
- **Delta:** `TmUiOptions.translations?: TmUiTranslationsLoader` (registered as the Transloco
  loader; loaded tables merge, never replace; a root load carries the server pack at the root and
  `tm` under `tm`); runtime language list via `TranslocoService.setAvailableLangs`; the library
  never calls `load`; `TmUiOptions.theme` (X-0010B-4).
- **From:** BE B.3; A§6.4 pB3.

### X-0010B-7 — 0002 §5: the `xs` touch-height rule for form fields
- **Current:** 0002 §3.2/§6 state the 44 px touch guidance; no rule ties a size to a breakpoint.
- **Delta:** below `sm`, `TM_FORM_FIELD_DEFAULTS.size` resolves to `lg` (46 px ≥ 44) for controls
  in page content.
- **From:** N23 §6 A8 (challenge C4).
- **Flags:** `UNDESIGNED`. Related to X-0010C-14 (N27 M4: menu rows ≥ 44 px below `sm`).

### X-0010B-8 — 0002 §7: live root direction
- **Current (0002 guiding rule 4, line 88):** "direction from CDK `Directionality`"; no live host (CDK's
  root reads `dir` once, per BE §13).
- **Delta:** `provideTellmaUi` provides `ɵTmLiveDirectionality` at the root, a `MutationObserver` on
  `<html dir>` and `<body dir>`; the documented host is `<html dir>`; `[dir]` islands still
  override.
- **From:** BE B.3; A§6.4 pB3 ("a documented live-direction host").
- **Flags:** `CASCADE` → X-0010D-10 (the modal follows the root instead of a `direction` option).

### X-0010B-9 — Part B Definition of done and budgets
- **Delta:** primary ceiling 5 → 7 KB (Part D's primary additions included); `./select` 8 → 9; the
  identity server's copied token CSS refreshed; per BE "B. Definition of done".
- **From:** BE.

## Part C — data grid and menu (0004)

### X-0010C-1 — 0004 §4, §7: `rowClass` and `rowHeaders`
- **Current (0004 §4):** "Div-based DOM with grid roles … Each row is `display: grid`"; no row
  class hook; row headers always render.
- **Delta:** `rowClass?: (row) => string | readonly string[] | null` (pure, per rendered row;
  styles through `--tm-grid-row-bg|ink|weight`); shipped modifiers `tm-grid-row--muted`,
  `tm-grid-row--emphasis`; `rowHeaders = input(true)` (`false`: no row-number column, no corner).
- **From:** BE C.6; A§6.4 pC4.

### X-0010C-2 — 0004 §4: `scrollbar-gutter: stable`
- **Current:** no gutter rule (classic scrollbars re-flow `flex` columns when rows arrive).
- **Delta:** `.tm-grid__scroller { scrollbar-gutter: stable; }`; the gutter's header band paints
  the header colour; e2e with scrollbars shown.
- **From:** BE C.7; pC4 (A§9 "Grids reserve their scrollbar gutter").

### X-0010C-3 — 0004 §4, §12: `scrollToRow(id, options)` with flash
- **Current:** no programmatic scroll-to-row.
- **Delta:** `scrollToRow(rowId, { flash?, activate?, align? }): boolean` (defaults `false`, `true`,
  `'center'`); trees expand ancestors first; flash tokens `--grid-flash-bg`/`-duration` (1200 ms;
  reduced-motion static; forced-colors outline); `activate` moves the active cell without moving DOM
  focus.
- **From:** BE C.8; pC4; consumer N31 (child-row deep link).

### X-0010C-4 — 0004 §4, §12: `scrollToRow` gains `column` and `focus`
- **Current:** (X-0010C-3's sketch) `{ flash?, activate?, align? }`.
- **Delta:** add `column?` (the column key to make active) and `focus?` (move DOM focus to that
  cell), so the error summary lands on the failing cell.
- **From:** N31 §6 Part C "new request".
- **Flags:** `UNDESIGNED`. BE C.8's rule is "the caller focuses (`grid.focus()`)"; `focus` adds a
  second way, which BE has not weighed.

### X-0010C-5 — 0004 §4, §7, §14: `footerRows`
- **Current:** no footer rows.
- **Delta:** `footerRows: readonly T[]` (≤ 3, after the data, sticky at block end, same columns
  and templates, not selectable or virtualized, reachable by arrows, `aria-rowindex` continuing,
  `rowClass` applied).
- **From:** N34 §6 "C.13".
- **Flags:** `UNDESIGNED`; numbered "C.13" by N34 although BE's C.11 is Budgets (X-0010-1
  numbering conflict). Overlaps BE C.6's `tm-grid-row--emphasis` ("totals rows"): two mechanisms
  for a totals row unless BE reconciles them.

### X-0010C-6 — 0004 §5.1: `activeRowId` model
- **Current (0004 §5.1):** inputs `data`, `field`, `readonly`; no active-row model.
- **Delta:** `activeRowId = model<TmRowId | null>(null)`; `moveActiveRow(by)`;
  `activateRow(rowId?, { newTab? })`; consumer writes move the active cell without moving focus;
  unknown ids are rewritten with the actual row.
- **From:** BE C.2; pC1 ("external active row").

### X-0010C-7 — 0004 §6.1, §14: sort state and `aria-sort`
- **Current (0004 Non-goals, line 62):** "Sorting, filtering, grouping, aggregation UI — the consumer
  owns the array it binds." (the non-goal stands: the grid shows sort state, never sorts).
- **Delta:** `orderBy = model<TmGridSort | null>`; `TmGridColumn.sortable` (requires `key`); click
  cycles `asc → desc → null`; a 14 px indicator slot always reserved; `aria-sort` on the sorted
  header; announcements `sorted`/`sortCleared`; the grid never reorders data.
- **From:** BE C.3; A§6.4 pC2.

### X-0010C-8 — 0004 §6.1, §8.2, §14: header navigation and the header action slot
- **Current (0004 §8.3):** "Click column header … only when the press lands on the header
  background/label, never on interactive projected content"; headers are not navigable.
- **Delta:** `ng-template[tmGridHeaderEnd]`, `button[tmGridHeaderAction]` (`active`); header
  navigation (ArrowUp from row 1, Left/Right/Home/End, Enter/Space sort, Alt+ArrowDown opens the
  action) only when a column is `sortable` or declares `tmGridHeaderEnd`; roving `tabindex`;
  24×24 target; slot reserved.
- **From:** BE C.4; pC2 ("header end slot for a filter button with an active state").

### X-0010C-9 — 0004 §6.1: header underline while its action is expanded (optional)
- **Delta:** the header cell shows a 2 px accent underline while its `[tmGridHeaderAction]` has
  `aria-expanded="true"`.
- **From:** N30 §6 "BE C.4 (optional)".
- **Flags:** `UNDESIGNED`.

### X-0010C-10 — 0004 §8.2: the keyboard driver `input[tmGridDriver]`
- **Current (0004 §8.2):** "Navigation & selection (grid focused, no editor open)" — keys act only
  with focus in the grid.
- **Delta:** `TmGridDriverTarget` interface and `input[tmGridDriver]` directive: ArrowDown/Up,
  PageDown/Up move the active row, Enter activates; `aria-controls` + `aria-activedescendant` on the
  input; all other keys stay with the input. In `@tellma/core-ui/grid`.
- **From:** BE C.2; pC1.
- **Flags:** `CONFLICT` (location): N30 §6 asks that `TmGridDriverTarget` move to
  `@tellma/core-ui/contracts` (types only) so `tm-tiles` implements it without importing `grid`
  (N30 request R1 to 0023); BE C.2 declares it in `@tellma/core-ui/grid`.

### X-0010C-11 — 0004 §8.2: printable keys pass through a readonly grid
- **Delta:** state and pin that a readonly grid leaves unmodified printable keys unhandled (never
  `defaultPrevented`), so the search page's type-to-search receives them.
- **From:** N30 §6 "0004 §8.2".
- **Flags:** `UNDESIGNED` (BE C.1's key table does not state it).

### X-0010C-12 — 0004 §8.2: exported key map
- **Delta:** export `TM_GRID_KEYS: readonly TmKeys[]` (every binding with a modifier) and
  `TM_GRID_KEY_HELP: readonly { keys: TmKeys; labelKey: string }[]` (labels `tmUi.grid.keys.*`)
  from `@tellma/core-ui/grid`, for 0027's conflict rule and Shortcuts dialog.
- **From:** N27 §6 M5.
- **Flags:** `UNDESIGNED`. `CASCADE` → depends on 0023's `TmKeys` (new entry point `kbd`).

### X-0010C-13 — 0004 §8.3, §8.8, Decision 18: row activation
- **Current (0004 §8.8):** "List screens **activate** records through consumer-rendered links in
  the first column"; Decision 18 "List screens: consumer links activate, a grid-provided checkbox
  column selects … A row-selection mode … was rejected".
- **Delta:** `rowActivation = input<'none' | 'row'>('none')` (readonly grids only);
  `rowActivate = output<TmGridRowActivation<T>>` with `{ row, rowId, via, newTab }`; gesture table
  (click/tap/Enter activate; Mod or middle → `newTab`; F2 reaches a cell's link; 400 ms
  double-activation guard); active-row visual (`--grid-active-row-bg`, inline-start bar); hidden
  hint `tmUi.grid.rowActivationHint`. Decision 18 replaced.
- **From:** BE C.1; A§6.4 pC1 ("`rowActivation: 'none' | 'row'`, output `rowActivate`").
- **Flags:** `CONFLICT` (payload shape): N23 §6 A7 requires "the grid's `rowActivate` payload has
  the shape of `TmItemActivation<T>` (`item`, `id`, `newTab`)" (N23 §3.8: "same shape as the
  grid's rowActivate"); BE C.1 defines `TmGridRowActivation<T> { row, rowId, via, newTab }`.
  N30 decision 10 swaps grid, tiles and tree on one state, so the two shapes must agree.

### X-0010C-14 — 0004 §8.5: menu trigger, submenus, groups, checkable and link items, header
- **Current (0004 §8.5):** "Scope for this phase: flat items + separators, disabled state, a
  leading icon slot, item shape `TmMenuItem { id, label | labelKey, icon?, shortcut?, disabled?,
  action }` … Submenus … are deferred"; "consumer icons are inline-SVG `TemplateRef`s — the icon
  registry remains a future concern".
- **Delta:** `TmMenuItem` gains `description`, `detail`, `role` (`checkbox`/`radio`), `checked`,
  `keepOpen`; new `TmMenuLink` (`href`, `target`, optional `action`), `TmMenuSubmenu` (one level),
  `TmMenuGroup`; `TmMenuOpenOptions.initialFocus`; `button[tmMenuTriggerFor]` (APG menu button);
  `ng-template[tmMenuHeader]`; `./menu` ceiling 6 → 7.
- **From:** BE C.10; A§6.4 pC5.
- **Flags:**
  - `CONFLICT` (icon and shortcut types): BE C.10 keeps `TmMenuItem`'s `icon` and `shortcut`
    "unchanged" and types `TmMenuLink.icon` / `TmMenuSubmenu.icon` as `TemplateRef<void>`; N23 §6
    A5 asks `TmMenuItem.icon: TemplateRef<void> | string` (a `TmIconRegistry` name), the menu
    setting `--icon-size: var(--menu-icon-size)`, and `TmMenuItem.shortcut` accepting `TmKeys`
    rendered through `tmFormatKeys`; N27 §6 M3 extends the string `icon` to links and submenus.
    Both also replace 0004 §8.5's "the icon registry remains a future concern".
  - `UNDESIGNED` additions on the same entry point: N27 M1 `TmMenuGroup.layout?: 'list' |
    'segmented'` (a radio group as one segmented row; Left/Right move within it); N27 M2
    `cssClass?` on `TmMenuItem`, `TmMenuLink`, `TmMenuSubmenu`; N27 M4 rows ≥ 44 px below `sm`
    (see X-0010B-7).
  - `DUPLICATE`: A§6.4 pC5's list equals BE C.10's scope (merged).

### X-0010C-15 — 0004 §8.8: select-all across pages
- **Current (0004 §8.8):** "a tri-state select-all checkbox in its header"; the toggle replaces
  the set with the current rows or empties it (as implemented).
- **Delta:** the tri-state counts the current rows; from `all` the toggle removes the current rows'
  ids, from `none`/`mixed` it adds them, keeping foreign ids; announcement `checkedCountAcross`.
- **From:** BE C.5; A§6.4 pC3.

### X-0010C-16 — 0004 §8.8: `rowSelectable`
- **Delta:** `rowSelectable: (row: T) => boolean` (default all): disabled checkbox
  (`aria-disabled`); select-all, Shift+click and Ctrl+Shift+Space skip the row; the tri-state
  counts selectable rows.
- **From:** N34 §6 "C.12".
- **Flags:** `UNDESIGNED`; `CASCADE` → X-0010C-15's counting rule (BE wrote it over all current
  rows).

### X-0010C-17 — 0004 §12, §13.3: tree `expandedIds` and batched `loadChildren`
- **Current (0004 §13.3):** "on expand, the grid calls `loadChildren: (row: T) =>
  Promise<void>`"; §12 "Tree expansion set | `gridId + contentKey`".
- **Delta:** `expandedIds = model<ReadonlySet<TmRowId> | undefined>` (bound wins over the store;
  unknown ids kept); `loadChildren: (rows: readonly T[]) => Promise<void>` (breaking), batched at
  bind/restore, after each batch, and for same-task expands; replaces `collapseUnloadedLazyRows`.
- **From:** BE C.9; A§6.4 pC4.
- **Flags:** `DUPLICATE` with N23 §6 A7, second half ("the tree-grid's `expandedIds` model and
  batched `loadChildren(rows)` use the signatures tiles mirror"): same signatures; merged.

### X-0010C-18 — 0004 §13.2: siblings render in data order
- **Current (0004 §13.2):** "The engine flattens the hierarchy to the visible-row sequence
  (depth-first, respecting the expansion set)" — sibling order unstated.
- **Delta:** state that the flattening keeps the array's order among siblings, so a server
  `OrderBy` sorts siblings.
- **From:** N30 §6 "0004 §13.2".
- **Flags:** `UNDESIGNED`.

### X-0010C-19 — 0004 §16: budget re-baseline
- **Current (0004 §16):** the 24 KB grid budget (never met).
- **Delta:** `./grid` ceiling 38 → 42 KB, `./menu` 6 → 7; `./grid-engine` 15 and `./tree-grid` 8
  unchanged; breakdown recorded; the readonly/editable split deferred (BE review flag 6).
- **From:** BE C.11; pC4.

## Part D — basics (0005)

### X-0010D-1 — 0005 §2.1: public `@tellma/core-ui/overlay`
- **Current (0005 §2.1):** "`private/` — the overlay-composition helper".
- **Delta:** `tmCreateAnchoredOverlay`, `tmLogicalPositions`, `tmPushEscapeDismissal` and their
  types move, unchanged, to a new public entry point `@tellma/core-ui/overlay`; `private` keeps
  only `ɵTmErrorPopover`; documented rules "one Escape stack" and "inline top layer"; budgets
  `./overlay` 2, `./private` 4 → 2.
- **From:** BE D.1; A§6.4 pD1.
- **Flags:** `CONFLICT` with A§14 ruling 3 ("A new `@tellma/core-ui` entry point is 0023's"): this
  is a new entry point holding existing code; BE challenge 5 proposes rewording the ruling.

### X-0010D-2 — 0005 §2.2: date-time, time and relative-time formats; `TM_TIME_ZONE`
- **Current (0005 §2.2):** "the option surface is deliberately a small closed set — number: … date:
  locale × calendar × style".
- **Delta:** `tmFormatDateTime`, `tmFormatTime`, `tmFormatRelativeTime(instant, locale, { now,
  timeZone, calendar?, form?: 'relative' | 'age', style?: 'long' | 'short' | 'narrow' })` over ISO
  instants in an explicit IANA zone; thresholds < 60 s "now" (Intl), minutes, hours, calendar days
  below 7 ("yesterday", "3 days ago"), then the `medium` absolute date; `TM_TIME_ZONE:
  InjectionToken<Signal<string>>` (default the browser zone); `TmL10n.timeZone`, `formatDateTime`,
  `formatTime`, `formatRelativeTime`; golden arrays `dateTime`, `time`, `relative`; `./l10n` 8 → 9.
- **From:** BE D.4; A§6.4 pD3.
- **Flags:**
  - `CONFLICT` with N23 §6 A3 and N23 §3.4: A3 asks for a pure function `(instant: string, now:
    number, options: { locale; timeZone; style: 'narrow' | 'long' }) => string`; N23 §3.4 fixes the
    text "under 1 min 'Just now', minutes, hours, 'Yesterday', then the `medium` date", with "Just
    now" a message key (`relativeTime.justNow`). BE differs in signature (locale positional;
    `form`; `calendar`), style set (adds `short`, its default), the < 60 s text (Intl's "now", not
    a message key) and the day rule (BE keeps "3 days ago" until 7 calendar days; N23 shows the
    absolute date after "Yesterday").
  - `DUPLICATE`: `TM_TIME_ZONE` is asked by N23 A3 and defined by BE D.4 with the same type;
    merged (filled by `@tellma/core` with `PreferredTimeZone ?? tenant zone`).

### X-0010D-3 — 0005 §2.2: month, quarter and year formats
- **Delta:** the closed date format set gains `month` ("Mar 2026"), `quarter` ("Q1 2026" / "الربع
  1 2026") and `year`, each in an explicit calendar (Gregorian, Umm al-Qura, Ethiopic), with
  goldens.
- **From:** N34 §6 "D.7" (0033 §3.2 `display` month, quarter, year).
- **Flags:** `UNDESIGNED`; numbered "D.7" by N34 (X-0010-1).

### X-0010D-4 — 0005 §2.2: `weekday` date style and `tmZonedParts`
- **Delta:** `TmDateStyle` gains `'weekday'` (CLDR `yMMMEEEEd`; goldens per calendar); a pure
  `tmZonedParts(epochMs, timeZone): { date; hour; minute }` in `@tellma/core-ui/l10n`, with goldens
  across DST days.
- **From:** N35 §6 Part D (a), (b).
- **Flags:** `UNDESIGNED`.

### X-0010D-5 — 0005 §2.3: `TmClientCache.clearAll` sweep
- **Current (0005 §2.3):** "`clearAll()` enumerates and deletes every `tm-`-prefixed store
  (`caches.keys()` today; `indexedDB.databases()` and prefixed web-storage keys the day a feature
  first uses them)"; "A distribution calls `clearAll()` on logout."
- **Delta (BE):** `clearAll(options?: { keep?: (name, area) => boolean })` deletes CacheStorage
  `tm-*`, IndexedDB `tm-*` (via `indexedDB.databases()`), `localStorage` `tm.*` and this tab's
  `sessionStorage` `tm.*`, minus `keep`; blocked deletes resolve; the library knows nothing of the
  boot record.
- **From:** BE D.5; A§6.4 pD4; N24 §6 (Part D needs); N25 §6.
- **Flags:** `CONFLICT` (three-way, same call):
  - A§6.4 pD4: "`TmClientCache.clearAll` also deletes `tm-*` IndexedDB databases and `tm.*`
    web-storage keys (sparing the boot record's device fields)".
  - N25 §6: `clearAll` "also deletes `tm-*` IndexedDB databases (here `tm-app`) … keeping
    `tm.boot`'s device fields (`v`, `theme`, `lang`, `dir`)", relying on `versionchange` so the
    delete of `tm-app` is not blocked (N25 §3, line 183).
  - BE D.5 (challenge 1): the library cannot spare fields inside a key; `keep` is a whole-name
    predicate; "0025's `TmClientStore` keeps `tm-app` out of `clearAll`" and sweeps it itself.
  - N24 §8.4 step 6 calls `TmClientCache.clearAll()` with no `keep`, which under BE D.5 deletes
    `tm-app` and the whole `tm.boot` key (device fields included) before the same step's
    `boot.patch(…)`.

### X-0010D-6 — 0005 §3 (`tmButton`): `tm-icon` children
- **Current (0005 §3):** "Icons: leading/trailing inline SVGs projected as content, `aria-hidden`".
- **Delta:** `.tm-button` sets `--icon-size: var(--button-icon-size)`; the projected-icon and
  pending rules cover `tm-icon` children as they cover `svg`.
- **From:** N23 §6 A4.
- **Flags:** `UNDESIGNED`.

### X-0010D-7 — 0005 §7.1, §7.2: `TM_BLOB_FETCHER` cancellation
- **Current (0005 §7.1):** "The component talks to the network through a `TM_BLOB_FETCHER` token
  (default implementation: `HttpClient` …)".
- **Delta:** `TmBlobFetchOptions.signal?: AbortSignal`; `tm-image` aborts on destroy, `src`/`etag`
  change, or leaving the `defer` viewport; the blob cache's coalescing is reference-counted; an
  aborted request writes nothing.
- **From:** BE D.6; A§6.4 pD4.
- **Flags:** `CASCADE` → 0024's fetcher (BE challenge 7: `TmApi` has no GET method for blobs).

### X-0010D-8 — 0005 §7.4: `tm-image` `placeholderOnError`
- **Current (0005 §7.4):** "Placeholder template shows until the swap (and permanently when `src`
  is empty)"; the error state shows the broken-image glyph.
- **Delta:** `placeholderOnError: boolean` (default `false`): the error state renders the
  `tmImagePlaceholder` template; the error stays announced through `alt`.
- **From:** N23 §6 A2 (avatars fall back to initials).
- **Flags:** `UNDESIGNED`.

### X-0010D-9 — 0005 §9: `tm-tab-group` `prerender`
- **Current (0005 §9, line 802):** "`preserveContent` (default `false`)".
- **Delta:** `prerender: 'none' | 'idle'` (default `none`): with `idle`, unvisited panels are
  instantiated at idle, hidden and `inert`, and kept like `preserveContent`.
- **From:** N31 §6 Part D "new request".
- **Flags:** `UNDESIGNED`.

### X-0010D-10 — 0005 §10: `TmModal` `placement: 'end'`, live direction, the `sm` query
- **Current (0005 §10):** centered modal only; the shell CSS (code) has `@media (max-width:
  600px)`.
- **Delta:** `TmModalConfig.placement?: 'center' | 'end'` (drawer: inline-end, full height, widths
  360/480/720 by `size`, full width below `sm`, no slide); `TmModal` passes no `direction`, and each
  open modal follows the live root `Directionality` (`setDirection`, `updatePosition`); the query
  becomes `@media (width < 576px)`.
- **From:** BE D.2; A§6.4 pD2; A§14 ruling 1.
- **Flags:** `CONFLICT` with A§6.4 pD2 "`TmModal` … live `direction`": BE ships no `direction`
  option (it would give the content a static `Directionality`); live direction comes from Part B's
  root service (X-0010B-8; BE challenge 2).

### X-0010D-11 — 0005 §10: `TmModalConfig.role` and `describedBy`
- **Delta:** `role?: 'dialog' | 'alertdialog'` (default `dialog`) and `describedBy?: string` (an
  element id inside the content), passed to CDK `DialogConfig.role` and `ariaDescribedBy`.
- **From:** N23 §6 A1 (`TmConfirm` is an APG alert dialog).
- **Flags:** `UNDESIGNED`; same interface as X-0010D-10.

### X-0010D-12 — 0005 §11: `tm-popover` `size` and `panelClass`
- **Current (0005 §11):** no `size` input; the panel takes its content's width (BE: up to
  `--popover-max-inline-size`).
- **Delta:** `size = input<'auto' | 'sm' | 'md' | 'lg'>('auto')` (320/440/640 px, clamped to
  `100vw − 2 × 8px`; never resizes while open); `panelClass`.
- **From:** BE D.3; A§6.4 pD2.

### X-0010D-13 — Part D Definition of done and budgets
- **Delta:** per BE "D. Definition of done": `./overlay` 2, `./private` 2, `./l10n` 9; harness
  additions `TmModalHarness.getPlacement()`, `TmPopoverHarness.getSize()`.
- **From:** BE.

## Part E — entity picker (0006)

### X-0010E-1 — 0006 Non-goals: lift two non-goals
- **Current (0006 Non-goals):** "Multi-select (tag/chip input) — a future component; the picker
  stays a scalar FK selector."; "Rich option templates — options render the consumer's label
  string, one line, ellipsized."
- **Delta:** both lifted by X-0010E-2 and X-0010E-3 (the picker stays scalar; the multi-picker is a
  sibling).
- **From:** BE E.1, E.2 ("the multi-select non-goal lifted").

### X-0010E-2 — 0006 §2, §4.1: two-line options
- **Current (0006 §2):** options are one label string.
- **Delta:** `itemDescription = input<(item) => string | null>()`; with it bound every result row is
  two lines at a fixed `--entity-picker-option-height-2line`; footer rows keep one line; accessible
  name "label, description".
- **From:** BE E.1; A§6.4 pE1.

### X-0010E-3 — 0006 §2, §3, §4, §6: `tm-entity-multi-picker`
- **Current:** none (a non-goal).
- **Delta:** `TmEntityMultiPicker<T, Id>` (`value: readonly Id[]` in pick order; `picked`,
  `removed`; `maxItems`; no `edit`), composing an inner single picker in `ɵcommitMode: 'emit'`; chip
  row before the input on one fixed-height line that scrolls horizontally, with a fixed count
  badge; keyboard table; `TmEntityPickerPageData` gains `multiple?` and `ids?`, and a page may
  return `readonly TmEntityPick[]`; `./entity-picker` 12 → 14 KB.
- **From:** BE E.2; A§6.4 pE1.
- **Flags:** `CONFLICT` with N23 §6 A6 (part E) and N23 decision 18: "the multi-select picker
  render[s] chosen values with `tm-chip-set overflow="collapse"`" — one line with a "+N" chip; BE
  E.2 scrolls the chip row horizontally with a count badge (BE review flag 8).

### X-0010E-4 — 0006 §6: `TmEntityPickerPage` object form gains `context`
- **Current (0006 §2, line 120):** `export type TmEntityPickerPage = …` carries no consumer
  payload.
- **Delta:** the object form gains `context?: unknown`, passed to the page as
  `TmEntityPickerPageData.context`, so one shim page serves every entity.
- **From:** N29 §6 Part E "new request" (N31 §6 Part E: "none beyond 0029's `context` member").
- **Flags:** `UNDESIGNED`; touches the same `TmEntityPickerPageData` as X-0010E-3.

### X-0010E-5 — 0006 §7.1: the grid `entity` column gains `itemDescription` (optional)
- **Current (0006 §7.1):** "`tm-grid-column` gains the picker's data seam … `itemId?`,
  `itemLabel?`".
- **Delta:** add `itemDescription?` beside them.
- **From:** N29 §6 Part E "Optional".
- **Flags:** `UNDESIGNED`; `TARGET` — N29 names no section; the column inputs live in 0006 §7.1.

### X-0010E-6 — Part E Definition of done
- **Delta:** per BE "E. Definition of done" (two-line rows, multi-picker on 0023's `tm-chip`,
  the `ɵcommitMode` suite, geometry e2e for 0, 1 and 30 chips).
- **From:** BE.
- **Flags:** `CASCADE` → needs 0023's `tm-chip` (BE R-1); 0023 must drop the "multi-select input"
  from `chips` (BE challenge 6).

## Part F — Queryex (0008)

Every item is additive: `QueryexLanguage.Version` stays 1 and every pre-existing golden stays
byte-identical except paging names (F.0.2). Items F.1–F.7 restate 0012 §11.2 (never implemented);
F.8–F.15 are N33 §6's F1–F8 in the same order; F.16 is N29's request. `CONFLICT` below records
where N33 §6's text (still cited by N33's own body: "Part F item F1"…"F8", `QX3304`) differs
from F; F's §2 "Deviations from N33 §6" table says 0033 adopts F's version, but the N33 note has
not been updated.

### X-0010F-1 — 0008 §14, Appendix A, §16, §18, §19, §20: Part F's frame (F.0)
- **Current (0008 §20):** "the public surface is exactly §1.4, §2, §3"; Appendix A lists today's
  codes.
- **Delta:** compatibility rules (additive, positional records keep their constructors, new
  `FilterTree` node types `ViaNode`, `ExistsNode`); cache-key table (L2/L3 per item); new codes
  `QX3104` (a table calendar over an aggregated value), `QX4009` (paths not permitted here),
  `QX5008` (more than 32 grouping expressions); `QX2009` widened; new diagnostic locations
  (`…Via[<navigation>]`, `…Exists[<entity>]`, `ParameterExpressions[<name>]`, `Constant[<i>]`);
  interpreter and corpus obligations (`LedgerFixture` gains `TreeLevels(4)`, a child table, the `uq`
  map).
- **From:** F §3 (F.0.1–F.0.5).
- **Flags:** `CONFLICT` with N33 §6 lead: N33 adds `QX3304` ("a path where none is allowed"); F
  uses `QX4009` (F deviations table: "QX33xx is the hierarchy family").

### X-0010F-2 — 0008 §2.1, §13.4: key-set restrictions (F.1)
- **Current (0012 §11.2 item 1):** "The engine binds `Path` in Filter mode … emits `AND <column> IN
  (SELECT [Id] FROM <TableSource>)` conjoined after the filter tree".
- **Delta:** `KeySetRestriction(Path, TableSource)`, `QuerySpec.Restrictions`; root statement only,
  never inside `Exists`; joins count toward `MaxJoins`; caller errors for a bad path or source.
- **From:** F F.1; A§6.4 pF1; A§6.3 r11.
- **Flags:** `DUPLICATE` of 0012 §11.2 item 1 (moved, X-0012-13).

### X-0010F-3 — 0008 §10.10: `level(node)` (F.2)
- **Current (0012 §11.2 item 2):** "`level(node: HierarchyId) -> Numeric`, `NotNull` when its
  argument is, emitted as `.GetLevel()`".
- **Delta:** registry entry `level(node: HierarchyId) -> Numeric`, `{0}.GetLevel()`,
  `Propagate(0)`, store type `smallint`; non-node argument → QX3005.
- **From:** F F.2; pF1; r11.

### X-0010F-4 — 0008 §2.1 (`CompiledQuery`), §13.4: prologue and body (F.3)
- **Current (0012 §11.2 item 3):** "`CompiledQuery.Prologue` holds the hoisted `DECLARE`s … and
  `Body` the `SELECT`".
- **Delta:** `Prologue` (with a trailing empty line, `""` when none), `Body` (no `;`); `Sql ==
  Prologue + Body + ";\n"`; embedding rule (`ORDER BY` only with paging).
- **From:** F F.3; pF1; r11.

### X-0010F-5 — 0008 §13.1, §13.4, §16: paging slots (F.4)
- **Current (0012 §11.2 item 4):** "`Skip`/`Take` as parameter slots (`Origin = Declared`, names
  `@qx{b}_skip`/`@qx{b}_take`)".
- **Delta:** the slots keep origin `Literal`, renamed `@qx{b}_skip`/`@qx{b}_take`, listed last; the
  L3 key holds their presence, not their values; a cache hit re-stamps their `Value`; paging goldens
  rewritten once.
- **From:** F F.4, decision 4; pF1; r11.
- **Flags:** `CONFLICT` with the moved text (0012 §11.2 item 4: `Origin = Declared`): F changes
  the origin to `Literal` (F review flag 1). `CASCADE` → 0012 §11.3 sentence (X-0012-14).

### X-0010F-6 — 0008 §10.6, §13.1: context slots (F.5) — no engine change
- **Current (0012 §11.2 item 5):** "`today()` and the `TimeZone` slot bind the tenant zone through
  `QueryexContextValues` (§5.1)".
- **Delta:** recorded as a host binding rule (0012 §5.1, §11.3); no code changes.
- **From:** F F.5.
- **Flags:** `CONFLICT` with A§6.4 pF1 ("the seven amendments (…, context slots, …)"): F says it is
  not an engine amendment (F challenge 3).

### X-0010F-7 — 0008 §2.3, §16, §17: versioned leaves (F.6)
- **Current (0012 §11.2 item 6):** "Each leaf binds under its own language version".
- **Delta:** `FilterTree.Leaf(text, languageVersion)` (refuses unsupported versions),
  `LeafNode.LanguageVersion`; binds under `LanguageVersion ?? options`; L3 keys include it.
- **From:** F F.6; pF1; r11.

### X-0010F-8 — 0008 §2.3: `FilterTree.Via` (F.7)
- **Current (0012 §11.2 item 7):** "A segment that is not a to-one navigation is a compile-time
  diagnostic."
- **Delta:** `Via(navigation, inner)`, `ViaNode`; segments may be foreign-key or level
  navigations (F.9); a `LEFT` last join adds a presence guard `([Pn].[Key] IS NOT NULL AND
  (<inner>))`, so `not` stays two-valued; empty path, non-navigation segment, or `Via` in `Having`
  are caller errors; discovery prefixes inner paths.
- **From:** F F.7, decision 3; pF1; r11.
- **Flags:** `CONFLICT` with the moved text: 0012 §11.2 item 7 makes a bad segment a diagnostic;
  F makes it a caller error (F review flag 2).

### X-0010F-9 — 0008 §2.1, §8.3, §11.3, §13.4: grouping sets and the level indicator (F.8)
- **Current (0008 §2.1):** `QuerySpec` emits one plain `GROUP BY`.
- **Delta:** `QuerySpec.KeyGroups` (optional; null = one group per key), `QuerySpec.GroupingSets`;
  `QueryexColumn.IsGroupingLevel`; `GROUP BY GROUPING SETS (…)` plus an appended `GROUPING_ID(…)`
  column; `Having` filters the finest set only (`HAVING GROUPING_ID(…) <> 0 OR (…)`); nullity and
  tiebreaker rules; caller errors (incl. `KeyGroups` without `GroupingSets`, a group in no set,
  > 31 groups, > 4,096 sets); `QX5008` above 32 keys.
- **From:** F F.8; N33 §6 F1; A§6.4 pF2.
- **Flags:** `DUPLICATE` (N33 F1 and F F.8, merged) with these `CONFLICT`s in N33 F1's text:
  "`GroupingSets` without … `KeyGroups`" is a caller error (F: optional); "`Having` when set 0 is
  not the set of every group" (F: "when no set holds every group"); "more than 256 sets" (F:
  4,096).

### X-0010F-10 — 0008 §3, §6.1, §10.10, §12.2: tree level navigations (F.9)
- **Current (0008 §10.10):** hierarchy functions `descendantOf`/`ancestorOf`; no ancestor-at-depth
  navigation.
- **Delta:** `EntityBuilder.TreeLevels(maxDepth)` (1–32, requires a unique `TreeNode`),
  `EntityDescriptor.TreeLevels`; `Level{j}` resolves after properties and FK navigations, targets
  the entity itself, is not listed in `Navigations`; always `LEFT`, `Nullable`; the join reads
  `CASE WHEN … GetLevel() >= j THEN … GetAncestor(… - j) END`; usable in every clause, in `Via`
  paths and `descendantOf` key paths.
- **From:** F F.9, decisions 6–7; N33 §6 F2; A§6.4 pF3.
- **Flags:**
  - `CONFLICT` with A§6.4 pF3: the row asks "a tree-rollup grouping key over the dimension's node
    closure (`IsDescendantOf`), the only fan-out allowed"; F (challenge 1) and N33 (challenge 1)
    replace it with to-one level navigations (no fan-out), because a closure join counts a fact
    once per ancestor (F's walk-through: 102,000 instead of 52,000).
  - `CONFLICT` with N33 F2: "Declares `Level1` … `Level{maxDepth}` on the entity: to-one
    navigations" (listed); F counts them and leaves them out of `Navigations` (F decision 7).
  - `CASCADE` → 0012 §11.1 declares `TreeLevels([Tree].MaxDepth)` (X-0012-12); 0012 §11.4 and
    0015 §5.2/§7.3 refuse a level path in an entity `Select` (X-0012-15, X-0015-15, X-0015-19);
    0029's client path model treats `Level{j}` as a self-navigation (F §5 requests).

### X-0010F-11 — 0008 §6.3, §8.4, §9.2: omitted parameters (F.10)
- **Current (0008 §6.3):** every declared parameter gets a slot; a missing argument is the
  host's problem.
- **Delta:** `QueryCompilationOptions.OmittedParameters` (`IReadOnlyCollection<string>`, ignore
  case; declared names; no slot); syntactic folding before binding in the skeleton of `Filter` and
  `Having` leaves and every aggregation's condition: bare omitted elements of `in` and
  `descendantOf`/`ancestorOf` key lists drop first; an atom still naming one becomes `true` under
  even negations, `false` under odd; elsewhere an omitted parameter is a typed absent value.
- **From:** F F.10, decision 10; N33 §6 F3; A§6.4 pF4.
- **Flags:**
  - `CONFLICT` with A§6.4 pF4 ("a comparison that names an omitted parameter folds to true"): F
    folds by polarity (`false` under an odd number of `not`s) and drops list elements.
  - `CONFLICT` with N33 F3: whole-atom polarity folding only (no list-element dropping; F review
    flag 4) and `IReadOnlySet<string>` (F: `IReadOnlyCollection<string>`).

### X-0010F-12 — 0008 §6.3, §13.1: parameter expressions (F.11)
- **Delta:** `QueryCompilationOptions.ParameterExpressions` (name → value-mode expression); each
  expression is path-free (`QX4009`), aggregation-free (`QX4002`), parameter-free (`QX3007`), one
  item (`QX2009`), checked against the declared type; every occurrence lowers as the expression
  (no `Declared` slot); `'uq'` lookups inside become prologue variables.
- **From:** F F.11, decision 11; N33 §6 F4.
- **Flags:** not in A§6.4 (F challenge 2: the row list omits it; see `gaps.md`). `CONFLICT` with
  N33 F4: "at the current language version" (F: at `QueryCompilationOptions.LanguageVersion`) and
  `QX3304` (F: `QX4009`).

### X-0010F-13 — 0008 §1.4: constant statements, `EvaluateConstant` (F.12)
- **Current (0008 §1.4, line 38):** "It compiles text; it never opens a connection, never …"
  (no constant entry point).
- **Delta:** `QueryexEngine.EvaluateConstant(IReadOnlyList<ConstantItem>,
  ConstantCompilationOptions)` → one `FROM`-less `SELECT` (1 to `MaxListItems` items; diagnostics
  at `Constant[i]`).
- **From:** F F.12, decision 12; N33 §6 F5; A§6.4 pF5.
- **Flags:** `DUPLICATE` (N33 F5 = F F.12; merged). Naming: F challenge 4 proposes
  `CompileConstants` (the engine never evaluates) but keeps `EvaluateConstant` (review flag 10).
  `CONFLICT` with N33 F5 on the path code (`QX3304` vs `QX4009`, via F4's rules).

### X-0010F-14 — 0008 §2.4, §4: parameter binding, `BindParameters` (F.13)
- **Delta:** `QueryexEngine.BindParameters(text, ParameterBindingOptions)` → canonical text with
  valued parameters as literals (`cast('…', 'date')` etc.) and F.10's folding applied; syntax-only
  (no schema); caller errors for undeclared names and mistyped values.
- **From:** F F.13, decision 13; N33 §6 F6.
- **Flags:** not in A§6.4 (F challenge 2). `DUPLICATE` (N33 F6 = F F.13 in substance; F adds
  `Limits`, the `null`/omitted value-position literal and "a parameter in neither map stays").

### X-0010F-15 — 0008 §3, §10.4: `'uq'` through a host month map (F.14)
- **Current (0008 §10.4, line 1604):** "This version implements `'gc'` … only; `'uq'` … emits
  lookups against a host-provided Hijri month-map table named through the schema contract".
- **Delta:** `QueryexSchemaBuilder.CalendarMap(calendar, source)` (only `'uq'`); table columns and
  host obligations (contiguous months; unique (`Calendar`, `StartDate`) and (`Calendar`, `Year`,
  `Month`)); `UmAlQuraCalendar` semantics; every `'uq'` result `Nullable`; emission by grain —
  row-grain operand: one `OUTER APPLY (SELECT TOP (1) … ORDER BY [StartDate] DESC)` seek per
  distinct operand (a second lookup for `startOfYear`, `diffYears`, `addYears`, `addMonths`);
  path-free operand: prologue variables; aggregated operand: `QX3104`.
- **From:** F F.14, decision 14; N33 §6 F7; A§6.4 pF7.
- **Flags:**
  - `CONFLICT` with N33 F7: "join-shaped (`OUTER APPLY (SELECT … WHERE … d < [EndDate])`), one per
    distinct date operand, never a scalar subquery" — no `TOP (1)` seek, no prologue variables, no
    `QX3104` (F review flags 5, 6, 11).
  - `CASCADE` → 0013 §8.2 owns `core.CalendarMonths` (X-0013-15); 0012 §11.1 declares
    `CalendarMap` (X-0012-12).

### X-0010F-16 — 0008 §2.3: `FilterTree.Exists` (F.15)
- **Delta:** `FilterTree.Exists(entity, navigation, inner)`, `ExistsNode`; a semi-join
  `EXISTS (SELECT 1 FROM … WHERE [Xn].[FK] = <current>.[Key] AND (<inner>))`; own join trie, shared
  slots; `NotNull`, `not` → `NOT EXISTS`; nesting with `Via` either way; restrictions never inside;
  `DiscoverQuery` reports no `PathUse` from inside.
- **From:** F F.15, decision 15; N33 §6 F8.
- **Flags:** not in A§6.4 (F challenge 2). `CONFLICT` with N33 F8: "anything else is a diagnostic at
  `Filter.Exists[<entity>]`" (F: caller errors, decision 3); F adds "a level navigation cannot
  correlate".

### X-0010F-17 — 0008 §2.4: `ParameterUse.ComparedWith` (F.16)
- **Current (0008 §2.4, line 619):** `public sealed record ParameterUse(` without the member.
- **Delta:** `ParameterUse.ComparedWith: IReadOnlyList<string>?` — the segments of the one path a
  bare parameter meets (`=`, `!=`, orderings, `in` element, `descendantOf`/`ancestorOf` key); null
  for none or conflicting paths; root-relative inside `Via`; nothing inside `Exists`.
- **From:** F F.16; N29 §6 Part F; A§6.4 pF6.
- **Flags:** `DUPLICATE` (N29 and F agree; merged).

### X-0010F-18 — Part F public surface and Definition of done
- **Delta:** the F.1–F.16 sketches are the complete public additions; Definition of done per F §9.2
  (F.1–F.4, F.6, F.7 first; F.8–F.16 before 0033); `eng/queryex-inspection` exposes the new inputs
  (0008 §19).
- **From:** F §9.
- **Flags:** `CASCADE` → ARCHITECTURE.md's Queryex and reports sections (F §9.2 "Docs").

---

# 0011 — `0011-distribution-host-and-multitenancy.md`

### X-0011-1 — §1.2 "Client project" bullet: one client location and the build
- **Current (line 190–203):** "In Development, `dotnet run` on the Web project starts the SPA proxy,
  which launches `pnpm start` … `proxy.conf.js` forwards `/bff`, `/api`, `/id`, … `aspnetcore-https.js`
  exports the development certificate"; tree lines 158–159 list both files.
- **Delta:** the client tree becomes A§3.1's (no `proxy.conf.js`, no `aspnetcore-https.js`;
  `generated/` gitignored); "launches `pnpm start`" stays as `tellma-client serve`; the esproj
  references the Migrator (`ReferenceOutputAssembly=false`) and its build runs `tellma-client build
  --no-dotnet-build`; the proxy's prefix list becomes "the reserved words of spec 0024 §9.1".
- **From:** N24 E1; A§6.3 r7.
- **Flags:** `CASCADE` → §1.2's `Tellma:PublicOrigin` = `https://localhost:4200` and 0018 §6.2's
  `Authority = https://localhost:4200/id` assume the fixed port that X-0011-18 makes variable
  (`gaps.md`).

### X-0011-2 — §2.1 contribution items: report seeds, widget types, dashboard seeds
- **Current (§2.1, lines 288–317):** the `FeatureContributionItem` records end with
  `ShapeRequirementContributionItem`.
- **Delta:** add `ReportSeedContributionItem` (`FeatureContribution.ReportSeed(ReportSeed)`),
  `WidgetTypeContributionItem` (`FeatureContribution.WidgetType(key)` and
  `WidgetType<TSettings>(key)`, realised into `IWidgetTypeRegistry`) and
  `DashboardSeedContributionItem` (`FeatureContribution.DashboardSeed(seed)`), each realised by
  `Tellma.Core`.
- **From:** N33 E15; N35 E2.
- **Flags:** `TARGET` — both notes say "0011 §2 (contribution items)"; the records are §2.1.
  `CASCADE` → §6.2 platform steps (X-0011-17) and 0018 §6.3's step list (`gaps.md`).

### X-0011-3 — §3.2 last paragraph: the landing reads memberships in the SPA
- **Current (line 566):** "The company picker's single-tenant shortcut (§5.5) reads the membership
  list, not a policy."
- **Delta:** "The SPA's launch redirect (spec 0024 §9.3) reads the membership list through
  `/bff/user`".
- **From:** N24 E4, E8(f); A§6.3 r1.

### X-0011-4 — §3.3: "company picker" → "tenant picker"
- **Current (line 589):** "so the company picker localises without opening a tenant database".
- **Delta:** rename.
- **From:** N24 E8(f); r1.

### X-0011-5 — §3.8 membership directory readers
- **Current (line 822–824):** "Readers: `GET /bff/user` (the company picker), the sign-in redirect
  that skips the picker for a single live membership (§5.5), and the admin `GET …/members`".
- **Delta:** rename (E8(f)); the second reader no longer exists after X-0011-10 and must become the
  SPA's launch redirect (through `/bff/user`).
- **From:** N24 E8(f) (rename only).
- **Flags:** `CASCADE` of X-0011-10 that no note lists (the "sign-in redirect that skips the
  picker" reader; `gaps.md`).

### X-0011-6 — §4.5 listeners: `/bff/logout` terminates the session
- **Current (line 1031):** "the session store (§5.5) calls `SessionsTerminatedAsync` on back-channel
  logout and revocation with the handles (§7.4) of the deleted rows".
- **Delta:** "… on back-channel logout, revocation and `/bff/logout` (with the caller's own
  handle, before the row is deleted)".
- **From:** N24 E5; A§6.3 r3.

### X-0011-7 — §5.1 `MapTellma`: routes and SPA hosting
- **Current (line 1148–1158):** "`MapTellma()` maps … `/api/strings/{language}` (spec 0013 §10.4),
  `/api/notification-catalogue` (spec 0021 §2.5) … static assets and the SPA fallback
  (`MapFallbackToFile("index.html")` when `wwwroot/index.html` exists, excluding every reserved
  prefix)".
- **Delta:** (a) remove the two GET routes; (b) the fallback sentence → "static assets and the SPA
  fallback under spec 0024 §6's rules (endpoint-routed static assets stamped
  `TenantlessEndpointMetadata("spa-assets")`, cache headers by hashed name, the navigation-only
  fallback, `index.html`'s security headers and CSP)"; (c) add `POST /api/client-telemetry`
  (`Tellma.Web` policy, `AsTenantlessEndpoint("client-telemetry")`, `tellma-telemetry`, 32 KB,
  untraced) and, under `/api/admin/v1`, the three support routes of N26 §12.7.
- **From:** N24 E6 (a, b); N26 E1 (c); A§6.3 r5; A§6.1; A§6.6.
- **Flags:** `CASCADE` → 0016 §2.1, §8.2, §8.5, §9.4 (X-0016-1; §8.2, §8.5, §9.4 are in
  `gaps.md`); 0013 §10.4, 0021 §2.5, §8.

### X-0011-8 — §5.2 anonymous list
- **Current (line 1184–1188):** "`AllowAnonymous` is permitted only on … the strings pack (spec
  0013 §10.4), the notification catalogue (spec 0021 §2.5), … the SPA fallback".
- **Delta:** remove the strings pack and the notification catalogue; add "the SPA's static
  assets".
- **From:** N24 E7; r5.

### X-0011-9 — §5.3 endpoint audit: SPA segments and the telemetry prefix
- **Current (line 1205–1207):** "a distribution endpoint sits under a reserved prefix (`/bff`,
  `/api/admin`, `/api/distribution-info`, `/health`, `/id`, `/openapi`, `/.well-known`,
  `/signin-oidc`, `/signout-callback-oidc`)".
- **Delta:** add `/api/client-telemetry` to the reserved prefixes; a stack's `ResourceSegment` or a
  hand-mapped tenant route whose second segment is in `TellmaRoutes.SpaTenantSegments`
  (`settings`, `me`, `list`) fails startup; a distribution endpoint whose first segment is in
  `SpaFirstSegments` (`tenants`, `signed-out`) is refused like a reserved prefix.
- **From:** N24 E7a; N26 E2; r5.
- **Flags:** `CASCADE` → 0016 §4.6 (the projection audit, which also covers child segments per
  N33 E8; X-0016-20).

### X-0011-10 — §5.5 BFF endpoints (a): the post-callback redirect; rename
- **Current (line 1315–1317):** "After the callback the BFF redirects to `returnUrl` when one was
  given; otherwise, when exactly one membership of §3.8 is `Live`, `Active` and `isActive`, straight
  into `/{tenantId}/`, skipping the picker; else to `/`, the picker."
- **Delta:** redirect to `returnUrl ?? "/"`; delete the skip-rule sentence (the SPA's launch
  redirect owns it); "company picker" → "tenant picker" throughout §5.5.
- **From:** N24 E8(a), E8(f); A§6.3 r1.
- **Flags:** `CASCADE` → §3.8 (X-0011-5).

### X-0011-11 — §5.5 BFF endpoints (b): `/bff/login` forwards `ui_locales`
- **Current (line 1311):** "`GET /bff/login?returnUrl=&acr_values=&max_age=`".
- **Delta:** also accepts `ui_locales` and forwards it as the OIDC parameter.
- **From:** N24 E8(b).
- **Flags:** `CASCADE` → 0010 Part A (X-0010A-3).

### X-0011-12 — §5.5 BFF endpoints (c): `POST /bff/logout` answers `LogoutResult`
- **Current (line 1317–1318):** "`POST /bff/logout` (`{ "scope": "local" | "global" }`, default
  `global`: local sign-out then RP-initiated end-session with `id_token_hint`; clears
  `tellma.profile`)".
- **Delta:** answers `200 LogoutResult(RedirectUrl)`: for `global` the end-session URL the OIDC
  handler builds (`OnRedirectToIdentityProviderForSignOut` captures it; `post_logout_redirect_uri` =
  `/signout-callback-oidc`), for `local` `/signed-out`; clears `tellma.profile`; sends
  `Clear-Site-Data: "cache"`; calls the listener (X-0011-6); `SignedOutRedirectUri` = `/signed-out`.
- **From:** N24 E8(c); A§6.3 r3; A§6.1 (`LogoutResult`).

### X-0011-13 — §5.5 BFF endpoints (d): profile cookie v2 and renewal
- **Current (line 1321–1327):** "the readable profile cookie `tellma.profile`: `{ v, name, locale }`
  … has the session cookie's absolute lifetime, is never rewritten, and is cleared by
  `/bff/logout`".
- **Delta:** v2 = `base64url(JSON { v: 2, sub, sh, name, email, locale })` (`sh` = the session
  handle, a name not a credential); re-issued whenever the session cookie is (sliding renewal, a
  refresh that changes `name` or `locale`); expires with the session's idle expiry, capped by the
  absolute; a request carrying `Tellma-Activity: background`, **or any request on the `Hub` route
  group (negotiate and connect)**, renews neither (`OnCheckSlidingExpiration` sets `ShouldRenew =
  false`).
- **From:** N24 E8(d); N25 E16 (the bold part); A§6.3 r2.
- **Flags:** `DUPLICATE` (N24 E8(d) and N25 E16 write the same sentence; N25 extends it, merged).
  N25 E16 extends A§6.3 r2, which names only the header. `CASCADE` → 0021 §6.1 (X-0021-9).

### X-0011-14 — §5.5 BFF endpoints (e): the client-caching sentence
- **Current (line 1328–1331):** "the SPA takes it from `me` (spec 0014) and `settings/client` (spec
  0013), caches both per tenant in local storage, and validates the cache by version tags … falls
  back to the picker on `403` or `404`."
- **Delta:** → "Client caching is spec 0025's; a `404 tenant-not-found` sends the SPA to the tenant
  picker (spec 0024 §11)".
- **From:** N24 E8(e); A§6.3 r4.

### X-0011-15 — §5.9 `DistributionInfo`, admin surface
- **Current (line 1431–1435):** `DistributionInfo(Slug, DisplayName, DeploymentId, PlatformVersion,
  DistributionVersion, IdentityInfo Identity, Surfaces, Login, AdminVersions)`; `IdentityInfo(string
  Authority, string Mode)`.
- **Delta:** `IdentityInfo` gains `AccountUrl` = `{Authority}/account?client_id={slug}` (JSON
  `identity.accountUrl`); `DistributionInfo` gains `DistributionLinks Links` (`record
  DistributionLinks(string? Terms, string? Privacy)`, JSON `links`), `Support(bool Enabled, bool
  ProviderAccess)` (JSON `support`) and `Telemetry(bool Enabled, double SampleRate)` (JSON
  `telemetry`); the admin-surface bullet gains N26 §12.7's support-request routes
  (`/api/admin/v1/tenants/{id}/support-requests`) and the `SupportRequestRead` log event.
- **From:** N24 E9; N27 E2; N26 E3; A§6.3 r6.
- **Flags:** `DUPLICATE`-free merge of three notes on one record (no clash). N26 E3 extends A§6.3
  r6 (`support { enabled }` only) with `ProviderAccess` and a `telemetry` member. `CASCADE` → the
  section's closing paragraph enumerating "the identity-server changes … specified by spec 0010"
  (X-0010-1).

### X-0011-16 — §6.1 commands: `emit-client-assets`
- **Current (§6.1 table):** `migrate`, `provision`, `set-state`, `status`.
- **Delta:** add `emit-client-assets <dir>`: realises the composition through the §2.3 gates
  without a database, configuration beyond `appsettings.json`, hosted services or connections;
  writes spec 0024 §3.2's files; exit 0/1/3.
- **From:** N24 E2 (A§5.2's emit).

### X-0011-17 — §6.2 provisioning steps: report and dashboard seeds
- **Current (line 1556–1561):** "Platform steps: `10 core.bootstrap-administrator` …; `15
  core.settings` …; `20 core.blob-container` …. Spec 0018 adds `100 gl.sample-centers`."
- **Delta:** add `core.report-seeds` (0033 §13) and `core.dashboard-seeds`, which runs after it.
- **From:** N35 E2 (dashboard seeds, "after `core.report-seeds`"); N33 E15 names the step
  `core.report-seeds` but targets only §2.
- **Flags:** `CASCADE` of X-0011-2 for the report step (no note edits §6.2 for it). Neither note
  gives the steps' `Order` values.

### X-0011-18 — §6.6 local development: one workspace, a variable port
- **Current (line 1607):** "From a fresh clone: `pnpm install` in the Client project, …
  `launchSettings.json` is tracked with fixed ports (`7052` for the Web project, `4200` for the dev
  server)".
- **Delta:** "`pnpm install` at the repository root (one workspace)"; dev-server port
  `<SLUG>_CLIENT` from `.dev-ports.local`, else 4200.
- **From:** N24 E3; A§6.3 r7.
- **Flags:** `CASCADE` → 0018 §6.3 ("`pnpm install` in the Client project") and the fixed
  `localhost:4200` origins in §1.2 and 0018 §6.2 (`gaps.md`).

### X-0011-19 — §8 configuration: seed languages, support, telemetry, links
- **Current (§8):** `"Seed": { "Tenants": [], "AdminEmail": null }`; key row "`Seed:Tenants`
  (`Id`, `Name`, `Category`, `LiveTenantId`)".
- **Delta:** `Seed:Tenants` entries gain optional `Languages` (`["en","ar"]`) and `Calendars`
  (`["gc","uq"]`); add `Tellma:Support` (`Enabled` false, `RetentionDays` 30,
  `MaxRequestsPerUserPerDay` 20), `Tellma:ClientTelemetry` (`Enabled` true, `SampleRate` 1.0),
  `Tellma:Links:Terms` and `Tellma:Links:Privacy` (absolute `https` URL or empty; startup refuses
  anything else outside Development), each in the JSON and the key table.
- **From:** N24 E10 (seed; with 0013 §5.7, X-0013-15); N26 E4; N27 E2; A§6.3 r6, r34.
- **Flags:** `TARGET` (vs skeleton): A§6.3 r34 places "tenant 1 languages `en, ar`, calendars `gc,
  uq`" in 0018 §1.2, §6; N24 puts the mechanism in 0011 §8 and 0013 §5.7.

### X-0011-20 — §9 observability: inbound `traceparent`, the relay
- **Current (§9):** "Every log event carries `TraceId` and `SpanId` from the current activity" — no
  inbound trace adoption.
- **Delta:** "Web, BFF and tenantless requests adopt an inbound `traceparent` trace id; the
  recording decision is the server sampler's over the trace id, never the inbound `sampled` flag;
  inbound `tracestate` and `baggage` from browsers are dropped. Logs export with `IncludeScopes`.
  `POST /api/client-telemetry` is excluded from request tracing and never renews the session; its
  instruments and records are spec 0026 §6.4's." Add the client alert queries to
  `infra/monitoring/`.
- **From:** N26 E5; A§6.3 r8; N24 E17 cites it.
- **Flags:** N26 widens r8 ("on the web surface") to BFF and tenantless requests.

---

# 0012 — `0012-data-access-layer.md`

### X-0012-1 — Context ¶1 and Goals: the engine amendments live in 0010 Part F
- **Current (line 27):** "binds the slots, and documents seven additive engine amendments
  (§11.2): the key-set restriction, `level()` and the prologue/body split …, paging slots,
  tenant-zone context values, versioned leaves and `FilterTree.Via`"; Goals (line 17) "the engine
  amendments spec 0008 deferred".
- **Delta:** → "relies on the engine amendments of spec 0010 Part F (key-set restrictions,
  `level()`, the prologue/body split, paging slots, versioned leaves, `FilterTree.Via`, tree level
  navigations) and binds the context slots to the tenant (§5.1)"; drop the Goals clause.
- **From:** F E1; A§6.3 r11.

### X-0012-2 — §2.2 annotations: `[DisplayNameSource]`
- **Current (§2.2):** the annotation block and table have no display-name source.
- **Delta:** `public sealed class DisplayNameSourceAttribute(string property) : Attribute {
  public string? Prefix { get; init; } } // on type; inherited`; table row "`[DisplayNameSource]` |
  `EntityMetadata.DisplayName`; the related projection's minimum (§2.5) | rendering: spec 0029
  §5"; default source: the `Name` group, else the first natural key, else `Id`.
- **From:** N29 E1; A§6.3 r9; A§6.1 (the same sketch).
- **Flags:** `DUPLICATE` (A§6.1 sketch = N29 E1; merged). Not `[DisplayName]`
  (`System.ComponentModel` owns that name).

### X-0012-3 — §2.5 the related projection carries the display name
- **Current (line 365–368):** "`Id`, the `[Multilingual]` `Name` group, `Code` when the entity has
  one, and every `[BlobReference]` column whose preset is `Avatar`. A declaration replaces the
  default rather than extending it."
- **Delta:** "`Id`, the display-name columns (every twin of its group, or its scalar, and its
  prefix), `Code` when the entity has one, and every `Avatar`-preset blob column. A
  `[RelatedSelect]` replaces the default but must contain `Id`, the display-name columns and `Code`;
  startup rejects one that drops them." Add the `IDisplayNames` paragraph (X-0012-5).
- **From:** N29 E2; r9.
- **Flags:** `CASCADE` → 0015 §2.2 `RelatedSelect` row (X-0015-2); every existing
  `[RelatedSelect]` in specs 0014–0022 must keep the display-name columns (`gaps.md`).

### X-0012-4 — §2.7 natural keys: citation
- **Current (line 399):** "a key restriction binds through (§11.2)".
- **Delta:** → "(spec 0010 §F.1)".
- **From:** F E5.

### X-0012-5 — §3.1 contract: `DisplayName`, `DisplayNameMetadata`, `IDisplayNames`
- **Current (§3.1):** `EntityMetadata` without a display-name member.
- **Delta:** `EntityMetadata.DisplayName { get; }`; `public sealed record
  DisplayNameMetadata(PropertyMetadata Source, MultilingualGroup? Group, PropertyMetadata?
  Prefix);`; in `Tellma.Core.Abstractions.Data`, `IDisplayNames { Columns(entity, shape,
  navigationPath?); Render(entity, values, contentSlot, culture, calendar); }`, with a member row
  citing spec 0029 §5.2 for the rule.
- **From:** N29 E3.
- **Flags:** `CASCADE` → consumers 0019 §4.1 (X-0019-4), 0033, MCP, notifications.

### X-0012-6 — §3.2 startup validation: display-name checks
- **Current (§3.2):** no display-name rule.
- **Delta:** the source names a group anchor or a string, integer or date scalar; the prefix a
  string or integer scalar; both exist on the leaf.
- **From:** N29 E4.

### X-0012-7 — §5.2 `CallerAuthority`: platform composers
- **Current (line 891):** "The platform's own composers — the emitter, the access guards, the blob
  effect and the job, notification, progress and outbox statements — append through an internal
  channel".
- **Delta:** add "the assignment statements (spec 0028)".
- **From:** N28 E13.

### X-0012-8 — §5.4 key capture: citation
- **Current (line 943):** "using `CompiledQuery.Body` (§11.2)".
- **Delta:** → "(spec 0010 §F.3)".
- **From:** F E5.

### X-0012-9 — §6.1 the command's cancellation token
- **Current (line 1019):** "the concatenated text; the cancellation token of the scope."
- **Delta:** "the scope's cancellation token: on a request, `HttpContext.RequestAborted` for `Read`
  and `Validate` batches, the cold prologue-only round trip and retry delays; a `Persist` round
  trip, once sent, runs with `CancellationToken.None` to completion".
- **From:** N24 E11; A§6.3 r10.
- **Flags:** `CASCADE` → 0016 §8.3 still says "The data-access executor receives
  `HttpContext.RequestAborted` on every command … on cancellation … the transaction is rolled back"
  (no note lists it; `gaps.md`).

### X-0012-10 — §6.3 error numbers: `Assignments.AssigneeInvalid`
- **Current (line 1100):** "`50422` | invariants: `Tree.Cycle` (§10.2), `Access.*` (spec 0014),
  `Blob.NotAttachable` …".
- **Delta:** add `Assignments.AssigneeInvalid` (spec 0028).
- **From:** N28 E13.

### X-0012-11 — §10.1 the node scheme: citation
- **Current (line 1551):** "(Queryex `level(Node)` emits `GetLevel()`, §11.2)".
- **Delta:** → "(spec 0010 §F.2)".
- **From:** F E4.

### X-0012-12 — §11.1 the schema provider: `TreeLevels`, `CalendarMap`, citations
- **Current (line 1691–1697):** "**Tree node.** The shadow `Node` … registered as `TreeNode`";
  "child-entity rebasing (`FilterTree.Via`, §11.2 item 7)".
- **Delta:** the "Tree node" bullet adds "and declares `TreeLevels([Tree].MaxDepth)` (spec 0010
  §F.9): navigations `Level1` … `LevelN` to the entity itself"; add "Calendar maps:
  `CalendarMap("uq", "[core].[CalendarMonths]")` (spec 0013 §8, spec 0010 §F.14)"; the schema
  fingerprint includes both; "(`FilterTree.Via`, §11.2 item 7)" → "(`FilterTree.Via`, spec 0010
  §F.7)".
- **From:** N33 E13; F E3.
- **Flags:** `DUPLICATE` (F E3 rewrites N33 E13's citations; merged).

### X-0012-13 — §11.2 engine amendments → pointer
- **Current (lines 1702–1769):** the amendments sketch and items 1–7.
- **Delta:** replace with F §5's pointer text ("Spec 0008 is frozen; the engine changes this spec
  relies on are spec 0010 Part F: key-set restrictions (§F.1), `level()` (§F.2), the prologue/body
  split (§F.3), paging slots (§F.4), versioned leaves (§F.6), `FilterTree.Via` (§F.7) and tree
  level navigations (§F.9) … restrictions on `Id` and `ParentId` cover every consumer.").
- **From:** F E2; A§6.3 r11; A§6.4 pF1.
- **Flags:** `CONFLICT` (behaviour carried by the move): item 4's `Origin = Declared` becomes
  `Literal` (X-0010F-5) and item 7's diagnostic becomes a caller error (X-0010F-8).

### X-0012-14 — §11.3 rows and count: citation and paging slots
- **Current (line 1774):** "`LanguageVersion` per §11.2 item 6, binds `Literal` slots from the
  compiled query".
- **Delta:** "per spec 0010 §F.6"; add after the slot-binding sentence "The paging slots
  `@qx{b}_skip` and `@qx{b}_take` are `Literal` slots like any other (spec 0010 §F.4)."
- **From:** F E6.

### X-0012-15 — §11.4 entity queries: level paths refused in `Select`
- **Current (line 1819):** "`Query<TEntity>(EntityQuery)` restricts `Select` to bare paths (a
  computed item is a compose-time `ArgumentException`…)".
- **Delta:** add "A `Select` path through a level navigation (spec 0010 §F.9) is a compose-time
  `ArgumentException`: no foreign key links a materialized row to its ancestor. `Filter` and
  `OrderBy` may use one."
- **From:** F E7.
- **Flags:** `CASCADE` → 0015 §5.2/§7.3 user-facing 400 (X-0015-15, X-0015-19).

### X-0012-16 — Decisions record 25: citation
- **Current (line 2142):** "25. **Additive engine amendments, language version unchanged** — …
  restrictions on paths cover every consumer (§11.2)."
- **Delta:** "(§11.2)" → "(spec 0010 Part F)".
- **From:** F E8.

---

# 0013 — `0013-settings-localization-and-cache.md`

### X-0013-1 — §1.2 composition: the settings client-asset contributor
- **Current (line 107–118):** "`CoreFeature` … contributes …: the EF configurations …; `…`;
  `ApiService<SettingsService>()`; `Securables(...)`".
- **Delta:** `CoreFeature` registers `SettingsClientAssetContributor` (spec 0024 §3.2) writing
  `settingKeys` and `settingCategories` into the Core chunk.
- **From:** N32 E4.
- **Flags:** `CASCADE` → N24's request that 0013, 0014, 0016, 0021 "write their sections through
  `IClientAssetContributor`" (N24 §7); only this item, X-0014-15, X-0020-4 and X-0021-6 state it
  (`gaps.md`).

### X-0013-2 — §2.5 the bump publishes `cache.changed` (wider, with values)
- **Current (line 416–421):** "For every bumped name that is `settings` or an `entity:<Name>`, the
  epilogue contributor calls … `ClientEvent("cache.changed", [], { tag: <name> })` … The
  `permissions` and user-level bumps publish nothing".
- **Delta:** after the commit: `settings` → tenant group `{ tag: "settings", value: <wire> }`;
  `entity:<Name>` → tenant group `{ tag, value }`; `permissions` → tenant group `{ tag:
  "permissions" }` (no value); a user's `PermissionsTag` → that user, `{ tag: "permissions", value:
  <the user's composite wire tag> }`; a user's `PreferencesTag` → that user, `{ tag:
  "preferences", value: <wire> }`; `entities` and pack names publish nothing; when a user rule
  fired, the bump statement ends with `SELECT N'Permissions' AS [Column], [Id] FROM
  @tm_userIds_Permissions UNION ALL SELECT N'Preferences', [Id] FROM @tm_userIds_Preferences;`.
- **From:** N25 E1; A§6.3 r12.
- **Flags:**
  - `CONFLICT` with A§6.3 r12: the row says "payload `{ tag, value? }`, with `value` only for
    `settings` and `entity:*`"; N25 E1 also sends `value` for the user-level `permissions` and
    `preferences` events.
  - `TARGET`: r12 names "0013 §2.3, §2.4"; the bullet is in §2.5 (N25 is right).
  - `CASCADE` → §12 Testing (line 1603–1608: "a `permissions` bump none"), 0021 §6.3 (X-0021-10),
    0013 §6.4 (X-0013-12) (`gaps.md` for §12).

### X-0013-3 — §2.7 the composite `permissions` wire tag; `client/sync`
- **Current (line 459–464):** "spec 0016 writes `settings`, `permissions` and `entities` from
  `BatchOutcome.VersionTags` and `preferences` from `BatchOutcome.UserVersionTags.Preferences` …
  a changed `entities` composite makes the client fetch `settings/entity-tags` (§6.2) and refetch
  only the lists whose tag moved".
- **Delta:** `permissions` = the composite `"{UserAccess.FormatVersion}.{tenant
  permissions:N}{user PermissionsTag:N}"`; a response whose last batch carried no connect prologue
  omits `preferences` and `permissions`; "fetch `settings/entity-tags`" → "call spec 0025's
  `client/sync`, which names the lists whose tag moved".
- **From:** N25 E3; A§6.3 r13.
- **Flags:** `CASCADE` → 0016 §6.2 (X-0016-22), 0016 §3.4 (X-0016-12), 0014 §10 (X-0014-15).

### X-0013-4 — §2.8 the migrator bumps only after a change
- **Current (line 474–477):** "The migrator runs both statements on every `migrate` per tenant
  database, after applying migrations … so every tag moves after every migration."
- **Delta:** the insert on every `migrate`; the wholesale `UPDATE` only when that run applied at
  least one migration or provisioning step to that database; "so every tag moves after every run
  that changed the database".
- **From:** N25 E4; A§6.3 r14.
- **Flags:** `CASCADE` → §1.1 table (line 96: "Runs the `core.VersionTags` seed and wholesale bump
  (§2.8) on every `migrate`") and §12 Testing (line 1627: "seeding and the wholesale bump … on
  every `migrate`") (`gaps.md`).

### X-0013-5 — §4.3 `GetWireTagsAsync` row
- **Current (line 655):** "… Served by `settings/entity-tags` (§6.2)."
- **Delta:** "Served by `settings/entity-tags` (§6.2) and spec 0025's `client/sync`".
- **From:** N25 E5.

### X-0013-6 — §5.3 setting keys: descriptors, groups, categories
- **Current (§5.3):** `ISettingKeyRegistry { Keys; Find; IReadOnlyList<string> Categories }`;
  `SettingKeyDescriptor` lives in §6.1.
- **Delta:** `SettingKeyDescriptor` moves to §5.3 and gains `Group`, `EnumType`, `LabelKey`,
  `DescriptionKey` (N32 §13.1 shape); `Categories` → `IReadOnlyList<SettingCategoryDescriptor>`
  (`Name`, `Resource`, `Order`, `LabelKey`; `general` first, then first-appearance order); add
  `Describe()`; rules add "a nullable `T` defaults to `null`" (startup gate) and the group rule
  ("the name's segments between the first and the last, less a leading segment equal to its
  category").
- **From:** N32 E1; A§6.3 r15.
- **Flags:** N32 extends r15 (which names only `DescriptionKey`, `EnumType` and the category label
  key and order) with `Group` and `LabelKey`.

### X-0013-7 — §5.4 labels: the settings page renders from the metadata
- **Current (line 821):** "the settings page renders the same key from the string pack (§10.4)".
- **Delta:** → "the settings page renders it from the client metadata (spec 0032)".
- **From:** N32 E2.

### X-0013-8 — §5.7 provisioning: seeded languages and calendars
- **Current (line 940–943):** the `core.settings` step writes "`PrimaryLanguage` = the
  distribution's default language, `PrimaryCalendar = 'gc'`, `TimeZone = 'UTC'`".
- **Delta:** the first provisioning of a seeded tenant writes the seed entry's `Languages` and
  `Calendars` (X-0011-19) into its settings.
- **From:** N24 E10; A§6.3 r34.
- **Flags:** `TARGET` (vs skeleton r34, see X-0011-19).

### X-0013-9 — §6.1 the settings service contract
- **Current (line 954):** `public Task<(byte[] Json, string WireTag)?> ClientAsync(string? ifTag);
  // null = unchanged`; records `TenantSettingsDetails`, `SettingKeyDescriptor`,
  `TenantSettingsPatch`; `SaveAsync` returns `TenantSettingsForClient`.
- **Delta:** (a) `ClientAsync()` → `(byte[] Json, string WireTag)`, never null; `ifTag` removed;
  (b) N32 §13.1's records replace `TenantSettingsDetails` (no `Keys`; adds `EntryStamps`),
  `TenantSettingsPatch` (`ExpectedModifiedAt?`, `ExpectedEntryStamps`, `Concurrency`) and
  `SaveAsync`'s result (`SettingsSaveResult(Details, Client)`); `SettingKeyDescriptor` moves out
  (X-0013-6).
- **From:** N25 E5 (a); N32 E3 (b); A§6.3 r16, r17.
- **Flags:** two notes edit the same code block; the parts do not clash.

### X-0013-10 — §6.2 `client`, `entity-tags` and `details`
- **Current (line 988–990):** "`Client(ifTag)` returns … or the *unchanged* result when `ifTag`
  equals the current wire tag"; (line 1000) "`Keys` describes every declared key of a held
  category".
- **Delta:** first sentence → "`Client()` returns the pre-serialized bytes and their wire tag; the
  SPA does not call it, because spec 0025's `client/sync` returns the same bytes when its held tag
  differs"; the `Keys` sentence → "`EntryStamps` gives each stored row's `ModifiedAt`; descriptors
  are in the client metadata (§5.3)".
- **From:** N25 E5; N32 E3; r16.

### X-0013-11 — §6.3 `save`: stamps per entry, entries-only saves
- **Current (line 1020):** "`ExpectedModifiedAt` is the typed row's stamp and is required
  (`Concurrency.StampRequired`)."; steps 2–5.
- **Delta:** `ExpectedModifiedAt` is required only when `Fields` names a typed member; each listed
  entry carries its expected stamp (`ExpectedEntryStamps`; `null` = no row read); steps 2–5 → N32
  §13.1's (validate batch reads the typed row only when listed; per-entry `Check`; a
  `UX_SettingEntries_Key` violation on insert is `ConcurrencyException`; `Override` skips stamp
  checks); the save answers `SettingsSaveResult`.
- **From:** N32 E3; A§6.3 r17.
- **Flags:** `CONFLICT` with A§6.3 r17: "entries carry **per-category** expected stamps; `save`
  answers the new stamps"; N32 uses one stamp **per stored entry row** (`EntryStamps`,
  `ExpectedEntryStamps`) and adds `ConcurrencyMode Concurrency` (Overwrite) to the patch.

### X-0013-12 — §6.4 `refresh-caches` publishes user tags too
- **Current (line 1070–1072):** "After the commit the action publishes `cache.changed` (§2.5) for
  `settings` and for every `entity:<Name>` of the composition."
- **Delta:** "… for `settings`, every `entity:<Name>`, and `{ tag: "permissions" }` and `{ tag:
  "preferences" }` to the tenant group (no values: every user's tags moved)".
- **From:** N25 E2.
- **Flags:** none; note the tenant-group, value-less `preferences` event is a form only this item
  uses (X-0013-2 addresses `preferences` to one user, with a value).

### X-0013-13 — §6.5 validation codes
- **Current (line 1092):** "`Concurrency.StampRequired` | `ExpectedModifiedAt` absent".
- **Delta:** covers entries too (a listed entry without an expected stamp).
- **From:** N32 E3.

### X-0013-14 — §7.6 labels: navigation, collection and action labels; key twins
- **Current (§7.6 table):** `PropertyLabel`, twins, `EntityLabel`, `EntityLabel(plural)`,
  `EnumValueLabel`, `SettingLabel`.
- **Delta:** rows `NavigationLabel(T, n)` (`<Schema>_<Entity>_<Navigation>` → the FK's
  `PropertyLabel` → Core `<Navigation>` → humanized), `CollectionLabel(T, c)`
  (`<Schema>_<Entity>_<Collection>` → the child's plural), `ActionLabel(T, a)`
  (`<Schema>_<Entity>_Action_<Name>` → Core `Action_<Name>` → humanized); each call gains a
  key-resolving twin (`PropertyLabelKey`, …) returning the first key of its chain present in a
  neutral `Strings` resource, or null; spec 0029's emit writes it.
- **From:** N29 E5.

### X-0013-15 — §8.2 the three systems: `core.CalendarMonths`
- **Current (line 1251–1253):** "The `uq` month-map engine amendment (spec 0008's reserved `'uq'`
  emission) is deferred; it will be specified with this spec's successor when scheduled."
- **Delta:** → "`core.CalendarMonths` (`Calendar varchar(2)`, `Year int`, `Month int`, `StartDate
  date`, `EndDate date` exclusive; PK (`Calendar`, `Year`, `Month`); `UX (Calendar, StartDate)`)
  holds the Umm al-Qura months AH 1318–1500, seeded by a migration's `HasData` from
  `UmAlQuraCalendar`; spec 0010 Part F emits `'uq'` over it"; add "Months are contiguous and never
  overlap per calendar; the unique index on (`Calendar`, `StartDate`) serves the one-row lookup of
  spec 0010 §F.14."
- **From:** N33 E14; F E13; A§6.4 pF7.
- **Flags:** `DUPLICATE` (F E13 appends to N33 E14; merged). `CASCADE` → the Non-goals bullet
  (line 79: "the `uq` month-map engine amendment — deferred") and 0018 §7's initial migration list
  (`gaps.md`).

### X-0013-16 — §10.1 files and keys: new key conventions and Core keys
- **Current (line 1355–1372):** label keys `<Schema>_<Entity>`, `_Plural`, `_<Property>`; enum
  values; `Setting_<key>`; validation codes; `Problem_<code>`; month names.
- **Delta:** add conventions — navigations and collections (X-0013-14); actions
  `<Schema>_<Entity>_Action_<Name>` (`Name` PascalCased, `/` removed) with Core `Action_<Name>`;
  Queryex diagnostics `Queryex_<code>` (`Queryex_QX3001`) in Core with 0008 Appendix A's named
  arguments; `Entity_DisplayNameWithPrefix` ("{prefix} - {name}") and `Entity_DisplayNameFallback`
  ("#{id}"); `SettingCategory_<category>` (Core `SettingCategory_general` "General"),
  `SettingGroup_<category>_<group>`, optional `Setting_<key>_Description`,
  `Securable_<resource>`, `Access_Problem_<code>`, `Access_Reason_<reason>` (Core
  `Access_Reason_self` "Their own records"). Core keys added: `Action_Share`, `Action_Publish`,
  `Action_Transfer`; `ReportKind_List` … `ReportKind_Chart`, `ShareAccess_View`,
  `ShareAccess_Edit`, codes `ReportDefinition.*`, `Report.*`, `Sharing.*`,
  `Notification_core_report_shared`; codes `Dashboard.*`, `Notification_core_dashboard_shared`,
  `Core_Dashboard_Synonyms`.
- **From:** N29 E6; N32 E2; N33 E1 (the three `Action_*` strings), E17; N35 E6; A§6.3 r15.
- **Flags:** four notes add to one section; no clash. N33 E1 lists the `Action_*` strings under
  0014 §2 (`TARGET`: the keys belong here).

### X-0013-17 — §10.4 the string pack: emitted, not served
- **Current (line 1456–1460):** "Spec 0016 serves the pack at `GET /api/strings/{language}?v=<version>`,
  tenantless and anonymous"; (line 1464) "The languages it lets a user switch among are the tenant's
  content languages from `settings/client`".
- **Delta:** keep `StringPack`/`IStringPackProvider`; the route sentence → "spec 0024's emit writes
  one pack per offered language into the client bundle; no route serves packs"; the SPA paragraph →
  "the language switch inside a tenant offers the tenant's content languages (plus the active
  language), the tenantless shell the offered languages (spec 0024 §7.1); server keys merge at the
  root of the client dictionary".
- **From:** N24 E13; A§6.3 r18.
- **Flags:** `CASCADE` → 0011 §5.1/§5.2 (X-0011-7, X-0011-8), 0016 §2.1, §8.2, §8.5, §9.4,
  0021 §2.5, §8.

---

# 0014 — `0014-users-roles-and-permissions.md`

### X-0014-1 — Goals / Non-goals: owner of the engine amendments
- **Current (line 83–84):** "The batch executor, the save emitter, id allocation, the Queryex
  schema adapter and the engine amendments (`KeySetRestriction`, `level()`, `Via`) — spec 0012."
- **Delta:** "… — spec 0010 Part F, composed by spec 0012".
- **From:** F E9.
- **Flags:** `TARGET` — F says "0014 §1 (non-goals)"; the bullet is in Goals / Non-goals.

### X-0014-2 — §2 vocabulary and conventions: `CoreResources`, `AccessActions`
- **Current (line 204–231):** `AccessActions { Read, Save, Delete, Activate, Invite, Preferences,
  Credentials, Retry, Cancel, Resume, Wildcard }`; `CoreResources { User, Role, SettingsGeneral,
  SettingsPrefix, Job, Schedule, Notification, OutgoingEmail, Export, Import, Wildcard }`.
- **Delta:** `CoreResources` adds `Assignment = "core.Assignment"`, `ReportDefinition =
  "core.ReportDefinition"`, `Dashboard = "core.Dashboard"`, `SupportRequest = "core.SupportRequest"`
  (`Read`, `Delete`); `AccessActions` adds `Share`, `Publish`, `Transfer`.
- **From:** N28 E14; N33 E1; N35 E1; N26 E6; A§6.3 r19.
- **Flags:** `DUPLICATE` (four notes and r19 ask the same block; merged). N33 E1's Core strings
  `Action_Share`, `Action_Publish`, `Action_Transfer` belong to 0013 §10.1 (X-0013-16).

### X-0014-3 — §3.3 `core.UserStamps`: `AssignmentsSeenAt`
- **Current (line 391):** "`InboxSeenAt` | `datetimeoffset(3)` | yes | | set by spec 0021's
  `inbox/seen`"; `HasData` `(1, NULL, …, …, NULL)`.
- **Delta:** add `AssignmentsSeenAt datetimeoffset(3) NULL` (set by spec 0028's
  `assignments/seen`); the `HasData` row gains a `NULL`.
- **From:** N28 E14; A§6.2.
- **Flags:** `CASCADE` → §8.2's companion `INSERT` column list (line 1334) and 0018 §7's initial
  migration (`gaps.md`).

### X-0014-4 — §3.4 first paragraph: pinned pages
- **Current (line 410–412):** "Pinned quick-access screens are the key `nav.pinned`; an
  administrator-managed default for less technical users is a tenant setting (spec 0013) the client
  merges when the key is absent."
- **Delta:** "Pinned pages are the key `nav.pinned`, whose shape and client are spec 0027's; a user
  without the key starts with no pins, and an administrator may write it through `preferences/set`
  (spec 0018)."
- **From:** N27 E3.

### X-0014-5 — §3.4 last paragraph: write-behind policy and the warm writer
- **Current (line 442–449):** "the SPA applies them locally at once and flushes coalesced per-key
  writes, on a timer and on page hide, … and a refetch overlays the device's unflushed keys onto the
  fetched bag."
- **Delta:** "the SPA applies them locally at once and flushes them in the background (the flush
  policy and the platform's key registry are spec 0025 §9) … the write's result carries the full bag
  and the new `PreferencesTag` (spec 0018 §3.5), which the writing tab adopts as its bag while still
  showing the keys it has not flushed; another device's or an administrator's write reaches a
  browser as `cache.changed { preferences }` or a newer header tag and costs that browser one
  `client/sync`."
- **From:** N25 E6; A§6.3 r20.
- **Flags:** different paragraph from X-0014-4 (N27 E3); no clash.

### X-0014-6 — §4.1 contracts: the `Fingerprint` row
- **Current (line 681):** "`Fingerprint` | SHA-256 … changes only on deploy; travels in `me` so the
  role editor caches the securables list under it."
- **Delta:** → "a deployment fact; the client metadata's securables section carries the list".
- **From:** N32 E5; A§6.3 r22.
- **Flags:** `CASCADE` → §2 vocabulary "**Fingerprint** — … It changes only on deploy and travels in
  `me`" (line 157–158) and §10's `me` bullet "and the registry `Fingerprint`" and MCP bullet "the
  securables fingerprint" — not listed by any note (`gaps.md`).

### X-0014-7 — §4.4 the registry as data source
- **Current (line 724–725):** "The registry is the role editor's data source (the SPA labels its
  resource and action keys from spec 0013's string pack)".
- **Delta:** the parenthesis → "(the securables section of the client metadata, with emitted label
  keys, spec 0032)".
- **From:** N32 E5.

### X-0014-8 — §5.1 contracts: bulk outcome evaluation
- **Current (line 828):** `public interface IAccessEvaluator // scoped` without a multi-user method.
- **Delta:** `Task<IReadOnlyDictionary<int, AccessOutcome>> EvaluateOutcomesAsync(IReadOnlyList<int>
  userIds, string resource, string action);` — outcomes only; ≤ 2,000 users; one `Read` round trip
  loading those users' active role grants (public roles included); criteria providers asked per
  user; decided by `UserAccess.Decide`; no witness, no cache write; member-table row.
- **From:** N33 E2.

### X-0014-9 — §5.2 step 5: citation
- **Current (line 869):** "each as `FilterTree.Leaf(text, stamp)` (spec 0012 §11.2)".
- **Delta:** → "(spec 0010 §F.6)".
- **From:** F E10.

### X-0014-10 — §5.3 bespoke self-scopes
- **Current (line 921–923):** "Users, jobs, exports, imports and notifications are self-scoped by a
  bespoke criterion that their owning spec's `IAccessCriteriaProvider` supplies".
- **Delta:** add "report definitions and dashboards by spec 0033's sharing criteria".
- **From:** N33 E3.
- **Flags:** `CASCADE` (unlisted): A§6.2 says `core.Assignments` uses a "self-scoped criterion"
  (0028) and N26 adds `core.SupportRequests`; neither is added to this list (`gaps.md`).

### X-0014-11 — §5.5 child entities as query roots: citation
- **Current (line 945):** "rebased through `FilterTree.Via(navigation, inner)` (spec 0012 §11.2)".
- **Delta:** → "(spec 0010 §F.7)".
- **From:** F E10.

### X-0014-12 — §7.1, §7.3 the profile carries `ModifiedAt`
- **Current (line 1030):** `public sealed record UserProfile(` without `ModifiedAt`; the prologue's
  profile set (§7.3) does not select it.
- **Delta:** `UserProfile` gains `DateTimeOffset ModifiedAt`; the prologue's profile set selects
  `[ModifiedAt]`.
- **From:** N32 E15 (decision 19).
- **Flags:** `CASCADE` → 0016 §3.4 `UserProfileView` (X-0016-13).

### X-0014-13 — §7.8 the initializer: background requests do not stamp
- **Current (line 1296–1297):** "it composes a `ConnectPremises.ForSubject` … from the tenant state
  (`ReadOnly` → both flags `false`), the endpoint's `NoActivityStampMetadata`, and the throttle memo".
- **Delta:** also `StampActivity = false` for a request carrying `Tellma-Activity: background`
  (reads and idempotent actions only).
- **From:** N24 E18; A§6.3 r21.
- **Flags:** `TARGET` — r21 says "0014 §7"; the sentence is §7.8.

### X-0014-14 — §9 drift: problems through `roles/problems`
- **Current (line 1463–1465):** "returned by the role details read as a per-permission diagnostic
  … through spec 0018's `RoleService`, which the role editor renders as a banner".
- **Delta:** → "returned by spec 0018's `roles/problems`, which the role editor renders as a banner
  and row markers".
- **From:** N32 E6.
- **Flags:** `CASCADE` → 0018 §4 (X-0018-10), 0016 §2.4 (X-0016-7).

### X-0014-15 — §10 "Can I, and why": `me` and the securables section
- **Current (line 1489–1497):** "`AccessSummary(Tag, …)` … and the registry `Fingerprint`. The SPA's
  first call after sign-in and its refresh call whenever a response's `Tellma-Version-Tags` header
  … carries a tag the SPA does not hold".
- **Delta:** (a) after `AccessSummary(…)`: "whose `Tag` is spec 0013 §2.7's composite `permissions`
  wire tag"; the "SPA's first call …" sentence → "The SPA receives the same `MeResult` from spec
  0025's `client/sync` whenever its held `preferences` or `permissions` tag differs; `users/me`
  serves other callers (the MCP server's `tellma_whoami`)"; (b) add `ClientSecurable` (N32 §13.4)
  and its client-asset contributor (the securables section of the client metadata).
- **From:** N25 E7 (a); N32 E5 (b); A§6.3 r22.
- **Flags:** `CASCADE` (unlisted): the bullet's "and the registry `Fingerprint`" and the MCP bullet's
  "the securables fingerprint" must go with X-0016-12 and X-0016-29 (`gaps.md`).

---

# 0015 — `0015-crud-service-pipeline.md`

### X-0015-1 — §1.2 principle 3: the named consumer
- **Current (line 133):** "`StackDescriptor` is the only thing HTTP, MCP, the SPA schema, securable
  registration, Excel …".
- **Delta:** "the SPA schema" → "the client metadata's entity section (spec 0029)".
- **From:** N29 E7; A§6.3 r23.

### X-0015-2 — §2.2 the descriptor: `DefaultOrderBy`, `DisplayName`, `RelatedSelect`
- **Current (line 185–190, 258):** `StackDescriptor(… DefaultSelect, RelatedSelect, DetailsExpand,
  Limits, …)`; row "`RelatedSelect` | … (spec 0012's default …: `Id`, the `Name` group, `Code`,
  `Avatar`-preset blob columns)".
- **Delta:** add `string? DefaultOrderBy` and `DisplayNameMetadata DisplayName` (after
  `DetailsExpand`); rows "`DefaultOrderBy` | `[Stack(DefaultOrderBy)]`; null = `Id desc`" and
  "`DisplayName` | spec 0012's `EntityMetadata.DisplayName`"; the `RelatedSelect` row adds "always
  containing `Id`, the display-name columns and `Code` (spec 0012 §2.5)".
- **From:** N29 E8; A§6.3 r23.

### X-0015-3 — §2.2 the registry: child query descriptors
- **Current (§2.2):** `IStackRegistry` lists stacks only.
- **Delta:** add `ChildQueryDescriptor` (N33 §10 sketch) and `IStackRegistry.ChildQueries` /
  `FindChildQuery(resource)`; row "every child collection, at any depth, of a stack with `Query`,
  unless `[ApiResource(Queryable = false)]`".
- **From:** N33 E4; A§6.1 (child-entity query routes).
- **Flags:** `CASCADE` → `[ApiResource]` (declared in 0016 §4.1) gains `Queryable`, which no item
  lists for 0016 (`gaps.md`).

### X-0015-4 — §2.2 capabilities vocabulary: `assignable`
- **Current (line 250):** "`Capabilities` | Closed vocabulary: `activatable`, `tree`, `temporal`,
  `multilingual`, `cacheable`, `blobs`, `job-entity`, `searchable`, `excel` (§12)."
- **Delta:** add `assignable`.
- **From:** N28 E12.

### X-0015-5 — §2.3 annotations: `[Stack(DefaultOrderBy)]`; projection attributes
- **Current (line 268–274):** `StackAttribute(…, string? DefaultSelect = null)`.
- **Delta:** `[Stack]` gains `string? DefaultOrderBy = null`; declare `public sealed class
  RelatedSelectAttribute(string select) : Attribute;` and `public sealed class
  DetailsExpandAttribute(params string[] navigations) : Attribute;` (on type, inherited).
- **From:** N29 E9.
- **Flags:** `CONFLICT` (premise): N29 E9 calls the two attributes "referenced-but-undeclared"; both
  are declared in 0016 §4.1 (line 838–839, `Tellma.Core.Abstractions.Api`). Declaring them here
  gives one type two owning specs.

### X-0015-6 — §2.3 annotations: reserved action names
- **Current (§2.3):** "reserved names (the standard operation segments of §2.4) are a startup
  failure".
- **Delta:** → "the standard operation segments the stack projects (§2.4) are reserved; a stack
  without `Export` may name an action `export`" — for `[EntityAction]` and `[ApiAction]` alike.
- **From:** N33 E5 (`reports/export`, `reports/export/start` on a stack that omits `Export`).
- **Flags:** `CASCADE` → 0016 §4.6's projection rule "is not a standard segment" (X-0016-20).

### X-0015-7 — §2.4 standard operations: `extras`
- **Current (§2.4 table):** Query … Custom operation; no extras operation.
- **Delta:** add the standard operation `extras` under `Details` (securable `Read`):
  `GetExtrasAsync(id, include)` — the details plan's step 1 (main id under the access filter) and
  step 3 (the requested extras) only, one `Read` round trip; an invisible id is
  `NotFoundException`; `MaxExtras` applies.
- **From:** N31 E1 (decision 14).
- **Flags:** `CASCADE` → §12's "Keyed, audited" projected-segment list (line 1620), 0016 §2.2/§2.3
  (X-0016-4, X-0016-5).

### X-0015-8 — §2.6 startup checks
- **Current (§2.6, line 374):** "a `[DefaultSelect]`, `[RelatedSelect]` or `[DetailsExpand]` text that
  fails …".
- **Delta:** add "a `DefaultOrderBy` that fails `Validate` as an ordering"; "an extra name declared
  twice across a stack's service and components"; the reserved-name check of X-0015-6.
- **From:** N29 E9; N31 E2; N33 E5.

### X-0015-9 — §2.7 securable registration: child securables
- **Current (§2.7):** the contributor registers each stack's securables.
- **Delta:** per `ChildQueryDescriptor`, also `(Child, Read, FilterRoot = Child, Owner =
  (OwnerResource, OwnerNavigation))`.
- **From:** N33 E6 (0014 §5.5 already anticipates them).

### X-0015-10 — §3.1 `EntityService`: extras
- **Current (§3.1):** no extras operation; no extras declaration.
- **Delta:** `GetExtrasAsync(id, include)` (X-0015-7); `protected virtual IReadOnlyList<string>
  Extras => []` and `DeferredExtras => []`.
- **From:** N31 E1, E2.

### X-0015-11 — §3.2 composable components: deferred extras
- **Current (§3.2):** `IDetailsContributor<T>` without a deferred list.
- **Delta:** `IReadOnlyList<string> DeferredExtras => []; // produced only when asked; the SPA asks
  through extras`.
- **From:** N31 E2 (R14 to 0029: the metadata lists every extra with its kind).

### X-0015-12 — §4.2 wire records this spec consumes
- **Current (§4.2):** lists `QueryRequest` with `Filter`.
- **Delta:** the record list follows X-0016-8 (`Filters`).
- **From:** N30 E2.

### X-0015-13 — §5.2 query step 3: one leaf per filter; default order
- **Current (line 757):** "the user filter is `Leaf(request.Filter)`".
- **Delta:** "the user filter is `And(Leaf(f) for each f in request.Filters)`, each leaf compiled as
  its own clause `filters[i]` for diagnostics"; add "an absent `OrderBy` takes `DefaultOrderBy ??
  "Id desc"`"; add "`IncludeAncestors` on a stack without the tree capability is ignored".
- **From:** N30 E2; A§6.3 r24.

### X-0015-14 — §5.2 query: child query surfaces and the owner-path traversal
- **Current (§5.2 step 4):** the traversal rule runs from the stack root.
- **Delta:** (a) `QueryAsync(request)` applies to a child query surface with `Root` = the child and
  the child's decision; (b) step 4 adds "From a child root whose decision has no direct child
  grant, a path through the owner navigation is checked as if the owner were the root"; (c) the
  rule becomes the named service `IQueryTraversalRule.Check(string root, DiscoveryResult discovery,
  Func<string, AccessDecision> decisions)`, used by `query`, `queryex/discover` (0029) and
  `reports/run`.
- **From:** N33 E7; A§6.1.

### X-0015-15 — §5.2 query: level paths refused in `Select`
- **Current (line 777):** the select check reports `ValidationCodes.QueryGeographyNotSelectable`.
- **Delta:** "a path through a level navigation `Level{n}` (spec 0010 §F.9) is refused with
  `Query.LevelNotSelectable` (argument `path`)"; `Filter` and `OrderBy` may use the path.
- **From:** F E12.

### X-0015-16 — §5.3 details: the `AddExtra` row and the extras plan
- **Current (line 861):** "`AddExtra` | … `name` must be in `Request.Include` and declared by the
  caller's `Extras` (the service declares none and may add any requested name)".
- **Delta:** "(the service declares its names through its own `Extras` and `DeferredExtras`)"; the
  `extras` operation runs only steps 1 and 3 (X-0015-7).
- **From:** N31 E1, E2.

### X-0015-17 — §5.4 get by parent ids: roots and order in one call
- **Current (line 867):** "`Select = request.Select ?? DefaultSelect`, `Filter` = the `And` of
  `Leaf(request.Filter)`, …"; order "`Id` unless the select carries an `OrderBy`".
- **Delta:** `Filters` (X-0016-9); with `IncludeRoots` the result is the roots plus the children of
  `ParentIds` in one statement; null or empty `ParentIds` still means the roots; `OrderBy` defaults
  to `DefaultOrderBy ?? "Id"`; the statement reads `MaxTake + 1` rows and more than `MaxTake` is
  `LimitExceededException("MaxTake", …)`.
- **From:** N30 E3; A§6.3 r25.
- **Flags:** the default differs from X-0015-13's `DefaultOrderBy ?? "Id desc"` (same note; noted,
  not a conflict between sources).

### X-0015-18 — §7.1 contexts: `ActionContext` gains `Assignments`
- **Current (line 1131):** `public abstract class ActionContext<TEntity>` without assignments.
- **Delta:** `BatchAssignments Assignments` (spec 0028 §3.1).
- **From:** N28 E12.
- **Flags:** `TARGET` — N28 cites "§11.2" for `ActionContext`; it is declared in §7.1.

### X-0015-19 — §7.3 errors and codes
- **Current (line 1260–1278):** `ValidationCodes` with `QueryGeographyNotSelectable`.
- **Delta:** add `QueryPathNotReachable = "Query.PathNotReachable"` (arguments `entity`, `path`;
  reported by `queryex/discover` where `query` answers 403) and `Query.LevelNotSelectable`
  (X-0015-15).
- **From:** N29 E10; F E12.

### X-0015-20 — §8 concurrency: Overwrite resends with `Override`
- **Current (line 1357):** "The web details-page save always sends `Check`".
- **Delta:** "The web details page sends `Check`; after the user chooses Overwrite in its conflict
  dialog it resends the same payload with `Override` (spec 0031)".
- **From:** N31 E4 (the text contradicted the section's "the UI offers overwrite").

### X-0015-21 — §9.1 type level: citation
- **Current (line 1385):** "the user's filter the current version (spec 0012 §11.2)".
- **Delta:** → "(spec 0010 §F.6)".
- **From:** F E11.

### X-0015-22 — §11.2 the general action path: argument error paths
- **Current (line 1525–1526):** "either way its `ValidationAttribute`s are walked."
- **Delta:** "… walked, each failure at `arguments.<Property>`".
- **From:** N31 E7.
- **Flags:** `CASCADE` → 0016 §7.3 (X-0016-24).

### X-0015-23 — §12 capability recipes: Assignable
- **Current (§12 table):** Keyed … Excel.
- **Delta:** row "Assignable | `[Assignable]` | `AssignableDeleteEffect<T>`,
  `AssignmentTrailContributor<T>` (extra `assignments`), registered by spec 0028's feature".
- **From:** N28 E12.

### X-0015-24 — §13.1 transactional participation: `PersistContext.Assignments`
- **Current (line 1670):** `public abstract class PersistContext<TEntity>`.
- **Delta:** gains `BatchAssignments Assignments` (spec 0028 §3.1).
- **From:** N28 E12.

### X-0015-25 — §13.3 enlisted writes: composer list
- **Current (line 1990):** "The platform's own composers — the emitter, `IJobQueue`, `INotifier`, …".
- **Delta:** add `IAssignments`.
- **From:** N28 E12.

### X-0015-26 — §14.1 the closed set: a sent persist completes
- **Current (§14.1):** "An `OperationCanceledException` raised on the request's aborted token is
  neither mapped nor logged".
- **Delta:** add "A client abort cancels the stages before the persist round trip (reads,
  validation rounds); a sent persist completes and commits, and its response is discarded with the
  closed connection".
- **From:** N24 E12; A§6.3 r10.
- **Flags:** `CASCADE` → 0016 §8.3 (`gaps.md`).

### X-0015-27 — §15 the Excel row source: `Filters`
- **Current (§15):** the row source binds `ExportSource.ByQuery(string? Filter, …)`.
- **Delta:** `IReadOnlyList<string>? Filters`, composed as X-0015-13.
- **From:** N30 E4.
- **Flags:** `CASCADE` → 0019 §2.1 (X-0019-2).

---

# 0016 — `0016-crud-web-api.md`

### X-0016-1 — §2.1 route table: remove the two GET routes; the fallback sentence
- **Current (line 214–215):** rows `/api/notification-catalogue` and `/api/strings/{language}`;
  (line 227–231) "The SPA fallback (`index.html`) never serves a path whose first segment is `api`,
  `bff`, `health`, `id`, `mcp`, `openapi`, `signin-oidc`, `signout-callback-oidc` or `.well-known`,
  nor a tenant-prefixed path whose second segment is `api`, `mcp`, `hub` or `blobs`".
- **Delta:** remove both rows; the fallback sentence → "The SPA fallback follows spec 0024 §6:
  navigations only, outside `TellmaRoutes.ServerFirstSegments` and the tenant second segments".
- **From:** N24 E15; A§6.3 r26, r18; A§6.6.
- **Flags:** `CASCADE` (unlisted) → §8.2's `tellma-anonymous` row ("strings, the notification
  catalogue"), §8.5 (line 1291), §9.4 (line 1336) still describe the two routes (`gaps.md`).

### X-0016-2 — §2.1 route table: client telemetry and support requests
- **Delta:** tenantless row `POST /api/client-telemetry` (relay; not retried; never MCP); the
  `support-requests` stack row (`query`, `get`, `get-by-ids`, `delete`, `delete-by-query`,
  `submit`; `submit` not retried) and `/{t}/blobs/support-bundle`.
- **From:** N26 E7; A§6.1; r26.

### X-0016-3 — §2.1 route table: child-entity query routes
- **Delta:** row `/{tenantId}/api/web/{child-segment}/query` (POST, `Web`), e.g.
  `sales-invoice-lines/query`, under the owner's `Read`, rebased with `FilterTree.Via`.
- **From:** N33 E8; A§6.1.

### X-0016-4 — §2.2 client retry contract: the new routes' classes
- **Current (line 246–254):** "reads (`query`, `get`, `get-by-ids`, `get-by-parent-ids`, `all`) and
  every `[ApiAction]` with `Idempotent = true` … are retried …; `save`, `import`, `delete`, … are
  never retried".
- **Delta:** retried reads add `extras`, `client/sync`, `queryex/discover`, `reports/run`,
  `reports/browse`, `reports/audience`, `reports/export`, `dashboards/audience`, the dashboards
  `query`/`get`/`get-by-ids`, `assignments/summary`, `assignments/seen`, `jobs/status`,
  `roles/problems`; never retried add `users/me/locale`, `support-requests/submit`,
  `reports/export/start`, `reports/share|publish|transfer`, `dashboards/save|delete|…|share|
  publish|transfer`, `POST /api/client-telemetry`.
- **From:** N24 E15; N26 E7; N27 E4; N28 E15; N31 E3; N32 E8; N33 E8; N35 E3; r26.
- **Flags:** `DUPLICATE` (eight notes add to one list; merged). Note `assignments/seen` is a write
  marked `Idempotent = true` (retried, like `inbox/seen`).

### X-0016-5 — §2.3 stack operations: `extras`
- **Current (line 271–290):** the segment table has no `extras` row.
- **Delta:** row `extras` | `Read` | `ExtrasRequest` | `EntitiesResult<T>`: `ids`, empty `entities`,
  `extras`; 404 absent or invisible | `Details`.
- **From:** N31 E3.

### X-0016-6 — §2.3 stack operations: the reports stack's extra routes
- **Delta:** the `reports` stack's routes with their classes: `browse`, `audience`, `run`, `export`
  (an `.xlsx` stream), `export/start` (202 `JobAccepted`), `share`, `publish`, `transfer`.
- **From:** N33 E8; A§6.1.
- **Flags:** `CASCADE` → §4.6 reserved-segment rule (X-0016-20), §8.3 timeouts (`gaps.md`).

### X-0016-7 — §2.4 service routes: the Core family's new routes
- **Current (line 302–313):** "The routes shipped by the Core family …: `users/invite`, … `users/me`,
  `users/me/save`, … `settings/client`, … `inbox/summary`, … `schedules/run-now`".
- **Delta:** add `client/sync` (0025), `queryex/discover` (0029: member-only, idempotent read,
  hidden from MCP), `users/me/locale` (0018: action of the users stack, `MemberOnly`, mutation),
  `assignments/summary`, `assignments/seen` (0028; `[ApiAction]`s of the assignments stack),
  `jobs/status` (0028), `roles/problems` (0032; roles stack, `Read`), the `dashboards` stack routes
  (0035 §3.2), `support-requests/*` (0026).
- **From:** N24 E15; N29 E11; N27 E4; N28 E15; N32 E8; N35 E3; N26 E7; r26.
- **Flags:** `DUPLICATE` (seven notes add rows; merged). The paragraph's "`settings/client` is
  member-only" stays true; the SPA stops calling it (X-0013-10).

### X-0016-8 — §3.3 `QueryRequest.Filters`; default order
- **Current (line 409–411):** `public string? Filter { get; set; } // Queryex predicate; …`;
  `public string? OrderBy { get; set; }`.
- **Delta:** `public IReadOnlyList<string>? Filters { get; set; } // Queryex predicates, each its own
  leaf, conjoined; at most 32`; the `OrderBy` comment "absent = `StackDescriptor.DefaultOrderBy ??
  "Id desc"`".
- **From:** N30 E1; A§6.3 r24.
- **Flags:** `CASCADE` → §3.4 `QueryDiagnostic.Clause` (X-0016-14), §3.5 example (X-0016-15),
  §11.5 `tellma_query`'s `filter?` argument mapping (`gaps.md`); `DeleteByQueryRequest(string
  Filter, …)` stays singular (consistency, `gaps.md`).

### X-0016-9 — §3.3 `ParentIdsRequest`
- **Current (line 442–445):** `ParentIdsRequest(IReadOnlyList<long>? ParentIds, string? Select,
  string? Filter, IReadOnlyDictionary<string, JsonElement>? Arguments, bool IncludeInactive =
  false)`.
- **Delta:** `(ParentIds, Select, IReadOnlyList<string>? Filters, Arguments, bool IncludeInactive =
  false, bool IncludeRoots = false, string? OrderBy = null)`; the §2.3 row follows.
- **From:** N30 E3; A§6.3 r25.

### X-0016-10 — §3.3 `ExtrasRequest`
- **Delta:** `public sealed record ExtrasRequest(long Id, IReadOnlyList<string> Include); // 1 ≤
  count ≤ MaxExtras; an unknown name is 400`.
- **From:** N31 E3.

### X-0016-11 — §3.3 `SaveRequest` comments
- **Current (line 451, 453):** `Select` "the row echo select; the details page sends the search
  page's select"; `Concurrency` "the details page always sends Check".
- **Delta:** `Select` → "the row echo select; the SPA sends none (spec 0031 marks remembered lists
  stale)"; `Concurrency` → "the details page sends `Check`, then `Override` after the user chooses
  Overwrite (spec 0031)".
- **From:** N31 E5.

### X-0016-12 — §3.4 `MeResult`: no fingerprint; one meaning for the access tag
- **Current (line 497–499, 528–531):** `MeResult(…, IReadOnlyDictionary<string, string> Tags,
  string SecurablesFingerprint)`; "`SecurablesFingerprint` is spec 0014's
  `ISecurableRegistry.Fingerprint`, deployment-scoped, so the SPA refreshes its securable list on a
  deploy without a tag row."
- **Delta:** delete `MeResult.SecurablesFingerprint` and its sentence; append to the paragraph:
  "Its `permissions` member is spec 0013 §2.7's composite, and `AccessSummary.Tag` carries the same
  value."
- **From:** N32 E7; N25 E8; A§6.3 r22, r13.

### X-0016-13 — §3.4 `UserProfileView.ModifiedAt`
- **Current (line 501–504):** `UserProfileView(int Id, …, Gender? Gender)`.
- **Delta:** gains `DateTimeOffset ModifiedAt` (last member).
- **From:** N32 E15.

### X-0016-14 — §3.4 `QueryDiagnostic.Clause`
- **Current (line 511–513):** "`// Clause ∈ select | filter | orderBy | having`".
- **Delta:** `Clause ∈ select | filters[i] | orderBy | having`.
- **From:** N30 E1 (`TARGET`: N30 cites §3.3; `QueryDiagnostic` is in the §3.4 block).

### X-0016-15 — §3.5 query rows: message source; the example
- **Current (line 599–600):** "`errors[]` carries one localized item per diagnostic with `path` =
  the clause name"; example (line 566) `"filter": "Name contains @q"`.
- **Delta:** "… rendered from `Queryex_<code>` for spec 0008 codes and from the validation-code key
  for `Query.*` codes (spec 0013 §10.1)"; the example uses `"filters": ["Name contains @q"]`.
- **From:** N29 E12; N30 E1.

### X-0016-16 — §3.6 entity envelopes: `rows`
- **Current (line 638–640):** "`rows` is the search-page row echo … so the search page updates its
  cached row when the details page loads or saves."
- **Delta:** "… for a client that holds list rows".
- **From:** N31 E5.

### X-0016-17 — §3.9 hand-offs: `me`
- **Current (line 723–726):** "`users/me` returns `MeResult` …; the SPA calls it once after login and
  whenever `Tellma-Version-Tags` carries a `preferences` or `permissions` tag the SPA does not hold".
- **Delta:** → "**`me`.** `users/me` returns `MeResult`, the caller's preference bag included, for
  API and MCP callers. The SPA reads it through spec 0025's `client/sync` and adopts the result of
  its own preference writes (spec 0025 §9.5)."
- **From:** N25 E9.

### X-0016-18 — §3.10 contract fingerprints: computed in `Tellma.Core`, read from the metadata
- **Current (line 736, 750, 759):** "At startup the projector computes one fingerprint per projected
  web endpoint"; "The SPA build embeds its distribution's `contracts.json`."; "The SPA's obligation.
  On `client-outdated` the SPA asks its service worker for the new version".
- **Delta:** "`IContractFingerprints` (`Tellma.Core`, framework-free) computes … the projector stamps
  its value"; "The SPA reads fingerprints from its client metadata (spec 0024 §3.4); `contracts.json`
  stays the review snapshot, and a test asserts the emitted map equals it"; the 412 carries
  `Tellma-Build`; the obligation paragraph → "spec 0024 §5.6".
- **From:** N24 E16; A§6.3 r27.
- **Flags:** `CASCADE` (unlisted) → the `Tellma-Build` bullet (line 763–766): "Every tenant-surface
  response carries … a diagnostic … that no rule compares" contradicts r27 ("the client tells
  server-newer from server-older") and X-0016-22 (every response) (`gaps.md`).

### X-0016-19 — §4.3 endpoint metadata: `NoActivityStampMetadata`
- **Current (line 884–885):** "`NoActivityStampMetadata()` on `users/me`, `settings/client`,
  `settings/entity-tags` and `inbox/summary`".
- **Delta:** add `client/sync` and `assignments/summary`.
- **From:** N24 E18; A§6.3 r21.
- **Flags:** `CASCADE` → the long-operation sentence (line 889–893) must add the reports routes and
  `dashboards/audience` for `tellma-long` (X-0016-27; `gaps.md`).

### X-0016-20 — §4.6 the startup audit: reserved segments
- **Current (line 953–955):** "Every `[ApiAction]`/`[EntityAction]` segment … is not a standard
  segment; every `[ApiRoute]` route is unique and is not a stack segment."
- **Delta:** the audit covers child segments (N33 E8: "the startup audit covers child segments");
  the standard-segment rule must admit an action named like a standard segment the stack does not
  project (X-0015-6).
- **From:** N33 E8 (child segments, `TARGET`: N33 names §2.1, §2.3, §2.4).
- **Flags:** `CASCADE` (unlisted) of X-0015-6: no note edits this rule, which forbids
  `reports/export` as an action segment (`gaps.md`). Related: X-0011-9 (SPA segments in 0011's
  audit).

### X-0016-21 — §6.1 request headers: `traceparent`, `Tellma-Activity`, `Accept-Language`
- **Current (§6.1 table):** `Accept-Language`, `Tellma-Time-Zone`, `Tellma-Calendar`, `Tellma-Client`,
  `Tellma-Contract`.
- **Delta:** rows `traceparent` (honoured per spec 0011 §9) and `Tellma-Activity: background` (reads
  and idempotent actions; no activity stamp, no cookie renewal; ignored on a mutation);
  `Accept-Language` note: the SPA sends its formatting locale (`ar-SA`).
- **From:** N24 E17; A§6.3 r21.
- **Flags:** `CASCADE` (unlisted) → `TellmaHeaders` (§3.4, line 515–523) has no `Activity`
  constant (`gaps.md`).

### X-0016-22 — §6.2 response headers: early stamping; the composite tag
- **Current (line 1066–1076):** "Every tenant-surface response, success or problem, carries
  `Tellma-Trace-Id`"; "`settings`, `permissions` and `entities` from the last batch's
  `BatchOutcome.VersionTags`, `preferences` from its `BatchOutcome.UserVersionTags.Preferences` …
  so the SPA revalidates … without an extra call".
- **Delta:** (a) `Tellma-Trace-Id` and `Tellma-Build` are stamped by an early middleware on every
  response, tenantless `/api/*` and `/bff/*` included, and on early refusals (CSRF 403, 412, 429);
  `Content-Language`, `Tellma-Calendar` and `Tellma-Version-Tags` stay filter-set and are absent on
  early refusals; (b) `settings` and `entities` from `BatchOutcome.VersionTags`, `preferences` from
  `UserVersionTags.Preferences`, `permissions` the composite; `preferences` and `permissions`
  absent when the last batch carried no connect prologue; "so the SPA's refresher (spec 0025 §5)
  compares them …".
- **From:** N24 E19 (a); N25 E10 (b); A§6.3 r28, r13.
- **Flags:** N24 E19 widens r28 (which names only `Tellma-Trace-Id` on tenantless routes) to
  `Tellma-Build`.

### X-0016-23 — §7.1 the mapping: the cookie challenge's 401
- **Current (§5.2, line 984):** "Cookie challenges answer 401 (no session) or 403 …, never
  [redirect]"; no code on the 401.
- **Delta:** the cookie challenge's 401 on API endpoints writes a problem body with code
  `unauthenticated`.
- **From:** N24 E20; A§6.3 r28.
- **Flags:** `TARGET` — the challenge behaviour is stated in §5.2 (line 984) and §7.5; §7.1's table
  maps exceptions.

### X-0016-24 — §7.3 validation error paths: the SPA parses paths
- **Current (line 1183–1184):** "A path is a segment list and is never parsed on either side."
- **Delta:** "A path renders a segment list; the SPA splits it back into property and index segments
  (`name` and `[digits]`) to place each error on its field (spec 0031), and nothing else interprets
  it. An action's argument errors are rooted at `arguments` (`arguments.creditLimit`)."
- **From:** N31 E6.

### X-0016-25 — §7.5 framework problems: `route-not-found`
- **Current (line 1215):** "a route miss under a tenant prefix → 404 `not-found`".
- **Delta:** "→ 404 `route-not-found`" (entity misses keep `not-found`).
- **From:** N24 E20; A§6.3 r28; A§6.6 (fallback).

### X-0016-26 — §8.2 rate policies: `tellma-telemetry`
- **Current (line 1254–1262):** policies `tellma-user`, `tellma-tenant`, `tellma-anonymous`,
  `tellma-export`/`tellma-import`, `tellma-blobs`, `tellma-mcp`.
- **Delta:** `tellma-telemetry` (partition `{sub}`, `TelemetryBatchesPerMinutePerUser` 60, sliding
  window 6, queue 0); the option joins the options object.
- **From:** N26 E8; A§6.3 r29.
- **Flags:** `TARGET` — N26 says "the option to §8.1's object"; the options class is in §1.3
  (line 159–167).

### X-0016-27 — §8.2 rate policies: `tellma-reports`
- **Delta:** per user, 8 concurrent, queue 16, on `reports/run`, `reports/export`,
  `reports/audience` and `dashboards/audience`; the long request timeout on the same routes.
- **From:** N33 E9; N35 E4.
- **Flags:** `DUPLICATE` (N35 E4 extends N33 E9; merged). `TARGET`/`CASCADE` (unlisted): the
  timeout is §8.3's `tellma-long` list (line 1268–1269) and §4.3's long-operation sentence; the
  option belongs in §1.3 (`gaps.md`).

### X-0016-28 — §10.1 instruments: client telemetry names
- **Current (§10.1):** `ApiTelemetryNames` on meter `Tellma.Core.AspNetCore`.
- **Delta:** one sentence: client telemetry instruments are `ClientTelemetryNames` (spec 0026) on
  the same meter.
- **From:** N26 E9.

### X-0016-29 — §11.5 tools: `WhoamiResult` loses the fingerprint
- **Current (line 1511–1512):** `WhoamiResult(WhoamiUser User, WhoamiTenant Tenant, …, string
  SecurablesFingerprint, …)`.
- **Delta:** delete `SecurablesFingerprint` and the sentence describing it.
- **From:** N32 E7 ("0016 §3.4, §11"); A§6.3 r22.

### X-0016-30 — §11.5 tools: `tellma_describe` from the client entity model
- **Current (line 1501):** "`tellma_describe` | … With `entity`: properties (…)".
- **Delta:** "With `entity`: the entity's spec 0029 `IClientEntityModel` description gated by the
  tenant's shape — properties (…), display name, default order, …".
- **From:** N29 E13.

### X-0016-31 — §11.5 tools: `tellma_query` over child query surfaces
- **Delta:** `tellma_query` accepts a child query surface's entity name.
- **From:** N33 E10.

---

# 0017 — `0017-blob-storage.md`

### X-0017-1 — §1.3 composition: the `support-bundle` core kind
- **Current (line 197–201):** "The kinds shipped by Core are declared by attributes on Core
  entities: `user-image` …, `user-signature` …, `export-file` …, `import-file` … and
  `email-attachment` …".
- **Delta:** add `support-bundle` per N26 §12.4 (the `core.SupportRequests` dump and screenshot
  references).
- **From:** N26 E10; A§6.3 r30.
- **Flags:** `TARGET` — r30 names "0017 §2.3, §3"; the core-kinds list is §1.3 (N26 is right).

### X-0017-2 — §2.3 `[BlobReference]`: policies reach the SPA
- **Current (§2.3):** "From it the platform derives: the column shape …, the download authorisation
  (§5), and the Excel exclusion" — nothing client-side.
- **Delta:** add "Each `[BlobReference]` property's policy reaches the SPA in spec 0029's entity
  section (`accept`, `maxSize`, `image`, `thumbnail`, `readAccess`); the SPA pre-checks size and
  type and requests `thumb` only when `Variants` lists it".
- **From:** N29 E14; A§6.3 r30.

---

# 0018 — `0018-core-and-gl-reference-stacks.md`

### X-0018-1 — §1.2 composition: the reference distribution's additions
- **Current (line 129–131):** `Compose(TellmaBuilder tellma) => tellma.UseAzureDefaults()
  .AddFeature<GlFeature>().Languages(["en", "ar"]);`
- **Delta (merged):**
  - `acme.Customer` (child `Contacts`; `[EntityAction("set-credit-limit")]` with
    `SetCreditLimitArguments(decimal CreditLimit)`) and `acme.SalesInvoice` (child `Lines`; child
    `Attachments` with `FileId` `[BlobReference("invoice-attachment")]`; eager extra
    `related-documents`; deferred extra `history` over `[Temporal]` history; zero-argument
    `[EntityAction("close")]`); en and ar resx labels (N24 E21; N31 E8).
  - `UseEntity<Center, MyCenter>()` (`ManagerId` → `core.User`) (N24 E21).
  - securable `acme.AgingReport` and its `[ApiAction]`; `AcmeReports`; the seed
    `acme.sales-by-center` (pivot over `acme.SalesInvoiceLine`, `Center` tree) and a role `Sales` it
    is published to (N24 E21; N33 E16).
  - `DashboardSeed("acme.sales-overview", …)` published to `Sales` (N35 E7).
  - `AcmeSettings`: `acme.invoicing.prefix` (string `"INV-"`, `Client`), `acme.invoicing.padding`
    (int 5) in category `invoicing`; `acme.sales.defaultCurrencyId` (int? → `acme.Currency`),
    `acme.sales.allowedCustomerTypes` (enum array) in `sales`; `acme.integration.marminApiKey`
    (string, `Secret`); labels, descriptions, `Acme_Settings_*` (N32 E9).
  - Development `appsettings` set `Tellma:Support:Enabled` true (N26 E11).
- **From:** N24 E21; N26 E11; N31 E8; N32 E9; N33 E16; N35 E7; A§6.3 r34.
- **Flags:** six notes edit one composition; no clash. `CASCADE` → `acme.Currency` (named by N32
  E9's `references`) is not among the entities any note adds (`gaps.md`); the GL resources
  (X-0018-12); §7 migrations (X-0018-14).

### X-0018-2 — §3.1 service and operations: `users/me/locale`
- **Current (line 253–254, 314–315):** `SaveMeAsync(MeSaveRequest)`, `SetMyPreferencesAsync(…)`;
  member rows `SaveMe`, `SetMyPreferences`, `DeleteMyPreferences`.
- **Delta:** add `public Task<MeLocaleResult> SetMyLocaleAsync(MeLocaleRequest request);` and the
  records `MeLocaleRequest(string? Language, string? Calendar)`, `MeLocaleResult(string?
  PreferredLanguage, string? PreferredCalendar, DateTimeOffset ModifiedAt, string PreferencesTag)`;
  member row `SetMyLocale` · `[ApiAction("me/locale", MemberOnly = true)]`; `users/me/locale`.
- **From:** N27 E1a, E1b; A§6.3 r31; A§6.1.

### X-0018-3 — §3.1 composition of the users stack: the `activity` extra
- **Delta:** `CoreFeature` registers `UserActivityContributor` (eager extra `activity`: `SELECT
  s.[UserId], s.[LastActiveAt] FROM [core].[UserStamps] …`).
- **From:** N32 E12 ("Last active").

### X-0018-4 — §3.1 records: `MeSaveRequest.Concurrency`
- **Current (line 272–275):** `MeSaveRequest(…, DateTimeOffset ModifiedAt)`.
- **Delta:** new last member `ConcurrencyMode Concurrency = ConcurrencyMode.Check`.
- **From:** N32 E14 (0031's Overwrite).

### X-0018-5 — §3.2 hooks: validation for `me/locale`
- **Delta:** add `Users.LocaleEmpty` (`language`): `me/locale` with both members null; `me/locale`
  applies the `PreferredLanguage` and `PreferredCalendar` rows with paths `language` and
  `calendar`.
- **From:** N27 E1c.

### X-0018-6 — §3.3 the `invite` action: links through `ITellmaLinks`
- **Current (line 371, 421):** "`ReturnUrl = <Tellma:PublicOrigin>/<tenantId>`"; the welcome body
  "links the tenant (`<Tellma:PublicOrigin>/<tenantId>`, the invitation's `ReturnUrl`)".
- **Delta:** both → `ITellmaLinks.Tenant(tenantId)` (value unchanged: `{PublicOrigin}/{t}`).
- **From:** N24 E22; A§6.3 r44.
- **Flags:** `CASCADE` → `ITellmaLinks` (`Tellma.Core.Abstractions.Composition`) is defined by the
  new spec 0024 §9.6; no edit to 0011 or 0015 registers it in `CoreFeature` (`gaps.md`).

### X-0018-7 — §3.5 self-service: `SetMyLocale`
- **Delta:** new bullet after `SaveMe`: "**`SetMyLocale(MeLocaleRequest)`.** Validation in memory
  (§3.2). One `IGuardedBatchRunner.Run(BatchPurpose.Persist, …)`: `ContributeLock`, spec 0012 §9.1's
  `Update<TUser>` by the caller's id assigning only the set members (stamped; unchanged values skip
  the row), `ContributeInvariants`, then `SELECT [PreferredLanguage], [PreferredCalendar],
  [ModifiedAt] FROM [core].[Users] WHERE [Id] = @tm_UserId`. The entity's rule bumps the caller's
  `PreferencesTag` …; one round trip. The SPA calls it from the session menu (spec 0027 §12.1)
  instead of `me/save`."
- **From:** N27 E1d; r31.

### X-0018-8 — §3.5 self-service: the preference key grammar
- **Current (line 488):** "a key matches `^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)*$` and is at most
  128 characters".
- **Delta:** `^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9][a-zA-Z0-9-]*)*$` (≤ 128 unchanged); 0014 §3.4's
  "dotted ASCII segments" stays.
- **From:** N25 E11; A§6.3 r32.

### X-0018-9 — §3.5 self-service: `SaveMe` with `Override`
- **Current (line 471):** "**`SaveMe(MeSaveRequest)`.** Three round trips. (1) One `Read` batch …".
- **Delta:** step (1) adds "unless `Concurrency = Override`, which also enlists with `Override`".
- **From:** N32 E14.

### X-0018-10 — §4 the roles stack: `roles/problems`
- **Current (line 585–588):** "`RoleService` adds no operations".
- **Delta:** add `PermissionProblem(int PermissionId, AccessProblemCode Code,
  IReadOnlyList<QueryDiagnostic> Diagnostics)` and `RoleService<TRole>.GetProblemsAsync(IdRequest)`
  (`[ApiAction("problems", Action = "Read", Idempotent = true, Mutation = false)]`): one `Read`
  batch, 0014 §9's five drift conditions in memory.
- **From:** N32 E10.

### X-0018-11 — §4 the roles stack: the `members` extra
- **Current (line 591–596):** "a details extra (`IDetailsContributor<Role>` named `members` …
  projecting `UserId, User.Name, User.Name2, User.Name3, User.Email, Notes`, served only when the
  caller holds `core.User × Read`)".
- **Delta:** deferred (0031); projects `UserId, User.Name, User.Name2, User.Name3, User.Email,
  User.ImageId, User.IsActive, User.State, Notes` by `User.Name`, at most 501 rows; filtered by the
  caller's `core.User × Read` decision rebased through `User` (`FilterTree.Via`); served when that
  decision is not `Denied`.
- **From:** N32 E11; A§6.3 r33.
- **Flags:** N32 extends r33 (deferred, row filter, cap); the three added columns match r33.

### X-0018-12 — §5.5 `GlFeature`: resources
- **Current (§5.5):** no navigation label resources.
- **Delta:** GL resources add `Gl_Nav_Financials` ("Financials" / "المالية") and
  `Gl_Center_CodeHint`.
- **From:** N32 E19.

### X-0018-13 — §6 provisioning and local development: the development seed
- **Current (§6.3, line 873–877):** "`migrate` provisions tenants 1 and 2 … the centers tree lists
  the eight sample rows on tenant 1."
- **Delta:** tenant 1 languages `en`, `ar` and calendars `gc`, `uq` (through X-0011-19 and
  X-0013-8); about 60 `acme.Customer` rows (three types, two currencies, images on some, a few
  inactive) and a 40-node, four-level `gl.Center` tree with inactive nodes; the report and dashboard
  seeds (X-0018-1).
- **From:** N30 E8; N24 E10 (via 0011/0013); N33 E16; N35 E7; A§6.3 r34.
- **Flags:** `CASCADE` (unlisted) → §6.3's "`pnpm install` in the Client project" (X-0011-18), its
  step list (`core.report-seeds`, `core.dashboard-seeds`), and the "eight sample rows" sentence
  (`gaps.md`).

### X-0018-14 — §7 reference distribution migrations
- **Current (line 886–900):** the initial migration lists every `core` table of specs 0012–0021
  (`core.Users` … `Exports`, `Imports`), `gl.Centers` and the UDTTs.
- **Delta:** Acme's new entities' migrations follow §7.
- **From:** N24 E21.
- **Flags:** `CASCADE` (unlisted): the initial-migration table list must add the round-2 core
  tables and columns (`core.Assignments`, `core.ReportDefinitions`, `core.ReportDefinitionShares`,
  `core.Dashboards`, `core.DashboardWidgets`, `core.DashboardShares`, `core.SupportRequests`,
  `core.CalendarMonths` with its `HasData`, `Jobs.LabelKey`/`LabelArgumentsJson`,
  `UserStamps.AssignmentsSeenAt`) and their UDTTs; no note edits the list (`gaps.md`).

### X-0018-15 — §8.1 round trips
- **Current (§8.1 table):** `users/invite` … `users/activate`/`deactivate`.
- **Delta:** rows `users/me/locale` · 1 · — and `roles/problems` · 1.
- **From:** N27 E1e; N32 E13.

### X-0018-16 — §9 testing: `me/locale` vectors
- **Delta:** integration vectors: language only, calendar only, both, neither (422), not offered
  (422), unchanged value (no history row, tag unchanged), read-only tenant (403), the tag bump
  visible in `Tellma-Version-Tags`.
- **From:** N27 E1f.

---

# 0019 — `0019-excel-codec.md`

### X-0019-1 — §1.3, §2.1: `ExportKind.Report`
- **Current (line 157, 203):** "(`ExportKind = Display | ForImport`)"; `public enum ExportKind {
  Display, ForImport }`.
- **Delta:** add `Report` (`core.Exports.Kind` value `Report`).
- **From:** N33 E11.

### X-0019-2 — §2.1 `ExportSourceRequest` and `ExportSource.ByQuery`: `Filters`
- **Current (line 208, 218–219):** `public string? Filter { get; set; }`; `ByQuery(string? Filter,
  string? Search, …)`.
- **Delta:** `IReadOnlyList<string>? Filters`, composed as 0015 §5.2 (X-0015-13); the member-table
  row (line 329) follows.
- **From:** N30 E4.

### X-0019-3 — §2.1 `ExportRequest.Columns`
- **Current (line 227–228, 332):** `public string Select { get; set; } // required; exactly as the
  grid uses it`; `Headers`; row "`ExportRequest.Select` | The grid's select, verbatim; every item
  becomes one column".
- **Delta:** `public IReadOnlyList<ExportColumn> Columns { get; set; } // required, 1–100` with
  `public sealed record ExportColumn(string Expression, string? Header); // Header ≤ 255; derived
  when null`; the `Select` and `Headers` rows become one `Columns` row; "Import template" stays
  `export-for-import` with `Ids = []` (already in line 329).
- **From:** N30 E5; A§6.3 r35.

### X-0019-4 — §4.1 the display-shape plan
- **Current (line 493):** "One column per `QueryexColumn` in select order".
- **Delta:** "One column per `ExportColumn`: a bare path ending at a navigation exports the target's
  display name (`IDisplayNames.Columns` then `Render`, spec 0012) in the request culture's content
  slot; one ending at a `[Multilingual]` group anchor exports that slot, falling back to slot 1; any
  other path or expression exports its value as before. `Id` is exported only when listed. The
  engine's select is the union of the expanded terms."; derived headers use the slash label chain.
- **From:** N30 E6; r35.

### X-0019-5 — §12.2, §12.3 enqueue: job labels; `fileId` on the ready notice
- **Current (§12.2):** `export/start`, `export-for-import/start`, `import/start` enqueue without a
  label.
- **Delta:** enqueue with `Label = new JobMessage("Excel.Export.Label" | "Excel.Import.Label", {
  entity = descriptor.Resource })`; `core.export.ready` arguments gain `fileId` (the `export-file`
  blob id).
- **From:** N28 E11; A§6.3 r36.

### X-0019-6 — §12.1–§12.3 and §4.1: report exports through one writer
- **Current (line 1227–1231):** `ExportJobArguments` cases `DisplayExportJobArguments`,
  `ForImportExportJobArguments`; `IExcelExporter` (line 528).
- **Delta:** case `ReportExportJobArguments(string Resource, ExcelJobCulture Culture,
  ReportExportRequest Request)`; the handler's step 3 dispatches a report case to `IReportEngine` and
  `WriteTableAsync`; `IExcelExporter` gains `WriteTableAsync(ExcelTableSpec,
  IAsyncEnumerable<ExcelTableRow>, ExcelContext, Stream)` with `ExcelTableSpec`, `ExcelTableRow`,
  `ExcelRowStyle`.
- **From:** N33 E11.
- **Flags:** `TARGET` — `IExcelExporter` is declared in §4.1, not §12. `CASCADE` (unlisted): a report
  `export/start` sets no `Label` (X-0019-5 covers only the codec's own routes; `gaps.md`).

### X-0019-7 — §15 testing: corpus cases
- **Delta:** a navigation column (one display-name column, Arabic slot with fallback), a group
  anchor, no `Id` unless listed, `Filters` with two leaves.
- **From:** N30 E7.

---

# 0020 — `0020-background-jobs-and-scheduler.md`

### X-0020-1 — §2.1 `core.Jobs`: label columns
- **Delta:** `LabelKey nvarchar(64) NULL`, `LabelArgumentsJson nvarchar(4000) NULL`.
- **From:** N28 E7; A§6.2; A§6.3 r36.

### X-0020-2 — §2.3 the `Job` entity: label members and render rule
- **Current (§2.3):** `Job` members end with progress and error fields.
- **Delta:** `Job` members for both columns, both in `[DefaultSelect]`; the render order and the
  `entity` argument rule (N28 §11.2); run-now, the tick and 0021's delivery enqueue write null.
- **From:** N28 E7; r36.

### X-0020-3 — §2.3 the `Job` entity: `[RelatedSelect]`
- **Delta:** `[RelatedSelect]` = `Id, HandlerKey, Status, ProgressPercent, LabelKey,
  LabelArgumentsJson, CompletedAt`, so a path into `core.Job` passes 0015 §5.2's traversal rule for
  any reader of the owning row.
- **From:** N32 E18 (decision 22), after X-0020-2.

### X-0020-4 — §3.7 registration: `jobHandlers` in the client metadata
- **Current (§3.7):** the handler registry has no client contributor.
- **Delta:** the registry's client-asset contributor writes `jobHandlers` (`{ key, schedulable,
  labelKey, labelFallback? }` per handler, Core chunk).
- **From:** N32 E17.

### X-0020-5 — §4.1 contracts: `JobRequest.Label`
- **Current (line 511–513):** `JobRequest(string HandlerKey, object? Arguments, DateTimeOffset? DueAt,
  int? RequestedById, int? RunAsUserId, IJobEntity? Entity)`.
- **Delta:** `JobRequest(…, IJobEntity? Entity, JobMessage? Label = null)`.
- **From:** N28 E7.

### X-0020-6 — §4.2 the enqueue statement and `JobRequestList`
- **Current (line 318, 548):** `JobRequestList` (`Ordinal`, `HandlerKey`, `DueAt`, `ArgumentsJson`,
  …); the enqueue `INSERT`.
- **Delta:** both carry `LabelKey` and `LabelArgumentsJson`.
- **From:** N28 E7.
- **Flags:** `TARGET` — `JobRequestList` is defined in §2.5 (line 318), not §4.

### X-0020-7 — §5.2 renew: a progress jump renews at once
- **Current (line 663–664):** "`job.changed { jobId }` is published … for each carried row whose
  `RequestedById` is set."
- **Delta:** add "A `Report` whose percent is at least 5 points from the job's last carried percent,
  and at least 5 s after its last progress-carrying renewal, triggers an immediate renewal (as
  `FlushAsync`), so `job.changed` follows".
- **From:** N28 E8; A§6.3 r37.

### X-0020-8 — §5.3 append: publish on checkpoints
- **Delta:** "After the caller's transaction commits, `Append` publishes `job.changed { jobId }` to
  the job's `RequestedById` (`IClientEventPublisher.Publish(batch, …)`)".
- **From:** N28 E8; r37.
- **Flags:** `CASCADE` (unlisted) → 0021 §6.3's `job.changed` publisher cell ("claim,
  progress-carrying renewal, completion, the job actions") (`gaps.md`).

### X-0020-9 — §7.2 `core.ScheduleStates`: readable as the `state` extra
- **Delta:** "`ScheduleService` declares the eager details extra `state` (`NextDueAt`,
  `LastFiredAt`, `LastScheduledFor`, `LastSkippedAt`, `LastJobId`); `core.ScheduleStates` stays out
  of Queryex".
- **From:** N32 E16; A§6.3 r38.
- **Flags:** `CASCADE` → `ScheduleService` is §7.8; the declaration belongs there too.

### X-0020-10 — §7.9 built-in schedules: id 8
- **Current (§7.9 table):** ids 1–6.
- **Delta:** row `8 · core.assignment-retention · 15 4 * * * · spec 0028`.
- **From:** N28 E10.
- **Flags:** pre-existing gap: id 7 (`0022`'s outgoing-email retention, `45 3 * * *`, 0022 §7.3)
  is missing from the table that "is the record of every Core built-in's id" (`gaps.md`).

### X-0020-11 — §10 hub events: the publisher list
- **Current (line 1408–1411):** "`job.changed { jobId }` … through … `Publish(batch, …)` on the
  completion batch and on the job actions' batch (§11.1), and through `PublishAsync` after claims
  and progress-carrying renewals."
- **Delta:** add the immediate renewal (X-0020-7) and `Append` (X-0020-8).
- **From:** N28 E8; r37.

### X-0020-12 — §11.1 `JobService`: `status`
- **Delta:** `[ApiAction("status", Action = "Read", Idempotent = true, Mutation = false)]
  Task<JobsStatus> StatusAsync()`, `record JobsStatus(DateTimeOffset? LastTickAt, int HeldCount)`
  (`HeldCount` under the caller's `Read` filter, capped at 1,000; `LastTickAt` null unless the
  decision is `Unrestricted`).
- **From:** N28 E9.

### X-0020-13 — §11.3 endpoints and pages: page owners
- **Current (line 1508–1515):** "Client pages render a job's progress …: **Schedules** …;
  **Background jobs** …; **My jobs** …".
- **Delta:** the pages sentence → "The Jobs page and the job details page are spec 0028's;
  Schedules is spec 0032's".
- **From:** N28 E9.

### X-0020-14 — §14.4 retention: support requests
- **Current (line 1685–1686):** "selects expired `Export` and `Import` ids … on the two stacks".
- **Delta:** "selects expired `Export`, `Import` and `SupportRequest` ids … on the three stacks".
- **From:** N26 E12.

---

# 0021 — `0021-notifications-inbox-and-hub.md`

### X-0021-1 — Context: assigned work is 0028's
- **Current (line 21–22):** "Work assigned to a user and an operator's announcements are not
  notifications; a later spec owns both."
- **Delta:** → "Work assigned to a user is spec 0028's (`core.Assignments`, the tray); operator
  announcements are a later spec's; neither is a notification".
- **From:** N28 E1; A§6.3 r39.
- **Flags:** `CASCADE` (unlisted) → line 26's thin-event list (`inbox.changed`, `job.changed`,
  `cache.changed`, `session.ended`) lacks `assignments.changed` (`gaps.md`).

### X-0021-2 — Goals / Non-goals: the same
- **Current (line 94–95):** "**Assigned work and operator announcements** — a later spec".
- **Delta:** as X-0021-1.
- **From:** N28 E1; r39.

### X-0021-3 — §2.3 the type registry: `core.report.shared`, `core.dashboard.shared`
- **Delta:** catalogue rows `core.report.shared` (0033 §7) and `core.dashboard.shared` (category
  `reports`, `Toast = false`, inbox and email as `core.report.shared`, raised on newly added user
  grants); Core categories `jobs`, `files`, `users` add `reports`; the "Raised by" column cites specs
  0033 and 0035.
- **From:** N33 E12; N35 E5; A§6.3 r43.

### X-0021-4 — §2.3, §4.3: category labels
- **Delta:** "A category's label is `NotificationCategory_<Category>`, declared with the first type
  of the category".
- **From:** N28 E6.

### X-0021-5 — §2.4 rendering: an optional detail line
- **Delta:** "A type may add `Notification_<Key>_Detail`, a second line rendered with the same
  arguments; `Render` returns the title, then the detail on a new line when the key exists".
- **From:** N28 E5.

### X-0021-6 — §2.5 the catalogue: emitted into the client metadata
- **Current (line 366–371):** "the SPA fetches them once per deployment from `GET
  /api/notification-catalogue?v=<version>`. The endpoint is tenantless and anonymous, and spec 0016
  §9.4 maps it beside the string pack."
- **Delta:** the route is removed; types, channels and client events reach the SPA in the client
  metadata's Core chunk (section shape 0021's, written through `IClientAssetContributor`).
- **From:** N24 E14; A§6.3 r18.
- **Flags:** `CASCADE` (unlisted) → §8's endpoint sentence ("the tenantless `GET
  /api/notification-catalogue` (§2.5)") and 0016 §8.2, §8.5, §9.4 (`gaps.md`).

### X-0021-7 — §5.2, §5.3 the inbox: actors and job labels
- **Current (line 780–785):** `InboxSummary(int Unseen, …, IReadOnlyList<Notification> Latest,
  IReadOnlyList<InboxRunningJob> Running)`; `InboxRunningJob(int Id, string HandlerKey, …)`.
- **Delta:** `InboxSummary` gains `RelatedEntities Related` (`core.User`, the actors of `Latest`,
  spec 0012's related projection), read by a `Query<User>` restricted to `Latest`'s `ActorUserId`s in
  the same batch; `InboxRunningJob` gains `string? LabelKey, string? LabelArgumentsJson, string?
  ErrorCode`, and the running select lists them.
- **From:** N28 E3; A§6.3 r40, r36.
- **Flags:** N28 extends r36/r40 with `ErrorCode` (so `Held` shows its reason).

### X-0021-8 — §5.4 the client protocol: leader-driven summary; presentation moves
- **Current (line 867–883):** "The SPA calls `inbox/summary` at load, on every `inbox.changed` and
  `job.changed` event, and on hub reconnect. The badge shows … The dropdown lists … A toast pops …
  'View all notifications' opens … The preferences page is …".
- **Delta:** first sentence → "The SPA's leader tab of a tenant (spec 0025 §7) calls
  `inbox/summary` when elected, on every hub (re)connect, on every `inbox.changed` and
  `job.changed`, and on each degraded-mode poll, and stores it as the tenant's `inbox` document,
  from which every tab renders (spec 0025 §8.6)."; delete the badge, dropdown, toast-rule, "View
  all" and preferences-page sentences; add "The bell's badge, panel, toasts and pages are spec
  0028's (§5–§10); the verbs below are what it calls."; keep the verb calls.
- **From:** N25 E12; N28 E2; A§6.3 r39.
- **Flags:** `DUPLICATE` (both notes delete the presentation sentences; N28 also lists "View all"
  and the preferences page; merged; the two wrote it as a joint edit).

### X-0021-9 — §6.1 `TellmaHub` and the connect: no activity, no renewal
- **Current (line 891):** "the `Origin`/`Sec-Fetch-Site` check with cross-origin negotiate refused
  outright".
- **Delta:** add "`NoActivityStampMetadata` (spec 0014: neither the negotiate nor the connect stamps
  `LastActiveAt`), and the background rule of spec 0011 §5.5 (no `Hub`-group request renews the
  session or profile cookie), because a leader tab's reconnects are not user activity".
- **From:** N25 E13 (decision 14).
- **Flags:** `CASCADE` ← X-0011-13.

### X-0021-10 — §6.3 the catalogue: `cache.changed`
- **Current (line 953):** "`cache.changed` | `{ tag }` | spec 0013's epilogue contributor,
  `Publish(batch, …)` per `settings` or `entity:<Name>` bump | every connected user of the tenant".
- **Delta:** payload `{ tag, value? }`; publisher "spec 0013 §2.5's epilogue contributor, per bumped
  tag"; recipients "the tenant group for tenant-level tags; the affected users for user-level tags".
- **From:** N25 E14; A§6.3 r12, r41.
- **Flags:** inherits X-0013-2's `CONFLICT` with r12 (which user-level events carry `value`).

### X-0021-11 — §6.3 the catalogue: `assignments.changed`
- **Delta:** row `assignments.changed` | none | spec 0028's `IAssignments` and `assignments/seen` |
  the parties of the change (spec 0028 §4.5); the registration sentence ("this spec's for the other
  two") names spec 0028's.
- **From:** N28 E4; A§6.3 r41.

### X-0021-12 — §8 securables, endpoints, pages
- **Current (line 1109–1111):** "Pages: the bell (summary dropdown), Notifications (standard search
  over `core.Notification`), Notification preferences."
- **Delta:** → "Pages: spec 0028 (the bell panel, the Notifications and Notification settings
  pages)".
- **From:** N28 E1; r39.
- **Flags:** `CASCADE` (unlisted) → the same paragraph's "the tenantless `GET
  /api/notification-catalogue` (§2.5)" (X-0021-6; `gaps.md`).

### X-0021-13 — §9.2 `Tellma:Realtime`: client timeout
- **Current (line 1131):** `ClientTimeoutInterval { get; set; } = TimeSpan.FromSeconds(30);`.
- **Delta:** `TimeSpan.FromSeconds(150)`; add below the block "150 s covers a hidden leader tab,
  whose keep-alive timer the browser may throttle to once a minute (spec 0025 §8.2)."
- **From:** N25 E15; A§6.3 r42.

### X-0021-14 — Decisions record 19
- **Current (line 1328–1329):** "19. **Assigned work and announcements are not notifications** — …;
  a later spec owns both (Context)."
- **Delta:** as X-0021-1 (assigned work is spec 0028's).
- **From:** N28 E1.

---

# 0022 — `0022-email-outbox.md`

### X-0022-1 — §2.1 the entity: display-name source
- **Delta:** `OutgoingEmail` declares `[DisplayNameSource(nameof(Subject))]` (0012 §2.2, X-0012-2).
- **From:** N32 E20.

### X-0022-2 — §4 the email channel: deep link
- **Current (line 312–313):** "given `tenantName`, `text` and `link` (`<Tellma:PublicOrigin>/<tenantId>`,
  Review flags 1)".
- **Delta:** `link` → `ITellmaLinks.Target(t, TargetResource, TargetId)` (`{PublicOrigin}/{t}/{segment}/{id}`,
  else the tenant root).
- **From:** N24 E23; A§6.3 r44.
- **Flags:** `CASCADE` (unlisted) → the Sara example's link `https://app.example/42` (line 321)
  (`gaps.md`).

### X-0022-3 — §8 admin surface: the page bullet
- **Current (line 484–489):** "**Page "Outgoing emails"**: the standard search, default order
  `CreatedAt DESC`, with filters on … Actions: the three verbs."
- **Delta:** → "**Page.** Spec 0032 owns the Outgoing emails pages."; the server facts stay.
- **From:** N32 E21; A§6.3 r45.
- **Flags:** `CASCADE` (unlisted): the default order `CreatedAt DESC` has no home once the bullet
  goes; with X-0015-5 it would be `[Stack(DefaultOrderBy = "CreatedAt desc")]` on §2.1
  (`gaps.md`).

### X-0022-4 — Review flags: delete flag 1
- **Current (line 631):** "1. **Notification emails link to the tenant root** (§4): no deep-link
  contract exists."
- **Delta:** delete it (renumber the rest).
- **From:** N24 E23.
