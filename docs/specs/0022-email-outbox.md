# Spec: Email Outbox

- **Author:** Ahmad Akra
- **Date:** 3 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every distribution sends mail it must not lose: the welcome email, a notification a user asked to
receive by email, an invoice to a customer. Spec 0007 shipped the connectors that send a message and
reserved `IEmailOutbox` for the durable tier above them (spec 0007 §13); this spec ships it.

Each email is a row of `core.OutgoingEmails`, written in the transaction of the change that produced
it, so both commit or roll back together. A producer hands the outbox a rendered message (§3); spec
0021's `email` notification channel is one (§4). One spec 0020 job per row sends the email as the
system user and records the result in the row's `State`, and a spec 0007 webhook later fills its
delivery columns. An administrator browses the rows on spec 0032's Outgoing emails pages, reads
their envelopes, bodies and attachments, and can retry, cancel or delete them.

## Goals / Non-goals

**Goals**

- The `OutgoingEmail` entity over `core.OutgoingEmails` and `core.OutgoingEmailAttachments`, with
  its `Query | Details | Delete | Enlist` stack and admin surface (§2, §8).
- One way in, `IEmailOutbox.EnqueueAsync` (§3), which spec 0021's `email` channel also uses (§4).
- The sending job (§5), delivery events (§6), the `retry`, `cancel` and `delete` verbs and retention
  (§7), and options, telemetry and composition (§9).

**Non-goals (explicitly out of scope)**

- **Per-tenant quotas, spread pacing and approval thresholds** (handed here by spec 0007 §10) — no
  producer sends bulk yet, and nothing throttles a tenant today.
- **The dedup keys spec 0007 §1.3 rule 5 assigns to the outbox** — neither SendGrid nor ACS honours
  an idempotency key, so a retried `TransientFailure` can duplicate a message (§1.3 principle 2).
- **Unsubscribe headers, stream classification, suppression refusal and UI** (spec 0007 §11) — every
  consumer is transactional, and `Dropped` events already surface suppression.
- **The Outgoing emails pages** — spec 0032 §10, over this spec's stack and verbs.
- **A per-document email tab** — a client follow-up over the indexed regarding columns (§2.1).
- **Per-recipient delivery rows** — one recipient is the dominant case; `DeliveryRecipient` names
  the address an event concerned.
- **Open and click tracking, digests, a template mechanism, inbound mail** — no consumer needs them.
- **Purging bodies before the row is deleted** (Decisions record 11).
- **Retrying a `Rejected` row** — the message was refused; the producer sends a corrected one.

## 1. Placement, vocabulary, principles, statements

### 1.1 Projects and namespaces

- **Contracts** in `Tellma.Core.Abstractions`: the types of §2.1, §3.1 and §9 in namespace `.Email`,
  replacing spec 0007 §13's uncompiled shape; the two table types in `.TableTypes`.
- **Runtime** in `Tellma.Core`, namespace `Tellma.Core.Outbox`: `EmailOutbox`,
  `OutgoingEmailService`, `OutgoingEmailHandler`, `OutgoingEmailDeliveryHandler`,
  `OutgoingEmailRetentionHandler` and `OutboxStatements`. It uses `IEmailSender` from Abstractions
  only; the connectors stay a host composition (spec 0007 §1.1).

Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are left out.

### 1.2 Vocabulary

- An **outgoing email** is one row of `core.OutgoingEmails`: one message to one or more addresses,
  rendered by its producer (§3), so the row records exactly what was sent.
- A row is **settled** in `Sent`, `Sandboxed`, `Rejected`, `Failed` or `Cancelled`, and
  **unsettled** in `Pending` or `Sending`. A job is **live** in `Pending`, `Running` or `Held`. An
  unsettled row is **orphaned** when its `JobId` is null (job retention deleted the job) or its job
  is not live (a job-level cancel or failure, §7.1).

### 1.3 Principles

1. **The row is the truth.** The verdict lives in `State`; the job succeeds for every settled row.
2. **Fail closed on an interrupted send.** A row found in `Sending` at claim fails and is never
   re-sent automatically; an administrator decides. A `TransientFailure` is retried, accepting a
   possible duplicate (spec 0007 §1.3 rule 5).
3. **Fixed write paths.** Rows arrive only through the stack pipeline (§3); there is no `save`
   endpoint, so §2.1's write-once and server-owned markings bind only the enlisted insert. The
   sending job and the delivery handler (§6) then write only server-owned columns and leave the
   audit columns alone (spec 0012 §2.1). `retry` and `cancel` are user actions and stamp the row.
4. **Nothing non-transactional inside a save.** Staging precedes the persist; sending is a job.

### 1.4 Statement execution rules

SQL blocks are the exact statements to emit, each run through spec 0012's
`IDataBatch.Sql(sql, options)` with `SqlOptions.Writes` naming the tables written. Local names take
the reserved `@tb{b}_` prefix, `{b}` being the batch ordinal. The pre-mark, settle and delivery
update write `core.OutgoingEmails` from `Tellma.Core` through spec 0012's internal channel, as
`IJobQueue` and `INotifier` do (spec 0015 §13.3); `retry` and `cancel` use the row's stack.

| Statement | `Purpose` | `TransactionMode` | `Idempotent` | `Writes` | Scope |
|---|---|---|---|---|---|
| enqueue (enlisted save, §3) | the caller's (`Persist`) | the caller's | `false` | `core.OutgoingEmails`, `core.OutgoingEmailAttachments`, `core.Jobs`; with attachments also `core.Blobs` (the blob effect's confirm) | the caller's frame |
| dispatch load (§5 step 2) | `Read` | `Auto` | `true` | none | the job scope |
| pre-mark, settle (§5) | `Maintenance` | `None` | `true` | `core.OutgoingEmails` | the job scope |
| delivery update (§6) | `Maintenance` | `None` | `true` | `core.OutgoingEmails` | the tenant database, no scope |
| `retry` (§7.1) | the action's (`Persist`) | `Auto` | `false` (`Update`, then spec 0020 §4.2's enqueue) | `core.OutgoingEmails`, `core.Jobs` | the caller's |
| `cancel` (§7.1) | the action's (`Persist`) | `Auto` | `false` (`Update`, spec 0012 §6.5) | `core.OutgoingEmails` | the caller's |
| retention page (§7.3) | `Read` for ids, then the pipeline delete | `Auto` | `true` for the id read; the delete is not (spec 0012 §6.5) | `core.OutgoingEmails`, `core.OutgoingEmailAttachments`, `core.Blobs` | the system job scope |

Neither table carries a version-tag attribute or bumps a tenant tag (spec 0013 §2.3's list).

## 2. The row

### 2.1 The entity

```csharp
// Tellma.Core.Abstractions.Email
public enum OutgoingEmailState { Pending, Sending, Sent, Sandboxed, Rejected, Failed, Cancelled }
public enum EmailDeliveryStatus { Deferred = 1, Delivered, Dropped, Failed, Bounced, SpamReported }  // weak to strong

// Table core.OutgoingEmails: non-temporal, [TableType], sequence core.sq_OutgoingEmails, Id int PK
// with CK_OutgoingEmails_Id.
// Indexes: IX_OutgoingEmails_State (State, CreatedAt);
//   IX_OutgoingEmails_Regarding (RegardingEntity, RegardingId) WHERE RegardingEntity IS NOT NULL;
//   IX_OutgoingEmails_Recipient (RecipientUserId, CreatedAt DESC) WHERE RecipientUserId IS NOT NULL.
// [Stack(Operations = Query | Details | Delete | Enlist, DefaultOrderBy = "CreatedAt desc")],
// [ApiResource(Mcp = ReadOnly)], [DisplayNameSource(nameof(Subject))] (spec 0012 §2.2).
// [DefaultSelect]: every member except TextBody, HtmlBody, Cc, Bcc, ProviderMessageId and
// LastProviderEventId.
// Audit columns (spec 0012 §4.2): CreatedById is the acting user (a job's run-as user).
public sealed class OutgoingEmail : TopLevelEntity, IJobEntity
{
    public int? JobId { get; set; }                          // IJobEntity; FK ON DELETE SET NULL; filtered UX_OutgoingEmails_JobId
    public OutgoingEmailState State { get; set; }            // varchar(9); server-owned

    // Write-once.
    public EmailAudience Audience { get; set; }              // varchar(8)
    public string? From { get; set; }                        // nvarchar(512); "Name" <address>; null = the transport's default (spec 0007 §2.3)
    public string? ReplyTo { get; set; }                     // nvarchar(512); null = none
    public string To { get; set; }                           // nvarchar(max); "Name" <address>, "Other" <address>
    public string? Cc { get; set; }                          // nvarchar(max)
    public string? Bcc { get; set; }                         // nvarchar(max)
    public string Subject { get; set; }                      // nvarchar(500)
    public string TextBody { get; set; }                     // nvarchar(max)
    public string? HtmlBody { get; set; }                    // nvarchar(max)
    public int? RecipientUserId { get; set; }                // FK core.Users, navigation RecipientUser; the member addressed
    public string? RegardingEntity { get; set; }             // varchar(128); the business record the email concerns
    public long? RegardingId { get; set; }
    public DateTimeOffset? SendAfter { get; set; }           // datetimeoffset(3); earliest dispatch; becomes JobRequest.DueAt

    // Server-owned, like State.
    public int AttemptCount { get; set; }                    // DF 0; claims that pre-marked the row; survives job retention
    public DateTimeOffset? SentAt { get; set; }              // datetimeoffset(3); SYSUTCDATETIME() in the settle (§5 step 5)
    public string? ProviderMessageId { get; set; }           // nvarchar(255); from the send result (spec 0007 §1.3)
    public bool ExpectsDeliveryEvents { get; set; }          // DF 0; from the send result
    public string? ErrorCode { get; set; }                   // nvarchar(64); an Email.* key
    public string? ErrorDetail { get; set; }                 // nvarchar(1024); the provider's text
    public EmailDeliveryStatus? DeliveryStatus { get; set; } // varchar(12); the strongest event so far
    public DateTimeOffset? DeliveryAt { get; set; }          // datetimeoffset(3); from that event, as are the next two
    public string? DeliveryRecipient { get; set; }           // nvarchar(255)
    public string? DeliveryReason { get; set; }              // nvarchar(512)
    public string? LastProviderEventId { get; set; }         // nvarchar(100); dedup

    [NotMapped, MaxChildren(20)]
    public IReadOnlyList<OutgoingEmailAttachment> OutgoingEmailAttachments { get; set; }
}

// Table core.OutgoingEmailAttachments: non-temporal, [TableType], sequence
// core.sq_OutgoingEmailAttachments, Id int PK with CK_OutgoingEmailAttachments_Id.
public sealed class OutgoingEmailAttachment : ChildEntity
{
    [ParentKey] public int OutgoingEmailId { get; set; }
    public string FileName { get; set; }                     // nvarchar(255); the name the recipient sees
    public string? ContentId { get; set; }                   // nvarchar(255); set for an inline cid: image
    [BlobReference("email-attachment", BlobPreset.Attachment, ReadAccess = BlobReadAccess.OwnerRead)]
    public int? FileId { get; set; }                         // navigation File; the outbox sets it when staging (§3.2)
}
```

Bodies stay on the row (Decisions record 2): a thousand emails a day with 50 KB of HTML, stored as
`nvarchar` at two bytes a character and kept 180 days, is about 18 GB. A tenant that wants less
shortens `Retention` (§9.1). An attachment's content type and size come from its blob row, and a
download authorises through the parent's `Read` grant (spec 0017 §5.1).

Queryex: root `core.OutgoingEmail` with navigations `Job`, `RecipientUser`, `CreatedBy`,
`ModifiedBy`; child root `core.OutgoingEmailAttachment` with `File` and `OutgoingEmail`.

### 2.2 Standalone table types

| Type | Used by | Columns |
|---|---|---|
| `OutgoingEmailOutcomeList` | settle (§5 step 5) | `Id int PK`, `State varchar(9)`, `ProviderMessageId nvarchar(255) NULL`, `ExpectsDeliveryEvents bit`, `ErrorCode nvarchar(64) NULL`, `ErrorDetail nvarchar(1024) NULL` |
| `EmailDeliveryEventList` | delivery update (§6) | `Ordinal int PK`, `OutgoingEmailId int`, `Status varchar(12)`, `Rank tinyint`, `Recipient nvarchar(255) NULL`, `Reason nvarchar(512) NULL`, `Timestamp datetimeoffset(3) NULL`, `ProviderEventId nvarchar(100)` |

### 2.3 States

| State | Entered from | What an administrator reads |
|---|---|---|
| `Pending` | the enqueue; `Sending` after a transient failure with attempts left (the job retries with backoff); `retry` of a `Failed` or orphaned row (a new job) | committed, not yet handed to a provider; `SendAfter` or the job's `DueAt` says when |
| `Sending` | `Pending`, at the pre-mark (§5 step 3) | being sent now; still `Sending` after its job ended means the send was interrupted |
| `Sent` | `Sending` | the provider accepted it; events follow when `ExpectsDeliveryEvents = 1`, otherwise nothing more will be known |
| `Sandboxed` | `Sending` | never reached a real recipient: sent through the transport's sandbox channel or withheld (spec 0007 §3.3) |
| `Rejected` | `Sending` | the provider or the outbox refused it permanently; `ErrorCode` says why |
| `Failed` | `Sending`, when attempts run out (`Email.AttemptsExhausted`) or the next claim finds it there (`Email.Interrupted`) | `retry` resends; the delivery columns may fill if an interrupted send went out |
| `Cancelled` | `Pending`, by `cancel` | cancelled before it was sent |

Example: an invoice email to an address that no longer exists settles `Sent` once SendGrid accepts
it. Minutes later a bounce event sets `DeliveryStatus = Bounced` and the reason, while `State` stays
`Sent`. The administrator fixes the customer's address and sends the invoice again, as a new row.

## 3. Enqueueing

### 3.1 Contracts

```csharp
// Tellma.Core.Abstractions.Email

// A blob the caller can download: a Staged email-attachment blob the caller uploaded, attached
// as is, or a Committed blob of any kind (an invoice's own PDF) under the kind's read access
// (spec 0017 §5.2), copied into a new staged blob (§3.2 step 2).
public sealed record EmailBlobAttachment(string Kind, int BlobId, string? FileName = null, string? ContentId = null);

// The outbox stages Message.Attachments (bytes) and ignores Message.Correlation (§5 step 4).
public sealed record EmailEnqueueRequest(
    EmailMessage Message,
    IReadOnlyList<EmailBlobAttachment>? BlobAttachments = null,  // null = none
    int? RecipientUserId = null,                                 // the member addressed, when there is one
    string? RegardingEntity = null, long? RegardingId = null,
    DateTimeOffset? SendAfter = null,                            // null = as soon as the worker runs
    string Source = "api");                                      // tellma.outbox.enqueued's source tag (§9.2)

public interface IEmailOutbox                                    // scoped; EmailOutbox : IEnlists<OutgoingEmail>
{
    // Validates, stages attachments and enlists one OutgoingEmail row per request in the caller's
    // frame, so the rows and their jobs commit with the caller's transaction (§3.2).
    Task<EnlistedSave<OutgoingEmail>> EnqueueAsync(IWriteHost host, IReadOnlyList<EmailEnqueueRequest> requests);
}
```

Validation (§3.2 step 1, before anything is staged) throws a `ValidationException`:

- `Email.InvalidMessage` — spec 0007 §1.3 rule 3's structural rules (a non-empty `To`, no blank
  address), plus a non-blank `Subject` and `TextBody`.
- `Email.TooManyRecipients` — `To + Cc + Bcc` above `MaxRecipients` (§9.1).
- `Email.TooLarge` — bodies plus every attachment's size above `MaxMessageBytes`; a blob
  attachment's `Size` comes from its `ResolveAsync` load.

Blob failures carry spec 0017's codes. Staging an `EmailAttachment` or a copy can throw
`BlobRejectedException` (spec 0017 §3.2, §8.2) with `Blob.TooLarge`, `Blob.StagingQuotaExceeded` or
`Blob.UnsupportedType`, the last for a type outside the `Attachment` preset's allow-list, which
always refuses HTML (spec 0017 §2.4). A blob attachment that `ResolveAsync` does not return fails
`Blob.NotAttachable` (`reason = missing`), and the frame's validator adds `Blob.NotAttachable` and
`Blob.DuplicateReference` (spec 0017 §4.2).

### 3.2 From an action or a job

A producer makes one call. An invoice service's `send` action:

```csharp
[EntityAction("send", SingleTarget = true)]                          // SingleTarget: the method returns its own result
public async Task<int> SendAsync(ActionContext<Invoice, int> context, SendInvoiceArguments args)
{
    var invoice = context.Entities[0];
    var pdf = await _renderer.RenderAsync(invoice);                  // bytes in memory
    var message = new EmailMessage
    {
        To = [new EmailAddress(args.To, invoice.CustomerName)],
        Subject = _strings["Invoice_Email_Subject", new { code = invoice.Code }],  // ServerStrings, ICU named arguments (spec 0013 §10.2)
        TextBody = _strings["Invoice_Email_Text", new { code = invoice.Code, total = invoice.Total }],
        Audience = EmailAudience.External,
        Attachments = [new EmailAttachment($"{invoice.Code}.pdf", "application/pdf", pdf)],
    };
    var saved = await _outbox.EnqueueAsync(context.Host,
        [new EmailEnqueueRequest(message, RegardingEntity: "acme.Invoice", RegardingId: invoice.Id)]);
    return saved.Rows[0].Id;                                         // the outgoing email's id
}
```

What `EnqueueAsync` does:

1. Resolves every `EmailBlobAttachment` with `ResolveAsync`, which checks read access and returns
   the `Size` (spec 0017 §5.1 serves a staged blob to its uploader), then validates (§3.1).
2. Stages before the frame persists (spec 0017 §4.6): each `EmailAttachment` through
   `IBlobService.StageAsync` under the acting user, at one round trip and one store write each, and
   a copy of each committed `EmailBlobAttachment` through `OpenReadAsync` and `StageAsync`
   (Decisions record 3).
3. Builds `Pending` rows with RFC 5322 address lists and calls `EnlistSaveAsync(host, rows)` (spec
   0015 §13.3 admits `EmailOutbox` as a receiver).
4. The frame's validation round checks the blob references against the attach rule (spec 0017 §4.2),
   and its persist runs the effect that confirms the staged blobs (spec 0017 §4.4).
5. In the same persist, `OutgoingEmailService.ContributeAsync` calls `IJobQueue.Enqueue` on
   `context.Batch` for each new row whose `JobId` is null: `HandlerKey = "core.outgoing-email"`,
   `Entity` = the row, `RunAsUserId = SystemUserId`, `RequestedById = null` and
   `DueAt = row.SendAfter`. The job queue sends the nudge (spec 0020 §4.4).

On rollback only the staged blobs remain, and spec 0017's sweep expires them. With attachments the
frame pays one extra round trip, the blob validator's load; without them it pays none. Blobs staged
in step 2 count against the acting user's staging quota (spec 0017 §3.4), which exempts a `System`
scope, so a producer running as a person slices its enqueues when one frame may stage more than 200
files or 512 MiB.

Other callers:

- **A job** makes the same call on its injected `IOpenWriteHost`; the rows ride the completion
  transaction, and the job's run-as user both uploads and saves.
- **Provisioning** makes the same call while the tenant is still `Provisioning`, from a step on the
  step runner's frame (spec 0015 §13.3) and from the administrator's `invite` that `provision` runs
  after the steps (spec 0018 §6.1). Its rows wait `Pending` until the tenant is `Active`, because
  spec 0020's worker polls only `Active` tenants.
- **A `ContributeAsync` hook or a `SaveOptions.OnPersist` delegate** cannot enlist, because the
  frame's rounds are over (spec 0015 §4.1, §13.3). A producer that decides to email there enqueues
  an arguments-only job whose handler calls the outbox.

## 4. The email channel

`EmailNotificationChannel.DeliverAsync` (spec 0021 §4.4) runs in spec 0021 §4.5's delivery job, with
the recipients that job resolved for `email` from current addresses and preferences. The channel:

1. Groups them by `LocalizationContext`. Per group, inside `LocalizationScope.Enter(context)`
   (spec 0013 §10.2), it renders the text once with `INotificationRenderer.Render` (spec 0021
   §2.4). It then renders the subject and bodies from Core's `ServerStrings` keys
   `Email_Notification_Subject`, `Email_Notification_Text` and `Email_Notification_Html`, given:
   - `tenantName`;
   - `text`, the rendered notification text;
   - `link`, which is `ITellmaLinks.Target(tenantId, TargetResource, TargetId)` (spec 0011
     §5.10): the target's details page, else the tenant root.
2. Makes one `EnqueueAsync(host, …)` call with one request per recipient that has an address: `To`
   the single address `ContactEmail ?? Email`, `Audience = Internal`, `RecipientUserId`,
   `RegardingEntity` and `RegardingId` the request's target, and `Source = "notification"`.

**Example —** Sara (`PreferredLanguage = ar`, no `ContactEmail`) turned email on for Acme's
channel-only type `acme.invoice.assigned` (spec 0028 §3.6). Sales invoice 17 of tenant 42 is
assigned to her. Her email:

- goes to `sara@acme.example`;
- carries the text of `Notification_acme_invoice_assigned` in Arabic, with dates in her calendar
  and zone;
- has the subject `Email_Notification_Subject` with `tenantName` "Acme";
- links to `https://acme.app.tellma.com/42/sales-invoices/17`.

The inbox's dedup and mutability rules do not apply (spec 0021 §2.3, §4.4): a user who opted in is
emailed every time. A notification email has no attachments.

## 5. Dispatch

```csharp
// Tellma.Core.Outbox
[JobHandler("core.outgoing-email", BatchSize = 25, LeaseSeconds = 120, MaxConcurrency = 1,
            MaxAttempts = 12, RetryBaseSeconds = 60, RetryMaxSeconds = 7200,
            InboxOnlyFailureNotices = true)]
public sealed class OutgoingEmailHandler : IEntityJobHandler<OutgoingEmail>;
```

Each job runs as the system user with `RequestedById = null` (Decisions record 7). Per claim:

1. **Triage.** A settled row means a re-run or a cancel: `Succeed()` without sending. A row still in
   `Sending` was interrupted and may have reached the wire: settle it `Failed` with
   `Email.Interrupted`, log `OutboxSendInterrupted`, `Succeed()` and notify (step 6). Only `Pending`
   rows go on.
2. **Load.** The claim's entity join load (spec 0020 §3.2) selects every scalar member of
   `OutgoingEmail`, not only `[DefaultSelect]`, so the bodies, `Cc` and `Bcc` arrive with the row.
   One `Read` round trip then loads the `Pending` rows' attachments, joined to `core.Blobs` for
   `StorageKey` (outside Queryex, spec 0017 §5.1), `ContentType` and `Size`, and each attachment is
   read into memory with `IBlobStore.OpenReadAsync`, so only the send lies between the pre-mark and
   the wire. A missing object (`null`, spec 0017 §6.1) makes the row **unsendable**: it is
   pre-marked with the others but not sent, and settles `Rejected` in step 5.
3. **Pre-mark.** One `Maintenance` round trip of two statements over `@ids`: an `UPDATE` that sets
   `State = 'Sending'` and adds one to `AttemptCount` where `[State] = 'Pending'`, then a `SELECT`
   of the ids now in `Sending`. Only those rows go on; a row left out was cancelled meanwhile and
   gets `Succeed()` without sending. Because the `SELECT` reads the end state, not what changed, the
   executor's re-run of this `Idempotent` batch (spec 0020 §1.4) returns the same rows.
4. **Send.** One `IEmailSender.SendAsync(messages)` for the sendable rows, each correlated as
   `new EmailCorrelation("outbox", id.ToString(CultureInfo.InvariantCulture), tenantId)`; the router
   applies sandbox routing (§7.2). No cancellation token reaches the sender, so stopping the host
   never aborts a batch mid-send (spec 0007 §1.3 rule 2's exemption); a process that exits mid-send
   leaves its rows `Sending` for the next claim's triage. An exception before any send (spec 0007
   §1.3 rule 2) is a `TransientFailure` for every row.
5. **Settle.** One `Maintenance` round trip over `OutgoingEmailOutcomeList`, matched on the id and
   `[State] = 'Sending'`, so a re-run is a no-op:

   | `EmailSendResult.Outcome` | Row | Job |
   |---|---|---|
   | `Sent` | `Sent`; `SentAt`, `ProviderMessageId`, `ExpectsDeliveryEvents` | `Succeed()` |
   | `Sandboxed` | `Sandboxed`; `SentAt` | `Succeed()` |
   | `Rejected` | `Rejected`; `Email.Rejected`, the provider's detail | `Succeed()` + notify |
   | none: unsendable (step 2) | `Rejected`; `Email.AttachmentMissing` | `Succeed()` + notify |
   | `TransientFailure`, attempts left | `Pending`; `Email.TransientFailure`, detail | `Retry()` (backoff) |
   | `TransientFailure`, `Job.Attempts >= MaxAttempts` | `Failed`; `Email.AttemptsExhausted`, detail | `Succeed()` + notify |

6. **Notify.** If any row settled `Rejected` or `Failed`, one `NotifyOnSuccess` on the first such
   item raises `core.email.failed` (§9.3) to `IAdministratorDirectory.GetAdministratorIdsAsync()`,
   with arguments `{ count: n }`, target `core.OutgoingEmail` / that row's id, `InboxOnly = true`,
   `DedupKey = "core.email.failed"` and `OnDuplicate = Replace`. Administrators see one unread
   notice however many claims fail, with the latest count and row, and never an email.

The settle runs before the completion, so a known verdict survives a lost or re-done completion; the
completion carries only job outcomes and the notices. Per batch, the load, pre-mark, settle and
completion take one round trip each, plus one store read per attachment.

## 6. Delivery events

`OutgoingEmailDeliveryHandler : IEmailDeliveryEventHandler`, with `OwnerKey = "outbox"`, runs on
spec 0007 §7's tenantless webhook path. Its `HandleAsync(IReadOnlyList<EmailDeliveryEvent> events)`:

1. Drops `Opened`, `Clicked` and `Other` events, groups the rest by `Correlation.TenantId`, and
   keeps the strongest event per row (by rank, then `Timestamp`).
2. Looks up each tenant with `ITenantRegistry.Find`. A missing, `Provisioning` or `Retired` tenant's
   events are dropped with a Warning (`OutboxDeliveryTenantSkipped`). `Active` and `Suspended`
   tenants are updated, since the update is bookkeeping and a suspension outlasts the provider's
   24-hour redelivery window. A `ReadOnly` tenant's events wait for redelivery: the handler applies
   every other tenant's events first, then throws once, so spec 0007's receiver answers 503.
3. Opens each tenant to update with `ITenantDatabaseFactory.OpenAsync` and runs one `Maintenance`
   statement over `EmailDeliveryEventList`, with no tenant scope and no principal. It skips applied
   events and moves a row only to a stronger status or a later event of the same status:

```sql
UPDATE [e]
SET [DeliveryStatus] = [v].[Status], [DeliveryAt] = COALESCE([v].[Timestamp], SYSUTCDATETIME()),
    [DeliveryRecipient] = [v].[Recipient], [DeliveryReason] = [v].[Reason],
    [LastProviderEventId] = [v].[ProviderEventId]
FROM [core].[OutgoingEmails] AS [e] INNER JOIN @tb{b}_t0 AS [v] ON [v].[OutgoingEmailId] = [e].[Id]
WHERE ([e].[LastProviderEventId] IS NULL OR [e].[LastProviderEventId] <> [v].[ProviderEventId])
  AND ([e].[DeliveryStatus] IS NULL
       OR CASE [e].[DeliveryStatus] WHEN 'Deferred' THEN 1 WHEN 'Delivered' THEN 2 WHEN 'Dropped' THEN 3
               WHEN 'Failed' THEN 4 WHEN 'Bounced' THEN 5 WHEN 'SpamReported' THEN 6 END < [v].[Rank]
       OR ([e].[DeliveryStatus] = [v].[Status] AND [e].[DeliveryAt] < [v].[Timestamp]));
SELECT @@ROWCOUNT AS [Applied];
SELECT [v].[OutgoingEmailId] FROM @tb{b}_t0 AS [v]                   -- unmatched references
WHERE NOT EXISTS (SELECT 1 FROM [core].[OutgoingEmails] AS [e] WHERE [e].[Id] = [v].[OutgoingEmailId]);
```

The update applies in any state, so an interrupted row whose message did go out still records its
`Delivered`. `Applied` counts the updated rows, and the second result set lists the references that
match no row, each logged as a Warning (`OutboxDeliveryEventUnmatched`, spec 0007 §1.4's anomaly);
the remaining events were duplicates or stale and are logged at Debug. Exceptions propagate, and a
redelivery is harmless because applied events are skipped.

## 7. Retry, cancel, delete, retention, sandbox

### 7.1 The verbs

```csharp
// Tellma.Core.Outbox
public sealed class OutgoingEmailService : EntityService<OutgoingEmail>   // Query | Details | Delete | Enlist
{
    // ContributeAsync enqueues the jobs (§3.2 step 5); ValidateDeleteAsync checks the delete precondition.
    public Task RetryAsync(ActionContext<OutgoingEmail, int> context);    // [EntityAction] "retry"
    public Task CancelAsync(ActionContext<OutgoingEmail, int> context);   // [EntityAction] "cancel", Destructive
}
```

`retry` and `cancel` follow spec 0020 §11.1's two-round-trip shape. The first round trip loads the
targets under the grant filter and checks the precondition. The second appends `IDataBatch.Update`
(`UpdateSpec.ByIds`, spec 0012 §9.1), filtered by the grant and the precondition, that sets the
state and error columns and stamps the row as any user action does (spec 0012 §2.1).

- **`retry`** (`core.OutgoingEmail × Retry`). Precondition: the row is orphaned (§1.2), or `Failed`
  with no live job (its completion has landed); else `Email.NotRetryable`. Effect:
  `State = 'Pending'`, the error and delivery columns cleared, `AttemptCount` kept, and one new job
  through `IJobQueue.Enqueue`, which allows a null or terminal `JobId` (spec 0020 §4.1).
- **`cancel`** (`core.OutgoingEmail × Cancel`, `Destructive`). Precondition: `Pending`, else
  `Email.NotCancellable`; a `Sending` row may already be on the wire. Effect: `Cancelled`; the job,
  when claimed, meets a settled row and succeeds.
- **`delete`** (`core.OutgoingEmail × Delete`, `Destructive`). Precondition: settled or orphaned
  (§1.2), else `Email.NotDeletable`. The delete runs through the pipeline, so spec 0017's effect
  releases the attachments. This is the erasure path for a data-removal request.

A whole job can also fail (spec 0020 §5.1): `Jobs.Internal` exhaustion, or a crash on the last
attempt that the next claim fails with `Jobs.AttemptsExhausted`. Administrators learn of it from
`core.job.failed` (spec 0020 §10); the row stays `Pending` or `Sending`, orphaned, until a `retry`.

### 7.2 Sandbox

The handler sends inside the tenant's job scope, so spec 0007's router marks internal mail and sends
external mail through the transport's sandbox channel or withholds it (spec 0007 §3.3). A sandbox
cloned from live inherits the live rows; the clone command, when it ships, cancels the copy's
unsettled rows and their jobs (spec 0011 §6.3), so staff never receive marked duplicates of
scheduled mail. Rows restored from an old backup stay `Pending` while their jobs are `Held` (spec
0020 §6.10), until an administrator resumes or cancels them.

### 7.3 Retention

```csharp
[JobHandler("core.outgoing-email-retention", LeaseSeconds = 600)]
public sealed class OutgoingEmailRetentionHandler : IJobHandler;          // BuiltInSchedule(7, …, "45 3 * * *")
```

Built-in schedule id 7 (spec 0020 §7.9) runs it after `core.job-retention`, as the system user,
reporting progress per page. Each page reads up to 500 ids of settled or orphaned rows (§1.2) whose
`CreatedAt` is older than `Retention` by the database clock, which is the `delete` verb's
precondition, so `ValidateDeleteAsync` never fails a page. It then calls `DeleteByIdsAsync` on the
stack, as `core.file-retention` does (spec 0020 §14.4), so the blob effect releases the attachments
for the sweep.

## 8. Admin surface

- **Securables**: `core.OutgoingEmail × Read | Delete | Retry | Cancel`, none sensitive, no
  self-scope criterion, no seeded grant beyond administrators' `*`. Members do not see mail
  addressed to them; that would be a second inbox.
- **Endpoints** (spec 0016): `outgoing-emails/query`, `get`, `get-by-ids`, `delete`,
  `delete-by-query`, `retry`, `cancel`. Attachments download through spec 0017's blob endpoint.
- **Pages**: spec 0032 §10 owns the Outgoing emails pages.
- **MCP**: read-only (`Mcp = ReadOnly`); `tellma_query` answers "was the invoice to X delivered".

## 9. Options, telemetry, composition

### 9.1 `OutboxOptions`

```csharp
// bound from Tellma:Outbox
public sealed class OutboxOptions
{
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(180);  // rows older than this are deleted (§7.3); 1 day to 10 years
    public long MaxMessageBytes { get; set; } = 7 * 1024 * 1024;       // bodies + attachment sizes per email; 1 KiB to 20 MiB
    public int MaxRecipients { get; set; } = 50;                       // To + Cc + Bcc; 1 to 1000
}
```

A value outside its range fails startup. The defaults are ACS's caps (Decisions record 13); the
upper bounds are SendGrid's, whose 30 MB cap on the encoded message (spec 0007 §5.2) is about 20 MiB
before base64.

### 9.2 Telemetry and logging

Meter `Tellma.Core`, with constants in `OutboxTelemetryNames`. The tenant is never a tag, and
addresses, subjects and bodies never appear above Debug (spec 0007 §9). Spec 0007 already meters
sends and events under `email.owner = "outbox"`, and spec 0020 meters the jobs under `handler`.

| Instrument | Kind | Tags |
|---|---|---|
| `tellma.outbox.enqueued` | counter | `source`: the request's `Source`, `api` or `notification` (§4) |
| `tellma.outbox.settled` | counter | `state` |
| `tellma.outbox.latency` | histogram (s), `CreatedAt` to `SentAt` | none |
| `tellma.outbox.delivery_updates` | counter | `result = applied \| stale \| unmatched \| tenant_skipped` |
| `tellma.outbox.retained` | counter | none |

Log events, all Warning: `OutboxSendInterrupted(rowId)`, `OutboxDeliveryEventUnmatched`,
`OutboxDeliveryTenantSkipped(tenantId, state)`. Alerts: any interrupted row; the `Failed` and
`Rejected` rate; latency p95; unmatched events.

### 9.3 Composition and startup

`CoreFeature` (in `Tellma.Core.Composition`) contributes:

- `Entity<OutgoingEmail, OutgoingEmailService>()`;
- the scoped `IEmailOutbox` and `IEmailDeliveryEventHandler` registrations;
- `JobHandler<OutgoingEmailHandler>()` and `JobHandler<OutgoingEmailRetentionHandler>()`, with the
  built-in schedule of §7.3;
- `NotificationType(new("core.email.failed", "jobs", false, "core.OutgoingEmail", …))` with
  `Severity = Error` and `Toast = true`, arguments `{ count }`, and the resources
  `Notification_core_email_failed` and `NotificationType_core_email_failed` in Core's `Strings`
  (spec 0021 §2.3, §2.4, §4.3);
- the `Email_<Code>` resources in Core's `Strings` for the codes of §3.1, §5 and §7.1
  (`Email_InvalidMessage` … `Email_NotDeletable`), rendered as spec 0013 §10.1 renders a code;
- `BlobKind("email-attachment", p => p with { Thumbnail = null })` (spec 0017 §1.3), since an email
  attachment needs no preview; the kind itself comes from the attribute;
- the two standalone table types, and `OutboxOptions` (§9.1).

Startup check `outbox.sender`: `JobsOptions.Enabled` needs an `IEmailSender` (in Development, spec
0007 §2.4's log sink). The tables ship in a later migration (spec 0012 §4.3's N−1 rule).

## 10. Testing

**Unit** (`test/core/Tellma.Core.Tests/Outbox/`):

- §3.1 validation codes; address-list formatting and parsing; `OutboxOptions` bounds (§9.1).
- The strongest-event choice and the `Opened`/`Clicked`/`Other` drop.
- §6's tenant-state policy with a fake registry: `Retired` drops, `Suspended` applies, `ReadOnly`
  throws after the others.
- §4 with a fake `IEmailOutbox`: one request per recipient, to `ContactEmail ?? Email`, rendered in
  that recipient's language, calendar and zone, linking to `ITellmaLinks.Target`; a recipient with
  no address is skipped.

**Integration** (`test/core/Tellma.Core.IntegrationTests/Outbox/`, LocalDB):

- Enqueue with a byte attachment, a user-staged blob and a copied committed blob: commit confirms
  all three; rollback leaves only staged rows for the sweep; no thumbnail is rendered.
- Dispatch: `CapturingEmailSender.OnSending`, scripted per message, yields every outcome of §5 step
  5, including attempts exhausted, a sender exception before any send and a missing attachment
  object. A re-run of the pre-mark returns the same rows. A `Sending` row at claim settles
  `Email.Interrupted` with no send; `cancel` then claim sends nothing. A `Rejected` row raises one
  inbox notice and no delivery job.
- Delivery events: `DeliveryEvents.For(correlation)` with duplicates, out-of-order events and
  unknown references leaves the strongest status once and returns the unmatched reference; a sandbox
  tenant yields `Sandboxed` with `ExpectsDeliveryEvents = 0`.
- Verbs: `retry` on `Failed` makes a new job and on `Rejected` fails `Email.NotRetryable`; `delete`
  on `Pending` with a live job fails `Email.NotDeletable`; `retry` and `cancel` stamp `ModifiedAt`.
- Retention: deletes settled and orphaned rows, releases blobs, and keeps an unsettled row with a
  live job without failing the page.
- Spec 0013's write audit sees exactly the declared tables.

Notification emails are pinned end to end by spec 0021's delivery-job suite, and the welcome email
by spec 0018 §9's integration suite.

## 11. Definition of done

- **Projects**: the contracts in `Tellma.Core.Abstractions.Email` and `.TableTypes`, the runtime in
  `Tellma.Core.Outbox`, the two test folders; READMEs, XML docs on every member, Windows and Linux
  under warnings-as-errors, wired into `Tellma.slnx`.
- **Behavior**: §2 to §9 implemented and pinned by §10, green in CI.
- **Observability**: every instrument of §9.2 emitted and asserted; the log events asserted; alert
  queries under `infra/monitoring/`.
- **Docs**: ARCHITECTURE.md updated where this spec touches it (the outbox namespace inside
  `Tellma.Core`; the `Tellma.Core` meter gains `tellma.outbox.*`). Public XML docs and error
  messages reference no `docs/` paths.
- **Not in scope of done**: the Non-goals.

## Decisions record

1. **`OutgoingEmail` / `core.OutgoingEmails`** — reads well for pending and sent rows alike. `Email`
   is the namespace, `EmailMessage` is spec 0007's wire type, and `EmailOutboxMessage` yields
   three-noun labels. `IEmailOutbox` and owner key `outbox` stay (§2.1, §3.1, §6).
2. **Bodies on the row, no blob** — staging rejects HTML and runs in its own transaction; a column
   is queryable and cheap at this volume (§2.1).
3. **Attachments through blob staging, one owner per blob, committed blobs copied** — one file
   mechanism; only a `Staged` blob can be confirmed (spec 0017 §4.4), and a copy preserves what was
   sent when the document's file is later replaced (§2.1, §3.2).
4. **A top-level entity on an `Enlist` stack, enqueued only from a frame** — blob confirm runs only
   in the pipeline; a persist hook hops through a job (§3.2).
5. **One row shape; notifications reach the outbox through spec 0021's delivery job** — the email
   channel renders in C# like any producer, in each recipient's language, calendar and zone, so
   every row records what was sent and dispatch only sends (§4).
6. **The verdict lives on the row and is settled before completion** — `Rejected` and `Failed` rows
   are job successes; the administrator retries an email, not a job (§5).
7. **System user, `RequestedById = null`, batches of 25, twelve attempts** — one partition per claim
   (spec 0020 §3.5) and no flooded "My jobs"; the attempts span about five hours on average and ten
   at most, riding out a provider outage (spec 0020 §5.5's backoff); a host stop fails at most 25
   rows closed, and at most 25 messages' attachments sit in memory per batch (§5).
8. **Interrupted sends fail closed; transient failures retry** — no provider offers an idempotency
   key, and a duplicate invoice is worse than a row an administrator resends; a `TransientFailure`
   is usually a refusal before acceptance (§5 steps 1 and 5).
9. **One deduplicated `core.email.failed` per claim to administrators, never by email** — an outage
   is not a storm, and a failing transport must not feed itself, so the notice and the handler's
   `core.job.failed` are raised `InboxOnly` (§5).
10. **Delivery events apply monotonically in any state, to `Suspended` tenants too, through
    `ITenantDatabaseFactory`** — cheap indexed updates on the webhook path, with no scope and no
    principal; `Opened` and `Clicked` are dropped because corporate link scanners open mail (§6).
11. **One 180-day window, rows deleted through the pipeline** — a body-purge window would cost an
    option and a statement for a few dollars of storage, and providers keep content for at most 45
    days, so the row is the only durable record (§7.3).
12. **Addresses as RFC 5322 text** — readable in a grid, filterable with `contains` (§2.1).
13. **`Mcp = ReadOnly`** keeps the verbs with people; **ACS's caps are the defaults** (§8, §9.1).
14. **Notification emails link to their target** — `ITellmaLinks.Target` builds the SPA's details
    route, so the email opens the record, or the tenant when the type has none (§4).

## Review flags

None.
