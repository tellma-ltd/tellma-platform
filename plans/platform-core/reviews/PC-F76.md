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
