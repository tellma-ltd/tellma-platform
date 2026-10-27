# Verify: shell cluster (decisions 11-17, R-16, R-20)

Findings in fix-notes.md §3 format. Ids V3-nn.

Checked and found consistent (no finding): no `tellma-client init` and no `check` command in any
spec or ARCHITECTURE.md; the template is a non-goal in 0024 and 0011 (Non-goals and §1.2) and is
named in ARCHITECTURE.md (repo layout, Distribution repo layout, Onboarding); 0024 Appendix A is
"Build problem codes" and every build code cited in 0024, 0027, 0029, 0030, 0032, 0034 and 0035
exists there; `TmDeclarationKey` has only client kinds and no spec hides `entity:`, `widget:` or
`report:`; NGSW Decisions row 20, Review flag 6(c), 0024 §5.4 and 0021 Review flag 3; the
sign-out flow in 0024 §8.4, 0025 §6.2 and 0027 §10.3; the BFF landing in 0011 §5.5, §3.8, §10 and
Decision 29 against 0024 §9.3; `TmLoginMark`, `__tmBoot.auto` and `TmLaunch` in 0024 §4.2, §9.3,
§10.3, §11; the silent picker in 0024 §4.4, §11 and 0027 §6.2, §7.5, §13; the signed-out page's
Sign in (0024 §8.3, 0027 §6.3); `TmUrls` in 0024 §9.4, §10.1 and its callers in 0023, 0027
(Connect, "Open {live name}"), 0028, 0030, 0031, 0032, 0034 and 0035.

## 0024

- [V3-01] (must) 0024 §8.6 step 2 — "The tab runs spec 0025 §2.6's session clean-up for the
  expiry path (drafts kept), deletes a stale profile cookie" → replace "(drafts kept)" with
  "(drafts, and the boot record's `sub` and `lastTenantId`, kept)" and add one sentence: "So a
  later launch at `/`, after Sign in or after the tab is closed, returns to the last tenant through
  `tm-boot.js` step 3c (§4.2); only sign-out clears them (§8.4, §8.5)." Why: R-16 / decision 16;
  0024 and 0025 must state the same rule (see V3-06).
- [V3-02] (should) 0024 §8.4 step 1 — "`TmBusyOverlay.run(…, { label: 'Signing out…', immediate:
  true })`" while step 2 says "the label reads 'Waiting for a save to finish…'" → pass a
  `Signal<string>` label (0023 §5.4 accepts `string | Signal<string>`): "`run(…, { label, immediate:
  true })`, where `label` is a signal that starts at 'Signing out…'". A plain string cannot change
  in step 2 or return in step 4. Why: decision 15 / 0023 §5.4's contract.
- [V3-03] (should) 0024 §8.3 — "`signIn()` writes the sign-in mark (§4.2) … The mark's `auto` is
  true only for `returnUrl: '/'`" with §4.2's `sub` comment "the expired record's sub, or the
  signed-in user's" → add to §8.3: "Its `sub` is the tab's session user, which the expiry path
  keeps (§8.6), so another user signing in from the session-ended dialog counts as automatic
  (Review flag 20)." After an in-app expiry the profile cookie is deleted, so "the signed-in
  user's" sub is undefined there, and design-locale §3.5's "yes when another user signs in" would
  not hold for that path. Why: decision 16 / design-locale.md §3.3, §3.5.
- [V3-04] (must; R-28, outside this cluster) 0024 §14 Acme bullet — "the hand-mapped
  `acme-reports/credit-exposure`, called through `TmApi.read` with `long`" → "the coded report's
  projected route `acme-reports/credit-exposure` (`[ApiAction]` with `Long = true`), which the
  Contracts chunk lists, so its call passes no `long`". Why: R-28.
- [V3-05] (must; R-12, outside this cluster) 0024 §14 Server pieces — "spec 0016's string
  decimals (§3.2) and `IContractFingerprints` (§3.10)" → "`IRouteContracts` (§3.10)". Why: R-12.

## 0025-27

- [V3-06] (must) 0025 §2.6, the session clean-up — "run by spec 0024 §8.4, §8.5 and §8.6, is three
  calls in order: … 3. `boot.patch({ sub: null, lastTenantId: null })`." → step 3 becomes "On
  sign-out only (spec 0024 §8.4, §8.5): `boot.patch({ sub: null, lastTenantId: null })`. The
  expiry path (spec 0024 §8.6) keeps both, so `tm-boot.js` step 3c can return the next launch to
  the last tenant." Today the in-app expiry clears them before the user signs in, so a tab closed
  at the session-ended dialog and a later launch at `/` land on the picker. Why: R-16 / decision 16.
- [V3-07] (must) 0025 §2.1 `TmBootRecord` — "`sub: string | null; lastTenantId: number | null;
  // user fields, cleared when the session ends`" → "// user fields: cleared at sign-out, and by
  `tm-boot.js` after an expiry once it has copied them (spec 0024 §4.2)". Why: R-16; the comment
  contradicts the expiry rule.
- [V3-08] (should) 0025 §14, the Store row — "each guard row; each sweep leaves exactly the right
  keys; …" → add "the expiry path's clean-up keeps `sub` and `lastTenantId`, and sign-out's clears
  them". Why: R-16 needs a pinning test.
- [V3-09] (must) 0026 Appendix A.2 — "| spec 0024 | `tm.launch`, `tm.boot`, `tm.config`, `tm.sw`,
  `tm.api` (other names), `tm.session`, `tm.l10n` |" → add `tm.urls` after `tm.api` (other
  names). 0024 §9.4 and §14 emit `tm.urls.no-tenant`, and 0026 §3.2 says "a new area is added
  there". Why: decision 17 / editor-notes §2 (`tm.urls.no-tenant`).

## 0028-32

- [V3-10] (must) 0032 §2 table — the row "Hide a stock launcher entry; the page stays reachable
  (spec 0024 §2.3) | `hide: ['entry:core.Schedule']` | 1" dropped the `settingsTab:` example → add
  a row: "| Drop an earlier feature's settings-tab editor; the category's generic tab returns (spec
  0024 §2.3) | `hide: ['settingsTab:invoicing']` | 1 |". 0024 §2.2's `TmDeclarationKey` and §2.3
  still allow `settingsTab:`, so the example stays. Why: R-20.
