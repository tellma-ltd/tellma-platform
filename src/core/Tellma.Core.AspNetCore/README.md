# Tellma.Core.AspNetCore

The ASP.NET Core adapter of Tellma's runtime. A distribution's web host composes the platform with
three calls, and gets a fixed request pipeline, the platform's routes and the tenant route groups its
own endpoints go on.

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddTellma("acme", tellma => tellma.AddFeature<GlFeature>());

WebApplication app = builder.Build();
app.UseTellma();
TellmaEndpoints endpoints = app.MapTellma();
endpoints.Web.MapGet("/reports/{id:int}", RunReport).RequireSecurable("acme.Report", "Read");
app.Run();
```

## `AddTellma`

Runs the distribution's `compose` and then its optional `composeWeb` on one `TellmaBuilder`, so both
pass the composition's gates in one realisation. `composeWeb` is for web-only registrations; a
feature or entity added there would diverge the web host's model from the migrator's. It then adds:

- the host's options (`Tellma`, `Tellma:Identity`, `Tellma:Session`, `Tellma:DataProtection`,
  `Tellma:ForwardedHeaders`, `Tellma:Admin`, `Tellma:Support`, `Tellma:ClientTelemetry`,
  `Tellma:Links`, `Tellma:Api`), validated by the startup gate's options check;
- the `Tellma.Session` cookie scheme and the `Tellma.Bearer` JWT scheme, and the policies
  `Tellma.Web`, `Tellma.Api`, `Tellma.Mcp` and `Tellma.ControlPlane`. No scheme is the default:
  each policy names its own, and the fallback policy requires an authenticated session;
- Data Protection under the application name `tellma-{slug}`, persisted to
  `Tellma:DataProtection:KeyRingPath` when set; outside Development a host with no persistent key
  repository fails `host.data-protection`;
- forwarded headers from the configured proxies alone;
- the endpoint audit (`host.endpoint-audit`) and the health checks;
- Serilog from the `Serilog` section with the version enrichers, as the host's own logger (the
  static `Log.Logger` is left alone), replacing the framework's default logger providers: each event
  goes to Serilog's sinks and to OpenTelemetry's logger provider, once;
- OpenTelemetry over the platform's meters and activity sources and the ASP.NET Core, HttpClient
  and SqlClient instrumentation. SQL spans carry no batch text. No exporter is added here.

## `UseTellma`

Installs the pipeline in this order and nothing else:

1. the header stamp (`Tellma-Trace-Id`, `Tellma-Build`) on every response;
2. forwarded headers, when enabled;
3. Serilog request logging, whose completion event of a tenant request carries `TenantId`,
   `TenantCategory`, `Subject` and `UserId`;
4. the exception handler, answering RFC 9457 problems;
5. HSTS and HTTPS redirection, outside Development;
6. routing;
7. authentication, then authorization;
8. the CSRF control;
9. the tenant middleware.

The web API's compression, rate limiting, timeouts, output caching and contract middleware, and the
in-proc identity engine's middleware, take their places in this order when those features are
composed. A distribution's own middleware goes between `UseTellma` and `MapTellma`.

## `MapTellma`

Maps the tenantless platform routes and returns the tenant route groups:

| Route | Access |
|---|---|
| `GET /api/distribution-info` | anonymous; the information document |
| `GET /health/live`, `GET /health/ready` | anonymous |
| `/api/admin/v1/...` | `Tellma.ControlPlane`; when `Tellma:Admin:Enabled` (default: standalone identity) |
| `GET /.well-known/oauth-protected-resource/{tenantId}/mcp` | anonymous; RFC 9728 metadata |
| the SPA's files and the navigation fallback | anonymous |

| Group | Route | Policy | Guard |
|---|---|---|---|
| `Web` | `/{tenantId}/api/web` | `Tellma.Web` | the access filter |
| `Api` | `/{tenantId}/api/v{version}` | `Tellma.Api` | the access filter |
| `Hub` | `/{tenantId}/hub` | `Tellma.Web` | the tenant middleware |
| `Blobs` | `/{tenantId}/blobs` | `Tellma.Web` | the access filter |
| `Tenantless` | `/api` | each endpoint's own | none; each calls `AsTenantlessEndpoint` |

An endpoint on a tenant group carries exactly one of `RequireSecurable(resource, action)` and
`AllowMember()`. Every `Web`, `Api` and `Blobs` endpoint states whether it changes data with
`WithMutation(bool)`, which also clears any read-only allowance; nothing is inferred from its HTTP
methods. The `Hub` group carries SignalR hubs alone (`MapHub`), whose connections are always
reads.

The endpoint audit fails startup, naming the route and method, for any endpoint a tenant could be
reached through without the platform's controls:

- a tenant endpoint whose surface is not its group's, or that sits outside the tenant groups
  (only MCP's, stamped by MCP's own front door, may);
- a `Web`, `Api` or `Blobs` endpoint whose request delegate is not the one the access guard wraps;
- a `Web`, `Api` or `Blobs` endpoint that does not state `WithMutation`;
- a `Hub` endpoint that is not a SignalR hub's, or that states a mutation;
- an endpoint that short-circuits routing (`ShortCircuit()`) without `AllowAnonymous()`, since
  authentication, authorization, CSRF and the tenant middleware would never run for it;
- a missing or doubled grant, another policy than the group's, anonymous access, CORS on a cookie
  surface, or a cookie mutation answering `GET`.

It also fails startup for a distribution route, or a stack's resource segment, that takes a word
the platform or the SPA owns under `/{tenantId}`.

## CSRF

Every cookie-authenticated request runs through one control after authorization. A request that
may change data must come from the distribution's own origin (`Sec-Fetch-Site`, `Origin` or
`Referer`), carry `Tellma-Client: <name>/<version>` with a configured name, and send JSON unless the
endpoint accepts binary bodies. Safe methods and the hub's negotiate answer to the origin rules
alone. Bearer and anonymous endpoints are exempt.

## Tests

`test/core/Tellma.Core.AspNetCore.Tests` hosts the real pipeline on a test server over a fake tenant
catalog.
