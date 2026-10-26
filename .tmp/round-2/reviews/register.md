# Round-2 integration register

Inputs: `design/00-architecture.md` (the skeleton, "A§n"), `design/outlines.md`, and the fifteen
design notes `design/{0023, 0010-BE, 0024 … 0035, 0010-F}.md` ("N24 §n" = that note's future-spec
section). This register reports; it decides nothing.

- **Part A** lists every item of every note's "Challenges to the architecture" section (111 items),
  grouped by the skeleton section it challenges.
- **Part B** lists every request of every note's "Requests to other round-2 specs" section, grouped
  by target, with the target note's answer (Provided, Partial, Contradicts, Lacks). It also lists,
  in a closing block of the 0010-BE and 0010-F groups, the section-6 amendment needs consumer notes
  address to those two notes (A§14 ruling 2 puts them in section 6, and several section-7 rows
  point there).
- **Part C** lists cross-note mismatches of names and contracts found in the sketches and prose
  (79 items, M-1 … M-79, grouped by theme, not in numeric order).

Part B holds 166 request IDs (175 entries: a request to several targets appears under each) plus 7
section-6 needs addressed to 0010-BE.

Status words in Part A: **Followed** (the note builds what the skeleton says and only raises the
point), **Deviated** (the note builds something else), **Completed** (an additive change the note
builds on top of the skeleton's sketch). Note on timing: `0010-BE.md` (13:59) and `0033.md` (15:38)
were written before most of their consumers and before `0010-F.md` (16:06), so many "Lacks" against
them are ordering, not refusals.

## Part A — Challenges to the architecture

### A§1 Spec map and scope moves

- **C-0028-9** — 0028 §12 item 9. Skeleton A§1: "0028: Notifications, Notification settings,
  Assignments, Jobs (one page…)". Challenge: A§1 gives Jobs to 0028 while N27 §3.2 lists Jobs under
  Administration, which every member sees through the self-scope. Proposal: none in the skeleton;
  raised as 0028 review flag 13 (search-only or a personal section). Status: Followed. Interactions:
  R-0028-27(3) asks 0027 to reconsider (0027 keeps Jobs under Administration); same theme as
  C-0027-3 (no personal section) and 0032 review flag 19 (Exports, Imports under Administration).
- **C-0032-6** — 0032 §12 item 6. Skeleton A§1 0031 row and A§3.6: Settings and My profile have no
  framework ("Users, Roles, Settings, My profile …" are 0032's pages). Proposal: a details-source
  seam (`TmDetailsSource`, `tm-details-page`) that A§1 should list under 0031. Status: Deviated
  (0032 §12 sketches the seam in `@tellma/core/details`). Interactions: R-0032-R1 (0031 lacks it);
  M-40 (contract defined by a non-owner).
- **C-0035-1** — 0035 §12 item 1. Skeleton A§1 0035 row "Depends on —" and A§6.3 (no 0035 rows but
  0014 §2). Proposal: list 0035's edits to 0011, 0013, 0016, 0021, 0018. Status: Completed (0035 §5
  E1–E7). Interactions: none.

### A§2.1 Package family

- **C-0032-4** — 0032 §12 item 4. Skeleton A§2.1: "`@tellma/module-gl` … `tmGlFeature()`, the
  `gl.Center` declaration and layout, `TmGlCenterFields`" as one package entry. Proposal:
  `@tellma/module-gl` (eager declaration) plus `@tellma/module-gl/centers` (layout and
  `TmGlCenterFields`), because the layout imports `/details`; A§2 should state the split for every
  pack with layouts. Status: Deviated. Interactions: the A§2.2 import-rule family below; A§3.4's
  example imports `TmGlCenterFields` from `@tellma/module-gl` (would become `/centers`).

### A§2.2 Entry points, the import rule, the flowchart

Seven notes meet the same gap: the primary (eager) must reach lazy entry points through `import()`,
which A§2.2's rule ("the primary imports no other entry point … no eager entry point statically
imports a lazy one") does not say. Several ask for one ruling.

- **C-0025-8** — 0025 §12 item 8. A§2.2 import rule; `TmHub` "lives in `/realtime`". Proposal:
  token `TM_REALTIME` filled by the tenant shell's route providers; one answer (tokens filled by
  `/shell`, or an exception for dynamic imports from the primary) should serve 0024 too. Status:
  Deviated. Interactions: duplicates C-0027-1, C-0029-5, C-0032-5; R-0025-27(1) (0027 provides),
  R-0027-24(2) (0024 lacks); M-29.
- **C-0027-1** — 0027 §12 item 1. A§2.2 table: `/shell` "eager". Challenge: nothing statically pulls
  `/shell` (or `/attention`) into the initial bundle under the rule. Proposal: route table loads the
  shells with `import()`, `tellma-client build` adds `modulepreload`; proposed A§2.2 sentence "the
  primary's route table and its loader tokens may reference other entry points through `import()`".
  Status: Deviated. Interactions: answers C-0025-8; R-0027-24(1) (0024 lacks); duplicates C-0032-5,
  C-0029-5.
- **C-0028-8** — 0028 §12 item 8. A§2.2 rule: `provideTellma` cannot reference `/attention`.
  Challenge: the toast overflow action cannot open the bell. Proposal: the pill navigates to the
  Notifications page; alternative a primary "open panel" signal `/attention` listens to. Status:
  Deviated. Interactions: R-0023-24 (provideTellmaToasts with 0028's action, 0024 lacks); 0028's own
  R-0028-30(6) uses `TmAttention.toggle('bell', 'toast')` from `/search` (allowed: lazy → eager).
- **C-0029-3** — 0029 §12 item 3. A§2.2 `/entity` holds "full descriptors". Proposal: descriptor
  code in the primary (≤ 6 KB) because `TmEntities.get` is primary API; only the data loads lazily.
  Status: Deviated. Interactions: C-0029-2.
- **C-0029-5** — 0029 §12 item 5. A§2.2 rule vs A§10.1 Stock Q1 (picker modals show the target's
  search and details pages). Proposal: `TM_ENTITY_PAGES` loaders from `provideTellma` plus a
  `context` member on 0006's page object (0010 Part E); one ruling should cover 0024, 0025 and
  0029. Status: Deviated. Interactions: R-0029-24(3), R-0030-R7, R-0031-R6 (0024 lacks the token);
  Part E `context` (0010-BE lacks, M-64); duplicates C-0025-8, C-0027-1, C-0032-5.
- **C-0030-9** — 0030 §12 item 9. A§2.2 rule vs 0031's paging (`/details` cannot import `/search`).
  Proposal: `TmSearchSnapshot` (and `TmTileDeclaration`, `TmCustomFilterDeclaration`) as type-only
  exports of the primary. Status: Deviated. Interactions: R-0030-R10 (0024 lacks); 0031 consumes the
  type (§12.1).
- **C-0031-4** — 0031 §12 item 4. A§3.6 "an entity action with arguments: `dialog` (0031)" vs the
  rule (the search page's bulk actions open the same dialogs). Proposal: `TmEntityActions` and
  `injectTmActionDialog` in `@tellma/core/entity`. Status: Deviated. Interactions: R-0031-R16 (0029
  lacks them in its `/entity` list); R-0031-R21 (0030 partial).
- **C-0032-5** — 0032 §12 item 5. A§2.2 `/admin` "lazy route, one chunk", named by eager
  declarations. Proposal: declarations `import()` `@tellma/core/admin`; one ruling for 0025 C8, 0029
  C5 and this. Status: Deviated (0032 §3 `const admin = () => import('@tellma/core/admin')`).
  Interactions: duplicates C-0025-8, C-0027-1, C-0029-5; R-0032-R2 asks 0024 for the ruling (lacks).
  The same pattern appears unchallenged in 0026 §12.5 (`import('@tellma/core/support')`), 0028 §1
  (`/attention-pages`), 0034 §2.3 (`/reports`, `/report-studio`), 0035 §12.1 (`/dashboards`).
- **C-0035-3** — 0035 §12 item 3. A§2.2 flowchart draws `/dashboards` from `/reports` only.
  Proposal: `/dashboards` imports `/details` and `/attention`, reaches `/reports` only through
  `import()`. Status: Completed (additive edges). Interactions: 0028 §1 confirms `/dashboards`
  imports `TmAssignmentList` from `/attention`.

### A§2.3 Generic versus feature components

- **C-0023-1** — 0023 §12 C1. A§2.3: "`chips` (+ multi-select input)". Proposal: ship `tm-chip` and
  `tm-chip-set` only; the multi-selects are 0010 Part B's and Part E's, embedding `tm-chip-set`
  (0023 A6). Status: Deviated. Interactions: duplicates C-0010BE-6 and R-0010BE-1; conflicts with
  R-0032-R14 (`tm-chips` free entry, which 0023's evidence says no consumer needs); 0010-BE renders
  neither multi-select through `tm-chip-set` (M-12).
- **C-0023-2** — 0023 §12 C2. A§2.3 entry-point list. Proposal: add `@tellma/core-ui/copy`
  (`tm-copy-button`, `tm-reference`) for trace-id and version copy outside the error dialog. Status:
  Deviated (built; 30 entry points). Interactions: R-0023-26 (0026 uses them); 0030 §5.3, 0028 §10.5,
  0032 §4.4 consume `tm-reference`.
- **C-0010BE-6** — 0010-BE §12 item 6. A§2.3 still lists `drawer` and a "multi-select input" under
  0023. Proposal: edit A§2.3 (ruling 1 removed the drawer; the input duplicates B.4 and E.2). Status:
  Followed. Interactions: duplicates C-0023-1.
- **C-0034-5** — 0034 §12 item 5. A§2.3 and A§6.6 treat `core-ui/chart` as the ECharts boundary.
  Proposal: the entry point is small (card and table need no engine); the engine is a dynamic chunk,
  alone in `on-demand`. Status: Completed. Interactions: 0024 §5.3 cuts dynamic-import edges "into
  `@tellma/core-ui/chart`" for the `on-demand` group (would need to cut at the engine chunk instead).

### A§3 What a distribution writes

- **C-0024-5** — 0024 §12 item 5. A§3.1 says both "`index.html` … tm-boot.js" and "build … injects
  `tm-boot.js`". Proposal: the scaffolded `index.html` carries the tag; the build generates the file;
  `check` verifies the tag. Status: Completed. Interactions: none.
- **C-0029-1** — 0029 §12 item 1. A§3.3 `tmEntity<AcmeCustomer>('acme.Customer', …)`. Challenge: an
  explicit type argument disables path inference. Proposal: name-literal typing through a generated
  `TmEntityShapes` augmentation; no type argument. Status: Deviated. Interactions: 0026 §12.5 and
  0028 §10.1 still write `tmEntity<CoreSupportRequest>` / `tmEntity<CoreNotification>` (M-23).
- **C-0030-7** — 0030 §12 item 7. A§3.3 tile `image: 'ImageId'` vs 0029's navigation column keys
  (`Image`). Proposal: both resolve for `image`; examples use `Image`. Status: Completed.
  Interactions: 0023 §3.8 and 0029 §3 examples still use `ImageId`; 0032 uses `Image` (M-10).
- **C-0033-5** — 0033 §12 item 5. A§3.4 `tmCodedReport` with `run` and `default: 'today()'`.
  Challenge: the closure hides the route (no server export), and the client cannot evaluate
  `today()`. Proposal: the data-only form declares `route`; defaults applied and echoed by the C#
  method. Status: Deviated. Interactions: duplicates C-0034-1; R-0033-R3 (0034 provides).
- **C-0034-1** — 0034 §12 item 1. Same A§3.4 example. Proposal: `route` is the common case, `run` the
  hatch, defaults in C#. Status: Deviated (0034 §2.2). Interactions: duplicates C-0033-5; wire naming
  of coded-report arguments differs (M-72).
- **C-0024-4** — 0024 §12 item 4. A§3.6 `hide: ['core.roles']`. Proposal: kind-qualified keys
  (`entry:core.Role`, `entity:`, `page:`, `report:`, `section:`, `settingsTab:`, `appBar:`). Status:
  Deviated. Interactions: C-0027-6 follows it; 0032 §2 uses `hide: ['entry:core.Schedule']`,
  `['settingsTab:gl']`.
- **C-0027-6** — 0027 §12 item 6. A§3.6 menu hatch "redeclare sections or entries in `overrides`".
  Proposal: follow N24 (overrides are entity-only); change a section or entry with `hide` plus a
  redeclaration under a new id. Status: Deviated. Interactions: C-0024-4.
- **C-0030-6** — 0030 §12 item 6. A§3.6 "`search: () => import(…)` replaces the body". Proposal:
  "body" = the results region (row 3); rows 1–2 stay the platform's; the route host loads the
  search host, not the hatch. Status: Completed. Interactions: R-0030-R6 contradicts 0024 §4.4 (route
  host loads the hatch itself, M-30).

### A§5.1 Distribution API

- **C-0027-3** — 0027 §12 item 3. A§5.1: core sections `administration` and `analysis` only.
  Challenge: Notifications, Assignments, My profile, Notification settings get no browse home.
  Proposal followed: search-only entries; alternative a `personal` section. Status: Followed.
  Interactions: C-0028-9; C-0027-4 (optional `section`).

### A§5.2 Client metadata

- **C-0024-2** — 0024 §12 item 2. A§5.2 `contracts: … Record<string, string>`. Proposal: per route
  `{ fp, read, long }` so `TmApi` can refuse a non-retriable `read` and pick the watchdog. Status:
  Followed (adds a `long?` option instead). Interactions: R-0024-34 (0034 runs with `long`).
- **C-0024-7** — 0024 §12 item 7. A§5.2 `distribution.displayName` as a build fact. Challenge: it is
  runtime configuration. Proposal followed: read `appsettings.json` at emit time (0024 flag 10).
  Status: Followed. Interactions: none.
- **C-0029-7** — 0029 §12 item 7. A§5.2 with 0024 §6's anonymous static assets. Challenge: the entity
  schema ships in an anonymous bundle, a disclosure A§12 does not list. Proposal: review flag 8.
  Status: Followed. Interactions: none.
- **C-0032-7** — 0032 §12 item 7. A§5.2 lists `settingKeys` alone; and launcher member names differ
  between 0027 and 0029. Proposal: add `settingCategories`; use 0029's `launcher: { … }`. Status:
  Deviated (adds a section). Interactions: R-0032-R3 (0024 lacks); M-3 (member names); R-0032-R16
  (0027 contradicts).

### A§5.3 API client, blob fetching and the display zone

- **C-0010BE-7** — 0010-BE §12 item 7. A§5.3 "The platform provides `TM_BLOB_FETCHER` over `TmApi`".
  Challenge: `TmApi` has only POST verbs; blobs are GETs. Proposal: a GET blob method on `TmApi`, or a
  fetcher over `fetch` repeating the user check. Status: Followed (raised only). Interactions: 0024
  §10.5 chose the second ("a `GET` with `If-None-Match` … the user check of §10.3") — resolved by
  0024; C-0026-5 (no upload).
- **C-0026-5** — 0026 §12 item 5. A§5.3 `TmApi` has no upload. Proposal: `TmApi.upload(kind, blob,
  { fileName, signal?, onProgress? })`. Status: Followed (requested). Interactions: same ask from
  0029, 0030 (R9), 0031 (R7); 0024 lacks it (M-21).
- **C-0023-6** — 0023 §12 C6. A§5.3/A§5.4: "the client formats instants in the same zone" with no
  named seam. Proposal: `TM_TIME_ZONE: InjectionToken<Signal<string>>` (0023 A3). Status: Completed
  (requested of 0010 Part D). Interactions: 0010-BE D.4 defines `TM_TIME_ZONE`; R-0010BE-2 and
  R-0023-24 ask 0024 to fill it (0024 lacks, M-29); 0029 §12.1 `TmFormats` reads it.

### A§5.5–A§5.8 Store, bus, launch cache, preferences, page state

- **C-0025-2** — 0025 §12 item 2. A§5.6/§5.7 `prefs.changed { keys }`; "other tabs apply at once".
  Proposal: `values` and `at` replace `keys`. Status: Deviated (0025 §6.2). Interactions: C-0025-6.
- **C-0025-6** — 0025 §12 item 6. A§5.7 "stores the returned bag (overlaid with keys still
  unflushed)". Proposal: the store holds server truth; the overlay applies at read time. Status:
  Followed in effect. Interactions: A§6.3 row 0014 §3.4 wording (0025 E6 rewrites it).
- **C-0025-11** — 0025 §12 item 11. A§5.7 `TmLaunchDocs` `[list: \`entity:${string}\`]` index member.
  Challenge: cannot start a lazy load from a signal read. Proposal: `TmLaunchCache.entitySet()`.
  Status: Deviated. Interactions: R-0025-29 (0029 provides).
- **C-0025-7** — 0025 §12 item 7. A§5.8 recents "by route template". Challenge: every stored report
  collapses into one entry. Proposal: key by tenant-relative path; pages only. Status: Deviated.
  Interactions: 0027's deviation table cites it; C-0035-2 (dashboards count as recents only with
  their own context kind).
- **C-0025-12** — 0025 §12 item 12. A§5.8 sketches. Proposal: `TmTreeExpansion.replace`,
  `TmPageState.markStale` (and `isStale`, `forget`). Status: Completed. Interactions: R-0010BE-3
  (whole-set writes, provided by `replace`); 0030 §16.3 and 0031 §8.3 use them; R-0031-R10 asks that
  `markStale` reach details entries.
- **C-0030-2** — 0030 §12 item 2. A§5.7 and 0025 Appendix A: `search.<entity>.view` is the four-valued
  union and `.columns` is `string[]`. Proposal: `.view` = `{ narrow?, wide?, list? }`, `.columns` =
  `TmColumnPreference[]`. Status: Deviated. Interactions: C-0030-1; R-0030-R11 (0025 contradicts,
  M-6).
- **C-0031-8** — 0031 §12 item 8. A§5.8 `TmDrafts` key `<id|new>`: a clone shares `new` with a
  create. Proposal: none needed (the draft's `clonedFrom` disambiguates). Status: Followed.
  Interactions: none.

### A§5.9 Telemetry and request log

- **C-0026-3** — 0026 §12 item 3. A§5.9 `TmRequestRecord` (one record) and N24 §10.3 step 5.
  Proposal: `begin`/`end` handles and a `pending` outcome so a hanging call shows. Status: Deviated.
  Interactions: R-0026-24(1) (0024 contradicts: one record after the response); R-0024-26 asks 0026
  for "§10.3 step 5's" fields (M-19).

### A§5.10 Page context and page actions

- **C-0026-1** — 0026 §12 item 1. A§5.10 "the context maps to a `STATE_SNAPSHOT` with `STATE_DELTA`".
  Challenge: those are agent-to-client events; client facts reach the agent in `RunAgentInput`.
  Proposal: §9.7's mapping (context, tools). Status: Followed where it binds (nothing ships).
  Interactions: none.
- **C-0026-6** — 0026 §12 item 6. A§5.10 `TmPageActions.register` "adds to the current context".
  Challenge: a dialog's action would land on the page beneath. Proposal: the nearest page scope
  (`provideTmPageScope`), root falls back to `current`. Status: Deviated. Interactions:
  R-0026-24(5) (route host provides scopes; 0024 partial); C-0035-2 and R-0035-R8(a) (a page hatch
  replacing the host's context loses its actions).
- **C-0026-7** — 0026 §12 item 7. A§5.9, §5.10, §6.1 sketches. Proposal: completions —
  `TmPageContext.status`, `overlay`; `TmPageAction.group`, `order`, `confirm`, `descriptionKey`;
  `TmFormContext.dirty`, `assisted`, boolean `propose`; `TmErrorRecord.source`, `fingerprint`,
  `repeats`; `ClientTelemetryBatch.SentAt`; `TmAppVersion`, `TmSupport`. Status: Completed.
  Interactions: 0030, 0031, 0034, 0035 build on them (consistent); 0024 §2.4's `custom` context lacks
  `status` (R-0026-24(5)).
- **C-0035-2** — 0035 §12 item 2. A§5.10/0026 §9.3 give dashboards a `dashboard` kind while 0031
  §3.7 says the `page` hatch's context stays the host's. Proposal: the hatch registers `dashboard`,
  replacing the host's context; R8(a) keeps the host's actions. Status: Followed A§5.10 (against
  0031). Interactions: R-0035-R8(a) (0031 lacks `actions`); the same conflict exists unraised for
  0034's report page (a details `page` hatch publishing `report`, 0034 §13) (M-54); C-0026-6.

### A§5.11 Shell registries and slots

- **C-0027-2** — 0027 §12 item 2. A§5.11 `TmShellSlot.badge?: Signal<number | null>`. Challenge:
  declarations are module constants where `inject` is unavailable. Proposal: `badge?: () =>
  Signal<number | null>`. Status: Deviated. Interactions: R-0027-24(4) (0024 lacks).
- **C-0027-4** — 0027 §12 item 4. A§5.11 `TmNavEntry`. Proposal: `section` optional (search-only
  entries) and `requires` accepting several securables. Status: Completed. Interactions: C-0027-3.
- **C-0027-5** — 0027 §12 item 5. A§5.11 places `help` and `assistant` between the tray and the
  session button; the brief lists them before the bell. Proposal: none (flag so brief and skeleton
  agree). Status: Followed. Interactions: none.

### A§5.12 Entities, display names, permissions

- **C-0029-2** — 0029 §12 item 2. A§5.12 `TmEntities { get(): TmEntityDescriptor; bySegment():
  TmEntityDescriptor | undefined }` in the eager primary. Proposal: `summary`/`bySegment` →
  `TmEntitySummary` (Core chunk), `get` → descriptor after `load()`; the display-name rule moves into
  the summary. Status: Deviated. Interactions: C-0029-3; R-0024-29 (`bySegment` at match time,
  provided); R-0029-24(2) (route host awaits `load()`, 0024 partial, M-22).

### A§5.13 URLs, errors, page frameworks

- **C-0031-2** — 0031 §12 item 2. A§5.13 `TmDetailsContext` (`cancel(): void`). Proposal:
  `cancel(): Promise<boolean>`; adds `id`, `status`, `error`, `model`, `dirty`, `extra(name)`, `can`,
  `childTarget`, `update`. Status: Deviated/Completed. Interactions: R-0035-R8(a), (b) ask for more
  (`actions`, permission-extra `can`).
- **C-0034-4** — 0034 §12 item 4. A§5.13 `TmReportHost` refresh mode `spinner | silent`. Challenge:
  no force refresh that clears. Proposal: `run({ clear: true })`. Status: Completed. Interactions:
  R-0034-R11 (0035 uses it, §11.3).

### A§5.14 Library and attention seams

- **C-0023-3** — 0023 §12 C3. A§5.14 `TmBanners.show(banner: TmBanner)`. Proposal: rename 0028's data
  type `TmBannerOptions`; 0023's component is `tm-banner-bar` / `TmBannerBar`. Status: Followed
  (0023 avoids the name). Interactions: duplicates C-0028-2; R-0023-28 (0028 renamed).
- **C-0028-2** — 0028 §12 item 2. Same A§5.14 line. Proposal: rename to `TmBannerOptions`. Status:
  Deviated (renamed). Interactions: duplicates C-0023-3 (M-1).
- **C-0028-7** — 0028 §12 item 7. Not skeleton text: N27 request 1 asks `tm-banner-host` to expose
  `visible`. Proposal: the primary's `TmBanners.visible` serves the shell. Status: Deviated (from
  0027's ask). Interactions: R-0027-28(1) (contradicted); 0027 §5.1 still reads the height from the
  host (M-58).

### A§6.1 New and changed endpoints

- **C-0025-5** — 0025 §12 item 5. A§6.1 `ClientSyncRequest(… Held); // document → wire tag held`.
  Proposal: `Held` is keyed by tag name (`me` depends on two tags); C# shape unchanged. Status:
  Followed (comment correction). Interactions: none.
- **C-0029-6** — 0029 §12 item 6. A§6.1 `queryex/discover` description. Proposal: a `Permission`
  profile so 0032's role editor validates as role save does. Status: Completed. Interactions:
  R-0029-32 (0032 uses `profile: 'permission'`).
- **C-0033-3** — 0033 §12 item 3. A§6.1 `reports/…` routes `export`, `export/start`. Challenge: they
  collide with 0015's reserved standard segments. Proposal: the stack omits `Export`/`Import`;
  0015's reserved-name rule narrows to the segments a stack projects (0033 E5). Status: Deviated.
  Interactions: none.
- **C-0031-3** — 0031 §12 item 3. Not in the skeleton: extras loading (0015 offers no extras-only
  read). Proposal: a standard `extras` operation and declared deferred names (0031 E1–E3). Status:
  Completed. Interactions: R-0031-R14 (0029 metadata `extras` shape contradicts, M-44); 0032 uses
  deferred `members` (§5.4).

### A§6.2 New tables and columns

- **C-0026-4** — 0026 §12 item 4. A§6.2 `core.SupportRequests` with "dump and screenshot blob refs".
  Challenge: 0017 allows one blob property per kind. Proposal: a child table
  `core.SupportRequestFiles` under the single kind `support-bundle`. Status: Deviated. Interactions:
  none.
- **C-0028-1** — 0028 §12 item 1. A§6.2 `core.Assignments` "publishes `assignments.changed` to the old
  and new assignee". Proposal: also the assigners of the closed and new rows. Status: Deviated.
  Interactions: none.

### A§6.3 Edits to specs 0010–0022

- **C-0025-10** — 0025 §12 item 10. A§6.3 rows "0011 §5.5 profile cookie" and "0014 §7, 0016 §4.3,
  §6.1". Challenge: hub traffic is missing from the background rule, so a hidden leader keeps a
  session alive. Proposal: 0025 E13, E16 (no `Hub`-group request stamps activity or renews cookies).
  Status: Completed. Interactions: R-0025-24(5) asks 0024 to fold E16 into its E8(d) (0024 lacks).
- **C-0030-3** — 0030 §12 item 3. A§6.3 row 0015 §5.4 (`IncludeRoots` + `OrderBy`). Challenge:
  `get-by-parent-ids` is unbounded and still takes one `Filter`. Proposal: E3 adds `Filters` and a
  `MaxTake` cap. Status: Completed. Interactions: none.
- **C-0030-5** — 0030 §12 item 5. A§10.1 Trees Q1 and A§6.3 row 0019. Challenge: only `Columns` is
  named, not the source's `Filter`. Proposal: E4 (`Filters`). Status: Completed. Interactions: 0034
  §9 drill-list export sends `filters: [filter]` (consistent).
- **C-0032-1** — 0032 §12 item 1. A§6.3 row 0013 §6.3 "entries carry per-category expected stamps".
  Challenge: a category has no stamp (a reset deletes its row). Proposal: per-entry stamps. Status:
  Deviated (0032 E3). Interactions: none.
- **C-0032-2** — 0032 §12 item 2. A§6.3 row 0018 §4 (`members` extra gains columns). Challenge: the
  extra is unpaged and ignores a filtered `core.User × Read`. Proposal followed: extra plus row filter
  and a 500 cap (E11); alternative 0033's child-query route. Status: Followed with an amendment.
  Interactions: C-0031-3 (deferred extras).
- **C-0032-3** — 0032 §12 item 3. A§6.3 row 0020 §7.2 (`ScheduleStates` as a details extra).
  Proposal: a `[Sibling]` navigation would make state sortable; followed with the `state` and
  `activity` extras. Status: Followed. Interactions: none.
- **C-0033-6** — 0033 §12 item 6. A§6.3 0015/0016 rows for child-entity routes. Challenge: under a
  filtered owner grant the traversal rule 403s `Invoice.PostingDate`. Proposal: owner-path
  refinement (E7(b)) and the matching client rule. Status: Completed. Interactions: R-0033-R1(3)
  (0029 partial).
- **C-0033-7** — 0033 §12 item 7. Not skeleton text: research D5's narrow share check vs the brief's
  "which users can see it, and why"; 0014's `access/check` is one user at a time. Proposal: the
  audience resolver and a bulk `EvaluateOutcomesAsync` in 0014 (E2). Status: Completed.
  Interactions: R-0035-R3(d) (multi-securable overload, 0033 lacks).

### A§6.4 Amendments to frozen specs (spec 0010 Parts B–F)

- **C-0010BE-1** — 0010-BE §12 item 1. A§6.4 Part D: "`clearAll` … sparing the boot record's device
  fields". Challenge: the library cannot know the boot record, and deleting `tm-app` blocks. Proposal:
  `clearAll({ keep })`; 0025 keeps `tm-app` and `tm.boot`. Status: Deviated. Interactions: R-0010BE-3
  (0025 contradicts); 0024 §6 and §8.4 step 6 call `clearAll()` expecting the skeleton's behaviour
  (M-15).
- **C-0010BE-2** — 0010-BE §12 item 2. A§6.4 Part D: "live `direction`" on `TmModal`. Proposal: a live
  root `Directionality` watching `<html dir>` (Part B, B.3). Status: Deviated. Interactions: 0024
  §4.4 renders a `[dir]` wrapper in `tm-app` and calls it "0010 Part D's live-direction host" (M-16).
- **C-0010BE-4** — 0010-BE §12 item 4. A§6.4 Part B cites "0002 §4, §8" (§8 is the budget). Proposal:
  §4 and §1. Status: Followed (citation fix). Interactions: none.
- **C-0033-1** — 0033 §12 item 1. A§6.4 Part F "tree-rollup grouping key over the dimension's node
  closure (`IsDescendantOf`), the only fan-out allowed". Challenge: unsound under grouping sets
  (grand total 102,000 instead of 52,000). Proposal: level navigations (F2). Status: Followed in
  name, deviated in mechanism. Interactions: duplicates C-0010F-1; affects A§4.2 row "tree rollups".
- **C-0010F-1** — 0010-F §12 item 1. Same A§6.4 text. Proposal: level navigations (F.9). Status:
  Followed in name, deviated in mechanism. Interactions: duplicates C-0033-1 (agrees).
- **C-0033-2** — 0033 §12 item 2. A§6.4's Part F list is incomplete. Proposal: add `Exists` (F8),
  parameter expressions (F4), parameter binding for drill text (F6); `'uq'` must emit join-shaped.
  Status: Completed. Interactions: duplicates C-0010F-2; their details diverge (M-75).
- **C-0010F-2** — 0010-F §12 item 2. Same. Proposal: F.11, F.13, F.15. Status: Completed.
  Interactions: duplicates C-0033-2.
- **C-0010F-3** — 0010-F §12 item 3. A§6.4 counts "context slots" among seven engine amendments.
  Proposal: a host binding rule; F.5 records no engine change. Status: Followed (recorded).
  Interactions: none.
- **C-0010F-4** — 0010-F §12 item 4. A§6.4/A§10.1 name `EvaluateConstant`, but the engine never
  evaluates. Proposal: `CompileConstants`; kept the name (flag 10). Status: Followed. Interactions:
  0033 uses `EvaluateConstant` (consistent today).

### A§6.6 SPA hosting rules, CSP, worker

- **C-0024-1** — 0024 §12 item 1. A§6.6 CSP `require-trusted-types-for 'script'` vs A§7.1 step 6
  `registerWhenStable:30000`. Challenge: Angular registers the worker with a string, which Trusted
  Types refuses. Proposal: custom registration through `registrationStrategy` with a `TrustedScriptURL`
  from a shared `tellma` policy. Status: Deviated (kept the CSP). Interactions: the `tellma` policy is
  requested of 0010 Part B in 0024 §6 (0010-BE lacks; 0023 needs no policy, M-17).
- **C-0026-2** — 0026 §12 item 2. A§6.6 `/support` in the `on-demand` group. Proposal: prefetch it
  (matters offline); followed on demand with Copy diagnostics in the primary. Status: Followed.
  Interactions: none.

### A§7 Data flows

- **C-0025-4** — 0025 §12 item 4. A§7.3 step 2 (`ifAvailable`, "wait for its `store.changed`").
  Challenge: lost wake-ups. Proposal: queued lock plus re-check inside it. Status: Deviated.
  Interactions: none.
- **C-0025-1** — 0025 §12 item 1. A§7.4 "Name-only events (… `preferences` and user-level
  `permissions` to one user) always refresh" and the A§6.3 `cache.changed` row. Proposal:
  user-addressed events carry the user's wire tag. Status: Deviated (E1). Interactions: none.
- **C-0028-5** — 0028 §12 item 5. A§7.5 banner text "A new version of Tellma is available". Proposal:
  "A new version is available." (branding, length at 390 px). Status: Deviated. Interactions: 0024
  §5.5 keeps the skeleton text (M-2); C-0023-5 (one-line banner copy).
- **C-0025-3** — 0025 §12 item 3. A§7.8 steal rule ("or leader not connected for 30 s"). Challenge:
  leadership ping-pongs between two visible windows. Proposal: `leaderVisible` on `hub.status`, a
  `disabled` state; steal a disconnected leader only when it is hidden. Status: Deviated.
  Interactions: 0028 §5.2/§6.2 read `TmLeader.hubStatus().state` with `disabled` (consistent).
- **C-0025-9** — 0025 §12 item 9. A§7.8 "Realtime disabled" with no client signal. Proposal:
  `ClientSyncResult.Realtime` and `TmTenantFacts.realtime`. Status: Completed. Interactions: none.

### A§8 Route grammar and query state

- **C-0028-3** — 0028 §12 item 3. A§8 spells one concept twice (`?mine=1` for Jobs, `?tab=to-me|by-me`
  for Assignments). Proposal: `?tab=mine|all` for Jobs; followed the skeleton through a `param` on
  0030's `tabs`. Status: Followed. Interactions: conflicts with C-0030-4 (`x.mine=1`); R-0030-R15
  (0028 contradicts); M-7.
- **C-0028-4** — 0028 §12 item 4. A§8 Assignments `?tab=to-me|by-me`. Proposal: add `all`. Status:
  Completed. Interactions: none.
- **C-0030-1** — 0030 §12 item 1. A§8 and A§5.13 `view: 'grid' | 'tiles' | 'tree' | 'list'`.
  Challenge: two independent axes (grid or tiles; tree or list). Proposal: `view=grid|tiles` plus
  `list=1`. Status: Deviated. Interactions: C-0030-2; R-0030-R4 (0024 contradicts, M-5); 0026 §9.3
  search state lists `view` only (R-0030-R12).
- **C-0030-4** — 0030 §12 item 4. A§8 `/{t}/jobs?mine=1` is outside the query grammar. Proposal:
  `x.mine=1`. Status: Deviated (requested). Interactions: conflicts with C-0028-3; M-7.
- **C-0030-8** — 0030 §12 item 8. A§8 precedence "URL, then page state, then preferences". Proposal:
  the URL query applies as a whole, not field by field. Status: Followed (interpretation).
  Interactions: 0034 §4.5 and 0035 §10.3 apply the same rule to `p.*`; C-0031-7.
- **C-0031-7** — 0031 §12 item 7. A§8 history (query changes replace). Challenge: Back lands on the
  old page's entry after paging across pages. Proposal: `TmSearchSnapshot.supersedes` adopted by 0030.
  Status: Completed (requested). Interactions: R-0031-R19 (0030 lacks), R-0031-R20 (0030 §15.3
  contradicts the read rule, M-49).
- **C-0033-4** — 0033 §12 item 4. A§8 reports page `?tab=mine|shared|all` cannot be expressed in
  client Queryex. Proposal: `reports/browse`. Status: Completed (route added). Interactions: C-0034-2.
- **C-0034-2** — 0034 §12 item 2. Same A§8 parameter. Proposal: `x.tab` through a 0030 read hatch
  (`searchRead`). Status: Deviated. Interactions: C-0033-4; R-0034-R3 (0030 lacks/contradicts);
  M-8.
- **C-0034-3** — 0034 §12 item 3. A§8 drill list "the filter shows as read-only chips; no free text
  from the URL is rendered". Proposal: chips from navigation state, else discover's path labels.
  Status: Deviated. Interactions: none.
- **C-0034-6** — 0034 §12 item 6. A§8 the whole drill filter in the URL meets Kestrel's 8 KB line.
  Proposal: `?drill=state` fallback. Status: Completed. Interactions: none.
- **C-0035-5** — 0035 §12 item 5. A§8 "edit mode edits the layout". Proposal: wording (also widget
  settings, dashboard settings and parameters). Status: Followed. Interactions: none.
- **C-0029-4** — 0029 §12 item 4. A§8 `cols` rule ("a navigation means its display name") covers
  navigations only. Proposal: groups follow the same rule; a slot-1 key would change 0024's grammar
  (review flag 2). Status: Completed. Interactions: none.

### A§9 Breakpoints and layout constants

- **C-0023-4** — 0023 §12 C4. A§9 `xs` "44 px touch height for controls in page content". Challenge:
  no owner or mechanism. Proposal: 0010 Part B, default size resolves to `lg` below `sm` (0023 A8).
  Status: Completed (requested). Interactions: 0010-BE lacks A8; 0027 M4, 0029 §13.3, 0030 §3.2, 0031
  §5.2 rely on 44 px.
- **C-0023-5** — 0023 §12 C5. A§9 banner row 32 px. Proposal: a 52 px two-line row below `sm`; until
  then 0028 keeps copy short. Status: Followed. Interactions: R-0023-28 (0028 caps messages at ≈ 45
  characters, §9.1); C-0028-5.
- **C-0010BE-3** — 0010-BE §12 item 3. A§9 "Values here; tokens, `TM_BREAKPOINTS` …". Challenge:
  custom properties cannot appear in media or container queries. Proposal: no breakpoint tokens.
  Status: Deviated. Interactions: none.
- **C-0027-7** — 0027 §12 item 7. A§9 `xl` "pins overflow into More below this". Proposal: measured
  overflow. Status: Deviated. Interactions: none.
- **C-0028-6** — 0028 §12 item 6. A§9 input-idle gate. Proposal: a hidden page applies at once; the
  host announces at queue time. Status: Completed. Interactions: R-0028-23 (`announce` input, 0023
  lacks).
- **C-0035-6** — 0035 §12 item 6. A§9 "components use container queries". Proposal: none (the grid
  goes narrow on its own width). Status: Followed. Interactions: none.

### A§10.1 Open questions

- **C-0026-8** — 0026 §12 item 8. A§10.1 Observability Q1 "every item carries … the last trace ids".
  Proposal: only error items carry the last three; request items their own. Status: Deviated.
  Interactions: none.
- **C-0031-1** — 0031 §12 item 1. A§10.1 Non-aggregate Q1 "opens the tab whose `tm-child-grid` binds
  `Lines`". Challenge: tab content is lazy. Proposal: `tm-details-tab collection="Lines"`. Status:
  Deviated. Interactions: 0031 §6 Part D `prerender` request (0010-BE lacks).
- **C-0031-5** — 0031 §12 item 5. A§10.1 Details Q3 "no `beforeunload` prompt". Proposal: armed only
  while a save is in flight. Status: Completed (refinement). Interactions: none.
- **C-0035-4** — 0035 §12 item 4. A§10.1 Dashboards Q1. Proposal: fixed-value bindings and no
  dashboard defaults. Status: Completed. Interactions: none.

### A§11 Conventions, gates, budgets

- **C-0024-3** — 0024 §12 item 3. A§11 "Only `TmApi` handles 401, step-up, 412 and 503 retries".
  Proposal: `TmApi` classifies; `TmErrorPresenter` asks once, then redirects. Status: Deviated.
  Interactions: none.
- **C-0024-6** — 0024 §12 item 6. A§11 gates row (warm chrome ≤ 300 ms under Lighthouse). Challenge:
  Lighthouse resets storage. Proposal: a Playwright nightly with CPU throttling. Status: Deviated.
  Interactions: none.
- **C-0031-6** — 0031 §12 item 6. A§11 details route 80 KB vs an editable grid (~57 KB) plus `/entity`
  (28 KB). Proposal: defer the grid inside `tm-child-grid`; raise the budget if 0024 counts deferred
  chunks. Status: Followed. Interactions: 0024 §12 counts "the closure of the route host's dynamic
  import … plus the distribution's layout chunks" (deferred chunks not stated).

### A§12 Risks

- **C-0034-7** — 0034 §12 item 7. A§12 #24 (Trusted Types with ECharts). Proposal: settled by design
  (no HTML component, canvas renderer). Status: Completed. Interactions: C-0024-1 (TT policy).

### A§14 Orchestrator rulings

- **C-0010BE-5** — 0010-BE §12 item 5. A§14 ruling 3 ("A new `@tellma/core-ui` entry point is 0023's").
  Challenge: `@tellma/core-ui/overlay` is new but holds existing code. Proposal: "a new component is
  0023's; promoting or changing existing code is 0010's". Status: Deviated (0010-BE D.1 ships it).
  Interactions: 0023 still imports the helpers from `private` (M-14); 0034 §6 relies on ruling 3 for
  `tm-pivot` reusing `tmComputeAxisWindow`.

## Part B — Requests between round-2 specs

**IDs.** Notes that number their requests keep their numbers: `R-0030-R4` is 0030's R4 (0030,
0031, 0032, 0033, 0034, 0035 number theirs; 0010-BE uses R-1 … R-5). Notes whose request table has
one row per target (0023, 0024, 0025, 0026, 0027, 0028, 0029) get the target's last two digits as
the row number, and a numbered sub-item in parentheses: `R-0024-25(3)` is 0024's row to 0025, item
(3); a row to several targets takes the first (`R-0026-32` is the row to 0032, 0034, 0035). 0010-F's
two requests are `R-0010F-1` (to 0033) and `R-0010F-2` (to 0029).

**Status.** **Provided** (the target note states it; § quoted), **Partial**, **Contradicts** (the
target designs something incompatible), **Lacks** (not in the target note).

### To 0010-BE (Parts B–E)

- **R-0023-10** (0023 §7, row 0010-BE) — apply amendments A1–A8 (0023 §6).
  - A1 `TmModalConfig.role` / `describedBy` for an `alertdialog` confirm — **Lacks** (D.2 adds only
    `placement`).
  - A2 `tm-image placeholderOnError` — **Lacks**.
  - A3 a pure relative-time function `(instant, now, { locale; timeZone; style: 'narrow' | 'long' })`,
    "Just now" as a message key, plus `TM_TIME_ZONE` — **Partial / Contradicts**: D.4 provides
    `TM_TIME_ZONE: InjectionToken<Signal<string>>` and `tmFormatRelativeTime(instant, locale,
    { now, timeZone, calendar?, form?: 'relative' | 'age', style?: 'long' | 'short' | 'narrow' })`,
    with "now" from `Intl` and day counts up to 7 calendar days (M-13).
  - A4 `.tm-button` sets `--icon-size` for projected `tm-icon` — **Lacks**.
  - A5 `TmMenuItem.icon` accepts a registry name, `--menu-icon-size`, `shortcut` as `TmKeys` —
    **Lacks** (C.10: "existing: id, label, labelKey, icon, disabled, shortcut, action() — unchanged";
    `TmMenuLink.icon?: TemplateRef<void>`) (M-63).
  - A6 multi-selects render chosen values with `tm-chip-set overflow="collapse"` — **Contradicts**
    (B.4: "the selected labels joined with `Intl.ListFormat` … ellipsized"; E.2: "the chip row
    scrolls horizontally … a fixed-width count badge") (M-12).
  - A7 grid `rowActivate` payload shaped as `TmItemActivation<T>` — **Contradicts** (C.1
    `TmGridRowActivation { row, rowId, via, newTab }`); tree-grid `expandedIds` and batched
    `loadChildren(rows)` — **Provided** (C.9) (M-11).
  - A8 `TM_FORM_FIELD_DEFAULTS.size` resolves to `lg` below `sm` (the 44 px touch rule) — **Lacks**.
- **R-0029-10** (0029 §7, row "0010-BE, 0010-F: note section 6") — Part E: `TmEntityPickerPage`
  object form gains `context?: unknown`; optional grid `entity` column `itemDescription` —
  **Lacks** (E.2 adds only `multiple` and `ids` to `TmEntityPickerPageData`) (M-64). Part F: see the
  0010-F group.
- **R-0035-R1** (0035 §7 R1) — Part D: `TmDateStyle` `'weekday'` and `tmZonedParts(epochMs,
  timeZone)` — **Lacks** (D.4 adds date-time, time and relative formats only) (M-65).

**Section-6 amendment needs addressed to 0010-BE** (not section-7 rows; listed for completeness):

- **0024 §6 "new request"** — one lazily created Trusted Types policy named `tellma` in the core-ui
  primary, shared by 0023's icon registry and worker registration — **Lacks** (M-17).
- **0025 §6** — `clearAll` also deletes `tm-*` IndexedDB "(here `tm-app`)", keeping `tm.boot`'s
  device fields — **Contradicts** (D.5 `clearAll({ keep })`: "0025's `TmClientStore` keeps `tm-app`
  out of `clearAll`") (M-15).
- **0027 §6 M1–M5** — `TmMenuGroup.layout: 'segmented'`; `cssClass` on items, links, submenus;
  registry-name icons on links and submenus; 44 px rows below `sm`; `TM_GRID_KEYS` and
  `TM_GRID_KEY_HELP` — **Lacks** all (M-63, M-65).
- **0030 §6** — readonly grid leaves unmodified printable keys unhandled; siblings render in data
  order; `TmGridDriverTarget` moves to `@tellma/core-ui/contracts`; optional header underline while
  the action is expanded — **Lacks** (C.2 declares `TmGridDriverTarget` in `@tellma/core-ui/grid`).
- **0031 §6** — `scrollToRow(id, { flash?, column?, focus? })` — **Lacks** (C.8 has `flash`,
  `activate`, `align`); `tm-tab-group prerender: 'none' | 'idle'` — **Lacks**.
- **0034 §6** — C.12 `rowSelectable`, C.13 `footerRows`, D.7 `month`/`quarter`/`year` styles in an
  explicit calendar — **Lacks** (0010-BE's Part C ends at C.11, Part D at D.6).
- **0035 §6** — the same items as R-0035-R1.
- 0023 §6 A1–A8 are R-0023-10 above; 0026, 0028, 0032, 0033 have no section-6 need here.

### To 0010-F (Part F)

- **R-0033-R7** (0033 §7 R7) — write Part F from 0033 section 6 — **Provided** with deviations
  (0010-F §2 "Deviations from N33 §6": F1→F.8 … F8→F.15; optional `KeyGroups`; QX4009 for QX3304;
  `TOP (1)` seek instead of one `OUTER APPLY` per operand; caller errors for `Exists`) (M-75).
- **R-0029-10** (Part F half) — `ParameterUse.ComparedWith` — **Provided** (F.16: "segments of the
  one path the parameter meets; null otherwise").

### To 0023 (Component Library Additions)

- **R-0010BE-1** (0010-BE §5.1) — ship `tm-chip` (label, `removable`, `remove`, focusable, ladder
  heights) and the chip-row keyboard — **Provided** (§3.4 `TmChip`, `TmChipSet`, "Removable chips
  keyboard"); drop the multi-select input from `chips` — **Provided** (§1 out of scope "Multi-select
  `tm-select` and multi-select entity picker: 0010 Parts B and E"); `tm-relative-time` formats
  through `TmL10n.formatRelativeTime` and owns the ticking `now` — **Partial** (§3.4 "Text comes from
  the relative-time format of the l10n closed set (0010 Part D)"; "One shared ticker"; option names
  differ, M-13); new components use container queries — **Provided** (§3.7 `TmDescriptionList`
  "auto: 2 at container ≥ sm"); new components use `@tellma/core-ui/overlay` — **Contradicts** (§3.1
  `toast`, `radio`, `segmented` depend on `private`; §3.2 "`tmPushEscapeDismissal`, 0005 §2.1")
  (M-14).
- **R-0024-23** (0024 §7, row 0023) — `error-dialog` content with message, items, reference chip
  with Copy, Details disclosure, count badge — **Partial** (§3.5 `TmErrorDialogOptions { title,
  message, details?, reference?, retry? }`: no Details disclosure, no count) (M-18); `confirm` —
  **Provided** (§3.5 `TmConfirm`); `busy-overlay` — **Provided** (§3.5 `TmBusyOverlay`);
  `empty-state` slots for `tm-error-state` — **Provided** (§3.5 "content: detail text;
  [tmEmptyStateActions]: buttons; a tm-reference may sit in the detail"); `skeleton` and
  `page-header` geometry — **Provided** (§3.4, §3.7).
- **R-0026-23** (0026 §7, row 0023) — `tmKeysMatch` matches letters and digits by `event.code` when
  `event.key` is one non-ASCII character — **Lacks** (§3.3 "Matching compares `event.key`"); an
  inline banner variant (tone warning) usable inside a dialog — **Lacks** (`tm-banner-bar` is
  "Exactly 32 px (the shell's banner row)") (M-67).
- **R-0027-23** (0027 §7, row 0023) — (1) `tmKeysMatch` by `event.code` when the modifiers include Alt
  on Apple platforms — **Lacks**; (2) the chip tokens `--chip-*` are public — **Partial** (§3.9 group
  `chip` emits `--chip-*`; public status not stated).
- **R-0028-23** (0028 §7, row 0023) — `tm-banner-bar` gains `announce: InputSignal<boolean>` —
  **Lacks** (§3.5 inputs `tone`, `message`, `icon`, `actionLabel`, `dismissible`, `position`).
- **R-0030-R1** — `tm-tiles` implements `TmGridDriverTarget` — **Lacks** (§3.8 `TmTiles` has
  `activeId`, `activate`, `scrollToItem`, `focus`).
- **R-0030-R2** — `tm-tiles` `scrollOffset()` and `scrollTo(offset)` — **Lacks**.
- **R-0030-R3** — `tm-pager` `disabled` with geometry kept — **Lacks**.
- **R-0031-R1** — `TmBusyOverlay.run`'s label can change while it runs — **Lacks** (§3.5 `run(work,
  { label })`, "nested runs share one overlay").
- **R-0031-R2** — `tm-page-header` keeps an empty slot's width — **Lacks** (§3.7 "`TmPageHeader {}`
  … no inputs").
- **R-0031-R3** — `tm-pager mode="item"` accepts a capped total ("52 / 10,000+") — **Lacks**
  (`totalIsLowerBound` is documented for range mode: "count cap hit: 1–20 / 1,000+").
- **R-0032-R13** — `tm-signature-pad` — **Lacks**.
- **R-0032-R14** — `tm-chips` free entry (Enter or comma adds; `parse`) — **Contradicts** (§12 C1:
  "no round-2 consumer needs a free-text chip input"; no `tm-chips` selector exists) (M-66).
- **R-0034-R5** — `tm-skeleton` shapes for a table, a pivot block and a chart frame — **Lacks**
  (`shape: 'line' | 'block' | 'circle'`); a read-only `tm-chip` with a "+n" overflow popover —
  **Partial** (`tm-chip-set overflow="collapse"` with a "+N" chip and tooltip) (M-68).
- **R-0035-R7** — `tm-card` gains `[tmCardSubtitle]` — **Lacks** (§3.7 slots `[tmCardActions]`,
  body, `[tmCardFooter]`) (M-68).

### To 0024 (Client App Foundation)

- **R-0010BE-2** (0010-BE §5.1) —
  - `tm/allowed-breakpoints` in the `@tellma/core-tools` stylelint preset — **Provided** (§1
    "`@tellma/core-tools/stylelint` (includes 0010 Part B's breakpoint rule)").
  - `<meta name="color-scheme" content="light dark">` in `index.html` — **Lacks** (§2.1's file has
    `theme-color` only).
  - `tm-boot.js` writes the effective `data-theme` — **Provided** (§4.2 "`data-theme` is `light` or
    `dark`").
  - `provideTellmaUi({ translations, theme: { initial } })` — **Provided** (§2.2 translation loader;
    §7.6 "`TmTheme` … is seeded from `boot.theme`").
  - await `load(lang)` before `setActiveLang`, flip `<html dir lang>` — **Provided** (§7.2 steps 2–3).
  - `TM_ACTIVE_LOCALE`, `TM_CALENDAR` — **Provided** (§7.1 Providers); `TM_TIME_ZONE` — **Lacks**
    (§7.1 has `TmLocalization.timeZone` but no token) (M-29).
  - `TM_BLOB_FETCHER` forwarding `signal` — **Provided** (§10.5).
  - update `<meta name="theme-color">` from `TmTheme.effective` — **Lacks**.
  - budget the search route knowing grid + engine ≈ 57 KB — **Provided** in effect (§12 "search
    route 110 KB").
- **R-0023-24** (0023 §7, row 0024) — `TmFeatureDefinition.icons` merged into `TmIconRegistry`, and
  the platform's own icon registrations — **Lacks**; fill `TM_NOW` and `TM_TIME_ZONE` — **Lacks**;
  call `provideTellmaToasts` with 0028's overflow action — **Lacks**; include
  `styles/tm-description-list.css` in `@tellma/core`'s styles — **Partial** (§7.6 "imports … the
  directive stylesheets"); `@tellma/core-testing`'s fake clock replaces `TM_NOW` — **Partial** (§1
  "fake clock"; token not named) (M-27, M-29).
- **R-0025-24** (0025 §7, row 0024) —
  - (1) `tm-boot.js` deletes by the frozen layout with the `oldVersion = 0` abort — **Partial** (§4.2
    "through 0025's frozen key layout"; the abort not stated).
  - (2) §8.4 step 6 runs `sweep('session-ended')` before `clearAll()` — **Provided** (§8.4 step 6
    order).
  - (3) `tenant-not-found` presentation calls `sweep({ tenant: t })` — **Lacks** (§11 "navigates to
    `TmUrls.picker({ notice, from })` and clears `lastTenantId`").
  - (4) Acme declares a cacheable `acme.Currency` and the key `acme.bankReconciliation.account` —
    **Lacks** (E21 lists `acme.Customer`, `acme.SalesInvoice`, `MyCenter`, `acme.AgingReport`).
  - (5) fold E16 (Hub group renews nothing) into E8(d) — **Lacks** (E8(d) "no renewal on
    `Tellma-Activity: background`").
- **R-0026-24** (0026 §7, row 0024) —
  - (1) `TmApi` calls `TmRequestLog.begin` at send and `end` at settle — **Contradicts** (§10.3 step 5
    "a record → `TmRequestLog`") (M-19).
  - (2) `TmApi.upload(kind, blob, { fileName, signal?, onProgress? })` — **Lacks** (M-21).
  - (3) `provideBrowserGlobalErrorListeners()` and `TmErrorHandler` as `ErrorHandler` — **Partial**
    (§2.2 "browser global error listeners"; `TmErrorHandler` not named).
  - (4) `TmErrorPresenter` calls `TmTelemetry.error`; 5xx Details adds "Copy diagnostics" and "Report
    this problem" — **Lacks** (§11 Details lists status, code, time, route template, UI version).
  - (5) the route host provides `provideTmPageScope()` and registers `custom` with `status: 'ready'`
    — **Partial** (§2.4 "it publishes a `custom` page context with the title from `titleKey`").
  - (6) sign-out calls `TmTelemetry.flush()` before `/bff/logout` — **Lacks** (§8.4 steps 3–4).
  - (7) hidden source maps archived per UI version — **Lacks**.
  - (8) `TmUpdates` exposes the controlling worker's version — **Lacks** (§5.5 `enabled`,
    `available`, `check`, `reload`) (M-33).
  - (9) `TmApi.send` takes `{ background: true }` — **Contradicts** (§10.1 `send(route, body): void`;
    §10.2 `Tellma-Activity` "reads with `background: true`") (M-20).
- **R-0027-24** (0027 §7, row 0024) —
  - (1) shells through `loadComponent: () => import('@tellma/core/shell')` plus `modulepreload` —
    **Lacks** (§4.4 "Shells are route components (0027)"; §2.6 `build` injects font and metadata
    preloads only).
  - (2) the `:t` route carries the tenant shell's providers, `TM_REALTIME` among them — **Lacks**.
  - (3) primary token `TM_APP_DECLARATIONS` — **Lacks**.
  - (4) `tmPage` and feature `entries` take `order`, `icon`, `synonymsKey`; `TmShellSlot.badge` as a
    factory — **Lacks** (§2.2 "`TmFeatureDefinition`, `tmFeature`, `tmPage` are A§5.1") (M-24, M-27).
  - (5) config problem `app-bar-reserved-id` — **Lacks** (M-26).
  - (6) `TmLocalization.load(code)` and `translate(key, code)` — **Lacks**.
  - (7) `TmCalendarDefinition.labelKey` — **Lacks** (§2.2 `{ code, load }`).
  - (8) `TmClientMetadata.distribution.copyright` — **Lacks** (M-28).
  - (9) `TmErrorPresenter` shows the tenant-state page through `TmShellMain.replace(…)` — **Partial**
    (§11 "renders the tenant-state page in the tenant shell's `main`").
  - (10) platform icon registrations include the shell's names — **Lacks**.
- **R-0028-24** (0028 §7, row 0024) — (1) `TmServerText.message(key, args)` and `has(key)` —
  **Lacks** (§7.5 `problem(problem)`, `error(error, entity?)`) (M-32); (2) call
  `provideTellmaToasts({ maxVisible: 3, overflow })` — **Lacks**; (3) `TmUrls.target` covers
  `core.Job`, `core.Export`, `core.Import`, `core.Schedule`, `core.OutgoingEmail` — **Provided**
  generically (§9.4 "details when the entity has a details page, else null"); (4) E18 lists
  `assignments/summary` — **Provided** (E18).
- **R-0029-24** (0029 §7, row 0024) — (1) codes `unknown-path`, `unknown-action`,
  `operation-missing`, `entity-not-routable`, and the config spec imports the Entities chunk —
  **Lacks** (§2.2 code list; §3.6 imports "the Core chunk") (M-26); (2) the route host awaits
  `TmEntities.load()` — **Partial** (§4.4 "takes the descriptor from `TmEntities`"; no await) (M-22);
  (3) `TM_ENTITY_DECLARATIONS`, `TM_ENTITY_PAGES` — **Lacks** (M-29); (4) `TmUrls.blob(kind, id,
  { variant?, download? })` — **Lacks** (M-31); (5) `TmApi.upload` — **Lacks**; (6) Acme E21 additions
  (`[DisplayNameSource]` on `acme.SalesInvoice`, `ImageId` blob, `CustomerType` enum, cacheable
  `acme.Currency`, labels) — **Lacks**; (7) `label-missing` warnings — **Lacks**.
- **R-0030-R4** — split `view` into `view?: 'grid' | 'tiles'` and `list?: boolean` — **Contradicts**
  (§9.5 "**`view`**: `grid | tiles | tree | list`") (M-5).
- **R-0030-R5** — `x.{key}` free strings; canonical order of `f.`, `x.`, `cols` — **Provided** (§9.5
  "Order … `x.*` by key"; "Values are culture-free").
- **R-0030-R6** — the entity search route loads `TmSearchHost`, which resolves the `search` hatch —
  **Contradicts** (§4.4 "loads the search host, the details host, or the declaration's `search` or
  `details.page` hatch") (M-30).
- **R-0030-R7** — `TM_ENTITY_PAGES.search = TmSearchPickerPage` — **Lacks** (M-29).
- **R-0030-R8** — the `search` skeleton matches 0030 §3's rows — **Provided** in principle (§4.4 "a
  title bar, one toolbar row and a body block whose geometry the search and details hosts match").
- **R-0030-R9** — `TmApi.upload` with `signal` and `onProgress` — **Lacks**.
- **R-0030-R10** — the primary exports `TmTileDeclaration`, `TmCustomFilterDeclaration`,
  `TmSearchSnapshot` — **Lacks**.
- **R-0031-R4** — router option `canceledNavigationResolution: 'computed'` — **Lacks** (§9.2 Router
  options list).
- **R-0031-R5** — the `details` skeleton matches 0031 §5 — **Provided** in principle (§4.4).
- **R-0031-R6** — `TM_ENTITY_PAGES.details = TmDetailsPickerPage` — **Lacks**.
- **R-0031-R7** — `TmApi.upload` and `TmUrls.blob` — **Lacks** (M-21).
- **R-0031-R8** — `present` with `kind: 'save'`/`'action'` covers 400, 413, 403, 401 step-up, 412,
  offline, 5xx; `describe` for reads — **Provided** (§10.6 table; §11 "`present` does what §10.6's last
  column says").
- **R-0032-R2** — route data for `settings`, `settings/:category`, `me` — **Partial** (§9.2 lists
  "'settings', 'settings/:category', 'me', 'me/notifications', 'list/:segment' core-feature pages");
  `TmUrls.settings(category?)`, `TmUrls.me(sub?)` — **Lacks** (M-31); `TmUrls.target` for the six
  stock entities — **Provided** generically; a ruling on dynamic imports of lazy entry points from the
  primary — **Lacks**.
- **R-0032-R3** — `TmClientMetadata.settingCategories` and `jobHandlers`; `unknown-setting-category` —
  **Lacks** (M-26, M-28).
- **R-0033-R5** — `TmUrls.target('core.ReportDefinition', id)` → `/{t}/reports/{id}`; exports through
  `TmApi.download` — **Provided** (§9.4 `target`; §10.1 `download`).
- **R-0034-R2** — (a) `TmRouteData.skeleton` adds `'report'` — **Lacks** (§4.4 `'search' | 'details' |
  'blank'`) (M-25); (b) config problem `coded-report-source` — **Lacks**; (c) `TmUrls.describe`
  classifies `/reports/{key}` and `/list/:segment` — **Lacks** (§9.4 `TmRouteMatch.kind` has no report
  or list kind); (d) the primary exports `tmCodedReport` — **Provided** by A§2.2 (not restated in 0024).
- **R-0035-R5** — (a) `TmFeatureDefinition.widgets`; `tm/lazy-layouts` covers `component` and
  `settingsEditor` — **Lacks** / **Partial** (§2.1 rule covers "`details`, `component`, `search`,
  `home`, `page`"); (b) skeletons `'home'` and `'dashboard'` — **Lacks**; (c) core metadata
  `widgetTypes`, problems `widget-missing`, `widget-not-registered` — **Lacks**; (d)
  `TmUrls.target('core.Dashboard', id)` — **Provided** generically; (e) the tenant `''` route loads
  `TmHomeHost`, which picks the hatch — **Partial** (§9.2 "`''` home — 0035's default, or the last
  feature's `home`": 0024 picks).

### To 0025 (Client State and Cross-Tab Sync)

- **R-0010BE-3** (0010-BE §5.1) — `sweep('session-ended')` calls `clearAll({ keep })` —
  **Contradicts** (0025 §6: `clearAll` "also deletes `tm-*` IndexedDB databases (here `tm-app`)")
  (M-15); `TmClientStore` closes its connection on `versionchange` — **Provided** (§2.2 "Every
  connection closes itself on `versionchange`"); `TmTreeExpansion` accepts whole-set writes —
  **Provided** (§12 `replace(page, ids)`).
- **R-0024-25** (0024 §7, row 0025) — (1) the key layout frozen — **Provided** (§2.2 "Layout
  (frozen: `tm-boot.js` deletes by it)"); (2) `sessionStorage['tm.login']` belongs to `tm-boot.js` —
  **Provided** (§11 "`sessionStorage` holds only drafts and `tm.login`"); (3) a cold boot with no
  `boot.sub` sweeps everything but the device partition — **Provided** (§2.6 step 2); (4) on
  `tenant-not-found` delete that tenant's partition — **Provided** (§2.6 `{ tenant: t }` "0024
  request 4"; clearing `lastTenantId` stays the caller's); (5) `TmDrafts.clearAll(sub)` — **Provided**
  (§11); (6) the bus messages — **Provided** (§6.2); (7) `persist()` belongs to the store —
  **Provided** (§2.7).
- **R-0026-25** (0026 §7, row 0025) — `tabId` from the first injection — **Provided** (§6.1
  "`crypto.randomUUID()` per document load"); the `distribution` document as a signal — **Provided**
  (§3.1 `distribution: Signal<…>`); `isLeader`, `hubStatus` readable — **Provided** (§7.1).
- **R-0027-25** (0027 §7, row 0025) — (1) `nav.pinned` = `TmPinsDoc`, fallback `{ v: 1, items: [] }`,
  no tenant default — **Contradicts** (Appendix A: value "0027's pins model", fallback "the tenant's
  default pins setting when 0027 declares one, else `[]`") (M-61); (2) the locale adoption patches
  `User.PreferredLanguage`, `PreferredCalendar`, `PreferencesTag`, `Tags.preferences` — **Provided**
  (§9.5 "patching `me.User.PreferredLanguage` or `PreferredCalendar` (request to 0027)"); (3)
  `revalidateUser()` on `/tenants` — **Provided** (§2.3 "on a tenantless route"; §3.1).
- **R-0028-25** (0028 §7, row 0025) — (1) Appendix A row `notifications.toasts` — **Lacks** (M-62);
  (2) a non-leader tab may `put` a leader document it read — **Partial** (§8.6 "A page that opens a
  panel may read fresher data itself (0028's choice)"; the §2.5 guard does not restrict it); (3)
  `savedAt` set at `put` time on the writer's clock — **Partial** (the writer supplies `savedAt` in
  `TmStoredDoc`; §2.4 defines it as "last written or last confirmed").
- **R-0029-25** (0029 §7, row 0025) — nothing beyond R-0025-29; `TmDisplayNames` clears a tenant's
  cache "on the same tag moves as page state" — **Provided** as the rule it leans on (decision 19:
  "a tenant's entries drop when its `permissions` or `settings` tag moves"; 0025 itself puts
  display names out of scope, §1).
- **R-0030-R11** — Appendix A values `search.<entity>.columns` = `TmColumnPreference[]`, `.view` =
  `{ narrow?, wide?, list? }`, `.pageSize` ∈ 20, 50, 100, 200 — **Contradicts** for `columns` and `view`
  (Appendix A "`string[]` of paths"; "`'grid' | 'tiles' | 'tree' | 'list'`"); `pageSize` **Provided**
  (number) (M-6).
- **R-0031-R9** — `TmDrafts.load` parses with the lossless parser — **Lacks** (§11 "Stored value
  `{ v: 1, at, data }` as JSON").
- **R-0031-R10** — `markStale({ tenantId, entity })` marks details entries too; `TmDetailsSnapshot` is
  a page-state value — **Partial** (§10 "entries for that entity's pages restore query, scroll and
  selection and refetch rows"; the consumers table lists details).
- **R-0032-R9** — `TmCacheRefresher.adopt(tenantId, { settings?, me? })` — **Lacks** (§5.3 has
  `observe`, `refresh`, `lastSyncAt`; adoption is internal to §9.5 for preferences).
- **R-0034-R6** — (a) `report.<id|key>.params` = `{ v: 1, args }` with non-defaulted values —
  **Partial** (Appendix A row exists: "parameter values", fallback `{}`); (b) `report.studioPanel` —
  **Lacks** (M-62).
- **R-0035-R2** — (a) Appendix A row `dashboard.<id>.params` — **Lacks** (first segment `dashboard`
  is reserved, no row) (M-62); (b) reserve the launch-document name `home` — **Lacks** (`TmLaunchDocs`
  has no `home`; `home` is reserved as a preference segment only).

### To 0026 (Client Observability, Support and Page Context)

- **R-0024-26** (0024 §7, row 0026) — `TmRequestLog` fields as N24 §10.3 step 5 — **Contradicts**
  (§8.1 `begin`/`end`, `pending`, plus `tenantId`, `method`, `background`, `code`) (M-19); UI version
  meaning — **Provided** (§7 "the distribution's informational version"); the 0024 events in the
  catalogue — **Partial** (§3.2 "Each spec lists its own events; 0026 governs the names"; A.2 maps
  `tm.launch`, `tm.boot`, `tm.config`, `tm.sw`, `tm.api`, `tm.session`, `tm.l10n` to 0024);
  `TmContextError` from error dialogs — **Provided** (§9.2 "the 0024 error dialogs register an overlay
  context carrying their `TmContextError`").
- **R-0023-26** (0023 §7, row 0026) — the request log and "Copy diagnostics" use `tm-copy-button`
  and `tm-reference` — **Provided** (§1 consumes "`tm-copy-button`, `tm-reference`" from 0023; §8.4
  "the Reference cell is a `tm-reference` chip"; §12.2, §12.5 use `tm-reference` chips). Copy
  diagnostics itself is a button in §8.4; `tm-copy-button` is not named there.
- **R-0025-26** (0025 §7, row 0026) — a state-dump contributor seam with 0025's names-and-tags block —
  **Lacks** (§10.1 `TmSupportDump` is a fixed shape; it carries `session.leader`, `hub` and
  `launch.documents` itself) (M-53); events in the catalogue — **Partial** (A.2 maps `tm.store`,
  `tm.sync`, `tm.realtime`, `tm.prefs` to 0025).
- **R-0027-26** (0027 §7, row 0026) — (1) catalogue `tm.shell`, `tm.launcher`, `tm.shortcut` —
  **Partial** (A.2 area owners); (2) nothing else.
- **R-0028-26** (0028 §7, row 0026) — catalogue `tm.attention.*` — **Partial** (A.2); a state-dump
  contribution `{ open, bell, tray, banners, armed }` — **Lacks** (M-53).
- **R-0029-26** (0029 §7, row 0026) — events in `tm.entity`, `tm.queryex` — **Partial** (A.2);
  `TmVisibleData.columns[].label` = `TmColumnPlan.label` — **Provided** (§9.1 columns `{ path, label }`
  "as displayed"); `TmFormContext.fields[].labelKey` from descriptors — **Provided** (§9.1
  `TmFormField { path, labelKey, label, … }`).
- **R-0030-R12** — the `search` row: `state` = `{ query, view, list, selectedIds, selectedCount }` —
  **Partial** (§9.3 "`{ query: TmSearchQuery, view, selectedIds (≤ 200), selectedCount }`" minimum;
  `list` absent) (M-52).
- **R-0031-R11** — `details` state gains `dirty` and `child`; PascalCase paths with indexes; action
  ids; `tm.details` events — **Partial** (§9.3 "`{ id, mode, tab? }`"; paths **Provided**: "'Name',
  'Lines[2].Quantity'"; events per spec).
- **R-0032-R15** — the `settings` row `{ category, mode, dirty }`; prefixes `settings.*`, `admin.*`;
  `tm.admin`; overlay contexts publishing no values — **Partial** (§9.3 settings "`{ category, mode }`";
  `settings.*` listed, `admin.*` not (M-51); §9.6 `includeData: false` default covers "no values").
- **R-0034-R7** — the `report` and `studio` rows, action ids, events — **Provided** (§9.3 rows
  `report`, `studio`, owner 0034).
- **R-0035-R9** — `home` and `dashboard` rows as 0035 completes them — **Contradicts** for `dashboard`
  (§9.3 "`{ dashboard: number, parameters, widgets: number }`" vs 0035's `widgets: [{ key, type, title,
  status, executedAt }]`); `home` **Provided** (minimum `{}`) (M-52).

### To 0027 (Shell, Launcher and Tenant Picker)

- **R-0010BE-5** (0010-BE §5.1, consumption) — menus and overlay — **Provided** (§10 "`tm-menu` (BE
  C.10)"; §9.1 "opening pushes `tmPushEscapeDismissal` (BE D.1)").
- **R-0023-27** (0023 §7, row 0027) — set `--toast-offset-block-start` — **Provided** (§5.1 CSS);
  bind a shortcut to `TmToasts.focus()` — **Provided** (§4.3 `tm.toasts.focus` `Alt+Shift+T`); use
  `TmKeys`, `tmKeysMatch`, `tm-kbd` — **Provided** (§4.1–§4.3); compose the app bar from `tm-icon`,
  overlay `tm-counter` with `--counter-halo`, `tm-avatar`, `tm-progress-ring` — **Provided** (§7.2,
  §7.4; the ring is 0028's bell); render banners with `tm-banner-bar` — **Provided** through 0028's
  `<tm-banner-host />` (§5.2).
- **R-0024-27** (0024 §7, row 0027) — picker notice — **Provided** (§6.2 "Notice"); signed-out page —
  **Provided** (§6.3); tenant-state page slot — **Provided** (§7.5 `TmShellMain`); call `signOut`,
  `setLanguage`, `setCalendar`, `TmTheme.set`, `TmUrls.preload` — **Provided** (§10.1, §6.2, §8.4);
  link My account per Part A — **Provided** (§10.1 row 3); the menu lists `TmLocalization.languages()`
  — **Provided** (§10.1 row 4).
- **R-0025-27** (0025 §7, row 0027) — (1) the tenant shell's route providers supply `TM_REALTIME` —
  **Provided** (§7.5 "Route providers … `TM_REALTIME`", asked of 0024); (2) the launcher renders
  `TmRecents.pages`; `url(t)` canonical — **Provided** (§9.3 band 2; §3.1 "canonical path"); (3)
  `users/me/locale` answers `{ preferencesTag }` — **Provided** (§12.1 `MeLocaleResult(…,
  PreferencesTag)`); (4) the pins model under `nav.pinned` with its admin-default fallback —
  **Contradicts** (decision 13 "No tenant default and no role seeds for pins in v1") (M-61).
- **R-0026-27** (0026 §7, row 0027) — "Report a problem" row — **Provided** (§10.1 row 10); About row
  trailing `TmAppVersion.display` — **Provided** (row 9); About dialog versions with Copy, "server
  differs" note, Diagnostics tab — **Provided** (§11.1); reserve `Mod+Alt+Shift+D` — **Provided**
  (§4.3); the Shortcuts dialog lists `top` actions — **Provided** (§11.2 "This page"); `helpKey` on nav
  entries — **Provided** (§3.1).
- **R-0028-27** (0028 §7, row 0027) — (1) accepted items — n/a; (2) read `--tm-banner-height` from
  `TmBanners.visible` — **Partial** (§5.1 "The shell writes `--tm-banner-height` … when 0028's host
  reports a banner") (M-58); (3) reconsider Jobs under Administration — **Contradicts** (§3.2 core
  navigation table keeps Jobs under `administration`).
- **R-0029-27** (0029 §7, row 0027) — launcher entries from `section`/`launcher` with labels from
  `TmEntities.label` and the gate `allows(e, 'query')` — **Contradicts** (§3.2 "Navigation members of
  `tmEntity` …: `section`, `navOrder`, `icon`, `synonymsKey`"; requires `[name, 'Read']` through
  `TmPermissions.can`) (M-3); export the search normalizer as `tmNormalizeForSearch` — **Contradicts**
  (§1 exports `tmSearchNormalize` from `@tellma/core/shell`) (M-4).
- **R-0030-R13** — §4.2 states unmodified printable keys belong to the page — **Partial** (§4.3
  refuses "keys without `Mod`, `Ctrl` or `Alt`"; not stated as a page right).
- **R-0031-R12** — page shortcuts `F2`, `Mod+S`, `Alt+PageUp`/`Alt+PageDown` — **Contradicts** (§4.3:
  `F2` has no modifier; "Pages and distributions use `Mod+<key>`, `Mod+Shift+<key>` and `Mod+Enter`";
  `F2` and Alt+arrows are grid keys) (M-59).
- **R-0032-R16** — order entries by `launcher.order`; agree member names with 0029 — **Contradicts**
  (§3.2 `navOrder`) (M-3).
- **R-0033-R6** — none.
- **R-0034-R9** — launcher entries from `tmCodedReport` (`section`, `order`, `icon`, `synonymsKey`)
  and from `tmReportDefinitions` under `analysis` — **Provided** (§3.2 table row `tmCodedReport`;
  "`analysis` … Reports `core.ReportDefinition` (0034)").

### To 0028 (Assignments and Attention Surfaces)

- **R-0010BE-5** (consumption) — drawer below `lg`, popover `md` from `lg` — **Provided** (§6.4).
- **R-0023-28** (0023 §7, row 0028) — toasts through `TmToasts.show` with `key` — **Provided** (§8.3
  "Keys"); banner copy ≈ 45 characters — **Provided** (§9.1 "≤ 45 characters so one 32 px line fits
  390 px"); `TmBanners` drives `tm-banner-bar` — **Provided** (§9.4); rename to `TmBannerOptions` —
  **Provided** (§9.1).
- **R-0024-28** (0024 §7, row 0028) — banner kinds `offline` and `update` — **Provided** (§9.5), with a
  different `update` message (M-2).
- **R-0025-28** (0025 §7, row 0028) — (1) `TM_LEADER_DOCUMENTS` for `inbox`, `assignments` —
  **Provided** (§5.1); (2) `assignments/summary` carries `NoActivityStampMetadata` — **Provided**
  (§4.1, §4.3); (3) "Live updates paused" follows `hubStatus().state` — **Provided** (§6.2 status
  line); (4) toasts from document changes in the focused tab — **Provided** (§8.2); (5) the
  presentation half of E12 — **Provided** (0028 E2).
- **R-0026-28** (0026 §7, row 0028) — admit "Diagnostics copied", "Couldn't copy" — **Provided** (§8.1
  "Admitted feedback toasts").
- **R-0027-28** (0027 §7, row 0028) — (1) bell and tray in 30 × 30 boxes — **Provided** (§6.1);
  `tm-banner-host` exposes `visible` — **Contradicts** (0028 C7: `TmBanners.visible` instead) (M-58);
  banners in the tenantless shell — **Provided** (§9.5); (2) `Alt+Shift+N`/`A` — **Provided** (§6.1,
  §7.1); (3) toasts "Couldn't sign out…", "Pinned {label}" — **Provided** (§8.1); (4) panel surfaces —
  **Provided** (§6.4).
- **R-0029-28** (0029 §7, row 0028) — feed `InboxSummary.Related` to `rememberRelated` — **Provided**
  (§7.3 "the attention documents feed `rememberRelated`").
- **R-0030-R14** — toasts "Exporting…" (sticky, progress, Cancel), "Export started"/"Import started"
  with Track, bulk results ("3 items deactivated."), failures — **Partial / Contradicts**: export and
  import started **Provided** (§8.1) with a different Track target (M-56); "Exporting…" **Lacks**;
  bulk results **Contradicts** (§8.1 "Saves, deletes, activations … on a page: never"); failures
  **Partial** ("The user's own action failed and has no inline home") (M-55).
- **R-0030-R15** — the bell's "My jobs" link uses `x.mine=1` — **Contradicts** (§6.2 "'My jobs' links
  to `/{t}/jobs?mine=1`"; §10.5 tab group with param `mine`) (M-7).
- **R-0031-R13** — a toast "{Entity} deleted." after a delete from the details page — **Contradicts**
  (§8.1 "Saves, deletes, activations, validation and request errors on a page: **never**") (M-55).
- **R-0032-R8** (to 0031 and 0028) — a `202 JobAccepted` action shows "Started in the background."
  with Track — **Lacks** (not in §8.1's admitted list) (M-55, M-56).
- **R-0032-R17** — `TmAttentionText.job` for a bare handler key — **Partial** (§11.1 `job(row: {
  HandlerKey; LabelKey: string | null; LabelArgumentsJson: string | null })`, rendering falls back to
  `JobHandler_<HandlerKey>`); the job details page links its schedule — **Provided** (§10.5 "schedule
  (link)").
- **R-0034-R10** — toasts "Exporting…", "Export started" (Track), "Report saved.", "Report deleted.",
  "Sharing updated."; the job label in the bell — "Export started" **Provided**; the rest **Lacks**,
  and "Report deleted." **Contradicts** §8.1's "never" row; job label **Partial** (§11.2's label
  mechanism; the report label itself is 0033's) (M-55).
- **R-0035-R6** — (a) `TmAttention.refresh('assignments')` — **Lacks** (§5.2 `TmAttention` has
  `toggle`, `read`, `readAll`, `clear`, `clearRead`, `cancelJob`); (b) `tm-assignment-list` fills its
  container and exposes a skeleton row or `loading` input — **Lacks** (§7.3 inputs `items`, `related`,
  `party`, `variant`, `seenAt`) (M-60).

### To 0029 (Client Entity Model)

- **R-0010BE-5** (consumption) — `itemDescription` content, multi-picker for navigation `in:`,
  `tm-multi-select` for enums beyond a checkbox list — **Provided** (§14.2 step 4; §13.2 table).
- **R-0023-29** (0023 §7, row 0029) — the path picker composes `tm-tree` — **Provided** (§11 "on
  0023's `tm-tree`"); `tm-radio-group` for booleans, `tm-date-range` for between — **Provided** (§13.2);
  `tm-search-field` where a text field filters — **Partial** (the path picker's built-in field; text
  filters use `input[tmInput]`).
- **R-0024-29** (0024 §7, row 0029) — entity, typed-codegen and limits sections through
  `IClientAssetContributor` — **Provided** (§2.1 `EntityClientAssetContributor`; §15); `bySegment` at
  match time — **Provided** (§4.1 "synchronous"); `tmCheckAppConfig` hooks — **Provided** (§3
  `tmCheckEntityDeclaration`).
- **R-0025-29** (0025 §7, row 0029) — read cacheable lists through `entitySet` — **Provided** (§5.3
  "Cacheable entities … answer `label` from `TmLaunchCache.entitySet()`"; §14.2 step 3).
- **R-0026-29** (0026 §7, row 0029) — `tmEntity` reserves `helpKey` — **Provided** (§3 `helpKey?`);
  `TmVisibleData` labels from descriptors — **Provided** (§6.2 `TmColumnPlan.label`); the
  `support-bundle` client policy — **Provided** (§16 last bullet).
- **R-0027-29** (0027 §7, row 0029) — (1) `tmEntity` carries `section`, `navOrder`, `icon`,
  `synonymsKey` — **Contradicts** (§3 `launcher?: { order?, icon?, synonymsKey?, labelKey? }`) (M-3);
  (2) Core-chunk summaries carry label keys and `query`/`save` operations — **Provided** (§2.2
  `TmEntitySummary`); (3) a blob URL helper (`TmBlobs.url` or equivalent) — **Lacks** (redirected:
  §16 "URLs come from `TmUrls.blob(…)` (request to 0024)") (M-21).
- **R-0028-29** (0028 §7, row 0029) — (1) summaries carry `icon` and the `assignable` capability —
  **Partial** (`capabilities` provided; no `icon`) (M-48); (2) label keys for `core.Notification`,
  `core.Assignment`, `core.Job` — **Provided** (§2.4, emitted for every entity); (3) `rememberRelated`
  fed from both attention documents — **Provided** (§5.3 "the bell's `Related` (0028)").
- **R-0030-R16** — `TmDisplayNames.rememberRows(entity, set)` — **Lacks** (§5.3 `remember(entity,
  rows)`) (M-46).
- **R-0030-R17** — `tmDescribeFilter(column, spec)` — **Lacks** (M-46).
- **R-0030-R18** — `TmExpressionEditor.select(start, length)` — **Lacks** (§10.1 `insertAtCaret`,
  `focus`) (M-46).
- **R-0030-R19** — the plan accepts `{ expression, label }` beside `{ expression, labelKey }` —
  **Lacks** (§3 `TmColumnInput` `{ expression, labelKey, type? }`) (M-9).
- **R-0030-R20** — create label chain `<Schema>_<Entity>_New` → Core `Entity_New` — **Lacks** (E6
  lists actions, Queryex and display-name keys only).
- **R-0031-R14** — `TmEntityMetadata.extras` becomes `{ name, deferred }[]` — **Contradicts** (§2.2
  `extras: readonly string[]`) (M-44).
- **R-0031-R15** — the `defaults?: () => TmJsonObject` member; `validate` typed as 0031 §7.2 —
  **Lacks** `defaults`; `validate` **Provided** as a placeholder ("`validate?: unknown; // a Signal
  Forms schema function (0031 types it)`").
- **R-0031-R16** — `TmEntityActions` and `injectTmActionDialog` ship in `@tellma/core/entity` —
  **Lacks** (§1's `/entity` contents).
- **R-0031-R17** — the binding opens `TM_ENTITY_PAGES.details` with `size: 'lg'` — **Lacks**.
- **R-0031-R18** — `tm-value-editor` keeps a field `pending` during upload and exposes the staged
  descriptor — **Lacks** (§16 "the staged id becomes the field's value").
- **R-0032-R10** — `tm-value`/`tm-value-editor` accept a `TmValueDescriptor` — **Lacks** (§12.1
  `property: TmPropertyDescriptor | TmPathResolution`) (M-45).
- **R-0032-R11** — column input `{ path, filter }` — **Lacks** (`TmColumnInput` `{ path, display?,
  width? }`) (M-39).
- **R-0033-R1** — (1) `tmQx.raw(text)` — **Lacks** (§8.2 builder) (M-46); (2)
  `TmNavigationMetadata.kind` adds `'level'` — **Lacks** (`'reference' | 'owner' | 'sibling'`) (M-47);
  (3) `reachable` mirrors E7(b) for child roots — **Partial** (§7 "a child entity takes its owner's
  access"; no owner-path rule); (4) the golden's expansion rows pin `IDisplayNames.Columns` —
  **Provided** (§5.2 "expansion rows (§6.2)").
- **R-0034-R4** — (a) term lists in `TmQueryexDraft` (`terms(clause)`, `[term]`) — **Lacks**; (b)
  clause `'constant'` — **Lacks** (§10.1 `TmQxClause = 'select' | 'filter' | 'having' | 'orderBy'`);
  (c) `TmEntities.enum(name)` — **Lacks**; (d) `tmQx.raw` — **Lacks** (M-46).
- **R-0010F-2** — `ComparedWith` is N29 §6's shape — **Provided** (§9 `IReadOnlyList<string>?
  ComparedWith`); the client path model treats `Level{j}` as a self-navigation of a tree entity with
  `TreeLevels ≥ j`, and `reachable` checks it as a traversal into the same entity — **Lacks** (M-47).

### To 0030 (Search Page)

- **R-0010BE-4** (0010-BE §5.1) — seed `activeRowId` with the first row when results land —
  **Provided** (§6.3 "Seeding (BE R-4)").
- **R-0023-30** (0023 §7, row 0030) — tiles on `tm-tiles` — **Provided** (§8); column editor on
  `tm-tree` (multiple) + `tm-reorder-list` — **Provided** (§11.1, via 0029's `tm-path-picker`);
  server-error state on `tm-empty-state` + `tm-reference` — **Provided** (§5.3 "failed"); `tm-pager` —
  **Provided** (§14); `tm-segmented` view toggle — **Provided** (§3.4); `tm-search-field` — **Provided**
  (§6.1); type-to-search calls `TmSearchField.focus()` and moves the highlight — **Provided** (§6.2,
  §6.3).
- **R-0024-30** (0024 §7, row 0030) — the meaning of `f.` operators per type — **Provided by the
  owner elsewhere**: 0030 §9.2 cites "0029 §13.2's" operators (A§4.1 gives filter controls to 0029);
  precedence and history over `TmUrls.search`/`parseSearch` — **Provided** (§4.2–§4.4).
- **R-0025-30** (0025 §7, row 0030) — `markStale` after import and bulk actions — **Provided** (§12.3
  step 3; §13.3 step 4); `replace` on collapse — **Provided** (§16.3); the `search.*` keys of Appendix A
  — **Partial** (used, with other value shapes, M-6).
- **R-0026-30** (0026 §7, row 0030) — publish the `search` context with `status` and `visible` —
  **Provided** (note §9); render toolbar and overflow from `group` — **Provided** (§3.1 "the
  distribution's `group: 'primary'` page actions"); route Export, Import, Delete and bulk actions
  through `invoke` with `confirm` — **Provided** (§12.3 step 1; note §9 table).
- **R-0027-30** (0027 §7, row 0030) — `page` the only paging parameter; query changes `replaceUrl` —
  **Provided** (§4.4 table).
- **R-0028-30** (0028 §7, row 0030) — (1) `tabs: { param?, items }` — **Lacks**; (2) computed columns
  `{ id, labelKey, select, display }` — **Contradicts** (§2.1 "a declared expression `{ expression,
  labelKey, type? }`"; §2.2 `{ path, display }`) (M-9); (3) `rowLink(row, ctx)`, `onActivate(rows,
  ctx)` — **Lacks**; (4) `pageActions`, `bulkActions` — **Lacks**; (5) `injectTmSearch().refreshRows
  (ids)` and a Refresh dot — **Lacks** (§2.5 `refresh()`; §13.4) (M-37, M-46); (6) the export and
  import 202 toast with "Track" (`TmAttention.toggle('bell', 'toast')`) — **Contradicts** (§13.2
  "with "Track" (the Jobs page, 0028)"; §13.3 Track → `/{t}/imports/{importId}`) (M-56).
- **R-0029-30** (0029 §7, row 0030) — build every query from `tmSelectPlan` — **Provided** (§5.1);
  `tm-column-filter` in the header popover, `tmFilterLeaf` into `Filters` — **Provided** (§9.1, §5.1);
  column editor composes `tm-path-picker` — **Provided** (§11.1); `TmSearchPickerPage` — **Provided**
  (§17); 0019 `ExportColumn` through `IDisplayNames` — **Provided** (E6); decide re-query on a
  language switch — **Provided** (§5.4 "the host re-queries").
- **R-0031-R19** — `TmSearchSnapshot.supersedes?` and the adoption rule — **Lacks** (§15.1 sketch has
  no `supersedes`) (M-49).
- **R-0031-R20** — past an edge the details page reads `request.route` with the full `request.body`
  and new `skip`/`take`, and writes the new page into the snapshot — **Contradicts** (§15.3 "reads
  `request.route` with `request.body` plus `select: 'Id'`, `skip` and `take`"; writing back not stated)
  (M-49).
- **R-0031-R21** — bulk actions with arguments open `TmEntityActions.dialog` — **Partial** (§12.2 "one
  with arguments opens its declared `dialog` (0031's hatch)").
- **R-0032-R12** — export `TmImportResultView` from `/search` — **Lacks** (§1 `TmImportDialog` is
  internal) (M-50).
- **R-0034-R3** — (a) `searchRead(request, query) → { route, body }` — **Lacks** (M-37); (b) custom
  filter options without `queryex`, and `hidden` filters — **Contradicts** (§2.4
  `TmCustomFilterDeclaration`: every option carries `queryex`; no `hidden`) (M-38).

### To 0031 (Details Page)

- **R-0010BE-5** (consumption) — `scrollToRow` for the child-row deep link — **Provided** (§14.1 step
  3).
- **R-0023-31** (0023 §7, row 0031) — save on `TmBusyOverlay.run` — **Provided** (§8.1 step 5); leave
  guard and delete through `TmConfirm` — **Provided** (§11.3; §10.2 through 0026's dispatcher, which
  uses 0023 confirm); header on `tm-page-header` with `tm-pager mode="item"` — **Provided** (§5.1,
  §12.1); cards on `tm-card` — **Provided** (§6.1 "on 0023's `tm-card`").
- **R-0024-31** (0024 §7, row 0031) — handle `validation` and `conflict` before `present` —
  **Provided** (§8.6 table); restore edit mode from the draft after step-up — **Provided** (§11.2);
  block navigation during a write — **Provided** (§8.7).
- **R-0025-31** (0025 §7, row 0031) — `TmDrafts.key`/`save`/`load`/`clear` — **Provided** (§11.1);
  `markStale` after save, delete and actions — **Provided** (§8.3 step 4, §10.2, §10.4).
- **R-0026-31** (0026 §7, row 0031) — the `details` context with `form` (`propose`, `assisted` mark on
  `tm-field`), `status`, `errors` — **Provided** (§16; §6.2 "Assisted mark"); the Actions menu renders
  `group: 'menu'` — **Provided** (§10.1); Delete's confirmation moves to the action's `confirm` —
  **Provided** (§10.2 step 1 "0026's dispatcher, `confirm` keys"); `tm-field` reserves `helpKey` —
  **Provided** (§6.2).
- **R-0028-31** (0028 §7, row 0031) — a stack without `save` opens view mode with no Edit —
  **Provided** (§3.1 "(0028's request)"); the `assignments` extra requestable by a layout — **Provided**
  (§6.8 `*tmDetailsExtra`, eager by default); `refresh()` from a layout — **Provided** (§4.1).
- **R-0029-31** (0029 §7, row 0031) — `tm-field` renders `tm-value`/`tm-value-editor` and
  `tm-entity-field` — **Provided** (§6.2 table); `TmDetailsPickerPage` — **Provided** (§15);
  `rememberRelated` with every `get` and `save` — **Provided** (§3.3 step 4, §8.3 step 1); clone strips
  by descriptor — **Provided** (§3.6).
- **R-0030-R21** — paging reads `history.state.tmFrom` and `TmSearchSnapshot` — **Provided** (§12.1).
- **R-0032-R1** — `TmDetailsSource` and `tm-details-page`, neutral conflict text — **Lacks** (M-40).
- **R-0032-R4** — `actions[].visible?: (entity, { userId }) => boolean` — **Lacks** (the member is
  0029's shape, which lacks it too) (M-36).
- **R-0032-R5** — a zero-argument action with a `dialog` opens it; `run(args, { form?, keepOpen?,
  ids? })` resolves to the outcome; `close()` — **Contradicts** (§10.4 `run(args: A, form?:
  FieldTree<A>): Promise<boolean>`; "Which actions appear … with no arguments, or with arguments and a
  declared `dialog`") (M-41).
- **R-0032-R6** — `tm-details-tabs` gains a `selected` model — **Lacks** (§6.6 `TmDetailsTabs { id }`)
  (M-42).
- **R-0032-R7** — a `@tellma/core/private` registry for platform components that register fields and
  collections — **Lacks**.
- **R-0032-R8** (to 0031 and 0028) — "Started in the background." with Track after `202` — **Lacks**.
- **R-0034-R8** — (a) a reusable `tmLeaveGuard(dirty)` and the concurrency dialog as content —
  **Lacks**; (b) the details context lists an embedded report's title and status — **Lacks**; (c)
  `details: { page, create? }` — **Lacks** (the member shape is 0029's, M-35).
- **R-0035-R8** — (a) `TmDetailsContext.actions` for a page hatch that registers its own context —
  **Lacks** (§4.1); (b) `can.*` read an eager `permissions` extra — **Lacks** (§4.1 "`can` = the
  operation exists ∧ `TmPermissions.allows` ∧ the mode") (M-43); (c) `clone(copy, source)` —
  **Lacks** (§3.6 step 5 "`declaration.clone?.(copy)`") (M-34).

### To 0032 (Stock Pages)

- **R-0026-32** (0026 §7, row "0032, 0034, 0035", 0032 part) — publish the `settings` context —
  **Provided** (note §9 "Settings publishes `settings` with `{ category, mode, dirty }`").
- **R-0027-32** (0027 §7, row 0032) — (1) My profile adopts `MeLocaleResult.ModifiedAt` or meets the
  conflict dialog — **Provided** (§8 "Conflicts"); (2) Settings is the entry `core.settings` —
  **Provided** (§3 "Settings 40 (0027's `core.settings`)"); (3) admin declarations set `section:
  'administration'` — **Provided** (§3 declarations).
- **R-0028-32** (0028 §7, row 0032) — details pages for `core.Export`, `core.Import`, `core.Schedule`,
  `core.OutgoingEmail` — **Provided** (§9, §10, §11).
- **R-0029-32** (0029 §7, row 0032) — Users, Roles and Centers use the generic binding; the permission
  filter uses `tm-expression-editor` with `profile: 'permission'` and `filterRoot` — **Provided** (§4.1,
  §5.3 "`injectTmQueryexDraft({ root: filterRoot, profile: 'permission' })`").
- **R-0030-R22** — the Imports details page shows `ResultJson`; Exports and Imports have no Create —
  **Provided** (§11 "Neither stack saves"; `TmImportDetails` "Result").
- **R-0031-R22** — stock layouts use the kit; the permission-check tab is a tab with its own
  component — **Provided** (§4.1 `TmUserTabs`, the `access` tab).

### To 0033 (Report Definitions and Engine)

- **R-0028-33** (0028 §7, row 0033) — register `core.report.shared` with `Toast = false` —
  **Contradicts** (§7 table: `Toast` "true") (M-57).
- **R-0029-33** (0029 §7, row 0033) — expand with `IDisplayNames.Columns`, render exports with
  `Render`, follow the golden's expansion rows — **Provided** (§8.4, §12.2, §9.2).
- **R-0010F-1** — adopt 0010-F's deviations (citations §F.8–§F.15, QX4009, optional `KeyGroups`,
  caller errors for `Exists`) — **Contradicts** (0033 §6 still writes F1–F8, QX3304, "Caller errors:
  `GroupingSets` without … `KeyGroups`", one `OUTER APPLY` per operand, `Exists` diagnostics) (M-75).
- **R-0034-R1** — (a) `ReportResult.Id`, `.Root`, `.Chart`, `ReportColumn.Expand` — **Lacks** (§9.1
  envelope) (M-73); (b) `Code` beside `Id` and `Draft` on run and export requests — **Contradicts**
  (§8.1 `ReportRunRequest(Id, Draft, …)`; §12.1 `ReportExportRequest(Id, Draft, Route, …)`) (M-71);
  (c) a details extra `permissions { save, … transfer }` on `reports/get` — **Lacks** (M-79); (d)
  `ReportTerm.format.calendar` — **Contradicts** (§3.2 "`month`, `quarter`, `year` (rendered in the
  viewer's calendar)") (M-76); (e) coded-report bodies named after the parameter keys and
  `IReportResults.Require(value, key)` — **Contradicts** (§11 `AgingArguments(DateOnly? AsOf, int?
  CustomerId)`) / **Lacks** (M-72); (f) seeds `acme.monthly-sales`, `acme.invoice-lines` — **Lacks**
  (E16 adds `acme.sales-by-center` only); (g) report export jobs set `Job.LabelKey`
  `Core_Job_ExportReport { title }` — **Lacks** (E11).
- **R-0035-R3** — (a) dashboard ids are `int` — **Contradicts** (§5.7 `AllowsAsync(long dashboardId,
  …)`, `DashboardsUsing → (long Id, string Title)`; §8.1 `DashboardId` `long?`; §6.1 `DashboardId`
  `long?`) (M-70); (b) a generic `SharePermissions(Save, Delete, Activate, Share, Publish, Transfer)`
  extra — **Lacks** (M-79); (c) ownership hooks applied by `Shareable<,>()` — **Partial** (§5.1
  `Shareable` registers "the criteria provider, `IShareCommands` …, the audience resolver's source and
  the startup check"; §2.1 calls ownership "the service's hooks"); (d) a multi-securable
  `EvaluateOutcomesAsync` overload — **Lacks** (E2 single `resource, action`); (e) state that an
  inactive definition still runs and that `DashboardsUsing` titles use the content slot — **Lacks**;
  (f) run and export counters gain `via` — **Lacks** (§14 tags `kind`, `source`, `outcome`).

### To 0034 (Report Renderers and Studio)

- **R-0023-34** (0023 §7, row 0034) — studio on `tm-splitter`, `tm-segmented`, `tm-switch`, `tm-tree`
  with `dragData`, `tm-reorder-list` with `accepts` — **Provided** (§12.2, §12.3 "Term lists are
  `tm-reorder-list` (`accepts: ['tm.path']`)"); the dark panel re-points its tokens — **Provided**
  (§12.2 "a `data-theme="dark"` scope"); name the non-drag "Add to…" path — **Provided** (§12.5).
- **R-0024-34** (0024 §7, row 0034) — coded keys checked by `reserved-path`/`invalid-report-key` —
  **Provided** (§2.2 "beside 0024's key checks"); `run` through `TmApi.read` with `long` — **Provided**
  (§3.1 Transport).
- **R-0025-34** (0025 §7, row "0034, 0035", 0034 part) — `report.<id|key>.params` and `TmPageState`;
  the host publishes kind `report` — **Provided** (§4.5, §3.7, §13).
- **R-0026-32** (0034 part) — publish `report` and `studio` contexts — **Provided** (§13).
- **R-0027-34** (0027 §7, row "0034, 0035", 0034 part) — `tmCodedReport` takes `section`, `order`,
  `icon`, `synonymsKey`; Reports declares `section: 'analysis'` — **Provided** (§2.2
  `TmCodedReportDefinition`; §2.3).
- **R-0029-34** (0029 §7, row 0034) — field list is `tm-path-picker` (`fields`, `draggable`, `accept`
  for measures) — **Partial** (§12.2 `mode="fields" draggable`; `accept` used for attributes, measures
  take any field); one shared `injectTmQueryexDraft` with confirmed types — **Provided** (§12.4, §12.7);
  numeric → `sum`, else `count` — **Provided** (§12.5).
- **R-0030-R23** — the reports page uses the `search` hatch — **Provided** (§11); the drill list may
  host `TmSearchHost` with a fixed leaf — **Contradicts** (§8.3 `TmDrillListPage` with its own
  `tm-grid`).
- **R-0031-R23** — child-rooted drill rows link with `TmUrls.details(owner, ownerId, { collection,
  id })`; a report in a card is `TmReportHost` in `tm-details-card` — **Provided** (§5.2, §8.3 "rows";
  §2.1).
- **R-0033-R2** — renderers read only `ReportResult`; bar from `ReportParameter`; `p.{Key}`; drill URLs
  from `ReportDrill`; cube rendering; chart tokens; reports page calls `browse`; share dialog calls
  `share`, `publish`, `audience`; delete warning reads `audience.grants`; studio previews with `Draft`
  and maps errors by `pointer` — **Provided** (§3.5, §4, §4.4, §8.1, §6, §7.7, §11, §10.4, §10.3,
  §12.7).
- **R-0033-R3** — `tmCodedReport` declares `route`; `run` is the hatch; `default` a placeholder —
  **Provided** (§2.2).
- **R-0035-R4** — (a) widget mode never reads `reports/get`; `[kind]` input; tile's title; waiting
  copy — **Lacks** (§3.1 inputs; §3.6 widget column) (M-77); (b) `[held]` input; `silent` parameter
  changes present as `spinner` — **Lacks**; (c) only a plain `run()` is a background request in
  `silent`; `forbidden`/`not-found` clear the held output — **Partial** (§3.4 "A `silent` run passes
  `background: true`"); (d) export `tm-report-parameter-bar` and `tm-report-parameter-field`; codec
  takes dashboard declarations — **Lacks** (§1 exports `TmReportHost`, `injectTmReportHost`,
  `TmReportDrillEvent`, `TmShareDialog`, `tmReportArgsFromUrl`/`ToUrl`) (M-77); (e) `TmShareDialog` with
  `segment: 'dashboards'` lists `DashboardAudience.Widgets` warnings — **Partial** (§10.4 "data
  `{ segment: 'reports' | 'dashboards', id, title }`, so 0035 reuses it"; widget warnings absent) (M-78).

### To 0035 (Dashboards and Home Page)

- **R-0023-35** (0023 §7, row 0035) — refresh line on `tm-relative-time` — **Contradicts** (decision 11
  "'Updated 10:42' is absolute") (M-69); widget empty and error states on `tm-empty-state` —
  **Provided** (§8.6).
- **R-0025-34** (0035 part) — `TmPageState`; the dashboard host publishes kind `dashboard` —
  **Provided** (§11.4; note §9; C-0035-2).
- **R-0026-32** (0035 part) — `dashboard` and `home` contexts — **Provided** (note §9).
- **R-0027-34** (0035 part) — Dashboards declares `section: 'analysis'` — **Provided** (§12.1); home
  "may render `TmPins.items` and `TmNavigation.sections`" — not used (the default home renders neither).
- **R-0028-35** (0028 §7, row 0035) — home lists with `tm-assignment-list variant="home"` from the
  stored `assignments` document — **Provided** (§9.2, §15.4).
- **R-0031-R24** — the dashboard view uses `details: { page }` and the host's edit, save, drafts —
  **Provided** (§12, §13.1).
- **R-0033-R4** — implement `IDashboardReadThrough` — **Provided** with `int` ids (§6.3), against
  0033's `long` (M-70); `FK_DashboardWidgets_ReportDefinitionId` NO ACTION — **Provided** (§3.1);
  reuse the sharing model and register `core.dashboard.shared` — **Provided** (§3.1, §3.2, §7.2);
  `DashboardId` on widget runs — **Provided** (§9.1); `Tellma-Activity: background` on silent refresh —
  **Provided** (§11.2); degrade widgets with `EvaluateOutcomesAsync` — **Partial** (§7.1 uses it for
  audience warnings; §6.2 hides tiles from the caller's cached decisions).
- **R-0034-R11** — widgets as `tm-report-host mode="widget" refresh="silent" [dashboardId]
  [parameters]`; force refresh = `run({ clear: true })`; "Last refreshed" = `executedAt`; Export =
  `export()`; sharing through `TmShareDialog` — **Provided** (§9.1, §11.3, §8.5 "Updated", §8.4,
  §12.1).

### Requests to specs outside round 2

- **R-0024-13** (0024 §7, row "0013, 0014, 0016, 0021") — write their sections through
  `IClientAssetContributor`; 0016 exposes `IContractFingerprints`. Targets are round-1 specs; the edits
  that carry it: 0024 E13 (0013 packs), E14 (0021 catalogue), E16 (0016 fingerprints); 0032 E4 (0013
  setting keys and categories) and E5 (0014 securables).

## Part C — Cross-note mismatches

Each item names the notes and sections, what differs, and which spelling the skeleton and the
contract's owner use ("owner" per A§1 and the notes' own "owns" tables). Items already filed in
Parts A and B are cross-referenced; the rest come from reading the sketches side by side.

### Names, launcher and declaration shape

- **M-1** — Banner data type. Skeleton A§5.14 `TmBanners.show(banner: TmBanner)`; 0028 §9.1
  `show(options: TmBannerOptions): TmBannerRef`, plus `TmBannerLevel`; 0023 §3.5 names its component
  `tm-banner-bar` / `TmBannerBar` (C3). 0024 §5.5 calls the ordering "precedence 3"; 0028 §9.5 calls
  it "level". Owner 0028: `TmBannerOptions`, "level". Skeleton: `TmBanner`, "precedence".
  (C-0023-3, C-0028-2.)
- **M-2** — Update banner copy and id. Skeleton A§7.5 step 2 "A new version of Tellma is available.
  Reload"; 0024 §5.5 keeps it ("dismissing hides it for that version in this tab"); 0028 §9.5 row
  `tm.update` "A new version is available." 0024 §7 row 0028 names the banner "kinds `offline` and
  `update`"; 0028's ids are `tm.offline`, `tm.update`. Two notes write the same banner's copy;
  owner of banner sources 0028. (C-0028-5.)
- **M-3** — `tmEntity` launcher members. 0029 §3 (shape owner): `section?` plus `launcher?: { order?,
  icon?, synonymsKey?, labelKey? }`, gate `allows(e, 'query')`. 0027 §3.2: `section`, `navOrder`,
  `icon`, `synonymsKey` flat on the declaration, gate `[name, 'Read']` through `TmPermissions.can`.
  0032 §3 writes 0029's `launcher: { order, icon }`; 0034 §2.3 and 0035 §12.1 write 0027-style flat
  `icon`, `synonymsKey`. `tmCodedReport` (0034 §2.2) and `tmPage` (0027 §3.2) use flat `order`. The
  skeleton names none. (C-0032-7, R-0027-29(1), R-0029-27, R-0032-R16.)
- **M-4** — Search normalizer. 0027 §1 exports `tmSearchNormalize` from `@tellma/core/shell`, and 0034
  §11 uses that name; 0029 §7 row 0027 asks for `tmNormalizeForSearch`. Owner 0027:
  `tmSearchNormalize`.
- **M-23** — Explicit type argument. Skeleton A§3.3 `tmEntity<AcmeCustomer>(…)`; 0026 §12.5
  `tmEntity<CoreSupportRequest>`, 0028 §10.1 `tmEntity<CoreNotification>`; 0029 §3 `tmEntity<const N
  extends string>(name: N, …)`, so a type argument there must be the name literal, not the row type.
  Owner 0029. (C-0029-1.)
- **M-34** — `clone` signature and member casing. 0029 §3 `clone?: (copy: TmJsonObject) =>
  TmJsonObject`; 0031 §3.6 step 5 calls `declaration.clone?.(copy)`; 0035 §12.1 writes `clone: (copy,
  source) => ({ ...copy, Code: null, Shares: [], BasedOnId: source.Id })`. Two arguments, and
  PascalCase members, while 0029 decision 4 and 0031 §4.1 say rows are the wire's camelCase objects
  (0034 §2.1 writes `details.entity()?.id`). Owner 0029 (shape), 0031 (call). (R-0035-R8(c).)
- **M-35** — `details` member. 0029 §3 `details?: TmLazyComponent | { page: TmLazyComponent }`; 0034
  §2.3 `details: { page, create }` (a separate create component, the studio); 0035 §12.1 `{ page }`
  for view, edit and create. Owner 0029. (R-0034-R8(c).)
- **M-36** — `actions[]` members and the share dialog factory. 0029 §3 `{ name, dialog?, icon?,
  button? }`; 0032 R4 wants `visible?: (entity, { userId }) => boolean` and asks 0031, not 0029.
  0034 §2.3 and 0035 §12.1 write `dialog: tmShareDialogFor('reports' | 'dashboards')`, which no note
  declares: 0035's import table cites 0034 §10.4, and 0034 §1's export table lists `TmShareDialog`
  but not `tmShareDialogFor`, `TmTransferDialog`, `TmReportsPage`, `TmReportPage`, and does not
  declare the primary-side `TM_REPORT_TABS` and `tmReportScope` used in §2.3. Owner 0029 (member),
  0034 (dialogs).
- **M-37** — Declaration and search members used but undefined. In neither 0029 §3 nor 0030 §2:
  `searchRead` (0034 §2.3); `tabs: { param?, items }`, `rowLink`, `onActivate`, `pageActions`,
  `bulkActions` (0028 §10, R-0028-30). Missing from 0030 §2.5: `injectTmSearch().refreshRows(ids)`
  (0028). 0034 §2.3 reads `query.custom?.['tab']`, and 0030 §9's `search.query` action takes
  `custom?`; 0024 §9.5's codec names no `TmSearchQuery` member for `x.*`. Owners 0029 (declaration),
  0030 (search API), 0024 (codec).
- **M-38** — Custom filter shape. 0030 §2.4 `TmCustomFilterDeclaration`: every option carries
  `queryex`, no `hidden`. 0034 §2.3 `filters: [{ key: 'tab', labelKey, options: TM_REPORT_TABS,
  hidden: true }]`, whose options carry no Queryex, read through `searchRead` instead. Owner 0030.
  (R-0034-R3(b).)
- **M-39** — Column input with a filter. 0032 R11 `{ path, filter }`; 0029 `TmColumnInput` `{ path,
  display?, width? }`. Owner 0029.
- **M-9** — Computed column shape. 0029 §3 and 0030 §2.1 `{ expression, labelKey, type? }`; 0030 R19
  also wants `{ expression, label }`; 0028 R-0028-30(2) `{ id, labelKey, select, display }`. Owner
  0029. (R-0030-R19.)
- **M-10** — Tile image key. Skeleton A§3.3 `tile: { image: 'ImageId', … }`; 0029 §3 example and 0023
  §3.8 example (`c.ImageId ? blobUrl(c.ImageId) : null`) use `ImageId`; 0029 §6.1's column-key table
  names the image navigation `Image`; 0030 §2.3 and 0032 §3 write `image: 'Image'`; 0030 C7 proposes
  that both resolve. Owner of column keys 0029: `Image`. Skeleton: `ImageId`. (C-0030-7.)
- **M-44** — Extras in metadata. 0029 §2.2 `TmEntityMetadata.extras: readonly string[]`; 0031 E1–E3
  and R14 `{ name, deferred }[]`; 0032 §5.4 relies on a deferred `members`. Owner 0029 (metadata),
  0031 (semantics). (R-0031-R14.)
- **M-48** — Entity icon. 0028 §0 consumes "`TmEntities` (labels, icons, capabilities)"; 0029
  `TmEntitySummary` has no icon; icons live on the declaration (`launcher.icon` or flat `icon`, M-3).
  Owner 0029. (R-0028-29(1).)
- **M-22** — `TmEntities` contract. Skeleton A§5.12 `get(): TmEntityDescriptor` and `bySegment():
  TmEntityDescriptor | undefined` in the eager primary; 0029 §4.1: `summary`/`bySegment` return a
  `TmEntitySummary` at once, `get` a descriptor only after `load()`; 0024 §9.2 matches routes through
  `TmEntities.bySegment` and §4.4's route host "takes the descriptor from `TmEntities`" without
  awaiting `load()`; 0027 reads segment, label keys and operations, and 0029 §7 row 0027 asks it to
  use `TmEntities.label`. Owner 0029. (C-0029-2, R-0029-24(2).)

### URLs, routes and the search query

- **M-5** — Search view axis. Skeleton A§8 and A§5.13 `view: 'grid' | 'tiles' | 'tree' | 'list'`;
  0024 §9.5 `view`: `grid | tiles | tree | list`; 0025 Appendix A `.view` four-valued; 0026 §9.3
  search state has `view` only; 0030 §3.4 and §4: `view=grid|tiles` plus `list=1`. Owner of meaning
  0030 (0024 §9.5 "0030 owns their meaning"); owner of codec 0024. (C-0030-1, R-0030-R4.)
- **M-6** — Search preference values. 0025 Appendix A: `search.<entity>.columns` = `string[]` of
  paths, `.view` = the four-valued union; 0030 §4.3: `TmColumnPreference[]`, `{ narrow?, wide?,
  list? }`. Owner of the key table 0025; of meaning 0030. (C-0030-2, R-0030-R11.)
- **M-7** — "My jobs" URL. Skeleton A§8 `/{t}/jobs?mine=1`; 0028 §6.2 the bell links
  `/{t}/jobs?mine=1` and §10.5 declares a tab group with `param: 'mine'`; 0030 C4 `x.mine=1`; 0028 C3
  floats `?tab=mine|all`. Owner of the page 0028. (C-0028-3, C-0030-4, R-0030-R15.)
- **M-8** — Report and assignment tab parameters. Skeleton A§8 `/{t}/reports?tab=mine|shared|all`;
  0033 §5.5 cites `?tab=` and adds `ReportBrowseRequest(ReportScope Scope, QueryRequest Query)`; 0034
  §11 writes `x.tab=mine|shared|all` (`all` omitted) through `searchRead` to `reports/browse { scope,
  query }`. Assignments: skeleton `?tab=to-me|by-me`, 0028 adds `all`, 0035 §9 action
  `home.view-assignments { tab: 'to-me' | 'by-me' }`. 0024 §9.5's codec has no `tab` key (only
  `x.*`). Owner of the page 0034: `x.tab`. Skeleton and 0033: `?tab`. (C-0033-4, C-0034-2.)
- **M-24** — `tmPage` signature. Skeleton A§5.1 `tmPage(path, { component, titleKey, section?,
  securable? })`; 0027 §3.2 adds `order`, `icon`, `synonymsKey` (asked of 0024, R-0027-24(4)); 0034
  §2.3 declares `tmPage('reports/:id/studio', …)` with a route parameter and publishes context kind
  `studio`, while 0024 §2.4 says the route host publishes a `custom` context for `tmPage` routes (0024
  §9.2 does admit the path). Owner 0024.
- **M-25** — `TmRouteData.skeleton`. 0024 §4.4 `'search' | 'details' | 'blank'`; 0034 adds `'report'`;
  0035 adds `'home'`, `'dashboard'`; 0032 R2 uses `'details'` for `settings` and `me`. Owner 0024.
- **M-30** — Who loads a page hatch. 0024 §4.4: the route host "loads the search host, the details
  host, or the declaration's `search` or `details.page` hatch". 0030 C6 and §2.5: the route host loads
  `TmSearchHost`, which hosts `search` as its results region. 0031 §3.7, 0034 §11 (`TmReportsPage` "as
  its results region (0030 §2.5)") and 0035 §12 (`TmDashboardPage` "page hatch (0031 §3.7): the host
  owns header…") agree with the page owners. Owners 0030 and 0031 against 0024. (C-0030-6,
  R-0030-R6.)
- **M-31** — `TmUrls` members. 0024 §9.4: `home`, `search`, `parseSearch`, `details`, `create`,
  `page`, `picker`, `signedOut`, `target`, `describe`, `preload`. Consumers add `blob(kind, id, {
  variant?, download? })` (0029 §16, 0031 R7), `settings(category?)` and `me(sub?)` (0032 R2; 0032 §8
  writes `urls.me('notifications')`), and `describe` kinds for `/reports/{key}` and `/list/:segment`
  (0034 R2(c)). My profile is `urls.page('me')` in 0027 §10.1 and `urls.me()` in 0032. Owner 0024.

### Tokens, providers, metadata and config

- **M-26** — Config problem codes. 0024 §2.2 holds the list (0034 relies on its `reserved-path`,
  `invalid-report-key`). Requested and absent: `app-bar-reserved-id` (0027); `unknown-path`,
  `unknown-action`, `operation-missing`, `entity-not-routable`, warning `label-missing` (0029 §3);
  `unknown-setting-category` (0032); `coded-report-source` (0034); `widget-missing`,
  `widget-not-registered` (0035). Owner 0024.
- **M-27** — `TmFeatureDefinition` and shell slots. Skeleton A§5.1 and A§5.11; 0024 §2.2 keeps them
  "as A§5.1". Members consumers add: `icons` (0023), `widgets` (0035), `entries` with `order`,
  `icon`, `synonymsKey` (0027); `TmShellSlot.badge` a factory `() => Signal<number | null>` (0027 C2)
  against skeleton `badge?: Signal<number | null>`. Owner 0024 (feature), 0027 (slots).
- **M-28** — Client metadata members. Skeleton A§5.2; additions written elsewhere and absent from
  0024: `distribution.copyright` (0027), `settingCategories` and `jobHandlers` (0032), `widgetTypes`
  (0035); `contracts` per route `{ fp, read, long }` is 0024's own change (C-0024-2). Owner 0024.
- **M-29** — Providers `provideTellma` must supply. Tokens defined or consumed elsewhere and filled
  by nobody in 0024 §2.2 and §7.1 (which provide `TM_ACTIVE_LOCALE`, `TM_CALENDAR`, `TM_BLOB_FETCHER`):
  `TM_TIME_ZONE` (0010-BE D.4; read by 0023 and 0029 `TmFormats`), `TM_NOW` (0023), `TM_REALTIME`
  (0025; route providers in 0027 §7.5), `TM_APP_DECLARATIONS` (0027), `TM_ENTITY_DECLARATIONS` and
  `TM_ENTITY_PAGES` (0029; 0030 R7, 0031 R6 fill `search`, `details`), `provideTellmaToasts({
  maxVisible, overflow })` (0023, 0028). Owner 0024. (C-0025-8, C-0029-5.)
- **M-17** — Trusted Types policy. 0024 §6 asks 0010 Part B for one `tellma` policy in the core-ui
  primary, with `createHTML` "for sanitized SVG" for 0023's icon registry, and §5 lists `tellma` in
  the CSP's `trusted-types`; 0023 §3.3 builds icons with `createElementNS` ("No `innerHTML`, so no
  Trusted Types policy"); 0010-BE defines no policy. Owner named by 0024: 0010 Part B. (C-0024-1.)
- **M-33** — Worker version. 0024 §5.5 `TmUpdates { enabled, available, check, reload }`; 0026 reads
  the controlling worker's version for About and the dump. Owner 0024. (R-0026-24(8).)
- **M-32** — `TmServerText`. 0024 §7.5 `problem(problem)`, `error(error, entity?)`; 0028 calls
  `message(key, args)` and `has(key)`. Owner 0024. (R-0028-24(1).)

### HTTP, logging and blobs

- **M-19** — Request log record. Skeleton A§5.9 `TmRequestRecord` (one record); 0024 §10.3 step 5
  writes one record after settle; 0026 §8.1 `begin`/`end` with a `pending` outcome and fields
  `tenantId`, `method`, `background`, `code`; 0024 §7 row 0026 asks 0026 to take "§10.3 step 5's"
  fields. Owner 0026. (C-0026-3.)
- **M-20** — `send` options. 0024 §10.1 `send(route, body): void`, §10.2 background marks only
  "reads with `background: true`"; 0026 calls `send(route, body, { background: true })`. Owner 0024.
- **M-21** — Upload and blob URLs. `TmApi.upload(kind, blob, { fileName, signal?, onProgress? })` is
  called by 0026, 0029, 0030, 0031; 0024 §10.1's `TmApi` has `read`, `write`, `download`, `send`.
  Blob URLs: 0027 asks 0029 for `TmBlobs.url(kind, id, { variant })`; 0029 §16 redirects to
  `TmUrls.blob(…)` on 0024, which lacks it; 0023 §3.8's example calls an undefined `blobUrl`. Owner
  0024. (C-0026-5.)
- **M-18** — Error dialog content. 0024 §11 dialogs show a message, items, a reference chip with
  Copy, a Details disclosure (status, code, time, route template, UI version) and a count badge; 0026
  adds "Copy diagnostics" and "Report this problem" to 5xx Details; 0023 §3.5 `TmErrorDialogOptions {
  title, message, details?, reference?, retry? }`. The `alertdialog` role needs 0010-BE `TmModalConfig.
  role` (0023 A1, absent). Owner of the component 0023; of copy 0024.

### Library members (0023, 0010-BE)

- **M-11** — Row activation payload. 0023 A7 `TmItemActivation<T>`, shared by `tm-tiles` and the
  grid; 0010-BE C.1 `TmGridRowActivation { row, rowId, via, newTab }`. Owners 0010-BE (grid), 0023
  (tiles).
- **M-12** — Multi-select rendering and naming. 0023 A6: both multi-selects render values with
  `tm-chip-set overflow="collapse"`; 0010-BE B.4: text joined with `Intl.ListFormat`; E.2: a
  horizontally scrolling `tm-chip` row with a count badge. 0023 §1 calls the component "multi-select
  `tm-select`"; 0010-BE ships sibling `tm-multi-select` (decision 9). Owner 0010-BE.
- **M-13** — Relative time. 0023 §3.4 `TmRelativeTime { value, format: 'narrow' | 'long' }`,
  `TM_NOW: InjectionToken<() => number>`, thresholds "Just now", minutes, hours, "Yesterday", then the
  `medium` date; 0023 A3 asks a function `(instant, now, { locale, timeZone, style })`. 0010-BE D.4
  `tmFormatRelativeTime(instant, locale, { now, timeZone, calendar?, form?: 'relative' | 'age', style?:
  'long' | 'short' | 'narrow' })` and `TmL10n.formatRelativeTime(instant, { now, form?, style? })`,
  with day counts to 7 days. Input name (`format` vs `style`), value set, and day threshold differ.
  Owner of the format 0010-BE; of the component 0023.
- **M-14** — Overlay helpers' entry point. 0010-BE D.1 promotes `tmPushEscapeDismissal` and kin to
  `@tellma/core-ui/overlay`; 0023 §3.1 lists `private` as the dependency of `toast`, `radio`,
  `segmented` and §3.2 cites 0005 §2.1; 0027 §9.1 cites "BE D.1". Owner disputed under ruling 3
  (C-0010BE-5).
- **M-15** — `clearAll` semantics. Skeleton A§6.4 Part D: `clearAll` spares "the boot record's device
  fields"; 0010-BE D.5 `clearAll({ keep })`, "0025's `TmClientStore` keeps `tm-app` out"; 0025 §6:
  `clearAll` "also deletes `tm-*` IndexedDB databases (here `tm-app`)"; 0024 §6 and §8.4 step 6 call
  `clearAll()` as the skeleton describes. Owner 0010-BE. (C-0010BE-1, R-0010BE-3.)
- **M-16** — Live-direction host. Skeleton A§6.4 Part D: "live `direction`" on `TmModal`; 0010-BE
  B.3: a live root `Directionality` watching `<html dir>` (Part B); 0024 §4.4 renders a `[dir]`
  wrapper in `tm-app` called "0010 Part D's live-direction host", and its §0 table lists the host
  under Part B. Owner 0010-BE. (C-0010BE-2.)
- **M-63** — Menu members. 0027 M1–M5: `TmMenuGroup.layout?: 'list' | 'segmented'`; `cssClass` on
  `TmMenuItem`, `TmMenuLink`, `TmMenuSubmenu`; registry-name icons on links and submenus; 44 px rows
  below `sm`; `TM_GRID_KEYS`, `TM_GRID_KEY_HELP`. 0023 A5: registry-name `TmMenuItem.icon`, `shortcut`
  as `TmKeys`. 0010-BE C.10: items "unchanged", `TmMenuLink.icon?: TemplateRef<void>`. Owner 0010-BE.
- **M-64** — Picker page context. 0029 C5: `TmEntityPickerPage`'s object form gains `context?:
  unknown`, read by 0030 §17 `TmSearchPickerPage` and 0031 §15 `TmDetailsPickerPage`; 0010-BE E.2
  `TmEntityPickerPageData` adds `multiple`, `ids` only. Owner 0010-BE.
- **M-65** — Grid, tabs and l10n members consumers use. Absent from 0010-BE: `TM_GRID_KEYS`,
  `TM_GRID_KEY_HELP` (0027); `scrollToRow(id, { column?, focus? })` (0031; C.8 has `flash`,
  `activate`, `align`); `tm-tab-group prerender` (0031); C.12 `rowSelectable` (used by 0034 §11) and
  C.13 `footerRows` (0034); D.7 `month`, `quarter`, `year` styles in an explicit calendar (0034);
  `'weekday'` and `tmZonedParts` (0035); readonly-grid printable keys (0030). `TmGridDriverTarget` is
  in `@tellma/core-ui/grid` (0010-BE C.2), and 0030 wants it in `@tellma/core-ui/contracts`. Owner
  0010-BE.
- **M-66** — Chip and alert names in 0032. 0032 §0 lists `tm-chips` and `tm-alert` under 0023 and
  R14 asks `tm-chips` free entry; 0023 ships `tm-chip` and `tm-chip-set` only (C1); 0031 §0 lists
  `tm-alert` under 0005 (frozen). Owner 0023 (chips), 0005 (alert).
- **M-67** — Inline banner. 0026's support dialog uses a "0023 inline banner, tone warning,
  `role="note"`" and asks 0023 for it; 0023's `tm-banner-bar` is "exactly 32 px (the shell's banner
  row)"; 0027 §6.2, 0031 §11, 0034 §5 use 0005's `tm-alert` for the same role. Owner 0023 / 0005.
- **M-68** — 0023 members consumed and undefined. `tm-skeleton` shapes for a table, a pivot block and
  a chart frame (0034; 0023 `shape: 'line' | 'block' | 'circle'`); `[tmCardSubtitle]` (0035);
  `tm-pager` `disabled` (0030) and an item-mode capped total (0031); `tm-tiles` as
  `TmGridDriverTarget`, `scrollOffset()`, `scrollTo()` (0030); `tm-banner-bar announce` (0028); a
  changing `TmBusyOverlay` label (0031); reserved width in `tm-page-header` (0031);
  `tm-signature-pad` (0032); a read-only chip with a "+n" popover (0034). Owner 0023.
- **M-69** — The "Updated" line. 0023 §7 row 0035 puts it on `tm-relative-time`; 0035 decision 11
  and §8.5: "'Updated 10:42' is absolute"; 0034 §10 writes "Updated 10:42 (`tm-relative-time` over
  `executedAt`)", which that component cannot render (it is relative until "Yesterday"); 0033 §9.1 and
  0034 R11 call it "Last refreshed". Owners 0034, 0035 (copy), 0023 (component).

### Client state, page context and support

- **M-49** — Search snapshot and paging read. 0031 R19 `TmSearchSnapshot.supersedes?`; R20: past an
  edge read `request.route` with the full `request.body` and new `skip`/`take`, then write the page
  back. 0030 §15.1 has no `supersedes`; §15.3 reads `request.body` "plus `select: 'Id'`, `skip` and
  `take`" and says nothing of writing back. Owner 0030. (C-0031-7.)
- **M-50** — Import result view. 0032 R12 imports `TmImportResultView` from `/search`; 0030 §1 keeps
  `TmImportDialog` internal and exports no result view. Owner 0030.
- **M-51** — Action id prefixes. 0026 §9.3: framework prefixes `search`, `details`, `report`,
  `studio`, `dashboard`, `settings`, `support`; entity `<schema>.<entity-kebab>.<action>`;
  distribution `<feature>.<area>.<verb>`. Used elsewhere and outside the list: `attention.open-item`,
  `attention.read-all`, `attention.clear-read`, `attention.cancel-job` (0028 §9),
  `notifications.settings.set`, `.test` (0028 §10.3), `admin.user.check-access` (0032),
  `home.view-assignments` (0035). Owner 0026.
- **M-52** — Page-context state per kind. 0026 §9.3 minimums against owners: `search` gains `list`
  (0030 R12); `details` gains `dirty`, `child` (0031); `settings` `{ category, mode }` (0026) vs
  `{ category, mode, dirty }` (0032), and 0028's notification-settings page also publishes kind
  `settings`, with `{ saving, failed }`, so two owners share one kind; `dashboard` `widgets: number`
  (0026) vs `widgets: [{ key, type, title, status, executedAt }]` (0035); `home` `{}` vs `{ hatch:
  boolean }` (0035). Owner 0026 (table), the page specs (completion).
- **M-53** — Support dump contributions. 0026 §10.1 `TmSupportDump` is a fixed shape; 0025 (names and
  tags block) and 0028 (`{ open, bell, tray, banners, armed }`) contribute blocks through a seam 0026
  does not define. Owner 0026. (R-0025-26, R-0028-26.)
- **M-54** — A hatch's page context. 0031 §3.7: a `page` hatch's context stays the host's. 0026
  §9.2: "a second non-overlay `register` in the same scope replaces the first". 0035 §11 registers
  `dashboard`, replacing the host's context; 0034 §13's report page (a details `page` hatch)
  publishes `report`; 0034's studio (`tmPage`) publishes `studio` beside 0024 §2.4's `custom`. Owners
  0026 (registry), 0031 (host). (C-0035-2, R-0035-R8(a).)
- **M-58** — Banner height source. 0027 R1 asks `tm-banner-host` to expose `visible`; 0028 C7 puts
  `visible` on `TmBanners` (primary); 0027 §5.1 still reads it from "0028's host". Owner 0028:
  `TmBanners.visible`. (C-0028-7, R-0027-28(1).)
- **M-60** — Attention API used by 0035. 0035 calls `TmAttention.refresh('assignments')` and expects
  `tm-assignment-list` to fill its container with a skeleton row or a `loading` input; 0028 §5.2
  `TmAttention` has `toggle`, `read`, `readAll`, `clear`, `clearRead`, `cancelJob`; §7.3 inputs
  `items`, `related`, `party`, `variant`, `seenAt`. Owner 0028. (R-0035-R6.)
- **M-61** — Pins fallback. 0025 Appendix A `nav.pinned`: "0027's pins model", fallback "the tenant's
  default pins setting when 0027 declares one, else `[]`"; 0027: `TmPinsDoc`, fallback `{ v: 1,
  items: [] }`, decision 13 "No tenant default". Owner 0027 (model), 0025 (table).
- **M-62** — Preference keys missing from 0025 Appendix A: `notifications.toasts` (0028 §8.4,
  `tmNotificationToastsKey`), `dashboard.<id>.params` (0035), `report.studioPanel` (0034); the
  launch-document name `home` (0035). Owner 0025.

### Toasts, notifications and shortcuts

- **M-55** — Toast policy against feature toasts. 0028 §8.1 says "Saves, deletes, activations,
  validation and request errors on a page: never" and lists the admitted toasts. Asked for or written
  elsewhere: bulk results ("3 items deactivated.") and a sticky "Exporting…" (0030 R14); "{Entity}
  deleted." (0031 R13); "Started in the background." (0032 R8); "Report saved.", "Report deleted.",
  "Sharing updated." (0034 R10; 0034 §10.4 writes "Sharing updated."). Owner 0028.
- **M-56** — "Track" destination. 0028 §8.1 "opens the bell" (R-0028-30(6) `TmAttention.toggle('bell',
  'toast')`); 0030 §13.2 export → the Jobs page; 0030 §13.3 import → `/{t}/imports/{importId}` (0032
  §11 agrees); 0032 R8 → `TmUrls.target('core.Job', jobId)`; 0034 §9 → "0028's Jobs page". Owner
  0028.
- **M-57** — Sharing notifications and toasts. 0033 §7 registers `core.report.shared` with `Toast`
  true; 0028 asks `Toast = false` ("shares never toast"); 0035 E5 registers `core.dashboard.shared`
  with `Toast = false`, "inbox and email as `core.report.shared`". Owner 0033 (type), 0028 (policy).
  (R-0028-33.)
- **M-59** — Page shortcuts. 0031 R12 binds `F2`, `Mod+S`, `Alt+PageUp`/`Alt+PageDown`; 0027 §4.3
  admits only `Mod+<key>`, `Mod+Shift+<key>`, `Mod+Enter` for pages and treats `F2` and Alt-arrows
  as grid keys. Owner 0027. (R-0031-R12.)

### Details, settings and stock pages

- **M-40** — Details-source seam. 0032 §12 sketches `TmDetailsSource` and `tm-details-page` in
  `@tellma/core/details`; 0031, which owns that entry point, defines neither. Owner 0031.
  (C-0032-6, R-0032-R1.)
- **M-41** — Action dialog contract. 0031 §10.4 `injectTmActionDialog`: `run(args: A, form?:
  FieldTree<A>): Promise<boolean>`; a dialog appears only for actions with arguments. 0032 R5: a
  zero-argument action may have a dialog; `run(args, { form?, keepOpen?, ids? })` resolves to the
  outcome; `close()`. 0030 §12.2 bulk actions open "its declared `dialog` (0031's hatch)" over
  several ids. Owner 0031.
- **M-42** — `tm-details-tabs`. 0031 §6.6 `TmDetailsTabs { id }`; 0032 R6 binds a `selected` model.
  Owner 0031.
- **M-43** — `TmDetailsContext` members. 0031 §4.1 has no `actions`; its `can` covers `edit`,
  `create`, `delete`, `activate`, derived from `TmPermissions.allows`. 0035 R8(a) wants `actions`;
  R8(b) wants `can` from a `permissions` extra; 0034 §10 and 0035 §12.2 gate on `permissions.save`,
  `.share`, `.publish`, `.transfer`, outside `can`. Owner 0031.
- **M-45** — `tm-value` input. 0029 §12.1 `property: TmPropertyDescriptor | TmPathResolution`; 0032
  R10 passes a `TmValueDescriptor` for setting values that have no entity. Owner 0029.

### Client Queryex and the engine (0029, 0033, 0010-F)

- **M-46** — Client Queryex and display-name members used and undefined. `tmQx.raw(text)` (0033 R1,
  0034 R4); the clause `'constant'` (0034) against 0029 §10.1 `TmQxClause = 'select' | 'filter' |
  'having' | 'orderBy'`; `terms(clause)` (0034); `TmEntities.enum(name)` (0034);
  `TmExpressionEditor.select(start, length)` (0030) against `insertAtCaret`, `focus`;
  `tmDescribeFilter` (0030); `TmDisplayNames.rememberRows` (0030) against `remember(entity, rows)`.
  Owner 0029.
- **M-47** — Level navigations in metadata. 0033 F2 and R1(2): levels are navigations "declared on
  the entity", and `TmNavigationMetadata.kind` gains `'level'`; 0010-F F.9: levels are counted
  (`TreeLevels`), resolved by name, "absent from `Navigations`", and R-0010F-2 asks 0029 to treat
  `Level{j}` as a self-navigation; 0029 has `kind: 'reference' | 'owner' | 'sibling'` and no level
  count. Owners 0010-F (engine), 0029 (client).
- **M-75** — Part F as written by 0033 §6 and 0010-F (0010-F §2 deviations table):
  - citations F1–F8 vs §F.8–§F.15;
  - F1 `KeyGroups` required (a caller error without them) vs optional (one group per key);
  - `Having` needs set 0 to be every group, vs some set holding every group;
  - at most 256 grouping sets (0033) vs 4,096 (0010-F);
  - F2 levels declared as navigations vs counted;
  - F3 whole-atom folding vs that plus dropping bare omitted list elements;
  - F4 "at the current language version" vs `QueryCompilationOptions.LanguageVersion`;
  - QX3304 vs QX4009 for a path where none is allowed;
  - F7 one `OUTER APPLY` per operand vs a `TOP (1)` seek, prologue variables and QX3104;
  - F8 `Exists` diagnostics vs caller errors;
  - `OmittedParameters` typed `IReadOnlySet<string>` (0033) vs `IReadOnlyCollection<string>` (0010-F).

  Owner 0010-F (Part F, ruling 2). The skeleton (A§6.4 `IsDescendantOf` rollup) matches neither.
  (C-0033-1, C-0033-2, C-0010F-1, C-0010F-2, R-0010F-1.)

### Reports and dashboards wire (0033, 0034, 0035)

- **M-70** — Id widths. 0033 `ReportRunRequest(long? Id, …, long? DashboardId)`, `ReportExportRequest(…,
  long? DashboardId)`, `AudienceGrant(… long? DashboardId …)`, `AllowsAsync(long dashboardId, …)`,
  `DashboardsUsing → (long Id, string Title)`; inside 0033, though, `ReportSource.Stored(int Id, long?
  DashboardId)` and `IAudienceResolver.ResolveAsync(string resource, int id, …)`. 0035 §3.1 tables use
  `int` (`DashboardId`, `ReportDefinitionId`), and R3(a) asks for `int`; 0034 `TmReportRef { id:
  number }`. Owner 0033 (wire), 0035 (tables). (R-0035-R3(a).)
- **M-71** — Run by code. 0034 `TmReportRef` has `{ code }` and R1(b) asks `Code` beside `Id` and
  `Draft` on `ReportRunRequest` and `ReportExportRequest`; 0033 §8.1 takes exactly one of `Id` and
  `Draft`. Owner 0033.
- **M-72** — Coded-report argument names. 0034 §2.2 declares `parameters: { AsOf, Customer }` and sends
  "members = the keys in camelCase"; 0034 R1(e) expects `AgingArguments(DateOnly? AsOf, int?
  Customer)`; 0033 §11 writes `AgingArguments(DateOnly? AsOf, int? CustomerId)`, so the member does not
  match the key. Owner 0033 (C# side). (C-0034-1.)
- **M-73** — `ReportResult` members read by renderers. 0034 R1(a) reads `ReportResult.Id`, `.Root`,
  `.Chart` and `ReportColumn.Expand`; 0033 §9.1's envelope is `(Kind, Title, Arguments, Columns, Table,
  Cube, Drill, ExecutedAt)`. Owner 0033.
- **M-74** — Parameter type vocabularies. 0033 §4.1 `type?: 'Numeric' | 'String' | 'Bool' | 'Date' |
  'DateTime' | 'DateTimeOffset' | 'Guid'`, which 0035 `DashboardParameter.Type` reuses; 0034
  `TmCodedParameter.type?: 'string' | 'number' | 'bool' | 'date' | 'datetime' | 'instant' | 'guid'`;
  skeleton A§3.4 `type: 'date'`. The 0034 §4 bar and codec read both vocabularies. Owners 0033
  (stored), 0034 (coded).
- **M-76** — Calendar of month, quarter and year terms. 0033 §3.2 renders them "in the viewer's
  calendar"; 0034 R1(d) asks `ReportTerm.format.calendar` (`gc`, `uq`, `et`) and 0010 D.7 styles in an
  explicit calendar. Owner 0033. (R-0034-R1(d).)
- **M-77** — `TmReportHost` surface used by 0035. 0035 uses inputs `[kind]` and `[held]`, the widget
  title, waiting copy, and imports `tm-report-parameter-bar`, `tm-report-parameter-field` and a codec
  over dashboard declarations; 0034 §3.1 has none of these inputs, and §1 exports `TmReportHost`,
  `injectTmReportHost`, `TmReportDrillEvent`, `TmShareDialog`, `tmReportArgsFromUrl`/`ToUrl` only.
  Owner 0034. (R-0035-R4.)
- **M-78** — Audience shape in the share dialog. 0034 §10.4 `TmShareDialog` reads `audience` as 0033's
  `ShareAudience`, with data `{ segment: 'reports' | 'dashboards', id, title }`; 0035 returns
  `DashboardAudience(ShareAudience Audience, IReadOnlyList<WidgetAudienceWarning> Widgets)`, a wrapper
  the dialog does not read. Owners 0034 (dialog), 0035 (route).
- **M-79** — Per-row ability extra. 0034 R1(c): a details extra `permissions { save, delete, activate,
  share, publish, transfer }` on `reports/get`, read as `permissions.save`; 0035 R3(b): a generic
  `SharePermissions(Save, Delete, Activate, Share, Publish, Transfer)` carried as
  `DashboardView.Permissions` inside the `view` extra and read as `view.permissions`. 0033 defines
  neither. Owner 0033.
