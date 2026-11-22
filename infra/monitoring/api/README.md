# Web API alert queries

One `.kql` file per alert the web surface's instruments are designed to back: the API's own
(meter `Tellma.Core.AspNetCore`) and the data layer's round trips per operation (meter
`Tellma.Core`), which the CRUD pipeline tags with the operation each request runs.

| File | Alerts on |
|---|---|
| `server-errors.kql` | More than five 500 problems on one instance in five minutes: a fault on the server side, never a client's mistake. |
| `roundtrip-budget.kql` | An operation one of whose units of work took more database round trips in the hour than the ceiling of its kind, the sign of an N+1. |

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
counts, minimums and maximums rather than buckets, so the round-trip query compares the hour's
worst unit of work with a ceiling instead of a percentile with the budget. A kind's ceiling is its
warm budget, an overridden validation included, plus the one round trip a cold connect cache or a
stale tag adds: two for a read, three for a delete (whose stack validation makes its warm budget
two) and for a cached table read in full (two beyond its row limit), and four for a save or an
entity action, its dependent-round maximum. Every kind without a row of its own, a distribution's
API actions among them, is held to the save ceiling. A mean would let one save at
five round trips pass among the twos; the price is that a transient failure's retry, a round trip of
its own, can fire the alert alone, so it is one to look at rather than to page on. Jobs,
provisioning steps, an Excel export's rows (one round trip per page) and units of work outside any
request are left out. Thresholds are starting points, not settled operational
policy; they become `Microsoft.Insights/scheduledQueryRules` resources in the per-distribution
Bicep when the alerts are deployed.
