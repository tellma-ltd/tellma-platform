# Spec: Notifications, the Inbox, and the Hub

- **Author:** Ahmad Akra
- **Date:** 4 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every product built on the platform tells its users things: a document was assigned to you, the
export you asked for is ready, the import you ran failed on row 3,412, a schedule you own was
paused because its owner was deactivated. This spec ships the machinery that carries those messages
to the person: the `core.Notifications` table and its `Notification` entity, the per-type registry
that says which notifications exist and which can be muted, the channel registry (`inbox` and
`email` in every distribution; a module or distribution adds its own), the
`core.NotificationPreferences` rows that hold each user's choices per channel, `INotifier` — the
writer every service, handler and platform
feature calls — and the inbox that a user sees: two counters on the bell icon, a dropdown of the
latest items, a full notifications page, and the mark-as-read verbs.

It also ships the real-time channel that keeps those counters honest without polling: `TellmaHub`,
one SignalR hub at `/{tenantId}/hub`, connected under the session cookie exactly as spec 0003 §7.4
requires, carrying thin events (`inbox.changed`, `job.changed`, `cache.changed`, `session.ended`)
to per-(tenant, user) groups assigned by the server. Publishers never see SignalR: they call
`IClientEventPublisher`, which rides the same post-commit hook the notifier uses, so an event is
never observed for a transaction that did not commit.

The design rests on the frozen specs and the sibling specs written with it. Spec 0011's `IDataBatch`
is the only way any statement here reaches SQL Server: a notification insert is a fixed-text
statement appended to the caller's persist batch, so notifying a hundred assignees costs zero
additional round trips, and its ids are taken inside the statement rather than from the allocator's
buffer. Spec 0013 owns the user, the connect prologue whose `@tm_UserId` every inbox statement
reads, and the `core.UserStamps` sibling row whose `InboxSeenAt` column this spec adds and whose
`PreferencesTag` this spec's preference save bumps. Spec 0012 owns the ICU localizer under which
notification text is rendered at read time. Spec 0014 exposes `PersistContext.Notify` and
`ActionContext.Notify` so a distribution author raises a notification with one line inside a save
or an action. Spec 0019 is the largest consumer: its completion and tick batches raise the job and
schedule notifications through the same `INotifier`, and its `core.notification-retention` schedule
runs the retention handler this spec ships. Spec 0010 defines the listeners the hub implements and
the route group the hub is mapped on; spec 0015 maps it.

Deliberately left to later specs: the `email` channel's sender (the outbox spec, behind the channel
seam of §4.4), push channels and their device subscriptions, a cross-instance nudge over the hub, a
rendered `Title` column, and the reserved MCP tool `tellma_notifications`.

## Goals / Non-goals

**Goals**

- Ship `core.Notifications`, the `Notification` entity, the notification type registry with its
  descriptors and the eight Core types, and `INotificationRenderer` (read-time ICU rendering).
- Ship `INotifier.Notify(batch, requests)` riding the caller's batch with inactive-recipient,
  mute and duplicate predicates inside the statement (a duplicate suppressed or replaced in
  place), ids reserved inside the statement, and the `inbox.changed` event published after
  commit; `NotifyAsync` for callers with no batch.
- Ship the channel registry with `inbox` and `email`, the channel seam a sender implements,
  `core.NotificationPreferences`, its self-service `get`/`save` operations, the
  `Notifications.CannotMute` rule and the `PreferencesTag` bump.
- Ship the inbox: `InboxSeenAt`, `inbox/summary` with capped counts and the latest ten,
  `inbox/seen`, `inbox/read`, `inbox/read-all`, and the notifications page as the standard query
  over `core.Notification` under the self-scope criterion.
- Ship `TellmaHub`, its groups, the client event catalogue, `IClientEventPublisher` and its
  SignalR implementation, the session and tenant-state listeners, and the three hosting modes
  (in-process, Redis backplane, Azure SignalR Service).
- Ship the `core.notification-retention` handler and schedule, the telemetry, the composition
  sugars' realizers, and the startup checks.

**Non-goals (explicitly out of scope)**

- **The job machinery, the scheduler and their notifications' triggers** — spec 0019; this spec
  only defines the contracts they call and lists their types in the catalogue.
- **Email delivery** — the outbox spec ships the `email` channel's sender behind §4.4; the channel
  is registered and its preference editable now, consumed later.
- **Push channels and device subscriptions** — a later spec, or a distribution, registers them as
  channels (§4.1); nothing is reserved for them here.
- **The welcome email** — spec 0017 sends it post-commit through `IEmailSender` to every user an
  invite added; this spec ships only the inbox type `core.user.added`.
- **Export and import notifications' content** — spec 0018 raises them; this spec registers the
  descriptors' shape and the catalogue row.
- **The session store, back-channel logout and the BFF** — spec 0010; the hub only listens.
- **MCP tools over notifications** — `tellma_notifications` stays reserved (spec 0015); the
  generic `tellma_query` over `core.Notification` already answers under the self-scope.
- **A cross-instance nudge or any server-to-server use of the hub** — rejected by spec 0019.

## 1. Placement and architecture

### 1.1 Projects and packages

| Piece | Location | Notes |
|---|---|---|
| Contracts | `src/core/Tellma.Core.Abstractions/`, namespaces `Tellma.Core.Abstractions.Notifications` and `Tellma.Core.Abstractions.Realtime` | Every type of §2–§6 and §9.1, §10.1; BCL plus `Tellma.Core.Queryex` only. `NotificationRowList` and `NotificationPreferenceList` live in `Tellma.Core.Abstractions.TableTypes`. |
| Runtime | `src/core/Tellma.Core/`, namespace `Tellma.Core.Notifications` | `Notifier`, `NotificationStatements`, `NotificationTypeRegistry`, `NotificationChannelRegistry`, `ClientEventRegistry`, `NotificationRenderer`, `InboxService`, `NotificationPreferencesService`, `NotificationAccessCriteria`, `NotificationRetentionHandler`, `NullClientEventPublisher`, the realizers of `NotificationTypeContributionItem`, `NotificationChannelContributionItem` and `ClientEventContributionItem`. No SignalR reference. |
| Web host | `src/core/Tellma.Core.AspNetCore/`, namespace `Tellma.Core.AspNetCore.Realtime` | `TellmaHub`, `TellmaUserIdProvider`, `HubConnectionTracker`, `SignalRClientEventPublisher`, `TellmaRealtimeOptions`, `RealtimeApplicationName`, `AddTellmaRealtime`. SignalR from the shared framework `Microsoft.AspNetCore.App`; no Azure or Redis package. |
| Composition | `Tellma.Core.Composition` | `CoreFeature` contributes the `Notification` stack, the two `[ApiRoute]` services, the criteria provider, the Core notification types this spec owns, the two baseline channels, the four client events, the retention handler and its built-in schedule. |
| Reference distribution | `distributions/acme/` (`Tellma.Distro.Acme.Web`) | Composes realtime through `UseAzureDefaults()` (`Tellma.Defaults.Azure`), which carries the `Microsoft.Azure.SignalR` 1.33.1 pin and selects the mode (spec 0010 §1.3); `Program.cs` stays the three platform calls. `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.11 is pinned by an on-premises host, not by this distribution. |
| Tests | `test/core/Tellma.Core.Tests/Notifications/`, `test/core/Tellma.Core.IntegrationTests/Notifications/`, `test/core/Tellma.Core.AspNetCore.Tests/Realtime/`, `test/core/Tellma.Core.AspNetCore.IntegrationTests/Realtime/` | §12. |

Dependency edges: `Tellma.Core.Abstractions` → `Tellma.Core.Queryex`; `Tellma.Core` →
`Tellma.Core.Abstractions`, `Tellma.Core.EntityFrameworkCore`, `MessageFormat`;
`Tellma.Core.AspNetCore` → `Tellma.Core` + `Microsoft.AspNetCore.App`. `Tellma.Core` registers
`NullClientEventPublisher` with `TryAdd` semantics so a host without the web package (a dedicated
worker, the migrator, the offline test suites) composes and runs; `Tellma.Core.AspNetCore` replaces
it with the SignalR publisher. Nothing in `Tellma.Core` depends on spec 0019: the retention handler
is an ordinary `IJobHandler` and the built-in schedule an ordinary contribution, both resolved by
spec 0019's runtime when it is present.

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
  statement and up to one row per recipient.
- The **inbox** is one user's view of their rows: two counters, the latest items, the page.
- **Unseen** rows were created after the user last opened the inbox; **unread** rows carry no
  `ReadAt`.
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
   indexes; nothing increments a column.
5. **Bookkeeping never touches audit columns.** `core.Notifications` has no `ModifiedAt`;
   `InboxSeenAt` lives on `core.UserStamps`; neither table bumps a tenant version tag.
6. **The hub is server→client only and carries no content.** Groups are computed from the
   authenticated context; the client fetches what an event announces.

### 1.4 Statement execution rules

Every statement in §3–§5 and §7 is fixed text owned by this spec, executed through spec 0011's
`IDataBatch.Sql(sql, options)` with `SqlOptions.Writes` naming each table it writes. Parameter and
table-variable names use the reserved `@tb{b}_` prefix; the caller's user id is the connect
prologue's `@tm_UserId` (spec 0013's prologue rides every caller batch).

| Statement | `Purpose` | `TransactionMode` | `Idempotent` | `Writes` | Scope |
|---|---|---|---|---|---|
| notify (`INotifier.Notify`) | the caller's (`Persist`) | the caller's (`Auto`) | `false` | `core.Notifications` | the caller's |
| notify (`NotifyAsync`) | `Persist` | `Auto` | `false` | `core.Notifications` | the current scope |
| a channel's statement (§4.4) | the notify call's | the notify call's | `false` | the channel's own tables | the notify call's |
| inbox summary, preferences get | `Read` | `Auto` | `true` | none | the caller's |
| inbox seen | `Persist` | `Auto` | `true` | `core.UserStamps` | the caller's |
| inbox read, read-all | `Persist` | `Auto` | `true` | `core.Notifications` | the caller's |
| preferences save | `Persist` | `Auto` | `false` | `core.NotificationPreferences`, `SqlOptions.ForCaller` | the caller's |
| retention page | `Maintenance` | `None` | `true` | `core.Notifications` | system tenant scope |

`core.Notifications`, `core.NotificationPreferences` and `core.UserStamps` carry no tenant-level
version-tag attribute and never bump a tenant tag; `core.NotificationPreferences` is covered by
spec 0012's fixed user-level rule (`UserVersionTagRule(core.NotificationPreferences, Preferences,
"UserId")`), so the preferences save names the caller through `SqlOptions.ForCaller` and the
executor's epilogue bumps that user's `PreferencesTag`. The `Read` batches of the inbox declare no
dependencies and are served regardless of tag state (`inbox/summary` is a polling endpoint and
carries spec 0013's `NoActivityStampMetadata`, per spec 0015).

## 2. Notifications

### 2.1 `core.Notifications`

Non-temporal; written only by `INotifier` (the insert and the replace of §3.3) and the inbox verbs
(`ReadAt`); `[TableType]` (every column; used by the fixture tier only); sequence
`core.sq_Notifications`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | `PK_Notifications` clustered; `core.sq_Notifications` | reserved inside the insert |
| `UserId` | `int` | no | `FK_Notifications_UserId → core.Users(Id)` NO ACTION | the recipient |
| `Type` | `nvarchar(64)` | no | | registered type key |
| `ArgumentsJson` | `nvarchar(4000)` | yes | | ICU arguments; the client's click data |
| `TargetResource` | `varchar(128)` | yes | | securable resource id (`core.Export`, `acme.Document`) |
| `TargetId` | `bigint` | yes | | the target row's id |
| `ActorUserId` | `int` | yes | `FK_Notifications_ActorUserId → core.Users(Id)` NO ACTION | who caused it; null for the system |
| `DedupKey` | `nvarchar(128)` | yes | | best-effort collapse key |
| `CreatedAt` | `datetimeoffset(3)` | no | | `SYSUTCDATETIME()` in the insert |
| `ReadAt` | `datetimeoffset(3)` | yes | | null while unread |

Indexes: `IX_Notifications_User (UserId, CreatedAt DESC) INCLUDE (ReadAt, Type)` —
the summary's unseen count and the latest-ten page; `IX_Notifications_Unread (UserId) WHERE ReadAt
IS NULL` — the unread count and the read verbs; `IX_Notifications_Dedup (UserId, DedupKey) WHERE
DedupKey IS NOT NULL AND ReadAt IS NULL` — non-unique, the seek of the duplicate predicate and
of the replace;
`IX_Notifications_Retention (ReadAt, CreatedAt)` — the retention pages. No `ON DELETE CASCADE`: a
user is never deleted after `State = New` (spec 0013); deleting a `New` user with rows is a 547
mapped to `Fk.InUse` by the pipeline, which is the correct answer.

### 2.2 The `Notification` entity and its stack

```csharp
// Tellma.Core.Abstractions.Notifications
public sealed class Notification : Entity<int>   // [Table("Notifications", Schema = "core")], [TableType], [Stack(Operations = Query | Details)], [DefaultSelect(...)]
{
    public int UserId { get; set; }                     // server-owned; FK -> core.Users
    public string Type { get; set; }                    // server-owned
    public string? ArgumentsJson { get; set; }          // server-owned; [JsonColumn] is not applied (nvarchar(4000), not max)
    public string? TargetResource { get; set; }         // server-owned
    public long? TargetId { get; set; }                 // server-owned
    public int? ActorUserId { get; set; }               // server-owned; FK -> core.Users
    public string? DedupKey { get; set; }               // server-owned
    public DateTimeOffset CreatedAt { get; set; }       // datetimeoffset(3); server-owned
    public DateTimeOffset? ReadAt { get; set; }         // datetimeoffset(3); server-owned
}
```

`Notification` is a system-written entity (spec 0011): it derives from `Entity<int>`, carries
`CreatedAt` explicitly, has no audit set and no concurrency token, and is never written through
`Save`. Its stack is `[Stack(Operations = Query | Details)]`: `query`, `get`, `get-by-ids` and
nothing else — no save, delete, actions or Excel operations. `[DefaultSelect("Id, Type,
ArgumentsJson, TargetResource, TargetId, ActorUserId, CreatedAt, ReadAt")]`; `UserId` and `DedupKey`
are selectable but not in the default projection. The resource segment is `notifications`; the
entity name — the Queryex name and the securable resource alike — is `core.Notification` (spec
0013's `CoreResources.Notification`). Navigations from the two FKs: `User` and `ActorUser` (derived
from `ActorUserId`). Neither navigation is in a `[RelatedSelect]` of its own — `User` and
`ActorUser` project through spec 0011's default related projection (`Id`, the `Name` group, the
avatar column), so the notifications page shows the actor's name and image and nothing else about
them.

**The self-scope.** `NotificationAccessCriteria : IAccessCriteriaProvider` (`Resource =
"core.Notification"`) returns one criterion for every user: `AccessCriterion("Read", "UserId =
me()", "self")`. Spec 0013's composition rule makes a criterion alone a `Filtered` decision, so
every member reads exactly their own rows with no role grant; the pipeline conjoins the filter into
every query and details read of the stack. A stored role grant on `core.Notification × Read` is
accepted by the securables registry (the pair exists) and widens the scope by spec 0013's `Or`
composition — that is the audit path for an administrator who must see what a user was told, and
no such grant is seeded (review flag 9).

### 2.3 The type registry

```csharp
// Tellma.Core.Abstractions.Notifications
public sealed record NotificationTypeDescriptor(
    string Key, string Category, bool Mutable, string? TargetResource);

public interface INotificationTypeRegistry              // singleton; built at composition; validated by the startup check of §11
{
    IReadOnlyList<NotificationTypeDescriptor> All { get; }
    NotificationTypeDescriptor? Find(string key);       // ordinal comparison
    IReadOnlyList<string> Categories { get; }
}
```

| Member | Meaning |
|---|---|
| `Key` | The row's `Type`. Grammar `^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$`, at most 64 characters — the handler-key grammar of spec 0019, cited, not redefined. `core.` is reserved for the platform; modules use their slug (`gl.`), distributions their deployment slug (`acme.document.assigned`). Keys are persisted and stable for the life of a distribution; a changed meaning is a new key. |
| `Category` | Lowercase identifier `^[a-z][a-z0-9-]*$`, at most 32 characters; groups the preferences page. Core categories: `jobs`, `files`, `users`. |
| `Mutable` | `true`: the user may disable the `inbox` channel for the type. `false`: the inbox channel cannot be disabled (`Notifications.CannotMute` on save; the insert ignores preferences). Every other channel is always the user's choice. |
| `TargetResource` | When set, every request of the type must carry this `TargetResource` and a `TargetId`, and the client maps the resource to a route. When null, requests carry neither. |

Types are registered through spec 0010's `FeatureContribution.NotificationType(descriptor)`
(`NotificationTypeContributionItem`), realised by `Tellma.Core` into the registry. The Core
catalogue, with the spec whose code raises each type and therefore declares its descriptor:

| Key | Category | `Mutable` | Target | Raised by | Arguments |
|---|---|---|---|---|---|
| `core.export.ready` | `files` | false | `core.Export` | spec 0018's export handler | `{ fileName, rowCount }` |
| `core.import.completed` | `files` | false | `core.Import` | spec 0018's import handler | `{ fileName, rowCount, errorCount }` |
| `core.import.failed` | `files` | false | `core.Import` | spec 0018's import handler | `{ fileName, errorCode }` |
| `core.job.failed` | `jobs` | false | `core.Job` | spec 0019 | `{ handlerKey, errorCode, errorMessage }` |
| `core.job.held` | `jobs` | false | `core.Job` | spec 0019 | `{ handlerKey, dueAt }` |
| `core.schedule.paused` | `jobs` | true | `core.Schedule` | spec 0019 | `{ scheduleId, name, reason }` |
| `core.scheduler.gap` | `jobs` | false | none | spec 0019 (`DedupKey = 'core.scheduler.gap'`) | `{ previousTickAt, now, heldCount }` |
| `core.user.added` | `users` | true | none | spec 0017's `UserService.Invite` | `{ tenantName, actorName }` |

Each descriptor is declared once, by the feature code that raises it (spec 0019's registration for
its four; spec 0018's for its three; this spec's `CoreFeature` contribution for `core.user.added`);
the registry rejects a duplicate key at startup. A distribution declares its own types the same
way, in its feature's `Contribute`.

### 2.4 Rendering

```csharp
// Tellma.Core.Abstractions.Notifications
public interface INotificationRenderer                  // scoped
{
    string Render(Notification notification, CultureInfo uiCulture);
}
```

`Render` resolves the resource `Notification_<Key>` — the key with every `.` replaced by `_`
(`Notification_core_export_ready`) — through spec 0012's `IcuStringLocalizerFactory` in the
declaring package's `Resources/Strings.resx` (satellites ship with the package that declares the
type; a distribution's types resolve against the distribution's resources), and formats it with
the row's `ArgumentsJson` as ICU named arguments: top-level JSON members only; strings as strings,
numbers as numbers, booleans as booleans, ISO 8601 strings as `DateTimeOffset` (rendered in the
reader's display zone and calendar through spec 0012's `ICalendarSystem`), anything else as its
JSON text. A missing resource renders the key itself and counts on spec 0012's
`tellma.localization.missing`; malformed `ArgumentsJson` renders with no arguments and logs
`NotificationArgumentsInvalid` (warning, once per row id). The summary endpoint never calls the
renderer: `InboxSummary.Latest` carries the raw rows and the SPA renders them from spec 0012's
string pack — the server-side renderer serves MCP and the future email digest. The notifications
page shows the rendered text as a client-side column; sorting and filtering on rendered text is
not offered (review flag 1).

## 3. `INotifier`

### 3.1 Contracts

```csharp
// Tellma.Core.Abstractions.Notifications
public enum DuplicatePolicy { Suppress, Replace }

public sealed record NotificationRequest(
    string Type, IReadOnlyList<int> RecipientIds, object? Arguments, string? TargetResource,
    long? TargetId, int? ActorUserId, string? DedupKey, DuplicatePolicy OnDuplicate = DuplicatePolicy.Suppress);

public interface INotifier                              // scoped
{
    void Notify(IDataBatch batch, IReadOnlyList<NotificationRequest> requests);
    Task NotifyAsync(IReadOnlyList<NotificationRequest> requests);
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
}
```

| Member | Meaning |
|---|---|
| `NotificationRequest.Type` | A registered key (§2.3). |
| `RecipientIds` | User ids; duplicates collapse; the empty list appends nothing. At most `MaxRecipientsPerRequest`. |
| `Arguments` | Serialised with the platform JSON options (spec 0015) into `ArgumentsJson`; `null` stores `NULL`. At most `MaxArgumentsChars` after serialisation. |
| `TargetResource`, `TargetId` | Required together when the descriptor declares a target, and must equal the descriptor's resource; forbidden when it declares none. |
| `ActorUserId` | Defaults to `RequestContext.UserId`; `null` in a `System` scope. Must be an existing user id at insert time (the FK). |
| `DedupKey` | At most 128 characters. An unread row of the recipient with the same `DedupKey` is a **duplicate**; `OnDuplicate` decides its fate; a read row is never a duplicate. |
| `OnDuplicate` | `Suppress` (default): the recipient receives nothing while a duplicate exists. `Replace`: the duplicate becomes this request's row — type, arguments, target, actor and a fresh `CreatedAt`, same id, still unread — so it resurfaces unseen and on top, and the recipient receives `inbox.changed`; recipients without a duplicate receive a new row. Requires a `DedupKey`. |
| `Notify` | Appends one statement of §3.3 per request to `batch`, then each `INotificationChannel`'s statement (§4.4), registers one post-commit callback for the whole call (§3.4), returns synchronously. Never awaits, never allocates an id from the allocator's buffer. |
| `NotifyAsync` | Creates a `Persist` batch on spec 0011's `ITenantDatabase` of the current scope, calls `Notify`, executes it. For callers with no batch in flight (a hosted service, a post-commit effect). |

Every rule in the table is checked in C# before any statement is appended and fails the call with
`InvalidOperationException` — a request is composed by platform or distribution code, never by a
client, so a violation is a programming error (a 500 with a trace id), not a validation error. A
type that is not registered on this instance fails the same way: a distribution never raises a type
it did not declare. A call carries any number of requests, and several calls on one batch are the
same as one call with their union; the only bound is spec 0011's parameter cap per batch — eight
parameters per request, plus each channel's own.

### 3.2 The standalone table type

`NotificationRowList` (`Tellma.Core.Abstractions.TableTypes`; physical
`[dbo].[NotificationRowList_<hash8>]` per spec 0001 §5): `Ordinal int PK`, `UserId int`. One row per
distinct recipient, `Ordinal` from 0 in the recipients' first-seen order. Bound through
`IDataBatch.Tvp<NotificationRow>(rows)`.

### 3.3 The insert and the replace

Per request, ordinal `{b}`; `@tb{b}_t0 : NotificationRowList`; scalars `@tb{b}_p0 nvarchar(64)`
type, `@tb{b}_p1 nvarchar(4000)` arguments, `@tb{b}_p2 varchar(128)` target resource, `@tb{b}_p3
bigint` target id, `@tb{b}_p4 int` actor, `@tb{b}_p5 nvarchar(128)` dedup key, `@tb{b}_p6 bit`
the descriptor's `Mutable`; `SqlOptions(Writes = [core.Notifications], ResultSets = 1)`, or
`ResultSets = 2` under `Replace`, whose statement is the replace below followed by the insert:

```sql
-- Replace only, ahead of the insert:
UPDATE [n] SET [Type] = @tb{b}_p0, [ArgumentsJson] = @tb{b}_p1, [TargetResource] = @tb{b}_p2, [TargetId] = @tb{b}_p3,
               [ActorUserId] = @tb{b}_p4, [CreatedAt] = SYSUTCDATETIME()
OUTPUT [inserted].[UserId]
FROM [core].[Notifications] AS [n] JOIN @tb{b}_t0 AS [r] ON [r].[UserId] = [n].[UserId]
WHERE [n].[DedupKey] = @tb{b}_p5 AND [n].[ReadAt] IS NULL
  AND EXISTS (SELECT 1 FROM [core].[Users] AS [u] WHERE [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1)
  AND (@tb{b}_p6 = 0 OR NOT EXISTS (SELECT 1 FROM [core].[NotificationPreferences] AS [p]
                                    WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = @tb{b}_p0 AND [p].[Channel] = 'inbox' AND [p].[Enabled] = 0));

-- Every request:
DECLARE @tb{b}_n int = (SELECT COUNT(*) FROM @tb{b}_t0);
DECLARE @tb{b}_first sql_variant;
EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Notifications', @range_size = @tb{b}_n, @range_first_value = @tb{b}_first OUTPUT;
INSERT INTO [core].[Notifications] ([Id], [UserId], [Type], [ArgumentsJson], [TargetResource], [TargetId], [ActorUserId], [DedupKey], [CreatedAt])
OUTPUT [inserted].[UserId]
SELECT CAST(@tb{b}_first AS int) + [r].[Ordinal], [r].[UserId], @tb{b}_p0, @tb{b}_p1, @tb{b}_p2, @tb{b}_p3, @tb{b}_p4, @tb{b}_p5, SYSUTCDATETIME()
FROM @tb{b}_t0 AS [r]
WHERE EXISTS (SELECT 1 FROM [core].[Users] AS [u] WHERE [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1)
  AND (@tb{b}_p6 = 0 OR NOT EXISTS (SELECT 1 FROM [core].[NotificationPreferences] AS [p]
                                    WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = @tb{b}_p0 AND [p].[Channel] = 'inbox' AND [p].[Enabled] = 0))
  AND (@tb{b}_p5 IS NULL OR NOT EXISTS (SELECT 1 FROM [core].[Notifications] AS [n]
                                        WHERE [n].[UserId] = [r].[UserId] AND [n].[DedupKey] = @tb{b}_p5 AND [n].[ReadAt] IS NULL));
```

Rules: ids are reserved inside the statement, never through the allocator (an id filtered out by
a predicate is simply unused — sequences are not gap-free and nothing reads them as a count); the
range is taken even when every recipient is filtered, because `@tb{b}_n > 0` is guaranteed by the
C# side; `@tb{b}_p6 = 0` short-circuits the preferences seek for non-mutable types; the duplicate
predicate is best effort — two concurrent batches can both pass it and both insert, which is
accepted because a unique filtered index would fail a business save over a notification race
(review flag 2); under `Replace` the update runs first and the insert's duplicate predicate then
skips every row it refreshed, so a recipient gets a refreshed row or a new one, never both, and a
replace is exactly as best-effort as the predicate it rides; an unknown recipient id inserts
nothing (the `EXISTS` on `core.Users`), and a recipient deactivated between validation and persist
inserts nothing, so a save never fails for a notification reason. The `OUTPUT` result sets — the
user ids inserted and, under `Replace`, the ids refreshed — are read by the notifier after
execution and feed §3.4 and the `tellma.notifications.created` and `tellma.notifications.replaced`
counters; the difference between `@tb{b}_n` and the rows returned feeds
`tellma.notifications.suppressed`.

### 3.4 After commit

`Notify` registers one `IDataBatch.OnCommitted` callback per call. After the round trip commits,
the callback unions the inserted and refreshed user ids of every request in the call and
publishes one `ClientEvent("inbox.changed", userIds, null)` through
`IClientEventPublisher.PublishAsync` (§6.4) — one event per user however many requests targeted
them. Failures in the callback are logged (`InboxEventFailed`, warning) and never thrown, per spec
0011's `OnCommitted` contract; the row is committed either way and the client's next summary call
sees it.

### 3.5 Raising a notification from the pipeline

Spec 0014 exposes `PersistContext.Notify(request)` and `ActionContext.Notify(request)`, each
calling `INotifier.Notify` on the persist batch, so a service hook raises a notification in the
same transaction as the write it describes and the event fires only when that write commits.

*Illustration* — a distribution's `ContributeAsync` hook after assigning a document:

```csharp
context.Notify(new NotificationRequest("acme.document.assigned", [assigneeId], new { number = doc.Number, comment },
    "acme.Document", doc.Id, context.Context.UserId, DedupKey: $"acme.document.assigned:{doc.Id}",
    OnDuplicate: DuplicatePolicy.Replace));
```

Reassigning the document, or editing the assignment's comment, while the first notice is unread
refreshes that notice in place for the same assignee — the latest comment, back on top; once it
was read, a fresh notice is inserted. Spec 0019's handlers call
`JobItem.NotifyOnSuccess(request)` and the worker calls `INotifier.Notify` on the completion
batch; both paths end in §3.3.

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

A channel is registered through spec 0010's `FeatureContribution.NotificationChannel(descriptor)`
(`NotificationChannelContributionItem`), realised by `Tellma.Core` into the registry; a duplicate
key is rejected at startup. `Key` follows the category grammar of §2.3 (`^[a-z][a-z0-9-]*$`), at
most 16 characters; its label is `NotificationChannel_<Key>` in the declaring package's resources.
`DefaultEnabled` is what a user who saved nothing gets. `CoreFeature` registers the two baseline
channels of every distribution: `inbox` (`DefaultEnabled = true`), which is the statement of §3.3,
and `email` (`DefaultEnabled = false`), whose sender the outbox spec ships behind §4.4. A module or
a distribution registers further channels (`push`, `sms`, `whatsapp`) the same way, each with its
sender; a distribution hides nothing, because a channel it did not register does not exist on its
instance.

### 4.2 `core.NotificationPreferences`

Non-temporal; not an entity; absent from the Queryex schema; no UDTT.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `UserId` | `int` | no | PK part; `FK_NotificationPreferences_UserId → core.Users(Id) ON DELETE CASCADE` | |
| `Type` | `nvarchar(64)` | no | PK part | a registered type key; unknown keys are refused on save |
| `Channel` | `varchar(16)` | no | PK part | a registered channel key; unknown keys are refused on save |
| `Enabled` | `bit` | no | | ignored by the insert for `inbox` on a non-mutable type |

PK clustered `(UserId, Type, Channel)`. A missing row means the channel's `DefaultEnabled` for the
type; the save stores deviations only (§4.3), so the table stays small and a channel's seek usually
finds nothing. The cascade is the one place a user-sibling row disappears with the user, and it
fires only for a `New` user (spec 0013's delete policy), who cannot have saved preferences — the
cascade exists for schema honesty, not for a path that runs.

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

public sealed record NotificationPreferencesResult(
    IReadOnlyList<NotificationTypeDescriptor> Types,
    IReadOnlyList<NotificationChannelDescriptor> Channels,
    IReadOnlyList<NotificationPreference> Preferences);   // Preferences = stored deviations only

// Tellma.Core.Notifications (runtime)
public sealed class NotificationPreferencesService      // [ApiRoute("notification-preferences")]
{
    public Task<NotificationPreferencesResult> GetAsync();
    public Task<NotificationPreferencesResult> SaveAsync(IReadOnlyList<NotificationPreference> preferences);
}
```

| Member | Annotation and rule |
|---|---|
| `Get` | `[ApiAction("get", MemberOnly = true, Idempotent = true)]`. One `Read` batch: `SELECT [Type], [Channel], [Enabled] FROM [core].[NotificationPreferences] WHERE [UserId] = @tm_UserId;`. `Types` lists every registered type descriptor and `Channels` every channel descriptor; the SPA renders their labels, `NotificationType_<Key>` and `NotificationChannel_<Key>` with `.` → `_`, from spec 0012's string pack. |
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

The `PreferencesTag` bump is what makes spec 0013's cached `UserProfile` and the SPA's
`preferences` wire tag move, so a second tab learns of the change on its next request. Rows for a
type or channel whose descriptor was removed by a deployment stay in the table, are invisible to
`Get` (unknown keys are filtered out of `Preferences`), and are deleted by the next `Save` (absent
from the list); no statement reads them, because no request carries a removed type and no sender
exists for a removed channel.

### 4.4 The channel seam

```csharp
// Tellma.Core.Abstractions.Notifications
public sealed record NotifyStatement(SqlIdentifier Recipients);   // the insert's recipient rows, as IDataBatch.Tvp returned them

public interface INotificationChannel                   // scoped; at most one per registered channel other than inbox
{
    string Key { get; }
    void Append(IDataBatch batch, NotificationRequest request, NotifyStatement insert);
}
```

`inbox` is the notifier's own statement; every other registered channel delivers through an
`INotificationChannel`, registered as a scoped service by the feature that registered the
descriptor (the startup check of §11 verifies the pairing; a channel with no sender stores its
preference and delivers nothing). After appending a request's insert, `Notify` calls `Append` on
every channel in registration order, in the same call and on the same batch, so a channel's work
rides the caller's transaction and costs no round trip. A channel that delivers through a table
(the outbox spec's `email`) appends its own fixed-text statement under its own ordinal `{c}`, with
`SqlOptions.Writes` naming its tables, selecting from the insert's recipient rows through the
identifier it was handed and deciding each recipient itself: the active-user predicate of §3.3,
its own preference rows — `COALESCE((SELECT [p].[Enabled] FROM [core].[NotificationPreferences]
AS [p] WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = @tb{c}_p0 AND [p].[Channel] =
@tb{c}_p1), @tb{c}_p2) = 1` — and whatever else its transport needs; the inbox's duplicate rule is
not its concern. A channel that
delivers through an API call appends a `SELECT` of the same shape, reads its result set after
execution, and sends from a `batch.OnCommitted` callback, so nothing leaves the process for a
transaction that did not commit. A channel never raises past the batch: a statement it appends
fails the batch like any other statement, and an `OnCommitted` failure is logged.

## 5. The inbox

### 5.1 `InboxSeenAt`

Spec 0013's `core.UserStamps` carries `InboxSeenAt datetimeoffset(3) NULL`, written only by
`inbox/seen`. Unseen rows are those with `CreatedAt > InboxSeenAt` — every row when it is null.

### 5.2 Contracts

```csharp
// Tellma.Core.Abstractions.Notifications
public sealed record InboxSummary(int Unseen, int Unread, IReadOnlyList<Notification> Latest);   // counts capped at SummaryCountCap; Latest = SummaryLatestCount rows, newest first

// Tellma.Core.Notifications (runtime)
public sealed class InboxService                        // [ApiRoute("inbox")]; every action member-only
{
    public Task<InboxSummary> SummaryAsync();
    public Task<AffectedResult> SeenAsync();
    public Task<AffectedResult> ReadAsync(IdsRequest request);
    public Task<AffectedResult> ReadAllAsync();
}
```

| Member | Annotation | Batch |
|---|---|---|
| `Summary` | `[ApiAction("summary", MemberOnly = true, Idempotent = true)]`; `NoActivityStampMetadata` (spec 0015) | one `Read` batch through spec 0013's `IGuardedBatchRunner.Run(Read, …)`: the summary statement; `Latest` rows materialised by name |
| `Seen` | `[ApiAction("seen", MemberOnly = true, Idempotent = true)]` | one `Persist` batch: the seen statement; `Count` = 1 |
| `Read` | `[ApiAction("read", MemberOnly = true, Idempotent = true)]`; `IdsRequest.Ids` (spec 0015; `ReturnEntities`, `Select`, `Include` ignored) | one `Persist` batch: the read statement with `@tb{b}_t0 : IdList`; `Count` = rows newly marked |
| `ReadAll` | `[ApiAction("read-all", MemberOnly = true, Idempotent = true)]` | one `Persist` batch: the read-all statement; `Count` = rows newly marked |

Each of `Seen`, `Read` and `ReadAll` publishes `ClientEvent("inbox.changed", [callerId], null)`
through `IClientEventPublisher.Publish(batch, …)` so the user's other tabs and devices refresh.
Ids in `Read` that belong to another user or are already read are ignored — the predicate is the
authorization; no 404 or 403 is raised for an id outside the caller's rows.

### 5.3 Statements

```sql
-- inbox/summary (Read; @tb{b}_p0 = SummaryCountCap, @tb{b}_p1 = SummaryLatestCount; ResultSets = 2)
DECLARE @tb{b}_seenAt datetimeoffset(3) = (SELECT [InboxSeenAt] FROM [core].[UserStamps] WHERE [UserId] = @tm_UserId);
SELECT
  (SELECT COUNT(*) FROM (SELECT TOP (@tb{b}_p0) 1 AS [x] FROM [core].[Notifications]
                         WHERE [UserId] = @tm_UserId AND [CreatedAt] > COALESCE(@tb{b}_seenAt, '0001-01-01')) AS [a]) AS [Unseen],
  (SELECT COUNT(*) FROM (SELECT TOP (@tb{b}_p0) 1 AS [x] FROM [core].[Notifications]
                         WHERE [UserId] = @tm_UserId AND [ReadAt] IS NULL) AS [b]) AS [Unread];
SELECT TOP (@tb{b}_p1) [Id], [UserId], [Type], [ArgumentsJson], [TargetResource], [TargetId], [ActorUserId], [DedupKey], [CreatedAt], [ReadAt]
FROM [core].[Notifications] WHERE [UserId] = @tm_UserId ORDER BY [CreatedAt] DESC, [Id] DESC;

-- inbox/seen (Persist; Writes = core.UserStamps)
UPDATE [core].[UserStamps] SET [InboxSeenAt] = SYSUTCDATETIME() WHERE [UserId] = @tm_UserId;

-- inbox/read (Persist; Writes = core.Notifications; @tb{b}_t0 : IdList; ResultSets = 1)
UPDATE [core].[Notifications] SET [ReadAt] = SYSUTCDATETIME()
WHERE [UserId] = @tm_UserId AND [ReadAt] IS NULL AND [Id] IN (SELECT [Id] FROM @tb{b}_t0);
SELECT @@ROWCOUNT AS [Count];

-- inbox/read-all (Persist; Writes = core.Notifications; ResultSets = 1)
UPDATE [core].[Notifications] SET [ReadAt] = SYSUTCDATETIME() WHERE [UserId] = @tm_UserId AND [ReadAt] IS NULL;
SELECT @@ROWCOUNT AS [Count];
```

Both counts are `O(SummaryCountCap)` seeks on the two user indexes; the badge shows `99+` past the
cap and never a maintained number. `Latest` returns the raw rows; the client renders them (§2.4).
The seen statement writes the sibling row with no `Stamp` semantics — `core.UserStamps` is
bookkeeping, outside every version tag and every audit column.

### 5.4 The client protocol

The SPA calls `inbox/summary` at load, on every `inbox.changed` event, and on hub reconnect; it
calls `inbox/seen` when the bell opens (the red badge resets), `inbox/read` with one id when an
item is clicked (then navigates to the route it maps from `TargetResource` and `TargetId`, or
nowhere for a target-less type), and `inbox/read-all` from the dropdown's button. "View all
notifications" opens the standard search page over `core.Notification` (`query` with the default
select, ordered `CreatedAt desc`), which offers the standard filtering and column selection over
the raw columns plus the client-rendered text column. The preferences page is `Get`/`Save` of
§4.3: types by registered channel, grouped by `Category`, the `inbox` switch locked on for
non-mutable types.

## 6. The hub

### 6.1 `TellmaHub` and the connect

`TellmaHub` (`Tellma.Core.AspNetCore.Realtime`) is one SignalR hub class mapped by spec 0015's
`MapTellma` at `/{tenantId:int:min(1)}/hub` on spec 0010's `Hub` route group: session-cookie
scheme (`TellmaPolicies.Web`), `MemberEndpointMetadata`, `TenantEndpointMetadata(Hub, IsMutation =
false)`, the `Origin`/`Sec-Fetch-Site` check with cross-origin negotiate refused outright, and no
`Tellma-Client` header (a browser cannot decorate the WebSocket handshake). The negotiate and the
connect request pass spec 0010's tenant middleware and `ITenantAccessGuard.EnsureAccessAsync`
like any read: the initializers run, spec 0013's connect step resolves the subject to a user id
(the cold path pays its prologue-only round trip), non-members and deactivated users are refused
with `TenantNotFoundException` (404), and a `ReadOnly` tenant accepts the connection because the
hub is not a mutation. `Suspended`, `Provisioning` and `Retired` tenants refuse it with the
verdicts of spec 0010.

`OnConnectedAsync` reads `IRequestContextAccessor.Current` (`Tenant.Id`, `UserId`, `Subject`,
`SessionId`), adds the connection to its groups (§6.2), records it in `HubConnectionTracker`, and
counts `tellma.realtime.connects{outcome = connected}`. `OnDisconnectedAsync` removes it. The hub
exposes no client-invocable methods — every invocation is refused with a `HubException` and
counted as `tellma.realtime.connects{outcome = invocation-refused}`; the channel is server→client
only. `TellmaUserIdProvider : IUserIdProvider` returns the identity `sub`, so SignalR's per-user
addressing (and Azure SignalR's user-scoped operations) names the same principal spec 0003 does.
Hub options: `KeepAliveInterval` and `ClientTimeoutInterval` from `TellmaRealtimeOptions` (§9.2),
`MaximumReceiveMessageSize = 4096` (clients send nothing but protocol frames), JSON protocol only,
`EnableDetailedErrors = false`.

### 6.2 Groups

Group membership is computed from the authenticated context, never from client input. Groups are
not a security boundary in SignalR; they are one here because only the server assigns them and the
server assigns them from spec 0013's connect result.

| Group | Members | Used by |
|---|---|---|
| `t{tenantId}` | every connection of the tenant | events with empty `UserIds`; tenant-state closes |
| `t{tenantId}.u{userId}` | the user's connections in this tenant | every user-addressed event |
| `t{tenantId}.s{subject}` | the subject's connections in this tenant | `TenantAccessRevokedAsync(tenantId, subject)` |
| `x{sessionKey}` | the connections of one BFF session, across tenants | `SessionsTerminatedAsync(subject, sessionKeys)` |

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

Events are registered through spec 0010's `FeatureContribution.ClientEvent(name)`
(`ClientEventContributionItem`); the name grammar is the type-key grammar of §2.3; publishing an
unregistered name is `InvalidOperationException`. The Core catalogue:

| Event | Payload | Publisher | Recipients |
|---|---|---|---|
| `inbox.changed` | none | `INotifier` (§3.4), the inbox verbs (§5.2) | the affected users |
| `job.changed` | `{ jobId }` | spec 0019 (claim, progress-carrying renewal, completion) | the job's `RequestedById` |
| `cache.changed` | `{ tag }` | spec 0012's epilogue contributor, `Publish(batch, …)` per `settings` or `entity:<Name>` bump | every connected user of the tenant |
| `session.ended` | `{ reason }` | the hub's listeners (§6.5) | the closing connections |

`session.ended` reasons: `session-terminated`, `access-revoked`, `tenant-unavailable`. Payloads
are ids and short codes only; the publisher refuses a payload whose serialisation exceeds
`MaxEventPayloadBytes` (4,096) with `InvalidOperationException` — a thin event that grew is a
design error, not a runtime condition.

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
| `Publish` | With a batch: registers an `OnCommitted` callback that calls `PublishAsync`; several `Publish` calls on one batch coalesce into one callback that deduplicates `(Name, UserIds, Payload)` triples. Without a batch: `PublishAsync` fire-and-forget on the scope's `TaskScheduler`, exceptions logged. |
| `PublishAsync` | Validates the name and payload size, resolves the tenant from `RequestContext.Tenant` (a tenantless scope is `InvalidOperationException`), sends `ClientEventEnvelope` to `t{tenantId}.u{id}` per user id or to `t{tenantId}` when `UserIds` is empty, counts `tellma.realtime.events{event}`. Failures are logged (`ClientEventFailed`, warning) and never thrown to the caller. |

Two implementations: `NullClientEventPublisher` (`Tellma.Core.Notifications`; validates and
counts nothing, registered with `TryAdd`) and `SignalRClientEventPublisher`
(`Tellma.Core.AspNetCore.Realtime`; `IHubContext<TellmaHub>`), which `AddTellmaRealtime` (§6.6)
registers in place of the null one. A dedicated worker host therefore publishes nothing, and a
web host of the same deployment picks the event up on the client's next summary call at worst —
events are an optimisation, never a dependency (§1.3).

### 6.5 Listeners: sessions, revocation, tenant state

`HubConnectionTracker` (singleton) implements spec 0010's `ISessionTerminationListener` and
`ITenantStateListener` (restated members: `SessionsTerminatedAsync(subject, sessionKeys)`,
`TenantAccessRevokedAsync(tenantId, subject)`, `OnStateChangedAsync(tenant, previous)`), and maps
each connection this instance hosts to `(tenantId, userId, subject, sessionKey, HubCallerContext)`.

| Call | Event sent first | Then closed |
|---|---|---|
| `SessionsTerminatedAsync(subject, keys)` (back-channel logout, revocation; spec 0010) | `session.ended { session-terminated }` to each `x{key}` | every connection in those groups |
| `TenantAccessRevokedAsync(tenantId, subject)` (spec 0017's `UserService` after a deactivation commits) | `session.ended { access-revoked }` to `t{tenantId}.s{subject}` | that group |
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
never ended by a tenant deactivation (spec 0010) — the user may belong to another tenant, and that
tenant's connection stays up.

### 6.6 Hosting modes

`services.AddTellmaRealtime(Action<ISignalRServerBuilder>? configure = null)`
(`Tellma.Core.AspNetCore`) registers the hub, the user id provider, the tracker, the publisher and
`TellmaRealtimeOptions`; `MapTellma` maps the hub. `AddTellmaAspNetCore` — inside `AddTellma`, after
`compose` — calls `AddTellmaRealtime()` with no delegate, which never replaces a delegate a composer
registered earlier; a host selects a mode by calling `AddTellmaRealtime(configure)` on
`TellmaBuilder.Services` from `compose`. The delegate receives the framework's
`ISignalRServerBuilder` after `AddSignalR(hubOptions)` has run, so the composing host's own package
references decide the mode without `Tellma.Core.AspNetCore` referencing either package:

| Mode | When | Requirements |
|---|---|---|
| In-process | default; one instance | nothing |
| Redis backplane | on-premises, two or more instances | `AddStackExchangeRedis(connectionString, o => o.Configuration.ChannelPrefix = RedisChannel.Literal(prefix))`; sticky sessions at the load balancer; `prefix` = `DeploymentIdentity.DeploymentId` (spec 0007) so two deployments sharing a Redis never cross |
| Azure SignalR Service | SaaS; `Azure:SignalR:ConnectionString` present | `AddAzureSignalR(o => { o.ApplicationName = RealtimeApplicationName.From(deployment); o.ClaimsProvider = c => [sub]; o.AccessTokenLifetime = …; o.CloseOnAuthenticationExpiration = true; })`; the hub-only token carries `sub` and nothing else (spec 0003 §7.4); the application name isolates the deployment on a shared service instance (below) |

*Illustration* — the realtime line inside `UseAzureDefaults()` (`Tellma.Defaults.Azure`):

```csharp
if (tellma.Configuration["Azure:SignalR:ConnectionString"] is not null)
    tellma.Services.AddTellmaRealtime(s => s.AddAzureSignalR(o =>
    {
        o.ApplicationName = RealtimeApplicationName.From(deployment);   // "etpharma_staging"
        o.ClaimsProvider = c => [c.FindFirst("sub")!];
    }));
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

Startup fails (spec 0010's realised gate, check `realtime-hosting`) when
`Azure:SignalR:ConnectionString` is configured and no delegate selected Azure SignalR, or when
Azure SignalR is selected with an `ApplicationName` other than the deployment's — the check locates
the SDK's `ServiceOptions` by type name, as it does to detect the mode — and warns when the host
reports more than one instance (`WEBSITE_INSTANCE_ID` present without Azure SignalR, or
`Tellma:Realtime:ExpectedInstances > 1`) and no backplane is configured. Redis is required only for
on-premises multi-instance deployments; single-instance on-premises and SaaS need none.

## 7. Retention

`NotificationRetentionHandler` (`Tellma.Core.Notifications`) is an arguments-only
`IJobHandler` with `[JobHandler("core.notification-retention", LeaseSeconds = 600)]` (spec 0019's
attribute), registered through `FeatureContribution.JobHandler<NotificationRetentionHandler>()`
and scheduled by `FeatureContribution.BuiltInSchedule("core.notification-retention", "30 3 * * *")`
— the schedule row lives in spec 0019's reserved band, runs as the system user, and fires after
`core.job-retention`. Each page is its own `Maintenance` round trip, `Idempotent = true`
(`@tb{b}_p0` = `RetentionPageSize`, `@tb{b}_p1` = `ReadRetention` in seconds, `@tb{b}_p2` =
`UnreadRetention` in seconds; `ResultSets = 1`), looping per band until a page is short and
reporting progress per page through spec 0019's `IJobProgress.Report`:

```sql
DELETE TOP (@tb{b}_p0) FROM [core].[Notifications]
WHERE [ReadAt] IS NOT NULL AND [ReadAt] < DATEADD(second, -@tb{b}_p1, SYSUTCDATETIME());
SELECT @@ROWCOUNT AS [Deleted];
-- second band, same loop:
DELETE TOP (@tb{b}_p0) FROM [core].[Notifications]
WHERE [ReadAt] IS NULL AND [CreatedAt] < DATEADD(second, -@tb{b}_p2, SYSUTCDATETIME());
SELECT @@ROWCOUNT AS [Deleted];
```

Cut-offs are computed from the database clock inside the statement (spec 0019's clock rule).
Deleted rows count on `tellma.notifications.retained{band = read | unread}`. Retention never reads
preferences or types: a row of a removed type ages out like any other.

## 8. Securables, endpoints, pages

| Resource × action | Registered by | Filter root | Who holds it |
|---|---|---|---|
| `core.Notification × Read` | the stack feature's contributor (spec 0014), from `[Stack(Operations = Query \| Details)]` | `core.Notification` | every member through the self-scope criterion (§2.2); a stored grant widens |

`InboxService` and `NotificationPreferencesService` are `[ApiRoute]` services whose actions are all
`MemberOnly`: any connected active member, no securable; `contribution.ApiService<T>()` registers
them and the realised gate audits them like every endpoint (spec 0015). No `Save`, `Delete`,
`Activate` or Excel securable exists for notifications; none is sensitive.

Endpoints (spec 0015's projection, all POST under `/{tenantId}/api/web/`): `notifications/query`,
`notifications/get`, `notifications/get-by-ids`; `inbox/summary`, `inbox/seen`, `inbox/read`,
`inbox/read-all`; `notification-preferences/get`, `notification-preferences/save`; and the hub at
`/{tenantId}/hub`. Pages: the bell (summary dropdown), Notifications (standard search over
`core.Notification`), Notification preferences. MCP: `tellma_query`/`tellma_get` over
`core.Notification` work under the self-scope; `tellma_notifications` remains reserved.

## 9. Configuration

### 9.1 `Tellma:Notifications`

`NotificationsOptions` (§3.1), bound and validated at startup by spec 0010's `Options<T>`
declaration: every count at least 1; `SummaryCountCap` at most 1,000; `SummaryLatestCount` at most
50; `MaxArgumentsChars` at most 4,000 (the column width); `RetentionPageSize` at most 10,000.

### 9.2 `Tellma:Realtime`

```csharp
// Tellma.Core.AspNetCore.Realtime
public sealed class TellmaRealtimeOptions
{
    public bool Enabled { get; set; } = true;                                         // false: no hub is mapped; the null publisher stays
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan ClientTimeoutInterval { get; set; } = TimeSpan.FromSeconds(30);   // at least twice KeepAliveInterval
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);      // Azure SignalR only; the revocation bound of §6.5
    public int MaxEventPayloadBytes { get; set; } = 4096;
    public int ExpectedInstances { get; set; } = 1;                                   // the backplane warning of §6.6
}
```

`Azure:SignalR:ConnectionString` stays where spec 0010 places it (a host secret outside the
`Tellma:` tree); the Redis connection string for the backplane is the on-premises host's own
configuration, passed to its delegate.

## 10. Telemetry and logging

### 10.1 Instruments

```csharp
// Tellma.Core.Abstractions.Notifications
public static class NotificationsTelemetryNames         // constants; MeterName = "Tellma.Core"
{
    public const string Created = "tellma.notifications.created";
    public const string Suppressed = "tellma.notifications.suppressed";
    public const string Replaced = "tellma.notifications.replaced";
    public const string Retained = "tellma.notifications.retained";
    public const string Summaries = "tellma.notifications.inbox.summaries";
    public const string MarkedRead = "tellma.notifications.inbox.read";
    public const string PreferencesSaved = "tellma.notifications.preferences.saved";
    public const string TypeTag = "type";
    public const string BandTag = "band";
    public const string VerbTag = "verb";
}

// Tellma.Core.Abstractions.Realtime
public static class RealtimeTelemetryNames              // constants; MeterName = "Tellma.Core.AspNetCore"; Events is spec 0015's ApiTelemetryNames.RealtimeEvents ("tellma.realtime.events")
{
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
| `tellma.notifications.suppressed` | counter, `{notification}` | `type` | recipients filtered by inactivity, mute or a suppressed duplicate |
| `tellma.notifications.replaced` | counter, `{notification}` | `type` | unread duplicates refreshed in place |
| `tellma.notifications.retained` | counter, `{notification}` | `band ∈ read \| unread` | rows deleted by retention |
| `tellma.notifications.inbox.summaries` | counter, `{request}` | — | polling pressure |
| `tellma.notifications.inbox.read` | counter, `{notification}` | `verb ∈ read \| read-all` | rows marked read |
| `tellma.notifications.preferences.saved` | counter, `{request}` | — | preference churn |
| `tellma.realtime.events` (spec 0015) | counter, `{event}` | `event` | push volume |
| `tellma.realtime.connections` | up-down counter, `{connection}` | `mode ∈ in-process \| redis \| azure` | open connections on this instance |
| `tellma.realtime.connects` | counter, `{attempt}` | `outcome ∈ connected \| refused \| invocation-refused` | connect churn and refusals |
| `tellma.realtime.closes` | counter, `{connection}` | `reason ∈ session-terminated \| access-revoked \| tenant-unavailable` | forced closes |

The tenant and the user are never tags. Alert queries under `infra/monitoring/`: `suppressed`
rising against `created` for one type (a mute wave or a dedup storm), `connects{refused}` per
minute (a stale SPA or an attack), and `closes{access-revoked}` with no matching `session.ended`
delivery on Azure.

### 10.2 Log events

Structured events, tenant and user ids in the log scope: `NotificationsInserted` (debug; type,
requested, inserted), `InboxEventFailed` (warning), `ClientEventFailed` (warning; event name),
`ClientEventRefused` (error; unregistered name or oversized payload), `NotificationArgumentsInvalid`
(warning; row id), `HubConnected`/`HubDisconnected` (debug), `HubConnectionRefused` (information;
reason), `HubConnectionsClosed` (information; group, count, reason), `RealtimeHostingWarning`
(warning; the backplane check of §6.6).

## 11. Composition and startup checks

`CoreFeature.Contribute` adds: `Entity<Notification>()` (the default `EntityService<Notification>`;
no service subclass exists), `ApiService<InboxService>()`,
`ApiService<NotificationPreferencesService>()`, `Scoped<INotifier, Notifier>()`,
`Scoped<INotificationRenderer, NotificationRenderer>()`,
`Singleton<INotificationTypeRegistry, NotificationTypeRegistry>()`,
`Singleton<INotificationChannelRegistry, NotificationChannelRegistry>()`,
`Singleton<IClientEventRegistry, ClientEventRegistry>()`,
`Singleton<IAccessCriteriaProvider, NotificationAccessCriteria>()`,
`NotificationType(core.user.added)`, `NotificationChannel(inbox)`, `NotificationChannel(email)`,
`ClientEvent("inbox.changed")`, `ClientEvent("job.changed")`, `ClientEvent("cache.changed")`,
`ClientEvent("session.ended")`, `JobHandler<NotificationRetentionHandler>()`,
`BuiltInSchedule("core.notification-retention", "30 3 * * *")`, and the two standalone table types
on the model. `IClientEventPublisher` is registered with `TryAdd` as the null publisher.

`IStartupCheck`s reporting into spec 0010's realised gate: `notification-types` (every key and
category matches its grammar; keys unique; every non-null `TargetResource` is a registered
securable resource or stack resource, so the client can route it); `notification-channels`
(grammar; keys unique; `inbox` and `email` registered; every `INotificationChannel` names a
registered channel other than `inbox`, at most one per channel); `client-events` (grammar,
unique); `notification-options` (§9.1 bounds); `realtime-hosting` (§6.6, `Tellma.Core.AspNetCore`
only). A failed check names the feature that contributed the offending item.

## 12. Testing

| Suite | Location | Traits | Pins |
|---|---|---|---|
| Notifications unit | `test/core/Tellma.Core.Tests/Notifications/` | none | request validation (§3.1 table, every `InvalidOperationException` path), recipient dedup and `Ordinal` assignment, the renderer (ICU arguments by JSON kind, dates in the display zone and calendar, missing resource → key, malformed JSON → no arguments), `NotificationsOptions` bounds, the registries' grammar and duplicate rejection, the null publisher, the `OnCommitted` coalescing of §3.4 and §6.4 with a fake batch |
| Notifications fixture | `test/core/Tellma.Core.IntegrationTests/Notifications/` | `Category=Integration` (LocalDB, RCSI on and off) | the insert: ids contiguous from one range, inactive recipient skipped, muted mutable type skipped, non-mutable type ignores a disabled `inbox` row, a duplicate suppressed against an unread row and not against a read one, `Replace` refreshing the unread duplicate in place (same id; type, arguments, target, actor and `CreatedAt` from the request; both result sets read) and inserting for the rest, unknown recipient skipped, `OUTPUT` matches inserted rows; a fake `INotificationChannel` receives the insert's recipients identifier and its statement runs in the same round trip; the summary caps at `SummaryCountCap` and orders `Latest`; `seen`/`read`/`read-all` counts and the `inbox.changed` publish; preferences `save` synchronises, drops default rows, bumps `PreferencesTag` and refuses `CannotMute`/`UnknownType`/`UnknownChannel`/`DuplicatePreference`; an `email` deviation stored and never read by the insert; the self-scope filter on `query` and `get` (another user's id is 404); retention bands and paging; spec 0012's change-tracking write audit sees exactly the declared tables |
| Realtime unit | `test/core/Tellma.Core.AspNetCore.Tests/Realtime/` | none | `TestServer` with the in-process mode: connect succeeds for a member and is refused for a non-member and a deactivated user; groups assigned from the context; an event to a user reaches that user's connections only and never a second tenant's; empty `UserIds` reaches the tenant group; payload cap; unregistered name refused; the tracker aborts by session key, by `(tenant, subject)` and by tenant group and sends `session.ended` first; `ReadOnly` connects, `Suspended` refuses; client invocations refused; instruments asserted |
| Realtime live | `test/core/Tellma.Core.AspNetCore.IntegrationTests/Realtime/` | `Category=Integration`, `Live=true` | against a real Azure SignalR instance (`Azure:SignalR:ConnectionString` from the CI secret store): connect through the service, group delivery, `session.ended` delivery, token expiry closing the connection within `AccessTokenLifetime`; two hosts with different application names on the one instance never deliver to each other's `t{tenantId}` group |

PR runs the two unit suites and the LocalDB fixture suite; the live suite runs nightly and on
demand. Fixture entities: the shared fixture tenant of spec 0011's fixture tier with two users, one
inactive user, and a `fixture.Documents` row to target; the hub tests host `CoreFeature` alone.

## 13. Definition of done

- **Projects**: the `Tellma.Core.Abstractions.Notifications`/`.Realtime` namespaces, the
  `Tellma.Core.Notifications` and `Tellma.Core.AspNetCore.Realtime` runtime namespaces, the four
  test folders, and the realtime composition inside `UseAzureDefaults()` (`Tellma.Defaults.Azure`)
  with its `Microsoft.Azure.SignalR` pin — each project with a README, XML docs on every member,
  building and testing on Windows and Linux under warnings-as-errors, wired into `Tellma.slnx`.
- **Behavior**: §2 table, entity, stack, self-scope, registry and renderer; §3 `INotifier`, the
  insert and the replace; §4 channels and preferences; §5 the inbox; §6 the hub, groups, events,
  publisher, listeners and hosting modes; §7 retention; §8 securables and endpoints; §11 composition
  and checks — implemented and pinned by the suites of §12, green in CI.
- **Observability**: every instrument of §10.1 emitted and asserted; the log events of §10.2
  asserted in the unit suites; the alert queries present under `infra/monitoring/`.
- **CI**: unit and fixture suites on PR; the live realtime suite nightly with the Azure SignalR
  secret.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the "Library architecture — package
  naming and dependency rules" section (notifications are a namespace inside `Tellma.Core`; the hub
  and the SignalR publisher live in `Tellma.Core.AspNetCore`; the Azure SignalR pin lives in
  `Tellma.Defaults.Azure` and the Redis pin in the on-premises host), the "Hosting on Azure" section
  (Redis required only for on-premises multi-instance SignalR; Azure SignalR Service with the
  hub-only token in SaaS), and the "Observability" section (meter `Tellma.Core` for
  `tellma.notifications.*`, `Tellma.Core.AspNetCore` for `tellma.realtime.*`). Public XML docs and
  error messages reference no `docs/` paths, per repo rule.
- **Not in scope of done**: the `email` channel's sender, push channels, the `Title` column, the
  cross-instance nudge, `tellma_notifications`, and the descriptors declared by specs 0017, 0018
  and 0019.

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
7. **Capped counts and one `InboxSeenAt`** — maintained counters drift; the sibling row absorbs
   the churn (§5.1, §5.3).
8. **The self-scope is a criterion, not a seeded grant** — every member reads their own rows with
   no role; a stored grant can widen for audit (§2.2).
9. **`Notification` is `Query | Details` only** — nothing is written through `Save`; the verbs
   are inbox actions (§2.2).
10. **One hub, four server-assigned groups** — tenant isolation without a hub per tenant;
    per-user, per-subject and per-session addressing for events and closes (§6.2).
11. **Publishers never see SignalR; the null publisher is the default** — worker hosts and tests
    compose without the web package (§6.4).
12. **The composing host (directly or through `UseAzureDefaults()`) selects the hosting mode by
    delegate** — `Tellma.Core.AspNetCore` references neither Azure SignalR nor Redis; on Azure the
    deployment's application name lets the deployments of one environment share one service
    instance (§6.6).
13. **Azure closes are event-plus-token-lifetime** — no Management SDK, a five-minute bound on a
    revoked connection that carries only ids (§6.5).
14. **`core.user.added` is this spec's descriptor; every other Core type is declared beside the
    code that raises it** — one registration per key, no cross-spec ordering (§2.3).
15. **Retention runs as a spec 0019 handler on the database clock** — read 90 days, unread 365,
    paged and idempotent (§7).
16. **A duplicate is suppressed or replaced in place** — `OnDuplicate = Replace` refreshes the
    unread row, so the notice carries the latest facts without a second row (§3.3).
17. **Channels are registered, not columns** — `inbox` and `email` are every distribution's
    baseline; a module or distribution adds its own behind `INotificationChannel`; the preference
    table holds one row per `(user, type, channel)` deviation (§4).

## Review flags

1. **Read-time rendering** (§2.4): rows store keys and arguments; text is rendered in the reader's
   culture. Alternative: a `Title` column rendered in the tenant's primary language at creation,
   which makes the notifications page sortable and searchable on text. Flips if users demand text
   search over notifications or the string pack proves too heavy.
2. **Best-effort dedup, and `Replace` rides on it** (§3.3): a non-unique filtered index and a
   predicate. Alternative: a unique filtered index on `(UserId, DedupKey) WHERE ReadAt IS NULL`,
   which fails the business save when two batches race. Flips if duplicate notices are observed at
   a rate that annoys users.
3. **No push channel yet** (§4.1): the spec that ships a transport registers it. Alternative:
   register `push` now with no sender, so the preference is collected early. Flips if the push
   spec is scheduled before the next release.
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
   exists, so spec 0013's composition honours a role grant. Alternative: a registry flag that marks
   a securable criteria-only and rejects stored grants on it. Flips if an audit requirement says
   nobody, administrators included, may read another user's notifications.
10. **`notification-preferences/get` added beside `save`** (§4.3): the page needs the registered
    types and channels and the stored deviations. Alternative: fold the types into `settings/client`
    and return only deviations from `save`. Flips if the client would rather cache types under the
    settings tag.
11. **`MaxRecipientsPerRequest = 10,000` as a programming-error limit** (§3.1): a violation is a
    500, never a 422. Alternative: split automatically inside `Notify`. Flips if a legitimate
    broadcast (every user of a large tenant) is needed before a "tenant broadcast" type exists.
12. **`Notification` stack is `Query | Details` with no Excel operations** (§2.2). Alternative:
    include `Export` so a user can export their notifications. Flips on the first request for it.
13. **The composing host (directly or through `UseAzureDefaults()`) selects the hosting mode by
    delegate, and startup fails when the Azure connection string is present without it** (§6.6).
    Alternative: `Tellma.Core.AspNetCore` references `Microsoft.Azure.SignalR` and selects
    automatically. Flips if every host ends up writing the same two lines.
14. **One Azure SignalR instance per environment, shared by its deployments** (§6.6): isolation by
    application name, units pooled. Alternative: an instance per deployment. Flips if one
    deployment's connection volume needs its own unit scaling, or a hosted distribution must not
    share an access key.
