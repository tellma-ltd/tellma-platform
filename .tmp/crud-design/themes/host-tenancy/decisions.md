# Distribution host and multi-tenancy — settled design (theme `host-tenancy`, future spec 0010)

Judged 2026-09-01 from three independent proposals plus the brain dump, the breakdown, ARCHITECTURE.md,
specs 0001/0003/0007/0008, the code under `src/core/Tellma.Core.Abstractions`, and
`research/host-tenancy.md`. This file is self-contained: every name, type, statement and column a
spec author needs is here. Contract blocks use the platform's contract notation: names are normative;
shape is described, not transcribed. SQL is the exact shape to emit.

Optimisation order where lenses conflict: fail-closed access control, then one code path per concern,
then per-request cost, then distribution-author ceremony. Each section names the loser where a lens lost.

---

## 1. Critique

### 1.1 The general design

The brain dump describes the monolith's sharding model and bolts the new platform's open questions
onto it. Four structural things are missing or wrong.

1. **There is no session model.** Spec 0003 obliges every distribution to be a confidential OIDC
   client with a BFF cookie, to expose a `backchannel_logout_uri` that validates a `logout_token` and
   "kills its session" by `sid`, to keep the session sliding for seven days tied to a server-held
   refresh token, to treat token refresh as the policy re-evaluation point, to close SignalR
   connections on session end, and to answer under-assured sensitive operations with a
   `401 insufficient_user_authentication` challenge. None of this appears in the brain dump, and none
   of it is provided by the framework: .NET 10 ships no BFF package and no cookie token refresh,
   Duende.BFF is a paid licence for every customer-facing deployment, and a stateless encrypted
   cookie cannot honour a `sid`-keyed logout. The distribution's authentication is a small session
   subsystem — a store, a refresh loop, a revocation path, and a bounded staleness — that this theme
   has to state.
2. **There is no CSRF posture.** An all-POST JSON surface authenticated by a cookie is exactly the
   shape CSRF applies to, and the .NET 10 antiforgery middleware enforces nothing on JSON endpoints.
3. **The tenant registry conflates three things and stores secrets in one of them.**
   `ResolveConnectionString(tenantId)` returns a secret-bearing string on the hottest path in the
   system; `RegisterConnectionString(tenantId, connString)` stores credentials in a table, which the
   same section then forbids. Two storage topologies (config for single-live, catalog for multi-live)
   are two code paths for one concern. The better answer to "where do we store each database's
   password?" is that no tenant database gets one: in SaaS the distribution's managed identity is a
   contained user in every tenant database; on-prem one login serves every database. The catalog
   stores a *location*, never a credential.
4. **"Which tenants am I a member of" is answered by fan-out**, an N+1 across databases that also
   makes a listing look like an authorization result. Membership is a fact of each tenant database,
   verified per request; the cross-tenant list is a navigation hint that grants nothing.

Two more gaps: there is no **request context** (tenant, principal, culture, calendar, time zone,
sandbox flag) as an explicit value that reaches services and background scopes — and the research
shows the obvious `AsyncLocal` implementation leaks stale tenants into fire-and-forget work and pooled
consumers — and there is no **tenant state model** although ARCHITECTURE.md makes the distribution
the enforcer of suspension.

### 1.2 The detailed choices

- **`{tenantId}` is unspecified and inconsistent** (`api/{tenantId}/documents` in one place,
  `{tenantId}/api/web/...` in another). The identifier's type decides the URL grammar, the MCP
  resource identifier, log correlation and route collision rules; fixed in D5.
- **"Every request comes with a tenantId"** is false for a distribution: the BFF routes, the
  back-channel receiver, `/api/distribution-info`, the admin surface, health probes, webhook receivers
  (which already carry `WebhookEndpointMetadata` so a tenant middleware can skip them) and the whole
  in-proc identity engine at `/id/*` are tenant-less. The design needs an explicit *deployable*
  request class or `ISandboxContext` has nothing sound to stand on.
- **"Keys and secrets are never stored in the DB in clear text"** invites an encrypted-secrets
  column. The stronger and simpler rule is that no per-tenant secret exists at all.
- **Aggressive caching of routing info without a staleness bound** is how a suspended tenant keeps
  serving traffic. Every cache here has a stated maximum staleness and a stated behaviour when the
  backing store is unreachable.
- **Layer names.** "Data / service / web layer" is fine as prose; distribution folders should name
  what they contain (`Entities/`, `Services/`, `Endpoints/`), never a layer.
- **Extending the registry to blob storage or Key Vault** is the wrong layer: per-tenant locations
  derive from the tenant id, per-tenant credentials do not exist, one `TokenCredential` reaches every
  store. **Extending it to provisioning** is also wrong: provisioning needs DDL rights the web app must
  never hold; the web app records intent and triggers a job.
- **The MCP question** dissolves once the authorization model is in view: MCP requires an RFC 8707
  `resource` equal to the server URI and an audience-bound token, so a per-tenant endpoint gives
  per-tenant audiences for free and one endpoint per distribution would put tenant selection inside
  every tool call.

### 1.3 Misalignments with ARCHITECTURE.md and the frozen specs

- ARCHITECTURE.md's "reuses `Tellma.Core`'s sharding code unchanged": no such code exists;
  `Tellma.Core.csproj` references only `Tellma.Core.Abstractions` today. The catalog is written fresh.
- The layout tree says `samples/tellma-sample-distribution/`; the phasing text says
  `distributions/<slug>/`; neither folder nor `taxonomy.json` exists.
- ARCHITECTURE.md says a distribution on the shared authority references none of the identity
  projects; the reference distribution must run with no shared services locally (Guiding Principles;
  spec 0003 §10.4), so it references the engine and selects in-proc mode by configuration.
- Spec 0008 §10.6 defines `today()` as the current date in the *tenant's* zone; the brain dump's web
  section wants the client to assert today. The frozen spec wins (D9).
- The control-plane client is deliberately never granted a distribution's origin as a resource
  (`TellmaClientProperties.CallsDistributionApis`), so the admin contract cannot bind its tokens to
  one distribution today (D20).

---

## 2. Decisions

### D1 — Package topology: `Tellma.Core` composes, `Tellma.Core.AspNetCore` serves, `Tellma.Core.Migrator` migrates, `Tellma.Core.Abstractions` names

| Package | References | Owns from this theme |
|---|---|---|
| `Tellma.Core.Abstractions` (existing; no package references) | — | `TenantCategory`, `TenantState`, `TenantDescriptor`, `RequestContext`, `IRequestContextAccessor`, `RequestContextSnapshot`, `PrincipalKind`, `ITenantMembershipDirectory`, `ITenantStateListener`, `ISessionTerminationListener`, `ITenantProvisioningStep`, the feature contract (`ITellmaFeature`, `FeatureDeclaration`, `FeatureContribution`, `FeatureContributionItem` and the two built-in items), `IStartupCheck`, `CompositionProblem`, `TellmaCompositionException`, the theme's exceptions, `TenancyTelemetryNames` |
| `Tellma.Core` (existing, grows) | `Tellma.Core.Abstractions`, `Tellma.Core.Queryex`, `Tellma.Core.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.Extensions.*` | `AddTellma` and the composition pipeline, `TellmaBuilder`, `IContributionRealizer<T>`, `TellmaStartupGate`, `CatalogDbContext` and its entities, `ITenantRegistry`, `ITenantCatalog`, `ITenantConnectionFactory`, `ITenantConnectionProvider`, `ITenantDbContextFactory`, `IRequestContextHolder`, `ITenantScopeFactory`, `TenantSandboxContext`, `TellmaDbContext` |
| `Tellma.Core.AspNetCore` (new) | `Tellma.Core`, `FrameworkReference Microsoft.AspNetCore.App`, `Microsoft.AspNetCore.Authentication.OpenIdConnect`, `Microsoft.AspNetCore.Authentication.JwtBearer`, the two pinned `Azure.Extensions.AspNetCore.DataProtection.*` packages | `AddTellma(WebApplicationBuilder)` / `UseTellma` / `MapTellma`, the BFF (cookie + OIDC + session store + refresh + back-channel receiver), CSRF, tenant middleware and access filter, the endpoint audit, route groups, `/api/distribution-info`, the admin surface, health, Data Protection and forwarded-headers configuration, `HostTelemetryNames` |
| `Tellma.Core.Migrator` (new) | `Tellma.Core`, `Tellma.Core.EntityFrameworkCore.Design`, `Microsoft.EntityFrameworkCore.Design` | `TellmaMigrator.RunAsync`, the platform-owned catalog migrations (`catalog.__EFMigrationsHistory`), `TellmaDesignTimeDbContextFactory<TContext>`, the commands of D22 |

`Tellma.Core` gains the edge to `Tellma.Core.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.SqlServer`
(forced regardless of this theme: the Queryex schema adapter, metadata-driven TVP binding and the
catalog context all need EF). `Tellma.Core` takes **no** ASP.NET dependency, so the migrator and any
worker host compose it without the web stack, and Core services are testable without `HttpContext`.
The feature contract lives in `Tellma.Core.Abstractions` because ARCHITECTURE.md's dependency rule 2
forbids any `Tellma.Module.<m>` → `Tellma.Core` edge and a module must be able to ship a feature.

**Rejected.** A `FrameworkReference` inside `Tellma.Core` (pollutes every non-web host). A separate
`Tellma.Core.Composition` package carrying DI/EF/ASP.NET references for modules to reference (a new
Module → non-Abstractions edge the dependency graph forbids). A separate `Tellma.Core.Migrations`
package (one package carrying engine and catalog migrations is enough; the migrator project is the
only consumer). Distribution-generated catalog migrations (the catalog schema is platform-fixed;
`Tellma.Identity.Migrations` is the precedent for a platform-owned migrations assembly).

**Confidence.** High on `Tellma.Core.AspNetCore` and the Abstractions-only contract; medium on one
migrator package.

### D2 — The reference distribution lives at `distributions/acme/`, slug `acme`, two source projects and two test projects

```
distributions/acme/
├── README.md
├── src/
│   ├── Tellma.Distro.Acme.Web/
│   │   ├── Program.cs                          # six lines (D4)
│   │   ├── AcmeComposition.cs                  # Slug + Compose(TellmaBuilder); shared with the migrator
│   │   ├── Entities/                           # sealed leaves and distro-only entities (empty at first)
│   │   ├── Services/                           # custom services and validators (empty at first)
│   │   ├── Endpoints/                          # custom endpoints (empty at first)
│   │   ├── Properties/launchSettings.json      # tracked in Phase 1 (fixed ports); template scheme arrives with the CLI
│   │   ├── appsettings.json / appsettings.Development.json
│   │   ├── wwwroot/                            # SPA shell, added with the UI phase
│   │   └── Tellma.Distro.Acme.Web.csproj       # → Tellma.Core, Tellma.Core.AspNetCore, Tellma.Core.Email, Tellma.Core.Webhooks,
│   │                                           #   Tellma.Identity (in-proc), Tellma.Module.Gl (+ .Abstractions); never a Design package
│   └── Tellma.Distro.Acme.Migrator/
│       ├── Program.cs                          # one line (D4)
│       ├── AcmeDesignTimeFactory.cs            # derives TellmaDesignTimeDbContextFactory<TellmaDbContext>; five lines
│       ├── Migrations/                         # tenant-model migrations + snapshot, generated by dotnet ef
│       └── Tellma.Distro.Acme.Migrator.csproj  # → Web project, Tellma.Core.Migrator
└── test/
    ├── Tellma.Distro.Acme.Web.Tests/           # composition parity, endpoint audit, one-way dependency, no tracked secrets
    └── Tellma.Distro.Acme.IntegrationTests/    # Category=Integration: migrate + provision + sign-in through in-proc identity on Testcontainers
```

Namespace and project prefix `Tellma.Distro.Acme`; `DeploymentIdentity("acme", environment)`; OIDC
`client_id = acme`. `acme` is added to the reserved-slug list as *taken by the platform*. The
distribution is deployable (`acme.app.tellma.com` is the platform's smoke deployment); a reference
that is not deployed drifts. The Migrator references the Web project (the shape ARCHITECTURE.md
prescribes); if an executable-to-executable reference fights the SDK, the fallback is a third project
`Tellma.Distro.Acme` (class library holding `AcmeComposition` and `Entities/`) referenced by both —
neither `Program.cs` changes.

`taxonomy.json` is created at the repo root by this spec: `modules: []` (T8 adds `Gl`),
`compliance: []`, `reservedSlugs` (ARCHITECTURE.md's list plus `acme`), `distributions: ["acme"]`. A
unit test in `Tellma.Core.Tests` asserts every `distributions/<slug>/` folder is listed, every listed
slug is valid (lowercase, starts with a letter, ≤ 15 characters), and no distribution slug is in
`reservedSlugs` except through `distributions`.

**Rejected.** `samples/` (a sample is copied, not deployed); `reference`/`sample`/`demo` as slugs
(reserved, or a role rather than a name); `banan` (customer-zero is a real business and should be
its own distribution); a flat layout without `src/`/`test/` (would be reorganised at graduation).

**Confidence.** High on location and project set; medium on the slug.

### D3 — Layer vocabulary stays "data / service / web"; folders are `Entities/`, `Services/`, `Endpoints/`

The prose vocabulary of ARCHITECTURE.md stands. Inside the Web project the folders are named for the
artefacts a distribution adds. There is no `Data/` folder because the distribution owns no data-access
code (no `DbContext` by default, no repository, no SQL), and no per-feature folder structure (most
entities have neither a custom service nor a custom endpoint). Clean-Architecture names invite an
agent to create the interfaces and repositories the platform has already decided against.

**Confidence.** High.

### D4 — Three calls in the web host, one in the migrator; one composition method shared by both

```csharp
// Illustration — distributions/acme/src/Tellma.Distro.Acme.Web/Program.cs
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddTellma(AcmeComposition.Slug, AcmeComposition.Compose);
WebApplication app = builder.Build();
app.UseTellma();
app.MapTellma();
app.Run();
```

```csharp
// Illustration — AcmeComposition.cs
public static class AcmeComposition
{
    public const string Slug = "acme";
    public static void Compose(TellmaBuilder tellma)
    {
        tellma.AddFeature<GlFeature>();                       // Tellma.Module.Gl (T8)
        tellma.Services.AddTellmaEmail();                     // spec 0007: every host composes email explicitly
        tellma.Services.AddSmtpEmail(tellma.Configuration);
    }
}
```

```csharp
// Illustration — Migrator/Program.cs
return await TellmaMigrator.RunAsync(args, AcmeComposition.Slug, AcmeComposition.Compose);
```

`AddTellma(WebApplicationBuilder, slug, compose)` (in `Tellma.Core.AspNetCore`) calls
`services.AddTellma(slug, configuration, environment, compose)` (in `Tellma.Core`, the host-agnostic
composition root of D6) and then `AddTellmaAspNetCore(configuration)`: the authentication schemes and
policies (D15–D17), the session store, CSRF options, Data Protection and forwarded headers (D21),
the endpoint audit and health checks, Serilog from the `Serilog` section, OpenTelemetry (the platform
meters and activity sources, ASP.NET Core, HttpClient and SqlClient instrumentation; Azure Monitor
gated on `APPLICATIONINSIGHTS_CONNECTION_STRING`, as the identity host does), and — when
`Tellma:Identity:Mode = InProc` — `AddTellmaIdentity` (D19).

`UseTellma()` installs the pipeline in this fixed order and nothing else: forwarded headers (guarded
as the identity host guards them), Serilog request logging, the exception handler producing RFC 9457
problem details (the exception-to-status mapping is T6's), HSTS and HTTPS redirection outside
Development, `UseRouting`, `UseTellmaIdentity` (in-proc only), `UseAuthentication`, `UseAuthorization`,
the CSRF middleware (D17), the tenant middleware (D11). Custom middleware goes between `UseTellma()`
and `MapTellma()`.

`MapTellma()` maps: `/api/distribution-info`, `/health/live`, `/health/ready`, `/bff/*`,
`/api/admin/*`, `/api/webhooks/{key}` (when `Tellma.Core.Webhooks` is composed), the in-proc identity
endpoints, `/.well-known/oauth-protected-resource/{tenantId:int}/mcp`, the tenant route groups (D12)
filled by feature contributions (T6's projected endpoints, T10's hub, T6's MCP endpoint, T7's blob
endpoints), static assets and the SPA fallback (`MapFallbackToFile("index.html")` when
`wwwroot/index.html` exists, excluding every reserved prefix). It returns `TellmaEndpoints` so a
distribution can map custom endpoints onto `tellma.Web` and inherit every filter.

**Rationale.** Pipeline order is the thing agents get wrong (the identity implementation record notes
`UseRouting` before `UseTellmaIdentity` as a hard-won fact); encapsulating it removes the class of
bug while the two seams (custom middleware, custom endpoints) keep the escape hatch.

**Confidence.** High.

### D5 — Tenant identifier: immutable positive `int`, tenant-first route prefix `/{tenantId:int:min(1)}`

| Surface | Route | Scheme |
|---|---|---|
| Web API (SPA) | `/{tenantId:int:min(1)}/api/web/...` | session cookie |
| Public API | `/{tenantId:int:min(1)}/api/v1/...` | bearer (seam; T6, not shipped) |
| MCP | `/{tenantId:int:min(1)}/mcp` | bearer (D18) |
| SignalR hub | `/{tenantId:int:min(1)}/hub` | session cookie (T10) |
| Blobs | `/{tenantId:int:min(1)}/blobs/...` | session cookie, GET allowed (T7) |
| SPA deep links | `/{tenantId:int:min(1)}/{**path}` | anonymous; serves the app shell |

Tenant ids are allocated from `catalog.sq_Tenants`, never reused, never renamed; there is no tenant
slug. Every tenant-less route starts with a non-numeric segment (`/bff`, `/api`, `/signin-oidc`,
`/signout-callback-oidc`, `/health`, `/id`, `/.well-known`), so the integer constraint alone
guarantees platform routes and tenant routes never shadow each other. Enumerability is not a security
property: membership is verified per request and a non-member gets the same 404 as a non-existent
tenant.

**Rejected.** Slugs (immutability becomes a rule instead of a type property; reserved-word management
appears twice); GUIDs (leak into every URL for no gain); host-based tenancy (one certificate, one DNS
record and one redirect-URI registration per tenant); `/api/web/{tenantId}` (breaks the symmetry
across web, MCP, hub and blobs, and the MCP path-inserted PRM prefers a prefix).

**Confidence.** High.

### D6 — Feature composition at minimal fidelity: a BCL-only contract in Abstractions, `Requires` edges, two aggregated gates

`ITellmaFeature` (§3.1) has a stable `Name`, `Declare(FeatureDeclaration)` and
`Contribute(FeatureContribution)`. A feature *declares* edges (`Requires<TFeature>()`, or the sugar
`[Requires<TFeature>]`) and options bindings (`Options<TOptions>(configurationPath)`), and
*contributes* data-only items (`FeatureContributionItem` records): service registrations by type,
EF model contributors by type, nested features, and the item types other themes define (T5's
`StackContributionItem`, T4's `SecurableContributionItem`, T8's `SeedContributionItem`, T6's endpoint
mapper item, T10's schedule item). `Tellma.Core` realises every item through
`IContributionRealizer<TItem>`; an item type no realizer handles is a composition problem (a pack
newer than Core). No package but `Tellma.Core` ever sees `IServiceCollection` or `ModelBuilder`
inside a feature, which is what keeps Abstractions package-free and modules free of `Tellma.Core`.

`services.AddTellma(slug, configuration, environment, compose)` runs:

1. **Collect.** Registers `DeploymentIdentity(slug, environment.EnvironmentName)`; adds the built-in
   `CoreFeature` (users, roles, permissions, settings, cache — T3/T4/T8's content) unconditionally;
   constructs `TellmaBuilder` and runs `compose`. Selection is explicit (`AddFeature<T>()`); no
   assembly scanning. A pack ships its feature class; the distribution writes
   `tellma.AddFeature<GlFeature>()` (a pack cannot ship an `AddGl(this TellmaBuilder)` extension
   without referencing `Tellma.Core`).
2. **Declare.** Each feature's `Declare` records name, edges and options.
3. **Validate (graph gate).** Missing `Requires` targets, duplicate names, cycles, a type added
   twice with different instances, an option type bound twice to different paths — every problem
   aggregated into one `TellmaCompositionException` thrown from `AddTellma`, before `Build()`.
4. **Realise.** Contribute in topological order (dependencies first, so a dependent feature's
   registration of the same service type overrides its dependency's — the "distro replaces a pack
   service" mechanism); realise items; register the catalog context, registry, connection factories,
   the per-tenant `DbContext` factory (D9), the context holder, `ISandboxContext` (D10), the options
   with `ValidateOnStart`, and `TellmaStartupGate` as the **first** hosted service.
5. **Startup (realised gate).** `TellmaStartupGate` (`IHostedLifecycleService.StartingAsync`) runs
   every registered `IStartupCheck` inside one scope and throws one `TellmaCompositionException` with
   every problem before the host serves traffic. Built-in checks: options valid; every model
   contributor implements EF's `IEntityTypeConfiguration<>`; every stack's service type constructible;
   `ISandboxContext` and `DeploymentIdentity` resolve and `DeploymentIdentity.Application == slug`;
   ICU globalization mode active (invariant mode breaks T3's calendars); no configuration value under
   `Tellma:` starts with `@Microsoft.KeyVault(`; connection-profile rules (D8); Data Protection
   configured outside Development (D21); the endpoint audit (D16); T4's securables consistency check.
   `TellmaComposition.Validate(slug, configuration, compose)` runs steps 1–3 host-free for tests.

`Recommends`, `Excludes`, slot cardinality, providers, the manifest source generator, the Builder
tool and the bypass analyzer are ARCHITECTURE.md's target and are not built; `FeatureDeclaration`
reserves nothing for them — adding them later is additive.

**Rejected.** `Contribute(IServiceCollection, ModelBuilder)` (forces DI and EF packages into
Abstractions); reflection-based discovery (hides the selection site; AOT-hostile); failing startup on
a transiently unreachable catalog (D7 handles that through readiness).

**Confidence.** High on the shape; medium on records-plus-realizers versus an interface per item.

### D7 — One catalog database per distribution, always; schema `catalog`; single-live versus multi-live is a registration policy

Every distribution has exactly one catalog database (SaaS `tellma-<slug>-catalog`; Development
`<Tellma:Sql:DatabasePrefix>.catalog`, default prefix `Tellma.dev.<slug>`), reached through
`Tellma:Catalog:ConnectionString`, migrated by the migrator before any tenant database. Schema
`catalog` holds `Tenants`, `TenantMemberships`, `Sessions`, `CatalogState` (§4) and the
`TenantMembershipList` table type. In in-proc identity mode the identity engine's `idsvr` schema
also lives in this database (its `ConfigureDbContext` hook is pointed at the catalog connection; it
keeps its own migration history in that schema).

`Tellma:Tenancy:RegistrationPolicy` ∈ `SingleLive | MultiLive` (default `SingleLive`) governs the
write side: under `SingleLive` a second `Live` registration is refused, sandboxes are always
registrable and must name their `LiveTenantId`; under `MultiLive` any registration is allowed. A
startup check fails when a `SingleLive` catalog holds more than one live tenant. The catalog holds
**no connection strings and no secrets**: a row carries `Server`, `Database` and a
`CredentialProfile` name (D8).

**Rationale.** One store means one code path for the registry, the migrator fan-out, the session
store, the membership hint and the in-proc identity store; a restore of any tenant database never
changes which tenants exist. The cost is one small database (a pool admits 500) and one pooled
connection string (`Max Pool Size=10`). The brain dump's own requirement that sandboxes can be
created makes "single-live has no programmatic registration" false — only *live* registration is
policy-gated.

**Rejected.** A catalog table inside the live database (topology inside tenant data; the live backup
carries the sandbox list; nowhere to keep sessions and `idsvr` when five sandboxes exist); a
configuration-backed store (read-only; suspension state must change at runtime); co-locating the
`catalog` schema in the live database by pointing the connection string at it (works with no extra
code, but adds a two-contexts-one-database test matrix and the restore hazard — recorded as a review
flag for on-prem single-customer installs).

**Confidence.** High.

### D8 — Connection resolution: credential profiles in configuration, locations in the catalog, no per-tenant secrets, bounded pools

`Tellma:Sql:Profiles:<name>` holds SqlClient connection-string fragments; the default profile is
`default`. A tenant's string is composed once per `(tenantId, row version)` by `TenantConnectionFactory`
with `SqlConnectionStringBuilder` (never concatenation): the profile fragment, then `Data Source` =
row `Server`, `Initial Catalog` = row `Database`, `Application Name` = `DeploymentId`. The row may
not carry `;` or `=` in either column (CHECK constraint); the composed string never overrides
`Encrypt`, `Authentication` or any credential keyword from the row.

| Deployment | `default` profile (web app) | Migrator identity |
|---|---|---|
| SaaS (Azure SQL) | `Authentication=Active Directory Managed Identity;User Id=<client id>;Encrypt=True` | the job's own managed identity (DDL) |
| On-prem Windows | `Integrated Security=true;Encrypt=True` (service account) | a DDL service account |
| On-prem Linux | `User Id=tellma_app;Password=<host-injected>;Encrypt=True` | `User Id=tellma_ddl;Password=…` |
| Development | `Integrated Security=true` on LocalDB, or the container's `sa` | same |

Startup checks: every profile sets `Max Pool Size` ≤ `Tellma:Sql:MaxPoolSizePerTenant` (default 20)
and `Min Pool Size` = 0 or absent; `Encrypt` present outside Development; no `Password=`/`Pwd=`
outside Development unless `Tellma:Sql:AllowSqlPassword = true`; no `@Microsoft.KeyVault(` literal.
Pool arithmetic the spec states as an invariant: `T` active tenants × `I` instances × `P` per-tenant
pool size bounds the sessions held against the elastic pool's 30,000-session and per-pool login
limits; `P = 20` keeps 200 tenants × 3 instances at 12,000 and relies on SqlClient's 4–8-minute idle
drain so idle tenants hold nothing. T10's scheduler staggers per-tenant work; the migrator's fan-out
parallelism is bounded (default 4).

**Per-tenant EF access.** `ITenantDbContextFactory` keeps one `PooledDbContextFactory<TellmaDbContext>`
per tenant (`poolSize` 16), created lazily with that tenant's composed string, cached by tenant id,
dropped when the registry reports a location change. All factories share EF's internal service
provider and the compiled model (the SQL Server options extension's `ShouldUseSameServiceProvider`
compares engine type and compatibility levels only, and the relational extension's
`GetServiceProviderHashCode` returns 0 — verified against the EF Core source checkout). The scoped
`TellmaDbContext` a handler may inject is created from the ambient tenant's factory and returned to
that tenant's pool at scope end. `Database.SetConnectionString` is never used. The batch executor (T2)
opens `SqlConnection`s through `ITenantConnectionProvider` (ambient tenant) and never receives a
connection string; the migrator and reconcilers use `ITenantConnectionFactory` with an explicit id.

**The application role.** Every tenant database has a database role `tellma_app`, created by the
migrator on first migration and re-granted idempotently after every migrate run from the model:
`SELECT, INSERT, UPDATE, DELETE` on every schema the tenant model maps, `UPDATE` on every
`sq_<Table>` sequence (`sp_sequence_get_range` needs it), and `EXECUTE ON TYPE` for every table type
through spec 0001's configured grant principals (`AddTellma` sets `tellma_app` as the default
principal set). The web app's principal — the managed identity's contained user in SaaS, the login's
user on-prem — is a member of `tellma_app`; the migrator's principal owns the database. The role name
is a platform constant, so migrations never carry an environment-specific principal name. This
answers ARCHITECTURE.md's open question on the ad-hoc-SQL permission set.

**Secrets policy** (binding on every host):

- No credential, key or token is stored in `catalog.*` or in any tenant table, in `appsettings*.json`
  checked into a repository (a test in the reference distribution scans for `Password=`,
  `ClientSecret` values), in logs (the factory redacts before logging) or in telemetry tags.
- SaaS tenant databases have no passwords at all: provisioning runs
  `CREATE USER [<web app identity>] FROM EXTERNAL PROVIDER` and adds it to `tellma_app`.
- Configuration values that must be secret (an on-prem SQL password, `Tellma:Identity:ClientSecret`,
  `Tellma:SignalR:ConnectionString`) arrive through configuration providers: App Service Key Vault
  references or `Azure.Extensions.AspNetCore.Configuration.Secrets` (`AddAzureKeyVault`,
  `ReloadInterval` 15 min) in SaaS; environment variables or a key-per-file directory
  (`Microsoft.Extensions.Configuration.KeyPerFile`, fed by Docker/Kubernetes secrets or systemd
  `LoadCredential`) on-prem; user secrets in Development.
- Windows DPAPI is not used anywhere (Windows-only; every script must run on Linux).
- No per-tenant secret exists in this release; when a tenant-specific integration needs one it is a
  tenant-database column protected with Data Protection behind an `ITenantSecretStore` seam defined
  then. `catalog.Tenants.Properties` is a non-secret, distribution-defined JSON bag by rule and may
  hold the *name* of such a secret.
- `Microsoft.Data.SqlClient` moves to 6.1.6 (EF SqlServer 10.0.11 requires ≥ 6.1.6; T2 needs it for
  hierarchyid); Entra modes stay bundled on 6.x. When the provider moves to 7.x,
  `Microsoft.Data.SqlClient.Extensions.Azure` is added in the same bump or every Entra mode fails at
  runtime.

**Rejected.** A full connection string per row (a compromised row points the app at an attacker's
server with an attacker's credential); one global string with `Initial Catalog` substituted (breaks
the first tenant promoted to a dedicated server); per-tenant passwords in Key Vault (hundreds of
secrets, hundreds of GETs per cold start, 24-hour staleness, no benefit); `Active Directory Default`
in production (the docs warn about its latency; it is the developer profile); scoped
`AddDbContext` with per-scope options (works, but the pooled factory is the only shape EF documents
for per-tenant databases and T2's hot path wants pooled contexts); `Max Pool Size` left at 100.

**Confidence.** High.

### D9 — The registry is an in-memory snapshot invalidated by a 15-second version poll, with a staleness bound

`TenantRegistry` (singleton, `Tellma.Core`) holds an immutable dictionary of `TenantInfo` by id plus
the catalog `Version` it was loaded from. `Find`/`Get` are lookups with no I/O. A hosted refresh loop
(`PeriodicTimer` over `TimeProvider`, `Tellma:Catalog:RefreshInterval` default `00:00:15`) runs
`SELECT [Version] FROM [catalog].[CatalogState] WHERE [Id] = 1` and reloads the full snapshot (one
`SELECT` over `catalog.Tenants`) only when the version differs (`!=`, never `<`, so a restored backup
with an older stamp still reloads). Every catalog write runs in one transaction that also sets
`CatalogState.Version = NEWID()`, then refreshes the local registry, so the writing instance is
consistent immediately and every other instance converges within one interval. Refresh is
single-flight. After a reload that changed descriptors, and after a local write, the registry calls
every `ITenantStateListener` with the changed tenants (T10 closes a suspended tenant's connections
and pauses its jobs; the connection factory drops cached strings for relocated tenants).

**Staleness bound.** A refresh that fails keeps serving the snapshot for up to
`Tellma:Catalog:MaxStaleness` (default `00:05:00`) at Warning; beyond that every tenant request
answers `503 catalog_unavailable` and `/health/ready` reports unhealthy. A suspension therefore takes
effect immediately on the instance that received it and within 15 s fleet-wide, and a catalog outage
can never turn into a permanently ignored suspension.

**Startup.** The first snapshot load is attempted with a 10-second bound; on success the migration
history is compared with the platform's expected latest catalog migration — a behind or missing
catalog is a startup failure whose message names the exact migrator command (in Development, a
missing catalog database prints `dotnet run --project … Migrator -- migrate`); the web app never
migrates. On a *reachability* failure the host starts, `/health/ready` reports not-ready, and the
loop keeps retrying — a transient outage during a slot swap must not crash-loop every instance.

**Rejected.** A per-request catalog read; a full-table refresh on every tick (works, but a single-row
version read is cheaper for hundreds of rows); per-tenant lazy refresh (unknown-id negative caching
and per-entry timers for the same bound); `SqlDependency`/query notifications; `rowversion` as the
stamp (bumps on every row update including hint writes); a monotonic `bigint` (restore-then-increment
can collide with a value an instance already saw).

**Confidence.** High. Review flag on the interval.

### D10 — Tenant states, the suspension hook, and the sandbox context

`TenantState` ∈ `Provisioning | Active | ReadOnly | Suspended | Retired`. `Category` is immutable
after provisioning; a sandbox is a separate row whose `LiveTenantId` names its live tenant.

| State | Read request | Mutating request | Background scope (`ITenantScopeFactory`) |
|---|---|---|---|
| `Provisioning` | `503 tenant_provisioning`, `Retry-After: 30` | same | throws `TenantUnavailableException` |
| `Active` | served | served | served |
| `ReadOnly` | served | `403 tenant_read_only` | scope created; T10 runs read-only handlers only |
| `Suspended` | `403 tenant_suspended` | same | throws |
| `Retired` | `404 tenant_not_found` (indistinguishable from absent) | same | throws |

"Mutating" is endpoint metadata (`TenantEndpointMetadata.IsMutation`) stamped by T6's projection from
the securable's action kind and by `.WithMutation(bool)` on custom endpoints; the audit (D16) refuses
a tenant endpoint without it. Verdicts are returned only after authentication succeeded (`401` first).

`ITenantCatalog.SetStateAsync(tenantId, state, reason, actor)` is the **suspension hook**: it validates
the transition (`Active ⇄ ReadOnly`, `Active|ReadOnly → Suspended`, `Suspended → Active`,
`any → Retired`; `Retired` is terminal), writes `State`, `StateReason`, `StateChangedAt`,
`StateChangedBy`, `ModifiedAt`, bumps `CatalogState.Version`, refreshes the local registry and
notifies `ITenantStateListener`s. The control plane reaches it through the admin surface (D20); the
migrator through `set-state`.

`TenantSandboxContext : ISandboxContext` (scoped, registered by `AddTellma`) returns
`RequestContext.Tenant?.Category == Sandbox` and **throws** `InvalidOperationException` when no tenant
is bound: a side-effecting connector called outside a tenant scope is a bug that must surface on the
first call rather than leak (unbound treated as live) or hide behind "mail not delivered" (unbound
treated as sandbox). Deployable work that legitimately sends mail (the in-proc identity engine's
invitation mail) runs in the identity engine's own composition, which registers `SandboxContext.Never`
for its scope. The invariant that makes this safe is structural: every tenant-routed endpoint passes
the tenant middleware (the audit refuses any that would not) and every background scope is created
by the factory.

**Confidence.** High on the state table; medium on `ReadOnly` semantics for background work (T10's
call — the descriptor exposes the state).

### D11 — Tenant resolution is middleware; tenant *access* is one service called from two front doors

`TenantMiddleware` (`Tellma.Core.AspNetCore`, after `UseAuthorization`) reads `tenantId` from the
endpoint's route values. No route value → the holder stays at `RequestContext.Deployable` and the
request proceeds (tenant-less surfaces). A route value → `ITenantRegistry.Find` (no I/O); unknown or
`Retired` → `404 tenant_not_found`; a state verdict per D10; otherwise the holder is set with the
tenant part, the principal part (`Kind`, `Subject`, `ClientId`, `SessionId`, `Assurance` from the
authenticated user), `Now` from `TimeProvider`, and the request `Activity` is tagged
`tellma.tenant.id`. Placement after authorization means an anonymous probe gets `401` before it can
learn whether a tenant id exists, and binding before the endpoint means a `TellmaDbContext` or any
tenant-scoped service injected as a handler argument already sees the tenant.

`ITenantAccessGuard.EnsureAccessAsync(TenantAccessRequirement)` is the single place that decides
whether the bound principal may proceed against the bound tenant; two front doors call it —
`TenantAccessFilter` (an endpoint filter on every tenant route group) and the MCP request filters
(`AddCallToolFilter`/`AddListToolsFilter`, T6; the MCP SDK maps `RequestDelegate` endpoints that
endpoint filters do not reach). It runs the ordered `IRequestContextInitializer`s:

1. T4's connect step (`Order` 100): resolves `Subject`/`ClientId` to the tenant user (`UserId`,
   active flag), stamps activity, reads the version tags. `NotAMember` and `Deactivated` both answer
   `404 tenant_not_found` — a non-member cannot distinguish "no such tenant" from "not yours". T4 may
   answer from its per-tenant cache and attach the verification statement to the first batch (seam
   16); a failed verification makes the executor throw `TenantNotFoundException` so the optimistic
   path fails closed.
2. T3's locale negotiation (`Order` 200): fills `Language`, `Culture`, `Calendar`, `TimeZone`,
   `TenantTimeZone`, `Today`.
3. Assurance: if the endpoint carries `RequireAssuranceMetadata` and the session's `acr` or its
   evidence time is insufficient → `InsufficientAssuranceException` → T6 maps to the `401` challenge
   of spec 0003 §9.3 (`StepUpChallenge.Write` in `Tellma.Core.AspNetCore` writes the header).

Each initializer returns a new immutable `RequestContext`; the guard sets the final one. After
`EnsureAccessAsync` returns, `UserId` is set or the request was refused — that is the whole contract
T5 relies on.

**Confidence.** High.

### D12 — Route groups the platform owns; features and distributions fill them

`MapTellma()` builds and returns `TellmaEndpoints`:

- `Web` = `/{tenantId:int:min(1)}/api/web`, policy `Tellma.Web`, `AddEndpointFilter<TenantAccessFilter>()`,
  metadata `TenantEndpointMetadata(Surface: Web)`, JSON conventions of T6;
- `Api` = `/{tenantId:int:min(1)}/api/v1`, policy `Tellma.Api` (seam; nothing mapped in this release);
- `Hub` and `Blobs` sibling groups on the cookie policy, built the same way for T10 and T7;
- the MCP endpoint `/{tenantId:int:min(1)}/mcp` on policy `Tellma.Mcp` when T6's MCP feature is composed;
- `Deployable` = `/api` for tenant-less endpoints a distribution adds; each must call
  `.AsDeployableEndpoint("reason")`.

T6's projection and a distribution's custom endpoints (`tellma.Web.MapPost(...)`) inherit
authorization, the access filter and the surface metadata with no per-endpoint code. A tenant-path
endpoint mapped outside these groups is an audit violation (D16).

**Confidence.** High.

### D13 — Request context: one immutable record in a scoped holder, two writers, one accessor; no `AsyncLocal`

`RequestContext` (§3.1) is an immutable record: `Tenant` (`TenantDescriptor?`), `Kind`
(`PrincipalKind`), `Subject`, `ClientId`, `SessionId`, `Assurance`, `UserId` (`int?`, late-bound),
`Language`, `Culture`, `Calendar`, `TimeZone`, `TenantTimeZone`, `Now`, `Today`, `OriginTraceParent`.
`IRequestContextAccessor.Current` (Abstractions) is how every service reads it;
`IRequestContextHolder.Set` (`Tellma.Core`, platform-internal) is how it is written. The holder is
scoped; nothing stores the context in an `AsyncLocal`, a static or a singleton; singletons take
`IServiceScopeFactory` and open a scope. `Activity.Current` carries `tellma.tenant.id` as a
diagnostics mirror only.

Exactly two writers exist: `TenantMiddleware` + `ITenantAccessGuard` for requests (D11), and
`ITenantScopeFactory.CreateScopeAsync(RequestContextSnapshot)` for background work — it re-resolves
the tenant from the registry (state may have changed; non-`Active` handling per D10), creates an
`AsyncServiceScope`, sets the holder from the snapshot, and starts an `Activity` *linked to* (never
parented by) `OriginTraceParent`. T10's runner and the migrator's provisioning steps are its callers;
a snapshot with `Kind = System` binds the reserved system user T4 defines. `RequestContextSnapshot`
is the serialisable copy (tenant id, kind, subject, client id, user id, language, culture, calendar,
time-zone id, trace parent) that anything enqueuing work stores in the job row as columns, never as
a serialised object.

`TenantDescriptor` exposes `Id`, `Name`, `Category`, `State`, `LiveTenantId` — never the database
location, which stays inside `Tellma.Core` (`TenantInfo`).

**`today()` binds to `RequestContext.Today`, computed in the tenant's time zone** (`TenantTimeZone`,
from tenant settings) as spec 0008 §10.6 defines; `TimeZone` (the user's, from the platform time-zone
header or the user's preference) formats instants in messages and exports. There is no client
"today" header: a client cannot move the tenant's business date, and `PostingDate = today()` must
mean the same thing for every user of a tenant.

Locale precedence, resolved by T3's initializer: request headers (`Accept-Language`, `Tellma-Calendar`,
`Tellma-Time-Zone`; T6 ratifies the names) → the user's stored preferences → the tenant's defaults →
platform defaults (`en`, `gc`, `UTC`). Culture names are stripped of `-u-` extensions before
negotiation; the calendar is never encoded in the culture name.

**Rejected.** Three separate interfaces over one holder (fine, but a single immutable record makes
initializers pure functions and "where did this tenant come from" answerable by reading two classes);
a mutable context services can patch; `IHttpContextAccessor` in Core services; a client-asserted
`Today`.

**Confidence.** High on the mechanism; medium on the `today()` position (review flag).

### D14 — Membership directory: a catalog hint table written post-commit, reconciled by the migrator, never an authorization source

`catalog.TenantMemberships(Subject, TenantId, IsActive, UpdatedAt)` behind `ITenantMembershipDirectory`.
Writers: T8's user service after commit of an invite/activate/deactivate (a second transaction against
the catalog; a failure is logged and metered, never surfaced); the migrator's provisioning (the
bootstrap admin) and its per-tenant reconciliation on every `migrate`/`provision` run, from
`core.Users(Subject, IsActive)` through the `[catalog].[TenantMembershipList]` table type; a T10
nightly reconciliation may be added later with the same statements. Readers: `GET /bff/user` (the
company picker), T6's tenant-listing MCP tool. The directory answers within one catalog query and
never opens a tenant database. Every entry into a tenant is re-verified by the connect step, so a
stale hint can at most show a tenant that answers 404 or hide one reachable by URL until the next
deploy.

**Rejected.** Fan-out at sign-in (N logins against the pool; an authorization decision from a
listing); an authoritative membership table in the catalog (two sources of truth for access
control; tenant databases must stay self-contained for backup and on-prem delivery); consulting the
identity server (it holds no tenant membership by design); self-healing writes from the connect
step (a catalog write on T4's hot path for a case the deploy-time reconciliation already covers —
review flag).

**Confidence.** High.

### D15 — Distribution-side authentication: BFF cookie with a catalog-backed session store, refresh-anchored liveness, back-channel logout, per-surface schemes

`AddTellmaAspNetCore` registers:

- **Cookie scheme `Tellma.Session`**: cookie `__Host-tellma.session` (`tellma.session` under plain
  HTTP in Development), `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, sliding
  `Tellma:Session:IdleLifetime` (default 7 days, equal to the authority's refresh-token idle window),
  absolute `Tellma:Session:AbsoluteLifetime` (default 90 days), `SessionStore = CatalogSessionStore`,
  `EventsType = TellmaSessionEvents`. The cookie carries only a 256-bit random session key
  (43-character base64url); the ticket — principal, `sid`, tokens — is `TicketSerializer` output
  protected by an `IDataProtector` (purpose `Tellma.Core.AspNetCore.Session`) in `catalog.Sessions`.
  `RetrieveAsync` consults a per-instance bounded `MemoryCache` (TTL `Tellma:Session:CacheTtl`,
  default 60 s, 50,000 entries) and falls back to one primary-key read; `RenewAsync` writes through;
  `RemoveAsync` deletes and evicts. The TTL is the maximum time a revoked session keeps working on an
  instance other than the one that revoked it. `IApiEndpointMetadata` makes API endpoints answer
  401/403 instead of redirecting.
- **OIDC scheme `Tellma.OpenIdConnect`**: `Authority = Tellma:Identity:Authority` (the host's own
  origin + `/id` in in-proc mode), `ClientId = slug`, `ClientSecret` from configuration,
  `ResponseType = code`, PKCE, `PushedAuthorizationBehavior = Require` (spec 0003 carries assurance
  and allowed methods inside PAR; a downgrade must fail closed), `SaveTokens = true`,
  `MapInboundClaims = false`, `GetClaimsFromUserInfoEndpoint = false`, scopes
  `openid profile email offline_access tellma_api`, `CallbackPath = /signin-oidc`,
  `SignedOutCallbackPath = /signout-callback-oidc` (the exact URIs `ClientDescriptorFactory.Distribution`
  registers). Principal claims kept: `sub`, `sid`, `name`, `email`, `locale`, `acr`, `amr`,
  `auth_time`, `tellma_acr_auth_time`. `OnTokenValidated` rejects a principal without `sub` or `sid`.
- **Refresh-anchored liveness** in `TellmaSessionEvents.ValidatePrincipal`: when the stored access
  token expires within `Tellma:Session:RefreshSkew` (default 60 s), the event re-reads the session
  row (bypassing the cache); if `TokensVersion` already moved (another instance refreshed) it adopts
  the newer ticket, otherwise it redeems the refresh token at the authority and writes the new ticket
  with a compare-and-set on `TokensVersion` (§4); a lost CAS re-reads and adopts. Success →
  `ReplacePrincipal` (the refreshed id token's `acr`, evidence time and `locale` replace the
  principal's) + `ShouldRenew`. `invalid_grant` or any 4xx → `RejectPrincipal`, the row is deleted, the
  SPA receives `401` and re-enters the challenge: authority-side "sign out everywhere", policy
  tightening and user disablement take effect within one access-token lifetime even if back-channel
  delivery failed. A 5xx or network failure keeps the session for up to
  `Tellma:Session:MaxAuthorityOutage` (default 1 h), after which the session is rejected. Concurrent
  requests on one session refresh once (in-process single flight per key; the authority's 30 s reuse
  leeway covers the cross-instance race the CAS does not).
- **Back-channel logout receiver** `POST /bff/backchannel-logout` (anonymous by design,
  `application/x-www-form-urlencoded`, parameter `logout_token`; the URI provisioning registers as
  `tellma:backchannel_logout_uri`): validated with the OIDC handler's own configuration manager —
  signature against the JWKS, `iss`, `aud = slug`, `typ = logout+jwt`, `iat` within ±5 min, `events`
  containing `http://schemas.openid.net/event/backchannel-logout`, no `nonce`, `sid` or `sub` present.
  Then `DELETE catalog.Sessions WHERE Sid = @sid` (or `Subject = @sub` when only `sub` is present)
  with `OUTPUT deleted`, local eviction, `ISessionTerminationListener.SessionsTerminatedAsync(subject, keys)`
  (T10 closes the hub connections), and `200`. Any validation failure answers `400` with no body. A
  replayed token deletes nothing and answers `200`; `jti` is not tracked. The handler's built-in
  `RemoteSignOutPath` is front-channel only (it authenticates the *browser's* cookie — verified in
  the handler source) and stays unused.
- **BFF endpoints** (deployable, cookie scheme): `GET /bff/login?returnUrl=&acr_values=&max_age=`
  (local-only `returnUrl`; challenges the OIDC scheme, forwarding `acr_values`/`max_age` and the
  `tellma_allowed_methods` supplied by `IAuthenticationPolicyProvider`, default no constraint —
  initial sign-in requests no tier because the user has no tenant yet); `POST /bff/logout`
  (`{ "scope": "local" | "global" }`, default global: local sign-out then RP-initiated end-session
  with `id_token_hint`); `GET /bff/user` (`{ sub, name, email, locale, acr, tenants: [{ id, name,
  category, state, isActive }] }` from D14; `401` when unauthenticated). At login the BFF also writes
  the readable display-profile cookie `tellma.profile` (`{ name, locale }`, not `HttpOnly`, no
  tokens) that spec 0003 §7.1 describes for instant launch.
- **Bearer scheme `Tellma.Bearer`** (`JwtBearer`): same issuer, JWKS cached (no per-request identity
  call), `ValidTypes = ["at+jwt"]`, `NameClaimType = "sub"`, audience validated per request against
  the canonical resource of the route (D18).
- **Policies** (`TellmaPolicies`): `Tellma.Web` = scheme `Tellma.Session` + authenticated;
  `Tellma.Api` = scheme `Tellma.Bearer` + scope `tellma_api` + audience = origin; `Tellma.Mcp` =
  `Tellma.Bearer` + `tellma_api` + the tenant's MCP audience; `Tellma.ControlPlane` = `Tellma.Bearer`
  + scope `tellma_control_plane`; `FallbackPolicy = RequireAuthenticatedUser` (default scheme
  `Tellma.Session`). A policy names its scheme explicitly, so a cookie can never authenticate a
  bearer surface and a bearer can never authenticate the web surface.
- **`PrincipalKind`** is derived at binding: cookie → `User`; bearer with `auth_time` → `User` (a
  person through Claude Code, Codex, the CLI); bearer without it → `ServiceAccount` (`Subject` = the
  client id). An explicit `tellma_kind` claim on access tokens is recorded as a recommended
  identity-server addition, not a prerequisite.
- **Session revocation on user deactivation is tenant-level, not cookie-level.** Deactivating a user
  in one tenant bumps the user's permissions tag so the next connect step answers `404` for that
  tenant, records `IsActive = false` in the hint table, and calls
  `ISessionTerminationListener.TenantAccessRevokedAsync(tenantId, subject)` so T10 closes that user's
  connections for that tenant. The distribution session survives because the user may belong to other
  tenants. `ICatalogSessionStore.RevokeBySubjectAsync` exists for distribution-wide removal (the
  control plane, or a sub-only logout token).

**Rationale.** A stateless cookie cannot honour a `sid`-keyed logout without a revoked list that needs
a store and a sweep anyway, carries 3–5 KB on every API call, and is unrevocable until expiry (a
stolen cookie is a stolen refresh token for seven days). The store makes the cookie 43 characters,
revocation a `DELETE`, refresh coordination a compare-and-set (two instances 31 s apart would
otherwise trip reuse detection and revoke the whole family), and costs one primary-key read per
session per instance per minute. Anchoring liveness on refresh is what spec 0003 designed the
refresh token for.

**Rejected.** Stateless cookie + throttled `ValidatePrincipal` + polled revoked-`sid` list (review
flag: fewer tables, same staleness bound, more moving parts); `Duende.BFF`;
`Duende.AccessTokenManagement` (manages a stateless cookie's tokens — the CAS is the part that
matters and it is ~100 lines); an `IDistributedCache` store (there is no Redis; SQL is the
distributed store already present); OpenIddict validation instead of `JwtBearer` (spec 0003 names
JWT-bearer for resource servers).

**Confidence.** High on the shape; medium on the exact lifetimes.

### D16 — The endpoint audit: the "hard to leave unsecured" mechanism

`TellmaEndpointAudit` (a startup check over `EndpointDataSource`, contributing to the realised gate)
requires that every endpoint whose route begins with the `tenantId` parameter carries
`TenantEndpointMetadata` (with `Surface` and `IsMutation`), a policy from the set allowed for its
surface, no `IAllowAnonymous`, and (T4/T6) a securable or a public marker; that every cookie-scheme
`GET` endpoint is not stamped `IsMutation`; that no CORS policy is attached to a cookie-authenticated
group; and that every other endpoint carries `DeployableEndpointMetadata` — stamped by the platform
on the routes it maps, by `WebhookEndpointMetadata` on webhook receivers, by assembly membership for
the in-proc identity engine's endpoints, and by `.AsDeployableEndpoint("reason")` on anything a
distribution maps outside a tenant group. A distribution endpoint under any reserved prefix
(`/bff`, `/api/admin`, `/api/distribution-info`, `/health`, `/id`, `/.well-known`, the callbacks) is a
violation named by route and method.

**Confidence.** High.

### D17 — CSRF posture: required custom header + `Origin`/`Sec-Fetch-Site` + JSON-only bodies + no CORS; no antiforgery tokens

`TellmaCsrfMiddleware` (after `UseAuthorization`, before the tenant middleware) applies to every
request authenticated by `Tellma.Session` whose method is not `GET`, `HEAD` or `OPTIONS`:

1. If `Sec-Fetch-Site` is present it must be `same-origin` or `none`; else `403 csrf_rejected`.
2. If `Origin` (or, when absent on a non-navigation request, `Referer`) is present it must equal
   `Tellma:PublicOrigin`; else `403`.
3. The request must carry the header `Tellma-Client` with any non-empty value (the SPA sends its
   build tag; the value is logged, never a metric tag); else `403`. A custom header forces a CORS
   preflight for any cross-origin caller, and no CORS policy exists anywhere in the platform, so the
   preflight fails.
4. A request with a body must declare `Content-Type: application/json`, unless the endpoint carries
   `AcceptsMultipartMetadata` (T7's upload endpoint), which still requires the header; else `415`.

Cookie-authenticated `GET`s (`/bff/user`, blob downloads, the hub's WebSocket upgrade) are exempt
from the header but subject to the origin checks when the headers are present, and must be
side-effect free (D16). Bearer surfaces and anonymous endpoints are exempt. `SameSite=Lax` stays
(spec 0003 §7.1; `Strict` breaks the post-login redirect chain and deep links); RFC 10017's
`SameSite=Strict` is a SHOULD and the required header is its MUST-equivalent control — recorded as
the deliberate departure from the SHOULD.

**Rejected.** Cookie-to-header antiforgery tokens (a token endpoint, a second cookie, and the .NET 10
middleware enforces nothing for JSON anyway); `X-Requested-With` or `X-Tellma-Client` (RFC 6648
deprecates `X-`; a platform-named header is self-documenting); `SameSite=Strict`.

**Confidence.** High. T6 ratifies the header name.

### D18 — MCP topology: one endpoint per tenant, stateless, per-tenant audience; what the identity server must add

`/{tenantId:int:min(1)}/mcp` is mapped inside the tenant group when T6's MCP feature is composed, in
the SDK's stateless mode (no affinity), policy `Tellma.Mcp`, the tenant access guard in the MCP
request filters (D11), and the same `RequestContext` population as the web surface. Its RFC 8707
resource identifier is `{PublicOrigin}/{tenantId}/mcp`; the RFC 9728 document is served by the
platform at `/.well-known/oauth-protected-resource/{tenantId:int}/mcp` with
`resource` = that URI, `authorization_servers = [Tellma:Identity:Authority]`,
`scopes_supported = ["tellma_api"]`, `bearer_methods_supported = ["header"]`; the `401` challenge
names it. Token validation accepts `aud` ∈ { `{PublicOrigin}`, `{PublicOrigin}/{tenantId}/mcp` }
until the identity amendment below ships, then requires the exact endpoint audience; membership is
verified by the connect step in both cases. Human users arrive through CIMD or a pre-registered
public native client per vendor and are resolved by `sub`; autonomous agents use a service account
(`client_credentials`, `resource` = the endpoint) whose `client_id` T4 maps to a tenant user. An
agent that needs two tenants configures two servers; live and sandbox are two URLs. Tool shape, tool
listing by permission and the server name (`tellma-<slug>`) are T6's; the developer-tooling server
stays `dotnet tellma mcp`.

**Identity-server additions** (an amendment to spec 0003; prerequisites for the MCP surface, not for
this spec's definition of done):

1. **Origin-prefix resource grants.** A client holding `rsrc:{origin}` may request
   `resource={origin}/{tenantId}/mcp`; the authorization and token endpoints copy the *requested*
   URI into `aud`. This is a normalisation step in the controller (validate that the resource's
   origin is granted, then set the full URI), not a change to OpenIddict, and it gives per-tenant
   audiences without per-tenant identity state.
2. **Client ID Metadata Documents** (`client_id_metadata_document_supported: true`; `none` in
   `token_endpoint_auth_methods_supported`) — what Claude Code, hosted Claude and Codex use to
   register without an operator. DCR stays unimplemented (deprecated by MCP). Until CIMD ships, one
   pre-registered public native client per vendor through the existing `Cli`/`Native` seed kinds.
3. `code_challenge_methods_supported: ["S256"]` (already emitted) and refresh-token rotation for
   public clients (already OpenIddict's default; verify in the identity suite).

**Rejected.** One server per distribution with a `tenantId` argument on every tool (context bloat, a
tenant-selection tool, a shared audience); per-tenant resource *registration* on the identity server
(hundreds of rows of identity state created at provisioning and cleaned at retirement; OpenIddict
7.x has only static resources); `StatefulForInitializeClients` by default.

**Confidence.** High on topology; medium on the origin-prefix rule (an identity-server design
decision Ahmad should see).

### D19 — In-proc identity mode

`Tellma:Identity:Mode = InProc` makes `AddTellma` call `AddTellmaIdentity` with `PathBase = /id`,
`Issuer = {PublicOrigin}/id`, and the identity store pointed at the catalog connection (schema
`idsvr`, migrated by the identity engine's own migrations — the one schema the web process may
migrate, because the engine already owns that path). The OIDC handler's authority is then the
distribution's own origin; `UseTellma` places `UseTellmaIdentity()` after routing; `MapTellma` maps
its endpoints and marks them deployable.

**Required identity-engine addition.** The in-proc distribution must own its BFF client (`<slug>`)
and service client (`<slug>-svc`) with *stable* secrets from configuration, because a standalone
on-prem deployment may run several instances and a per-boot `CreateDistributionAsync` (which
generates fresh secrets on every call) would invalidate the other instances' client secret. The
engine gains `TellmaIdentitySeedClientKind.Distribution` with `Origin`, `BackchannelLogoutUri`,
`ClientSecret` and `ServiceClientSecret`, seeded through `ClientDescriptorFactory.Distribution` with
the supplied secrets and the `rsrc:<origin>` grant. `AddTellma` writes that seed entry from
`Tellma:Identity:ClientSecret` / `ServiceClientSecret` and `PublicOrigin`. In Development, when
`ClientSecret` is absent, `AddTellma` generates one per boot (single instance, no persistence needed).

Development defaults scaffolded in `appsettings.Development.json`: in-proc mode, `DevAdmin.Enabled`
(subject `00000000-0000-0000-0000-000000000001`, `admin@localhost`), development signing keys, the
email log sink. The migrator's `migrate` seeds `Tellma:Seed:Tenants` (tenant 1 "Acme" Live, tenant 2
"Acme Sandbox" Sandbox of 1) and, in each, T8's bootstrap creates the tenant admin on the dev-admin
subject, so the first sign-in is the email-code path with the code in the console (spec 0003 §10.4).

**Confidence.** High; the engine change is small and isolated.

### D20 — The distribution contract surface, the admin surface, and health

- `GET /api/distribution-info` (anonymous, `Cache-Control: public, max-age=60`):
  `{ "slug": "acme", "displayName": "Acme", "deploymentId": "acme-staging", "platformVersion": "…",
  "distributionVersion": "…", "identity": { "authority": "…", "mode": "Standalone" },
  "surfaces": { "web": "/{tenantId}/api/web", "mcp": "/{tenantId}/mcp", "api": null },
  "login": "/bff/login", "registrationPolicy": "SingleLive" }`. No tenant list, no environment
  secrets, no instance identity.
- `GET /health/live` (process up; no I/O) and `GET /health/ready` (registry snapshot loaded and
  within `MaxStaleness`, Data Protection keys loadable, authority discovery document cached), both
  anonymous. App Service health checks point at `/health/ready`.
- Admin surface (policy `Tellma.ControlPlane`, deployable): `GET /api/admin/info` (the info document
  plus tenant counts by state), `GET /api/admin/tenants`, `POST /api/admin/tenants/{id}/state`
  (`{ "state": "Active" | "ReadOnly" | "Suspended" | "Retired", "reason": "…" }` → the descriptor and
  `{ "effectiveWithinSeconds": 15 }`; `409` for an illegal transition), `POST /api/admin/tenants`
  (registers a `Provisioning` row and calls `ITenantProvisioningTrigger`; `501` while no trigger is
  registered; `409` under `SingleLive` for a second live tenant), `GET /api/admin/tenants/{id}/members`
  (the hint table). Usage/metering and self-registration with the control plane are deferred until a
  control plane exists. The control-plane audience is the fixed platform audience spec 0003 assigns
  to `tellma_control_plane` **until** the identity server grants the control-plane client each
  distribution's origin — recorded as an identity-side change, because a token valid at every
  distribution is a fleet-wide capability the audience should not have to be.

**Confidence.** High on the contract; low on the control-plane audience.

### D21 — Host baseline: Data Protection, forwarded headers, TLS, telemetry

- **Data Protection.** `SetApplicationName("tellma-" + slug)` so slot swaps keep sessions valid.
  Keys: Azure Blob + Key Vault key when `Tellma:DataProtection:BlobUri` and `KeyId` are set (the
  pinned `Azure.Extensions.AspNetCore.DataProtection.*` packages, the host's `TokenCredential`),
  otherwise `Tellma:DataProtection:KeyRingPath` (a shared folder, optionally
  `ProtectKeysWithCertificate`). Outside Development, neither configured is a startup failure.
- **Forwarded headers.** `AddTellmaForwardedHeaders` reproduces the identity host's rule set
  (explicit `KnownProxies`/`KnownNetworks`; refuses `ASPNETCORE_FORWARDEDHEADERS_ENABLED` and
  enabled-but-empty).
- **TLS.** HTTPS redirection and HSTS outside Development; the `__Host-` prefix makes plain HTTP
  structurally unable to carry a session. `Tellma:PublicOrigin` is required, absolute, `https`
  outside Development, path-less; redirect URIs, MCP resource identifiers, the back-channel URI and
  the CSRF origin check all derive from it.
- **Telemetry.** Meter `Tellma.Core` (`TenancyTelemetryNames`): `tellma.tenancy.resolutions`
  (counter; `outcome` ∈ `served | not_found | provisioning | read_only | suspended | not_member |
  catalog_unavailable`), `tellma.tenancy.catalog.refresh.duration` (histogram s; `outcome` ∈
  `unchanged | reloaded | failed`), `tellma.tenancy.catalog.snapshot.age` (observable gauge s),
  `tellma.tenancy.catalog.tenants` (observable gauge; `category`, `state`),
  `tellma.tenancy.connection.factories` (observable gauge), `tellma.tenancy.membership.writes`
  (counter; `outcome` ∈ `succeeded | failed`), `tellma.tenancy.scopes` (counter; `kind` ∈
  `request | background`). Meter `Tellma.Core.AspNetCore` (`HostTelemetryNames`):
  `tellma.session.operations` (counter; `operation` ∈ `store | retrieve | renew | remove | revoke`,
  `source` ∈ `cache | database`), `tellma.session.refreshes` (counter; `outcome` ∈
  `refreshed | adopted | rejected | deferred`), `tellma.auth.backchannel_logouts` (counter;
  `outcome` ∈ `accepted | rejected`), `tellma.auth.csrf_rejections` (counter; `rule` ∈
  `sec_fetch_site | origin | header | content_type`). No tenant tag on any instrument. Every tenant
  request runs inside a logger scope with `TenantId`, `TenantCategory`, `Subject` (never email),
  `UserId`; background scopes add `JobId`. The request activity carries `tellma.tenant.id` and
  `tellma.deployment.id` (per-tenant identity is allowed on traces and logs — the control plane's
  telemetry path — and forbidden on metrics). Alert queries under `infra/monitoring/` are checked in
  and cross-checked by the existing instrument-name test.

**Confidence.** High.

### D22 — Provisioning: migrator commands, a step seam, a trigger seam

`Tellma.Distro.<Slug>.Migrator` is the single DDL-privileged executable. `TellmaMigrator.RunAsync(args, slug, compose)`
builds a generic host with the same composition and runs one command (exit 0 converged, 1 usage,
2 partial failure):

| Command | Does |
|---|---|
| `migrate [--catalog-only] [--tenant <id>]* [--parallelism N]` | Migrates the catalog (under `sp_getapplock('tellma:migrate:catalog')`; `idsvr` too in in-proc mode); registers and provisions any `Tellma:Seed:Tenants` entry not yet in the catalog; then for every `Active`/`ReadOnly`/`Provisioning` tenant (or the named ones): applock `tellma:migrate:<database>`, `Migrate()`, `tellma_app` grants (D8), versioned seeds and provisioning steps, membership reconciliation. Bounded parallelism (default 4), continues past failures, per-tenant report. |
| `provision --name … --category Live\|Sandbox [--live-tenant <id>] [--id <n>] [--server …] [--database …] [--admin-subject … \| --admin-email …]` | Inserts the row as `Provisioning` (or resumes one the web app inserted); `CREATE DATABASE` (Azure: `(EDITION = …, SERVICE_OBJECTIVE = ELASTIC_POOL(name = …))` from `Tellma:Provisioning:CreateDatabaseTemplate`; on-prem: plain); `ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON` on the fresh database; the application principal and `tellma_app`; migrations; steps (T8's bootstrap admin, seeds); the admin's membership hint; then `SetStateAsync(Active)`. Idempotent: rerunning converges. |
| `set-state --tenant <id> --state … [--reason …]` | `ITenantCatalog.SetStateAsync` with actor `migrator`. |
| `status` | Lists tenants with state, schema version and pending migrations. |

`ITenantProvisioningStep` (Abstractions; `Name`, `Order`, `RunAsync(TenantProvisioningContext)`) is
registered by features as an ordinary scoped service; the migrator runs steps in `Order` (platform
0–99, packs 100–199, distributions 200+) inside a tenant scope with the System principal, and records
each completion in the tenant database's `dbo.__TellmaProvisioning (Step, CompletedAt, PlatformVersion)`
so a re-run skips completed steps unless `--force-steps`. Tenant-specific external integrations are
steps. T8's versioned seeds are steps.

The self-serve trigger is `ITenantProvisioningTrigger.StartAsync(tenantId)` with no implementation
shipped beyond `NotConfiguredProvisioningTrigger` (throws a composition problem naming the
registration); the reference distribution ships a local process starter for Development; the
Container Apps Job starter is infrastructure work outside the spec. Sandbox cloning from a live
tenant (`CREATE DATABASE … AS COPY OF` on Azure, backup-restore on-prem) is a later command; the row
shape supports it.

Design-time: the distribution's `AcmeDesignTimeFactory : TellmaDesignTimeDbContextFactory<TellmaDbContext>`
overrides one method (`Compose`) so `dotnet ef` builds the context from the same composition without
relying on host-factory discovery.

**Confidence.** High.

### D23 — The tenant model host: `TellmaDbContext` is platform-owned; a distribution derives one only when it wants `DbSet` conveniences

`AddTellma` registers `TellmaDbContext` (in `Tellma.Core`), whose `OnModelCreating` applies every
contributed model contributor in feature order and calls `UseTableTypes(...)`. Migrations live in the
distribution's Migrator project (`MigrationsAssembly` set by `TellmaMigrator`). A distribution may
call `tellma.UseDbContext<AcmeDbContext>()` in `Compose` to substitute a derived context; the default
writes zero distribution code. T2 owns everything the context exposes beyond this registration.

**Confidence.** Medium (review flag; T2 conflict).

### D24 — Vocabulary this theme fixes

Plural table names in schema `catalog` (`Tenants`, `TenantMemberships`, `Sessions`, `CatalogState`),
matching ARCHITECTURE.md's `gl.Invoices`; lowercase schema names (`catalog`, `core`, `gl`, `idsvr`);
`int` tenant ids; enum columns as `nvarchar(16)` strings with CHECK constraints (T2 ratifies the
enum-as-string convention); `datetime2(3)` UTC timestamps; `CreatedAt/ModifiedAt` plus `StateChangedBy`
on the tenant row (a subject or client id, not an FK — there is no tenant user in the catalog);
"catalog" (not "registry") for the table and the database, "registry" for the in-memory read model;
"tenant access guard" for the membership-and-state check; "connect step" for T4's subject-to-user
resolution; machine codes in snake_case (`tenant_not_found`, `tenant_provisioning`,
`tenant_suspended`, `tenant_read_only`, `catalog_unavailable`, `csrf_rejected`); request headers
`Tellma-Client`, `Tellma-Calendar`, `Tellma-Time-Zone` (RFC 6648; T6 ratifies).

### D25 — Local development story

From a fresh clone: `dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator -- migrate`
(creates `Tellma.dev.acme.catalog`, provisions tenants 1 and 2 from `Tellma:Seed:Tenants` with the
dev admin on subject `…0001`), then `dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Web`,
browse `https://localhost:7052/`, sign in as `admin@localhost` with the code from the console. The
web host detects a missing catalog at startup and prints that exact `migrate` command instead of a
stack trace; it never migrates. The `Tellma.dev.<worktree-id>.*` naming and the
`launchSettings.template.json` scheme wait for `dotnet tellma setup-worktree`.

### D26 — Testing

| Suite | Tier | Pins |
|---|---|---|
| `test/core/Tellma.Core.Tests` | unit | graph gate (aggregation, cycles, unknown items, fix text); registry snapshot semantics with `FakeTimeProvider` (version unchanged → no reload; changed → reload; `!=` on an older GUID; staleness bound → `catalog_unavailable`); connection composition and the startup rejections; state-transition table; `RequestContext` immutability and snapshot round trip; holder set-once; `TenantSandboxContext` throws unbound; `taxonomy.json` consistency |
| `test/core/Tellma.Core.IntegrationTests` (`Category=Integration`, Testcontainers) | integration | catalog migrations apply from empty and are idempotent; state transitions bump `Version`; membership TVP reconciliation; `tellma_app` grants recomputed from the model; session store CRUD, CAS (two concurrent refreshers, exactly one redeems), sweep |
| `test/core/Tellma.Core.AspNetCore.Tests` | unit (`WebApplicationFactory`, fake authentication) | tenant verdict matrix (unknown/retired/provisioning/suspended/read-only × read/write × anonymous/member/non-member); CSRF matrix; scheme isolation (cookie on a bearer route fails, bearer on the web route fails); endpoint-audit violations; back-channel token vectors (bad `iss`, bad `aud`, missing `events`, `nonce` present, expired, sub-only, replay → 200 no-op); step-up challenge shape; distribution-info shape |
| `distributions/acme/test/Tellma.Distro.Acme.Web.Tests` | unit | composition parity (web and migrator build byte-identical models); the audit passes for every mapped endpoint; one-way dependency (no `src/` project references `distributions/`); no secrets in tracked configuration |
| `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests` | `Category=Integration` | `migrate` + `provision` on Testcontainers; full sign-in through the in-proc engine (reusing the identity suite's OIDC flow client); first tenant request; suspension round trip within one refresh; back-channel logout end to end; refresh rejection ends the session; readiness before/after catalog availability |

No `Live=true` suite in this theme.

---

## 3. Contracts

### 3.1 `Tellma.Core.Abstractions`

```contract
// Tellma.Core.Abstractions.Tenancy
enum TenantCategory = Live | Sandbox
enum TenantState = Provisioning | Active | ReadOnly | Suspended | Retired
enum PrincipalKind = Anonymous | User | ServiceAccount | System

record TenantDescriptor(Id: int, Name: string, Category: TenantCategory, State: TenantState, LiveTenantId: int?)

record AuthenticationAssurance(Acr: string?, AuthTime: DateTimeOffset?, AcrAuthTime: DateTimeOffset?)

record RequestContext
  Tenant: TenantDescriptor?                 // null for deployable work
  Kind: PrincipalKind = Anonymous
  Subject: string?                          // sub, or a service account's client id, or "system"
  ClientId: string?                         // the OAuth client the credential arrived through
  SessionId: string?                        // sid; users through the cookie only
  Assurance: AuthenticationAssurance?
  UserId: int?                              // late-bound by the connect step
  Language: string = "en"                   // messages; BCP 47, no extensions
  Culture: string = "en"                    // formatting; BCP 47, no extensions
  Calendar: string = "gc"                   // gc | uq | et (T3 ratifies)
  TimeZone: string = "UTC"                  // the user's IANA zone, for rendering instants
  TenantTimeZone: string = "UTC"            // the tenant's IANA zone, in which Today is computed
  Now: DateTimeOffset                       // fixed at the start of the unit of work
  Today: DateOnly                           // the business date; what today() binds to
  OriginTraceParent: string?                // W3C traceparent of the causing request, for links
  IsSandbox: bool                           // derived: Tenant?.Category == Sandbox
  RequireTenant() -> TenantDescriptor       // sync; throws when not tenant-scoped
  static Deployable: RequestContext         // the tenant-less context

record RequestContextSnapshot(TenantId: int, Kind: PrincipalKind, Subject: string?, ClientId: string?,
  UserId: int?, Language: string, Culture: string, Calendar: string, TimeZoneId: string, TraceParent: string?)

service IRequestContextAccessor
  Current: RequestContext                   // sync property; Deployable when nothing set one

record TenantMembership(Tenant: TenantDescriptor, IsActive: bool, UpdatedAt: DateTimeOffset)

data TenantMembershipRecord                 // [TableType] standalone shape → [catalog].[TenantMembershipList]
  TenantId: int
  Subject: nvarchar(36)
  IsActive: bit

service ITenantMembershipDirectory
  ListAsync(subject: string) -> list<TenantMembership>          // excludes Retired tenants; one catalog query
  RecordAsync(records: list<TenantMembershipRecord>)             // upsert; best effort, never fails a save

contract ITenantStateListener               // implemented by T10 (hub closer, job pauser)
  OnStateChangedAsync(tenant: TenantDescriptor, previous: TenantState?)

contract ISessionTerminationListener        // implemented by T10 over the hub
  SessionsTerminatedAsync(subject: string, sessionKeys: list<string>)
  TenantAccessRevokedAsync(tenantId: int, subject: string)

record TenantProvisioningContext(Tenant: TenantDescriptor, Services: IServiceProvider, IsNew: bool, AdminSubject: string?, AdminEmail: string?)

contract ITenantProvisioningStep            // registered as a scoped service by any feature
  Name: string                              // key in dbo.__TellmaProvisioning
  Order: int                                // platform 0–99, packs 100–199, distributions 200+
  RunAsync(context: TenantProvisioningContext)

record TenantNotFoundException(TenantId: int)                                   // → 404 tenant_not_found
record TenantUnavailableException(TenantId: int, State: TenantState?, Code: string, RetryAfter: TimeSpan?)  // → 503 / 403 by Code
record InsufficientAssuranceException(RequiredAcr: string, MaxAge: TimeSpan?)   // → 401 insufficient_user_authentication

record TenancyTelemetryNames                // const names of D21; MeterName = "Tellma.Core"
```

```contract
// Tellma.Core.Abstractions.Composition
contract ITellmaFeature
  Name: string                              // stable, unique, lowercase dotted: "core", "gl", "acme"
  Declare(declaration: FeatureDeclaration)  sync
  Contribute(contribution: FeatureContribution) sync

annotation [Requires<TFeature>]  on type    // sugar for FeatureDeclaration.Requires<TFeature>()

data FeatureDeclaration
  Requires<TFeature>() -> FeatureDeclaration  sync   where TFeature: ITellmaFeature
  Options<TOptions>(configurationPath: string) -> FeatureDeclaration  sync   // bound and validated at startup
  RequiredFeatures: list<Type>

base FeatureContributionItem                // abstract record; every item type is a record in Abstractions

enum ServiceLifetimeKind = Singleton | Scoped | Transient
record ServiceContributionItem(ServiceType: Type, ImplementationType: Type?, Factory: Func<IServiceProvider, object>?, Lifetime: ServiceLifetimeKind) : FeatureContributionItem
record ModelContributionItem(ContributorType: Type) : FeatureContributionItem   // implements EF's IEntityTypeConfiguration<>

data FeatureContribution
  Add(item: FeatureContributionItem) -> FeatureContribution  sync
  Feature(feature: ITellmaFeature) -> FeatureContribution  sync           // nested feature; validated with the rest
  Singleton<TService, TImplementation>() -> FeatureContribution  sync
  Scoped<TService, TImplementation>() -> FeatureContribution  sync
  Transient<TService, TImplementation>() -> FeatureContribution  sync
  Model<TContributor>() -> FeatureContribution  sync

contract IStartupCheck                      // registered as a service by any package; run by the realised gate
  Name: string
  CheckAsync(services: IServiceProvider) -> list<CompositionProblem>

record CompositionProblem(Source: string, Problem: string, Fix: string?)
record TellmaCompositionException(Problems: list<CompositionProblem>)   // one message line per problem
```

### 3.2 `Tellma.Core`

```contract
// Tellma.Core.Composition
service TellmaServiceCollectionExtensions
  AddTellma(services: IServiceCollection, slug: string, configuration: IConfiguration, environment: IHostEnvironment, compose: Action<TellmaBuilder>) -> IServiceCollection  sync

data TellmaBuilder
  Services: IServiceCollection
  Configuration: IConfiguration
  Environment: IHostEnvironment
  AddFeature<TFeature>() -> TellmaBuilder  sync   where TFeature: ITellmaFeature, new()   // idempotent
  AddFeature(feature: ITellmaFeature) -> TellmaBuilder  sync
  UseDbContext<TContext>() -> TellmaBuilder  sync  where TContext: TellmaDbContext

contract IContributionRealizer<TItem>       // one per item type Core understands
  Realize(item: TItem, owner: ITellmaFeature, context: RealizationContext) sync   where TItem: FeatureContributionItem

service TellmaComposition
  Validate(slug: string, configuration: IConfiguration, compose: Action<TellmaBuilder>) -> list<CompositionProblem>  sync   // host-free

// Tellma.Core.Tenancy
record TenantLocation(Server: string, Database: string, CredentialProfile: string)
record TenantInfo(Descriptor: TenantDescriptor, Location: TenantLocation, ConnectionString: string, Properties: string?, Version: Guid)

service ITenantRegistry                     // singleton; snapshot reads, no I/O
  Tenants: list<TenantInfo>
  SnapshotVersion: Guid
  SnapshotLoadedAt: DateTimeOffset?
  Find(tenantId: int) -> TenantInfo?  sync
  Get(tenantId: int) -> TenantInfo  sync       // throws TenantNotFoundException
  RefreshAsync(force: bool)                    // single-flight

enum TenantRegistrationPolicy = SingleLive | MultiLive
record TenantRegistration(Name: string, Category: TenantCategory, LiveTenantId: int?, Location: TenantLocation?, Id: int?)

service ITenantCatalog                      // write side; every member is one catalog transaction + version bump + local refresh
  RegistrationPolicy: TenantRegistrationPolicy
  RegisterAsync(registration: TenantRegistration, actor: string) -> TenantDescriptor   // Provisioning state
  SetStateAsync(tenantId: int, state: TenantState, reason: string?, actor: string) -> TenantDescriptor
  RenameAsync(tenantId: int, name: string)
  RelocateAsync(tenantId: int, location: TenantLocation, actor: string)
  ListAsync() -> list<TenantInfo>             // from the store, never the cache

service ITenantConnectionFactory            // singleton; explicit tenant (migrator, reconcilers)
  GetConnectionString(tenantId: int) -> string  sync   // cached per (id, Version); never logged
  OpenAsync(tenantId: int) -> SqlConnection
  OpenCatalogAsync() -> SqlConnection

service ITenantConnectionProvider           // scoped; the batch executor's door to SQL
  Tenant: TenantInfo
  OpenAsync() -> SqlConnection

service ITenantDbContextFactory
  Create(tenantId: int) -> TellmaDbContext  sync   // from that tenant's pooled factory

service IRequestContextHolder : IRequestContextAccessor   // scoped; platform-internal writer
  Set(context: RequestContext) sync

contract IRequestContextInitializer         // T4 at 100, T3 at 200
  Order: int
  InitializeAsync(context: RequestContext, inputs: RequestContextInputs) -> RequestContext

record RequestContextInputs(AcceptLanguage: string?, RequestedCalendar: string?, RequestedTimeZone: string?)

service ITenantAccessGuard
  EnsureAccessAsync(requirement: TenantAccessRequirement)   // runs initializers + assurance; throws on refusal
record TenantAccessRequirement(IsMutation: bool, Assurance: RequireAssuranceMetadata?)
record RequireAssuranceMetadata(Acr: string, MaxAge: TimeSpan?)   // endpoint metadata; T6 exposes it on operations

service ITenantScopeFactory
  CreateScopeAsync(snapshot: RequestContextSnapshot) -> TenantScope   // re-resolves the tenant; throws per D10
  Capture() -> RequestContextSnapshot  sync
record TenantScope(Services: IServiceProvider, Activity: Activity?)   // IAsyncDisposable

service ITenantProvisioningTrigger
  StartAsync(tenantId: int)

service IAuthenticationPolicyProvider       // login-time constraints; default: none
  GetLoginPolicyAsync(tenantId: int?) -> LoginPolicy
record LoginPolicy(AcrValues: string?, MaxAge: TimeSpan?, AllowedMethods: list<string>?)
```

### 3.3 `Tellma.Core.AspNetCore`

```contract
service TellmaWebApplicationBuilderExtensions
  AddTellma(builder: WebApplicationBuilder, slug: string, compose: Action<TellmaBuilder>) -> WebApplicationBuilder  sync
service TellmaApplicationBuilderExtensions
  UseTellma(app: WebApplication) -> WebApplication  sync
service TellmaEndpointRouteBuilderExtensions
  MapTellma(app: WebApplication) -> TellmaEndpoints  sync

data TellmaEndpoints
  Web: RouteGroupBuilder                    // /{tenantId:int:min(1)}/api/web
  Api: RouteGroupBuilder                    // /{tenantId:int:min(1)}/api/v1 (seam)
  Hub: RouteGroupBuilder                    // /{tenantId:int:min(1)}/hub (T10)
  Blobs: RouteGroupBuilder                  // /{tenantId:int:min(1)}/blobs (T7)
  Deployable: RouteGroupBuilder             // /api; each endpoint calls AsDeployableEndpoint
  AsDeployableEndpoint(builder, reason: string)  sync   // extension on IEndpointConventionBuilder
  WithMutation(builder, isMutation: bool)  sync          // extension on IEndpointConventionBuilder

record TellmaAuthentication                 // const: SessionScheme "Tellma.Session", OidcScheme "Tellma.OpenIdConnect",
                                            //   BearerScheme "Tellma.Bearer", SessionCookieName "__Host-tellma.session",
                                            //   ProfileCookieName "tellma.profile", CsrfHeaderName "Tellma-Client"
record TellmaPolicies                       // const: Web "Tellma.Web", Api "Tellma.Api", Mcp "Tellma.Mcp", ControlPlane "Tellma.ControlPlane"

enum TenantSurface = Web | Api | Mcp | Hub | Blobs
record TenantEndpointMetadata(Surface: TenantSurface, IsMutation: bool)
record DeployableEndpointMetadata(Reason: string)
record AcceptsMultipartMetadata()

service ICatalogSessionStore : ITicketStore // rows in catalog.Sessions, per-instance cache, revocation
  RevokeBySidAsync(sid: string)
  RevokeBySubjectAsync(subject: string)
  SweepAsync() -> int                       // T10 consumer; deletes expired rows in batches of 1000

service StepUpChallenge
  Write(response: HttpResponse, acrValues: string, maxAge: int?)  sync   // 401 + WWW-Authenticate per spec 0003 §9.3

record DistributionInfo(Slug: string, DisplayName: string, DeploymentId: string, PlatformVersion: string,
  DistributionVersion: string, Identity: IdentityInfo, Surfaces: map<string, string?>, Login: string,
  RegistrationPolicy: TenantRegistrationPolicy)

record HostTelemetryNames                   // const names of D21; MeterName = "Tellma.Core.AspNetCore"
```

### 3.4 `Tellma.Core.Migrator`

```contract
service TellmaMigrator
  RunAsync(args: list<string>, slug: string, compose: Action<TellmaBuilder>) -> int   // exit code

base TellmaDesignTimeDbContextFactory<TContext>   // IDesignTimeDbContextFactory<TContext>
  Compose(builder: TellmaBuilder)  sync          // abstract; the distribution points it at its Compose
```

### 3.5 Configuration (`Tellma` section; keys additive-only within a family major)

```jsonc
{
  "Tellma": {
    "DisplayName": "Acme",
    "PublicOrigin": "https://acme.app.tellma.com",
    "Catalog": {
      "ConnectionString": "Server=tcp:sql-tellma-platform.database.windows.net,1433;Database=tellma-acme-catalog;Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=10",
      "RefreshInterval": "00:00:15",
      "MaxStaleness": "00:05:00"
    },
    "Sql": {
      "Profiles": { "default": "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=20;Min Pool Size=0" },
      "MaxPoolSizePerTenant": 20,
      "AllowSqlPassword": false,
      "DatabasePrefix": "tellma-acme",
      "ApplicationPrincipal": "tellma-acme"
    },
    "Tenancy": { "RegistrationPolicy": "SingleLive" },
    "Identity": { "Mode": "Standalone", "Authority": "https://identity.tellma.com", "ClientSecret": null, "ServiceClientSecret": null, "InProc": { } },
    "Session": { "IdleLifetime": "7.00:00:00", "AbsoluteLifetime": "90.00:00:00", "CacheTtl": "00:01:00", "RefreshSkew": "00:01:00", "MaxAuthorityOutage": "01:00:00" },
    "DataProtection": { "BlobUri": null, "KeyId": null, "KeyRingPath": null },
    "ForwardedHeaders": { "Enabled": false, "KnownNetworks": [] },
    "Provisioning": { "CreateDatabaseTemplate": null, "Trigger": null },
    "Seed": { "Tenants": [ { "Id": 1, "Name": "Acme", "Category": "Live" }, { "Id": 2, "Name": "Acme Sandbox", "Category": "Sandbox", "LiveTenantId": 1 } ], "AdminEmail": "admin@localhost" }
  }
}
```

---

## 4. Schema

All tables live in the catalog database, schema `catalog`, created by the platform-owned migrations
in `Tellma.Core.Migrator` (history table `catalog.__EFMigrationsHistory`). No IDENTITY; `datetime2(3)`
UTC; enum columns as strings with CHECK constraints; explicit constraint names; no temporal tables.

**`catalog.Tenants`**

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK, sequence `sq_Tenants` (START WITH 1) | the leading route segment; never reused |
| `Name` | `nvarchar(255)` | no | | display name; mirrored from tenant settings by `RenameAsync` |
| `Category` | `nvarchar(16)` | no | CHECK IN (`Live`, `Sandbox`) | immutable after provisioning |
| `LiveTenantId` | `int` | yes | FK → `catalog.Tenants`; CHECK `(Category = 'Sandbox') = (LiveTenantId IS NOT NULL)` | |
| `State` | `nvarchar(16)` | no | CHECK IN (`Provisioning`, `Active`, `ReadOnly`, `Suspended`, `Retired`) | |
| `StateReason` | `nvarchar(1024)` | yes | | operator text |
| `StateChangedAt` | `datetime2(3)` | no | | |
| `StateChangedBy` | `nvarchar(128)` | yes | | subject, client id, or `migrator` |
| `Server` | `nvarchar(255)` | no | CHECK `NOT LIKE '%[;=]%'` | host, or host,port |
| `Database` | `nvarchar(128)` | no | CHECK `NOT LIKE '%[;=]%'`; unique with `Server` | |
| `CredentialProfile` | `nvarchar(64)` | no | default `'default'` | name of a configured profile |
| `Properties` | `nvarchar(max)` | yes | | non-secret, distribution-defined JSON |
| `Version` | `uniqueidentifier` | no | | `NEWID()` on every update; keys the composed-string cache |
| `CreatedAt` | `datetime2(3)` | no | | |
| `ModifiedAt` | `datetime2(3)` | no | | |

Indexes: `IX_Tenants_State (State)`, `IX_Tenants_LiveTenantId (LiveTenantId) WHERE LiveTenantId IS NOT NULL`,
`UQ_Tenants_Location (Server, Database)`. No password, key, token or connection-string column may ever
be added; a model test asserts the column set.

**`catalog.CatalogState`** — one row (`Id = 1`, CHECK), `Version uniqueidentifier NOT NULL`,
`ModifiedAt datetime2(3) NOT NULL`; seeded `(1, NEWID(), SYSUTCDATETIME())`.

**`catalog.TenantMemberships`**

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Subject` | `nvarchar(36)` | no | PK (with `TenantId`) | the identity `sub` or a service account client id |
| `TenantId` | `int` | no | PK, FK → `catalog.Tenants` | |
| `IsActive` | `bit` | no | | |
| `UpdatedAt` | `datetime2(3)` | no | | |

Index `IX_TenantMemberships_TenantId (TenantId)`. Table type `[catalog].[TenantMembershipList]
(TenantId int, Subject nvarchar(36), IsActive bit)` is the standalone `[TableType]` class
`TenantMembershipRecord` registered on `CatalogDbContext` (physical name carries the content hash).

**`catalog.Sessions`**

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Key` | `varchar(64)` | no | PK, collation `Latin1_General_BIN2` | base64url of 32 CSPRNG bytes |
| `Subject` | `nvarchar(36)` | no | | |
| `Sid` | `nvarchar(128)` | yes | | authority session id |
| `Ticket` | `varbinary(max)` | no | | Data-Protection-encrypted `TicketSerializer` output |
| `TokensVersion` | `int` | no | | compare-and-set for refresh |
| `CreatedAt` | `datetime2(3)` | no | | |
| `RenewedAt` | `datetime2(3)` | no | | |
| `ExpiresAt` | `datetime2(3)` | no | | sliding: last renewal + `IdleLifetime`, capped by the absolute lifetime |

Indexes: `IX_Sessions_Sid (Sid) WHERE Sid IS NOT NULL`, `IX_Sessions_Subject (Subject)`,
`IX_Sessions_ExpiresAt (ExpiresAt)`.

**In every tenant database** — `dbo.__TellmaProvisioning (Step nvarchar(128) PK, CompletedAt datetime2(3), PlatformVersion nvarchar(64))`,
and the role and grants recomputed by the migrator after every run:

```sql
IF DATABASE_PRINCIPAL_ID('tellma_app') IS NULL CREATE ROLE [tellma_app];
-- for every schema S the tenant model maps:
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[S] TO [tellma_app];
-- for every sequence in the model:
GRANT UPDATE ON OBJECT::[S].[sq_<Table>] TO [tellma_app];
-- table types: GRANT EXECUTE ON TYPE emitted by the migrations themselves (spec 0001 grant principals).
-- provisioning only, SaaS:
CREATE USER [<web app identity>] FROM EXTERNAL PROVIDER;  ALTER ROLE [tellma_app] ADD MEMBER [<web app identity>];
-- provisioning only, on-prem:
CREATE USER [tellma_app_user] FOR LOGIN [tellma_app];  ALTER ROLE [tellma_app] ADD MEMBER [tellma_app_user];
-- provisioning only, fresh database (no-op on Azure SQL, where it is the default):
ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON;
```

**Load-bearing statements** (parameterised; `SET XACT_ABORT ON` where a transaction is opened):

```sql
-- D9: refresh probe, every 15 s per instance
SELECT [Version] FROM [catalog].[CatalogState] WHERE [Id] = 1;

-- D9: snapshot reload, only when the version changed
SELECT [Id], [Name], [Category], [LiveTenantId], [State], [Server], [Database], [CredentialProfile], [Properties], [Version]
FROM [catalog].[Tenants];

-- D10: state transition (the guard @from is the transition table's precondition)
SET XACT_ABORT ON; BEGIN TRAN;
UPDATE [catalog].[Tenants]
   SET [State] = @to, [StateReason] = @reason, [StateChangedAt] = SYSUTCDATETIME(), [StateChangedBy] = @actor,
       [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME()
 WHERE [Id] = @tenantId AND [State] = @from;
IF @@ROWCOUNT <> 1 BEGIN ROLLBACK; THROW 51001, 'Illegal or concurrent tenant state transition.', 1; END
UPDATE [catalog].[CatalogState] SET [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME() WHERE [Id] = 1;
COMMIT;

-- D14: membership list
SELECT t.[Id], t.[Name], t.[Category], t.[State], t.[LiveTenantId], m.[IsActive], m.[UpdatedAt]
  FROM [catalog].[TenantMemberships] m JOIN [catalog].[Tenants] t ON t.[Id] = m.[TenantId]
 WHERE m.[Subject] = @subject AND t.[State] <> N'Retired';

-- D14: membership upsert from the table type (no MERGE); reconciliation adds the DELETE for one tenant
UPDATE m SET m.[IsActive] = r.[IsActive], m.[UpdatedAt] = SYSUTCDATETIME()
  FROM [catalog].[TenantMemberships] m JOIN @rows r ON r.[TenantId] = m.[TenantId] AND r.[Subject] = m.[Subject]
 WHERE m.[IsActive] <> r.[IsActive];
INSERT INTO [catalog].[TenantMemberships] ([Subject], [TenantId], [IsActive], [UpdatedAt])
SELECT r.[Subject], r.[TenantId], r.[IsActive], SYSUTCDATETIME() FROM @rows r
 WHERE NOT EXISTS (SELECT 1 FROM [catalog].[TenantMemberships] m WHERE m.[Subject] = r.[Subject] AND m.[TenantId] = r.[TenantId]);
DELETE m FROM [catalog].[TenantMemberships] m
 WHERE m.[TenantId] = @tenantId AND NOT EXISTS (SELECT 1 FROM @rows r WHERE r.[Subject] = m.[Subject]);

-- D15: ticket retrieve on cache miss
SELECT [Subject], [Sid], [Ticket], [TokensVersion], [ExpiresAt] FROM [catalog].[Sessions]
 WHERE [Key] = @key AND [ExpiresAt] > SYSUTCDATETIME();

-- D15: refresh compare-and-set (zero rows = lost the race; re-read and adopt)
UPDATE [catalog].[Sessions]
   SET [Ticket] = @ticket, [TokensVersion] = [TokensVersion] + 1, [ExpiresAt] = @expiresAt, [RenewedAt] = SYSUTCDATETIME()
OUTPUT inserted.[TokensVersion]
 WHERE [Key] = @key AND [TokensVersion] = @expectedVersion;

-- D15: back-channel logout
DELETE FROM [catalog].[Sessions] OUTPUT deleted.[Key], deleted.[Subject] WHERE [Sid] = @sid;

-- T10 consumer: session sweep
DELETE TOP (1000) FROM [catalog].[Sessions] WHERE [ExpiresAt] < DATEADD(day, -1, SYSUTCDATETIME());

-- D22: tenant migration lock
EXEC sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 600000;
```

---

## 5. Answers

| Brain-dump / briefing question | Answer | Decision |
|---|---|---|
| "Are those the proper layer names in a .NET business app?" | Data / service / web stays prose; folders `Entities/`, `Services/`, `Endpoints/`; no `Data/` folder. | D3 |
| Where the reference distribution lives; slug; reserved-slug implications; project set | `distributions/acme/` in distribution-repo shape; slug `acme` (taken by the platform); Web + Migrator + two test projects; `taxonomy.json` created. | D2 |
| `AddTellma` at minimal fidelity; module package and stack feature on one shape | BCL-only `ITellmaFeature` in Abstractions with declare/contribute and data-only items realised by Core; `Requires` only; graph gate in `AddTellma`, realised gate at startup; explicit `AddFeature<T>()`. | D6 |
| `{tenantId}` shape | Immutable positive `int`, tenant-first prefix `/{tenantId:int:min(1)}`; platform routes never start with a digit. | D5 |
| "Is the TenantRegistry the right shape?" | No: it stored connection strings. Split into `ITenantRegistry` (snapshot reads), `ITenantCatalog` (writes), `ITenantConnectionFactory`/`ITenantConnectionProvider` (strings and connections). | D8, D9 |
| Catalog in config vs live DB vs dedicated DB; ARCHITECTURE's Catalog-DB assumption | Always one dedicated catalog database (schema `catalog`); single-live versus multi-live is a registration policy; configuration only seeds. | D7 |
| "if each db gets a password, where do we store these passwords?" | No database gets a password: managed identity as a contained user in SaaS, integrated security on Windows, one login on Linux; the catalog stores server + database + profile name. | D8 |
| Per-tenant connection resolution and caching | Profile fragment + row location composed once per row version; one pooled `DbContext` factory and one SqlClient pool per active tenant, `Max Pool Size` 20; 15 s version poll, 5 min maximum staleness, then fail closed. | D8, D9 |
| Secrets policy | No secret in any table, tracked file, log or metric; Key Vault references / configuration provider in SaaS; environment or key-per-file on-prem; no DPAPI; unresolved references fail startup. | D8 |
| "Extend the TenantRegistry to blob storage / Key Vault?" | No: locations derive from the tenant id, one `TokenCredential` reaches every store; secret *names* may live in `Properties`. | D8 |
| "Extend the TenantRegistry to support provisioning?" | No: provisioning is a migrator command with DDL rights and an `ITenantProvisioningStep` seam; the web app records a `Provisioning` row and calls `ITenantProvisioningTrigger`. | D22 |
| Membership lookup ("fan out for single-live") | `catalog.TenantMemberships` hints, written post-commit and reconciled by the migrator; one query for both shapes; never an authorization source. | D14 |
| `ISandboxContext` implementation | `TenantSandboxContext` over `IRequestContextAccessor`; throws when no tenant is bound. | D10 |
| Request context abstractions and how they reach services and background scopes | Immutable `RequestContext`, `IRequestContextAccessor`, a scoped holder with two writers; `RequestContextSnapshot` stored with job rows; `ITenantScopeFactory` rebinds; no `AsyncLocal`. | D13 |
| "X-Today header, or the user's time zone?" | Time zone, never a client date: `Tellma-Time-Zone` for the user's zone (rendering); `today()` is the tenant-zone date per spec 0008. | D13 |
| OIDC relying party with the BFF cookie; CSRF; session revocation on deactivation | Catalog session store with per-instance cache, CAS-coordinated refresh, back-channel receiver, per-surface schemes; header + origin CSRF; tenant-level deactivation is a per-request 404 plus connection close. | D15–D17 |
| Suspension hook | `ITenantCatalog.SetStateAsync`: immediate locally, ≤ 15 s fleet-wide; the state table of behaviours. | D9, D10 |
| "One MCP server per tenant, or one for the entire distro?" | Per tenant at `/{tenantId}/mcp`, one code path, per-tenant audience once the identity server echoes the requested resource; two servers for two tenants. | D18 |
| "the MCP schema is discoverable at runtime … can be changed without breaking existing agents (is this true?)" | Partly: adding tools or parameters is safe; renaming or re-semanticising a tool breaks prompts and saved workflows that name it — tool names are a public API (T6). | D18 |
| What provisioning leaves as a seam | The trigger, sandbox cloning, distribution-specific per-tenant facts (`Properties` or tenant settings). | D22 |
| `/api/distribution-info` and the admin contract | Anonymous info document without a tenant list; `/api/admin/*` on the control-plane policy; `/health/live`, `/health/ready`. | D20 |
| "Do we need shared coordination state in the catalog DB for background tasks?" | No; the catalog stays read-mostly; leasing is per tenant database (T10). | D7 |

---

## 6. Seams

1. **Batch abstraction (T2 owns).** The executor opens connections only through
   `ITenantConnectionProvider` (request scope) or `ITenantConnectionFactory` (explicit id: migrator,
   reconcilers) and never receives a connection string; a batch is bound to one tenant for its
   lifetime; it never issues `USE` or `ChangeDatabase`. Catalog statements (§4) run through the same
   executor against `OpenCatalogAsync`, which is how the membership TVP rides T2's metadata-driven
   binding. Required of T2: when a deferred connect verification reports no member, the executor
   throws `TenantNotFoundException` so the optimistic collapse fails closed; the executor's span
   carries `tellma.db.role = catalog | tenant` so the DB-call budget excludes catalog reads.
4. **Queryex schema per tenant (T2/T3/T4).** The schema cache key includes `RequestContext.Tenant.Id`;
   this theme guarantees the descriptor is bound before any handler runs.
5. **Version tags (T3).** `CatalogState.Version` follows the opaque-GUID convention; the catalog is
   not a home for tenant-data tags. The access guard is where T4's connect step reads tags.
6. **Feature composition (owner).** Contract in §3.1. T5 adds `StackContributionItem`, T4
   `SecurableContributionItem`, T8 `SeedContributionItem`, T6 an endpoint-mapper item, T10 a schedule
   item; each ships its realizer in `Tellma.Core`. T8's `GlFeature` declares
   `[Requires<CoreFeature>]` and contributes its stack and model items; the distribution's own
   feature uses identical items.
8. **Background-task columns and lease statements (T10/T2).** The job row stores the
   `RequestContextSnapshot` fields as columns; the runner calls `ITenantScopeFactory.CreateScopeAsync`
   and skips tenants whose state is not `Active` (read-only handlers only under `ReadOnly`); T10
   implements `ITenantStateListener` and `ISessionTerminationListener`, and hosts the session sweep.
9. **Request context (owner).** Contract in §3.1/§3.2. T4 and T3 are `IRequestContextInitializer`s
   (100 and 200); T6 extracts `RequestContextInputs` from `Accept-Language`, `Tellma-Calendar`,
   `Tellma-Time-Zone`; T10 is the only background caller of the scope factory; the migrator's steps
   run under `Kind = System`.
10. **Platform exceptions (T5 types, T6 mapping).** This theme's exceptions live in
    `Tellma.Core.Abstractions.Tenancy` and derive from T5's base platform exception if one is
    defined. Mappings needed: `TenantNotFoundException` → `404 tenant_not_found`;
    `TenantUnavailableException` → `503 tenant_provisioning` / `503 catalog_unavailable` (with
    `Retry-After`) / `403 tenant_suspended` / `403 tenant_read_only` by `Code`;
    `InsufficientAssuranceException` → `401` with the `insufficient_user_authentication` challenge;
    CSRF rejection → `403 csrf_rejected`.
11. **Permission evaluation (T4).** Consumes `RequestContext.Subject`, `Kind`, `ClientId`,
    `Assurance` and sets `UserId` through its initializer; a service account's client id resolves
    through the same connect step. The endpoint audit needs T4's securable metadata type and its
    public marker.
12. **Blob staging tokens (T7).** Not touched, except that the upload endpoint joins the CSRF
    middleware with `AcceptsMultipartMetadata`.
13. **Wire shapes (T6).** `/bff/user`, `/api/distribution-info` and the admin bodies follow T6's JSON
    conventions (camelCase, source-generated).
14. **Telemetry names.** D21; two meters named after their packages; no tenant tags on instruments.
16. **Connect-call collapse (T4/T5).** The guard's contract is only "after `EnsureAccessAsync`
    returns, `UserId` is set or the request was refused"; the tenant middleware touches no tenant
    database, so whatever T4/T5 fold into the first business round trip is invisible here provided
    the optimistic path still yields `404 tenant_not_found` before any row is returned.
17. **Vocabulary.** D24.

---

## 7. Departures from ARCHITECTURE.md

| ARCHITECTURE.md says | This design | Why |
|---|---|---|
| "its own Catalog DB … reuses `Tellma.Core`'s sharding code unchanged" | One catalog database per distribution, written fresh; the Elastic Database client library is not used. | No such code exists; the shard-map ecosystem is contracting and its credential model is per-shard logins. |
| `samples/tellma-sample-distribution/` (layout) vs `distributions/<slug>/` (phasing) | `distributions/acme/` in distribution-repo shape; the `samples/` entry is removed from the layout tree. | One location; graduation is a folder move. |
| `Tellma.Core` provides the CRUD stack base "including generated endpoints"; `Tellma.Core.Webhooks` is the one project with a framework reference | New `Tellma.Core.AspNetCore` owns everything with an ASP.NET dependency; `Tellma.Core` stays host-agnostic; `Tellma.Core` → `Tellma.Core.EntityFrameworkCore` → `Microsoft.EntityFrameworkCore.SqlServer` added. | The migrator and workers compose Core without the web stack; Queryex, TVP binding and the catalog need EF. |
| "Only the distribution generates and ships migrations" | The catalog schema ships platform-owned migrations in `Tellma.Core.Migrator`. | The schema is identical across distributions; `Tellma.Identity.Migrations` is the precedent. |
| `<Slug>DbContext.cs` in the distribution | `TellmaDbContext` is platform-owned; a derived context is optional. | Zero distribution code for the common case. |
| Feature edges `Requires`/`Recommends`/`Excludes`, slots, providers, cardinality, manifest generator, Builder Tool, bypass analyzer | `Requires` only; explicit `AddFeature<T>()`; one aggregated exception per gate; the endpoint audit covers the bypass failure mode. | Minimal fidelity for the first release; additive later. |
| A distribution on the shared authority references none of the identity projects | The reference distribution references the engine and selects in-proc by configuration. | Local development with no shared services (Guiding Principles; spec 0003 §10.4). |
| Reserved-slug list | Adds `acme` as taken; `taxonomy.json` gains `reservedSlugs` and `distributions`. | D2. |
| "Every distribution reads its connection strings … from its own Key Vault" | SaaS holds no tenant connection secrets at all; Key Vault holds the OIDC secrets and any on-prem-style profile secret only. | Managed identity + contained users. |
| Tenant suspension: soft = read-only, hard = lockout | `ReadOnly`/`Suspended`/`Retired` with the verdict table; `Retired` answers 404. | Makes soft/hard concrete. |
| "Permissions model under ad-hoc SQL" (open question) | Answered: `tellma_app` role, grants recomputed from the model after every migrate. | D8. |
| Migrator flags `--all-tenants` / `--tenant <id>` | `migrate`, `provision`, `set-state`, `status` with the flags of D22. | Catalog and provisioning need their own commands. |
| `Tellma.dev.<worktree-id>.*` database names; `launchSettings.template.json` | Development names are `Tellma.dev.<slug>.*`; `launchSettings.json` is tracked in Phase 1. | The `dotnet tellma` CLI does not exist yet. |
| Startup validates "before the application serves traffic" (implied fail-fast) | An unreachable catalog does not fail startup; readiness reports it; a *behind* catalog does fail startup. | Slot swaps must not crash-loop on a transient outage. |
| In-proc identity store is "the distribution's own database" (spec 0003 §2.2) | The catalog database, schema `idsvr`. | The only distribution-wide database. |

---

## 8. Verification

Relied on from `research/host-tenancy.md` (verified there 2026-09-01): PAR and
`PushedAuthorizationBehavior.Require`; no first-party BFF, no built-in token refresh (dotnet/aspnetcore#8175
moved to .NET 12 planning), Duende licensing; cookie auth answers 401/403 on `IApiEndpointMetadata`
endpoints; RFC 10017 cookie and CSRF requirements; the .NET 10 antiforgery doc leaving JSON endpoints
unenforced; `SameSite` defaults; `ITicketStore` members and the `ValidatePrincipal` per-request cost
warning; Duende's server-side session design; Finbuckle 10.1.3 facts (EF ≥ 10.0.11, per-tenant-DB
recipe incompatible with pooling); EF pooling documentation and `SetConnectionString` caveats;
SqlClient pool-per-connection-string, `Max Pool Size` 100, idle drain 4–8 min; App Service Key Vault
references (24 h cache, literal string on failure); `Azure.Extensions.AspNetCore.Configuration.Secrets`
1.5.2 and Key Vault limits; managed-identity SqlClient modes, `CREATE USER … FROM EXTERNAL PROVIDER`,
Entra-only authentication, SqlClient 7.0's Azure split; the shard-map catalog storing location, not
credentials; DPAPI Windows-only and the on-prem shapes; elastic-pool database, session (30,000) and
login/worker limits; no cross-database queries or `USE` on Azure SQL; `AsyncLocal`/`HttpContextAccessor`/
`Activity.Current` semantics and the framework guidance for background work; route groups with
parameters and constraints, filter ordering, filters seeing bound arguments (inference recorded there).

Relied on from sibling research files as summarised in the briefing's §8: MCP 2026-07-28 (stateless,
RFC 9728 path-inserted PRM, RFC 8707 `resource`, CIMD preferred, DCR deprecated); C# SDK 2.2.0
(`MapMcp` with route values, request filters); OpenIddict 7.6.1 (RFC 8707 with exact-match `rsrc:`
checks, no PRM/DCR/CIMD); fallback-policy and `AllowAnonymous` semantics; `-u-ca-` culture traps and
the absence of a standard time-zone header; App Service drain and `Always On`; Azure SignalR
`CloseOnAuthenticationExpiration`; EF SqlServer 10.0.11 requiring SqlClient ≥ 6.1.6.

Verified by the judge against the repository on 2026-09-01: `Tellma.Core.csproj` references only
`Tellma.Core.Abstractions`; only `Tellma.Identity` and `Tellma.Core.Webhooks` take a
`FrameworkReference`; `Directory.Packages.props` pins neither `Microsoft.AspNetCore.Authentication.OpenIdConnect`
nor `…JwtBearer` (both need pins at the repo's ASP.NET line), and does pin
`Azure.Extensions.AspNetCore.DataProtection.Blobs` 1.5.3 / `.Keys` 1.6.3, `Serilog.AspNetCore` 10.0.0,
`OpenTelemetry.Extensions.Hosting` 1.16.0, `Microsoft.EntityFrameworkCore.SqlServer` 10.0.9,
`Microsoft.Data.SqlClient` 6.1.1; `TellmaIdentitySeedClientKind` has `Cli` and `Native` (no
`Distribution`); `PolicyConstants.AcrAuthTime = "tellma_acr_auth_time"`;
`IClientProvisioningService.CreateDistributionAsync` exists and `TellmaClientProperties.BackchannelLogoutUri = "tellma:backchannel_logout_uri"`;
`WebhookEndpointMetadata` exists; spec 0001 emits `GRANT EXECUTE ON TYPE::<type> TO <principal>` for a
configurable principal set with every type version; `distributions/` and `taxonomy.json` do not
exist; `samples/` and `templates/` exist. ARCHITECTURE.md dependency rule 2 (no Module → Core edge)
and rule 6 (optional Core-layer packages depend only on Abstractions and are never referenced by
`Tellma.Core`) were read and respected: `Tellma.Core.AspNetCore` and `Tellma.Core.Migrator` depend on
`Tellma.Core` (they are adapters of it, not optional peers), and the feature contract sits in
Abstractions.

Facts asserted by proposals and accepted because the proposal named a primary source the judge could
not cheaply re-read: `OpenIdConnectHandler.HandleRemoteSignOutAsync` is front-channel only (dotnet/aspnetcore
`main`); EF Core's `RelationalExtensionInfo.GetServiceProviderHashCode() => 0` and the SQL Server
extension comparing engine type and compatibility levels only (EF source checkout 2026-06-09); OIDC
Back-Channel Logout 1.0's required claims and the `sub`-or-`sid` rule; `Microsoft.Extensions.Configuration.KeyPerFile`
10.0.11 on NuGet; systemd `LoadCredential` and `$CREDENTIALS_DIRECTORY`.

Still unverified (implementation must confirm): an executable-to-executable `ProjectReference`
(Migrator → Web) under the Web SDK (fallback named in D2); that a managed identity which owns a
database may run `CREATE USER … FROM EXTERNAL PROVIDER` without being the server's Entra admin
(provisioning-time check in the first Azure deployment); whether the MCP C# SDK's `AddMcp` can serve a
per-route PRM document (D18 serves it from a platform endpoint regardless); the cookie handler's
renewal cadence with a session store and the resulting `RenewedAt` write frequency; the exact
`CREATE DATABASE … ELASTIC_POOL` syntax and `AS COPY OF` timing; the identity server's fixed audience
value for `tellma_control_plane` (read from spec 0003 §6.2's implementation when writing D20).

---

## 9. Review flags

1. **Session model (D15).** Catalog-backed `ITicketStore` with a 60 s per-instance cache and
   CAS-coordinated refresh, versus a stateless encrypted cookie with a throttled `ValidatePrincipal`
   refresh and a polled revoked-`sid` table: fewer tables and no Data Protection dependency, at the
   price of a 3–5 KB cookie per call, a ten-minute revocation bound and no session listing.
2. **Catalog placement (D7).** A dedicated catalog database always, versus allowing the `catalog`
   schema to co-locate with the live database by configuration for on-prem single-customer installs
   (no extra code, one fewer database, but a restore hazard and a two-contexts-one-database matrix).
3. **`today()` (D13).** Tenant-zone business date per spec 0008, versus the user's zone (one line in
   T3's initializer plus an amendment note in spec 0011; two users of one tenant would then disagree
   on today's postings).
4. **Refresh interval (D9).** 15 s versus 60 s (a quarter of the probes; a one-minute suspension
   latency) or 5 s.
5. **Membership self-heal (D14).** Deploy-time reconciliation only, versus the connect step also
   upserting the hint at most once per 24 h per (subject, tenant) — repairs a failed post-commit write
   without waiting for a deploy, at the cost of a catalog write on T4's hot path.
6. **MCP audience (D18).** The origin-prefix normalisation on the identity server (requested
   `resource` copied into `aud`), versus per-tenant resource registration (identity state per tenant)
   or a permanent distribution-wide audience with membership as the only isolation.
7. **`Provisioning` verdict (D10).** `503` with `Retry-After` (reveals that a provisioning tenant
   exists to any authenticated user of the distribution) versus `404` (indistinguishable from
   absent; the admin surface shows the state).
8. **`ReadOnly` and background work (D10).** Scope created with read-only handlers only, versus
   pausing all background work for a read-only tenant.
9. **Feature contribution shape (D6).** Records plus realizers (reflectable, serialisable for a
   future catalog), versus an interface per item kind.
10. **`TellmaDbContext` ownership (D23).** Platform-owned default with an optional derived context,
    versus a mandatory distribution `DbContext` as ARCHITECTURE.md's layout shows.
11. **Migrator project shape (D2).** Migrator references the Web executable, versus a third
    class-library project holding the composition and entities.
12. **Admin surface (D20).** Ship the minimal surface now (cheap because bearer authentication exists
    for MCP), versus deferring it wholesale until a control plane exists.
13. **CSRF header name (D17).** `Tellma-Client` (RFC 6648-clean, doubles as the build tag), versus
    `X-Requested-With: XMLHttpRequest` (needs no SPA change in libraries that set it by default).
14. **Sandbox context when unbound (D10).** Throw, versus treat as sandbox (fail-safe but hides the
    bug behind "mail not delivered").
15. **Explicit feature selection (D6).** `AddFeature<T>()` per pack, versus assembly scanning
    (`AddFeaturesFrom(assembly)`).

---

## 10. Conflicts

1. **T2 (data access).** Owns `TellmaDbContext`'s surface beyond the registration in D23 and the
   pooled-factory choice in D8; must consume `ITenantConnectionProvider`/`ITenantConnectionFactory`
   and never a connection string; must throw `TenantNotFoundException` from a failed deferred
   connect verification; must tag executor spans with `tellma.db.role`; must ratify `nvarchar(16)`
   enum columns and plural table names; must bump `Microsoft.Data.SqlClient` to 6.1.6 (this theme
   assumes it).
2. **T3 (settings, localization).** Implements `IRequestContextInitializer` at `Order` 200 filling
   `Language`, `Culture`, `Calendar`, `TimeZone`, `TenantTimeZone`, `Today` (tenant zone); must
   ratify the calendar codes `gc | uq | et` and the precedence header → user → tenant → platform;
   must treat `CatalogState.Version` as an example of the opaque-GUID tag convention, not a tag home.
3. **T4 (users, roles, permissions).** Implements `IRequestContextInitializer` at `Order` 100 (the
   connect step) answering `404 tenant_not_found` for non-members and deactivated users; maps a
   service account's client id to a tenant user; defines the reserved system user that
   `Kind = System` binds; supplies the securable metadata type and public marker the endpoint audit
   requires; decides whether the connect step may answer from cache with a deferred verification
   (seam 16); does not write the membership hint from the connect step unless review flag 5 is taken.
4. **T5 (service pipeline).** Defines the base platform exception this theme's exceptions derive
   from, or accepts them as standalone; contributes `StackContributionItem` and its realizer; marks
   sensitive operations with `RequireAssuranceMetadata`.
5. **T6 (web API, MCP).** Ratifies the header names `Tellma-Client`, `Tellma-Calendar`,
   `Tellma-Time-Zone`; maps this theme's exceptions and codes to problem details; stamps
   `TenantEndpointMetadata(Surface, IsMutation)` from the securable's action kind on every projected
   endpoint; maps `/{tenantId}/mcp` in stateless mode with the access guard in the MCP request
   filters; serves the PRM document shape of D18; owns the MCP tool list and server name; must not
   offer a client-asserted "today" header.
6. **T7 (blobs).** The upload endpoint declares `AcceptsMultipartMetadata` and still requires the
   CSRF header; blob GETs are cookie-authenticated, side-effect free and origin-checked; tenant
   scoping of paths derives from `RequestContext.Tenant.Id`, never from a registry entry.
7. **T8 (Core and GL stacks).** `GlFeature` declares `[Requires<CoreFeature>]` and contributes
   through the items of D6; `UserService` calls `ITenantMembershipDirectory.RecordAsync` post-commit
   and raises `ISessionTerminationListener.TenantAccessRevokedAsync` on deactivation; the tenant
   bootstrap and versioned seeds are `ITenantProvisioningStep`s recorded in `dbo.__TellmaProvisioning`
   (T8 must not introduce a separate `__SeedHistory`); T8 adds `Gl` to the `taxonomy.json` this
   theme creates.
8. **T10 (background tasks, inbox).** Stores `RequestContextSnapshot` fields as job-row columns and
   is the only background caller of `ITenantScopeFactory`; implements `ITenantStateListener`
   (close connections, pause jobs; read-only handlers only under `ReadOnly`) and
   `ISessionTerminationListener` over the hub with per-user-per-tenant groups; hosts the session
   sweep and, optionally, a nightly membership reconciliation reusing §4's statements; never adds
   cross-tenant coordination tables to the catalog.
9. **Identity server (spec 0003 amendment).** Must add: `TellmaIdentitySeedClientKind.Distribution`
   with stable secrets (D19); the origin-prefix resource normalisation and CIMD (D18); a grant of each
   distribution's origin to the control-plane client, or an agreed fixed audience for the admin
   surface (D20); optionally a `tellma_kind` claim (D15).
