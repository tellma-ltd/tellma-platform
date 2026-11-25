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

## Review 1

Reviewed commit 620d0d2b on the pc-f2 slot. Summary: 0 blocking, 0 major, 1 minor.

What I checked and found sound:

- **Root cause.** I agree with it. PC-126's `test-core-full` log is a plain full core run (no
  injection). There the test failed at 19,232 ms of 15,000. So the timeout is not caused by the
  injection, and D-278's lead does not apply here. The paste imports its chunk once per paste, and
  every read goes through the fake `TmApi`, so it never reaches a per-call `import()`.
- **(a) Exactness of `ɵtmPasteGroups`.** It holds, and I found no counterexample. The product
  calls it with one leaf function, `ɵtmPasteLeaf`, and one test, `length <= room && literals <= 512`.
  - Take a run `texts[start..start+s]`. Each of its sets (strings, numbers, ids, lefts, rights) is a
    subset of the same set for the run of size s+1.
  - Every term is added once its set is non-empty, and none is ever removed.
  - So both the text length and the literal count are non-decreasing in s, and `fits` is true for
    every size up to some point, then false for every size after it.
  - Labels that map to the same literal (`042` and `42`) or the same half only keep a set the same
    size. They never shrink it.
  - The search keeps two facts true: `fit` fits, and `over` does not fit (or is `left + 1`). Each
    probe lies strictly between them. While doubling, `fit < left` holds, because `fit == left`
    means `over - fit == 1`. The search stops at `over == fit + 1`.
  - Probes never pass `left`, and a run of one is never tested, as in the old loop.
  - The greedy groups are also the fewest possible contiguous groups, so the TSDoc's "as few as"
    is true.
  - The re-reads in halves after a full page take subsets of a group that already fit, so they
    fit too.
- **(b) Behaviour.** It is unchanged.
  - The groups are the same and come in the same order. The reads still go out from the same
    synchronous `Promise.all`.
  - The in-place `push` is correct: every insertion finishes before the first `decide`, and the
    array never leaves the map.
- **(c) The four-in-flight test.** It still proves the limit of four.
  - With a pool of five, all five `settle` calls start in the same synchronous `map`. The fifth
    query arrives right after the fourth and is held, and `tmSettle()` gives it five macrotask
    turns to show up. Then `expect(held).toHaveLength(4)` fails.
  - `most` is taken before the release. After the release, answers settle synchronously, so the
    count cannot rise again.
  - `servePaste` returns a superset of what the server would return for these flat ASCII tables:
    - any list against any top-level column, ignoring case;
    - an integer literal against `String(number)`;
    - the split term's left and right items as separate items.

    So it never drops a row the server would return. No spec that uses it depends on filtering
    by column. Those shapes are pinned by the leaf tests and the paste-leaf golden.
- **(d) The ceiling raise is honest under D-031.** I measured both sides myself:
  - `pnpm run build` then `size:check`: the chunk is 1.52 of 2, ok.
  - Re-bundling the built chunk with the old grouping, the old copy and without the new export:
    1.453 KB, so +0.07 KB.

  I looked for savings that would avoid the raise, by re-bundling variants of the built chunk:
  - dropping the test-only `ɵtmPasteGroups` export: 1.512;
  - a one-line `?.push(...) ?? set(...)` collection: 1.511;
  - both together: 1.502.

  None gets under 1.5 unless the export goes and the search is changed too. Plain bisection
  capped at `LITERALS` reaches 1.492, but it is not exact: for a plan with no text column,
  labels such as `1`, `01` and `001` share one literal, so a group can hold more than 512
  labels. The raise is therefore the honest measure.
- **(e) Note only.** The MessageChannel harness does spin the CPU. Each wrapped call posts
  messages back to back for the whole 300 ms, so the page's main thread stays busy. Concurrent
  waits add to each other, and other specs' timers and renders compete with them. Timeouts seen
  only in a full delayed run (PC-F86's guard spec at 4,769 ms, PC-F93) may be partly this load
  rather than delay. A wait on the real `setTimeout` taken before fakes install (as
  `tm-app.testing.util` keeps it in `realSetTimeout`) would give the same delay without the spin.
- **Tests I ran:**
  - `ng test core --include …/tm-entity-pickers.spec.ts`: 66/66.
  - The full core run (`ng test core`) twice under the real 300 ms delay (PC-F85-delay.py's
    form, the two core-tools ESLint rule files left out): 2454/2454 both times, with nothing
    failing or slow on the paste specs. The delay is reverted and the tree is clean.
  - The implementer's list of tests run matches PLAN.md §7.1 item 4.

### Findings

**R1-1 (minor): the four-in-flight test now waits for its fourth query with no deadline.**

- Where: `client/projects/core/tellma-core/entity/src/tm-entity-pickers.spec.ts:645-671`.
- What happens: `await four` resolves only when the fourth held query arrives. Suppose a change
  lowers the pool below four, or a paste sends fewer than four reads. The test then hangs until
  the runner's 15 s timeout, which reports "Test timed out" with no condition. The paste is
  still pending with its held queries when the next test starts.
- Why it matters: the project's helper for this exact case is `tmSettleUntil`. Its TSDoc
  describes this situation: a load through a lazy chunk that no number of turns bounds. It
  fails after `TM_WAIT_DEADLINE_MS` (4 s, more than ten times the 300 ms delayed chunk load),
  and the error names the condition.
- Suggested fix: replace `fourth`/`four` and `await four` with
  `await tmSettleUntil(() => held.length >= 4, 'four queries held')`. Keep the following
  `tmSettle()` and `expect(held).toHaveLength(4)`, so a pool of five still fails.

### Responses

**R1-1: fixed in the "PC-F85: review 1 fixes" commit.**

- The `fourth`/`four` promise is gone. The test now runs
  `await tmSettleUntil(() => held.length >= 4, 'four queries held')`. The wait's deadline is
  `TM_WAIT_DEADLINE_MS` (4 s), and its error names the condition.
- The `tmSettle()` and `expect(held).toHaveLength(4)` that follow are kept, so a pool of five
  still fails.
- Mutation check: I set `READS = 3`, ran the test, then restored 4. The test failed at 4,401 ms
  with "The condition did not hold within 4001 ms: four queries held", not with the runner's
  15 s timeout.
- `tm-entity-pickers.spec` alone: 66/66, with the test at 773 ms.
- One full `ng test core` under the real 300 ms delay: 2454/2454, with the test at 2,123 ms.
  The harness was `PC-F85-delay.py` with the two core-tools ESLint rule files left out, and it
  was reverted afterwards; the tree is clean.
- eslint and `tsc -p projects/core/tellma-core/tsconfig.json` pass on the spec.
