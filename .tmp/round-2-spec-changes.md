# Changes to existing specs and ARCHITECTURE.md

This report lists every change made to the existing documents: specs 0010–0022 and
`ARCHITECTURE.md`. The new specs 0023–0035 appear only as the reason a change was needed.

How to read a bullet: **§ — what changed — needed by 00NN (why)**. A bare four-digit number names
a spec.

## Summary

- **Client metadata is emitted at build time.** The Migrator's new `emit-client-assets` writes the
  deployment facts and string packs into the client bundle. So `/api/strings/{language}` and
  `/api/notification-catalogue` are removed. (0011, 0013, 0014, 0016, 0020, 0021; for 0024.)
- **BFF, profile cookie and logout.** The BFF never picks a tenant, `/bff/logout` answers JSON, and
  the v2 profile cookie renews with the session but never on background requests.
  `DistributionInfo` gains five members. (0011; for 0024, 0025.)
- **`cache.changed`, the composite tag and `client/sync`.** Events carry the new tag value and reach
  the affected users. The `permissions` tag combines tenant and user tags. The SPA reads `me` and
  settings through `client/sync`. (0013, 0014, 0016, 0021; for 0025.)
- **Display names.** `[DisplayNameSource]` and `IDisplayNames` give one rule for naming a row, and
  every related projection carries its columns. (0012, 0015, 0019, 0022; for 0029, 0033.)
- **Queries.** `Filters` is a list of leaves, stacks have a default order, child collections are
  read-only query surfaces, and extras can be computed in C#. (0015, 0016; for 0030, 0031, 0033.)
- **The export column model.** Exports take `Columns` and write a navigation as its display name.
  Report exports reuse `core.export` through a table writer. (0019; for 0030, 0033, 0034.)
- **Assignments and jobs for the attention surfaces.** Assignments get a capability, context
  members, a hub event and a seen stamp. Jobs gain labels and a progress push. (0012, 0014–0016,
  0020, 0021; for 0028, 0032.)
- **Settings.** Saves are stamped per stored row, descriptors gain groups and label keys, seeded
  tenants get languages, and `core.CalendarMonths` backs Umm al-Qura. (0013; for 0032, 0033.)
- **Reports, dashboards and Acme.** New contribution items, actions, a rate policy and notification
  types. Acme gains a feature of its own with entities, settings, a report and development data.
  (0011, 0014, 0016, 0018, 0021; for 0033, 0035.)
- **Spec 0010 restructured.** It is now "Amendments to Implemented Specs", Parts A–F. Old citations
  now name Part A (§A.n), and 0012's engine amendments moved to Part F. (0010; cited in 0011–0022.)

Many sections were also rewritten as bullets, tables or numbered steps with the same meaning. That
reflow accounts for much of the line count below. Each file section lists those sections last.

| File | Gist | Lines + / − |
|---|---|---|
| `0010-amendments-to-implemented-specs.md` | Renamed from `0010-identity-server-amendments.md`; Parts A–F | +3,179 / −351 |
| `0011-distribution-host-and-multitenancy.md` | Client project, BFF and cookie, SPA hosting, `ITellmaLinks`, `emit-client-assets`, config | +451 / −222 |
| `0012-data-access-layer.md` | Display names, `EvaluateConstant`, tree levels and `uq` map, Part F pointer | +202 / −144 |
| `0013-settings-localization-and-cache.md` | Metadata sections, `cache.changed` table, per-entry stamps, `CalendarMonths`, label keys | +407 / −175 |
| `0014-users-roles-and-permissions.md` | New actions and resources, securables section, `AssignmentsSeenAt`, background requests | +169 / −86 |
| `0015-crud-service-pipeline.md` | Child query surfaces, `Filters`, default order, computed extras, assignments, `[EntityRef]` | +657 / −325 |
| `0016-crud-web-api.md` | Routes removed and added, headers, error codes, report and telemetry policies, contracts emit | +563 / −288 |
| `0017-blob-storage.md` | `support-bundle` kind, client blob policy, upload CSRF citation | +61 / −28 |
| `0018-core-and-gl-reference-stacks.md` | `AcmeFeature`, `me/locale`, `members`/`problems`/`activity` extras, migration contents | +488 / −170 |
| `0019-excel-codec.md` | `Filters`, `Columns` with display names, report table writer, job labels, `fileId` | +293 / −110 |
| `0020-background-jobs-and-scheduler.md` | Job labels, progress push, `jobHandlers` metadata, schedule extras | +208 / −76 |
| `0021-notifications-inbox-and-hub.md` | Catalogue emitted, actors in the summary, two new types, hub not activity | +197 / −122 |
| `0022-email-outbox.md` | Display name, default order, email links to the target, pages moved to 0032 | +29 / −20 |
| `ARCHITECTURE.md` | App framework, client packages, reports tiers, warm launch, client observability | +114 / −44 |

## 0010 — Amendments to Implemented Specs

The file was renamed with `git mv` from `0010-identity-server-amendments.md` and rewritten. It is
now the one home for changes to the frozen specs 0001–0009, whose text is never edited. Each part
targets one spec and has its own Testing, Definition of done, Decisions and Review flags, so it can
be implemented and merged on its own.

- **Front matter.** A new Context names the parts and their consumers. "How to read an amendment"
  sets the numbering and the Target / Requested by / Why labels.
- **Part A — identity server (0003).** The old text, renumbered: old §1 opens the part, and old
  §2–§9 are §A.1–§A.8. Part A still blocks 0011.
  - **§A.9** (new) — a stable "My account" URL that honours `return_url`. Needed by 0011 §5.9 and
    0027 §10.1.
  - **§A.10** (new) — the sign-in page takes its language from `ui_locales`. Needed by 0011 §5.5
    and 0024 §8.3, so an Arabic client gets an Arabic sign-in.
- **Shared rules for Parts B–E.** The build order, and every changed entry point ships whole:
  golden, harness, story, English and Arabic strings, tokens and budgets.
- **Part B — foundation (0002), §B.1–§B.6.** Needed by 0023, 0024, 0027, 0029 and 0030.
  - Breakpoints, the System theme, a translations seam with live direction, a grouped
    multi-select, the lint scope, and touch sizes.
- **Part C — grid and menu (0004), §C.1–§C.12.** Needed by 0027, 0030, 0031, 0034 and 0023's
  tiles.
  - Row activation, an external active row, sort state, header actions, select-all across pages,
    row classes and a stable gutter.
  - `scrollToRow`, remembered tree expansion with batched loading, a data-driven menu, budgets and
    a published key map.
- **Part D — basics (0005), §D.1–§D.10.** Needed by 0023–0025, 0027, 0028, 0030, 0031 and
  0033–0035.
  - A public overlay entry point; the drawer, roles and live direction on `TmModal`; popover sizes.
  - New date and time formats, a full client-cache sweep, cancellable blob fetches, and image and
    button tweaks.
- **Part E — entity picker (0006), §E.1–§E.4.** Two-line options, a multi-entity picker, a picker
  page context and two-line options in grid cells. Needed by 0029.
- **Part F — Queryex (0008).** All items are additive, so the language version is unchanged
  (§F.0). Needed by 0012, 0014, 0029 §9, 0033 and 0035.
  - **§F.1–§F.7** moved here from 0012 §11.2: key-set restrictions, `level()`, prologue and body,
    paging slots, context slots, versioned leaves and `Via`.
  - **§F.8–§F.15** are new, for reports: grouping sets, level navigations, omitted parameters,
    constant statements, `BindParameters`, `'uq'`, `Exists` and `ComparedWith`.
- **No amendment** is needed for specs 0001, 0007 and 0009.

## 0011 — Distribution host and multi-tenancy

**Client project and local development**

- §1.2 — The Client tree follows 0024 §2.1 (`src/`, `public/`, a gitignored `generated/`).
  `proxy.conf.js` and `aspnetcore-https.js` are gone — needed by 0024.
- §1.2 — The esproj references the Migrator and builds through `tellma-client build`; `pnpm start`
  is `tellma-client serve` — needed by 0024.
- §1.2 — `tellma-client init` scaffolds the project, a member of the repo-root pnpm workspace —
  needed by 0024.
- §1.2 — The dev proxy forwards the server's reserved words (`TellmaRoutes`), not a hand list —
  needed by 0024 §9.1.
- §1.2, §6.6 — Ports come from `.dev-ports.local`, else 4200 (client) and 7052 (web). `AddTellma`
  reads the file in Development — needed by 0024 (parallel worktrees).
- §6.6 — A fresh clone runs `pnpm install` at the repo root; the steps are numbered — needed by
  0024.

**Feature contract**

- §2.1 — New contribution items `ReportSeed` and `WidgetType` / `WidgetType<TSettings>`, realised
  by 0033 and 0035 — needed by 0033, 0035.

**Tenancy, BFF and session**

- §3.2, §3.8 — `/bff/user` serves the membership list to the tenant picker and the SPA's launch
  redirect. The single-membership shortcut is gone — needed by 0024 §9.3.
- §3.3 and throughout — "company picker" is now "tenant picker" — needed by 0024, 0027.
- §4.5, §5.5 — `/bff/logout` first calls `SessionsTerminatedAsync` with the caller's own handle —
  needed by 0024, 0021 (the hub closes that session).
- §5.5 — `/bff/login` always redirects to `returnUrl ?? "/"` and forwards `ui_locales` (0010
  §A.10) — needed by 0024 (the SPA decides where `/` lands).
- §5.5 — `/bff/logout` answers `200 LogoutResult` instead of redirecting, because the SPA calls it
  with `fetch`. It ends with `Clear-Site-Data: "cache"` — needed by 0024.
- §5.5 — `LogoutResult.RedirectUrl` is the end-session URL for `global` and `/signed-out` for
  `local` — needed by 0024.
- §5.5 — Profile cookie v2 is `base64url` JSON `{ v: 2, sub, sh, name, email, locale }`, `Secure`
  and `SameSite=Lax` — needed by 0024, 0025.
- §5.5 — `sh`, the session handle, is a name and never a credential — needed by 0024.
- §5.5 — The profile cookie is re-issued with the session cookie and expires at its idle expiry —
  needed by 0025 (the warm cache and the session end together).
- §5.5 — No cookie renews on background requests: `Tellma-Activity: background`, the hub, the
  telemetry relay — needed by 0025 (an idle tab must not keep the session alive).
- §5.5 — Client caching is 0025's; a `404 tenant-not-found` sends the SPA to the tenant picker —
  needed by 0024, 0025.

**Hosting and routes**

- §5.1 — `UseTellma` now starts with a header stamp, so `Tellma-Trace-Id` and `Tellma-Build` are on
  every response — needed by 0016 §6.2, 0024 §5.6.
- §5.1 — `MapTellma` drops `/api/strings/{language}` and `/api/notification-catalogue`, and adds
  the telemetry relay and admin support routes — needed by 0024, 0026.
- §5.1, §3.1, §5.2 — The SPA follows 0024 §6: hashed assets, metadata chunks behind the cookie,
  `index.html` for navigations only — needed by 0024.
- §5.3 — The audit reserves `/api/client-telemetry` and refuses the SPA's route words (`tenants`,
  or a stack segment `settings`) — needed by 0024, 0026.

**Information document and links**

- §5.9 — `DistributionInfo` gains `Links`, `Support`, `Telemetry` and `Session` (`IdleDays`);
  `IdentityInfo` gains `AccountUrl` — needed by 0024–0027.
- §5.9 — The admin surface becomes a route table and adds the support-request reads — needed by
  0026.
- §5.9 — The identity list cites 0010 Part A, adding the account URL and the sign-in language —
  needed by 0010.
- New §5.10 — `ITellmaLinks` (`Tenant`, `Target`) builds every server-written link in the SPA's
  route grammar — needed by 0018, 0022, 0024.

**Migrator**

- §6.1 — New command `emit-client-assets <dir>` writes 0024's client metadata with no database;
  exit code 3 is a composition problem — needed by 0024.
- §6.2 — New order bands: platform 0–99 and 900+, packs 100–199, distributions 200–899 — needed by
  0033 §13.
- §6.2 — `900 core.report-seeds` runs after every pack and distribution step, so a seed's roles
  exist — needed by 0033 §13.

**Configuration**

- §8 — New keys `Copyright` and `Tellma:Support` (`Enabled`, `RetentionDays`,
  `MaxRequestsPerUserPerDay`) — needed by 0026, 0027.
- §8 — New keys `Tellma:ClientTelemetry` and `Tellma:Links` (the terms and privacy URLs) — needed
  by 0024, 0026, 0027.
- §8, §5.7 — `Seed:Tenants` entries gain `Languages` and `Calendars`; dev tenant 1 gets `en`, `ar`,
  `gc` and `uq` — needed by 0024 (Arabic and Hijri from the first run).

**Observability**

- §9 — Requests adopt an inbound `traceparent` trace id; browser `tracestate` and `baggage` are
  dropped — needed by 0026 (one trace id from browser to SQL).
- §9 — Logs export with scopes, the relay is untraced, and 0026's client alerts join
  `infra/monitoring/` — needed by 0026.

**Records**

- Context adds "Other specs that carry parts of this design"; Non-goals add "The client" (0024,
  0025).
- Decisions 29–31 are added: the BFF never picks a tenant, the idle window is the session's, one
  link builder.
- Testing and Definition of done cover the new rules. Every 0010 citation now names Part A (§A.1,
  §A.2, §A.5, §A.6).
- Rewritten as bullets or tables, same meaning: §3.3, §3.8, §4.5, §5.5 (refresh), §5.7, §5.9, §6.2,
  §9, §10.

## 0012 — Data access layer

**Scope**

- Context, Goals, Non-goals — The engine amendments now live in 0010 Part F; this spec composes
  them — needed by 0010 (one home for frozen-spec changes).

**Display names**

- §2.2 — New `[DisplayNameSource(property, Prefix = …)]`, on the type and inherited — needed by
  0029 (one rule on client and server).
- §2.5 — The related projection always carries the display-name columns. A `[RelatedSelect]` that
  drops them fails startup — needed by 0015, 0019, 0029, 0033.
- §2.5 — The source is the attribute, else the `Name` group, else the first natural key, else `Id`.
  **Example —** `1101 - Cash` — needed by 0029.
- §3.1 — New `EntityMetadata.DisplayName` and `IDisplayNames` (`Columns`, `Render`), the one server
  renderer — needed by 0019, 0021, 0029, 0033.
- §3.2 — Startup checks the source and prefix types, and that a `[RelatedSelect]` keeps the columns
  — needed by 0029.

**Batch and executor**

- §5.2 — New `IDataBatch.EvaluateConstant(items, languageVersion)`: one row from 0010 §F.11's
  statement — needed by 0033 (report parameter defaults).
- §5.2 — The internal channel adds the assignment statements; the `CallerAuthority` rules move
  below the table — needed by 0028.
- §6.1 — Aborting a request cancels reads, validation and retry delays; a sent `Persist` runs to
  completion — needed by 0024 (reads cancel, writes never).
- §6.3 — Error `50422` lists `Assignments.AssigneeInvalid` — needed by 0028.

**Queryex host**

- §11.1 — Tree entities declare `TreeLevels`, with navigations `Level1` … `LevelN` — needed by 0033
  (tree rollups).
- §11.1 — The schema declares `CalendarMap("uq", "[core].[CalendarMonths]")`; the fingerprint
  covers both — needed by 0033 (Hijri grouping).
- §11.2 — The amendment sketch and items 1–7 become a list of the Part F items used, and where —
  needed by 0010 Part F.
- §11.3 — Retitled "Rows, Count and constant statements"; paging slots are `Literal` slots (0010
  §F.4) — needed by 0010.
- §11.3 — `EvaluateConstant` binds the context slots like `Rows`, so a default and its query share
  one `today()` — needed by 0033.
- §11.4 — A `Select` through a level navigation is refused at compose time; filters and orderings
  may use one — needed by 0033.
- §2.7, §5.4, §10.1, Decision 25 — Citations move to 0010 §F.1, §F.3, §F.2 and Part F.

**Records**

- §13 — New tests: `IDisplayNames` against a golden shared with 0029, the level refusal, and
  `EvaluateConstant`. The `Via` binding tests moved to Part F.
- §14 is updated, Decision 35 (display names as metadata) is added, and Review flag 19 is reworded.
- Rewritten as bullets, same meaning: Context, §2.7, §5.2, §6.1, §11.3.

## 0013 — Settings, localization and cache

**Client metadata and string packs**

- §1.1, §1.2 — New `SettingsClientAssetContributor` writes `languages`, `calendars`, `settingKeys`
  and `settingCategories` — needed by 0024, 0032 (tabs render before any request).
- §10.4 — String packs are emitted into the bundle, one per offered language; no route serves them
  — needed by 0024 (the bundle and its strings are one version).
- §10.4 — The tenant shell offers the content languages plus the active one; the tenantless shell
  offers all — needed by 0024.

**Version tags and `cache.changed`**

- §2.5 — The bump statement returns the affected users, and `cache.changed` follows a new per-tag
  table — needed by 0025.
- §2.5 — Tenant tags go to the tenant group; a user's tags go to that user, with the value. Omar
  example — needed by 0025 (a tab holding the value skips the refresh).
- §2.7 — The header's `permissions` tag combines tenant and user tags. User tags are omitted without
  a connect prologue — needed by 0016, 0025.
- §2.7, §4.3 — A changed `entities` tag sends the client to `client/sync`, which also serves the
  entity tags — needed by 0025.
- §2.8 — The wholesale bump runs only after a run that changed the database — needed by 0025 (a
  no-op deploy costs no refetch).
- §6.4 — `refresh-caches` also publishes value-less `permissions` and `preferences` events —
  needed by 0025.

**Setting keys and save**

- §5.3 — `SettingKeyDescriptor` moves here and gains `Group`, `EnumType`, `LabelKey` and
  `DescriptionKey` — needed by 0032.
- §5.3 — New `SettingCategoryDescriptor` and `Describe()`. A nullable key defaults to `null`, and
  groups derive from key names — needed by 0032.
- §5.4 — The settings page takes label keys from the metadata and text from the string pack —
  needed by 0032.
- §5.7 — Seeded tenants get their `Languages` and `Calendars` at provisioning. Acme example —
  needed by 0024.
- §6.1, §6.2 — `ClientAsync()` returns bytes and tag with no `ifTag`; the SPA no longer calls it —
  needed by 0025.
- §6.1 — `TenantSettingsDetails` drops `Keys` and gains `EntryStamps`; `SaveAsync` returns
  `SettingsSaveResult` — needed by 0032.
- §6.3 — Stamps are per stored row: the typed row only when listed, and one stamp per entry —
  needed by 0032 (two admins editing different keys never conflict).
- §6.3, §6.5 — `Override` skips every check. A stale stamp or a concurrent first write is a 409.
  Farah and Omar example — needed by 0032.

**Labels and resource keys**

- §7.1, §7.6 — `ILabelProvider` gains navigation, collection and action labels, and a key-returning
  twin per call — needed by 0029 (the emit writes keys).
- §7.6 — 0029's emit adds the create-label and action-field chains — needed by 0029.
- §10.1 — New key conventions for navigations, actions, `Queryex_<code>`, display names, client
  labels and `Boot_*` — needed by 0024, 0029.
- §10.1 — New key conventions for settings pages, access, and Core report and dashboard keys —
  needed by 0032–0035.

**Umm al-Qura month table**

- §8.2 — New table `core.CalendarMonths`, seeded with the Umm al-Qura months AH 1318–1500 (2,196
  rows) — needed by 0033 (Hijri grouping), 0010 §F.13.
- Goals, Non-goals, §1.2, §2.3 — The table ships here, Part F owns the `'uq'` emission, and the
  table bumps no tag.

**Records**

- §12, §13 — New tests and done items: the bump table, the migrator rule, the seed, stamps, the
  metadata sections and labels.
- Decisions 25–29 and Review flag 23 (the table ends at AH 1500) are added.
- Rewritten as bullets, same meaning: §1.2, §5.5, §5.7, §6.2, §6.3, §10.4.

## 0014 — Users, roles and permissions

**Vocabulary and resources**

- §2 — `AccessActions` gains `Share`, `Publish` and `Transfer`, for report definitions and
  dashboards — needed by 0033, 0035.
- §2 — `CoreResources` gains `Assignment`, `ReportDefinition`, `Dashboard` and `SupportRequest` —
  needed by 0026, 0028, 0033, 0035.
- §5.3 — Assignments and support requests are self-scoped; reports and dashboards use 0033's
  sharing rules — needed by 0026, 0028, 0033, 0035.

**Securables in the client metadata**

- §2, §4.1, §10 — The securables `Fingerprint` leaves `me` and `tellma_whoami` — needed by 0032.
- §1.2, §10 — New `SecurablesClientAssetContributor` writes one `ClientSecurable` per securable —
  needed by 0024, 0032 (the list caches with the build).
- §4.4 — The role editor reads that section, with emitted label keys — needed by 0032.

**Users and preferences**

- §3.3, §8.2 — `core.UserStamps` gains `AssignmentsSeenAt` — needed by 0028.
- §3.4 — Pins are `nav.pinned`, shaped by 0027 §8.2, with no tenant default — needed by 0027.
- §3.4 — Write-behind keys flush per 0025 §9; another writer's change costs one `client/sync` —
  needed by 0025.
- §7.1, §7.3 — `UserProfile` gains `ModifiedAt` — needed by 0032 (My profile's save stamp).
- §7.2, §7.8 — A read sent with `Tellma-Activity: background` stamps no activity — needed by 0024
  (a polling tab is not a user at work).
- §7.5, §10 — The SPA gets `MeResult` through `client/sync`; `AccessSummary.Tag` is the composite
  tag — needed by 0025.

**Drift**

- §9 — Drift comes back in the Role stack's `problems` extra, shown as a banner and row markers —
  needed by 0032.

**Records**

- Non-goals, §1.3, §5.2, §5.5, §15 — The engine amendments are cited as 0010 §F.1, §F.6 and §F.7.
- Non-goals — The client's use of this design belongs to 0025 §9, 0027 §8.2 and 0032 §5, §12.4.
- New tests: the securables golden and a background read. Decisions 27 and 28 are added.
- Rewritten as bullets, same meaning: §1.2, §3.4, §7.8, §9.

## 0015 — CRUD service pipeline

**Scope**

- Context, Goals, Non-goals, §1.1 — The engine amendments are 0010 Part F's; the consumer table
  adds 0028, 0029 and 0033.
- §1.2 — "The SPA schema" becomes "the client metadata's entity section" — needed by 0029.

**Descriptor and annotations**

- §2.2 — `StackDescriptor` gains `DefaultOrderBy` and `DisplayName`; `RelatedSelect` always keeps
  the display-name columns — needed by 0029, 0030.
- §2.2 — The capability vocabulary adds `assignable` — needed by 0028.
- §2.3 — New `[Stack(DefaultOrderBy = …)]`. `[RelatedSelect]` and `[DetailsExpand]` are cited from
  0016 §4.1 — needed by 0029, 0030.
- §2.3 — Only the segments a stack projects are reserved, so a stack without `Export` may name an
  action `export` — needed by 0033.
- §2.6 — New startup checks: a bad `DefaultOrderBy`, a reserved `[ApiAction]` name, and an
  `[EntityRef]` to no stack — needed by 0029, 0033.

**Child query surfaces**

- §1.3, §2.2 — Every child collection of a `Query` stack gets a read-only `query`
  (`ChildQueryDescriptor`) — needed by 0033 (reports over invoice lines).
- §2.2 — `[ApiResource(Queryable = false)]` opts a child out — needed by 0033.
- §2.7 — Each surface registers `Read` alone, owned by its parent stack — needed by 0033.
- §5.2 — `IChildQueryService.QueryAsync` runs the query steps. A path through the owner is checked
  as if the owner were the root — needed by 0029, 0033.
- §5.2 — The traversal rule is now one service, `IQueryTraversalRule`, for `query`,
  `queryex/discover` and `reports/run` — needed by 0029, 0033.

**Queries**

- §4.2, §5.2 — `Filter` becomes `Filters`, each its own leaf with diagnostics at `filters[i]` —
  needed by 0030, 0034 (each filter control owns its leaf).
- §5.2 — A query with no `OrderBy` uses `DefaultOrderBy ?? "Id desc"` — needed by 0030 (stable
  paging).
- §5.2, §7.3 — Selecting through `Level{j}` is refused. New codes `Query.LevelNotSelectable` and
  `Query.PathNotReachable` — needed by 0029, 0033.
- §5.2 — `IncludeAncestors` is ignored on a stack without `tree` — needed by 0030.
- §4.2, §5.4 — `ParentIdsRequest` gains `Filters`, `IncludeRoots` and `OrderBy`, capped at
  `MaxTake` — needed by 0030 (tree restore in one call).
- §4.2, §15 — `ExportSource.ByQuery` takes `Filters` — needed by 0019, 0030.

**Details**

- §3.1 — New `EntityService.Extras` lists the extras a service produces — needed by 0029, 0031.
- §5.3 — An extra is a statement (`AddExtra`) or C# run after the batch (`AddComputedExtra`, fed by
  `AddRead`) — needed by 0018, 0020, 0032, 0033, 0035.
- §5.3 — Computed extras exist because per-row permissions, drift and dashboard views need C#
  without a second round trip.

**Writes and actions**

- §7.1, §11.2, §13.1 — `ActionContext` and `PersistContext` gain `Assignments` — needed by 0028.
- §12, §13.3 — A new "Assignable" recipe, and `IAssignments` joins the internal channel — needed by
  0028.
- §8 — The details page sends `Check`, then `Override` after the user chooses Overwrite — needed by
  0031 §9.1.
- §11.2 — Argument errors land at `Arguments.<Property>` — needed by 0031.
- §11.2 — New `[EntityRef(resource)]` on action arguments, metadata only, so the action dialog
  shows a picker — needed by 0029, 0031.
- §14.1 — A client abort cancels reads and validation only; a sent persist commits — needed by 0024.
- §9.1 — Versioned leaves cite 0010 §F.6.

**Records**

- §16.2, §18 — The budget covers child surfaces, and new tests pin the rules above.
- Decisions 31–34 and Review flag 27 (`[EntityRef]` is metadata only) are added; flag 13 is
  reworded.
- Rewritten as bullets or tables, same meaning: Context, Goals, §1.3, §2.6, §2.7, §8, §9.1, §11.2,
  §13.1, §13.3, §15, §18.

## 0016 — CRUD web API

**Routes**

- §2.1, §8.2, §8.5, §9.4 — `/api/strings/{language}` and `/api/notification-catalogue` are removed
  everywhere — needed by 0024.
- §2.1 — The SPA fallback follows 0024 §6 and the server words of `TellmaRoutes` — needed by 0024.
- §2.1 — New rows: `POST /api/client-telemetry`, the `support-requests` stack and the
  `support-bundle` upload — needed by 0026.
- §2.1, §4.1 — New route `/{tenantId}/api/web/{child-segment}/query`, mapped through
  `IChildQueryService` — needed by 0033.
- §4.1, §4.6, §10.1 — New `[ApiResource(Queryable)]`; child segments are unique; startup logs
  `ApiChildSurfacesProjected` — needed by 0033.
- §2.2 — The retry contract becomes two lists, retried and never retried, covering the new routes —
  needed by 0024–0029, 0033, 0035.
- §2.3 — An action may use a standard segment its stack does not project, such as the reports
  `export` — needed by 0033.
- §2.4 — Core routes become an owner / routes / kind table, with the new client, assignment,
  support, report and dashboard routes — needed by 0025–0029, 0033, 0035.
- §4.6 — A tenant route whose second segment is an SPA word fails startup — needed by 0024.

**Wire records**

- §3.3 — `QueryRequest.Filters` (at most 32) and a default `OrderBy`; `DeleteByQueryRequest` keeps
  one `Filter` — needed by 0030.
- §3.3 — `ParentIdsRequest` gains `Filters`, `IncludeRoots` and `OrderBy` — needed by 0030.
- §3.3 — The `SaveRequest` comments state what the SPA sends for `Select` and `Concurrency` —
  needed by 0031.
- §3.4 — `MeResult` drops `SecurablesFingerprint`, and its `permissions` tag is the composite —
  needed by 0025, 0032.
- §3.4 — `UserProfileView` gains `ModifiedAt`, `QueryDiagnostic.Clause` adds `filters[i]`, and
  `TellmaHeaders.Activity` is new — needed by 0024, 0030, 0032.
- §3.5 — The example sends `filters`; error messages render from `Queryex_<code>` or the code's key
  — needed by 0029, 0030.
- §3.6, §3.9 — The row echo serves a client that holds list rows; `reports/export/start` answers
  202 — needed by 0031, 0033.
- §3.9 — The SPA reads `MeResult` through `client/sync`; `users/me` serves API and MCP callers —
  needed by 0025.

**Contracts and headers**

- §1.1, §3.10 — New `IContractFingerprints` in `Tellma.Core`, so the host's check and the
  Migrator's emit share one map — needed by 0024.
- §3.10 — New `ContractsClientAssetContributor` writes the `contracts` section, pinned to equal
  `contracts.json` — needed by 0024.
- §3.10, §6.2 — `Tellma-Trace-Id` and `Tellma-Build` are on every response, early refusals included
  — needed by 0024, 0026.
- §6.2 — The tag header omits `preferences` and `permissions` without a connect prologue — needed
  by 0025.
- §6.1 — New request headers: `Tellma-Activity: background` (no stamp, no renewal) and
  `traceparent` (adopted) — needed by 0024–0026.
- §4.3 — `client/sync` and `assignments/summary` stamp no activity; the report routes get
  `tellma-long` and `tellma-reports` — needed by 0025, 0028, 0033.

**Errors and policies**

- §5.2, §7.5 — No session is now 401 `unauthenticated` — needed by 0024 (to tell sign-in from
  step-up).
- §7.5 — A route miss under a tenant is 404 `route-not-found`; framework problems become a table —
  needed by 0024 (to spot an outdated client).
- §7.3 — Argument errors are rooted at `arguments`; the SPA splits paths to place errors on fields
  — needed by 0031.
- §1.3, §8.2 — New policy `tellma-reports`: per user, 8 concurrent, queue 16 — needed by 0033, 0035
  (widgets queue instead of failing).
- §1.3, §8.2 — New policy `tellma-telemetry`: per user, 60 batches a minute — needed by 0026.
- §8.3 — `tellma-long` covers the report routes; reads cancel and a sent persist completes —
  needed by 0024, 0033.
- §9.2, §10.1 — The hub adds `assignments.changed`; the relay meters under 0026's names — needed by
  0026, 0028.

**MCP**

- §11.5 — `WhoamiResult` drops `SecurablesFingerprint` — needed by 0032.
- §11.5 — `tellma_describe` adds the display name and default order to an entity's server metadata
  — needed by 0029.
- §11.5 — `tellma_query` reaches child entities and passes `filter` as the one leaf of `Filters` —
  needed by 0030, 0033.
- Goals, §11.9, §14 — The identity amendments are cited as 0010 Part A.

**Records**

- Context adds "Other specs that carry parts of this design"; the SPA's parser and outdated-client
  handling go to 0024.
- §13 — The tests for the removed routes are gone; new tests pin every rule above.
- Decision 27 is extended; Decisions 29–31 (`Filters`, headers everywhere, the reports policy) are
  added.
- Rewritten as bullets or tables, same meaning: §1.1, §2.2, §3.2, §4.1, §4.3, §4.6, §5.2, §5.6,
  §7.3, §7.5, §8.2, §8.3, §8.5, §11.5, §13.

## 0017 — Blob storage

- §1.3 — The Core kinds become a table and add `support-bundle` on `SupportRequest` (`Attachment`,
  `OwnerRead`) — needed by 0026.
- §5.2 — The submitter and holders of `core.SupportRequest × Read` read a `support-bundle` —
  needed by 0026.
- §2.3 — `[BlobReference]` also yields a client policy (`accept`, `maxSize`, `image`, `thumbnail`,
  `readAccess`) — needed by 0029 (checks before upload).
- §8.1 — The upload's `Tellma-Client` rule cites 0011 §5.4; the SPA uploads through
  `TmApi.upload` — needed by 0024.
- Rewritten as bullets or tables, same meaning: §1.3, §2.3, §5.2, §8.1.

## 0018 — Core and GL reference stacks

**Scope**

- Context, Goals, Non-goals, §1.1 — The scope adds Acme's feature, `me/locale` and three extras.
  Reports, pages and the client are non-goals.

**The reference distribution's feature (new §1.4)**

- §1.2, §1.4 — New `AcmeFeature`, requiring GL, to exercise every client path — needed by 0024,
  0025, 0028–0033.
- §1.4 — `acme.Currency` (cacheable) and `acme.Customer` (an enum, a decimal, an avatar, contacts,
  `set-credit-limit`).
- §1.4 — `acme.SalesInvoice` (display name, `[Assignable]`, lines, attachments, `close`, `assign`,
  `post`) and `MyCenter.ManagerId`.
- §1.4 — A `related-documents` extra, the coded report `acme-reports/aging`, and the seed
  `acme.sales-by-center` for role `Sales` — needed by 0031, 0033.
- §1.4 — The channel-only type `acme.invoice.assigned`, five settings in three categories, and resx
  labels — needed by 0028, 0032.
- §6.3 — Development data: a 40-node centers tree, currencies, about 60 customers, role `Sales`
  and 20 invoices — needed by 0030, 0033.
- §6.3 — Three invoices are assigned to the administrator, and `core.report-seeds` runs last —
  needed by 0028, 0033.
- §6.2 — In Development the authority follows the client port, and support requests are on —
  needed by 0024, 0026.
- §5.6 — The GL step runs after the platform steps below 100 — consistency with 0011 §6.2.
- §7 — The initial migration lists, in order, the new `core` tables and columns of 0013, 0020,
  0026, 0028, 0033 and 0035 — needed by those specs.
- §7 — It also adds schema `acme` and Acme's tables; report seeds and development data stay in
  provisioning.

**Users**

- §3.1, §3.5, §8.1 — New action `me/locale`: one round trip, only the given members, no stamp —
  needed by 0027 (the session menu's switches).
- §3.2, §3.5 — An unchanged value writes nothing and moves no tag; new code `Users.LocaleEmpty` —
  needed by 0027.
- §3.1, §3.5 — `MeSaveRequest` gains `Concurrency`, and `SaveMe` skips the stamp check under
  `Override` — needed by 0031, 0032.
- §3.5 — Preference keys allow hyphens and digits (`report.acme-aging.params`) — needed by 0025.
- §3.5 — `Me()` no longer adds the securables fingerprint — needed by 0032.
- §1.2, §3.1 — New `activity` extra (`LastActiveAt`) through `UserActivityContributor` — needed by
  0032 (the Users page).
- §3.3 — Invitation and welcome links use `ITellmaLinks.Tenant` — needed by 0011, 0024.

**Roles (new §4.3)**

- §4.3 — `members` adds the user's image, active flag and state, sorted by name, at most 500 rows
  — needed by 0032.
- §4.3 — `members` is filtered by the caller's user grant and absent when denied — needed by 0032
  (no hidden user leaks).
- §4.3 — `problems` lists drifted permissions, computed in memory; there is no `roles/problems`
  route — needed by 0014, 0032.

**GL**

- §5.1 — The GL strings add `Gl_Center_CodeHint` and `Gl_Nav_Financials` — needed by 0032.

**Records**

- §1.3 — The seams table adds every new dependency; the 0010 citations become §A.7 and §A.8.
- §9, §10 — New tests and done items for `me/locale`, the extras, Acme's data and `assign`/`post`.
- Decisions 14–17 and Review flag 15 (the 500-row cap) are added.
- Rewritten as bullets or lists, same meaning: Context, Goals, §1.2, §3.3, §3.5, §5.1, §5.6, §6.2,
  §7, §9.

## 0019 — Excel codec

**Display export**

- §2.1 — `Filter` becomes `Filters`; `Ids` beside a non-empty `Filters` is refused — needed by 0030.
- §1.1, §2.1 — `Select` and `Headers` become `Columns` (1–100) of `ExportColumn(Expression,
  Header?)` — needed by 0030, 0034.
- §4.1 — A navigation column exports its display name in the reader's language — needed by 0030
  (the sheet holds what the user sees).
- §4.1 — A multilingual group falls back to slot 1, `Id` appears only when listed, and headers are
  derived. Acme example — needed by 0030.
- §5.3 — The display manifest takes `Path` from the expression and marks navigation columns as
  `Reference`.

**Report exports**

- Context, Goals, Non-goals, §1.3 — Report exports reuse `core.export` and the `Export` row; the
  layouts stay 0033's — needed by 0033.
- §2.1, §12.1 — `ExportKind.Report` and `ReportExportJobArguments` are new — needed by 0033.
- §4.4 — New table writer `WriteTableAsync`: header rows, subtotal and total styles, a frozen pane,
  no manifest — needed by 0033, 0034.
- §12.3 — The report case skips the `Read` re-check, because the engine authorizes the run —
  needed by 0033.

**Jobs and notices**

- §12.2 — Every enqueue sets `JobRequest.Label`, such as "Exporting {entity}" — needed by 0028 (the
  Jobs page and bell text).
- §12.2, §12.3 — `core.export.ready` carries `fileId`, and the client downloads from it — needed by
  0028.

**Records**

- §14.3, §15 — The `shape` tag adds `report`; new tests pin columns, the writer, labels and
  `fileId`.
- Decisions 23 (display columns) and 24 (one writer for reports) are added.
- Rewritten as bullets or lists, same meaning: Context, §1.1, §1.3, §2.1, §5.3, §12.2, §12.3.

## 0020 — Background jobs and scheduler

**Job labels**

- §2.1, §2.3 — `core.Jobs` and `Job` gain `LabelKey` and `LabelArgumentsJson` — needed by 0028
  (labels in each reader's language).
- §2.5, §4.1, §4.2 — `JobRequest.Label` is stored by the enqueue; an oversized label throws —
  needed by 0028.
- §2.3 — Render order: the label, else `JobHandler_<HandlerKey>`, else the key. An `entity`
  argument renders as a plural label — needed by 0028.
- §2.3 — `Job`'s related projection carries state and label, so `Import.Job.Status` passes the
  traversal rule — needed by 0032.
- §3.7, §15 — New `JobHandlersClientAssetContributor` writes the `jobHandlers` section — needed by
  0024, 0028, 0032.

**Progress**

- §5.2, §3.3 — A jump of 5 points or more renews at once, at most every 5 s per job — needed by
  0028 (a live progress bar).
- §5.3, §10 — `Append` publishes `job.changed` after the caller's commit — needed by 0028.

**Schedules**

- §7.2, §7.8 — The new `state` extra reads `core.ScheduleStates`, which stays out of Queryex —
  needed by 0032 (the status aside).
- §7.8 — The new computed `permissions` extra makes `delete` and `activate` false on built-ins —
  needed by 0031, 0032 (hide Delete).
- §7.9 — The built-in table adds rows 7 `core.outgoing-email-retention` and 8
  `core.assignment-retention` — needed by 0022, 0028.

**Pages and retention**

- Non-goals, §6.8, §6.9, §7.1, §7.8, §11.1, §11.3 — The Jobs pages are 0028's; the Schedules page
  is 0032's — needed by 0028, 0032.
- §14.1 — `reports/export/start` is a third enqueuer — needed by 0033.
- §14.4 — `core.file-retention` also deletes expired `SupportRequest` rows — needed by 0026.
- Context — `KeySetRestriction` cites 0010 §F.1.

**Records**

- §16 — New tests pin labels, the jump renewal, `jobHandlers`, `Append`, both extras and retention.
- Decision 12 is extended; Decisions 23–25 and Review flags 22–23 are added.
- Rewritten as bullets, same meaning: Context, §3.3, §7.1, §10, §11.3, §14.1.

## 0021 — Notifications, inbox and hub

**Scope**

- Context, Goals, Non-goals, §1.2 — The inbox's server side stays here; 0028 shows it, and 0025 §8
  owns the hub connection — needed by 0025, 0028.
- Context, Decision 19 — Assigned work is 0028's; announcements belong to a later spec.

**Types and the catalogue**

- §2.3 — New types `core.report.shared` and `core.dashboard.shared` (category `reports`, mutable,
  no toast) — needed by 0033, 0035.
- §2.3 — `core.export.ready` adds `fileId` — needed by 0019, 0028 (the download link).
- §2.3, §4.3 — Core categories add `reports`; a category's label is
  `NotificationCategory_<Category>` — needed by 0028.
- §2.4 — An optional detail line, `Notification_<Key>_Detail` — needed by 0028.
- §2.5 — The catalogue route and its types are removed; `NotificationsClientAssetContributor`
  writes three metadata sections — needed by 0024, 0028.

**Inbox**

- §5.2, §5.3 — `InboxSummary` gains `Related`, the latest rows' actors, read in the same batch —
  needed by 0028 (no second call).
- §5.2, §5.3 — `InboxRunningJob` gains `LabelKey`, `LabelArgumentsJson` and `ErrorCode` — needed
  by 0028.
- §5.4 — The leader tab calls `inbox/summary` and stores the `inbox` document; bell and toast rules
  moved to 0028 — needed by 0025, 0028.
- §5.4 — A row opens through 0028's open route, which marks it read — needed by 0028.

**Hub**

- §6.1 — Negotiate and connect stamp no activity and renew no cookie — needed by 0025 (reconnects
  are not user activity).
- §6.3 — `cache.changed` carries `{ tag, value? }` to the recipients of 0013 §2.5 — needed by 0025.
- §6.3 — New event `assignments.changed`; `job.changed` adds the jump renewal and `Append` — needed
  by 0028.
- §9.2 — `ClientTimeoutInterval` goes from 30 s to 150 s, to outlast a hidden tab's throttled
  keep-alive — needed by 0025.
- §8 — The pages are 0028's, and the catalogue has no route — needed by 0024, 0028.

**Records**

- §11–§13 cover the contributor, the actors and the hub rule.
- Decisions 20, 23 and 24 are updated; Decision 27 (the hub is not activity) is added; Review flags
  10 and 17 are updated.
- Rewritten as bullets, same meaning: Context, §2.4, §6.1, §8.

## 0022 — Email outbox

- §2.1 — The entity adds `[DisplayNameSource(nameof(Subject))]` and
  `DefaultOrderBy = "CreatedAt desc"` — needed by 0029, 0032.
- §4 — The email's `link` is `ITellmaLinks.Target`: the record's details page, else the tenant —
  needed by 0024 (an email opens the record).
- §4 — The Sara example is now an `acme.invoice.assigned` email that links to sales invoice 17 —
  needed by 0024, 0028.
- Context, Goals, Non-goals, §8 — The page description is replaced by "0032 §10 owns the pages" —
  needed by 0032.
- Review flag 1 (tenant-root links) is deleted, and Decision 14 is added.

## ARCHITECTURE.md

**Frontend**

- Frontend — The client is `@tellma/core-ui`, the `@tellma/core` app framework and pack libraries,
  in one Angular app — needed by 0024.
- New "App framework" — The packages, their entry points and the import rule for lazy boundaries —
  needed by 0024.
- App framework — One `provideTellma`, declarations per feature (with a `tmEntity` example) and one
  merge rule — needed by 0024, 0029.
- App framework — Deployment facts ship in the bundle; tenant and user facts come through
  `client/sync` — needed by 0024, 0025.
- App framework — PWA, warm launch, the update banner, one hub per tenant, cancellable reads and
  hosting rules — needed by 0024, 0025.
- App framework — One page-context and action model, and links to 0027–0035 by the pages they own —
  needed by 0026–0035.
- UI component library — It is domain-free. Generic parts are 0023's, charts 0034's, and changes to
  existing parts 0010 Parts B–E — needed by 0023, 0034.
- Accessibility & i18n — Live direction, emitted string packs, and `@tellma/locale-ar` covering
  `@tellma/core` — needed by 0024, 0010 Part B.
- Accessibility & i18n — Dates use `TmL10n` and Core's calendars, never a Locale pack's;
  `tellma-client build` preloads fonts — needed by 0024.
- Theming — The MCP theme tools, which do not exist, are removed (a fact correction).
- Icons — `tm-icon` and `TmIconRegistry` ship with 0023, with no Trusted Types policy — needed by
  0023.
- Forms — `provideTellma` calls `provideTellmaUi()` — needed by 0024.

**Repository and packages**

- Platform repo layout — New `core-testing`, `core-tools` and `module-gl` folders, and a root
  `pnpm-workspace.yaml` — needed by 0024, 0032.
- Distribution repo layout — The duplicate `client/` workspace is removed; the Client project
  matches 0011 §1.2 — needed by 0024.
- Library architecture — New "Client packages": npm naming, peer dependencies and
  declarations-only primary entry points — needed by 0024, 0032.
- Local development, workspace layout, logic vs configuration — `tellma-client` commands, fallback
  ports and `pnpm` — needed by 0024.
- Rollout & Phasing — Phase 1 uses one root pnpm workspace with `tsconfig` paths — needed by 0024,
  0011.
- Code organization — "Angular shell" becomes "Angular app", since "app shell" now names the
  platform's frame — needed by 0027.

**Reports**

- Reports — three tiers — Tier 1 adds report definitions; tier 2 reaches the UI as a coded report;
  tier 3 drops dashboards — needed by 0033–0035.
- Reports — New bullets on definitions, sharing, coded reports and dashboards, and the Part F
  features they use — needed by 0033–0035, 0010.

**Principles and operations**

- Performance — Instant loads hold "while the session lives"; the claim about caching API
  responses is gone — needed by 0024, 0025.
- Performance — No optimistic UI: a save blocks under a busy overlay. Abandoned reads cancel;
  writes never do — needed by 0024, 0031.
- Observability — The client telemetry relay, one trace id across browser and server, and the UI
  version — needed by 0026.
- CI/CD — New client gates on PR, nightly and on deploy, on Ubuntu and Windows — needed by 0024.
- Hosting on Azure — New configuration sections `Reports`, `Dashboards`, `ClientTelemetry`,
  `Support` and `Links` — needed by 0011, 0026, 0033, 0035.
- Identity — The link points to the renamed 0010, Part A, and adds the account URL and
  `ui_locales` — needed by 0010.
- Glossary — Fourteen new rows, from the `@tellma/core` family to Dashboard; the Stack and
  Tellma.Core rows are updated — needed by 0024–0035.
- Open Questions — "UI component-library v1 scope" is marked answered — needed by 0023.

---

Three staged files are not part of these changes: `docs/specs/0002-component-library-foundation.md`
and two token files under `client/projects/core/tellma-core-ui-tokens/`. Someone else staged them
during the session, and they were left untouched. Specs 0001–0009 were not edited.

---

# Review round 1 (26 October 2026)

This part lists every change review round 1 made to specs 0010–0021 and `ARCHITECTURE.md` since
commit 063e313; spec 0022 is unchanged. Where it conflicts with the earlier part, this part wins.
Bullets keep the earlier format, **§ — what changed (reason) — needed by 00NN**. The reason is a
review decision (decision N), a ruling on an editor's question (R-N) or a finding of the final
check (FB-01, FC-05).

## Summary

- **Leaf-grain reports and the C# measure evaluator.** Pivots group at leaf grain in SQL and roll
  up in C#. Part F loses grouping sets and tree level navigations and gains measure partials
  (§F.14) and their evaluator (§F.15). `TreeLevels`, `Level{j}` paths and their refusals are gone,
  `QueryColumn` gains `Precision`, and SQL's divide-by-zero and overflow errors become a typed
  exception. (0010, 0012, 0013, 0015, 0016, 0019, ARCHITECTURE.md; for 0033, 0034.)
- **Part F renumbered; `'uq'` in three forms.** Old §F.10–§F.15 are now §F.8–§F.13, and every
  citation follows. A path-free `'uq'` call becomes a hoisted variable, so QX3104 goes. (0010,
  0012, 0013; for 0033.)
- **Decimals and longs as strings.** `decimal` and `long` travel as JSON strings, full scale and
  never in exponent form; inbound takes a string or a number. Ids follow their entity's key type.
  This reverses 0016 Decision 5. (0013, 0015, 0016, 0017, 0021; for 0024, 0029.)
- **`Long`, `Idempotent` and the Contracts chunk.** Actions declare `Long` beside `Idempotent`.
  `IContractFingerprints` becomes `IRouteContracts` and publishes the long and idempotent routes,
  which drive the server's timeout and the client's watchdog and retries. (0015, 0016, 0019; for
  0024, 0033, 0035.)
- **Forbidden reasons.** A 403 names the refused pair and one of three reasons, `no-grant`,
  `outside-filter` or `restricted-path` with the path, and never roles, filters or other users.
  (0013, 0014, 0015, 0016, 0018, 0019; for 0024.)
- **Regional formats and time zones.** Users and tenants choose a region and a digit style apart
  from the language; the catalogue lists each language's regions; negotiation gains a region step;
  server text always uses Latin digits. `me/locale` takes a time zone for the device-zone prompt,
  and 0010 §D.4 adds offset labels. (0010, 0011, 0013, 0014, 0016, 0018, 0020, 0021,
  ARCHITECTURE.md; for 0024, 0029, 0032.)
- **Launch landing and the session cookie.** After a sign-in to `/`, the BFF lands a
  single-tenant user on their tenant, by the SPA's rule and one shared vector file. The session
  cookie is persistent, so the warm launch survives a browser restart. (0011; for 0024, 0025.)
- **White labelling.** Optional resx keys `Distribution_DisplayName`, `Distribution_ShortName`,
  `Distribution_Copyright` and `Distribution_About` override the configured values per language,
  and server copy says `{displayName}`, never "Tellma". (0011, 0013, 0016; for 0024, 0027.)
- **Credit-control examples.** Acme's aging report becomes `acme-reports/credit-exposure`, with
  the matching securable, preference key, escape-hatch route and MCP tool. (0016, 0018; for
  0033.)
- **Icons, keys, avatars and tokens in 0010 Parts B–E.** Every library glyph draws through
  `tm-icon` (§B.7, §C.13, §D.11, §E.5), the grid matches shortcuts through `kbd` on every keyboard
  layout (§C.12), and new `categorical` and `sequential` token groups (§B.8) serve avatars and
  charts. (0010; for 0023, 0034.)
- **The client toolchain and the service worker.** The `dotnet new tellma-distribution` template
  scaffolds a distribution (a later spec); `tellma-client` drops `init` and `check`; the client
  never hides server entities; the platform stays on Angular's worker, and Web Push will wrap it.
  (0011, 0021, ARCHITECTURE.md; for 0024.)
- **Version order on 412.** The SPA orders `Tellma-Build` against its own version by SemVer
  precedence and asks the service worker only about two builds of one version. (0016,
  ARCHITECTURE.md; for 0024.)

The readability passes also rewrote some sections with the same meaning. Each file section lists
them last.

| File | Gist | Lines + / − |
|---|---|---|
| `0010-amendments-to-implemented-specs.md` | Glyphs, keys and colour groups (§B.7, §B.8, §C.12, §C.13, §D.11, §E.5), zone labels, Part F at leaf grain with §F.14–§F.15 | +954 / −392 |
| `0011-distribution-host-and-multitenancy.md` | Post-sign-in landing, persistent session cookie, regioned languages, template non-goal, brand fallbacks | +74 / −36 |
| `0012-data-access-layer.md` | `QueryColumn.Precision`, `ArithmeticFailureException`, tree levels removed | +38 / −32 |
| `0013-settings-localization-and-cache.md` | Regions and digits in the catalogue, settings and negotiation; brand and `forbidden` keys | +211 / −95 |
| `0014-users-roles-and-permissions.md` | `PreferredRegion` and `PreferredDigits`, the three 403 reasons | +60 / −32 |
| `0015-crud-service-pipeline.md` | `Long` on actions, `ForbiddenReason`, the level refusal removed | +82 / −54 |
| `0016-crud-web-api.md` | Decimals and longs as strings, `IRouteContracts` and the route facts, 403 reasons, region, 412 ordering | +192 / −107 |
| `0017-blob-storage.md` | `size` as a JSON string | +1 / −1 |
| `0018-core-and-gl-reference-stacks.md` | Region and digits on `me/save`, time zone on `me/locale`, credit-exposure report | +53 / −32 |
| `0019-excel-codec.md` | `Long = true` on the Excel operations, the 403 reason, report pages and totals | +27 / −21 |
| `0020-background-jobs-and-scheduler.md` | The job scope reads the region | +7 / −7 |
| `0021-notifications-inbox-and-hub.md` | `TargetId` as a string, the recipient's region, the push wrapper | +7 / −5 |
| `ARCHITECTURE.md` | Regional formats, distribution template, client commands, PWA, leaf-grain pivots, 412 ordering | +10 / −10 |

## 0010 — Amendments to Implemented Specs

**Front matter**

- Context, "Other specs" — The spec 0023 bullet names `tm-icon`, `kbd` and `tm-chip` and what
  each part builds on them (decisions 6, 7) — needed by 0023 §3.
- Context — New bullets: 0023 §4.2 and 0034 §7.7 read §B.8's colour groups (decision 8, R-24);
  0024 §7.8 and 0029 §12.1 choose where §D.4's zone label shows (decision 19). The 0024 bullet
  adds `TM_DEVICE_TIME_ZONE`.
- Context — The Part F host bullet drops `TreeLevels`; the bullet on refusing level paths (0012
  §11.4, 0015 §5.2) is deleted; a new bullet cites 0012 §3.3 and §11.3 for
  `QueryColumn.Precision`; the consumers bullet names `queryex/discover` "with its `measures`
  clause" (decision 1) — needed by 0029 §9.
- Goals — Part B adds glyphs and the categorical and sequential colour groups; Part C a key map
  that works on every keyboard layout, and glyphs; Part D the offset label and glyphs; Part E
  glyphs (decisions 6, 7, 8, 19).
- Goals, Part F — Grouping sets and level navigations give way to "measure partials with their C#
  evaluation" (decision 1).
- Non-goals — Two new bullets: matching named keys (arrows, Enter, Escape, Tab) through `kbd`
  (decision 7); non-decomposable aggregates such as distinct count, median and percentile, which
  §F.14 refuses until they are designed (decision 1).

**Parts B–E: rules shared**

- Order — Spec 0023 §3.1–§3.2 (`icon`, `kbd`) come first, then Part B, then Part D. Part C needs
  only 0023 §3, and Part E also needs 0023 §4 (decisions 6, 7).
- New rule "Glyphs draw through `tm-icon`" — Every built-in glyph follows 0023 §3.1's rule and
  its two exceptions (the grid's masks stay). Each glyph keeps its box and stroke, pinned by a
  geometry e2e per part in LTR and RTL (decision 6) — needed by 0023 §3.1.
- New rule "Keys through `kbd`" — Shortcuts match with `tmKeysMatch`, hints use `tmFormatKeys` or
  `tmKeysText`, the platform comes from `tmIsApplePlatform`, and named keys keep direct checks.
  The ESLint rule `tm/no-platform-sniffing` refuses `navigator.platform` and
  `navigator.userAgentData` outside `kbd/`; Part C ships it (decision 7, R-4) — needed by 0023
  §3.2.

**Part B — foundation**

- Intro — Part B also changes the foundation's glyphs and the token contract; its consumers add
  the avatars and charts.
- §B.4 — A multi-select row's checkbox draws its `check` through `tm-icon`, not the grid's mask
  (decision 6, R-2).
- New §B.7 — Foundation glyphs through `tm-icon`: `checkbox` (`check`, `minus`), `select` (the
  caret, the option check, the multi-select row's check) and `form-field` (`circle-alert`). Sizes
  hold through `--icon-size`, and the grid's masks draw the same paths (decision 6) — needed by
  0023 §3.1.
- New §B.8 — The `categorical` and `sequential` colour groups in `TmSchemeColors`: eight
  `TmCategoricalTone { mark, tint, onTint }` slots with light and dark values, and a seven-step
  ramp over the teal primitives. Both emit in each scheme block, the slot order is fixed, and a
  unit test holds `onTint` on `tint` at 4.5:1 (decision 8, R-24) — needed by 0023 §4.2 (avatar
  tones), 0034 §7.7 (chart series and ramp).
- Testing, Definition of done — The contrast test, the emitted names, §B.7's geometry and
  `tokens:check` over both groups; new Glyphs and Tokens items; `./checkbox` and `./form-field`
  re-measured.
- Decisions 10–12 (every glyph through `tm-icon`; categorical colours as a scheme group; the
  sequential ramp as a scheme group) and Review flag 7 (eight slots) are added.

**Part C — grid and menu**

- Intro — Part C also moves the grid's keys onto `kbd` and its glyphs onto `tm-icon`.
- §C.3 — The sort arrows are a decorative `tm-icon`, not inline SVG (decision 6).
- §C.10 — `TmMenuIcon` adds `TmIconSource`, rendered with `[svg]` for the grid's context menu.
  The paste hint "Press {shortcut} to paste" fills from `tmKeysText('Mod+V')` (decisions 6, 7) —
  needed by 0023 §3.
- §C.12 — Retitled "The key map, published and matched through `kbd`". One binding table drives
  the matcher and `TM_GRID_KEYS`. `Mod+A`, `Mod+Z`, `Mod+Y`, `Mod+D`, `Mod+F` and the row keys
  match through `tmKeysMatch`, with `Mod+Alt+=` kept as a second binding; the pointer's Mod and
  the find bar go through `kbd`; an Arabic-layout example and tests (decision 7, R-3) — needed by
  0023 §3.2 (shortcuts that work on an Arabic layout).
- New §C.13 — Grid and menu glyphs through `tm-icon`: a table of the context-menu, find-bar,
  status-bar, tree-expander, sort and menu-row glyphs and their RTL rule. `ɵTmGridIcons` goes, the
  expander swaps chevrons instead of rotating, and the per-cell check and dash masks stay
  (decision 6) — needed by 0023 §3.1.
- Testing, Definition of done — Key matching with an Arabic `key` and a Latin `code`, an Arabic
  e2e for Mod+Z, D, A and F, glyph geometry, the scroll probe on an expanded tree and the lint
  vectors; new Keys and Glyphs items.
- Decisions 15–17 (shortcuts through `kbd`, named keys direct; one binding table; the masks stay)
  and Review flag 8 (one `tm-icon` per visible tree row and option row) are added.

**Part D — basics**

- Intro — Part D also changes the components' glyphs.
- §D.4 — Retitled "Date-time, time of day, relative time and zone labels". New `zoneLabel`
  options, `tmFormatZoneOffset`, `tmFormatZoneName`, `TM_DEVICE_TIME_ZONE` and `TmZoneLabelMode`
  (`auto`, `column`, `none`); `TmL10n` gains `deviceTimeZone`, `zoneMismatch` and `zoneLabel()`.
  Offsets are compared at the instant shown, never abbreviated, and never added to relative times
  or dates. A `zoneLabel` golden array is added (decision 19) — needed by 0024 §7.8, 0029 §12.1.
- New §D.11 — Basics glyphs through `tm-icon` for `alert`, `date-picker`, `file-preview`,
  `files`, `image`, `modal` and `private`. The date popup's chevrons use `mirrorInRtl`, and the
  image edit pane's check keeps its 2.25 stroke (decision 6) — needed by 0023 §3.1.
- Testing, Definition of done — The label modes, `zoneMismatch`, the label golden rows and
  §D.11's geometry; the `./l10n` ceiling goes from 9 to 10 KB.
- Decision 12 (zone labels are GMT offsets, compared at the instant shown) is added.

**Part E — entity picker**

- Intro, new §E.5 — The picker's `circle-alert`, `search` and `check` draw through `tm-icon`;
  Testing and Definition of done add their geometry e2e (decision 6) — needed by 0023 §3.1.

**Part F — Queryex**

- Intro — The eight report items are optional parameters, defaults, drill-through, Hijri
  grouping, sharing, parameter pickers, and pivot and summary rollups (decision 1).
- §F.8, §F.9 — "Grouping sets and the level indicator" and "Tree level navigations" are deleted,
  with `KeyGroups`, `GroupingSets`, `GROUPING_ID`, QX5008, `TreeLevels` and `Level{j}` (decision
  1) — needed by 0033 §8.6 (rollups in C#).
- Numbering — Old §F.10–§F.15 are now §F.8–§F.13, and every citation in the file follows
  (decision 5).
- §F.0 — The vocabulary swaps key group, grouping set, level indicator and level navigation for
  partial, combine and rank column. Compatibility drops `Level2` and adds the `Measures` clause;
  L3 keys on the `Measures` text (decision 1).
- §F.0, diagnostic codes — QX3104 and QX5008 go. QX4010 ("cannot be computed over rolled-up
  values") and QX4011 ("a measure must aggregate") are added, QX6001 and QX6002 are evaluation
  codes rather than diagnostics, and `Measures[<i>]` is a new location (decisions 1, 2).
- §F.0, interpreter and fixture — The reference interpreter evaluates measures directly over a
  group's rows. `LedgerFixture` drops `TreeLevels(4)` and gains numeric columns near their
  bounds, tying text rows and `hierarchyid` paths that sort differently as text (decision 1).
- §F.2, §F.7, §F.12 — 0033 §8.6 no longer requests `level()`; `Via` and `Exists` bind foreign-key
  navigations only (decision 1).
- §F.9 — A `'uq'` call in a constant statement takes §F.11's scalar form (decision 2).
- §F.11 — Three emission forms, chosen by all the call's operands: row grain takes the lateral
  seek, path-free a hoisted prologue variable (0008 §12.4), aggregated the scalar form in place.
  No position refuses a `'uq'` call, so QX3104 goes. A hoisted example is added, and the hoisted
  goldens also run on SQL Server (decision 2, R-5) — needed by 0033 §8.7.
- New §F.14 — `QuerySpec.Measures` and partial columns: `QueryDiscoverySpec.Measures`,
  `CompiledQuery.Measures` and `MeasurePartial`. `avg` splits into `sum` and `count`, the
  aggregate list is closed, and text and hierarchy `min`/`max` get a `DENSE_RANK` column. `Guid`
  is not Ordered, so it has no `min`/`max`. The example's cap is `MaxPivotRows + 1` (decision 1,
  R-6) — needed by 0033 §8.4, §8.5, 0029 §9.
- New §F.15 — Measure evaluation in C#: `CompiledMeasures`, `MeasureContext`, `MeasureEvaluator`,
  `PartialTable`, `MeasureValue` and `QueryexEvaluationException`. It defines the supported subset
  (QX4010 and QX4011 outside it), `SqlDecimal` at each node's SQL type with SQL Server's scale and
  rounding, nulls and laziness, and errors that fail the run, which 0033 §8.11 answers as
  `422 Report.MeasureFailed`. A three-identity differential suite pins it (decisions 1, 3, 4) —
  needed by 0033 §8.4, §8.6, §8.11.
- Testing — The grouping-set and level cases go. Hoisted `'uq'` goldens, partial and rank
  columns, evaluator vectors, the measure identities and rank exactness under seven collations
  are added, plus a one-off `'uq'` join-shape benchmark before §F.11 is built (decisions 1, 2).
- Definition of done — The inspection tool shows measures with their partial and rank columns; a
  new Benchmark item; Docs name the measure evaluator; 0012's `QueryColumn.Precision` and 0033's
  cube builder are out of scope (decisions 1, 2).
- Decisions — Old 6–9 (level navigations, counted levels, grouping sets, finest-set `Having`) are
  deleted and 10–14 become 6–10. Decision 9 (`'uq'`) states three forms with the measured cost.
  New 11–13: measures decompose into partials evaluated in C#; `SqlDecimal` at each node's type;
  text and hierarchy partials combine by an SQL rank (decisions 1, 2, 3).
- Review flags — Old 5 and 6 are deleted. Flag 4 becomes path-free `'uq'` calls as hoisted
  variables, flipping if the benchmark finds them more than 1.5× slower than a bound parameter.
  Old 7 keeps only the name `EvaluateConstant` and becomes 5, and old 8 becomes 6. New 7–9: the
  evaluator's subset; exact text `min`/`max` by rank rather than decision 1's approximate
  comparer; evaluation errors fail the run (decisions 1, 2, 3).
- Rewritten for readability, same meaning: Goals (Parts C and D), Context (the spec 0023 bullet),
  "Order", §F.0 (Compatibility, the QX2009 bullet).

## 0011 — Distribution host and multi-tenancy

**Launch landing**

- §3.2, §3.8 — The membership list has three readers: `/bff/user`, the BFF's post-sign-in landing
  and the admin members route (decision 16) — needed by 0024 §9.3.
- §5.5 — `/bff/login` redirects to `returnUrl ?? "/"`, or to the user's one live tenant when that
  target is `/` (decision 16) — needed by 0024 §9.3.
- §5.5 — New "The post-sign-in landing": in `OnTicketReceived`, only for a target of exactly `/`
  or none, one `ListAsync(sub)` and then `TenantLanding.SingleLiveTenant` (new, in
  `Tellma.Core.Abstractions.Tenancy`). A directory error keeps `/` and logs `BffLandingFailed`.
  The SPA's `tmSingleLiveTenant` applies the same rule, and both test one
  `launch-landing-vectors.json` (decision 16) — needed by 0024 §9.3 (a single-tenant user sees no
  tenantless skeleton and no shell swap).
- §9, §10 — A `BffLandingFailed` Warning row; tests of the rule over every vector row and of the
  landing cases (decision 16).
- Decision 29 — Rewritten: the BFF picks a tenant only for a sign-in to `/`, by the SPA's rule
  (decision 16).

**Session cookie**

- §5.5 — The session cookie is persistent: `/bff/login` challenges with `IsPersistent = true`,
  the cookie's `Expires` is the session row's `ExpiresAt`, and every renewal re-issues it. The
  profile cookie takes the session cookie's `Expires` (FB-01) — needed by 0024 §8.1, 0025 (the
  7-day warm launch survives a browser restart).
- §10 — The Sessions tests add the persistent cookie and its `Expires` at sign-in and after a
  renewal (FB-01).

**Regional formats**

- Context — A new "Other specs" bullet: 0013 §7.3 and §9.2 own the regioned codes `Languages`
  takes and the culture's region (decision 24).
- §1.3 — The illustration declares `Languages(["en-GB", "ar-SA"])`: a code may carry the region
  its users get by default (decision 24) — needed by 0024 §7.7.
- §2.2 — `AddLanguage(LanguageInfo info, LanguageFormats formats)`; the member table names the
  default region and the added languages' formats (decision 24, R-8).
- §4.1 — `Culture` is language and region (`ar-SA`), and `CultureInfo` is its Gregorian clone
  with `-u-nu-latn`, so server text always uses Latin digits; the region follows 0013 §9.2
  (decision 24).

**Distribution template and white labelling**

- Non-goals, §1.2 — New non-goal "The distribution template": `dotnet new tellma-distribution`
  scaffolds the server and client projects together and belongs to a later distribution-tooling
  spec. It, not `tellma-client init`, scaffolds the Client project (decision 11) — needed by 0024
  §2.1.
- §5.9 — `DistributionInfo.DisplayName` holds one value, because the document is anonymous and
  has no language (decision 25).
- §8 — `DisplayName` and `PublicOrigin` get rows of their own. `DisplayName` and `Copyright` are
  the fallbacks for the optional resx keys `Distribution_DisplayName` and `Distribution_Copyright`
  (decision 25) — needed by 0024 §3.1, 0027 §11.1.
- Rewritten for readability, same meaning: §5.5 (the cookie scheme), §10 (Sessions).

## 0012 — Data access layer

- Goals, §11.1, §11.2, §11.4 — `TreeLevels`, the `Level1` … `LevelN` navigations, the
  fingerprint's tree levels and the entity `Select` refusal of a level path are deleted (decision
  1) — needed by 0033 §8.6 (trees roll up in C#).
- §3.3, §11.3 — `QueryColumn` gains `int? Precision`, a `decimal` result's precision, read from
  `DbColumn.NumericPrecision`. 0010 §F.15's evaluator reads it with `Scale`, and 0016's converter
  never writes it (decision 1) — needed by 0010 §F.15, 0033 §8.6.
- §6.4 — SQL errors 8134 (divide by zero) and 8115 (overflow) become the new internal
  `ArithmeticFailureException(Reason)`, final, with reason `divide-by-zero` or `overflow` and a
  `StatementOrdinal`. The report engine answers it as `422 Report.MeasureFailed`; every other
  caller still gets a 500 (decision 1) — needed by 0033 §8.11.
- §5.2, §11.1, §11.2, §11.3 — Part F citations follow the new numbers: constant statements §F.9,
  `'uq'` §F.11 (decision 5).
- §13.1, §13.3, §14 — The tests drop the level refusal and add the 8134 and 8115 mapping, a
  division by zero and an overflow in a `Rows` statement, and `Precision` per `decimal` column;
  the DoD drops tree levels (decision 1).
- Decision 36 (SQL's divide-by-zero and overflow errors are a typed exception with a reason) is
  added (decision 1).
- Rewritten for readability, same meaning: §11.1 (`Fingerprint`), §11.3 (the reader), §14.

## 0013 — Settings, localization and cache

**Regional formats**

- Goals, Non-goals — The catalogue carries each language's regions and native digits, and the
  tenant gets a default region and digits, so "a per-tenant default culture" leaves the deferred
  list (decision 24) — needed by 0024 §7.7.
- §1.1, §7.1 — New `DigitStyle` (`Latin`, `Native`) and `LanguageFormats` (language, default
  region, offered regions, native digits). `ILanguageCatalog` gains `Formats(language)` and
  `IsRegionOffered(region)`, and `IsOffered` takes a bare code (decision 24) — needed by 0024
  §7.7.
- §1.2 — Each `languages` entry carries the default region, the offered regions and the native
  digits, with Acme's Arabic entry as an example. The builder surface is `Languages(codes)` and
  `AddLanguage(info, formats)` (decision 24, R-8) — needed by 0024 §3.1, §7.7, 0032 §12.1.
- §1.3, §7.3 — A declared code may carry its language's default region
  (`Languages(["en-GB", "ar-SA"])`), which must be one of the language's regions. Only the
  language is offered and stored. `AddLanguage(info, formats)` gives an added language its
  formats, so `Formats` covers every offered language (decision 24, R-8).
- §5.1, §5.2, §5.6, §5.7 — `core.Settings` gains `Region varchar(2)` and `Digits varchar(8)`,
  read by the cold load and carried by `TenantSettings` and `TenantSettingsForClient` (decision
  24, R-7) — needed by 0024 §7.7, 0032 §12.1.
- §6.1, §6.3, §6.5 — `TenantSettingsPatch` gains `Region` and `Digits`. The save trims and
  upper-cases the region and checks it with `IsRegionOffered`; new code
  `Settings.RegionNotOffered` (decision 24) — needed by 0032 §12.1.
- §7.2 — Each entry's formats are fixed in code: every .NET 10 `<language>-R` pair (243), the
  `zh` aliases, native digits for `ar`, `ur` and `hi`, and a default region per language.
  Chromium lacks the `om`, `ti` and `so` pairs, and `en-SA` exists nowhere (decision 24) — needed
  by 0024 §7.7.
- §9.2 — The Culture row becomes Language, and a new Region row resolves header →
  `PreferredRegion` → tenant `Region` → the language's default, skipping a region not offered with
  the language. The culture is `<language>-<region>`, cloned with `-u-nu-latn` so server text uses
  Latin digits; the SPA's `-u-nu-*` extension is ignored. A regioned `PreferredLanguage` no longer
  picks formats. Acme example (decision 24) — needed by 0024 §7.7.
- §9.3 — The initializer passes `PreferredRegion` and the tenant's `Region`: four preferences
  (decision 24).
- §11 — The regions, like the language list and the calendars, are code (decision 24).
- §12, §13 — Catalogue invariants on Ubuntu and Windows, the Latin-digit clone, the region steps,
  each language's formats in the client metadata and the `Region`/`Digits` round trip. The DoD's
  Docs item names regional formats as Core's (decision 24, R-8).
- Decision 30 (region is its own axis) and Review flag 24 (offered regions are every .NET pair)
  are added. Flag 7 becomes "Server text always uses Latin digits" (decision 24).

**Other decisions**

- §5.3 — A `long` or `decimal` setting value uses 0016 §3.2's string encoding, and a number is
  accepted on input (decision 18) — needed by 0024 §10.4.
- §6.1 — `details` with no grant raises `ForbiddenException` with `NoGrant`, naming
  `core.Settings.General` × `Read` (decision 21).
- §10.1 — New "The distribution's brand": `Distribution_DisplayName` and `Distribution_Copyright`
  override 0011 §8 per language, `Distribution_ShortName` is the installed app's short name, and
  `Distribution_About` is the About dialog's description. The resource audit accepts the four keys
  (decision 25) — needed by 0024 §3.1, §5.1, 0027 §11.1.
- §10.1 — New "The `forbidden` problem": `Problem_forbidden_Detail` is an ICU `select` on
  `reason`, worded as the SPA's messages, with the permission's labels and an example (decision
  21) — needed by 0024 §11.
- §10.1 — `Entity_Level` is deleted (decision 1).
- Non-goals, §8.2 — The `'uq'` citations move from §F.13 to §F.11 (decision 5).
- Rewritten for readability, same meaning: §1.2 (the builder surface), §5.1, §7.3, §9.2, §9.3
  (numbered steps), §12 (Integration, Settings).

## 0014 — Users, roles and permissions

**Regional formats**

- §1.3 — A new dependency row: `ILanguageCatalog` and `DigitStyle` (0013 §7.1) (decision 24).
- §3.1 — `User` gains `PreferredRegion varchar(2)` and `PreferredDigits varchar(8)`, and
  `PreferredLanguage` holds a bare code. The region is checked by `IsRegionOffered` after trimming
  and upper-casing, the digits by their enum (decision 24, R-7) — needed by 0024 §7.7, 0032 §12.2.
- §3.8, §7.1, §7.3, §7.8 — `PreferredDigits` is a string-typed enum; `UserProfile` and the
  prologue's profile result set carry both members; negotiation reads `PreferredRegion` (decision
  24).
- Review flag 17 covers the typed region and digits columns (decision 24).

**Forbidden reasons**

- §5.3 — A new table of the three 403 reasons: `NoGrant` (`no-grant`), `OutsideFilter`
  (`outside-filter`) and `RestrictedPath` (`restricted-path`) with `Path`. The error never names
  roles, filters or other users, and each refusal below names its reason (decision 21) — needed
  by 0024 §11.
- §5.1, §14, Appendix A — `RequireAsync` on `Denied` raises `NoGrant` and the `RowSecurity`
  post-check `OutsideFilter`; a unit test pins the first (decision 21).
- Decision 29 (a 403 names the refused pair and one of three reasons) is added (decision 21).
- Rewritten for readability, same meaning: §3.1 (Preferences), §3.8, §5.3, §7.8.

## 0015 — CRUD service pipeline

**`Long` and `Idempotent`**

- §2.2 — `ActionDescriptor`, `EntityActionDescriptor` and `ApiActionDescriptor` gain `bool Long`
  after `Idempotent`, taken from the attribute (decision 20) — needed by 0024 §3.4.
- §2.3 — `[EntityAction]` and `[ApiAction]` gain `Long = false`. `Long` marks an operation that
  runs under 0016's long timeout, and the client learns `Idempotent` and `Long` from the Contracts
  chunk (decision 20) — needed by 0024 §3.4.
- §18.1 — The descriptor tests cover each action's `Long` and `Idempotent`; Decision 35 (`Long`
  and `Idempotent` declared once on the action) is added (decision 20).

**Forbidden reasons**

- §14.1 — `ForbiddenException` gains `ForbiddenReason Reason` and `string? Path`, with
  `enum ForbiddenReason { NoGrant, OutsideFilter, RestrictedPath }` sent in kebab case. A new
  paragraph names the raise site of each reason and the `Arguments` (decision 21) — needed by
  0024 §11.
- §5.2 step 4 — The traversal refusal is `RestrictedPath` with the path (decision 21).
- §6.6, §9.1–§9.3, §10.1, §14.2 — The save pre-check, the delete and action pre-check and the
  post-check raise `OutsideFilter`; "no grant" raises `NoGrant` (decision 21).
- §18.1, §18.2 — The tests pin every raise site's reason, `OutsideFilter` on the row-security
  403s and `NoGrant` without a grant; Decision 36 (a 403 carries its reason, set at the raise
  site) is added (decision 21).

**Other decisions**

- §5.2 step 4, §7.3 — The `Level{j}` select refusal and `ValidationCodes.QueryLevelNotSelectable`
  are deleted; only a `Geography` select item is refused (decision 1).
- §14.1 — `ConcurrencyConflict.ModifiedById` is `int?` and `LimitExceededException` takes `int`
  counts, so both stay JSON numbers (decision 18) — consistency with 0016 §7.4 and 0031 §9.
- Rewritten for readability, same meaning: §2.2 and §2.3 (the sketches, the projection-metadata
  row), §5.2 step 4 (select-item types), §14.1.

## 0016 — CRUD web API

**`Long`, `Idempotent` and the Contracts chunk**

- §2.2 — The retry contract is published per route in the Contracts chunk. The retried class is
  every action declared `Idempotent = true`, which moves `activate` and `deactivate` out of "never
  retried" (decision 20, R-10) — needed by 0024 §3.4, §10.5.
- §2.4 — `reports/audience`, `run` and `export` are long, and `dashboards/audience` is idempotent
  and long (decision 20) — needed by 0033 §2.2, 0035 §3.2.
- §1.1, §3.10, §14 — `IContractFingerprints` is renamed `IRouteContracts` and gains the sets
  `Long` and `Idempotent` (decision 20, R-12) — needed by 0024 §3.4.
- §3.10 — New "The route facts": a route is long when its action declares `Long = true`, and
  idempotent when it is a standard read or its action declares `Idempotent = true`.
  `contracts.json` lists both; the contributor writes the chunk's `fingerprints`, `long` and
  `idempotent` sections, tested against the endpoints; a JSON example (decision 20) — needed by
  0024 §3.4.
- §4.1 — The restated descriptors gain `Long`, and `EntityActionDescriptor` gains the
  `SingleTarget` it lacked against 0015 §2.2 (decision 20).
- §4.3, §8.3 — `tellma-long` covers every action declared `Long = true`: 0019's four Excel
  operations, 0033's report routes and 0035's `dashboards/audience`, plus the blob upload
  (decision 20) — needed by 0033, 0035.
- §4.5 — A hand-mapped endpoint is in neither Contracts list, takes `tellma-long` itself, and its
  caller passes `long` (decision 20, R-28) — needed by 0024 §3.4.
- Decision 3 adds that the server publishes each route's retry class and long flag (decision 20).

**`decimal` and `long` as strings**

- Context, Goals, Non-goals, §14 — "Lossless numbers" and "the SPA's lossless parser" become
  `decimal` and `long` as JSON strings and the SPA's JSON handling (decision 18) — needed by 0024
  §10.4.
- §3.1 — `NumberHandling = Strict` refuses a quoted `int` or `double`; `decimal` and `long` have
  converters of their own (decision 18).
- §3.2 — `long` and `decimal` are JSON strings, every digit and full scale with no exponent, and
  inbound takes a string or a number; a string that is not a plain decimal is 400. The paragraph
  "Lossless numbers are a client obligation" is replaced, with an `acme.Customer` example
  (decision 18) — needed by 0024 §10.4, 0029.
- §3.2 — A new Schemas bullet: OpenAPI, MCP `inputSchema` and the client metadata's argument
  schemas describe a `decimal` as a string or number with `format: decimal`, and a `long` as a
  string or integer with `format: int64` (decision 18) — needed by 0029 §2.2.
- §3.2 — Wire ids travel in their entity's key type: `EntitiesResult<T>.Ids` and
  `ConcurrencyConflict.Id` follow the stack's `KeyType`, a number for `int` and a string for
  `long` (decision 18, R-11) — needed by 0024 §10.1, 0029.
- §3.5, §3.7 — `long[]` and `decimal[]` cells are strings, and the `QueryColumn` sketch gains
  `Precision`, which the converter never writes (decisions 18, 1).
- §13.3 Wire — Decimals and longs round-trip as strings and read back from either form; a quoted
  `int`, `NaN` and an exponent-form decimal string are 400; `int` ids are numbers and `long` ids
  strings; the schemas are asserted (decision 18).
- Decision 5 — Rewritten: `decimal` and `long` travel as JSON strings, as RFC 7493 recommends;
  JSON numbers with a lossless parser were the alternative. Review flag 2 is restated the other
  way round and flips if generic tools need numeric columns (decision 18).

**Forbidden reasons**

- §7.1 — The `forbidden` problem carries `arguments.reason` and, for `restricted-path`,
  `arguments.path` (decision 21) — needed by 0024 §11.
- §7.2 — A `forbidden` problem's `detail` renders `resource` and `action` as the permission's
  labels, while `arguments` keep the raw pair (decision 21) — needed by 0024 §11, 0032 §12.4.
- §3.8, §5.6 — A readable id outside the grant is 403 `outside-filter`; a non-projected column is
  403 naming `E` and `Read`, with `restricted-path` and the path (decision 21).
- §13.3 Security — `no-grant`, `outside-filter` and `restricted-path` assertions, and no role,
  filter or user in the body (decision 21).

**Regional formats**

- §3.4 — `UserProfileView` gains `PreferredRegion` and `PreferredDigits` (decision 24) — needed
  by 0032 §12.2.
- §6.1 — `Accept-Language` carries the SPA's locale (`ar-SA-u-nu-latn`): language and region pick
  the culture, and `nu` is ignored. Precedence adds a region chain, and the language falls back to
  the distribution default instead of `en` (decision 24, FC-05) — needed by 0024 §7.7.
- §13.3 Context — `ar-SA-u-nu-latn` negotiates `ar-SA`, and the region fallback is asserted
  (decision 24).

**Other decisions**

- §3.10 — On a 412 the SPA orders `Tellma-Build` against its own version by SemVer precedence,
  ignoring `+<sha>`: a newer server means the client is outdated, an older one that the server
  is, and the same version with another sha asks the service worker (decision 22) — needed by
  0024 §5.6.
- §11.1, §11.6 — The MCP `title` is `"<Tenant name> (<display name>)"`, and the sensitive-operation
  refusal says "this operation requires the {displayName} web app" in the message language
  (decision 25) — needed by 0024 §3.1.
- §4.5, §11.9 — The examples become `reports/credit-exposure/stream` and `CreditExposureTools`
  (decision 23) — needed by 0033 §11.
- Rewritten for readability, same meaning: §3.8 (the delete refusals as bullets), §8.3, §11.1,
  §13.3 (Wire, Security, Context), Decisions 3 and 5.

## 0017 — Blob storage

- §3.1 — A `BlobDescriptor`'s `size` is a `long`, so it travels as a JSON string (decision 18) —
  needed by 0024 §10.4.

## 0018 — Core and GL reference stacks

**Regional formats and the time zone**

- §1.2, §1.3 — The composition declares `Languages(["en-GB", "ar-SA"])`, and the spec 0013 row
  adds `ILanguageCatalog` (`IsOffered`, `IsRegionOffered`) and `DigitStyle` (decision 24).
- §3.1, §3.5 — `MeSaveRequest` gains `PreferredRegion` and `PreferredDigits`, so `SaveMe` copies
  thirteen members (decision 24) — needed by 0032 §12.2.
- §3.2 — `PreferredLanguage` is trimmed and lower-cased as a bare code, so `ar-SA` fails the
  language rule, and `PreferredRegion` is trimmed and upper-cased. New rules:
  `Users.RegionNotOffered`, and the digits' enum binding (decision 24) — needed by 0032 §12.2.
- §3.1, §3.2, §3.5 — `MeLocaleRequest` gains `TimeZone` and `MeLocaleResult` gains
  `PreferredTimeZone`. `me/locale` checks the zone at `timeZone`, and the device time-zone prompt
  and its automatic update call it (decision 19) — needed by 0024 §7.8.
- §9 — The `me/locale` tests add a time zone, all three members, a regioned language and an
  unknown zone; a new Regional formats bullet; the admin `save` of one's own row is 403
  `no-grant` (decisions 19, 21, 24).
- Decision 14 — `me/locale` also serves the time-zone prompt (decision 19).

**Credit control**

- §1.4 — The coded report is `acme-reports/credit-exposure` under `acme.CreditExposureReport` ×
  `Read`: per customer, the credit limit, the open invoices posted on or before `AsOf`, the
  headroom and an over-limit flag (decision 23) — needed by 0033 §11.
- §3.5, §9 — The preference-key example is `report.acme-credit-exposure.params` (decision 23).
- Rewritten for readability, same meaning: §3.1 (the sketch's comments), §3.2 (the `me/locale`
  row), §3.5 (`SetMyLocale`).

## 0019 — Excel codec

- §1.2, §2.1, §2.2 — `export`, `export-for-import`, `inspect-import` and `import` declare
  `Long = true`, so one flag drives `tellma-long` and the Contracts chunk; §1.2 cites the §2.1
  sketch for each method's attributes (decision 20) — needed by 0024 §3.4.
- §12.3 step 2 — A denied export fails with a `forbidden` job error whose arguments add `reason`,
  and `path` for `restricted-path` (decision 21).
- §12.3 step 3 — A report list or summary streams through `IReportEngine.PagesAsync` in pages of
  `MaxTake` rows, and a summary with totals writes its Total row last, from the last page
  (decision 4, R-22) — needed by 0033 §12.1, §12.2.
- Rewritten for readability, same meaning: §1.2 (the stacks' methods as bullets), §2.2 (timeouts
  and limits as bullets).

## 0020 — Background jobs and scheduler

- §8 — The job scope's negotiation also reads the run-as user's `PreferredRegion` (decision 24) —
  consistency with 0013 §9.3.

## 0021 — Notifications, inbox and hub

- §2.2 — `Notification.TargetId` is a JSON string on the wire (decision 18) — needed by 0028 (a
  notification's target).
- §4.5 — The delivery handler's user `SELECT` adds `PreferredRegion`, so a notification email
  formats in the recipient's region (decision 24).
- Review flag 3 — The push spec also owns the service-worker wrapper script that imports
  `ngsw-worker.js` and adds the push handlers (decision 14) — needed by 0024 §5.4.

## ARCHITECTURE.md

- The five dimensions, Per-dimension contents (Locale) — `ArAe` leaves the examples. Regional
  formats (region and digits, from CLDR) are Core's on client and server, never a Locale pack's
  (decision 24) — needed by 0024 §7.7, 0013 §13.
- Accessibility & i18n — The locale joins the UI language with a region and a digit style, each
  the user's or else the tenant's (`ar-SA-u-nu-latn`), and the server formats with the same region
  (decision 24) — needed by 0024 §7.7.
- Distribution repo layout, Tenant & Distribution Onboarding — The
  `dotnet new tellma-distribution` template scaffolds the Client project with the server
  projects, not `tellma-client init`; a later distribution-tooling spec owns it (decision 11) —
  needed by 0024 §2.1.
- App framework — `@tellma/core-tools` lists the commands `emit`, `serve` and `build`, whose first
  and last steps check the sources and the output; `init` and `check` are gone (decisions 11, 12)
  — needed by 0024 §2.6.
- App framework, One merge rule — The server decides which entities, widget types and coded
  reports exist; the client follows the metadata and never hides them (decision 13) — needed by
  0024 §2.3.
- App framework, PWA — Angular's worker receives security fixes only. The platform stays on it,
  with a fork into `@tellma/core-tools` as the fallback, and Web Push will wrap it in a script
  that imports `ngsw-worker.js` (decision 14) — needed by 0024 §5.4.
- Reports — three tiers — A pivot groups at leaf grain in SQL, and the cube builder rolls up
  trees, subtotals and totals in C# with the engine's measure evaluator, replacing grouping sets
  and level navigations (decision 1) — needed by 0033 §8.6, 0010 Part F.
- Observability — On `412` the client orders the two versions by SemVer precedence, ignoring the
  commit; two builds of one version ask the service worker (decision 22) — needed by 0024 §5.6.

---

# Review round 1, follow-up (27 October 2026)

Two more decisions from the review of round 1. Where this part conflicts with the parts above, this
part wins.

## Summary

- **Tenant collation.** Every tenant database is created with `Latin1_General_100_CI_AS`, because
  the server default `SQL_Latin1_General_CP1_CI_AS` defines no order on Amharic text. (0011, 0012,
  ARCHITECTURE.md.)
- **No SQL rank for text `min`/`max`.** Rolled-up text `min` and `max` compare with a
  case-insensitive .NET comparer, and hierarchy paths by their integer levels. The `DENSE_RANK`
  columns and their tests go. (0010; for 0033.)

## 0010 — Amendments to implemented specs

- §F.0 — The glossary drops "rank column".
- §F.14 — `MeasurePartial` loses `RankOrdinal`; the "Rank columns" bullet goes; the rank golden and
  the rank-exactness suite over seven collations go.
- §F.15 — `PartialTable.Add` loses its `ranks` argument and storage keeps no ranks. `min` and `max`
  compare text with `StringComparer.InvariantCultureIgnoreCase` (ties keep the ordinally smaller
  value) and hierarchy paths level by level as integers. The QX4010 reason for text comparisons
  says the comparer only approximates the collation. The differential identities include text
  `min` and `max` only over plain text, matched ignoring case.
- Testing, Definition of done — Partial columns only, no rank columns or rank exactness.
- Decisions — 13 becomes the comparer and the path order, with the SQL rank as the alternative.
- Review flags — Old 8 (exact text by rank) is deleted, and old 9 becomes 8.

## 0011 — Distribution host and multi-tenancy

- §6.1 — `provision` creates the database with `COLLATE Latin1_General_100_CI_AS` and fails when
  the database already exists with another collation.
- Decisions — New 32: the tenant collation, set at creation and never altered.

## 0012 — Data access layer

- §13.2 — The fixture database is created with the tenant collation, as provisioning does.

## ARCHITECTURE.md

- Migrations & seeding — Provisioning creates the database with `Latin1_General_100_CI_AS`.
