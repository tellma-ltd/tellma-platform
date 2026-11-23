# PC-F73: client tools lint:test tm-prefix-exports spec: root-cause the 5 s timeout under parallel load

## Implementer notes

### The failure and its cause

PC-108's run (`PC-108-linttest.log`) failed `tm/prefix-exports > accepts prefixed and allowlisted
exports, rejects the rest` at 5574 ms against the 5 s default. That run was extremely loaded: the
summed `import` time was 846 s (about 190 s on a quiet run here).

The failing test is a single `it` that calls `RuleTester.run` with 19 cases. ESLint's RuleTester
looks for global `describe` and `it`, finds none (Vitest exports them, `globals` is off), and falls
back to running every case synchronously inside the caller's test. So one test carried:

- the worker process's first parse, first lint and first rule-option schema compile, and
- 18 more full RuleTester cases (fresh FlatConfigArray, ajv schema check, parse, scope analysis,
  verify, AST-unchanged check).

There is no fixture build, TypeScript program or type checker in this spec; nothing is set up per
case beyond what RuleTester itself does per case.

Measurements alone (probes `PC-F73-cold.mjs`, `PC-F73-prof.mjs` and a temporary per-case probe spec,
all in the shared scratchpad):

| Cost | Alone | Under `ng test core-ui` | 19 workers on 1 core |
|---|---|---|---|
| The whole RuleTester test (19 cases) | 120-256 ms | 496 ms | 13.3 s (failed) |
| Its first case (cold) | 50-57 ms | 123 ms | 2.5 s |
| Each later case | about 3.3 ms (1.9 ms once JIT-warm) | 8.9 ms | 404 ms mean, 1.7 s max |

The cold first case is JIT and first-use initialisation, not module loading: no module is
`require`d during it. A CPU profile of a cold first case puts it in ESLint (67 ms of samples), ajv
compiling the rule's option schema (63), typescript (42), typescript-estree's converter (38) and
scope-manager (13); with the parser and linter already warm, the first RuleTester case is still
about 50 ms, half of it ajv. The parser's default `lib` (scope-manager's implicit lib globals)
adds about 0.3 ms a case once warm: not worth a deviation from the workspace's parser setup.

The rest of the file: `import` 2.2-2.7 s (eslint, typescript-eslint, vitest; no timeout applies),
and the scope test's first `lintText` 0.9-1.7 s, which loads the workspace `eslint.config.mjs`
once per file and already has a 60 s budget. That one is inherent: one config load per file,
shared by both scope tests.

### Reproduction

`PC-F73-affinity.cmd` pins the `tools` project (19 workers) to one core. That gives a summed
import time of 769 s, close to PC-108's 846 s, and reproduces the failure: the target test took
13.3 s and failed, and so did seven other RuleTester tests of the same shape (below). A 60-thread
spinner (`PC-F73-spin.mjs`) and a parallel `ng test core-ui` slow the test only 2.5-4x and do not
reproduce it.

### The fix

`client/projects/core/tellma-core-tools/fixtures/rule-tester.ts` adds `createRuleTester(config)`.
It hands RuleTester Vitest's `describe`, `it` and `it.only` (RuleTester's documented hooks for a
test runner), so `run` registers a describe block per rule and one test per case. (Since fix 1
the hooks sit on a private subclass, so ESLint's own class is never changed; see R1-1.) Each
test then holds one case's work, and the first case in a file holds the cold cost alone. A
failure now names the case. The specs call `run` inside a `describe` (it was inside an `it`), since Vitest refuses a
test declared inside another test.

No timeout was raised, and there are no retries or quarantine. Total work is unchanged (RuleTester's
per-case work is inherent to it). What changes is that no test carries 19 cases plus the cold start.

### Sweep of the other lint:test specs

Every spec file was run alone (`PC-F73-sweep.mjs`, `PC-F73-sweep-before.json`). The same pattern,
every RuleTester case of a `run` in one `it` with the default 5 s, is in all ten RuleTester specs,
so all ten use the helper now. Each test's alone time is below 1 s, but seven of them failed in the
one-core reproduction, so leaving them would leave the same flake in place:

| Spec | Before: tests, max alone | After: tests, max alone | 1 core before | 1 core after (max) |
|---|---|---|---|---|
| tools/tests/tm-prefix-exports | 1, 141 ms | 19, 41 ms | 13.3 s, failed | 2.1 s |
| tools/tests/csp-only-in-csp-specs | 1, 90 ms | 6, 45 ms | 10.1 s, failed | 1.5 s |
| tools/tests/no-platform-sniffing | 1, 291 ms | 22, 44 ms | 12.9 s, failed | 1.8 s |
| tools/tests/no-state-bindings-on-form-field | 1, 97 ms | 7, 47 ms | 2.6 s | 0.5 s |
| core-tools eslint/rules/core-import-boundary | 5, 116 ms | 49, 43 ms | 5.9 s, failed | 2.0 s |
| core-tools eslint/rules/lazy-layouts | 1, 177 ms | 26, 48 ms | 12.5 s, failed | 1.7 s |
| core-tools eslint/rules/literal-declaration-keys | 1, 136 ms | 18, 47 ms | 8.3 s, failed | 0.9 s |
| core-tools eslint/rules/no-raw-http | 2, 84 ms | 19, 45 ms | 6.6 s, failed | 1.7 s |
| core-tools eslint/rules/no-web-storage | 1, 86 ms | 9, 43 ms | 2.7 s | 0.6 s |
| core-tools eslint/rules/no-decimal-operators (120 s budget) | 3, 738 ms | 22, 608 ms | 60.1 s | 35.8 s |

no-decimal-operators is type-aware: its first case builds the project-service program, which is
inherent and already has a 120 s describe budget. Its cases are now named after their bodies, not
the shared import header.

The other tests whose alone time is over about 1 s are each one shared or deliberate setup, not
per-case setup, and each already has a raised budget:

- core-lint-scope (10.0 s; 300 s), no-platform-sniffing scope (4.5 s; 120 s), csp and tm-prefix
  scopes (0.9-1.0 s; 60 s): one workspace ESLint config load per file and, where typed, one
  TypeScript program, shared by the file's tests.
- extract-components (2.4 s; 120 s): one ts-morph project of the library sources.
- build.spec config-check cases (3.7 s and 3.6 s; 120 s): each starts the real config-check
  process on different emitted data, which is the point of those two cases.
- eslint/index.spec "reads the prefix…" (3.3 s; 120 s) and tellma-client bin (1.1 s; 60 s): a fresh
  Node process each, so that cwd and the bin script are exercised for real.
- union-order golden check (0.46 s; 60 s): one transpile and parse per committed golden. Each golden
  is its own source, so this is not the shared-source fix PC-F69 applied.

### lint:test runs (full, `pnpm run lint:test`)

| Run | Result | Duration | RuleTester max test | tm-prefix-exports rule tests |
|---|---|---|---|---|
| Before, alone | 264 passed (probe included) | 125.7 s | 492 ms | 1 test, 404 ms |
| Before, beside `ng test core-ui` | 264 passed | 126.5 s | 506 ms | 1 test, 496 ms |
| After, alone #1 | 443 passed | 80.7 s | 216 ms | 19 tests, max 170 ms |
| After, alone #2 | 443 passed | 79.4 s | 193 ms | 19 tests, max 172 ms |
| After, beside `ng test core-ui` then `ng test core` | 443 passed | 92.1 s | 366 ms | 19 tests, max 154 ms |
| Before, `tools` project on 1 core (19 workers) | 11 failed / 253 | 140.7 s | 13.3 s | 1 test, 13.3 s, failed |
| After, `tools` project on 1 core (19 workers) | 2 failed / 432 | 99.7 s | 2.1 s | 19 tests, max 2.1 s |

The test count grows from 262 to 443 because RuleTester cases are now tests. The duration
differences between runs come mostly from the fixture build and the machine's other work, not from
this change. Logs and JSON reports are `PC-F73-*.log` / `PC-F73-*.json` in the shared scratchpad.

### Open risks

- **The one-core stress still fails tests that are not this pattern.** After the fix it failed
  union-order's two permutation tests (27-29 ms alone; 10.3 s and 8.5 s there; they passed in the
  "before" one-core run). The "before" run also failed serve.spec "serves on the --port given…"
  (76 ms alone), app-project.spec "finds a package in node_modules…" (38 ms alone) and
  extract-examples (544 ms alone, 62.9 s against its 60 s). On one core with 19 workers, a test of
  a few tens of milliseconds can be descheduled for seconds, so no per-test fix absorbs that stress.
  It is harsher than the PC-108 run, where only the 120-256 ms RuleTester test crossed 5 s. These
  are not fixed here. If one of them flakes for real, it is a separate root cause; the lever then
  is the tools project's worker count against the machine's free cores, not the tests.
- Test names are longer (`… > tm/prefix-exports > valid > <code>`), and the rule name appears
  twice in the path for specs that wrap `run` in a titled describe. Cosmetic.

### What reviewers should examine hardest

- That `createRuleTester` keeps RuleTester's checks intact: duplicate-case detection, schema
  validation and the AST-unchanged check all still run, now per test. A throwaway spec checked
  this: a valid case that reports, an invalid case that does not, and a duplicate case each
  failed as its own named test ("Should have no errors but had 1", "Should have 1 error but had
  0", "detected duplicate test case").
- That no spec still calls `run` inside an `it` (Vitest would throw at collection), and that
  the `describe` budget of no-decimal-operators (120 s) still reaches its cases.
- Whether the helper's home, the core-tools package's `fixtures/` (beside `fake-emit.ts`, outside
  the build's `src`), is the right one for a module the workspace's `tools/tests` specs also import.

## Review 1

### Findings

What was checked, and held (no finding):

- Every RuleTester case still runs. A setup-file probe counted the `valid` and `invalid` arrays
  handed to each `run` (17 calls across the ten files) and matched them to the tests Vitest
  reported per file: 19, 6, 22, 7, 49, 26, 18, 22, 19 and 9 RuleTester tests, 197 in all, all
  passed (203 with the six untouched scope tests).
- A broken rule fails its cases. A scratch Vitest config aliased each spec's rule import to a
  wrapper around the real rule and ran the ten real spec files unchanged. With a rule that
  reports nothing, all 105 invalid cases failed ("Should have 1 error but had 0") and the 92
  valid ones passed. With a rule that reports once on `Program`, all 197 RuleTester tests failed
  ("Should have no errors but had 1", messageId mismatch). The six scope tests load the rule
  through `eslint.config.mjs`, not the alias, and passed, as expected.
- `it.only`: a scratch spec with one `only: true` case ran that case and skipped the other two.
  Vitest's chainables keep their context in a closure, so RuleTester calling them as
  `this.constructor.describe(...)` does not matter.
- No timeout raised. The diff touches no `timeout`. no-decimal-operators keeps
  `{ timeout: 120_000 }` on its outer `describe`, and Vitest 4.1.9 copies a suite's options into
  nested suites and their tests (`@vitest/runner` `suiteFn` merges `currentSuite.options`;
  `createSuiteCollector` merges `suiteOptions` into each test), so the type-aware first case
  still gets 120 s. Its first test took 1.6 s here.
- No spec still calls `run` inside an `it`: all 17 calls sit in a `describe`. ESLint 10.6.0
  skips the empty `valid` describe (`test.valid.length > 0`), so no-decimal-operators' `valid: []`
  blocks raise no "No test found in suite".
- The helper resolves the same `eslint@10.6.0` and `vitest@4.1.9` instances as the specs; the
  fixture is outside the package build (`src/**` only) and outside `files`.
- Re-run here: the ten changed files (203 passed), `tsc -p` on core-tools `tsconfig.spec.json` and
  `tools/tsconfig.json`, and `eslint` on the eleven changed TypeScript files (the fixture is
  linted, not ignored): all clean. The full lint:test runs in the commit message are not re-run.
  The pc/PC-F73 push run 45114710359 is the usual pre-integration skip.

**R1-1 (minor): the helper mutates the shared `RuleTester` class, so test order can matter
when files share a worker.**
`client/projects/core/tellma-core-tools/fixtures/rule-tester.ts:28-30` assigns
`RuleTester.describe`, `.it` and `.itOnly`, which are static fields of ESLint's module-level
class. Under this config's default per-file isolation that stays inside one file: a probe pair
of files showed the second file, in its own process, still seeing ESLint's `itDefaultHandler`.
But it is process-wide state, and nothing in the TSDoc or the comment says so. If the tools
project ever runs with `isolate: false` (a common lint:test speed-up), files share one `eslint`
module in a worker. Then a spec written the old way, `new RuleTester(...)` with `run` called
inside an `it`, passes alone and throws Vitest's "test inside another test" error only when it
is scheduled after a file that called `createRuleTester`. That is an order-dependent failure,
the class this checkpoint removes.
Suggested fix: keep the hooks on a subclass, which ESLint's static accessors support, since
they read and write `this[...]` and `run` calls `this.constructor.describe`/`it`/`itOnly`:
`class TmRuleTester extends RuleTester {}` with `TmRuleTester.describe = describe;
TmRuleTester.it = it; TmRuleTester.itOnly = it.only;` at module level, and `createRuleTester`
returning `new TmRuleTester(config)`. ESLint's own class is then never touched. At the least,
say in the TSDoc that the call sets RuleTester's static hooks for the whole process.

### Responses

- R1-1: fixed in fix 1 (this commit). `fixtures/rule-tester.ts` now declares a private
  `VitestRuleTester extends RuleTester` and sets `describe`, `it` and `itOnly` on it once, at module
  level; `createRuleTester` returns `new VitestRuleTester(config)`. ESLint's static accessors store
  on the class they are called on (`this[DESCRIBE]`), and `run` reads them through
  `this.constructor`, so the subclass gets Vitest's functions and ESLint's own `RuleTester` keeps
  its defaults. A throwaway spec checked both sides in one file: after `createRuleTester`,
  `RuleTester.it` and `RuleTester.describe` are not Vitest's, and the returned tester's
  constructor's `it` is. The file comment now says why the hooks sit on a subclass. Re-run: the ten
  RuleTester spec files plus that probe (204 passed), `tsc -p` on core-tools `tsconfig.spec.json`
  and `tools/tsconfig.json`, `eslint` on the fixture, `dotnet format --verify-no-changes`.
