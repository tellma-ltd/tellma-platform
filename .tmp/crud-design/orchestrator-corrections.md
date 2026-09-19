# Orchestrator corrections to the ledger and seam contracts (2026-09-04)

Apply each correction to `ledger.md` and `seams.md` (and where named, `spec-style.md`) by replacing the
stale text in place — never by appending notes or history. Keep one name per concept across both
files. Where a correction changes a review flag, rewrite the flag to state the new position and the
alternative it replaced. Each correction below is final.

## C1 — `IsActive` is write-once, not server-owned

Overrides ledger §2.5, S5, seam 3's activatable row, and every errata line that says "`IsActive`
server-owned". New rule:

- `IsActive` carries `[WriteOnce]`: a client may set it when **creating** a row (save with `id = 0`,
  import in `Insert` mode, and the insert half of `Upsert`), so imported reference data can arrive
  inactive. After creation it changes **only** through the `activate`/`deactivate` actions
  (`IDataBatch.Update` under the `Activate` securable); a changed value on an update is the
  `WriteOnce` validation error at the path, exactly like `Subject` or `Email`.
- The editable Excel shape **includes** `IsActive` (it is settable on insert); on `Update` and on
  the update half of `Upsert` a changed value is the `WriteOnce` error, reported with the sheet
  coordinates. The display export includes it as before.
- `PropertyOwnership` for `IsActive` is `WriteOnce`; the emitter never puts it in an `UPDATE` set
  list; the `INSERT` takes it from the payload (default `true`).
- The `IncludeInactive` default filter and the `Activate` securable are unchanged.
- Rewrite review flag S5 as: "`IsActive` write-once (creatable inactive through save and import;
  afterwards only through the actions) versus server-owned (always created active; imports cannot
  create inactive rows) versus 0014's diff-gated editable `IsActive`." Keep 0011 flag 2 pointing at
  S5.

## C2 — Tree bases carry no CLR `Parent` navigation; no `TSelf` generics

Overrides ledger §2.3 (the `TreeEntity<TSelf>` line), §4.8.3, errata 0014 #2 and 0017 #11, seam 2's
base shapes (`seams.md` §2.1 lines defining `TreeEntity<TSelf>` and `ActivatableTreeEntity<TSelf>`),
seam 3's tree row, and the two illustrations in `seams.md` (`MyCenter : Center<MyCenter>`,
`UseEntity<Center, MyCenter>`). New rule:

- `TreeEntity : TopLevelEntity` (and `TreeEntity<TKey>`) declares `ParentId: TKey?` and
  `SubtreeCount`; `ActivatableTreeEntity : TreeEntity, IActivatable` adds `IsActive` and
  `ActiveSubtreeCount`. Neither declares a CLR `Parent` property. The tree convention configures
  the self-referencing FK without a navigation; the Queryex navigation `Parent` is derived from
  the FK column name exactly as every other navigation-less FK is (`ParentId` → `Parent`), so
  `Parent.Name`, `descendantOf`, `ancestorOf` and `level(Node)` work unchanged.
- `Center : ActivatableTreeEntity` is a single non-abstract, unsealed class in
  `Tellma.Module.Gl.Abstractions`; there is no `Center<TCenter>` generic base. A distribution
  extends it by plain inheritance (`MyCenter : Center`) like `User` and `Role`, and registers the
  leaf with `tellma.UseEntity<Center, MyCenter>()` where `TLeaf : TDefault` is the one constraint
  for every entity kind.
- The in-memory graph a details read returns still carries the parent row in `Related` (the
  `Parent` navigation is in the default `[RelatedSelect]` projection), so the UI needs no CLR
  navigation; EF LINQ users of the `Linq<T>()` escape hatch join on `ParentId` explicitly.
- ARCHITECTURE.md's generic-base fork pattern remains available for pack entities whose
  navigations point at *other* entities; the ledger records that self-referencing trees do not use
  it. Add review flag S38: "No CLR `Parent` on tree bases (plain-inheritance extension, one
  `UseEntity` constraint) versus `TreeEntity<TSelf>` with a typed `Parent` (LINQ ergonomics; every
  tree extension must close a generic)."
- Fix the `seams.md` illustrations accordingly (`public sealed class MyCenter : Center { … }`).

## C3 — ImageSharp stays isolated in `Tellma.Core.Imaging`

Overrides ledger §2.17 (the `Tellma.Core` package line and the `SixLabors.ImageSharp` reference),
S1's parenthesis, errata 0010 #1 and 0016 #2, and `seams.md` §0.1's package table. New rule:

- `Tellma.Core.Imaging` is a separate Core-layer package (`src/core/Tellma.Core.Imaging/`) that
  references `Tellma.Core.Abstractions` and `SixLabors.ImageSharp` only, and ships
  `ImageSharpImageProcessor : IImageProcessor` plus `services.AddTellmaImageSharp()`.
  `Tellma.Core` does **not** reference it or ImageSharp. The reason is licence isolation: the
  Six Labors split licence is the one dependency whose reading Ahmad must confirm, and a swap to
  SkiaSharp must be a package swap in the distribution, not a change inside `Tellma.Core`.
- Composition: the reference distribution references `Tellma.Core.Imaging` and calls
  `services.AddTellmaImageSharp()`; alternatively `tellma.Blobs(b => b.ImageProcessor<T>())` names
  any `IImageProcessor`. The realised startup gate fails when any `Image`-kind blob kind exists and
  no `IImageProcessor` is registered (already the rule).
- Everything else in §2.17 stands (`DocumentFormat.OpenXml`, `Cronos`, `MessageFormat`,
  HierarchyId in `Tellma.Core`). Update §6's package-naming row and the 0016 errata to match.

## C4 — The distribution THROW band moves to 50600–50699

`TellmaSqlErrors` currently reserves `DistroMin = 50500`, `DistroMax = 50599` while also defining
`Transient = 50503` inside that range. Fix in ledger §2.20, seam 1's `TellmaSqlErrors` line, and
`seams.md` §1.2: the platform owns `50400–50599` (`50401`, `50403`, `50404`, `50409`, `50412`,
`50413`, `50422`, `50428`, `50503`); distributions and packs raise their own guards in
`50600–50699` (`DistroMin = 50600`, `DistroMax = 50699`), which the executor maps to
`BatchAssertionFailedException` → `ValidationException` with the message as the code. State the
rule once: no number outside these two bands may be thrown from platform or distribution SQL.

## C5 — Spec 0020 is accepted; file names

Eleven specs. Update `spec-style.md`'s file-name table: 0019 is
`docs/specs/0019-background-jobs-and-scheduler.md` and 0020 is
`docs/specs/0020-notifications-inbox-and-hub.md`. The ledger's §0 table already lists 0020; add
one sentence to §0 saying the breakdown's optional split is taken and 0020 reads the
`background-inbox` decisions file for the inbox, notification and hub decisions (D15–D20 there)
while 0019 reads the rest.

## C6 — Reference distribution slug

Keep `distributions/acme/` and slug `acme`, but add review flag S39: "`acme` as the reference
distribution's slug (a fictional customer, so the folder exercises the real distribution shape and
`Tellma.Distro.Acme.*` names) versus a slug that says what it is (`reference`, which must then be
added to the reserved list)."

## C7 — Consistency sweep after C1–C6

After applying the above, grep both files for `server-owned` next to `IsActive`, for `TSelf`,
`Center<`, `Tellma.Core.Imaging`, `ImageSharp`, `50500`, `50599`, `DistroMin`, and fix every
remaining occurrence. Do not change anything else. Return a list of every passage changed
(file, section, one line each).
