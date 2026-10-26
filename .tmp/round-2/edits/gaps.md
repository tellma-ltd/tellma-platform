# Gaps in the round-2 edit set

Two lists: (1) skeleton rows (`design/00-architecture.md` §6.3, §6.4) that no design note details,
or details only in part, differently, or at the wrong section; (2) existing-spec text that a note's
design contradicts but that no note lists as an edit. Item ids `X-…` and row ids `rN`/`pX` are
`consolidated.md`'s. Nothing here decides anything; each entry names the text, the design it
contradicts, and the likely owner of the fix.

## 1. Skeleton rows not (fully) detailed

### 1.1 §6.3 rows (edits to 0010–0022)

Every one of the 45 rows has at least one detailing note edit. These rows are detailed only in
part, at another section, or differently:

| Row | What is missing or different | Item |
|---|---|---|
| r1 (0011 §3.2, §5.5) | §3.8's reader "the sign-in redirect that skips the picker for a single live membership (§5.5)" is not edited (only renamed) | X-0011-5; G1 |
| r6 (0011 §5.9, §8) | the closing paragraph of 0011 §5.9 (spec 0010's eight deltas) is not updated; N26 adds `support.providerAccess` and a `telemetry` member the row does not name | X-0011-15; G2 |
| r7 (0011 §1.2, §6.6) | the fixed `https://localhost:4200` origins (0011 §1.2, 0018 §6.2) and 0018 §6.3's restated `pnpm install` are not edited | X-0011-1, X-0011-18; G3, G4 |
| r10 (0012 §6.1, 0015 §14.1) | 0016 §8.3 states the opposite rule for persists and is not edited | G11 |
| r12 (0013 §2.3, §2.4, 0021 §6.3) | the bullet is 0013 §2.5 (N25 targets it correctly); N25 sends `value` on user-level events, which the row excludes (`CONFLICT`); 0013 §12's test line is not edited | X-0013-2; G7 |
| r14 (0013 §2.8) | 0013 §1.1 and §12 still say "on every `migrate`" | X-0013-4; G6 |
| r17 (0013 §6.3) | the row says "per-category expected stamps"; N32 designs per-entry stamps (`CONFLICT`) | X-0013-11 |
| r18 (0013 §10.4, 0021 §2.5) | 0016 §8.2, §8.5, §9.4 and 0021 §8 still describe the two removed routes | X-0016-1; G13, G21 |
| r21 (0014 §7, 0016 §4.3, §6.1) | 0016 §3.4's `TellmaHeaders` gains no `Activity` constant | X-0016-21; G15 |
| r22 (0014 §10, 0016 §3.4) | 0014 §2's vocabulary bullet and §10's two fingerprint mentions are not edited | X-0014-6, X-0014-15; G8 |
| r27 (0016 §3.10) | the `Tellma-Build` bullet ("no rule compares", "every tenant-surface response") is not edited | X-0016-18; G12 |
| r30 (0017 §2.3, §3) | the core-kinds list is 0017 §1.3 (N26 targets it correctly); nothing in §3 changes | X-0017-1 |
| r34 (0018 §1.2, §6) | the languages/calendars seed is designed in 0011 §8 and 0013 §5.7, not 0018 (`TARGET`); N25's request that 0024 E21 add a `[Cacheable] acme.Currency` is not in N24 E21, though N32 E9 references `acme.Currency` | X-0011-19, X-0013-8, X-0018-1; G31 |
| r35 (0019 §2.1, §4.1) | "Import template stays `export-for-import` with `Ids = []`" needs no edit: 0019 §2.1 (line 329) already says an empty list yields the template | X-0019-3 |
| r38 (0020 §7.2) | `ScheduleService` and its extras live in 0020 §7.8; N32 edits only §7.2 | X-0020-9 |

### 1.2 §6.4 rows (amendments in 0010)

Every row has a detailing design (0010-BE, 0010-F, or N24 §6 for Part A). These differ from the
row's text (all flagged `CONFLICT` or `TARGET` in `consolidated.md`):

| Row | Difference | Item |
|---|---|---|
| pB1 | "0002 §4, §8": §8 is the performance budget; BE targets §4 and §1; A§9's "tokens" for breakpoints are not shipped | X-0010B-3 |
| pB4 | "`tm-select` multi-select": BE ships a sibling `tm-multi-select` | X-0010B-2 |
| pD2 | "`TmModal` … live `direction`": BE ships no `direction` option; the root `Directionality` is live (Part B) | X-0010D-10, X-0010B-8 |
| pD4 | "sparing the boot record's device fields": BE's `keep` is per whole name; N25 and N24 assume otherwise | X-0010D-5 |
| pF1 | "context slots" counted as an engine amendment: F records no engine change | X-0010F-6 |
| pF3 | "tree-rollup grouping key over the dimension's node closure": F and N33 use level navigations | X-0010F-10 |
| pF4 | "folds to true": F folds by polarity and drops list elements | X-0010F-11 |
| pD1 vs A§14 ruling 3 | a new entry point (`overlay`) for existing code, which ruling 3 assigns to 0023 | X-0010D-1 |

### 1.3 Amendments the notes request that §6.4 does not list

None of these has an A§6.4 row; those marked `UNDESIGNED` have no design in `0010-BE.md` (ruling
A§14.2 makes BE their designer), most because the request arrived after BE was written.

| Part | Item | Requested by | Designed? |
|---|---|---|---|
| A | `return_url` on the return link; `ui_locales` through PAR | N24 A2, A3 | yes (N24) |
| B | shared Trusted Types policy `tellma` | N24 §6 | `UNDESIGNED` (X-0010B-5) |
| B | `xs` touch-height rule for form fields | N23 A8 | `UNDESIGNED` (X-0010B-7) |
| C | `scrollToRow` `column`/`focus` | N31 | `UNDESIGNED` (X-0010C-4) |
| C | `footerRows` | N34 C.13 | `UNDESIGNED` (X-0010C-5) |
| C | header underline while expanded | N30 | `UNDESIGNED` (X-0010C-9) |
| C | `TmGridDriverTarget` in `contracts` | N30 | `CONFLICT` with BE C.2 (X-0010C-10) |
| C | printable keys pass through | N30 | `UNDESIGNED` (X-0010C-11) |
| C | `TM_GRID_KEYS`, `TM_GRID_KEY_HELP` | N27 M5 | `UNDESIGNED` (X-0010C-12) |
| C | `rowActivate` payload = `TmItemActivation` | N23 A7 | `CONFLICT` with BE C.1 (X-0010C-13) |
| C | menu `icon` as a registry name; `shortcut` as `TmKeys` | N23 A5, N27 M3 | `CONFLICT` with BE C.10 (X-0010C-14) |
| C | menu `layout: 'segmented'`, `cssClass`, 44 px rows below `sm` | N27 M1, M2, M4 | `UNDESIGNED` (X-0010C-14) |
| C | `rowSelectable` | N34 C.12 | `UNDESIGNED` (X-0010C-16) |
| C | sibling order in tree flattening | N30 | `UNDESIGNED` (X-0010C-18) |
| D | relative-time function shape and thresholds | N23 A3 | `CONFLICT` with BE D.4 (X-0010D-2) |
| D | `month`, `quarter`, `year` formats | N34 D.7 | `UNDESIGNED` (X-0010D-3) |
| D | `weekday` style, `tmZonedParts` | N35 | `UNDESIGNED` (X-0010D-4) |
| D | `tmButton` sizes `tm-icon` | N23 A4 | `UNDESIGNED` (X-0010D-6) |
| D | `tm-image` `placeholderOnError` | N23 A2 | `UNDESIGNED` (X-0010D-8) |
| D | `tm-tab-group` `prerender` | N31 | `UNDESIGNED` (X-0010D-9) |
| D | `TmModalConfig.role`, `describedBy` | N23 A1 | `UNDESIGNED` (X-0010D-11) |
| B, E | chips in the multi-select field (`tm-chip-set overflow="collapse"`) | N23 A6 | `CONFLICT` with BE B.4, E.2 (X-0010B-2, X-0010E-3) |
| E | `TmEntityPickerPage.context` | N29 | `UNDESIGNED` (X-0010E-4) |
| E | grid `entity` column `itemDescription` | N29 (optional) | `UNDESIGNED` (X-0010E-5) |
| F | parameter expressions, `BindParameters`, `FilterTree.Exists` | N33 F4, F6, F8 | yes (F.11, F.13, F.15; F challenge 2) |

## 2. Existing-spec text the designs contradict, listed by no note

Ordered by target file. "Owner" is the note whose design forces the change.

| # | File §, line | Current text (brief) | Contradicted by | Owner |
|---|---|---|---|---|
| G1 | 0011 §3.8, 822–824 | "Readers: `GET /bff/user` (the company picker), the sign-in redirect that skips the picker for a single live membership (§5.5)" | N24 E8(a): the BFF always redirects to `returnUrl ?? "/"`; the SPA's launch redirect reads `/bff/user` | 0024 |
| G2 | 0011 §5.9, 1478–1482 | "The identity-server changes this spec and its siblings depend on — the `Distribution` seed client kind, … the versioned management routes — are specified by spec 0010" | 0010 is retitled and Part A gains the account entry URL (X-0010-1, X-0010A-1) | 0024 / 0010 editor |
| G3 | 0011 §1.2, 195; 0011 §6.6, 1611–1615; 0018 §6.2, 866 | "`Tellma:PublicOrigin` in `appsettings.Development.json` is `https://localhost:4200`"; "opens `https://localhost:4200/`"; "`Authority = https://localhost:4200/id`" | N24 E3: the dev-server port comes from `.dev-ports.local` (`<SLUG>_CLIENT`), 4200 only as the fallback | 0024 |
| G4 | 0018 §6.3, 873–877 | "Spec 0011 §6.6 gives the commands (`pnpm install` in the Client project, …)"; the step list `core.bootstrap-administrator`, `core.settings`, `core.blob-container`, `gl.sample-centers`; "the centers tree lists the eight sample rows on tenant 1" | N24 E3 (repo-root install); N33 E15, N35 E2 (`core.report-seeds`, `core.dashboard-seeds`); N30 E8's 40-node development tree (whether it replaces or adds to the eight sample rows is unstated) | 0024, 0030, 0033, 0035 |
| G5 | 0013 Goals / Non-goals, 79–80 | "A per-tenant default culture, a pack-level language declaration, and the `uq` month-map engine amendment — deferred" | N33 E14 and F.14 ship `core.CalendarMonths` and `'uq'` | 0033 |
| G6 | 0013 §1.1, 96; 0013 §12, 1627 | "Runs the `core.VersionTags` seed and wholesale bump (§2.8) on every `migrate`"; "seeding and the wholesale bump of `core.VersionTags` on every `migrate`" | N25 E4: the wholesale bump only after a run that changed the database | 0025 |
| G7 | 0013 §12, 1603–1608 | "a `settings` or `entity:*` bump publishes one `cache.changed` per name after the commit …, a `permissions` bump none" | N25 E1: `permissions` and user-level bumps publish | 0025 |
| G8 | 0014 §2, 157–158; 0014 §10, 1495 and 1519 | "**Fingerprint** — … It changes only on deploy and travels in `me`"; "… and the registry `Fingerprint`"; MCP "the securables fingerprint" | A§6.3 r22, N32 E5, E7: `MeResult.SecurablesFingerprint` and `WhoamiResult.SecurablesFingerprint` are removed | 0032 |
| G9 | 0014 §5.3, 921–923 | "Users, jobs, exports, imports and notifications are self-scoped by a bespoke criterion" (N33 E3 adds reports and dashboards) | A§6.2: `core.Assignments` has a "self-scoped criterion" (0028); `core.SupportRequests` (0026) is per-user too | 0028, 0026 |
| G10 | 0014 §8.2, 1334 | the companion `INSERT [core].[UserStamps] (…, [InboxSeenAt])` | N28 E14 adds `AssignmentsSeenAt` (nullable; the insert may stay, but the column list is the spec's record) | 0028 (low) |
| G11 | 0016 §8.3, 1270–1274 | "The data-access executor receives `HttpContext.RequestAborted` on every command …; on cancellation SqlClient sends an attention, the transaction is rolled back" | A§6.3 r10, N24 E11, E12: a sent `Persist` runs with `CancellationToken.None` to completion | 0024 |
| G12 | 0016 §3.10, 763–766 | "`Tellma-Build`. Every tenant-surface response carries `Tellma-Build` … a diagnostic for support tickets and logs that no rule compares." | A§6.3 r27 ("the client tells server-newer from server-older"); N24 E19 (stamped on every response, tenantless included) | 0024 |
| G13 | 0016 §8.2, 1258; §8.5, 1290–1291; §9.4, 1336–1338 | `tellma-anonymous` covers "strings, the notification catalogue"; the two routes "carry the immutable-or-`no-store` header"; "`MapTellma` maps … `/api/strings/{language}` … and `/api/notification-catalogue`" | A§6.3 r18, N24 E13–E15: both routes are removed | 0024 |
| G14 | 0016 §4.6, 953–955 | "Every `[ApiAction]`/`[EntityAction]` segment … is not a standard segment" | N33 E5 (0015 §2.3): an action may use a standard segment the stack does not project (`reports/export`, `reports/export/start`, A§6.1) | 0033 |
| G15 | 0016 §3.4, 515–523 | `TellmaHeaders` constants (`TimeZone`, `Calendar`, `Client`, `VersionTags`, `Build`, `TraceId`, `Contract`) | N24 E17 adds the request header `Tellma-Activity` | 0024 |
| G16 | 0016 §8.3, 1268–1269; §4.3, 889–893; §1.3 options | `tellma-long` "on `export`, `export-for-import`, `import`, `inspect-import` and the blob upload"; the options class | N33 E9, N35 E4: the long timeout and a `tellma-reports` policy (8 concurrent, queue 16) on `reports/run`, `reports/export`, `reports/audience`, `dashboards/audience` | 0033 |
| G17 | 0016 §11.5, 1502–1503 | `tellma_query` / `tellma_aggregate` take `filter?` | N30 E1: `QueryRequest.Filter` becomes `Filters`; the tool-to-request mapping is unstated | 0030 |
| G18 | 0016 §3.3, `DeleteByQueryRequest(string Filter, …)` | a single filter | N30 E1, E3, E4 move `QueryRequest`, `ParentIdsRequest` and `ExportSourceRequest` to `Filters`; not a contradiction (bulk delete by query is deferred, N30 §1), a consistency gap | 0030 (low) |
| G19 | 0016 §4.1, 828–834 | `ApiResourceAttribute { Segment, Description, Mcp, ContractRevision }` | N33 E4 uses `[ApiResource(Queryable = false)]` | 0033 |
| G20 | 0015 §2.3 vs 0016 §4.1, 837–839 | `RelatedSelectAttribute`, `DetailsExpandAttribute` are declared in 0016 §4.1 | N29 E9 declares them again in 0015 §2.3 as "referenced-but-undeclared" (`CONFLICT` on X-0015-5) | 0029 |
| G21 | 0021 §8, 1108–1109 | "beside them sit the hub at `/{tenantId}/hub` and the tenantless `GET /api/notification-catalogue` (§2.5)" | N24 E14 removes the route | 0024 |
| G22 | 0021 Context, 26 | "carrying thin events (`inbox.changed`, `job.changed`, `cache.changed`, `session.ended`)" | N28 E4 adds `assignments.changed` | 0028 |
| G23 | 0021 §6.3, 952 | `job.changed` publisher "spec 0020 (claim, progress-carrying renewal, completion, the job actions)" | N28 E8: also `Append` and the immediate renewal on a 5-point jump | 0028 |
| G24 | 0022 §4, 317–321 | Sara's email has "the link `https://app.example/42`" | N24 E23: the link is `ITellmaLinks.Target(…)` (`/{t}/{segment}/{id}` when the target has details) | 0024 |
| G25 | 0022 §8, 484–485 | "the standard search, default order `CreatedAt DESC`" | N32 E21 removes the page bullet; the order needs `[Stack(DefaultOrderBy = …)]` (N29 E9) on 0022 §2.1 or it is lost | 0032 |
| G26 | 0020 §7.9 table | ids 1–6 only, "the record of every Core built-in's id" | pre-existing: 0022 §7.3 ships id 7 (`45 3 * * *`); N28 E10 adds id 8 under it | 0028 (with the row-8 edit) |
| G27 | 0018 §7, 886–900 | the initial migration's table list ends at `Exports`, `Imports`, `gl.Centers` | new core tables and columns: `core.Assignments` (0028), `core.ReportDefinitions`, `core.ReportDefinitionShares` (0033), `core.Dashboards`, `core.DashboardWidgets`, `core.DashboardShares` (0035), `core.SupportRequests` (0026), `core.CalendarMonths` with `HasData` (0033 E14), `core.Jobs.LabelKey`/`LabelArgumentsJson` (0028), `core.UserStamps.AssignmentsSeenAt` (0028) and their UDTTs | each owner; 0018 §7 editor |
| G28 | 0019 §12.2 (after X-0019-5) | the codec's `/start` routes set a job `Label` | N33 E11's report `export/start` enqueues the same `core.export` handler without a stated `Label`, so the bell's "In progress" row has no label for report exports | 0033 |
| G29 | 0011 §2.1 / `CoreFeature` | no `ITellmaLinks` service | N24 E22, E23 call `ITellmaLinks` (`Tellma.Core.Abstractions.Composition`), defined only in the new front-end spec 0024 §9.6; no existing server spec registers or owns it | 0024 |
| G30 | 0013, 0014, 0016, 0021 composition sections | no `IClientAssetContributor` | N24 §7 asks all four to write their metadata sections through it; edits exist only for 0013 §1.2 (setting keys, X-0013-1), 0014 §10 (securables, X-0014-15), 0020 §3.7 (job handlers, X-0020-4), 0021 §2.5 (catalogue, X-0021-6); 0016 (the contract fingerprints the SPA reads, X-0016-18) states no contributor | 0024 |
| G31 | 0018 §1.2 | Acme composition | N25 §7 request 4 asks 0024 E21 for a `[Cacheable] acme.Currency` (target of `Customer.Currency`); N24 E21 omits it; N32 E9's `acme.sales.defaultCurrencyId` references it | 0024 |
| G32 | 0010 citations in 0011, 0016, 0018 | "spec 0010 §6", "§7", "§8", "§9" (about 15 places) | X-0010-1: the citations stay valid only if Part A keeps its section numbers in the restructure | 0010 editor |
| G33 | `ARCHITECTURE.md`, 817 | links `docs/specs/0010-identity-server-amendments.md` | X-0010-1 renames the file (A§6.5's scope, not a spec) | 0010 editor |
