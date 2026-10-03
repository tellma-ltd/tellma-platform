# Spec: Identity Server Amendments for Distributions, MCP and Sandboxes

- **Author:** Ahmad Akra
- **Date:** 11 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Spec 0003 shipped the identity server as the platform's shared OpenID Connect authority: a
confidential BFF client per distribution with exact redirect URIs, resource indicators validated
against per-client resource grants, a fixed scope catalog, the bulk invite and delivery-status
APIs, and seeded clients — public for the CLI and native applications, confidential for the control
plane. The distribution host (spec 0011), the web API with its MCP surface (spec 0016) and the user
stack (spec 0018) each depend on a change to that server that no tenant-side spec can own, and each
of those specs excludes the change from its own definition of done.

This spec collects the changes: eight deltas to spec 0003, each stated once with the behaviour it
replaces and, where the delta is code rather than seed configuration, the test that proves it.
Nothing else about the server changes. Every rule of spec 0003 not named here stands: PKCE S256
required, `iss` in authorization responses, refresh-token rotation with reuse detection, exact
redirect-URI matching, no wildcard trust, and no CORS policy on the server's endpoints.

The order below is the implementation order. The `Distribution` seed kind (§2) unblocks the
reference distribution's in-proc identity mode; per-tenant resources (§3) and client ID metadata
documents (§4) unblock the MCP surface, with the interim native clients (§5) bridging until §4
ships; the control-plane grants (§6) unblock the admin surface; the `tellma_kind` claim (§7) is
optional; `existingOnly` (§8) unblocks sandbox invites; the versioned management routes (§9) ship
before spec 0018's identity client, the API's first distribution caller.

## Goals / Non-goals

**Goals**

- Add the `Distribution` seed client kind so a standalone deployment seeds its own BFF and service
  clients from configuration with stable secrets.
- Accept per-tenant MCP resources under a granted distribution origin by a fixed path pattern, and
  copy the accepted resource into `aud`.
- Support OAuth client ID metadata documents for public and `private_key_jwt` clients, and
  advertise `none` among the token-endpoint authentication methods.
- Seed the interim `Native` clients coding agents use until metadata documents ship: three with
  loopback redirects for local agents, two with the vendors' callback URIs for hosted agents.
- Grant the control-plane client each distribution's origin so its tokens carry a distribution
  audience.
- Stamp `tellma_kind` on every access token.
- Add `existingOnly` to the bulk invite API so a sandbox tenant never causes an email.
- Put the management API under a version segment so a breaking change rolls out across
  distributions on different platform releases.

**Non-goals (explicitly out of scope)**

- **Service-account creation from a tenant** — the API exists in spec 0003; spec 0018's
  `issue-credentials` action is the tenant side and needs nothing here.
- **Distribution self-registration** and the operator surface beyond spec 0003 §11.4 — later.
- **The MCP tools, request filters and rate limits** — spec 0016.
- **Metadata documents for clients with a shared secret** — only `none` and `private_key_jwt`
  are accepted; a confidential client with a secret is registered, never discovered.
- **Every deferral of spec 0003 §17** — unchanged.

## 1. Placement

Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are omitted,
so a block is never pasted into code.

| Project | Location | Change |
|---|---|---|
| `Tellma.Identity` (the engine) | `src/apps/Tellma.Identity/` | the seed kind and descriptor (§2), resource evaluation (§3), the metadata-document resolver and discovery changes (§4), the control-plane grants (§6), the claim (§7), the invite flag (§8), the versioned routes (§9) |
| `Tellma.Identity.Migrations` | `src/apps/Tellma.Identity.Migrations/` | none: no table changes; metadata documents are cached in memory |
| `Tellma.Identity.Web` | `src/apps/Tellma.Identity.Web/` | configuration only (§4, §5, §6) |
| Tests | `test/apps/Tellma.Identity.Tests`, `test/apps/Tellma.Identity.IntegrationTests` | §10 |

No new package is referenced. The metadata-document fetch uses the engine's existing
`IHttpClientFactory` registration with a named client `Tellma.Identity.ClientMetadata`.

## 2. The `Distribution` seed client kind

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

A `Distribution` seed entry produces the two applications spec 0003 §5's onboarding automation
registers, and nothing the automation would not:

| Application | Type | Grants | Redirect URIs | Resources | Scopes | Secret |
|---|---|---|---|---|---|---|
| `<slug>` | confidential | `authorization_code`, `refresh_token` | `{Origin}/signin-oidc`, `{Origin}/signout-callback-oidc`; back-channel logout at `BackchannelLogoutUri` | `{Origin}` plus `Resources` | `openid profile email offline_access tellma_api` | `ClientSecret` |
| `<slug>-svc` | confidential | `client_credentials` | — | `{Origin}` plus `Resources` | `tellma_identity`, `tellma_api` | `ServiceClientSecret` |

- **Idempotent.** Seeding on every start converges: an entry whose values are unchanged is a no-op;
  a changed secret, origin or redirect list is applied; the seeder never deletes an application
  and never touches one it did not create (`TellmaClientProperties.FirstParty`).
- **Validated at startup.** `ClientId` lowercase, letter-first, at most fifteen characters;
  `Origin` absolute and `https` (`http` only in Development); `ClientSecret` and
  `ServiceClientSecret` non-empty outside Development. A failure names the entry.
- **Consumers.** Spec 0011 §5.7 writes one entry from `Tellma:Identity:ClientSecret`,
  `ServiceClientSecret` and `PublicOrigin`; a per-boot secret in Development is the host's choice,
  never the seeder's.

## 3. Per-tenant resources under a granted origin

```csharp
// Tellma.Identity.Services
public interface IResourceGrantEvaluator
{
    bool IsGranted(IReadOnlySet<string> grantedResources, string requestedResource);   // exact, or <origin>/{int}/mcp
}
```

A requested `resource` (RFC 8707) is accepted when it equals a resource the client holds, **or**
when it is `<granted>/<id>/mcp` exactly, where `<granted>` is a granted resource that is an origin
(scheme, host, port; no path) and `<id>` is a positive decimal integer without leading zeros. The
accepted value is copied into `aud` verbatim. Nothing else matches: no other path, no trailing
slash, no query, no wildcard, no per-tenant registration — the audience space stays enumerable from
the origin grants alone. The rule applies wherever spec 0003 validates `resource`: the
authorization, token (code exchange, refresh, client credentials, device code) and token-exchange
paths. A refused resource is `invalid_target`, as before.

A refresh's `resource` must be a subset of the resources of the original authorization, with the
pattern above evaluated against those, so a refresh may narrow the audience to one tenant's resource
and never widens it to another tenant or to the origin. A token whose `aud` is `<origin>/17/mcp` is
refused by tenant 18's MCP endpoint by audience alone; membership at the resource server remains the
second wall.

## 4. Client ID metadata documents

Public clients that cannot pre-register — hosted coding agents, IDE agents, one-off MCP clients —
identify themselves by an `https` URL as `client_id`, and the server fetches the client's metadata
from that URL (the OAuth client ID metadata document specification the MCP authorization profile
requires).

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
    public int MaxBytes { get; set; } = 5120;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MinCache { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan MaxCache { get; set; } = TimeSpan.FromHours(24);
    public int FetchesPerHostPerMinute { get; set; } = 10;
}
```

The options are a nested member of the engine's root options beside `Lifetimes`, `Keys` and `Seed`;
an in-proc host sets them in code where spec 0011 §5.7 configures the engine.

- **Recognition.** A `client_id` that parses as an absolute `https` URL with a host and no fragment
  is a document client; anything else is looked up as a registered client, so registered clients
  are unaffected.
- **Fetch guards.** `https` only; the host is not an IP literal and does not resolve to a loopback,
  link-local, private or multicast address (checked after resolution, per address); redirects are
  not followed; `Content-Type` is `application/json`; the body is at most `MaxBytes`; the request
  times out at `Timeout`; fetches are rate-limited per host. A guard failure is `invalid_client`
  and an audit event.
- **Validation.** The document's `client_id` equals the URL exactly; `redirect_uris` is non-empty
  and every entry is absolute; `token_endpoint_auth_method` is `none` or `private_key_jwt`
  (`jwks_uri` on the same origin as the document is required for the latter; inline `jwks` is
  refused); `grant_types`, when present, contain only `authorization_code` and `refresh_token`.
- **Redirect matching.** Exact, as spec 0003 §5, with one relaxation: a documented loopback
  redirect (`http://127.0.0.1` or `http://[::1]`, any path) matches any port, as RFC 8252 §7.3
  allows native clients.
- **Caching.** The document is cached per its HTTP caching headers, clamped to `[MinCache,
  MaxCache]`; a fetch failure serves a cached copy until it expires and refuses afterwards.
- **What a document client gets.** PKCE S256 required; the scopes `openid`, `profile`, `email`,
  `offline_access` and `tellma_api` at most; refresh tokens with rotation, as spec 0003 grants
  public native clients; `resource` evaluated by §3's `IsGranted` against the distribution-origin
  list onboarding maintains beside §5 and §6 — the same `Resources` the interim native and
  control-plane clients hold — so an origin in that list, and §3's per-tenant pattern under it, are
  accepted and nothing else is; membership at the resource server is the isolation, exactly as for
  the seeded native clients of spec 0003 §5. Consent is always explicit and shows the document's
  `client_name` and the `client_id` host.
- **Discovery.** The discovery document advertises `client_id_metadata_document_supported: true`
  and includes `none` in `token_endpoint_auth_methods_supported` — hosted agents select the
  document path only when `none` is advertised.

## 5. Interim native clients

Until §4 has served production traffic for one release, the platform configuration seeds five
`Native` entries (§2's kind; public, PKCE, `tellma_api`, `Resources` holding every distribution
origin, which onboarding maintains): three — `claude-code`, `codex`, `cursor` — with port-less
loopback redirect URIs for local agents, and two public web clients — `claude-web` and `codex-web` —
for hosted Claude and hosted Codex, whose redirect URIs are the vendors' published callback URIs
carried by the entry's `RedirectUris` and whose client ids an organisation enters in the vendor's
connector dialog. After that release the five entries are removed from the platform configuration
and an operator deletes the five applications from the identity store — the seeder never deletes an
application (§2) — after which a client that presents one of the ids receives `invalid_client`.

## 6. Control-plane grants

The control-plane seed client gains `Resources` holding every distribution origin, maintained by
onboarding beside §5's list, so a `client_credentials` token with scope `tellma_control_plane` may
name `resource = {origin}` and carry that origin as `aud`. A distribution's control-plane policy
(spec 0011 §5.5) validates scope and `aud = PublicOrigin`. The fixed platform audience of spec 0003
is the audience of the identity server's own operator API alone; a distribution does not accept
it.

## 7. The `tellma_kind` claim

Every access token carries `tellma_kind`: `individual` when the token descends from an
`authorization_code` or `device_code` grant (refreshes inherit it); `service` for
`client_credentials` and for token exchange of a machine token. The claim is set from the grant
type at issuance and is never client-supplied. Resource servers prefer it to the `auth_time`
inference (spec 0011 §5.5) when present.

## 8. `existingOnly` on the bulk invite

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

With `ExistingOnly = true` the item is a lookup: an existing user who holds a credential answers
`Status = Active` with their `sub`; an existing user without a credential answers the per-user
error `existing_user_has_no_credential`; an unknown email answers `unknown_email`. No user is
created, no link is issued and no email is sent for such an item, whatever the rest of the batch
does. A `disabled` or `purged` user answers the existing refusal. The delivery-status API is
unaffected. Spec 0018's user stack sets the flag on sandbox tenants and maps both errors to one
validation code.

## 9. Management API versions

The management API of spec 0003 §11.4 moves under a version segment, relative to the authority;
the unversioned routes are removed, and their only callers, the server's own suites and engineering
scripts, move to `v1`:

| Routes | API |
|---|---|
| `POST api/identity/v1/invitations` | bulk invite (§8) |
| `POST api/identity/v1/invitations/delivery-status` | bulk delivery status |
| `POST api/identity/v1/service-accounts`; `GET`, `DELETE api/identity/v1/service-accounts/{clientId}` | service accounts |
| `GET api/identity/v1/users/{sub}`; `POST api/identity/v1/users/{sub}/temporary-access-passes` | operator |

The version is the API's own integer, independent of the engine's release. One server answers every
distribution, and each distribution adopts platform releases on its own schedule, so:

- A new endpoint, response member or optional request member stays within a version, as §8's
  `ExistingOnly` does, and a caller ignores response members it does not know.
- A breaking change maps `v{n+1}` beside `v{n}` in one server release, before any platform release
  calls it; `v{n}` is removed once nothing calls it.
- Every call is counted on `tellma.identity.api.calls` (tags `version`, `endpoint`) and logged with
  the calling client, so an operator sees when a version falls silent and who still calls it.

An in-proc engine answers only its own distribution, whose identity client ships in the same
platform release, so the overlap matters for the shared server alone.

## 10. Testing

| Suite | Tier | Pins |
|---|---|---|
| `test/apps/Tellma.Identity.Tests` | unit | The resource evaluator's vectors (`origin` exact; `origin/17/mcp` accepted; `origin/017/mcp`, `origin/17/mcp/`, `origin/17/other`, `origin/17/mcp?x`, `other-origin/17/mcp` and `origin/0/mcp` refused; a granted resource with a path never anchors the pattern; a document client's `resource` evaluated against the distribution-origin list of §4, an origin outside that list refused); document validation vectors (`client_id` mismatch, missing redirects, `client_secret_basic`, inline `jwks`, off-origin `jwks_uri`, loopback port relaxation); fetch guards against a fake handler (private address, redirect, oversize, wrong content type, timeout); cache clamping; the seed descriptor of a `Distribution` entry (both applications, all permissions, idempotence, secret rotation, never deleting); `tellma_kind` by grant type; `existingOnly` outcomes. |
| `test/apps/Tellma.Identity.IntegrationTests` | `Category=Integration` | The full code flow of a document client against a local document server, with consent showing the host; `resource=origin/17/mcp` through code exchange and refresh with `aud` asserted, a refresh to `origin/18/mcp` refused, a refresh to `origin` refused; a control-plane token with a distribution audience; a `Distribution` seed applied twice; the bulk invite with mixed `existingOnly` items asserting no mail queued for them; the discovery document advertising `client_id_metadata_document_supported: true` and `none` among `token_endpoint_auth_methods_supported`; every management route answering under `v1` and `404` without the segment. |

Both suites run on every pull request on Windows and Linux (spec 0011 §10); no `Live=true` suite
exists, so the nightly tier runs nothing for this spec.

## 11. Definition of done

- **Projects**: `src/apps/Tellma.Identity` grown, its README updated, XML docs on every member,
  building and testing on Windows and Linux under warnings-as-errors; no new project.
- **Behavior**: §2, §3, §4, §6, §7, §8 and §9 implemented and pinned by the suites of §10; §5 is
  seed configuration carried by the `Tellma.Identity.Web` row of §1 and is not pinned by a suite;
  the discovery document changes of §4 asserted.
- **Observability**: audit events `client.metadata.fetched`, `client.metadata.rejected` (reason),
  `resource.refused` (client, requested), `seed.distribution.applied`; the counters
  `tellma.identity.client_metadata.fetches` (tag `outcome` ∈ `fetched | rejected | failed`) and
  `tellma.identity.api.calls` (§9) on the `Tellma.Identity` meter. No `client_id` URL is a metric
  tag.
- **CI**: unit and integration suites green on every pull request on both platforms.
- **Docs**: the architecture document's identity section updated for metadata-document support,
  per-tenant resources by path pattern, the `Distribution` seed kind, the control-plane grants and
  the versioned management routes. Public XML docs and error messages reference no `docs/` paths,
  per repo rule.
- **Not in scope of done**: retiring the interim clients of §5 (a later configuration change plus an
  operator deletion); distribution self-registration; the tenant side of every item (specs 0011,
  0016, 0018).

## Decisions record

The load-bearing decisions, where not already evident above:

1. **A fixed path pattern under an origin grant instead of per-tenant resource registration** — a
   tenant is a row in a distribution's catalog, not identity state, and the audience space stays
   enumerable (§3).
2. **Document clients are public or `private_key_jwt` only; secrets are never discovered** — a
   document proves control of a URL, never possession of a secret (§4).
3. **Explicit consent for every document client, showing the host** — the user, not the server,
   is the last check on an unregistered client (§4).
4. **Control-plane isolation by distribution audience, not a fixed platform audience** — a
   control-plane token for one distribution is structurally invalid at another (§6).
5. **`existingOnly` never creates and never mails; a credential-less user is an error, not
   `Reinvited`** — the sandbox must not become a side door to onboarding (§8).
6. **`tellma_kind` from the grant type at issuance** — a resource server should not infer the
   principal kind from `auth_time` (§7).
7. **The `Distribution` seed kind mirrors the onboarding automation exactly** — in-proc and
   standalone deployments register the same two applications (§2).
8. **A URL version segment on the management API, additive changes inside a version** — one
   server answers distributions on different platform releases, so a breaking change ships beside
   the old shape and the old one retires once nothing calls it (§9).

## Review flags

1. **The hard-coded `/{int}/mcp` pattern** (§3) versus a configurable pattern list on the client
   or the server. Flips when a second per-tenant surface needs its own audience.
2. **Cache bounds of one minute to one day for metadata documents** (§4) versus honouring the
   document's headers unbounded. Flips with measured fetch volume from hosted agents.
3. **Refusing inline `jwks` and off-origin `jwks_uri`** (§4) versus accepting them as the draft
   allows. Flips if a mainstream agent ships only an inline key set.
4. **The interim client set** (§5): three native ids and two hosted web clients, retired one
   release after §4. Flips with the agents that actually appear in customer traffic.
5. **Control-plane grants per origin** (§6) versus an agreed fixed audience accepted by every
   distribution. Flips if maintaining the origin list at onboarding proves error-prone before
   self-registration exists.
6. **`existing_user_has_no_credential` as an error** (§8) versus answering `Active` for any
   existing user. Flips if sandbox users commonly exist without a credential.
7. **The claim name `tellma_kind` with values `individual | service`** (§7). Flips only with a
   platform-wide claim-naming review.
