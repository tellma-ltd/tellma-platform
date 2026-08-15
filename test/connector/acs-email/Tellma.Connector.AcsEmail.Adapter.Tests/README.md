# Tellma.Connector.AcsEmail.Adapter.Tests

The adapter over a real Azure pipeline whose transport is redirected through a scripted HTTP handler
— a public seam on the client options, so no semi-internal test framework is needed and the same
scripted handler serves this transport and the SendGrid one.

| Folder | Covers |
|---|---|
| `Correlation/` | The base32hex codec at every length remainder, and the message-id codec: round trips including colons and non-ASCII in the reference, a different id per attempt for the same correlation, tolerant parsing of everything it did not produce, and the length budget. |
| `Sending/` | The outcome table, the zero-retry pin, the message id stamped only on correlated mail, the validate-message-id header deliberately absent, the delivery-event expectation, and the bare sender address that drops any display name — with the matching startup refusal of a configured one. Also the two failures that do not arrive as a `RequestFailedException` and so once escaped the batch loop entirely: the Azure pipeline's own network timeout, and a token credential that cannot produce a token. |
| `Webhook/` | Recorded Event Grid payloads: the subscription-validation echo, a correlated delivery report, the envelope-time fallback when the delivery timestamp is spelled the way Microsoft's samples spell it, an engagement report as uncorrelated by design, the full status and engagement maps, token verification and rotation, and a dispatch failure becoming transient. |
| `Conformance/` | The ACS transport answering the shared `IEmailSender` contract. Its no-retry case doubles as the behavioural proof that the Azure pipeline's retry policy really was zeroed: an unconfigured pipeline would answer a scripted refusal with three more requests. |

## Running

```bash
dotnet test test/connector/acs-email/Tellma.Connector.AcsEmail.Adapter.Tests
```
