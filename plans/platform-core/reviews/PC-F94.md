# PC-F94: Align tmTestMetadata with the emitted model

## Implementer notes

### The defect

`tmTestMetadata()` and `tmTestEntitiesChunk()` (@tellma/core-testing) hand-wrote three platform
entities. Their `core.Blob` had a `Name`, and their `core.User` had `ManagerId` and a self-referencing
`Manager`. The server's model has neither. `provideTellmaTesting()` and the route rig
(`tmAppRig`) load this stand-in, so a core spec could resolve a path or count on a field the
product never sends and still pass.

### What was built

- **The stand-in no longer hand-writes any platform entity.**
  `client/projects/core/tellma-core-testing/src/tm-test-metadata.ts` takes every `core.*` summary,
  entity and enum from the recorded entity model in `fixtures/entity-model/`. That is the JSON
  `ClientEntityFixturesTests` goldens from the server's emit, and `tmTestEntities()` already reads
  it. The Core chunk's `entities` and the Entities chunk now carry all 16 platform entities as the
  server emits them, and the Entities chunk carries all 24 platform enums. Before, it carried 3
  hand-written entities and no enums.
  - The test distribution's own two entities stay hand-written: `acme.Customer`, and `gl.Center`
    with `ManagerId`/`Manager` → `core.User`. They are merged in and ordered by name.
  - Both functions now return a deep copy on every call (`structuredClone`). Before, every call
    shared the same summary and property objects.
  - `summary()` lost its dead `null`-segment and `operations` branches, which only the old
    `core.Blob` used.
- **Pinned to the PC-F84 composition.** A new fact,
  `ClientEntityFixturesTests.The_test_model_writes_the_platform_entities_as_the_platform_alone_writes_them`,
  composes the platform alone (`TestComposition.Compose(static _ => { })`, the composition behind
  PC-F84's platform declarations fixture). It runs the same entity contributor and asserts that, for
  each of three sections, the test model's `core.*` items are byte-identical to the platform-alone
  output. The three sections are the Core chunk's entities, the Entities chunk's entities, and its
  enums. The chain is: the fixture JSON equals the test model (the existing goldens), and the test
  model's `core.*` items equal the platform alone (the new fact). So the stand-in's platform
  entities are the platform's emit, and drift fails the unit tier.
- **A guard on the client side.** `tm-test-metadata.spec.ts` gains two things:
  - a closure check: every navigation target, child entity and named enum resolves inside the chunk;
  - a test that the stand-in's `core.*` summaries, entities and enums equal the recording's.

### Specs that leaned on the stand-in (found by running the suites, then by grep)

I changed the stand-in first and ran the full `core` and `core-testing` unit projects: 29 failures
in 2 files. I then searched for `Manager`, `core.Blob`, `core.User`, `core.Role`, `core_*` label
keys, and hand-written `core.*` summaries in the specs and testing utilities.

1. `errors/src/tm-error-presenter.spec.ts`, "names a refused path from the query root, else shows
   it raw". The spec resolved `gl.Center` → `Manager.Manager`, which needs `core.User.Manager`. It
   now uses `Manager.Image`: `gl.Center.Manager` → `core.User`, then `core.User.Image` →
   `core.Blob`. Both are real navigations, and the path is still two navigations deep, which is
   what the spec is about. The raw half uses the same path. It also asserts that the path resolves
   (non-null) instead of dereferencing a null.
2. `config-check/src/tm-check-app-config.spec.ts`, 28 tests.
   - **27 were label noise.** The server writes a `labelFallback` for every platform label that no
     string resource declares, and the platform's emit has 306 of them today. `tmCheckAppConfig`
     warns `label-missing` once per such key, so every "sound configuration" assertion saw 306
     warnings. The spec now checks against `coreChunk()`/`entitiesChunk()`. These drop
     `labelFallback`, at any depth, from `core.*` items only. That is the shape the server sends
     once those strings exist; the field is optional and the server omits it for declared keys.
     The label tests that patch their own fallbacks (acme, gl, the core job handler) are unchanged.
   - **1 was real: the stand-in hid a reserved-path conflict.** "accepts kebab-case literals…"
     declared the page `reports/:id/studio` from the `acme` feature. With the real `core.Report`
     (segment `reports`) in the model, the check rightly reports `reserved-path`. The stand-in
     lacked `core.Report`, so the spec could not see it. The example path is now
     `customers/:id/studio`, which is acme's own entity segment.
3. `src/routes/tm-app.testing.util.ts`. `tmRouteTestMetadata()` appended a hand-written
   `core.Report` summary (operations `query`, `details`, `save`; label fallback `core.Report`).
   The real one (which also has `delete`) is now in the base, so the duplicate is gone.
   `core.Dashboard` stays hand-written: product code names it (`tm-navigation.ts`,
   `tm-route-host.ts`), but the server does not model it yet.
4. `src/attention/tm-attention.testing.util.ts` appended a hand-written `core.Notification`
   summary: display name `Title`, label key `core_Notification`. The real one has display name
   `Id` and label key `Core_Notification`. The duplicate is gone. The real summary gives the
   overflow pill its search route, and the attention specs pass on it unchanged.

Every other spec that names a core entity already ran over `tmTestEntities()` (the recorded test
model), or over the stand-in without touching the removed fields.

### Deviations

- No CHECKPOINTS entry or spec covers this fix item. The ledger row is met.
- The ledger asks to move specs that need the extra fields onto "a test entity of their own".
  None turned out to need them. The one spec that used them (the error presenter) tests a
  two-navigation path, and real navigations express that. I rewrote it over real fields rather
  than invent a test entity, so the spec now runs against shapes the product sends. `acme.Customer`
  and `gl.Center` remain the test distribution's own entities.
- I derive from the recorded test-model JSON, not from PC-F84's `.d.ts.txt` fixture. A
  declarations file cannot yield runtime metadata (labels, operations, limits, actions, enums).
  The new fact ties the JSON's `core.*` items to the same platform-alone composition PC-F84
  goldens, so the source is the same emit.

### Overlap with PC-118

- `client/projects/core/tellma-core-testing/README.md`: one paragraph of three lines added after
  the `tmTestEntities()` example (the "`fixtures/entity-model/`" section), stating that
  `tmTestMetadata()`/`tmTestEntitiesChunk()` take their `core.*` entities and enums from the same
  recording. The table row and everything else in the file are untouched.
- The api golden is not changed: `api:check` passes, and no public signature changed (only TSDoc
  prose on `tmTestMetadata`/`tmTestEntitiesChunk`).

### Gates run locally

- `pnpm run typecheck`, `typecheck:emitted` (47.7 s, both projects), `lint`, and `lint:test` (517
  tests) all passed.
- `rm -rf client/dist`, then `pnpm run build` and `size:check`: OK, cap line `@tellma/core` 53.59
  of 55. No product code changed. `api:check` and `docs:build` passed.
- Unit, no delay:
  - full `core` project, before the spec fixes: 29 failed in 2 files, 2560 passed;
  - the two fixed files after: 42/42;
  - full `core` at the end: 2589/2589 in 135 files; full `core-testing`: 48/48 in 9 files.
- Unit, under the PC-F86 lazy-delay harness: full `core` and full `core-testing`. The harness was
  reverted afterwards and `git status` was clean.
- .NET: `ClientEntityFixturesTests` 5/5 (the new fact included).
  `dotnet build Tellma.slnx` succeeded with 0 warnings. `dotnet format --verify-no-changes` is
  clean.

### Open risks and observations

- **The platform's own labels.** The platform alone emits 306 label fallbacks: `Core_Blob`,
  `Core_Export`, `Core_Job`, `Core_Notification`, … (`Strings.resx` declares, for example,
  `Core_User_Plural` but not `Core_User`). A distribution's `tmCheckAppConfig` therefore reports
  306 `label-missing` warnings for keys it does not own. This is product behaviour and outside
  this item. I report it for the orchestrator: either core declares those strings, or the check
  stops warning about another feature's labels.
- `gl.Center` in the stand-in (and in the server's test model) has `ManagerId`/`Manager`, which
  the real `Tellma.Module.Gl` `Center` lacks. It is the test distribution's entity, the same as the
  server's test model, and out of this item's `core` scope. If the rule "named like a real entity
  ⇒ real fields" should extend to module entities, that is a wider change to both test models.
- `structuredClone` of about 145 KB per `tmTestEntitiesChunk()` call. It is called a few times per
  spec, which costs nothing measurable next to the 47 s suite.

### What reviewers should examine hardest

- Whether the new C# fact really closes the chain (fixture JSON → test model → platform alone) and
  fails on drift.
- `declared()` in the config-check spec: that it strips fallbacks only from `core.*` items, after
  the patches apply, and that no assertion about the platform's own label handling was lost.
- The error-presenter rewrite: that `Manager.Image` exercises the same code path (two-hop
  resolution, labels joined) that `Manager.Manager` did.
