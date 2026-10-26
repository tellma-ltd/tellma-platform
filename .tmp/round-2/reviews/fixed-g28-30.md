# Fix log — specs 0028, 0029, 0030

## Spec 0028 (`docs/specs/0028-assignments-and-attention-surfaces.md`)

- K-shell-entity-29 — Context: 0027 line cites §3.2, §5.1, §7.2, Appendix A (shortcuts), not §4.3.
- K43 / K-shell-entity-1 / F-5 — Context: 0024 line adds §9.2 (the `me/notifications` and open routes); new line "spec 0030 §3.1, §12.2 — `selection` page actions".
- K-shell-entity-23 — §1 primary row: names `TM_ATTENTION_DOCUMENTS`, the two `TM_LEADER_DOCUMENTS` entries.
- K43 / F-5 — §1 `/attention-pages` row: adds `TmNotificationOpenPage`.
- K-shell-entity-27 — §6.1: reduced-motion pointer is spec 0023 §2.2.
- K-shell-entity-29 — §6.1, §7.1: shortcut pointers are spec 0027 Appendix A.
- K43 / K-shell-entity-1 / F-5 — §6.2 Activation: a row is a real link to `/{t}/notifications/{id}/open`; a new tab marks read through the route.
- K-shell-entity-27 / K-foundation-26 — §6.4: 44 px panel rows kept as 0028's own rule, now "as spec 0010 §C.10's menu rows are" (not §B.6). Chose K-foundation-26's §C.10 variant over K-shell-entity-27's "touch height of §B.6": under K3 §B.6 is the 46 px form-field rule.
- K43 / K-shell-entity-1 / Q-14 — §6.5 "Read on visit" deleted (last subsection; no renumbering). Followed K-shell-entity-1 and ruling Q-14 over F-5's "§6.5 becomes …".
- K-shell-entity-37 — §7.4: one sentence citing spec 0035 §9.2's widgets `core.assignments-to-me` / `core.assignments-by-me`, up to `SummaryLatestCount` rows; counts, View all and refresh left to 0035.
- K-shell-entity-40 — §8.1: "(a spec admits another by an edit here)" → "this list is closed".
- K-shell-entity-30 — §8.1 "Exporting…" row: raised by specs 0030 §13.2, 0034 §9.
- K33 / K-shell-entity-4 — §8.1 pin row: "A pin added with no visible chip (the bar hidden, or the pin in More)", copy "Pinned {label}.".
- K-shell-entity-19 / K-foundation-27 — §8.1 diagnostics row: copy "Diagnostics copied." / "Couldn't copy. Open About, then Diagnostics, to see the details." 0026 §8.3 now cites 0028 for the copy (owner wins), so 0028 carries the longer, more helpful failure copy (K-foundation-27's stated alternative).
- K-crud-15 — §8.1: added row "Caches refreshed" / "Caches refreshed." raised by spec 0032 §7.7.
- K-shell-entity-28 / K-foundation-29 — §9.2 Dismiss: "stays dismissed for that version, the value of `TmUpdates.available()` (spec 0024 §5.5)"; no longer claims 0024 states the rule.
- K43 / K-shell-entity-1 — §10 opener: custom code includes the notification open route.
- K43 — §10.1 "Rows open their target": `rowLink` may return a notification's open route; bulk actions include `selection` page actions (spec 0030 §12.2).
- K43 / K-shell-entity-1 — §10.1 table, Notifications row: adds `/{t}/notifications/{id}/open` and `tmPage('notifications/:id/open', …)` (no launcher entry). Kept in the Notifications row (as Jobs holds job details) so "the four pages" stays true.
- K-shell-entity-15 — §10.1 table, Notification settings row: declaration is "a platform route (spec 0024 §9.2)", not `tmPage('me/notifications', …)`.
- K43 / K-shell-entity-1 / F-5 — §10.2 declaration: `rowLink` → `TmUrls.page('notifications/{id}/open')`; added `selection` page actions `attention.notifications.read` (`inbox/read`) and `attention.notifications.clear` (`inbox/clear`, destructive, confirmed); kept read-all and clear-read (F-5); added `tmNotificationOpen = tmPage('notifications/:id/open', …)`.
- K43 / K-shell-entity-1 / F-5 — §10.2 prose: bullets for the text cell, actions ("Mark as read" and "Clear" act on the selection), and the open route's three steps (stored `inbox` row else `notifications/get`; `TmAttention.read([id])` when unread and writable; replace the URL with the target, else `/{t}/notifications`). "Opening a row … marks nothing read" removed. Used `TmAttention.read` (overlay and failure toast of §5.2) rather than a bare `inbox/read`; same wire call.
- K-shell-entity-15 — §10.3 opener: platform route `/{t}/me/notifications` (spec 0024 §9.2) with route data `{ kind: 'settings', skeleton: 'details', titleKey, load }`, `load` importing `TmNotificationSettingsPage`. Picked `skeleton: 'details'`, as spec 0032 §7.1's settings route does.
- K-shell-entity-18 — §10.3 Test delivery: address is the contact email, else the email (spec 0021 §4.4); "No address" when neither.
- K43 / K-shell-entity-1 — §12 Playwright row: adds a bell row opened in a new tab marked read, and Mark as read / Clear over a selection.
- K-shell-entity-40 — §13 Server: "the members the Context list names in other specs exist".
- K44 / K-shell-entity-2 / F-25 — §13 Acme bullet: cites spec 0018 §1.4's assignment additions instead of restating them.
- K43 / K-shell-entity-1 — Decisions: added 13 (open route marks read); Review flag 12 (no bulk Mark as read / Clear) deleted, old flag 13 renumbered 12.

## Spec 0029 (`docs/specs/0029-client-entity-model.md`)

- K13 / K-shell-entity-3 — Context: 0016 line is §2.4, §3.4, §3.5 (route, `QueryDiagnostic` and `AccessSummary`, row set); `tellma_describe` dropped.
- K-shell-entity-32 — Context: 0024 line is §3.2, §4.4, Appendix A.
- K-0032b / K-crud-11 (counterpart) — Non-goals: "Per-row capabilities" no longer says the server exposes none; a stack's `permissions` extra carries them, read by the details host (spec 0031 §4.1). Not named by K-crud-11, which fixes only 0031's copy of the same claim; applied so 0029 does not contradict K-0032b.
- K-shell-entity-16 — Vocabulary: a level navigation is a navigation descriptor with `level` set.
- K-shell-entity-31 — §2.1 Sources: "(spec 0012 §3.1, §11.1)".
- K13 / K-shell-entity-3 — §2.1: sentence "`tellma_describe` renders from the same model" deleted.
- K-shell-entity-32 — §2.4: `label-missing` cites spec 0024 §3.6.
- K-0034a / K-shell-entity-5 / K-crud-4 / F-6 — §3 sketch: `details` object gains `readonly edit?: 'page'`; comment cites spec 0031 §3.7.
- K-shell-entity-36 / K-shell-entity-32 — §3 Build check: four-row code table replaced by the code list citing spec 0024 Appendix A. Kept one sentence on which operation each member needs (`details`; `search`, `columns`, `filters` → `query`), because 0024 Appendix A's `operation-missing` row is generic and names 0029 §3 as the owner.
- K-crud-31 — §3 example: caption "launcher and picker members (spec 0030 §2 shows the search members)"; `columns:` and `tile:` lines deleted.
- K-shell-entity-16 — §4.1: `TmNavigationDescriptor extends TmNavigationMetadata { label; level? }`; no `kind: 'level'`.
- K-shell-entity-16 — §4.3 Level navigations: `Level{j}` resolves by name (spec 0010 §F.9) to a `kind: 'reference'` self-navigation with `level` `j`, never listed in `navigations`.
- F-3 — §7 Source: added "The route host never creates a page host before the first `me` and `settings` (spec 0024 §4.4), so hosts never see that state." Depends on F-3's 0024 §4.4 edit, which is 0024's fixer's.
- K-shell-entity-20 — §9 `QueryexProfile` comment: "validated as role save validates it (step 3)".
- K30 / K-shell-entity-9 / K-foundation-15 — §18 Telemetry: `2-10`, `1-4` with ASCII hyphens.
- K-shell-entity-39 — Decisions 3 and 11: "Details Q2:" and "Stock pages Q1:" removed.

## Spec 0030 (`docs/specs/0030-search-page.md`)

- K-crud-31 — §2 example: caption "the search members of Acme's customers declaration (spec 0029 §3 shows its launcher and picker members)"; `section` line dropped (a launcher member, shown in 0029).
- K43 / K-crud-1 / F-5 — §3.1: opener adds that a `selection` entry is an Actions-menu item (§12.2); sketch gains `selection?: boolean` and `run(search, ids: readonly number[])` (ids `[]` when not a selection action). Chose K-crud-1's required `ids` over F-5's optional `ids?`: 0030 owns the signature, and an always-present array needs no undefined check.
- K26 / K-crud-25 — §3.1 sketch: `confirm` gains `args?: Signal<TmJsonObject>`, citing spec 0026 §9.4's shape.
- K43 / K-crud-1 — §3.1 example: the Notifications `rowLink` to the open route and the `selection` "Mark as read" action, captioned as spec 0028 §10.2's.
- K-foundation-26 — §3.2: controls below `sm` are 46 px (`lg`, spec 0010 §B.6).
- K-foundation-26 — §19 WCAG row: "46 px below `sm`" to match §3.2.
- K-crud-18 — §3.4 and §16.1: both write the merged `{ view, list }` value of `search.<entity>.view`.
- K-crud-10 / K-reports-9 — §4.3: in list mode the canonical URL keeps the list path and its fixed `filter` (§17).
- K-crud-19 — §4.3: pins store canonical strings (spec 0027 §8.2); recents keep the path only (spec 0025 §13).
- K-crud-17 — §5.1: tree keys `ParentId`, `SubtreeCount` and, on an activatable tree, `ActiveSubtreeCount` (spec 0012 §2.1); §16.2 `hasChildren` likewise.
- K-foundation-25 — §6.1 Parts: the input sits inside a `tm-form-field` drawing the magnifier and clear slot (spec 0023 §6.2's custom-chrome case).
- K-crud-20 — §6.4 grid row: `§C.5`, `§C.4` → spec 0010 §C.5, §C.4.
- K-crud-33 — §7.2, §11.1, §13.2 headings: "(Search Q1)", "(`choose-columns.png`)", "(Trees Q1)" removed.
- K-foundation-24 — §10.1: a chip's label cites `TmChip.activate` (spec 0023 §4.3, now added by 0023's owner); text otherwise kept.
- K43 / K-crud-1 / F-5 — §12.2: opener adds that a page action shows while its `requires` holds; table gains the "page actions" group (each `selection: true` entry, its `run` with the selected ids, its own `confirm`, no count toast).
- K37 / K-crud-5 — §15.3: two bullets replaced by one sentence (spec 0031 §12 steps `ids`, reads `request` past an edge, writes back with `supersedes`); the `request: null` guard is 0031's.
- F-19 — §15.1 sketch: `request` is also null for a browse tree (§16.2).
- K-crud-19 — §17: the list is never a recent cites spec 0024 §9.4, spec 0025 §13.
- K-crud-30 — §19 Page context: `state` is spec 0026 §9.3's `search` minimum.
- K43 / K-crud-1 — §19 Page actions: a `selection` entry is a `menu` action over the selection.
- K-crud-16 — §19 Telemetry: `1-10`, `1-5`, `6-10` with ASCII hyphens.
- K43 — Decision 3: bulk actions derive from entity actions and `selection` page actions.
- K-crud-33 — Decisions 1 and 2: "Search Q1:" and "Trees Q1:" removed.
- K-crud-33 — Review flag 11: "Three layout choices", with the alternatives listed.
- K-crud-33 (same class) — Review flags 2 and 9: "as designed" removed.

## Skipped

- K-shell-entity-6 (K32 vs the launcher's English terms): 0024 and 0027 only.
- K-shell-entity-2's 0018 §1.4 edits, K-shell-entity-17 (0021), K-shell-entity-3's 0016 half, K-crud-1's 0024 route line, K-crud-4's 0031 and 0034 halves: other fixers' files.
