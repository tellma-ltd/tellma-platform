# Tellma.Connector.SendGrid.Adapter

The SendGrid transport: an `IEmailSender` over the first-party
[SendGrid client](../Tellma.Connector.SendGrid/README.md), plus the receiver that turns SendGrid's
event webhook into platform delivery events.

```csharp
services.AddSendGridEmail(builder.Configuration);
```

SendGrid is the platform's **hosted fallback** — the SaaS choice wherever the Azure-native default
does not hold, and the one to pick when a deployment needs deferred-event granularity, recipient
spam-complaint events, or a per-request validation mode, none of which ACS emits.

## Configuration

```jsonc
"Email": {
  "Provider": "sendgrid",
  "SendGrid": {
    "ApiKey": "<from the secret store>",
    "From": { "Address": "no-reply@tellma.com", "DisplayName": "Tellma" },
    "MaxConcurrency": 8,
    "TimeoutSeconds": 30,
    "Webhook": {
      // Event-webhook verification keys. More than one so a key can be rotated without dropping
      // events; the receiver exists only when this list is non-empty.
      "VerificationKeys": [ "<base64 public key>" ]
    }
  }
}
```

There is no `Sandbox` section **by design, not by omission**: SendGrid's sandbox channel is a
per-request mode on the same credentials, so it needs no configuration of its own.

## Sending

One message maps to one mail-send request — each message carries its own subject and bodies, so
SendGrid's same-content fan-out across 1,000 personalizations does not apply to this contract. A
batch issues its requests with bounded concurrency and reassembles results positionally.

| Response | Result |
|---|---|
| 2xx | `Sent`, with `X-Message-Id` as the provider id. Sandbox mode answers 200 where an ordinary send answers 202; both mean accepted. |
| 400, 413 | `Rejected` — the payload itself is unacceptable (invalid address, or over the 30 MB cap). |
| 401, 403 | **Throws** if nothing in the batch succeeded: the batch never went out and an operator must fix the key. After a success, the remainder is `TransientFailure` instead. |
| 429 | `TransientFailure`, and the batch's unattempted remainder is short-circuited without firing another request. |
| 5xx, network, timeout | `TransientFailure`. |

A message over SendGrid's 1,000-recipient cap is rejected without a request rather than eating a 400.
The correlation rides in `custom_args` prefixed with this deployment's id, which is what makes a
returning event attributable under shared credentials.

`ExpectsDeliveryEvents` is true only when a verification key is configured *and* the message carries
a correlation — without a key no event would be accepted, and without a correlation none could be
routed. The sandbox channel always reports false: sandbox mode emits no events at all.

## The event webhook

`sendgrid-events`, so the endpoint is `/api/webhooks/sendgrid-events`. Configure it in the SendGrid
dashboard and enable signature verification, then put the issued key in `VerificationKeys`.

**Registered whenever keys are configured, independent of `Email:Provider`** — a deployment that has
migrated to another transport keeps draining the tail of its in-flight SendGrid events. Registration
is decided at composition, so adding the first key takes a restart; that is deliberate, because an
endpoint that exists but always answers 401 is worse than one that is not there.

Per call: POST only, signature verified over the raw bytes, payload parsed, events translated,
handed to the dispatcher. A dispatch failure becomes a transient outcome so SendGrid's redelivery
(30-second batches, retried for 24 hours) is the retry loop.

Events whose correlation envelope names **another deployment** are dropped before dispatch and
metered as `foreign` — routine background under shared credentials, and a misrouted-dashboard signal
under dedicated ones. The drop is per event, not per batch: one batch legitimately mixes deployments,
and dropping it whole would lose this deployment's own events. Events with no correlation at all are
expected too — SendGrid documents that delayed bounces can arrive without the send's metadata.

Event names map `delivered`, `deferred`, `bounce`, `dropped`, `open`, `click`, and `spamreport` onto
the platform types; everything else, including `processed` and the unsubscribe family, maps to
`Other` with the provider's own name kept verbatim.

`Opened` and `Clicked` arrive only where open/click tracking is enabled, which is an account or
subuser dashboard setting rather than a per-message concern — with the usual privacy considerations,
since open tracking embeds a pixel and click tracking rewrites links.
