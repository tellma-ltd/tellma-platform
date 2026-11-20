# PC-F62: DeletePlan declares the tables written by Cascade/SetNull referencing foreign keys

## Implementer notes

### What was built

- **`ForeignKeyActions`** (`src/core/Tellma.Core/Data/Writes/ForeignKeyActions.cs`), a new
  internal singleton registered in `DataComposition`. It reads every foreign-key constraint of the
  tenant model's **relational** model (`IModel.GetRelationalModel()`, `ITable.ForeignKeyConstraints`)
  whose `OnDeleteAction` is `Cascade`, `SetNull` or `SetDefault`, grouped by the referenced
  table. So it counts every key, whether a convention (sibling, `IJobEntity`) or a model
  configuration declared it, and whether or not the referencing table is an entity
  (`core.ScheduleStates`, `core.UserStamps` and the preference tables are not).
  `WrittenByDeleting(deleted)` walks the keys: a referencing table is written, and a `Cascade`
  makes it a deleted table, so its own referencing keys are followed in turn. A `SET NULL` or
  `SET DEFAULT` table loses no rows, so the walk stops there.
- **`DeletePlan`** takes `ForeignKeyActions`. `Writes` is now the tables the delete's own
  statements name (root, collections, explicitly deleted siblings) plus a new `Consequences`, which
  is `WrittenByDeleting` over those tables. The builder no longer adds siblings to the writes by
  hand: a sibling's `FK_<Table>_Id ON DELETE CASCADE` makes it a consequence like any other.
  `Aggregate`, which `CheckAuthority` checks, is unchanged, so consequences are exempt from
  authority exactly as siblings were. `CallerAuthority` is never touched, so the caller's own
  statements over a consequence table stay refused.
- **`SavePlan`** (extension, see Deviations): a save deletes the child rows a supplied collection
  no longer carries, so its `Writes` now adds `WrittenByDeleting` over the child tables (never the
  root, which a save never deletes). A new `Aggregate` is what `DataBatch.Save` checks authority
  over, so the same exemption holds.
- **Users named by consequence rows.** `VersionTagRegistry.FixedRules` gives `core.UserPreferences`
  and `core.NotificationPreferences` a user-level `Preferences` rule (`UserId`), so once a user
  delete declared them, the bump epilogue refused the batch ("no statement declared which users it
  wrote"). A key's action reports no rows, so `DeletePlan.ConsequenceUserIdCaptures` lists each
  consequence table with a user-level rule together with the keys that reach it (`DeleteReach`: the
  key, the plan's nodes over the referenced table, and the cascades that remove referenced rows in
  turn). `DeleteText` declares a `uidk_<Schema>_<Table>` capture (`UserIdCapture.ImageDeclaration`:
  only the `Old_`/`New_` columns, since these tables have no single-column key). Before the first
  `DELETE`, it fills the capture with `INSERT … SELECT d.[UserId], NULL FROM <table> AS d WHERE
  EXISTS (…referenced row removed by a node's scope or by a cascade…)`. `DataBatch.Delete` registers
  it as a `UserIdSource`. `ForeignKeyActions` now carries each key's columns and principal columns,
  `HeldBy(dependent)` and `RemovedByDeleting`. A `SET DEFAULT` key that writes a rule's user column,
  or a cascade cycle, is refused when the plan is built.
- **Save refusal.** A save cannot tell which rows its child deletes reach, so `SavePlan` refuses to
  build (`InvalidOperationException`) when a consequence outside the aggregate has a user-level rule.
  No current model has such a key: the preference tables reference `core.Users`, which is never a
  child.
- `DataExecutor` takes and exposes `ForeignKeys`, and `SaveEmitter` takes `ForeignKeyActions`. The two
  hand-built executors in tests (`BatchHarness`, `IdAllocatorTests.ExecutorWith`) pass it.
- **Workaround removed.** `JobStatements.DeclareScheduleDelete` and its unit test are deleted, and
  so is the `DeletePersistContext` case in `ScheduleService.ContributeAsync`. A schedule delete now
  declares `core.ScheduleStates` (cascade) and `core.Jobs` (`FK_Jobs_ScheduleId` set null) through
  the general path. `ScheduleService` appends nothing on a delete beyond the base.
- `src/core/Tellma.Core/README.md` (Updates and deletes) describes the declared consequences.
  ARCHITECTURE.md says nothing at this level, so it is unchanged.

### Which deletes newly declare which tables

Dumped from the default composition's relational model (Core only; `src/module/gl` declares no
`OnDelete`, no `[Sibling]` and no `IJobEntity`, so the GL model adds none). These are the only
keys with a writing action in production code:

| Key | Action | Delete that now declares the table |
| --- | --- | --- |
| `FK_ScheduleStates_ScheduleId` (core.ScheduleStates → core.Schedules) | CASCADE | `core.Schedule` delete declares `core.ScheduleStates` (was the PC-144 workaround) |
| `FK_Jobs_ScheduleId` (core.Jobs → core.Schedules) | SET NULL | `core.Schedule` delete declares `core.Jobs` (was the PC-144 workaround) |
| `FK_UserStamps_UserId` (core.UserStamps → core.Users) | CASCADE | `core.User` delete declares `core.UserStamps` (new; the user-level bump already wrote it, so no change to the guard's lock) |
| `FK_UserPreferences_UserId` (core.UserPreferences → core.Users) | CASCADE | `core.User` delete declares `core.UserPreferences` (new) |
| `FK_NotificationPreferences_UserId` (core.NotificationPreferences → core.Users) | CASCADE | `core.User` delete declares `core.NotificationPreferences` (new) |
| `FK_ScheduleStates_LastJobId` (core.ScheduleStates → core.Jobs) | SET NULL | a `core.Job` delete would declare `core.ScheduleStates`; no `DataBatch.Delete<Job>` exists yet (job retention is not built), see risks |
| `[Sibling]` `FK_<Table>_Id` | CASCADE | unchanged: already declared, now through the general path |
| `IJobEntity` `FK_<Table>_JobId` → core.Jobs | SET NULL | as for `LastJobId`: only a job delete, which does not exist yet |

Two of the consequence tables, `core.UserPreferences` and `core.NotificationPreferences`, carry a
fixed user-level `Preferences` rule. A user delete therefore now also captures the users their
rows name, before the user's rows cascade away, and bumps those users' `Preferences` tag (the
deleted user's own, which the `core.Users` rule already bumped). The integration test
`VersionTagTests.A_user_delete_takes_the_memberships_and_tells_the_deleted_user_alone` caught the
missing capture. No existing test asserted the old undeclared behaviour. The sibling tests (`DeleteStatementTests`) assert the sibling among the
writes and still pass unchanged.

### Deviations

1. **Saves too.** The brief scoped the fix to `DeletePlan`. A save's child-row deletes are the
   same bug class, so `SavePlan` declares their consequences too, with the same authority exemption.
   No current model has a key with an action that references a child table, so this changes no
   current batch. It is pinned by a test model.
2. **Spec 0012 §5.2** says `Save`, `Update` and `Delete` derive their declared writes "from
   metadata (children included)". They now also derive them from the relational model's
   delete actions. §9.2's "never cascades" still holds for the statements themselves: no
   statement is emitted for a consequence, and the database's own action does the write.
3. **A capture by selection for consequence users.** Spec 0013 §2.5 has each platform write
   capture its rows' users through `OUTPUT`. A key's action has no `OUTPUT`, so a delete reads the
   rows the keys will reach before its deletes run, and a save refuses the shape instead of
   capturing it. The capture keeps no row id, since nothing reads one.

### Tests

New `test/core/Tellma.Core.Tests/Data/Writes/ReferentialWriteTests.cs` over a new
`ReferentialModel.cs`. In that model a hub has bays (a child), spokes cascade to the hub, rims set
null to a spoke and to a bay, treads cascade to a rim, and seats cascade to a spoke and name a
rider with a `[BumpsUserVersionTag]` rule. The tests cover:

- The keys read from the model, by the referenced table and by the holding table, with their
  actions and columns.
- The walk: hub → spokes (cascade) → rims (set null); treads are never reached through a
  set-null; a deleted rim does reach treads.
- A hub delete declares `{Bays, Hubs, Rims, Seats, Spokes}`, not `Treads`. No statement writes a
  consequence table.
- **Transitive user capture:** a hub delete captures the seats' riders before any `DELETE`
  (seats → spokes → hubs in the keys), and registers a `UserIdSource` over `rf.Seats` reading
  `Old_RiderId` and `New_RiderId`. A spoke delete captures them too. A rim delete reaches only treads
  and captures nothing.
- A save plan whose child deletes would reach a table with a user-level rule is refused.
- **Authority:** spokes and rims are owned by other stacks and the caller holds only `{Hubs, Bays}`.
  The delete passes and `CallerAuthority` is unchanged afterwards. The caller's own `Sql` writes to
  `rf.Rims` and `rf.Spokes`, after the delete, are refused naming the owner. A `Delete<Spoke>`
  (the referencing entity itself) is refused.
- A hub save declares `{Bays, Hubs, Rims}` (the bay's set-null), never `Spokes`, and passes
  without authority over rims.
- The core model: a `Schedule` delete declares exactly `{core.Jobs, core.ScheduleStates,
  core.Schedules}`. A `User` delete declares `core.UserStamps`, `core.UserPreferences` and
  `core.NotificationPreferences`, and registers user-id sources over both preference tables.
- The PC-144 pin `ScheduleTests.A_delete_removes_each_schedules_state_detaches_its_jobs_and_withdraws_its_paused_notice_after_it_committed`
  (a schedule with a `Succeeded` job, under the fixture's change-tracking audit) now passes
  through the general path, with the workaround gone.

### Open risks and later seams

- **Raw `Sql` deletes are not covered.** A raw `DELETE` declares its own `SqlOptions.Writes`, and
  nothing derives that statement's consequences. Future job retention (spec 0020's
  `core.job-retention`, which deletes `core.Jobs` rows) must declare `core.ScheduleStates`
  (`LastJobId` set null) and every `IJobEntity` table. A later seam: expose
  `ForeignKeyActions.WrittenByDeleting` to such composers, or have the internal channel add it.
- **Blobs in non-sibling cascades.** `DeletePlan` captures blobs only for nodes it deletes
  explicitly (aggregate and capturing siblings). A table reached by a non-sibling cascade, or a
  sibling of a child row removed by a save, is declared but its blob references are not captured.
  No such table exists today: spec 0012 §4 allows `ON DELETE CASCADE` only on sibling tables and
  four named non-entity tables. Nothing enforces that at startup, though, so a model that adds
  such a key would leak its blobs silently. Users are now captured for deletes (see above), and
  saves refuse the shape.
- **Capture cost.** The user capture of a user delete is one `INSERT … SELECT` per preference
  table, filtered by its `UserId` key against the deleted ids.
  A deep cascade chain would nest one `EXISTS` per hop.
- **PC-088 (Users stack, in parallel in pc-1)** may add its own local declaration for the user
  delete's cascades. If it lands first, that workaround should be removed at PC-F62's integration
  or by a follow-up: the general path now declares `core.UserStamps`, `core.UserPreferences` and
  `core.NotificationPreferences` for every user delete.
- The id allocator's refill pass (`IdAllocator.AppendRefills`) iterates `WrittenTables`. A
  schedule delete may now append a `core.Jobs` sequence refill when that buffer is low. This is
  harmless (it tops a buffer up), and siblings already behaved this way.

### What reviewers should examine hardest

- `DeleteText.WriteConsequenceCaptures`: that the `EXISTS` conditions select exactly the rows
  each key's action reaches (node scopes and cascades in turn), and that they run before any row
  leaves.
- That exempting consequences from `CheckAuthority` cannot be used to write a foreign stack's table
  beyond what the database's own key action does. The exemption covers the delete statement only,
  and the statement text never names a consequence table.
- `ForeignKeyActions` reads `OnDeleteAction` from the relational model, which maps EF's
  `ClientSetNull` / `ClientCascade` / `Restrict` / `NoAction` to non-writing actions. Confirm that
  matches what the migrations emit.
- The sibling path now relies on the sibling's cascading key being in the relational model rather
  than on the `[Sibling]` attribute. The existing sibling tests still assert the sibling tables
  among the writes.
