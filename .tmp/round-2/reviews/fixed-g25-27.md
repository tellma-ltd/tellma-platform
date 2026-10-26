# Fix log — specs 0025, 0026, 0027

## Spec 0025 (`docs/specs/0025-client-state-and-cross-tab-sync.md`)

- K-foundation-32 — §1: "three owners" → four writers: `TmClientStore` (IndexedDB, `tm.boot`), `TmDrafts` (`tm.draft:*`), `TmSession` (`tm.login`, 0024 §8.3), `tm-boot.js` (0024 §4.2).
- K-foundation-32 — §11 last bullet: `tm.login` is "the sign-in loop guard (spec 0024 §4.2)".
- F-4 — §2.1 sketch: `readAll(...partitions: TmPartition[]): Promise<void>`, one readonly transaction over their key ranges. Deviation: returns `void`, not the per-name map (two partitions would share one name space; no spec reads the map); variadic keeps single-partition calls (§3.1 tenant switch) valid.
- F-4 — §2.6 boot step: now a two-item list; item 2 reads P's user partition and, on a tenant path, P's tenant partition in one `readAll` (one transaction), then `TmSession` patches `sub`. Skipped F-4's §2.3 "read by the boot step" cell: §2.6 owns the read (each fact once).
- K-foundation-1 — §2.6 session clean-up: numbered three calls; `clearAll({ keep })` keeps `tm-app`, `tm.boot` and, on the expiry path, every `tm.draft:*` key; "made by" → "run by spec 0024 §8.4, §8.5 and §8.6"; the wrong "Sweeps never touch session storage" reason removed.
- K-foundation-35 — §2.2: the restated `tm-boot.js` delete bullet → "`tm-boot.js` deletes by this layout (spec 0024 §4.2 step 3c), so a change to it is a change to the boot script."
- K-foundation-30 — §3.1 sketch comments: `tenants` = spec 0011 §5.5's `/bff/user` entry shape, camelCase; `distribution` = spec 0011 §5.9's `DistributionInfo`, camelCase. Deviation: cited §5.5's flattened entry (TenantDescriptor plus `isActive`) instead of §3.8's `TenantMembership`, which nests the descriptor and is not the wire shape.
- K-foundation-2 / F-13 — §5.3 `observe`: compares at once; `TmApi` calls it one task after delivering the body (spec 0024 §10.3 step 5).
- K-foundation-2 — Review flag 7: "`TmApi` calls `observe` one task after the body", in line with §5.3.
- K-reports-22 — §10 Key bullet: a `page` hatch remembering apart from its host adds a segment (`/17/reports/42/run`); `markStale` maps the key without it. Dropped the `/17/reports/acme-aging` example and shortened the query sentence to hold the line budget.
- K-foundation-38 — Decision 12: "SignalR Q1" question id removed; the decision is stated in words, alternatives in one short clause.
- K-foundation-38 (same defect as K-shell-entity-38) — §15: dropped the brief ids "(2A)"–"(2F)" after the bold leads.
- Size — §15 "Page context and dump" reflowed and `TmSupport.registerDumpSection` replaced by its owner pointer (spec 0026 §10.1); file 1,098 lines (was 1,099).
- F-3 — §3.2 cold row: skipped. The fix (page data request waits for the cold sync) contradicts design/00-architecture.md §7.1 step 5 ("`client/sync` … and the page's data request start together"), which is binding.

## Spec 0026 (`docs/specs/0026-client-observability-and-support.md`)

- K-foundation-37 — §6.2: rate-policy bullet keeps only "(spec 0016 §8.2): ten open tabs use a third of it."; the 60-a-minute bucket is 0016 §8.2's.
- K-foundation-19 — §8.3 Triggers: "reserved in spec 0027 §4.3" → "spec 0027 Appendix A".
- K-foundation-19 — Context: "spec 0027 §4.3" → "spec 0027 Appendix A" in the shortcut bullet, to match §8.3.
- K-foundation-27 — §8.3 Clipboard: toast copy removed; "Success and failure each toast once (spec 0028 §8.1)." K-shell-entity-19 (0028 takes the long copy) is compatible: 0028 owns the copy either way.
- K-crud-13 — §9.3 `settings` row owner: "spec 0032 §7, §8 with `category: 'profile'`; spec 0028 §10.3 with `category: 'notifications'`".
- K-foundation-17 — §9.8: "fills spec 0027's `help` slot (§7.2)" → "(spec 0027 §7.2)".
- F-21 — §12.2: "five states" → "six"; new row Unavailable: the `/support` import failed, "This isn't available offline. Press {keys} to copy diagnostics." with `tm-kbd` (the keys work from the primary); Close.
- K-foundation-38 — Decisions 1–6: "Observability Q1/Q2" and "Supportability Q1–Q4" ids removed; leads reworded ("What to collect", "Transport and error logging", "Slow-request trace ids", "A screenshot", "Where support data goes", "The UI version"); reflowed.
- Addendum 2 / A2 — §12.3, §12.6: already self-scoped (`CreatedById = me()`, reason `self`); no change.
- K26, K30 — §9.4 `confirm` shape and §3.2 value grammar already match; no change.

## Spec 0027 (`docs/specs/0027-shell-launcher-and-tenant-picker.md`)

- K-shell-entity-38 — §3.2: "as the design draws" removed.
- K-shell-entity-26 — §5.1 Banner row: "(spec 0028 §9.1; spec 0028 §9.3's idle gate times entries and exits)".
- K-shell-entity-38 — §6.2: "(rule 2D)" → "Recency never moves a row, so the layout stays stable."
- K-foundation-18 / K-shell-entity-24 — §6.2: "(`value`, spec 0023 §6.4)" → "spec 0023 §6.2".
- K-shell-entity-22 — §7.4: `me.User.ImageId` → `me.user.imageId`; `me.User`'s name → `me.user`'s, and its fallback `Name` → `name` (same casing defect); `settings.Names` → `settings.names` (spec 0013 §5.6's `Names`).
- K33 / K-shell-entity-4 — §8.5: no visible chip → the shell toasts "Pinned {label}." once (spec 0028 §8.1) instead of announcing; the toast host announces it.
- K-shell-entity-38 — §8.7: "(rule 2D)" removed.
- F-24 — §9.3 band 2: a recent page's icon is its `icon`, else spec 0035 §9.2's icon for its kind; typed rows use the same icon.
- K-shell-entity-6 (applied as K32 allows) — §9.4 candidates table: English label and synonyms read through Transloco's `translate(key, {}, 'en')`, English loading at boot (spec 0024 §7.3). K32 (binding) deletes `TmLocalization.translate`; rulings §2.6 (Q-9) keep English matching, so the finding's "drop English" alternative was not taken either. §3.3, Decision 4 and Review flag 17 stay as written.
- K-foundation-18 / K-shell-entity-24 / K-shell-entity-38 — §9.6: "spec 0023 §6.4" → "§6.2"; "(rule 2C)" removed.
- K3 / K-foundation-9 / K-shell-entity-12 — §10.4: "44 px rows (spec 0010 §B.6)" → "(spec 0010 §C.10)".
- K-foundation-18 / K-shell-entity-25 — §11.1: `dl[tmDescriptionList]` "(spec 0023 §7.3)" → "(spec 0023 §7.1)".
- K18 / K-foundation-3 / K-shell-entity-7 / K-shell-entity-35 — §12.1: the restated action contract replaced by one paragraph: rows 4 and 5 call spec 0024 §7.2's switch, which writes `users/me/locale` (spec 0018 §3.5) and adopts the result (spec 0025 §5.3); the My profile concurrency sentence kept.
- K-shell-entity-35 — §12.2: `DistributionLinks` restatement replaced by "The About dialog reads `links.terms` and `links.privacy` of `DistributionInfo` (spec 0011 §5.9) from `TmLaunchCache.distribution` (spec 0025 §3.1) …".
- K-shell-entity-38 — §14: "(2A)"–"(2F)" removed from the bold leads.
- K-shell-entity-38 — Decision 3: "the design draws" → "the launcher shows"; Decision 18: "(Q1 format, Q2 merging …)" → "(its format, a merge with the assistant)", "prejudges Q2" → "prejudges the merge".
- K-shell-entity-38 — Review flag 5: "not the design's `?`" → "not `?`"; flag 9: "the design's currency and location" → "the tenant's currency and location", "the design's sub-line" → "that sub-line".
- K-shell-entity-21 — §10.1 row 4 keeps "with `lang`": the 0010 fixer added `TmMenuItem.lang`; no change here.
- K12, K31 — §1 (primary members) and §3.2 (Administration 900) already match; no change.
- F-3 — §7.5 cold row: skipped, for the reason logged under spec 0025 (design/00-architecture.md §7.1 step 5).
