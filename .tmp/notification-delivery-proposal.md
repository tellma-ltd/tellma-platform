# Proposal: notification delivery as a job, one outgoing-email shape

- **Author:** Ahmad Akra (drafted with Claude)
- **Date:** 3 October 2026
- **Touches:** specs 0018, 0020, 0021, 0022

## Problem

Spec 0022 gives `core.OutgoingEmails` two row shapes:

- a **pre-rendered row**, carrying addresses, subject and bodies, from `IEmailOutbox.EnqueueAsync`;
- a **notification row**, carrying a recipient user, a type and arguments, inserted by the `email`
  channel's SQL and rendered by the sending job.

The second shape exists because spec 0021 §4.4 delivers every non-inbox channel as a synchronous
`Append` of SQL to the caller's batch. Recipients are decided inside that SQL (whether the user is
active, the channel preference and whether they have an address), so C# never sees a recipient
before commit and cannot render per recipient. Decisions record 5 of 0022 ("SQL cannot render ICU
text") is a consequence of this, not its cause.

The seam has two flaws that go beyond email:

1. **Every table-backed channel inherits the unrendered shape.** SMS, push and WhatsApp would each
   need their own type-plus-arguments row and a render step in their sender, as email does now.
2. **API-backed channels are not durable.** 0021 §4.4 sends them from `batch.OnCommitted`, so a
   crash between commit and send loses the message, and a failed send is retried by nobody.

0022 then works around the seam: `IJobQueue.EnqueueForRows`, `CK_OutgoingEmails_Shape`, a render
step in dispatch, a recipient load, four rendered-text columns in `OutgoingEmailOutcomeList`, an
exception to "fixed write paths", `Email.RecipientUnavailable`, and a `JSON_VALUE` sniff on
`handlerKey` to stop emails about failed emails.

## Proposal

Delivering to any channel other than the inbox is background work, and spec 0020 is the platform's
durable background work. So:

1. `Notify` keeps appending the inbox statement to the caller's batch, and also appends a
   **conditional enqueue** of one `core.notification-delivery` job per request. The job row is
   inserted only if some recipient could receive a non-inbox channel.
2. The **delivery job** resolves recipients in one read, then calls each channel's async
   `DeliverAsync` with the recipients that channel should reach. Channels run C#.
3. The **email channel** renders each recipient's email and calls `IEmailOutbox.EnqueueAsync`.
   The rows commit in the delivery job's completion transaction.
4. The **outbox** has one row shape and one way in. Every row records exactly what was sent.

```
caller's transaction                       delivery job                         outgoing-email job
────────────────────                       ────────────                         ──────────────────
INSERT core.Notifications (inbox)
INSERT core.Jobs                     ──▶   read recipients + preferences
  core.notification-delivery               email: render per recipient
  WHERE <a non-inbox channel                 └─ IEmailOutbox.EnqueueAsync  ──▶  send, settle
         could apply>                      sms / push: their own DeliverAsync
                                           commit with the completion
```

The inbox is unchanged: its rows stay language-neutral and are rendered when read (0021 principle
2). Rendering moves only for channels that leave the platform, where the text is frozen at send
time anyway.

## Spec 0021 changes

### The channel seam (replaces §4.4)

```csharp
// Tellma.Core.Abstractions.Notifications
public sealed record NotificationRecipient(
    int UserId, string Name, string? Email, string? ContactEmail, string? ContactMobile,
    LocalizationContext Localization);   // PreferredLanguage/Calendar/TimeZone, else the tenant's

public interface INotificationChannel   // scoped; at most one per registered channel other than inbox
{
    string Key { get; }

    // Delivers one request to the recipients the dispatcher resolved for this channel: active, and
    // with this channel enabled for the type. Writes through host commit with the delivery job's
    // completion; an exception fails the job attempt, and spec 0020 retries it.
    Task DeliverAsync(IOpenWriteHost host, NotificationRequest request, IReadOnlyList<NotificationRecipient> recipients);

    Task<TestNotificationResult> SendTestAsync();   // unchanged
}
```

`NotifyStatement` and the `OnCommitted` path for API-backed channels are removed. A channel that
calls an API sends from `DeliverAsync` directly. A re-run after a crash between the send and the
completion can send twice, which is the same failure mode, and the same trade-off, as 0022's
interrupted send. A channel that needs fail-closed behaviour writes its own outbox row, as email
does.

### `Notify` (amends §3.1 and §3.3)

After a request's inbox statement, `Notify` calls spec 0020's conditional enqueue (below) with:

- `HandlerKey = "core.notification-delivery"`, `RunAsUserId = SystemUserId`, `RequestedById = null`;
- `Arguments` = the request (`Type`, `ArgumentsJson`, `TargetResource`, `TargetId`,
  `ActorUserId`, `RecipientIds`);
- a condition over the request's recipient rows that is true when an active recipient has some
  delivered channel enabled for the type.

The condition is fixed text built once at startup from the channels that have a sender. For each
such channel `c` with default `d`:

```sql
EXISTS (SELECT 1 FROM @tb{b}_t0 AS [r]
        INNER JOIN [core].[Users] AS [u] ON [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1
        WHERE COALESCE((SELECT [p].[Enabled] FROM [core].[NotificationPreferences] AS [p]
                        WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = @tb{b}_p0
                          AND [p].[Channel] = 'email'), 0) = 1
           -- OR one COALESCE(...) = 1 term per further delivered channel, with that channel's default
       )
```

`@tb{b}_t0` and `@tb{b}_p0` are the inbox statement's recipients and type, already declared in the
batch. With `email` off by default, the predicate is a seek that usually finds nothing, and no job
is created. No round trip is added, so principle 1 holds.

A request with `InboxOnly = true` (below) skips the enqueue entirely.

### The delivery handler (new section)

```csharp
// Tellma.Core.Notifications
[JobHandler("core.notification-delivery", BatchSize = 25, MaxAttempts = 12, InboxOnlyFailureNotices = true)]
public sealed class NotificationDeliveryHandler : IJobHandler;
```

Per job:

1. **Load.** One `Read` round trip for the request's `RecipientIds`: `IsActive`, `Name`, `Email`,
   `ContactEmail`, `ContactMobile`, the three `Preferred*` columns, and every preference row for the
   request's type.
2. **Resolve.** For each delivered channel, pick the active recipients whose preference, or else
   the channel's `DefaultEnabled`, is on.
3. **Deliver.** Call `DeliverAsync` for each channel with a non-empty recipient list, in registration
   order, on the job's `IOpenWriteHost`.
4. **Complete.** Writes from every channel commit with the job's completion, so a re-run before
   completion leaves nothing behind.

Preferences and addresses are read when the job runs, seconds after the notification, not at the
moment it was raised. A user who opts out in between is honoured.

### Rendering (amends §2.4)

`INotificationRenderer.Render` takes a `LocalizationContext` (language, calendar, time zone) in
place of a bare `CultureInfo`, so an email shows dates in the recipient's own calendar and zone.
This resolves 0022 Review flag 2's formatting half. The SPA and MCP callers pass the reader's
context, as they do now.

### Inbox-only requests (amends §3.1)

`NotificationRequest` gains `bool InboxOnly = false`. When set, `Notify` writes the inbox row and
enqueues no delivery job. It replaces 0022's hard-coded SQL filter:

- 0022 raises `core.email.failed` with `InboxOnly = true`.
- 0020 raises `core.job.failed` with `InboxOnly = true` for a handler registered with
  `InboxOnlyFailureNotices = true`: `core.notification-delivery` and `core.outgoing-email`. A
  failing delivery path cannot then feed itself.

## Spec 0020 changes

### Conditional enqueue (amends §4.1 and §4.2)

```csharp
// Tellma.Core.Abstractions.Jobs
public interface IJobQueue
{
    // existing members unchanged

    // Appends §4.2's statement for one request whose job row is inserted only when `when` holds.
    // `when` is a fixed-text boolean expression that references only identifiers already declared
    // earlier in the same batch. The handle's value is the new job's id, or null when the
    // condition was false.
    BatchResult<int?> EnqueueWhen(IDataBatch batch, JobRequest request, string when);
}
```

The statement is §4.2's with `WHERE {when}` on the `core.Jobs` insert and a one-row range taken
unconditionally (an unused sequence value is a gap, as in 0021 §3.3). `request.Entity` must be
null. The nudge is registered and sent only when a row was inserted.

### Handler registration (amends §3)

`[JobHandler]` gains `bool InboxOnlyFailureNotices` (default `false`): this handler's
`core.job.failed` notices are raised with `InboxOnly = true`.

`IJobQueue.EnqueueForRows` from 0022 §3.3 is not added.

## Spec 0022 changes

| Area | Change |
|---|---|
| §1.2 vocabulary | One kind of row; drop "pre-rendered row" and "notification row". |
| §1.3 principle 3 | Drop the exception for a notification row's rendered columns; the job and the delivery handler write only server-owned columns. |
| §1.4 table | Drop the "channel insert + `EnqueueForRows`" statement. |
| §2.1 entity | Drop `NotificationType`, `NotificationArgumentsJson` and `CK_OutgoingEmails_Shape`. `To`, `Subject` and `TextBody` become non-nullable. `RecipientUserId` stays, optional, for every producer. |
| §2.2 table types | `OutgoingEmailOutcomeList` drops `[To]`, `Subject`, `TextBody` and `HtmlBody`. |
| §3.3 | Deleted. |
| §4 | Becomes "The email channel": `EmailNotificationChannel.DeliverAsync` renders each recipient (`To = ContactEmail ?? Email`; subject and bodies from `Email_Notification_*` given `tenantName`, the rendered text and the link) and makes one `EnqueueAsync` call with `Audience = Internal`, `RecipientUserId`, and `RegardingEntity`/`RegardingId` = the request's target. Recipients without an address are skipped. |
| §5 | Step 2 drops the recipient load; step 3 (render) is deleted; the settle table drops "+ the rendered text" and the `Email.RecipientUnavailable` outcome. |
| §5 step 7 | `core.email.failed` is raised with `InboxOnly = true`. |
| §9.2 | `tellma.outbox.enqueued` keeps `source = api \| notification`, set by the caller of `EnqueueAsync`. |
| Decisions 5, 6 | Replaced by one record: "One row shape; notifications reach the outbox through 0021's delivery job." |
| Review flag 4 | Narrows to the pre-mark, settle and delivery update; the channel insert no longer writes through the internal channel. |

The welcome email and the `core.job.failed` email tests (0022 §10) move to exercising the delivery
job end to end.

## Spec 0018 changes

`EmailNotificationChannel` implements `DeliverAsync` as described under 0022 §4 above.
`SendTestAsync` is unchanged.

## Trade-offs

- **Two job hops before a notification email leaves:** delivery, then sending. That adds a few
  seconds, on mail that is never urgent.
- **One extra job row** per notify request that has a non-inbox recipient. With email off by
  default, that is a small share of requests.
- **Preferences and addresses are read late**, at delivery rather than when the notification was
  raised. The gap is seconds, and reading later is arguably more correct.
- **Delivery and sending fail independently.** A delivery job that exhausts its attempts raises
  `core.job.failed` to administrators (inbox only). Nothing is emailed for that notification, and
  the inbox row is unaffected.

## Open points

1. **Job argument size.** The delivery job carries `RecipientIds`. 0021's
   `MaxRecipientsPerRequest = 10000` does not reliably fit 0020's 64 KB `ArgumentsJson` cap (ids
   of up to seven digits plus separators). Recommendation: lower the cap to 5,000. Alternative:
   enqueue one delivery job per slice of recipients.
2. **Deep links.** Notification emails still link to the tenant root; 0022 Review flag 2's link
   half remains until a deep-link contract exists.
3. **`core.notification-delivery` batch shape.** `BatchSize = 25` assumes requests are small. A
   request to 5,000 recipients renders 5,000 emails inside one job item; if that is too heavy for
   one completion, slice as in point 1.
4. **Sequencing.** 0020 and 0021 are committed specs. These changes land as amendments in the same
   round that 0022 is merged, so all three describe the same seam.
