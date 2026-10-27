# Review round 1: notes for the editors (binding with decisions.md)

Read `decisions.md` first. This file settles what the two design notes left open, fixes the shared
names every editor must use, and says who owns which file. Where this file and an analysis
(`review-r1/g*.md`) disagree, this file wins. The two design notes win over the analyses on their
topics.

## 1. State of the specs before you start

- Baseline: commit `063e313`. `git diff HEAD -- <file>` shows what this round changed.
- **Part F is already renumbered** in every spec by a script: old §F.10–§F.15 are now §F.8–§F.13.
  Citations of the two cut items read `§F.CUT-8` (grouping sets) and `§F.CUT-9` (tree level
  navigations). Every `§F.CUT-` marker must be gone when you finish: delete the text that exists
  only for levels and grouping sets, or rewrite it to the leaf-grain design.
- **Numbering in the sources:** `review-r1/g1-0010-partF.md` uses the OLD Part F numbers (its
  §F.13 is today's §F.11, its §F.10 today's §F.8). `design-measures.md` uses the NEW numbers, and
  its new items are §F.14 (`QuerySpec.Measures` and partial columns) and §F.15 (measure evaluation
  in C#).

## 2. Settled choices

- **Measures design** (`design-measures.md`, as revised): accepted whole.
  - **Text and hierarchy `min`/`max` combine by an SQL `DENSE_RANK`** (exact), not by an
    approximate .NET comparer. This deviates from decision 1's "approximate for text" because it is
    exact at the same cost; record it as 0010 Part F flag F2 (alternative: the comparer).
  - **F5 is applied, narrowly.** 0012 §6.4 gains a row: SQL errors 8134 (divide by zero) and 8115
    (arithmetic overflow) → internal `ArithmeticFailureException(Reason)` with reason
    `divide-by-zero` or `overflow` (it joins the internal data-layer exceptions listed there). The
    report engine (0033) maps it to `422 Report.MeasureFailed` with no cell. Every other caller
    still surfaces it as 500 (0015 is unchanged); keep a 0033 flag: "the CRUD pipeline could map it
    to a 422 as well".
  - F1, F2, F3, F4 and F6 become review flags in the spec that owns each (F1–F3 in 0010 Part F;
    F4 in 0034; F6 in 0033).
- **Locale design** (`design-locale.md`): accepted whole, flags R1–R9 placed as its §4 says.
- **Retry classes (decision 20).** Server side, `[EntityAction]` and `[ApiAction]` (0015 §2.3) gain
  `bool Long = false` and `bool Idempotent = false`; `ActionDescriptor` gains both. A `GET` route
  is always idempotent; a `POST` route is idempotent only when its action declares `Idempotent =
  true` (a `POST` that only reads, such as `reports/run` or `queryex/discover`). The Contracts
  chunk (0016 §3.10, 0024 §3.1) carries `{ fingerprints, long: readonly string[], idempotent:
  readonly string[] }`, where `idempotent` lists the non-`GET` routes declared idempotent, keyed
  like `fingerprints`. `TmApi`: a long route gets the 6-minute watchdog on its own; `read` on a
  non-`GET` route missing from `idempotent` throws in development and never retries in production.
  The `long` call option stays only for hand-mapped routes. A test matches both lists against the
  endpoints.
- **Decimals as strings (decision 18).**
  - Wire: `decimal` and `long` are JSON strings, full scale, no exponent; inbound accepts a string
    or a number. `int`, `short`, `byte` and `double` stay JSON numbers.
  - Client type: `TmDecimal` is a branded string (`string & { readonly [tmDecimalBrand]: true }`)
    for both `decimal` and `long` values. Helpers live on `TmDecimals`: `parse(text)`,
    `isDecimal(value)`, `add`, `subtract`, `compare`, `toNumber`, `fromNumber`. Display formats the
    text through `Intl.NumberFormat.format(string)`.
  - Ids: an `int` key stays a `number`; a `bigint` key or pointer (`TargetId` in 0021 and 0028) is a
    `TmDecimal` on the client. Generic APIs that take any entity's id use `TmId = number |
    TmDecimal` (0024 §9.4 builders, 0029's `label`, `displayWith` and the like). Entity-specific
    types keep `id: number` where the key is `int`.
  - The lossless parser (0024 §10.4) and the `TmDecimal` class go; `TmJson` loses its object
    member; bodies parse with `JSON.parse`. Lint: typescript-eslint `restrict-plus-operands`.
- **`TmUrls` (decision 17).** `forTenant(id): TmTenantUrls`; outside a tenant a plain builder
  throws in development and returns `/tenants` with telemetry `tm.urls.no-tenant` in production.
- **White labelling (decision 25).** No user-facing copy in any spec says "Tellma": UI text,
  dialogs, toasts, error messages, email bodies and notices say `{displayName}` (the localized
  display name). Identifiers stay as they are (`Tellma.Core`, `tellma-long`, the `Tellma-Build`
  header, `Tellma:` config keys, `@tellma/core`, log event names). The About dialog's credit line
  is the distribution's own `Distribution_About` text; no "built on Tellma" is forced. Text written
  for developers (README, build output, problem codes, logs) may still say Tellma.
- **Acme credit control (decision 23).** Use g5 item 13's rename table at every place it lists,
  and apply the same renames anywhere else you meet the old examples (`bank-reconciliation`,
  `acme-reports/aging`, "aging" pages and reports).

## 3. Shared names (use exactly these)

| Area | Names |
|---|---|
| Report engine | `QuerySpec.Measures`, `QueryDiscoverySpec.Measures`, `CompiledQuery.Measures`, `MeasurePartial`, `MeasurePartialKind`, `CompiledMeasures`, `CompiledMeasure`, `MeasureContext`, `MeasureEvaluator`, `PartialTable`, `MeasureValue`, `QueryexNumericType`, `QueryexEvaluationException`; codes QX4010, QX4011, QX6001, QX6002; `QueryColumn.Precision`; `ReportTable.TotalsOmitted`; `422 Report.MeasureFailed` (`pointer`, `reason`, `row`, `column`); `413 Report.TooManyCells`; `ReportOptions.MaxPivotCellPartials`; `ReportEvents.MeasureFailed`; `ArithmeticFailureException(Reason)` |
| Removed | `TreeLevels`, `Level{j}`, `Query.LevelNotSelectable`, `QueryLevelNotSelectable`, `Entity_Level`, QX5008, the report `levels` field, `CubeTruncation`, `Truncated`, `TooManyLevels`, grouping sets, `GROUPING_ID`, the level indicator, `treeLevels` |
| Locale | `DigitStyle`, `LanguageFormats`, `ILanguageCatalog.Formats`/`IsRegionOffered`, `User.PreferredRegion`, `User.PreferredDigits`, `Settings.Region`, `Settings.Digits`, `Users.RegionNotOffered`, `Settings.RegionNotOffered`, `MeLocaleRequest.TimeZone`, `tm-region-field`, `tm-digits-field`, `tm-auto-time-zone`, `TmDeviceZone`, `TmZoneDoc`, `TM_DEVICE_TIME_ZONE`, `TmL10n.deviceTimeZone`/`zoneMismatch`/`zoneLabel`, `TmZoneLabelMode`, `tmFormatZoneOffset`, `tmFormatZoneName`, `TmFormats.zoneSuffix` |
| Launch | `TenantLanding.SingleLiveTenant`, `tmSingleLiveTenant`, `launch-landing-vectors.json`, `TmLoginMark`, `__tmBoot.auto`, `TmLaunch` (`automatic`, `markAutomatic`, `settle`, `takeAutomatic`), `BffLandingFailed` |
| Client API | `TmDecimal`, `TmDecimals`, `TmId`, `TmUrls.forTenant`, `TmTenantUrls`, `Long`, `Idempotent`, `ActionDescriptor.Long`/`Idempotent` |
| Errors | `ForbiddenException.Reason` (`ForbiddenReason`: `NoGrant`, `OutsideFilter`, `RestrictedPath`, sent as `no-grant`, `outside-filter`, `restricted-path`), `ForbiddenException.Path`, `arguments.reason`, `arguments.path`, `TmPermissions.label` |
| Updates | `TmAppVersion.compare`; `check()` results gain `failed` and `unreachable`; `update()` |
| Library | `tm-icon` with `TmIconSource`, `mirrorInRtl`; `tm-avatar` `colorKey`; `tmAvatarTone`; the `categorical` token group (0010 §B.8); busy overlay `immediate: true`; `tmKeysText` |
| White label | resx `Distribution_DisplayName`, `Distribution_ShortName`, `Distribution_Copyright`, `Distribution_About`; manifest `name_localized`, `short_name_localized` |
| New 0010 items | §B.7 icons (0002 components), §B.8 categorical tokens, §C.13 icons (grid and menu), §D.11 icons (basics), §E.5 icons (picker), §F.14, §F.15 |

## 4. How to edit

- **Replace, don't append** (`docs/AGENTS.md`). State each decision as it stands, once, in the
  section that owns it. No "previously", "now", "was", "changed to", "per review". Rationale goes
  only in the Decisions table; a reversed decision is rewritten, not annotated.
- Follow `.tmp/round-2/style-guide.md`: plain words, bullets over long paragraphs, examples where
  they help, C# or TypeScript sketches for contracts.
- **Numbering.** Never renumber existing sections of 0011–0035 (other specs cite them). Add a new
  section at the end of its parent (0024 §7.7, §7.8). When a section's topic disappears, reuse the
  section for its replacement (0024 §10.4 becomes the plain JSON rule) or delete it and list every
  citation of it in your log. 0010's new items use the numbers in §3 above.
- **Your files only.** Edit only the files your group owns (§5). When a decision needs a change in
  another group's file that its owner may miss, write it under "Requests for other files" in your
  log, with the file, section and exact change.
- **Specs 0001–0009 are frozen.** Never edit them; amendments go in 0010.
- **Grep your files** for every term in §3's Removed row, `§F.CUT-`, "Tellma" in quoted copy,
  `TmDecimal`, `lossless`, `long: true`, `tenantId?`, `bank-reconciliation`, `aging`, `init`,
  `tellma-client check`, `hide`, `forbidden`, `412`, `busy`, `debounce`, `avatar`, `icon`, `kbd`,
  `ar-SA`, `ar-AE`, `time zone`, `displayName`, `copyright`, `levels`, `totals`, `having`, and
  fix every hit the decisions touch.
- **Log** to `.tmp/round-2/r3/applied-<group>.md`:
  - for each file of 0010–0022 and `ARCHITECTURE.md`: one bullet per change, with section and
    decision number, specific enough for implementers to find it (this feeds the change report);
  - for each file of 0023–0035: a short list of changes per decision;
  - "Requests for other files"; "Open questions" (anything you could not decide).

## 5. Ownership and sources

| Group | Files | Main sources (besides decisions.md and this file) |
|---|---|---|
| `0010-F` | 0010: Context, Goals, Part F, and Part F's entries in Testing, DoD, Decisions, Review flags | design-measures.md (§7, §8.1, §9); g1 items 2 and 3 (old numbers) |
| `0010-AE` | 0010: Parts A–E and every Context, Goals, Testing, DoD, Decisions and Review-flag entry about them; runs after `0010-F` | g2 (all items); design-locale.md §2.2 and §4 "0010 Part D" |
| `0033` | 0033 | design-measures.md (§2, §6, §8.2); g1 items 2–3; g4 item 10; g5 item 13 |
| `0034-35` | 0034, 0035 | design-measures.md §8.3, §8.5; g2 items 3 and 5; g4 items 7, 10; g5 items 13, 15; design-locale.md §2.1 |
| `0023-29` | 0023, 0029 | g2 (all); design-measures.md §8.5 (0029); design-locale.md §4 (0023, 0029); g4 items 7, 8, 10; g5 item 11 |
| `0024` | 0024 | g3, g4, g5 (all); design-locale.md (all, §4 "0024"); g2 items 4, 6 |
| `0025-27` | 0025, 0026, 0027 | g3 item 5; g4 items 6–8; g5 items 12, 13, 15; design-locale.md §3, §4 |
| `0028-32` | 0028, 0030, 0031, 0032 | g2 items 3–5; g4 items 7, 8, 10; g5 items 11, 13, 15; design-locale.md §4 |
| `0011-14` | 0011, 0012, 0013, 0014 | design-measures.md §8.5; design-locale.md §1, §3.2, §4; g3 item 1; g4 item 6; g5 items 11, 15 |
| `0015-22` | 0015–0022, ARCHITECTURE.md | design-measures.md §8.5; g3 items 1, 4 and its ARCHITECTURE notes; g4 items 8, 10; g5 items 11, 13, 15; design-locale.md §4 (0016, 0018, ARCHITECTURE) |
