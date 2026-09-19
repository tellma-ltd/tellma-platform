# Research: Distribution host and multi-tenancy (theme `host-tenancy`, future spec 0010)

Verification date for every finding: **2026-09-01** unless a finding says otherwise. "Verified" means read
in the cited source on that date. "Inference" means a conclusion drawn by the researcher from verified
facts; designers should treat inferences as proposals, not facts.

Repo facts that frame the findings (read from the repo, 2026-09-01): `global.json` pins SDK 10.0.300;
`Directory.Packages.props` pins EF Core 10.0.9, `Microsoft.Data.SqlClient` 6.1.1, `Azure.Identity` 1.21.0,
`Azure.Core` 1.61.0, `Azure.Security.KeyVault.Secrets` 4.11.0, OpenIddict 7.5.0, and ASP.NET Core 10.0.9
packages. Spec 0003 (`docs/specs/0003-identity-server.md`) already fixes on the identity-server side: BFF
cookie sessions with `SameSite=Lax` and the SPA holding no tokens (§7.1), `sid` in tokens binding
back-channel logout (§6.1, §7.3), a back-channel emitter that POSTs a signed `logout_token` to each
distribution's `backchannel_logout_uri` which "validates the token's signature and `sid` and kills its
session" (§7.3), security-stamp invalidation plus short cookie lifetimes on the server (§7.3), and an
`ITicketStore`-backed SSO cookie deferred (§17). PAR is used for the authorization request (§9.1).

## Version table (exact latest stable, verified on NuGet / GitHub / vendor docs, 2026-09-01)

| Package or product | Latest stable | Released | Notes |
|---|---|---|---|
| ASP.NET Core / .NET | 10.0.x (repo on 10.0.9 packages) | GA Nov 2025 | Release notes read for 9.0 and 10.0 |
| Finbuckle.MultiTenant (+ .EntityFrameworkCore) | 10.1.3 | 2026-08-12 | net10.0 only; EF Core package requires `Microsoft.EntityFrameworkCore.Relational >= 10.0.11`; Apache-2.0 |
| Microsoft.Data.SqlClient | 7.0.2 | 2026-06-25 | 7.0.0 (2026-03-17) moved Entra auth to `Microsoft.Data.SqlClient.Extensions.Azure`; 6.1.x line: 6.1.1 = 2025-08-14, latest 6.1.6 = 2026-06-24 |
| Microsoft.Data.SqlClient.Extensions.Azure | 7.0.2 | 2026-06-25 | Depends on Azure.Identity >= 1.18.0, Azure.Core >= 1.51.1 |
| Azure.Extensions.AspNetCore.Configuration.Secrets | 1.5.2 | 2026-08-18 | net8.0 / net10.0 / netstandard2.0; needs Azure.Core >= 1.61.0, Azure.Security.KeyVault.Secrets >= 4.11.0, Microsoft.Extensions.Configuration >= 10.0.10 |
| Duende.BFF | 4.2.0 | 2026-06-10 | net10.0; commercial, source-available; Community Edition only under USD 1M revenue and not for customer-facing deployments |
| Duende.AccessTokenManagement.OpenIdConnect | 4.2.0 | 2026-03-18 | net8.0/9.0/10.0; Apache-2.0 |
| RFC 10017 / BCP 212 "OAuth 2.0 for Browser-Based Applications" | published | August 2026 | Normative BFF cookie and CSRF requirements (see §1.5) |

---

## 1. ASP.NET Core 10 OpenID Connect handler and BFF

### 1.1 What shipped in .NET 9 for the OIDC handler (verified)

- **PAR is built in and on by default.** `OpenIdConnectHandler` sends a Pushed Authorization Request
  when the provider's discovery document advertises the PAR endpoint. Control is
  `OpenIdConnectOptions.PushedAuthorizationBehavior` with values `UseIfAvailable` (default), `Disable`,
  and `Require`; a new `OpenIdConnectEvents.OnPushAuthorization` event lets the app customise or take
  over the pushed request. (Contributed by Duende's Joe DeCock; API proposal dotnet/aspnetcore#51686.)
- **`AdditionalAuthorizationParameters`** on the OAuth and OIDC options adds parameters to the
  authorization request without overriding `OnRedirectToIdentityProvider`.
- The OIDC configuration doc's recommended RP setup is: confidential client, `ResponseType = code`,
  PKCE, `SaveTokens = true`, `GetClaimsFromUserInfoEndpoint = true`, `MapInboundClaims = false`,
  `offline_access` only if a refresh token is wanted, and optionally
  `PushedAuthorizationBehavior = Require` when the provider supports PAR. The doc states "Public OpenID
  Connect/OAuth clients are no longer recommended for web applications" and recommends BFF.

Sources: https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-9.0 (section
"OpenIdConnectHandler adds support for Pushed Authorization Requests (PAR)");
https://github.com/dotnet/aspnetcore/issues/51686;
https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0
(ms.date 2026-01-22).

Implication: the distribution's `AddOpenIdConnect` should set `PushedAuthorizationBehavior.Require`
(spec 0003 §9.1 carries required assurance and allowed methods inside PAR, so a downgrade to a plain
front-channel request must fail closed).

### 1.2 What shipped in .NET 10 for authentication, cookies and Minimal APIs (verified)

The .NET 10 release notes contain **no OIDC handler changes, no BFF package or template, no cookie
token refresh, and no antiforgery changes**. What did ship:

- **Authentication and authorization metrics** on the `Microsoft.AspNetCore.Authorization` meter
  (challenge, forbid, sign-in/out counts, authenticated request duration, requests requiring
  authorization) and **ASP.NET Core Identity metrics**.
- **"Avoid cookie login redirects for known API endpoints"**: the cookie handler returns 401/403 instead
  of redirecting to `LoginPath` when the endpoint carries `IApiEndpointMetadata`, which is applied
  automatically to `[ApiController]` controllers, Minimal API endpoints registered with
  `MapGet`/`MapPost`/etc., endpoints that request JSON, and SignalR hubs.
- **Minimal API validation**: `builder.Services.AddValidation()` validates DataAnnotations on
  parameters, records and `IValidatableObject`; per-endpoint `.DisableValidation()`; failures are
  400 problem details customisable through `IProblemDetailsService`; the APIs live in the
  `Microsoft.Extensions.Validation` package/namespace.
- Passkeys in ASP.NET Core Identity (server side; not relevant to the RP).

Sources: https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0;
https://learn.microsoft.com/en-us/aspnet/core/security/authentication/api-endpoint-auth?view=aspnetcore-10.0.

Implication: a cookie-authenticated Minimal API surface under `/api/web/{tenantId}` gets correct 401/403
semantics for free in .NET 10; the SPA must treat 401 as "re-enter the OIDC challenge" (a top-level
navigation to a login endpoint), never as a fetch-redirect. The validation feature can serve the "payload
limits" requirement (T6) without hand-written checks.

### 1.3 Token refresh in the cookie handler (verified)

- `SaveTokens = true` stores the access, refresh and id tokens inside the cookie's
  `AuthenticationProperties`; **it does not refresh anything**. The Blazor Web App OIDC sample
  (ms.date 2025-12-18) implements refresh with a custom `CookieOidcRefresher` hooked on
  `CookieAuthenticationEvents.OnValidatePrincipal` that redeems the refresh token before the access
  token expires and calls `ReplacePrincipal` / `ShouldRenew`.
- Built-in refresh was **announced for .NET 10 and then postponed**: docs issue
  dotnet/AspNetCore.Docs#36160 (opened 2025-09-26) removes the sentence "this functionality is planned
  for .NET 10", citing dotnet/aspnetcore#8175 ("OAuth 2 refresh token support"), whose milestone is
  now ".NET 12 Planning".
- The maintained third-party alternative is `Duende.AccessTokenManagement.OpenIdConnect` 4.2.0
  (2026-03-18, Apache-2.0, net8/9/10): refreshes the user's tokens stored in the cookie and injects
  them into `HttpClient`.

Sources: https://learn.microsoft.com/en-us/aspnet/core/blazor/security/blazor-web-app-with-oidc?view=aspnetcore-10.0;
https://github.com/dotnet/AspNetCore.Docs/issues/36160; https://github.com/dotnet/aspnetcore/issues/8175;
https://www.nuget.org/packages/Duende.AccessTokenManagement.OpenIdConnect/.

Implication: the distribution does not call downstream APIs with the user's access token (spec 0003:
the SPA holds no tokens and the distribution is the resource), so it may not need refresh at all; if it
keeps tokens (for example to call the identity server's management APIs on behalf of the user), use
`OnValidatePrincipal` refresh or the Duende package rather than waiting for the framework.

### 1.4 Is there a first-party BFF package or template? (verified absence)

No. As of 2026-09-01 Microsoft ships no `Microsoft.AspNetCore.*Bff*` package and no BFF project
template. The official guidance is the pattern description in the OIDC doc plus Blazor samples that
compose the BFF from the cookie handler, the OIDC handler and YARP (`BlazorWebAppEntraBff`). The only
packaged BFF is `Duende.BFF` 4.2.0 (2026-06-10, net10.0): commercial, source-available; the Community
Edition requires "less than $1M USD projected annual gross revenue" and "access to less than $3M USD in
capital facilities", and "If you're building identity infrastructure for an end customer, that customer
needs a paid license".

Sources: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0
(section "Backend for frontend (BFF) security architecture");
https://learn.microsoft.com/en-us/aspnet/core/blazor/security/additional-scenarios?view=aspnetcore-10.0;
https://www.nuget.org/packages/Duende.BFF/; https://duendesoftware.com/products/communityedition.

Implication: the distribution's BFF is a few dozen lines over the built-in handlers (cookie + OIDC +
login/logout/user endpoints); a Duende dependency would be a paid license for every customer-facing
distribution, so the platform should own this code.

### 1.5 The normative BFF and CSRF baseline: RFC 10017 (verified)

RFC 10017, "OAuth 2.0 for Browser-Based Applications", Best Current Practice (BCP 212), August 2026,
describes the BFF architecture: tokens exist only at the BFF, "there are no tokens available to extract
from the browser". Cookie requirements: "The BFF MUST enable the Secure flag for its cookies", "The BFF
MUST enable the HttpOnly flag for its cookies", "The BFF SHOULD enable the SameSite=Strict flag for its
cookies". CSRF: "the BFF SHOULD require that the browser-based application includes a custom request
header" (forcing a CORS preflight that rejects foreign origins); anti-forgery tokens are an alternative.

Source: https://datatracker.ietf.org/doc/rfc10017/.

Implication: the distribution's posture should be stated against this RFC; see §1.7 for the SameSite
trade-off.

### 1.6 Antiforgery for a cookie-authenticated JSON API in .NET 10 (verified)

- The .NET 10 antiforgery doc: the middleware's verdict is enforced only for endpoints that read form
  data (`[FromForm]`, Razor Pages, MVC views, Blazor SSR). "A JSON API endpoint that binds its body from
  JSON, or a handler that ignores the request body, isn't rejected automatically on a cross-origin
  request. The verdict is still recorded on `IAntiforgeryValidationFeature` for code that wants to
  inspect it, but nothing enforces it. CSRF is a form-and-cookie attack vector, so endpoints that don't
  consume a browser-submitted form generally don't need this rejection."
- The SPA pattern the doc documents is cookie-to-header: `AddAntiforgery(o => o.HeaderName =
  "X-XSRF-TOKEN")`, a `GET antiforgery/token` endpoint that calls `IAntiforgery.GetAndStoreTokens` and
  writes a non-HttpOnly `XSRF-TOKEN` cookie, and the SPA echoing it in the header. "When the antiforgery
  token is provided in both the request header and in the form payload, only the token in the header is
  validated." `DisableAntiforgery()` opts an endpoint out (webhooks, bearer-token endpoints).
- OWASP CSRF cheat sheet: a required custom header works because "All modern browsers designate
  requests with custom headers as 'to be preflighted'"; the caveat is CORS misconfiguration (a
  wildcard or a compromised subdomain); SameSite "is useful as a defense-in-depth control but it does
  not replace a proper CSRF defense in most deployments" because "Lax only blocks unsafe methods" and it
  "is scoped to the registrable domain, not the origin"; Origin/Referer verification is recommended
  because those headers "cannot be altered programmatically"; naive double-submit is insecure, signed
  double-submit or the header-only variant is acceptable.

Sources: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0;
https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html.

Implication (inference from the above): an all-POST JSON surface that (a) only accepts
`Content-Type: application/json` bodies, (b) requires a fixed custom header on every request (a
constant such as `X-Requested-With: XMLHttpRequest` or a platform header the SPA already sends for
culture/time zone), (c) allows no cross-origin CORS, and (d) verifies `Origin`/`Sec-Fetch-Site` against
the distribution's host, needs neither the antiforgery middleware nor a per-session token, and the .NET
10 doc explicitly permits leaving JSON endpoints outside the middleware. A per-session token
(`GetAndStoreTokens`) is the belt-and-braces option if a form-posting endpoint ever appears (file upload
via multipart is the likely one, T7).

### 1.7 SameSite facts for the session cookie (verified)

- Framework defaults: `CookieAuthenticationOptions.Cookie` = `Lax`; `AntiforgeryOptions.Cookie` =
  `Strict`; `RemoteAuthenticationOptions.CorrelationCookie` and `OpenIdConnectOptions.NonceCookie` =
  `None` because "Some forms of authentication like OpenID Connect ... default to POST based redirects.
  The POST based redirects trigger the SameSite browser protections, so SameSite is disabled for these
  components."
- `CookiePolicyOptions.MinimumSameSitePolicy` defaults to `Lax`; the cookie doc says setting `Strict`
  "breaks OAuth2 and other cross-origin authentication schemes".
- Spec 0003 §7.1 already states the distribution session cookie is `SameSite=Lax`.

Sources: https://learn.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-10.0;
https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0.

Implication (inference): RFC 10017's SHOULD for `Strict` conflicts with the framework's guidance and
spec 0003's `Lax`; `Lax` plus the custom-header requirement satisfies the RFC's CSRF requirement
(the header is the MUST-equivalent control, `Strict` is a SHOULD) while keeping the post-login redirect
chain and deep links working. Record this as a deliberate departure from the SHOULD.

---

## 2. Session revocation for cookie authentication

### 2.1 What the cookie handler offers (verified)

- "Once a cookie is created, the cookie is the single source of identity." Revocation is implemented in
  `CookieAuthenticationEvents.ValidatePrincipal` (register a scoped `EventsType`), which can
  `RejectPrincipal()` and sign out, or `ReplacePrincipal` + `ShouldRenew = true` to refresh claims
  without a sign-out. The doc's pattern keys a `LastChanged` claim against a database value and warns:
  "The approach described here is triggered on every request. Validating authentication cookies for all
  users on every request can result in a large performance penalty for the app."
- `ITicketStore` (`CookieAuthenticationOptions.SessionStore`) "provides an abstract storage mechanic to
  preserve identity information on the server while only sending a simple identifier key to the client";
  members in .NET 10: `StoreAsync(ticket[, HttpContext], ct)`, `RenewAsync(key, ticket[, HttpContext],
  ct)`, `RetrieveAsync(key[, HttpContext], ct)`, `RemoveAsync(key[, HttpContext], ct)`. With a store,
  removing the server-side entry revokes the session immediately and the cookie stays small.
- ASP.NET Core Identity's `SecurityStampValidatorOptions.ValidationInterval` "Defaults to 30 minutes";
  this is the identity server's mechanism (spec 0003 §7.3), not available to a plain OIDC relying party
  because the RP has no user store with a stamp.
- .NET 10 adds nothing here (verified absence in the release notes).

Sources: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0
(sections "React to back-end changes", "Persistent cookies");
https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.cookies.iticketstore?view=aspnetcore-10.0;
https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.securitystampvalidatoroptions.validationinterval?view=aspnetcore-10.0.

### 2.2 The reference design for server-side BFF sessions (verified)

Duende BFF server-side sessions: the cookie carries only a session id; tokens "are never placed in the
cookie"; sessions are keyed so that a back-channel logout by `sid`/`sub` can revoke them "outside the
context of a browser interaction"; an EF-backed store with a periodic `SessionCleanupInterval` removes
abandoned sessions.

Source: https://docs.duendesoftware.com/bff/fundamentals/session/server-side-sessions/.

### 2.3 Implication for the design (inference)

Spec 0003 obliges every distribution to honour back-channel logout by `sid`, which a stateless cookie
cannot do by itself: either (a) an `ITicketStore` in a distribution-owned table keyed by the ticket key
with `sid` and `sub` columns (immediate revocation, one store read per request that is cacheable in
memory per instance, cleanup as a T10 consumer), or (b) a stateless cookie plus a `ValidatePrincipal`
check throttled to every N minutes that consults a per-user "session version" and a small revoked-`sid`
list held until the cookie's absolute expiry. Two separate events must not be conflated: identity-server
logout (kills the distribution cookie, all tenants) and tenant-level deactivation (the user may remain
active in another tenant of the same distribution), which is an authorization outcome of the per-tenant
connect step (T4) rather than a cookie revocation. Keep the cookie's absolute lifetime short enough that
the revoked-`sid` list is bounded, and keep the throttle window equal to the accepted revocation latency.

---

## 3. Multi-tenancy in .NET in 2026

### 3.1 Finbuckle.MultiTenant state (verified)

- Latest: **10.1.3, 2026-08-12** for `Finbuckle.MultiTenant`, `.AspNetCore` and `.EntityFrameworkCore`;
  maintenance lines 9.4.12 and 8.1.17 released the same day. License Apache-2.0. "Beginning with
  MultiTenant v10, major version releases align with .NET major version releases."
- **10.0.0 (2025-11-20)**: targets net10.0 only (net8/net9 targets removed); `TenantInfo` became a record
  and `ITenantInfo` was removed; `IMultiTenantContext` implementations are immutable; abstractions split
  into `Finbuckle.MultiTenant.Abstractions`, per-tenant options into `Finbuckle.MultiTenant.Options`,
  Identity into `Finbuckle.MultiTenant.Identity.EntityFrameworkCore`. 10.1.0 (2026-05-25) added
  conditional middleware bypass.
- The EF Core package depends on `Microsoft.EntityFrameworkCore.Relational >= 10.0.11`, above the
  repo's pinned 10.0.9.
- Strategies: Static, Delegate, HttpContext, BasePath, Claim, Session, Route (route parameter, default
  name `__tenant__`, configurable, with link-generation ambient value), Host, Header,
  RemoteAuthenticationCallback. Stores: InMemory, Configuration (read-only), EFCore
  (`EFCoreStoreDbContext`), HttpRemote (read-only), DistributedCache (no `GetAllAsync`), Echo; stores
  are checked "in the order registered until a matching tenant is resolved"; the store interface is
  Add/Update/Remove/GetByIdentifier/Get/GetAll.
- EF Core integration targets the shared-database model (shadow `TenantId` + global query filter via
  `[MultiTenant]` or `IsMultiTenant()`, `TenantMismatchMode`, `TenantNotSetMode`). Database-per-tenant
  is done by adding a `ConnectionString` property to the tenant record and calling `UseSqlServer` in
  `OnConfiguring` from `IMultiTenantContextAccessor`; the docs say nothing about `DbContext` pooling,
  and `OnConfiguring` is not called for pooled contexts (see §3.2), so the documented Finbuckle
  per-tenant-database recipe is incompatible with pooling as written.

Sources: https://github.com/Finbuckle/Finbuckle.MultiTenant/releases;
https://github.com/Finbuckle/Finbuckle.MultiTenant/blob/main/CHANGELOG.md;
https://www.nuget.org/packages/Finbuckle.MultiTenant.EntityFrameworkCore/;
https://www.finbuckle.com/MultiTenant/Docs/v10.1.3/Introduction;
https://www.finbuckle.com/MultiTenant/Docs/v10.1.3/Strategies;
https://www.finbuckle.com/MultiTenant/Docs/v10.1.3/Stores;
https://www.finbuckle.com/MultiTenant/Docs/v10.1.3/EFCore.

Implication (inference): Tellma's tenant model (one DB per tenant, Live/Sandbox category, suspension
state, membership lookup, catalog-in-live-DB vs dedicated catalog, secrets policy) is exactly the part
Finbuckle does not provide, while what it does provide (route-parameter resolution and a store
abstraction) is under two hundred lines to own. Depending on it would couple the platform's major
version cadence to Finbuckle's, force EF Core 10.0.11+ now, and put a third-party type (`TenantInfo`
record) into `Tellma.Core.Abstractions`, which must stay framework-free. Roll our own; the store/strategy
split and the "stores checked in order" idea are worth copying for config-vs-catalog resolution.

### 3.2 Per-tenant `DbContext` connections with EF Core 10 (verified)

- EF docs, multi-tenancy: database-per-tenant "is as simple as providing the correct connection
  string"; the sample resolves the string in `OnConfiguring` from an injected tenant service, and the
  factory lifetime must be `Scoped` (or `Transient` when the tenant can change within a scope) because
  "the options are cached at the Scoped level".
- EF docs, pooling: "the context's `OnConfiguring` is only invoked once - when the instance context is
  first created - and so cannot be used to set state which needs to vary (e.g. a tenant ID)"; the
  documented pattern is a singleton `AddPooledDbContextFactory<T>` wrapped by a scoped
  `IDbContextFactory<T>` that stamps tenant state on each checked-out instance. `AddDbContextPool`
  remarks: "when using pooling, the context configuration cannot change between uses, and scoped
  services injected into the context will only be resolved once from the initial scope"; default
  `poolSize` 1024. Pooling "generally does not reset state in the underlying database driver ... if you
  manually open and use a `DbConnection` ... it's up to you to restore that state before returning the
  context instance to the pool."
- `RelationalDatabaseFacadeExtensions.SetConnectionString(DatabaseFacade, string?)` exists in EF Core
  10 ("It may not be possible to change the connection string if existing connection, if any, is
  open"); `SetDbConnection`/`GetDbConnection` are siblings.
- SqlClient pools connections "per process, per application domain, per connection string" with exact
  string matching; `Max Pool Size` default 100; idle connections are removed after roughly 4-8 minutes
  when `Min Pool Size` is 0 (default), and the pool itself is then destroyed; the docs name "pool
  fragmentation due to many databases" as a known web-app problem (its suggested `USE` workaround is
  unavailable on Azure SQL, see §5.3).

Sources: https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy;
https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics;
https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.entityframeworkservicecollectionextensions.adddbcontextpool?view=efcore-10.0;
https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.relationaldatabasefacadeextensions.setconnectionstring?view=efcore-10.0;
https://learn.microsoft.com/en-us/sql/connect/ado-net/sql-server-connection-pooling?view=sql-server-ver17.

Implication (inference): two pooling-compatible shapes exist. (a) One `PooledDbContextFactory<T>` per
tenant, built with that tenant's options and cached in the tenant registry alongside the resolved
connection string; the EF model is shared because the model cache key is the context type, so the cost
per tenant is one options object and a lazily filled pool. (b) One pooled factory plus
`Database.SetConnectionString` on checkout and a reset on return; the driver-state caveat applies. Since
the CRUD stack executes most SQL through `SqlConnection`/`SqlCommand` batches (Queryex + TVP save), the
`DbContext` is mostly the model host, and the real per-tenant resource is the SqlClient pool: a
distribution with N active tenants holds up to N pools of up to 100 connections per app instance, so the
registry should set a smaller `Max Pool Size` per tenant string and rely on the 4-8 minute idle drain.

---

## 4. Secrets

### 4.1 Azure App Service Key Vault references (verified)

- Syntax `@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/<name>[/<version>])` or
  `@Microsoft.KeyVault(VaultName=<vault>;SecretName=<name>;SecretVersion=<v>)`, usable in app settings
  and connection strings; no code change.
- Identity: the app's system-assigned identity by default; `keyVaultReferenceIdentity` selects a
  user-assigned identity (needed when references must resolve at creation time); RBAC role "Key Vault
  Secrets User" or access policy `Get` on secrets.
- Rotation: version-less references pick up a new version "within 24 hours" because "App Service caches
  the values of the Key Vault references and refetches them every 24 hours"; any app-settings change
  restarts the app and refetches; a management API call
  `.../config/configreferences/appsettings/refresh?api-version=2022-03-01` forces resolution.
- Failure mode: "If a reference isn't resolved properly, the reference string is used instead", so the
  app sees the literal `@Microsoft.KeyVault(...)` text.

Source: https://learn.microsoft.com/en-us/azure/app-service/app-service-key-vault-references (ms.date 2026-04-09).

Implication: connection-string secrets read through references are static for the process lifetime and
up to 24 hours stale after rotation; a per-tenant secret per reference is impractical for hundreds of
tenants (one app setting each, redeploy to add). Startup should reject any configuration value that
still starts with `@Microsoft.KeyVault(`.

### 4.2 `Azure.Extensions.AspNetCore.Configuration.Secrets` (verified)

Version 1.5.2 (2026-08-18); `AddAzureKeyVault(new Uri(vault), new DefaultAzureCredential())` on the
configuration builder; `AzureKeyVaultConfigurationOptions.ReloadInterval` re-reads secrets on a timer;
`KeyVaultSecretManager` maps secret names to configuration keys (prefix filtering, `--` to `:`).
Dependencies match the repo's pins (`Azure.Core >= 1.61.0`, `Azure.Security.KeyVault.Secrets >= 4.11.0`).
Key Vault limits: no cap on the number of secrets; 4,000 secret GET-class transactions per 10 seconds
per vault per region (300 per 10 s for CREATE secret), subscription-wide limit five times that.

Sources: https://www.nuget.org/packages/Azure.Extensions.AspNetCore.Configuration.Secrets/;
https://learn.microsoft.com/en-us/azure/key-vault/general/service-limits.

Implication: the configuration provider loads the whole vault (or a prefix) at startup and on
`ReloadInterval`; hundreds of tenant secrets would fit the limits but each cold start would issue
hundreds of GETs per instance, another reason not to hold a secret per tenant.

### 4.3 Azure SQL with managed identity: no stored passwords (verified)

- `Authentication=Active Directory Default` (SqlClient >= 3.0) runs the `DefaultAzureCredential` chain
  (Environment, WorkloadIdentity, ManagedIdentity, SharedTokenCache, Visual Studio, VS Code, Azure
  PowerShell, Azure CLI, Azure Developer CLI; interactive browser disabled). The SqlClient doc warns it
  "can come with performance impacts because it has to look in multiple places" and "isn't recommended
  for environments that have strict service level response times".
- `Authentication=Active Directory Managed Identity` (alias `Active Directory MSI`, >= 2.1) targets the
  managed identity directly; user-assigned identity via `User Id=<client id>` (>= 3.0).
  `Active Directory Workload Identity` (>= 5.2) for federated identities.
  `SqlConnection.AccessTokenCallback` (>= 5.2) lets the app supply tokens; the callback delegate is part
  of the pool key and "allows the access token to be refreshed within a connection pool".
  `Active Directory Password` is deprecated.
- **SqlClient 7.0.0 (2026-03-17) removed Azure dependencies from the core package**; every Entra mode
  now requires `Microsoft.Data.SqlClient.Extensions.Azure` (7.0.2, Azure.Identity >= 1.18.0); "No code
  changes are required beyond adding the package reference." The repo's 6.1.1 still bundles them.
- Per-database grant, run by the server's Entra admin inside each database:
  `CREATE USER [<identity-name>] FROM EXTERNAL PROVIDER; ALTER ROLE db_datareader ADD MEMBER ...;
  ALTER ROLE db_datawriter ADD MEMBER ...; ALTER ROLE db_ddladmin ADD MEMBER ...;` (contained user, no
  server login). Connection string `Server=tcp:<server>.database.windows.net;Authentication=Active
  Directory Default;Database=<db>;` works both locally (VS/CLI credential) and in App Service; "The
  DefaultAzureCredential class caches the token in memory and retrieves it from Microsoft Entra ID
  before expiration"; permission changes are not seen until the cached token expires.
- The logical server can be switched to **Microsoft Entra-only authentication**, which disables SQL
  authentication for every login and database on the server (an Entra admin must be set first;
  unsupported with Elastic jobs, SQL Data Sync, some CDC and replication scenarios).
- "Microsoft Entra ID and managed identities aren't supported for on-premises SQL Server."

Sources: https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/azure-active-directory-authentication?view=sql-server-ver17 (ms.date 2026-03-17);
https://learn.microsoft.com/en-us/azure/app-service/tutorial-connect-msi-sql-database;
https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-only-authentication?view=azuresql;
https://www.nuget.org/packages/Microsoft.Data.SqlClient/; https://www.nuget.org/packages/Microsoft.Data.SqlClient.Extensions.Azure/;
https://github.com/dotnet/SqlClient/blob/main/release-notes/6.1/README.md.

Implication: in SaaS every tenant database is reachable with the distribution's single managed identity
and a connection string that contains no secret at all; tenant provisioning must create the contained
user in each new database (this needs an Entra admin credential at provisioning time, which is a seam
for the provisioning job, not the web app). Production strings should say `Active Directory Managed
Identity` (or `AccessTokenCallback` with one shared credential object) for latency; `Active Directory
Default` is for developer machines. Plan the SqlClient 7.x bump: the platform must add
`Microsoft.Data.SqlClient.Extensions.Azure` when EF Core's provider moves to 7.x, or Entra modes fail at
runtime with an "install the package" error.

### 4.4 How a catalog of hundreds of tenant databases stores its connection information (verified)

- The Azure SQL SaaS patterns doc: for database-per-tenant "a catalog must be deployed that maps tenant
  identifiers to database uniform resource identifiers"; the Elastic Database client library's shard map
  is that catalog, storing `Key -> Shard Location` where a location is server name + database name,
  with a "Global Shard Map" database in schema `__ShardManagement` and a per-shard local copy.
- Credentials are **not** in the map: routing apps "instantiate a shard map manager object ... using
  credentials that have read-only access on the GSM database. Individual requests for later connections
  supply credentials necessary for connecting to the appropriate shard database"; administration uses a
  separate, higher-privileged credential.
- Elastic query in shard-map-manager mode reaches end of support on 2027-03-31 (the client library
  itself carries no end-of-support notice on its page, but the ecosystem around it is contracting).

Sources: https://learn.microsoft.com/en-us/azure/azure-sql/database/saas-tenancy-app-design-patterns?view=azuresql;
https://learn.microsoft.com/en-us/azure/azure-sql/database/elastic-scale-shard-map-management?view=azuresql;
https://learn.microsoft.com/en-us/azure/azure-sql/database/elastic-database-client-library?view=azuresql.

Implication (inference): the catalog row should hold only non-secret location data (server, database,
optional pool/region and state) and the distribution composes the connection string from one
configured credential template (managed identity in SaaS, one integrated or SQL login on-prem). The
brain dump's worry "if each db gets a password, where do we store these passwords?" dissolves: no tenant
database gets its own password. ARCHITECTURE.md's reference to reusing existing "sharding code" should
not mean the Elastic Database client library.

### 4.5 On-prem equivalents (verified)

- `System.Security.Cryptography.ProtectedData` (DPAPI) "is supported on the Windows platform only. Its
  use on .NET Core on platforms other than Windows throws a PlatformNotSupportedException."
- ASP.NET Core Data Protection key-at-rest options: Windows DPAPI (Windows only), DPAPI-NG (Windows
  8+/domain), X.509 certificate (`ProtectKeysWithCertificate`, cross-platform), Azure Key Vault; "If you
  specify an explicit key persistence location, the data protection system deregisters the default key
  encryption at rest mechanism. Consequently, keys are no longer encrypted at rest."
- Integrated authentication from Linux requires Kerberos (kinit or keytab, `MSSQLSvc` SPN, resolvable
  FQDN, trusted domain); a February 2026 SqlClient issue (#3954, RHEL 8, SqlClient 5.1.6 and 6.1.4)
  reports failures where the ODBC driver succeeds with identical tickets.
- EF docs: "Secrets should never be added to configuration files."

Sources: https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata?view=net-10.0;
https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest?view=aspnetcore-10.0;
https://github.com/dotnet/runtime/issues/26659; https://github.com/dotnet/SqlClient/issues/3954;
https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-strings.

Implication (inference): the on-prem secrets policy must be cross-platform (AGENTS.md rule), so it
cannot rest on DPAPI. The realistic shapes are (a) Windows host + `Integrated Security=true` with a
service account, (b) Linux/Windows host + one SQL login whose password arrives through an environment
variable or a root-owned file injected by the host (systemd credentials, Docker/Kubernetes secrets), or
(c) a Data-Protection-encrypted secrets file with the key ring protected by an X.509 certificate. In
all three the catalog still stores no passwords; per-tenant distinct passwords are neither needed nor
advisable.

---

## 5. Azure SQL elastic pool facts for a database-per-tenant model

### 5.1 Databases per pool (verified, docs ms.date 2026-03-09 and 2025-06-24)

| Purchasing model / tier | Max databases per pool |
|---|---|
| vCore General Purpose standard-series (Gen5) | 100 (2 vCores), 200 (4 vCores), **500** (6 vCores and above) |
| vCore General Purpose Fsv2 / DC-series | 500 (DC-series ramps 100-400-500) |
| vCore Business Critical | 50 (4 vCores), **100** (8 vCores and above) |
| vCore Hyperscale (standard and premium series) | **25** |
| DTU Basic and Standard | 100 (50 eDTU), 200 (100 eDTU), **500** (200 eDTU and above) |
| DTU Premium | 50 (125 eDTU), **100** (250 eDTU and above) |

Setting min vCores/DTUs per database above 0 "implicitly limits the number of databases that can be
added to the pool". The dense-pool doc: "it might not be feasible to increase the number of databases
in the pool up to the maximums documented" when many are active; it recommends a "quarantine" pool for
newly created tenant databases until their consumption is known, keeping servers at "1000-2000 databases
per server" although the hard limit is 5000 databases per logical server.

### 5.2 Connections, sessions, workers (verified)

- **Max concurrent sessions per pool: 30,000** in every tier and size.
- Max concurrent workers per pool scale with size: GP Gen5 210 (2 vCores), 420 (4), 630 (6), 840 (8),
  1050 (10), 1260 (12), ... 4200 (40), 8400 (80), 13,440 (128); Business Critical from 420 (4 vCores);
  DTU Standard 100 (50 eDTU) to 6000 (3000 eDTU). **Max concurrent logins per pool equals the worker
  figure** in every table. Each database inside a pool keeps the single-database worker limit of its
  max-vCore setting (for example 750 workers for a database capped at 10 vCores in a `GP_Gen5_10` pool
  whose pool-wide limit is 1050).
- "New requests are rejected when session or worker limits are reached" (error 10928); workers, not
  requests, are what the limit counts, and parallel plans consume several workers.

Sources: https://learn.microsoft.com/en-us/azure/azure-sql/database/resource-limits-vcore-elastic-pools?view=azuresql;
https://learn.microsoft.com/en-us/azure/azure-sql/database/resource-limits-dtu-elastic-pools?view=azuresql;
https://learn.microsoft.com/en-us/azure/azure-sql/database/resource-limits-logical-server?view=azuresql;
https://learn.microsoft.com/en-us/azure/azure-sql/database/elastic-pool-resource-management?view=azuresql.

Implication: the binding limits for hundreds of tenants are logins-per-pool (a deploy that opens a
connection to every tenant at once on a 2-vCore pool exceeds 210 logins) and pooled-connection
inventory across app instances (N tenants x instances x `Max Pool Size` against 30,000 sessions), not
the database count. The registry's connection strings should set `Max Pool Size` well below 100, keep
`Min Pool Size` 0, and background schedulers (T10) should stagger per-tenant work.

### 5.3 Cross-database access (verified)

"Cross-database and cross-instance queries using three or four part names" are not supported in Azure
SQL Database ("Three part names referencing the `tempdb` database and the current database are
supported"); `USE` is not supported ("you must create a new connection to that database"); linked
servers, `OPENQUERY`/`OPENDATASOURCE`, cross-database ownership chaining and `TRUSTWORTHY` are
unsupported; Windows authentication is unsupported and contained database users are encouraged. The
only cross-database mechanism is **elastic query** (external tables): still in preview, "read-only
access to external tables", "only supported when connecting with SQL Server Authentication" (so not with
managed identity), slow first execution on lower tiers, and its shard-map mode ends support 2027-03-31.

Sources: https://learn.microsoft.com/en-us/azure/azure-sql/database/transact-sql-tsql-differences-sql-server?view=azuresql (ms.date 2026-04-02);
https://learn.microsoft.com/en-us/azure/azure-sql/database/elastic-query-overview?view=azuresql (ms.date 2026-08-11).

Implication: nothing in the platform may join catalog and tenant data in SQL; the "which tenants am I a
member of" answer for multi-live distributions must be a best-effort table the application maintains in
the catalog (as the brain dump proposes), and on-prem SQL Server must be treated the same way even
though it technically allows three-part names, so one code path serves both.

---

## 6. AsyncLocal ambient context vs scoped DI for tenant/user context in background work

### 6.1 Runtime semantics (verified)

- `AsyncLocal<T>` "Represents ambient data that is local to a given asynchronous control flow"; values
  flow with the `ExecutionContext` across awaits and thread hops; the constructor overload takes a
  value-changed callback fired on explicit sets and on context transitions. `ExecutionContext.SuppressFlow()`
  "Suppresses the flow of the execution context across asynchronous threads" for the current thread until
  `AsyncFlowControl.Undo()`; captured async locals therefore do not reach work started under
  suppression.
- `HttpContextAccessor` is implemented as a static `AsyncLocal<HttpContextHolder>`; the setter first
  clears the holder ("Clear current HttpContext trapped in the AsyncLocals, as its done") and uses "an
  object indirection to hold the HttpContext in the AsyncLocal, so it can be cleared in all
  ExecutionContexts when its cleared": any code that captured the accessor sees `null` after the request
  completes, by design.
- `Activity.Current` "Gets or sets the current operation (Activity) for the current thread. This flows
  across async calls" (AsyncLocal-backed): fire-and-forget work started from a request inherits the
  request's trace context unless it is reset.
- No .NET 10 change to `AsyncLocal<T>` or `ExecutionContext` was found in the API docs consulted
  (verified absence, not a proof).

Sources: https://learn.microsoft.com/en-us/dotnet/api/system.threading.asynclocal-1?view=net-10.0;
https://learn.microsoft.com/en-us/dotnet/api/system.threading.executioncontext.suppressflow?view=net-10.0;
https://github.com/dotnet/aspnetcore/blob/main/src/Http/Http/src/HttpContextAccessor.cs;
https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activity.current?view=net-10.0.

### 6.2 Current framework guidance (verified)

ASP.NET Core Best Practices (ms.date 2025-12-29) and David Fowler's ASP.NET Core guidance: do not store
`IHttpContextAccessor.HttpContext` in a field (it "frequently captures a null or incorrect HttpContext");
`HttpContext` is not thread-safe; do not capture `HttpContext` or request-scoped services in background
work ("The `ContosoDbContext` is scoped to the request, resulting in an `ObjectDisposedException`");
copy the needed values before starting the work and create a fresh scope with
`IServiceScopeFactory.CreateAsyncScope()` inside it; "Background tasks should be implemented as hosted
services"; long-running work belongs outside the HTTP request. EF's pooling remarks add that scoped
services injected into a pooled context "will only be resolved once from the initial scope".

Sources: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/best-practices?view=aspnetcore-10.0;
https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AspNetCoreGuidance.md;
https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md.

### 6.3 Implication for the request-context seam (inference)

Carry tenant, user, culture, calendar, time zone and sandbox flag as an explicit immutable value (a
record) held by a scoped holder service that middleware or the tenant endpoint filter populates once per
request, and that background infrastructure populates once per job scope from a copied value handed to
it at enqueue time. Do not make an `AsyncLocal` the source of truth: a value set inside a callee does
not flow back to the caller, fire-and-forget work silently inherits a stale request context unless flow
is suppressed, and singleton or pooled consumers that read it lazily observe the wrong tenant. An
`AsyncLocal` mirror is acceptable only as a diagnostics convenience (like `Activity.Current`) whose
leakage cannot change authorization or data routing. `ISandboxContext` should read the scoped holder so
one implementation serves request and job scopes, and the job runner should start each job under a new
`Activity` linked to (not parented by) the enqueuing request's trace when one exists.

---

## 7. Minimal API route groups with a `{tenantId}` prefix and endpoint filters in .NET 10

### 7.1 Route groups (verified)

- `MapGroup` prefixes may contain route parameters and constraints; groups nest; "the route handler
  mapped to the `user` group can capture the `{org}` and `{group}` route parameters defined in the
  outer group prefixes", so a handler under `app.MapGroup("/api/web/{tenantId:int}")` can declare
  `int tenantId` directly. Constraint syntax `{id:int}` and `{slug:regex(...)}` applies to prefixes.
- Conventions on a group (`RequireAuthorization`, `WithMetadata`, `AddEndpointFilter`,
  `AddEndpointFilterFactory`, `WithTags`) apply to every endpoint in it "before adding extra filters or
  metadata that might exist in an inner group or specific endpoint"; outer-group filters run before
  inner-group filters, which run before endpoint filters, regardless of registration order across
  groups; within one group or endpoint, registration order is the order.
- An empty prefix `MapGroup("")` is allowed to attach metadata or filters without changing routes.

Source: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers?view=aspnetcore-10.0 (ms.date 2026-04-28).

### 7.2 Endpoint filters (verified)

- `IEndpointFilter.InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)`;
  the context exposes `HttpContext` and `Arguments` ("the arguments passed to the handler in the order
  in which they appear in the declaration of the handler"), plus `GetArgument<T>(index)`; a filter may
  modify arguments or return a result to short-circuit. Before-`next` code runs first-in-first-out,
  after-`next` code first-in-last-out. Filter classes "can resolve dependencies from DI ... Although
  filters can resolve dependencies from DI, filters themselves can't be resolved from DI" (they are
  activated with constructor injection by `AddEndpointFilter<T>()`).
- `AddEndpointFilterFactory` receives `EndpointFilterFactoryContext.MethodInfo`, which allows a group
  filter to inspect the handler signature once and install a pass-through when a parameter type is
  absent (the doc's `TodoDb` example).
- Because `Arguments` holds already-bound handler arguments, filters necessarily execute after routing
  and parameter binding (inference from the documented context shape; the doc does not state the order
  in those words).
- `IApiEndpointMetadata` (§1.2) is applied to Minimal API endpoints automatically, so cookie challenges
  inside such a group yield 401/403.

Source: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/min-api-filters?view=aspnetcore-10.0 (ms.date 2026-04-28).

### 7.3 Implication for tenant routing (inference)

Resolution of the tenant must not wait for an endpoint filter if anything bound as a handler argument
(a `DbContext`, a service that needs the tenant) is constructed during binding: read `tenantId` from
`HttpContext.GetRouteValue("tenantId")` inside a lazily-evaluating scoped tenant holder, or resolve it in
a middleware placed after `UseRouting` and before `UseAuthorization`. A single group
`/api/web/{tenantId:int}` with `.RequireAuthorization()` and one `AddEndpointFilter<TenantFilter>()`
(membership, suspension and sandbox state checks, connect step) is enough for every projected endpoint,
and the same group can carry `AddValidation()`-driven payload limits. Custom distribution endpoints join
the group with `group.MapPost(...)`, inheriting the tenant filter without any per-endpoint code, which
answers the "hard to leave unsecured" question for the web surface.

---

## Open items not verified

- No primary source states, in words, that Minimal API filters run after parameter binding; treated as
  an inference from the documented `Arguments` list.
- `Microsoft.Data.SqlClient` token-cache behaviour per pool for `Active Directory Managed Identity`
  (beyond `DefaultAzureCredential` in-memory caching and `AccessTokenCallback` pool-keying) was not
  measured; the docs' latency warning applies to `Active Directory Default` only.
- Whether the Finbuckle EF Core store or route strategy is used anywhere in production platforms of
  comparable shape was not surveyed; the recommendation to roll our own rests on the verified feature
  set and dependency facts, not on adoption data.
- Duende BFF's exact price per tier was not on the product page consulted; only the tier front-end
  counts and the Community Edition thresholds are verified.
