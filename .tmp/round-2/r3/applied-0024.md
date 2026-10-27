# Applied: group 0024 (spec 0024 only)

Baseline `063e313`. Only `docs/specs/0024-client-app-foundation.md` was edited. It grew from 1,575
to about 2,030 lines; the growth is the new design of decisions 16, 19, 21, 22 and 24 (new §7.7,
§7.8, the launch-mark design in §4.2 and §9.3, the 412 table, the forbidden rows). No section was
renumbered; §9.3's heading gained "and automatic entries"; §10.4 is reused as "Plain JSON";
Appendix A is retitled "Build problem codes".

## 0024 changes by decision

- **1–5 (report engine):** no hits (`levels`, `totals`, `having`, `§F.CUT-` absent).
- **6 (icons):** no change needed; 0024 uses registry names only (`TmErrorView.icon`, feature
  `icons`).
- **9 (busy overlay):** §8.4 sign-out runs `TmBusyOverlay.run(…, { label, immediate: true })` in
  the acting tab and in every other tab.
- **10 (search feedback):** §10.5 keystroke-read row is "one per search the field fires (0023
  §6.2)".
- **11 (no `init`):** §2.1 opener lists the files a distribution owns, scaffolded by a distribution
  template; Non-goals bullet names the later distribution-tooling spec and its parity test; §2.6
  `init` row gone; §5.1 "The scaffolded manifest"; §13 `init` row → "Responsive and RTL"; §14 Acme
  bullet drops the reproduction; Decision 18.
- **12 (no `check`):** §2.6 three commands; build step 1 runs the source checks (config, §4.1,
  §5.1, §7.6) before `ng build`, step 6 the output checks (budgets, hashed names); §4.1, §5.1,
  §6, §7.6, §12 say `build` fails with a code; Appendix A "Build problem codes" with a second table
  (`index-html-invalid`, `manifest-invalid`, `brand-file-invalid`, `budget-exceeded`,
  `budget-warning` (warning), `budget-note-missing`, `unhashed-asset`); §3.6 comment "Appendix A's
  config codes"; §13 build-checks row; Decision 19.
- **13 (no client hide of server facts):** §2.2 `TmDeclarationKey` = `page | section | entry |
  settingsTab | appBar`; §2.3 Hide bullet rewritten plus "Server facts are not hidden"; Non-goal for
  the later server switch; Decision 4 extended.
- **14 (NGSW on the record):** §5.4 "Web Push, later" bullet (wrapper script via `importScripts`,
  `tellma` policy admits it); Non-goal; Decision 20; Review flag 6(c) (fork fallback, flips).
- **15 (sign-out waits first):** §8.4 diagram (overlay, wait loop, optional confirm), numbered
  flow, "Waiting for a save to finish…", ≤ 60 s, no Cancel, confirm table (dirty / save still
  running), "a confirmed sign-out never asks again"; Decision 11; Review flag 5(c); §13 three
  sign-out cases.
- **16 (launch redirects):** §1 Vocabulary "Automatic entry"; §4.2 `TmLoginMark`, 3c keeps
  copies, 3d `returnUrl` rule (`/{lastTenantId}` on an expiry at `/`), step 4 and warm rewrite set
  `__tmBoot.auto`, second example; §4.3 one wait for a single-tenant sign-in; §4.4 automatic 404
  → picker silently; §8.3 `signIn` writes the mark; §9.3 rewritten: BFF landing,
  `tmSingleLiveTenant`, `launch-landing-vectors.json`, `TmLaunch` sketch and lifetime table, the
  flows table; §10.3 step 5 `TmLaunch.settle`; §11 two `tenant-not-found` branches and
  `tm.launch.tenant-gone`; §13 vectors and e2e; Decisions 22, 23 (13 trimmed); Review flag 7(b)
  deleted; flags 19 (R7), 20 (R8).
- **17 (`TmUrls`):** §9.4 `TmTenantUrls`, `forTenant`, "One tenant rule", "Outside a tenant"
  (`tm.urls.no-tenant`); §10.1 `tenantId` default and "No tenant" bullet (production rejects kind
  `server`, code `no-tenant`); §13; Decision 24.
- **18 (decimals and longs as strings):** Goals; §2.1 lint `restrict-plus-operands`; §10.1
  `TmJson` loses `TmDecimal`, branded `TmDecimal`, `TmId`, `TmDecimals` helpers; `TmUrls` builders
  and `TmRouteMatch.id` take `TmId`; upload comment (long `size` is a `TmDecimal`); §10.4 reused as
  "Plain JSON" (wire table, outbound, arithmetic, display, storage); §12 lint gate; §13 row;
  Decision 6 rewritten; Review flag 2 rewritten (strings vs lossless parser).
- **19 (time zones):** §1 dynamic imports add the zone dialog; Vocabulary "Device zone"; §2.2
  provides `TM_DEVICE_TIME_ZONE`; §7.1 `deviceTimeZone`; new §7.8 (label table, re-read,
  `TmZoneDoc`, `TmDeviceZone`, the seven-step check, the dialog copy, the option, the automatic
  update, Farah's example, telemetry); §13 rows and `device-zone.spec`, `zone-labels.spec`;
  Decision 26; Review flags 8(d), 16 (R4), 17 (R5), 18 (R6).
- **20 (long and idempotent routes):** §3.1 `TmContracts { fingerprints, long, idempotent }`;
  §3.4 rewritten; §10.1 `long?` comment and `read` refusal (development throws, production never
  retries); §10.3 step 3; §13 unit cases; Decision 8.
- **21 (forbidden reasons):** §2.4 gate renders reason `no-grant` with the declared pair; §10.6
  row names `arguments.reason`; §11 three rows, `{permission}` via `TmPermissions.label`, raw pair
  in details, no roles/filters/users; 400 dialog uses the table's copy for listed kinds; §13
  `TmErrorPresenter` row; Decision 10.
- **22 (412 and update failure):** §5.5 `TmUpdateCheck` (`failed`, `unreachable`), `checking`,
  `update()`, detection table; §5.6 precedence table via `TmAppVersion.compare`, 3 s worker cap,
  same build → `server`/`contract-mismatch`, rollback tie-break, telemetry `source`; §4.4 chunk
  failure mapping; §11 Reload flow ("Getting the update…", "Couldn't download the update…",
  "Reload anyway"; `actions` became a `Signal`, `'reload-anyway'` added); §13; Decision 21.
- **23 (credit control):** §2.4 grammar `credit-review/:customerId`, example `credit-review`; §9.1
  `/17/credit-review`; §14 Acme: credit-exposure coded report, `tmPage('credit-review')` with
  `acme.creditReview.threshold`, hand-mapped `acme-reports/credit-exposure`.
- **24 (regional formats):** §2.2 `TmLanguageDefinition` loses `locale`, `tmEnglish()`,
  `tmArabic()`; §3.1 languages comment; §7.1 `region`, `digits`, `locale` row; §7.2 region or
  digits change applies as step 2; new §7.7 (vocabulary, resolution, locale string, browser check,
  Acme table, live switching, where edited, server text); §10.2 `ar-SA-u-nu-latn`; §13 rows,
  `regional-formats.spec`, cross-engine pair test with the gap list; §14 Acme languages;
  Decision 12 reworded, Decision 25; Review flags 8(b), 15 (R1), 21 (R9).
- **25 (white labelling):** §1 Vocabulary "Display name"; §2.5 branding hatch; §3.1 the three
  `Distribution_*` keys as overrides of the config values and "the localized display name"
  (`TmLocalization.displayName`, new in §7.1); §4.1 `<title>` is the fallback; §4.2 `TM_BOOT`
  carries `Distribution_DisplayName`, `tm-boot.js` sets `document.title`, boot `h1`
  "{displayName} couldn't start"; §4.4 title `<page title> · {displayName}`; §5.1
  `name_localized`/`short_name_localized` and the 12-character check on each; §6 `/signed-out`
  title; §11 every "Tellma" in copy → `{displayName}`; Decisions 15, 27; Review flag 9.

## Requests for other files

- **0011 (group 0011-14):** §1.2 Client bullet: "`tellma-client init` scaffolds it" → the
  distribution template, a later distribution-tooling spec (decision 11). §5.5 `/bff/login`: the
  landing through `TenantLanding.SingleLiveTenant` and `BffLandingFailed`; 0024 §9.3 cites "spec
  0011 §5.5" for it, and the C# tests link `launch-landing-vectors.json` from `@tellma/core`
  `src/launch/`. §8 config table: `DisplayName`/`Copyright` are fallbacks for
  `Distribution_DisplayName`/`_ShortName`/`_Copyright` (0024 §3.1 cites "spec 0011 §8").
- **0013:** §1.2 `TmLanguageMetadata` must carry `region`, `regions`, `nativeDigits` (0024 §3.1,
  §7.7 cite §1.2 and §7.2); §5.6 `Region`, `Digits`; §9.2 resolution order matching 0024 §7.7.
- **0014:** §3.1 `PreferredRegion`, `PreferredDigits` (0024 §7.7 cites §3.1).
- **0015:** §2.3 `Long` and `Idempotent` on `[EntityAction]`/`[ApiAction]` (0024 §3.4 cites §2.3);
  §14.1 `ForbiddenException.Reason`/`Path`.
- **0016:** §3.2 line ~428 "parses every `/api/web` body with a lossless parser (spec 0024 §10.4)"
  → "with plain `JSON.parse` (spec 0024 §10.4)"; §3.10 the contributor writes
  `{ fingerprints, long, idempotent }` (`idempotent` = POST routes of §2.2's retried class: stack
  reads plus `Idempotent = true` actions) and the `Tellma-Build` bullet states SemVer precedence
  and the unordered case (0024 §5.6); DoD line ~2076 "the SPA's lossless parser" → "the SPA's
  plain JSON"; §7.1 `forbidden` row adds `arguments.reason`, `arguments.path`.
- **0018:** §3.5 `MeLocaleRequest.TimeZone` (0024 §7.8 writes `users/me/locale { timeZone }`).
- **0021:** Review flag 3: the push spec owns the wrapper script that imports `ngsw-worker.js`
  (spec 0024 §5.4) (decision 14).
- **0023:** §5.4 `run(work, { label, immediate })` is what 0024 §8.4 calls.
- **0025:** §2.1/§2.3 device-partition document `zone` (`TmZoneDoc`, defined in 0024 §7.8, cites
  "0025 §2.3"), surviving sign-out and expiry, 20-user cap; §11 line ~1023 "written and read with
  spec 0024 §10.4's lossless JSON, so `TmDecimal` values round-trip" → plain JSON; §13 test row
  drops the `TmDecimal` round trip; §9.1 preference example → `acme.creditReview.threshold`.
- **0026:** §7 `TmAppVersion.compare(other): -1 | 0 | 1 | null` (0024 §5.6 depends on it);
  §9.1 line ~592 "in lossless JSON (spec 0024 §10.4)" → plain JSON; §12.2 and every display-name
  use cite `TmLocalization.displayName` (spec 0024 §3.1, §7.1).
- **0027:** §6.2 Connect and §7.4 "Open {live name}" build with `TmUrls.forTenant(id).home()`
  (0024 §9.4); §6.2 notice only for a chosen entry (0024 §9.3, §11); §6.3 the signed-out page's
  title comes from `tm-boot.js`'s `document.title`, and Sign in is `signIn({ returnUrl: '/' })`
  (mark `auto: true`, 0024 §8.3); §7.1 and §11.1 use `TmLocalization.displayName` and the
  localized copyright; §11.1 the credit line is `Distribution_About` only (no forced "built on
  Tellma"); §11.1 item 4 server-differs alert only when the server is newer; §10.3 sign-out:
  "overlay, wait for saves, confirmation when needed, broadcast, failure toast, redirect".
- **0028:** §8.1 outcome "Times now show in {zone} ({offset})." with Change → My profile (0024
  §7.8 cites §8.1); §9.3 exports the input-idle gate for 0024 §7.8 step 7.
- **0029:** §7 `TmPermissions.label(resource, action): Signal<string>` (0024 §11); §12.1
  `TmFormats.zoneSuffix` (0024 §7.8 cites §12.1 for the header label); §15 `decimal` and `long`
  as `TmDecimal` (branded string), generic ids as `TmId` (0024 §10.1).
- **0030, 0034:** drop `long: true` on projected routes; the Contracts chunk marks them (0024
  §3.4). 0034 §2.2's hand-mapped routes keep it.
- **0032:** §4.2 `tm-region-field`, `tm-digits-field`; §7.5 Formats card; §8
  `tm-auto-time-zone` under the Time zone field (0024 §7.7, §7.8 cite these sections).
- **0010-AE:** §D.4 `zoneLabel` modes, `TM_DEVICE_TIME_ZONE`, `tmFormatZoneOffset`,
  `tmFormatZoneName` (0024 §7.1, §7.8 cite §D.4).
- **ARCHITECTURE.md (group 0015-22):** the Client esproj is "scaffolded by the `dotnet new
  tellma-distribution` template", not `tellma-client init`; optionally note NGSW's maintenance
  status and the fork fallback (0024 Decision 20, flag 6(c)).

## Open questions

- **Spec size.** 0024 is now about 2,030 lines. Everything added is design the decisions asked
  for, but §7.8 (about 100 lines) could move to its own small spec if Ahmad prefers.
- **Names I chose** (not in editor-notes §3): build codes `budget-warning` and
  `budget-note-missing`; `TmApi`'s production rejection `server` / `no-tenant`;
  `TmUpdateCheck`; `TmContracts`; `TmLocalization.displayName` (one holder for the localized
  display name, so 0026/0027 can cite it); `TmErrorView.actions` became a `Signal` so "Reload
  anyway" can appear after a second failure.
- **Judgment calls:** a chunk that fails to load shows `client-outdated` on `ready` or `failed`,
  else `offline` (§4.4); `update()` reloads on `ready` or `disabled`, and every other result
  (including `none`) shows "Couldn't download the update"; the 400 dialog uses the error table's
  copy for kinds the table lists, so the `forbidden` rows reach writes; with both dirty drafts and
  a running save, the sign-out confirm joins both messages under the dirty title.

## Fix pass

Only `docs/specs/0024-client-app-foundation.md` was edited (0023–0035 file: short list). No section
renumbered; no Decisions or Review-flag number changed.

- **§2.1** `eslint.config.mjs` rules add `tm/no-decimal-operators` (V4-11).
- **§3.1** new `TmLanguageMetadata` sketch below `TmClientMetadata` (`code`, `nativeName`, `dir`,
  `region`, `regions`, `nativeDigits`); the `languages` comment points at it (V5-09).
- **§3.1** brand keys: four optional keys, each with its own fallback (`Distribution_DisplayName`
  → the localized display name bullet; `Distribution_ShortName` → the manifest's `short_name`;
  `Distribution_Copyright` → `copyright`; `Distribution_About`, no fallback, 0027 §11.1)
  (V6-02, V8-08).
- **§3.4** `long` bullet: "the actions declared `Long = true`, spec 0019's Excel operations among
  them" (V8-07, V4-09).
- **§4.2** `TmLoginMark.sub` comment: "the expired record's sub (3d), or the user the tab ran as
  (§8.3)" (V3-03).
- **§5.1** localized names: a language without a key gets no entry (V6-02).
- **§7.7** "Where they live" points at `TmLanguageMetadata` (§3.1) instead of restating it (V5-09);
  "Where they are edited" states the region lists per surface (My profile: the active language's;
  Users page and Formats card: every offered region) via the fields' `scope` (V5-06, R-19).
- **§7.8** check step 7 waits for `TmInputIdle.whenIdle()` (V5-07, V8-09).
- **§8.3** the mark's `sub` is the user the tab ran as, kept by the expiry path, so another user
  signing in from the session-ended dialog is an automatic entry (V3-03).
- **§8.4** step 1 passes `label` as a signal starting at "Signing out…" (V3-02).
- **§8.6** step 2: the expiry path keeps the drafts and the boot record's `sub` and
  `lastTenantId`; only sign-out clears them (§8.4, §8.5), so a tab closed at the dialog and a later
  launch at `/` return to the last tenant. Step 4: the entry is cold because the clean-up swept
  the launch documents (V8-05, V3-01, R-16).
- **§9.3** first bullet drops the sentence that repeated Decision 22's why (V7-02).
- **§10.1** `TmWriteOptions` spelled out with `tenantId` and `long` comments matching
  `TmReadOptions` (V4-10).
- **§10.4** Arithmetic: TypeScript refuses `-`, `*`, `/`; `tm/no-decimal-operators` refuses `+`,
  `+=`, `<`, `<=`, `>`, `>=` on a `TmDecimal`; `restrict-plus-operands` refuses string + number;
  comparisons use `TmDecimals.compare` (V4-11).
- **§11** `{permission}` example "Sales invoices: Create and edit" (V8-06).
- **§12** Lint gate names `tm/no-decimal-operators`; pins "no text arithmetic or comparison on a
  `TmDecimal`" (V4-11).
- **§13** Plain JSON row pins `tm/no-decimal-operators`; Automatic-entries e2e adds "an in-app
  expiry whose tab closes at the dialog, then a launch at `/` → the last tenant" (V4-11, R-16).
- **§14** Server pieces: `IRouteContracts` (V8-04, V3-05, V4-07, R-12). What Acme adds: the
  hand-mapped bullet is gone; the `acmeFeature` bullet says the report's route is a projected
  `[ApiAction]` with `Long = true` (0033 §11), listed by the Contracts chunk, so no call passes
  `long` (V6-01, V8-03, V3-04, V4-08, R-28).
- **Review flag 15** reworded: "No format switch in the session menu: region and digits are edited
  on My profile, the Users page and the Settings Formats card" (V5-08).

Grep after the pass: no §3 Removed name, no `§F.CUT-`, no `IContractFingerprints`, no
hand-mapped `long` on the coded report; "lossless" survives only as the rejected alternative in
Decision 6 and Review flag 2.

### Skipped findings

- **R-10** (activate/deactivate idempotent): no change. §3.4 already defines `idempotent` as the
  stack reads plus every action declared `Idempotent = true`, which 0016 §2.2 says includes
  `activate` and `deactivate`; 0024 names neither.
- **R-11** (generic id lists by key type): no change. 0024 types no `EntitiesResult.Ids` or
  conflict ids; `TmId` (§10.1) already covers generic ids. 0029 owns the result types.
- **R-18** (closing dialogs use `immediate`): no change. 0024's only overlay caller is sign-out,
  which passes `immediate: true` in every tab, as 0023 §5.4 says.
- **R-15**: names already used as accepted.
- Duplicates folded into one edit each: V8-03/V3-04/V4-08 into V6-01; V3-05/V4-07 into V8-04;
  V3-01 into V8-05; V8-08 into V6-02; V8-09 into V5-07; V4-09 into V8-07.

### Unresolved for other groups

- **0025 §2.6 (group 0025-27):** step 3 still runs `boot.patch({ sub: null, lastTenantId: null })`
  on every path. It must run on sign-out only (0024 §8.4, §8.5); the expiry path (0024 §8.6) keeps
  both. Already V3-06; 0024 §8.6 now states the rule and depends on it.
- **0025 §2.1 (group 0025-27):** `TmBootRecord` comment "cleared when the session ends" → "cleared
  at sign-out, and by `tm-boot.js` after an expiry once it has copied them (spec 0024 §4.2)".
  Already V3-07.
- **0016 §1, §3.10 (group 0015-22):** `IContractFingerprints` is still the name at lines ~116,
  ~792, ~806 and in the §3.10 bullets; 0024 §14 now cites `IRouteContracts` (R-12).

## Readability pass

Only `docs/specs/0024-client-app-foundation.md` was edited. No meaning changed; no section,
decision or flag renumbered.

- **Context:** the 0015/0016 bullet split into one bullet per spec.
- **Non-goals:** scaffolding bullet says who owns the template first; "later" → "a later spec".
- **§2.1:** opener split into two sentences.
- **§2.3:** "Server facts are not hidden" says who decides (the server); semicolon split.
- **§2.4:** `securable` bullet: "in place" moved beside the view; "it never redirects" its own
  sentence.
- **§2.6:** build steps 1 and 6 name "the source checks" and "the output checks", the terms
  Appendix A and Decision 19 use.
- **§3.1:** "stays the runtime source" → "is the runtime source".
- **§3.4:** `long` bullet: "on its own" → "with no `long` option from the caller".
- **§4.2:** `document.title` its own bullet; step 4's `auto` rule as two stated cases; the
  `TM_BOOT` copy sentence split.
- **§4.4:** Failure bullet says who shows the view.
- **§5.1:** the scaffolded manifest members as a list, with a lead-in for the build's bullets.
- **§5.6:** the unordered row puts every outcome in the Code column; the rollback bullet as a
  lead-in plus two outcome bullets.
- **§6:** Hashed names: the three exempt scripts in their own sentence.
- **§7.7:** step 5 "once per base" → "once per `<language>-<region>`", fallback facts as
  sub-bullets; Live switching says who computes `locale`; "Where they are edited" as a list per
  surface.
- **§7.8:** "never label" → "never get a label"; check step 7 as four sub-steps; "Any other
  failure" → "A failure other than `Users.TimeZoneUnknown`"; telemetry as a list naming each
  field's values.
- **§8.1:** Lifetime parenthetical → "because".
- **§8.3:** the mark's `auto`, the mark's `sub` and the step-up suffix as bullets.
- **§8.4:** step 3 as two cases; step 4's acting-tab order as three sub-steps.
- **§8.6:** step 2's last sentence reworded; step 4 line wrap fixed.
- **§9.3:** "Both replaces mark the entry" → a **Marking** bullet naming `TmLaunchRedirect`; the
  sign-out sentence split.
- **§11:** `{permission}` and `{resource}` as separate bullets; `{field}` sentence split;
  `tenant-not-found` routing as two cases; the 400 dialog's parts as a list.
- **§13:** Playwright Automatic entries, sign-out cases, Language/formats/errors and Time zones as
  sub-bullets.
- **§14:** Server pieces as a list.
- **Appendix A:** opener says the config check is one of the source checks; the second table's
  "When" cells point at their sections instead of restating the rules of §4.1, §5.1, §7.6 and §6.
