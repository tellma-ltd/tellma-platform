# Spec: Email Infrastructure — Core Contract and the First Connectors

- **Author:** Ahmad Akra
- **Date:** 11 August 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every deployable in the Tellma fleet sends email: the identity server sends sign-in codes,
invitation links, and recovery mail; every distribution will send system notifications today and
customer-facing documents (invoices, statements) once the durable email outbox lands. Until now the
only email code in the platform is private to the identity server engine — a minimal
`IEmailSender` with an SMTP implementation and a Development log sink, defined inside
`Tellma.Identity` (spec 0003 §10.6) because nothing shared existed to define it against.

This spec promotes email to platform infrastructure:

- **The contract** — transport-agnostic email types and extension-point interfaces in
  `Tellma.Core.Abstractions`, consumed by the identity server, every module pack, and every
  distribution.
- **The pipeline** — the runtime machinery in `Tellma.Core.Email` and `Tellma.Core.Webhooks`:
  provider selection from configuration, the sandbox-tenant routing policy, the Development log
  sink with its guard, the delivery-event dispatcher, and the HTTP fronting for inbound
  webhooks.
- **The first three connectors** — ACS Email (the Azure-native default for SaaS deployments,
  §6.1), SendGrid (the hosted fallback), and SMTP (the on-prem/air-gapped fallback and the
  local-dev inspection path) — as connector packages.

These are the platform's **first connector packages**. Their shape — package naming, the
raw-client/adapter split, live-vs-sandbox routing, configuration layout, webhook handling,
observability, and test strategy — sets the precedent every future connector (banks, e-invoicing,
payment, messaging) will follow. Details matter here beyond email itself.

Two contract families ship together because the second exists for the first: the **webhook
contract** (`IWebhookReceiver`) is transport-neutral and will serve every future connector that
receives callbacks, but its first consumer is the SendGrid delivery-event webhook. Specifying them
together keeps the seams honest — the webhook contract is proven against a real provider before
other connectors depend on it.

The durable email outbox (`IEmailOutbox`) is **specified here but not built**: its interface
appears in this spec to pin the intended shape and to justify contract decisions (correlation,
delivery events, batch semantics) that only make full sense with the outbox in view. See §13.

## Goals / Non-goals

**Goals**

- Ship the email and webhook contracts in `Tellma.Core.Abstractions`, production-grade and
  XML-documented, as the permanent extension-point surface for all email transports.
- Ship the runtime pipeline as two lean packages — `Tellma.Core.Email` (config-driven provider
  selection, the sandbox routing policy, the Development log sink + guard, delivery-event
  dispatch) and `Tellma.Core.Webhooks` (webhook HTTP fronting) — composable without
  `Tellma.Core`.
- Ship `Tellma.Connector.Smtp.Adapter` (on MailKit), `Tellma.Connector.SendGrid` +
  `Tellma.Connector.SendGrid.Adapter` (first-party raw client, §5.1), and
  `Tellma.Connector.AcsEmail.Adapter` (on the Azure SDK, §6.1) — each with README, tests, and
  full observability.
- Ship `Tellma.Core.Testing` with the email test-capture tooling distributions and platform apps
  use in integration and E2E tests.
- Define the observability surface — metrics, logs, traces, and the alerts they enable — for
  email health across the fleet.
- Retire the identity server's private email types: the identity-server branch adopts the shared
  contract before it merges.
- Set the connector-precedent patterns: live/sandbox config duality, adapter package shape,
  webhook verification discipline, connector test strategy.

**Non-goals (explicitly out of scope)**

- **`IEmailOutbox` implementation** — durable queuing, the dispatch worker, retry/dead-letter
  policy, per-tenant quotas. The interface is specified (§13) but not compiled or built; it ships
  with its own spec.
- **External unsubscribe / suppression management** — unsubscribe links, `List-Unsubscribe`
  headers, suppression-list UI. Designed with the outbox, which owns external customer mail. The
  connectors already surface the raw material (drop/bounce/spam events) so nothing here blocks it
  (§11).
- **Synthetic monitoring** — scheduled probe sends from an external worker. Designed once
  several connectors exist and share the probe infrastructure.
- **Mass-marketing campaigns** — a different product surface (list management, IP warm-up,
  campaign scheduling), not the transactional pipe (§10).
- **Inbound mail** (receiving/parsing email) — nothing here reads mailboxes.

## 1. The contract — `Tellma.Core.Abstractions`

### 1.1 Placement and packages

| Piece | Project | Namespace |
|---|---|---|
| Email contract (§1.2–§1.4) | `Tellma.Core.Abstractions` | `Tellma.Core.Abstractions.Email` |
| Webhook contract (§1.5) | `Tellma.Core.Abstractions` | `Tellma.Core.Abstractions.Webhooks` |
| Deployment identity (§1.6) | `Tellma.Core.Abstractions` | `Tellma.Core.Abstractions.Hosting` |
| Sandbox-context seam (§3.1) | `Tellma.Core.Abstractions` | `Tellma.Core.Abstractions.Tenancy` |
| Email pipeline: routing, selection, guard, log sink, dispatcher (§2, §3, §8) | `Tellma.Core.Email` | `Tellma.Core.Email` |
| Webhook fronting (§7) | `Tellma.Core.Webhooks` | `Tellma.Core.Webhooks` |
| SMTP connector (§4) | `Tellma.Connector.Smtp.Adapter` | `Tellma.Connector.Smtp.Adapter` |
| SendGrid raw client (§5.1) | `Tellma.Connector.SendGrid` | `Tellma.Connector.SendGrid` |
| SendGrid connector (§5) | `Tellma.Connector.SendGrid.Adapter` | `Tellma.Connector.SendGrid.Adapter` |
| ACS Email connector (§6) | `Tellma.Connector.AcsEmail.Adapter` | `Tellma.Connector.AcsEmail.Adapter` |
| Test capture (§12.4) | `Tellma.Core.Testing` | `Tellma.Core.Testing.Email` |

Contracts live in `Tellma.Core.Abstractions` because every layer consumes email through them and
only the composition root picks implementations. All runtime machinery — including the Development
log sink — lives in two focused sibling packages under `src/core/`: **`Tellma.Core.Email`**
(provider selection, the router, the log sink and its guard, the delivery-event dispatcher, email
telemetry) and **`Tellma.Core.Webhooks`** (the HTTP fronting for `IWebhookReceiver`s, the one
piece that takes a `FrameworkReference` to `Microsoft.AspNetCore.App`). The split serves two
charters at once: Abstractions stays a pure interface surface (the sink belongs with the selection
pipeline that activates and guards it), and hosts that are not distributions — the identity
server, the landing app, a future webhook gateway (§5.5) — compose email with lean dependencies
instead of dragging in `Tellma.Core`'s distribution machinery (CRUD stack, multi-tenancy,
settings, jobs). Neither package is referenced by `Tellma.Core`; every host, distributions
included, adds them explicitly at composition. `Tellma.Core.Webhooks` is email-agnostic — future
connectors' receivers ride it unchanged.

Connector packages follow the platform naming convention `Tellma.Connector.<vendor>…Adapter`
(singular `Connector`, matching `Tellma.Module.*` / `Tellma.Industry.*`), under `src/connector/`
grouped one folder per vendor:

```
src/connector/
├── acs-email/
│   └── Tellma.Connector.AcsEmail.Adapter/      # csproj — Azure-SDK-based IEmailSender + Event Grid receiver
├── sendgrid/
│   ├── Tellma.Connector.SendGrid/              # csproj — raw client: v3 mail-send, webhook signature verification, error model
│   └── Tellma.Connector.SendGrid.Adapter/      # csproj — IEmailSender + IWebhookReceiver over the raw client
└── smtp/
    └── Tellma.Connector.Smtp.Adapter/          # csproj — MailKit-based IEmailSender
```

Grouping folders are **lowercase vendor names**; dotted-PascalCase folder names are reserved for
actual C# project folders, matching how the lowercase category folders (`src/core/`,
`src/connector/`) already read. The repo's existing placeholder folder `src/connectors/` is
renamed to `src/connector/` to match the platform's singular category-folder convention.

**Whether a connector ships the raw-client/adapter split or adapter-only is decided by the state
of the upstream client ecosystem, per vendor.** SMTP and ACS ship adapter-only: MailKit *is* the
.NET mail client — first-party quality, actively maintained, the library Microsoft's own docs
recommend — and `Azure.Communication.Email` is a maintained first-party SDK exposing everything
the adapter needs, including the client-supplied operation id the correlation design turns on
(§6.1); wrapping either would be indirection without value. SendGrid ships the full split: the
official SDK is dormant (§5.1), so the platform implements the small API surface it needs as a
raw `Tellma.Connector.SendGrid` client, which the adapter then maps to the platform contracts. A
raw client is written when the upstream client is absent or unfit, not on principle.

For protocol-shaped connectors (SMTP here; sftp, AS2, etc. later) the protocol name occupies the
`<vendor>` slot — the "external system" is the protocol itself.

### 1.2 Email types

```csharp
namespace Tellma.Core.Abstractions.Email;

/// <summary>
///     Identifies the owner and subject of an email for delivery-event routing. Attached at send
///     time, round-tripped through the transport (e.g. provider custom arguments), and returned on
///     delivery events so each event finds the record it updates.
/// </summary>
/// <remarks>
///     Canonical single-string form is <c>ownerKey:tenantId:reference</c>, with an empty middle
///     segment when <see cref="TenantId"/> is null. The first two colons delimit; the remainder is
///     the <see cref="Reference"/>, which may therefore contain colons. Contents must be
///     meaningless outside the platform — row keys and tenant numbers only, never user
///     identifiers, addresses, or token material — because the value transits third-party systems
///     and returns on an endpoint protected only by signature verification.
/// </remarks>
/// <param name="OwnerKey">The event owner; matches <see cref="IEmailDeliveryEventHandler.OwnerKey"/>.
///     Lowercase letters, digits, and hyphens; no colons; at most 32 characters.</param>
/// <param name="Reference">Owner-opaque subject reference, typically a row id. Non-empty, at
///     most 128 characters.</param>
/// <param name="TenantId">The tenant whose database holds the referenced state, when the owner's
///     state is tenant-sharded (the outbox); null for owners with unsharded state (identity).</param>
public sealed record EmailCorrelation(string OwnerKey, string Reference, int? TenantId = null)
{
    /// <summary>The canonical single-string form, for transports with one echo field.</summary>
    public override string ToString() => $"{OwnerKey}:{TenantId}:{Reference}";

    /// <summary>Parses the canonical form; false when the value is null or malformed.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out EmailCorrelation? result) { /* … */ }
}

/// <summary>An email address with an optional display name.</summary>
/// <param name="Address">The address ("someone@example.com").</param>
/// <param name="DisplayName">The display name shown by mail clients, when known.</param>
public sealed record EmailAddress(string Address, string? DisplayName = null);

/// <summary>A file attached to an email.</summary>
/// <param name="FileName">The file name presented to the recipient.</param>
/// <param name="ContentType">The MIME content type.</param>
/// <param name="Content">The file content.</param>
/// <param name="ContentId">A content id for inline use (e.g. images referenced by
///     <c>cid:</c> from the HTML body); null for ordinary attachments.</param>
public sealed record EmailAttachment(
    string FileName,
    string ContentType,
    ReadOnlyMemory<byte> Content,
    string? ContentId = null);

/// <summary>
///     Classifies whose inbox an email targets — the input to the sandbox-tenant routing policy.
///     Every send site states it explicitly; there is no default.
/// </summary>
public enum EmailAudience
{
    /// <summary>
    ///     Addressed exclusively to users of the platform itself — tenant staff and operators
    ///     signed into the sending system (system notifications, workflow alerts). On behalf of a
    ///     sandbox tenant this mail still goes out for real, marked as sandbox-originated.
    /// </summary>
    Internal,

    /// <summary>
    ///     Addressed to anyone outside the sending system — customers, suppliers, arbitrary
    ///     addresses (invoices, statements, portals). On behalf of a sandbox tenant this mail is
    ///     never really delivered. When in doubt — any mixed or unverifiable recipient list —
    ///     classify as external; the failure mode of over-classifying is an undelivered test
    ///     email, the failure mode of under-classifying is a test invoice in a real customer's
    ///     inbox.
    /// </summary>
    External,
}

/// <summary>One outgoing email, transport-agnostic.</summary>
public sealed record EmailMessage
{
    /// <summary>The sender; null uses the configured default of the registered transport.</summary>
    public EmailAddress? From { get; init; }

    /// <summary>The reply-to address, when replies should divert from <see cref="From"/>.</summary>
    public EmailAddress? ReplyTo { get; init; }

    /// <summary>The primary recipients. At least one is required.</summary>
    public required IReadOnlyList<EmailAddress> To { get; init; }

    /// <summary>The carbon-copy recipients.</summary>
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];

    /// <summary>The blind-carbon-copy recipients.</summary>
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];

    /// <summary>The localized subject.</summary>
    public required string Subject { get; init; }

    /// <summary>The plain-text body. Always present: text-only mail is fully valid, and
    ///     HTML-only mail hurts deliverability.</summary>
    public required string TextBody { get; init; }

    /// <summary>The optional HTML body, sent as a multipart alternative to
    ///     <see cref="TextBody"/>.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>The attachments, if any.</summary>
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];

    /// <summary>Whose inbox this email targets. Drives the sandbox-tenant routing policy;
    ///     required so the classification is a deliberate act at every send site.</summary>
    public required EmailAudience Audience { get; init; }

    /// <summary>The delivery-event correlation, minted by the owner of the email's state
    ///     (the outbox for outbox mail, identity for identity mail); null for fire-and-forget
    ///     mail that expects no delivery events.</summary>
    public EmailCorrelation? Correlation { get; init; }
}
```

Contract notes:

- `EmailCorrelation` validates in its constructor — `OwnerKey` against `^[a-z0-9-]+$` and at
  most 32 characters, `Reference` non-empty and at most 128 (`ArgumentException` otherwise) —
  so a malformed correlation fails at mint time, not when an event comes back unroutable. The
  length bounds also guarantee every transport encoding of the canonical form stays inside its
  carrier's limits (trivially for SendGrid's 10,000-byte custom args; within the RFC 5322
  message-id line for ACS, §6.3). `TryParse` accepts exactly what `ToString` produces: a valid
  owner key, an empty-or-integer middle segment, and a non-empty remainder.
- `EmailAudience` is the answer to "who decides internal vs external, and how do we make it
  intentional": the decision is inherently per-email and belongs to the code composing the message
  (only it knows who the recipients are), so the contract makes it **required with no default** —
  it cannot be forgotten, only stated, and every statement is visible in review and greppable in
  audit. §3 defines what the pipeline does with it.
- Correlations round-trip through transport-specific echo channels — SendGrid custom args,
  prefixed with the deployment id as a wire envelope (§1.6, §5.4); an encoded `Message-ID`
  header on ACS (§6.3) — invisible to the contract type and to handlers.

### 1.3 Sending

```csharp
namespace Tellma.Core.Abstractions.Email;

/// <summary>The per-message outcome of a send attempt.</summary>
public enum EmailSendOutcome
{
    /// <summary>Accepted by the transport for real delivery toward the recipient. Terminal
    ///     unless the result expects delivery events.</summary>
    Sent,

    /// <summary>Failed in a way that may succeed later (throttling, connection loss mid-batch);
    ///     the caller may retry this message.</summary>
    TransientFailure,

    /// <summary>Rejected permanently (e.g. invalid address); the caller must not retry.</summary>
    Rejected,

    /// <summary>
    ///     Handled by the sandbox policy instead of being delivered: routed to the transport's
    ///     sandbox channel (a provider validation mode, a mail trap) or withheld with no wire
    ///     activity — either way, no real email went out to the recipient. Success-class and
    ///     terminal: workflows treat it as success (failure handling branches on
    ///     <see cref="TransientFailure"/> and <see cref="Rejected"/>, never on inequality with
    ///     <see cref="Sent"/>), while user-facing status may render it distinctly — a sandbox
    ///     tenant's outbox reads "sandboxed", not "sent". Emitted only by the platform routing
    ///     policy; transports never produce it.
    /// </summary>
    Sandboxed,
}

/// <summary>The outcome of one message in a send batch, positional to the input list.</summary>
/// <param name="Outcome">What happened to the message.</param>
/// <param name="ProviderMessageId">The transport's own identifier for the accepted message, when
///     it reports one (SendGrid's <c>X-Message-Id</c>, the SMTP Message-Id). Diagnostic and audit
///     data — stored for support lookups and as a fallback correlation; nothing branches on it.</param>
/// <param name="Error">Human-readable failure detail when <paramref name="Outcome"/> is
///     <see cref="EmailSendOutcome.TransientFailure"/> or <see cref="EmailSendOutcome.Rejected"/>.</param>
/// <param name="ExpectsDeliveryEvents">True when delivery events may still arrive for this
///     message: it went out on the live channel of a transport whose delivery webhook is
///     configured, and it carries a correlation for events to return to. False otherwise —
///     sandboxed, uncorrelated, or eventless-transport mail is terminal at its send outcome.
///     Per message, because one batch can mix both (an internal and an external message of a
///     sandbox tenant).</param>
public sealed record EmailSendResult(
    EmailSendOutcome Outcome,
    string? ProviderMessageId = null,
    string? Error = null,
    bool ExpectsDeliveryEvents = false);

/// <summary>
///     Sends email on the wire. Implemented by connector adapters (SendGrid, SMTP, …) and by the
///     development log sink, but the implementation consumers receive via DI is the platform
///     router, which applies the sandbox policy and forwards to the active adapter — consumers
///     stay transport-agnostic either way. The contract is batch-shaped: bulk operations hand
///     the transport one call, never one call per message.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends a batch of messages and reports a per-message outcome.</summary>
    /// <remarks>
    ///     Implementations must return exactly one result per input message, in input order —
    ///     position is the correlation between input and outcome. Implementations may throw only
    ///     when no message went out (an up-front authentication or connection failure) or when
    ///     the caller cancels; once any message has gone out they must report per-message
    ///     outcomes instead of throwing, so callers can retry transient failures without
    ///     duplicating messages that already went out. Implementations must not retry beyond
    ///     sub-second transport pragmatics — durable retry policy belongs to the caller.
    /// </remarks>
    Task<IReadOnlyList<EmailSendResult>> SendAsync(
        IReadOnlyList<EmailMessage> messages,
        CancellationToken cancellationToken);
}

/// <summary>Convenience extensions over <see cref="IEmailSender"/>.</summary>
public static class EmailSenderExtensions
{
    /// <summary>Sends a single message; shorthand for a single-element batch.</summary>
    public static async Task<EmailSendResult> SendAsync(
        this IEmailSender sender, EmailMessage message, CancellationToken cancellationToken)
        => (await sender.SendAsync([message], cancellationToken))[0];
}
```

Batch-contract invariants, binding on every implementation (the conformance suite in §12.1 pins
them):

1. **One result per message, positionally.** No reordering, no elision, ever.
2. **Throw only when nothing was sent.** An up-front authentication, connection, or
   configuration failure may surface as an exception because the caller knows nothing went out.
   From the first success onward, failures are per-message results — an exception after partial
   sending would force the caller to choose between duplicating sent mail and dropping unsent
   mail. Under concurrent dispatch the rule applies after quiescence: on an authentication
   failure the adapter stops issuing, drains in-flight requests, and throws only if nothing
   succeeded — otherwise it reports per-message results. Cancellation is exempt: when the
   caller's token fires, adapters cancel in-flight work and let `OperationCanceledException`
   propagate even mid-batch — the caller chose to abandon the batch and owns the resulting
   ambiguity (rule 5).
3. **A structurally invalid message** (empty `To`, blank address) yields a `Rejected` result for
   that message; the rest of the batch proceeds. One malformed row must not poison a bulk
   dispatch.
4. **No durable retries inside the adapter.** Sub-second transport pragmatics (e.g. a TLS
   renegotiation) are the adapter's business; anything resembling retry policy belongs to the
   caller, which owns durability (today: the caller's own loop; later: the outbox worker).
5. **At-least-once overall.** A `TransientFailure` after the wire was touched is ambiguous — the
   provider may have accepted the message. Callers that retry accept possible duplicates; the
   contract minimizes the window (rule 2) but cannot eliminate it. Consumers for whom duplicates
   are costly wait for the outbox, which adds dedup keys at a tier that can own them.

### 1.4 Delivery events

```csharp
namespace Tellma.Core.Abstractions.Email;

/// <summary>The platform's classification of a delivery event.</summary>
public enum EmailDeliveryEventType
{
    /// <summary>Accepted by the recipient's mail server.</summary>
    Delivered,
    /// <summary>Temporarily refused; the provider keeps retrying.</summary>
    Deferred,
    /// <summary>Permanently refused by the recipient's mail server.</summary>
    Bounced,
    /// <summary>Discarded by the provider before sending (suppression list, invalid address).</summary>
    Dropped,
    /// <summary>Terminally not delivered, for a reason that is neither a recipient-server
    ///     refusal (<see cref="Bounced"/>) nor a pre-send discard (<see cref="Dropped"/>) — a
    ///     provider-internal failure or a post-acceptance filtering verdict; detail in
    ///     <see cref="EmailDeliveryEvent.Reason"/> and <see cref="EmailDeliveryEvent.RawType"/>.</summary>
    Failed,
    /// <summary>Opened by the recipient, where tracking is enabled.</summary>
    Opened,
    /// <summary>A link in the message was clicked, where tracking is enabled.</summary>
    Clicked,
    /// <summary>Reported as spam by the recipient.</summary>
    SpamReported,
    /// <summary>A provider event with no platform classification; see
    ///     <see cref="EmailDeliveryEvent.RawType"/>.</summary>
    Other,
}

/// <summary>
///     A transport-neutral delivery event, produced by a connector adapter from a provider
///     callback.
/// </summary>
/// <param name="Correlation">The correlation attached at send time, when present. Events without
///     one (mail sent outside the platform, recipient-level spam reports) are metered by the
///     dispatcher and never reach handlers.</param>
/// <param name="Recipient">The address the event concerns, when the provider reports it — one
///     recipient of a multi-recipient message can bounce while another delivers.</param>
/// <param name="Type">The platform's classification of the event.</param>
/// <param name="RawType">The provider's own event name, verbatim — diagnostic detail, and the
///     event's only meaning when <paramref name="Type"/> is <see cref="EmailDeliveryEventType.Other"/>.</param>
/// <param name="Reason">Provider-reported detail, chiefly for failures (the bounce reason).</param>
/// <param name="Timestamp">When the event occurred at the provider — not when the webhook
///     arrived; bounces can surface minutes later. Null when the callback carried no usable
///     time: such an event is still translated and routed, but contributes no arrival-lag
///     measurement, because a substituted time is indistinguishable from a real one and a
///     substituted default reads as decades or millennia of lag.</param>
/// <param name="ProviderEventId">The provider's unique id for this event. Handlers deduplicate
///     on it, because providers deliver at least once.</param>
public sealed record EmailDeliveryEvent(
    EmailCorrelation? Correlation,
    string? Recipient,
    EmailDeliveryEventType Type,
    string RawType,
    string? Reason,
    DateTimeOffset? Timestamp,
    string ProviderEventId);

/// <summary>
///     Routes delivery events from connector adapters to the handler owning each event's
///     underlying state. Adapters call this from their webhook receivers after signature
///     verification and translation; they never invoke handlers directly.
/// </summary>
/// <remarks>
///     The implementation routes by <see cref="EmailCorrelation.OwnerKey"/> and meters events
///     with no or unrecognized correlation. Dispatch may be synchronous — handler work is bounded
///     by contract — so a handler failure surfaces as
///     <see cref="WebhookOutcome.TransientFailure"/> on the webhook result and the provider's
///     redelivery becomes the retry.
/// </remarks>
public interface IEmailDeliveryEventDispatcher
{
    /// <summary>Routes a batch of events to their owning handlers.</summary>
    Task DispatchAsync(
        IReadOnlyList<EmailDeliveryEvent> events,
        CancellationToken cancellationToken);
}

/// <summary>
///     Consumes delivery events for correlations this owner minted, updating the owner's own
///     records (outbox rows, invitation records).
/// </summary>
/// <remarks>
///     Handlers receive only events whose owner key matches; an event whose
///     <see cref="EmailCorrelation.Reference"/> resolves to no row is an anomaly worth logging,
///     not ignoring. Handlers must deduplicate on <see cref="EmailDeliveryEvent.ProviderEventId"/>
///     and must be cheap — indexed updates only; anything heavier is enqueued as background work
///     by the handler itself, because the dispatcher runs on the webhook request path.
/// </remarks>
public interface IEmailDeliveryEventHandler
{
    /// <summary>The owner-key segment this handler owns; matches
    ///     <see cref="EmailCorrelation.OwnerKey"/> and is unique across the composition,
    ///     validated at startup.</summary>
    string OwnerKey { get; }

    /// <summary>Handles a batch of events belonging to this owner.</summary>
    Task HandleAsync(
        IReadOnlyList<EmailDeliveryEvent> events,
        CancellationToken cancellationToken);
}
```

`EmailDeliveryEventType` deliberately has no `Unsubscribed` member yet: unsubscribe semantics are
designed with the outbox and suppression work (§11), and adding an enum member later is additive.
Until then unsubscribe-family provider events map to `Other` with the provider name in `RawType`,
so nothing is lost — merely unclassified.

### 1.5 Webhooks

```csharp
namespace Tellma.Core.Abstractions.Webhooks;

/// <summary>
///     A transport-neutral inbound webhook call: the raw request an external system made,
///     captured before any deserialization so receivers can verify payload signatures over
///     the exact wire bytes.
/// </summary>
/// <param name="Method">The HTTP method, uppercase. Usually "POST"; "GET" for the verification
///     handshakes some providers perform at endpoint registration.</param>
/// <param name="Headers">Request headers. Keys compare case-insensitively; repeated headers
///     carry multiple values.</param>
/// <param name="QueryParams">Query-string parameters; repeated keys carry multiple values.
///     Some providers deliver verification challenges and tokens here.</param>
/// <param name="Body">The raw, unmodified request body.</param>
public sealed record WebhookRequest(
    string Method,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> QueryParams,
    ReadOnlyMemory<byte> Body);

/// <summary>
///     The semantic outcome of a webhook call. The host maps it to an HTTP status code,
///     which drives the external system's redelivery behavior.
/// </summary>
public enum WebhookOutcome
{
    /// <summary>Verified and accepted; the host responds 2xx and the caller will not redeliver.</summary>
    Accepted,

    /// <summary>Signature or credential verification failed; the host responds 401. Not redelivered.</summary>
    Unauthorized,

    /// <summary>The payload is malformed or unprocessable; the host responds 400. Not redelivered.</summary>
    Invalid,

    /// <summary>A transient internal failure; the host responds 5xx so the caller redelivers later.</summary>
    TransientFailure,
}

/// <summary>The receiver's response to a webhook call.</summary>
/// <param name="Outcome">The semantic outcome; determines the HTTP status code.</param>
/// <param name="Detail">Diagnostic detail for logs. Never sent to the external system.</param>
/// <param name="ResponseBody">A response body, only for providers whose protocol requires one:
///     challenge echoes at endpoint verification, literal acknowledgment tokens. A connector that
///     needs full response control beyond this is an inbound API, not a webhook.</param>
/// <param name="ResponseContentType">The content type of <paramref name="ResponseBody"/>
///     (e.g. "text/plain"); required when a body is set — providers are strict about it.</param>
public sealed record WebhookResult(
    WebhookOutcome Outcome,
    string? Detail = null,
    ReadOnlyMemory<byte>? ResponseBody = null,
    string? ResponseContentType = null);

/// <summary>
///     A connector adapter's inbound half: verifies, translates, and forwards callbacks from one
///     external system. Implementations are transport-neutral; a host fronts them with HTTP
///     (the platform projects <c>/api/webhooks/{Key}</c>; a minimal host maps the route by hand).
/// </summary>
/// <remarks>
///     Implementations must verify the payload signature over the raw <see cref="WebhookRequest.Body"/>
///     bytes before trusting any content, and must return promptly: verify, translate, hand off to
///     a dispatcher — never process inline.
/// </remarks>
public interface IWebhookReceiver
{
    /// <summary>
    ///     The route segment identifying this receiver (lowercase kebab-case, e.g.
    ///     "sendgrid-events"). Uniqueness across the composition is validated at startup.
    /// </summary>
    string Key { get; }

    /// <summary>Handles one webhook call.</summary>
    Task<WebhookResult> HandleAsync(WebhookRequest request, CancellationToken cancellationToken);
}
```

The host constructing `WebhookRequest` guarantees `Headers` and `QueryParams` use
ordinal-case-insensitive key comparers, so receivers index them without defensive re-wrapping
(§7).

### 1.6 Deployment identity

```csharp
namespace Tellma.Core.Abstractions.Hosting;

/// <summary>
///     Identifies this running deployment across the fleet. Registered once at composition as a
///     singleton; consumed by any feature that must name the deployment durably or on a wire —
///     the email correlation envelope, export file naming, future connectors.
/// </summary>
/// <remarks>
///     Deliberately not configuration: the application half is a compile-time constant of the
///     composition — an app knows its own name the way it knows its assembly name, and
///     configuration would let two deployments claim each other by mistake — while the
///     environment half comes from the host at startup. Pipelines that depend on it (email)
///     validate at startup that an instance is registered.
/// </remarks>
/// <param name="Application">The deployable's hardcoded name: a distribution's slug
///     ("etpharma"), "identity" for the identity server. Lowercase letters, digits, and
///     hyphens; validated in the constructor.</param>
/// <param name="EnvironmentName">The host environment name
///     (<c>IHostEnvironment.EnvironmentName</c>).</param>
public sealed record DeploymentIdentity(string Application, string EnvironmentName)
{
    /// <summary>
    ///     The fleet-unique deployment id: <see cref="Application"/> alone in Production,
    ///     environment-qualified (lowercased) otherwise — "etpharma", "etpharma-staging",
    ///     "identity-development". Lowercase kebab-case and colon-free, safe for wire envelopes
    ///     and file names; validated in the constructor.
    /// </summary>
    public string DeploymentId { get; }
}
```

Every composition registers it with its own hardcoded name — one line the distribution template
scaffolds:

```csharp
services.AddSingleton(new DeploymentIdentity("etpharma", builder.Environment.EnvironmentName));
```

Production stays unqualified so the id reads as the slug everywhere ids surface; every other
environment is qualified automatically, which is what keeps a staging deployment's wire artifacts
(§5.4) from ever colliding with production's. The type lives in
`Tellma.Core.Abstractions.Hosting`, not in email options, because the id is a platform concern —
email is merely its first consumer.

## 2. Composition and configuration

### 2.1 The registration model

The composition problem: multiple transports can be *referenced* (a distribution ships with both
SendGrid and SMTP adapters compiled in), exactly one is *active*, and the choice must be a pure
configuration decision — flipping a deployed instance from SendGrid to SMTP, or a developer
enabling the log sink, edits `appsettings`/Key Vault, never code.

Adapters therefore may not hand `IEmailSender` to DI directly; they declare themselves to the
pipeline through a registration record in Abstractions (adapters reference only
`Tellma.Core.Abstractions`, so the seam must live there):

```csharp
namespace Tellma.Core.Abstractions.Email;

/// <summary>
///     Declares one email transport for configuration-driven selection. Adapters register an
///     instance in DI; the platform email pipeline picks the one whose <see cref="Name"/> matches
///     the configured provider and exposes it to consumers as the <see cref="IEmailSender"/>.
/// </summary>
/// <param name="Name">The transport's stable configuration name (lowercase kebab-case:
///     "sendgrid", "smtp", "log-sink"). Matched case-insensitively; unique across the
///     composition, validated at startup.</param>
/// <param name="Live">Creates the live sender — real delivery with the transport's live
///     configuration.</param>
/// <param name="Sandbox">Creates the sandbox-delivery sender — the transport's no-real-delivery
///     channel (a validation-only provider mode, a mail-trap host) — or null when the transport
///     has none, in which case the pipeline withholds external sandbox mail itself.</param>
public sealed record EmailTransportRegistration(
    string Name,
    Func<IServiceProvider, IEmailSender> Live,
    Func<IServiceProvider, IEmailSender>? Sandbox = null);
```

Composition then reads, in a distribution or platform app host:

```csharp
services.AddTellmaEmail();      // Tellma.Core.Email: pipeline, options, guard, log-sink transport, dispatcher
services.AddSmtpEmail();        // Tellma.Connector.Smtp.Adapter: registers the "smtp" transport
services.AddSendGridEmail();    // Tellma.Connector.SendGrid.Adapter: registers the "sendgrid" transport
                                // + its webhook receiver when webhook config is present
services.AddAcsEmail();         // Tellma.Connector.AcsEmail.Adapter: registers the "acs-email" transport
                                // + its Event Grid receiver when webhook config is present
```

`AddTellmaEmail()` (in `Tellma.Core.Email`, class `TellmaEmailServiceCollectionExtensions`)
registers:

- `EmailOptions` bound from the `Email` configuration section, `ValidateOnStart`.
- The **router** (§3.3) as the sole `IEmailSender` registration — scoped, because it consults the
  ambient `ISandboxContext`.
- The `"log-sink"` transport (§2.4).
- `IEmailDeliveryEventDispatcher` (§8) and the email metrics/logging plumbing (§9).

Consumers everywhere — identity engine, module packs, distribution code — take `IEmailSender` via
constructor injection and never learn which transport is behind it.

At startup (options validation running under `ValidateOnStart`, before the host serves traffic)
the pipeline fails loudly when:

- No `Email:Provider` is configured outside the Development environment (Development defaults to
  `log-sink`).
- `Email:Provider` names no registered transport — the error lists the registered names, so a
  typo ("sendgird") is a one-glance fix.
- Two transports register the same name, or a transport name is not lowercase kebab-case.
- The active provider is `log-sink` outside the Development environment (§2.4).
- No `DeploymentIdentity` is registered (§1.6 — the correlation wire envelope and
  cross-deployment event protection key on it).
- No `ISandboxContext` implementation is registered (§3.1).

Validation also **resolves the active transport's factories once**, so the active adapter's own
options validation (missing API key, missing host) fires at startup rather than on the first send;
inactive adapters stay cold and unvalidated — referencing the SendGrid adapter without SendGrid
config is legal as long as SendGrid is not the active provider.

### 2.2 Configuration schema

```jsonc
{
  "Email": {
    // Which transport sends mail: "acs-email" | "sendgrid" | "smtp" | "log-sink"
    // (case-insensitive). Required outside Development; Development defaults to "log-sink".
    "Provider": "acs-email",

    "AcsEmail": {
      // Auth is Entra ID (managed identity) against the resource endpoint; no key is stored.
      "Endpoint": "https://tellma-etpharma.communication.azure.com",
      // No DisplayName: ACS takes the sender's display name from the domain's MailFrom
      // address and refuses a senderAddress carrying one, so the setting is rejected
      // at startup rather than silently ignored (§6.2).
      "From": { "Address": "no-reply@etpharma.tellma.com" },
      "MaxConcurrency": 8,                 // parallel send requests per batch
      "Webhook": {
        // Accepted ?token= values on the Event Grid subscription URL; more than one
        // accepted to allow rotation (§6.4). Receiver active iff non-empty.
        "Tokens": [ "<random secret>" ]
      }
    },

    "SendGrid": {
      "ApiKey": "<from Key Vault>",
      "From": { "Address": "no-reply@tellma.com", "DisplayName": "Tellma" },
      "MaxConcurrency": 8,                 // parallel mail-send requests per batch
      "Webhook": {
        // Event-webhook signature verification keys; more than one accepted to
        // allow zero-downtime rotation (§5.4). Receiver active iff non-empty.
        "VerificationKeys": [ "<base64 public key>" ]
      }
    },

    "Smtp": {
      "Host": "smtp.contoso.local",
      "Port": 587,
      "SecureSocket": "StartTls",          // MailKit SecureSocketOptions; see the adapter section
      "Username": "…",
      "Password": "<from Key Vault>",
      "From": { "Address": "no-reply@contoso.com", "DisplayName": "Contoso ERP" },
      // Optional sandbox-delivery channel: a mail-trap host (e.g. Mailpit) that
      // receives external sandbox-tenant mail. Absent → such mail is withheld (§3.3).
      "Sandbox": { "Host": "mailtrap.staging.local", "Port": 1025, "SecureSocket": "None" }
    }
  }
}
```

Schema rules, which future connectors inherit as the pattern:

- **One section per transport, named after the transport.** The adapter binds and validates its
  own section; the pipeline owns only `Email:Provider`.
- **Live config is the section's top level; sandbox config is a nested `Sandbox` subsection**
  present only when the transport's sandbox channel needs its own settings. SendGrid needs none —
  its sandbox channel is a per-request provider mode on the live credentials (§5.2) — so it has no
  `Sandbox` section by design, not by omission.
- **Secrets** (`ApiKey`, `Password`) come from the host's secret store (Key Vault in Azure,
  user-secrets in local dev); the JSON above shows placement, not storage.

### 2.3 The default `From`

Both adapters carry a required `From` in configuration, used when `EmailMessage.From` is null —
the overwhelmingly common case: consumers rarely know or care what mailbox mail leaves from, and
the sending domain is a deployment concern (domain authentication, §11) rather than a business
one. A message-level `From` overrides it for the rare flow that speaks as a different sender.

The default sits per transport section, not hoisted to the `Email` root, for two reasons: each
adapter reads only its own section (§2.2), and the value is transport-coupled — an address must be
authorized for the channel that carries it (SendGrid's authenticated domain, a smarthost's
permitted senders), so a shared default would imply an interchangeability between transports that
does not exist.

**`DisplayName` is transport-coupled too, and ACS cannot carry one.** SendGrid and SMTP take the
sender's display name per message; ACS validates `senderAddress` against a MailFrom address
configured on the domain and refuses the `Display Name <address>` form outright, so the name is a
property of the domain resource there (§6.2). The ACS adapter therefore rejects a configured
`From:DisplayName` at startup, and drops one arriving on a message's own `From`. This asymmetry is
found rather than chosen — an adapter cannot make a provider accept a field it validates against a
resource — and it belongs in the same category as SendGrid having no `Sandbox` section: a
transport's own shape, surfaced rather than papered over.

### 2.4 The Development log sink

`LogSinkEmailSender` (in `Tellma.Core.Email`, registered by `AddTellmaEmail()` as the `"log-sink"`
transport) writes every message to `ILogger` — recipients, subject, full text body, attachment
names and sizes (never attachment content), audience, correlation — where developers and tests
read it. It reports every message `Sent` with `ExpectsDeliveryEvents` false. Its `Sandbox`
factory returns the sink parameterized as the sandbox channel: in development, external sandbox
mail is also worth seeing, and the log line names the channel that carried each message.

It is the Development default (no configuration needed on a fresh clone) and is useful beyond
first-run: it is where a developer reads the mail a feature just composed, and it is the only
capture path open to an E2E suite that drives a deployed app and so cannot reach in-process
state. Suites that run the app in-process capture through `Tellma.Core.Testing` instead (§12.4),
which is what the identity server's own integration and E2E suites do today.

**The guard** admits the sink in Development only, in two layers, both in `Tellma.Core.Email`:

1. **Startup validation** (§2.1): activating `log-sink` outside the Development environment fails
   startup with an error naming the fix ("configure Email:Provider to a real transport"). This is
   the guard proper — centralized in the pipeline every host composes, so no app can forget it.
2. **Constructor guard**: `LogSinkEmailSender` itself throws when constructed outside a
   Development `IHostEnvironment`. This is defense in depth for the host that bypasses
   `AddTellmaEmail` and hand-registers the sink; such a host has left the pipeline, and this is
   the last tripwire.

Staging is deliberately included in the ban: it exists to be a faithful replica of production,
which a log sink is not — staging sends through the real pipeline into a mail trap (§3.5). A
deployment that must never email anyone points the SMTP transport at a trap, not at the sink.

## 3. Sandbox tenants — routing, marking, suppression

### 3.1 The tenant category and `ISandboxContext`

Every tenant is **Live** or **Sandbox**. The category is a property of the tenant, not of the
deployment: staging and production distributions both host tenants of either category, and
distribution code mostly doesn't know which environment it runs in. Sandbox tenants must produce
no external side effects — for email: nothing a sandbox tenant does may land in a real customer's
inbox.

The email pipeline learns the category through a deliberately narrow seam:

```csharp
namespace Tellma.Core.Abstractions.Tenancy;

/// <summary>
///     Reports whether the ambient unit of work executes on behalf of a sandbox tenant — a tenant
///     whose actions must produce no external side effects. Consulted by connector pipelines
///     (email today; every side-effecting connector eventually) to route between live and
///     sandbox channels.
/// </summary>
/// <remarks>
///     Distributions implement this over their tenant context and must ensure it is resolvable
///     wherever sends happen — request scopes and background-worker scopes alike. Hosts without
///     tenants (identity, landing) register <see cref="SandboxContext.Never"/>. There is no
///     default registration: a composition that forgot to decide fails at startup rather than
///     silently treating sandbox tenants as live.
/// </remarks>
public interface ISandboxContext
{
    /// <summary>True when the ambient work belongs to a sandbox tenant.</summary>
    bool IsSandbox { get; }
}

/// <summary>Fixed <see cref="ISandboxContext"/> implementations for hosts without tenants.</summary>
public static class SandboxContext
{
    /// <summary>Ambient work is never sandboxed (identity, landing, single-purpose hosts).</summary>
    public static ISandboxContext Never { get; }
}
```

This interface is domain-neutral on purpose: the same seam will route bank, e-invoicing, and
payment connectors, and this spec's routing policy (§3.3) is the template those connectors follow.
Requiring an explicit registration (validated at startup, §2.1) is the second half of the
"intentional decision" answer: `EmailAudience` forces the per-email decision, `ISandboxContext`
forces the per-composition one.

### 3.2 Who owns the sandbox marking

The **router** in `Tellma.Core.Email` — not the business logic building templates (which would have to
remember it in a thousand places), and not the connector adapters (which would each reimplement
it, and drift). Business code composes the message it wants; the router applies policy centrally
on the way to the transport. No send bypasses it because the router *is* the registered
`IEmailSender`.

### 3.3 The routing policy

`EmailRouter` (scoped; the sole DI `IEmailSender`) resolves the active transport's live — and,
lazily, sandbox — senders once, then applies per batch:

| Tenant | Audience | What happens |
|---|---|---|
| Live | Internal or External | Passed through to the **live** sender untouched. |
| Sandbox | Internal | **Marked** (below), then sent via the **live** sender — real mail to real staff, visibly test-originated. |
| Sandbox | External | Sent via the transport's **sandbox** sender when registered (SendGrid validation mode §5.2, SMTP mail-trap §4.4; ACS registers none, §6.2), or **withheld** with no wire activity when none is. Either way the reported outcome is `Sandboxed`. |

Mechanics:

- **Partition and reassemble.** A batch may mix audiences; the router partitions, forwards the
  partitions to the appropriate senders, and reassembles results **in input order** — the
  positional contract holds across the policy layer.
- **Marking** (sandbox + internal) produces a copy of the message record:
  - Subject: prefixed `[Sandbox] ` (skipped if already so prefixed, so an outbox redispatch never
    stacks prefixes).
  - Text body: first line `[Sandbox] This message was generated from a sandbox (test)
    environment.` followed by a blank line, then the original body.
  - HTML body, when present: a banner `<div>` (inline-styled amber, no external resources)
    carrying the same sentence, inserted immediately after the first `<body…>` tag when the
    string contains one, otherwise prepended.
  - The marker text is **fixed English, not localized**: it is an operational token, recognizable
    and filterable across every locale, and localizing it would demand a locale decision the
    router cannot make (message locale is not carried on the contract).
- **`Sandboxed` means exactly "no real email went out to the recipient"**, whatever the
  mechanism. Results returned by the sandbox channel have their success outcome rewritten
  `Sent` → `Sandboxed` by the router (failures pass through unchanged; a sandbox-channel result
  may carry a `ProviderMessageId`, a withheld one never does); withheld messages get `Sandboxed`
  directly. One outcome value, because to the person testing on a sandbox tenant the two
  mechanisms are the same event — the recipient received nothing — and a status that sometimes
  said "sent" for provider-validated mail would misreport precisely the case that matters
  (a customer invoice). Success-class and terminal: workflows proceed exactly as if sent, the
  contract's rule being that failure handling branches on `TransientFailure`/`Rejected` (§1.3),
  while status UIs render the truth. The rewrite lives in the router so adapters stay
  policy-free (§3.2); telemetry mirrors the outcome and separately records the wire mechanism
  (§9).
- **`ExpectsDeliveryEvents`** arrives already set by the adapter on live-channel results (§5.2)
  and is false on every `Sandboxed` result: nothing real was delivered, so no delivery event
  will ever arrive and the send outcome is final.

### 3.4 The pattern for future connectors

Restating the general rule this section instantiates, since this spec is the precedent: every
connector with external side effects ships **live and sandbox channels as explicit configuration**
(the sandbox channel using the provider's sandbox facility where one exists), consults
`ISandboxContext` through a central policy component owned by the platform — never scattered
through business logic — and reports every sandbox-policy interception as **one explicit
success-class outcome meaning "nothing real happened"**, whether the provider simulated the call
or the platform withheld it: workflows proceed as in production, while the result, records, and
telemetry all state the truth.

### 3.5 Environment profiles

The same binaries serve every environment; only configuration differs. The standard postures:

| Environment | Configuration | Effect |
|---|---|---|
| Development | nothing — defaults | `log-sink`: every message logged, nothing sent. Mailpit over the SMTP transport when rendered mail matters (§4.4). |
| Staging | `Provider: smtp`, live **and** sandbox sections pointed at the staging mail trap | The application runs exactly its production behavior — the §3.3 routing, sandbox-tenant marking, the real send pipeline — while every message that leaves the process is intercepted at the wire and stored where internal users and E2E suites can read it. |
| Production | the production transport | The §3.3 matrix, for real. |

Staging's defining constraint is that its mail must be **interceptable and readable, not
absent**: internal users sign into staging with emailed codes, and E2E suites assert on message
content — so a posture that silently validates-and-discards (a provider sandbox mode) would lock
everyone out of the very instance they are testing. Interception at the wire is what SMTP
mail-trapping *is*: the trap (Mailpit — SMTP endpoint in, authenticated web UI and REST API out)
plays the smarthost, the application neither knows nor cares, and the routing/marking logic runs
identically to production. Pointing the SMTP `Sandbox` section at the same trap keeps
sandbox-tenant external mail visible there too (its results still report `Sandboxed`). The trap
must be reachable only inside the staging network and requires authentication — it holds sign-in
codes. What this posture gives up is the last wire hop of the production transport (staging does
not exercise the SendGrid adapter against the live API); that contract is covered by the nightly
gated live suite (§12.3), which is a better fidelity instrument than staging traffic anyway.
This applies uniformly to distribution deployments, the standalone identity server's staging
instance, and staging distros embedding identity in-proc — all of them just carry the trap SMTP
config.

## 4. The SMTP connector — `Tellma.Connector.Smtp.Adapter`

### 4.1 Role and client library

SMTP is the fallback transport: air-gapped and on-prem installations relaying through a customer
smarthost, deployments where a SendGrid account is unavailable, and the local-dev path for
inspecting real MIME output in a visual sink. It is also what the identity server ships with today
(spec 0003 §14 pins an on-prem identity deployment to "store/PFX certificates, a file-system key
ring, and SMTP email"); this adapter replaces that private implementation.

The client is **MailKit** — not `System.Net.Mail.SmtpClient`, which Microsoft marks compat-only
("not recommended for new development … use MailKit or other libraries instead"). MailKit is the
library Microsoft's own documentation names as the replacement, is actively maintained (4.17 as of
May 2026), and its MimeKit foundation gives correct multipart, encoding, and internationalization
behavior for free. The identity server already made this call; the adapter inherits it.

### 4.2 Configuration

The `Email:Smtp` section (§2.2), bound to `SmtpEmailOptions` and validated when the transport is
active (§2.1): `Host` required; `Port` defaults to 587; `SecureSocket` is the MailKit
`SecureSocketOptions` enum (`None` | `Auto` | `SslOnConnect` | `StartTls` |
`StartTlsWhenAvailable`) and **defaults to `StartTls`** — mandatory TLS that fails the connection
when the server can't upgrade, the fail-closed default for credentialed submission; opportunistic
modes are an explicit choice for legacy relays. `Username`/`Password` optional (anonymous relays
exist on-prem); `From` required (§2.3); `TimeoutSeconds` defaults to 30.

### 4.3 Send semantics

- **One MailKit `SmtpClient` per `SendAsync` call.** MailKit's client is not thread-safe and
  holds one connection; the batch contract already amortizes the setup — connect + authenticate
  once, send every message on the reused connection, `QUIT`. No connection pooling; a future
  throughput need adds it behind the same contract.
- **Connect/authenticate failures throw** — nothing was attempted (§1.3 rule 2).
- **Message construction** (MimeKit): configured `From` unless the message carries one;
  `ReplyTo`; To/Cc/Bcc; `multipart/alternative` with the text part first and the HTML part last
  (last = preferred, per MIME); attachments with `ContentId` become linked resources of the HTML
  body (`multipart/related`), the rest ordinary attachments. MimeKit generates a globally unique
  `Message-Id`, which the adapter reports as `ProviderMessageId` — it is what a receiving server's
  logs and support tickets can be searched by; the smarthost's `250` acceptance line (queue id) is
  logged at Debug.
- **Internationalization**: when the connected server advertises `SMTPUTF8`, sends use MailKit's
  international format options, so non-ASCII addresses pass through unmangled; otherwise MailKit's
  default encoding rules apply and a server rejection maps like any other.
- **Outcome mapping**, per message:

  | Failure | Result |
  |---|---|
  | `SmtpCommandException` with status 400–499 | `TransientFailure` (SMTP transient-negative class: greylisting, throttling, mailbox busy) |
  | `SmtpCommandException` with status 500–599 | `Rejected` (permanent-negative class: unknown user, policy refusal, message too large) |
  | `SmtpProtocolException` / `IOException` mid-batch | Current message `TransientFailure`; the connection is dead — the adapter reconnects once and continues the remainder; if the reconnect fails, every remaining message reports `TransientFailure` (never an exception after the first attempt) |

  The exception's `ErrorCode` (`SenderNotAccepted` / `RecipientNotAccepted` /
  `MessageNotAccepted`) and `Mailbox` feed the result's `Error` text. Partial recipient
  acceptance is not modeled: MailKit's default aborts the message on the first rejected
  recipient, and the message maps to one result by its status code — per-recipient granularity is
  a delivery-events concept, which SMTP does not have.
- **`ExpectsDeliveryEvents` is always false**, structurally: SMTP's only feedback is the
  synchronous accept, which means "the smarthost took responsibility", not "delivered". Bounces
  arrive out-of-band as DSN messages to the return-path mailbox; parsing an inbound mailbox is a
  deliberate non-goal (an inbound-mail connector could add it one day, behind the same
  delivery-event contract).

### 4.4 The sandbox channel

When `Email:Smtp:Sandbox` is present, the adapter registers a sandbox sender: the same
implementation bound to the sandbox host/port/credentials — pointed at a mail-trap (a staging
Mailpit, an internal catch-all relay) so external sandbox-tenant mail is inspectable instead of
delivered. When absent, the registration's `Sandbox` factory is null and the router withholds
the mail (outcome `Sandboxed`, §3.3). The trap's `From` falls back to the live `From` when the
sandbox section doesn't override it.

The same mechanism doubles as the local-dev inspection path: a developer who wants to see real
rendered mail (instead of log-sink text) runs Mailpit — the actively maintained standard local
sink, with SMTP endpoint, web UI, and REST API — and sets `Email:Provider` to `smtp` with
`Host: localhost, Port: 1025, SecureSocket: None`. Configuration only, no code.

## 5. The SendGrid connector — `Tellma.Connector.SendGrid` + `.Adapter`

### 5.1 Why a first-party raw client

The official `sendgrid-csharp` SDK is unfit for the platform (assessed August 2026): its last
release is 9.29.3 of **April 2024** with no commits since — dormant, though not archived; it
targets only `netstandard2.0`-era TFMs (no nullability annotations); it serializes with
Newtonsoft.Json (a dependency the platform's System.Text.Json-only package graph should not
transitively impose on every distribution); and it verifies webhook signatures through
`starkbank-ecdsa`, an obscure third-party crypto library — an unacceptable trust anchor for the
platform's webhook authentication when the BCL's `ECDsa` does the same job.

The surface the platform needs is small and stable — one POST endpoint plus signature
verification — so `Tellma.Connector.SendGrid` implements it directly:

- **`SendGridClient`** — a typed client over `HttpClient` (constructor-injected, so hosts wire
  it through `IHttpClientFactory` and tests through a fake handler): `POST /v3/mail/send` with
  the bearer API key, System.Text.Json source-generated serialization of the v3 payload records
  (personalizations, content, attachments, `custom_args`, `mail_settings.sandbox_mode`), and a
  typed result carrying the status code, the `X-Message-Id` response header, and the parsed
  error body (`errors[].message/field`) on failure. No retries (§1.3 rule 4); timeout from
  options (default 30 s).
- **`SendGridWebhookVerifier`** — ECDSA signature verification using BCL crypto only: imports
  the base64 SubjectPublicKeyInfo key SendGrid issues (the curve rides in the key), verifies the
  DER-encoded signature from `X-Twilio-Email-Event-Webhook-Signature` over SHA-256 of
  `timestamp + raw body bytes`, timestamp from `X-Twilio-Email-Event-Webhook-Timestamp` —
  exactly the documented scheme, against the exact wire bytes (§1.5).
- **Event payload records** — the event-webhook JSON shape (`event`, `email`, `timestamp`,
  `sg_event_id`, `sg_message_id`, `reason`, plus custom args as top-level fields).

The raw client has zero Tellma dependencies and could serve future SendGrid adapters (marketing,
inbound parse) unchanged. If the official SDK comes back to life, this decision is revisitable —
the adapter's contract mapping wouldn't change either way.

### 5.2 Sending

One `EmailMessage` maps to one mail-send request — each message carries its own subject and
bodies, so SendGrid's 1,000-personalization fan-out (same content, many recipients) does not
apply to this contract. The adapter issues the batch's requests with bounded concurrency
(`Email:SendGrid:MaxConcurrency`, default 8), well inside the endpoint's documented
10,000-requests/second ceiling; results reassemble positionally.

Payload mapping:

| Contract | v3 mail-send |
|---|---|
| `From` (or configured default, §2.3) | `from` |
| `ReplyTo` | `reply_to` |
| `To`/`Cc`/`Bcc` | one personalization; combined recipient count over SendGrid's 1,000/request cap → `Rejected` without a request (§1.3 rule 3) |
| `Subject` | `subject` |
| `TextBody`, `HtmlBody` | `content` array — `text/plain` first, then `text/html` (SendGrid requires that order) |
| `Attachments` | `attachments[]`, base64; `ContentId` present → `disposition: "inline"` + `content_id` |
| `Correlation` | `custom_args`: `{ "tellma_correlation": "<deployment id>:<canonical form>" }` — the `DeploymentIdentity.DeploymentId` (§1.6) prefixed as the wire envelope (§5.4); a few dozen bytes against the 10,000-byte custom-args cap |
| (router-selected sandbox channel) | `mail_settings.sandbox_mode.enable: true` |

The sandbox channel (§3.3) is the live client plus `sandbox_mode`: SendGrid validates the full
payload, consumes no credits, never delivers, and emits **no** webhook events — the provider's
purpose-built no-delivery mode, needing no configuration of its own (§2.2). It returns 200 rather
than the normal 202; the adapter treats any 2xx as `Sent`, which the router then surfaces as
`Sandboxed` (§3.3).

Outcome mapping, per request:

| Response | Result |
|---|---|
| 2xx | `Sent`, `ProviderMessageId` = `X-Message-Id` |
| 400, 413 | `Rejected` — the payload itself is unacceptable (invalid address, oversized: SendGrid caps the total message at 30 MB); error body text into `Error` |
| 401, 403 | Before any success in the batch: **throw** — the batch as a whole never went out (§1.3 rule 2) and the operator must fix the key. After a success (key revoked mid-batch): `TransientFailure` for the remainder |
| 429 | `TransientFailure`; the adapter short-circuits the batch's **unattempted** remainder to `TransientFailure` without firing further requests — hammering a throttling endpoint helps no one |
| 5xx, network failure, timeout | `TransientFailure` (post-wire ambiguity accepted per §1.3 rule 5) |

Live-channel results carry `ExpectsDeliveryEvents = true` exactly when
`Email:SendGrid:Webhook:VerificationKeys` is non-empty (without a verification key no event will
ever be accepted) **and** the message has a correlation for events to return to. The sandbox
sender always reports false — sandbox mode emits no events.

### 5.3 Tracking-derived events

`Opened`/`Clicked` events arrive only when open/click tracking is enabled — an account/subuser
dashboard setting, deliberately not a per-message platform concern. The adapter translates
whatever arrives; whether tracking is on is an ops decision per deployable (with the usual
privacy considerations — open tracking embeds a pixel, click tracking rewrites links).

### 5.4 The event webhook receiver

`SendGridEventsWebhookReceiver`, key **`sendgrid-events`**, registered by `AddSendGridEmail()`
**iff `VerificationKeys` is non-empty — independent of whether SendGrid is the active send
transport**, so a deployment mid-migration to SMTP keeps accepting (and a misconfigured one keeps
rejecting) the tail of in-flight SendGrid events.

Handling, in order:

1. **Method check** — only POST is meaningful (SendGrid performs no GET handshake); anything
   else → `Invalid`.
2. **Verification** — both signature headers present, else `Unauthorized`; ECDSA verification
   (§5.1) against **each configured key** until one matches, else `Unauthorized` with a
   non-specific `Detail` (no oracle for forgers). Multiple accepted keys are the platform's
   rotation affordance: SendGrid holds one signing key per webhook and rotation regenerates it,
   so the runbook is regenerate → add the new key to config alongside the old → deploy → drop
   the old key; any events 401'd in the gap redeliver for 24 hours, so nothing is lost. There is
   deliberately **no timestamp-freshness check**: SendGrid redelivers failed batches for up to
   24 hours with the original event timestamps, a legitimate staleness no tolerance window can
   distinguish from replay; replay defense is handler-side deduplication on `sg_event_id`, per
   the contract.
3. **Parsing** — the body is a JSON array of events; malformed JSON → `Invalid`.
4. **Translation** — per event: `Recipient` = `email`, `Timestamp` = the Unix `timestamp`,
   `ProviderEventId` = `sg_event_id` (documented unique, ≤ 100 chars, the recommended dedupe
   key), `Reason` = `reason` where present, `RawType` = `event` verbatim. The
   `tellma_correlation` custom arg is split on its **envelope**: the leading segment names the
   deployment that sent the message; when it differs from this deployment's
   `DeploymentIdentity.DeploymentId` (§1.6) the event is **foreign** — dropped before dispatch
   and metered (§9.1), routine background under shared provider credentials (§5.5) and a
   misrouted-dashboard signal under dedicated ones. Otherwise `Correlation` = `EmailCorrelation.TryParse` of the remainder (absent or
   unparsable → null; SendGrid documents that delayed/asynchronous bounces can arrive without
   the send's metadata, so uncorrelated bounces are expected background, handled by §8's
   metering). Type map: `delivered` → `Delivered`, `deferred` → `Deferred`, `bounce` →
   `Bounced`, `dropped` → `Dropped`, `open` → `Opened`, `click` → `Clicked`, `spamreport` →
   `SpamReported`; everything else — `processed`, `unsubscribe`, `group_unsubscribe`,
   `group_resubscribe`, and any future name — → `Other` (§1.4).
5. **Dispatch** — the translated batch goes to `IEmailDeliveryEventDispatcher`; a dispatch
   failure maps to `TransientFailure` so SendGrid's redelivery (30-second/768 KB batches,
   retried up to 24 hours) becomes the retry loop. SendGrid's response timeout is not officially
   published (folklore says ~10 s), which the design doesn't lean on: verification and
   translation are microseconds, and handler work is bounded by contract (§1.4).

Signed-webhook OAuth (SendGrid can alternatively fetch a client-credentials token and present it
on each POST) is noted and not used: signature verification is self-contained, while OAuth would
make the platform host a token endpoint per deployable for no added assurance.

### 5.5 Account topology (operational guidance, non-normative)

The contract works under any credential topology; what varies is isolation and cost. Three
models, in descending isolation:

1. **Subuser (or account) per deployable** — own API key, own event webhook at its own
   `/api/webhooks/sendgrid-events`, own suppression lists, own sender reputation; Twilio's
   documented multi-tenant model ("customer-per-subuser"). Subuser **credit limits** are the
   enforceable per-deployable quota (§10). Plan reality (August 2026): subusers require the Pro
   plan (≈ $90/month, 15 subusers included, more by negotiation or Premier); the free tier was
   retired in 2025, so even staging sending is paid.
2. **Account per group** — several Pro accounts, each carrying up to 15 deployables as
   subusers: linear cost (≈ $6/deployable/month before volume), full isolation, more dashboards
   to operate.
3. **Shared subuser for small distributions** — one credential set and one event webhook
   serving many low-volume deployables, with events fanned out by a small **webhook gateway**:
   a stateless host on `Tellma.Core.Webhooks` that verifies the SendGrid signature once, splits
   each batch by the correlation envelope's deployable segment (§5.4), and forwards every
   deployment's share to its own endpoint under an internal credential. The accepted costs are
   real: shared sender reputation, **shared suppression lists** (a recipient's bounce or spam
   report against deployable A suppresses deployable B's mail to the same address), and every
   event transiting the gateway. The gateway is future infrastructure — built when the fleet's
   tail of small distributions materializes; the envelope this spec already stamps is what
   makes it possible.

SendGrid's fleet role is **the hosted fallback**: the SaaS default wherever ACS's conditional
default does not hold (§6.1), and the hosted choice for non-Azure deployments or for tenants
needing deferred-event granularity, recipient spam-complaint events, or a per-request validation
mode — signals ACS does not emit. The comparative analysis, with Postmark as the named tertiary
candidate, is [docs/research/email-provider-comparison.md](../research/email-provider-comparison.md).
SendGrid bought through the Azure Marketplace bills through Azure at the standard SendGrid
plans — the discounted legacy marketplace offering was retired in 2023 — so the marketplace is a
procurement convenience, not a pricing lever.

**Domain authentication before first send**, whichever topology: the platform requires SPF and
DKIM per sending domain, plus DMARC before any deployable approaches the mailbox providers'
5,000-messages/day bulk-sender bar (§11).

## 6. The ACS Email connector — `Tellma.Connector.AcsEmail.Adapter`

### 6.1 Role and client library

Azure Communication Services Email is **the default hosted transport for Azure-deployed
distributions, conditionally**: the default holds provided ACS email quota increases prove to be
granted **per subscription** — one onboarding ticket lifting every co-located distribution — and
flips to SendGrid (§5) if grants turn out to be per resource, a support ticket per distribution
being operationally untenable at fleet scale. Resolving that scope question with Azure support
precedes the first production rollout. The comparative analysis behind the choice is
[docs/research/email-provider-comparison.md](../research/email-provider-comparison.md); the short
form: per-deployable isolation is ACS's *native* model at near-zero fixed cost (a Communication
Services resource inside each distribution's own resource group, provisioned by its own Bicep),
authentication is managed identity (no mail secret exists at all), data-location options cover
the fleet's regions, and operations land in the Azure estate the platform already runs on.

The adapter builds on the first-party SDK — `Azure.Communication.Email` for sending and
`Azure.Messaging.EventGrid` for event parsing — which is fit per the §1.1 rule: actively
maintained, `TokenCredential`-native, and its message model carries the **custom headers the
correlation channel rides on** (§6.3). Two SDK behaviors are configured away: the Azure.Core
retry policy is set to zero retries (§1.3 rule 4 — durable retry belongs to the caller), and
sends use the start-only wait mode (no polling, §6.2).

### 6.2 Sending

The `Email:AcsEmail` section (§2.2) carries the resource `Endpoint`, the default `From` (§2.3),
`MaxConcurrency` (default 8), and the webhook tokens (§6.4) — and deliberately **no credential**:
the client authenticates with `TokenCredential` (the App Service's managed identity in Azure, the
developer's Azure credential locally); ACS's HMAC access keys are left unused, so there is no
mail secret to store or rotate.

One `EmailMessage` maps to one send request (ACS has no batch endpoint); the adapter issues the
batch with bounded concurrency and reassembles results positionally. Recipient-count and
request-size caps are resource-level and support-raisable, so the adapter does not pre-validate
them — the provider's synchronous 400 maps to `Rejected` like any other payload refusal.

**The sender goes on the wire as a bare address.** ACS validates `senderAddress` against a MailFrom
address configured on the sending domain, and answers `400 BadRequest — "Request body validation
error. See property 'senderAddress'"` to anything in the `Display Name <address>` form, quoted or
not. The sender's display name is consequently a property of the domain resource — the MailFrom
address's own `DisplayName`, set in the portal or through
`az communication email domain sender-username`, and unavailable on Azure managed domains, which
permit no sender usernames at all. So `From:DisplayName` fails options validation on this transport
(§2.3) and a display name on a message's `From` is dropped; recipient display names are unaffected,
since those ride a structured field. This was found by the live suite (§12.3) against a resource the
offline vectors had happily agreed with — the case for keeping that suite.

**Correlation rides the standard `Message-ID` header** (§6.3): the adapter stamps every
correlated message's internet message id with an encoding of its correlation, and delivery
reports echo it back as `internetMessageId`. Uncorrelated messages carry no custom id.
`ProviderMessageId` is the send operation id — the handle Azure diagnostics key on. **No
deployment envelope is stamped** (§1.2): each deployment's events return only to its own Event
Grid subscription on its own resource, so cross-deployment protection is structural on this
transport.

Outcome mapping, per request: 202 → `Sent`; 400 → `Rejected` (error detail into `Error`);
401/403 → throw before any success in the batch, `TransientFailure` for the remainder after one
(§1.3 rule 2); 429 → `TransientFailure`, short-circuiting the batch's unattempted remainder
(default quotas are low, §6.5); 5xx, network failure, timeout → `TransientFailure`.

**202 is `Sent`; the adapter never polls the send operation.** The 202 means ACS has queued the
message — exactly this contract's `Sent` ("accepted by the transport"), the same epistemic state
as SendGrid's 202. Polling the long-running operation to a terminal state is both unwanted
(`SendAsync` must return promptly) and unviable (status-poll quotas sit an order of magnitude
below send quotas — the operations endpoint is a diagnostics facility, not a status channel).
Post-acceptance failures surface as delivery reports with status `Failed`, which makes **the
Event Grid receiver effectively part of the transport on ACS**: without it, mail that fails after
acceptance fails silently. Deployments activating this transport configure §6.4 as a matter of
course; the startup log (§9.2) makes a missing webhook configuration visible.

Live-channel results carry `ExpectsDeliveryEvents = true` exactly when the webhook tokens are
configured and the message has a correlation. **There is no sandbox channel**: ACS offers no
validate-only mode, the registration's `Sandbox` factory is null, and the router withholds
external sandbox mail (`Sandboxed`, §3.3).

### 6.3 The Message-ID correlation channel

ACS keeps a customer-supplied internet message id when it is valid, and every delivery report
echoes the sent message's id back as `internetMessageId`. `x-ms-acsemail-validate-message-id` is
deliberately **left unset** (the lenient default): under it an invalid or duplicate id is
silently replaced with a generated one rather than failing the send — the worst case is an event
that arrives unrecognizable and meters as uncorrelated, where strict validation would instead
turn a correlation nicety into a rejected email. The adapter stamps correlated mail with

```
Message-ID: <tlm1-{base32hex(canonical correlation)}-{entropy}@{sending domain}>
```

- **base32hex, lowercase, unpadded**, because the canonical correlation is not a legal RFC 5322
  atom (`:` is not atext) and the value must survive byte-exact — the §1.2 length bounds keep
  the encoded id well inside RFC 5322's 998-character line limit;
- **a random entropy suffix per send**, ignored at parse, because internet message ids must be
  unique per message while correlations repeat across resends — a repeated id risks
  recipient-side deduplication (mail clients thread and drop by `Message-ID`) and ACS's own
  duplicate handling. Entropy in string space buys per-attempt uniqueness with no state and no
  bit budget;
- **the sending domain as the id's right-hand side**, because a message id that disagrees with
  the sending domain is a weak spam signal.

The receiver parses tolerantly — strip angle brackets, require the `tlm1-` tag, decode,
`EmailCorrelation.TryParse` — and anything else (an ACS-regenerated id, mail sent outside the
platform, a future format) resolves to null and flows through the uncorrelated metering (§8).
**Engagement events carry no `internetMessageId`** (in both the GA and preview event contracts),
so `Opened`/`Clicked` are uncorrelated on this transport by design — translated and metered,
never reaching handlers. That is an accepted trade for opt-in engagement telemetry, and it
dissolves by parsing alone if the field is ever added; deployments needing correlated open/click
tracking use SendGrid. The first pilot validates the echo loop end to end.

### 6.4 The Event Grid receiver

`AcsEmailEventsWebhookReceiver`, key **`acs-email-events`**, registered by `AddAcsEmail()` iff
`Email:AcsEmail:Webhook:Tokens` is non-empty — independent of the active send transport, the same
drain-out rule as §5.4. Subscriptions are provisioned on the Event Grid **system topic** of the
deployment's own resource, EventGridEvent schema, targeting
`/api/webhooks/acs-email-events?token=<secret>`.

Handling, in order:

1. **Method check** — POST only (the CloudEvents OPTIONS handshake is not used; subscriptions
   are provisioned with the EventGridEvent schema).
2. **Verification** — Event Grid does not sign deliveries, so authenticity rests on the
   subscription URL's `token` query parameter: compared constant-time against the configured
   accepted tokens (multiple accepted for rotation, mirroring §5.4's key list); absent or
   unmatched → `Unauthorized`. Microsoft Entra ID delivery auth is the documented hardening
   upgrade — it changes subscription provisioning, not this contract. The fronting never logs
   query strings (§7), so the token stays out of log stores.
3. **Handshake** — a `SubscriptionValidationEvent` returns `Accepted` with body
   `{"validationResponse": "<code>"}` and content type `application/json` — the §1.5
   challenge-echo path.
4. **Translation** — events parse through the SDK's system-event models. Per event:
   `Recipient` = the event's recipient (engagement events omit it when the original message had
   several recipients — passed through as null); `Timestamp` = the delivery/engagement
   timestamp, falling back to the Event Grid envelope's event time; `ProviderEventId` = the
   Event Grid event id (stable across redeliveries — the dedupe key); `Reason` = the delivery
   status detail; `RawType` = the ACS status or engagement type verbatim. `Correlation` parses
   from the delivery report's `internetMessageId` (§6.3); engagement events carry none and are
   always uncorrelated (§8). Type map:
   `Delivered` → `Delivered`, `Bounced` → `Bounced`, `Suppressed` → `Dropped` (the provider
   discarded it — its managed suppression list), `Failed` / `Quarantined` / `FilteredSpam` →
   `Failed`, `Expanded` → `Other`; engagement `View` → `Opened`, `Click` → `Clicked`.
5. **Dispatch** — failures map to `TransientFailure`; Event Grid redelivers with backoff for up
   to 24 hours.

**Dead-lettering is part of provisioning, not an option**: Event Grid never retries a delivery
answered 401 or 403, so a token-rotation mistake would otherwise silently drop events. Every
subscription is provisioned with a dead-letter container, and the silent-webhook alert (§9.4)
covers the residual gap.

### 6.5 Provisioning and quotas (operational guidance, non-normative)

- **Everything is the distribution's own Bicep**: the Communication Services resource, the Email
  Communication Service and custom domain (whose SPF/DKIM records feed the distribution's DNS
  setup), the managed-identity role assignment, the Event Grid system topic, and the webhook
  subscription (token from Key Vault, dead-letter storage). Declarative per-distribution
  provisioning at hundreds of distributions is the operational argument for this transport.
- **Quotas gate onboarding**: fresh subscriptions send 30 messages/minute and 100/hour on custom
  domains (Azure-managed domains are test-only), and increases go through a reputation-gated
  support ticket (up to 72 hours, discretionary). The ticket is an onboarding prerequisite, and
  the unresolved **grant-scope question — per subscription or per resource — decides the
  default-provider policy** (§6.1).
- **Suppression**: ACS's platform-managed suppression list auto-suppresses hard-bouncing
  addresses; such sends surface as `Suppressed` events (→ `Dropped`). Customer-managed
  suppression lists are preview-only and unused.

## 7. Webhook HTTP fronting — `MapTellmaWebhooks`

`Tellma.Core.Webhooks` ships the one HTTP fronting every receiver shares, so no connector ever
writes a controller:

```csharp
app.MapTellmaWebhooks();                    // maps /api/webhooks/{key}, GET + POST
```

- **Registration.** Receivers register in DI as `IWebhookReceiver` (adapters do this in their
  `Add*` methods, §2.1). At startup the fronting validates the set: keys unique, lowercase
  kebab-case; violations fail startup.
- **Request path.** The endpoint allows anonymous access (webhook callers hold no platform
  credentials — the receiver's signature verification is the authentication), is excluded from
  antiforgery, and runs outside any tenant-resolution scope: a webhook belongs to the deployable,
  not to a tenant; tenant state is reached later through the correlation's `TenantId` by the
  owning handler.
- **Body capture.** The body is buffered fully into memory before the receiver runs — signature
  verification needs the exact bytes — under a configurable cap (`Webhooks:MaxRequestBodyBytes`,
  default 2 MiB, comfortably above SendGrid's event-batch sizes); an oversized request gets 413
  and is metered, never dispatched.
- **Unknown key** → 404, metered. Probes and scanners land here; the response carries no body.
- **Outcome mapping**: `Accepted` → 200 (with the receiver's response body and content type when
  present), `Unauthorized` → 401, `Invalid` → 400, `TransientFailure` → 503. An unhandled
  receiver exception is logged as an error and returned as 500 — semantically `TransientFailure`,
  so the provider redelivers.
- **No payload or query-string logging.** Request bodies never reach logs at any level — they
  may contain recipient addresses and provider junk — and neither do query strings, which may
  carry webhook credentials (§6.4); diagnostics rely on the receiver's `Detail`, the metrics,
  and (for verified payloads) the translated events.

The route shape `/api/webhooks/{key}` is a platform contract: operators configure provider
dashboards against it, and it must remain stable across releases. Receiver keys are therefore
part of a connector's public surface — renaming one is a breaking change.

## 8. Delivery-event dispatch

`Tellma.Core.Email`'s `IEmailDeliveryEventDispatcher` implementation (foreign-deployable events
never reach it — adapters drop them at translation, §5.4):

- Collects the composition's `IEmailDeliveryEventHandler`s; owner keys are validated at startup
  (unique, `^[a-z0-9-]+$`) alongside the webhook-receiver validation.
- Groups the batch by `Correlation.OwnerKey`, preserving arrival order within each group, and
  invokes each owner's handler once with its sub-batch — batch in, batch out, no per-event calls.
- Events with **no correlation** (mail sent outside the platform through the same provider
  account, or provider events that carry no custom arguments) are metered and logged at Debug —
  expected background noise, invisible unless someone goes looking.
- Events with a correlation whose **owner key matches no handler** are metered and logged at
  Warning — with foreign deployments already filtered by the envelope (§5.4), this is a
  composition bug (an owner minted correlations but registered no handler), worth an alert
  (§9.4).
- **Handler failures propagate.** The receiver maps the exception to `TransientFailure`, the
  provider redelivers the whole batch later, and handler-side deduplication on `ProviderEventId`
  makes the redelivery harmless. This is deliberate at-least-once design: no event queue exists
  at this tier, so provider redelivery is the only durable retry — swallowing a handler failure
  would silently lose the event instead.

## 9. Observability

All email telemetry is emitted by the platform pipeline — `Tellma.Core.Email` for the send and
delivery-event instruments, `Tellma.Core.Webhooks` for the webhook instruments — so every
transport is measured identically. Adapters add their transport-specific log detail, plus the
one counter only they can emit: an adapter that drops foreign-deployment events at translation
(§5.4) records the `foreign` slice of `tellma.email.delivery.events` itself, under the same
meter name — those events never reach the dispatcher. Instruments follow the OpenTelemetry
conventions for custom meters — lowercase dot-separated names, units in instrument metadata,
durations in seconds — and the shapes mirror the OTel messaging semantic conventions where they
fit.

### 9.1 Metrics

Two meters, `Tellma.Email` and `Tellma.Webhooks` (one per emitting package), created through
`IMeterFactory`:

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `tellma.email.sent.messages` | Counter | `{message}` | `email.transport`, `email.outcome` (`sent` \| `transient_failure` \| `rejected` \| `sandboxed`), `email.audience` (`internal` \| `external`), `email.delivery` (`live` \| `sandbox` \| `withheld` — the wire mechanism), `email.owner` (owner key or `none`), `tenant.category` (`live` \| `sandbox`) |
| `tellma.email.send.duration` | Histogram | `s` | `email.transport`, `error.type` (exception type when the batch throws) |
| `tellma.email.send.batch.size` | Histogram | `{message}` | `email.transport` |
| `tellma.email.delivery.events` | Counter | `{event}` | `email.transport`, `email.event.type` (enum name, lowercase), `email.event.routing` (`routed` \| `uncorrelated` \| `unknown_owner` \| `foreign`), `email.owner` |
| `tellma.email.delivery.event.lag` | Histogram | `s` | `email.transport`, `email.event.type` — webhook arrival time minus the event's provider timestamp |
| `tellma.webhook.requests` | Counter | `{request}` | `webhook.key` (or `unknown`), `webhook.outcome` (enum name, plus `error` \| `too_large` \| `unknown_key`) |
| `tellma.webhook.request.duration` | Histogram | `s` | `webhook.key`, `webhook.outcome` |

Cardinality is bounded by construction: every tag is a small closed set except `email.owner`
(a handful of registered owners) and `tenant.category`. **Deliberately absent**: `tenant.id` — a
per-tenant tag multiplies every other dimension and App Insights costs by tenant count for a
question ("which tenant sent this?") the structured logs answer better; and any distribution
dimension — each deployable already reports to its own Application Insights resource, and the
shared Log Analytics workspace supplies the fleet rollup keyed by resource.

Recipient addresses, subjects, and body content never appear in metrics.

### 9.2 Logs

Source-generated `LoggerMessage` events (the platform's logging idiom), the key ones:

| Event | Level | Content |
|---|---|---|
| Batch sent | Information | Transport, batch size, per-outcome counts, elapsed. One line per batch, not per message. |
| Message failed | Warning | Transport, outcome, provider error text, correlation, provider message id. No recipient address. |
| Batch never attempted | Error | Transport, batch size, exception (the §1.3 rule-2 throw). |
| Sandboxed mail | Information | Count and correlations of messages the sandbox policy kept from real delivery, and by which mechanism (channel or withheld). |
| Webhook rejected | Warning | Receiver key, outcome, receiver `Detail`. Never the payload. |
| Webhook receiver threw | Error | Receiver key, exception. |
| Events dispatched | Debug | Per-owner counts. |
| Unknown owner key | Warning | The unrecognized owner key and event count. |
| Uncorrelated / foreign events | Debug | Counts only. |
| Log-sink message | Information | The full message content — the sink's entire purpose (Development only, §2.4). |
| Active transport selected | Information | At startup: transport name, deployment id, delivery webhook configured or not, sandbox channel present or not. |

The PII rule, stated once and binding everywhere: **recipient addresses and message content
appear only in the Development log sink's output and in Debug-level adapter logs**; Information
and above carry correlations, provider ids, counts, and error texts — enough to find the row,
never the person. (Provider error texts can quote the recipient address back — e.g. an SMTP `550
unknown user <x@y>`; that is accepted: it is failure-path-only, third-party-originated, and
operationally essential.)

### 9.3 Traces

One `ActivitySource`, `Tellma.Email`:

- `email.send` — around each transport call, tags `email.transport`, batch size, per-outcome
  counts on completion. The SendGrid HTTP call beneath it is captured by the standard
  `HttpClient` instrumentation and parents naturally.
- `email.dispatch_events` — around dispatcher runs, tags: event count, owner count. Webhook
  requests themselves ride the ASP.NET Core request activity; the receiver's verify/translate
  work is fast enough not to warrant its own span.

Hosts opt in by adding the source and meter to their OpenTelemetry configuration (the identity
server's OTel wiring already follows this pattern; distributions inherit it from the host
template).

### 9.4 Alerts and dashboards (operational guidance, non-normative)

The instruments above are designed to back these Azure Monitor alerts — listed here so the
implementation validates that each is expressible, not to fix thresholds forever:

- **Send failure rate**: `transient_failure + rejected` over `sent` above N% for M minutes, per
  transport.
- **Webhook auth failures**: any sustained `unauthorized` rate on `tellma.webhook.requests` —
  the signature of a key rotation gone wrong or a forgery attempt.
- **Silent webhook**: live-channel sending on a webhook-configured transport while
  `tellma.email.delivery.events` stays zero over a window — a misconfigured or broken provider
  webhook, otherwise invisible because sends still succeed.
- **Bounce/spam ratio**: `bounced + dropped` (and separately `spam_reported`) over `delivered`
  above the provider's reputation thresholds (§11) — the deliverability early-warning.
- **Unknown owner keys**: any occurrence (composition bug or environment leakage, §8).
- **Delivery-event lag p95** above minutes — provider-side callback delay or a struggling
  receiver.

Per-deployable dashboards live in each Application Insights resource; the fleet view aggregates
in the shared Log Analytics workspace.

## 10. Guardrails — and where they don't belong

Question: should the connector tier throttle a badly-behaved distribution or tenant before it
exhausts provider limits? **No — by design.** Volume policy cannot live at the transport:

- The transport cannot distinguish a runaway loop from a legitimate bulk dispatch (a statement
  run to thousands of customers is a *normal* outbox day); a cap low enough to stop bugs breaks
  real workloads, and a cap high enough for real workloads stops no bugs.
- In-process counters are wrong the moment a deployable scales to two instances; doing
  distributed rate accounting correctly requires durable shared state, which is precisely what
  the connector tier doesn't have and the outbox does.

Where volume control actually lives, each layer owning what it can enforce honestly:

1. **Provider-side hard quotas** — the enforceable backstop: per-deployable SendGrid credentials
   with provider-level limits (§5.5) mean a runaway deployable exhausts *its* allowance, never
   the fleet's; an on-prem smarthost enforces its own relay policy.
2. **The outbox (future)** — the durable tier that sees every unattended message as a row is the
   right place for per-tenant quotas, spread pacing, and approval thresholds; its spec owns them.
3. **Interactive-path rate limits** — endpoints that trigger mail on request (invitations,
   recovery) carry their own per-IP/per-user limits, as the identity server already does; that is
   request-abuse control, not email policy.
4. **Detection** — §9.1's metrics make anomalous volume visible within minutes; a send-volume
   alert per deployable is part of the standard alert set.

**Mass marketing campaigns are not this pipe.** `IEmailSender` is the transactional channel;
campaign sending needs list management, per-recipient unsubscribe state, scheduling, IP warm-up,
and content tooling — a different product surface (e.g. SendGrid's Marketing Campaigns API)
behind a different contract, designed if the need materializes. Pushing campaign volume through
the transactional pipe would also poison its sender reputation — the deliverability of invoices
and sign-in codes must never depend on a marketing blast's spam rate. Nothing enforces this
mechanically today (the outbox's quotas will); it is a stated platform rule.

## 11. Deliverability, suppression, and unsubscribe

What this spec ships is the raw material; the policy tier lands with the outbox. The division:

**Shipped now:**

- **Suppression events surface.** SendGrid maintains per-subuser suppression lists (bounces,
  spam reports, unsubscribes, invalid addresses); a send to a suppressed address is discarded
  provider-side and surfaces as a `dropped` event with a reason. The connector translates it
  (`Dropped`), the dispatcher routes it, and the owner's records show the truth. Nothing
  swallows it.
- **Spam reports surface** as `SpamReported` the same way.
- **Domain authentication is an onboarding prerequisite**, not an afterthought: the platform
  requires SPF **and** DKIM on every sending domain, with an aligned DMARC record before any
  deployable approaches 5,000 messages/day to one mailbox provider. (The 2024 Gmail/Yahoo
  sender rules themselves demand at least one of SPF/DKIM from every sender and all three from
  bulk senders — the platform standard clears the bulk bar from day one.) This is
  per-deployable ops work (each sending identity authenticates its domain) that the deployment
  runbook owns; the spec records it because unauthenticated mail fails regardless of code
  quality.
- **Reputation alerting** (§9.4), against the provider's documented health thresholds:
  investigate a bounce rate persistently above ~5%, treat a spam-report rate above 0.1% as
  excessive, keep the Gmail-reported spam rate under 0.3% (aim 0.1%).

**Deferred to the outbox spec, deliberately:**

- **Unsubscribe.** The 2024 mailbox-provider rules require one-click `List-Unsubscribe`
  (RFC 8058) for *marketing/subscribed* mail; **transactional mail is exempt**, and everything
  the platform sends today — sign-in codes, invitations, system notifications, invoiced
  documents — is transactional. The obligation attaches when the outbox starts carrying
  subscription-shaped streams (dunning sequences, statement subscriptions); the outbox spec will
  classify its streams and own the headers, the preference state, and its interplay with
  provider suppression groups. Designing that without the outbox's data model would be
  guesswork.
- **Suppression-driven send refusal** (checking the suppression list before dispatch rather
  than eating a `dropped` per send) and any suppression-list UI.

The SMTP path has no provider suppression layer; relay hygiene and bounce-mailbox handling
belong to whoever operates the smarthost — stated plainly in the adapter README.

## 12. Testing

The strategy per tier: **exhaustive offline tests** where the platform's own logic lives
(mapping, verification, routing, policy), **hermetic in-process protocol tests** for SMTP
(a real socket, no external dependency), and **narrow, gated live suites** for the hosted
providers — the one thing no local test can vouch for is that payloads, credentials, and quotas
satisfy the real APIs. SMTP gets no live suite: there is no canonical "the" SMTP server, and the
in-process server exercising MailKit over a real socket is the honest equivalent.

Test projects mirror `src/` per repo convention, vendor grouping folders included. The
in-process SMTP tests need no external infrastructure, so they live in the ordinary `*.Tests`
projects; `IntegrationTests` naming stays reserved for suites needing external resources (the
gated live suite).

```
test/connector/
├── acs-email/
│   ├── Tellma.Connector.AcsEmail.Adapter.Tests/    # mapping, Message-ID correlation, Event Grid receiver, conformance
│   └── Tellma.Connector.AcsEmail.IntegrationTests/ # gated live suite (real send to an internal mailbox)
├── sendgrid/
│   ├── Tellma.Connector.SendGrid.Tests/            # raw client: payload snapshots, verifier vectors
│   ├── Tellma.Connector.SendGrid.Adapter.Tests/    # mapping, receiver, conformance
│   └── Tellma.Connector.SendGrid.IntegrationTests/ # gated live suite (sandbox_mode against the real API)
└── smtp/
    └── Tellma.Connector.Smtp.Adapter.Tests/        # in-process SMTP server suite, conformance
test/core/
├── Tellma.Core.Abstractions.Tests/                 # correlation parsing, contract types
├── Tellma.Core.Email.Tests/                        # router, selection & guard, dispatcher, log sink
└── Tellma.Core.Webhooks.Tests/                     # fronting: mapping, caps, key validation
```

### 12.1 The sender conformance suite

An abstract xUnit class pinning the §1.3 invariants — one result per message in order; throw
only when nothing was sent (drain-then-throw under concurrency); cancellation surfaces
`OperationCanceledException` with in-flight work cancelled; invalid message → `Rejected` without
poisoning the batch; no adapter-level retries (asserted as at-most-one wire attempt per
message) — instantiated per
sender against its scriptable seam: the SendGrid adapter over a scripted `HttpMessageHandler`,
the ACS adapter over Azure.Core's mock transport, the SMTP adapter over the in-process server
with scripted responses, the log sink as-is. Every future email transport inherits the suite; it
is the executable form of the contract.

### 12.2 Offline suites

- **Correlation** (`Tellma.Core.Abstractions.Tests`): `ToString`/`TryParse` round-trips
  including colons in `Reference` and null `TenantId`; wire-envelope round-trips; rejection
  table (bad owner charset, non-integer tenant, empty reference, null); constructor validation.
- **Router** (`Tellma.Core.Email.Tests`): the §3.3 matrix over fake transports — pass-through
  for live tenants; marking for sandbox+internal (subject idempotence, text prefix, HTML banner
  placement with and without `<body>`); the `Sent`→`Sandboxed` rewrite on sandbox-channel
  results and direct `Sandboxed` on withheld messages, failures passing through unchanged;
  positional reassembly of mixed batches; `ExpectsDeliveryEvents` passed through untouched on
  live results, false on every `Sandboxed` result.
- **Selection & guard** (`Tellma.Core.Email.Tests`): provider resolution case-insensitivity;
  unknown provider startup failure listing registered names; Development default;
  missing-provider and missing-`DeploymentIdentity` failures; `log-sink` startup failure
  outside Development and the sink's own constructor guard; missing `ISandboxContext` failure;
  active-transport eager validation vs. cold inactive adapters.
- **Deployment identity** (`Tellma.Core.Abstractions.Tests`): `DeploymentId` composition per
  environment (bare in Production, qualified elsewhere) and constructor rejection of invalid
  application names.
- **Dispatcher** (`Tellma.Core.Email.Tests`): grouping and order preservation; unknown-owner
  and uncorrelated metering (asserted via `MetricCollector<T>`); handler-exception propagation.
- **Webhook fronting** (`Tellma.Core.Webhooks.Tests`, in-memory host): status mapping for all
  four outcomes; response body + content type pass-through; unknown key → 404; oversized body →
  413; duplicate-key and bad-key startup validation; receiver exception → 500.
- **SendGrid raw client** (`…SendGrid.Tests`): request snapshots through a fake handler —
  personalization shape, content ordering, attachments and inline `content_id`, `custom_args`,
  `sandbox_mode`, bearer header; `X-Message-Id` capture; error-body parsing.
  **Verifier vectors**: a test-generated ECDSA key pair signs `timestamp + body` exactly as
  SendGrid does; assert acceptance, then rejection on tampered body, tampered timestamp, wrong
  key, and malformed signature; assert multi-key acceptance order-independence.
- **SendGrid adapter** (`…SendGrid.Adapter.Tests`): contract→payload mapping (default `From`,
  recipient-cap rejection); outcome table per status code including the 401-before-success
  throw, 401-after-success remainder, and the 429 short-circuit; receiver behavior end-to-end
  over recorded event payloads (each documented event name, unknown names → `Other`, missing
  custom args → uncorrelated, foreign deployable segment → dropped and metered, malformed JSON →
  `Invalid`, missing headers → `Unauthorized`); dispatch-failure → `TransientFailure`.
- **ACS adapter** (`…AcsEmail.Adapter.Tests`, over Azure.Core's mock transport): Message-ID
  round-trip vectors — encode/parse including colons and unicode in `Reference`, entropy-suffix
  uniqueness across repeated sends of one correlation, tolerant parsing (angle brackets,
  ACS-regenerated ids, unknown prefixes → uncorrelated); the header stamped only on correlated
  mail and the validation flag left unset; the zero-retry client configuration pinned; the §6.2
  outcome table including the 401-before-success throw and the 429 short-circuit; receiver
  behavior end-to-end over recorded Event Grid payloads — subscription-validation echo (body and
  content type), token verification (constant-time, multi-accept, absent/unmatched →
  `Unauthorized`), the §6.4 translation table including engagement events (always uncorrelated)
  and the multi-recipient null-recipient case, `ProviderEventId` = the Event Grid event id;
  dispatch-failure → `TransientFailure`.

### 12.3 The SMTP protocol suite and the gated live suites

**SMTP** (`…Smtp.Adapter.Tests`): the `SmtpServer` NuGet package (11.x — stable, cross-platform,
in-process) hosts a real SMTP endpoint inside the test process, with `IMessageStore` capturing
raw MIME for MimeKit re-parsing and `IMailboxFilter`/`IUserAuthenticator` scripting refusals:

- Full round-trip fidelity: multipart/alternative order, inline `cid:` resources, ordinary
  attachments, non-ASCII subjects/display names, default-`From` application, `Message-Id`
  reported as `ProviderMessageId`.
- AUTH success; AUTH failure → throw (never attempted).
- STARTTLS against a test certificate; `SecureSocket: None` for the trap profile.
- Scripted 4xx recipient refusal → `TransientFailure`; 5xx → `Rejected`; subsequent messages in
  the batch unaffected.
- Mid-batch connection teardown → current message `TransientFailure`, one reconnect, remainder
  completes (the reconnect path is also unit-tested at its seam, since socket-level teardown
  timing can be platform-sensitive).

**SendGrid live** (`…SendGrid.IntegrationTests`): sends real requests with
`mail_settings.sandbox_mode` — full validation, zero delivery, zero credits, zero events — using
an API key from the `TELLMA_SENDGRID_TEST_APIKEY` environment variable; `Assert.SkipWhen` skips
the suite when the variable is absent, so local runs and PR CI never touch the network. Cases:
minimal message, full-feature message (attachments, inline image, custom args, non-ASCII), and a
small batch — asserting 2xx acceptance. CI runs the suite on a **nightly schedule and manual
dispatch, never as a PR gate**: an external service on the PR path is a flakiness tax, and fork
PRs cannot see repository secrets anyway — the suite would only ever fail there, and granting
access via `pull_request_target` patterns is the actual leak hazard, off the table; a nightly
pulse plus unit vectors detects API or credential drift within a day. (Webhook delivery cannot be exercised this way — sandbox mode emits no events; the
receiver's correctness rests on the recorded-payload and self-signed vectors above, and
SendGrid's dashboard "Test Your Integration" button covers manual smoke at onboarding.)

**ACS live** (`…AcsEmail.IntegrationTests`): same gating and cadence — environment-supplied
endpoint and credential (`TELLMA_ACS_TEST_ENDPOINT` plus a federated CI credential), skipped
cleanly when absent, nightly + manual, never a PR gate. ACS has no validate-only mode, so the
suite **really delivers**: one minimal and one full-feature message to a fixed Tellma-owned
mailbox on a dedicated test resource, asserting acceptance — volume that sits comfortably inside
even default quotas. Event Grid delivery is not asserted here (that is synthetic monitoring,
which is out of scope); the receiver's correctness rests on the recorded-payload suites above.

**Both live suites are built to explain their own failures**, because the reader of a nightly
failure is someone who cannot reproduce it: the run is hours old, it talks to an external account,
and re-running it costs a day. Three things travel with every failure, from
`Tellma.Core.Testing.Diagnostics` (§12.4):

- **The provider's own reason on the assertion.** Acceptance is asserted through a helper that
  fails with `EmailSendResult.Error` — the provider's error code and text — rather than through a
  bare `Assert.Equal` on the outcome, which reports "Expected: Sent, Actual: Rejected" and discards
  the only sentence that says why.
- **The transport's log lines,** routed into the test's output at `Debug`, which is where both
  adapters record a refusal's status and error code.
- **A masked report of the environment the run used** — endpoint, and sender and recipient reduced
  to their domains, a credential to its presence and length. The sending domain is the likeliest
  cause of a live refusal and the mailboxes are the part that must not be published: these suites'
  output lands in a public repository's Actions logs.

### 12.4 Test capture — `Tellma.Core.Testing`

A new published package (charter: test doubles and assertion helpers for `Tellma.Core.Abstractions`
contracts — the C# sibling of the `@tellma/core-ui-testing` precedent), so distribution and
platform test suites stop hand-rolling email fakes; the identity server's test suites adopt it
during their contract migration, retiring their private capturing senders. First inhabitants,
namespace `Tellma.Core.Testing.Email`:

- **`CapturingEmailSender : IEmailSender`** — thread-safe record of every send:
  `CapturedEmail(EmailMessage Message, EmailSendResult Result, DateTimeOffset Timestamp)`;
  scriptable per-message results (`OnSending(Func<EmailMessage, int, EmailSendResult>)`) for
  retry-path and `ExpectsDeliveryEvents` scenarios; `WaitForAsync(predicate, timeout)` for tests
  that trigger sends through background workers; `Clear()`.
- **`AddCapturingEmail()`** — two modes: replace `IEmailSender` outright (unit-style tests that
  don't care about policy), or register as transport `"capture"` selected via
  `Email:Provider` — keeping the full router/policy pipeline in the loop so integration tests
  exercise exactly what production runs, sandbox marking included.
- **`DeliveryEvents.For(correlation)`** — a small builder producing plausible
  `EmailDeliveryEvent` batches (unique event ids, ordered timestamps) for handler tests, plus
  duplicate/redelivery shaping for dedupe tests.

A second namespace, `Tellma.Core.Testing.Diagnostics`, carries what the live suites need to be
readable after the fact (§12.3): `AddTestOutput()` routes `ILogger` records into the running test's
output, and `LiveTestEnvironment` reports the settings a run used with mailboxes masked to their
domains and credentials to their length. They live here rather than in either suite because both
need them and neither owns them — and this is already the one package that takes an xunit
dependency, which a test-output sink requires.

E2E suites that drive a deployed app (Playwright) and cannot reach in-process state use the log
sink and scrape codes/links from log output — the identity E2E suites' existing pattern — or
point the SMTP transport at a Mailpit container and read its REST API; both are configuration-only
profiles of this spec's machinery, not additional test infrastructure.

## 13. The outbox — specified shape, future work

For contract-shape alignment only; **none of this compiles in this spec's deliverables** — the
types below are reproduced so the contracts above can be judged against their eventual primary
consumer, and will ship (possibly amended) with the outbox spec. Shipping unimplemented API
surface now would freeze design decisions the outbox spec is entitled to revisit.

```csharp
namespace Tellma.Core.Abstractions.Email;

/// <summary>One email to enqueue durably in the outbox.</summary>
/// <param name="Message">The message. Any <see cref="EmailMessage.Correlation"/> is ignored:
///     the outbox owns delivery events for its rows and mints the correlation itself at
///     dispatch time.</param>
/// <param name="RegardingEntity">The canonical name of the business entity this email concerns,
///     for audit and the per-document email UI; null when the email concerns no single entity.</param>
/// <param name="RegardingId">The id of the business entity this email concerns.</param>
/// <param name="SendAfter">The earliest dispatch time; the email is held in the queue until due.
///     Null dispatches as soon as the worker runs.</param>
public sealed record EmailEnqueueRequest(
    EmailMessage Message,
    string? RegardingEntity = null,
    long? RegardingId = null,
    DateTimeOffset? SendAfter = null);

/// <summary>
///     Durable, transactional email queuing — the reliability tier for unattended, must-arrive
///     mail (invoices, statements). Enqueued rows join the caller's current unit of work: they
///     commit and roll back with the business change that produced them, and a background worker
///     dispatches committed rows via <see cref="IEmailSender"/> with retry and dead-lettering.
///     Enqueuing durably records intent; it does not mean the mail has been sent when the
///     call returns.
/// </summary>
/// <remarks>
///     Implementations signal the dispatch worker only after the enclosing transaction commits,
///     and the signal is contentless ("work may exist"): correctness derives from the committed
///     rows and the worker's polling loop, never from the signal.
/// </remarks>
public interface IEmailOutbox
{
    /// <summary>Enqueues a batch of emails within the caller's current unit of work.</summary>
    Task EnqueueAsync(
        IReadOnlyList<EmailEnqueueRequest> requests,
        CancellationToken cancellationToken);
}
```

How today's contract decisions serve it: the outbox worker is the archetypal `IEmailSender`
caller — batch-shaped dispatch (§1.3), per-message outcomes driving row-level retry and
dead-letter transitions (rule 2 is what makes retry safe), `SendAfter` and quota pacing at the
tier that owns durable state (§10), correlations minted per row with `TenantId` set (its state is
tenant-sharded), a delivery-event handler (`OwnerKey: "outbox"`) updating row status with
`ProviderEventId` dedup, and each result's `ExpectsDeliveryEvents` deciding whether that row's
`Sent` (or `Sandboxed`) is terminal. The outbox spec also owns: unsubscribe and stream
classification (§11), per-tenant quotas, the dispatch worker's signal/poll loop, and the
per-document email UI fed by `RegardingEntity`/`RegardingId`.

## 14. Definition of done

- **Projects**: `Tellma.Core.Abstractions` additions; `Tellma.Core.Email`;
  `Tellma.Core.Webhooks`; `Tellma.Connector.AcsEmail.Adapter`; `Tellma.Connector.SendGrid` +
  `Tellma.Connector.SendGrid.Adapter`; `Tellma.Connector.Smtp.Adapter`; `Tellma.Core.Testing` —
  each with a README stating purpose and usage, XML docs on every member, building and testing
  on Windows and Linux under the repo's warnings-as-errors gates. `src/connectors/` renamed to
  `src/connector/` with lowercase vendor grouping folders (§1.1).
- **Behavior**: the §3.3 routing matrix, §2.1 startup validations (including both production
  guards), §7 fronting semantics, §8 dispatch semantics, the three adapters' outcome mappings,
  and the ACS Message-ID correlation channel (encode, stamp, parse) — all implemented and
  covered by the suites of §12, conformance suite included, green in CI.
- **Observability**: §9.1 instruments and §9.2 log events implemented and asserted
  (`MetricCollector<T>`); `ActivitySource` wired; each §9.4 alert expressible against the
  emitted telemetry (validated by writing the queries, not by deploying alerts).
- **CI**: unit/protocol suites on every PR (no new external dependencies on the PR path); the
  gated SendGrid and ACS live suites wired as nightly + manual, each skipping cleanly where its
  credentials are absent.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the connector folder rename
  and lowercase vendor grouping, the two new core runtime packages, the `Tellma.Core.Testing`
  package, the SendGrid raw-client example replacing any official-SDK assumption. Public XML
  docs and error messages reference no `docs/` paths, per repo rule.
- **Not in scope of done**: the identity server's adoption of the contract is executed and
  verified on the identity-server branch before that branch merges, not gated here;
  `IEmailOutbox` remains uncompiled (§13).

## Decisions record

The load-bearing decisions, where not already evident above:

1. **`Tellma.Connector.*`, singular, under `src/connector/<vendor>/`** — matches the platform's
   singular category prefixes; lowercase grouping folders reserve dotted-PascalCase for project
   folders; `src/connectors/` renamed (§1.1).
2. **SendGrid gets a first-party raw client; SMTP and ACS ride upstream clients** — the split
   is earned per vendor: the SendGrid SDK is dormant with unwanted dependencies, while MailKit
   and `Azure.Communication.Email` are fit (§1.1, §5.1, §6.1).
3. **Runtime in `Tellma.Core.Email`/`Tellma.Core.Webhooks`; contracts in Abstractions** — a
   pure contract surface, and lean non-distribution hosts; only the seams adapters need join
   Abstractions (§1.1, §2.1).
4. **`EmailAudience` is required on every message** — the internal/external call is per-email
   and only the composing code can make it; the compiler forces it to be stated (§1.2, §3.3).
5. **Sandbox policy lives in the router** — the sole registered `IEmailSender`, so no send path
   bypasses it; templates and adapters never carry policy (§3.2).
6. **One `Sandboxed` outcome for every sandbox interception** — `Sent` must unambiguously mean
   a real email went out; the wire mechanism is telemetry-only (§1.3, §3.3).
7. **The sandbox marker is fixed English** — an operational token, filterable in every locale;
   the contract carries no message locale to localize against (§3.3).
8. **The log sink is admitted in Development only** — pipeline validation plus a constructor
   tripwire; staging sends through the real pipeline into a mail trap instead (§2.4, §3.5).
9. **Provider selection is configuration-only** — `Email:Provider` picks among registered
   transports; inactive adapters stay cold and unvalidated (§2.1).
10. **Multiple accepted webhook keys; no timestamp-freshness check** — multi-key config is the
    rotation affordance; freshness is unsound under 24-hour redelivery, and dedup on the event
    id already neutralizes replay (§5.4).
11. **Receivers activate on webhook config, independent of the active send transport** — a
    deployable mid-migration keeps draining in-flight events (§5.4, §6.4).
12. **No volume guardrails at the connector tier** — the transport cannot tell a runaway from a
    statement run; enforcement lives at provider quotas, the future outbox, and API-layer rate
    limits, detection in metrics (§10).
13. **No `Unsubscribed` event type yet** — the outbox spec owns unsubscribe; `Other` +
    `RawType` loses nothing meanwhile (§1.4, §11).
14. **In-process `SmtpServer` for protocol tests; Mailpit for humans; live suites nightly,
    never PR gates** — PR CI stays hermetic; the real APIs are pulse-checked daily (§12).
15. **`IEmailOutbox` is specified but not compiled** — semver must not freeze what the outbox
    spec is free to revisit (§13).
16. **Delivery-event expectation is per-result, not per-sender** — one batch can mix
    event-bearing and terminal mail; only a per-message flag tells the truth (§1.3).
17. **Staging intercepts at the wire — SMTP into a mail trap, normal routing** — staging mail
    must be readable (sign-in codes, E2E), not validated-and-discarded; last-hop fidelity is
    the nightly live suites' job (§3.5).
18. **The correlation wire envelope carries the deployment id on echo transports** — it makes
    cross-deployment misdelivery detectable and shared-credential fan-out routable; the id is a
    hardcoded DI constant, not configuration, and lives in Hosting because email is not its
    only consumer (§1.6, §5.4, §5.5).
19. **ACS Email is the SaaS default, conditionally on quota-grant scope** — native isolation,
    Bicep provisioning, managed identity, and data residency win; per-resource grants would
    flip the default to SendGrid (§6.1,
    [docs/research/email-provider-comparison.md](../research/email-provider-comparison.md)).
20. **ACS `202` is `Sent`; the send operation is never polled** — poll quotas forbid it, and
    post-acceptance failures arrive as delivery reports, making the Event Grid receiver part of
    the transport (§6.2).
21. **ACS correlation rides the `Message-ID` header** — string space beats the UUID-bound
    operation id: no storage, no coordination, stateless attempt uniqueness; engagement events
    carry no echo and stay uncorrelated (§6.3).
22. **Event Grid verification is rotating URL tokens with mandatory dead-lettering** — Event
    Grid cannot sign deliveries, and a 401/403 is never retried (§6.4).
23. **`Failed` is the one added event classification** — consumers must never parse provider
    `RawType`s to learn a message died; provider verdicts fold into it (§1.4, §6.4).
24. **The ACS sender is a bare address, and `From:DisplayName` is refused at startup** — ACS
    validates `senderAddress` against the domain's MailFrom address, where the display name
    actually lives; a setting the transport cannot honour fails loudly instead of silently
    (§2.3, §6.2).
25. **A live suite must explain its own failure** — the transport's log at `Debug`, the
    provider's error text on the assertion, and a masked report of the environment the run used;
    a nightly failure is read hours later by someone who cannot reproduce it, and an
    "Expected: Sent, Actual: Rejected" tells them nothing (§12.3).
