# Blob alert queries

One `.kql` file per alert the blob machinery's telemetry is designed to back: the sweep, reconcile
and attach instruments on the runtime's meter (`Tellma.Core`).

| File | Alerts on |
|---|---|
| `sweep-failures.kql` | Any sweep failure: a blob whose objects the store could not delete, a swept row still referenced, or a failed purge of the store's incomplete artefacts. |
| `reconcile-released.kql` | Any committed blob the weekly reconcile released because no owner row referenced it: a path that bypassed the save pipeline. |
| `attach-rejected-rate.kql` | More than 20 refused attaches of one reason on one kind in the window. |

The other instruments back no alert, by design: `tellma.blobs.uploads`, `tellma.blobs.upload.bytes`,
`tellma.blobs.upload.duration`, `tellma.blobs.downloads`, `tellma.blobs.image.duration`,
`tellma.blobs.image.failures` and `tellma.blobs.sweep.deleted` size the workload and its latency,
and the store's instruments (meter `Tellma.Blobs`, `tellma.blobs.store.operations` and
`tellma.blobs.store.duration`) describe the storage the host was given.

**These are checked against the code.** A test in `Tellma.Core.Tests` resolves every
`customMetrics` name, every `customDimensions[...]` key and every quoted value the queries compare
against to an instrument, a tag, a log event name or a tag value the blob machinery can emit, and
requires exactly the three instruments above to back a query. Renaming an instrument, a tag or a
tag value therefore fails the build instead of leaving an alert that reports zero forever.

The queries are written against Application Insights' `customMetrics` table, where the
OpenTelemetry instruments land through the Azure Monitor exporter that `UseAzureWebDefaults` adds
when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. Thresholds are starting points, not settled
operational policy; they become `Microsoft.Insights/scheduledQueryRules` resources in the
per-distribution Bicep when the alerts are deployed.
