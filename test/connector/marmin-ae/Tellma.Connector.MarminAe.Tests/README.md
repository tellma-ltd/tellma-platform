# Tellma.Connector.MarminAe.Tests

The raw client, offline. Everything the vendor's wire contract asks of us that can be proven without
an account, driven through a scripted transport and a clock the tests advance. Nothing here sleeps,
polls, or reads the wall clock.

| Folder | Covers |
|---|---|
| `Auth/` | The signature recipe, against answers computed with OpenSSL rather than by the code under test — drift toward hex, a swapped key and message, a trailing newline, or an ASCII round trip over a non-ASCII secret each produces a plausible-looking signature the vendor rejects. Then the token cache: the refresh margin asserted from both sides, single-flight under eight concurrent callers, a failed request that must not be cached, the one re-authentication a refusal buys — including the body being replayed byte for byte, which a naive retry cannot do — and everything else that is deliberately not retried. |
| `Requests/` | Every route across all four families, identifiers escaped, and a base address whose path prefix must survive. The version header and the bearer over a matrix of every public method. Submission bodies: the fields the vendor owns absent from the wire, snake_case throughout, dates and times in the vendor's formats, decimals unchanged under a comma-decimal culture, and the credit-note-only fields. Listing queries. The local size cap. |
| `Responses/` | Documents, listings, downloads, the business profile, and the two differently shaped transmission-status payloads — camel-cased on one route, snake-cased on the other, which is why they are separate types and separate classes. Unknown fields ignored, unknown statuses passed through, a date returned as a timestamp read anyway. |
| `Errors/` | Refusal parsing over the vectors, throttling with the quota reading parsed and no second attempt, and cancellation — a caller who gave up and a request that ran out of time, told apart. |
| `Webhook/` | Signature vectors: acceptance, then rejection on a tampered body, on a body differing only in insignificant whitespace, on the wrong secret, on a hex encoding, and on a malformed signature. Multi-secret acceptance wherever the live secret sits. Then notification parsing, for tolerance as much as correctness. |
| `Vectors/` | The parser inputs. **Read [PROVENANCE.md](Vectors/PROVENANCE.md) before citing any of them.** |
| `Infrastructure/` | The scripted harness over the shared handler, the response builders, the operation matrix, and the vector loader. |

## Three things worth knowing before changing anything here

**Most vectors are captures; some are not, and the file name says which.** A file ending
`.recorded.` came off the vendor's sandbox with only account identifiers replaced. A file ending
`.synthetic.` is hand-authored, because the response it stands for could not be provoked — no
document in the sandbox account reaches a transmitted state, so the two-leg status snapshot has no
observable instance, and the client refuses an oversized submission before the vendor ever gets to.
Tests over synthetic files establish tolerance, not fidelity, and a test that reads one says so.

**The concurrency cases are decided, not raced.** The single-flight test launches its callers
synchronously and holds the response open on a task it completes itself, which is what makes "a
provider that did not single-flight would already have issued eight requests" a fact rather than a
hope. Do not introduce a delay, a sleep, or a poll to make something here pass.

**One test reads IL.** `Compares_the_macs_with_the_frameworks_fixed_time_comparison` is the only
executable form the constant-time promise has; a timing measurement on a shared runner is a coin
flip that ends up skipped. Extracting the comparison into another type will break it, which is the
point — that refactor is exactly when the property gets quietly lost.

## Running

```bash
dotnet test test/connector/marmin-ae/Tellma.Connector.MarminAe.Tests
```
