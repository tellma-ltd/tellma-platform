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

## Review 1

Reviewed 8b07aa85 against base 13cbb8e5, with the PC-F94 ledger row, D-283 and D-321. There is no
CHECKPOINTS entry (a fix item), so I checked against the ledger row and spec 0024's routing and
config-check rules that the changed specs exercise.

Runs, this review:

- `dotnet build test/core/Tellma.Core.Tests` printed "Build succeeded", 0 warnings.
  `ClientEntityFixturesTests` passed 5/5, including the new fact.
- `ng test core-testing` (full) passed 48/48 in 9 files.
- `ng test core` passed 244/244 in 17 files. The run included `tm-check-app-config.spec`,
  `tm-error-presenter.spec`, `src/attention/**`, `src/routes/**` and `src/nav/**`.

Verified, with no finding:

- **The stand-in matches the emit.** Every `core.*` summary, entity and enum now comes from
  `fixtures/entity-model/*.json`. Those files are goldened against the test model. The new fact
  composes `TestComposition.Compose(static _ => { })`, which is the same platform-alone composition
  the PC-F84 declarations fact uses. It compares each of the three sections' `core.*` items as
  indented JSON. So the chain from fixture to test model to platform alone is closed: if the test
  model's features ever changed a core entity, the fact fails with a diff.
- **The recording is self-contained.** In the recording, core entities navigate only to core
  entities, and their children are core too. None of the hand-written entities collides with a
  `core.*` name. `byName` sorts by ordinal, the same order as the `sort()` assertion.
- **The reserved path is correct.** In the recording, `core.Report` has the segment `reports`. Spec
  0024's route table allows a page only under the segment of an entity its own feature declares
  (`reports/:id/studio` is core's own page). So `acme` declaring `reports/:id/studio` is rightly
  `reserved-path`. `customers/:id/studio` is allowed because `acme` declares `acme.Customer`, and
  the existing `customers/map` case pins the same rule.
- **The error-presenter rewrite keeps the test.** The presenter only calls
  `TmEntities.resolve(entity, path).label()`. `Manager.Image` (gl.Center to core.User to
  core.Blob) is still a two-navigation resolution over real fields. The spec now also asserts that
  the path resolves.
- **The attention and route rigs are correct.** The real `core.Notification` and `core.Report`
  replace the duplicates, and the specs that cover them pass. `core.Dashboard` stays hand-written
  because the server does not model it yet; that is justified in the notes.
- **The label filtering hides no defect today.** Before this change, no assertion covered a core
  entity label fallback: the hand-written core entities had no `labelFallback`. The label tests
  patch acme entities, a gl enum and a job handler, and `declared()` touches none of them. The 306
  warnings are a real product gap, already opened as PC-F103 (D-321). R1-1 covers the latent trap.
- **Both deviations are justified.**
  - No spec needed the removed fields. The one spec that used them now runs over real
    navigations, which is a stronger test than a made-up test entity.
  - A `.d.ts` cannot yield runtime metadata, and the new fact ties the JSON to the same
    composition PC-F84 goldens.
- **The tests listed were the right ones.** Nothing outside `core` and `core-testing` consumes
  `tmTestMetadata`, `tmTestEntitiesChunk`, `provideTellmaTesting` or `tmRouteTestMetadata` (grep
  over `client/`). So the touched-package gates listed in the commit are the right set.
  `tokens:check` does not apply, because no CSS changed.

### Findings

**R1-1 (minor): `declared()` also strips label fallbacks that a test patches in itself.**
Location: `client/projects/core/tellma-core/config-check/src/tm-check-app-config.spec.ts:96-112`.

`coreChunk(patch)` and `entitiesChunk(patch)` apply the patch first and then drop every
`labelFallback` from every `core.*` item. A later spec that patches a core entity, enum value or
action with a fallback loses it silently. For example, a spec that checks `label-missing` for a
core label is attributed to the `core` feature. The check then sees nothing:

- an expected warning fails with an empty result that is hard to read;
- an assertion that no warning appears passes vacuously.

The implementer notes call "after the patches apply" intended, but nothing in the spec tells a
future author.

Suggested fix: strip only the recording's own fallbacks. For example, `declared()` could drop a
`labelFallback` only when it equals the value at the same item in `tmTestEntities()`. Or apply it
only when the patch does not set `entities` or `enums`, and derive the spec's own patches (lines
551-555, 572-579, 588 and 632) from `coreChunk().entities` and `entitiesChunk().entities`. At the
least, state in `declared()`'s TSDoc that a patched core fallback is dropped. Remove `declared()`
once PC-F103 re-records the fixtures without core fallbacks.

**R1-2 (minor): the new closure check skips the references in action fields.**
Location: `client/projects/core/tellma-core-testing/src/tm-test-metadata.spec.ts:21-36`.

The comment says that every named enum resolves inside the chunk. The check walks only
`properties[].enum`, navigations and children. It does not walk `actions[].fields[].value.enum` or
`value.target`.

The recording has 10 enum references and 3 target references in action fields. Three enums are
named only there: `core.ConcurrencyMode`, `core.MeLocaleMember` and `acme.Priority`. Suppose a
change narrowed `PLATFORM_ENUMS`, for example to "the enums the properties name", or a
hand-written action field named a missing enum or entity. The check would stay green, while
`TmEntities` and the action dialogs would meet a dangling name.

Suggested fix: also iterate `entity.actions`. For each `field.value`, expect `value.enum` to be in
`enums` and `value.target` to be in `names` whenever they are set.

**R1-3 (minor): nothing tests the per-call deep copy.**
Location: `client/projects/core/tellma-core-testing/src/tm-test-metadata.ts:234,307-308` and
`tm-test-metadata.spec.ts:50-54`.

`PLATFORM_SUMMARIES`, `PLATFORM_ENTITIES` and `PLATFORM_ENUMS` alias the imported JSON module
objects. `tmTestEntities()` shares those objects, and the unit runner shares one page between a
run's files. Only `structuredClone` keeps one spec's in-place patch, such as
`entity.properties.push(...)` or a changed `labelFallback`, from leaking into every later call. The
only freshness assertion is the top-level `expect(tmTestMetadata()).not.toBe(tmTestMetadata())`,
and it still passes if both `structuredClone` calls are removed.

Suggested fix: add assertions for three things:

- a nested object differs between calls, for example
  `tmTestEntitiesChunk().entities[0].properties` and `tmTestMetadata().entities[0]`;
- mutating one call's core entity does not change the next call's;
- mutating one call's core entity does not change `tmTestEntities()`.

**R1-4 (minor): a dead string key is left over from the old stand-in's label key.**
Location: `client/projects/core/tellma-core/src/nav/tm-navigation.spec.ts:103`.

`core_Report_Plural: 'Reports'` matched the plural label key of the old hand-written `core.Report`
in `tmRouteTestMetadata()`, which `summary()` built from `name.replace('.', '_')`. The real
summary's key is `Core_Report_Plural`, so the entry no longer matches anything. Nothing asserts on
it, because the spec gives `core.Report` no section. Yet it reads as the label of the platform's
reports, and the implementer's grep for `core_*` keys missed it. If a later spec gives
`core.Report` a section and expects "Reports", that spec gets the fallback instead and the cause is
not obvious.

Suggested fix: rename the key to `Core_Report_Plural`, or delete the entry.

### Recommendation on the open question (gl.Center's ManagerId and Manager)

Do not extend the "real names mean real fields" rule to module entities in this item, and do not
rename now.

- **The risk is low.** The rule exists because core's code consumes core's entity shapes, so a fake
  core field can make core code pass against data the product never sends. The client has no `gl`
  package (`client/projects/` holds only core, internal and locale). Core's code is generic over
  entity metadata, so `gl.Center.Manager` cannot hide a defect in code that reads a real Gl Center.
  The real hazard is confusion: the server's test model (`ClientModel.Centers`), the client
  stand-in and even product documentation (`client/projects/core/tellma-core/README.md:818`,
  `resolve('gl.Center', 'Parent.Manager')`) present a `gl.Center.Manager` that
  `Tellma.Module.Gl.Abstractions/Center.cs` does not have.
- **A rename is not cheap.** About 70 client files name `gl.Center`, 15 of them with `Manager` and
  about 61 lines. On the server, the rename would touch `ClientModel.cs`, the `ClientEntityModelTests`
  InlineData, `LabelChainsTests`, the `Gl_Center_*` label keys, and a re-record of both JSON
  fixtures and the test-model declarations. It would also overlap PC-118 and PC-F103's re-record.
- **Suggested now:**
  - say in the TSDoc of `ENTITIES` and of `ClientModel`'s `Center` that `gl.Center` is the test
    distribution's entity, not `Tellma.Module.Gl`'s;
  - fix the core README example to a path the real model has, or to a test entity.
- **Suggested later:** open a low-priority ledger item to move the test distribution off the `gl`
  key (for example to an `acme.*` or `test.*` entity). Trigger it when a client gl package lands or
  when a server test composes `Tellma.Module.Gl` alongside the test model, whichever comes first.

### Responses

- R1-1: fixed in the fix1 commit. `declared()` now drops a `core.*` label fallback only where the
  recording (`tmTestEntities()`) has the same fallback at the same place. It walks the item in step
  with its recorded counterpart and matches array elements by `name` (enum values by `value`),
  falling back to the index. A fallback that a spec patches in, whether new or changed, survives.
  New test: "warns label-missing for a platform label a spec patches in, and drops only the
  recording's own".
  - It changes `core.User`'s summary fallback to 'Person', and adds a `core.Role` property with
    its own fallback.
  - It expects exactly those two warnings, so the recorded fallbacks on the same entities are
    still dropped.
  - The old `declared()` dropped the patched 'Person', so it would have failed this test.
- R1-2: fixed. The closure check also walks `actions[].fields[].value.enum` (it must be in the
  chunk's enums) and `value.target` (it must be in the chunk's entities).
- R1-3: fixed. New test: "returns an independent deep copy on every call, apart from the recording".
  - It mutates one call's chunk in place: it pushes onto `core.User`'s properties, renames its
    first navigation's label key, empties a platform enum's values and the `acme.Customer`
    property list, and renames the `core.User` summary's label key from a `tmTestMetadata()` call.
  - It then asserts that a second call of each function, and a fresh `tmTestEntities()`, equal
    the snapshots taken before the mutation.
  - With the three `structuredClone` calls removed, the test failed; with them restored, it passed.
- R1-4: fixed. `core_Report_Plural` became `Core_Report_Plural`, the real `core.Report` summary's
  plural key.
- gl.Center, as ruled:
  - The TSDoc of the stand-in's `ENTITIES` says that `gl.Center` follows the server's test model,
    which has a `Manager`, and not the real general ledger module's center.
  - The core README's `resolve` example (formerly `gl.Center`, `Parent.Manager`) now reads
    `resolve('core.RoleMembership', 'Role.ModifiedBy')`. That is a real two-hop navigation end,
    with fkPath `Role.ModifiedById`.
