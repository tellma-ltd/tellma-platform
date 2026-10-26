# Round-2 rulings

Binding on the thirteen spec authors and on the editors of specs 0010–0022. Precedence: these
rulings, then `design/00-architecture.md` as amended by Part 1, then each design note. Item IDs are
the register's (`C-…`, `R-…`, `M-…`), the edit set's (`X-…`, `G…`) and the quality review's
(`Q-…`). Every verdict is tagged **A** (accept as proposed), **M** (accept with the stated change)
or **R** (reject; the reason is in Part 4).

## 0. The rulings that cascade

1. **Import rule (Q-1).** The entry-point rule governs *static* imports. A dynamic `import()` of a
   lazy entry point from the primary is allowed: route loaders, declaration members (`details`,
   `search`, `component`, `home`), the picker binding's pages, the `/realtime` loader. Type-only
   imports (`import type`) cross any boundary. `tellma-client build` emits `modulepreload` for the
   `/shell` and `/attention` chunks, which is what "eager" means for them. `TM_REALTIME`,
   `TM_ENTITY_PAGES` and `TM_ENTITY_DECLARATIONS` are deleted; one token `TM_APP_DECLARATIONS`
   holds the merged, `hide`-applied declarations of every kind.
2. **URL grammar (Q-2).** `TmSearchQuery` gains `tab?: string` (URL `tab=`) and splits `view` into
   `view?: 'grid' | 'tiles'` and `list?: boolean` (URL `list=1`). Jobs `tab=mine`; Assignments
   `tab=to-me|by-me|all`; Reports `tab=mine|shared|all`. `x.tab`, `?mine=1`, `tabs.param` and
   `hidden` custom filters are gone. No spec adds a parameter outside `x.{key}` and `p.{Key}`.
3. **Declaration shape (Q-40).** 0029 publishes the final `tmEntity` shape before anyone writes;
   launcher members are flat (`section`, `order`, `icon`, `synonymsKey`) on `tmEntity`, `tmPage`
   and `tmCodedReport` alike. No explicit type argument (`tmEntity('acme.Customer', …)`).
4. **Generated action dialogs (Q-3).** An action with arguments gets a dialog generated from its
   argument schema by `TmEntityActions` (`/entity`); `dialog` is the hatch. Search bulk actions and
   the details Actions menu share it.
5. **Details source (Q-25).** 0031 defines `TmDetailsSource<T>`; Settings and My profile are
   details pages over a non-entity source. A `details.page` hatch may publish its own context kind;
   the host exposes `actions`.
6. **Toasts (M-55, M-56).** 0028's policy gains one rule: an outcome with no inline home toasts
   once (a delete that leaves the page, a bulk action's count, a 202 "Started in the background",
   a save that navigates away). "Track" always opens the job's details page.
7. **Part F is 0010-F's (ruling 2).** Every M-75 difference resolves to 0010-F's text. F.11
   (parameter expressions) is cut (Q-7); defaults run as a constant statement first. `'uq'` emits
   the row-grain seek and `QX3104` for anything else (Q-22).
8. **Library amendments.** Where 0010-BE designed an item, its design stands (sibling
   `tm-multi-select`, scrolling chip row in the multi-picker, `clearAll({ keep })`, root
   `Directionality`, public `overlay`, `tmFormatRelativeTime`). Requests BE did not design are ruled
   one by one in Part 3; the `tellma` Trusted Types policy moves to 0024.
9. **Metadata behind the cookie (Q-31).** The three metadata chunks require the session cookie;
   string packs and the bundle stay anonymous; `/signed-out` renders from bundle strings only.
10. **Cuts (Q-7 … Q-23).** Every cut-list row stands except where Part 2 says otherwise. Targets:
    0033 ≤ 1,200 lines, every other spec ≤ 1,100.

## Part 1 — Skeleton amendments (`design/00-architecture.md`)

| § | Old → new |
|---|---|
| §0 item 2 | add "Declarations of every kind are read through `TM_APP_DECLARATIONS`" |
| §1 table, 0032 row | `@tellma/module-gl` ships the Centers layout in `@tellma/module-gl/centers` |
| §1 table, 0035 row | "Depends on —" → "0011, 0013, 0016, 0018, 0021 (edits §6.3)" (C-0035-1) |
| §2.1 `@tellma/module-gl` row | holds `tmGlFeature()` and the `gl.Center` declaration; the layout and `TmGlCenterFields` ship in the secondary entry point `@tellma/module-gl/centers`. Rule: a pack's primary holds declarations only; layouts ship in secondary entry points (C-0032-4) |
| §2.2 intro | add after "Eager means in the initial bundle": "`/shell` and `/attention` are loaded by the primary's route table through `import()` and preloaded by `modulepreload` tags that `tellma-client build` injects; they are budgeted with the initial transfer" (C-0027-1) |
| §2.2 `/entity` row | "full descriptors" → "descriptor data (the entities chunk), client Queryex, expression editor, path tree picker, value and filter controls, picker binding, `TmEntityActions` and the generated action dialog"; the primary row adds "`TmEntities` (summaries synchronous; descriptors after `load()`)" (C-0029-2, C-0029-3, C-0031-4) |
| §2.2 import rule | append: "The rule governs static imports. A dynamic `import()` of a lazy entry point from the primary (route loaders, declaration members, the picker binding's pages, the `/realtime` loader) is allowed; `import type` crosses any boundary; the build fails on a static import that violates the DAG" (Q-1) |
| §2.2 flowchart | add edges `details --> dash`, `attn --> dash`; `reports -.-> dash` becomes a dashed (dynamic) edge (C-0035-3) |
| §2.3 0023 list | remove `drawer`, `accordion` and "(+ multi-select input)"; `chips` → "`chip` (`tm-chip`, `tm-chip-set`)"; add `copy` (`tm-copy-button`, `tm-reference`) (C-0010BE-6, C-0023-1, C-0023-2, Q-17) |
| §2.3 last sentence | "ECharts only with `core-ui/chart`" → "the ECharts engine is a dynamic chunk of `core-ui/chart`, alone in the `on-demand` group; the entry point itself is small" (C-0034-5) |
| §3.3 first snippet | `tmEntity<AcmeCustomer>('acme.Customer', {` → `tmEntity('acme.Customer', {` (name-literal typing); `image: 'ImageId'` → `image: 'Image'`; drop the `import type` line (C-0029-1, C-0030-7) |
| §3.3 "Typed paths" bullet | "the type parameter is optional" → "the emitted `TmEntityShapes` augmentation types paths by the name literal; no type argument" |
| §3.4 `agingReport` | `run: (api, args, signal) => api.read(…)` → `route: 'acme-reports/aging'` with the comment "the common case; `run` is the hatch"; `default: 'today()'` → `default: 'today()'  // applied and echoed by the server`; `type: 'date'` → `type: 'Date'` (C-0033-5, C-0034-1, M-74) |
| §3.4 `MyCenterDetails` | `imports: [TmDetailsKit, TmGlCenterFields]` keeps its names; the import specifier becomes `@tellma/module-gl/centers` (comment) |
| §3.6 row "A different menu" | "redeclare sections or entries in `overrides`; `hide: ['core.roles']`" → "`overrides` patch `section:` and `entry:` keys member by member; `hide: ['entry:core.Role']` (kind-qualified keys: `entity:`, `page:`, `report:`, `section:`, `entry:`, `settingsTab:`, `appBar:`)" (C-0024-4, Q-28) |
| §3.6 row "A search page that is not a list" | "replaces the body" → "replaces the results region; the title bar and toolbar stay the platform's; `TmSearchHost` hosts it" (C-0030-6) |
| §3.6 row "An entity action with arguments" | "`actions: [{ name, dialog }]`; zero-argument actions need nothing" → "nothing: the dialog is generated from the argument schema; `actions: [{ name, dialog: () => import(…) }]` replaces it" (Q-3) |
| §3.6 new row | "A list page with tabs or row links \| `tabs: [{ key, labelKey, queryex? }]`, `rowLink: (row) => string`, `pageActions` \| 0030" (Q-26) |
| §3.6 row "A report the engine cannot express" | "`tmCodedReport` with `run`" → "`tmCodedReport` with `route` (data only), `run` (custom fetch) or `component` (custom UI)" |
| §4.1 row "Client store …" | unchanged; add row "Details source `TmDetailsSource<T>` \| 0031 (new) \| 0032" (Q-25) |
| §4.1 row "Page-context registry …" | add "page scopes (`provideTmPageScope`)" (C-0026-6) |
| §4.2 row "Aggregate and pivot execution" | "(grouping sets, tree rollups)" → "(grouping sets, level navigations)" (C-0033-1) |
| §4.3 row "No subtotals…" | "tree rollup, omitted-parameter folding, constant evaluation" → "level navigations, omitted-parameter folding by polarity, constant statements, `BindParameters`, `Exists`" |
| §4.3 row "Column editor can 403" | unchanged |
| §5.1 `TmFeatureDefinition` | `overrides?: readonly TmEntityDeclaration[]` → `overrides?: readonly TmDeclarationPatch[]  // kind-qualified keys, member-wise`; add `icons?: Readonly<Record<string, string>>` (0023 registry), `widgets?: readonly TmWidgetTypeDeclaration[]` (0035); `entries` gain `order`, `icon`, `synonymsKey`; `tmPage(path, { component, titleKey, section?, securable?, order?, icon?, synonymsKey? })`, paths may carry `:param` segments (M-24, M-27) |
| §5.1 new line | `declare const TM_APP_DECLARATIONS: InjectionToken<TmAppDeclarations>; // merged, hide-applied declarations of every kind` |
| §5.2 `TmClientMetadata` | `distribution` gains `copyright`; add `settingCategories`, `jobHandlers`, `widgetTypes` (M-28); `contracts` stays `Record<string, string>` (C-0024-2 as followed) |
| §5.2 "Loading" bullet | append "The three metadata chunks require the session cookie (§6.6); string packs do not" (Q-31) |
| §5.3 `TmReadOptions` | add `long?: boolean` (watchdog class); `TmApi` adds `upload(kind: string, file: Blob, options: { fileName: string; signal: AbortSignal; onProgress?: (fraction: number) => void }): Promise<TmBlobDescriptor>  // the one XMLHttpRequest in the client` and `send(route, body, options?: { background?: boolean })` (Q-24, M-20) |
| §5.3 last bullet | "a record to `TmRequestLog`" → "`TmRequestLog.begin` at send and `end` at settle" (C-0026-3) |
| §5.3 bullet "The platform provides `TM_BLOB_FETCHER` over `TmApi`" | → "over `fetch` with the same user check and `If-None-Match`; blob URLs come from `TmUrls.blob(kind, id, { variant?, download? })`" (C-0010BE-7, M-21) |
| §5.4 `TmLocalization` | add `readonly timeZone: Signal<string>; load(code: string): Promise<void>; readonly languages: Signal<readonly TmLanguageDefinition[]>`; `provideTellma` provides `TM_TIME_ZONE`, `TM_NOW`, `TM_ACTIVE_LOCALE`, `TM_CALENDAR` (M-29) |
| §5.5 bullet "A session's data ends with it" | append "The idle window is `DistributionInfo.session.idleDays` (default 7)" (Q-34) |
| §5.6 `TmBusMessage` | `prefs.changed { tenantId, keys }` → `{ tenantId; values: Readonly<Record<string, unknown>> }`; `hub.status` gains `leaderVisible: boolean` and status `'disabled'`; `drafts.reply` gains `busy: boolean` (C-0025-2, C-0025-3, Q-30) |
| §5.7 `TmLaunchDocs` | delete the index member; `TmLaunchCache` gains `entitySet(name: string): Signal<TmCachedEntitySet \| undefined>  // starts the lazy load on first read` (C-0025-11); `TmTenantFacts` gains `realtime: boolean` (C-0025-9) |
| §5.7 `TmCacheRefresher` | add `adopt(tenantId: number, docs: { settings?: …; me?: … }): void  // a write's result replaces the document and its tag` (R-0032-R9) |
| §5.7 "Flush" bullet | delete "(overlaid with keys still unflushed)" and the `at`/overlay sentences; → "the tab adopts the returned bag and tag through `adopt`; unflushed keys overlay at read time" (C-0025-6, Q-13) |
| §5.7 "Platform keys" | `search.<entity>.columns` = `TmColumnPreference[]`; `.view` = `{ view?: 'grid' \| 'tiles'; list?: boolean }`; add `notifications.toasts`, `dashboard.<id>.params`; `nav.pinned` = `TmPinsDoc`, fallback `{ v: 1, items: [] }` (C-0030-2, M-61, M-62) |
| §5.8 sketches | `TmPageState` adds `markStale(filter)`, `isStale`, `forget`; `TmTreeExpansion` adds `replace(page, ids)`; `TmRecents` "by route template" → "by tenant-relative path, any routed page, cap 10" (C-0025-12, C-0025-7) |
| §5.9 `TmRequestRecord` | add `outcome: 'pending' \| 'ok' \| 'cancelled' \| 'error'`, `tenantId`, `background`, `code`; `TmRequestLog` gains `begin(…): TmRequestHandle` (C-0026-3) |
| §5.9 `TmErrorRecord` | add `source: 'api' \| 'render' \| 'global'`, `fingerprint: string  // errorClass + first frame` (C-0026-7, Q-8) |
| §5.10 `TmPageContext` | add `status: Signal<'loading' \| 'ready' \| 'error'>`; `TmPageAction` adds `group: 'primary' \| 'menu'`, `order?`, `confirm?: string`, `descriptionKey?`; `TmFormContext` adds `dirty`, `assisted`; `TmPageActions.register` "adds to the current context" → "adds to the nearest page scope (`provideTmPageScope`), else `current`" (C-0026-6, C-0026-7) |
| §5.10 AG-UI bullet | "the context maps to a `STATE_SNAPSHOT` with `STATE_DELTA` changes" → "the context is the client-side state the agent receives in `RunAgentInput`; actions map to frontend tools" (C-0026-1) |
| §5.11 `TmNavEntry` | `section` optional; `requires?: TmSecurableRef \| readonly TmSecurableRef[]`; `TmShellSlot.badge?: () => Signal<number \| null>` (C-0027-2, C-0027-4) |
| §5.11 `TmShortcuts` | append "A `page` shortcut fires only when the event is not `defaultPrevented`; `F2`, `Mod+S`, `Mod+Enter`, `Alt+PageUp` and `Alt+PageDown` are admitted page keys" (M-59) |
| §5.12 `TmEntities` | `get(name): TmEntityDescriptor` → `summary(name): TmEntitySummary; bySegment(segment): TmEntitySummary \| undefined; load(): Promise<void>; get(name): TmEntityDescriptor  // after load()`; `TmEntityMetadata` gains `treeLevels: number` (C-0029-2, M-47) |
| §5.13 `TmSearchQuery` | `view?: 'grid' \| 'tiles' \| 'tree' \| 'list'` → `view?: 'grid' \| 'tiles'; list?: boolean; tab?: string` (Q-2) |
| §5.13 `TmUrls` | add `blob(kind, id, { variant?, download? }): string`; `describe` kinds gain `report` and `list` (M-31) |
| §5.13 `TmDetailsContext` | `cancel(): void` → `cancel(): Promise<boolean>`; add `id`, `status`, `error`, `dirty`, `can`, `actions`, `source: TmDetailsSource<T>`, `registerField(path)`, `registerCollection(name)` (C-0031-2, Q-25, R-0035-R8) |
| §5.13 `TmReportHost` bullet | add "`run({ clear: true })` forces a refresh that clears the held result" (C-0034-4) |
| §5.14 | `TmBanners.show(banner: TmBanner)` → `show(options: TmBannerOptions): TmBannerRef; readonly visible: Signal<boolean>`; "precedence 1–5" → "levels 1–5" (M-1, M-58) |
| §6.1 route table | reports row: "the stack omits `Export` and `Import`; `export` and `export/start` are `[ApiAction]`s" (C-0033-3); add the generated-dialog note to no row (client only); `jobs/status` and `roles/problems` never enter the table (Part 3) |
| §6.1 `ClientSyncRequest` comment | "document → wire tag held" → "tag name → wire tag held" (C-0025-5); `ClientSyncResult` gains `bool Realtime` inside `TenantFacts` |
| §6.2 `core.SupportRequests` row | "dump and screenshot blob refs" → "one `support-bundle` blob: a JSON document embedding the screenshot as a data URL" (C-0026-4) |
| §6.2 `core.Assignments` row | "to the old and new assignee" → "to the old and new assignee and the assigners of the closed and new rows" (C-0028-1) |
| §6.3 row 0011 §5.5 profile cookie | append "or any request on the `Hub` route group" (C-0025-10) |
| §6.3 row 0013 §2.3, §2.4 | target "§2.5"; "with `value` only for `settings` and `entity:*`" → "with `value` for `settings`, `entity:*` and the user-addressed `permissions` and `preferences` events" (C-0025-1) |
| §6.3 row 0013 §6.3 | "per-category expected stamps" → "per-entry expected stamps" (C-0032-1) |
| §6.3 row 0015 §5.4 | add "`Filters` and a `MaxTake` cap" (C-0030-3) |
| §6.3 row 0019 §2.1, §4.1 | add "the source carries `Filters`" (C-0030-5) |
| §6.3 row 0018 §4 | "adds `User.ImageId`, `IsActive`, `State`" → "… and a row filter under the caller's `core.User × Read`, capped at 500" (C-0032-2) |
| §6.3 row 0020 §7.2 | add "and the `activity` extra" (C-0032-3) |
| §6.3 row 0014 §2 | add "`SharePermissions` extra `permissions` on `reports/get` and `dashboards/get`" (M-79) |
| §6.3 new rows | 0015 §11.2: `[EntityRef(resource)]` on argument properties (Q-3); 0017 §5: the upload route requires `Tellma-Client` (Q-24); 0011 §5.9: `DistributionInfo.session.idleDays` (Q-34); 0011 §5.10 `ITellmaLinks` (G29) |
| §6.4 Part B row §4, §8 | "§4, §8" → "§4, §1"; delete "tokens" (C-0010BE-4, C-0010BE-3) |
| §6.4 Part B row §3.4 | "`tm-select` multi-select" → "a sibling `tm-multi-select`" |
| §6.4 Part B new row | "§3.2 \| below `sm`, `TM_FORM_FIELD_DEFAULTS.size` resolves to `lg` (44 px touch); menu rows ≥ 44 px \| 0023, 0027" (C-0023-4) |
| §6.4 Part D row §10, §11 | delete "and live `direction`"; Part B gains "a live root `Directionality` watching `<html dir>`" (C-0010BE-2) |
| §6.4 Part D row §2.3, §7 | "(sparing the boot record's device fields)" → "`clearAll({ keep })`; 0025 keeps `tm-app` and `tm.boot` and clears the user fields itself" (C-0010BE-1) |
| §6.4 Part D new rows | `TmModalConfig.role`/`describedBy`; `tm-image placeholderOnError`; `month`/`quarter`/`year` and `weekday` formats; `.tm-button` sizes `tm-icon` |
| §6.4 Part E new row | `TmEntityPickerPage.context`; grid `entity` column `itemDescription` |
| §6.4 Part F row §10.10, §13 | "a tree-rollup grouping key over the dimension's node closure (`IsDescendantOf`), the only fan-out allowed" → "to-one level navigations `Level{j}` declared by `TreeLevels(maxDepth)`; no fan-out" (C-0033-1) |
| §6.4 Part F row §6, §9 | "folds to true" → "folds by polarity; bare omitted list elements drop" (X-0010F-11) |
| §6.4 Part F new rows | `BindParameters` (F.13), `FilterTree.Exists` (F.15); delete nothing else; "context slots" leaves the seven (a host binding rule, C-0010F-3) |
| §6.6 "Hashed JS, CSS, fonts, `generated/*` chunks" | → "Hashed JS, CSS, fonts, string packs: anonymous. Metadata chunks (`core-*`, `entities-*`, `contracts-*`): `public, immutable` but require the session cookie" (Q-31) |
| §6.6 Service worker cell | "the chunks reachable only from `@tellma/core-ui/chart`" → "the ECharts engine chunk"; `/support` stays `on-demand` (C-0034-5, C-0026-2) |
| §7.3 step 2 | "takes the lock with `ifAvailable`; … waits for its `store.changed`" → "queues on the lock and re-checks staleness inside it" (C-0025-4) |
| §7.4 last bullet | "Name-only events … always refresh" → "User-addressed events carry the user's wire tag and refresh only when it differs; the tenant-wide `permissions` event always refreshes after a 0–10 s delay" (C-0025-1) |
| §7.5 step 2 | "A new version of Tellma is available. Reload" → "A new version is available." with a Reload action (0028 owns banner copy) (C-0028-5) |
| §7.8 flowchart and text | steal rule → "visible, and (probe unanswered for 5 s, or the leader hidden and not connected for 30 s)"; add status `disabled` (C-0025-3); "the leader polls … while a tab is visible" → "while it is visible or when a probe arrives" (Q-13) |
| §8 table, `/{t}/assignments` | `?tab=to-me\|by-me` → `?tab=to-me\|by-me\|all` |
| §8 table, `/{t}/jobs` | `?mine=1` → `?tab=mine` |
| §8 table, `/{t}/reports` | "a `search` hatch body" → "a search page with `tabs`; `?tab=mine\|shared\|all` through `reports/browse`" |
| §8 table, `/{t}/list/{segment}` | "the List renderer with a derived heading" → "`TmSearchHost` with a fixed read-only filter leaf, the derived heading and a back link" (R-0030-R23) |
| §8 table, `/{t}/dashboards/{id}` | "edit mode edits the layout" → "edit mode edits the layout, widget settings, dashboard settings and parameters" (C-0035-5) |
| §8 table, `/{t}/{segment}/new` | add "pinnable" (Q-5) |
| §8 query-state table | `view` row → "`view` \| `grid` or `tiles` \| `view=tiles`"; add rows "`list` \| tree shown as a flat list \| `list=1`" and "`tab` \| the page's tab \| `tab=mine`"; `cols` note adds "a multilingual group means its active slot" (C-0029-4) |
| §8 precedence bullet | "URL, then page state" → "the URL query, applied as a whole when any key is present; then page state" (C-0030-8); history adds "a page change past an edge writes `TmSearchSnapshot.supersedes`" (C-0031-7) |
| §9 "Owner" bullet | delete "tokens," (C-0010BE-3) |
| §9 `xl` row | delete "pins overflow into More below this" → keep: "pins overflow into More below `xl`" (the rule, not measured; C-0027-7) |
| §9 "Stable layout" | add "A banner shown while the page is hidden enters at once; the host announces at queue time" (C-0028-6); add "Save-error summaries and notices render in a reserved strip under the toolbar, never inside the scrolling band" (Q-4) |
| §10.1 Non-aggregate Q1 | "opens the tab whose `tm-child-grid` binds `Lines`" → "opens the `tm-details-tab collection="Lines"`" (C-0031-1) |
| §10.1 Details Q3 | append "`beforeunload` is armed only while a save is in flight" (C-0031-5) |
| §10.1 Visibility Q2 | "the List renderer with a derived heading" → "the search host with a fixed filter leaf" |
| §10.1 Visibility Q4 | "a tree-rollup key, omitted-parameter folding, constant evaluation" → "level navigations, polarity folding, constant statements, `BindParameters`, `Exists`" |
| §10.1 Observability Q1 | "every item carries … the last trace ids" → "error items carry the last three trace ids; request items their own" (C-0026-8) |
| §10.1 Dashboards Q1 | append "fixed-value bindings; no dashboard-level defaults" (C-0035-4) |
| §11 "Errors" bullet | "Only `TmApi` handles 401, step-up, 412 and 503 retries" → "`TmApi` classifies 401, step-up, 412 and 503; `TmErrorPresenter` asks once, then redirects" (C-0024-3) |
| §11 gates table, Installability row | "warm chrome ≤ 300 ms" → "warm chrome ≤ 300 ms in a Playwright nightly with 4× CPU throttling (Lighthouse resets storage)" (C-0024-6) |
| §11 budgets | append "a `@defer`red chunk is budgeted on its own line, not in its route's" (C-0031-6) |
| §12 #24 | delete "Trusted Types with ECharts" (settled: canvas renderer, no HTML) (C-0034-7); add #30 "Metadata chunks require the cookie (§6.6)" and #31 "Toasts for outcomes without an inline home" |
| §14 ruling 1 | unchanged |
| §14 ruling 3 | "A new `@tellma/core-ui` entry point is 0023's. A change to an entry point that already exists is 0010's." → "A new component is 0023's. Promoting or changing existing code, even into a new entry point (`overlay`), is 0010's." (C-0010BE-5) |
| §14 new ruling 5 | "Numbering of spec 0010: every part uses letter-dotted sections (`§A.1` … `§F.16`); Part A's existing §2–§9 become §A.1–§A.8 and the citations in 0011, 0016 and 0018 are updated" (X-0010-1) |

## Part 2 — Per-spec rulings

Each list is what the author changes relative to the design note. "Adopt" means the register
item's proposal as the target note answers it; "Modify" states the change.

### 2.1 Spec 0010 — Amendments to Implemented Specs

- **Structure (X-0010-1, M).** Parts A–F, letter-dotted numbering throughout (§14 ruling 5). Each
  part ends with its own Testing, Definition of done, Decisions record and Review flags.
- **Part A (A).** X-0010A-1 … X-0010A-4 as N24 wrote them, renumbered §A.9 (account URL and
  `return_url`), §A.10 (`ui_locales` through PAR). G2, G32, G33 cascades applied.
- **Part B.** B.1 breakpoints, no tokens (X-0010B-3, A); B.2 `TmTheme` (A); B.3 translations and the
  live root `Directionality` (X-0010B-6, X-0010B-8, A); B.4 sibling `tm-multi-select` with joined
  text, no chips (X-0010B-2, M: 0023 A6 rejected); B.5 lint (A); **new B.6** the `xs` touch rule:
  `TM_FORM_FIELD_DEFAULTS.size` resolves to `lg` below `sm` unless set, and menu rows are ≥ 44 px
  below `sm` (X-0010B-7, N27 M4, A); X-0010B-5 Trusted Types policy **dropped** from Part B (0024
  creates it). Budgets per X-0010B-9.
- **Part C.** C.1 row activation with `TmGridRowActivation<T>`; add "a readonly grid leaves
  unmodified printable keys unhandled" (X-0010C-11, A) and `rowActivate` is also `tm-tiles`'s payload
  type (X-0010C-13, M); C.2 `activeRowId`, driver in `grid` (X-0010C-10, M: type-only import for
  tiles); C.3 sort (A); C.4 header slot, no underline (X-0010C-9, R); C.5 select-all (A); C.6
  `rowClass`, `rowHeaders` (A); C.7 gutter (A); C.8 `scrollToRow(id, { flash?, activate?, align?,
  column? })` — `column` added, `focus` rejected (X-0010C-4, M); C.9 tree `expandedIds`, batched
  `loadChildren`, "siblings keep data order" (X-0010C-17, X-0010C-18, A); C.10 menu: items, links
  and submenus accept `icon: TemplateRef<void> | string` (registry name) and `shortcut: TmKeys`,
  `--menu-icon-size` (X-0010C-14, M); `layout: 'segmented'` and `cssClass` rejected; **new C.12**
  `TM_GRID_KEYS: readonly { keys: TmKeys; labelKey: string }[]` (one export; X-0010C-12, M); C.11
  budgets as ceilings only. `footerRows` and `rowSelectable` rejected (X-0010C-5, X-0010C-16).
- **Part D.** D.1 public `overlay` (A); D.2 `placement: 'end'`, no `direction` option, the `sm`
  query, plus `role` and `describedBy` (X-0010D-10, X-0010D-11, A); D.3 popover `size` (A); D.4
  formats: `tmFormatRelativeTime` without the `age` form, `TM_TIME_ZONE` (X-0010D-2, M); **new D.7**
  `month`, `quarter`, `year` in an explicit calendar (X-0010D-3, A) and **D.8** `'weekday'` style
  (X-0010D-4, M: `tmZonedParts` rejected); D.5 `clearAll({ keep })` (A); D.6 fetcher `signal` (A);
  **new D.9** `tm-image placeholderOnError` (X-0010D-8, A); **new D.10** `.tm-button` sizes `tm-icon`
  children (X-0010D-6, A); `tm-tab-group prerender` rejected (X-0010D-9).
- **Part E.** E.1 two-line options (A); E.2 `tm-entity-multi-picker` with the scrolling chip row
  (X-0010E-3, A; 0023 A6 rejected); **new E.3** `TmEntityPickerPage.context` (X-0010E-4, A); **new
  E.4** grid `entity` column `itemDescription` (X-0010E-5, A).
- **Part F.** 0010-F's text throughout (R-0033-R7, R-0010F-1, C-0010F-1, C-0010F-2, M-75: A for
  F; R-0029-10's `ComparedWith` half is F.16). Cut F.11 and renumber F.12–F.16
  to F.11–F.15 (Q-7, A). F.14 (`'uq'`) keeps the row-grain `TOP (1)` seek and `QX3104`; prologue
  variables are gone (Q-22, A). `EvaluateConstant` keeps its name (C-0010F-4). Cache-key table →
  one rule; one example each for F.4 and F.8; flags 11 → 8 (cut list, A).

### 2.2 Spec 0023 — Component Library Additions

- Adopt C-0023-1 (`chip`, `chip-set` only), C-0023-2 (`copy` entry point), C-0023-3 (component is
  `tm-banner-bar`), C-0023-4 (touch rule lives in Part B), C-0023-6 (`TM_TIME_ZONE` from Part D,
  filled by 0024).
- Drop A6 (chips in the multi-selects), A7's `TmItemActivation` (use `TmGridRowActivation`), A3's
  function shape (use D.4's `tmFormatRelativeTime`; `TmRelativeTime` input is `style`, "Just now"
  is Intl's output; days up to 7). A1, A2, A4, A5, A8 are accepted amendments (Part 3).
- Depend on `@tellma/core-ui/overlay`, never `private` (M-14).
- Add: `tmKeysMatch` matches letters and digits by `event.code` when `event.key` is non-ASCII or Alt
  is held on Apple platforms (R-0026-23, R-0027-23(1), A); chip tokens are public (A); `tm-tiles`
  implements `TmGridDriverTarget` (type-only import) and exposes `scrollOffset()`/`scrollTo()`
  (R-0030-R1, R-0030-R2, A); `tm-pager disabled` with geometry kept and item mode with a capped
  total (R-0030-R3, R-0031-R3, A); `TmBusyOverlay.run` label as a signal (R-0031-R1, A);
  `tm-page-header` keeps an empty slot's width (R-0031-R2, A); `tm-skeleton shape="table"`
  (R-0034-R5, M: pivot and chart compose `block`); `[tmCardSubtitle]` (R-0035-R7, A);
  `TmErrorDialogOptions { title, message, items?, reference?, details?: { labelKey, value }[],
  actions?: TmErrorDialogAction[], retry? }` (M-18, M).
- Reject: `tm-banner-bar announce` (the host announces; R-0028-23), `tm-signature-pad`
  (R-0032-R13), `tm-chips` free entry (R-0032-R14), `TmSearchField.error`, `accordion` (Q-17), an
  inline banner variant (0005's `tm-alert` serves dialogs; M-67).
- Hardening: `TmIconRegistry.register` refuses attribute names starting with `on`, `href`,
  `xlink:href` (Q-35). Icons are built with `createElementNS`; no Trusted Types policy here (M-17).
- Cuts (Q-17): §9 keeps group names only; §10 keys only; §11 no story list; one keyboard table.
- Outline: §7 loses `accordion`; otherwise as `outlines.md`.

### 2.3 Spec 0024 — Client App Foundation

- Adopt the import ruling (§0.1) in §1 and §9.2: route loaders `import()` the lazy entry points;
  `build` injects `modulepreload` for `/shell` and `/attention` (R-0027-24(1)); `provideTellma`
  provides `TM_APP_DECLARATIONS`, `TM_TIME_ZONE`, `TM_NOW`, `TM_ACTIVE_LOCALE`, `TM_CALENDAR`,
  `TM_BLOB_FETCHER` and calls `provideTellmaToasts({ maxVisible: 3, overflow })`, where the overflow
  pill navigates to `/{t}/notifications` (C-0028-8, M-29). No `TM_REALTIME`, `TM_ENTITY_PAGES`,
  `TM_ENTITY_DECLARATIONS`.
- §2.2 `TmFeatureDefinition`: `overrides` and `hide` with kind-qualified keys, member-wise for
  sections and entries too (C-0024-4, Q-28); `icons`, `widgets`; entries and `tmPage` take `order`,
  `icon`, `synonymsKey`; `tmPage` paths may carry `:param` (M-24, M-27). Config problem codes
  centralised here: add `app-bar-reserved-id`, `unknown-path`, `unknown-action`,
  `operation-missing`, `entity-not-routable`, `unknown-setting-category`, `coded-report-source`,
  `widget-missing`, `widget-not-registered`, warning `label-missing` (M-26, A).
- §3 metadata: `distribution.copyright`, `settingCategories`, `jobHandlers`, `widgetTypes` (M-28);
  `contracts` stays a map and `TmReadOptions.long` picks the watchdog (C-0024-2); `displayName` and
  `copyright` read from `appsettings.json` at emit (C-0024-7). The config check runs as a Node step
  over the emitted chunks and compiled declarations, not through a browser (Q-16, A). Hidden source
  maps archived per UI version (R-0026-24(7)).
- §4 boot: one boot page "Tellma couldn't start" with Try again; keep the `tm.login` loop guard
  (Q-16); the scaffolded `index.html` carries the `tm-boot.js` tag, the build generates the file,
  `check` verifies the tag (C-0024-5, A); `index.html` carries `<meta name="color-scheme" content="light dark">`; `tm-boot.js` deletes
  by the frozen key layout with the `oldVersion = 0` abort (R-0025-24(1)); the route host awaits
  `TmEntities.load()` (from the cache) before the body (R-0029-24(2)); `TmRouteData.skeleton` ∈
  `search | details | report | dashboard | home | blank` (M-25); the route host loads `TmSearchHost`
  and `TmDetailsHost`, which host the hatches (M-30); the `''` route loads 0035's `TmHomeHost`; the
  route host provides `provideTmPageScope()` and registers `custom` with `status` (R-0026-24(5)).
  Delete the `[dir]` wrapper: `<html dir>` is the live-direction host (M-16). Drop the Cookie Store
  listener (Q-16). Router option `canceledNavigationResolution: 'computed'` (R-0031-R4).
- §5 PWA: the service worker registers through a `TrustedScriptURL` from a `tellma` policy that
  0024 creates (`createScriptURL` only; C-0024-1, M); `TmUpdates` exposes the controlling worker's
  `version` (M-33); update `<meta name="theme-color">` from `TmTheme.effective`; the `on-demand` group
  is the ECharts engine chunk, `/report-studio` and `/support` (C-0034-5).
- §6 hosting: metadata chunks require the session cookie; the `/signed-out` page renders from
  bundle strings and `index.html`'s title and skips the metadata load (Q-31, M). Record it as a
  review flag.
- §7 localization: `TmLocalization.timeZone`, `load(code)`, `languages()`; `TmCalendarDefinition.labelKey`
  (R-0027-24(6), (7)); `TmServerText` gains `message(key, args)` and `has(key)` (M-32).
- §8 session: `drafts.reply` carries `busy`; sign-out waits for busy tabs up to the watchdog and
  the confirm names them (Q-30); `TmTelemetry.flush()` before `/bff/logout` (R-0026-24(6)); fold
  0025 E16 into E8(d) (R-0025-24(5)); state that `sh` is accepted by no endpoint as a credential
  (Q-35). The expiry sweep runs before `clearAll({ keep: tm-app, tm.boot })`, then `boot.patch`
  (X-0010D-5).
- §9 URLs: `TmSearchQuery` per §0.2; `TmUrls.blob`; `describe` kinds `report` and `list`; My profile
  and settings are `urls.page('me')`, `urls.page('settings/general')` — no `me()`/`settings()`
  helpers (M-31, M); `/new` is pinnable (Q-5).
- §10 HTTP: `upload` over `XMLHttpRequest` with progress (Q-24); `send(route, body, { background })`
  (M-20); `TmRequestLog.begin`/`end` (M-19); lossless JSON falls back to a regex pre-pass where
  `JSON.rawJSON` is absent; the feature gate keeps `navigator.locks`, `BroadcastChannel`, `indexedDB`
  only (Q-36, A). `TmErrorHandler` is the `ErrorHandler` (R-0026-24(3)).
- §11 errors: the 5xx dialog's Details adds "Copy diagnostics" and "Report this problem"
  (R-0026-24(4)); `TmErrorPresenter` calls `TmTelemetry.error`; `tenant-not-found` calls
  `sweep({ tenant })` (R-0025-24(3)); the shared state copy lives here and page specs cite it
  (Q-20).
- E21 Acme: add a `[Cacheable] acme.Currency`, the key `acme.bankReconciliation.account`,
  `[DisplayNameSource]` on `acme.SalesInvoice`, the `ImageId` blob and `CustomerType` enum
  (R-0025-24(4), R-0029-24(6)); drop the deferred `history` extra (Q-12).
- `ITellmaLinks` is defined by 0011 §5.10 and cited here (G29).
- Cuts (cut list): §2.6 commands one row each; §12 cites A§11; §5.3 group assignment two sentences;
  §8.4–§8.7 keep the diagram, cut the prose steps.
- Outline unchanged.

### 2.4 Spec 0025 — Client State and Cross-Tab Sync

- Adopt C-0025-1 (user-addressed events carry the tag; E1 stands), C-0025-2 modified (`prefs.changed
  { tenantId, values }`, no `at`), C-0025-3 (`leaderVisible`, `disabled`), C-0025-4 (queued lock),
  C-0025-5, C-0025-6, C-0025-7 (recents by tenant-relative path), C-0025-9 (`realtime`), C-0025-10,
  C-0025-11 (`entitySet()`), C-0025-12 (`replace`, `markStale`, `isStale`, `forget`).
- `TmLeader` loads `/realtime` through `import()` itself; delete `TM_REALTIME` and §8.1's token
  (C-0025-8 modified, Q-1).
- `clearAll` is BE's: `sweep('session-ended')` sweeps `tm-app` itself, then calls `clearAll({ keep })`
  keeping `tm-app` and `tm.boot`; `boot.patch` clears the user fields (M-15).
- Q-13 (A): own writes adopt through `TmCacheRefresher.adopt` before `observe` sees the header (no
  superseded set, no settle window); plain JSON for both preference kinds, a `TooManyPreferenceKeys`
  refusal drops the write with telemetry; `savedAt` written on `put` only and the idle rule reads
  the sync time (no daily re-put, no 30-day sweep); degraded polling while visible or on a probe
  (no `visible` lock); last `prefs.changed` wins (no `at`, no 60 s expiry). The idle window is
  `DistributionInfo.session.idleDays` (Q-34); record the shared-device trade-off as a review flag.
- Appendix A: `search.<entity>.columns = TmColumnPreference[]`, `.view = { view?, list? }`,
  `.pageSize` ∈ 20, 50, 100, 200 (M-6); `nav.pinned = TmPinsDoc`, fallback `{ v: 1, items: [] }`
  (M-61); add `notifications.toasts`, `dashboard.<id>.params`, `report.<id|key>.params =
  { v: 1, args }` (M-62, R-0034-R6(a)); no `report.studioPanel`; no `home` launch document
  (R-0035-R2(b), R).
- `TmDrafts.load` parses with the lossless parser (R-0031-R9); `markStale` covers details entries
  (R-0031-R10); a non-leader tab may `put` a leader document it read (R-0028-25(2)); `tabId` per
  document load (R-0026-25).
- State-dump contribution through 0026's `TmSupport.registerDumpSection` (M-53).
- Q-39: name the probe path as the recovery on engines without Page Lifecycle; add a WebKit case to
  the multi-tab e2e.
- Cuts: §6.1 two sentences; §13 "title as displayed"; §2.7 "drop the write, telemetry"; §8.2 prose.
- Outline unchanged.

### 2.5 Spec 0026 — Client Observability, Support and Page Context

- Adopt C-0026-1, C-0026-2, C-0026-3 (`begin`/`end`, `pending`), C-0026-5 (upload, via 0024),
  C-0026-6 (page scopes), C-0026-7 completions (less `repeats`), C-0026-8.
- C-0026-4 modified: one blob kind `support-bundle` holding a JSON document that embeds the
  screenshot as a data URL; no `core.SupportRequestFiles`.
- Q-8 (A): cut INP and layout shift (keep `chromeMs`, `dataMs`); fingerprint = `errorClass + first
  frame`, cap 30 error items per 10 min; sampling = one boolean per tab from `sampleRate`; two
  counters (`errors`, `requests.slow`); one truncation rule (drop `visible.rows`, then `telemetry`);
  two admin routes without a paging record; capture = "Chromium: tab; others: picker; none: attach".
- `TmSupport.registerDumpSection(name, () => TmJson, destroyRef)` is the contributor seam for 0025
  and 0028 (M-53, A).
- §9.3 state minimums: `search` adds `list`; `details` adds `dirty`, `child`; `settings` is
  `{ category, mode, dirty }` and 0028's notification settings page publishes the same kind with
  `category: 'notifications'`; `dashboard` and `home` as 0035 completes them (M-52). Action-id
  prefixes add `attention.*`, `notifications.*`, `admin.*`, `home.*` (M-51).
- Dump and `SupportRequest.PageUrl` carry the path only unless page data is included (Q-32); the
  screenshot preview warns that the picker may capture other windows (Q-35); iOS uses "Attach image"
  (Q-38).
- The support dialog's inline notice is 0005's `tm-alert` (M-67). Examples use `tmEntity('core.SupportRequest', …)`
  with no type argument (M-23).
- Cuts: §9.7 AG-UI → five lines; decisions 18 → 10; §12.2 states only.
- Outline unchanged (§5 shrinks).

### 2.6 Spec 0027 — Shell, Launcher and Tenant Picker

- Adopt C-0027-1 (modulepreload; the ruling is 0024's), C-0027-2 (badge factory), C-0027-3
  (search-only entries), C-0027-4, C-0027-5 (record the slot order with the reason). C-0027-6 is
  superseded by member-wise `overrides` for sections and entries (Q-28). C-0027-7 rejected: pins
  overflow into More from the `xl` rule.
- Launcher members are flat on every declaration: `section`, `order`, `icon`, `synonymsKey`;
  rename `navOrder` → `order`; the gate is `TmPermissions.can(name, 'Read')` and the `query`
  operation (M-3). The normalizer is `tmSearchNormalize` (M-4).
- No `TM_REALTIME` and no `TM_APP_DECLARATIONS` of its own: read `TM_APP_DECLARATIONS` from 0024
  (M-29). No `tm-banner-host` `visible`: read `TmBanners.visible` (M-58).
- Page shortcuts: `F2`, `Mod+S`, `Mod+Enter`, `Alt+PageUp`, `Alt+PageDown` are admitted; a `page`
  shortcut fires only when the event is not `defaultPrevented`, so a focused grid wins (M-59, M);
  state that unmodified printable keys belong to the page (R-0030-R13).
- Jobs, Exports and Imports stay under `administration` for every member (C-0028-9; review flag).
- Q-9 (A): keep pin + popover and the Manage pins dialog; cut the context menu, the launcher-to-bar
  drag and the measured overflow; ranking = prefix match, then contains, over the normalized label
  and synonyms (active language and English), ordered by section then label; providers keep the
  interface and the "append below Pages" rule only.
- `<mark>` wrapping uses DOM nodes (Q-35). Menu rows ≥ 44 px below `sm` come from Part B;
  `layout: 'segmented'` and `cssClass` are rejected: the language, calendar and theme toggles are
  radio items.
- Cuts: §4.3 key lists → an appendix table; §7.1 collapse order only; §12.1 cite 0018; deviations
  table → decisions; §6.2 picker → a table.
- Outline unchanged.

### 2.7 Spec 0028 — Assignments and Attention Surfaces

- Adopt C-0028-1 (assigners notified), C-0028-2 (`TmBannerOptions`, "level"), C-0028-4 (`all`),
  C-0028-5 (copy; 0028 owns banner copy and ids `tm.offline`, `tm.update`), C-0028-6 (hidden page
  applies at once; the host announces), C-0028-7 (`TmBanners.visible`).
- C-0028-3 modified: Jobs tabs are `tab=mine|all` through 0030's `tabs` member; no `param`; the
  bell's "My jobs" links `/{t}/jobs?tab=mine` (M-7). C-0028-8 modified: the overflow pill navigates
  to the Notifications page.
- Toast policy (§0.6): admit outcomes with no inline home — "{Entity} deleted." from a details
  page, a bulk action's count ("3 items deactivated."), "Started in the background." with Track
  after a 202, "Report saved." when the studio navigates away, "Sharing updated." when the dialog
  closes, and 0030's sticky "Exporting…" with Cancel (M-55). "Track" opens the job's details page
  through `TmUrls.target('core.Job', jobId)` (M-56). `core.report.shared` and
  `core.dashboard.shared` have `Toast = false` (M-57).
- Q-14 (A): replace §8.2–§8.3 with the three-sentence arrival rule plus "a replaced row toasts
  again"; cut read-on-visit (§6.5), `jobs/status`, `TmJobsAlerts`, E9 and the "Connection restored."
  message.
- Q-37: new rows insert at the top only while the panel is scrolled to its top; otherwise a pinned
  "{n} new" header; delete the Baseline claim.
- `TmAttention.refresh('assignments')`; `tm-assignment-list` takes a `loading` input and fills its
  container (R-0035-R6, A). Examples use `tmEntity('core.Notification', …)` (M-23). Notification
  settings page publishes kind `settings` (M-52). Dump section through `TmSupport.registerDumpSection`.
- Pages are declarations on 0030's `tabs`, `rowLink` and `pageActions`; bulk actions derive from
  entity actions; no `onActivate`, `bulkActions`, `refreshRows` or Refresh dot (Q-26).
- Cuts: §10.3 → a table; §10.5 cite the declaration; decisions 21 → 12; §4.8 three counters.
- Outline: add §12 Testing and §13 Definition of done.

### 2.8 Spec 0029 — Client Entity Model

- Adopt C-0029-1 (no type argument; `TmEntityShapes` augmentation), C-0029-2 and C-0029-3
  (`summary`/`bySegment` synchronous in the primary, `get` after `load()`), C-0029-4, C-0029-6
  (`profile: 'permission'`), C-0029-7 (resolved by Q-31; keep the flag pointing at 0024).
- C-0029-5 modified: no `TM_ENTITY_PAGES`; the binding imports `/search` and `/details` pages
  dynamically (Q-1); `TmEntityPickerPage.context` stands (Part E).
- Final `tmEntity` shape (Q-40), one line per member naming the meaning's owner: `section`,
  `order`, `icon`, `synonymsKey` (0027); `columns` as `path | { path, display?, width?, select?,
  filter? } | { expression, labelKey, type? }` (0030; M-9, M-39: `select` names extra paths a
  display reads; `filter` restricts the picker in editable cells); `orderBy`, `tile`, `filters`,
  `tabs`, `rowLink`, `pageActions`, `search`, `searchRead` (0030); `details: TmLazyComponent |
  { page, create? }` (0031; M-35); `defaults`, `clone: (copy, source) => copy`, `validate`,
  `beforeSave`, `actions: [{ name, dialog?, icon?, button?, visible? }]` (0031; M-34, M-36);
  `picker`, `helpKey` (0029). Rows are the wire's camelCase objects; paths are PascalCase.
- `TmEntitySummary` carries `icon` from the declaration (M-48); `TmEntityMetadata.extras` stays
  `string[]` (Q-12; R-0031-R14 rejected); `treeLevels: number` and `Level{j}` resolved as a
  self-navigation (R-0010F-2, M-47: 0010-F's model, no `kind: 'level'`); `reachable` applies the
  owner-path rule for child roots (R-0033-R1(3)).
- Add: `tmQx.raw(text)`, clause `'constant'`, `TmEntities.enum(name)`, `tmDescribeFilter(column,
  spec)`, the create label chain `<Schema>_<Entity>_New` → `Entity_New` (M-46, R-0030-R17,
  R-0030-R20, A); `tm-value-editor` accepts `TmValueDescriptor` for entity-less values (R-0032-R10,
  A); a `pending` field state during upload (R-0031-R18, A); `TmEntityActions` and the generated
  dialog in `/entity` (Q-3, R-0031-R16); the picker opens the details page at `size: 'lg'` through a
  dynamic import (R-0031-R17); `entitySet()` settling with no list falls back to the server search
  (Q-29). Action metadata carries `entity` per argument from `[EntityRef]` (Q-3).
- Reject: `TmExpressionEditor.select(start, length)`, `{ expression, label }`, `rememberRows`
  (use `remember`), `TmDisplayNames` cache rule changes, `terms(clause)` (R-0034-R4(a)).
- Blob URLs are `TmUrls.blob` (0024); `TmBlobs.url` does not exist (M-21). Examples use `Image`
  (M-10).
- Cuts (Q-18): cite 0008 §4 for the lexer; drop mechanics in one sentence; path search = filter the
  visible tree by label; fold rare types; §14.4 cites the import ruling; §16 policy sketch only;
  decisions 17 → 12, flags 14 → 10.
- Outline unchanged.

### 2.9 Spec 0030 — Search Page

- Adopt C-0030-1 modified (`view` + `list` in 0024's codec), C-0030-2 modified (`.view =
  { view?, list? }`, no width bands; `.columns = TmColumnPreference[]`), C-0030-3, C-0030-5,
  C-0030-6, C-0030-7, C-0030-8, C-0030-9 modified (declaration types live with `tmEntity` in the
  primary; `TmSearchSnapshot` is exported type-only from the primary).
- C-0030-4 rejected: Jobs uses `tab=mine` (Q-2).
- Members (Q-26): `tabs: [{ key, labelKey, queryex? }]` with URL `tab=`, `rowLink(row)`,
  `pageActions`, `searchRead(request, query) → { route, body }` (platform-internal; 0034 is the
  consumer); `injectTmSearch().refresh()`; no `refreshRows`.
- The drill-through list is `TmSearchHost` with a fixed read-only filter leaf, `cols` from the URL
  and a derived heading; 0034's own grid page is gone (R-0030-R23, A).
- `TmSearchSnapshot.supersedes` and the adoption rule; past an edge the details page reads
  `request.route` with the full body and new `skip`/`take`, then writes the page back (R-0031-R19,
  R-0031-R20, A). Export `TmImportResultView` (R-0032-R12, A). Bulk actions with arguments open
  `TmEntityActions`' dialog (R-0031-R21).
- Reject R-0034-R3(b) (options without `queryex`, `hidden`): the reports page uses `tabs`.
- Toasts per §0.6; "Track" opens the job's details page.
- Q-19 (A): expression columns apply only when the URL carries no `cols`; drop dead-key handling;
  export = synchronous, `export/start` on 413; the custom-filter `component` hatch waits for a
  consumer. Q-20: state table keeps page-specific rows and cites 0024 §11; drop §18.
- Cuts: §13.3 cite 0019's shapes; §3.2 four rows; §7.1 and §9.1 one example.
- Outline: §18 removed; §18 Testing, §19 Definition of done.

### 2.10 Spec 0031 — Details Page

- Adopt C-0031-1 (`tm-details-tab collection`), C-0031-2 (`cancel(): Promise<boolean>` and the
  members), C-0031-4 (`TmEntityActions`, `injectTmActionDialog` in `/entity`), C-0031-5, C-0031-6
  (defer the grid inside `tm-child-grid`), C-0031-7, C-0031-8.
- C-0031-3 rejected with Q-12: all extras are eager; no `extras` operation, no `DeferredExtras`,
  no E1–E3; `*tmDetailsExtra` stays. An error in an unrendered tab is listed as text; activating it
  opens the tab, then focuses; no `prerender`. The deletion dialog offers Discard only.
- Q-25 (A): `TmDetailsSource<T>` with `tmEntitySource(descriptor, id)` as the default; `actions[].visible`;
  `injectTmActionDialog().run(args, { form?, keepOpen?, ids? })` resolving to the outcome and
  `close()`; a zero-argument action with a declared `dialog` opens it (M-41); `tm-details-tabs`
  `selected` model (M-42); `registerField(path)`/`registerCollection(name)` on the context (no
  `/private` registry).
- Q-3 (A): §10.4 the default dialog is generated from `arguments` (one `tm-value-editor` per schema
  property, `entity` hints, `required`, 422 by path); `dialog` is the hatch.
- Q-27 (A): `details: { page, create? }`; a hatch may register its own kind; `TmDetailsContext.actions`
  (R-0035-R8(a)); `can` reads the eager `permissions` extra when the stack declares it (M-43,
  R-0035-R8(b)); `clone(copy, source)` with camelCase rows (M-34).
- Reusable `tmLeaveGuard(dirty)` and the concurrency dialog as content for the studio (R-0034-R8(a),
  A); an embedded report in the context is rejected (R-0034-R8(b)).
- Q-4: the save-error summary and the "restored" notice render in the reserved strip under the
  toolbar. Q-20: cite 0024 §11 for shared states; drop §17.
- Toast "{Entity} deleted." after a delete that leaves the page (§0.6). Upload cites 0024 §10.
- Cuts: §6.3 cite 0024; §6.4 to code; §2 minimal example; decisions 20 → 14.
- Outline: §6.8 "Deferred extras" removed (keep `*tmDetailsExtra` in §6); §17 removed; §17 Testing,
  §18 Definition of done.

### 2.11 Spec 0032 — Stock Pages

- Adopt C-0032-1 (per-entry stamps), C-0032-2 (filtered, capped `members`; eager), C-0032-3,
  C-0032-4 (`@tellma/module-gl/centers`), C-0032-5 (import ruling), C-0032-6 (via 0031's source;
  §12 removed), C-0032-7 (`settingCategories`; flat launcher members).
- Q-15 (A): signature upload only (no pad); "Check delivery" button; drift as an eager details
  extra `problems` on the Role stack (no `roles/problems` route; X-0014-14, X-0018-10 modified).
- Q-25: the Settings and My profile pages are 0031 details pages over `TmDetailsSource`; R1–R8
  are answered by 0031. `TmUrls.page('me')`, `page('settings/<category>')` (M-31). Chips are
  `tm-chip`/`tm-chip-set`; `tm-alert` is 0005's (M-66). `tm-chips` free entry rejected.
- Column `{ path, filter }` is admitted as `filter?` on the path-column form (M-39, A).
- `MeResult.SecurablesFingerprint` removal cascades (G8). `roles/problems` and `jobs/status` leave
  every route list.
- Settings save: `TmCacheRefresher.adopt(tenantId, { settings })` on the result (R-0032-R9).
- Cuts: §4.3 three rows; §4.6 one paragraph; §9 layout and the built-in rule; §13.1 cite 0013 §6.3;
  decisions 25 → 15.
- Outline: §12 "The source seam" and §14 removed → §12 Server and metadata changes, §13 Testing,
  §14 Definition of done.

### 2.12 Spec 0033 — Report Definitions and Engine

- Adopt C-0033-1 (level navigations, 0010-F's model), C-0033-2 (F.13, F.15 listed; F.11 cut),
  C-0033-3 (stack omits `Export`/`Import`; reserved rule narrows), C-0033-4 (`reports/browse`),
  C-0033-5 (`route`, defaults in C#), C-0033-6 (owner-path refinement), C-0033-7 modified (Q-21).
- Part F citations become §F.8 … §F.15 as 0010-F numbers them after the F.11 cut; `QX4009`;
  optional `KeyGroups`; 4,096 sets; polarity folding with list-element drops;
  `IReadOnlyCollection<string>`; caller errors for `Exists` (R-0010F-1, M-75).
- Q-7 (A): §4.3 defaults run as one constant statement first (one Read batch, two round trips only
  when defaults apply), bound as ordinary parameters and echoed; no parameter expressions.
- Q-21 (A): the resolver answers members with reasons and the highest access; `RootRead` is a
  per-grant count from the grant's role; no `EvaluateOutcomesAsync` (E2 dropped); dashboards
  contribute a reason by id. Q-33: members are listed only to a caller with `core.Role × Read`.
- Ids are `int` everywhere (M-70). `ReportRunRequest` and `ReportExportRequest` take `Id` or `Draft`
  only; no `Code` (M-71, R). `ReportResult` gains `Id`, `Root`, `Chart`; `ReportColumn.Expand`
  (M-73, A). One `permissions` extra (`SharePermissions(Save, Delete, Activate, Share, Publish,
  Transfer)`) on `reports/get` and `dashboards/get` (M-79, A). Coded-report argument members are
  named after the parameter keys (`Customer`), and `IReportResults.Require(value, key)` exists
  (M-72, A). Month, quarter and year terms take an optional explicit `calendar`, default the
  viewer's (M-76, A). One parameter type vocabulary, 0033's, shared with `tmCodedReport` (M-74).
  Report export jobs set `Label` `Core_Job_ExportReport { title }` (G28). State that an inactive
  definition still runs and that `DashboardsUsing` titles use the content slot (R-0035-R3(e)).
  `core.report.shared` has `Toast = false` (M-57). Child query surfaces inherit the owner's
  `McpExposure` and the audit lists them (Q-35).
- Reject: seeds `acme.monthly-sales`, `acme.invoice-lines` (one seed, `acme.sales-by-center`);
  `via` counter tags; the multi-securable overload.
- Cuts: §3.5 two examples; §5.3 cite 0010-F; §9.3 walk-through table only; §12.3–§12.4 "fully
  expanded compact form, four styles" + cite 0019; §8.10 to code; decisions 24 → 16, flags 16 → 12.
- Outline unchanged. Target ≤ 1,200 lines.

### 2.13 Spec 0034 — Report Renderers and Studio

- Adopt C-0034-1, C-0034-2 modified (`tab=` through `tabs` and `searchRead`; no `x.tab`, no
  hidden filter), C-0034-3, C-0034-4, C-0034-5, C-0034-7.
- C-0034-6 rejected: the drill filter travels in the URL; a filter over the limit is refused with a
  notice.
- The drill-through list is `TmSearchHost` with a fixed leaf (R-0030-R23); `TmDrillListPage` with
  its own grid is gone; `rowSelectable` and `footerRows` requests are withdrawn (reports-page rows
  that cannot be selected are filtered from selection by the page; a totals row uses `rowClass`).
- `TmReportRef = { id } | { key }` (no `code`); `details: { page, create }` per 0031's shape; the
  studio is a `tmPage` with a `:id` segment publishing `studio` (M-24, M-35).
- `TmReportHost` widget surface: `mode="widget"`, `[dashboardId]`, `[parameters]`, `refresh`,
  `[held]` (initial result), `status`, `executedAt`, `run({ clear })`, `export()`; a `silent` run is a
  background request; `forbidden`/`not-found` clear the held output; export `tm-report-parameter-field`
  and a codec over a parameter list (R-0035-R4, M: no `[kind]`, no dashboard-declaration codec).
  `tmShareDialog(segment)` is declared and exported; the dialog reads 0033's `ShareAudience` for both
  segments (M-36, M-78).
- "Updated 10:42" is absolute (`tmFormatTime`); `tm-relative-time` is not used for it (M-69).
- Q-10 (A): chart types `column`, `bar`, `line`, `area`, `pie`, `doughnut`, `card`; ECharts'
  `richText` tooltip and legend; "Show as table" is the keyboard path; cut per-point navigation and
  the hidden table; keep `TmChartSpec` and its validator.
- Q-6: one line in §3.6 names the future search-page pivot view as `TmReportHost` in card mode fed a
  draft. Q-20: drop §14.
- Cuts: §12.6 one sentence; §8.2 fallback; §4.5 remembered parameters stay only as the
  `report.<id|key>.params` key; `report.studioPanel`; §6.2–§6.4 compact-form rule and four styles;
  §7.3 to code; decisions 29 → 18, flags 18 → 12.
- Outline: §14 removed; §14 Testing, §15 Definition of done.

### 2.14 Spec 0035 — Dashboards and Home Page

- Adopt C-0035-1, C-0035-2 (the hatch registers `dashboard`; the host's `actions` carry over),
  C-0035-3, C-0035-4, C-0035-5, C-0035-6.
- Q-11 (A): cut dashboard seeds (`DashboardSeed`, `core.dashboard-seeds`, X-0011-2's item,
  X-0011-17's step, X-0018-1's seed); cut §7.1 widget audience warnings and `DashboardAudience`;
  scheduler → "a due tile runs when the document is visible and online; runs are capped by the
  `tellma-reports` policy; a forced refresh clears"; pointer drag stays with "push down, then
  compact" in one sentence; `tmFirstName` keeps the Arabic `عبد/أبو` rule.
- Ids are `int`; `IDashboardReadThrough` matches 0033 (M-70). `TmFeatureDefinition.widgets` and
  core metadata `widgetTypes` stand (R-0035-R5). `clone(copy, source)` writes camelCase members
  (M-34). `core.dashboard.shared` `Toast = false`.
- `TmAttention.refresh('assignments')` and the list's `loading` input (R-0035-R6). "Updated 10:42"
  absolute (M-69). No `home` launch document; the home page renders from the `assignments` document
  and `TmRecents` (R-0035-R2(b)).
- Q-20: drop §15.5.
- Cuts: §8.2 algorithm to code; §13.4 a paragraph; §9.2 cite 0028; decisions 24 → 15.
- Outline: §15.5 removed; add §17 Testing and §18 Definition of done.

### 2.15 Mismatch resolutions (Part C, by ID)

| ID | Resolution |
|---|---|
| M-1 | `TmBannerOptions`, "level" (0028); skeleton amended |
| M-2 | "A new version is available."; ids `tm.offline`, `tm.update`; 0028 owns copy, 0024 cites |
| M-3 | flat `section`, `order`, `icon`, `synonymsKey` on every declaration; gate `can(name, 'Read')` + `query` |
| M-4 | `tmSearchNormalize` |
| M-5 | `view` + `list` (0024 codec) |
| M-6 | `TmColumnPreference[]`; `{ view?, list? }` |
| M-7 | `/{t}/jobs?tab=mine` |
| M-8 | `tab=` everywhere; `reports/browse { scope, query }` through `searchRead` |
| M-9 | `{ expression, labelKey, type? }`; `select?` on the path form |
| M-10 | `Image` |
| M-11 | `TmGridRowActivation<T>` for grid and tiles |
| M-12 | BE's rendering; `tm-multi-select` is the name |
| M-13 | D.4's function and option names; `style` ∈ long, short, narrow; no `age` |
| M-14 | `overlay` is public (D.1); 0023 depends on it |
| M-15 | `clearAll({ keep })`; 0025 sweeps `tm-app`; 0024 calls it after the sweep |
| M-16 | root `Directionality` on `<html dir>`; no `[dir]` wrapper |
| M-17 | the `tellma` policy lives in 0024 (`createScriptURL`); icons need none |
| M-18 | 0023 component with `details` and `actions`; 0024 supplies copy; no count badge |
| M-19 | `begin`/`end` |
| M-20 | `send(route, body, { background })` |
| M-21 | `TmApi.upload` and `TmUrls.blob` in 0024 |
| M-22 | summaries synchronous; `get` after `load()`; the route host awaits `load()` |
| M-23 | no type argument |
| M-24 | `tmPage` takes `order`, `icon`, `synonymsKey`; `:param` segments; the page's own context replaces `custom` |
| M-25 | `search \| details \| report \| dashboard \| home \| blank` |
| M-26 | all codes in 0024 §2.2 |
| M-27 | `icons`, `widgets`, entry members, badge factory |
| M-28 | `copyright`, `settingCategories`, `jobHandlers`, `widgetTypes` |
| M-29 | `TM_TIME_ZONE`, `TM_NOW`, `TM_APP_DECLARATIONS`, `provideTellmaToasts` from 0024; no `TM_REALTIME`, `TM_ENTITY_PAGES`, `TM_ENTITY_DECLARATIONS` |
| M-30 | the route host loads the hosts; hosts host hatches |
| M-31 | add `blob`; `describe` kinds `report`, `list`; `page('me')`, `page('settings/…')` |
| M-32 | `message(key, args)`, `has(key)` added |
| M-33 | `TmUpdates.version` |
| M-34 | `clone(copy, source)`; camelCase rows |
| M-35 | `TmLazyComponent \| { page, create? }` |
| M-36 | `visible?` on actions; `tmShareDialog(segment)` declared by 0034 |
| M-37 | `tabs`, `rowLink`, `pageActions`, `searchRead` are 0030 members; `refresh()` only; `TmSearchQuery.tab` |
| M-38 | no `hidden`; tabs replace it |
| M-39 | `filter?` on the path-column form |
| M-40 | `TmDetailsSource` in 0031 |
| M-41 | 0032's `run` options and `close()`; a zero-argument action may declare a dialog |
| M-42 | `selected` model |
| M-43 | `can` from the `permissions` extra when present; `actions` on the context |
| M-44 | `extras: string[]`; all eager |
| M-45 | `TmValueDescriptor` accepted |
| M-46 | `raw`, `'constant'`, `enum`, `tmDescribeFilter`, `remember` accepted; `terms`, `select`, `rememberRows` rejected |
| M-47 | 0010-F's model: `treeLevels`, `Level{j}` by name |
| M-48 | `summary.icon` from the declaration |
| M-49 | `supersedes`; full body read; write back |
| M-50 | `TmImportResultView` exported |
| M-51 | prefixes added |
| M-52 | minimums per Part 2.5 |
| M-53 | `registerDumpSection` |
| M-54 | a hatch may register its own kind; `actions` carry over |
| M-55 | the no-inline-home rule |
| M-56 | the job's details page |
| M-57 | `Toast = false` |
| M-58 | `TmBanners.visible` |
| M-59 | admitted page keys; `defaultPrevented` rule |
| M-60 | `refresh('assignments')`; `loading` |
| M-61 | `TmPinsDoc`, no tenant default |
| M-62 | rows added; `report.studioPanel` and `home` dropped |
| M-63 | registry-name icons and `TmKeys` shortcuts on items, links, submenus; `layout`, `cssClass` rejected |
| M-64 | `context` accepted |
| M-65 | `TM_GRID_KEYS` (merged) and `scrollToRow column` accepted; `focus`, `prerender`, `rowSelectable`, `footerRows`, `tmZonedParts` rejected; D.7 and `weekday` accepted; printable keys accepted; driver target stays in `grid` |
| M-66 | `tm-chip`/`tm-chip-set`; `tm-alert` is 0005's |
| M-67 | `tm-alert` |
| M-68 | per Part 2.2 |
| M-69 | absolute "Updated {time}" |
| M-70 | `int` |
| M-71 | no `Code` |
| M-72 | members = keys |
| M-73 | members added |
| M-74 | 0033's vocabulary |
| M-75 | 0010-F's text; F.11 cut |
| M-76 | optional explicit `calendar` |
| M-77 | per Part 2.13 |
| M-78 | `ShareAudience` for both; no wrapper |
| M-79 | one `permissions` extra |

### 2.16 Request coverage (Part B, by ID)

"Stands" = the target note already provides it and nothing changes. Otherwise the pointer names
the ruling. Sub-items not named follow the row's verdict.

| Target | Requests |
|---|---|
| 0010-BE | R-0023-10: A1, A2, A4, A5, A8 accepted (Parts 3.1 D.11/D.9/D.10/C.10/B.6); A3, A6, A7 rejected (BE's design). R-0029-10: `context` accepted (E.3), `itemDescription` accepted (E.4). R-0035-R1: `weekday` accepted, `tmZonedParts` rejected. Section-6 needs: 0024 policy dropped; 0025 `clearAll` → BE's; 0027 M3 and M5 accepted (merged `TM_GRID_KEYS`), M1, M2 rejected, M4 via B.6; 0030 printable keys and sibling order accepted, `contracts` move rejected, underline rejected; 0031 `column` accepted, `focus` and `prerender` rejected; 0034 C.12, C.13 rejected, D.7 accepted; 0035 as R-0035-R1 |
| 0010-F | R-0033-R7 stands (F's deviations win); R-0029-10 stands |
| 0023 | R-0010BE-1 stands; R-0024-23 accepted as M-18 (details and actions; no count badge); R-0026-23 accepted; R-0027-23 (1), (2) accepted; R-0028-23 rejected; R-0030-R1, R2, R3 accepted; R-0031-R1, R2, R3 accepted; R-0032-R13, R14 rejected; R-0034-R5 modified (`table` shape; chip overflow stands); R-0035-R7 accepted |
| 0024 | R-0010BE-2 accepted in full (meta `color-scheme`, `theme-color` update, `TM_TIME_ZONE`); R-0023-24 accepted (`icons`, `TM_NOW`, `TM_TIME_ZONE`, toasts, stylesheet, fake clock replaces `TM_NOW`); R-0025-24 (1)(3)(4)(5) accepted, (2) stands; R-0026-24 (1)–(9) accepted; R-0027-24 (1)(3)(4)(5)(6)(7)(8)(9)(10) accepted, (2) rejected; R-0028-24 (1)(2) accepted, (3)(4) stand; R-0029-24 (1)(2)(4)(5)(6)(7) accepted, (3) rejected; R-0030-R4 accepted; R5, R8 stand; R6 accepted; R7 rejected; R9, R10 accepted; R-0031-R4 accepted; R5, R8 stand; R6 rejected; R7 accepted; R-0032-R2 route data stands, helpers rejected, ruling given; R-0032-R3 accepted; R-0033-R5 stands; R-0034-R2 (a)(b)(c) accepted, (d) stands; R-0035-R5 (a)(b)(c) accepted, (d) stands, (e) accepted (0035's host picks) |
| 0025 | R-0010BE-3 `clearAll` → BE's, rest stands; R-0024-25 stands; R-0026-25 stands; R-0027-25 (1) accepted (0027's model), (2)(3) stand; R-0028-25 (1) accepted, (2)(3) stand as 0025 wrote them; R-0029-25 stands; R-0030-R11 accepted (`columns`, `view`); R-0031-R9, R10 accepted; R-0032-R9 accepted (`adopt`); R-0034-R6 (a) accepted, (b) rejected; R-0035-R2 (a) accepted, (b) rejected |
| 0026 | R-0024-26 request log → `begin`/`end` (0026's), rest stands; R-0023-26 stands; R-0025-26 seam accepted; R-0027-26 stands; R-0028-26 seam accepted; R-0029-26 stands; R-0030-R12 accepted; R-0031-R11 accepted; R-0032-R15 accepted; R-0034-R7 stands; R-0035-R9 accepted (owner completes) |
| 0027 | R-0010BE-5 stands; R-0023-27 stands; R-0024-27 stands; R-0025-27 (1)(2)(3) stand, (4) → no tenant default; R-0026-27 stands; R-0028-27 (2) → `TmBanners.visible`, (3) rejected; R-0029-27 → flat members, `tmSearchNormalize`; R-0030-R13 accepted; R-0031-R12 accepted with the `defaultPrevented` rule; R-0032-R16 → `order`; R-0033-R6 none; R-0034-R9 stands |
| 0028 | R-0010BE-5 stands; R-0023-28 stands; R-0024-28 stands with 0028's copy; R-0025-28 stands; R-0026-28 stands; R-0027-28 (1) `visible` → `TmBanners`, rest stands; R-0029-28 stands; R-0030-R14 accepted under §0.6 ("Exporting…", bulk counts), Track → job details; R-0030-R15 → `tab=mine`; R-0031-R13 accepted; R-0032-R8 accepted; R-0032-R17 stands; R-0034-R10 accepted ("Report saved.", "Report deleted.", "Sharing updated."); R-0035-R6 (a)(b) accepted |
| 0029 | R-0010BE-5 stands; R-0023-29 stands; R-0024-29 stands; R-0025-29 stands; R-0026-29 stands; R-0027-29 (1) → flat members, (2) stands, (3) → `TmUrls.blob`; R-0028-29 (1) `icon` accepted, (2)(3) stand; R-0030-R16, R18, R19 rejected; R17, R20 accepted; R-0031-R14 rejected; R15 accepted (`defaults`; `validate` typed by 0031); R16, R17, R18 accepted; R-0032-R10, R11 accepted; R-0033-R1 (1)(3)(4) accepted, (2) → F's model; R-0034-R4 (a) rejected, (b)(c)(d) accepted; R-0010F-2 accepted |
| 0030 | R-0010BE-4 stands; R-0023-30 stands; R-0024-30 stands; R-0025-30 stands with the new value shapes; R-0026-30 stands; R-0027-30 stands; R-0028-30 (1) accepted (`tabs`, `tab=`), (2) → `select?`, (3) `rowLink` accepted, `onActivate` rejected, (4) `pageActions` accepted, `bulkActions` rejected, (5) rejected, (6) → job details; R-0029-30 stands; R-0031-R19, R20, R21 accepted; R-0032-R12 accepted; R-0034-R3 (a) accepted, (b) rejected |
| 0031 | R-0010BE-5 stands; R-0023-31 stands; R-0024-31 stands; R-0025-31 stands; R-0026-31 stands; R-0028-31 stands; R-0029-31 stands; R-0030-R21 stands; R-0032-R1, R4, R5, R6 accepted; R7 → `registerField`/`registerCollection`; R8 accepted; R-0034-R8 (a) accepted, (b) rejected, (c) accepted; R-0035-R8 (a)(b)(c) accepted |
| 0032 | R-0026-32, R-0027-32, R-0028-32, R-0029-32, R-0030-R22, R-0031-R22 stand |
| 0033 | R-0028-33 accepted (`Toast = false`); R-0029-33 stands; R-0010F-1 accepted; R-0034-R1 (a)(c)(d)(e)(g) accepted, (b)(f) rejected; R-0035-R3 (a)(b)(c)(e) accepted, (d)(f) rejected |
| 0034 | R-0023-34 stands; R-0024-34 stands; R-0025-34 stands; R-0026-32 stands; R-0027-34 stands; R-0029-34 stands (`accept` for measures too); R-0030-R23 accepted (search host); R-0031-R23 stands; R-0033-R2, R3 stand; R-0035-R4 (a) `kind` rejected, title and copy accepted, (b) rejected, (c) accepted, (d) field and codec accepted, bar rejected, (e) rejected |
| 0035 | R-0023-35 → absolute time; R-0025-34, R-0026-32, R-0027-34, R-0028-35, R-0031-R24, R-0033-R4 (with `int`), R-0034-R11 stand |
| outside | R-0024-13 accepted (G30) |

### 2.17 Quality findings (by ID)

| Q | Verdict | Q | Verdict | Q | Verdict |
|---|---|---|---|---|---|
| Q-1 | A (§0.1) | Q-15 | A | Q-29 | A |
| Q-2 | A (§0.2) | Q-16 | A | Q-30 | A |
| Q-3 | A | Q-17 | A | Q-31 | M (cookie on the three chunks; `/signed-out` without metadata) |
| Q-4 | A | Q-18 | A | Q-32 | A |
| Q-5 | A | Q-19 | A | Q-33 | A |
| Q-6 | A | Q-20 | A | Q-34 | M (`DistributionInfo.session.idleDays`, no new config key) |
| Q-7 | A | Q-21 | A | Q-35 | A |
| Q-8 | A | Q-22 | A | Q-36 | A |
| Q-9 | A | Q-23 | A | Q-37 | A |
| Q-10 | A | Q-24 | A | Q-38 | A |
| Q-11 | A | Q-25 | A | Q-39 | A |
| Q-12 | A | Q-26 | M (`pageActions` is a declaration member; bulk actions derive from entity actions) | Q-40 | A |
| Q-13 | A | Q-27 | A | Q-41 | A |
| Q-14 | A | Q-28 | A | Cut list | A, all fifteen rows |

## Part 3 — Edits to existing specs

Every item of `consolidated.md` is applied unless listed here; the lines below resolve each flag
and add or drop items. "A" = apply as written.

### 3.1 Spec 0010

- X-0010-1: letter-dotted numbering throughout (§14 ruling 5); all three cascades applied.
- X-0010A-1 … A-4: A (numbered §A.9, §A.10, Part A tests).
- X-0010B-1: A. X-0010B-2: BE's text; drop the N23 A6 conflict. X-0010B-3: A (targets §4 and §1;
  no tokens). X-0010B-4: A. X-0010B-5: **dropped** (0024 creates the policy). X-0010B-6: A.
  X-0010B-7: designed in B.6 (also the 44 px menu rows). X-0010B-8: A. X-0010B-9: A.
- X-0010C-1, C-2, C-3: A. X-0010C-4: `column` only. X-0010C-5: **dropped**. X-0010C-6, C-7, C-8: A.
  X-0010C-9: **dropped**. X-0010C-10: BE's location; tiles import the type. X-0010C-11: A (one
  sentence in C.1). X-0010C-12: one export `TM_GRID_KEYS`. X-0010C-13: BE's payload; tiles reuse
  it. X-0010C-14: BE's scope plus registry-name `icon` and `TmKeys` `shortcut` on items, links and
  submenus; `layout` and `cssClass` dropped; 44 px rows via B.6. X-0010C-15: A. X-0010C-16:
  **dropped**. X-0010C-17, C-18, C-19: A.
- X-0010D-1: A (ruling 3 reworded). X-0010D-2: BE's shape; `age` dropped; `TM_TIME_ZONE` merged.
  X-0010D-3: A (D.7). X-0010D-4: `weekday` only (D.8). X-0010D-5: BE's `clearAll({ keep })`; 0024
  and 0025 align. X-0010D-6: A (D.10). X-0010D-7: A. X-0010D-8: A (D.9). X-0010D-9: **dropped**.
  X-0010D-10: BE's (no `direction`). X-0010D-11: A. X-0010D-12, D-13: A.
- X-0010E-1, E-2: A. X-0010E-3: BE's chip row. X-0010E-4: A (E.3). X-0010E-5: A (E.4). X-0010E-6: A.
- X-0010F-1: A (`QX4009`). X-0010F-2 … F-4: A. X-0010F-5: F's `Literal` origin (the moved text
  changes). X-0010F-6: A (not an engine amendment). X-0010F-7: A. X-0010F-8: F's caller error.
  X-0010F-9: F's (optional `KeyGroups`, "no set holds every group", 4,096). X-0010F-10: F's level
  navigations; cascades applied. X-0010F-11: F's polarity folding, `IReadOnlyCollection`.
  X-0010F-12: **dropped** (Q-7). X-0010F-13: A (`QX4009`; name kept). X-0010F-14: A. X-0010F-15:
  row-grain seek and `QX3104` only; prologue variables dropped (Q-22). X-0010F-16: A (caller
  errors). X-0010F-17: A. X-0010F-18: A, with F.12–F.16 renumbered F.11–F.15.

### 3.2 Specs 0011–0022

- **0011.** X-0011-1: A; G3 applied (variable port, 4200 fallback, in §1.2, §6.6 and 0018 §6.2).
  X-0011-2: report seed and widget-type items only; the dashboard-seed item **dropped**. X-0011-3 …
  X-0011-5: A (G1 applied: the §3.8 reader becomes the SPA's launch redirect). X-0011-6: A.
  X-0011-7: A (G13, G21 cascades applied in 0016 §8.2, §8.5, §9.4 and 0021 §8). X-0011-8, 9: A
  (X-0016-20 cascade applied). X-0011-10 … 12: A. X-0011-13: A (merged). X-0011-14: A. X-0011-15:
  A; add `Session(int IdleDays)` (Q-34); G2 applied. X-0011-16: A. X-0011-17: `core.report-seeds`
  only, `Order` 30. X-0011-18: A (G4 applied). X-0011-19: A (the mechanism stays in 0011 §8 and 0013
  §5.7; the skeleton row is corrected). X-0011-20: A (widened to BFF and tenantless). **New
  X-0011-21** §5.10: `ITellmaLinks` (`Tenant(t)`, `Target(t, resource, id)`) defined here and
  registered by the host (G29). **New X-0011-22** §5.5: `sh` is accepted by no endpoint as a
  credential (Q-35).
- **0012.** X-0012-1 … 8: A. X-0012-9: A (G11 applied to 0016 §8.3). X-0012-10 … 12: A. X-0012-13:
  A, carrying F's `Literal` origin and caller error. X-0012-14 … 16: A.
- **0013.** X-0013-1: A (G30: the four contributors are 0013 §1.2, 0014 §10, 0020 §3.7, 0021 §2.5;
  0016 §3.10 adds the fingerprint contributor). X-0013-2: N25's (values on user-addressed events);
  G7 applied; target §2.5. X-0013-3: A. X-0013-4: A (G6 applied). X-0013-5 … 8: A. X-0013-9 … 10:
  A. X-0013-11: N32's per-entry stamps. X-0013-12, 13: A. X-0013-14: A. X-0013-15: A (G5 applied:
  the Non-goals bullet goes). X-0013-16: A (the `Action_*` strings here, not 0014 §2). X-0013-17: A.
- **0014.** X-0014-1 … 7: A (G8 applied: §2 vocabulary and §10's two fingerprint mentions).
  X-0014-8: **dropped** (Q-21). X-0014-9 … 13: A (G9, G10 applied: assignments and support requests
  join the self-scope list; the `INSERT` column list gains `AssignmentsSeenAt`). X-0014-14:
  → "returned by the Role stack's eager details extra `problems`". X-0014-15: A.
- **0015.** X-0015-1 … 4: A. X-0015-5: `[Stack(DefaultOrderBy)]` only; the two attributes stay
  declared in 0016 §4.1 and are cited (G20). X-0015-6: A (G14 applied in 0016 §4.6). X-0015-7:
  **dropped**. X-0015-8: without the extras check. X-0015-9: A. X-0015-10: `Extras` only; no
  `DeferredExtras`. X-0015-11: **dropped**. X-0015-12 … 15: A (G17: `tellma_query` maps `filter?`
  onto `Filters` with one leaf; G18: `DeleteByQueryRequest` stays singular). X-0015-16: `Extras`
  wording only. X-0015-17: A (the two defaults stay: `Id desc` for queries, `Id` for trees).
  X-0015-18 … 21: A. **New X-0015-28** §11.2: `[EntityRef(resource)]` on an argument property
  (Q-3). X-0015-22 … 27: A (G19: `ApiResourceAttribute.Queryable` added in 0016 §4.1).
- **0016.** X-0016-1 … 3: A (G13 applied). X-0016-4: without `extras`, `jobs/status`,
  `roles/problems`; `users/me/locale` is never retried. X-0016-5: **dropped**. X-0016-6: A (G16:
  `tellma-long` and `tellma-reports` on the reports routes and `dashboards/audience`, in §8.3 and
  §1.3). X-0016-7: without `jobs/status` and `roles/problems`. X-0016-8, 9: A (G17, G18). X-0016-10:
  **dropped**. X-0016-11 … 17: A. X-0016-18: A (G12 applied: the `Tellma-Build` bullet now says the
  client compares it). X-0016-19: A (G16). X-0016-20: A. X-0016-21: A (G15: `TellmaHeaders.Activity`).
  X-0016-22 … 31: A. **New X-0016-32** §4.1: `ApiResourceAttribute` gains `Queryable` (G19); `RelatedSelectAttribute`
  and `DetailsExpandAttribute` stay here (G20). **New X-0016-33** §3.10: the fingerprint contributor
  (G30).
- **0017.** X-0017-1, 2: A. **New X-0017-3** §5: the upload route requires the `Tellma-Client`
  header (Q-24).
- **0018.** X-0018-1: A without `DashboardSeed` and the deferred `history` extra; add `acme.Currency`
  (G31); G4 applied (the 40-node tree replaces the eight sample rows; `core.report-seeds` in the step
  list). X-0018-2 … 9: A. X-0018-10: → the Role stack's eager extra `problems` (same record, no
  route). X-0018-11: eager, filtered, capped at 500. X-0018-12, 13: A. X-0018-14: A (G27 applied:
  the initial-migration list gains every round-2 table and column). X-0018-15: `users/me/locale`
  row only. X-0018-16: A.
- **0019.** X-0019-1 … 7: A (G28: the report `export/start` sets `Label`).
- **0020.** X-0020-1 … 11: A (G23 applied in 0021 §6.3; X-0020-9 declared in §7.8). X-0020-12:
  **dropped** (Q-14). X-0020-13, 14: A. G26: add the id 7 row (`core.outgoing-email-retention`,
  `45 3 * * *`, spec 0022) above id 8.
- **0021.** X-0021-1 … 14: A (G22 applied: `assignments.changed` in the Context list). X-0021-3:
  both types `Toast = false`.
- **0022.** X-0022-1 … 4: A (G24: the Sara example links `/42/sales-invoices/17`; G25:
  `[Stack(DefaultOrderBy = "CreatedAt desc")]` on §2.1).

### 3.3 Gaps (G1–G33)

All thirty-three are accepted as edits, applied by the owners named in `gaps.md`, with these
modifications: G4 — the 40-node tree replaces the eight sample rows; G16 — both the timeout and the
`tellma-reports` policy; G20 — declared in 0016 only; G29 — `ITellmaLinks` is 0011's; G32, G33 —
per the numbering ruling.

## Part 4 — Rejected items

One line each; the reason is the ruling.

- **C-0027-7** measured pin overflow: the `xl` rule is enough; a measurement loop is cost without a
  need.
- **C-0030-4** `x.mine=1`: one `tab=` spelling (Q-2).
- **C-0031-3**, **R-0031-R14**, **X-0015-7**, **X-0015-11**, **X-0016-5**, **X-0016-10**, **X-0010D-9**
  deferred extras and tab prerender: no consumer has a heavy extra; one operation and one
  amendment fewer (Q-12).
- **C-0034-6** `?drill=state` fallback: a filter over the limit is a notice, not a second channel.
- **C-0023-5** 52 px two-line banner row: copy stays ≤ 45 characters.
- **R-0028-23** `tm-banner-bar announce`: the host announces at queue time.
- **R-0032-R13** `tm-signature-pad`: upload only; one component for one page is not a library
  addition (Q-15).
- **R-0032-R14** `tm-chips` free entry: no consumer.
- **R-0032-R2** `TmUrls.settings()`, `me()`: `page()` covers them.
- **R-0030-R7**, **R-0031-R6**, **R-0027-24(2)**, **R-0029-24(3)** `TM_ENTITY_PAGES`, `TM_REALTIME`,
  `TM_ENTITY_DECLARATIONS`: replaced by dynamic imports and one `TM_APP_DECLARATIONS` (Q-1).
- **R-0030-R18** `TmExpressionEditor.select`: the editor shows diagnostics itself.
- **R-0030-R19** `{ expression, label }`: labels must be keys.
- **R-0030-R16** `rememberRows`: `remember` exists.
- **R-0028-30(3)(4)(5)** `onActivate`, `bulkActions`, `refreshRows`, Refresh dot: `rowLink` covers
  activation, bulk actions derive from entity actions, admin pages refresh by button.
- **R-0034-R3(b)** hidden custom filter without `queryex`: `tabs` replaces it.
- **R-0034-R4(a)** term lists on the draft: the studio keeps its own term model.
- **R-0034-R8(b)** embedded report in the details context: v1 needs no assistant view of a card
  report.
- **R-0034-R1(b)**, **M-71** `Code` on run and export: ids only; the reports page resolves seeds.
- **R-0034-R1(f)** two more seeds: one seed exercises the path.
- **R-0035-R3(d)**, **X-0014-8** multi-securable `EvaluateOutcomesAsync`: the resolver needs no
  per-member outcome (Q-21).
- **R-0035-R3(f)** `via` counter tag: existing tags suffice.
- **R-0035-R4(b)** `[kind]` input and `held` semantics for parameter changes: the host knows its
  kind from the result; parameter changes present as `spinner`.
- **R-0035-R4(e)**, **M-78** per-widget audience warnings: cut with Q-11.
- **R-0035-R2(b)** a `home` launch document: the home page reads `assignments` and recents.
- **R-0034-R6(b)** `report.studioPanel`: cut list.
- **R-0035-R1**, **X-0010D-4** `tmZonedParts`: `tmFormatTime` and `formatDateTime` suffice.
- **X-0010B-5** library Trusted Types policy: 0024 creates it; icons need none.
- **X-0010C-4** `focus` on `scrollToRow`: the caller focuses (one way).
- **X-0010C-5** `footerRows`: a totals row is a last row with `rowClass`.
- **X-0010C-9** header underline: the action button's `active` state shows it.
- **X-0010C-16** `rowSelectable`: no consumer after the drill-list and reports-page rulings.
- **N23 A6** chips in both multi-selects: BE's renderings stand (text list; scrolling chip row).
- **N23 A7** `TmItemActivation`: one payload type.
- **N27 M1, M2** `layout: 'segmented'`, `cssClass`: radio items; no styling hook without a need.
- **N30** `TmGridDriverTarget` in `contracts`: type-only imports make the move pointless.
- **0023 `accordion`**, **`TmSearchField.error`**: no consumer (Q-17).
- **0028 `jobs/status`**, **`TmJobsAlerts`**, read-on-visit, "Connection restored.": operations
  features the brief did not ask for (Q-14).
- **0032 `roles/problems`** route: an eager extra does it without a route (Q-15).
- **0033 F.11** parameter expressions and **`'uq'` prologue variables**: one round trip is not
  worth two engine amendments (Q-7, Q-22).
- **0035 dashboard seeds**, viewport-gated scheduler, six-run cap, English `tmFirstName` list:
  Q-11.
- **0026** INP, layout shift, SHA-1 fingerprints, FNV sampling, six instruments, truncation ladder,
  admin paging: Q-8.
- **0027** context menu, launcher-to-bar drag, ranking classes, provider caching: Q-9.
- **0034** heatmap, treemap, gauge, Angular tooltip, per-point keyboard navigation, hidden table:
  Q-10.
- **0024** three extra boot pages, browser-based config check, Cookie Store listener: Q-16.
- **0025** superseded sets, settle window, `{at, v}` envelope, daily `savedAt`, `visible` lock,
  foreign-overlay expiry: Q-13.
- **0031** "Save as new": a third conflict choice users rarely hit (Q-12).

## Part 5 — Questions for Ahmad

Each becomes a review flag in the named spec; the ruling above is the default until he answers.

1. **Metadata chunks behind the session cookie (0024 §6, flag).** Ruled: `core`, `entities` and
   `contracts` require the cookie; anonymous access would expose every entity, action, securable
   and setting key. Alternative: anonymous, immutable assets (simpler boot; `/signed-out` needs no
   special case). Flip: if the OpenAPI document is anonymous anyway, or if a signed-out shell page
   must show tenant-independent metadata.
2. **Jobs, Exports and Imports under "Administration" for every member (0027 §3.2, flag).**
   Ruled: they stay there, self-scoped. Alternative: a `personal` section ("My jobs", "My
   exports"). Flip: a preference for a two-section launcher for non-admins.
3. **Toasts for outcomes without an inline home (0028 §8.1, flag).** Ruled: delete-and-leave, bulk
   counts, 202 starts, saves that navigate away toast once. Alternative: 0028's "never" (the page
   state is the feedback). Flip: a preference for a quieter UI.
4. **Drill-through list as the search host (0034 §8, flag).** Ruled: the drill list is a search
   page with a fixed filter leaf (ordering, column filters, paging, export for free). Alternative:
   a dedicated List renderer page. Flip: wanting a read-only, toolbar-free drill view.
5. **No dashboard seeds in v1 (0035 §7, flag).** Ruled: report seeds ship; dashboard seeds wait for a
   pack that needs one; Acme's e2e creates its dashboard. Alternative: seed `acme.sales-overview`
   as the brief's example home. Flip: wanting a seeded home dashboard for every new tenant.
