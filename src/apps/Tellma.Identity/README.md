# Tellma.Identity

The Tellma Identity Server **engine**: a Razor Class Library that carries the complete OpenID Connect
authority — OpenIddict server/validation configuration, ASP.NET Core Identity (users, passkeys, MFA,
the SSO session cookie), the protocol controllers, the auth-flow UI (Razor Pages), the lifecycle APIs
(bulk invitation, service accounts, operator surface), authentication-policy enforcement (`acr`/`amr`),
back-channel logout, and the SQL-backed stores.

The engine is deployed in two hosting shapes through **one registration path**:

- **Standalone** — [`Tellma.Identity.Web`](../Tellma.Identity.Web/README.md) hosts it as its own app
  (the shared authority, or an isolated authority for data-residency deployments).
- **In-proc** — a distribution's web host references this project and mounts the authority at a
  reserved path base (for example `/id`) on its own origin.

Both shapes call the same three extensions, plus the email pipeline the engine expects the host to
compose:

```csharp
builder.Services.AddTellmaIdentity(builder.Configuration.GetSection("TellmaIdentity"));

// Email: the engine asks for IEmailSender and never registers one. Add the pipeline, at least one
// transport, and the two seams it requires. In-proc, the hosting distribution already has these.
builder.Services.AddTellmaEmail();
builder.Services.AddSmtpEmail(builder.Configuration);          // or AddSendGridEmail / AddAcsEmail
builder.Services.AddSingleton(SandboxContext.Never);           // identity has no tenants
builder.Services.AddSingleton(new DeploymentIdentity("identity", builder.Environment.EnvironmentName));

app.UseTellmaIdentity();
app.MapTellmaIdentity();
```

Omitting the email registrations **fails startup** rather than accepting sign-in codes and
discarding them — the engine mounts a mail worker, and that worker refuses to start against a
container with no sender in it.

Configuration binds to `TellmaIdentityOptions` (see `Options/`); mail is configured separately,
under the platform's own `Email` section. The server runs fully on-prem: certificate-store or PFX
key material, file-system Data Protection, and mail relayed through the SMTP transport; Azure Key
Vault, blob-backed Data Protection, Azure Monitor and the two hosted email providers are
config-gated optional paths.

What the engine does own is the dispatch policy above the contract, and how a message reads.
`Services/Email/` queues every message to a background worker so an enumeration-safe endpoint answers
at the same speed whether or not the account exists, drains that queue on graceful shutdown, and
renders each message in the recipient's own locale.

Every message goes out in both forms, and both are rendered from one description of it — the parts in
`EmailContent`, never two hand-written copies that drift. `EmailTextLayout` produces the plain-text
body, which is what always arrives and what the test suites read a code or a link back out of;
`EmailHtmlLayout` produces what most recipients see: an ink banner, a heading, one button or one code
panel, and a grey footer, built as nested tables with inline styles because Outlook renders through
Word and Gmail discards a document's stylesheet for non-Gmail accounts. The two carry the same
sentences, which is both fairer to a reader whose client refuses HTML and one fewer thing for a spam
filter to score. Its colors are literals copied from the emitted design
tokens, since `var()` resolves nowhere in either; a token change has to be copied across
deliberately. The layout takes its direction from the recipient's own culture, so Arabic mirrors
whole rather than per element, and the Latin runs inside it — the address under the button, the
one-time code — are isolated back individually.

An invitation is the one message whose recipient is not waiting for it, so it is the one that
cannot be recovered by the user asking again. Each single-use code therefore records how far the
mail carrying it got, a Quartz sweep finishes any that never reached a transport, and the calling
distribution can read the outcome back through `POST /api/identity/v1/invitations/delivery-status`
(scoped to the invitations that client raised, and to nothing else). Two consequences worth
knowing: a resend rotates the code's secret, so a crash between the wire and the record produces a
second email and invalidates the first link — at-least-once, last link wins; and `Sent` is the
terminal state on a transport that reports nothing back, which is what `expectsDeliveryEvents`
tells a caller. An SMTP relay has no webhook, so on-premise deployments rest at `Sent` for good.

An `Active` result from the invite API means the user already had a credential and **no email was
sent**. Telling them they now have access is the caller's job; the server has no tenants and cannot
tell a new membership from an existing one.

`Services/Email/tellma-wordmark-email.png` is the brand mark those emails carry, embedded in the
assembly and attached to each message as an inline part. It is a hand-committed raster of
`wwwroot/img/tellma-wordmark-on-dark.svg` rendered well above the size it is drawn at, because mail
clients drop SVG, block remote images until the reader trusts the sender, and could not reach an
on-premise authority in any case. Refresh it by rendering that SVG at
396&nbsp;&times;&nbsp;112 on a transparent background whenever the wordmark changes — comfortably
past what the densest screen asks of a mark this size, and a few kilobytes either way.

One cross-cutting rule about the UI is worth knowing before touching any page in the sign-in flow.
Every response carries `form-action 'self'`, and a browser applies that directive to **every hop of the
navigation a form submission produces** — judged against the policy of the document holding the form,
not the response that does the redirecting. Signing in, granting consent, accepting an invitation and
starting a federated hand-off all end at another origin, so each of those pages widens its own policy
to exactly the destinations involved: the pending client's registered callbacks, the configured
provider's origin, the invitation's sealed destination. The widenings accumulate, because one page can
need two of them. Get this wrong and there is no error to find: the server issues a perfectly good
redirect, the browser silently declines to follow it, and the page appears to do nothing.

Failures with nowhere else to go land on the error page, which always shows the request's trace
identifier — the reference a user can quote and an operator can search for. Standalone, the host also
routes its own unhandled exceptions there; in-proc, that is the hosting distribution's to wire.

The engine reads the client IP from the connection (`RemoteIpAddress`) for rate limiting and audit.
A host that sits behind a reverse proxy — standalone or in-proc — must register the ASP.NET Core
forwarded-headers middleware as its first middleware, restricted to the deployment's known
proxies/networks; see the [`Tellma.Identity.Web` README](../Tellma.Identity.Web/README.md) for the
fail-closed configuration pattern.

EF Core migrations for the engine's database (SQL schema `idsvr`) live in the separate
[`Tellma.Identity.Migrations`](../Tellma.Identity.Migrations/README.md) project.

## The management API

The management API lives under a version segment relative to the authority, `api/identity/v1/…`
(under the path base in-proc, for example `/id/api/identity/v1/…`); nothing answers without the
segment. Version 1 is:

| Routes | Scope | Audience |
|---|---|---|
| `POST invitations`, `POST invitations/delivery-status` | `tellma_identity` | `urn:tellma:identity` |
| `POST service-accounts`; `GET`, `DELETE service-accounts/{clientId}` | `tellma_identity` | `urn:tellma:identity` |
| `GET users/{sub}`; `POST users/{sub}/temporary-access-passes` | `tellma_control_plane`, standalone only | `urn:tellma:control-plane` |

Each policy in `ApiPolicies` requires its scope **and** its audience (`ApiPolicies.HasAudience`
beside `HasScope`), so a token minted for a distribution — whose only audience is that
distribution's origin — answers `403` at both APIs whatever its scopes. Both audiences are fixed
URNs, never an origin: in-proc the issuer shares the distribution's origin, and an origin-valued
management audience would make one token valid at both. The two platform scopes converge on exactly
their URN at every start. A caller reads and deletes
every service account only with control-plane authority (`ApiPolicies.HasControlPlaneAuthority`:
the control-plane scope and the control-plane audience).

The version is the API's own integer, not the engine's release. An additive change — a new endpoint,
response member or optional request member — stays inside a version, and callers ignore response
members they do not know. A breaking change is mapped as the next version beside the current one in
one release, and the old one is removed once nothing calls it. Both facts live in one place:
`[ManagementApi(n)]` on a controller maps its routes under `v{n}` and records each call, and
`[ManagementApiEndpoint(…)]` names each action from the closed set in `ManagementApiEndpoints`.
Every call is counted on `tellma.identity.api.calls` (tags `version`, `endpoint`, `outcome`) and
logged with the calling client's id, so an operator sees when a version falls silent and who still
calls it, including a caller whose every call is refused. A refused call never reaches MVC: the
authorization middleware answers it, so the engine decorates the host's
`IAuthorizationMiddlewareResultHandler` (or the framework default) to record it as `unauthorized`
(no valid token, so no client to name) or `forbidden` (the client is named). An authorized call is
recorded by the controller's filter once answered, as `error` for an unhandled exception or a 5xx
and `ok` otherwise. A host that registers its own result handler must do so before
`AddTellmaIdentity`; one registered after it would replace the decorator, and startup refuses that.
The client id is never a tag.

A bulk-invite item with `existingOnly: true` is a lookup by email, used by sandbox tenants: it never
creates a user, issues a link or queues an email, whatever the rest of the batch does. A user who
holds a credential answers `Active` with their `sub` (an orphaned one is restored, as an ordinary
invite restores them); a user without one answers the error `existing_user_has_no_credential`; an
address nobody holds answers `unknown_email`; a disabled or purged user gets the refusal any invite
gives. The item's `returnUrl` is ignored, since no link carries it.

Every access token carries `tellma_kind`: `individual` for the `authorization_code` and
`device_code` grants and their refreshes, `service` for `client_credentials` and the exchange of a
machine token. `TellmaKindClaimHandler` writes it inside OpenIddict's sign-in pipeline from the
grant type, after each token's principal is built and before any token is signed, and strips it
from every other token; nothing a client sends and no claim already on the principal can set it.

## Resources and audiences

A `resource` parameter (RFC 8707) is decided in one place. `IResourceGrantEvaluator` holds the rule:
a requested resource is granted when it equals a granted one exactly, or when it is exactly
`<origin>/<tenant id>/mcp` under a granted resource that is itself a canonical origin, with a positive
32-bit tenant id without leading zeros — nothing else, and always compared ordinally.
`ResourceGrantValidator` decides what a request is evaluated against and audits every refusal as
`ResourceRefused`, and one OpenIddict handler per endpoint asks it (`Handlers/ResourceGrantHandlers.cs`):
authorization, pushed authorization, device authorization, and the token endpoint for every grant.
OpenIddict's own resource checks are turned off, and a refusal is always `invalid_target`.

| Request | Evaluated against |
|---|---|
| A code exchange, device-code redemption or refresh | the presented grant's own resources: it may narrow, never widen |
| Any other request | the client's `rsrc:` permissions; a document client (`tellma:document_client`) also holds every origin of the distribution-origin list |
| A control-plane client (one permitted `tellma_control_plane`) | its own permissions exactly, never the tenant pattern; with the control-plane scope on `client_credentials`, also any origin of the distribution-origin list, exactly |

The distribution-origin list is the `tellma_api` scope's resources, which onboarding appends each
distribution's origin to. A device request's resources are carried into its codes. A token request
against a stored grant narrows the access token alone (`NarrowAccessTokenAudienceHandler`); the
rotated refresh token keeps the grant, so every refresh is evaluated against the original
authorization.

A token's audience is exactly its named resources — for a machine token (`MachineTokenAudiences`,
for `client_credentials` and token exchange) and an interactive one (`TellmaPrincipalFactory`)
alike; without one, a BFF's token carries its origin, and any other token its scopes' platform
audiences (`urn:tellma:control-plane`, `urn:tellma:identity`). A machine token with `tellma_api`
always needs a resource. One token for a distribution is valid in one place: a machine request that
names a distribution's resource (an origin of the list, or a tenant resource under one) may name no
other resource, and an interactive request naming one may neither ask for `tellma_identity` nor
name a resource of a second distribution (resources of one distribution combine freely); all are
`invalid_target`. The token endpoint checks resources only after OpenIddict has verified the
presented grant (PKCE, presenter, redirect URI), so a stolen code cannot probe its resources. The control plane therefore asks for one token per distribution, naming its
origin, and one without a resource for the identity server's own APIs.

A `Native` seed entry with `ThirdParty` is third-party software, such as a coding agent: it carries
no `tellma:first_party` marker, so no first-party treatment applies to it, and its consent is always
explicit. `Seed:InterimClients` holds such entries keyed by client id (each must be a third-party
`Native` entry whose client id is its key); they seed beside `Seed:Clients`, and any entry of either
with `Enabled=false` is neither validated nor seeded.

End session asks the user to confirm unless its `id_token_hint` names the signed-in session (the
confirmation posts back with an anti-forgery token), so no page can force a global logout. A
disabled seed entry whose client is still in the store is logged as a Warning at every start.

Every interactive endpoint with error pass-through (authorization, end session) first asks
`ProtocolRefusal` (`Find` re-raises the refusal at authorization; `FindAtEndSession` answers a bare
`400`, since end session takes no challenge, and the error page renders the refusal): a request OpenIddict refused while validating it still reaches the action,
which then only re-raises the refusal for the error page — it never starts an interaction, signs in
or out, or writes state.

## The store check and seeding

Every start runs `IdentitySeeder`, and its first step is the **store check**
(`Services/Seeding/IdentityStoreCheck.cs`), which runs before any row is written:

1. An existing database must be in the platform collation, `DatabaseCollations.Platform`
   (`Latin1_General_100_CI_AS`); on Azure SQL Database its catalog collation must also be
   `DATABASE_DEFAULT`. Anything else is refused, naming the database, the value expected and the
   value found — with `Seed:ApplyMigrations` on or off, because a database's collation cannot be
   changed in place.
2. With `Seed:ApplyMigrations` on, the engine migrates the store itself, creating it when missing.
   EF creates it in the model's collation with read-committed snapshot on, and step 1's check then
   runs on what was created (a concurrent start may have created it first). The context must name
   `Tellma.Identity.Migrations` as its migrations assembly, or the check refuses before creating
   anything.
3. The migration history must hold `TellmaIdentityConstants.LatestMigration`. A missing database, a
   missing history table, or a history without that row fails the check, naming the database, the
   `idsvr` schema, the migration and `Seed:MigrateCommand` when it is set. Later rows pass, so the
   previous engine release runs on a store the next one has already migrated.

**Only development and test hosts turn `Seed:ApplyMigrations` on.** A deployed engine holds no DDL
rights: the shared store is migrated by an operator, and the in-proc store (the distribution's
catalog) by the distribution's migrator through `TellmaIdentityStore.MigrateAsync`. Step 3 reads a
constant, never the migrations, so a host that never migrates needs no reference to
`Tellma.Identity.Migrations`. A failed check throws `IdentityStoreCheckException` and stops the host.

Seeding then converges, in this order: the scope catalog, the platform clients of
`Seed:Clients` (`Cli`, `Native`, `ControlPlane`), the `Distribution` entries, and the bootstrap
identities. A **`Distribution` entry** registers a distribution's two clients through onboarding's own
steps (`ClientRegistrationConverger.ConvergeDistributionAsync`, the path `CreateDistributionAsync`
uses): the BFF client named by the slug and its `<slug>-svc` backend client without token exchange,
both built by `ClientDescriptorFactory`, with the entry's `ClientSecret` and `ServiceClientSecret` in
place of generated ones. Its `Origin` joins the `tellma_api` scope's resources and is granted to every
seeded client that calls distribution APIs; `BackchannelLogoutUri` defaults to
`{Origin}/bff/backchannel-logout`. An unchanged entry writes nothing; a changed secret, origin or
redirect list is applied and audited as `DistributionSeedApplied`. The seeder never deletes an
application, and never generates a secret: an entry needs both secrets outside Development, where
startup validation also requires a slug of at most 15 lowercase characters and an `https` origin.

Every seeded registration — scopes, platform clients and distributions — and onboarding go through
`ClientRegistrationConverger`, which writes only what differs. Several instances start together when
a secret is rotated or a deployment first comes up, and converge the same rows at once; a write lost
to a concurrent writer (a stale concurrency token, a duplicate client id or scope name) is not an
error: that write is converged again in a fresh scope, finds the winner's row and accepts or
rewrites it. Each write point (an application, a scope, a grant) is retried on its own, up to
`ClientRegistrationConverger.MaxAttemptsPerWrite` attempts, so a convergence with more write points
tolerates more lost writes in total.

The UI renders through three layouts under `Areas/Identity/Pages/Shared/`: `_Document` owns the
document and the scripts slot, and two shells nest inside it — `_AuthLayout` for the sign-in flow
(a brand panel beside the form on wide viewports, a card on a grey pane below that, and the bare
form on the narrowest screens) and `_ManageLayout` for the self-service pages (a topbar and a
navigation rail that collapses to a strip). Both are styled by the single hand-written
`wwwroot/css/identity.css`, which has to stay one file: the client workspace's dangling-`var()`
gate names that exact path. `wwwroot/img/` holds the brand wordmarks, hand-committed like the
fonts below.

## The account entry and the requested language

An application links its users to **`GET {issuer}/account`** (`AccountController`), not to a page
of this UI. The entry redirects (302, `no-store`) to the page of the `section` it is given —
`profile` (also the default, and any unknown name), `passkeys`, `authenticator`, `external-logins`
or `sessions`; the mapping is `AccountSections` — so the pages can move without any application's
link changing. An anonymous caller is sent to sign in first and comes back to the entry.

Three query values travel with the user: `client_id`, `return_url` and `ui_locales`. They are read
in one place, `CarriedQuery` (a value given twice, blank or over its length limit counts as absent;
`ui_locales` is reduced to its well-formed language tags), and kept on **every link, form and
redirect between the engine's own pages** by two pieces that apply one rule, `IdentityPageLinks`:
`CarriedQueryTagHelper` for what a view renders and `CarriedQueryRedirectFilter` for what a page
handler redirects to. A new page keeps them without doing anything. Only the pages under
`/Identity/Account` and `/Identity/Manage` receive them: a protocol endpoint never does (a
parameter added to an authorization request changes that request), and neither does a URL on
another origin.

- **The way back.** `_ManageLayout` shows a link back to the application, resolved by
  `ApplicationLinkResolver`. For a first-party client with a registered `Origin` it is the
  `return_url` when that address is on the same origin (scheme, host and port equal, `http`/`https`
  only, no userinfo), written from the parsed address; otherwise the origin's root. A third-party
  or unknown client gets the configured `Ui:ApplicationUrl`, or no link.
- **The language.** `ui_locales` on the URL outranks the language cookie and `Accept-Language`
  (`UiLocalesRequestCultureProvider`); the first tag that names an offered language, or a regional
  form of one, wins. The authorization endpoint adds the validated request's `ui_locales` to its
  redirect to sign-in, so a pushed request's language is honoured although the browser's URL only
  holds a reference; the cookie challenge that guards the account pages does the same. On the
  authorization endpoint's own pages (consent), `RequestedLanguageAttribute` applies a pushed
  request's language in place of the browser's preference, never over the cookie.
- **The user's own choice.** The language switch and the profile's language field replace the
  `ui_locales` on the address they return to, so a language the user picks is not overridden by the
  one the application asked for. On a sign-in page the switch also replaces it inside the address
  the page returns to after sign-in (`IdentityPageLinks.WithLanguage`), when that address is a
  sign-in page, an account page or the account entry: a user who switches before signing in keeps
  the choice afterwards. A pending authorization request held there is left as it is.

The `wwwroot/css/tokens.css` stylesheet is the **emitted build of `@tellma/core-ui-tokens`** (the
client workspace's design-token package), and `wwwroot/fonts/` vendors the brand faces from
`@tellma/core-ui`. Both are committed copies because this project's build has no Node toolchain; the
client workspace's `pnpm run tokens:check` CI gate fails whenever they drift from the emitter's
output. To refresh after a token change: `pnpm run tokens:build-css` in `client/`, then copy
`client/projects/core/tellma-core-ui-tokens/css/tellma-default.css` over `wwwroot/css/tokens.css`
(and `client/projects/core/tellma-core-ui/fonts/` over `wwwroot/fonts/` when the faces change).
Per-tenant branding later means serving a different tokens file, not editing styles.
