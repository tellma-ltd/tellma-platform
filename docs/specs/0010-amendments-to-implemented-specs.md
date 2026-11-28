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
  settings' zone list (spec 0013), the permission evaluator (spec 0014), the report engine (spec
  0033) and the client's `queryex/discover` (spec 0029 §9).
- **Part G — table types (spec 0001).** It is needed by the catalog's membership directory (spec
  0011 §3.8) and the users stack's invite write-back (spec 0018 §3.1), through spec 0012's
  `[Collation]` annotation.

Specs 0007 and 0009 need no amendment.

Other specs that carry parts of this design:

- spec 0011 — the distribution side of Part A:
  - §1.2 — the Migrator project references `Tellma.Identity.Migrations`, and the Web project
    does not;
  - §5.5 — `/bff/login` forwards `ui_locales`;
  - §5.7 — the in-proc seed entry and its store options;
  - §5.9 — `IdentityInfo.AccountUrl`;
  - §6.1 — `migrate` applies the in-proc store's migrations;
  - §6.4 — `SELECT` on the catalog's `dbo.__EFMigrationsHistory`, which the in-proc store check
    reads;
  - §6.7 — the database collation;
  - §10 — the collation pin.
- spec 0016 §11.9 — the MCP surface's use of §A.2–§A.4 and §A.6.
- spec 0018 §2, §3.3 — the identity client on the `v1` routes; `ExistingOnly` on sandbox tenants.
- spec 0021 §6.1, §6.5, §6.6 — the hub side of §A.12: the hub claims, the closes and the Azure
  SignalR options; spec 0011 §4.3 adds the claims at the hub's front door.
- spec 0023 §3, §4 — `tm-icon`, `kbd` and `tm-chip`:
  - every part's glyphs draw through `tm-icon` (§B.7, §C.13, §D.11, §E.5);
  - the grid matches keys through `kbd` (§C.10, §C.12);
  - §E.2 builds on `tm-chip` and `tm-chip-set`'s `scroll` mode.
- spec 0023 §4.2, spec 0034 §7.7 — the avatar tones, chart series and chart ramp that read
  §B.8's `categorical` and `sequential` groups.
- spec 0024 §7.1, §7.3, §7.6, §10.5 — `@tellma/core` fills the Part B and Part D seams: the
  translations loader, the theme seed, `TM_TIME_ZONE`, `TM_DEVICE_TIME_ZONE` and
  `TM_BLOB_FETCHER`.
- spec 0024 §7.8, spec 0029 §12.1 — which surfaces show §D.4's zone label.
- spec 0024 §7.9 — the rule against bidi control characters that §D.14 and §E.1 apply; it uses
  §D.14's `tmStripBidiControls`.
- spec 0029 §7 — `TmPermissions.disabledReason`, the text §D.12's reason carries.
- spec 0025 §2.6 — the session sweep that calls `clearAll({ keep })` (§D.5).
- spec 0012 §11.1–§11.2, §11.4 — the host of Part F: the schema's `CalendarMap`, the amendments
  the data access layer relies on, and the `[JsonColumn]` columns it passes to §F.16.
- spec 0012 §3.3, §11.3 — `QueryColumn.Precision`, which fills §F.15's `MeasureContext`.
- spec 0013 §8.2 — the `uq` month table that §F.11 reads.
- spec 0032 §4, §5.3 — the role membership window and the permission filter, edited in §C.15's
  popup cells.
- spec 0030 §6.3, §16.3 — the search page's driver target, which forwards §C.2's members, and the
  remembered tree, which binds §C.9's `expansionSource="model"`.
- spec 0029 §5.2, spec 0033 §4.6, spec 0034 §4.1, §7.1 — the ungrouped numbers of §D.15.
- spec 0029 §9, §11.2, spec 0033, spec 0034 §8.4, spec 0035 §6.3 — the consumers of Part F:
  `queryex/discover` with its `measures` clause and conjuncts, the studio's date parts, the report
  engine and its drill conditions, the drill list's chips, and the dashboard read-through.
- spec 0012 §5.2, spec 0014 §3.7, spec 0033 §2.1 — the stored stamps that carry §F.18's
  `QueryexVersion` name.
- spec 0013 §7.7, spec 0024 §7.8 — the `core.TimeZone` value set, which lists §F.22's current zone
  ids and carries its aliases, and the device zone mapped through them.
- spec 0012 §1.3, §2.2, §4.4, spec 0011 §3.8, spec 0018 §3.1 — the users of Part G: the
  `[Collation]` annotation and its convention, and the two `Subject` columns that declare it.

## Goals / Non-goals

**Goals**

- **Part A.** Give the distribution host, the MCP surface, the users stack, the hub and the client
  what they need from the identity server. That is a `Distribution` seed kind, per-tenant MCP
  resources, client ID metadata documents, interim native clients, control-plane audiences, the
  `tellma_kind` claim, `existingOnly` invites, versioned management routes, an account entry URL
  that honours `return_url`, the sign-in language from the authorization request, the identity
  database on the platform collation, migrated outside the web process once deployed, and the hub
  token's claims and closes.
- **Part B.** Breakpoints, the System theme, a translations seam with live direction, a
  closed-list multi-select with option groups, the prefix lint's scope, touch sizes on phones,
  glyphs through `tm-icon`, and categorical and sequential colour groups for avatars and charts.
- **Part C.** The grid features a list page needs, a richer data-driven menu, and glyphs through
  `tm-icon`. The grid features are row activation, the browser's menu on cell links, an external
  active row, sort state, header actions, select-all across pages, row classes, a stable gutter,
  scroll-to-row, remembered tree expansion, a published key map that works on every keyboard
  layout, a sticky row-end column, and popup editors that spill past their cell.
- **Part D.**
  - a public overlay entry point;
  - the drawer, alert-dialog roles and live direction on modals;
  - fixed popover sizes;
  - date-time, time, relative, month, quarter, year and weekday formats, with an offset label when
    the display zone differs from the device's, and month and weekday names;
  - a full client-cache sweep and cancellable blob fetches;
  - image and button support for the new avatar and icon components;
  - link buttons, and buttons disabled with a reason;
  - unselected tabs instantiated on request;
  - formatted text free of bidi control characters;
  - numbers without group separators;
  - glyphs through `tm-icon`.
- **Part E.** Two-line picker options, a multi-entity picker whose advanced search edits the
  whole selection, a page context for picker pages, two-line options in grid cells, and glyphs
  through `tm-icon`.
- **Part F.** The Queryex changes the data access layer, the permission evaluator and the report
  engine need: the seven amendments moved from spec 0012 (§F.1–§F.7), omitted parameters, constant
  statements, parameter binding, `'uq'` through a month table, `Exists`, `ComparedWith`, measure
  partials with their C# evaluation (§F.8–§F.15), undeclared root columns (§F.16),
  `startOfQuarter` (§F.17), the `QueryexVersion` name (§F.18), the `'et'` calendar (§F.19), a
  measure's row condition (§F.20), a filter's top-level conjuncts (§F.21), and current zone ids
  with an alias map (§F.22).
- **Part G.** A collation on a string column of a class-derived standalone table type, so a
  table-valued parameter can meet a binary-collated column (§G.1).

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
  - the Manage pages' content, and every deferral of spec 0003 §17;
  - tools that create and migrate the shared identity database, and on-prem deployment tooling: a
    later release, reusing the migrator's approach (§A.11).
- **Parts B–E:**
  - splitting the grid's readonly and editable code (Part C flag 6);
  - grid footer rows and a row-selectable predicate (Part C decision 9);
  - a second submenu level, and a time-of-day type without an instant;
  - tab prerendering beyond an explicit request (Part D decision 11);
  - matching named keys (arrows, Enter, Escape, Tab) through `kbd`: they are the same on every
    layout (§C.12).
- **Part F:**
  - a language-level list parameter, and `descendantOf` over a table-valued parameter: key-set
    restrictions cover every consumer (spec 0012 §11.2);
  - window functions for running totals (spec 0033 reserves `running`);
  - non-decomposable aggregates (distinct count, median, percentile): Queryex has none, and the
    decomposition refuses any future one until it is designed (§F.14);
  - a new `QueryexLanguage.Version`: no item changes the meaning of stored text (§F.0).

## How to read an amendment

Every amendment has a letter-dotted number: the part's letter, then its place in the part (§A.1 …
§A.12, §B.1 …). Two citation forms appear:

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
- **Closing labels** — **Compatibility**, **Tests** and, in Parts B–E, **Budget**, when the
  amendment has them. An amendment without **Tests** is pinned by its Part's Testing section; one
  without **Compatibility** is additive.

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

Part A amends spec 0003, the platform's shared OpenID Connect authority. Five consumers need
changes to it that no tenant-side spec can own: the distribution host (spec 0011), the web API and
its MCP surface (spec 0016), the users stack (spec 0018), the hub (spec 0021) and the client
(specs 0024, 0027).

Every rule of spec 0003 that Part A does not name stands. That includes required PKCE S256, `iss`
in authorization responses, refresh-token rotation with reuse detection, exact redirect-URI
matching, no wildcard trust, and no CORS policy on the server's endpoints.

The order of the sections is the implementation order:

1. **§A.1** — the `Distribution` seed kind unblocks the reference distribution's in-proc identity
   mode.
2. **§A.2, §A.3** — per-tenant resources and client ID metadata documents unblock the MCP surface.
   The interim native clients of §A.4 bridge the gap until §A.3 ships.
3. **§A.5** — the control-plane grants unblock the admin surface.
4. **§A.6** — the `tellma_kind` claim, which resource servers prefer to the `auth_time` inference.
5. **§A.7** — `existingOnly` unblocks sandbox invites.
6. **§A.8** — the versioned management routes ship before spec 0018's identity client, the API's
   first distribution caller.
7. **§A.9, §A.10** — the account entry URL and the sign-in language ship before the client's
   Account links (spec 0027 §10.1) and sign-in (spec 0024 §8.3).
8. **§A.11** — the identity database's collation and migrations ship before any identity database
   is deployed: a deployed database keeps the collation it was created with.
9. **§A.12** — the hub token's claims and closes ship with spec 0021's hub.

**Terms used in Part A**

- **BFF client** — a distribution's confidential web client, named by its slug (`acme`). BFF
  stands for backend for frontend.
- **Document client** — a client whose `client_id` is the `https` URL of its own metadata
  document. It holds no secret, and the engine stores it as an ordinary application row (§A.3).
- **Distribution-origin list** — the `Resources` of the `tellma_api` scope: onboarding
  (`CreateDistributionAsync`) appends every distribution's origin. In an in-proc engine it is the
  `Distribution` seed entry's `Origin` alone (§A.1). Onboarding also grants each origin to the
  seeded clients that call distribution APIs: the CLI and the interim native clients (§A.4).
  Document clients (§A.3) and the control-plane client (§A.5) are checked against the list at
  request time and store no grant.
- **Store check** — the seeder's first step at every start: it checks the store's collation, then
  its migrations (§A.11).

**Placement**

| Project | Location | Change |
|---|---|---|
| `Tellma.Identity` (the engine) | `src/apps/Tellma.Identity/` | the code of §A.1–§A.3 and §A.5–§A.11, and §A.4's `ThirdParty` seed flag |
| `Tellma.Identity.Migrations` | `src/apps/Tellma.Identity.Migrations/` | §A.11: the migrations regenerated as one `Initial`, scaffolded from the model with the platform collation, and `TellmaIdentityStore`. No table changes: a document client is a row of OpenIddict's applications table (§A.3), and its document is cached in memory |
| `Tellma.Identity.Web` | `src/apps/Tellma.Identity.Web/` | configuration only (§A.3, §A.4, §A.11) |
| `Tellma.Core.Abstractions` | `src/core/Tellma.Core.Abstractions/` | `DatabaseCollations`, as spec 0011 §6.7 declares it (§A.11) |
| Tests | `test/apps/Tellma.Identity.Tests`, `.IntegrationTests`, `.E2E` | Part A — Testing; the unit suite gains a reference to `Tellma.Identity.Migrations` |

No new package is referenced. The metadata-document fetch uses the engine's existing
`IHttpClientFactory` registration, with a named client `Tellma.Identity.ClientMetadata` whose
primary handler is a `SocketsHttpHandler` (§A.3's fetch guards). The key-set fetch of a
`private_key_jwt` document client uses the same named client. §A.12 changes no identity project: it
amends a rule that distribution backends follow, and spec 0021 implements it.

### §A.1 The `Distribution` seed client kind

**Target:** 0003 §5 (seeded clients). **Requested by:** spec 0011 §5.7. **Why:** a standalone
deployment seeds its own BFF and service clients from configuration, with stable secrets.

A new seed kind, `Distribution`, registers a distribution's two clients from one configuration
entry through onboarding's own steps.

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
    public IList<string> Resources { get; }                  // ignored for Distribution: onboarding's descriptors fix the resources
    public bool RequireConsent { get; set; }
    public bool ThirdParty { get; set; }                     // Native only: no FirstParty marker (§A.4)
    public string? Origin { get; set; }                       // Distribution only; absolute, path-less
    public string? BackchannelLogoutUri { get; set; }         // Distribution only; default {Origin}/bff/backchannel-logout
    public string? ServiceClientSecret { get; set; }          // Distribution only; the <slug>-svc secret
}
```

An entry runs the steps of onboarding (`IClientProvisioningService.CreateDistributionAsync`), with
the entry's secrets in place of generated ones:

- **Descriptors.** `ClientDescriptorFactory.Distribution` and
  `ClientDescriptorFactory.DistributionService` build the two applications, with token exchange
  off.
- **Audience.** The origin joins the `tellma_api` scope's resources and is granted to every seeded
  client that calls distribution APIs, as onboarding does.

The two applications:

| Application | Type | Grants | Redirect URIs | Resources | Scopes | Secret |
|---|---|---|---|---|---|---|
| `<slug>` | confidential | `authorization_code`, `refresh_token` | `{Origin}/signin-oidc`, `{Origin}/signout-callback-oidc`; back-channel logout at `BackchannelLogoutUri` | `{Origin}` | `openid profile email offline_access tellma_api` | `ClientSecret` |
| `<slug>-svc` | confidential | `client_credentials`; no token exchange | — | `{Origin}`, the issuer origin | `tellma_identity`, `tellma_api` | `ServiceClientSecret` |

- **Idempotent.** Seeding on every start converges. An entry whose values are unchanged is a
  no-op, and a changed secret, origin or redirect list is applied.
- **Never destructive.** The seeder never deletes an application. It converges the two
  applications the entry names, whoever created them.
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

Such a client becomes a document client: a client whose `client_id` is an `https` URL and that
holds no secret. The server fetches the client's metadata from that URL. This is the OAuth client
ID metadata document specification, which the MCP authorization profile requires.

```csharp
// Tellma.Identity.Services
public interface IClientMetadataDocumentResolver
{
    Task<ClientMetadataDocument?> ResolveAsync(Uri clientId);   // null: not a document client
}

public sealed record ClientMetadataDocument(
    Uri ClientId, string? ClientName, IReadOnlyList<Uri> RedirectUris, string TokenEndpointAuthMethod,
    Uri? JwksUri, JsonWebKeySet? Keys, DateTimeOffset FetchedAt, DateTimeOffset ExpiresAt);   // Keys: fetched from JwksUri; null for none

public sealed class TellmaIdentityClientMetadataOptions      // Tellma.Identity.Options; TellmaIdentityOptions.ClientMetadata, bound from TellmaIdentity:ClientMetadata
{
    public bool Enabled { get; set; } = true;
    public int MaxBytes { get; set; } = 5120;                            // the largest document or key-set body accepted
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);     // per fetch
    public TimeSpan MinCache { get; set; } = TimeSpan.FromMinutes(1);    // MinCache..MaxCache clamps the HTTP cache lifetime
    public TimeSpan MaxCache { get; set; } = TimeSpan.FromHours(24);
    public int FetchesPerHostPerMinute { get; set; } = 10;               // the per-host rate limit
    public int MaxClients { get; set; } = 10000;                         // document-client rows; at the cap a new one is invalid_client
    public TimeSpan UnusedClientLifetime { get; set; } = TimeSpan.FromHours(1);   // a row with no authorization older than this is pruned
}
```

The options are the engine's root options' `ClientMetadata` member
(`TellmaIdentityOptions.ClientMetadata`), beside `Lifetimes`, `Keys` and `Seed`. An in-proc host
sets them in code where spec 0011 §5.7 configures the engine.

- **Recognition.** A `client_id` that parses as an absolute `https` URL with a host and no
  fragment is a document client. It is at most 100 characters, the width of the applications
  table's `ClientId`; a longer one is `invalid_client`. Anything else is looked up as a registered
  client, so registered clients are unaffected.
- **Fetch guards.** A guard failure is `invalid_client` and an audit event.
  - **Address.** `https` only. The host is not an IP literal and does not resolve to a loopback,
    link-local, private or multicast address. The named client's `ConnectCallback` resolves the
    host, checks every address and connects only to a checked one, so a second resolution cannot
    swap the address. The handler uses no proxy and `AllowAutoRedirect = false`.
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
- **The application row.** The engine stores a document client as an ordinary OpenIddict
  application whose `ClientId` is the URL. Every engine path that resolves a client by id, the
  consent record and the token rows then work unchanged.
  - **When.** `DocumentClientHandler`, an OpenIddict server handler, resolves the document before
    the built-in client validation on the authorization, pushed-authorization, token and
    revocation endpoints. A fetch whose document differs from the row upserts the row. An
    unchanged document writes nothing, as the seeder converges (§A.1).
  - **What it holds.** `DisplayName` is `client_name`, else the host. `RedirectUris` come from the
    document. The permissions are the authorization, pushed-authorization, token and revocation
    endpoints, the `authorization_code` and `refresh_token` grants, response type `code`, required
    PKCE, and the scopes under "What a document client gets". `ConsentType` is explicit. The
    property `tellma:document_client` is set; `TellmaClientProperties.FirstParty` never is.
  - **Application type.** Native when any redirect URI is a loopback one, so OpenIddict's RFC 8252
    port relaxation applies as for spec 0003's seeded native clients. Web otherwise.
  - **Client type.** `none` gives a public client. `private_key_jwt` gives a confidential client
    with no secret, whose `JsonWebKeySet` is the key set fetched from `jwks_uri`. That fetch runs
    under the fetch guards and the cache clamp above, and accepts `application/json` or
    `application/jwk-set+json`.
  - **No resource permissions.** The row carries no `rsrc:` permission. Resources are evaluated
    against the distribution-origin list at request time (below).
  - **The document decides.** A request whose document cannot be resolved (nothing cached, and the
    fetch fails) is `invalid_client`, whatever the row holds.
  - **Bounded.** At most `MaxClients` document-client rows exist. At the cap, a new document client
    is `invalid_client` with the audit reason `cap`. An hourly Quartz job beside the engine's
    session prune deletes a document-client row that holds no authorization and was created more
    than `UnusedClientLifetime` ago (`TellmaClientProperties.CreatedUtc`).
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
distribution-origin list as `Resources`. Each sets `RequireConsent` and `ThirdParty`: the agents are
third-party software, so the user consents as for a document client (Decision 3), and no
first-party treatment, such as §A.9's back link, applies.

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

The control-plane client stores no resource grant. A `client_credentials` request with scope
`tellma_control_plane` may name `resource = {origin}` for any origin in the distribution-origin
list, matched exactly through §A.2's `IResourceGrantEvaluator` at request time, and the token
carries that origin as `aud`. The per-tenant pattern of §A.2 does not apply to it.

- **The distribution's check.** Its control-plane policy (spec 0011 §5.5) validates the scope and
  `aud = PublicOrigin`.
- **The fixed platform audience** of spec 0003 is the audience of the identity server's own
  operator API alone; a distribution does not accept it.
- **An explicit resource is the whole audience.** When a `client_credentials` request names
  `resource`, the token's `aud` is exactly the named resources. The platform audiences of the
  requested scopes are not added, as token exchange already behaves. A request without `resource`
  still gets those platform audiences.
- **Two kinds of control-plane token.** The control plane asks for one token per distribution,
  with `resource = {origin}`, and one without `resource` for the identity server's own APIs.
- **The identity server checks its own audiences.** The operator API's policy
  (`ApiPolicies.ControlPlaneScope`) also requires `aud` to contain `urn:tellma:control-plane`. The
  management API's policy (`ApiPolicies.IdentityScope`) also requires `aud` to contain the issuer
  origin. A `HasAudience` helper beside `HasScope` reads the principal's `aud` claims. A token
  minted for a distribution therefore answers `403` at both APIs.
- **Unaffected caller.** Spec 0018 §2.2's identity client asks for `tellma_identity` without
  `resource`, so its token keeps the issuer origin.

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
**Why:** the Account link (My settings, spec 0032 §8.1, and the tenantless Account menu, spec 0027
§6.1) needs one stable URL, and its back link must return the user to the page they left.

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
  then back to the same URL. The challenge forwards `ui_locales` as a top-level query on the
  sign-in URL, as §A.10 does, so the sign-in page renders in that language.
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

**Example —** Farah opens Account from My settings while working in Arabic:

```
GET https://id.tellma.com/account?client_id=acme&section=passkeys&ui_locales=ar
      &return_url=https%3A%2F%2Facme.example.com%2F1%2Fme
302 Location: /Identity/Manage/Passkeys?client_id=acme&ui_locales=ar
      &return_url=https%3A%2F%2Facme.example.com%2F1%2Fme
```

The Passkeys page renders in Arabic, and its back link opens `https://acme.example.com/1/me`. With `return_url=https://evil.example/` the back
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

### §A.11 The identity database's collation and migrations

**Target:** 0003 §2.1 (the `Tellma.Identity.Migrations` row), §2.2 (the Store row), §12, §16.
**Requested by:** spec 0011 §5.7, §6.1, §6.7. **Why:** every platform database shares one
collation, and the server default defines no order on Amharic text, such as users' names in the
shared directory. A deployed web process never migrates.

The identity database uses the platform collation of spec 0011 §6.7. The engine's model carries
it, and no migration ever changes it. The engine checks its store at every start. Outside
development and tests, the migrator or an operator migrates it.

```csharp
// Tellma.Identity.TellmaIdentityConstants — new member
public const string LatestMigration;   // the id of the last migration in Tellma.Identity.Migrations

// Tellma.Identity.Migrations — new
public static class TellmaIdentityStore
{
    public static Task MigrateAsync(string connectionString);   // applies the engine's migrations (spec 0011 §6.1)
}

// Tellma.Identity.Options.TellmaIdentitySeedOptions — new member
public string? MigrateCommand { get; set; }                    // the remedy a failed store check names
```

- **The model.** `TellmaIdentityDbContext` calls
  `modelBuilder.UseCollation(DatabaseCollations.Platform)` (spec 0011 §6.7).
- **The migrations.** `Tellma.Identity.Migrations` is re-baselined: its migrations and snapshot
  are deleted, and one `Initial` migration is scaffolded from that model.
  - An initial migration carries no collation operation, so no identity migration alters the
    database's collation.
  - `Initial` keeps the hand-written `IX_OpenIddictTokens_CreationDate` index.
  - Every later migration follows the N−1 rule of spec 0012 §4.3. It lands before the engine
    deploys, so the previous engine release runs on it during a rollout.
- **The store check.** At every start, before it seeds, the seeder checks its store:
  1. It reads an existing database's collations as spec 0011 §6.7 states. A value other than the
     platform collation fails seeding, naming the database, the value expected and the value
     found.
  2. With `Seed.ApplyMigrations` on, it runs EF's `MigrateAsync`, then step 3's check.
  3. It reads `dbo.__EFMigrationsHistory`. A history without the row
     `TellmaIdentityConstants.LatestMigration` fails seeding, naming the schema, that migration
     and `Seed.MigrateCommand` when set. A missing database or history table fails the same way.
     Later rows do not fail the check, so the previous engine release runs on a newer store (the
     N−1 rule).

  A failed check stops the host, as any failed seeding does. An engine host needs
  `Tellma.Identity.Migrations` only to turn `Seed.ApplyMigrations` on: step 3 reads the constant,
  never the migrations.
- **Development and tests.** Only development and test hosts turn `Seed.ApplyMigrations` on
  (Decision 13). On such a host, EF's `MigrateAsync` creates a missing database: the model's
  collation makes EF write `COLLATE Latin1_General_100_CI_AS`, and EF turns read-committed
  snapshot on.
- **Standalone, deployed.** An operator sets up the shared identity database by hand, once for
  the whole fleet:
  1. Run spec 0011 §6.7's statement in `master`. On Azure SQL Database, use its Azure form, with
     the service tier or pool options the database needs.
  2. On SQL Server, run `ALTER DATABASE … SET READ_COMMITTED_SNAPSHOT ON`, as the migrator does
     in a fresh catalog (spec 0011 §6.7).
  3. Apply the engine's migrations with `dotnet ef database update` against
     `Tellma.Identity.Migrations`, or with the script that `dotnet ef migrations script
     --idempotent` writes.
  4. Create a database user for the engine's principal, with `SELECT`, `INSERT`, `UPDATE` and
     `DELETE` on schema `idsvr` and `SELECT` on `dbo.__EFMigrationsHistory`.

  Each engine release that adds a migration repeats step 3 before the engine deploys. Otherwise
  the store check stops the engine, and its error names step 3's command: `Tellma.Identity.Web`
  sets `Seed.MigrateCommand` to it.
- **In-proc** (`TellmaIdentityOptions.Mode = InProc`, set by spec 0011 §5.7). The store is the
  catalog database. The distribution's migrator creates it and applies the engine's migrations
  through `TellmaIdentityStore.MigrateAsync` (spec 0011 §6.1). The distribution's host runs the
  engine with `Seed.ApplyMigrations` off, so the engine never creates or migrates the catalog.
- **The migrator's entry.** `TellmaIdentityStore.MigrateAsync` builds the context as the
  design-time factory does: `IdentityOptions` from `TellmaIdentityModelDefaults`, and the engine's
  migrations assembly. It then applies the migrations to the database the connection string
  names.

**Example —** the operator's step 3, from a checkout after `dotnet tool restore`:

```
dotnet ef database update --project src/apps/Tellma.Identity.Migrations --startup-project src/apps/Tellma.Identity.Migrations --connection "<connection string>"
```

**Compatibility:** the only identity database that exists is a developer's standalone one. Its
migration history does not match the re-baselined `Initial`, and it uses the server's default
collation, so the store check refuses it. The developer drops it, and the next start with
`Seed.ApplyMigrations` on re-creates and re-seeds it (0003 §10.4).

### §A.12 The hub token's claims and its closes

**Target:** 0003 §7.4, and the 0003 §14 row "Real-time (SignalR) token exfiltration via XSS".
**Requested by:** spec 0021 §6.1, §6.5. **Why:** with Azure SignalR Service the connect never
reaches the distribution, so the token must carry the ids the hub needs; and a tenant-level
revocation must close one tenant's connections only.

Four rules of 0003 §7.4 change. The rest of §7.4 stands: the cookie-authenticated negotiate, the
hub-only `aud`, the query-string deviation, thin events, and a hub that is never a step-up surface.

- **Claims.** The hub-scoped service token carries `sub` and three ids: the tenant id, the user's
  id in that tenant and the BFF session's handle. Spec 0021 §6.1 names them. None is a secret: the
  handle already rides the readable profile cookie (spec 0011 §5.5). `ClaimsProvider` copies
  nothing else from the cookie principal.
- **Lifetime.** The token only opens a connection, and its lifetime is short; spec 0021 §6.6 sets
  it. A later connect negotiates again. Token expiry never closes an established connection.
- **Closes.** Every hosting mode closes a connection by aborting it through its hub context
  (`HubCallerContext.Abort()`), on the instance that hosts it. On Azure SignalR Service the SDK
  then sends the service a close for that client connection. The per-user
  `users/{user}/:closeConnections` call is not used: it would also close the user's connections to
  the distribution's other tenants, which a tenant-level revocation keeps (spec 0021 §6.5).
- **Backstop.** `CloseOnAuthenticationExpiration` is set on the hub's mapping
  (`HttpConnectionDispatcherOptions`) in every mode. Self-hosted, ASP.NET Core closes the
  connection when the session ticket expires. On Azure the SDK embeds the ticket's expiry in the
  service token, and the service closes the connection then.

**The threat row** reads: the hub-scoped service token reaches nothing but the hub (`aud` = the
service endpoint), carries `sub` and three ids, opens connections only within its short lifetime,
and its connections are server-closeable; pushes are thin events, so no data rides the channel.

**Compatibility.** The identity server is unchanged: 0003 §7.4 is a rule for distribution
backends. **Tests.** Spec 0021 §12 pins every rule above.

### Part A — Testing

| Suite | Tier | When it runs |
|---|---|---|
| `test/apps/Tellma.Identity.Tests` | unit | every pull request, Windows and Linux (spec 0011 §10) |
| `test/apps/Tellma.Identity.IntegrationTests` | `Category=Integration` | every pull request, Windows and Linux |
| `test/apps/Tellma.Identity.E2E` | `Category=E2E` | its existing Linux job, every non-draft pull request |

No `Live=true` suite exists, so the nightly tier runs nothing for Part A. §A.12 is pinned by spec
0021 §12, not by Part A's suites. The integration and E2E fixtures let EF create their databases
through the engine's model, as spec 0011 §6.7 allows for test databases.

**The unit suite pins:**

- **The resource evaluator (§A.2).** `origin` exact and `origin/17/mcp` accepted. Refused:
  `origin/017/mcp`, `origin/17/mcp/`, `origin/17/other`, `origin/17/mcp?x`, `other-origin/17/mcp`
  and `origin/0/mcp`. A granted resource with a path never anchors the pattern. A document
  client's and the control plane's `resource` are evaluated against the distribution-origin list,
  and an origin outside it is refused; the control plane gets no per-tenant pattern.
- **Document validation (§A.3).** `client_id` mismatch, missing redirects, `client_secret_basic`,
  inline `jwks`, off-origin `jwks_uri`, loopback port relaxation.
- **Fetch guards (§A.3),** against a fake handler and a fake resolver: private address, a
  rebinding host (public on the first lookup, link-local on the second) never connected to the
  unchecked address, redirect, oversize, wrong content type, timeout. Also cache clamping.
- **Document-client rows (§A.3).** The descriptor built from a document: public for `none`;
  confidential with the fetched key set and no secret for `private_key_jwt`; native application
  type only with a loopback redirect; `tellma:document_client` set, `FirstParty` never set, no
  `rsrc:` permission. A `client_id` URL over 100 characters is refused. The key-set fetch under the
  guards: off-origin, wrong content type, oversize. The cap.
- **Machine audiences (§A.5).** A `client_credentials` request naming `resource` gets exactly that
  `aud`; one without it gets the requested scopes' platform audiences. The operator and management
  policies refuse a principal whose `aud` lacks their audience.
- **The `Distribution` seed descriptor (§A.1).** Both applications equal onboarding's descriptors
  (the issuer origin on `<slug>-svc`, no token exchange), the origin among the `tellma_api` scope's
  resources, idempotence, secret rotation, never deleting. A `Native` entry with `RequireConsent`
  and `ThirdParty` gets explicit consent and no `FirstParty` marker (§A.4).
- **Claims and invites.** `tellma_kind` by grant type (§A.6); `existingOnly` outcomes (§A.7).
- **The account entry (§A.9).** The section mapping, unknown → `profile`. The back-link resolver:
  same origin, another port, userinfo, `javascript:`, a relative URL, an unknown client, a
  third-party client.
- **The store's migrations (§A.11).** Spec 0011 §10's collation pin, run over
  `Tellma.Identity.Migrations`. `TellmaIdentityConstants.LatestMigration` equals the id of the
  last migration in that assembly.

**The integration suite pins:**

- **Document clients.** A full code flow against a local document server, with consent showing
  the host. The application row created on the first fetch, and updated when a re-fetch changes
  the redirect URIs. A request refused with `invalid_client` once the document has expired and its
  fetch fails, although the row exists. A `private_key_jwt` client's code exchange with an
  assertion signed by a key from its `jwks_uri`. The prune deleting a never-consented row and
  keeping a consented one.
- **Per-tenant audiences.** `resource=origin/17/mcp` through code exchange and refresh, with `aud`
  asserted. A refresh to `origin/18/mcp` is refused, and so is a refresh to `origin`.
- **Seeds and grants.** A control-plane token requested with a distribution's `resource` carries
  only that audience, and the operator API and the management API both answer it `403`. One
  naming an origin outside the list is refused with `invalid_target`. A control-plane token
  requested without `resource` is accepted by the operator API. A `Distribution` seed applied
  twice.
- **Invites.** The bulk invite with mixed `existingOnly` items, asserting no mail queued for them.
- **Discovery.** `client_id_metadata_document_supported: true`, and `none` among
  `token_endpoint_auth_methods_supported`.
- **Routes.** Every management route answers under `v1`, and `404` without the segment.
- **Account entry.** `/account` with and without the identity cookie, per section, asserting
  `Location`, the carried query and `no-store`. A Manage page's back link with a same-origin and
  an off-origin `return_url`.
- **Sign-in language.** The sign-in redirect carries `ui_locales` from a query request and from a
  pushed request.
- **The store (§A.11).**
  - A standalone engine with `Seed.ApplyMigrations` on creates a missing database;
    `DATABASEPROPERTYEX(DB_NAME(), 'Collation')` reads `Latin1_General_100_CI_AS`, and
    `is_read_committed_snapshot_on` reads 1.
  - A database created in another collation fails the store check with `Seed.ApplyMigrations` on
    and off, and the error names both collations.
  - With `Seed.ApplyMigrations` off, three stores each fail the store check, create nothing and
    name `Seed.MigrateCommand`: a missing database, an empty one created in the platform
    collation, and one without the `TellmaIdentityConstants.LatestMigration` row. A store with an
    extra row after that one passes.
  - An engine with `Seed.ApplyMigrations` on whose context names no migrations assembly fails the
    store check.
  - `TellmaIdentityStore.MigrateAsync` brings an empty database in the platform collation to the
    engine's last migration, and the store check then passes.

**The E2E suite pins:** `ui_locales=ar` through a pushed authorization request renders the
sign-in page in Arabic with `dir=rtl`, and the email-code step keeps it. The account entry's back
link returns the browser to `return_url`.

### Part A — Definition of done

- **Projects:** `src/apps/Tellma.Identity` grown and `src/apps/Tellma.Identity.Migrations`
  regenerated. Each has its README updated and XML docs on every member, and builds and tests on
  Windows and Linux under warnings-as-errors. `DatabaseCollations` is added to
  `src/core/Tellma.Core.Abstractions`, whose README lists it. No new project.
- **Behaviour:** §A.1–§A.3 and §A.5–§A.11 implemented and pinned by Part A's suites, the discovery
  changes of §A.3 included. §A.4 is seed configuration in `Tellma.Identity.Web` (the placement
  table); the `ThirdParty` seed flag it uses is pinned by the unit suite. §A.12 is spec 0021's to
  implement and pin (spec 0021 §6, §12).
- **Observability:**
  - audit events `ClientMetadataFetched`, `ClientMetadataRejected` (reason; `cap` when §A.3's row
    cap refuses a new client), `ResourceRefused` (client, requested) and `DistributionSeedApplied`;
  - on the `Tellma.Identity` meter, the counters `tellma.identity.client_metadata.fetches` (tag
    `outcome` ∈ `fetched | rejected | failed`) and `tellma.identity.api.calls` (§A.8);
  - no `client_id` URL is a metric tag.
- **CI:** the unit and integration suites green on every pull request on both platforms; the E2E
  suite green in its job.
- **Docs:** the architecture document's identity section updated for metadata-document support
  with document clients stored as application rows, per-tenant resources by path pattern, the
  `Distribution` seed kind, the control-plane grants with an explicit resource as a machine token's
  whole audience, the versioned management routes, the account entry URL, the identity database's
  collation and who creates and migrates it, and the hub token's claims and closes. Public XML
  docs and error messages reference no `docs/` paths.
- **Not in scope of done:** retiring the interim clients of §A.4 (a later configuration change
  plus an operator deletion); distribution self-registration; the tenant side of every item (specs
  0011, 0016, 0018, 0021, 0024, 0027); creating and migrating the shared identity database, an
  operator's step (§A.11).

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
7. **The `Distribution` seed kind runs onboarding's own steps** — in-proc and standalone
   deployments register the same two applications from one descriptor factory, so they cannot
   drift (§A.1).
8. **A URL version segment on the management API, additive changes inside a version** — a breaking
   change ships beside the old shape, and the old one retires once nothing calls it (§A.8).
9. **The account entry URL only redirects** — the distribution publishes one stable URL, and the
   Manage pages keep their own routes and content (§A.9).
10. **`return_url` is a link, honoured only when same-origin with the client's registered origin**
    — the back link can never point anywhere the client is not already trusted to receive users
    (§A.9).
11. **`ui_locales` travels on the sign-in URL** — the existing culture provider applies it; no new
    state or cookie (§A.10).
12. **The engine's model carries the platform collation, and its migrations are re-baselined so
    that none alters a database** — EF then creates a database in the platform collation, and no
    migration attempts the collation change that Azure SQL Database refuses. No identity database
    is deployed, so the migration chain can restart (§A.11).
13. **The engine checks its store at every start and migrates it only in development and tests;
    the migrator or an operator migrates every other store** — the web process holds no DDL
    rights, and a missing or behind store fails with the command that fixes it. The check reads a
    constant naming the last migration, as the catalog's does (spec 0011 §3.4), so a host that
    never migrates carries no migrations. A store in another collation is refused outright: no
    deployed database needs a fallback (§A.11).
14. **An explicit `resource` is a machine token's whole audience, and the identity server's own
    APIs check their audience** — a token minted for a distribution sits in that distribution's
    host and logs, so it must never be a credential at the operator or management API (§A.5).
15. **A document client is an ordinary OpenIddict application row, kept in step with its
    document** — every engine path, the consent record and the token rows resolve a client by id,
    so a row serves them unchanged; the document, not the row, decides validity. A store decorator
    serving transient applications was rejected because authorizations and tokens reference a
    stored row, and OpenIddict's degraded mode is server-wide (§A.3).
16. **The hub token carries the tenant, user and session ids, and every hosting mode closes by
    aborting through the hub context** — on Azure the connect never reaches the instance, so the
    token is the only carrier; the per-user data-plane close would also end the user's other
    tenants' connections (§A.12).

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
9. **The document-client cap and the prune of never-consented rows** (§A.3): `MaxClients` =
   10,000, and a row without an authorization is pruned after `UnusedClientLifetime` (1 h).
   Alternative: a cap per host, or rows created only at consent. Flips if probe traffic fills the
   cap, which `ClientMetadataRejected` with reason `cap` shows.

## Parts B–E: rules shared by the library amendments

Parts B–E change `@tellma/core-ui` (`client/projects/core/tellma-core-ui/`) and its tokens,
testing and Arabic locale packages. File paths in these parts are relative to that folder unless
stated.

- **Order.** Spec 0023 §3.1 and §3.2 (`icon`, `kbd`) come first, then Part B, then Part D.
  - **Why `icon` and `kbd` first.** Every part draws its glyphs through `tm-icon`, and Part C
    matches keys through `kbd`. Neither entry point depends on this spec.
  - **Parts C and E.** Part C needs spec 0023 §3 and §B.3's live `Directionality` (§C.10's submenu
    side, §C.13's live flip), so it follows Part B. Part E also needs spec 0023 §4 (`tm-chip`,
    `tm-chip-set`).
  - **The rest of spec 0023** builds on §B.1's breakpoints and §D.1's overlay entry point.
- **Glyphs draw through `tm-icon`.** Every built-in glyph follows spec 0023 §3.1's rule for the
  library's own glyphs. The rule's two exceptions hold here too, and one of them is the grid's
  masks, which stay (§C.13).
  - **Nothing moves.** Each glyph keeps its box and its stroke. A geometry e2e per part pins them,
    in LTR and RTL.
  - **The lists.** §B.7, §C.13, §D.11 and §E.5 list each entry point's glyphs.
- **Keys through `kbd`.** Library code uses spec 0023 §3.2's helpers:
  - **Shortcuts** on letters, digits and symbols match with `tmKeysMatch`.
  - **Key hints** are written with `tmFormatKeys` or `tmKeysText`.
  - **The platform** comes from `tmIsApplePlatform`.
  - **Named keys** (arrows, Home, End, Enter, Escape, Tab, F-keys) keep direct `event.key` checks:
    they are the same on every layout.
  - **The ESLint rule `tm/no-platform-sniffing`** refuses `navigator.platform` and
    `navigator.userAgentData` outside `kbd/`, in `projects/core/**` and `projects/module/**`.
    `lint:test` holds its vectors. Part C ships it with the grid's migration (§C.12).
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
| Format golden | `l10n/format-golden.json`, which pins the client's formatters only; no C# test reads it (Part D decision 16) |
| `lint:test` | the stylelint and ESLint rule vectors (`client/tools/`) |
| Playwright on the showcase | Chromium in full; Firefox and WebKit on `@cross-engine`; touch; at 390, 768, 992 and 1440 px, in LTR and RTL |
| axe battery | light and dark, LTR and RTL, with popups open |
| Gates | API goldens, `docs:build`, `size:check`, `tokens:check` |

## Part B — Component library foundation (spec 0002)

Part B changes the library's foundation: the primary entry point, the select, the lint rules, the
foundation's glyphs and the token contract. Its consumers are the app foundation (spec 0024: theme,
strings, direction), the shell (spec 0027), the filter controls (spec 0029 §13), the search page
(spec 0030), the avatars and charts (specs 0023, 0034) and every responsive layout, spec 0023's
components included.

### §B.1 Breakpoints

**Target:** 0002 §4 (a new subsection, "Breakpoints") and §1 (lint). **Requested by:** specs 0023
§2, 0027 §7.1, 0028 §6.4, 0030 §3.2 and every page spec. **Why:** one closed set of widths for the
whole platform.

Pages switch structure with `TmBreakpoints`; components adapt with container queries. Both use
the same five thresholds.

```ts
// @tellma/core-ui (primary, folder layout/)
type TmBreakpoint = 'xs' | 'sm' | 'md' | 'lg' | 'xl' | 'xxl';
declare const TM_BREAKPOINTS: { readonly sm: 576; readonly md: 768; readonly lg: 992; readonly xl: 1200; readonly xxl: 1440 }; // xs starts at 0; imported from layout/breakpoints.json

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
  The values live once, in `layout/breakpoints.json`: `TM_BREAKPOINTS` imports it, and the lint
  rule below reads it.

**Example —** the bell picks its surface (spec 0028 §6.4):

```ts
private readonly wide = inject(TmBreakpoints).atLeast('lg');
protected open(): void {
  this.wide() ? this.popover().open(this.bell()) : this.modal.open(TmBellPanel, { placement: 'end', size: 'sm', title });
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
- **One source.** The rule reads `layout/breakpoints.json` through the package export
  `@tellma/core-ui/breakpoints.json`, so distributions get the same set.

**Existing code that changes:** only the modal shell's width query (§D.2). No other width query
exists in the library.

**Compatibility:** additive. **Tests:** unit — `TmBreakpoints` at 390, 576, 767, 768 and 1440 px,
memoized signals, `atLeast('xs')`. `lint:test` — accepted and refused preludes.
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
- **The root load** (`scope: null`) carries the server pack at the root, then `tmUi`, then the `tm`
  scope under `tm`. Spec 0024 §7.3 owns the merge order.
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
  and `aria-multiselectable="true"`. Each row shows a checkbox: the `tm-checkbox` box with its
  `check` glyph (§B.7).
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

### §B.7 Foundation glyphs through `tm-icon`

**Target:** 0002 §3.5 (built-in glyphs as static inline SVGs). **Requested by:** spec 0023 §3.1.
**Why:** one drawing path gives every glyph the same stroke, size token and RTL rule.

The foundation's inline SVGs become `tm-icon` instances, under the shared glyph rule (Parts B–E:
rules shared).

| Entry point | Glyphs |
|---|---|
| `checkbox` | `check`; `minus` for the indeterminate state |
| `select` | the trigger caret `chevron-down`; the option `check` of `tm-select`; the row checkbox's `check` in `tm-multi-select` (§B.4) |
| `form-field` | `circle-alert` beside an error |

- **Sizes stay.** Each host points `--icon-size` at the glyph size token it uses today, so no box
  changes.
- **The checkbox and the grid agree.** The grid's check and dash masks (§C.13) draw the same paths
  at the same stroke, so a checkbox cell looks exactly like a `tm-checkbox`.
- **No RTL change.** None of these glyphs is directional.

**Compatibility:** no public API changes; the three entry points import `@tellma/core-ui/icon`,
which `size:check` keeps outside their self-weight. **Tests:** e2e geometry — each glyph's box and
stroke in LTR and RTL; a `tm-checkbox` and a grid checkbox cell compared pixel for pixel.
**Budget:** re-measured; `./form-field` sits closest to its 5 KB ceiling.

### §B.8 The `categorical` and `sequential` colour groups

**Target:** 0002 §4 (`TmSchemeColors` and the emitter). **Requested by:** spec 0023 §4.2 (avatar
tones), spec 0034 §7.7 (chart series and ramp). **Why:** eight hues that tell things apart and a
seven-step ramp for magnitude, each with its own values in light and dark.

The emitter writes component tokens once, in `:root`. Only `TmSchemeColors` switches under
`[data-theme=dark]`. So two new groups in the scheme shape hold the hues and the ramp:

- **`categorical`.** Each slot has a solid mark and a tinted pair for text.
- **`sequential`.** Each step is one colour, step 1 the faintest.

```ts
// @tellma/core-ui-tokens, contract/tokens.ts
interface TmCategoricalTone {
  readonly mark: TmTokenValue;     // a solid hue for chart marks; never carries text
  readonly tint: TmTokenValue;     // a pale background of the hue
  readonly onTint: TmTokenValue;   // text on tint, at least 4.5:1
}
interface TmSchemeColors {         // existing members unchanged
  readonly categorical: Readonly<Record<1 | 2 | 3 | 4 | 5 | 6 | 7 | 8, TmCategoricalTone>>; // new
  readonly sequential: Readonly<Record<1 | 2 | 3 | 4 | 5 | 6 | 7, TmTokenValue>>; // new; 1 faintest
}
```

- **Emission.** In both scheme blocks, `categorical.3.onTint` emits `--categorical-3-on-tint` and
  `sequential.4` emits `--sequential-4`. `tmRefToVarName` gains both cases.
- **Fixed order.** A slot keeps its place, so a chart series and an avatar keep their colours
  across releases. A distribution re-brands a slot's values in `@layer tm.theme`, never the order.
- **Default `categorical` values:**

  | Slot | Hue | Light `mark` · `tint` · `onTint` | Dark `mark` · `tint` · `onTint` |
  |---|---|---|---|
  | 1 | teal | `#118AA6` · `#D3E9EE` · `#084B5D` | `#2AA5C2` · `#144554` · `#AED6DF` |
  | 2 | orange | `#EB6834` · `#FBE3DA` · `#6A3B2A` | `#D95926` · `#5A3A2F` · `#F0CCBD` |
  | 3 | violet | `#6250D6` · `#E2DFF7` · `#2C3173` | `#9085E9` · `#2E3363` · `#C7C5EE` |
  | 4 | amber | `#C98500` · `#F4E8D0` · `#5A4813` | `#C98500` · `#4F441F` · `#E6D5AE` |
  | 5 | magenta | `#D55181` · `#F7DFE8` · `#60314D` | `#D55181` · `#533348` · `#E9C5D4` |
  | 6 | green | `#008300` · `#D0E8D0` · `#004813` | `#008300` · `#0F431F` · `#A9D4AE` |
  | 7 | blue | `#2A78D6` · `#D8E6F7` · `#134373` | `#3987E5` · `#1C4063` · `#B6D1EE` |
  | 8 | red | `#E34948` · `#F9DDDD` · `#662E33` | `#E66767` · `#583136` · `#EDC3C3` |

- **Default `sequential` values** reference the teal ramp, so a distribution that re-brands teal
  re-brands the steps too. Each scheme runs from near its surface colour to its strongest teal:

  | Step | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
  |---|---|---|---|---|---|---|---|
  | Light | `{teal.100}` | `{teal.200}` | `{teal.300}` | `{teal.400}` | `{teal.600}` | `{teal.700}` | `{teal.800}` |
  | Dark | `{teal.900}` | `{teal.800}` | `{teal.700}` | `{teal.600}` | `{teal.400}` | `{teal.300}` | `{teal.200}` |

- **Who reads them.** A keyed `tm-avatar` paints `tint` with `onTint` initials (spec 0023 §4.2).
  Chart series 1–8 use `mark`, and the chart's `sequential` palette uses the seven steps (spec
  0034 §7.7). Every other surface keeps the brand's roles.
- **Contrast.** A tokens unit test checks `onTint` on `tint` at 4.5:1 or more, in both schemes and
  for any preset. The 9–11 px initials of small avatars need that ratio. Spec 0034 §7.7's chart
  test checks `mark`.

**Compatibility:** additive. Presets extend the default by typed merge (0002 §4), so they inherit
the eight slots and the seven steps. **Tests:** unit — the contrast test; both scheme blocks emit
the 31 names; the schema and missing-ref gates. **Budget:** none in the library's entry points.

### Part B — Testing

| Suite | Pins |
|---|---|
| Unit | `TmBreakpoints`; `TmTheme`; the loader merge and the live `Directionality`; `tm-multi-select` value order, toggle and unmatched values; group navigation; `ɵtmControlSize`; the `categorical` contrast test; the names both colour groups emit |
| `lint:test` | `tm/allowed-breakpoints` vectors; the `tm/prefix-exports` cases |
| Playwright | the theme toggle; the live direction flip with a modal open; both selects with groups; touch geometry at 390 px; §B.7's glyph geometry, the checkbox against a grid cell |
| axe | select groups and multi-select states in both themes and directions |
| Gates | the primary and `./select` goldens; `tokens:check` over the `categorical` and `sequential` groups; the ceilings below |

### Part B — Definition of done

- **Breakpoints:** `TM_BREAKPOINTS`, `TmBreakpoints` and `tm/allowed-breakpoints` shipped over one
  `layout/breakpoints.json` (§B.1); the modal query migrated.
- **Theme:** `TmTheme` shipped (§B.2).
- **Strings and direction:** the loader seam and `ɵTmLiveDirectionality` shipped (§B.3); the
  showcase's overlays no longer depend on its `[dir]` wrapper.
- **Selects:** `tm-multi-select`, `tm-option-group` and their harnesses shipped (§B.4).
- **Lint:** the prefix rule's new scope (§B.5).
- **Touch:** §B.6 shipped.
- **Glyphs:** `checkbox`, `select` and `form-field` draw through `tm-icon` (§B.7).
- **Tokens:** the `categorical` and `sequential` groups in the contract, the emitter and the
  default preset (§B.8); the identity server's token CSS refreshed.
- **UI rules:**
  - **Instant feedback** — `TmTheme.set` writes in the click's task (unit).
  - **Non-blocking** — nothing in Part B waits on the network.
  - **Stable layout** — the multi-select trigger keeps one height (e2e geometry); sizes change only
    across 576 px (§B.6 unit); glyph boxes hold (§B.7 e2e).
  - **RTL** — the live switch (§B.3 e2e).
  - **WCAG AA** — axe over the new select states in both themes and directions; 46 px touch fields
    (§B.6 e2e); `onTint` on `tint` at 4.5:1 or more (§B.8 unit).
- **Budgets:** primary `.` 5 → **7** (Part D's primary additions included); `./select` 8 → **9**;
  `./checkbox` and `./form-field` re-measured after §B.7; notes recorded.

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
10. **Every built-in glyph draws through `tm-icon` with `lucide` data, apart from `tm-spinner` and
    the grid's masks (Part C decision 17)** — one stroke, one size token and one live RTL rule for
    every glyph; static inline SVGs per component were the alternative (§B.7, §C.13, §D.11,
    §E.5).
11. **Categorical colours are a semantic scheme group, read by avatars and charts** — component
    tokens emit once, so per-scheme hues need the scheme shape; separate `avatar` and `chart`
    groups would hold the same hues twice (§B.8).
12. **The chart's magnitude ramp is a `sequential` scheme group** — the light ramp runs pale to
    dark and the dark ramp the reverse, and a component token is emitted once, so it cannot hold
    both; one ramp for both schemes was the alternative (§B.8).

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
7. **Eight categorical slots** (§B.8). Alternative: more slots, like Fluent UI's thirty avatar
   colours. Flips if member lists show same-coloured neighbours too often.

## Part C — Data grid and menu (spec 0004)

Part C gives the grid what list and line-entry pages need, and gives the menu the entry kinds the
shell and page toolbars need. It also moves the grid's keys onto `kbd` and its glyphs onto
`tm-icon`. Its consumers are the search page (spec 0030), the details page (spec 0031), the shell
(spec 0027), the report renderers (spec 0034), the stock pages' editable grids (spec 0032) and
spec 0023's tiles, which reuse the activation and driver types.

- **Where grid members land.** Every grid input below is on `ɵTmGridBase`
  (`grid/internal/grid-base.ts`), so `tm-grid` and `tm-tree-grid` share it. Tree-only members are
  on `TmTreeGrid`.
- **The headless `grid-engine` does not change.** The work is in `ɵTmGridCore`
  (`grid/internal/grid-core.ts`) and the view.

### §C.1 Row activation

**Target:** 0004 §8.2, §8.3, §8.6, §8.8 and Decision 18. **Requested by:** spec 0030 §6.4,
§7.2; spec 0023 §8 (tiles). **Why:** a readonly list opens a row with a click anywhere or Enter,
while ranges, copy and the browser's link menu keep working.

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
| F2 on the active cell | on a popup column's cell, opens the read-only pane (§C.15); elsewhere, actuates the cell's first interactive child (0004 §8.2's readonly Enter) |
| A second activation of the same row within 400 ms | ignored, so a double-click opens once |

- **`'none'`** (the default) keeps every behaviour of today, Enter included.
- **Printable keys pass through.** A readonly grid, in either mode, leaves unmodified printable
  characters other than Space unhandled and never calls `preventDefault` on them, so a page's
  type-to-search receives them (spec 0030 §6.2). Space keeps its selection and sort meanings (0004
  §8.8, §C.4).
- **Visual.** The active row carries `tm-grid__row--active`. It paints a row tint
  (`--grid-active-row-bg`, a new token on `surface.selected`) and a 2 px inline-start accent bar on
  the first painted cell. The focus ring stays on the active cell. Rows show `cursor: pointer`.
- **Accessibility.** The scroller gains `aria-describedby` pointing at a visually hidden hint,
  `tmUi.grid.rowActivationHint`: "Press Enter to open a row. Press F2 to reach a link in a cell."
  Roles are unchanged (`grid`, `row`, `gridcell`).
- **Links stay links.** The search page renders display-name and navigation cells as real
  `<a [routerLink]>` links, so opening in a new tab and copying a link stay native.
- **A link keeps the browser's menu,** in every grid and either mode (amending 0004 §8.3 and
  §8.6). A right-click or a long-press whose target is inside an `a[href]` in a cell moves the
  active cell there, then leaves the event alone: no `preventDefault`, no grid menu, no long-press
  timer. Elsewhere in the cell, and from the keyboard (the Menu key, Shift+F10), the grid's menu
  opens as before. Spec 0030 §6.4's "Open in new tab" and "Copy link" items serve those paths.
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
readonly controlsId: Signal<string>;                 // the scroller's id
readonly activeDescendantId: Signal<string | null>;  // the active row's element id; null with no active row
readonly pageStep: Signal<number>;                   // the visible row count

// @tellma/core-ui/grid — the face the driver uses; both grids implement it
interface TmGridDriverTarget {
  readonly activeRowId: Signal<TmRowId | null>;
  readonly controlsId: Signal<string>;                // the id aria-controls names
  readonly activeDescendantId: Signal<string | null>; // the id aria-activedescendant names
  readonly pageStep: Signal<number>;                  // rows (tiles: lattice lines) a PageDown moves
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
  | PageDown / PageUp | `moveActiveRow(±pageStep())` |
  | Enter | `activateRow()` when a row is active (Mod → `newTab`); otherwise the input's own Enter |

  Every other key (Home, End, Left, Right, Space, characters) stays with the input.
- **ARIA.** The driver sets `aria-controls` to the target's `controlsId()` and
  `aria-activedescendant` to its `activeDescendantId()`, and reads nothing from the target beyond
  its members. ARIA 1.2 lets a textbox or searchbox reference an element in the
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
  `arrow-down` (a decorative `tm-icon`, §C.13). The slot is always present, so sorting never shifts
  the label. Hover previews the next direction faintly.
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
- **Header navigation turns on when any column is `sortable` or declares `tmGridHeaderEnd`, or
  the grid declares a row-end header (§C.14):**

  | Key (focus on a header cell) | Effect |
  |---|---|
  | ArrowUp on the first data row | focus moves to the active column's header cell |
  | ArrowDown | back to the first data row, same column |
  | ArrowLeft / ArrowRight, Home / End | between header cells (direction-mapped) |
  | Enter or Space | cycles the sort on a sortable column; toggles select-all on its header; on any other header, clicks its first `[tmGridHeaderAction]` |
  | Alt+ArrowDown | clicks the header's first `[tmGridHeaderAction]` (opens the filter) |
  | Tab | leaves the grid; the single tab stop stands |

- **Focus mechanics.** Header cells carry a roving `tabindex` and `aria-describedby` to a hidden
  hint: `tmUi.grid.headerNavHint`, "{filter, select, true {Enter to sort. {filterKeys} to filter.}
  other {Enter to sort.}}", on a sortable header, where `filter` is whether the header has an action
  and `filterKeys` is `tmKeysText('Alt+ArrowDown')`, and `tmUi.grid.headerActionHint`, "Press
  Enter to open.", on a header whose only interaction is its action. Header actions get
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
  - `tm-grid__row--muted` — secondary ink, for a tree ancestor shown only because a descendant
    matched;
  - `tm-grid__row--emphasis` — weight 600 on `surface.subtle`, for a totals row, which is the last
    data row.
- **`rowHeaders: false`** drops the row-number column and the corner. Mod+A and Shift+Space keep
  the selections those headers offered by pointer.

**Example —** a tree search mutes ancestors: `[rowClass]="r => matched.has(r.id) ? null :
'tm-grid__row--muted'"`.

### §C.7 `scrollbar-gutter`

**Target:** 0004 §4. **Requested by:** spec 0030 §3.3. **Why:** arriving rows never re-flow the
columns.

- **`.tm-grid__scroller { scrollbar-gutter: stable; }`.** Rows arriving never re-flow `flex`
  columns on classic-scrollbar platforms (Windows, Linux). Overlay scrollbars (the macOS default,
  mobile) reserve nothing and need nothing. The scrollbars keep the platform's width.
- **The gutter's bands** come from a two-stop background on the scroller: the header band paints
  the header colour, so the header row reads as continuous, and the body band paints the grid's
  body background, so the strip a short result leaves reads as margin, not as a track.
- **Compatibility.** On classic-scrollbar platforms, proportional columns lose one scrollbar width
  even without overflow; fixed-width columns are untouched.
- **Test.** Headless Chromium runs without scrollbars, so the pinning e2e launches with
  `ignoreDefaultArgs: ['--hide-scrollbars']` and asserts that a column's width is equal before and
  after rows load, and that the gutter's body band matches the body background.

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

- **Trees** expand the row's ancestors first, as find does; with `expansionSource` `'model'` the
  ancestors join the bound set and `expandedIdsChange` emits it (§C.9).
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
readonly expansionSource = input<'store' | 'model'>('store');   // new: who keeps the expansion
readonly expandedIds = model<ReadonlySet<TmRowId>>(new Set());  // new; read and written only when expansionSource is 'model'
readonly loadChildren = input<((rows: readonly T[]) => Promise<void>) | undefined>(undefined);
// old: loadChildren?: (row: T) => Promise<void>
```

- **Who keeps the expansion.** `expansionSource`, read at content bind, decides:
  - `'store'` (the default): the state store's expansion slice, then `defaultExpandedDepth`, as
    today. The grid never reads or writes `expandedIds`.
  - `'model'`: `expandedIds` alone. The store's expansion slice is neither read nor written, and
    `defaultExpandedDepth` does not apply.
- **The bound set is the source of truth.** In `'model'` mode the grid keeps the set beside the
  engine, unpruned. It seeds the engine with `TmGridEngine.restoreExpansion(set)`, which keeps only
  the ids present and expandable, and calls it again after each batch resolves.
  `expandedIdsChange` always emits a copy of the bound set, never the engine's pruned one.
- **Two-way.** A user expand adds the id at request time (the spinner shows); a collapse removes it.
  Collapsing an ancestor of the active cell removes that ancestor only. A failed load removes its
  batch's ids and announces once.
- **A consumer write after bind** replaces the bound set and applies like a restore:
  `restoreExpansion` runs, rows it leaves out collapse, and newly expanded rows load as at moment 1
  below. The grid does not echo the write through `expandedIdsChange`.
- **Unknown ids are kept, never pruned,** in the bound set. A remembered grandchild arrives with a
  later batch, and a search may hide rows that the next query brings back.
- **Batching.** The grid collects every expanded row that is marked `hasChildren` and has no loaded
  children. It calls `loadChildren` once with them, in model order, at three moments:
  1. at content bind, at restore and on a consumer write (this replaces today's collapse of
     unloaded lazy rows);
  2. after a batch resolves, for the rows it brought in that the expansion holds (the bound set, or
     in `'store'` mode the store's slice), level by level;
  3. for user expands landing in the same task, coalesced in a microtask.
- **Engine: no change.** The bulk `TmGridEngine.restoreExpansion(ids)` (one flatten for a 500-id
  restore) seeds the engine; user toggles keep using `setExpanded`.
- **Lifecycle.** A content switch or destroy discards in-flight completions (the existing
  load-generation guard). The consumer appends rows to its own array, as today.
- **Sibling order.** The flattening keeps the data array's order among siblings, so a server
  `OrderBy` sorts siblings.

**Example —** the remembered Centers tree (spec 0030 with spec 0025's `TmTreeExpansion`):

```html
<tm-tree-grid gridId="search.gl.Center" [data]="rows()" [rowId]="byId" [parentId]="parentOf"
              [hasChildren]="hasKids" [loadChildren]="loadMany"
              expansionSource="model" [expandedIds]="expansion()" (expandedIdsChange)="remember($event)">
```

**Compatibility:** `expansionSource` defaults to `'store'`, so a tree that binds neither new input
behaves as today. `loadChildren` changes signature. The showcase tree story and the tree-grid
specs migrate to `rows => …`, and the API golden records it; no other consumer exists.

### §C.10 Menu

**Target:** 0004 §8.5 (flat items only; submenus deferred; "the icon registry remains a future
concern"). **Requested by:** spec 0026 §9.4; spec 0027 §8.8, §10; spec 0030 §12.2, §13.2; spec
0031 §10.1. **Why:** the session, Export and Actions menus and the pin folders need submenus,
groups, checkable and link items, items disabled with a reason, registry icons and platform key
labels.

The menu stays data-driven; new entry kinds join the `TmMenuEntry` union.

```ts
// @tellma/core-ui/menu
type TmMenuIcon = TemplateRef<void> | string | TmIconSource; // string: a TmIconRegistry name; TmIconSource: direct data (spec 0023 §3.1)

interface TmMenuItem {                  // id, label, labelKey, disabled and action() unchanged
  readonly icon?: TmMenuIcon;           // old: TemplateRef<void>
  readonly shortcut?: TmKeys;           // old: a pre-formatted string; TmKeys is spec 0023 §3's
  readonly description?: string;        // new: a muted second line
  readonly detail?: string;             // new: a trailing value ('English', 'v2.14.0'); joins the accessible name
  readonly role?: 'checkbox' | 'radio'; // new: menuitemcheckbox / menuitemradio
  readonly checked?: boolean;           // new: with role
  readonly keepOpen?: boolean;          // new: activation leaves the menu open (checkbox toggles)
  readonly lang?: string;               // new: the label's BCP 47 language, set as lang on the row (native language names)
  readonly disabledReason?: string;     // new: disabled, still focusable; the reason is its second line and description
}
interface TmMenuLink {                  // new
  readonly id: string; readonly label?: string; readonly labelKey?: string;
  readonly icon?: TmMenuIcon; readonly shortcut?: TmKeys; readonly detail?: string; readonly disabled?: boolean;
  readonly href: string;
  readonly target?: '_blank';           // an external glyph and a hidden "(opens in a new tab)"
  action?(): void;                      // when set, a plain activation runs it (SPA routing) instead of navigating
  readonly disabledReason?: string;     // new: as on TmMenuItem
}
interface TmMenuSubmenu {               // new; one level only
  readonly id: string; readonly label?: string; readonly labelKey?: string;
  readonly icon?: TmMenuIcon; readonly shortcut?: TmKeys; readonly detail?: string; readonly disabled?: boolean;
  readonly items: readonly (TmMenuItem | TmMenuLink | TmMenuGroup | TmMenuSeparator)[];
  readonly disabledReason?: string;     // new: as on TmMenuItem; the submenu does not open
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
  { id: 'me', labelKey: 'tm.session.mySettings', icon: 'user-cog', href: this.mySettingsUrl() },
  { separator: true },
  { id: 'sign-out', labelKey: 'tm.session.signOut', icon: 'log-out', action: () => this.session.signOut() },
]);
```

- **Trigger (the APG menu button).** Sets `aria-haspopup="menu"`, `aria-expanded` and
  `aria-controls`. A click toggles; Enter, Space and ArrowDown open with focus on the first item;
  ArrowUp opens on the last. Focus returns to the trigger on Escape and after an activation.
- **A reason.** An entry with `disabledReason` renders `aria-disabled="true"` with the disabled
  styling and stays in the roving focus order and typeahead. Activation does nothing: a click,
  Enter or Space neither runs `action()`, follows the link nor opens the submenu, and the menu stays
  open. The reason shows as the muted second line, in place of `description`, and is the row's
  accessible description, so pointer, touch, keyboard and screen-reader users all read it.
  `disabled` stays for transient states, as on buttons (§D.12); setting both is a dev warning.
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
- **Icons.** The icon slot sets `--icon-size: var(--menu-icon-size)`. Each `TmMenuIcon` form
  renders its own way:
  - a string renders `tm-icon` from the registry;
  - a `TmIconSource` renders `tm-icon` with `[svg]`, as library code does (the grid's context
    menu, §C.13);
  - a template renders as today.
- **Shortcuts** are display only, `aria-hidden`, rendered through `tmFormatKeys` for the platform:
  `Mod+C` shows Ctrl and C on Windows and Linux, ⌘ and C on Apple. The grid's own context menu
  passes `Mod+X`-style keys instead of its pre-formatted strings. Its paste hint, "Press
  {shortcut} to paste", fills `{shortcut}` with `tmKeysText('Mod+V')`: "Ctrl+V" or "⌘V".
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
| `./grid` | 38 → **43** (42 for §C.1–§C.14; §C.15's popup editors add 1) |
| `./grid-engine` | 15, unchanged (§C.9 reuses `restoreExpansion`) |
| `./tree-grid` | 8, unchanged |
| `./menu` | 6 → **7** |

The search route's budget (spec 0024 §12) carries the grid and its engine at about 57 KB. Loading
the editable-only machinery on demand would cut that; it is not part of this amendment (Part C
flag 6).

### §C.12 The key map, published and matched through `kbd`

**Target:** 0004 §8.2 (the keyboard reference), §8.3, §8.7, §13.2 (the tree grid's keys).
**Requested by:** spec 0027 §4.3, §11.2; spec 0023 §3.2. **Why:** the shortcut registry refuses
grid keys and the Shortcuts dialog lists them, without restating 0004. Mod+Z, Mod+D, Mod+A and
Mod+F must also work on an Arabic layout, where `event.key` is an Arabic letter.

```ts
// @tellma/core-ui/grid
declare const TM_GRID_KEYS: readonly { readonly keys: TmKeys; readonly labelKey: string }[]; // kbd imported at runtime
```

- **Contents.** One entry per key combination of the grid's navigation state, the tree additions
  and Part C's own keys (F2 in §C.1, Alt+ArrowDown on a header in §C.4), with label keys under
  `tmUi.grid.keys.*`. Entries that share a `labelKey` render as one help row.
- **One table.** The keymap's binding table lists each combination with its intent and label key.
  `TM_GRID_KEYS` is derived from that table by dropping the intents, so it cannot miss a binding.
- **Matched through `kbd`.** Bindings on a letter, digit or symbol match with `tmKeysMatch`, so
  they work on every layout. Named keys keep the keymap's direct checks (Parts B–E: rules shared).
  - **Letters:** `Mod+A`, `Mod+Z`, `Mod+Y`, `Mod+Shift+Z` (a second redo binding, with `Mod+Y`'s
    intent and label), `Mod+D` and `Mod+F`.
  - **Rows:** `Mod+Alt+Plus` and `Mod+Alt+Minus`. `Mod+Alt+=` is a second binding with
    `Mod+Alt+Plus`'s intent and label.
  - **Cost.** Parsed keys are cached, so a keydown stays cheap.
- **The platform through `kbd`.** The find bar's `Mod+F` matches with `tmKeysMatch`. The pointer's
  Mod (§C.1's new tab, Mod+drag) reads `tmIsApplePlatform`. The context menu's hints use
  `tmFormatKeys` and `tmKeysText` (§C.10). The grid keeps no platform check of its own, which
  `tm/no-platform-sniffing` enforces.
- **Consumers.** The shortcut registry refuses a registration equal to an entry that carries a
  modifier (spec 0027 §4.3); the Shortcuts dialog renders the list (spec 0027 §11.2).

**Example —** `{ keys: 'Mod+D', labelKey: 'tmUi.grid.keys.fillDown' }` renders as "Fill down" with
Ctrl D on Windows and ⌘ D on Apple. On an Arabic layout, Ctrl+D sends `key` `'ي'` with `code`
`'KeyD'`, and fill down runs.

**Compatibility:** no public API change. **Tests:** unit — each binding matched by a synthetic
event, an Arabic `key` with a Latin `code` included. e2e — the grid's Mod+Z, Mod+D, Mod+A and
Mod+F on an Arabic layout. `lint:test` — `tm/no-platform-sniffing` vectors. **Budget:** `kbd`
stays outside `./grid`'s self-weight.

### §C.13 Grid and menu glyphs through `tm-icon`

**Target:** 0004 §8.5 (the context menu), §8.7 (find), §10 (the error tally), §13.2 (the tree
expander), §15 (RTL). **Requested by:** spec 0023 §3.1. **Why:** one drawing path, and one live
RTL rule for the grid's directional glyphs.

The grid's and the menu's inline SVGs become `tm-icon` instances, under the shared glyph rule
(Parts B–E: rules shared).

| Where | Glyphs | Mirrors in RTL |
|---|---|---|
| Context menu | `scissors`, `copy`, `copy-plus`, `clipboard-paste`, `plus`, `corner-down-right`, `trash-2` | no |
| Find bar | `chevron-up`, `chevron-down`, `x` | no |
| Status bar | `chevron-left`, `circle-alert`, `chevron-right` | the chevrons |
| Tree expander | `chevron-right` when collapsed, `chevron-down` when expanded | the collapsed chevron |
| Sort indicator (§C.3) | `arrow-up`, `arrow-down` | never |
| Menu rows (§C.10) | `check` (checkable items), `external-link` (`target: '_blank'`), `chevron-right` (submenu parents) | the submenu chevron |

- **The context menu passes data.** Its entries carry `TmIconSource` icons (§C.10), so the
  internal `ɵTmGridIcons` component and its templates go.
- **Live direction.** Mirroring reads the computed direction (spec 0023 §3.1). The expander swaps
  glyphs instead of rotating one. This replaces its `:host-context([dir='rtl'])` rotation, which
  reads the `dir` attribute rather than the live direction.
- **The masks stay.** Checkbox cells and the select-all header draw `check` and `minus` as CSS
  masks (`--tm-grid-check-mask`, `--tm-grid-dash-mask`). A grid paints thousands of them, and a
  mask costs no component. They keep `tm-checkbox`'s paths and stroke (§B.7).
- **Per-row cost.** The expander is one `tm-icon` per visible tree row. The grid's scroll probe
  (no long task over 50 ms) runs on an expanded tree.

**Compatibility:** no public API change beyond §C.10's icon type; `./grid` imports
`@tellma/core-ui/icon` at runtime. **Tests:** e2e geometry — every glyph's box and stroke in LTR
and RTL; the expander and status-bar chevrons after a live direction flip; the scroll probe on an
expanded tree. **Budget:** within §C.11's ceilings, re-measured.

### §C.14 The row-end column

**Target:** 0004 §4, §6.1, §8.2, §9. **Requested by:** spec 0030 §7.1, §11.1. **Why:** a list's
column editor sits in the grid's own header, stays in view while the grid scrolls sideways, and
goes when the grid does; the column also holds a per-row menu later.

```ts
// ɵTmGridBase: two new content templates
@Directive({ selector: 'ng-template[tmGridRowEndHeader]' })
declare class TmGridRowEndHeaderDef {}
@Directive({ selector: 'ng-template[tmGridRowEnd]' })
declare class TmGridRowEndDef {}
interface TmGridRowEndContext<T> { readonly $implicit: T; readonly rowId: TmRowId }
```

**Example —** the search page's column editor button (spec 0030 §7.1):

```html
<ng-template tmGridRowEndHeader>
  <button tmGridHeaderAction [tmPopoverTriggerFor]="columns" [attr.aria-label]="t('Choose columns')">
    <tm-icon name="columns-3" /></button>
</ng-template>
```

- **Presence.** Declaring either template adds one column after the last data column.
- **Geometry.** `--grid-row-end-width` wide (36 px: a 24 px action and 6 px each side, a new
  token), never resized. Its cells are `position: sticky; inset-inline-end: 0`, so the column stays
  at the inline end while the content scrolls under it, the right in LTR and the left in RTL. They
  paint §C.6's row properties and the zebra, selected, checked and active states, so a row reads
  as one band. A hairline on the column's inline-start edge shows while content scrolls under it.
- **The header cell** renders `tmGridRowEndHeader`: one `button[tmGridHeaderAction]` (§C.4), with
  no label, sort slot or resize handle. Header navigation reaches it last; Enter, Space or
  Alt+ArrowDown click its action, and `headerActionHint` describes it.
- **Body cells** render `tmGridRowEnd` per rendered row. Without it they are empty and
  `aria-hidden`, and a click on one activates the row as a data cell does (§C.1). With it, a body
  cell is a `gridcell` that the inline-end arrow and End reach after the last data column; a click
  on its content is interactive content (§C.1).
- **Not a data column.** Range selection, Mod+A, copy and "Copy with headers", cut, paste, fill,
  find, column select, resize, sort and the state store's column widths and order never include
  it.
- **States keep the header.** The header row, this cell included, stays while the grid shows its
  loading or empty template (0004 §5.4), so the action stays reachable whatever the body shows.

**Compatibility:** additive; a grid that declares neither template renders as today. **Tests:**
unit — the column outside selection, copy, find and column select; header navigation reaching the
action; an empty body cell activating its row. e2e — the column at the inline end in LTR and RTL
while the grid scrolls sideways, the row bands continuous under it, the header kept with the empty
template; axe with the popover open. **Budget:** within §C.11's `./grid` ceiling, re-measured.

### §C.15 Popup cell editors

**Target:** 0004 §4 (the fixed row height), §6.1, §8.4 ("the editor mounts inside the cell box").
**Requested by:** spec 0032 §4 (a role membership's window), §5.3 (a permission's filter). **Why:**
an editor taller than a row, such as a multi-line expression or a small sub-form, opens over its
cell and spills past the grid's edge, while every row keeps its height.

```ts
// tm-grid-column — new input
readonly editorPopup = input<TmGridEditorPopup | null>(null); // null: the editor mounts in the cell
// @tellma/core-ui/grid
interface TmGridEditorPopup {
  readonly minWidth?: number;    // px; default 320
  readonly maxHeight?: number;   // px; default 360; the content scrolls inside beyond it
}
```

- **Where it renders.** The column's `*tmGridEditor` template renders in an anchored pane (§D.1)
  hosted by the gridcell, `popoverHost: { type: 'parent', element: cell }`, never by the row. The
  pane is a DOM descendant of the cell, so focus stays inside the grid and commit-on-blur (0004
  §8.4) keeps its meaning, while the top layer escapes the scroller's clipping.
- **Geometry.** The pane covers the cell's own box and grows toward the block end, so the cell
  appears expanded. Its width is `max(cell width, minWidth)`, aligned to the cell's inline start
  and pushed to stay inside the viewport. It flips to grow upward when the viewport has no room
  below. It takes no layout space: rows keep their height and nothing moves.
- **Always edit mode.** The editor opens in edit mode even when typing seeds it, and F2 does not
  toggle the mode, so the arrows move inside the editor and never commit and move.
- **Keys.**
  - Tab and Shift+Tab move among the pane's controls. From the last or the first one they commit
    and move to the next or previous editable cell, as Tab does from a cell editor.
  - Enter commits and moves down unless the editor consumed it: a multi-line editor inserts a
    line. Mod+Enter always commits and keeps the cell active.
  - Escape works in stages. The editor's own layers close first, since they sit on §D.1's stack;
    the next Escape cancels and closes the pane.
- **Pointer and scroll.** A press inside the pane is not a grid gesture (§D.1). A press on another
  cell commits, as today, and a press outside the grid commits on blur. The pane follows its cell
  when the grid or the page scrolls. While the cell is out of the grid's view the pane hides, its
  state kept, and the next keystroke scrolls the cell back first (0004 §4).
- **Read-only cells.** In a readonly grid or row, F2 on a popup column's cell opens the pane with
  the editor's field `readonly`; Escape, Tab or a press outside closes it, and nothing commits.
  Enter keeps its readonly meaning (§C.1).
- **Errors.** While the pane is open, the cell's error popover (0004 §10) stays closed, because the
  editor shows its own diagnostics.
- **Undo and clipboard.** A commit is one undo step. While the pane is open, paste and copy go to
  the editor (0004 §8.4).
- **ARIA.** The pane is labelled by the column header. With more than one control it is
  `role="group"`.

**Example —** spec 0032 §5.3's filter column: `<tm-grid-column key="filter" header="Filter"
[editorPopup]="{ minWidth: 480 }">` whose `*tmGridEditor` holds `<tm-expression-editor
clause="filter" [rows]="4" />`. Typing `C` opens the pane seeded with "C", and ← moves the caret.

**Compatibility:** additive; `null` keeps today's in-cell editor. **Tests:** unit — edit mode under a
typed seed; Tab inside a two-control pane and out of its last control; Enter consumed and
committing; Mod+Enter; Escape stages; the read-only open; one undo step. e2e (`@cross-engine`) —
the pane spilling past the grid's bottom edge; the upward flip at the viewport edge; inline-start
alignment in RTL; focus inside the gridcell, with axe while open; a press outside committing; the
pane hidden and restored as its cell scrolls out and back. **Budget:** `./grid` +1 KB (§C.11), with
one `budgetNotes` entry.

### Part C — Testing

- **Unit.**
  - Grid input: row activation gestures and the 400 ms guard; a right-click and a long-press on a
    cell link left to the browser, elsewhere in the cell opening the grid's menu; printable
    characters other than Space unhandled in a readonly grid; the driver's keys and ARIA; header
    navigation, Enter on a header that only has an action included.
  - Grid state: the sort cycle and `aria-sort`; select-all across pages; `rowClass`;
    `scrollToRow` with `column` and flash; `expansionSource` in both modes, the bound set kept
    unpruned across batches, a consumer write after bind, batching and sibling order; the row-end
    column outside the column model (§C.14).
  - Popup editors: edit mode under a seed, Tab, Enter and Escape in the pane, the read-only open
    (§C.15).
  - The menu kinds and a row's `lang`; an entry with `disabledReason` focusable, inert on
    activation and described by its reason.
  - Keys: each binding matched through `tmKeysMatch`, an Arabic `key` with a Latin `code`
    included (§C.12).
- **Playwright.**
  - The `grid-search-page` story: driver input, activation, sort, header actions with popovers,
    cross-page selection over a paged source, no row headers, the sticky row-end column while
    scrolling sideways, and the native menu on a cell link.
  - `menu-rich`, with 44 px rows at 390 px; a tree expansion restore; the gutter test with
    scrollbars shown.
  - The grid's Mod+Z, Mod+D, Mod+A and Mod+F on an Arabic layout, with a synthetic `key`/`code`
    pair (§C.12).
  - Glyph geometry, a live direction flip and the scroll probe on an expanded tree (§C.13).
  - The `grid-popup-editor` story: the pane spilling past the grid, its flip and its RTL alignment,
    on `@cross-engine` (§C.15).
  - Every new path in `dir=rtl`.
- **`lint:test`.** The `tm/no-platform-sniffing` vectors.
- **axe.** The new stories in both themes and directions.
- **Gates.** The `./grid`, `./tree-grid` and `./menu` goldens; the §C.11 ceilings.

### Part C — Definition of done

- **Grid behaviour:** §C.1–§C.9, §C.14 and §C.15 on both grids; `TmGridDriver`,
  `TmGridHeaderAction`, `TmGridHeaderEndDef`, `TmGridRowEndHeaderDef`, `TmGridRowEndDef`,
  `TmGridEditorPopup` and `TM_GRID_KEYS` exported. `TmGridHarness` gains `activateRow(i)`,
  `getActiveRowIndex()`, `sortBy(key)`, `getSort()`, `openHeaderAction(key)`, `openRowEndAction()`,
  `getEditorPopup()` and `isRowFlashing(i)`; `TmTreeGridHarness` gains `getExpandedIds()`.
- **Menu:** §C.10, with `TmMenuHarness.openSubmenu(label)`, `getGroups()`, `isChecked(label)`,
  `getLinkHref(label)`, `getDisabledReason(label)` and a new `TmMenuTriggerHarness`; the grid's
  context menu on `TmKeys` and `TmIconSource`.
- **Keys:** §C.12's one binding table, matched through `kbd`; no platform check outside `kbd`,
  enforced by `tm/no-platform-sniffing` in the library's lint config.
- **Glyphs:** §C.13 on both grids and the menu; `ɵTmGridIcons` removed.
- **Showcase:** the stories `grid-search-page`, `grid-popup-editor`, `menu-rich` and a
  `tree-grid` expansion-restore variant.
- **Tokens:** `--grid-active-row-bg`, `--grid-flash-bg`, `--grid-flash-duration`,
  `--grid-row-end-width` and `--menu-item-height-touch` in the preset; the identity server's token
  CSS refreshed.
- **UI rules:**
  - **Instant feedback** — the active-row class lands in the press's frame (e2e).
  - **Non-blocking** — sort and header actions never wait on data; the consumer fetches.
  - **Stable layout** — the gutter e2e (§C.7); the sort and header-action slot geometry e2e; glyph
    boxes hold (§C.13 e2e); the row-end column holds its place while the grid scrolls (§C.14 e2e);
    an open popup editor moves no row (§C.15 e2e).
  - **RTL** — every new e2e runs in `dir=rtl`: driver arrows, submenu side, header navigation, the
    row-end column at the left, the popup pane on its cell's inline start; the expander and
    status-bar chevrons mirror after a live flip (§C.13 e2e).
  - **WCAG AA** — axe on the new stories in both themes and directions; keyboard-only e2e for
    activation, sort, filter open, cross-page selection and submenus; 44 px menu rows below `sm`
    (e2e); the editing shortcuts on an Arabic layout (§C.12 e2e); focus inside the gridcell while
    a popup editor is open, with axe (§C.15 e2e).
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
9. **A totals row is the last data row with `tm-grid__row--emphasis`** — no footer-row mechanism;
   and no row-selectable predicate, which no consumer needs (§C.6).
10. **`scrollToRow` takes `column` but never focuses** — the caller focuses with `grid.focus()`:
    one way to move focus (§C.8).
11. **`loadChildren` takes a batch; `expansionSource` says who keeps the expansion, and a
    model-kept set keeps unknown ids** — one signature serves an expand and a restore; an explicit
    input cannot flip when the grid writes its own model, as an `undefined`-means-unbound sentinel
    would; an additive `loadChildrenBatch` was the alternative (§C.9).
12. **Menus stay data-driven; new kinds are union members; submenus go one level** — `TmMenuItem`
    and `itemSelected` keep their types. No segmented group layout and no per-entry class hook: the
    shell's toggles are radio items (§C.10).
13. **One key-map export, with labels** — one list serves the conflict rule and the help dialog
    (§C.12).
14. **Ceilings re-baselined to the measured surface** — 0004's 24 KB was never met, and a ceiling
    that is not real guards nothing (§C.11).
15. **The grid matches letter, digit and symbol shortcuts through `kbd`; named keys keep direct
    checks** — `event.key` alone fails on an Arabic layout, and named keys are the same on every
    layout; migrating every handler would churn tested code for no user gain (§C.12).
16. **One binding table drives the matcher and `TM_GRID_KEYS`** — the published list cannot drift
    from what the grid handles (§C.12).
17. **The grid's per-cell check and dash stay CSS masks** — a grid paints thousands of them; a
    `tm-icon` per cell was the alternative (§C.13).
18. **A link in a cell keeps the browser's menu; the rest of the cell keeps the grid's** — a real
    link offers new window, copy address and bookmark; the keyboard path and range copy keep the
    grid's menu (§C.1).
19. **The row-end column is sticky and outside the column model** — a column editor at the far end
    of a wide grid must stay in view, and an empty column must never join copy, find or ranges, as
    a plain consumer column would (§C.14).
20. **A tall editor opens in a pane hosted by its gridcell, over the cell, always in edit mode** —
    the row keeps its height and the pane spills past the grid with no portal, so focus and
    commit-on-blur behave as for an in-cell editor; a panel under the grid, which separated the
    value from its row, was the alternative (§C.15).
21. **`TmGridDriverTarget` carries the ids and the page step the driver needs, as signals** — the
    driver then works over any target, tiles and a page's forwarding target included, without
    reading grid internals (§C.2).

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
6. **The grid ceiling re-baselined to 43 KB with no split** (§C.11). Alternative: load the
   editable-only machinery (editors, the editor session, the paste ladder, fill, the status bar,
   touch handles) through `import()` on the first `field` bind or paste, about 8–10 KB off the
   search route. Flips if spec 0024's search-route budget is exceeded.
7. **Submenus one level deep** (§C.10). Flips with a consumer that needs a second level.
8. **One `tm-icon` per visible tree row and option row** (§C.13, §B.7, §E.5). Alternative: CSS
   masks, as the checkbox cells use. Flips if the scroll probe or a long option list shows a long
   task.
9. **The popup pane covers its cell and grows down** (§C.15). Alternative: a pane that starts under
   the cell, keeping the cell's value in view. Flips with a consumer that needs to see the cell
   while editing.

## Part D — Basics (spec 0005)

Part D changes the basic components and the shared infrastructure of spec 0005: the overlay
helpers, the modal and popover, the l10n formats, the client cache, the image, the button, the
tabs and the components' glyphs. Its consumers are spec 0023 (overlay, alert dialogs, avatars,
icons, relative time), the shell and attention surfaces (specs 0027, 0028), the page frameworks
(specs 0026, 0030, 0031, 0035), the report renderers (specs 0033, 0034), the client entity model
(spec 0029) and the app foundation (specs 0024, 0025).

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

### §D.4 Date-time, time of day, relative time and zone labels

**Target:** 0005 §2.2 (the closed format set). **Requested by:** spec 0023 §4
(`tm-relative-time`), spec 0024 §7.8, spec 0028 §6.2, spec 0029 §12.1–§12.2, spec 0035
§8.5; spec 0024 §7.1 fills both zones. **Why:** "2 min ago" and "Updated 10:42" format instants
in the display zone, and an absolute time names its offset when the device sits in another one.

The closed format set gains three formats over **instants**, each with golden rows. The absolute
ones can carry an offset label.

```ts
// @tellma/core-ui/l10n — pure, DOM-free, DI-free
type TmTimeStyle = 'short' | 'medium';  // short: hour and minute; medium: adds seconds; the hour cycle per locale
interface TmDateTimeFormatOptions {
  readonly timeZone: string;             // an IANA id; required
  readonly calendar?: TmCalendar;        // default Gregorian
  readonly dateStyle?: TmDateStyle;      // default 'numeric'
  readonly timeStyle?: TmTimeStyle;      // default 'short'; §D.8 adds 'none'
  readonly zoneLabel?: boolean;          // true: appends the zone's 'shortOffset' name ("GMT+3")
}
interface TmTimeFormatOptions { readonly timeZone: string; readonly timeStyle?: TmTimeStyle; readonly zoneLabel?: boolean }
interface TmRelativeTimeFormatOptions {
  readonly now: number;                  // epoch ms: the caller's clock
  readonly timeZone: string;             // decides calendar days
  readonly calendar?: TmCalendar;        // for the absolute fallback
  readonly style?: 'long' | 'short' | 'narrow'; // default 'short'
}
declare function tmFormatDateTime(instant: string | null | undefined, locale: string, options: TmDateTimeFormatOptions): string;
declare function tmFormatTime(instant: string | null | undefined, locale: string, options: TmTimeFormatOptions): string;
declare function tmFormatRelativeTime(instant: string | null | undefined, locale: string, options: TmRelativeTimeFormatOptions): string;
declare function tmFormatZoneOffset(instant: string | number, locale: string, timeZone: string): string; // "GMT+3", "GMT+5:30", "غرينتش+٣"
declare function tmFormatZoneName(timeZone: string, locale: string, at: number): string;                 // 'longGeneric': "United Kingdom Time"

// @tellma/core-ui (primary)
declare const TM_TIME_ZONE: InjectionToken<Signal<string>>;        // default: the browser's resolved zone; @tellma/core provides the display zone
declare const TM_DEVICE_TIME_ZONE: InjectionToken<Signal<string>>; // default: the browser's zone, read once; @tellma/core provides a live one
declare const TM_NOW: InjectionToken<() => number>;                // new; epoch ms; default Date.now; @tellma/core-testing's fake clock replaces it
type TmZoneLabelMode = 'auto' | 'column' | 'none';
// TmL10n gains (each reads locale, calendar and timeZone first):
readonly timeZone: Signal<string>;
readonly deviceTimeZone: Signal<string>;
readonly zoneMismatch: Signal<boolean>;      // the two offsets differ now; recomputed when a zone changes and on visibilitychange
zoneLabel(instant?: string | null): string;  // the display zone's offset at instant (default now) when it differs from the device's; else ''
formatDateTime(instant: string | null | undefined, options?: { dateStyle?: TmDateStyle; timeStyle?: TmTimeStyle; zoneLabel?: TmZoneLabelMode }): string; // zoneLabel default 'auto'
formatTime(instant: string | null | undefined, options?: { timeStyle?: TmTimeStyle; zoneLabel?: TmZoneLabelMode }): string;
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
- **Zone labels.** An absolute date-time or time shows the display zone's offset when that offset
  differs from the device zone's. Spec 0024 §7.8 decides which surfaces show it.
  - **Compared at the instant shown,** never by zone id, so `Asia/Kuwait` and `Asia/Riyadh` (both
    +3) never label.
  - **Offsets, never abbreviations.** "IST" and "CST" are ambiguous. "GMT+5:30" is not, and it
    localizes ("غرينتش+٣").
  - **Never labelled:** relative time, dates, and `timeStyle: 'none'` (§D.8).
- **The modes** of the facade's `zoneLabel` option:
  - **`'auto'`** (the default) labels when `zoneLabel(instant)` is not empty.
  - **`'column'`** is for the cells of a column whose header shows `zoneLabel()` while
    `zoneMismatch()` is true (spec 0029 §12.1).
    - **Under a labelled header,** a cell labels when its instant's display offset differs from the
      header's, as across a DST change.
    - **Under a bare header,** a cell labels when `'auto'` would.
  - **`'none'`** never labels.
- **The golden.** `l10n/format-golden.json` gains four arrays:
  - **`dateTime` and `time`,** with rows `{ locale, timeZone, calendar, dateStyle, timeStyle,
    zoneLabel?, instant, expected }`. Rows with `zoneLabel: true` cover `en-GB`, `ar-SA` and
    `ar-SA-u-nu-latn` in `Asia/Riyadh`, and `Asia/Kolkata` (+5:30).
  - **`relative`,** with rows `{ locale, timeZone, now, instant, style, expected }`.
  - **`zoneLabel`,** with rows `{ locale, timeZone, deviceTimeZone, instant, mode, header,
    expected }`. Its rows cover equal offsets with different ids, `Asia/Beirut` across its DST
    change, and `UTC`.

  The arrays cover the date rows' locales and the zones `UTC`, `Asia/Riyadh`, `Africa/Addis_Ababa` and
  `Asia/Beirut`. Beirut has DST, for the day-boundary rows.

**Example —** for `2026-10-20T07:42:00Z` in `Asia/Riyadh`, with the device in the same zone:
`l10n.formatTime(at)` gives "10:42" in en-GB and "١٠:٤٢ ص" in ar-SA; `l10n.formatDateTime(at)`
gives "20/10/2026, 10:42" in en-GB. With the device in `Europe/London`, `l10n.formatDateTime(at)`
gives "20/10/2026, 10:42 GMT+3", and `l10n.zoneLabel(at)` gives "GMT+3".

**Compatibility:** additive. **Tests:** the four golden arrays; unit — every threshold boundary,
truncation, a DST day boundary, malformed input; the three label modes, and `zoneMismatch` after a
zone change. **Budget:** `./l10n` 7.65 → ≈ 9.3 KB with §D.7 and §D.8; the primary tokens and
facade within Part B's primary ceiling.

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

**Compatibility:** `clearAll()` also deletes the IndexedDB databases and storage keys the naming
rule names; the showcase's image story is its only caller. **Tests:** unit in browser mode — each
area deleted; `keep` honoured per area; a blocked IndexedDB delete (a second open connection that
ignores `versionchange`) resolves; a missing `indexedDB.databases` skips the area; `ngsw:` caches
untouched. **Budget:** primary +0.3 KB, within Part B's primary ceiling.

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

### §D.7 Month, quarter and year formats; month and weekday names

**Target:** 0005 §2.2 (the date styles). **Requested by:** spec 0033 §3.2, spec 0034 §3.5.
**Why:** report terms display a date as its month, quarter or year, and a month or weekday number
as its name, in an explicit calendar.

```ts
// @tellma/core-ui/l10n
type TmDateStyle = 'numeric' | 'medium' | 'long' | 'month' | 'quarter' | 'year' | 'weekday'; // old: 'numeric' | 'medium' | 'long'; weekday is §D.8
interface TmDateFormatOptions {
  readonly calendar?: TmCalendar;        // unchanged: default Gregorian
  readonly dateStyle?: TmDateStyle;      // unchanged: default 'numeric'
  readonly quarterPattern?: string;      // new: '{quarter}' and '{year}' placeholders; default 'Q{quarter} {year}'
}
declare function tmFormatMonthName(month: number, locale: string, options?: { readonly calendar?: TmCalendar }): string; // new
declare function tmFormatWeekdayName(weekday: number, locale: string): string; // new; 1 = Monday … 7 = Sunday
// TmL10n gains formatMonthName(month, options?) and formatWeekdayName(weekday), in its locale
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
- **Names.** A month name is the standalone long month (`LLLL`) of that month in the calendar,
  read off a day of the month in a fixed reference year. A weekday name is the standalone long
  weekday (`cccc`) of a day in a fixed reference week. A number outside 1–12 (1–13 in Ethiopic)
  or 1–7 formats as a number through `tmFormatNumber`.

**Example —** the date `2026-03-15`, which falls on a Sunday and in Hijri month 9:

| Locale | Calendar | `month` | `quarter` | `year` | Month name (3; 9) | Weekday name (7) |
|---|---|---|---|---|---|---|
| en-GB | Gregorian | Mar 2026 | Q1 2026 | 2026 | March | Sunday |
| ar-SA | Umm al-Qura | رمضان ١٤٤٧ | الربع ٣ ١٤٤٧ | ١٤٤٧ | رمضان | الأحد |

**Compatibility:** additive. **Tests:** golden rows per style and per name over Gregorian, Umm
al-Qura and Ethiopic, Ethiopic's thirteenth month included; out-of-range numbers. **Budget:**
within §D.4's `./l10n` estimate.

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

### §D.11 Basics glyphs through `tm-icon`

**Target:** 0005 §6.2, §6.4 (the date picker), §7.4–§7.5 (the image), §8.1, §8.4 (files), §10
(the modal), §13 (the alert), §15 (RTL). **Requested by:** spec 0023 §3.1. **Why:** one drawing
path, and one live RTL rule for the date popup.

The basics' inline SVGs become `tm-icon` instances, under the shared glyph rule (Parts B–E: rules
shared).

| Entry point | Glyphs | Mirrors in RTL |
|---|---|---|
| `alert` | the four tone glyphs: `info`, `circle-check-big`, `triangle-alert`, `circle-x` | no |
| `date-picker` | `circle-alert` (error), `calendar` (trigger); in the popup, `chevron-left` and `chevron-right` | the popup chevrons |
| `file-preview` | `circle-x` (error), `file-text` (no preview) | no |
| `files` | `upload` (the dropzone) | no |
| `image` | `image-off` (error), `image` (empty), `camera`, `crop`, `trash-2`, and the edit pane's `check` | no |
| `modal` | `x` (close) | no |
| `private` | `circle-alert` (the error popover) | no |

- **The popup's flip.** The date popup's chevrons pass `mirrorInRtl`, which replaces their
  `transform: scaleX(-1)` rule.
- **The edit pane's check** keeps its heavier 2.25 stroke: its host sets `--icon-stroke-width`.
- **Docs examples.** The `button` and `input` examples show `<tm-icon [svg]>` children, which
  §D.10 sizes, in place of inline SVG.

**Compatibility:** no public API changes; each entry point imports `@tellma/core-ui/icon`.
**Tests:** e2e geometry — every glyph's box and stroke in LTR and RTL; the popup chevrons after a
live direction flip. **Budget:** re-measured; `./alert` and `./image` sit closest to their
ceilings.

### §D.12 `tmButton` on links, and `disabledReason`

**Target:** 0005 §3, §12, and its Non-goal "Anchor tags styled as buttons", which is lifted.
**Requested by:** spec 0026 §9.4, spec 0027 §6.2, spec 0030 §13.1, spec 0031 §5.2. **Why:** a
link action looks like the buttons beside it, and an action the user may not take says why.

```ts
// TmButton — selector: 'button[tmButton], a[tmButton]' (old: 'button[tmButton]'); new input
readonly disabledReason = input<string | null | undefined>(undefined); // non-empty: disabled, and this is why

// TmTooltip (0005 §12) — new internal input
readonly ɵshowOnTap = input(false, { transform: booleanAttribute }); // a tap shows the tooltip; the next tap elsewhere hides it
```

- **Links.** On an `<a>`, the directive applies the variant and size and nothing else: no `type`,
  no `pending`. The anchor stays a link, so Mod+click, the middle button and the browser's link
  menu stay native.
- **A reason** disables a `button[tmButton]` without `disabled`:
  - it renders `aria-disabled="true"`, keeps focus, and suppresses activation as `pending` does
    (click, Enter and Space swallowed at the host), with the disabled styling;
  - the reason is a `tmTooltip` (0005 §12) that shows on hover, on focus-visible and on a tap,
    since the tap does nothing else. The tap goes through the tooltip's internal `ɵshowOnTap`,
    because 0005 §12's tooltip opens on touch only by long-press; a tap-opened tooltip hides on
    the next tap elsewhere;
  - AriaDescriber describes the host with the reason, so a screen reader hears it after the name.
- **An anchor cannot be disabled.** A page renders a link action that has a reason as a
  `button[tmButton]` with it; a reason on an anchor is a dev error.
- **Transient states stay native.** `disabled` serves loading, busy states and locks, with no
  tooltip. A reason is for a fact the user can act on, such as a missing permission. Setting
  both is a dev warning.

**Example —** `<button tmButton [disabledReason]="permissions.disabledReason(entity, 'save')()"
(click)="edit()">Edit</button>` shows "Needs “Customers: Create and edit”. Ask your administrator
to grant it." on hover, focus and tap.

**Compatibility:** additive; a button without a reason is unchanged. **Tests:** unit —
`aria-disabled`, focus kept, click, Enter and Space suppressed, the description; an anchor's
variants and sizes; the dev error and warning. e2e — the tooltip on hover, focus and tap; axe.
**Budget:** `./button` imports `@tellma/core-ui/tooltip`; its self-weight stays within 2 KB.

### §D.13 Instantiating an unselected tab

**Target:** 0005 §9 ("only the active tab is in the DOM"). **Requested by:** spec 0031 §6.6, §8.4.
**Why:** after a failed save, the fields on unvisited tabs must exist so their errors show and
take focus.

```ts
// TmTabGroup — new method
instantiate(ids?: readonly string[]): void;   // default every tab; renders content without selecting
```

- **The effect.** Each named tab without content renders it now, `hidden` and `inert` in its
  unselected panel, and keeps it from then on, as a `preserveContent` tab keeps a visited panel.
  Selection, focus and the panels' order do not change. The content exists after the next render
  (`afterNextRender`).
- **Existing content stays.** The selected tab and a visited `preserveContent` tab are left as they
  are, so no state inside them is recreated.
- **The group renders it.** In `@angular/aria` 22.0.3, `TabPanel` writes its deferred content's
  `contentVisible` from its own selection on every render, so `ngTabContent` cannot show an
  unselected panel. For an instantiated tab, `tm-tab-group` renders the content template itself
  through `ngTemplateOutlet` inside the panel element, and aria's `inert` on the unselected panel
  holds. Selecting that tab later shows the same instance.
- **The cost is the caller's.** A tab's content renders only when asked; spec 0031 asks after a
  failed save leaves an error on a field no tab rendered.

**Compatibility:** additive. **Tests:** unit — content present, hidden and inert, with selection
unchanged; existing content not recreated; a later selection showing the same instance.
**Budget:** within `./tabs`' ceiling.

### §D.14 Formatted text without bidi controls

**Target:** 0005 §2.2 (the codecs), §15 (the date placeholder's marks), and the formats of §D.4,
§D.7 and §D.8; 0004 §15 (copy exports display strings); 0002 §7 (the default message resolver).
**Requested by:** spec 0024 §7.9. **Why:** a copied date or amount pastes into a spreadsheet as a
date or a number, and two strings that look equal are equal.

```ts
// @tellma/core-ui/l10n — new
declare const TM_BIDI_CONTROLS: RegExp;                    // global: spec 0024 §7.9's set
declare function tmStripBidiControls(text: string): string;
```

- **The strip.** Every formatter of `@tellma/core-ui/l10n` (`tmFormatNumber`, `tmFormatDate`,
  `tmDatePlaceholder`, and those of §D.4, §D.7 and §D.8) and every `TmL10n` method removes
  `TM_BIDI_CONTROLS` (spec 0024 §7.9's set) from Intl's output through `tmStripBidiControls`. CLDR's
  Arabic patterns carry U+200F between date parts, and U+200E or U+061C beside a minus or a percent
  sign. The parsers already skip format characters, so round trips hold.
- **The placeholder** loses its marks with the value. Both then lay out as one left-to-right run
  in an RTL field, so the placeholder's fields still read in the value's order.
- **Direction is markup.** The grid's `number` cells (0004 §6.1) wrap their text in a `dir="ltr"`
  span. `tmNumber` gains a `dir="auto"` host attribute, as `tm-input` has, so its digits resolve to
  LTR. Either way a minus shows at the left, Arabic-Indic digits included (Review flag 7). Dates
  need nothing: a numeric date is one digit run, and a month name orders itself.
- **Messages.** The default `TM_UI_TRANSLATE` (0002 §7) keeps messageformat's `biDiSupport` off,
  its default, and removes the set from each resolved message, so neither an ICU `number`
  argument nor a translator's pasted mark reaches the text.
- **Copy.** The grid copies these strings (0004 §15), so a copied range carries no mark.
- **The golden.** `format-golden.json`'s expected strings lose the marks. Server text drops the
  same set (spec 0013 §9.2).

**Example —** `tmFormatDate('2026-11-02', 'ar-SA')` gives "٢/١١/٢٠٢٦", and
`tmFormatNumber(-1234.5, 'ar')` gives "-1,234.5".

**Compatibility:** output changes in Arabic locales only; `TM_BIDI_CONTROLS`, `tmStripBidiControls`
and `tmNumber`'s `dir="auto"` are new. **Tests:** golden rows for `ar`, `ar-SA`, `ar-SA-u-nu-latn`
and `ar-EG`; a unit that no formatter output holds a character of the set, for every golden
locale; e2e — a negative number cell in `dir=rtl` shows its minus at the left, and so does an
`input[tmNumber]` with Latin and with Arabic-Indic digits; a copied Arabic date range pastes as
plain text. **Budget:** within §D.4's `./l10n` estimate.

### §D.15 Number grouping

**Target:** 0005 §2.2 (`TmNumberFormatOptions` and the cross-platform contract), §5 (`tmNumber`).
**Requested by:** spec 0029 §5.2; spec 0033 §3.2, §4.6; spec 0034 §4.1, §7.1. **Why:** a year, a
code or a serial number reads "2026", never "2,026".

```ts
// @tellma/core-ui/l10n
interface TmNumberFormatOptions {
  readonly grouping?: boolean;   // new; default true. false: no group separators
}
// TmNumber (input[tmNumber]) — new input
readonly grouping = input(true, { transform: booleanAttribute });
```

- **Formatting.** `false` formats with Intl's `useGrouping: false`. `true` keeps today's `'auto'`,
  which honours the locale's minimum grouping digits, so Spanish still writes "1234". The digits
  stay the locale's: `ar-SA` gives "٢٠٢٦".
- **Parsing is unchanged.** `tmParseNumber` still accepts group separators, so a typed "2,026"
  reads 2026.
- **`tmNumber`** formats its text through the option, so on blur a `grouping` of false writes
  "2026".
- **The contract.** 0005 §2.2's closed number set becomes locale × `minDecimals`/`maxDecimals`/
  `percent`/`grouping`.
- **Grids.** `number` columns are unchanged; a column that needs ungrouped text binds `format`.

**Example —** `tmFormatNumber(2026, 'en', { grouping: false })` gives "2026", and
`tmFormatNumber(1234567, 'en')` still gives "1,234,567".

**Compatibility:** additive. **Tests:** format golden rows with a `grouping` column, in `en`,
`ar-SA` and `es` at 1234 and 1234567; unit — `tmNumber` with `grouping` false reformats on blur
and still parses "2,026". **Budget:** within the `./l10n` and `./number` ceilings.

### Part D — Testing

| Suite | Pins |
|---|---|
| Unit | the overlay suites under the new entry point; modal placement classes, roles and live direction; popover sizes and clamping; the formatters' thresholds and malformed input; the zone-label modes and `zoneMismatch`; `clearAll` per area; blob abort reference counting; `placeholderOnError`; `disabledReason` and anchor buttons; `instantiate`; no formatter output holding a bidi control; `tmNumber` without grouping |
| Format golden | `dateTime` and `time` with their zone-label rows, `relative` and `zoneLabel`; the `month`, `quarter`, `year` and `weekday` styles and the month and weekday names over three calendars; number rows with and without grouping; every expected string free of bidi controls |
| Playwright | the drawer at 390, 768 and 1440 px in LTR and RTL, with a live flip; fixed popover geometry while content loads; aborted image requests counted on navigation; `tm-icon` in buttons; a reason's tooltip on hover, focus and tap; §D.11's glyph geometry and the popup chevrons after a live flip; a negative number cell's minus at the left in RTL, and an `input[tmNumber]`'s with Latin and Arabic-Indic digits |
| axe | the drawer, an alert dialog, the popover sizes and a button disabled with a reason, in both themes and directions |
| Gates | the new `./overlay` golden; the `./l10n`, `./modal`, `./popover` and `./image` goldens; the ceilings below |

### Part D — Definition of done

- **Overlay:** `@tellma/core-ui/overlay` public with a golden and a docs page; `private` reduced;
  every importer migrated (§D.1).
- **Modal and popover:** §D.2 and §D.3 shipped, with `TmModalHarness.getPlacement()` and
  `TmPopoverHarness.getSize()`; the drawer and popover tokens in the preset; the identity server's
  token CSS refreshed.
- **Formats:** §D.4, §D.7 and §D.8 shipped, with `TM_TIME_ZONE`, `TM_DEVICE_TIME_ZONE`, the facade
  methods and their label modes, the `tmUi.l10n.quarter` string and its Arabic twin; the golden
  diff reviewed through `golden:approve`.
- **Cache, blobs, image and button:** §D.5, §D.6, §D.9, §D.10 and §D.12 shipped, with
  `TmButtonHarness.getDisabledReason()`.
- **Tabs:** §D.13 shipped, with `TmTabGroupHarness.isInstantiated(id)`.
- **Bidi-free text:** §D.14 across every formatter, with `TM_BIDI_CONTROLS` and
  `tmStripBidiControls` exported, the grid's `number` cells and `tmNumber`'s `dir="auto"`.
- **Grouping:** §D.15 in `tmFormatNumber`, `tmNumber` and the format golden.
- **Glyphs:** §D.11's entry points draw through `tm-icon`.
- **Showcase:** the stories `modal` (drawer variants, an alert dialog), `popover` (sizes) and
  `i18n` (date-time, time, and relative with a fake clock, per zone; zone labels with a device zone
  that differs; month, quarter, year and weekday, and the month and weekday names, per calendar).
- **UI rules:**
  - **Instant feedback** — the drawer renders in the click's frame (e2e: no animation; the panel is
    present one frame after the click).
  - **Non-blocking** — image requests abort on navigation (§D.6 unit, and an e2e counting aborted
    requests).
  - **Stable layout** — fixed popover sizes and the drawer never resize while content loads
    (geometry e2e); glyph boxes hold (§D.11 e2e).
  - **RTL** — the drawer's edge and the live flip (§D.2 e2e); the date popup's chevrons after a
    flip (§D.11 e2e); the Arabic golden rows, free of bidi controls (§D.14).
  - **WCAG AA** — axe on the drawer, the alert dialog, the popover sizes and a button disabled
    with a reason in both themes and directions; the reason reaches the accessible description
    and shows on focus (§D.12).
- **Budgets:** `./overlay` **2** (new); `./private` 4 → **2**; `./l10n` 8 → **10**; `./modal` 6,
  `./popover` 4, `./image` 10 and `./button` 2 unchanged; §D.11's entry points re-measured; notes
  recorded.

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
11. **An unselected tab instantiates only on request** — `instantiate()` serves the one caller
    that must reach unrendered fields, a failed save; a `prerender` input would render every tab
    on every open (§D.13).
12. **Zone labels are GMT offsets, never abbreviations, compared at the instant shown** — "IST" is
    ambiguous, and two zone ids can share an offset; comparing zone ids was the alternative
    (§D.4).
13. **A button disabled with a reason keeps focus and explains itself** — a natively disabled
    button takes no focus and fires no pointer events, so its tooltip could never show;
    `aria-disabled` reuses `pending`'s suppressed activation (§D.12).
14. **Formatters drop CLDR's bidi marks, and markup orders the text** — a copied value equals what
    was shown, so a pasted Arabic date still parses as a date (§D.14).
15. **Grouping is a format option, and `true` keeps Intl's `'auto'`** — a year or a code needs no
    separators, and `'auto'` writes what each locale expects; `'always'` would group Spanish
    four-digit numbers (§D.15).
16. **`format-golden.json` pins the client's formatters only** — .NET formats through NLS on
    Windows and ICU on Linux, so client-equal strings are no server contract; the server keeps its
    own `ICalendarSystem` goldens (spec 0013 §12). It amends 0005 §2.2: no C# test reads the file
    (Parts B–E: rules shared).

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
6. **A numeric Arabic date lays out as one left-to-right run** (§D.14): "٢/١١/٢٠٢٦" shows its day
   at the left, where CLDR's marks put it at the right, as inputs, Excel and Word lay it out.
   Alternative: render a displayed date's fields in separate isolates through `formatToParts`,
   leaving cells and inputs in different orders. Flips if Arabic users read the order as wrong.
7. **A number in Arabic-Indic digits shows its minus at the left** (§D.14), where CLDR's U+061C
   puts it at the right. Alternative: `dir="rtl"` on numbers in Arabic-Indic digits. Flips on
   Arabic users' feedback.

## Part E — Entity picker (spec 0006)

Part E extends the entity picker for the client entity model's generic binding (spec 0029 §14)
and its navigation filters (spec 0029 §13). It also draws the picker's glyphs through `tm-icon`
(§E.5). Every member is additive: `tm-entity-picker` without the new inputs renders as today.

### §E.1 Two-line options

**Target:** 0006 §2, §4.1, and its Non-goal "Rich option templates" (lifted for a second line).
**Requested by:** spec 0029 §14.2, §14.3. **Why:** two Centers named "Jeddah" are told apart by
their code and parent, and a Latin code beside an Arabic name keeps its order.

```ts
// TmEntityPicker — new inputs
readonly itemDescription = input<((item: T) => string | null) | undefined>(undefined);
readonly itemLabelParts = input<((item: T) => readonly string[] | null) | undefined>(undefined); // joined by ' - ', they equal itemLabel
```

- **Rows.** With `itemDescription` bound, every result row is two lines: the label, then the
  description in secondary ink at `--text-xs`, each ellipsized.
- **One height.** All result rows share `--entity-picker-option-height-2line` (a new token), even
  a row whose description is `null`. The panel never re-measures between queries. Footer rows
  (Advanced search…, Create…, Edit…) keep the one-line height.
- **Accessible name.** "label, description", with a visually hidden separator, so a screen reader
  hears the disambiguating line.
- **Direction.** Each line renders in a `<bdi>`, so a label in another script than the page keeps
  its order (spec 0024 §7.9). One-line rows do the same. With `itemLabelParts`, the label line
  renders each part in its own `<bdi>`, joined by the plain text " - " (spec 0029 §5.2's fixed
  separator), so "INV-0011 - نقد" keeps its code first in an RTL page. `null` renders `itemLabel`
  in one `<bdi>`. The accessible name stays the plain label.
- **What the description holds** is the binding's choice (spec 0029 §14.2).

**Example —** `[itemDescription]="c => c.code + ' · ' + parentName(c)"`.

**Compatibility:** unbound, rows render as today. **Tests:** unit — the fixed row height with and
without descriptions; the accessible name; each label part in its own `<bdi>`.
**Budget:** +0.3 KB.

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
  readonly itemLabelParts = input<((item: T) => readonly string[] | null) | undefined>(undefined); // option rows, as §E.1; passed to the inner picker
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
// TmEntityPickerPageData gains multiple, ids and maxItems (§E.3's sketch);
// a page opened with multiple closes with a TmEntityMultiPick, whose ids replace the value
interface TmEntityMultiPick<Id extends TmEntityId = TmEntityId, T = unknown> {
  readonly ids: readonly Id[];                                // the whole selection: kept ids in value order, then new ones
  readonly picks: readonly TmEntityPick<Id, T>[];             // label memos for rows the page showed
}
```

**Example —** a Parent filter (spec 0029 binds it inside its filter control):

```html
<tm-form-field label="Parent">
  <tm-entity-multi-picker [formField]="filter.parents" [search]="centers.search" [itemId]="byId"
                          [itemLabel]="centers.label" [displayWith]="centers.labelOf" [maxItems]="50" />
</tm-form-field>
```

**Composition.** The field holds two parts:

- a `tm-chip-set` with `overflow: 'scroll'` of removable `tm-chip`s (spec 0023 §4.3), which owns
  the chips' roving focus;
- an inner `tm-entity-picker` in an internal `ɵcommitMode: 'emit'`.

The rules of the composition:

- **`'emit'` changes only the single picker's post-commit step.** A commit comes from the list,
  from auto-resolution on blur or Enter, or from the create page. In emit mode, the inner picker
  emits `picked` and returns to empty and pristine:
  - its own `value` stays `null`, and the settled answer is retired;
  - after a list pick or an Enter commit, with focus still in the input, the panel stays open on
    the browse list; after a blur, a Tab or a create-page commit it stays closed.

  Search, debounce, abort, resolution and the footer rows are the single picker's, untouched.
- **Toggle.** The multi-picker passes its chosen ids in (`ɵchosenIds`). Those rows show the check
  and `aria-selected="true"`, and the listbox is `aria-multiselectable`. Only an explicit list pick
  toggles: a pointer pick, or Enter on a highlighted row. Picking a chosen row that way removes it.
  Auto-resolution and a Tab commit that land on a chosen id keep its chip and clear the text.
- **Advanced search** opens with `{ multiple: true, ids, maxItems }` and edits the whole
  selection. Closing with a `TmEntityMultiPick` replaces `value` with its `ids`, an empty list
  included, and emits `picked` for each added id and `removed` for each removed one; `picks` seed
  the chip labels. Closing with no result leaves the value as it was. Create adds the created
  entity. Edit is not offered.
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
- **Overflow.** The chip set's `scroll` mode (spec 0023 §4.3) holds the chips; wheel, trackpad and
  keyboard scroll them. A fixed-width count badge at the inline end shows the total.
- **Chip labels** follow the single picker's ladder: `displayWith(id)`, else the pick's label
  memo, else `String(id)` with a dev warning.

**Keyboard:**

| Key | Where | Effect |
|---|---|---|
| typing, ArrowDown, Alt+ArrowDown, Enter, Tab, Escape | input | as the single picker (0006 §4.3); only a pointer pick or Enter on a highlighted row toggles (Toggle above) |
| ArrowLeft (ArrowRight in RTL) at the caret's start | input | the chip set's `focusLast()` |
| ArrowLeft / ArrowRight, Home / End | chips | the chip set's roving focus (spec 0023 §2.1); its `exit('end')` past the last chip returns focus to the input, and its `exit('start')` past the first chip leaves focus there |
| Backspace | empty input | the first press calls `focusLast()`; a second press removes that chip |
| Backspace / Delete | chip | removes it; focus moves to the next chip, else the previous, else the input (`exit('end')`, spec 0023 §2.1) |
| Tab, Shift+Tab | field | two tab stops: the chip set, one stop while a chip exists, then the input |

**ARIA:**

- **Roles.** The input stays the combobox. The chip set is `role="list"` of `role="listitem"`
  (spec 0023 §4.3), each a `tm-chip` whose remove button carries its own name.
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
text pristine, the settled answer retired); toggle-remove by pointer and by Enter;
auto-resolution and Tab on a chosen id keeping its chip; the panel open after a list or Enter commit
and closed after a blur, a Tab and a create-page commit; label parts reaching the inner picker's
rows; `maxItems`; advanced search closing with a set that adds and removes, and with an empty set,
emitting the difference. e2e — chip keyboard paths in LTR and RTL; the field's height with 0, 1 and
30 chips.

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
  readonly maxItems?: number;      // new (§E.2): the multi-picker's cap; the page refuses more
  readonly context?: unknown;      // new: the object form's context, passed through untouched
}
```

- **Opaque.** The picker never reads `context`; the page casts it to the shape its binding agreed.
- **`maxItems`.** A page opened with it keeps Done disabled above the cap, with §E.2's message.
- **The bare `Type` form** passes no context.

**Example —** a binding hands one search page the entity it serves (spec 0029 §14.4):
`advancedSearch = { component: searchPage, size: 'lg', context: { entity: 'acme.Customer' } }`.

**Compatibility:** additive. **Tests:** unit — the context reaches `TM_MODAL_DATA` for advanced
search, create and edit.

### §E.4 Two-line options in grid cells

**Target:** 0006 §7.1 (the `entity` column's data seam). **Requested by:** spec 0029 §14.5.
**Why:** a grid cell's dropdown tells entities apart as the field does, and its display keeps a
mixed-direction label in order.

```ts
// TmGridColumn — beside itemId, itemLabel and format
readonly itemDescription = input<((item: never) => string | null) | undefined>(undefined);
readonly itemLabelParts = input<((item: never) => readonly string[] | null) | undefined>(undefined);
readonly formatParts = input<((value: V, row: T) => readonly string[] | null) | undefined>(undefined); // as format's arguments; joined by ' - ', they equal format
```

- **The cell editor's dropdown** renders two-line rows and label parts as §E.1 does.
- **The display cell** shows the label only. With `formatParts`, it renders each part in its own
  `<bdi>`, joined by the plain text " - ", as §E.1's label line; otherwise `format`'s text in one
  `<bdi>`. A copied cell carries `format`'s plain text (0004 §15).

**Compatibility:** unbound, cells render as today. **Tests:** unit — the cell editor passes the
function to its picker; e2e — two-line rows in a cell's dropdown at the fixed height.

### §E.5 Picker glyphs through `tm-icon`

**Target:** 0006 §3 (anatomy), §4.1 (the option rows), §9 (RTL). **Requested by:** spec 0023
§3.1. **Why:** the picker draws its glyphs as every other library component does.

The picker's inline SVGs become `tm-icon` instances, under the shared glyph rule (Parts B–E: rules
shared).

| Glyph | Where | Mirrors in RTL |
|---|---|---|
| `circle-alert` | beside the field's error | no |
| `search` | the magnifier at the inline end | no |
| `check` | a chosen option, in the single picker, the multi-picker's rows (§E.2) and a cell's dropdown (§E.4) | no |

**Compatibility:** no public API changes; `./entity-picker` imports `@tellma/core-ui/icon`.
**Tests:** e2e geometry — the three glyphs in a field and in a cell's dropdown, in LTR and RTL.
**Budget:** within Part E's ceiling, re-measured.

### Part E — Testing

| Suite | Pins |
|---|---|
| Unit | two-line rows at the fixed height; label parts each in a `<bdi>`; the single picker's suite in emit mode; multi-picker toggle, `maxItems` and whole-set pages; the page context and `maxItems`; the grid column's `itemDescription`, `itemLabelParts` and `formatParts` |
| Playwright | add, toggle-remove, chip removal and Backspace, keyboard-only, in LTR and RTL; the field's height with 0, 1 and 30 chips; two-line rows in a field and in a cell; an option and a display cell whose Latin code sits beside an Arabic name, code first in LTR and RTL; §E.5's glyph geometry |
| axe | chips and an open panel, in both themes and directions |
| Gates | the `./entity-picker` and `./grid` goldens; the ceiling below |

### Part E — Definition of done

- **Rich options:** `itemDescription` and `itemLabelParts` on the picker and the grid column, and
  `formatParts` on the column, with the fixed two-line height (§E.1, §E.4); the new token in the
  preset; the identity server's token CSS refreshed.
- **Multi-picker:** `tm-entity-multi-picker` on spec 0023's `tm-chip` and `tm-chip-set` (§E.2),
  and its harness.
- **Page context:** §E.3 shipped.
- **Glyphs:** §E.5's three glyphs draw through `tm-icon`.
- **Showcase:** the `entity-picker` story gains two-line rows, a multi-picker, and an advanced
  search that adds and removes picks.
- **UI rules:**
  - **Instant feedback** — a pick shows its chip in the same frame (e2e).
  - **Non-blocking** — every keystroke still aborts the previous search (the single picker's suite,
    emit mode included).
  - **Stable layout** — the field's height is identical with 0, 1 and 30 chips (geometry e2e);
    glyph boxes hold (§E.5 e2e).
  - **RTL** — chip navigation keys mirror (e2e).
  - **WCAG AA** — axe with chips and an open panel in both themes and directions; keyboard-only
    e2e for add, toggle-remove, chip removal and Backspace.
- **Budgets:** `./entity-picker` 12 → **14**; note recorded.

### Part E — Decisions record

1. **Two-line rows share one fixed height, `null` descriptions included** — the panel never
   re-measures between queries (§E.1).
2. **The multi-picker composes the single picker in an internal `'emit'` commit mode** — the search
   state machine is not forked; a second state machine was the alternative (§E.2).
3. **Chips scroll in one fixed-height line, with a count badge, through `tm-chip-set`'s `scroll`
   mode** — the field never grows, so nothing below it moves, and the chip set's roving focus gives
   the row one tab stop; the `collapse` mode's "+N" chip was the alternative (§E.2).
4. **No multi-entity grid editor** — grid cells hold scalar ids (§E.2).
5. **`context` is opaque to the picker** — the binding and its page agree on the shape; a generic
   type parameter on the page form was the alternative (§E.3).
6. **Advanced search from a multi-picker returns the whole selection** — the page is a full editor
   of the set, removals included; an add-only result could not remove a chosen id (§E.2).
7. **Only an explicit list pick toggles a chosen row off** — auto-resolution and Tab act without a
   visible list, so they never remove a chip; a list or Enter commit keeps the panel open for the
   next pick (§E.2).

### Part E — Review flags

1. **The multi-picker composes the single picker through `ɵcommitMode`** (§E.2). Alternative: a
   separate state machine. Flips if emit mode destabilises the single picker's suite.
2. **Chips scroll in one line with a count badge** (§E.2). Alternative: wrapping chips that grow
   the field. Flips if users miss chosen items hidden by the scroll.

## Part F — Queryex (spec 0008)

Part F amends spec 0008, the Queryex compiler in `src/core/Tellma.Core.Queryex`. The data access
layer (spec 0012) and the permission evaluator (spec 0014) need changes to compose queries and
access filters (§F.1–§F.7), constant statements and the `'uq'` month table (§F.9, §F.11), and to
read the columns their schema omits (§F.16). The report engine (spec 0033) needs changes for
optional parameters, defaults, drill-through, Hijri grouping, sharing, parameter pickers, and
pivot and summary rollups (§F.8–§F.15), then a quarter truncation (§F.17), Ethiopic grouping
(§F.19), measure conditions for drill-through (§F.20) and a filter's conjuncts for the drill list's
chips (§F.21). Every host needs the stamp's clearer name
(§F.18) and current zone ids for the `core.TimeZone` value set (§F.22). Every item is additive
except §F.18's renames and §F.22's removed zone ids (§F.0).

### §F.0 Shared rules

**Target:** 0008 §14 and Appendix A (codes), §16 (caches), §17 (versions), §18 (testing), §19
(the inspection tool), §20 (the public surface). **Requested by:** every item below. **Why:** each
item then states only its own change.

**Placement and order**

- **The engine only.** Every change lands in `src/core/Tellma.Core.Queryex`, with no new project
  and no new package reference. The public surface of 0008 §20 grows by exactly the members this
  part's sketches add. Nothing is removed or retyped, except §F.18's renames and §F.22's removed
  zone ids.
- **Tests** grow in the existing projects: `test/core/Tellma.Core.Queryex.Tests`,
  `test/core/Tellma.Core.Queryex.IntegrationTests` and the shared harness
  `test/shared/Tellma.Queryex.Testing`. The inspection tool `eng/queryex-inspection` mirrors the
  new inputs.
- **Order.** §F.1–§F.4, §F.6–§F.9, §F.11, §F.14–§F.16, §F.18 and §F.22 ship first: specs 0012,
  0013 and 0014 cannot ship without them, and §F.18 names the members the other items add. §F.5
  needs no code. §F.13, §F.17, §F.19 and §F.21 ship before spec 0029, whose discover endpoint and
  date parts use them. §F.10, §F.12 and §F.20 ship before spec 0033. Part F waits on no other part.
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
- **Table calendar** — a calendar code whose arithmetic reads a host month table (`'uq'`).
- **Partial** — a `sum`, `count`, `min` or `max` column that a measure is computed from (§F.14).
- **Combine** — merging two partials of one kind: adding sums and counts, keeping the least or
  greatest `min` or `max`.

**Compatibility**

- **Additive.** With every new member at its default, the engine emits byte-identical SQL, slots
  and columns, except the paging slot names of §F.4. The existing goldens with paging are rewritten
  once with `TELLMA_UPDATE_GOLDENS=1` (0008 §18.2). Every other golden and corpus entry stands; none
  names a zone id §F.22 removes.
- **Version 1.** `QueryexLanguage.Version` and `Minimum` stay 1. No stored text changes meaning,
  so the change is additive under 0008 §17:
  - `level()` and `startOfQuarter()` are new functions;
  - `'uq'` resolves only under a month map, and `'et'` is the code 0008 §10.4 reserved for it;
  - `Measures` is a new clause, and with it empty the engine emits today's SQL;
  - §F.18 renames members, not language, and §F.22's removed ids name no stored text, since no
    host stores Queryex before Part F ships.
- **Records keep their constructors.** New members on `ParameterUse`, `CompiledQuery` and
  `DiscoveryResult` are `init` properties, never positional parameters.
- **Two new `FilterTree` node types**, `ViaNode` and `ExistsNode`, are sealed nested types like
  the existing four. A host that pattern-matches trees adds two cases.

**Cache keys.** One rule extends 0008 §16: every new input joins the key of each layer whose output
it changes. The input is held as text or structure, never hashed.

- **L2** gains a leaf's own Queryex version, and the omitted names a text refers to.
- **L3** gains the leaf's version too. It also gains restrictions, the presence of paging (never
  its values), `Via` and `Exists` nodes, the omitted set, the `Measures` text and the undeclared
  columns in order.
- **A constant statement** keys on its items, types and options.
- **Calendar maps** add nothing: they live in the schema, whose identity already keys every layer.

**Diagnostic codes.** Appendix A gains three codes:

| Code | Condition | Arguments | Where |
|---|---|---|---|
| QX4009 | Paths are not permitted in this position | `name` (the first path) | §F.9 |
| QX4010 | This cannot be computed over rolled-up values | `construct`: the operator or function name, or `text comparison` | §F.15 |
| QX4011 | A measure must aggregate: a path outside an aggregation, or no aggregation at all | — | §F.14 |

- **QX2009 widens** from "A predicate is a single expression; list not permitted" to "A single
  expression is required here; list not permitted". It also covers a constant item (§F.9).
- **Evaluation codes.** QX6001 (division by zero) and QX6002 (arithmetic overflow) are the `Code`
  of `QueryexEvaluationException` (§F.15). They are never diagnostics.
- **New locations.** `…Via[<navigation>]` and `…Exists[<entity>]` inside a filter tree, nesting as
  the tree does: `Filter.Or[2].Exists[core.ReportShare].Or[0]`. `Constant[<i>]` names a
  constant item, and `Measures[<i>]` a measure.

**Reference interpreter and corpus**

- **The reference interpreter** (0008 §18.3) implements every item from this part's text,
  independently of the emitter: restrictions, `level`, `Via`, omitted folding, constants, `'uq'`
  (through .NET `UmAlQuraCalendar`), `'et'` (through its own day count, §F.19), `Exists`, measures
  and undeclared columns. It evaluates each
  measure directly over a group's rows, never through partials, and orders text by its own
  weights. The differential suite runs every new corpus entry on SQL Server and on the interpreter.
- **`LedgerFixture` grows:**
  - a child table with a parent navigation, for `Exists`;
  - a `uq` month map seeded from `UmAlQuraCalendar` by spec 0013 §8.2's rule;
  - nullable numeric columns of every width, `tinyint` to `decimal(38,10)`, with values near each
    type's bounds and zero denominators; `decimal(38,10)` values stay within 28 significant digits,
    what `System.Decimal` carries, and one vector shows a 38-digit value failing in the reader;
  - text rows that tie under the fixture's case-insensitive collation;
  - `hierarchyid` nodes whose paths sort differently as text, such as `/1/2/` and `/1/10/`;
  - an `nvarchar(max)` column its schema does not declare, for §F.16.
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
- **Scope.** The root statement only, never inside `Exists` (§F.12). In an aggregate query it
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

**Target:** 0008 §10.1 (the registry), §10.10. **Requested by:** spec 0012 §10.1. **Why:** the
depth of a tree node, with no stored `Level` column.

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
**Why:** a stored criterion was validated under the Queryex version stamped beside it, so each
leaf of a composed tree binds under its own version.

```csharp
public abstract class FilterTree
{
    public static FilterTree Leaf(string text, int queryexVersion); // new; ArgumentOutOfRangeException unless QueryexLanguage.IsSupported
    public sealed class LeafNode : FilterTree
    {
        public int? QueryexVersion { get; }                          // new; null for the one-argument form
    }
}
```

- **Binding.** A leaf binds under `QueryexVersion ?? QueryCompilationOptions.QueryexVersion`; in
  discovery, `?? DiscoveryOptions.QueryexVersion` (§F.18 names both).

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
- **Binding.** Each segment resolves to a foreign-key navigation of the current root. `inner`
  binds with the path's target as its current root. Context functions and parameters bind as
  anywhere else.
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

### §F.8 Omitted parameters

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
  3. Absorption follows, on the syntax tree, to a fixed point: `x and true` becomes `x`, `x and
     false` becomes `false`, `x or true` becomes `true`, `x or false` becomes `x`, and `not true`
     and `not false` become `false` and `true`.

  `CompileQuery`, `BindParameters` (§F.10) and `MeasureCondition` (§F.20) apply the same three
  steps. Lowering's nullity folding (0008 §8.4) still runs afterwards.
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
absorption through `and`, `or` and `not` to a fixed point; polarity through `FilterTree.Not` and
nested `not`; list dropping in `in` and `descendantOf`; folding inside `Having` and inside a select
measure's condition; the typed absent value in a select item; the caller error. Differential: the
interpreter applies this section's rule with its own code.

### §F.9 Constant statements — `EvaluateConstant`

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
    public required int QueryexVersion { get; init; }
    public bool HasUser { get; init; } = true;                     // false: me() is absent
    public int BatchOrdinal { get; init; }                         // the {b} of the @qx{b}_ slot names
    public QueryexLimits Limits { get; init; } = QueryexLimits.Default;
}
```

- **Items.** One to `Limits.MaxListItems` items; any other count is a caller error. Each item binds
  in `Value` mode under `QueryexVersion` and checks against its `Type`. A string literal coerces
  (0008 §7.3); any other mismatch is QX3200.
- **Diagnostics.** A path is QX4009; an aggregation QX4002; a parameter QX3007, since a constant
  statement declares none; a list QX2009. Each carries the location `Constant[<i>]` and a span
  into the item's text.
- **Emission.** `SELECT <e0> AS [c0], <e1> AS [c1], …` with no `FROM`: always one row. An operand
  that a guard would repeat uses 0008 §12.3's single-emission form, because there is no row source
  for a value binding. Nothing hoists, so `Prologue` is empty and a `'uq'` call takes §F.11's
  scalar form.
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

### §F.10 Parameter binding — `BindParameters`

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
    public required QueryexMode Mode { get; init; }                               // where §F.8 folds: the skeleton in predicate modes; aggregation conditions in every mode
    public required int QueryexVersion { get; init; }
    public IReadOnlyList<QueryexParameterDeclaration> Parameters { get; init; } = [];
    public IReadOnlyDictionary<string, object?> Values { get; init; }             // by name, ignoring case; the declared type's CLR value; default empty
    public IReadOnlyCollection<string> Omitted { get; init; } = [];               // folded by §F.8's rule
    public QueryexLimits Limits { get; init; } = QueryexLimits.Default;
}
```

- **The result** is canonical text from 0008 §12.1's printer. Parsing it gives the source's parse
  with two changes: each valued parameter is replaced by a literal, and §F.8's folding is applied
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

  A folded atom absorbs into its `and`, `or` or `not` (§F.8 step 3); a text that folds whole
  prints as `true` or `false`.
- **Caller errors:** a valued or omitted name that is not declared; a name both valued and omitted;
  a value whose CLR type does not match the declared type.

**Example —** `Invoice.PostingDate >= @From and Invoice.PostingDate <= @To`, mode `Filter`, `From =
2026-01-01`, `To` omitted → `Invoice.PostingDate >= cast('2026-01-01', 'date')`: the omitted atom
folds to `true` and is absorbed.

**Compatibility:** a new entry point. **Tests:** a vector per literal type and per §F.8 table row;
a property test that binding then compiling equals compiling with the arguments (differential on
the fixture); the printer round trip `parse(print(t)) = t`.

### §F.11 `'uq'` through a host month table

**Target:** 0008 §3, §10.4, §12.3, §12.4, §13.4. **Requested by:** spec 0033 §8.7; spec 0012
§11.1 (maps it); spec 0013 §8.2 (owns the table). **Why:** Acme reports by Umm al-Qura month.
0008 §10.4 reserved `'uq'` for a host-provided month table named through the schema.

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
  and of `startOfQuarter` (§F.17) when the schema maps it. Otherwise it stays QX3101.
- **Semantics** follow .NET `UmAlQuraCalendar`:
  - `year`, `month` and `day` of the Hijri date; `quarter` = `(month + 2) / 3`; `week` =
    `(dayOfYear − 1) / 7 + 1`;
  - `startOfYear`, `startOfQuarter` and `startOfMonth` give the Gregorian date of the Hijri
    boundary;
  - `addYears` and `addMonths` keep the time of day, clamp to the target month's last day, and
    truncate `n` as `'gc'` does;
  - `diffYears` and `diffMonths` count completed units by 0008 §10.4's rule;
  - a date outside the map, or a result beyond it, is absent.
- **Nullity.** Every `'uq'` result is `Nullable`, through a registry rule: "`Nullable` when the
  calendar argument selects a table calendar, else the signature's rule".
- **Three emission forms.** The call's operands choose the form. The operands are its dates and,
  for `addYears` and `addMonths`, `n`.

  | Form | The call's operands | Emission | Example |
  |---|---|---|---|
  | Row grain | An operand reads a path, and none contains an aggregation | the lateral lookup below, in every clause | `month(Invoice.PostingDate, 'uq')` |
  | Path-free | No operand reads a path or contains an aggregation | a hoisted variable (below), read in every clause | `startOfMonth(today(), 'uq')`, `month(@AsOf, 'uq')` |
  | Aggregated | An operand contains an aggregation | the scalar form below, in place | `month(max(PostingDate), 'uq')` |

- **Hoisted variable.** A path-free call is row-invariant, so it is hoisted into the prologue
  (0008 §12.4), like the hierarchy node lookups: `DECLARE @qx{b}_v{n} <type> = (<scalar form>);`.
  Each distinct call gets one variable per statement, by structural equality (0008 §11.3). Every
  use reads the variable.
- **Where an aggregated call sits.** It can only sit in a grouped statement's `SELECT`, `HAVING`
  or `ORDER BY`, where SQL Server allows a subquery: aggregations never nest (QX4003), and a
  grouping key holds none.
- **No position refuses a `'uq'` call.** SQL Server refuses a subquery in `GROUP BY` (error 144)
  and inside an aggregate (error 130), never a variable or a derived-table column. The lateral and
  hoisted forms cover every row-grain position.
- **Lateral lookup.** Each distinct date operand of a row-grain call gets one lookup per
  statement, by structural equality (0008 §11.3). The operand is made atomic first, so the seek
  stays sargable: a `Date` column as is; anything else through a value binding (0008 §12.3), a
  `DateTime` as `CAST(<d> AS date)`. The lookup is a one-row backward seek on (`Calendar`,
  `StartDate`):

  ```sql
  OUTER APPLY (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM <source> AS [M]
               WHERE [M].[Calendar] = <slot> AND [M].[StartDate] <= <d> AND <d> < [M].[EndDate]
               ORDER BY [M].[StartDate] DESC) AS [C{n}]
  ```

- **Lateral results** read `[C{n}].[Month]` and the like.
- **Scalar form.** The same `SELECT TOP (1) [M].[<column>] … ORDER BY [M].[StartDate] DESC` as a
  parenthesized subquery. It has three uses:
  - in place for an aggregated call, one per use;
  - in a constant statement (§F.9);
  - as a hoisted variable's initializer.

  The operand enters it once (0008 §12.3): a parameter or a column as is, anything else through a
  value binding inside the subquery. A second lookup nests one more subquery.
- **Second lookups.** A function that needs another month's row adds an equality lookup on
  (`Calendar`, `Year`, `Month`):
  - the year's first month, for `startOfYear` and `week`;
  - the quarter's first month, for `startOfQuarter`;
  - the target month, for `addYears`, `addMonths` and the completed-unit test of `diffYears` and
    `diffMonths`.
- **Order and scope.** Lateral sources, bindings and lookups alike, emit in creation order, which
  respects their dependencies. The calendar code reaches SQL as a literal slot. An `Exists`
  subquery (§F.12) carries its own lookups.

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

**Example —** a hoisted call, sales of the current Hijri month per center: `Select = "Center.Id,
sum(Amount, Invoice.PostingDate >= startOfMonth(today(), 'uq'))"`, aggregate:

```sql
DECLARE @qx0_v0 date = (SELECT TOP (1) [M].[StartDate] FROM [core].[CalendarMonths] AS [M]
                        WHERE [M].[Calendar] = @qx0_p1 AND [M].[StartDate] <= @qx0_p0 AND @qx0_p0 < [M].[EndDate]
                        ORDER BY [M].[StartDate] DESC);

SELECT [P1].[Id] AS [c0], SUM(CASE WHEN @qx0_v0 IS NOT NULL AND [P2].[PostingDate] >= @qx0_v0 THEN [T].[Amount] END) AS [c1]
FROM [acme].[SalesInvoiceLines] AS [T]
LEFT JOIN [gl].[Centers] AS [P1] ON [P1].[Id] = [T].[CenterId]
INNER JOIN [acme].[SalesInvoices] AS [P2] ON [P2].[Id] = [T].[InvoiceId]
GROUP BY [P1].[Id];
```

`@qx0_p0` is the `Today` slot and `@qx0_p1` the calendar code.

**Compatibility:** a schema without the map keeps QX3101 for `'uq'`. **Tests:**

- differential for each function over every day from 1900-04-30 to 2077-11-16 and beyond both
  ends, against `UmAlQuraCalendar` in the interpreter;
- lateral goldens for a `Date` operand, a `DateTime` operand, a second lookup, a grouping key over
  a `'uq'` key, and a call that mixes a row-grain and a path-free operand;
- hoisted goldens for a path-free call in `WHERE`, `HAVING`, an aggregation's condition and a
  grouping key, each also run on SQL Server to show that no position needs a diagnostic; a call
  used twice declares one variable;
- scalar-form goldens for an aggregated operand in `SELECT` and `HAVING`, and for a constant
  statement;
- QX3101 on a schema without the map.

### §F.12 `FilterTree.Exists`

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
- **Caller errors:** an unknown entity; a navigation that is missing or targets an entity other
  than the current root; an `Exists` in a `Having` tree. Diagnostics inside carry
  `…Exists[<entity>]` locations.

**Example —** from `core.Report`, `Exists("core.ReportShare", "Report", Leaf("UserId = me()"))`:

```sql
WHERE EXISTS (SELECT 1 FROM [core].[ReportShares] AS [X1]
              WHERE [X1].[ReportId] = [T].[Id] AND ([X1].[UserId] IS NOT NULL AND [X1].[UserId] = @qx0_p0))
```

Spec 0033 §5.3 shows the full sharing criterion, with a `Via` and a nested `Exists` inside.

**Compatibility:** a tree without an `ExistsNode` compiles as today. **Tests:** goldens (plain,
nested, inside `Via`, `Via` inside, under `Or` and `Not`); differential over the fixture's child
table; one SQL text for two users; alias uniqueness across nested subqueries; the caller errors;
`MaxJoins` counting.

### §F.13 `ParameterUse.ComparedWith`

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

### §F.14 `QuerySpec.Measures` and partial columns

**Target:** 0008 §2.1, §2.4, §11.3, §13.4. **Requested by:** spec 0033 §8.4, §8.5; spec 0029 §9.
**Why:** a pivot rolls up, and a summary totals, in C#. That needs each measure's aggregate parts
as columns, and its outer expression in a form C# can run (§F.15).

```csharp
public sealed record QuerySpec
{
    public string? Measures { get; init; }      // new; comma-separated measures on the Group axis; requires Aggregate
}

public sealed record QueryDiscoverySpec
{
    public string? Measures { get; init; }      // new; checked as CompileQuery checks it, QX4010 and QX4011 included
}

public sealed record CompiledQuery
{
    public CompiledMeasures? Measures { get; init; }   // new; null when QuerySpec.Measures is empty (§F.15)
}

public sealed record MeasurePartial(
    int Ordinal,                    // the result column: a reused select item, or an appended one
    MeasurePartialKind Kind,
    QueryexType Type);              // the aggregate's type; Numeric for sum and count

public enum MeasurePartialKind { Sum, Count, Min, Max }
```

- **Binding.** Each item binds in Aggregate mode, like a select measure, at location
  `Measures[<i>]`.
  - An item that reads a path outside an aggregation, or has no aggregation, is QX4011. It
    replaces QX4007 here, so the two never fire on one node.
  - Outside its aggregations, an item may use only §F.15's subset; anything else is QX4010 at the
    node's span.
  - Measures never become grouping keys: the keys still come from the select list alone (0008
    §11.3).
- **Partials.** Each aggregate call is a leaf of its measure: its arguments are never decomposed,
  so `sum(Amount - Cost)` is one partial.
  - `count`, `sum`, `min` and `max` are each one partial. `avg(x[, c])` splits into `sum(x[, c])`
    and `count(x[, c])`.
  - The list is closed: any other aggregate is QX4010, so a non-decomposable one is refused until
    it is designed.
  - `min` and `max` take any Ordered type (0008 §7.1). `Guid` is not Ordered, so no `Guid`
    partial exists.
  - Structurally equal partials (0008 §11.3) are one partial, in first-use order across the items.
  - A partial structurally equal to a select item reuses that column.
- **Columns.** The appended partials follow the select items.
- **Other clauses are unchanged.** `Having`, `OrderBy` and paging run in SQL over the statement's
  rows, as without `Measures`. Omitted parameters (§F.8) fold inside a partial's condition as in
  any aggregation.
- **Discovery.** `DiscoverQuery` binds `Measures` as `CompileQuery` does and reports its
  parameters, paths and diagnostics at `Measures[<i>]` sites.
- **Caches.** Each item caches in L2 like a select item; L3 keys on the `Measures` text (§F.0).
  `CompiledQuery.Measures` is cached with the plan.
- **Caller error:** `Measures` without `Aggregate`, in `QuerySpec` and `QueryDiscoverySpec` alike.

**Example —** a pivot's leaf statement over `acme.SalesInvoiceLine` by `Center`. For this example
the line also has `Cost` and `Discount`, both `decimal(19,4)`. The spec is:

- `Select = "Center.Id"`;
- `Measures = "sum(Amount), sum(Amount - Cost) / sum(Amount), avg(Discount)"`;
- `OrderBy = "Center.Id"` and `Take = MaxPivotRows + 1` (spec 0033 §8.5).

```sql
SELECT [P1].[Id] AS [c0], SUM([T].[Amount]) AS [c1], SUM([T].[Amount] - [T].[Cost]) AS [c2],
       SUM([T].[Discount]) AS [c3], COUNT_BIG([T].[Discount]) AS [c4]
FROM [acme].[SalesInvoiceLines] AS [T]
LEFT JOIN [gl].[Centers] AS [P1] ON [P1].[Id] = [T].[CenterId]
GROUP BY [P1].[Id]
ORDER BY [c0] ASC OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY;
```

The three measures need four partials: `sum(Amount)` serves two of them and is one column. With
`Select = "Center.Id, sum(Amount)"`, `sum(Amount)` reuses `[c1]` and three partial columns are
appended.

**Compatibility:** with `Measures` empty, the engine emits today's bytes, and
`CompiledQuery.Measures` is null. **Tests:**

- corpus: QX4011, not QX4007, for a stray path, and QX4011 for a measure with no aggregation;
  QX4010 for each refused row of §F.15's table, with span and `construct`; identity `cast`s
  accepted; discovery sites; the caller error;
- goldens: the `avg` split, deduplication, select-item reuse, conditional partials and folding
  inside a partial's condition.

### §F.15 Measure evaluation in C#

**Target:** 0008 §1.4, §9. **Requested by:** spec 0033 §8.4, §8.6, §8.11. **Why:** a subtotal of
`sum(a) / sum(b)` is the outer expression over rolled-up partials, never a sum of the children's
ratios. One owner keeps both sides in step: the emitter writes SQL Server's arithmetic, and the
evaluator reproduces it.

```csharp
public sealed class CompiledMeasures                          // immutable and thread-safe; cached with the plan
{
    public IReadOnlyList<MeasurePartial> Partials { get; }   // deduplicated across all measures (§F.14)
    public IReadOnlyList<CompiledMeasure> Items { get; }     // parallel to the Measures items
    public MeasureEvaluator Prepare(MeasureContext context);  // once per run: fixes every partial's and node's SQL type
}

public sealed record CompiledMeasure(string Text, QueryexType Type, QueryexNullity Nullity);

public sealed record MeasureContext(
    IReadOnlyList<QueryexNumericType?> DecimalTypes,          // parallel to Partials: the reader's precision and scale for a decimal partial; null otherwise
    IReadOnlyDictionary<string, object?> Arguments,           // declared parameters by name, ignoring case; an omitted one is absent
    DateOnly Today, DateTimeOffset Now, object? UserId);      // the values the statement's context slots were bound with

public readonly record struct QueryexNumericType(byte Precision, byte Scale);

public sealed class MeasureEvaluator                          // one run's prepared measures; immutable once prepared
{
    public IReadOnlyList<QueryexNumericType?> ResultTypes { get; } // parallel to Items: a Numeric measure's SQL type
    public PartialTable CreateTable(int capacity);            // capacity: the expected cell count
    public MeasureValue Evaluate(PartialTable table, int cell, int measure);
}

public sealed class PartialTable                              // columnar: one typed array per partial, one slot per cell; not thread-safe
{
    public int AddCell();                                     // an empty cell: sums and min/max null, counts 0
    public void Add(int cell, int partial, Array buffer, int row); // one row's non-null value from a reader buffer
    public void Merge(int target, int source);                // the combine, partial by partial
}

public readonly struct MeasureValue                           // typed: no boxing
{
    public QueryexType Type { get; }
    public bool IsNull { get; }
    public SqlDecimal Number { get; }                         // Numeric, at the measure's ResultType
    public bool Boolean { get; }
    public DateOnly Date { get; }
    public DateTime DateTime { get; }
    public DateTimeOffset DateTimeOffset { get; }
    public string? Text { get; }                              // String, and a HierarchyId's path
}

public sealed class QueryexEvaluationException : Exception
{
    public string Code { get; }                               // QX6001 division by zero; QX6002 arithmetic overflow
}
```

**The subset.** Outside its aggregations, a `Measures` item may use only what the evaluator can
compute exactly. A measure written as a select item is evaluated by SQL and keeps the whole
language.

| Construct | Status |
|---|---|
| number, `null`, `true` and `false` literals; parameters; `today()`, `now()`, `me()` | supported |
| a string literal | supported as a value (`if(c, 'High', 'Low')`), or as a date when it coerces (0008 §7.3) |
| unary `-`; `+`, `-`, `*`, `/` | supported, at SQL Server's types (below) |
| `=`, `!=`, `<`, `<=`, `>`, `>=` and `in` over Numeric, Bool, Date, DateTime, DateTimeOffset | supported |
| `is null`, `is not null`; `and`, `or`, `not`; `if`, `coalesce` | supported, any type |
| `abs`, `round`, `floor`, `ceiling` | supported |
| `cast` to its operand's own type; `cast(null, …)` | supported: the identity (0008 §10.8) |
| `year`, `quarter`, `month`, `day` over `Date` in calendar `'gc'`; `diffDays`, `diffHours`, `diffMinutes`, `diffSeconds` over `Date` | supported |
| `%` | QX4010: no reporting use, and a type rule of its own |
| `\|\|` | QX4010: building text belongs in dimensions and attributes |
| comparisons and `in` over String, Guid or HierarchyId | QX4010 (`text comparison`): text equality depends on the collation, which C# only approximates |
| any other `cast` | QX4010: text rendering and parsing rules |
| every other date function; `'uq'`; `local`; date functions over `DateTime` or `DateTimeOffset` | QX4010: they need the tenant zone, the month table or SQL Server's boundary counting |
| text functions | QX4010: collation-dependent |

`descendantOf`, `ancestorOf` and `level` need a path, so outside an aggregation they are QX4011.

**Example —** `diffDays(min(PostingDate), max(PostingDate))` over a `Date` column is supported.
`diffHours(min(PostedAt), max(PostedAt))` over a `DateTimeOffset` column is QX4010 on `diffHours`;
the author shows `min(PostedAt)` and `max(PostedAt)` as two measures instead.

**Types.** Every number is a `SqlDecimal` at the SQL type SQL Server gives that node, so each
operation lands on SQL Server's type and digits. `Prepare` fixes the types once per run:

| Node | SQL type |
|---|---|
| `sum` partial | `decimal(38, s)`, the argument's scale; an integer argument widens to `decimal(38, 0)` (0008 §10.2) |
| `count` partial | `bigint` |
| `min` or `max` partial | a whole argument's own type (`tinyint` … `bigint`); a decimal's precision and scale from `DecimalTypes` |
| `avg` | `sum ÷ count`, which lands on `decimal(38, max(s, 6))`, SQL's `AVG` type |
| number literal | `decimal(digits, written scale)`, as the engine binds it (0008 §13.1) |
| numeric parameter | `new SqlDecimal(value)`, which equals SqlClient's inference |
| `me()` | the `UserId` slot's store type |
| `year`, `quarter`, `month`, `day` | `int`, as the emitter types them |
| `diffDays`, `diffHours`, `diffMinutes` | `decimal(26, 6)`, as the emitter types them |
| `diffSeconds` | `bigint`, as the emitter types it |
| whole `+`, `-`, `*` whole; unary `-`; `abs` | the wider whole type, overflowing at that width (negating the minimum included) |
| whole `/` whole | the dividend widens to `decimal(19, 0)`, as the emitter does (0008 §9.1); integer division never happens |
| whole with decimal | the whole side becomes `decimal(3, 0)`, `(5, 0)`, `(10, 0)` or `(19, 0)` by width |
| decimal with decimal | `SqlDecimal`'s operators |
| `if`, `coalesce` | two whole types: the wider. Otherwise the union: scale `max(s)`, integer digits `max(p − s)`; past 38 digits the scale shrinks, with no floor |
| `abs`, `round` | the argument's type |
| `floor`, `ceiling` | `decimal(p, 0)`, or the same whole type |

- **Whole partials are typed from the plan.** Precision alone cannot tell `int` from
  `decimal(10,0)`, and SQL Server treats them differently: `int` 50,000 × 50,000 overflows, while
  `decimal(10,0)` gives `decimal(21,0)`. A decimal partial's precision and scale come from the
  reader (spec 0012 §3.3's `QueryColumn.Precision` and `Scale`), since SQL Server derives a
  computed argument's facets.
- **Scale collapse.** Where `+`, `-`, `if` or `coalesce` combine a `decimal(38, s)` operand with one
  of larger scale, 38 digits leave no room. SQL Server keeps scale s, and the evaluator does the
  same:
  - `sum(Qty) + sum(Amount)` is whole when `Qty` is an integer;
  - `(38,2) + (38,4)` keeps 2 decimals;
  - to keep the decimals, scale the coarser operand first: `sum(Qty) * 1.0000 + sum(Amount)` is
    `decimal(38, 4)`.
- **Rounding.** `SqlDecimal` matches SQL Server on each rule:
  - division truncates: `decimal(38,4)` 2 / 3 is 0.666666;
  - multiplication rounds;
  - `round` rounds half away from zero;
  - an `if` that narrows a scale rounds.
- **The sign defect.** `SqlDecimal` drops the sign of a negative product that rounds to one unit of
  the last place (−0.0015 × 0.0005 gives +0.000001). The evaluator multiplies magnitudes and
  applies the sign itself.
- **38 digits.** Evaluation runs at 38 digits, as SQL does. Inputs arrive as `System.Decimal`
  through spec 0012's `QueryRowSet`: a value too large for it fails in the reader for every query,
  and every value that arrives is exact.

**Example —** `if(c, sum(a) / sum(b), sum(a))` with `decimal(38,4)` partials: the branches are
`decimal(38,6)` and `decimal(38,4)`, so the union keeps 34 integer digits and 4 decimals. SQL
rounds the ratio to 4 places, and so does the evaluator.

**Accumulation** (`Add` and `Merge`):

- **Storage.** One typed array per partial, one slot per cell: `Int128` mantissas for sums, `long`
  counts, the column's own type for `min` and `max`. Nothing boxes.
- **Inputs** take the partial's SQL type on arrival. The reader delivers `System.Decimal` at the
  value's own scale (`1.5000m` is `(5,4)`, `0m` is `(1,0)`), so `Add` rescales it to the
  partial's (p, s) first. `buffer` is one of the reader's typed column arrays (spec 0012 §3.3).
  The engine references no data library.
- **Sums** add exactly at the partial's scale, as a checked 128-bit mantissa. A sum past 38 digits
  is QX6002, where SQL Server raises 8115.
- **Counts** add as a checked `long`, like `COUNT_BIG`.
- **`min` and `max`** compare numbers by value at any scale, dates by value, `DateTimeOffset` by
  instant and then offset, and `Bool` with false first.
  - **Text** compares with `StringComparer.InvariantCultureIgnoreCase`. On ordinary business text
    it agrees with the platform collation (spec 0011 §6.7). It can differ on awkward pairs, such as
    hyphens, apostrophes and symbols. Values it calls equal ('Sales' and 'SALES') keep the
    ordinally smaller, so the output never depends on row order.
  - **Hierarchy paths** compare level by level, each level as an integer, with an ancestor before
    its descendants: `/1/` < `/1/2/` < `/1/10/` < `/2/`. This is SQL Server's `hierarchyid` order
    for the id paths that every tree stores (spec 0012 §10.1).
- **A bare measure** such as `sum(Amount)` is its partial and skips `SqlDecimal`.

**Nulls.** 0008 §9 applies unchanged.

- **Partials.** A `sum`, `min` or `max` partial is null when no contributing row has a value. A
  `count` is never null. `avg` is null when its count is 0.
- **Operators.** Arithmetic propagates null. Comparisons are total (0008 §9.2): `null = null` is
  true, and an ordering against null is false. `and`, `or` and `not` stay two-valued.
- **Laziness.** `if` and `coalesce` run only the chosen branch, as SQL's `CASE` does after
  aggregation. They are the portable guard: `if(sum(b) = 0, null, sum(a) / sum(b))`.
- **`and` and `or` in C#** stop at the first operand that decides, left to right. SQL Server fixes
  no operand order, so a guard written with them holds in C# only:
  `if(sum(b) != 0 and sum(a) / sum(b) > 1, 'High', 'Low')` can raise 8134 in SQL.

**Errors.**

- **Codes.** `Evaluate` throws `QueryexEvaluationException` with QX6001 for a division by zero and
  QX6002 for an overflow: a result past 38 digits, a whole result past its type, or a sum past 38
  digits. These are the cases where SQL Server raises 8134 or 8115 (0008 §9.1).
- **The run fails,** as a SQL statement fails when one group divides by zero. The caller reports
  it: spec 0033 §8.11 answers `422 Report.MeasureFailed`.
- **One edge differs.** An overflow inside a pivot measure (`max(Qty) * max(Qty)` past the `int`
  range) fails only in C#, since SQL never evaluates that measure.
- **Session settings.** SQL Server raises 8134 and 8115 only under its default `ANSI_WARNINGS` and
  `ARITHABORT`. Keeping them is already a host obligation (0008 §13.6).

**Lifetimes.**

- **`CompiledMeasures`** is cached with the plan.
- **A `MeasureEvaluator`** serves one run, because the decimal types come from that run's reader.
  One plan can meet two decimal types, such as a computed `sum(Amount * Rate)` whose scale
  differs by tenant.
- **A `PartialTable`** serves one cube, one totals row or one export.
- **No IR leaks.** The engine lowers each measure to an internal tree. Callers see
  `CompiledMeasure` and the opaque `MeasureEvaluator`, never the nodes.

**Example —** the rollup of §F.14's leaf rows. Head office (10) has facts of its own; its children
Riyadh (11) and Jeddah (12) have the rest.

| Leaf row | `c1` sum(Amount) | `c2` sum(Amount − Cost) | `c3` sum(Discount) | `c4` count(Discount) |
|---|---|---|---|---|
| 10 | 2,000.0000 | 500.0000 | 0.0500 | 1 |
| 11 | 30,000.0000 | 9,000.0000 | 0.3000 | 4 |
| 12 | 20,000.0000 | 4,000.0000 | 0.1000 | 2 |

The caller loads each leaf row into its own cell, merges the three into Head office's cell, and
evaluates every cell. The sums are `decimal(38,4)` and the count `bigint`; both divisions give
`decimal(38,6)`, truncated as SQL does:

| Cell | Sales | Margin | Average discount |
|---|---|---|---|
| Head office | 52,000.0000 | 13,500 / 52,000 = 0.259615 | 0.4500 / 7 = 0.064285 |
| — Head office (direct) | 2,000.0000 | 0.250000 | 0.050000 |
| — Riyadh | 30,000.0000 | 0.300000 | 0.075000 |
| — Jeddah | 20,000.0000 | 0.200000 | 0.050000 |

Averaging the margins would give 0.25, and averaging the averages 0.058333. Head office's cell
equals SQL's own answer with no `GROUP BY`, because SQL's `AVG` truncates exactly as the evaluator
does.

**Compatibility:** new types only; nothing existing changes. **Tests,** unit:

- **Types,** with value and (p, s), for every row of the types table and each rounding rule:
  - whole families: `int` 5 + 3 is `int`, `decimal(10,0)` 5 + 3 is `(11,0)`; `int` 50,000 ×
    50,000 and `tinyint` 200 + 100 are QX6002, `decimal(10,0)` 50,000 × 50,000 is `(21,0)`; `if`
    over `int` and `bigint` is `bigint`; negating and `abs` of `int`'s minimum are QX6002;
  - scale collapse: `(38,4)` 1.1234 + `(38,0)` 1 is 2 `(38,0)`; `(38,0)` 1 + `(38,10)` 0.5 is 2;
    `if` and `coalesce` over `(38,0)` and `(38,4)` are `(38,0)`; `(38,2)` + `(38,4)` is `(38,2)`;
    `(38,6)` − `(38,4)` is `(38,4)`; `sum(Qty) * 1.0000 + sum(Amount)` is `(38,4)`;
  - literal scales: `(38,4)` × 0.5 is `(38,5)`, × 1.25 is `(38,6)`; with `(38,4)` a = 1 and b = 3,
    `(a / b) * 100` is 33.333300 and `100 * a / b` is 33.333333;
  - dates: `diffDays` and `diffSeconds` across a leap day and a year end; `year` … `day` of `Date`
    partials.
- **The sign defect's scope:** negative products that round to one unit, with a decimal and with a
  `(3,0)` multiplier; control vectors for rounding, `ConvertToPrecScale`, division and `Ceiling`,
  which keep the sign.
- **Reader-shaped inputs:** `1.5000m` and `0m` loaded into `(38,4)` partials give `(38,6)` on
  division, not the `(15,10)` of their own types.
- **Laziness:** a guarded ratio over a zero denominator is null, and unguarded it is QX6001;
  `sum(b) != 0 and sum(a) / sum(b) > 1` is false, not QX6001.
- **Accumulation:** `Add` and `Merge` per kind and type; text compared ignoring case, with ties
  keeping the ordinally smallest value; the path order above; the 38-digit sum boundary and two near-maximum partials are QX6002; shuffled rows give
  identical results.
- **Preparation per run:** one `CompiledMeasures` prepared with two decimal types for one partial
  gives two `ResultTypes`.

**Tests,** differential on SQL Server: hand-written measures plus 500 generated ones (seeded,
depth ≤ 4, the subset's constructs only). Each case compiles one statement whose `Select` holds
the measure (SQL's value) and whose `Measures` holds it too (the partials). Three identities hold
exactly, in value and scale:

1. **Leaf.** Per group, evaluating the row's partials equals SQL's value in the same row.
2. **Rollup.** Combining the fine groups by a coarser key, then evaluating, equals SQL's value
   grouped by that key.
3. **Grand total.** Combining every row equals SQL's value with no `GROUP BY`.

A zero denominator raises SQL error 8134 and QX6001 alike. The generator guards a division only
with `if` or `coalesce` (**Nulls** above). Text `min` and `max` take part only over plain text
(letters, digits and spaces) and match ignoring case, because the comparer only approximates the
collation.

### §F.16 `QuerySpec.UndeclaredColumns`

**Target:** 0008 §2.1 (`QuerySpec`, `QueryexColumn`), §3 (the injection boundary), §13.4.
**Requested by:** spec 0012 §11.4, §11.5. **Why:** a host reads the root columns its schema omits
by design (opaque JSON text) in the statement that reads the rest of the row, without editing
compiled SQL.

```csharp
public sealed record QuerySpec
{
    public IReadOnlyList<string> UndeclaredColumns { get; init; } = []; // new; physical columns of Root's Source, read after Select
}
```

- **Each name** is an unquoted physical column of the root's `Source`, like
  `PropertyDescriptor.Column` (0008 §3), matching `[A-Za-z_][A-Za-z0-9_]*`. The engine does not
  look it up: the schema omits the column on purpose, so its existence and its character type are
  the host's obligation. It is the fourth host-authored text to reach SQL (0008 §3), after
  `TableSource` (§F.1).
- **Emission.** Each name is one more select item after the compiled ones, in list order, on the
  root alias (0008 §12.2): `[T].[<name>]`. `Body` carries them (§F.3).
- **Columns.** Each gains a `QueryexColumn` after the compiled ones: `Text` the name, `Type`
  `QxString`, `Nullity` `Nullable`, `Path` null, `IsGroupingKey` false.
- **Nothing reads them.** No clause can name an undeclared column, so the joins, slots, ordering
  and paging tiebreakers are those of the same spec without the list.
- **Row grain only.** An undeclared column is neither a grouping key nor a measure (0008 §11.3),
  so the list requires `Aggregate` unset. `Measures` requires `Aggregate` (§F.14), so it never
  meets the list.
- **Discovery.** `QueryDiscoverySpec` gains nothing: the list holds no text, and the host knows
  the names from its model.
- **Caller errors:** a name not of that form, or a duplicate ignoring case; a non-empty list with
  `Aggregate`.

**Example —** a details read of a line. For this example the line also has `ExtrasJson`, an
`nvarchar(max)` column the schema does not declare. With `Select = "Id, Amount,
Invoice.PostingDate"` and `UndeclaredColumns = ["ExtrasJson"]`:

```sql
SELECT [T].[Id] AS [c0], [T].[Amount] AS [c1], [P1].[PostingDate] AS [c2], [T].[ExtrasJson] AS [c3]
FROM [acme].[SalesInvoiceLines] AS [T]
INNER JOIN [acme].[SalesInvoices] AS [P1] ON [P1].[Id] = [T].[InvoiceId];
```

`Columns[3]` is `(3, "ExtrasJson", QxString, Nullable, null, false)`.

**Compatibility:** an empty list emits today's SQL and columns. **Tests:** goldens with zero, one
and two columns, beside a join and with paging; `Columns` for each; the caller-error vectors;
differential (the interpreter reads the fixture's undeclared column directly).

### §F.17 `startOfQuarter`

**Target:** 0008 §10.4. **Requested by:** spec 0033 §3.2, spec 0029 §11.2. **Why:** a report
groups by calendar quarter as it groups by year and month. 0008 §10.4 truncates to a year, a month,
a week and a day, but not to a quarter.

```
startOfQuarter(d: Cal [, cal])  -> Date    the first day of the quarter that quarter(d, cal) names
```

- **Semantics.** The first day of month `3 × (quarter(d, cal) − 1) + 1` of the date's year in
  `cal`. Under `'gc'` that is 1 January, 1 April, 1 July or 1 October.
- **Calendars.** `cal` binds as it does for the other calendar functions: `'gc'`, `'et'` (§F.19),
  and `'uq'` when the schema maps it (§F.11).
- **Nullity.** `Propagate(0)`, as for the other truncations.
- **Emission.** Under `'gc'`, whole quarters since the epoch `CONVERT(date, '00010101')`:
  `DATEADD(QUARTER, DATEDIFF(QUARTER, <epoch>, <d>), <epoch>)`. The operand appears once.

**Example —** `startOfQuarter(Invoice.PostingDate)` gives `2026-04-01` for a posting date of
`2026-05-20`. `startOfQuarter(Invoice.PostingDate, 'uq')` gives the Gregorian date on which that
date's Hijri quarter began.

**Compatibility:** additive; a new name in the registry. **Tests:** corpus rows over each quarter
boundary and over 29 February; a golden per calendar; differential over every day of 1900–2100
under `'gc'` and `'et'`, and over §F.11's range under `'uq'`.

### §F.18 `QueryexVersion`

**Target:** 0008 §1.4 (`ValidationOptions`, `DiscoveryOptions`), §2.1 (`QueryCompilationOptions`),
§16, §17, §20. **Requested by:** spec 0012 §5.2; spec 0014 §3.7; spec 0033 §2.1. **Why:** in Tellma
"language" means a human language, so a `LanguageVersion` beside a report's `Title2` reads as a
translation revision. The stamp names what it versions.

```csharp
public sealed record ValidationOptions       { public required int QueryexVersion { get; init; } }                    // old: LanguageVersion
public sealed record QueryCompilationOptions { public required int QueryexVersion { get; init; } }                    // old: LanguageVersion
public sealed record DiscoveryOptions        { public int QueryexVersion { get; init; } = QueryexLanguage.Version; } // old: LanguageVersion
```

- **One word in every layer.** §F.6, §F.9 and §F.10 add their members under this name, and hosts
  stamp with it (`Report.QueryexVersion`, `Permission.FilterQueryexVersion`).
- **`QueryexLanguage` keeps its name.** `Version`, `Minimum` and `IsSupported` read correctly on it,
  and a class named `QueryexVersion` would clash with the members.
- **The implemented code:** 64 occurrences in 21 files, matched ignoring case.
  - `src/core/Tellma.Core.Queryex`: `QueryCompilationOptions.cs` (the three members),
    `QueryexEngine.cs`, `Binding/BindingContext.cs`, `Caching/CacheKeys.cs` (a constructor
    parameter and the key's property), `Pipeline/Discoverer.cs`, `Pipeline/ExpressionCompiler.cs`
    and `Pipeline/QueryClauses.cs`;
  - `eng/queryex-inspection/Tellma.Queryex.Inspection/Inspection.cs`;
  - `test/core/Tellma.Core.Queryex.Tests`: `Versioning/LanguageVersionTests.cs`, which becomes
    `Versioning/QueryexVersionTests.cs` with its class, `Robustness/HostileInputTests.cs`,
    `Emit/CompileQueryTests.cs`, `Binding/DiagnosticShapeTests.cs`, `Properties/PropertyTests.cs`,
    `Corpus/ExpressionCorpusTests.cs`, `Corpus/GoldenTests.cs` and `Emit/FoldingTests.cs`;
  - `test/core/Tellma.Core.Queryex.IntegrationTests`: `BackendFactsTests.cs`,
    `DifferentialTests.cs` and `ExpressionDifferentialTests.cs`;
  - `test/shared/Tellma.Queryex.Testing/Probe`: `ProbeCacheKeys.cs` and `ProbePlan.cs`.
- **XML docs** say "Queryex version" for the stamp, internal members included.

**Compatibility:** source-breaking for callers of the three options. SQL, slots, cache identity and
goldens do not change, because the cache key holds the value, never the member's name. **Tests:**
the existing suites under the new names.

### §F.19 The `'et'` calendar

**Target:** 0008 §10.4, which reserves `'et'` as "algorithmic", emitted "as inline date
arithmetic". **Requested by:** spec 0029 §11.2; spec 0033 §3.2, §8.7. **Why:** Ethiopic authors
group by the Ethiopic year, quarter and month, as Hijri authors do through §F.11.

```text
cal = 'et'    accepted by the eleven calendar functions (0008 §10.4) and startOfQuarter (§F.17); no schema opt-in
```

- **The calendar** is Amete Mihret, as ICU's `ethiopic`: twelve months of 30 days, then Pagume of
  5 days, or 6 when `year mod 4 = 3`. 1 Meskerem 1 is the proleptic Gregorian `0008-08-27`.
- **The day count.** `c = DATEDIFF(DAY, '00080827', <d>) + 2922`. The two four-year cycles added
  keep `c` non-negative for every SQL Server `date`, so integer division floors. Then:
  - `y = (4c + 1463) / 1461`, and the Ethiopic year is `y − 8`;
  - the day of the year is `k = c − (365 × (y − 1) + y / 4)`, from 0;
  - the month is `k / 30 + 1`, and the day `k % 30 + 1`.

  The inverse gives a date from a year, month and day:
  `DATEADD(DAY, 365 × (y − 1) + y / 4 + 30 × (month − 1) + day − 1 − 2922, '00080827')`, with
  `y` = the Ethiopic year + 8.
- **Semantics.**
  - `quarter` = `min(4, (month + 2) / 3)`, so Pagume falls in the fourth quarter, as §D.7's
    client format places it; `week` = `k / 7 + 1`;
  - `startOfYear`, `startOfQuarter` and `startOfMonth` give the Gregorian date of the Ethiopic
    boundary;
  - `addYears` and `addMonths` count 13 months to a year, keep the time of day, clamp to the
    target month's last day (Pagume's 5 or 6 included) and truncate `n` as `'gc'` does;
  - `diffYears` and `diffMonths` count completed units by 0008 §10.4's rule.
- **Nullity** is the signature's, as for `'gc'`: the arithmetic answers every date.
- **Emission.** Inline integer arithmetic over `c`. A date operand that is not a column or a
  parameter enters once, through a value binding (0008 §12.3). A `DateTime` operand is cast to
  `date` for the count, and `addYears` and `addMonths` add its time of day back.

**Example —** `month(Invoice.PostingDate, 'et')` is 1 for `2026-09-11` (1 Meskerem 2019) and 13 for
`2026-09-10` (5 Pagume 2018). `startOfYear(Invoice.PostingDate, 'et')` is `2023-09-12` for every
date of 2016, because 2015 had a 6-day Pagume. The year emits as:

```sql
(4 * (DATEDIFF(DAY, CONVERT(date, '00080827'), [P1].[PostingDate]) + 2922) + 1463) / 1461 - 8
```

**Compatibility:** additive; `'et'` was QX3101. **Tests:** corpus rows over each year's first day,
Pagume 5 and 6, and the fourth quarter; a golden per function; differential over every day of
1900–2100, against the interpreter's own conversion; a checked-in vector file from ICU's
`ethiopic` calendar (`Intl.DateTimeFormat`), the client's source of Ethiopic dates, that both the
corpus and the interpreter read.

### §F.20 `MeasureCondition`

**Target:** 0008 §1.4 (the public API), §10.2 (an aggregation's condition argument). **Requested
by:** spec 0033 §9.4; spec 0034 §8.1. **Why:** a drill from a measure lists exactly the rows its
number aggregates, so it needs the measure's row condition as self-contained text. §F.14's partials
carry no condition.

```csharp
public sealed class QueryexEngine
{
    public QueryexResult<string?> MeasureCondition(string measure, ParameterBindingOptions options); // new; null: every row in scope feeds the measure
}
```

- **What it collects:** the condition argument of every aggregation in `measure`, the second
  argument of `count`, `sum`, `avg`, `min` and `max`.
- **Binding.** Each condition prints by §F.10's rule under `options`, whose `Mode` is ignored,
  since an aggregation's condition folds in every mode: valued parameters become literals, and
  omitted ones fold (§F.8).
- **The answer.**
  - **null** when an aggregation has no condition, or its condition folds to `true`;
  - otherwise the distinct conditions, compared as canonical text (0008 §12.1), each
    parenthesized and joined with `or` in first-use order;
  - a condition that folds to `false` drops out, and the answer is `false` when every one does;
  - a measure with no aggregation answers null, since binding refuses it elsewhere (QX4011,
    §F.14).
- **Syntactic only,** like `BindParameters`: it needs no schema, and lexical and syntax errors
  return their diagnostics. Its caller errors are `BindParameters`'.

| Measure | Values | Answer |
|---|---|---|
| `sum(Amount, Invoice.Customer.CustomerType = 'Corporate')` | — | `(Invoice.Customer.CustomerType = 'Corporate')` |
| `sum(Amount, Invoice.Customer.CustomerType = 'Corporate') / sum(Amount)` | — | null |
| `sum(Amount, CustomerId = @Customer) / count(Amount, CustomerId = @Customer)` | `Customer` = 1012 | `(CustomerId = 1012)` |
| the same | `Customer` omitted | null |

**Compatibility:** a new entry point. **Tests:** a vector per table row; deduplication; a `false`
fold; diagnostics. Differential: for a measure with one aggregation, the aggregation over the rows
the answer admits equals the measure over every row, on the fixture.

### §F.21 Filter conjuncts in discovery

**Target:** 0008 §2.4 (`QueryDiscoverySpec`, `DiscoveryResult`). **Requested by:** spec 0029 §9;
spec 0034 §8.4. **Why:** a drill list opened from a bare link labels its chips from the server's
parse of the very filter it runs, values included, never from text carried beside it.

```csharp
public sealed record QueryDiscoverySpec
{
    public bool Conjuncts { get; init; }                           // new; default false
}
public sealed record DiscoveryResult
{
    public IReadOnlyList<FilterConjunct>? Conjuncts { get; init; } // new; null unless asked
}
public sealed record FilterConjunct(                               // new
    QueryexTextSite Site,                 // the atom's leaf location and span
    IReadOnlyList<string>? Path,          // the compared path's segments; null for an expression
    string? Expression,                   // the compared expression's canonical text; null for a path
    FilterConjunctOperator Operator,
    object? Value,                        // the literal, as §F.10's CLR value; null for null
    QueryexType Type);                    // the compared side's type
public enum FilterConjunctOperator { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual, DescendantOf }
```

- **Which atoms.** Each atom reached from a `Filter` leaf through `and` alone, never under `or` or
  `not`, that is one of:
  - a bare path or a parenthesized expression compared with a literal by `=`, `!=`, `<`, `<=`, `>`
    or `>=`, in either operand order; a literal on the left mirrors the operator;
  - `is null` and `is not null`, as `Equal` and `NotEqual` with a null `Value`;
  - `descendantOf(path, literal)`.
- **Literals** are §F.10's printed forms: numbers (a negated one included), strings, `true` and
  `false`, and `cast('…', '<type>')` of a string.
- **Nothing else yields one.** `true` atoms, `Via` and `Exists` nodes, comparisons with a parameter
  and atoms that do not bind produce no conjunct; their paths still appear in `Paths`. `Having`
  yields none.
- **Order:** by leaf, then by span. `DiscoveryResult` keeps its constructor; the new member is an
  `init` property (§F.0).

**Example —** a leaf `Invoice.CustomerId = 1012 and Invoice.PostingDate >= cast('2026-07-01',
'date') and (Amount * 2) > 100 and (Region = 'East' or Region = 'West')` yields three conjuncts:
`Invoice.CustomerId` `Equal` 1012, `Invoice.PostingDate` `GreaterOrEqual` 2026-07-01, and the
expression `Amount * 2` `Greater` 100. The disjunction yields none.

**Compatibility:** with `Conjuncts` false, discovery answers as today. **Tests:** discovery corpus
rows per operator and operand order; each literal form; the skipped atoms (`or`, `not`, `true`,
`Via`, `Exists`, a parameter); sites across two leaves.

### §F.22 Current and legacy zone ids

**Target:** 0008 §10.3 (`local`'s `tz` and the embedded CLDR table), §20. **Requested by:** spec
0013 §7.7 (the `core.TimeZone` value set and its aliases); spec 0024 §7.8 (the device zone); spec
0032 §4.2 (the zone options). **Why:** the application's zone list comes from the table Queryex
compiles with, so every zone a user picks compiles and no second list can drift; each location
appears once, under its current id, and the older spelling a browser reports still finds it.

```csharp
// Tellma.Core.Queryex — new
public static class QueryexTimeZones
{
    public static IReadOnlyList<string> Ids { get; }                    // the current ids, in ordinal order
    public static IReadOnlyDictionary<string, string> Aliases { get; }  // alias → current id; keys match ignoring case
}
// Time/CldrTimeZones.cs, internal: one table becomes two maps
//   Zones:   current IANA id → Windows zone name
//   Aliases: alias IANA id → current IANA id
```

- **Current ids.** An id is current when tzdb's `zone.tab` lists it as a location, or when it is
  `UTC`. `zone.tab` keeps each country's own id, such as `Asia/Kuwait` and `Africa/Addis_Ababa`,
  which tzdb's `backward` file links to a merged zone. Zones keeps today's Windows name for each
  current id.
- **Aliases.** An alias is an id that a supported browser engine reports in place of a current id.
  CLDR keeps its own id for zones that IANA renamed, and Chromium's
  `Intl.DateTimeFormat().resolvedOptions().timeZone` returns CLDR's. The table is refreshed with
  Zones and holds these 19:

  | Alias | Current id | Alias | Current id |
  |---|---|---|---|
  | `Africa/Asmera` | `Africa/Asmara` | `Asia/Calcutta` | `Asia/Kolkata` |
  | `America/Buenos_Aires` | `America/Argentina/Buenos_Aires` | `Asia/Katmandu` | `Asia/Kathmandu` |
  | `America/Catamarca` | `America/Argentina/Catamarca` | `Asia/Rangoon` | `Asia/Yangon` |
  | `America/Coral_Harbour` | `America/Atikokan` | `Asia/Saigon` | `Asia/Ho_Chi_Minh` |
  | `America/Cordoba` | `America/Argentina/Cordoba` | `Atlantic/Faeroe` | `Atlantic/Faroe` |
  | `America/Godthab` | `America/Nuuk` | `Europe/Kiev` | `Europe/Kyiv` |
  | `America/Indianapolis` | `America/Indiana/Indianapolis` | `Pacific/Enderbury` | `Pacific/Kanton` |
  | `America/Jujuy` | `America/Argentina/Jujuy` | `Pacific/Ponape` | `Pacific/Pohnpei` |
  | `America/Louisville` | `America/Kentucky/Louisville` | `Pacific/Truk` | `Pacific/Chuuk` |
  | `America/Mendoza` | `America/Argentina/Mendoza` | | |

- **Other ids leave the table.** Renamed and deprecated ids no browser reports, such as
  `US/Alaska`, `Australia/ACT`, `GB` and `Etc/Zulu`, and the fixed-offset `Etc/GMT±n` zones are
  QX3101.
- **Resolution.** `tz` accepts a Zones key, or an Aliases key resolved through its current id,
  matched ignoring case as today. The Windows name comes from Zones.
- **The lists.** `QueryexTimeZones.Ids` is Zones' keys in ordinal order, and `Aliases` is the alias
  map. Spec 0013 builds the `core.TimeZone` value set from `Ids` and carries `Aliases` beside it,
  so the metadata never lists an alias as a value.
- **The class.** `CldrTimeZones`'s remark about keeping deprecated ids for stored expressions
  gives way to the rules of Zones and Aliases. `Data` keeps one record per Windows zone, with
  current ids only; the aliases are a second embedded constant of `alias|current` lines.
- **Files.** `Time/CldrTimeZones.cs` and a new `QueryexTimeZones.cs`;
  `test/shared/Tellma.Queryex.Testing/Probe/QueryexProbe.cs`, whose `ZoneIdentifierCount` counts
  Zones; `test/core/Tellma.Core.Queryex.IntegrationTests/BackendFactsTests.cs`, whose truncation
  floor follows Zones' count; and a new
  `test/core/Tellma.Core.Queryex.Tests/Time/TimeZoneTableTests.cs` with a checked-in copy of
  `zone.tab`'s ids, refreshed with the table.

**Example —** `local(PostedAt, 'Asia/Calcutta')` binds the backend name `India Standard Time`
through its current id `Asia/Kolkata`, and `local(PostedAt, 'US/Alaska')` is QX3101.

**Compatibility:** an id in neither map is QX3101; no stored text names one (§F.0). **Tests:** unit
— every Zones key is current against the checked-in ids, and no `Etc/GMT±n` id is one; every
Aliases value is a Zones key; no id in both maps; each alias resolves to its current id's Windows
name; `Ids` equals Zones' keys in ordinal order, and `Aliases` the alias map; `US/Alaska` and
`Etc/GMT-4` are QX3101. Integration — every Windows name exists in `sys.time_zone_info`, as today.

### Part F — Testing

Each amendment's **Tests** line lists its cases. All three suites run on every pull request on
Windows and Linux; none is nightly.

- **`Tellma.Core.Queryex.Tests`: corpus, goldens, cache probes (unit).**
  - Every new code and caller error.
  - One golden per new emitted shape: restrictions, `level`, paging names, `Via` guards, folded
    atoms, constant statements, `'uq'` lateral, hoisted and scalar lookups, `'et'` arithmetic,
    `Exists`, partial columns, undeclared columns.
  - L3 hits across pages; L3 splits on leaf stamps, omitted sets, measures and undeclared columns.
  - Measure conditions (§F.20), discovered conjuncts (§F.21) and the two zone maps (§F.22).
- **`Tellma.Core.Queryex.Tests`: property tests and evaluator vectors (unit).**
  - Determinism and no duplication with every new member; grouping keys repeat only in `GROUP BY`.
  - `Sql == Prologue + Body + ";\n"` (§F.3); bind-then-compile equals compile-with-arguments
    (§F.10); the printer round trip.
  - The evaluator's type, rounding, null, accumulation and error vectors (§F.15).
- **`Tellma.Core.Queryex.IntegrationTests`: differential on SQL Server (integration).**
  - Its database is created as spec 0011 §6.7 states. The fixture's text columns keep the
    collation they declare (0008 §18.3).
  - Every new corpus entry against the interpreter.
  - Absent rows under `Not(Via)` and `Not(Exists)`; `'uq'` over its whole range and beyond; a
    hoisted `'uq'` call in every position (§F.11); `'et'` over every day of 1900–2100 (§F.19).
  - A measure's aggregation over the rows its condition admits (§F.20).
  - `Body` as a derived table.
  - The three measure identities over hand-written and generated measures (§F.15).
- **Once, before §F.11 is built: the `'uq'` join-shape benchmark.** Lines reach `PostingDate`
  through the invoice header, with a covering and a non-covering header date index. It compares
  the hoisted variable, a bound parameter and the scalar form at 10M rows. It is a measurement for
  flag 4, not a suite (0008 §18).

### Part F — Definition of done

- **Engine:** §F.1–§F.4 and §F.6–§F.22 in `src/core/Tellma.Core.Queryex`, with XML docs on every
  new member and the project README's list of amendments updated. Warnings-as-errors green on
  Windows and Linux; no new project or package reference.
- **Surface:** 0008 §20's public surface plus exactly the members this part's sketches add, with
  §F.18's three renames.
- **Behaviour:** pinned by Part F's suites, over the grown `LedgerFixture` and the extended
  interpreter (§F.0).
- **Compatibility:** every pre-existing corpus entry passes unchanged; every pre-existing golden is
  byte-identical except the paging names of §F.4; `QueryexLanguage.Version` is 1.
- **Tool:** `eng/queryex-inspection` accepts restrictions, measures, omitted parameters, constant
  items, undeclared columns and the conjuncts switch, and shows partial columns, conjuncts and a
  measure's condition.
- **Benchmark:** the `'uq'` join-shape benchmark (Part F — Testing) ran before §F.11 was built,
  and its result is recorded against flag 4.
- **Docs:** the architecture document names the `'uq'` month table, `Exists` and the measure
  evaluator where it describes Queryex. Public XML docs and error messages reference no `docs/`
  paths.
- **Not in scope of done:** what hosts do with the amendments: spec 0012 declaring `CalendarMap`,
  binding the paging slots, passing its `[JsonColumn]` columns and reporting
  `QueryColumn.Precision`; spec 0013's month table and `core.TimeZone` value set with its aliases
  (§7.7); spec 0024's device zone (§7.8); spec 0014's `Via`; spec 0033's compiler, cube builder,
  drill conditions and `Exists` criteria; spec 0029's conjuncts on the wire.

### Part F — Decisions record

1. **One home for engine amendments: the data access layer's items live here, and spec 0012
   §11.2 points here** — a frozen spec changes only through this spec (§F.1–§F.7, §F.16).
2. **`QueryexLanguage.Version` stays 1** — no stored text changes meaning: each new form binds only
   under a host opt-in or a code 0008 reserved, §F.18 renames members, not language, and §F.22's
   removed ids name no stored text. A version 2 with dual compilation was the alternative (§F.0).
3. **Host-built structure fails as caller errors, user text as diagnostics** — `Via` and `Exists`
   names, restriction paths and option names are never user input (§F.1, §F.7, §F.12).
4. **Paging slots are `Literal` slots with fixed names, re-stamped per call** — hosts already bind
   `Literal` slots from `Value`, and a `Declared` slot could collide with a user parameter `@take`
   (§F.4).
5. **Context slots need no engine change** — 0008 already emits them; binding them to the tenant is
   the host's (§F.5).
6. **Omitted parameters fold syntactically, by polarity, after bare list elements drop** — one
   folder serves §F.8 and §F.10, and `in (@A, @B)` keeps `@A`. Folding the bound tree, and
   whole-atom folding only, were the alternatives (§F.8).
7. **Constant statements are a second entry point emitting one `FROM`-less `SELECT`** — the
   statement is complete, so not a fragment API; `CompileQuery` over a dummy root would return one
   row per root row (§F.9).
8. **`BindParameters` is syntactic** — only the engine has the lexer and the printer; host string
   replacement breaks on `@` inside strings (§F.10).
9. **`'uq'` reads a host month table by one `TOP (1)` seek, in three forms: lateral for a row-grain
   call, a hoisted variable for a path-free one, scalar for an aggregated one** — the lateral and
   hoisted forms are legal in `GROUP BY` and inside aggregates, so no position refuses a `'uq'`
   call. The seek stays sargable (§F.11).
10. **`Exists` is a host-built semi-join; the language gains no collection syntax** — sharing needs
    "some child row" without fan-out, and a host-filled key set cannot sit under `Or` (§F.12).
11. **Measures decompose into `sum`, `count`, `min` and `max` partials, evaluated in C# by the
    engine** — a pivot groups at leaf grain and rolls up in C# (spec 0033 §8.6), so a subtotal
    needs the outer expression over rolled-up partials. One owner keeps both arithmetics in step.
    The closed list refuses a non-decomposable aggregate by default. Grouping sets with tree level
    navigations were the alternative (§F.14, §F.15).
12. **The evaluator runs `SqlDecimal` at each node's SQL type** — it reproduces SQL Server's value
    and scale, digit for digit. `System.Decimal` with six-digit agreement was the alternative
    (§F.15).
13. **Text partials combine with a case-insensitive .NET comparer, and hierarchy paths by their
    integer levels** — no extra SQL and no engine machinery. The comparer differs from the
    collation only on awkward pairs, which business uses of `min` and `max` over text almost never
    meet. An SQL `DENSE_RANK` column per partial was the alternative: exact, at the cost of a sort
    per partial (§F.15).
14. **Columns the schema omits ride `CompileQuery` as names** — the engine owns the
    statement's layout and its column list, so no host parses or splices compiled text. Splicing
    the projections into `Body` before its `FROM` line was the alternative (§F.16).
15. **The stamp is `QueryexVersion` in every layer, the implemented library included** — beside
    multilingual titles, "language version" reads as a translation revision; renaming only the
    hosts would leave `QueryexVersion = options.LanguageVersion` at every seam. `QueryexLanguage`
    keeps its name (§F.18).
16. **`'et'` is one day count with integer division, offset by two four-year cycles** — 0008 §10.4
    fixed the strategy, the offset keeps division a floor over every SQL Server `date`, and no
    table or schema opt-in is needed (§F.19).
17. **A measure's drill condition is the `or` of its aggregations' conditions, none if any has
    none, built syntactically by the engine** — the rows that feed a measure are the union of the
    rows that feed its parts, and only the engine holds the printer and the folder (§F.20).
18. **Conjuncts are the top-level `and` atoms that compare with a literal** — chips then come from
    the parse of the filter that runs; disjunctions and negations keep label-only chips (§F.21).
19. **The zone table holds current ids and the ids browsers report for them, and the application's
    zone list is its current ids** — one list cannot drift from what compiles, and two lists kept
    equal by a test was the alternative. Chromium reports CLDR's ids for zones IANA renamed, so
    without the aliases a device in India would never match `Asia/Kolkata`. Current ids are
    `zone.tab`'s, since tzdb's `Zone` names alone would drop merged country ids such as
    `Asia/Kuwait`. The `Etc/GMT±n` zones leave: their signs read inverted (`Etc/GMT-4` is UTC+4),
    and no current id means the same, so they cannot be aliases (§F.22).
20. **`if` and `coalesce` are the portable guard; `and` and `or` short-circuit in C# only** — SQL
    Server fixes no operand order, so a guard written with `and` holds only in C#, and the
    differential suite guards divisions with `if` or `coalesce` (§F.15).
21. **Omitted atoms fold, then absorb through `and`, `or` and `not`, identically in every entry
    point** — a drill's printed condition then matches what the compiler runs; printing `and true`
    was the alternative (§F.8).

### Part F — Review flags

1. **`Literal` paging slots** (§F.4). Alternative: `Declared` slots the host binds by name. Flips if
   a host must rebind pages without the engine re-stamping the query.
2. **Caller errors for host-built structure** (§F.1, §F.7, §F.12). Alternative: diagnostics.
   Flips if a host ever builds `Via`, `Exists` or restrictions from user text.
3. **Folding before binding, with bare list elements dropped** (§F.8). A drift error inside a
   folded atom goes unreported at run time. Alternatives: bind, then fold; fold whole atoms only.
   Flips if optional atoms break silently in stored reports, or when list parameters ship.
4. **Path-free `'uq'` calls as hoisted variables** (§F.11). A variable's cardinality estimate is
   blind (0008 §12.4), and `OPTION (RECOMPILE)` would compile every run. Alternative: the scalar
   subquery, which SQL Server refuses inside aggregation arguments and grouping keys, so those
   positions would need a diagnostic. Flips if the join-shape benchmark (Part F — Testing) finds
   the variable more than 1.5× slower than a bound parameter.
5. **The name `EvaluateConstant`** (§F.9). Alternative: `CompileConstants`, since the engine never
   executes. Flips on preference.
6. **A month table sought by (`Calendar`, `StartDate`)** (§F.11). Alternative: a per-day table with
   equality seeks. Flips on measured lookup cost over large fact tables.
7. **The evaluator's subset** (§F.15). Alternative: more date functions over aggregates
   (`startOfMonth`, `addDays`, distances over `DateTime` and `DateTimeOffset`). Flips when a real
   report needs one; each is an additive evaluator node.
8. **Evaluation errors fail the run** (§F.15), as 0008 §9.1 makes division by zero an error.
   Alternative: a null cell with a warning. Flips if users prefer blanks to guarding measures with
   `if`.
9. **Undeclared columns are text only** (§F.16). Alternative: a name with a `QueryexType` per
   column. Flips when a host needs to read an undeclared column that is not text.
10. **`count(x)` adds no presence condition** (§F.20). A drill from `count(CustomerId)` also lists
    lines with no customer, which the cell does not count. Alternative: `(x) != null` as its
    condition. Flips if users read the difference as a wrong drill.

## Part G — Table types (spec 0001)

Part G amends spec 0001, the table-types library in `src/core/Tellma.Core.EntityFrameworkCore`.
It lets a class-derived standalone table type give a string column a collation (§G.1). Every rule
of spec 0001 that Part G does not name stands. Table-derived types need no change: they already
take each column's collation from the table (0001 §2).

**Placement**

| Project | Location | Change |
|---|---|---|
| `Tellma.Core.EntityFrameworkCore` | `src/core/Tellma.Core.EntityFrameworkCore/` | §G.1: `TableTypeBuilder.UseCollation` and the character-column check |
| Tests | `test/core/Tellma.Core.EntityFrameworkCore.Tests`, `.Design.Tests`, `.IntegrationTests`, and the migrations host `test/core/assets/Tellma.Core.EntityFrameworkCore.MigrationsHost` | Part G — Testing |

No new project or package reference. Part G waits on no other part. It ships before spec 0012,
whose `[Collation]` annotation maps onto it (spec 0012 §1.3).

### §G.1 Column collations on class-derived standalone types

**Target:** 0001 §5 (standalone table types) and its Testing plan. **Requested by:** spec 0012
§1.3, §2.2 and §4.4; spec 0011 §3.8; spec 0018 §3.1. **Why:** a type's string column compared
with an explicitly collated column must carry that collation, or the statement fails to compile
with Msg 468. Spec 0001 sets one only through an ad-hoc `Column(…, collation)`, and `Column(…)`
cannot add a column the class already has.

```csharp
// Tellma.Core.EntityFrameworkCore.TableTypes.TableTypeBuilder — new member
public virtual TableTypeBuilder UseCollation(string column, string collation);
```

**Example —** the registration that spec 0012's conventions make for the catalog's membership
type, whose `Subject` meets a `Latin1_General_100_BIN2` column. The class carries spec 0012's
`[TableType]`, not the library's, so the call names the type:

```csharp
modelBuilder.HasTableType<TenantMembershipRecord>(
    name: "TenantMembershipList",
    schema: "catalog",
    buildAction: type => type.UseCollation("Subject", "Latin1_General_100_BIN2"));
```

- **What it sets.** The named column's collation, whether the class or `Column(…)` added the
  column. The last call wins, over an earlier call and over `Column(…, collation)`.
- **When it applies.** The builder applies it when it builds, so the call's order against
  `Column(…)` does not matter.
- **An unknown column.** A name no column has fails registration with an
  `InvalidOperationException` naming the type and the column. Names match case-insensitively, as
  the duplicate-column and key checks do.
- **Character columns only.** A standalone column with a collation, set by `UseCollation` or by
  `Column(…, collation)`, must resolve to `char`, `varchar`, `nchar` or `nvarchar`. Any other
  store type, `json` included, fails model finalization, naming the type, the column and the store
  type. Without the check, SQL Server fails the migration with Msg 447, and the library silently
  replaces a `json` column's collation with the wire form's.
- **Names are not validated.** SQL Server refuses an unknown name at `CREATE TYPE` with Msg 448.
  Write the canonical spelling: the name is part of the canonical JSON, so a change of case
  creates a new version.
- **Versioning.** Adding, changing or removing a column's collation changes the definition. The
  differ creates a new physical version beside the old one, and the sweep retires the old (0001
  §3).

**Compatibility:** additive. A column without a collation serializes as before, so no existing
definition changes its hash. The snapshot and operation generators already write a column's
collation. The character-column check moves a failure from `CREATE TYPE` to model finalization.
It also refuses a `json` column with a collation, which today builds with the wire form's
collation.

**Tests:**

- unit:
  - a class-derived column with `UseCollation` gets the collation in the definition, and keeps
    its store type;
  - a column added by `Column(…)`, with `UseCollation` before or after it;
  - the last call wins;
  - an unknown column throws;
  - `int`, `datetimeoffset` and `json` columns with a collation fail finalization;
  - adding a collation emits a create and the cleanup, and no drop;
  - a type without one keeps its canonical JSON;
- design — the snapshot round-trip model gains a collated class-derived column;
- integration — a class-derived type with a `Latin1_General_100_BIN2` column is created,
  `sys.columns.collation_name` reads that collation, and a variable of the type joins a `BIN2`
  table column without Msg 468.

### Part G — Testing

| Suite | Tier | Pins |
|---|---|---|
| `Tellma.Core.EntityFrameworkCore.Tests` | unit | §G.1's builder, finalization and differ cases |
| `Tellma.Core.EntityFrameworkCore.Design.Tests` | unit | the snapshot round trip with a collated column |
| `Tellma.Core.EntityFrameworkCore.IntegrationTests` | `Category=Integration` | the collated type on SQL Server; the migrations host gains the type, so its migrations are re-scaffolded and its type counts updated; the fixture creates each database with spec 0011 §6.7's SQL Server statement |

All three run on every pull request on Windows and Linux; none is nightly.

### Part G — Definition of done

- **Library:** §G.1 in `src/core/Tellma.Core.EntityFrameworkCore`, with XML docs on the new
  member and the README's standalone paragraph updated. Warnings-as-errors green on Windows and
  Linux; no new project or package reference.
- **Behaviour:** pinned by Part G's suites.
- **Compatibility:** no existing definition's hash changes. Every existing test passes unchanged,
  except where Part G — Testing adds the collated type: the snapshot round-trip model, the
  migrations host's migrations and the integration suite's type counts.
- **Docs:** the architecture document names the column collation of a class-derived type where it
  describes standalone table types. Public XML docs and error messages reference no `docs/` paths.
- **Not in scope of done:** spec 0012's `[Collation]` annotation and its convention; the two
  `Subject` columns (specs 0011 and 0018).

### Part G — Decisions record

1. **A builder call, not a library attribute** — the platform's standalone classes live in EF-free
   `Tellma.Core.Abstractions`, where a library attribute cannot go. Spec 0012 maps its own
   annotation onto this call, as it maps `[TableType]`, and EF itself sets collations only
   through the fluent API (§G.1).
2. **Table-derived types are unchanged** — they already copy each column's collation (0001 §2).
3. **Collation names are not validated** — EF validates none, and SQL Server refuses an unknown
   name at `CREATE TYPE` (§G.1).
4. **A collation on a non-character column fails model finalization** — the error comes at build
   time and names the column, instead of coming from `CREATE TYPE` (§G.1).

### Part G — Review flags

1. **No library attribute** (§G.1). Alternative: a collation attribute in the library beside the
   builder call. Flips if a class outside `Tellma.Core.Abstractions` needs a collation.
2. **Refusing `json` with a collation** (§G.1). Alternative: keep replacing it silently with the
   wire form's. Flips if a model must state the wire collation.
3. **Case-sensitive names in the hash** (§G.1). Alternative: normalise the spelling before
   hashing. Flips if a spelling change ever churns a version.

