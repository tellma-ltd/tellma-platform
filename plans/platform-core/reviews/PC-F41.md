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

### Responses
