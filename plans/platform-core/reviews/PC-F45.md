# PC-F45: grid-states showcase story hands each contentKey switch its own record

## Implementer notes

### What was built, and where

- **`client/projects/internal/showcase/src/app/grid/grid-states-story.ts`.** The story's
  "Switch content" button toggled `contentKey` A/B over one shared rows array, against the
  grid's contentKey TSDoc ("A new key comes with its own rows array", D-077). On a flat grid that
  is harmless today (no lazy loads), but the story demonstrated the misuse, and B showed A's
  edits (removals, refreshes, shuffles) as its own. Now, as PC-F37 did for the tree-grid story:
  - A and B are separate records. Each owns a seed (`seeds.A`, `seeds.B`: two `makeRows()`
    calls, so separate arrays of separate row objects).
  - `switchContent` parks the outgoing record's rows in a private `parked` map and binds the
    incoming record's own rows (its seed on its first bind), setting the key and the rows in the
    same handler.
  - `restoreData` restores the bound record's own seed. It used to restore one shared `baseRows`
    for both, which, once records exist, would have let A and B end up holding the same array
    (restore in A, switch to B, restore in B, switch back to A: A's parked array is B's bound
    array, so the key changes over an unchanged array).
  - Every other transition (refresh, remove, shuffle, set empty) already produced a new array
    from the bound record's rows, so no two records can ever share an array.
  - TSDoc added to every declaration that lacked it (the row interface and its members, the
    record type, the seed helper, the signals, the accessor and the handlers), plus template
    section comments; the class TSDoc states the own-rows rule.
- **`client/e2e/specs/grid-virtualization.spec.ts`**, "state lifetimes" group, one new test,
  *each content binds its own rows: one content's edits never show in the other*. A: remove the
  middle ten and refresh (41 rows, qty 1,105). Switch to B: 51 rows, qty 105, note v0 (B's own
  seed). B: shuffle. Back to A: 41 rows, row 0 is Row 0, qty 1,105 (A's edits kept, B's shuffle
  absent). Back to B: 51 rows, still shuffled, unrefreshed qty. The file header now mentions the
  records.
- Both changed files were run through prettier with the repo's `.prettierrc`. That also
  re-wrapped pre-existing lines (three test signatures in the spec, toolbar buttons in the
  template) that predate this item; no behaviour changes. Prettier is not enforced by any gate,
  and many other client files carry the same drift.

### Mutation check

The new test was run against two mutations of `switchContent`, each against a production build
of the mutated story (`CI=1 node scripts/e2e.mjs --project chromium --workers 2 --grep "binds its
own rows" e2e/specs/grid-virtualization.spec.ts`), and the code was restored afterwards:
1. The old story (toggle the key only, one shared array): fails on both attempts at the first B
   assertion, aria-rowcount expected 51, received 41 (`.tmp/f45-e2e-mut1.log`).
2. Toggle the key and hand over a copy of the current array (`[...rows]`), which satisfies array
   identity but still shares the data: fails the same way (`.tmp/f45-e2e-mut2.log`).

The `restoreData` aliasing case (one shared seed) is not observable on a flat grid, which has no
behaviour keyed on array identity beyond the data it shows, so no e2e test can pin it; it is
prevented by construction (per-record seeds) and stated here for review.

### Verification

- `grid-virtualization.spec.ts`, `grid-a11y.spec.ts` and `grid-keyboard.spec.ts` (every spec that
  opens the grid-states story) in CI mode after `build:showcase`:
  `CI=1 node scripts/e2e.mjs --project chromium --project touch --grep-invert "@nightly|@perf|@quarantine" --workers 2 <specs>`:
  40 passed, none flaky, before the mutations (`.tmp/f45-e2e-1.log`) and again on the restored
  code after a fresh `build:showcase` (`.tmp/f45-e2e-2.log`). The touch project matches only the
  touch specs, so it runs none of these.
- `pnpm run lint` (full: eslint + stylelint) clean; `prettier --check` and `tsc -p e2e` clean on
  the changed files.
- No library source, .NET project, API golden or bundle changed, so the other gates are not
  touched (the brief scopes this item to the story and its e2e spec).

### Deviations

1. As in PC-F37, the switch binds separate records rather than a copy of one shared array: a copy
   satisfies array identity but still shows A's rows under B.
2. Prettier re-wrapped a few pre-existing lines of both changed files (see above); no behaviour
   change.

### Open risks

- None known. `loading` and the refresh counter stay story-wide (they are UI toggles, not record
  data); a refresh in either record bumps the shared generation number in the notes.

### What reviewers should examine hardest

- Whether any transition can still leave A and B holding the same array (the `restoreData` case
  above is the one I found).
- Whether the new test would really fail on both mutations (rerun them if in doubt) and whether
  its assertions hold on the touch project.
- The other `grid-states` consumers (grid-a11y's loading/empty tests, grid-keyboard) were run
  and are unaffected.

## Review 1

### Findings

Scope checked: the story change against the item and D-077 / the contentKey TSDoc
(`client/projects/core/tellma-core-ui/grid/tm-grid-base.ts:71-82`, "change `contentKey`
together with the bound rows, never over the same array"); every transition for array sharing
between A and B (seeds are two `makeRows()` calls; refresh, shuffle, set-empty and remove
produce new arrays from the bound rows or keep the bound one; restore binds the bound record's
own seed; `switchContent` sets key and rows in one handler); switching back and forth,
set-empty / restore / mount across switches; the new test's assertions against the
shared-array, copy, never-park and park-under-the-wrong-key mutations (each fails at a
rowcount assertion); consistency with PC-F37's tree-grid `parked` design; TSDoc and template
section comments; no spec, §, `docs/` or PC-id references added to TSDoc or test names. No
blocking or major issues found. Reran `build:showcase`, then grid-virtualization, grid-a11y and
grid-keyboard in CI mode (chromium, `--workers 2`): 40 passed, none flaky; eslint, prettier
`--check` and `tsc -p e2e` are clean on the changed files. The implementer's mutation logs show
both mutations failing at the first B rowcount assertion (41 instead of 51).

**R1-1 (minor). Restore data's per-record seed is changed code with no test, because the two
seeds are value-identical.**
`client/projects/internal/showcase/src/app/grid/grid-states-story.ts:33-41` and `:120-123`
seed A and B with identical values, and `:158-160` now restores `this.seeds[this.contentKey()]`.
The implementer notes say that the aliasing case "is not observable on a flat grid". That is
true of array identity, but it also makes a wrong-record restore invisible. For example, a
regression to `this.rows.set(this.seeds.A)`, or a restore that reads the outgoing key, would
show exactly the same cells in B. No test presses Restore data after a switch
(`client/e2e/specs/grid-virtualization.spec.ts:172-207` never uses it). So the only behaviour
this item changed in `restoreData` is pinned by nothing. Suggested fix: make B's seed visibly
B's own, e.g. give `makeRows` a record argument that writes it into the note (`note 5 v0 (B)`)
or offsets B's qty. Then extend the new test: after B's shuffle, restore in B and assert B's
unshuffled seed (B's marker, row 0 = `Row 0`); switch to A and assert A's edits are still there
(41 rows, `1,105`); restore in A and assert 51 rows with A's seed values. The other consumers
(`grid-a11y.spec.ts:35-49,195-203`, `grid-keyboard.spec.ts:232-260`) only touch A, so they are
unaffected.

### Responses

R1-1: fixed in the "PC-F45: review 1 fixes" commit. The seeds are now distinguishable: every
note names its record (`note <id> v<generation> (<record>)`, through a `noteOf` helper that
`makeRows(record)` and the refresh both use, so a refresh keeps the marker). Ids, names and
values stay the same in both records, so no other spec changes (grid-a11y and grid-keyboard
assert no notes). The new test, renamed "each content binds its own rows and restores its own
seed, never the other's", now also asserts A's marker before and after its refresh
(`note 5 v0 (A)`, `note 5 v1 (A)`) and B's on its first bind (`note 5 v0 (B)`). After the existing
switches it presses Restore data in B (51 rows, row 0 is `Row 0` again, `note 5 v0 (B)`), switches
to A (41 rows, `1,105`, `note 5 v1 (A)`: B's restore left A alone) and restores in A (51 rows, qty
105, `note 5 v0 (A)`). Mutation checks, each against a production build of the mutated story
(`CI=1 node scripts/e2e.mjs --project chromium --workers 2 --grep "restores its own seed"
e2e/specs/grid-virtualization.spec.ts`), with the code restored afterwards:
- `restoreData` always binds `seeds.A`: fails on both attempts at B's restore, `note 5 v0 (B)`
  expected, `note 5 v0 (A)` received (`.tmp/f45-e2e-mut-ra.log`).
- `restoreData` always binds `seeds.B`: fails on both attempts at A's restore, `note 5 v0 (A)`
  expected, `note 5 v0 (B)` received (`.tmp/f45-e2e-mut-rb2.log`; the first attempt,
  `.tmp/f45-e2e-mut-rb.log`, timed out building in the web server under load and ran no test).
After restoring: grid-virtualization, grid-a11y and grid-keyboard in CI mode after a fresh
`build:showcase`, `--project chromium --project touch --workers 2`: 40 passed, none flaky
(`.tmp/f45-e2e-3.log`); eslint,
prettier `--check` and `tsc -p e2e` clean on the changed files.
