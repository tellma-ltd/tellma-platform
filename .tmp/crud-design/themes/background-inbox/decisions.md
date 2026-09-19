# Background tasks, scheduler, and the inbox — decisions (spec 0019 jobs and scheduler; spec 0020 notifications and the hub)

Settled design for theme T10. Self-contained: every name, type, statement and column a spec author needs is here. Contract blocks use the platform's contract notation (names are normative; shape is described, not transcribed); SQL is the exact shape to emit.

Vocabulary: a **job** is one durable unit of background work (one row of `core.Job`); a **handler** is the platform or distribution class that executes jobs of one **handler key**; a **claim** is the statement that leases a batch of due jobs to one instance; a **schedule** is a cron definition that creates jobs when due; a **notification** is one row of `core.Notification` addressed to one user and the **inbox** is that user's view of them; the **hub** is the SignalR hub that pushes thin events. "Task" is not used as a type-family name because it collides with `System.Threading.Tasks.Task` in every file that would use it; the specs' titles keep the words "background tasks" for readers.

## 1. Critique

**The brain dump's instincts are right; one structural choice is reversed.** Per-row leasing, a handler registry, batch acquisition, nudging, tenant isolation, multi-instance safety without a central table — all correct and matching Hangfire, Azure Functions and Kubernetes. The choice to put "standard columns for leasing and progress on any entity that wants to track a background task" is reversed for four concrete reasons: (1) *status collision* — an outbox row already has a business status (queued, sent, bounced) and an export request has one; a second lease status forces prefixed columns on every such table and Queryex users see two statuses on one entity; (2) *temporal churn and concurrency-token contamination* — lease renewal writes every minute, which on a temporal entity is a history row per minute and on any entity is a write that must be excluded from the `ModifiedAt` concurrency stamp; (3) *prunability and one hot index* — terminal queue rows must be deletable so the pending index stays tiny, and lease columns on a business row cannot be pruned without deleting the business row; (4) *one code path* — one table means one claim, one renewal, one completion, one admin page, one retention job and one instrument set. The replacement is one per-tenant `core.Job` table that business rows reference through a nullable `JobId`; the machinery never writes a business table.

**Lease expiry with a safety buffer versus renewal.** Every mature system renews (a sliding lease renewed at one fifth of its length, carrying a fencing token) and none sizes a one-shot lease from a duration estimate, because estimates are wrong exactly when it matters. Renewal wins; the "safety buffer" survives only as the rule that a lease is at least three renewal intervals long so one missed renewal is not fatal.

**Gaps in the brain dump, all filled below:** no overlap policy for schedules (every mature scheduler has one); no cancellation path for a running job; no checkpoint state for hours-long jobs (the 5-second Linux App Service drain makes this a data-loss hazard); no retention for terminal rows; no poison threshold or dead-letter state; no clock-source rule (instance clocks drift — the database clock is the only clock in comparisons); no handler idempotency contract (at-least-once is unavoidable with leases); no detection of rows whose handler no longer exists after a deploy; no rule for a user-triggered job whose user is deactivated before it runs; no rule for a *batch* of jobs enqueued by different users (whose identity does the handler run under?); the restore safeguard covers schedules but not the thousands of *queued* rows a restored backup brings back; and `DueAt` for a retry must not be computed on an instance clock.

**"CRON expressions can be stored anywhere."** They should not be. One `core.Schedule` table holds every schedule, built-in or user-created; a business entity that recurs holds a `ScheduleId`, never a cron string. One table is what makes the tick a single claim statement and gives one validation path (parse, time zone, handler key, run-as), one admin page, one replay policy and one place to pause on owner deactivation.

**Inbox.** The seen/unread split is right and common (GitHub, Slack). "Inbox tracking" on the user row is wrong for the same churn reason as `LastActive`: it is one timestamp, `InboxSeenAt`, on the user's non-temporal sibling table, and the two counters are `TOP`-capped counts over indexes, never maintained counters (maintained counters drift). Missing and added: a per-type registry (what types exist, which can be muted, what they link to), deduplication (re-assigning the same document must not stack notifications), a target link so the click action is data rather than code, retention, and MCP access. "Should completion of a self-initiated task be in the inbox and can it be muted?" has a clean answer once exports are entities: the export row is the durable artifact, the notification is a convenience whose inbox channel cannot be muted.

**Traces.** A job is a new root span of kind `Consumer` linked to the enqueuing request's context, never a child of it.

**Naming.** `Job` over `Task`; `Notification` over `InboxItem` (the same row feeds email and push later and every product calls the page Notifications); `Schedule` over `CronJob` (users configure schedules; jobs are what they produce).

**Internal inconsistency reconciled.** "Queuing background operations IS transactional" and "nudge the engine so it is processed immediately" both hold only through a post-commit callback on the batch executor: the insert rides the transaction, the nudge and the hub event fire after commit. That callback is a contract this theme needs from the batch abstraction (§6, seam 1).

**Email outbox.** The first consumer named by the brain dump, specified by a later spec; the alignment rule fixed here is that outbox rows carry `JobId`, never their own lease columns, and the outbox worker is an `IJobHandler` over the outbox row with batch size 100 running as the system user.

## 2. Decisions

### D1 — One queue table per tenant database, `core.Job`; business rows reference it one-to-one

**Decision.** Every unit of background work is one row of `core.Job` (§4). An entity whose rows are individually processed in the background carries `JobId int NULL` (FK to `core.Job`, `ON DELETE SET NULL`, unique where not null) through the capability `IJobEntity`; the runtime loads such rows by joining the claimed job ids and never writes the business table. A job that has no business row (a scheduled recompute, a sweep, an export request's job) carries its input in `Job.ArgumentsJson`. The relation is one-to-one: one business row is one job; work that spans many rows is an argument-only job whose handler queries the rows itself.

**Rationale.** The four reasons in §1. One-to-one is what makes per-item outcomes (D6) and the entity join in the claim (D4) well defined; a many-rows-per-job shape would collapse many outcomes onto one row.

**Alternatives rejected.** Standard columns on each task-bearing entity (the brain dump) — rejected for the reasons in §1. A polymorphic `(EntityType, EntityId)` reference on the job — cannot be a database FK (fixed fact: every reference is an FK) and needs a `bigint` that lies about the entity's id type. A Hangfire-style separate `JobQueue` plus `Job` — two tables where one suffices, because app-assigned ids make the FK from a business row to its job settable before either insert.

**Confidence.** High. Review flag: the brain dump explicitly wanted the columns on the entity; this is the largest departure from it.

### D2 — Names, keys, packaging, pins

**Decision.** Contracts live in `Tellma.Core.Abstractions` under namespaces `Tellma.Core.Abstractions.Jobs`, `…Notifications`, `…Realtime` (BCL only). Runtime packages: `src/core/Tellma.Core.Jobs/` (worker, scheduler, claim/renew/complete emitter, job and schedule services; references `Tellma.Core.Abstractions`, the data-access package, `Microsoft.Extensions.Hosting`, `Cronos`) and `src/core/Tellma.Core.Notifications/` (notifier, inbox service, `TellmaHub`, the event publisher; references the ASP.NET Core shared framework for SignalR). Meters and `ActivitySource`s follow the repo's package pattern (`Tellma.Core.Email` emits `Tellma.Email`): `Tellma.Jobs` and `Tellma.Notifications`. Tables: `core.Job`, `core.Schedule`, `core.ScheduleState`, `core.JobWorkerState`, `core.Notification`, `core.NotificationPreference` (singular, following the brain dump; pluralize mechanically if seam 17 settles on plural). Sequences `sq_Job`, `sq_Schedule`, `sq_Notification`.

Handler keys are lowercase, dot-separated, kebab-case segments `<slug>.<name>` validated at registration against `^[a-z][a-z0-9]*(\.[a-z0-9-]+)+$` with a 64-character cap; the `core.` prefix is reserved for platform handlers (`core.export`, `core.import`, `core.email-outbox`, `core.blob-staging-sweep`, `core.job-retention`, `core.notification-retention`, `core.export-retention`); module packages use their module slug (`gl.`); distributions use their deployment slug (`etpharma.recompute-balances`). Keys are persisted and must be stable for the life of a distribution; a breaking change to a handler's arguments is a new key. Notification type keys and hub event names use the same grammar (`core.export.ready`, `core.job.failed`, `core.job.held`, `core.schedule.paused`, `core.scheduler.gap`; events `inbox.changed`, `job.changed`, `cache.changed`, `session.ended`).

New central-package pins: `Cronos` 0.13.0 (platform, `Tellma.Core.Jobs`); `Microsoft.Azure.SignalR` 1.33.1 and `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.11 referenced by the reference distribution's Web project only (the hosting choice is the distribution's). Nothing here depends on `Quartz.Extensions.Hosting`.

**Rationale.** Keys in rows and seeds need a grammar that prevents collisions between packs and distributions and lets the admin page group by prefix. Two runtime packages keep the SignalR dependency out of the worker so a dedicated worker host can reference `Tellma.Core.Jobs` alone; the short meter names match the existing packages.

**Alternatives rejected.** CLR type names as keys (renames break rows); integer handler ids (unreadable in SQL and logs); one runtime package inside `Tellma.Core` (drags SignalR into every host that only wants jobs — review flag).

**Confidence.** High on keys; medium on the two-package split.

### D3 — The handler contract: batch-shaped, one interface, options on an attribute, one run-as user per handler invocation

**Decision.** A handler implements `IJobHandler<TItem>` where `TItem` is `Job` (argument-only jobs) or an entity class implementing `IJobEntity` (entity-backed jobs). The worker hands the handler a `JobBatch<TItem>` of 1..`BatchSize` items claimed under one lease token; the handler reports per-item outcomes (§3). Options live on `[JobHandler]`:

| Option | Default | Meaning |
|---|---|---|
| `Key` | required | Handler key (D2). |
| `BatchSize` | 1 | Maximum items per claim; 1–500. |
| `LeaseSeconds` | 300 | Sliding lease length; renewal interval is one fifth; minimum 15. |
| `MaxAttempts` | 5 | Claims after which a job is dead-lettered (D6). |
| `MaxConcurrency` | 1 | Batches of this key in flight per tenant per instance. |
| `RetryBaseSeconds` / `RetryMaxSeconds` | 30 / 3600 | Exponential backoff with full jitter between attempts. |

Outcome rules: a normal return marks every unmarked item `Succeeded`; an exception marks every unmarked item `Retry` (or `Failed` when attempts are exhausted or the exception is `JobFailedException`); `OperationCanceledException` while the batch token is cancelled marks unmarked items `Released` on host shutdown, `Retry` on lease loss, and `Cancelled` on a cancel request; `Succeed()`, `Retry(after, error)`, `Fail(error)` are explicit per item.

**Run-as partitioning.** A claim may return jobs with different `RunAsUserId` values. The worker partitions the claimed rows by `RunAsUserId` and invokes the handler once per partition, sequentially, each invocation inside its own tenant scope for that user (seam 9) under the same lease token. Handlers meant for throughput (the email outbox, any batch-shaped platform work) are therefore enqueued with `RunAsUserId` = the system user (D11), which makes every claim a single partition; user-triggered jobs (export, import) have `BatchSize = 1` so partitioning is moot. The contract is at-least-once: a lost lease re-runs the item elsewhere, so handlers are idempotent or checkpoint through `StateJson`.

**Rationale.** One interface keeps the registry and executor to one code path; batch shape is what the outbox needs (one `IEmailSender.SendAsync` per claim) and costs a single-item handler nothing (`batch.Single`). Attribute options are the most mechanical thing a coding agent can write and are validated at registration. Partitioning after the claim keeps the claim statement simple and makes the "whose permissions?" question unambiguous without a per-user claim.

**Alternatives rejected.** Two interfaces (arguments vs entities): two registries, two executors. Fluent options: more code per handler for no gain. A claim predicate on `RunAsUserId`: complicates the hot statement for a case the system user already avoids.

**Confidence.** High.

### D4 — State machine, invariants, the claim statement and its index

**Decision.** `Status ∈ {Pending, Running, Succeeded, Failed, Cancelled, Held}`. Transitions: `Pending → Running` (claim); `Running → Pending` (retry or release); `Running → Succeeded | Failed | Cancelled`; `Pending → Cancelled` (cancel); `Pending | Running(expired) → Held` (gap quarantine, D10); `Held → Pending` (admin resume) or `Held → Cancelled`; `Failed → Pending` (admin retry). Invariant enforced by `CHECK`: `Status = 'Running'` if and only if `LeaseToken` and `LeaseExpiresAt` are both not null. Availability predicate: `Status IN ('Pending','Running') AND DueAt <= now AND (LeaseExpiresAt IS NULL OR LeaseExpiresAt < now)` — one predicate serves never-claimed rows and abandoned leases, so no reaper exists.

The claim, one per registered handler key with free slots, emitted by `Tellma.Core.Jobs` and executed through the batch executor as a non-transactional statement at READ COMMITTED (each statement autocommits; never under SNAPSHOT — error 650):

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
-- entity-backed handlers only; emitted from EF metadata by the data layer:
SELECT <entity columns> FROM <entity table> AS [e] INNER JOIN @claimed AS [c] ON [c].[Id] = [e].[JobId];
```

Table-variable names are suffixed per statement ordinal by the emitter (several claims share one command text). Index `IX_Job_Available ON core.Job (HandlerKey, DueAt, Id) INCLUDE (LeaseExpiresAt) WHERE Status IN (N'Pending', N'Running')`; the statement repeats the filter literals verbatim so the optimizer can prove the predicate is a subset of the filter; `@key`, `@n` (≤ 500), `@token`, `@leaseSeconds`, `@owner` are parameters. `core.Job` is created with `LOCK_ESCALATION = DISABLE` and carries no triggers. Errors 1205 and 1222 on the claim are retried after jitter by the executor's `MayRetry` path. `READPAST` is always paired with `UPDLOCK, ROWLOCK` (a bare `READPAST` is a silent no-op under RCSI, which is on by default on Azure SQL and off on-prem).

**Rationale.** The verified queue idiom (Hangfire's production fetch, the OUTPUT-clause page's own example); the invariant lets one predicate cover fresh and abandoned rows; `INTO @claimed` plus the join keeps claim and entity load in one round trip; `SYSUTCDATETIME()` removes instance clock skew from every comparison.

**Alternatives rejected.** Keeping `Status = 'Pending'` while leased (Hangfire) — users could not see "Running" on their export without decoding lease columns. A separate reaper for expired leases — an extra statement and a race. `DELETE … OUTPUT` destructive dequeue — loses history and progress. `FORCESEEK` — omitted; the filtered index makes a seek the only sensible plan; revisit on a plan regression.

**Confidence.** High on the statement; medium on the optimizer matching the `IN`-filtered index with extra conjuncts (the definition of done includes a plan test; fallback: filter on `Status = N'Pending'` only plus a `LeaseExpiresAt IS NULL` invariant for pending rows and a reaper). Note for capacity: in-flight `Running` rows with valid leases sort before newer pending rows and are walked as residual-filter rows by every claim of that key — bounded by `MaxConcurrency × BatchSize` per instance, acceptable.

### D5 — Sliding lease with a fencing token; renewal carries progress and returns cancellation

**Decision.** One `LeaseToken` (app-generated `Guid`) per claim. The worker renews every in-flight lease of a tenant on this instance with one statement per tenant, on an interval equal to the shortest `LeaseSeconds / 5` among them (renewing a longer lease more often is harmless), piggybacking buffered progress:

```sql
DECLARE @now datetime2(3) = SYSUTCDATETIME();
UPDATE [j]
SET [LeaseExpiresAt] = DATEADD(second, [t].[LeaseSeconds], @now),
    [ProgressPercent] = COALESCE([p].[ProgressPercent], [j].[ProgressPercent]),
    [ProgressMessage] = COALESCE([p].[ProgressMessage], [j].[ProgressMessage]),
    [StateJson] = COALESCE([p].[StateJson], [j].[StateJson])
OUTPUT [inserted].[Id], [inserted].[LeaseToken], [inserted].[CancelRequestedAt]
FROM [core].[Job] AS [j]
INNER JOIN @tokens AS [t] ON [t].[Id] = [j].[LeaseToken]     -- JobLeaseList (Id uniqueidentifier, LeaseSeconds int)
LEFT JOIN @progress AS [p] ON [p].[Id] = [j].[Id]             -- JobProgressList
WHERE [j].[Status] = N'Running';
```

Index `IX_Job_Lease ON core.Job (LeaseToken) INCLUDE (Status) WHERE LeaseToken IS NOT NULL` makes renewal, completion and release seeks (without it every renewal scans the retained terminal rows). A claimed job absent from the output has lost its lease (reclaimed after expiry by another instance): the worker cancels that batch's token with reason `LeaseLost`, counts `tellma.jobs.lease_lost`, and discards the batch's eventual outcomes (the completion is fenced anyway). A row whose `CancelRequestedAt` is not null cancels that *item's* token (`JobItem.CancellationToken`, linked to the batch token) with reason `CancelRequested`; for a batch of one, item and batch tokens coincide. Progress via `item.Progress.Report(percent, message, state)` is buffered and flushed with the next renewal, or immediately through `Flush()` (a checkpoint before a non-idempotent step). A handler that ignores cancellation is abandoned after one further lease length: its task is left running with a logged warning, it never blocks shutdown, and its row becomes re-claimable when the lease lapses.

**Rationale.** The verified industry shape: sliding lease, renewal at one fifth, a stamp as the fence. Addressing renewal by token makes one statement serve every batch of the tenant; progress on the renewal costs zero round trips.

**Alternatives rejected.** One-shot lease sized by a safety buffer — wrong whenever the estimate is wrong. Renewal per batch — N statements per interval. Rotating the token on renewal (Hangfire rotates `FetchedAt`) — more state for no added safety; the token already proves continuity from the claim.

**Confidence.** High.

### D6 — Completion, attempts, backoff, poison handling, dead-lettering

**Decision.** `Attempts` counts *claims* (incremented by the claim statement) — the only counter that catches a handler that crashes the process before reporting anything. A graceful release un-counts its claim. Rules, evaluated in C# after the claim and mapped into one completion statement per batch driven by `JobOutcomeList (Id int PK, Status nvarchar(16), Released bit, RetryAfterSeconds int NULL, ErrorCode nvarchar(64) NULL, ErrorMessage nvarchar(1024) NULL, ErrorDetails nvarchar(max) NULL, StateJson nvarchar(max) NULL)`:

- A claimed row with `Attempts > MaxAttempts` is not handed to the handler; it is completed as `Failed` with `ErrorCode = 'attempts_exhausted'` (crash loops end here).
- A claimed entity-backed row whose business row no longer exists is completed as `Cancelled` with `ErrorCode = 'entity_missing'` without execution.
- `Retry` → `Status = 'Pending'`, `RetryAfterSeconds = U(0, min(RetryMaxSeconds, RetryBaseSeconds × 2^(Attempts−1)))` (full jitter) unless the handler supplied a delay; when `Attempts ≥ MaxAttempts` the outcome becomes `Failed` (`ErrorCode` from the handler's error or `'attempts_exhausted'`).
- `Released` (host shutdown; claimed but never started) → `Status = 'Pending'`, `Released = 1` (attempt un-counted), `DueAt` unchanged.
- `Succeeded`, `Failed` (`ErrorCode` required), `Cancelled` → terminal with `CompletedAt`.

```sql
DECLARE @now datetime2(3) = SYSUTCDATETIME();
UPDATE [j]
SET [Status] = [o].[Status],
    [DueAt] = CASE WHEN [o].[RetryAfterSeconds] IS NULL THEN [j].[DueAt] ELSE DATEADD(second, [o].[RetryAfterSeconds], @now) END,
    [Attempts] = CASE WHEN [o].[Released] = 1 THEN [j].[Attempts] - 1 ELSE [j].[Attempts] END,
    [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL,
    [CompletedAt] = CASE WHEN [o].[Status] IN (N'Succeeded', N'Failed', N'Cancelled') THEN @now ELSE NULL END,
    [ErrorCode] = [o].[ErrorCode], [ErrorMessage] = [o].[ErrorMessage], [ErrorDetails] = [o].[ErrorDetails],
    [StateJson] = COALESCE([o].[StateJson], [j].[StateJson]),
    [ProgressPercent] = CASE WHEN [o].[Status] = N'Succeeded' THEN 100 ELSE [j].[ProgressPercent] END
OUTPUT [inserted].[Id], [inserted].[Status], [inserted].[RequestedById], [inserted].[Attempts]
FROM [core].[Job] AS [j] INNER JOIN @outcomes AS [o] ON [o].[Id] = [j].[Id]
WHERE [j].[LeaseToken] = @token AND [j].[Status] = N'Running';
```

Retry delays are durations applied to the database clock, never instance-computed timestamps. The completion runs in one READ COMMITTED transaction together with every statement the handler appended to `batch.Batch` (its own DB side effects — the outbox marking rows sent, the export recording its blob) and with the notifications the worker adds: on `Failed`, `core.job.failed` to `RequestedById` (or to administrators when null); on `Held` see D10; on `Succeeded`, whatever the handler requested through `item.NotifyOnSuccess(...)`. A completion transaction that fails is retried as a whole only when every statement in it is `MayRetry`; otherwise the lease is left to lapse and the job is re-claimed (at-least-once), which is why handlers append idempotent statements. Poison rows stay in `core.Job` as `Failed`; the admin action `retry` sets `Pending`, `Attempts = 0`, `DueAt = now`, clears the error; `cancel` sets `Cancelled` on `Pending`/`Held` rows and stamps `CancelRequestedAt` on `Running` rows.

**Rationale.** Per-row outcomes let a 100-email batch succeed for 97 rows and retry 3 (spec 0007's per-message rule depends on this). Counting claims rather than reported failures is the only poison detector that survives a crashing handler; un-counting graceful releases is what keeps a three-hour export released by two deploys from being dead-lettered for no fault. Full-jitter backoff is the standard remedy for the immediate-redelivery footgun.

**Alternatives rejected.** Batch-level outcome only — forces the outbox to re-send successes. Counting only reported failures — misses crash loops. A separate dead-letter table — one more table and page for what a status value and a filter give.

**Confidence.** High.

### D7 — The worker: per-instance loop, per-tenant adaptive polling, local nudges, no cross-instance coordination, safe drain

**Decision.** `JobWorker` is one `BackgroundService` per host process implementing `IHostedLifecycleService` (its `StoppingAsync` stops claiming before Kestrel finishes draining) and never lets an exception escape `ExecuteAsync` (`BackgroundServiceExceptionBehavior` defaults to `StopHost`). It holds a `TenantPoller` per tenant listed by the tenant registry (refreshed on the registry's cadence). Each poller owns a coalescing wake-up channel (`Channel.CreateBounded<bool>` with capacity 1 and `FullMode = DropWrite`) and a `PeriodicTimer(interval, TimeProvider)`; it waits on whichever fires first. The interval is adaptive: after a poll that claimed a full batch for any key, poll again immediately; after a poll that found nothing, double from `MinPollInterval` (1 s) to `MaxPollInterval` (30 s); a nudge resets to the minimum.

One poll is one round trip containing, in order: the heartbeat block (D10: read `LastTickAt`, run the gap quarantine when due, update the heartbeat only when it is older than 60 s — a seek and usually no write); one claim (D4) per registered key that has free concurrency slots on this instance for this tenant, with `TOP (min(BatchSize, free slots × BatchSize))`; the due-schedule claim (D9). Once per minute per tenant the poll also carries the backlog sample — for each registered key `SELECT TOP (1) [DueAt] FROM [core].[Job] WHERE [Status] = N'Pending' AND [HandlerKey] = @key ORDER BY [DueAt]` (a seek per key on the filtered index; never a `MIN` across keys, which would scan the whole backlog). Claimed batches run on the thread pool under a per-instance `SemaphoreSlim(MaxParallelBatches, default 8)` and a per-(tenant, key) counter enforcing the handler's `MaxConcurrency`.

A nudge is in-process only: `IJobQueue.Enqueue` registers a post-commit callback that wakes the enqueuing instance's poller for that tenant, so the common case (the request that enqueued runs on an instance with capacity) is processed within milliseconds; other instances see the work on their next poll, bounded by `MaxPollInterval`. There is no central table, no Redis, no `sp_getapplock`: correctness derives from the leased rows alone. Sandbox tenants are polled like live ones; the job scope's `ISandboxContext` reports the tenant's category. `JobsOptions.Enabled = false` turns the worker off on a host.

Shutdown: `StoppingAsync` stops polling and cancels every batch token with reason `HostStopping`; the worker waits up to `DrainTimeout` (default 4 s, under the 5 s Linux App Service default) for handlers to return; batches whose handler returned (or never started) are completed with `Released` outcomes in one statement per tenant; batches whose handler is still running keep their lease — it lapses naturally and another instance reclaims — because releasing a row under a still-running handler is the one way to run a job twice concurrently. The reference distribution's infra template sets `WEBSITES_CONTAINER_STOP_TIME_LIMIT` to 30 and `Always On` (the poller stops after 20 idle minutes otherwise).

**Rationale.** Multi-instance safety without shared state is a stated goal and the leased rows provide it; local nudges answer "do not wait two minutes" at zero infrastructure; adaptive polling and the conditional heartbeat bound the cost of hundreds of idle tenants (500 idle tenants ≈ 17 polls per second per instance, each a handful of empty seeks); the drain rule follows the verified App Service limits — release beats graceful completion when the budget is 5 s, but never at the price of concurrent double execution.

**Alternatives rejected.** Cross-instance nudge through the SignalR backplane — abuses a client channel for server coordination for a latency win that only matters when the local instance is saturated. `sp_getapplock` for one poller per tenant — needs a held connection, fights pooling, and the row leases already serialize what matters. Quartz clustering — incompatible dialect, its own store, pinned for the identity server only. Releasing every in-flight lease at shutdown — double execution.

**Confidence.** High on the shape; medium on the default numbers (options, tuned with D13's instruments).

### D8 — Enqueue rides the batch; nudge and hub events fire after commit

**Decision.** `IJobQueue.Enqueue(batch, requests)` reserves ids from `sq_Job` through the allocator (seam 1, synchronous from its buffer), appends one `INSERT [core].[Job] (…) SELECT … FROM @rows` (the `Job` UDTT, one row per request) declaring that it writes `core.Job`, stores the current `Activity`'s `traceparent` on each row, and registers a post-commit callback that nudges the local poller for the tenant. The ids are returned synchronously so the caller can set `JobId` on its entity rows before the same batch inserts them. `EnqueueAsync(requests)` does the same in its own round trip for callers outside a batch. `JobRequest` carries `HandlerKey`, `Arguments` (serialized with the platform JSON options; ≤ 64 KB), `DueAt` (null = now), `RequestedById` (null = nobody to notify), `RunAsUserId` (defaults to the current user in a request scope; required in a background scope). Validation at enqueue: the key is registered on this instance; the run-as user is resolved; arguments serialize under the cap.

**Rationale.** Transactional enqueue plus immediate processing both hold only with a post-commit hook; app-assigned ids make the FK from business rows to jobs settable inside one batch.

**Alternatives rejected.** Enqueue after commit from the service — a crash between commit and enqueue loses the job. A trigger or DB-side outbox — logic in the database, banned.

**Confidence.** High.

### D9 — The scheduler: `core.Schedule` plus a state sibling, ticked inside the poll, Cronos, three missed policies and an overlap policy

**Decision.** Schedules are ordinary editable top-level entities (`core.Schedule`: system-versioned, four audit columns, multilingual name, `IsActive` through the activatable capability so activate/deactivate come for free) with a non-temporal sibling `core.ScheduleState` (`NextDueAt`, `LastFiredAt`, `LastScheduledFor`, `LastJobId`, `LastSkippedAt`, tick lease columns) inserted with the schedule. Cron expressions are five-field Unix cron parsed by Cronos 0.13.0 (`L`, `W`, `#`, `?`, `H` supported; no seconds — the poll ceiling is 30 s). `TimeZoneId` is an IANA or Windows id resolved with `TimeZoneInfo.FindSystemTimeZoneById`; null means the tenant's time zone from settings at fire time. Built-in schedules are declared by features (`jobs.AddBuiltInSchedule(key, cron, arguments, timeZoneId)`) and seeded through `HasData` in the reserved id band with `IsBuiltIn = 1`, `Code = HandlerKey`, `Name = HandlerKey`, `RunAsUserId` = the system user; on a built-in row only `CronExpression`, `TimeZoneId`, `IsActive`, `MissedPolicy`, `OverlapPolicy`, `CatchUpWindowMinutes` are editable and delete is refused. Platform code never changes a shipped built-in's seeded values (that would scaffold an `UpdateData` migration overwriting tenant edits); a changed default is a new key.

The tick rides the poll and costs one extra round trip only when something is due:

1. In the poll, claim due state rows:
```sql
DECLARE @now datetime2(3) = SYSUTCDATETIME();
DECLARE @due TABLE ([ScheduleId] int NOT NULL PRIMARY KEY);
WITH [d] AS (
    SELECT TOP (50) [s].*
    FROM [core].[ScheduleState] AS [s] WITH (READPAST, UPDLOCK, ROWLOCK)
    WHERE [s].[NextDueAt] IS NOT NULL AND [s].[NextDueAt] <= @now
      AND ([s].[LeaseExpiresAt] IS NULL OR [s].[LeaseExpiresAt] < @now)
    ORDER BY [s].[NextDueAt])
UPDATE [d] SET [LeaseToken] = @tick, [LeaseExpiresAt] = DATEADD(second, 30, @now)
OUTPUT [inserted].[ScheduleId] INTO @due;
SELECT [s].[Id], [s].[HandlerKey], [s].[ArgumentsJson], [s].[CronExpression], [s].[TimeZoneId], [s].[MissedPolicy],
       [s].[OverlapPolicy], [s].[CatchUpWindowMinutes], [s].[RunAsUserId], [s].[IsBuiltIn],
       [st].[NextDueAt], [st].[LastScheduledFor], [u].[IsActive] AS [OwnerIsActive], @now AS [Now]
FROM @due AS [d]
INNER JOIN [core].[Schedule] AS [s] ON [s].[Id] = [d].[ScheduleId]
INNER JOIN [core].[ScheduleState] AS [st] ON [st].[ScheduleId] = [d].[ScheduleId]
INNER JOIN [core].[User] AS [u] ON [u].[Id] = [s].[RunAsUserId];
```
2. In C#, per schedule, with `Now` from the database: `occurrences = CronExpression.GetOccurrences(NextDueAt, Now, zone, fromInclusive: true, toInclusive: true)`. **Missed policy** when more than one occurrence is due: `Coalesce` (default) fires one job with `ScheduledFor` = the latest occurrence; `ReplayAll` fires one job per occurrence not older than `CatchUpWindowMinutes` (default 1440); `Skip` fires only when the latest occurrence is within `2 × MaxPollInterval` of `Now` and drops the rest. When `Now − NextDueAt` exceeds `GapThreshold` (D10) every policy degrades to `Coalesce` for this tick and `tellma.schedules.missed` counts the drops. If `OwnerIsActive = 0` the schedule is paused instead (`IsActive = 0`, `PausedReason = 'owner_inactive'`, `NextDueAt = NULL`) and `core.schedule.paused` goes to administrators. If `GetNextOccurrence(Now, zone)` returns null the schedule is exhausted: paused with `PausedReason = 'exhausted'`. Otherwise `NextDueAt = GetNextOccurrence(Now, zone)` (strictly after now, so a window is never replayed twice).
3. Second round trip, one transaction: the job inserts with the **overlap policy** as a predicate, then the fenced state advance, the pause updates, the notifications, and a post-commit nudge:
```sql
DECLARE @now datetime2(3) = SYSUTCDATETIME();
DECLARE @fired TABLE ([ScheduleId] int NOT NULL, [JobId] int NOT NULL);
INSERT INTO [core].[Job] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [RunAsUserId], [RequestedById], [ScheduleId], [ScheduledFor], [CreatedAt])
OUTPUT [inserted].[ScheduleId], [inserted].[Id] INTO @fired
SELECT [r].[Id], [r].[HandlerKey], N'Pending', [r].[DueAt], 0, [r].[ArgumentsJson], [r].[RunAsUserId], [r].[RequestedById], [r].[ScheduleId], [r].[ScheduledFor], @now
FROM @jobs AS [r]                                                    -- Job UDTT
INNER JOIN [core].[Schedule] AS [s] ON [s].[Id] = [r].[ScheduleId]
WHERE [s].[OverlapPolicy] = N'Allow'
   OR NOT EXISTS (SELECT 1 FROM [core].[Job] AS [x] WHERE [x].[ScheduleId] = [r].[ScheduleId] AND [x].[Status] IN (N'Pending', N'Running'));
UPDATE [st]
SET [NextDueAt] = [n].[NextDueAt],
    [LastScheduledFor] = COALESCE([n].[LastScheduledFor], [st].[LastScheduledFor]),
    [LastFiredAt] = CASE WHEN [f].[ScheduleId] IS NOT NULL THEN @now ELSE [st].[LastFiredAt] END,
    [LastJobId] = COALESCE([f].[JobId], [st].[LastJobId]),
    [LastSkippedAt] = CASE WHEN [n].[Due] = 1 AND [f].[ScheduleId] IS NULL THEN @now ELSE [st].[LastSkippedAt] END,
    [LeaseToken] = NULL, [LeaseExpiresAt] = NULL
FROM [core].[ScheduleState] AS [st]
INNER JOIN @next AS [n] ON [n].[ScheduleId] = [st].[ScheduleId]     -- ScheduleNextList (ScheduleId, NextDueAt, LastScheduledFor, Due)
LEFT JOIN (SELECT [ScheduleId], MAX([JobId]) AS [JobId] FROM @fired GROUP BY [ScheduleId]) AS [f] ON [f].[ScheduleId] = [st].[ScheduleId]
WHERE [st].[LeaseToken] = @tick;
```
`RunAsUserId` on the inserted jobs is the schedule's; `RequestedById` is null for built-ins and the owner for user schedules. If the second round trip fails, the 30-second tick lease lapses and the next poll on any instance recomputes from the unchanged `NextDueAt` — at-least-once; the overlap predicate prevents duplicates for `Skip` schedules. Activating a schedule (the schedule service's post-persist hook, seam 3) sets `NextDueAt` to the next occurrence after now — a paused period is never caught up; saving recomputes `NextDueAt` from the new expression.

**Rationale.** The three-way missed policy maps onto Quartz (`FireOnceNow` / `IgnoreMisfirePolicy` / `DoNothing`), Hangfire (`Relaxed` / `Strict` / `Ignorable`), Airflow and Temporal vocabulary; the overlap policy is universal and was missing; riding the tick on the poll gives every tenant a scheduler at zero extra round trips when nothing is due; Cronos is the only candidate with documented time-zone and DST semantics.

**Alternatives rejected.** Cron strings on business entities — no single validation, tick or admin surface. Quartz — incompatible dialect (mandatory seconds, `?` rule) and a second persistence model. NCrontab — no time-zone support. A `Queue` overlap policy (Temporal's `BufferOne`) — achievable with `Allow` plus handler `MaxConcurrency = 1`. Catching up a paused period on activation (Kubernetes) — surprising for administrators who paused on purpose. Seeding built-ins at first poll instead of `HasData` — contradicts the fixed seed rule.

**Confidence.** High on the model; medium on `Skip`'s "just became due" window and on the 24-hour default catch-up window (review flags).

### D10 — The gap safeguard: per-tenant heartbeat, coalesced schedules, quarantined stale jobs, one admin decision

**Decision.** `core.JobWorkerState` (one row per tenant database) carries `LastTickAt` and `LastTickOwner`. Every poll's first block is:

```sql
SET NOCOUNT ON;
DECLARE @now datetime2(3) = SYSUTCDATETIME();
DECLARE @last datetime2(3) = (SELECT [LastTickAt] FROM [core].[JobWorkerState] WHERE [Id] = 1);
IF @last IS NOT NULL AND @last < DATEADD(minute, -@gapMinutes, @now)
    UPDATE [j]
    SET [Status] = N'Held', [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL, [ErrorCode] = N'held_after_gap'
    FROM [core].[Job] AS [j]
    WHERE [j].[Status] IN (N'Pending', N'Running')
      AND [j].[DueAt] < DATEADD(minute, -@gapMinutes, @now)
      AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < @now);
UPDATE [core].[JobWorkerState] SET [LastTickAt] = @now, [LastTickOwner] = @owner
WHERE [Id] = 1 AND ([LastTickAt] IS NULL OR [LastTickAt] < DATEADD(second, -60, @now));
SELECT @last AS [PreviousTickAt], @now AS [Now];
```

`GapThreshold` (`@gapMinutes`) defaults to 6 hours. When `PreviousTickAt` reports a gap, the worker logs the gap, increments `tellma.schedules.gap_detected`, and raises `core.scheduler.gap` to administrators with `DedupKey = 'core.scheduler.gap'` (two instances detecting the same gap produce one notification). Held rows (`Status = 'Held'`) are work that was already more than `GapThreshold` overdue when the worker came back — the thousands of stale rows a restored backup brings, or work queued before a long outage; they wait for an administrator's `resume` (→ `Pending`, `DueAt = now`, error cleared) or `cancel` on the Background jobs page. Rows due within the threshold and everything enqueued after the restart proceed normally. Schedules coalesce automatically on a gap (D9) — one late report is harmless; queued rows are not, because they carry side effects (emails, filings). The seed row has `LastTickAt = NULL` so a new tenant's first tick sees no gap.

**Rationale.** The brain dump's year-old-backup worry: a heartbeat detects the restore independently of any schedule; the two precedents (Temporal's catch-up window, Kubernetes' miss cut-off with an alert) both apply to schedules only, and a queue table needs its own guard. Holding rather than cancelling loses nothing and puts the decision with a human who knows whether the backup was a restore or a weekend outage.

**Alternatives rejected.** Alert only, keep claiming — re-sends a year of emails before anyone reads the alert. Pause the whole tenant worker until resumed — blocks new exports and imports for a weekend outage. Cancelling stale rows — a 7-hour-late invoice email is usually still wanted; the administrator decides.

**Confidence.** Medium-high. Review flag: the 6-hour default (an on-prem weekend maintenance window trips it; 72 h is the alternative) and whether holding should be opt-out per handler (`HoldAfterGap = false` on `[JobHandler]` for idempotent maintenance keys).

### D11 — Credentials: the system user for built-ins, the last saver for user schedules, the requester for user jobs; permissions evaluated at run time

**Decision.** Every job carries `RunAsUserId` (not null). Built-in schedules and platform batch handlers run as the seeded **system user** (`WellKnownUsers.SystemUserId`: a `core.User` row in the reserved band with no subject, `IsActive = 1`, cannot sign in, holds every permission — T4 seeds it). User-created schedules run as **the user who last saved them**: `RunAsUserId` is server-set to the current user on every save of a non-built-in schedule (an administrator editing someone's schedule takes it over — the UI says so), which removes any way to make a schedule run as someone else and needs no impersonation action. User-triggered jobs (export, import, anything a service enqueues on behalf of the caller) run as the requester. In every case the job scope's first round trip performs the connect step for `RunAsUserId` (tag reads and `IsActive`; it stamps no `LastActive`) and the handler evaluates permissions through the same evaluator a request uses; a job whose run-as user is inactive is completed as `Failed` with `ErrorCode = 'user_inactive'` without executing the handler, and its schedule (if any) pauses at the next tick (D9). Handlers of system jobs must not read data on behalf of a person; they operate on platform tables (retention, sweeps, dispatch) or on data the tenant as a whole owns. Admin `retry` of a dead-lettered job runs as the original `RunAsUserId`, never as the administrator.

**Rationale.** Built-in work has no natural human owner; a user-configured schedule must neither outlive the user's permissions nor grant more than they have; run-time evaluation with the owner as principal closes the "schedule a report on data I cannot see" hole by construction.

**Alternatives rejected.** Evaluating permissions at schedule time and caching a filter on the row — stale on every role change. A service account per schedule — heavy; identity-server service accounts are for external callers. An `impersonate` action letting an administrator set another owner — a second authorization path for no real need.

**Confidence.** High.

### D12 — Trace linkage: a new root per handler invocation, linked to each job's enqueuing context

**Decision.** `IJobQueue` stores the enqueuing `Activity`'s W3C `traceparent` in `Job.TraceParent` (55 characters; `tracestate` is not stored). The worker starts every handler invocation as a **new root** activity — `ActivitySource("Tellma.Jobs").StartActivity("process " + key, ActivityKind.Consumer, parentContext: default, tags, links)` with one `ActivityLink` per claimed row that has a `TraceParent` — so the enqueuing request's trace closes when the request does and its sampling decision does not decide the job's. The claim is a `Client` span `receive <key>`, the completion a `Client` span `settle <key>`. Bounded tags only: `messaging.system = "tellma.jobs"`, `messaging.destination.name = <key>`, `messaging.operation.name ∈ {receive, process, settle}`, `messaging.batch.message_count`, `tellma.jobs.attempt`; tenant id, job ids and user id go to the log scope, never to tags. Baggage is neither stored nor propagated; tenant, user, culture and time zone are explicit columns and scope values.

**Rationale.** The messaging semantic conventions: a span has one parent, a batch has many creation contexts, and a job that inherits the request trace keeps it open for hours. Azure Monitor serializes links into `_MS.links`; cross-trace navigation is a KQL join, documented as such.

**Alternatives rejected.** Child-of-request spans; baggage propagation (the OTel security note, and the fixed fact that `AsyncLocal` must not carry tenant context).

**Confidence.** High.

### D13 — Telemetry names

**Decision.** Constants in `Tellma.Core.Abstractions.Jobs.JobsTelemetry` (meter `Tellma.Jobs`) and `…Notifications.NotificationsTelemetry` (meter `Tellma.Notifications`). Tags: `handler` (the key; bounded by the registry), `outcome ∈ {succeeded, retry, failed, released, cancelled, lease_lost, held}`, `policy ∈ {coalesce, replay_all, skip}`, `type` (notification type key), `event` (hub event name). The tenant is never a tag.

| Instrument | Kind, unit | Tags | Answers |
|---|---|---|---|
| `tellma.jobs.claimed` | counter, `{job}` | handler | throughput |
| `tellma.jobs.completed` | counter, `{job}` | handler, outcome | failure and dead-letter rate |
| `tellma.jobs.duration` | histogram, `s` | handler | lease sizing (compare with `LeaseSeconds`) |
| `tellma.jobs.queue_latency` | histogram, `s` (`StartedAt − DueAt`) | handler | backed-up queues, poll interval too long |
| `tellma.jobs.batch_fill` | histogram, `1` (claimed ÷ `BatchSize`) | handler | batch size mis-configured |
| `tellma.jobs.in_flight` | up-down counter, `{job}` | handler | concurrency |
| `tellma.jobs.lease_lost` | counter, `{batch}` | handler | handlers slower than their lease |
| `tellma.jobs.backlog_age` | histogram, `s` | handler | starvation (the per-minute sample, D7) |
| `tellma.jobs.poll.duration` | histogram, `s` | — | poll cost per tenant |
| `tellma.jobs.orphaned` | counter, `{job}` | — | pending rows older than an hour whose key has no handler on this instance (hourly scan) |
| `tellma.schedules.fired` | counter, `{job}` | policy | firing volume |
| `tellma.schedules.missed` | counter, `{occurrence}` | policy | dropped or coalesced occurrences |
| `tellma.schedules.overlap_skipped` | counter, `{firing}` | — | schedules whose interval is shorter than their run |
| `tellma.schedules.gap_detected` | counter, `{tick}` | — | restore or long outage |
| `tellma.notifications.created` | counter, `{notification}` | type | fan-out volume |
| `tellma.realtime.events` | counter, `{event}` | event | push volume |

Alert queries under `infra/monitoring/` (cross-checked by the existing test): dead-letter rate, `backlog_age > 10 min`, `gap_detected > 0`, and a log-based "heartbeat older than 2 minutes" per tenant.

**Confidence.** High on names; medium on `batch_fill` earning its keep.

### D14 — First consumers: export, import, the blob staging sweep, retention, and the email outbox alignment

**Decision.**

*Export (T9 owns the entity; this is the contract it must meet).* `core.Export` is a top-level entity: `Id`, `Resource` (securable resource name), `Kind ∈ {Display, ForImport}`, `RequestJson` (the query or the ids plus the select, verbatim), `FileName`, `BlobId` (null until produced; seam 12), `RowCount`, `ExpiresAt` (default now + 7 days), `JobId` (`IJobEntity`), `CreatedAt`, `CreatedById`. The export endpoint decides synchronous versus background by the estimated row count (T9's threshold; the capped count is already available); background means one batch inserting the `Export` row and its job (`core.export`, `RunAsUserId = RequestedById = current user`) and returning `{ exportId, jobId }`. The `core.export` handler (`BatchSize = 1`, `LeaseSeconds = 600`) re-runs authorization in the job scope, streams pages of the query into the Excel writer, reports progress per page, writes the file through the blob service, and in its completion batch updates `Export.BlobId/RowCount` and raises `core.export.ready` to the requester (target `core.exports`, id = export id; inbox channel not mutable). The user's "My exports" page lists `core.Export` under the self-scope criterion `CreatedById = me()`; the file is served by the etag-validated blob endpoint under the same criterion — the notification is a convenience, the export row is the durable access path. Built-in schedule `core.export-retention` (daily, `0 4 * * *`) deletes expired exports and, post-commit, their blobs.

*Import (T9).* `core.Import`: `Id`, `Resource`, `Mode ∈ {Insert, Update, Merge}`, `FileBlobId` (the uploaded sheet, staged through the blob staging token), `ResultBlobId` (annotated error sheet, null when clean), `RowCount`, `ErrorCount`, `JobId`, `CreatedAt`, `CreatedById`. Small files run inline; large files enqueue `core.import` (`BatchSize = 1`, `LeaseSeconds = 600`, `MaxAttempts = 1` — a partially committed import must never be re-run blindly; a crash mid-import surfaces as `attempts_exhausted`). The handler runs the bulk pipeline as the requester and raises `core.import.completed` (with counts) or `core.import.failed`. Per-chunk versus single-transaction commit is T9's decision; the job contract supports both through `StateJson` checkpoints. The import upload's staging TTL must exceed the queue latency budget (24 h, distinct from image staging).

*Blob staging sweep (T7).* Built-in schedule `core.blob-staging-sweep` (`15 * * * *`), argument-only handler shipped by the blob feature, deletes staged blobs older than the staging TTL in pages of 500, reports progress, runs as the system user.

*Retention.* `core.job-retention` (`0 3 * * *`) deletes `Succeeded` jobs older than `SucceededRetention` (30 days) and `Failed`/`Cancelled` older than `FailedRetention` (180 days) in pages of 1,000 (referencing rows `SET NULL`); `core.notification-retention` (`30 3 * * *`) deletes read notifications older than 90 days and unread older than 365 days. `Held` rows are never deleted by retention.

*Email outbox (spec 0007's reserved shape; a later spec).* The outbox row implements `IJobEntity`; its handler is `IJobHandler<EmailOutboxMessage>` with `BatchSize = 100`, `LeaseSeconds = 120`, `MaxConcurrency = 1` per tenant (order and quota pacing), enqueued with `RunAsUserId` = the system user and `RequestedById` = the enqueuing user; `EmailEnqueueRequest.SendAfter` maps to `JobRequest.DueAt`; per-row outcomes map from `EmailSendResult`; its "signal after commit" is the post-commit nudge of D8. The reserved shape in spec 0007 needs no change.

**Rationale.** Each consumer exercises a different corner: export is the long single job with progress and a notification; import is the non-retryable job; the sweep is the argument-only built-in; retention keeps the queue table small; the outbox is the batch-shaped entity-backed handler.

**Confidence.** High on the shapes; Excel thresholds are T9's.

### D15 — Notifications: the entity, the type registry, preferences, and `INotifier` riding the batch

**Decision.** `core.Notification` (§4) is one row per recipient, written only by `INotifier`, immutable except `ReadAt`. A row stores its **type key**, **arguments** (JSON ≤ 4,000 characters), an optional **target** (`TargetResource` securable name + `TargetId`), an optional **actor** and an optional **dedup key**; it stores no rendered text. Rendering happens at read time from the type's ICU message in the reader's UI language (server-side for the summary endpoint, MCP and a future email digest; client-side for the SPA list, which ships the same strings), so a user who switches language sees old notifications in the new one and the insert needs no per-recipient language lookup. The **type registry** is populated once per feature (`notifications.AddType(descriptor)`), validated at startup (unique keys, D2 grammar), and exposed to the preferences page; types with `Mutable = false` (`core.export.ready`, `core.import.completed`, `core.import.failed`, `core.job.failed`, `core.job.held`, `core.schedule.paused`, `core.scheduler.gap`) cannot have their inbox channel disabled; the email channel is always user-controlled. **Preferences** live in `core.NotificationPreference (UserId, Type, Inbox, Email)`; a missing row means the type's defaults (inbox on, email off); a push channel is added later as an additive column when a push transport exists (channel addresses — contact email, mobile — stay on T4's `User`). `INotifier.Notify(batch, requests)` appends, per request, one statement over the standalone type `NotificationRowList (Id int PK, UserId int)` whose ids were reserved from `sq_Notification` for every recipient (ids filtered out by the predicates are simply unused):

```sql
INSERT INTO [core].[Notification] ([Id], [UserId], [Type], [ArgumentsJson], [TargetResource], [TargetId], [ActorUserId], [DedupKey], [CreatedAt])
OUTPUT [inserted].[UserId]
SELECT [r].[Id], [r].[UserId], @type, @args, @targetResource, @targetId, @actorId, @dedupKey, SYSUTCDATETIME()
FROM @rows AS [r]
WHERE EXISTS (SELECT 1 FROM [core].[User] AS [u] WHERE [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1)
  AND (@mutable = 0 OR NOT EXISTS (SELECT 1 FROM [core].[NotificationPreference] AS [p]
                                   WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = @type AND [p].[Inbox] = 0))
  AND (@dedupKey IS NULL OR NOT EXISTS (SELECT 1 FROM [core].[Notification] AS [n]
                                        WHERE [n].[UserId] = [r].[UserId] AND [n].[DedupKey] = @dedupKey AND [n].[ReadAt] IS NULL));
```

and registers a post-commit callback publishing `inbox.changed` to the user ids the `OUTPUT` returned. Dedup is best effort (a non-unique filtered index; two concurrent saves can both insert — a unique index would fail a business save over a notification race, which is worse). The email channel is added by the outbox spec as a second statement appended by the same call (an insert into the outbox from the same `@rows` joined against `Email = 1` preferences), which is why the call is batch-shaped from day one.

**Rationale.** Zero extra round trips for fan-out (the brain dump's hard requirement for "assigned to you"); language-neutral rows; one registry feeding the preferences page and the mute rule; SQL-side predicates are declarative filtering, not logic in the database.

**Alternatives rejected.** Rendering the title at creation in the recipient's language — a lookup per recipient and a frozen language. One JSON "notification settings" blob on the user — cannot be joined in the insert predicate. `InboxItem` — the row outlives the inbox (email, push, MCP). A `Push` column now — nothing reads it.

**Confidence.** High on the model; medium on read-time rendering. Review flag: the notifications search page cannot sort or filter on a rendered message under read-time rendering; if that is required, add a `Title` column rendered in the tenant's primary language at creation (one column, no lookup) and keep arguments for the click action.

### D16 — Counters and the inbox API: capped counts, one `InboxSeenAt` timestamp

**Decision.** T4's non-temporal user sibling table (referred to here as `core.UserState`) gains `InboxSeenAt datetime2(3) NULL`. Unseen = notifications with `CreatedAt > InboxSeenAt` (all when null); unread = `ReadAt IS NULL`. One endpoint, one round trip, returns `InboxSummary { Unseen, Unread, Latest[10] }`:

```sql
DECLARE @seenAt datetime2(3) = (SELECT [InboxSeenAt] FROM [core].[UserState] WHERE [UserId] = @me);
SELECT
  (SELECT COUNT(*) FROM (SELECT TOP (100) 1 AS [x] FROM [core].[Notification] WHERE [UserId] = @me AND [CreatedAt] > COALESCE(@seenAt, '0001-01-01')) AS [a]) AS [Unseen],
  (SELECT COUNT(*) FROM (SELECT TOP (100) 1 AS [x] FROM [core].[Notification] WHERE [UserId] = @me AND [ReadAt] IS NULL) AS [b]) AS [Unread];
SELECT TOP (10) [Id], [Type], [ArgumentsJson], [TargetResource], [TargetId], [ActorUserId], [CreatedAt], [ReadAt]
FROM [core].[Notification] WHERE [UserId] = @me ORDER BY [CreatedAt] DESC, [Id] DESC;
```

The badge shows "99+" past the cap. `inbox/seen` runs `UPDATE [core].[UserState] SET [InboxSeenAt] = SYSUTCDATETIME() WHERE [UserId] = @me`; `inbox/read { ids }` runs `UPDATE [core].[Notification] SET [ReadAt] = SYSUTCDATETIME() WHERE [UserId] = @me AND [ReadAt] IS NULL AND [Id] IN (SELECT [Id] FROM @ids)`; `inbox/read-all` the same without the `IN`. Each is one round trip and publishes `inbox.changed` to the same user (other tabs and devices update). The client calls the summary at load and on every `inbox.changed`. Clicking an item marks it read and navigates to the route the client maps from `TargetResource`/`TargetId` (the type descriptor fixes the resource for types that have one). The notifications page is the standard query endpoint over `core.Notification` under the platform-registered self-scope criterion `UserId = me()`; no role permission exists or is honoured for reading others' notifications.

**Rationale.** Counters on the user row churn; maintained counters drift and need reconciliation; a capped count over `(UserId, CreatedAt)` and the filtered unread index is O(100).

**Alternatives rejected.** Maintained counters bumped by insert and mark-read — drift under retries and dedup skips. Counts on the connect call — per-request cost for a value that changes rarely; events invalidate better.

**Confidence.** High.

### D17 — The hub: one `TellmaHub`, server-assigned per-tenant and per-(tenant, user) groups, thin events, an abstraction for publishers

**Decision.** One hub class, `TellmaHub`, mapped at `/{tenantId}/hub` (route ownership T6) under the cookie scheme; the negotiate/connect request runs tenant resolution and the connect step exactly like an API request, refuses non-members, and `OnConnectedAsync` adds the connection to `t{tenantId}` and `t{tenantId}.u{userId}` — both computed server-side from the authenticated context, never from client input, which is what makes groups safe here although groups are not a security feature. The `IUserIdProvider` returns the identity `sub` so the per-user close APIs address the right connections; self-hosted, a `ConnectionTracker` maps `(tenantId, userId) → connection ids`. Publishers never see SignalR: `IClientEventPublisher.Publish(batch?, event)` (post-commit when a batch is given, immediate otherwise) sends `Clients.Groups(names).SendAsync("event", { name, tenantId, payload })`, payload ids only; an event with no user ids goes to `t{tenantId}`. Event catalogue registered like notification types: `inbox.changed`, `job.changed { jobId }` (published on claim, on renewals that carried progress, and on completion, only when `RequestedById` is set), `cache.changed { tag }` for T3's cacheable-entity invalidation, `session.ended`. `CloseOnAuthenticationExpiration = true`. Hosting: `AddAzureSignalR()` when `Azure:SignalR:ConnectionString` is configured (SaaS; `ClaimsProvider` pruned to `sub` per spec 0003), otherwise in-process SignalR; on-prem with two or more instances requires `AddStackExchangeRedis` with channel prefix = `DeploymentIdentity.DeploymentId` and sticky sessions (a single on-prem instance needs nothing). `IHubConnectionCloser.CloseAsync(tenantId, userId)` is what T1's session-end hooks call: on Azure `users/{sub}/:closeConnections` through the SDK; self-hosted, abort the tracked connections.

**Rationale.** Spec 0003's rules (cookie at connect, thin events, close on session end, hub-only Azure token, `CloseOnAuthenticationExpiration`) honoured verbatim; per-(tenant, user) groups give tenant isolation without a hub per tenant; the abstraction keeps SignalR out of Abstractions and out of handlers.

**Alternatives rejected.** `Clients.User(sub)` — a user open in two tenants would receive both tenants' signals. A hub per tenant — N hubs of Azure SignalR configuration for nothing groups do not give. Server-to-server pub-sub over the hub — rejected in D7.

**Confidence.** High.

### D18 — Two specs: 0019 is jobs and the scheduler; 0020 is notifications and the hub

**Decision.** 0019 owns D1–D14 and `core.Job`, `core.Schedule`, `core.ScheduleState`, `core.JobWorkerState`; 0020 owns D15–D17, `core.Notification`, `core.NotificationPreference`, `TellmaHub`, and the `InboxSeenAt` column. 0019 depends on 0020 only through `INotifier` and `IClientEventPublisher` (contracts in Abstractions; 0019 ships with no-op registrations until 0020 lands); 0020 does not depend on 0019.

**Rationale.** Two audiences (operators and consumers of background work; end users and the SPA), two hosting concerns (Always On and drain; Azure SignalR and Redis), and together too large for one frozen spec; the seam is two small interfaces.

**Confidence.** High.

### D19 — Securables, endpoints and pages

**Decision.** Registered by the platform features in T4's registry. `core.jobs`: `read` (filterable), `retry`, `cancel`, `resume`; the self-scope bespoke criterion `RequestedById = me()` grants `read` and `cancel` on one's own jobs without a role. `core.schedules`: `read`, `save`, `delete`, `activate` (filterable). `core.notifications`: self-scope only. Endpoints (T6 projects them): the standard CRUD set for schedules; query, details, `retry`, `cancel`, `resume` for jobs; `inbox/summary`, `inbox/seen`, `inbox/read`, `inbox/read-all`, query for notifications; `notification-preferences` get/save (self). Pages: Schedules (standard search + details), Background jobs (admin search with status/handler/age filters, the worker heartbeat banner, and a "held after gap" banner with resume/cancel), My jobs, Notifications (standard search), Notification preferences. MCP (T6's sketch): `get_job`, `list_notifications`.

**Confidence.** High.

### D20 — Testing

**Decision.** `test/core/Tellma.Core.Jobs.Tests`: every emitted statement against LocalDB with RCSI on and off (claim under contention from two connections, renewal fencing, completion fencing, the gap quarantine, filtered-index plan assertions); the worker with `FakeTimeProvider` (adaptive interval, nudge coalescing, drain rules); Cronos policies with fixed zones and DST transitions; attempts semantics (crash-loop exhaustion, release un-counting). `test/core/Tellma.Core.Notifications.Tests`: the notifier statement (mute, dedup, inactive recipient), the summary counts, the hub with an in-memory `IClientEventPublisher` fake and the self-hosted `ConnectionTracker`. `Tellma.Core.Notifications.IntegrationTests` (`Category=Integration`, `Live=true`) against a real Azure SignalR instance for the close-connections path.

**Confidence.** High.

## 3. Contracts

### 3.1 `Tellma.Core.Abstractions.Jobs`

```contract
enum JobStatus = Pending | Running | Succeeded | Failed | Cancelled | Held

// Entity: table core.Job (§4). Written only by the jobs runtime.
data Job
  Id: int                                   server-owned
  HandlerKey: string
  Status: JobStatus                         // stored as string
  DueAt: datetime2(3)
  Attempts: int
  LeaseToken: Guid?
  LeaseExpiresAt: datetime2(3)?
  LeaseOwner: string?
  StartedAt: datetime2(3)?
  CompletedAt: datetime2(3)?
  CancelRequestedAt: datetime2(3)?
  ArgumentsJson: string?
  StateJson: string?
  ProgressPercent: byte?
  ProgressMessage: string?
  ErrorCode: string?
  ErrorMessage: string?
  ErrorDetails: string?
  TraceParent: string?
  RunAsUserId: int                          FK -> core.User
  RequestedById: int?                       FK -> core.User
  ScheduleId: int?                          FK -> core.Schedule
  ScheduledFor: datetime2(3)?
  CreatedAt: datetime2(3)

// Capability of an entity whose rows are processed one job per row.
contract IJobEntity
  JobId: int?                               FK -> core.Job, ON DELETE SET NULL, unique where not null

annotation [JobHandler(Key, BatchSize = 1, LeaseSeconds = 300, MaxAttempts = 5, MaxConcurrency = 1, RetryBaseSeconds = 30, RetryMaxSeconds = 3600)]   on type

// Implemented by platform and distribution handlers; resolved from the job scope.
contract IJobHandler<TItem>
    where TItem: Job | IJobEntity
  Execute(batch: JobBatch<TItem>) -> void   // the operation's cancellation token is the batch token

record JobBatch<TItem>
  Items: list<JobItem<TItem>>
  Single: JobItem<TItem>                    // throws when Items has more than one
  Batch: IBatchBuilder                      // statements appended here run in the completion transaction
  RunAsUserId: int                          // the partition's user (D3)
  CancellationReason: JobCancellationReason

record JobItem<TItem>
  Job: Job
  Item: TItem                               // the Job itself when TItem is Job
  CancellationToken                         // batch token linked with this item's cancel request
  Progress: IJobProgress
  Arguments<TArgs>() -> TArgs               sync   // tolerant JSON: unknown members ignored, missing defaulted
  State<TState>() -> TState?                sync
  Succeed()                                 sync
  Retry(after: TimeSpan?, error: JobError?) sync
  Fail(error: JobError)                     sync
  NotifyOnSuccess(request: NotificationRequest) sync

service IJobProgress
  Report(percent: byte?, message: string?, state: object?) sync   // buffered; rides the next renewal
  Flush() -> void                                                 // writes now (checkpoint)

record JobError(Code: string, Message: string, Details: string?)
enum JobCancellationReason = None | LeaseLost | CancelRequested | HostStopping
// Exception type: JobFailedException(Error: JobError) — fails every unmarked item permanently.

record JobRequest(HandlerKey: string, Arguments: object?, DueAt: DateTimeOffset?, RequestedById: int?, RunAsUserId: int?)

service IJobQueue
  Enqueue(batch: IBatchBuilder, requests: list<JobRequest>) -> list<int>   sync   // ids in request order
  EnqueueAsync(requests: list<JobRequest>) -> list<int>

// Feature-time registration (seam 6).
contract IJobsRegistrar
  AddHandler<THandler>() -> IJobsRegistrar                                                         sync
  AddBuiltInSchedule(handlerKey: string, cron: string, arguments: object?, timeZoneId: string?) -> IJobsRegistrar   sync

data JobsOptions                             // bound from Tellma:Jobs
  Enabled: bool = true
  MaxParallelBatches: int = 8
  MinPollInterval: TimeSpan = 1s
  MaxPollInterval: TimeSpan = 30s            // must be ≤ 30 s (cron granularity ceiling)
  DrainTimeout: TimeSpan = 4s
  GapThreshold: TimeSpan = 6h
  SucceededRetention: TimeSpan = 30d
  FailedRetention: TimeSpan = 180d

enum MissedPolicy = Coalesce | ReplayAll | Skip
enum OverlapPolicy = Skip | Allow

// Entity: table core.Schedule (§4); carries AuditedEntity, ActivatableEntity, MultilingualEntity (T2's bases).
data Schedule
  Id: int
  Code: string?                              unique where not null; equals HandlerKey on built-ins
  Name: string; Name2: string?; Name3: string?
  HandlerKey: string                         write-once on built-ins
  ArgumentsJson: string?
  CronExpression: string                     // five-field Unix cron, Cronos dialect
  TimeZoneId: string?                        // null = tenant time zone at fire time
  MissedPolicy: MissedPolicy = Coalesce
  OverlapPolicy: OverlapPolicy = Skip
  CatchUpWindowMinutes: int = 1440
  RunAsUserId: int                           server-owned (D11), FK -> core.User
  IsBuiltIn: bool                            server-owned
  PausedReason: string?                      server-owned; 'owner_inactive' | 'exhausted'
  IsActive: bool = true

// Instrument names (D13); the const home for alert cross-checks.
data JobsTelemetry
  MeterName = "Tellma.Jobs"; ActivitySourceName = "Tellma.Jobs"
  Claimed, Completed, Duration, QueueLatency, BatchFill, InFlight, LeaseLost, BacklogAge, PollDuration, Orphaned,
  SchedulesFired, SchedulesMissed, SchedulesOverlapSkipped, SchedulesGapDetected
  TagHandler = "handler"; TagOutcome = "outcome"; TagPolicy = "policy"
```

| Member | Meaning |
|---|---|
| `IJobHandler.Execute` | Processes one partition of a claim (D3). Unmarked items succeed on return, retry on exception; cancellation maps by `CancellationReason`. At-least-once. |
| `JobBatch.Batch` | Executed in the completion transaction with the outcomes (D6); statements must be idempotent or `MayRetry = false`. |
| `JobItem.Retry` | `after` null ⇒ full-jitter backoff from the attribute; dead-letters when attempts are exhausted. |
| `IJobQueue.Enqueue` | Reserves ids synchronously, appends the insert, registers the post-commit nudge (D8). |
| `IJobsRegistrar.AddHandler` | Reads `[JobHandler]`, infers `TItem`, validates key grammar, options and (for entity types) `IJobEntity`; duplicate keys fail startup. |
| `IJobsRegistrar.AddBuiltInSchedule` | Declares a `HasData` seed in the reserved band (D9). |

### 3.2 `Tellma.Core.Abstractions.Notifications` and `…Realtime`

```contract
// Entity: table core.Notification (§4). Written only by INotifier; immutable except ReadAt.
data Notification
  Id: int; UserId: int; Type: string; ArgumentsJson: string?
  TargetResource: string?; TargetId: long?; ActorUserId: int?; DedupKey: string?
  CreatedAt: datetime2(3); ReadAt: datetime2(3)?

// Entity: table core.NotificationPreference; self-service rows.
data NotificationPreference
  UserId: int; Type: string; Inbox: bool = true; Email: bool = false

record NotificationTypeDescriptor(Key: string, Category: string, Mutable: bool, TargetResource: string?)
record NotificationRequest(Type: string, RecipientIds: list<int>, Arguments: object?, TargetResource: string?, TargetId: long?, ActorUserId: int?, DedupKey: string?)

service INotifier
  Notify(batch: IBatchBuilder, requests: list<NotificationRequest>)   sync   // appends the D15 insert(s); hub event after commit
  NotifyAsync(requests: list<NotificationRequest>) -> void                   // own round trip

contract INotificationsRegistrar
  AddType(descriptor: NotificationTypeDescriptor) -> INotificationsRegistrar   sync

service INotificationRenderer
  Render(notification: Notification, uiCulture: CultureInfo) -> string   sync   // ICU template of the type

record InboxSummary(Unseen: int, Unread: int, Latest: list<Notification>)

data NotificationsTelemetry
  MeterName = "Tellma.Notifications"
  Created = "tellma.notifications.created"; RealtimeEvents = "tellma.realtime.events"
  TagType = "type"; TagEvent = "event"

record ClientEvent(Name: string, UserIds: list<int>, Payload: object?)   // empty UserIds = every connected user of the tenant

service IClientEventPublisher
  Publish(batch: IBatchBuilder?, clientEvent: ClientEvent)   sync   // post-commit hook when batch given, else immediate
  PublishAsync(clientEvent: ClientEvent) -> void

service IHubConnectionCloser
  Close(tenantId: int, userId: int) -> void

contract IClientEventsRegistrar
  AddEvent(name: string) -> IClientEventsRegistrar   sync
```

### 3.3 Needed from other themes' seams (the exact members this theme calls)

```contract
// Seam 1 — batch abstraction (T2).
service IBatchBuilder
  AddSql(sql: string, parameters: list<SqlParameterSpec>, mayRetry: bool, writes: list<string>) -> IBatchResultHandle
  Tvp<TRow>(name: string, rows: list<TRow>) -> SqlParameterSpec        sync   // standalone or table-derived type, metadata-driven ordinals
  ReserveIds<TEntity>(count: int) -> list<int>                          sync   // from the allocator buffer
  OnCommitted(callback: (IBatchResults) -> void)                        sync   // after commit, outside the transaction; failures logged, never thrown
  SelectByJoin<TEntity>(fkColumn: string, tableVariable: string) -> IBatchResultHandle<list<TEntity>>   sync
service IBatchExecutor
  Execute(batch: IBatchBuilder, transactional: bool) -> IBatchResults   // transactional = one READ COMMITTED transaction; never SNAPSHOT

// Seam 9 — request context in background scopes (T1).
service ITenantScopeFactory
  CreateScope(tenantId: int, userId: int) -> IServiceScope   sync   // tenant holder, ISandboxContext, user, culture, calendar, time zone set; no AsyncLocal
service ITenantRegistry
  ListTenants() -> list<TenantDescriptor>

// Seam 11 — permissions and users (T4).
service IPermissionEvaluator
  Evaluate(resource: string, action: string) -> PermissionDecision
service IConnectStep
  Run(userId: int, stampActivity: bool) -> ConnectResult     // tags + IsActive; stampActivity = false in job scopes
data WellKnownUsers
  SystemUserId: int                                          // reserved band
service IAdministratorDirectory
  GetAdministratorIds() -> list<int>                         // users holding every permission, capped at 20

// Seam 12 — blobs (T7): batch write/read/delete; the export handler writes one blob and records its id.
```

## 4. Schema

All tables live in the tenant database, schema `core`. Timestamps are `datetime2(3)` UTC. Every enum column is a string with a `CHECK` on the closed set. Table types are derived from the entity classes per spec 0001; standalone types are listed at the end.

### `core.Job` — non-temporal; `LOCK_ESCALATION = DISABLE`; no triggers; `[TableType]` (all columns)

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered, sequence `sq_Job` | |
| `HandlerKey` | `nvarchar(64)` | no | | D2 grammar |
| `Status` | `nvarchar(16)` | no | `CHECK IN ('Pending','Running','Succeeded','Failed','Cancelled','Held')` | |
| `DueAt` | `datetime2(3)` | no | | earliest claim time; moved by retry backoff |
| `Attempts` | `int` | no | `DEFAULT 0` | claim counter (D6) |
| `LeaseToken` | `uniqueidentifier` | yes | | fencing token |
| `LeaseExpiresAt` | `datetime2(3)` | yes | | |
| `LeaseOwner` | `nvarchar(128)` | yes | | `DeploymentId`/instance id/process start; diagnostic only |
| `StartedAt` | `datetime2(3)` | yes | | first claim |
| `CompletedAt` | `datetime2(3)` | yes | | terminal time |
| `CancelRequestedAt` | `datetime2(3)` | yes | | |
| `ArgumentsJson` | `nvarchar(max)` | yes | | ≤ 64 KB, enforced at enqueue |
| `StateJson` | `nvarchar(max)` | yes | | handler checkpoint |
| `ProgressPercent` | `tinyint` | yes | `CHECK (ProgressPercent <= 100)` | |
| `ProgressMessage` | `nvarchar(256)` | yes | | handler-supplied, not localized |
| `ErrorCode` | `nvarchar(64)` | yes | | `user_inactive`, `attempts_exhausted`, `entity_missing`, `held_after_gap`, handler codes |
| `ErrorMessage` | `nvarchar(1024)` | yes | | |
| `ErrorDetails` | `nvarchar(max)` | yes | | |
| `TraceParent` | `nvarchar(55)` | yes | | W3C `traceparent` |
| `RunAsUserId` | `int` | no | FK `core.User` | |
| `RequestedById` | `int` | yes | FK `core.User` | |
| `ScheduleId` | `int` | yes | FK `core.Schedule` `ON DELETE SET NULL` | |
| `ScheduledFor` | `datetime2(3)` | yes | | the cron occurrence |
| `CreatedAt` | `datetime2(3)` | no | | |

Constraint `CK_Job_Lease`: `(Status = 'Running' AND LeaseToken IS NOT NULL AND LeaseExpiresAt IS NOT NULL) OR (Status <> 'Running' AND LeaseToken IS NULL AND LeaseExpiresAt IS NULL)`. Indexes: `IX_Job_Available (HandlerKey, DueAt, Id) INCLUDE (LeaseExpiresAt) WHERE Status IN (N'Pending', N'Running')`; `IX_Job_Lease (LeaseToken) INCLUDE (Status) WHERE LeaseToken IS NOT NULL`; `IX_Job_ScheduleActive (ScheduleId) WHERE Status IN (N'Pending', N'Running') AND ScheduleId IS NOT NULL`; `IX_Job_RequestedBy (RequestedById, CreatedAt DESC) WHERE RequestedById IS NOT NULL`; `IX_Job_Retention (Status, CompletedAt) WHERE Status IN (N'Succeeded', N'Failed', N'Cancelled')`; `IX_Job_Held (Id) WHERE Status = N'Held'`.

### `core.Schedule` — system-versioned → `core.ScheduleHistory`; carries the audited, activatable and multilingual bases; `[TableType]`

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered, sequence `sq_Schedule` | built-ins in the reserved band |
| `Code` | `nvarchar(64)` | yes | unique filtered `WHERE Code IS NOT NULL` | |
| `Name` | `nvarchar(256)` | no | | |
| `Name2` / `Name3` | `nvarchar(256)` | yes | | gated by tenant languages |
| `HandlerKey` | `nvarchar(64)` | no | | write-once on built-ins |
| `ArgumentsJson` | `nvarchar(max)` | yes | | |
| `CronExpression` | `nvarchar(128)` | no | | validated by Cronos on save |
| `TimeZoneId` | `nvarchar(64)` | yes | | |
| `MissedPolicy` | `nvarchar(16)` | no | `DEFAULT 'Coalesce'`, `CHECK IN ('Coalesce','ReplayAll','Skip')` | |
| `OverlapPolicy` | `nvarchar(16)` | no | `DEFAULT 'Skip'`, `CHECK IN ('Skip','Allow')` | |
| `CatchUpWindowMinutes` | `int` | no | `DEFAULT 1440`, `CHECK (> 0)` | |
| `RunAsUserId` | `int` | no | FK `core.User` | server-owned (D11) |
| `IsBuiltIn` | `bit` | no | `DEFAULT 0` | |
| `PausedReason` | `nvarchar(32)` | yes | `CHECK IN ('owner_inactive','exhausted')` | |
| `IsActive` | `bit` | no | `DEFAULT 1` | |
| `CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById` | | no | FKs `core.User` | `ModifiedAt` is the concurrency token (T2) |
| `ValidFrom` / `ValidTo` | `datetime2(7)` | no | period columns | shadow properties |

Index `IX_Schedule_HandlerKey (HandlerKey)`.

### `core.ScheduleState` — non-temporal sibling; one row per schedule, inserted with it

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `ScheduleId` | `int` | no | PK clustered; FK `core.Schedule` `ON DELETE CASCADE` | |
| `NextDueAt` | `datetime2(3)` | yes | | null while inactive or paused |
| `LastFiredAt` | `datetime2(3)` | yes | | |
| `LastScheduledFor` | `datetime2(3)` | yes | | |
| `LastJobId` | `int` | yes | FK `core.Job` `ON DELETE SET NULL` | |
| `LastSkippedAt` | `datetime2(3)` | yes | | overlap skips |
| `LeaseToken` | `uniqueidentifier` | yes | | tick lease |
| `LeaseExpiresAt` | `datetime2(3)` | yes | | |

Index `IX_ScheduleState_Due (NextDueAt) WHERE NextDueAt IS NOT NULL`.

### `core.JobWorkerState` — single row; the tenant's heartbeat

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK, `CHECK (Id = 1)` | seeded |
| `LastTickAt` | `datetime2(3)` | yes | | null until the first tick |
| `LastTickOwner` | `nvarchar(128)` | yes | | |

### `core.Notification` — non-temporal; written by `INotifier` only; `[TableType]`

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered, sequence `sq_Notification` | |
| `UserId` | `int` | no | FK `core.User` | recipient |
| `Type` | `nvarchar(64)` | no | | registered type key |
| `ArgumentsJson` | `nvarchar(4000)` | yes | | |
| `TargetResource` | `nvarchar(64)` | yes | | securable resource name |
| `TargetId` | `bigint` | yes | | |
| `ActorUserId` | `int` | yes | FK `core.User` | |
| `DedupKey` | `nvarchar(128)` | yes | | |
| `CreatedAt` | `datetime2(3)` | no | | |
| `ReadAt` | `datetime2(3)` | yes | | null while unread |

Indexes: `IX_Notification_User (UserId, CreatedAt DESC) INCLUDE (ReadAt, Type)`; `IX_Notification_Unread (UserId) WHERE ReadAt IS NULL`; `IX_Notification_Dedup (UserId, DedupKey) WHERE DedupKey IS NOT NULL AND ReadAt IS NULL` (non-unique; the insert predicate enforces best-effort); `IX_Notification_Retention (ReadAt, CreatedAt)`.

### `core.NotificationPreference` — non-temporal; self-service rows

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `UserId` | `int` | no | PK part; FK `core.User` `ON DELETE CASCADE` | |
| `Type` | `nvarchar(64)` | no | PK part | |
| `Inbox` | `bit` | no | `DEFAULT 1` | ignored for non-mutable types |
| `Email` | `bit` | no | `DEFAULT 0` | consumed by the outbox spec |

### Column added to T4's user sibling table (`core.UserState`)

| Column | Type | Null | Notes |
|---|---|---|---|
| `InboxSeenAt` | `datetime2(3)` | yes | set by `inbox/seen` |

### Consumer tables (final shape T9/T7) — the columns this theme depends on

`core.Export`: `Id int PK`; `Resource nvarchar(64)`; `Kind nvarchar(16) CHECK IN ('Display','ForImport')`; `RequestJson nvarchar(max)`; `FileName nvarchar(256)`; `BlobId <T7's id type> NULL`; `RowCount int NULL`; `ExpiresAt datetime2(3)`; `JobId int NULL FK core.Job ON DELETE SET NULL` (unique filtered); `CreatedAt`; `CreatedById FK core.User`; indexes `(CreatedById, CreatedAt DESC)`, `(ExpiresAt)`. `core.Import`: `Id int PK`; `Resource`; `Mode nvarchar(16) CHECK IN ('Insert','Update','Merge')`; `FileBlobId`; `ResultBlobId NULL`; `RowCount NULL`; `ErrorCount NULL`; `JobId int NULL FK core.Job ON DELETE SET NULL` (unique filtered); `CreatedAt`; `CreatedById`. Every `IJobEntity` table carries `IX_<Table>_Job (JobId) WHERE JobId IS NOT NULL`, unique.

### Standalone table types (spec 0001 §5; registered by the platform features)

`JobOutcomeList (Id int PK, Status nvarchar(16), Released bit, RetryAfterSeconds int NULL, ErrorCode nvarchar(64) NULL, ErrorMessage nvarchar(1024) NULL, ErrorDetails nvarchar(max) NULL, StateJson nvarchar(max) NULL)`; `JobProgressList (Id int PK, ProgressPercent tinyint NULL, ProgressMessage nvarchar(256) NULL, StateJson nvarchar(max) NULL)`; `JobLeaseList (Id uniqueidentifier PK, LeaseSeconds int)`; `ScheduleNextList (ScheduleId int PK, NextDueAt datetime2(3) NULL, LastScheduledFor datetime2(3) NULL, Due bit)`; `NotificationRowList (Id int PK, UserId int)`. The existing `IdList` carries job ids for `retry`/`cancel`/`resume` and notification ids for `inbox/read`.

### Seeded rows (reserved band, `HasData`)

Built-in schedules `core.job-retention` (`0 3 * * *`), `core.notification-retention` (`30 3 * * *`), `core.export-retention` (`0 4 * * *`), `core.blob-staging-sweep` (`15 * * * *`), each `RunAsUserId = WellKnownUsers.SystemUserId`, `IsBuiltIn = 1`, `Code = Name = HandlerKey`, with their `core.ScheduleState` rows (`NextDueAt` computed at first tick when null and active); one `core.JobWorkerState` row (`Id = 1`, `LastTickAt = NULL`).

## 5. Answers to the brain dump's open questions

| Brain-dump question (abridged) | Answer | Decision |
|---|---|---|
| "Is the design robust?" | Robust in shape; the columns-on-entity choice and the one-shot lease are replaced; overlap, cancellation, checkpoints, retention, poison, clock, run-as partitioning and the queued-rows restore guard are added. | D1, D4–D7, D10 |
| "Shared coordination state in the catalog DB, or leasing without a central table?" | No central state. Leased rows in each tenant database (`UPDLOCK, READPAST, ROWLOCK` plus a fencing token) are the only coordination; schedules fire once per occurrence through the same mechanism on `core.ScheduleState`. | D4, D7, D9 |
| "How do OTel traces fit? Inherit or own trace?" | Own: a new root `Consumer` span per handler invocation with one link per job to the enqueuing `traceparent` stored on the row. | D12 |
| "CRON expressions stored anywhere, triggering all kinds of tasks?" | Stored in one place, `core.Schedule`; a recurring business entity references a schedule. Any registered key can be scheduled; the tick rides the poll and fires through the job table. | D9 |
| "User-triggered tasks run under the user's credentials?" | Yes: `RunAsUserId = RequestedById`; permissions evaluated at run time; deactivated user ⇒ fail closed. | D11 |
| "Triggered tasks: which credentials? System user? Prevent scheduling reads of data the user cannot see?" | Built-ins run as the seeded system user; user schedules run as their last saver; run-time evaluation grants exactly that user's rights; owner deactivation pauses the schedule and notifies administrators. | D11 |
| "Replay all, last only, or drop after X hours? Safeguard against the year-old restore?" | Per schedule `Coalesce` (default) / `ReplayAll` within `CatchUpWindowMinutes` / `Skip`, plus an overlap policy; a heartbeat gap beyond 6 h coalesces every schedule, holds stale queued rows for an administrator's decision, and alerts. | D9, D10 |
| "Lease expiry with a safety buffer, or renewal?" | Renewal: sliding lease renewed at one fifth with a fencing token; cancellation on loss; lease ≥ 3 renewal intervals. | D5 |
| "Nudge the engine so a new email is processed immediately." | Post-commit in-process nudge through a coalescing channel per tenant; other instances within `MaxPollInterval` (30 s); correctness never depends on the nudge. | D7, D8 |
| "Logs and metrics for mis-configured estimates, batch sizes, latency, backed-up queues." | The instrument table: duration vs lease, batch fill, queue latency, backlog age, lease losses, gap detection, heartbeat. | D13 |
| "Progress as states or a percentage; failures as code + message + dump." | `ProgressPercent`, `ProgressMessage`, `StateJson` flushed with renewals; `ErrorCode`, `ErrorMessage`, `ErrorDetails` on the row. | D5, D6 |
| "Include self-initiated task completion in the inbox? Some must not be unsubscribable; or always provide another way to the artifact?" | Both: completion notifications exist with a non-mutable inbox channel, and the export row with its file is the durable, notification-independent path ("My exports"). | D14, D15 |
| "What happens when you click an unread item? Differs per type." | The row carries `TargetResource`/`TargetId`; the client maps resource to route; clicking marks it read. | D15, D16 |
| "Best entity model and API interface?" (inbox) | `core.Notification` + `core.NotificationPreference` + `InboxSeenAt`; `INotifier` riding the batch; `inbox/summary`, `inbox/seen`, `inbox/read`, `inbox/read-all`; the standard query endpoint under a self-scope criterion. | D15, D16, D19 |
| "Notifying must not add a round trip; it rides the save." | `INotifier.Notify(batch, …)` appends one `INSERT … SELECT` per request with preferences, dedup and inactive-recipient checks as predicates. | D15 |
| "SignalR notifies that counters changed; the client fetches." | `inbox.changed` to per-(tenant, user) groups after commit; the summary endpoint is the fetch. | D16, D17 |
| "What is the shape of inbox tracking?" | One column, `InboxSeenAt`, on the user's non-temporal sibling table. | D16 |
| "Notification settings: what to store?" | Per-type channel bits in `core.NotificationPreference`; channel addresses stay on `User` (T4). | D15 |
| Orchestrator: "handler registry; batch lease acquisition; multi-instance safety; poison handling and dead-lettering" | `[JobHandler]` + `IJobsRegistrar`; `TOP (@n)` claim with `OUTPUT INTO`; token fencing; claim-counted attempts ⇒ `Failed` with admin retry. | D3, D4, D6 |
| Orchestrator: "nudging within and across instances" | Within: immediate. Across: none by design; bounded by `MaxPollInterval`. | D7 |
| Orchestrator: "first consumers" | `core.Export`/`core.Import` rows with jobs; `core.blob-staging-sweep`; retention; outbox rows carry `JobId`. | D14 |
| Orchestrator: "per-user groups, thin events, tenant isolation, Azure SignalR in SaaS" | Groups `t{tenant}` and `t{tenant}.u{user}` set server-side; ids-only payloads; Azure SignalR when configured, Redis for multi-instance on-prem. | D17 |
| Orchestrator: "whether the inbox splits off as 0020" | Yes. | D18 |

## 6. Seams

**Seam 1 — the batch abstraction (T2 owns).** This theme is the heaviest raw-SQL consumer and needs: `AddSql` with a declared write set and a per-statement `MayRetry`; TVP binding of standalone types by metadata; synchronous `ReserveIds<T>(count)` so job and notification ids are known before the batch runs; `OnCommitted` callbacks (nudge, hub events) that run outside the transaction and are logged, not thrown, on failure; `SelectByJoin<TEntity>` to load entity rows joined to a table variable produced earlier in the same command; statement-scoped table-variable names. Transactions are READ COMMITTED (never SNAPSHOT) because claim statements carry `READPAST`; the executor refuses `MayRetry` retries inside a transaction and retries whole non-transactional commands (the claim, the renewal) after jitter on 1205/1222 and transient errors.

**Seam 8 — background-task columns and lease statements (this theme owns semantics, T2 emits).** Resolved: there are no per-entity lease columns. T2's data layer executes four statement shapes owned by this theme — claim (D4), renew (D5), complete (D6), gap-hold (D10) — and emits the `JobId` join load for `IJobEntity` types from EF metadata. The only capability T2 adds to the entity contract is `IJobEntity` (one nullable FK column, `ON DELETE SET NULL`, unique filtered index).

**Seam 9 — request context in background scopes (T1 owns).** `ITenantScopeFactory.CreateScope(tenantId, userId)` must produce a scope in which the tenant holder, `ISandboxContext`, the user context, culture, calendar and time zone are set from the tenant's settings and the run-as user's preferences (no `AsyncLocal`); `ITenantRegistry.ListTenants()` for polling. The hub's connect reuses T1's tenant resolution and the connect step.

**Seam 11 — permission evaluation (T4 owns).** Job scopes call the same evaluator as requests; T4 exposes the connect step as `IConnectStep.Run(userId, stampActivity)` so a job's first round trip reads tags and `IsActive` without stamping `LastActive`; T4 seeds the system user (`WellKnownUsers.SystemUserId`) and provides `IAdministratorDirectory`; T4's user sibling table takes `InboxSeenAt`; T4's registry accepts the bespoke self-scope criteria this theme registers for jobs, exports and notifications.

**Seam 15 — notification enqueue riding the save batch (this theme owns, T5 consumes).** `INotifier.Notify(batch, requests)` (§3.2); T5's pipeline exposes its batch to services in the transactional side-effects step, and the hub event fires from the batch's post-commit hook. T5 exposes `IJobQueue.Enqueue` at the same step for services that hand work off.

**Seam 3 — one capability, declared once (T5 owns).** The activatable capability on `Schedule` must trigger a post-persist hook so the schedule service recomputes `NextDueAt` and clears `PausedReason`; this theme needs T5's pipeline to offer a per-entity `AfterPersist(batch, saved)` extension point that appends statements to the same batch. The schedule service also enforces D11's `RunAsUserId` rule and the built-in editing restrictions in preprocessing and validation.

**Seam 6 — feature composition (T1 owns).** The jobs feature contributes: handler and schedule registrars, notification type and event registrars, the worker hosted service, the hub mapping, securables and endpoints, standalone table types, and seeded rows; the feature contract must allow contributing seed data and standalone table types to the model.

**Seam 12 — blob staging tokens (T7 owns).** Export writes one blob and records its id; import reads the staged sheet by token in the job scope; the import upload staging TTL is 24 h.

**Seam 13 — wire shapes (T6 owns).** Background hand-off responses (`{ exportId, jobId }`), the job/schedule/notification rows in the standard details and query shapes, the bespoke `InboxSummary`, the hub route, and the MCP exposure of `get_job`/`list_notifications`.

**Seam 14 — telemetry (T2 owns instruments; this theme names its own).** D13's names are constants in `JobsTelemetry` and `NotificationsTelemetry`; the tenant is never a tag.

**Seam 5 — version tags (T3 owns).** Nothing in this theme is tag-cached; every statement declares the tables it writes so a future cache bumps automatically. `cache.changed { tag }` is the hub event T3 may publish for cacheable-entity invalidation.

**Seam 10 — platform exceptions (T5/T6).** The job, schedule and inbox endpoints throw only the standard platform exceptions (not found, forbidden, validation); nothing new.

**Seam 17 — vocabulary.** Singular table names here; pluralize mechanically if the synthesizer chooses plural. Four audit columns on `Schedule`; none on `Job` and `Notification` (system-written; `CreatedAt` only). `int` ids throughout (a tenant enqueuing 10,000 jobs a day exhausts `int` in 588 years).

## 7. Departures from ARCHITECTURE.md

1. **A second scheduler library.** The package list pins Quartz for the identity server only; this theme adds Cronos 0.13.0 and its own leasing scheduler rather than extending Quartz to tenant work — Quartz's cron dialect (mandatory seconds, `?` rule) is not the Unix dialect users know, its clustered job store is a second persistence model, and it cannot ride the per-tenant batch.
2. **Redis for multi-instance on-prem SignalR.** The local-stack text says "Redis if any"; this theme makes Redis a requirement only for on-prem deployments with two or more instances that want real-time events. Single-instance on-prem and SaaS need none.
3. **Hosting requirements added under Hosting on Azure:** `Always On` (the poller stops after 20 idle minutes otherwise) and `WEBSITES_CONTAINER_STOP_TIME_LIMIT = 30` in the distribution's infra template.
4. **Two new runtime packages** (`Tellma.Core.Jobs`, `Tellma.Core.Notifications`) in the Core family, and new CPM pins (`Cronos`; `Microsoft.Azure.SignalR`, `Microsoft.AspNetCore.SignalR.StackExchangeRedis` in the reference distribution).
5. **Tenant isolation on the hub** is stated as a rule (server-assigned groups) — an addition to spec 0003 §7.4, not a contradiction.

No departure from the fixed facts: `MERGE` is unused; retry is the executor's; `READPAST` is always paired with `UPDLOCK`; JSON stays `nvarchar(max)`; no logic in the database beyond predicates in emitted statements; every reference is an FK; seeds via `HasData` in the reserved band.

## 8. Verification

Relied on from `research/background-inbox.md` (verified 2026-09-01): the `UPDATE TOP … WITH (READPAST, UPDLOCK, ROWLOCK) OUTPUT` idiom and Hangfire's production fetch (§1.1); bare `READPAST` is a silent no-op under RCSI while `UPDLOCK + READPAST` behaves identically with RCSI on and off, `READPAST` under SNAPSHOT raises error 650, ordered claims work through a `TOP … ORDER BY` CTE (§1.2, local reproduction); sliding lease renewed at one fifth with a fencing token, poison after five attempts, immediate redelivery is a footgun, `DequeueCount` counts receipts (§1.3); filtered-index requirements and the `sqlcmd` SET-option trap (§1.4); `ROWLOCK` does not prevent escalation at 5,000 locks, `LOCK_ESCALATION = DISABLE`, `UPDATE TOP` is unordered, `OUTPUT` without `INTO` is illegal on triggered tables, deadlock guidance (§1.5); `HostOptions.ShutdownTimeout` 30 s, `BackgroundServiceExceptionBehavior = StopHost`, `IHostedLifecycleService.StoppingAsync` ordering (§2.1); `PeriodicTimer(TimeSpan, TimeProvider)` and `BoundedChannelFullMode.DropWrite` as a coalescing signal (§2.2); Always On, `WEBSITE_INSTANCE_ID` diagnostic only, Linux drain default 5 s max 120, slot swaps abandon long operations (§2.3); Cronos 0.13.0 API, DST semantics, `GetOccurrences`, `GetNextOccurrence` (§3.1); NCrontab has no time-zone support; Quartz's dialect differs (§3.2–3.3); links at creation, `Consumer` kind, messaging attributes, baggage security note, `Activity.AddLink` (§4.1); Azure Monitor serializes links to `_MS.links` (§4.2); Azure SignalR 1.33.1, Redis backplane 10.0.11, `IUserIdProvider`, groups in-memory and not a security feature, `IHubContext` from hosted services, `users/{user}/:closeConnections`, `ClaimsProvider`, `CloseOnAuthenticationExpiration` since 1.19.0, sticky sessions and channel prefix (§5); the misfire and overlap policies of Quartz, Kubernetes, Airflow, Hangfire and Temporal (§6).

Relied on from the specs, code and fixed facts: spec 0003 §7.4 (cookie at connect, hub-only Azure token, close on session end, thin events); spec 0007 §1.6 (`DeploymentIdentity.DeploymentId`), §3.1 (`ISandboxContext` resolvable in background scopes, no default registration), §13 (`IEmailOutbox`/`EmailEnqueueRequest` reserved shape, per-message outcomes, contentless post-commit signal); spec 0001 §5 (standalone table types, the four bulk lists); `IEmailSender.SendAsync` batch contract; `MERGE` banned; SqlClient retry skipped inside transactions; concatenated command text with `NextResult()`; RCSI default on Azure only; app-assigned ids; telemetry naming; the repo's `src/core/` package set (`Tellma.Core`, `Tellma.Core.Email`, `Tellma.Core.Webhooks` with short meter names) and the absence of Cronos/SignalR pins in `Directory.Packages.props`.

Still unverified (to be proven by tests in the specs' definitions of done): that the optimizer matches `IX_Job_Available` for the claim predicate that repeats the `IN` list with extra conjuncts (D4 records the fallback); that `OUTPUT … INTO @table` followed by a join in the same command performs as one seek-and-join under `READPAST`; that `UPDATE` through a CTE with `TOP … ORDER BY` and an `OUTPUT INTO` clause is accepted on a filtered-index seek plan (T4 of the research reproduction covered the CTE without `OUTPUT INTO`); Cronos's maximum year (`GetNextOccurrence` returning null is handled as `exhausted`); whether Application Insights renders `_MS.links` (KQL joins assumed); the default numbers (poll 1 s/30 s, parallel batches 8, drain 4 s, gap 6 h, retention 30/180 days, catch-up window 24 h) are judgment calls tuned with D13's instruments.

## 9. Review flags

1. **Queue table versus columns on the entity (D1).** The brain dump wanted lease columns on any task-bearing entity; the design uses one `core.Job` table with a `JobId` FK. Alternative: the brain dump's shape with a sibling lease table per entity — rejected for status collision, temporal churn and one-code-path reasons.
2. **`Job` as the type family name (D2).** Alternative `Task`/`BackgroundTask` — collides with `System.Threading.Tasks.Task`.
3. **Two runtime packages (D2).** Alternative: both runtimes inside `Tellma.Core`; chosen split keeps SignalR out of a future dedicated worker host.
4. **Filtered index shape (D4).** `Status IN ('Pending','Running')` filter; alternative `Status = 'Pending'` only with a reaper for expired leases if the plan test fails.
5. **Attempts count claims, releases un-count (D6).** Alternative: count only reported failures — misses crash loops; alternative: count everything — dead-letters long jobs across deploys.
6. **Shutdown leaves still-running handlers leased (D7).** Alternative: release every in-flight lease at shutdown for faster pickup at the cost of possible concurrent double execution.
7. **Local-only nudge (D7).** Alternative: a cross-instance nudge through Redis or Azure SignalR for sub-second pickup when the local instance is saturated.
8. **Default numbers (D7, D9, D10, D14):** poll 1 s/30 s, 8 parallel batches, drain 4 s, gap 6 h (vs 72 h for on-prem weekends), catch-up window 24 h (vs the schedule's own interval), `Skip`'s "just became due" window of 2 × `MaxPollInterval`, retention 30/180 days.
9. **Built-in schedules seeded by `HasData` (D9).** Platform must never change a shipped built-in's seed values; alternative: an idempotent "ensure built-ins" insert at first tick, which contradicts the seed rule but never scaffolds an `UpdateData` migration.
10. **Gap quarantine holds stale queued rows (D10).** Alternative: alert only and keep claiming; or a per-handler `HoldAfterGap = false` opt-out for idempotent maintenance keys.
11. **User schedules run as the last saver (D11).** Alternative: an explicit owner field with an `impersonate` action for administrators.
12. **Read-time rendering of notifications (D15).** Alternative: a `Title` column rendered in the tenant's primary language at creation, enabling a sortable message column on the notifications page.
13. **Dedup is best effort (D15).** Alternative: a unique filtered index that makes a concurrent duplicate fail the business save.
14. **No `Push` channel column yet (D15).** Alternative: reserve the column now.
15. **Per-(tenant, user) groups on one hub (D17).** Alternative: a hub per tenant.
16. **`batch_fill` instrument (D13)** may not earn its keep.

## 10. Conflicts

1. **T2 (seam 1):** must provide synchronous `ReserveIds`, `OnCommitted`, `SelectByJoin`, statement-scoped table-variable names, READ COMMITTED transactions, and the "no `MayRetry` inside a transaction; retry whole non-transactional commands" rule; must add `IJobEntity` to the entity contract and emit a unique filtered index on `JobId`.
2. **T4:** the user sibling table's name (referred to here as `core.UserState`) and its `InboxSeenAt` column; the seeded system user in the reserved band with `WellKnownUsers.SystemUserId`; `IConnectStep.Run(userId, stampActivity: false)`; `IAdministratorDirectory`; bespoke self-scope criteria for `core.jobs`, `core.exports`, `core.notifications`; the `core.jobs` / `core.schedules` securables and their actions (`retry`, `cancel`, `resume`, `activate`); notification channel addresses stay on `User` while per-type channel bits live in `core.NotificationPreference`.
3. **T1:** `ITenantScopeFactory.CreateScope(tenantId, userId)` with `ISandboxContext`, culture, calendar and time zone set; `ITenantRegistry.ListTenants()`; the session-end hooks calling `IHubConnectionCloser`; the feature contract accepting seed data, standalone table types, hosted services and hub mappings; the infra template's `Always On` and container stop limit.
4. **T3:** the tenant time zone used when `Schedule.TimeZoneId` is null; the ICU message catalogue keyed by notification type; the `cache.changed` hub event as the cacheable-entity invalidation signal.
5. **T5:** the transactional side-effects step exposing the batch to `INotifier.Notify` and `IJobQueue.Enqueue`; an `AfterPersist(batch, saved)` hook for the schedule service; the activatable capability on `Schedule`.
6. **T6:** the hub route `/{tenantId}/hub`; the `{ exportId, jobId }` hand-off shape; the `InboxSummary` bespoke endpoint; MCP tools `get_job` and `list_notifications`; the standard exception mapping for the new endpoints.
7. **T7:** `core.blob-staging-sweep` handler shipped by the blob feature; a 24-hour staging TTL for import uploads; post-commit blob deletion in `core.export-retention`.
8. **T9:** `core.Export` and `core.Import` entities with `IJobEntity`, the synchronous-versus-background threshold, and per-chunk versus single-transaction import commits.
9. **The outbox spec:** outbox rows implement `IJobEntity`, run as the system user with `BatchSize = 100`, and `EmailEnqueueRequest.SendAfter` maps to `JobRequest.DueAt`; the notification email channel is a second statement in `INotifier.Notify`.
10. **Seam 17:** singular table names pending the synthesizer's choice.
