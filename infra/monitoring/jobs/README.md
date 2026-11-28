# Job alert queries

One `.kql` file per alert the background-job machinery's telemetry is designed to back: the job
worker's and the scheduler's instruments (meter `Tellma.Core`) and the worker's heartbeat log
events (category `Tellma.Core.Jobs`).

| File | Alerts on |
|---|---|
| `dead-letter-rate.kql` | A handler whose jobs end `Failed` above 5% of its completions, or more than 20 in the window. |
| `backlog-age.kql` | A handler whose oldest claimable job has waited past its due time for more than ten minutes. |
| `scheduler-gap.kql` | Any poll that found the worker had been away from a tenant past the gap threshold, so overdue work was held for an administrator. |
| `stale-heartbeat.kql` | A served tenant whose latest heartbeat is older than two minutes: no instance is polling it. |

The other instruments back no alert, by design: `tellma.jobs.claimed`, `tellma.jobs.duration`,
`tellma.jobs.queue.latency`, `tellma.jobs.batch_fill`, `tellma.jobs.in_flight`,
`tellma.jobs.lease_lost`, `tellma.jobs.poll.duration` and `tellma.jobs.orphaned` size and tune the
workload (throughput, lease and batch sizing, concurrency, poll cost, rows of keys no instance
runs), and `tellma.schedules.fired`, `tellma.schedules.missed` and
`tellma.schedules.overlap_skipped` describe the scheduler's firing volume and policies.

**These are checked against the code.** A test in `Tellma.Core.Tests` resolves every
`customMetrics` name, every `customDimensions[...]` key and every quoted value the queries compare
against to an instrument, a tag, a log event name, a log scope key or a tag value the machinery can
emit, and requires every instrument but those above to back at least one query. Renaming an
instrument, a tag, a tag value or a log event therefore fails the build instead of leaving an alert
that reports zero forever.

The metric queries are written against Application Insights' `customMetrics` table, where the
OpenTelemetry instruments land through the Azure Monitor exporter that `UseAzureWebDefaults` adds
when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. The heartbeat query reads the `traces` table,
where the log records land with their event name and their log scope (the tenant id among it) as
custom dimensions. Thresholds are starting points, not settled operational policy; they become
`Microsoft.Insights/scheduledQueryRules` resources in the per-distribution Bicep when the alerts
are deployed.
