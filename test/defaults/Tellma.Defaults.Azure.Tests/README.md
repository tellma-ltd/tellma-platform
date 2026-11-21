# Tellma.Defaults.Azure.Tests

Unit tests of the two Azure defaults bundles, `Tellma.Defaults.Azure` and
`Tellma.Defaults.Azure.AspNetCore`: what each registers, what each leaves unregistered until its
configuration is set, and that a second call changes nothing. Each bundle is composed into a plain
service collection through `AddTellma`, so no host starts and no Azure resource is reached. One
test builds a web host that composes the web bundle, to pin the platform's trace sampler over the
Azure Monitor exporter's; the exporter it builds keeps no offline storage, sends no live metrics,
and reports to an `.invalid` ingestion endpoint only. `AzureRealtimeTests` builds web hosts, never
started, over the realtime line: the service's options (the deployment's application name, the
token's lifetime and claims) and the hosting check's verdicts on another application name, on a
deployment id that cannot name an application (reported by the check, never thrown by the options)
and on a connection string the web defaults did not compose. `AzureSignalRCompositionTests` starts
the shared web test host (`test/shared/Tellma.Testing.WebHost`) with the line composed and its
server connections aimed at an `.invalid` host: it starts without a job worker, the front door
refuses a stranger (its problem code and one refused connection counted) and the anonymous before
the service's SDK could mint a token, and a member's negotiate passes the door to the SDK, which
answers that no server connection is online. Its host, `AzureSignalRTestHost`, is linked from the
live suite, `Tellma.Defaults.Azure.AspNetCore.IntegrationTests`.

The endpoint audit over a host that composes the web bundle is the Acme distribution's web suite's
(`distributions/acme/test/Tellma.Distro.Acme.Web.Tests`).

```bash
dotnet test test/defaults/Tellma.Defaults.Azure.Tests
```
