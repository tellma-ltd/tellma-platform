# Client alert queries

One `.kql` file per alert the SPA's telemetry is designed to back: the two counters the client
telemetry relay (`POST /api/client-telemetry`) keeps on the web host's meter
(`Tellma.Core.AspNetCore`) for the items it accepts.

| File | Alerts on |
|---|---|
| `client-errors.kql` | More than 25 render or global errors across the deployment in 15 minutes: a fault in the client's own code, which a release or a browser update can bring to every user at once. |
| `slow-requests.kql` | More than 50 of the SPA's requests reaching 3 s across the deployment in 15 minutes, as the browser saw them, network and queueing included. |

The counters carry no tenant and no user: each tag takes a closed set of values, and a value a
client sends outside its set is counted as `other`. The items themselves, with the tenant the client
claims, its route and its user's subject, are log records in `customEvents` and `traces`.

**These are checked against the code.** A test in `Tellma.Core.AspNetCore.Tests` resolves every
`customMetrics` name, every `customDimensions[...]` key and every quoted value the queries compare
against to an instrument, a tag or a tag value the relay can emit, and requires every relay
instrument to back at least one query.

The queries are written against Application Insights' `customMetrics` table, where the
OpenTelemetry instruments land through the Azure Monitor exporter. They sum across instances, since
a client's error is not the instance's that relayed it. Thresholds are starting points, not settled
operational policy, and scale with a deployment's users; they become
`Microsoft.Insights/scheduledQueryRules` resources in the per-distribution Bicep when the alerts
are deployed.
