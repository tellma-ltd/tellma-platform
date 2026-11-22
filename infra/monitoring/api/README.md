# Web API alert queries

One `.kql` file per alert the web surface's instruments are designed to back: the API's own
(meter `Tellma.Core.AspNetCore`) and the data layer's round trips per operation (meter
`Tellma.Core`), which the CRUD pipeline tags with the operation each request runs.

| File | Alerts on |
|---|---|
| `server-errors.kql` | More than five 500 problems on one instance in five minutes: a fault on the server side, never a client's mistake. |
| `roundtrip-budget.kql` | An operation whose units of work averaged more database round trips over the hour than the budget of its kind, the sign of an N+1. |

The third alert the web surface is designed to back, CSRF refusals on the `origin` rule above a
floor (a misdeployed public origin), is the host's `../host/csrf-rejections.kql`.

Four instruments back no alert, by design: `tellma.api.requests.rejected` counts rate-limited and
outdated clients, both a normal part of a busy day or a deployment; `tellma.api.operation.duration`
sizes the workload's latency; `tellma.api.unknown_members` diagnoses a client sending members the
server ignores; and `tellma.realtime.events` counts what the hub publishes, which the realtime
queries under `../realtime/` read through their own instruments.

**These are checked against the code.** A test in `Tellma.Core.AspNetCore.Tests` resolves every
`customMetrics` name, every `customDimensions[...]` key and every quoted value the queries compare
against to an instrument, a tag or a tag value the platform can emit, and requires every API
instrument but the four above to back at least one query.

They are written against Application Insights' `customMetrics` table, where the OpenTelemetry
instruments land through the Azure Monitor exporter. A histogram lands there as per-interval sums,
counts, minimums and maximums rather than buckets, so the round-trip query compares the hour's mean
with the budget instead of a percentile. Thresholds are starting points, not settled operational
policy; they become `Microsoft.Insights/scheduledQueryRules` resources in the per-distribution
Bicep when the alerts are deployed.
