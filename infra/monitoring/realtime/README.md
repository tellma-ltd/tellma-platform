# Realtime alert queries

One `.kql` file per alert the realtime hub's telemetry is designed to back: the hub's instruments
on the web host's meter (`Tellma.Core.AspNetCore`).

| File | Alerts on |
|---|---|
| `connects-refused.kql` | An instance refusing more than 30 hub connection attempts a minute for five minutes: a stale SPA build, or someone probing tenants. |
| `access-revoked-untold.kql` | A window in which an instance closed connections for a revoked access without handing any `session.ended` to the transport. |

The other instrument backs no alert, by design: `tellma.realtime.connections` sizes the workload
(the connections each instance holds open, by hosting mode).

**These are checked against the code.** A test in `Tellma.Core.AspNetCore.Tests` resolves every
`customMetrics` name, every `customDimensions[...]` key and every quoted value the queries compare
against to an instrument, a tag or a tag value the hub can emit, and requires every instrument but
the one above to back at least one query. Renaming an instrument, a tag or a tag value therefore
fails the build instead of leaving an alert that reports zero forever.

The queries are written against Application Insights' `customMetrics` table, where the
OpenTelemetry instruments land through the Azure Monitor exporter that `UseAzureWebDefaults` adds
when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. Thresholds are starting points, not settled
operational policy; they become `Microsoft.Insights/scheduledQueryRules` resources in the
per-distribution Bicep when the alerts are deployed.
