# Spec: Amendments to Implemented Specs

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Specs 0001–0009 are implemented and frozen. Their text is the record of what shipped and is never
edited. When a later design needs one of them to change, the change is written here as an
amendment. This spec is the one home for those amendments.

It has one part per target spec. Each part carries its own Testing, Definition of done, Decisions
record and Review flags, so it is implemented and merged on its own:

- **Part A — identity server (spec 0003).** It blocks spec 0011: the distribution host cannot ship
  without it.
- **Parts B–E — the component library (specs 0002, 0004, 0005, 0006).** Each is needed when its
  first consumer among specs 0023–0035 is built.
- **Part F — Queryex (spec 0008).** It is needed by the data access layer (spec 0012), the
  permission evaluator (spec 0014), the report engine (spec 0033) and the client's
  `queryex/discover` (spec 0029 §9).

Specs 0001, 0007 and 0009 need no amendment.

**Other specs that carry parts of this design**

- spec 0011 §5.5, §5.7, §5.9 — the distribution side of Part A: `/bff/login` forwards
  `ui_locales`, the in-proc seed entry, `IdentityInfo.AccountUrl`.
- spec 0016 §11.9 — the MCP surface's use of §A.2–§A.4 and §A.6.
- spec 0018 §2, §3.3 — the identity client on the `v1` routes; `ExistingOnly` on sandbox tenants.
- spec 0023 §3, §4 — `tm-icon`, `TmKeys` and `tm-chip`, which §C.10, §C.12 and §E.2 build on.
- spec 0024 §7.1, §7.3, §7.6, §10.5 — `@tellma/core` fills the Part B and Part D seams: the
  translations loader, the theme seed, `TM_TIME_ZONE` and `TM_BLOB_FETCHER`.
- spec 0025 §2.6 — the session sweep that calls `clearAll({ keep })` (§D.5).
- spec 0012 §11.1–§11.2 — the host of Part F: the schema's `TreeLevels` and `CalendarMap`, and
  the amendments the data access layer relies on.
- spec 0012 §11.4, spec 0015 §5.2 — the refusal of level paths in an entity `Select` (§F.9).
- spec 0013 §8.2 — the `uq` month table that §F.13 reads.
- spec 0029 §9, spec 0033, spec 0035 §6.3 — the consumers of Part F: `queryex/discover`, the report
  engine and the dashboard read-through.

## Goals / Non-goals

**Goals**

- **Part A.** Give the distribution host, the MCP surface, the users stack and the client what
  they need from the identity server. That is a `Distribution` seed kind, per-tenant MCP
  resources, client ID metadata documents, interim native clients, control-plane audiences, the
  `tellma_kind` claim, `existingOnly` invites, versioned management routes, an account entry URL
  that honours `return_url`, and the sign-in language from the authorization request.
- **Part B.** Breakpoints, the System theme, a translations seam with live direction, a
  closed-list multi-select with option groups, the prefix lint's scope, and touch sizes on phones.
- **Part C.** The grid features a list page needs — row activation, an external active row, sort
  state, header actions, select-all across pages, row classes, a stable gutter, scroll-to-row,
  remembered tree expansion, a published key map — and a richer, data-driven menu.
- **Part D.** A public overlay entry point; the drawer, alert-dialog roles and live direction on
  modals; fixed popover sizes; date-time, time, relative, month, quarter, year and weekday formats;
  a full client-cache sweep; cancellable blob fetches; image and button support for the new avatar
  and icon components.
- **Part E.** Two-line picker options, a multi-entity picker, a page context for picker pages, and
  two-line options in grid cells.
- **Part F.** The Queryex changes the data access layer, the permission evaluator and the report
  engine need: the seven amendments moved from spec 0012 (§F.1–§F.7), grouping sets, level
  navigations, omitted parameters, constant statements, parameter binding, `'uq'` through a month
  table, `Exists` and `ComparedWith` (§F.8–§F.15).

**Non-goals (explicitly out of scope)**

- **Editing specs 0001–0009.** Their text stays as merged; an amendment here supersedes only the
  section it names.
- **New library components** — spec 0023 owns them. This spec changes existing entry points,
  including promoting existing code into a new one (§D.1).
- **How consumers use the seams** — each consumer spec owns its use; this spec names the consumer.
- **Part A:**
  - service-account creation from a tenant: the API exists in spec 0003, and spec 0018 §3.8's
    `issue-credentials` action is the tenant side;
  - distribution self-registration, and the operator surface beyond spec 0003 §11.4;
  - the MCP tools, request filters and rate limits (spec 0016 §11);
  - metadata documents for clients with a shared secret: only `none` and `private_key_jwt` are
    accepted;
  - the Manage pages' content, and every deferral of spec 0003 §17.
- **Parts B–E:**
  - splitting the grid's readonly and editable code (Part C flag 6);
  - grid footer rows and a row-selectable predicate (Part C decision 9);
  - a second submenu level, and a time-of-day type without an instant;
  - tab prerendering (Part D decision 11).
- **Part F:**
  - a language-level list parameter, and `descendantOf` over a table-valued parameter: key-set
    restrictions cover every consumer (spec 0012 §11.2);
  - window functions for running totals (spec 0033 reserves `running`);
  - a new `QueryexLanguage.Version`: every item is additive (§F.0).

## How to read an amendment

Every amendment has a letter-dotted number: the part's letter, then its place in the part (§A.1 …
§A.10, §B.1 …). Two citation forms appear:

- **"0004 §8.2"** names a section of a frozen spec.
- **"spec 0030 §6.2"** names a section of a designed spec.

Each amendment opens with three labels in one short paragraph:

- **Target** — the frozen spec's section that the amendment replaces or extends. Everything else
  in that spec stands.
- **Requested by** — the specs whose design needs it.
- **Why** — the need, in a few words.

Then it states the change, in this order:

- **The delta** — a sketch. A changed member's comment shows its old shape (`// old: …`).
- **The rules** — behaviour, then accessibility and RTL where relevant.
- **Closing labels** — **Compatibility**, **Tests** and, in the library parts, **Budget**.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted. A member prefixed `ɵ` is
internal to its package.

**Vocabulary**

- **Frozen spec** — one of specs 0001–0009, implemented and never edited.
- **Amendment** — one numbered change to a frozen spec.
- **Consumer** — a spec whose design needs an amendment.
- **Entry point** — the primary `@tellma/core-ui` (written `.` in budget tables) or a secondary
  entry point such as `@tellma/core-ui/grid`.
- **Ceiling** — an entry point's gzip budget in KB (`budgetsInKb` in the library's
  `package.json`); it rises only with a `budgetNotes` entry.
- **Showcase** — the library's story app, the target of its Playwright suites.

## Part A — Identity server (spec 0003)

Part A amends spec 0003, the platform's shared OpenID Connect authority. Four consumers need
changes to it that no tenant-side spec can own: the distribution host (spec 0011), the web API and
its MCP surface (spec 0016), the users stack (spec 0018) and the client (specs 0024, 0027).

Every rule of spec 0003 that Part A does not name stands. That includes required PKCE S256, `iss`
in authorization responses, refresh-token rotation with reuse detection, exact redirect-URI
matching, no wildcard trust, and no CORS policy on the server's endpoints.

The order of the sections is the implementation order:

1. **§A.1** — the `Distribution` seed kind unblocks the reference distribution's in-proc identity
   mode.
2. **§A.2, §A.3** — per-tenant resources and client ID metadata documents unblock the MCP surface.
   The interim native clients of §A.4 bridge the gap until §A.3 ships.
3. **§A.5** — the control-plane grants unblock the admin surface.
4. **§A.6** — the `tellma_kind` claim is optional.
5. **§A.7** — `existingOnly` unblocks sandbox invites.
6. **§A.8** — the versioned management routes ship before spec 0018's identity client, the API's
   first distribution caller.
7. **§A.9, §A.10** — the account entry URL and the sign-in language ship before the client's
   session menu (spec 0027 §10.1) and sign-in (spec 0024 §8.3).

**Terms used in Part A**

- **BFF client** — a distribution's confidential web client, named by its slug (`acme`). BFF
  stands for backend for frontend.
- **Document client** — a public client whose `client_id` is the `https` URL of its own metadata
  document (§A.3).
- **Distribution-origin list** — the public origin of every distribution, kept by onboarding. The
  interim native clients (§A.4) and the control-plane client (§A.5) hold it as `Resources`, and
  document clients are checked against it (§A.3).

**Placement**

| Project | Location | Change |
|---|---|---|
| `Tellma.Identity` (the engine) | `src/apps/Tellma.Identity/` | the code of §A.1–§A.3 and §A.5–§A.10 |
| `Tellma.Identity.Migrations` | `src/apps/Tellma.Identity.Migrations/` | none: no table changes; metadata documents are cached in memory |
| `Tellma.Identity.Web` | `src/apps/Tellma.Identity.Web/` | configuration only (§A.3, §A.4, §A.5) |
| Tests | `test/apps/Tellma.Identity.Tests`, `.IntegrationTests`, `.E2E` | Part A — Testing |

No new package is referenced. The metadata-document fetch uses the engine's existing
`IHttpClientFactory` registration, with a named client `Tellma.Identity.ClientMetadata`.

### §A.1 The `Distribution` seed client kind

**Target:** 0003 §5 (seeded clients). **Requested by:** spec 0011 §5.7. **Why:** a standalone
deployment seeds its own BFF and service clients from configuration, with stable secrets.

A new seed kind, `Distribution`, registers a distribution's two clients from one configuration
entry, exactly as onboarding would.

```csharp
// Tellma.Identity.Options
public enum TellmaIdentitySeedClientKind { Cli, Native, ControlPlane, Distribution }

public sealed class TellmaIdentitySeedClientOptions
{
    public string? ClientId { get; set; }                     // Distribution: the slug
    public string? DisplayName { get; set; }
    public TellmaIdentitySeedClientKind Kind { get; set; }
    public IList<string> RedirectUris { get; }               // ignored for Distribution: derived from Origin
    public string? ClientSecret { get; set; }                 // Distribution: the BFF client's secret
    public IList<string> Resources { get; }                  // Distribution: Origin is added implicitly
    public bool RequireConsent { get; set; }
    public string? Origin { get; set; }                       // Distribution only; absolute, path-less
    public string? BackchannelLogoutUri { get; set; }         // Distribution only; default {Origin}/bff/backchannel-logout
    public string? ServiceClientSecret { get; set; }          // Distribution only; the <slug>-svc secret
}
```

An entry produces the two applications that spec 0003 §5's onboarding automation registers, and
nothing more:

| Application | Type | Grants | Redirect URIs | Resources | Scopes | Secret |
|---|---|---|---|---|---|---|
| `<slug>` | confidential | `authorization_code`, `refresh_token` | `{Origin}/signin-oidc`, `{Origin}/signout-callback-oidc`; back-channel logout at `BackchannelLogoutUri` | `{Origin}` plus `Resources` | `openid profile email offline_access tellma_api` | `ClientSecret` |
| `<slug>-svc` | confidential | `client_credentials` | — | `{Origin}` plus `Resources` | `tellma_identity`, `tellma_api` | `ServiceClientSecret` |

- **Idempotent.** Seeding on every start converges. An entry whose values are unchanged is a
  no-op, and a changed secret, origin or redirect list is applied.
- **Never destructive.** The seeder never deletes an application. It never touches one it did not
  create (`TellmaClientProperties.FirstParty`).
- **Validated at startup.** `ClientId` is lowercase, letter-first, at most fifteen characters.
  `Origin` is absolute and `https` (`http` only in Development). `ClientSecret` and
  `ServiceClientSecret` are non-empty outside Development. A failure names the entry.
- **Consumers.** Spec 0011 §5.7 writes one entry from `Tellma:Identity:ClientSecret`,
  `ServiceClientSecret` and `PublicOrigin`. A per-boot secret in Development is the host's choice,
  never the seeder's.

### §A.2 Per-tenant resources under a granted origin

**Target:** 0003 §6.2. **Requested by:** spec 0016 §11.2, §11.9. **Why:** one MCP audience per
tenant, with no per-tenant registration.

A client granted an origin may also request one fixed path under it per tenant:
`<origin>/<tenant id>/mcp`.

```csharp
// Tellma.Identity.Services
public interface IResourceGrantEvaluator
{
    bool IsGranted(IReadOnlySet<string> grantedResources, string requestedResource);   // exact, or <origin>/{int}/mcp
}
```

- **The rule.** A requested `resource` (RFC 8707) is accepted in two cases:
  - it equals a resource the client holds; or
  - it is exactly `<granted>/<id>/mcp`. Here `<granted>` is a granted resource that is an origin
    (scheme, host, port; no path), and `<id>` is a positive decimal integer without leading zeros.

  The accepted value is copied into `aud` verbatim.
- **Nothing else matches:** no other path, no trailing slash, no query, no wildcard, no per-tenant
  registration. The audience space stays enumerable from the origin grants alone.
- **Where it applies.** Wherever spec 0003 validates `resource`: the authorization, token (code
  exchange, refresh, client credentials, device code) and token-exchange paths. A refused resource
  is `invalid_target`, as before.
- **Refresh narrows, never widens.** A refresh's `resource` must be a subset of the original
  authorization's resources, with the pattern evaluated against those. A refresh may narrow the
  audience to one tenant's resource. It never widens it to another tenant or to the origin.
- **Isolation.** Tenant 18's MCP endpoint refuses a token whose `aud` is `<origin>/17/mcp` by
  audience alone. Membership at the resource server remains the second wall.

**Example —** a client granted `https://acme.example.com` may request
`https://acme.example.com/17/mcp`. It is refused `…/017/mcp`, `…/17/mcp/`, `…/17/other` and
`…/0/mcp`.

### §A.3 Client ID metadata documents

**Target:** 0003 §5, §6.2 and the discovery document. **Requested by:** spec 0016 §11.3, §11.9.
**Why:** hosted agents, IDE agents and one-off MCP clients cannot pre-register.

Such a client becomes a document client: a public client whose `client_id` is an `https` URL. The
server fetches the client's metadata from that URL. This is the OAuth client ID metadata document
specification, which the MCP authorization profile requires.

```csharp
// Tellma.Identity.Services
public interface IClientMetadataDocumentResolver
{
    Task<ClientMetadataDocument?> ResolveAsync(Uri clientId);   // null: not a document client
}

public sealed record ClientMetadataDocument(
    Uri ClientId, string? ClientName, IReadOnlyList<Uri> RedirectUris, string TokenEndpointAuthMethod,
    Uri? JwksUri, DateTimeOffset FetchedAt, DateTimeOffset ExpiresAt);

public sealed class ClientMetadataDocumentOptions            // Tellma.Identity.Options; bound from TellmaIdentity:ClientMetadata
{
    public bool Enabled { get; set; } = true;
    public int MaxBytes { get; set; } = 5120;                            // the largest document body accepted
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);     // per fetch
    public TimeSpan MinCache { get; set; } = TimeSpan.FromMinutes(1);    // MinCache..MaxCache clamps the HTTP cache lifetime
    public TimeSpan MaxCache { get; set; } = TimeSpan.FromHours(24);
    public int FetchesPerHostPerMinute { get; set; } = 10;               // the per-host rate limit
}
```

The options are a nested member of the engine's root options, beside `Lifetimes`, `Keys` and
`Seed`. An in-proc host sets them in code where spec 0011 §5.7 configures the engine.

- **Recognition.** A `client_id` that parses as an absolute `https` URL with a host and no
  fragment is a document client. Anything else is looked up as a registered client, so registered
  clients are unaffected.
- **Fetch guards.** A guard failure is `invalid_client` and an audit event.
  - **Address.** `https` only. The host is not an IP literal and does not resolve to a loopback,
    link-local, private or multicast address. The check runs after resolution, per address.
  - **Response.** Redirects are not followed. `Content-Type` is `application/json`, and the body
    is at most `MaxBytes`.
  - **Limits.** The request times out at `Timeout`, and fetches are rate-limited per host.
- **Validation.**
  - The document's `client_id` equals the URL exactly.
  - `redirect_uris` is non-empty, and every entry is absolute.
  - `token_endpoint_auth_method` is `none` or `private_key_jwt`. The latter requires a `jwks_uri`
    on the document's origin; inline `jwks` is refused.
  - `grant_types`, when present, contain only `authorization_code` and `refresh_token`.
- **Redirect matching.** Exact, as spec 0003 §5, with one relaxation for native clients (RFC 8252
  §7.3): a documented loopback redirect (`http://127.0.0.1` or `http://[::1]`, any path) matches
  any port.
- **Caching.** The document is cached per its HTTP caching headers, clamped to `[MinCache,
  MaxCache]`. After a fetch failure, a cached copy is served until it expires; then the client is
  refused.
- **What a document client gets.**
  - **Tokens.** PKCE S256 is required. Scopes are at most `openid`, `profile`, `email`,
    `offline_access` and `tellma_api`. Refresh tokens rotate, as spec 0003 grants public native
    clients.
  - **Resources.** `resource` is evaluated by §A.2's `IsGranted` against the distribution-origin
    list. An origin in that list, and §A.2's per-tenant pattern under it, are accepted; nothing
    else is. Membership at the resource server is the isolation, exactly as for spec 0003 §5's
    seeded native clients.
  - **Consent** is always explicit. It shows the document's `client_name` and the `client_id`
    host.
- **Discovery.** The discovery document advertises `client_id_metadata_document_supported: true`
  and includes `none` in `token_endpoint_auth_methods_supported`. Hosted agents select the
  document path only when `none` is advertised.

### §A.4 Interim native clients

**Target:** 0003 §5. **Requested by:** spec 0016 §11.9. **Why:** coding agents need a client until
§A.3 has served production traffic.

Until §A.3 has served production traffic for one release, the platform configuration seeds five
`Native` entries (§A.1's kind). Each is public, uses PKCE and the `tellma_api` scope, and holds the
distribution-origin list as `Resources`.

- **Three for local agents** — `claude-code`, `codex`, `cursor` — with port-less loopback redirect
  URIs.
- **Two public web clients for hosted agents** — `claude-web` (hosted Claude) and `codex-web`
  (hosted Codex). Their redirect URIs are the vendors' published callback URIs, carried by the
  entry's `RedirectUris`. An organisation enters their client ids in the vendor's connector dialog.

**Retirement.** After that release, the five entries leave the platform configuration. An operator
then deletes the five applications from the identity store, since the seeder never deletes one
(§A.1). A client that then presents one of the ids receives `invalid_client`.

### §A.5 Control-plane grants

**Target:** 0003 §5, §6.2. **Requested by:** spec 0011 §5.5. **Why:** a control-plane token for one
distribution must be invalid at another.

The control-plane seed client gains the distribution-origin list as `Resources`. A
`client_credentials` token with scope `tellma_control_plane` may then name `resource = {origin}`
and carry that origin as `aud`.

- **The distribution's check.** Its control-plane policy (spec 0011 §5.5) validates the scope and
  `aud = PublicOrigin`.
- **The fixed platform audience** of spec 0003 is the audience of the identity server's own
  operator API alone; a distribution does not accept it.

### §A.6 The `tellma_kind` claim

**Target:** 0003 §6.1. **Requested by:** spec 0011 §5.5. **Why:** a resource server should read the
principal kind, not infer it.

Every access token carries `tellma_kind`:

- **`individual`** when the token descends from an `authorization_code` or `device_code` grant;
  refreshes inherit it.
- **`service`** for `client_credentials` and for token exchange of a machine token.

The claim is set from the grant type at issuance and is never client-supplied. Resource servers
prefer it to the `auth_time` inference (spec 0011 §5.5) when present.

### §A.7 `existingOnly` on the bulk invite

**Target:** 0003 §10.1, §11.4. **Requested by:** spec 0018 §3.3. **Why:** a sandbox tenant must
never cause an email.

An invite item gains a flag that turns it into a lookup by email.

```csharp
// Tellma.Identity.Controllers.Api
public sealed class InviteUserItem
{
    public string Email { get; init; }
    public string? DisplayName { get; init; }
    public string? Locale { get; init; }
    public string? Gender { get; init; }
    public string? ReturnUrl { get; init; }
    public bool ExistingOnly { get; init; }                   // get-by-email; never creates, never mails
}
```

With `ExistingOnly = true`, the answer depends on whom the email belongs to:

| The email belongs to | Answer |
|---|---|
| an existing user who holds a credential | `Status = Active` with their `sub` |
| an existing user without a credential | the per-user error `existing_user_has_no_credential` |
| no user | the per-user error `unknown_email` |
| a `disabled` or `purged` user | the existing refusal |

- **No side effects.** No user is created, no link is issued and no email is sent for such an
  item, whatever the rest of the batch does.
- **The delivery-status API** is unaffected.
- **The consumer.** Spec 0018's users stack sets the flag on sandbox tenants and maps both errors
  to one validation code.

### §A.8 Management API versions

**Target:** 0003 §11.4. **Requested by:** spec 0018 §2. **Why:** one server answers distributions
that adopt platform releases on different schedules.

The management API moves under a version segment, relative to the authority. The unversioned
routes are removed. Their only callers, the server's own suites and engineering scripts, move to
`v1`.

| Routes | API |
|---|---|
| `POST api/identity/v1/invitations` | bulk invite (§A.7) |
| `POST api/identity/v1/invitations/delivery-status` | bulk delivery status |
| `POST api/identity/v1/service-accounts`; `GET`, `DELETE api/identity/v1/service-accounts/{clientId}` | service accounts |
| `GET api/identity/v1/users/{sub}`; `POST api/identity/v1/users/{sub}/temporary-access-passes` | operator |

The version is the API's own integer, independent of the engine's release.

- **Additive changes stay within a version.** A new endpoint, response member or optional request
  member stays in `v{n}`, as §A.7's `ExistingOnly` does. A caller ignores response members it does
  not know.
- **A breaking change** maps `v{n+1}` beside `v{n}` in one server release, before any platform
  release calls it. `v{n}` is removed once nothing calls it.
- **Observed.** Every call is counted on `tellma.identity.api.calls` (tags `version`, `endpoint`)
  and logged with the calling client. An operator sees when a version falls silent, and who still
  calls it.
- **In-proc.** An in-proc engine answers only its own distribution, whose identity client ships in
  the same platform release. The overlap of versions matters for the shared server alone.

### §A.9 The account entry URL and the return link

**Target:** 0003 §11.2 (Account & Security). **Requested by:** spec 0011 §5.9, spec 0027 §10.1.
**Why:** "My account" needs one stable URL, and its back link must return the user to the page they
left.

A new URL, `{issuer}/account`, redirects to one of the Manage pages. The pages' back link honours a
`return_url` that is same-origin with the client.

```csharp
// Tellma.Identity.Controllers — GET {issuer}/account; in-proc /id/account (the route convention adds the prefix)
public sealed class AccountController : Controller
{
    [Authorize]   // the identity cookie: an anonymous caller signs in first, then returns here
    public IActionResult Index(string? client_id, string? return_url, string? section, string? ui_locales);   // 302 to the section's Manage page
}

// Tellma.Identity.Infrastructure
public sealed class ApplicationLinkResolver
{
    public Task<ApplicationLink?> ResolveAsync(string? clientId, string? returnUrl);   // old: ResolveAsync(clientId)
}
```

- **Query.** `client_id` (the distribution's BFF client), `return_url` (absolute), `section`, and
  `ui_locales` (the page language, as §A.10).
- **Sections.**

  | `section` | Manage page |
  |---|---|
  | `profile` — also the default and any unknown value | `/Identity/Manage` |
  | `passkeys` | `/Identity/Manage/Passkeys` |
  | `authenticator` | `/Identity/Manage/EnableAuthenticator` |
  | `external-logins` | `/Identity/Manage/ExternalLogins` |
  | `sessions` | `/Identity/Manage/Sessions` |

- **Response.** `302` to the section's page, carrying `client_id`, `return_url` and `ui_locales`,
  with `Cache-Control: no-store`. Without the identity cookie, the normal challenge runs: sign-in,
  then back to the same URL.
- **The back link.**
  - **A first-party client with a registered `Origin`** gets `return_url` when it is same-origin
    with that origin, else the origin root (today's link).
  - **Same-origin** is `InvitationReturnUrlValidator.IsSameOrigin`'s rule: `http` or `https` only;
    scheme, host and port equal; no userinfo.
  - **A third-party or unknown client** gets today's answer: the configured application URL, else
    no link.
- **Carried forward.** Every Manage navigation link keeps `client_id`, `return_url` and
  `ui_locales`, so the back link survives a move between sections.
- **Published.** The distribution publishes `{Authority}/account?client_id={slug}` as
  `IdentityInfo.AccountUrl` (spec 0011 §5.9). The client appends `return_url` and `ui_locales`
  (spec 0027 §10.1).
- **Not amended.** The Manage pages' content, and the post-logout URIs: the §A.1 seed already
  registers `/signout-callback-oidc`.

**Example —** Farah opens "My account" from a sales invoice while working in Arabic:

```
GET https://id.tellma.com/account?client_id=acme&section=passkeys&ui_locales=ar
      &return_url=https%3A%2F%2Facme.example.com%2F1%2Fsales-invoices%2F17
302 Location: /Identity/Manage/Passkeys?client_id=acme&ui_locales=ar
      &return_url=https%3A%2F%2Facme.example.com%2F1%2Fsales-invoices%2F17
```

The Passkeys page renders in Arabic, and its back link opens
`https://acme.example.com/1/sales-invoices/17`. With `return_url=https://evil.example/` the back
link is `https://acme.example.com`.

### §A.10 The sign-in language from the authorization request

**Target:** 0003 §11.2 (sign-in). **Requested by:** spec 0011 §5.5, spec 0024 §8.3. **Why:** a user
who signs in from an Arabic client sees an Arabic sign-in page. Today only a `ui_locales` query on
the page itself is honoured.

- **Carried to the sign-in page.** When the authorization endpoint redirects to the sign-in page,
  it adds the request's `ui_locales` to the sign-in URL. The value is the validated request's own:
  the query's, or under PAR the pushed request's (OpenIddict restores it from `request_uri`).
- **Applied by the existing provider.** `UiLocalesRequestCultureProvider` reads the query ahead of
  the culture cookie and `Accept-Language`; the first supported tag wins.
- **Kept through the interaction.** Links and redirects between the sign-in pages (email code,
  external login, passkey registration, step-up) keep the `ui_locales` query. The return to the
  authorization endpoint re-applies it from the request.
- **Fallback.** A missing value, or one with no supported tag, falls back as today.

**Example —** `/bff/login?ui_locales=ar` pushes an authorization request carrying `ui_locales=ar`.
The authorization endpoint answers `302 /Identity/Account/Login?returnUrl=…&ui_locales=ar`, and the
page renders right-to-left in Arabic.

### Part A — Testing

| Suite | Tier | When it runs |
|---|---|---|
| `test/apps/Tellma.Identity.Tests` | unit | every pull request, Windows and Linux (spec 0011 §10) |
| `test/apps/Tellma.Identity.IntegrationTests` | `Category=Integration` | every pull request, Windows and Linux |
| `test/apps/Tellma.Identity.E2E` | `Category=E2E` | its existing Linux job, every non-draft pull request |

No `Live=true` suite exists, so the nightly tier runs nothing for Part A.

**The unit suite pins:**

- **The resource evaluator (§A.2).** `origin` exact and `origin/17/mcp` accepted. Refused:
  `origin/017/mcp`, `origin/17/mcp/`, `origin/17/other`, `origin/17/mcp?x`, `other-origin/17/mcp`
  and `origin/0/mcp`. A granted resource with a path never anchors the pattern. A document
  client's `resource` is evaluated against the distribution-origin list, and an origin outside it
  is refused.
- **Document validation (§A.3).** `client_id` mismatch, missing redirects, `client_secret_basic`,
  inline `jwks`, off-origin `jwks_uri`, loopback port relaxation.
- **Fetch guards (§A.3),** against a fake handler: private address, redirect, oversize, wrong
  content type, timeout. Also cache clamping.
- **The `Distribution` seed descriptor (§A.1).** Both applications, all permissions, idempotence,
  secret rotation, never deleting.
- **Claims and invites.** `tellma_kind` by grant type (§A.6); `existingOnly` outcomes (§A.7).
- **The account entry (§A.9).** The section mapping, unknown → `profile`. The back-link resolver:
  same origin, another port, userinfo, `javascript:`, a relative URL, an unknown client, a
  third-party client.

**The integration suite pins:**

- **A document client's full code flow** against a local document server, with consent showing
  the host.
- **Per-tenant audiences.** `resource=origin/17/mcp` through code exchange and refresh, with `aud`
  asserted. A refresh to `origin/18/mcp` is refused, and so is a refresh to `origin`.
- **Seeds and grants.** A control-plane token with a distribution audience. A `Distribution` seed
  applied twice.
- **Invites.** The bulk invite with mixed `existingOnly` items, asserting no mail queued for them.
- **Discovery.** `client_id_metadata_document_supported: true`, and `none` among
  `token_endpoint_auth_methods_supported`.
- **Routes.** Every management route answers under `v1`, and `404` without the segment.
- **Account entry.** `/account` with and without the identity cookie, per section, asserting
  `Location`, the carried query and `no-store`. A Manage page's back link with a same-origin and
  an off-origin `return_url`.
- **Sign-in language.** The sign-in redirect carries `ui_locales` from a query request and from a
  pushed request.

**The E2E suite pins:** `ui_locales=ar` through a pushed authorization request renders the
sign-in page in Arabic with `dir=rtl`, and the email-code step keeps it. The account entry's back
link returns the browser to `return_url`.

### Part A — Definition of done

- **Projects:** `src/apps/Tellma.Identity` grown, its README updated, XML docs on every member,
  building and testing on Windows and Linux under warnings-as-errors; no new project.
- **Behaviour:** §A.1–§A.3 and §A.5–§A.10 implemented and pinned by Part A's suites, the discovery
  changes of §A.3 included. §A.4 is seed configuration in `Tellma.Identity.Web` (the placement
  table) and is not pinned by a suite.
- **Observability:**
  - audit events `client.metadata.fetched`, `client.metadata.rejected` (reason),
    `resource.refused` (client, requested) and `seed.distribution.applied`;
  - on the `Tellma.Identity` meter, the counters `tellma.identity.client_metadata.fetches` (tag
    `outcome` ∈ `fetched | rejected | failed`) and `tellma.identity.api.calls` (§A.8);
  - no `client_id` URL is a metric tag.
- **CI:** the unit and integration suites green on every pull request on both platforms; the E2E
  suite green in its job.
- **Docs:** the architecture document's identity section updated for metadata-document support,
  per-tenant resources by path pattern, the `Distribution` seed kind, the control-plane grants, the
  versioned management routes and the account entry URL. Public XML docs and error messages
  reference no `docs/` paths.
- **Not in scope of done:** retiring the interim clients of §A.4 (a later configuration change
  plus an operator deletion); distribution self-registration; the tenant side of every item (specs
  0011, 0016, 0018, 0024, 0027).

### Part A — Decisions record

1. **A fixed path pattern under an origin grant, not per-tenant resource registration** — a tenant
   is a row in a distribution's catalog, not identity state, and the audience space stays
   enumerable (§A.2).
2. **Document clients are public or `private_key_jwt` only; secrets are never discovered** — a
   document proves control of a URL, never possession of a secret (§A.3).
3. **Explicit consent for every document client, showing the host** — the user, not the server, is
   the last check on an unregistered client (§A.3).
4. **Control-plane isolation by distribution audience, not a fixed platform audience** — a
   control-plane token for one distribution is structurally invalid at another (§A.5).
5. **`existingOnly` never creates and never mails; a credential-less user is an error, not
   `Reinvited`** — the sandbox must not become a side door to onboarding (§A.7).
6. **`tellma_kind` from the grant type at issuance** — a resource server should not infer the
   principal kind from `auth_time` (§A.6).
7. **The `Distribution` seed kind mirrors the onboarding automation exactly** — in-proc and
   standalone deployments register the same two applications (§A.1).
8. **A URL version segment on the management API, additive changes inside a version** — a breaking
   change ships beside the old shape, and the old one retires once nothing calls it (§A.8).
9. **The account entry URL only redirects** — the distribution publishes one stable URL, and the
   Manage pages keep their own routes and content (§A.9).
10. **`return_url` is a link, honoured only when same-origin with the client's registered origin**
    — the back link can never point anywhere the client is not already trusted to receive users
    (§A.9).
11. **`ui_locales` travels on the sign-in URL** — the existing culture provider applies it; no new
    state or cookie (§A.10).

### Part A — Review flags

1. **The hard-coded `/{int}/mcp` pattern** (§A.2). Alternative: a configurable pattern list on the
   client or the server. Flips when a second per-tenant surface needs its own audience.
2. **Cache bounds of one minute to one day for metadata documents** (§A.3). Alternative: honour
   the document's headers unbounded. Flips with measured fetch volume from hosted agents.
3. **Refusing inline `jwks` and off-origin `jwks_uri`** (§A.3). Alternative: accept them as the
   draft allows. Flips if a mainstream agent ships only an inline key set.
4. **The interim client set** (§A.4): three native ids and two hosted web clients, retired one
   release after §A.3. Flips with the agents that actually appear in customer traffic.
5. **Control-plane grants per origin** (§A.5). Alternative: an agreed fixed audience accepted by
   every distribution. Flips if maintaining the origin list at onboarding proves error-prone
   before self-registration exists.
6. **`existing_user_has_no_credential` as an error** (§A.7). Alternative: answer `Active` for any
   existing user. Flips if sandbox users commonly exist without a credential.
7. **The claim name `tellma_kind` with values `individual | service`** (§A.6). Flips only with a
   platform-wide claim-naming review.
8. **A closed `section` list with `profile` as the fallback** (§A.9). Alternative: accept any
   Manage path. Flips if a distribution needs to deep-link a page outside the five.

## Parts B–E: rules shared by the library amendments

Parts B–E change `@tellma/core-ui` (`client/projects/core/tellma-core-ui/`) and its tokens,
testing and Arabic locale packages. File paths in these parts are relative to that folder unless
stated.

- **Order.** Part B first, then Part D. Part C follows spec 0023 §3 (`tm-icon`, `TmKeys`), because
  its menu renders registry icons and key labels. Part E follows spec 0023 §4 (`tm-chip`). Spec
  0023 itself builds on §B.1's breakpoints and §D.1's overlay entry point.
- **Every changed entry point ships whole.** In the same change it updates:
  - its API golden (`client/api/*.api.md`);
  - its `*.examples.ts` and its `components.json` entry;
  - its harness in `@tellma/core-ui-testing`, and its showcase story;
  - its English strings (`i18n/strings-en.ts`) and their Arabic twins in `@tellma/locale-ar`,
    which the parity gate checks.
- **New tokens** get a value in the default preset (`tellma-core-ui-tokens/presets/tellma-default.ts`).
  A part that adds tokens refreshes the identity server's copy of the token CSS with `pnpm run
  identity:copy-assets`; `tokens:check` is the drift gate.
- **No public text cites a spec.** TSDoc, strings and test names stay self-contained; `docs:build`
  gates it.
- **Budgets** are gzip self-weights measured by `pnpm run size:check`. Each part lists its new
  ceilings, and every raise carries a `budgetNotes` entry.
- **UI rules.** Each part's Definition of done states how it meets instant feedback, non-blocking,
  stable layout, RTL and WCAG AA, with the test that pins each. Instant warm launch is the app's
  concern (spec 0024), not the library's.

Each part's Testing names what it pins in these suites, all run on every pull request:

| Suite | What runs |
|---|---|
| Unit | vitest in browser mode over `core-ui` |
| Format golden | `l10n/format-golden.json`, whose rows the server twin also reads |
| `lint:test` | the stylelint and ESLint rule vectors (`client/tools/`) |
| Playwright on the showcase | Chromium in full; Firefox and WebKit on `@cross-engine`; touch; at 390, 768, 992 and 1440 px, in LTR and RTL |
| axe battery | light and dark, LTR and RTL, with popups open |
| Gates | API goldens, `docs:build`, `size:check`, `tokens:check` |

## Part B — Component library foundation (spec 0002)

Part B changes the library's foundation: the primary entry point, the select and the lint rules.
Its consumers are the app foundation (spec 0024: theme, strings, direction), the shell (spec 0027),
the filter controls (spec 0029 §13), the search page (spec 0030) and every responsive layout,
spec 0023's components included.

### §B.1 Breakpoints

**Target:** 0002 §4 (a new subsection, "Breakpoints") and §1 (lint). **Requested by:** specs 0023
§2, 0027 §7.1, 0028 §6.4, 0030 §3.2 and every page spec. **Why:** one closed set of widths for the
whole platform.

Pages switch structure with `TmBreakpoints`; components adapt with container queries. Both use
the same five thresholds.

```ts
// @tellma/core-ui (primary, folder layout/)
type TmBreakpoint = 'xs' | 'sm' | 'md' | 'lg' | 'xl' | 'xxl';
declare const TM_BREAKPOINTS: { readonly sm: 576; readonly md: 768; readonly lg: 992; readonly xl: 1200; readonly xxl: 1440 }; // xs starts at 0

@Injectable({ providedIn: 'root' })
declare class TmBreakpoints {
  readonly current: Signal<TmBreakpoint>;              // the widest breakpoint the viewport reaches
  atLeast(breakpoint: TmBreakpoint): Signal<boolean>;  // memoized per breakpoint; atLeast('xs') is always true
}
```

- **How it measures.** Five `matchMedia('(width >= Npx)')` lists with `change` listeners feed one
  signal. No resize listener and no layout read run between changes.
- **What it is for.** Page-level structure only: grid or tiles, drawer or popover, a full-screen
  launcher.
- **Components use container queries.** A component that adapts sets `container-type:
  inline-size` on an inner wrapper, and queries `@container (width >= 576px)`. A `display: block`
  host whose width never depends on its content may serve as the wrapper. The component then
  behaves the same in a narrow card on a wide screen.
- **No breakpoint tokens.** CSS custom properties cannot appear in media or container queries.
  `TM_BREAKPOINTS` and the lint rule below are the two homes of the values, pinned equal by a test.

**Example —** the bell picks its surface (spec 0028 §6.4):

```ts
private readonly wide = inject(TmBreakpoints).atLeast('lg');
protected open(): void {
  this.wide() ? this.popover().open(this.bell()) : this.modal.open(TmBellPanel, { placement: 'end', title });
}
```

**The stylelint rule `tm/allowed-breakpoints`** (a new plugin in `client/tools/stylelint/`):

- **Where it applies.** Every `@media` and `@container` prelude in the CSS of
  `projects/core/**`, `projects/module/**` and `projects/locale/**`; the showcase is exempt.
  Distributions get it through `@tellma/core-tools`' stylelint preset (spec 0024 §1).
- **Accepted.** Range syntax on `width` or `inline-size`, a lower bound with `>=` and an upper
  bound with `<`, values from the set: `(width >= 768px)`, `(width < 576px)`,
  `(576px <= width < 992px)`.
- **Refused.** `min-width`, `max-width`, `min-inline-size`, `max-inline-size`, `device-width`;
  `width <= N` and `width > N`; any unit but `px`; any other value. The message names the nearest
  threshold and the canonical form.
- **Unrestricted.** `height`, `orientation`, `pointer`, `hover`, `prefers-*`, `forced-colors`.
- **One source.** The rule's set is a literal. A `lint:test` case imports `TM_BREAKPOINTS` and
  asserts the two are equal.

**Existing code that changes:** only the modal shell's width query (§D.2). No other width query
exists in the library.

**Compatibility:** additive. **Tests:** unit — `TmBreakpoints` at 390, 576, 767, 768 and 1440 px,
memoized signals, `atLeast('xs')`. `lint:test` — accepted and refused preludes, the parity case.
**Budget:** primary +0.4 KB.

### §B.2 `TmTheme` and the System theme

**Target:** 0002 §4. **Requested by:** spec 0024 §7.6, spec 0027 §10.1. **Why:** users choose
System, Light or Dark.

A new root service, `TmTheme`, resolves System to light or dark and paints the page. The app, not
the library, persists the choice.

```ts
// @tellma/core-ui (primary, folder theme/)
type TmThemeSetting = 'system' | 'light' | 'dark';
interface TmThemeOptions { readonly initial?: TmThemeSetting | (() => TmThemeSetting) } // default 'system'

@Injectable({ providedIn: 'root' })
declare class TmTheme {
  readonly setting: Signal<TmThemeSetting>;
  readonly effective: Signal<'light' | 'dark'>;   // the setting, or the OS preference under 'system'
  set(setting: TmThemeSetting): void;              // writes <html data-theme> in the same task
}
// TmUiOptions gains: readonly theme?: TmThemeOptions (§B.3)
```

- **The DOM write.** `<html data-theme>` always holds `light` or `dark`, never `system`, so the
  emitted CSS stays two-state. `set()` writes synchronously: a theme click repaints in its own
  frame.
- **System.** Under `system`, a `matchMedia('(prefers-color-scheme: dark)')` listener updates
  `effective` and the attribute. Under `light` or `dark` the listener is detached.
- **Startup.**
  - With `provideTellmaUi({ theme })`, an environment initializer constructs `TmTheme` with
    `initial`, so OS changes are followed from boot.
  - Without the option, `TmTheme` is created on first inject, with `system`.
  - The seed equals what the boot script already painted, so the first write is a no-op and
    nothing flashes.
- **It never persists.** The app stores the setting in spec 0025's boot record (spec 0024 §7.6).
- **`color-scheme` needs no change.** The tokens emitter already writes `color-scheme: light` in
  the `:root` block and `color-scheme: dark` under `[data-theme=dark]`. Native scrollbars,
  controls and autofill therefore follow `set()`.
- **Forced colors** are untouched by `TmTheme`.

**Example —** `@tellma/core` seeds the theme and stores its changes (spec 0024 §7.6):

```ts
provideTellmaUi({ theme: { initial: () => store.boot.read().theme } });
effect(() => store.boot.patch({ theme: theme.setting() }));   // in @tellma/core, never in the library
```

**Compatibility:** an app that writes `data-theme` itself (the showcase) is unaffected until it
injects `TmTheme`. **Tests:** unit — `set` and `effective`; the media listener attached only under
System (emulated `prefers-color-scheme`); the same-task write. e2e — a toggle repaints and never
writes `data-theme=system`; axe light and dark unchanged. **Budget:** primary +0.5 KB.

### §B.3 The translations loader and live direction

**Target:** 0002 §5 (`provideTellmaUi`) and §7 (direction). **Requested by:** spec 0024
§7.1–§7.3. **Why:** the app loads string packs and switches language and direction at run time, and
every library surface must follow.

`provideTellmaUi` keeps owning Transloco. The app supplies a loader and flips `<html dir>`.

```ts
// @tellma/core-ui (primary)
type TmUiTranslationTable = { readonly [key: string]: string | TmUiTranslationTable };
type TmUiTranslationsLoader = (request: { readonly lang: string; readonly scope: string | null }) => Promise<TmUiTranslationTable>;

interface TmUiOptions {                              // old: { forms?, availableLangs? }
  readonly forms?: TmFormsOptions;
  readonly availableLangs?: readonly string[];      // the boot-time list; later changes via TranslocoService.setAvailableLangs
  readonly translations?: TmUiTranslationsLoader;   // new: registered as the Transloco loader
  readonly theme?: TmThemeOptions;                  // new (§B.2)
}
```

**Strings:**

- **Merged, never replaced.** Transloco merges a loaded table into the language's existing
  translation. The library's `tmUi` English and a locale pack's `tmUi` Arabic survive any load
  order; the pack registers with `setTranslation(…, { merge: true })`.
- **The root load** (`scope: null`) carries the server pack at the root and the `tm` scope under
  `tm`. Spec 0024 §7.3 owns the merge order.
- **Fallback loads.** With `useFallbackTranslation`, loading `ar` also loads `en` through the same
  loader.
- **The library never calls `load`.** `TM_UI_TRANSLATE` re-resolves on `translationLoadSuccess`
  and `langChanges$`. The app activates a language (spec 0024 §7.2): it awaits `load(lang)`, then
  calls `setActiveLang(lang)`. A switch is therefore one synchronous re-render.
- **No loader** keeps today's behaviour: English plus the locale packs.

**Live direction.** `provideTellmaUi` provides `{ provide: Directionality, useClass:
ɵTmLiveDirectionality }` at the root.

- **What it watches.** A `MutationObserver` on the `dir` attribute of `<html>` and `<body>`, read
  body first as CDK's root reads them. On a change it sets the inherited `valueSignal` and emits
  `change`.
- **The documented host is `<html dir>`.** The app flips `document.documentElement.dir` and
  `lang`, and every library surface follows: open and future modals (§D.2), connected overlays at
  their next attach, the grid's arrow mapping, submenu sides (§C.10).
- **`[dir]` islands** (CDK `Dir`) still override locally.

**Compatibility:** additive. **Tests:** unit — a loaded table merges with a pre-registered `tmUi`
pack in both orders; the `en` fallback load; `TM_UI_TRANSLATE` re-renders after a load; the live
`Directionality` emits on `<html dir>` and `<body dir>` changes. e2e — flip `dir` with a modal open
and with a menu closed, then open the menu: both are right-to-left. **Budget:** primary +0.5 KB.

### §B.4 `tm-multi-select` and option groups

**Target:** 0002 §3.4 and its Non-goal "Phase-1 Select is single-select". **Requested by:** spec
0029 §13.3, spec 0030 §9.2. **Why:** enum filters with more options than a checkbox list holds,
and grouped option lists.

A new closed-list `tm-multi-select` sits beside `tm-select` and reads the same `tm-option`
definitions. Both selects gain option headings through `tm-option-group`.

```ts
// @tellma/core-ui/select — new
@Component({ selector: 'tm-option-group' })
declare class TmOptionGroup {
  readonly label = input.required<string>();     // the visible heading and the group's accessible name
}

@Component({ selector: 'tm-multi-select' })     // implements TmFormFieldControl, FormValueControl<readonly T[]>
declare class TmMultiSelect<T> {
  readonly value = model<readonly T[]>([]);      // always in option order, never click order
  readonly valueKey = input<((value: T) => string | number) | undefined>(undefined);
  readonly displayWith = input<((value: T) => string) | undefined>(undefined);
  readonly placeholder = input<string | undefined>(undefined);
  readonly size = input<TmControlSize | undefined>(undefined);
  // disabled, readonly, required, invalid, touched, dirty, pending, errors, aria-label,
  // aria-describedby, touch: exactly as TmSelect
  readonly selectionChange = output<readonly T[]>();
  readonly opened = output<void>();
  readonly closed = output<void>();
}
```

**Example —** a Center type filter:

```html
<tm-form-field label="Center type">
  <tm-multi-select [formField]="filter.types">
    <tm-option-group label="Operating">
      <tm-option value="Service">Service</tm-option>
      <tm-option value="Operation">Operation</tm-option>
    </tm-option-group>
    <tm-option value="Sale">Sale</tm-option>
  </tm-multi-select>
</tm-form-field>
```

**`tm-multi-select`:**

- **Panel.** The same combobox and listbox wiring as `tm-select`, with `ngListbox [multi]="true"`
  and `aria-multiselectable="true"`. Each row shows a checkbox glyph: the `tm-checkbox` box, drawn
  with the grid's mask, so it costs no component.
- **Toggle, stay open.** Click, Space and Enter toggle the active option and keep the panel open.
  Escape, Tab and an outside press close it. Home, End and typeahead work on the open panel.
- **Trigger.** One line at a fixed height. It shows the selected labels joined with
  `Intl.ListFormat(locale, { type: 'unit', style: 'short' })` ("Service, Operation") and
  ellipsized, or the placeholder when empty. The accessible value is the full list.
- **Values not in the options** are kept, never pruned. As in `tm-select`, they show through
  `displayWith`, else `String(value)` with a dev warning.
- **Not a grid editor.** Grid cells hold scalars; `tm-multi-select` implements no `TmCellEditor`.

**`tm-option-group`, in both selects:**

- **Markup.** Each group renders `<li role="group" aria-labelledby>` holding a heading row and its
  options. The heading has the option height, so the panel's measured height is stable.
- **Navigation** skips headings; typeahead searches options only.
- **Nesting** is one level; a group inside a group is a dev error.
- **Definitions.** Groups are content definitions like `tm-option`, because aria's `ngOption`
  cannot be projected. `tm-select` reads `contentChildren(TmOption, { descendants: true })` for
  the flat order and `contentChildren(TmOptionGroup)` for the headings.

**Accessibility and RTL:**

- **Keyboard and ARIA** as `tm-select`: a select-only combobox with an active descendant. Options
  carry `aria-selected`, and the group's `aria-labelledby` names it.
- **RTL.** The check glyph sits at the inline end, as in `tm-select`.

**Compatibility:** `tm-select` without groups renders identically. **Harness:**
`TmMultiSelectHarness` (`open`, `toggle(label)`, `getValueText`, `isOptionSelected`);
`TmSelectHarness.getGroups()`. **Tests:** unit — the option-order value, toggle keeps the panel
open, unmatched values, navigation skips headings. e2e and axe — both selects with groups, LTR and
RTL, light and dark. **Budget:** `./select` ≈ 8.4 KB.

### §B.5 The `tm/prefix-exports` scope

**Target:** 0002 §1 (the ESLint prefix rule). **Requested by:** spec 0024 §1. **Why:** the naming
convention covers context accessors (`injectTm…`) and the npm packs under `projects/module/`.

- **Admits `injectTm…`.** `VALUE_LIKE` in `client/tools/eslint/tm-prefix-exports.mjs` becomes
  `/^(tm[A-Z0-9]|provideTellma[A-Z0-9]|injectTm[A-Z0-9])/`.
- **Covers packs.** The rule's `files` add `projects/module/**/*.ts`; its `ignores` add the Node
  package `projects/core/tellma-core-tools/**`, beside `tellma-core-ui-mcp`.
- **Tests:** `lint:test` cases for `injectTmDetails` (passes), `injectDetails` (fails), and a
  `projects/module/` file (covered).

### §B.6 Touch sizes below `sm`

**Target:** 0002 §5 (`TM_FORM_FIELD_DEFAULTS`). **Requested by:** spec 0023 §2. **Why:** controls
in page content reach a 46 px touch height on phones. Menu rows follow §C.10.

- **Controls.** A control on the size ladder whose `size` input is unset renders at
  `TM_FORM_FIELD_DEFAULTS.size` from `sm` up, and at `lg` (46 px) below `sm`. An explicit `size`
  always wins.
- **Which controls.** Every control that reads the default: `tm-form-field` and the controls it
  hosts, `tmButton`, `tm-select` and `tm-multi-select`.
- **Not the grid.** The grid keeps its row size at every width; 0004 §8.6 owns its touch model.
- **One resolver.** The controls resolve their size through one internal helper:

  ```ts
  // @tellma/core-ui (primary, folder forms/); TmFormFieldDefaults is unchanged
  declare function ɵtmControlSize(explicit: Signal<TmControlSize | undefined>): Signal<TmControlSize>;
  // explicit ?? (TmBreakpoints.atLeast('sm')() ? TM_FORM_FIELD_DEFAULTS.size : 'lg')
  ```

- **Stable layout.** Sizes change only when the viewport crosses 576 px, as on a rotation; never on
  input.

**Compatibility:** nothing changes from 576 px up. On phones, defaulted controls grow to 46 px.
**Tests:** unit — the resolved size at 390 and 768 px, with and without an explicit `size`; e2e at
390 px — a field is 46 px tall. **Budget:** primary +0.1 KB.

### Part B — Testing

| Suite | Pins |
|---|---|
| Unit | `TmBreakpoints`; `TmTheme`; the loader merge and the live `Directionality`; `tm-multi-select` value order, toggle and unmatched values; group navigation; `ɵtmControlSize` |
| `lint:test` | `tm/allowed-breakpoints` vectors and their parity with `TM_BREAKPOINTS`; the `tm/prefix-exports` cases |
| Playwright | the theme toggle; the live direction flip with a modal open; both selects with groups; touch geometry at 390 px |
| axe | select groups and multi-select states in both themes and directions |
| Gates | the primary and `./select` goldens; the ceilings below |

### Part B — Definition of done

- **Breakpoints:** `TM_BREAKPOINTS`, `TmBreakpoints` and `tm/allowed-breakpoints` shipped (§B.1);
  the parity test green; the modal query migrated.
- **Theme:** `TmTheme` shipped (§B.2).
- **Strings and direction:** the loader seam and `ɵTmLiveDirectionality` shipped (§B.3); the
  showcase's overlays no longer depend on its `[dir]` wrapper.
- **Selects:** `tm-multi-select`, `tm-option-group` and their harnesses shipped (§B.4).
- **Lint:** the prefix rule's new scope (§B.5).
- **Touch:** §B.6 shipped.
- **UI rules:**
  - **Instant feedback** — `TmTheme.set` writes in the click's task (unit).
  - **Non-blocking** — nothing in Part B waits on the network.
  - **Stable layout** — the multi-select trigger keeps one height (e2e geometry); sizes change only
    across 576 px (§B.6 unit).
  - **RTL** — the live switch (§B.3 e2e).
  - **WCAG AA** — axe over the new select states in both themes and directions; 46 px touch fields
    (§B.6 e2e).
- **Budgets:** primary `.` 5 → **7** (Part D's primary additions included); `./select` 8 → **9**;
  notes recorded.

### Part B — Decisions record

1. **Breakpoints are TypeScript constants plus a lint rule, never CSS tokens** — custom properties
   cannot appear in media or container queries, and a `var()` there fails silently (§B.1).
2. **CSS spells thresholds only with range syntax** — one spelling, no `767.98px`; `min-width:`
   was the alternative (§B.1).
3. **Components adapt with container queries; `TmBreakpoints` is for page structure** — a
   component behaves the same in a narrow card on a wide screen (§B.1).
4. **`TmTheme` never persists; the app seeds it and stores its changes** — the boot record has one
   writer (spec 0025); a storage port in the library was the alternative (§B.2).
5. **Live direction is a root `Directionality` watching `<html dir>`** — every root-injected
   surface follows a switch. A modal `direction` option would hand dialog content a static
   `Directionality`, and a `[dir]` wrapper never reaches the overlay container (§B.3).
6. **The translations seam is a loader function; activation is the app's** — one owner of the
   language runtime; a library `use(lang)` service was the alternative (§B.3).
7. **The multi-select is a sibling component** — an input flag cannot change the model's type;
   `tm-select [multiple]` was the alternative (§B.4).
8. **The multi-select trigger is joined text, not chips** — the panel shows every choice, and one
   line of text keeps the trigger at the field height with no overflow logic (§B.4).
9. **Below `sm` the default size is `lg`; explicit sizes win; grids keep their density** — one rule
   for page content, no per-page code; touch grids are 0004 §8.6's (§B.6).

### Part B — Review flags

1. **Sibling components (`tm-multi-select`, `tm-entity-multi-picker`) rather than a `multiple`
   input** (§B.4, §E.2). Flips only if Angular gains input-dependent model types.
2. **Live direction by a root `MutationObserver` on `<html dir>`** (§B.3). Alternative: an explicit
   `setDirection` call. Flips if a distribution needs per-region directions outside `[dir]`
   islands.
3. **`TmTheme` constructed at startup only when `provideTellmaUi({ theme })` is passed** (§B.2).
   Alternative: always. Flips if an app forgets the option and loses System tracking.
4. **CSS range syntax only** (§B.1). Alternative: also accept `min-width:`. Flips if an embedded
   browser customers use predates Safari 16.4 or Firefox 102.
5. **No opt-out of the touch rule** (§B.6). Alternative: a `touchSize` member on
   `TmFormFieldDefaults`, `null` turning the rule off. Flips if a distribution wants dense forms on
   phones.
6. **Grids excluded from the touch rule** (§B.6). Flips if phone users edit line-entry grids.

## Part C — Data grid and menu (spec 0004)

Part C gives the grid what list and line-entry pages need, and gives the menu the entry kinds the
shell and page toolbars need. Its consumers are the search page (spec 0030), the details page
(spec 0031), the shell (spec 0027), the report renderers (spec 0034) and spec 0023's tiles, which
reuse the activation and driver types.

- **Where grid members land.** Every grid input below is on `ɵTmGridBase`
  (`grid/internal/grid-base.ts`), so `tm-grid` and `tm-tree-grid` share it. Tree-only members are
  on `TmTreeGrid`.
- **The headless `grid-engine` does not change.** The work is in `ɵTmGridCore`
  (`grid/internal/grid-core.ts`) and the view.

### §C.1 Row activation

**Target:** 0004 §8.2, §8.3, §8.8 and Decision 18. **Requested by:** spec 0030 §7.2; spec 0023 §8
(tiles). **Why:** a readonly list opens a row with a click anywhere or Enter, while ranges and copy
keep working.

```ts
// ɵTmGridBase — new members
readonly rowActivation = input<'none' | 'row'>('none');   // 'row' requires a readonly grid (dev error otherwise)
readonly rowActivate = output<TmGridRowActivation<T>>();

// @tellma/core-ui/grid
interface TmGridRowActivation<T> {
  readonly row: T;
  readonly rowId: TmRowId;
  readonly via: 'pointer' | 'keyboard';
  readonly newTab: boolean;          // Mod held, or the middle button: the consumer opens a new tab
}
```

Under `rowActivation: 'row'`:

| Gesture | Result |
|---|---|
| Press and release on a data cell, under 4 px of movement, no modifier | the active cell moves on press (instant highlight); `rowActivate { via: 'pointer', newTab: false }` on release |
| The same with Mod (Ctrl, ⌘) or the middle button (`auxclick`) | `rowActivate { newTab: true }` |
| Press and drag | range selection, unchanged; no activation |
| Shift+click | extends the range, unchanged; no activation |
| Mod+drag | adds a range, unchanged |
| Click on interactive cell content (`a[href]`, `button`, inputs, `[tabindex]`) | native behaviour only (the router link, the button); no activation |
| Checkbox cell, select-all header, expander, row header, resize handle | their own behaviour; never activation |
| Touch tap | activation |
| Enter on the active cell | `rowActivate { via: 'keyboard', newTab: Mod held }` |
| F2 on the active cell | actuates the cell's first interactive child (0004 §8.2's readonly Enter) |
| A second activation of the same row within 400 ms | ignored, so a double-click opens once |

- **`'none'`** (the default) keeps every behaviour of today, Enter included.
- **Printable keys pass through.** A readonly grid, in either mode, leaves unmodified printable
  keys unhandled and never calls `preventDefault` on them, so a page's type-to-search receives them
  (spec 0030 §6.2).
- **Visual.** The active row carries `tm-grid__row--active`. It paints a row tint
  (`--grid-active-row-bg`, a new token on `surface.selected`) and a 2 px inline-start accent bar on
  the first painted cell. The focus ring stays on the active cell. Rows show `cursor: pointer`.
- **Accessibility.** The scroller gains `aria-describedby` pointing at a visually hidden hint,
  `tmUi.grid.rowActivationHint`: "Press Enter to open a row. Press F2 to reach a link in a cell."
  Roles are unchanged (`grid`, `row`, `gridcell`).
- **Links stay links.** The search page renders display-name and navigation cells as real
  `<a [routerLink]>` links, so opening in a new tab and copying a link stay native.
- **One payload type.** `tm-tiles` emits the same `TmGridRowActivation<T>` (spec 0023 §8), so a
  page swaps grid and tiles behind one handler.
- **0004's Decision 18 reads:** list screens open records by row activation or through consumer
  links in cells; a grid-provided checkbox column selects.

**Example —** a list screen (spec 0030 writes this inside its host; distributions write nothing):

```html
<tm-grid gridId="search.acme.Customer" [data]="rows()" [rowId]="byId" selectable rowActivation="row"
         [rowHeaders]="false" [(selectedIds)]="selected" [(activeRowId)]="active" [(orderBy)]="order"
         (rowActivate)="open($event)">
  …columns…
</tm-grid>
```

### §C.2 The external active row and the keyboard driver

**Target:** 0004 §5.1, §8.2. **Requested by:** spec 0030 §6.3. **Why:** the page drives the
highlight while focus stays in its search field.

The active row becomes a two-way model, `activeRowId`. A new directive, `tmGridDriver`, lets a
text input move and open that row.

```ts
// ɵTmGridBase — new members
readonly activeRowId = model<TmRowId | null>(null);  // two-way: the active cell's row
moveActiveRow(by: number | 'first' | 'last'): void;  // clamped, never wraps; keeps the active column
activateRow(rowId?: TmRowId, options?: { newTab?: boolean }): boolean; // emits rowActivate { via: 'keyboard' }; false when absent

// @tellma/core-ui/grid — the face the driver uses; both grids implement it
interface TmGridDriverTarget {
  readonly activeRowId: Signal<TmRowId | null>;
  moveActiveRow(by: number | 'first' | 'last'): void;
  activateRow(rowId?: TmRowId, options?: { newTab?: boolean }): boolean;
}
@Directive({ selector: 'input[tmGridDriver]' })
declare class TmGridDriver {
  readonly tmGridDriver = input.required<TmGridDriverTarget>(); // an interface, so TmGrid<Customer> binds without variance friction
}
```

- **The model.**
  - **Grid to consumer.** Every change of the active cell's row writes `activeRowId`: a click, an
    arrow, a data reconcile.
  - **Consumer to grid.** A write moves the active cell to that row, in the current column, else
    the first data column. It scrolls the cell into view (`nearest`) and never moves DOM focus.
  - **Unknown ids.** An id not in the data is ignored. The model is rewritten with the actual row,
    or `null` when the grid is empty.
- **The driver** handles keydown on its input, ignoring IME composition and other modifiers:

  | Key | Effect |
  |---|---|
  | ArrowDown / ArrowUp | `moveActiveRow(+1 / −1)`; with no active row, ArrowDown goes to the first row |
  | PageDown / PageUp | moves by the visible row count |
  | Enter | `activateRow()` when a row is active (Mod → `newTab`); otherwise the input's own Enter |

  Every other key (Home, End, Left, Right, Space, characters) stays with the input.
- **ARIA.** The driver sets `aria-controls` to the grid's scroller id, and `aria-activedescendant`
  to the active row's element id. ARIA 1.2 lets a textbox or searchbox reference an element in the
  container it controls. The active row is always rendered (0004 §4), so the reference never
  dangles.
- **Who seeds the first row.** The page: spec 0030 writes `activeRowId` to the first row's id when
  results land while focus is in its field.
- **The type lives in `grid`.** `tm-tiles` implements `TmGridDriverTarget` through `import type`,
  so the tiles carry no grid code (spec 0023 §8).

**Example —** the search field: `<input tmInput type="search" [tmGridDriver]="grid" … />`.

### §C.3 Sort state and `aria-sort`

**Target:** 0004 §6.1, §14; the Non-goal "the consumer owns the array it binds" stands.
**Requested by:** spec 0030 §7.3. **Why:** the grid shows and edits one sort; the consumer orders
the data.

```ts
// @tellma/core-ui/grid
interface TmGridSort { readonly key: string; readonly direction: 'asc' | 'desc' }  // key: the column key
// ɵTmGridBase
readonly orderBy = model<TmGridSort | null>(null);
// TmGridColumn
readonly sortable = input(false, { transform: booleanAttribute }); // requires `key` (dev error otherwise)
```

- **Cycle.** A click on a sortable header (press and release, no drag, no modifier) cycles
  `asc → desc → null`. A click on another sortable column sets `{ key, 'asc' }`: one sorted column.
  A drag across headers still selects columns, and Shift+click and Mod+click keep their selection
  meaning. Non-sortable headers keep press-to-select.
- **Indicator.** Sortable headers reserve a 14 px slot after the label for `arrow-up` or
  `arrow-down` (inline SVG, `aria-hidden`). The slot is always present, so sorting never shifts the
  label. Hover previews the next direction faintly.
- **ARIA.** The sorted column's `role="columnheader"` carries `aria-sort="ascending"` or
  `"descending"`; other headers carry none. A change announces `tmUi.grid.announce.sorted` ("Sorted
  by {column}, {direction, select, asc {ascending} other {descending}}") or `sortCleared`.
- **Nothing else happens on a change.** No reorder and no selection change; the active cell
  follows its row when the consumer swaps the data (0004 §5.3).
- **RTL.** The arrows are vertical and never mirror; the slot follows the label in reading order.

### §C.4 Header navigation and the header action slot

**Target:** 0004 §6.1, §8.2, §8.3, §14. **Requested by:** spec 0030 §9.1. **Why:** a filter button
per column header, reachable from the keyboard without adding tab stops.

```ts
// TmGridColumn: a new content template beside *tmGridHeader
@Directive({ selector: 'ng-template[tmGridHeaderEnd]' })
declare class TmGridHeaderEndDef {}
interface TmGridHeaderEndContext { readonly $implicit: string; readonly key: string | undefined; readonly sort: 'asc' | 'desc' | null }

// @tellma/core-ui/grid: the header action button
@Directive({ selector: 'button[tmGridHeaderAction]' })
declare class TmGridHeaderAction {
  readonly active = input(false, { transform: booleanAttribute }); // accent styling and a hidden "Active" description
}
```

**Example —** a column filter button (spec 0030's column template):

```html
<tm-grid-column key="Name" header="Name" sortable>
  <ng-template tmGridHeaderEnd>
    <button tmGridHeaderAction [active]="filtered('Name')" [tmPopoverTriggerFor]="nameFilter"
            [attr.aria-label]="filterLabel('Name')"><tm-icon name="funnel" /></button>
  </ng-template>
</tm-grid-column>
```

- **Layout.** A header holds, in order, the label (or `*tmGridHeader`), the sort slot and the
  header-end content. The action is a 24 × 24 px target inside the header height. Every column that
  declares `tmGridHeaderEnd` reserves the slot, so toggling `active` moves nothing.
- **The open indicator** is the action's `active` state; the header draws no underline of its own.
- **Header navigation turns on when any column is `sortable` or declares `tmGridHeaderEnd`:**

  | Key (focus on a header cell) | Effect |
  |---|---|
  | ArrowUp on the first data row | focus moves to the active column's header cell |
  | ArrowDown | back to the first data row, same column |
  | ArrowLeft / ArrowRight, Home / End | between header cells (direction-mapped) |
  | Enter or Space | cycles the sort on a sortable column; toggles select-all on its header |
  | Alt+ArrowDown | clicks the header's first `[tmGridHeaderAction]` (opens the filter) |
  | Tab | leaves the grid; the single tab stop stands |

- **Focus mechanics.** Header cells carry a roving `tabindex` and `aria-describedby` to a hidden
  hint, `tmUi.grid.headerNavHint`: "Enter to sort. Alt+Down Arrow to filter." Header actions get
  `tabindex="-1"`. Focus inside a header action counts as header focus of that column, so a popover
  closing back onto its button continues header navigation.
- **Without sortable columns or header actions,** headers behave exactly as today: no navigation
  into the header row, and projected header content stays in the tab order.
- **Overlays opened from a header** render in place (CDK's inline top layer); the grid's existing
  `inOverlay()` filter already ignores their events.

### §C.5 Select-all across pages

**Target:** 0004 §8.8. **Requested by:** spec 0030 §12.1. **Why:** a selection survives paging.

Select-all acts on the current rows and keeps every id selected on other pages.

- **The tri-state** counts only the current `data` rows, as today.
- **The toggle** is a header click, Space on its header cell, or Ctrl+Shift+Space.
  - From `all`, it removes the current rows' ids and keeps every other id.
  - From `none` or `mixed`, it adds every current row's id to the existing set.
  - Today's toggle replaces the set with the current rows, or empties it.
- **Announcement.** With no ids outside the current rows, it is unchanged ("3 of 20 selected").
  With such ids, it is `tmUi.grid.announce.checkedCountAcross` ("{onPage, number} of {total,
  number} selected here, {selected, number} in all").
- **Compatibility.** Identical to today whenever `selectedIds` holds only current rows.

**Example —** Farah checks 3 Customers on page 1, then moves to page 2 (20 rows) and toggles
select-all. The set now holds 23 ids, and the grid announces "20 of 20 selected here, 23 in all".
Toggling again leaves the 3 from page 1.

### §C.6 `rowClass` and `rowHeaders`

**Target:** 0004 §4, §7. **Requested by:** spec 0030 §3.3, §16.4; spec 0034 §5.3. **Why:** muted
tree ancestors, an emphasised totals row, and lists without row numbers.

```ts
// ɵTmGridBase
readonly rowClass = input<((row: T) => string | readonly string[] | null) | undefined>(undefined);
readonly rowHeaders = input(true, { transform: booleanAttribute }); // false: no row-number column, no corner
```

- **`rowClass`** runs per rendered row (the window only) and must be pure. Classes land on the
  `role="row"` element. Cells paint row backgrounds, so a class styles a row through inherited
  custom properties the cells read: `--tm-grid-row-bg`, `--tm-grid-row-ink`,
  `--tm-grid-row-weight`. Selection, checked and active states paint over them.
- **Two shipped modifiers** cover the platform's needs without consumer CSS:
  - `tm-grid-row--muted` — secondary ink, for a tree ancestor shown only because a descendant
    matched;
  - `tm-grid-row--emphasis` — weight 600 on `surface.subtle`, for a totals row, which is the last
    data row.
- **`rowHeaders: false`** drops the row-number column and the corner. Mod+A and Shift+Space keep
  the selections those headers offered by pointer.

**Example —** a tree search mutes ancestors: `[rowClass]="r => matched.has(r.id) ? null :
'tm-grid-row--muted'"`.

### §C.7 `scrollbar-gutter`

**Target:** 0004 §4. **Requested by:** spec 0030 §3.3. **Why:** arriving rows never re-flow the
columns.

- **`.tm-grid__scroller { scrollbar-gutter: stable; }`.** Rows arriving never re-flow `flex`
  columns on classic-scrollbar platforms (Windows, Linux). Overlay scrollbars (the macOS default,
  mobile) reserve nothing and need nothing.
- **The gutter's header band** paints the header colour (a two-stop background on the scroller), so
  the header row reads as continuous.
- **Compatibility.** On classic-scrollbar platforms, proportional columns lose one scrollbar width
  even without overflow; fixed-width columns are untouched.
- **Test.** Headless Chromium runs without scrollbars, so the pinning e2e launches with
  `ignoreDefaultArgs: ['--hide-scrollbars']` and asserts that a column's width is equal before and
  after rows load.

### §C.8 `scrollToRow`

**Target:** 0004 §4, §12. **Requested by:** spec 0031 §8.4, §14.1. **Why:** the child-row deep
link lands on a row, and the error summary lands on a cell.

```ts
// ɵTmGridBase
scrollToRow(rowId: TmRowId, options?: {
  flash?: boolean;                // default false
  activate?: boolean;             // default true: the active cell moves to the row; DOM focus stays
  align?: 'nearest' | 'center';   // default 'center'
  column?: string;                // a column key the active cell lands on; default the current column
}): boolean;                      // false when the row is not in the data
```

- **Trees** expand the row's ancestors first (writing `expandedIds`, §C.9), as find does.
- **Focus is the caller's.** `scrollToRow` never moves DOM focus; a caller that wants it calls
  `grid.focus()` afterwards.
- **`column`** applies with `activate`. An unknown key keeps the current column, with a dev
  warning.
- **Flash.** The row's cells pulse `--grid-flash-bg` (a new token on `status.info.bg`) for
  `--grid-flash-duration` (1,200 ms). Under `prefers-reduced-motion` the tint holds still for the
  same time, then clears; under forced colors an outline replaces the tint. The flash is keyed by
  row id, so a recycled row element never carries it to another row.
- **Timing.** Call it once the row is in `data`; spec 0031 calls it from an effect on its child
  grid's rows. A row not yet present returns `false`.

**Example —** the error summary activates a failing cell: `grid.scrollToRow(42, { column:
'Quantity' }); grid.focus();`. The deep link `?child=Lines:42` calls `grid.scrollToRow(42, {
flash: true })`.

### §C.9 Tree `expandedIds`, batched `loadChildren` and sibling order

**Target:** 0004 §12, §13.2, §13.3. **Requested by:** spec 0030 §16.3, §16.5; spec 0025 §12.
**Why:** a remembered tree restores in one batch per level, and a server order sorts siblings.

```ts
// TmTreeGrid
readonly expandedIds = model<ReadonlySet<TmRowId> | undefined>(undefined);   // new; undefined = unbound
readonly loadChildren = input<((rows: readonly T[]) => Promise<void>) | undefined>(undefined);
// old: loadChildren?: (row: T) => Promise<void>
```

- **Precedence at content bind.** A bound `expandedIds`, then the state store's expansion slice,
  then `defaultExpandedDepth`. While `expandedIds` is bound, the store's expansion slice is neither
  read nor written.
- **Two-way.** A user expand adds the id at request time (the spinner shows); a collapse removes it.
  Collapsing an ancestor of the active cell removes that ancestor only. A failed load removes its
  batch's ids and announces once.
- **Unknown ids are kept, never pruned.** A remembered grandchild arrives with a later batch, and a
  search may hide rows that the next query brings back.
- **Batching.** The grid collects every row that is in `expandedIds`, is marked `hasChildren` and
  has no loaded children. It calls `loadChildren` once with them, in model order, at three moments:
  1. at content bind and at restore (this replaces today's collapse of unloaded lazy rows);
  2. after a batch resolves, for the rows it brought in that are themselves in `expandedIds`, level
     by level;
  3. for user expands landing in the same task, coalesced in a microtask.
- **Engine: no change.** A bound set seeds through the existing bulk
  `TmGridEngine.restoreExpansion(ids)` (one flatten for a 500-id restore); user toggles keep using
  `setExpanded`.
- **Lifecycle.** A content switch or destroy discards in-flight completions (the existing
  load-generation guard). The consumer appends rows to its own array, as today.
- **Sibling order.** The flattening keeps the data array's order among siblings, so a server
  `OrderBy` sorts siblings.

**Example —** the remembered Centers tree (spec 0030 with spec 0025's `TmTreeExpansion`):

```html
<tm-tree-grid gridId="search.gl.Center" [data]="rows()" [rowId]="byId" [parentId]="parentOf"
              [hasChildren]="hasKids" [loadChildren]="loadMany"
              [expandedIds]="expansion()" (expandedIdsChange)="remember($event)">
```

**Compatibility:** `loadChildren` changes signature. The showcase tree story and the tree-grid
specs migrate to `rows => …`, and the API golden records it; no other consumer exists.

### §C.10 Menu

**Target:** 0004 §8.5 (flat items only; submenus deferred; "the icon registry remains a future
concern"). **Requested by:** spec 0027 §8.8, §10; spec 0030 §13.2; spec 0031 §10.1. **Why:** the
session, Export and Actions menus and the pin folders need submenus, groups, checkable and link
items, registry icons and platform key labels.

The menu stays data-driven; new entry kinds join the `TmMenuEntry` union.

```ts
// @tellma/core-ui/menu
type TmMenuIcon = TemplateRef<void> | string;   // string: a TmIconRegistry name, rendered by tm-icon (spec 0023 §3)

interface TmMenuItem {                  // id, label, labelKey, disabled and action() unchanged
  readonly icon?: TmMenuIcon;           // old: TemplateRef<void>
  readonly shortcut?: TmKeys;           // old: a pre-formatted string; TmKeys is spec 0023 §3's
  readonly description?: string;        // new: a muted second line
  readonly detail?: string;             // new: a trailing value ('English', 'v2.14.0'); joins the accessible name
  readonly role?: 'checkbox' | 'radio'; // new: menuitemcheckbox / menuitemradio
  readonly checked?: boolean;           // new: with role
  readonly keepOpen?: boolean;          // new: activation leaves the menu open (checkbox toggles)
  readonly lang?: string;               // new: the label's BCP 47 language, set as lang on the row (native language names)
}
interface TmMenuLink {                  // new
  readonly id: string; readonly label?: string; readonly labelKey?: string;
  readonly icon?: TmMenuIcon; readonly shortcut?: TmKeys; readonly detail?: string; readonly disabled?: boolean;
  readonly href: string;
  readonly target?: '_blank';           // an external glyph and a hidden "(opens in a new tab)"
  action?(): void;                      // when set, a plain activation runs it (SPA routing) instead of navigating
}
interface TmMenuSubmenu {               // new; one level only
  readonly id: string; readonly label?: string; readonly labelKey?: string;
  readonly icon?: TmMenuIcon; readonly shortcut?: TmKeys; readonly detail?: string; readonly disabled?: boolean;
  readonly items: readonly (TmMenuItem | TmMenuLink | TmMenuGroup | TmMenuSeparator)[];
}
interface TmMenuGroup {                 // new
  readonly group: true; readonly label?: string; readonly labelKey?: string;
  readonly items: readonly (TmMenuItem | TmMenuLink | TmMenuSubmenu)[];
}
type TmMenuEntry = TmMenuItem | TmMenuLink | TmMenuSubmenu | TmMenuGroup | TmMenuSeparator;

interface TmMenuOpenOptions { readonly restoreFocus?: HTMLElement; readonly initialFocus?: 'first' | 'last' } // initialFocus new

@Directive({ selector: 'button[tmMenuTriggerFor]' })   // new
declare class TmMenuTrigger { readonly tmMenuTriggerFor = input.required<TmMenu>() }

@Directive({ selector: 'ng-template[tmMenuHeader]' })  // new: a non-interactive block above the list
declare class TmMenuHeaderDef {}
```

**Example —** part of a session menu (spec 0027 §10 builds the real one):

```ts
readonly items = computed<readonly TmMenuEntry[]>(() => [
  { id: 'lang', labelKey: 'tm.session.language', icon: 'languages', detail: this.languageName(), items: [
    { group: true, items: this.languages().map((l) => ({ id: l.code, label: l.nativeName, lang: l.code,
      role: 'radio', checked: l.code === this.l10n.language(),
      action: () => this.l10n.setLanguage(l.code) })) },
  ] },
  { id: 'account', labelKey: 'tm.session.myAccount', icon: 'user', href: this.accountUrl(), target: '_blank' },
  { separator: true },
  { id: 'sign-out', labelKey: 'tm.session.signOut', icon: 'log-out', action: () => this.session.signOut() },
]);
```

- **Trigger (the APG menu button).** Sets `aria-haspopup="menu"`, `aria-expanded` and
  `aria-controls`. A click toggles; Enter, Space and ArrowDown open with focus on the first item;
  ArrowUp opens on the last. Focus returns to the trigger on Escape and after an activation.
- **Submenus** use aria's `MenuItem.submenu` input with a second anchored overlay.
  - Position: `inline-end`, aligned `start`, flipping to `inline-start`, and pushed on screen at
    narrow widths.
  - Open: ArrowRight (ArrowLeft in RTL), Enter, Space, hover after aria's 100 ms
    `expansionDelay`, or a tap.
  - Close: ArrowLeft (ArrowRight in RTL) or Escape, one level per press. Each level pushes its own
    entry on the shared Escape stack (§D.1).
  - An activation anywhere closes the whole tree. A submenu inside a submenu is a dev error.
- **Groups** render `role="group"` with `aria-labelledby` on a non-focusable heading, and a divider
  above every group but the first. Radio items must sit in a group (dev warning).
- **Checkable items** bind `role` and `aria-checked`; aria's `MenuItem` has a `role` input but no
  checked state. A check glyph fills the icon slot.
- **Links** render `<a ngMenuItem [href] [attr.target] rel="noopener">`.
  - A plain click or Enter runs `action()` with `preventDefault` when `action` is set, else follows
    the link.
  - Ctrl, ⌘, Shift and middle-click stay native.
  - The panel's `contextmenu` suppression skips links, so "Copy link address" works.
- **Icons.** A string `icon` renders `tm-icon` from the registry; a template renders as today. The
  icon slot sets `--icon-size: var(--menu-icon-size)`.
- **Shortcuts** are display only, `aria-hidden`, rendered through `tmFormatKeys` for the platform:
  `Mod+C` shows Ctrl and C on Windows and Linux, ⌘ and C on Apple. The grid's own context menu
  passes `Mod+X`-style keys instead of its pre-formatted strings.
- **Header slot.** `ng-template[tmMenuHeader]` renders above the list, outside `role="menu"`. The
  menu's `aria-describedby` references it, so it is read on open. It holds no interactive content
  (dev warning).
- **Typeahead** reads the label only. `detail` joins the accessible name ("Language, English");
  `shortcut` stays out of it.
- **Touch rows.** Below `sm`, rows are `--menu-item-height-touch` tall (a new token, 44 px). A
  `(width < 576px)` media query applies it, because the panel lives in the overlay container. Type
  sizes are unchanged, and the grid's context menu follows. Form controls follow §B.6.
- **RTL.** The submenu side and its arrow keys mirror through the live `Directionality` (§B.3).

**Compatibility:** existing `items` arrays render identically; `TmMenuItem.action` stays required;
`itemSelected` still emits `TmMenuItem` (links and submenu parents never emit it). The entry point
gains imports of `@tellma/core-ui/icon` and `@tellma/core-ui/kbd`.

### §C.11 Budgets

**Target:** 0004 §16; its 24 KB grid budget is replaced. **Requested by:** spec 0024 §12. **Why:**
the ceilings of record become the measured surface after Part C.

| Entry point | Ceiling (KB gzip) |
|---|---|
| `./grid` | 38 → **42** |
| `./grid-engine` | 15, unchanged (§C.9 reuses `restoreExpansion`) |
| `./tree-grid` | 8, unchanged |
| `./menu` | 6 → **7** |

The search route's budget (spec 0024 §12) carries the grid and its engine at about 57 KB. Loading
the editable-only machinery on demand would cut that; it is not part of this amendment (Part C
flag 6).

### §C.12 The published key map

**Target:** 0004 §8.2, §13 (the keyboard reference). **Requested by:** spec 0027 §4.3, §11.2.
**Why:** the shortcut registry refuses grid keys and the Shortcuts dialog lists them, without
restating 0004.

```ts
// @tellma/core-ui/grid
declare const TM_GRID_KEYS: readonly { readonly keys: TmKeys; readonly labelKey: string }[]; // TmKeys imported as a type
```

- **Contents.** One entry per key combination of the grid's navigation state, the tree additions
  and Part C's own keys (F2 in §C.1, Alt+ArrowDown on a header in §C.4), with label keys under
  `tmUi.grid.keys.*`. Entries that share a `labelKey` render as one help row.
- **Derived, never restated.** The list is built from the keymap's own binding table; a unit test
  asserts that every binding has an entry.
- **Consumers.** The shortcut registry refuses a registration equal to an entry that carries a
  modifier (spec 0027 §4.3); the Shortcuts dialog renders the list (spec 0027 §11.2).

**Example —** `{ keys: 'Mod+D', labelKey: 'tmUi.grid.keys.fillDown' }` renders as "Fill down" with
Ctrl D on Windows and ⌘ D on Apple.

### Part C — Testing

- **Unit.**
  - Grid input: row activation gestures and the 400 ms guard; printable keys unhandled in a
    readonly grid; the driver's keys and ARIA; header navigation.
  - Grid state: the sort cycle and `aria-sort`; select-all across pages; `rowClass`;
    `scrollToRow` with `column` and flash; `expandedIds` precedence, batching and sibling order.
  - The menu kinds and a row's `lang`; `TM_GRID_KEYS` covering every binding.
- **Playwright.**
  - The `grid-search-page` story: driver input, activation, sort, header actions with popovers,
    cross-page selection over a paged source, no row headers.
  - `menu-rich`, with 44 px rows at 390 px; a tree expansion restore; the gutter test with
    scrollbars shown.
  - Every new path in `dir=rtl`.
- **axe.** The new stories in both themes and directions.
- **Gates.** The `./grid`, `./tree-grid` and `./menu` goldens; the §C.11 ceilings.

### Part C — Definition of done

- **Grid behaviour:** §C.1–§C.9 on both grids; `TmGridDriver`, `TmGridHeaderAction`,
  `TmGridHeaderEndDef` and `TM_GRID_KEYS` exported. `TmGridHarness` gains `activateRow(i)`,
  `getActiveRowIndex()`, `sortBy(key)`, `getSort()`, `openHeaderAction(key)` and
  `isRowFlashing(i)`; `TmTreeGridHarness` gains `getExpandedIds()`.
- **Menu:** §C.10, with `TmMenuHarness.openSubmenu(label)`, `getGroups()`, `isChecked(label)`,
  `getLinkHref(label)` and a new `TmMenuTriggerHarness`; the grid's context menu on `TmKeys`.
- **Showcase:** the stories `grid-search-page`, `menu-rich` and a `tree-grid` expansion-restore
  variant.
- **Tokens:** `--grid-active-row-bg`, `--grid-flash-bg`, `--grid-flash-duration` and
  `--menu-item-height-touch` in the preset; the identity server's token CSS refreshed.
- **UI rules:**
  - **Instant feedback** — the active-row class lands in the press's frame (e2e).
  - **Non-blocking** — sort and header actions never wait on data; the consumer fetches.
  - **Stable layout** — the gutter e2e (§C.7); the sort and header-action slot geometry e2e.
  - **RTL** — every new e2e runs in `dir=rtl`: driver arrows, submenu side, header navigation.
  - **WCAG AA** — axe on the new stories in both themes and directions; keyboard-only e2e for
    activation, sort, filter open, cross-page selection and submenus; 44 px menu rows below `sm`
    (e2e).
- **Budgets:** the §C.11 ceilings, with notes recorded.

### Part C — Decisions record

1. **Row activation is opt-in and readonly-only; click and Enter open the row; F2 reaches a
   cell's link** — a list row opens from anywhere, and APG's "enter the cell" key still reaches
   links (§C.1).
2. **One activation payload for grid and tiles** — a page swaps views behind one handler; a
   separate tiles payload was the alternative (§C.1).
3. **The highlight is the active row, driven by `activeRowId` and `input[tmGridDriver]`** — no list
   page rewrites arrow handling; page-side key handling over a model was the alternative (§C.2).
4. **The driver's type stays in `grid`; tiles import it as a type** — a type-only import carries no
   code, so a move to `contracts` buys nothing (§C.2).
5. **The grid shows and edits sort state, never sorts** — 0004's non-goal stands; the consumer owns
   the order (§C.3).
6. **The header row is navigable only with a sortable column or a header action** — zero change for
   other grids; header buttons in the tab order would cost 2N stops before row 1 (§C.4).
7. **The action's `active` state is the open-filter indicator** — one indicator; no header
   underline (§C.4).
8. **Select-all toggles the current rows and keeps the others** — a selection survives paging
   (§C.5).
9. **A totals row is the last data row with `tm-grid-row--emphasis`** — no footer-row mechanism;
   and no row-selectable predicate, which no consumer needs (§C.6).
10. **`scrollToRow` takes `column` but never focuses** — the caller focuses with `grid.focus()`:
    one way to move focus (§C.8).
11. **`loadChildren` takes a batch; a bound `expandedIds` wins and keeps unknown ids** — one
    signature serves an expand and a restore; an additive `loadChildrenBatch` was the alternative
    (§C.9).
12. **Menus stay data-driven; new kinds are union members; submenus go one level** — `TmMenuItem`
    and `itemSelected` keep their types. No segmented group layout and no per-entry class hook: the
    shell's toggles are radio items (§C.10).
13. **One key-map export, with labels** — one list serves the conflict rule and the help dialog
    (§C.12).
14. **Ceilings re-baselined to the measured surface** — 0004's 24 KB was never met, and a ceiling
    that is not real guards nothing (§C.11).

### Part C — Review flags

1. **Enter opens the row; F2 reaches a cell's link** (§C.1). Alternative: Enter actuates a cell
   link when the active cell has one, else opens the row. Flips if keyboard users mostly follow
   navigation links from lists.
2. **Header navigation only when a column is sortable or has a header action** (§C.4).
   Alternative: always-navigable headers. Flips if consistency across grids proves worth more than
   zero change for line-entry grids.
3. **Mod+click opens a new tab in row mode** (§C.1), giving up Mod+click "add a range"; Mod+drag
   keeps it. Flips if users multi-range-copy from search results by clicking.
4. **The sort cycle includes "none"** (§C.3), rather than ascending and descending only. Flips if
   users find the third click surprising.
5. **`loadChildren` becomes batch-only, a breaking signature** (§C.9). Alternative: an additive
   `loadChildrenBatch`. Flips if an external consumer of the 0.x releases appears before Part C
   ships.
6. **The grid ceiling re-baselined to 42 KB with no split** (§C.11). Alternative: load the
   editable-only machinery (editors, the editor session, the paste ladder, fill, the status bar,
   touch handles) through `import()` on the first `field` bind or paste, about 8–10 KB off the
   search route. Flips if spec 0024's search-route budget is exceeded.
7. **Submenus one level deep** (§C.10). Flips with a consumer that needs a second level.

## Part D — Basics (spec 0005)

Part D changes the basic components and the shared infrastructure of spec 0005: the overlay
helpers, the modal and popover, the l10n formats, the client cache, the image and the button. Its
consumers are spec 0023 (overlay, alert dialogs, avatars, icons, relative time), the shell and
attention surfaces (specs 0027, 0028), the page frameworks (specs 0030, 0031, 0035), the report
renderers (specs 0033, 0034) and the app foundation (specs 0024, 0025).

### §D.1 The public overlay entry point

**Target:** 0005 §2.1. **Requested by:** spec 0023 §2, spec 0027 §9.1, spec 0028 §6.2. **Why:**
overlays built outside the library share its anchoring and its one Escape stack.

```ts
// @tellma/core-ui/overlay — a new public entry point; shapes unchanged from private/
declare function tmCreateAnchoredOverlay(config: TmAnchoredOverlayConfig): TmAnchoredOverlay;
declare function tmLogicalPositions(side: TmOverlaySide, align: TmOverlayAlign): ConnectedPosition[];
declare function tmPushEscapeDismissal(onEscape: () => void): () => void;
type TmOverlaySide = 'block-end' | 'block-start' | 'inline-start' | 'inline-end';
type TmOverlayAlign = 'start' | 'center' | 'end';
// and TmAnchoredOverlay, TmAnchoredOverlayConfig, TmAnchoredOverlayOrigin
```

- **The move.** `private/tm-anchored-overlay.ts` and `private/tm-dismiss-stack.ts` move to
  `overlay/`. Every library entry point imports `@tellma/core-ui/overlay`; `private` keeps only
  `ɵTmErrorPopover`. The popover golden stops leaking `TmOverlaySide` from an excluded entry point.
- **Two documented rules** come with the public contract:
  - **One Escape stack.** A surface that dismisses on Escape pushes onto `tmPushEscapeDismissal` on
    open and releases on close. It never adds its own document listener: two capture listeners on
    one node both run, so one Escape would close two layers.
  - **Inline top layer.** By default (`popoverHost: 'inline'`) the pane renders inside the origin's
    subtree. A container that delegates key or pointer events must ignore events from inside
    `.cdk-overlay-pane`. An anchor inside `role="row"` passes `popoverHost: { type: 'parent',
    element }`.

**Compatibility:** no shape changes; the library's own imports move. **Tests:** the existing
overlay suites under the new entry point; the new golden. **Budget:** `./overlay` ≈ 1.5 KB;
`./private` 2.54 → ≈ 1.1 KB.

### §D.2 `TmModal`: the drawer, roles, live direction and the `sm` query

**Target:** 0005 §10. **Requested by:** spec 0023 §5 (confirm and error dialogs), spec 0028 §6.4,
§7.2, spec 0035 §13.4. **Why:** side panels on narrow screens, APG alert dialogs, and modals that
follow a live direction switch.

```ts
interface TmModalConfig {           // existing members unchanged
  placement?: 'center' | 'end';     // new; default 'center'. 'end' is a modal side sheet: the drawer
  role?: 'dialog' | 'alertdialog';  // new; default 'dialog'; passed to CDK DialogConfig.role
  describedBy?: string;             // new: an element id inside the content; passed to DialogConfig.ariaDescribedBy
}
```

**The drawer (`placement: 'end'`):**

- **Geometry.** Docked to the inline-end edge — right in LTR, left in RTL — through CDK's
  `GlobalPositionStrategy.end('0').top('0')`, at full height (`100dvh`). `size` picks the width:
  `--modal-drawer-width-sm` 360 px, `-md` 480 px (the default), `-lg` 720 px (new tokens). Below
  `sm` the drawer takes the full width.
- **Same shell, same rules.** The header (title, X), a scrolling body and the footer slot; focus
  trap and restore; Escape and backdrop dismissal with `canDismiss`; stacking; the scrim. It never
  resizes while open.
- **No motion.** The drawer appears in the frame of the click, with no slide. The centered modal's
  fade is unchanged.

**Example —** the tray on a phone (spec 0028 §7.2): `modal.open(TmTrayPanel, { placement: 'end',
size: 'sm', title })`.

**Roles.** `role: 'alertdialog'` is for a dialog that interrupts with a message the user must
answer, such as spec 0023's confirm. `describedBy` names the message element, so a screen reader
reads it on open. An alert dialog without `describedBy` is a dev warning.

**Live direction:**

- **Creation.** `TmModal` passes no `direction` to CDK. The overlay therefore takes the live root
  `Directionality` (§B.3), and the content injects the same live instance. Had `direction` been
  set, CDK `Dialog` would give the content a static `Directionality`.
- **While open.** Each open modal subscribes to the root's `change` and calls
  `overlayRef.setDirection(dir)`, then `updatePosition()`. An open drawer moves to the other edge,
  and an open centered modal mirrors its content.

**The `sm` query.** The modal shell's `@media (max-width: 600px)` becomes `@media (width < 576px)`
(§B.1). Centered sizes converge to near-full-screen below `sm`. Between 576 and 600 px, a `md`
modal keeps its 640 px width, clamped by `max-inline-size`, so nothing visible changes.

**Compatibility:** the defaults keep today's centered dialog. **Tests:** unit — placement classes;
no `direction` passed; `role` and `ariaDescribedBy` reach CDK. e2e — the drawer at 390 (full
width), 768 and 1440 px in LTR and RTL; flipping `<html dir>` with a drawer open moves its edge;
focus trap and Escape; axe, an alert dialog included. **Budget:** `./modal` 2.97 → ≈ 3.4 KB.

### §D.3 `tm-popover` sizes

**Target:** 0005 §11. **Requested by:** spec 0030 §9.1, §10.1, §11.1; spec 0028 §6.2. **Why:**
the filter, column-editor and notification panels read alike and never resize.

```ts
// TmPopover — new inputs
readonly size = input<'auto' | 'sm' | 'md' | 'lg'>('auto'); // 'auto': today's content width, up to --popover-max-inline-size
readonly panelClass = input<string | undefined>(undefined); // the hatch, as on TmModal
```

- **Fixed widths.** `sm` 320 px, `md` 440 px, `lg` 640 px (`--popover-width-sm|md|lg`, new
  tokens). A fixed size never changes while open, whatever the content loads.
- **Clamping.** Every size is capped at `100vw − 2 × --popover-viewport-margin` (8 px, a new
  token), so a `lg` popover on a phone is full width. The panel's block size is capped at the space
  the chosen side offers, and the content scrolls inside.
- **Consumers.** Column filter popups use `sm`; the advanced filter and the notifications panel
  `md`; the column editor `lg`.

**Compatibility:** `auto` is the default; existing popovers are unchanged. **Tests:** unit — size
classes and clamping; e2e — the geometry stays fixed while content loads. **Budget:** `./popover`
3.09 → ≈ 3.2 KB.

### §D.4 Date-time, time of day and relative time

**Target:** 0005 §2.2 (the closed format set). **Requested by:** spec 0023 §4
(`tm-relative-time`), spec 0028 §6.2, spec 0035 §8.5; spec 0024 §7.1 fills the zone. **Why:**
"2 min ago" and "Updated 10:42" format instants in the display zone.

The closed format set gains three formats over **instants**, each with golden rows.

```ts
// @tellma/core-ui/l10n — pure, DOM-free, DI-free
type TmTimeStyle = 'short' | 'medium';  // short: hour and minute; medium: adds seconds; the hour cycle per locale
interface TmDateTimeFormatOptions {
  readonly timeZone: string;             // an IANA id; required
  readonly calendar?: TmCalendar;        // default Gregorian
  readonly dateStyle?: TmDateStyle;      // default 'numeric'
  readonly timeStyle?: TmTimeStyle;      // default 'short'; §D.8 adds 'none'
}
interface TmTimeFormatOptions { readonly timeZone: string; readonly timeStyle?: TmTimeStyle }
interface TmRelativeTimeFormatOptions {
  readonly now: number;                  // epoch ms: the caller's clock
  readonly timeZone: string;             // decides calendar days
  readonly calendar?: TmCalendar;        // for the absolute fallback
  readonly style?: 'long' | 'short' | 'narrow'; // default 'short'
}
declare function tmFormatDateTime(instant: string | null | undefined, locale: string, options: TmDateTimeFormatOptions): string;
declare function tmFormatTime(instant: string | null | undefined, locale: string, options: TmTimeFormatOptions): string;
declare function tmFormatRelativeTime(instant: string | null | undefined, locale: string, options: TmRelativeTimeFormatOptions): string;

// @tellma/core-ui (primary)
declare const TM_TIME_ZONE: InjectionToken<Signal<string>>; // default: the browser's resolved zone; @tellma/core provides the display zone
// TmL10n gains (each reads locale, calendar and timeZone first):
readonly timeZone: Signal<string>;
formatDateTime(instant: string | null | undefined, options?: { dateStyle?: TmDateStyle; timeStyle?: TmTimeStyle }): string;
formatTime(instant: string | null | undefined, options?: { timeStyle?: TmTimeStyle }): string;
formatRelativeTime(instant: string | null | undefined, options: { now: number; style?: 'long' | 'short' | 'narrow' }): string;
```

- **Instants** are ISO 8601 strings with `Z` or a `±hh:mm` offset (the wire form of
  `DateTimeOffset`). A fraction longer than three digits is truncated to milliseconds before
  parsing. `''`, `null` and `undefined` format as `''`. A string with no offset, or a malformed
  one, is returned as-is with a dev warning, as the date codec does.
- **Date-time** formats with the date style's fixed skeleton plus `hour: 'numeric', minute:
  '2-digit'`, in the zone and the calendar; `medium` adds `second`. The era is stripped, as
  `tmFormatDate` does.
- **Relative time** compares Δ = `now − instant`, truncated, never rounded up:

  | \|Δ\| | Unit | Output (`Intl.RelativeTimeFormat`, `numeric: 'auto'`) |
  |---|---|---|
  | under 60 s | — | "now" |
  | under 60 min | minutes | "2 min ago", "in 5 min" |
  | under 24 h | hours | "3 hr ago" |
  | 24 h or more, and under 7 calendar days in the zone | days, by calendar-day difference | "yesterday", "3 days ago" |
  | otherwise | — | the absolute date: `tmFormatDate` style `medium` in the zone and calendar ("13 Oct 2026") |

  The strings shown are en-GB with `style: 'short'`. Every locale's output is CLDR's, pinned by
  the golden.
- **The ticking clock is the caller's.** Spec 0023's `tm-relative-time` owns its timer and passes
  `now`.
- **No time-of-day type.** A time without an instant (a `TimeOnly`) has no consumer and stays out.
- **The golden.** `l10n/format-golden.json` gains three arrays, which the server twin also reads:
  - `dateTime` and `time`, with rows `{ locale, timeZone, calendar, dateStyle, timeStyle, instant,
    expected }`;
  - `relative`, with rows `{ locale, timeZone, now, instant, style, expected }`.

  They cover the date rows' locales and the zones `UTC`, `Asia/Riyadh`, `Africa/Addis_Ababa` and
  `Asia/Beirut`. Beirut has DST, for the day-boundary rows.

**Example —** for `2026-10-20T07:42:00Z` in `Asia/Riyadh`: `l10n.formatTime(at)` gives "10:42" in
en-GB and "١٠:٤٢ ص" in ar-SA; `l10n.formatDateTime(at)` gives "20/10/2026, 10:42" in en-GB.

**Compatibility:** additive. **Tests:** the three golden arrays; unit — every threshold boundary,
truncation, a DST day boundary, malformed input. **Budget:** `./l10n` 7.65 → ≈ 8.9 KB with §D.7 and
§D.8; the primary token and facade within Part B's primary ceiling.

### §D.5 `TmClientCache.clearAll` sweeps every store

**Target:** 0005 §2.3. **Requested by:** spec 0025 §2.6, spec 0024 §8.4. **Why:** a session's end
leaves no `tm` data behind except what the caller keeps.

```ts
type TmClientCacheArea = 'cache-storage' | 'indexeddb' | 'local-storage' | 'session-storage';
interface TmClientCacheClearOptions { readonly keep?: (name: string, area: TmClientCacheArea) => boolean }
declare class TmClientCache {
  clearAll(options?: TmClientCacheClearOptions): Promise<void>; // old: clearAll(): Promise<void>, CacheStorage only
}
```

- **The naming rule.** CacheStorage caches and IndexedDB databases are named `tm-…`;
  `localStorage` and `sessionStorage` keys are named `tm.…`. The Angular service worker's `ngsw:…`
  caches never match.
- **What it deletes,** in parallel, minus `keep`:
  - CacheStorage `tm-*` (today `tm-images-v1`);
  - IndexedDB `tm-*`, enumerated with `indexedDB.databases()`; where that is absent, the area is
    skipped;
  - `localStorage` keys `tm.*`;
  - this tab's `sessionStorage` keys `tm.*`; other tabs clear their own on the sign-out broadcast
    (spec 0025).
- **Blocked deletes never hang it.** A database delete resolves its part of `clearAll` on
  `success`, `error` or `blocked`. A blocked delete stays queued, and later opens of that database
  wait behind it. Every opener of a `tm-*` database therefore closes its connection on
  `versionchange`.
- **Best effort.** It never throws, as today.
- **The library knows nothing of the boot record.** A caller keeps what it sweeps itself.

**Example —** the session sweep (spec 0025 §2.6; spec 0024 §8.4 runs it on sign-out):

```ts
await store.sweep('session-ended');                                      // key-range deletes inside tm-app
await cache.clearAll({ keep: (name) => name === 'tm-app' || name === 'tm.boot' });
store.boot.patch(…);                                                     // spec 0025: clears the boot record's user fields
```

**Tests:** unit in browser mode — each area deleted; `keep` honoured per area; a blocked IndexedDB
delete (a second open connection that ignores `versionchange`) resolves; a missing
`indexedDB.databases` skips the area; `ngsw:` caches untouched. **Budget:** primary +0.3 KB, within
Part B's primary ceiling.

### §D.6 `TM_BLOB_FETCHER` cancellation

**Target:** 0005 §7.1, §7.2. **Requested by:** spec 0024 §10.5. **Why:** an image request the user
abandons is aborted, never left to finish.

```ts
interface TmBlobFetchOptions {
  readonly ifNoneMatch?: string;
  readonly signal?: AbortSignal;   // new: aborts the request; a fetcher rejects with signal.reason
}
```

- **`tm-image`** aborts its wait when it is destroyed, when its `src` or `etag` changes, or when it
  leaves the `defer` viewport before the bytes arrive.
- **The blob cache's coalescing is reference-counted.** The shared request for a URL has its own
  `AbortController`, aborted only when every waiter has aborted. A background revalidation is
  aborted when no instance of that URL remains. An aborted request writes nothing to `tm-images-v1`
  and shows no error placeholder.
- **The default fetcher** (over `HttpClient`) unsubscribes on abort, which aborts the request. It
  rejects with `signal.reason`. Spec 0024's fetcher over `fetch` passes the signal straight
  through.

**Compatibility:** `signal` is optional, so a custom fetcher that ignores it still works, without
cancellation. **Tests:** unit — a destroy mid-fetch aborts; two instances on one URL abort only when
both go; a revalidation aborts when the last instance goes; no cache write after an abort.
**Budget:** `./image` 9.66 → ≈ 9.8 KB.

### §D.7 Month, quarter and year formats

**Target:** 0005 §2.2 (the date styles). **Requested by:** spec 0033 §3.2, spec 0034 §3.5.
**Why:** report terms display a date as its month, quarter or year, in an explicit calendar.

```ts
// @tellma/core-ui/l10n
type TmDateStyle = 'numeric' | 'medium' | 'long' | 'month' | 'quarter' | 'year' | 'weekday'; // old: 'numeric' | 'medium' | 'long'; weekday is §D.8
interface TmDateFormatOptions {
  readonly calendar?: TmCalendar;        // unchanged: default Gregorian
  readonly dateStyle?: TmDateStyle;      // unchanged: default 'numeric'
  readonly quarterPattern?: string;      // new: '{quarter}' and '{year}' placeholders; default 'Q{quarter} {year}'
}
```

- **`month`** uses the skeleton `yMMM`, and **`year`** uses `y`. Both format in the given calendar
  with the era stripped.
- **`quarter`** fills `quarterPattern` with the quarter number and the calendar year, both in the
  locale's digits. The quarter is `min(4, ⌈month / 3⌉)` of the date's month in that calendar, so
  Ethiopic's thirteenth month falls in the fourth quarter.
- **The pattern is an option.** Intl has no quarter field, and the l10n functions stay pure.
  `TmL10n.formatDate` passes the string `tmUi.l10n.quarter` ("Q{quarter} {year}"; Arabic "الربع
  {quarter} {year}"), so locale packs translate it.
- **Any day of the period** formats to the same text.

**Example —** the date `2026-03-15`:

| Locale | Calendar | `month` | `quarter` | `year` |
|---|---|---|---|---|
| en-GB | Gregorian | Mar 2026 | Q1 2026 | 2026 |
| ar-SA | Umm al-Qura | رمضان ١٤٤٧ | الربع ٣ ١٤٤٧ | ١٤٤٧ |

**Compatibility:** additive. **Tests:** golden rows per style over Gregorian, Umm al-Qura and
Ethiopic, Ethiopic's thirteenth month included. **Budget:** within §D.4's `./l10n` estimate.

### §D.8 The `weekday` style

**Target:** 0005 §2.2. **Requested by:** spec 0035 §15.2. **Why:** the home page's date line,
"Tuesday, 20 Oct 2026", in the user's calendar and the display zone.

- **The style.** `TmDateStyle` gains `'weekday'` (§D.7's sketch): the CLDR skeleton `yMMMEEEEd`,
  with the era stripped.
- **An instant's date in a zone.** The date-time formatter's `timeStyle` also takes `'none'`: the
  instant's calendar date in the zone, with no time.

```ts
// TmDateTimeFormatOptions, and the options of TmL10n.formatDateTime
readonly timeStyle?: TmTimeStyle | 'none';   // old: TmTimeStyle
```

**Example —** the home date line (spec 0035 §15.2): `l10n.formatDateTime(new Date(now).toISOString(),
{ dateStyle: 'weekday', timeStyle: 'none' })` gives "Tuesday, 20 Oct 2026" in en-GB, and
"الثلاثاء، ٩ جمادى الأولى ١٤٤٨" in ar-SA with Umm al-Qura.

**Compatibility:** additive. **Tests:** golden rows per calendar for `tmFormatDate` and for
`tmFormatDateTime` with `timeStyle: 'none'`, across a day boundary in `Asia/Riyadh`. **Budget:**
within §D.4's estimate.

### §D.9 `tm-image` `placeholderOnError`

**Target:** 0005 §7.4. **Requested by:** spec 0023 §4 (`tm-avatar`). **Why:** an avatar whose image
fails falls back to initials, not a broken-image glyph.

```ts
// TmImage — new input
readonly placeholderOnError = input(false, { transform: booleanAttribute }); // the error state renders the placeholder template
```

- **With `true`,** the error state renders the `ng-template[tmImagePlaceholder]` template instead
  of the broken-image glyph. The error stays announced through `alt`.
- **Without a placeholder template,** the glyph shows as today.

**Compatibility:** `false` by default. **Tests:** unit — a failing fetch renders the placeholder
with the flag and the glyph without it. **Budget:** within `./image`'s ceiling.

### §D.10 `tmButton` sizes `tm-icon` children

**Target:** 0005 §3. **Requested by:** spec 0023 §3. **Why:** `<button tmButton><tm-icon
name="plus" /></button>` sizes and hides like a projected `svg`.

- **Size.** `.tm-button` sets `--icon-size: var(--button-icon-size)`, which `tm-icon` reads.
- **The projected-icon rule** (`flex: none`, `pointer-events: none`) covers `tm-icon` children as
  it covers `svg`.
- **Pending.** The existing rule already hides every element child but the spinner, `tm-icon`
  included.

**Compatibility:** CSS only; `svg` children are unchanged. **Tests:** e2e geometry — a `tm-icon`
child at each button size; the pending state hides it.

### Part D — Testing

| Suite | Pins |
|---|---|
| Unit | the overlay suites under the new entry point; modal placement classes, roles and live direction; popover sizes and clamping; the formatters' thresholds and malformed input; `clearAll` per area; blob abort reference counting; `placeholderOnError` |
| Format golden | `dateTime`, `time` and `relative`; the `month`, `quarter`, `year` and `weekday` styles over three calendars |
| Playwright | the drawer at 390, 768 and 1440 px in LTR and RTL, with a live flip; fixed popover geometry while content loads; aborted image requests counted on navigation; `tm-icon` in buttons |
| axe | the drawer, an alert dialog and the popover sizes, in both themes and directions |
| Gates | the new `./overlay` golden; the `./l10n`, `./modal`, `./popover` and `./image` goldens; the ceilings below |

### Part D — Definition of done

- **Overlay:** `@tellma/core-ui/overlay` public with a golden and a docs page; `private` reduced;
  every importer migrated (§D.1).
- **Modal and popover:** §D.2 and §D.3 shipped, with `TmModalHarness.getPlacement()` and
  `TmPopoverHarness.getSize()`; the drawer and popover tokens in the preset; the identity server's
  token CSS refreshed.
- **Formats:** §D.4, §D.7 and §D.8 shipped, with `TM_TIME_ZONE`, the facade methods, the
  `tmUi.l10n.quarter` string and its Arabic twin; the golden diff reviewed through `golden:approve`.
- **Cache, blobs, image and button:** §D.5, §D.6, §D.9 and §D.10 shipped.
- **Showcase:** the stories `modal` (drawer variants, an alert dialog), `popover` (sizes) and
  `i18n` (date-time, time, and relative with a fake clock, per zone; month, quarter, year and
  weekday per calendar).
- **UI rules:**
  - **Instant feedback** — the drawer renders in the click's frame (e2e: no animation; the panel is
    present one frame after the click).
  - **Non-blocking** — image requests abort on navigation (§D.6 unit, and an e2e counting aborted
    requests).
  - **Stable layout** — fixed popover sizes and the drawer never resize while content loads
    (geometry e2e).
  - **RTL** — the drawer's edge and the live flip (§D.2 e2e); the Arabic golden rows.
  - **WCAG AA** — axe on the drawer, the alert dialog and the popover sizes in both themes and
    directions.
- **Budgets:** `./overlay` **2** (new); `./private` 4 → **2**; `./l10n` 8 → **9**; `./modal` 6,
  `./popover` 4, `./image` 10 and `./button` 2 unchanged; notes recorded.

### Part D — Decisions record

1. **The overlay helpers become a public `@tellma/core-ui/overlay`, unchanged** — shell overlays
   share the one Escape stack; copies in `@tellma/core` were the alternative (§D.1).
2. **The drawer is `TmModal` with `placement: 'end'`, with no slide** — one modal shell and one
   focus model, and nothing the user waits for animates (§D.2).
3. **`TmModal` takes no `direction` option** — the live root `Directionality` (§B.3) serves every
   overlay; a per-modal option would hand content a static one (§D.2).
4. **Alert dialogs are a `role` on `TmModal`** — spec 0023's confirm and error dialogs reuse the
   shell (§D.2).
5. **Popovers get fixed widths and `panelClass`** — panels read alike and never resize; free width
   was the alternative (§D.3).
6. **Instants format in an explicit zone; relative time takes `now`** — deterministic and
   golden-testable; the browser's zone and an internal clock were the alternatives (§D.4).
7. **One relative form, and "now" is Intl's word** — no compact "age" form and no message key: one
   function, CLDR's text in every locale (§D.4).
8. **`clearAll({ keep })`; the library never learns the boot record** — a library cannot spare
   fields inside a key, and deleting `tm-app` would queue every later open behind a blocked delete
   (§D.5).
9. **The quarter label is a translatable pattern** — Intl has no quarter field, and the pure
   functions stay DI-free (§D.7).
10. **`weekday` is a date style, and `timeStyle: 'none'` gives an instant's date in a zone** — no
    separate zoned-parts helper; the formatters already resolve the zone (§D.8).
11. **No `prerender` on `tm-tab-group`** — every details extra is eager, so no unvisited panel
    holds heavy content; 0005 §9's `preserveContent` keeps visited panels.

### Part D — Review flags

1. **Relative-time thresholds and truncation, with the absolute date from 7 calendar days**
   (§D.4). Flips with design feedback on "3 days ago" against "13 Oct 2026".
2. **Fixed popover widths of 320, 440 and 640 px, and drawer widths of 360, 480 and 720 px**
   (§D.2, §D.3). Flips with design review of the filter, column-editor and bell panels.
3. **`clearAll({ keep })` instead of a boot-record-aware sweep** (§D.5). Flips if callers beyond
   spec 0025 need the same `keep` set; a named preset would follow.
4. **Ethiopic's thirteenth month in the fourth quarter** (§D.7). Alternative: no `quarter` style
   for Ethiopic. Flips if the server's `quarter(d, 'et')` adopts another rule.
5. **`timeStyle: 'none'` on the date-time formatter** (§D.8). Alternative: a separate
   `tmFormatZonedDate(instant, locale, { timeZone, calendar, dateStyle })`. Flips if the option
   makes the date-time contract hard to read.

## Part E — Entity picker (spec 0006)

Part E extends the entity picker for the client entity model's generic binding (spec 0029 §14)
and its navigation filters (spec 0029 §13). Every member is additive: `tm-entity-picker` without
the new inputs renders as today.

### §E.1 Two-line options

**Target:** 0006 §2, §4.1, and its Non-goal "Rich option templates" (lifted for a second line).
**Requested by:** spec 0029 §14.2. **Why:** two Centers named "Jeddah" are told apart by their
code and parent.

```ts
// TmEntityPicker — new input
readonly itemDescription = input<((item: T) => string | null) | undefined>(undefined);
```

- **Rows.** With `itemDescription` bound, every result row is two lines: the label, then the
  description in secondary ink at `--text-xs`, each ellipsized.
- **One height.** All result rows share `--entity-picker-option-height-2line` (a new token), even
  a row whose description is `null`. The panel never re-measures between queries. Footer rows
  (Advanced search…, Create…, Edit…) keep the one-line height.
- **Accessible name.** "label, description", with a visually hidden separator, so a screen reader
  hears the disambiguating line.
- **What the description holds** is the binding's choice (spec 0029 §14.2).

**Example —** `[itemDescription]="c => c.code + ' · ' + parentName(c)"`.

**Compatibility:** unbound, rows render as today. **Tests:** unit — the fixed row height with and
without descriptions; the accessible name. **Budget:** +0.3 KB.

### §E.2 `tm-entity-multi-picker`

**Target:** 0006 §2, §3, §4, §6, and its Non-goal "Multi-select (tag/chip input)", which is lifted;
`tm-entity-picker` stays a scalar picker. **Requested by:** spec 0029 §13.3. **Why:** the navigation
`in:` filters choose several entities in one field, with the single picker's search lifecycle.

A new component, `tm-entity-multi-picker`, shows the chosen entities as chips and wraps a single
picker for the search.

```ts
// @tellma/core-ui/entity-picker — new; implements TmFormFieldControl, FormValueControl<readonly Id[]>
@Component({ selector: 'tm-entity-multi-picker' })
declare class TmEntityMultiPicker<T, Id extends TmEntityId = TmEntityId> {
  readonly value = model<readonly Id[]>([]);                  // in pick order; never duplicates
  readonly search = input.required<TmEntitySearchFn<T>>();
  readonly itemId = input.required<(item: T) => Id>();
  readonly itemLabel = input.required<(item: T) => string>();
  readonly itemDescription = input<((item: T) => string | null) | undefined>(undefined);
  readonly displayWith = input<((id: Id) => string | null) | undefined>(undefined); // chip labels
  readonly advancedSearch = input<TmEntityPickerPage | undefined>(undefined);
  readonly create = input<TmEntityPickerPage | undefined>(undefined);
  readonly maxItems = input<number | undefined>(undefined);
  // placeholder, searchDebounce, createLabel, aria-label, aria-describedby, disabled, readonly,
  // required, invalid, touched, pending, errors, touch: as TmEntityPicker. No `edit`.
  readonly picked = output<TmEntityPicked<T, Id>>();          // each addition
  readonly removed = output<Id>();                            // each removal
  focus(): void;
  openDropdown(): void;
}
// TmEntityPickerPageData gains multiple and ids (§E.3's sketch);
// a page opened with multiple may close with readonly TmEntityPick[]: each new id is added
```

**Example —** a Parent filter (spec 0029 binds it inside its filter control):

```html
<tm-form-field label="Parent">
  <tm-entity-multi-picker [formField]="filter.parents" [search]="centers.search" [itemId]="byId"
                          [itemLabel]="centers.label" [displayWith]="centers.labelOf" [maxItems]="50" />
</tm-form-field>
```

**Composition.** The field holds two parts:

- a chip row of spec 0023's removable `tm-chip`s, focused through `TmChip.focus()` (spec 0023
  §4.3);
- an inner `tm-entity-picker` in an internal `ɵcommitMode: 'emit'`.

The rules of the composition:

- **`'emit'` changes only the single picker's post-commit step.** A commit comes from the list,
  from auto-resolution on blur or Enter, or from the create page. In emit mode, the inner picker
  emits `picked` and returns to empty and pristine:
  - its own `value` stays `null`, and the settled answer is retired;
  - the panel stays open on the browse list.

  Search, debounce, abort, resolution and the footer rows are the single picker's, untouched.
- **Toggle.** The multi-picker passes its chosen ids in (`ɵchosenIds`). Those rows show the check
  and `aria-selected="true"`, and the listbox is `aria-multiselectable`. Picking a chosen row
  removes it.
- **Advanced search** opens with `{ multiple: true, ids }`. A page returning an array adds every id
  not yet chosen; a single pick adds one. Create adds the created entity. Edit is not offered.
- **`maxItems` reached.** Further picks are refused with the status message
  `tmUi.entityPicker.maxItems` ("You can choose up to {max, number}"), announced politely.

**Example —** Farah filters Centers by parent:

1. She types "Jed", and the inner picker searches as the single picker does.
2. She picks Jeddah. `picked` fires, a Jeddah chip appears, and the input empties. The panel stays
   open on the browse list.
3. She picks Riyadh, then picks Jeddah again. The Jeddah row showed a check, so this removes its
   chip.

**Field, chips and stable layout:**

- **One line, at a fixed height** (the size ladder). Chips sit before the input. The field never
  grows, so nothing below it moves.
- **Overflow.** The chip row scrolls horizontally, with its scrollbar hidden; wheel, trackpad and
  keyboard scroll it. A fixed-width count badge at the inline end shows the total.
- **Chip labels** follow the single picker's ladder: `displayWith(id)`, else the pick's label
  memo, else `String(id)` with a dev warning.

**Keyboard:**

| Key | Where | Effect |
|---|---|---|
| typing, ArrowDown, Alt+ArrowDown, Enter, Tab, Escape | input | as the single picker (0006 §4.3), with toggle semantics |
| ArrowLeft (ArrowRight in RTL) at the caret's start | input | focus moves to the last chip |
| ArrowLeft / ArrowRight, Home / End | chips | roving focus between chips (direction-mapped); past the last chip, back to the input |
| Backspace | empty input | the first press focuses the last chip; a second press removes it |
| Backspace / Delete | chip | removes it; focus moves to the next chip, else the previous, else the input (spec 0023 §2.1) |

**ARIA:**

- **Roles.** The input stays the combobox. The chip row is `role="list"` of `role="listitem"`,
  each a `tm-chip` whose remove button carries its own name (spec 0023 §4.3).
- **Summary.** The input's `aria-describedby` adds a hidden summary, `selectedSummary`
  ("{count, plural, one {# selected} other {# selected}}: Jeddah, Riyadh").
- **Announcements.** Additions and removals announce `announce.added` and `announce.removed`.

**Other rules:**

- **Grid.** There is no multi-entity cell editor; grid cells hold scalar ids.
- **Compatibility.** `tm-entity-picker` without `ɵcommitMode` is unchanged. The entry point's
  budget rises, but an app that imports only `TmEntityPicker` tree-shakes the multi-picker
  (`sideEffects: false`).
- **Harness.** `TmEntityMultiPickerHarness` (`getChipLabels`, `removeChip(label)`, `search(text)`,
  `pick(label)`, `getCountBadge`).

**Tests:** unit — the single picker's suite in emit mode (a commit leaves `value` `null` and the
text pristine, the settled answer retired, the panel open); toggle-remove; `maxItems`; advanced
search returning several picks. e2e — chip keyboard paths in LTR and RTL; the field's height with 0,
1 and 30 chips.

### §E.3 A context for picker pages

**Target:** 0006 §2, §6 (`TmEntityPickerPage`, `TmEntityPickerPageData`). **Requested by:** spec
0029 §14.4. **Why:** one shim page serves every entity.

```ts
type TmEntityPickerPage =
  | Type<unknown>
  | { component: Type<unknown>; size?: 'sm' | 'md' | 'lg'; title?: string; context?: unknown };  // context new

interface TmEntityPickerPageData<Id extends TmEntityId = TmEntityId> {
  readonly query: string;          // unchanged
  readonly id?: Id;                // unchanged: edit launches
  readonly multiple?: true;        // new (§E.2): opened from a multi-picker
  readonly ids?: readonly Id[];    // new (§E.2): the ids already chosen
  readonly context?: unknown;      // new: the object form's context, passed through untouched
}
```

- **Opaque.** The picker never reads `context`; the page casts it to the shape its binding agreed.
- **The bare `Type` form** passes no context.

**Example —** a binding hands one search page the entity it serves (spec 0029 §14.4):
`advancedSearch = { component: searchPage, size: 'lg', context: { entity: 'acme.Customer' } }`.

**Compatibility:** additive. **Tests:** unit — the context reaches `TM_MODAL_DATA` for advanced
search, create and edit.

### §E.4 Two-line options in grid cells

**Target:** 0006 §7.1 (the `entity` column's data seam). **Requested by:** spec 0029 §14.5.
**Why:** a grid cell's dropdown tells entities apart as the field does.

```ts
// TmGridColumn — beside itemId and itemLabel
readonly itemDescription = input<((item: never) => string | null) | undefined>(undefined);
```

- **The cell editor's dropdown** renders two-line rows as §E.1 does. The cell's display text stays
  the label only.

**Compatibility:** unbound, cells render as today. **Tests:** unit — the cell editor passes the
function to its picker; e2e — two-line rows in a cell's dropdown at the fixed height.

### Part E — Testing

| Suite | Pins |
|---|---|
| Unit | two-line rows at the fixed height; the single picker's suite in emit mode; multi-picker toggle, `maxItems` and multi-pick pages; the page context; the grid column's `itemDescription` |
| Playwright | add, toggle-remove, chip removal and Backspace, keyboard-only, in LTR and RTL; the field's height with 0, 1 and 30 chips; two-line rows in a field and in a cell |
| axe | chips and an open panel, in both themes and directions |
| Gates | the `./entity-picker` and `./grid` goldens; the ceiling below |

### Part E — Definition of done

- **Rich options:** `itemDescription` on the picker and the grid column, with the fixed two-line
  height (§E.1, §E.4); the new token in the preset; the identity server's token CSS refreshed.
- **Multi-picker:** `tm-entity-multi-picker` on spec 0023's `tm-chip` (§E.2), and its harness.
- **Page context:** §E.3 shipped.
- **Showcase:** the `entity-picker` story gains two-line rows, a multi-picker, and an advanced
  search returning several picks.
- **UI rules:**
  - **Instant feedback** — a pick shows its chip in the same frame (e2e).
  - **Non-blocking** — every keystroke still aborts the previous search (the single picker's suite,
    emit mode included).
  - **Stable layout** — the field's height is identical with 0, 1 and 30 chips (geometry e2e).
  - **RTL** — chip navigation keys mirror (e2e).
  - **WCAG AA** — axe with chips and an open panel in both themes and directions; keyboard-only
    e2e for add, toggle-remove, chip removal and Backspace.
- **Budgets:** `./entity-picker` 12 → **14**; note recorded.

### Part E — Decisions record

1. **Two-line rows share one fixed height, `null` descriptions included** — the panel never
   re-measures between queries (§E.1).
2. **The multi-picker composes the single picker in an internal `'emit'` commit mode** — the search
   state machine is not forked; a second state machine was the alternative (§E.2).
3. **Chips scroll in one fixed-height line, with a count badge** — the field never grows, so
   nothing below it moves; a collapsing "+N" chip set was the alternative (§E.2).
4. **No multi-entity grid editor** — grid cells hold scalar ids (§E.2).
5. **`context` is opaque to the picker** — the binding and its page agree on the shape; a generic
   type parameter on the page form was the alternative (§E.3).

### Part E — Review flags

1. **The multi-picker composes the single picker through `ɵcommitMode`** (§E.2). Alternative: a
   separate state machine. Flips if emit mode destabilises the single picker's suite.
2. **Chips scroll in one line with a count badge** (§E.2). Alternative: wrapping chips that grow
   the field. Flips if users miss chosen items hidden by the scroll.

## Part F — Queryex (spec 0008)

Part F amends spec 0008, the Queryex compiler in `src/core/Tellma.Core.Queryex`. The data access
layer (spec 0012) and the permission evaluator (spec 0014) need seven changes to compose queries
and access filters (§F.1–§F.7). The report engine (spec 0033) needs eight more for pivots, tree
rollups, optional parameters, defaults, drill-through, Hijri grouping and sharing (§F.8–§F.15).
Every item is additive (§F.0).

### §F.0 Shared rules

**Target:** 0008 §14 and Appendix A (codes), §16 (caches), §17 (versions), §18 (testing), §19
(the inspection tool), §20 (the public surface). **Requested by:** every item below. **Why:** each
item then states only its own change.

**Placement and order**

- **The engine only.** Every change lands in `src/core/Tellma.Core.Queryex`, with no new project
  and no new package reference. The public surface of 0008 §20 grows by exactly the members this
  part's sketches add. Nothing is removed or retyped.
- **Tests** grow in the existing projects: `test/core/Tellma.Core.Queryex.Tests`,
  `test/core/Tellma.Core.Queryex.IntegrationTests` and the shared harness
  `test/shared/Tellma.Queryex.Testing`. The inspection tool `eng/queryex-inspection` mirrors the
  new inputs.
- **Order.** §F.1–§F.4, §F.6 and §F.7 ship first: specs 0012 and 0014 cannot ship without them.
  §F.5 needs no code. §F.8–§F.15 ship before spec 0033. Part F waits on no other part.
- **SQL blocks** show the emitted statement with its layout shortened. The emitter writes one
  clause per line, and the golden snapshots (0008 §18.2) fix the exact bytes.
- **The running example** is Acme's `acme.SalesInvoiceLine` (spec 0018 §1.4), with:
  - `Amount`;
  - `Invoice` → `acme.SalesInvoice`, with `PostingDate` and `CustomerId`;
  - `Center` → the tree entity `gl.Center`.

**Vocabulary**

- **Caller error** — an `ArgumentException` that a host bug raises at the API boundary (0008
  §1.4). User text produces diagnostics instead.
- **Current root** — the entity a predicate's paths resolve from: the query root, the target of an
  enclosing `Via`, or the entity of an enclosing `Exists`.
- **Skeleton** — the nodes of a predicate reached from its root through `and`, `or` and `not`
  only, including `FilterTree` connectives and parentheses.
- **Atom** — a skeleton node that is none of those three: a comparison, an `in`, an `is null`, a
  `Bool` call, a `Via` or an `Exists` node.
- **Row grain** — evaluated once per row: `WHERE`, grouping keys, aggregate arguments, ungrouped
  items. Its opposite is once per group: `HAVING`, a grouped item outside an aggregate.
- **Key group** — grouping keys that roll up together.
- **Grouping set** — the key groups that one level of rows groups by.
- **Level indicator** — the appended `GROUPING_ID` column that tells the levels apart.
- **Level navigation** — `Level{j}` on a tree entity: a row's ancestor at depth `j`.
- **Table calendar** — a calendar code whose arithmetic reads a host month table (`'uq'`).

**Compatibility**

- **Additive.** With every new member at its default, the engine emits byte-identical SQL, slots
  and columns, except the paging slot names of §F.4. The existing goldens with paging are rewritten
  once with `TELLMA_UPDATE_GOLDENS=1` (0008 §18.2). Every other golden and corpus entry stands.
- **Version 1.** `QueryexLanguage.Version` and `Minimum` stay 1. Each new form was a diagnostic
  before and binds only under a host opt-in, so the change is additive under 0008 §17:
  - `level()` is a new function;
  - `Level2` resolves only on a schema that declares levels;
  - `'uq'` resolves only under a month map.
- **Records keep their constructors.** New members on `QueryexColumn`, `ParameterUse` and
  `CompiledQuery` are `init` properties, never positional parameters. `NavigationDescriptor
  .ForeignKey` stays non-null.
- **Two new `FilterTree` node types**, `ViaNode` and `ExistsNode`, are sealed nested types like
  the existing four. A host that pattern-matches trees adds two cases.

**Cache keys.** One rule extends 0008 §16: every new input joins the key of each layer whose output
it changes. The input is held as text or structure, never hashed.

- **L2** gains a leaf's own language version, and the omitted names a text refers to.
- **L3** gains the leaf's version too. It also gains restrictions, the presence of paging (never
  its values), `Via` and `Exists` nodes, key groups, grouping sets and the omitted set.
- **A constant statement** keys on its items, types and options.
- **Level navigations and calendar maps** add nothing: they live in the schema, whose identity
  already keys every layer.

**Diagnostic codes.** Appendix A gains three codes:

| Code | Condition | Arguments | Where |
|---|---|---|---|
| QX3104 | A table-calendar operand that is not a row-grain path sits in an aggregation's argument or a grouping key | `function` | §F.13 |
| QX4009 | Paths are not permitted in this position | `name` (the first path) | §F.11 |
| QX5008 | Too many grouping keys for grouping sets | `limit` (32 keys, 31 key groups) | §F.8 |

- **QX2009 widens** from "A predicate is a single expression; list not permitted" to "A single
  expression is required here; list not permitted". It now also covers a constant item (§F.11).
- **New locations.** `…Via[<navigation>]` and `…Exists[<entity>]` inside a filter tree, nesting as
  the tree does: `Filter.Or[2].Exists[core.ReportDefinitionShare].Or[0]`. `Constant[<i>]` names a
  constant item.

**Reference interpreter and corpus**

- **The reference interpreter** (0008 §18.3) implements every item from this part's text,
  independently of the emitter: restrictions, `level`, `Via`, grouping sets with the indicator,
  level navigations, omitted folding, constants, `'uq'` (through .NET `UmAlQuraCalendar`) and
  `Exists`. The differential suite runs every new corpus entry on SQL Server and on the
  interpreter.
- **`LedgerFixture` grows:** `TreeLevels(4)` on its hierarchical entity, with rows at depths 1–4; a
  child table with a parent navigation, for `Exists`; and a `uq` month map seeded from
  `UmAlQuraCalendar` by spec 0013 §8.2's rule.
- **Every new code is reachable** by a corpus entry that asserts code, span, location and
  arguments (0008 §18.1).

### §F.1 Key-set restrictions

**Target:** 0008 §2.1 (`QuerySpec`), §3 (the injection boundary), §13.4. **Requested by:** spec
0012 §2.7, §5.4, §11.3, §11.4; spec 0015 §5.4, §9.3. **Why:** a host restricts a query to keys it
holds in a table, without putting the values in the text.

```csharp
public sealed record KeySetRestriction(string Path, string TableSource); // TableSource: '@' + identifier, a table with an [Id] column

public sealed record QuerySpec
{
    public IReadOnlyList<KeySetRestriction> Restrictions { get; init; } = []; // new; conjoined after Filter
}
```

- **`Path`** is a bare path from the root (`Id`, `ParentId`, `Invoice.Id`), resolved like a select
  path, and ends at an Equatable scalar. Its joins ride the shared trie (0008 §12.2).
- **`TableSource`** is `@` followed by `[A-Za-z_][A-Za-z0-9_]*`, never beginning with `@qx` in any
  case. The host either binds a table-valued parameter of the key's list type, or names a
  `@tb{b}_` table variable. The list types are `IdList`, `BigIdList`, `GuidList`, `StringList` and
  `DateList` (spec 0012 §4.4). It is the third host-authored text to reach SQL, after `Source` and
  `Column` (0008 §3).
- **Emission.** Each restriction is one more top-level conjunct of `WHERE`, after the filter:
  `<column> IN (SELECT [Id] FROM <TableSource>)`. With no filter, the restrictions alone form the
  clause.
- **Nullity.** A top-level conjunct is never negated, so a row whose key is absent never matches.
- **Scope.** The root statement only, never inside `Exists` (§F.14). In an aggregate query it
  filters rows, like `Filter`. A restriction's joins count toward `MaxJoins`.
- **Caller errors:** a path that does not resolve, ends at a navigation or ends at a type that is
  not Equatable; a malformed table source, or one in the `@qx` namespace.

**Example —** the children of captured keys, `Filter = Leaf("IsActive")` and `Restrictions =
[("ParentId", "@tb2_keys")]`:

```sql
WHERE [T].[IsActive] = 1 AND [T].[ParentId] IN (SELECT [Id] FROM @tb2_keys)
```

**Compatibility:** an empty list emits today's SQL. **Tests:** goldens with zero, one and two
restrictions, with and without a filter, and in an aggregate query; the caller-error vectors;
differential (the interpreter filters by an in-memory set).

### §F.2 `level(node)`

**Target:** 0008 §10.1 (the registry), §10.10. **Requested by:** spec 0012 §10.1; spec 0033 §8.6.
**Why:** the depth of a tree node, with no stored `Level` column.

```text
level(node: HierarchyId) -> Numeric      emits {0}.GetLevel(); nullity Propagate(0); store type smallint
```

- **The argument** is the tree-node column (`level(Node)`, `level(Center.Node)`): only a path can
  supply a `HierarchyId`. Anything else is QX3005.
- **No numbering scheme is assumed.** The engine reports `GetLevel()` as is. Under spec 0012
  §10.1's id paths, a top-level row has depth 1.
- **Names stay free.** A property named `level` still resolves: function names are not reserved
  (0008 Appendix C), and the parenthesis decides call versus path.

**Example —** `level(Node) <= 2` → `WHERE [T].[Node].GetLevel() <= @qx0_p0`.

**Compatibility:** a new registry entry; `level(…)` was QX3003. **Tests:** corpus (type, nullity,
QX3005 on a non-node argument); differential (the interpreter counts the node's path segments).

### §F.3 Prologue and body

**Target:** 0008 §2.1 (`CompiledQuery`), §13.4. **Requested by:** spec 0012 §5.2, §5.4; spec 0015
§9.3. **Why:** a host embeds a compiled statement in its own SQL: a capped count, key capture, a
post-check, a delete by query.

```csharp
public sealed record CompiledQuery
{
    public string Prologue { get; init; } = ""; // new; the hoisted DECLAREs, then one empty line; "" when none
    public string Body { get; init; } = "";     // new; SELECT … through the last clause and its line feed; no ';'
}                                               // Sql == Prologue + Body + ";\n", byte-identical to today
```

- **The embedding rule.** `Body` is a valid derived table or `INSERT … SELECT` source when it has
  no `ORDER BY`, or has paging: SQL Server refuses `ORDER BY` in a derived table without `TOP` or
  `OFFSET`. A host that needs a bare count compiles an unordered twin.
- **Prologue first.** The prologue's `@qx{b}_v{n}` variables precede the body in the same batch. A
  host composing several statements writes each prologue before its body.

**Example —** spec 0012 §5.4's key capture writes the keys-only twin's `Prologue`, then `INSERT
INTO @tb2_keys ([Id])` followed by its `Body`.

**Compatibility:** `Sql` is unchanged. **Tests:** for every golden, `Sql == Prologue + Body +
";\n"`; an integration test runs `SELECT COUNT(*) FROM (<Body>) AS q` over a paged and an
unordered query.

### §F.4 Paging slots

**Target:** 0008 §13.1, §13.4, §16. **Requested by:** spec 0012 §11.3. **Why:** a page change
reuses the cached template and the server's plan, so `Skip` and `Take` leave the compilation's
identity.

- **Names.** `@qx{b}_skip` and `@qx{b}_take`, listed last in `Parameters`, skip before take. Only
  the present ones exist; `Take` without `Skip` still writes `OFFSET 0 ROWS`.
- **Slots.** Origin `Literal`, store type `int`, `Value` the spec's value. Hosts bind them from
  `Value` like any literal slot (spec 0012 §11.3).
- **The L3 key** holds whether `Skip` and `Take` are present, not their values. A hit returns the
  cached query with the two slots' `Value` replaced, so every caller sees its own page.

**Example —** `ORDER BY [c1] ASC, [c0] ASC OFFSET @qx0_skip ROWS FETCH NEXT @qx0_take ROWS ONLY`.

**Compatibility:** the one change to existing output: 0008 names paging slots `@qx{b}_p{n}` in
sequence and keys them by value. **Tests:** pages 1 and 2 of one spec compile once (a
`ProbeCacheKeys` hit), with identical `Sql` and different `Value`s; the goldens with paging
rewritten.

### §F.5 Context slots — no engine change

**Target:** 0008 §10.6, §13.1. **Requested by:** spec 0012 §5.1, §11.3. **Why:** reports and
access filters see the tenant's date and zone.

0008 already compiles `today()`, `now()`, `me()` and the implicit zone of `local()` to one slot per
statement, with the origins `Today`, `Now`, `UserId` and `TimeZone`. Binding `Today` to the date in
the tenant's zone, and `TimeZone` to that zone, is the host's rule (spec 0012 §5.1, §11.3). The
engine does not change, and Part F adds no test for it.

### §F.6 Versioned leaves

**Target:** 0008 §2.3, §16, §17. **Requested by:** spec 0014 §5.2; spec 0015 §9.1; spec 0033 §8.3.
**Why:** a stored criterion was validated under the language version stamped beside it, so each
leaf of a composed tree binds under its own version.

```csharp
public abstract class FilterTree
{
    public static FilterTree Leaf(string text, int languageVersion); // new; ArgumentOutOfRangeException unless QueryexLanguage.IsSupported
    public sealed class LeafNode : FilterTree
    {
        public int? LanguageVersion { get; }                          // new; null for the one-argument form
    }
}
```

- **Binding.** A leaf binds under `LanguageVersion ?? QueryCompilationOptions.LanguageVersion`;
  in discovery, `?? DiscoveryOptions.LanguageVersion`.

**Compatibility:** the one-argument `Leaf` is unchanged. With one supported version the bound
trees are identical; the stamp matters from the first version bump. **Tests:** construction
refuses 0 and `Version + 1`; a tree mixing both forms compiles; L3 keys differ when only a leaf's
stamp differs.

### §F.7 `FilterTree.Via`

**Target:** 0008 §2.3, §2.4, §12.2. **Requested by:** spec 0014 §5.5; spec 0017 §5.1; spec 0033
§5.3. **Why:** a child's rows are filtered by a criterion written against its owner. The criterion
is rebased onto a to-one navigation, never rewritten as text.

```csharp
public abstract class FilterTree
{
    public static FilterTree Via(string navigation, FilterTree inner); // new; a dotted to-one path: "Invoice", "Line.Invoice"
    public sealed class ViaNode : FilterTree                            // new
    {
        public string Navigation { get; }
        public FilterTree Inner { get; }
    }
}
```

- **Meaning.** True for a row whose navigation path reaches a row on which `inner` is true. False
  when the path reaches no row.
- **Binding.** Each segment resolves to a navigation of the current root: a foreign-key
  navigation, or a level navigation (§F.9). `inner` binds with the path's target as its current
  root. Context functions and parameters bind as anywhere else.
- **Shared joins.** `inner`'s paths join through the navigation path in the shared trie, so
  `Via("Invoice", …)` and a select of `Invoice.PostingDate` use one join.
- **Emission.** The node emits `inner`'s predicate. When the path's last join is `LEFT`, a
  presence guard comes first: `([Pn].[<Key>] IS NOT NULL AND (<inner>))`. Inside the guard,
  `inner`'s nullity, computed against the target, is sound. The node therefore stays two-valued
  under `not`.
- **Depth.** The node counts one level toward `FilterTree.MaxDepth`; nesting is allowed.
- **Discovery.** `DiscoverQuery` binds `inner` against the target and reports its paths prefixed
  with the navigation's segments, so `PathUse.Segments` stay root-relative.
- **Caller errors:** an empty path; a segment that is not a navigation; a `Via` in a `Having` tree.
  Diagnostics inside `inner` carry `…Via[<navigation>]` locations.

**Example —** from `acme.SalesInvoiceLine`, `Via("Center", Leaf("CenterType = 'Sale'"))`:

```sql
LEFT JOIN [gl].[Centers] AS [P1] ON [P1].[Id] = [T].[CenterId]
WHERE ([P1].[Id] IS NOT NULL AND [P1].[CenterType] = @qx0_p0)
```

**Compatibility:** a tree without a `ViaNode` compiles as today. **Tests:** goldens through an
`INNER` path (no guard) and a `LEFT` path (guard). Differential: `Not(Via(…))` is true exactly
where the positive form is false, over lines with no center. Also a join shared with a select path,
nested `Via`, the caller errors and discovery prefixes.

### §F.8 Grouping sets and the level indicator

**Target:** 0008 §2.1, §8.3, §11.3, §13.4. **Requested by:** spec 0033 §8.5. **Why:** one aggregate
statement returns every leaf cell, every subtotal and the grand totals of a pivot. An appended
indicator tells a subtotal from a "(None)" member, since both carry null keys.

```csharp
public sealed record QuerySpec
{
    public IReadOnlyList<IReadOnlyList<int>>? KeyGroups { get; init; }    // new; a partition of the grouping keys by select ordinal; null: one group per key
    public IReadOnlyList<IReadOnlyList<int>>? GroupingSets { get; init; } // new; each set lists KeyGroups indexes; [] is the grand total; null: a plain GROUP BY
}

public sealed record QueryexColumn
{
    public bool IsGroupingLevel { get; init; }                            // new; true only on the appended indicator column
}
```

- **Emission.** `GROUP BY GROUPING SETS ((…), …, ())`.
  - The sets come in caller order, and each set's keys in select order.
  - Keys are written exactly as in the select list, value bindings included.
  - After the `n` select items, the engine appends `GROUPING_ID(<first key of group 0>, …, <first
    key of group g−1>) AS [c{n}]`. One key per group suffices, because a group rolls up whole.
- **The indicator.** Bit `g − 1 − i` is 1 when group `i` is rolled up in the row. So 0 means every
  group is present, and `2^g − 1` is the grand total. Its column is `Ordinal = n`, `Text = ""`,
  `QxNumeric`, `NotNull`, `Path = null`, `IsGroupingKey = false`, `IsGroupingLevel = true`.
- **Nullity.** A key whose group some set omits is `Nullable`. For 0008 §8.3, the query counts as
  having grouping keys only when no set is empty. So once `()` is present, `sum`, `avg`, `min` and
  `max` are `Nullable`; `count` stays `NotNull`.
- **`Having`** applies only to the rows in which every group is present: `HAVING GROUPING_ID(…) <>
  0 OR (<having>)`. Subtotals and totals include every row the `WHERE` admitted. A visible
  subtotal can therefore exceed the sum of its visible children.
- **Ordering and paging.** QX4005 and QX4006 are unchanged. The paging tiebreakers are the keys in
  select order, then the indicator, so the order is total across sets.
- **QX5008** applies when `GroupingSets` is set and the query passes a limit:
  - more than 32 keys, SQL Server's limit on grouping expressions; or
  - more than 31 key groups, the most an `int` indicator holds.

  It is reported at the span of the first key past the limit, location `Select[i]`. Key groups are
  validated only within these limits.
- **Caller errors:**
  - `GroupingSets` without `Aggregate`, or on a query that derives no key;
  - `KeyGroups` without `GroupingSets`, or not a partition of exactly the derived keys' ordinals;
  - an index out of range, or repeated in one set;
  - two equal sets, since SQL Server does not merge duplicates; a group in no set;
  - more than 4,096 sets (SQL Server's limit);
  - a `Having` when no set holds every group.

**Example —** `Select = "Center.Id, quarter(Invoice.PostingDate), sum(Amount)"`, `GroupingSets =
[[0, 1], [0], []]`, `OrderBy = "Center.Id"`, `Take = 1001`:

```sql
SELECT [P1].[Id] AS [c0], DATEPART(QUARTER, [P2].[PostingDate]) AS [c1], SUM([T].[Amount]) AS [c2],
       GROUPING_ID([P1].[Id], DATEPART(QUARTER, [P2].[PostingDate])) AS [c3]
FROM [acme].[SalesInvoiceLines] AS [T]
LEFT JOIN [gl].[Centers] AS [P1] ON [P1].[Id] = [T].[CenterId]
INNER JOIN [acme].[SalesInvoices] AS [P2] ON [P2].[Id] = [T].[InvoiceId]
GROUP BY GROUPING SETS (([P1].[Id], DATEPART(QUARTER, [P2].[PostingDate])), ([P1].[Id]), ())
ORDER BY [c0] ASC, [c1] ASC, [c3] ASC OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY;
```

`[c3]` is 0 on leaf cells, 1 on a center's subtotal and 3 on the grand total. The subtotal of the
lines with no center reads `(NULL, NULL, …, 1)` and the grand total `(NULL, NULL, …, 3)`: the keys
alike, the indicator not.

**Compatibility:** with `GroupingSets` null, a plain `GROUP BY` as today. **Tests:** goldens for
one key per group, a text term's two keys in one group, and `Having`. Differential over
`LedgerFixture` with null keys in the data: the interpreter runs each set as its own `GROUP BY` and
unions them with the indicator. Also `sum` in `()` over an empty filter; on SQL Server, 31 groups
run while 33 keys or 32 groups are QX5008; every caller error.

### §F.9 Tree level navigations

**Target:** 0008 §3, §6.1, §6.2, §10.10, §12.2. **Requested by:** spec 0012 §11.1 (declares them);
spec 0033 §8.6; spec 0029 §4.3 (the client path model). **Why:** a tree dimension groups by
`Level1.Id`, `Level2.Id`, and so on. A grouping set over those levels then yields every subtree's
total exactly once.

```csharp
public sealed class QueryexSchemaBuilder
{
    public sealed class EntityBuilder
    {
        public EntityBuilder TreeLevels(int maxDepth); // new; 1–32; requires TreeNode
    }
}

public sealed class EntityDescriptor
{
    public int TreeLevels { get; }                     // new; 0 when none are declared
}
```

- **Meaning.** `Level{j}` of a row is its ancestor at depth `j`, where depth is the node's
  `GetLevel()`. A row at depth `j` is its own `Level{j}`; a shallower row has none. Under spec
  0012 §10.1 a top-level row has depth 1, so `Level1` is the root of its tree.
- **Resolution.** A path segment resolves in this order: a property, then a foreign-key
  navigation, then `Level{j}` for `1 ≤ j ≤ TreeLevels`. `Level` matches ignoring case, and `j` is
  decimal without leading zeros.
  - The target is the entity itself.
  - Level navigations are not listed in `Navigations`, since they have no foreign key.
  - `PathUse.Segments` and `QueryexColumn.Path` carry `Level2` like any segment.
- **Join.** Always `LEFT`, so a path through one is always `Nullable`. The condition reads the
  node of the parent join `[Pp]`, which is `[T]` when the tree entity is the root:

  ```sql
  ON [Pn].[<node>] = CASE WHEN [Pp].[<node>].GetLevel() >= j
                     THEN [Pp].[<node>].GetAncestor([Pp].[<node>].GetLevel() - j) END
  ```

  The `CASE` is required because `GetAncestor` raises on a negative argument. The join seeks the
  unique node index, so nothing fans out.
- **`j` in SQL** is an engine-authored integer token, like Bool realisation's 0 and 1 (0008
  §13.1): the validated index of the resolved navigation, never expression text.
- **Usable wherever a navigation is:** every clause, a `Via` path (§F.7), and the key path of
  `descendantOf`, as in `descendantOf(Center.Level1.Id, 10)`. Pruning and `MaxJoins` treat it as
  any join. It cannot correlate an `Exists` (§F.14).
- **Hosts.** An entity query cannot materialize a level path, because no foreign key links the row
  to it. Spec 0012 §11.4 and spec 0015 §5.2 refuse one in `Select`; filters and orderings may use
  it.
- **Caller errors at `Build()`:**
  - `TreeLevels` without a `TreeNode`, or a `TreeNode` property that is not `IsUnique`;
  - `maxDepth` outside 1–32;
  - a property or navigation of the entity named `Level<n>` for `n ≤ maxDepth`.

**Example —** `Select = "Center.Level1.Id, Center.Level2.Id, sum(Amount)"`, `GroupingSets = [[0,
1], [0], []]`:

```sql
SELECT [P2].[Id] AS [c0], [P3].[Id] AS [c1], SUM([T].[Amount]) AS [c2], GROUPING_ID([P2].[Id], [P3].[Id]) AS [c3]
FROM [acme].[SalesInvoiceLines] AS [T]
LEFT JOIN [gl].[Centers] AS [P1] ON [P1].[Id] = [T].[CenterId]
LEFT JOIN [gl].[Centers] AS [P2] ON [P2].[Node] = CASE WHEN [P1].[Node].GetLevel() >= 1
     THEN [P1].[Node].GetAncestor([P1].[Node].GetLevel() - 1) END
LEFT JOIN [gl].[Centers] AS [P3] ON [P3].[Node] = CASE WHEN [P1].[Node].GetLevel() >= 2
     THEN [P1].[Node].GetAncestor([P1].[Node].GetLevel() - 2) END
GROUP BY GROUPING SETS (([P2].[Id], [P3].[Id]), ([P2].[Id]), ());
```

Head office (10) holds 2,000 of its own. Its children Riyadh (11) and Jeddah (12) hold 30,000 and
20,000. The query returns:

| Indicator `[c3]` | Rows, as (`[c0]`, `[c1]`) → `[c2]` |
|---|---|
| 0 | `(10, NULL)` → 2,000; `(10, 11)` → 30,000; `(10, 12)` → 20,000 |
| 1 | `(10, NULL)` → 52,000 |
| 3 | `(NULL, NULL)` → 52,000, the grand total |

Spec 0033 §8.6 decodes these rows into a tree.

**Compatibility:** a schema that declares no levels resolves as today, and `Level2` stays QX3001.
**Tests:** goldens from a child root and from the tree entity as root; differential over the
depth-4 fixture (every `Level{j}` of rows at every depth, absent ones included); the example above
as an integration test; `descendantOf` through a level path; the build errors.

### §F.10 Omitted parameters

**Target:** 0008 §6.3, §8.4, §9.2. **Requested by:** spec 0033 §4.4. **Why:** an optional report
parameter left blank imposes no restriction. Without this, `PostingDate >= @From` is false for
every row when `@From` is absent (0008 §9.2).

```csharp
public sealed record QueryCompilationOptions
{
    public IReadOnlyCollection<string> OmittedParameters { get; init; } = []; // new; declared names, matched ignoring case
}
```

- **Each name** must be declared (a caller error otherwise). It gets no slot and counts as
  referenced for 0008 §6.3.
- **Folding** runs on the syntax tree, before binding. It applies to:
  - the skeleton of every `Filter` leaf and every `Having` leaf;
  - the condition argument of every aggregation, wherever the aggregation sits. That is the second
    argument of `count`, `sum`, `avg`, `min` and `max`.

  It runs in three steps:
  1. In an `in` list, and in the key list of `descendantOf` and `ancestorOf`, the elements that
     are bare omitted parameters are dropped.
  2. An atom that still mentions an omitted parameter, or whose list is now empty, becomes a
     constant. It is `true` under an even number of negations on its skeleton path, and `false`
     under an odd number; `FilterTree.Not` nodes count. A `Via` or `Exists` node is one atom.
  3. Ordinary constant folding follows (0008 §8.4).
- **Elsewhere,** an omitted parameter is the typed absent value, exactly as if `cast(null,
  '<type>')` were written in its place. That covers select items, ordering, `if` conditions and
  other function arguments.
- **Folded atoms are not bound,** so their diagnostics are not reported. Save-time validation
  compiles with nothing omitted (spec 0033 §3.4).

The folding rule's cases, with `@Customer` and `@Other` omitted:

| Clause | Becomes |
|---|---|
| `PostingDate <= @AsOf and CustomerId = @Customer` | `PostingDate <= @AsOf` |
| `not (CustomerId = @Customer)` | `true` |
| `Region = 'East' or CustomerId = @Customer` | `true`: an omitted parameter lifts a disjunction |
| `CustomerId in (@Main, @Other)` | `CustomerId in (@Main)` |
| `sum(Amount, CustomerId = @Customer)` | `sum(Amount, true)`: the rows of `sum(Amount)` |

**Example —** the first row over `acme.SalesInvoice`, with `@AsOf` declared `Date` and nullable:

```sql
WHERE @qx0_p0 IS NOT NULL AND [T].[PostingDate] <= @qx0_p0
```

**Compatibility:** an empty set compiles as today. **Tests:** a corpus entry per table row;
polarity through `FilterTree.Not` and nested `not`; list dropping in `in` and `descendantOf`;
folding inside `Having` and inside a select measure's condition; the typed absent value in a
select item; the caller error. Differential: the interpreter applies this section's rule with its
own code.

### §F.11 Constant statements — `EvaluateConstant`

**Target:** 0008 §1.4. **Requested by:** spec 0033 §4.3, §8.3. **Why:** a host learns the values of
path-free expressions, such as the defaults a report run applies, from one statement. The engine
compiles it and the host executes it, as with `CompileQuery`.

```csharp
public sealed class QueryexEngine
{
    public QueryexResult<CompiledQuery> EvaluateConstant(IReadOnlyList<ConstantItem> items, ConstantCompilationOptions options); // new
}

public sealed record ConstantItem(string Text, QueryexType Type);  // new

public sealed record ConstantCompilationOptions                    // new
{
    public required QueryexSchema Schema { get; init; }            // the calendar codes it accepts; cache identity
    public required int LanguageVersion { get; init; }
    public bool HasUser { get; init; } = true;                     // false: me() is absent
    public int BatchOrdinal { get; init; }                         // the {b} of the @qx{b}_ slot names
    public QueryexLimits Limits { get; init; } = QueryexLimits.Default;
}
```

- **Items.** One to `Limits.MaxListItems` items; any other count is a caller error. Each item binds
  in `Value` mode under `LanguageVersion` and checks against its `Type`. A string literal coerces
  (0008 §7.3); any other mismatch is QX3200.
- **Diagnostics.** A path is QX4009; an aggregation QX4002; a parameter QX3007, since a constant
  statement declares none; a list QX2009. Each carries the location `Constant[<i>]` and a span
  into the item's text. A `'uq'` call emits §F.13's scalar form.
- **Emission.** `SELECT <e0> AS [c0], <e1> AS [c1], …` with no `FROM`: always one row. An operand
  that a guard would repeat uses 0008 §12.3's single-emission form, because there is no row source
  for a value binding. Nothing hoists, so `Prologue` is empty.
- **Columns** carry each item's text, type and nullity, with `Path` null and no keys.
- **Slots** follow 0008 §13.1 under `BatchOrdinal`. The host runs the statement through
  `IDataBatch.EvaluateConstant` (spec 0012 §5.2). That member binds the context slots from the same
  values as the query that uses the result, so both see one `today()` (spec 0033 §4.3).
- **Not a fragment API** (0008 §1.4): the statement is complete and shares nothing with another.

**Example —** items `[("startOfMonth(today())", QxDate), ("me()", QxNumeric)]`, `BatchOrdinal = 1`:

```sql
SELECT DATEADD(MONTH, DATEDIFF(MONTH, CONVERT(date, '00010101'), @qx1_p0), CONVERT(date, '00010101')) AS [c0],
       @qx1_p1 AS [c1];
```

`@qx1_p0` is the `Today` slot and `@qx1_p1` the `UserId` slot.

**Compatibility:** a new entry point. **Tests:** goldens; the interpreter evaluates the same items;
`HasUser = false` makes `me()` absent; a `'uq'` item (`startOfMonth(today(), 'uq')`); each
diagnostic with its location; the coercion of a string literal to `Date`.

### §F.12 Parameter binding — `BindParameters`

**Target:** 0008 §1.4, §2.4, §12.1. **Requested by:** spec 0033 §9.4. **Why:** the drill-through
list runs a plain query whose filter must be self-contained text. The engine prints a text with
its arguments as literals and its omitted atoms folded.

```csharp
public sealed class QueryexEngine
{
    public QueryexResult<string> BindParameters(string text, ParameterBindingOptions options); // new
}

public sealed record ParameterBindingOptions                                       // new
{
    public required QueryexMode Mode { get; init; }                               // where §F.10 folds: the skeleton in predicate modes; aggregation conditions in every mode
    public required int LanguageVersion { get; init; }
    public IReadOnlyList<QueryexParameterDeclaration> Parameters { get; init; } = [];
    public IReadOnlyDictionary<string, object?> Values { get; init; }             // by name, ignoring case; the declared type's CLR value; default empty
    public IReadOnlyCollection<string> Omitted { get; init; } = [];               // folded by §F.10's rule
    public QueryexLimits Limits { get; init; } = QueryexLimits.Default;
}
```

- **The result** is canonical text from 0008 §12.1's printer. Parsing it gives the source's parse
  with two changes: each valued parameter is replaced by a literal, and §F.10's folding is applied
  for the omitted ones. A parameter in neither map stays as written.
- **Syntactic only.** No schema is needed. Lexical and syntax errors return their diagnostics.
- **Literals.** The printer parenthesizes as precedence requires.

  | Type | CLR value | Printed as |
  |---|---|---|
  | `Numeric` | `decimal`, `int`, `long` | invariant decimal text; a negative value with unary minus: `-12.5` |
  | `String` | `string` | quoted, `'` doubled: `'O''Brien'` |
  | `Bool` | `bool` | `true`, `false` |
  | `Date` | `DateOnly` | `cast('2026-01-01', 'date')` |
  | `DateTime` | `DateTime` | `cast('2026-01-01T10:30:00', 'datetime')`: up to seven fractional digits, trailing zeros trimmed |
  | `DateTimeOffset` | `DateTimeOffset` | `cast('2026-01-01T10:30:00+03:00', 'datetimeoffset')` |
  | `Guid` | `Guid` | `cast('…', 'guid')`, lowercase and hyphenated |
  | any | `null`, or an omitted parameter in a value position | `cast(null, '<type>')` |

  A folded atom prints as `true` or `false`.
- **Caller errors:** a valued or omitted name that is not declared; a name both valued and omitted;
  a value whose CLR type does not match the declared type.

**Example —** `Invoice.PostingDate >= @From and Invoice.PostingDate <= @To`, mode `Filter`, `From =
2026-01-01`, `To` omitted → `Invoice.PostingDate >= cast('2026-01-01', 'date') and true`.

**Compatibility:** a new entry point. **Tests:** a vector per literal type and per §F.10 table row;
a property test that binding then compiling equals compiling with the arguments (differential on
the fixture); the printer round trip `parse(print(t)) = t`.

### §F.13 `'uq'` through a host month table

**Target:** 0008 §3, §10.4, §12.3, §13.4. **Requested by:** spec 0033 §8.7; spec 0012 §11.1 (maps
it); spec 0013 §8.2 (owns the table). **Why:** Acme reports by Umm al-Qura month. 0008 §10.4
reserved `'uq'` for a host-provided month table named through the schema.

```csharp
public sealed class QueryexSchemaBuilder
{
    public QueryexSchemaBuilder CalendarMap(string calendar, string source); // new; ("uq", "[core].[CalendarMonths]")
}
```

- **The table.** `source` is validated like an entity `Source` and falls under the same injection
  boundary (0008 §3). Its columns are `[Calendar]` (varchar), `[Year]` and `[Month]` (int),
  `[StartDate]` and `[EndDate]` (date, exclusive).
- **Host obligations.** Like `IsUnique`, the engine trusts them. Per calendar, months are
  contiguous and never overlap. Unique indexes cover (`Calendar`, `StartDate`) and (`Calendar`,
  `Year`, `Month`). Spec 0013 §8.2's `core.CalendarMonths` meets them.
- **Caller errors at `Build()`:** a calendar other than `'uq'`, since `'gc'` and `'et'` are
  algorithmic; a second map for one calendar.
- **Binding.** `'uq'` joins the accepted `cal` values of the eleven calendar functions (0008 §10.4)
  when the schema maps it. Otherwise it stays QX3101.
- **Semantics** follow .NET `UmAlQuraCalendar`:
  - `year`, `month` and `day` of the Hijri date; `quarter` = `(month + 2) / 3`; `week` =
    `(dayOfYear − 1) / 7 + 1`;
  - `startOfYear` and `startOfMonth` give the Gregorian date of the Hijri boundary;
  - `addYears` and `addMonths` keep the time of day, clamp to the target month's last day, and
    truncate `n` as `'gc'` does;
  - `diffYears` and `diffMonths` count completed units by 0008 §10.4's rule;
  - a date outside the map, or a result beyond it, is absent.
- **Nullity.** Every `'uq'` result is `Nullable`, through a registry rule: "`Nullable` when the
  calendar argument selects a table calendar, else the signature's rule".
- **Two emission forms,** chosen by the date operand:
  - **A row-grain operand** reads a path and contains no aggregation. It takes the lateral lookup
    below, in every clause.
  - **Any other operand** is path-free (`startOfMonth(today(), 'uq')`) or aggregated
    (`month(max(PostingDate), 'uq')`). It takes the scalar form below, which SQL Server allows in
    `SELECT`, `WHERE` and `HAVING`.
  - **QX3104.** Inside an aggregation's argument or a grouping key, such an operand is QX3104:
    SQL Server refuses a subquery there (errors 130 and 144).
- **Lateral lookup.** Each distinct row-grain operand gets one lookup per statement, by structural
  equality (0008 §11.3). The operand is made atomic first, so the seek stays sargable: a `Date`
  column as is; anything else through a value binding (0008 §12.3), a `DateTime` as `CAST(<d> AS
  date)`. The lookup is a one-row backward seek on (`Calendar`, `StartDate`):

  ```sql
  OUTER APPLY (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM <source> AS [M]
               WHERE [M].[Calendar] = <slot> AND [M].[StartDate] <= <d> AND <d> < [M].[EndDate]
               ORDER BY [M].[StartDate] DESC) AS [C{n}]
  ```

- **Lookup results** read `[C{n}].[Month]` and the like. A derived-table column is legal in `GROUP
  BY` and `GROUPING_ID`, where a subquery is not (SQL Server error 144).
- **Scalar form.** Any other operand emits the same `SELECT TOP (1) [M].[<column>] … ORDER BY
  [M].[StartDate] DESC` as a parenthesized subquery in place, one per use. The operand enters it
  once (0008 §12.3): a parameter or a column as is, anything else through a value binding inside
  the subquery. A second lookup nests one more subquery.
- **Second lookups.** A function that needs another month's row adds an equality lookup on
  (`Calendar`, `Year`, `Month`):
  - the year's first month, for `startOfYear` and `week`;
  - the target month, for `addYears`, `addMonths` and the completed-unit test of `diffYears` and
    `diffMonths`.
- **Order and scope.** Lateral sources, bindings and lookups alike, emit in creation order, which
  respects their dependencies. The calendar code reaches SQL as a literal slot. An `Exists`
  subquery (§F.14) carries its own lookups.

**Example —** `Select = "month(Invoice.PostingDate, 'uq'), sum(Amount)"`, aggregate:

```sql
SELECT [C1].[Month] AS [c0], SUM([T].[Amount]) AS [c1]
FROM [acme].[SalesInvoiceLines] AS [T]
INNER JOIN [acme].[SalesInvoices] AS [P1] ON [P1].[Id] = [T].[InvoiceId]
OUTER APPLY (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M]
             WHERE [M].[Calendar] = @qx0_p0 AND [M].[StartDate] <= [P1].[PostingDate] AND [P1].[PostingDate] < [M].[EndDate]
             ORDER BY [M].[StartDate] DESC) AS [C1]
GROUP BY [C1].[Month];
```

**Compatibility:** a schema without the map keeps QX3101 for `'uq'`. **Tests:**

- differential for each function over every day from 1900-04-30 to 2077-11-16 and beyond both
  ends, against `UmAlQuraCalendar` in the interpreter;
- lateral goldens for a `Date` operand, a `DateTime` operand and a second lookup, and for a
  grouping key and a `GROUPING_ID` over a `'uq'` key;
- scalar-form goldens for a path-free and an aggregated operand in `SELECT`, `WHERE` and `HAVING`,
  and in a constant statement;
- QX3104 for such an operand inside an aggregation's argument and in a grouping key; QX3101 on a
  schema without the map.

### §F.14 `FilterTree.Exists`

**Target:** 0008 §2.3, §2.4, §3 ("Collections"). **Requested by:** spec 0033 §5.3; spec 0035
§6.3. **Why:** sharing and the dashboard read-through need "some row of a child entity points at
this row and satisfies `inner`".

`Exists` is a semi-join that a host composes into a filter tree. The language gains no collection
syntax.

```csharp
public abstract class FilterTree
{
    public static FilterTree Exists(string entity, string navigation, FilterTree inner); // new; inner And([]) means "any row"
    public sealed class ExistsNode : FilterTree                                         // new
    {
        public string Entity { get; }
        public string Navigation { get; }
        public FilterTree Inner { get; }
    }
}
```

- **Binding.** `entity` names a schema entity. `navigation` is one of its foreign-key navigations
  whose target is the current root. `inner` binds with `entity` as its current root.
- **Emission.**

  ```sql
  EXISTS (SELECT 1 FROM <entity Source> AS [X{n}] <joins>
          WHERE [X{n}].[<FK column>] = <current root alias>.[<Key>] AND (<inner>))
  ```

- **Its own plan.** Each subquery has its own join trie, value bindings and calendar lookups.
  Parameter and context slots are the statement's. Restrictions (§F.1) never apply inside.
- **Aliases** continue the statement's sequences, so an inner alias never shadows an outer one.
  `[X1]`, `[X2]`, … run in pre-order across the statement's subqueries, and a subquery's joins,
  bindings and lookups take the next `[P{n}]`, `[B{n}]` and `[C{n}]`.
- **Nullity.** `NotNull`; `not` gives `NOT EXISTS`, two-valued. Nothing fans out, so aggregation
  stays sound. The subquery's source and joins count toward `MaxJoins`.
- **Nesting.** `Exists` and `Via` nest in either order.
- **Discovery.** `DiscoverQuery` binds `inner` for diagnostics and parameters but reports no
  `PathUse` from it: its paths are not root-relative.
- **Caller errors:** an unknown entity; a navigation that is missing, is a level navigation, or
  targets an entity other than the current root; an `Exists` in a `Having` tree. Diagnostics
  inside carry `…Exists[<entity>]` locations.

**Example —** from `core.ReportDefinition`, `Exists("core.ReportDefinitionShare",
"ReportDefinition", Leaf("UserId = me()"))`:

```sql
WHERE EXISTS (SELECT 1 FROM [core].[ReportDefinitionShares] AS [X1]
              WHERE [X1].[ReportDefinitionId] = [T].[Id] AND ([X1].[UserId] IS NOT NULL AND [X1].[UserId] = @qx0_p0))
```

Spec 0033 §5.3 shows the full sharing criterion, with a `Via` and a nested `Exists` inside.

**Compatibility:** a tree without an `ExistsNode` compiles as today. **Tests:** goldens (plain,
nested, inside `Via`, `Via` inside, under `Or` and `Not`); differential over the fixture's child
table; one SQL text for two users; alias uniqueness across nested subqueries; the caller errors;
`MaxJoins` counting.

### §F.15 `ParameterUse.ComparedWith`

**Target:** 0008 §2.4. **Requested by:** spec 0029 §9; spec 0033 §4.2. **Why:** the report studio
offers an entity picker for `@Customer` when it knows the path the parameter meets.

```csharp
public sealed record ParameterUse
{
    public IReadOnlyList<string>? ComparedWith { get; init; } // new; the segments of the one path the parameter meets; null otherwise
}
```

- **Set by `Discover` and `DiscoverQuery` when a schema is supplied,** from the bound tree, for:
  - a bare parameter compared with a bare path by `=`, `!=`, `<`, `<=`, `>` or `>=`, in either
    operand order;
  - a bare parameter element of an `in` whose tested value is a bare path;
  - a bare parameter key of `descendantOf` or `ancestorOf` (the key path).
- **Null** when the parameter meets no path, or meets paths with different segments (ignoring
  case). Inside `Via` the segments are root-relative; inside `Exists` nothing is reported.
- **Consumers.** `queryex/discover` derives the compared entity from it (spec 0029 §9); the report
  validator stores that entity on the parameter (spec 0033 §4.2).

**Example —** from `acme.SalesInvoiceLine`, `Invoice.CustomerId = @Customer and
Invoice.PostingDate >= @From` gives `Customer` → `["Invoice", "CustomerId"]` and `From` →
`["Invoice", "PostingDate"]`. In `@a = @b`, both are null.

**Compatibility:** null without a schema, like the inferred type. **Tests:** discovery corpus rows
per operator and operand order; conflicting paths; `Via` prefixes; silence inside `Exists`.

### Part F — Testing

Each amendment's **Tests** line lists its cases. All three suites run on every pull request on
Windows and Linux; none is nightly.

- **`Tellma.Core.Queryex.Tests`: corpus, goldens, cache probes (unit).**
  - Every new code and caller error.
  - One golden per new emitted shape: restrictions, `level`, paging names, `Via` guards, grouping
    sets, level joins, folded atoms, constant statements, `'uq'` lateral and scalar lookups,
    `Exists`.
  - L3 hits across pages; L3 splits on leaf stamps, omitted sets and grouping.
- **`Tellma.Core.Queryex.Tests`: property tests (unit).**
  - Determinism and no duplication with every new member; grouping keys repeat only in `GROUP BY`
    and `GROUPING_ID`.
  - `Sql == Prologue + Body + ";\n"` (§F.3); bind-then-compile equals compile-with-arguments
    (§F.12); the printer round trip.
- **`Tellma.Core.Queryex.IntegrationTests`: differential on SQL Server (integration).**
  - Every new corpus entry against the interpreter.
  - Absent rows under `Not(Via)` and `Not(Exists)`; null keys under grouping sets; levels at every
    depth; `'uq'` over its whole range and beyond; the grouping limits.
  - `Body` as a derived table; §F.9's example.

### Part F — Definition of done

- **Engine:** §F.1–§F.4 and §F.6–§F.15 in `src/core/Tellma.Core.Queryex`, with XML docs on every
  new member and the project README's list of amendments updated. Warnings-as-errors green on
  Windows and Linux; no new project or package reference.
- **Surface:** 0008 §20's public surface plus exactly the members this part's sketches add.
- **Behaviour:** pinned by Part F's suites, over the grown `LedgerFixture` and the extended
  interpreter (§F.0).
- **Compatibility:** every pre-existing corpus entry passes unchanged; every pre-existing golden is
  byte-identical except the paging names of §F.4; `QueryexLanguage.Version` is 1.
- **Tool:** `eng/queryex-inspection` accepts restrictions, grouping sets, omitted parameters and
  constant items, and shows the indicator column.
- **Docs:** the architecture document names grouping sets, level navigations, the `'uq'` month
  table and `Exists` where it describes Queryex. Public XML docs and error messages reference no
  `docs/` paths.
- **Not in scope of done:** what hosts do with the amendments: spec 0012 declaring `TreeLevels`
  and `CalendarMap` and binding the paging slots; spec 0013's month table; spec 0014's `Via`;
  spec 0033's compiler and `Exists` criteria.

### Part F — Decisions record

1. **One home for engine amendments: the data access layer's seven items live here, and spec 0012
   §11.2 points here** — a frozen spec changes only through this spec (§F.1–§F.7).
2. **Every item is additive; `QueryexLanguage.Version` stays 1** — no valid text changes meaning,
   and each new form binds only under a host opt-in. A version 2 with dual compilation was the
   alternative (§F.0).
3. **Host-built structure fails as caller errors, user text as diagnostics** — `Via` and `Exists`
   names, restriction paths, key groups and option names are never user input (§F.1, §F.7, §F.8,
   §F.14).
4. **Paging slots are `Literal` slots with fixed names, re-stamped per call** — hosts already bind
   `Literal` slots from `Value`, and a `Declared` slot could collide with a user parameter `@take`
   (§F.4).
5. **Context slots need no engine change** — 0008 already emits them; binding them to the tenant is
   the host's (§F.5).
6. **Tree rollups use level navigations** — to-one joins keep every grouping set exact. A join over
   the node closure counts each fact once per ancestor (102,000 instead of 52,000 in §F.9's
   example), and a query per level re-scans the facts (§F.9).
7. **Level navigations are counted (`TreeLevels`), not listed in `Navigations`** — they have no
   foreign key, and `ForeignKey` stays non-null (§F.9).
8. **Grouping sets over select ordinals, with one `GROUPING_ID` indicator; `KeyGroups` optional**
   — one compact `int` column, and text slots roll up together. `ROLLUP` and `CUBE` cannot express
   a pivot's sets; a `GROUPING()` column per key costs a column per level (§F.8).
9. **`Having` filters the finest set only** — a collapsed member never shows a hole; SQL's
   semantics would drop subtotal rows (§F.8).
10. **Omitted parameters fold syntactically, by polarity, after bare list elements drop** — one
    folder serves §F.10 and §F.12, and `in (@A, @B)` keeps `@A`. Folding the bound tree, and
    whole-atom folding only, were the alternatives (§F.10).
11. **Constant statements are a second entry point emitting one `FROM`-less `SELECT`** — the
    statement is complete, so not a fragment API; `CompileQuery` over a dummy root would return one
    row per root row (§F.11).
12. **`BindParameters` is syntactic** — only the engine has the lexer and the printer; host string
    replacement breaks on `@` inside strings (§F.12).
13. **`'uq'` reads a host month table by one `TOP (1)` seek, lateral for a row-grain operand and
    scalar for any other** — the lateral form keeps `GROUP BY` and `GROUPING_ID` legal and the seek
    sargable. The scalar form runs a default such as `startOfMonth(today(), 'uq')` with no prologue
    variable. QX3104 applies only where SQL Server refuses a subquery (§F.13).
14. **`Exists` is a host-built semi-join; the language gains no collection syntax** — sharing needs
    "some child row" without fan-out, and a host-filled key set cannot sit under `Or` (§F.14).

### Part F — Review flags

1. **`Literal` paging slots** (§F.4). Alternative: `Declared` slots the host binds by name. Flips if
   a host must rebind pages without the engine re-stamping the query.
2. **Caller errors for host-built structure** (§F.1, §F.7, §F.8, §F.14). Alternative: diagnostics.
   Flips if a host ever builds `Via`, `Exists` or restrictions from user text.
3. **Folding before binding, with bare list elements dropped** (§F.10). A drift error inside a
   folded atom goes unreported at run time. Alternatives: bind, then fold; fold whole atoms only.
   Flips if optional atoms break silently in stored definitions, or when list parameters ship.
4. **Two emission forms for `'uq'`** (§F.13): the lateral seek for row-grain path operands, a
   scalar subquery for the rest, refused inside aggregation arguments and grouping keys.
   Alternative: the lateral form only, refusing every other operand. Flips if the scalar form's
   plans measure poorly over large fact tables.
5. **Counted level navigations** (§F.9). Alternative: list them in `Navigations` with a nullable
   `ForeignKey`. Flips if hosts need one uniform navigation list.
6. **`Having` on the finest set as an engine rule** (§F.8). Alternatives: SQL's semantics, or an
   option. Flips when a second consumer wants SQL's semantics, or report users expect totals to
   drop with filtered leaves.
7. **Optional `KeyGroups`; the name `EvaluateConstant`** (§F.8, §F.11). Alternatives: required
   groups; `CompileConstants`, since the engine never executes. Flips on preference.
8. **A month table sought by (`Calendar`, `StartDate`)** (§F.13). Alternative: a per-day table with
   equality seeks. Flips on measured lookup cost over large fact tables.

