# Outbox alert queries

One `.kql` file per alert the email outbox's telemetry is designed to back: the outbox's
instruments (meter `Tellma.Core`) and the sending job's log events (category `Tellma.Core.Outbox`).

| File | Alerts on |
|---|---|
| `interrupted-sends.kql` | Any outgoing email a later claim found still sending: it may or may not have reached its recipient, and was failed rather than sent twice. |
| `failure-rate.kql` | Emails that settled failed or rejected above 5% of the emails settled, or more than 20 in the window. |
| `latency-p95.kql` | A 95th-percentile wait from enqueue to send above ten minutes. |

The other instrument backs no alert, by design: `tellma.outbox.enqueued` sizes the workload by
source (the platform's api and the notification channel). The delivery-event and retention
instruments ship with the delivery-event handler and the retention job.

**These are checked against the code.** A test in `Tellma.Core.Tests` resolves every
`customMetrics` name, every `customDimensions[...]` key and every quoted value the queries compare
against to an instrument, a tag, a log event name or a tag value the outbox can emit, and requires
every instrument but the one above to back at least one query. Renaming an instrument, a tag, a tag
value or a log event therefore fails the build instead of leaving an alert that reports zero
forever.

The metric queries are written against Application Insights' `customMetrics` table, where the
OpenTelemetry instruments land through the Azure Monitor exporter that `UseAzureWebDefaults` adds
when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. The interrupted-sends query reads the `traces`
table, where the log records land with their event name as a custom dimension. Thresholds are
starting points, not settled operational policy; they become
`Microsoft.Insights/scheduledQueryRules` resources in the per-distribution Bicep when the alerts are
deployed.
