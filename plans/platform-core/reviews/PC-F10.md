# PC-F10: make the core lint-scope test independent of local build state (PC-065)

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
operator, and reported nothing. The untyped rules (the boundary rule, `restrict-plus-operands` on
the parsed text) were unaffected, which is why the other two tests passed.

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
