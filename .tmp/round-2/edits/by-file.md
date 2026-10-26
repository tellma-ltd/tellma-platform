# Edit worklists by target file

One block per target spec file: the `consolidated.md` item ids its editor applies, in section
order. Flag letters: **C** conflict to settle first, **D** merged duplicate, **K** cascade into
another section, **U** undesigned (0010-BE must design it), **T** the requester named the wrong
section. Unlisted cascades are in `gaps.md` (G-numbers given per file).

## 0010 — `docs/specs/0010-identity-server-amendments.md → 0010-amendments-to-implemented-specs.md`

**whole spec** (1)

| Id | Target and change | Flags |
|---|---|---|
| X-0010-1 | rename and restructure into parts | C K |

**Part A (0003)** (4)

| Id | Target and change | Flags |
|---|---|---|
| X-0010A-1 | 0003 §11.2: Account & Security entry URL | K |
| X-0010A-2 | 0003 §11.2: the return link honours `return_url` |  |
| X-0010A-3 | 0003 §11.2 / sign-in: `ui_locales` through the authorization request | K |
| X-0010A-4 | Part A tests and Definition of done |  |

**Part B (0002)** (9)

| Id | Target and change | Flags |
|---|---|---|
| X-0010B-1 | 0002 §1 (lint): `tm/prefix-exports` scope |  |
| X-0010B-2 | 0002 §3.4: multi-select and option groups | C |
| X-0010B-3 | 0002 §4 (new subsection): breakpoints | C T |
| X-0010B-4 | 0002 §4: `TmTheme` and the System theme |  |
| X-0010B-5 | 0002 §4 or §2.1 (unassigned): a shared Trusted Types policy | K U |
| X-0010B-6 | 0002 §5: `provideTellmaUi({ translations })` loader seam |  |
| X-0010B-7 | 0002 §5: the `xs` touch-height rule for form fields | U |
| X-0010B-8 | 0002 §7: live root direction | K |
| X-0010B-9 | Part B Definition of done and budgets |  |

**Part C (0004)** (19)

| Id | Target and change | Flags |
|---|---|---|
| X-0010C-1 | 0004 §4, §7: `rowClass` and `rowHeaders` |  |
| X-0010C-2 | 0004 §4: `scrollbar-gutter: stable` |  |
| X-0010C-3 | 0004 §4, §12: `scrollToRow(id, options)` with flash |  |
| X-0010C-4 | 0004 §4, §12: `scrollToRow` gains `column` and `focus` | U |
| X-0010C-5 | 0004 §4, §7, §14: `footerRows` | U |
| X-0010C-6 | 0004 §5.1: `activeRowId` model |  |
| X-0010C-7 | 0004 §6.1, §14: sort state and `aria-sort` |  |
| X-0010C-8 | 0004 §6.1, §8.2, §14: header navigation and the header action slot |  |
| X-0010C-9 | 0004 §6.1: header underline while its action is expanded (optional) | U |
| X-0010C-10 | 0004 §8.2: the keyboard driver `input[tmGridDriver]` | C |
| X-0010C-11 | 0004 §8.2: printable keys pass through a readonly grid | U |
| X-0010C-12 | 0004 §8.2: exported key map | K U |
| X-0010C-13 | 0004 §8.3, §8.8, Decision 18: row activation | C |
| X-0010C-14 | 0004 §8.5: menu trigger, submenus, groups, checkable and link items, header | C D U |
| X-0010C-15 | 0004 §8.8: select-all across pages |  |
| X-0010C-16 | 0004 §8.8: `rowSelectable` | K U |
| X-0010C-17 | 0004 §12, §13.3: tree `expandedIds` and batched `loadChildren` | D |
| X-0010C-18 | 0004 §13.2: siblings render in data order | U |
| X-0010C-19 | 0004 §16: budget re-baseline |  |

**Part D (0005)** (13)

| Id | Target and change | Flags |
|---|---|---|
| X-0010D-1 | 0005 §2.1: public `@tellma/core-ui/overlay` | C |
| X-0010D-2 | 0005 §2.2: date-time, time and relative-time formats; `TM_TIME_ZONE` | C D |
| X-0010D-3 | 0005 §2.2: month, quarter and year formats | U |
| X-0010D-4 | 0005 §2.2: `weekday` date style and `tmZonedParts` | U |
| X-0010D-5 | 0005 §2.3: `TmClientCache.clearAll` sweep | C |
| X-0010D-6 | 0005 §3 (`tmButton`): `tm-icon` children | U |
| X-0010D-7 | 0005 §7.1, §7.2: `TM_BLOB_FETCHER` cancellation | K |
| X-0010D-8 | 0005 §7.4: `tm-image` `placeholderOnError` | U |
| X-0010D-9 | 0005 §9: `tm-tab-group` `prerender` | U |
| X-0010D-10 | 0005 §10: `TmModal` `placement: 'end'`, live direction, the `sm` query | C |
| X-0010D-11 | 0005 §10: `TmModalConfig.role` and `describedBy` | U |
| X-0010D-12 | 0005 §11: `tm-popover` `size` and `panelClass` |  |
| X-0010D-13 | Part D Definition of done and budgets |  |

**Part E (0006)** (6)

| Id | Target and change | Flags |
|---|---|---|
| X-0010E-1 | 0006 Non-goals: lift two non-goals |  |
| X-0010E-2 | 0006 §2, §4.1: two-line options |  |
| X-0010E-3 | 0006 §2, §3, §4, §6: `tm-entity-multi-picker` | C |
| X-0010E-4 | 0006 §6: `TmEntityPickerPage` object form gains `context` | U |
| X-0010E-5 | 0006 §7.1: the grid `entity` column gains `itemDescription` (optional) | U T |
| X-0010E-6 | Part E Definition of done | C K |

**Part F (0008)** (18)

| Id | Target and change | Flags |
|---|---|---|
| X-0010F-1 | 0008 §14, Appendix A, §16, §18, §19, §20: Part F's frame (F.0) | C |
| X-0010F-2 | 0008 §2.1, §13.4: key-set restrictions (F.1) | D |
| X-0010F-3 | 0008 §10.10: `level(node)` (F.2) |  |
| X-0010F-4 | 0008 §2.1 (`CompiledQuery`), §13.4: prologue and body (F.3) |  |
| X-0010F-5 | 0008 §13.1, §13.4, §16: paging slots (F.4) | C K |
| X-0010F-6 | 0008 §10.6, §13.1: context slots (F.5) — no engine change | C |
| X-0010F-7 | 0008 §2.3, §16, §17: versioned leaves (F.6) |  |
| X-0010F-8 | 0008 §2.3: `FilterTree.Via` (F.7) | C |
| X-0010F-9 | 0008 §2.1, §8.3, §11.3, §13.4: grouping sets and the level indicator (F.8) | C D |
| X-0010F-10 | 0008 §3, §6.1, §10.10, §12.2: tree level navigations (F.9) | C K |
| X-0010F-11 | 0008 §6.3, §8.4, §9.2: omitted parameters (F.10) | C |
| X-0010F-12 | 0008 §6.3, §13.1: parameter expressions (F.11) | C |
| X-0010F-13 | 0008 §1.4: constant statements, `EvaluateConstant` (F.12) | C D |
| X-0010F-14 | 0008 §2.4, §4: parameter binding, `BindParameters` (F.13) | D |
| X-0010F-15 | 0008 §3, §10.4: `'uq'` through a host month map (F.14) | C K |
| X-0010F-16 | 0008 §2.3: `FilterTree.Exists` (F.15) | C |
| X-0010F-17 | 0008 §2.4: `ParameterUse.ComparedWith` (F.16) | D |
| X-0010F-18 | Part F public surface and Definition of done | K |

Items: 70. Unlisted cascades in `gaps.md`: G2 (0011 §5.9 paragraph), G32, G33.

## 0011 — `docs/specs/0011-distribution-host-and-multitenancy.md`

| Id | Target and change | Flags |
|---|---|---|
| X-0011-1 | §1.2 "Client project" bullet: one client location and the build | K |
| X-0011-2 | §2.1 contribution items: report seeds, widget types, dashboard seeds | K T |
| X-0011-3 | §3.2 last paragraph: the landing reads memberships in the SPA |  |
| X-0011-4 | §3.3: "company picker" → "tenant picker" |  |
| X-0011-5 | §3.8 membership directory readers | K |
| X-0011-6 | §4.5 listeners: `/bff/logout` terminates the session |  |
| X-0011-7 | §5.1 `MapTellma`: routes and SPA hosting | K |
| X-0011-8 | §5.2 anonymous list |  |
| X-0011-9 | §5.3 endpoint audit: SPA segments and the telemetry prefix | K |
| X-0011-10 | §5.5 BFF endpoints (a): the post-callback redirect; rename | K |
| X-0011-11 | §5.5 BFF endpoints (b): `/bff/login` forwards `ui_locales` | K |
| X-0011-12 | §5.5 BFF endpoints (c): `POST /bff/logout` answers `LogoutResult` |  |
| X-0011-13 | §5.5 BFF endpoints (d): profile cookie v2 and renewal | D K |
| X-0011-14 | §5.5 BFF endpoints (e): the client-caching sentence |  |
| X-0011-15 | §5.9 `DistributionInfo`, admin surface | D K |
| X-0011-16 | §6.1 commands: `emit-client-assets` |  |
| X-0011-17 | §6.2 provisioning steps: report and dashboard seeds | K |
| X-0011-18 | §6.6 local development: one workspace, a variable port | K |
| X-0011-19 | §8 configuration: seed languages, support, telemetry, links | T |
| X-0011-20 | §9 observability: inbound `traceparent`, the relay |  |

Items: 20. Unlisted cascades in `gaps.md`: G1, G2, G3, G29.

## 0012 — `docs/specs/0012-data-access-layer.md`

| Id | Target and change | Flags |
|---|---|---|
| X-0012-1 | Context ¶1 and Goals: the engine amendments live in 0010 Part F |  |
| X-0012-2 | §2.2 annotations: `[DisplayNameSource]` | D |
| X-0012-3 | §2.5 the related projection carries the display name | K |
| X-0012-4 | §2.7 natural keys: citation |  |
| X-0012-5 | §3.1 contract: `DisplayName`, `DisplayNameMetadata`, `IDisplayNames` | K |
| X-0012-6 | §3.2 startup validation: display-name checks |  |
| X-0012-7 | §5.2 `CallerAuthority`: platform composers |  |
| X-0012-8 | §5.4 key capture: citation |  |
| X-0012-9 | §6.1 the command's cancellation token | K |
| X-0012-10 | §6.3 error numbers: `Assignments.AssigneeInvalid` |  |
| X-0012-11 | §10.1 the node scheme: citation |  |
| X-0012-12 | §11.1 the schema provider: `TreeLevels`, `CalendarMap`, citations | D |
| X-0012-13 | §11.2 engine amendments → pointer | C |
| X-0012-14 | §11.3 rows and count: citation and paging slots |  |
| X-0012-15 | §11.4 entity queries: level paths refused in `Select` | K |
| X-0012-16 | Decisions record 25: citation |  |

Items: 16. Unlisted cascades in `gaps.md`: —.

## 0013 — `docs/specs/0013-settings-localization-and-cache.md`

| Id | Target and change | Flags |
|---|---|---|
| X-0013-1 | §1.2 composition: the settings client-asset contributor | K |
| X-0013-2 | §2.5 the bump publishes `cache.changed` (wider, with values) | C K T |
| X-0013-3 | §2.7 the composite `permissions` wire tag; `client/sync` | K |
| X-0013-4 | §2.8 the migrator bumps only after a change | K |
| X-0013-5 | §4.3 `GetWireTagsAsync` row |  |
| X-0013-6 | §5.3 setting keys: descriptors, groups, categories |  |
| X-0013-7 | §5.4 labels: the settings page renders from the metadata |  |
| X-0013-8 | §5.7 provisioning: seeded languages and calendars | T |
| X-0013-9 | §6.1 the settings service contract |  |
| X-0013-10 | §6.2 `client`, `entity-tags` and `details` |  |
| X-0013-11 | §6.3 `save`: stamps per entry, entries-only saves | C |
| X-0013-12 | §6.4 `refresh-caches` publishes user tags too |  |
| X-0013-13 | §6.5 validation codes |  |
| X-0013-14 | §7.6 labels: navigation, collection and action labels; key twins |  |
| X-0013-15 | §8.2 the three systems: `core.CalendarMonths` | D K |
| X-0013-16 | §10.1 files and keys: new key conventions and Core keys | T |
| X-0013-17 | §10.4 the string pack: emitted, not served | K |

Items: 17. Unlisted cascades in `gaps.md`: G5, G6, G7, G30.

## 0014 — `docs/specs/0014-users-roles-and-permissions.md`

| Id | Target and change | Flags |
|---|---|---|
| X-0014-1 | Goals / Non-goals: owner of the engine amendments | T |
| X-0014-2 | §2 vocabulary and conventions: `CoreResources`, `AccessActions` | D |
| X-0014-3 | §3.3 `core.UserStamps`: `AssignmentsSeenAt` | K |
| X-0014-4 | §3.4 first paragraph: pinned pages |  |
| X-0014-5 | §3.4 last paragraph: write-behind policy and the warm writer |  |
| X-0014-6 | §4.1 contracts: the `Fingerprint` row | K |
| X-0014-7 | §4.4 the registry as data source |  |
| X-0014-8 | §5.1 contracts: bulk outcome evaluation |  |
| X-0014-9 | §5.2 step 5: citation |  |
| X-0014-10 | §5.3 bespoke self-scopes | K |
| X-0014-11 | §5.5 child entities as query roots: citation |  |
| X-0014-12 | §7.1, §7.3 the profile carries `ModifiedAt` | K |
| X-0014-13 | §7.8 the initializer: background requests do not stamp | T |
| X-0014-14 | §9 drift: problems through `roles/problems` | K |
| X-0014-15 | §10 "Can I, and why": `me` and the securables section | K |

Items: 15. Unlisted cascades in `gaps.md`: G8, G9, G10.

## 0015 — `docs/specs/0015-crud-service-pipeline.md`

| Id | Target and change | Flags |
|---|---|---|
| X-0015-1 | §1.2 principle 3: the named consumer |  |
| X-0015-2 | §2.2 the descriptor: `DefaultOrderBy`, `DisplayName`, `RelatedSelect` |  |
| X-0015-3 | §2.2 the registry: child query descriptors | K |
| X-0015-4 | §2.2 capabilities vocabulary: `assignable` |  |
| X-0015-5 | §2.3 annotations: `[Stack(DefaultOrderBy)]`; projection attributes | C |
| X-0015-6 | §2.3 annotations: reserved action names | K |
| X-0015-7 | §2.4 standard operations: `extras` | K |
| X-0015-8 | §2.6 startup checks |  |
| X-0015-9 | §2.7 securable registration: child securables |  |
| X-0015-10 | §3.1 `EntityService`: extras |  |
| X-0015-11 | §3.2 composable components: deferred extras |  |
| X-0015-12 | §4.2 wire records this spec consumes |  |
| X-0015-13 | §5.2 query step 3: one leaf per filter; default order |  |
| X-0015-14 | §5.2 query: child query surfaces and the owner-path traversal |  |
| X-0015-15 | §5.2 query: level paths refused in `Select` |  |
| X-0015-16 | §5.3 details: the `AddExtra` row and the extras plan |  |
| X-0015-17 | §5.4 get by parent ids: roots and order in one call |  |
| X-0015-18 | §7.1 contexts: `ActionContext` gains `Assignments` | T |
| X-0015-19 | §7.3 errors and codes |  |
| X-0015-20 | §8 concurrency: Overwrite resends with `Override` |  |
| X-0015-21 | §9.1 type level: citation |  |
| X-0015-22 | §11.2 the general action path: argument error paths | K |
| X-0015-23 | §12 capability recipes: Assignable |  |
| X-0015-24 | §13.1 transactional participation: `PersistContext.Assignments` |  |
| X-0015-25 | §13.3 enlisted writes: composer list |  |
| X-0015-26 | §14.1 the closed set: a sent persist completes | K |
| X-0015-27 | §15 the Excel row source: `Filters` | K |

Items: 27. Unlisted cascades in `gaps.md`: G20.

## 0016 — `docs/specs/0016-crud-web-api.md`

| Id | Target and change | Flags |
|---|---|---|
| X-0016-1 | §2.1 route table: remove the two GET routes; the fallback sentence | K |
| X-0016-2 | §2.1 route table: client telemetry and support requests |  |
| X-0016-3 | §2.1 route table: child-entity query routes |  |
| X-0016-4 | §2.2 client retry contract: the new routes' classes | D |
| X-0016-5 | §2.3 stack operations: `extras` |  |
| X-0016-6 | §2.3 stack operations: the reports stack's extra routes | K |
| X-0016-7 | §2.4 service routes: the Core family's new routes | D |
| X-0016-8 | §3.3 `QueryRequest.Filters`; default order | K |
| X-0016-9 | §3.3 `ParentIdsRequest` |  |
| X-0016-10 | §3.3 `ExtrasRequest` |  |
| X-0016-11 | §3.3 `SaveRequest` comments |  |
| X-0016-12 | §3.4 `MeResult`: no fingerprint; one meaning for the access tag |  |
| X-0016-13 | §3.4 `UserProfileView.ModifiedAt` |  |
| X-0016-14 | §3.4 `QueryDiagnostic.Clause` | T |
| X-0016-15 | §3.5 query rows: message source; the example |  |
| X-0016-16 | §3.6 entity envelopes: `rows` |  |
| X-0016-17 | §3.9 hand-offs: `me` |  |
| X-0016-18 | §3.10 contract fingerprints: computed in `Tellma.Core`, read from the metadata | K |
| X-0016-19 | §4.3 endpoint metadata: `NoActivityStampMetadata` | K |
| X-0016-20 | §4.6 the startup audit: reserved segments | K T |
| X-0016-21 | §6.1 request headers: `traceparent`, `Tellma-Activity`, `Accept-Language` | K |
| X-0016-22 | §6.2 response headers: early stamping; the composite tag |  |
| X-0016-23 | §7.1 the mapping: the cookie challenge's 401 | T |
| X-0016-24 | §7.3 validation error paths: the SPA parses paths |  |
| X-0016-25 | §7.5 framework problems: `route-not-found` |  |
| X-0016-26 | §8.2 rate policies: `tellma-telemetry` | T |
| X-0016-27 | §8.2 rate policies: `tellma-reports` | D K T |
| X-0016-28 | §10.1 instruments: client telemetry names |  |
| X-0016-29 | §11.5 tools: `WhoamiResult` loses the fingerprint |  |
| X-0016-30 | §11.5 tools: `tellma_describe` from the client entity model |  |
| X-0016-31 | §11.5 tools: `tellma_query` over child query surfaces |  |

Items: 31. Unlisted cascades in `gaps.md`: G11, G12, G13, G14, G15, G16, G17, G18, G19, G20, G30.

## 0017 — `docs/specs/0017-blob-storage.md`

| Id | Target and change | Flags |
|---|---|---|
| X-0017-1 | §1.3 composition: the `support-bundle` core kind | T |
| X-0017-2 | §2.3 `[BlobReference]`: policies reach the SPA |  |

Items: 2. Unlisted cascades in `gaps.md`: —.

## 0018 — `docs/specs/0018-core-and-gl-reference-stacks.md`

| Id | Target and change | Flags |
|---|---|---|
| X-0018-1 | §1.2 composition: the reference distribution's additions | K |
| X-0018-2 | §3.1 service and operations: `users/me/locale` |  |
| X-0018-3 | §3.1 composition of the users stack: the `activity` extra |  |
| X-0018-4 | §3.1 records: `MeSaveRequest.Concurrency` |  |
| X-0018-5 | §3.2 hooks: validation for `me/locale` |  |
| X-0018-6 | §3.3 the `invite` action: links through `ITellmaLinks` | K |
| X-0018-7 | §3.5 self-service: `SetMyLocale` |  |
| X-0018-8 | §3.5 self-service: the preference key grammar |  |
| X-0018-9 | §3.5 self-service: `SaveMe` with `Override` |  |
| X-0018-10 | §4 the roles stack: `roles/problems` |  |
| X-0018-11 | §4 the roles stack: the `members` extra |  |
| X-0018-12 | §5.5 `GlFeature`: resources |  |
| X-0018-13 | §6 provisioning and local development: the development seed | K |
| X-0018-14 | §7 reference distribution migrations | K |
| X-0018-15 | §8.1 round trips |  |
| X-0018-16 | §9 testing: `me/locale` vectors |  |

Items: 16. Unlisted cascades in `gaps.md`: G3, G4, G27, G31.

## 0019 — `docs/specs/0019-excel-codec.md`

| Id | Target and change | Flags |
|---|---|---|
| X-0019-1 | §1.3, §2.1: `ExportKind.Report` |  |
| X-0019-2 | §2.1 `ExportSourceRequest` and `ExportSource.ByQuery`: `Filters` |  |
| X-0019-3 | §2.1 `ExportRequest.Columns` |  |
| X-0019-4 | §4.1 the display-shape plan |  |
| X-0019-5 | §12.2, §12.3 enqueue: job labels; `fileId` on the ready notice |  |
| X-0019-6 | §12.1–§12.3 and §4.1: report exports through one writer | K T |
| X-0019-7 | §15 testing: corpus cases |  |

Items: 7. Unlisted cascades in `gaps.md`: G28.

## 0020 — `docs/specs/0020-background-jobs-and-scheduler.md`

| Id | Target and change | Flags |
|---|---|---|
| X-0020-1 | §2.1 `core.Jobs`: label columns |  |
| X-0020-2 | §2.3 the `Job` entity: label members and render rule |  |
| X-0020-3 | §2.3 the `Job` entity: `[RelatedSelect]` |  |
| X-0020-4 | §3.7 registration: `jobHandlers` in the client metadata |  |
| X-0020-5 | §4.1 contracts: `JobRequest.Label` |  |
| X-0020-6 | §4.2 the enqueue statement and `JobRequestList` | T |
| X-0020-7 | §5.2 renew: a progress jump renews at once |  |
| X-0020-8 | §5.3 append: publish on checkpoints | K |
| X-0020-9 | §7.2 `core.ScheduleStates`: readable as the `state` extra | K |
| X-0020-10 | §7.9 built-in schedules: id 8 |  |
| X-0020-11 | §10 hub events: the publisher list |  |
| X-0020-12 | §11.1 `JobService`: `status` |  |
| X-0020-13 | §11.3 endpoints and pages: page owners |  |
| X-0020-14 | §14.4 retention: support requests |  |

Items: 14. Unlisted cascades in `gaps.md`: G26.

## 0021 — `docs/specs/0021-notifications-inbox-and-hub.md`

| Id | Target and change | Flags |
|---|---|---|
| X-0021-1 | Context: assigned work is 0028's | K |
| X-0021-2 | Goals / Non-goals: the same |  |
| X-0021-3 | §2.3 the type registry: `core.report.shared`, `core.dashboard.shared` |  |
| X-0021-4 | §2.3, §4.3: category labels |  |
| X-0021-5 | §2.4 rendering: an optional detail line |  |
| X-0021-6 | §2.5 the catalogue: emitted into the client metadata | K |
| X-0021-7 | §5.2, §5.3 the inbox: actors and job labels |  |
| X-0021-8 | §5.4 the client protocol: leader-driven summary; presentation moves | D |
| X-0021-9 | §6.1 `TellmaHub` and the connect: no activity, no renewal | K |
| X-0021-10 | §6.3 the catalogue: `cache.changed` | C |
| X-0021-11 | §6.3 the catalogue: `assignments.changed` |  |
| X-0021-12 | §8 securables, endpoints, pages | K |
| X-0021-13 | §9.2 `Tellma:Realtime`: client timeout |  |
| X-0021-14 | Decisions record 19 |  |

Items: 14. Unlisted cascades in `gaps.md`: G21, G22, G23, G30.

## 0022 — `docs/specs/0022-email-outbox.md`

| Id | Target and change | Flags |
|---|---|---|
| X-0022-1 | §2.1 the entity: display-name source |  |
| X-0022-2 | §4 the email channel: deep link | K |
| X-0022-3 | §8 admin surface: the page bullet | K |
| X-0022-4 | Review flags: delete flag 1 |  |

Items: 4. Unlisted cascades in `gaps.md`: G24, G25.

## Totals

| File | Items | With a conflict | Undesigned |
|---|---|---|---|
| 0010 | 70 | 23 | 18 |
| 0011 | 20 | 0 | 0 |
| 0012 | 16 | 1 | 0 |
| 0013 | 17 | 2 | 0 |
| 0014 | 15 | 0 | 0 |
| 0015 | 27 | 1 | 0 |
| 0016 | 31 | 0 | 0 |
| 0017 | 2 | 0 | 0 |
| 0018 | 16 | 0 | 0 |
| 0019 | 7 | 0 | 0 |
| 0020 | 14 | 0 | 0 |
| 0021 | 14 | 1 | 0 |
| 0022 | 4 | 0 | 0 |
| **all** | **253** | **28** | **18** |
