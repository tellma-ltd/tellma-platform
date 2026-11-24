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
  host, and fails on any error diagnostic. Errors core-testing's program repeats from core's sources
  print once. A sanity check fails the leg if the fixture registers no `core.*` entity in
  `TmEntityTypes` (which would leave every row open and check nothing).
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
