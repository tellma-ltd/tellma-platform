# f01: Distribution host, BFF and identity amendments (digest of 0011 and 0010)

## 1. Sources read

- `docs/specs/0011-distribution-host-and-multitenancy.md`: 2131 lines, read in full.
- `docs/specs/0010-identity-server-amendments.md`: 351 lines, read in full.
- Context: `.tmp/round-2/README.md` (92 lines), `.tmp/round-2-brief.md` (462 lines).
- Spot-checked to confirm cross-references only (not digested): 0016 §3.10, §6.2 and `TellmaHeaders`;
  0013 §10.4 (strings pack); 0003 §7.3, §7.4, §11.2; 0002 §7 (font caching). Facts taken from those
  are marked "(x-ref)".

Both specs carry the status line "Ready for implementation. Frozen once merged", but per the round-2
README, 0010–0022 are unimplemented and may change in place. 0010 is renamed this round to
`0010-amendments-to-implemented-specs.md`, and its current text becomes Part A.

## 2. Key facts

### 2.1 Packages that matter to the front end (0011 §1.1, §1.3)

- `Tellma.Core.AspNetCore` (new) owns everything with an ASP.NET dependency: `AddTellma(WebApplicationBuilder)`,
  `UseTellma`, `MapTellma`, the BFF and session store, CSRF, the tenant middleware and access filter,
  the endpoint audit, route groups, `/api/distribution-info`, health, the admin surface,
  `SessionSweepService`, `MembershipReconcileService`, Data Protection and forwarded headers. It
  references `Serilog.AspNetCore` and `OpenTelemetry.*`.
- `Tellma.Core.Abstractions` owns `.Tenancy` and `.Composition` (every enum and record a module or
  distribution names), plus `TenancyTelemetryNames` and `HostTelemetryNames`.
- `Tellma.Defaults.Azure.AspNetCore` (new) references `Microsoft.Azure.SignalR`.
  `UseAzureWebDefaults()` is called from the web host's `composeWeb` delegate. It selects the Azure
  SignalR backplane when `Azure:SignalR:ConnectionString` is present (spec 0021 §6.6).
- Package pin: `Microsoft.AspNetCore.SpaProxy` at the repository's ASP.NET line (§1.1).
- Web `Program.cs` is exactly three platform calls (§1.3, and a test pins it, §10):
  `builder.AddTellma(AcmeComposition.Slug, AcmeComposition.Compose, tellma => tellma.UseAzureWebDefaults());`
  then `app.UseTellma(); app.MapTellma();`.
- Shared composition (§1.3): `tellma.UseAzureDefaults().AddFeature<GlFeature>().Languages(["en", "ar"])`.
  The distribution's offered languages come from `TellmaBuilder.Languages`/`AddLanguage` (spec 0013).
  0011 says nothing about calendars beyond the `CalendarContributionItem`/`Calendar<T>()` items (§2.1),
  which spec 0013 realises.
- Feature contribution item `ClientEventContributionItem(string Name)`, with sugar `ClientEvent(name)`,
  is realised by spec 0021 (§2.1). It is the only "client" item in the feature contract: there is no
  item for navigation entries, launcher pages, SPA routes or client bundles.

### 2.2 The reference distribution and the Client project (0011 §1.2, §6.6)

- Layout: `distributions/acme/src/` holds the following:
  - `Tellma.Distro.Acme.Web/` holds `Program.cs`, `Endpoints/`, the tracked `Properties/launchSettings.json`,
    the appsettings files, and `wwwroot/` (the published SPA: build output, untracked). Its csproj
    sets `SpaRoot`, `SpaProxyServerUrl` and `SpaProxyLaunchCommand`.
  - `Tellma.Distro.Acme/` is the composition class library: `AcmeComposition.cs`, `Entities/`,
    `Services/`. It never references an ASP.NET package or the Client esproj.
  - `Tellma.Distro.Acme.Client/` holds `src/` (the Angular app, "arrives with the UI phase"),
    `proxy.conf.js`, `aspnetcore-https.js`, `angular.json`, `package.json`, `tsconfig*.json` and
    `Tellma.Distro.Acme.Client.esproj` (`Microsoft.VisualStudio.JavaScript.Sdk`).
  - `Tellma.Distro.Acme.Migrator/` references only the library and `Tellma.Core.Migrator`. It never
    builds the Client esproj and needs no Node.
  - Tests: `test/Tellma.Distro.Acme.Web.Tests`, `test/Tellma.Distro.Acme.IntegrationTests`, and
    `test/Tellma.Distro.Acme.E2E` (Playwright). The E2E project is scaffolded with no tests and runs
    empty on every PR until the UI specs ship (§10).
- The Web project references the Client esproj with `ReferenceOutputAssembly=false`, plus
  `Microsoft.AspNetCore.SpaProxy` and `Tellma.Identity` (in-proc mode).
- Client project recipe: a JavaScript-SDK esproj beside the Web project. It is "scaffolded by hand
  from a current Angular CLI workspace (vitest, pnpm) with the Visual Studio template as a guide
  only, because the template's recipe is out of date".
- Development flow:
  - `dotnet run` on the Web project starts the SPA proxy. The proxy launches `pnpm start` and
    redirects the browser to the Angular dev server, so the browser's origin is the dev server's.
  - `Tellma:PublicOrigin` in `appsettings.Development.json` is `https://localhost:4200`.
  - `aspnetcore-https.js` exports the ASP.NET Core development certificate for the dev server on
    first start.
  - The `__Host-` session cookie works behind the proxy because both origins are `https` on `localhost`.
- `proxy.conf.js` forwards these prefixes to the Web project's port: `/bff`, `/api`, `/id`, `/signin-oidc`,
  `/signout-callback-oidc`, `/health`, `/openapi`, `/.well-known`, `/{tenantId}/api`,
  `/{tenantId}/hub` (websockets on), `/{tenantId}/blobs` and `/{tenantId}/mcp`. "Every route it does
  not forward is an SPA deep link."
- Outside Development the SPA proxy is inert. `dotnet publish` builds the client and copies
  `dist/<app>/browser` into `wwwroot`, which `MapTellma` serves with the fallback of §5.1.
- Ports (§6.6): `7052` for the Web project and `4200` for the dev server, fixed in the tracked
  `launchSettings.json` "until the `dotnet tellma` CLI exists".
- Fresh-clone steps (§6.6):
  1. `pnpm install` in the Client project.
  2. `dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator -- migrate`.
  3. `dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Web`, which opens
     `https://localhost:4200/`.
  4. Sign in as `admin@localhost` with the email code printed to the console.
- Development seed (§5.7):
  - In-proc identity.
  - The development administrator `admin@localhost`, subject `00000000-0000-0000-0000-000000000001`.
  - `Tellma:Seed:Tenants`: tenant 1 "Acme" (Live) and tenant 2 "Acme Sandbox" (Sandbox of 1). The
    bootstrap step makes the dev subject administrator of both.
- Names (§1.2): slug `acme`; OIDC `client_id = acme`; service client `acme-svc`; smoke deployment
  `acme.app.tellma.com`. `taxonomy.json` lists `distributions: ["acme"]`. Slugs are lowercase,
  letter-first and at most 15 characters.

### 2.3 What the host serves for the SPA: pipeline, static files, headers (0011 §5.1, §5.2, §5.8)

- `UseTellma()` installs this fixed pipeline and nothing else:
  1. forwarded headers;
  2. Serilog request logging;
  3. the exception handler, producing RFC 9457 problem details (spec 0016's mapping);
  4. HSTS and HTTPS redirection, outside Development;
  5. response compression (0016 §8.4);
  6. `UseRouting`;
  7. `UseTellmaIdentity` (in-proc only);
  8. `UseAuthentication`, then `UseAuthorization`;
  9. the rate limiter (0016 §8.2), partitioned by principal;
  10. request timeouts (0016 §8.3);
  11. output caching (0016 §8.5);
  12. `TellmaCsrfMiddleware`;
  13. `TellmaContractMiddleware` (0016 §3.10);
  14. `TenantMiddleware`.

  Custom middleware goes between `UseTellma()` and `MapTellma()`.
- `MapTellma()` maps, among others, "static assets and the SPA fallback
  (`MapFallbackToFile("index.html")` when `wwwroot/index.html` exists, excluding every reserved
  prefix)", then runs the endpoint audit. The full mapped set is in §2.4 below.
- The route table (§3.1) lists "SPA deep links `/{tenantId:int:min(1)}/{**path}`: anonymous; serves
  the app shell".
- Fallback authorization policy (§5.2): `RequireAuthenticatedUser` under the `Tellma.Session` scheme.
  `AllowAnonymous` is permitted only on endpoints carrying `TenantlessEndpointMetadata`:
  - the information document;
  - the health probes;
  - the BFF login and back-channel receiver;
  - the protected-resource metadata document;
  - the strings pack;
  - the notification catalogue;
  - the webhook receivers;
  - the Development OpenAPI document;
  - the SPA fallback;
  - the identity engine's endpoints.

  Static asset endpoints are not named in this list.
- TLS (§5.8): HTTPS redirection and HSTS outside Development. The `__Host-` prefix makes plain HTTP
  unable to carry a session. `Tellma:PublicOrigin` must be absolute, `https` outside Development, and
  path-less (startup check `core.public-origin`). Redirect URIs, MCP resource ids, the back-channel
  URI and the CSRF origin check all derive from it.
- Hosting (§5.8): App Service health checks use `/health/ready`, `Always On` is required,
  `WEBSITES_CONTAINER_STOP_TIME_LIMIT = 30`, and the host `ShutdownTimeout` is 30 s.
- **Not specified anywhere in 0011:**
  - `Cache-Control` for `index.html`, hashed bundles, `ngsw.json`, or `manifest.webmanifest`;
  - any service worker or PWA rule;
  - CSP, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, `X-Content-Type-Options` on SPA
    responses.

  Only these headers are specified: `/api/distribution-info` sends `Cache-Control: public, max-age=60`;
  the PRM document sends `public, max-age=300`. Elsewhere (x-ref): the strings pack and the
  notification catalogue use `public, max-age=31536000, immutable` when `v` matches the deployment
  and `no-store` otherwise (0013 §10.4, 0016); blob GETs set CSP `frame-ancestors 'self'`, `nosniff`
  and `private, max-age=31536000, immutable` (0017); 0002 §7 states "Long-cache immutable
  (fingerprinted filenames, `Cache-Control: immutable, max-age=1y`) plus the PWA service-worker
  cache" for fonts; 0003's threat model cites "CSP; Trusted Types" for the BFF.

### 2.4 Route grammar (0011 §3.1, §5.1, §5.2, §5.3)

Tenant ids are immutable positive `int`s from `catalog.sq_Tenants`. They are never reused or renamed,
and there is no tenant slug (§3.1).

| Surface | Route | Scheme / policy |
|---|---|---|
| Web API (SPA) | `/{tenantId:int:min(1)}/api/web/...` (group `Web`) | session cookie; `Tellma.Web` |
| Public API | `/{tenantId:int:min(1)}/api/v{version:apiVersion}/...` (group `Api`) | bearer; `Tellma.Api` (seam; nothing mapped) |
| MCP | `/{tenantId:int:min(1)}/mcp` | bearer; `Tellma.Mcp` |
| SignalR hub | `/{tenantId:int:min(1)}/hub` (group `Hub`) | session cookie; `Tellma.Web` |
| Blobs | `POST /{tenantId}/blobs/{kind}`, `GET /{tenantId}/blobs/{kind}/{id}` (group `Blobs`) | session cookie; `Tellma.Web` |
| SPA deep links | `/{tenantId:int:min(1)}/{**path}` | anonymous; serves the app shell |

- Tenantless routes all begin with a non-numeric segment, so "the integer constraint alone keeps
  platform routes and tenant routes from shadowing each other" (§3.1).
- Tenantless routes mapped by `MapTellma` (§5.1):
  - `GET /api/distribution-info`, `GET /health/live`, `GET /health/ready`;
  - `/bff/*`;
  - `/api/admin/v1/*` (when enabled);
  - `/api/webhooks/{key}`;
  - `/api/strings/{language}` (0013 §10.4);
  - `/api/notification-catalogue` (0021 §2.5);
  - the in-proc identity endpoints under `/id`;
  - `/.well-known/oauth-protected-resource/{tenantId}/mcp`;
  - `/signin-oidc` and `/signout-callback-oidc` (OIDC handler paths);
  - `/openapi` (Development, 0016 §12.1).
- The `Tenantless` route group is `/api`. A distribution's tenantless endpoint must call
  `.AsTenantlessEndpoint(reason)` (§5.2).
- Reserved prefixes, which no distribution endpoint may use (§5.3): `/bff`, `/api/admin`,
  `/api/distribution-info`, `/health`, `/id`, `/openapi`, `/.well-known`, `/signin-oidc`,
  `/signout-callback-oidc`.
- `TellmaEndpoints` (returned by `MapTellma`) exposes `Web`, `Api`, `Hub`, `Blobs` and `Tenantless`
  as `RouteGroupBuilder`s. Endpoint conventions: `AsTenantlessEndpoint(reason)`,
  `WithMutation(bool)`, `RequireSecurable(resource, action)`, `AllowMember()`,
  `AcceptsBinary(maxBytes)` (§5.1).
- Metadata types: `enum TenantSurface { Web, Api, Mcp, Hub, Blobs }`,
  `TenantEndpointMetadata(Surface, IsMutation)`, `TenantlessEndpointMetadata(Reason)`,
  `AcceptsBinaryMetadata(MaxBytes)`.
- The endpoint audit fails startup on any of these (§5.3):
  - a tenant route without `TenantEndpointMetadata`;
  - a tenant route without the right policy, or carrying `IAllowAnonymous`;
  - a tenant route without exactly one of `SecurableEndpointMetadata` / `MemberEndpointMetadata`;
  - a cookie `GET` stamped `IsMutation = true`;
  - a CORS policy on any cookie endpoint;
  - an `IAllowAnonymous` endpoint without tenantless metadata;
  - an endpoint with neither a policy nor tenantless metadata;
  - a distribution endpoint under a reserved prefix.

### 2.5 Authentication constants, schemes, policies (0011 §5.1, §5.5)

- `TellmaAuthentication` constants:
  - `SessionScheme = "Tellma.Session"`, `OidcScheme = "Tellma.OpenIdConnect"`,
    `BearerScheme = "Tellma.Bearer"`;
  - `SessionCookieName = "__Host-tellma.session"`, `ProfileCookieName = "tellma.profile"`;
  - `CsrfHeaderName = "Tellma-Client"`.
- `TellmaPolicies`: `Web = "Tellma.Web"`, `Api = "Tellma.Api"`, `Mcp = "Tellma.Mcp"`,
  `ControlPlane = "Tellma.ControlPlane"`. Each policy names its scheme explicitly: a cookie never
  authenticates a bearer surface, and a bearer never authenticates the web surface.
- **Session cookie** (`Tellma.Session` scheme):
  - Name `__Host-tellma.session`, or `tellma.session` under plain HTTP in Development.
  - Flags: `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`.
  - Sliding `Tellma:Session:IdleLifetime`, default **7 days**, "equal to the authority's refresh-token
    idle window".
  - Absolute `Tellma:Session:AbsoluteLifetime`, default **90 days**.
  - The cookie holds only a 256-bit random key (43-char base64url). The ticket lives in
    `catalog.Sessions`, protected by Data Protection with purpose `Tellma.Core.AspNetCore.Session`.
  - `IApiEndpointMetadata` makes API endpoints answer `401`/`403` instead of redirecting.
- **OIDC handler** (`Tellma.OpenIdConnect`):
  - `Authority = Tellma:Identity:Authority` (in-proc mode: `{PublicOrigin}/id`); `ClientId = slug`.
  - Code flow with PKCE; `PushedAuthorizationBehavior = Require`; `SaveTokens = true`;
    `MapInboundClaims = false`; `GetClaimsFromUserInfoEndpoint = false`.
  - Scopes `openid profile email offline_access tellma_api`.
  - `CallbackPath = /signin-oidc`, `SignedOutCallbackPath = /signout-callback-oidc`.
  - Claims kept: `sub`, `sid`, `name`, `email`, `locale`, `acr`, `amr`, `auth_time`,
    `tellma_acr_auth_time`. A principal without `sub` or `sid` is rejected.
- **Bearer** (`Tellma.Bearer`): `ValidTypes = ["at+jwt"]`, `ClockSkew` 60 s against 10-minute access
  tokens, `NameClaimType = "sub"`, audience validated per route (MCP audience
  `{PublicOrigin}/{tenantId}/mcp`, §5.6).
- **Policy definitions:**
  - `Tellma.Web` = `Tellma.Session` + authenticated.
  - `Tellma.Api` = bearer + scope `tellma_api` + `aud = PublicOrigin`.
  - `Tellma.Mcp` = bearer + `tellma_api` + the tenant MCP audience.
  - `Tellma.ControlPlane` = bearer + `tellma_control_plane` + `aud = PublicOrigin`.
- **`PrincipalKind`** binding: cookie → `User`; bearer with `auth_time` → `User`; bearer without it →
  `ServiceAccount`. When present, the `tellma_kind` claim (0010 §7) wins.

### 2.6 BFF endpoints (0011 §5.5): every `/bff/*` route

All are tenantless and use the cookie scheme.

| Route | Auth | Request | Response / effect |
|---|---|---|---|
| `GET /bff/login?returnUrl=&acr_values=&max_age=` | anonymous | `returnUrl` must be local; `acr_values` and `max_age` optional (step-up) | Challenges the OIDC scheme, forwarding `acr_values`/`max_age` and the `tellma_allowed_methods` from `IAuthenticationPolicyProvider.GetLoginPolicyAsync(null)`. "Initial sign-in requests no tier because the user has no tenant yet." After the callback, redirects per §2.8 below. |
| `POST /bff/logout` | session | JSON `{ "scope": "local" \| "global" }`, default `global` | Local sign-out. For `global`, then RP-initiated end-session with `id_token_hint`. Clears `tellma.profile`. **No response shape is specified.** |
| `GET /bff/user` | session | none | `{ sub, name, email, locale, acr, tenants: [{ id, name, name2, name3, languages, category, state, liveTenantId, isActive }] }`. Each tenant is §3.3's `TenantDescriptor` plus `isActive`, read from §3.8. `401` when unauthenticated. |
| `POST /bff/backchannel-logout` | anonymous by design | `application/x-www-form-urlencoded`, `logout_token` | Server-to-server from the authority, not called by the SPA; see §2.9. `200` (including replays); `400` with no body on validation failure. |

- OIDC callback paths `/signin-oidc` and `/signout-callback-oidc` are handler paths, not `/bff/*`.
  The handler's `RemoteSignOutPath` (front-channel) "stays unused".
- CSRF: `/bff/user` is a cookie GET and so exempt from the `Tellma-Client` header (§5.4).
  `POST /bff/logout` is a cookie-authenticated POST, so CSRF rules 1–4 apply: it needs `Tellma-Client`
  and `Content-Type: application/json`. A plain HTML form post would be refused with `415`/`403`.
- `IAuthenticationPolicyProvider.GetLoginPolicyAsync(int? tenantId)` returns
  `LoginPolicy(AcrValues, MaxAge, AllowedMethods)` and defaults to none. `/bff/login` always passes
  `null`.

### 2.7 The `tellma.profile` cookie (0011 §5.5)

- Written **once**, on the OIDC callback redirect.
- Value `{ v, name, locale }`: the ID token's `name` and `locale` claims as of sign-in; `v` versions the
  shape. The encoding of the cookie value is not specified.
- `Path=/`, not `HttpOnly`, no token and nothing tenant-scoped. It has the session cookie's
  **absolute** lifetime (90 days by default), is "never rewritten", and is cleared by `/bff/logout`.
- Its stated purpose: "so the tenantless shell (the picker) renders without a request". "Its presence
  is the SPA's synchronous signed-in signal: without it the SPA goes to `/bff/login` before spending
  a request."
- Not in the cookie: `email`, `sub`, the tenant list, or any expiry or idle timestamp.

### 2.8 Post-sign-in redirect, the picker skip rule and returning visits (0011 §3.2, §5.5)

- After the callback, the BFF redirects:
  1. to `returnUrl` when one was given;
  2. otherwise, "when exactly one membership of §3.8 is `Live`, `Active` and `isActive`", straight into
     `/{tenantId}/` (trailing slash; no `/home`), skipping the picker;
  3. otherwise to `/`, "the picker".
- The skip rule "reads the membership list, not a policy" (§3.2). Single-live and multi-live
  distributions share one code path.
- Tenant data the BFF never reads: "company name, preferences, landing page, calendar". "The SPA
  takes it from `me` (spec 0014) and `settings/client` (spec 0013), caches both per tenant in local
  storage, and validates the cache by version tags on its first request (spec 0016); a returning
  visit reopens the last-used tenant from that cache and falls back to the picker on `403` or `404`."
- 0011 calls the page "the company picker" (§3.2, §3.3, §3.8) and also "the picker" (§5.5). The brief
  calls it "tenant picker".

### 2.9 Session liveness, refresh, revocation, step-up: how a sign-out propagates (0011 §4.5, §5.5, §7.4)

- **Per-instance cache:** `RetrieveAsync` uses a bounded `MemoryCache` (`Tellma:Session:CacheTtl`,
  default 60 s, 50,000 entries). "The TTL is the maximum time a revoked session keeps working on an
  instance other than the one that revoked it."
- **Refresh** (`TellmaSessionEvents.ValidatePrincipal`):
  - Runs when the access token expires within `RefreshSkew` (60 s).
  - Compare-and-set on `TokensVersion`; `ReplacePrincipal` replaces `acr`, evidence time and `locale`.
  - `invalid_grant` or any 4xx → `RejectPrincipal`, the row is deleted, and "the SPA receives `401`
    and re-enters the challenge".
  - A 5xx or network failure keeps the session for up to `MaxAuthorityOutage` (1 h).
- **Back-channel logout** (`POST /bff/backchannel-logout`):
  - Validates the token against the OIDC handler's configuration: `iss`; `aud = slug`;
    `typ = logout+jwt`; `iat` within ±5 min; `events` containing the back-channel event URI; no
    `nonce`; `sid` or `sub` present.
  - Deletes by `Sid` (or by `Subject` when only `sub` is present) with `OUTPUT deleted`, evicts the
    local cache, then calls `ISessionTerminationListener.SessionsTerminatedAsync(subject, handles)`.
  - `jti` is not tracked.
- **Listeners** (§4.5):
  - `ISessionTerminationListener` has two methods: `SessionsTerminatedAsync(subject, sessionHandles)`
    and `TenantAccessRevokedAsync(tenantId, subject)`.
  - It is implemented by spec 0021's `TellmaHub` connection tracker ("close a revoked user's
    connections for that tenant; end sessions").
  - The session store calls it on back-channel logout and revocation. 0011 does not list local
    `RemoveAsync` (the `/bff/logout` path) as a caller.
- **Session handle** (§7.4): `Handle char(22)` is base64url of the first 16 bytes of SHA-256 over
  `Key`. It is the session's name for listeners, hub groups (0021 §6.2) and logs, and is bound as
  `RequestContext.SessionHandle`.
- **Deactivation is tenant-level:** `IsActive = false` in the hint table plus
  `TenantAccessRevokedAsync`. The next connect answers `404`. The distribution session survives, and
  `/bff/user` shows `isActive: false`.
- **Step-up** (§4.3, §5.5):
  - `Tellma:Session:StepUp { Acr: "urn:tellma:acr:mfa", MaxAge: "00:15:00" }` is stamped as
    `RequireAssuranceMetadata` on every endpoint whose securable `IsSensitive`.
  - The guard raises `StepUpRequiredException(Acr, MaxAge)`, which becomes `401` with
    `WWW-Authenticate: Bearer error="insufficient_user_authentication", acr_values="…", max_age=…`
    (via `StepUpChallenge.Write`).
  - A `ServiceAccount` gets `403 individual-required` instead.
  - The SPA's recovery is a top-level navigation to `/bff/login?returnUrl=…&acr_values=…&max_age=…`
    (the parameters exist; the SPA flow is not described).
- **Cross-tab sign-out:** 0011 says nothing about other browser tabs. Server-side the effects are:
  - deleted rows make the next request from any tab `401` (immediately on the revoking instance,
    within `CacheTtl` on others);
  - the hub tracker closes connections for the terminated handles;
  - `/bff/logout` clears `tellma.profile` for the whole origin, but nothing notifies the other tabs;
  - a back-channel logout (sign-out elsewhere) does not touch `tellma.profile` at all.

  x-ref 0003 §7.3: global logout is the default and fans out by back-channel to every distribution
  with a session for that `sid`. 0003 §7.4: session end closes SignalR connections, with
  `CloseOnAuthenticationExpiration` as a backstop.
- `SessionSweepService` (`SweepInterval` 15 min) deletes rows expired for more than a day (§5.5, §7.5).

### 2.10 CSRF posture (0011 §5.4; Decisions 18)

`TellmaCsrfMiddleware` runs after `UseAuthorization` and before `TenantMiddleware`. It applies to
every `Tellma.Session`-authenticated request whose method is not `GET`/`HEAD`/`OPTIONS`. Rules, in
order:

1. `Sec-Fetch-Site`, if present, must be `same-origin` or `none`. Otherwise `403 csrf-rejected`
   (`rule = sec_fetch_site`).
2. `Origin` (else `Referer`), if present, must equal `Tellma:PublicOrigin`. Otherwise `403`
   (`rule = origin`).
3. The request must carry `Tellma-Client: <name>/<version>`, with `<name>` in
   `TellmaApiOptions.ClientNames` (default `web`) and a non-empty `<version>`. Otherwise `403`
   (`rule = header`).
   - The name part becomes `RequestContext.Client` and the `tellma.client` tag (a closed set).
   - "the version part is logged, never a metric tag".
4. A request with a body must declare `Content-Type: application/json`. Endpoints with
   `AcceptsBinaryMetadata` accept any declared type, but the header stays required. Otherwise `415`
   (`rule = content_type`).

- Exempt from the header (rules 1–2 still apply when present): cookie GETs (`/bff/user`, blob
  downloads) and the hub negotiate. These must be side-effect free. "the hub rejects a cross-origin
  negotiate outright". Bearer surfaces and anonymous endpoints are exempt entirely.
- No CORS policy exists anywhere. No antiforgery tokens are used ("the .NET antiforgery middleware
  enforces nothing on JSON endpoints"). `SameSite=Lax` stays because `Strict` "breaks the post-login
  redirect chain and deep links".
- Implications for the front end:
  - `navigator.sendBeacon` cannot set `Tellma-Client`, so a beacon POST to a cookie endpoint fails
    rule 3.
  - `fetch(..., { keepalive: true })` can set the header.
  - x-ref: 0016 projects queries as `POST …/query`, so every grid query carries the header.

### 2.11 Tenant model and what the SPA sees per state (0011 §3.3, §3.4, §3.7, §3.9, §4.3, §4.6)

- Enums: `enum TenantCategory { Live, Sandbox }`; `enum TenantState { Provisioning, Active, ReadOnly,
  Suspended, Retired }`.
- `TenantDescriptor(int Id, string Name, string? Name2, string? Name3, IReadOnlyList<string> Languages,
  TenantCategory Category, TenantState State, int? LiveTenantId)`. It never exposes the database
  location.
- `Category` is immutable. A sandbox is its own row (its own id and URL). `LiveTenantId` is "the
  picker's grouping key". A standalone sandbox names no live tenant.
- Verdicts. Order: `401` first, then non-member `404 tenant-not-found`, then the state verdict.

| State | Read | Mutation | Background scope | Notes for the SPA |
|---|---|---|---|---|
| `Provisioning` | `404 tenant-not-found` | same | throws (except migrator) | Indistinguishable from absent on the tenant surface; `/bff/user` carries `state`, so "the picker can show a tenant being set up". |
| `Active` | served | served | created | |
| `ReadOnly` | served (premises `StampActivity = false`, `AllowStateFlip = false`) | `403 tenant-read-only` | throws; the worker skips the tenant | Reads work against a `READ_ONLY` database. |
| `Suspended` | `403 tenant-suspended` | same | throws | Still listed in `/bff/user`. |
| `Retired` | `404 tenant-not-found` | same | throws | Excluded from `/bff/user` (`ListAsync` excludes `Retired`). |

- Verdicts that are not states:
  - `503 tenant-schema-behind` (`RetryAfter` 30 s; raised by spec 0012's executor when schema
    fingerprints mismatch, e.g. mid-deployment);
  - `503 catalog-unavailable` (`Retry-After: 15`; the registry snapshot is older than `MaxStaleness`,
    5 min).
- "Mutating" is endpoint metadata (`TenantEndpointMetadata.IsMutation`), stamped by 0016's projection
  from the operation descriptor.
- Legal transitions: `Active ⇄ ReadOnly`; `Active | ReadOnly → Suspended`; `Suspended → Active`;
  `Provisioning → Active` (migrator only); any state `→ Retired` (terminal).
- Propagation: state changes take effect immediately on the writing instance and fleet-wide within
  one `Tellma:Catalog:RefreshInterval` (15 s). The admin API returns `effectiveWithinSeconds: 15`
  (§5.9).
- `ITenantStateListener.OnStateChangedAsync(tenant, previous)` is implemented by 0021's hub tracker
  ("close a suspended tenant's connections") and 0020's worker (§4.5).
- Sandbox (§3.9): `TenantSandboxContext : ISandboxContext` answers `RequestContext.Tenant.Category ==
  Sandbox`. `RequestContext.IsSandbox` is derived. 0011 defines no client-facing sandbox signal
  beyond `category` in `/bff/user`.
- Exceptions (§4.6). All derive from 0015's `TellmaException`, and problem codes are kebab-case:
  - `TenantNotFoundException(TenantId)` → `404 tenant-not-found`. It also covers `Provisioning`,
    non-members and deactivated users.
  - `TenantUnavailableException(TenantId, State?, Code, RetryAfter?)` → `503 catalog-unavailable` |
    `tenant-schema-behind`, or `403 tenant-suspended` | `tenant-read-only`.
  - `StepUpRequiredException(Acr, MaxAge)` → `401 step-up-required` + `WWW-Authenticate`.
  - `TenantStateException(Code, Detail)` → `409` (`Tenant.RegistrationRefused` | `Tenant.IllegalTransition`;
    admin surface only).
  - CSRF → `403 csrf-rejected`; content type → `415`.
  - 0016 maps all of these to problem details.

### 2.12 The membership directory and what the picker can show (0011 §3.8, §7.1, §7.3, §7.5)

- `TenantMembership(TenantDescriptor Tenant, bool IsActive, DateTimeOffset UpdatedAt)`.
- `ITenantMembershipDirectory`:
  - `ListAsync(subject)` excludes `Retired` and runs one catalog query (a join of `TenantMemberships`
    and `Tenants`).
  - `RecordAsync(records)` is a best-effort upsert.
- `catalog.TenantMemberships` is "a navigation hint, never an authorization source". Columns:
  `Subject varchar(255)` (BIN2 collation), `TenantId int`, `IsActive bit`, `UpdatedAt datetimeoffset(3)`.
  PK `(Subject, TenantId)`.
- Writers:
  - spec 0018's `UserService` effects, post-commit and best-effort;
  - migrator provisioning;
  - `MembershipReconcileService`, every 24 h (`Tellma:Tenancy:MembershipReconcileInterval`), first run
    5 min after the first snapshot, single instance by `sp_getapplock`.
- Staleness: "a stale hint can at most show a tenant that answers `404` or hide one reachable by URL
  until the next" reconciliation. The connect step never writes the hint.
- Readers: `GET /bff/user` (the picker), the sign-in skip rule, and the admin `GET
  /api/admin/v1/tenants/{id}/members`.
- What the picker gets per tenant:
  - `id`; `name`, `name2`, `name3`; `languages` (ordered codes, each paired with a name);
  - `category` (`Live`/`Sandbox`); `state` (all except `Retired`); `liveTenantId`; `isActive`.
- Names in all tenant languages (§3.3, §3.7, §7.1):
  - `Name`/`Name2`/`Name3` (`nvarchar(255)`) mirror the tenant settings' name group.
  - `Language1` (`varchar(35)`, BCP 47, NOT NULL) and `Language2`/`Language3` are the content-language
    codes. `CK_Tenants_Names` keeps each name and code paired.
  - At registration only `Name` and `Language1` (the distribution default) are set. "`Languages` …
    holds the distribution's default language alone until the first settings save."
  - Mirroring happens through `ITenantCatalog.RenameAsync(tenantId, TenantDisplayNames)`, called by
    0013's `SettingsService.Save` post-commit, best effort.
- Absent from `/bff/user` and the catalog: tenant logo or icon, colour, last-visited time, sort order,
  any count, the user's display name inside the tenant, and the tenant's `Properties` JSON.

### 2.13 Request context: how language, calendar, time zone reach the server (0011 §4.1, §4.3)

- `RequestContext` is an immutable record, held in a scoped holder with exactly two writers.
- Locale-related fields and their defaults:

  | Field | Default | Notes |
  |---|---|---|
  | `Language` | `"en"` | BCP 47, no extensions |
  | `Culture` | `"en"` | |
  | `CultureInfo` | | Gregorian-forced clone of `Culture` |
  | `Calendar` | `"gc"` | `gc \| uq \| et` |
  | `CalendarSystem` | | |
  | `ContentLanguageIndex` | `1` | 1..3: the tenant content language matching `Culture`, else 1 |
  | `TimeZone` | `"UTC"` | the display zone |
  | `TenantTimeZone` | `"UTC"` | binds `today()` |
  | `Now` | | |
  | `Today` | | in `TenantTimeZone` |
  | `TenantSettings` | | |
  | `Client` | `"web"` | `web \| mcp \| worker` |
  | `OriginTraceParent` | | |

- Inputs, as `RequestContextInputs(AcceptLanguage, RequestedCalendar, RequestedTimeZone, Client,
  MessageLanguage, MessageCalendar)`. On the web surface the host extracts the first four from:
  - `Accept-Language`;
  - `Tellma-Calendar`;
  - `Tellma-Time-Zone`;
  - `Tellma-Client` (its name part).

  The two `Message*` members are MCP-only.
- Precedence, set by spec 0013's negotiation initializer at `Order` 200: "request header → the user's
  stored preference → the tenant's settings → platform defaults (`en`, `gc`, `UTC`)". Further rules:
  - Culture names are stripped of `-u-` extensions.
  - "the calendar is never encoded in the culture name".
  - `TimeZone` follows "header → User.PreferredTimeZone → tenant".
- "No client header asserts today: a client cannot move a tenant's business date" (Decision 13).
- Nothing in 0011 uses cookies for language, calendar or time zone. The profile cookie's `locale` is
  only the ID-token claim at sign-in.
- `UserId` is set by spec 0014's connect initializer at `Order` 100.
- Principal fields come from claims: `sub`; `azp`/`client_id`; `sid`; `acr`; `auth_time`;
  `tellma_acr_auth_time`. `SessionHandle` comes from the ticket.
- The request `Activity` is tagged `tellma.tenant.id` (§4.3).
- x-ref 0016 §6.2: every tenant-surface response carries these headers:
  - `Tellma-Trace-Id`;
  - `Content-Language` (the resolved message language);
  - `Tellma-Calendar` (the effective calendar);
  - `Tellma-Build`;
  - `Tellma-Version-Tags: settings=…, permissions=…, preferences=…, entities=…`;
  - `Retry-After` on 429/503;
  - optionally `Server-Timing`.

  These are tenant-surface only, so `/bff/*` and `/api/*` tenantless responses do not carry them.

### 2.14 DistributionInfo and versions an About dialog can show (0011 §2.2, §5.9, §9)

- `DeploymentVersions(string PlatformVersion, string DistributionVersion)` is a singleton. These are
  the informational versions of `Tellma.Core` and of the entry assembly. "both carry the source
  commit as `+<sha>`" (SourceLink and `IncludeSourceRevisionInInformationalVersion`).
- `GET /api/distribution-info`: anonymous, tenantless, `Cache-Control: public, max-age=60`. Returns
  `DistributionInfo(Slug, DisplayName, DeploymentId, PlatformVersion, DistributionVersion, IdentityInfo
  Identity, IReadOnlyDictionary<string,string?> Surfaces, string Login, IReadOnlyList<int>
  AdminVersions)`. The JSON is:

  ```
  { slug,
    displayName,                // Tellma:DisplayName; a single string, not localised
    deploymentId,
    platformVersion, distributionVersion,
    identity: { authority, mode: "Standalone" | "InProc" },
    surfaces: { "web": "/{tenantId}/api/web", "mcp": "/{tenantId}/mcp" | null, "api": null },
    login: "/bff/login",
    adminVersions }             // [1], or [] when the admin surface is off
  ```

  It holds "No tenant list, no secrets, no instance identity".
- Versions available to an About dialog:
  - `platformVersion` (Tellma.Core) and `distributionVersion` (entry assembly), both with `+sha`.
  - x-ref 0016 §3.10: `Tellma-Build: <DistributionVersion>` on every tenant-surface response,
    "a diagnostic … that no rule compares".
  - x-ref 0013 §10.4: the SPA build has `DistributionVersion` "stamped into its build" as the strings
    pack `v`.
- Not available: per-pack or per-feature versions (no feature version in `ITellmaFeature`), an SPA
  build id distinct from the distribution version, localised descriptions, a copyright notice, and
  terms-of-service or privacy URLs.
- Observability use (§9): log enricher properties `PlatformVersion`/`DistributionVersion`; the OTel
  resource sets `service.version` = distribution version and `tellma.platform.version` = platform
  version.

### 2.15 SignalR hub hosting (0011 §1.2, §1.3, §3.1, §4.5, §5.2, §5.4, §8)

- Route `/{tenantId:int:min(1)}/hub`, route group `Hub`, policy `Tellma.Web` (cookie), with
  `TenantAccessFilter` and `Surface = Hub`. Tenant state verdicts apply.
- The hub is one-per-tenant by construction: a connection belongs to one tenant URL.
- The negotiate is exempt from the CSRF header. The hub rejects cross-origin negotiate.
- The dev proxy forwards `/{tenantId}/hub` with websockets on.
- Backplane: Azure SignalR when `Azure:SignalR:ConnectionString` is present (outside the `Tellma`
  section; a secret delivered via Key Vault, §3.6). It is selected by `UseAzureWebDefaults()` per
  0021 §6.6. On-prem gets "a Redis backplane" bundle with the first on-prem distribution.
- The hub implements `ITenantStateListener` and `ISessionTerminationListener` (§4.5). Groups and
  events are spec 0021's (a Non-goal of 0011).
- x-ref 0003 §7.4:
  - With Azure SignalR, the negotiate response hands the browser a hub-scoped service token (~1 h,
    pruned to the subject). It travels in the query string to the Azure service endpoint, a
    different origin from the SPA.
  - Events are thin: the client re-fetches over HTTP.

### 2.16 Observability configuration (0011 §5.1, §8, §9)

- `AddTellmaAspNetCore` wires:
  - Serilog from the `Serilog` configuration section;
  - OpenTelemetry: the platform meters and activity sources, plus ASP.NET Core, HttpClient and
    SqlClient instrumentation. The SqlClient enrich callback removes `db.query.text` and
    `db.query.summary` from spans;
  - Azure Monitor, gated on `APPLICATIONINSIGHTS_CONNECTION_STRING`, which sits outside the `Tellma`
    section.
- Meters: `Tellma.Core` (tenancy) and `Tellma.Core.AspNetCore` (host). Host instruments:
  - `tellma.session.operations` (`operation`, `source`);
  - `tellma.session.refreshes` (`outcome` ∈ `refreshed|adopted|rejected|deferred`);
  - `tellma.auth.backchannel_logouts`;
  - `tellma.auth.csrf_rejections` (`rule`);
  - `tellma.auth.stepup_challenges`.
- Tenancy instruments:
  - `tellma.tenancy.resolutions` (`outcome` ∈ `served|not_found|provisioning|read_only|suspended|not_member|catalog_unavailable`);
  - `catalog.refresh.duration`, `catalog.snapshot.age`, `catalog.tenants`;
  - `connection.factories`, `membership.writes`, `scopes`.
- "No tenant or user tag on any instrument". Per-tenant identity is allowed on traces and logs only.
- Logger scope per tenant request: `TenantId`, `TenantCategory`, `Subject` (never email), `UserId`.
  0016 adds `Client`, `Resource`, `Operation`.
- Every log event carries `TraceId`/`SpanId`.
- The request activity carries `tellma.tenant.id` and `tellma.deployment.id`.
- Alert queries are checked in under `infra/monitoring/`.
- **Client telemetry:** nothing.
  - There is no ingestion endpoint and no browser-facing connection string.
  - Nothing says whether incoming W3C `traceparent` from the SPA is honoured. `OriginTraceParent` is
    for background scopes and is "never a tracing instruction".
  - `Tellma-Client`'s version part is logged.

### 2.17 Configuration relevant to the front end (0011 §8)

| Key | Default | Relevance |
|---|---|---|
| `Tellma:DisplayName` | `"Acme"` (example) | `distribution-info.displayName` |
| `Tellma:PublicOrigin` | `https://acme.app.tellma.com`; Dev `https://localhost:4200` | CSRF origin, redirect URIs, MCP resources |
| `Tellma:Identity:Mode` | `Standalone` (`\| InProc`) | `distribution-info.identity.mode`; `/id` exists only InProc |
| `Tellma:Identity:Authority` | `https://identity.tellma.com` | `distribution-info.identity.authority`; base for any "My account" link |
| `Tellma:Session:IdleLifetime` | `7.00:00:00` | matches the brief's 7-day warm launch |
| `Tellma:Session:AbsoluteLifetime` | `90.00:00:00` | also the `tellma.profile` lifetime |
| `Tellma:Session:CacheTtl` | `00:01:00` | revocation lag on other instances |
| `Tellma:Session:MaxAuthorityOutage` | `01:00:00` | |
| `Tellma:Session:StepUp` | `{ Acr: "urn:tellma:acr:mfa", MaxAge: "00:15:00" }` | step-up bar |
| `Tellma:Catalog:RefreshInterval` | `00:00:15` | tenant state propagation |
| `Tellma:Catalog:MaxStaleness` | `00:05:00` | then `503 catalog-unavailable` |
| `Tellma:Tenancy:MembershipReconcileInterval` | `1.00:00:00` | picker hint repair |
| `TellmaApiOptions.ClientNames` (0016's options) | `web` | allowed `Tellma-Client` names |
| `Azure:SignalR:ConnectionString` | absent | Azure backplane |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | absent | Azure Monitor exporter |

- All options are bound with `ValidateDataAnnotations` and `ValidateOnStart`. Keys are
  "additive-only within a family major".
- Data Protection (§5.8): `SetApplicationName("tellma-" + slug)`. Keys come from Azure Blob + Key
  Vault (`Tellma:DataProtection:BlobUri`/`KeyId`) or `KeyRingPath`. Neither configured outside
  Development fails the startup check `host.data-protection`.
- Forwarded headers (§5.8): explicit `KnownProxies`/`KnownNetworks` are required, and
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is refused.
- There is no SPA-facing configuration key: no feature flags for support upload, no telemetry
  sampling, no terms or privacy URLs.

### 2.18 In-proc identity mode (0011 §5.7)

- `Tellma:Identity:Mode = InProc` makes `AddTellma` call `AddTellmaIdentity`:
  - `PathBase = /id`;
  - `Issuer = {PublicOrigin}/id`;
  - the store is the catalog database's `idsvr` schema.
- The OIDC authority becomes `{PublicOrigin}/id`, so the identity UI (sign-in, Account & Security)
  is same-origin with the SPA in InProc and cross-origin in Standalone.
- The distribution owns its BFF client `<slug>` and service client `<slug>-svc`, with stable secrets
  `Tellma:Identity:ClientSecret` and `ServiceClientSecret` (`host.identity` fails outside Development
  when absent; Dev generates a secret per boot).
- `AddTellma` writes the 0010 `Distribution` seed entry from configuration and `PublicOrigin`.

### 2.19 Admin surface (0011 §5.9): not SPA-facing, for completeness

- Policy `Tellma.ControlPlane` (bearer); mapped only when `Tellma:Admin:Enabled`.
- Routes:
  - `GET /api/admin/v1/info`;
  - `GET /api/admin/v1/tenants`;
  - `POST /api/admin/v1/tenants/{id}/state` with `{ state, reason }` → descriptor plus
    `{ effectiveWithinSeconds: 15 }`, or `409`;
  - `POST /api/admin/v1/tenants` with `TenantRegistration` → `Provisioning` row plus the trigger
    (`501` while `NotConfiguredProvisioningTrigger`);
  - `GET /api/admin/v1/tenants/{id}/members`.
- Its own URL version is advertised in `adminVersions`. A caller retries a `404` once on the older
  version.

### 2.20 Spec 0010: identity server amendments (all sections)

- Placement (0010 §1): only `src/apps/Tellma.Identity/` (the engine) and configuration of
  `Tellma.Identity.Web`. No table changes, no new package. Metadata documents are fetched with the
  named HttpClient `Tellma.Identity.ClientMetadata`.
- **Seed client kinds** (§2): `enum TellmaIdentitySeedClientKind { Cli, Native, ControlPlane,
  Distribution }`.
- `TellmaIdentitySeedClientOptions` members:
  - `ClientId`, `DisplayName`, `Kind`, `RedirectUris`, `ClientSecret`, `Resources`, `RequireConsent`;
  - Distribution-only: `Origin` (absolute, path-less), `BackchannelLogoutUri` (default
    `{Origin}/bff/backchannel-logout`), `ServiceClientSecret`.
- **A `Distribution` entry** yields two applications:
  - `<slug>`: confidential; `authorization_code` and `refresh_token`; redirect URIs
    `{Origin}/signin-oidc` and `{Origin}/signout-callback-oidc`; back-channel logout at
    `BackchannelLogoutUri`; resources `{Origin}` plus `Resources`; scopes
    `openid profile email offline_access tellma_api`.
  - `<slug>-svc`: confidential; `client_credentials`; scopes `tellma_identity` and `tellma_api`.
- The seeder is idempotent, never deletes, and only touches applications marked
  `TellmaClientProperties.FirstParty`.
- Startup validation: `ClientId` lowercase, letter-first, at most 15 characters; `Origin` absolute and
  `https` (`http` only in Development); secrets non-empty outside Development.
- **One origin per distribution:** the redirect URIs and the post-logout callback derive from the
  single `Origin`. Exact redirect matching is kept from spec 0003.
- **Per-tenant resources** (§3): `IResourceGrantEvaluator.IsGranted(granted, requested)` accepts an
  exact granted resource, or `<granted-origin>/<id>/mcp` (positive int, no leading zeros, no trailing
  slash, no query). The accepted value is copied into `aud`. A refresh may narrow the audience but
  never widen it. A refused resource is `invalid_target`.
- **Client ID metadata documents** (§4):
  - `ClientMetadataDocumentOptions` (`TellmaIdentity:ClientMetadata`): `Enabled` true, `MaxBytes` 5120,
    `Timeout` 5 s, `MinCache` 1 min, `MaxCache` 24 h, `FetchesPerHostPerMinute` 10.
  - Only `none` and `private_key_jwt` clients are accepted; loopback redirects may use any port.
  - Discovery advertises `client_id_metadata_document_supported: true` and lists `none` among
    `token_endpoint_auth_methods_supported`.
  - Consent is always explicit and shows `client_name` and the host.
- **Interim native clients** (§5): `claude-code`, `codex`, `cursor` (loopback), plus `claude-web` and
  `codex-web` (vendor callback URIs). They are removed one release after §4 serves production.
- **Control-plane grants** (§6): the control-plane client gains every distribution origin as a
  resource; its tokens carry `aud = {origin}`.
- **`tellma_kind`** (§7): `individual` for `authorization_code`/`device_code` grants and their
  refreshes; `service` for `client_credentials` and for token exchange of machine tokens. It is set
  from the grant type and never client-supplied.
- **`existingOnly` on bulk invite** (§8):
  - `InviteUserItem { Email, DisplayName?, Locale?, Gender?, ReturnUrl?, ExistingOnly }`.
  - With `ExistingOnly`: an existing user with a credential answers `Status = Active` plus `sub`; an
    existing user without one answers `existing_user_has_no_credential`; an unknown email answers
    `unknown_email`. No user is created and no mail is sent. Spec 0018 sets the flag for sandbox
    tenants.
  - `ReturnUrl` is the invite's return-to-distribution URL (x-ref 0003: held server-side). Its target
    inside the SPA is not specified.
- **Management API versions** (§9), relative to the authority:
  - `POST api/identity/v1/invitations`;
  - `POST api/identity/v1/invitations/delivery-status`;
  - `POST api/identity/v1/service-accounts`;
  - `GET`/`DELETE api/identity/v1/service-accounts/{clientId}`;
  - `GET api/identity/v1/users/{sub}`;
  - `POST api/identity/v1/users/{sub}/temporary-access-passes`.

  Unversioned routes are removed. Calls are counted on `tellma.identity.api.calls` (`version`,
  `endpoint`).
- Observability (§11):
  - Audit events `client.metadata.fetched`, `client.metadata.rejected`, `resource.refused`,
    `seed.distribution.applied`.
  - Counters `tellma.identity.client_metadata.fetches` (`outcome`) and `tellma.identity.api.calls`.
- **Absent from 0010:**
  - the URL of the identity server's Account & Security ("my account") pages;
  - any end-session or post-logout redirect rule beyond spec 0003;
  - any change to sign-out behaviour;
  - any CORS or front-end-facing endpoint;
  - any SPA client kind (the SPA is not an OAuth client; the BFF `<slug>` is).

  x-ref 0003 §11.2: the distribution "surfaces Account & Security as a 'Sign-in & security' tab inside
  its own account area that deep-links to these pages over SSO (no re-login) with a `return_url`".
  No path is given there either.

## 3. Gaps for round 2

Hosting, PWA, headers:
- **SPA caching headers.** No rule for `index.html` (it must revalidate so a new deployment is seen),
  hashed bundles (immutable), `ngsw.json`, the SW script, or `manifest.webmanifest`. The SW update
  model of the brief depends on these.
- **Service worker routing.** No rule keeps the SW's navigation fallback off server-handled paths:
  `/bff/*` (login is a top-level navigation), `/signin-oidc`, `/signout-callback-oidc`, `/id`,
  `/api`, `/health`, `/.well-known`, `/openapi`, and `/{tenantId}/api|hub|blobs|mcp`.
- **Security headers on the SPA document.** None are specified: CSP (`connect-src` must admit the
  Azure SignalR service origin; `form-action`/navigation must admit the authority in Standalone),
  `frame-ancestors`, `Referrer-Policy`, `Permissions-Policy` (relevant to a screenshot feature),
  Trusted Types.
- **Static assets and the fallback policy.** Static asset endpoints are not on the `AllowAnonymous`
  list of §5.2. Under a denying fallback policy and the endpoint audit, the platform must stamp them,
  or a signed-out browser cannot load the bundle that runs the auth guard.
- **Deep-link catch-all.** The tenant deep-link catch-all `/{tenantId}/{**path}` has no stated
  exclusion for `api|hub|blobs|mcp`. A mistyped API path could answer `200 index.html`.

BFF and session:
- **`POST /bff/logout` response contract.** A `fetch` POST cannot follow a redirect to the
  authority's end-session endpoint (cross-origin in Standalone, and no CORS), and the JSON plus CSRF
  rules forbid a form post. The post-logout landing page (`SignedOutRedirectUri`) is also
  unspecified.
- **Cross-tab sign-out.** No signal exists. Other tabs learn only through a `401` or a hub close. A
  back-channel sign-out never clears `tellma.profile`. 0025 must define the client mechanism.
- **Profile cookie contents.** `tellma.profile` lacks `email` (the brief's tenantless shell shows it)
  and `sub` (needed to partition warm-launch caches per user on a shared browser, and to detect a
  different user after re-login). Its encoding is unspecified, and it is never rewritten (a name
  change stays stale until the next sign-in).
- **Session idle signal.** The profile cookie outlives the 7-day idle session (90 days), so the SPA
  cannot tell synchronously that a session is likely dead. It needs its own last-activity stamp if it
  is to avoid render-then-`401`.
- **Step-up in the SPA.** Not described: the `401` with `WWW-Authenticate:
  error="insufficient_user_authentication"` (code `step-up-required`) leads to a top-level
  `/bff/login?returnUrl&acr_values&max_age`, which loses in-memory state (drafts needed). Also,
  `/bff/login` always calls `GetLoginPolicyAsync(null)`, so a tenant's login policy cannot shape a
  step-up.
- **"My account" link.** No spec gives the path of the identity server's Account & Security pages.
  Only `distribution-info.identity.authority` and 0003's "deep-links … with a `return_url`" exist.

Tenantless shell, picker, About:
- **Tenantless languages.** No tenantless list of offered languages (`distribution-info` has no
  `languages`), and no tenantless place to persist a language choice (no tenantless user
  preference; `locale` is the identity server's).
- **Picker content.** No tenant logo or colour, and no server-side last-used order. The picker must
  decide how to show `Provisioning` (which answers `404`), `Suspended` (`403`), and `isActive: false`
  (`404`) rows.
- **Picker freshness.** The skip rule and `/bff/user` use the hint table, which repairs within 24 h.
- **Tenant state inside the tenant shell.** A `ReadOnly` UI (hide edit actions) and a sandbox banner
  need the state and category in tenant-scoped cached data. 0011 provides them only via tenantless
  `/bff/user`.
- **About dialog.** Missing: per-pack versions (`ITellmaFeature` has no version), an SPA build id
  distinct from `DistributionVersion`, a localised description, a copyright notice, terms and privacy
  URLs. `displayName` is a single string.

Observability and support:
- **Client telemetry ingestion.** No endpoint, no browser-safe ingestion key, no meters or log events
  for it. `sendBeacon` fails CSRF rule 3, so the endpoint needs `fetch` with `keepalive` or a
  documented exemption. Whether it is tenant-scoped (inherits `TenantId` logger scope and rate
  limiter partition) or tenantless is open.
- **Trace ids outside tenant surfaces.** `Tellma-Trace-Id` is tenant-surface only, so `/bff/user` and
  tenantless calls give the user no trace id. Whether an SPA-sent `traceparent` is honoured, which
  would let a cancelled or slow request be logged with a known id, is unspecified.
- **Send to support.** No upload route, blob kind, or control-plane channel for a state dump or
  screenshot. There is no opt-in configuration key, and the distribution has no outbound
  control-plane client.
- **UI version on the wire.** `Tellma-Client: web/<version>` exists, but its version format is free
  and it is required only on non-GET requests. Nothing states the SPA sends it on GETs.

Errors and back-end pieces:
- **`503` handling.** `catalog-unavailable` (`Retry-After: 15`) and `tenant-schema-behind` (30 s)
  need a shared client treatment, the 0024 HTTP layer's job. 0011 only defines the verdicts.
- **New back-end pieces.** `core.Assignments` (0028) and report definitions (0033) need nothing new
  from 0011's feature contract (existing `Entity<>`, `Securables`, `ClientEvent`,
  `NotificationType` items suffice), but any new hub event name must go through `ClientEvent(name)`
  (spec 0021).

## 4. Likely change points

- **0011 §5.5, BFF endpoints:**
  - Define the `/bff/logout` response (for example `200 { endSessionUrl }` or `204`) and the
    post-logout landing.
  - Revise the profile cookie shape: add `email` and `sub`, define the encoding, and decide whether
    it is rewritten after a refresh.
  - Change the post-sign-in redirect target if the picker moves to `/tenants` and the home page to
    `/{id}/home` (brief open questions 1–2).
  - Narrow "falls back to the picker on `403` or `404`" to specific problem codes.
  - Replace the sentence prescribing "local storage" caching of `me`/`settings/client` with a
    citation of 0025.
- **0011 §5.1 and §5.2, `MapTellma` and the anonymous list:**
  - static asset and SW file headers;
  - SPA document security headers;
  - deep-link exclusions;
  - adding static assets, manifest, SW and telemetry endpoints to the `AllowAnonymous` and tenantless
    stamps;
  - possibly new platform routes (client telemetry, support upload).
- **0011 §5.3, reserved prefixes:** add any new platform prefix. The SPA's tenantless routes (e.g.
  `/tenants`) must be non-numeric and must avoid every reserved and forwarded prefix.
- **0011 §5.4, CSRF:** a beacon-telemetry exemption, or a stated `keepalive` rule. Possibly make
  `Tellma-Client` mandatory on every SPA request, so the UI version is logged on reads.
- **0011 §5.9, `DistributionInfo`:** candidate members `languages` (offered), `calendars`, an identity
  account URL, `termsUrl`/`privacyUrl`, support-upload enablement, feature or pack versions. Its
  `max-age=60` interacts with SW caching.
- **0011 §2.1 and §2.2:** a feature or pack version, if the About dialog lists packs.
- **0011 §1.2 and §6.6, Client project tree:** the `src/` layout, `ngsw-config.json`,
  `manifest.webmanifest`, the embedded `contracts.json` (0016), the updated `proxy.conf.js` prefix
  list, and the E2E suite gaining tests.
- **0011 §8, configuration:** new keys (terms and privacy URLs, support channel, telemetry options).
- **0011 §9, observability:** client-telemetry ingestion instruments and log events.
- **0011 §4.5 and §5.5, listener calls:** have local `/bff/logout` call
  `ISessionTerminationListener.SessionsTerminatedAsync` for its own handle, so the hub closes that
  session's connections (see §5 item 7).
- **0010, as a whole:** rename to "Amendments to Implemented Specs", keep the current text as Part A,
  and add parts for 0002, 0004, 0005, 0006 and 0008. A stable Account & Security URL and `return_url`
  contract, or a post-logout redirect rule, would be a new Part A amendment to frozen 0003.

## 5. Contradictions and surprises

1. **The picker is often skipped.** The brief says "On a cold load after sign-in, the user lands on
   the tenant picker". 0011 §5.5 skips the picker when exactly one membership is `Live` + `Active` +
   `isActive`. Sandboxes do not count, so the dev admin (tenant 1 Live, tenant 2 Sandbox) lands on
   `/1/`, not the picker.
2. **Route names disagree.** 0011 makes `/` "the picker" and the BFF's default landing, and the
   single-tenant redirect is `/{tenantId}/`. The brief wants `/tenants` and `/{tenantId}/home`. If `/`
   is also the SPA's warm-launch redirect, a post-sign-in landing on `/` with an existing cache jumps
   to the last tenant instead of showing the picker.
3. **Cookie lifetimes differ.** `tellma.profile` lives 90 days; the session idles out at 7 days. Days
   8–90 give a "signed-in" synchronous signal, a warm render from cache, then `401` and a redirect to
   `/bff/login`.
4. **Logout is not navigable.** `POST /bff/logout` is a JSON, CSRF-guarded endpoint, so neither a
   link nor a form post can drive it. Its redirect to the authority cannot be followed by `fetch`.
5. **"Falls back to the picker on 403" is too broad.** `403` also means `tenant-read-only` (mutations
   only), `individual-required`, `csrf-rejected`, and ordinary permission denials. `Provisioning`
   answers `404`, so a tenant being set up looks deleted on the tenant surface while the picker shows
   its state.
6. **Back-channel sign-out is invisible to the browser.** Sign-out elsewhere (another distribution,
   or "sign out everywhere" in Account & Security) deletes the session row server-side only. The
   profile cookie stays, and tabs learn on their next request (within `CacheTtl` 60 s on other
   instances) or when the hub closes.
7. **Hub connections may survive the initiating session's sign-out.** This is plausible, not
   confirmed; check 0021 §6.2. `SessionsTerminatedAsync` is called on back-channel logout and
   revocation, not on local `RemoveAsync`. On a global sign-out the local row is deleted first, so the
   later back-channel `DELETE … OUTPUT` returns no handle for that session. Only
   `CloseOnAuthenticationExpiration` (0003 §7.4) would close those connections.
8. **The anonymous list omits static assets.** Static asset endpoints are not listed, and the
   fallback policy denies anonymous callers. Implementers must stamp them, or the audit fails startup
   or the bundle answers `401`.
9. **The deep-link catch-all can return HTML for API typos.** `/{tenantId}/{**path}` can shadow
   unknown `/{tenantId}/api/...` paths, so a client sees HTML with `200` instead of a `404` problem.
10. **Diagnostic headers are tenant-surface only.** `Tellma-Trace-Id`, `Tellma-Build` and
    `Content-Language` (0016 §6.2) are absent from `/bff/*` and tenantless `/api/*`, so the
    tenantless shell has no trace id for support.
11. **The browser's `Accept-Language` beats stored preferences.** Browsers always send it, and the
    precedence puts the header first, so the SPA must send its chosen language explicitly or the
    browser's preference wins. WebSockets, `<img>` blob GETs and `sendBeacon` cannot carry `Tellma-*`
    headers.
12. **One origin is structural.** 0010 seeds exactly one `Origin` per distribution (and in
    Development that is the dev server `https://localhost:4200`, not the Web project's `7052`). The
    `__Host-` cookie, the CSRF origin check and the absence of CORS make SPA and API one origin;
    hosting the SPA document on a CDN origin is not possible without changes. The Azure SignalR
    service and, in Standalone mode, the authority are different origins.
13. **Identity mode changes origins.** In InProc the identity UI is same-origin (`/id`); in
    Standalone it is `https://identity.tellma.com`. The account link, CSP and post-logout flow must
    handle both.
14. **New tenants have one name.** `Name2`/`Name3` stay null and `Languages` holds only the default
    language until the tenant's first settings save. Names are paired with `languages` by position,
    not keyed by code.
15. **Ownership overlap with 0025.** 0011 §5.5 already prescribes client behaviour: cache `me` and
    `settings/client` per tenant "in local storage", validate by version tags, reopen the last-used
    tenant. 0025 should own this, with 0011 citing it.
16. **Naming mismatch.** 0011 says "company picker"; the brief says "tenant picker".
17. **Headers that look similar mean different things.** `Tellma-Calendar` is a request header (the
    requested calendar) and a response header (the effective calendar). The `Tellma-Client` version is
    "logged, never a metric tag" and compared with nothing; client staleness is detected by
    `Tellma-Contract` (0016 §3.10).
18. **The SPA build already knows the server version.** x-ref 0013 §10.4: the SPA build has
    `DistributionVersion` stamped in, as the strings pack `v`. Publishing builds the client in the
    same `dotnet publish`, so "UI version = DistributionVersion (`+sha`)" is nearly free.
19. **A deactivated user still sees the tenant.** `isActive: false` rows remain in `/bff/user`, and
    connecting answers `404`. The session survives because deactivation is tenant-level.
