# PC-F85: Entity pickers paste-concurrency spec timeout under the import delay

## Implementer notes

### Root cause

`tm-entity-pickers.spec` "keeps at most four queries in flight however many a paste needs" spent
its time on CPU work, not on waits. The work came from the test's fake server.

- The paste is 3,000 codes. The resolver writes 12 groups: 11 of 256 labels and one of 184.
- The spec answered every query with `serve(table)`: all 3,000 rows, whatever the filter.
- The resolver matches each label of a group against each row its read returns, as an exact
  `Intl.Collator` comparison (spec 0029 §14.5). So the test made it compare 3,000 labels × 3,000
  rows, about 9 million pairs of two or three collator comparisons each.
- Measured alone, with temporary timers, in a 6,350 ms test: matching took about 5.6 s, the
  grouping 261 ms, and row conversion with the display-name cache 24 ms.
- A full core run adds load, and the test went over its 15 s budget. The scratch logs show it at
  15,522, 15,653, 16,703, 18,946 and 19,232 ms in PC-112, PC-114, PC-116, PC-121 and PC-126 runs.
  PC-126's log is a plain full core run, so the import delay is not needed for the failure.
- A real server answers only the rows the leaf asks for, about one per label. The product then
  compares about 3,000 × 256 pairs, about 0.42 s for this paste, in 12 slices between reads.

**The 300 ms import injection is not the cause here (the D-278 lead).** The paste resolver calls
`import('./tm-entity-paste')` once per paste. It then awaits `TmEntities.load()`, whose import ran
once at rig start. The test's reads go through the fake `TmApi`, so `tm-http-api`'s per-call
imports are never reached. The injection costs this test about 0.3–0.6 s, as it does every paste
spec: each one measures about 700 ms in a delayed full run, against about 50 ms alone without
the delay. That is not the 9 s gap. I did not change the injection, so this fix does not explain
PC-F86 or PC-F93. One observation for those items: the injection waits by posting
MessageChannel messages until 300 ms pass. That keeps the page's main thread busy for the whole
wait, on every wrapped call, so it adds CPU load to a full run as well as delay.

A secondary cost was in the product. `groupsOf` tried each label in turn by writing the group's
whole leaf again, `[...group, text]`. That made the grouping O(labels × group size). The
resolver's distinct-text map also copied each label's array to add one more, which is O(n²) for a
column that pastes one name in every row.

### What changed

- `client/projects/core/tellma-core/entity/src/tm-entity-paste.ts`
  - `groupsOf` is now `ɵtmPasteGroups`, exported for its test only. It is not re-exported from
    any entry point, so the API goldens do not change. Each group is the longest run of the
    remaining labels whose leaf fits.
    - It is found by doubling the run's size until a leaf does not fit, then halving the gap.
    - This is exact because a leaf only grows as labels join it: each list is a set that gains
      items, and a term appears once its list has any. So the sizes that fit are all those below
      the first that does not, and the groups are the ones the one-at-a-time loop made.
    - A group now costs O(g log g) leaf items in place of O(g²). Grouping this paste took 261 ms
      before and 22 ms after.
  - A repeated label is added to its text's array in place.
- `tm-entity-pickers.spec.ts`
  - New `servePaste(table)` responder. It answers a paste's read with the rows that have a value
    equal, ignoring case, to an item of the leaf's `in` lists. The answer holds every row the
    server's would, a superset that the client's exact match still decides. Unlike `serve`, a
    label missing from its group's leaf is now found nowhere, so these tests also check that
    every label reaches a leaf.
  - The four multi-query paste specs use it: 300 prefixed names, 600 codes, the failed-query
    split, and the four-in-flight test. The single-query specs keep `serve`, because their tables
    are small and their rows are chosen to exercise the client's match.
  - The four-in-flight test no longer waits with `vi.waitFor`, whose 1 s budget raced the chunk
    load. It awaits a promise that the fourth held query resolves. `tmSettle()` then still gives
    a fifth query the turns to start, so a pool of five still fails the test.
  - New: `groups the labels as taking them one at a time would…`. The one-at-a-time loop is
    kept in the spec as the reference. Over about 600 mixed labels (codes, halves, integers, ids,
    quotes, leading-zero integers whose literal another label already holds), two plans and four
    rooms (120 to 100,000, the last bounded only by the literal ceiling), the groups must be
    equal. Making the halving stop one short (`over - fit > 2`) fails it.
  - New: `resolves a name pasted in every row of a column with one query, the name in it once`.
- `client/projects/core/tellma-core/package.json`: the `tm-entity-paste` lazy-chunk ceiling is
  raised from 1.5 to 2 KB. A clean build measured 1.45 KB before and 1.52 KB after (+0.07 KB),
  rounded up to the next 0.5 KB (D-031), and the `./entity` budget note records it. The search
  loop was merged into one loop first to save bytes (1.54 to 1.52).

### Proof

The 300 ms injection was applied in this slot with a scratch harness, never committed:
`PC-F85-delay.py` in the shared scratch folder. It uses PC-112's delay form, wraps 32 call sites
in 22 core, core-ui and locale-ar files, and leaves out the two core-tools ESLint rule files. The
full core run is `ng test core`, 2,454 tests.

| Run (full core, 300 ms delay) | Result | Four-in-flight test |
|---|---|---|
| Before (HEAD code and spec) | 2452/2452 | 14,553 ms of 15,000 |
| After, run 1 | 2454/2454 | 2,131 ms |
| After, run 2 | 2454/2454 | 1,848 ms |
| After, run 3 | 2454/2454 | 1,816 ms |
| After the final loop merge, run 4 | 2454/2454 | 2,025 ms |
| After the final loop merge, run 5 | 2454/2454 | 1,464 ms |

Alone, without the delay, the test takes 616–756 ms (6,350 ms before).

### Open risks and what to examine hardest

- The monotonicity argument behind `ɵtmPasteGroups`. A future change to `ɵtmPasteLeaf` that can
  shorten a leaf, or lower its literals, as labels join would break the search silently. The
  equivalence spec is the guard, and it would fail.
- Matching stays O(labels × rows) per read (§14.5's exact collator match). For a real server's
  answers that is linear in the paste, with g ≤ 256. Only labels that each match many rows make
  it heavy: full pages, read again in halves. An index would need a collation key that `Intl`
  does not expose, and a case- or accent-folded key would change which rows match (contractions,
  ignorable characters). So the matching is unchanged.
- `servePaste` matches loosely on purpose: any list, any top-level column, ignoring case. Check
  that no spec using it depends on the server's column-specific filtering.
- No e2e covers this. Grid paste e2e uses the real clipboard, which fails on local Windows
  (PROCESS.md §11).
