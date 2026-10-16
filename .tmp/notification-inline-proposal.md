# Proposal: notifications raised inside the frame

- **Author:** Ahmad Akra (drafted with Claude)
- **Date:** 3 October 2026
- **Touches:** specs 0015, 0018, 0020, 0021, 0022
- **Alternative to:** [notification-delivery-proposal.md](notification-delivery-proposal.md) (the
  deferred design, which hands delivery to a job)

## Problem

The persist pipeline works in two round trips:

- **RT1** loads everything that validation and C# logic need: before images, validator loads, and
  the loads of enlisted writes.
- **RT2** writes the result in one transaction.

Spec 0021 does not follow this model. `Notify` is raised from `ContributeAsync`
(`PersistContext.Notify`) and from the job completion batch, both after the validation rounds have
closed. A notification therefore cannot load anything, and every decision about its recipients
(whether they are active, their preferences, and for email whether they have an address) happens as
SQL predicates inside RT2. That is fine for the inbox, which needs nothing in C#. A channel, though,
needs per-recipient C# work (rendering in the recipient's language), which requires data that only
RT1 can provide.

Spec 0022 then works around the missing RT1:

- an unrendered "notification row" shape;
- `IJobQueue.EnqueueForRows`, because the rows only exist inside SQL;
- a render step in the sending job, plus rendered columns written back by the settle;
- an exception to principle 3 ("fixed write paths") for those rendered columns;
- a `JSON_VALUE` check on `handlerKey` to stop emails about failed emails.

## The rule this proposal restores

> **RT1 decides; RT2 writes.** A notification is raised while the frame is still composing, its
> recipients are resolved from data loaded in the frame's rounds, and RT2 holds only writes plus the
> checks that must happen inside the transaction because two saves could race (the inbox's
> duplicate check).

## Design overview

`Notify` becomes a **frame participant**, like an enlisted write:

1. **Raise.** A service hook, a validator, an action method or a job handler calls `Notify` while
   the frame is composing. `ContributeAsync` is too late, as it already is for enlistment.
2. **Resolve preferences without a round trip.** The non-default preference rows are a small,
   tenant-cached set. From the cache, C# knows which recipients have a non-inbox channel enabled for
   the type, without querying.
3. **Load only when a channel will deliver.** If any recipient has a non-inbox channel enabled, the
   notifier asks the frame's loader for those users' rows (address, language, calendar, time zone).
   The load rides the round in progress, or adds one round if the frame's last round has already
   run. If nobody opted in, there is no load.
4. **Compose.** Each channel gets its resolved recipients and composes in C#. The email channel
   renders and enlists pre-rendered `OutgoingEmail` rows through `IEmailOutbox.EnqueueAsync`.
5. **Persist (RT2).** The inbox statement (with only its active and duplicate checks left in SQL),
   the enlisted outbox rows and their send jobs all commit with the business change.

```
compose phase                                    RT2 (one transaction)
─────────────                                    ─────────────────────
ValidateAsync → context.Notify(request)          host's own writes
  preferences from the tenant cache  (no RT)     INSERT core.Notifications   (inbox; IsActive + dedup in SQL)
  opted-in recipients? → loader.ByIds<User>      INSERT core.OutgoingEmails  (enlisted, pre-rendered)
     (rides RT1, or one more round)              INSERT core.Jobs            (one send job per email row)
  email channel: render, EnqueueAsync(host)      COMMIT
```

The outbox ends up with one row shape and one way in, and `EnqueueForRows` is not needed.

## Spec 0021 changes

### Where a notification may be raised (amends §3.1, §3.5)

`Notify` is a member of every context that runs while a frame is composing, and of nothing that
runs after.

| Caller | Member | Allowed |
|---|---|---|
| A save's `ValidateAsync`, an `IEntityValidator<T>` | `SaveContext.Notify(request)` (new) | yes |
| A delete's `ValidateDeleteAsync` | `DeleteContext.Notify(request)` (new) | yes |
| `ValidateActionAsync`, an `[EntityAction]` method | `ActionContext.Notify(request)` (existing; new meaning) | yes |
| An `[ApiAction]` method, a provisioning step | `INotifier.Notify(host, requests)` on the injected `IOpenWriteHost` | yes, before `PersistAsync` |
| A job handler | `JobItem.NotifyOnSuccess(request)` (existing) | yes; the worker raises it in the job frame (spec 0020 changes) |
| `ContributeAsync`, `IPersistEffect<T>`, `SaveOptions.OnPersist` | `PersistContext.Notify` (removed) | no: `InvalidOperationException`, as for enlistment |
| No frame (a hosted service, a post-commit effect) | `INotifier.NotifyAsync(requests)` | yes: opens its own frame |

```csharp
// Tellma.Core.Abstractions.Notifications
public interface INotifier                              // scoped
{
    // Raises the requests in a frame that is still composing. Preferences come from the tenant cache;
    // the users of channel recipients are loaded on the frame's loader; channels compose once the
    // load is filled; the inbox statement joins the frame's persist batch as the notifier's group.
    void Notify(IWriteHost host, IReadOnlyList<NotificationRequest> requests);

    // Opens a frame in the current scope, raises the requests and persists: 1 round trip, or 2 when
    // a channel recipient's user row must be loaded.
    Task NotifyAsync(IReadOnlyList<NotificationRequest> requests);
}
```

A decision that can only be made at persist time (it depends on a value computed in SQL) goes
through a job whose handler raises the notification, the same rule 0022 §3.2 already states for
the outbox.

### The preference cache (new section; amends §1.4 and §4.2)

`core.NotificationPreferences` stores deviations from the defaults only, so it stays small: a row
per user who changed a toggle. The notifier reads it from a tenant-level cache rather than loading
it.

```csharp
// Tellma.Core.Notifications
public interface INotificationPreferenceCache           // scoped facade over spec 0013's tenant cache
{
    // The channel's effective setting for (user, type): the stored deviation, else the default.
    bool IsEnabled(int userId, string type, string channel);

    // True when any of the users has the channel enabled for the type; cheap when nobody deviated.
    bool AnyEnabled(IReadOnlyList<int> userIds, string type, string channel);
}
```

- The table gains a tenant-level version tag, `notification-preferences`. The preferences save
  bumps it in addition to the caller's `PreferencesTag`.
- The cache is advisory: a preference changed a moment before a notify can be missed by that one
  notify. The persist batch does not declare a dependency on the tag, because a preference is not a
  security decision.

### Resolution and the recipient load (new section)

For each request, the notifier:

1. **Filters the inbox recipients** by the inbox preference from the cache (mutable types only).
   Whether each recipient is active stays a predicate in SQL, so the inbox needs no load.
2. **Picks channel candidates.** For every channel other than the inbox that has a sender: the
   recipients with the channel enabled for the type, from the cache. If no channel has candidates,
   resolution ends here.
3. **Loads the candidates' users** with `host.Loader.ByIds<User, int>(ids, "Id,IsActive,Name,Email,
   ContactEmail,ContactMobile,PreferredLanguage,PreferredCalendar,PreferredTimeZone")`. The loader
   deduplicates, so a user a validator already asked for costs nothing extra.
4. **Builds `NotificationRecipient`s** from the active candidates, after the round fills the load,
   and calls each channel's `ComposeAsync` with its own recipients.

A request flagged `InboxOnly` skips steps 2–4.

### The channel seam (replaces §4.4)

```csharp
// Tellma.Core.Abstractions.Notifications
public sealed record NotificationRecipient(
    int UserId, string Name, string? Email, string? ContactEmail, string? ContactMobile,
    LocalizationContext Localization);                  // the user's Preferred*, else the tenant's

public interface INotificationChannel                   // scoped; at most one per registered channel other than inbox
{
    string Key { get; }

    // Composes delivery for recipients the notifier resolved: active, with this channel enabled
    // for the request's type. Runs in the frame's compose phase: it may enlist writes through host,
    // never call a transport and never append to a batch directly.
    Task ComposeAsync(IWriteHost host, NotificationRequest request, IReadOnlyList<NotificationRecipient> recipients);

    Task<TestNotificationResult> SendTestAsync();       // unchanged
}
```

A channel never sends inside the frame. It writes durable rows, its own outbox, and a job sends
them, as email does. `NotifyStatement` and the `OnCommitted` path for API-backed channels are
removed, which fixes their loss on a crash after commit.

### The inbox statement (amends §3.3)

Recipients arrive already filtered for the inbox preference. The statement drops the preference
predicate and the `@tb{b}_p6` (`Mutable`) parameter. It keeps:

- the `IsActive` predicate, a cheap check inside the transaction that also covers a user deactivated
  since validation;
- the duplicate predicate and the replace, which are race-sensitive and belong in the transaction.

It joins the persist batch as the notifier's group, after the host's effects and every `AfterHost`
group.

### Rendering (amends §2.4)

`INotificationRenderer.Render` takes a `LocalizationContext` in place of a bare `CultureInfo`, so a
rendered email shows dates in the recipient's calendar and zone. 0022 Review flag 2's formatting
half is resolved.

### Inbox-only requests (amends §3.1)

`NotificationRequest` gains `bool InboxOnly = false`: the notifier writes the inbox row and composes
no channel. 0022 sets it on `core.email.failed`; 0020 sets it on `core.job.failed` for a handler
registered with `InboxOnlyFailureNotices = true` (`core.outgoing-email`). The `JSON_VALUE` check in
0022 §4 is gone.

## Spec 0015 changes

- **§7.1:** `SaveContext` and `DeleteContext` gain `Notify(NotificationRequest)`. `ActionContext.
  Notify` raises in the frame instead of appending to `Batch`.
- **§13.1:** `PersistContext.Notify` is removed, and so is `INotifier.Notify` from the composers a
  `ContributeAsync` participant or a `SaveOptions.OnPersist` delegate may call. The paragraph
  "Anything that must eventually happen … is a job row appended here" stays true.
- **§13.3:** the notifier is admitted as a frame participant that is not a stack. It registers
  tasks on the host's scheduler, uses the host's loader, and contributes one group (the inbox
  statements) to the persist batch at a fixed position after the `AfterHost` groups. A `Notify`
  outside the compose phase is refused with `InvalidOperationException`, like a late enlistment.
- **§16.2:** add a "Notification" row to the round-trip budget (see the budget below).

## Spec 0020 changes

- **Raising in the job frame (§3.3, §5.6).** After the handler returns and outcomes are known, the
  worker raises every `NotifyOnSuccess` request of a succeeded item, and its own notices
  (`core.job.failed`, `core.schedule.paused`), with `INotifier.Notify(jobHost, …)` before it persists
  the frame. Any load joins the frame's validation round, which the budget already allows as "0–1".
- **`[JobHandler]` gains `InboxOnlyFailureNotices`** (default `false`).
- **The gap notices** (`core.scheduler.gap`, `core.job.held`) keep their own batches through
  `NotifyAsync`.
- `IJobQueue` is unchanged: no `EnqueueForRows`, no conditional enqueue.

## Spec 0022 changes

| Area | Change |
|---|---|
| §1.2 vocabulary | One kind of row. |
| §1.3 principle 3 | The exception for rendered columns is dropped. |
| §1.4 table | The "channel insert + `EnqueueForRows`" row is dropped. |
| §2.1 entity | Drop `NotificationType`, `NotificationArgumentsJson` and `CK_OutgoingEmails_Shape`. `To`, `Subject` and `TextBody` are non-nullable. |
| §2.2 | `OutgoingEmailOutcomeList` drops `[To]`, `Subject`, `TextBody`, `HtmlBody`. |
| §3.3 | Deleted. |
| §4 | Becomes "The email channel" (below). |
| §5 | Step 2 drops the recipient load; step 3 is deleted; the settle drops the rendered text and `Email.RecipientUnavailable`. |
| §5 step 7 | `core.email.failed` is raised with `InboxOnly = true`. |
| Audit columns | A notification email's `CreatedById` is the frame's acting user, as for any enlisted row; the special case in §2.1 goes. |
| Decisions 5, 6 | Replaced by: "One row shape; notifications reach the outbox from the notifying frame." |
| Review flag 4 | Narrows to the pre-mark, settle and delivery update. |

### The email channel (new 0022 §4; implemented in 0018's `EmailNotificationChannel`)

`ComposeAsync(host, request, recipients)`:

1. Drops recipients with neither `ContactEmail` nor `Email`.
2. Groups the rest by `Localization` and renders once per group: the notification text through
   `INotificationRenderer`, then `Email_Notification_Subject`, `_Text` and `_Html` given
   `tenantName`, `text` and `link`. The body depends on the recipient only through their language,
   calendar and zone.
3. Makes one `IEmailOutbox.EnqueueAsync(host, requests)` call with one request per recipient: `To`
   the single address, `Audience = Internal`, `RecipientUserId`, and `RegardingEntity`/`RegardingId`
   set to the notification's target.

The rows carry no attachments, so the outbox stages nothing and its enlistment declares no load.
The send jobs come from `OutgoingEmailService.ContributeAsync` through the ordinary `Enqueue`.

## Examples

The tenant is Acme. Two members:

| User | `PreferredLanguage` | Email preference for `acme.document.assigned` |
|---|---|---|
| Sara (id 7) | `ar` | enabled (a row in `core.NotificationPreferences`) |
| Omar (id 9) | none (tenant primary `en`) | default (off) |

### 1. Save: reassigning a document

A distribution's document service notifies the new assignee when `AssigneeId` changes. `Changed`
needs the before image, so the call happens after RT1 has returned:

```csharp
protected override Task ValidateAsync(SaveContext<Document> context)
{
    for (var i = 0; i < context.Entities.Count; i++)
    {
        var doc = context.Entities[i];
        if (doc.AssigneeId is int assignee && (context.IsNew(i) || context.Changed(i, d => d.AssigneeId)))
            context.Notify(new NotificationRequest("acme.document.assigned", [assignee],
                new { number = doc.Number }, "acme.Document", doc.Id, context.Context.UserId,
                DedupKey: $"acme.document.assigned:{doc.Id}", OnDuplicate: DuplicatePolicy.Replace));
    }
    return Task.CompletedTask;
}
```

**Assigned to Omar.** The cache says Omar has no channel other than the inbox enabled, so no load
and no channel. RT2 carries the document update and one inbox insert. **2 round trips**, the same
as today.

**Assigned to Sara.** The cache says Sara has email enabled, so the notifier asks the loader for
user 7. RT1 has already run, so this is round **RT1a**. When it returns, the email channel renders
the Arabic text and enlists one `OutgoingEmail` row to `sara@acme.example`. RT2 carries the
document update, the inbox insert, the email row and its send job. **3 round trips.** The extra one
is paid only because an email is going out.

**Reassigned to Sara while her earlier notice is unread.** Same as above. In RT2 the inbox replace
refreshes her unread notice, and a new email row is still written: email has no duplicate rule, as
in 0022 §4.

### 2. The same notification, raised before the first round

If the service notifies on creation only (`IsNew` needs no before image) and calls `Notify` before
its first `await`, Sara's user load joins RT1 itself, so the email adds no round trip to a save that
already has an RT1. The one exception is a create that otherwise needs no RT1 at all (1 round
trip): there the load creates RT1, giving **2 round trips with an email, 1 without**.

### 3. Action: approving a document notifies its author

```csharp
[EntityAction("approve")]
public Task ApproveAsync(ActionContext<Document, int> context)
{
    context.Update(new Dictionary<string, object?> { ["State"] = "Approved" });
    foreach (var doc in context.Entities)           // loaded in RT1
        context.Notify(new NotificationRequest("acme.document.approved", [doc.CreatedById],
            new { number = doc.Number }, "acme.Document", doc.Id, context.Context.UserId, DedupKey: null));
    return Task.CompletedTask;
}
```

Approving three documents by Omar and two by Sara gives five requests. The cache marks only Sara's
two for email, so one `ByIds` for user 7 runs in RT1a, deduplicated to one row. The email channel
renders once (Arabic) and enlists two email rows. **3 round trips**; with no opted-in authors,
**2**.

### 4. Job: an export finishes

The export handler calls `item.NotifyOnSuccess(new NotificationRequest("core.export.ready", [7],
…))`. After the handler returns, the worker raises it in the job frame. Sara opted in, so her user
row joins the frame's single validation round. The completion transaction then carries the export
row, the inbox insert, the email row and its send job. The job costs **one extra round trip** only
because an email is going out.

### 5. Rendering once per language

A request to Sara (`ar`), Omar (`en`, opted in this time) and Lina (`en`, opted in) renders twice,
once for `ar` and once for `en`, and enlists three rows. The text, subject and bodies of Omar's and
Lina's rows are identical; only `To` and `RecipientUserId` differ.

### 6. A notification raised too late

```csharp
protected override Task ContributeAsync(PersistContext<Document> context)
{
    context.Notify(…);   // no longer compiles: PersistContext has no Notify
}
```

The fix is to move the decision into `ValidateAsync`. If it really depends on a value only SQL knows
at persist time, enqueue an arguments-only job whose handler raises the notification.

### 7. The outbox's own failure notices

A `core.outgoing-email` job exhausts its attempts. Its handler is registered with
`InboxOnlyFailureNotices = true`, so the worker raises `core.job.failed` with `InboxOnly = true`.
Administrators get the inbox notice and no email, so a broken transport cannot feed itself.
`core.email.failed` (0022 §5 step 7) is always raised inbox-only.

## Round-trip budget

| Operation | Today | Inline, nobody opted in | Inline, someone opted in |
|---|---|---|---|
| Save raising from data known before RT1 | 1–2 | 1–2 | 2 |
| Save raising from the before image | 2 | 2 | 3 |
| Action raising from its loaded targets | 2 | 2 | 3 |
| Job completion with `NotifyOnSuccess` | 1 | 1 | 2 |
| `NotifyAsync` with no frame | 1 | 1 | 2 |

The rule: **an operation pays a round trip for a notification only when a channel will deliver it.**
The inbox never costs a round trip. Today's design has the same column for every case, but it pays
for that later: the render, the unrendered row shape and the rows written back when the email is
sent.

## Trade-offs against the deferred proposal

| | Inline (this proposal) | Deferred |
|---|---|---|
| Round trips for the caller | +1 only when an email or other channel delivers and the notify came after the last round | +0 always |
| Email row exists when the business change commits | yes, visible to admins at once | after the delivery job runs |
| Time before a notification email is sent | one job (send) | two jobs (deliver, then send) |
| New machinery | the notifier as a frame participant, the preference cache, its tenant tag | a delivery job, `EnqueueWhen`, a 64 KB arguments limit to respect |
| Where callers may notify | while composing; `ContributeAsync` callers must move | anywhere, as today |
| Large fan-out | bounded by the outbox stack's `MaxSaveCount` per frame | sliced across delivery jobs |
| Matches the RT1/RT2 model | in the caller's own frame | in a second, later frame |

## Open points

1. **Fan-out ceiling.** One frame can enlist at most the outbox stack's `MaxSaveCount` email rows.
   A request with more opted-in email recipients than that is an authoring error
   (`LimitExceededException`), and a broadcast producer raises from a job in slices. The ceiling's
   value (a few hundred?) needs choosing.
2. **Moving callers.** Every caller that raises from `ContributeAsync` or `SaveOptions.OnPersist`
   today must move: 0021 §3.5's example, and any 0018 or 0019 code that does so. Most decisions
   are already known in `ValidateAsync`.
3. **The cache's tenant tag.** 0013 needs the `notification-preferences` tenant tag registered, and
   0021 §1.4's "carries no tenant-level version-tag attribute" changes for
   `core.NotificationPreferences`.
4. **Persist batch size.** Notification bodies ride RT2 as parameters. At about 10 KB per email,
   the fan-out ceiling of point 1 bounds a batch to a few MB.
5. **Deep links.** Notification emails still link to the tenant root; 0022 Review flag 2's link
   half remains.
