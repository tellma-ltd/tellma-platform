# Tellma.Core.AspNetCore.Tests

Tests for `Tellma.Core.AspNetCore`, the web host. No database, no network: each host is a real
`WebApplication` on a test server, composed with the real `AddTellma`, `UseTellma` and `MapTellma`,
over an in-memory tenant catalog, a fake clock, a scripted user connection, a static identity
server (a fixed issuer and signing key, so no discovery document is fetched) and probe endpoints on
every surface that answer the request context they ran under.

| Folder | Covers |
|---|---|
| `Tenancy/` | The tenant verdict matrix: every tenant state against reads, writes and writes a read-only tenant allows, for an anonymous caller, a member and a stranger, on the web surface, the hub's front door, a blob read and a public API read and write (a service account's token included); the principal, session, client and assurance a session binds; a stale catalog answering `503 catalog-unavailable` with `Retry-After`; the guard reaching an endpoint from a data source that ignores endpoint filters, and running once on a route handler that both the filter and the backstop wrap. |
| `Csrf/` | Every CSRF rule on a cookie-authenticated write (`Sec-Fetch-Site`, `Origin` and `Referer`, `Tellma-Client`, the JSON media type and the binary opt-in); the provenance rules alone on safe methods and the hub's negotiate; the tenantless session fallback; bearer and anonymous endpoints exempt; the refusal counter by rule. |
| `Authentication/` | Scheme isolation (a token never authenticates a cookie surface, a cookie never a bearer surface, the control plane its own scope); the public API's scope and audience; MCP's per-tenant audience and client name; a token of another type refused; how a bearer token's claims name a user, a service account or nobody; the step-up challenge's header, problem and counter, and a service account refused an endpoint with an assurance bar. |
| `Routing/` | The endpoint audit: the platform's endpoints and the probes pass it, and each violation a distribution could map fails startup naming its method and route, at `MapTellma` or when the host starts (a tenant endpoint off the groups, a surface stamped on another surface's group, an MCP endpoint its front door did not install, a delegate the guard does not wrap, an endpoint that does not state whether it mutates, a stack's reserved resource segment); a hub endpoint mapped without methods stamped a read; `WithMutation` clearing a read-only allowance; `MapTellma` mapping every route and group, refusing a pipeline `UseTellma` did not install, and mapping once. |
| `Surfaces/` | The distribution info document (shape, cache header, links, the admin surface on and off); the protected-resource metadata, alike for every tenant id; liveness; the admin surface over an in-memory catalog (info and tenant counts, the version prefix, listing, registration with and without a provisioning trigger, malformed bodies, state changes and the states it refuses, the catalog's refusals, members of an unknown tenant, no CSRF headers needed); the SPA over a generated publish manifest (hashed and unhashed caching, the index page's security headers and policy, the navigation and segment rules, `route-not-found` against `not-found`, a host without the SPA, the policy's realtime and identity origins). |
| `Hosting/` | The persisted key ring outside Development, the framework's forwarded-headers switch refused, readiness following the catalog and naming no check. |
| `Observability/` | A browser's `traceparent` adopted without its sampling decision, `Tellma-Trace-Id` and `Tellma-Build` on every answer including early refusals, database spans stripped of query text, and each log event reaching every logger provider once with none of the host's defaults left. |
| `Infrastructure/` | The test host, the probe endpoints, the in-memory catalog write side and the problem reader. |

## Running

```bash
dotnet test test/core/Tellma.Core.AspNetCore.Tests
```
