# Applied edits — spec 0016 (`docs/specs/0016-crud-web-api.md`)

One line per item: ID — §target — what changed — needed by.

- G32 — Goals, §11.9, §14 — the spec 0010 citations name Part A (and §A.1–§A.4, §A.6 in §11.9) — needed by spec 0010
- X-0016-18 (scope) — Non-goals, §3.2 — the SPA's lossless parser and outdated-client handling are spec 0024's (§10.4, §5.6) — needed by spec 0024
- X-0016-18 — §1.1 — `Tellma.Core` holds `IContractFingerprints`, so the migrator's emit and the Web host compute one map — needed by spec 0024
- X-0016-26 — §1.3 — `TellmaApiOptions.TelemetryBatchesPerMinutePerUser` (60) — needed by spec 0026
- X-0016-27 / G16 — §1.3 — `ConcurrentReportsPerUser` (8) and `ReportQueuePerUser` (16) — needed by specs 0033, 0035
- X-0016-1 / G13 — §2.1 — removed the `/api/strings/{language}` and `/api/notification-catalogue` rows; the fallback sentence cites spec 0024 §6 and `TellmaRoutes.ServerFirstSegments` / `ServerTenantSegments` — needed by spec 0024
- X-0016-2 — §2.1 — rows for the tenantless `POST /api/client-telemetry` and the `support-requests` stack; the upload row names the `support-bundle` kind — needed by spec 0026
- X-0016-3 — §2.1 — row for `/{tenantId}/api/web/{child-segment}/query` (child query surfaces) — needed by spec 0033
- X-0016-4 — §2.2 — the retry contract became a two-row table (retried / never retried) with the new routes; `extras`, `jobs/status`, `roles/problems` left out; `users/me/locale` never retried — needed by specs 0024–0029, 0033, 0035
- X-0016-9 — §2.3 — the `get-by-parent-ids` row states the `includeRoots` result — needed by spec 0030
- X-0016-6 / G14 — §2.3 — new paragraph: an action may take a standard segment its stack does not project (reports `export`, `export/start`); the reports routes are listed in §2.4's table — needed by spec 0033
- X-0016-6, X-0016-7 — §2.4 — the Core route list became an owner/routes/kind table and gained `client/sync`, `queryex/discover`, `users/me/locale`, `assignments/summary`, `assignments/seen`, `support-requests/submit`, the reports and dashboards actions; no `jobs/status` or `roles/problems` — needed by specs 0025–0029, 0033, 0035
- X-0016-8 — §3.3 — `QueryRequest.Filter` became `Filters` (each its own leaf, at most 32); `OrderBy` comment states the default `DefaultOrderBy ?? "Id desc"` — needed by spec 0030
- X-0016-9 — §3.3 — `ParentIdsRequest` takes `Filters`, `IncludeRoots`, `OrderBy` — needed by spec 0030
- G18 — §3.3 — no change: `DeleteByQueryRequest` keeps its single `Filter` per the ruling — needed by spec 0030
- X-0016-11 — §3.3 — `SaveRequest.Select` and `Concurrency` comments state what the SPA sends (spec 0031 §8.3, §9.1) — needed by spec 0031
- X-0016-12 — §3.4 — `MeResult.SecurablesFingerprint` and its sentence deleted; the paragraph says `permissions` is the composite and `AccessSummary.Tag` carries it — needed by specs 0025, 0032
- X-0016-13 — §3.4 — `UserProfileView` gains `DateTimeOffset ModifiedAt` (last) — needed by spec 0032
- X-0016-14 — §3.4 — `QueryDiagnostic.Clause ∈ select | filters[i] | orderBy | having` — needed by spec 0030
- X-0016-21 / G15 — §3.4 — `TellmaHeaders.Activity = "Tellma-Activity"` — needed by spec 0024
- X-0016-15 — §3.5 — the `centers/query` example sends `"filters": ["Name contains @q"]`; `errors[]` messages render from `Queryex_<code>` or the validation-code key — needed by specs 0029, 0030
- X-0016-16 — §3.6 — the `rows` bullet says the echo serves a client that holds list rows — needed by spec 0031
- X-0016-6 (cascade) — §3.9 — the Jobs bullet names `reports/export/start` among the 202 operations — needed by spec 0033
- X-0016-17 — §3.9 — the `me` bullet: `users/me` serves API and MCP callers; the SPA reads `MeResult` through `client/sync` and adopts its own writes (spec 0025 §9.5) — needed by spec 0025
- X-0016-18 — §3.10 — `IContractFingerprints` (`Tellma.Core.Api`) computes the fingerprints and the projector stamps them (sketch added); the SPA reads them from its client metadata; `contracts.json` stays the review snapshot; the SPA's response cites spec 0024 §5.6 — needed by spec 0024
- G12 — §3.10 — the `Tellma-Build` bullet: on every response, compared by the SPA on a 412 to tell a newer server from an older one — needed by spec 0024
- X-0016-33 / G30 — §3.10 — new bullet: `ContractsClientAssetContributor` writes `All` as the `contracts` section; a test pins emitted map = snapshot = endpoint metadata — needed by spec 0024
- X-0016-3 (cascade) — §4.1 — `IStackRegistry` sketch gains `ChildQueries` and `FindChildQuery`; the projector maps `query` per child query surface — needed by spec 0033
- X-0016-8 (cascade of X-0015-2) — §4.1 — the `StackDescriptor` sketch gains `DefaultOrderBy` and `DisplayName` after `DetailsExpand`, matching spec 0015 §2.2 — needed by specs 0029, 0030
- X-0016-32 / G19 — §4.1 — `ApiResourceAttribute.Queryable` (child types only, default true); the attribute applies to child types — needed by spec 0033
- G20 — §4.1 — no change: `RelatedSelectAttribute` and `DetailsExpandAttribute` stay declared here — needed by spec 0029
- X-0016-19 — §4.3 — `NoActivityStampMetadata` adds `client/sync` and `assignments/summary` — needed by specs 0025, 0028
- G16 — §4.3 — the long-operation sentence became two bullets: Excel operations under `tellma-export`/`tellma-import`, `reports/run`, `reports/export`, `reports/audience`, `dashboards/audience` under `tellma-reports`, all on `tellma-long` — needed by specs 0033, 0035
- X-0016-20 / G14 — §4.6 — an action segment may not be a standard segment the stack projects; child segments are unique and differ from stack segments; `[ApiRoute]` routes differ from both — needed by spec 0033
- X-0011-9 (cascade) — §4.6 — the reserved-segment rule is spec 0011's (a tenant route whose second segment is in `TellmaRoutes.SpaTenantSegments` fails startup) — needed by spec 0024
- X-0016-23 — §5.2 — the no-session cookie challenge is 401 `unauthenticated` (the code is defined in §7.5) — needed by spec 0024
- X-0016-8 (cascade) — §5.6 — the traversal walks each of `filters`; added the owner-path rule for child query surfaces (spec 0015 §5.2) — needed by specs 0030, 0033
- X-0016-21 — §6.1 — `Accept-Language` value note (`ar-SA`); rows `Tellma-Activity: background` and `traceparent`; `Tellma-Client` also required on `POST /api/client-telemetry` — needed by specs 0024, 0026
- X-0016-22 — §6.2 — `Tellma-Trace-Id` and `Tellma-Build` stamped at the head of `UseTellma`'s pipeline on every response, early refusals included; the filter-set headers are absent on early refusals; `permissions` is the composite; `preferences` and `permissions` absent without a connect prologue; the refresher (spec 0025 §5.3) compares them — needed by specs 0024, 0025, 0026
- X-0016-24 — §7.3 — the path sentence: the SPA splits a path into `name` and `[digits]` segments to place errors (spec 0031 §8.5), nothing else interprets it; argument errors are rooted at `arguments`; the grammar example shows `filters[1]` — needed by spec 0031
- X-0016-23 — §7.5 — new row: the no-session cookie challenge on an API endpoint is 401 `unauthenticated` (the framework list became a cause/status/code table) — needed by spec 0024
- X-0016-25 — §7.5 — a route miss under a tenant prefix is 404 `route-not-found`; an entity miss stays `not-found` — needed by spec 0024
- X-0016-26 — §8.2 — new policy row `tellma-telemetry` (partition `{sub}`, 60 per minute, sliding window of 6, queue 0) — needed by spec 0026
- X-0016-27 / G16 — §8.2 — new policy row `tellma-reports` (per user, 8 concurrent, queue 16 oldest-first) on `reports/run`, `reports/export`, `reports/audience`, `dashboards/audience` — needed by specs 0033, 0035
- G13 — §8.2 — `tellma-anonymous` no longer lists strings or the notification catalogue — needed by spec 0024
- G16 — §8.3 — `tellma-long` adds the four report and dashboard routes — needed by specs 0033, 0035
- G11 — §8.3 — the cancellation sentence became two bullets: reads, validation, the cold prologue and retry delays are cancelled; a sent `Persist` runs with `CancellationToken.None` to completion (spec 0012 §6.1, spec 0015 §14.1) — needed by spec 0024
- G13 — §8.5 — the immutable-or-`no-store` sentence for the two removed routes is gone; static assets, client metadata and `index.html` cite spec 0024 §6 — needed by spec 0024
- G22 (cascade) — §9.2 — the hub's event list adds `assignments.changed` — needed by spec 0028
- G13 — §9.4 — the two removed documents are gone; spec 0011 maps spec 0026's `POST /api/client-telemetry` — needed by specs 0024, 0026
- X-0016-28 — §10.1 — one sentence: the relay's instruments are spec 0026's `ClientTelemetryNames` (§6.4) on the same meter — needed by spec 0026
- X-0016-29 — §11.5 — `WhoamiResult.SecurablesFingerprint` removed from the sketch and from the `tellma_whoami` row — needed by spec 0032
- X-0016-30 — §11.5 — `tellma_describe` with `entity` renders the `IClientEntityModel` description (spec 0029 §2.1) gated by the tenant's shape, adding display name and default order — needed by spec 0029
- X-0016-31 — §11.5 — `tellma_query` also matches `IStackRegistry.ChildQueries`, under the owner's `Mcp` — needed by spec 0033
- G17 — §11.5 — `tellma_query` and `tellma_aggregate` pass a given `filter` as the one leaf of `Filters` — needed by spec 0030
- G32 — §11.9 — the identity-server list became bullets citing spec 0010 §A.1–§A.4 and §A.6 (old §2–§5, §7) — needed by spec 0010
- (cascades, all items) — §13.1, §13.3 — tests for the removed strings and catalogue routes are gone; added: child query surface, `Tellma-Activity`, abort vs sent persist, `filters[i]`, `includeRoots`, `unauthenticated`, `route-not-found`, the emitted-contracts parity, early headers, `tellma_query` over a child entity — needed by specs 0024, 0030, 0033
- X-0016-18, G32 — §14 — Projects bullet adds `IContractFingerprints` and its contributor; out of scope cites spec 0010 Part A and spec 0024 §10.4, §5.6 — needed by specs 0010, 0024
- X-0016-18 — Decisions 27 — adds the framework-free fingerprint computation feeding both the check and the emit — needed by spec 0024
- X-0016-8 — Decisions 29 (new) — `Filters` as separate leaves — needed by spec 0030
- X-0016-22 — Decisions 30 (new) — trace id and build on every response — needed by specs 0024, 0026
- X-0016-27 — Decisions 31 (new) — the per-user `tellma-reports` policy with a queue — needed by specs 0033, 0035
- X-0016-5, X-0016-10 — not applied: dropped by the rulings (no `extras` operation, no `ExtrasRequest`; Q-12)
- X-0016-18, X-0016-3 (scope) — Context — closing list "Other specs that carry parts of this design": spec 0024 §3.4, §5.6, §6, §10.4 and spec 0033 §10 — needed by specs 0024, 0033

## Not applied

- None. Every item the rulings keep for 0016 is applied; X-0016-5 and X-0016-10 are dropped by the rulings (Q-12), and `extras`, `jobs/status` and `roles/problems` are left out of X-0016-4 and X-0016-7 as ruled.

## Notes for other editors

- 0011 §5.1: §6.2 here says `UseTellma` stamps `Tellma-Trace-Id` and `Tellma-Build` at the head of its pipeline; 0011's fixed pipeline order should name that stage (no 0011 item states it).
- 0015 §2.2: the `StackDescriptor` sketch here carries `DefaultOrderBy` and `DisplayName` after `DetailsExpand` (X-0015-2's placement) and `IStackRegistry` carries `ChildQueries` / `FindChildQuery` (X-0015-3).
- Naming chosen here (not in any note): `ContractsClientAssetContributor`; options `ConcurrentReportsPerUser`, `ReportQueuePerUser`.
