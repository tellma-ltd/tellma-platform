# Review round 1: rulings for the verify and fix pass (binding)

Read `decisions.md` and `editor-notes.md` first; this file adds to them. The ten editors' logs are
`applied-<group>.md` in this folder; each ends with "Requests for other files" and "Open
questions". The rulings below settle those open questions. Owners are the groups of
editor-notes.md §5.

## 1. Rulings on the editors' open questions

| # | Topic | Ruling | Files (owner) |
|---|---|---|---|
| R-1 | Identity server pages and emails say "Tellma" (0010-AE Q1) | No edit. It is a question for Ahmad. | none |
| R-2 | Multi-select row check (0010-AE Q2) | Keep it through `tm-icon`; no third exception. | none |
| R-3 | `Mod+Alt+=` (0010-AE Q3) | Keep it as a second binding. | none |
| R-4 | `tm/no-platform-sniffing` scope (0010-AE Q4) | Keep: `navigator.platform` and `navigator.userAgentData`. Recording `navigator.userAgent` in a support bundle (0026) is not sniffing. | none |
| R-5 | `'uq'` form chosen by all operands (0010-F Q1) | Accepted. Any spec that describes the three forms must agree with 0010 §F.11. | 0033 |
| R-6 | `min`/`max` over GUIDs (0010-F Q4) | No case: `Guid` is not Ordered, so it is QX3201. No spec may claim GUID `min`/`max` support. | 0033, 0034 |
| R-7 | Digits column width (0011-14 Q1) | `varchar(8)`, per 0012 §1.3's enum convention. Nowhere `varchar(6)`. | 0013, 0014, 0018 |
| R-8 | A language added with `AddLanguage` has no formats (0011-14 Q2) | `AddLanguage` takes the language's `LanguageFormats` too (its regions, default region and native digits), so `ILanguageCatalog.Formats` covers every offered language. One sentence where `AddLanguage` is defined, one test. | 0011-14 |
| R-10 | `activate`/`deactivate` retry class (0015-22) | Accepted: `Idempotent = true`, in 0016's retried class. 0015, 0016 and 0024 must agree. | 0015-22, 0024 |
| R-11 | Generic id lists by key type (0015-22) | Accepted: `EntitiesResult.Ids` and conflict ids follow the stack's key type (number for `int`, string for `long`). Client types must agree (`TmId` where generic). | 0024, 0029 |
| R-12 | `IContractFingerprints` now carries more than fingerprints (0015-22) | Rename it `IRouteContracts` everywhere (0016 ×5, 0024 ×1). | 0015-22, 0024 |
| R-13 | `long?: true` on `TmPropertyMetadata`/`TmValueDescriptor` (0023-29) | Accepted (question for Ahmad). Every spec that switches on data types must treat `integer` with `long` as a `TmDecimal`. | 0029 consumers |
| R-14 | 0029 §6.2 aggregate mode disagrees with 0033 §8.4 (0023-29) | Grep 0030–0035 for `aggregate: true` and `tmSelectPlan`. If nothing uses the aggregate mode, delete it from 0029 §6.2 (and its tests); otherwise align it with 0033 §8.4 (navigation labels as `max(...)`). | 0023-29 |
| R-15 | Names 0024 chose (`TmLocalization.displayName`, `TmContracts`, `TmUpdateCheck`, `budget-warning`, `budget-note-missing`, the `no-tenant` rejection, `TmErrorView.actions` as a `Signal`) and 0025-27 chose (`TmAppVersion.serverNewer`, `thresholdKey`) | Accepted. Every other spec uses them exactly. | all |
| R-16 | In-app expiry clears `lastTenantId` before the user signs in (0025-27 Q1) | Fix. Decision 16: an expired session always returns to the last tenant, whether the user presses Sign in or closes the tab and launches `/` later. The in-app expiry path (0024 §8.6) and 0025 §2.6's clean-up must leave what `tm-boot.js` step 3c needs (`sub` and `lastTenantId`, per design-locale.md §3) in the boot record; only sign-out clears them. Both specs must state the same rule. | 0024, 0025-27 |
| R-18 | "Closing action dialogs use `immediate`" (0028-32 Q1) | Accepted reading: a dialog that closes when its button is pressed (a confirmation, the conflict dialog, sign-out) passes `immediate: true`; an action dialog that stays open during `run` shows its button `pending` and the scrim follows at 200 ms. 0023 §5.4, 0024 and 0031 must agree. | 0023-29, 0024, 0028-32 |
| R-19 | Region field `scope: 'language' \| 'offered'` (0028-32 Q3) | Accepted. 0024 §7.7 and any other text saying the region list is always the active language's regions must agree. | 0024 |
| R-20 | `settingsTab:` example removed from 0032 §2 (0028-32 Q4) | Restore it if 0024 §2.3 still allows `settingsTab:` (decision 13 covers only `entity:`, `widget:` and `report:`). | 0028-32 |
| R-21 | `pointer` is `""` when SQL raised the error (0033 Q1) | Accepted. 0034 needs label-free copy for that case. | 0034-35 |
| R-22 | `IReportEngine.PagesAsync` (0033 Q2) | Accepted. 0019 §12.3 and 0034 exports must agree. | 0015-22, 0034-35 |
| R-23 | 512 MB load-test ceiling (0033 Q3) | Accepted. | none |
| R-24 | 0034 §7.7's sequential ramp has light and dark values (0034-35 Q1) | 0010 §B.8 gains a `sequential` group (steps 1–7, light and dark values as 0034 §7.7 lists them today), emitted the same way as `categorical`, with a Decisions row. 0034 §7.7 reads it and stops listing the values. | 0010-AE, 0034-35 |
| R-25 | `Numeric` arguments held as `number` until 15 digits (0034-35 Q2) | Accepted (question for Ahmad). | none |
| R-26 | Measure label resolved on the client (0034-35 Q3) | Accepted. | none |
| R-27 | Omitted-totals message in `widget` mode (0034-35 Q4) | No overlay strip on a tile. If the tile has a status line (0035), a short note goes there; otherwise widget mode shows no message (the full report view explains). | 0034-35 |
| R-28 | `long` on coded report routes (0033, 0034-35 Q5) | A coded report's `route` is a projected `[ApiAction]` with `Long = true` (0033 §11, 0016 §2.4), so the Contracts chunk lists it and no call site passes `long`. Fix 0024 §14's "hand-mapped … with `long`". Only a `run` hatch that calls a hand-mapped endpoint passes `long: true`. | 0024 |

## 2. Requests between groups

Every "Requests for other files" item in the ten logs stands unless a ruling above or a decision
says otherwise. Some were already met by the owner (the editors ran in sequence and some read
earlier logs); the requests auditor checks each against the current text.

## 3. Finding format (verifiers)

Write findings to `verify-<key>.md` in this folder, grouped under one `## <owner group>` heading
per group of editor-notes.md §5 (`0010-F`, `0010-AE`, `0024`, `0033`, `0023-29`, `0034-35`,
`0025-27`, `0028-32`, `0011-14`, `0015-22`). One bullet per finding:

```
- [V3-07] (must) 0025 §2.6 — quote of the current text, at most 25 words → the exact change:
  what to replace with what, or what to add where. Why: decision 16 / R-16 / request from <group>.
```

`must` = contradicts a decision, a ruling, a shared name or another spec, or leaves a stale term;
`should` = unclear, duplicated or misplaced text. Quote the current text so the fixer can find it.
Only report what you confirmed in the current file. No finding for things that are right.
