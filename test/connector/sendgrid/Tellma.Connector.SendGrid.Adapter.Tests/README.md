# Tellma.Connector.SendGrid.Adapter.Tests

The adapter over a scripted HTTP wire, keyed by an ordinal each conformance message carries in its
subject.

| Folder | Covers |
|---|---|
| `Infrastructure/` | The harness: the sender built over a scripted handler, with per-message replies and a settled record of which messages actually reached the wire. |
| `Sending/` | The outcome table, including the credential failure that throws when nothing went out and reports when something did, and the throttling response that stops the batch. The recipient-cap rejection that fires without a request, the default sender, the deployment envelope stamped into the correlation, and the exact conditions under which a result expects delivery events. |
| `Webhook/` | Every documented event name and the unknown ones, the whole translated event, uncorrelated events passing through, foreign-deployment events dropped *and* metered, the signature and method checks, malformed payloads, and a dispatch failure becoming a transient outcome. |
| `Conformance/` | The SendGrid transport answering the shared `IEmailSender` contract. |

## Running

```bash
dotnet test test/connector/sendgrid/Tellma.Connector.SendGrid.Adapter.Tests
```
