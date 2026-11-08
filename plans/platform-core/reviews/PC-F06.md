# PC-F06: Isolate the ICU CSP test so its policy cannot leak (PC-008)

## Implementer notes

### Root cause

`@angular/build:unit-test` runs vitest with `isolate: false`: every spec file of a run shares one
browser page. `i18n/tm-icu-csp.spec.ts` inserts a Content Security Policy `<meta>`
(`script-src 'self'`, Trusted Types required). A policy applied that way cannot be lifted, so
every file that ran after it in the same page and needed Angular's JIT (TestBed compiling its
dynamic test module calls `new Function`) failed with `EvalError ... 'unsafe-eval'`. File order
differs between machines, which is why it passed locally and failed in CI (runs 42509964842 and
42510050135, on `tm-control-size.spec.ts`).

### Fix

- The spec is renamed `i18n/tm-icu.csp.spec.ts`. The `*.csp.spec.ts` suffix marks a suite that
  must run alone.
- `angular.json` (core-ui): the `test` target excludes `**/*.csp.spec.ts`; a new `test-csp`
  target (same builder, tsconfig and browser) includes only that suffix. It is a separate vitest
  run with its own browser page, so the policy reaches no other suite.
- `scripts/test.mjs` runs `test` and then every `test-<name>` target of a project, each as its
  own `ng run`, so `pnpm run test` (CI's command) covers the isolated suite without listing it.
- The test is unchanged in substance: it applies the web host's policy, asserts `new Function`
  throws (proving the policy is live), then formats a plural and a select in en and ar through
  the real Transloco path. Its header comment now explains the isolation.
- READMEs (client workspace, core-ui authoring conventions) document the suffix.

### Proof

A scratch vitest `runnerConfig` (gitignored, `.artifacts/pc-f06/`) with a sequencer that forces
CSP files first:
- Before the fix, the full core-ui suite in that order: 27 tests failed with the same `EvalError`
  in every file that compiled a TestBed module after the policy (icon registry, grid, …).
- After the fix, the same sequencer on `test`: the CSP file is no longer in the run (order log),
  64 files / 1327 tests pass. `ng run core-ui:test-csp`: 1 file / 1 test passes.

### Gates

lint, typecheck, lint:test, tokens:check, build, test (core-ui 64 files / 1327 tests, then
core-ui:test-csp 1 / 1, tokens, locale-ar, showcase), api:check, docs:build, size:check: all
green. PR-tier e2e (chromium + touch): 465 / 478. The 13 failures are 30 s timeouts in
`page.goto` / `beforeEach` of the first specs while the dev server warmed up (alert, button,
grid-keyboard) and the known entity-picker axe "popup states" timeout, on a machine at ~85-100 %
CPU from parallel slots. The change touches no showcase or library code, so e2e cannot be
affected by it. Re-running those specs was not possible: the showcase build's esbuild service
kept stopping ("The service was stopped") under the same load, in both the dev and the
production paths, even after clearing `.angular/cache`.

### Risks / reviewers

- Any future spec that installs a page-wide irreversible change (CSP, Trusted Types default
  policy, a frozen global) must use the suffix; nothing enforces it beyond the convention.
- `test.mjs` now invokes `ng run <project>:<target>` instead of `ng test <project>`; the options
  are the same.
