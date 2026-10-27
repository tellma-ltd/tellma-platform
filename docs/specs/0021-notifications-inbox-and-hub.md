# Spec: Notifications, the Inbox, and the Hub

- **Author:** Ahmad Akra
- **Date:** 4 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every product built on the platform tells its users things: your document was approved, the export
you asked for is ready, the import you ran failed on row 3,412, a schedule you own was paused
because its owner was deactivated. This spec ships the machinery that carries those messages to the
person:

- the `core.Notifications` table and its `Notification` entity;
- the per-type registry that says which notifications exist and which can be muted;
- the channel registry: `inbox` and `email` in every distribution, and a module or distribution
  adds its own;
- the `core.NotificationPreferences` rows that hold each user's choices per channel;
- `INotifier`, the writer every service, handler and platform feature calls;
- the delivery job that carries a notification to every channel other than the inbox;
- the inbox's server side: the summary behind the bell (capped counts, the latest items with their
  actors, the user's running jobs) and the seen, read and clear verbs.

Spec 0028 shows them: the bell, its toasts and the Notifications pages. Work assigned to a user is
spec 0028's (`core.Assignments`, the tray), and operator announcements are a later spec's. Neither
is a notification.

It also ships the real-time channel that keeps the bell's badge honest without polling. `TellmaHub`
is one SignalR hub at `/{tenantId}/hub`, connected under the session cookie exactly as spec 0003
§7.4 requires. It carries thin events (`inbox.changed`, `job.changed`, `cache.changed`,
`assignments.changed`, `session.ended`) to per-(tenant, user) groups assigned by the server.
Publishers never see SignalR: they call `IClientEventPublisher`, which rides the same post-commit
hook the notifier uses, so an event is never observed for a transaction that did not commit.

The design rests on the frozen specs and the sibling specs written with it. Spec 0012's `IDataBatch`
is the only way any statement here reaches SQL Server: a notification insert is a fixed-text
statement appended to the caller's persist batch, so notifying a hundred recipients costs zero
additional round trips, and its ids are taken inside the statement rather than from the allocator's
buffer; the same statement enqueues the job that delivers to the other channels when some recipient
enabled one. Spec 0014 owns the user, the connect prologue whose `@tm_UserId` every inbox statement
reads, and the `core.UserStamps` sibling row whose `InboxSeenAt` column this spec adds and whose
`PreferencesTag` this spec's preference save bumps. Spec 0013 owns the ICU localizer that renders
notification text and the localization scope that sets the language, calendar and zone it renders
in. Spec 0015 exposes `Notify` and `Withdraw` on `PersistContext` and `ActionContext` so a
distribution author raises or withdraws a notification with one line inside a save or an action, and
defines the job frame a channel writes through. Spec 0020 is the largest consumer: its completion
and tick batches raise the job and schedule notifications through the same `INotifier`, its job and
schedule actions withdraw them, its `core.Jobs` rows feed the summary's running jobs, its worker
runs the delivery handler, and its `core.notification-retention` schedule runs the retention handler
this spec ships. Spec 0011 defines the listeners the hub implements and the route group the hub is
mapped on; spec 0016 maps it.

Deliberately left to later specs: the `email` channel's delivery (spec 0022, behind the channel seam
of §4.4; the channel's test send is spec 0018's), push channels and their device subscriptions, a
cross-instance nudge over the hub, a rendered `Title` column, and the reserved MCP tool
`tellma_notifications`.

## Goals / Non-goals

**Goals**

- Ship `core.Notifications`, the `Notification` entity, the notification type registry with its
  descriptors and the eleven Core types, the catalogue emitted into the client metadata, and
  `INotificationRenderer` (ICU rendering in a given localization context).
- Ship `INotifier.Notify(batch, requests)` riding the caller's batch with inactive-recipient, mute
  and duplicate predicates inside the statement (a duplicate suppressed or replaced in place; for a
  condition, a read row counts too), ids reserved inside the statement, the conditional enqueue of a
  delivery job per slice of recipients, and the `inbox.changed` event published after commit;
  `INotifier.Withdraw(batch, requests)` deleting a condition's rows by key; `NotifyAsync` and
  `WithdrawAsync` for callers with no batch.
- Ship the channel registry with `inbox` and `email`, the channel seam a sender implements
  (`DeliverAsync` and its test send, `TestNotificationResult`), the `core.notification-delivery`
  handler that calls it, `core.NotificationPreferences`, its self-service `get`/`save` operations,
  the `Notifications.CannotMute` rule and the `PreferencesTag` bump.
- Ship the inbox: `InboxSeenAt`, `inbox/summary` with capped counts, the highest unseen severity,
  the latest ten with their actors and the caller's running jobs, `inbox/seen`, `inbox/read`,
  `inbox/read-all`, `inbox/clear`, `inbox/clear-read`, and the standard query over
  `core.Notification` under the self-scope criterion.
- Ship `TellmaHub`, its groups, the client event catalogue, `IClientEventPublisher` and its
  SignalR implementation, the session and tenant-state listeners, and the three hosting modes
  (in-process, Redis backplane, Azure SignalR Service).
- Ship the `core.notification-retention` handler and schedule, the telemetry, the composition
  sugars' realizers, and the startup checks.

**Non-goals (explicitly out of scope)**

- **The job machinery, the scheduler and their notifications' triggers** — spec 0020; this spec
  only defines the contracts they call and lists their types in the catalogue.
- **Email delivery** — spec 0022 §4 supplies the `email` channel's `DeliverAsync` and spec 0018
  ships its `EmailNotificationChannel` with the test send; this spec registers the channel with an
  editable preference and delivers to it through §4.5.
- **Push channels and device subscriptions** — a later spec, or a distribution, registers them as
  channels (§4.1); nothing is reserved for them here.
- **The welcome email** — spec 0018 §3.3 enqueues it through spec 0022's outbox in the `invite`
  action's frame; this spec ships only the inbox type `core.user.added`.
- **Export and import notifications' content** — spec 0019 raises them; this spec registers the
  descriptors' shape and the catalogue row.
- **Assigned work and operator announcements** — assigned work is spec 0028's
  (`core.Assignments`, the tray); announcements are a later spec's. Neither is a row of
  `core.Notifications`.
- **The bell, its toasts, and the Notifications and Notification settings pages** — spec 0028
  §5–§10; this spec ships the routes they call. The browser's hub connection and the `inbox`
  leader document are spec 0025 §8.
- **The session store, back-channel logout and the BFF** — spec 0011; the hub only listens.
- **MCP tools over notifications** — `tellma_notifications` stays reserved (spec 0016); the
  generic `tellma_query` over `core.Notification` already answers under the self-scope.
- **A cross-instance nudge or any server-to-server use of the hub** — rejected by spec 0020.

## 1. Placement and architecture

### 1.1 Projects and packages

| Piece | Location | Notes |
|---|---|---|
| Contracts | `src/core/Tellma.Core.Abstractions/`, namespaces `Tellma.Core.Abstractions.Notifications` and `Tellma.Core.Abstractions.Realtime` | The contract types of §2–§6 — the entity, interfaces, records, descriptors and options sketched under `Tellma.Core.Abstractions.*` — and §9.1, §10.1; the runtime and web-host types of those sections are in the two rows below. BCL plus `Tellma.Core.Queryex` only. `NotificationRowList`, `NotificationSliceList` and `NotificationPreferenceList` live in `Tellma.Core.Abstractions.TableTypes`. |
| Runtime | `src/core/Tellma.Core/`, namespace `Tellma.Core.Notifications` | `Notifier`, `NotificationStatements`, `NotificationTypeRegistry`, `NotificationChannelRegistry`, `NotificationsClientAssetContributor`, `ClientEventRegistry`, `NotificationRenderer`, `InboxService`, `NotificationPreferencesService`, `NotificationAccessCriteria`, `NotificationDeliveryHandler`, `NotificationDeliveryArguments`, `NotificationRetentionHandler`, `NullClientEventPublisher`, the realizers of `NotificationTypeContributionItem`, `NotificationChannelContributionItem` and `ClientEventContributionItem`. No SignalR reference. |
| Web host | `src/core/Tellma.Core.AspNetCore/`, namespace `Tellma.Core.AspNetCore.Realtime` | `TellmaHub`, `TellmaUserIdProvider`, `HubConnectionTracker`, `SignalRClientEventPublisher`, `TellmaRealtimeOptions`, `RealtimeApplicationName`, `AddTellmaRealtime`. SignalR from the shared framework `Microsoft.AspNetCore.App`; no Azure or Redis package. |
| Composition | `Tellma.Core.Composition` | `CoreFeature` contributes the `Notification` stack, the two `[ApiRoute]` services, the client asset contributor, the criteria provider, the Core notification types this spec owns, the two baseline channels, the client events `inbox.changed` and `session.ended`, the delivery and retention handlers, and the retention handler's built-in schedule. |
| Reference distribution | `distributions/acme/src/Tellma.Distro.Acme.Web/` (`Program.cs`, spec 0011 §1.2) | Composes realtime from the `composeWeb` delegate of `AddTellma` (spec 0011 §5) through `UseAzureWebDefaults()` (`Tellma.Defaults.Azure.AspNetCore`), which carries the `Microsoft.Azure.SignalR` 1.33.1 pin and selects the mode (spec 0011 §1.3). `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.11 is pinned by an on-premises host, not by this distribution. |
| Tests | `test/core/Tellma.Core.Tests/Notifications/`, `test/core/Tellma.Core.IntegrationTests/Notifications/`, `test/core/Tellma.Core.AspNetCore.Tests/Realtime/`, `test/core/Tellma.Core.AspNetCore.IntegrationTests/Realtime/` | §12. |

Dependency edges: `Tellma.Core.Abstractions` → `Tellma.Core.Queryex`; `Tellma.Core` →
`Tellma.Core.Abstractions`, `Tellma.Core.EntityFrameworkCore`, `MessageFormat`;
`Tellma.Core.AspNetCore` → `Tellma.Core` + `Microsoft.AspNetCore.App`. `Tellma.Core` registers
`NullClientEventPublisher` with `TryAdd` semantics so a host without the web package (a dedicated
worker, the migrator, the offline test suites) composes and runs; `Tellma.Core.AspNetCore` replaces
it with the SignalR publisher. Spec 0020's runtime also lives in `Tellma.Core`: the delivery enqueue
writes its `core.Jobs` table and wakes its local poller (§3.3, §3.4), the summary reads it (§5.3),
and its worker runs the delivery and retention handlers, which are ordinary `IJobHandler`s, and the
built-in schedule, an ordinary contribution.

Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are omitted,
so a block is never pasted into code. SQL statements are the exact shape to emit. `{b}` is the
statement's batch ordinal.

### 1.2 Vocabulary

- A **notification** is one row of `core.Notifications`, addressed to one user.
- A **type** is a registered `NotificationTypeDescriptor`; its **key** is the row's `Type`.
- A **channel** is a registered way a notification reaches its recipient: `inbox` is this spec's
  rows; every other channel is a sender behind §4.4.
- A **request** (`NotificationRequest`) fans one message out to many recipients; it becomes one
  statement, up to one row per recipient and up to one delivery job per slice.
- A **slice** is up to `DeliverySliceSize` of a request's recipients in `Ordinal` order; a
  **delivery job** (`core.notification-delivery`) carries one slice to every channel other than the
  inbox (§4.5).
- The **inbox** is one user's rows as §5 serves them: the summary (counts, the latest items, their
  running jobs) and the verbs.
- **Unseen** rows were created after the user last opened the inbox; **unread** rows carry no
  `ReadAt`.
- A **severity** is `Info`, `Success`, `Warning` or `Error`, in that order; a type declares one and
  a request may override it.
- A **condition** is a notification that stands while something holds and is deleted by a
  **withdrawal** when it clears; under `Ongoing` it has at most one row per recipient, read or not.
- A **channel-only** type (`Inbox = false`) writes no row; its requests run the delivery enqueue
  alone.
- A **client event** is a thin, named message pushed through the hub; it carries ids, never
  tenant content.

### 1.3 Principles

1. **A notification never adds a round trip.** The insert is appended to the batch the business
   write already pays for; the hub event fires from that batch's post-commit hook.
2. **Rows are language-neutral.** A row stores a type key and JSON arguments; text is rendered at
   read time in the reader's culture, so a user who switches language sees every notification in
   the new one and the insert needs no per-recipient lookup.
3. **Predicates, not logic, in SQL.** Inactive recipients, muted types and duplicates are
   handled by predicates on the insert and the replace; no procedure, trigger or function exists.
4. **Counters are counted, never maintained.** Unseen and unread are `TOP`-capped counts over
   indexes and the badge's severity a `MAX` over the same capped rows; nothing increments a column.
5. **Bookkeeping never touches audit columns.** `core.Notifications` has no `ModifiedAt`;
   `InboxSeenAt` lives on `core.UserStamps`; neither table bumps a tenant version tag.
6. **The hub is server→client only and carries no content.** Groups are computed from the
   authenticated context; the client fetches what an event announces.

### 1.4 Statement execution rules

Every statement in §3–§5 and §7 is fixed text owned by this spec, executed through spec 0012's
`IDataBatch.Sql(sql, options)` with `SqlOptions.Writes` naming each table it writes. Parameter and
table-variable names use the reserved `@tb{b}_` prefix; the caller's user id is the connect
prologue's `@tm_UserId` (spec 0014's prologue rides every caller batch).

| Statement | `Purpose` | `TransactionMode` | `Idempotent` | `Writes` | Scope |
|---|---|---|---|---|---|
| notify (`INotifier.Notify`) | the caller's (`Persist`) | the caller's (`Auto`) | `false` | `core.Notifications`, plus `core.Jobs` with the delivery enqueue; `core.Jobs` alone for a channel-only type (§3.3) | the caller's |
| notify (`NotifyAsync`) | `Persist` | `Auto` | `false` | `core.Notifications`, plus `core.Jobs` with the delivery enqueue; `core.Jobs` alone for a channel-only type | the current scope |
| withdraw (`INotifier.Withdraw`) | the caller's (`Persist`) | the caller's (`Auto`) | `true` | `core.Notifications` | the caller's |
| withdraw (`WithdrawAsync`) | `Persist` | `Auto` | `true` | `core.Notifications` | the current scope |
| delivery read (§4.5) | `Read` | `Auto` | `true` | none | the delivery job's (system user) |
| inbox summary, preferences get | `Read` | `Auto` | `true` | none | the caller's |
| inbox seen | `Persist` | `Auto` | `true` | `core.UserStamps` | the caller's |
| inbox read, read-all, clear, clear-read | `Persist` | `Auto` | `true` | `core.Notifications` | the caller's |
| preferences save | `Persist` | `Auto` | `false` | `core.NotificationPreferences`, `SqlOptions.ForCaller` | the caller's |
| retention page | `Maintenance` | `None` | `true` | `core.Notifications` | system tenant scope |

`core.Notifications`, `core.NotificationPreferences` and `core.UserStamps` carry no tenant-level
version-tag attribute and never bump a tenant tag. Spec 0013's fixed user-level rule
`UserVersionTagRule(core.NotificationPreferences, Preferences, "UserId")` covers
`core.NotificationPreferences`, so the preferences save names the caller through
`SqlOptions.ForCaller` and the executor's epilogue bumps that user's `PreferencesTag`. The `Read`
batches of the inbox and the delivery handler declare no dependencies and are served regardless of
tag state (`inbox/summary` is a polling endpoint and carries spec 0014's `NoActivityStampMetadata`,
per spec 0016).

## 2. Notifications

### 2.1 `core.Notifications`

Non-temporal; `LOCK_ESCALATION = DISABLE` (the read-all and clear-read verbs touch one user's rows
in one statement); written only by `INotifier` (the insert, the replace and the withdrawal of §3.3),
the inbox verbs (`ReadAt`, the clear deletes) and the retention deletes of §7; `[TableType]` (every
column; used by the fixture tier only); sequence `core.sq_Notifications`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | `PK_Notifications` clustered; `CK_Notifications_Id CHECK ([Id] > 0)`; `core.sq_Notifications` | reserved inside the insert |
| `UserId` | `int` | no | `FK_Notifications_UserId → core.Users(Id)` NO ACTION | the recipient |
| `Type` | `nvarchar(64)` | no | | registered type key |
| `Severity` | `varchar(8)` | no | | `NotificationSeverity` as text (spec 0012's enum convention); the request's override, else the descriptor's |
| `ArgumentsJson` | `nvarchar(4000)` | yes | | ICU arguments; the client's click data |
| `TargetResource` | `varchar(128)` | yes | | securable resource id (`core.Export`, `acme.Document`) |
| `TargetId` | `bigint` | yes | | the target row's id |
| `ActorUserId` | `int` | yes | `FK_Notifications_ActorUserId → core.Users(Id)` NO ACTION | who caused it; null for the system |
| `DedupKey` | `nvarchar(128)` | yes | | best-effort collapse key |
| `CreatedAt` | `datetimeoffset(3)` | no | | `SYSUTCDATETIME()` in the insert |
| `ReadAt` | `datetimeoffset(3)` | yes | | null while unread |

Indexes: `IX_Notifications_User (UserId, CreatedAt DESC, Id DESC) INCLUDE (ReadAt, Type, Severity)`
— the summary's unseen count and severity, the latest-ten page and the clear-read verb;
`IX_Notifications_Unread (UserId) WHERE ReadAt IS NULL` — the unread count and the read verbs;
`IX_Notifications_Dedup (DedupKey, UserId) INCLUDE (ReadAt) WHERE DedupKey IS NOT NULL` —
non-unique, the seek of the duplicate predicate, of the replace and of the withdrawal, `ReadAt` a
residual under `Suppress` and `Replace`; `IX_Notifications_Retention (ReadAt, CreatedAt)` — the
retention pages. No `ON DELETE CASCADE`: a user is never deleted after `State = New` (spec 0014);
deleting a `New` user with rows is a 547 mapped to `Fk.InUse` by the pipeline, which is the correct
answer.

### 2.2 The `Notification` entity and its stack

```csharp
// Tellma.Core.Abstractions.Notifications
public sealed class Notification : Entity<int>   // [Table("Notifications", Schema = "core")], [TableType], [Stack(Operations = Query | Details)], [DefaultSelect(...)]
{
    public int UserId { get; set; }                     // server-owned; FK -> core.Users
    public string Type { get; set; }                    // server-owned
    public NotificationSeverity Severity { get; set; }  // server-owned
    public string? ArgumentsJson { get; set; }          // server-owned; [JsonColumn] is not applied (nvarchar(4000), not max)
    public string? TargetResource { get; set; }         // server-owned
    public long? TargetId { get; set; }                 // server-owned; a JSON string on the wire (spec 0016 §3.2)
    public int? ActorUserId { get; set; }               // server-owned; FK -> core.Users
    public string? DedupKey { get; set; }               // server-owned
    public DateTimeOffset CreatedAt { get; set; }       // datetimeoffset(3); server-owned
    public DateTimeOffset? ReadAt { get; set; }         // datetimeoffset(3); server-owned
}
```

`Notification` is a system-written entity (spec 0012): it derives from `Entity<int>`, carries
`CreatedAt` explicitly, has no audit set and no concurrency token, and is never written through
`Save`. Its stack is `[Stack(Operations = Query | Details)]`: `query`, `get`, `get-by-ids` and
nothing else — no save, delete, actions or Excel operations; `[DefaultSelect]` of every column but
`UserId` and `DedupKey`, which stay selectable. The resource segment is `notifications`; the entity
name — the Queryex name and the securable resource alike — is `core.Notification` (spec 0014's
`CoreResources.Notification`). Navigations from the two FKs: `User` and `ActorUser` (derived from
`ActorUserId`). Neither navigation is in a `[RelatedSelect]` of its own — `User` and `ActorUser`
project through spec 0012's default related projection (`Id`, the `Name` group, the avatar column),
so the notifications page shows the actor's name and image and nothing else about them.

**The self-scope.** `NotificationAccessCriteria : IAccessCriteriaProvider`
(`Resource = "core.Notification"`) returns one criterion for every user:
`AccessCriterion("Read", FilterTree.Leaf("UserId = me()"), "self")`. Spec 0014's composition rule
makes a criterion alone a `Filtered` decision, so every member reads exactly their own rows with no
role grant; the pipeline conjoins the filter into every query and details read of the stack. A
stored role grant on `core.Notification × Read` is accepted by the securables registry (the pair
exists) and widens the scope by spec 0014's `Or` composition — that is the audit path for an
administrator who must see what a user was told and has not cleared, and no such grant is seeded
(review flag 9).

### 2.3 The type registry

```csharp
// Tellma.Core.Abstractions.Notifications
public enum NotificationSeverity { Info, Success, Warning, Error }   // ascending; the row's Severity (§2.1)

public sealed record NotificationTypeDescriptor(
    string Key, string Category, bool Mutable, string? TargetResource,
    NotificationSeverity Severity = NotificationSeverity.Info,       // the rows' default; a request may override it (§3.1)
    bool Toast = false,                                               // a row arriving in an open app pops a toast (spec 0028 §8)
    bool Inbox = true);                                               // false: channel-only; no row is written (§3.3)

public interface INotificationTypeRegistry              // singleton; built at composition; validated by the startup check of §11
{
    IReadOnlyList<NotificationTypeDescriptor> All { get; }
    NotificationTypeDescriptor? Find(string key);       // ordinal comparison
    IReadOnlyList<string> Categories { get; }           // each once, in the order of its first registered type
}
```

| Member | Meaning |
|---|---|
| `Key` | The row's `Type`. Grammar `^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$`, at most 64 characters — the handler-key grammar of spec 0020, cited, not redefined. `core.` is reserved for the platform; modules use their slug (`gl.`), distributions their deployment slug (`acme.document.approved`). Keys are persisted and stable for the life of a distribution; a changed meaning is a new key. |
| `Category` | Lowercase identifier `^[a-z][a-z0-9-]*$`, at most 32 characters; groups the Notification settings page (spec 0028 §10.3). Core categories: `jobs`, `files`, `users`, `reports`. A category's label is the resource `NotificationCategory_<Category>`, declared in the resources of the package that declares the category's first type. |
| `Mutable` | `true`: the user may disable the `inbox` channel for the type. `false`: the inbox channel cannot be disabled (`Notifications.CannotMute` on save; the insert ignores preferences). Every other channel is always the user's choice. |
| `TargetResource` | When set, every request of the type must carry this `TargetResource` and a `TargetId`, and the client maps the resource to a route. When null, requests carry neither. |
| `Severity` | The default severity of the type's rows, overridable per request (§3.1); drives the row's icon and the badge colour (spec 0028 §6). |
| `Toast` | `true`: a row of the type that arrives while the app is open pops a toast (spec 0028 §8). |
| `Inbox` | `false`: a channel-only type — no row, so no inbox, no duplicate rules and no toast; a request runs the delivery enqueue alone (§3.3) and the Notification settings page shows no `inbox` switch (spec 0028 §10.3). Requires `Mutable = false` and `Toast = false`. |

Types are registered through spec 0011's `FeatureContribution.NotificationType(descriptor)`
(`NotificationTypeContributionItem`), realised by `Tellma.Core` into the registry. The Core
catalogue, with the spec whose code raises each type and therefore declares its descriptor:

| Key | Category | `Mutable` | `Severity` | `Toast` | Target | Raised by | Arguments |
|---|---|---|---|---|---|---|---|
| `core.email.failed` | `jobs` | false | `Error` | true | `core.OutgoingEmail` | spec 0022's outbox handler (`DedupKey = 'core.email.failed'`) | `{ count }` |
| `core.export.ready` | `files` | false | `Success` | true | `core.Export` | spec 0019's export handler | `{ fileName, rowCount, fileId }` |
| `core.import.completed` | `files` | false | `Success` | true | `core.Import` | spec 0019's import handler | `{ fileName, rowCount, errorCount }` |
| `core.import.failed` | `files` | false | `Error` | true | `core.Import` | spec 0019's import handler | `{ fileName, errorCode }` |
| `core.job.failed` | `jobs` | false | `Error` | true | `core.Job` | spec 0020 | `{ handlerKey, errorCode }` |
| `core.job.held` | `jobs` | false | `Warning` | true | none | spec 0020 (`DedupKey = 'core.job.held'`) | `{ count }` |
| `core.dashboard.shared` | `reports` | true | `Info` | false | `core.Dashboard` | spec 0035 §7.2, on a newly added user grant (`DedupKey = 'core.Dashboard:<id>'`) | `{ title, actorName }` |
| `core.report.shared` | `reports` | true | `Info` | false | `core.ReportDefinition` | spec 0033 §7, on a newly added user grant (`DedupKey = 'core.ReportDefinition:<id>'`) | `{ title, actorName }` |
| `core.schedule.paused` | `jobs` | true | `Warning` | true | `core.Schedule` | spec 0020 (`DedupKey = 'core.schedule.paused:<scheduleId>'`) | `{ scheduleId, name, reason }` |
| `core.scheduler.gap` | `jobs` | false | `Warning` | true | none | spec 0020 (`DedupKey = 'core.scheduler.gap'`) | `{ previousTickAt, now, heldCount }` |
| `core.user.added` | `users` | true | `Info` | false | none | spec 0018's `UserService.Invite` (`DedupKey = 'core.user.added'`) | `{ tenantName, actorName }` |

Each descriptor is declared once, by the feature code that raises it (the table's "Raised by"
column). The one exception is `core.user.added`, which this spec's `CoreFeature` contribution
declares. The registry rejects a duplicate key at startup. A type's duplicate policy and the
trigger that withdraws it are the raiser's (spec 0020 §10, spec 0022 §5, spec 0018 §3.3, spec 0033
§7, spec 0035 §7.2). A distribution declares its own types the same way, in its feature's
`Contribute`.

### 2.4 Rendering

```csharp
// Tellma.Core.Abstractions.Notifications
public interface INotificationRenderer                  // scoped
{
    string Render(string type, string? argumentsJson, string? targetResource, long? targetId,
        LocalizationContext localization);
}
```

`Render` resolves the resource `Notification_<Key>` — the key with every `.` replaced by `_`
(`Notification_core_export_ready`) — through spec 0013's `IcuStringLocalizerFactory` in the
declaring package's `Resources/Strings.resx` (satellites ship with the package that declares the
type; a distribution's types resolve against the distribution's resources), and formats it with
`argumentsJson` as ICU named arguments: top-level JSON members only; strings as strings, numbers as
numbers, booleans as booleans, ISO 8601 strings as `DateTimeOffset`, anything else as its JSON text.
Both steps run inside spec 0013 §10.2's `LocalizationScope` entered with `localization`, so the
resource resolves in that context's culture and dates render in its calendar and display zone,
whichever request or job is running. A missing resource renders the key itself and counts on spec
0013's `tellma.localization.missing`; malformed `argumentsJson` renders with no arguments and logs
`NotificationArgumentsInvalid` (warning, once per type and arguments text in a scope).

- **The detail line.** A type may add the resource `Notification_<Key>_Detail`
  (`Notification_core_import_failed_Detail`), a second line formatted with the same arguments.
  `Render` returns the title, then the detail on a new line when the resource exists.
- **Who renders.** The summary endpoint never calls the renderer: `InboxSummary.Latest`, `Related`
  and `Running` carry raw rows. The SPA renders them from spec 0013's string pack, and a job's
  progress as spec 0020 §3.3 renders it. The server-side renderer serves two callers: MCP, which
  passes a row's columns and the reader's context, and the channels of §4.4, which pass each
  recipient's context.
- **No text column.** Spec 0028's Notifications page shows the rendered text as a client-side
  column; sorting and filtering on rendered text is not offered (review flag 1).

### 2.5 The catalogue in the client metadata

Types, channels and client events are composed per distribution, never per tenant, so they reach
the SPA in the Core chunk of the client metadata, built with the bundle. No route serves them.

```csharp
// Tellma.Core.Notifications (runtime)
public sealed class NotificationsClientAssetContributor : IClientAssetContributor;   // spec 0024 §3.2; registered by CoreFeature (§11)
```

When spec 0024's emit runs, the contributor writes three sections of the Core chunk, serialised
with the platform JSON options (spec 0016 §3.1: camelCase, enums as strings):

- **`notificationTypes`** — `INotificationTypeRegistry.All` (§2.3) in registration order, one
  `NotificationTypeDescriptor` per item with all its members. The client's
  `TmNotificationTypeMetadata` mirrors it. Categories appear in the order of their first type,
  which is `INotificationTypeRegistry.Categories`.
- **`notificationChannels`** — `INotificationChannelRegistry.All` (§4.1) in registration order, one
  `NotificationChannelDescriptor` (`key`, `defaultEnabled`) per item.
- **`clientEvents`** — `IClientEventRegistry.Names` (§6.3).

**Example —** the Acme sections, shortened:

```json
"notificationTypes": [
  { "key": "core.export.ready", "category": "files", "mutable": false, "targetResource": "core.Export",
    "severity": "Success", "toast": true, "inbox": true }
],
"notificationChannels": [ { "key": "inbox", "defaultEnabled": true }, { "key": "email", "defaultEnabled": false } ],
"clientEvents": [ "inbox.changed", "session.ended", "cache.changed", "job.changed", "assignments.changed" ]
```

Spec 0028 reads `Severity`, `Toast`, `Category`, `Mutable`, `Inbox` and `TargetResource` from
these sections for the bell, the toasts and the Notification settings page. The preferences
endpoint returns deviations only (§4.3).

## 3. `INotifier`

### 3.1 Contracts

```csharp
// Tellma.Core.Abstractions.Notifications
public enum DuplicatePolicy { Suppress, Replace, Ongoing }

public sealed record NotificationRequest(
    string Type, IReadOnlyList<int> RecipientIds, object? Arguments, string? TargetResource,
    long? TargetId, int? ActorUserId, string? DedupKey, DuplicatePolicy OnDuplicate = DuplicatePolicy.Suppress,
    bool InboxOnly = false,                             // true: the inbox row only; no delivery job
    NotificationSeverity? Severity = null);             // null: the descriptor's

public sealed record WithdrawRequest(string DedupKey, IReadOnlyList<int>? RecipientIds = null);   // null: every recipient

public interface INotifier                              // scoped
{
    void Notify(IDataBatch batch, IReadOnlyList<NotificationRequest> requests);
    Task NotifyAsync(IReadOnlyList<NotificationRequest> requests);
    void Withdraw(IDataBatch batch, IReadOnlyList<WithdrawRequest> requests);
    Task WithdrawAsync(IReadOnlyList<WithdrawRequest> requests);
}

public sealed class NotificationsOptions                // Tellma:Notifications; validated at startup
{
    public int MaxRecipientsPerRequest { get; set; } = 10000;
    public int MaxArgumentsChars { get; set; } = 4000;
    public int SummaryCountCap { get; set; } = 100;
    public int SummaryLatestCount { get; set; } = 10;
    public TimeSpan ReadRetention { get; set; } = TimeSpan.FromDays(90);
    public TimeSpan UnreadRetention { get; set; } = TimeSpan.FromDays(365);
    public int RetentionPageSize { get; set; } = 1000;
    public int DeliverySliceSize { get; set; } = 250;
}
```

| Member | Meaning |
|---|---|
| `NotificationRequest.Type` | A registered key (§2.3). |
| `RecipientIds` | User ids; duplicates collapse; the empty list appends nothing. At most `MaxRecipientsPerRequest`. |
| `Arguments` | Serialised with the platform JSON options (spec 0016) into `ArgumentsJson`; `null` stores `NULL`. At most `MaxArgumentsChars` after serialisation. |
| `TargetResource`, `TargetId` | Required together when the descriptor declares a target, and must equal the descriptor's resource; forbidden when it declares none. |
| `ActorUserId` | Defaults to `RequestContext.UserId`; `null` in a `System` scope. Must be an existing user id at insert time (the FK). |
| `DedupKey` | At most 128 characters. A row of the recipient with the same `DedupKey` is a **duplicate** — unread rows only under `Suppress` and `Replace`, read rows too under `Ongoing`; `OnDuplicate` decides its fate. Keys are matched without regard to `Type`. Activity about a record keys by `<resource>:<id>` (`acme.Document:42`); everything else keys by its type key, with an anchor where it needs one (`core.schedule.paused:42`, `core.job.held`). |
| `OnDuplicate` | `Suppress` (default): the recipient receives nothing while a duplicate exists. `Replace`: the duplicate becomes this request's row — type, severity, arguments, target, actor and a fresh `CreatedAt`, same id, still unread — so it resurfaces unseen and on top, and the recipient receives `inbox.changed`; recipients without a duplicate receive a new row. `Ongoing`: as `Suppress`, but a read row counts as a duplicate too, so a condition re-raised while it holds writes nothing; the raiser withdraws it when it clears, and withdraws and re-raises it in one batch when its facts change. A row the recipient cleared (§5.2) returns at the next raise while the condition holds. `Replace` and `Ongoing` require a `DedupKey`. |
| `InboxOnly` | `true`: the request writes inbox rows only and enqueues no delivery job (§3.3), so no other channel delivers it. A notice about a failing delivery path is raised this way (spec 0020's `InboxOnlyFailureNotices`, spec 0022's `core.email.failed`), so it never re-enters that path. |
| `Severity` | `null`: the descriptor's `Severity`. Set: this request's rows take it, so one type can raise `Warning` at 90% and `Error` at 100%. |
| `Notify` | Appends one statement of §3.3 per request to `batch` — the inbox insert (and replace), then the conditional delivery enqueue, or the enqueue alone for a channel-only type — registers the batch's post-commit callback (§3.4), returns synchronously. Never awaits, never allocates an id from the allocator's buffer. |
| `NotifyAsync` | Creates a `Persist` batch on spec 0012's `ITenantDatabase` of the current scope, calls `Notify`, executes it. For callers with no batch in flight (a hosted service, a post-commit effect). |
| `WithdrawRequest.DedupKey` | Required; at most 128 characters. Every row with the key is deleted, read or not, whatever its type. |
| `WithdrawRequest.RecipientIds` | `null`: every recipient's rows. Else the user ids whose rows are deleted; duplicates collapse; the empty list appends nothing; at most `MaxRecipientsPerRequest`. |
| `Withdraw` | Appends one statement of §3.3 per request to `batch`, shares the post-commit callback of §3.4, returns synchronously. A key with no rows deletes nothing. |
| `WithdrawAsync` | As `NotifyAsync`, for `Withdraw`. |

Every rule in the table is checked in C# before any statement is appended and fails the call with
`InvalidOperationException` — a request is composed by platform or distribution code, never by a
client, so a violation is a programming error (a 500 with a trace id), not a validation error. A
type that is not registered on this instance fails the same way — a distribution never raises a type
it did not declare — and so do `InboxOnly` and a `DedupKey` on a request of a channel-only type
(§2.3), which has no row for either. A call carries any number of requests, and several calls on one
batch are the same as one call with their union; the only bound is spec 0012's parameter cap per
batch — nine parameters per request (eight scalars and the recipients TVP), eleven with the delivery
enqueue, four for a channel-only type (§3.3); a withdrawal takes one, two with named recipients.

### 3.2 The standalone table types

`NotificationRowList` (`Tellma.Core.Abstractions.TableTypes`; physical
`[dbo].[NotificationRowList_<hash8>]` per spec 0001 §5): `Ordinal int PK`, `UserId int`. One row per
distinct recipient, `Ordinal` from 0 in the recipients' first-seen order. Bound through
`IDataBatch.Tvp<NotificationRowList>(rows)`.

`NotificationSliceList` (same namespace and naming): `SliceOrdinal int PK`, `FirstOrdinal int`,
`LastOrdinal int`, `ArgumentsJson nvarchar(max)`. One row per slice of a request's recipients
(§3.3), `SliceOrdinal` from 0; `FirstOrdinal` and `LastOrdinal` are the inclusive
`NotificationRowList` ordinals the slice covers, and `ArgumentsJson` is the slice's
`NotificationDeliveryArguments` (§4.5), serialised with the platform JSON options. Bound through
`IDataBatch.Tvp<NotificationSliceList>(rows)`.

### 3.3 The insert, the replace, the withdrawal and the delivery enqueue

Per request, ordinal `{b}`, with `SqlOptions(Writes = [core.Notifications], ResultSets = 1)`:
`@tb{b}_t0 : NotificationRowList`; scalars `@tb{b}_p0 nvarchar(64)` type, `@tb{b}_p1 varchar(8)` the
resolved severity (§3.1), `@tb{b}_p2 nvarchar(4000)` arguments, `@tb{b}_p3 varchar(128)` target
resource, `@tb{b}_p4 bigint` target id, `@tb{b}_p5 int` actor, `@tb{b}_p6 nvarchar(128)` dedup key,
`@tb{b}_p7 bit` the descriptor's `Mutable`. Under `Replace` the replace below runs ahead of the
insert and adds a result set; under `Ongoing` the insert's duplicate predicate omits its
`[n].[ReadAt] IS NULL` term. Unless the request is `InboxOnly` or no channel has a sender, the
delivery enqueue follows the insert: it binds `@tb{b}_t1 : NotificationSliceList` and
`@tb{b}_p8 nvarchar(55)` the trace parent, adds `core.Jobs` to `Writes` and adds a result set. For a
channel-only type (§2.3) the replace and the insert are omitted, and the request is the delivery
enqueue alone, or nothing when no channel has a sender. That enqueue binds the recipients, the
slices, the type and the trace parent, named by spec 0012 §5.4 from the text it contains, with
`Writes = [core.Jobs]` and `ResultSets = 1`.

```sql
-- Replace only, ahead of the insert:
UPDATE [n] SET [Type] = @tb{b}_p0, [Severity] = @tb{b}_p1, [ArgumentsJson] = @tb{b}_p2, [TargetResource] = @tb{b}_p3,
               [TargetId] = @tb{b}_p4, [ActorUserId] = @tb{b}_p5, [CreatedAt] = SYSUTCDATETIME()
OUTPUT [inserted].[UserId]
FROM [core].[Notifications] AS [n] JOIN @tb{b}_t0 AS [r] ON [r].[UserId] = [n].[UserId]
WHERE [n].[DedupKey] = @tb{b}_p6 AND [n].[ReadAt] IS NULL
  AND EXISTS (SELECT 1 FROM [core].[Users] AS [u] WHERE [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1)
  AND (@tb{b}_p7 = 0 OR NOT EXISTS (SELECT 1 FROM [core].[NotificationPreferences] AS [p]
                                    WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = @tb{b}_p0 AND [p].[Channel] = 'inbox' AND [p].[Enabled] = 0));

-- Every request of a type with Inbox = true:
DECLARE @tb{b}_n int = (SELECT COUNT(*) FROM @tb{b}_t0);
DECLARE @tb{b}_first sql_variant;
EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Notifications', @range_size = @tb{b}_n, @range_first_value = @tb{b}_first OUTPUT;
INSERT INTO [core].[Notifications] ([Id], [UserId], [Type], [Severity], [ArgumentsJson], [TargetResource], [TargetId], [ActorUserId], [DedupKey], [CreatedAt])
OUTPUT [inserted].[UserId]
SELECT CAST(@tb{b}_first AS int) + [r].[Ordinal], [r].[UserId], @tb{b}_p0, @tb{b}_p1, @tb{b}_p2, @tb{b}_p3, @tb{b}_p4, @tb{b}_p5, @tb{b}_p6, SYSUTCDATETIME()
FROM @tb{b}_t0 AS [r]
WHERE EXISTS (SELECT 1 FROM [core].[Users] AS [u] WHERE [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1)
  AND (@tb{b}_p7 = 0 OR NOT EXISTS (SELECT 1 FROM [core].[NotificationPreferences] AS [p]
                                    WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = @tb{b}_p0 AND [p].[Channel] = 'inbox' AND [p].[Enabled] = 0))
  AND (@tb{b}_p6 IS NULL OR NOT EXISTS (SELECT 1 FROM [core].[Notifications] AS [n]
                                        WHERE [n].[UserId] = [r].[UserId] AND [n].[DedupKey] = @tb{b}_p6 AND [n].[ReadAt] IS NULL));

-- Delivery enqueue, after the insert, or alone for a channel-only type, unless InboxOnly or no channel has a sender (the condition shown: email, DefaultEnabled = false):
DECLARE @tb{b}_qualified TABLE ([SliceOrdinal] int NOT NULL PRIMARY KEY);
INSERT INTO @tb{b}_qualified ([SliceOrdinal])
SELECT [s].[SliceOrdinal] FROM @tb{b}_t1 AS [s]
WHERE EXISTS (SELECT 1 FROM @tb{b}_t0 AS [r] INNER JOIN [core].[Users] AS [u] ON [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1
              WHERE [r].[Ordinal] BETWEEN [s].[FirstOrdinal] AND [s].[LastOrdinal]
                AND (COALESCE((SELECT [p].[Enabled] FROM [core].[NotificationPreferences] AS [p]
                               WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = @tb{b}_p0 AND [p].[Channel] = 'email'), 0) = 1
                     -- OR one such term per further channel with a sender, its key and DefaultEnabled inlined
                    ));
DECLARE @tb{b}_jobFirst sql_variant, @tb{b}_jobN int = (SELECT COUNT(*) FROM @tb{b}_qualified);
IF @tb{b}_jobN > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Jobs', @range_size = @tb{b}_jobN, @range_first_value = @tb{b}_jobFirst OUTPUT;
INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [TraceParent], [RunAsUserId], [RequestedById], [CreatedAt])
OUTPUT [inserted].[Id]
SELECT CAST(@tb{b}_jobFirst AS int) + ROW_NUMBER() OVER (ORDER BY [s].[SliceOrdinal]) - 1, N'core.notification-delivery', 'Pending',
       SYSUTCDATETIME(), 0, [s].[ArgumentsJson], @tb{b}_p8, 1, NULL, SYSUTCDATETIME()   -- 1 = WellKnownIds.SystemUserId
FROM @tb{b}_t1 AS [s] INNER JOIN @tb{b}_qualified AS [q] ON [q].[SliceOrdinal] = [s].[SliceOrdinal];
```

```sql
-- Withdraw, per request (Writes = [core.Notifications], ResultSets = 1; @tb{b}_p0 nvarchar(128) the key):
DELETE [n] OUTPUT [deleted].[UserId], [deleted].[Type]
FROM [core].[Notifications] AS [n] WHERE [n].[DedupKey] = @tb{b}_p0;

-- Withdraw for named recipients (also @tb{b}_t0 : IdList):
DELETE [n] OUTPUT [deleted].[UserId], [deleted].[Type]
FROM [core].[Notifications] AS [n] WHERE [n].[DedupKey] = @tb{b}_p0 AND [n].[UserId] IN (SELECT [Id] FROM @tb{b}_t0);
```

Rules: ids are reserved inside the statement, never through the allocator (an id filtered out by a
predicate is simply unused — sequences are not gap-free and nothing reads them as a count); the
range is taken even when every recipient is filtered, because `@tb{b}_n > 0` is guaranteed by the C#
side; `@tb{b}_p7 = 0` short-circuits the preferences seek for non-mutable types; the duplicate
predicate is best effort — two concurrent batches can both pass it and both insert, which is
accepted because a unique filtered index would fail a business save over a notification race (review
flag 2); under `Ongoing` the same race can leave two rows for one condition, and one withdrawal
removes both; under `Replace` the update runs first and the insert's duplicate predicate then skips
every row it refreshed, so a recipient gets a refreshed row or a new one, never both, and a replace
is exactly as best-effort as the predicate it rides; an unknown recipient id inserts nothing (the
`EXISTS` on `core.Users`), and a recipient deactivated between validation and persist inserts
nothing, so a save never fails for a notification reason. The withdrawal deletes read and unread
rows alike and is idempotent. The `OUTPUT` result sets — the user ids inserted, under `Replace` the
ids refreshed, and for a withdrawal the user ids and types deleted — are read by the notifier after
execution and feed §3.4 and the `tellma.notifications.created`, `tellma.notifications.replaced` and
`tellma.notifications.withdrawn` counters; the difference between `@tb{b}_n` and the rows returned
feeds `tellma.notifications.suppressed`.

The delivery enqueue inserts one `core.notification-delivery` job (§4.5) for each slice that holds
an active recipient with some channel enabled for the type, by the user's preference row or else the
channel's `DefaultEnabled`; other slices get none, and with `email` off by default the preference
seek usually finds nothing. `Notify` cuts the request's recipients into slices (§1.2) and binds them
as one `NotificationSliceList` (§3.2), so the statement's parameters are fixed and a slice's
arguments stay within spec 0020's 64 KB cap. The condition is fixed text, built once at startup from
the channels that the `notification-channels` check (§11) pairs with a sender: one term per channel,
joined by `OR`, with the channel's key and `DefaultEnabled` inlined as literals (the key grammar of
§4.1 admits no quote). It leaves out the inbox's mute and duplicate rules (§4.4). The job ids come
from `core.sq_Jobs` inside the statement, as spec 0020 §7.6's tick step 2 takes them, and the range
is taken only when a slice qualifies. `@tb{b}_p8` is the enqueuing activity's `traceparent`, copied
as spec 0020 §4.3 copies it. The ids the `OUTPUT` returns feed §3.4's nudge and
`tellma.notifications.delivery_enqueued`. A channel-only request feeds only `delivery_enqueued`.

### 3.4 After commit

The first `Notify` or `Withdraw` call on a batch registers one `IDataBatch.OnCommitted` callback for
the whole batch. After the round trip commits, the callback unions the inserted, refreshed and
withdrawn user ids of every request on the batch and publishes one
`ClientEvent("inbox.changed", userIds, null)` through `IClientEventPublisher.PublishAsync` (§6.4),
one event per user however many requests targeted them. When any request's delivery enqueue returned
a job id, the callback also wakes the local `TenantPoller` of the batch's tenant, as spec 0020
§4.4's nudge does, so the delivery jobs are claimed at once. The callback catches its own failures
and logs them (`InboxEventFailed`, warning), so spec 0012's `OnCommitted` failure event never fires
for it; the rows are committed either way, the client's next summary call sees them, and the next
poll claims the jobs.

### 3.5 Raising a notification from the pipeline

Spec 0015 exposes `Notify(request)` and `Withdraw(request)` on `PersistContext` and `ActionContext`,
each calling `INotifier` on the persist batch, so a service hook raises or withdraws a notification
in the same transaction as the write it describes and the event fires only when that write commits.

*Illustration* — a distribution's `ContributeAsync` hook after approving a document:

```csharp
context.Notify(new NotificationRequest("acme.document.approved", [doc.CreatedById], new { number = doc.Number, comment },
    "acme.Document", doc.Id, context.Context.UserId, DedupKey: $"acme.Document:{doc.Id}",
    OnDuplicate: DuplicatePolicy.Replace));
```

A later event on the same document while this notice is unread — a comment after the approval —
refreshes it in place for the same recipient: the latest event, back on top; once it was read, the
next event inserts a fresh notice. Spec 0020's job and schedule actions withdraw their notices
through `context.Withdraw` (spec 0020 §10). Spec 0020's handlers call
`JobItem.NotifyOnSuccess(request)` and the worker calls `INotifier.Notify` on the completion batch;
both paths end in §3.3, and every channel other than the inbox receives the notification through the
delivery job of §4.5.

## 4. Channels and preferences

### 4.1 The channel registry

```csharp
// Tellma.Core.Abstractions.Notifications
public sealed record NotificationChannelDescriptor(string Key, bool DefaultEnabled);

public interface INotificationChannelRegistry           // singleton; built at composition; validated by the startup check of §11
{
    IReadOnlyList<NotificationChannelDescriptor> All { get; }
    NotificationChannelDescriptor? Find(string key);    // ordinal comparison
}
```

A channel is registered through spec 0011's `FeatureContribution.NotificationChannel(descriptor)`
(`NotificationChannelContributionItem`), realised by `Tellma.Core` into the registry; a duplicate
key is rejected at startup. `Key` follows the category grammar of §2.3 (`^[a-z][a-z0-9-]*$`), at
most 16 characters; its label is `NotificationChannel_<Key>` in the declaring package's resources.
`DefaultEnabled` is what a user who saved nothing gets. `CoreFeature` registers the two baseline
channels of every distribution: `inbox` (`DefaultEnabled = true`), which is the statement of §3.3,
and `email` (`DefaultEnabled = false`), whose `INotificationChannel` is spec 0018's
`EmailNotificationChannel`, delivering as spec 0022 §4 specifies. A module or a distribution
registers further channels (`push`, `sms`, `whatsapp`) the same way, each with its sender; a
distribution hides nothing, because a channel it did not register does not exist on its instance.

### 4.2 `core.NotificationPreferences`

Non-temporal; not an entity; absent from the Queryex schema; no UDTT.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `UserId` | `int` | no | PK part; `FK_NotificationPreferences_UserId → core.Users(Id) ON DELETE CASCADE` | |
| `Type` | `nvarchar(64)` | no | PK part | a registered type key; unknown keys are refused on save |
| `Channel` | `varchar(16)` | no | PK part | a registered channel key; unknown keys are refused on save |
| `Enabled` | `bit` | no | | ignored by the insert for `inbox` on a non-mutable type |

PK clustered `(UserId, Type, Channel)`. A missing row means the channel's `DefaultEnabled` for the
type; the save stores deviations only (§4.3), so the table stays small and the delivery condition's
seek (§3.3) usually finds nothing. The cascade is the one place a user-sibling row disappears with
the user, and it fires only for a `New` user (spec 0014's delete policy), who cannot have saved
preferences — the cascade exists for schema honesty, not for a path that runs.

### 4.3 The service and its table type

`NotificationPreferenceList` (`Tellma.Core.Abstractions.TableTypes`): `Type nvarchar(64)`,
`Channel varchar(16)`, `Enabled bit`; PK `(Type, Channel)`.

```csharp
// Tellma.Core.Abstractions.Notifications
public sealed class NotificationPreference              // row shape; not an entity; always the caller's own
{
    public string Type { get; set; }
    public string Channel { get; set; }
    public bool Enabled { get; set; }
}

// Tellma.Core.Notifications (runtime)
public sealed class NotificationPreferencesService      // [ApiRoute("notification-preferences")]
{
    public Task<IReadOnlyList<NotificationPreference>> GetAsync();
    public Task<IReadOnlyList<NotificationPreference>> SaveAsync(IReadOnlyList<NotificationPreference> preferences);
}
```

| Member | Annotation and rule |
|---|---|
| `Get` | `[ApiAction("get", MemberOnly = true, Idempotent = true, Mutation = false)]`. One `Read` batch: `SELECT [Type], [Channel], [Enabled] FROM [core].[NotificationPreferences] WHERE [UserId] = @tm_UserId;`. Returns the stored deviations. The types, categories and channels come from the client metadata (§2.5); their labels `NotificationType_<Key>`, `NotificationCategory_<Category>` (§2.3) and `NotificationChannel_<Key>` (`.` → `_`) come from spec 0013's string pack. |
| `Save` | `[ApiAction("save", MemberOnly = true)]`. The list is the caller's complete set: rows absent from it revert to defaults. Validation (422, path `[i].type` / `[i].channel` / `[i].enabled`): `Notifications.UnknownType` when `Type` is not registered; `Notifications.UnknownChannel` when `Channel` is not; `Notifications.DuplicatePreference` when a `(Type, Channel)` pair repeats; `Notifications.CannotMute` when `Enabled = false` for `inbox` on a non-mutable type. Rows equal to the channel's default are dropped before binding; the rest bind as `@tb{b}_t0 : NotificationPreferenceList` and run the statement below in one `Persist` batch with `SqlOptions.ForCaller([core.NotificationPreferences])`, so the epilogue bumps the caller's `PreferencesTag`. Returns the same shape as `Get` from the rows just written. |

```sql
DELETE P FROM [core].[NotificationPreferences] AS P
WHERE P.[UserId] = @tm_UserId AND NOT EXISTS (SELECT 1 FROM @tb{b}_t0 AS I WHERE I.[Type] = P.[Type] AND I.[Channel] = P.[Channel]);
UPDATE P SET P.[Enabled] = I.[Enabled]
FROM [core].[NotificationPreferences] AS P JOIN @tb{b}_t0 AS I ON I.[Type] = P.[Type] AND I.[Channel] = P.[Channel]
WHERE P.[UserId] = @tm_UserId AND P.[Enabled] <> I.[Enabled];
INSERT INTO [core].[NotificationPreferences] ([UserId], [Type], [Channel], [Enabled])
SELECT @tm_UserId, I.[Type], I.[Channel], I.[Enabled] FROM @tb{b}_t0 AS I
WHERE NOT EXISTS (SELECT 1 FROM [core].[NotificationPreferences] AS P WHERE P.[UserId] = @tm_UserId AND P.[Type] = I.[Type] AND P.[Channel] = I.[Channel]);
```

The `PreferencesTag` bump is what makes spec 0014's cached `UserProfile` and the SPA's `preferences`
wire tag move, so a second tab learns of the change on its next request. Rows for a type or channel
whose descriptor was removed by a deployment stay in the table, are invisible to `Get` (unknown keys
are filtered out of its result), and are deleted by the next `Save` (absent from the list); nothing
acts on them, because no request carries a removed type and no sender exists for a removed channel.

### 4.4 The channel seam

```csharp
// Tellma.Core.Abstractions.Notifications
public sealed record NotificationRecipient(
    int UserId, string Name, string? Email, string? ContactEmail, string? ContactMobile,
    LocalizationContext Localization);                  // the user's Preferred*, else the tenant's

public enum TestNotificationResult { Sent, NoContactAddress, NotConfigured }

public interface INotificationChannel                   // scoped; at most one per registered channel other than inbox
{
    string Key { get; }
    Task DeliverAsync(IOpenWriteHost host, NotificationRequest request, IReadOnlyList<NotificationRecipient> recipients);
    Task<TestNotificationResult> SendTestAsync();       // one test message to the caller through the channel's transport; outside any batch
}
```

`inbox` delivers through the notifier's own statement (§3.3). Every other registered channel
delivers through an `INotificationChannel`, registered as a scoped service by the feature that
registered the descriptor; the startup check of §11 verifies the pairing, and a channel with no
sender stores its preference and delivers nothing. The delivery handler (§4.5) calls `DeliverAsync`
with the job frame's `IOpenWriteHost`, the request and the recipients it resolved for the channel:
active users with the channel enabled for the type, each with the `LocalizationContext` their text
renders in.

A channel that writes rows enlists them with `host` (spec 0015 §13.3), so they commit with the
delivery job's completion; `email` enlists its rows through spec 0022's outbox (spec 0022 §4). A
channel that calls an API sends from `DeliverAsync`, so a crash between the send and the completion
re-runs the job and can send twice; a channel that needs fail-closed delivery writes its own outbox
row, as `email` does. An exception from `DeliverAsync` fails the job attempt (§4.5) and never
reaches the operation that raised the notification.

Channels have no dedup: the inbox's duplicate rules govern inbox rows only (review flag 15). A
channel registered with `DefaultEnabled = true` satisfies §3.3's delivery condition for every active
recipient who has not opted out, so nearly every request enqueues a delivery job.

`SendTestAsync` serves spec 0018 §3.5's `me/test-notification`: it runs outside any batch, resolves
the caller's address from `ConnectedUser.Profile` (`ContactEmail ?? Email` for `email`,
`ContactMobile` for an SMS channel), and answers `NoContactAddress` when the profile holds none,
`NotConfigured` when the channel's transport is not configured on this instance, and `Sent` after
one send; a transport failure is `DependencyUnavailableException`.

### 4.5 The delivery handler

```csharp
// Tellma.Core.Notifications
[JobHandler("core.notification-delivery", BatchSize = 4, MaxAttempts = 12, InboxOnlyFailureNotices = true)]
public sealed class NotificationDeliveryHandler : IJobHandler;

public sealed record NotificationDeliveryArguments(     // the job's ArgumentsJson; one slice of one request
    string Type, NotificationSeverity Severity, string? ArgumentsJson, string? TargetResource, long? TargetId,
    int? ActorUserId, IReadOnlyList<int> RecipientIds);
```

Each delivery job carries one slice of one request (§3.3) and runs as the system user with no
requester, so every claim is one run-as partition (spec 0020 §3.5). An invocation handles up to four
slices (`BatchSize`):

1. **Load.** One `Read` round trip runs the statement below once per item and reads the slice's
   active users and their preference rows for the type. The read comes seconds after the raise, so a
   user who opts out, changes `ContactEmail` or is deactivated in between is honoured.
2. **Resolve.** For each item, and each channel with a sender in registration order (§4.1), lists
   the slice's recipients, in order, whose preference row for the channel, else its
   `DefaultEnabled`, is on. Each recipient's `LocalizationContext` comes from spec 0013's
   `ILocalizationNegotiator`, given their `Preferred*` columns, the tenant's settings and no
   headers, as spec 0013 §9.3 does for a background scope's run-as user.
3. **Deliver.** Calls `DeliverAsync` for each channel with a non-empty list, on the job frame's
   `IOpenWriteHost` (spec 0020 §8). The request is rebuilt from the arguments: `RecipientIds` is the
   slice's, `Severity` the resolved one, `Arguments` the stored JSON as a `JsonElement` (null when
   none), and `DedupKey` null.
4. **Persist.** Calls `PersistAsync` on the frame once, after every item is delivered, so every
   channel's enlisted rows commit with the completion (spec 0020 §5.6) and a slice re-run before
   that commit finds nothing written.

```sql
-- per item, ordinal {b}: @tb{b}_t0 : IdList the slice's RecipientIds, @tb{b}_p0 nvarchar(64) the type; ResultSets = 2
SELECT [u].[Id], [u].[Name], [u].[Email], [u].[ContactEmail], [u].[ContactMobile],
       [u].[PreferredLanguage], [u].[PreferredCalendar], [u].[PreferredTimeZone], [u].[PreferredRegion]
FROM [core].[Users] AS [u] INNER JOIN @tb{b}_t0 AS [r] ON [r].[Id] = [u].[Id]
WHERE [u].[IsActive] = 1;
SELECT [p].[UserId], [p].[Channel], [p].[Enabled]
FROM [core].[NotificationPreferences] AS [p] INNER JOIN @tb{b}_t0 AS [r] ON [r].[Id] = [p].[UserId]
WHERE [p].[Type] = @tb{b}_p0;
```

An exception from a channel or from `PersistAsync` ends `ExecuteAsync` with nothing a channel
enlisted written, and spec 0020 §3.4 maps every item of the invocation: `Retry` under backoff, or
`Failed` at once for a `ValidationException`. A failed slice delivers nothing and leaves its inbox
rows as they are. Its `core.job.failed` reaches administrators (spec 0020 §10) in the inbox only,
because the handler declares `InboxOnlyFailureNotices`, so a failing delivery path never feeds
itself. Four slices hold at most 1,000 recipients (§9.1), which bounds the rows one completion
carries for a channel that writes one row per recipient, as `email` does.

## 5. The inbox

### 5.1 `InboxSeenAt`

Spec 0014's `core.UserStamps` carries `InboxSeenAt datetimeoffset(3) NULL`, written only by
`inbox/seen`. Unseen rows are those with `CreatedAt > InboxSeenAt` — every row when it is null.

### 5.2 Contracts

```csharp
// Tellma.Core.Abstractions.Notifications
public sealed record InboxSummary(
    int Unseen,                                         // capped at SummaryCountCap
    NotificationSeverity? MaxUnseenSeverity,            // over the same capped unseen rows; null when none
    int Unread,                                         // capped at SummaryCountCap
    IReadOnlyList<Notification> Latest,                 // at most SummaryLatestCount rows, newest first
    IReadOnlyList<InboxRunningJob> Running,             // at most SummaryLatestCount rows, newest first
    RelatedEntities Related);                           // spec 0012's related set: the core.User rows of Latest's actors (§5.3)

public sealed record InboxRunningJob(                   // the caller's own spec 0020 job: not terminal, not a cron occurrence
    int Id, string HandlerKey, JobStatus Status, byte? ProgressPercent, string? ProgressKey, string? ProgressArgumentsJson,
    DateTimeOffset? CancelRequestedAt, DateTimeOffset? StartedAt, DateTimeOffset CreatedAt,
    string? LabelKey, string? LabelArgumentsJson,       // the job's label (spec 0020 §2.3); null = the handler's label
    string? ErrorCode);                                 // why a Held job waits (spec 0020 §2.1)

// Tellma.Core.Notifications (runtime)
public sealed class InboxService                        // [ApiRoute("inbox")]; every action member-only
{
    public Task<InboxSummary> SummaryAsync();
    public Task<AffectedResult> SeenAsync();
    public Task<AffectedResult> ReadAsync(IdsRequest request);
    public Task<AffectedResult> ReadAllAsync();
    public Task<AffectedResult> ClearAsync(IdsRequest request);
    public Task<AffectedResult> ClearReadAsync();
}
```

| Member | Annotation | Batch |
|---|---|---|
| `Summary` | `[ApiAction("summary", MemberOnly = true, Idempotent = true, Mutation = false)]`; `NoActivityStampMetadata` (spec 0014; applied by spec 0016's projection) | one `Read` batch through spec 0014's `IGuardedBatchRunner.Run(Read, …)`: the summary statement, then the actors' `Query<User>` (§5.3); `Latest` and `Running` rows materialised by name, the actors into `Related` |
| `Seen` | `[ApiAction("seen", MemberOnly = true, Idempotent = true)]` | one `Persist` batch: the seen statement; `Count` = 1 |
| `Read` | `[ApiAction("read", MemberOnly = true, Idempotent = true)]`; `IdsRequest` (spec 0016 §3.3) | one `Persist` batch: the read statement with `@tb{b}_t0 : IdList`; `Count` = rows newly marked |
| `ReadAll` | `[ApiAction("read-all", MemberOnly = true, Idempotent = true)]` | one `Persist` batch: the read-all statement; `Count` = rows newly marked |
| `Clear` | `[ApiAction("clear", MemberOnly = true, Idempotent = true, Destructive = true)]`; `IdsRequest` | one `Persist` batch: the clear statement with `@tb{b}_t0 : IdList`; `Count` = rows deleted |
| `ClearRead` | `[ApiAction("clear-read", MemberOnly = true, Idempotent = true, Destructive = true)]` | one `Persist` batch: the clear-read statement; `Count` = rows deleted |

Each of `Seen`, `Read`, `ReadAll`, `Clear` and `ClearRead` publishes
`ClientEvent("inbox.changed", [callerId], null)` through `IClientEventPublisher.Publish(batch, …)`
so the user's other tabs and devices refresh. Ids in `Read` or `Clear` that belong to another user,
and ids in `Read` that are already read, are ignored — the predicate is the authorization; no 404 or
403 is raised for an id outside the caller's rows.

### 5.3 Statements

```sql
-- inbox/summary (Read; @tb{b}_p0 = SummaryCountCap, @tb{b}_p1 = SummaryLatestCount; ResultSets = 3; @tb{b}_actors feeds the actors' Query<User>)
DECLARE @tb{b}_seenAt datetimeoffset(3) = (SELECT [InboxSeenAt] FROM [core].[UserStamps] WHERE [UserId] = @tm_UserId);
SELECT [a].[Unseen], [a].[MaxUnseenSeverity],
  (SELECT COUNT(*) FROM (SELECT TOP (@tb{b}_p0) 1 AS [x] FROM [core].[Notifications]
                         WHERE [UserId] = @tm_UserId AND [ReadAt] IS NULL) AS [b]) AS [Unread]
FROM (SELECT COUNT(*) AS [Unseen],
             MAX(CASE [Severity] WHEN 'Error' THEN 3 WHEN 'Warning' THEN 2 WHEN 'Success' THEN 1 ELSE 0 END) AS [MaxUnseenSeverity]
      FROM (SELECT TOP (@tb{b}_p0) [Severity] FROM [core].[Notifications]
            WHERE [UserId] = @tm_UserId AND [CreatedAt] > COALESCE(@tb{b}_seenAt, '0001-01-01')
            ORDER BY [CreatedAt] DESC, [Id] DESC) AS [u]) AS [a];
DECLARE @tb{b}_latest TABLE ([Id] int NOT NULL PRIMARY KEY, [ActorUserId] int NULL);
INSERT INTO @tb{b}_latest ([Id], [ActorUserId])
SELECT TOP (@tb{b}_p1) [Id], [ActorUserId] FROM [core].[Notifications] WHERE [UserId] = @tm_UserId ORDER BY [CreatedAt] DESC, [Id] DESC;
SELECT [n].[Id], [n].[UserId], [n].[Type], [n].[Severity], [n].[ArgumentsJson], [n].[TargetResource], [n].[TargetId], [n].[ActorUserId], [n].[DedupKey], [n].[CreatedAt], [n].[ReadAt]
FROM [core].[Notifications] AS [n] INNER JOIN @tb{b}_latest AS [l] ON [l].[Id] = [n].[Id]
ORDER BY [n].[CreatedAt] DESC, [n].[Id] DESC;
DECLARE @tb{b}_actors TABLE ([Id] int NOT NULL PRIMARY KEY);
INSERT INTO @tb{b}_actors ([Id]) SELECT DISTINCT [ActorUserId] FROM @tb{b}_latest WHERE [ActorUserId] IS NOT NULL;
SELECT TOP (@tb{b}_p1) [Id], [HandlerKey], [Status], [ProgressPercent], [ProgressKey], [ProgressArgumentsJson], [CancelRequestedAt], [StartedAt], [CreatedAt],
       [LabelKey], [LabelArgumentsJson], [ErrorCode]
FROM [core].[Jobs]
WHERE [RequestedById] = @tm_UserId AND [RequestedById] IS NOT NULL AND [Status] IN ('Pending', 'Running', 'Held') AND [ScheduledFor] IS NULL
ORDER BY [CreatedAt] DESC, [Id] DESC;

-- inbox/seen (Persist; Writes = core.UserStamps)
UPDATE [core].[UserStamps] SET [InboxSeenAt] = SYSUTCDATETIME() WHERE [UserId] = @tm_UserId;

-- inbox/read (Persist; Writes = core.Notifications; @tb{b}_t0 : IdList; ResultSets = 1)
UPDATE [core].[Notifications] SET [ReadAt] = SYSUTCDATETIME()
WHERE [UserId] = @tm_UserId AND [ReadAt] IS NULL AND [Id] IN (SELECT [Id] FROM @tb{b}_t0);
SELECT @@ROWCOUNT AS [Count];

-- inbox/read-all (Persist; Writes = core.Notifications; ResultSets = 1)
UPDATE [core].[Notifications] SET [ReadAt] = SYSUTCDATETIME() WHERE [UserId] = @tm_UserId AND [ReadAt] IS NULL;
SELECT @@ROWCOUNT AS [Count];

-- inbox/clear (Persist; Writes = core.Notifications; @tb{b}_t0 : IdList; ResultSets = 1)
DELETE FROM [core].[Notifications] WHERE [UserId] = @tm_UserId AND [Id] IN (SELECT [Id] FROM @tb{b}_t0);
SELECT @@ROWCOUNT AS [Count];

-- inbox/clear-read (Persist; Writes = core.Notifications; ResultSets = 1)
DELETE FROM [core].[Notifications] WHERE [UserId] = @tm_UserId AND [ReadAt] IS NOT NULL;
SELECT @@ROWCOUNT AS [Count];
```

The counts and the maximum are `O(SummaryCountCap)` seeks on the two user indexes and the running
jobs are one seek on spec 0020's `IX_Jobs_RequestedActive`; the badge shows `99+` past the cap and
never a maintained number. `MaxUnseenSeverity` is the `CASE` rank mapped back to
`NotificationSeverity`, null when nothing is unseen. `Latest` and `Running` return raw rows; the
client renders them (§2.4). `clear-read` deletes a user's read rows in one statement; the
read-retention window (§7) bounds them. The seen statement writes the sibling row with no `Stamp`
semantics — `core.UserStamps` is bookkeeping, outside every version tag and every audit column.

The actors reach `Related` through spec 0012's `Query<User>`, appended to the same batch after the
statement:

- **Restriction.** `KeySetRestriction("Id", "@tb{b}_actors")`, so it reads exactly the actors of
  `Latest`, and nothing when every row is the system's.
- **Projection.** `Select` is the `core.User` stack's `RelatedSelect` (spec 0015 §2.2): `Id`, the
  display-name columns and the avatar, as the Notifications page shows them (§2.2).
- **No row filter.** An actor's display projection is part of the notice the caller may read, as
  spec 0015 treats the related entities of a details read.

### 5.4 The client protocol

The SPA calls the routes below. The bell's badge, panel, toasts and pages that make the calls are
spec 0028's (§5–§10).

- **`inbox/summary`** from the tenant's leader tab (spec 0025 §7): when elected, on every hub
  (re)connect, on every `inbox.changed` and `job.changed`, and on each degraded-mode poll. The
  leader stores the result as the tenant's `inbox` document, from which every tab renders (spec
  0025 §8.6).
- **`inbox/seen`** when the bell's panel opens.
- **`inbox/read`** with the ids of the rows the user marks read. A row opens through spec 0028
  §10.2's open route, which marks it read through `inbox/read`.
- **`inbox/clear`** with the ids of the rows the user clears.
- **`inbox/read-all`** and **`inbox/clear-read`** for the user's whole inbox.
- **`notification-preferences/get`** and **`save`** (§4.3) behind the Notification settings page.

## 6. The hub

### 6.1 `TellmaHub` and the connect

`TellmaHub` (`Tellma.Core.AspNetCore.Realtime`) is one SignalR hub class mapped by spec 0016's
`MapTellma` at `/{tenantId:int:min(1)}/hub` on spec 0011's `Hub` route group, with:

- **The session cookie.** The session-cookie scheme (`TellmaPolicies.Web`),
  `MemberEndpointMetadata` and `TenantEndpointMetadata(Hub, IsMutation = false)`.
- **Same origin only.** The `Origin`/`Sec-Fetch-Site` check, with a cross-origin negotiate refused
  outright.
- **No activity.** `NoActivityStampMetadata` (spec 0014): neither the negotiate nor the connect
  stamps `LastActiveAt`. Spec 0011 §5.5's background rule applies too: no `Hub`-group request
  renews the session or profile cookie. A leader tab's reconnects are not user activity.
- **No `Tellma-Client` header**, because a browser cannot decorate the WebSocket handshake.

The negotiate and the connect request pass spec 0011's tenant middleware and
`ITenantAccessGuard.EnsureAccessAsync` like any read:

- the initializers run, and spec 0014's connect step resolves the subject to a user id (the cold
  path pays its prologue-only round trip);
- non-members and deactivated users are refused with `TenantNotFoundException` (404);
- a `ReadOnly` tenant accepts the connection, because the hub is not a mutation;
- `Suspended`, `Provisioning` and `Retired` tenants refuse it with the verdicts of spec 0011.

`OnConnectedAsync` reads `IRequestContextAccessor.Current` (`Tenant.Id`, `UserId`, `Subject`,
`SessionHandle`, the handle of the session's `catalog.Sessions` row of spec 0011 §7.4), adds
the connection to its groups (§6.2), records it in `HubConnectionTracker`, and counts
`tellma.realtime.connects{outcome = connected}`. `OnDisconnectedAsync` removes it. The hub exposes
no client-invocable methods — every invocation is refused with a `HubException` and counted as
`tellma.realtime.connects{outcome = invocation-refused}`; the channel is server→client only.
`TellmaUserIdProvider : IUserIdProvider` returns the identity `sub`, so SignalR's per-user
addressing (and Azure SignalR's user-scoped operations) names the same principal spec 0003 does.
Hub options: `KeepAliveInterval` and `ClientTimeoutInterval` from `TellmaRealtimeOptions` (§9.2),
`MaximumReceiveMessageSize = 4096` (clients send nothing but protocol frames), JSON protocol only,
`EnableDetailedErrors = false`.

### 6.2 Groups

Group membership is computed from the authenticated context, never from client input. Groups are
not a security boundary in SignalR; they are one here because only the server assigns them and the
server assigns them from spec 0014's connect result.

| Group | Members | Used by |
|---|---|---|
| `t{tenantId}` | every connection of the tenant | events with empty `UserIds`; tenant-state closes |
| `t{tenantId}.u{userId}` | the user's connections in this tenant | every user-addressed event |
| `t{tenantId}.s{subject}` | the subject's connections in this tenant | `TenantAccessRevokedAsync(tenantId, subject)` |
| `x{handle}` | the connections of one BFF session, across tenants (`handle` = `RequestContext.SessionHandle`, spec 0011 §7.4) | `SessionsTerminatedAsync(subject, sessionHandles)` |

A subject open in two tenants holds two connections in two `t{…}` families and never receives the
other tenant's events; `Clients.User(sub)` is never used for events for exactly that reason.

### 6.3 The client event catalogue

```csharp
// Tellma.Core.Abstractions.Realtime
public sealed record ClientEvent(string Name, IReadOnlyList<int> UserIds, object? Payload);   // empty UserIds = every connected user of the tenant

public sealed record ClientEventEnvelope(string Name, int TenantId, object? Payload);   // the wire shape; client method "event"

public interface IClientEventRegistry                   // singleton; built at composition
{
    IReadOnlyList<string> Names { get; }
    bool IsRegistered(string name);
}
```

Events are registered through spec 0011's `FeatureContribution.ClientEvent(name)`
(`ClientEventContributionItem`), each once, by the feature code that publishes it: spec 0020 for
`job.changed`, spec 0013 for `cache.changed`, spec 0028 for `assignments.changed`, and this spec for
the other two. The name grammar is the type-key grammar of §2.3. Publishing an unregistered name is
`InvalidOperationException`. The Core catalogue:

| Event | Payload | Publisher | Recipients |
|---|---|---|---|
| `inbox.changed` | none | `INotifier` (§3.4), the inbox verbs (§5.2) | the affected users |
| `job.changed` | `{ jobId }` | spec 0020 (claim, progress-carrying renewal, the immediate renewal after a jump in percent, `Append`, completion, the job actions) | the job's `RequestedById` |
| `cache.changed` | `{ tag, value? }` | spec 0013 §2.5's epilogue contributor, per bumped tag | the tenant group for tenant-level tags; the affected users for user-level tags (spec 0013 §2.5) |
| `assignments.changed` | none | spec 0028's `IAssignments` and `assignments/seen` | the parties of the change (spec 0028 §4.5) |
| `session.ended` | `{ reason }` | the hub's listeners (§6.5) | the closing connections |

`session.ended` reasons: `session-terminated`, `access-revoked`, `tenant-unavailable`. Payloads
are ids and short codes only; a payload whose serialisation exceeds `MaxEventPayloadBytes` (4,096)
is refused with `InvalidOperationException` — a thin event that grew is a design error, not a
runtime condition. Both refusals are thrown to the caller synchronously by `Publish` and
`PublishAsync` (§6.4), before any callback is registered or any delivery is attempted.

### 6.4 `IClientEventPublisher`

```csharp
// Tellma.Core.Abstractions.Realtime
public interface IClientEventPublisher                  // scoped
{
    void Publish(IDataBatch? batch, ClientEvent clientEvent);   // post-commit hook when a batch is given; immediate otherwise
    Task PublishAsync(ClientEvent clientEvent);
}
```

| Member | Meaning |
|---|---|
| `Publish` | Validates the name and the payload size synchronously (§6.3). With a batch: registers an `OnCommitted` callback that calls `PublishAsync`; several `Publish` calls on one batch coalesce into one callback that deduplicates `(Name, UserIds, Payload)` triples. Without a batch: `PublishAsync` fire-and-forget on the scope's `TaskScheduler`, delivery exceptions logged. |
| `PublishAsync` | Validates the name and the payload size synchronously (`InvalidOperationException`, §6.3), resolves the tenant from `RequestContext.Tenant` (a tenantless scope is `InvalidOperationException`), sends `ClientEventEnvelope` to `t{tenantId}.u{id}` per user id or to `t{tenantId}` when `UserIds` is empty, counts `tellma.realtime.events{event}`. Delivery failures are logged (`ClientEventFailed`, warning) and never thrown to the caller. |

Two implementations: `NullClientEventPublisher` (`Tellma.Core.Notifications`; applies the refusals
of §6.3, delivers and counts nothing, registered with `TryAdd`) and `SignalRClientEventPublisher`
(`Tellma.Core.AspNetCore.Realtime`; `IHubContext<TellmaHub>`), which `AddTellmaRealtime` (§6.6)
registers in place of the null one. A dedicated worker host therefore publishes nothing, and a web
host of the same deployment picks the event up on the client's next summary call at worst — events
are an optimisation, never a dependency (§1.3).

### 6.5 Listeners: sessions, revocation, tenant state

`HubConnectionTracker` (singleton) implements spec 0011's `ISessionTerminationListener` and
`ITenantStateListener` (restated members: `SessionsTerminatedAsync(subject, sessionHandles)`,
`TenantAccessRevokedAsync(tenantId, subject)`, `OnStateChangedAsync(tenant, previous)`), and maps
each connection this instance hosts to `(tenantId, userId, subject, handle, HubCallerContext)`; the
listener delivers handles (spec 0011 §7.4), so no session key is held, matched or logged here.

| Call | Event sent first | Then closed |
|---|---|---|
| `SessionsTerminatedAsync(subject, sessionHandles)` (back-channel logout, revocation; spec 0011) | `session.ended { session-terminated }` to each `x{handle}` | every connection in those groups |
| `TenantAccessRevokedAsync(tenantId, subject)` (spec 0018's `UserService` after a deactivation commits) | `session.ended { access-revoked }` to `t{tenantId}.s{subject}` | that group |
| `OnStateChangedAsync(tenant, previous)` with the new state `Suspended` or `Retired` | `session.ended { tenant-unavailable }` to `t{tenantId}` | that group |
| `OnStateChangedAsync` to `ReadOnly`, `Active` or `Provisioning` | nothing | nothing (reads stay allowed; a reconnect re-runs the verdicts) |

"Closed" is by hosting mode. Self-hosted (in-process or Redis): the tracker aborts every locally
hosted connection in the group (`HubCallerContext.Abort()`); with a Redis backplane each instance
receives the listener call from its own session store or registry and aborts its own connections,
so the union covers the deployment. Azure SignalR Service: connections live in the service, not
the instance; the tracker sends the event and relies on two facts — the client disconnects itself
on `session.ended`, and the service closes every connection whose access token has expired
(`CloseOnAuthenticationExpiration = true`, `AccessTokenLifetime` = 5 minutes, §9.2), after which
the reconnect's negotiate re-runs the verdicts and is refused. A revoked user on Azure can
therefore still receive thin events for at most five minutes (review flag 5). The BFF cookie is
never ended by a tenant deactivation (spec 0011) — the user may belong to another tenant, and that
tenant's connection stays up.

### 6.6 Hosting modes

`services.AddTellmaRealtime(Action<ISignalRServerBuilder>? configure = null)`
(`Tellma.Core.AspNetCore`) registers the hub, the user id provider, the tracker, the publisher and
`TellmaRealtimeOptions`; `MapTellma` maps the hub. `AddTellmaAspNetCore` — inside `AddTellma`, after
`compose` and `composeWeb` — calls `AddTellmaRealtime()` with no delegate, which never replaces a
delegate a composer registered earlier; a host selects a mode by calling
`AddTellmaRealtime(configure)` on `TellmaBuilder.Services` from the web host's `composeWeb` delegate
(spec 0011 §5). The delegate receives the framework's `ISignalRServerBuilder` after
`AddSignalR(hubOptions)` has run, so the composing host's own package references decide the mode
without `Tellma.Core.AspNetCore` referencing either package:

| Mode | When | Requirements |
|---|---|---|
| In-process | default; one instance | nothing |
| Redis backplane | on-premises, two or more instances | `AddStackExchangeRedis(connectionString, o => o.Configuration.ChannelPrefix = RedisChannel.Literal(prefix))`; sticky sessions at the load balancer; `prefix` = `DeploymentIdentity.DeploymentId` (spec 0007) so two deployments sharing a Redis never cross |
| Azure SignalR Service | SaaS; `Azure:SignalR:ConnectionString` present | `AddAzureSignalR(o => { o.ApplicationName = RealtimeApplicationName.From(deployment); o.ClaimsProvider = c => [sub]; o.AccessTokenLifetime = …; o.CloseOnAuthenticationExpiration = true; })`; the hub-only token carries `sub` and nothing else (spec 0003 §7.4); the application name isolates the deployment on a shared service instance (below) |

*Illustration* — the realtime line of `UseAzureWebDefaults()` (`Tellma.Defaults.Azure.AspNetCore`):

```csharp
if (tellma.Configuration["Azure:SignalR:ConnectionString"] is not null)
{
    var realtime = tellma.Configuration.GetSection("Tellma:Realtime").Get<TellmaRealtimeOptions>() ?? new();
    tellma.Services.AddTellmaRealtime(s => s.AddAzureSignalR(o =>
    {
        o.ApplicationName = RealtimeApplicationName.From(deployment);   // "etpharma_staging"
        o.ClaimsProvider = c => [c.FindFirst("sub")!];
        o.AccessTokenLifetime = realtime.AccessTokenLifetime;           // the revocation bound of §6.5
        o.CloseOnAuthenticationExpiration = true;
    }));
}
```

**Sharing a service instance.** `RealtimeApplicationName.From(DeploymentIdentity)`
(`Tellma.Core.AspNetCore.Realtime`) is the deployment id (spec 0007) with every `-` replaced by
`_` — `etpharma-staging` → `etpharma_staging` — because the service accepts an application name
that starts with a letter and holds letters, digits and underscores only. The SDK prefixes every
hub name with it, so several deployments share one Azure SignalR instance without their hubs,
groups or server connections ever meeting; without it the service treats every app server that
maps `TellmaHub` as one application, and a client that negotiated with one deployment could be
served by another. What a shared instance pools is its units and its access key or identity, so an
instance is shared only among the deployments one operator runs, and one environment never shares
with another: a production instance serves the production deployments of a region, a staging
instance the staging ones. A distribution hosted by a third party gets its own instance.

Startup fails (spec 0011's realised gate, check `realtime-hosting`) when
`Azure:SignalR:ConnectionString` is configured and no delegate selected Azure SignalR, or when Azure
SignalR is selected with an `ApplicationName` other than the deployment's,
`CloseOnAuthenticationExpiration` false, or an `AccessTokenLifetime` other than
`TellmaRealtimeOptions.AccessTokenLifetime` — the check locates the SDK's `ServiceOptions` by type
name, as it does to detect the mode — and warns when the host reports more than one instance
(`WEBSITE_INSTANCE_ID` present without Azure SignalR, or `Tellma:Realtime:ExpectedInstances > 1`)
and no backplane is configured. Redis is required only for on-premises multi-instance deployments;
single-instance on-premises and SaaS need none.

## 7. Retention

`NotificationRetentionHandler` (`Tellma.Core.Notifications`) is an arguments-only `IJobHandler`
with `[JobHandler("core.notification-retention", LeaseSeconds = 600)]` (spec 0020's attribute),
registered through `FeatureContribution.JobHandler<NotificationRetentionHandler>()` and scheduled by
`FeatureContribution.BuiltInSchedule(2, "core.notification-retention", "30 3 * * *")` — the schedule
row lives in spec 0020's reserved band, runs as the system user, and fires after
`core.job-retention`. Each page is its own `Maintenance` round trip, `Idempotent = true`
(`@tb{b}_p0` = `RetentionPageSize`, `@tb{b}_p1` = `ReadRetention` in seconds, `@tb{b}_p2` =
`UnreadRetention` in seconds; `ResultSets = 1`), looping per band until a page is short and
reporting progress per page through spec 0020's `IJobProgress.Report`:

```sql
DELETE TOP (@tb{b}_p0) FROM [core].[Notifications]
WHERE [ReadAt] IS NOT NULL AND [ReadAt] < DATEADD(second, -@tb{b}_p1, SYSUTCDATETIME());
SELECT @@ROWCOUNT AS [Deleted];
-- second band, same loop:
DELETE TOP (@tb{b}_p0) FROM [core].[Notifications]
WHERE [ReadAt] IS NULL AND [CreatedAt] < DATEADD(second, -@tb{b}_p2, SYSUTCDATETIME());
SELECT @@ROWCOUNT AS [Deleted];
```

Cut-offs are computed from the database clock inside the statement (spec 0020's clock rule).
Deleted rows count on `tellma.notifications.retained{band = read | unread}`. Retention never reads
preferences or types: a row of a removed type ages out like any other.

## 8. Securables, endpoints, pages

| Resource × action | Registered by | Filter root | Who holds it |
|---|---|---|---|
| `core.Notification × Read` | the stack feature's contributor (spec 0015), from `[Stack(Operations = Query \| Details)]` | `core.Notification` | every member through the self-scope criterion (§2.2); a stored grant widens |

`InboxService` and `NotificationPreferencesService` are `[ApiRoute]` services whose actions are all
`MemberOnly`: any connected active member, no securable; `contribution.ApiService<T>()` registers
them and the realised gate audits them like every endpoint (spec 0016). No `Save`, `Delete`,
`Activate` or Excel securable exists for notifications; none is sensitive.

- **Endpoints.** Spec 0016's projection maps these as POST under `/{tenantId}/api/web/`:
  `notifications/query`, `notifications/get`, `notifications/get-by-ids`; `inbox/summary`,
  `inbox/seen`, `inbox/read`, `inbox/read-all`, `inbox/clear`, `inbox/clear-read`;
  `notification-preferences/get` and `notification-preferences/save`.
- **The hub** sits beside them at `/{tenantId}/hub`. The catalogue has no route (§2.5).
- **Pages** are spec 0028's: the bell panel, and the Notifications and Notification settings pages.
- **MCP.** `tellma_query`/`tellma_get` over `core.Notification` work under the self-scope;
  `tellma_notifications` remains reserved.

## 9. Configuration

### 9.1 `Tellma:Notifications`

`NotificationsOptions` (§3.1), bound and validated at startup by spec 0011's `Options<T>`
declaration: every count at least 1; `SummaryCountCap` at most 1,000; `SummaryLatestCount` at most
50; `MaxArgumentsChars` at most 4,000 (the column width); `RetentionPageSize` at most 10,000;
`DeliverySliceSize` at most 250 (§4.5).

### 9.2 `Tellma:Realtime`

```csharp
// Tellma.Core.AspNetCore.Realtime
public sealed class TellmaRealtimeOptions
{
    public bool Enabled { get; set; } = true;                                         // false: no hub is mapped; the null publisher stays
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan ClientTimeoutInterval { get; set; } = TimeSpan.FromSeconds(150);  // at least twice KeepAliveInterval
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);      // Azure SignalR only; the revocation bound of §6.5
    public int MaxEventPayloadBytes { get; set; } = 4096;
    public int ExpectedInstances { get; set; } = 1;                                   // the backplane warning of §6.6
}
```

The 150 s `ClientTimeoutInterval` covers a hidden leader tab, whose keep-alive timer the browser
may throttle to once a minute (spec 0025 §8.2).

`Azure:SignalR:ConnectionString` stays where spec 0011 places it (a host secret outside the
`Tellma:` tree); the Redis connection string for the backplane is the on-premises host's own
configuration, passed to its delegate.

## 10. Telemetry and logging

### 10.1 Instruments

```csharp
// Tellma.Core.Abstractions.Notifications
public static class NotificationsTelemetryNames         // constants
{
    public const string MeterName = "Tellma.Core";
    public const string Created = "tellma.notifications.created";
    public const string Suppressed = "tellma.notifications.suppressed";
    public const string Replaced = "tellma.notifications.replaced";
    public const string Withdrawn = "tellma.notifications.withdrawn";
    public const string DeliveryEnqueued = "tellma.notifications.delivery_enqueued";
    public const string Retained = "tellma.notifications.retained";
    public const string Summaries = "tellma.notifications.inbox.summaries";
    public const string MarkedRead = "tellma.notifications.inbox.read";
    public const string Cleared = "tellma.notifications.inbox.cleared";
    public const string PreferencesSaved = "tellma.notifications.preferences.saved";
    public const string TypeTag = "type";
    public const string BandTag = "band";
    public const string VerbTag = "verb";
}

// Tellma.Core.Abstractions.Realtime
public static class RealtimeTelemetryNames              // constants; Events is spec 0016's ApiTelemetryNames.RealtimeEvents ("tellma.realtime.events")
{
    public const string MeterName = "Tellma.Core.AspNetCore";
    public const string Connections = "tellma.realtime.connections";
    public const string Connects = "tellma.realtime.connects";
    public const string Closes = "tellma.realtime.closes";
    public const string OutcomeTag = "outcome";
    public const string ReasonTag = "reason";
    public const string ModeTag = "mode";
}
```

| Instrument | Kind, unit | Tags | Answers |
|---|---|---|---|
| `tellma.notifications.created` | counter, `{notification}` | `type` | fan-out volume per type |
| `tellma.notifications.suppressed` | counter, `{notification}` | `type` | recipients filtered by inactivity, mute or a duplicate under `Suppress` or `Ongoing` |
| `tellma.notifications.replaced` | counter, `{notification}` | `type` | unread duplicates refreshed in place |
| `tellma.notifications.withdrawn` | counter, `{notification}` | `type` | rows deleted by a withdrawal |
| `tellma.notifications.delivery_enqueued` | counter, `{job}` | `type` | delivery jobs inserted, one per qualifying slice (§3.3) |
| `tellma.notifications.retained` | counter, `{notification}` | `band ∈ read \| unread` | rows deleted by retention |
| `tellma.notifications.inbox.summaries` | counter, `{request}` | — | polling pressure |
| `tellma.notifications.inbox.read` | counter, `{notification}` | `verb ∈ read \| read-all` | rows marked read |
| `tellma.notifications.inbox.cleared` | counter, `{notification}` | `verb ∈ clear \| clear-read` | rows the user deleted |
| `tellma.notifications.preferences.saved` | counter, `{request}` | — | preference churn |
| `tellma.realtime.events` (spec 0016) | counter, `{event}` | `event` | push volume |
| `tellma.realtime.connections` | up-down counter, `{connection}` | `mode ∈ in-process \| redis \| azure` | open connections on this instance |
| `tellma.realtime.connects` | counter, `{attempt}` | `outcome ∈ connected \| refused \| invocation-refused` | connect churn and refusals |
| `tellma.realtime.closes` | counter, `{connection}` | `reason ∈ session-terminated \| access-revoked \| tenant-unavailable` | forced closes |

The tenant and the user are never tags. Alert queries under `infra/monitoring/`: `suppressed`
rising against `created` for one type (a mute wave or a dedup storm), `connects{refused}` per
minute (a stale SPA or an attack), and `closes{access-revoked}` with no matching `session.ended`
delivery on Azure.

### 10.2 Log events

Structured events, tenant and user ids in the log scope: `NotificationsInserted` (debug; type,
requested, inserted, delivery jobs), `NotificationsWithdrawn` (debug; key, deleted),
`InboxEventFailed` (warning), `ClientEventFailed` (warning; event name; a delivery failure, §6.4),
`NotificationArgumentsInvalid` (warning; type), `HubConnected`/`HubDisconnected` (debug),
`HubConnectionRefused` (information; reason), `HubConnectionsClosed` (information; group, count,
reason), `RealtimeHostingWarning` (warning; the backplane check of §6.6).

## 11. Composition and startup checks

`CoreFeature.Contribute` adds: `Entity<Notification>()` (the default `EntityService<Notification>`;
no service subclass exists), `ApiService<InboxService>()`,
`ApiService<NotificationPreferencesService>()`, `Scoped<INotifier, Notifier>()`,
`Scoped<INotificationRenderer, NotificationRenderer>()`,
`Singleton<INotificationTypeRegistry, NotificationTypeRegistry>()`,
`Singleton<INotificationChannelRegistry, NotificationChannelRegistry>()`,
`Singleton<IClientEventRegistry, ClientEventRegistry>()`,
`NotificationsClientAssetContributor` as spec 0024 §3.2's `IClientAssetContributor` (§2.5),
`Singleton<IAccessCriteriaProvider, NotificationAccessCriteria>()`,
`NotificationType(core.user.added)`, `NotificationChannel(inbox)`, `NotificationChannel(email)`,
`ClientEvent("inbox.changed")`, `ClientEvent("session.ended")`,
`JobHandler<NotificationDeliveryHandler>()`, `JobHandler<NotificationRetentionHandler>()`,
`BuiltInSchedule(2, "core.notification-retention", "30 3 * * *")`, and the three standalone table
types on the model. `IClientEventPublisher` is registered with `TryAdd` as the null publisher.

`IStartupCheck`s reporting into spec 0011's realised gate: `notification-types` (every key and
category matches its grammar; keys unique; every non-null `TargetResource` is a registered securable
resource or stack resource, so the client can route it; a channel-only type satisfies §2.3's `Inbox`
rule); `notification-channels` (grammar; keys unique; `inbox` and `email` registered; every
`INotificationChannel` names a registered channel other than `inbox`, at most one per channel);
`client-events` (grammar, unique); `notification-options` (§9.1 bounds); `realtime-hosting` (§6.6,
`Tellma.Core.AspNetCore` only). A failed check names the feature that contributed the offending
item.

## 12. Testing

| Suite | Location | Traits | Pins |
|---|---|---|---|
| Notifications unit | `test/core/Tellma.Core.Tests/Notifications/` | none | request validation (§3.1 table, every `InvalidOperationException` path: `WithdrawRequest`, `Ongoing` without a key, `InboxOnly` or a key on a channel-only type among them), recipient dedup and `Ordinal` assignment, a channel-only request composing the delivery enqueue alone, slicing (`FirstOrdinal`, `LastOrdinal` and one `ArgumentsJson` per `DeliverySliceSize` recipients), the delivery condition built from the channels with a sender and omitted for `InboxOnly` or when none has one, the renderer (ICU arguments by JSON kind, the resource in the given context's culture and dates in its display zone and calendar, missing resource → key, malformed JSON → no arguments), the delivery handler's resolution with fake channels (a preference row over `DefaultEnabled`, channels in registration order, each recipient's negotiated `LocalizationContext`, one `PersistAsync`, nothing persisted when a channel throws), `NotificationsOptions` bounds, the registries' grammar and duplicate rejection, the three client metadata sections written from them in registration order (§2.5), the renderer's detail line present and absent, the null publisher, the `OnCommitted` coalescing of §3.4 and §6.4 with a fake batch, one callback per batch across `Notify` and `Withdraw` calls |
| Notifications fixture | `test/core/Tellma.Core.IntegrationTests/Notifications/` | `Category=Integration` (LocalDB, RCSI on and off) | the insert: ids contiguous from one range, inactive recipient skipped, muted mutable type skipped, non-mutable type ignores a disabled `inbox` row, the severity from the descriptor and from the request's override, a duplicate suppressed against an unread row and not against a read one, `Ongoing` suppressed against a read row too, `Replace` refreshing the unread duplicate in place (same id; type, severity, arguments, target, actor and `CreatedAt` from the request; both result sets read) and inserting for the rest, a withdrawal for everyone and for named recipients deleting read and unread rows and publishing `inbox.changed` to exactly the withdrawn users, a withdrawal of an absent key deleting nothing, unknown recipient skipped, `OUTPUT` matches inserted rows; the delivery enqueue with a fake channel registered: no job when no active recipient enabled a channel, one `core.notification-delivery` row per qualifying slice (system run-as user, no requester, the slice's arguments, the trace parent) and none for the others, a job for every slice with an active recipient under a `DefaultEnabled = true` channel, no job and no `core.sq_Jobs` range for `InboxOnly` or when nothing qualifies, the job ids returned and the poller nudged after commit; the delivery job end to end through spec 0020's worker: one `Read` round trip per invocation, an inactive and a non-opted-in recipient skipped, the fake channel's enlisted rows committed with the completion, and a final failure raising `core.job.failed` to administrators inbox only; the summary caps at `SummaryCountCap`, orders `Latest`, takes `MaxUnseenSeverity` over the capped newest unseen rows and null when none, fills `Related` with exactly the actors of `Latest` (none for system rows), and lists in `Running` the caller's own `Pending`, `Running` and `Held` rows with `ScheduledFor` null only, capped and newest first, with their label and error columns; `seen`/`read`/`read-all`/`clear`/`clear-read` counts (another user's id ignored) and the `inbox.changed` publish; preferences `save` synchronises, drops default rows, bumps `PreferencesTag` and refuses `CannotMute`/`UnknownType`/`UnknownChannel`/`DuplicatePreference`; an `email` deviation stored and never read by the inbox insert; the self-scope filter on `query` and `get` (another user's id is 404); retention bands and paging; spec 0013's change-tracking write audit sees exactly the declared tables |
| Realtime unit | `test/core/Tellma.Core.AspNetCore.Tests/Realtime/` | none | `TestServer` with the in-process mode: connect succeeds for a member and is refused for a non-member and a deactivated user; the negotiate and the connect stamp no `LastActiveAt` and renew no cookie; groups assigned from the context; an event to a user reaches that user's connections only and never a second tenant's; empty `UserIds` reaches the tenant group; payload cap; unregistered name refused; the tracker aborts by session handle, by `(tenant, subject)` and by tenant group and sends `session.ended` first; `ReadOnly` connects, `Suspended` refuses; client invocations refused; instruments asserted |
| Realtime live | `test/core/Tellma.Core.AspNetCore.IntegrationTests/Realtime/` | `Category=Integration`, `Live=true` | against a real Azure SignalR instance (`Azure:SignalR:ConnectionString` from the CI secret store): connect through the service, group delivery, `session.ended` delivery, token expiry closing the connection within `AccessTokenLifetime`; two hosts with different application names on the one instance never deliver to each other's `t{tenantId}` group |

PR runs the two unit suites and the LocalDB fixture suite; the live suite runs nightly and on
demand. Fixture entities: the shared fixture tenant of spec 0012's fixture tier with two users, one
inactive user, and a `fixture.Documents` row to target; the hub tests host `CoreFeature` alone.

## 13. Definition of done

- **Projects**: the `Tellma.Core.Abstractions.Notifications`/`.Realtime` namespaces, the
  `Tellma.Core.Notifications` and `Tellma.Core.AspNetCore.Realtime` runtime namespaces, the four
  test folders, and the realtime composition inside `UseAzureWebDefaults()`
  (`Tellma.Defaults.Azure.AspNetCore`) with its `Microsoft.Azure.SignalR` pin — each project with a
  README, XML docs on every member, building and testing on Windows and Linux under
  warnings-as-errors, wired into `Tellma.slnx`.
- **Behavior**: §2 table, entity, stack, self-scope, registry, catalogue and renderer; §3
  `INotifier`, the insert, the replace, the withdrawal and the delivery enqueue; §4 channels,
  preferences and the delivery handler; §5 the inbox; §6 the hub, groups, events, publisher,
  listeners and hosting modes; §7 retention; §8 securables and endpoints; §11 composition and checks
  — implemented and pinned by the suites of §12, green in CI.
- **Observability**: every instrument of §10.1 emitted and asserted; the log events of §10.2
  asserted in the unit suites; the alert queries present under `infra/monitoring/`.
- **CI**: unit and fixture suites on PR; the live realtime suite nightly with the Azure SignalR
  secret.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the "Library architecture — package
  naming and dependency rules" section (notifications are a namespace inside `Tellma.Core`; the hub
  and the SignalR publisher live in `Tellma.Core.AspNetCore`; the Azure SignalR pin lives in
  `Tellma.Defaults.Azure.AspNetCore` and the Redis pin in the on-premises host), the "Hosting on
  Azure" section (Redis required only for on-premises multi-instance SignalR; Azure SignalR Service
  with the hub-only token in SaaS), and the "Observability" section (meter `Tellma.Core` for
  `tellma.notifications.*`, `Tellma.Core.AspNetCore` for `tellma.realtime.*`). Public XML docs and
  error messages reference no `docs/` paths, per repo rule.
- **Not in scope of done**: the `email` channel's delivery (spec 0022's), push channels, the `Title`
  column, the cross-instance nudge, `tellma_notifications`, the descriptors declared by specs
  0019, 0020, 0022, 0033 and 0035, and the client surfaces of specs 0025 and 0028.

## Decisions record

The load-bearing decisions, where not already evident above:

1. **One row per recipient, language-neutral, rendered at read time** — no per-recipient lookup on
   insert and no frozen language (§1.3, §2.4).
2. **The insert rides the caller's batch with ids reserved inside the statement** — zero extra
   round trips and no dependency on the allocator's buffer (§3.3).
3. **Predicates filter inactive, muted and duplicate recipients** — declarative filtering in the
   statement; a save never fails for a notification reason (§3.3).
4. **Dedup is best effort** — a unique index would fail business saves over a notification race
   (§3.3).
5. **Non-mutable inbox channel for completion and failure types** — the export row remains the
   durable path; the notice cannot be silenced by accident (§2.3).
6. **Preferences store deviations only** — the insert's seek usually finds nothing; defaults live
   in code (§4.3).
7. **Capped counts, a capped maximum and one `InboxSeenAt`** — maintained counters drift; the
   badge's severity is read from the same capped rows; the sibling row absorbs the churn (§5.1,
   §5.3).
8. **The self-scope is a criterion, not a seeded grant** — every member reads their own rows with
   no role; a stored grant can widen for audit (§2.2).
9. **`Notification` is `Query | Details` only** — nothing is written through `Save`; the verbs
   are inbox actions (§2.2).
10. **One hub, four server-assigned groups** — tenant isolation without a hub per tenant;
    per-user, per-subject and per-session addressing for events and closes (§6.2).
11. **Publishers never see SignalR; the null publisher is the default** — worker hosts and tests
    compose without the web package (§6.4).
12. **The composing host (directly or through `UseAzureWebDefaults()`) selects the hosting mode by
    delegate** — `Tellma.Core.AspNetCore` references neither Azure SignalR nor Redis; on Azure the
    deployment's application name lets the deployments of one environment share one service
    instance (§6.6).
13. **Azure closes are event-plus-token-lifetime** — no Management SDK, a five-minute bound on a
    revoked connection that carries only ids (§6.5).
14. **`core.user.added` is this spec's descriptor; every other Core type is declared beside the
    code that raises it** — one registration per key, no cross-spec ordering (§2.3).
15. **Retention runs as a spec 0020 handler on the database clock** — read 90 days, unread 365,
    paged and idempotent (§7).
16. **A duplicate is suppressed or replaced in place, and a condition keeps one row** — `Replace`
    refreshes the unread row so the notice carries the latest facts without a second row; `Ongoing`
    keeps one row per condition, read or not; `Withdraw` deletes it for everyone when the condition
    clears (§3.1, §3.3).
17. **Channels are registered, not columns** — `inbox` and `email` are every distribution's
    baseline; a module or distribution adds its own behind `INotificationChannel`; the preference
    table holds one row per `(user, type, channel)` deviation (§4).
18. **Channels other than the inbox deliver from a `core.notification-delivery` job** — the notify
    statement enqueues one per qualifying slice, in SQL and in the caller's transaction, so a
    notification adds no round trip, recipients are filtered by predicates and a save never fails
    for a notification reason (principles 1 and 3, Decision 3); recipients, preferences and
    addresses are read when the job runs, and a channel's writes commit with its completion (§3.3,
    §4.5).
19. **Assigned work and announcements are not notifications** — a stored row about either duplicates
    its source and goes stale when the source moves on; assigned work is spec 0028's and
    announcements a later spec's (Context).
20. **Severity is the type's, overridable per request, and the badge shows one number** — unseen,
    coloured by the highest unseen severity; unread stays per row (§2.3, §5.3; the badge is spec
    0028 §6).
21. **Channel-only types skip the insert** — `Inbox = false` keeps the type registry and the channel
    preferences for a message that must never be a bell row (§2.3, §3.3).
22. **The user clears their own rows** — the bell is not an audit record; the document's history and
    the job row are (§5.2).
23. **The catalogue is emitted into the client metadata** — types, channels and client events change
    only with a deployment, so they ship with the bundle and need no route; preferences carry
    deviations alone (§2.5, §4.3).
24. **Running jobs and the latest rows' actors ride the summary** — one more result set over spec
    0020's index and one related read, so a bell row renders without a second call; no job counter
    and no shell button of its own (§5.3).
25. **`Ongoing` is for re-evaluated conditions** — a type raised once per occurrence keeps
    `Suppress` and is withdrawn when its cause ends; under `Ongoing` a missed withdrawal would
    silence the next occurrence for the retention window (§3.1).
26. **A key names a record or a type** — keys match without regard to `Type`, so activity keys by
    `<resource>:<id>` and everything else by its type key, and a withdrawal never removes another
    raiser's row (§3.1).
27. **The hub is not user activity** — the negotiate and the connect stamp no `LastActiveAt` and
    renew no cookie, so an open leader tab never keeps an idle session alive; the 150 s client
    timeout outlasts a hidden tab's throttled keep-alive (§6.1, §9.2).

## Review flags

1. **Read-time rendering** (§2.4): rows store keys and arguments; text is rendered in the reader's
   culture. Alternative: a `Title` column rendered in the tenant's primary language at creation,
   which makes the notifications page sortable and searchable on text. Flips if users demand text
   search over notifications or the string pack proves too heavy.
2. **Best-effort dedup, and `Replace` and `Ongoing` ride on it** (§3.3): a non-unique index and a
   predicate. Alternative: a unique filtered index on `(UserId, DedupKey) WHERE ReadAt IS NULL`,
   which fails the business save when two batches race and cannot cover `Ongoing`, whose read rows
   count. Flips if duplicate notices are observed at a rate that annoys users.
3. **No push channel yet** (§4.1): the spec that ships a transport registers it. That spec also
   owns the service-worker wrapper script that imports Angular's `ngsw-worker.js` and adds the
   `push` and `notificationclick` handlers (spec 0024 §5.4). Alternative: register `push` now with
   no sender, so the preference is collected early. Flips if the push spec is scheduled before the
   next release.
4. **Per-(tenant, user) groups on one hub** (§6.2): four groups per connection. Alternative: a hub
   per tenant with `Clients.User`. Flips only if Azure SignalR pricing or limits per hub change the
   arithmetic.
5. **Azure closes bounded by `AccessTokenLifetime`** (§6.5): no Management SDK, a five-minute
   window during which a revoked connection can receive ids-only events. Alternative: reference
   `Microsoft.Azure.SignalR.Management` from a small adapter package and close the group through
   the service's REST API immediately. Flips if a compliance reading treats "closed on session
   end" as immediate, or if the window is judged too long.
6. **`NotificationPreferences (UserId, Type, Channel, Enabled)`** (§4.2): one row per deviation,
   channels registered rather than declared as columns. Alternative: a column per channel, which
   makes every new channel a Core migration. Flips only if the seek's third key column shows up,
   which a three-column clustered key rules out.
7. **Capped counts instead of materialised counters** (§5.3): `TOP (100)` seeks per summary.
   Alternative: `InboxUnseenCount`/`InboxUnreadCount` on `UserStamps` bumped by every insert and
   read. Flips if the summary's two seeks show up under a tenant with heavy fan-out — unlikely at
   O(100) per call.
8. **System-written rows take ids inside their statements** (§3.3): `sp_sequence_get_range` per
   request, `Notify` synchronous. Alternative: an asynchronous chain through the allocator's
   buffer, saving a range call per request at the cost of an `await` in every hook. Flips if
   range calls show up in the persist round trip's duration.
9. **A stored grant on `core.Notification × Read` widens the self-scope** (§2.2): the securable
   exists, so spec 0014's composition honours a role grant. Alternative: a registry flag that marks
   a securable criteria-only and rejects stored grants on it. Flips if an audit requirement says
   nobody, administrators included, may read another user's notifications.
10. **The catalogue ships in the client metadata** (§2.5): one copy per build, like the string pack,
    which already names every type and channel. Alternative: return the descriptors from
    `notification-preferences/get`, or fold them into `settings/client` under the `settings` tag.
    Flips if a deployment ever registers types per tenant.
11. **`MaxRecipientsPerRequest = 10,000` as a programming-error limit** (§3.1): a violation is a
    500, never a 422; a withdrawal's named recipients share the limit. Alternative: split
    automatically inside `Notify`. Flips if a legitimate fan-out to every user of a large tenant is
    needed.
12. **`Notification` stack is `Query | Details` with no Excel operations** (§2.2). Alternative:
    include `Export` so a user can export their notifications. Flips on the first request for it.
13. **The composing host (directly or through `UseAzureWebDefaults()`) selects the hosting mode by
    delegate, and startup fails when the Azure connection string is present without it** (§6.6).
    Alternative: `Tellma.Core.AspNetCore` references `Microsoft.Azure.SignalR` and selects
    automatically. Flips if every host ends up writing the same two lines.
14. **One Azure SignalR instance per environment, shared by its deployments** (§6.6): isolation by
    application name, units pooled. Alternative: an instance per deployment. Flips if one
    deployment's connection volume needs its own unit scaling, or a hosted distribution must not
    share an access key.
15. **Channels have no dedup** (§4.4): the inbox's duplicate rules govern inbox rows only, so a user
    who enabled `email` for a `Suppress` type (`core.job.held`, `core.scheduler.gap`) is emailed on
    every raise, and would be on every evaluation of an `Ongoing` condition that still holds.
    Alternative: evaluate the delivery condition over the recipients whose inbox row was inserted or
    refreshed, which also ties every channel to the inbox mute and cannot serve a channel-only type.
    Flips if repeated notice emails annoy users, or before the first `Ongoing` type ships with a
    channel.
16. **`Severity` stored as text and ranked by a `CASE`** (§2.1, §5.3): spec 0012's enum convention,
    so the notifications page filters `Severity = 'Error'`. Alternative: `tinyint`, which `MAX`
    ranks natively. Flips only if the `CASE` over at most `SummaryCountCap` rows shows up in
    profiles.
17. **`Running` rides the summary, capped at `SummaryLatestCount`, refreshed on `job.changed`**
    (§5.3, §5.4): one summary per claim, progress-carrying renewal, `Append` and completion of a
    running job, in the leader tab only.
    Alternative: a `jobs/running` read of its own with its own cap and event. Flips if
    `inbox.summaries` rises with running jobs, or a user needs more than ten in progress.
