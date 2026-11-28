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

## Review 1

Verified on ce1f544 (Windows, `pnpm run tokens:build-css` first; scratch configs in the r1-PC-F06
scratchpad, no repo file touched except this one):

- (a) Leak. Old behaviour reproduced without editing `angular.json`: `ng run core-ui:test
  --exclude=__none__` (CSP file back in the run) with a CSP-first vitest sequencer as
  `--runner-config` fails 2 files with the same `EvalError ... 'unsafe-eval'` (tm-select.spec.ts
  failed to load, tm-icu.spec.ts 2 tests), exit 1. With the commit's config: CSP-first sequencer
  64/64 files, 1327/1327 tests; `sequence.shuffle.files` seeds 7 and 4242 both 64/1327 green.
- (b) CI. `ci.yml` client job runs `pnpm run test` → `scripts/test.mjs`, which now runs
  `core-ui:test` then `core-ui:test-csp`; the full script run here printed both labels and
  passed. A non-zero `ng run` status still exits the script with that status, as before, so a
  failing CSP test fails the job. Linux/Windows: plain `spawnSync(process.execPath, …)` and
  project-relative globs; no path separators involved.
- (c) Proof. The spec still asserts `new Function` throws after the `<meta>` lands, and
  `tm-icu.ts` parses lazily at format time (its `parsed` cache is empty in a run of its own), so
  an engine that compiled to JavaScript would throw inside the assertions.
- (d) Counts. `--list-tests`: `test` 64 files, `test-csp` 1 file (`i18n/tm-icu.csp.spec.ts`),
  old default include 65 files = every `*.spec.ts` file on disk under core-ui. Nothing else
  dropped.
- (e) Docs. client/README.md and the core-ui README authoring conventions describe the suffix;
  CONTRIBUTING.md lists `pnpm run test`, which covers the new target, so it needs no change.
- e2e. Only core-ui's test targets in `angular.json`, `scripts/test.mjs`, one spec and READMEs
  change; the showcase targets, library sources and e2e scripts are untouched, so the PR-tier
  e2e cannot be affected. The 465/478 local result is not evidence against this commit.

### Findings

R1-1 (minor) — `client/scripts/test.mjs:13-14` and `:55-58`. The doc comment says each
`test-<name>` target runs "after `test`" and the inline comment says "`test` first", but the
filter keeps `Object.keys(project.architect)` order, so a `test-*` target declared above `test` in
`angular.json` runs before it. Harmless today (separate runs), but the comment states an ordering
the code does not enforce. Fix: build the list as
`['test', ...Object.keys(project.architect).filter((t) => t.startsWith('test-'))]`, or reword
both comments to "in declaration order".

R1-2 (minor) — `client/projects/core/tellma-core-ui/README.md:119-122`, `client/eslint.config.mjs`.
The suffix is a convention only (the implementer lists this as a risk). A future spec that
installs a CSP `<meta>` without the suffix reintroduces exactly this failure: order-dependent,
green on one machine and red in CI, blamed on an unrelated file. Fix: a cheap guard, e.g. an
ESLint `no-restricted-syntax` entry for `**/*.spec.ts` with `ignores: ['**/*.csp.spec.ts']` that
flags the literal `'Content-Security-Policy'`, with a message pointing at the suffix. The config
already uses `no-restricted-syntax` blocks scoped by file globs.

### Responses

R1-1: fixed in ed4aa7e. `scripts/test.mjs` builds the list as `['test',
...test-* targets]`, so `test` always runs first whatever the declaration order; both comments
now say so ("in declaration order among themselves" for the `test-*` targets).

R1-2: fixed in ed4aa7e, as an ESLint guard on `projects/**/*.spec.ts`. It is a
small custom rule, `tm/csp-only-in-csp-specs` (`tools/eslint/csp-only-in-csp-specs.mjs`), rather
than a `no-restricted-syntax` entry. Flat config replaces that rule's options per block, and the
grid-engine, calendar and l10n boundary blocks already set `no-restricted-syntax` for their own
spec files: a later entry would have silently dropped their DI-free selector, and an earlier one
would have silently missed those folders. The rule flags any string or template literal
containing `Content-Security-Policy` (case-insensitive) outside `*.csp.spec.ts`, with a message
pointing at the suffix. `tools/tests/csp-only-in-csp-specs.spec.mts` holds the RuleTester vectors
and scope tests through the real config (including grid-engine and l10n specs).
