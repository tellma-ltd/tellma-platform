# Tellma.Connector.SendGrid

A first-party client for the small slice of the SendGrid v3 API the platform needs: one POST
endpoint, the event-webhook signature scheme, the event payload shape, and the vendor error model.
**Zero dependencies** — no packages, no project references — so any future SendGrid adapter
(marketing, inbound parse) can reuse it unchanged.

## Why this exists rather than the official SDK

Assessed August 2026: `sendgrid-csharp`'s last release was 9.29.3 in April 2024 with no commits
since; it targets `netstandard2.0`-era frameworks with no nullability annotations; it serializes with
Newtonsoft.Json, a dependency the platform's System.Text.Json-only package graph should not
transitively impose on every distribution; and it verifies webhook signatures through
`starkbank-ecdsa`, an obscure third-party crypto library where the BCL's own `ECDsa` does the same
job. The surface actually needed is one endpoint plus a signature check, so it is implemented here.

A raw client is written when the upstream client is absent or unfit, not on principle. If the
official SDK comes back to life this is revisitable — the adapter's contract mapping would not
change either way.

## What is in it

**[SendGridClient](SendGridClient.cs)** — `POST /v3/mail/send` over an injected `HttpClient`, with
source-generated serialization and a typed result carrying the status, the `X-Message-Id` header, and
the parsed `errors[]`. Deliberately retry-free: durable retry policy belongs to the caller, and a
retry here would silently duplicate mail the caller believes failed. The bearer token is set per
request, never on `DefaultRequestHeaders`, which is what lets an API-key rotation take effect on the
next batch. The client owns its timeout so that "this request timed out" can be told apart from "the
caller abandoned the batch".

**[SendGridWebhookVerifier](SendGridWebhookVerifier.cs)** — ECDSA verification using BCL crypto only:
import the base64 SubjectPublicKeyInfo key SendGrid issues (the curve rides in the key), verify the
signature over SHA-256 of the timestamp followed by the raw body bytes.

Two details in there are load-bearing enough to name:

- The signature is **ASN.1 DER**, so verification passes `DSASignatureFormat.Rfc3279DerSequence`.
  The overload without it assumes IEEE P1363 and returns false for every genuine delivery — a
  failure mode that looks exactly like a misconfigured key. A test signs with both formats to keep
  that regression impossible.
- **More than one key is accepted**, which is the rotation affordance: SendGrid holds a single
  signing key per webhook and rotating it regenerates that key. The runbook is regenerate, add the
  new key alongside the old, deploy, then drop the old; events 401'd in the gap redeliver for 24
  hours, so nothing is lost.

There is deliberately **no timestamp-freshness check**. SendGrid redelivers failed batches for up to
24 hours carrying their original timestamps, a legitimate staleness no tolerance window can
distinguish from a replay; replay defence is handler-side deduplication on the event id.

**[SendGridEventParser](SendGridEventParser.cs)** — reads an event batch. Hand-rolled over
`JsonDocument` rather than source-generated, because the interesting part of an event — a message's
custom arguments — arrives as arbitrary top-level fields, and the natural model for that is extension
data, which does not work reliably with init accessors under source generation.
