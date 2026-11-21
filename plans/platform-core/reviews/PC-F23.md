# PC-F23: API goldens print unions in a build-path-dependent order

## Implementer notes

### Root cause

- TypeScript stores a union's members sorted by internal type id. A type gets its id when the
  checker first creates it, so the id order follows the order in which files are checked.
- The declaration emitter copies a written type node when it can, which keeps the author's order.
  It prints an inferred type from the checker's type, which uses id order. The affected members are
  inferred: `rowActivation = input<'none' | 'row'>('none')` emits
  `InputSignal<"row" | "none">` on a clean build. The source order is `'none' | 'row'`, so the
  printed order is not the source order either.
- A warm `client/.angular/cache` lets ng-packagr re-check only the files that changed, in a
  different order. That changes which literal types are created first, and so the printed order.
- Reproduced in this slot. A one-line edit to `grid/tm-grid-base.ts`, then `ng build core-ui` with
  the warm cache, made `dist/core-ui/types/tellma-core-ui-grid.d.ts` print
  `InputSignal<"none" | "row">`. The committed golden, from a clean build like CI's, reads
  `"row" | "none"`. That is the flip PC-048 approved in f6d0487
  (`.tmp/pcf23-evidence-single-build.txt`).
- Non-literal members can flip in the same way: interfaces, classes and instantiated generics also
  get their ids on first use. Keyword types (`string`, `undefined`) have fixed ids.

### Choice: sort every union in the report, on both paths

`client/tools/api/union-order.mjs` (`normalizeUnionOrder`) runs in `scripts/api-extractor.mjs`
after the ɵ filter. It runs on the check path and the approve path, so a golden can only be written
in canonical form and is only ever compared with canonical form.

- **How it works.** It parses the report's ```ts fence with the workspace's TypeScript, and every
  `UnionTypeNode` gets its members in a canonical order. Inner unions are sorted first, and outer
  unions compare members by their canonical text.
  - Order: literals, then keyword types, then named types, then structural types (functions,
    object literals, arrays), then `null`, then `undefined`.
  - Within a rank, number literals sort by value and everything else by UTF-16 code units, so no
    locale setting can change the order.
- **What it leaves as written.**
  - A union already in canonical order keeps its text, line breaks included.
  - A union with comments between its members keeps its order. Only a type the emitter copied from
    source can carry comments (TmTimeStyle in l10n), and a copied type keeps the author's order,
    which is stable.
  - Intersections are never touched.
- **Fails loudly.** The fence is checked for syntax errors with `ts.transpileModule` first. A
  report that does not parse throws, rather than being rewritten wrongly.
- **Why every union, not only literal ones.** Every member that is not a keyword type can flip by
  type id. Sorting only literal unions would leave named-type unions able to drift later without
  warning. The cost is a one-time reorder of 143 lines across 29 goldens. Every change is a union
  reorder: the normalized HEAD goldens were checked byte for byte against a fresh approve.
- **Rejected: (b) a deterministic emit.** Deleting `.angular/cache` before every `api:*` run, or
  disabling the cache for library builds, slows every build. It also leaves the order a function
  of the program's file order, so an unrelated new file that mentions `"none"` first could still
  flip a golden. A rule outside the script (always build clean before approving) is the kind of
  thing that already failed twice.
- **Rejected: writing the types out at the source** (annotating the inputs). Every future inferred
  union would need the same care, and nothing would catch a missing one.

### Tests

`client/tools/tests/union-order.spec.mts` runs in `lint:test`, 16 tests:

- both emitted orders of `rowActivation` give one text, and all 24 permutations of a four-member
  union give one text;
- the rank order and the numeric order;
- nested unions;
- canonical unions kept with their formatting, comment-bearing unions and intersections untouched,
  and string literals containing `|` untouched;
- comments and blank lines between declarations kept, and a parse error thrown;
- fence-only rewriting, idempotence;
- every committed golden already canonical. This guard needs no build: a golden approved without
  the sort, or edited by hand, fails `lint:test`.

The golden guard failed on the pre-fix goldens (`.tmp/pcf23-unit1.log`: core-testing.api.md first)
and passes on the normalized ones.

### Evidence

All runs are in this slot, with logs in `.tmp/pcf23-*`.

1. At HEAD, after a one-line edit and a single-project `ng build core-ui` on the warm cache, the
   d.ts printed `"none" | "row"`.
2. `api:approve` with the fix, run on that flipped dist, gave goldens byte-identical to the HEAD
   goldens put through the normalizer. The flipped and clean builds therefore differ only in union
   order, and the sort removes the difference.
3. Warm cache: the edit was reverted, then two `pnpm run build` runs were made on the warm cache.
   `api:check` passed, and `api:approve` gave no diff (`.tmp/pcf23-warm-proof.log`).
4. Clean: `client/.angular/cache` was moved to `.tmp/pcf23-ngcache-moved`, then `pnpm run build`
   ran. `api:check` passed, and `api:approve` gave no diff (`.tmp/pcf23-clean-proof.log`). The raw
   extractor report printed `"row" | "none"` here, and `"none" | "row"` in step 1.

### Notes for the orchestrator and reviewers

- **PC-023 (chips) is not integrated yet.** Its chip `kind` golden must be regenerated with
  `api:approve` after it merges this. Approving from any build path is now safe.
- Examine hardest:
  - the comment-bearing exemption (`hasCommentsBetweenMembers`): can an emitted, id-ordered union
    ever carry a comment?
  - the `renderChildren` splice: text between child nodes is copied as written, and only union
    spans are rebuilt.
- No runtime code changed, so e2e was not run.

### Gates

All run in this slot after the final clean build: tokens:build-css, build, lint, typecheck,
lint:test (106 tests), size:check, api:check (warm and clean) and test passed. Logs are in
`.tmp/pcf23-gate-*`.

## Review 1

### Findings

**R1-1 (major): comments on the same line as a union's members are deleted, or end up on another member.**
`client/tools/api/union-order.mjs:180-189` (`hasCommentsBetweenMembers`), used at `:199`.
- **Cause.** `ts.getLeadingCommentRanges(text, pos)` at a non-zero `pos` only collects comments
  that come after a line break. A comment on the same line as the `|` or as the previous member
  counts as a trailing comment there, so the exemption never sees it. The union is then rebuilt
  from its members' text joined by `" | "`, and the comment in the text between members is dropped.
- **Reproduced** (`.tmp/r1-pcf23-harness/adv2.mjs`, `.tmp/r1-pcf23-adv2.log`):
  - `"b" /* c */ | "a"` gives `"a" | "b"`, and the comment is gone.
  - The same happens to `"b" | /* c */ "a"`, `"b" | // c` + newline + `"a"`, and the leading-pipe
    layout `| "b"` / `| // c` / `"a"`.
  - `/** x */ 'short' | /** y */ 'medium' | 'long'` gives `/** x */ 'long' | 'medium' | 'short'`.
    `/** y */` is deleted, and the doc for `'short'` now sits on `'long'`.
  - Only the own-line layout in the test (`union-order.spec.mts:90-100`) is caught.
- **Failure scenario.** A copied-from-source union carries inline comments and is not in canonical
  order, for example `'legacy' /* deprecated */ | 'auto'`. The d.ts emitter keeps comments inside
  copied types, as TmTimeStyle shows. The golden silently loses that comment or moves it to the
  wrong member, and a later edit to the comment never shows as drift. This breaks the module's
  stated rule that a union with a comment between its members keeps its written order.
- **Suggested fix.** Decide from the gap text rather than from comment ranges:
  - For each `i > 0`, require `source.slice(types[i - 1].end, types[i].getStart(sourceFile))` to
    match `/^\s*\|\s*$/`.
  - For the first member, require `source.slice(union.pos, types[0].getStart(sourceFile))` to match
    `/^\s*\|?\s*$/`. This also catches `= /* c */ "b" | "a"`.
  - Add tests for same-line block, line and JSDoc comments on both sides of `|`.

**R1-2 (minor): the canonical text of a multi-line union depends on the input order.**
`union-order.mjs:199-207`.
- **Cause.** A union already in order keeps its written separators. A union out of order is
  rebuilt with `" | "`.
- **Reproduced** (`.tmp/r1-pcf23-adv.log`, "multiline perms equal: false"): `type T =` followed by
  the lines `| "a"` and `| "b"` stays multi-line. The same type with the lines `| "b"` and `| "a"`
  becomes `type T =` + newline + `"a" | "b";`.
- **Failure scenario.** The output is not a function of the member set. That contradicts the
  module header ("the same public API always yields the same golden"). It is harmless today,
  because the order-unstable unions are inferred ones, which the printer emits on one line with
  `" | "`. It would bite if a TypeScript or API Extractor upgrade ever wraps long inferred unions.
- **Suggested fix.** Reuse the written separators by position: put the i-th original separator
  text between the i-th and (i+1)-th sorted members, and keep any leading `|`. Add a permutation
  test on a multi-line union.

**R1-3 (minor): the comparator is not a total order when bigint and number literals are mixed.**
`union-order.mjs:116-127`.
- **Cause.** In rank 0, two number literals compare by value, and every other pair compares by
  text. That gives a cycle: 2 < 10 by value, `10` < `1n` by text, and `1n` < `2` by text.
- **Reproduced** (bigintMix1-3 in `.tmp/r1-pcf23-adv.log`): `2 | 10 | 1n`, `1n | 2 | 10` and
  `10 | 1n | 2` each come back unchanged. That is three canonical forms for one set.
- **Scope.** This is the only gap. For every other literal kind, the number texts form one
  contiguous block in code-unit order: quotes sort before digits and `-`, and backtick, `t` and `f`
  sort after them.
- **Suggested fix.** Give number and bigint literals one sub-rank within rank 0 and compare them
  by value (for a bigint, `BigInt` of its text without the `n`). Add a permutation test.

**R1-4 (minor): `normalizeUnionOrder` silently returns a CRLF report unsorted.**
`union-order.mjs:242-246`.
- **Cause.** `lines.indexOf('```ts')` does not match a line that ends in `\r`. The report takes the
  "no fence" path and comes back unchanged, with no error.
- **Why it is safe today.** `stripPrivateByConvention` converts to LF first. The golden test does
  `replaceAll('\r\n', '\n')` before normalizing, and the TSDoc states the LF precondition.
- **Failure scenario.** The two calls in `api-extractor.mjs` get reordered, or a new caller passes
  a Windows checkout's golden. The sort is then a silent no-op, and the order flips come back on
  Windows only.
- **Suggested fix.** Convert `\r\n` to `\n` at the top of `normalizeUnionOrder`, or throw when the
  input contains `\r`. Add a CRLF test.

**R1-5 (minor): the spec leaves several risky shapes untested.**
`client/tools/tests/union-order.spec.mts`.
- **Shapes with no test.** The harness (`.tmp/r1-pcf23-harness/adv.mjs`) shows these all
  normalize correctly today, but none of them is pinned by a test:
  - parenthesized unions and arrays of them;
  - unions inside conditional types, mapped types (including the `as` clause), template literals,
    tuples, indexed-access types, `infer … extends`, and `import()` type arguments;
  - an intersection whose operands are unions (the operands are sorted and the `&` order is kept);
  - `undefined | null`;
  - the rewrite of a multi-line union with a leading `|`.
- **Failure scenario.** A refactor of `render`, `renderChildren` or `rankOf` could break any of
  these without a failing test.
- **Suggested fix.** Add a table-driven test over these shapes, together with the cases from
  R1-1 to R1-4.

### Verification done

- **Gates.** In `client/`, on 87db466, lint, typecheck and lint:test (24 files, 106 tests) passed.
  Logs: `.tmp/r1-pcf23-{lint,typecheck,linttest}.log`.
- **Clean build.** I moved `client/.angular/cache` to `.tmp/r1-pcf23-ngcache`, then ran
  `pnpm run build` and `api:check`. All 45 goldens were ok, with exit 0. The raw dist printed
  `InputSignal<"row" | "none">`. Logs: `.tmp/r1-pcf23-clean-{build,check}.log`.
- **Warm-cache flip.** I added one comment line above `rowActivation` in `grid/tm-grid-base.ts`
  and ran `ng build core-ui` on the warm cache. The dist printed `InputSignal<"none" | "row">`, so
  the flip reproduced. `api:check` on that dist passed: 45 ok, exit 0
  (`.tmp/r1-pcf23-warm-check.log`).
- **Mutation.** I made three changes in one run, then ran `api:check` once
  (`.tmp/r1-pcf23-mut-check.log`, exit 1):
  - **Added member.** In source, `rowActivation` became `input<'none' | 'row' | 'cell'>`, with a cast
    at the one assignment so it compiles, and I rebuilt core-ui. Result: API DRIFT on
    `core-ui.grid.api.md`.
  - **Removed member.** In the dist, I removed `'changed'` from `TmSessionCheck`. Result: API DRIFT
    on `core.api.md`.
  - **Reorder only.** In the dist, I swapped `'light' | 'dark'` in core-ui-tokens. Result:
    `ok core-ui-tokens.api.md`.
  - **No other drift.** No other report drifted, so the normaliser hides only reorders.
  - **Restore.** I put the source back and rebuilt. The full warm rebuild again printed
    `"none" | "row"`, which is a second instance of the flip.
- **Golden churn** (`.tmp/r1-pcf23-harness/churn.mjs`, `.tmp/r1-pcf23-churn.log`). For all 29
  changed goldens:
  - `normalizeUnionOrder(golden at cd1ab64)` is identical to the golden at 87db466;
  - the text outside the fence is byte-identical;
  - the multiset of parse-tree leaf tokens inside the fence is identical, so nothing was added,
    removed or rewritten.
  - `git diff -w` shows the 143 changed lines.
  - All 45 HEAD goldens are fixed points of the normalizer.
- **CI.** `lint:test`, which holds the canonical-golden guard, runs in both client jobs (Linux and
  Windows) in `.github/workflows/ci.yml`.
- **Determinism.**
  - The sort compares code units and numeric values, with no `localeCompare`.
  - `Array.prototype.sort` is stable.
  - The parser is the workspace TypeScript, resolved from `client/package.json`.
  - Paths are built with `node:path` and `node:url`.
  - API Extractor analyses with its bundled TS 5.9.3, while the parser is TS 6.0.3. That gap is
    harmless, because the parser only re-reads the emitted text.
- **AGENTS.md.**
  - TSDoc is present on every declaration, and the inline comments are present.
  - `client/README.md` documents the sort. `client/tools/` is not a project, so it needs no README
    of its own.
  - There are no spec references or PC ids in the new code or text.

### Responses

All five fixed in the review 1 fix commit (sha in the commit log; it follows 87db466). The 25 new
tests (41 in all) fail 14 ways on the 87db466 normalizer (`.tmp/pcf23-int-unit-oldimpl.log`) and
pass now. Every committed golden stays a fixed point, so no golden changed.

- R1-1: fixed. `hasCommentsBetweenMembers` now reads the gap text. The text from the end of the
  preceding token to the first member must match `^\s*\|?\s*$`, and each text between members
  must match `^\s*\|\s*$`; anything else (in a parsed union, only a comment) freezes the order.
  Tests cover block, line and JSDoc comments on the same line on both sides of `|`, a comment
  before the first member, and one after a leading `|`, besides the own-line layout.
- R1-2: fixed. A union is always rebuilt by position: the leading `|` (if any), then the i-th
  sorted member after the separator written before the i-th member. A union already in canonical
  order comes out byte for byte as written, and the result depends only on the member set and
  the layout. Tests: every permutation of a multi-line leading-`|` union gives one text, and
  mixed separators stay in place.
- R1-3: fixed. Literals (rank 0) have sub-ranks: string literals, then number and bigint
  literals as one block compared by exact value (`<` across number and bigint; a bigint's text is
  read with `BigInt`, which keeps hex and octal prefixes), then `true`, `false` and template
  literals. Equal values (`1 | 1n`) fall back to the text. Tests: every permutation of
  `2 | 10 | 1n | -3n | 0x10` gives `-3n | 1n | 2 | 10 | 0x10`, and the sub-rank order.
- R1-4: fixed. `normalizeUnionOrder` converts `\r\n` (and a lone `\r`) to `\n` first and
  returns LF. Test: a CRLF report comes back sorted with LF endings.
- R1-5: fixed. A table-driven test pins the shapes listed: parenthesized unions and arrays of
  them, conditional types, mapped types with an `as` clause, template literals, tuples, indexed
  access, `infer … extends`, `import()` type arguments, intersections of unions (operands sorted,
  `&` order kept), `undefined | null`, and the multi-line leading-`|` rewrite.
- Also: the golden fixed-point test now has a 60 s timeout. On a loaded machine it took 13 s
  against vitest's default 5 s.
