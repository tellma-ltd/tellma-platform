# Applied: group 0010-AE

File: `docs/specs/0010-amendments-to-implemented-specs.md`: Parts A–E and their entries in Context,
Goals/Non-goals and "Parts B–E: rules shared". Part F and its entries untouched (group 0010-F ran
first). Parts A–E grow from 2,288 to 2,570 lines: five new items (§B.7, §B.8, §C.13, §D.11, §E.5)
plus §C.12's matching and §D.4's zone labels. No existing section was renumbered.

## Spec 0010

**Front matter**

- Context, "Other specs": the 0023 bullet names `tm-icon`, `kbd` and `tm-chip` and cites §B.7,
  §C.13, §D.11, §E.5 (glyphs), §C.10, §C.12 (keys), §E.2 (chips) (d6, d7); new bullet "spec 0023
  §4.2, spec 0034 §7.7 — the avatar tones and chart series that read §B.8's `categorical` group"
  (d8); the 0024 bullet adds `TM_DEVICE_TIME_ZONE` (d19); new bullet "spec 0024 §7.8, spec 0029
  §12.1 — which surfaces show §D.4's zone label" (d19).
- Goals: Part B adds glyphs through `tm-icon` and a categorical colour group (d6, d8); Part C adds
  "a published key map that works on every keyboard layout" and glyphs (d6, d7); Part D adds the
  offset label and glyphs (d6, d19); Part E adds glyphs (d6).
- Non-goals, Parts B–E: new bullet "matching named keys (arrows, Enter, Escape, Tab) through `kbd`:
  they are the same on every layout (§C.12)" (d7).

**Parts B–E: rules shared**

- "Order" rewritten: spec 0023 §3.1–§3.2 (`icon`, `kbd`) come first, then Part B, then Part D;
  Part C needs only 0023 §3, Part E also 0023 §4 (d6, d7).
- New rule "Glyphs draw through `tm-icon`": `<tm-icon [svg]>` with `lucide` data; directional
  glyphs pass `{ node, mirrorInRtl: true }`; exceptions `tm-spinner` and the grid's per-cell check
  and dash masks; boxes and strokes (1.75 by default) unchanged, drawn in the creating pass, pinned
  by a geometry e2e per part (d6).
- New rule "Keys through `kbd`": letters, digits and symbols through `tmKeysMatch`, hints through
  `tmFormatKeys`/`tmKeysText`, platform through `tmIsApplePlatform`; named keys keep direct checks;
  ESLint rule `tm/no-platform-sniffing` refuses `navigator.platform` and `navigator.userAgentData`
  outside `kbd/` in `projects/core/**` and `projects/module/**`, shipped by Part C (d7).

**Part B**

- Intro: Part B also changes the foundation's glyphs and the token contract; consumers add the
  avatars and charts (specs 0023, 0034) (d6, d8).
- §B.4 Panel bullet: a multi-select row shows the `tm-checkbox` box with its `check` glyph through
  `tm-icon` (§B.7), not the grid's mask (d6; see Open question 2).
- New §B.7 "Foundation glyphs through `tm-icon`" (target 0002 §3.5): `checkbox` (`check`,
  `minus`), `select` (caret `chevron-down`, option `check`, the multi-select row's `check`),
  `form-field` (`circle-alert`); sizes kept through `--icon-size`; the grid masks match; tests and
  budget (`./form-field` closest to its ceiling) (d6).
- New §B.8 "The `categorical` colour group" (target 0002 §4): `TmCategoricalTone { mark, tint,
  onTint }`; `TmSchemeColors.categorical` as `Record<1..8, TmCategoricalTone>`; emission
  `--categorical-{n}-mark|tint|on-tint` in both scheme blocks (`tmRefToVarName` gains the case);
  fixed slot order; the default preset's light and dark values for all eight slots (marks are
  0034's former series values, tints and inks from g2's table, in 0034's order teal, orange,
  violet, amber, magenta, green, blue, red); readers (avatar `tint`/`onTint`, chart `mark`); a
  tokens unit test for `onTint` on `tint` ≥ 4.5:1 in both schemes (d8).
- Testing: unit adds the contrast test and emitted names; Playwright adds §B.7 geometry with the
  checkbox against a grid cell; gates add `tokens:check` over the group (d6, d8).
- DoD: new **Glyphs** and **Tokens** bullets (identity server's token CSS refreshed); Stable layout
  adds glyph boxes; WCAG AA adds the 4.5:1 test; Budgets add `./checkbox`, `./form-field`
  re-measured (d6, d8).
- Decisions: new 10 (every built-in glyph through `tm-icon`, two exceptions) and 11 (categorical
  colours as a semantic scheme group read by avatars and charts) (d6, d8).
- Review flags: new 7 (eight categorical slots; alternative more slots, as Fluent's 30) (d8).

**Part C**

- Intro: Part C also moves the grid's keys onto `kbd` and its glyphs onto `tm-icon` (d6, d7).
- §C.3 Indicator: the sort arrows are a decorative `tm-icon` (§C.13), not inline SVG (d6).
- §C.10: `type TmMenuIcon = TemplateRef<void> | string | TmIconSource`; the Icons bullet renders a
  `TmIconSource` with `[svg]` (the grid's context menu); the Shortcuts bullet adds the paste hint
  "Press {shortcut} to paste" filled by `tmKeysText('Mod+V')` (d6, d7).
- §C.12 retitled "The key map, published and matched through `kbd`"; Target adds 0004 §8.3, §8.7;
  Requested by adds spec 0023 §3.2; Why adds the Arabic-layout failure; the sketch comment says
  `kbd` is imported at runtime; new bullets "One table" (the binding table drives the matcher and
  `TM_GRID_KEYS`), "Matched through `kbd`" (`Mod+A/Z/Y/D/F`, `Mod+Alt+Plus` with `Mod+Alt+=`,
  `Mod+Alt+Minus` through `tmKeysMatch`; named keys direct; parsed keys cached), "The platform
  through `kbd`" (find bar `Mod+F`, pointer Mod via `tmIsApplePlatform`, hints; no grid platform
  check, enforced by `tm/no-platform-sniffing`); the example adds Arabic Ctrl+D; new
  Compatibility/Tests/Budget labels (d7).
- New §C.13 "Grid and menu glyphs through `tm-icon`" (target 0004 §8.5, §8.7, §10, §13.2, §15):
  table of context-menu, find-bar, status-bar, tree-expander, sort and menu-row glyphs with their
  RTL rule; the context menu passes `TmIconSource` so `ɵTmGridIcons` goes; the expander swaps
  `chevron-right`/`chevron-down` instead of rotating, replacing its `:host-context([dir='rtl'])`
  rule; the check and dash masks stay; per-row cost under the scroll probe (d6).
- Testing: unit adds key matching with an Arabic `key`/Latin `code`; Playwright adds the Arabic
  Mod+Z/D/A/F e2e and §C.13 geometry, live flip and scroll probe; new `lint:test` line for
  `tm/no-platform-sniffing` (d6, d7).
- DoD: Menu bullet adds `TmIconSource`; new **Keys** and **Glyphs** bullets (`ɵTmGridIcons`
  removed); UI rules add glyph boxes, the mirrored chevrons after a flip, and the Arabic shortcuts
  (d6, d7).
- Decisions: new 15 (letter/digit/symbol shortcuts through `kbd`, named keys direct), 16 (one
  binding table), 17 (per-cell masks stay) (d6, d7).
- Review flags: new 8 (one `tm-icon` per visible tree row and option row; alternative CSS masks)
  (d6).

**Part D**

- Intro: Part D also changes the components' glyphs (d6).
- §D.4 retitled "Date-time, time of day, relative time and zone labels"; Requested by adds spec
  0024 §7.8 and spec 0029 §12.1–§12.2, "spec 0024 §7.1 fills both zones"; Why adds the offset
  label. Sketch: `zoneLabel?: boolean` on `TmDateTimeFormatOptions` and `TmTimeFormatOptions`;
  `tmFormatZoneOffset`, `tmFormatZoneName`; `TM_DEVICE_TIME_ZONE`; `TmZoneLabelMode`; `TmL10n`
  gains `deviceTimeZone`, `zoneMismatch`, `zoneLabel(instant?)`, and `zoneLabel?: TmZoneLabelMode`
  (default `'auto'`) on `formatDateTime`/`formatTime`. New bullets "Zone labels" (compared at the
  instant, offsets never abbreviations, never on relative time, dates or `timeStyle: 'none'`) and
  "The modes" (`'auto'`, `'column'`, `'none'`). Golden: four arrays; `dateTime`/`time` rows take
  `zoneLabel?` with `en-GB`, `ar-SA`, `ar-SA-u-nu-latn` in Asia/Riyadh and Asia/Kolkata; new
  `zoneLabel` array `{ locale, timeZone, deviceTimeZone, instant, mode, header, expected }` (equal
  offsets, Beirut DST, UTC); the server twin skips label rows. Example adds the device zone and
  "20/10/2026, 10:42 GMT+3". Tests add the modes and `zoneMismatch`. Budget `./l10n` ≈ 9.3 KB
  (d19, design-locale §2.2).
- New §D.11 "Basics glyphs through `tm-icon`" (target 0005 §6.2, §6.4, §7.4–§7.5, §8.1, §8.4, §10,
  §13, §15): `alert`, `date-picker`, `file-preview`, `files`, `image`, `modal`, `private`; the date
  popup's chevrons take `mirrorInRtl`, replacing its `scaleX(-1)` rule; the image edit pane's check
  keeps its 2.25 stroke through `--icon-stroke-width`; the `button` and `input` docs examples use
  `<tm-icon [svg]>` (d6).
- Testing: unit adds the zone-label modes and `zoneMismatch`; format golden adds the label rows
  and the `zoneLabel` array; Playwright adds §D.11 geometry and the popup chevrons after a flip
  (d6, d19).
- DoD: Formats adds `TM_DEVICE_TIME_ZONE` and the label modes; new **Glyphs** bullet; showcase
  `i18n` adds zone labels with a differing device zone; UI rules add glyph boxes and the popup
  chevrons; Budgets: `./l10n` 8 → **10** (was 9; 7.65 + §D.7/§D.8 + 0.4 KB ≈ 9.3), §D.11's entry
  points re-measured (d6, d19).
- Decisions: new 12 "Zone labels are GMT offsets, never abbreviations, compared at the instant
  shown" (d19).

**Part E**

- Intro: Part E also draws the picker's glyphs through `tm-icon` (§E.5) (d6).
- New §E.5 "Picker glyphs through `tm-icon`" (target 0006 §3, §4.1, §9): `circle-alert`, `search`,
  `check` (single picker, multi-picker rows, a cell's dropdown) (d6).
- Testing: Playwright adds §E.5 geometry. DoD: new **Glyphs** bullet; Stable layout adds glyph
  boxes (d6).

**Part A**

- No change. Decision 25: Part A quotes no user-facing copy that says "Tellma" (`id.tellma.com`
  in §A.9's example is a host name, `tellma_kind`/`tellma_api` are identifiers). See Open
  question 1.

**Decisions with no 0010 Parts A–E part**

- d9 (busy overlay `immediate`) and d10 (search feedback): owned by 0023, 0024, 0030, 0031, 0032,
  0034; nothing in Parts A–E mentions the overlay delay or search debounce.
- Grep of Parts A–E for editor-notes §4 terms: no hits for the Removed names, `§F.CUT-`,
  `TmDecimal`, `lossless`, `long: true`, `tenantId?`, `bank-reconciliation`, `aging`, `init`,
  `tellma-client check`, `hide`, `forbidden`, `412`, `busy`, `levels`, `having`; `totals` hits
  (§C.6 totals row) are unaffected; `ar-SA` hits are format examples, unchanged.

## Requests for other files

- **0023 (group 0023-29), §3.1 Rendering:** "1.75, the weight of every Lucide glyph the library
  ships" is not quite true: the image edit pane's done check draws at 2.25, which 0010 §D.11 keeps
  by setting `--icon-stroke-width` on its host. Suggest "(1.75 by default)" or name the exception.
- **0023 (group 0023-29), §3.2 "One platform check":** if 0023 names the lint rule, use
  `tm/no-platform-sniffing` (0010 "Parts B–E: rules shared").
- **0023 (group 0023-29), Context "Other specs" Part D bullet:** may add `TM_DEVICE_TIME_ZONE`
  beside `TM_TIME_ZONE` if `tm-relative-time`'s tooltip label reads it (0010 §D.4).
- **0034 (group 0034-35), §7.7:** already reads `categorical.N.mark` in the same order; the values
  now live only in 0010 §B.8 (light and dark). No further change needed unless 0034 restates them.
- **0029 (group 0023-29), §12.1:** `TmFormats.zoneSuffix` should build on `TmL10n.zoneLabel()`
  while `zoneMismatch()` is true, and `cell` should pass `zoneLabel: 'column'` (0010 §D.4 defines
  the header contract that way).

## Open questions

1. **Identity server pages and decision 25.** The identity server (spec 0003 §11.1, §11.3) is the
   platform's shared authority; its sign-in, consent and Manage pages and its emails default to
   Tellma branding through `BrandingResolver` (`client_id → distribution`). They are not one
   distribution's surface in the shared deployment, but in the in-proc mode (spec 0011 §5.7) they
   serve a single distribution. Part A amends none of that copy, so I changed nothing. Should a
   Part A amendment make the in-proc engine (and the shared one, per `client_id`) show the
   distribution's `Distribution_DisplayName` instead of "Tellma"?
2. **Multi-select row checkbox.** Decision 6 names only `tm-spinner` and the grid's per-cell masks
   as exceptions, so §B.4's row checkbox now draws its `check` through `tm-icon` instead of the
   grid's mask (one component per option row, covered by Part C flag 8). Alternative: keep the mask
   there as a third exception, since the row box is the same CSS box as a grid cell.
3. **`Mod+Alt+=`.** The grid accepts `=` as well as `+` for insert-row today. §C.12 keeps that as a
   second binding (`Mod+Alt+=`) sharing the intent and label, since `tmKeysMatch('Mod+Alt+Plus')`
   matches `+` only. Drop it if the unshifted `=` was accidental.
4. **Lint rule scope.** `tm/no-platform-sniffing` refuses `navigator.platform` and
   `navigator.userAgentData` only, not `navigator.userAgent`, because spec 0026's support bundle
   records the user agent. Confirm the name and scope.

## Fix pass

Findings for 0010-AE: V2-01 and V8-02 (the same R-24 change), V2-02, V7-01; ruling R-24. R-1 to R-4
name 0010-AE questions with no edit; the current text matches each (R-2: §B.4/§B.7 row check
through `tm-icon`; R-3: §C.12 keeps `Mod+Alt+=`; R-4: the lint rule refuses `navigator.platform`
and `navigator.userAgentData` only). Part F untouched.

### Spec 0010

- **Context, "Other specs":** the 0023 §4.2 / 0034 §7.7 bullet reads "the avatar tones, chart
  series and chart ramp that read §B.8's `categorical` and `sequential` groups" (V2-01, V8-02,
  R-24).
- **Goals, Part B:** "categorical and sequential colour groups for avatars and charts" (V2-01,
  R-24).
- **Parts B–E: rules shared, "Glyphs draw through `tm-icon`":** reduced to a citation of spec 0023
  §3.1's rule for the library's own glyphs, its two exceptions included (the grid's masks stay,
  §C.13), plus what the amendments add: each glyph keeps its box and stroke, a geometry e2e per part
  in LTR and RTL, and the list of §B.7, §C.13, §D.11, §E.5. The restated object form, exceptions,
  1.75 stroke and first-frame rule are gone (V2-02).
- **§B.8** retitled "The `categorical` and `sequential` colour groups" (number unchanged):
  `TmSchemeColors.sequential: Readonly<Record<1..7, TmTokenValue>>` (step 1 faintest); emission
  `sequential.4` → `--sequential-4` in both scheme blocks, `tmRefToVarName` gains the case; default
  values as references into the teal ramp, light `{teal.100}`, 200, 300, 400, 600, 700, 800 and
  dark `{teal.900}`, 800, 700, 600, 400, 300, 200 (teal-500 skipped so seven steps keep 0034's
  former endpoints); "Who reads them" adds the chart's `sequential` palette (spec 0034 §7.7);
  Compatibility inherits the seven steps; Tests: both scheme blocks emit 31 names (was 24)
  (V2-01, V8-02, R-24).
- **Part B Testing:** Unit row "the names both colour groups emit"; Gates row `tokens:check` over
  the `categorical` and `sequential` groups (V2-01, R-24).
- **Part B Definition of done, Tokens:** both groups in the contract, emitter and default preset
  (V2-01, R-24).
- **Part B Decisions:** 10 reads "apart from `tm-spinner` and the grid's masks (Part C decision
  17)", so the masks' decision lives once, in Part C (V7-01); new 12 "The chart's magnitude ramp is
  a `sequential` scheme group" (light and dark ramps run in opposite directions, which a component
  token emitted once cannot hold; one ramp for both schemes was the alternative) (R-24). No number
  changed; nothing cites Part B decisions by number.

### Skipped findings

- None. V8-02 duplicates V2-01; both applied as one change. V8-02's "seven concrete hex values" was
  met with references to the teal primitives instead of literals, matching V2-01 and R-24 ("values
  as 0034 §7.7 lists them today") and keeping a re-branded teal ramp in step.

### Unresolved for other groups

- **0034 (0034-35), §7.7:** V2-08/V8-12 stand: the `sequential1`–`sequential7` row reads
  "`sequential.1` … `sequential.7` (spec 0010 §B.8)" and the lead paragraph says the ramp reads the
  semantic `sequential` group. The Context bullet "the `categorical` tones" (line ~39) may add "and
  the `sequential` ramp".
- **0023 (0023-29), §9 "One new semantic group, `categorical` (spec 0010 §B.8)":** §B.8 now adds two
  groups; 0023 reads only `categorical`. Suggest "**The semantic group it reads,** `categorical`
  (spec 0010 §B.8)" so a reader does not take `categorical` for the only one. (should)

## Readability pass

No meaning changes; no section, decision or flag renumbered. Part A had no changed text; Goals
Parts C and D were already reworked by the 0010-F pass and are left as they stand.

- Context, "Other specs", the spec 0023 §3, §4 bullet: its three uses become sub-bullets.
- Parts B–E rules shared, "Order": the sequence leads; the why, Parts C and E, and the rest of
  spec 0023 become bold-led sub-bullets.
- Parts B–E rules shared, "Glyphs draw through `tm-icon`": the dense parenthetical about the two
  exceptions becomes its own sentence; the list pointer gains a bold lead.
- Parts B–E rules shared, "Keys through `kbd`": the three-helper sentence becomes sub-bullets
  (shortcuts, key hints, platform).
- §B.8 opener: active voice ("The emitter writes …"); the two groups' shapes become bullets.
- §B.8 "Emission" and "Contrast": semicolon and qualifier splits.
- Part B Decision 12: the why-clause reordered for one reading. Part B flag 7: "as" → "like".
- §C.10 "Icons": the three `TmMenuIcon` forms become a sub-list after the size rule.
- §C.12 Why: split into two sentences.
- §C.12 "Matched through `kbd`": the key run becomes Letters, Rows and Cost sub-bullets.
- §D.4 opener: the offset label split into its own sentence.
- §D.4 "Zone labels": "Where each surface labels is spec 0024 §7.8's" reworded; a semicolon split.
- §D.4 "The modes", `'column'`: the labelled-header and bare-header cases become sub-bullets.
- §D.4 "The golden": each array a bold-led bullet with full sentences; "They cover" → "The arrays
  cover".
- §D.11 "Docs examples": the "which §D.10 sizes" clause moved next to the children it describes.
- Part E opener: the glyph clause split into its own sentence.
