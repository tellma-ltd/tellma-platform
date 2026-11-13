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

- the host's options (`Tellma`, `Tellma:Identity`, `Tellma:Session`, `Tellma:Tenancy`,
  `Tellma:DataProtection`, `Tellma:ForwardedHeaders`, `Tellma:Admin`, `Tellma:Support`,
  `Tellma:ClientTelemetry`, `Tellma:Links`, `Tellma:Api`), validated by the startup gate's options
  check;
- the `Tellma.Session` cookie scheme, the `Tellma.OpenIdConnect` sign-in scheme and the `Tellma.Bearer` JWT
  scheme, and the policies `Tellma.Web`, `Tellma.Api`, `Tellma.Mcp` and `Tellma.ControlPlane`. No
  scheme is the default: each policy names its own, and the fallback policy requires an
  authenticated session. The default policy names no scheme, so the endpoint audit refuses any
  distribution endpoint under it;
- the session store, the token refresh, the session sweep and the membership reconciliation (see
  [Sessions](#sessions));
- the `host.identity` check: the distribution's client secret, and in-proc outside Development the
  service client's secret and an `IEmailSender`;
- with `Tellma:Identity:Mode = InProc`, the identity engine (`Tellma.Identity`, which the web host
  references), configured from the `TellmaIdentity` section overlaid with what the distribution
  dictates: the `/id` path base, the issuer, the catalog as its store, and the distribution's own
  client in its seed. The engine's mail (sign-in codes, invitations, password resets) belongs to no
  tenant: it goes through the distribution's email pipeline under the sender the engine resolves by
  its key, and while that sender sends outside a tenant the sandbox flag answers "never sandboxed",
  as a standalone identity host's does, so the router passes it to the live channel untouched. Every
  other send outside a tenant still fails;
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
7. the in-proc identity engine's middleware, in in-proc mode;
8. authentication, then authorization;
9. the CSRF control;
10. the tenant middleware.

The web API's compression, rate limiting, timeouts, output caching and contract middleware take
their places in this order when those features are composed. A distribution's own middleware goes between `UseTellma` and `MapTellma`.

## `MapTellma`

Maps the tenantless platform routes and returns the tenant route groups:

| Route | Access |
|---|---|
| `GET /api/distribution-info` | anonymous; the information document |
| `GET /health/live`, `GET /health/ready` | anonymous |
| `/api/admin/v1/...` | `Tellma.ControlPlane`; when `Tellma:Admin:Enabled` (default: standalone identity) |
| `GET /.well-known/oauth-protected-resource/{tenantId}/mcp` | anonymous; RFC 9728 metadata |
| `GET /bff/login` | anonymous; the sign-in challenge |
| `POST /bff/logout` | `Tellma.Web`; the sign-out, `global` or `local` |
| `GET /bff/user` | `Tellma.Web`; the user and the tenants the membership hints list |
| `POST /bff/backchannel-logout` | anonymous; the identity server's logout tokens |
| `/id/...` | the engine's own, in in-proc mode |
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
the platform or the SPA owns under `/{tenantId}`, and, in the realised gate, for any endpoint whose
`RequireSecurable(resource, action)` names a pair no feature registers in the securable registry
(compared ignoring case), or names it only through a renamed resource's alias; `MapTellma`'s own
run leaves the pairs to the gate. When the securable registry did not compose, the gate's audit
skips the pairs and says so in one line, leaving the cause to `core.securables`.

- a distribution endpoint under a policy that names no authentication scheme.

## Sessions

The session cookie carries a reference to a row of `catalog.Sessions`, never the ticket: a random
key, protected by Data Protection, whose SHA-256 prefix is the session's handle. The handle is what
logs, the profile cookie and the session-termination listeners see; the key reaches none of them.
Each instance caches the rows it reads for `Tellma:Session:CacheTtl`. The session slides by
`IdleLifetime` up to `AbsoluteLifetime` from the sign-in; the persistent cookie and the readable
`tellma.profile` cookie expire with the row. Background requests (`Tellma-Activity: background`,
the hub, client telemetry) renew neither.

A sign-in pushes its request (PAR) with PKCE as the distribution's confidential client. Its
`returnUrl` must be a local path of visible ASCII characters (anything else arrives
percent-encoded). The distribution's `IAuthenticationPolicyProvider` bounds what the login's query
asks for: the stronger of the policy's and the caller's `acr_values` is sent (ranked by the identity
server's tiers, `aal1` < `aal2`/`phr` < `aal3`/`phrh`, a list as its weakest member; when either
cannot be ranked, the caller's wins only when it is `Tellma:Session:StepUp:Acr`), the shorter
`max_age` wins, and the allowed methods are the policy's alone.

An access token within `RefreshSkew` of its expiry is refreshed once per session per instance; the
new ticket is written with a compare-and-set on the row's `TokensVersion`, and an instance that
loses it adopts the winner's. A refused refresh ends the session. The identity server rotates the
refresh token on each redemption and accepts a spent one again only within its reuse leeway (thirty
seconds by default), so the refresh keeps every retry of a token that may be spent inside it:

| Timing | Value | What it bounds |
| --- | --- | --- |
| Redemption deadline | 5 s | One redemption, metadata read included (a cold read the framework does not cancel too). |
| Retries, and the pause before each | 2, 1 s | A redemption that may have spent the token (the deadline after sending, a cut-off or malformed answer), and a failed write of redeemed tokens. |
| Retry window | 12 s | The latest a retry is sent after the first attempt: (5 s + 1 s) x 2, under half the leeway. |
| A request's wait | 5 s | How long a request waits on its session's refresh before it is served on its current tokens. |
| Backoff | 20 s | A refresh put off: the token never reached the identity server, or the retries are spent. |

Tokens the identity server issued are never dropped: an identity token signed by a key its metadata
still does not list after one re-read is stored with the session's current claims
(`SessionRefreshClaimsKept`), and tokens the catalog cannot store are kept on the instance and
stored by the session's next refresh there. An identity server that cannot be reached keeps the
session for `MaxAuthorityOutage` past the access token's expiry. A sign-in that returns to the root
lands on the user's one live tenant when there is exactly one. The identity server's return from a
global sign-out (`/signout-callback-oidc`) always lands on `/signed-out`. A global sign-out whose
identity server cannot be reached, or names no end-session endpoint, signs out locally and logs
`BffGlobalSignOutUnavailable`.

`POST /bff/backchannel-logout` validates the logout token (issuer, audience, `logout+jwt`, the
back-channel event, no nonce, a recent `iat`) and deletes the sessions of its `sid`, or of its
`sub` when it names no session. A sign-out, a back-channel logout, a refused refresh and a sign-in
over an existing session tell every `ISessionTerminationListener` the ended handles.

`SessionSweepService` deletes rows expired for a day, every `SweepInterval`.
`MembershipReconcileService` rebuilds every active tenant's membership hints from its users five
minutes after the registry's first snapshot and then every `Tellma:Tenancy:MembershipReconcileInterval`.
The catalog's application lock keeps two instances' runs from overlapping, not from both running:
each instance's timer runs on its own, so each may run once per interval. A run is idempotent, so a
second one over unchanged users changes no row.

## CSRF

Every cookie-authenticated request runs through one control after authorization. A request that
may change data must come from the distribution's own origin (`Sec-Fetch-Site`, `Origin` or
`Referer`), carry `Tellma-Client: <name>/<version>` with a configured name, and send JSON unless the
endpoint accepts binary bodies. Safe methods and the hub's negotiate answer to the origin rules
alone. Bearer and anonymous endpoints are exempt, and so is the in-proc identity engine, whose
forms carry its own antiforgery token.

## Tests

`test/core/Tellma.Core.AspNetCore.Tests` hosts the real pipeline on a test server over a fake tenant
catalog, in-memory session rows and a fake identity server's back channel, and the in-proc mode over
the real engine. The session statements, the compare-and-set and the application lock run against
SQL Server in `test/core/Tellma.Core.IntegrationTests`.
