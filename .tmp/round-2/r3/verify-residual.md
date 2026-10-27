# Residual items left by the fix pass

## 0024

- [VR-01] (should) 0024 §3.1 — "`Distribution_Copyright`, the About dialog's copyright line, falling
  back to `copyright`;" → "`Distribution_Copyright`, the About dialog's copyright line, in the
  active language, else `copyright`;". Why: spec 0027 §11.1 item 5 cites §3.1 for that rule.
- [VR-02] (should) 0024 Review flag 2 — it restates spec 0016's flag on `decimal` and `long` as
  JSON strings → keep only what is 0024's own (the client side: `TmDecimal`, plain `JSON.parse`,
  the lint rules) and cite spec 0016's flag for the rest. Why: each fact once (docs/AGENTS.md).

## 0026

- [VR-03] (must) 0026 §8.3 "Physical key" — "The shortcut matches `event.code` `KeyD`, so it works
  on non-Latin layouts where `event.key` is `ي` … ignored when `event.key` is a printable character
  other than `d` or `D`." → the shortcut is registered with spec 0027's registry and matched by
  `tmKeysMatch` (spec 0023 §3.2); in an editable field it follows spec 0027 §4.2 step 4. Delete the
  hand-written `event.code`/`event.key` rule. Why: decision 7 (only `kbd` reads keys and platform);
  with Shift on Arabic (101) `event.key` is `]`, not `ي`, and the field rule blocked it on Arabic
  layouts.

## 0023

- [VR-04] (should) 0023 §9 — "**One new semantic group,** `categorical` (spec 0010 §B.8)" → "**The
  semantic group it reads,** `categorical` (spec 0010 §B.8)". Why: 0010 §B.8 now defines two groups
  (`categorical` and `sequential`); the library reads only `categorical`.
