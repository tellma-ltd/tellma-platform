# Applied: group 0023-29 (specs 0023 and 0029)

Baseline `063e313`. Only `docs/specs/0023-component-library-additions.md` (1239 → 1329 lines) and
`docs/specs/0029-client-entity-model.md` (1207 → 1231 lines) were edited. No `§F.CUT-` marker is
left in either file.

## 0023 changes by decision

**Decision 6 (icons)**
- Context, other specs: §B.8 (categorical), §C.10 "icon names or data", §C.12, and a line for
  0010 §B.7, §C.13, §D.11, §E.5 (existing glyphs through `tm-icon`).
- §1 table: `error-dialog` depends on `icon` (its actions draw icons).
- §3.1: opener says every library glyph goes through `tm-icon`; Rendering pins
  `--icon-stroke-width` at 1.75; new "First frame" bullet (draws in the creating pass); new "The
  library's own glyphs" bullet (`[svg]` with `lucide` data, `mirrorInRtl`; exceptions `tm-spinner`
  and the grid's per-cell masks; 0010 §B.7/§C.13/§D.11/§E.5 migrate existing entry points).
- §12 unit: `tm-icon` drawn after the first change detection. §13 Stable layout: a glyph present
  in its control's first frame. Decisions: new 19.

**Decision 7 (keys)**
- §1 table: `kbd` exports `tmKeysText`; `copy` depends on `kbd`.
- §3.2: opener names the grid's key map, find bar and hints; `tmKeysText` in the sketch; Shift not
  compared for symbols (numpad and main-key `Mod+Alt+Plus`); the `event.code` fallback covers US
  punctuation (11 codes listed, shifted symbols with Shift); AltGraph guard; one platform check
  with the ESLint rule `tm/no-platform-sniffing` (spec 0010 §C.12); `tmKeysText` rendering;
  example adds `tmKeysText('Mod+V')` and Arabic `Mod+/`.
- §3.3 copy failure tooltip uses `tmKeysText('Mod+C')` ("Press ⌘C to copy" on a Mac).
- §12 unit: vectors for Arabic `Mod+D`/`Mod+/`, Polish AltGr+A not matching, numpad and main-key
  `Mod+Alt+Plus`, `tmKeysText`. Decisions: 3 rewritten.

**Decision 8 (avatar colours; flag 10 closed)**
- §1 table: `avatar` exports `tmAvatarTone`.
- §4.2: opener; `colorKey` input; `tmAvatarTone(key): 1–8`; Tones (categorical `tint`/`onTint`,
  ≥ 4.5:1; no key keeps `--avatar-bg`); the FNV-1a hash written out (`h mod 8 + 1`, fixed
  vectors); Which key (`{entity}:{id}`, never email or `sub`; per-tenant tone accepted);
  Forced colours (1 px transparent border, system text colour); example `core.User:17` → 4,
  `core.User:18` → 3 (computed).
- §9: "No new semantic tokens" replaced by "One new semantic group, `categorical`" plus the two
  literals; opener drops "existing".
- §12: unit `tmAvatarTone` vectors; axe avatars in every tone; forced colours a tinted avatar.
- Decisions: 1 rewritten (one new semantic group); new 20. Review flag 10 deleted; old flag 11
  renumbered to 10 (no spec cites 0023's flags).

**Decision 9 (busy overlay, option B)**
- §5.4: opener (feedback in the click's frame, scrim only for long waits unless no trigger);
  `run` options gain `immediate?: boolean`; step 1 adds `cursor: progress`; bullets: callers
  with no visible trigger pass `immediate: true` (sign-out in every tab, closing action dialogs);
  every other caller shows its trigger busy in the click's frame.
- §12 harness and Playwright rows; §13 Instant feedback. Decision 9 rewritten; Review flag 3
  rewritten (alternative: a faint veil at once, option C).

**Decision 10 (search feedback)**
- §4.4: `tm-progress-bar` gains `active` (default true); new "The hold" bullet
  (`--progress-min-visible` 250 ms, never delays content, `aria-busy` follows the truth, overlaid
  by consumers).
- §6.2: `search` fires on the leading edge and once more `debounce` ms after a burst (as 0006
  §4.2); `debounce` is the coalescing window; new "Loading feedback" consumer rule (active from
  emit while the shown result is stale; 0030 §3.3 and 0034 §3.3 follow it); the example adds a
  timed "jed" walk-through.
- §12 unit (leading and trailing edges; the 250 ms hold); §13 Instant feedback. New Decision 21;
  new Review flag 11 (leading edge vs trailing only).

**Decision 19 (time zones)**
- Context: §D.4 adds "the zone label". §4.5 Tooltip: `'auto'` zone label appends the offset
  ("20 Oct 2026, 10:40 GMT+3"); the relative text is never labelled.

**Decision 24 (regional formats)**
- §10 Digits bullet: `ar-SA-u-nu-latn` gives Latin digits.

**Decisions 23, 25:** no hits in 0023 (no Acme aging/bank examples; no user-facing "Tellma").

## 0029 changes by decision

**Decision 1 / design-measures.md §8.5 (levels removed; measures on discover)**
- Context: drops "§F.CUT-9" and "level navigations"; cites §F.13 for `ComparedWith` and §F.14 for
  discovery's `Measures`.
- Vocabulary: *Level navigation* deleted. §2.2: `treeLevels` deleted. §4.1: `level?` and "level
  navigations included" deleted. §4.3: the Level-navigations bullet (with `Entity_Level`) deleted.
  §6.2: the level-navigation sentence of Aggregate mode deleted. §7 `reachable`: "a level
  navigation included" dropped. §11.2 Hidden: "level navigations" dropped. §17 descriptors row:
  "level navigations" dropped.
- §9: `QueryexDiscoverRequest` gains `string? Measures = null` (needs `Aggregate`); Diagnostics
  clause list adds `measures`; step 1: `Measures` without `Aggregate` is `400 bad-request`; step
  2: `Measures` included (spec 0010 §F.14); step 3: `QuerySpec` carries `Measures`, measure
  diagnostics at `Measures[<i>]` in clause `measures`; step 5 reworded ("a diagnostic never
  makes a 400").
- §10.1: `TmQxClause` gains `'measures'` (sent only with aggregate). §10.2: `measures` goes as
  `measures` (spec 0034 §12.7).
- §17 discover row: `measures` with QX4010/QX4011 at their sites, and `400` without `aggregate`.

**Decision 18 (decimals and longs as strings)**
- Context: spec 0024 §10.1, §10.4 (`TmId`, `TmDecimal`, plain JSON).
- §2.2: `TmDataType` comment (`integer` covers int, short, byte, long); `key.type` comment (ids are
  `number` for `int`, `TmDecimal` for `long`); `TmPropertyMetadata.long?: true`.
- §5.3: `label(entity, id: TmId)`, `resolve(entity, ids: readonly TmId[])`.
- §6.2: `TmPlanRow.id: TmId` ("as the wire sent it"); `TmQueryRowSet` "decimal and long cells are
  TmDecimal" (no "losslessly").
- §8.2: `tmQx.num(value: number | string)`; Literals bullet: pass the text such as a `TmDecimal`;
  non-decimal text throws.
- §12.1: `TmValueDescriptor.long?: true`. §12.2: new "Decimals and longs" bullet (format the text
  with `Intl.NumberFormat.prototype.format`; `tmNumber` converts per 0024 §10.4).
- §14.1: `itemId`, `displayWith`, `tm-entity-field` value and `picked`, `tm-entity-multi-field`
  value use `TmId`.
- §15: `AcmeCustomer.creditLimit: TmDecimal`; Types bullet (`decimal` and `long` are `TmDecimal`,
  a `long` key included; `int` keys stay `number`).
- §17: plans/filters/formats row (`TmDecimal` at 28 digits, `long` exact); printer row (`num`
  refusals).

**Decision 20 (retry classes)**
- 0029 calls no long route. §9 Wire class: `Idempotent = true` puts `queryex/discover` in the
  Contracts chunk's `idempotent` list, so `TmApi.read` accepts it (spec 0024 §10.1).

**Decision 21 (`TmPermissions.label`)**
- Context: spec 0032 §12.4 joins the 0014 §10 line. §7: `label(resource, action?)` in the sketch;
  new bullet (labels from the securables section, joined by `tm.entity.permission`
  "{resource}: {action}"; unlisted pair shows raw names; read by 0024 §11's `forbidden` messages).
- §17 descriptors row: `label` cases. New Decision 13.

**Decisions 19 and 24 (time zones, regional formats)**
- §5.2: new golden row `ar-SA-u-nu-latn` → "42"; new bullet "Native digits are client-only"
  (`clientOnly: true`; the C# test skips; server text uses Latin digits, spec 0013 §9.2).
- §12.1: `TmFormats.text` labels instants in `'auto'`, `cell` in `'column'`; new
  `zoneSuffix(of): Signal<string>` (" (GMT+3)" while `zoneMismatch()`); new "Zone labels" bullet.
- §12.2 `instant` row: labelled per §12.1; `datetime` never labelled.
- §17: golden row wording; zone-label unit cases.

**Decision 8 (avatar key) in 0029**
- §12.2 `target` row: the avatar's `colorKey` is the record key `{entity}:{id}` (`core.User:17`).

**Decision 7 in 0029**
- §10.1/§10.5: `submit` on `Mod+Enter`, matched by `tmKeysMatch` (no platform sniffing).

**Decisions 17, 23, 25:** no change needed in 0029 (all URL builders used inside a routed tenant;
no Acme aging/bank example; no user-facing "Tellma").

## Requests for other files

- **0030 §6.1 (owner 0028-32):** "`tmSearch` (spec 0023 §6.2), a 50 ms sliding debounce" →
  "`tmSearch` (spec 0023 §6.2): it fires on the first key after a pause and again 50 ms after a
  burst". §3.3 Loading: the bar turns `active` when `search` emits and holds while the result is
  stale, with the 250 ms hold of spec 0023 §4.4 (replaces "150 ms after a read starts"); same for
  the §5.3 "reloading" row and the §18 timing test. §7.1: `header(col)` appends
  `TmFormats.zoneSuffix(col)` (spec 0029 §12.1).
- **0027 §9.6 Timing (owner 0025-27):** "Providers run on the debounced `search` output" →
  "Providers run on the `search` output, which fires on the leading and trailing edges (spec
  0023 §6.2)".
- **0027 §7.4 (owner 0025-27):** the session button's `tm-avatar` passes `colorKey`
  `core.User:{me.user.id}` (spec 0023 §4.2). §6.1's tenantless Account avatar passes no key
  (accent tone).
- **0021 actors (owner 0015-22) and 0028 actors, 0032 §5.4 members (owner 0028-32), 0035 owner
  (owner 0034-35):** every direct `tm-avatar` passes `colorKey` `{entity}:{id}`.
- **0031 §6.2 navigation row (owner 0028-32):** "with an avatar column, `tm-avatar` and the name"
  → add "its `colorKey` the record key (spec 0029 §12.2)", or cite 0029 §12.2.
- **0032 §7.4 setting-key mapping (owner 0028-32):** an `Int64` key maps to `integer` with
  `long: true` (spec 0029 §12.1's `TmValueDescriptor.long`), so its value is a `TmDecimal`.
- **0031 editors (owner 0028-32):** the `tmNumber` binding converts for `decimal` and for
  `integer` with `long` (spec 0029 §12.2).
- **0024 §11 (owner 0024):** the `{permission}` example "Sales invoices: Save" → "Sales invoices:
  Create and edit" (the Save action's label in 0032 §5.2). `TmPermissions.label(resource)` with no
  action gives `{resource}` alone.

## Open questions

- **`TmPropertyMetadata.long` / `TmValueDescriptor.long`.** Decision 18 needs the client to know
  which `integer` values are strings, so editors convert them; I added an optional `long?: true`
  flag rather than a new `TmDataType` `'long'` (which would ripple through 0030–0034's type
  switches). Flip to a distinct type if Ahmad prefers.
- **0029 §6.2 Aggregate mode** still says "each term a grouping key that depends on the id",
  while 0033 §8.4 reads navigation labels as `max(...)`. No spec calls `tmSelectPlan` with
  `aggregate: true` today (0030 uses the plain mode). Left as is; consider removing the aggregate
  option or aligning it with 0033 §8.4.
- **`tm.entity.permission`** is a new client string ("{resource}: {action}"); 0029 lists no
  string table, so it is named only in §7.

## Fix pass

No file of 0010–0022 or ARCHITECTURE.md was edited in this pass.

**0023**
- §3.1: the opener's "Every glyph a library component draws goes through `tm-icon`" deleted (the
  "library's own glyphs" bullet states the rule with its two exceptions); "So every glyph shares one
  stroke, one size token and one RTL rule" deleted (Decision 19 holds the why). V7-04.
- §3.1: new **Stroke** bullet split from Rendering: `--icon-stroke-width` is 1.75 by default; a host
  may set its own, as the image edit pane's check does at 2.25 (spec 0010 §D.11). §9 "What a
  consumer may set" gains `--icon-stroke-width`. Decision 19: "at stroke 1.75 by default". V2-03 /
  V8-11.
- §3.2: sketch example `'Shift+?'` → `'?'`. V2-05.
- §3.2: the `event.code` fallback has three cases: a letter or digit binding when `event.key` is not
  an ASCII letter or digit ("ي" from D, "]" from Shift+D); a US punctuation binding when `event.key`
  is one non-ASCII character ("ظ"); either kind under Alt on Apple. Example: `Mod+D` matches `'ي'`
  and `Mod+Shift+D` matches `']'`, both by `KeyD` (replaces the `Mod+Alt+Shift+D` / `'ي'` claim).
  §12 unit row adds the `Mod+Shift+D` (`key` `']'`) vector. V2-06.
- §4.2 Which key: the rationale clause after "never an email or a `sub`" deleted (Decision 20 holds
  it). V7-05.
- §5.4: `immediate` callers are sign-out and "a command whose trigger has closed, such as a
  confirmation, the conflict dialog or a menu item (spec 0031 §10.1)"; every other caller's
  examples add the `pending` action button of a dialog that stays open (spec 0031 §10.4). V2-04 /
  R-18.

**0029**
- §6.2: `tmSelectPlan(root, keys)` loses `options?: { aggregate?: boolean }`; the "Aggregate mode"
  bullet deleted. No spec in 0030–0035 uses it (0030 §5.1 is the only caller), and §17 has no row
  for it. V1-10 / V8-10 / R-14.

**Rulings confirmed, no change:** R-11 (0029's generic ids are `TmId`: §5.3, §6.2, §14.1), R-13
(0029 §2.2, §12.1, §12.2 treat `integer` with `long` as `TmDecimal`).

### Skipped findings

- None. V8-10 duplicates V1-10 and V8-11 duplicates V2-03; each pair was applied once.

### Unresolved for other groups

- **0026 §8.3 "Physical key" (owner 0025-27):** "so it works on non-Latin layouts where `event.key`
  is `ي` (spec 0023 §3.2)" → "where `event.key` is not `d`, such as `ي`, or `]` with Shift, on an
  Arabic layout (spec 0023 §3.2)". Its next sentence, "In an editable field, it is ignored when
  `event.key` is a printable character other than `d` or `D`", blocks the shortcut in a field on an
  Arabic layout; align it with whatever 0027 §4.2 step 4 adopts for V2-07 (Option-typed characters
  only, not a non-Latin `event.key` under Mod).

## Readability pass

No meaning changes; no section, decision or flag renumbered.

**0023**
- §3.1 "The library's own glyphs": split into short sentences; the two exceptions as sub-bullets.
- §3.2 Modifiers and Keys bullets: last sentence split; the Arabic-layout example as a small
  binding / `event.key` / `event.code` table.
- §4.2 Tones as two sub-bullets (with and without a key); the FNV-1a hash as three numbered steps;
  Forced colours split into two sentences.
- §4.4 "The hold": "stays visible for at least"; the two never-delayed facts as sub-bullets.
- §4.5 Tooltip: "the display zone's offset differs from the device's".
- §5.4 opener's last sentence reworded ("only after a long wait, or at once when no trigger is
  visible"); the `immediate` callers as sub-bullets; "Every other caller" split into sentences.
- §6.2 "When `search` emits" leading and trailing edges reworded; "Loading feedback" split; the
  "jed" example as a numbered walk-through.
- §9 the `categorical` bullet reworded (no dangling "it").
- §13 Instant feedback: semicolon chain split into sentences.
- Decisions 9 and 20: why clauses tightened.

**0029**
- §5.3 "Ids by key type": the `targetId` case split out as a "For example" sentence.
- §7 `label` bullet: the three rules as sub-bullets.
- §8.2 Literals: "pass its text instead".
- §9 step 3 ("including QX4010 and QX4011"), step 5 split into two clauses.
- §10.2 When: the `measures` gloss moved out of the parenthesis.
- §12.1 Zone labels: the `'column'` mode's header and cells as sub-bullets.
- §12.2 `target` row and "Decimals and longs" bullet reworded.
- §15 Types: semicolons replaced by sentences.
