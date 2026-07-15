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

EF Core migrations for the engine's database (SQL schema `idsvr`) live in the separate
[`Tellma.Identity.Migrations`](../Tellma.Identity.Migrations/README.md) project.

The `wwwroot/css/tokens.css` stylesheet is a **placeholder** for the compiled output of
`@tellma/core-ui-tokens`; it will be replaced byte-for-byte when the client component library ships.
