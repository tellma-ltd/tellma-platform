# Verify: cross-group requests and rulings (V8)

Cluster `requests`. Findings in fix-notes.md §3 format, grouped by the owner group of the TARGET
file. Every request in the ten logs' "Requests for other files" sections and every owner-naming
ruling of fix-notes.md §1 was checked against the current text; the ledger at the end lists each
one. Findings exist only for requests and rulings that are partly met or not met.

## 0010-F

- [V8-01] (should) 0010 §F.15 — "**Requested by:** spec 0033 §8.4, §8.6." and, under Errors,
  "The caller reports it (spec 0033 owns the report error)." → make the header "**Requested by:**
  spec 0033 §8.4, §8.6, §8.11." and the Errors bullet "(spec 0033 §8.11 maps it to `422
  Report.MeasureFailed`)". Why: request from 0033 (the "Requested by" lines cite §8.4, §8.5, §8.6
  and §8.11); 0033 §8.11 is where the mapping lives.

## 0010-AE

- [V8-02] (must) 0010 §B.8 — the section defines only "`categorical`" ("eight hues that tell things
  apart"); no `sequential` group exists anywhere in 0010 → add a `sequential` group to
  `TmSchemeColors` (steps 1–7, each a `TmTokenValue`, light and dark values from 0034 §7.7's
  current ramp "light teal-100 → teal-800; dark teal-900 → teal-200", resolved to seven concrete
  hex values per scheme), emitted as `--sequential-<n>` in both scheme blocks like `categorical`;
  extend "Who reads it" (chart `sequential` palette, spec 0034 §7.7), the emitter test ("24 names"
  grows), Part B DoD "Tokens", the Context citation of 0034 §7.7 (line ~38), and add a Part B
  Decisions row (one ramp per scheme in the scheme shape, because component tokens emit once). Why:
  R-24.

## 0024

- [V8-03] (must) 0024 §14 "What Acme adds" — "the hand-mapped `acme-reports/credit-exposure`,
  called through `TmApi.read` with `long`." → "the coded route `acme-reports/credit-exposure`
  (spec 0033 §11), which the Contracts chunk lists as long and idempotent, so `TmApi.read` passes
  no `long`." Why: R-28; requests from 0033 and 0034-35; contradicts 0034 §2.2 "Long by
  declaration".
- [V8-04] (must) 0024 §14 "Server pieces" — "spec 0016's string decimals (§3.2) and
  `IContractFingerprints` (§3.10)" → "`IRouteContracts` (§3.10)". Why: R-12 (rename everywhere).
- [V8-05] (must) 0024 §8.6 step 2 — "The tab runs spec 0025 §2.6's session clean-up for the expiry
  path (drafts kept), deletes a stale profile cookie" → add: "The boot record keeps `sub` and
  `lastTenantId`; only sign-out clears them (§9.3), so a later launch of `/` reaches §4.2 step 3c
  and returns to the last tenant." Make sure step 4 ("Back from sign-in, the tab boots cold") still
  holds with `sub` kept (§4.2 step 4 calls a launch cold only without a record or on a different
  `sub`). Why: R-16, decision 16; today the clean-up's `boot.patch({ sub: null, lastTenantId: null
  })` (0025 §2.6) runs on this path, contradicting §9.3 "only an expiry returns to the last tenant".
- [V8-06] (must) 0024 §11 "`forbidden` names the permission" — "`{permission}` is
  `TmPermissions.label(resource, action)` (0029 §7), such as "Sales invoices: Save"" → "such as
  "Sales invoices: Create and edit"". Why: request from 0023-29; 0029 §7 gives "'Sales invoices:
  Create and edit'" and 0032 §4.6/§5.2 label the Save action "Create and edit".
- [V8-07] (should) 0024 §3.4 `long` bullet — "the actions declared `Long = true` (spec 0015 §2.3)
  and the Excel operations" → "the actions declared `Long = true` (spec 0015 §2.3), spec 0019's
  Excel operations among them". Why: request from 0015-22; 0016 §8.3 reads one flag ("every action
  declared `Long = true` … among them spec 0019's `export`, …").
- [V8-08] (should) 0024 §3.1 — "`displayName` and `copyright` come from `Tellma:DisplayName` and
  `Tellma:Copyright` … They are fallbacks for three optional server keys" (two values, three keys)
  → say `displayName` backs `Distribution_DisplayName`, `copyright` backs `Distribution_Copyright`,
  and `Distribution_ShortName` falls back to the manifest's own `short_name` (§5.1). Why: note from
  0011-14 (0011 §8 states only the first two pairs).
- [V8-09] (should) 0024 §7.8 step 7 — "Wait for the input-idle gate (spec 0028 §9.3)" → "Wait for
  `TmInputIdle.whenIdle()` (spec 0028 §9.3)". Why: optional request from 0028-32 (0028 §9.3 exports
  the gate under that name).

## 0023-29

- [V8-10] (must) 0029 §6.2 — "`options?: { aggregate?: boolean }`" and "**Aggregate mode** serves
  report previews (spec 0034). It expands navigations the same way, each term a grouping key that
  depends on the id." → delete the option and the bullet (and any test of aggregate mode). No spec
  in 0030–0035 calls `tmSelectPlan` with `aggregate: true` (0030 §5.1 is the only caller, without
  it), and the bullet contradicts 0033 §8.4 ("A navigation groups by its key alone … `max(...)`").
  Why: R-14.
- [V8-11] (should) 0023 §3.1 Rendering — "`--icon-stroke-width` (1.75, the weight of every Lucide
  glyph the library ships)" → "(1.75 by default; a host may set it, as the image edit pane's done
  check does at 2.25, spec 0010 §D.11)". Why: request from 0010-AE; 0010 §D.11 "The edit pane's
  check keeps its heavier 2.25 stroke: its host sets `--icon-stroke-width`."

## 0034-35

- [V8-12] (must) 0034 §7.7 — "| `sequential1`–`sequential7` | light teal-100 → teal-800; dark
  teal-900 → teal-200 |" → "| `sequential1`–`sequential7` | `sequential.1` … `sequential.7` (spec
  0010 §B.8) |", and the lead paragraph says the sequential ramp, like the series, reads the
  semantic group. Why: R-24 (0034 stops listing the values once V8-02 lands).
- [V8-13] (must) 0034 §3.3 and §5.3 — §3.3 "The notice strip … holds one closable info `tm-alert`:
  … or, in `widget` mode, a summary's omitted totals (§5.3)."; §5.3 "`widget` mode has no footer,
  so the notice strip (§3.3) says it." → drop the widget case from the strip; §5.3 reads "In
  `widget` mode a short note goes on the tile's status line (spec 0035 §8.5); a body without a
  status line shows no message, and the full report view explains." Also §14 Testing "the
  `totalsOmitted` footer and its `widget`-mode notice" → "… and the tile's status-line note". Why:
  R-27 (no overlay strip on a tile).
- [V8-14] (must) 0035 §8.5 — "Report widgets, and types that declare `status: true`, show "Updated
  {time}"." (no omitted-totals case) → add a bullet: when the report's result has
  `totalsOmitted`, the line appends a short note, such as "Updated 10:20 · Totals not shown", whose
  tooltip carries 0034 §5.3's sentence; the line keeps its reserved width. Why: R-27, which puts the
  note on the status line; 0034 §5.3 must cite this bullet (V8-13).

## 0025-27

- [V8-15] (must) 0025 §2.6 "The session clean-up, run by spec 0024 §8.4, §8.5 and §8.6, is three
  calls in order: … 3. `boot.patch({ sub: null, lastTenantId: null })`." → step 3 runs on the
  sign-out paths only (0024 §8.4, §8.5); on the expiry path (0024 §8.6) the boot record keeps `sub`
  and `lastTenantId` for `tm-boot.js` step 3c. Also §2.1's `TmBootRecord` comment "user fields,
  cleared when the session ends" → "cleared at sign-out and by `tm-boot.js`'s expiry delete", and
  add a §14 store test (the expiry clean-up leaves both fields). Why: R-16, decision 16; must state
  the same rule as 0024 §8.6 (V8-05).

## 0028-32

- [V8-16] (must) 0032 §2 table — the row reads "| Hide a stock launcher entry; the page stays
  reachable (spec 0024 §2.3) | `hide: ['entry:core.Schedule']` | 1 |"; the baseline's
  `hide: ['settingsTab:invoicing']` example was removed → restore it, e.g. "| Hide a stock launcher
  entry or settings tab (spec 0024 §2.3) | `hide: ['entry:core.Schedule']`,
  `hide: ['settingsTab:invoicing']` | 1 |". Why: R-20; 0024 §2.3 still allows it ("`page:`,
  `settingsTab:` and `appBar:` drop the declaration").

## 0011-14

- [V8-17] (must) 0011 §2.2 — "`public TellmaBuilder AddLanguage(LanguageInfo info);`" and the table
  row "| `Languages`, `AddLanguage` | … and catalogue additions (spec 0013 §7.3). |" →
  `AddLanguage(LanguageInfo info, LanguageFormats formats)`, and the row says an addition carries
  its formats. Why: R-8 (every offered language has formats, so `ILanguageCatalog.Formats` covers
  it).
- [V8-18] (must) 0013 §7.3 — "`tellma.AddLanguage(info)` adds a language the catalogue does not
  describe." → "`tellma.AddLanguage(info, formats)` adds a language the catalogue does not describe,
  with its `LanguageFormats` (its regions, default region and native digits), so `Formats` covers
  every offered language." Update §1.2's "`Languages(codes)` and `AddLanguage(info)`" to match, and
  add one §12 test (an added language's `Formats` and its regions in `IsRegionOffered`). Why: R-8.
- [V8-19] (should) 0013 §10.1 "The `forbidden` problem" — "a `no-grant` refusal of
  `acme.SalesInvoice` × `Save` reads "It needs “Sales invoices: Save”." → "It needs “Sales
  invoices: Create and edit”." Why: the label chain gives the Save action's label (0029 §7, 0032
  §4.6 "It needs “Sales invoices: Create and edit”"); same correction as V8-06.

## 0015-22

- [V8-20] (must) 0016 §1.1, §3.10 (three places) and §14 — "It holds `IContractFingerprints`
  (§3.10)", "`IContractFingerprints` (`Tellma.Core`, framework-free) computes", "public interface
  IContractFingerprints", "Beside the fingerprints, `IContractFingerprints` answers", "`IContractFingerprints`
  and its client-asset contributor" → `IRouteContracts` in all five. Why: R-12.
- [V8-21] (should) 0016 §7.2 — "`detail` is its `_Detail` resource rendered the same way, arguments
  substituted" (no case for `forbidden`) → add: "A `forbidden` problem's `detail` renders `resource`
  and `action` as the permission's labels, resolved through the securables' label chains (spec 0013
  §10.1)." Why: request from 0011-14; without it §7.2 reads as substituting the raw pair, while 0013
  §10.1 renders labels.

## Request ledger

| Requesting group | Target | Request | Status |
|---|---|---|---|
| 0010-F | 0033 §8.4–§8.7, §8.11 | drop "QX3104 included"; keep §8.4/§8.5/§8.6 roles; own `422 Report.MeasureFailed` | met |
| 0010-F | 0012 §3.3, §11.1–§11.4 | `QueryColumn.Precision` from `NumericPrecision`; delete level text | met |
| 0010-F | 0015 §5.2, §7.3 | delete level-path refusal and `QueryLevelNotSelectable` | met |
| 0010-F | 0029 §9, Context | `QueryexDiscoverRequest.Measures`; drop §F.CUT-9 citation | met |
| 0010-F | ARCHITECTURE.md | Queryex: `'uq'` month table, `Exists`, measure evaluator; no grouping sets | met |
| 0010-AE | 0023 §3.1 | stroke "1.75 by default" or name the 2.25 exception | not met (V8-11) |
| 0010-AE | 0023 §3.2 | lint rule `tm/no-platform-sniffing` | met |
| 0010-AE | 0023 Context | `TM_DEVICE_TIME_ZONE` if `tm-relative-time` reads it | not needed (it reads `TmL10n`'s `'auto'` label only) |
| 0010-AE | 0034 §7.7 | categorical values only in 0010 §B.8 | met |
| 0010-AE | 0029 §12.1 | `zoneSuffix` on `zoneLabel()`/`zoneMismatch()`; cells `'column'` | met |
| 0024 | 0011 §1.2, §5.5, §8 | template not `init`; landing rule, `BffLandingFailed`, vectors; config fallbacks | met |
| 0024 | 0013 §1.2, §5.6, §9.2 | `region`/`regions`/`nativeDigits`; `Region`, `Digits`; resolution order | met |
| 0024 | 0014 §3.1 | `PreferredRegion`, `PreferredDigits` | met |
| 0024 | 0015 §2.3, §14.1 | `Long`, `Idempotent`; `ForbiddenException.Reason`/`Path` | met |
| 0024 | 0016 §3.2, §3.10, §7.1, DoD | plain `JSON.parse`; `{ fingerprints, long, idempotent }`; SemVer `Tellma-Build`; forbidden args | met |
| 0024 | 0018 §3.5 | `MeLocaleRequest.TimeZone` | met |
| 0024 | 0021 flag 3 | push spec owns the `ngsw-worker.js` wrapper | met |
| 0024 | 0023 §5.4 | `run(work, { label, immediate })` | met |
| 0024 | 0025 §2.1, §2.3, §9.1, §11, §13 | `zone` device doc; plain JSON; credit-review preference | met |
| 0024 | 0026 §7, §9.1, §12.2 | `TmAppVersion.compare`; plain JSON; `TmLocalization.displayName` | met |
| 0024 | 0027 §6.2, §6.3, §7.1, §7.4, §10.3, §11.1 | `forTenant`; notice on chosen entry; signed-out page; display name; About; sign-out summary | met |
| 0024 | 0028 §8.1, §9.3 | zone toast; exported input-idle gate | met |
| 0024 | 0029 §7, §12.1, §15 | `TmPermissions.label`; `zoneSuffix`; `TmDecimal`/`TmId` | met |
| 0024 | 0030, 0034 | drop `long: true` on projected routes | met |
| 0024 | 0032 §4.2, §7.5, §8 | region/digits fields; Formats card; `tm-auto-time-zone` | met |
| 0024 | 0010-AE §D.4 | zone label modes, `TM_DEVICE_TIME_ZONE`, zone formatters | met |
| 0024 | ARCHITECTURE.md | esproj from the `dotnet new` template; NGSW status | met |
| 0033 | 0034 §3.3 | label-free copy for `pointer` `""` | met (R-21) |
| 0033 | 0034 §5.3, §8 | no Total drill under `top` | met |
| 0033 | 0034 §2.2, 0024 §14 | coded route is projected, `Long = true`; no `long` at call site | 0034 met; 0024 not met (V8-03) |
| 0033 | 0019 §12.3 | report case streams through `IReportEngine.PagesAsync` | met (R-22) |
| 0033 | 0012 §6.4 | reasons `divide-by-zero`, `overflow` | met |
| 0033 | 0010-F §F.14, §F.15 | "Requested by" cites 0033 §8.4–§8.6, §8.11 | partly met (V8-01) |
| 0033 | 0016 §8.3 | reports routes `Long = true` | met |
| 0023-29 | 0030 §3.3, §5.3, §6.1, §7.1, §18 | leading/trailing search; bar rule; header `zoneSuffix` | met |
| 0023-29 | 0027 §9.6 | providers on leading/trailing `search` | met |
| 0023-29 | 0027 §6.1, §7.4 | session avatar `colorKey`; tenantless avatar none | met |
| 0023-29 | 0021, 0028, 0032 §5.4, 0035 | direct `tm-avatar` passes `colorKey` | met (0021/0028 draw none; 0035's Owner is a 0029 cell) |
| 0023-29 | 0031 §6.2 | navigation avatar `colorKey` | met |
| 0023-29 | 0032 §7.4 | `Int64` key: `integer` with `long: true` | met |
| 0023-29 | 0031 editors | `tmNumber` converts `decimal` and `long` | met (through `tm-value-editor`, 0029 §12.2) |
| 0023-29 | 0024 §11 | "Sales invoices: Create and edit"; `label(resource)` alone | partly met (V8-06) |
| 0034-35 | 0024 §14 | coded route listed long and idempotent | not met (V8-03) |
| 0034-35 | 0030 §3.3, §7.1 | state the bar rule; `header(col)` + `zoneSuffix` | met |
| 0034-35 | 0016 §2.4, §8.3 | `dashboards/audience` idempotent, long | met |
| 0025-27 | 0024 §5.6 | rows match `compare` 0 / null | met |
| 0025-27 | 0030 §16.2 | `parentIds` not typed `number[]` | met |
| 0025-27 | 0018 §3.5, §9 | credit-exposure preference keys | met |
| 0028-32 | 0024 §7.8 step 7 | name `TmInputIdle.whenIdle()` (optional) | not met (V8-09) |
| 0028-32 | 0034 §5.1, §11 | instant headers `zoneSuffix`; exports bare; `TmId` | met |
| 0028-32 | 0013 §1.2, §5.6, §6.1, §6.5, §7.2 | sections carry the region and digits facts | met |
| 0028-32 | 0014 §3.1, 0016 §3.4, 0018 §3.1, §3.2 | cited members exist | met |
| 0028-32 | 0023 or 0010 | `tm.digits.<system>` strings (conditional) | no change needed (0032 owns the keys) |
| 0011-14 | 0015 §14.1 and raise sites | `ForbiddenReason` per raise site | met |
| 0011-14 | 0016 §3.2, §3.3/§3.5, §3.4, §7.1 | encoding; no `Precision`; `UserProfileView`; forbidden args | met |
| 0011-14 | 0016 §7.2 | forbidden `detail` renders labels | not met (V8-21) |
| 0011-14 | 0018 §1.2, §3.1, §3.2 | `Languages(["en-GB","ar-SA"])`; `MeSaveRequest`; `RegionNotOffered` | met |
| 0011-14 | 0033 | `ArithmeticFailureException` reasons confirmed | met (informational) |
| 0011-14 | 0024 §3.1 | `Distribution_ShortName` falls back to manifest `short_name` | partly met (V8-08) |
| 0011-14 | ARCHITECTURE.md | regional formats are Core's, not a Locale pack's | met |
| 0015-22 | 0024 §3.4 | "spec 0019's Excel operations among them" | not met (V8-07) |
| 0015-22 | 0024 §10.1, §10.5 | `activate`/`deactivate` not named non-idempotent | met |
| 0015-22 | 0013 §1.1 | `DigitStyle` in 0013's namespace | met |
| 0015-22 | 0011 §5.9 | keep `DistributionInfo.DisplayName` | met |
| fix-notes | 0033 | R-5 `'uq'` forms agree with §F.11 | met |
| fix-notes | 0033, 0034 | R-6 no GUID `min`/`max` claim | met |
| fix-notes | 0013, 0014, 0018 | R-7 digits `varchar(8)` | met |
| fix-notes | 0011, 0013 | R-8 `AddLanguage` takes `LanguageFormats` | not met (V8-17, V8-18) |
| fix-notes | 0015, 0016, 0024 | R-10 `activate`/`deactivate` idempotent | met |
| fix-notes | 0024, 0029 | R-11 ids follow the key type | met |
| fix-notes | 0016, 0024 | R-12 `IRouteContracts` | not met (V8-04, V8-20) |
| fix-notes | 0029 consumers | R-13 `integer` + `long` is `TmDecimal` | met |
| fix-notes | 0029 | R-14 delete unused aggregate mode | not met (V8-10) |
| fix-notes | all | R-15 0024/0025-27 names used exactly | met |
| fix-notes | 0024, 0025 | R-16 expiry keeps `sub`, `lastTenantId` | not met (V8-05, V8-15) |
| fix-notes | 0023, 0024, 0031 | R-18 `immediate` reading | met |
| fix-notes | 0024 | R-19 region field `scope` | met |
| fix-notes | 0032 | R-20 restore `settingsTab:` example | not met (V8-16) |
| fix-notes | 0034 | R-21 label-free copy | met |
| fix-notes | 0019, 0034 | R-22 `PagesAsync` | met |
| fix-notes | 0010-AE, 0034 | R-24 `sequential` group in §B.8 | not met (V8-02, V8-12) |
| fix-notes | 0034, 0035 | R-27 omitted totals on the status line, no strip | not met (V8-13, V8-14) |
| fix-notes | 0024 | R-28 coded route not hand-mapped | not met (V8-03) |
