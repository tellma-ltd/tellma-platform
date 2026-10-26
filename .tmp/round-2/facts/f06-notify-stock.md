# f06 — Digest: notifications and hub (0021), email outbox (0022), core and GL reference stacks (0018)

Fact-finding digest for the round-2 design pass. Section references are to the source specs
("0021 §5.3"). Exact names are reproduced as the specs spell them.

## 1. Sources read

| Source | Lines |
|---|---|
| `docs/specs/0021-notifications-inbox-and-hub.md` | 1416 |
| `docs/specs/0022-email-outbox.md` | 631 |
| `docs/specs/0018-core-and-gl-reference-stacks.md` | 1107 |
| `.tmp/round-2/README.md` (task, spec map) | 92 |
| `.tmp/round-2-brief.md` (skimmed for needs) | 462 |

All three specs read in full.

## 2. Key facts

### 2.A Spec 0021 — notifications, inbox, hub

#### Placement (0021 §1.1)
- Contracts: `Tellma.Core.Abstractions.Notifications`, `Tellma.Core.Abstractions.Realtime`
  (in `src/core/Tellma.Core.Abstractions/`). Table types `NotificationRowList`,
  `NotificationSliceList`, `NotificationPreferenceList` in `Tellma.Core.Abstractions.TableTypes`.
- Runtime: `Tellma.Core.Notifications` (`Notifier`, `InboxService`,
  `NotificationPreferencesService`, `NotificationCatalogueProvider`, `NotificationRenderer`,
  `NotificationAccessCriteria`, delivery and retention handlers, `NullClientEventPublisher`, …). No
  SignalR reference.
- Web host: `Tellma.Core.AspNetCore.Realtime` (`TellmaHub`, `TellmaUserIdProvider`,
  `HubConnectionTracker`, `SignalRClientEventPublisher`, `TellmaRealtimeOptions`,
  `RealtimeApplicationName`, `AddTellmaRealtime`).
- Reference distribution `distributions/acme/src/Tellma.Distro.Acme.Web/` composes realtime via
  `UseAzureWebDefaults()` (`Tellma.Defaults.Azure.AspNetCore`, pins `Microsoft.Azure.SignalR`
  1.33.1). Redis (`Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.11) is pinned by an
  on-prem host, not Acme.
- Context para: "Work assigned to a user and an operator's announcements are not notifications; a
  later spec owns both." Non-goals repeat: "Assigned work and operator announcements — a later
  spec; neither is a row of `core.Notifications`." Decision 19: a stored row about either
  "duplicates its source and goes stale when the source moves on".

#### Vocabulary (0021 §1.2)
- Notification = one row of `core.Notifications`, one user. Type = registered
  `NotificationTypeDescriptor`; key = row's `Type`.
- Channel: `inbox` (rows) or any sender behind §4.4.
- Unseen = created after user last opened inbox (`InboxSeenAt`); unread = `ReadAt IS NULL`.
- Severity order `Info`, `Success`, `Warning`, `Error`.
- Condition = notification that stands while something holds; deleted by withdrawal; under
  `Ongoing` at most one row per recipient, read or not.
- Channel-only type (`Inbox = false`) writes no row.
- Client event = thin named message through the hub; ids, never tenant content.

#### Principles (0021 §1.3)
1. No extra round trip (insert rides caller's batch; hub event from post-commit hook).
2. Rows language-neutral (type key + JSON args), rendered at read time in reader's culture.
3. Predicates not logic in SQL.
4. Counters counted (TOP-capped), never maintained.
5. No audit columns; `InboxSeenAt` on `core.UserStamps`; no tenant version tag bump.
6. Hub server→client only, no content; client fetches what an event announces.

#### `core.Notifications` table (0021 §2.1)
- Non-temporal; `LOCK_ESCALATION = DISABLE`; sequence `core.sq_Notifications`; `[TableType]`.
- Columns: `Id int` (PK, >0), `UserId int` (FK `core.Users`), `Type nvarchar(64)`,
  `Severity varchar(8)` (enum as text), `ArgumentsJson nvarchar(4000) NULL` ("ICU arguments; the
  client's click data"), `TargetResource varchar(128) NULL` (securable resource id, e.g.
  `core.Export`, `acme.Document`), `TargetId bigint NULL`, `ActorUserId int NULL` (FK; null =
  system), `DedupKey nvarchar(128) NULL`, `CreatedAt datetimeoffset(3)`, `ReadAt datetimeoffset(3)
  NULL`.
- Indexes: `IX_Notifications_User (UserId, CreatedAt DESC, Id DESC) INCLUDE (ReadAt, Type,
  Severity)`; `IX_Notifications_Unread (UserId) WHERE ReadAt IS NULL`; `IX_Notifications_Dedup
  (DedupKey, UserId) INCLUDE (ReadAt) WHERE DedupKey IS NOT NULL` (non-unique);
  `IX_Notifications_Retention (ReadAt, CreatedAt)`.
- No cascade; deleting a `New` user with rows → 547 → `Fk.InUse`.

#### `Notification` entity and stack (0021 §2.2)
- `Notification : Entity<int>`, `[Table("Notifications", Schema = "core")]`, `[TableType]`,
  `[Stack(Operations = Query | Details)]`, `[DefaultSelect(...)]` = every column except `UserId`
  and `DedupKey` (still selectable). All properties server-owned. No audit set, no concurrency
  token, never written through `Save`.
- Operations: `query`, `get`, `get-by-ids` only. No save/delete/actions/Excel (review flag 12:
  no Export).
- Resource segment `notifications`; entity name / Queryex name / securable = `core.Notification`
  (`CoreResources.Notification`).
- Navigations `User`, `ActorUser`; projected via spec 0012 default related projection (`Id`, the
  `Name` group, the avatar column) — the page shows actor name and image.
- Self-scope: `NotificationAccessCriteria` returns `AccessCriterion("Read",
  FilterTree.Leaf("UserId = me()"), "self")` for every user → `Filtered` decision, no role grant.
  A stored role grant on `core.Notification × Read` widens it (audit path; none seeded; review
  flag 9).

#### Type registry and descriptors (0021 §2.3)
- `enum NotificationSeverity { Info, Success, Warning, Error }`.
- `record NotificationTypeDescriptor(string Key, string Category, bool Mutable, string?
  TargetResource, NotificationSeverity Severity = Info, bool Toast = false, bool Inbox = true)`.
- `INotificationTypeRegistry { All; Find(key); Categories }`.
- Key grammar `^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$`, ≤64 chars; `core.` reserved; modules use
  slug (`gl.`), distributions deployment slug (`acme.document.approved`). Stable for distribution
  life.
- `Category` `^[a-z][a-z0-9-]*$`, ≤32 chars; groups the preferences page. Core categories: `jobs`,
  `files`, `users`.
- `Mutable`: true = user may disable `inbox` for the type; false = cannot
  (`Notifications.CannotMute`). Other channels always user's choice.
- `TargetResource`: when set every request carries it + `TargetId`, and "the client maps the
  resource to a route". Null = neither.
- `Toast`: a row arriving while the app is open pops a toast.
- `Inbox = false`: channel-only; requires `Mutable = false`, `Toast = false`; preferences page shows
  no `inbox` switch.
- Registered via `FeatureContribution.NotificationType(descriptor)`
  (`NotificationTypeContributionItem`).
- Core catalogue (key | category | Mutable | Severity | Toast | Target | raised by | arguments):
  - `core.email.failed` | jobs | false | Error | true | `core.OutgoingEmail` | 0022 outbox handler,
    `DedupKey = 'core.email.failed'` | `{ count }`
  - `core.export.ready` | files | false | Success | true | `core.Export` | 0019 | `{ fileName,
    rowCount }`
  - `core.import.completed` | files | false | Success | true | `core.Import` | 0019 | `{ fileName,
    rowCount, errorCount }`
  - `core.import.failed` | files | false | Error | true | `core.Import` | 0019 | `{ fileName,
    errorCode }`
  - `core.job.failed` | jobs | false | Error | true | `core.Job` | 0020 | `{ handlerKey, errorCode
    }`
  - `core.job.held` | jobs | false | Warning | true | none | 0020, `DedupKey = 'core.job.held'` |
    `{ count }`
  - `core.schedule.paused` | jobs | **true** | Warning | true | `core.Schedule` | 0020, `DedupKey =
    'core.schedule.paused:<scheduleId>'` | `{ scheduleId, name, reason }`
  - `core.scheduler.gap` | jobs | false | Warning | true | none | 0020, `DedupKey =
    'core.scheduler.gap'` | `{ previousTickAt, now, heldCount }`
  - `core.user.added` | users | **true** | Info | **false** | none | 0018 `UserService.Invite`,
    `DedupKey = 'core.user.added'` | `{ tenantName, actorName }`
- Each descriptor declared once by the raising feature (0020 four, 0019 three, 0022
  `core.email.failed`, 0021's `CoreFeature` `core.user.added`).

#### Rendering (0021 §2.4)
- `INotificationRenderer.Render(type, argumentsJson, targetResource, targetId,
  LocalizationContext)` (scoped, server side).
- Resource name `Notification_<Key>` with `.`→`_` (`Notification_core_export_ready`), in declaring
  package's `Resources/Strings.resx`; ICU named args from top-level JSON members (ISO 8601 strings
  → `DateTimeOffset`).
- Missing resource renders the key; malformed JSON renders with no args
  (`NotificationArgumentsInvalid`).
- **Summary never calls the renderer**: `InboxSummary.Latest` and `Running` carry raw rows; "the
  SPA renders them from spec 0013's string pack, a job's progress as spec 0020 §3.3 renders it".
  Server renderer serves MCP and channels.
- Notifications page shows rendered text as a **client-side column**; no sort/filter on rendered
  text (review flag 1).

#### Catalogue endpoint (0021 §2.5)
- `record NotificationCatalogue(string Version, IReadOnlyList<NotificationTypeDescriptor> Types,
  IReadOnlyList<NotificationChannelDescriptor> Channels)`; `Version` =
  `DeploymentVersions.DistributionVersion` (spec 0011).
- `GET /api/notification-catalogue?v=<version>` — tenantless, anonymous, mapped by 0016 §9.4
  beside the string pack; platform JSON options (0016 §3.1); string pack caching contract (0013
  §10.4), immutable when `v` equals `Catalogue.Version`.
- SPA reads `Severity`, `Toast`, `Category`, `Inbox` from it. Preferences endpoint returns
  deviations only.
- Review flag 10 alternative: fold into `settings/client` under `settings` tag.

#### `INotifier` and requests (0021 §3.1)
- `enum DuplicatePolicy { Suppress, Replace, Ongoing }`.
- `record NotificationRequest(string Type, IReadOnlyList<int> RecipientIds, object? Arguments,
  string? TargetResource, long? TargetId, int? ActorUserId, string? DedupKey, DuplicatePolicy
  OnDuplicate = Suppress, bool InboxOnly = false, NotificationSeverity? Severity = null)`.
- `record WithdrawRequest(string DedupKey, IReadOnlyList<int>? RecipientIds = null)` (null = every
  recipient).
- `INotifier { void Notify(IDataBatch, requests); Task NotifyAsync(requests); void
  Withdraw(IDataBatch, requests); Task WithdrawAsync(requests); }` (scoped).
- `NotificationsOptions` (`Tellma:Notifications`): `MaxRecipientsPerRequest = 10000`,
  `MaxArgumentsChars = 4000`, **`SummaryCountCap = 100`** (max 1,000), **`SummaryLatestCount =
  10`** (max 50), `ReadRetention = 90 days`, `UnreadRetention = 365 days`, `RetentionPageSize =
  1000` (max 10,000), `DeliverySliceSize = 250` (max 250) (bounds 0021 §9.1).
- Duplicate semantics: same `DedupKey` for the recipient, matched without regard to `Type`.
  `Suppress` (default) — nothing while an unread duplicate exists. `Replace` — the unread duplicate
  is rewritten in place (type, severity, args, target, actor, fresh `CreatedAt`, same id, still
  unread) so it "resurfaces unseen and on top". `Ongoing` — as Suppress but read rows count too;
  raiser withdraws when the condition clears. A cleared row returns at the next raise while the
  condition holds. `Replace`/`Ongoing` require a `DedupKey`.
- Key convention: record activity `<resource>:<id>` (`acme.Document:42`); else the type key with
  optional anchor (`core.schedule.paused:42`).
- `InboxOnly = true`: inbox row only, no delivery job (used by failure notices).
- `Severity` override per request (e.g. Warning at 90%, Error at 100%).
- Violations are `InvalidOperationException` (500), never 422 — requests are composed by code, not
  clients.
- Withdraw deletes every row with the key, read or not, whatever its type.

#### Statements, after-commit (0021 §3.2–§3.4)
- Insert predicates: recipient `IsActive = 1`; mute check for mutable types (`Channel = 'inbox'
  AND Enabled = 0`); duplicate check. Ids via `sp_sequence_get_range` inside the statement.
- Delivery enqueue: one `core.notification-delivery` job per qualifying slice (active recipient
  with some non-inbox channel enabled by preference, else `DefaultEnabled`). Jobs run as system
  user (`RunAsUserId = 1`), `RequestedById = NULL`.
- §3.4: one `OnCommitted` callback per batch publishes **one** `ClientEvent("inbox.changed",
  userIds, null)` for the union of inserted/refreshed/withdrawn user ids; nudges the local
  `TenantPoller` when delivery jobs were enqueued. Failures logged `InboxEventFailed`.
- §3.5: spec 0015 `PersistContext`/`ActionContext` expose `Notify(request)`/`Withdraw(request)`.

#### Channels and preferences (0021 §4)
- `record NotificationChannelDescriptor(string Key, bool DefaultEnabled)`;
  `INotificationChannelRegistry { All; Find }`; registered via
  `FeatureContribution.NotificationChannel(descriptor)`. Key grammar `^[a-z][a-z0-9-]*$`, ≤16
  chars; label resource `NotificationChannel_<Key>`.
- Baseline: `inbox` (`DefaultEnabled = true`), `email` (`DefaultEnabled = false`; sender =
  spec 0018's `EmailNotificationChannel`, delivering per 0022 §4). Modules/distros may add `push`,
  `sms`, `whatsapp`.
- `core.NotificationPreferences` (§4.2): not an entity, not in Queryex, no UDTT. Columns `UserId
  int` (PK part, FK cascade), `Type nvarchar(64)` (PK), `Channel varchar(16)` (PK), `Enabled bit`.
  PK `(UserId, Type, Channel)`. Missing row = channel `DefaultEnabled`; **deviations only**.
- `NotificationPreference { string Type; string Channel; bool Enabled; }` (row shape, always the
  caller's).
- `NotificationPreferencesService` `[ApiRoute("notification-preferences")]`:
  - `get` — `[ApiAction("get", MemberOnly = true, Idempotent = true, Mutation = false)]`; returns
    stored deviations (unknown keys filtered out). Labels from string pack: `NotificationType_<Key>`
    and `NotificationChannel_<Key>` (`.`→`_`).
  - `save` — `[ApiAction("save", MemberOnly = true)]`; body = caller's **complete set** (absent
    rows revert to default). 422 codes with paths `[i].type` / `[i].channel` / `[i].enabled`:
    `Notifications.UnknownType`, `Notifications.UnknownChannel`,
    `Notifications.DuplicatePreference`, `Notifications.CannotMute`. Rows equal to default dropped.
    `SqlOptions.ForCaller` → epilogue bumps caller's `PreferencesTag`. Returns same shape as `get`.
  - The `PreferencesTag` bump moves spec 0014's cached `UserProfile` and the SPA's `preferences`
    wire tag "so a second tab learns of the change on its next request" (no hub event for it).
  - NOTE two resource names per type: the renderer uses `Notification_<Key>` (message text, §2.4);
    the preferences page uses `NotificationType_<Key>` (type label, §4.3).
- Channel seam (§4.4): `record NotificationRecipient(int UserId, string Name, string? Email,
  string? ContactEmail, string? ContactMobile, LocalizationContext Localization)`; `enum
  TestNotificationResult { Sent, NoContactAddress, NotConfigured }`; `INotificationChannel { Key;
  DeliverAsync(IOpenWriteHost, NotificationRequest, recipients); Task<TestNotificationResult>
  SendTestAsync(); }`. `SendTestAsync` serves 0018 §3.5 `me/test-notification`; address from
  `ConnectedUser.Profile` (`ContactEmail ?? Email` for email, `ContactMobile` for SMS); transport
  failure = `DependencyUnavailableException`.
- Channels have no dedup (review flag 15): a user enabling `email` for `core.job.held` gets an
  email per raise.
- Delivery handler (§4.5): `[JobHandler("core.notification-delivery", BatchSize = 4, MaxAttempts =
  12, InboxOnlyFailureNotices = true)]`; `record NotificationDeliveryArguments(Type, Severity,
  ArgumentsJson, TargetResource, TargetId, ActorUserId, RecipientIds)`. Reads `core.Users` (`Id`,
  `Name`, `Email`, `ContactEmail`, `ContactMobile`, `PreferredLanguage`, `PreferredCalendar`,
  `PreferredTimeZone`, `IsActive = 1`) and preferences at run time.

#### Inbox (0021 §5)
- `InboxSeenAt datetimeoffset(3) NULL` on spec 0014's `core.UserStamps`, written only by
  `inbox/seen`. Unseen = `CreatedAt > InboxSeenAt` (all when null).
- `record InboxSummary(int Unseen, NotificationSeverity? MaxUnseenSeverity, int Unread,
  IReadOnlyList<Notification> Latest, IReadOnlyList<InboxRunningJob> Running)` — counts capped at
  `SummaryCountCap`; `MaxUnseenSeverity` over the same capped (newest) unseen rows, null when none;
  `Latest` and `Running` ≤ `SummaryLatestCount` each, newest first.
- `record InboxRunningJob(int Id, string HandlerKey, JobStatus Status, byte? ProgressPercent,
  string? ProgressKey, string? ProgressArgumentsJson, DateTimeOffset? CancelRequestedAt,
  DateTimeOffset? StartedAt, DateTimeOffset CreatedAt)` — the caller's own 0020 job, status in
  `Pending | Running | Held`, `ScheduledFor IS NULL` (no cron occurrences), `RequestedById = me`.
- `InboxService` `[ApiRoute("inbox")]`, all `MemberOnly`:

  | Action | Attributes | Body | Result |
  |---|---|---|---|
  | `summary` | `Idempotent = true, Mutation = false`; `NoActivityStampMetadata` | none | `InboxSummary` |
  | `seen` | `Idempotent = true` | none | `AffectedResult` (`Count` = 1) |
  | `read` | `Idempotent = true` | `IdsRequest` (0016 §3.3) | `AffectedResult` (rows newly marked) |
  | `read-all` | `Idempotent = true` | none | `AffectedResult` |
  | `clear` | `Idempotent = true, Destructive = true` | `IdsRequest` | `AffectedResult` (rows deleted) |
  | `clear-read` | `Idempotent = true, Destructive = true` | none | `AffectedResult` |

- All are POST under `/{tenantId}/api/web/` (§8): `inbox/summary`, `inbox/seen`, `inbox/read`,
  `inbox/read-all`, `inbox/clear`, `inbox/clear-read`, `notification-preferences/get`,
  `notification-preferences/save`; plus stack endpoints `notifications/query`, `notifications/get`,
  `notifications/get-by-ids`.
- Each mutating verb publishes `inbox.changed` to `[callerId]` post-commit (other tabs/devices
  refresh). Foreign ids silently ignored (no 403/404). There is **no "mark unread"** verb.
- "Clear" = hard delete of the user's own rows (Decision 22).
- Badge: `99+` past the cap.

#### Client protocol (0021 §5.4) — what the bell must do
- Call `inbox/summary` at load, on every `inbox.changed` and `job.changed`, and on hub reconnect.
- Badge shows `Unseen`, attention colour while `MaxUnseenSeverity` is `Warning` or `Error`.
- Dropdown: `Running` under an "In progress" heading, then `Latest`.
- Toast rule: pop for each `Latest` row whose type has `Toast` and whose `CreatedAt` > newest
  `CreatedAt` of the previous summary's `Latest`; if that list was empty every such row qualifies;
  the **first summary after load pops none**. A replaced row pops again; a row resurfacing after
  clears/withdrawals does not.
- `inbox/seen` when the bell opens; `inbox/read` with one id on item click, then navigate to the
  route mapped from `TargetResource` + `TargetId` (none for target-less); `inbox/clear` with one id
  from an item menu; `inbox/read-all` and `inbox/clear-read` from dropdown buttons.
- "View all notifications" → the standard search page over `core.Notification` (`query`, default
  select, `CreatedAt desc`), standard filters/columns over raw columns + client-rendered text
  column.
- Preferences page = `get`/`save` over the catalogue: types by registered channel, grouped by
  `Category`, `inbox` switch locked on for non-mutable, absent for channel-only.
- Pages listed (§8): the bell (summary dropdown), Notifications (standard search), Notification
  preferences.

#### Hub (0021 §6)
- `TellmaHub` mapped by 0016 `MapTellma` at **`/{tenantId:int:min(1)}/hub`** on 0011's `Hub` route
  group. Auth: session cookie (`TellmaPolicies.Web`), `MemberEndpointMetadata`,
  `TenantEndpointMetadata(Hub, IsMutation = false)`, `Origin`/`Sec-Fetch-Site` check,
  cross-origin negotiate refused, **no `Tellma-Client` header** (a browser cannot decorate the
  WebSocket handshake).
- Negotiate/connect pass tenant middleware + `ITenantAccessGuard.EnsureAccessAsync`; non-members
  and deactivated users → `TenantNotFoundException` (404); `ReadOnly` tenant accepted;
  `Suspended`/`Provisioning`/`Retired` refused.
- `OnConnectedAsync` reads `Tenant.Id`, `UserId`, `Subject`, `SessionHandle` (handle of the
  `catalog.Sessions` row, 0011 §7.4). No client-invocable methods (`HubException`). JSON protocol
  only; `MaximumReceiveMessageSize = 4096`; `EnableDetailedErrors = false`.
  `TellmaUserIdProvider` returns identity `sub`.
- Groups (§6.2), server-assigned only: `t{tenantId}` (all connections of tenant),
  `t{tenantId}.u{userId}` (user-addressed events), `t{tenantId}.s{subject}` (access revocation),
  `x{handle}` (one BFF session across tenants). `Clients.User(sub)` never used for events.
- **One connection per (tab, tenant)** is implied: the hub URL is tenant-scoped, so a tab (or a
  leader tab, per the brief) needs one connection per tenant it follows.
- Client event catalogue (§6.3): wire envelope `record ClientEventEnvelope(string Name, int
  TenantId, object? Payload)` delivered on the **single client method `"event"`**. Server type
  `record ClientEvent(string Name, IReadOnlyList<int> UserIds, object? Payload)` (empty = whole
  tenant). Registered via `FeatureContribution.ClientEvent(name)`; name grammar = type-key grammar.

  | Event | Payload | Publisher | Recipients |
  |---|---|---|---|
  | `inbox.changed` | none | `INotifier`, inbox verbs | affected users |
  | `job.changed` | `{ jobId }` | spec 0020 (claim, progress renewal, completion, job actions) | job's `RequestedById` |
  | `cache.changed` | `{ tag }` | spec 0013 epilogue, per `settings` or `entity:<Name>` bump | every connected user of tenant |
  | `session.ended` | `{ reason }` | hub listeners | closing connections |

  - `session.ended` reasons: `session-terminated`, `access-revoked`, `tenant-unavailable`.
  - Payload cap `MaxEventPayloadBytes` = 4096.
  - **No `notification.*` event names exist**; there is no event for preferences, permissions or
    user-profile changes (only `cache.changed` tags `settings` and `entity:<Name>`).
- `IClientEventPublisher { void Publish(IDataBatch?, ClientEvent); Task PublishAsync(ClientEvent);
  }` — with a batch, post-commit; coalesces duplicate triples. Null publisher on worker hosts →
  "events are an optimisation, never a dependency".
- Listeners (§6.5), `HubConnectionTracker`: `SessionsTerminatedAsync` → `session.ended
  {session-terminated}` to `x{handle}` then close; `TenantAccessRevokedAsync` (0018's
  `UserService` after deactivation commits) → `{access-revoked}` to `t{tenantId}.s{subject}`;
  `OnStateChangedAsync` to `Suspended`/`Retired` → `{tenant-unavailable}` to `t{tenantId}`;
  `ReadOnly`/`Active`/`Provisioning` → nothing.
- On Azure SignalR the client "disconnects itself on `session.ended`"; the service closes
  connections whose token expired (`AccessTokenLifetime` 5 min, `CloseOnAuthenticationExpiration =
  true`) → a revoked user may receive thin events ≤5 min (review flag 5). A tenant deactivation
  never ends the BFF cookie.
- Hosting modes (§6.6): in-process (default), Redis backplane (on-prem multi-instance; sticky
  sessions; channel prefix = `DeploymentIdentity.DeploymentId`), Azure SignalR Service (SaaS, when
  `Azure:SignalR:ConnectionString` present; `ApplicationName` =
  `RealtimeApplicationName.From(deployment)`, `-`→`_`). Startup check `realtime-hosting`.
- `TellmaRealtimeOptions` (`Tellma:Realtime`, §9.2): `Enabled = true` (false: no hub mapped),
  `KeepAliveInterval = 15s`, `ClientTimeoutInterval = 30s` (≥ 2× keep-alive),
  `AccessTokenLifetime = 5 min`, `MaxEventPayloadBytes = 4096`, `ExpectedInstances = 1`.

#### Retention (0021 §7)
- `core.notification-retention` handler, `LeaseSeconds = 600`; built-in schedule
  `BuiltInSchedule(2, "core.notification-retention", "30 3 * * *")`, system user, after
  `core.job-retention`. Read rows deleted after `ReadRetention` (90 d) by `ReadAt`; unread after
  `UnreadRetention` (365 d) by `CreatedAt`. DB clock.

#### Securables (0021 §8)
- Only `core.Notification × Read` (from the stack), self-scope criterion. Inbox and preferences
  services are `MemberOnly`, no securable. Nothing sensitive.
- MCP: `tellma_query`/`tellma_get` over `core.Notification`; `tellma_notifications` reserved.

#### Telemetry (0021 §10)
- Meter `Tellma.Core`: `tellma.notifications.created|suppressed|replaced|withdrawn|
  delivery_enqueued|retained|inbox.summaries|inbox.read|inbox.cleared|preferences.saved`.
- Meter `Tellma.Core.AspNetCore`: `tellma.realtime.connections|connects|closes`;
  `tellma.realtime.events` (0016). Alert on `connects{refused}` per minute ("a stale SPA or an
  attack").

#### Startup checks (0021 §11)
- `notification-types` (every non-null `TargetResource` is a registered securable or stack
  resource "so the client can route it"), `notification-channels`, `client-events`,
  `notification-options`, `realtime-hosting`.

#### Relevant review flags (0021)
- RF1 read-time rendering (alternative: `Title` column). RF10 catalogue tenantless GET
  (alternative: fold into `settings/client`). RF12 no Export on notifications. RF15 channels no
  dedup. RF17 `Running` rides summary capped at `SummaryLatestCount` (alternative: `jobs/running`
  with its own cap and event).

### 2.B Spec 0022 — email outbox (admin surface focus)

#### Placement and principles (0022 §1)
- Contracts `Tellma.Core.Abstractions.Email` (+ `.TableTypes`); runtime `Tellma.Core.Outbox`
  (`EmailOutbox`, `OutgoingEmailService`, `OutgoingEmailHandler`, `OutgoingEmailDeliveryHandler`,
  `OutgoingEmailRetentionHandler`, `OutboxStatements`).
- Settled states: `Sent`, `Sandboxed`, `Rejected`, `Failed`, `Cancelled`; unsettled: `Pending`,
  `Sending`. Orphaned = unsettled with `JobId` null or job not live (§1.2).
- Principles (§1.3): row is the truth; interrupted send fails closed (`Email.Interrupted`, never
  auto re-sent); **no `save` endpoint** (rows only via enlisted pipeline); `retry`/`cancel` stamp
  the row.
- No version-tag attribute; no tenant tag bump → **no `cache.changed` for the outbox**; the admin
  page has no live refresh event.

#### `OutgoingEmail` entity (0022 §2.1)
- `enum OutgoingEmailState { Pending, Sending, Sent, Sandboxed, Rejected, Failed, Cancelled }`
  (`varchar(9)`).
- `enum EmailDeliveryStatus { Deferred = 1, Delivered, Dropped, Failed, Bounced, SpamReported }`
  (weak→strong; `varchar(12)`).
- `OutgoingEmail : TopLevelEntity, IJobEntity`, table `core.OutgoingEmails`, sequence
  `core.sq_OutgoingEmails`; `[Stack(Operations = Query | Details | Delete | Enlist)]`,
  `[ApiResource(Mcp = ReadOnly)]`.
- `[DefaultSelect]`: every member except `TextBody`, `HtmlBody`, `Cc`, `Bcc`, `ProviderMessageId`,
  `LastProviderEventId` (the details page must request bodies explicitly).
- Columns: `JobId int?` (FK `ON DELETE SET NULL`, `UX_OutgoingEmails_JobId`), `State`
  (server-owned); write-once `Audience` (`EmailAudience`, `varchar(8)`), `From nvarchar(512)?`,
  `ReplyTo nvarchar(512)?`, `To nvarchar(max)` (RFC 5322 `"Name" <address>, …`), `Cc`, `Bcc`,
  `Subject nvarchar(500)`, `TextBody nvarchar(max)`, `HtmlBody nvarchar(max)?`, `RecipientUserId
  int?` (nav `RecipientUser`), `RegardingEntity varchar(128)?`, `RegardingId long?`, `SendAfter
  datetimeoffset(3)?`; server-owned `AttemptCount int`, `SentAt`, `ProviderMessageId
  nvarchar(255)`, `ExpectsDeliveryEvents bit`, `ErrorCode nvarchar(64)` (an `Email.*` key),
  `ErrorDetail nvarchar(1024)` (provider text), `DeliveryStatus`, `DeliveryAt`, `DeliveryRecipient
  nvarchar(255)`, `DeliveryReason nvarchar(512)`, `LastProviderEventId nvarchar(100)`; plus audit
  columns (`CreatedById` = acting user, a job's run-as user).
- Child `OutgoingEmailAttachments` (`[NotMapped, MaxChildren(20)]`): `OutgoingEmailAttachment :
  ChildEntity` — `OutgoingEmailId` (`[ParentKey]`), `FileName nvarchar(255)`, `ContentId
  nvarchar(255)?` (inline `cid:`), `FileId int?` with `[BlobReference("email-attachment",
  BlobPreset.Attachment, ReadAccess = BlobReadAccess.OwnerRead)]` (nav `File`). Content type and
  size come from the blob row; download authorises through the parent's `Read` grant (0017 §5.1).
- Indexes: `IX_OutgoingEmails_State (State, CreatedAt)`; `IX_OutgoingEmails_Regarding
  (RegardingEntity, RegardingId) WHERE RegardingEntity IS NOT NULL`; `IX_OutgoingEmails_Recipient
  (RecipientUserId, CreatedAt DESC) WHERE RecipientUserId IS NOT NULL`.
- Queryex: root `core.OutgoingEmail` with navs `Job`, `RecipientUser`, `CreatedBy`, `ModifiedBy`;
  child root `core.OutgoingEmailAttachment` with `File`, `OutgoingEmail`.

#### States table (0022 §2.3) — what an administrator reads
- `Pending` (committed; `SendAfter` or job `DueAt` says when), `Sending` (still `Sending` after its
  job ended = interrupted), `Sent` (events follow when `ExpectsDeliveryEvents = 1`), `Sandboxed`
  (never reached a real recipient), `Rejected` (`ErrorCode` says why), `Failed`
  (`Email.AttemptsExhausted` or `Email.Interrupted`; `retry` resends), `Cancelled`.
- `DeliveryStatus` is independent of `State` (e.g. `Sent` + `Bounced`).

#### Enqueue (0022 §3) and email channel (§4)
- `IEmailOutbox.EnqueueAsync(IWriteHost host, IReadOnlyList<EmailEnqueueRequest>)` →
  `EnlistedSave<OutgoingEmail>`. `record EmailEnqueueRequest(EmailMessage Message,
  BlobAttachments?, int? RecipientUserId, string? RegardingEntity, long? RegardingId,
  DateTimeOffset? SendAfter, string Source = "api")`.
- Validation codes: `Email.InvalidMessage`, `Email.TooManyRecipients`, `Email.TooLarge`; blob codes
  `Blob.TooLarge`, `Blob.StagingQuotaExceeded`, `Blob.UnsupportedType`, `Blob.NotAttachable`,
  `Blob.DuplicateReference`.
- Each row gets one `core.outgoing-email` job (`RunAsUserId = SystemUserId`, `RequestedById =
  null`, `DueAt = SendAfter`) → **outbox jobs never appear in a user's `InboxSummary.Running`**.
- Email channel (§4): per-recipient request, `To` = `ContactEmail ?? Email`, `Audience =
  Internal`, `RecipientUserId`, regarding = notification's target, `Source = "notification"`.
  Subject/bodies from `ServerStrings` `Email_Notification_Subject`, `Email_Notification_Text`,
  `Email_Notification_Html` with `tenantName`, `text`, `link`; **`link` =
  `<Tellma:PublicOrigin>/<tenantId>`** (tenant root, not a deep link; review flag 1: "no deep-link
  contract exists"). No attachments.

#### Dispatch (0022 §5)
- `[JobHandler("core.outgoing-email", BatchSize = 25, LeaseSeconds = 120, MaxConcurrency = 1,
  MaxAttempts = 12, RetryBaseSeconds = 60, RetryMaxSeconds = 7200, InboxOnlyFailureNotices =
  true)]`.
- Settle outcomes: `Sent`, `Sandboxed`, `Rejected` (`Email.Rejected`), unsendable → `Rejected`
  (`Email.AttachmentMissing`), transient with attempts left → `Pending` (`Email.TransientFailure`),
  exhausted → `Failed` (`Email.AttemptsExhausted`).
- Notify: any `Rejected`/`Failed` → `core.email.failed` to
  `IAdministratorDirectory.GetAdministratorIdsAsync()`, args `{ count: n }`, target
  `core.OutgoingEmail`/row id, `InboxOnly = true`, `DedupKey = "core.email.failed"`, `OnDuplicate =
  Replace` → one unread admin notice, latest count and row. (So clicking the notice routes to one
  outgoing email's details page, not a filtered list.)

#### Delivery events (0022 §6)
- Webhook path, `OwnerKey = "outbox"`; drops `Opened`, `Clicked`, `Other`; monotonic strongest
  status; applies to `Active` and `Suspended` tenants; `ReadOnly` → 503 for redelivery.

#### Verbs (0022 §7.1)
- `OutgoingEmailService : EntityService<OutgoingEmail>` with `RetryAsync` (`[EntityAction]
  "retry"`) and `CancelAsync` (`[EntityAction] "cancel"`, `Destructive`).
- `retry` — securable `core.OutgoingEmail × Retry`; precondition orphaned, or `Failed` with no live
  job; else `Email.NotRetryable`. Effect `State = 'Pending'`, error and delivery columns cleared,
  `AttemptCount` kept, new job. `Rejected` is **not** retryable (non-goal).
- `cancel` — `core.OutgoingEmail × Cancel`, `Destructive`; precondition `Pending`, else
  `Email.NotCancellable`.
- `delete` — `core.OutgoingEmail × Delete`, `Destructive`; precondition settled or orphaned, else
  `Email.NotDeletable`; the erasure path.
- Retention (§7.3): `core.outgoing-email-retention`, built-in schedule id 7, `"45 3 * * *"`; pages
  of 500 ids; `Retention` 180 days.

#### Admin surface (0022 §8)
- Securables `core.OutgoingEmail × Read | Delete | Retry | Cancel`, none sensitive, no self-scope,
  no seeded grant beyond administrators' `*`. Members do not see mail addressed to them.
- Endpoints (0016 projection): `outgoing-emails/query`, `get`, `get-by-ids`, `delete`,
  `delete-by-query`, `retry`, `cancel`. Attachments via 0017's blob endpoint.
- Page "Outgoing emails": standard search, default order `CreatedAt DESC`; filters on state,
  delivery status, `To contains`, created by, regarding entity and id, date range. Details: envelope,
  state and delivery columns with timestamps, error code and detail, `AttemptCount`, the job link,
  the attachments, and **the HTML body in a sandboxed `iframe` with a text toggle**. Actions: the
  three verbs.
- MCP read-only.

#### Options and resources (0022 §9)
- `OutboxOptions` (`Tellma:Outbox`): `Retention = 180 d` (1 d–10 y), `MaxMessageBytes = 7 MiB`
  (1 KiB–20 MiB), `MaxRecipients = 50` (1–1000).
- Resources `Notification_core_email_failed`, `NotificationType_core_email_failed`, `Email_<Code>`
  (`Email_InvalidMessage` … `Email_NotDeletable`) in Core's `Strings`.
- Blob kind `email-attachment` with `Thumbnail = null`.
- Meter `tellma.outbox.enqueued{source}`, `settled{state}`, `latency`, `delivery_updates{result}`,
  `retained`.

### 2.C Spec 0018 — core and GL reference stacks

The `User`, `Role`, `RoleMembership`, `Permission` entities, their columns, enums
(`UserKind { Individual, System, Service }`, `UserState { New, Invited, Joined }`,
`InviteStatus { Invited, Reinvited, Active }`, `Gender { Female, Male }`), `UserStamps`,
`UserPreferences`, role-save validation codes and the securables registry are **spec 0014's** and
are digested in f03 §2.22–§2.29 (not restated here). `MeResult`, `UserProfileView`,
`AccessSummary` are spec 0016's (f04). 0018 adds the services, actions and rules below.

#### Placement and composition (0018 §1.1–§1.3)
- `Tellma.Core.Abstractions.Identity`, `.Access` (contracts, result records);
  `Tellma.Core.Identity`, `Tellma.Core.Users`, `Tellma.Core.Access`, `Tellma.Core.Notifications`
  (runtime; `EmailNotificationChannel` lives in `Tellma.Core.Notifications`).
- GL: `src/module/gl/Tellma.Module.Gl.Abstractions/` (namespace `Tellma.Module.Gl`; `Center`,
  `CenterType`, `GlModule`, `GlResources`, `GlValidationCodes`, `IGlSampleCenters`,
  `SampleCenter`, `SampleCenterName`) and `src/module/gl/Tellma.Module.Gl/` (`CenterService<TCenter>`,
  `GlFeature`, `GlSampleCentersStep`, `DefaultGlSampleCenters`, `Resources/Strings.resx`). The
  module **never references `Tellma.Core`** (unit test asserts the closure).
- Reference distribution projects named here: `distributions/acme/src/Tellma.Distro.Acme/`
  (composition library, gains `Tellma.Module.Gl` + `.Abstractions`), `Tellma.Distro.Acme.Web/`,
  `Tellma.Distro.Acme.Migrator/` (migrations live here, §7);
  tests `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests`. The Client project is only
  mentioned in passing (§6.3: "`pnpm install` in the Client project"); its structure is spec
  0011's (see f01 lines 45–60: `Tellma.Distro.Acme.Client/` esproj, `src/` "arrives with the UI
  phase", Playwright `test/Tellma.Distro.Acme.E2E`).
- `CoreFeature` registers `Entity<User, UserService<User>>()`, `Entity<Role, RoleService<Role>>()`,
  `ApiService<AccessService>()`, `Singleton<IIdentityServerClient, IdentityServerClient>()`,
  `Singleton<IAccessCriteriaProvider, UserAccessCriteria>()`, `Scoped<INotificationChannel,
  EmailNotificationChannel>()`; binds `IdentityServerClientOptions` from `Tellma:Identity`.
- Leaf substitution: `tellma.UseEntity<User, MyUser>()`, `UseEntity<Role, MyRole>()`,
  `UseEntity<Center, MyCenter>()`; the platform closes `UserService<MyUser>` etc.
- Acme composition (§1.2): `public static void Compose(TellmaBuilder tellma) =>
  tellma.UseAzureDefaults().AddFeature<GlFeature>().Languages(["en", "ar"]);` — invoked by both
  Web and Migrator hosts. **Only languages are set**; calendars/locale are not.

#### Identity-server client (0018 §2)
- `IIdentityServerClient { InviteAsync; GetInvitationDeliveryAsync; CreateServiceAccountAsync;
  DeleteServiceAccountAsync }` — machine-to-machine, chunked at 1,000, never in a transaction.
- `record IdentityInvitation(Email, DisplayName?, Locale?, Gender?, ReturnUrl?, bool ExistingOnly =
  false)`; `record IdentityInvitationResult(Email, Subject?, InviteStatus? Status, Error?)`.
- `record IdentityDeliveryStatus(Subject, IdentityDeliveryState State, bool ExpectsDeliveryEvents,
  SentUtc?, UpdatedUtc?, Reason?)`; `enum IdentityDeliveryState { NotFound, Pending, Sent,
  Delivered, Bounced, Complained, Rejected, Abandoned, Accepted }` — the user page's delivery
  drill-down vocabulary.
- `record IdentityServiceAccount(ClientId, ClientSecret)`.
- `IdentityServerClientOptions`: `Authority` (required, https except Development), `ClientId`
  (`<slug>-svc`, set by platform), `ServiceClientSecret` (required).
- Failures: identity server unreachable → `DependencyUnavailableException` (503, `Retry-After:
  1`); identity server refusing the distribution → `InvalidOperationException` (500 + trace id).
  After ≥1 chunk, a failure returns the prefix (§2.3).

#### Users stack: operations and routes (0018 §3.1)
- `public class UserService<TUser> : EntityService<TUser>, IEnlists<TUser> where TUser : User`.
- Result/request records (`Tellma.Core.Abstractions.Access`):
  - `InviteResult(int Id, InviteStatus? Status, string? Error)`
  - `CredentialsResult(int Id, string ClientId, string ClientSecret)`
  - `UserPreferencesRequest(int UserId)`; `UserPreferencesArguments(IReadOnlyList<KeyValueItem>
    Items)`; `UserPreferenceKeysArguments(IReadOnlyList<string> Keys)`
  - `MeSaveRequest(string Name, string? Name2, string? Name3, int? ImageId, int? SignatureId,
    string? PreferredLanguage, string? PreferredCalendar, string? PreferredTimeZone, Gender? Gender,
    string? ContactEmail, string? ContactMobile, DateTimeOffset ModifiedAt)` — "the self-editable
    members and the expected stamp" (11 members + stamp; a full replacement, not a patch).
  - `InvitationStatus(int Id, UserState State, InviteStatus? InviteStatus, string?
    LastInviteError, IdentityDeliveryStatus? Delivery)`
  - `KeyValueItem(string Key, string Value)`
  - `PreferencesResult(IReadOnlyDictionary<string,string> Preferences, string PreferencesTag)`
  - `UserPreferencesResult(IReadOnlyDictionary<string,string> Preferences)` (another user's bag)
  - Standalone `[TableType("UserInvitationOutcomeList")] UserInvitationOutcome` (write-back row).
- Routes (all POST under `/{tenantId}/api/web/`):

  | Route | Annotation | Body → result | Securable |
  |---|---|---|---|
  | `users/invite` | `[EntityAction("invite", Description = "Invite users through the identity server")]` | `IdsRequest` → `list<InviteResult>` | `core.User × Invite` (filtered, sensitive) |
  | `users/issue-credentials` | `[EntityAction("issue-credentials", Action = "Credentials", SingleTarget = true, …)]` | `IdRequest` → `CredentialsResult` | `core.User × Credentials` (filtered, sensitive) |
  | `users/invitation-status` | `[ApiAction("invitation-status", Action = "Read", Idempotent = true, Mutation = false)]` | `IdsRequest` → `list<InvitationStatus>` | `core.User × Read` |
  | `users/me` | `[ApiAction("me", MemberOnly = true, Idempotent = true, Mutation = false)]` | — → `MeResult` | member |
  | `users/me/save` | `[ApiAction("me/save", MemberOnly = true)]` | `MeSaveRequest` → `MeResult` | member |
  | `users/me/preferences/set` | `[ApiAction("me/preferences/set", MemberOnly = true)]` | items (path `items[i].key`) → `PreferencesResult` | member |
  | `users/me/preferences/delete` | `[ApiAction("me/preferences/delete", MemberOnly = true)]` | keys → `PreferencesResult` | member |
  | `users/preferences/get` | `[ApiAction("preferences/get", Action = "Preferences", Idempotent = true, Mutation = false)]` | `{ userId }` → `UserPreferencesResult` | `core.User × Preferences` (not sensitive) |
  | `users/preferences/set` | `[EntityAction("preferences/set", Action = "Preferences", SingleTarget = true)]` | `IdRequest<UserPreferencesArguments>` → `UserPreferencesResult` | `core.User × Preferences` |
  | `users/preferences/delete` | `[EntityAction("preferences/delete", Action = "Preferences", SingleTarget = true)]` | `IdRequest<UserPreferenceKeysArguments>` → `UserPreferencesResult` | `core.User × Preferences` |
  | `users/me/test-notification` | `[ApiAction("me/test-notification", MemberOnly = true)]` | `{ channel }` → `TestNotificationResult` | member |

- Standard projection otherwise: `query`, `get`, `get-by-ids`, `save`, `delete`,
  `delete-by-query`, `activate`, `deactivate`, Excel ops (0019 §1.2). "This service adds hooks,
  never operations." Securables `core.User × Read | Save | Delete | Activate | Invite | Preferences
  | Credentials`, `FilterRoot = core.User`.
- `UserAccessCriteria` (`Resource = core.User`): `("Read", Leaf("Id = me()"), "self")`.

#### Users hooks and validation (0018 §3.2)
- Own-row read via criterion; **no criterion covers `Save`**: a member without a `Save` grant is
  refused (403) on the admin `save` of their own row; self-service goes through `me/save`.
- `PreprocessAsync`: `Email`, `ContactEmail` trimmed, lower-cased, null when empty; names and
  `ContactMobile` trimmed; `PreferredLanguage` canonical BCP 47 casing, extensions stripped;
  `PreferredCalendar` lower-case.
- `ValidateAsync` codes (path): `Users.LanguageNotOffered` (`PreferredLanguage`; must be
  `ILanguageCatalog.IsOffered`), `Users.CalendarUnknown` (`PreferredCalendar`; `ICalendarRegistry.Codes`),
  `Users.TimeZoneUnknown` (`PreferredTimeZone`; `TimeZoneInfo.TryFindSystemTimeZoneById`),
  `Users.ContactEmailInvalid`, `Users.MobileInvalid` (E.164 `^\+[1-9][0-9]{6,14}$`). Then spec
  0014's `UserAccessRules<TUser>` (escalation, self-lockout, email lock, `Kind`/`Email` pair,
  delete policy).
- `invite` validation per target: `Kind = Individual` else `Users.NotIndividual`; `IsActive = 1`
  else `Users.Inactive`; `State ≠ Joined` else `Users.AlreadyJoined`. Rows hidden by the `Invite`
  grant → `Entity.NotFound`.

#### `invite` flow (0018 §3.3)
- Three phases, two round trips, no transaction during the remote call. **One invalid row fails
  the whole action (422)**; identity-server errors are per-row results (not 422).
- Invitation: `Locale = PreferredLanguage ?? TenantSettings.Languages[0].Code`; `DisplayName` = name
  column of the tenant language matching `Locale` (fallback `Name`); **`ReturnUrl =
  <Tellma:PublicOrigin>/<tenantId>`**; `ExistingOnly = RequestContext.IsSandbox`.
- Sandbox errors `unknown_email` / `existing_user_has_no_credential` → per-row
  `Users.SandboxRequiresExistingIdentity` (recorded in `LastInviteError`).
- Write-back updates `Subject`, `InvitedAt`, `InviteStatus`, `LastInviteError`, `ModifiedAt`,
  `ModifiedById`; bumps each invited user's `PreferencesTag`. `ModifiedAt` stamped → "an admin's
  stale details page gets a concurrency error on its next save".
- Subject mismatch → `InviteResult(Id, null, "Users.SubjectMismatch")`, row untouched. `2627` on
  `UX_Users_Subject` → `Unique` at `Subject`.
- For each user with a status: `context.Notify` `core.user.added` to `[id]`, `{ tenantName,
  actorName }`, no target, `DedupKey = "core.user.added"`; welcome email via
  `IEmailOutbox.EnqueueAsync` (`To = ContactEmail ?? Email`, `Audience = Internal`,
  `RegardingEntity = "core.User"`, template `UserAdded`, subject "You have been added to
  {TenantName}", rendered in the **invitee's** `LocalizationContext`, link
  `<Tellma:PublicOrigin>/<tenantId>`).
- Post-commit: `ITenantMembershipDirectory.RecordAsync` for rows that gained a subject.
- Result `list<InviteResult>` in request order; `tellma.users.invites{outcome ∈ Invited |
  Reinvited | Active | Error | SubjectMismatch | NotAttempted}`.
- **Partial failure**: `PartialFailureException("partial-failure", Results = prefix, Failed = ids
  not attempted)` → **502 `partial-failure`** with `errorDetails.results` and
  `errorDetails.failed[]`; the admin re-invites `failed`.

#### Invitation status (0018 §3.4)
- One `Read` batch under `core.User × Read` (hidden ids omitted), then one delivery call for rows
  `State = Invited` and `InviteStatus ≠ Active`. `Delivery` per row: `State = New` → null;
  `InviteStatus = Active` → `IdentityDeliveryStatus(Subject, NotFound, false, …)` rendered as "no
  invitation email was needed"; `State = Joined` → null; otherwise the identity server's answer
  (`NotFound` = raised by another client; `Sent` with `ExpectsDeliveryEvents = false` terminal;
  `Reason` only for `Bounced`, `Rejected`, `Abandoned`, `Complained`).
- Decision 4: delivery state "is a live drill-down, never stored".

#### Self-service (0018 §3.5)
- `me` → `MeResult` from `ConnectedUser`, `EvaluateAllAsync()` (`AccessSummary`),
  `ISecurableRegistry.Fingerprint`, tenant tags, caller's `PreferencesTag`, the caller's bag. One
  round trip (it is also the cold prologue's).
- `me/save`: three round trips; stale `ModifiedAt` → `ConcurrencyException` (409) with nothing
  written; error paths drop the row index (`preferredLanguage`, `imageId`); blob refs validated
  (`Blob.NotAttachable` at `imageId`); bumps caller's `PreferencesTag`; returns full `MeResult`.
  `RoleMemberships` left null (untouched).
- `me/preferences/set|delete`: key grammar `^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)*$`, ≤128
  chars (`Users.PreferenceKeyInvalid`, path `items[i].key`); value ≤ `MaxPreferenceValueBytes`
  (`Users.PreferenceValueTooLarge`); `Users.PreferenceKeyDuplicate`; `Users.TooManyPreferenceKeys`
  (asserted in SQL, `THROW 50422`). One round trip; result = full bag + new `PreferencesTag`.
- `me/test-notification` `{ channel }`: `inbox` → `Users.ChannelNotTestable` (422, at `channel`);
  unknown key → `Notifications.UnknownChannel` (422); registered channel without sender →
  `NotConfigured`; else the channel's `SendTestAsync`. Email: zero round trips, one
  `IEmailSender.SendAsync` (audience `Internal`, template `TestNotification`, caller's language and
  calendar). **Sent directly, not through the outbox.**
- Admin preference ops (`core.User × Preferences`): `preferences/get` (1 RT; hidden id →
  `NotFoundException`), `preferences/set|delete` (2 RT; bump the target's `PreferencesTag`).

#### Post-commit side effects (0018 §3.6)
- Deactivate → `ISessionTerminationListener.TenantAccessRevokedAsync(tenantId, subject)` (hub
  closes with `session.ended {access-revoked}`), `RecordAsync(… false)`; for a `Service` row
  `DeleteServiceAccountAsync(subject)` — **reactivation needs `issue-credentials` again**. BFF
  cookie untouched.
- Activate → `RecordAsync(… true)`. Delete → only `State = New` rows
  (`Users.OnlyNewUsersDeletable`), no side effects.
- Membership hint repaired by `MembershipReconcileService` (daily).

#### `AccessService` (0018 §3.7)
- `[ApiRoute("access")]`, `access/check` — `[ApiAction("check", MemberOnly = true, Idempotent =
  true, Mutation = false)]`; `CheckAsync(AccessCheckRequest)` → `IReadOnlyList<AccessDecision>`.
- `UserId = null` → the caller (0 RT). Another user → requires `core.Role × Read` and visibility
  under `core.User × Read` (else `NotFoundException`), then `EvaluateForAsync` per securable (2
  RT). Unknown securable → `Denied` with `AccessProblemCode.UnknownResource` / `UnknownAction`,
  never 400. **≤ 200 securables per request** (`LimitExceededException`).
- This is the "can user X do Y, and why" endpoint a role/user page can use.

#### `issue-credentials` (0018 §3.8)
- Target must be `Kind = Service` (`Users.NotService`), active (`Users.Inactive`). Creates a
  confidential client (`CreateServiceAccountAsync(Name)`), writes `Subject = clientId`, sets
  `JoinedAt` once. Rotation deletes the previous client after commit. **The secret crosses once,
  in the response; "the SPA shows it once"**; never logged or stored.

#### Roles stack (0018 §4)
- `public class RoleService<TRole> : EntityService<TRole> where TRole : Role` — **adds no
  operations**: `query`, `get`, `get-by-ids`, `save`, `delete`, `delete-by-query`, `activate`,
  `deactivate`, Excel ops. Securables `core.Role × Read | Save | Delete | Activate` (`FilterRoot =
  core.Role`; Save/Delete/Activate sensitive per 0014 §4.3, f03 §2.27).
- **Members extra**: `IDetailsContributor<Role>` named `members` — one `Rows` query over
  `core.RoleMembership` joined to `core.User`, restricted by the details plan's `IdsSource`,
  projecting `UserId, User.Name, User.Name2, User.Name3, User.Email, Notes`, **served only when the
  caller holds `core.User × Read`**. "Adding members is a bulk user save" (no role-side membership
  editing; review flag 4).
- Filter validation and language-version stamp are spec 0014 §3.7's `RoleAccessRules<TRole>` (f03
  §2.25: codes `Permissions.UnknownSecurable`, `Permissions.FilterNotSupported`,
  `Permissions.FilterVersionUnsupported`, `Permissions.FilterTooLong`, `Permissions.FilterInvalid`,
  `Roles.TooManyPermissions`, `Permissions.EscalationBeyondSelf`, path `[i].Permissions[j]`).
- What a permissions editor must edit (entity owned by 0014; f03 §2.25): per `Permission` row
  `Resource` (resource or `*`), `Action` (action or `*`), `Filter` (Queryex predicate over the
  securable's `FilterRoot`, `nvarchar(2048)`, `me()` allowed, no parameters), `Notes`;
  `FilterLanguageVersion` is `[Derived]`. Role header: `Name`/`Name2`/`Name3`, `Code`,
  `IsPublic`, `IsActive` (server-owned, via actions).
- §4.2: role save writes `core.Roles` + `core.Permissions`, both `[BumpsVersionTag("permissions")]`
  → tenant `permissions` tag bump; every caller's cached set invalidates at its next prologue. No
  hub event for it.

#### GL module: `Center` (0018 §5.1–§5.5)
- `enum CenterType { Abstract, BusinessUnit, Service, Operation, Sale }`; children allowed only
  under `Abstract` and `BusinessUnit` ("grouping types").
- `[Table("Centers", Schema = "gl"), TableType, Tree(MaxDepth = 32)]`, `[ApiResource(Description =
  "Responsibility centers")]`, `public class Center : ActivatableTreeEntity` (non-abstract,
  unsealed, no CLR `Parent`): `CenterType` (required), `[Multilingual, Searchable] Name` (required,
  ≤255), `Name2`, `Name3` (≤255), `[NaturalKey, Searchable(SearchKind.Prefix)] Code` (required,
  ≤50).
- From `ActivatableTreeEntity`: `ParentId`, `SubtreeCount`, `ActiveSubtreeCount`, `IsActive`
  (server-owned); shadow `Node hierarchyid`. Queryex entity `gl.Center`; navs `Parent →
  gl.Center`, `CreatedBy`/`ModifiedBy → core.User`; default related projection `Id, Name, Name2,
  Name3, Code`.
- `GlModule.FeatureName = "gl"`; `GlResources.Center = "gl.Center"`;
  `GlValidationCodes.ParentMustBeGrouping = "Centers.ParentMustBeGrouping"`, `HasChildren =
  "Centers.HasChildren"`.
- Table `gl.Centers` (§5.3): `Id int` (PK, >0; sequence `gl.sq_Centers` **START WITH 1000**),
  `ParentId int?` (FK self, `IX_Centers_ParentId`), `CenterType varchar(12)` (no `IN` check),
  `Name nvarchar(255)`, `Name2`/`Name3` (absent from the schema when the tenant lacks the
  language), `Code nvarchar(50)` (`UX_Centers_Code`), `IsActive bit` (DF 1), `Node hierarchyid`
  (`UX_Centers_Node`), `SubtreeCount int` (DF 1), `ActiveSubtreeCount int` (DF 1), `CreatedAt`,
  `ModifiedAt` (concurrency token, `datetimeoffset(7)`), `CreatedById`, `ModifiedById`. Index
  `IX_Centers_IsActive_Name (IsActive, Name)`. UDTT `CentersList`. No history table.
- Validation (`CenterService<TCenter>`, §5.4): parent must be grouping → `Centers.ParentMustBeGrouping`
  at `[i].ParentId`; type change to non-grouping with children → `Centers.ParentMustBeGrouping` at
  `[i].CenterType`; self-parent → `Tree.Cycle` at `[i].ParentId`; invisible parent → `Fk.NotFound`.
  Delete with `SubtreeCount > 1` → `Centers.HasChildren` at `Ids[i]` ("use
  `delete-with-descendants`").
- Search: platform disjunction over `Code` (prefix) and the `Name` group (contains).
- Routes (§5.5): `/{tenantId}/api/web/centers/{operation}` with `query`, `get`, `get-by-ids`,
  **`get-by-parent-ids`**, `save`, `delete`, `delete-by-query`, **`delete-with-descendants`**,
  `activate`, `deactivate`, `export`, `export/start`, `export-for-import`,
  `export-for-import/start`, `inspect-import`, `import`, `import/start`. MCP exposure `Full`.
- Securables `gl.Center × Read | Save | Delete | Activate` (`FilterRoot = gl.Center`), none
  sensitive by default.
- Resource keys (`Tellma.Module.Gl.Resources.Strings`, en + ar): `Gl_Center`, `Gl_Center_Plural`,
  `Gl_Center_Name`, `Gl_Center_Code`, `Gl_Center_CenterType`, `Gl_Center_ParentId`,
  `Gl_Center_IsActive`, `Gl_CenterType_Abstract` … `Gl_CenterType_Sale`,
  `Centers_ParentMustBeGrouping`, `Centers_HasChildren`.
- `GlFeature : ITellmaFeature` contributes exactly four items: `Entity<Center,
  CenterService<Center>>()`, `ProvisioningStep<GlSampleCentersStep>()`, `Singleton<IGlSampleCenters,
  DefaultGlSampleCenters>()`, `Model<CenterConfiguration>()`. No web, MCP or permission code.
- Round trips: create/update/delete 2, activate/deactivate 1.

#### Sample centers and provisioning (0018 §5.6, §6)
- `record SampleCenterName(Language, Text)`; `record SampleCenter(Code, Names, CenterType,
  ParentCode?)`; `IGlSampleCenters { int Version; Centers }`; step `gl.sample-centers`, `Order =
  100`, `Version = 1`, **Development only** (writes nothing otherwise), idempotent by `Code`.
- Default tree: `HQ` (Abstract) → `BU-TRD` (BusinessUnit) → `OP-TRD` (Operation), `SL-TRD`
  (Sale); `HQ` → `BU-MFG` (BusinessUnit) → `OP-MFG` (Operation); `HQ` → `SV-IT`, `SV-HR`
  (Service). Arabic twins included.
- First administrator (§6.1): step `10 core.bootstrap-administrator` →
  `ITenantBootstrapper.BootstrapAdministrator(...)`, user `State = New`, membership in role 1.
  - Development: `Tellma:Seed:AdminEmail = admin@localhost`, fixed subject
    `00000000-0000-0000-0000-000000000001` (also seeded by the in-proc identity server) → row
    reads `Invited`; first sign-in flips it to `Joined` via the connect prologue.
  - Deployed: `provision --admin-email` → migrator runs the `invite` action as `System`; admin gets
    `core.user.added`, the welcome email (waits `Pending` until tenant `Active`) and the identity
    invite.
  - Sandbox: `ExistingOnly = true`; unknown/credential-less admin → provisioning fails with
    `Users.SandboxRequiresExistingIdentity`.
- Development configuration (§6.2): `Tellma:Identity:Mode = InProc`, **`Authority =
  https://localhost:4200/id`** (`{PublicOrigin}/id`; 7052 is only the Web project's Kestrel port
  behind the SPA proxy), seeded stable secrets, `ClientId = acme-svc`.
- Fresh clone (§6.3): spec 0011 §6.6 commands (`pnpm install` in Client, `migrate`, run Web);
  `migrate` provisions **tenants 1 and 2** with `admin@localhost` and runs
  `core.bootstrap-administrator`, `core.settings`, `core.blob-container`, `gl.sample-centers`; "the
  centers tree lists the eight sample rows on tenant 1".
- Test fixture (§9): tenant 1 with `en`/`ar`, one administrator, three plain users (`New`,
  `Invited`, `Joined`), one service account, two roles (one public), the sample centers — a ready
  E2E data set for the stock pages.

#### Migrations (0018 §7)
- `Migrations/Tenant/20260904000000_InitialTenant` in `Tellma.Distro.Acme.Migrator`, generated by
  `dotnet ef migrations add InitialTenant --context TellmaDbContext` via `AcmeDesignTimeFactory`.
  Contains schemas `core`, `gl`; every core table of specs 0012–0021 (`Users`, `UsersHistory`,
  `UserStamps`, `UserPreferences`, `Roles`, `RoleMemberships`, `Permissions` (+ histories),
  `Settings`, `SettingEntries` (+ histories), `VersionTags`, `Blobs`, `Jobs`, `Schedules`,
  `ScheduleStates`, `JobWorkerState`, `Notifications`, `NotificationPreferences`, `Exports`,
  `Imports`), `gl.Centers`, `dbo.__TellmaProvisioning`, `dbo.__TellmaSchema`, every UDTT and the
  standalone types (incl. `UserInvitationOutcomeList`, `UserPreferenceList`,
  `NotificationPreferenceList`, `NotificationRowList`, `NotificationSliceList`), `HasData` rows.
  No `IDENTITY`, `rowversion`, trigger or `MERGE`.
- Later migrations follow spec 0012's N−1 rule (new column nullable/defaulted; rename = add, copy,
  drop over three releases; UDTT change = new physical name). Model parity test
  (`GetPendingModelChanges()` empty).

#### Budgets (0018 §8.1)
- `users/invite` 2 RT + ⌈n/1000⌉ remote; `issue-credentials` 2; `invitation-status` 1 + delivery
  calls; admin `users/save` 2; `users/me` 1; `users/me/save` 3; `me/preferences/set|delete` 1;
  `preferences/get` 1; `preferences/set|delete` 2; `me/test-notification` 0 + 1 send;
  `users/activate|deactivate|delete` 2; `access/check` 0 (self) / 2 (other); `roles/save|delete|
  activate|deactivate` 2; `centers/save|delete` 2; `centers/activate|deactivate` 1.

#### Relevant review flags (0018)
- RF1 `Invited → Joined` flip in the connect prologue (admin's page is current).
- RF3 `me/save` fixed member list; a distribution's extra self-editable columns need its own
  `[ApiAction]`.
- RF4 `RoleMemberships` single-owned by `User`; role members a read-only extra. "Flips if admins
  reject 'add members' as a bulk user save."
- RF6 `Gender` kept for gender-inflected SPA strings via `UserProfileView`.
- RF10 `ExpectedModifiedAt` required on the settings patch (spec 0013's `TenantSettingsPatch`).
- RF11 delete restricted to `State = New` users.
- RF13 sample centers Development only.

## 3. Gaps for round 2

### 3.1 Bell, toasts, hub client (0025, 0028)
- **No deep-link contract.** 0021 §2.3 says "the client maps the resource to a route" from
  `TargetResource` + `TargetId`; no registry or route grammar exists (0022 RF1: "no deep-link
  contract exists"). The SPA needs a resource → route map (`core.Export`, `core.Import`,
  `core.Job`, `core.Schedule`, `core.OutgoingEmail`, distro resources); the startup check only
  verifies the resource is registered.
- **Every Core target needs a page.** Clicking `core.job.failed`, `core.schedule.paused`,
  `core.export.ready`, `core.import.*`, `core.email.failed` lands on Jobs, Schedules, Exports,
  Imports and Outgoing-email details pages; none is in the round-2 spec map.
- **Actor display in the dropdown.** `InboxSummary.Latest` returns raw columns (§5.3 select list);
  `ActorUserId` has no name or avatar there (only the search page gets `ActorUser` via the default
  related projection). The bell needs a lookup, or the summary must project the actor.
- **Client-side ICU rendering.** The SPA renders `Notification_<Key>` with `ArgumentsJson` (ISO
  dates → user calendar/zone) and job progress from `ProgressKey`/`ProgressArgumentsJson`; needs an
  ICU formatter and string-pack entries (prefixes recognised per f03 §2.19). No label resource for
  a job's `HandlerKey` and none for a notification `Category` (`NotificationCategory_<x>` is not
  defined) — the preferences page groups by category with no label.
- **Warm launch.** The badge comes from `inbox/summary` (a network call); 0021 does not say whether
  the last summary is cached for instant render, nor how the "first summary after load pops none"
  toast rule behaves with a cached summary.
- **Cross-tab leader.** 0021 assumes each tab connects and calls `summary` itself (§5.2 "other
  tabs and devices refresh"). With one leader connection per tenant (brief), follower tabs must
  receive events over a cross-tab channel; toast de-duplication across tabs (pop once, in which
  tab?) is unspecified.
- **One connection per tenant.** The hub URL is `/{tenantId}/hub`; a leader following several
  tenants holds several connections. No per-tenant unread count exists for the tenant picker.
- **`session.ended` client behaviour.** Only "the client disconnects itself" (§6.5). Per reason the
  SPA must decide: `session-terminated` → sign-in; `access-revoked` → tenant picker;
  `tenant-unavailable` → tenant-state page. Unowned.
- **Reconnect policy.** No retry/backoff schedule, no fallback polling interval when the hub is
  down (`summary` is called "a polling endpoint" but no interval is given), and the hub skips
  `Tellma-Client`, so the contract fingerprint (0016 §3.10) does not guard event shapes.
- **No push for security/profile changes.** `cache.changed` carries only `settings` and
  `entity:<Name>` tags; the tenant `permissions` tag (role save, 0018 §4.2), the user
  `PermissionsTag` (membership) and `PreferencesTag` (me/save, preferences, notification
  preferences) have no event, so another tab/device learns only "on its next request".
- **No assignments event.** 0028 must register its own (e.g. `assignments.changed`) through
  `FeatureContribution.ClientEvent`; the 0021 §6.3 catalogue lists four events only.
- **No mark-unread, no per-item seen, no "load more" in the dropdown** (Latest ≤
  `SummaryLatestCount` = 10; the full page is the standard search).
- **Running jobs actions.** `InboxRunningJob` carries `CancelRequestedAt` but the summary offers no
  cancel; cancelling uses spec 0020's `jobs/cancel` (f05). System-run jobs (outbox, notification
  delivery) never appear (`RequestedById = null`).
- **Announcements** (operator broadcasts) are "a later spec" in 0021; the round-2 map (README)
  gives 0028 assignments plus "toasts and banners" but names no owner for announcements.

### 3.2 Notifications page and preferences page
- No owner in the spec map for the Notifications search page (0021 §8) or the Notification
  preferences page; 0018 §10 says "the inbox page (spec 0021)" but 0021 specifies only protocol.
- The search page's text column is client-rendered: not sortable/filterable (RF1); `Type` filter
  needs labels from the catalogue + string pack; `Severity` is an enum filter.
- `Notification` has `Details` but no details page is described (a click navigates to the target).
- Preferences `save` is a complete-set replacement with no concurrency token; two tabs can
  clobber each other.

### 3.3 Users page (0032)
- **Securable step-up.** `core.User × Save | Delete | Activate | Invite | Credentials` are
  sensitive (f03 §2.27); the page must survive `401 insufficient_user_authentication` without
  losing the form (f03 G14: `SecurableSummary` lacks `IsSensitive`).
- **Invite UX has three outcome shapes**: 422 (one invalid row fails all), 200 with per-row
  `InviteResult.Error` codes, and 502 `partial-failure` with `errorDetails.results`/`failed[]`.
- **Invitation status is live and costly** (remote call per ≤1,000 subjects); suited to a details
  drill-down, not a grid column.
- **Service accounts**: `issue-credentials` returns the secret once; the page needs a show-once
  dialog with copy; deactivation deletes the client and reactivation requires re-issuing.
- **Profile vs admin edit.** A member without `Save` gets 403 on the admin `save` of their own row;
  a "My profile" page must use `me/save`. No round-2 spec owns that page (the brief's "My account"
  links to the identity server instead).
- **Language and calendar toggles** in the session menu: `PreferredLanguage`/`PreferredCalendar`
  are `core.Users` columns writable only via `me/save` (full 11-member `MeSaveRequest` +
  `ModifiedAt`, 3 RT, 409 on a stale stamp). No lightweight endpoint exists for a toggle.
- **Image and signature**: `ImageId`/`SignatureId` are blob references (`user-image`,
  `user-signature`, spec 0017 staging; f05); signature is "a transparent PNG raster of the stylus
  capture" (f03) — capture UI unowned.
- **Admin preference editing** exists (`users/preferences/*`) but no UI is described; a raw
  key/value bag editor or targeted resets (e.g. `nav.pinned`) is a design choice.
- **Last active.** `core.UserStamp.LastActiveAt` is Queryex-readable (f03 §2.24) but 0018 names no
  navigation from `core.User` to it; a "last active" column may need one.

### 3.4 Roles page (0032)
- **No securables catalogue endpoint** for the resource/action pickers and filter root (f03 G13);
  0018 §4 adds none. Labels for actions and non-entity resources have no key convention (f03 G9).
- **No filter validate endpoint.** Filter text is validated only on role save (422
  `Permissions.FilterInvalid` with diagnostic args); live validation/autocomplete in the editor
  needs a Queryex validate call or client-side validation (0029).
- **Members are read-only on the role page** (members extra; requires `core.User × Read`). "Add
  members" from the role means saving each user with a full `RoleMemberships` list (synchronise
  semantics); no add-one-membership operation exists (RF4).
- **Effective access view.** `access/check` answers per securable list (≤200) for one user;
  there is no "list all effective grants of user X" or "who holds securable Y" endpoint.
- How the SPA requests the `members` extra (details plan, spec 0015) is not described in 0018
  (see f02).

### 3.5 Centers page (0032)
- Tree page needs `get-by-parent-ids` (lazy children) and `delete-with-descendants`; tree search
  with ancestors is spec 0012/0015's (f02).
- Parent picker should restrict to `CenterType ∈ { Abstract, BusinessUnit }`; the rule is
  server-side only.
- No property beyond `CenterType`, names, `Code` (no description, manager, etc.).

### 3.6 Settings page (0032)
- 0018 leaves settings entirely to spec 0013 (`settings/save`, `TenantSettingsPatch` with
  required `ExpectedModifiedAt`, RF10). See f03.

### 3.7 Outgoing emails admin page
- Specified in 0022 §8 (filters, details with sandboxed `iframe` + text toggle, three verbs) but
  unowned in the round-2 map. Bodies are outside `[DefaultSelect]` → details must `get` with an
  explicit select. The "job link" needs a Jobs details page. No live refresh event (no tag, no hub
  event).

### 3.8 New back-end pieces (0028, 0033)
- A report-share notification (brief: "the other user gets a notification") needs a new type
  (e.g. `core.report.shared`) registered with `FeatureContribution.NotificationType`, a
  `TargetResource` that is a registered stack resource (startup check `notification-types`), a
  `Category`, and its three resources. Its `DedupKey` should follow `<resource>:<id>`.
- Assignments must not be notification rows (0021 Decision 19); 0028 needs its own table, event
  and tray, and must decide whether an assignment also raises a notification.
- New tables ship in a distribution migration under spec 0012's N−1 rule (0018 §7).

## 4. Likely change points

| Section | Why a round-2 design may edit it |
|---|---|
| 0021 Context, Non-goals ("Assigned work and operator announcements — a later spec") | Assignments now owned by 0028; announcements need an owner or an explicit deferral. |
| 0021 §2.3 descriptors | Add new types (report shared, assignment-related if any); possibly a route hint or category label resource. |
| 0021 §2.5 catalogue (RF10) | Warm-launch bundle may want it cached with or folded into the settings/string pack; category labels. |
| 0021 §5.2/§5.3 `InboxSummary` | Actor name/avatar for the dropdown; possibly a per-tenant count for the picker; `Running` cancel. |
| 0021 §5.4 client protocol | Rewrite around the leader tab (0025), cached summary on warm launch, toast-once rule, route map, move UI detail to 0028. |
| 0021 §6.1/§6.3 hub and events | Add `assignments.changed`; maybe tags or events for `permissions`/`preferences`; client `session.ended` handling; reconnect policy. |
| 0021 §8 pages | Name owners for the Notifications page and preferences page. |
| 0022 §4 and RF1 | Notification email `link` should become a deep link once 0024 defines resource routes. |
| 0022 §8 | Owner for the Outgoing emails page; details layout under 0031's framework. |
| 0018 §3.1 | Possible new endpoints: lightweight language/calendar preference write; securables catalogue (or in 0014/0016); permission filter validate. |
| 0018 §3.3 `ReturnUrl`, welcome email link | Both are `<Tellma:PublicOrigin>/<tenantId>`; must match the launch-redirect route (brief OQ: `/<tenant-id>/home` vs `/<tenant-id>`). |
| 0018 §4 members extra | Projection lacks `User.ImageId`, `User.IsActive`, `User.State`; role-side member editing if RF4 flips. |
| 0018 §1.2 `AcmeComposition.Compose` | Brief wants en + ar, Gregorian + Umm Al-Qura, `ar-SA`; only `Languages(["en", "ar"])` is set. Client project wiring. |
| 0018 §6.3 fresh clone, §9 fixture | Add the Client build and E2E over the stock pages; the fixture is a ready data set. |
| 0018 §7 InitialTenant | New round-2 tables; `core.OutgoingEmails` absent from the list (see §5). |

## 5. Contradictions and surprises

1. **No `notification.*` events.** The only hub events are `inbox.changed` (no payload),
   `job.changed {jobId}`, `cache.changed {tag}`, `session.ended {reason}`, all delivered on one
   client method `"event"` as `ClientEventEnvelope(Name, TenantId, Payload)`. The client always
   refetches `inbox/summary`; events never carry content.
2. **"Unseen" vs "unread".** The badge counts **unseen** (`CreatedAt > InboxSeenAt`, reset by
   opening the bell), not unread; unread is per row. A `Replace` refresh gives the row a fresh
   `CreatedAt`, so it becomes unseen again and re-toasts.
3. **Two resource names per type**: `Notification_<Key>` (the message, ICU) and
   `NotificationType_<Key>` (the type label on the preferences page), plus
   `NotificationChannel_<Key>`. `.` → `_` in all.
4. **`core.email.failed` targets one email.** The deduplicated admin notice (`Replace`) points at
   the latest failing row with `{ count }`; a click opens one email, not a filtered list.
5. **Clear is a hard delete** (`inbox/clear`, `inbox/clear-read`); there is no undo or unread
   verb. Ids of another user are silently ignored (`Count` 0, no 403/404).
6. **Running jobs are only the caller's own requested jobs** (`RequestedById = me`, not cron);
   outbox and delivery jobs run as the system user and never show.
7. **Mute rules apply to the inbox only.** Non-mutable types cannot have `inbox` disabled
   (`Notifications.CannotMute`), but `email` is always the user's choice and has no dedup (RF15) —
   a user who enables email for `core.job.held` is emailed on every raise.
8. **The test notification bypasses the outbox** (direct `IEmailSender.SendAsync`), while every
   other email goes through `core.OutgoingEmails`.
9. **Links point to the tenant root.** Invite `ReturnUrl`, the welcome email and notification
   emails all use `<Tellma:PublicOrigin>/<tenantId>`; if round 2 makes `/<tenant-id>/home` the home
   route, `/<tenant-id>` must still resolve (launch redirect).
10. **Admin save of one's own row is 403** without a `Save` grant; self-service is `me/save` only,
    and `me/save` is a full replacement of 11 members with a stamp (not a patch), 3 round trips.
11. **Role members cannot be edited from the role.** Memberships are children of `User` only; the
    role page's `members` extra is read-only and hidden without `core.User × Read`.
12. **`access/check` for another user needs `core.Role × Read`**, ≤200 securables, unknown pairs
    answer `Denied` (never 400).
13. **`invite` has three failure shapes** (422 for the batch, per-row `Error`, 502
    `partial-failure`), and re-running is safe (identity server answers `Reinvited`/`Active`).
14. **Delete only `New` users** (`Users.OnlyNewUsersDeletable`); everyone else is deactivated.
    Deactivation is post-commit-pushed to the hub (`access-revoked`); on Azure SignalR a revoked
    user may still receive thin events for ≤5 minutes.
15. **InitialTenant omits the outbox tables.** 0018 §7 lists "every core table of specs
    0012–0021" (no `core.OutgoingEmails`/`OutgoingEmailAttachments`, no
    `OutgoingEmailOutcomeList`/`EmailDeliveryEventList`), and 0022 §9.3 says "The tables ship in a
    later migration"; yet 0018 §3.3 and §6.1 enqueue the welcome email through the outbox during
    `invite` and `provision`. Implementation order must ship the outbox migration first.
16. **Acme composition vs the brief.** 0018 §1.2 sets `Languages(["en", "ar"])` only; the brief's
    Gregorian + Umm Al-Qura and `ar-SA` locale are tenant settings (spec 0013) not stated here, and
    Queryex `uq` grouping is unavailable server-side (f03).
17. **Development origin**: the identity authority is `https://localhost:4200/id` (the SPA dev
    proxy origin), not the Kestrel port 7052; E2E and the BFF must run behind the 4200 proxy.
18. **Sample centers are Development-only**; a non-development tenant has an empty centers tree,
    so the Centers page empty state matters. Center ids start at 1000.
19. **`Center` has no CLR `Parent`**; the Queryex `Parent` navigation derives from `ParentId`, and
    the default related projection is `Id, Name, Name2, Name3, Code`.
20. **Notification rows have no `Title`.** Text exists only at render time in the reader's
    culture; search over notification text is impossible without RF1's `Title` column.
21. **The hub accepts no client messages** (`MaximumReceiveMessageSize = 4096`, every invocation
    refused): no topic subscriptions, so a details page cannot subscribe to one record's changes.
22. **Assignments scope.** The brief says the research doc kept assignments out of scope and
    brings them in (0028); 0021 Decision 19 rejects assignment rows in `core.Notifications`. The two
    agree only if assignments get their own store, event and tray, not notification rows.
