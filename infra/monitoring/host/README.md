# Host alert queries

One `.kql` file per alert the distribution host's instruments are designed to back: the tenancy
runtime's (meter `Tellma.Core`) and the web host's sessions and authentication (meter
`Tellma.Core.AspNetCore`).

| File | Alerts on |
|---|---|
| `catalog-refresh-failures.kql` | An instance whose every refresh of the tenant snapshot failed in the window. |
| `catalog-snapshot-age.kql` | An instance whose snapshot was not confirmed against the catalog for five minutes, so tenant state changes are not reaching it. |
| `catalog-unavailable-requests.kql` | Any tenant request refused because the instance's snapshot went past its staleness bound. |
| `membership-write-failures.kql` | Failed membership-hint writes: any from the reconciliation, or above 5% from the other writers. |
| `session-refresh-failures.kql` | Refreshes deferred because the identity authority is unreachable, and a rising share of refreshes it rejects. |
| `backchannel-logout-rejections.kql` | Any back-channel logout token the web host refused, which leaves an ended session alive. |
| `csrf-rejections.kql` | A burst of CSRF refusals on one rule, the tighter bound on the rules the host's own client would break. |

Four instruments back no alert, by design: `tellma.tenancy.scopes`,
`tellma.tenancy.catalog.tenants` and `tellma.session.operations` size the workload, and
`tellma.auth.stepup_challenges` counts a normal part of signing in to a sensitive operation.

**These are checked against the code.** A test in `Tellma.Core.AspNetCore.Tests` resolves every
`customMetrics` name, every `customDimensions[...]` key and every quoted value the queries compare
against to an instrument, a tag or a tag value the host can emit, and requires every instrument but
the four above to back at least one query. Renaming an instrument, a tag or a tag value therefore
fails the build instead of leaving an alert that reports zero forever. The email pipeline's queries
under `../email/` are checked the same way, by the same shared check.

They are written against Application Insights' `customMetrics` table, where the OpenTelemetry
instruments land through the Azure Monitor exporter that `UseAzureWebDefaults` adds when
`APPLICATIONINSIGHTS_CONNECTION_STRING` is set. Thresholds are starting points, not settled
operational policy; they become `Microsoft.Insights/scheduledQueryRules` resources in the
per-distribution Bicep when the alerts are deployed.
