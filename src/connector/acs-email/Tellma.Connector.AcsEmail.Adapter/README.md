# Tellma.Connector.AcsEmail.Adapter

The Azure Communication Services Email transport: an `IEmailSender` on the first-party Azure SDK,
the `Message-ID` correlation channel, and the Event Grid receiver that turns delivery reports into
platform delivery events.

```csharp
services.AddAcsEmail(builder.Configuration);
```

ACS is the default hosted transport for Azure-deployed distributions: per-deployable isolation is its
*native* model at near-zero fixed cost — a Communication Services resource inside each
distribution's own resource group, provisioned by its own Bicep — authentication is managed identity,
and operations land in the Azure estate the platform already runs on.

## Configuration

```jsonc
"Email": {
  "Provider": "acs-email",
  "AcsEmail": {
    "Endpoint": "https://tellma-etpharma.communication.azure.com",
    "From": { "Address": "no-reply@etpharma.tellma.com", "DisplayName": "Tellma" },
    "MaxConcurrency": 8,
    "TimeoutSeconds": 30,
    "Webhook": {
      // Accepted ?token= values on the Event Grid subscription URL; more than one so a token can be
      // rotated. The receiver exists only when this list is non-empty.
      "Tokens": [ "<random secret>" ]
    }
  }
}
```

**There is deliberately no credential.** The client authenticates with a `TokenCredential` — the App
Service's managed identity in Azure, the developer's own Azure sign-in locally — so ACS's HMAC access
keys are left unused and there is no mail secret to store or rotate. A host that needs a specific
identity registers its own `TokenCredential` and wins; otherwise `DefaultAzureCredential` stands in.

## Sending

One message maps to one send request; ACS has no batch endpoint. Recipient-count and request-size
caps are resource-level and support-raisable, so nothing is pre-validated against them — the
provider's synchronous 400 maps to a rejection like any other payload refusal.

**A 202 is reported as `Sent`, and the send operation is never polled.** The 202 means ACS has queued
the message, which is exactly this contract's "accepted by the transport". Polling the long-running
operation is both unwanted — the call must return promptly — and unviable, since status-poll quotas
sit an order of magnitude below send quotas: that endpoint is a diagnostics facility, not a status
channel.

The consequence is worth stating plainly: **post-acceptance failures arrive only as delivery
reports, which makes the Event Grid receiver part of this transport.** Without it, mail that fails
after acceptance fails silently. A deployment activating this transport configures the webhook as a
matter of course, and the startup log line makes a missing configuration visible.

401 and 403 throw when nothing in the batch succeeded and report transient afterwards; 429
short-circuits the unattempted remainder, because default quotas are low and continuing to issue only
deepens the hole. Everything else transient.

`clientOptions.Retry.MaxRetries = 0` is the single most important line in this adapter: Azure.Core
otherwise retries 429 and 5xx three times with backoff, which is exactly the durable retry the
contract reserves for the caller and which would silently duplicate mail on an ambiguous failure.
It lives in `BuildClientOptions`, which both the deployment path and the test harness call, so the
suite pins the line a deployment actually runs rather than a copy of it.

Two failure modes deserve their own mention, because neither arrives as a `RequestFailedException`
and both would otherwise escape the batch loop entirely. Azure.Core raises its own network timeout as
a `TaskCanceledException` on a token the caller never cancelled, and a token credential raises
`AuthenticationFailedException`. The first becomes a per-message transient failure; the second is an
authentication failure in the contract's sense and takes the stop-drain-decide path a 401 takes.

There is **no sandbox channel** — ACS offers no validate-only mode — so the pipeline withholds a
sandbox tenant's external mail itself.

## The Message-ID correlation channel

ACS keeps a customer-supplied internet message id and echoes it on every delivery report, which makes
the `Message-ID` header the correlation channel here. Correlated mail is stamped
`<tlm1-{base32hex}-{entropy}@{sending domain}>`:

- **base32hex** because the canonical correlation contains colons, which are not legal in an RFC 5322
  atom, and the value must survive byte for byte;
- **a random suffix per send**, ignored at parse, because message ids must be unique per message
  while correlations repeat across resends — a repeated id risks recipient-side threading and
  deduplication;
- **the sending domain** on the right, because an id that disagrees with it is a weak spam signal.

`x-ms-acsemail-validate-message-id` is deliberately left unset. Under the lenient default an invalid
or duplicate id is silently replaced with a generated one, so the worst case is an event that meters
as uncorrelated; strict validation would instead turn a correlation nicety into a rejected email.
Parsing is tolerant for the same reason: an ACS-generated id, mail sent outside the platform, or a
future format all resolve to no correlation and flow through the pipeline's metering.

**No deployment envelope is stamped.** Each deployment's events return only to its own Event Grid
subscription on its own resource, so cross-deployment protection is structural on this transport.

**Engagement events carry no message id** in either the GA or the preview contract, so `Opened` and
`Clicked` are uncorrelated here by design — translated and metered, never routed to a handler. That
is an accepted trade for opt-in engagement telemetry, and it dissolves by parsing alone if the field
is ever added; a deployment needing correlated open/click tracking uses SendGrid.

## The Event Grid receiver

`acs-email-events`, so the endpoint is `/api/webhooks/acs-email-events?token=<secret>`. Subscriptions
are provisioned on the Event Grid system topic of the deployment's own resource, with the
EventGridEvent schema.

Event Grid does not sign its deliveries, so authenticity rests on that URL token, compared in
constant time against the accepted list. Microsoft Entra ID delivery authentication is the documented
hardening upgrade — it changes subscription provisioning, not this code. The webhook fronting never
logs query strings, which is what keeps the token out of the log store.

**Dead-lettering is part of provisioning, not an option.** Event Grid never retries a delivery
answered 401 or 403, so a token-rotation mistake would otherwise drop events silently. Provision
every subscription with a dead-letter container.

Statuses map `Delivered`, `Bounced`, `Suppressed` (the provider's own managed suppression list) onto
delivered, bounced, and dropped; `Failed`, `Quarantined`, and `FilteredSpam` onto failed; anything
else, including the documented `Expanded`, onto `Other`. The comparison is on the raw string rather
than the SDK's enum members, because ACS documents statuses the SDK has no member for.

## Provisioning notes

Everything is the distribution's own Bicep: the Communication Services resource, the Email
Communication Service and custom domain (whose SPF and DKIM records feed the distribution's DNS
setup), the managed-identity role assignment, the Event Grid system topic, and the webhook
subscription with its dead-letter storage. **Quotas gate onboarding** — a fresh subscription sends 30
messages per minute on a custom domain, and increases go through a reputation-gated support ticket
that is an onboarding prerequisite rather than an afterthought.

One thing not used, so the next reader does not rediscover it as a missed opportunity: ACS exposes a
send overload taking a client-supplied operation id, which would give idempotent retries. There is no
durable retry at this tier, correlation already rides the `Message-ID`, and a client-supplied id
would make `ProviderMessageId` a value Azure diagnostics did not mint.
