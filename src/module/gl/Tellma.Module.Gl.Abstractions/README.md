# Tellma.Module.Gl.Abstractions

The general ledger module's contract surface, and the package a distribution's own modules reference
to build on GL. It references only `Tellma.Core.Abstractions`, so a module that names a center never
reaches `Tellma.Core`, Entity Framework or the GL implementation. The namespace is `Tellma.Module.Gl`,
shared with [Tellma.Module.Gl](../Tellma.Module.Gl/README.md), so a type keeps its name if it ever
moves between the two.

## What is in it

**[Center](Center.cs)** is the responsibility center: a node of the tenant's tree of business units,
cost centers and revenue centers. It is an `ActivatableTreeEntity`, so `ParentId`, `SubtreeCount`,
`ActiveSubtreeCount` and `IsActive` come from the platform, and its annotations alone produce the
`gl.Centers` table through the platform's conventions: the `gl.sq_Centers` sequence from 1000, the
`CentersList` table type, `CenterType` stored by name as `varchar(12)`, the tree's hierarchy column
and foreign key, the unique `Code` natural key and the `(IsActive, Name)` listing index. `Name` is
multilingual (`Name2`, `Name3`), and both `Name` and `Code` are searchable. A distribution extends it
by plain inheritance and registers its class as the leaf; the class has no navigation property, since
the query language derives `Parent` from `ParentId`.

The stack serves every operation but delete by query: a center with children must not be deleted on
its own, and a delete by query runs no delete rules, so centers are deleted by their ids or with
their descendants.

**[CenterType](CenterType.cs)** and **[CenterTypes](CenterTypes.cs)**: `Abstract` and `BusinessUnit`
centers group others; `Service`, `Operation` and `Sale` centers have no children.

**[GlModule](GlModule.cs)**, **[GlResources](GlResources.cs)** and
**[GlValidationCodes](GlValidationCodes.cs)** name the feature (`gl`, what a dependent feature
requires), the schema, the `gl.Center` securable and the two validation codes the centers' rules
report.

**[IGlSampleCenters](IGlSampleCenters.cs)**, **[SampleCenter](SampleCenter.cs)** and
**[SampleCenterName](SampleCenterName.cs)** are the seam of the development tenants' sample tree. GL
registers a default; a distribution built for one customer or sector replaces it from a feature that
requires `gl` and registers its own `IGlSampleCenters` singleton. Each center names its parent by
code, and its names by language subtag, the first being the fallback.

## License

Apache-2.0
