# Tellma.Connector.AcsEmail.IntegrationTests

ACS has no validate-only mode, so this suite **really delivers**: one minimal and one full-feature
message to a fixed Tellma-owned mailbox on a dedicated test resource, asserting acceptance. Two
messages sit comfortably inside even a fresh subscription's default quota.

Event Grid delivery is deliberately not asserted — that is synthetic monitoring, which is out of
scope; the receiver's correctness rests on the recorded-payload suite instead. The full-feature
message is correlated, so a first pilot can watch the message-id echo come back on the delivery
report by hand.

Authentication is a token credential: a developer's own Azure sign-in locally, a federated identity
in CI. There is no mail secret to store, which is the point of the managed-identity design.

## Diagnosing a failure

The reader of a nightly failure cannot step through it, so three things travel with every one: the
transport's error text on the assertion (not just `Expected: Sent, Actual: Rejected`), the adapter's
own log lines at `Debug` in the test output, and a report of the environment the run used — the
endpoint, and the sender and recipient masked to their domains, since the sending domain is the
likeliest cause of a refusal and the mailboxes must not be published by a public repository's
Actions runs. This is how the suite's first real find — ACS refusing a `senderAddress` that carried
a display name — was read off a failed run rather than reproduced by hand.

## Running

Marked `Category=Integration` and `Live=true`, so it is excluded from every PR job and runs in the
nightly workflow. Each test skips cleanly when the environment is absent.

```bash
az login
```

```bash
dotnet test test/connector/acs-email/Tellma.Connector.AcsEmail.IntegrationTests --filter "Live=true"
```

| Variable | What it is |
|---|---|
| `TELLMA_ACS_TEST_ENDPOINT` | The test Communication Services resource endpoint. |
| `TELLMA_ACS_TEST_SENDER` | An address on a domain that resource is provisioned for. |
| `TELLMA_ACS_TEST_RECIPIENT` | The Tellma-owned mailbox that receives the mail. Supplied by the environment rather than hardcoded, so a live address is not published in a public repository. |

The signed-in identity needs the ACS email send role **on that resource only**.
