# Verify: acme-label (decisions 23 and 25, ruling R-1)

Scope checked: every old name of g5 item 13's rename table (grep over 0010-0035 and
ARCHITECTURE.md: `bank-reconciliation`, `bankReconciliation`, `BankRec`, `bank/bank-page`,
`auto-match`, `match-next`, `accountId`/`unmatched`, `aging` in every form, `AgingReport`,
`AgingAsync`, `AgingArguments`, `AgingReportTools`, `Acme_Report_Aging`, `Acme_Customer_Aging`,
`Report.aging`, `report.-aging`, pin title "Aging"): none is left. Every new name appears as the
table spells it in 0016 §4.5, §11.9; 0018 §1.4, §3.5, §9; 0024 §2.4, §9.1, §14; 0025 §9.1, §15;
0026 §2, §3.2, §9.3, §14; 0027 §2, §4.1, §8.2, §14; 0032 §5.2, §12.4; 0033 §11, §12.1, §15, §16;
0034 §2.1, §2.2, §15; 0035 §2.3, §3.1, §10.2, §18. Every bare "Tellma" in the specs is developer
prose or an identifier; no UI copy, dialog, toast, error, email or notice says "Tellma". The
placeholder is `{displayName}` everywhere; no `{display name}`, `{appName}` or `{productName}` is
left. `TmLocalization.displayName` is the only client holder. The About dialog forces no credit.

## 0024

- [V6-01] (must) 0024 §14 "What Acme adds" — "the hand-mapped `acme-reports/credit-exposure`,
  called through `TmApi.read` with `long`." → replace the bullet with "the coded report's route
  `acme-reports/credit-exposure`, a projected `[ApiAction]` with `Long = true` (spec 0033 §11) that
  the Contracts chunk lists, so `TmApi.read` gets the long watchdog with no `long` option". Why:
  R-28. It also contradicts 0033 §11 (`Idempotent = true, Long = true`) and 0034 §2.2 ("The host
  calls `TmApi.read(route, args, { signal })`"; only a `run` hatch passes `long: true`).
- [V6-02] (should) 0024 §3.1 — "They are fallbacks for three optional server keys in the
  distribution's string packs" → give each key its own fallback, because there is no config
  short name: `Distribution_DisplayName` in the active language, else `displayName`;
  `Distribution_ShortName` falls back to the manifest's `short_name` (a language without the key
  gets no `short_name_localized` entry, §5.1); `Distribution_Copyright` in the active language,
  else `copyright`. Add a fourth bullet: "`Distribution_About`, the About dialog's description,
  with no fallback (0027 §11.1)". This lists every `Distribution_*` key that §2.5's "Branding" row
  points to, and makes 0013 §10.1's citation of §3.1 for `Distribution_About` hold. Why: decision
  25 (config values as fallbacks; `Distribution_About` is the credit line); g5 item 15's edit plan
  ("name the three keys and the order").

## 0025-27

- [V6-03] (should) 0027 §11.1 step 5 — "`Distribution_Copyright` through `TmServerText` when a
  pack holds it, else `distribution.copyright`" → "`Distribution_Copyright` in the active
  language, else `distribution.copyright` (spec 0024 §3.1)". `TmServerText.has` is true when the
  active OR the English pack holds the key (0024 §7.5), so an Arabic user would see the English
  copyright before the config value. The display name, by contrast, uses the active language,
  else config (0024 §3.1, §4.2; 0016 §11.6). Why: one fallback rule for the `Distribution_*` keys
  (decision 25); follows V6-02.

## 0011-14

- [V6-04] (should) 0013 §10.1 "The distribution's brand" — "`Distribution_DisplayName`,
  `Distribution_ShortName` and `Distribution_Copyright`, which override spec 0011 §8's
  `DisplayName` and `Copyright` per language" → "`Distribution_DisplayName` and
  `Distribution_Copyright`, which override spec 0011 §8's `DisplayName` and `Copyright` per
  language; `Distribution_ShortName`, the installed app's short name (spec 0024 §5.1)". 0011 §8
  has no short-name key for `Distribution_ShortName` to override. Why: decision 25; consistency
  with 0011 §8 and 0024 §3.1.
