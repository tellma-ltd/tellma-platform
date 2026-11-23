# PC-F76: Client: enforce the 50 KB budget on the @tellma/core primary

## Implementer notes

### What was built

Spec 0024 §12's line "The `@tellma/core` primary's own weight, the sum of its shares, error 50 KB"
is now a budget of `tellma-client build`, `corePrimary`, beside the other §12 lines. It is measured
at gzip level 9 on the app build's output, first-frame bytes only (D-205), with Brotli printed
beside, and it fails and raises exactly as the other lines do.

All changes are in `client/projects/core/tellma-core-tools/`:

- `src/checks/budgets.ts`: `TM_BUDGETS` gains `{ name: 'corePrimary', warn: null, error: 50 }`,
  in the spec table's order (after `workerShell`). `TmBudgetInputs` gains `sourceMapsDir`.
  `tmMeasureBudgets` measures the line; `tmBudgetProblems` and `tmReadBudgetLevels` are unchanged,
  so `budget-exceeded` over 50 KB and `budget-note-missing` for a raise without a note apply to it
  as to every line.
- `src/output/stats.ts`: `TmBuildGraph` now also reads the metafile's `inputs` (the module graph),
  marking each import relative or by package name from esbuild's `original` specifier.
  `tmCorePrimaryModules(graph)` is the primary's module set: the module it starts at
  (`@tellma/core/fesm2022/tellma-core.mjs`, or `tellma-core/public-api.ts` in a workspace) and
  every module reached from it through relative imports, static or dynamic. Secondary entry points
  and other packages are imported by package name, so they stay out. The same rule works for the
  installed package, whose bundle imports the chunks ng-packagr split it into
  (`tellma-core-tellma-core-<hash>.mjs`, 540 KB of the 545 KB raw today) by relative paths, and for
  the in-repo build from sources. A file-name rule could not tell the primary's split chunks from a
  secondary entry point's.
- `src/output/source-attribution.ts` (new): reads a version 3 source map and cuts out of a built
  file the runs of generated code whose source a test accepts. A run lasts until the next run or the
  line's end, and a run with no source belongs to none. Columns are UTF-16 code units, as esbuild
  writes them.
- `src/commands/build.ts`: step 6 passes `project.sourceMapsDir`. Step 5 has moved the hidden maps
  there by then.
- The measure: for each initial file (the scripts, their static imports and the modulepreloads,
  `/shell` and `/attention` included, the same set as `initialJs`) that holds a primary module, the
  primary's runs are cut out through the file's hidden source map. The runs of all files are joined
  and compressed alone. The build fails with a `TmBuildFailure` (exit 2) when such a file has no
  map, or when the maps attribute no code at all to the primary (they would name modules
  differently from the stats).
- `size:check`'s per-entry ceilings (`"."` 45) are untouched. The new line adds the spec's cap on
  the app's output and replaces nothing.

Tests:
- `src/output/source-attribution.spec.ts` (new): run attribution across lines, glue with no source,
  hand-written VLQ mappings, UTF-16 columns with a surrogate pair, `sourceRoot` and backslashes,
  malformed mappings and maps.
- `src/output/output.spec.ts`: the module graph read from the metafile, and
  `tmCorePrimaryModules` over a workspace layout and an installed-bundle layout. The installed
  layout includes a secondary entry point's own hashed chunk, which must stay out.
- `src/checks/budgets.spec.ts`:
  - the level is 50;
  - a raise needs a note (`budget-note-missing`); a lower level or a new warn level does not;
  - over 50 KB is `budget-exceeded`; exactly 50 and under pass; a noted raise moves the line;
  - the measure on a fixture build equals the gzip of exactly the primary's runs in the initial
    files. App and Angular code in the same file, a lazy chunk's primary code and a zero-code
    re-export module are left out;
  - a missing map fails the build, and maps that name the modules differently fail it.
- `fixtures/source-maps.ts` (new) writes maps from plain runs, so the specs state their expected
  bytes instead of trusting a second decoder.
- `src/commands/build.spec.ts`: a `corePrimary` raise without a note stops the build before
  `ng build`, with exit 3.
- `src/commands/build.fixture.spec.ts` (real `ng build`):
  - the `corePrimary` line prints with level 50;
  - a level lowered to 0.1 fails with `budget-exceeded budget:corePrimary`;
  - on the real application builder's output, the bytes the maps attribute to the primary agree
    with the stats' `bytesInOutput` for its modules, within -1% and +3% plus 64 bytes.

### Measured on today's base (5dd234a0: PC-F73 landed, PC-111 not yet)

`tellma-client build` on Acme with this checkpoint's code, which printed and passed the line:

| Line | Gzip 9 | Brotli 11 | Error | Files |
|---|---|---|---|---|
| corePrimary | **37.80 KB** (38,711 B) | 33.52 KB | 50 | 4 |

- Headroom: **12.20 KB**.
- The maps attribute 114,567 raw bytes to the primary in the four initial files that hold its
  code. The stats count 113,713 for its modules there (+0.75%: the separators between modules come
  with the runs).
- Primary code outside the first frame: 9,019 raw bytes, the lazy `tm-entity-model` and
  `tm-locale-write` chunks.

This is about 7.2 KB below `size:check`'s figure for `"."` in this slot (44.97 KB against its
45 KB ceiling, linked and bundled alone, gzip 6). The two measure different things, which is why
both lines stay:
- `size:check` bundles the entry point's whole FESM with its imports kept as import statements.
- This line counts what the app actually ships of it, after the app build's own optimizations.
  Five primary modules have no code in Acme's output today, because nothing on its first frame
  reaches them yet: `tm-shortcuts.ts`, `tm-shortcut.ts`, `tm-display-names.ts`, `tm-decimals.ts`
  and `tm-entity-declaration.ts`.
- When a consumer of one of those modules lands (spec 0027's shell registering shortcuts, for
  example), this figure rises by that module's weight, even though `size:check`'s `"."` does not
  move.

For the held items (D-247, D-248): together they add about 5.5 KB on the `size:check` measure. If
they keep that ratio on this measure (about 0.84), they would bring `corePrimary` to about 42.5 KB.
That is an estimate only. The gate measures their real weight after PC-115 lands.

### Tests run locally

- `pnpm run lint`, `typecheck`, `lint:test` (44 files, 463 tests, the fixture's real `ng build`
  included), `build`, `api:check` and `size:check` (`"."` 44.97 against 45): all green.
- `tellma-client build` on Acme: 0 errors, and the `corePrimary` line printed at 37.80 KB.
- `dotnet build Tellma.slnx` and `dotnet format --verify-no-changes`. No C# changed.

### Deviations

1. The spec names no budget key. It is `corePrimary`, in the style of the other keys.
2. Spec 0024 §12 says the primary's ceiling is "the sum of every share, rounded up", and that a
   share that grows raises it, with a note. The shares are spec prose, not data the build can sum,
   so the line is the spec's stated 50 KB error level. A distribution raises it like any other
   budget: `"tellma": { "budgets": { "corePrimary": { "error": 52, "note": "…" } } }`, and a raise
   without a note is `budget-note-missing`. The platform's own level changes in `TM_BUDGETS`.
3. "Own weight" is read as the primary's code cut out of the shared output files and compressed
   alone. Gzip in context, or the file's gzip pro-rated by raw bytes, cannot isolate one package's
   bytes in a file it shares with Angular and the app.

### Open risks

- The line depends on the hidden script source maps the spec's step 5 already requires. A
  distribution whose `angular.json` turns them off now fails the build (exit 2) with the missing
  map named, rather than measuring 0.
- If a distribution turned on `sourceMap.vendor`, an installed package's maps would name the
  package's original TypeScript sources instead of its bundle files. The "attribute none" guard
  then fails the build loudly; it does not silently pass. This is not supported, and Acme builds
  from sources.
- The measure counts what Acme's first frame reaches, so primary code that no consumer reaches yet
  does not count. See the figure notes above.

### What reviewers should examine hardest

- `tmCorePrimaryModules`: whether any primary code can escape the relative-import closure, or any
  other code can enter it, in either layout. One known case is an import whose `original`
  esbuild omits: only the `<runtime>` helper has none today.
- `tmCodeFromSources`: the run-boundary rule, the VLQ decoder's field handling (1, 4 or 5 fields;
  the source index relative across lines) and the UTF-16 column assumption.
- The first-frame set: it is `initialJs`, the set the `initialJs` budget measures. The /shell and
  /attention modulepreloads are counted, per D-205.

## Review 1

Reviewed 9cdbdeb7 against spec 0024 §12 and §2.6 step 6, the PC-F76 ledger row, and D-031, D-205,
D-214, D-247 and D-248.

Run here:
- The four changed spec files without the fixture (`budgets`, `output`, `source-attribution` and
  `build`): 48/48 passed.
- `build.fixture.spec.ts`, with its real `ng build`: 12/12 passed in 86 s.
- The pc/PC-F76 push run was skipped, so it gives no evidence.
- A probe over the slot's Acme output (`dist/acme`, built from this commit's tools):
  - 91 primary modules. The stats count 113,713 B in the initial files and the maps attribute
    114,567 B, so the commit's figures reproduce.
  - No import lacks `original`.
  - The primary reaches `/shell`, `/leader` and `/recents` only through package-name imports, so
    they stay out.
  - No `tellma-core/src` JS module falls outside the closure. The only ones outside are three
    component stylesheets, which go to CSS outputs.
  - 14 primary modules have no bytes in any output.
- Angular's chunk optimizer cannot desync the maps from the metafile, because step 1 runs
  `ng build` with `NG_BUILD_OPTIMIZE_CHUNKS=false` (`build.ts:137`).

Checked and confirmed:
- The 50 KB level matches the shares: 14 + 4 + 9 + 6 + 5 + 2.5 + 6 + 2 + 0.6 + 0.6 = 49.7, which
  rounds up to 50. So deviation 2 (a fixed 50 rather than a computed sum) loses nothing today.
- The key name (deviation 1) is fine.
- `budget-note-missing` (exit 3, before `ng build`) and `budget-exceeded` (exit 3) behave as for
  every other line, and both are tested. A missing map is a `TmBuildFailure` (exit 2), like the
  step's other output faults.
- The VLQ decoder handles 1, 4 and 5 fields and the cross-line source delta correctly. Columns are
  UTF-16 code units, as esbuild writes them.
- Paths are normalised on both sides, and esbuild writes forward slashes on Windows too.
- Map sources and metafile inputs agree because Angular's `outdir` is the workspace root and JS
  outputs are flat.
- The fixture test adds no second `ng build`: it reuses the snapshot, and its tolerance held with
  room on Acme (+0.75%).

### Findings

**R1-1 (major): the measure leaves out primary members that Acme does not reach, so it is not the
"own weight" the spec caps, and it runs about 7 KB below the figure the 50 KB accounting uses.**
`client/projects/core/tellma-core-tools/src/checks/budgets.ts:212-228` (and the
`corePrimary` label at `:65`).

The line weighs only the primary code that survives Acme's tree shaking and lands in initial
files. Spec 0024 §12 caps "the `@tellma/core` primary's own weight, the sum of its shares". The
shares are allocations of entry-point members, whether or not an app uses them:
- 0025 §1: "at most 9 KB gzip, its share of the primary".
- 0028 §1's table: "Budget (gzip, own weight)" per entry point, with the members listed.
- 0002 §8 defines self/own weight as the entry point's weight with shared Angular excluded.

No spec text makes the figure depend on what one app consumes.

The project's own accounting uses the whole-entry measure, not this one:
- Every `"."` budget note in `tellma-core/package.json` says "The primary's 50 KB total is not
  reached" on the linked whole-entry measure.
- D-247 projects 50.4 KB on that measure.
- D-248 moves `tmSerializePageContext` lazy to get under 50.

D-205 R2-6 is about counting modulepreloaded bytes honestly. It does not support a figure that
leaves out unconsumed members.

Failure scenarios:
- PC-111, PC-109 and PC-115 take size:check's `"."` to about 50.4 KB, the overrun D-247 wanted
  stopped. This line reads about 42.5 KB (the implementer's own estimate) and passes.
- The primary can hold 7 KB or more of spec'd but unconsumed members, and that grows with every
  member that lands ahead of its consumer. The line fires only when a consumer lands. For example,
  0027's shell registering shortcuts would pay for `tm-shortcuts.ts` bytes added long before. So
  the failure lands late, on the wrong checkpoint, which then has to cut bytes it did not add.
- The figure depends on the distribution. Only Acme is gated in CI, so another distribution that
  uses more of the primary can fail its own build over the platform's cap while the platform's CI
  stays green.

Deviation 3 justifies cutting the code out instead of gzipping it in context or pro-rating it. It
does not justify dropping unconsumed members, which is the larger effect.

The reading I think is right: the cap is on the whole primary entry point's own code, meaning
every member and its eager split chunks but not its `import()`-only chunks, at gzip level 9. That
is size:check's linked measure of `"."` at level 9.

Suggested fix: enforce the 50 KB cap on the whole-entry measure. The app build cannot see code it
shook out, and Acme compiles the primary from sources, so that measure belongs in `size:check`:
- measure `"."` also at gzip level 9;
- fail it over a platform-held 50 KB level that only a noted raise moves;
- refuse a `"."` ceiling in `budgetsInKb` above that level without such a note.

Keep this commit's line as a second, app-level figure, renamed or relabelled "on the first frame".
That figure is useful, and it is what the user downloads.

If the orchestrator rules for the first-frame reading instead:
- restate D-247 and D-248's projections and the budget notes' "50 KB total" claims on this
  measure;
- record under deviation 3 that unconsumed members do not count, and the roughly 7 KB that leaves
  out today;
- raise it with Ahmad.

**R1-2 (major): if the primary's root module is not found, the line silently reads 0.00 KB and
passes.**
`client/projects/core/tellma-core-tools/src/output/stats.ts:222` (`CORE_PRIMARY_ROOT`), `:128`
(`metafile.inputs ?? {}`), and `budgets.ts:218-228`.

The root is found by a hard-coded path pattern: `@tellma/core/fesm2022/tellma-core.mjs` or
`tellma-core/public-api.ts`. When nothing matches, `primary` is empty, `holding` is `[]`, and the
"attribute none" guard (`holding.length > 0 && …`) does not fire. The figure is
`tmCompressedSize('')` with 0 files, and the line passes. Three ways this happens:
- a stats file without `inputs`, which `read` accepts;
- an Angular Package Format change to the `fesm2022` folder or the FESM name;
- the workspace moving `public-api.ts`.

Every distribution's app imports the primary (`bootstrapTellma`), so an empty set is always a
fault, never a real 0. The existing test at `budgets.spec.ts:136` ("No module of the
@tellma/core primary is in this build", `files` 0) encodes the silent zero.

Suggested fix: throw a `TmBuildFailure` when `tmCorePrimaryModules` finds no root. Better still,
throw when no initial file holds a primary module that has bytes in its output. Give the
general-budget test graph a primary module, and add a test where the root is missing that expects
the failure.

**R1-3 (minor): the mismatch guard is all-or-nothing, so a partial mismatch under-counts silently.**
`client/projects/core/tellma-core-tools/src/checks/budgets.ts:220-228`.

The build fails only when every holding file attributes nothing. Suppose one file's map names its
sources differently from the stats, or attributes only part of that file's primary code: a
post-processed chunk, a mixed vendor-map setup, or a later builder change to one output kind. That
file's share then drops out of the figure with no error.

The invariant that would catch this, that attributed bytes agree with the stats' `bytesInOutput`
(within -1% / +3% + 64 B), is checked only in `build.fixture.spec.ts` on the fixture. The build
never checks it.

Suggested fix: keep each input's `bytesInOutput` in `TmBuildOutput` when reading the metafile.
In `tmMeasureBudgets`, per holding file, compare the attributed raw bytes with the stats' count for
the primary's modules in that file, and fail with `TmBuildFailure` below the test's lower bound. A
file whose primary modules have 0 bytes, such as the re-export list, then needs no special case.
Add a unit test where one of two maps names its sources differently.

**R1-4 (minor): a bare `'.'` or `'..'` specifier counts as a package import, so its subtree leaves
the primary.**
`client/projects/core/tellma-core-tools/src/output/stats.ts:133`.

`/^\.\.?[\\/]/` needs a slash after the dots. `import { x } from '..'` (a folder's `index.ts`) or
`from '.'` is relative, but it is marked as by package name. The module it names, and everything
reached only through it, then falls out of the closure without a word. The primary has no such
import today (I grepped), and no lint rule I know of forbids one.

Suggested fix: `/^\.\.?(?:[\\/]|$)/`, with a case in `output.spec.ts`.

### Responses

- R1-1: fixed in fix 1, per D-251.
  - The 50 KB cap of spec 0024 §12 is enforced on the whole primary entry, in `size:check`:
    - `client/tools/bundle-caps.mjs` holds the platform's caps: `@tellma/core` `"."` at 50 KB,
      measured at gzip level 9.
    - The cap is measured on the same linked, bundled-alone weight as the ceiling. Every member
      counts. The `import()`-only chunks budgeted on their own (`tm-entity-model`,
      `tm-locale-write`) stay out, as they do for the ceiling.
    - `createBundleMeasurer` now returns the default-level and level-9 gzip of one bundle, so the
      cap costs no second bundle.
    - A library raises a cap only through `"tellma".capsInKb: { ".": { "error": …, "note": "…" } }`.
      A raise without a non-blank note fails the gate, and so does a cap the platform does not
      set. A lower level needs no note.
    - `size:check` prints `@tellma/core (cap, gzip 9)` beside the ceiling and fails `OVER CAP`.
    - Tests are in `client/tools/tests/bundle-caps.spec.mts`: over fails, at or under passes, a
      noted raise moves the line, a raise without a note fails (missing, empty, blank), a lower
      level needs no note, and malformed settings and unknown caps are refused.
  - Measured on this branch's base: the cap line reads **44.90 KB** against 50 (headroom 5.10). The
    ceiling line reads 44.97 against 45 at the default level.
  - The `tellma-client build` line keeps its 50 KB level, restated as the first-frame figure, a
    lower bound and not the cap:
    - it is renamed `corePrimaryFirstFrame` (`TM_CORE_PRIMARY_FIRST_FRAME`);
    - its label, the file comment, the core-tools README and `@tellma/core`'s README say so.
  - Deviation 3 is restated in the fix commit: there are two measures, and the whole-entry one
    is the cap.
- R1-2: fixed in fix 1. The figure is never a silent zero. The line is reported as the new
  problem `budget-unmeasurable` (an error, exit 3, keyed `budget:corePrimaryFirstFrame`), with its
  own message, in two cases:
  - no module matches the primary root;
  - no initial file holds primary bytes.
  The general budget test now expects that problem instead of `files` 0. New tests cover a
  missing root and an initial set without the primary.
- R1-3: fixed in fix 1.
  - `TmBuildGraph.read` keeps each output's `bytesInOutput` per module (`TmBuildOutput.bytes`).
  - The measure compares each file's attributed bytes with the stats' count for the primary's
    modules in that file. A file whose primary modules have no code there, such as the re-export
    list, is skipped.
  - A file outside the band reports `budget-unmeasurable`, naming the file and both counts. The
    band is 1% under or 3% over, plus 64 bytes.
  - Measured drift on Acme: +0.63% and +0.86% on the two large files, and +15 B (about +4%) on the
    two one-module files. Nothing ever came in under.
  - The 64 bytes absorb the separators on small files. The 1% under catches a lost module of any
    real size, and the 3% over catches foreign code given to the primary.
  - The all-or-nothing "attribute none" failure is gone; the per-file check replaces it.
  - Tests: one file's map naming its module by another path, a map over the band, and glue within
    the band. The fixture build's real-builder check stays.
- R1-4: fixed in fix 1. The pattern is `/^\.\.?(?:[\\/]|$)/`. `output.spec.ts` now covers `.` and
  `..` as relative, and a `.hidden` package name as not relative.

### Verification

A targeted verification of 20bb7b60 found two minor defects and nothing blocking or major. The cap
measure, the raise and note rules, the malformed settings, backslash paths, the regex and the CI
cost all held.

- V-1 (minor), `client/tools/bundle-caps.mjs:54` and `client/scripts/bundle-size.mjs:95`: the cap
  can be absent or near zero without failing.
  - Renaming the core package's `name` to `@tellma/core-renamed` makes `size:check` exit 0 with no
    cap row.
  - Truncating `dist/core/fesm2022/tellma-core.mjs` makes the cap read 0.02 "ok".
  - Fix: after the loop, fail every package or entry point in `ENTRY_POINT_CAPS` that was never
    measured, and fail a capped entry that measures below a floor (1 KB, justified in a comment),
    with tests for both.
- V-2 (minor), `source-attribution.ts:168` and `budgets.ts:289`: a line's trailing `export{…}` glue,
  which has no source, is attributed to the primary, because the last run extends to the end of
  the line.
  - That is the whole +15 B drift on the small chunks, plus 118 B on `chunk-MFPI4UOF`.
  - It can false-fail a small chunk with many exports, and its positive drift can hide about
    1.1 KB raw of under-count in the large chunk.
  - Fix: leave unmapped trailing glue out of the last run, then tighten the band to what the
    corrected drift justifies (re-measured on Acme, figures in the comment), with a test where
    the export list grows and the attribution does not.
- Also, outside the diff: a non-numeric `budgetsInKb` ceiling such as `"abc"` passes silently,
  because the comparison is NaN. Make `bundle-size.mjs` refuse a non-numeric or non-positive
  ceiling, with a test.

### Responses to the verification

- V-1: fixed in fix 2.
  - `bundle-size.mjs` records each entry point it measured against its cap. After the loop,
    `unmeasuredCaps` (`bundle-caps.mjs`) fails each capped package with nothing measured, the
    renamed-package case, and each capped entry point of a measured package that went unmeasured.
  - `capFailure` fails a capped entry under `CAP_FLOOR_KB` (1 KB at gzip 9) as an empty or
    truncated build, and the row reads `UNDER FLOOR`. The comment justifies the floor: the only
    capped entry weighs about 45 KB, and an entry point with exports and their code cannot
    compress under 1 KB.
  - Tests in `client/tools/tests/bundle-caps.spec.mts`:
    - the floor: 20 B and 0 B fail, 1 KB passes;
    - unmeasured caps: a renamed package, nothing measured, and only another entry point measured.
- V-2: fixed in fix 2.
  - In `tmCodeFromSources`, a line's last run now stops before a trailing `export{…};`, or
    `export{…}from"…";`, that follows the run's code unmapped. A mapping that starts at the
    statement itself keeps it with its source.
  - Re-measured on Acme, the six files holding primary code:
    - before: +412, +412, +57, +50, +15 and +15 B;
    - after: exact on five; `chunk-MFPI4UOF` reads +294 B (+0.45%).
  - The +294 B are constants the Angular compiler hoists out of a component with no mapping of
    their own (the `tm-page-header` content selectors after a `tm-page-focus` run). It is not
    trailing glue, so it stays.
  - Single modules shift by up to 40 B either way, but within a file the shifts cancel, so no file
    reads under.
  - The band is tightened from 1% under or 3% over plus 64 B to 0.5% under or 1% over plus 16 B:
    twice the drift seen over, half that under. The fixture build's check uses the same band.
  - Test: the export list grows from none to four names, and a re-export `from` form, while the
    attribution stays the body. A mapped statement stays with its source, and an export that is
    not at the line's end stays in the run.
- Ceilings: fixed in fix 2.
  - `ceilingProblem` (`bundle-caps.mjs`) refuses a `budgetsInKb` or `lazyChunkBudgetsInKb` ceiling
    that is not a finite positive number. `bundle-size.mjs` prints the problem and fails, and does
    not measure against that ceiling.
  - Tests: `"abc"`, `"45"`, 0, -1, NaN, Infinity, null and an object are refused; 45 and 0.5 are
    taken.
- Measured after fix 2, on the same base:
  - the `size:check` cap line reads 44.90 KB at gzip 9 against 50, unchanged;
  - Acme's `corePrimaryFirstFrame` reads 37.49 KB at gzip 9 (Brotli 33.28) against 50, over 4
    files. It was 37.80 before the trailing export statements came out.
