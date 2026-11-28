# PC-F41: startup gate refusing implicit shadow properties

## Implementer notes

### What was built

- **The gate.** `EntityMetadataBuilder` gains phase 5, `CheckShadowProperties`. It runs inside the existing `core.entity-metadata` startup check, so every problem is aggregated with the other entity-contract problems and the host refuses to start.
  - It walks every entity type of the runtime model, not only the entity leaves: Core's migration-only tables, owned types and shared types are covered too.
  - It reports each shadow property once, on the type that declares it (`GetDeclaredProperties`).
  - Files: `src/core/Tellma.Core/Data/Metadata/EntityMetadataBuilder.cs` (phase 5 and `ReportShadow`).
- **The allow-list.** `src/core/Tellma.Core/Data/Metadata/PlatformShadowColumns.cs`. `IsDeclared(entityType, name)` is true for exactly three cases:
  - `ValidFrom`/`ValidTo` on a temporal entity type (`IsTemporal()`);
  - `Node` on a tree (`EntityShapes.IsTree`);
  - the model-only machinery columns, keyed by CLR class: `Job` → `StateJson`, `LeaseToken`, `ConfirmedKeys`; `Blob` → `StorageKey`, `Sha256`. The names come from the `JobConfiguration` and `BlobConfiguration` constants. Both classes are sealed and no leaf replaces them, so the class identifies the table. Only Core writes model configurations, so nothing outside Core can extend the list.
- **Messages.** There are three shapes, each with a fix:
  - a shadow FK that EF Core adds for a navigation on the dependent ("Remove the navigation … and declare … [References(typeof(T))] or, for a child's parent, [ParentKey]");
  - a shadow FK for a navigation on the principal, i.e. a mapped collection ("Mark T.Items [NotMapped] …");
  - a shadow FK or column a model configuration names (generic remedy).
- **Overlap with existing rules.**
  - `CollectReferences`' "has a foreign key over X, which is not a property of the class" now skips a shadow dependent that the gate reports, so there is one problem per cause. It still reports a non-shadow, undescribed dependent, and a dependent over an allowed shadow column.
  - The discriminator of a hierarchy with derived types is not reported, because phase 1 already refuses the derived or re-added type. A discriminator on a lone type is reported.
- **Neighbouring fix.** `core.Job.ConfirmedKeys` was missing from `QueryexSchemaProvider.Withheld`, so it would have reached the query language. Spec 0020 §2.3 says all three job machinery columns are absent from the Queryex schema. The test now cross-checks that every machinery column in the allow-list is withheld.
- **Existing models.** No real shadow property was found in Core's tenant model (`AccessModelTests` now pins the exact set: period columns on temporal tables plus the five machinery columns), the fixture model (Tellma.Core.IntegrationTests boots it through the gate), or the acme distribution (its integration and web suites boot it). No model fix or migration change was needed.
- **Docs.**
  - ARCHITECTURE.md's "No implicit shadow properties" bullet now names the startup gate instead of an analyzer, and lists the machinery columns as the third declared kind.
  - The `Tellma.Core` README's rule list gains the shadow rule.

### Tests run

- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings. `dotnet format --verify-no-changes`: clean.
- Unit filter, one project at a time: Tellma.Core.Tests (3967 passed, 1 skipped), Tellma.Core.AspNetCore.Tests (494), Tellma.Distro.Acme.Web.Tests (27), Tellma.Defaults.Azure.Tests (11).
- Integration on Testcontainers: Tellma.Core.IntegrationTests (307), Tellma.Distro.Acme.IntegrationTests (11). The other touched projects have no integration tests.

### Deviations

1. The allow-list holds five machinery columns beyond the period columns and `Node`.
   - Spec 0012 §14, ARCHITECTURE.md and the item text speak of only the period columns and `Node`. Spec 0020 §2.3 and spec 0017 §2.1-§2.2 mandate the Jobs and Blobs columns as shadow properties, "never members".
   - Making them members would contradict those specs and expose them on the class.
2. The FK-over-shadow-column problem is now reported by the shadow rule, with its cause, rather than by the reference rule (test renamed and its expected text changed).
3. `core.Job.ConfirmedKeys` is now withheld from the Queryex schema (a spec 0020 §2.3 conformance fix).

### Open risks

- Scope is the tenant model (`TellmaDbContext`), which is where the `core.entity-metadata` check runs. The catalog model (`CatalogDbContext`, Core-only) and the identity server's own context (OpenIddict's vendor entities) are not gated. Neither takes pack or distribution input.
- The period columns are recognised by the temporal convention's names. EF's `GetPeriodStartPropertyName()` throws on a runtime entity type, so it cannot be used. Only `TemporalConvention` makes a table temporal today; a Core configuration that called `IsTemporal()` with EF's default `PeriodStart`/`PeriodEnd` names would be refused. That is intended, since the platform's period columns are `ValidFrom`/`ValidTo`.

### What reviewers should examine hardest

- Whether keying the machinery columns by CLR class (rather than by table) is right. The metadata tests' stand-ins `MdJob`/`MdBlob` map `core.Jobs`/`core.Blobs` without those columns.
- The discriminator skip and the `CollectReferences` skip: they must not hide a problem that no other rule reports.
- Whether any other part of the model can hold a shadow property that `GetEntityTypes()`/`GetDeclaredProperties()` misses. Complex types are not walked: EF Core's model validator already refuses every shadow property on a complex type except its own discriminator (`ComplexTypeShadowProperty`).

## Review 1

### Findings

Reviewed commit 8fbefe3c only. Verified by building Tellma.Core.Tests (Build succeeded, 0 warnings) and running its unit filter (3967 passed, 1 skipped). Also ran Tellma.Core.IntegrationTests on Testcontainers: 306 of 307 passed.
- The one failure was `VersionTagSeedTests.A_name_registered_later_…`, a SqlClient "Connection Timeout Expired … post-login phase" on the catalog connection.
- That class passed 6/6 on rerun. It is an infrastructure flake, unrelated to this change. Probed the gate with a scratch test project under `.tmp/pcf41-r1-probe/`. That project is gitignored and builds the stand-in model through `TellmaModelFactory` and `EntityMetadataBuilder.Build`, exactly as `MetadataModels` does. Probe results:

- Self-collection and TPH-derived types: refused once, correctly.
- Many-to-many: passes with no problem (R1-1).
- `[Owned]` collection: refused twice, with remedies that are wrong for an owned type (R1-2).
- `[Owned]` reference on a `[TableType]` entity: the table-type convention throws, with a clear message.

**R1-1 (major): a many-to-many skip navigation passes the gate and grows a join table with columns no class carries.**
- Where: `src/core/Tellma.Core/Data/Metadata/EntityMetadataBuilder.cs:313-333` (`CheckShadowProperties`), together with `:170` (phase 1 skips shared-type entity types).
- Scenario: a pack declares `class PxA : TopLevelEntity { public List<PxB> Bs { get; set; } = []; }` and `class PxB : TopLevelEntity { public List<PxA> As { get; set; } = []; }`, neither `[NotMapped]`.
  - EF Core's convention makes them skip navigations over a shared-type join entity `PxAPxB` (`Dictionary<string, object>`) whose FK columns `AsId` and `BsId` are indexer properties.
  - `IsShadowProperty()` is false for an indexer property, because its `PropertyInfo` is the indexer. Phase 1 skips the shared type. No other rule looks at skip navigations: `MappedMembers` finds no `IProperty` for a navigation member and reports nothing.
  - The probe's build returned `problems=0`. The migration would gain a `PxAPxB` table and two FK columns that no class, metadata entry or Queryex schema describes.
  - This is the failure the new ARCHITECTURE.md bullet promises to stop: "a navigation without an explicit FK property fails the composition rather than growing a hidden column". The item's own brief also names join entities for skip navigations as a case to refuse.
- Suggested fix: in phase 5, also report every skip navigation (`entityType.GetDeclaredSkipNavigations()`), naming both collections. Remedy: mark them `[NotMapped]`, and model the link as a child entity with a `[ParentKey]` and a `[References]`.
  - Also refuse any indexer property, or any shared-type entity type outside Core's own. Core's model has none: `src` holds no `SharedTypeEntity`, `IndexerProperty` or `UsingEntity`. Report it once per join type, not once per column.
  - Add a malformed pair and a `Single`-problem test.

**R1-2 (minor): an owned type is refused only through its shadow keys, with remedies that do not name the cause.**
- Where: `src/core/Tellma.Core/Data/Metadata/EntityMetadataBuilder.cs:339-380` (`ReportShadow`; the principal-side branch is at `:362`, the generic branch at `:376`).
- Scenario: a pack marks `class PxAddress { public string? Street { get; set; } }` with `[Owned]` and maps `public List<PxAddress> Homes { get; set; }` on a leaf. The gate reports two problems.
  - First problem: "PxAddress maps PxOwnerManyId, a shadow foreign key … EF Core adds for the navigation PxOwnerMany.Homes". Its fix says to declare `PxOwnerManyId` on `PxAddress` "marked [References(typeof(PxOwnerMany))] or … [ParentKey]".
  - Second problem: "PxAddress maps Id, a shadow property". Its fix says "Declare Id … or remove the model configuration that maps it", but no configuration exists.
  - Following either remedy leaves an owned type in the model. The real cause, `[Owned]`, is never named.
  - The case is refused, so this is not a hole. But the brief asks that each case be "refused with a clear message naming the cause".
- Suggested fix: when `entityType.IsOwned()`, report once per owned type and skip its other shadow properties. For example: "The entity X maps T as an owned type through X.Nav; the platform maps no owned types". Fix: "Remove [Owned] from T, or mark X.Nav [NotMapped]". Pin it with a test.

**Judgements on the implementer's flags (no finding):**

1. **The allow-list is keyed precisely.**
   - `PlatformShadowColumns.IsDeclared` keys on the CLR class (`typeof(Job)`, `typeof(Blob)`, both sealed) plus an ordinal name.
   - Period columns need `IsTemporal()`, and `Node` needs `EntityShapes.IsTree`.
   - `ShadowedMachineryConfiguration` proves that a same-named `StateJson` elsewhere is refused. `ShadowedNodeConfiguration` and `ShadowedPeriodConfiguration` prove the same for the other two kinds.
   - Only Core can add a configuration, since `ModelContributionItem` and its extensions are internal, so the list stays closed.
   - The extra five columns are justified. Spec 0020 §2.3 says "shadow properties, never members", and spec 0017 §2.2's `Blob` class omits `StorageKey`/`Sha256`.
   - For Ahmad: spec 0012 §14's Docs bullet still says "two platform shadow properties exist". It should name the machinery columns, matching the updated ARCHITECTURE.md.
2. **Gating only the tenant model is right.**
   - The rule protects migration determinism across distributions, and only the tenant model takes pack and distribution input.
   - `CatalogDbContext` is Core-only.
   - The identity server's OpenIddict entities carry vendor navigations with shadow FKs by design, so gating them would refuse the vendor model.
   - ARCHITECTURE.md now says "tenant model" explicitly.
3. **The `core.Job.ConfirmedKeys` fix is correct and required.**
   - Spec 0020 §2.3 says all three job columns are absent from the Queryex schema.
   - `JobConfiguration` maps `ConfirmedKeys` as `varchar(2000)`, which `QueryexStoreTypes` types, so before this fix it would have been a queryable property of `core.Job`.
   - The new assertion fails without the fix. The cross-check from `PlatformShadowColumns.Machinery` to `Withheld` stops the two lists from drifting in that direction.

**Test adequacy and suites (no finding):**
- Each new rule has a test that would fail if it broke:
  - the navigation, collection and configuration-FK shadows;
  - an undeclared column, and a declared name on the wrong entity (Node, period, machinery);
  - a lone discriminator;
  - the positive period/Node set over the sound model.
- The positive machinery path runs through the real gate in `AccessLeafTests.The_extended_model_passes_the_startup_gate`, whose composition includes `core.Blobs` and `core.Jobs`.
- The discriminator skip is pinned by the existing `SingleProblem(typeof(Customer), typeof(VipCustomer))`. The `CollectReferences` skip is pinned by the renamed `…_is_refused_once` test.
- The suites listed in the commit match PLAN.md §7.1 item 4's closure of `Tellma.Core`:
  - Unit filter: Core, Core.AspNetCore, Acme.Web and Defaults.Azure.
  - Integration: Core.IntegrationTests and Acme.IntegrationTests.
  - Acme.E2E has no tests.
  - The Identity, connector, Email and Testing test projects do not reference `Tellma.Core`.

### Responses

- R1-1: fixed in "PC-F41: review 1 fixes" (this commit, child of 8fbefe3c). Phase 5 first collects every skip navigation by its join entity type. It then reports each join table once, naming every collection through it ("The model maps Linked.Linkers and Linking.Links of … as a many-to-many link, for which EF Core adds the join table LinkedLinking with columns no class carries"). The remedy is to mark them `[NotMapped]` and store the link as a child entity with a `[ParentKey]` and a `[References]`. The join's own columns are not reported separately. Indexer properties now count like shadow properties in the column loop, so a property-bag type that is not a join is refused too, column by column. Pinned by `A_many_to_many_pair_of_collections_is_refused_once_for_its_join_table` (the `Linking`/`Linked` pair, single problem, exact problem and fix text).
- R1-2: fixed in the same commit. An owned entity type is now reported once, naming the navigation that owns it ("The entity … HomeOwner maps … Home as an owned type through HomeOwner.Homes, whose keys no class carries; the platform maps no owned types"). The remedy is to mark the navigation `[NotMapped]` and remove `[Owned]` or the `OwnsOne`/`OwnsMany`, or to make the type a child entity with a `[ParentKey]`. Its shadow keys are no longer reported one by one. Pinned by `An_owned_type_is_refused_once_naming_the_navigation_that_owns_it` (an `[Owned]` collection, single problem, exact text). The `Tellma.Core` README's rule list names both cases.
