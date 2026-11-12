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
