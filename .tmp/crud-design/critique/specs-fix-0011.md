# Spec 0011 — fix record (2026-09-04)

## Applied

- A-0011-1 (major): §1.3 `Application Name` is now `DeploymentIdentity.DeploymentId`, restating spec 0010's composition rule; the `<slug>-web|migrator|worker` list is gone.
- A-0011-2: §5.2 (`Update` row, `UpdateSpec.Assignments` remark) and §9.1 state the carve-out mechanically — the compose-time check admits `IsActive` of an `IActivatable` as the one `WriteOnce` column an `Update` may assign; no new `UpdateSpec` flag (seam unchanged).
- A-0011-3: §5.2 `Purpose` row says `Maintenance` carries the `System` prologue variant (§5.3); §5.5 now gives a `Maintenance` batch step 1 with that variant before steps 3, 6, 7, 8.
- A-0011-4: §7.3 `TakeAsync` consumer is "callers outside any batch (a job handler of spec 0019 or 0020 inserting rows through `Sql` outside the pipeline)"; the enqueue example is gone; buffer-first, round trip only when empty (matches §13.3's assertion).
- A-0011-5: §13.2 `fixture.Tasks` renamed `fixture.Shipments` (`TopLevelEntity<long>`, `IJobEntity`); `fixture.Jobs` avoided because the `core.Jobs` stand-in is named in the same paragraph.
- A-0011-6: §4.1 cascade exception lists every sibling the sibling specs declare: `core.UserStamps`, `core.UserPreferences` (0013), `core.NotificationPreferences` (0020), and `core.ScheduleStates` on `core.Schedules` (0019 §, found while verifying) — phrased as "one-row-per-owner sibling tables their owning specs name".
- A-0011-8: §13.2 states that the fixture entities and `TellmaFixtureDbContext` live in `test/shared/Tellma.Testing.Entities/` (the placement spec 0014 already uses) and that sibling specs add their tables there (`fixture.Lookups` of 0012, `fixture.BlobOwners` of 0016); §1.1 and §14 Projects name the shared project.

## Accepted without a spec change

- A-0011-7: `ITenantDatabase.Linq<TEntity>()` stays in §5.1 (review flag 24); the fix is in seams.md §1, not in this spec.

## Rejected / not this spec's

- A cross-spec (`DetailsPlan.IdsSource` `@tb{b}_main` vs `@tb{b}_keys`): §1.4 reserves `@tb{b}_keys` and lists no `@tb{b}_main`; the reconciliation belongs to seams.md §3.3 and spec 0014 — 0011 is already the consistent side.
- B-0016-4 ("spec 0011's persist assembly"), B-0017-7 (`@tb{b}_main`), B-0017-8 (`CenterType` wording), B-0017-9 (`entity` tag): drift in 0016/0017; 0011's text is the reference.
- B-0020-3 (`cache.changed` has no publisher): 0011's epilogue is spec 0012's bump contributor; the publication call belongs to 0012 (or 0020 drops the row) — no 0011 change.
- Fixture-entity divergence between 0014 §18.2 (`WidgetLine`, `Serial`, `Bucket`) and 0011 §13.2 (`WidgetParts`, `Subject`, `Nodes`): 0014's finding (0014-2); 0011 keeps its table as the owner's definition.
