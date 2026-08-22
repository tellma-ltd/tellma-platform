# Spec: Marmin UAE Connector — Raw Client

- **Author:** Ahmad Akra
- **Date:** 21 August 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

The UAE's mandatory e-invoicing regime follows the Peppol five-corner model: a supplier's system
submits structured invoices (UAE PINT) to an accredited service provider, which delivers them to
the buyer's provider and reports them to the Federal Tax Authority on a parallel leg. ERPs do not
talk to the Peppol network or the FTA directly; they talk to their accredited provider. Marmin is
one such provider, and its UAE deployment exposes a versioned JSON API — hosted per country
(`api-sandbox.ae.marmin.ai` for the UAE sandbox), documented and versioned independently of
Marmin's other country deployments, with no .NET SDK of any kind.

This spec ships the **raw client half** of the Marmin UAE connector, following the
raw-client/adapter split spec 0007 §1.1 established: `Tellma.Connector.MarminAe`, a first-party,
zero-dependency client for the slice of the Marmin UAE API the platform needs — authentication,
outbound sales documents, inbound purchase documents, transmission status, legal-artifact
downloads, and webhook signature verification.

The adapter is deliberately deferred. Its job is to implement upper-layer extension points —
outbound submission orchestration, an `IWebhookReceiver`, document mapping — and the layers that
declare those extension points (a Sales module, a UAE compliance pack) do not exist yet. The raw
client carries no such dependency: it is independently specifiable, independently testable against
Marmin's sandbox, and useful to any future adapter unchanged. Vendor-facing risk — API drift,
credential handling, signature schemes, quota behavior — is retired now; platform-facing seams are
designed when their owning layers exist.

**Why `MarminAe` and not `Marmin`:** the vendor slot names the specific external system a raw
client binds to, not the corporation — the precedent set by `Tellma.Connector.AcsEmail`, where the
product qualifier distinguishes the one ACS API the platform consumes from the rest of the Azure
Communication Services umbrella. Marmin's country deployments are distinct external systems:
separately hosted, separately versioned, and shaped by different regulatory models (the UAE's
Peppol decentralized exchange; Saudi Arabia's ZATCA clearance). A future Saudi integration would
be a sibling `Tellma.Connector.MarminSa` with a genuinely different client, not an extension of
this one. An unqualified `Tellma.Connector.Marmin` would either falsely claim that scope or
quietly come to mean "the UAE one".

## Goals / Non-goals

**Goals**

- Ship `Tellma.Connector.MarminAe` under `src/connector/marmin-ae/`: typed client, token
  provider, webhook signature verifier, webhook event parsing, request/response models, and a
  typed error model — README, XML docs on every member, building and testing on Windows and
  Linux under the repo's warnings-as-errors gates.
- Ship exhaustive offline unit tests (`Tellma.Connector.MarminAe.Tests`) and a gated live suite
  against the Marmin UAE sandbox (`Tellma.Connector.MarminAe.IntegrationTests`), wired into the
  test tiers of spec 0007 §12.

**Non-goals (explicitly out of scope)**

- **The adapter** — extension-point implementations, the `IWebhookReceiver` registration, the
  submission outbox/state machine, document mapping from platform entities, tenant routing and
  sandbox-tenant policy, configuration schema, DI registration. It ships with its own spec once
  its upper layers exist; the `Ae` compliance-registry addition its package name needs is
  deferred with it.
- **Tax semantics** — computing `profile_execution_id` flags, choosing tax categories, deciding
  which conditional fields a scenario requires. The client transmits what it is given; UAE rule
  knowledge belongs to the future compliance layer.
- **API surface with no platform consumer** — proforma invoices, sale/purchase registers
  (recording documents issued outside the API — the platform has none), business-profile
  claim/update/OTP flows (portal onboarding, one-time), code-list endpoints (static UAE PINT
  vocabularies), purchase-document create/resubmit (inbound documents arrive via Peppol; the
  platform never authors them). The client grows additively when a consumer appears.
- **Production onboarding** — credentials, profile setup, webhook registration in Marmin's
  portal: deployment runbook work, not code.

## 1. Package and placement

```
src/connector/marmin-ae/
└── Tellma.Connector.MarminAe/          # csproj — client, token provider, verifier, models, error model
test/connector/marmin-ae/
├── Tellma.Connector.MarminAe.Tests/            # offline: snapshots, vectors, token lifecycle, parsing
└── Tellma.Connector.MarminAe.IntegrationTests/ # gated live suite against the Marmin UAE sandbox
```

**Zero dependencies** — no packages, no project references — so any future adapter, in any
scoping, consumes it unchanged. Serialization is source-generated System.Text.Json; crypto is BCL
`HMACSHA256`. A first-party client is written when the upstream client is absent or unfit
(spec 0007 §1.1); Marmin publishes no .NET client at all, so the question does not even reach
fitness.

**The API version is pinned in code.** Every request carries `X-MARMIN-VERSION: 20260507` from a
single constant. Marmin versions the UAE API by date and documents each version separately; a
version bump changes wire contracts and is therefore a deliberate package release with re-run
live suites, never configuration.

**The code in this spec is normative for shape, not for formatting** (spec 0007 §1.1's
convention): snippets fix types, members, and contracts; layout is the repository's business.

## 2. The API surface

The slice the client covers, as published in the vendor's version-2026-05-07 reference:

| Operation | Method and route |
|---|---|
| Obtain access token | `GET /auth/token?client_id=…` + `x-marmin-signature` header |
| Create sales invoice | `POST /api/sales-invoices/{business_profile_id}` |
| Edit and resubmit sales invoice | `PUT /api/sales-invoices/{uuid}/{business_profile_id}` |
| Create sales credit note | `POST /api/sales-credit-notes/{business_profile_id}` |
| Edit and resubmit sales credit note | `PUT` — family pattern of the invoice resubmit |
| Retrieve a document | `GET /api/{family}/{id}` |
| List documents (paged, filterable) | `GET /api/{family}?page=…&size=…` + filters |
| List document UUIDs | `GET` — per the vendor reference |
| Peppol status snapshot | `GET /api/{family}/{id}/peppol-status` |
| Peppol status log | `GET` — per the vendor reference |
| Download UBL 2.1 XML | `GET /api/{family}/{id}/xml` |
| Download PDF / attachment | `GET` — per the vendor reference |
| Retrieve business profile | `GET /api/business-profiles/{profileId}` |

`{family}` ranges over the four uniform document families — `sales-invoices`,
`sales-credit-notes`, `purchase-invoices`, `purchase-credit-notes`. Mutations exist for the two
sales families only (§Non-goals). Routes the table does not spell out follow the family pattern
in the vendor reference; the client centralizes route construction in one place, so a vendor
route correction is one edit.

The business-profile read is included because the future adapter's obvious first act is
validating its configured profile id at startup; the rest of party management stays out.

**Vendor behaviors the client's shape must honor:**

- **Server-owned fields are structurally unsendable.** Marmin derives the supplier party on
  sales documents from the business profile and overwrites anything submitted, and computes all
  totals itself. Request and response models are therefore separate types: request models simply
  lack `accounting_supplier_party`, `id`, `org_id`, `document_sequence`, the calculated totals,
  and `meta_info` — the compiler enforces what the vendor's implementation notes ask of
  integrators.
- **Resubmit is a full replace.** The vendor accepts no partial patch and may clear omitted
  fields, and permits resubmission only from Peppol validation failure. Create and resubmit
  share the same request type, which makes "include every root-level field you would send on
  create" a fact of the type system rather than a discipline.
- **Acceptance is not terminal.** A `201` means API-level validation passed; Peppol and FTA
  validation run asynchronously on two independent legs (delivery to the buyer's corner,
  reporting to the FTA) and can fail a document the API accepted. Peppol outcomes are **data,
  not exceptions**: status models expose the per-leg and overall statuses as strings, with
  constants for the values consumers branch on (e.g. the validation-failure status that gates
  resubmit). The vendor does not publish the vocabulary exhaustively, so the set stays open.
- **Payloads are capped at 8 MB** including Base64 attachments. The client pre-rejects an
  oversized payload locally with a precise error instead of eating a `413` — the same courtesy
  the SendGrid client extends to its recipient cap.
- **Wire conventions**: snake_case names; dates as `yyyy-MM-dd` (`DateOnly`); times as
  `HH:mm:ss` in Gulf Standard Time (`TimeOnly`, the zone being the vendor's fixed convention);
  amounts as `decimal`.

## 3. Authentication

Marmin authenticates machine clients with a signature-for-token exchange: prove possession of the
Client Secret by sending `Base64(HMAC-SHA256(key: client_secret, message: client_id))` in the
`x-marmin-signature` header of the token request, receive a short-lived JWT (`token`,
`expires_at`), and present it as a bearer on every subsequent call.

```csharp
namespace Tellma.Connector.MarminAe;

/// <summary>Connection settings for one Marmin UAE organization.</summary>
public sealed record MarminAeClientOptions
{
    /// <summary>The API host, e.g. https://api-sandbox.ae.marmin.ai for the sandbox.</summary>
    public required Uri BaseAddress { get; init; }

    /// <summary>The organization client id ("org_…"). Not a secret.</summary>
    public required string ClientId { get; init; }

    /// <summary>The client secret, from the host's secret store.</summary>
    public required string ClientSecret { get; init; }

    /// <summary>The per-request timeout; the client owns it so a request timeout is
    ///     distinguishable from caller abandonment.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
///     Obtains and caches the short-lived access token: computes the HMAC signature, calls the
///     token endpoint, and serves the cached token until it nears expiry.
/// </summary>
public sealed class MarminAeTokenProvider
{
    /// <summary>The valid token, from cache or freshly obtained.</summary>
    public Task<string> GetTokenAsync(CancellationToken cancellationToken);

    /// <summary>Drops the cached token so the next call obtains a fresh one.</summary>
    public void Invalidate();
}
```

Provider semantics:

- **Proactive refresh with skew**: a token within a safety margin of `expires_at` counts as
  expired, so a data call never departs with a token that dies in flight.
- **Single-flight**: concurrent callers needing a refresh await one token request, not a
  stampede — the token endpoint is rate-limited like everything else.
- **A fresh signature per token request**, computed at call time from the configured
  credentials, so a secret rotation takes effect on the next refresh without restart.
- The bearer is attached **per request**, never on default headers, for the same rotation
  reason.

On a `401` from a data call, the client invalidates the cache, obtains a fresh token, and retries
that request **once**; a second `401` surfaces. This is not a durable retry: an unauthenticated
request performed no server-side work for any verb, so the retry cannot duplicate a document —
it is the token-expiry race resolved where it occurs, invisible to callers.

## 4. Client semantics

`MarminAeClient` sits over an injected `HttpClient` plus the token provider. One method per §2
operation: dedicated create/resubmit methods per sales family (invoice and credit-note request
models differ — a credit note additionally requires `credit_note_type_code`,
`discrepancy_response`, and a `billing_reference` to the original invoice), and
family-parameterized reads (`MarminAeDocumentKind` selects among the four families) since the
vendor keeps the read surface uniform. Downloads return streams with their content type; the
document response model is one type across families, kind-discriminated, mirroring the vendor's
own field union.

- **Retry-free** beyond the single §3 re-auth: durable retry policy belongs to the caller — the
  future adapter's outbox — where it can be idempotent by design. A retry inside the client
  would silently duplicate legal documents the caller believes failed.
- **A `429` is never retried internally.** The vendor rate-limits per account per endpoint
  (60/minute on document operations, 30/minute on business profiles) and answers with
  `X-RateLimit-*` and `Retry-After` headers. The client surfaces both the status and the parsed
  headers so the caller's pacing can be honest; pacing itself is caller policy.
- **Failures throw `MarminAeRequestException`**, carrying the HTTP status, the parsed
  vendor error detail — the field-level validation errors on a `400` — the parsed rate-limit
  headers when present, and a size-capped raw body for diagnostics. The vendor reference does
  not publish its error-body JSON schema; the shape is captured from the sandbox during
  implementation and pinned in test vectors thereafter.
- **Cancellation propagates** as `OperationCanceledException`, in-flight request cancelled.

## 5. Webhook verification and events

Marmin's webhooks are thin notifications: the payload identifies the event and the document but
carries no document state — consumers fetch `resource_url` for truth. Receiving, dispatching, and
fetch-on-notify are adapter concerns; the raw package ships the two vendor-coupled pieces, both
hosting-agnostic:

```csharp
namespace Tellma.Connector.MarminAe;

/// <summary>
///     Verifies a webhook call's <c>x-marmin-signature</c> header: Base64 HMAC-SHA256 over the
///     raw request body, keyed with the endpoint's signing secret.
/// </summary>
public static class MarminAeWebhookVerifier
{
    /// <summary>True when the signature matches the body under any accepted secret.</summary>
    public static bool Verify(
        ReadOnlySpan<byte> body, string? signature, IReadOnlyList<string> secrets);
}

/// <summary>One webhook notification, parsed from a verified payload.</summary>
/// <param name="OrgId">The organization that owns the document.</param>
/// <param name="EventType">The vendor event name, verbatim (e.g. "sale.invoice.update");
///     known names are published as constants, and the set stays open.</param>
/// <param name="ProfileId">The business profile the event belongs to.</param>
/// <param name="ResourceId">The affected document's id.</param>
/// <param name="ResourceUrl">The vendor URL serving the document's current state.</param>
/// <param name="EventTimestamp">When the event occurred at the vendor.</param>
/// <param name="WebhookEventId">The vendor's unique id for this delivery — the consumer's
///     deduplication key, because delivery is at-least-once.</param>
public sealed record MarminAeWebhookEvent(
    Guid OrgId,
    string EventType,
    string ProfileId,
    Guid ResourceId,
    Uri ResourceUrl,
    DateTimeOffset EventTimestamp,
    Guid WebhookEventId);
```

- Verification is **constant-time** over the computed and received MACs and accepts **multiple
  secrets** — the rotation affordance of spec 0007 §5.4: the portal's regenerate flow replaces
  the secret at a keystroke, and accepting old and new across the deploy window is what keeps
  rotation from dropping events.
- **No timestamp-freshness check**, for the reason spec 0007 records: the vendor redelivers
  failed deliveries with original timestamps, and replay defence is deduplication on
  `WebhookEventId` at the handler tier.
- Eight event names are known today — `sale`/`purchase` × `invoice`/`credit_note` ×
  `create`/`update` — and the vendor coalesces pending deliveries to the latest state per
  document, so `EventType` is a routing hint, never a state carrier.

## 6. Testing

Per the spec 0007 §12 tiers: the offline suite runs on every PR; the live suite carries
`Live=true`, runs nightly and on manual dispatch, never gates a PR, and skips cleanly
(attribute-level, before any test body runs) when its credentials are absent.

### 6.1 Offline — `Tellma.Connector.MarminAe.Tests`

- **Signature recipe**: a fixed known-answer vector (message, key, expected Base64) pins the
  HMAC construction against drift toward hex encoding, wrong key/message roles, or trailing
  newlines — every one of which produces a plausible-looking signature the vendor rejects.
- **Token lifecycle**, over a scripted handler: cache hit issues no second token request;
  near-expiry triggers proactive refresh; concurrent refreshes single-flight; `401` on a data
  call → invalidate, fresh token, exactly one retry; second `401` surfaces; `Invalidate()`
  forces a fresh token.
- **Request snapshots** through a fake handler: URL composition (profile and document ids
  escaped), the version and bearer headers on every request, create/resubmit body shape —
  server-owned fields absent by construction, snake_case naming, `DateOnly`/`TimeOnly`/decimal
  formatting — list-query string composition, and the credit-note-only fields.
- **The 8 MB pre-rejection**: an oversized payload fails locally with no wire attempt.
- **`429` handling**: surfaced with parsed rate-limit headers, no second wire attempt.
- **Error parsing vectors** over recorded sandbox bodies: field-level `400` detail, `401`,
  `413`, `5xx`, and an unparseable body degrading to the capped raw capture.
- **Verifier vectors**: acceptance under the right secret; rejection on tampered body, wrong
  secret, and malformed signature; multi-secret acceptance order-independence.
- **Webhook event parsing**: the documented payload round-trips; unknown extra fields are
  ignored; malformed payloads fail cleanly.
- **Peppol status parsing** over recorded snapshots: per-leg and overall statuses, unknown
  status strings passing through verbatim.

### 6.2 Live — `Tellma.Connector.MarminAe.IntegrationTests`

The one thing offline tests cannot vouch for is that payloads, credentials, and signature
recipes satisfy the real API. The live suite runs against the vendor's **sandbox environment** —
a real deployment of the real API with test credentials, whose documents reach no counterparty
and no authority. Credentials arrive in `TELLMA_MARMINAE_TEST_CLIENTID`,
`TELLMA_MARMINAE_TEST_CLIENTSECRET`, and `TELLMA_MARMINAE_TEST_PROFILEID`; the base address
defaults to the sandbox host and `TELLMA_MARMINAE_TEST_BASEADDRESS` overrides it. Cases:

- **Token issuance**: the signature exchange yields a token with a future `expires_at`.
- **Create → retrieve → status → artifacts**, one minimal AED sales invoice: `201` with
  server-assigned id and computed totals; the totals reconcile with the submitted lines;
  retrieve round-trips the echoed fields; the Peppol status endpoint returns a well-formed
  snapshot (shape asserted, not a terminal status — the async legs owe the suite nothing by
  the time it polls); the UBL XML and PDF downloads are non-empty and carry their magic bytes.
- **Credit note**: created against the invoice above via `billing_reference`, asserting the
  credit-note-only required fields pass the real validator.
- **Validation rejection**: a deliberately invalid submission asserts the `400` field-error
  parse against the live shape.
- **Listing**: the created documents' uuids appear in their family listing; the
  purchase-invoice listing succeeds shape-wise (inbound documents cannot be conjured from
  outside — purchase-side reads otherwise rest on the offline vectors).
- **Business profile**: the configured profile retrieves and its `profile_id` matches.

Created documents accumulate in the sandbox account — the API offers no delete — so every
document the suite creates carries a recognizable marker in `buyer_reference`. The suite stays
far inside the 60/minute budget by construction and never probes throttling: deliberately
exhausting a shared account's quota to watch a `429` is a flakiness generator, and the `429`
path is fully pinned offline.

Failure diagnostics follow spec 0007 §12.3: the vendor's own error text on the assertion, the
client's log lines in the test output, and a masked environment report (client id reduced to its
prefix and length, secret to its presence and length) — a nightly failure is read hours later by
someone who cannot reproduce it.

## 7. Definition of done

- `Tellma.Connector.MarminAe` builds and tests on Windows and Linux under the repo's
  warnings-as-errors gates, with a README stating purpose and usage and XML docs on every
  member; public XML docs and error messages reference no `docs/` paths, per repo rule.
- The §6.1 suite green in PR CI; the §6.2 suite wired to the nightly schedule and manual
  dispatch with `Live=true`, skipping cleanly without credentials, and the nightly job failing
  outright when credentials are missing — a scheduled run that skipped everything and reported
  green is the failure that tier exists to prevent.
- Error-body and Peppol-status vectors in §6.1 are recorded from the live sandbox, not authored
  from guesswork.
- No ARCHITECTURE.md changes: the package occupies the existing `Tellma.Connector.<vendor>`
  slot, and the `Ae` compliance-registry addition belongs to the adapter spec.

## Decisions record

The load-bearing decisions, where not already evident above:

1. **`MarminAe`, not `Marmin`** — the vendor slot names the external system, and Marmin's
   country deployments are distinct systems (hosting, versioning, regulatory model); `AcsEmail`
   is the naming precedent, and `MarminSa` remains free for a genuinely different Saudi client
   (§Context).
2. **Raw client now, adapter later** — the adapter's extension points belong to layers that do
   not exist; the client retires vendor-facing risk immediately and is consumable unchanged by
   whatever adapter shape those layers produce (§Context, §Non-goals).
3. **First-party client** — no upstream .NET client exists (§1).
4. **The API version is a pinned constant, not configuration** — a date-versioned wire contract
   changes only with a deliberate, live-suite-verified release (§1).
5. **Separate request/response models make server-owned fields unsendable** — the vendor's
   "always overwritten" and "do not send" rules become compile-time facts (§2).
6. **Create and resubmit share a request type** — resubmit is a full replace, and sharing the
   type makes the full-payload rule structural (§2).
7. **Peppol outcomes are data, not exceptions; the status vocabulary stays open** — acceptance
   is not terminal, and the vendor does not publish the closed set (§2, §4).
8. **One re-auth retry on `401`, nothing else retried** — the token-expiry race resolved where
   it occurs; an unauthenticated request did no work, so the retry cannot duplicate a document.
   Durable retry belongs to the future adapter's outbox (§3, §4).
9. **`429` surfaces with parsed rate-limit headers, never auto-retried** — pacing is caller
   policy; the client's job is honest signal (§4).
10. **Local 8 MB pre-rejection** — a precise local error over a vendor `413` (§2, §4).
11. **Multi-secret webhook verification, constant-time, no freshness check** — rotation without
    dropped events; replay defence is `WebhookEventId` dedup at the handler tier, per the
    spec 0007 §5.4 reasoning (§5).
12. **The live suite is sandbox-only, nightly, and accumulation-tolerant** — sandbox documents
    reach no one, cannot be deleted, and are marked recognizably; throttle behavior is pinned
    offline rather than provoked live (§6.2).
13. **Covered surface is the consumer-driven minimum** — proforma, registers, code lists,
    profile mutations, and purchase-side authoring wait for a consumer; additions are additive
    (§2, §Non-goals).
