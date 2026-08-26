# Vector provenance

The files here are the inputs to the offline parser tests. Every one declares where it came from in
its own name, and nothing in this suite is allowed to be ambiguous about it. JSON carries no
comments, so the file name is the only marker that survives being read in a diff, a stack trace, or
the build output.

| Suffix | Means |
|---|---|
| `*.recorded.*` | A capture of a real response from the vendor's UAE sandbox, verbatim except for the substitutions listed below. |
| `*.synthetic.*` | **Not a recording.** Hand-authored, because the response it stands for could not be provoked from outside. A plausible shape, not an observed one. |

## What every recording had replaced

Account identifiers, and only account identifiers. Everything else — field names, ordering, nulls,
number formatting, whitespace — is exactly as the vendor sent it.

| In the recordings | Was |
|---|---|
| `MBP-EXAMPLE0000000` | the sandbox business profile identifier |
| `00000000-0000-4000-8000-00000000000n` | the organization, profile, user, and address identifiers |
| `Example Seller`, `Example Street`, `seller@example.com` | the sandbox account's own name, address, and mailbox |
| `1000000002`, `100000000200003`, `10000002` | the sandbox account's TIN, VAT registration, and trade licence |
| the JWT in `Documents/token-response.recorded.json` | a real sandbox token, replaced with a structurally identical one whose claims are placeholders |
| the `request_id` in `Errors/429-rate-limited.recorded.json` | the vendor's identifier for that one request |

## What a synthetic file is, and is not, evidence of

A synthetic file cannot show that a parser reads the vendor's real responses correctly. It shows
only that the parser reads *that* shape correctly, and that shape is a guess.

What the tests over them do establish is **tolerance**: that no body, of any shape, makes a parser
throw, lose the HTTP status, or discard the raw capture an operator needs to diagnose a failure.
That property is real, it is what protects the caller when a guess turns out wrong, and it is worth
pinning either way.

Do not cite a synthetic file as evidence of vendor behaviour — not in a review, not in an incident,
not in a specification.

## What each file is

### Recorded

| File | What it is |
|---|---|
| `Errors/400-field-validation.recorded.json` | The vendor refusing a document with three bad fields. A map from field name to message under an `errors` object. |
| `Errors/401-invalid-token.recorded.json` | The vendor refusing a bearer it never issued. Captured without the client, so no token was spent obtaining one. |
| `Errors/404-unknown-document.recorded.json` | A missing document. A single message under an `errors` object. |
| `Errors/404-unknown-profile.recorded.json` | A missing business profile. A bare `error` string at the root — a third shape, on a third endpoint. |
| `Errors/413-payload-too-large.recorded.json` | The vendor refusing an oversized submission. Captured by probing the cap the vendor states ambiguously: 8,099,998 bytes were accepted and reached validation, 8,499,998 were refused, which places the limit at 8 MiB rather than eight million bytes. Both probe documents were deliberately invalid, so neither created anything. |
| `Errors/429-rate-limited.recorded.json` | Throttling, from the token endpoint, which allows five calls a minute. A flat `message` at the root, with a request identifier. The response carried `RateLimit-Limit: 5`, `RateLimit-Remaining: 0`, `RateLimit-Reset: 47` and `Retry-After: 47` — note that the reset is a wait in seconds, not an instant, and that none of the header names the vendor's reference documents were present. |
| `PeppolStatus/status-not-ready.recorded.json` | The status route on a document the network has not started carrying. |
| `PeppolStatus/status-logs.recorded.json` | The transmission log of a freshly accepted document: a bare array, snake-cased, one entry. |
| `Documents/sales-invoice.recorded.json` | A sales invoice as the vendor stored it, totals and all. |
| `Documents/sales-credit-note.recorded.json` | The same for a credit note, with the three fields an invoice does not carry. |
| `Documents/page-sales-invoices.recorded.json` | One page of a family listing. |
| `Documents/page-sales-invoice-ids.recorded.json` | One page of identifiers. |
| `Documents/business-profile.recorded.json` | The configured business profile. |
| `Documents/token-response.recorded.json` | The token exchange's answer. Worth reading: it carries `expires_in`, not the `expires_at` the vendor's reference documents. |

### Synthetic, and why it had to be

| File | Why it could not be captured |
|---|---|
| `PeppolStatus/snapshot-transmitted.synthetic.json` | No document in the sandbox account reaches a transmitted state, so the two-leg snapshot has no observable instance. The field names are the vendor's, from its published example; the nesting, the nullability and the values are not evidence. |
| `PeppolStatus/snapshot-one-leg-absent.synthetic.json` | The same, plus a leg deliberately absent and a status deliberately unrecognised — the two things the reader must survive. |
| `PeppolStatus/meta-info-peppol-status.synthetic.json` | Every document the sandbox returns carries a null `meta_info`, so the summary block has no observable instance either. |
| `PeppolStatus/meta-info-validation-failed.synthetic.json` | The same, for the one outcome a caller most needs to read. The validation-result element names come from the vendor's guidance rather than from a capture. |
| `PeppolStatus/status-logs-empty.synthetic.json` | Trivially empty; not a shape claim. |
| `Errors/500-internal.synthetic.json` | Not provocable on demand, and not worth provoking. |
| `Errors/400-described-list.synthetic.json`, `Errors/400-multi-valued-paths.synthetic.json`, `Errors/problem-details.synthetic.json` | Shapes the vendor has not been seen using. They are here for tolerance, not fidelity: the parser must survive the day it starts using one. |
| `Errors/502-gateway.synthetic.html` | Not vendor-shaped at all — a gateway error page, standing in for any non-JSON failure body. |
| `Errors/truncated.synthetic.json`, `Errors/empty.synthetic.txt` | Deliberately malformed and deliberately empty. Not shape claims. |

## Replacing a synthetic file with a recording

Vectors load by logical name and a recording wins over a synthetic file of the same name, so
promoting one is adding one file:

1. Take the body from the live suite's attached response bodies — its job produces them as an
   artifact, named exactly for the file each becomes — or capture it by hand.
2. The live suite replaces the account identifiers with a `<redacted>` marker on the way out, so a
   body from the artifact is already scrubbed; read it anyway, and swap in a placeholder from the
   table above wherever a name reads better than a marker. A body captured by hand needs the
   substitutions made by hand.
3. Save it beside the synthetic file as `<name>.recorded.<ext>`.
4. Run the suite. It now reads the recording. Delete the synthetic file in the same commit, and add
   a row above.

Nothing else changes: no test code, no project file. If a test now fails, the guess was wrong and
the parser needs fixing — which is the whole point of the exercise.
