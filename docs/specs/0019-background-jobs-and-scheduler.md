# Spec: Background Jobs and the Scheduler

- **Author:** Ahmad Akra
- **Date:** 4 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Work that outlives a request — sending queued email, importing or exporting a large workbook,
sweeping staged blobs, deleting expired rows, verifying tree counts — runs in the background, on
whichever application instance has capacity, isolated per tenant, without ever executing twice at
once. This spec ships that machinery for the tenant database: one queue table per tenant, a handler
contract, the lease statements that let several instances claim, renew and complete rows safely, a
worker with per-tenant polling and in-process nudges, a cron scheduler that turns schedule rows into
jobs with explicit missed and overlap policies, the credentials model every job runs under, trace
linkage, the administrative surface, and the guard that keeps a restored backup from replaying a
year of queued work.

It builds on the frozen base: spec 0001's table types carry the standalone row lists the statements
bind; spec 0003's session rules govern nothing here directly but fix the identity model (`sub`,
the system principal) the run-as rules rest on; spec 0007's email outbox is the first consumer this
machinery was reserved for — its `IEmailOutbox` and `EmailEnqueueRequest` shape (spec 0007 §13)
needs no change and its "signal after commit" is the post-commit nudge defined here; spec 0008's
`QuerySpec` and `KeySetRestriction` amendment are how an entity-backed handler receives its rows.
The batch abstraction of spec 0011 executes every statement in this document; spec 0013's connect
prologue runs at the head of every job's completion batch; spec 0014's pipeline exposes the enqueue
call to services at the transactional side-effects step; spec 0018 ships the export and import
handlers; spec 0016 ships the blob sweep and reconcile handlers; spec 0020 ships the notifications
and hub events the worker raises.

The design departs from an entity-column shape deliberately: no business table carries lease or
progress columns. A business row that is processed in the background references its job through a
single nullable foreign key, and the machinery never writes a business table. Cron expressions live
in one table, `core.Schedules`, never on a business entity.

Tenantless (catalog-scoped) jobs, a cross-instance nudge, push delivery, and the email outbox's own
handler are left to later specs; the scope boundary with spec 0020 is two interfaces (`INotifier`,
`IClientEventPublisher`) that this spec consumes and spec 0020 implements.

## Goals / Non-goals

**Goals**

- Ship `core.Jobs`, `core.Schedules`, `core.ScheduleStates` and `core.JobWorkerState` in every
  tenant database, with the `Job` and `Schedule` entities, their stacks and their securables.
- Ship the handler contract (`[JobHandler]`, `IJobHandler`, `IEntityJobHandler<TEntity>`,
  `JobBatch`, `JobItem`, `IJobProgress`) with batch-shaped, per-item outcomes and an at-least-once
  execution guarantee stated as a contract handlers can rely on.
- Ship `IJobQueue` riding the persist batch: ids assigned inside the statement, the owning row's
  `JobId` set in the same transaction, a post-commit nudge.
- Ship the fixed-text statements — claim, renew, append, complete, the gap check and hold, the
  heartbeat stamp, tick steps 1 and 2, the orphan sample and the retired-key cancel — plus enqueue
  and run-now, every one fenced by a lease token where a lease exists.
- Ship `JobWorker`: per-instance, per-tenant adaptive polling, local nudges, bounded concurrency,
  cancellation, a safe drain on shutdown, and no cross-instance coordination state.
- Ship the scheduler: Cronos-parsed five-field cron, a per-schedule time zone, the missed policies
  `Coalesce | ReplayAll | Skip` and the overlap policies `Skip | Allow`, built-in schedules in the
  reserved band, run-now, pause on owner deactivation, exhaustion or a retired key, and the gap
  safeguard.
- Ship the credentials model: the system user for built-ins, the creator for user schedules with
  an explicit `take-over`, the requester for user-triggered jobs, permissions evaluated at run time.
- Ship trace linkage (a new root span per handler invocation, linked to each job's enqueuing
  context), the instrument set, the admin actions, and the retention and tree-verify handlers.

**Non-goals (explicitly out of scope)**

- **Notifications, the inbox, the hub** — spec 0020; this spec raises notifications and the
  `job.changed` event through its contracts only.
- **The export and import handlers and the `Export`/`Import` entities** — spec 0018; this spec
  states the contract they meet.
- **The blob sweep and reconcile handlers** — spec 0016.
- **The email outbox handler** — the outbox spec; the alignment rules are recorded in §14.
- **Tenantless (catalog-scoped) jobs and schedules** — a later spec; the catalog session sweep is
  a hosted timer in spec 0010, not a job.
- **A cross-instance nudge** (Redis or Azure SignalR as a server bus) — deferred; pickup on other
  instances is bounded by `MaxPollInterval`.
- **Per-handler opt-out of the gap hold** (`HoldAfterGap`) — deferred.

## 1. Placement and architecture

### 1.1 Projects and packages

| Piece | Location | Notes |
|---|---|---|
| Contracts | `src/core/Tellma.Core.Abstractions/`, namespace `Tellma.Core.Abstractions.Jobs` | Every type of §2–§4, §7.1, §12.1 and §13; BCL plus `Tellma.Core.Queryex` only. |
| Runtime | `src/core/Tellma.Core/`, namespace `Tellma.Core.Jobs` | `JobWorker`, `TenantPoller`, `JobStatements`, `JobQueue`, `JobService`, `ScheduleService`, the Cronos scheduler, the retention and tree-verify handlers, the contribution realizers. Adds the `Cronos` 0.13.0 package reference to `Tellma.Core`. |
| Composition | `Tellma.Core.Composition` | `CoreFeature` contributes the handlers, built-in schedules, notification types, the `job.changed` event, the stacks and the worker's hosted service. |
| Tests | `test/core/Tellma.Core.Tests/Jobs/`, `test/core/Tellma.Core.IntegrationTests/Jobs/` | §16. |
| Reference distribution | `distributions/acme/` | Infra template sets `Always On` and `WEBSITES_CONTAINER_STOP_TIME_LIMIT = 30` (§12.3). |

Dependency edges: `Tellma.Core.Abstractions` → `Tellma.Core.Queryex`; `Tellma.Core` →
`Tellma.Core.Abstractions`, `Tellma.Core.EntityFrameworkCore`,
`Microsoft.Extensions.Hosting.Abstractions`, `Cronos`. Nothing here references SignalR or ASP.NET
Core: the worker runs in any host that composes `AddTellma`, including a future dedicated worker
host, which registers Data Protection sharing the web host's application name and key ring when its
handlers read spec 0012's `Secret` settings. `Quartz.Extensions.Hosting` stays pinned for the
identity server alone; tenant scheduling never uses it.

Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are omitted,
so a block is never pasted into code. SQL statements are the exact text to emit, with `{b}` the
statement's batch ordinal.

### 1.2 Vocabulary

- A **job** is one durable unit of background work: one row of `core.Jobs`.
- A **handler** is the class that executes jobs of one **handler key**.
- A **claim** leases a batch of due jobs of one key to one instance under one **lease token**.
- A **schedule** is a row of `core.Schedules`: a cron definition that creates jobs when due.
- A **tick** is the scheduler's pass over due schedules; it rides the worker's poll.
- **At-least-once** is the execution guarantee: a lost lease re-runs the item elsewhere.

### 1.3 Principles

1. **The database clock is the only clock.** Every comparison, due time, lease expiry and retry
   delay is computed from `SYSUTCDATETIME()` inside the statement; instance clocks never enter a
   stored timestamp. Retry delays travel as durations.
2. **Correctness comes from leased rows alone.** No catalog table, application lock, Redis key or
   backplane message coordinates instances; `READPAST` paired with `UPDLOCK, ROWLOCK` and a
   fencing token are the entire protocol.
3. **A lease fences every write that follows it.** Completion, checkpoints and the tick's second
   step compare the token and the affected row count and `THROW 50422` on a mismatch, so nothing a
   handler wrote survives a lost lease.
4. **The machinery never writes a business table.** Business rows reference jobs; the enqueue
   statement sets that one column inside the caller's transaction (a handler that inserts its own
   row supplies it, §2.4), and nothing else.
5. **Nudges are an optimisation, never a dependency.** Every job is processed within
   `MaxPollInterval` with no nudge at all.
6. **Bookkeeping never touches audit columns.** `core.Jobs` carries no `ModifiedAt`; the
   administrative actions of §11.1 write the table through fixed-text statements;
   `core.ScheduleStates` is a non-temporal sibling so renewals and ticks write no history rows.

### 1.4 Statement execution rules

Every statement in §4–§7 and §11.1 is fixed text owned by this spec, executed through spec 0011's
`IDataBatch.Sql(sql, options)` with `SqlOptions.Writes` naming each table it writes. Table-variable
and parameter names use the reserved `@tb{b}_` prefix (`@tb{b}_claimed`, `@tb{b}_due`,
`@tb{b}_fired`, `@tb{b}_gap`, `@tb{b}_jobIds`, `@tb{b}_room`, `@tb{b}_p{i}`, `@tb{b}_t{i}`);
distribution code never emits them. Statements name other specs' columns (`core.Users`' `IsActive`
in §7.4, an owner's `JobId` in §4.2) under spec 0011 §3.2's column-name rule.

| Statement | `Purpose` | `TransactionMode` | `Idempotent` | Scope |
|---|---|---|---|---|
| gap check and hold, claim, tick step 1, the samples, the retired-key cancel, heartbeat stamp (one poll round trip) | `Maintenance` | `None` | `true` | system tenant scope, no prologue |
| renew | `Maintenance` | `None` | `true` | system tenant scope |
| append (`IJobProgress.Append`) | the caller's | the caller's | `false` | the handler's chunk batch, reached through spec 0014's `SaveOptions.OnPersist` |
| complete | `Persist` | `Auto` | `false` | the run-as user's scope, prologue and guard; the system tenant scope, completion statement and notifications only, for rows completed without a handler (§5.4) |
| tick step 2 | `Persist` | `Auto` | `false` | system tenant scope, `System` prologue |
| enqueue | the caller's (`Persist`) | the caller's (`Auto`) | `false` | the caller's scope |
| run-now (§7.8) | the action's (`Persist`) | `Auto` | `false` | the caller's scope |
| retry, cancel, resume (§11.1) | the action's (`Persist`) | `Auto` | `true` | the caller's scope |

`Maintenance` batches carry no connect prologue, and their epilogue bumps only the tags their
declared writes resolve to (spec 0011 §5.5); none of the four job tables carries a version-tag
attribute, so nothing here bumps a tag. A `None` batch autocommits each statement; the executor
re-runs the whole round trip after a reported transient failure, and after an ambiguous one when
every statement is `Idempotent` (spec 0011 §6.5); §6.8 states why every poll statement and the
renewal tolerate the re-run. `READPAST` appears only beside `UPDLOCK, ROWLOCK` (a bare `READPAST`
is a no-op under read-committed snapshot isolation, which is on by default on Azure SQL and off
on-premises) and never under `SNAPSHOT` isolation (error 650): the executor runs these batches at
`READ COMMITTED`.

## 2. The job table and the `Job` entity

### 2.1 `core.Jobs`

Non-temporal; `LOCK_ESCALATION = DISABLE`; no triggers; `[TableType]` over the entity's columns
minus the two model-only columns, `StateJson` and `LeaseToken` (§2.3; the tick's insert binds the
derived UDTT); sequence `core.sq_Jobs`; written only by the statements of this spec and never
through `IDataBatch.Save`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered; `CK_Jobs_Id CHECK ([Id] > 0)` | assigned inside the enqueue and tick statements from `core.sq_Jobs` |
| `HandlerKey` | `nvarchar(64)` | no | | §3.7 grammar |
| `Status` | `varchar(9)` | no | | `JobStatus` as text |
| `DueAt` | `datetimeoffset(3)` | no | | earliest claim time; moved by retry backoff and `retry`/`resume` |
| `Attempts` | `int` | no | `DF_Jobs_Attempts 0` | counts claims (§5.5) |
| `LeaseToken` | `uniqueidentifier` | yes | `CK_Jobs_Lease ((Status = 'Running' AND LeaseToken IS NOT NULL AND LeaseExpiresAt IS NOT NULL) OR (Status <> 'Running' AND LeaseToken IS NULL AND LeaseExpiresAt IS NULL))` | fencing token; model-only (§2.3) |
| `LeaseExpiresAt` | `datetimeoffset(3)` | yes | | |
| `LeaseOwner` | `nvarchar(128)` | yes | | `<instance id>/<process id>`: the instance id is `WEBSITE_INSTANCE_ID` when set, else the machine name; truncated to 128 characters; diagnostic only |
| `StartedAt` | `datetimeoffset(3)` | yes | | first claim |
| `CompletedAt` | `datetimeoffset(3)` | yes | | terminal time |
| `CancelRequestedAt` | `datetimeoffset(3)` | yes | | stamped by `cancel` on a `Running` row; cleared by `retry` and `resume` (§11.1) |
| `ArgumentsJson` | `nvarchar(max)` | yes | | ≤ 64 KB, enforced at enqueue and on schedule save |
| `StateJson` | `nvarchar(max)` | yes | | handler checkpoint; model-only (§2.3) |
| `ProgressPercent` | `tinyint` | yes | `CK_Jobs_Progress (ProgressPercent <= 100)` | |
| `ProgressKey` | `nvarchar(64)` | yes | | the last progress message's resource key (§3.3) |
| `ProgressArgumentsJson` | `nvarchar(4000)` | yes | | its arguments |
| `ErrorCode` | `nvarchar(64)` | yes | | a resource key: `Jobs.UserInactive`, `Jobs.AttemptsExhausted`, `Jobs.EntityMissing`, `Jobs.HeldAfterGap`, `Jobs.Cancelled`, `Jobs.Internal`, `Jobs.HandlerRetired` (§3.4), a handler's code or a validation code |
| `ErrorArgumentsJson` | `nvarchar(4000)` | yes | | its arguments |
| `ErrorTraceId` | `varchar(32)` | yes | | the W3C trace id of the activity that recorded the error (§3.4) |
| `TraceParent` | `nvarchar(55)` | yes | | W3C `traceparent` of the enqueuing activity |
| `RunAsUserId` | `int` | no | `FK_Jobs_RunAsUserId → core.Users` | §8 |
| `RequestedById` | `int` | yes | `FK_Jobs_RequestedById → core.Users` | who is notified |
| `ScheduleId` | `int` | yes | `FK_Jobs_ScheduleId → core.Schedules ON DELETE SET NULL` | |
| `ScheduledFor` | `datetimeoffset(3)` | yes | | the cron occurrence; null for a run-now job (§7.8) |
| `CreatedAt` | `datetimeoffset(3)` | no | | |

Indexes, all filtered:

| Index | Key | Include | Filter |
|---|---|---|---|
| `IX_Jobs_Available` | `(HandlerKey, DueAt, Id)` | `(LeaseExpiresAt)` | `Status IN ('Pending', 'Running')` |
| `IX_Jobs_Lease` | `(LeaseToken)` | `(Status)` | `LeaseToken IS NOT NULL` |
| `IX_Jobs_ScheduleActive` | `(ScheduleId)` | | `Status IN ('Pending', 'Running') AND ScheduleId IS NOT NULL` |
| `IX_Jobs_RequestedBy` | `(RequestedById, CreatedAt DESC)` | | `RequestedById IS NOT NULL` |
| `IX_Jobs_Retention` | `(Status, CompletedAt)` | | `Status IN ('Succeeded', 'Failed', 'Cancelled')` |
| `IX_Jobs_Held` | `(RequestedById)` | | `Status = 'Held'` |

- Every claim repeats the filter literals of `IX_Jobs_Available` verbatim so the optimizer can
  prove the predicate a subset of the filter; the plan test of §16 pins the seek.
- **The error group.** `ErrorCode`, `ErrorArgumentsJson` and `ErrorTraceId` are one group: a
  statement that writes `ErrorCode` writes the other two with it, and a statement that keeps the
  code keeps them. Set-based statements that write a code — the gap hold (§6.10), the `cancel`
  action (§11.1), the retired-key cancel (§6.9) — write null arguments and a null trace id.

### 2.2 States and transitions

```csharp
// Tellma.Core.Abstractions.Jobs
public enum JobStatus { Pending, Running, Succeeded, Failed, Cancelled, Held }
```

| From | To | By |
|---|---|---|
| `Pending` | `Running` | claim (§5.1) |
| `Running` | `Pending` | completion with `Retry` or `Released` (§5.4) |
| `Running` | `Succeeded` / `Failed` / `Cancelled` | completion (§5.4), the rows completed without a handler included (§5.1) |
| `Pending` | `Cancelled` | `cancel` action |
| `Running` | `Running` + `CancelRequestedAt` | `cancel` action; the item's token is cancelled at the next renewal, or the next claim cancels the row (§5.1) |
| `Pending`, `Running` (lease expired) | `Held` | gap hold (§6.10) |
| `Pending`, `Held`, `Running` (lease expired) | `Cancelled` | retired-key cancel (§6.9) |
| `Held` | `Pending` | `resume` action |
| `Held` | `Cancelled` | `cancel` action |
| `Failed` | `Pending` | `retry` action (`Attempts = 0`) |

The availability predicate, the only one the claim uses, is `Status IN ('Pending', 'Running')`
and `DueAt <= now` and `(LeaseExpiresAt IS NULL OR LeaseExpiresAt < now)`. A row whose lease
lapsed is available again with no reaper; a row past its last allowed attempt is failed by the
claim that returns it (§5.1).

### 2.3 The `Job` entity

```csharp
// Tellma.Core.Abstractions.Jobs
public class Job : Entity<int>                              // system-written; table core.Jobs; stack resource core.Job
{
    public string HandlerKey { get; set; }
    public JobStatus Status { get; set; }
    public DateTimeOffset DueAt { get; set; }               // datetimeoffset(3)
    public int Attempts { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }     // datetimeoffset(3)
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? StartedAt { get; set; }          // datetimeoffset(3)
    public DateTimeOffset? CompletedAt { get; set; }        // datetimeoffset(3)
    public DateTimeOffset? CancelRequestedAt { get; set; }  // datetimeoffset(3)
    public string? ArgumentsJson { get; set; }
    public byte? ProgressPercent { get; set; }
    public string? ProgressKey { get; set; }
    public string? ProgressArgumentsJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorArgumentsJson { get; set; }
    public string? ErrorTraceId { get; set; }
    public string? TraceParent { get; set; }
    public int RunAsUserId { get; set; }
    public int? RequestedById { get; set; }
    public int? ScheduleId { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }       // datetimeoffset(3)
    public DateTimeOffset CreatedAt { get; set; }           // datetimeoffset(3)
}
```

- `Job` derives from spec 0011's `Entity<int>` (no audit columns) and carries every member as
  `[ServerOwned]`; the stack is `[Stack(Operations = Query | Details)]` with `[DefaultSelect]`
  covering every member minus `ArgumentsJson`, `LeaseOwner` and `TraceParent`; the details page
  selects `ArgumentsJson`, as spec 0018 §12.1's pages select their JSON payloads. `ErrorTraceId`
  is the identifier a requester quotes in a support ticket, as with spec 0015 §6.2's trace id.
- `StateJson` and `LeaseToken` are the machinery's columns: the jobs feature's model configuration
  maps them as shadow properties, never members; they are absent from the Queryex schema (spec
  0011 §11.1) and excluded from the Jobs UDTT (`ExcludeFromTableType()`, as spec 0011's tree
  convention excludes `Node`); the worker reads them from the claim's result set through the job
  reader of spec 0011 §4.3.
- Queryex navigations `RunAsUser`, `RequestedBy`, `Schedule` derive from the foreign keys;
  `[RelatedSelect]` is the default projection of spec 0011.

### 2.4 `IJobEntity`

Spec 0011's capability interface, restated verbatim as consumed here:

```csharp
// Tellma.Core.Abstractions.Entities (spec 0011)
public interface IJobEntity
{
    int? JobId { get; set; }                            // server-owned; FK -> core.Jobs ON DELETE SET NULL; unique where not null
}
```

One business row is one job. Its `JobId` is set by the enqueue statement (§4.2) or supplied by a
handler that inserts its own row (`core.export`, §14.1) under `EnlistSaveOptions.ServerOwned`, and
cleared by retention (`SET NULL`); the machinery never writes the business table otherwise. Work
that spans many rows is an arguments-only job whose handler queries the rows itself.

### 2.5 Standalone table types

Registered by `CoreFeature` in `Tellma.Core.Abstractions.TableTypes`, physical
`[dbo].[<Name>_<hash8>]` per spec 0001 §5:

| Type | Columns | Use |
|---|---|---|
| `JobRequestList` | `Ordinal int PK`, `HandlerKey nvarchar(64)`, `DueAt datetimeoffset(3) NULL`, `ArgumentsJson nvarchar(max) NULL`, `TraceParent nvarchar(55) NULL`, `RunAsUserId int`, `RequestedById int NULL` | enqueue (§4.2) |
| `JobOutcomeList` | `Id int PK`, `Status varchar(9)`, `Released bit`, `RetryAfterSeconds int NULL`, `ErrorCode nvarchar(64) NULL`, `ErrorArgumentsJson nvarchar(4000) NULL`, `ErrorTraceId varchar(32) NULL`, `StateJson nvarchar(max) NULL` | complete (§5.4) |
| `JobProgressList` | `Id int PK`, `ProgressPercent tinyint NULL`, `ProgressKey nvarchar(64) NULL`, `ProgressArgumentsJson nvarchar(4000) NULL`, `StateJson nvarchar(max) NULL` | renew, append (§5.2, §5.3) |
| `JobLeaseList` | `Id uniqueidentifier PK`, `LeaseSeconds int` | renew |
| `ScheduleNextList` | `ScheduleId int PK`, `NextDueAt datetimeoffset(3) NULL`, `LastScheduledFor datetimeoffset(3) NULL` | tick step 2, schedule save (§7.6, §7.8) |

The existing `IdList` carries job ids for the admin actions and schedule ids for `take-over`, and
`StringList` the handler keys of the orphan sample and the retired-key cancel (§6.9).

## 3. Handlers

### 3.1 The handler attribute

```csharp
// Tellma.Core.Abstractions.Jobs
public sealed class JobHandlerAttribute(
    string Key, int BatchSize = 1, int LeaseSeconds = 300, int MaxAttempts = 5, int MaxConcurrency = 1,
    int RetryBaseSeconds = 30, int RetryMaxSeconds = 3600, bool Schedulable = false)
    : Attribute;                                        // on type
```

| Option | Range | Meaning |
|---|---|---|
| `Key` | §3.7 grammar, ≤ 64 chars | The handler key persisted on every job row. |
| `BatchSize` | 1–500 | Maximum items per claim. |
| `LeaseSeconds` | ≥ 15 | Sliding lease length; renewal interval is one fifth, so one missed renewal is never fatal. |
| `MaxAttempts` | ≥ 1 | Claims after which a row is failed with `Jobs.AttemptsExhausted`. |
| `MaxConcurrency` | ≥ 1 | Batches of this key in flight per tenant per instance. |
| `RetryBaseSeconds`, `RetryMaxSeconds` | ≥ 1; max ≥ base | Full-jitter exponential backoff bounds (§5.5). |
| `Schedulable` | | Whether a user-created schedule may name this key (§7.8); built-ins are exempt. |

Values outside the range fail the composition gate with a `CompositionProblem` naming the handler.

### 3.2 Two handler shapes, one registry

```csharp
// Tellma.Core.Abstractions.Jobs
public interface IJobHandler                            // arguments-only handlers; resolved from the job scope
{
    Task ExecuteAsync(JobBatch<Job> batch);             // the operation's token is the batch token
}

public interface IEntityJobHandler<TEntity> where TEntity : IJobEntity   // entity-backed handlers; the claim appends the entity join load
{
    Task ExecuteAsync(JobBatch<TEntity> batch);         // the operation's token is the batch token
}
```

A handler class implements exactly one of the two and carries `[JobHandler]`. For
`IEntityJobHandler<TEntity>` the worker appends spec 0011's `Query<TEntity>` with `Restrictions =
[KeySetRestriction("JobId", "@tb{b}_claimed")]` to the claim round trip, so the claimed rows and
their entities arrive together; `TEntity` must be a mapped leaf (the composition gate resolves the
registered leaf through `UseEntity`). Handlers are resolved from the job's tenant scope (§8) with
`Scoped` lifetime, one instance per invocation.

### 3.3 The batch, the item, and progress

```csharp
// Tellma.Core.Abstractions.Jobs
public sealed record JobBatch<TItem>
{
    public IReadOnlyList<JobItem<TItem>> Items { get; init; }
    public JobItem<TItem> Single { get; }               // throws when Items has more than one
    public IDataBatch Batch { get; init; }              // statements appended here run in the completion transaction
}

public sealed record JobItem<TItem>
{
    public Job Job { get; init; }
    public TItem Item { get; init; }                   // never null: the job, or the claimed entity row (§5.1)
    public CancellationToken CancellationToken { get; init; }
    public JobCancellationReason CancellationReason { get; }   // None until CancellationToken is cancelled
    public IJobProgress Progress { get; init; }
    public TArgs Arguments<TArgs>();                    // tolerant JSON
    public TState? State<TState>();
    public void Succeed();
    public void Retry(TimeSpan? after = null, JobError? error = null);
    public void Fail(JobError error);
    public void NotifyOnSuccess(NotificationRequest request);
}

public sealed record JobMessage(string Key, IReadOnlyDictionary<string, object?> Arguments);   // a resource key and its ICU arguments

public interface IJobProgress
{
    void Report(byte? percent, JobMessage? message, object? state);                     // buffered; rides the next renewal
    Task FlushAsync();                                                                  // writes now
    void Append(IDataBatch batch, byte? percent, JobMessage? message, object? state);   // fenced checkpoint inside the caller's transaction
}

public sealed record JobError(string Code, IReadOnlyDictionary<string, object?> Arguments)
    : CodedError(Code, Arguments);                                                      // CodedError: spec 0014 §7.3

public enum JobCancellationReason { None, LeaseLost, CancelRequested, HostStopping }

public sealed class JobFailedException(JobError Error) : Exception;         // fails the unmarked items at once (§3.4); never reaches a request
```

| Member | Meaning |
|---|---|
| `JobBatch.Items` | The partition handed to this invocation (§3.5), in claim order. |
| `JobBatch.Batch` | A `Persist` batch in the run-as scope. Statements the handler appends (`Sql` with declared writes, `Save`, `Update`, `Delete`) execute in the completion transaction after the completion statement (§5.6) and write unowned tables only: a table a stack owns is written by enlisting the rows with the job frame (§8; spec 0014 §13.3), whose groups the frame places on this batch, and a statement appended here that writes such a table is refused at append (spec 0014 §13.3). Nothing appended here runs if the lease is lost. |
| `JobItem.Item` | The job itself for `IJobHandler`; the entity row for `IEntityJobHandler`. Never null: a claimed row whose entity is missing is completed without invoking the handler (§5.1). |
| `JobItem.CancellationToken` | Linked to the batch token; also cancelled alone when a renewal reports this row's `CancelRequestedAt` (§5.2). For a batch of one, item and batch tokens coincide. |
| `JobItem.CancellationReason` | `None` until the item's token is cancelled; then `CancelRequested`, `LeaseLost` or `HostStopping`, the reason §3.4 maps the item under; once set it changes only to `LeaseLost`. |
| `JobItem.Arguments<TArgs>` | Deserializes `ArgumentsJson` with the platform JSON options: unknown members ignored, missing members defaulted; `null` when the column is null and `TArgs` is nullable, else a fresh default instance. |
| `JobItem.State<TState>` | The checkpoint the claim returned or the handler last wrote through `Report`, `Flush` or `Append`; `null` when none. |
| `JobItem.Succeed` / `Retry` / `Fail` | Explicit per-item outcomes; the last call wins; unmarked items follow §3.4. A handler never marks an item `Cancelled`: it leaves the item unmarked and §3.4 maps it from its reason. |
| `JobItem.Retry(after)` | `after` null ⇒ the backoff of §5.5; a supplied delay is used verbatim (capped at 30 days). |
| `JobItem.NotifyOnSuccess` | Queues a spec 0020 `NotificationRequest` that the worker appends to the completion batch only when the item's final outcome is `Succeeded`. |
| `IJobProgress.Report` | Buffers one `JobProgressList` row for this job; the next renewal carries it (§5.2); repeated calls overwrite. |
| `IJobProgress.Flush` | Executes one renew round trip now carrying the buffered rows of every in-flight job of the tenant on this instance; a checkpoint before a non-idempotent step. |
| `IJobProgress.Append` | Appends the append statement (§5.3) to a batch the handler owns — a chunked import's per-chunk persist batch, reached through spec 0014's `SaveOptions.OnPersist` — so the checkpoint commits with the chunk or not at all. |
| `JobMessage`, `JobError` | A resource key and its arguments. The SPA renders the key with its arguments from the string pack (spec 0012 §10.4), exactly as it renders a validation code; the worker renders it in English for the log (§9). A handler's keys live in its own assembly's `Strings.resx` (spec 0012 §10.1); the platform's `Jobs.*` keys (§2.1) are Core's. The arguments serialise with the platform JSON options into an `nvarchar(4000)` column (§2.1); beyond 4,000 characters `Report`, `Append`, `Retry`, `Fail` and `JobFailedException`'s constructor throw `ArgumentException`, a programmer error as in §4.1. |

### 3.4 Outcome rules

Evaluated by the worker per item after `Execute` returns or throws, in this order:

1. When any item's `CancellationReason` is `LeaseLost`, the completion is not executed (the fence
   would reject it) and every outcome is discarded, marked ones included.
2. A marked item keeps its explicit outcome.
3. An unmarked item whose reason is `CancelRequested` is `Cancelled` (`Jobs.Cancelled`), however
   `Execute` ended.
4. Every other unmarked item follows how `Execute` ended:

| `Execute` | Unmarked items become |
|---|---|
| returned normally | `Succeeded` |
| threw `JobFailedException` | `Failed` with its `JobError` |
| threw `ValidationException` | `Failed` at once with the code and arguments of its first error (spec 0014 §7.3) |
| threw `OperationCanceledException` | `Released` when the handler never observed the item (no progress, no state, no explicit outcome), else `Retry` with `after = 0` |
| threw anything else | `Retry` with `Jobs.Internal`; the exception goes to `JobHandlerFailed` (§9) |

Every outcome that records an `ErrorCode` records `ErrorTraceId`, the trace id of the current
activity: `process <key>` (§9) for an invocation, an inactive run-as user's completion included
(§8), and the poll's activity for a row completed without a handler (§5.1). `Jobs.Internal`
carries no arguments; `JobHandlerFailed` logs the exception at `Error` inside `process <key>`, so
its `TraceId` equals the row's `ErrorTraceId` (spec 0010 §9 puts the trace id and the job id on
every log line). `Retry` becomes `Failed` (with the same error, or `Jobs.AttemptsExhausted` when
none) when the row's `Attempts` (already incremented by the claim) is at or above `MaxAttempts`.

### 3.5 Run-as partitioning

A claim may return rows with different `RunAsUserId` values. The worker partitions the claimed rows
by `RunAsUserId`, preserving claim order, and invokes the handler once per partition, sequentially,
each invocation in its own tenant scope for that user (§8) under the same lease token, with its own
completion batch. Throughput handlers (the email outbox, retention, sweeps) are enqueued with
`RunAsUserId = WellKnownIds.SystemUserId`, so every claim is one partition; user-triggered handlers
(`core.export`, `core.import`) declare `BatchSize = 1`, so partitioning is moot.

### 3.6 The idempotency contract

The guarantee is at-least-once: a lost lease, a crashed instance, an ambiguous completion failure or
a `Retry` re-runs the item, possibly on another instance, with `Attempts` incremented. A handler is
correct when either every step is idempotent or it checkpoints through `StateJson` (`Report` +
`Flush`, or `Append` inside its own transaction) and resumes from the checkpoint. Statements a
handler appends to `JobBatch.Batch` and the writes it enlists with the job frame (§8) are inside
the completion transaction and therefore atomic with the outcome; side effects outside the database
(an email sent, a file written) must be recorded in that transaction (the outbox row marked sent,
the export's `FileId`) so a re-run can detect them.

### 3.7 Registration and the key grammar

Handler keys match `^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$`, at most 64 characters: a prefix and one
or more dot-separated kebab-case segments. The `core.` prefix is reserved for the platform; a module
uses its module slug (`gl.`); a distribution uses its deployment slug (`acme.recompute-balances`).
Keys are persisted on rows and seeds and are stable for the life of a distribution; a breaking
change to a handler's arguments is a new key. Notification type keys and hub event names follow the
same grammar.

A feature registers a handler through spec 0010's `FeatureContribution.JobHandler<THandler>()`
(`JobHandlerContributionItem`). The realizer reads `[JobHandler]`, infers the shape from the
implemented interface, validates the grammar, the option ranges, the `core.` reservation (only
`CoreFeature` may use it), that `TEntity` implements `IJobEntity` and is mapped, and that no key is
registered twice; every problem reports into the composition gate. The realized gate
(`IStartupCheck` `jobs.handlers`) additionally warns when a built-in schedule names a key no
handler registered.

A feature that removes or replaces a handler retires its key through spec 0010's
`FeatureContribution.RetiredJobKey(key)` (`RetiredJobKeyContributionItem`); the realizer validates
the grammar and the `core.` reservation and refuses a key some handler registers or a built-in
schedule names. A retired key's rows are cancelled (§6.9); a user schedule naming it pauses (§7.7).

## 4. Enqueue

### 4.1 Contracts

```csharp
// Tellma.Core.Abstractions.Jobs
public sealed record JobRequest(
    string HandlerKey, object? Arguments, DateTimeOffset? DueAt, int? RequestedById, int? RunAsUserId,
    IJobEntity? Entity);

public interface IJobQueue
{
    BatchResult<IReadOnlyList<int>> Enqueue(IDataBatch batch, IReadOnlyList<JobRequest> requests);
    Task<IReadOnlyList<int>> EnqueueAsync(IReadOnlyList<JobRequest> requests);
}
```

| Member | Meaning |
|---|---|
| `JobRequest.Arguments` | Serialized with the platform JSON options into `ArgumentsJson`; `null` stores null; more than 64 KB is an `ArgumentException` at enqueue (a programmer error, never a validation error). |
| `JobRequest.DueAt` | Null = now (the statement binds `SYSUTCDATETIME()`); a future instant is normalized to offset zero and stored as `datetimeoffset(3)`. |
| `JobRequest.RequestedById` | The user notified on failure and hold and the owner of "My jobs"; null = nobody (failures go to administrators; a hold is reported by `core.scheduler.gap`). |
| `JobRequest.RunAsUserId` | Null = `RequestContext.UserId` in a user scope, `WellKnownIds.SystemUserId` in a system scope; required (else `InvalidOperationException`) in a scope with neither. |
| `JobRequest.Entity` | The owning `IJobEntity` row, already in the same batch's save or already in the table; the statement sets its `JobId` column in the same transaction and `Enqueue` sets the in-memory member when the batch's outcome is read. |
| `IJobQueue.Enqueue` | Appends the statement of §4.2 to `batch`, declares `Writes = { core.Jobs } ∪ { each owner table }`, registers the post-commit nudge (§4.4), and returns a handle whose value is the ids in request order after execution. Synchronous: ids are assigned inside the statement, never from the allocator's buffer. |
| `IJobQueue.EnqueueAsync` | For callers holding no batch: creates a `Persist` batch in the current scope, enqueues, executes, returns the ids. |

Validation at enqueue, all programmer errors: the key is registered on this instance
(`InvalidOperationException` naming the key); `Entity` is an `IJobEntity` whose `Id > 0` (a row with
a temporary id is enqueued through the pipeline's `ContributeAsync`, after ids are assigned);
`Entity.JobId` is null or refers to a terminal job (an active job on a row is a duplicate request).
The pipeline of spec 0014 exposes `PersistContext.Batch` to `ContributeAsync`, which is where a
service hands off work: spec 0018's `ImportService` enqueues `core.import` through it with the
`Import` row as `Entity`.

### 4.2 The enqueue statement

```sql
-- ENQUEUE (IJobQueue.Enqueue; @tb{b}_t0 : JobRequestList; ids reserved here, never from the allocator's buffer;
-- declares writes core.Jobs plus each owner table named by JobRequest.Entity)
DECLARE @tb{b}_first sql_variant, @tb{b}_n int = (SELECT COUNT(*) FROM @tb{b}_t0);
DECLARE @tb{b}_jobIds TABLE ([Ordinal] int NOT NULL PRIMARY KEY, [JobId] int NOT NULL);
IF @tb{b}_n > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Jobs', @range_size = @tb{b}_n, @range_first_value = @tb{b}_first OUTPUT;
INSERT INTO @tb{b}_jobIds ([Ordinal], [JobId]) SELECT [r].[Ordinal], CAST(@tb{b}_first AS int) + [r].[Ordinal] FROM @tb{b}_t0 AS [r];   -- Ordinal is 0-based
INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [TraceParent], [RunAsUserId], [RequestedById], [CreatedAt])
SELECT [i].[JobId], [r].[HandlerKey], 'Pending', COALESCE([r].[DueAt], SYSUTCDATETIME()), 0, [r].[ArgumentsJson], [r].[TraceParent], [r].[RunAsUserId], [r].[RequestedById], SYSUTCDATETIME()
FROM @tb{b}_t0 AS [r] INNER JOIN @tb{b}_jobIds AS [i] ON [i].[Ordinal] = [r].[Ordinal];
-- per JobRequest.Entity (owner table T, owner id @tb{b}_p{k}, ordinal o): the owner's JobId column, inside the same transaction
UPDATE [core].[Imports] SET [JobId] = [i].[JobId] FROM @tb{b}_jobIds AS [i] WHERE [i].[Ordinal] = o AND [core].[Imports].[Id] = @tb{b}_p{k};
SELECT [Ordinal], [JobId] FROM @tb{b}_jobIds ORDER BY [Ordinal];   -- result set: the BatchResult<IReadOnlyList<int>> value
```

The `UPDATE` line is emitted once per request that names an `Entity` (the `[core].[Imports]` line
is the shape): it takes the owner's table from spec 0011's `EntityMetadata` and names its `Id` and
`JobId` columns literally under spec 0011 §3.2's column-name rule. The owner's `ModifiedAt` is not
touched: `JobId` is bookkeeping. The `sp_sequence_get_range` call is the reason the executor's
analyzer whitelists `EXEC sys.sp_sequence_get_range` in platform statements.

### 4.3 The trace parent

`Enqueue` copies `Activity.Current?.Id` (the W3C `traceparent`, 55 characters) into each row's
`TraceParent`; `tracestate` and baggage are never stored. §9 states how the worker uses it.

### 4.4 The nudge

`Enqueue` registers, through `IDataBatch.OnCommitted`, a callback that wakes the local
`TenantPoller` of the batch's tenant (§6.4). The callback runs after the round trip's commit,
outside the transaction; a failure is logged and never thrown. A nudge reaches only the instance
that enqueued; other instances see the rows on their next poll. `EnqueueAsync` nudges after its own
round trip.

## 5. Claim, renew, append, complete

### 5.1 The claim

One claim per registered handler key that has free concurrency slots on this instance for this
tenant, all in the poll round trip (§6.3); `@tb{b}_p1 = BatchSize` (§3.1), so one claim is one
batch of at most 500 rows and a further free slot of the key fills on the next poll (§6.4).

```sql
-- CLAIM (one per handler key with free slots; @tb{b}_p0 key, @tb{b}_p1 n <= 500, @tb{b}_p2 token, @tb{b}_p3 leaseSeconds, @tb{b}_p4 owner)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
DECLARE @tb{b}_claimed TABLE ([Id] int NOT NULL PRIMARY KEY);
-- rows an earlier attempt of this round trip leased under the same token (§6.8); none on a first attempt
INSERT INTO @tb{b}_claimed ([Id])
SELECT [j].[Id] FROM [core].[Jobs] AS [j]
WHERE [j].[LeaseToken] = @tb{b}_p2 AND [j].[LeaseExpiresAt] >= @tb{b}_now;
DECLARE @tb{b}_room int = @tb{b}_p1 - (SELECT COUNT(*) FROM @tb{b}_claimed);
WITH [due] AS (
    SELECT TOP (@tb{b}_room) [j].*
    FROM [core].[Jobs] AS [j] WITH (READPAST, UPDLOCK, ROWLOCK)
    WHERE [j].[Status] IN ('Pending', 'Running')
      AND [j].[HandlerKey] = @tb{b}_p0
      AND [j].[DueAt] <= @tb{b}_now
      AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < @tb{b}_now)
    ORDER BY [j].[DueAt], [j].[Id])
UPDATE [due]
SET [Status] = 'Running', [LeaseToken] = @tb{b}_p2, [LeaseExpiresAt] = DATEADD(second, @tb{b}_p3, @tb{b}_now),
    [LeaseOwner] = @tb{b}_p4, [Attempts] = [Attempts] + 1, [StartedAt] = COALESCE([StartedAt], @tb{b}_now)
OUTPUT [inserted].[Id] INTO @tb{b}_claimed;
SELECT [j].* FROM [core].[Jobs] AS [j] INNER JOIN @tb{b}_claimed AS [c] ON [c].[Id] = [j].[Id] ORDER BY [j].[DueAt], [j].[Id];
-- entity-backed handlers: the platform appends Query<TEntity> with Restrictions = [KeySetRestriction("JobId", "@tb{b}_claimed")]
```

- The first statement collects the rows an earlier attempt of this round trip leased under the
  same token with a live lease, so a re-run returns them and leaves no row leased and untracked
  (§6.8); it seeks `IX_Jobs_Lease` and reads nothing on a first attempt.
- Rows the claim returns go to the handler except three kinds, which the worker completes without
  invoking it (§5.4), the first that applies deciding: a row whose `CancelRequestedAt` is set is
  `Cancelled` (`Jobs.Cancelled`); a row whose `Attempts` exceeds `MaxAttempts` — a crash left it
  `Running` past its last allowed attempt — is `Failed` with `Jobs.AttemptsExhausted`, keeping a
  code an earlier completion recorded (with its group, §2.1); an entity-backed row whose join load
  returns no entity is `Cancelled` (`Jobs.EntityMissing`), so `JobItem.Item` is never null.
- Rows claimed with valid leases (`Running`, in flight elsewhere) sort before newer pending rows in
  the index and are walked as residual rows by every claim of that key; the cost is bounded by
  `MaxConcurrency × BatchSize` per instance.
- The token `@tb{b}_p2` is a fresh `Guid` per claim statement; `@tb{b}_p4` is
  `<instance id>/<process id>` (instance id = `WEBSITE_INSTANCE_ID` when set, else the machine
  name), truncated to 128 characters.

### 5.2 Renew

One statement per tenant per instance, executed on an interval equal to the shortest
`LeaseSeconds / 5` among the tenant's in-flight batches on this instance (a longer lease renewed
more often is harmless), carrying the progress rows buffered since the last renewal:

```sql
-- RENEW (one per tenant per interval; @tb{b}_t0 : JobLeaseList, @tb{b}_t1 : JobProgressList)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
UPDATE [j]
SET [LeaseExpiresAt] = DATEADD(second, [t].[LeaseSeconds], @tb{b}_now),
    [ProgressPercent] = COALESCE([p].[ProgressPercent], [j].[ProgressPercent]),
    [ProgressKey] = COALESCE([p].[ProgressKey], [j].[ProgressKey]),
    [ProgressArgumentsJson] = CASE WHEN [p].[ProgressKey] IS NULL THEN [j].[ProgressArgumentsJson] ELSE [p].[ProgressArgumentsJson] END,
    [StateJson] = COALESCE([p].[StateJson], [j].[StateJson])
OUTPUT [inserted].[Id], [inserted].[LeaseToken], [inserted].[CancelRequestedAt]
FROM [core].[Jobs] AS [j]
INNER JOIN @tb{b}_t0 AS [t] ON [t].[Id] = [j].[LeaseToken]
LEFT JOIN @tb{b}_t1 AS [p] ON [p].[Id] = [j].[Id]
WHERE [j].[Status] = 'Running';
```

- A tracked job absent from the output of a renewal that returned has lost its lease (it lapsed,
  and another claim, the gap hold or the retired-key cancel took the row): the worker cancels every
  item token of its batch with reason `LeaseLost`, counts `tellma.jobs.lease_lost{handler}`, and
  discards the batch's eventual outcomes. A renewal round trip that fails carries no verdict: the
  leases wait for the next interval, four intervals before any lapses.
- A row whose `CancelRequestedAt` is not null cancels that item's token with reason
  `CancelRequested`.
- Progress rows that were carried are cleared from the buffer; `job.changed { jobId }` is published
  through `IClientEventPublisher.PublishAsync` for each carried row whose `RequestedById` is set.
- A handler that ignores cancellation is abandoned after one further lease length: its task keeps
  running with a logged warning, renewals for it stop, and its rows become re-claimable when the
  lease lapses. Shutdown abandons running handlers under the final renewal of §6.7.

### 5.3 Append

```sql
-- APPEND (IJobProgress.Append inside the caller's transaction; fenced by the token; @tb{b}_p0 token, @tb{b}_t0 : JobProgressList)
UPDATE [j] SET [ProgressPercent] = COALESCE([p].[ProgressPercent], [j].[ProgressPercent]),
    [ProgressKey] = COALESCE([p].[ProgressKey], [j].[ProgressKey]),
    [ProgressArgumentsJson] = CASE WHEN [p].[ProgressKey] IS NULL THEN [j].[ProgressArgumentsJson] ELSE [p].[ProgressArgumentsJson] END,
    [StateJson] = COALESCE([p].[StateJson], [j].[StateJson])
FROM [core].[Jobs] AS [j] INNER JOIN @tb{b}_t0 AS [p] ON [p].[Id] = [j].[Id]
WHERE [j].[LeaseToken] = @tb{b}_p0 AND [j].[Status] = 'Running';
IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @tb{b}_t0) THROW 50422, N'Job.LeaseLost', 1;
```

The executor maps `50422` to `BatchAssertionFailedException`; the worker treats the code
`Job.LeaseLost`, and no other, as a lost lease: it cancels every item token of the batch with reason
`LeaseLost`, and the caller's chunk — a partially applied import chunk — rolls back with it under
`XACT_ABORT`.

### 5.4 Complete

One completion per handler invocation (per partition), in one `Persist` batch in the run-as scope
(§8): the connect prologue of spec 0013 (`ConnectPremises.ForUser` or `ForSystem`) heads the batch;
inside the transaction, after the platform's guard statements, the completion statement is the first
statement, then the handler's appended statements, then the worker's notifications (§10), then the
epilogue. Rows completed without invoking a handler — the three kinds of §5.1 and the items of a
partition whose run-as user is inactive (§8) — are completed in the system tenant scope
(`ConnectAsSystem()`) by a batch carrying the completion statement and the worker's notifications
for them (§10) only.

```sql
-- COMPLETE (one per batch, one READ COMMITTED transaction; the FIRST statement after the platform's guards, before the handler's statements
-- and the worker's notifications, so that nothing the handler wrote survives a lost lease; @tb{b}_p0 token, @tb{b}_t0 : JobOutcomeList)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
UPDATE [j]
SET [Status] = [o].[Status],
    [DueAt] = CASE WHEN [o].[RetryAfterSeconds] IS NULL THEN [j].[DueAt] ELSE DATEADD(second, [o].[RetryAfterSeconds], @tb{b}_now) END,
    [Attempts] = CASE WHEN [o].[Released] = 1 THEN [j].[Attempts] - 1 ELSE [j].[Attempts] END,
    [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL,
    [CompletedAt] = CASE WHEN [o].[Status] IN ('Succeeded', 'Failed', 'Cancelled') THEN @tb{b}_now ELSE NULL END,
    [ErrorCode] = [o].[ErrorCode], [ErrorArgumentsJson] = [o].[ErrorArgumentsJson], [ErrorTraceId] = [o].[ErrorTraceId],
    [StateJson] = COALESCE([o].[StateJson], [j].[StateJson]),
    [ProgressPercent] = CASE WHEN [o].[Status] = 'Succeeded' THEN 100 ELSE [j].[ProgressPercent] END
OUTPUT [inserted].[Id], [inserted].[Status], [inserted].[RequestedById], [inserted].[Attempts]
FROM [core].[Jobs] AS [j] INNER JOIN @tb{b}_t0 AS [o] ON [o].[Id] = [j].[Id]
WHERE [j].[LeaseToken] = @tb{b}_p0 AND [j].[Status] = 'Running';
IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @tb{b}_t0) THROW 50422, N'Job.LeaseLost', 1;
```

Outcome to row mapping:

| Outcome | `Status` | `Released` | `RetryAfterSeconds` | Error columns | `StateJson` |
|---|---|---|---|---|---|
| `Succeeded` | `Succeeded` | 0 | null | null | last checkpoint |
| `Retry` | `Pending` | 0 | backoff or the supplied delay | the error, when supplied | last checkpoint |
| `Released` | `Pending` | 1 | null (`DueAt` unchanged) | null | null |
| `Failed` | `Failed` | 0 | null | required | last checkpoint |
| `Cancelled` | `Cancelled` | 0 | null | `Jobs.Cancelled` / `Jobs.EntityMissing` | last checkpoint |

The error columns are the group of §2.1: the code, its arguments and the trace id (§3.4). The
worker appends the completion statement to the batch before handing the batch to the handler,
binding a `JobOutcomeList` it fills after `Execute` returns; TVP rows are bound when the round trip
executes. The `OUTPUT` rows drive the instruments (§13) and the `job.changed` events.

### 5.5 Attempts, backoff, poison

- `Attempts` counts claims: the claim increments it, a `Released` completion decrements it. This
  is the only counter that catches a handler crashing the process before reporting anything.
- Backoff for `Retry` without a supplied delay: `RetryAfterSeconds = U(0, min(RetryMaxSeconds,
  RetryBaseSeconds × 2^(Attempts − 1)))` — full jitter, applied to the database clock.
- Poison: a row whose `Attempts` reaches `MaxAttempts` is `Failed` at its completion (§3.4); one a
  crash left `Running` past its last attempt is failed by its next claim without the handler (§5.1),
  at `MaxAttempts + 1`; either stays `Failed` until retention or an administrator's `retry`, which
  resets `Attempts` to 0. The claim's counters (`tellma.jobs.claimed`, `batch_fill`) count such
  rows.

### 5.6 The completion transaction

Contents, in order, all in one round trip: the prologue; `BEGIN TRAN` and the platform's guards;
the completion statement; the handler's statements (which must declare their writes and write
unowned tables only, §3.3; distribution guards throw in the `50600–50699` band and surface as the
batch's failure) and the groups of every write the handler enlisted with the job frame (§8; spec
0014 §13.3), each group the target stack's emitter, `ContributeAsync` and effects, appended where
the handler awaited `PersistAsync`; one spec 0020 `INotifier.Notify` per notification the worker
raises (§10) and per `NotifyOnSuccess` request of an item that succeeded; the epilogue; `COMMIT`.
Post-commit: the post-commit of every enlisted target (spec 0014 §13.2), `job.changed` for every
completed row with a `RequestedById`, and the nudge when any outcome is `Retry` with a zero delay.

Failure handling:

- A `Job.LeaseLost` fence maps to `LeaseLost`: nothing to do; another instance owns the rows. Any
  other `50422` code — spec 0016's `Blob.NotAttachable` from an export's blob effect, for example —
  is a final failure (the last case below), never `LeaseLost`.
- A reported transient failure re-runs the whole round trip through the executor's retry (the
  completion is a single transaction, so re-running is safe even though it is not marked
  `Idempotent`).
- An ambiguous failure (connection lost during `COMMIT`) is treated as not committed — the lease is
  left to lapse and the rows are re-claimed, which is why handlers append idempotent statements or
  checkpoint (§3.6).
- A guard failure — `GuardPassed = false` at the prologue, or `50412` at spec 0013 §7.4's
  in-transaction re-check (`StaleContextException`) — is answered by applying the connect result
  and executing a fresh completion batch that carries only the completion statement with every item
  `Retry` (`RetryAfterSeconds = 0`); the handler's statements and enlisted groups are discarded and
  the work re-runs under the new permissions. A `50401` re-check (`TenantNotFoundException`: the
  run-as user was deactivated during the run) takes the inactive-user path of §8.
- Every other failure is final for the attempt: a constraint violation, invariant or distribution
  guard in a statement the handler appended or in an enlisted group, which the worker translates to
  a `ValidationException` by spec 0014 §14.2's rows (an enlisted group's error attributed to its
  group by `StatementOrdinal`, spec 0014 §13.3), a `DependencyUnavailableException`, anything else.
  The worker logs it (`JobCompletionFailed` at `Error`, job ids in scope), discards the handler's
  statements and enlisted groups, and executes a fresh completion batch carrying only the completion
  statement and the worker's notifications (§10), every item mapped by §3.4's rules for an exception
  from `Execute`: a `ValidationException` fails every item at once with its first error's code and
  arguments; anything else is `Retry` with `Jobs.Internal`, under the backoff of §5.5. Should that
  batch fail too, the lease is left to lapse.

## 6. The worker

### 6.1 Hosting

`JobWorker` is one `BackgroundService` per host process, registered by `CoreFeature` when
`JobsOptions.Enabled` is true, implementing `IHostedLifecycleService` so that `StoppingAsync`
stops claiming before the web host finishes draining. `ExecuteAsync` never lets an exception
escape (the host's default `BackgroundServiceExceptionBehavior` would stop the process): every
poller iteration catches, logs at `Error` with the tenant id in the log scope, and continues after
`MaxPollInterval`. Time comes from the injected `TimeProvider`; tests drive it with a fake.

### 6.2 Tenant pollers

The worker holds one `TenantPoller` per tenant whose `TenantDescriptor.State` is `Active` in spec
0010's `ITenantRegistry.Tenants`, created and retired on the registry's refresh cadence and on
`ITenantStateListener.OnStateChangedAsync` (the worker implements the listener): a tenant leaving
`Active` stops its poller after the current poll; in-flight batches finish and complete (the
completion scope is created before the state change is observed; a scope creation refused with
`TenantUnavailableException` leaves the lease to lapse). Tenants in `Provisioning`, `ReadOnly`,
`Suspended` and `Retired` are never polled. Sandbox tenants are polled like live ones; the job
scope's `ISandboxContext` reports the category.

Each poller owns a coalescing wake-up channel (`Channel.CreateBounded<bool>` with capacity 1 and
`FullMode = DropWrite`) and a `PeriodicTimer(interval, TimeProvider)`, and waits on whichever fires
first.

### 6.3 The poll round trip

One `Maintenance` batch in a system tenant scope (`ITenantScopeFactory.CreateScopeAsync` with
`Kind = System`, `UserId = WellKnownIds.SystemUserId`), `TransactionMode = None` and `Idempotent`
(§1.4, §6.8), containing in order:

1. The gap check and hold (§6.10).
2. One claim (§5.1) per registered key with free slots for this tenant on this instance, each
   followed by the entity join load for entity-backed handlers.
3. Tick step 1 (§7.4), when `JobsOptions.Enabled` and the instance is not stopping.
4. Once per minute per tenant, the backlog sample: for each registered key, the `DueAt` of its
   oldest `Pending` row (`SELECT TOP (1) [DueAt] … ORDER BY [DueAt]`) — a seek per key on
   `IX_Jobs_Available`, never a `MIN` across keys.
5. Hourly per tenant, the orphan sample and, when this instance knows retired keys, the retired-key
   cancel (§6.9).
6. The heartbeat stamp (§6.10), last.

Result sets are read in order with `NextResult()`; the poll duration is recorded on
`tellma.jobs.poll.duration`.

### 6.4 Adaptive interval and nudges

After a poll that claimed a full batch for any key, or whose tick step 1 returned its full 50 rows,
poll again immediately; after a poll that found something short of that, reset the interval to
`MinPollInterval` (1 s); after a poll that found nothing, double it up to `MaxPollInterval` (30 s);
a nudge (§4.4) resets it to the minimum and wakes the channel. Five hundred idle tenants cost an
instance about seventeen polls per second, each a handful of empty seeks.

### 6.5 Concurrency

Claimed batches run on the thread pool under a per-instance `SemaphoreSlim(MaxParallelBatches)`
and a per-(tenant, key) counter enforcing `[JobHandler].MaxConcurrency`. A key with no free slot is
omitted from the poll's claims. `tellma.jobs.in_flight{handler}` tracks the count.

### 6.6 Cancellation

Three tokens: the host token (stopping); the batch token (linked to the host token; cancelled with
`LeaseLost`, `HostStopping`, or when every item's token is cancelled; the operation's token); and
each item's token (linked to the batch token; cancelled alone with `CancelRequested` when a renewal
reports the row's `CancelRequestedAt`, §5.2). The handler observes the reasons on its items
(`JobItem.CancellationReason`, §3.3).

### 6.7 Shutdown and drain

`StoppingAsync`: stop polling; cancel every batch token with reason `HostStopping` (each item's
reason follows, §3.3); wait up to `DrainTimeout` (§12.1) for handlers to return. Batches whose
handler returned or never started are completed in one completion batch per partition with the
outcomes of §3.4 (`Released` for untouched items). Batches whose handler is still running keep
their lease, which must outlive the process: before `StoppingAsync` returns, one final renewal per
tenant carries their rows with `LeaseSeconds` bound to the host's `HostOptions.ShutdownTimeout`
(the renew statement's per-row `JobLeaseList.LeaseSeconds`, §5.2); renewals then stop and the
handlers are abandoned. One that returns before the process exits is completed under that lease (a
scope refusal leaves the lease to lapse); the rest are re-claimed once the lease lapses, after the
process is gone. Releasing a running handler's row, or letting its lease lapse while the process
lives, would run the job twice concurrently, and the fences protect database writes only (§3.6).
The final renewal also shortens a longer lease to `ShutdownTimeout`, so another instance picks the
row up sooner after a deploy.

### 6.8 Re-running the poll

The poll round trip is `Idempotent` (§1.4), so the executor re-runs it after a reported transient
failure and after an ambiguous one, binding the same tokens. Every statement tolerates the re-run:
a claim and tick step 1 first collect the rows an earlier attempt leased under their token and
return them with the rows they lease now, within the same `BatchSize` or 50 (§5.1, §7.4); the hold
changes only rows still `Pending` or `Running`, and the held counts are read from the table
(§6.10); the stamp is the last statement, so a re-run of a round trip that failed before it sees
the gap again; the samples only read, and the retired-key cancel changes only rows not yet
cancelled (§6.9). No row is ever leased and untracked, so `tellma.jobs.lease_lost` counts slow
handlers only. The one residual: an ambiguous failure after the stamp committed loses that round
trip's gap notifications; the held rows stay on the Background jobs page (§11.3). The renewal is
`Idempotent` as well (it joins by token), and a renewal round trip that fails outright carries no
verdict (§5.2).

### 6.9 Orphaned and retired keys

Hourly, per tenant, the poll carries the orphan sample and, when this instance knows retired keys
(§3.7), the retired-key cancel:

```sql
-- ORPHAN SAMPLE (hourly, in the poll; @tb{b}_t0 : StringList of the keys this instance registers or retires)
SELECT COUNT(*) AS [Orphaned]
FROM [core].[Jobs] AS [j]
WHERE [j].[Status] = 'Pending' AND [j].[DueAt] < DATEADD(hour, -1, SYSUTCDATETIME())
  AND NOT EXISTS (SELECT 1 FROM @tb{b}_t0 AS [k] WHERE [k].[Id] = [j].[HandlerKey]);
```

```sql
-- RETIRED-KEY CANCEL (hourly, in the poll, when this instance knows retired keys; @tb{b}_t0 : StringList of the retired keys)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
UPDATE [j]
SET [Status] = 'Cancelled', [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL, [CompletedAt] = @tb{b}_now,
    [ErrorCode] = N'Jobs.HandlerRetired', [ErrorArgumentsJson] = NULL, [ErrorTraceId] = NULL
FROM [core].[Jobs] AS [j] WITH (READPAST, UPDLOCK, ROWLOCK)
INNER JOIN @tb{b}_t0 AS [k] ON [k].[Id] = [j].[HandlerKey]
WHERE [j].[Status] IN ('Pending', 'Held', 'Running')
  AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < @tb{b}_now);
```

The worker keeps the latest orphan sample per tenant (dropped when the poller retires, §6.2), and
the observable gauge `tellma.jobs.orphaned` reports their sum (§13); each instance reports its own
registry's view, so a rolling deploy shows a transient value on one instance, and dashboards read
it per instance, never summed across instances. Rows of an unregistered key that no feature
retires are never cancelled automatically; the Background jobs page shows them (§11.3). A retired
key's rows end `Cancelled` with `Jobs.HandlerRetired` and raise no notification; a running row of
the key is cancelled once its lease lapses.

### 6.10 Heartbeat and the gap hold

```sql
-- GAP CHECK AND HOLD (first block of every poll; @tb{b}_p0 gapMinutes)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
DECLARE @tb{b}_last datetimeoffset(3) = (SELECT [LastTickAt] FROM [core].[JobWorkerState] WHERE [Id] = 1);
DECLARE @tb{b}_gap bit = CASE WHEN @tb{b}_last < DATEADD(minute, -@tb{b}_p0, @tb{b}_now) THEN 1 ELSE 0 END;
IF @tb{b}_gap = 1
    UPDATE [j]
    SET [Status] = 'Held', [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL,
        [ErrorCode] = N'Jobs.HeldAfterGap', [ErrorArgumentsJson] = NULL, [ErrorTraceId] = NULL
    FROM [core].[Jobs] AS [j]
    WHERE [j].[Status] IN ('Pending', 'Running')
      AND [j].[DueAt] < DATEADD(minute, -@tb{b}_p0, @tb{b}_now)
      AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < @tb{b}_now);
SELECT @tb{b}_last AS [PreviousTickAt], @tb{b}_now AS [Now];
SELECT [h].[RequestedById], COUNT(*) AS [HeldCount]   -- empty unless a gap was detected; a seek on IX_Jobs_Held
FROM [core].[Jobs] AS [h]
WHERE @tb{b}_gap = 1 AND [h].[Status] = 'Held'
GROUP BY [h].[RequestedById];
```

```sql
-- HEARTBEAT STAMP (last statement of every poll; @tb{b}_p0 owner)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
UPDATE [core].[JobWorkerState] SET [LastTickAt] = @tb{b}_now, [LastTickOwner] = @tb{b}_p0
WHERE [Id] = 1 AND ([LastTickAt] IS NULL OR [LastTickAt] < DATEADD(second, -60, @tb{b}_now));
```

- `core.JobWorkerState` is a single-row table: `Id int PK` with
  `CK_JobWorkerState_SingleRow (Id = 1)` beside the positive-key check of spec 0011's
  convention, `LastTickAt datetimeoffset(3) NULL`, `LastTickOwner nvarchar(128) NULL`; seeded
  `(1, NULL, NULL)` by `HasData`, so a new tenant's first tick sees no gap.
- `@tb{b}_p0` of the gap check is `JobsOptions.GapThreshold` in minutes (default 360); `@tb{b}_p0`
  of the stamp is the poll's lease owner, the string of §5.1's `@tb{b}_p4`. When `PreviousTickAt`
  is older than the threshold the worker logs `SchedulerGapDetected` at `Warning`, counts
  `tellma.schedules.gap_detected`, and, in `Persist` batches under the system user after the poll
  (as many as spec 0011's parameter cap requires), raises `core.scheduler.gap` to administrators
  with `DedupKey = 'core.scheduler.gap'` and `heldCount` = the sum of the block's per-requester
  counts, and one `core.job.held` to each non-null `RequestedById` of those counts with its `count`
  and `DedupKey = 'core.job.held'`; rows with no requester are reported by the gap notice alone.
  The counts cover every `Held` row, not only this statement's, so a re-run (§6.8) or a second
  instance that found the rows already held still reports them; both notices rely on spec 0020's
  default `OnDuplicate = Suppress`, so a repeat while the first is unread adds nothing.
- Held rows are work that was already more than `GapThreshold` overdue when the worker came back:
  the thousands of rows a restored backup brings, or work queued before a long outage. They wait
  for an administrator's `resume` or a `cancel` (a member may cancel their own held rows; only an
  administrator resumes them, §11.2); rows due within the threshold and everything enqueued after
  the restart proceed normally. Schedules coalesce automatically on a gap (§7.5).
- The heartbeat stamp is the poll's last statement (§6.8), throttled to once per minute per tenant
  (a seek and usually no write).

## 7. The scheduler

### 7.1 The `Schedule` entity

```csharp
// Tellma.Core.Abstractions.Jobs
public enum MissedPolicy { Coalesce, ReplayAll, Skip }

public enum OverlapPolicy { Skip, Allow }

public enum SchedulePausedReason { OwnerInactive, Exhausted, HandlerRetired }

[Temporal]
public class Schedule : TopLevelEntity, IActivatable    // stack resource core.Schedule
{
    [Unique] public string? Code { get; set; }                // equals HandlerKey on built-ins
    [Multilingual] public string Name { get; set; }
    public string? Name2 { get; set; }
    public string? Name3 { get; set; }
    [WriteOnce] public string HandlerKey { get; set; }
    [JsonColumn] public string? ArgumentsJson { get; set; }
    public string CronExpression { get; set; }                // five-field Unix cron, Cronos dialect
    public string? TimeZoneId { get; set; }                   // null = tenant zone at fire time
    public MissedPolicy MissedPolicy { get; set; } = MissedPolicy.Coalesce;
    public OverlapPolicy OverlapPolicy { get; set; } = OverlapPolicy.Skip;
    public int CatchUpWindowMinutes { get; set; } = 1440;
    [ServerOwned] public int RunAsUserId { get; set; }        // the creator, re-stamped only by the take-over action; the system user on built-ins
    [ServerOwned] public SchedulePausedReason? PausedReason { get; set; }
    public bool IsActive { get; set; } = true;
}
```

`Schedule` is an ordinary editable top-level entity: `[Stack]` with the full operation set,
`[Searchable]` on `Name` and `Code`, `[DefaultSelect]` of every column but `ArgumentsJson`, which
`[JsonColumn]` keeps out of the Queryex schema (spec 0011 §2.6); the details read carries it (spec
0014 §5.3). Activate and deactivate come from the activatable recipe of spec 0014; `IsActive` is
server-owned and changes only through the actions.

A schedule is built-in exactly when its id lies in the reserved band (spec 0013 §2; spec 0011
§4.1), `Id <= WellKnownIds.ReservedIdBandEnd`: the `HasData` rows of §7.9 and nothing else. Every
built-in this spec names is one of these, and the Schedules page derives its badge and locked
fields from the id. `ReplayAll` requires `OverlapPolicy.Allow` (§7.8): replayed occurrences are
independent jobs that may run concurrently, so an overlap guard over them is contradictory.

**`core.Schedules`** — `[Temporal]` (`core.SchedulesHistory`), UDTT, sequence `core.sq_Schedules`:

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered; `CK_Schedules_Id CHECK ([Id] > 0)` | built-ins in the reserved band (§7.9) |
| `Code` | `nvarchar(64)` | yes | `UX_Schedules_Code WHERE Code IS NOT NULL` | |
| `Name` | `nvarchar(255)` | no | | |
| `Name2`, `Name3` | `nvarchar(255)` | yes | | gated by tenant languages |
| `HandlerKey` | `nvarchar(64)` | no | `IX_Schedules_HandlerKey` | |
| `ArgumentsJson` | `nvarchar(max)` | yes | | ≤ 64 KB |
| `CronExpression` | `nvarchar(128)` | no | | validated on save |
| `TimeZoneId` | `varchar(64)` | yes | | IANA or Windows id |
| `MissedPolicy` | `varchar(9)` | no | `DF_Schedules_MissedPolicy 'Coalesce'` | |
| `OverlapPolicy` | `varchar(8)` | no | `DF_Schedules_OverlapPolicy 'Skip'` | |
| `CatchUpWindowMinutes` | `int` | no | `DF 1440`, `CK_Schedules_CatchUp (CatchUpWindowMinutes > 0)` | |
| `RunAsUserId` | `int` | no | `FK_Schedules_RunAsUserId → core.Users` | |
| `PausedReason` | `varchar(14)` | yes | | |
| `IsActive` | `bit` | no | `DF 1` | |
| audit set | | no | FKs `core.Users` | `ModifiedAt` is the concurrency token |
| period columns | `datetime2(7)` | no | | shadow |

### 7.2 `core.ScheduleStates`

Non-temporal sibling, one row per schedule, inserted with it and cascade-deleted with it, so ticks
write no history rows and never touch `ModifiedAt`:

| Column | Type | Null | Constraints |
|---|---|---|---|
| `ScheduleId` | `int` | no | PK clustered; `FK_ScheduleStates_ScheduleId → core.Schedules ON DELETE CASCADE` |
| `NextDueAt` | `datetimeoffset(3)` | yes | null = nothing scheduled: on an inactive or paused schedule (§7.7, §7.8), and on a built-in until the first tick initialises it (§7.4, §7.9); `IX_ScheduleStates_Due (NextDueAt) WHERE NextDueAt IS NOT NULL` |
| `LastFiredAt`, `LastScheduledFor`, `LastSkippedAt` | `datetimeoffset(3)` | yes | |
| `LastJobId` | `int` | yes | `FK_ScheduleStates_LastJobId → core.Jobs ON DELETE SET NULL` |
| `LeaseToken` | `uniqueidentifier` | yes | tick lease; `IX_ScheduleStates_Lease (LeaseToken) WHERE LeaseToken IS NOT NULL`, the seek of tick step 1's first statement (§7.4) |
| `LeaseExpiresAt` | `datetimeoffset(3)` | yes | |

### 7.3 Cron dialect and time zones

- Expressions are five-field Unix cron parsed by Cronos 0.13.0 with `CronFormat.Standard`:
  minute, hour, day of month, month, day of week; `*`, ranges, lists, steps, `L`, `W`, `#`, `?`
  and the `@daily`-style macros; no seconds field (the poll ceiling is 30 s).
- `TimeZoneId` resolves with `TimeZoneInfo.FindSystemTimeZoneById` (IANA and Windows ids both
  resolve on Windows and Linux under .NET's ICU conversion); null means the tenant's zone,
  `TenantSettings.TimeZone` of spec 0012, read at fire time so a settings change takes effect at the
  next tick.
- DST follows Cronos: a skipped local time fires at the next valid instant; a repeated local time
  fires once.

### 7.4 Tick step 1

Rides the poll (§6.3): lease up to 50 state rows, uninitialised built-ins first and then due
schedules, for 30 seconds, and return what the C# step needs.

```sql
-- SCHEDULE TICK, step 1 (in the poll; @tb{b}_p0 tick token)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
DECLARE @tb{b}_due TABLE ([ScheduleId] int NOT NULL PRIMARY KEY);
-- rows an earlier attempt of this round trip leased under the same token (§6.8)
INSERT INTO @tb{b}_due ([ScheduleId])
SELECT [s].[ScheduleId] FROM [core].[ScheduleStates] AS [s]
WHERE [s].[LeaseToken] = @tb{b}_p0 AND [s].[LeaseExpiresAt] >= @tb{b}_now;
DECLARE @tb{b}_room int = 50 - (SELECT COUNT(*) FROM @tb{b}_due);
-- seeded built-in rows not yet initialised (§7.9): a range seek on the clustered key over the reserved band, active schedules only
UPDATE TOP (@tb{b}_room) [s] SET [LeaseToken] = @tb{b}_p0, [LeaseExpiresAt] = DATEADD(second, 30, @tb{b}_now)
OUTPUT [inserted].[ScheduleId] INTO @tb{b}_due
FROM [core].[ScheduleStates] AS [s] WITH (READPAST, UPDLOCK, ROWLOCK)
WHERE [s].[ScheduleId] <= 999 AND [s].[NextDueAt] IS NULL
  AND ([s].[LeaseExpiresAt] IS NULL OR [s].[LeaseExpiresAt] < @tb{b}_now)
  AND EXISTS (SELECT 1 FROM [core].[Schedules] AS [x] WHERE [x].[Id] = [s].[ScheduleId] AND [x].[IsActive] = 1);
SET @tb{b}_room = 50 - (SELECT COUNT(*) FROM @tb{b}_due);
-- due rows: a seek on IX_ScheduleStates_Due
WITH [d] AS (
    SELECT TOP (@tb{b}_room) [s].*
    FROM [core].[ScheduleStates] AS [s] WITH (READPAST, UPDLOCK, ROWLOCK)
    WHERE [s].[NextDueAt] IS NOT NULL AND [s].[NextDueAt] <= @tb{b}_now
      AND ([s].[LeaseExpiresAt] IS NULL OR [s].[LeaseExpiresAt] < @tb{b}_now)
    ORDER BY [s].[NextDueAt])
UPDATE [d] SET [LeaseToken] = @tb{b}_p0, [LeaseExpiresAt] = DATEADD(second, 30, @tb{b}_now)
OUTPUT [inserted].[ScheduleId] INTO @tb{b}_due;
SELECT [s].[Id], [s].[HandlerKey], [s].[ArgumentsJson], [s].[CronExpression], [s].[TimeZoneId], [s].[MissedPolicy],
       [s].[OverlapPolicy], [s].[CatchUpWindowMinutes], [s].[RunAsUserId],
       [st].[NextDueAt], [st].[LastScheduledFor], [u].[IsActive] AS [OwnerIsActive], @tb{b}_now AS [Now]
FROM @tb{b}_due AS [d]
INNER JOIN [core].[Schedules] AS [s] ON [s].[Id] = [d].[ScheduleId]
INNER JOIN [core].[ScheduleStates] AS [st] ON [st].[ScheduleId] = [d].[ScheduleId]
INNER JOIN [core].[Users] AS [u] ON [u].[Id] = [s].[RunAsUserId];
```

The uninitialised branch touches only the reserved band's rows through the clustered key and the
active check; the due branch seeks `IX_ScheduleStates_Due`; neither ever leases an inactive or
paused schedule. Built-in ids stay inside the band (§7.9); the literal `999` is its end,
`WellKnownIds.ReservedIdBandEnd`.

### 7.5 Occurrences and the missed policy

Per returned schedule, in C#, with `Now` and `NextDueAt` read from the database as `DateTimeOffset`
and `zone` resolved per §7.3; the Cronos calls are `CronExpression`'s `DateTimeOffset` overloads
(`GetNextOccurrence(DateTimeOffset, TimeZoneInfo)`), taking and returning `DateTimeOffset`:

1. When `OwnerIsActive = 0` the schedule pauses (`OwnerInactive`), and when its key is retired
   (§3.7) it pauses (`HandlerRetired`), per §7.7; nothing fires; go to step 6.
2. When `NextDueAt` is null the row is an uninitialised built-in: nothing fires; go to step 5.
3. When `Now − NextDueAt > GapThreshold` every policy degrades to `Coalesce` for this tick: one
   job fires for `CronExpression.GetPreviousOccurrence(Now, zone, inclusive: true)`, the latest
   occurrence at or before `Now`; the occurrences in between are never enumerated (a dense
   expression over a long gap would materialise hundreds of thousands), and the gap itself is
   counted by `tellma.schedules.gap_detected` (§6.10).
4. Otherwise `GetOccurrences(NextDueAt, Now, zone, fromInclusive: true, toInclusive: true)` yields
   the due occurrences, at most one per minute of `GapThreshold`: `Coalesce` fires one job with
   `ScheduledFor` = the latest occurrence; `ReplayAll` (always with `Allow`, §7.1) fires one job
   per occurrence not older than `CatchUpWindowMinutes` before `Now` (older ones count as missed);
   `Skip` fires one job only when the latest occurrence is within `2 × MaxPollInterval` of `Now`
   and drops the rest (counted). One occurrence fires one job. Dropped and coalesced occurrences
   count on `tellma.schedules.missed{policy}`.
5. `next = GetNextOccurrence(Now, zone)` (strictly after `Now`, so a window is never replayed
   twice); null means exhausted (§7.7).
6. The step-2 rows: one `core.Jobs` UDTT row per fired job (`Id` left 0 — assigned in the
   statement; `HandlerKey`, `DueAt = Now`, `ArgumentsJson`, `RunAsUserId` = the schedule's,
   `RequestedById` = null for a built-in (§7.1), else the owner, `ScheduleId`, `ScheduledFor`) and
   one `ScheduleNextList` row (`NextDueAt = next`, null when the schedule pauses;
   `LastScheduledFor` = the latest fired occurrence or null).

A schedule whose `HandlerKey` no handler on this instance registers and no feature retires fires
normally (the rows are inert until an instance with the handler claims them; a rolling deploy makes
this transient) and logs `SchedulerHandlerUnknown` at `Warning`; no error code is written — the
orphan sample of §6.9 reports the rows.

### 7.6 Tick step 2

A second round trip only when step 1 returned rows: one `Persist` batch in the system tenant scope
(`System` prologue variant), `TransactionMode = Auto`.

```sql
-- SCHEDULE TICK, step 2 (second round trip, one transaction, ResultSets = 1; @tb{b}_t0 : Jobs UDTT with ids reserved in this statement, @tb{b}_t1 : ScheduleNextList, @tb{b}_p0 tick token)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
DECLARE @tb{b}_fired TABLE ([ScheduleId] int NOT NULL, [JobId] int NOT NULL);
-- tick-lease fence: every schedule in this step must still be leased by this ticker, else the whole step rolls back
IF (SELECT COUNT(*) FROM [core].[ScheduleStates] AS [st] WITH (UPDLOCK, ROWLOCK) INNER JOIN @tb{b}_t1 AS [n] ON [n].[ScheduleId] = [st].[ScheduleId]
    WHERE [st].[LeaseToken] = @tb{b}_p0 AND [st].[LeaseExpiresAt] >= @tb{b}_now) <> (SELECT COUNT(*) FROM @tb{b}_t1)
    THROW 50422, N'Schedule.TickLeaseLost', 1;
DECLARE @tb{b}_first sql_variant, @tb{b}_n int = (SELECT COUNT(*) FROM @tb{b}_t0);
IF @tb{b}_n > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Jobs', @range_size = @tb{b}_n, @range_first_value = @tb{b}_first OUTPUT;
INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [RunAsUserId], [RequestedById], [ScheduleId], [ScheduledFor], [CreatedAt])
OUTPUT [inserted].[ScheduleId], [inserted].[Id] INTO @tb{b}_fired
SELECT CAST(@tb{b}_first AS int) + ROW_NUMBER() OVER (ORDER BY [r].[ScheduleId], [r].[ScheduledFor]) - 1,
       [r].[HandlerKey], 'Pending', [r].[DueAt], 0, [r].[ArgumentsJson], [r].[RunAsUserId], [r].[RequestedById], [r].[ScheduleId], [r].[ScheduledFor], @tb{b}_now
FROM @tb{b}_t0 AS [r]
INNER JOIN [core].[Schedules] AS [s] ON [s].[Id] = [r].[ScheduleId]
INNER JOIN [core].[ScheduleStates] AS [st] ON [st].[ScheduleId] = [r].[ScheduleId] AND [st].[LeaseToken] = @tb{b}_p0 AND [st].[LeaseExpiresAt] >= @tb{b}_now
WHERE [s].[OverlapPolicy] = 'Allow'
   OR NOT EXISTS (SELECT 1 FROM [core].[Jobs] AS [x] WHERE [x].[ScheduleId] = [r].[ScheduleId] AND [x].[Status] IN ('Pending', 'Running'));
UPDATE [st]
SET [NextDueAt] = [n].[NextDueAt],
    [LastScheduledFor] = COALESCE([n].[LastScheduledFor], [st].[LastScheduledFor]),
    [LastFiredAt] = CASE WHEN [f].[ScheduleId] IS NOT NULL THEN @tb{b}_now ELSE [st].[LastFiredAt] END,
    [LastJobId] = COALESCE([f].[JobId], [st].[LastJobId]),
    [LastSkippedAt] = CASE WHEN [f].[ScheduleId] IS NULL AND EXISTS (SELECT 1 FROM @tb{b}_t0 AS [r] WHERE [r].[ScheduleId] = [st].[ScheduleId]) THEN @tb{b}_now ELSE [st].[LastSkippedAt] END,
    [LeaseToken] = NULL, [LeaseExpiresAt] = NULL
FROM [core].[ScheduleStates] AS [st]
INNER JOIN @tb{b}_t1 AS [n] ON [n].[ScheduleId] = [st].[ScheduleId]
LEFT JOIN (SELECT [ScheduleId], MAX([JobId]) AS [JobId] FROM @tb{b}_fired GROUP BY [ScheduleId]) AS [f] ON [f].[ScheduleId] = [st].[ScheduleId]
WHERE [st].[LeaseToken] = @tb{b}_p0;
SELECT [ScheduleId], [JobId] FROM @tb{b}_fired ORDER BY [JobId];
```

- The overlap predicate is evaluated inside the transaction: under `Skip` (the default) a schedule
  with a `Pending` or `Running` job fires nothing and its `LastSkippedAt` is stamped, which marks
  such an overlap skip only (a schedule with no job row in `@tb{b}_t0` keeps its value); `Allow`
  always inserts.
- The pause of §7.7 and the notifications it raises ride the same transaction. The result set
  lists the fired jobs: from it the worker counts `tellma.schedules.fired{policy}` and
  `tellma.schedules.overlap_skipped`, and when any row fired the post-commit hook nudges the local
  poller so the fired jobs are claimed at once.
- If the round trip fails the 30-second tick lease lapses and the next poll on any instance
  recomputes from the unchanged `NextDueAt`: at-least-once, with the overlap predicate preventing
  duplicates for `Skip` schedules.

### 7.7 Pausing

A schedule pauses — `IsActive = 0`, `PausedReason` set — through one `IDataBatch.Update<Schedule>`
with `Stamp = true` (the pause is a visible state change), appended to tick step 2's batch; its
`ScheduleNextList` row carries `NextDueAt = NULL` (§7.5 step 6), so `core.ScheduleStates` takes no
second update:

| Cause | `PausedReason` | Notification |
|---|---|---|
| the run-as user is inactive at tick time | `OwnerInactive` | `core.schedule.paused` to administrators (`IAdministratorDirectory.GetAdministratorIdsAsync()`), target `core.Schedule`, arguments `{ scheduleId, name, reason }` |
| `GetNextOccurrence` returns null at tick time: the tick's terminal branch, unreachable for an expression that passed `Schedules.CronNeverOccurs` (§7.8) and the `jobs.schedules` check (§15) | `Exhausted` | the same, to the owner when it is a person, else administrators |
| the key is retired (§3.7) | `HandlerRetired` | as `Exhausted` |

Reactivation through `activate` clears `PausedReason` and recomputes `NextDueAt` from now; it is
refused while the cause stands (§7.8), and a paused period is never caught up. Built-ins never
pause: the system user is never inactive, and the gates refuse a shipped expression with no next
occurrence (`jobs.schedules`, §15) and a built-in naming a retired key (the realizer, §3.7).

### 7.8 `ScheduleService`

```csharp
// Tellma.Core.Jobs
public class ScheduleService : EntityService<Schedule>   // full stack; ContributeAsync recomputes NextDueAt in core.ScheduleStates
{
    public Task TakeOverAsync(ActionContext<Schedule, int> context);   // [EntityAction] "take-over", action Save: RunAsUserId := the caller; the explicit way to run another user's schedule as oneself
    public Task<JobAccepted> RunNowAsync(ActionContext<Schedule, int> context);   // [EntityAction] "run-now", action Save, SingleTarget: one job of the schedule, now
}
```

Preprocessing (`PreprocessAsync`): trims `Code`, `CronExpression`, `TimeZoneId`; on insert sets
`RunAsUserId = RequestContext.UserId` and `PausedReason = null` (`[ServerOwned]` columns are
overwritten by the emitter anyway; the hook sets the values the emitter will keep).

Validation (`ValidateAsync`, an `IEntityValidator<Schedule>` registered by `CoreFeature`), codes at
the property path:

| Code | Condition |
|---|---|
| `Schedules.CronInvalid` | Cronos fails to parse `CronExpression` |
| `Schedules.CronNeverOccurs` | `CronExpression` parses but has no next occurrence (`GetNextOccurrence(Now, zone)` is null: a five-field expression either never occurs or recurs indefinitely); on every save and on `activate` |
| `Schedules.TimeZoneUnknown` | `TimeZoneId` does not resolve |
| `Schedules.HandlerUnknown` | `HandlerKey` names no registered handler (a retired key included); on every save and on `activate` |
| `Schedules.HandlerNotSchedulable` | the handler is not `Schedulable` (built-ins exempt); only `core.export` is schedulable this release |
| `Schedules.ReplayAllRequiresAllow` | `MissedPolicy = ReplayAll` with `OverlapPolicy = Skip`, at `MissedPolicy` |
| `Schedules.ArgumentsTooLarge` | `ArgumentsJson` exceeds 64 KB |
| `Schedules.OwnedByAnotherUser` | an update changes `HandlerKey`, `ArgumentsJson`, `CronExpression` or `TimeZoneId` on a schedule whose `RunAsUserId` is not the caller; the caller takes it over first or leaves it — renaming, `IsActive` changes through the actions and the policies do not change the owner |
| `Schedules.OwnerInactive` | `activate` targets a schedule whose run-as user is inactive (the load `ValidateActionAsync` declares); the remedy is `take-over`, then `activate` |
| `Schedules.BuiltInImmutable` | an update to a built-in changes anything but `Name`, `Name2`, `Name3`, `CronExpression`, `TimeZoneId`; or a delete targets a built-in; or `take-over` targets a built-in |
| `Schedules.BuiltInAlwaysActive` | `deactivate` targets a built-in (`ValidateActionAsync`) |
| `Schedules.RunInProgress` | `run-now` on a `Skip` schedule with a `Pending` or `Running` job |

`ContributeAsync` (a save hook; the activatable recipe invokes it after `activate`/`deactivate` as
well, on an `ActionPersistContext<Schedule>` whose `Action` names which; the service overrides
`ValidateActionAsync` for both actions, so they take spec 0014 §11.2's general path and the context
carries the target rows): for every saved or activated row computes
`next = GetNextOccurrence(Now, zone)` in C# (`Now` = `RequestContext.Now`, the one place the
application clock enters — the next tick corrects it against the database clock) and appends, with a
`ScheduleNextList` TVP, an upsert of `core.ScheduleStates` (`INSERT` for new ids,
`UPDATE … SET NextDueAt` otherwise; `NextDueAt = NULL` for deactivated rows) and, on activation, an
`UPDATE [core].[Schedules] SET [PausedReason] = NULL` for the affected ids; declares
`Writes = { core.ScheduleStates, core.Schedules }`.

`TakeOver`: an `[EntityAction("take-over", Action = "Save")]` over ids; validation refuses
built-ins; the action is an `IDataBatch.Update<Schedule>` with `Stamp = true` that sets
`RunAsUserId` to the caller; a subsequent save by the caller then passes the ownership rule.
`take-over` changes the owner only; it neither clears `PausedReason` nor activates. The details
page shows the owner and offers the action when the caller is not the owner.

`RunNow`: RT1 loads the schedule under `Save`'s grant and, through a load the action declares
without a grant filter, whether a `Pending` or `Running` job carries its id (a seek on
`IX_Jobs_ScheduleActive`); under `OverlapPolicy = Skip` such a job refuses the action with
`Schedules.RunInProgress`. `IsActive` and `PausedReason` are no preconditions: a paused schedule
can be run once. RT2 appends the statement below (`Writes = { core.Jobs }`); the method awaits
`Persisted` and returns `JobAccepted(JobId, null)` (spec 0015 §3.3); the post-commit hook nudges
the local poller (§4.4). The job runs as the caller for a user schedule and as the system user for
a built-in, with the caller as `RequestedById` either way (§8); `ScheduledFor` stays null, and
`core.ScheduleStates` is untouched, so the cron's next due time and last fired job stay the cron's.

```sql
-- RUN-NOW (ScheduleService.RunNow; @tb{b}_p0 schedule id, @tb{b}_p1 run-as user id, @tb{b}_p2 requester id, @tb{b}_p3 traceparent)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
DECLARE @tb{b}_jobIds TABLE ([JobId] int NOT NULL);
INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [TraceParent], [RunAsUserId], [RequestedById], [ScheduleId], [ScheduledFor], [CreatedAt])
OUTPUT [inserted].[Id] INTO @tb{b}_jobIds
SELECT NEXT VALUE FOR [core].[sq_Jobs], [s].[HandlerKey], 'Pending', @tb{b}_now, 0, [s].[ArgumentsJson], @tb{b}_p3, @tb{b}_p1, @tb{b}_p2, [s].[Id], NULL, @tb{b}_now
FROM [core].[Schedules] AS [s] WHERE [s].[Id] = @tb{b}_p0;
SELECT [JobId] FROM @tb{b}_jobIds;   -- result set: the job id
```

Delete (`ValidateDeleteAsync`) refuses built-ins; the emitter's delete cascades `ScheduleStates`
and sets `Jobs.ScheduleId` to null through the FK.

### 7.9 Built-in schedules

Declared through spec 0010's `FeatureContribution.BuiltInSchedule` (the id, handler key, cron,
arguments and time zone) and realized as `HasData` rows in the reserved band — a `core.Schedules`
row with `Code = Name = HandlerKey`, `RunAsUserId = WellKnownIds.SystemUserId`, `IsActive = 1`,
`MissedPolicy = Coalesce`, `OverlapPolicy = Skip` and the system user in its audit columns — plus
a `core.ScheduleStates` row with `NextDueAt = NULL`, initialised by the first tick (§7.4, §7.5)
from `Now` without firing and never caught up; the heartbeat's seeded row (§6.10) starts null the
same way.

Ids are declared by the contribution and stable for the life of a distribution: 1–99 are Core's
(table below); 100–499 are the module packs', each pack recording its ids in its own spec; 500–999
are the distribution's. The composition gate enforces the reserved band, Core's reservation of
1–99, and the uniqueness of every id and of every handler key — one built-in per key, since
`Code = HandlerKey` is unique; the pack and distribution sub-bands are a convention the gate
cannot tell apart. A retired built-in retires its id with it.

| Id | Key | Cron | Shipped by |
|---|---|---|---|
| 1 | `core.job-retention` | `0 3 * * *` | this spec (§14.4) |
| 2 | `core.notification-retention` | `30 3 * * *` | spec 0020 |
| 3 | `core.file-retention` | `0 4 * * *` | this spec (§14.4) |
| 4 | `core.blob-sweep` | `*/15 * * * *` | spec 0016 |
| 5 | `core.blob-reconcile` | `0 2 * * 6` | spec 0016 |
| 6 | `core.tree-verify` | `0 1 * * 6` | this spec (§14.5) |

This table is the record of every Core built-in's id and expression; the contributing specs cite
it. Platform code never changes a shipped built-in's seeded values (that would scaffold an
`UpdateData` migration overwriting tenant edits); a changed default is a new key. A tenant may edit
a built-in's `CronExpression` and `TimeZoneId` and its names, nothing else (§7.8).

## 8. Credentials and job scopes

- **Every job carries `RunAsUserId`.** Built-in schedules and platform batch handlers run as the
  seeded system user (`WellKnownIds.SystemUserId`, a `core.Users` row with no subject that holds
  every permission and cannot sign in). User-created schedules run as their creator, re-stamped only
  by `take-over`. User-triggered jobs run as the requester; a run-now job (§7.8) is user-triggered:
  it runs as its caller, or as the system user on a built-in. Admin `retry` and `resume` keep the
  original `RunAsUserId`, never the administrator's.
- **The job scope.** Before invoking a handler the worker builds a `RequestContextSnapshot`
  (`TenantId`, `Kind = User` or `System`, `Subject` = the user's subject or `"system"`,
  `UserId = RunAsUserId`, `Client = "worker"` — a value of spec 0015's client set that never
  reaches a request filter — and `TraceParent` = the job's when the partition holds one job, else
  null: log-correlation data that fills `RequestContext.OriginTraceParent`, spec 0010 §4.1, while
  the links of §9 carry every job) and opens `ITenantScopeFactory.CreateScopeAsync(snapshot)` (spec
  0010 §4.4) inside the `process <key>` activity of §9; `TenantUnavailableException` (the tenant
  left `Active`) leaves the lease to lapse. The scope factory runs the connect initializer —
  `IUserConnector.ConnectAsUser(RunAsUserId)` (`ConnectPremises.ForUser`: tags and `IsActive`, no
  activity stamp, no state flip) or `ConnectAsSystem()` for the system user — and then spec 0012's
  negotiation initializer, which resolves the locale fields from the run-as user's
  `PreferredLanguage`, `PreferredCalendar` and `PreferredTimeZone`, else the tenant defaults (spec
  0012 §9.3), so a handler invocation pays one connect. A run-as user who is inactive fails the
  connect with `TenantNotFoundException` (spec 0013 §7.6): the handler is not invoked, and every
  item of the partition is `Failed` with `Jobs.UserInactive` by a completion without a handler
  (§5.4), which raises `core.job.failed` for each (§10). A run-as user deactivated during the run
  takes the same path when the completion's re-check fails (§5.6). The schedule pauses at the next
  tick (§7.7).
- **The job frame.** Around each `ExecuteAsync` the worker opens the frame of spec 0014 §13.3 —
  `Kind = Job`, `Operation = "job:<key>"` — bound to the partition's completion batch and
  registered in the job scope as the scoped `IOpenWriteHost`. A handler that writes rows a stack
  owns implements `IEnlists<TTarget>`, enlists them with the frame and awaits the injected frame's
  `PersistAsync`, which places every group on `JobBatch.Batch` after the completion statement
  (§5.6), so the rows commit with the outcome and never survive a lost lease. The run-as scope
  authorises the frame; an enlisted group carries no decision of its own (spec 0013 §5.3). A
  `ValidationException` from `PersistAsync` surfaces inside `ExecuteAsync` and follows §3.4: it
  fails the item at once; one raised at persist time, in the completion round trip, fails it the
  same way (§5.6).
- **Permissions at run time.** Handlers evaluate permissions through spec 0013's
  `IAccessEvaluator.EvaluateAsync(resource, action)` exactly as a request does, so a schedule can
  never read more than its owner may read today. Handlers of system jobs never read data on behalf
  of a person: they operate on platform tables or on data the tenant as a whole owns.
- **`DataAccessScope`** is fresh per invocation (`Operation = "job:<key>"`) so the round-trip
  budget instruments cover jobs.

## 9. Trace linkage and logging

- `Enqueue` stores the enqueuing activity's `traceparent` (§4.3). The worker owns one activity per
  handler invocation: it clears `Activity.Current`, then starts `process <key>` as a **true root**
  — `ActivitySource("Tellma.Core").StartActivity("process <key>", ActivityKind.Consumer,
  parentContext: default, tags, links)` with one `ActivityLink` per claimed row that has a
  `TraceParent` — so the enqueuing request's trace closes when the request does and its sampling
  decision does not decide the job's. The job scope (§8) is opened inside it, so the connect, the
  handler and the data spans nest under `process <key>`; the completion is its child `Client` span
  `settle <key>`. The claim is a `Client` span `receive <key>` under the activity the worker starts
  for each poll round trip (§6.3). A partition of several jobs has a null snapshot `TraceParent` and
  the links carry the detail; the snapshot's `TraceParent` only ever fills
  `RequestContext.OriginTraceParent` (spec 0010 §4.1) and never parents or links anything.
- Tags are bounded: `messaging.system = "tellma.jobs"`, `messaging.destination.name = <key>`,
  `messaging.operation.name ∈ { receive, process, settle }`, `messaging.batch.message_count`,
  `tellma.jobs.attempt`. Tenant id, job ids and user id go to the log scope, never to tags or
  metrics. Baggage is neither stored nor propagated.
- Log events (structured, `Tellma.Core.Jobs` category): `JobClaimed`, `JobProgress` (`Debug`: the
  percent and the rendered message), `JobCompleted` (outcome, duration, and the rendered error of a
  `Failed` or `Retry` outcome), `JobHandlerFailed` (`Error`: the exception `Execute` threw, §3.4),
  `JobCompletionFailed` (`Error`: the completion's failure, §5.6), `JobLeaseLost`,
  `JobHandlerAbandoned`, `SchedulerGapDetected`, `SchedulePaused`, `SchedulerHandlerUnknown`,
  `WorkerDrainTimedOut`; messages cite no document.
- A progress or error key (§3.3) is rendered for the log in English, in the invariant culture,
  through spec 0012 §10.2's `IcuStringLocalizerFactory`: a handler's keys from its handler type's
  assembly's `Strings`, then Core's, which holds the `Jobs.*` keys (spec 0020 §2.4 renders
  notifications the same way).

## 10. Notifications and hub events raised by the machinery

Notification types this spec registers through spec 0010's `FeatureContribution.NotificationType`
(spec 0020's `NotificationTypeDescriptor(Key, Category, Mutable, TargetResource)`), all with
`Category = "jobs"`:

| Key | `Mutable` | Target | Raised | Recipients | Arguments |
|---|---|---|---|---|---|
| `core.job.failed` | false | `core.Job` | a completion ends `Failed` | `RequestedById`, else administrators | `{ handlerKey, errorCode }` |
| `core.job.held` | false | none | the gap hold, once per requester; `DedupKey = 'core.job.held'` | each distinct `RequestedById` among the held rows | `{ count }` |
| `core.schedule.paused` | true | `core.Schedule` | §7.7 | `Exhausted` or `HandlerRetired`: the owner when a person, else administrators; `OwnerInactive`: administrators | `{ scheduleId, name, reason }` |
| `core.scheduler.gap` | false | none | §6.10; `DedupKey = 'core.scheduler.gap'` | administrators | `{ previousTickAt, now, heldCount }` |

Notifications ride the completion or tick batch as `NotificationRequest`s of spec 0020 §3.1 through
`INotifier.Notify(batch, requests)`; the gap notices (`core.scheduler.gap`, `core.job.held`) and
nothing else use their own `Persist` batches under the system user after the poll (§6.10).
`core.job.failed` names the error by its code; the job pages render it with its arguments (§11.3).
Administrators come from `IAdministratorDirectory.GetAdministratorIdsAsync()` (spec 0013).

The hub event `job.changed { jobId }` is registered through `FeatureContribution.ClientEvent` and
published through spec 0020's `IClientEventPublisher.Publish(batch, ClientEvent(Name, UserIds,
Payload))` on the completion batch and `PublishAsync` after claims and progress-carrying renewals,
only when the row's `RequestedById` is set and only to that user. `INotifier` is always present;
`IClientEventPublisher` resolves to spec 0020's `NullClientEventPublisher` (registered with
`TryAdd`) on a host without the hub, so the worker runs unchanged on a dedicated worker host.

## 11. The administrative surface

### 11.1 `JobService`

```csharp
// Tellma.Core.Jobs
public class JobService : EntityService<Job>            // Operations = Query | Details
{
    public Task RetryAsync(ActionContext<Job, int> context);            // [EntityAction] "retry", action Retry
    public Task CancelAsync(ActionContext<Job, int> context);           // [EntityAction] "cancel", action Cancel, destructive
    public Task ResumeAsync(ActionContext<Job, int> context);           // [EntityAction] "resume", action Resume
}
```

| Action | Precondition (else the validation code) | Effect (a fixed-text `Sql` statement on the action's persist batch, `Writes = { core.Jobs }`) |
|---|---|---|
| `retry` | `Status = Failed` (`Jobs.NotRetryable`) | `Status = Pending`, `Attempts = 0`, `DueAt = @tb{b}_now`; `CompletedAt`, `CancelRequestedAt` and the error columns null |
| `cancel` | `Status IN (Pending, Held, Running)` (`Jobs.NotCancellable`) | `Pending`/`Held` → `Cancelled`, `CompletedAt = @tb{b}_now`, `ErrorCode = 'Jobs.Cancelled'`; `Running` → `CancelRequestedAt = @tb{b}_now` only |
| `resume` | `Status = Held` (`Jobs.NotHeld`) | `Status = Pending`, `DueAt = @tb{b}_now`; `CancelRequestedAt` and the error columns null |

The three write actions take the pre-checked visible ids as `@tb{b}_t0 : IdList` and open on the
database clock (§1.3):

```sql
-- RETRY (@tb{b}_t0 : IdList)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
UPDATE [j] SET [Status] = 'Pending', [Attempts] = 0, [DueAt] = @tb{b}_now, [CompletedAt] = NULL, [CancelRequestedAt] = NULL,
    [ErrorCode] = NULL, [ErrorArgumentsJson] = NULL, [ErrorTraceId] = NULL
FROM [core].[Jobs] AS [j] INNER JOIN @tb{b}_t0 AS [i] ON [i].[Id] = [j].[Id]
WHERE [j].[Status] = 'Failed';
```

```sql
-- CANCEL (@tb{b}_t0 : IdList; a Running row is only flagged — its item token is cancelled at the next renewal, or its next claim cancels it)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
UPDATE [j]
SET [Status] = CASE WHEN [j].[Status] = 'Running' THEN [j].[Status] ELSE 'Cancelled' END,
    [CancelRequestedAt] = CASE WHEN [j].[Status] = 'Running' THEN @tb{b}_now ELSE [j].[CancelRequestedAt] END,
    [CompletedAt] = CASE WHEN [j].[Status] = 'Running' THEN [j].[CompletedAt] ELSE @tb{b}_now END,
    [ErrorCode] = CASE WHEN [j].[Status] = 'Running' THEN [j].[ErrorCode] ELSE N'Jobs.Cancelled' END,
    [ErrorArgumentsJson] = CASE WHEN [j].[Status] = 'Running' THEN [j].[ErrorArgumentsJson] ELSE NULL END,
    [ErrorTraceId] = CASE WHEN [j].[Status] = 'Running' THEN [j].[ErrorTraceId] ELSE NULL END
FROM [core].[Jobs] AS [j] INNER JOIN @tb{b}_t0 AS [i] ON [i].[Id] = [j].[Id]
WHERE [j].[Status] IN ('Pending', 'Held', 'Running');
```

```sql
-- RESUME (@tb{b}_t0 : IdList)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
UPDATE [j] SET [Status] = 'Pending', [DueAt] = @tb{b}_now, [CancelRequestedAt] = NULL,
    [ErrorCode] = NULL, [ErrorArgumentsJson] = NULL, [ErrorTraceId] = NULL
FROM [core].[Jobs] AS [j] INNER JOIN @tb{b}_t0 AS [i] ON [i].[Id] = [j].[Id]
WHERE [j].[Status] = 'Held';
```

Each action runs through the pipeline's general action path (spec 0014 §11.2): RT1 loads the
targets under the action's own grant filter and evaluates the precondition, RT2 appends the
statement over the loaded ids to the `Persist` batch; no post-check. `retry` and `resume` nudge the
local poller after commit. Each statement repeats its precondition in its `WHERE`, so its batch
declares `Idempotent = true` (§1.4): a repeat matches no rows, save a `Running` row's cancel flag,
which it sets again. None of the three declares spec 0015 §2.2's `Idempotent` (retry-safe): a
client's retry after a commit would fail the precondition. `cancel` is `Destructive = true`; the
three keep the default `Mutation = true`.

### 11.2 Securables

Registered by the stack feature from the descriptors, each resource with its actions: `core.Job` ×
`Read | Retry | Cancel | Resume`; `core.Schedule` × `Read | Save | Delete | Activate` (the three
write actions are sensitive by default and require step-up per spec 0013; `core.Job × Cancel` is
not sensitive). The bespoke self-scope criterion `RequestedById = me()` is registered for
`core.Job × Read` and `core.Job × Cancel` through an `IAccessCriteriaProvider`, so every member
sees "My jobs" and cancels the jobs they requested without a role; `retry` and `resume` are never
bespoke. Route segments: `jobs` and `schedules`; action segments `retry`, `cancel`, `resume`,
`take-over`, `run-now`.

### 11.3 Endpoints and pages

Spec 0015's `MapTellma` projects both stacks from their `StackDescriptor`s: the standard operations
for `schedules` plus `take-over` and `run-now`; query, details, `retry`, `cancel` and `resume` for
`jobs`. A background operation answers 202 with spec 0015's `JobAccepted(JobId, ResourceId?)`:
`run-now` and an export carry a null `ResourceId` (§7.8; an export's row is inserted by its handler
on completion, §14.1). Client pages render a job's progress and error from the string pack (§3.3):
**Schedules** (standard search and details; owner, next due, last fired, take-over and run-now
buttons, and a paused banner naming the remedy: `take-over` for `OwnerInactive`, an edited
expression for `Exhausted`); **Background jobs** (administrative search with status, handler and age
filters, each error's trace id with a copy control, a heartbeat banner reading
`core.JobWorkerState`, a "held after gap" banner with resume and cancel over the held ids);
**My jobs** (the same query under the self-scope criterion, with the cancel action on the member's
own jobs). MCP exposure: the tool `tellma_job` is reserved; none ships in this release.

## 12. Configuration and hosting

### 12.1 `JobsOptions`

```csharp
// Tellma.Core.Abstractions.Jobs; bound from Tellma:Jobs and validated at startup
public sealed class JobsOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxParallelBatches { get; set; } = 8;
    public TimeSpan MinPollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxPollInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(4);
    public TimeSpan GapThreshold { get; set; } = TimeSpan.FromHours(6);
    public TimeSpan SucceededRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan FailedRetention { get; set; } = TimeSpan.FromDays(180);
}
```

| Option | Rule |
|---|---|
| `Enabled` | `false` turns the worker off on this host (a web-only instance beside a worker host); enqueue, the stacks and the schedule service still work. |
| `MaxParallelBatches` | 1–256. |
| `MinPollInterval`, `MaxPollInterval` | `1 s ≤ Min ≤ Max ≤ 30 s`; the ceiling is the cron granularity. |
| `DrainTimeout` | ≥ 1 s and below the host's `ShutdownTimeout`, which the final renewal of §6.7 also binds. |
| `GapThreshold` | ≥ 1 h. |
| `SucceededRetention`, `FailedRetention` | ≥ 1 d; read by `core.job-retention`. |

Violations report into the composition gate through
`FeatureDeclaration.Options<JobsOptions>("Tellma:Jobs")`.

### 12.2 Package pins

`Cronos` 0.13.0 is added to central package management and referenced by `Tellma.Core`. No
SignalR, Redis or Quartz pin changes belong to this spec.

### 12.3 Hosting requirements

The reference distribution's infra template sets `Always On` (an idle App Service stops the poller
after twenty minutes otherwise) and `WEBSITES_CONTAINER_STOP_TIME_LIMIT = 30` (the Linux default of
5 s is below any useful drain); the host's `ShutdownTimeout` stays at its 30 s default. On-premises
hosts need nothing beyond the process staying alive. Every instance that runs the worker must be
able to reach every tenant database; there is no worker-to-tenant affinity.

## 13. Telemetry

Constants in `JobsTelemetryNames` (`Tellma.Core.Abstractions.Jobs`); meter `Tellma.Core`;
`ActivitySourceName = "Tellma.Core"`. Tags: `handler` (bounded by the registry), `outcome` and
`policy`; the tenant is never a tag. `outcome` takes `succeeded`, `retry`, `failed`, `released` or
`cancelled`; `policy` takes `coalesce`, `replay_all` or `skip`.

```csharp
// Tellma.Core.Abstractions.Jobs
public static class JobsTelemetryNames
{
    public const string MeterName = "Tellma.Core";
    public const string ActivitySourceName = "Tellma.Core";
    public const string Claimed = "tellma.jobs.claimed";
    public const string Completed = "tellma.jobs.completed";
    public const string Duration = "tellma.jobs.duration";
    public const string QueueLatency = "tellma.jobs.queue.latency";
    public const string BatchFill = "tellma.jobs.batch_fill";
    public const string InFlight = "tellma.jobs.in_flight";
    public const string LeaseLost = "tellma.jobs.lease_lost";
    public const string BacklogAge = "tellma.jobs.backlog.age";
    public const string PollDuration = "tellma.jobs.poll.duration";
    public const string Orphaned = "tellma.jobs.orphaned";
    public const string SchedulesFired = "tellma.schedules.fired";
    public const string SchedulesMissed = "tellma.schedules.missed";
    public const string SchedulesOverlapSkipped = "tellma.schedules.overlap_skipped";
    public const string SchedulesGapDetected = "tellma.schedules.gap_detected";
    public const string HandlerTag = "handler";
    public const string OutcomeTag = "outcome";
    public const string PolicyTag = "policy";
}
```

| Instrument | Kind, unit | Tags | Answers |
|---|---|---|---|
| `tellma.jobs.claimed` | counter, `{job}` | handler | throughput |
| `tellma.jobs.completed` | counter, `{job}` | handler, outcome | failure and dead-letter rate |
| `tellma.jobs.duration` | histogram, `s` | handler | lease sizing against `LeaseSeconds` |
| `tellma.jobs.queue.latency` | histogram, `s` (`StartedAt − DueAt` at first claim) | handler | backed-up queues, poll interval too long |
| `tellma.jobs.batch_fill` | histogram, `1` (claimed ÷ `BatchSize`) | handler | batch size mis-configured |
| `tellma.jobs.in_flight` | up-down counter, `{job}` | handler | concurrency |
| `tellma.jobs.lease_lost` | counter, `{batch}` | handler | handlers slower than their lease |
| `tellma.jobs.backlog.age` | histogram, `s` | handler | starvation (the per-minute sample) |
| `tellma.jobs.poll.duration` | histogram, `s` | — | poll cost per tenant |
| `tellma.jobs.orphaned` | observable gauge, `{job}` | — | pending rows overdue by an hour whose key no handler on this instance registers and no feature retires; the sum of this instance's latest per-tenant samples (§6.9) |
| `tellma.schedules.fired` | counter, `{job}` | policy | firing volume |
| `tellma.schedules.missed` | counter, `{occurrence}` | policy | dropped or coalesced occurrences within `GapThreshold` (§7.5) |
| `tellma.schedules.overlap_skipped` | counter, `{firing}` | — | schedules whose interval is shorter than their run |
| `tellma.schedules.gap_detected` | counter, `{tick}` | — | restore or long outage |

Alert queries under `infra/monitoring/` (cross-checked by the existing name test): dead-letter rate,
`backlog.age > 10 min`, `gap_detected > 0`, and a log-based "heartbeat older than 2 minutes" per
tenant.

## 14. First consumers

### 14.1 Export (spec 0018)

The `core.export` handler is an arguments-only `IJobHandler` with `[JobHandler]` (key `core.export`,
`BatchSize = 1`, `LeaseSeconds = 600`, `MaxAttempts = 3`, `Schedulable = true`) and implements
`IEnlists<Export>`. The `export/start` and `export-for-import/start` actions enqueue it through
`IJobQueue.EnqueueAsync` with no `Entity`, `Arguments` the request's `ExportJobArguments` (spec
0018 §12.1) and `RunAsUserId = RequestedById` = the caller, and return `JobAccepted(JobId, null)`
(spec 0018 §12.2); a user schedule naming `core.export` carries the same `ExportJobArguments` in
its `ArgumentsJson`, so a request-triggered run and a schedule-fired run are the same and neither
has an `Export` row to read. The handler re-evaluates `Read` on the resource in the job scope,
streams the query in pages, reports progress per page, stages the workbook through spec 0016's
`IBlobService`, and at the end of every run enlists with the job frame (§8) one insert of the
`Export` row — `Kind` from the arguments' case, `FileId`, `FileName` and `RowCount` set,
`JobId = Job.Id` under `EnlistSaveOptions.ServerOwned` — and calls `NotifyOnSuccess` with
`core.export.ready` (target `core.Export`, id = the inserted row): the insert, the confirmation of
the staged file by its blob effect and the notification ride the completion batch, one round trip
atomic with the outcome, so a lost lease leaves no `Export` row behind (spec 0018 §12.3).

### 14.2 Import (spec 0018)

`core.import`: an `IEntityJobHandler<Import>` implementing `IEnlists<Import>`, declared with
`[JobHandler("core.import", BatchSize = 1, LeaseSeconds = 600, MaxAttempts = 1)]` — a partially
committed import must never re-run blindly; a crash mid-import surfaces as `Jobs.AttemptsExhausted`
and `core.job.failed` (§10). The handler commits each chunk through the target stack's front door,
`SaveAsync` in a `Persist` batch of its own (the handler is a host, not a participant of a pipeline,
spec 0014 §13.3), and checkpoints the position of the first uncommitted row (spec 0018 §12.5) with
`IJobProgress.Append(batch, …)` through spec 0014's `SaveOptions.OnPersist`, inside each chunk's
persist transaction, so a `Job.LeaseLost` fence rolls the chunk back with the checkpoint. The
completion columns — `ResultJson`, `RowCount`, `ErrorCount` — are an enlisted update of the claimed
`Import` row with the job frame (§8) on the completion batch, one round trip atomic with the outcome
(spec 0018 §12.4, §12.6). The `import/start` action enlists the `Import` row in the invoker's frame
and awaits the injected `IOpenWriteHost`'s `PersistAsync`; `ImportService.ContributeAsync` enqueues
every new row whose `JobId` is null, so the row, its job and the attach of the staged upload to the
row commit together and the action returns `JobAccepted(JobId, ImportId)` (spec 0018 §12.2), and
queue latency is not bounded by the staging TTL.

### 14.3 Blob sweep and reconcile (spec 0016)

`core.blob-sweep` (every 15 minutes) and `core.blob-reconcile` (weekly) are arguments-only
`IJobHandler`s shipped by the blob feature, running as the system user: the sweep pages by
`BlobOptions.SweepBatchSize` and reports progress per page, the reconcile runs one `Maintenance`
batch per registered kind (spec 0016 §7.1, §7.2). They are the reference shape for a maintenance
handler: idempotent and bounded.

### 14.4 Retention (this spec)

`core.job-retention` (`0 3 * * *`; `[JobHandler("core.job-retention", LeaseSeconds = 600)]`):
deletes `Succeeded` rows with `CompletedAt` older than `SucceededRetention` and `Failed`/`Cancelled`
rows older than `FailedRetention`, in pages of 1,000 — each page its own `Maintenance` round trip
with `Idempotent = true` (`@tb{b}_p0` = `SucceededRetention` in seconds, `@tb{b}_p1` =
`FailedRetention` in seconds; `ResultSets = 1`), looping per band until a page is short and
reporting progress per page:

```sql
DELETE TOP (1000) FROM [core].[Jobs]
WHERE [Status] IN ('Succeeded') AND [CompletedAt] < DATEADD(second, -@tb{b}_p0, SYSUTCDATETIME());
SELECT @@ROWCOUNT AS [Deleted];
-- second band, same loop:
DELETE TOP (1000) FROM [core].[Jobs]
WHERE [Status] IN ('Failed', 'Cancelled') AND [CompletedAt] < DATEADD(second, -@tb{b}_p1, SYSUTCDATETIME());
SELECT @@ROWCOUNT AS [Deleted];
```

Cut-offs are computed from the database clock inside the statement (§1.3); §12.1 keeps the two
options as `TimeSpan` and the handler converts to seconds when binding. Referencing `IJobEntity`
rows and `ScheduleStates.LastJobId` go null through their FKs. `Held` and `Pending` rows are never
deleted.

`core.file-retention` (`0 4 * * *`): selects expired `Export` and `Import` ids (`ExpiresAt < now`)
in pages of 500 and deletes them through spec 0014's `DeleteByIdsAsync` on the two stacks as the
system user, so the `[BlobReference]` release effect runs and the sweep reclaims the bytes later;
nothing is deleted post-commit.

### 14.5 Tree verify (this spec)

`core.tree-verify` (`0 1 * * 6`): for every tree table in the model, one `Maintenance` round trip
running spec 0011's tree recount statement in whole-table scope, `Idempotent = true`, as the system
user; repaired rows count on spec 0011's `tellma.data.tree.repairs` and a non-zero count logs its
`TreeVerify.Repaired` at Warning. The weekly pass is the backstop for the per-save affected-set
recount.

### 14.6 The email outbox (the outbox spec)

Alignment rules this spec fixes: the outbox row implements `IJobEntity`; its handler is
`IEntityJobHandler<EmailOutboxMessage>` with `BatchSize = 100`, `LeaseSeconds = 120`,
`MaxConcurrency = 1`; rows are enqueued with `RunAsUserId = WellKnownIds.SystemUserId` and
`RequestedById` = the enqueuing user; `EmailEnqueueRequest.SendAfter` maps to `JobRequest.DueAt`;
per-message `EmailSendResult`s map to per-item outcomes; the "signal after commit" is the nudge of
§4.4. Spec 0007 §13's reserved shape needs no change.

## 15. Composition and startup checks

`CoreFeature` contributes: `JobHandler<T>()` for `core.job-retention`, `core.file-retention` and
`core.tree-verify`; `BuiltInSchedule(...)` for the three keys of §7.9 it owns; the four notification
types of §10 and the `job.changed` event; `Entity<Job, JobService>()` and
`Entity<Schedule, ScheduleService>()`; the `Schedule` validator; the `core.Job` self-scope criteria
provider; the five standalone table types; the model configuration for the four tables (the
`StateJson` and `LeaseToken` shadow properties and their exclusion from the Jobs UDTT, §2.3; the
filtered indexes, `LOCK_ESCALATION = DISABLE`, `HasData` rows); the `JobWorker` hosted service (when
`Enabled`); `JobsOptions`.

The checks of the `JobHandler`, `BuiltInSchedule` and `RetiredJobKey` realizers — handler
registration and retired keys (§3.7), the built-in ids (§7.9) — and the `JobsOptions` bounds (§12.1)
report into the composition gate. Realized-gate checks (`IStartupCheck`s named `jobs.handlers`,
`jobs.schedules`): built-in schedules whose cron fails to parse or has no next occurrence
(`jobs.schedules`), or whose key has no handler (`jobs.handlers`; a warning, not a failure — a
schedule for a handler another host runs is legal).

Illustration (a distribution registering a handler and a nightly schedule for it):

```csharp
contribution.JobHandler<RecomputeBalancesHandler>()
            .BuiltInSchedule(500, "acme.recompute-balances", "0 1 * * *");
```

## 16. Testing

Test projects mirror `src/`: `test/core/Tellma.Core.Tests/Jobs/` (offline, hermetic) and
`test/core/Tellma.Core.IntegrationTests/Jobs/` (`Category=Integration`; LocalDB on Windows,
Testcontainers SQL Server on Linux, both with RCSI on and off). No `Live=true` suite: nothing here
needs an external service. PR CI runs the offline suite and the integration suite; nightly adds
nothing for this spec.

Offline suite pins: the key grammar and option validation; outcome mapping of §3.4 for every
per-item reason and exception shape, including a batch of several items with one cancelled; an
unexpected exception recorded as `Jobs.Internal` with the trace id of its `JobHandlerFailed` event;
full-jitter backoff bounds; run-as partitioning order; the worker under a fake `TimeProvider` —
adaptive interval doubling and reset, the immediate re-poll after a full claim or a full
tick step 1, nudge coalescing (ten nudges, one wake), `MaxParallelBatches` and `MaxConcurrency`
limits, drain rules (untouched items completed `Released`, running handlers left leased, the final
renewal binding `ShutdownTimeout` for every still-running batch, the abandonment warning); Cronos
policies with fixed zones and DST transitions (`Coalesce`, `ReplayAll` with the catch-up window,
`Skip`'s window, exhaustion, the gap degradation firing the latest occurrence without enumerating);
the validator's `Schedules.ReplayAllRequiresAllow`; the statement text of §4–§7 and §11.1 as golden
files; `JobsTelemetryNames` against `infra/monitoring/`; the composition checks of §15 (the built-in
ids, a key both registered and retired, a built-in naming a retired key), each `IStartupCheck`
invoked directly over a composed service provider.

Integration suite pins, on a fixture tenant database: the claim under contention from two
connections (disjoint sets, order by `DueAt, Id`); the plan test — the filtered-index seek of the
claim under `TOP (@tb{b}_room)` (its failure flips review flag 4 on the index shape of §2.1), and
tick step 1's clustered range seek over the reserved band and its `IX_ScheduleStates_Due` seek;
renew fencing (a reclaimed row is absent from the output); append and complete fencing
(`Job.LeaseLost` rolls back the handler's statements and the groups it enlisted with the job frame,
§8); an enlisted group's rows committing with the outcome in the one completion round trip; a
constraint violation in an enlisted group failing every item at once, and a guard failure at
completion retrying every item with the handler's statements discarded (§5.6); `Released`
un-counting; a resumed item reading the checkpoint its claim returned; the completions without a
handler of §5.4 (`Jobs.AttemptsExhausted` for a row past its last attempt, `Jobs.Cancelled` for a
re-claimed row with `CancelRequestedAt` set, `Jobs.EntityMissing`, `Jobs.UserInactive`); the gap
hold with per-requester counts, the heartbeat throttle and the stamp last; the retired-key cancel (a
running row only once its lease lapses) and the `HandlerRetired` pause; enqueue riding a persist
batch with `Entity` (the owner's `JobId` set in the same transaction, ids in request order, the
post-commit nudge observed); the job actions' preconditions and each statement's repeat (no row
matched, save a `Running` row's cancel flag, §11.1); retention paging and `SET NULL`.

On the same fixture, the scheduler: tick step 1 leasing — a seeded built-in row with a null
`NextDueAt` leased and initialised by the first tick without firing, a deactivated row with a null
`NextDueAt` never leased; step 2's overlap predicate, its result set, `LastSkippedAt` on overlap
skips only, the pause's single write, the tick-lease fence, `sp_sequence_get_range` id assignment
and the `ScheduleStates` advance; the schedule validator codes, `Schedules.CronNeverOccurs` and
`Schedules.OwnerInactive` among them (`activate` with an inactive owner refused, `take-over` then
`activate` succeeding); `run-now` (`Schedules.RunInProgress`; the inserted row's `ScheduleId` set,
`ScheduledFor` null, the run-as user per kind, `RequestedById` the caller). Re-running the poll
(§6.8): a failure injected after the claims, tick step 1 and the hold committed — in both classes, a
deadlock and a connection killed after the command was sent — re-runs the poll, which returns every
row leased under the same tokens with `Attempts = 1`, reports the gap once with the held counts, and
leaves no row leased and untracked. An end-to-end test runs a fixture handler through enqueue,
claim, progress, completion and `job.changed` with in-memory `INotifier` and `IClientEventPublisher`
fakes.

## 17. Definition of done

- **Projects**: the `Tellma.Core.Abstractions.Jobs` namespace and the `Tellma.Core.Jobs` runtime
  namespace, the two test folders, and the reference distribution's infra template change — each
  project with a README, XML docs on every member, building and testing on Windows and Linux under
  warnings-as-errors, wired into `Tellma.slnx`.
- **Behavior**: §2 tables and entity, §3 handler contract and outcome rules, §4 enqueue, §5 the
  lease statements and the completion transaction, §6 the worker (polling, nudges, concurrency,
  cancellation, drain, re-running the poll, orphaned and retired keys, gap hold), §7 the scheduler
  (entity, tick, missed and overlap policies, pausing, service rules, run-now, built-ins), §8
  credentials, §9 trace links, §10 notifications and events, §11 actions and securables,
  §14.4–§14.5 the three handlers — all implemented and pinned by the suites of §16, green in CI.
- **Observability**: every instrument of §13 emitted and asserted; the log events of §9 asserted
  in the offline suite; the alert queries present under `infra/monitoring/`.
- **CI**: the offline and integration suites on PR; the plan test in the integration suite.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the "Hosting on Azure" section
  (`Always On` and `WEBSITES_CONTAINER_STOP_TIME_LIMIT = 30` required; Cronos for tenant schedules,
  Quartz identity-only), the "Library architecture — package naming and dependency rules" section
  (`Tellma.Core` references `Cronos`; jobs are a namespace inside `Tellma.Core`, not a package),
  and the "Observability" section (meter `Tellma.Core` for `tellma.jobs.*` and
  `tellma.schedules.*`). Public XML docs and error messages reference no `docs/` paths, per repo
  rule.
- **Not in scope of done**: the export, import, blob and notification-retention handlers, the
  notification and hub implementations, tenantless jobs, the cross-instance nudge, `HoldAfterGap`.

## Decisions record

The load-bearing decisions, where not already evident above:

1. **One `core.Jobs` table per tenant; business rows reference it through `IJobEntity.JobId`** —
   lease columns on business rows collide with business statuses, churn temporal history on every
   renewal, contaminate the concurrency stamp and cannot be pruned (§2).
2. **Two handler shapes, one registry** — arguments-only and entity-backed handlers differ only in
   what the claim loads; the worker, statements and options are shared (§3.2).
3. **Batch-shaped handlers with per-item outcomes** — the outbox needs 97 successes and 3 retries
   from one claim; a single-item handler pays nothing through `Single` (§3.3, §3.4).
4. **Renewed sliding lease with a Guid fencing token** — estimates are wrong exactly when it
   matters; renewal at one fifth makes one missed renewal harmless (§5.2).
5. **`Attempts` counts claims; graceful releases un-count** — the only poison detector that
   survives a crashing process, without dead-lettering a long job across two deploys (§5.5).
6. **The completion statement is the first statement of its transaction** — nothing a handler
   wrote survives a lost lease (§5.4).
7. **Ids assigned inside the enqueue and tick statements** — `Enqueue` stays synchronous and never
   depends on the allocator's buffer (§4.2, §7.6).
8. **No cross-instance coordination** — leased rows are the whole protocol; the nudge is local and
   pickup elsewhere is bounded by `MaxPollInterval` (§6).
9. **Drain releases untouched batches and leaves running handlers leased past the process's death**
   — releasing under a running handler, or a lease lapsing while the process lives, is the way to
   execute twice concurrently (§6.7).
10. **The gap hold quarantines stale queued rows for a human** — a restored backup must not replay
    a year of side effects, and cancelling loses work someone may still want (§6.10).
11. **One `core.Schedules` table, Cronos, three missed policies and an overlap policy** — one
    validation path, one tick, one admin page; the policies match every mature scheduler's
    vocabulary, and `ReplayAll` implies `Allow` (§7, §7.1).
12. **Built-ins are `HasData` rows with explicit ids in the reserved band and immutable policies and
    keys** — identified by the band alone (§7.1); sweeps and retention cannot be switched off from
    the tenant; a changed default is a new key (§7.8, §7.9).
13. **The creator owns a user schedule; ownership moves only through `take-over`** — no silent
    re-stamping on an administrator's edit, and an inactive owner is cured by `take-over`, never by
    re-stamping on `activate`; no impersonation path: a run-now job runs as its caller, never as the
    owner (§7.8, §8).
14. **Permissions are evaluated at run time under the run-as user** — a schedule can never read
    more than its owner may read today (§8).
15. **A new root span per invocation, linked to each job's enqueuing context** — a job must not
    keep a request trace open for hours or inherit its sampling decision (§9).
16. **Unexpected failures store a generic code and the attempt's trace id** — the exception goes to
    the log, never to the tenant database or a member's notification: spec 0015 §7.1's 500 rule
    applied to jobs (§3.4, §9).
17. **A handler writes a stack-owned table only by enlisting with the job frame** — the target's
    pipeline runs as a participant of the completion transaction, so an export's `Export` row with
    its `FileId` and `JobId`, or an import's completion columns, commit with the outcome in the one
    completion round trip, and no second write path into a stack exists (§3.3, §5.6, §8; spec
    0014 §13.3).
18. **Members cancel the jobs they requested** — every handler already tolerates an administrator's
    cancel, so letting the requester cancel changes who may, not what happens; a running import
    stops at a chunk boundary with the committed chunks kept (§11.2).
19. **Progress and error text are resource keys plus arguments, rendered by the reader** — spec 0020
    §1.3's rule; the log gets the English rendering (§3.3, §9).
20. **The poll is keyed by its tokens and re-runnable** — a re-run returns what an earlier attempt
    leased, so no row is ever leased and untracked (§6.8).
21. **Retired keys are declared; their rows are cancelled, their schedules paused** — a breaking
    change is a new key (§3.7), so keys retire routinely and their rows must not wait forever (§3.7,
    §6.9, §7.7).

## Review flags

1. **Queue table versus columns on the entity** (§2): one `core.Jobs` table with
   a Guid `LeaseToken`, referenced by `IJobEntity.JobId`. Alternative: lease columns on every
   task-bearing entity with a `bigint` fencing token. Flips if a consumer needs per-row lease
   visibility inside its own Queryex entity without a join.
2. **`Job` as the type family name** (§1.2). Alternative: `Task`/`BackgroundTask`. Flips only if
   the collision with `System.Threading.Tasks.Task` is judged acceptable in every handler file.
3. **Jobs inside `Tellma.Core`** (§1.1): one runtime package. Alternative: a separate
   `Tellma.Core.Jobs` package so a dedicated worker host references less. Flips if a worker host
   that must not load the web-facing runtime becomes a deployment requirement.
4. **Filtered index shape** (§2.1): `WHERE Status IN ('Pending', 'Running')` and a claim that
   repeats the literals. Alternative: `Status = 'Pending'` only, plus a reaper that returns expired
   `Running` rows to `Pending`. Flips if the plan test shows the optimizer refusing the seek.
5. **Attempts count claims, releases un-count** (§5.5). Alternatives: count reported failures only
   (misses crash loops) or count everything (dead-letters long jobs across deploys). Flips on
   evidence that graceful releases mask a genuine crash loop.
6. **Shutdown leaves running handlers leased past the process** (§6.7). Alternative: release every
   in-flight lease for faster pickup, accepting concurrent double execution. Flips only if every
   shipped handler is proven idempotent under concurrency.
7. **Local-only nudge** (§4.4, §6.4). Alternative: a Redis or Azure SignalR server bus for
   sub-second pickup when the local instance is saturated. Flips if `queue.latency` shows the
   30 s bound hurting a real workload.
8. **Default numbers** (§12.1, §7.5): poll 1 s/30 s, 8 parallel batches, drain 4 s, gap 6 h
   (72 h is the on-premises weekend alternative), catch-up window 24 h (the schedule's own interval
   is the alternative), `Skip`'s window of `2 × MaxPollInterval`, retention 30/180 days. Flip with
   the instruments of §13.
9. **Built-in schedules seeded by `HasData`** (§7.9). Alternative: an idempotent "ensure
   built-ins" insert at first tick, which never scaffolds an `UpdateData` migration but contradicts
   the seed rule. Flips if a shipped built-in ever needs its default changed in place.
10. **Gap quarantine holds stale queued rows** (§6.10). Alternatives: alert only and keep
    claiming; or a per-handler `HoldAfterGap = false` opt-out for idempotent maintenance keys.
    Flips if held sweeps after routine outages become an operational burden.
11. **User schedules run as their creator; ownership moves only through `take-over`** (§7.8, §8).
    Alternative: re-stamp `RunAsUserId` on every save by whoever saves. Flips if administrators
    editing others' schedules find the explicit step obstructive.
12. **`batch_fill` instrument** (§13) may not earn its keep. Alternative: drop it and derive fill
    from `claimed` and the configured `BatchSize`. Flips after one release of dashboards.
13. **Workers skip every non-`Active` tenant** (§6.2). Alternative: read-only handlers
    (exports) under `ReadOnly`. Flips if read-only maintenance windows prove long enough that
    blocked exports matter.
14. **`IJobProgress.Append` as the resumable-chunk mechanism** (§3.3, §5.3). Alternative:
    dedicated checkpoint columns on the `Import` row. Flips if a consumer needs a checkpoint that
    outlives the job row.
15. **System-written rows take ids inside their statements** (§4.2, §7.6). Alternative: an
    async path through the allocator's buffer. Flips if `sp_sequence_get_range` inside the persist
    transaction measurably contends on `core.sq_Jobs`.
16. **Built-ins keep `IsActive`, policies, arguments and key immutable** (§7.8).
    Alternative: let administrators pause a sweep with a notification. Flips if a tenant has a
    legitimate reason to suspend retention.
17. **Machinery columns off the `Job` class** (§2.3). Alternative: keep `StateJson` and `LeaseToken`
    as members and strip them in the pipeline's projection. Flips if the job reader proves awkward
    beside the materializer.
18. **The poll is keyed by its tokens and marked `Idempotent`** (§5.1, §6.8, §7.4). Alternative:
    tolerate stray claims, whose rows lapse and re-run with one wasted attempt. Flips if the
    per-claim token seek shows in `tellma.jobs.poll.duration`.
19. **A guard failure at completion discards the handler's statements and retries** (§5.6).
    Alternative: apply the fresh permissions and re-execute the same batch. Flips if a permission
    change during a long export is observed often enough that the re-run cost matters.
20. **Constraint, invariant and distribution-guard failures fail the job at once** (§3.4, §5.6).
    Alternative: retry them with backoff like every other error. Flips if transient data races
    make such failures recoverable often enough to matter.
21. **Retired rows end `Cancelled`, not `Failed`** (§6.9). Alternative: `Failed` with
    `core.job.failed` to each requester. Flips if requesters miss the silent cancel.
