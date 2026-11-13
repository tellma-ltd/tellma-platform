# Tellma.Defaults.Azure.Tests

Unit tests of the two Azure defaults bundles, `Tellma.Defaults.Azure` and
`Tellma.Defaults.Azure.AspNetCore`: what each registers, what each leaves unregistered until its
configuration is set, and that a second call changes nothing. Each bundle is composed into a plain
service collection through `AddTellma`, so no host starts and no Azure resource is reached. One
test builds a web host that composes the web bundle, to pin the platform's trace sampler over the
Azure Monitor exporter's; the exporter it builds keeps no offline storage, sends no live metrics,
and reports to an `.invalid` ingestion endpoint only.

The endpoint audit over a host that composes the web bundle is the Acme distribution's web suite's
(`distributions/acme/test/Tellma.Distro.Acme.Web.Tests`).

```bash
dotnet test test/defaults/Tellma.Defaults.Azure.Tests
```
