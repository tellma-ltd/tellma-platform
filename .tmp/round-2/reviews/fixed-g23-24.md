# Fix log — specs 0023 and 0024

## 0023 (`docs/specs/0023-component-library-additions.md`)

- K-foundation-40 — §1 table, §2.1 keyboard row, §2.2 Sizes, §4 heading, §4.3 heading: entry point `chips` → `chip` (ruling 2.2). §10 keys `chips.remove`, `chips.more` → `chip.remove`, `chip.more` to match the component name, as the other keys do (no other spec cites them).
- K-foundation-24 — §4.3 `TmChip`: added `activate` output (bound without `href`, the label renders a button); `focus()` now label button, else remove button, else link; §4.3 opener says "label, link, button or removable value"; §2.1 chip row: label and remove buttons are roving stops, Enter or Space on a label button emits `activate`; §4 opener: activatable chips take input. Took the owner-side fix, not the 0030 alternative.
- K-foundation-10 — Decision 4: `tm-multi-select` renders joined text and the multi-picker composes `tm-chip` in its own scrolling row (spec 0010 §B.4, §E.2).
- K-foundation-19 / K-shell-entity-33 — §4.1 Overlay counters: "spec 0028 §6.1" → "spec 0028 §5.2".
- K-foundation-19 — §5.1 Focus: `focus()` bound to `tm.toasts.focus` (spec 0027 Appendix A). Same pointer fix in Context: "spec 0027 §4.3, §5.1" → "spec 0027 §5.1, Appendix A".
- K-foundation-20 — §8.5 Virtualization: `tmComputeAxisWindow` cited as `@tellma/core-ui/grid-engine`'s, the grid's own windowing (no spec 0004 pointer).
- K-foundation-23 — §3.2 `TmKeys` comment: 'Plus', 'Minus', 'Space' spell '+', '-', ' '; example 'Mod+Plus'.
- K-foundation-28 — §5.1 example: added `duration: TM_TOAST_FEEDBACK_MS`.
- K-foundation-34 — §5.2 Geometry: "A message fits one line at 390 px, about 45 characters (spec 0028 owns the copy)." → "The copy and its length cap are spec 0028 §9.1's."
- K-foundation-36 — Vocabulary: Resolved size is `ɵtmControlSize`'s answer (spec 0010 §B.6); §4.5 Text: thresholds cited to spec 0010 §D.4, no longer restated.
- K-foundation-39 — §9 Values: "come from the design canvases" removed; Decision 18: "toasts as designed" → "toasts at the top inline-end".
- K-crud-21 — §7.1 Card: "spec 0031 §6.9" → "spec 0031 §6.8".
- K-reports-32 — §4.5: `TmL10n.formatTime` pointer "spec 0035 §8.5" → "spec 0034 §3.6".
- K22, K23, K24, K21 (0023 side), F-27 (0023 §2.1) — verified already consistent; no change.

## 0024 (`docs/specs/0024-client-app-foundation.md`)

- K-foundation-30 — §2.2 sketch: added `interface TmAppDeclarations` (entities map, pages, reports, widgets, sections, entries, settingsTabs, appBar, features, home); member-table row shortened to the token's role.
- K-foundation-34 — §2.2 `provideTellma` row: the overflow pill clause now cites spec 0028 §8.1's overflow action. Kept `maxVisible: 3` in the call (ruling 2.3 puts the call here and 0028 §8.1 cites 0024 §2.2 for it).
- K31 / K-foundation-11 / K-shell-entity-11 / F-15 — §2.3 example: `{ order: 10 }` moves Administration (order 900, spec 0027 §3.2) to the front.
- K-crud-8 — §2.3 example: "patches the Centers declaration (spec 0032 §6)".
- K-foundation-6 / K43 — §2.4 `path` row: first segment may be an entity segment only when the same feature declares that entity (§9.2); Appendix A `reserved-path` matches (routing word, `new`, or another feature's entity segment; rule §9.1, §9.2).
- F-4 — §4.3 step 2: the store's boot step reads the user partition and, on a tenant path, the tenant partition, as one store read (0025 §2.6). Worded mechanism-neutral so it holds whichever form 0025 picks and keeps 00-architecture's "one store read".
- K-foundation-31 — §4.2: sentence after the boot-page table names Core's `Boot_Title`, `Boot_Unsupported`, `Boot_Cookies`, `Boot_SignInFailed`, `Boot_LoadFailed`, `Boot_TryAgain`; `TM_BOOT` comment shortened so the fact is stated once.
- K-foundation-33 / K-foundation-34 — §5.5: `VERSION_READY` row cites 0028 §9.5 only (copy removed) and emits `tm.sw.update-ready { fromVersion }`; `unrecoverable` (emits `tm.sw.unrecoverable`) and `VERSION_INSTALLATION_FAILED` (emits `tm.sw.install-failed`) split into two rows, matching §14 Telemetry.
- K32 / K-foundation-12 / F-15 — §7.1 sketch: deleted `load(code)` and `translate(key, code)`; §7.3 Sources: "or `load`" removed. Conflict: K-shell-entity-6 asks to keep `translate` for spec 0027 §9.4's English synonyms; K32 (binding, unamended) wins. 0027 must read English some other way (English loads at boot, §7.3) or drop English matching; that is 0027's fixer's call.
- K18 / K-foundation-3 / K-shell-entity-7 / F-13 — §7.2 step 3: the switch passes `MeLocaleResult` to `TmCacheRefresher.adopt(t, { locale })` (0025 §5.3, §9.5); pointer spec 0018 §3.1 → §3.5.
- K-foundation-21 — §7.3 Sources: a server pack key beginning `tm.` or `tmUi.` is `string-key-collision`.
- K-foundation-1 — §8.4 Clean-up row: "spec 0025 §2.6's session clean-up, then `TmDrafts.clearAll(sub)`"; §8.4 diagram step reads "session clean-up (0025 §2.6), drafts cleared"; §8.6 step 2: "spec 0025 §2.6's session clean-up for the expiry path (drafts kept)".
- F-23 — §8.4 Signing out row: `TmApi` rejects new calls except the acting tab's `/bff/logout` write and `send` calls.
- K39 / K-foundation-5 / K-crud-7 / K-reports-8 / F-12 — §9.2 Page routes row: a `canDeactivate` that runs the leave checks registered in the page scope (`tmLeaveGuard`, 0031 §11.3), which the details host and the report studio register. Skipped F-12's named export `tmPageScopeCanDeactivate`: route assembly is internal and no distribution writes routes.
- K43 / F-5 / K-crud-1 — §9.2 route list: `'notifications/:id/open'` (0028 §10.2) listed among the tmPage routes before `:segment/:id`; "Under an entity segment" row names it beside `reports/:id/studio`. Skipped F-5's "not pinnable": the open route replaces its URL at once, so it is never pinned or recorded.
- F-17 — §9.2: `'reports/new'` dropped (it is `:segment/new` with the `create` hatch); `'reports/:id/studio'` moved under `<page paths>`, where it belongs as a `tmPage`; `'reports/:key'` cites 0034 §2.2.
- F-16 — §9.2: `:segment` canMatch is `TmEntities.bySegment(segment)?.kind === 'stack'`, shared by `:segment/new` and `:segment/:id`; `list/:segment` admits a stack or child segment; §9.4 `describe` comment says it classifies by §9.2's routes.
- K-crud-32 — §9.5: `size` omitted at the page's default (Canonical order row); example `page=2&size=50` → `page=2&size=100`.
- K17 / K-foundation-2 / F-13 — §10.3 step 5: `end` records first; `observe` runs one task after step 6 delivers the body, so a write's caller adopts first. §13 `TmApi` row pins it.
- K18 — §13 `TmLocalization` row pins the locale write's adoption.
- K-foundation-22 — §10.6: `blob-rejected` leaves the 413 row; the 422 row is "422 `validation`; `blob-rejected` at any status", presented in 0031 §8.5's fields or the uploading field, else the 400 dialog.
- K38 / K-foundation-4 / K-crud-6 / F-14 — §10.6 404 row: a details save shows the deletion dialog (0031 §9.2); another write the 400 dialog.
- K-foundation-17 — §11 Assistant seam: `TmContextError` cited at 0026 §9.1, §9.2.
- K-foundation-38 — Decisions 13, 14: "Tenant picker Q1:" and "Q2:" removed.
- K-reports-32 — Appendix A `coded-report-source`: sources neither exactly one of `route`, `run` and `component` nor `component` with `route`.
- F-3 — skipped (all 0024 parts: §4.4 cold-entry await, §2.4 securable, §14 cold-link pin). It makes the page host and its data read wait for the cold sync, which contradicts 00-architecture §7.1 step 5 ("`client/sync` … and the page's data request start together"), as does 0025 §3.2's cold row. Needs a ruling; if ruled in, the §4.4 row, §4.3's "one network wait" sentence and §14's DoD row change together.
- K26, K33, addendum 6 — verified: 0024 declares no `TmPageAction.confirm`; the sign-out toast copy matches 0028 §8.1; `notificationChannels` is `{ key, defaultEnabled }[]` in §3.1. No change.
