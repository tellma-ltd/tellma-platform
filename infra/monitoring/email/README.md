# Email alert queries

One `.kql` file per alert the email pipeline's instruments are designed to back. They are the
operational half of the observability design: the instruments exist so that these questions are
answerable, and checking the queries in is what proves each one actually is.

| File | Alerts on |
|---|---|
| `send-failure-rate.kql` | Transient failures plus rejections over everything handled, per transport. |
| `webhook-auth-failures.kql` | Any sustained unauthorized rate — a key rotation gone wrong, or a forger. |
| `silent-webhook.kql` | Live-channel mail going out on a webhook-configured transport while no delivery event arrives. Invisible otherwise, because the sends still succeed. |
| `bounce-spam-ratio.kql` | Bounces and provider-side drops over deliveries, and spam reports separately, against the providers' reputation thresholds. |
| `unknown-owner-keys.kql` | Any delivery event naming an owner key nothing handles — a composition bug. |
| `delivery-event-lag-p95.kql` | Provider callback delay, or a receiver falling behind. |

**These are checked against the code, not just checked in.** A test in `Tellma.Core.Email.Tests`
extracts every `customMetrics` name, every `customDimensions[...]` key, and every quoted value the
queries compare against, and asserts each resolves to something the pipeline can actually emit —
a declared telemetry constant, or a tag value the diagnostics layer produces. Renaming an instrument,
a tag, or a tag value therefore turns a stale alert into a failing build, instead of into an alert
that quietly reports zero forever.

They are written against Application Insights' `customMetrics` table, which is where OpenTelemetry
instruments land through the Azure Monitor exporter. Thresholds are starting points chosen to be
expressible, not settled operational policy; they will become
`Microsoft.Insights/scheduledQueryRules` resources in the per-distribution Bicep when the alerts are
deployed.
