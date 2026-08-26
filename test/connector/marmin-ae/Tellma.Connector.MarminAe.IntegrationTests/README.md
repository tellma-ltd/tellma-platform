# Tellma.Connector.MarminAe.IntegrationTests

The one thing no offline test can vouch for: that the payloads, the credentials, and the signature
recipe satisfy the real API. Runs against the vendor's UAE sandbox — a real deployment of the real
API, whose documents reach no counterparty and no authority.

## What it creates, and why that is safe

Roughly five documents a run. The API offers no delete, so they accumulate; every one carries a
`TELLMA-LIVE-{run}-{id}` marker in `buyer_reference`, which is how an operator selects them for a
sweep and how a stray document is traced back to the run that made it.

One client and one token provider serve the whole suite, and the classes run one at a time. That is
not only politeness: the vendor allows **five token requests a minute** against sixty document
calls, so a suite that authenticated per test would throttle itself before it finished.

## What it deliberately does not assert

- **A terminal transmission status.** The delivery and reporting legs run asynchronously and owe the
  suite nothing by the time it asks. Only the shape is asserted.
- **The dedicated XML and PDF downloads succeeding.** The vendor gates both on the document reaching
  an approved status, which no sandbox document does. The suite asserts the artifact the vendor
  *does* render immediately — the PDF it holds as an attachment — and asserts that the two gated
  routes refuse with an explanation the client could parse, which is the part the client is
  responsible for.
- **Throttling.** Deliberately exhausting a shared account's quota to watch a refusal is a
  flakiness generator, and that path is pinned offline.
- **Resubmission.** It is permitted only from a validation failure, which cannot be provoked on
  demand. The request shape is pinned offline; this is a known live gap.
- **Webhook delivery.** That is synthetic monitoring, not a test; the verifier and the parser rest
  on offline vectors.

## Diagnosing a failure

Three things travel with every one, because whoever reads a nightly failure is hours late and cannot
reproduce it.

- **The vendor's own words on the assertion**, not `Expected: 201, Actual: 400`.
- **A masked report of what the run was pointed at** — the client id and profile id reduced to a
  prefix and a length, the secret to its presence and length. These logs are published by a public
  repository's build runs.
- **The full request and response transcript**, with the bearer, the signature and the client id
  redacted, and every body scrubbed of the sandbox account's own identity — its tax numbers, its
  trade licence, its network address, its postal address and its mailbox. The client is
  dependency-free and logs nothing of its own, so this is where the detail comes from.

## Feeding the offline vectors

Every JSON response body is also attached to the test result under exactly the file name of the
offline vector it would become — `400-field-validation.recorded.json` and the rest — and the nightly
job uploads them as an artifact. Promoting one is downloading the artifact and dropping the file
into the offline suite's `Vectors/` folder, beside the sibling that names the same thing; the
account identifiers are already replaced, so read the file before committing it and swap in a
readable placeholder wherever a `<redacted>` marker reads worse than a name would. Delete the
synthetic in the same commit. No test code changes; see that folder's `PROVENANCE.md`.

Only JSON is captured, and only when the vendor declared its length in advance. A rendered document
is streamed to its caller, and buffering one here to write it into a log would defeat the streaming
and publish the document.

## Running

Marked `Category=Integration` and `Live=true`, so it is excluded from every pull-request job and
runs nightly. Each test skips cleanly when the credentials are absent, so running the filter locally
without them is safe.

```bash
dotnet test test/connector/marmin-ae/Tellma.Connector.MarminAe.IntegrationTests --filter "Live=true"
```

| Variable | What it is |
|---|---|
| `TELLMA_MARMINAE_TEST_CLIENTID` | The sandbox organization client id. Not a secret, but supplied by the environment so the suite is not tied to one account. |
| `TELLMA_MARMINAE_TEST_CLIENTSECRET` | The matching client secret. |
| `TELLMA_MARMINAE_TEST_PROFILEID` | The business profile documents are issued under. It must have finished onboarding; a dedicated test asserts that, so a half-configured account fails on a case that says so rather than only on every submission. |
| `TELLMA_MARMINAE_TEST_BASEADDRESS` | Optional; overrides the sandbox host. Deliberately outside the credential gate and fatal when unparseable — a typo must not quietly turn the suite into a skip. |

The sandbox account is provisioned by the vendor's support team; it cannot be self-served.
