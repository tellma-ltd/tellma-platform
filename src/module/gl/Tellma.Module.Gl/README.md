# Tellma.Module.Gl

The general ledger module: the responsibility centers' stack, the feature a distribution composes,
and the sample tree a development tenant starts from. Its contract lives in
[Tellma.Module.Gl.Abstractions](../Tellma.Module.Gl.Abstractions/README.md); this package references
that, the platform's contract surface and the two `Microsoft.Extensions` abstractions it injects, and
never `Tellma.Core` or an Entity Framework package. The unit tests hold it to that closure.

## Composing it

```csharp
services.AddTellma("acme", configuration, environment, tellma => tellma
    .AddFeature<GlFeature>());
```

**[GlFeature](GlFeature.cs)** (`gl`) requires no other feature and contributes three items: the
centers' stack over `CenterService<Center>`, the `gl.sample-centers` provisioning step, and
`DefaultGlSampleCenters` as the `IGlSampleCenters` singleton. It writes no model configuration and no
securables: the stack's descriptor registers `gl.Center` for `Read`, `Save`, `Delete` and `Activate`,
and the web edge projects the routes under `/{tenantId}/api/web/centers/`. A distribution extends
`Center` by inheritance and registers its leaf; the service and the step are closed over it.

## The rules

**[CenterService](CenterService.cs)** keeps the tree's shape:

- **A parent groups.** A row's parent, as the payload saves it or as stored, is an abstract center or
  a business unit, else `Gl.Centers.ParentMustBeGrouping` at the row's `ParentId`. Parents the payload
  does not carry load in the round the save's first round trip already makes, so a save costs two
  round trips. A parent the platform already refused (not found or hidden) is not judged again, so a
  hidden parent's type never shows through an error, and a row under itself is the platform's
  `Tree.Cycle`.
- **A grouping center stops grouping only once it has no children.** An update that changes a stored
  center to `Service`, `Operation` or `Sale` while it keeps a child is refused at its `CenterType`. A
  child it keeps is another payload row naming it as parent, or a stored child the payload does not
  move elsewhere; the second is decided exactly from the before images' subtree counts, so a save
  that moves every child away and changes the type in one go is accepted.
- **A delete leaves no child behind.** A delete by ids is refused with `Gl.Centers.HasChildren` at
  `Ids[i]` for a target whose subtree holds a center the delete does not name; naming the whole
  subtree is accepted, as the self-referencing foreign key accepts it. A delete with descendants is
  never refused here. The targets load with the delete's first round trip, so a delete costs two.

Activation and deactivation take one round trip, the tree's recount included: the service adds no
rule to them.

## Sample centers

**[GlSampleCentersStep](GlSampleCentersStep.cs)** (`gl.sample-centers`, order 100, the version of the
registered `IGlSampleCenters`) writes the sample tree into a tenant when the deployment runs in
Development, and only records its completion anywhere else. It first refuses a list that cannot be
written as given (a missing code or name, a code given twice, a parent code that names no other
center of the list), naming the implementation, before anything is read. It then finds the stored
centers of the list's codes in one query, inactive ones included, and saves the whole list as the
system in one save: a stored center keeps its id, so a re-run at a higher version updates names,
types and parents and never duplicates, and a new center and every reference to it take a temporary
negative id. A center's names fill the tenant's language slots by language subtag, the primary
falling back to the center's first name. The step costs three round trips, and none for an empty
list. It logs `SampleCentersProvisioned`.

**[DefaultGlSampleCenters](DefaultGlSampleCenters.cs)** is the default tree (version 1): `HQ` over
the `BU-TRD` and `BU-MFG` business units, their operating and sales centers, and the `SV-IT` and
`SV-HR` service centers, named in English and Arabic.

## Strings

`Resources/Strings.resx` (English) and `Resources/Strings.ar.resx` (Arabic), base name
`Tellma.Module.Gl.Resources.Strings`, hold the center's labels, the type names, the two validation
messages, the feature's label, and two keys the browser module uses: the `Code` field's hint and the
launcher's Financials section.

## License

Apache-2.0
