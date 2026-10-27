# Applied review round 1: group 0011-14

Files: `docs/specs/0011-distribution-host-and-multitenancy.md`, `0012-data-access-layer.md`,
`0013-settings-localization-and-cache.md`, `0014-users-roles-and-permissions.md`. Decision
numbers (dN) are `decisions.md`'s; "design-locale" and "design-measures" are the two design notes.
No `§F.CUT-` marker remains in the four files.

## 0011 — Distribution host and multi-tenancy

- Context, "Other specs": new bullet "spec 0013 §7.3 and §9.2 — the regioned language codes
  `Languages` takes and the culture's region (§1.3, §4.1)" (d24).
- Non-goals: new bullet "The distribution template" — `dotnet new tellma-distribution` scaffolds
  server and client together and belongs to a later distribution-tooling spec; this spec fixes the
  shape it scaffolds (§1.2) (d11).
- §1.2 Client project bullet: "`tellma-client init` scaffolds it" → "The distribution template (a
  non-goal here) scaffolds it with the rest of the distribution" (d11).
- §1.3 composition illustration: `Languages(["en", "ar"])` → `Languages(["en-GB", "ar-SA"])`, plus
  one sentence: a code may carry its language's default region; spec 0013 §7.3 owns the rule (d24).
- §2.2 member table, `Languages`/`AddLanguage` row: "offered languages, each with an optional
  default region (`"ar-SA"`), and catalogue additions (spec 0013 §7.3)" (d24).
- §3.2 last paragraph: the BFF's post-sign-in landing (§5.5) and the SPA's launch redirect both
  read the membership list (§3.8) (d16).
- §3.8: "two endpoints read it" → "three readers use it"; Readers bullet adds the BFF's
  post-sign-in landing (§5.5) (d16).
- §4.1 `RequestContext` sketch: `Culture` comment → "language and region ("ar-SA"), no extensions";
  `CultureInfo` comment → "the Gregorian clone of Culture + "-u-nu-latn": Latin digits always".
  Member table row for the locale fields adds "The culture's region follows spec 0013 §9.2" (d24).
- §5.5 `/bff/login` bullet: the redirect is `returnUrl ?? "/"`, or the user's one live tenant when
  that target is `/` (replaces "the SPA's launch redirect decides where `/` lands") (d16).
- §5.5 new bullet "The post-sign-in landing" (When: `OnTicketReceived`, target exactly `/` or
  absent; What: one `ITenantMembershipDirectory.ListAsync(sub)` then
  `TenantLanding.SingleLiveTenant`, a hit sets `ReturnUri = "/{id}"`; Failure: keeps `/`, logs
  `BffLandingFailed` Warning; One rule, two places: `tmSingleLiveTenant`,
  `launch-landing-vectors.json` in `@tellma/core` `src/launch/`, linked by `Tellma.Core.Tests`),
  and the C# sketch `TenantLanding.SingleLiveTenant(IReadOnlyList<TenantMembership>)` in
  `Tellma.Core.Abstractions.Tenancy` (d16, design-locale §3.1–§3.2).
- §5.9 `DistributionInfo.DisplayName` comment: "one value: the document is anonymous and has no
  language" (d25).
- §8 configuration table: `DisplayName` and `PublicOrigin` split into two rows. `DisplayName`: the
  information document's name and `distribution.displayName`, the fallback for the optional resx
  key `Distribution_DisplayName`. `Copyright`: `distribution.copyright`, the fallback for
  `Distribution_Copyright`; null with no key omits the line (d25).
- §9 log-event table: new row `BffLandingFailed` (§5.5), Warning (d16).
- §10 `Tellma.Core.Tests` row: `TenantLanding.SingleLiveTenant` over every row of
  `launch-landing-vectors.json`. `Tellma.Core.AspNetCore.Tests` Sessions bullet: the landing cases
  (one live → `/1`; two or sandbox only → `/`; `/tenants` and deep links untouched; a directory
  failure keeps `/`) (d16).
- Decision 29 rewritten in place: "The BFF picks a tenant only for a sign-in to `/`, by the SPA's
  rule" (d16, design-locale §4 "0011").
- d22: no change. `Tellma-Build` is defined in spec 0016 §3.10; 0011 §5.1 only stamps the header.

## 0012 — Data access layer

- Goals, Queryex host integration bullet: drops "tree level navigations and" (d1, design-measures
  §8.5).
- §3.3 `QueryColumn` gains `int? Precision` (after `Scale`); the prose defines it ("the precision
  of a `decimal` result, `null` for every other column; spec 0010 §F.15's measure evaluator reads it
  with `Scale`; spec 0016's converter does not write it") (d1, design-measures §8.5).
- §6.4 provider-error table: new row "8134 (divide by zero), 8115 (arithmetic overflow) →
  `ArithmeticFailureException(Reason)`, `Reason` = `divide-by-zero` or `overflow`; final; the report
  engine answers it as `422 Report.MeasureFailed` (spec 0033 §8.11); every other caller surfaces it
  as 500" (editor-notes §2, F5 narrow).
- §6.4 internal exceptions paragraph: `ArithmeticFailureException(Reason)` joins the list and
  carries `StatementOrdinal` ("the eight from `UniqueConstraintViolationException` to
  `ArithmeticFailureException`") (F5).
- §11.1: `Fingerprint` drops "tree levels"; the Tree node bullet drops the `TreeLevels` sentence
  (d1).
- §11.2 amendments list: "`FilterTree.Via` (§F.7) and tree level navigations" → "`FilterTree.Via`
  (§F.7)" (d1).
- §11.3 reader: `Precision` read from `DbColumn.NumericPrecision` beside `NumericScale` (d1).
- §11.4: the level-navigation `Select` refusal sentence deleted (d1).
- §13.1 unit row: drops "a `Select` through `Level{j}` refused at compose time"; adds "8134 and
  8115 mapped to `ArithmeticFailureException` and their reasons" and "`Precision` per `decimal`
  column" (d1, F5).
- §13.3 Executor bullet: a division by zero and a `decimal(38,0)` overflow in a `Rows` statement
  surface as `ArithmeticFailureException` with their reasons; a computed `decimal` column reports
  SQL Server's precision and scale (d1, F5).
- §14 DoD: "the schema provider (tree levels and the calendar map included)" → "(the calendar map
  included)" (d1).
- Decisions: new 36 "SQL's divide-by-zero and overflow errors are a typed exception with a reason"
  (F5).
- Part F citations (§F.9, §F.11) were already renumbered by the script; checked.

## 0013 — Settings, localization and cache

- Goals: the language bullet adds "each language's regions and native digits" and "the tenant's
  default region and digits" (d24).
- Non-goals: "A per-tenant default culture and a pack-level language declaration" → "A pack-level
  language declaration" (the tenant default region now exists) (d24).
- §1.1 `.Localization` namespace list adds `LanguageFormats`, `DigitStyle` (d24).
- §1.2 `languages` section: each entry carries the default region, the offered regions (ordinal
  order) and the native digit system; new JSON example of Acme's Arabic entry (d24, design-locale
  §1.5).
- §1.3 Languages check: a declared region must be one of its language's `Regions` (d24).
- §5.1: `core.Settings` also holds the tenant's default formats (region and digits) (d24).
- §5.2 `core.Settings` columns: `Region varchar(2) NULL`, `Digits varchar(8) NULL` after
  `TimeZone` (d24; see open question 1 on the width).
- §5.3 rules table, the type row: values use spec 0016 §3.2's scalar encodings; a `long` or
  `decimal` setting value is a JSON string, a number accepted on input (d18).
- §5.6 `TenantSettings` gains `string? Region`, `DigitStyle? Digits`; `TenantSettingsForClient`
  gains `Region`, `Digits`; member table row `Region` / `Digits`; the client-view row lists "the
  region and digits" (d24).
- §5.7 cold-load `SELECT` adds `[Region], [Digits]` (d24).
- §6.1 `TenantSettingsPatch` gains `string? Region, DigitStyle? Digits` (typed members, so
  `core.Settings.General` × `Save`). `Details` row: `ForbiddenException` (`NoGrant`, naming
  `core.Settings.General` × `Read`) (d24, d21).
- §6.3 step 3 typed members: the region trimmed, upper-cased and null or `IsRegionOffered`; an
  unknown `Digits` value fails spec 0016's enum binding (d24).
- §6.5: new code `Settings.RegionNotOffered` (d24).
- §7.1: `DigitStyle` enum, `LanguageFormats` record; `ILanguageCatalog` gains
  `Formats(language)` and `IsRegionOffered(region)`; `IsOffered` comment "a bare language code"
  (d24, design-locale §1.3).
- §7.2: new prose on each entry's formats (regions = every .NET 10 `<language>-R` pair plus the
  `zh` aliases, frozen in code; the Chromium `om`/`ti`/`so` gap; `en-SA` absent; native digits
  `ar`/`ur`/`hi`; default regions) and an example (d24).
- §7.3: a declared code may carry the language's default region (`Languages(["en-GB", "ar-SA"])`);
  the first code's language is the distribution default; the language alone is offered and stored
  (d24).
- §9.2: `NegotiationInput` gains `PreferredRegion`; the table's Culture row becomes "Language", and
  a new "Region" row (header's region → `PreferredRegion` → tenant `Region` → `DefaultRegion`); a
  region not offered with the language is skipped; the "regioned `PreferredLanguage`" sentence is
  deleted; new bullets "Latin digits in server text" (the `-u-nu-latn` clone) and "Gregorian"
  (replacing the old `CultureInfo` paragraph); new example (d24, design-locale §1.6).
- §9.3: the initializer reads `PreferredRegion` and the tenant's `Region`; "the four preferences"
  (d24).
- §10.1: the `Entity_Level` bullet deleted (d1). New bullet "The distribution's brand":
  `Distribution_DisplayName`, `Distribution_ShortName`, `Distribution_Copyright` (overriding spec
  0011 §8 per language) and `Distribution_About` (d25). New bullet "The `forbidden` problem":
  `Problem_forbidden_Detail` is an ICU `select` on `reason`, worded as the SPA's messages, with
  `resource` and `action` rendered as the permission's labels, `path` as sent; example (d21).
- §11 configuration: "the language list, its regions and the calendars are code" (d24).
- §12 unit: catalogue invariants (every pair with `predefinedOnly: true` on Ubuntu and Windows; the
  `-u-nu-latn` clone accepts Gregorian and prints Latin digits; `Formats`/`IsRegionOffered`);
  negotiation table (language, region steps, `en-SA` → `en-GB`, `ar-SA-u-nu-arab → ar-SA`); client
  metadata (formats per language); resource audit accepts the four `Distribution_` keys (d24, d25).
  Integration Settings bullet: `Region` and `Digits` round-trip through `save`, the cold load and
  both views (d24).
- §13 DoD: "the languages and their formats"; Docs: ARCHITECTURE gains "regional formats (region
  and digits) being Core's, from CLDR, and never a Locale pack's" (d24).
- Decisions: new 30 "Region is its own axis, beside the language" (d24).
- Review flags: flag 7 rewritten in place as design-locale R3 ("Server text always uses Latin
  digits"); new flag 24 = R2 ("Offered regions are every .NET pair, frozen in code") (d24).

## 0014 — Users, roles and permissions

- §1.3 dependency table: new row `ILanguageCatalog`, `DigitStyle` (spec 0013 §7.1) for the
  language and format preferences (d24).
- §3.1 `User` sketch: `PreferredLanguage` comment "a bare language code ("ar")"; new
  `string? PreferredRegion` and `DigitStyle? PreferredDigits`. Column table: `PreferredRegion
  varchar(2) NULL`, `PreferredDigits varchar(8) NULL`. Preferences bullet: region validated by
  `IsRegionOffered` after trim and upper-case, digits by enum; region and digits pick formats apart
  from the language (d24, design-locale §1.2, §1.7).
- §3.8: `PreferredDigits` listed among the string-typed enum properties (d24).
- §5.1 `RequireAsync` comment and `Require` row: `ForbiddenException` with reason `NoGrant` (d21).
- §5.3: new opening table naming the three reasons (`NoGrant`/`no-grant`,
  `OutsideFilter`/`outside-filter`, `RestrictedPath`/`restricted-path` with `Path`) and "never roles,
  filters or other users"; each bullet names its reason: restricted path → `RestrictedPath`;
  collection on `Denied` → `NoGrant`; save pre-check short count, delete/action grant check and the
  `50403` post-check → `OutsideFilter` (d21).
- §7.1 `UserProfile` gains `PreferredRegion`, `PreferredDigits`. §7.3 result set 3 `SELECT` adds
  both columns. §7.8: negotiation reads `PreferredRegion` too (d24).
- §14 unit: `RequireAsync` on `Denied` raises `ForbiddenException` with the pair and `NoGrant` (d21).
- Decisions: new 29 "A 403 names the refused pair and one of three reasons, never roles, filters or
  other users" (d21).
- Review flag 17: "Typed `PreferredLanguage/Calendar/TimeZone/Region/Digits`" (d24).
- Appendix A `RowSecurity` row: `ForbiddenException` (`OutsideFilter`) (d21).

## Requests for other files

- **0015 (group 0015-22), §14.1 and raise sites:** `ForbiddenException` gains `ForbiddenReason
  Reason` and `string? Path`. Reasons per 0014 §5.3: `RequireAsync` on `Denied` → `NoGrant`; §5.2
  step 4's restricted traversal → `RestrictedPath` with the path; the save pre-check short count,
  the delete/action grant statements (§9.2, §9.3) and the `50403` post-check (§14.2 translation of
  `RowSecurityException`) → `OutsideFilter`. 0015 is otherwise unchanged by F5:
  `ArithmeticFailureException` reaches its generic 500 path (0012 §6.4).
- **0016 (group 0015-22):** §3.4 `UserProfileView` gains `PreferredRegion`, `PreferredDigits` (0014
  §7.1). §3.3/§3.5 the `QueryColumn` converter does not write `Precision` (0012 §3.3). §7.1
  `forbidden` row adds `arguments.reason` and `arguments.path`; §7.2 the `detail` of a `forbidden`
  problem renders `resource` and `action` as the securables' labels (0013 §10.1), so the renderer
  needs the label resolution spec 0032 §12.4's chains define. §3.2: settings entries follow the
  string encoding of `decimal`/`long` (0013 §5.3 now cites §3.2 for it).
- **0018 (group 0015-22):** §1.2 `Languages(["en-GB", "ar-SA"])` (0011 §1.3, 0013 §7.3); §3.1
  `MeSaveRequest` gains `PreferredRegion`, `PreferredDigits`; §3.2 `Users.RegionNotOffered` with
  trim and upper-case preprocessing, and `PreferredLanguage` a bare code (`ar-SA` is
  `Users.LanguageNotOffered`), as design-locale §1.7 lists.
- **0033 (group 0033):** confirmed: `ArithmeticFailureException(Reason)` carries exactly
  `divide-by-zero` (8134) or `overflow` (8115), and also `StatementOrdinal` (0012 §6.4).
- **0024 (group 0024):** no change needed; its citations of 0011 §5.5, §8 and 0013 §1.2, §5.6,
  §7.2, §9.2 and 0014 §3.1 now resolve. Note `Distribution_ShortName` falls back to the manifest's
  own `short_name` (0024 §5.1), not to `Tellma:DisplayName`; 0011 §8 says so only for
  `Distribution_DisplayName`.
- **ARCHITECTURE.md (group 0015-22):** the Locale dimension line: regional formats (region,
  digits) are Core's, from CLDR, never a Locale pack's (0013 §13 Docs now expects it).

## Open questions

1. **Digits column width.** design-locale §1.2 gives `PreferredDigits`/`Digits` as `varchar(6)`.
   0012 §1.3's `EnumStringLengthConvention` sizes every enum column `varchar(max(8, longest
   member))`, so 0013 §5.2 and 0014 §3.1 state `varchar(8)`, as for `Gender`. Keep the convention,
   or add `[MaxLength(6)]` to both properties?
2. **`AddLanguage` and formats.** `ILanguageCatalog.Formats` covers catalogued languages only
   (`KeyNotFoundException` otherwise), and design-locale is silent on a language a distribution adds
   with `AddLanguage(info)`: it has no regions, default region or native digits. Should
   `LanguageInfo`/`AddLanguage` take a `LanguageFormats`, or should an added language get its .NET
   pairs at startup (which breaks "frozen in code")? Nothing was written.
3. **§7.8 reads only `PreferredRegion`.** design-locale §4 "0014" says §7.8 adds both new members;
   the server never reads digits (server text is always Latin, 0013 §9.2), so 0014 §7.8 names the
   region only. The profile (§7.1, §7.3) carries both, for the client's `me`.
4. **Resource UI culture.** The negotiated `CultureInfo` is the `-u-nu-latn` clone and 0013 §9.3
   sets it as `CurrentUICulture` too. Resource fallback (0013 §10.3, `ar-SA → ar → neutral`) should
   be unaffected, but an implementer may prefer to set `CurrentUICulture` to the plain
   `<language>-<region>` culture. Not specified further.

## Fix pass

Findings addressed to 0011-14: V5-01, V5-02, V5-03 (verify-locale), V6-04 (verify-acme-label),
V7-06 (verify-structure), V8-17, V8-18, V8-19 (verify-requests). Rulings naming the group: R-7,
R-8, R-15. V8-17 duplicates V5-01 and V8-18 duplicates V5-02; each pair was applied once.

### 0011 — Distribution host and multi-tenancy

- §2.2 `TellmaBuilder` sketch: `AddLanguage(LanguageInfo info)` →
  `AddLanguage(LanguageInfo info, LanguageFormats formats)` (R-8; V5-01, V8-17).
- §2.2 member table, `Languages`/`AddLanguage` row: "catalogue additions (spec 0013 §7.3)" →
  "catalogue additions with their `LanguageFormats` (spec 0013 §7.3)" (R-8; V5-01, V8-17).
- §5.5 "The post-sign-in landing" lead: the rationale clause "so a single-tenant user sees the
  tenant shell first, with no tenantless skeleton and no shell swap" deleted; the bullet now states
  only the rule (V7-06).
- Decision 29: gains "and no shell swap", so the rationale removed from §5.5 lives only here
  (V7-06).

### 0012 — Data access layer

- No finding addressed; no change in the fix pass.

### 0013 — Settings, localization and cache

- §1.2 last paragraph: the builder surface reads `Languages(codes)` and
  `AddLanguage(info, formats)` (R-8; V5-02, V8-18); paragraph rewrapped.
- §7.1 `ILanguageCatalog.Formats` comment: "any catalogued language" → "any catalogued or added
  language" (R-8; V5-02).
- §7.3: "`tellma.AddLanguage(info)` adds a language the catalogue does not describe." →
  "`tellma.AddLanguage(info, formats)` adds a language the catalogue does not describe, with its
  `LanguageFormats` under the same code (its regions, default region and native digits), so
  `Formats` covers every offered language." Paragraph rewrapped (R-8; V5-02, V8-18).
- §9.2, paragraph before the bullets: the SPA sends "a `-u-nu-*` extension when the user's digits
  need one, which the server ignores (spec 0024 §7.7)", replacing "a `-u-nu-*` extension the
  server ignores", so the text agrees with 0024 §7.7 step 4 (V5-03).
- §9.2 example: a user with `PreferredRegion = AE` gets `ar-AE` from the SPA, "which sends
  `ar-AE`" (was `ar-AE-u-nu-latn`; `ar-AE` already formats with Latin digits) (V5-03, d24).
- §9.2 header paragraph: an over-long line rewrapped (no content change).
- §10.1 "The distribution's brand": `Distribution_DisplayName` and `Distribution_Copyright`
  override spec 0011 §8's `DisplayName` and `Copyright` per language; `Distribution_ShortName` is
  the installed app's short name (spec 0024 §5.1); `Distribution_About` is "the About dialog's
  description" (the name 0024 §3.1 and 0027 §11.1 use; was "credit line") (V6-04, d25).
- §10.1 "The `forbidden` problem" example: "It needs “Sales invoices: Save”." → "It needs
  “Sales invoices: Create and edit”." — the Save action's label (V8-19, d21).
- §12 unit, Catalogue invariants: adds "a language added with `AddLanguage` answers both
  [`Formats` and `IsRegionOffered`] from the formats it was given" (R-8's one test; V5-02, V8-18).

### 0014 — Users, roles and permissions

- §5.3 restricted-path bullet: an over-long line rewrapped (no content change).

### Rulings checked, no change needed

- R-7: `Digits varchar(8)` (0013 §5.2) and `PreferredDigits varchar(8)` (0014 §3.1); no
  `varchar(6)` in the four files.
- R-15: none of the names 0024 and 0025-27 chose (`TmLocalization.displayName`, `TmContracts`,
  `TmUpdateCheck`, `budget-*`, `no-tenant`, `TmErrorView.actions`, `TmAppVersion.serverNewer`,
  `thresholdKey`) is cited in the four files, and no variant of them is.

### Final greps

- No `§F.CUT-` marker and no editor-notes §3 Removed name (`TreeLevels`, `Level{j}`,
  `LevelNotSelectable`, `Entity_Level`, QX5008, `levels`, `CubeTruncation`, `Truncated`,
  `TooManyLevels`, grouping sets, `GROUPING_ID`, level indicator, `treeLevels`) in 0011–0014.
- Part F citations: §F.9 (constant statements), §F.11 (`'uq'`) and §F.15 (measure evaluator) only,
  all matching decision 5.
- No quoted user-facing copy says "Tellma"; `AddLanguage` appears nowhere outside 0011 and 0013.

### Skipped findings

- None. (V8-17 and V8-18 were applied through their duplicates V5-01 and V5-02.)

### Unresolved for other groups

- None. No other spec or ARCHITECTURE.md cites `AddLanguage` or claims that `Formats` covers
  catalogued languages only, so R-8 needs no change outside 0011 and 0013.

## Readability pass

No meaning changes; every name, number, code, citation, test, decision and flag kept.

### 0011 — Distribution host and multi-tenancy

- Non-goals, "The distribution template": split into two sentences.
- §4.1 `RequestContext.CultureInfo` comment: reworded ("Gregorian-forced clone … always Latin digits").
- §5.5 cookie-scheme bullet: the persistent-cookie sentences split; an over-long line rewrapped.
- §5.5 post-sign-in landing, **What.**: names the actor; semicolon split into two sentences.
- §8 `DisplayName` row: the fallback clause made its own sentence.
- §10 Sessions bullet: a broken line rewrapped.
- Decision 29: why-clause split into two sentences.

### 0012 — Data access layer

- §3.3 `Precision` prose: tightened into two sentences.
- §11.1 `Fingerprint` sentence: semicolon split.
- §11.3 reader paragraph: the `Scale`/`Precision` sentence split; `decimal` sources named together.
- §13.3 Executor bullet: "with reasons `divide-by-zero` and `overflow`".
- §14 DoD Behavior bullet: rewrapped.
- Decision 36: the alternative made its own sentence.

### 0013 — Settings, localization and cache

- §1.2 `languages` bullet: two sentences instead of a double colon.
- §1.2 builder surface: the two builder calls and two per-feature calls as a bullet list.
- §5.1 first paragraph: a broken line rewrapped.
- §6.3 step 3 typed members: the region rule spelled out ("trimmed and upper-cased, then either
  null or accepted by `IsRegionOffered`").
- §7.2 Regions bullet: "alias names both ends resolve" → "alias names that both .NET and browsers
  resolve"; the Chromium gap as its own clause.
- §7.3 first paragraph: three bold-led bullets (distribution default, default region per
  language, added languages) plus the unchanged closing sentences.
- §9.2 culture paragraph: split into four short sentences.
- §9.2 example: three bullets.
- §9.3 initializer paragraph: numbered steps 1–5, then the unchanged closing sentences.
- §10.1 "The distribution's brand": one sub-bullet per key group.
- §10.1 "The `forbidden` problem": sub-bullets Wording, Arguments, Example.
- §12 Integration Settings bullet: the `Region`/`Digits` round-trip made its own clause; over-long
  line rewrapped.
- §13 DoD Docs bullet: rewrapped.
- Decision 30: split into three sentences.

### 0014 — Users, roles and permissions

- §3.1 Preferences bullet: one sub-bullet per preference, then the formats and typed-columns
  sentences.
- §3.8 enum-properties sentence: rewrapped as one sentence with "and".
- §5.3 opener: the `ForbiddenException` sentence split.
- §5.3 restricted-path bullet: rewrapped so "step 4" stays on one line.
- §7.8 last paragraph: the four preferences listed by name.
- Decision 29: the why split into two sentences.
