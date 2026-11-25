# PC-F84: Core typecheck against emitted entity types

## Implementer notes

### The gap

Core's own gates compile `@tellma/core` with `TmEntityTypes` and `TmEntityShapes` empty, so
`TmEntityType<'core.Notification'>` is the open `TmJsonObject` there and any string indexes it.
The Acme build compiles the same sources beside its emitted `generated/tellma-entities.d.ts`, which
closes each core row to its wire interface (`CoreNotification`, …) and gives `TmPath`/`tmEntity`
real shapes. PC-155's attention helpers indexed a closed row with a `string` and passed only core's
gates (D-269).

### What was built

- **The fixture, emitted, never hand-written.** A new fact in
  `test/core/Tellma.Core.Tests/Client/ClientEntityFixturesTests.cs`,
  `The_platform_declarations_fixture_is_what_the_platform_alone_writes`, composes the platform with
  no feature of its own (`TestComposition.Compose(static _ => { })`, the same `AddTellma` a
  distribution calls), runs the same `EntityClientAssetContributor` → `EntityTypesWriter` the
  Migrator's `emit-client-assets` runs for Acme, and goldens the declarations to
  `client/tools/typecheck/fixtures/tellma-entities.d.ts.txt`. It uses the class's existing
  `Golden` helper, so it fails the unit tier on any drift and re-records with
  `TELLMA_UPDATE_GOLDENS=1`. This is what keeps the fixture from going stale: a change to core's
  model fails build-test in the same CI run until the fixture is re-recorded. The 16 `Core*`
  interfaces in it are byte-identical to the ones in the existing test-model fixture and in Acme's
  emitted file.
- **The leg.** `client/tools/typecheck/emitted-entities.mts` (`pnpm run typecheck:emitted`, at the
  root and in `client/`) parses `projects/core/tellma-core/tsconfig.json` and
  `projects/core/tellma-core-testing/tsconfig.json` (the plain typecheck's views: every source file,
  specs included), adds the fixture as an in-memory `tellma-entities.d.ts` root through a compiler
  host, and fails on any error diagnostic. (Since fix 1: one program over both projects' files
  while their options agree, and a typed in-memory probe in place of the textual self-check; see
  the responses to R1-1 and R1-2.)
  - The fixture stays `.d.ts.txt` and is served from memory for the reason the existing test-model
    fixture is: a `.d.ts` on disk under `client/` that augments `@tellma/core` would join every
    program the workspace's tools build.
- **CI.** A `pnpm run typecheck:emitted` step after `typecheck` in the `client` job, and in
  `client-windows` too, because that job's header says it runs every gate of the Linux job except
  e2e.
- **Docs.** `client/README.md` (the gate and how to re-record the fixture), `CONTRIBUTING.md`
  (the gate list).

### Shown failing on the PC-155 pattern

I reverse-applied 0b7537de's two core files (tm-arrival-toasts.ts, tm-attention.ts) in the working
tree, without committing, and ran the leg: 8 errors. Examples are TS7053 "expression of type
'string' can't be used to index type 'CoreNotification'" at tm-arrival-toasts.ts:70 and
tm-attention.ts:104/110, and TS2345 open `TmJsonObject` rows passed where `CoreNotification` is due
at tm-arrival-toasts.ts:184/186/214/227. The plain `tsc -p projects/core/tellma-core/tsconfig.json`
passed on the same tree. I then restored both files; the diff carries neither revert.

### What the leg found on platform-core (7a27e3c6)

One error, in a spec:
`config-check/src/tm-check-app-config.spec.ts:517` declares `tmEntity('core.Blob', { columns:
['Name'] })`. `core.Blob` has no `Name` (TS2322 against `TmColumnInput<'core.Blob'>`). The test
checks operation-missing and entity-not-routable, not the path. The column is now `Id`, the one
`core.Blob` scalar that both the real model and `tmTestMetadata()` carry. `FileName` would
type-check, but `tmTestMetadata()`'s stand-in `core.Blob` lacks it, so the check added an
unknown-path problem (tried; the spec failed). The spec's expectations are unchanged and its 29
tests pass.
No product code failed. The PC-155 fix (0b7537de) already covers the attention helpers.

Observation, not changed here: `tmTestMetadata()` in @tellma/core-testing gives `core.Blob` a
`Name` property, and `core.User` a `ManagerId`/`Manager` pair, that the server's model does not
have. It is untyped data, so this leg cannot see it. Aligning it with the server's model would
change a published testing helper and every spec that leans on it, so I left it alone.

### Evidence beyond the leg

- A fresh Acme client build (`tellma-client build`, 0 errors) emitted `Core*` interfaces
  byte-identical to the fixture's.
- Sizes unchanged after `rm -rf client/dist` and a clean build: "." 50.23 of 50.23, cap line 50.13
  of 55.
- Gates run locally: lint, typecheck, typecheck:emitted, lint:test (514), build, size:check,
  api:check, docs:build, the tm-check-app-config spec (29), ClientEntityFixturesTests (4), the Acme
  client build, `dotnet build Tellma.slnx` (0 warnings) and `dotnet format --verify-no-changes`.

### Deviations

- No spec covers this item (a fix item with no CHECKPOINTS entry); none from the ledger row.
- The leg also covers `@tellma/core-testing` (its program compiles core's sources and its own
  against the closed rows), and runs on `client-windows` as well as `client` (that job's stated
  parity rule).

### Open risks

- The fixture is the platform alone. A distribution adds its own entities to the same
  registries. Core code generic over `keyof TmEntityTypes` could in theory behave differently with
  a larger union. The Acme client build still covers that case.
- `core.Assignment` is not an entity yet, so `TmEntityType<'core.Assignment'>` stays open in both
  core and Acme. When a later item adds it, the fixture's golden fails until re-recorded, and the
  leg then checks the assignment code paths too.

### What reviewers should examine hardest

- Whether the in-memory host really adds the augmentation to the same program the plain typecheck
  builds: same options, same file list. The PC-155 reproduction is the evidence.
- That the fixture cannot go stale without a red unit tier: the golden test and its path.
- The `client-windows` step and the path normalisation in `isDeclarations` (it ran on Windows
  locally).

## Review 1

### Findings

Reviewer 1 (Opus), on commit a7cff53c. Verified locally:
- **The golden.** `dotnet test test/core/Tellma.Core.Tests --filter "FullyQualifiedName~ClientEntityFixturesTests"`:
  4/4 passed.
  - Scratch model change, not kept: a `ScratchProbeAt` property added to `UserStamp`. The new fact
    failed (`ClientEntityFixturesTests.cs:79`), and so did the two test-model facts. Reverted,
    rebuilt, 4/4 again.
  - The composition is the Migrator's: `AddTellma` with no feature, then the same
    `EntityClientAssetContributor` and `EntityTypesWriter`. The bundle only re-encodes the file
    as UTF-8 with LF. All 36 `Core*` declarations and all 32 `core.*` registry entries (types and
    shapes) are identical to the ones in Acme's emitted `generated/tellma-entities.d.ts`. The test
    configuration (sound settings, in-memory blobs) changes nothing in them.
- **The leg.** `pnpm run typecheck:emitted` on the commit: both projects pass (42.4 s + 20.4 s).
  - PC-155 pattern (0b7537de's two core files reverse-applied, then restored): 8 errors in core,
    the same 8 repeated in core-testing and printed once. Exit 1. Plain `tsc -p` of both projects
    exits 0 on the same tree.
  - The parent's spec line (`columns: ['Name']`) restored: one TS2322 at
    `tm-check-app-config.spec.ts:517`. Plain typecheck exits 0.
- **Coverage.** Every client source that imports `@tellma/core` or `@tellma/core-testing` was
  grepped:
  - core-ui, core-ui-testing, core-ui-tokens, core-ui-mcp and the showcase import neither, so
    leaving core-ui out is right.
  - locale-ar imports only string types. The core-tools fixture app uses
    `tmFeature`/`tmPage`/`provideTellma`. Neither touches entity rows.
  - `tools/tests/entity-model.spec.mts` already compiles the test-model declarations with
    `skipLibCheck: false`.
- **The spec fix.** `ng run core:test --include **/tm-check-app-config.spec.ts`: 29/29 passed.
  - `Id` is a scalar of the real `core.Blob` (`TmEntityShapes`).
  - `tmTestEntitiesChunk()` prepends `Id` to every test entity, so the check adds no
    unknown-path problem.
  - The test asserts the operation and routability problems, not the path, so the change keeps
    the test's intent.
- **CI.** Both steps run from the repository root after `pnpm run typecheck`, with the same
  inputs (installed packages and sources; no built output needed), through tsx on both OSes. The
  isDeclarations comparison is case-insensitive where the file system is. pc/PC-F84's only run
  (45492985615) was skipped, as expected before integration.
- **Lint.** eslint and prettier on the new script and the changed spec: clean.

**R1-1 (minor). The leg's self-check is textual, so a fixture whose augmentation does not take
effect passes silently.**
- Where: `client/tools/typecheck/emitted-entities.mts:56-61`.
- The guard regex-matches `interface TmEntityTypes { 'core.…':` in the text.
- The workspace preset sets `skipLibCheck: true`, and the fixture is a `.d.ts`. So nothing in the
  fixture itself is checked:
  - an augmentation whose module does not resolve (TS2664);
  - an augmentation that lands on a different interface;
  - an import that no longer exists.
- In all three cases the rows stay open and the leg reports success.
- Shown with a scratch copy of the script, not kept: with the PC-155 revert applied, changing only
  `declare module '@tellma/core'` to `declare module '@tellma/core-nope'` took the leg from
  8 errors to "compiles against the emitted declarations".
- Realistic trigger: core moves or renames the registry, for example `TmEntityTypes` leaves the
  root entry point. The writer would still emit the old target until someone changes it.
  `entity-model.spec.mts` (`skipLibCheck: false`, `@ts-expect-error` probes) narrows this, which
  is why it is minor. But this leg's own guard claims to catch "a fixture that closes no row", and
  it cannot.
- Fix: replace or extend the regex with a typed probe. Serve a second in-memory root beside the
  declarations, for example
  `import type { TmEntityTypes } from '@tellma/core'; export const probe: keyof TmEntityTypes = 'core.Notification';`.
  `keyof` of the empty registry is `never`, so the probe fails whenever the augmentation does not
  close core's rows. Keep its diagnostics in the reported set.

**R1-2 (minor). The leg checks core's sources twice; one program would do.**
- Where: `client/tools/typecheck/emitted-entities.mts:40-43` and `:135-151`.
- The core-testing program reaches core through the `@tellma/core` path mapping, so it re-checks
  every core source it reaches. The two tsconfigs carry the same compiler options, so it does
  this with identical options.
  - In the PC-155 run, all 8 of core-testing's errors were duplicates.
  - core-testing's own files use no registry-typed API (no `TmEntityType`, `tmEntity`, `TmPath`).
- The second pass therefore costs about 20 s locally (a third of the leg) for only core-testing's
  few dozen files. CI pays that on Linux and again on the slower Windows runner. The orchestrator
  asked for a cheap leg.
- Fix: build one program whose roots are the union of both parsed `fileNames`, plus the
  declarations (and the R1-1 probe). Use core's options, after asserting that both parsed options
  match, minus `configFilePath`. If they ever diverge, fall back to two programs. The duplicate
  bookkeeping (`keyOf`, `reported`) then goes away.

**On the implementer's `tmTestMetadata()` observation.**
- `tmTestMetadata()` gives `core.Blob` a `Name`, and `core.User` a `ManagerId`/`Manager`, that the
  real model lacks.
- This is not a defect of this item, and I would not fix it here. The helper is documented as a
  small, coherent stand-in distribution, not a mirror of the server's model. It is untyped
  metadata that this leg cannot and need not see.
- Existing specs lean on the invented members. For example, `tm-error-presenter.spec.ts:137-141`
  resolves `Manager.Manager` through `core.User`.
- Aligning it would change a published testing helper, and those specs. If Ahmad wants test
  metadata to track the real core entities, that is a separate, low-priority fix item. It could
  also make the stand-in's core entities a subset of the emitted ones.

No blocking or major findings. The deviations hold:
- core-testing is genuinely another program over core's sources, though see R1-2 on its cost.
- `client-windows` must mirror the `client` job's gates by its header.

### Responses

R1-1: fixed in fix 1.
- The textual regex is gone. The host now also serves an in-memory source file,
  `tools/typecheck/fixtures/registries.probe.ts`. It imports `TmEntityTypes` and `TmEntityShapes`
  from `@tellma/core` and assigns `'core.Notification'` to `keyof` each.
- It is a `.ts`, not a `.d.ts`, so `skipLibCheck` does not skip it. An augmentation that does not
  reach the registries leaves their keys `never`, so the probe fails, and the leg adds a line
  saying the rest of the check proves nothing.
- Scratch edits, each reverted with `git checkout`:
  - The fixture's `interface TmEntityTypes {` renamed to `interface TmEntityTypesRenamed {` (the
    augmentation target renamed): exit 1, with one TS2322 at `registries.probe.ts:4`.
  - Your scenario: the PC-155 revert plus `declare module '@tellma/core-nope'`. Before the fix
    this passed silently. Now it exits 1 with two TS2322, at `registries.probe.ts:4` and `:5`.

R1-2: fixed in fix 1.
- The leg parses both tsconfigs. If their options are deep-equal (ignoring `configFilePath`) and
  their project references match, it builds one program over the union of their file lists,
  deduplicated by canonical path. Otherwise it prints that it fell back and builds one program per
  project.
- The two configs agree today, so one program runs. The `keyOf`/`reported` bookkeeping is gone.
- Run time, measured back to back on this loaded machine: the old two-program leg took 45.5 s and
  39.7 s (30.6 + 14.9 and 24.8 + 14.9). The new one-program leg took 27.5 s and 30.8 s, about
  30-40% less.

Gates after fix 1:
- `typecheck:emitted` on the fix-1 tree: exit 0, one program.
- With the PC-155 revert (scratch, reverted): exit 1 with the same 8 errors, each printed once.
- lint and typecheck: exit 0 on the final tree.
- lint:test: 514 passed. It ran before a one-line change to `comparableOptions` (TS4111 asked for
  `options['configFilePath']`), and that script is not one of its inputs.
- Final-tree runs: the leg passes in 35.2 s. With the PC-155 revert it exits 1 with 8 errors in
  35.2 s.

The `tmTestMetadata()` observation is left alone, as ruled: it is fix item PC-F94.
