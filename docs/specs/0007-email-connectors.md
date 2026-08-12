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
- **The first two connectors** — SendGrid (the hosted default) and SMTP (the on-prem/air-gapped
  fallback and the local-dev inspection path), as connector adapter packages.

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
delivery events, batch semantics) that only make full sense with the outbox in view. See §12.

## Goals / Non-goals

**Goals**

- Ship the email and webhook contracts in `Tellma.Core.Abstractions`, production-grade and
  XML-documented, as the permanent extension-point surface for all email transports.
- Ship the runtime pipeline as two lean packages — `Tellma.Core.Email` (config-driven provider
  selection, the sandbox routing policy, the Development log sink + guard, delivery-event
  dispatch) and `Tellma.Core.Webhooks` (webhook HTTP fronting) — composable without
  `Tellma.Core`.
- Ship `Tellma.Connector.Smtp.Adapter` (on MailKit) and `Tellma.Connector.SendGrid` +
  `Tellma.Connector.SendGrid.Adapter` (first-party raw client, §5.1), each with README, tests,
  and full observability.
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
  policy, per-tenant quotas. The interface is specified (§12) but not compiled or built; it ships
  with its own spec.
- **External unsubscribe / suppression management** — unsubscribe links, `List-Unsubscribe`
  headers, suppression-list UI. Designed with the outbox, which owns external customer mail. The
  connectors already surface the raw material (drop/bounce/spam events) so nothing here blocks it
  (§10).
- **Synthetic monitoring** — scheduled probe sends from an external worker. Designed once
  several connectors exist and share the probe infrastructure.
- **Mass-marketing campaigns** — a different product surface (list management, IP warm-up,
  campaign scheduling), not the transactional pipe (§9).
- **Inbound mail** (receiving/parsing email) — nothing here reads mailboxes.

## 1. The contract — `Tellma.Core.Abstractions`

### 1.1 Placement and packages

| Piece | Project | Namespace |
|---|---|---|
| Email contract (§1.2–§1.4) | `Tellma.Core.Abstractions` | `Tellma.Core.Abstractions.Email` |
| Webhook contract (§1.5) | `Tellma.Core.Abstractions` | `Tellma.Core.Abstractions.Webhooks` |
| Sandbox-context seam (§3.1) | `Tellma.Core.Abstractions` | `Tellma.Core.Abstractions.Tenancy` |
| Email pipeline: routing, selection, guard, log sink, dispatcher (§2, §3, §7) | `Tellma.Core.Email` | `Tellma.Core.Email` |
| Webhook fronting (§6) | `Tellma.Core.Webhooks` | `Tellma.Core.Webhooks` |
| SMTP connector (§4) | `Tellma.Connector.Smtp.Adapter` | `Tellma.Connector.Smtp.Adapter` |
| SendGrid raw client (§5.1) | `Tellma.Connector.SendGrid` | `Tellma.Connector.SendGrid` |
| SendGrid connector (§5) | `Tellma.Connector.SendGrid.Adapter` | `Tellma.Connector.SendGrid.Adapter` |
| Test capture (§11.4) | `Tellma.Core.Testing` | `Tellma.Core.Testing.Email` |

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
of the upstream client ecosystem, per vendor.** SMTP ships adapter-only: MailKit *is* the .NET
mail client — first-party quality, actively maintained, the library Microsoft's own docs
recommend — and wrapping it in a pass-through project would be indirection without value.
SendGrid ships the full split: the official SDK is dormant (§5.1), so the platform implements the
small API surface it needs as a raw `Tellma.Connector.SendGrid` client, which the adapter then
maps to the platform contracts. A raw client is written when the upstream client is absent or
unfit, not on principle.

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
///     Lowercase letters, digits, and hyphens; no colons.</param>
/// <param name="Reference">Owner-opaque subject reference, typically a row id.</param>
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

- `EmailCorrelation` validates `OwnerKey` in its constructor (`^[a-z0-9-]+$`; throws
  `ArgumentException` otherwise) so a malformed key fails at mint time, not when an event comes
  back unroutable. `TryParse` accepts exactly what `ToString` produces: a valid owner key, an
  empty-or-integer middle segment, and a non-empty remainder.
- `EmailAudience` is the answer to "who decides internal vs external, and how do we make it
  intentional": the decision is inherently per-email and belongs to the code composing the message
  (only it knows who the recipients are), so the contract makes it **required with no default** —
  it cannot be forgotten, only stated, and every statement is visible in review and greppable in
  audit. §3 defines what the pipeline does with it.
- On the wire, adapters prefix the canonical correlation form with the deployment's fleet-unique
  id (§5.4) — an envelope the contract type never sees, stripped and validated on the way back
  in.

### 1.3 Sending

```csharp
namespace Tellma.Core.Abstractions.Email;

/// <summary>The per-message outcome of a send attempt.</summary>
public enum EmailSendOutcome
{
    /// <summary>Accepted by the transport. Terminal unless the result expects delivery events.</summary>
    Sent,

    /// <summary>Failed in a way that may succeed later (throttling, connection loss mid-batch);
    ///     the caller may retry this message.</summary>
    TransientFailure,

    /// <summary>Rejected permanently (e.g. invalid address); the caller must not retry.</summary>
    Rejected,

    /// <summary>
    ///     Withheld with no wire activity: the sandbox policy suppressed an externally-audienced
    ///     message that has no sandbox channel. Success-class and terminal — consumers treat it
    ///     as success for workflow purposes (branch failure handling on
    ///     <see cref="TransientFailure"/> and <see cref="Rejected"/>, never on inequality with
    ///     <see cref="Sent"/>) but may surface it distinctly: an outbox row reads "suppressed",
    ///     not "sent". Emitted only by the platform routing policy; transports never produce it.
    /// </summary>
    Suppressed,
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
///     sandbox-delivered, suppressed, uncorrelated, or eventless-transport mail is terminal at
///     its send outcome. Per message, because one batch can mix both (an internal and an
///     external message of a sandbox tenant).</param>
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
    ///     when the batch as a whole was never attempted (authentication or connection failure up
    ///     front); once any message has been attempted they must report per-message outcomes
    ///     instead of throwing, so callers can retry transient failures without duplicating
    ///     messages that already went out. Implementations must not retry beyond sub-second
    ///     transport pragmatics — durable retry policy belongs to the caller.
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

Batch-contract invariants, binding on every implementation (the conformance suite in §11.1 pins
them):

1. **One result per message, positionally.** No reordering, no elision, ever.
2. **Throw only before the first attempt.** An up-front authentication, connection, or
   configuration failure may surface as an exception because the caller knows nothing went out.
   From the first attempted message onward, failures are per-message results — an exception after
   partial sending would force the caller to choose between duplicating sent mail and dropping
   unsent mail.
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
///     arrived; bounces can surface minutes later.</param>
/// <param name="ProviderEventId">The provider's unique id for this event. Handlers deduplicate
///     on it, because providers deliver at least once.</param>
public sealed record EmailDeliveryEvent(
    EmailCorrelation? Correlation,
    string? Recipient,
    EmailDeliveryEventType Type,
    string RawType,
    string? Reason,
    DateTimeOffset Timestamp,
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
designed with the outbox and suppression work (§10), and adding an enum member later is additive.
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
(§6).

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
///     has none, in which case the pipeline suppresses external sandbox mail itself.</param>
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
```

`AddTellmaEmail()` (in `Tellma.Core.Email`, class `TellmaEmailServiceCollectionExtensions`)
registers:

- `EmailOptions` bound from the `Email` configuration section, `ValidateOnStart`.
- The **router** (§3.3) as the sole `IEmailSender` registration — scoped, because it consults the
  ambient `ISandboxContext`.
- The `"log-sink"` transport (§2.4).
- `IEmailDeliveryEventDispatcher` (§7) and the email metrics/logging plumbing (§8).

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
- `Email:Deployable` is missing outside the Development environment (§5.4 — the correlation wire
  envelope and cross-deployment event protection key on it).
- No `ISandboxContext` implementation is registered (§3.1).

Validation also **resolves the active transport's factories once**, so the active adapter's own
options validation (missing API key, missing host) fires at startup rather than on the first send;
inactive adapters stay cold and unvalidated — referencing the SendGrid adapter without SendGrid
config is legal as long as SendGrid is not the active provider.

### 2.2 Configuration schema

```jsonc
{
  "Email": {
    // Which transport sends mail: "sendgrid" | "smtp" | "log-sink" (case-insensitive).
    // Required outside Development; Development defaults to "log-sink".
    "Provider": "sendgrid",

    // Fleet-unique id of this deployment, environment-qualified where a slug repeats
    // ("etpharma", "etpharma-staging", "identity"). Stamped into the correlation wire
    // envelope and checked on inbound events (§5.4). Required outside Development.
    "Deployable": "etpharma",

    // "Normal" routes per tenant category and audience (§3.3); "ForceSandbox" routes
    // every message through the sandbox channel regardless — the staging posture (§3.5).
    "Delivery": "Normal",

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
      // receives external sandbox-tenant mail. Absent → such mail is suppressed (§3.3).
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
the sending domain is a deployment concern (domain authentication, §10) rather than a business
one. A message-level `From` overrides it for the rare flow that speaks as a different sender.

The default sits per transport section, not hoisted to the `Email` root, for two reasons: each
adapter reads only its own section (§2.2), and the value is transport-coupled — an address must be
authorized for the channel that carries it (SendGrid's authenticated domain, a smarthost's
permitted senders), so a shared default would imply an interchangeability between transports that
does not exist.

### 2.4 The Development log sink

`LogSinkEmailSender` (in `Tellma.Core.Email`, registered by `AddTellmaEmail()` as the `"log-sink"`
transport) writes every message to `ILogger` — recipients, subject, full text body, attachment
names and sizes (never attachment content), audience, correlation — where developers and tests
read it. It reports every message `Sent` with `ExpectsDeliveryEvents` false. Its `Sandbox` factory
returns the same sink: in development, external sandbox mail is also worth seeing, and the log
line's delivery tag (§8.2) distinguishes it.

It is the Development default (no configuration needed on a fresh clone) and is useful beyond
first-run: E2E suites scrape codes and links from it (the identity server's suites already do).

**The guard** admits the sink in Development only, in two layers, both in `Tellma.Core.Email`:

1. **Startup validation** (§2.1): activating `log-sink` outside the Development environment fails
   startup with an error naming the fix ("configure Email:Provider to a real transport"). This is
   the guard proper — centralized in the pipeline every host composes, so no app can forget it.
2. **Constructor guard**: `LogSinkEmailSender` itself throws when constructed outside a
   Development `IHostEnvironment`. This is defense in depth for the host that bypasses
   `AddTellmaEmail` and hand-registers the sink; such a host has left the pipeline, and this is
   the last tripwire.

Staging is deliberately included in the ban: it exists to be a faithful replica of production,
which a log sink is not — staging runs a real transport in its sandbox posture (§3.5). A
deployment that must never email anyone configures a real transport under `ForceSandbox` or an
SMTP mail trap, not the sink.

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
lazily, sandbox — senders once, then applies per batch. `Email:Delivery` is consulted first:
under `ForceSandbox` (§3.5) **every** message — any tenant, any audience — takes the sandbox
path of the matrix's last row, unmarked, since nothing reaches a real inbox. Under `Normal`:

| Tenant | Audience | What happens |
|---|---|---|
| Live | Internal or External | Passed through to the **live** sender untouched. |
| Sandbox | Internal | **Marked** (below), then sent via the **live** sender — real mail to real staff, visibly test-originated. |
| Sandbox | External | Sent via the transport's **sandbox** sender when registered (SendGrid validation mode §5.2, SMTP mail-trap §4.4); otherwise **suppressed**: no wire activity, outcome `Suppressed`. |

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
- **Suppression** returns `Suppressed` per message — success-class and terminal, so calling
  workflows proceed exactly as if sent, while the record stays honest: an outbox row or
  per-document email UI can read "suppressed" instead of claiming a sandbox tenant's customer
  invoice was "sent". Users of a sandbox tenant know they are in one; a truthful status is less
  alarming than a false `Sent`, and it is auditable. The hazard is consumer code testing
  equality with `Sent` — the contract's stated rule is that failure handling branches on
  `TransientFailure`/`Rejected` (§1.3). `ProviderMessageId` is null; telemetry mirrors the
  outcome (§8).
- **`ExpectsDeliveryEvents`** arrives already set by the adapter on live-channel results (§5.2)
  and is false on everything else: sandbox-delivered and suppressed mail generates no delivery
  events even on an events-capable transport — nothing real was delivered — so those messages
  are terminal at their send outcome.

### 3.4 The pattern for future connectors

Restating the general rule this section instantiates, since this spec is the precedent: every
connector with external side effects ships **live and sandbox channels as explicit configuration**
(the sandbox channel using the provider's sandbox facility where one exists), consults
`ISandboxContext` through a central policy component owned by the platform — never scattered
through business logic — and **suppresses with an explicit success-class outcome** when a sandbox
tenant invokes a side effect the provider offers no sandbox for: workflows proceed as in
production, while the result, records, and telemetry all state the truth.

### 3.5 Environment profiles

The same binaries serve every environment; only configuration differs. The standard postures:

| Environment | Configuration | Effect |
|---|---|---|
| Development | nothing — defaults | `log-sink`: every message logged, nothing sent. Mailpit over the SMTP transport when rendered mail matters (§4.4). |
| Staging | the production transport + `Delivery: ForceSandbox` | The full production pipe — credentials, payload validation, telemetry — with zero deliverability: SendGrid validates via sandbox mode, SMTP delivers to its configured trap, channel-less transports suppress. All tenants, all audiences. |
| Production | the production transport, `Delivery: Normal` | The §3.3 matrix. |

Staging exists to rehearse a release against production's shape, so it runs production's
transport rather than a substitute, and `ForceSandbox` is what makes that safe — a pure
configuration switch, because staging and production run identical builds. When a staging test
must *read* delivered mail (an E2E asserting an invoice email's rendered content), the SMTP
transport pointed at a staging Mailpit trades transport fidelity for readable delivery; suites
that only assert sending behavior read telemetry or in-process capture (§11.4) instead.

## 4. The SMTP connector — `Tellma.Connector.Smtp.Adapter`

### 4.1 Role and client library

SMTP is the fallback transport: air-gapped and on-prem installations relaying through a customer
smarthost, deployments where a SendGrid account is unavailable, and the local-dev path for
inspecting real MIME output in a visual sink. It is also what the identity server ships with today
(spec 0003 §13 pins an on-prem identity deployment to "store/PFX certificates, a file-system key
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
delivered. When absent, the registration's `Sandbox` factory is null and the router suppresses
(§3.3). The trap's `From` falls back to the live `From` when the sandbox section doesn't override
it.

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
| `Correlation` | `custom_args`: `{ "tellma_correlation": "<deployable>:<canonical form>" }` — the configured `Email:Deployable` prefixed as the wire envelope (§5.4); a few dozen bytes against the 10,000-byte custom-args cap |
| (router-selected sandbox channel) | `mail_settings.sandbox_mode.enable: true` |

The sandbox channel (§3.3) is the live client plus `sandbox_mode`: SendGrid validates the full
payload, consumes no credits, never delivers, and emits **no** webhook events — the provider's
purpose-built no-delivery mode, needing no configuration of its own (§2.2). It returns 200 rather
than the normal 202; the adapter treats any 2xx as `Sent`.

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
   deployable that sent the message; when it differs from this deployment's `Email:Deployable`
   the event is **foreign** — dropped before dispatch and metered (§8.1), routine background
   under shared provider credentials (§5.5) and a misrouted-dashboard signal under dedicated
   ones. Otherwise `Correlation` = `EmailCorrelation.TryParse` of the remainder (absent or
   unparsable → null; SendGrid documents that delayed/asynchronous bounces can arrive without
   the send's metadata, so uncorrelated bounces are expected background, handled by §7's
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
   enforceable per-deployable quota (§9). Plan reality (August 2026): subusers require the Pro
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

SendGrid is the first hosted provider, not a commitment. The contract is provider-neutral, and
if fleet economics outgrow subuser pricing, the candidates to evaluate are **Azure Communication
Services Email** (an ACS resource per deployable inside its own resource group; delivery reports
and engagement events via Event Grid; pay-per-message, no plan gating) and **Postmark**
(per-"server" isolation with per-server tokens and webhooks). An adapter swap touches
composition and configuration only. SendGrid bought through the Azure Marketplace bills through
Azure at the standard SendGrid plans — the discounted legacy marketplace offering was retired in
2023 — so the marketplace is a procurement convenience, not a pricing lever.

**Domain authentication before first send**, whichever topology: SPF + DKIM per sending domain
(mandatory at Gmail/Yahoo since 2024), DMARC once any deployable exceeds 5,000 messages/day to
one mailbox provider (§10).

## 6. Webhook HTTP fronting — `MapTellmaWebhooks`

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
- **No payload logging.** Request bodies never reach logs at any level — they may contain
  recipient addresses and provider junk; diagnostics rely on the receiver's `Detail`, the
  metrics, and (for verified payloads) the translated events.

The route shape `/api/webhooks/{key}` is a platform contract: operators configure provider
dashboards against it, and it must remain stable across releases. Receiver keys are therefore
part of a connector's public surface — renaming one is a breaking change.

## 7. Delivery-event dispatch

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
  (§8.4).
- **Handler failures propagate.** The receiver maps the exception to `TransientFailure`, the
  provider redelivers the whole batch later, and handler-side deduplication on `ProviderEventId`
  makes the redelivery harmless. This is deliberate at-least-once design: no event queue exists
  at this tier, so provider redelivery is the only durable retry — swallowing a handler failure
  would silently lose the event instead.

## 8. Observability

All email telemetry is emitted by the platform pipeline — `Tellma.Core.Email` for the send and
delivery-event instruments, `Tellma.Core.Webhooks` for the webhook instruments — so every
transport is measured identically; adapters add only their transport-specific log detail.
Instruments follow the OpenTelemetry conventions for custom meters — lowercase dot-separated
names, units in instrument metadata, durations in seconds — and the shapes mirror the OTel
messaging semantic conventions where they fit.

### 8.1 Metrics

Two meters, `Tellma.Email` and `Tellma.Webhooks` (one per emitting package), created through
`IMeterFactory`:

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `tellma.email.sent.messages` | Counter | `{message}` | `email.transport`, `email.outcome` (`sent` \| `transient_failure` \| `rejected` \| `suppressed`), `email.audience` (`internal` \| `external`), `email.delivery` (`live` \| `sandbox` \| `suppressed`), `email.owner` (owner key or `none`), `tenant.category` (`live` \| `sandbox`) |
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

### 8.2 Logs

Source-generated `LoggerMessage` events (the platform's logging idiom), the key ones:

| Event | Level | Content |
|---|---|---|
| Batch sent | Information | Transport, batch size, per-outcome counts, elapsed. One line per batch, not per message. |
| Message failed | Warning | Transport, outcome, provider error text, correlation, provider message id. No recipient address. |
| Batch never attempted | Error | Transport, batch size, exception (the §1.3 rule-2 throw). |
| Sandbox suppression | Information | Count and correlations of externally-audienced sandbox mail that was suppressed or trap-routed. |
| Webhook rejected | Warning | Receiver key, outcome, receiver `Detail`. Never the payload. |
| Webhook receiver threw | Error | Receiver key, exception. |
| Events dispatched | Debug | Per-owner counts. |
| Unknown owner key | Warning | The unrecognized owner key and event count. |
| Uncorrelated / foreign events | Debug | Counts only. |
| Log-sink message | Information | The full message content — the sink's entire purpose (Development only, §2.4). |
| Active transport selected | Information | At startup: transport name, delivery mode (`Normal`/`ForceSandbox`), delivery webhook configured or not, sandbox channel present or not. |

The PII rule, stated once and binding everywhere: **recipient addresses and message content
appear only in the Development log sink's output and in Debug-level adapter logs**; Information
and above carry correlations, provider ids, counts, and error texts — enough to find the row,
never the person. (Provider error texts can quote the recipient address back — e.g. an SMTP `550
unknown user <x@y>`; that is accepted: it is failure-path-only, third-party-originated, and
operationally essential.)

### 8.3 Traces

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

### 8.4 Alerts and dashboards (operational guidance, non-normative)

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
  above the provider's reputation thresholds (§10) — the deliverability early-warning.
- **Unknown owner keys**: any occurrence (composition bug or environment leakage, §7).
- **Delivery-event lag p95** above minutes — provider-side callback delay or a struggling
  receiver.

Per-deployable dashboards live in each Application Insights resource; the fleet view aggregates
in the shared Log Analytics workspace.

## 9. Guardrails — and where they don't belong

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
4. **Detection** — §8.1's metrics make anomalous volume visible within minutes; a send-volume
   alert per deployable is part of the standard alert set.

**Mass marketing campaigns are not this pipe.** `IEmailSender` is the transactional channel;
campaign sending needs list management, per-recipient unsubscribe state, scheduling, IP warm-up,
and content tooling — a different product surface (e.g. SendGrid's Marketing Campaigns API)
behind a different contract, designed if the need materializes. Pushing campaign volume through
the transactional pipe would also poison its sender reputation — the deliverability of invoices
and sign-in codes must never depend on a marketing blast's spam rate. Nothing enforces this
mechanically today (the outbox's quotas will); it is a stated platform rule.

## 10. Deliverability, suppression, and unsubscribe

What this spec ships is the raw material; the policy tier lands with the outbox. The division:

**Shipped now:**

- **Suppression events surface.** SendGrid maintains per-subuser suppression lists (bounces,
  spam reports, unsubscribes, invalid addresses); a send to a suppressed address is discarded
  provider-side and surfaces as a `dropped` event with a reason. The connector translates it
  (`Dropped`), the dispatcher routes it, and the owner's records show the truth. Nothing
  swallows it.
- **Spam reports surface** as `SpamReported` the same way.
- **Domain authentication is an onboarding prerequisite**, not an afterthought: SPF + DKIM per
  sending domain (a hard mailbox-provider requirement since the 2024 Gmail/Yahoo sender rules),
  DMARC with From-domain alignment once a deployable sends over 5,000 messages/day to one
  provider. This is per-deployable ops work (each subuser authenticates its domain) that the
  deployment runbook owns; the spec records it because unauthenticated mail fails regardless of
  code quality.
- **Reputation alerting** (§8.4), against the provider's documented health thresholds:
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

## 11. Testing

The strategy per tier: **exhaustive offline tests** where the platform's own logic lives
(mapping, verification, routing, policy), **hermetic in-process protocol tests** for SMTP
(a real socket, no external dependency), and a **narrow, gated live suite** for SendGrid — the
one thing no local test can vouch for is that our payloads and credentials satisfy the real API,
and that matters precisely because the wire client is first-party code. SMTP gets no live suite:
there is no canonical "the" SMTP server, and the in-process server exercising MailKit over a
real socket is the honest equivalent.

Test projects mirror `src/` per repo convention, vendor grouping folders included. The
in-process SMTP tests need no external infrastructure, so they live in the ordinary `*.Tests`
projects; `IntegrationTests` naming stays reserved for suites needing external resources (the
gated live suite).

```
test/connector/
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

### 11.1 The sender conformance suite

An abstract xUnit class pinning the §1.3 invariants — one result per message in order; throw
only before first attempt; invalid message → `Rejected` without poisoning the batch; no
adapter-level retries (asserted as at-most-one wire attempt per message) — instantiated per
sender against its scriptable seam: the SendGrid adapter over a scripted `HttpMessageHandler`,
the SMTP adapter over the in-process server with scripted responses, the log sink as-is. Every
future email transport inherits the suite; it is the executable form of the contract.

### 11.2 Offline suites

- **Correlation** (`Tellma.Core.Abstractions.Tests`): `ToString`/`TryParse` round-trips
  including colons in `Reference` and null `TenantId`; wire-envelope round-trips; rejection
  table (bad owner charset, non-integer tenant, empty reference, null); constructor validation.
- **Router** (`Tellma.Core.Email.Tests`): the §3.3 matrix over fake transports — pass-through
  for live tenants; marking for sandbox+internal (subject idempotence, text prefix, HTML banner
  placement with and without `<body>`); sandbox-sender routing and `Suppressed` outcomes for
  sandbox+external; the `ForceSandbox` override across all tenant/audience combinations;
  positional reassembly of mixed batches; `ExpectsDeliveryEvents` passed through untouched on
  live results, false on suppression.
- **Selection & guard** (`Tellma.Core.Email.Tests`): provider resolution case-insensitivity;
  unknown provider startup failure listing registered names; Development default;
  missing-provider and missing-`Deployable` failures outside Development; `log-sink` startup
  failure outside Development and the sink's own constructor guard; missing `ISandboxContext`
  failure; active-transport eager validation vs. cold inactive adapters.
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

### 11.3 The SMTP protocol suite and the gated live suite

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
dispatch, never as a PR gate**: an external service on the PR path is a flakiness tax and would
expose the secret to fork PRs; a nightly pulse plus unit vectors detects API or credential drift
within a day. (Webhook delivery cannot be exercised this way — sandbox mode emits no events; the
receiver's correctness rests on the recorded-payload and self-signed vectors above, and
SendGrid's dashboard "Test Your Integration" button covers manual smoke at onboarding.)

### 11.4 Test capture — `Tellma.Core.Testing`

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

E2E suites that drive a deployed app (Playwright) and cannot reach in-process state use the log
sink and scrape codes/links from log output — the identity E2E suites' existing pattern — or
point the SMTP transport at a Mailpit container and read its REST API; both are configuration-only
profiles of this spec's machinery, not additional test infrastructure.

## 12. The outbox — specified shape, future work

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
tier that owns durable state (§9), correlations minted per row with `TenantId` set (its state is
tenant-sharded), a delivery-event handler (`OwnerKey: "outbox"`) updating row status with
`ProviderEventId` dedup, and each result's `ExpectsDeliveryEvents` deciding whether that row's
`Sent` (or `Suppressed`) is terminal. The outbox spec also owns: unsubscribe and stream
classification (§10), per-tenant quotas, the dispatch worker's signal/poll loop, and the
per-document email UI fed by `RegardingEntity`/`RegardingId`.

## 13. Definition of done

- **Projects**: `Tellma.Core.Abstractions` additions; `Tellma.Core.Email`;
  `Tellma.Core.Webhooks`; `Tellma.Connector.SendGrid` + `Tellma.Connector.SendGrid.Adapter`;
  `Tellma.Connector.Smtp.Adapter`; `Tellma.Core.Testing` — each with a README stating purpose
  and usage, XML docs on every member, building and testing on Windows and Linux under the
  repo's warnings-as-errors gates. `src/connectors/` renamed to `src/connector/` with lowercase
  vendor grouping folders (§1.1).
- **Behavior**: the §3.3 routing matrix, §2.1 startup validations (including both production
  guards), §6 fronting semantics, §7 dispatch semantics, and both adapters' outcome mappings —
  all implemented and covered by the suites of §11, conformance suite included, green in CI.
- **Observability**: §8.1 instruments and §8.2 log events implemented and asserted
  (`MetricCollector<T>`); `ActivitySource` wired; each §8.4 alert expressible against the
  emitted telemetry (validated by writing the queries, not by deploying alerts).
- **CI**: unit/protocol suites on every PR (no new external dependencies on the PR path); the
  gated SendGrid live suite wired as scheduled + manual, skipping cleanly where the secret is
  absent.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the connector folder rename
  and lowercase vendor grouping, the two new core runtime packages, the `Tellma.Core.Testing`
  package, the SendGrid raw-client example replacing any official-SDK assumption. Public XML
  docs and error messages reference no `docs/` paths, per repo rule.
- **Not in scope of done**: the identity server's adoption of the contract is executed and
  verified on the identity-server branch before that branch merges, not gated here;
  `IEmailOutbox` remains uncompiled (§12).

## Decisions record

The load-bearing decisions, where not already evident above:

1. **`Tellma.Connector.*`, singular, under `src/connector/<vendor>/`** — the singular category
   prefix matches `Tellma.Module.*` / `Tellma.Industry.*`; grouping folders are lowercase
   vendor names, with dotted-PascalCase folder names reserved for C# project folders. The
   checked-in `src/connectors/` placeholder is renamed accordingly (§1.1).
2. **First-party SendGrid raw client, not the official SDK** — the SDK is dormant (no release
   since April 2024), targets legacy TFMs, drags Newtonsoft.Json into the platform's dependency
   graph, and delegates webhook signature verification to a third-party ECDSA library where BCL
   crypto suffices; the needed surface is one endpoint plus verification (§5.1). SendGrid
   therefore gets the convention's raw-client/adapter split while SMTP stays adapter-only on
   MailKit — the split is earned per vendor by the upstream ecosystem, not applied uniformly
   (§1.1).
3. **Runtime in `Tellma.Core.Email` / `Tellma.Core.Webhooks`; contracts in Abstractions** —
   Abstractions' charter is a pure contract surface (the log sink belongs with the pipeline
   that activates and guards it), and dedicated runtime packages keep non-distribution hosts
   (identity, landing, the future webhook gateway) free of `Tellma.Core`'s distribution
   machinery. Only the seams adapters need (`EmailTransportRegistration`, `ISandboxContext`)
   join the contracts in Abstractions (§1.1, §2.1).
4. **`EmailAudience` as a required message property** — the internal/external decision is
   per-email and only the composing code can make it, so the contract forces it to be stated
   rather than trying to centralize a judgment no central place can make. The compiler is the
   reminder; review and audit see the stated value; §3.3's routing consumes it (§1.2).
5. **Sandbox policy centralized in the router** — not in templates (forgettable at every send
   site) and not in adapters (duplicated per transport, guaranteed drift). The router is the
   registered `IEmailSender`, so no send path bypasses policy; adapters implement channels,
   never policy (§3.2–§3.3).
6. **Suppression is an explicit `Suppressed` outcome, not a fabricated `Sent`** — success-class
   and terminal, so workflows proceed as in production while records and UI can tell a sandbox
   tenant's user the truth ("suppressed", not "sent" on a test invoice). The naive-equality
   hazard is owned by the contract: failure handling branches on `TransientFailure`/`Rejected`,
   never on inequality with `Sent` (§1.3, §3.3).
7. **The sandbox marker is fixed English** — an operational token, not user copy: recognizable
   and filterable in every locale, and the contract carries no message-locale field for the
   router to localize against (§3.3).
8. **The log sink is admitted in Development only** — startup validation in the pipeline plus a
   constructor tripwire in the sink itself. Staging is included in the ban: it exists to be a
   faithful replica of production and runs a real transport in `ForceSandbox` posture instead
   (§2.4, §3.5).
9. **Provider selection is configuration-only** — adapters register transports; `Email:Provider`
   picks one; switching transports (or enabling the sink) is an `appsettings`/Key Vault edit.
   Inactive adapters stay cold and unvalidated so shipping both adapters costs nothing (§2.1).
10. **Multiple accepted webhook verification keys; no timestamp-freshness check** — multi-key
    config is the rotation affordance SendGrid's single-signing-key model lacks; freshness
    checking is unsound against a provider that legitimately redelivers for 24 hours, and
    replay is already neutralized by handler dedup on the provider event id (§5.4).
11. **The SendGrid receiver activates on webhook config, independent of the active send
    transport** — a deployable mid-migration keeps draining in-flight events (§5.4).
12. **No volume guardrails at the connector tier** — the transport cannot tell a runaway loop
    from a statement run, and in-process counters lie under scale-out. Enforcement lives where
    it is honest: provider-side subuser credit limits now, outbox quotas later, interactive
    rate limits at the API layer, detection via metrics (§9).
13. **No `Unsubscribed` event type yet** — unsubscribe semantics belong to the outbox spec;
    the enum grows additively when the model exists; meanwhile `Other` + `RawType` loses
    nothing (§1.4, §10).
14. **In-process `SmtpServer` for protocol tests; Mailpit for humans; live suite gated and
    scheduled** — PR CI stays hermetic and cross-platform with no Docker dependency; the real
    SendGrid API is pulse-checked nightly via sandbox mode (full validation, zero delivery)
    rather than gating PRs on an external service (§11).
15. **`IEmailOutbox` is specified but not compiled** — publishing dead API would let semver
    freeze decisions the outbox spec must be free to revisit; the shape is recorded here so
    today's contracts could be validated against their eventual primary consumer (§12).
16. **Delivery-event expectation is per-result, not per-sender** — `ExpectsDeliveryEvents` on
    `EmailSendResult`: one batch can mix a sandbox tenant's internal mail (live channel, events
    coming) with its external mail (suppressed, terminal), and only a per-message flag can tell
    the truth. A sender-level capability property cannot (§1.3, §5.2).
17. **`Email:Delivery: ForceSandbox` is the staging posture** — staging runs production's
    transport and pipeline with delivery forced through the sandbox channel for every tenant
    and audience. The switch must be configuration because staging and production run identical
    builds; a log sink or substitute transport would forfeit the rehearsal value staging exists
    for (§3.5).
18. **The correlation wire envelope carries `Email:Deployable`** — adapters prefix the
    fleet-unique deployment id on the wire and drop foreign events at translation. Correlation
    segments repeat across deployments, so the envelope is what makes cross-deployment
    misdelivery detectable — and it is the routing key that makes the shared-subuser gateway
    topology possible (§5.4, §5.5).
