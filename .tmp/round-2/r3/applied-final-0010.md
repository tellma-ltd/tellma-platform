# Final fix pass: 0010 (all parts)

Sources: `verify-fable-reports.md`, `verify-fable-session.md`, `verify-fable-data.md`,
`verify-residual.md`. None of the four files has a `## 0010`, `## 0010-F` or `## 0010-AE`
heading, so this pass has no findings to apply to spec 0010 and leaves it unchanged.

## Changes

### 0010-amendments-to-implemented-specs.md

- None.

### 0011–0022, ARCHITECTURE.md

- Not in this range.

### 0023–0035

- Not in this range.

## Checks made against 0010 (no edit needed)

Findings in other ranges that rest on a 0010 fact were checked against the current text:

- **FA-07 (0033 §8.10).** 0010 §F.15's `MeasureContext` carries `DecimalTypes`, `Arguments`
  (an omitted parameter absent), `Today`, `Now` and `UserId` ("the values the statement's context
  slots were bound with"). The finding's premise holds; the fix is 0033's alone.
- **FA-01 (0033 §8.4, every export page carries `Measures`).** 0010 §F.14 "Rank columns → Scope"
  already ranks over every surviving group before paging and says two statements agree when the
  data did not change between them, so per-page partials of an export combine with no 0010 change.
- **FA-02 (0033 §9.2 totals row shape).** The totals wire shape is 0033's; 0010 §F.15 defines
  only `MeasureEvaluator`/`PartialTable`/`MeasureValue`, which do not constrain it.
- **VR-04 (0023 §9).** 0010 §B.8 defines both the `categorical` and `sequential` groups, as the
  finding states.
- **Residue grep.** No `§F.CUT-` marker, no term of editor-notes.md §3's Removed row, no
  `IContractFingerprints` and no `lossless` remains in 0010.

## Skipped findings

- None (no finding targets 0010).

## Choices to confirm

- None.

## Unresolved for other ranges

- None.
