# Round 1 critique — security, correctness, performance

Reviewed 2026-09-04 against `notation.md`, `ledger.md`, `seams.md`, the brain dump and the breakdown; theme decision files and specs were opened only to verify specific claims (cited inline). Severity: **blocking** = a single spec author cannot work around it because the defect spans owners or the ledger fixes the wrong answer; **major** = a real hole or an unimplementable seam that one owner can repair; **minor** = a doubt, ambiguity, or drift worth a sentence.

## Blocking

### 1. The row-level post-check covers only rows whose children changed — inserted and root-edited rows escape it
- **Location.** seams §1.3 (declaration of `@tb3_touched`, steps 2 and 4), §1.6 step (11), `PersistContext.TouchedIds/NewIds` (§3.3); ledger §2.24, seam 11 ("in-transaction post-check").
- **Problem.** In §1.3 `@tb3_touched` is a table variable filled only by the child `DELETE/UPDATE/INSERT` `OUTPUT … INTO`. The root `INSERT` has no `OUTPUT`; the root `UPDATE` emits `OUTPUT inserted.[Id]` as a *result set*, not into `@tb3_touched`. §1.6 step (11) then counts visibility under the save grant only over `@tb{b}_touched`. A caller whose `Save` grant is `Region = 'A'` can insert a row with `Region = 'B'`, or edit a visible row's `Region` to `'B'`, and pass the post-check because neither row is in the table. This is the exact hole the post-check exists to close. `PersistContext.NewIds` names `@tb{b}_new`, which nothing declares. 0014 D13 (theme file line 531) bound `@tm_touched : [IdList]` and `@tm_synced : [IdList]` from C# as *two* sets — the translation into the emitter collapsed them into one table with the narrower meaning.
- **Fix.** Declare three batch-local tables per root table: `@tb{b}_new` (all inserted root ids; the root `INSERT` gets `OUTPUT inserted.[Id] INTO`), `@tb{b}_saved` (every root id in the payload, new and existing, filled by `INSERT … SELECT [Id] FROM @tb{b}_t0 UNION SELECT [Id] FROM @tb{b}_t1` before any DML), and `@tb{b}_touched` (parents of synchronised children, as today). Step (11) runs over `@tb{b}_saved`; the stamped-ids result set is derived from `@tb{b}_saved` after the `UPDATE`. `PersistContext` exposes all three with their meanings in a Members table so distribution guards do not read `TouchedIds` as "the rows I saved". Add a conformance test: insert outside the filter, edit into outside the filter, both → 403.

### 2. `INotifier.Notify` and `IJobQueue.Enqueue` are `sync` but take ids from `IIdAllocator.TakeAsync`, which may pay a round trip
- **Location.** seam 15 (`Notify … sync // … ids from IIdAllocator.TakeAsync`), seam 8 (`Enqueue … sync // ids from IIdAllocator.TakeAsync`), seam 1 §1.2 (`TakeAsync … answers from the buffer or pays one dedicated round trip`), `PersistContext.Notify … sync`, `ActionContext.Notify … sync` (§3.3). 0019 D8 says "synchronously from its buffer" with no empty-buffer rule.
- **Problem.** The chain 0014 `PersistContext.Notify` (sync) → 0020 `INotifier.Notify` (sync) → 0011 `TakeAsync` (async, may hit the DB) cannot be implemented as written: a sync method cannot await, and `Reserve(batch, …)` cannot help because a non-immediate reservation assigns ids only *after* the batch executes, while the `INSERT` is inside that batch. Blocking on the buffer thread or `.GetAwaiter().GetResult()` inside a request is not acceptable, and silently failing to notify when the buffer is empty is a correctness hole. Three spec owners share the signatures.
- **Fix (preferred).** System-written rows (`Notifications`, `Jobs`) assign ids in SQL: the statement reserves its range in-batch (`EXEC sp_sequence_get_range … @range_first_value = @tb{b}_first OUTPUT`) and inserts `Id = @tb{b}_first + ROW_NUMBER() OVER (ORDER BY …) - 1`, returning the ids as a result set (`BatchResult<list<int>>`). `Notify`/`Enqueue` stay sync, never touch the buffer, and business rows that need `JobId` before the insert take it from the result inside the same round trip (the emitter already orders statements; the `Exports.JobId` case is an `UPDATE … FROM @tb{b}_jobIds` appended after the insert). Alternative: make the whole chain async and document the possible extra round trip — worse, because it breaks the "notification rides the save train" guarantee on a cold buffer.

### 3. No default `[RelatedSelect]` — `core.User` has none, so `CreatedBy.*` on every entity in every distribution projects `Email`, `Subject`, `ContactMobile`
- **Location.** seam 2 (`[RelatedSelect]` "defines the projection through navigations"), seam 3 (`RelatedSelect: set<string>` on `StackDescriptor`, annotation "on entity type; inherited"), seam 11.3 (`User` carries `[ApiResource]` but no `[RelatedSelect]`), seam 11.2 rules ("navigation traversal is limited to the target's `[RelatedSelect]` when the caller lacks `Read` on it"), ledger §1 item 23 ("related entities are a declared display projection").
- **Problem.** The default when the attribute is absent is never stated. If the default is "all columns" (the only reading of `set<string>` with no rule), every `Read` on any entity leaks every referenced user's `Email`, `Subject`, `ContactEmail`, `ContactMobile`, `Gender` through `CreatedBy`/`ModifiedBy` — the leak the ledger says it closed. 0015 owns the attribute, 0013 owns `User`, 0011 owns the metadata: nobody is told to declare it.
- **Fix.** Ledger §2.3/seam 2: the default projection is `Id`, the `[Multilingual]` `Name` group, `Code` when present, and `[BlobReference]` columns of kind `Avatar`; anything wider must be declared. seam 11.3: `User` carries `[RelatedSelect("Name,Name2,Name3,ImageId")]`; `Role` `[RelatedSelect("Name,Name2,Name3,Code")]`. The realised gate warns when an entity with a `[Searchable]` or `[NaturalKey]` string outside the default lacks a declaration. Add a conformance test that `select CreatedBy.Email` from a caller without `core.User × Read` is refused.

### 4. User-level tag bumps cannot be derived from raw `Sql` statements, yet four statements rely on exactly that
- **Location.** seam 5.1 (`IVersionTagRegistry.Resolve(writtenEntityTypes) -> VersionTagEffects`, `UserVersionTagRule(EntityType, Column, UserIdProperty)`), seam 5.3 bump ("only when a user-level rule fired; `@tm_userIds : IdList`"), `SqlOptions(Writes, Idempotent, ResultSets)` (§1.1); consumers: seam 11.4 `UserPreferences` statement ("the epilogue bumps `PreferencesTag`"), seam 15.1 `notification-preferences/save` ("the epilogue bumps `PreferencesTag`"), seam 21 invite write-back (`Sql` with `UserInvitationOutcomeList`, writes `core.Users` which carries `[BumpsUserVersionTag(Preferences, "Id")]`), seam 8.3 job actions (`Update` with `Stamp = false`).
- **Problem.** A user rule needs the *affected user ids*. The emitter knows them from entity rows; a raw `Sql` statement declares only table names. The epilogue therefore cannot fill `@tm_userIds` for the three self-service statements or the invite write-back — either it bumps nothing (stale `UserProfile` and preference caches after the very operations that change them) or bumps every user (unacceptable). `UserPreferences`/`NotificationPreferences` are "not entities", so they cannot carry the attribute at all. 0011, 0012, 0013 and 0020 each assume the other side solves it.
- **Fix.** `SqlOptions` gains `UserIds: SqlIdentifier?` (a `@tb{b}_` id table or an `IdList` TVP the statement author fills; `@tm_UserId` sugar `SqlOptions.ForCaller`). The registry's startup gate (or `TELLMA0003`) fails a raw statement whose declared writes hit a table with a user-level rule when `UserIds` is absent. The three self-service statements and the invite write-back declare it. Ledger §2.18 states the rule; seam 1's Members table documents `UserIds`.

## Major

### 5. The in-transaction "narrowed window" re-checks only tenant tags; the user's `PermissionsTag` and both tags are read without a lock, so a membership revocation still races a persist
- **Location.** ledger §2.24 ("narrowing the window to the transaction"), seam 5.3 guard (`JOIN @tm_expectedTags` against `[core].[VersionTags]` only), seam 16 (`@tm_Guard` compares `permissions` **and** `PermissionsTag`), §1.6 steps (4)–(5).
- **Problem.** The guard after `BEGIN TRAN` never looks at `core.UserStamps.PermissionsTag`, so a membership removed and committed between the prologue and the transaction is invisible to the persist. Even for the tenant tag, an unlocked `SELECT` under RCSI does not stop a permission-changing transaction (which bumps last before its `COMMIT`) from committing right after the check; the persist then completes under the revoked grant. The ledger rejected `UPDLOCK` on stamp rows (S4) because it would serialize every save — correct — but a *shared* lock held to commit on the two tag rows conflicts only with permission writers, not with other saves.
- **Fix.** The guard reads `VersionTags` rows `permissions`/`settings`/… and the caller's `UserStamps` row `WITH (REPEATABLEREAD, ROWLOCK)` inside the transaction and compares both levels; the bump `UPDATE`s of a concurrent role/membership save then wait for the persist to commit (rare, bounded by one round trip). Add the `PermissionsTag` comparison to the `50412` guard text. Keep S4's flag but restate what is and is not closed.

### 6. `TryDenyFast` fail-closes into a 15-minute lockout for a user who just gained a permission
- **Location.** seam 11.2 (`TryDenyFast … cache-only; true = definitely denied`), 0013 D13 order "CSRF → tenant → coarse securable → handler → witness", `AccessOptions.PermissionsMaxAge = 15min`, `CacheSlidingExpiration = 4h`.
- **Problem.** The endpoint filter denies before any round trip, so the prologue that would refresh the user's tag never runs for that operation. A user with no prior grant on a resource who is granted one is denied on that instance until `PermissionsMaxAge` (or until some *other* allowed operation of theirs runs the prologue). "Definitely denied" is false: the cache can be stale in the permissive direction, and the two-level tags are only consulted by the prologue.
- **Fix.** Either drop `TryDenyFast` (a denied call costs one prologue round trip — the rare path) or bound it: fast-deny only when the cached `UserAccess` was validated by a prologue within `Tellma:Access:FastDenyWindow` (default 5 s); otherwise fall through to the handler, whose prologue refreshes. Document in seam 11 and the 0015 filter order.

### 7. `COMPLETE` has no lease fence, so a handler's transactional statements commit after the lease was lost
- **Location.** seam 8.3 `COMPLETE` (`WHERE LeaseToken = @token AND Status = 'Running'` with no row-count check), `JobBatch.Batch` ("statements appended here run in the completion transaction"), contrast `APPEND` (`IF @@ROWCOUNT <> … THROW 50422, N'Job.LeaseLost'`).
- **Problem.** Worker A's lease expires, worker B claims and runs the job; A finishes later. A's `COMPLETE` updates zero rows (token mismatch) but the handler's statements in the same transaction — `Exports.FileId`, notifications, business writes — commit. Double effects, and A's `NotifyOnSuccess` fires. The design's multi-instance safety claim rests on this fence.
- **Fix.** After the `UPDATE`: `IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @tb{b}_t0) THROW 50422, N'Job.LeaseLost', 1;` so `XACT_ABORT` rolls back the whole completion; the worker maps it to `JobCancellationReason.LeaseLost` and meters `tellma.jobs.lease_lost`. Order the `COMPLETE` first in the transaction so the handler's statements never run under a lost lease.

### 8. Schedule tick step 2 inserts jobs without checking the tick lease — a slow ticker double-fires
- **Location.** seam 8.3 `SCHEDULE TICK, step 2` (the `INSERT INTO [core].[Jobs] … FROM @tb{b}_t0` has no `LeaseToken` predicate; only the `ScheduleStates` `UPDATE` has `WHERE [st].[LeaseToken] = @tb{b}_p0`); step 1 leases for 30 s.
- **Problem.** If step 2 runs after the 30 s lease lapsed, another instance's step 1 re-leases the same schedules and both insert jobs. `OverlapPolicy = Skip`'s `NOT EXISTS` is evaluated per instance under RCSI and does not see the other's uncommitted insert.
- **Fix.** Join the `INSERT … SELECT` to `[core].[ScheduleStates] AS st ON st.[ScheduleId] = r.[ScheduleId] AND st.[LeaseToken] = @tb{b}_p0 AND st.[LeaseExpiresAt] >= @tb{b}_now`; assert the fired count equals the leased count or `THROW 50422, N'Schedule.TickLeaseLost'`.

### 9. Any user with `core.Schedule × Save` can run any registered handler, and an administrator's edit silently takes over a low-privilege user's arguments
- **Location.** seam 8.1 (`Schedule.HandlerKey [WriteOnce]`, `RunAsUserId [ServerOwned]; the last saver`), 0019 D11 ("an administrator editing someone's schedule takes it over — the UI says so"), handler keys `core.blob-sweep`, `core.blob-reconcile`, `core.job-retention`, `core.notification-retention`, `core.file-retention`, `core.session-sweep`.
- **Problem.** (a) No handler is marked user-schedulable. A schedule saver can point a schedule at maintenance handlers whose statements carry no permission checks (retention, sweeps) and run them as themselves — at minimum a denial-of-service lever, and `core.session-sweep` targets the catalog. (b) Take-over-on-edit is a privilege escalation path that "the UI says so" does not cover on the MCP surface: a low-privilege user authors `ArgumentsJson` (export of a resource they cannot read), an administrator agent fixes a typo via `tellma_save`, and the export now runs under administrator permissions. Ownership of the artifact bounds but does not remove the harm (the arguments were authored by someone else).
- **Fix.** `[JobHandler]` gains `Schedulable: bool = false`; `ScheduleService` validates `Schedules.HandlerNotSchedulable` for non-built-ins; Core marks only `core.export` schedulable in this release. Re-stamp `RunAsUserId` only when `HandlerKey`, `ArgumentsJson`, `CronExpression` or `TimeZoneId` change (a name edit keeps the owner); a change of run-as by someone other than the previous owner is the validation error `Schedules.RunAsChanged` unless the request sets `TakeOver = true` (an explicit `[EntityAction("take-over")]` is cleaner). Keep 0019 flag 11 but record the takeover rule.

### 10. `SaveMe` does not say what happens to the `RoleMemberships` child collection — an escalation path if the pipeline default applies
- **Location.** seam 21 (`SaveMe(request: SaveRequest<TUser>) … [SelfEditable] columns only`), seam 2 (child collections: `null` = untouched, `[]` = delete all, list = synchronise), seam 11.3 (`User.RoleMemberships [NotMapped] child collection`).
- **Problem.** "Columns only" is silent on collections. If the self-service save flows through the ordinary emitter with the payload's `RoleMemberships`, a user can grant themselves the Administrator role (or strip their own memberships, tripping the lockout guard). `UserAccessRules` escalation rules may catch the grant, but the rule set is not in the seams and self-service should not depend on it.
- **Fix.** seam 21: `SaveMe` sets every child collection to `null` before preprocessing and ignores `Id`s other than the caller's (`Users.SelfServiceOnly`); the pipeline's `SaveSource = Web` with a `SelfService = true` option restricts the emitter's `SET` list to `[SelfEditable]` columns and forbids child synchronisation. Test: a `SaveMe` carrying a membership row is a 422, never a grant.

### 11. `TenantState.ReadOnly` is unimplementable with a prologue that writes on every read
- **Location.** seam 9 (`ReadOnly → mutations 403`), seam 16 prologue (`UPDATE [core].[UserStamps] SET [LastActiveAt]`, the `Invited → Active` flip), ledger §2.24 ("every batch … begins with the prologue").
- **Problem.** If `ReadOnly` means the database is read-only (a relocation, a replica, a restore in progress — `RelocateAsync` exists), the activity stamp fails and every read on the tenant fails with a 500. If it means only "the app refuses mutations", the state is cosmetic and the prologue still writes. Neither reading is stated.
- **Fix.** Define `ReadOnly` as "the app refuses mutations and the database may be read-only". The connect initializer composes `ConnectPremises` with `StampActivity = false` and a new `AllowStateFlip = false` when `Tenant.State = ReadOnly`; the prologue gains `@tm_AllowFlip bit` guarding the `Invited → Active` block. Add `ReadOnly` to the conformance matrix (read succeeds against a database set `READ_ONLY`).

### 12. `core.session-sweep` is a "deployable job" but `core.Jobs` lives in tenant databases
- **Location.** seam 8.1 (`core.session-sweep (deployable; catalog)`), ledger §2.9 (one `core.Jobs` table per tenant), seam 9 (`Deployable` context has no tenant), Azure fact (no cross-database queries).
- **Problem.** The worker polls tenant tables; a deployable job has no row to claim, no lease, no schedule row. Either a catalog-side `catalog.Jobs`/`catalog.Schedules` pair exists (a second worker over the catalog connection) or the sweep is not a job at all.
- **Fix.** Make the session sweep a plain hosted timer in `Tellma.Core.AspNetCore` (`ICatalogSessionStore.SweepAsync` every N minutes, idempotent, no lease needed because the delete is set-based and safe to run twice) and drop it from the handler-key list; state that jobs and schedules are tenant-scoped only in this release.

### 13. `EntityService` is a base class in `Tellma.Core.Abstractions` whose operations need `Tellma.Core.Crud` internals
- **Location.** seam 3.2 (`// Tellma.Core.Abstractions.Crud … base EntityService<TEntity, TKey>` with `QueryAsync`, `SaveAsync` etc.), the note "`IEntityPipeline<TEntity, TKey>` and `IEntityBehavior<TEntity, TKey>` … are `Tellma.Core.Crud` internals", ledger §2.17 (Abstractions references only Queryex; modules never reference `Tellma.Core`).
- **Problem.** `Tellma.Module.Gl`'s `CenterService : EntityService<TCenter>` must compile against Abstractions, so `EntityService`'s operation bodies must delegate to something visible from Abstractions. An internal type in `Tellma.Core` is not. As stated the two sides cannot both hold.
- **Fix.** Declare `IEntityPipeline<TEntity, TKey>` (the operations) and `IEntityBehavior<TEntity, TKey>` (the hooks as the pipeline sees them) as public contracts in `Tellma.Core.Abstractions.Crud`, marked not-for-distributions in prose (and `[EditorBrowsable(Never)]` in code); `EntityService` receives the pipeline through its constructor and implements `IEntityBehavior` explicitly. Add both to seam 3.2 and the 0014 row of §23.

### 14. Hydrated import rows default to `Override`, so an import silently overwrites concurrent edits to columns the sheet does not even contain
- **Location.** seam 19 (`ImportRequest.Concurrency … null = Check when the sheet carries Stamp, else Override`), ledger 4.9 (hydration through `GetByIdsAsync`, "+1 hydration"), round-trip ledger (hydration is a separate round trip from the save).
- **Problem.** For `Update`/`Upsert` with a partial sheet, the pipeline reads the row (hydration), overlays the sheet's columns, and saves under `Override` because the sheet has no `Stamp` column. A concurrent edit between hydration and persist — including to columns absent from the sheet — is lost without a conflict. Silent data loss on the most common import shape.
- **Fix.** Hydrated rows always save under `Check` using the `ModifiedAt` read at hydration; the sheet's `Stamp` column (when present) replaces it; `Override` applies only when the caller passes `Concurrency = Override` explicitly. Conflicts surface as `Excel.Import.ConcurrencyConflict` per row. Update errata 0018 D9.

### 15. T5 recounts the whole tree on every tree save, activation and delete
- **Location.** seams §1.4 T5 (`UPDATE t … FROM [gl].[Centers] AS t CROSS APPLY (… WHERE d.[Node].IsDescendantOf(t.[Node]) = 1) … WHERE t.[SubtreeCount] <> x.[Cnt] …`), ledger §2.10 ("counts recomputed set-based for rows whose counts changed"), 0011 flag 5.
- **Problem.** "Rows whose counts changed" is found by recomputing the count for *every* row: N range seeks summing to O(N × average depth) row reads per save. A single `deactivate` of one center in a 100k-node tree reads on the order of a million index rows inside the persist transaction while U locks are held. The ledger defers this to "measure", but the shape is knowably wrong and the fix is cheap.
- **Fix.** Capture `(Id, OldNode)` of the affected set before T4 (`INSERT INTO @tb{b}_old SELECT Id, Node FROM Centers WHERE Id IN (SELECT Id FROM @tb{b}_aff)`); restrict T5 to `t` where some affected row's old or new node `IsDescendantOf(t.[Node])` — the union of the old and new ancestor chains of the affected rows, O(|aff| × depth) seeks. For `Update` actions and deletes the affected set is the target ids (plus, for deletes, the deleted rows' old nodes). Keep the whole-table statement as the `core.tree-verify` maintenance job.

### 16. Every per-user cache key must include the tenant, and nothing says so
- **Location.** seam 5.1 (`VersionedCache<TKey, TValue>`), seam 11.2 (`UserAccess { UserId, Tag, UserTag }`, `IAccessEvaluator.Invalidate(userId: int)`, `AccessOptions.MaxCachedUsers`), seam 16 (`IUserConnector.Connect() … cache`), seam 9 (`ConnectedUser` cache "per user per instance").
- **Problem.** User ids are per-tenant integers and subjects are global: user 5 exists in every tenant, and one `sub` is a member of several. A `UserAccess` or `ConnectedUser` entry keyed by `userId` or by `subject` alone returns tenant A's grants to tenant B's request. The two-level tag comparison would *usually* catch it (different Guids), turning a leak into cache thrash — but a design whose isolation rests on Guid inequality rather than key discipline is the kind of stale-cache leak this exercise is meant to remove.
- **Fix.** `UserAccess` gains `TenantId`; every cache kind's key is documented as `(TenantId, …)` (`settings`: tenant; `preferences`/`permissions`: `(TenantId, UserId)`; connect cache: `(TenantId, Subject)`; `entities`: `(TenantId, EntityName)`); `Invalidate(tenantId, userId)`. Add a fixture test with two tenant databases sharing user ids and subjects.

### 17. `IsSensitive` step-up, the applock and `Access.LockTimeout` treat a lock timeout as a validation error
- **Location.** seam 11.2 (`IAccessGuards.Contribute … sp_getapplock 'tellma.access' first`, `SecurityLockTimeout = 5s`, guard code `Access.LockTimeout` → `THROW 50422` → `ValidationException`).
- **Problem.** A 422 tells the client "fix your input"; a lock timeout is a transient condition that should be retried (and is retried by nothing, since `50422` is a reported non-transient failure). Under load a user/role save fails with an unfixable field error. Also unstated: whether `activate`/`deactivate` actions on `Users`/`Roles` (an `Update`, not an emitter save) take the applock — §1.6 step (6) says "when a security table is written", but §1.6 is the *Save* assembly; the action assembly is not shown.
- **Fix.** `Access.LockTimeout` → `THROW 50503` (new `TellmaSqlErrors.Transient`) mapped to `DependencyUnavailableException` with `Retry-After: 1`; the executor treats `50503` as a reported failure eligible for its three attempts. State that `IAccessGuards.Contribute` runs for every `Persist` batch whose `WrittenTables` intersects `Users`, `Roles`, `RoleMemberships`, `Permissions`, regardless of operation.

### 18. `Export`/`Import` are full `TopLevelEntity` stacks with no declared operations
- **Location.** seam 19 (`data Export : TopLevelEntity, IJobEntity … stack resource core.Export`, `data Import …`), seam 3 (keyed+audited projects `Read/Save/Delete`, `[Stack(Operations = All)]` default).
- **Problem.** With the default `All`, a member with the self-scope criterion can `save` an `Imports` row with any `Resource`, `RequestJson`, `ExpiresAt` (defeating retention) and a `FileId` of their own staged blob; `export`/`import` Excel operations are then projected onto the export table itself. Nothing forbids it.
- **Fix.** `[Stack(Operations = Query | Details | Delete)]` on both; `Resource`, `Kind`/`Mode`, `RequestJson`, `FileName`, `FileId`, `ResultFileId`, `RowCount`, `ErrorCount`, `ExpiresAt` are `[ServerOwned]`; the rows are created only by `ExcelOperations` and the handlers through `SaveSource = System`.

### 19. The prologue's `ConnectAsSystem` is callable from any scope
- **Location.** seam 16 (`ConnectAsSystem() // migrator and system jobs`, `System` variant "`@tm_Guard = 1`; `UserAccess.System`"), seam 9 (`PrincipalKind.System`).
- **Problem.** `IUserConnector` is a scoped service any distribution class can inject; calling `ConnectAsSystem()` inside a request scope yields an unrestricted `UserAccess.System` for the rest of the request. Distribution code is trusted, but the design's "hard to forget / hard to misuse" posture should not leave an escalation one method call away.
- **Fix.** `ConnectAsSystem` throws `InvalidOperationException` unless `RequestContext.Kind = System` (job scopes created with the system snapshot, provisioning steps, the migrator); the analyzer (or a startup check over `Tellma.Core.Abstractions` consumers) flags calls outside `Tellma.Core`.

### 20. A `[BlobReference]` on a child entity cannot be emitted: two `OUTPUT … INTO` clauses on one statement
- **Location.** seams §1.3 rules (child `DELETE … OUTPUT deleted.[UserId] INTO @tb3_touched` **and** "for every `[BlobReference]` column … `OUTPUT … INTO` it, including child synchronisation"), ledger 4.7 item 2 (weak owners deferred, "only top-level owners this release").
- **Problem.** SQL Server allows one `OUTPUT … INTO` and one plain `OUTPUT` per statement, not two `INTO`s. The child synchronisation statements already spend their `INTO` on `@tb{b}_touched`. The ledger defers blob kinds on weak owners for the *securable* reason, but the emitter rule as written is unimplementable and will be discovered late when the first distribution puts an attachment on a line.
- **Fix.** State the constraint and the shape now: the capture table for a child statement is `@tb{b}_cap_<Table> (ParentId, RowId, OldBlobId, NewBlobId, …)` — one `INTO` per statement carrying every captured column — and `@tb{b}_touched`/blob tables are filled from it afterwards. Keep the deferral of weak owners as a securable concern only.

### 21. Staged blobs have no read rule — the uploader cannot preview their own image before saving
- **Location.** seam 12 (`ResolveAsync … authorises through the owner row`; a `Staged` row has no owner), seam 12.2 (`CreatedById … the only user who may attach it`).
- **Problem.** The details page shows the picked image before `save`; with "authorise through the owner row" a staged blob is a 404 for everyone including its uploader, or — if an implementer shortcuts — readable by anyone who guesses the sequential id. Fail-closed by accident or fail-open by shortcut; the spec author must invent the rule.
- **Fix.** `ResolveAsync`: `Staged` → readable only when `CreatedById = RequestContext.UserId` and unexpired, with `Cache-Control: private, no-store`; `Committed` → the kind's `ReadAccess`; `Released`/`Deleting` → 404. State `Cache-Control: private, max-age=31536000, immutable` for committed blobs (never `public`).

### 22. The concurrency guard's `'M'` conflict turns a never-existed id into a 409
- **Location.** seams §1.3 step 1 (`CASE WHEN t.[Id] IS NULL THEN 'M'`), ledger §1 item 1 ("a row deleted under you is a conflict, never a resurrection").
- **Problem.** A client that invents a positive id (or an agent that guesses one) receives `409 concurrency-conflict` with `IsMissing = true`. The MCP `tellma_save` guidance then tells the agent to "reload and retry" — for a row that never existed. Not a hole, but a misleading contract and a cheap oracle for id existence (a `'M'` says "never existed or deleted"; the 404 path says the same, so no new leak — but two statuses for one fact).
- **Fix.** Map `IsMissing` conflicts to `NotFoundException` (404) when *every* conflict is `'M'`; keep 409 when any `'C'` exists. Document in seam 10.

### 23. `ConcurrencyMode.Override` still reports `'M'`, but `DeleteByIdsAsync(ids, expectedStamps?)` has no SQL and no 404/403 assert for `ByIds`
- **Location.** seams §1.5 (the `THROW 50403` assert is shown only for `WithDescendants`; `ByIds` shows no count check), §24.6 (`ExpectedStamps` "remains reachable at the service level" with no statement), ledger round-trip ledger ("delete … 1").
- **Problem.** In the one-round-trip delete (no validation hook) the 404-then-403 pre-check must happen in SQL, and the optional `ExpectedStamps` must be checked somewhere. Neither is written; an author has to invent both, and "strict all-or-nothing" (0014 flag 4) is not enforced by the text.
- **Fix.** §1.5 `ByIds`: `INSERT @keys … WHERE Id IN @ids` (no filter) → `IF COUNT <> @idCount THROW 50404`; then `IF (COUNT under Filter) <> COUNT THROW 50403`; when stamps are supplied, an `IdList`-plus-stamp TVP (`IdStampList(Id, ModifiedAt)`) and `THROW 50409` on mismatch. Add `IdStampList` to §1.8.

## Minor

### 24. `Settings` carries `[BumpsVersionTag("permissions")]` unconditionally — every settings save cold-paths every user
- seam 11.3 note, seam 22 (`Settings … [BumpsVersionTag("settings")], [BumpsVersionTag("permissions")]`). A tenant-name edit forces a permission reload for every user on every instance (one extra round trip each). Fix: bump `permissions` only when a language column changed — `SettingsService.Save` appends the bump explicitly (`DependsOn`-style `batch.Bump("permissions")`) instead of the static attribute; add `IDataBatch.BumpVersionTag(name)` for exactly this case.

### 25. `OverlapPolicy`/`MissedPolicy` on built-in schedules are editable, and `deactivate` on `core.blob-sweep` is allowed
- seam 8.1 (`ScheduleService … refuses delete and non-editable changes on built-ins`), 0019 D9 (editable: `CronExpression`, `TimeZoneId`, `IsActive`, …). An administrator can switch off the sweep or file retention. Either accept and notify (`core.schedule.paused` on built-ins) or make `IsActive` immutable on `IsBuiltIn` rows with a validation code `Schedules.BuiltInAlwaysActive`.

### 26. `CLAIM` does not filter `Attempts < MaxAttempts`, so poison rows are re-claimed once per poll before being failed
- seam 8.3 `CLAIM`. Add `AND [j].[Attempts] < @tb{b}_p5` (handler `MaxAttempts`) to the predicate and fail exhausted rows in a separate set-based statement; keeps the hot index seek and avoids a claim/complete pair per poison row.

### 27. `CountMismatchException` (409 `count-mismatch`) has no THROW number
- seam 10, `TellmaSqlErrors` (`50404` is "pre-check count mismatch" → `NotFound`). `DeleteByQueryRequest.ExpectedCount` is "verified inside the transaction" but its number is missing. Add `CountMismatch = 50428`.

### 28. Which SQL errors are "reported transient" for the executor's three attempts is unstated
- seam 1 header ("reported failures: the whole round trip, up to 3 attempts"). List them (1205, 1222, 4060, 10928, 10929, 40197, 40501, 40613, 49918–49920; 233/64/-2/timeout are *ambiguous*), and state that a `THROW 50xxx` is never retried.

### 29. Sequence healing keys on error 2627, which unique indexes also raise
- seam 1 §1.2 ("a 2627 on insert with an app-assigned id consumes the gap"). A duplicate `Code` would trigger a heal-and-retry. State: heal only when the violated constraint is `PK_<Table>`; every other 2601/2627 maps to the property.

### 30. `VersionTags` bump silently updates zero rows when a name is missing
- seam 5.3 bump. A pack that adds `[BumpsVersionTag("x")]` before its migrator run, or a hand-edited row, leaves caches stale forever with no signal. Add `IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @tm_tagNames) THROW 50422, N'VersionTag.Missing'` and meter it.

### 31. Three mechanisms seed `core.VersionTags`
- seam 18 (`HasData … VersionTags rows`), platform step `30 core.version-tags`, seam 5.3 migrator seed on every run. Pick the migrator seed (it is the only one that follows the registry) and drop the other two.

### 32. `Tellma-Client` "required on the cookie surface" is impossible for `<img>` and the hub negotiate
- ledger §2.19, seam 13.1. Browsers add no custom headers to image or WebSocket requests. State the exemptions: blob `GET` (side-effect free) and `/{tenantId}/hub` negotiate rely on `Origin`/`Sec-Fetch-Site` only, and the hub rejects cross-origin negotiate (cross-site WebSocket hijacking).

### 33. `AccessService.Check` for another user needs only `core.User × Read` on that user
- seam 21. A user's effective grants and filters are more sensitive than their name. Require `core.Role × Read` (the role editor's audience) in addition.

### 34. `Job.ErrorDetails` (stack traces) is readable by the requester under the self-scope criterion
- seam 8.1/8.2. Restrict `ErrorDetails` to callers with `core.Job × Read` from a stored grant (not the bespoke criterion), or move it to a `ReadDetails` action.

### 35. `IJobHandler<TItem> where TItem: Job | IJobEntity` is not a C# constraint
- seam 8.1. State the two shapes explicitly: `IJobHandler` (arguments-only, `JobBatch<Job>`) and `IEntityJobHandler<TEntity> where TEntity: IJobEntity`; the registry keys both.

### 36. Sandbox tenants invite through the identity server with real emails
- seam 21 `Invite` flow, spec 0007 `ISandboxContext`, digest ("delivery states report sandboxed sends as `Sent`"). `IdentityInvitation` has no sandbox flag; a sandbox tenant's admin can email real customers. Add `Sandbox: bool` to `IdentityInvitation` (the identity server's sandbox mode) or refuse `Invite` on sandbox tenants (`Users.InviteNotAllowedInSandbox`) with `Status = Active` reserved for subjects already known.

### 37. The persist read-back streams inside the open transaction
- §1.6 step (12). Locks (U on roots, X on written rows, the applock) are held while the result sets cross the network; a slow client with `ReturnEntities = true` on 1,000 rows stretches every lock. State `ReturnEntities = false` for import and MCP bulk saves and cap the read-back at `MaxEntitiesPerSave`; consider emitting the read-back after `COMMIT` for non-temporal roots (the ledger's flag 10 argument for in-transaction reads is only strong for temporal history consistency).

### 38. Raw `Sql` can hide DML behind `EXEC`, escaping `TELLMA0003` and the tag epilogue
- seam 1 Members ("the analyzer requires `Writes` on any statement containing DML"). State that `EXEC`/`sp_executesql` in distribution raw SQL is refused by the analyzer except the allow-listed `sp_sequence_get_range`/`sp_getapplock`.

### 39. Notation slips in seams.md
- seam 3.2 remark "protected virtual hooks, empty defaults" (C# keywords); seams 15, 21, 22 apply attributes in C# call syntax inside `service` blocks (`[ApiAction("summary", MemberOnly = true, Idempotent = true)] Summary()`); seam 6's inline `builder.AddTellma("acme", t => …)` illustration lacks the *Illustration* tag. Replace the first with "overridable hooks", move the attribute applications to Members tables (`Summary — [ApiAction] "summary", member-only, idempotent`), and tag the illustration.

### 40. Name drift between ledger and seams that §24 does not list
- ledger seam 1 `Save<TEntity>(rows, options: SaveOptions?)` vs seams `Save<TEntity>(rows, concurrency)` (listed, §24.1 — fine); ledger §2.16 action list lacks `ExportForImport` while §24.8 uses it as the default of `[ApiAction("export-for-import")]` although seam 3's table maps it to `Read` — state that `ExcelOperations` passes `Action = "Read"/"Save"` explicitly; ledger §2.29 `Kind varchar(8)` vs seam 11.4 `varchar(8)` fine, but `Users.State varchar(8)` cannot hold a future `Deactivated`/`Suspended` value — reserve `varchar(12)`; ledger §2.30 lists `IBlobService` "staging and resolution; the token is the staged `Id`" while seam 12 adds `IBlobKindRegistry`/`BlobKindDescriptor`/`BlobName` unlisted in the ledger — add to §2.30.

### 41. `[ApiAction].Action` "must exist at startup" contradicts "zero permission code"
- seam 11.1 ("every `[ApiAction].Action` must exist at startup"), ledger §1 item 27. A distribution author adding `[ApiAction("close", Action = "Close")]` must know to call `contribution.Securables(b => b.Add("acme.Thing", "Close", …))`. State instead that the stack feature's contributor auto-registers `(Resource, Action, FilterRoot = Entity)` for every `[EntityAction]` and `[ApiAction]` on entity services, and that `[ApiRoute]` services register through `contribution.ApiService<T>()` (a sugar missing from seam 6) whose actions default to `MemberOnly = false` plus a declared `Action`.

### 42. Custom validator registration has no sugar and an unstated variance rule
- seam 3.2 (`IEntityValidator<in TEntity> … resolved for the leaf and every base; registration order`), seam 6 sugars. Add `contribution.Validator<TEntity, TValidator>()` and `SaveEffect<,>()`; state that a validator registered for `Center<MyCenter>` runs for `MyCenter` by contravariance and that "registration order" means feature order then declaration order.

### 43. `[ApiResource]` optionality for a plain entity is ambiguous
- seam 3.1 (`[ApiResource(…)] on entity type`), seam 3 examples (`User`, `Center` carry it). Does an entity without it get web/MCP endpoints? State: `contribution.Entity<T>()` projects endpoints regardless; `[ApiResource]` only overrides segment, description, `Mcp`, `Public`.

### 44. `BlobReadAccess.Custom` has no declared source for `CustomReadFilter`
- seam 12 (`BlobKindDescriptor.CustomReadFilter: FilterTree?`, `BlobReadAccess = OwnerRead | AnyMember | Custom`). Add `BlobsBuilder.Kind(kind, policy, readFilter: FilterTree)` or an `IBlobReadPolicy<TOwner>` contract; otherwise drop `Custom` from this release.

### 45. `FromCache` inside `Validate`/`Persist` batches defaults to the `Refresh` policy
- seam 1 Members (`FromCache … statement only on a miss`), seam 5.3 (`entity:<Name>` → `Refresh`). A validator checking an FK against a cached lookup may accept a row deleted a moment ago (the DB FK catches deletes, not deactivations). State that `FromCache` on a non-`Read` batch declares `Rerun`.

### 46. `AccessGrantSource.Bespoke` as a sufficient grant is implied, not stated
- seam 11.2, seam 15 ("the notifications page is the standard `query` under the self-scope criterion"), seam 8.1 (`core.Job` self-scope). 0013 D-text tests "bespoke-only access" (theme file line 955) but the seams never say a criterion alone yields `Filtered` with no stored grant. One sentence in seam 11.2.

### 47. No N−1 rule for the emitter's explicit column lists and the `SELECT [j].*` job readers
- §1.3 `INSERT … (columns)`, §8.3 `SELECT [j].*`. Spec 0001 covers the UDTT window and ARCHITECTURE.md (line 1115) requires N−1-compatible migrations, but the ledger never states the expand/contract rule a distribution must follow for its own tables (new columns nullable or defaulted; drop after one release) nor that readers bind by name. Add it to ledger §2 and the 0011 spec's migration section.

### 48. `Kind = Service` exists but MCP "autonomous agents via service accounts" cannot be exercised this release
- ledger §2.29/§7 (creation deferred), seam 13.2 (MCP auth), briefing T6. Record in §7 that the MCP surface is human-only until service accounts ship, so 0015's auth section does not describe an untestable path as shipped.

### 49. `Tellma-Version-Tags` and `me` expose the raw tag Guids
- Opaque and restore-safe, no leak; but they double as a coarse activity oracle across users (the tenant `permissions` tag changes whenever any role changes). Acceptable; note it in 0012 so nobody later "improves" the tag into a counter.

### 50. `IdsRequest.Ids: list<long>` vs `JobAccepted(JobId: int)`, `MeResult.User.Id: int`, `InviteResult(Id: int)`
- seam 13.1. The "wire ids are `long`" rule is stated for request records only; say so explicitly in seam 13 to stop a spec author "fixing" the results.
