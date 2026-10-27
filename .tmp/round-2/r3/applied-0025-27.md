# Applied: group 0025-27 (specs 0025, 0026, 0027)

Baseline `063e313`. Only `docs/specs/0025-client-state-and-cross-tab-sync.md`,
`0026-client-observability-and-support.md` and `0027-shell-launcher-and-tenant-picker.md` were
edited. No section was renumbered or deleted. Sizes: 0025 1,271 → 1,287 lines; 0026 1,213 →
1,234; 0027 1,288 → 1,314. 0027's working copy had CRLF endings after editing; it was normalized
to LF to match HEAD and its siblings.

Decisions with no hit in these files (checked by grep): 1–5 (`levels`, `totals`, `having`,
`§F.CUT-`, `Truncated` absent; 0026's `truncated` is the dump's own member), 6 (icons are registry
names already), 7 (`tmKeysMatch`, `tm-kbd`, `tmFormatKeys` already used as 0023 defines them), 11
(`init` absent), 12 (`tellma-client check` absent), 13 (only `hide: ['entry:…']`, still valid), 14,
20 (`client/sync` already declares `Idempotent = true`; no `long: true`), 21 (no `forbidden` copy
here).

## 0025 — Client state and cross-tab sync

- **16 (launch redirects):** §2.2 last bullet: `tm-boot.js` deletes only the expired user's range
  and copies `sub` and `lastTenantId` before nulling them, so the sign-in returns to the last
  tenant. §3.2 cold `tenant-not-found`: the picker shows its notice unless the platform chose the
  tenant (0024 §9.3). §11: `tm.login` is "the sign-in mark".
- **15 (sign-out waits):** §6.2 `drafts.query` comment: "sign-out's wait and confirmation".
- **18 (decimals as strings):** §11 drafts stored with `JSON.stringify`/`JSON.parse`, decimals and
  longs already strings (0024 §10.4); §14 drops the `TmDecimal` round-trip case. Generic ids take
  `TmId` (editor-notes §2): §3.1 `TmCachedEntitySet.byId: ReadonlyMap<TmId, T>`; §11
  `TmDrafts.key(…, id: TmId | 'new')`; §12 `TmTreeExpansion` ids and stored `{ ids: TmId[] }`.
- **19, 24 (time zones, regional formats; design-locale §4 "0025"):** Context lists 0024 §7.8;
  §2.1 device partition "survives sign-out and expiry; holds only 'zone'"; §2.3 new `zone` row
  (device, `TmDeviceZone`, by `patch`) and bullet (`TmZoneDoc`, 20-user cap, no sweep touches it);
  §2.4 no idle rule for the device partition; §2.6 every sweep deletes user partitions only; §5.3
  `TmAdoptedDocs.locale` cites 0024 §7.2, §7.8 and the `locale` adoption writes
  `.preferredTimeZone`; §9.5 `users/me/locale` sets the time zone too (prompt and automatic update),
  region and digits are profile fields adopted as `me`; §14 Store row: `zone` survives every sweep
  and the expiry delete, 20-user cap; Decision 14 (device partition holds `zone`); Appendix A "Not
  preferences" adds time zone, region, digits and the automatic option.
- **23 (credit control):** §9.1 example → `acme.creditReview.threshold` (`number`, default 90,
  `thresholdKey`); §15 Acme line.

## 0026 — Client observability and support

- **18:** §9.1 cells "as plain JSON (spec 0024 §10.4), so a decimal is its string";
  `TmVisibleData.rows[].id: TmId | null`; §9.3 `details` state `id: TmId | null`.
- **19, 24 (design-locale §4 "0026"):** §9.1 an instant cell is ISO 8601 in the display zone's
  offset (`2026-10-20T10:42:00+03:00`); §9.7 the AG-UI context adds locale and both zones; §10.1 dump
  `locale` adds `region`, `digits`, `deviceTimeZone`, `zoneAuto`; §3.2 "Never in telemetry" adds
  time-zone ids and offsets (design-locale §2.6).
- **22 (412 and update failure):** §7 `TmAppVersion.compare(build): -1 | 0 | 1 | null` with an
  "Order" bullet and a four-row vector table (SemVer precedence, build metadata ignored, same
  `X.Y.Z[-pre]` other commit = `null`); `serverDiffers` replaced by `serverNewer`; "Where it shows"
  reworded; §13 Measurements row adds `compare` vectors and `serverNewer`; Decision 6 adds "ordered
  by SemVer precedence".
- **23:** §2 hatches (`acme.credit-exposure.run`; action `acme.credit-review.suggest-limits`,
  `Acme_CreditReview_SuggestLimits`, `suggestLimits()`; context `/:t/credit-review`,
  `{ threshold, overLimit }`); §3.2 name example; §9.3 action-id example; §14 Acme bullet.
- **25 (white labelling):** §12 opener "from inside the app"; §12.2 Sent state copies
  "{displayName} support request 1042, tenant 17"; the warning defines `{displayName}` as
  `TmLocalization.displayName` (0024 §3.1).

## 0027 — Shell, launcher and tenant picker

- **8 (avatar colours):** §6.1 tenantless avatar has no `colorKey` (no user record outside a
  tenant); §7.4 session-button avatar `colorKey` = `core.User:{me.user.id}` (0023 §4.2); §10.1 the
  header avatar uses the same key; §13 `TmSessionMenuHarness` checks it.
- **9, 15 (sign-out):** §10.3 rewritten: overlay at once (`immediate`), wait for running saves,
  confirmation only when still at risk, broadcast, failure toast, redirect.
- **10 (search feedback):** §9.6 Timing: providers run on `search`'s leading edge, then 50 ms after
  the last key of a burst (0023 §6.2).
- **16 (launch redirects):** Context adds 0024 §9.3; §6.2 new bullet "Only for a tenant the user
  chose" (automatic entry → no notice); §6.3 Sign in's `'/'` lets the BFF land a single-tenant
  user; §7.5 `tenant-not-found` row: silently on an automatic entry, else with the notice; §13
  picker harness: notice after a chosen entry, none after an automatic one.
- **17 (`TmUrls`):** Context cites `TmUrls.forTenant`; §6.2 Connect is
  `[href]="urls.forTenant(id).home()"`, preloading the same href; §7.4 "Open {live name}" links to
  `TmUrls.forTenant(liveTenantId).home()`; §13 picker harness checks Connect's href.
- **19, 24 (design-locale §4 "0027"):** §10.1 "No format rows" (region and digits on My profile,
  0024 §7.7, 0032 §4.2); §12.1 the zone prompt and automatic update write `users/me/locale
  { timeZone }` (0024 §7.8).
- **22:** §11.1 item 4 "Server newer" uses `TmAppVersion.serverNewer()`; Reload is 0024 §11's
  `TmUpdates.update()` flow (was `TmUpdates.reload()`); §13 e2e: About with a newer server (alert,
  blocked download, Reload anyway) and an older one (no alert).
- **23:** §2 `acmeFeature` (`tmPage('credit-review')`, `./credit/credit-review-page`,
  `Acme_CreditReview`, `creditExposureReport`); §4.1 id comment and shortcut example
  (`acme.credit-review.approve-next`, `Acme_CreditReview_ApproveNext`, `approveNext()`); §8.2 pin
  JSON (`reports/acme-credit-exposure?p.AsOf=…`, "Credit exposure"); §14 Acme bullet.
- **25 (white labelling):** §1 Vocabulary `{displayName}` (`TmLocalization.displayName`); every
  "{display name}" → `{displayName}` (§6.1 wordmark `alt`, §6.2 title, §6.3, §7.1, §11.1 term);
  §6.3 the signed-out page's name is the boot display name `tm-boot.js` set as `document.title`
  (was `index.html`'s `<title>`); §11.1 item 2 the description is `Distribution_About` or nothing
  (Core's `Tellma_About` and its "built on Tellma" copy removed); "Tellma platform" term →
  "Platform"; item 5 copyright is `Distribution_Copyright`, else `distribution.copyright`; §13 e2e
  About without `Distribution_About`; Decision 19.

## Requests for other files

- **0024 (group 0024):** §5.6 and §11 depend on 0026 §7's `compare(build)` as defined here: `1`
  = the server build is newer than the UI, `-1` older, `0` identical, `null` same `X.Y.Z[-pre]`
  with another commit. Please make sure §5.6's "same build" row means `compare` = `0` and the
  "unordered" row `null`.
- **0030 (group 0028-32):** §16.2 `parentIds` come from `TmTreeExpansion.expanded(page)()`, now a
  `ReadonlySet<TmId>` (0025 §12): a bigint-keyed tree sends strings, which the server accepts per
  decision 18. No change needed unless 0030 types `parentIds` as `number[]`.
- **0018 (group 0015-22):** §3.5's preference-key example and §9 testing bullet still say
  `report.acme-aging.params`, `Report.aging`, `report.-aging` (decision 23's table:
  `report.acme-credit-exposure.params`, `Report.exposure`, `report.-exposure`).

## Open questions

- **In-app expiry clears `lastTenantId` early.** 0025 §2.6's session clean-up (step 3,
  `boot.patch({ sub: null, lastTenantId: null })`) runs on 0024 §8.6's expiry path before the user
  presses Sign in. That tab signs in back to its own path, so it is fine; but if the user closes
  the tab instead, the next launch at `/` has no `sub` or `lastTenantId`, so `tm-boot.js` step 3c
  does not run and the user lands on the picker (or the BFF's pick) rather than the last tenant.
  Decision 16 says an expired session always returns to the last tenant. Fix options: the expiry
  path's clean-up keeps `sub` and `lastTenantId` and leaves them to `tm-boot.js` 3c (0025 §2.6 and
  0024 §8.6 together), or accept the edge case. Not changed here because the rule lives in both
  specs.
- **`ClientOutdated` (0026 §6.3)** is still "`ClientVersion` differs from `DistributionVersion`".
  It is a server-side telemetry dimension, not a 412 decision, so it was left alone; it could use
  the same SemVer order (true only when the client is older) if Ahmad wants the two to agree.
- **Names chosen here** (not in editor-notes §3): `TmAppVersion.serverNewer` (replaces
  `serverDiffers`); the 0025 preference const `thresholdKey`.

## Fix pass

Files 0010–0022 and `ARCHITECTURE.md`: no changes (not owned by this group).

### 0025 — Client state and cross-tab sync

- §2.1 `TmBootRecord` comment: user fields are "cleared at sign-out, and by tm-boot.js after an
  expiry (§2.2)" (V3-07, V8-15; R-16).
- §2.1 "Boot-record writers": adds "Sign-out's clean-up nulls both user fields (§2.6)" (R-16).
- §2.6 the session clean-up, step 3: `boot.patch({ sub: null, lastTenantId: null })` runs on
  sign-out only (0024 §8.4, §8.5); the expiry path (0024 §8.6) keeps both, so a launch at `/` after
  the tab is closed reaches `tm-boot.js` step 3c and returns to the last tenant. Opener "three
  calls" → "these calls" (V3-06, V8-15; R-16, decision 16). Agrees with 0024 §8.6 step 2 as it
  stands.
- §14 Store row: adds "the expiry path's clean-up keeps the boot record's `sub` and
  `lastTenantId`, and sign-out's clears them" (V3-08, V8-15).

### 0026 — Client observability and support

- Appendix A.2: spec 0024's areas add `tm.urls` (for `tm.urls.no-tenant`, 0024 §9.4) (V3-09;
  decision 17).

### 0027 — Shell, launcher and tenant picker

- §4.2 step 4: the editable-target guard applies only when Option on an Apple platform, held
  without ⌘ or Ctrl, typed a character other than the binding's own key; any other non-Latin
  `event.key` still runs (`ن` from `Mod+K`, `ظ` from `Mod+/` on an Arabic layout), matching 0023
  §3.2 (V2-07; decision 7).
- §13 `TmShortcuts` unit: adds "`Mod+K` and `Mod+/` in a field on an Arabic layout run" (V2-07).
- §11.1 item 5: copyright is `Distribution_Copyright` in the active language, else
  `distribution.copyright` (0024 §3.1); no longer "through `TmServerText` when a pack holds it",
  which fell back to English first (V6-03; decision 25).

R-15 names checked in all three files (`TmAppVersion.serverNewer`, `thresholdKey`,
`TmUpdates.update()`/`check()`/`version`); no stale variant (`serverDiffers`, `TmUpdates.reload()`
as the About Reload, `IContractFingerprints`). Grep for editor-notes §3 Removed names and
`§F.CUT-`: no hits (the two "levels" hits in 0027 are navigation levels). Line endings LF.

### Skipped findings

- None. V3-06 and V8-15 are the same change (applied once).

### Unresolved for other groups

- **0024 (group 0024), §3.1, `Distribution_Copyright` bullet (should):** say the language rule
  explicitly, as the display-name bullet does: "`Distribution_Copyright`, the About dialog's
  copyright line, in the active language, else `copyright`". 0027 §11.1 item 5 now cites §3.1 for
  "in the active language".

## Readability pass

No meaning changes; no section, decision or flag renumbered; line endings LF.

- 0025 §2.2, last bullet: split the `tm-boot.js` expiry sentence in two.
- 0025 §2.6, session clean-up: opener "is these calls" → "makes these calls"; step 3's expiry
  sentence split and reworded (no "still").
- 0025 §3.2, cold `client-outdated` bullet: the two codes as sub-bullets; cold `tenant-not-found` /
  `tenant-unavailable` bullet says which goes where.
- 0025 §9.5, "Locale is not a preference": paragraph → opener plus Writers, Adoption, Region and
  digits bullets.
- 0026 §7, Order bullet: "decides a `412` from it" → "uses it to judge a `412`".
- 0026 §9.1, Cells bullet: one rule per sub-bullet, plus a labelled example.
- 0026 §9.7, Context bullet: the `RunAgentInput.context` entry first, then what its value holds.
- 0027 §4.2 step 4: the Option rule and the non-Latin rule as two bold-led sub-bullets.
- 0027 §6.2, "Only for a tenant the user chose": semicolon split.
- 0027 §6.3: the BFF landing sentence reworded; the `{displayName}` source moved to its own bullet.
- 0027 §7.5, cold `client-outdated` row: each code paired with its button.
- 0027 §10.3: the sign-out sequence as a numbered list.
- 0027 §11.1 items 2 and 5: semicolon split; item 5 reflowed.
