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

## Review 1

### Findings

Ranked: 0 blocking, 0 major, 3 minor.

Checks that found nothing wrong (the responses need not cover them):

- **The derivation is complete.** `ForeignKeyActions` reads every `IForeignKeyConstraint` of the
  relational model, with composite columns and principal columns in key order (`Reached` zips
  them). The keys it finds match the generated migrations: the fixture's `Initial` carries exactly
  the five core keys, `FK_WidgetExtras_Id`, `FK_Exports_JobId` and `FK_Shipments_JobId`, and Acme's
  carries the five core keys. So the implementer's table is right. EF's `ClientSetNull`,
  `ClientCascade`, `Restrict` and `NoAction` map to non-writing actions, which is what the
  migrations emit. The walk visits each removed table once (`removed.Add` guards the queue), so a
  cycle or a self-reference terminates. A SET NULL target is written but not followed, which is
  right: the action updates no key that another constraint references, since the platform declares
  no `ON UPDATE` actions. `Removals` pops `path` on the way back, so it throws on a real cycle only
  and never on a diamond.
- **Authority.** Both the delete and the save call `CheckAuthority(plan.Aggregate)`. The save
  previously checked `Writes`, which equalled the aggregate, so its check is no looser.
  `CallerAuthority` is never touched, and no statement text names a consequence table. So the
  exemption covers the database's own key action and nothing more, and the authority test confirms
  that the caller's later `Sql` writes to those tables are still refused.
- **Frame and order.** The consequences are written by the delete statement's own `DELETE`, so
  they are atomic with it and run in its frame. The removed `DeclareScheduleDelete` was a separate
  statement after the delete. It is gone, with its unit test and the `ScheduleService` delete
  case. The PC-144 pin
  `ScheduleTests.A_delete_removes_each_schedules_state_detaches_its_jobs_and_withdraws_its_paused_notice_after_it_committed`
  passes through the general path (run here). The only other test edits are the two executor
  constructions, so no test was weakened.
- **The user capture fixes a real failure.** `VersionTagTests.A_user_delete_takes_the_memberships_and_tells_the_deleted_user_alone`
  is unchanged. It already expected the `PreferencesItem` event before this change, it fails at the
  epilogue's coverage check without the capture, and it passes with it (run here). The capture sits
  inside the delete statement, so the round-trip budget is unchanged. In production it adds no
  user: the captured user is the deleted one, whom the `core.Users` node already captures and whose
  `core.UserStamps` row cascades away in the same statement. The coverage check still requires it.
- **PC-088's local declaration** of `core.UserPreferences`, `core.UserStamps` and
  `core.NotificationPreferences` on a user delete becomes redundant and stays harmless. The written
  tables are a set, so a table declared twice adds no tag name twice. The epilogue fills
  `@tm_userIds_<Column>` under `NOT EXISTS`, so a user named by two sources is bumped once. PC-088's
  raw statement must still carry `SqlOptions.UserIds` for the preference tables, which TELLMA0003
  already enforces. It should still be removed at integration, as the implementer says.

R1-1 (minor). `src/core/Tellma.Core/Data/Writes/DeleteText.cs:167`. The consequence capture is a
plain `INSERT … SELECT` under read-committed snapshot, so it reads row versions and takes no locks,
while the cascade that later removes those rows does lock them. Failure scenario (the shape
`ReferentialModel` pins): the hub delete captures the seats' riders. A concurrent transaction then
inserts a seat for one of the hub's spokes and commits. The delete has not locked the spoke row,
and the hub row's U lock admits the FK check's S lock. The hub's cascade then removes that seat,
its rider's `Preferences` tag is never bumped, and that user's cache stays stale. No current model
is affected, because the only consequence tables with a rule key on `UserId` to the deleted user,
who is captured anyway. Fix: read the reached tables `WITH (UPDLOCK, HOLDLOCK)` in the capture (on
`d` and on each `r<n>` alias), so the key ranges the cascade will reach stay closed until commit.
The root's key capture already takes `UPDLOCK` the same way. Otherwise, state the window in the
plan's XML docs and carry it.

R1-2 (minor). `src/core/Tellma.Core/Data/Saves/SavePlan.cs:61`,
`src/core/Tellma.Core/Data/Writes/DeletePlan.cs:152` and `:173`. Three refusals are model-shape
errors raised only when an entity's plan is first built: a save whose child deletes reach a table
with a user-level rule, a `SET DEFAULT` key on a rule's user column, and a cascade cycle.
`SaveEmitter.Plan` and `DeletePlan.For` build plans lazily, so the error reaches production as a 500
on the first save or delete of that entity, not at composition, although spec 0012 §3.2 validates
model shape at startup. Fix: run the checks once at composition (in `ForeignKeyActions`, or in a
composition check over every top-level entity), so a model that declares such a key fails to
start. The cycle check can stay as a backstop, since the migration already refuses a cycle.

R1-3 (minor). `test/core/Tellma.Core.Tests/Data/Writes/ReferentialWriteTests.cs:107-130`, against
`src/core/Tellma.Core/Data/Writes/DeleteText.cs:175` (`Scope`). The multi-hop capture is checked
only by a text prefix and never runs against SQL Server; the only capture that runs is the user
delete's single hop from the root. No test, by text or by execution, exercises `Scope`'s child-node
branch: a reach whose referenced table is a collection or an explicitly deleted sibling,
`SELECT x.[Id] FROM <child> AS x WHERE x.<ParentKey> IN (…)`. A wrong parent key or a wrong nesting
there would pass the suite. Scenario: a later model gives a rule-carrying table a key to a child
table, and a swapped `ParentKey` silently captures the wrong users or none. Fix: add a unit golden
of the whole capture statement for a reach through a child node. A variant composition of the test
model can give `rf.Rims` a user-level rule, so that the bay's SET NULL reach goes through the child
branch. Add one executed case if a fixture shape allows it cheaply.

Rulings on the implementer's points and the brief's questions:

1. **Spec 0012 §5.2 and writes derived from the model: a spec gap, not a departure.** §5.2 says
   `Save`, `Update` and `Delete` derive their declared writes "from metadata (children included)".
   It also has `WrittenTables` audited against change tracking, and 0020 §7.8 has the emitter's
   delete cascade `ScheduleStates` and null `Jobs.ScheduleId`. So the spec requires these writes to
   be declared without saying how. The authority exemption, by contrast, departs from the literal
   text of 0012 §5.2 (Write authority) and 0015 §13.3, which refuse a `Delete` "whose declared
   writes include" a table outside `CallerAuthority`. Siblings already depart in the same way. Flag
   for Ahmad: "0012 §5.2's `WrittenTables` row should say that `Save` and `Delete` also declare
   every table a referencing key's `ON DELETE` action writes (siblings, the §4.1 cascade tables,
   and the `IJobEntity` and `Jobs.ScheduleId` SET NULL keys), read from the model's foreign keys.
   0012 §5.2 (Write authority) and 0015 §13.3 should say that the caller-channel check covers the
   tables the statement's own text writes, and that tables written only by a key's action are
   declared but exempt."
2. **Saves: the widening is right, and refusing is right for now.** It is the same bug class, and it
   changes no current batch. Under 0012 §4.1 no key with an action can reference a child table:
   siblings attach to registered entities, and the named cascades reference `core.Users` and
   `core.Schedules`. So the save path is a defence. Refusing beats an untested capture for a shape
   the spec forbids, but the refusal should happen at composition (R1-2).
3. **The 0013 §2.5 capture** is correct for every current model, stays inside the delete statement
   (no extra round trip), and the failure it fixes had a test (the `VersionTagTests` user delete).
   It is not safe against a reached row inserted after the capture (R1-1). That cannot affect the
   current tables, and the fix is a lock hint.
4. **Raw `Sql` deletes: a carry is enough, and no seam is needed now.** `ForeignKeyActions` is
   internal to `Tellma.Core`, where `JobStatements` lives, so a retention composer can declare
   `WrittenByDeleting([core.Jobs])` directly. Carry to **PC-145**, which builds `core.job-retention`
   and `core.file-retention` and is in progress. The retention `DELETE` of `core.Jobs` must declare
   `core.ScheduleStates` (`FK_ScheduleStates_LastJobId` SET NULL) and every `IJobEntity` table
   (`FK_<Table>_JobId` SET NULL), preferably from `ForeignKeyActions.WrittenByDeleting`. Its
   "retention paging and SET NULL" integration test, under the fixture's change-tracking audit,
   pins it. The same applies to any `file-retention` delete of a table that has a sibling or a
   referencing key.

Verification by the reviewer:

- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings, 0 errors.
- Tellma.Core.Tests, filtered to `Data.Writes`, `Data.Saves`, `Data.Batches`,
  `ScheduleStatementsTests` and `IdAllocatorTests`: 267 passed, 0 failed.
- Tellma.Core.IntegrationTests (`Category=Integration&Category!=Nightly&Live!=true`), filtered to
  `Jobs.ScheduleTests`, `Data.VersionTagTests`, `Access.UserWriteTests`, `Data.DataQueryTests`,
  `Crud.DeleteActionPipelineTests` and `Data.WriteStatementTests`: 103 passed, 0 failed.
- The PC-144 pin and the user-delete tag test, run alone: 2 passed.
- The implementer's list omitted `Data.DataQueryTests`, whose sibling delete now relies on the
  sibling's key rather than its attribute. It passes.

### Responses

- R1-1: fixed in the "PC-F62: review 1 fixes" commit (child of 7a58ce3f). The consequence capture
  now reads the reached table (`d`) and every referenced table (`r<n>`) `WITH (UPDLOCK, HOLDLOCK)`
  (`DeleteText.Locks`, documented there and in `WriteConsequenceCaptures`). It adds no round trip:
  the hints sit in the delete statement's own `INSERT … SELECT`. It adds no new lock order either.
  The capture runs after the keys statement has U-locked the root rows and before the deletes,
  which then take X on the same rows and ranges. That is the U-to-X conversion the root's keys
  capture already uses. The delete pipeline's integration tests pass with the hints:
  `Access.UserWriteTests`, `Data.VersionTagTests` (the user delete runs the capture against SQL
  Server), `Crud.DeleteActionPipelineTests`, `Data.DataQueryTests` and `Jobs.ScheduleTests`. The
  unit goldens assert the hints.
- R1-2: fixed in the "PC-F62: review 1 fixes" commit (child of 7a58ce3f). New
  `ForeignKeyActions.UserTagProblems(metadata, registry)` reports, as `core.version-tags` problems,
  every `SET DEFAULT` key on a rule's user-id column. It also reports every top-level entity whose
  save would delete child rows that a key's action carries to a table with a user-level rule
  (`SaveConsequenceNamingUsers`, shared with `SavePlan`). `VersionTagsCheck` returns those problems
  once the registry itself is sound, so such a model fails to start. The plan-time throws stay as
  backstops and reuse the same messages (`SaveMessage`, `DefaultUserMessage`). The cascade-cycle
  throw stays a plan-time backstop only, as the review allows: the migration already refuses a
  cycle. Pinned by
  `ReferentialWriteTests.A_model_whose_save_would_reach_a_table_naming_users_fails_the_version_tags_check`.
  The sound referential model passes the check; the variant with lamps (a `SET NULL` key to the hub's
  child bays, with a `[BumpsUserVersionTag]` keeper) fails with one problem naming `rf.Lamps`.
  `StartupGateTests` and the full unit runs of `Tellma.Core.Tests` and
  `Tellma.Core.AspNetCore.Tests` pass, so no existing composition trips the new check.
- R1-3: fixed in the "PC-F62: review 1 fixes" commit (child of 7a58ce3f). The test model gains a
  variant composition (`ReferentialModel.ComposeWithLamps`). Its lamps key to the hub's child
  collection `rf.Bays` with `SET NULL`, and carry a user-level rule on `KeeperId`. New
  `ReferentialWriteTests.A_delete_captures_the_users_named_by_the_rows_a_key_to_its_child_rows_reaches`
  asserts the whole capture statement for that reach, including `Scope`'s child-node branch
  (`r0.[Id] IN (SELECT x.[Id] FROM [rf].[Bays] AS x WHERE x.[HubId] IN (SELECT [Id] FROM
  @tb<k>_keys))`). It also asserts that the capture precedes every `DELETE` and that the user-id
  source is registered. No executed case was added: no integration fixture entity has a key to a
  child table, and adding one means a new fixture entity and a regenerated fixture migration. That
  is not cheap for a shape spec 0012 §4.1 does not allow in production. The executed single hop
  (the user delete, in `VersionTagTests`) runs the same `Reached` code.
