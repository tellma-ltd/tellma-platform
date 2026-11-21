# Tellma.Defaults.Azure.AspNetCore

The Azure web host's half of the defaults: one call from a distribution web host's `composeWeb`
delegate, for what only the web host composes on Azure.

```csharp
builder.AddTellma(AcmeComposition.Slug, AcmeComposition.Compose, tellma => tellma.UseAzureWebDefaults());
```

`UseAzureWebDefaults()` composes:

| Concern | What it registers | Configured by |
|---|---|---|
| Webhooks | The webhook fronting (`AddTellmaWebhooks()`), and its endpoint `GET`/`POST /api/webhooks/{key}`, which `MapTellma` maps among the platform's own tenantless routes. The receivers come from the email transports `UseAzureDefaults()` composes. | `Webhooks`, and each transport's webhook section |
| Data Protection key ring | The ring in one Azure blob, every key wrapped by a Key Vault key, both reached with the host's `TokenCredential`. Registered only when both locations are set. | `Tellma:DataProtection:BlobUri`, `Tellma:DataProtection:KeyId` |
| Azure Monitor | The Azure Monitor exporter for logs, traces and metrics, behind the host's own OpenTelemetry instrumentation (whose SQL spans carry no query text). Registered only when the connection string is set. | `APPLICATIONINSIGHTS_CONNECTION_STRING` |
| Realtime hub | The hub on Azure SignalR Service (`Microsoft.Azure.SignalR`, pinned): every instance's connections reached through the service, under the deployment's application name (`acme`, `acme_staging`), so deployments sharing an instance never hear each other. The service's token is minted at the negotiate, after the tenant middleware's front door, lives five minutes, and carries the subject and the three hub claims alone; nothing else of the session's principal reaches the service. Registered only when the connection string is set; without it the hub runs in-process. | `Azure:SignalR:ConnectionString` |
| Credential | A `DefaultAzureCredential` as the host's `TokenCredential`, unless the host registered another (a workload or user-assigned identity). | — |

## Rules

- **Web host only.** The bundle references `Tellma.Core.AspNetCore`; call it from `composeWeb`, never
  from the composition method the migrator shares. Its shared half is `Tellma.Defaults.Azure`.
- **Validated at startup.** The host's options check refuses a key ring configured by halves (a blob
  without its key, or a key without its blob), a location that is not an absolute `https` URL, and a
  `KeyRingPath` beside a `BlobUri`. Outside Development a host with no persistent key ring fails the
  `host.data-protection` check, and `/health/ready` verifies the ring loads.
- **The platform's own routes.** The webhook endpoint is anonymous and tenantless, and each receiver
  authenticates its own callbacks. It is stamped as a platform route through an internal seam of
  `Tellma.Core.AspNetCore`, so no distribution can stamp an endpoint of its own that way.
- **The deployment's application name.** The host's `host.realtime-hosting` check fails a service
  application name other than the deployment's, and a connection string the hub does not run on.
- **Idempotent.** A second call changes nothing.

## Tests

`test/defaults/Tellma.Defaults.Azure.Tests` pins the registrations and exercises the hub's line
over the full web host offline; the Acme distribution's web suite pins the endpoint audit over a
host that composes the bundle. `test/defaults/Tellma.Defaults.Azure.AspNetCore.IntegrationTests`
runs the hub's line nightly against a test Azure SignalR Service instance (`AzureSignalRLiveTests`).
