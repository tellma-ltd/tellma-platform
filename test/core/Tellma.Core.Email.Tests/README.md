# Tellma.Core.Email.Tests

The pipeline's own behaviour, driven through a composition built the way a host builds one — so the
startup gates are exercised rather than bypassed.

| Folder | Covers |
|---|---|
| `Routing/` | The routing matrix over fake transports, the sandbox marking (subject idempotence, the text prefix, HTML banner placement including the cases a naive scanner gets wrong), the `Sent`→`Sandboxed` rewrite, positional reassembly of mixed batches, and what the router does when a later partition fails after an earlier one produced results. |
| `Selection/` | Everything the pipeline refuses to start with: a missing or unknown provider, duplicate or malformed transport names, the log sink outside Development, a missing deployment identity or sandbox context, duplicate handler owner keys — plus the guarantee that an inactive adapter stays cold. |
| `LogSink/` | What the sink logs, the channel it names, and both halves of its Development-only guard. |
| `Dispatch/` | Grouping and order preservation, the metering of uncorrelated and unknown-owner events, the arrival-lag histogram against an injected clock, and handler failures propagating. |
| `Conformance/` | The log sink answering the shared `IEmailSender` contract. |
| `Observability/` | Every instrument's tags — including a guard that the tag set has not grown anything tenant-identifying — and a cross-check of the checked-in alert queries against the telemetry constants the pipeline actually emits. |

## Running

```bash
dotnet test test/core/Tellma.Core.Email.Tests
```
