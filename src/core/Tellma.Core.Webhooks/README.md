# Tellma.Core.Webhooks

The one HTTP fronting every inbound webhook shares, so no connector ever writes a controller.
Email is its first consumer; nothing in it is email-specific.

```csharp
services.AddTellmaWebhooks();   // options, instruments, the startup checks
app.MapTellmaWebhooks();        // GET + POST /api/webhooks/{key}
```

Both the receiver keys and this package's own `Webhooks` section are validated at startup. Unlike an
email adapter's options — which are checked only when that transport is the active one — this
section is always in play wherever the fronting is registered at all, so it validates on start
unconditionally. A body cap below 1 KiB is refused, because a cap of zero would answer every inbound
webhook 413 and the only visible symptom would be a provider quietly giving up.

Connector adapters register their own `IWebhookReceiver`s; this package never knows what they are.

## The route is a contract

`/api/webhooks/{key}` is configured into provider dashboards by operators, so it must stay stable
across releases — and a receiver key is part of its connector's public surface. Renaming one is a
breaking change, which is why duplicate and malformed keys fail at startup rather than on the first
callback.

## How a request is handled

1. **Find the receiver** by key. No match is a 404 with no body — probes and scanners land here, and
   they are counted under a literal key so they cannot inflate the metric's cardinality.
2. **Buffer the body** whole, under `Webhooks:MaxRequestBodyBytes` (2 MiB by default). Signature
   verification needs the exact wire bytes, so nothing may normalize or stream past them. An
   oversized request is answered 413 and metered, never dispatched.
3. **Hand it over** as a `WebhookRequest`, with headers and query parameters in case-insensitive
   dictionaries so receivers index them without defensive re-wrapping.
4. **Map the outcome** to a status code, which is what drives the caller's redelivery behaviour:
   accepted → 200, unauthorized → 401, invalid → 400, transient → 503, and an unhandled receiver
   exception → 500 (semantically transient, so the provider retries).

A receiver may return a response body only for the challenge echoes some providers require at
endpoint registration. A body with no content type is dropped rather than sent unlabeled — providers
are strict about it, and an unlabeled echo fails their verification in a way that is hard to
diagnose from their side.

## Rules this package lives by

- **The endpoint is anonymous and antiforgery-exempt.** A webhook caller holds no platform
  credential; the receiver's own signature verification *is* the authentication.
- **Nothing logs a request body or a query string, at any level.** Bodies carry recipient addresses
  and provider junk; query strings carry webhook credentials. Diagnostics rely on the receiver's
  `Detail`, the metrics, and the translated events.
- **A webhook belongs to the deployable, not to a tenant.** The endpoint carries
  [WebhookEndpointMetadata](WebhookEndpointMetadata.cs) so a host's tenant-resolution middleware can
  recognize and skip it; the tenant is reached later, through the correlation in the payload.
- **The receiver's `Detail` never reaches the caller.** It is for our logs; a forger must learn
  nothing from the response.
