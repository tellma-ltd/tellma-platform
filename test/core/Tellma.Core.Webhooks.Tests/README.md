# Tellma.Core.Webhooks.Tests

The webhook fronting, driven through a real in-memory web host rather than by calling the handler,
so the routing, the anonymous access, and the body pipeline are all genuinely exercised.

| Folder | Covers |
|---|---|
| `Fronting/` | The outcome-to-status map that drives a provider's redelivery, the challenge-echo path (and what happens to a body with no content type), unknown keys, receiver exceptions, exact wire bytes reaching the receiver, case-insensitive headers and query parameters, the body cap at and over the limit, and the startup validation of receiver keys. |
| `Observability/` | The request instruments, including the outcomes no receiver ever sees — an unknown key and an oversized body — and the literal key that keeps scanner traffic from inflating the dimension. |

## Running

```bash
dotnet test test/core/Tellma.Core.Webhooks.Tests
```
