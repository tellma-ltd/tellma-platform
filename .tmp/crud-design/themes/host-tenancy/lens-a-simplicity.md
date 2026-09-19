# Distribution host and multi-tenancy — design for the simplest possible distribution

Theme `host-tenancy`, future spec `0010-distribution-host-and-multitenancy.md`. Optimised for the
distribution author (a coding agent) writing the least and most mechanical code, and for the
external AI agent that must reach a tenant through MCP without ceremony. Every position below is
concrete enough to be written into the spec; where an equally plausible alternative exists it is
marked as a review flag.

The yardstick used throughout: **what does the reference distribution write?** The answer this
design lands on is three files of substance — a `Program.cs` of eight lines, a composition class of
about ten lines, a one-line migrator `Program.cs` — plus configuration. Nothing in this theme asks a
distribution to write a `DbContext`, an authentication setup, a tenant resolver, a session store, a
CSRF filter, a migrator, or an MCP host.

---

## 1. Critique

### 1.1 General design

The brain dump gets the shape of the problem right: distributions are multi-tenant by construction,
tenants are Live or Sandbox, the tenant id is in the URL, connection information is heavily read and
rarely changed, secrets must not sit in a table, and the "which tenants am I a member of" question
needs a seam from day one. Four things are missing or underspecified, and they are the bulk of what
this theme has to decide.

1. **The distribution-side half of the identity design is absent.** Spec 0003 obliges every
   distribution to be a confidential OIDC client using the BFF pattern, to expose a
   `backchannel_logout_uri` that "validates the token's signature and `sid` and kills its session",
   to keep a session cookie sliding for seven days tied to a server-held refresh token, to close
   SignalR connections on session end, and to answer under-assured sensitive operations with a
   `401 insufficient_user_authentication` challenge. None of this appears in the brain dump, and
   none of it is provided by the framework: .NET 10 ships no BFF package and no cookie token
   refresh, Duende.BFF is a paid license for every customer-facing distribution, and a stateless
   cookie cannot honour back-channel logout by itself. The platform has to own roughly two hundred
   lines here, and the design has to say where revoked sessions live in a multi-instance deployment.

2. **The tenant registry conflates three things.** `ResolveConnectionString(tenantId) -> conn
   string` returns a secret-bearing string; `RegisterConnectionString(tenantId, connString)` implies
   the catalog stores one secret per tenant. Under managed identity on Azure the tenant database
   has no password at all — the catalog row is server + database, and the distribution's single
   identity is granted a contained user in each database. The registry should answer *where* a
   tenant is (a `TenantDescriptor`), and connection composition should be a separate concern fed by
   one configured credential template. The brain dump's own question ("if each db gets a password,
   where do we store these passwords? Is storing them in a DB a good practice?") dissolves once no
   database gets a password.

3. **"Catalog table inside the live DB for single-live distributions" is the wrong economy.** It
   saves one database and costs a second code path, puts topology inside a tenant's data (a
   tenant backup/restore then drags the distribution's routing table with it), and leaves nowhere
   to keep distribution-wide state that the BFF needs anyway: revoked `sid`s from back-channel
   logout, the membership hint table, and — for in-proc identity — the `idsvr` schema that spec
   0003 §2.2 says lives in "the distribution's own database". A catalog database costs nothing
   in an elastic pool and one `CREATE DATABASE` on-prem. Every distribution gets one.

4. **Request context and background scopes are not mentioned.** Tenant, user, culture, calendar and
   time zone reach services somehow; the brain dump asks about an `X-Today` header in the web-layer
   section and leaves the rest implicit. The `ISandboxContext` contract already in Abstractions
   demands the answer: it must resolve in request scopes *and* worker scopes. `AsyncLocal` must not
   be the source of truth (a value set in a callee does not flow back, fire-and-forget inherits
   stale context, pooled consumers observe the wrong tenant); the answer is an explicit scoped
   holder populated once per request by middleware and once per job by a scope factory from a
   copied snapshot.

### 1.2 Detailed choices

- **Layer names.** "Data / Service / Web" is the vocabulary ARCHITECTURE.md already uses, and it is
  fine. What the distribution actually contains is narrower than three layers: entity classes, the
  occasional service customisation, the occasional custom endpoint. The folder names should be the
  names of the things a distribution adds — `Entities/`, `Services/`, `Endpoints/` — not
  Clean-Architecture layer names, which would imply an infrastructure layer the distribution does
  not own.

- **Route shape is internally inconsistent.** "Dedicated API per client" says
  `{tenantId}/api/web/...`; "Multi-tenancy" says `api/{tenantId}/documents`. The first is right: the
  tenant is the top-level namespace of every tenant-scoped surface (web API, public API, MCP, hub,
  blobs), and an integer-constrained leading segment does not collide with the non-tenant surfaces
  (`/bff/*`, `/api/distribution-info`, `/api/webhooks/*`, `/id/*`, `/health`).

- **"Keys and secrets are never stored in the DB in clear text"** implies encrypted-in-DB is
  acceptable. The stronger and simpler rule is that the catalog stores no secrets in any form; the
  only secrets a distribution has (OIDC client secret, service-client secret, an on-prem SQL
  password when integrated security is impossible, a SignalR key) live in configuration, which is
  fed by Key Vault in SaaS and by environment or key-per-file directories on-prem.

- **"Fan out a request to all the sandboxes" for single-live membership** is correct in spirit
  but unnecessary once a catalog exists for every distribution: a membership hint table maintained
  by the user service and reconciled by the migrator serves both shapes, and the tenant access
  guard stays authoritative.

- **The MCP question** ("one server per tenant, or one for the distro?") has a clean answer once
  the MCP authorization model is in view: the current MCP revision requires an RFC 8707 `resource`
  equal to the MCP server URI and an audience-bound token, so a per-tenant endpoint gives
  per-tenant audiences for free and one endpoint per distribution would need tenant selection
  inside every tool call. Per tenant.

- **Aggressive caching of routing info** is right but must be paired with the suspension hook
  ARCHITECTURE.md already describes (set state, invalidate cache, refuse traffic). A bounded TTL
  plus explicit invalidation is enough; there is no need for a change bus.

### 1.3 Gaps the spec must fill that the brain dump does not name

- The catalog database's own schema and who migrates it (the platform, not the distribution).
- Tenant states beyond Live/Sandbox: provisioning, read-only, suspended, retired, and what each
  refuses.
- The in-proc identity mode's need for the distribution's own BFF client with a *stable* secret:
  the identity engine's `CreateDistributionAsync` regenerates secrets on every call and the seeder
  knows only `Cli`, `Native` and `ControlPlane` client kinds. A required identity-engine addition.
- The MCP authorization gap on the identity server: no protected-resource metadata (served by the
  distribution, fine), no CIMD, no DCR, static resource registration. The spec must list what the
  identity server gains.
- `PublicOrigin` (`https://<slug>.app.tellma.com` in SaaS, arbitrary on-prem) as required
  configuration: redirect URIs, MCP resource identifiers, back-channel URIs and CSRF origin checks
  all derive from it.
- Health endpoints, forwarded-headers posture, Data Protection key ring sharing for the cookie
  across instances, OpenTelemetry wiring — the same decisions the identity host already made,
  which the distribution must not have to re-make.
- `taxonomy.json` does not exist; `samples/` and `templates/` are `.gitkeep` only; `distributions/`
  does not exist. The spec creates the folder and the registry file.

---

## 2. Decisions

### D1 — The reference distribution lives at `distributions/acme/`, slug `acme`

**Decision.** The reference distribution is a Phase-1 distribution under `distributions/acme/`, with
namespace and project prefix `Tellma.Distro.Acme`. It is deployable (it is the platform's own
showcase and the CI smoke distribution), so `acme` is added to the reserved-slug list in
`taxonomy.json` (D3) and no third party may claim `acme.app.tellma.com`.

```
distributions/acme/
├── README.md
├── src/
│   ├── Tellma.Distro.Acme.Web/
│   │   ├── Program.cs                    # 8 lines: AddTellma / UseTellma / MapTellma
│   │   ├── AcmeDistribution.cs           # the composition: Compose(TellmaBuilder)
│   │   ├── Entities/                     # sealed leaves + distro-only entities (empty at first)
│   │   ├── Services/                     # custom services and validators (empty at first)
│   │   ├── Endpoints/                    # custom endpoints (empty at first)
│   │   ├── Properties/launchSettings.json
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── wwwroot/                      # SPA output later; a placeholder index.html now
│   │   └── Tellma.Distro.Acme.Web.csproj
│   └── Tellma.Distro.Acme.Migrator/
│       ├── Program.cs                    # 1 line: return await TellmaMigrator.RunAsync(args, "acme", AcmeDistribution.Compose);
│       ├── Migrations/                   # EF migrations + snapshot for the tenant model
│       └── Tellma.Distro.Acme.Migrator.csproj
└── test/
    ├── Tellma.Distro.Acme.Web.Tests/          # composition, architecture (one-way dependency), publish closure
    └── Tellma.Distro.Acme.IntegrationTests/   # Category=Integration: boots the host with in-proc identity against SQL
```

**Rationale.** ARCHITECTURE.md's phasing text names `distributions/<slug>/` for Phase 1; its layout
tree names `samples/tellma-sample-distribution/` for the *target* architecture's smoke distribution.
The reference distribution is the first Phase-1 distribution and the template source, not a sample:
it runs CI end to end, it is where every later spec's "reference stack" lands, and it graduates to
its own repo the same way a customer distribution does. `acme` is already the slug the identity
in-proc test host uses, it is four characters, universally read as a placeholder company, and not in
the reserved list. `sample`, `demo`, `test` and `dev` are reserved precisely so that placeholder
names cannot become phishing-friendly hosts.

**Alternatives rejected.** `samples/` (wrong phase, wrong purpose); `reference` (reads as a category,
and a template-derived agent is less likely to notice it as a placeholder); `banan` (customer-zero
is a real business and should be its own distribution).

**Confidence.** Medium. **Review flag:** slug choice; Ahmad may prefer `reference` or a name he has
already used elsewhere.

### D2 — Layer vocabulary stays Data / Service / Web; folders are `Entities/`, `Services/`, `Endpoints/`

**Decision.** The prose vocabulary is the one ARCHITECTURE.md uses. Inside the Web project the
folders are named for the artifacts a distribution adds. There is no `Data/` folder because the
distribution owns no data-access code: no `DbContext`, no repository, no SQL. There is no per-feature
(vertical-slice) folder structure.

**Rationale.** A distribution's typical entity has no custom service and no custom endpoint; a
vertical-slice folder per entity would be dozens of one-file folders. "Put the entity in
`Entities/`" is the most mechanical rule an agent can follow. Clean-Architecture names
(Domain/Application/Infrastructure/Presentation) describe a layering the distribution does not own.

**Confidence.** High.

### D3 — `taxonomy.json` is created by this spec

**Decision.** `taxonomy.json` at the repo root:

```jsonc
{
  "modules": [],                // T8 adds "Gl"
  "compliance": [],
  "reservedSlugs": ["core", "landing", "identity", "platform", "tellma", "www", "app", "api",
                    "admin", "auth", "login", "secure", "support", "help", "training", "status",
                    "demo", "test", "staging", "sample", "dev", "acme"],
  "distributions": ["acme"]     // slugs in use inside this repo (Phase 1)
}
```

A unit test in `Tellma.Core.Tests` asserts every `distributions/<slug>/` folder is listed, every
listed slug is valid (lowercase, starts with a letter, ≤ 15 chars), and no distribution slug is in
`reservedSlugs` except through `distributions` (the reference distribution is the one exception,
listed in both).

**Rationale.** The breakdown notes the file does not exist and T8 needs the modules registry; the
slug registry is the natural place to make "reserved" enforceable.

**Confidence.** High. Conflict with T8 recorded in §6.

### D4 — Two projects; the migrator references the Web project; the composition is one static method

**Decision.** `Tellma.Distro.Acme.Migrator` is a console project referencing
`Tellma.Distro.Acme.Web`, `Tellma.Core.Migrator`, `Tellma.Core.EntityFrameworkCore.Design` and
`Microsoft.EntityFrameworkCore.Design`. The Web project references `Tellma.Core`,
`Tellma.Core.EntityFrameworkCore` and the packs it selects; never a Design package. Both entry points
call the same `AcmeDistribution.Compose`:

```csharp
// distributions/acme/src/Tellma.Distro.Acme.Web/Program.cs
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddTellma("acme", AcmeDistribution.Compose);
WebApplication app = builder.Build();
app.UseTellma();
app.MapTellma();
app.Run();
```

```csharp
// distributions/acme/src/Tellma.Distro.Acme.Web/AcmeDistribution.cs
namespace Tellma.Distro.Acme.Web
{
    /// <summary>The Acme distribution's composition: the features it selects and how it configures them.</summary>
    public static class AcmeDistribution
    {
        /// <summary>Selects and configures the features; shared by the web host and the migrator.</summary>
        public static void Compose(TellmaBuilder tellma)
        {
            // Email is composed explicitly by every host; the Core feature's startup check demands a sender.
            tellma.Services.AddTellmaEmail();
            tellma.Services.AddSmtpEmail(tellma.Configuration);

            tellma.AddGl();   // Tellma.Module.Gl: Centers (T8)
        }
    }
}
```

```csharp
// distributions/acme/src/Tellma.Distro.Acme.Migrator/Program.cs
return await Tellma.Core.Migrator.TellmaMigrator.RunAsync(args, "acme", Tellma.Distro.Acme.Web.AcmeDistribution.Compose);
```

`TellmaMigrator.RunAsync` builds a generic host (`Host.CreateApplicationBuilder`) *before* it
dispatches a command, so `dotnet ef` obtains the `TellmaDbContext` through EF's host-factory
resolver with no `IDesignTimeDbContextFactory` in the distribution. The Web project is an
executable; referencing it from the migrator is the shape ARCHITECTURE.md prescribes and the
identity repo already exercises the executable-to-library direction (`Tellma.Identity.Migrations`
is referenced by hosts). If exe-to-exe reference turns out to fight the SDK (duplicate
`appsettings.json` content items, `Program` type visibility), the fallback is a third project
`Tellma.Distro.Acme` (class library holding `AcmeDistribution` and `Entities/`) referenced by both —
the migrator `Program.cs` stays one line either way.

**Confidence.** Medium on exe-to-exe; the CI design-e2e leg is where it is proven. **Review flag:**
two projects vs three.

### D5 — `AddTellma` / `UseTellma` / `MapTellma`, and what they do

**Decision.** Three extension methods in `Tellma.Core`, mirroring the identity engine's
`AddTellmaIdentity` / `UseTellmaIdentity` / `MapTellmaIdentity` precedent.

`AddTellma(this WebApplicationBuilder builder, string slug, Action<TellmaBuilder> compose)`:
1. Binds `TellmaOptions` from the `Tellma` configuration section, `ValidateOnStart`, with the secrets
   guard of D15.
2. Registers `DeploymentIdentity(slug, environment)`, `TimeProvider.System`, `AddHttpContextAccessor`,
   `AddMetrics`, Serilog from the `Serilog` section, OpenTelemetry (platform meters and activity
   sources, ASP.NET Core, HttpClient and SqlClient instrumentation; Azure Monitor gated on
   `APPLICATIONINSIGHTS_CONNECTION_STRING`, as the identity host does).
3. Runs the composition (D6): constructs a `TellmaBuilder`, adds the Core feature itself, invokes
   `compose`, validates the graph, and contributes every feature in dependency order.
4. Registers the tenancy runtime (D9–D13): catalog context and cache, connection composition,
   request-context holder, `ISandboxContext`, tenant scope factory, membership directory.
5. Registers authentication and authorization (D18–D22): cookie + OIDC (BFF), JWT bearer for the
   bearer surfaces, the policy scheme, the fallback policy, the CSRF middleware's options, the
   session-refresh and revoked-session services, in-proc identity when configured.
6. Registers `TellmaDbContext` (scoped; options built per scope from the bound tenant) and the
   catalog `TellmaCatalogDbContext`.
7. Registers the startup gate (D7) as the *first* hosted service.

`UseTellma(this WebApplication app)` installs the pipeline in this order, and nothing else:
1. Forwarded headers, guarded exactly as the identity host guards them (explicit
   `ForwardedHeaders:KnownProxies|KnownNetworks`; refuse `ASPNETCORE_FORWARDEDHEADERS_ENABLED`;
   refuse enabled-but-empty).
2. Serilog request logging.
3. Exception handler with RFC 9457 problem details (the exception-to-status mapping is T6's).
4. HSTS and HTTPS redirection outside Development.
5. `UseRouting`.
6. In-proc identity (`UseTellmaIdentity`) when `Tellma:Identity:Mode = InProc`.
7. `UseAuthentication`.
8. CSRF middleware (D20) — runs for cookie-authenticated non-safe requests only.
9. `UseAuthorization`.
10. Tenant middleware (D10) — after authorization so an anonymous probe gets 401 before it can learn
    whether a tenant id exists, and before the endpoint so handler-argument binding sees a bound
    tenant.

`MapTellma(this WebApplication app)` maps: `/api/distribution-info` (D25), `/health` and
`/health/ready`, `/bff/*` (D18), `/api/webhooks/{key}` (`MapTellmaWebhooks`, when
`Tellma.Core.Webhooks` is composed), the in-proc identity endpoints, the tenant route groups (D11)
populated by feature contributions (T6's projected endpoints, the SignalR hub of T10, the MCP
endpoint of T6, the blob endpoints of T7), the control-plane admin surface (D24), static assets and
the SPA fallback (`MapFallbackToFile("index.html")` when `wwwroot/index.html` exists, excluding
`/api`, `/bff`, `/id`, `/health` and `/{tenantId:int}/api|mcp|hub|blobs`).

Custom middleware goes between `UseTellma()` and `MapTellma()`; custom endpoints are contributed
through the feature contract (`FeatureContext.MapTenantEndpoints`), never by calling `app.MapPost`
directly on a tenant path — a startup check refuses tenant-path endpoints that did not come through
the group (they would lack the access filter).

**Rationale.** Pipeline order is the thing agents get wrong (the identity implementation record
notes `UseRouting` before `UseTellmaIdentity` as a hard-won fact); encapsulating it removes the
class of bug. The Add/Use/Map trio keeps the escape hatch (custom middleware between the two) that a
single `TellmaDistribution.Run(args)` would hide.

**Confidence.** High.

### D6 — Feature composition at minimal fidelity: `Tellma.Core.Composition`, `Requires` only, explicit selection

**Decision.** A new, small package **`Tellma.Core.Composition`** holds the feature contract. It
depends on `Tellma.Core.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`,
`Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`,
`Microsoft.EntityFrameworkCore.Relational`, and takes `<FrameworkReference Include="Microsoft.AspNetCore.App"/>`.
Module packs (`Tellma.Module.<m>`) reference it to ship a feature; `Tellma.Core` references it to
realise features. `Tellma.Core.Abstractions` stays package-free, which is why the contract cannot
live there: contributing to DI needs `IServiceCollection`, contributing to the model needs
`ModelBuilder`, contributing endpoints needs `IEndpointRouteBuilder`.

The contract (full C# in §3.2):

- `ITellmaFeature` — `Name` and `Contribute(FeatureContext)`; `TellmaFeature` is the abstract base
  with `Name` defaulting to the type name.
- `[Requires<TFeature>]` — the only edge kind in this release. `Recommends`, `Excludes`, slot
  cardinality, the manifest source generator, the Builder tool and the bypass analyzer are
  ARCHITECTURE.md's target and are not built now.
- `FeatureContext` — `Services`, `Configuration`, `Environment`, `ConfigureModel(Action<ModelBuilder>)`,
  `MapTenantEndpoints(Action<IEndpointRouteBuilder>)`, `MapHostEndpoints(Action<IEndpointRouteBuilder>)`,
  `AddStack(StackDeclaration)`, `AddStartupCheck(Func<IServiceProvider, IEnumerable<CompositionProblem>>)`,
  `AddFeature(ITellmaFeature)` (a feature may contribute sub-features).
- `TellmaBuilder` — what `Compose` receives: `Services`, `Configuration`, `Environment`,
  `AddFeature<TFeature>(Action<TFeature>? configure = null)`, `AddFeature(ITellmaFeature)`. Packs add
  `AddGl(this TellmaBuilder, Action<GlOptions>?)`-style extension methods.
- `StackDeclaration(Type EntityType, Type ServiceType, ...)` — a *declaration* that T5's stack
  machinery in `Tellma.Core` realises (entity + UDTT registration, service, projected endpoints,
  securables). Modules declare stacks; they never reference the machinery.
- `CompositionProblem` and `TellmaCompositionException`.

Composition algorithm inside `AddTellma`:
1. **Collect.** Add `CoreFeature` (users, roles, settings — T3/T4/T8's content) unconditionally, then
   run `compose`. A feature type registered twice is a problem unless both registrations are
   reference-equal (idempotent `AddFeature<T>()` without options).
2. **Declare.** Read `[Requires<T>]` attributes off each feature type.
3. **Validate.** Every required feature must be present (message names the missing type and the
   fix: `tellma.AddFeature<GlFeature>()`); the graph must be acyclic. All problems are aggregated
   into one `TellmaCompositionException` thrown from `AddTellma` — before `Build()`, so a bad
   composition never constructs a host.
4. **Realise.** Contribute in topological order (dependencies first), so a dependent feature's DI
   registrations override its dependencies' (last registration wins for single services; this is
   the override mechanism for "a distro replaces a pack service"). Model actions are recorded and run
   in the same order inside `TellmaDbContext.OnModelCreating`; endpoint actions are recorded and run
   inside `MapTellma`; stack declarations are handed to T5's realiser; startup checks are handed to
   the gate (D7).

Discovery is explicit (`AddFeature<T>()` calls only). No assembly scanning: the manifest source
generator, when it arrives, emits those calls.

**Rationale.** The ARCHITECTURE.md declare/contribute split and one aggregated validation are kept;
everything that needs a catalog, a manifest or a GUI is deferred. Explicit selection is the most
mechanical thing for an agent and hides no edges. The topological contribution order gives the
"distro overrides pack" behaviour without a slot system.

**Alternatives rejected.** Putting the contract in `Tellma.Core.Abstractions` with BCL-only
signatures (untyped registrations, unusable); letting modules reference `Tellma.Core` (rule 2 of the
dependency graph exists for good reasons — modules would bind to concrete Core implementations);
assembly scanning (hidden edges, AOT-hostile).

**Confidence.** High on the shape; medium on the package boundary. **Review flag:** a module
referencing `Tellma.Core.Composition` acquires the ASP.NET Core framework reference transitively.
The alternative is two packages (`Tellma.Core.Composition` without the framework reference and
`Tellma.Core.Composition.AspNetCore` for endpoint contributions); one package is simpler and modules
only ever run inside ASP.NET hosts or the migrator console, which tolerates the framework reference.

### D7 — One startup gate, one exception

**Decision.** `TellmaStartupGate : IHostedService`, registered first by `AddTellma` so it starts before
any other hosted service, runs every contributed startup check inside a scope and throws a single
`TellmaCompositionException` listing every problem. Built-in checks contributed by the Core feature:

- `ISandboxContext` resolves (always true — `AddTellma` registers it — but the check is the contract).
- `DeploymentIdentity` resolves and its `Application` equals the slug passed to `AddTellma`.
- An `IEmailSender` is registered (the Core feature invites users); fix text names `AddTellmaEmail()`.
- Every endpoint under `/{tenantId:int}/` is inside a tenant group (carries `TenantSurfaceMetadata`)
  and none carries `AllowAnonymous`.
- `TellmaOptions.PublicOrigin` is absolute, `https` outside Development, and has no path.
- ICU globalization mode is active (invariant mode breaks the calendar and time-zone work of T3).
- The catalog database is reachable and at the platform's current catalog migration (a missing or
  behind catalog is a startup failure with the exact migrator command in the message; the web app
  never migrates).
- T4 contributes the securables-registry consistency check; T6 contributes the endpoint securable
  audit; both use the same `AddStartupCheck` seam.

`TellmaComposition.Validate(string slug, Action<TellmaBuilder> compose, IConfiguration configuration)`
runs steps 1–3 of D6 plus the host-free subset of the checks, for tests.

**Confidence.** High.

### D8 — Tenant routing: integer id in the leading path segment

**Decision.** Tenant-scoped surfaces are `/{tenantId:int}/api/web/...`, `/{tenantId:int}/api/v1/...`
(reserved; not shipped), `/{tenantId:int}/mcp`, `/{tenantId:int}/hub` (T10), `/{tenantId:int}/blobs/...`
(T7). Tenant ids are positive `int`s allocated from the catalog sequence `catalog.sq_Tenants`;
there is no slug for a tenant. The SPA uses the same leading segment for its own routes
(`/1/gl/centers`), so a deep link and its API calls share a prefix.

**Rationale.** Integers are stable, compact, opaque, and the monolith's users already know them; a
slug would need a rename story and uniqueness rules for no gain (the UI shows names). For an MCP
user configuring `https://acme.app.tellma.com/1/mcp`, an integer is unambiguous. The `int`
constraint is what keeps `/api/distribution-info`, `/bff/*`, `/id/*` and `/health` from ever being
read as tenant routes.

**Alternatives rejected.** `/api/web/{tenantId}` (breaks the "tenant is the namespace" symmetry
across web, MCP, hub and blobs); `/t/{tenantId}` (an extra segment for no disambiguation the
constraint does not already provide); slugs.

**Confidence.** High.

### D9 — Every distribution has exactly one catalog database; the tenant registry is `ITenantCatalog`

**Decision.** Single-live and multi-live distributions share one code path: a catalog database
(`Tellma.<slug>.catalog` on-prem and in development, `tellma-<slug>-catalog` in the SaaS pool) with
schema `catalog` (tables in §4). The single-live distribution's catalog holds one Live row and its
Sandbox rows; the multi-live distribution's catalog holds hundreds. The only difference is
`Tellma:Tenancy:AllowRegistration` (false by default), which gates the `add-tenant` command and the
control-plane `tenants` endpoint.

`ITenantCatalog` (Abstractions, BCL-only) answers *where* and *what state* a tenant is:
`FindAsync(int)`, `ListAsync()`, `RegisterAsync(TenantRegistration)`, `SetStateAsync(int, TenantState)`,
`Invalidate(int)`. The implementation in `Tellma.Core` reads `catalog.Tenants` through
`TellmaCatalogDbContext`, caches every row in a process-wide `ConcurrentDictionary<int, TenantDescriptor>`,
refreshes the whole table every `Tellma:Tenancy:CatalogRefreshSeconds` (default 60), caches
unknown ids negatively for 10 s, and invalidates locally on `SetStateAsync`/`Invalidate`. Propagation
to other instances is bounded by the refresh interval; a suspension therefore takes effect within
one minute fleet-wide and immediately on the instance that received the command.

The catalog is also the home of the distribution-wide tables the BFF needs (`catalog.RevokedSessions`,
D19), the membership hint (`catalog.Memberships`, D14), and the `idsvr` schema when identity runs
in-proc (D22). Its migrations ship with the platform in `Tellma.Core.Migrator` (migrations assembly
`Tellma.Core.Migrator`, history table `catalog.__EFMigrationsHistory`), because its model is fixed by
the platform, exactly as `Tellma.Identity.Migrations` ships the identity schema.

**Rationale.** See critique §1.1 item 3. One store, one code path, ARCHITECTURE.md-aligned, and the
place every other distribution-wide need lands.

**Alternatives rejected.** A configuration-section tenant store for single-live distributions
(zero extra database, but a second store implementation, no home for revoked sessions or the in-proc
identity schema, and a config edit plus deploy to add a sandbox); the brain dump's catalog table
inside the live database (topology inside tenant data; two code paths).

**Confidence.** Medium-high. **Review flag:** the configuration-only store for single-live is the
plausible alternative if the extra database is judged too much for the on-prem single-customer
install.

### D10 — Tenant resolution is middleware; tenant *access* is a service called from two front doors

**Decision.** `TenantMiddleware` (after `UseAuthorization`) reads `tenantId` from the endpoint's
route values. No route value → no tenant is bound and the request proceeds (non-tenant surfaces).
A route value → `ITenantCatalog.FindAsync`; unknown → 404 `tenant-not-found` problem details;
found → the scoped `RequestContextHolder` is bound (`ITenantContext` becomes readable), the request
`Activity` gets `tellma.tenant.id`, and the request proceeds. The middleware does not check state or
membership; it only binds.

`ITenantAccessGuard.EnsureAccessAsync(TenantAccessRequirement requirement, CancellationToken)` is
the one place that decides whether the bound principal may proceed against the bound tenant:

1. Principal kind and subject are taken from the authenticated principal (D21).
2. `IConnectStep.ConnectAsync()` (T4's seam) resolves subject → tenant user (`UserId`, `IsActive`)
   and refreshes version tags; T4 may implement it optimistically against its cache. `NotAMember`
   or `Deactivated` → 404 `tenant-not-found` (identical to a non-existent tenant, so a non-member
   cannot distinguish "no such tenant" from "not yours").
3. Tenant state: `Provisioning`/`Retired` → 404; `Suspended` → 403 `tenant-suspended`;
   `ReadOnly` and the endpoint is a mutation → 403 `tenant-read-only`. Whether an endpoint is a
   mutation is endpoint metadata (`TellmaOperationMetadata.IsMutation`) that T6's projection stamps;
   an endpoint without the metadata is treated as a mutation (fail closed).
4. Assurance: if the endpoint carries `RequireAssuranceMetadata` (D21) and the principal's `acr`
   or assurance freshness is insufficient → `InsufficientAssuranceException` → 401 challenge.

Two front doors call it: `TenantAccessFilter` (an endpoint filter on the tenant route groups) and
the MCP request filters (`AddCallToolFilter`, `AddListToolsFilter`, T6) — because endpoint filters
apply to route-handler endpoints and the MCP SDK maps `RequestDelegate` endpoints. One service, so
the rules cannot drift between the web surface and MCP.

**Rationale.** Middleware binding is what lets a `TellmaDbContext` or any tenant-scoped service be
injected as a handler argument (binding runs before endpoint filters); the guard as a service is
what makes MCP and the web API share one code path.

**Confidence.** High.

### D11 — Route groups the platform owns; features fill them

**Decision.** `MapTellma` builds:

```csharp
RouteGroupBuilder tenant = app.MapGroup("/{tenantId:int}");

RouteGroupBuilder web = tenant.MapGroup("/api/web")
    .RequireAuthorization(TellmaPolicies.WebSession)          // cookie scheme only
    .AddEndpointFilter<TenantAccessFilter>()
    .WithMetadata(new TenantSurfaceMetadata(TellmaSurface.Web));

RouteGroupBuilder bearer = tenant                             // /{tenantId}/mcp, /{tenantId}/api/v1
    .RequireAuthorization(TellmaPolicies.Bearer);             // JWT bearer only
```

`FeatureContext.MapTenantEndpoints(Action<IEndpointRouteBuilder> map)` receives `web`; T6's
projection and a distribution's custom endpoints join it and inherit authorization, the access
filter and the surface metadata with no per-endpoint code. The hub (`/{tenantId:int}/hub`, cookie
scheme, T10) and blobs (`/{tenantId:int}/blobs`, cookie scheme, GET allowed, T7) are sibling groups
built by the same method with the same filter. The MCP endpoint (T6) is `bearer.MapMcp("/mcp")`.

**Confidence.** High.

### D12 — Per-tenant connection resolution: one credential template, no pooling of contexts

**Decision.** `Tellma:Sql:ConnectionString` is a template without `Initial Catalog`; optional
`Tellma:Sql:Servers:{name}:ConnectionString` templates cover multi-server (region) layouts and are
selected by `TenantDescriptor.Server`. `TenantConnectionResolver` composes
`new SqlConnectionStringBuilder(template) { InitialCatalog = descriptor.Database }` (and
`DataSource` from the named server template when `Server` is set), validates it once, and caches the
composed string per tenant id alongside the descriptor. The scoped `ITenantConnection` (in
`Tellma.Core`, for T2's batch executor) exposes `ConnectionString` and `CreateConnection()`.

`TellmaDbContext` is registered with `AddDbContext<TellmaDbContext>((sp, o) => o.UseSqlServer(sp.GetRequiredService<ITenantConnection>().ConnectionString).UseTableTypes("tellma"))`
with scoped options — EF's documented multi-tenant shape. No `DbContext` pooling in this release:
the EF model is cached per context type (the connection string is not part of the model cache key),
context construction is cheap, and the CRUD hot path executes through SqlClient batches (T2) where
the real per-tenant resource is the SqlClient pool, not the context.

Template guidance enforced at startup: `Max Pool Size` must be set and ≤ 50 (default written into
the template as 30), `Min Pool Size` must be 0 or absent, `Encrypt=True` outside Development.
Hundreds of tenants × instances × pool size is what meets the pool's login and session limits, not
the database count.

**Confidence.** High on the shape; medium on "no pooling". **Review flag:** a per-tenant
`PooledDbContextFactory` cached in the resolver is the alternative if T2 finds context construction
on the hot path.

### D13 — Request context: three scoped interfaces over one holder; a snapshot for jobs

**Decision.** `Tellma.Core.Abstractions.Tenancy` gains `ITenantContext`, `IUserContext`,
`ILocaleContext` (C# in §3.1). All three are implemented by one scoped `RequestContextHolder` in
`Tellma.Core`, registered under each interface; the holder is bound exactly once per scope — by
`TenantMiddleware` and the authentication layer in requests, by `ITenantScopeFactory` in jobs — and
throws `InvalidOperationException` on read before binding (`IsBound` exists for the few callers that
must ask). There is no `AsyncLocal` source of truth; `Activity.Current` carries `tellma.tenant.id`
as a diagnostics mirror only.

- `ITenantContext`: `TenantId`, `TenantName`, `Category`, `State`.
- `IUserContext`: `Kind` (`User | ServiceAccount | System`), `Subject` (the `sub`, or the service
  account's client id, or `"system"`), `UserId` (the tenant user id once T4's connect step sets it),
  `Acr`, `AssuranceTime`, `Sid` (users only).
- `ILocaleContext`: `Culture` (a `CultureInfo` for messages and formatting), `CalendarCode`
  (`gc` | `uq` | `et`, T3's vocabulary), `TimeZone` (a `TimeZoneInfo` from an IANA id), and `Today`
  derived as the current date in `TimeZone` — never a client-asserted date.

Precedence for locale values, resolved at binding time by T3's negotiator through the seam
`ILocaleNegotiator.Negotiate(HttpRequest, ConnectedUser?, TenantDefaults)`: request headers
(`Accept-Language`, `Tellma-Calendar`, `Tellma-Time-Zone`; names are T6's to ratify) → the user's
stored preferences (T3/T4) → the tenant's defaults (T3) → platform defaults (`en`, `gc`, `UTC`).
Culture names are stripped of `-u-` extensions before negotiation; the calendar is never encoded in
the culture name.

`RequestContextSnapshot` is the serialisable copy (tenant id, principal kind, subject, user id,
culture name, calendar code, time-zone id) that anything enqueuing background work stores in the
job row; `ITenantScopeFactory.CreateScopeAsync(snapshot)` creates a DI scope, rebinds the holder
from the snapshot (resolving the tenant from the catalog and refusing non-`Active` tenants unless
`allowInactive` is passed), and starts a new `Activity` *linked to* (not parented by) the enqueuing
trace when the snapshot carries one. T10's runner and the migrator's seeding both use it; a job
with `Kind = System` binds the reserved system user T4 defines.

**Rationale.** Three narrow interfaces let a service declare exactly what it needs and keep
`ISandboxContext`'s existing narrowness; one holder keeps binding atomic. The snapshot is what makes
"copy, never capture" mechanical for T10.

**Confidence.** High.

### D14 — Membership lookup: a catalog hint table, written by the user service, reconciled by the migrator

**Decision.** `IMembershipDirectory` (Abstractions): `ListForSubjectAsync(subject)` returns the
tenants a subject probably belongs to; `RecordAsync(records)` upserts hints. Backed by
`catalog.Memberships (Subject, TenantId, IsActive, UpdatedAt)`. Writers: T4/T8's user service after
commit (best effort — a failure is logged, never surfaced to the user); the migrator's per-tenant
pass reconciles the table from `core.Users(Subject, IsActive)` on every deploy and on
`add-tenant`. Readers: `GET /bff/user` (the SPA's company picker), the MCP `list_tenants`-style
tool (T6). The hint grants nothing: `ITenantAccessGuard` is authoritative, and a stale hint shows a
tenant that then answers 404. No fan-out to tenant databases at sign-in in either distribution
shape.

**Rationale.** With a catalog for every distribution (D9) the fan-out the brain dump proposed for
single-live is unnecessary; one implementation serves both shapes and never issues N queries per
sign-in.

**Confidence.** High.

### D15 — Secrets policy

**Decision.**
- The catalog stores no secret in any form. `catalog.Tenants` has no password, key, token or
  connection-string column; a code review rule and a unit test over the catalog model enforce it.
- SaaS: `Tellma:Sql:ConnectionString` carries `Authentication=Active Directory Managed Identity`
  (or `Active Directory Default` on developer machines) and no password; each tenant database gets
  `CREATE USER [<identity>] FROM EXTERNAL PROVIDER` plus `db_datareader`, `db_datawriter`,
  `db_ddladmin` as a built-in provisioning step (D23) run under the migrator's DDL identity.
  `Microsoft.Data.SqlClient` 6.1.x still bundles the Entra modes; the 7.x bump adds
  `Microsoft.Data.SqlClient.Extensions.Azure` and is recorded as a pin-bump obligation (EF Core
  SqlServer 10.0.11 already requires SqlClient ≥ 6.1.6, so the bump from 6.1.1 is due regardless).
- On-prem Windows: `Integrated Security=true` with a service account — no secret.
- On-prem Linux or SQL authentication anywhere: the password arrives through configuration from an
  environment variable, a key-per-file directory (`Microsoft.Extensions.Configuration.KeyPerFile`
  10.0.11, fed by Docker/Kubernetes secrets or systemd `LoadCredential`'s `$CREDENTIALS_DIRECTORY`),
  or the Data-Protection-encrypted secrets file — and only when `Tellma:Sql:AllowSqlPassword = true`.
  Outside Development, a `Password=`/`Pwd=` in any SQL connection string without that flag is a
  startup failure.
- Every configuration value under `Tellma:` that still starts with `@Microsoft.KeyVault(` is an
  unresolved App Service reference and fails startup (the platform's documented failure mode is to
  hand the app the literal reference text).
- Other secrets (`Tellma:Identity:ClientSecret`, `Tellma:Identity:ServiceClientSecret`, the catalog
  connection string when it needs one, `Tellma:SignalR:ConnectionString`) are ordinary configuration
  values: Key Vault references or the Key Vault configuration provider in SaaS, environment or
  key-per-file on-prem, user secrets in Development. No per-tenant secrets exist in this release;
  when a tenant-specific integration needs one it will be a tenant-database column encrypted with
  Data Protection behind an `ITenantSecretStore` seam defined then.
- Data Protection: `Tellma:DataProtection` selects the key ring store (file share, or Azure Blob +
  Key Vault key) using the identity engine's configurator shape; multi-instance deployments must
  share the ring or cookies from one instance are unreadable on another.

**Confidence.** High.

### D16 — `ISandboxContext` implementation

**Decision.** `TenantSandboxContext : ISandboxContext` (scoped, registered by `AddTellma`) returns
`ITenantContext.Category == TenantCategory.Sandbox` and throws `InvalidOperationException("No tenant is bound to the current scope; side effects require a tenant scope.")`
when no tenant is bound. A side-effecting connector called outside a tenant scope is a bug, and the
exception surfaces it on the first call rather than either leaking (treating unbound as live) or
silently withholding (treating unbound as sandbox).

**Confidence.** High. **Review flag:** treating unbound as sandbox (withhold, log) is the fail-safe
alternative; it hides the bug behind "mail not delivered".

### D17 — Tenant states and the suspension hook

**Decision.** `TenantState { Provisioning, Active, ReadOnly, Suspended, Retired }` on the catalog
row. Effects: `Provisioning` and `Retired` answer 404 to every tenant request and run no jobs;
`Suspended` answers 403 `tenant-suspended` to every tenant request, runs no jobs, and closes the
tenant's SignalR connections (T10, on the state-change event); `ReadOnly` refuses mutations with
403 `tenant-read-only`, serves reads, and pauses jobs; `Active` is normal. State changes go through
`ITenantCatalog.SetStateAsync`, which writes the row, invalidates the local cache, and raises
`ITenantStateListener.OnStateChangedAsync` for in-process listeners. Fleet-wide propagation is
bounded by the catalog refresh (D9). The control plane reaches it through the admin surface (D24);
the migrator reaches it through `set-state`.

**Confidence.** High.

### D18 — The BFF: cookie + OIDC handler, three endpoints, one callback pair

**Decision.** Packages `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.11 and
`Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.11 are pinned (both are out-of-band packages,
not shared-framework). `AddTellma` configures:

- Cookie scheme `TellmaAuthentication.SessionScheme = "tellma.session"`: name `__Host-tellma.session`
  (`tellma.session` when the host is not HTTPS in Development), `HttpOnly`, `Secure`, `SameSite=Lax`,
  `Path=/`, `ExpireTimeSpan` 7 days sliding, absolute lifetime `Tellma:Session:AbsoluteLifetimeDays`
  (default 30), `EventsType = TellmaSessionEvents` (D19).
- OIDC scheme `TellmaAuthentication.OidcScheme = "tellma.oidc"`: `Authority = Tellma:Identity:Authority`
  (the in-proc issuer when `Mode = InProc`), `ClientId = slug`, `ClientSecret` from configuration,
  `ResponseType = code`, PKCE (handler default for the code flow), `PushedAuthorizationBehavior = Require`
  (spec 0003 carries assurance and allowed methods inside PAR; a downgrade must fail closed),
  `MapInboundClaims = false` (so `sub` stays `sub`), `GetClaimsFromUserInfoEndpoint = false` (the
  id token carries what the cookie needs), `SaveTokens = true` with the access and id tokens dropped
  in `OnTokenValidated` (only the refresh token is retained, keeping the cookie small), scopes
  `openid profile email offline_access tellma_api`, `CallbackPath = /signin-oidc`,
  `SignedOutCallbackPath = /signout-callback-oidc` — the exact URIs `ClientDescriptorFactory.Distribution`
  registers.
- The cookie principal is pruned to: `sub`, `name`, `email`, `locale`, `sid`, `acr`, `auth_time`,
  the assurance-freshness claim spec 0003 §9.2 defines, and `amr` for audit.
- Endpoints (non-tenant, host-level):
  - `GET /bff/login?returnUrl=&acr_values=&max_age=` — validates `returnUrl` is local, issues the
    OIDC challenge with `acr_values`/`max_age` forwarded and `tellma_allowed_methods` supplied by the
    distribution's policy hook (`IAuthenticationPolicyProvider`, default: no restriction). Anonymous.
  - `POST /bff/logout?scope=global|local` — CSRF-protected; `local` signs out the cookie only;
    `global` (default, per spec 0003 §7.3) also signs out the OIDC scheme so the browser reaches the
    authority's end-session endpoint and returns to `/signout-callback-oidc`.
  - `GET /bff/user` — the signed-in principal plus `IMembershipDirectory.ListForSubjectAsync` —
    `{ sub, name, email, locale, acr, tenants: [{ id, name, category, state }] }`. Returns 401
    (never a redirect: Minimal API endpoints carry `IApiEndpointMetadata`) when unauthenticated.
  - `POST /bff/backchannel-logout` — D19. Anonymous by design (authenticated by the token's signature).
- At login the BFF also writes the readable display-profile cookie `tellma.profile`
  (`{ name, locale }`, not `HttpOnly`, same lifetime) that spec 0003 §7.1 describes for instant launch.

**Confidence.** High.

### D19 — Session validity: throttled refresh in `ValidatePrincipal` plus a polled revoked-`sid` set

**Decision.** `TellmaSessionEvents : CookieAuthenticationEvents` implements `ValidatePrincipal`:

1. If the principal's `sid` is in the in-memory revoked set → `RejectPrincipal()` and sign out.
2. If the ticket's `tellma.refreshed_at` property is older than `Tellma:Session:RefreshIntervalMinutes`
   (default 10, equal to the access-token lifetime) → redeem the stored refresh token at the
   authority's token endpoint (one `HttpClient` call, resilience handler, 5 s timeout).
   - Success: replace the stored refresh token, re-stamp `acr`/`auth_time`/assurance claims from the
     new id token, `ShouldRenew = true`.
   - `invalid_grant` (revoked, rotated-and-reused, security stamp changed, method disallowed,
     user orphaned/disabled): `RejectPrincipal()`.
   - Transport failure: keep the session and record `tellma.auth.session.refreshes{outcome=failed}`;
     reject once staleness exceeds `Tellma:Session:MaxRefreshStalenessMinutes` (default 60).
3. Otherwise accept.

Refresh is what ties the distribution session to the authority's session (revocation within one
interval fleet-wide with no shared state) and what re-evaluates policy (spec 0003 §9.4). Concurrent
requests on the same session refresh once: a per-`sid` single-flight gate in the events class.

Back-channel logout (`POST /bff/backchannel-logout`, `application/x-www-form-urlencoded`,
`logout_token`): validated with `JsonWebTokenHandler` against the OIDC handler's configuration
manager (issuer = authority, audience = slug, asymmetric signing keys from JWKS, `typ = logout+jwt`,
`iat` within 2 minutes, `events` containing `http://schemas.openid.net/event/backchannel-logout`, no
`nonce`, `jti` not seen in the last 5 minutes). On success the `sid` is inserted into
`catalog.RevokedSessions (Sid, Subject, RevokedAt, ExpiresAt = RevokedAt + AbsoluteLifetimeDays)`,
added to the local set, `ISessionEndListener.OnSessionEndedAsync(subject, sid)` fires (T10 closes hub
connections), and the response is 200. Invalid tokens answer 400 `{"error":"invalid_request"}`.
Every instance polls `SELECT Sid, ExpiresAt FROM catalog.RevokedSessions WHERE RevokedAt > @since`
every 30 seconds (`RevokedSessionMonitor : BackgroundService`), so a logout delivered to one
instance is honoured everywhere within 30 seconds; expired rows are pruned by the same monitor
once a day. No `ITicketStore`, no per-request database read.

**Tenant-level deactivation is not a cookie event.** Deactivating a user in tenant 1 is an
authorization outcome of `IConnectStep` (404 on tenant 1 from the next request); the user may remain
active in tenant 2 of the same distribution and their cookie stays valid. The user service (T4/T8)
raises `ISessionEndListener` for the tenant's hub connections only.

**Rationale.** Immediate revocation would need an `ITicketStore` (one store read per request, a
table, and cleanup); the throttled refresh gives a ten-minute bound with zero per-request I/O, and
the polled revoked set gives a thirty-second bound for explicit logout. Spec 0003 already defers the
`ITicketStore` on the identity side for the same reasons.

**Confidence.** High. **Review flag:** `ITicketStore`-backed sessions (immediate, one cached store
read per request) are the alternative if a ten-minute revocation bound is judged too long for
"sign out everywhere".

### D20 — CSRF posture: POST + JSON + required custom header + origin check; no antiforgery tokens

**Decision.** `CsrfMiddleware` applies to every request whose principal was authenticated by the
cookie scheme and whose method is not `GET`, `HEAD` or `OPTIONS`:

1. `Origin` (or, when absent, `Referer`) must match `Tellma:PublicOrigin`; `Sec-Fetch-Site`, when
   present, must be `same-origin` or `none`.
2. The request must carry `X-Requested-With: XMLHttpRequest` (constant; the SPA's HTTP interceptor
   sets it on every call). A custom header forces a CORS preflight for any cross-origin caller, and
   the distribution registers no CORS policy, so the preflight fails.
3. Request bodies must be `application/json` unless the endpoint carries `AcceptsMultipartMetadata`
   (T7's upload endpoint), which still requires the header.

Failure → 403 `csrf-rejected`. Bearer-authenticated requests are exempt (no cookie, no CSRF).
`GET` endpoints reachable with the cookie (blob downloads, `/bff/user`) must be side-effect free; the
startup gate refuses a cookie-scheme `GET` endpoint stamped `IsMutation`. `SameSite=Lax` stays
(spec 0003 §7.1; `Strict` breaks the post-login redirect chain and deep links). RFC 10017's
`SameSite=Strict` is a SHOULD; the required custom header is its MUST-equivalent control, and this
is recorded as the deliberate departure from the SHOULD.

**Rationale.** The .NET 10 antiforgery middleware enforces nothing for JSON endpoints by design; a
per-session token adds a fetch and a cookie for no additional protection over a required header
plus an origin check on an all-POST JSON surface.

**Confidence.** High.

### D21 — Bearer surfaces, principal kinds, and the step-up seam

**Decision.** JWT bearer scheme `TellmaAuthentication.BearerScheme = "tellma.bearer"`: authority and
issuer from `Tellma:Identity:Authority`, signature from the cached JWKS (no per-request identity call),
`AudienceValidator` accepting `Tellma:PublicOrigin` (the `/api/v1` audience) or
`{PublicOrigin}/{tenantId}/mcp` for the tenant in the route (the MCP audience, D23). Policies:
`TellmaPolicies.WebSession` (cookie scheme, authenticated), `TellmaPolicies.Bearer` (bearer scheme,
authenticated, scope contains `tellma_api`), `TellmaPolicies.ControlPlane` (bearer, scope contains
`tellma_control_plane`). The host-wide fallback policy is "authenticated user", so an endpoint that
forgot its policy is at least authenticated; `AllowAnonymous` is used only on the closed list of
D5/D18 and the startup gate refuses it anywhere under `/{tenantId:int}/`.

`PrincipalKind` is derived at binding: cookie → `User`; bearer with `sid` or `auth_time` → `User`
(a person through the CLI, Claude Code, Codex); bearer without them → `ServiceAccount`
(`client_credentials`; `Subject` = the client id). The identity server should mint an explicit
`tellma_kind` claim to make this derivation unnecessary — recorded as an identity-server addition.
T4 maps a service account's client id to a tenant user through the same `IConnectStep` (the brain
dump's "service accounts get a separate table later" is T4's to place).

Step-up: `RequireAssuranceMetadata(string Acr, TimeSpan? MaxAge)` on an endpoint (T6 exposes it as an
attribute on projected operations); `ITenantAccessGuard` compares it against `IUserContext.Acr` and
the assurance-freshness claim and throws `InsufficientAssuranceException(acr, maxAge)`, which T6 maps
to `401` with `WWW-Authenticate: Bearer error="insufficient_user_authentication", acr_values="…", max_age=…`
and a JSON body carrying the same values; the SPA navigates to `/bff/login?acr_values=…&max_age=…&returnUrl=…`.
No operation in this release is marked sensitive; the seam exists so T8's user administration can
mark one.

**Confidence.** High.

### D22 — In-proc identity mode

**Decision.** `Tellma:Identity:Mode = InProc` makes `AddTellma` call
`AddTellmaIdentity(section "Tellma:Identity:InProc")` with `Mode = InProc`, `PathBase = /id`,
`Issuer = {PublicOrigin}/id`, and `ConnectionString` = the catalog connection string (schema
`idsvr`, migrated by the identity engine's own migrations when `Seed:ApplyMigrations` is true — the
one schema the web process may migrate, because the identity engine already owns that path). The
OIDC handler's authority is then the distribution's own origin. `UseTellma` places
`UseTellmaIdentity()` after routing; `MapTellma` maps its endpoints.

Development defaults (`appsettings.Development.json` scaffolded in the reference distribution):
in-proc mode, `DevAdmin.Enabled = true` (subject `00000000-0000-0000-0000-000000000001`,
`admin@localhost`), development self-signed keys, the email log sink. The migrator's `init` seeds
tenants from `Tellma:Seed:Tenants` and, in each, T8's bootstrap creates the tenant admin on that
subject — so the first sign-in is the email-code path with the code in the console, as spec 0003 §10.4
describes.

**Required identity-engine addition.** The in-proc distribution must own its BFF client (`<slug>`)
and service client (`<slug>-svc`) with *stable* secrets from configuration. Today
`IClientProvisioningService.CreateDistributionAsync` upserts the descriptors but generates fresh
secrets on every call, and `IdentitySeeder` knows only `Cli`, `Native` and `ControlPlane`. The
engine gains `TellmaIdentitySeedClientKind.Distribution` with `Origin`, `BackchannelLogoutUri`,
`ClientSecret` and `ServiceClientSecret`, seeded through `ClientDescriptorFactory.Distribution` /
`DistributionService` with the supplied secrets and the `rsrc:<origin>` grant. `AddTellma` writes
that seed entry from `Tellma:Identity:ClientSecret` / `ServiceClientSecret` and `PublicOrigin` so the
distribution's configuration names the secret once.

**Confidence.** High on the shape; the engine change is small and isolated.

### D23 — MCP topology: one endpoint per tenant, per-tenant audience, and the identity-server additions it needs

**Decision.** `/{tenantId:int}/mcp`, bearer-authenticated, stateless (`HttpServerSessionMode.Stateless`,
the SDK default), protected-resource metadata served by the SDK at
`/.well-known/oauth-protected-resource/{tenantId}/mcp` with `resource = {PublicOrigin}/{tenantId}/mcp`
and `authorization_servers = [Tellma:Identity:Authority]`. The tenant access guard runs in the MCP
request filters (D10). An agent needing two tenants configures two servers; an agent needing live
and sandbox configures both URLs. Tool shape, tool listing by permission, MRTR confirmations and
the server's name (`tellma-<slug>`) are T6's; T1 reserves the route, the scheme, the audience rule
and the metadata document. The developer-tooling server stays `dotnet tellma mcp`.

Identity-server additions this requires (recorded as prerequisites for the MCP surface, not for
this spec's own definition of done):
1. **Origin-prefix resource grants.** A client holding `rsrc:https://acme.app.tellma.com` may
   request `resource=https://acme.app.tellma.com/1/mcp`; the authorization controller copies the
   requested resource into `SetResources` so `aud` is the exact MCP server URI. Today OpenIddict's
   permission check is an exact-string match against the granted resource, so this is a normalisation
   step in the controller (validate that the resource's origin is granted, then set the full URI),
   not a change to OpenIddict. This gives per-tenant audiences without OpenIddict 8's dynamic
   resources and keeps a token for tenant 1 structurally invalid at tenant 2.
2. **Client ID Metadata Documents** (`client_id_metadata_document_supported: true`, `none` in
   `token_endpoint_auth_methods_supported`): an application-store decorator that materialises
   URL-shaped client ids by fetching, validating and caching the document. This is what removes the
   manual client-registration step for Claude Code, hosted Claude, ChatGPT and Codex at once. Until
   it ships, one pre-registered public native client per vendor (loopback redirect URIs, port-less)
   is seeded through the existing `Cli`/`Native` kinds.
3. `code_challenge_methods_supported: ["S256"]` is already advertised; refresh-token rotation for
   public clients is already the default.
4. A `tellma_kind` claim (`user` | `service_account`) on access tokens (D21).

Autonomous agents use a service account (`client_credentials`, `resource=<tenant MCP URI>`, granted
`rsrc:<origin>` by `CreateServiceAccountAsync`), never a user flow.

**Rationale.** Matches the orchestrator's hint and the MCP authorization model (RFC 8707 resource =
canonical MCP server URI; audience-bound tokens); one code path through the same access guard.

**Confidence.** High on topology; medium on the origin-prefix rule (it is an identity-server design
decision Ahmad should see). **Review flag:** a single distribution-wide audience
(`aud = PublicOrigin`) with tenant membership enforced from the token's `sub` is the alternative that
needs no identity-server change but lets a token minted for one tenant reach another tenant's MCP
endpoint of the same distribution (still membership-checked, but not audience-isolated).

### D24 — Provisioning: a migrator with commands and a step seam; the self-serve trigger is a seam

**Decision.** `Tellma.Core.Migrator` (new class library) provides `TellmaMigrator.RunAsync(args, slug, compose)`
with commands:

| Command | What it does |
|---|---|
| `init` | Creates the catalog database if missing (from `Tellma:Catalog:ConnectionString`'s server, name from the string), applies the catalog migrations, seeds `Tellma:Seed:Tenants` (each through `add-tenant`), and — in-proc mode — leaves the `idsvr` schema to the identity engine. Idempotent. |
| `add-tenant --name --category Live\|Sandbox [--sandbox-of id] [--id n] [--server name] [--database name] [--admin-email e]` | Requires `AllowRegistration` (or `init` seeding). Inserts the row in `Provisioning`, creates the database (`CREATE DATABASE` on the resolved server; on Azure with `Tellma:Sql:Azure:Edition`/`ServiceObjective`/`ElasticPool` options), applies the tenant migration chain under `sp_getapplock`, runs the built-in and registered `ITenantProvisioningStep`s (managed-identity user, T8's bootstrap admin and versioned seeds, membership reconciliation), then flips the row to `Active`. |
| `migrate [--all-tenants \| --tenant id] [--catalog] [--parallelism n]` | Migrates the catalog first, then converges tenant databases with bounded parallelism (default 4), continues past per-tenant failures, prints a per-tenant outcome report, exits non-zero if any tenant failed. Each tenant: `sp_getapplock`, `Migrate()`, steps, membership reconciliation. |
| `set-state --tenant id --state Active\|ReadOnly\|Suspended\|Retired` | `ITenantCatalog.SetStateAsync`. |
| `list-tenants` | Prints the catalog. |

`ITenantProvisioningStep` (in `Tellma.Core.Migrator`): `Name`, `Order`, `RunAsync(TenantProvisioningContext, ct)`;
idempotent by contract, recorded in the tenant database's `dbo.__TellmaProvisioning (Step, CompletedAt)`
so a re-run skips completed steps unless `--force-steps`. Packs and distributions register steps
through `FeatureContext.Services` (`AddTenantProvisioningStep<T>()`); the migrator runs them in
`Order`. Tenant-specific external integrations (the brain dump's concern) are steps.

The self-serve trigger (the web app starting a Container Apps Job, ARCHITECTURE.md) is the seam
`ITenantProvisioningTrigger.RequestAsync(TenantProvisioningRequest) -> ProvisioningTicket` with no
implementation shipped; the control-plane `POST /api/admin/tenants` (D25) returns 501 until one is
registered. Cloning a live tenant into a sandbox is a future step (Azure `CREATE DATABASE … AS COPY OF`
/ on-prem backup-restore) and is out of scope beyond the seam.

**Confidence.** High.

### D25 — The distribution contract surface

**Decision.**
- `GET /api/distribution-info` (anonymous, cacheable for 60 s):
  ```json
  { "slug": "acme", "displayName": "Acme", "platformVersion": "0.10.0", "distributionVersion": "2026.9.1+abc123",
    "identity": { "authority": "https://identity.tellma.com", "mode": "Standalone" },
    "surfaces": { "web": "/{tenantId}/api/web", "mcp": "/{tenantId}/mcp", "publicApi": null },
    "tenantRegistration": false }
  ```
  No environment name, no tenant list, no configuration echo.
- `GET /health` (anonymous, liveness: process up, composition validated) and `GET /health/ready`
  (anonymous, adds catalog reachability). App Service health checks point at `/health`.
- Control-plane admin surface, bearer with `tellma_control_plane`, non-tenant:
  `GET /api/admin/info` (the info document plus tenant counts by state), `GET /api/admin/tenants`,
  `POST /api/admin/tenants/{id}/suspend`, `POST /api/admin/tenants/{id}/unsuspend`,
  `POST /api/admin/tenants/{id}/read-only`, `POST /api/admin/tenants` (501 until D24's trigger is
  registered). `usage` is deferred with metering. Self-registration with the control plane on first
  boot is deferred until a control plane exists; this document is what it will post.

**Confidence.** High. **Review flag:** the admin surface could be deferred wholesale; it is cheap
because bearer authentication exists for MCP anyway.

### D26 — Telemetry names this theme owns

**Decision.** Meter `Tellma.Core` (the package name), names as `const`s in
`Tellma.Core.Abstractions.Tenancy.TenancyTelemetryNames` and `...Hosting.SessionTelemetryNames`:

| Instrument | Kind | Tags |
|---|---|---|
| `tellma.tenancy.resolutions` | Counter | `outcome` ∈ `found` \| `unknown` \| `suspended` \| `read_only` \| `not_member` |
| `tellma.tenancy.catalog.refresh.duration` | Histogram (s) | — |
| `tellma.auth.session.refreshes` | Counter | `outcome` ∈ `renewed` \| `rejected` \| `failed` \| `skipped` |
| `tellma.auth.backchannel_logouts` | Counter | `outcome` ∈ `accepted` \| `rejected` |
| `tellma.auth.csrf.rejections` | Counter | `reason` ∈ `origin` \| `header` \| `content_type` |
| `tellma.composition.startup.duration` | Histogram (s) | — |

No tenant tag on any instrument. `tellma.tenant.id` is a tag on the request span and a scope
property on every log line inside a tenant scope; the control plane's per-tenant usage telemetry
(ARCHITECTURE.md's telemetry path) is a later, dedicated export, not these instruments.

**Confidence.** High.

### D27 — Table naming and vocabulary this theme fixes

**Decision.** Plural table names (`catalog.Tenants`, `catalog.Memberships`, `catalog.RevokedSessions`),
matching ARCHITECTURE.md (`gl.Invoices`) and spec 0001's UDTT logical names (`InvoicesList`);
the brain dump's singular `core.User` changes. Schema `catalog` for the catalog database; `core`
and `gl` inside tenant databases (T2/T8). Enum columns are strings (`Category nvarchar(16)`,
`State nvarchar(16)`) with CHECK constraints, matching the enum-as-string convention T2 owns. Request
header names `Tellma-Calendar` and `Tellma-Time-Zone` (RFC 6648 deprecates `X-` prefixes; GitHub's
`Time-Zone` is the precedent) — T6 ratifies. "Catalog" (not "registry") for the tenant table and the
database; "tenant access guard" for the membership/state check; "connect step" for T4's
subject-to-user resolution.

**Confidence.** High on plural; T2 owns the final call.

### D28 — Local development story

**Decision.** From a fresh clone: `dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator -- init`
(creates `Tellma.dev.acme.catalog`, tenants 1 (Live, "Acme") and 2 (Sandbox, "Acme Sandbox"), each
with the dev admin on subject `…0001`), then `dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Web`,
browse `https://localhost:7052/`, sign in as `admin@localhost` with the code from the console. The
web host detects a missing catalog at startup and prints that exact `init` command instead of a
stack trace; it never migrates. Database names in Development default to `Tellma.dev.<slug>.catalog`
and `Tellma.dev.<slug>.t<id>` (`Tellma:Sql:DatabasePrefix`); the per-worktree id of ARCHITECTURE.md's
`Tellma.dev.<worktree-id>.*` scheme waits for `dotnet tellma setup-worktree`. `launchSettings.json`
is tracked in Phase 1 (fixed ports 7052/5052); the template-plus-generated scheme arrives with the
CLI.

**Confidence.** High. **Review flag:** a Development-only auto-`init` from the web host would make
F5 a one-step story at the cost of the "migrator never runs in the web process" rule.

---

## 3. Contracts

Code blocks are normative for shape; XML docs are abbreviated to summaries.

### 3.1 `Tellma.Core.Abstractions` (BCL only)

```csharp
namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>Whether a tenant's actions may reach the outside world.</summary>
    public enum TenantCategory
    {
        /// <summary>A real business unit; side effects are real.</summary>
        Live,
        /// <summary>A rehearsal copy; no external side effect may happen.</summary>
        Sandbox,
    }

    /// <summary>The lifecycle state of a tenant as recorded in the catalog.</summary>
    public enum TenantState
    {
        /// <summary>The database is being created and seeded; the tenant answers 404.</summary>
        Provisioning,
        /// <summary>Normal service.</summary>
        Active,
        /// <summary>Reads are served; mutations are refused; background work is paused.</summary>
        ReadOnly,
        /// <summary>Every tenant request is refused; data is retained.</summary>
        Suspended,
        /// <summary>Decommissioned; the tenant answers 404 and its database may be gone.</summary>
        Retired,
    }

    /// <summary>One catalog row: where a tenant is and what state it is in. Carries no secret.</summary>
    /// <param name="Id">The tenant id; the leading route segment of every tenant-scoped URL.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="Category">Live or Sandbox.</param>
    /// <param name="State">Lifecycle state.</param>
    /// <param name="SandboxOfId">For a sandbox, the live tenant it rehearses for; null otherwise.</param>
    /// <param name="Server">The named SQL server template to use; null selects the default template.</param>
    /// <param name="Database">The tenant database name on that server.</param>
    /// <param name="CreatedAt">When the row was created.</param>
    public sealed record TenantDescriptor(
        int Id,
        string Name,
        TenantCategory Category,
        TenantState State,
        int? SandboxOfId,
        string? Server,
        string Database,
        DateTimeOffset CreatedAt);

    /// <summary>The input to registering a tenant; the catalog allocates the id unless one is supplied.</summary>
    public sealed record TenantRegistration(
        string Name,
        TenantCategory Category,
        int? SandboxOfId = null,
        string? Server = null,
        string? Database = null,
        int? Id = null);

    /// <summary>The tenant catalog: the authoritative list of tenants, their location and their state.</summary>
    /// <remarks>Implementations cache reads process-wide and refresh on a bounded interval; a state
    ///     change is visible immediately on the instance that made it and within one refresh interval
    ///     elsewhere. Nothing here ever returns a connection string or a secret.</remarks>
    public interface ITenantCatalog
    {
        /// <summary>Finds a tenant by id, or null when no such tenant exists (negative answers are cached briefly).</summary>
        ValueTask<TenantDescriptor?> FindAsync(int tenantId, CancellationToken cancellationToken);

        /// <summary>Lists every tenant, in id order.</summary>
        Task<IReadOnlyList<TenantDescriptor>> ListAsync(CancellationToken cancellationToken);

        /// <summary>Whether this distribution accepts new tenants at run time.</summary>
        bool SupportsRegistration { get; }

        /// <summary>Registers a tenant in the Provisioning state. Throws when registration is not supported.</summary>
        Task<TenantDescriptor> RegisterAsync(TenantRegistration registration, CancellationToken cancellationToken);

        /// <summary>Changes a tenant's state, invalidates the local cache, and notifies state listeners.</summary>
        Task SetStateAsync(int tenantId, TenantState state, CancellationToken cancellationToken);

        /// <summary>Drops the cached row so the next read hits the store.</summary>
        void Invalidate(int tenantId);
    }

    /// <summary>Notified in-process after a tenant's state changes (hub closers, job pausers).</summary>
    public interface ITenantStateListener
    {
        /// <summary>Called after the catalog row was written.</summary>
        Task OnStateChangedAsync(TenantDescriptor tenant, TenantState previous, CancellationToken cancellationToken);
    }

    /// <summary>The tenant the current scope executes for. Bound once per request by middleware and
    ///     once per job by <see cref="ITenantScopeFactory"/>; reading before binding throws.</summary>
    public interface ITenantContext
    {
        /// <summary>Whether a tenant has been bound to this scope.</summary>
        bool IsBound { get; }
        /// <summary>The tenant id.</summary>
        int TenantId { get; }
        /// <summary>The tenant's display name.</summary>
        string TenantName { get; }
        /// <summary>Live or Sandbox.</summary>
        TenantCategory Category { get; }
        /// <summary>The tenant's state at binding time.</summary>
        TenantState State { get; }
    }

    /// <summary>Who the current scope acts as.</summary>
    public enum PrincipalKind
    {
        /// <summary>A person, through the session cookie or a bearer token minted for a user.</summary>
        User,
        /// <summary>A machine client authenticated with client credentials.</summary>
        ServiceAccount,
        /// <summary>The platform itself, in background work with no requesting user.</summary>
        System,
    }

    /// <summary>The principal the current scope acts as.</summary>
    public interface IUserContext
    {
        /// <summary>Whether a principal has been bound to this scope.</summary>
        bool IsBound { get; }
        /// <summary>User, ServiceAccount or System.</summary>
        PrincipalKind Kind { get; }
        /// <summary>The identity subject: the <c>sub</c> claim, a service account's client id, or "system".</summary>
        string Subject { get; }
        /// <summary>The tenant user id once the connect step has resolved it; null before.</summary>
        int? UserId { get; }
        /// <summary>The assurance tier of the session (users only).</summary>
        string? Acr { get; }
        /// <summary>When the evidence behind <see cref="Acr"/> was demonstrated (users only).</summary>
        DateTimeOffset? AssuranceTime { get; }
        /// <summary>The authority session id (users only); what back-channel logout revokes.</summary>
        string? Sid { get; }
    }

    /// <summary>Language, calendar and time zone for the current scope.</summary>
    public interface ILocaleContext
    {
        /// <summary>The culture used for messages and formatting.</summary>
        CultureInfo Culture { get; }
        /// <summary>The calendar code ("gc", "uq", "et").</summary>
        string CalendarCode { get; }
        /// <summary>The time zone (IANA id).</summary>
        TimeZoneInfo TimeZone { get; }
        /// <summary>The current date in <see cref="TimeZone"/>; what the query engine's today() binds to.</summary>
        DateOnly Today { get; }
    }

    /// <summary>A serialisable copy of the scope's context, stored with enqueued work and rebound by the runner.</summary>
    public sealed record RequestContextSnapshot(
        int TenantId,
        PrincipalKind Kind,
        string Subject,
        int? UserId,
        string CultureName,
        string CalendarCode,
        string TimeZoneId,
        string? TraceParent = null);

    /// <summary>A bound scope for out-of-request work.</summary>
    public sealed class TenantScope : IAsyncDisposable
    {
        /// <summary>The scope's service provider, with tenant, user and locale contexts bound.</summary>
        public IServiceProvider Services { get; }
        /// <summary>Disposes the scope and ends its activity.</summary>
        public ValueTask DisposeAsync();
    }

    /// <summary>Creates DI scopes bound to a tenant for background work and seeding.</summary>
    public interface ITenantScopeFactory
    {
        /// <summary>Creates a scope from a snapshot; refuses non-Active tenants unless <paramref name="allowInactive"/>.</summary>
        Task<TenantScope> CreateScopeAsync(RequestContextSnapshot snapshot, bool allowInactive, CancellationToken cancellationToken);

        /// <summary>Captures the current scope's context for hand-off.</summary>
        RequestContextSnapshot Capture();
    }

    /// <summary>A subject's probable membership in a tenant; a hint, not an authorization.</summary>
    public sealed record TenantMembership(TenantDescriptor Tenant, bool IsActive);

    /// <summary>One membership fact to record.</summary>
    public sealed record MembershipRecord(int TenantId, string Subject, bool IsActive);

    /// <summary>"Which tenants am I a member of", answered from the catalog's hint table.</summary>
    public interface IMembershipDirectory
    {
        /// <summary>The tenants a subject probably belongs to, with their descriptors.</summary>
        Task<IReadOnlyList<TenantMembership>> ListForSubjectAsync(string subject, CancellationToken cancellationToken);

        /// <summary>Upserts hints; best effort — callers never fail a business operation on this.</summary>
        Task RecordAsync(IReadOnlyList<MembershipRecord> records, CancellationToken cancellationToken);
    }

    /// <summary>Notified when a session ends for any reason (logout, revocation, rejection).</summary>
    public interface ISessionEndListener
    {
        /// <summary>Called with the subject and, when known, the session id.</summary>
        Task OnSessionEndedAsync(string subject, string? sid, CancellationToken cancellationToken);
    }

    /// <summary>Telemetry names for the tenancy instruments of the Tellma.Core meter.</summary>
    public static class TenancyTelemetryNames
    {
        public const string MeterName = "Tellma.Core";
        public const string Resolutions = "tellma.tenancy.resolutions";
        public const string CatalogRefreshDuration = "tellma.tenancy.catalog.refresh.duration";
        public const string OutcomeTag = "outcome";
        // ... closed tag-value constants
    }

    /// <summary>No tenant with the given id exists (or the caller may not know it does).</summary>
    public sealed class TenantNotFoundException(int tenantId) : Exception($"Tenant {tenantId} was not found.")
    {
        public int TenantId { get; } = tenantId;
    }

    /// <summary>The tenant exists but its state refuses this request.</summary>
    public sealed class TenantUnavailableException(int tenantId, TenantState state) : Exception($"Tenant {tenantId} is {state}.")
    {
        public int TenantId { get; } = tenantId;
        public TenantState State { get; } = state;
    }

    /// <summary>The session's assurance is below what the operation requires.</summary>
    public sealed class InsufficientAssuranceException(string requiredAcr, TimeSpan? maxAge) : Exception("A stronger authentication is required.")
    {
        public string RequiredAcr { get; } = requiredAcr;
        public TimeSpan? MaxAge { get; } = maxAge;
    }
}
```

### 3.2 `Tellma.Core.Composition` (DI, EF Relational, ASP.NET Core)

```csharp
namespace Tellma.Core.Composition
{
    /// <summary>A composable unit: declares its dependencies by attribute and contributes to DI, the EF model and the endpoint table.</summary>
    public interface ITellmaFeature
    {
        /// <summary>A stable name for diagnostics; the type name by default.</summary>
        string Name { get; }

        /// <summary>Registers the feature's services, model configuration, endpoints, stacks and startup checks.</summary>
        void Contribute(FeatureContext context);
    }

    /// <summary>The convenient base: <see cref="Name"/> defaults to the type name.</summary>
    public abstract class TellmaFeature : ITellmaFeature
    {
        /// <inheritdoc />
        public virtual string Name => GetType().Name;

        /// <inheritdoc />
        public abstract void Contribute(FeatureContext context);
    }

    /// <summary>Declares that the decorated feature is broken without <typeparamref name="TFeature"/>. Unmet: a composition error.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class RequiresAttribute<TFeature> : Attribute where TFeature : class, ITellmaFeature
    {
    }

    /// <summary>A CRUD stack declared by a feature and realised by the platform's stack machinery.</summary>
    /// <param name="EntityType">The concrete leaf entity type this distribution deploys.</param>
    /// <param name="ServiceType">The service type for the stack (T5 defines its base).</param>
    /// <param name="Configure">Optional stack options (T5 defines the options type).</param>
    public sealed record StackDeclaration(Type EntityType, Type ServiceType, Action<object>? Configure = null);

    /// <summary>One thing wrong with the composition, with the fix when one is known.</summary>
    public sealed record CompositionProblem(string Feature, string Message, string? Fix = null);

    /// <summary>Every problem found in one pass; thrown before the host is built or before it serves traffic.</summary>
    public sealed class TellmaCompositionException(IReadOnlyList<CompositionProblem> problems)
        : Exception(Describe(problems))
    {
        /// <summary>The problems, in the order found.</summary>
        public IReadOnlyList<CompositionProblem> Problems { get; } = problems;

        private static string Describe(IReadOnlyList<CompositionProblem> problems)
        {
            return "The Tellma composition is invalid: " + string.Join(" ", problems.Select(static p => $"[{p.Feature}] {p.Message}" + (p.Fix is null ? string.Empty : $" Fix: {p.Fix}")));
        }
    }

    /// <summary>What a feature contributes to. Recording, not executing: model actions run in the DbContext,
    ///     endpoint actions run in MapTellma, checks run in the startup gate.</summary>
    public sealed class FeatureContext
    {
        /// <summary>The host's service collection.</summary>
        public IServiceCollection Services { get; }
        /// <summary>The host's configuration.</summary>
        public IConfiguration Configuration { get; }
        /// <summary>The host environment.</summary>
        public IHostEnvironment Environment { get; }

        /// <summary>Contributes model configuration to the tenant DbContext, run in feature order.</summary>
        public void ConfigureModel(Action<ModelBuilder> configure);

        /// <summary>Contributes endpoints to the tenant web group (/{tenantId}/api/web): authorization, the tenant access filter and surface metadata are inherited.</summary>
        public void MapTenantEndpoints(Action<IEndpointRouteBuilder> map);

        /// <summary>Contributes non-tenant endpoints at the host root (rare: webhooks, admin surfaces).</summary>
        public void MapHostEndpoints(Action<IEndpointRouteBuilder> map);

        /// <summary>Declares a CRUD stack for the platform to realise.</summary>
        public void AddStack(StackDeclaration stack);

        /// <summary>Adds a check run once at startup inside a scope; every problem it returns is aggregated.</summary>
        public void AddStartupCheck(Func<IServiceProvider, IEnumerable<CompositionProblem>> check);

        /// <summary>Contributes a sub-feature; its edges are validated with everything else.</summary>
        public void AddFeature(ITellmaFeature feature);
    }

    /// <summary>What a distribution's Compose method receives.</summary>
    public sealed class TellmaBuilder
    {
        /// <summary>The host's service collection, for compositions that register non-feature services (email transports).</summary>
        public IServiceCollection Services { get; }
        /// <summary>The host's configuration.</summary>
        public IConfiguration Configuration { get; }
        /// <summary>The host environment.</summary>
        public IHostEnvironment Environment { get; }

        /// <summary>Selects a feature, constructing it with its parameterless constructor; a second call with no options is a no-op.</summary>
        public TellmaBuilder AddFeature<TFeature>(Action<TFeature>? configure = null) where TFeature : class, ITellmaFeature, new();

        /// <summary>Selects a pre-constructed feature.</summary>
        public TellmaBuilder AddFeature(ITellmaFeature feature);
    }

    /// <summary>Metadata stamped on every endpoint of a tenant surface by the platform's route groups.</summary>
    public sealed record TenantSurfaceMetadata(TellmaSurface Surface);

    /// <summary>The tenant-scoped surfaces.</summary>
    public enum TellmaSurface
    {
        Web,
        PublicApi,
        Mcp,
        Hub,
        Blobs,
    }

    /// <summary>Stamped by the endpoint projection on every operation; an endpoint without it is treated as a mutation.</summary>
    public sealed record TellmaOperationMetadata(string Resource, string Action, bool IsMutation);

    /// <summary>Requires a session at the given assurance tier, optionally fresher than <paramref name="MaxAge"/>.</summary>
    public sealed record RequireAssuranceMetadata(string Acr, TimeSpan? MaxAge);
}
```

### 3.3 `Tellma.Core` (runtime; only the public surface a distribution or another theme touches)

```csharp
namespace Tellma.Core.Hosting
{
    /// <summary>The three calls a distribution makes.</summary>
    public static class TellmaHostExtensions
    {
        /// <summary>Composes the distribution: options, telemetry, features, tenancy, authentication, the tenant DbContext, the startup gate.</summary>
        /// <param name="slug">The distribution's hardcoded slug; also its OIDC client id and its DeploymentIdentity application.</param>
        public static WebApplicationBuilder AddTellma(this WebApplicationBuilder builder, string slug, Action<TellmaBuilder> compose);

        /// <summary>Installs the request pipeline in the platform's order.</summary>
        public static WebApplication UseTellma(this WebApplication app);

        /// <summary>Maps every platform and contributed endpoint.</summary>
        public static WebApplication MapTellma(this WebApplication app);
    }

    /// <summary>Host-free composition validation for tests: the graph, options and the host-free startup checks.</summary>
    public static class TellmaComposition
    {
        /// <summary>Runs collect, declare and validate; throws <see cref="TellmaCompositionException"/> on any problem.</summary>
        public static void Validate(string slug, Action<TellmaBuilder> compose, IConfiguration configuration);
    }

    /// <summary>Authentication scheme and policy names.</summary>
    public static class TellmaAuthentication
    {
        public const string SessionScheme = "tellma.session";
        public const string OidcScheme = "tellma.oidc";
        public const string BearerScheme = "tellma.bearer";
        public const string SessionCookieName = "__Host-tellma.session";
        public const string ProfileCookieName = "tellma.profile";
        public const string CsrfHeaderName = "X-Requested-With";
        public const string CsrfHeaderValue = "XMLHttpRequest";
    }

    /// <summary>Authorization policy names.</summary>
    public static class TellmaPolicies
    {
        public const string WebSession = "tellma.web-session";
        public const string Bearer = "tellma.bearer";
        public const string ControlPlane = "tellma.control-plane";
    }

    /// <summary>Supplies the tenant's authentication policy at login (assurance and allowed methods).</summary>
    public interface IAuthenticationPolicyProvider
    {
        /// <summary>The acr_values, max_age and tellma_allowed_methods to push; null members mean "no constraint".</summary>
        ValueTask<LoginPolicy> GetLoginPolicyAsync(int? tenantId, CancellationToken cancellationToken);
    }

    /// <summary>The constraints pushed with an authorization request.</summary>
    public sealed record LoginPolicy(string? AcrValues, TimeSpan? MaxAge, IReadOnlyList<string>? AllowedMethods);
}

namespace Tellma.Core.Tenancy
{
    /// <summary>The single place that decides whether the bound principal may proceed against the bound tenant.</summary>
    public interface ITenantAccessGuard
    {
        /// <summary>Connects the principal (via the connect step), checks tenant state and assurance; throws the platform exceptions on refusal.</summary>
        Task EnsureAccessAsync(TenantAccessRequirement requirement, CancellationToken cancellationToken);
    }

    /// <summary>What an endpoint needs: whether it mutates, and any assurance requirement.</summary>
    public sealed record TenantAccessRequirement(bool IsMutation, RequireAssuranceMetadata? Assurance);

    /// <summary>The composed, secret-free-by-policy connection for the bound tenant; what the batch executor opens.</summary>
    public interface ITenantConnection
    {
        /// <summary>The composed connection string (template + database); never logged.</summary>
        string ConnectionString { get; }

        /// <summary>A new, unopened connection to the tenant database.</summary>
        SqlConnection CreateConnection();
    }
}

namespace Tellma.Core.Migrator
{
    /// <summary>The migrator entry point every distribution's migrator Program.cs calls.</summary>
    public static class TellmaMigrator
    {
        /// <summary>Builds the generic host with the same composition as the web host, then runs the command in <paramref name="args"/>.</summary>
        public static Task<int> RunAsync(string[] args, string slug, Action<TellmaBuilder> compose);
    }

    /// <summary>One idempotent step of tenant provisioning, run after migrations and recorded per tenant.</summary>
    public interface ITenantProvisioningStep
    {
        /// <summary>Stable name; the key in the tenant's provisioning history.</summary>
        string Name { get; }
        /// <summary>Execution order among steps; the platform's built-ins run first.</summary>
        int Order { get; }
        /// <summary>Runs inside a tenant scope with the System principal.</summary>
        Task RunAsync(TenantProvisioningContext context, CancellationToken cancellationToken);
    }

    /// <summary>What a step sees.</summary>
    public sealed record TenantProvisioningContext(TenantDescriptor Tenant, IServiceProvider Services, bool IsNew, string? AdminEmail);

    /// <summary>The seam for self-serve provisioning; no implementation ships in this release.</summary>
    public interface ITenantProvisioningTrigger
    {
        /// <summary>Requests an asynchronous provisioning run and returns a ticket the caller can poll.</summary>
        Task<ProvisioningTicket> RequestAsync(TenantRegistration registration, CancellationToken cancellationToken);
    }

    /// <summary>A handle to an in-flight provisioning run.</summary>
    public sealed record ProvisioningTicket(string Id, int? TenantId);
}
```

### 3.4 Configuration schema (`Tellma` section)

```jsonc
{
  "Tellma": {
    "DisplayName": "Acme",
    "PublicOrigin": "https://acme.app.tellma.com",           // required; https outside Development; no path
    "Catalog": { "ConnectionString": "Server=...;Database=tellma-acme-catalog;Authentication=Active Directory Managed Identity;Encrypt=True" },
    "Sql": {
      "ConnectionString": "Server=tcp:sql-tellma-shared.database.windows.net;Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=30",
      "Servers": { "eu": { "ConnectionString": "..." } },       // optional named templates selected by Tenants.Server
      "DatabasePrefix": "tellma-acme",                          // default database name = <prefix>-t<id>; Development: Tellma.dev.acme
      "AllowSqlPassword": false,
      "ManagedIdentityUser": "tellma-acme",                     // the contained user created in every tenant database (SaaS)
      "Azure": { "ElasticPool": "pool-standard" }               // used by add-tenant on Azure
    },
    "Tenancy": { "AllowRegistration": false, "CatalogRefreshSeconds": 60 },
    "Identity": {
      "Mode": "Standalone",                                     // or InProc
      "Authority": "https://identity.tellma.com",
      "ClientSecret": "<from Key Vault>",
      "ServiceClientSecret": "<from Key Vault>",
      "InProc": { /* TellmaIdentityOptions minus Mode/PathBase/Issuer/ConnectionString, which AddTellma sets */ }
    },
    "Session": { "IdleLifetimeDays": 7, "AbsoluteLifetimeDays": 30, "RefreshIntervalMinutes": 10, "MaxRefreshStalenessMinutes": 60 },
    "DataProtection": { "Store": "FileSystem", "Path": "..." },  // or Blob + KeyVault, the identity configurator's shape
    "ForwardedHeaders": { "Enabled": true, "KnownNetworks": ["10.0.0.0/8"] },
    "Seed": {                                                   // consumed by `init`
      "Tenants": [
        { "Id": 1, "Name": "Acme", "Category": "Live" },
        { "Id": 2, "Name": "Acme Sandbox", "Category": "Sandbox", "SandboxOf": 1 }
      ],
      "AdminEmail": "admin@localhost"                           // T8's bootstrap admin; Development pairs it with the dev-admin subject
    }
  },
  "Email": { "Provider": "smtp" },
  "Serilog": { }
}
```

### 3.5 Load-bearing statements

Catalog refresh (every 60 s, all instances):

```sql
SELECT [Id], [Name], [Category], [State], [SandboxOfId], [Server], [Database], [CreatedAt]
FROM [catalog].[Tenants];
```

Revoked-session poll (every 30 s, all instances; `@since` = the last `RevokedAt` seen):

```sql
SELECT [Sid], [Subject], [RevokedAt], [ExpiresAt]
FROM [catalog].[RevokedSessions]
WHERE [RevokedAt] > @since
ORDER BY [RevokedAt];
```

Membership reconciliation (migrator, per tenant, inside the tenant migration's app lock; run against
the catalog connection with the tenant's `(Subject, IsActive)` pairs as a TVP of `[StringList]`-like
shape — a two-column standalone table type `[catalog].[MembershipList] (Subject nvarchar(36), IsActive bit)`
declared by the catalog context):

```sql
UPDATE m SET m.[IsActive] = s.[IsActive], m.[UpdatedAt] = SYSUTCDATETIME()
FROM [catalog].[Memberships] m
JOIN @subjects s ON s.[Subject] = m.[Subject]
WHERE m.[TenantId] = @tenantId AND m.[IsActive] <> s.[IsActive];

INSERT INTO [catalog].[Memberships] ([TenantId], [Subject], [IsActive], [UpdatedAt])
SELECT @tenantId, s.[Subject], s.[IsActive], SYSUTCDATETIME()
FROM @subjects s
WHERE NOT EXISTS (SELECT 1 FROM [catalog].[Memberships] m WHERE m.[TenantId] = @tenantId AND m.[Subject] = s.[Subject]);

DELETE m FROM [catalog].[Memberships] m
WHERE m.[TenantId] = @tenantId AND NOT EXISTS (SELECT 1 FROM @subjects s WHERE s.[Subject] = m.[Subject]);
```

Tenant migration lock (migrator, per tenant database):

```sql
EXEC sp_getapplock @Resource = N'tellma:migrate', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 600000;
```

Managed-identity user (provisioning step, run by the migrator's DDL identity against a new tenant
database on Azure; skipped on-prem):

```sql
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = @user)
    EXEC(N'CREATE USER ' + QUOTENAME(@user) + N' FROM EXTERNAL PROVIDER;');
EXEC(N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@user) + N';');
EXEC(N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@user) + N';');
EXEC(N'ALTER ROLE db_ddladmin  ADD MEMBER ' + QUOTENAME(@user) + N';');
```

(`db_ddladmin` is needed for the table-type sweep and `sp_sequence_get_range` on sequences owned by
the schema; the exact grant set is T2's to finalise.)

---

## 4. Schema

All tables live in the catalog database unless stated. No IDENTITY columns; the sequence is the
allocator. Enum columns are strings with CHECK constraints. Names are plural.

```sql
CREATE SEQUENCE [catalog].[sq_Tenants] AS int START WITH 1 INCREMENT BY 1;

CREATE TABLE [catalog].[Tenants] (
    [Id]          int            NOT NULL CONSTRAINT [PK_Tenants] PRIMARY KEY,
    [Name]        nvarchar(255)  NOT NULL,
    [Category]    nvarchar(16)   NOT NULL CONSTRAINT [CK_Tenants_Category] CHECK ([Category] IN (N'Live', N'Sandbox')),
    [State]       nvarchar(16)   NOT NULL CONSTRAINT [CK_Tenants_State] CHECK ([State] IN (N'Provisioning', N'Active', N'ReadOnly', N'Suspended', N'Retired')),
    [SandboxOfId] int            NULL     CONSTRAINT [FK_Tenants_SandboxOf] REFERENCES [catalog].[Tenants]([Id]),
    [Server]      nvarchar(128)  NULL,
    [Database]    nvarchar(128)  NOT NULL,
    [CreatedAt]   datetime2(3)   NOT NULL,
    [ModifiedAt]  datetime2(3)   NOT NULL,
    CONSTRAINT [CK_Tenants_SandboxOf] CHECK (([Category] = N'Sandbox') = ([SandboxOfId] IS NOT NULL)),
    CONSTRAINT [UQ_Tenants_Location] UNIQUE ([Server], [Database])
);
-- No password, key, token or connection-string column may ever be added; a model test asserts the column set.
```

```sql
CREATE TABLE [catalog].[Memberships] (
    [Subject]   nvarchar(36)  NOT NULL,      -- the identity sub (36-char GUID string) or a service account client id
    [TenantId]  int           NOT NULL CONSTRAINT [FK_Memberships_Tenants] REFERENCES [catalog].[Tenants]([Id]) ON DELETE CASCADE,
    [IsActive]  bit           NOT NULL,
    [UpdatedAt] datetime2(3)  NOT NULL,
    CONSTRAINT [PK_Memberships] PRIMARY KEY ([Subject], [TenantId])
);
CREATE INDEX [IX_Memberships_TenantId] ON [catalog].[Memberships]([TenantId]);
```

```sql
CREATE TABLE [catalog].[RevokedSessions] (
    [Sid]       nvarchar(128) NOT NULL CONSTRAINT [PK_RevokedSessions] PRIMARY KEY,
    [Subject]   nvarchar(36)  NOT NULL,
    [RevokedAt] datetime2(3)  NOT NULL,
    [ExpiresAt] datetime2(3)  NOT NULL
);
CREATE INDEX [IX_RevokedSessions_RevokedAt] ON [catalog].[RevokedSessions]([RevokedAt]);
CREATE INDEX [IX_RevokedSessions_ExpiresAt] ON [catalog].[RevokedSessions]([ExpiresAt]);
```

```sql
CREATE TYPE [catalog].[MembershipList] AS TABLE (
    [Subject]  nvarchar(36) NOT NULL PRIMARY KEY,
    [IsActive] bit          NOT NULL
);
-- A standalone [TableType] class in Tellma.Core (catalog sweep scope "tellma-catalog"), versioned like every other type.
```

```sql
-- In the catalog database when Tellma:Identity:Mode = InProc: schema [idsvr], owned and migrated by Tellma.Identity.Migrations.
-- Migration history: [catalog].[__EFMigrationsHistory] for the catalog model; the identity engine keeps its own.
```

Tenant database (per tenant; the rest of the tenant schema is T2/T3/T4/T8's):

```sql
CREATE TABLE [dbo].[__TellmaProvisioning] (
    [Step]        nvarchar(128) NOT NULL CONSTRAINT [PK___TellmaProvisioning] PRIMARY KEY,
    [CompletedAt] datetime2(3)  NOT NULL,
    [Version]     nvarchar(64)  NOT NULL      -- the platform version that ran the step
);
```

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "Are those the proper layer names in a .NET business app?" | Keep Data / Service / Web as vocabulary; folders are `Entities/`, `Services/`, `Endpoints/`; no `Data/` folder because the distribution owns no data-access code (D2). |
| "Is the TenantRegistry the right shape?" | No. Split into `ITenantCatalog` (where and what state; no secrets) and connection composition from one credential template; registration exists but is gated by `AllowRegistration` (D9, D12). |
| "if each db gets a password, where do we store these passwords? Is storing them in a DB a good practice?" | No database gets a password: managed identity in SaaS, integrated security on Windows, a single configured login on Linux on-prem. The catalog stores server + database only (D15). |
| "one MCP server per tenant, or one server for the entire distro?" | One endpoint per tenant at `/{tenantId}/mcp` with a per-tenant audience; an agent needing two tenants configures two servers (D23). |
| "Should we extend the TenantRegistry to blob storage connection strings, azure key vault?" | No. One storage account and one Key Vault per distribution; blob paths are tenant-scoped by T7 using `ITenantContext`; per-tenant integration secrets, when they arrive, go behind an `ITenantSecretStore` seam in the tenant database (D15). |
| "Should we extend the TenantRegistry to support provisioning new tenants?" | No. Provisioning is the migrator (`add-tenant`) with an `ITenantProvisioningStep` seam for distro-specific work; the catalog only records the outcome; the self-serve trigger is a seam with no implementation (D24). |
| "Should we accept an X-Today header … or the user's timezone?" | Time zone, never a client date: `Tellma-Time-Zone` (IANA) with precedence header → user preference → tenant default → UTC; `ILocaleContext.Today` is derived and is what `today()` binds to (D13). |
| "the server schema is discoverable at runtime … can be changed without breaking existing agents (is this true?)" | Partly: `tools/list` is re-read (with `ttlMs`), so *adding* tools or parameters is safe; renaming or re-semanticising a tool breaks any prompt, skill or saved workflow that names it. Treat tool names as a public API (T6). |
| "Every request comes with a tenantId as part of the url api/{tenantId}/documents" vs "{tenantId}/api/web/…" | The tenant leads: `/{tenantId:int}/api/web/…`, `/{tenantId:int}/mcp` (D8). |
| "Tenant routing info is heavily read and rarely changed, so it benefits from aggressive caching." | Process-wide cache, full refresh every 60 s, negative cache 10 s, explicit invalidation on state change (D9, D17). |
| "The seam needs to be there from the beginning even if we didn't ship the multi-live-tenant distros later." | `IMembershipDirectory` over `catalog.Memberships`, one implementation for both shapes; no fan-out (D14). |
| "Keys and secrets are never stored in the DB in clear text." | Stronger: never stored in the catalog at all (D15). |

---

## 6. Seams

**6 — Feature composition (owned).** The contract is `ITellmaFeature` / `TellmaFeature`,
`[Requires<T>]`, `FeatureContext`, `TellmaBuilder`, `StackDeclaration`, `CompositionProblem`,
`TellmaCompositionException` in `Tellma.Core.Composition` (§3.2). T5's stack feature is realised
from `StackDeclaration`s the platform collects; T8's `GlFeature : TellmaFeature` declares
`context.AddStack(new StackDeclaration(typeof(Center), typeof(CenterService)))` and is selected by
`tellma.AddGl()`. Both use one shape. **What I need from T5:** the service base type that
`ServiceType` must derive from, in a package a module may reference (`Tellma.Core.Composition` is
available for it), and the options type `Configure` targets. **Conflict for T8:** `taxonomy.json` is
created here with an empty `modules` list; T8 adds `Gl`.

**9 — Request context (owned).** `ITenantContext`, `IUserContext`, `ILocaleContext`,
`RequestContextSnapshot`, `ITenantScopeFactory` (§3.1). Requests: bound by `TenantMiddleware`
(tenant), the authentication layer (principal) and T3's `ILocaleNegotiator` (locale). Jobs: T10 stores
a `RequestContextSnapshot` with the job row and calls `ITenantScopeFactory.CreateScopeAsync`. The
migrator's seeding uses the same factory with `Kind = System`. **What I need from T3:**
`ILocaleNegotiator` and the tenant locale defaults; **from T4:** the reserved system user id.

**1 — Batch abstraction (T2 owns).** What I provide: `ITenantConnection` (scoped; `CreateConnection()`),
`ITenantContext` for the connection cache key, and the `IHttpActivityFeature` request activity T2's
DB-call budget tags. What I need: the executor throws `TenantNotFoundException` when a deferred
connect statement reports no member, so the optimistic collapse (seam 16) fails closed.

**4 — Queryex schema per tenant (T2 owns).** `ITenantContext.TenantId` plus T3's settings tag form
the cache key; nothing else from this theme.

**5 — Version tags (T3 owns).** `ITenantAccessGuard` calls T4's connect step, which is where tags are
read; this theme does not read tags.

**10 — Platform exceptions (T5 types, T6 mapping).** This theme's exceptions and the mappings it
needs: `TenantNotFoundException` → 404 `tenant-not-found`; `TenantUnavailableException` → 403
`tenant-suspended` / `tenant-read-only`; `InsufficientAssuranceException` → 401 with the
`insufficient_user_authentication` challenge; CSRF rejection → 403 `csrf-rejected`. They may derive
from T5's base exception if T5 defines one; they live in `Tellma.Core.Abstractions.Tenancy` either way.

**11 — Permission evaluation (T4 owns).** What I need: `IConnectStep.ConnectAsync()` returning
`Connected | NotAMember | Deactivated` and setting `IUserContext.UserId`; a service-account client id
resolves through the same step.

**14 — Telemetry (T2 owns the budget).** This theme's instruments are in D26; meter `Tellma.Core`.

**16 — Connect-call collapse (T4/T5).** The guard's contract is only "after `EnsureAccessAsync`
returns, `UserId` is set or the request was refused"; whether the connect statement rides the first
business batch is T4/T5's, subject to seam 1's fail-closed rule.

**8 — Background-task columns (T10 owns).** Not touched; but T10's runner must consult
`ITenantCatalog.ListAsync` to enumerate tenants and skip non-`Active` ones, and must register
`ITenantStateListener` / `ISessionEndListener` for hub closing.

**12, 13, 15 — Blob tokens, wire shapes, notification enqueue.** Not touched.

**17 — Vocabulary.** Plural tables; `catalog` schema; `Tellma-Calendar` / `Tellma-Time-Zone` header
names proposed for T6; "catalog", "tenant access guard", "connect step" as terms.

---

## 7. Departures from ARCHITECTURE.md

1. **Reference distribution location.** `distributions/acme/` (Phase 1), not
   `samples/tellma-sample-distribution/`; the layout tree's `samples/` entry is removed and
   `distributions/` documented. Reason: it is a Phase-1 distribution and the template source, not a
   sample.
2. **No distribution `DbContext`.** The layout names `<Slug>DbContext.cs`; here `TellmaDbContext` is
   platform-owned and assembled from feature contributions. Reason: zero distribution code for the
   common case; one context type for the model cache.
3. **"Reuses `Tellma.Core`'s sharding code unchanged."** No such code exists; the catalog is written
   fresh (~200 lines) and the Elastic Database client library is not used (its shard-map mode ends
   support 2027-03-31; its credentials model is per-shard logins).
4. **`Tellma.Core` takes the ASP.NET Core framework reference** (the tenancy middleware, BFF and
   route groups live there); `Tellma.Core.Webhooks` is no longer "the one project" with it.
5. **Two new Core-layer packages:** `Tellma.Core.Composition` (referenced by module packs — a new
   edge in the dependency graph, permitted because it depends only on Abstractions and framework
   packages) and `Tellma.Core.Migrator` (referenced by distribution migrator projects).
6. **Feature composition at minimal fidelity:** `Requires` only; no `Recommends`/`Excludes`, slots,
   cardinality, manifest generator, Builder tool or bypass analyzer. The target stands; this is the
   first release's scope.
7. **`launchSettings.json` is tracked** in Phase 1; the template-plus-generated scheme and
   `Tellma.dev.<worktree-id>.*` database naming wait for `dotnet tellma setup-worktree`.
8. **Reserved slugs gain `acme`;** `taxonomy.json` gains `reservedSlugs` and `distributions`.
9. **In-proc identity store** (spec 0003 §2.2 "the distribution's own database") is the catalog
   database, schema `idsvr`.
10. **Every distribution has a catalog database, single-live included** — consistent with the
    architecture's assumption; recorded because the brain dump proposed otherwise.
11. **The web surface is all-POST** (T6's departure); this theme's CSRF posture assumes it and
    permits cookie `GET` only on side-effect-free endpoints.
12. **The migrator's Container Apps Job hosting** is unchanged as the target; this release ships
    the console and its commands, and the job image is infrastructure work outside the spec.
13. **Endpoint projection "read → GET, save → POST, delete → DELETE"** is T6's; noted here only
    because the tenant route groups are verb-agnostic.

---

## 8. Verification

Facts relied on from `.tmp/crud-design/research/host-tenancy.md` (verified there 2026-09-01):
- .NET 10 ships no BFF package, no cookie token refresh (postponed to ".NET 12 Planning"), no
  antiforgery changes; the OIDC handler has `PushedAuthorizationBehavior` (PAR by default, `Require`
  available); cookie auth returns 401/403 on `IApiEndpointMetadata` endpoints; Minimal API endpoints
  carry that metadata automatically.
- Duende.BFF 4.2.0 is commercial and excludes customer-facing deployments from the community edition.
- The .NET 10 antiforgery doc leaves JSON endpoints unenforced by design; RFC 10017 (BCP 212, August
  2026) makes `HttpOnly`/`Secure` MUSTs, `SameSite=Strict` a SHOULD, and the custom header the
  recommended CSRF control; OWASP's cheat sheet on header-forced preflights and `Origin` checks.
- `CookieAuthenticationEvents.ValidatePrincipal` and `ITicketStore` semantics; spec 0003 defers the
  identity-side `ITicketStore`.
- Finbuckle.MultiTenant 10.1.3 requires EF Core Relational ≥ 10.0.11 and its per-tenant-database
  recipe is incompatible with pooled contexts; EF's documented multi-tenant shape is
  `AddDbContext` with scoped options; SqlClient pools per connection string, default `Max Pool Size`
  100, idle drain 4–8 minutes.
- App Service Key Vault references resolve at startup, refresh within 24 h, and hand the app the
  literal reference string when unresolved; `Azure.Extensions.AspNetCore.Configuration.Secrets` 1.5.2.
- SqlClient `Active Directory Managed Identity` / `Default` modes; 7.0.0 moved Entra auth to
  `Microsoft.Data.SqlClient.Extensions.Azure`; `CREATE USER … FROM EXTERNAL PROVIDER` per database;
  Entra-only authentication; no managed identity on-prem.
- Azure SQL: no cross-database queries, no `USE`; elastic-pool database, session (30,000) and
  worker/login limits; the shard-map catalog stores server + database, not credentials.
- `AsyncLocal`/`ExecutionContext` semantics, `HttpContextAccessor`'s clearing holder, and the
  framework guidance to copy values and create a fresh scope for background work.
- Minimal API route groups accept route parameters and constraints; group conventions and filters
  apply to every endpoint in the group; filters run after parameter binding (inference recorded there).
- DPAPI is Windows-only; Data Protection key-at-rest options; integrated authentication from Linux
  needs Kerberos.

Facts relied on from other research files (verified there 2026-09-01):
- `web-api-mcp.md`: MCP revision 2026-07-28 (stateless, per-request `_meta`, RFC 9728 PRM with
  path insertion, RFC 8707 `resource` = canonical MCP URI, CIMD preferred, DCR deprecated); C# SDK
  2.2.0 (`MapMcp(pattern)` with route values, `AddMcp` PRM + 401 challenge, request filters with
  `HttpContext` access, `HttpServerSessionMode.Stateless` default); OpenIddict 7.6.1 has RFC 8707
  resources and exact-match `rsrc:` permission checks but no PRM, no DCR, no CIMD; client
  requirements of Claude Code, hosted Claude, ChatGPT/Codex, Cursor.
- `users-roles-permissions.md`: fallback policy applies only to endpoints with no authorization
  metadata; `AllowAnonymous` is absolute; `IAuthorizationRequirementData` on Minimal API endpoints;
  the identity invite/delivery-status APIs' exact shapes; the dev admin subject.
- `settings-cache-l10n.md`: `-u-ca-` culture names are unsafe on .NET 10/ICU; no standard time-zone
  or calendar header (GitHub's `Time-Zone` precedent); IANA ids resolve on Windows with ICU;
  invariant mode must be refused.
- `background-inbox.md`: App Service Linux drain default 5 s, host `ShutdownTimeout` 30 s, Always On
  required; Azure SignalR 1.33.1 with `CloseOnAuthenticationExpiration`; the hub user id must equal
  `sub`; self-hosted multi-instance needs Redis.
- `data-access.md`: `Microsoft.Data.SqlClient` 6.1.6 (2026-06-24) and 7.0.2; EF Core SqlServer
  10.0.11 requires SqlClient ≥ 6.1.6.

Facts verified by this design directly (2026-09-01):
- `Microsoft.Extensions.Configuration.KeyPerFile` 10.0.11 (2026-08-11), targets net10.0 /
  netstandard2.0 / net462 — https://www.nuget.org/packages/Microsoft.Extensions.Configuration.KeyPerFile
- `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.11 (2026-08-11) and
  `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.11 (2026-08-11) are separate NuGet packages
  targeting net10.0 — https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.OpenIdConnect ,
  https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer
- systemd credentials: `LoadCredential=name:path` places one file per credential in the directory
  named by `$CREDENTIALS_DIRECTORY` (`/run/credentials/<unit>` for system services; hard-coding the
  path is discouraged) — https://systemd.io/CREDENTIALS/ and
  https://github.com/systemd/systemd/blob/main/docs/CREDENTIALS.md (the systemd.exec man page
  itself refused the fetch; the introducing version is **unverified**).
- Repo facts read directly: `Tellma.Identity`'s `IClientProvisioningService.CreateDistributionAsync`
  upserts descriptors but generates fresh secrets on every call; `TellmaIdentitySeedClientKind` is
  `Cli | Native | ControlPlane`; `ClientDescriptorFactory.Distribution` registers exactly
  `{origin}/signin-oidc` and `{origin}/signout-callback-oidc`, requires PKCE and PAR, and grants
  `rsrc:{origin}`; the back-channel emitter POSTs `logout_token` as form-urlencoded with claims
  `sub`, `sid`, `jti`, `events` and `typ = logout+jwt`, audience = client id, 2-minute expiry;
  in-proc mode drops the control-plane scope; the dev admin subject is
  `00000000-0000-0000-0000-000000000001`; `Tellma.Core.csproj` is empty today; `samples/` and
  `templates/` hold only `.gitkeep`; `taxonomy.json` does not exist; the integration fixture pattern
  is Testcontainers with a `TELLMA_TEST_SQL` override; CI filters on `Category`/`Live` traits and
  runs a design-time `dotnet ef` leg.

Unverified or inferred, flagged for the spec author:
- Whether an executable-to-executable `ProjectReference` (Migrator → Web) builds cleanly under the
  Web SDK without content-item or entry-point conflicts (D4 names the fallback).
- Whether EF's design-time host-factory resolver captures the host built inside
  `TellmaMigrator.RunAsync` when `Main` is a single expression (the CI design-e2e leg is the proof).
- Whether Minimal API endpoint filters apply to the MCP SDK's `RequestDelegate`-based endpoints; D10
  assumes not and uses the SDK's request filters, which is correct either way.
- The systemd version that introduced `LoadCredential=`.
- The exact claim name spec 0003 §9.2 uses for assurance freshness (read it from
  `TellmaPrincipalFactory` when writing the spec).
- OpenIddict's exact-match resource permission behaviour was verified in the research file against
  7.6.1 source; the origin-prefix normalisation of D23 is a design proposal, not a verified feature.
