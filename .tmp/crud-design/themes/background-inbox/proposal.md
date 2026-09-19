# Background tasks, scheduler, and the inbox — design proposal (spec 0019, with 0020 split)

Designed under all three lenses at once: distro-author simplicity, tier-2 performance and operations, and correctness and long-term maintainability. Where the lenses conflict, the conflict is named in the decision.

Vocabulary used throughout: a **job** is one durable unit of background work (one row of `core.Job`); a **handler** is the distro or platform class that executes jobs of one **handler key**; a **schedule** is a cron definition that creates jobs when due; a **notification** is one row of `core.Notification` addressed to one user, and the **inbox** is the user's view of their notifications; the **hub** is the SignalR hub that pushes thin events. "Task" is deliberately not used as a type name because it collides with `System.Threading.Tasks.Task` in every file that would use it.

## 0. Lens checks

### Lens A — distro-author simplicity and AI-native authoring

**How many lines does a distro write to add a background job?** One class and one registration line. An argument-only job (a nightly recompute) is:

```csharp
[JobHandler("etpharma.recompute-balances", LeaseSeconds = 600, MaxAttempts = 3)]
public sealed class RecomputeBalancesHandler(IGlBalances balances) : IJobHandler<Job>
{
    public async Task ExecuteAsync(JobBatch<Job> batch, CancellationToken cancellationToken)
    {
        var args = batch.Single.Arguments<RecomputeArgs>();
        await balances.RecomputeAsync(args.FromDate, batch.Single.Progress, cancellationToken);
    }
}
```

plus `jobs.AddHandler<RecomputeBalancesHandler>();` in the feature's contribute step. An entity-backed job (rows of a distro table processed in batches) adds one property to the entity (`public int? JobId { get; set; }` through `IJobEntity`) and closes the handler over that entity instead of `Job`. A built-in schedule is one line, `jobs.AddBuiltInSchedule("etpharma.recompute-balances", cron: "0 2 * * *", arguments: new RecomputeArgs(...))`, which becomes a seeded `core.Schedule` row in the reserved id band. A notification type is one descriptor line, `notifications.AddType("etpharma.batch-expiring", category: "Inventory", mutable: true, targetResource: "etpharma.batches")`, plus its resource strings. Raising a notification from inside a save is one call against the pipeline's batch: `notifier.Notify(batch, new NotificationRequest("etpharma.batch-expiring", recipientIds, args, targetId: batch.Id))`.

**Is every capability declared once?** Yes. The handler attribute is the only place a handler's key, batch size, lease, concurrency, and retry policy live; a schedule references the key and adds nothing about execution; the notification descriptor is the only place a type's category, mute-ability, and target resource live; the securables for `core.jobs`, `core.schedules`, and `core.notifications`, the endpoints, the admin pages and the self-service pages are registered once by the platform's jobs feature. A distro never writes a lease statement, a poll loop, a SignalR call, or a counter query.

**MCP consumer.** Jobs and notifications are plain resources: an agent that triggers a large export receives a job id and polls `get_job` (or reads its notifications) exactly as the SPA does; nothing in the design assumes a browser. All state an agent needs (status, progress, error, target link) is on the row.

### Lens B — tier-2 performance and operations

**DB calls per operation** (one "round trip" is one command text executed once; statements inside it are concatenated and walked with `NextResult()`):

| Operation | Round trips | What rides in it |
|---|---|---|
| Enqueue N jobs from a save or any batch | **0 extra** | The `INSERT core.Job` rides the caller's batch; ids come from the allocator buffer. |
| Enqueue N jobs outside a batch (background code) | 1 | Same statement, own command. |
| Poll one tenant, nothing due | 1 | Per registered handler key with free slots: one claim statement; plus the due-schedule claim, the heartbeat update, and the oldest-due sample. |
| Poll one tenant, work found | **same 1** | The claim outputs into a table variable; the entity rows for entity-backed handlers are selected by join in the same command. |
| Lease renewal and progress flush | 1 per (instance, tenant) per renewal interval | One `UPDATE … WHERE LeaseToken IN (@tokens)` for every in-flight batch of that tenant on this instance, carrying progress and checkpoint state as a TVP, returning cancel flags. |
| Completion of one batch | 1 | Outcomes TVP, the handler's own DB side effects, the notifications it raised, all in one transaction. |
| Schedule fire | +1 | The claim of due schedules rides the poll; inserting the jobs and advancing `NextDueAt` needs a second round trip because cron evaluation is C#. |
| Release on shutdown | 1 per tenant with in-flight work | `UPDATE … SET Status = 'Pending' … WHERE LeaseToken IN (@tokens)`. |
| Inbox summary | 1 | Two capped counts and the latest 10 rows. |
| Mark seen / mark read / mark all read | 1 | Single `UPDATE`. |
| Notification fan-out to M recipients | 0 extra | One `INSERT … SELECT` from an `IdList` TVP joined against preferences; mute filtering is a predicate, not a read. |

**N+1s found and closed.** Per-job entity loads (a join against the claimed set, one statement per handler key, never per job). Per-recipient preference reads (folded into the insert predicate). Per-batch lease renewals (coalesced per tenant per interval; one statement addresses every token). Per-tenant polling in a multi-live distribution (adaptive interval from 1 s under load to 30 s idle; 500 tenants idle cost ~17 seeks per second per instance, each a range seek on a filtered index that is empty for idle tenants). Per-occurrence schedule inserts under `ReplayAll` (one TVP insert). Per-user SignalR sends after a fan-out (one `Clients.Groups(list)` call).

**Locks held across I/O.** None. The claim statement autocommits before the handler runs; no transaction spans handler execution; renewal and completion are single statements or single-round-trip transactions; the schedule tick holds a 30-second row lease, not a lock, between its two round trips. `TOP (@n)` is capped at 500 rows per claim so a single statement never approaches the 5,000-lock escalation threshold, and `core.Job` is created with `LOCK_ESCALATION = DISABLE` as belt and braces.

**Plan cache.** Every claim, renewal, completion and inbox statement is parameterized except the status literals that the filtered index requires (`Status IN (N'Pending', N'Running')`), which are constants by design; `TOP (@n)` is a parameter. Handler keys are parameters, so one plan serves every handler.

**Observability from day one.** Eleven instruments (D12) answer the brain dump's questions directly: queue latency (`StartedAt − DueAt`) exposes backed-up queues and a poll interval that is too long; duration versus lease length exposes mis-sized leases; batch fill ratio exposes wrong batch sizes; lease losses expose slow handlers; the heartbeat gap exposes dead workers and restores.

### Lens C — correctness, security, maintainability

**Where could a stale cache leak?** (1) Permissions in a job scope: never taken from the enqueuing request; the job's first round trip performs the connect step for `RunAsUserId` (tag reads and `IsActive`) exactly as an HTTP request would, so a permission revoked between enqueue and run is honoured and a deactivated user's job fails closed with `user_inactive`. (2) SignalR group membership is fixed at connect: a user removed from the tenant mid-session keeps receiving thin events until the session-end hook closes their connections; the events carry ids only, and every follow-up fetch is a fresh, authorized HTTP call, so the leak is at most the fact that "something changed". (3) The handler registry is process-static and the tenant list is the tenant registry's cache: a removed tenant is polled until the cache refreshes (harmless), a newly provisioned tenant waits one refresh (bounded by that cache's lifetime, which the tenant registry owns). (4) No server-side cache exists for inbox counters; they are computed on demand and invalidated by events.

**Which write path bypasses a tag bump?** None of this theme's tables are served from a version-tagged cache, so there is nothing to bump. Should a future inbox or schedule cache appear, every statement this theme emits declares the tables it writes, so the batch emitter bumps the tag automatically without touching this code.

**No silent data loss.** A claim whose round trip fails after the `OUTPUT` rows were produced is discarded client-side (the rows are back to their pre-statement state on rollback). A lease lost to a slow handler cannot commit its completion: the fencing token makes the completion statement affect zero rows, and the worker reports `lease_lost` instead of success. At-least-once execution is the contract and is documented on `IJobHandler<T>`: handlers must be idempotent or checkpoint through `StateJson`. Dead-lettering never deletes; retention deletes only terminal rows older than the configured age and `SET NULL`s referencing entities.

**Concurrency soundness.** The database clock (`SYSUTCDATETIME()`) is the only clock in claim, renewal, completion and schedule statements; instance clocks never enter a comparison. Fencing: one `LeaseToken` per claim batch; renewal, progress, completion and release all carry it. Schedules fire exactly once per occurrence per tenant because the `core.ScheduleState` row is leased with the same hint set; there is no global coordination state anywhere.

**Fail closed.** Unknown handler key: the row stays `Pending`, is surfaced hourly in logs and on the admin page, and is never executed by a guess. Deactivated schedule owner: the schedule pauses (`IsActive = 0`, `PausedReason = 'owner_inactive'`) and administrators are notified. Admin retry of a dead-lettered job runs as the original `RunAsUserId`, never as the administrator.

**Schema evolution and N−1.** `core.Job` evolves by adding nullable columns; the UDTT hash changes and the migrator handles it as any table-type change. Handler keys are stable strings owned by the distro (`<slug>.<name>`) and `core.*` is reserved; argument JSON is read tolerantly (unknown members ignored, missing members defaulted), and a breaking argument change is a new key. During a rolling deploy the old instance simply does not claim keys it does not know, and the new instance processes both. Nothing in a distro breaks on a platform minor unless it depends on `JobContext` internals; the public surface is the interfaces in §3.

**Testability.** The worker takes a `TimeProvider`; the claim/renew/complete statements are emitted by one class with a LocalDB test per statement (RCSI on and off — the fixed facts say the two behave differently for a bare `READPAST`); Cronos is deterministic given a `TimeZoneInfo`; the hub is behind `IClientEventPublisher` with an in-memory fake.

## 1. Critique of the brain dump

**The general design is right in its instincts and wrong in one structural choice.** Per-row leasing, a handler registry, batch acquisition, nudging, tenant isolation, and multi-instance safety without a central table are all the correct shape and match what Hangfire, Azure Functions and Kubernetes do. The structural choice to "add a bunch of standard columns for leasing and progress on any entity that wants to track a background task" should be reversed, for four concrete reasons:

1. **Name collision.** An email outbox row already has a business status (queued, sent, bounced, delivered); an export request has a business status; a document has a workflow state. A second `Status` for the lease state forces prefixes (`WorkStatus`, `WorkDueAt`) on every table, and Queryex users see two statuses on one entity.
2. **Temporal churn and concurrency-token contamination.** The orchestrator's own hint says bookkeeping columns must live in sibling tables so that `ModifiedAt` can be the concurrency token and temporal history stays meaningful. Lease renewal writes every minute; on a temporal entity that is a history row per minute, and on any entity it is a write that must be carefully excluded from stamping.
3. **Prunability and one hot index.** A queue table whose terminal rows can be pruned keeps its filtered index tiny forever; lease columns on a business table can never be pruned without deleting the business row, and every such table needs its own filtered index and claim statement.
4. **One code path.** One table means one claim statement, one renewal, one completion, one admin page, one "my jobs" page, one retention job, one set of metrics. The entity-per-task shape multiplies each by the number of task-bearing tables.

The replacement is a single per-tenant `core.Job` table that business rows reference through a nullable FK (`JobId`). The email outbox, the export request and the import request each keep their own business columns and point at their job; the machinery never touches a business table.

**"Lease expiry math with a safety buffer" versus renewal.** The brain dump offers both and asks which. Every mature system renews (a sliding lease at one fifth of its length, carrying a fencing token) and none sizes a one-shot lease from a duration estimate, because estimates are wrong exactly when it matters. Renewal is the answer; the "safety buffer" survives only as the rule that the lease must be at least three renewal intervals long so that one missed renewal is not fatal.

**Gaps.** No overlap policy for schedules (every mature scheduler has one; default skip). No cancellation path for a running job. No checkpoint state for hours-long jobs, which makes the 5-second Linux App Service drain a data-loss hazard. No retention policy for terminal rows. No poison threshold or dead-letter state. No clock-source rule (instance clocks drift; the database clock must be the only one in comparisons). No handler idempotency contract (at-least-once is unavoidable with leases). No detection of jobs whose handler no longer exists after a deploy. No rule for which user a *user-triggered* job runs as when that user is deactivated before it runs. The scheduler section asks the right credential questions and the orchestrator's hint answers them correctly; this proposal adopts the hint.

**"CRON expressions can be stored anywhere."** They should not be. One `core.Schedule` table holds every schedule, built-in or user-created; a business entity that is "recurring" holds a `ScheduleId` FK, not a cron string. One table is what makes the tick a single claim statement, gives one admin page, one validation path (parse, time zone, handler key, run-as), one replay policy and one place to pause on owner deactivation.

**Inbox.** The seen/unread split is exactly right and common (GitHub, Slack). Storing "inbox tracking" on the user row is wrong for the same churn reason as `LastActive`; it is one timestamp, `InboxSeenAt`, on the user's non-temporal sibling table, and the two counters are computed with `TOP`-capped counts rather than maintained (maintained counters drift; a capped count over a filtered index costs microseconds). Missing from the brain dump: a per-type registry (what types exist, which can be muted, what they link to), deduplication (re-assigning the same document should not stack notifications), a target link so the click action is data rather than code, retention, and MCP access. The question "should completion of self-initiated tasks go into the inbox, and can it be muted" has a clean answer once exports are entities: the export row is the durable artifact, the notification is a convenience that cannot be muted for the inbox channel but can be for email.

**Traces.** The question is asked and answerable from the messaging semantic conventions: a job is a new root span of kind `Consumer` linked to the enqueuing request's context, never a child of it.

**Naming.** "Task" is unusable as a C# type family name; "Job" is the industry term. `Notification` beats `InboxItem` because the same row feeds email and push later and the page is called Notifications everywhere users have seen one. `Schedule` beats `CronJob` because users configure schedules; the job is what they produce.

**Internal inconsistency.** The save flow says "queuing background operations IS transactional" and the background section says "nudge the engine … so that it is processed immediately". Both are right and reconcile through a post-commit callback on the batch executor: the insert rides the transaction, the nudge and the hub event fire after commit. This needs a contract from the batch abstraction (§6, seam 1) that the brain dump does not mention.

**Email outbox.** Listed as the first consumer but specified later; the alignment rule this proposal fixes is that the outbox spec's rows carry `JobId` and never invent their own lease columns, so the outbox worker is just an `IJobHandler<EmailOutboxMessage>` with batch size 100.

## 2. Decisions

### D1 — One queue table per tenant database: `core.Job`; business rows reference it

**Decision.** Every unit of background work is a row of `core.Job` (schema in §4). An entity whose rows are processed in the background carries `JobId int NULL` (FK to `core.Job`, `ON DELETE SET NULL`) through the capability interface `IJobEntity`; the machinery loads such rows by joining the claimed job ids and never writes to the business table. Argument-only jobs (scheduled maintenance, recomputes) carry their input in `Job.ArgumentsJson` and no business row.

**Rationale.** The four reasons in §1: no status collision, no temporal/concurrency contamination of business tables, prunable terminal rows with one small filtered index, one code path for claim/renew/complete/admin/metrics.

**Alternatives rejected.** Standard columns on each task-bearing entity (the brain dump) — rejected for the reasons above. A polymorphic `(EntityType, EntityId)` reference from the job to the business row — rejected because it cannot be a database FK (fixed fact: every reference is an FK) and needs a `bigint` id column that lies about the entity's id type. A job row *inside* the outbox table only (Hangfire-style separate `JobQueue` plus `Job`) — two tables where one suffices, because app-assigned ids make the FK from the business row to the job settable before either insert.

**Confidence.** High. **Review flag:** the brain dump explicitly wanted columns on the entity; this is the single largest departure from it and should be confirmed.

### D2 — Names and keys

**Decision.** Namespace `Tellma.Core.Abstractions.Jobs` for contracts, `Tellma.Core.Jobs` for the runtime; tables `core.Job`, `core.Schedule`, `core.ScheduleState`, `core.JobWorkerState`. Handler keys are lowercase kebab-case `<slug>.<name>` (`etpharma.recompute-balances`); the `core.` prefix is reserved for platform handlers (`core.export`, `core.import`, `core.email-outbox`, `core.blob-staging-sweep`, `core.job-retention`); module packages use their module slug (`gl.`). Keys are validated at registration (`^[a-z][a-z0-9]*(\.[a-z0-9-]+)+$`, max 64) and must be stable for the life of a distro; a breaking argument change is a new key. Notification type keys use the same grammar (`core.export.ready`, `core.job.failed`, `core.schedule.paused`, `core.scheduler.gap`).

**Rationale.** Keys are persisted in rows and seeds; a grammar prevents collisions between packs and distros and lets the admin page group by prefix.

**Alternatives rejected.** CLR type names as keys (renames break rows). Integer handler ids (unreadable in SQL and logs).

**Confidence.** High.

### D3 — The handler contract: batch-shaped, one interface, options on an attribute

**Decision.** A handler implements `IJobHandler<TItem>` where `TItem` is either `Job` (argument-only jobs) or an entity class implementing `IJobEntity` (entity-backed jobs). Execution is batch-shaped: the worker hands the handler a `JobBatch<TItem>` of 1..`BatchSize` items claimed under one lease token, and the handler reports per-item outcomes. Options live on `[JobHandler]`:

| Option | Default | Meaning |
|---|---|---|
| `Key` | required | Handler key (D2). |
| `BatchSize` | 1 | Maximum items per claim; ≤ 500. |
| `LeaseSeconds` | 300 | Sliding lease length; renewal interval is one fifth; minimum 15. |
| `MaxAttempts` | 5 | Failure outcomes (not releases) after which the job is dead-lettered. |
| `MaxConcurrency` | 1 | Batches of this key in flight per tenant per instance. |
| `RetryBaseSeconds` / `RetryMaxSeconds` | 30 / 3600 | Exponential backoff with full jitter between attempts. |

Outcome rules: a normal return marks every item not explicitly marked as `Succeeded`; an exception marks every unmarked item `Retry` (or `Failed` when `Attempts ≥ MaxAttempts` or the exception is `JobFailedException`); `item.Retry(after, error)`, `item.Fail(error)` and `item.Succeed()` are explicit. The handler runs inside a tenant scope created for the job's `RunAsUserId` (seam 9), so `ISandboxContext`, the request context, culture, calendar and time zone resolve exactly as in a request. The contract is at-least-once; the XML doc on the interface says so and names `StateJson` as the checkpoint.

**Rationale.** One interface keeps the registry and executor to one code path; batch shape is what the outbox needs (one `IEmailSender.SendAsync` per claim) and costs a single-item handler nothing (`batch.Single`). Attribute options are the most mechanical thing a coding agent can write and are validated at registration, not at first claim.

**Alternatives rejected.** Two interfaces (`IJob` for arguments, `IJobHandler<T>` for entities): two registries, two executors. Options through a fluent registration: more code per handler for no gain; the attribute is one line. Static abstract members for options: harder to read in a registry listing.

**Confidence.** High.

### D4 — The claim statement, the filtered index, and the invariants that make them safe

**Decision.** State machine: `Pending → Running → Succeeded | Failed | Cancelled`, with `Running → Pending` on retry or release. Invariant enforced by a `CHECK`: `Status = 'Running'` if and only if `LeaseToken` and `LeaseExpiresAt` are not null. Availability predicate: `Status IN ('Pending','Running') AND DueAt <= now AND (LeaseExpiresAt IS NULL OR LeaseExpiresAt < now)`, which covers both never-claimed rows and abandoned leases. The claim, emitted by the platform (T2's batch machinery executes it; this theme owns the text):

```sql
DECLARE @now datetime2(3) = SYSUTCDATETIME();
DECLARE @claimed TABLE ([Id] int NOT NULL PRIMARY KEY);
WITH [due] AS (
    SELECT TOP (@n) [j].*
    FROM [core].[Job] AS [j] WITH (READPAST, UPDLOCK, ROWLOCK)
    WHERE [j].[Status] IN (N'Pending', N'Running')
      AND [j].[HandlerKey] = @key
      AND [j].[DueAt] <= @now
      AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < @now)
    ORDER BY [j].[DueAt], [j].[Id])
UPDATE [due]
SET [Status] = N'Running',
    [LeaseToken] = @token,
    [LeaseExpiresAt] = DATEADD(second, @leaseSeconds, @now),
    [LeaseOwner] = @owner,
    [Attempts] = [Attempts] + 1,
    [StartedAt] = COALESCE([StartedAt], @now)
OUTPUT [inserted].[Id] INTO @claimed;
SELECT [j].* FROM [core].[Job] AS [j] INNER JOIN @claimed AS [c] ON [c].[Id] = [j].[Id] ORDER BY [j].[DueAt], [j].[Id];
-- entity-backed handlers only; emitted from EF metadata by the data layer's SqlBuilder<T>:
SELECT <columns> FROM [core].[EmailOutboxMessage] AS [e] INNER JOIN @claimed AS [c] ON [c].[Id] = [e].[JobId];
```

Index: `IX_Job_Available ON core.Job (HandlerKey, DueAt, Id) INCLUDE (LeaseExpiresAt) WHERE Status IN (N'Pending', N'Running')`. The `IN` literals in the statement repeat the index filter verbatim so the optimizer can prove the query predicate is a subset of the filter; `@key`, `@n`, `@token`, `@leaseSeconds`, `@owner` are parameters. `@n ≤ 500`. The claim runs in its own command with no explicit transaction (each statement autocommits) at READ COMMITTED; the batch executor must not run it under SNAPSHOT isolation (error 650) and must pair `READPAST` with `UPDLOCK` because a bare `READPAST` is a no-op under RCSI (research §1.2). `core.Job` is created with `ALTER TABLE core.Job SET (LOCK_ESCALATION = DISABLE)` by the migrator and carries no triggers (an `OUTPUT` without `INTO` is illegal on triggered tables; we use `INTO`, but the rule keeps future statements safe). Errors 1205 and 1222 on the claim are retried after jitter by the executor's `MayRetry` path.

**Rationale.** Verified queue idiom (research §1.1, Hangfire's production fetch); the invariant lets one predicate serve fresh and abandoned rows without a separate "reaper"; the `INTO @claimed` + join keeps claim and entity load in one round trip; `SYSUTCDATETIME()` removes instance clock skew from every comparison.

**Alternatives rejected.** Status stays `Pending` while leased (Hangfire) — simpler index, but users then cannot see "Running" on their export without decoding lease columns. A separate reaper that resets expired leases to `Pending` — an extra statement and a race for nothing. `DELETE … OUTPUT` destructive dequeue — loses history and progress. `FORCESEEK` — kept out of the text; the filtered index makes a seek the only sensible plan, and the hint is illegal with index parameters on an UPDATE target; revisit if a plan regression is ever observed.

**Confidence.** High on the statement; medium on the optimizer matching an `IN`-filtered index to an `IN` predicate with extra conjuncts (research §8 lists the exact rule as unverified; the statement repeats the filter verbatim, which is the documented sufficient condition). **Review flag:** if a plan test shows the filtered index unused, fall back to `Status = N'Pending'` only plus a `LeaseExpiresAt IS NULL` invariant on pending rows.

### D5 — Sliding lease with a fencing token; renewal carries progress and returns cancellation

**Decision.** One `LeaseToken` (an app-generated `Guid`) per claim batch. The worker renews every `LeaseSeconds / 5` with one statement per tenant per instance that addresses every in-flight token at once and piggybacks buffered progress:

```sql
DECLARE @now datetime2(3) = SYSUTCDATETIME();
UPDATE [j]
SET [LeaseExpiresAt] = DATEADD(second, [t].[LeaseSeconds], @now),
    [ProgressPercent] = COALESCE([p].[ProgressPercent], [j].[ProgressPercent]),
    [ProgressMessage] = COALESCE([p].[ProgressMessage], [j].[ProgressMessage]),
    [StateJson] = COALESCE([p].[StateJson], [j].[StateJson])
OUTPUT [inserted].[Id], [inserted].[LeaseToken], [inserted].[CancelRequestedAt]
FROM [core].[Job] AS [j]
INNER JOIN @tokens AS [t] ON [t].[Id] = [j].[LeaseToken]          -- standalone type JobLeaseList (Id uniqueidentifier, LeaseSeconds int)
LEFT JOIN @progress AS [p] ON [p].[Id] = [j].[Id]                  -- standalone type JobProgressList
WHERE [j].[Status] = N'Running';
```

Any job of a token that is absent from the output has lost its lease (reclaimed by another instance after expiry, or cancelled/retried by an administrator): the worker cancels that batch's `CancellationToken`, counts `tellma.jobs.lease_lost`, and discards the batch's eventual outcomes (the completion statement is fenced anyway). A row whose `CancelRequestedAt` is not null also cancels the batch's token; the handler sees one `CancellationToken` for both causes and `batch.CancellationReason` to tell them apart. Progress is reported through `item.Progress.Report(percent, message, state)`: the values are buffered in memory and flushed with the next renewal, or immediately when the handler calls `FlushAsync` (a checkpoint before a risky step). Lease length per handler defaults to 300 s; the renewal statement is skipped for a tenant with no in-flight batches. A handler that ignores cancellation is abandoned after one further lease length (its task is left running with a logged warning and the row is re-claimable); the worker never blocks shutdown on it.

**Rationale.** Verified industry shape (research §1.3): sliding lease, renewal at one fifth, old stamp as the fence. Addressing renewal by token instead of id list makes the statement independent of batch size and lets one statement serve all batches. Progress on the renewal costs zero round trips.

**Alternatives rejected.** One-shot lease sized by a "safety buffer" (the brain dump's alternative): wrong whenever the estimate is wrong. Renewal per batch: N statements per interval. Rotating the token on every renewal (Hangfire rotates `FetchedAt`): more state to track for no additional safety — the token already proves continuity from the claim.

**Confidence.** High.

### D6 — Completion, retry backoff, poison handling, dead-lettering

**Decision.** One completion statement per batch, fenced by the token, driven by the standalone table type `JobOutcomeList (Id int, Status nvarchar(16), DueAt datetime2(3) NULL, ErrorCode nvarchar(64) NULL, ErrorMessage nvarchar(1024) NULL, ErrorDetails nvarchar(max) NULL, StateJson nvarchar(max) NULL)`:

```sql
DECLARE @now datetime2(3) = SYSUTCDATETIME();
UPDATE [j]
SET [Status] = [o].[Status],
    [DueAt] = COALESCE([o].[DueAt], [j].[DueAt]),
    [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL,
    [CompletedAt] = CASE WHEN [o].[Status] IN (N'Succeeded', N'Failed') THEN @now ELSE NULL END,
    [ErrorCode] = [o].[ErrorCode], [ErrorMessage] = [o].[ErrorMessage], [ErrorDetails] = [o].[ErrorDetails],
    [StateJson] = COALESCE([o].[StateJson], [j].[StateJson]),
    [ProgressPercent] = CASE WHEN [o].[Status] = N'Succeeded' THEN 100 ELSE [j].[ProgressPercent] END
OUTPUT [inserted].[Id], [inserted].[Status], [inserted].[RequestedById], [inserted].[Attempts]
FROM [core].[Job] AS [j] INNER JOIN @outcomes AS [o] ON [o].[Id] = [j].[Id]
WHERE [j].[LeaseToken] = @token AND [j].[Status] = N'Running';
```

Outcome mapping performed in C# before the statement: `Retry` → `Status = 'Pending'`, `DueAt = now + min(RetryMaxSeconds, RetryBaseSeconds × 2^(Attempts−1)) × U(0,1)` (full jitter) unless the handler supplied an explicit delay, and if `Attempts ≥ MaxAttempts` the outcome becomes `Failed`; `Failed` → terminal, `ErrorCode` required; `Succeeded` → terminal; `Released` (shutdown, lease lost before start) → `Status = 'Pending'`, `DueAt` unchanged, attempts untouched. The completion statement runs inside one transaction together with every statement the handler appended to `batch.Batch` (its own DB side effects, for example the outbox marking rows sent) and with the notifications the worker adds: on `Failed` a `core.job.failed` notification to `RequestedById` (when set) or to administrators (when null, meaning a system job), and on `Succeeded` whatever the handler requested through `item.NotifyOnSuccess(...)`. Poison rows stay in `core.Job` with `Status = 'Failed'`; the admin page offers `Retry` (sets `Pending`, `Attempts = 0`, `DueAt = now`, clears the error) and `Cancel` (sets `Cancelled` when `Pending`, or stamps `CancelRequestedAt` when `Running`).

**Rationale.** Per-row outcomes are what let a 100-email batch succeed for 97 rows and retry 3 (spec 0007's rule 2 depends on this). Full-jitter exponential backoff is the standard remedy for the immediate-redelivery footgun (research §1.3). Terminal rows are the audit trail; deleting them is retention's job (D13), not completion's.

**Alternatives rejected.** Batch-level outcome only: forces the outbox to re-send successes. Counting releases toward `MaxAttempts`: a three-hour export released by two deploys would dead-letter for no fault; releases are not failures. A separate dead-letter table: one more table and one more page for what a status value and a filter give.

**Confidence.** High.

### D7 — The worker: per-instance loop, per-tenant adaptive polling, local nudges, no cross-instance coordination

**Decision.** `JobWorker` is one `BackgroundService` per host process, implementing `IHostedLifecycleService` so `StoppingAsync` stops claiming before Kestrel finishes draining. It holds a `TenantPoller` per tenant listed by the tenant registry (refreshed on the registry's cadence). Each poller owns a coalescing wake-up channel (`Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite })`) and a `PeriodicTimer(interval, timeProvider)`; it waits on whichever fires first. The interval is adaptive: after a poll that claimed a full batch for any key, poll again immediately; after a poll that found nothing, double the interval from `MinPollInterval` (1 s) up to `MaxPollInterval` (30 s); a nudge resets it to the minimum. A poll is one round trip (§0, Lens B) that contains: the heartbeat update of `core.JobWorkerState`, one claim per registered key that has free concurrency slots on this instance for this tenant (`TOP (min(BatchSize, free))`), the due-schedule claim (D9), and the oldest-due sample (`SELECT MIN(DueAt) … WHERE Status = N'Pending' AND LeaseExpiresAt IS NULL`, one seek). Claimed batches run on the thread pool under a per-instance `SemaphoreSlim(MaxConcurrency, default 8)` and a per-(tenant, key) counter enforcing the handler's `MaxConcurrency`. A nudge is a local, in-process signal only: `IJobQueue.Enqueue` registers a post-commit callback that wakes the enqueuing instance's poller for that tenant, so the common case (the request that enqueued runs on an instance with capacity) is processed within milliseconds; other instances see the work on their next poll, bounded by `MaxPollInterval`. There is no central coordination table, no Redis, no `sp_getapplock`: correctness derives from the leased rows alone. On shutdown: `StoppingAsync` stops polling and cancels every batch's token; the worker waits up to `DrainTimeout` (default 4 s, deliberately under the 5 s Linux App Service limit) for handlers to return, then issues one release statement per tenant for every token still in flight (`UPDATE core.Job SET Status = N'Pending', LeaseToken = NULL, LeaseExpiresAt = NULL, LeaseOwner = NULL WHERE LeaseToken IN (SELECT Id FROM @tokens) AND Status = N'Running'`) and exits. Sandbox tenants are polled like live ones; the job scope's `ISandboxContext` reports the tenant's category. `JobWorkerOptions.Enabled = false` turns the worker off on a host (a distribution may later run a dedicated worker host with the same code).

**Rationale.** Multi-instance safety without shared state is a stated goal and the leased rows provide it; local nudges answer the "do not wait two minutes" requirement in the common case at zero infrastructure; adaptive polling bounds the cost of hundreds of idle tenants; the drain rule follows the verified App Service limits (research §2.3) — release beats graceful completion when the budget is 5 s.

**Alternatives rejected.** A cross-instance nudge through the SignalR backplane or Azure SignalR: abuses a client channel for server coordination and adds a dependency for a latency win that only matters when the local instance is saturated. `sp_getapplock` for "one poller per tenant": needs a held connection, fights pooling, and the row leases already serialize what matters. Quartz clustering: incompatible cron dialect, its own store, and it is pinned only for the identity server (fixed fact).

**Confidence.** High on the shape; medium on the default numbers (1 s / 30 s / 8), which are options and should be tuned with the metrics in D12.

### D8 — Enqueue rides the batch; nudge and hub events fire after commit

**Decision.** `IJobQueue.Enqueue(IBatchBuilder batch, IReadOnlyList<JobRequest> requests)` reserves ids from `sq_Job` through the allocator (seam 1), appends one `INSERT core.Job … SELECT … FROM @rows` (the `Job` UDTT, one row per request) to the caller's batch declaring that it writes `core.Job`, records the enqueuing request's `traceparent` on each row, and registers a post-commit callback that nudges the local poller for the tenant and the affected handler keys. The returned ids are known synchronously so the caller can set `JobId` on its entity rows before the same batch inserts them. `EnqueueAsync` (no batch) does the same in its own round trip. `JobRequest` carries `HandlerKey`, `Arguments` (an object serialized with the platform's JSON options, ≤ 64 KB after serialization), `DueAt` (null = now), `RequestedById` (null = nobody to notify), `RunAsUserId` (defaults to the current user in a request scope and must be given explicitly in a background scope). Validation at enqueue: the key must be registered on this instance (a distro cannot enqueue for a handler it did not ship), the run-as user must be set, arguments must serialize under the cap.

**Rationale.** "Queuing background operations is transactional" (brain dump) and "process immediately" (brain dump) both hold only with a post-commit hook; app-assigned ids are what make the FK from business rows to jobs settable inside one batch.

**Alternatives rejected.** Enqueue after commit from the service: loses transactionality (a crash between commit and enqueue loses the job). Enqueue through a trigger or a DB-side outbox table: logic in the database, banned.

**Confidence.** High.

### D9 — The scheduler: `core.Schedule` plus a state sibling, ticked inside the poll, Cronos, three policies and a gap safeguard

**Decision.** Schedules are ordinary editable top-level entities (`core.Schedule`, system-versioned, four audit columns, `IsActive` through the activatable capability so activate/deactivate come for free) with a non-temporal sibling `core.ScheduleState` holding `NextDueAt`, `LastFiredAt`, `LastScheduledFor`, `LastJobId`, `LastSkippedAt`, and the tick's lease columns. Cron expressions are five-field Unix cron parsed by **Cronos 0.13.0** (new CPM pin; MIT; the only library with documented time-zone and DST semantics — research §3); seconds are not supported because the poll ceiling is 30 s. `TimeZoneId` is an IANA or Windows id resolved with `TimeZoneInfo.FindSystemTimeZoneById`; null means the tenant's time zone from settings at fire time. Built-in schedules are seeded rows (`IsBuiltIn = 1`, reserved-band ids, `RunAsUserId` = the system user) declared by features through `jobs.AddBuiltInSchedule(...)`; on a built-in row only `CronExpression`, `TimeZoneId`, `IsActive`, `MissedPolicy`, `OverlapPolicy` and `CatchUpWindowMinutes` are editable and delete is refused.

The tick is two statements across the poll's round trip and one follow-up round trip, executed only when something is due:

1. In the poll: claim due state rows — `WITH due AS (SELECT TOP (50) s.* FROM core.ScheduleState s WITH (READPAST, UPDLOCK, ROWLOCK) WHERE s.NextDueAt IS NOT NULL AND s.NextDueAt <= @now AND (s.LeaseExpiresAt IS NULL OR s.LeaseExpiresAt < @now) ORDER BY s.NextDueAt) UPDATE due SET LeaseToken = @tickToken, LeaseExpiresAt = DATEADD(second, 30, @now) OUTPUT inserted.ScheduleId INTO @dueSchedules;` followed by `SELECT s.*, st.NextDueAt, st.LastScheduledFor, u.IsActive AS OwnerIsActive, @now AS Now FROM core.Schedule s JOIN core.ScheduleState st ON … JOIN @dueSchedules d ON … JOIN core.User u ON u.Id = s.RunAsUserId`.
2. In C#, per schedule: `occurrences = CronExpression.GetOccurrences(fromUtc: NextDueAt, toUtc: Now, zone, fromInclusive: true, toInclusive: true)`. Apply the **missed policy** when more than one occurrence is due: `Coalesce` (default) fires one job with `ScheduledFor = last occurrence`; `ReplayAll` fires one job per occurrence not older than `CatchUpWindowMinutes` (default 1440) with `ScheduledFor` set to each; `Skip` fires only when the last occurrence is within `2 × MaxPollInterval` of `Now` (it "just became due") and drops everything else. Apply the **global safeguard**: if `Now − NextDueAt` exceeds `SchedulerOptions.GapAlertThreshold` (default 6 h) — or the previous heartbeat in `core.JobWorkerState.LastTickAt` is older than that — every policy degrades to `Coalesce` for this tick, the number of dropped occurrences is logged, `tellma.schedules.gap_detected` increments, and one `core.scheduler.gap` notification goes to administrators. Apply the **overlap policy** at insert time as a predicate: `Skip` (default) inserts only when no job of this schedule is `Pending` or `Running` (`NOT EXISTS` over the filtered index `IX_Job_ScheduleActive`) and stamps `LastSkippedAt` otherwise; `Allow` always inserts. If `OwnerIsActive = 0` the schedule is paused instead (`IsActive = 0`, `PausedReason = 'owner_inactive'`) and a `core.schedule.paused` notification goes to administrators. Compute `NextDueAt = GetNextOccurrence(Now, zone)` (strictly after now, so a missed window is never replayed twice).
3. Second round trip, one transaction: `INSERT core.Job … FROM @jobs` (the `Job` UDTT; `ScheduleId`, `ScheduledFor`, `RunAsUserId = Schedule.RunAsUserId`, `RequestedById = NULL` for built-ins or the owner for user schedules), `UPDATE core.ScheduleState SET NextDueAt = …, LastFiredAt = @now, LastScheduledFor = …, LastJobId = …, LeaseToken = NULL, LeaseExpiresAt = NULL WHERE ScheduleId = … AND LeaseToken = @tickToken` (fenced), the pause updates, the notifications, then a post-commit nudge. If the second round trip fails, the 30-second tick lease expires and the next poll (on any instance) recomputes from the unchanged `NextDueAt` — at-least-once, and the overlap predicate prevents duplicates for `Skip` schedules.

Activating a schedule (through the CRUD pipeline's post-persist hook in the schedule service) sets `NextDueAt` to the next occurrence after now — the paused period is never caught up. Saving a schedule recomputes `NextDueAt` from the new expression. `core.JobWorkerState` (one row) is updated on every poll with `LastTickAt = SYSUTCDATETIME()` and the owner tag; it is the restore detector and the "worker alive" signal for the admin page.

**Rationale.** The three-way policy maps one-to-one onto Quartz, Hangfire, Airflow and Temporal vocabulary (research §6); the overlap policy the brain dump lacks is universal; the gap safeguard combines Temporal's window with Kubernetes' miss cut-off and alert; a per-tenant heartbeat detects a restored backup even when no schedule exists. Riding the tick on the poll gives every tenant a scheduler with zero extra round trips when nothing is due.

**Alternatives rejected.** Storing cron strings on business entities (brain dump's "anywhere"): no single validation, tick, or admin surface. Quartz: incompatible dialect and store. A `Queue` overlap policy (Temporal's `BufferOne`): achievable with `Allow` plus handler `MaxConcurrency = 1`; not worth a third value on day one. Catching up a paused period on activation (Kubernetes' behaviour): surprising for administrators who paused a report on purpose.

**Confidence.** High on the model; medium on `Skip`'s "just became due" definition. **Review flag:** whether `ReplayAll`'s window should default to 24 h or to the schedule's own interval.

### D10 — Credentials: system user for built-ins, owner for user schedules, requester for user jobs; permissions evaluated at run time

**Decision.** Every job carries `RunAsUserId` (not null). Built-in schedules run as the seeded **system user** (`WellKnownUsers.SystemUserId`, a `core.User` row with no `Subject`, `IsActive = 1`, cannot sign in, holds every permission — seam with T4). User-created schedules always run as their owner: `RunAsUserId` is server-set to the creator on insert and can be changed only by a user who could themselves run as the new value (the current user, or any user when the current user holds `core.schedules/impersonate`); a schedule cannot be made to run as the system user except by seeding. User-triggered jobs (export, import) run as the requester. In every case the job scope performs the connect step for `RunAsUserId` in its first round trip (tag reads, `IsActive`) and evaluates permissions through the permission evaluator exactly as a request would; a job whose run-as user is inactive fails with `ErrorCode = 'user_inactive'` without executing the handler. A schedule whose owner is deactivated pauses at its next tick (D9). Handlers of built-in system jobs must not read data on behalf of a person; they operate on platform tables (retention, sweeps) or on data the tenant as a whole owns.

**Rationale.** Answers the brain dump's two worries: built-in work has no natural human owner, and a user-configured schedule must not outlive the user's permissions nor grant more than they have; run-time evaluation with the owner as principal closes the "schedule a report on data I cannot see" hole by construction.

**Alternatives rejected.** Evaluating permissions at schedule time and caching a filter on the row: stale on every role change. A service account per schedule: heavy, and the identity server's service accounts are for external callers.

**Confidence.** High.

### D11 — Trace linkage: a new root per batch, linked to each job's enqueuing context

**Decision.** `IJobQueue` stores the current `Activity`'s W3C `traceparent` in `Job.TraceParent` at enqueue (55 characters; `tracestate` is not stored). The worker starts every batch as a **new root** activity — `ActivitySource("Tellma.Core").StartActivity("process " + key, ActivityKind.Consumer, parentContext: default, tags, links)` with one `ActivityLink` per claimed row that has a `TraceParent` — so the enqueuing request's trace closes when the request does and its sampling decision does not decide the job's. The claim is a `Client` span named `receive <key>`; the completion is a `Client` span `settle <key>`. Bounded tags only: `messaging.system = "tellma.jobs"`, `messaging.destination.name = <key>`, `messaging.operation.name ∈ {receive, process, settle}`, `messaging.batch.message_count`, `tellma.jobs.attempt`; the tenant id, job ids and user id go to the log scope, never to tags. Baggage is neither stored nor propagated into jobs; tenant, user, culture and time zone are explicit columns and scope values.

**Rationale.** Verified conventions (research §4): a span has one parent, a batch has many creation contexts, and a job that inherits the request trace keeps it "open" for hours. Azure Monitor serializes links into `_MS.links`; cross-trace navigation is a KQL join, which is acceptable and documented.

**Alternatives rejected.** Child-of-request spans: wrong for batches and for long jobs. Baggage propagation: security note in the OTel spec and the fixed fact that `AsyncLocal` must not carry tenant context.

**Confidence.** High.

### D12 — Telemetry names (constants in `Tellma.Core.Abstractions.Jobs.JobsTelemetry` and `…Notifications.NotificationsTelemetry`)

Meter `Tellma.Core`. Tag `handler` is the handler key (bounded: the registry is a closed set per deployment), `outcome ∈ {succeeded, retry, failed, released, lease_lost}`, `policy ∈ {coalesce, replay_all, skip}`, `type` is the notification type key, `event` the hub event name.

| Instrument | Kind, unit | Tags | Answers |
|---|---|---|---|
| `tellma.jobs.claimed` | counter, `{job}` | handler | throughput |
| `tellma.jobs.completed` | counter, `{job}` | handler, outcome | failure and dead-letter rate |
| `tellma.jobs.duration` | histogram, `s` | handler | lease sizing (compare with `LeaseSeconds`) |
| `tellma.jobs.queue_latency` | histogram, `s` (`StartedAt − DueAt`) | handler | backed-up queues, poll interval too long |
| `tellma.jobs.batch_fill` | histogram, `1` (claimed ÷ `BatchSize`) | handler | batch size mis-configured |
| `tellma.jobs.in_flight` | up-down counter, `{job}` | handler | concurrency |
| `tellma.jobs.lease_lost` | counter, `{batch}` | handler | handlers slower than their lease |
| `tellma.jobs.oldest_due_age` | histogram, `s` | — | starvation across handlers |
| `tellma.jobs.poll.duration` | histogram, `s` | — | poll cost per tenant |
| `tellma.jobs.orphaned` | counter, `{job}` | — | rows whose key has no handler on this instance (hourly scan) |
| `tellma.schedules.fired` | counter, `{job}` | policy | firing volume |
| `tellma.schedules.missed` | counter, `{occurrence}` | policy | dropped or coalesced occurrences |
| `tellma.schedules.overlap_skipped` | counter, `{firing}` | — | schedules whose interval is shorter than their run |
| `tellma.schedules.gap_detected` | counter, `{tick}` | — | restore or long outage |
| `tellma.notifications.created` | counter, `{notification}` | type | fan-out volume |
| `tellma.hub.events` | counter, `{event}` | event | push volume |

Alert queries under `infra/monitoring/` (cross-checked by the existing test): dead-letter rate, `oldest_due_age > 10 min`, `gap_detected > 0`, heartbeat older than 2 minutes (a log-based alert, since the heartbeat is per tenant).

**Confidence.** High on names (they follow the repo rule); medium on `batch_fill` earning its keep.

### D13 — First consumers: export, import, the blob staging sweep, retention, and the email outbox alignment

**Decision.**

*Export (with T9).* `core.Export` is a top-level entity owned by the Excel spec, minimal shape here: `Id`, `Resource` (securable resource name), `Kind ∈ {Display, ForImport}`, `RequestJson` (the query or the ids plus the select, verbatim), `FileName`, `BlobId` (null until produced; seam 12), `RowCount`, `ExpiresAt` (default now + 7 days), `JobId`, `CreatedAt`, `CreatedById`. The export endpoint decides synchronous versus background by the estimated row count (T9's threshold; the capped count from the query is already available): background means one batch inserting the `Export` row and its job (`core.export`, `RunAsUserId = RequestedById = current user`), returning `{ exportId, jobId }` in the same shape the synchronous path returns a file so the client has one code path. The `core.export` handler (batch size 1, lease 600 s) re-runs authorization in the job scope, streams pages of the query into the Excel writer, reports progress per page, writes the file through the blob service, and in its completion batch updates `Export.BlobId/RowCount`, completes the job, and raises `core.export.ready` to the requester (target `core.exports`, id = export id; inbox channel not mutable). The user's "My exports" page lists `core.Export` rows with the self-scope bespoke criterion `CreatedById = me()`; the file is served by the etag-validated blob endpoint under the same criterion — the notification is a convenience, the export row is the durable access path. A built-in schedule `core.export-retention` (daily, 03:00 tenant time) deletes expired exports and their blobs.

*Import (with T9).* `core.Import` mirrors it: `Id`, `Resource`, `Mode`, `FileBlobId` (the uploaded sheet, staged through the blob staging token), `ResultBlobId` (the annotated error sheet, null when clean), `RowCount`, `ErrorCount`, `JobId`, `CreatedAt`, `CreatedById`. Small files run inline; large files enqueue `core.import` (batch size 1, lease 600 s, `MaxAttempts = 1` because a partially committed import must not be re-run blindly). The handler runs the bulk pipeline as the requester and raises `core.import.completed` (with counts) or `core.import.failed`. Whether a large import commits per chunk or as one transaction is T9's decision; the job contract supports both through `StateJson` checkpoints.

*Blob staging sweep (with T7).* `core.blob-staging-sweep` is a built-in schedule (hourly) whose argument-only handler, shipped by the blob feature, deletes staged blobs older than the staging TTL in pages of 500 and reports progress. It runs as the system user.

*Retention.* `core.job-retention` (daily) deletes `Succeeded` jobs older than `JobsOptions.SucceededRetention` (30 days) and `Failed`/`Cancelled` older than `FailedRetention` (180 days) in pages of 1,000; referencing rows are `SET NULL`. `core.notification-retention` (daily) deletes read notifications older than 90 days and unread older than 365 days.

*Email outbox (later spec).* The outbox table carries `JobId` and implements `IJobEntity`; its worker is `IJobHandler<EmailOutboxMessage>` with `BatchSize = 100`, `LeaseSeconds = 120`, `MaxConcurrency = 1` per tenant (preserves order and quota pacing); `EmailEnqueueRequest.SendAfter` maps to `JobRequest.DueAt`; per-row outcomes map from `EmailSendResult`; its "signal after commit" is the post-commit nudge of D8. The reserved shape in spec 0007 §13 needs no change.

**Rationale.** Each consumer exercises a different corner: export is the single long job with progress and a notification; import is the non-retryable job; the sweep is the argument-only built-in schedule; retention keeps the queue table small; the outbox is the batch-shaped entity-backed handler.

**Confidence.** High on the shapes; the Excel-side thresholds are T9's.

### D14 — Notifications: the entity, the type registry, preferences, and `INotifier` riding the batch

**Decision.** `core.Notification` (schema §4) is one row per recipient, written only by the platform (`INotifier`), immutable except `ReadAt`. A notification stores its **type key**, its **arguments** (JSON, ≤ 4,000 characters), an optional **target** (`TargetResource` securable name + `TargetId`), an optional **actor** and an optional **dedup key**; it does not store rendered text. Rendering happens at read time from the type's ICU message in the reader's UI language (server-side for the summary endpoint, MCP and the future email digest; client-side for the SPA's list, which ships the same strings) — so a user who switches language sees old notifications in the new one and the insert needs no per-recipient language lookup. The **type registry** is populated once per feature (`notifications.AddType(key, category, mutable, targetResource)`), validated at startup (unique keys, grammar of D2), and exposed to the preferences page; `mutable = false` types (`core.export.ready`, `core.import.completed`, `core.import.failed`, `core.job.failed`, `core.schedule.paused`, `core.scheduler.gap`) cannot have their inbox channel disabled; email and push channels are always user-controlled. **Preferences** live in `core.NotificationPreference (UserId, Type, Inbox, Email, Push)`; a missing row means the type's defaults (inbox on, email off, push off). `INotifier.Notify(batch, requests)` appends, per request, one statement:

```sql
INSERT INTO [core].[Notification] ([Id], [UserId], [Type], [ArgumentsJson], [TargetResource], [TargetId], [ActorUserId], [DedupKey], [CreatedAt])
OUTPUT [inserted].[UserId]
SELECT [ids].[Id], [r].[Id], @type, @args, @targetResource, @targetId, @actorId, @dedupKey, SYSUTCDATETIME()
FROM @recipients AS [r]                                    -- IdList
INNER JOIN @ids AS [ids] ON [ids].[Ordinal] = [r].[Ordinal] -- pre-allocated ids, zipped by ordinal
WHERE (@mutable = 0 OR NOT EXISTS (SELECT 1 FROM [core].[NotificationPreference] AS [p]
                                   WHERE [p].[UserId] = [r].[Id] AND [p].[Type] = @type AND [p].[Inbox] = 0))
  AND (@dedupKey IS NULL OR NOT EXISTS (SELECT 1 FROM [core].[Notification] AS [n]
                                        WHERE [n].[UserId] = [r].[Id] AND [n].[DedupKey] = @dedupKey AND [n].[ReadAt] IS NULL));
```

and registers a post-commit callback that publishes the thin hub event `inbox.changed` to the user ids the `OUTPUT` returned. Recipients who are inactive are excluded by the same predicate (`AND EXISTS (SELECT 1 FROM core.User u WHERE u.Id = r.Id AND u.IsActive = 1)`). The email channel is added by the outbox spec as a second statement appended by the same call (an `INSERT` into the outbox from the same `@recipients` joined against `Email = 1` preferences), which is why the call is batch-shaped from day one.

**Rationale.** Zero extra round trips for fan-out (the brain dump's hard requirement for "assigned to you"); rendering at read time avoids a language lookup at save time and keeps rows language-neutral; the registry gives the preferences page and the mute rule one source; SQL-side preference and dedup predicates are declarative filtering, not logic in the database.

**Alternatives rejected.** Rendering the title at creation in the recipient's language (Slack-style): needs the recipient's language at save time (a lookup or a cache miss per recipient) and freezes the language. A single JSON "notification settings" blob on the user (brain dump's question): cannot be joined in the insert predicate. `InboxItem` as the name: the row outlives the inbox (email, push, MCP).

**Confidence.** High on the model; medium on read-time rendering. **Review flag:** the search page's message column cannot be a Queryex column under read-time rendering; the notifications page therefore renders the message client-side and filters on `Type`, `CreatedAt`, `ReadAt`, `Actor`, `TargetResource` — if a sortable/searchable rendered text column is required, add a `Title` column rendered in the tenant's primary language at creation (one extra column, no lookup) and keep arguments for the click action.

### D15 — Counters and the inbox API: computed with capped counts, one `InboxSeenAt` timestamp

**Decision.** The user's non-temporal sibling table (T4's) gains `InboxSeenAt datetime2(3) NULL`. Unseen count = notifications with `CreatedAt > InboxSeenAt` (or all when null); unread count = `ReadAt IS NULL`. Both are computed by `SELECT COUNT(*) FROM (SELECT TOP (100) 1 …)` (the badge shows "99+"), served by one endpoint returning `{ unseen, unread, latest: Notification[10] }`; the client calls it at load and on every `inbox.changed` event. `POST inbox/seen` sets `InboxSeenAt = SYSUTCDATETIME()`; `POST inbox/read { ids }` sets `ReadAt` where `UserId = me()` and `ReadAt IS NULL`; `POST inbox/read-all` does the same without ids. Each is one round trip and each publishes `inbox.changed` to the same user (other tabs and devices update). The notifications search page is the standard query endpoint over `core.Notification` with the bespoke self-scope criterion `UserId = me()` registered by the platform (no role permission is needed or honoured; there is no "read others' notifications" action). Indexes make every count a seek (§4).

**Rationale.** Counters on the user row churn (the brain dump's own worry); maintained counters drift and need reconciliation; a capped count over `(UserId, CreatedAt)` and the filtered `(UserId) WHERE ReadAt IS NULL` index is O(100).

**Alternatives rejected.** Maintained counters bumped by insert and mark-read: drift under retries and dedup skips. Counts on the connect call: per-request cost for a value that changes rarely; events invalidate better.

**Confidence.** High.

### D16 — The hub: one `TellmaHub`, per-(tenant, user) groups, thin events, an abstraction for publishers

**Decision.** One hub class, `TellmaHub`, mapped at `/{tenantId}/hub` (route ownership T6) with the cookie scheme; the negotiate/connect request runs tenant resolution and the connect step exactly like an API request, refuses non-members, and `OnConnectedAsync` adds the connection to the group `t{tenantId}.u{userId}` — computed server-side from the authenticated context, never from client input, which is what makes groups safe here despite "groups are not a security feature". The `IUserIdProvider` returns the identity `sub` so the per-user close APIs (spec 0003 §7.4) address the right connections; self-hosted, a `ConnectionTracker` maps `(tenantId, userId) → connection ids` for the same purpose. Publishers never see SignalR: `IClientEventPublisher.Publish(batch?, ClientEvent)` (post-commit when a batch is given, immediate otherwise) sends `Clients.Groups(groupNames).SendAsync("event", { name, tenantId, payload })` where payload is ids only. Event catalogue registered like notification types (`events.Add("inbox.changed")`, `"job.changed"` with `{ jobId }`, `"cache.changed"` with `{ tag }` for T3's cacheable-entity invalidation, `"session.ended"`); the client re-fetches through HTTP for anything else. `CloseOnAuthenticationExpiration = true`. Hosting: `AddAzureSignalR()` when `Azure:SignalR:ConnectionString` is configured (SaaS; `ClaimsProvider` pruned to `sub` per spec 0003), otherwise in-process SignalR; on-prem with two or more instances requires `AddStackExchangeRedis` with a channel prefix equal to the `DeploymentId` (the seam records this as a hosting requirement; a single on-prem instance needs nothing). `IHubConnectionCloser.CloseAsync(tenantId, userId)` is the contract T1 calls on session end (Azure: `users/{sub}/:closeConnections` through the SDK's `IHubContext`… fallback to the Management SDK only for hub-less processes; self-hosted: abort tracked connections).

**Rationale.** Spec 0003's rules (cookie at connect, thin events, close on session end, hub-only Azure token) are honoured verbatim; per-(tenant, user) groups give tenant isolation without a hub per tenant; the abstraction keeps SignalR out of `Tellma.Core.Abstractions` (framework-free) and out of job handlers.

**Alternatives rejected.** `Clients.User(sub)`: a user open in two tenants would receive both tenants' signals. A hub per tenant: N hubs × Azure SignalR cost and configuration for nothing groups do not give. Server-to-server pub-sub over the hub (D7): rejected.

**Confidence.** High.

### D17 — Split: 0019 is jobs and the scheduler; 0020 is notifications and the hub

**Decision.** Two specs. 0019 owns D1–D13 and the `core.Job`, `core.Schedule`, `core.ScheduleState`, `core.JobWorkerState` tables; 0020 owns D14–D16 and `core.Notification`, `core.NotificationPreference`, the hub, and the `InboxSeenAt` column. The only dependency from 0019 to 0020 is `INotifier` (used by the worker for `core.job.failed` and by handlers) and `IClientEventPublisher` (for `job.changed`), both contracts in Abstractions; 0019 can ship with a no-op notifier and land 0020 the next week. 0020's dependencies on 0019 are none.

**Rationale.** The theme is two audiences (operators and consumers of background work; end users and the SPA), two hosting concerns (Always On and drain; Azure SignalR and Redis), and together too large for one frozen spec; the seam is two small interfaces.

**Confidence.** High.

### D18 — Packaging and pins

**Decision.** Contracts in `Tellma.Core.Abstractions` under `Jobs`, `Notifications`, `Realtime` (BCL only: `System.Text.Json` for `JsonElement`, no SignalR, no EF). Runtime in the stack package (`Tellma.Core`, namespaces `Tellma.Core.Jobs`, `Tellma.Core.Notifications`, `Tellma.Core.Realtime`), which already references ASP.NET Core; `Microsoft.AspNetCore.SignalR` is in the shared framework. New CPM pins: `Cronos` 0.13.0; `Microsoft.Azure.SignalR` 1.33.1 and `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.11 are referenced by the reference distribution's Web project, not by `Tellma.Core`, since the hosting choice is the distribution's. Tests: `test/core/Tellma.Core.Tests` for the emitter statements against LocalDB (RCSI on and off), the worker with `FakeTimeProvider`, Cronos policies with fixed zones; `*.IntegrationTests` (`Category=Integration`) for Azure SignalR against a real service, `Live=true`.

**Confidence.** High.

### D19 — Securables, endpoints and pages this theme registers

**Decision.** Resources and actions (registered by the platform feature, T4's registry): `core.jobs` — `read` (administrators; supports filter), `retry`, `cancel`; self-scope bespoke criterion `RequestedById = me()` grants `read` and `cancel` on one's own jobs without a role. `core.schedules` — `read`, `save`, `delete`, `activate` (filterable), `impersonate` (D10). `core.notifications` — self-scope only. Endpoints (T6 projects them): the standard CRUD set for schedules; query, details, `retry`, `cancel` for jobs; `inbox/summary`, `inbox/seen`, `inbox/read`, `inbox/read-all`, query for notifications; `notification-preferences` get/save (self). Pages: Schedules (standard search + details), Background jobs (admin search with status/handler/age filters and the worker heartbeat banner), My jobs, Notifications (standard search), Notification preferences.

**Confidence.** High.

## 3. Contracts

### 3.1 Owned by this theme — `Tellma.Core.Abstractions.Jobs`

```csharp
namespace Tellma.Core.Abstractions.Jobs;

/// <summary>Lifecycle states of a <see cref="Job"/>; stored as strings so Queryex filters read <c>Status = 'Running'</c>.</summary>
public enum JobStatus
{
    /// <summary>Waiting to be claimed; <c>DueAt</c> may be in the future (scheduled or retrying).</summary>
    Pending,
    /// <summary>Claimed under a lease; the lease may have expired, in which case the job is re-claimable.</summary>
    Running,
    /// <summary>Terminal success.</summary>
    Succeeded,
    /// <summary>Terminal failure after <c>MaxAttempts</c> or an explicit permanent failure (dead-lettered).</summary>
    Failed,
    /// <summary>Terminal; cancelled by a user before it ran or by request while it ran.</summary>
    Cancelled,
}

/// <summary>
///     One durable unit of background work in the tenant database. Rows are written only by the jobs runtime;
///     business entities that need background processing reference a job through <see cref="IJobEntity"/>.
/// </summary>
[TableType]
public class Job
{
    /// <summary>App-assigned id from the <c>sq_Job</c> sequence.</summary>
    public int Id { get; set; }
    /// <summary>The handler key (<c>&lt;slug&gt;.&lt;name&gt;</c>) that executes this job.</summary>
    public string HandlerKey { get; set; } = "";
    /// <summary>Current lifecycle state.</summary>
    public JobStatus Status { get; set; }
    /// <summary>Earliest time (UTC) the job may be claimed; moved forward by retry backoff.</summary>
    public DateTime DueAt { get; set; }
    /// <summary>Number of claims so far; incremented by the claim statement.</summary>
    public int Attempts { get; set; }
    /// <summary>Fencing token of the current lease; null unless <see cref="Status"/> is <see cref="JobStatus.Running"/>.</summary>
    public Guid? LeaseToken { get; set; }
    /// <summary>UTC expiry of the current lease; null unless running.</summary>
    public DateTime? LeaseExpiresAt { get; set; }
    /// <summary>Diagnostic tag of the leasing instance (deployment id, instance id, process start); never used for coordination.</summary>
    public string? LeaseOwner { get; set; }
    /// <summary>UTC time of the first claim.</summary>
    public DateTime? StartedAt { get; set; }
    /// <summary>UTC time the job reached <see cref="JobStatus.Succeeded"/> or <see cref="JobStatus.Failed"/>.</summary>
    public DateTime? CompletedAt { get; set; }
    /// <summary>Set by a cancel request while running; the next renewal cancels the handler.</summary>
    public DateTime? CancelRequestedAt { get; set; }
    /// <summary>Handler arguments as JSON (≤ 64 KB); null for entity-backed jobs.</summary>
    public string? ArgumentsJson { get; set; }
    /// <summary>Handler-owned checkpoint state, written with progress flushes and read on the next attempt.</summary>
    public string? StateJson { get; set; }
    /// <summary>Last reported completion percentage, 0–100.</summary>
    public byte? ProgressPercent { get; set; }
    /// <summary>Last reported progress message (not localized; handler-supplied).</summary>
    public string? ProgressMessage { get; set; }
    /// <summary>Stable error code of the last failure (for example <c>user_inactive</c>).</summary>
    public string? ErrorCode { get; set; }
    /// <summary>Human-readable message of the last failure.</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>Diagnostics dump of the last failure (exception text, handler details).</summary>
    public string? ErrorDetails { get; set; }
    /// <summary>W3C <c>traceparent</c> of the enqueuing operation, linked from the processing span.</summary>
    public string? TraceParent { get; set; }
    /// <summary>User whose identity and permissions the handler runs under.</summary>
    public int RunAsUserId { get; set; }
    /// <summary>User to notify about completion or failure; null for system work (administrators are notified on failure).</summary>
    public int? RequestedById { get; set; }
    /// <summary>The schedule that produced this job, if any.</summary>
    public int? ScheduleId { get; set; }
    /// <summary>The cron occurrence this job stands for; set for scheduled jobs (under <c>ReplayAll</c>, one per occurrence).</summary>
    public DateTime? ScheduledFor { get; set; }
    /// <summary>UTC insertion time.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Capability of an entity whose rows are processed in the background: the row references its job.</summary>
public interface IJobEntity
{
    /// <summary>The job that processes this row; null when none is pending. The FK is <c>ON DELETE SET NULL</c>.</summary>
    int? JobId { get; set; }
}

/// <summary>Declares a class as the handler of one handler key and fixes its execution options.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JobHandlerAttribute(string key) : Attribute
{
    /// <summary>The handler key; lowercase kebab-case <c>&lt;slug&gt;.&lt;name&gt;</c>, <c>core.</c> reserved.</summary>
    public string Key { get; } = key;
    /// <summary>Maximum items per claim (1–500).</summary>
    public int BatchSize { get; init; } = 1;
    /// <summary>Sliding lease length in seconds (≥ 15); renewed at one fifth.</summary>
    public int LeaseSeconds { get; init; } = 300;
    /// <summary>Failure outcomes after which a job is dead-lettered; releases do not count.</summary>
    public int MaxAttempts { get; init; } = 5;
    /// <summary>Batches of this key in flight per tenant per instance.</summary>
    public int MaxConcurrency { get; init; } = 1;
    /// <summary>Base of the exponential retry backoff, seconds.</summary>
    public int RetryBaseSeconds { get; init; } = 30;
    /// <summary>Cap of the retry backoff, seconds.</summary>
    public int RetryMaxSeconds { get; init; } = 3600;
}

/// <summary>
///     Executes claimed jobs of one handler key. <typeparamref name="TItem"/> is <see cref="Job"/> for argument-only
///     jobs or an entity implementing <see cref="IJobEntity"/> for entity-backed jobs. Execution is at-least-once:
///     a lost lease re-runs the item on another instance, so handlers must be idempotent or checkpoint through
///     <see cref="JobItem{TItem}.Progress"/>. Handlers run inside a tenant scope for the job's run-as user.
/// </summary>
public interface IJobHandler<TItem> where TItem : class
{
    /// <summary>Processes one claimed batch; unmarked items succeed on return and retry on exception.</summary>
    Task ExecuteAsync(JobBatch<TItem> batch, CancellationToken cancellationToken);
}

/// <summary>A batch of items claimed under one lease token, with the batch builder for transactional side effects.</summary>
public sealed class JobBatch<TItem> where TItem : class
{
    /// <summary>The items, in claim order.</summary>
    public IReadOnlyList<JobItem<TItem>> Items { get; }
    /// <summary>The single item of a batch-size-1 handler; throws when the batch holds several.</summary>
    public JobItem<TItem> Single { get; }
    /// <summary>Statements appended here execute in the completion transaction (seam 1).</summary>
    public IBatchBuilder Batch { get; }
    /// <summary>Why the cancellation token fired: none, lease lost, cancel requested, or host shutdown.</summary>
    public JobCancellationReason CancellationReason { get; }
}

/// <summary>One claimed item with its job row, typed arguments, progress reporter and outcome markers.</summary>
public sealed class JobItem<TItem> where TItem : class
{
    /// <summary>The job row as claimed.</summary>
    public Job Job { get; }
    /// <summary>The business row for entity-backed handlers, or the job itself when <typeparamref name="TItem"/> is <see cref="Job"/>.</summary>
    public TItem Item { get; }
    /// <summary>Deserializes <see cref="Job.ArgumentsJson"/> tolerantly (unknown members ignored).</summary>
    public TArgs Arguments<TArgs>();
    /// <summary>Deserializes <see cref="Job.StateJson"/>; default when none.</summary>
    public TState? State<TState>();
    /// <summary>Buffered progress; flushed with lease renewal or on demand.</summary>
    public IJobProgress Progress { get; }
    /// <summary>Marks the item successful (the default on normal return).</summary>
    public void Succeed();
    /// <summary>Requests a retry after an optional delay with an optional error; dead-letters when attempts are exhausted.</summary>
    public void Retry(TimeSpan? after = null, JobError? error = null);
    /// <summary>Marks the item permanently failed.</summary>
    public void Fail(JobError error);
    /// <summary>Requests a notification to <see cref="Job.RequestedById"/> in the completion transaction on success.</summary>
    public void NotifyOnSuccess(NotificationRequest request);
}

/// <summary>Progress and checkpoint reporting for a running item.</summary>
public interface IJobProgress
{
    /// <summary>Records progress; the values ride the next lease renewal.</summary>
    void Report(byte? percent, string? message = null, object? state = null);
    /// <summary>Writes buffered progress immediately (a checkpoint before a non-idempotent step).</summary>
    Task FlushAsync(CancellationToken cancellationToken);
}

/// <summary>A stable code plus message and optional details describing a job failure.</summary>
public sealed record JobError(string Code, string Message, string? Details = null);

/// <summary>Why a batch's cancellation token fired.</summary>
public enum JobCancellationReason { None, LeaseLost, CancelRequested, HostStopping }

/// <summary>Thrown by a handler to fail the whole batch permanently regardless of remaining attempts.</summary>
public sealed class JobFailedException(JobError error) : Exception(error.Message)
{
    /// <summary>The failure recorded on every unmarked item.</summary>
    public JobError Error { get; } = error;
}

/// <summary>A request to enqueue one job.</summary>
/// <param name="HandlerKey">Registered handler key.</param>
/// <param name="Arguments">Serialized into <see cref="Job.ArgumentsJson"/>; null for entity-backed jobs.</param>
/// <param name="DueAt">Earliest run time; null means now.</param>
/// <param name="RequestedById">User to notify; null for system work.</param>
/// <param name="RunAsUserId">Identity to run under; defaults to the current user in a request scope, required otherwise.</param>
public sealed record JobRequest(string HandlerKey, object? Arguments = null, DateTimeOffset? DueAt = null, int? RequestedById = null, int? RunAsUserId = null);

/// <summary>Durable, transactional enqueuing of jobs; the insert joins the caller's batch and the nudge fires after commit.</summary>
public interface IJobQueue
{
    /// <summary>Appends the inserts to <paramref name="batch"/> and returns the app-assigned job ids in request order.</summary>
    IReadOnlyList<int> Enqueue(IBatchBuilder batch, IReadOnlyList<JobRequest> requests);
    /// <summary>Enqueues in its own round trip, for callers outside a batch.</summary>
    Task<IReadOnlyList<int>> EnqueueAsync(IReadOnlyList<JobRequest> requests, CancellationToken cancellationToken);
}

/// <summary>Feature-time registration of handlers and built-in schedules (composition seam, T1).</summary>
public interface IJobsRegistrar
{
    /// <summary>Registers a handler class carrying <see cref="JobHandlerAttribute"/>; validated at startup.</summary>
    IJobsRegistrar AddHandler<THandler>() where THandler : class;
    /// <summary>Declares a seeded built-in schedule for a registered key; the row lives in the reserved id band.</summary>
    IJobsRegistrar AddBuiltInSchedule(string handlerKey, string cron, object? arguments = null, string? timeZoneId = null);
}

/// <summary>Instrument and tag names (see D12).</summary>
public static class JobsTelemetry
{
    public const string MeterName = "Tellma.Core";
    public const string Claimed = "tellma.jobs.claimed";
    public const string Completed = "tellma.jobs.completed";
    public const string Duration = "tellma.jobs.duration";
    public const string QueueLatency = "tellma.jobs.queue_latency";
    public const string BatchFill = "tellma.jobs.batch_fill";
    public const string InFlight = "tellma.jobs.in_flight";
    public const string LeaseLost = "tellma.jobs.lease_lost";
    public const string OldestDueAge = "tellma.jobs.oldest_due_age";
    public const string PollDuration = "tellma.jobs.poll.duration";
    public const string Orphaned = "tellma.jobs.orphaned";
    public const string SchedulesFired = "tellma.schedules.fired";
    public const string SchedulesMissed = "tellma.schedules.missed";
    public const string SchedulesOverlapSkipped = "tellma.schedules.overlap_skipped";
    public const string SchedulesGapDetected = "tellma.schedules.gap_detected";
    public const string TagHandler = "handler";
    public const string TagOutcome = "outcome";
    public const string TagPolicy = "policy";
}
```

Schedule entity, options, and the scheduler-side enums:

```csharp
namespace Tellma.Core.Abstractions.Jobs;

/// <summary>What to do when more than one cron occurrence is due at a tick (downtime, restore).</summary>
public enum MissedPolicy
{
    /// <summary>Fire once for the latest occurrence (default; Quartz FireOnceNow, Hangfire Relaxed).</summary>
    Coalesce,
    /// <summary>Fire once per occurrence inside the catch-up window, each with its <c>ScheduledFor</c>.</summary>
    ReplayAll,
    /// <summary>Fire only an occurrence that just became due; drop the rest.</summary>
    Skip,
}

/// <summary>What to do when the schedule is due while its previous job is still pending or running.</summary>
public enum OverlapPolicy
{
    /// <summary>Do not fire; stamp <c>LastSkippedAt</c> (default).</summary>
    Skip,
    /// <summary>Fire anyway; the handler's <c>MaxConcurrency</c> decides whether the runs overlap.</summary>
    Allow,
}

/// <summary>A cron schedule that creates jobs when due. Editable top-level entity (temporal, audited, activatable, multilingual).</summary>
[TableType]
public class Schedule : IActivatable, IAudited, IMultilingual   // T2's capability interfaces
{
    public int Id { get; set; }
    /// <summary>Stable code; required and unique for built-in schedules (equals the handler key), optional otherwise.</summary>
    public string? Code { get; set; }
    public string Name { get; set; } = "";
    public string? Name2 { get; set; }
    public string? Name3 { get; set; }
    /// <summary>Registered handler key; write-once on built-in rows.</summary>
    public string HandlerKey { get; set; } = "";
    /// <summary>Arguments passed to every job the schedule creates.</summary>
    public string? ArgumentsJson { get; set; }
    /// <summary>Five-field Unix cron expression (Cronos dialect: L, W, #, ?, H supported; no seconds).</summary>
    public string CronExpression { get; set; } = "";
    /// <summary>IANA or Windows zone id; null means the tenant's time zone at fire time.</summary>
    public string? TimeZoneId { get; set; }
    public MissedPolicy MissedPolicy { get; set; }
    public OverlapPolicy OverlapPolicy { get; set; }
    /// <summary>Occurrences older than this are never replayed (default 1440).</summary>
    public int CatchUpWindowMinutes { get; set; } = 1440;
    /// <summary>User the jobs run as; the system user for built-ins, the owner otherwise (server-enforced).</summary>
    public int RunAsUserId { get; set; }
    /// <summary>Seeded by a feature; delete refused, key and run-as write-once.</summary>
    public bool IsBuiltIn { get; set; }
    /// <summary>Set by the runtime when it deactivates the schedule (<c>owner_inactive</c>); cleared on activation.</summary>
    public string? PausedReason { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedById { get; set; }
    public DateTime ModifiedAt { get; set; }
    public int ModifiedById { get; set; }
}

/// <summary>Host-level options for the worker and scheduler; bound from <c>Tellma:Jobs</c>.</summary>
public sealed class JobsOptions
{
    /// <summary>Run the worker in this host (false for API-only hosts when a dedicated worker exists).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Batches in flight per instance across all tenants and keys.</summary>
    public int MaxConcurrency { get; set; } = 8;
    public TimeSpan MinPollInterval { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>Also the cron granularity ceiling; must be ≤ 30 s.</summary>
    public TimeSpan MaxPollInterval { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Time given to handlers to observe cancellation before leases are released on shutdown.</summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(4);
    /// <summary>Gap between a schedule's due time (or the last heartbeat) and now beyond which every missed policy coalesces and administrators are alerted.</summary>
    public TimeSpan GapAlertThreshold { get; set; } = TimeSpan.FromHours(6);
    public TimeSpan SucceededRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan FailedRetention { get; set; } = TimeSpan.FromDays(180);
}
```

### 3.2 Owned by this theme — `Tellma.Core.Abstractions.Notifications` and `…Realtime`

```csharp
namespace Tellma.Core.Abstractions.Notifications;

/// <summary>One notification addressed to one user. Written only by <see cref="INotifier"/>; immutable except <see cref="ReadAt"/>.</summary>
[TableType]
public class Notification
{
    public int Id { get; set; }
    /// <summary>Recipient.</summary>
    public int UserId { get; set; }
    /// <summary>Registered type key (<c>core.export.ready</c>); selects the message template, category and mute rule.</summary>
    public string Type { get; set; } = "";
    /// <summary>Template arguments as JSON (≤ 4,000 characters).</summary>
    public string? ArgumentsJson { get; set; }
    /// <summary>Securable resource of the click target (<c>core.exports</c>); the client maps it to a route.</summary>
    public string? TargetResource { get; set; }
    /// <summary>Id of the click target.</summary>
    public long? TargetId { get; set; }
    /// <summary>User whose action caused the notification, if any.</summary>
    public int? ActorUserId { get; set; }
    /// <summary>Suppresses a second unread notification with the same key for the same user.</summary>
    public string? DedupKey { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Null while unread.</summary>
    public DateTime? ReadAt { get; set; }
}

/// <summary>Per-user, per-type channel choices; a missing row means the type's defaults.</summary>
[TableType]
public class NotificationPreference
{
    public int UserId { get; set; }
    public string Type { get; set; } = "";
    /// <summary>Ignored (always on) for non-mutable types.</summary>
    public bool Inbox { get; set; } = true;
    public bool Email { get; set; }
    public bool Push { get; set; }
}

/// <summary>Describes one notification type; registered once per feature and validated at startup.</summary>
/// <param name="Key">Type key, D2 grammar.</param>
/// <param name="Category">Grouping on the preferences page (resource key).</param>
/// <param name="Mutable">False when the inbox channel cannot be switched off.</param>
/// <param name="TargetResource">The securable resource notifications of this type link to, if fixed.</param>
public sealed record NotificationTypeDescriptor(string Key, string Category, bool Mutable, string? TargetResource = null);

/// <summary>A request to notify a set of users; one row per recipient is inserted subject to preferences and dedup.</summary>
public sealed record NotificationRequest(
    string Type,
    IReadOnlyList<int> RecipientIds,
    object? Arguments = null,
    string? TargetResource = null,
    long? TargetId = null,
    int? ActorUserId = null,
    string? DedupKey = null);

/// <summary>Creates notifications inside the caller's batch (zero extra round trips) and pushes <c>inbox.changed</c> after commit.</summary>
public interface INotifier
{
    /// <summary>Appends the inserts to <paramref name="batch"/>.</summary>
    void Notify(IBatchBuilder batch, IReadOnlyList<NotificationRequest> requests);
    /// <summary>Notifies in its own round trip, for callers outside a batch.</summary>
    Task NotifyAsync(IReadOnlyList<NotificationRequest> requests, CancellationToken cancellationToken);
}

/// <summary>Feature-time registration of notification types.</summary>
public interface INotificationsRegistrar
{
    INotificationsRegistrar AddType(NotificationTypeDescriptor descriptor);
}

/// <summary>Renders a notification's message for a reader (server-side: summary endpoint, MCP, email digest).</summary>
public interface INotificationRenderer
{
    /// <summary>Renders title text from the type's ICU template in the reader's UI language.</summary>
    string Render(Notification notification, System.Globalization.CultureInfo uiCulture);
}

/// <summary>Inbox counters and the latest items, one round trip.</summary>
/// <param name="Unseen">Notifications newer than the user's <c>InboxSeenAt</c>, capped at 100.</param>
/// <param name="Unread">Unread notifications, capped at 100.</param>
/// <param name="Latest">The latest ten notifications, newest first.</param>
public sealed record InboxSummary(int Unseen, int Unread, IReadOnlyList<Notification> Latest);
```

```csharp
namespace Tellma.Core.Abstractions.Realtime;

/// <summary>A thin event for the SPA: a name and ids, never data.</summary>
/// <param name="Name">Registered event name (<c>inbox.changed</c>, <c>job.changed</c>, <c>cache.changed</c>).</param>
/// <param name="UserIds">Recipients within the current tenant; empty means every connected user of the tenant.</param>
/// <param name="Payload">Ids only; serialized with the platform JSON options.</param>
public sealed record ClientEvent(string Name, IReadOnlyList<int> UserIds, object? Payload = null);

/// <summary>Publishes thin events to connected clients of the current tenant, after commit when a batch is given.</summary>
public interface IClientEventPublisher
{
    /// <summary>Registers the event on the batch's post-commit hook, or publishes immediately when <paramref name="batch"/> is null.</summary>
    void Publish(IBatchBuilder? batch, ClientEvent clientEvent);
    /// <summary>Publishes now.</summary>
    Task PublishAsync(ClientEvent clientEvent, CancellationToken cancellationToken);
}

/// <summary>Closes a user's real-time connections in one tenant; called by the session-end hooks.</summary>
public interface IHubConnectionCloser
{
    Task CloseAsync(int tenantId, int userId, CancellationToken cancellationToken);
}
```

### 3.3 Needed from other themes' seams (the exact members this theme calls)

```csharp
// Seam 1 — batch abstraction (T2). This theme needs these members and nothing more.
public interface IBatchBuilder
{
    /// <summary>Appends raw SQL; <paramref name="writes"/> names the tables written so the emitter can bump tags.</summary>
    IBatchResultHandle AddSql(string sql, IReadOnlyList<SqlParameterSpec> parameters, bool mayRetry, IReadOnlyList<string> writes);
    /// <summary>Binds a TVP of a registered standalone or table-derived type (metadata-driven ordinals).</summary>
    SqlParameterSpec Tvp<TRow>(string name, IReadOnlyList<TRow> rows);
    /// <summary>Reserves app-assigned ids for a table's sequence; may consume the allocator's buffer or ride the batch.</summary>
    IReadOnlyList<int> ReserveIds<TEntity>(int count);
    /// <summary>Runs after a successful commit, outside the transaction; failures are logged, never propagated to the caller.</summary>
    void OnCommitted(Func<IBatchResults, CancellationToken, Task> callback);
    /// <summary>Emits <c>SELECT &lt;columns&gt; FROM &lt;table&gt; WHERE &lt;fk&gt; IN (SELECT Id FROM @tableVariable)</c> from EF metadata and materializes rows.</summary>
    IBatchResultHandle<IReadOnlyList<TEntity>> SelectByJoin<TEntity>(string fkColumn, string tableVariable) where TEntity : class;
}
public interface IBatchExecutor
{
    /// <summary>Executes the batch; <paramref name="transactional"/> wraps it in one READ COMMITTED transaction.</summary>
    Task<IBatchResults> ExecuteAsync(IBatchBuilder batch, bool transactional, CancellationToken cancellationToken);
}

// Seam 9 — request context in background scopes (T1).
public interface ITenantScopeFactory
{
    /// <summary>Creates a DI scope whose tenant, user, culture, calendar and time zone are set from the arguments and the user's preferences.</summary>
    IServiceScope CreateScope(int tenantId, int userId, string? traceParentToLink = null);
}
public interface ITenantRegistry { Task<IReadOnlyList<TenantDescriptor>> ListTenantsAsync(CancellationToken ct); }

// Seam 11 — permission evaluation (T4), used inside job scopes exactly as in requests.
public interface IPermissionEvaluator { Task<PermissionDecision> EvaluateAsync(string resource, string action, CancellationToken ct); }
// T4 also provides: WellKnownUsers.SystemUserId; IConnectStep.RunAsync() (subject/user → tags, IsActive) for the job's first round trip;
// IAdministratorDirectory.GetAdministratorIdsAsync() (users holding every permission, capped at 20) for system notifications;
// the UserState sibling table with InboxSeenAt.

// Seam 12 — blobs (T7), for export files and import sheets.
public interface IBlobService { /* batch write/read/delete; the export handler writes one blob and records its id on core.Export */ }
```

## 4. Schema

All tables live in the tenant database, schema `core`. Timestamps are `datetime2(3)` UTC. Strings are `nvarchar`. Every enum column is a string with a `CHECK` on the closed set. Sequences: `sq_Job`, `sq_Schedule`, `sq_Notification`. Table types are derived from the entity classes per spec 0001; standalone types are listed at the end.

```
core.Job                                         (non-temporal; LOCK_ESCALATION = DISABLE; no triggers)
  Id                 int             NOT NULL  PK clustered; from sq_Job
  HandlerKey         nvarchar(64)    NOT NULL
  Status             nvarchar(16)    NOT NULL  CHECK IN ('Pending','Running','Succeeded','Failed','Cancelled')
  DueAt              datetime2(3)    NOT NULL
  Attempts           int             NOT NULL  DEFAULT 0
  LeaseToken         uniqueidentifier NULL
  LeaseExpiresAt     datetime2(3)    NULL
  LeaseOwner         nvarchar(128)   NULL
  StartedAt          datetime2(3)    NULL
  CompletedAt        datetime2(3)    NULL
  CancelRequestedAt  datetime2(3)    NULL
  ArgumentsJson      nvarchar(max)   NULL
  StateJson          nvarchar(max)   NULL
  ProgressPercent    tinyint         NULL      CHECK (ProgressPercent <= 100)
  ProgressMessage    nvarchar(256)   NULL
  ErrorCode          nvarchar(64)    NULL
  ErrorMessage       nvarchar(1024)  NULL
  ErrorDetails       nvarchar(max)   NULL
  TraceParent        nvarchar(55)    NULL
  RunAsUserId        int             NOT NULL  FK core.User(Id)
  RequestedById      int             NULL      FK core.User(Id)
  ScheduleId         int             NULL      FK core.Schedule(Id) ON DELETE SET NULL
  ScheduledFor       datetime2(3)    NULL
  CreatedAt          datetime2(3)    NOT NULL
  CHECK CK_Job_Lease: (Status = 'Running' AND LeaseToken IS NOT NULL AND LeaseExpiresAt IS NOT NULL)
                   OR (Status <> 'Running' AND LeaseToken IS NULL AND LeaseExpiresAt IS NULL)
  IX_Job_Available   (HandlerKey, DueAt, Id) INCLUDE (LeaseExpiresAt)  WHERE Status IN (N'Pending', N'Running')
  IX_Job_ScheduleActive (ScheduleId)                                    WHERE Status IN (N'Pending', N'Running') AND ScheduleId IS NOT NULL
  IX_Job_RequestedBy (RequestedById, CreatedAt DESC)                    WHERE RequestedById IS NOT NULL
  IX_Job_Retention   (Status, CompletedAt)                              WHERE Status IN (N'Succeeded', N'Failed', N'Cancelled')
```

```
core.Schedule                                    (system-versioned → core.ScheduleHistory; four audit columns)
  Id                   int            NOT NULL  PK clustered; from sq_Schedule; built-ins in the reserved band
  Code                 nvarchar(64)   NULL      UNIQUE filtered WHERE Code IS NOT NULL
  Name                 nvarchar(256)  NOT NULL
  Name2                nvarchar(256)  NULL
  Name3                nvarchar(256)  NULL
  HandlerKey           nvarchar(64)   NOT NULL
  ArgumentsJson        nvarchar(max)  NULL
  CronExpression       nvarchar(128)  NOT NULL
  TimeZoneId           nvarchar(64)   NULL
  MissedPolicy         nvarchar(16)   NOT NULL  DEFAULT 'Coalesce'  CHECK IN ('Coalesce','ReplayAll','Skip')
  OverlapPolicy        nvarchar(16)   NOT NULL  DEFAULT 'Skip'      CHECK IN ('Skip','Allow')
  CatchUpWindowMinutes int            NOT NULL  DEFAULT 1440        CHECK (> 0)
  RunAsUserId          int            NOT NULL  FK core.User(Id)
  IsBuiltIn            bit            NOT NULL  DEFAULT 0
  PausedReason         nvarchar(32)   NULL      CHECK IN ('owner_inactive') OR NULL
  IsActive             bit            NOT NULL  DEFAULT 1
  CreatedAt            datetime2(3)   NOT NULL
  CreatedById          int            NOT NULL  FK core.User(Id)
  ModifiedAt           datetime2(3)   NOT NULL  (concurrency token per T2)
  ModifiedById         int            NOT NULL  FK core.User(Id)
  ValidFrom / ValidTo  datetime2(7)   period columns (shadow in EF)
  IX_Schedule_HandlerKey (HandlerKey)
```

```
core.ScheduleState                               (non-temporal sibling; one row per schedule, inserted with it)
  ScheduleId        int              NOT NULL  PK clustered; FK core.Schedule(Id) ON DELETE CASCADE
  NextDueAt         datetime2(3)     NULL      (null while inactive)
  LastFiredAt       datetime2(3)     NULL
  LastScheduledFor  datetime2(3)     NULL
  LastJobId         int              NULL      FK core.Job(Id) ON DELETE SET NULL
  LastSkippedAt     datetime2(3)     NULL
  LeaseToken        uniqueidentifier NULL
  LeaseExpiresAt    datetime2(3)     NULL
  IX_ScheduleState_Due (NextDueAt) WHERE NextDueAt IS NOT NULL
```

```
core.JobWorkerState                              (single row; the tenant's scheduler heartbeat)
  Id            int            NOT NULL  PK  CHECK (Id = 1)
  LastTickAt    datetime2(3)   NOT NULL
  LastTickOwner nvarchar(128)  NOT NULL
```

```
core.Notification                                (non-temporal; written by INotifier only)
  Id             int            NOT NULL  PK clustered; from sq_Notification
  UserId         int            NOT NULL  FK core.User(Id)
  Type           nvarchar(64)   NOT NULL
  ArgumentsJson  nvarchar(4000) NULL
  TargetResource nvarchar(64)   NULL
  TargetId       bigint         NULL
  ActorUserId    int            NULL      FK core.User(Id)
  DedupKey       nvarchar(128)  NULL
  CreatedAt      datetime2(3)   NOT NULL
  ReadAt         datetime2(3)   NULL
  IX_Notification_User        (UserId, CreatedAt DESC) INCLUDE (ReadAt, Type)
  IX_Notification_Unread      (UserId)                 WHERE ReadAt IS NULL
  IX_Notification_Dedup       (UserId, DedupKey)       WHERE DedupKey IS NOT NULL AND ReadAt IS NULL   (non-unique; the insert predicate enforces)
  IX_Notification_Retention   (ReadAt, CreatedAt)
```

```
core.NotificationPreference                      (non-temporal; self-service rows)
  UserId  int           NOT NULL  FK core.User(Id) ON DELETE CASCADE
  Type    nvarchar(64)  NOT NULL
  Inbox   bit           NOT NULL  DEFAULT 1
  Email   bit           NOT NULL  DEFAULT 0
  Push    bit           NOT NULL  DEFAULT 0
  PK (UserId, Type)
```

```
core.UserState (T4's sibling table) — column added by this theme
  InboxSeenAt  datetime2(3)  NULL
```

```
Consumer tables sketched for T9/T7 (final shape theirs); the columns this theme depends on:
core.Export:  Id int PK; Resource nvarchar(64); Kind nvarchar(16) CHECK IN ('Display','ForImport'); RequestJson nvarchar(max);
              FileName nvarchar(256); BlobId <T7's id type> NULL; RowCount int NULL; ExpiresAt datetime2(3); JobId int NULL FK core.Job ON DELETE SET NULL;
              CreatedAt; CreatedById FK core.User.  IX (CreatedById, CreatedAt DESC); IX (ExpiresAt).
core.Import:  Id int PK; Resource; Mode nvarchar(16) CHECK IN ('Insert','Update','Merge'); FileBlobId; ResultBlobId NULL; RowCount NULL; ErrorCount NULL;
              JobId int NULL FK core.Job ON DELETE SET NULL; CreatedAt; CreatedById.
```

Standalone table types (spec 0001 §5, registered by the platform feature): `JobOutcomeList (Id int PK, Status nvarchar(16), DueAt datetime2(3) NULL, ErrorCode nvarchar(64) NULL, ErrorMessage nvarchar(1024) NULL, ErrorDetails nvarchar(max) NULL, StateJson nvarchar(max) NULL)`; `JobProgressList (Id int PK, ProgressPercent tinyint NULL, ProgressMessage nvarchar(256) NULL, StateJson nvarchar(max) NULL)`; `JobLeaseList (Id uniqueidentifier PK, LeaseSeconds int)`; `OrdinalIdList (Ordinal int PK, Id int)` for zipping pre-allocated ids to recipients. The existing `IdList` carries recipients and job ids.

Seeded rows (reserved band, `HasData`): the built-in schedules `core.job-retention` (`0 3 * * *`), `core.notification-retention` (`30 3 * * *`), `core.export-retention` (`0 4 * * *`), `core.blob-staging-sweep` (`15 * * * *`), each with `RunAsUserId = WellKnownUsers.SystemUserId`, `IsBuiltIn = 1`, `Code = HandlerKey`; and one `core.JobWorkerState` row (`Id = 1`, `LastTickAt = '0001-01-01'`, `LastTickOwner = 'seed'`) so the first tick is a plain `UPDATE`.

## 5. Answers to the brain dump's open questions

| Brain-dump question (abridged) | Answer | Decision |
|---|---|---|
| "Is the design robust?" (background tasks) | Robust in shape; the columns-on-entity choice, the one-shot lease with a safety buffer, and the missing overlap, cancellation, checkpoint, retention, poison and clock rules are replaced or added. | D1, D4–D7 |
| "Shared coordination state in the catalog DB, or leasing without a centralized table?" | No central state. Leased rows in each tenant database (`UPDLOCK, READPAST, ROWLOCK` plus a fencing token) are the only coordination; schedules fire exactly once through the same mechanism on `core.ScheduleState`. | D4, D7, D9 |
| "How do OTel traces fit? Inherit the trace id of the request or get their own?" | Their own: a new root `Consumer` span per batch with one link per job to the enqueuing `traceparent` stored on the row. | D11 |
| "How to implement a scheduler where CRON expressions can be stored anywhere and trigger all kinds of tasks?" | They are stored in one place, `core.Schedule`; a business entity that recurs references a schedule. Any registered handler key can be scheduled; the tick rides the poll and fires through the job table. | D9 |
| "User-triggered tasks run under the user's credentials?" | Yes: `RunAsUserId = RequestedById`, permissions evaluated at run time in the job scope; deactivated user ⇒ fail closed. | D10 |
| "Under what credentials do triggered (scheduled) tasks run? System user? How do we prevent scheduling reads of data the user cannot see?" | Built-ins run as the seeded system user; user schedules run as their owner (server-enforced), so run-time evaluation grants exactly the owner's rights; owner deactivation pauses the schedule and notifies administrators. | D10 |
| "Replay all missed triggers, only the last, or a drop-after-X-hours dial? Safeguard against the year-old restore?" | Per schedule: `Coalesce` (default), `ReplayAll` within `CatchUpWindowMinutes`, or `Skip`; plus an overlap policy; plus a global gap threshold (6 h) that coalesces regardless, counts the drop, and alerts — detected from both the schedule's own gap and the per-tenant heartbeat. | D9 |
| "Lease expiry with a safety buffer, or renewal?" | Renewal: sliding lease at one fifth of its length, fencing token, cancellation on loss; lease ≥ 3 renewal intervals. | D5 |
| "Nudge the engine so a new email is processed immediately." | Post-commit local nudge through a coalescing channel per tenant; other instances pick it up within `MaxPollInterval` (30 s); correctness never depends on the nudge. | D7, D8 |
| "Logs and metrics to detect mis-configured estimates, batch sizes, latency, backed-up queues." | The instrument table: duration vs lease, batch fill, queue latency, oldest due age, lease losses, gap detection. | D12 |
| "Some tasks report progress as states or a percentage; some fail with code + message + dump." | `ProgressPercent`, `ProgressMessage`, `StateJson` flushed with renewals; `ErrorCode`, `ErrorMessage`, `ErrorDetails` on the row. | D5, D6 |
| "Is it better to include self-initiated task completion in the inbox? Some types must not be unsubscribable; or always provide another way to the artifact?" | Both: completion notifications exist and their inbox channel is not mutable; and the export row with its file is the durable, notification-independent access path ("My exports"). | D13, D14 |
| "What happens when you click an unread item? The action differs per type." | The row carries `TargetResource` and `TargetId`; the client maps resource to route; the type descriptor fixes the resource for types with one. Clicking marks the item read. | D14, D15 |
| "What is the best entity model and API interface?" (inbox) | `core.Notification` + `core.NotificationPreference` + `InboxSeenAt`; `INotifier` riding the batch; `inbox/summary`, `inbox/seen`, `inbox/read`, `inbox/read-all`, the standard query endpoint with a self-scope criterion. | D14, D15, D19 |
| "Notifying a user must not add a DB round trip; it rides the save." | `INotifier.Notify(batch, …)` appends one `INSERT … SELECT` per request with preferences and dedup as predicates. | D14 |
| "SignalR notifies the client that counters changed; the client fetches." | `inbox.changed` thin event to per-(tenant, user) groups after commit; the summary endpoint is the fetch. | D15, D16 |
| "What is the shape of inbox tracking?" (user entity section) | One column, `InboxSeenAt`, on the user's non-temporal sibling table. | D15 |
| Orchestrator: "handler registry; batch lease acquisition; multi-instance safety; poison handling and dead-lettering" | `[JobHandler]` + `IJobsRegistrar`; `TOP (@n)` claim with `OUTPUT INTO`; token fencing; `MaxAttempts` failures ⇒ `Failed` with admin retry. | D3, D4, D6 |
| Orchestrator: "first consumers: large import/export, blob orphan sweep, email outbox later" | `core.Export`/`core.Import` rows with jobs; `core.blob-staging-sweep` built-in schedule; outbox rows carry `JobId`. | D13 |
| Orchestrator: "per-user groups, thin events, tenant isolation, Azure SignalR in SaaS" | Groups `t{tenant}.u{user}` set server-side; ids-only payloads; Azure SignalR when configured, Redis backplane for multi-instance on-prem. | D16 |
| Orchestrator: "whether the inbox splits off as 0020" | Yes. | D17 |

## 6. Seams

**Seam 1 — the batch abstraction (T2 owns).** This theme is the heaviest raw-SQL consumer of the batch and needs five things: `AddSql` with a declared write set and a per-statement `MayRetry`; TVP binding for standalone types by metadata; synchronous `ReserveIds<T>(count)` so job and notification ids are known before the batch runs; `OnCommitted` callbacks (nudge, hub events) that run outside the transaction and are logged, not thrown, on failure; and a way to select entity rows joined to a table variable produced by an earlier statement of the same command (`SelectByJoin<TEntity>`), because the claim's `OUTPUT INTO @claimed` and the entity load must share one round trip. Transactions must be READ COMMITTED (never SNAPSHOT) for statements carrying `READPAST`; the executor should refuse `MayRetry` inside a transaction (spec 0007's fixed fact: SqlClient retry is skipped there) and retry only whole non-transactional commands such as the claim.

**Seam 8 — background-task columns and lease statements (this theme owns semantics, T2 emits).** Resolved as: there are no per-entity lease columns. T2 emits three statement shapes for this theme — claim (D4), renew (D5), complete (D6) — from `core.Job` metadata, plus the `JobId` join load for `IJobEntity` types. The capability T2 adds to the entity contract is only `IJobEntity` (one nullable FK column, `ON DELETE SET NULL`, with a non-unique index).

**Seam 9 — request context in background scopes (T1 owns).** The worker needs `ITenantScopeFactory.CreateScope(tenantId, userId, traceParent)` producing a scope in which the tenant holder, `ISandboxContext`, the user context, culture, calendar and time zone are set from the tenant's settings and the run-as user's preferences (no `AsyncLocal`), and `ITenantRegistry.ListTenantsAsync()` for polling. The hub's connect must reuse T1's tenant resolution and connect step.

**Seam 11 — permission evaluation (T4 owns).** Job scopes call the same evaluator as requests; T4 must expose the connect step as a callable (`IConnectStep`) so a job's first round trip stamps nothing but reads tags and `IsActive`; T4 seeds the system user and exposes `WellKnownUsers.SystemUserId`; T4 provides `IAdministratorDirectory.GetAdministratorIdsAsync()` for system notifications and the bespoke self-scope criteria hook this theme registers for jobs, exports and notifications; T4's `UserState` table takes `InboxSeenAt`.

**Seam 15 — notification enqueue riding the save batch (this theme owns, T5 consumes).** The contract is `INotifier.Notify(batch, requests)` (§3.2); T5's pipeline exposes its batch to services in the transactional side-effects step, and the hub event fires from the batch's post-commit hook. T5 should also expose `IJobQueue.Enqueue` at the same step for services that hand work off.

**Seam 3 — one capability, declared once (T5 owns).** The activatable capability on `Schedule` must trigger a post-persist hook so the schedule service recomputes `NextDueAt`; this theme needs T5's pipeline to offer a per-entity `AfterPersist(batch, saved)` extension point that can append statements to the same batch.

**Seam 6 — feature composition (T1 owns).** The jobs feature contributes: handler and schedule registrars, notification type and event registrars, the worker hosted service, the hub mapping, securables and endpoints, and the seeded rows. The feature contract must allow a feature to contribute seed data and standalone table types to the model.

**Seam 12 — blob staging tokens (T7 owns).** Export writes one blob and records its id; import reads the staged sheet by token in the job scope, so the staging TTL must exceed the queue latency budget (recommend 24 h for import uploads, distinct from image staging).

**Seam 13 — wire shapes (T6 owns).** Background hand-off responses (`{ exportId, jobId }`) and the job/notification rows use the standard details and query shapes; the summary endpoint is a bespoke record; the hub route and the `Mcp` exposure of `get_job`/`list_notifications` are T6's.

**Seam 14 — telemetry (T2 owns instruments; this theme names its own).** D12's names are constants in `Tellma.Core.Abstractions.Jobs.JobsTelemetry` and `…Notifications.NotificationsTelemetry`; the tenant is never a tag.

**Seam 17 — vocabulary.** This theme uses singular table names (`core.Job`, `core.Notification`) following the brain dump; if the synthesizer chooses plural, every name here pluralizes mechanically. Four audit columns on `Schedule`; none on `Job` and `Notification` (system-written; `CreatedAt` only). `int` ids throughout (volumes computed in D1/D14 keep `int` viable for a century). "Version tag" terminology is not used here because nothing in this theme is tag-cached.

## 7. Departures from ARCHITECTURE.md

1. **Quartz is not the scheduler.** ARCHITECTURE.md pins `Quartz.Extensions.Hosting` for the identity server; this theme adds Cronos 0.13.0 and its own leasing scheduler instead of extending Quartz to tenant work. Reason: Quartz's cron dialect (mandatory seconds, `?` rule) is incompatible with the Unix dialect users know, its clustered job store is a second persistence model, and it cannot ride the per-tenant batch.
2. **Groups as the tenant boundary on the hub.** Spec 0003 §7.4 does not prescribe how tenant isolation is achieved on a shared hub; this theme uses server-assigned per-(tenant, user) groups and states it as the rule. Not a contradiction, an addition.
3. **Redis for multi-instance on-prem SignalR.** ARCHITECTURE.md's local stack mentions "Redis if any"; this theme makes Redis a requirement only for on-prem deployments with two or more instances that want real-time events. Single-instance on-prem and SaaS need none.
4. **New CPM pins**: `Cronos` (platform), `Microsoft.Azure.SignalR`, `Microsoft.AspNetCore.SignalR.StackExchangeRedis` (reference distribution). The `Microsoft.Data.SqlClient` bump the research digest requires for hierarchyid is T2's, not this theme's.
5. **Always On** becomes a documented hosting requirement of every distribution's App Service (the poller stops after 20 idle minutes otherwise); this belongs in the infra template ARCHITECTURE.md describes under Hosting on Azure.

No departure from the fixed facts: `MERGE` is unused; retry is the executor's; `READPAST` is always paired with `UPDLOCK`; JSON stays `nvarchar(max)`; no logic in the database (predicates in emitted statements only); every reference is an FK.

## 8. Verification

Facts relied on from `research/background-inbox.md` (all verified there on 2026-09-01): the `UPDATE TOP … WITH (READPAST, UPDLOCK, ROWLOCK) OUTPUT` idiom and Hangfire's production fetch (§1.1); bare `READPAST` is a silent no-op under RCSI and `UPDLOCK + READPAST` behaves identically under RCSI on and off, `READPAST` under SNAPSHOT raises error 650, ordered claims work through a `TOP … ORDER BY` CTE (§1.2, local reproduction); sliding lease renewed at one fifth with a fencing token, poison after five attempts, immediate redelivery is a footgun (§1.3); filtered-index requirements and the SET-option trap for `sqlcmd` (§1.4); `ROWLOCK` does not prevent escalation at 5,000 locks, `LOCK_ESCALATION = DISABLE` exists, `UPDATE TOP` is unordered, `OUTPUT` without `INTO` is illegal on triggered tables, deadlock handling advice (§1.5); `HostOptions.ShutdownTimeout` 30 s, `BackgroundServiceExceptionBehavior = StopHost`, `IHostedLifecycleService.StoppingAsync` ordering (§2.1); `PeriodicTimer(TimeSpan, TimeProvider)` and `BoundedChannelFullMode.DropWrite` as a coalescing signal (§2.2); Always On, `WEBSITE_INSTANCE_ID` is diagnostic only, Linux drain default 5 s max 120, slot swaps abandon long operations (§2.3); Cronos 0.13.0 API, DST semantics, `GetOccurrences`, `H` jitter, no seconds by default, MIT (§3.1); NCrontab has no time-zone support, Quartz's dialect differs (§3.2–3.3); span links at creation, `Consumer` kind for processing, messaging attributes, baggage security note, `Activity.AddLink` on .NET 10 (§4.1); Azure Monitor serializes links to `_MS.links` (§4.2); Azure SignalR 1.33.1, Redis backplane package 10.0.11, `IUserIdProvider`, groups are in-memory and not a security feature, `IHubContext` usable from hosted services, `users/{user}/:closeConnections`, `ClaimsProvider`, `CloseOnAuthenticationExpiration` supported since 1.19.0, sticky sessions and channel prefix for Redis (§5); the misfire and overlap policies of Quartz, Kubernetes, Airflow, Hangfire and Temporal (§6).

Facts relied on from the briefing's digest and the fixed facts: `MERGE` banned; SqlClient retry skipped inside transactions; concatenated command text with `NextResult()` is the batch mechanism; RCSI default on Azure only; app-assigned ids from sequences; `[TableType]` derivation and standalone types (spec 0001 §5); spec 0003 §7.4 rules; spec 0007 §13 outbox shape; `ISandboxContext` must resolve in background scopes; `DeploymentIdentity.DeploymentId` as the Redis prefix and lease-owner prefix; telemetry naming rules.

Still unverified (this proposal's own inferences, to be proven by a plan test in the spec's definition of done): that the optimizer matches `IX_Job_Available` (filter `Status IN (…)`) for the claim predicate that repeats the `IN` list plus extra conjuncts — D4 records the fallback; that `OUTPUT … INTO @tableVariable` followed by a join in the same command performs as one seek-and-join under `READPAST` (standard T-SQL; not reproduced); Cronos's maximum year (irrelevant below 2099, but `GetNextOccurrence` returning null must be handled as "schedule exhausted" ⇒ deactivate with `PausedReason`, which would need a second allowed value — flagged); whether Application Insights renders `_MS.links` (research §8 says no primary source; the design assumes KQL joins); default numbers (poll 1 s/30 s, concurrency 8, drain 4 s, gap 6 h, retention 30/180 days) are judgment calls to be tuned with D12's instruments.


