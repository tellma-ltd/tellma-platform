# PC-F10: make the core lint-scope test independent of CI single-run inference (PC-065)

## Implementer notes

### Cause

CI run 42575574584 failed `pnpm run lint:test` on Ubuntu and Windows: in
`client/tools/tests/core-lint-scope.spec.mts`, the two tests that need the type-aware
`tm/no-decimal-operators` rule saw no findings at all.

The cause is not a build product. It is `CI=true`. The spec lints in-memory text under the names
of real source files (`ESLint.lintText(code, { filePath })`), so the text joins those files'
TypeScript projects. typescript-eslint 8 infers a one-shot CLI run when `CI === 'true'`
(`@typescript-eslint/typescript-estree` `inferSingleRun`). In that mode it builds each program
once from the files on disk and does not swap in the text `lintText` supplies. So the type-aware
rule judged the committed `tm-decimals.ts` and `tm-fake-telemetry.ts`, which hold no decimal
operator, and reported nothing. The other two tests passed only because of their order (corrected
after Review 1). `restrict-plus-operands` is type-aware too; its test passed because it was the
second `lintText` of `tm-decimals.ts`. In single-run mode typescript-estree counts parses per file,
treats a repeat as an autofix pass and builds an isolated program from the supplied text, so only
the first lint of each file (tests 1 and 4) saw the disk contents. The boundary rule needs no
types. Neither untyped nor type-aware checks are safe under single-run `lintText`; the fix removes
the order dependence along with the failure.

Reproduced in a fresh worktree after `pnpm install --frozen-lockfile`: the spec passes without
`CI` and fails 2 of 4 with `CI=true`, matching the CI log exactly.

### Fix

The spec's `ESLint` instance sets `parserOptions.disallowAutomaticSingleRunInference: true`
through `overrideConfig`, as an editor session does, with a comment saying why. The workspace
config itself is unchanged: single-run inference is the intended, faster behaviour for real CLI
lint runs, which lint files on disk.

Also, from the same suite under load: `tellma-client.spec.ts`'s bin-script test starts two Node
processes, and on a busy machine it exceeded vitest's 5 s default once while verifying this fix.
It now has a 60 s timeout, like the other process-spawning and program-building specs of
`@tellma/core-tools`.

### Verification

- Fresh Windows worktree: the spec passes with and without `CI=true`; full `lint:test` passes with
  `CI=true`.
- All client gates from the root of the fresh worktree, and the PR-tier e2e (535 passed).
- A `node:24-slim` Linux container with `CI=true`: clean frozen install, then `lint`, `typecheck`
  and `lint:test` (73/73).

### Open risk

`tools/tests/mcp-server.spec.mts` builds the docs in a 60 s `beforeAll` when
`components.json` is missing. Under heavy load it timed out once locally (and in Review 1 of
PC-065, on the parent commit too). This is pre-existing load sensitivity, not part of this fix.

## Review 1

### Findings

Verified on 4eb5789 (Windows, existing frozen install, typescript-estree 8.62.1):

- Root cause confirmed. A probe that replays the spec's four `lintText` calls against the
  parent's config finds no `tm/no-decimal-operators` under `CI=true` and finds them without
  `CI`. With the commit's `overrideConfig` and `CI=true`, the probe matches the run without `CI`.
  `inferSingleRun` returns true for `CI === 'true'`, unless `disallowAutomaticSingleRunInference`
  is set. Then `parseAndGenerateServices` takes the file's `SourceFile` from a `ts.createProgram`
  built from disk.
- The fixed spec passes with and without `CI=true`. The full `lint:test` passes with `CI=true`
  (20 files, 73 tests).
- No other spec needs the opt-out. `csp-only-in-csp-specs` and `tm-prefix-exports` lint
  in-memory text with the workspace config, but only under paths whose rules are not type-aware.
  `eslint/index.spec.ts` and `no-decimal-operators.spec.ts` use `projectService`, which opens
  the file with the supplied text. No workflow sets `TSESTREE_SINGLE_RUN`, which would override
  the opt-out.
- `pnpm run lint` with `CI=true` is clean. The workspace config is unchanged, and CLI runs
  already used single-run mode before this commit: the pnpm bin path
  `node_modules/eslint/bin/eslint.js` triggers it, so type-aware rules still check the
  committed files.
- The 60 s timeout is warranted. The test runs two `execFileSync` Node processes while 20 spec
  files run in parallel, and other specs that spawn processes or build programs use 60 to 120 s.

1. **R1-1 (minor)**: the commit subject contradicts its body and the fix item.
   - Where: commit 4eb5789, subject line, and `plans/platform-core/reviews/PC-F10.md:1`.
   - Problem: the subject says "independent of local build state". The body says "The cause was
     CI=true, not a missing build product", and the fix item is "independent of CI single-run
     inference". A later reader of `git log --oneline` would look for a build-order fault.
   - Fix: when this item merges, reword the subject in the merge message to name CI single-run
     inference (or reword the commit before it merges, if the process allows that). Retitle the
     review file's heading the same way.
2. **R1-2 (minor)**: the implementer notes misdescribe why the other two tests passed.
   - Where: `plans/platform-core/reviews/PC-F10.md:17-18`.
   - Problem: the notes call `restrict-plus-operands` an untyped rule "on the parsed text". It
     is type-aware.
     - It passed under `CI=true` because it is the second `lintText` of `tm-decimals.ts`.
       typescript-estree counts calls per file in single-run mode, treats a repeat as an
       autofix pass, and builds an isolated program from the supplied text
       (`parser.js`, the `parseAndGenerateServicesCalls[...] > 1` branch).
     - The first lint of each file got the disk `SourceFile`, which is why exactly tests 1 and 4
       failed.
     - So under the parent, which tests passed depended on their order. The fix removes that
       dependence too.
   - Fix: correct the sentence so a later reader does not conclude that untyped or
     plus-operand checks are safe under single-run `lintText`.

### Responses

- R1-1: fixed in the review 1 fixes commit. The review file's heading names the cause (CI
  single-run inference). Commit 4eb5789 cannot be amended, so the fix commit's subject names it
  ("PC-F10: review 1 fixes (CI single-run inference)"), and its body corrects commit 1's subject.
- R1-2: fixed in the review 1 fixes commit. The notes now say that `restrict-plus-operands` is
  type-aware and passed only as the second `lintText` of its file (typescript-estree's repeat-parse
  branch builds a program from the supplied text), so the parent's passing tests depended on order.
