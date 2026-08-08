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

Both shapes call the same three extensions:

```csharp
builder.Services.AddTellmaIdentity(builder.Configuration.GetSection("TellmaIdentity"));
app.UseTellmaIdentity();
app.MapTellmaIdentity();
```

Configuration binds to `TellmaIdentityOptions` (see `Options/`). The server runs fully on-prem:
certificate-store or PFX key material, file-system Data Protection, and SMTP email; Azure Key Vault,
blob-backed Data Protection, and Azure Monitor are config-gated optional paths.

The engine reads the client IP from the connection (`RemoteIpAddress`) for rate limiting and audit.
A host that sits behind a reverse proxy — standalone or in-proc — must register the ASP.NET Core
forwarded-headers middleware as its first middleware, restricted to the deployment's known
proxies/networks; see the [`Tellma.Identity.Web` README](../Tellma.Identity.Web/README.md) for the
fail-closed configuration pattern.

EF Core migrations for the engine's database (SQL schema `idsvr`) live in the separate
[`Tellma.Identity.Migrations`](../Tellma.Identity.Migrations/README.md) project.

The UI renders through three layouts under `Areas/Identity/Pages/Shared/`: `_Document` owns the
document and the scripts slot, and two shells nest inside it — `_AuthLayout` for the sign-in flow
(a brand panel beside the form on wide viewports, a card on a grey pane below that, and the bare
form on the narrowest screens) and `_ManageLayout` for the self-service pages (a topbar and a
navigation rail that collapses to a strip). Both are styled by the single hand-written
`wwwroot/css/identity.css`, which has to stay one file: the client workspace's dangling-`var()`
gate names that exact path. `wwwroot/img/` holds the brand wordmarks, hand-committed like the
fonts below.

The `wwwroot/css/tokens.css` stylesheet is the **emitted build of `@tellma/core-ui-tokens`** (the
client workspace's design-token package), and `wwwroot/fonts/` vendors the brand faces from
`@tellma/core-ui`. Both are committed copies because this project's build has no Node toolchain; the
client workspace's `pnpm run tokens:check` CI gate fails whenever they drift from the emitter's
output. To refresh after a token change: `pnpm run tokens:build-css` in `client/`, then copy
`client/projects/core/tellma-core-ui-tokens/css/tellma-default.css` over `wwwroot/css/tokens.css`
(and `client/projects/core/tellma-core-ui/fonts/` over `wwwroot/fonts/` when the faces change).
Per-tenant branding later means serving a different tokens file, not editing styles.
