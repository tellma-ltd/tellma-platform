# Verify: library cluster (decisions 6–10; R-2, R-3, R-4, R-18, R-24)

Checked against baseline `063e313` and the current text of 0010 Parts A–E (with their Context,
Goals, Testing, DoD, Decisions and Review-flag entries), 0023, and the consumers 0021, 0024, 0027,
0028, 0029, 0030, 0031, 0032, 0034, 0035. Found right and not repeated here: the four new glyph
amendments in the amendment format; `TmMenuIcon` with `TmIconSource`; `tm/no-platform-sniffing`
named the same everywhere, with R-4's scope; `kbd`'s punctuation fallback, AltGraph guard, Shift
rule and `tmKeysText`; §C.10/§C.12 on `kbd` with Arabic-layout tests; R-2 and R-3; `tmAvatarTone`
(FNV-1a; the vectors `core.User:17` → 4 and `core.User:18` → 3 recomputed and correct); §B.8's
values against 0034's former series and g2's tint table; 0023 flag 10 closed; the busy overlay in
0023 §5.4, 0024 §8.4, 0027 §10.3, 0031 §8.1/§10.1/§10.2/§10.4 and 0032 §4.3; search timing in 0023
§4.4/§6.2, 0030 §3.3/§5.3/§6.1/tests/Decision 14, 0027 §9.6 and 0034 §3.3/§14/§15; 0010 §D.4
against design-locale.md §2.2; 0023's Context.

## 0010-AE

- [V2-01] (must) 0010 §B.8 — the section defines only "`readonly categorical: Readonly<Record<1 | 2
  | 3 | 4 | 5 | 6 | 7 | 8, TmCategoricalTone>>`"; no `sequential` group exists anywhere in 0010 →
  add a `sequential` group beside `categorical`: `readonly sequential: Readonly<Record<1 | 2 | 3 |
  4 | 5 | 6 | 7, TmTokenValue>>` in `TmSchemeColors`, emitted as `--sequential-{n}` in both scheme
  blocks (`tmRefToVarName` gains the case), with the default preset's values (light teal-100 →
  teal-800, dark teal-900 → teal-200, as 0034 §7.7 lists them today) and "Who reads it: chart
  ramps (spec 0034 §7.7)". Add a Part B Decisions row (one ramp per scheme needs the scheme shape,
  as `categorical` does), extend Part B Testing's `tokens:check` row and DoD **Tokens** bullet to
  both groups, and extend the Context bullet "spec 0023 §4.2, spec 0034 §7.7 — the avatar tones
  and chart series that read §B.8's `categorical` group" and the Part B Goal ("a categorical colour
  group for avatars and charts") to name the sequential ramp. Retitle §B.8 "The `categorical` and
  `sequential` colour groups" (number unchanged). Why: R-24.
- [V2-02] (should) 0010 "Parts B–E: rules shared", **Glyphs draw through `tm-icon`** — "Every
  built-in glyph renders `<tm-icon [svg]>` with data imported from `lucide` … **Two exceptions:**
  `tm-spinner` … and the grid's per-cell check and dash masks" restates spec 0023 §3.1's "The
  library's own glyphs" bullet (rule, object form, both exceptions, first frame) → keep the rule
  once, in 0023 §3.1, and reduce this bullet to a citation plus what the amendments add: "Every
  built-in glyph follows spec 0023 §3.1's rule, its two exceptions included (the grid's masks stay,
  §C.13). Each glyph keeps its box and stroke; a geometry e2e per part pins them, in LTR and RTL.
  §B.7, §C.13, §D.11 and §E.5 list each entry point's glyphs." Why: the check "the glyph rule and
  its two exceptions are stated once and cited"; docs/AGENTS.md "state each fact once".

## 0023-29

- [V2-03] (must) 0023 §3.1 Rendering — "`--icon-stroke-width` (1.75, the weight of every Lucide
  glyph the library ships)" → "`--icon-stroke-width` (1.75 by default; a host may set it, as the
  image edit pane's check keeps 2.25, spec 0010 §D.11)". In Decision 19, "at stroke 1.75" → "at
  stroke 1.75 by default". Why: 0010 §D.11 "The edit pane's check keeps its heavier 2.25 stroke:
  its host sets `--icon-stroke-width`" contradicts "every"; request from 0010-AE.
- [V2-04] (should) 0023 §5.4 — "Sign-out in every tab (spec 0024 §8.4) and an action dialog that
  closes on its button are such callers." → "Sign-out in every tab (spec 0024 §8.4) and a command
  whose trigger has closed, such as a confirmation, the conflict dialog or a menu item (spec 0031
  §10.1), are such callers." In the next bullet, after "such as the Save button's spinner", add
  "or the `pending` action button of a dialog that stays open while its work runs (spec 0031
  §10.4)". Why: R-18; 0031 §10.1/§10.4 name the dialog that stays open the "action dialog", so
  0023's "an action dialog that closes on its button" reads against them.
- [V2-05] (should) 0023 §3.2 sketch comment — "Examples: 'Mod+K', 'Shift+?', 'F2', 'Mod+Plus'." →
  replace `'Shift+?'` with `'?'`. Why: the bullet below says a printable symbol "never compares
  Shift", so the example teaches a form the matcher ignores (decision 7, "Shift not compared for
  symbols").
- [V2-06] (should) 0023 §3.2 — "A letter, a digit or a US punctuation key matches by `event.code`
  in two cases: `event.key` is one non-ASCII character, as on an Arabic layout" misses letter keys
  whose Arabic character is ASCII: on Arabic (101), Shift+D types "]" and Shift+L types "/" (verify
  on the layout). Then the section's own example "`Mod+Alt+Shift+D` matches `key` `'ي'`" is wrong
  (with Shift the key is "]", not "ي"), and spec 0026's vector "`Mod+Alt+Shift+D` on an Arabic
  layout" fails. → for a letter or digit binding, match by `event.code` whenever `event.key` is not
  an ASCII letter or digit ("ي" from D, "]" from Shift+D); keep "one non-ASCII character" as the
  trigger for punctuation bindings. Fix the example to "`Mod+Alt+Shift+D` matches `key` `']'` with
  `code` `'KeyD'`" (or use unshifted `Mod+D` with "ي"), and add the vector Arabic `Mod+Shift+D`
  (`key` `']'`) to §12's unit row. Why: decision 7 (shortcuts work on an Arabic layout).

## 0025-27

- [V2-07] (must) 0027 §4.2 step 4 — "It never runs when `event.key` is a printable character other
  than its own letter, such as `∏` from Option+Shift+P." On an Arabic layout, Mod+K in a field sends
  `key` "ن" and Mod+/ sends "ظ", so the `inFields` shortcuts `tm.launcher.open` and
  `tm.shortcuts.open` (Appendix A) never run from a field, while 0023 §3.2 matches exactly these by
  `event.code` ("`Mod+/` matches `key` `'ظ'` with `code` `'Slash'`"). → "It never runs when Option
  (Alt on Apple, without ⌘ or Ctrl) typed a character other than its own key, such as `∏` from
  Option+Shift+P. A non-Latin `event.key` under Mod or Ctrl, as on an Arabic layout, still runs."
  Add to §13's dispatch unit vectors: Mod+K and Mod+/ in a field on an Arabic layout run. Why:
  decision 7; consistency with 0023 §3.2.

## 0034-35

- [V2-08] (must) 0034 §7.7 — "| `sequential1`–`sequential7` | light teal-100 → teal-800; dark
  teal-900 → teal-200 |" → "| `sequential1`–`sequential7` | `sequential.1` … `sequential.7` (spec
  0010 §B.8) |", and the intro sentence "Its series read the semantic `categorical` group (spec 0010
  §B.8)" → "Its series read the semantic `categorical` group and its ramp the `sequential` group
  (spec 0010 §B.8), both switching with the scheme". The values then live only in 0010 §B.8. Why:
  R-24 (a component group is emitted once and cannot hold per-scheme values).
- [V2-09] (should) 0034 §11 Columns — "Owner (avatar and name)"; 0035 §14 Rows — "Owner (avatar and
  name)" → in both, "Owner (avatar and name; the avatar's `colorKey` is the record key, spec 0029
  §12.2)". Why: decision 8 ("the platform passes the record key `{entity}:{id}`"); g2 item 3 lists
  "0034 sharing and owner lists, 0035 owner" as users that pass the key, and 0034 §10.4 and 0031
  §6.2 already say so explicitly.
