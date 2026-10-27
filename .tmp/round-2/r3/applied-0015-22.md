# Applied: group 0015-22 (specs 0015–0022 and ARCHITECTURE.md)

Baseline `063e313`. Edited: 0015, 0016, 0017, 0018, 0019, 0021, ARCHITECTURE.md. Checked and left
unchanged: 0020, 0022. No section was renumbered. 0016's Review flags 3–22 became 2–21 after flag 2
was removed; no spec cites a 0016 flag by number (grepped). No `§F.CUT-` marker remains in these
files.

## 0015 (CRUD service pipeline)

- **§2.2 `ActionDescriptor`, `EntityActionDescriptor`, `ApiActionDescriptor`** (decision 20): each
  gains `bool Long` after `Idempotent`, passed to the base. The `ActionDescriptor` row of the member
  table lists `Long` among the members that come from the attribute.
- **§2.3 `EntityActionAttribute`, `ApiActionAttribute`** (decision 20): gain `bool Long = false`
  (after `Idempotent`). The projection-metadata row now covers `Long`: a long-running operation,
  run by spec 0016 under its long request timeout (0016 §8.3); the client learns `Idempotent` and
  `Long` from the Contracts chunk's `idempotent` and `long` lists (0016 §3.10).
- **§5.2 step 4** (decision 21): the traversal refusal is
  `ForbiddenException("forbidden", E, "Read", ForbiddenReason.RestrictedPath, <the path>)`.
- **§5.2 step 4, "Select-item types"** (decision 1): "Two kinds of select item are refused" →
  one kind (`Geography`); the level-navigation bullet (`Level{j}`, `Query.LevelNotSelectable`,
  `§F.CUT-9`) deleted; the Geography rule restated as one sentence.
- **§6.6 step 1** (decision 21): the save pre-check's 403 carries `ForbiddenReason.OutsideFilter`.
- **§7.3 `ValidationCodes`** (decision 1): `QueryLevelNotSelectable` deleted.
- **§9.1 "No grant"** (decision 21): `ForbiddenReason.NoGrant`.
- **§9.2** (decision 21): the save pre-check and the delete/action `50403` → reason `OutsideFilter`.
- **§9.3** (decision 21): the post-check's translation carries `ForbiddenReason.OutsideFilter`.
- **§10.1 translation** (decision 21): `50403` → `ForbiddenException` (`OutsideFilter`).
- **§14.1** (decision 21): `ForbiddenException(Code, Resource, Action, ForbiddenReason Reason,
  string? Path = null)` and `enum ForbiddenReason { NoGrant, OutsideFilter, RestrictedPath }`
  (kebab-case on the wire). New paragraph after the sketch: which raise site sets which reason
  (§9.1; §6.6, §9.2, §9.3; §5.2 step 4), `Arguments` = `resource`, `action`, `reason`, `path`; no
  roles, filters or other users in the error (`access/check`, spec 0014 §10, answers those).
- **§14.2** (decision 21): the `RowSecurityException` row raises `OutsideFilter`.
- **§18.1** (decisions 1, 20, 21): Traversal drops "the level-path refusal" and pins the
  `RestrictedPath` reason with the path; Limits and translation adds the reason of every raise site;
  Descriptors adds each action's `Long` and `Idempotent`.
- **§18.2 Row-level security** (decision 21): the 403s assert `OutsideFilter`; no grant → `NoGrant`.
- **Decisions record** (decisions 20, 21): new 35 (`Long` and `Idempotent` declared once on the
  action) and 36 (a 403 carries its reason, set at the raise site).

## 0016 (Web API surface and MCP)

- **Context, "Other specs"** (decisions 18, 20): spec 0024 bullet → "the SPA's copy of the
  Contracts chunk … and plain JSON parsing".
- **Goals** (decision 18): "lossless numbers" → "`decimal` and `long` as JSON strings".
- **Non-goals** (decision 18): "The SPA's lossless JSON parser …" → "The SPA's JSON handling and
  its outdated-client handling — spec 0024 (§10.4, §5.6)".
- **§2.2 Client retry contract** (decision 20): the rule is "published per route in the Contracts
  chunk (§3.10)"; the retried class is "every action declared `Idempotent = true` (spec 0015 §2.3)"
  (both action kinds), which now names `activate` and `deactivate` (spec 0015 §11.1 already
  declares them `Idempotent = true`); they leave the "never retried" list. See Open questions.
- **§2.4 route table** (decision 20): `reports/audience`, `run`, `export` are long;
  `dashboards/audience` is "idempotent, long, not a mutation".
- **§3.1 JSON options** (decision 18): `NumberHandling = Strict` refuses quoted `int`/`double`;
  `decimal` and `long` have converters of their own.
- **§3.2 scalar table** (decision 18): `int`/`short`/`byte` number; `long` string (every digit,
  inbound string or number); `decimal` string (full scale, no exponent, inbound string or number,
  `Precision` rule kept).
- **§3.2 "Lossless numbers are a client obligation"** (decision 18): replaced by "`decimal` and
  `long` are JSON strings" (why; any client's plain parser; inbound string or number, a non-decimal
  string is 400 `bad-request`; MCP forwards text; per-column representation by JSON kind plus
  `type`/`scale`), and a two-line example (`creditLimit`, `targetId`).
- **§3.2 Wire ids** (decision 18): an id travels in its entity's key type (number for `int`, string
  for `long`); `EntitiesResult<T>.Ids` and `ConcurrencyConflict.Id` follow the stack's `KeyType`;
  inbound number or string; the range check unchanged.
- **§3.4 `UserProfileView`** (decision 24): gains `string? PreferredRegion, DigitStyle?
  PreferredDigits` after `PreferredTimeZone`; `DigitStyle` is spec 0013's.
- **§3.5** (decisions 18, 1): `long[]` and `decimal[]` cells are strings; the `QueryColumn` sketch
  gains `int? Precision` (spec 0012 §3.3, read by 0010 §F.15), commented "the converter never
  writes Precision".
- **§3.7 Precision** (decision 18): "A number whose textual form …" → "A `decimal` whose digits …".
- **§3.8** (decision 21): a readable id outside the grant is 403 `forbidden` with reason
  `outside-filter`.
- **§3.10** (decisions 20, 22): `IContractFingerprints` gains `IReadOnlySet<string> Long` and
  `IReadOnlySet<string> Idempotent`; new bullet "The route facts" (long = action declares `Long`;
  idempotent = a standard read or an action declaring `Idempotent`); `contracts.json` lists both;
  the contributor writes the `contracts` section from `All`, `Long`, `Idempotent`, and its test
  matches both lists against the endpoints; new JSON example of the section; the `Tellma-Build`
  bullet states SemVer precedence ignoring `+<sha>`, newer → client outdated, older → server
  outdated, same `X.Y.Z[-pre]` other sha → unordered, ask the service worker (spec 0024 §5.6).
- **§4.1 restated descriptors** (decision 20): `Long` added to the three records;
  `EntityActionDescriptor` also gains the `SingleTarget` member it was missing against spec 0015.
- **§4.3** (decision 20): every action whose descriptor has `Long = true` carries `tellma-long`;
  the Excel and Reports families additionally carry their concurrency policies.
- **§4.5** (decisions 23, 20): the escape-hatch example → `reports/credit-exposure/stream`,
  `CreditExposureReport.StreamAsync`, `acme.CreditExposureReport`; new sentence: a hand-mapped
  endpoint is in neither Contracts list, takes `tellma-long` itself, and its caller passes `long`.
- **§5.6** (decision 21): a non-projected column is 403 naming `E` and `Read`, reason
  `restricted-path` with the path.
- **§6.1 `Accept-Language` row** (decision 24): the SPA sends its locale (`ar-SA-u-nu-latn`);
  language and region pick the culture; extensions stripped, `nu` ignored (spec 0013 §9.2);
  precedence adds region: `User.PreferredRegion` → tenant `Region` → the language's default region.
- **§7.1 `ForbiddenException` row** (decision 21): `arguments.reason` (`no-grant`,
  `outside-filter`, `restricted-path`) and `arguments.path` for `restricted-path`.
- **§8.3 `tellma-long`** (decision 20): every action declared `Long = true`, among them 0019's four
  Excel operations, 0033's `reports/run`/`export`/`audience`, 0035's `dashboards/audience`; plus the
  blob upload.
- **§11.1 MCP `title`** (decision 25): `"<Tenant name> (<display name>)"`, the distribution's display
  name (spec 0011 §5.9).
- **§11.6 sensitive refusal text** (decision 25): "this operation requires the {displayName} web
  app", `{displayName}` in the message language (`Distribution_DisplayName`, else the configured
  name; spec 0024 §3.1).
- **§11.9** (decision 23): `AgingReportTools` → `CreditExposureTools`.
- **§13.3 Wire** (decision 18): decimals and longs round-trip as strings, read back from string or
  number; quoted `int`, `NaN` and exponent-form decimal strings are 400; new assertion: `int` ids
  numbers, `long` ids strings in `ids`, row cells and conflicts.
- **§13.3 Security** (decision 21): `no-grant` and `outside-filter` assertions, no role/filter/user
  in the body; `Parent.CreatedAt` refused with `restricted-path` and the path.
- **§13.3 Context** (decision 24): `Accept-Language: ar-SA-u-nu-latn` negotiates `ar-SA`; region
  fallback asserted.
- **§13.3 Contract** (decision 20): the `long` and `idempotent` lists asserted against endpoints.
- **§14 DoD, Not in scope** (decision 18): "the SPA's lossless parser" → "the SPA's JSON handling".
- **Decisions record** (decisions 18, 20): decision 3 adds that the server publishes each route's
  retry class and long flag in the Contracts chunk; decision 5 rewritten in place: `decimal` and
  `long` as JSON strings (RFC 7493), JSON numbers with a lossless parser as the alternative.
- **Review flags** (decision 18): flag 2 (decimals as numbers vs strings) deleted; 3–22 renumbered
  2–21.

## 0017 (Blob storage)

- **§3.1 member table, `BlobDescriptor.ExpiresAt` row** (decision 18): the descriptor serialises
  camel-cased with `size` (a `long`) as a JSON string (spec 0016 §3.2).

## 0018 (Core and GL reference stacks)

- **§1.2 composition illustration** (decision 24): `.Languages(["en-GB", "ar-SA"])`, commented
  "default regions: spec 0013 §7.3".
- **§1.3 seams table, spec 0013 row** (decision 24): adds `ILanguageCatalog` (`IsOffered`,
  `IsRegionOffered`), `DigitStyle`; used in §3.1 too.
- **§1.4 Reports bullet** (decision 23): `acme-reports/credit-exposure` under
  `acme.CreditExposureReport × Read`, described as per customer the credit limit, open invoices
  posted on or before `AsOf`, the headroom and an over-limit flag.
- **§3.1** (decisions 24, 19): `MeSaveRequest` gains `string? PreferredRegion, DigitStyle?
  PreferredDigits` (thirteen self-editable members); `MeLocaleRequest(string? Language, string?
  Calendar, string? TimeZone)`; `MeLocaleResult` gains `string? PreferredTimeZone`.
- **§3.2 PreprocessAsync** (decision 24): `PreferredLanguage` trimmed and lower-cased (a bare code;
  `ar-SA` fails the language rule); `PreferredRegion` trimmed and upper-cased.
- **§3.2 rules table** (decisions 24, 19): new rows `PreferredRegion` → `Users.RegionNotOffered`
  (`PreferredRegion`) and `PreferredDigits` (no code, enum binding); the `me/locale` row covers
  `TimeZone`; `me/locale` applies four rows at paths `language`, `calendar`, `timeZone`.
- **§3.5 `SaveMe`** (decision 24): "eleven" → "thirteen" self-editable members.
- **§3.5 `SetMyLocale`** (decision 19): callers add the device time-zone prompt and its automatic
  update (spec 0024 §7.8); the trailing `SELECT` adds `[PreferredTimeZone]`.
- **§3.5 preference-key example** and **§9 unit bullet** (decision 23):
  `report.acme-credit-exposure.params` passes; `Report.exposure`, `report.-exposure` fail.
- **§9 integration** (decisions 21, 19, 24): the admin `save` of one's own row is 403 `no-grant`;
  `me/locale` cases add a time zone only, all three, a regioned language, an unknown zone at
  `timeZone`; new "Regional formats" bullet (`PreferredRegion` upper-cased, `PreferredDigits`,
  `Users.RegionNotOffered` at `preferredRegion`).
- **Decisions record 14** (decision 19): `me/locale` serves the session menu's switches and the
  time-zone prompt.

## 0019 (Excel codec)

- design-measures §8.4 confirmed: nothing names levels or cube truncation (the manifest's
  `Truncated` row is the export flag, always false); no edit for decision 1.
- Decision 18: 0019 shows no JSON wire examples; its `long` members (`ImportPlan.TotalRows`,
  `ImportPlanSheet.Rows`, request `Ids`) follow spec 0016 §3.2 with no edit.
- **§1.2 and the §2.1 `ExcelOperations` sketch comments** (decision 20): `export`,
  `export-for-import`, `inspect-import` and `import` declare `Long = true`, so one flag drives the
  `tellma-long` policy and the Contracts chunk (spec 0016 §8.3, §3.10).
- **§2.2** (decision 20): the four synchronous operations run under the long timeout because they
  declare `Long = true`.
- **§12.3 step 2** (decision 21): the job error is `JobError("forbidden", { resource, action,
  reason })` (spec 0015 §14.1).
- **§12.3 step 3, Report case** (decision 4, per 0033's request): "A list or a summary streams
  through `IReportEngine.PagesAsync` in pages of `MaxTake` rows (spec 0033 §12.1); a summary with
  totals writes its Total row last, from the last page (spec 0033 §12.2)."

## 0020 (Background jobs and the scheduler)

- No change. Decision 25 checked: every "Tellma" is an identifier (meters, `tellma_job`,
  `Tellma:Jobs`, `MapTellma`); no user-facing copy names Tellma. No other decision touches it
  (its `Idempotent = true` on retry/cancel/resume is `SqlOptions.Idempotent`, and §11.1 already
  says none declares the retry-safe attribute).

## 0021 (Notifications, inbox and hub)

- **§2.2 `Notification.TargetId` comment** (decision 18): "a JSON string on the wire (spec 0016
  §3.2)".
- **Review flag 3** (decision 14): the push spec owns the service-worker wrapper script that imports
  Angular's `ngsw-worker.js` and adds the `push` and `notificationclick` handlers (spec 0024 §5.4).
- Decision 25 checked: no user-facing copy names Tellma. Decision 8 (avatars): 0021 renders no
  `tm-avatar` (the actors' display is spec 0028's), so 0023-29's request needs no edit here.

## 0022 (Email outbox)

- No change. Decision 25 checked: the email copy names the tenant (`tenantName`), never Tellma; the
  link `https://acme.app.tellma.com/…` is Acme's host under ARCHITECTURE's `<slug>.app.tellma.com`
  rule, not copy.

## ARCHITECTURE.md

- **Five dimensions table, Locale row** (decision 24): "(Ar, ArAe, Et, …)" → "(Ar, Et, …)"; adds
  that regional formats (region and digits, from CLDR) are Core's and a Locale pack never carries
  them.
- **Distribution repo layout, Client esproj comment** (decision 11): "scaffolded by
  `tellma-client init`" → "scaffolded with the server projects by the `dotnet new
  tellma-distribution` template".
- **Per-dimension contents, Locale bullet** (decision 24): regional formats are not a Locale
  concern (region and digits choose CLDR patterns in Core, client and server alike; spec 0024).
- **Reports — three tiers, "A report definition is data"** (decision 1, design-measures §8.5): the
  `GROUPING SETS` and level-navigation clauses replaced by "A pivot groups at leaf grain in SQL; the
  cube builder rolls up trees, subtotals and totals in C#, evaluating each measure from its
  aggregate partials with the engine's evaluator". This also answers 0010-F's request (the line
  names the `uq` month map, `FilterTree.Exists` and the measure evaluator).
- **Frontend, Accessibility & i18n** (decision 24): the active locale joins the UI language with a
  region and a digit style, the user's or the tenant's default (`ar-SA-u-nu-latn`), and the server
  formats with the same region; "Calendars and regional formats are Core's, never a Locale pack's".
- **App framework table, `@tellma/core-tools`** (decisions 11, 12): commands `emit`, `serve`,
  `build` (whose first and last steps check the sources and the output); `init` and `check` gone.
- **App framework, One merge rule** (decision 13): which entities, widget types and coded reports
  exist is the server's; the client follows the metadata and never hides them.
- **App framework, PWA bullet** (decision 14): Angular's worker takes security fixes only; the
  platform stays on it (per-tab versions), fork fallback into `@tellma/core-tools`, Web Push wraps
  it in a script importing `ngsw-worker.js`.
- **Tenant & Distribution Onboarding, New distribution** (decision 11): the template scaffolds the
  server projects and the Client project together; a later distribution-tooling spec owns it.
- **Observability, UI version bullet** (decision 22): on `412` the client orders versions by SemVer
  precedence ignoring the commit; immediate when each deployment bumps `X.Y.Z[-pre]`, otherwise the
  service worker decides (spec 0016).
- Decisions 18 and 25: ARCHITECTURE states no number-encoding or display-name rule, so nothing to
  replace.

## Requests for other files

- **0024 (group 0024), §3.4 `long` bullet:** "the actions declared `Long = true` (spec 0015 §2.3)
  and the Excel operations" → "the actions declared `Long = true` (spec 0015 §2.3), spec 0019's
  Excel operations among them" — 0019 §1.2 now declares `Long = true` on `export`,
  `export-for-import`, `inspect-import` and `import`, so the projector reads one flag.
- **0024 (group 0024), §10.5 or §10.1 (if it lists retry classes by name):** `activate` and
  `deactivate` are in spec 0016 §2.2's retried class (their `Idempotent = true`, spec 0015 §11.1),
  so they appear in the Contracts chunk's `idempotent` list; the SPA still calls them with `write`,
  which never retries. No change needed unless 0024 names them as non-idempotent.
- **0013 (group 0011-14), §1.1:** spec 0016 §3.4 and spec 0018 §3.1 cite `DigitStyle` as spec
  0013's; please keep it there (design-locale §1.2 places it in
  `Tellma.Core.Abstractions.Localization`).
- **0011 (group 0011-14), §5.9:** spec 0016 §11.1 cites "spec 0011 §5.9" for the distribution's
  display name in the MCP server `title`; please keep `DistributionInfo.DisplayName` there.

## Open questions

- **`activate`/`deactivate` retry class.** Spec 0015 §11.1 declares them `Idempotent = true`, while
  spec 0016 §2.2 listed them as never retried. With decision 20's chunk derived from the flag, I
  moved them into the retried class (a repeat skips rows already in the target state). The
  alternative is to declare them `Idempotent = false` in 0015 §11.1, which would also flip their MCP
  `idempotentHint`. Ahmad to confirm.
- **Generic id lists.** Spec 0016 §3.2 now writes `EntitiesResult<T>.Ids` and
  `ConcurrencyConflict.Id` in the stack's key type (numbers for `int` keys) so editor-notes' "an
  `int` key stays a `number`" holds; the converter needs the stack's `KeyType`, which the endpoint
  knows. The alternative (always strings for these `long` members) would make every int-keyed save
  answer string ids.
- **`IContractFingerprints` name.** It now also answers `Long` and `Idempotent`; I kept the name to
  avoid a cascade. A rename (`IContractFacts`) is possible if preferred.

## Fix pass

Findings: V4-01..V4-06, V5-04, V5-05, V7-07, V7-08, V8-20, V8-21; rulings R-7, R-10, R-12, R-15,
R-22, R-28 checked. Re-grepped every file for editor-notes §3's Removed names and `§F.CUT-`: no
hits beyond 0016 §13.2's "tree of three levels" (a fixture) and 0019 §5.3's manifest `Truncated` row
(the export flag, always false), both unrelated to the removed design.

### 0015 (CRUD service pipeline)

- **§14.1 `ConcurrencyConflict`**: `long? ModifiedById` → `int? ModifiedById`; a `core.Users` id is
  an `int`, so it stays a JSON number on the wire, as 0016 §7.4's example and 0031 §9 show (V4-02).
- **§14.1 `LimitExceededException`**: `(string Limit, long Actual, long Maximum)` →
  `(string Limit, int Actual, int Maximum)`; every ceiling it reports (`StackLimits`, the Excel
  caps, the securables cap) is an `int`, so `arguments.actual` and `.maximum` are JSON numbers
  (V4-05).

### 0016 (Web API surface and MCP)

- **§1.1, §3.10 (sketch and two bullets), §14 DoD**: `IContractFingerprints` renamed
  `IRouteContracts` at all five places (V4-01, V8-20, R-12).
- **§3.10 "The contributor"**: the contributor writes the Contracts chunk's three sections
  `fingerprints`, `long` and `idempotent`, and its test compares the emitted chunk with
  `contracts.json`; the example is captioned "the Contracts chunk" (V4-03).
- **§13.3 Contract**: "The emitted `contracts` section" → "The emitted Contracts chunk" (V4-03).
- **§3.2 "`decimal` and `long` are JSON strings"**: new bullet **Schemas**: OpenAPI (§12.1), MCP
  `inputSchema` (§11.5) and the client metadata's argument schemas (spec 0029 §2.2) describe a
  `decimal` as `{ "type": ["string", "number"], "format": "decimal" }` and a `long` as
  `{ "type": ["string", "integer"], "format": "int64" }`; outbound is always the string (V4-06).
- **§13.3 Wire**: new assertion that the OpenAPI document and an MCP `inputSchema` describe
  `decimal` and `long` members with those types (V4-06).
- **§7.2**: new bullet: a `forbidden` problem's `detail` renders `resource` and `action` as the
  permission's labels through the securables' label chains (spec 0013 §10.1); `arguments` keep
  the raw pair (V8-21, request from 0011-14).
- **Review flags**: flag 2 restored in place as "**`decimal` and `long` as JSON strings** (§3.2)
  versus JSON numbers with a lossless client parser; flips if users of generic tools (Power Query,
  BI) need numeric columns without setting a type"; flags 3–22 keep their baseline numbers (the
  editor pass's 2–21 renumbering is undone; nothing cites them) (V7-07).

### 0019 (Excel codec)

- **§12.3 step 2**: the denial's job error is `JobError("forbidden", { resource, action, reason,
  path })`, `path` only for `restricted-path`, so the report case's refusal in step 3 carries the
  path (V4-04). R-22 confirmed: step 3 streams through `IReportEngine.PagesAsync` (spec 0033
  §12.1) and writes a summary's Total row from the last page (spec 0033 §12.2).

### 0020 (Background jobs and the scheduler)

- **§8 "The job scope"**: the negotiation initializer resolves the locale from the run-as user's
  `PreferredLanguage`, `PreferredCalendar`, `PreferredTimeZone` and `PreferredRegion`, else the
  tenant defaults (spec 0013 §9.3); the bullet's tail rewrapped, no other wording changed (V5-05).

### 0021 (Notifications, inbox and hub)

- **§4.5 Load statement**: the user `SELECT` adds `[u].[PreferredRegion]`, so step 2's negotiator
  gets the recipient's region and a notification email formats in it (V5-04).

### ARCHITECTURE.md

- **App framework, "An installable PWA on the Angular service worker"**: "the platform stays on it
  because it already ties each tab to its version" → "the platform uses it because it ties each
  tab to its version" (V7-08).

### Rulings checked with no edit

- **R-7**: no `varchar(6)` or digits column in these files; 0018 shows only the `me/save` members.
- **R-10**: 0015 §11.1 declares `activate`/`deactivate` `Idempotent = true`, and 0016 §2.2 lists
  them in the retried class; the two agree.
- **R-15**: these files use none of the listed client names except through citations; nothing to
  align.
- **R-28**: 0016 §4.5 already says only a hand-mapped endpoint is outside the Contracts lists and
  its caller passes `long`; coded reports are projected `[ApiAction]`s (0033 §11).

### Skipped findings

- None.

### Unresolved for other groups

- **0024 (group 0024), Review flag 2** (optional, V7-07): it may cite the wire call as spec 0016's
  flag 2 instead of restating "`decimal` and `long` as JSON strings | JSON numbers with a lossless
  client parser | users of generic tools …"; 0016 flag 2 now carries it.

## Readability pass

No meaning changes; no section, decision or flag renumbered. 0020 and 0022 left untouched.

- **0015 §2.2, §2.3 sketches:** descriptor records and `EntityActionAttribute` rewrapped near 100 columns.
- **0015 §2.3 projection-metadata row:** the semicolon chain split into one sentence per member.
- **0015 §5.2 step 4, Select-item types:** the `Geography` refusal leads, its diagnostic fields as two bullets.
- **0015 §14.1:** the `ForbiddenReason` comment moved above the enum; `OutsideFilter` names its raise sites in its own sentence.
- **0016 §3.1:** the `NumberHandling` parenthetical shortened; the converter clause follows it.
- **0016 §3.2:** Inbound bullet cites the table instead of restating it; Schemas lists the two JSON schemas as sub-bullets; Wire ids split into three bullets.
- **0016 §3.4, §3.5, §4.1 sketches:** `UserProfileView`, `QueryColumn` and the descriptor records rewrapped.
- **0016 §3.5 opener:** the cells-are-strings clause made its own sentence.
- **0016 §3.8:** the delete refusals (404, 403 `outside-filter`, `Fk.InUse`) as three bullets.
- **0016 §3.10:** the contributor's test assertions as sub-bullets; the chunk example indented into that bullet and reflowed; `Tellma-Build` precedence outcomes as three sub-bullets.
- **0016 §4.5:** the hand-mapped-endpoint rule in its own paragraph, two sentences.
- **0016 §6.1 `Accept-Language` row:** effect and precedence cells split into sentences (Language / Region).
- **0016 §8.3 `tellma-long`:** the blob upload first, the `Long = true` examples as sub-bullets per owning spec.
- **0016 §11.1, §11.6:** the MCP `title` and the `{displayName}` refusal text each split into shorter sentences.
- **0016 §13.3 Wire, Security, Context, Contract:** compound assertions split into one bullet each; Context became sub-bullets.
- **0016 Decisions 3 and 5:** the why split into sentences; "still" dropped.
- **0017 §3.1 `BlobDescriptor.ExpiresAt` row:** the `size` encoding in its own sentence.
- **0018 §1.4 Reports:** what the credit-exposure report shows in its own sentence.
- **0018 §3.1 sketch:** trailing comments of `MeSaveRequest`, `MeLocaleRequest`, `MeLocaleResult` moved above the records.
- **0018 §3.2 rules table:** the `me/locale` row reads "sets at least one of `Language`, `Calendar` and `TimeZone`".
- **0018 §3.5 `SetMyLocale`:** opens with "Two callers use it instead of `me/save`".
- **0018 §9 `me/locale` and Regional formats:** test cases as sub-bullets; Regional formats as two sentences.
- **0019 §1.2:** the per-method attribute values cut and cited to the §2.1 sketch, which states them; the stacks' method lists as two bullets.
- **0019 §2.2:** timeouts and limits as two bullets (long methods; the three `/start` methods).
- **0019 §12.3 steps 2–3:** the `path` condition and the summary Total row each in their own clause or sentence.
- **0021 Review flag 3:** the wrapper-script ownership in its own sentence.
- **ARCHITECTURE.md:** Locale row merged to "calendars and regional formats … are Core's, never a Locale pack's"; Reports pivot clause, Accessibility locale sentence, `@tellma/core-tools` row, One merge rule ("The server decides …"), PWA bullet, New distribution and UI version bullets split into shorter sentences.
