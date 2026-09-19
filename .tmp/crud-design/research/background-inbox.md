# Research: Background tasks, scheduler, and the inbox (T10 → spec 0019)

Verification date for everything below: **2026-09-01** unless a finding says otherwise. "Verified" means read
in a primary source (vendor docs, source code, release page) or reproduced locally; "inference" is my
reading of verified facts. Repo facts come from `Directory.Packages.props` and `global.json` at HEAD.

Repo baseline relevant to this theme: .NET SDK `10.0.300`; `Microsoft.Extensions.Hosting` 10.0.11;
`OpenTelemetry.*` 1.16.0; `Azure.Monitor.OpenTelemetry.AspNetCore` 1.5.0; `Quartz.Extensions.Hosting` 3.18.2
(identity server only); `Microsoft.Data.SqlClient` 6.1.1; no Cronos/NCrontab/SignalR packages pinned yet.

---

## 1. SQL Server work-queue patterns

### 1.1 The claim statement (verified: docs + local reproduction)

**Verified facts (Microsoft Learn, table hints page, `ms.date` 2026-03-12):**

- `READPAST` "skips past the rows instead of blocking"; "row-level locks are skipped, but page-level locks
  aren't skipped"; it "is primarily used to reduce locking contention when implementing a work queue that uses
  a SQL Server table".
- `READPAST` "can be specified for any table referenced in an UPDATE or DELETE statement"; in an UPDATE it "is
  applied only when reading data to identify which records to update". Update/delete with `READPAST` "might
  block when reading foreign keys or indexed views, or when modifying secondary indexes".
- `READPAST` "can only be specified in transactions operating at the READ COMMITTED or REPEATABLE READ
  isolation levels" (error 650 otherwise).
- Docs also say `READPAST` "can't be specified when the READ_COMMITTED_SNAPSHOT database option is set to ON
  and ... the transaction isolation level of the session is READ COMMITTED", recommending `READCOMMITTEDLOCK`.
  **My local reproduction contradicts the "can't be specified" wording — see 1.2.**
- `UPDLOCK`: "update locks are to be taken and held until the transaction completes"; when specified, the
  `READCOMMITTED`/`READCOMMITTEDLOCK` hints are ignored.
- `ROWLOCK`: "row locks are taken when page or table locks are ordinarily taken"; under SNAPSHOT it takes
  no locks unless combined with `UPDLOCK`/`HOLDLOCK`; cannot be used with clustered columnstore.
- `FORCESEEK` (the hint Hangfire adds): "can't be specified for a table that is the target of an INSERT,
  UPDATE, or DELETE statement" **when given with index parameters**; the parameterless `FORCESEEK` is what
  Hangfire uses on its UPDATE target.
- Row-lock hints "might place locks on index keys rather than the actual data rows" when a covering index is
  used — relevant when the claim reads through a filtered index.
- `UPDATE TOP (n)`: "The rows referenced in the TOP expression used with INSERT, UPDATE, or DELETE aren't
  arranged in any order" (UPDATE page). Ordered claiming needs a CTE/derived table with `ORDER BY`.
- `OUTPUT` (OUTPUT clause page): "SQL Server doesn't guarantee the order in which rows are processed and
  returned by DML statements using the OUTPUT clause"; if `OUTPUT` is used **without `INTO`**, "the target of
  the DML operation can't have any enabled trigger defined on it for the given DML action"; an `OUTPUT` result
  "will return rows to the client even if the statement encounters errors and is rolled back". The page
  itself shows the queue idiom `DELETE TOP(1) ... WITH (READPAST) OUTPUT DELETED.*` and says "Use the READPAST
  table hint in UPDATE and DELETE statements if your scenario allows for multiple applications to perform a
  destructive read from one table."
- Hangfire.SqlServer (source, `SqlServerJobQueue.cs`, main): the production fetch is
  `update top (1) JQ set FetchedAt = GETUTCDATE() output INSERTED.Id, INSERTED.JobId, INSERTED.Queue,
  INSERTED.FetchedAt from [schema].JobQueue JQ with (forceseek, readpast, updlock, rowlock) where Queue in
  @queues and (FetchedAt is null or FetchedAt < DATEADD(second, @timeoutSs, GETUTCDATE()))`; the legacy mode
  is the same predicate with `delete top (1) ... output DELETED.*` inside a transaction. Poll delay defaults to
  200 ms in the fetch loop.

Sources:
- https://learn.microsoft.com/en-us/sql/t-sql/queries/hints-transact-sql-table
- https://learn.microsoft.com/en-us/sql/t-sql/queries/update-transact-sql
- https://learn.microsoft.com/en-us/sql/t-sql/queries/output-clause-transact-sql
- https://github.com/HangfireIO/Hangfire/blob/main/src/Hangfire.SqlServer/SqlServerJobQueue.cs
- https://rusanu.com/2010/03/26/using-tables-as-queues/ (secondary; the canonical write-up of
  `DELETE ... WITH (ROWLOCK, READPAST) OUTPUT`, clustered index in dequeue order, a `DueTime`-clustered
  "pending queue", and the warning that dropping `READPAST` serialises all consumers)

**Implication:** The lease statement T2 must emit is
`UPDATE TOP (@n) t SET ... OUTPUT inserted.* FROM <table> t WITH (READPAST, UPDLOCK, ROWLOCK) WHERE <pending predicate>`
(unordered), or a CTE with `TOP (@n) ... ORDER BY` for priority/FIFO; task tables must not carry enabled
triggers (or the emitter must use `OUTPUT INTO`), and the OUTPUT rows must be discarded if the batch fails.

### 1.2 Local reproduction: READPAST under RCSI (verified 2026-09-01 on LocalDB 15.0.4382.1 Express)

Setup: database with `READ_COMMITTED_SNAPSHOT ON` and `ALLOW_SNAPSHOT_ISOLATION ON`, table
`Tasks(Id PK clustered, Status tinyint, LeasedUntil datetime2(3))`, five rows; session A holds an X lock on
`Id = 1` inside an open transaction; session B (`SET LOCK_TIMEOUT 2000`, READ COMMITTED) ran:

| Test | Statement | Result |
|---|---|---|
| T1 | `SELECT ... WITH (READPAST)` alone | **No error; returned all 5 rows including the locked one** — under RCSI the read is versioned, there is no lock to skip, so READPAST is silently ineffective |
| T2 | `SELECT ... WITH (UPDLOCK, READPAST, ROWLOCK)` | Returned 2,3,4,5 (locked row skipped) |
| T3 | `UPDATE TOP (2) ... WITH (READPAST, UPDLOCK, ROWLOCK) OUTPUT inserted.Id WHERE Status = 0` | Claimed 2 and 3; no blocking |
| T4 | CTE `SELECT TOP (1) ... WITH (READPAST, UPDLOCK, ROWLOCK) WHERE Status = 0 ORDER BY Id` then `UPDATE cte` | Claimed 4 (next in order); ordered claim works |
| T5 | `SET TRANSACTION ISOLATION LEVEL SNAPSHOT` + `(READPAST, UPDLOCK)` | **Error 650**: "You can only specify the READPAST lock in the READ COMMITTED or REPEATABLE READ isolation levels" |
| T6 | `SELECT ... WITH (READPAST, READCOMMITTEDLOCK)` | Returned 2,3,4,5 |
| T7 | plain `UPDATE ... WHERE Id = 1` | Error 1222 lock timeout after 2 s (control) |

Also observed: `CREATE INDEX ... WHERE Status = 0` failed under `sqlcmd` with **Msg 1934**
("CREATE INDEX failed because the following SET options have incorrect settings: 'QUOTED_IDENTIFIER'") — `sqlcmd`
defaults `QUOTED_IDENTIFIER OFF`; the CREATE INDEX page's SET-options table requires `QUOTED_IDENTIFIER ON`
(plus ANSI_NULLS, ANSI_PADDING, ANSI_WARNINGS, ARITHABORT, CONCAT_NULL_YIELDS_NULL ON, NUMERIC_ROUNDABORT OFF)
for creating, modifying through, and using a filtered index. SqlClient sessions default these ON, so the
platform is fine; migration and ops scripts run through `sqlcmd` are not.

RCSI default: the locking guide states READ_COMMITTED_SNAPSHOT ON "is the default setting in Azure SQL
Database"; on-prem SQL Server defaults it OFF (verified).

Sources:
- local scripts `scratchpad/qsetup.sql`, `qblocker.sql`, `qreader.sql` (session scratchpad; not kept in repo)
- https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide
- https://learn.microsoft.com/en-us/sql/t-sql/statements/create-index-transact-sql (Required SET options)

**Implication:** Every lease/claim statement must combine `READPAST` with `UPDLOCK` (and `ROWLOCK`); a bare
`READPAST` read is a silent no-op on Azure SQL (RCSI on) and the same code would behave differently on-prem
(RCSI off). Never run the claim under SNAPSHOT isolation. The batch executor's transaction isolation level
must be READ COMMITTED for lease statements.

### 1.3 Lease / visibility-timeout semantics in established systems (verified)

- **Azure Storage Queues, Get Messages:** `visibilitytimeout` default 30 s, min 1 s, max 7 days; up to 32
  messages per get; `DequeueCount` starts at 1 and increments on each re-receipt; the message "isn't
  automatically deleted" and the consumer must delete it "before the time that's specified by
  `TimeNextVisible`" using the `PopReceipt`, which "is unique for each dequeuing"; "Because of clock skew, a
  message ... can reappear before that specified time-out has elapsed"; a stale pop receipt gets 404 — this is
  the fencing-token pattern.
- **Azure Functions queue trigger:** retries "up to five times ... including the first try", then moves the
  message to `<queue>-poison`; on failure the host re-applies `visibilityTimeout` (default **0** — immediate
  redelivery); if the host crashes "the message is left with the default 10 minute timeout set by the storage
  service"; polling backs off from ~200 ms to `maxPollingInterval` (default 1 min, 2 s locally); batch 16,
  new batch when 8 remain (max 24 in flight per VM); "The queue trigger automatically prevents a function from
  processing a queue message multiple times simultaneously."
- **Hangfire.SqlServer (source):** `SlidingInvisibilityTimeout` default 5 min; a keep-alive runs every
  `SlidingInvisibilityTimeout / 5` (= 1 min) with
  `update JQ set FetchedAt = getutcdate() output INSERTED.FetchedAt from JobQueue JQ with (forceseek, rowlock)
  where Queue = @queue and Id = @id and FetchedAt = @fetchedAt` — the old `FetchedAt` value is the fencing
  token; if the update affects no row the worker logs "was fetched by another worker, will not execute keep
  alive" and stops renewing; completion is `delete ... where ... and FetchedAt = @fetchedAt`; requeue on
  failure/dispose is `update JQ set FetchedAt = null ... and FetchedAt = @fetchedAt`. `QueuePollInterval`
  default `TimeSpan.Zero` (long-polling with the 200 ms loop), `UseRecommendedIsolationLevel = true` (READ
  COMMITTED; the historical default SERIALIZABLE caused deadlocks), `InvisibilityTimeout` (fixed lease, 30 min)
  is `[Obsolete]`.

Sources:
- https://learn.microsoft.com/en-us/rest/api/storageservices/get-messages
- https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-queue-trigger
- https://github.com/HangfireIO/Hangfire/blob/main/src/Hangfire.SqlServer/SqlServerTimeoutJob.cs
- https://github.com/HangfireIO/Hangfire/blob/main/src/Hangfire.SqlServer/SqlServerStorageOptions.cs

**Implication:** Industry practice is a *sliding* lease renewed at 1/5 of its length by a statement that
carries the previous lease stamp as a fencing token (renewal fails → the handler is cancelled), not a
one-shot lease sized with a "safety buffer"; a `DequeueCount`-style attempt counter with a fixed poison
threshold (5) and a dead-letter state is the norm; an immediate-redelivery default on failure is a footgun
(Azure Functions' `visibilityTimeout = 0`) — use backoff.

### 1.4 Filtered indexes for pending rows (verified)

- The filtered-index page lists exactly the queue case: "When rows in a table are marked as processed by a
  recurring workflow or queue process ... A filtered index on rows that aren't yet processed would benefit the
  recurring query." Predicates support only "simple comparison operators" (no `LIKE`), one table, no
  computed columns in the filter, and "the WHERE clause of the query should be a subset of the WHERE clause
  of the filtered index" for the optimizer to use it. A column in the filter need not be in the key/includes
  if the query predicate is equivalent to the filter and the column is not returned; the clustered key is
  included automatically. Filters cannot be put on PK/UNIQUE constraints (but can on UNIQUE indexes).
- Required SET options (see 1.2). Supported on Azure SQL Database.

Sources:
- https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-filtered-indexes
- https://learn.microsoft.com/en-us/sql/t-sql/statements/create-index-transact-sql

**Implication (partly inference):** Index `(DueAt, Id) INCLUDE (...) WHERE Status = <pending literal>`; the
claim statement must repeat the literal `Status = <pending>` (not a parameter — the optimizer must prove the
query predicate is a subset of the filter at compile time; a parameterised status cannot be proven and falls
back to the base index) and filter `DueAt <= @now` on the leading key so `TOP (n)` is a range seek.

### 1.5 Pitfalls: lock escalation, deadlocks, multiple pollers (verified)

- **Lock escalation** (locking guide): triggered at 5,000 locks on one table/index/partition per statement,
  or at instance lock-memory thresholds (24 % of engine memory by default); escalation is "directly to table
  locks" (never page); retried every 1,250 new locks if blocked. "**Using a lock hint such as ROWLOCK only
  alters the initial lock acquisition. Lock hints don't prevent lock escalation.**" Controls:
  `ALTER TABLE ... SET (LOCK_ESCALATION = AUTO | TABLE | DISABLE)`, trace flags 1211/1224 (instance-wide, not
  recommended), small batches ("DELETE TOP (500)" loop), and "Optimized locking" (Azure SQL; "Lock escalation
  is far less likely"; avoid table hints under it).
- **Deadlock monitor** (deadlocks guide): search interval 5 s, dropping "to as low as 100 milliseconds" when
  deadlocks occur; victim = "least expensive to roll back" unless `SET DEADLOCK_PRIORITY` differs; error 1205
  terminates the batch and rolls back the transaction; "applications should have an error handler that can
  handle error 1205"; `system_health` captures `xml_deadlock_report` by default; guidance: "Access objects in
  the same order."
- **Multiple pollers:** the docs' own queue guidance is READPAST on UPDATE/DELETE so competing readers skip
  each other's rows; Hangfire adds `FORCESEEK` so the claim never scans (a scan under UPDLOCK walks and
  U-locks the whole index → contention/escalation), holds no explicit transaction around the claim
  (autocommit), and keeps the claim's `TOP` small. Remus Rusanu: without READPAST "all transactions must
  serialize access to the queue"; a clustered index in dequeue order prevents hot-spot scans.
- `sp_getapplock` (verified page, applies to Azure SQL DB): `@LockOwner = 'Session'` locks are "released when
  the session is logged out" (or server shutdown); `@LockTimeout = 0` returns −1 instead of waiting; return
  −3 = deadlock victim; locks are per-database; only 32 chars of the resource name survive un-hashed.

Sources:
- https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide
- https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-deadlocks-guide
- https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql

**Implication:** Keep claim batches ≤ a few hundred rows and autocommit the claim; treat 1205/1222 on the
claim as "retry after jitter" (the `MayRetry` flag of the batch executor); poll with a seek on the filtered
index and consider `FORCESEEK`; a per-database `sp_getapplock` session lock is a viable "single scheduler tick
per tenant DB" primitive that needs no central table (lock dies with the connection — the holder must keep
that connection open, which conflicts with pooling unless the lock is held per tick inside a transaction).

---

## 2. .NET 10 hosting for background work

### 2.1 BackgroundService / IHostedLifecycleService / HostOptions (verified)

- `HostOptions` source (dotnet/runtime main): `ShutdownTimeout = TimeSpan.FromSeconds(30)` (config key
  `shutdownTimeoutSeconds`), `StartupTimeout = Timeout.InfiniteTimeSpan` (`startupTimeoutSeconds`),
  `ServicesStartConcurrently = false`, `ServicesStopConcurrently = false`,
  `BackgroundServiceExceptionBehavior = StopHost`. The 5 s → 30 s change is dotnet/runtime issue #63709 /
  PR #63712, milestone 7.0.0 ("in line with Kubernetes").
- Hosted-services doc (aspnetcore-10.0): `ExecuteAsync` "is called on the thread pool"; "No further services
  are started until ExecuteAsync becomes asynchronous, such as by calling await"; the host "blocks in
  StopAsync waiting for ExecuteAsync to complete"; the token "is triggered when IHostedService.StopAsync is
  called ... Otherwise, the service ungracefully shuts down at the shutdown timeout"; `StopAsync` "might not
  be called" on crash. Scoped services are consumed through `IServiceScopeFactory`.
- Generic-host doc: lifecycle order `StartingAsync → StartAsync → StartedAsync → ApplicationStarted`; stop:
  `ApplicationStopping → StoppingAsync → StopAsync → StoppedAsync → ApplicationStopped`. `ConsoleLifetime`
  handles SIGINT/SIGQUIT/SIGTERM (`docker stop`) gracefully since .NET 6; `Environment.Exit` is *not*
  graceful. Web scenario: after `ApplicationStopping`, Kestrel stops accepting, sends GOAWAY (HTTP/2/3) and
  "The active requests have until the shutdown timeout to complete"; "IIS behaves differently, by rejecting
  new requests with a 503 status code." Load-balancer drain steps are documented.
- ASP.NET Core 10 release notes: nothing new for hosted services; the SignalR section is empty; the one
  relevant change is `IApiEndpointMetadata` — SignalR endpoints now get 401/403 instead of cookie-login
  redirects.

Sources:
- https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Hosting/src/HostOptions.cs
- https://github.com/dotnet/runtime/issues/63709
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host
- https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0

**Implication:** The worker's drain budget is the host's 30 s `ShutdownTimeout` shared with Kestrel's request
drain; a handler that cannot checkpoint within that must release (un-lease) rather than finish. Because
`BackgroundServiceExceptionBehavior` defaults to `StopHost`, the worker loop must never let an exception
escape `ExecuteAsync`. Use `IHostedLifecycleService.StoppingAsync` (runs before `StopAsync`, before Kestrel's
drain completes) to stop claiming new work early.

### 2.2 PeriodicTimer and Channels for nudges (verified)

- `PeriodicTimer` (net-10.0 API page): constructors `(TimeSpan)` and `(TimeSpan, TimeProvider)`; `Period` is
  get/set; "intended to be used only by a single consumer at a time: only one call to
  WaitForNextTickAsync may be in flight"; `Dispose` makes a pending wait return `false`.
- `BoundedChannelFullMode` (net-10.0): `Wait`, `DropNewest`, `DropOldest`, `DropWrite` ("Drops the item being
  written"). A `Channel.CreateBounded<T>(new BoundedChannelOptions(1) { FullMode = DropWrite })` is therefore a
  coalescing "wake-up" signal: any number of nudges collapse into at most one pending item.

Sources:
- https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer
- https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.boundedchannelfullmode

**Implication:** In-process nudge = bounded(1)/DropWrite channel per (tenant, handler) awaited with
`Task.WhenAny(timerTick, channelRead)`; the repo already pins `Microsoft.Extensions.TimeProvider.Testing`, so
the timer should take the `TimeProvider` overload for tests.

### 2.3 Azure App Service: Always On, idle unload, multi-instance (verified)

- "When Always On is turned off (default), the app is unloaded after 20 minutes without any incoming
  requests." When on, "the front-end load balancer sends a GET request to the application root every five
  minutes." "Always On is required for continuous WebJobs or for WebJobs that a cron expression triggers."
  Always On is *not swapped* between slots (slot-specific).
- `WEBSITE_INSTANCE_ID`: "Read-only. Unique ID of the current VM instance, when the app is scaled out to
  multiple instances." `WEBSITE_DISABLE_OVERLAPPED_RECYCLING`: "This configuration does not guarantee that
  only one VM instance exists or runs at a time during scale up/down or platform-initiated maintenance --
  multiple VM instances may still exist concurrently, even when this setting is enabled."
- Slot swap: "Your former production instances are swapped into staging ... Those instances are recycled in
  the last step of the swap process. If you have any long-running operations in your application, they're
  abandoned when the workers recycle." During the swap both slots' instances are live (source warmed up while
  target stays online).
- Shutdown grace: Linux `WEBSITES_CONTAINER_STOP_TIME_LIMIT` "Default is 5. You can increase to a maximum of
  120" (seconds). Windows in-process: ANCM `shutdownTimeLimit` default **10 s** (max 600) when
  `app_offline.htm` is detected; IIS app-pool `processModel.shutdownTimeLimit` default `00:01:30`.
  `WEBSITE_TIME_ZONE` defaults to UTC. Health check: `WEBSITE_HEALTHCHECK_MAXPINGFAILURES` (2–10) removes an
  instance from rotation.

Sources:
- https://learn.microsoft.com/en-us/azure/app-service/configure-common
- https://learn.microsoft.com/en-us/azure/app-service/reference-app-settings
- https://learn.microsoft.com/en-us/azure/app-service/deploy-staging-slots
- https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/web-config?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/iis/configuration/system.applicationhost/applicationpools/add/processmodel/

**Implication:** Always On must be part of the distribution's infra template or the poller stops after 20
idle minutes; the design must assume N ≥ 2 instances at any moment (scale-out, swaps, maintenance) with no
instance identity stable across restarts — use `WEBSITE_INSTANCE_ID` + process start GUID only as a
diagnostic lease-owner tag, never for coordination; the effective drain budget on Linux App Service is 5 s
unless the setting is raised to ≤ 120 s, which is shorter than the host's 30 s default — so lease renewal and
"release on cancel" matter more than graceful completion.

---

## 3. CRON parsing libraries

### 3.1 Cronos (verified)

- NuGet: latest **0.13.0, released 2026-04-29** (0.12.0 2026-04-08 added `H` jitter; 0.11.1 2025-08-12);
  MIT; targets net6.0, netstandard1.0, netstandard2.0 (computed-compatible with net10.0); dependency-free.
- README: `CronExpression.Parse(expr)` (5 fields) or `Parse(expr, CronFormat.IncludeSeconds)` (6 fields);
  `GetNextOccurrence(DateTime utc[, TimeZoneInfo][, inclusive])`, `GetNextOccurrence(DateTimeOffset, ...)`,
  `GetOccurrences(from, to, ...)`, `GetPreviousOccurrence` (0.13.0). "You cannot use local DateTime objects
  (such as DateTime.Now), because this may lead to ambiguity during DST transitions, and an exception will be
  thrown." Special characters `L`, `W`, `#`, `?`, reversed ranges (`23-01`, `DEC-FEB`); **no year field**;
  day-of-month and day-of-week are ANDed (unlike Vixie cron's OR).
- DST semantics (README "Daylight Saving Time"): spring-forward — a fixed time inside the missing hour
  ("02:30 AM") "points to an invalid time"; "Cronos adjusts the next occurrence to the next valid time";
  fall-back — interval expressions (`*`, ranges, steps in the seconds/minutes/hours fields) fire "before and
  after clock shifts", non-interval expressions ("30 1 * * *") fire once (no duplicate).

Sources:
- https://www.nuget.org/packages/Cronos/
- https://github.com/HangfireIO/Cronos/blob/main/README.md

### 3.2 NCrontab (verified)

- NuGet: latest **3.4.0, 2025-09-13** (previous 3.3.3 2023-08-31); netstandard1.0/2.0, net35+; supports a
  6-field (seconds) form via `ParseOptions { IncludingSeconds = true }`.
- Source `CrontabSchedule.cs`: `GetNextOccurrence(DateTime)`, `GetNextOccurrence(DateTime, DateTime)`,
  `GetNextOccurrences(DateTime, DateTime)`; **no `TimeZoneInfo` overload, no DST handling** — it does
  component arithmetic on the supplied `DateTime` and preserves its `Kind`. No `L`/`W`/`#`.

Sources:
- https://www.nuget.org/packages/NCrontab/
- https://raw.githubusercontent.com/atifaziz/NCrontab/master/NCrontab/CrontabSchedule.cs

### 3.3 Quartz cron (verified; Quartz.NET 3.18.2 is already pinned)

- Format is **6 or 7 fields (seconds mandatory, year optional 1970–2099)**; `?` is required in one of
  day-of-month/day-of-week ("Support for specifying both a day-of-week and a day-of-month value is not
  complete"); `L`, `W`, `#` supported; `CronExpression.TimeZone` defaults to `TimeZoneInfo.Local`; the
  tutorial only warns "Be careful when setting fire times during daylight savings transitions".
- Quartz's misfire threshold and instructions are in §6.

Sources:
- https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/crontrigger.html
- https://github.com/quartznet/quartznet/blob/main/src/Quartz/CronExpression.cs

**Recommendation (inference from the above):** **Cronos 0.13.0** — the only one of the three with explicit,
documented UTC+`TimeZoneInfo` evaluation and DST rules, optional seconds, zero dependencies, MIT, and it is what
Hangfire uses. Store schedules as 5-field expressions (seconds opt-in per schedule via `CronFormat`), store
the tenant/user IANA-or-Windows zone id alongside (`TimeZoneInfo.FindSystemTimeZoneId` handles both on .NET 6+),
compute `NextDueAt` in UTC with `GetNextOccurrence(lastDueUtc, zone)`, and reject `DateTimeKind.Local` at the
API boundary. Do not reuse Quartz for the tenant scheduler: its format is incompatible with Cronos/Unix cron
(seconds field, `?` rule) and its store is a clustered job store, not the leasing model of this spec.

---

## 4. OpenTelemetry for asynchronous work

### 4.1 Span links vs parent-child (verified)

- OTel trace API spec: "A user MUST have the ability to record links to other SpanContexts. Linked
  SpanContexts can be from the same or a different trace"; spans "MUST have the ability to add Links ...
  after its creation" (`AddLink`), but "Links added after Span creation may not be considered by Samplers" —
  add links at creation when the context is known. `PRODUCER`/`CONSUMER` kinds are for work the producer
  "doesn't await".
- Messaging semantic conventions: status **Development** (not stable); instrumentations "SHOULD NOT change
  the version of the messaging conventions that they emit by default until ... marked stable". Producer
  "SHOULD attach a message creation context to each message"; a Receive/Process span "SHOULD link to the
  message's creation context" for every message it handles; "exclusively for single messages scenarios, the
  Process span MAY use the message's creation context as its parent". Reasons given: a span has one parent,
  batches carry many creation contexts, and a consumer often already has a parent (e.g. an HTTP server
  span). Operation types: `create`/`send` (PRODUCER or CLIENT), `receive` (CLIENT), `process` (CONSUMER),
  `settle` (CLIENT); span name `{messaging.operation.name} {destination}`; required attributes include
  `messaging.system`, `messaging.operation.name`, `messaging.destination.name`, `messaging.message.id`,
  `error.type`.
- .NET: `Activity.AddLink(ActivityLink)` exists since **.NET 9** (present on net-10.0); doc remark: "adding
  links at span creation is preferred ... because head sampling decisions can only consider information
  present during span creation." `ActivitySource.StartActivity(name, kind, parentContext, tags, links)` takes
  links at creation; passing `parentContext: default` with `Activity.Current == null` starts a new trace.
- Baggage (OTel concepts + API spec): propagated with the context (W3C `baggage` header); "remains
  disconnected from span, metric, or log attributes unless explicitly added" (a Baggage span processor can
  copy it); duplicate keys — "the new pair MUST take precedence"; security: "Sensitive Baggage items can be
  shared with unintended resources, like third-party APIs" because instrumentation puts it on outgoing
  requests, and there are no integrity checks on received baggage.

Sources:
- https://opentelemetry.io/docs/specs/otel/trace/api/
- https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/
- https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activity.addlink
- https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts
- https://opentelemetry.io/docs/specs/otel/baggage/api/
- https://opentelemetry.io/docs/concepts/signals/baggage/

### 4.2 How Azure Monitor handles links (verified where stated)

- Azure Monitor OpenTelemetry exporter source (`TraceHelper.cs`): `Activity.Links` are serialised into a
  custom property named **`_MS.links`** as a JSON array of `{"operation_Id":"<TraceId>","id":"<SpanId>"}`,
  capped at **100** links, on both request and dependency items. `ActivityKind.Server`/`Consumer` map to
  `requests`; `Client`/`Producer`/`Internal` map to `dependencies`; span attributes become `customDimensions`.
- **Unverified:** whether the Application Insights portal's end-to-end transaction view *renders* `_MS.links`
  as navigable links. Neither the "Add and modify OpenTelemetry" page nor the transaction-diagnostics page
  mentions links; I found no primary source either way. Treat portal navigation across links as a KQL join
  on `customDimensions["_MS.links"]`, not a UI feature.

Sources:
- https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.Exporter/src/Internals/TraceHelper.cs
- https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-add-modify

**Implication:** Persist the enqueuing request's `traceparent` (and optionally `tracestate`) on the task row;
start the handler's activity as a **new root of kind `Consumer`** with an `ActivityLink` to that context (a
batch of n rows → n links; the claim itself is a `Client` "receive" span); do not make it a child (the request
trace would otherwise stay "open" for hours and sampling of the request would decide sampling of the job). Do
not store or propagate baggage into tasks; carry tenant/user/culture as explicit row columns and set them as
span attributes (bounded tags only, per ARCHITECTURE.md). Expect to prove cross-trace navigation with a KQL
query, because portal support for links is unverified.

---

## 5. Azure SignalR Service and ASP.NET Core 10 SignalR

### 5.1 Versions (verified)

- `Microsoft.Azure.SignalR` latest stable **1.33.1, 2026-06-24**; previous 1.33.0 (2026-02-12) and earlier are
  marked deprecated on NuGet; targets **net8.0 and netstandard2.0** (net10.0 is computed-compatible, not an
  explicit target); depends on `Microsoft.Azure.SignalR.Protocols` 1.33.1.
- `Microsoft.AspNetCore.SignalR.StackExchangeRedis` latest **10.0.11, 2026-08-11**, net10.0, requires
  `StackExchange.Redis >= 2.7.27`.
- ASP.NET Core 10: no new SignalR features listed; SignalR endpoints carry `IApiEndpointMetadata`, so an
  unauthenticated hub connect returns 401 (403 when forbidden) instead of a cookie redirect.

Sources:
- https://www.nuget.org/packages/Microsoft.Azure.SignalR/
- https://www.nuget.org/packages/Microsoft.AspNetCore.SignalR.StackExchangeRedis/
- https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0

### 5.2 Users, IUserIdProvider, IHubContext from background services (verified)

- "By default, SignalR uses the `ClaimTypes.NameIdentifier` from the ClaimsPrincipal associated with the
  connection as the user identifier"; "The user identifier is case-sensitive"; a user "can have multiple
  connections ... If a message is sent to the user, all of the connections associated with that user receive
  the message." Custom mapping: implement `IUserIdProvider.GetUserId(HubConnectionContext)` and register it
  (singleton). Groups: "kept in memory, so they won't persist through a server restart"; membership "isn't
  preserved when a connection reconnects"; "groups are not a security feature".
- `IHubContext<THub>` (and `IHubContext<THub, T>`) is injectable into "a controller, middleware, or other DI
  service" and is resolvable from `IHost.Services` — i.e. usable from a hosted service; when called outside a
  hub "there's no caller associated with the invocation". `IHubContext<THub>` can be cast to non-generic
  `IHubContext` for library code that does not know the hub type.
- With Azure SignalR the app server's `IHubContext.Clients.User(id)` fans out through the service to every
  server; the REST/Management path is separate (5.3).

Sources:
- https://learn.microsoft.com/en-us/aspnet/core/signalr/groups?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/signalr/hubcontext?view=aspnetcore-10.0

### 5.3 Azure SignalR data-plane REST API and Management SDK (verified)

- REST versions: **2024-12-01 (latest)**, 2022-06-01, 1.0 (stable). Operations include
  `POST /api/hubs/{hub}/users/{user}/:closeConnections?api-version=...` (query `application`, `excluded[]`,
  `reason`; **204**), `POST /api/hubs/{hub}/users/{user}/:send` (202), `HEAD /api/hubs/{hub}/users/{user}`
  (200/404 = user has/has no connections), `PUT/DELETE /api/hubs/{hub}/users/{user}/groups/{group}`
  (`ttl` query; "the user lives in the group for 1 year at most"; only the latest 100 TTL-less groups are
  kept per user), `DELETE /api/hubs/{hub}/connections/{connectionId}`. Body ≤ 1 MB, headers ≤ 16 KB.
- Auth: HS256 JWT signed with the service AccessKey, `aud` "Needs to be the same as your HTTP request URL,
  not including the trailing slash and query parameters", or a Microsoft Entra token with scope
  `https://signalr.azure.com/.default`. "You can achieve client identification by including a `nameid` claim
  in each client's JWT ... Azure SignalR Service then uses the value of the `nameid` claim as the user ID".
- Management SDK (`Microsoft.Azure.SignalR.Management`): `ServiceManagerBuilder → ServiceManager →
  CreateHubContextAsync(hub)` gives `ServiceHubContext` with `Clients.User(id).SendAsync`,
  `UserGroups.Add/RemoveFromGroupAsync`, `ClientManager.CloseConnectionAsync`, `UserExists`,
  `NegotiateAsync(new() { UserId = ... })`; transports **Transient** (one HTTP request per message; wraps the
  REST API; retries idempotent requests since 1.22.0) or **Persistent** (WebSocket; needed for multiple
  service instances). "Creating ServiceHubContext is a rather expensive operation" — reuse it.

Sources:
- https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-reference-data-plane-rest-api
- https://learn.microsoft.com/en-us/azure/azure-signalr/swagger/signalr-data-plane-rest-v20241201
- https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-howto-use-management-sdk

### 5.4 Hub-only token facts and CloseOnAuthenticationExpiration (verified)

- `ServiceOptions` (SDK source): `ClaimsProvider` — "func to generate claims from HttpContext. The claims
  will be included in the auto-generated token for clients" (this is how spec 0003's "claims pruned to sub"
  is implemented); `AccessTokenLifetime` — "Default value is one hour"; `AccessTokenAlgorithm` HS256 default
  (HS512 available); `InitialHubServerConnectionCount` 5 per hub; `ServerStickyMode` Disabled by default;
  `GracefulShutdown` (migrate clients) available; `ApplicationName` prefixes hub names (the `application`
  query parameter of the REST API).
- Negotiate response: `{ url: "https://<svc>.service.signalr.net/client/?hub=<hub>", accessToken }` where
  the token's `aud` equals that url — the token is scoped to one hub of one service.
- **`CloseOnAuthenticationExpiration` is supported by the Azure SignalR SDK**: issue #1690 → PR #1699
  "Implement close on client authentication expiration", merged 2022-10-20, shipped in **1.19.0**. Spec 0003's
  statement stands.

Sources:
- https://github.com/Azure/azure-signalr/blob/dev/src/Microsoft.Azure.SignalR/ServiceOptions.cs
- https://github.com/Azure/azure-signalr/issues/1690 and https://github.com/Azure/azure-signalr/pull/1699

### 5.5 Self-hosted multi-instance backplane (verified)

- Redis backplane: `builder.Services.AddSignalR().AddStackExchangeRedis(connectionString, o => o.Configuration.ChannelPrefix = RedisChannel.Literal("MyApp"))`;
  "a Redis backplane is recommended only when it runs in the same data center"; "If your SignalR app is
  running in the Azure cloud, we recommend Azure SignalR Service instead"; "SignalR doesn't buffer messages
  ... Any messages sent while the Redis server is down are lost"; sticky sessions are required on the load
  balancer; a channel prefix isolates apps sharing one Redis.

Source: https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0

**Implication:** The hub's user id must be the identity `sub` via `IUserIdProvider` (default
`NameIdentifier` may not equal `sub` after claim mapping), and the same value must be the `nameid` in the
Azure SignalR client token so the REST `users/{sub}/:closeConnections` and `Clients.User(sub)` agree. Tenant
isolation cannot rely on groups (in-memory, not security); name the hub or the event payload per tenant and
authorise at connect. Per-user thin events from background handlers go through `IHubContext<THub>` on the app
server (works self-hosted with Redis and with Azure SignalR); the Management SDK/REST path is only needed for
processes with no hub (e.g. a separate worker). A single on-prem instance needs no backplane; ≥ 2 instances
need Redis with a channel prefix per distribution and sticky sessions.

---

## 6. Scheduler replay policies in established schedulers

| System | Misfire/catch-up policy (verified) | Overlap policy (verified) | Cut-off safeguard (verified) |
|---|---|---|---|
| **Quartz.NET 3.x** | `quartz.jobStore.misfireThreshold` default **60,000 ms**; `SmartPolicy` for CronTrigger maps to **`FireOnceNow`** ("wants to be fired now"); `DoNothing` = next fire time after now; `IgnoreMisfirePolicy` = "will never be evaluated for a misfire ... simply try to fire it as soon as it can" (i.e. replay every missed firing) | n/a (job `DisallowConcurrentExecution` attribute) | none beyond threshold |
| **Kubernetes CronJob** | Controller checks every 10 s and counts misses "from the last scheduled time to now"; without `startingDeadlineSeconds`, **>100 misses → skips creation and logs an error**; with `startingDeadlineSeconds` a late start is allowed only within that window, otherwise "skips that instance ... Future occurrences remain scheduled"; missed runs while `suspend: true` "count as missed Jobs" and, without a deadline, "are scheduled immediately" on unsuspend | `concurrencyPolicy`: `Allow` (default), `Forbid` (skip while previous runs; a deadline may still let it start after), `Replace` | the 100-miss stop; "A CronJob creates at most one Job per execution time", but duplicates are possible "in certain circumstances" — jobs "should be idempotent" |
| **Airflow 3.3.1** | `catchup=True` creates a run per missed data interval; **`scheduler.catchup_by_default = False`** now | `max_active_runs` | backfill is a separate manual operation |
| **Hangfire** | `MisfireHandlingMode.Relaxed` (**default**): "only a single background job will be created, no matter how many occurrences were missed"; `Strict`: "a new background job ... for every missed occurrence, with 'Time' parameter set to the corresponding schedule time"; `Ignorable`: "no background jobs should be created on missed schedule" | none built in | none; "Your Hangfire Server instance should be always on" |
| **Temporal Schedules** | **Catch-up window, default one year, minimum ten seconds**: after downtime, missed actions inside the window are run, older ones "are skipped unless manually recovered via Backfill" | `Skip` (**default**), `BufferOne`, `BufferAll`, `CancelOther`, `TerminateOther`, `AllowAll` | jitter; pause with notes; limited remaining actions; calendar specs default to UTC and Temporal "recommends using UTC to avoid various surprising properties of time zones" |

Sources:
- https://www.quartz-scheduler.net/documentation/quartz-3.x/configuration/reference.html
- https://github.com/quartznet/quartznet/blob/main/src/Quartz/Impl/Triggers/CronTriggerImpl.cs
- https://github.com/quartznet/quartznet/blob/main/src/Quartz/MisfireInstruction.cs
- https://kubernetes.io/docs/concepts/workloads/controllers/cron-jobs/
- https://airflow.apache.org/docs/apache-airflow/stable/core-concepts/dag-run.html
- https://github.com/HangfireIO/Hangfire/blob/main/src/Hangfire.Core/MisfireHandlingMode.cs
- https://docs.temporal.io/schedule

**Implication (inference):** The three-way policy the brief proposes maps exactly onto industry vocabulary:
`Coalesce` = Quartz `FireOnceNow` / Hangfire `Relaxed` / Temporal-within-window (the universal default),
`ReplayAll` = Quartz `IgnoreMisfirePolicy` / Hangfire `Strict` / Airflow `catchup=True` (with the scheduled
time passed to the run as data), `Skip` = Quartz `DoNothing` / Hangfire `Ignorable` / K8s past-deadline.
Every mature scheduler also has an overlap policy (default: skip while the previous run is still running) —
the brief does not, and it needs one. The backup-restore safeguard has two precedents: a **catch-up window**
(Temporal, default 1 year — too long for this case) and a **miss-count cut-off with an alert** (Kubernetes,
100 misses). A per-schedule window plus a global "gap > X hours ⇒ coalesce + alert" matches both; the
scheduler tick should also record its last tick time so a restore is detected as "now − lastTick ≫ tick
period" independently of any schedule.

---

## 7. Cross-cutting facts worth restating for the designers

- Azure SQL Database runs with RCSI ON by default; on-prem SQL Server does not. Any hint-based claim must
  behave identically under both (verified: `UPDLOCK`+`READPAST` does; bare `READPAST` does not).
- The repo already forbids per-tenant tags on instruments and requires `IMeterFactory` meters named after
  the package; the OTel messaging attributes above (`messaging.system = "tellma.tasks"` style,
  `messaging.destination.name = <handler>`) are bounded and fit, `tenant` must stay a log field.
- `Quartz.Extensions.Hosting` is pinned only for OpenIddict's token pruning in the identity server; nothing
  in this theme should take a dependency on it.

## 8. Unverified / not found

- Portal rendering of `_MS.links` in Application Insights transaction views (no primary source found).
- Cronos' maximum supported year / `MaxValue` (README excerpt did not state it).
- The exact optimizer rule for filtered-index matching against parameterised predicates (the docs only say
  the query predicate must be a subset of the filter; the literal-vs-parameter advice is inference).
- Whether `PeriodicTimer(TimeSpan, TimeProvider)` appeared in .NET 8 (it is present in the .NET 10 API; the
  introducing version was not checked).
