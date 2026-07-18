# Spec: Tellma Identity Server

- **Author:** Ahmad Akra
- **Date:** 30 June 2026

## 1. Overview

The Tellma Identity Server (`Tellma.Identity`) is the shared OpenID Connect authority that
authenticates users for every Tellma distribution. It performs **authentication only**: it answers
"who is this principal, and how strongly did they prove it?" It does not own roles, permissions, or
tenant membership — those are resolved by each distribution from its own databases, keyed on the
stable subject identifier (`sub`) the server issues. Distributions are plain OIDC relying parties, so
the authority is swappable.

**The server is deliberately tenant-agnostic.** It stores a global directory of users and a registry
of OAuth clients. It does **not** map users or service accounts to tenants or distributions; those
associations live in each distribution's own database.

OpenIddict provides the OAuth 2.0 / OpenID Connect protocol engine; ASP.NET Core Identity provides the
user store, credential storage, passkeys, MFA, external logins, and the sign-in cookie. The remainder
— the authorization controller, all UI, the client-provisioning and invitation APIs, the
authentication-policy enforcement, and back-channel logout — is application code defined here.

### 1.1 Built-in vs. built-here

OpenIddict provides as first-class built-ins: Authorization Code + PKCE, Client Credentials, Device
Authorization Grant, Refresh Token with **rotation on by default**, Token Exchange, Pushed
Authorization Requests (PAR), mTLS/certificate-bound tokens, RP-initiated (end-session) logout,
discovery + JWKS, EF Core stores, and Quartz token pruning.

The following are implemented by this project (each small and testable):

| Built here | Reason |
|---|---|
| The authorization controller and every auth-flow UI page | OpenIddict ships pass-through plumbing, not controllers or views |
| Invitation, recovery, and client-provisioning APIs | OpenIddict has no built-in Dynamic Client Registration; provisioning calls `IOpenIddictApplicationManager` |
| **Back-channel logout** (emit `logout_token`, track `sid`) | Not built into OpenIddict yet; front-channel logout is unusable (browsers block its third-party-cookie iframes) |
| The resource-server **step-up challenge** (`401 insufficient_user_authentication`) | Lives in the distribution backend; OpenIddict provides only the authorization-server side |
| A single-use email-code token provider | The built-in email/phone OTP provider is TOTP-based with a fixed, replayable window of up to ~15 minutes |

Sender-constrained tokens use **mTLS** (OpenIddict supports it; it does not support DPoP). Browser
clients hold no tokens (§7), so this is only relevant to confidential machine clients that opt in.

## 2. Deployment modes and project structure

One engine, two hosting shapes, selected by configuration.

- **Standalone (default).** `Tellma.Identity.Web` runs as its own Azure App Service at
  `https://identity.tellma.com`, the shared authority for a group of distributions. The same binary,
  configured with a different issuer, keys, and database, serves an **isolated** deployment (on Azure
  or on-prem) for an organization that requires an isolated authority for data-residency or compliance.
- **In-proc.** A distribution hosts the authority inside its own ASP.NET host, at its own origin
  (`https://<slug>.app.tellma.com`, under a reserved path such as `/id`), for local development and
  single-distribution on-prem/standalone delivery. In-proc is still multi-tenant and still
  authenticates external CLI and native clients against that distribution's API.

### 2.1 Projects

| Project | Kind | Role |
|---|---|---|
| **`Tellma.Identity`** | ASP.NET Core Razor Class Library | The engine: OpenIddict + Identity configuration, endpoints, controllers, Razor pages, services, stores. Exposes `AddTellmaIdentity(...)` and endpoint-mapping extensions. Referenced by the standalone host and, only when running in-proc, by a distribution's Web host. |
| **`Tellma.Identity.Migrations`** | Class library | The EF Core migrations assembly (migrations + design-time factory) for the engine's schema, kept separate so hosts opt into schema management explicitly. Referenced by hosts that apply migrations. |
| **`Tellma.Identity.Web`** | ASP.NET Core host | The thin standalone deployable app: composition, configuration, hosting for standalone mode. |

A distribution that uses the shared authority references **none of these projects** — it is an ordinary OIDC
relying party using standard OIDC middleware. The `Tellma.Identity` reference is opt-in for the in-proc
mode only. An architecture test asserts both hosting shapes share the same registration path so
behavior cannot drift.

### 2.2 Mode differences

| Concern | Standalone | In-proc |
|---|---|---|
| Issuer | `https://identity.tellma.com` (or the isolated host) | The distribution's own origin (+ path base) |
| Passkey RP ID (§8) | `identity.tellma.com` — one passkey works across all distributions | The distribution's host — passkeys scoped to that distribution |
| Keys | Config-selected source (§13) | The distribution's configuration; same sources (§13) |
| Store | Shared Identity database | The distribution's own database (dedicated schema) |
| Clients | Provisioned by onboarding automation | Local seed configuration |
| Operator surface (§11) | Present (in the control plane) | Absent; only the invite and service-account APIs |

## 3. Component responsibilities

| Layer | Owns |
|---|---|
| **ASP.NET Core Identity** | User store, credential storage & hashing, passkeys, TOTP/email codes, external logins, the **SSO session cookie** (`IdentityConstants.ApplicationScheme`), security-stamp validation |
| **OpenIddict** | Protocol endpoints (authorize, token, userinfo, introspection, revocation, device, end-session, discovery, JWKS); token issuance & validation |
| **`Tellma.Identity`** | The authorization controller, all UI, client provisioning, invitation/recovery, authentication-policy enforcement, back-channel logout, branding |

The SSO session is the Identity application cookie, not an OpenIddict artifact. The authorize endpoint
checks it with `HttpContext.AuthenticateAsync()`; if absent it issues a `Challenge` to the login UI;
once satisfied the controller builds a `ClaimsPrincipal` and returns
`SignIn(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)`, which OpenIddict turns into the
tokens. Endpoints run in pass-through mode so the controller shapes the interaction while OpenIddict
produces the protocol response. Server-side "sign out everywhere" combines `UpdateSecurityStampAsync` with session termination and
back-channel fan-out (§7.3); none of this revokes already-issued access tokens (§12).

## 4. Actors, clients, resources, and flows

| # | Use case | User | Client | Client type | Resource / audience | Flow |
|---|---|---|---|---|---|---|
| 1 | Distribution SPA → its API | End user | Distribution backend acting as **BFF** | Confidential | Distribution API (`aud` = distribution origin) | Authorization Code + PKCE; code exchanged server-side; rotating refresh held server-side |
| 2a | CLI, browser available | End user | Tellma CLI | Public | Distribution API / server management | Authorization Code + PKCE, loopback `http://127.0.0.1:{ephemeral-port}` redirect |
| 2b | CLI, headless / SSH | End user | Tellma CLI | Public | Distribution API | Device Authorization Grant |
| 3 | Job / integration script | None (machine) | Service account | Confidential | Distribution API / server management | Client Credentials (no refresh token) |
| 4 | Distribution backend → trusted service | None, or a user being acted for | Distribution backend | Confidential | Server management API (`tellma_identity`) | Client Credentials; Token Exchange when propagating a user identity or down-scoping |
| 5 | Native app on kiosk / tablet / device | End user (or device) | Native app | Public | Distribution API | Authorization Code + PKCE via system browser (claimed-`https` > loopback > custom scheme); Device Grant when no browser |
| 6 | Control plane → distribution admin surface | None (operator) | Control plane | Confidential | Distribution admin contract (`tellma_control_plane`) | Client Credentials |

Cross-cutting rules (per the OAuth Security BCP): PKCE for every authorization-code client (public and
confidential); implicit and ROPC grants are disallowed; exact redirect-URI matching; bearer tokens
never in query strings; `iss` returned in the authorization response to prevent mix-up. Native and CLI
clients use the system browser, never an embedded web view. Unattended kiosks that act autonomously use
a dedicated service account (client credentials); per-user kiosks use the Device Grant with short-lived
tokens, no token persistence, and short idle timeouts.

## 5. Client registration

Every distribution is a **confidential BFF client** (`client_id = <slug>`), provisioned by the
distribution's onboarding automation, which invokes the server's client-provisioning service (a wrapper over
`IOpenIddictApplicationManager.CreateAsync`). Because the automation knows the slug, it registers the
client's **exact redirect URIs** (`https://<slug>.app.tellma.com/signin-oidc` and the post-logout
`.../signout-callback-oidc`); the generated secret is written to the distribution's Key Vault. Redirect
validation therefore uses OpenIddict's built-in exact match — **there is no wildcard trust and no custom
redirect/CORS validator**. Because the BFF exchanges codes server-side, OpenIddict's endpoints need no
CORS policy.

Distinct clients with least-privilege permissions:

| Client | Type | Registered by | Grants |
|---|---|---|---|
| Distribution BFF (`<slug>`) | Confidential | Onboarding automation | `authorization_code`, `refresh_token` |
| Distribution backend M2M (`<slug>-svc`) | Confidential | Onboarding automation | `client_credentials` (+ `token_exchange` when it acts for users) |
| Service account | Confidential | Runtime (invitation/service-account API) | `client_credentials` |
| Tellma CLI | Public | Seeded platform config | `authorization_code` (loopback), `device_code`, `refresh_token` |
| Native app | Public | Seeded per app | `authorization_code` (system browser), `device_code`, `refresh_token` |
| Control plane | Confidential | Seeded platform config | `client_credentials` |

Public native and CLI clients hold refresh tokens (per RFC 8252 §6): they run on the user's own
device with no server-side session to fall back on, so silent renewal needs a client-held refresh
token. The token-theft exposure a browser faces (§7.1) does not apply — nothing is served to a remote
origin — and rotation with reuse detection (§6.3) bounds a stolen token. Service accounts still get no
refresh token (client credentials re-authenticates directly).

## 6. Tokens, claims, scopes, and audiences

Access tokens are **signed-only JWT** (`DisableAccessTokenEncryption()`), so any ASP.NET Core resource
server validates them with standard JWT-bearer middleware against the cached JWKS, with no per-request
database call. Authorization codes, refresh tokens, and device codes remain encrypted (OpenIddict's
default). Only **asymmetric** signing keys are registered: when a symmetric signing key is present
OpenIddict prefers it for access tokens, which would emit HS256 tokens that an offline resource server —
holding only the public JWKS — cannot validate.

### 6.1 Claims

| Claim | Source | Notes |
|---|---|---|
| `sub` | User store | Stable opaque identifier; the durable cross-distribution identity key. Never the email |
| `iss`, `aud`, `exp`, `iat`, `nbf`, `jti` | Server | `aud` is derived from requested scopes → resources |
| `client_id` / `azp`, `scope` | Grant | |
| `acr`, `amr`, `auth_time` | Authentication event | Assurance tier, methods used, and last-authentication time (§9) |
| `sid` | Session | Binds back-channel logout (§7) |
| `email`, `email_verified`, `name`, `preferred_username` | User store | |
| `locale` | User store | Preferred language, set at invitation; drives localized UI and email |

No roles, permissions, or tenant membership appear in any token.

### 6.2 Scopes, resources, audiences

Fixed scope catalog: `openid`, `profile`, `email`, `offline_access`, `tellma_api` (call a distribution
API), `tellma_identity` (call the server management API), `tellma_control_plane` (call the control-plane
admin surface). For a browser-based distribution (its BFF), the target API is unambiguous, so the `tellma_api` scope maps
to a resource **derived from the client's origin** — `aud = https://<slug>.app.tellma.com`, and a token
minted for one distribution is not structurally valid at another. Clients with no browser origin — the
CLI, native apps, and backend M2M callers that may target more than one distribution — instead name the
target API explicitly with the **`resource` parameter (Resource Indicators, RFC 8707)**; the server
validates each requested resource against the client's granted resource permissions before it becomes
the token's `aud`. Grantable audiences — the per-distribution API audiences plus the fixed platform
audiences — are assigned per client at provisioning time; because distribution audiences are created at
runtime there is no static registered-resource list and no general "create a resource" administration.

### 6.3 Lifetimes

| Token | Lifetime | Notes |
|---|---|---|
| Access token | 10 min | Short; also the policy re-evaluation point (§9) |
| ID token | 5 min | Consumed once at login |
| Refresh token | Rolling with reuse detection (~30 s reuse leeway for concurrent refreshes); **sliding 7-day idle lifetime** | Each rotation extends the window 7 days from last use, guaranteeing silent renewal within the return-visit window; an idle session expires 7 days after its last refresh |

## 7. Sessions, cookies, lifetimes, and logout

### 7.1 BFF pattern

Every distribution's SPA is served by the distribution's own same-origin ASP.NET backend, which acts as
the **Backend-For-Frontend**: it is a confidential OIDC client that exchanges the authorization code
server-side, holds the tokens server-side, and issues the SPA an encrypted `HttpOnly`, `Secure`,
`SameSite=Lax` session cookie. **The SPA holds no API-capable tokens**, which removes the
token-exfiltration class of XSS attacks (the hub-scoped real-time token in SaaS hosting is the single
deliberate exception, §7.4). Because the backend is same-origin, there is no proxy hop beyond the API call the SPA would
make anyway.

**Instant launch** (the app shell rendering from the service-worker cache with zero network round-trips)
is preserved: at login the backend also writes a small readable, non-sensitive display-profile cache
(name, avatar, locale — never tokens), so the cached shell renders logged-in chrome offline and
reconciles with a background call when online; the session cookie rides the first API call
automatically. A user returning within 7 days signs in silently because the session cookie and its
server-side refresh token are still valid (§6.3).

### 7.2 Two-cookie model

| Cookie | Domain | Purpose | Lifetime |
|---|---|---|---|
| Distribution session | `<slug>.app.tellma.com` | Authenticates SPA → its backend; drives instant launch | Persistent, sliding ≥ 7 days, tied to the refresh-token lifetime |
| Server SSO cookie | `identity.tellma.com` | Silent SSO and re-authentication at the authority | "Remember me" → persistent (sliding 14 d); otherwise a session cookie |

The distribution session is independently persistent, so returning to a distribution within 7 days is
silent even without "remember me" at the authority.

### 7.3 Single logout

Two scopes: **local** (end only the current distribution's session) and **global** (end the SSO session
and every distribution session). **Global is the default.** OpenIddict provides RP-initiated
(end-session) logout; global fan-out is built here: a session registry keyed on `sid` records the
distributions with active sessions for a user, and a back-channel emitter POSTs a signed `logout_token`
(OIDC Back-Channel Logout 1.0) to each distribution's `backchannel_logout_uri`, which validates the
token's signature and `sid` and kills its session. `UpdateSecurityStampAsync` and short cookie
re-validation provide defense-in-depth; immediate server-side termination of the SSO cookie via an
`ITicketStore` is deferred (§17). These stop token renewal but do not revoke already-issued
access tokens, which expire within their short lifetime; immediate access removal is the distribution's
tenant-level control (§12). The `sid` registry is backed by SQL initially behind an interface, so
substituting a distributed cache later is a configuration change (§12).

### 7.4 Real-time connections (SignalR)

Distribution backends push real-time updates to the SPA over SignalR — self-hosted in in-proc/on-prem
hosting, via **Azure SignalR Service** in SaaS hosting. The identity server issues nothing for this
channel; it is authenticated entirely by the distribution session cookie (§7.2), and the rules below keep
it consistent with the session model.

**Connection auth and the one browser-held token.** A SignalR connection authenticates once, at the
cookie-authenticated negotiate/connect request; the principal is then cached for the connection's lifetime
— no scheme re-validates an open socket. Self-hosted, the socket is same-origin and rides the session
cookie, so the no-tokens-in-browser rule (§7.1) holds unmodified. With Azure SignalR Service, the
negotiate response hands the browser a **hub-scoped service token** (minted by the backend SDK from the
cookie principal, signed with the service access key, `aud` = the service endpoint). It can reach nothing
but the hub — not the distribution API, not the identity server — and is the single deliberate exception
to §7.1. Claims copied into it are pruned to the subject identifier via `ClaimsProvider` (the default
copies the entire cookie principal into a browser-held token); its lifetime is the SDK default (~1 h). On
browser transports it travels as a query-string parameter — an accepted, documented deviation from the
no-tokens-in-query-strings rule (§4), tolerable only because the token is hub-only and short-lived.

**Session end closes connections.** Because nothing re-authenticates an open socket, every session-ending
event — the back-channel logout handler (§7.3), tenant-level disable, security-stamp invalidation — also
**closes the user's SignalR connections**: per-user close on Azure SignalR (the
`users/{user}/:closeConnections` data-plane API), a tracked user→connection abort self-hosted. As a
backstop, `CloseOnAuthenticationExpiration` is enabled (honored by the Azure SignalR SDK, which embeds the
expiry in the service token so the service enforces it), so no connection outlives its session ticket;
reconnection re-runs negotiate, re-checking the session cookie and current policy. Token expiry alone is
not a bound — an established WebSocket is not closed when the service token expires.

**Thin events.** Pushes carry change notifications, never the data itself: the client re-fetches through
the HTTP API, where tenant permissions are resolved fresh and `acr`/`auth_time` are evaluated on a current
token (§9.2–§9.3). The hub is therefore never a step-up surface — sensitive operations are HTTP API calls
only — and a connection that outlives a session or policy change leaks at most an event signal, never
data.

## 8. Authentication methods

### 8.1 Passkeys (primary)

Passkeys (WebAuthn/FIDO2) are the primary login: phishing-resistant, origin-bound, MFA-grade, native to
Identity in .NET 10. **Roaming hardware security keys are passkeys** and are supported with no extra
work; the ceremony and API are identical to platform authenticators. The passkey **RP ID is the server
origin**, so in standalone mode a user enrolls one passkey and signs in to every distribution (login
always happens at the authority). The ceremony is driven by the Identity APIs
(`MakePasskeyCreationOptionsAsync` / `MakePasskeyRequestOptionsAsync` / `PerformPasskeyAttestationAsync`
/ `PerformPasskeyAssertionAsync`; `AddOrUpdatePasskeyAsync`) from our own pages; only the framework's
scaffolding is Blazor-specific. The engine's own sign-in service — not `PasskeySignInAsync` — completes
the sign-in, so the method evidence behind `acr`/`amr` (§9) is stamped on the session. `ServerDomain` (RP ID) is set explicitly and the
`Host` header validated. Conditional-UI (autofill) sign-in is offered where available, always alongside
an explicit "Sign in with a passkey" button. Credentials are enrolled as discoverable resident keys
(`IdentityPasskeyOptions.ResidentKeyRequirement = "required"`) so a user can be selected by passkey
without first typing an identifier.

A **device-bound** authenticator — a roaming hardware security key, or a platform authenticator whose
credential cannot be synced — raises a credential to the `aal3` tier (§9); a synced passkey is `aal2`.
A credential is device-bound when it is **not backup-eligible** (`UserPasskeyInfo.IsBackupEligible ==
false`); the backup-*state* flag alone would misclassify a syncable passkey that has not yet synced. The
flag is **self-asserted by the authenticator**, so without attestation it separates device-bound from
synced but does not by itself prove a hardware-protected, non-exportable key; a **fully substantiated
NIST AAL3 therefore requires attestation-statement validation** against an allowlist of hardware
authenticator models (via the `IdentityPasskeyOptions.VerifyAttestationStatement` hook and
`fido2-net-lib`). Attestation is **off** by default (Identity's default) and deferred until a customer
requires it (§17); until then `aal3` reflects the self-asserted device-bound signal.

### 8.2 Method catalog

| Method | Role | Notes |
|---|---|---|
| Passkey (incl. hardware keys) | Primary | AAL2 (synced); device-bound targets AAL3 (§9, attestation deferred) |
| Email one-time code | Recovery and bootstrap; universal device floor | Custom single-use provider (§8.3) |
| External login (Google, Microsoft) | Alternative primary | Linked by verified email + ownership (§8.4). Sign in with Apple is out of scope |
| TOTP authenticator | Second factor on password | Enrollment (server-rendered QR + one-time recovery codes) ships now; the sign-in challenge arrives with password sign-in (§17) |
| Password | Optional, off by default | The enable flag gates the reset flows; the sign-in surface is deferred (§17). Lockout and rate limiting apply |

Passwords are not offered by default: a passkey-first system with an email-code recovery path does not
need a standing, phishable, reusable secret. SMS is not offered (restricted by NIST, discouraged by
Microsoft). A passkey alone already satisfies MFA.

### 8.3 Email one-time codes

Codes are **single-use, short-lived (10 min), rate-limited, and bound to the requesting session**. The
built-in email/phone token provider is TOTP-based and allows replay within its window, so a custom token
provider enforces single-use and expiry, delivering via the configured `IEmailSender`.

### 8.4 External-login account linking

External logins are linked by the provider's stable subject `(LoginProvider, ProviderKey)`, never
silently by email. Auto-merging by matching email is the pre-hijacking attack class; linking requires
`email_verified == true` **and** proof of ownership of the local account. Within the invitation flow the
single-use invitation link is itself the proof of email possession, so an invited user may link Google
or Microsoft immediately without an additional code (§10.1). Microsoft-as-social uses
`AddMicrosoftAccount`.

### 8.5 Coverage

Passkeys are effectively universal on modern platforms, including cross-device (scan a QR with a phone)
and roaming security keys. The universal floor for any device is the email one-time code. The narrow
gaps (locked-down machines with Bluetooth and USB blocked; Firefox on Linux for cross-device) are
covered by the email-code path and by admin-assisted recovery (§10.3).

## 9. Authentication policy and assurance levels

Authentication strength is expressed as an **Authentication Context Class Reference (`acr`)**, tiered by
assurance level, with the methods actually used recorded in the **Authentication Method Reference
(`amr`)**. The private tier scheme:

```
urn:tellma:acr:aal1   # single factor (baseline)
urn:tellma:acr:aal2   # two distinct factors, or one multi-factor authenticator (a passkey satisfies this)
urn:tellma:acr:aal3   # phishing-resistant, device-bound (non-synced); a fully substantiated NIST AAL3 also needs attestation (§8.1)
```

`amr` is derived from the actual event (device-bound passkey → `["hwk","user"]`; synced passkey →
`["swk","user"]`; password + TOTP → `["pwd","otp","mfa"]`; email code → `["otp"]`); `acr` is the highest
tier the event satisfies. Because attestation is off (§8.1), the device-bound vs. synced split — and thus
the `hwk`/`swk` value and the `aal3` tier — rests on the authenticator's self-asserted
backup-eligibility flag.
The server also accepts the `phr` and `phrh` inputs defined by OpenID Connect EAP ACR Values 1.0: `phr`
(phishing-resistant) maps to the passkey tier (`aal2`), `phrh` (phishing-resistant hardware) to `aal3`.

### 9.1 Ownership and transport

The server is tenant-agnostic and stores no per-tenant or per-user policy. **The distribution owns
authentication policy** (configured by tenant admins in the distribution, per tenant and overridable per
user). On each sign-in the distribution communicates two orthogonal constraints:

- **Required assurance** via the standard `acr_values` (and `max_age` for recency).
- **Allowed methods** via **`tellma_allowed_methods`** — a Tellma-defined request parameter (no standard
  OIDC equivalent exists) carrying a space-delimited allow-list drawn from a fixed method vocabulary
  (`passkey`, `email_code`, `totp`, `password`, `google`, `microsoft`). This is how a tenant admin
  disables, say, `google` for their users.

Both constraints travel inside the **Pushed Authorization Request (PAR)** — pushed server-to-server and
referenced by an opaque `request_uri` — so a user cannot tamper with them in the browser URL. The
authorization controller reads `tellma_allowed_methods` from the pushed request, offers only those methods
in the login UI, and **enforces the allow-list at the authority** — at the initial authorization and again
at every refresh — so a method a tenant disables stops minting tokens within one access-token lifetime
(§9.4). Enforcement lives here, not at the resource server, because `amr` (RFC 8176) is deliberately
coarse: its registered values name authenticator *properties*, not product methods, so a single `otp`
covers both an authenticator-app TOTP and an email code (which is not even RFC 4226/6238 "otp") and cannot
carry the allow-list's granularity. `amr` is emitted for audit and treated as informational, never as an
authorization input; the backend's per-request re-check rides on **`acr`** and `auth_time` (§9.2), the
standardized assurance signals. Where a distribution genuinely needs the resource server to enforce methods
per request, the authority emits the concrete methods in a dedicated claim in the allow-list vocabulary — a
purpose-built claim, since RFC 8176 `amr` is too coarse to reuse — which the backend checks by
set-membership. Global server configuration defines the method catalog and what each tier requires;
distributions select from and constrain that catalog but do not redefine tier semantics.

### 9.2 Enforcement and verification

The server offers only the allowed methods, drives step-up until the achieved assurance meets the
requested `acr`, and emits `acr`, `amr`, and `auth_time` as signed claims (returning
`unmet_authentication_requirements` if it cannot satisfy the request). The distribution verifies the
returned `acr` and `auth_time` on each request — because `acr_values` is only advisory, the returned `acr`
is always re-checked, never assumed; `amr` is carried for audit, not policy (§9.1). For **external logins**, a social sign-in is
`aal1`; where that is below the required tier the server steps the user up with a local factor before
issuing tokens.

### 9.3 Step-up for sensitive operations

Which operations are sensitive is fixed in the distribution's source code — a small, deliberately chosen
set, not tenant configuration. When an under-assured request arrives, the distribution returns
`401 WWW-Authenticate: Bearer error="insufficient_user_authentication", acr_values=…, max_age=…`; the
client re-authorizes with those parameters; the server steps the user up and issues a token with the
higher `acr` and a fresh `auth_time`. The server provides the authorization-server side; the `401`
challenge is distribution middleware.

**Step-up is non-destructive, without popups or embedded windows** (which behave poorly in an installed
PWA). Two mechanisms always apply. Because the sensitive-operation set is small and fixed, the distribution
**checks `acr` when the operation is opened or initiated and steps up before any data is entered**, so
nothing is in flight to lose; and where a step-up still requires a full redirect to the identity UI, the
SPA **persists the unsaved form as a draft** before navigating and restores it on return. A third, faster
path — passkey step-up completing **in-page** (a WebAuthn assertion posted to the BFF, no navigation) — is
available **only in in-proc mode**, where the authority shares the distribution's origin. It is impossible
in standalone mode: the passkey's RP ID is the authority origin, which is not a registrable-domain suffix
of the distribution origin, so the browser will not release an assertion for it from a distribution page;
standalone step-up therefore uses the redirect path. (WebAuthn Related Origin Requests could later extend
in-page step-up to standalone mode; deferred, §17.) The real-time channel is excluded from step-up
entirely: a SignalR connection authenticates only at connect and its cached principal never observes a
later step-up, so sensitive operations are HTTP-only and pushes carry thin events (§7.4).

### 9.4 Live policy changes

A tightened policy takes effect at the next token refresh — the short access-token lifetime is the
re-evaluation point, and the server re-evaluates the requested constraints on each authorization.
Immediate enforcement (lock, compromise) uses revocation plus global logout (§7).

## 10. User and credential lifecycle

All user-facing operations are bulkified: signatures accept and return collections; inviting many users
is one call that returns per-user results and hands the whole batch to email in a single send.

### 10.1 Invitation

1. A tenant admin creates user records (email + preferred language) in the distribution.
2. The distribution backend calls the server's **bulk invite** API (`tellma_identity` scope), which
   creates-or-gets each user by email, assigns each a `sub`, sends each a localized single-use
   invitation link, and returns each user's `sub` with a per-user status: `Invited`, `Reinvited` (an
   existing credential-less or orphaned user), or `Active` (already holds a credential; no link is sent).
3. The user opens the link (which proves control of the mailbox):
   - New user → a passkey-setup page → return to the distribution. The user may instead link Google or
     Microsoft immediately (verified, matching email — the link is the ownership proof).
   - Existing user, new only to this distribution → straight in; an existing passkey already works.

The distribution records the tenant-membership mapping (`sub` ↔ its user/roles); the server records only
the global user. The invitation link is the passwordless bootstrap, so no password is ever required to
onboard.

### 10.2 Service accounts

A tenant admin creates a service account in the distribution; the distribution backend calls the
server's create-service-account API, which registers a confidential client and returns `client_id` and
a strong secret **once**. The server does not tag the client with a tenant; the distribution records the
association. If the secret is lost: delete and recreate. Secrets do not
expire and are **not** force-rotated — rotation is at the tenant's discretion (§13). Clients that want
rotation-free strong authentication may use `private_key_jwt` or mTLS instead of a shared secret.

### 10.3 Recovery

- **Passkey / credential loss (self-service):** the user requests recovery and receives a single-use
  email code, signs in once, and enrolls a new passkey. Enrolling a second passkey lets most
  device-loss events self-recover with no email step.
- **Admin-assisted (email also lost):** an operator issues a **Temporary Access Pass** — a short-lived
  (≤ 1 h), single-use code shown to the operator and conveyed out-of-band. The user redeems it on the
  recovery page and receives a short-lived enrollment-only context — never a signed-in session — whose
  only exit is enrolling a new passkey; the pass cannot be used for normal sign-in. Issuance is audited
  and identity-proofing-gated.
- **Password reset (only when passwords are enabled):** self-service via a single-use email link with
  enumeration-safe responses.

### 10.4 Bootstrap

**Deployed instances (shared, or standalone/on-prem in-proc).** The server seeds a single break-glass
administrator; provisioning generates a one-time setup token, delivers it through a secure channel (Key
Vault secret / provisioning output), and configures the server with only the token's SHA-256 hash. The
administrator redeems the token on a setup page and enrolls a passkey — the token is single-use by
state, dead the moment the administrator holds any credential — then administers everyone else.

**Local development.** On first run the in-proc server seeds a dev admin identity (`admin@localhost`), and
the distribution seeds a matching tenant and admin role on the same `sub` — both are required, since the
server only authenticates and the admin needs distribution permissions to act. The first sign-in is the
ordinary email-code path — the code lands in the Development email sink (§10.6) — after which the admin
enrolls a passkey from Account & Security; the ceremony, session, and later assertion are identical to a
deployed instance.

Everything else is the deployed path: the admin invites additional users, and each link is read from the
sink (§10.6) and opened in a separate browser or incognito window to enroll that user's passkey. Passkeys
are discoverable resident credentials (§8.1) held by the OS platform authenticator, so one dev machine
holds every test user's passkey and the sign-in page offers a picker across them. A local database reset
re-seeds the admin and requires re-enrolling passkeys; orphaned platform credentials are simply replaced.

### 10.5 Lifecycle states

A user removed from their last distribution is **not deleted**: it is marked `orphaned`, preserving audit
history and credentials for painless re-invitation. States: `active` → `orphaned` → `disabled` →
`purged`. A retention policy purges `orphaned` users after a configurable window; a data-erasure request
purges immediately. Orphaned users cannot obtain tokens.

### 10.6 Local development

In the Development environment the only security-relevant change is the `IEmailSender` implementation: it
writes invitation and recovery links to a sink (console/log, or a local SMTP catcher such as smtp4dev)
instead of sending mail. Developers and E2E tests read the link from the sink. The invite API's response
is identical to production — status plus each user's `sub`, and **never** the link, in any environment —
so the email-ownership proof the link represents is never exposed to the caller.

## 11. Identity Server UI

### 11.1 Framework and branding

The auth-flow UI is server-rendered ASP.NET Core **Razor Pages** — the fit for redirect-based flows and
security-sensitive pages, with no SPA. Pages are kept on-brand by importing the compiled CSS-variable
stylesheet emitted by `@tellma/core-ui-tokens` (framework-agnostic custom properties, the same tokens
the Angular apps use) plus a small server-specific stylesheet and minimal vanilla JavaScript for the
WebAuthn ceremony. Login resolves branding (logo, color tokens) through a `BrandingResolver` seam —
`client_id → distribution` now, a tenant hint later — defaulting to Tellma branding; because branding is
a token set, adding per-tenant branding later is data, not code. When a user deep-links from a
distribution to these pages, the distribution passes `ui_locales` (and the server reads the stored
`locale` claim) so pages render in the user's current language; deeper formatting stays the
distribution's concern.

### 11.2 Pages

Sign-in (passkey autofill + explicit passkey button + email code + the configured external providers,
offering only the request's allowed methods; it doubles as the step-up / re-authentication surface);
passkey registration; email-code entry; external-login start/callback; password forgot/reset (only when
enabled); consent (skipped for first-party clients, shown for third-party); device verification;
invitation accept; Temporary Access Pass recovery; break-glass setup; logout and logged-out; access
denied; error; and self-service **Account & Security** (the profile fields the server owns — name,
locale — plus passkey management, authenticator-app enrollment with one-time recovery codes, and active
sessions with "sign out everywhere").

The distribution surfaces Account & Security as a "Sign-in & security" tab inside its own account area
that deep-links to these pages over SSO (no re-login) with a `return_url`, so a single-distribution user
sees one unified settings experience.

### 11.3 Management API surface

The server exposes management as APIs; there is no standalone admin SPA.

- **Distribution-facing (called by the distribution backend, M2M):** **invite users** (bulk) and
  **create / get / delete service account**. These are the only server APIs a tenant admin's workflows
  need. The server does not list users or service accounts by tenant and does not reset user passwords
  for a distribution admin — user and service-account listings are served from the distribution's own
  tables, and password reset is self-service (§10.3).
- **Operator-facing (control plane, restricted):** global user lookup and **Temporary Access Pass**
  issuance (§10.3). Absent in in-proc mode. The rest of the operator surface is deferred (§17).

## 12. Storage, scale, and performance

Backing store is SQL Server / Azure SQL: OpenIddict's four tables (applications, authorizations, scopes,
tokens) alongside the Identity tables. The shared server holds all users across all distributions (a
single global directory at 100,000s of users).

The hot path stays off the database: **signed JWT access tokens are validated offline** against the
cached JWKS, so resource servers make no per-request database call; the server's database is touched
only at login, issuance, and refresh. Access tokens are self-contained JWTs, not reference tokens (which
would force a database read per validation). OpenIddict still writes a lightweight metadata entry per
issued token — this, not the access-token payload, is what backs refresh-token rotation and reuse
detection — so token storage stays on and the entries are pruned by the Quartz job below; disabling it
globally would remove rotation/reuse detection, and there is no supported per-type toggle to drop only
access-token entries. Because access tokens are validated offline they are deliberately **not revocable
within their ≤ 10-minute lifetime**: immediate removal of a user's access is enforced by the distribution
at the tenant layer (authoritative for permissions), while authority-side session and refresh termination
(§7) stops renewal.

Optimizations applied now:

- **Token/authorization pruning via `UseQuartz()`** — the single most important operational job, since
  every issued token writes a row. An index leading with `CreationDate` (the prune query's selective
  bound) supports it; it is deliberately unfiltered, because the server never flips a token's status on
  expiry, so expired-but-still-`valid` access tokens are the prune bulk.
- Discovery/JWKS caching (same-process APIs use `UseLocalServer()`); output-cached discovery/JWKS at the
  edge.
- Bulkified user operations; indexed lookups on `sub` and `client_id`.

Interface seams keep a later move to a distributed cache (Redis) a configuration change, not a rewrite:
the `sid` session registry (§7.3) and the rate-limit counters; client metadata is already cached by
OpenIddict's built-in entity cache. The Data Protection key ring is shared across instances (file
system, or Azure Blob + Key Vault, §13) so cookies and Data-Protection-format tokens survive scale-out. Telemetry measures **time spent in SQL
Server I/O** so the decision to introduce Redis is driven by data (§15).

## 13. Keys, secrets, and rotation

Three independent materials:

| Material | Protects | Storage | Rotation |
|---|---|---|---|
| Signing certificate (ES256/RS256) | Token signatures; public half in JWKS | Config-selected: Azure Key Vault (managed identity), machine certificate store, or PFX file | Overlap rotation |
| Encryption certificate | Encrypted refresh tokens / codes; never in JWKS | Same config-selected sources | Overlap rotation |
| Data Protection key ring | Identity cookies, WebAuthn state, DP-format tokens | File system by default; shared Azure Blob encrypted with a Key Vault key when configured | Automatic ring rotation |

**Overlap rotation:** OpenIddict publishes all registered signing keys in JWKS (distinct `kid`) and signs
with the certificate whose `NotAfter` is furthest in the future. To roll: add the new certificate (later
expiry) alongside the old → the server signs with the new one while still publishing the old for
validation; keep the old until every token it signed has expired (sized to the refresh-token lifetime);
then remove it. Encryption certificates follow the same pattern without the public JWKS. Key Vault
integration is application glue (`WEBSITE_LOAD_CERTIFICATES` + thumbprint, or
`Azure.Security.KeyVault.Certificates` + `DefaultAzureCredential`); development/ephemeral certificates
are never used in production. Every Azure dependency is config-gated: an on-prem deployment runs fully
with store/PFX certificates, a file-system key ring, and SMTP email. Rotate signing/encryption certificates on a schedule (~90 days) with expiry
alerts.

Client secrets are hashed at rest. Service-account secrets are **not force-rotated**; when a tenant
chooses to rotate, a second secret can be added for a zero-downtime cutover. Confidential clients may use
`private_key_jwt` or mTLS to avoid shared-secret rotation entirely.

## 14. Security and threat model

| Threat | Mitigation |
|---|---|
| Redirect manipulation | Exact per-client redirect-URI matching (OpenIddict default); no wildcard trust |
| Token theft via XSS | BFF — no tokens in the browser; `HttpOnly`/`SameSite` cookies; CSP; Trusted Types |
| Clickjacking / UI redressing of the identity UI | `Content-Security-Policy: frame-ancestors 'none'` plus `X-Frame-Options: DENY` on every identity-UI response, so login, consent, and passkey ceremonies cannot be framed by a hostile site |
| Phishing | Origin-bound passkeys; no standing password; single-use, session-bound email codes |
| CSRF | `SameSite`; anti-forgery on state-changing endpoints; PKCE + `state` + `nonce` |
| Authorization-code interception | PKCE for all clients; PAR; `iss` in the response |
| Refresh-token replay | Rotation on by default + reuse detection revokes the token family; a short rotation **reuse leeway** (OpenIddict default ~30 s) still tolerates the concurrent/multi-tab refresh race, so a legitimate double-use does not spuriously revoke the family |
| Credential stuffing (if passwords enabled) | Lockout and rate limiting |
| Cross-distribution token replay | Per-distribution audience derived from origin |
| Key compromise | Key Vault + managed identity; short access-token lifetime; overlap rotation; revocation |
| Session fixation | Session regenerated on authentication |
| Account enumeration | Generic, constant-time responses on invite/recovery/reset |
| Back-channel logout abuse | `logout_token` signature and `sid` verification |
| Real-time (SignalR) token exfiltration via XSS | The hub-scoped service token reaches nothing but the hub (`aud` = service endpoint, claims pruned to `sub`), lives ~1 h, and its connections are server-closeable; pushes are thin events, so no data rides the channel (§7.4) |
| Stale real-time connection outliving logout or a policy change | Session-ending events close the user's connections; `CloseOnAuthenticationExpiration` bounds every connection to its session ticket; thin events mean a missed close leaks event signals, not data (§7.4) |
| Policy tampering | Required assurance and allowed methods carried via PAR, not URL parameters |
| Cross-device consent phishing (device flow) | Short-lived, rate-limited, one-time user codes; proximity where available |
| Admin recovery social engineering | TAP single-use/short-lived/proofing-gated/audited |
| External-login pre-hijacking | Verified email + ownership proof before linking |

Assurance tiers correspond to NIST SP 800-63B-4 authenticator assurance levels. NIST AAL3 requires a
hardware-protected, non-exportable key plus verifier impersonation resistance; the `aal3` tier delivers
phishing resistance and device-binding, but a fully substantiated AAL3 additionally requires passkey
attestation (deferred, §8.1), since the WebAuthn backup-state flag it relies on is self-asserted. Data erasure is served by user purge; data residency by the isolated and in-proc modes;
DNS hygiene under `app.tellma.com` remains a hosting control (a hijacked subdomain could host a hostile
relying party even without wildcard trust).

## 15. Operations, observability, and audit

Telemetry uses OpenTelemetry across three signals:

- **Logs** — structured (Serilog), exported to Application Insights / Log Analytics when configured.
- **Metrics** — ASP.NET Core and HTTP-client instrumentation (endpoint latency, throughput), plus a
  dedicated `Tellma.Identity` meter defining identity counters (sign-in attempts, token issuance,
  refresh reuse, email codes, invitations, back-channel deliveries).
- **Traces** — distributed traces with **W3C Trace Context propagation**: the server joins the incoming
  `traceparent` from the distribution rather than starting a new trace, so a user action that begins in a
  distribution, passes through its BFF, and reaches the server is one correlated trace. SQL-client
  instrumentation measures **time spent in SQL Server I/O** (the signal that informs the Redis decision,
  §12).

An **immutable audit event** is emitted for every security-relevant action — login success/failure,
email codes, credential enrollment and removal, step-up, token issuance/refresh/revocation (including
refresh reuse), session creation and termination, back-channel logout delivery, invitation,
service-account and client changes, temporary access passes, and bootstrap events — carrying `sub`,
`client_id`, and the correlation/trace id. Security alerts cover credential-stuffing patterns,
refresh-token reuse, and key/certificate expiry.

Keys and secrets live in the configured source (§13) with scheduled rotation; the server is stateless
and scales horizontally with the shared Data Protection ring and cacheable configuration.

## 16. Testing

Per repository conventions (xUnit v3; integration tests run against a real SQL Server; test layout
mirrors `src`). Projects: `Tellma.Identity.Tests` (unit), `Tellma.Identity.IntegrationTests` (flows and
APIs against real SQL via `WebApplicationFactory`), `Tellma.Identity.E2E` (UI via Playwright).

- **Unit:** `acr`/`amr` derivation; allowed-methods and step-up policy evaluation; return-URL
  validation; options validation.
- **Integration (real SQL):** every protocol flow end-to-end — Authorization Code + PKCE + PAR, Client
  Credentials, Device Grant, Refresh with rotation and reuse detection (a reused token revokes the
  family; a security-stamp change or a newly disallowed method stops renewal), Token Exchange
  (down-scope only), and back-channel logout — plus discovery parity across the standalone and in-proc
  compositions, seeding idempotency, migrations, and the invitation and service-account APIs.
- **Security / negative:** front-channel authorization without PAR, PKCE downgrade, redirect-URI
  mismatch, foreign `resource` requests, invalid client secrets, and scope-widening at exchange are all
  rejected; JWKS publishes only asymmetric keys; the invite response never carries the link.
- **UI (Playwright):** passkey registration and sign-in via the CDP virtual authenticator, driven
  through email-code sign-in and sign-out with an in-process email-capture harness; plus an assertion
  that the brand token stylesheet loads and its variables resolve in the rendered page.

## 17. Out of scope and deferred

**Out of scope:** distribution-side authentication validation and the Angular auth library; SAML /
enterprise SSO federation; Entra External ID as the authority; Sign in with Apple; the tenant/operator
admin UIs themselves (this spec defines the APIs and surfaces, not the screens); the landing page's
cross-distribution discovery; the distributions' real-time hub design beyond the session-integration
rules in §7.4.

**Deferred (seams kept in place):** passkey attestation / authenticator restriction via `fido2-net-lib`
(§8.1), which is also what substantiates a full NIST AAL3 claim (§9); WebAuthn Related Origin Requests to
enable in-page passkey step-up in standalone mode (§9.3), gated on browser support; **password sign-in**,
and with it the TOTP second-factor challenge, recovery-code redemption, and the breached-password check
(§8.2 — the enable flag already gates the reset flows); the operator surface beyond user lookup and
Temporary Access Pass issuance — user disable/enable/purge, last-resort credential reset,
client-registration/scope/key administration APIs, audit query, and consented impersonation (§11.3); an
`ITicketStore`-backed SSO cookie for immediate server-side session termination (§7.3); per-tenant login
branding (the `BrandingResolver` seam, §11.1); enterprise Entra federation as a social provider; a
distributed-cache (Redis) backing for the `sid` registry and rate-limit counters (§12), gated on the
SQL-I/O telemetry (§15); adopting OpenIddict's native back-channel logout when it ships.

## 18. References

**Standards:** OAuth 2.0 Security BCP (RFC 9700); OAuth 2.0 for Native Apps (RFC 8252); Device
Authorization Grant (RFC 8628); Token Exchange (RFC 8693); PKCE (RFC 7636); Resource Indicators
(RFC 8707); AS Issuer Identification (RFC 9207); Pushed Authorization Requests (RFC 9126); mTLS
certificate-bound tokens (RFC 8705); Step-Up Authentication Challenge (RFC 9470); `amr` values
(RFC 8176); JWT access tokens (RFC 9068); OpenID Connect Core; OIDC Back-Channel Logout 1.0;
OpenID Connect EAP ACR Values 1.0 (`phr`/`phrh`); LoA Profiles registry (RFC 6711); NIST SP 800-63B-4.

**Implementation references:** OpenIddict documentation (token storage; signing/encryption credentials;
mutual-TLS authentication). The mTLS-not-DPoP position reflects OpenIddict 7.x, which implements RFC 8705
mTLS token binding but not RFC 9449 DPoP.

**Related docs:** [Identity Server: Duende vs OpenIddict](../research/identity-server-duende-vs-openiddict.md).
