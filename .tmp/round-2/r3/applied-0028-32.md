# Applied: group 0028-32

Files: `docs/specs/0028-assignments-and-attention-surfaces.md` (1284 → 1300 lines),
`docs/specs/0030-search-page.md` (1366 → 1385), `docs/specs/0031-details-page.md` (1309 → 1328),
`docs/specs/0032-stock-pages.md` (1262 → 1319). Baseline `063e313`. No section renumbered; no section
removed. No `§F.CUT-` marker, `levels`, `totals`/`having` (report sense), `lossless`, `long: true`
(call option), `tenantId?`, `bank-reconciliation` or aging example is left in these files.

## 0028 — Assignments and attention surfaces

- **D6 (icons):** no change; every glyph is a registry name.
- **D8 (avatars):** no change; 0028 draws no `tm-avatar` directly. Bell rows show the actor's name
  only; the Notifications and Assignments pages' user columns go through spec 0029 §12.2, which
  passes `colorKey`.
- **D17 (`TmUrls`):** §8.1 the toast overflow pill: outside a tenant it reads "{n} more" with no
  action (its `TmUrls.search` would throw in development).
- **D18 (decimals, `TargetId`):** §4.3 example `"targetId": "23"`; §5.1 a row's `targetId` is a
  `TmDecimal`; §10.4 `rowLink` casts `TargetId` `as TmDecimal`; §11.3 the caption's stored
  `amount` is a string (`"12400.00"`) and `Intl.NumberFormat` formats the string.
- **D19 (time zones):** §8.1 closed outcome list gains "The time zone changed automatically with
  the device's" | "Times now show in {zone} ({offset})." | Change → My profile | spec 0024 §7.8;
  §9.3 new `TmInputIdle` (primary; `idle`, `whenIdle()`), the one input-idle clock that `TmBanners`
  and spec 0024 §7.8's prompt wait on; §1 table and Vocabulary (*Idle gate*) name it; Context cites
  0024 §7.8; §12 unit row covers `whenIdle`.
- **D24 (digits):** §6.4 badge digits follow the locale (Arabic-Indic in `ar-SA`, Latin in
  `ar-SA-u-nu-latn`); §13 RTL row "the locale's digits".
- **D20, D21, D23, D25:** no hits needing change (`summary`/`seen` already declare
  `Idempotent = true`; no "Tellma" in copy).

## 0030 — Search page

- **D10 (search feedback):** §6.1 `tmSearch` fires on the first key after a pause and again 50 ms
  after a burst (no "sliding debounce"); the instant-echo bullet adds the bar in the first key's
  frame. §3.3 Loading: the bar turns `active` when a read starts (for text, when `search` fires),
  stays active while the result is stale (§6.3), with 0023 §4.4's 250 ms hold; `aria-busy` while a
  read runs. §5.3 "reloading" row: "rows held, the result stale". §6.3 example re-timed (30 ms per
  key, leading edge). §18 unit (the bar's `active` rule) and harness (five keys → two reads, bar
  shown on the first key, 250 ms hold). §19 Instant feedback. New Decision 14. Context cites 0023
  §4.4, §6.2.
- **D18 (decimals, ids):** §2.2 `CreditCell extends TmSearchCell<TmDecimal>`; generic ids become
  `TmId` in `TmSearchContext` (`selection`, `highlight`, `select`, `open`, `url`),
  `TmSearchPageActionDeclaration.run` and `TmSearchSnapshot` (`ids`, `selection`, `highlight`,
  `expanded`), matching 0029's `TmPlanRow.id: TmId`.
- **D19 (time zones):** §7.1 `header(col)` appends `TmFormats.zoneSuffix(col)` ("Created at
  (GMT+3)"); §13.2 export `header` is the displayed header without the zone suffix (Excel is never
  labelled); §18 unit vectors. Context cites 0029 §12.1.
- **D20 (long routes):** §13.2 `TmApi.download(route, body, { signal, onProgress })`; the
  Contracts chunk marks `export` and `export-for-import` long (spec 0024 §3.4).
- **D24 (digits):** §14 pager status "in the locale's digits".
- **D7 (keys, consistency):** §10.1 the filter editor applies on `Mod+Enter`, matched through `kbd`
  (spec 0029 §10.5), not "Ctrl+Enter or Cmd+Enter".
- **D6, D8, D9, D13, D17, D21, D23, D25:** no hits needing change.

## 0031 — Details page

- **D8 (avatars):** §6.2 navigation row: the avatar's `colorKey` is the record key (spec 0029
  §12.2).
- **D9 (busy overlay):** §8.1 step 5: Save's `running` spinner is the click's feedback, the page
  turns inert at once, the scrim follows after 200 ms. New §10.1 bullet "The overlay's start": a
  trigger still on screen shows `running` (toolbar) or `pending` (an open dialog's action button)
  and the scrim follows at 200 ms; a command whose trigger closed (menu item, confirmation, conflict
  dialog) passes `immediate: true`. §9.1 Reload and Overwrite, §10.2 Delete and §10.3 Activate run
  under the immediate overlay; §10.4 `run` keeps the dialog open with its action button `pending`.
  §17 harness case; §18 Instant feedback row; new Decision 15. Context cites the `immediate` start.
- **D18 (decimals, ids):** §3.6 clone step 1 is a plain `structuredClone` (no `TmDecimal` case);
  §8.2 example amounts are strings (`"1032.0000"` read, `"800"` typed) and the `TmDecimal` sentence
  is replaced by one line on strings; §6.4 totals sum with `TmDecimals.add`; §10.4 example
  `"creditLimit": "250000"`; §11.1 draft `model` is plain JSON (no "lossless"). Generic ids become
  `TmId`: `TmDetailsSource.id`, `tmEntitySource`, `TmDetailsContext.id`, `childTarget.id`,
  `TmRelated.get`, `reveal`, `TmEntityActions.run`/`dialog`, `TmActionDialogContext.ids` and
  `run({ ids })`, `TmDetailsDraft.clonedFrom`; §4.1's `T` bullet says ids are `TmId`. §17 unit
  "exact totals with `TmDecimals`".
- **D19 (time zones):** §6.8 audit instants carry spec 0029 §12.2's zone label.
- **D25 (white label):** §5.1 the document title uses the localized display name (spec 0024 §4.4).
- **D17, D20, D21, D23:** no hits needing change.

## 0032 — Stock pages

- **D8 (avatars):** §5.4 role members: `colorKey` `core.User:{id}`.
- **D13 (no client hide of server facts):** §2 table: the row is "Hide a stock launcher entry; the
  page stays reachable" with `hide: ['entry:core.Schedule']` only. The old
  `hide: ['settingsTab:invoicing']` example implied a stock settings tab could be hidden; a
  settings category is server metadata, and `settingsTab:` only drops a declared tab body.
- **D18 (decimals):** §7.4 new "Large numbers" bullet: `Int64` keys are `integer` with
  `long: true` and `Decimal` keys `decimal`, both `TmDecimal`; `Int64` array lines parse through
  `TmDecimals.parse`.
- **D21 (forbidden labels, Access tab):** §4.6 rows are named by `TmPermissions.label` (the words
  of the `forbidden` messages, spec 0024 §11) with the raw pair as description; the toolbar search
  matches labels and raw names; new example (Omar, "Sales invoices: Create and edit"). §12.4 names
  `TmPermissions.label` and the Access tab as readers of the chains. §13 harness case. New
  Decision 16.
- **D19, D24 (design-locale §4 "0032"):** §4.1 `tm-user-locale-fields` renders five fields and
  gains inputs `scope` (default `offered`; My profile sets `language`) and `autoTimeZone`. §4.2
  retitled; `tm-region-field` and `tm-digits-field` sketches and table rows; the sample; Settings'
  null labels "Language default"/"Region default"; `Users.RegionNotOffered`; unknown digits fail
  enum binding. §7.5 General tab gains the *Formats* card. §7.7 step 1: a Region or Digits change
  re-formats in place. §8 layout passes `scope="language" autoTimeZone`; "thirteen" members; region
  and digits re-format in place; new "Automatic time zone" bullet (`tm-auto-time-zone`, reads
  `TmDeviceZone.auto`, writes `setAuto(on, 'profile')`, outside Save, draft and dirty state; never
  on the Users page); Conflicts bullet adds the time-zone prompt and automatic update as
  `users/me/locale` writers. §12.1 two rows (0013 §5.6/§6.1/§6.5; §1.2/§7.2), §12.2 "Regional
  members" bullet (0014 §3.1, 0016 §3.4, 0018 §3.1/§3.2). §13 harness and e2e cases. §14 Instant
  feedback. Decision 11 extended; Review flag 12 extended; new Review flag 15 (region lists differ
  by page). Context cites 0024 §7.7, §7.8.
- **D23 (credit control):** §5.2 "Aging report" → "Credit exposure report"; §12.4
  `Securable_acme_AgingReport` → `Securable_acme_CreditExposureReport`.
- **D25 (white label):** §7.7 Refresh caches copy "outside {displayName}".
- **D6, D9, D10, D17, D20:** no hits needing change (`access/check`, `invitation-status`,
  `settings/details` already declare idempotent; the invite and credentials dialogs stay open, so
  spec 0031 §10.4's `pending` button covers them).

## Requests for other files

- **0024 (group 0024), §7.8 step 7:** optionally name the gate's API: "wait for
  `TmInputIdle.whenIdle()` (spec 0028 §9.3)".
- **0034 (group 0034-35):** report grid headers of instant columns append
  `TmFormats.zoneSuffix(col)` as spec 0030 §7.1 does (design-locale §4 "0030"); report exports
  carry no zone suffix. `TmSearchContext` ids are now `TmId` (spec 0030 §2.5), which `ctx.open(id)`
  in 0034 §11 accepts unchanged.
- **0013 (group 0011-14):** 0032 §12.1 now cites §5.6, §6.1, §6.5 (`Region`, `Digits`,
  `Settings.RegionNotOffered`) and §1.2, §7.2 (`region`, `regions`, `nativeDigits` on each
  `languages` entry). Please confirm those sections carry them.
- **0014 (group 0011-14), 0016 and 0018 (group 0015-22):** 0032 §12.2 cites 0014 §3.1
  (`PreferredRegion`, `PreferredDigits`), 0016 §3.4 (`UserProfileView` both), 0018 §3.1
  (`MeSaveRequest`, thirteen members) and §3.2 (`Users.RegionNotOffered`).
- **0023 or 0010 (strings):** 0032 §4.2 names the digit systems with `tm.digits.<system>` ("Latin
  (123)", "Arabic-Indic (١٢٣)"), following `tm.calendar.<code>`; design-locale said "a `tmUi` name
  per system". If a library string is preferred, the owner of the `tmUi` table adds it and 0032
  follows.

## Open questions

1. **"Closing action dialogs use `immediate`."** I read it as: a dialog that closes when its button
   is pressed (a confirmation, the conflict dialog) passes `immediate: true`; the generated and
   declared action dialogs stay open during `run` (they show argument errors), so their action
   button shows `pending` and the scrim follows at 200 ms (0031 §10.1, §10.4; g2 item 4's edit plan
   for 0031 §10.4). If every action dialog should close at once and use `immediate`, 0031 §10.4
   must move its error display out of the dialog.
2. **Generic ids as `TmId` in 0030/0031.** Editor-notes say generic APIs take `TmId`; the search and
   details hosts serve any entity, including a distribution's `TopLevelEntity<long>`, so their id
   members became `TmId`. Entity-specific ids (notifications, jobs, users, blobs) stay `number`.
3. **Region list scopes (0032 Review flag 15).** The design says the region field lists the active
   language's regions. That fails for an administrator editing another user (their language says
   nothing about the user's) and for the tenant's Formats card (an English-speaking admin could not
   pick `SA`, since `en-SA` is not offered). I added `scope: 'language' | 'offered'`; only My
   profile uses `language`. Same for the digits field's visibility.
4. **`settingsTab:` hide row (0032 §2).** Removed as a "hide a stock tab" example under decision
   13's spirit; spec 0024 §2.3 still allows `settingsTab:` to drop a declared tab body. Confirm.

## Fix pass

No file of 0010–0022 or `ARCHITECTURE.md` belongs to this group; no change-report bullets.

### 0028, 0030
- No change. No finding addressed them; R-13 (integer with `long`) and R-15 names checked: 0030
  formats every value through `TmFormats` and spec 0029 helpers, so it has no type switch of its
  own.

### 0031
- §10.4: declared `TmEntitiesResult<T>` beside `TmEntityActionOutcome` (`ids: readonly TmId[]` in
  the stack's key type, `entities`, optional `related`, `extras`, `rows`; spec 0016 §3.4, §3.2,
  spec 0029 §6.2); the outcome's comment shortened to "spec 0016 §3.6's envelope" (V4-12, R-11).
- §5.1 Title: "with the localized display name, the document title" → "followed by the
  distribution's `{displayName}`, the document title", so it no longer reads as the record's
  display name (R-15, decision 25).
- R-18 confirmed: §10.1 "The overlay's start", §9.1, §10.2–§10.4, tests and Decision 15 already
  state the accepted reading; no edit.

### 0032
- §2 table: restored the `settingsTab:` example as its own row, "Drop an earlier feature's
  settings-tab editor; the category's generic tab returns (spec 0024 §2.3) |
  `hide: ['settingsTab:invoicing']` | 1" (V3-10, V8-16, R-20; 0024 §2.2's `TmDeclarationKey` and
  §2.3 still allow `settingsTab:`). This supersedes the D13 bullet above.

### Skipped findings
- V8-16's merged wording ("Hide a stock launcher entry or settings tab"): applied V3-10's separate
  row instead, since `settingsTab:` drops a feature's declared tab body, not a stock tab.

### Unresolved for other groups
- None. (0024's V5-06 and V8-09, which touch text citing 0028 §9.3 and 0032 §4.2, are filed under
  0024 by the verifiers.)

## Readability pass

No meaning changes; no section, decision or flag renumbered.

- 0028 §5.1: the `targetId` sentence reads "is a `long`, so it is a `TmDecimal` string".
- 0028 §9.3 "One clock, exported": one subject pair (`TmBanners` and the time-zone prompt).
- 0028 §11.3 "Stored form": the amount sentence without the appositive.
- 0030 §3.3 "Loading": split into four sub-bullets (start, stale, hold, `aria-busy`).
- 0030 §6.1 "Parts": `tmSearch` timing reordered ("fires at once on the first key …, then …").
- 0030 §6.3 example step 1: "answer older text".
- 0030 §7.1 "Headers": the zone-suffix case as a labelled "For example" in two sentences.
- 0030 §13.2: "It saves …" back beside the `TmApi.download` call; the 6-minute limit is its own
  "Time limit" bullet.
- 0030 §18 harness: the five-keys item split into three items (reads, progress bar, Enter).
- 0031 §5.1 "Title": focus target and document title as two sentences.
- 0031 §6.2 table, `navigation` view cell: the avatar's `colorKey` as its own clause.
- 0031 §8.1 step 5: the spinner sentence split from the inert/scrim sentence.
- 0031 §9.1 "Reload": the immediate-overlay clause moved to its own closing sentence.
- 0031 §10.1 "The overlay's start": two sub-bullets, *On screen* and *Closed*.
- 0031 §10.2 step 2 and §10.3: the "shows at once" reason as its own sentence.
- 0031 Decision 15: the rejected alternative without stacked parentheses.
- 0032 §4.1 `tm-user-locale-fields`: dropped the restated "Tenant default ({effective value})"
  label (owned by §4.2's "Nullable" bullet).
- 0032 §4.2 table, digits row: the native-system rule in short sentences.
- 0032 §4.2 "Nullable": the Settings page's labels cited to §7.5, which states them.
- 0032 §4.6 "Rows": three sub-bullets (name, description, state).
- 0032 §7.4 "Large numbers": one lead sentence and three sub-bullets.
- 0032 §8 "Language and calendar", "Automatic time zone" (four sub-bullets) and "Conflicts"
  ("Three writers call `users/me/locale` …").
- 0032 §12.4: the section's readers listed after "It has four readers:".
- 0032 Decision 11: the why in two sentences.
