# Verify: locale cluster (decisions 19 and 24, R-7, R-8, R-19)

Findings in fix-notes.md §3 format. Ids V5-nn.

Checked and found consistent (no finding): every design-locale.md §4 entry for 0010 §D.4 (sketch,
modes, golden arrays, example, Part D Testing, DoD, Decision 12), 0011 (§1.3, §2.2 example and row,
§4.1 `Culture`/`CultureInfo`), 0013 (§1.1, §1.2 JSON, §1.3, §5.1, §5.2, §5.6, §5.7, §6.1, §6.3,
§6.5, §7.1, §7.2, §7.3, §9.2 rows and digits bullet, §9.3, §12, Decision 30, flags 7 = R3 and
24 = R2), 0014 (§3.1, §3.8, §7.1, §7.3, §7.8), 0016 (§3.4 `UserProfileView`, §6.1, §13.3), 0018
(§1.2, §3.1 thirteen members and the three records, §3.2 rows and preprocessing, §3.5, tests,
Decision 14), 0023 §4.5, 0024 (§1, §2.2, §3.1 comment, §7.1, §7.2, §7.7, §7.8, §10.2, §13, §14,
Decisions 25–26, flags 8(b), 8(d), 15–21 = R1, R4–R9), 0025 (§2.1, §2.3, §2.4, §2.6, §5.3, §9.5,
§14, Decision 14, Appendix A), 0026 (§3.2, §9.1, §9.7, §10.1), 0027 (§10.1, §12.1), 0028 (§8.1 row,
§9.3 `TmInputIdle.whenIdle`), 0029 (§5.2, §12.1 `zoneSuffix`, §12.2), 0030 (§7.1, §13.2, §14),
0031 §6.8, 0032 (§4.1, §4.2 with R-19's `scope`, §7.5, §7.7, §8, §12.1, §12.2, §13, flag 15), 0034
(report grid suffix, "Updated" label) and ARCHITECTURE.md (Locale row without "ArAe", regional
formats Core's). R-7: `varchar(8)` in 0013 §5.2 and 0014 §3.1, no `varchar(6)` anywhere. The
resolution order agrees in 0013 §9.2, 0016 §6.1 and 0024 §7.7. `Languages(["en-GB", "ar-SA"])` in
0011, 0013 and 0018; no `ArAe`/`ar-AE` leftover (the remaining `ar-AE` hits are user-region examples).
No history narration in this cluster's added text.

## 0011-14

- [V5-01] (must) 0011 §2.2 — "`public TellmaBuilder AddLanguage(LanguageInfo info);`" and the
  member-table row "and catalogue additions (spec 0013 §7.3)" → change the signature to
  `public TellmaBuilder AddLanguage(LanguageInfo info, LanguageFormats formats);` and the row to
  "and catalogue additions, each with its `LanguageFormats` (spec 0013 §7.3)". Why: R-8.
- [V5-02] (must) 0013 §7.3 — "`tellma.AddLanguage(info)` adds a language the catalogue does not
  describe." → "`tellma.AddLanguage(info, formats)` adds a language the catalogue does not
  describe, with its `LanguageFormats` (regions, default region, native digits), so `Formats`
  covers every offered language." Also: §1.2 "`Languages(codes)` and `AddLanguage(info)`" →
  "`AddLanguage(info, formats)`"; §7.1 `Formats` comment "any catalogued language;
  KeyNotFoundException for an unknown code" → "any catalogued or added language;
  KeyNotFoundException for an unknown code"; §12 Catalogue invariants: add "a language added with
  `AddLanguage` answers `Formats` and `IsRegionOffered` from the formats it was given". Why: R-8
  (one sentence where `AddLanguage` is defined, one test).
- [V5-03] (must) 0013 §9.2 example — "a user with `PreferredRegion = AE` gets `ar-AE` from the
  SPA, which sends `ar-AE-u-nu-latn`" → "which sends `ar-AE`". `ar-AE` already formats with Latin
  digits, so the client adds no extension (0024 §7.7 step 4: "The extension is added only when it
  changes the region's default digits"; its example row `ar | — | AE | — | ar-AE`). Why: decision
  24 / design-locale §1.4; 0024 §7.7 is right.

## 0015-22

- [V5-04] (must) 0021 §4.5 Load statement — "`[u].[PreferredLanguage], [u].[PreferredCalendar],
  [u].[PreferredTimeZone]`" → add `, [u].[PreferredRegion]`, so step 2's negotiator receives
  `NegotiationInput.PreferredRegion` (0013 §9.2) and a recipient's notification email formats in
  their own region, not the tenant's. Why: decision 24; 0013 §9.3 reads "the four preferences".
- [V5-05] (should) 0020 §8, "The job scope" bullet — "resolves the locale fields from the run-as
  user's `PreferredLanguage`, `PreferredCalendar` and `PreferredTimeZone`, else the tenant defaults"
  → "from the run-as user's `PreferredLanguage`, `PreferredCalendar`, `PreferredTimeZone` and
  `PreferredRegion`, else the tenant defaults". Why: decision 24; 0013 §9.3 and 0014 §7.8 read the
  region too.

## 0024

- [V5-06] (should) 0024 §7.7 "Where they are edited" — "My profile and the Users page
  (`tm-region-field`, `tm-digits-field`, spec 0032 §4.2), and the Settings General tab's Formats
  card (spec 0032 §7.5)." → add: "My profile lists the regions offered with the active language;
  the Users page and the Formats card list every region offered with any offered language (the
  fields' `scope`, spec 0032 §4.1, §4.2)." 0032 §4.2 cites "spec 0024 §7.7" for the `offered`
  list, which §7.7 does not define today. Why: R-19.
- [V5-07] (should) 0024 §7.8 check step 7 — "Wait for the input-idle gate (spec 0028 §9.3) and for
  no open modal" → "Wait for `TmInputIdle.whenIdle()` (spec 0028 §9.3) and for no open modal".
  0028 §9.3 exports the gate under that name for this prompt. Why: design-locale §4 "0028" / shared
  clock.
- [V5-08] (should) 0024 Review flag 15 — "**Region and digits only on My profile** (§7.7)" while
  §7.7 edits them on My profile, the Users page and the Formats card → "**No format switch in the
  session menu**: region and digits are edited on My profile, the Users page and the Settings
  Formats card (§7.7)". Why: R1's call is "not in the session menu"; the flag as worded contradicts
  §7.7.
- [V5-09] (should) 0024 §3.1 — "`languages: readonly TmLanguageMetadata[]; // 0013 §1.2, regions
  and digits included (§7.7)`", but no spec declares `TmLanguageMetadata`, and 0032 §12.1 reads its
  `region`, `regions` and `nativeDigits` → add design-locale §1.5's sketch below `TmClientMetadata`:
  `interface TmLanguageMetadata { readonly code: string; readonly nativeName: string; readonly dir:
  'ltr' | 'rtl'; readonly region: string; readonly regions: readonly string[]; readonly
  nativeDigits: string | null }`, with 0013 §1.2 as the source of the values. Why: decision 24 /
  design-locale §1.5; the client type these members belong to.
