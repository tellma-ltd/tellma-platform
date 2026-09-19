# Distribution host and multi-tenancy — correctness, security, and long-term maintainability

Theme `host-tenancy`, future spec 0010. Written 2026-09-01 against `.tmp/crud-stack.md`,
`.tmp/crud-stack-specs.md`, ARCHITECTURE.md, specs 0001/0003/0007/0008, the code under
`src/core/Tellma.Core.Abstractions`, `src/apps/Tellma.Identity*`, the test hosts under `test/*/assets`,
and `.tmp/crud-design/research/host-tenancy.md` (plus the MCP, authorization, localization,
background-work and data-access research files where they touch this theme).

The optimisation order throughout: fail-closed access control first, no silent data loss second,
one code path per concern third, distribution-author convenience fourth. Where the third and the
fourth conflict, the third wins and the cost to the distribution author is stated.

---

## 1. Critique

### 1.1 The general design

The brain dump's multi-tenancy section is a description of the monolith's sharding model with the
open questions of the new platform bolted on. Four things are missing or wrong at the level of the
overall shape.

**It has no session model.** Spec 0003 §7.3 obliges every distribution to honour OIDC back-channel
logout by `sid` and to "kill its session"; §7.2 makes the distribution session independently
persistent for seven days and tied to a server-held refresh token; §9.4 makes the access-token
refresh the point where a tightened authentication policy takes effect. None of that is possible
with the stateless encrypted cookie the .NET handlers produce by default, and the brain dump does
not mention sessions, refresh, revocation, or back-channel logout at all. The distribution's
authentication is not "an OIDC relying party with a cookie"; it is a small session subsystem with a
store, a refresh loop, a revocation path, and a bounded staleness that has to be stated.

**It has no CSRF posture.** An all-POST JSON surface authenticated by a cookie is the exact shape
CSRF applies to. The .NET 10 antiforgery middleware does not enforce anything on JSON endpoints, so
the protection has to be designed, not assumed.

**It has two sources of truth for tenants and puts secrets in one of them.** "The live tenant
database connection string is defined in config" for single-live distributions and "the
tenant-to-db-conn-string map lives in a catalog db" for multi-live ones are two code paths for one
concern, and `TenantRegistry.RegisterConnectionString(tenantId, connString)` stores a full
connection string — credentials included — in a database, which the same section then forbids
("keys and secrets are never stored in the DB in clear text"). The question "if each db gets a
password, where do we store these passwords?" has a better answer than any storage location: no
tenant database gets its own password. In SaaS the distribution's managed identity is a contained
user in every tenant database; on-prem one login serves every database. The catalog stores a
location, never a credential.

**It answers "which tenants am I a member of" with an authorization decision made from the wrong
place.** Fanning a request out to every sandbox to "check if the user is a member" is an N+1 across
databases and, worse, it makes the tenant list an access-control result. Membership is a fact of
each tenant database, verified on every request by the per-tenant connect step; the cross-tenant
list is a *hint* used for navigation only and must never grant anything.

Two more structural gaps: the brain dump has no notion of a **request context** (tenant, principal,
culture, calendar, time zone, sandbox flag) as a value that reaches services and background scopes —
it appears only implicitly as "OnConnect" inside T4's flow, and the research shows that the obvious
implementation (`AsyncLocal`) is unsafe for background work and pooled contexts — and it has no
**tenant state model** (provisioning, active, read-only, suspended, retired) although
ARCHITECTURE.md makes the distribution the enforcer of suspension.

### 1.2 The detailed choices

- **`{tenantId}` is unspecified and inconsistent.** The dump writes `api/{tenantId}/documents` in one
  place and `{tenantId}/api/web/...` in another. The identifier's type decides the URL grammar, the
  MCP resource identifier, log correlation, and whether platform routes can ever collide with tenant
  routes. It must be fixed here.
- **"Every request comes with a tenantId"** is false for the distribution: the BFF login and callback
  routes, the back-channel logout receiver, `/api/distribution-info`, the admin contract surface,
  health probes, webhook receivers (which already carry `WebhookEndpointMetadata` precisely so a
  tenant middleware can skip them), and the whole in-proc identity engine at `/id/...` are
  tenant-less. The design needs a first-class *deployable* request class, or the sandbox routing
  seam has nothing sound to stand on.
- **`ISandboxContext` has no stated source.** The email spec says "distributions implement this over
  their tenant context"; the tenant context does not exist yet, and the category has to be a column
  of the catalog row, not a per-request guess.
- **The layer names question** ("Data / Service / Web") is fine at the concept level but the
  distribution's folders should name what is in them (`Entities/`, `Services/`, `Endpoints/`), which
  also matches ARCHITECTURE.md's `Entities/` folder.
- **"Should we extend the TenantRegistry to blob storage connection strings, azure key vault?"** —
  no. Per-tenant *locations* (a container name) derive from the tenant id; per-tenant *credentials*
  do not exist; the distribution's one `TokenCredential` reaches every store. A registry that hands
  out arbitrary connection strings per tenant is a secret-distribution mechanism, which is the thing
  to avoid.
- **"Should we extend the TenantRegistry to support provisioning?"** — provisioning is a job that
  needs DDL rights the web app must never hold (ARCHITECTURE.md's migrator argument). The web app
  records intent and triggers the job; the registry is a read model with one state-transition hook.
- **Caching "aggressively"** without a staleness bound is how a suspended tenant keeps serving
  traffic. Every cache in this theme has a stated maximum staleness and a stated behaviour when the
  backing store is unreachable.

### 1.3 Internal inconsistencies and misalignments with ARCHITECTURE.md

- ARCHITECTURE.md assumes "its own Catalog DB listing tenants" per distribution and says this "reuses
  `Tellma.Core`'s sharding code unchanged". No such code exists in the repo. The catalog is designed
  here from scratch; the *logical* catalog-per-distribution assumption survives, its physical
  placement becomes a configuration choice (see D5).
- ARCHITECTURE.md's layout tree lists `samples/tellma-sample-distribution/` while its phasing text
  says `distributions/<slug>/`; `taxonomy.json` does not exist; the `distributions/` folder does not
  exist. Settled in D1–D2.
- ARCHITECTURE.md says a distribution on the shared authority "references none of these projects"
  (the identity engine). The reference distribution must run with no shared services locally
  (Guiding Principles → Parallel Local Development, and spec 0003 §10.4), so it references the engine
  and selects in-proc mode by configuration. That is the opt-in ARCHITECTURE.md already describes,
  applied to the one distribution that is also the platform's own dogfood.
- Spec 0003 §4 lists the control plane calling "the distribution admin contract" with audience
  `tellma_control_plane`, but the provisioning code deliberately never grants the control-plane
  client a distribution's origin as a resource. The admin surface therefore cannot bind its tokens to
  one distribution today; D15 records the consequence and the identity-side change that fixes it.
- `today()` in spec 0008 §10.6 is "the current date in the tenant's zone"; the brain dump's web
  section wants the client to tell the server what today is. The two disagree on whose zone
  `today()` means. D9 keeps the frozen spec's meaning and states the consequence.

---

## 2. Decisions

### D1. Package topology: `Tellma.Core` composes, `Tellma.Core.AspNetCore` serves, `Tellma.Core.Abstractions` names

**Decision.** Three homes for this theme's code:

| Package | Takes | Owns from this theme |
|---|---|---|
| `Tellma.Core.Abstractions` | nothing (unchanged rule) | `RequestContext`, `IRequestContextAccessor`, `TenantDescriptor`, `TenantCategory`, `TenantState`, `ITenantMembershipDirectory`, the feature contract (`ITellmaFeature`, `FeatureDeclaration`, `FeatureContribution`, `FeatureContributionItem`), `TellmaCompositionException`, `TenancyTelemetryNames` |
| `Tellma.Core` | `Tellma.Core.Abstractions`, `Tellma.Core.Queryex`, `Tellma.Core.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.Extensions.*` | `AddTellma` and the composition pipeline, the host catalog model (`TellmaHostDbContext`, `Tenant`, `TenantMembership`, `Session`), `ITenantRegistry`, `ITenantConnectionFactory`, `ITenantScopeFactory`, `IRequestContextHolder`, `TenantSandboxContext`, `ITenantLifecycle`, `ITenantProvisioningTrigger`, the migrator engine (`TellmaMigrator`) |
| `Tellma.Core.AspNetCore` (new) | `Tellma.Core` + `FrameworkReference Microsoft.AspNetCore.App` + `Microsoft.AspNetCore.Authentication.OpenIdConnect` + `Microsoft.AspNetCore.Authentication.JwtBearer` | BFF authentication, the session store, CSRF, tenant middleware, endpoint audit, `/api/distribution-info`, the admin contract, health endpoints, the `{tenantId}` route groups T6 projects onto, Data Protection and forwarded-headers helpers |

`Tellma.Core` takes no ASP.NET dependency, so the migrator (a console host) and any future worker
host compose it without the web stack, and Core services are unit-testable without `HttpContext`.
`Tellma.Core.AspNetCore` follows the `Tellma.Core.Webhooks` precedent (the only current Core package
with a framework reference) and the `Tellma.Core.EntityFrameworkCore` naming precedent (named after
the framework it binds to).

The reference distribution lives at **`distributions/acme/`** in the shape of a distribution repo,
so that graduating it to its own repo is a folder move:

```
distributions/acme/
├── src/
│   ├── Tellma.Distro.Acme.Web/                 # ASP.NET host: composition + custom code
│   │   ├── Composition/
│   │   │   ├── AcmeComposition.cs              # AddAcme(IServiceCollection, IConfiguration) — shared with the migrator
│   │   │   └── AcmeFeature.cs                  # the distribution's own ITellmaFeature
│   │   ├── Entities/                           # sealed leaves: pack defaults extended, distro-only entities
│   │   ├── Services/                           # custom business logic beyond the projected stacks
│   │   ├── Endpoints/                          # custom endpoints mapped onto the tenant group
│   │   ├── Properties/launchSettings.template.json
│   │   ├── AcmeDbContext.cs                    # tenant model (TellmaDbContext)
│   │   ├── AcmeHostDbContext.cs                # host catalog model (TellmaHostDbContext<AcmeTenant>)
│   │   ├── Program.cs
│   │   ├── appsettings.json / appsettings.Development.json
│   │   └── Tellma.Distro.Acme.Web.csproj
│   └── Tellma.Distro.Acme.Migrator/            # console: dotnet-ef target + deploy-time migrator/provisioner
│       ├── Migrations/Tenant/                  # AcmeDbContext migrations + snapshot
│       ├── Migrations/Host/                    # AcmeHostDbContext migrations + snapshot
│       ├── AcmeDesignTimeFactories.cs
│       ├── Program.cs
│       └── Tellma.Distro.Acme.Migrator.csproj
├── test/
│   ├── Tellma.Distro.Acme.Web.Tests/           # composition parity, endpoint audit, architecture test
│   └── Tellma.Distro.Acme.IntegrationTests/    # Category=Integration: migrate + sign-in + tenant flows on Testcontainers
├── README.md
└── (client/ arrives with the UI phase)
```

Layer names: the distribution has three conceptual layers — **entities** (the model), **services**
(business logic and access control), **endpoints** (HTTP and MCP projection) — and the folders are
named after their contents, not after the layer ("`Entities/`", never "`DataLayer/`"). This is the
vocabulary ASP.NET Core, EF Core and the Minimal API docs use; "Data / Service / Web" is fine in
prose and appears nowhere in code.

**Rationale.** The migrator must share the composition root with the web host (ARCHITECTURE.md,
Data Layer → Migrations), which is only clean if the composition root has no web dependency. A
distribution-repo-shaped folder keeps the Phase-1 one-way dependency test simple (nothing under
`src/` or `test/` may reference anything under `distributions/`) and the split mechanical.

**Rejected.** Putting the web layer into `Tellma.Core` (simpler package graph; forces ASP.NET into
the migrator and every Core unit test, and gives modules a reason to reach for `HttpContext`).
`samples/acme/` (a sample is something you copy; this one is deployed, dogfooded in CI, and is the
template's source of truth — it is a distribution). A flat `distributions/acme/Tellma.Distro.Acme.Web`
without `src/`/`test/` (would have to be reorganised at graduation).

**Confidence.** High on the package split and the folder shape; medium on `distributions/` versus
`samples/`. **Review flag:** `samples/acme/` is equally defensible if Ahmad prefers "the reference
distribution is a sample" as the message to distribution agents.

### D2. Slug `acme`; the reference distribution deploys at `acme.app.tellma.com`

**Decision.** Slug `acme` (already used by the in-proc identity test host's `DeploymentIdentity`).
Project prefix `Tellma.Distro.Acme`, namespaces `Tellma.Distro.Acme.Web` / `.Migrator`,
`DeploymentIdentity("acme", environment)`, OIDC `client_id = acme`, database prefix
`Tellma.dev.<worktree>.acme.*` locally. `acme` is added to ARCHITECTURE.md's reserved-slug list as
*taken by the platform*, not as a phishing reservation, so no customer distribution can claim it.

**Rationale.** The reserved list exists because every `<slug>.app.tellma.com` host is a first-party
OIDC client; a reserved word (`sample`, `demo`, `test`) cannot be a slug, so the reference
distribution needs a real one. `acme` is unambiguous, short, and already in the repo.

**Rejected.** `sample`/`demo` (reserved, and for good reason); `reference` (not reserved but reads as
a role, not a name, and would invite a second "reference" later); `tellma` (reserved; also would put
the platform's own brand on the demo tenant's data).

**Confidence.** High.

### D3. `AddTellma` at minimal fidelity: features declare and contribute, `Requires` edges only, two aggregated gates

**Decision.** One composition call, shared verbatim by the web host and the migrator:

```csharp
// distributions/acme/src/Tellma.Distro.Acme.Web/Composition/AcmeComposition.cs
public static class AcmeComposition
{
    public static IServiceCollection AddAcme(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddTellma<AcmeDbContext, AcmeHostDbContext>("acme", configuration, tellma =>
        {
            tellma.AddFeature<GlFeature>();          // Tellma.Module.Gl: contributes the Center stack and its model
            tellma.AddFeature<AcmeFeature>();        // the distribution's own entities, services, securables
        });
    }
}
```

`AddTellma`:

1. Registers `DeploymentIdentity(slug, IHostEnvironment.EnvironmentName)` — the slug is stated once.
2. Registers the built-in `CoreFeature` (users, roles, permissions, settings, cache, request
   context, tenancy) and every feature the delegate adds, then runs the **declare** phase: each
   feature's `Declare(FeatureDeclaration)` records its stable name, its `Requires<T>()` edges and its
   `Options<T>(path)` bindings.
3. Validates the graph — unknown `Requires` targets, duplicate feature names, cycles, a feature
   added twice with different types — and throws **one** `TellmaCompositionException` listing every
   violation. Nothing is registered if this throws.
4. Runs the **contribute** phase in dependency order: each feature's `Contribute(FeatureContribution)`
   adds `FeatureContributionItem`s (service registrations, nested features, and the items other
   packages define: T5's stacks, T4's securables, T8's seeds, T2's model contributors). Core
   realises every item through `IContributionRealizer<TItem>`; an item type no realizer handles is a
   violation (a pack newer than Core), aggregated with the rest.
5. Registers the tenant `DbContext` (`AddPooledDbContextFactory<TDbContext>` per tenant, D6), the
   host `DbContext`, `ITenantRegistry`, `IRequestContextAccessor`, `ISandboxContext`
   (`TenantSandboxContext`, D9), and the `TellmaStartupValidator` hosted service as the **first**
   hosted service.
6. At startup the validator runs the realized-container checks — options validation
   (`ValidateOnStart`), model contributors resolvable, every stack's service type constructible,
   Data Protection configured outside Development (D16), `ISandboxContext` present — and throws one
   aggregated exception before the host serves traffic. `Tellma.Core.AspNetCore`'s endpoint audit
   (D10) is a second contributor to the same gate.

Edge strengths beyond `Requires` (`Recommends`, `Excludes`, slot cardinality) are not built; the
declaration API reserves the method names so adding them later is additive. The manifest source
generator, the Builder tool, and the bypass analyzer stay out (breakdown).

**How the module package and the stack feature use one shape.** `Tellma.Module.Gl` ships
`GlFeature : ITellmaFeature`; its `Contribute` adds a `StackContributionItem(typeof(Center), typeof(CenterService), …)`
(T5 defines the record in `Tellma.Core.Abstractions`) and a `ModelContributionItem(typeof(GlModelContributor))`.
`Tellma.Core` realises the stack item into the stack feature's registrations. The distribution's
`AcmeFeature` uses the identical items for its own entities. No package but `Tellma.Core` ever
sees `IServiceCollection` inside a feature, which is what keeps `Tellma.Core.Abstractions` free of
package references and modules free of `Tellma.Core`.

**Rationale.** Two gates because the two classes of error are found at different times: the graph is
known before the container exists, the realised container only after. Both are aggregated because a
composition with five problems must show five problems. Data-only contribution items make the
composition reflectable later (the Builder tool) without redesign.

**Rejected.** `FeatureContribution.Services(Action<IServiceCollection>)` (would force
`Microsoft.Extensions.DependencyInjection.Abstractions` into `Tellma.Core.Abstractions`, breaking its
no-packages rule); attribute-only declaration (fine for `Requires`, useless for options and stacks;
attributes stay as sugar over the same declaration); per-feature `AddGl()` extension methods (the
monolithic chained registration ARCHITECTURE.md replaces).

**Confidence.** High on the two-phase/two-gate shape; medium on the item/realizer mechanism.
**Review flag:** an interface-per-item (`IServiceContribution`, `IStackContribution`) instead of
records plus realizers is equally plausible; records were chosen because they are serialisable for
the future catalog.

### D4. Tenant identifier: immutable `int`, tenant-first route prefix `/{tenantId:int:min(1)}`

**Decision.** `Tenant.Id` is an `int`, app-assigned at provisioning, never reused, never renamed.
Every tenant-scoped URL starts with it:

| Surface | Route |
|---|---|
| Web API (SPA) | `/{tenantId:int:min(1)}/api/web/...` |
| Public API | `/{tenantId:int:min(1)}/api/v1/...` (seam; T6) |
| MCP | `/{tenantId:int:min(1)}/mcp` |
| SignalR hub | `/{tenantId:int:min(1)}/hub` (seam; T10) |
| SPA deep links | `/{tenantId:int:min(1)}/{**path}` served the app shell (deployable, anonymous) |

Every tenant-less route starts with a non-numeric segment (`/bff`, `/api`, `/signin-oidc`,
`/health`, `/id`, `/.well-known`), so the integer constraint alone guarantees that a platform route
can never be shadowed by a tenant route or vice versa, and that a request whose first segment is not
a positive integer is never treated as tenant-scoped.

**Rationale.** The identifier appears in the MCP resource identifier (`aud` of every agent token),
in log correlation, in blob container names and in export file names; every one of those must be
stable for the tenant's lifetime, which rules out slugs (renaming pressure) and names. Integers are
cheap to validate at the router (free 404 for garbage), and enumerability is not a security property
here — membership is verified per request (D9) and a non-member receives the same 404 as a
non-existent tenant.

**Rejected.** Slugs (`/acme-riyadh/api/web`): legible, but immutability would have to be a rule
instead of a type property, and reserved-word management appears a second time. GUIDs: stable, but
they leak into every URL the user sees and give nothing integers do not. Host-based tenancy
(`riyadh.acme.app.tellma.com`): one TLS certificate and one OIDC redirect registration per tenant.
API-first (`/api/web/{tenantId}`): the MCP path-inserted PRM and the SPA base path both prefer a
prefix.

**Confidence.** High.

### D5. One host catalog, `host` schema, EF-modelled and distribution-migrated; physical placement is configuration

**Decision.** Every distribution has exactly one **host database** — the logical "catalog DB" of
ARCHITECTURE.md — reached through `Tellma:Host:ConnectionString`. It carries the `host` schema:
`host.Tenants`, `host.TenantMemberships`, `host.Sessions`, `host.__EFMigrationsHistory` (§4). Its
model is `TellmaHostDbContext<TTenant>` in `Tellma.Core`; the distribution closes it
(`AcmeHostDbContext : TellmaHostDbContext<AcmeTenant>` or the default `Tenant`) and generates its
migrations, exactly like the tenant model. A single-live distribution points
`Tellma:Host:ConnectionString` at the live tenant's database, so the `host` schema co-locates with
the tenant schema (the identity engine's `idsvr` schema already shares a database the same way); a
multi-live distribution points it at a dedicated database. **The code has one path.** Configuration
never serves as a runtime tenant store; `Tellma:Host:SeedTenants` is read only by the migrator's
`provision` command to create the initial rows.

Rules the catalog enforces:

- A tenant row is inserted in state `Provisioning` and flipped to `Active` by the migrator only
  after the tenant database exists, is migrated, is seeded, and has its application principal (D6).
  A request for a `Provisioning` tenant is answered `503` with `Retry-After`, never routed to a
  half-built database.
- `Category` is a column, set at provisioning, immutable afterwards (a `Live` tenant never becomes
  `Sandbox`; a sandbox is a separate row with `LiveTenantId` pointing at its live tenant).
- Host-schema changes across platform minors are expand-only (a distribution migrates on bump; an
  N−1 app must keep working against an N host schema, which is the same discipline the tenant
  schema already follows).

**Rationale.** One physical database for a single-live distribution is what the brain dump asked
for; one logical catalog for every distribution is what keeps the registry, the migrator's fan-out,
the membership directory, the session store and the admin contract on a single implementation.
Modelling the catalog in EF and letting the distribution generate its migrations is the platform's
own rule for every table and lets a distribution extend `Tenant` with columns of its own — which is
exactly the "per-tenant integrations that vary per distribution" extensibility the brain dump
worries about, done as an entity extension rather than a JSON bag.

**Rejected.** A configuration-backed registry for single-live distributions (two implementations,
two test matrices, and configuration cannot hold tenant state). Shipping host migrations from the
platform (the identity precedent) — would make the host schema unextendable by a distribution and
create a second migrations-ownership rule. Reusing the Azure Elastic Database shard-map library
(contracting ecosystem; it stores nothing this design needs).

**Confidence.** High on the logical single catalog; medium on co-location as the single-live
default. **Review flag:** a tiny separate host database even for single-live distributions
(`Acme_Host`) costs one more database in the pool and removes every "same database, two contexts"
subtlety (two migrations-history tables, two sweep scopes); Ahmad may prefer it.

### D6. Tenant location, credential profiles, connection composition, and the application role

**Decision.** A tenant row carries **where** its database is, never **how** to authenticate:
`SqlServer` (host name), `SqlDatabase`, and `SqlCredentialProfile` (a name, default `"default"`).
Connection strings are composed at resolution time:

```
Server=<SqlServer>;Database=<SqlDatabase>;<profile fragment>;<Tellma:Host:ConnectionDefaults>;Application Name=<DeploymentId>
```

`Tellma:Host:SqlCredentialProfiles:<name>` holds the profile fragments; the set is small and per
deployment, not per tenant:

| Deployment | `default` (web app) | `migrator` (migrator job) |
|---|---|---|
| SaaS (Azure SQL) | `Authentication=Active Directory Managed Identity;User Id=<client id>` | same keywords, the job's own identity |
| On-prem Windows | `Integrated Security=true` | same, or a DDL service account |
| On-prem Linux | `User Id=tellma_app_login;Password=<from environment or host-injected file>` | `User Id=tellma_ddl_login;Password=…` |
| Local development | `Integrated Security=true` on LocalDB / `User Id=sa;Password=…` on the docker SQL Server | same |

`Tellma:Host:ConnectionDefaults` (default `Encrypt=Mandatory;Connect Timeout=15;Max Pool Size=32;Min Pool Size=0`)
is appended to every composed string; a tenant row cannot override `Encrypt`,
`TrustServerCertificate`, `Authentication` or any credential keyword — the composer rejects a
`SqlServer`/`SqlDatabase` value containing `;` or `=`.

**Secrets policy** (binding on every host):

- No credential, key, or token is ever stored in `host.*` or in any tenant table. The catalog stores
  locations and the *names* of profiles; the tenant `Properties` JSON column is non-secret by rule
  and may hold the *name* of a secret a connector resolves through the host's secret source.
- In SaaS, tenant databases have no passwords at all: the migrator runs
  `CREATE USER [<web app identity>] FROM EXTERNAL PROVIDER` in each new tenant database and adds it
  to the `tellma_app` role (below); the web app's connection strings contain no secret.
- Configuration values that must be secret (an on-prem SQL password, the OIDC client secret, the
  Data Protection key material references) arrive through the host's configuration providers — App
  Service Key Vault references or `AddAzureKeyVault` in SaaS, environment variables or a root-owned
  file on-prem. Startup rejects any configuration value that still begins with
  `@Microsoft.KeyVault(` (an unresolved reference) and any `Password=`/`Client Secret` appearing in
  `appsettings*.json` checked into the repository (a test in the reference distribution scans for
  it).
- Windows DPAPI is not used anywhere (Windows-only; AGENTS.md requires Linux).

**The application role.** Every tenant database has a database role `tellma_app`, created by the
migrator on first migration and granted, idempotently after every migrate run, from the model:
`SELECT, INSERT, UPDATE, DELETE` on every schema the tenant model maps, `UPDATE` on every
`sq_<Table>` sequence (`sp_sequence_get_range`), and `EXECUTE ON TYPE` for every table type (the
table-types extension's `HasGrants("tellma_app")` is the default `AddTellma` sets for every table
type). The web app's principal — the managed identity's contained user in SaaS, the login's user
on-prem — is a member of `tellma_app`; the migrator's principal owns the database. The role name is
a platform constant, so migrations never carry an environment-specific principal name and local
development (where the developer is `dbo`) applies the same migrations unchanged. This answers
ARCHITECTURE.md's open question on the ad-hoc-SQL permission set.

**Pooling.** `ITenantRegistry` caches, per tenant, the composed connection string and one
`PooledDbContextFactory<TDbContext>` built with that string; the EF model is shared (keyed by
context type). SqlClient pools are per connection string, so each active tenant costs one pool
capped at `Max Pool Size=32` per instance, drained by SqlClient after four to eight idle minutes.
`DbContext` instances are never handed a connection string after construction and never reused
across tenants; `Database.SetConnectionString` is not used.

**Rationale.** The research is unambiguous: Azure SQL with a managed identity needs no stored
password, `USE`/cross-database access does not exist, pools are per connection string, and the
tightest fleet limit is logins per elastic pool, not databases per pool. A profile *name* in the row
keeps the catalog free of secrets while still allowing the rare tenant that lives on a different
server with a different identity.

**Rejected.** A full connection string per row (secrets in the database; also lets a compromised
catalog row point the app at an attacker's server with an attacker's credential). A single global
connection string with `Initial Catalog` substituted (works for 95 % of tenants, breaks the moment
one tenant is promoted to a dedicated server). Per-tenant passwords in Key Vault (hundreds of
secrets, hundreds of GETs per cold start, 24-hour reference staleness, and no benefit over one
identity that is a contained user everywhere).

**Confidence.** High.

### D7. Registry cache, tenant states, and the suspension hook

**Decision.** `TenantState` ∈ `Provisioning | Active | ReadOnly | Suspended | Retired`. The
registry (`TenantRegistry : ITenantRegistry`) keeps one in-process entry per resolved tenant:

- An entry is refreshed synchronously on the first request after `Tellma:Host:TenantStateRefresh`
  (default 30 s) has elapsed; the refresh re-reads the whole row, so location changes ride the same
  interval. Concurrent refreshes of the same tenant are single-flighted.
- A refresh that fails because the host database is unreachable keeps serving the stale entry for
  up to `Tellma:Host:TenantStateMaxStaleness` (default 5 min) and logs at Warning; beyond that the
  tenant is answered `503 host_unavailable`. Unknown ids are negatively cached for the same 30 s.
- `ITenantLifecycle.SetStateAsync(tenantId, state, reason, actor)` — the **suspension hook** —
  writes the row (`State`, `StateReason`, `StateChangedAt`, `ModifiedBy`) and evicts the local
  entry, so the instance that received the command enforces immediately and every other instance
  enforces within 30 s. The admin contract's `suspend` response says so
  (`{"effectiveWithinSeconds": 30}`). T10's connection closer is invoked with the tenant id so
  open SignalR connections of that tenant's users are closed (seam).

Enforcement by state, applied by the tenant middleware after authentication (D9):

| State | Read request | Mutating request | Background work (`ITenantScopeFactory`) |
|---|---|---|---|
| `Provisioning` | `503 tenant_provisioning`, `Retry-After: 30` | same | throws `TenantUnavailableException` |
| `Active` | served | served | served |
| `ReadOnly` | served | `403 tenant_read_only` | scope created; T10 decides which handlers run |
| `Suspended` | `403 tenant_suspended` | same | throws |
| `Retired` | `404 tenant_not_found` (indistinguishable from absent) | same | throws |

"Mutating" is endpoint metadata (`TenantEndpointMetadata.IsMutation`), stamped by T6's projection
from the securable's action kind and by hand on custom endpoints; the audit (D10) refuses a tenant
endpoint without it.

**Rationale.** Suspension "owned and enforced by the distribution" needs a bounded latency, not an
unbounded cache. Thirty seconds on the slow path and immediate on the fast path is the honest
statement; a per-request catalog read would double the round trips of every read for a property
that changes a few times a year. Serving stale during a host-database outage — bounded — is the
only behaviour that does not turn a catalog hiccup into a fleet outage; failing closed after five
minutes is what keeps a suspension from being silently ignored forever.

**Rejected.** Reading the catalog per request (cost); an event bus for invalidation (a dependency the
on-prem shape does not have); mirroring the state into each tenant database so the connect step
reads it for free (two writers for one fact across databases with no transaction between them).

**Confidence.** High on the state set and bounds; medium on `ReadOnly` semantics (**review flag**:
whether `ReadOnly` should also refuse background mutations outright is T10's call; the descriptor
exposes the state so either policy is implementable).

### D8. Membership directory: a hint table in the host database, written on the tenant's own transitions, reconciled nightly, never an authorization source

**Decision.** `host.TenantMemberships(Subject, TenantId, IsActive, LastConfirmedAt)` is the
answer to "which tenants am I a member of", behind `ITenantMembershipDirectory` in
`Tellma.Core.Abstractions`:

- **Writers.** (1) `UserService` (T8) records `(sub, tenantId, IsActive=true)` after committing a
  user invite/activation in the tenant database and `IsActive=false` after a deactivation — a
  second, separate transaction against the host database, after the tenant commit; a failure there
  is logged and left to reconciliation. (2) The migrator's tenant bootstrap records the seeded
  admin. (3) The per-request connect step (T4) touches `LastConfirmedAt` at most once per
  `Tellma:Host:MembershipConfirmInterval` (default 24 h) per (sub, tenant), through the same
  rate-limited path it uses for `LastActive`, so the hint self-heals for users who were never
  invited through the pipeline. (4) A nightly job (T10 consumer; logic in `TenantMembershipReconciler`)
  reads every active tenant's `core.Users` (active rows) and rewrites the hint rows.
- **Readers.** `GET /bff/user` (the tenant switcher) and, later, the landing page through the
  admin contract. Every entry into a tenant is re-verified by the connect step against the tenant's
  own `core.Users` row; a stale hint can at most show a tenant the user can no longer open (they get
  the same 404 a non-member gets) or hide one they can (fixed by reconciliation or by a direct link).
- The directory answers within one host-database query; it never opens a tenant database.

**Rationale.** Fail-closed means the authorization source is the tenant database, per request. The
directory only has to be *useful*, and "eventually consistent, reconciled nightly, self-healing on
use" is the cheapest useful shape that scales to hundreds of tenants without a cross-database join
(which Azure SQL cannot do anyway).

**Rejected.** Fan-out at login (N tenant-database connections per sign-in, N logins against the pool,
and an authorization decision made from a listing); a distributed transaction between tenant and
host databases (unsupported on Linux, unnecessary for a hint).

**Confidence.** High.

### D9. Request context: an immutable record in a scoped holder, two writers, one accessor; `ISandboxContext` reads it

**Decision.** `RequestContext` (`Tellma.Core.Abstractions.Tenancy`) is an immutable record carrying
`Tenant` (a `TenantDescriptor`, or null for the deployable's own work), `Subject`, `ClientId`,
`UserId`, `Language`, `Culture`, `Calendar`, `TimeZone`, `TenantTimeZone`, `Now`, `Today`,
`OriginTraceParent`. `IRequestContextAccessor.Current` (Abstractions) is how every service reads it;
`IRequestContextHolder.Set(RequestContext)` (`Tellma.Core`, internal to the platform) is how it is
written. The holder is a **scoped** service; nothing in the platform stores the context in an
`AsyncLocal`, a static, or a singleton, and no Core service captures context values in a
constructor (an analyzer for this is deferred; the rule is stated and tested by the reference
distribution's scope-validation run in Development).

Exactly two writers exist:

1. **`TenantMiddleware`** (`Tellma.Core.AspNetCore`, placed after `UseAuthorization`), for endpoints
   carrying `TenantEndpointMetadata`. It builds the tenant part of the context from the route value
   and the registry, the principal part from the authenticated user (`sub`, `client_id`,
   `acr`/`auth_time` for step-up), stamps `Now` from `TimeProvider`, and then runs the ordered
   `IRequestContextInitializer`s: T4's connect step (`UserId`, activity stamp, the tag reads, the
   tenant-level active check — `403 user_inactive`, or `404 tenant_not_found` for a non-member) and
   T3's negotiation (`Language`, `Culture`, `Calendar`, `TimeZone`, `TenantTimeZone`, `Today`).
   Each initializer returns a new record; the middleware sets the final one. Endpoints without the
   metadata (the deployable's own routes) leave the holder at `RequestContext.Deployable`.
2. **`ITenantScopeFactory.CreateScopeAsync(RequestContext)`** (`Tellma.Core`), for background work.
   It validates the tenant's current state through the registry (D7), creates an `AsyncServiceScope`,
   sets the holder to the supplied context (which the job row stored at enqueue time: tenant, user,
   culture, calendar, time zone, origin trace), and starts an `Activity` linked to
   `OriginTraceParent` (never parented to it). T10 is its only caller.

`TenantSandboxContext : ISandboxContext` (`Tellma.Core`, scoped) returns
`accessor.Current.Tenant?.Category == TenantCategory.Sandbox`. A deployable scope is never sandboxed
— its work (identity in-proc mail, webhook processing, distribution-info) belongs to the deployable,
which is what spec 0007 means by "hosts without tenants". The invariant that makes this safe is
structural: every tenant-routed endpoint passes through `TenantMiddleware` (the audit refuses any
that would not), and every background scope is created by the factory.

`today()` binds to `RequestContext.Today`, which T3 computes in the **tenant's** time zone
(`TenantTimeZone`, from the tenant settings), as spec 0008 §10.6 defines; `TimeZone` (the user's,
from the platform time-zone header or the user's preference) formats instants in messages and
exports. There is no "today" header: a client cannot be allowed to move the tenant's business date.

**Rationale.** The research settles the mechanism (scoped holder, copied into job scopes,
`AsyncLocal` at most as a diagnostic mirror). Two writers and one accessor make "where did this
tenant come from" answerable by reading two classes. Keeping `today()` on the tenant zone keeps
spec 0008 frozen and makes `PostingDate = today()` mean the same thing for every user of a tenant,
which is the property a business date needs.

**Rejected.** `IHttpContextAccessor` in Core services (research §6.2; also drags ASP.NET into Core);
a mutable context object services can patch (two writers become twenty); computing `Today` from the
user's zone (two users in one tenant disagree on what today's postings are).

**Confidence.** High on the mechanism; medium on the `today()` position. **Review flag:** if Ahmad
wants the user's zone for `today()`, the change is one line in T3's initializer and an amendment
note in spec 0011 — but it should be a deliberate reversal of spec 0008's wording, not a drift.

### D10. Distribution-side authentication: BFF cookie with a server-side session store, refresh-anchored liveness, back-channel logout, per-surface schemes, and a startup endpoint audit

**Decision.** `services.AddTellmaAspNetCore(configuration)` registers:

- **Cookie scheme `Tellma.Session`**, cookie name `__Host-tellma.session` (`Secure`, `HttpOnly`,
  `Path=/`, no `Domain`, `SameSite=Lax`), sliding expiration with
  `Tellma:Authentication:SessionIdleLifetime` (default 7 days, equal to the authority's refresh-token
  idle window) and an absolute cap `SessionAbsoluteLifetime` (default 90 days). The cookie carries
  only a 256-bit random session key; the ticket lives in **`host.Sessions`** through
  `TellmaSessionStore : ITicketStore`, serialised with `TicketSerializer` and protected with an
  `IDataProtector` (purpose `Tellma.Core.AspNetCore.Session`) because it holds the refresh token.
  The store keeps a per-instance bounded cache of tickets for `Tellma:Authentication:RevocationLatency`
  (default 60 s), which is therefore the maximum time a revoked session keeps working on an instance
  other than the one that revoked it. The cookie handler's `IApiEndpointMetadata` behaviour makes
  every API endpoint answer 401/403 instead of redirecting.
- **OIDC scheme `Tellma.OpenIdConnect`**: `Authority` = `Tellma:Authentication:Authority` (or, in
  in-proc mode, the host's own origin plus `/id`), `ClientId` = slug, `ClientSecret` from
  configuration, `ResponseType = code`, PKCE, `PushedAuthorizationBehavior = Require`,
  `SaveTokens = true`, `MapInboundClaims = false`, `GetClaimsFromUserInfoEndpoint = false`, scopes
  `openid profile email offline_access tellma_api`, `CallbackPath = /signin-oidc`,
  `SignedOutCallbackPath = /signout-callback-oidc` (the exact URIs provisioning registers). The
  principal keeps `sub`, `sid`, `email`, `name`, `locale`, `acr`, `auth_time`, and the identity
  server's separate `acr` evidence-time claim; nothing else.
- **Refresh-anchored liveness.** `OnValidatePrincipal` (a scoped `CookieAuthenticationEvents`
  subclass, `TellmaCookieEvents`) refreshes the tokens when the stored access token is within two
  minutes of expiry, at most once per access-token lifetime, single-flighted per session key
  in-process (the authority's 30 s reuse leeway covers cross-instance races); the refreshed id
  token's claims replace the principal's (`acr`, allowed-method outcomes, locale), and the ticket
  is renewed. `invalid_grant` or any 4xx from the token endpoint rejects the principal and removes
  the session: authority-side "sign out everywhere", policy tightening, and user disablement take
  effect within one access-token lifetime even if back-channel delivery failed. A 5xx or network
  failure keeps the session for up to `Tellma:Authentication:MaxAuthorityOutage` (default 1 h),
  after which the session is rejected — the authority being down must not become a permanent
  session.
- **Back-channel logout receiver** `POST /signout-backchannel` (anonymous, antiforgery-exempt,
  form-encoded `logout_token`): validates the JWT against the authority's JWKS (signature, `iss`,
  `aud` = client id, `iat`/`exp`, `events` containing
  `http://schemas.openid.net/event/backchannel-logout`, no `nonce`, `sid` or `sub` present, `jti`
  replay-checked in-process for the token's lifetime), revokes by `sid` (or by `sub` when only `sub`
  is present), and answers `200`; any validation failure answers `400` with no body. The
  endpoint is what the provisioning call registers as `backchannel_logout_uri`.
- **BFF endpoints** (all deployable, all `__Host-tellma.session`-scheme):
  `GET /bff/login?returnUrl=` (local-only `returnUrl`, challenges the OIDC scheme),
  `POST /bff/logout` (global by default: local sign-out then RP-initiated end-session with
  `id_token_hint`; `?scope=local` signs out of this distribution only), `GET /bff/user` (display
  profile plus the membership hints from D8: `{ sub, name, email, locale, tenants: [...] }`).
- **Bearer scheme `Tellma.Bearer`** (`Microsoft.AspNetCore.Authentication.JwtBearer`): authority =
  the same issuer, `ValidTypes = ["at+jwt"]`, audience validated per request against the canonical
  resource of the route — `https://<host>/{tenantId}/mcp` under the MCP route, `https://<host>` under
  `/api/v1` and the admin contract (D13/D15) — `NameClaimType = "sub"`.
- **Policies** (`TellmaPolicies`): `Web` = scheme `Tellma.Session` + authenticated;
  `Api` = scheme `Tellma.Bearer` + scope `tellma_api`; `Mcp` = `Api` + the MCP audience;
  `ControlPlane` = scheme `Tellma.Bearer` + scope `tellma_control_plane`;
  `FallbackPolicy = RequireAuthenticatedUser` (default scheme `Tellma.Session`). A policy names its
  scheme explicitly, so a cookie can never authenticate a bearer surface and a bearer can never
  authenticate the web surface — cross-surface replay is impossible by construction.
- **Endpoint audit** (`TellmaEndpointAudit`, a startup hosted service over `EndpointDataSource`,
  contributing to the aggregated gate of D3): every endpoint whose route begins with the `tenantId`
  parameter must carry `TenantEndpointMetadata` (with `Surface` and `IsMutation`), a policy from the
  set allowed for its surface, and no `IAllowAnonymous`; every other endpoint must carry
  `DeployableEndpointMetadata` — stamped by the platform on the routes it maps, by
  `WebhookEndpointMetadata` on webhook receivers, by assembly membership for the in-proc identity
  engine's controllers and pages, and by `.AsDeployableEndpoint("reason")` on anything a distribution
  maps outside a tenant group. Anything else is a violation named by route and method.
- **Session revocation on user deactivation.** Tenant-level deactivation is an authorization outcome
  of the connect step (`403 user_inactive`), immediate on every instance, plus T10's connection
  close; it does not end the distribution session, because the user may belong to other tenants.
  `ITellmaSessionStore.RevokeBySubjectAsync(sub)` exists for the case where an administrator
  removes a subject from the distribution entirely (the control plane, or T8's "remove from all
  tenants" if it ships) and for the sub-only back-channel token.

**Rationale.** A stateless cookie cannot honour a `sid`-keyed logout without a revoked list that has
to live somewhere anyway; a server-side session gives immediate revocation on the revoking instance,
bounded staleness elsewhere, small cookies, a per-user session list, and a place to keep the refresh
token out of the browser. Anchoring liveness on refresh is what spec 0003 designed the refresh
token for. Per-surface schemes plus the audit are the "hard to leave unsecured" mechanism the
research recommends (fallback policy + metadata + startup audit), and they close the specific hole
where a cookie-bearing cross-site request reaches a bearer surface.

**Rejected.** Stateless cookie + revoked-`sid` list (research option b): fewer tables, but a 3–6 KB
cookie on every API call (tokens inside), no session listing, a revoked list that still needs a
store and a sweep, and a `ValidatePrincipal` throttle that is the same staleness bound with more
moving parts. `Duende.BFF` (paid per customer-facing deployment). OpenIddict validation instead of
`JwtBearer` for the bearer scheme (works; `JwtBearer` is what spec 0003 §6 names for resource
servers and has no OpenIddict coupling).

**Confidence.** High on the shape; medium on the exact lifetimes. **Review flag:** the stateless
alternative is genuinely plausible for a first release; the table, the sweep (a T10 consumer) and
the Data Protection dependency are the price of immediate revocation and small cookies.

### D11. CSRF posture for the cookie-authenticated surfaces

**Decision.** For every request authenticated by `Tellma.Session` (the web surface, `/bff/*`,
the hub negotiate), `TellmaCsrfMiddleware` (before the tenant middleware) enforces, in order:

1. If `Sec-Fetch-Site` is present, it must be `same-origin` or `none`; otherwise `403 csrf_rejected`.
2. If `Origin` is present, it must equal the request's own origin (scheme + host + port);
   otherwise `403`.
3. Unless the request is a top-level navigation (`Sec-Fetch-Mode: navigate`, which only the login
   and callback routes accept) or a WebSocket upgrade (whose `Origin` was checked in step 2), the
   request must carry the header `Tellma-Client` (any non-empty value; the SPA sends its build
   version); otherwise `403`.
4. A request with a body must declare `Content-Type: application/json` (the blob upload endpoint,
   T7, opts into `multipart/form-data` explicitly and still requires the header).

No CORS policy is registered anywhere in the platform; a distribution that adds one to a
cookie-authenticated group is a startup audit violation. The bearer surfaces are exempt (no cookie,
no CSRF). `SameSite=Lax` and the `__Host-` prefix are defence in depth, not the control.

**Rationale.** RFC 10017 §6 makes a required custom header the primary CSRF control for a BFF; the
.NET 10 antiforgery middleware does nothing for JSON endpoints; a per-session token would add a
round trip and a second cookie for no additional protection over a custom header plus an origin
check on a surface that never accepts forms. `Sec-Fetch-Site` and `Origin` cannot be set by script,
so checking them when present costs nothing and catches the misconfigured-proxy case.

**Rejected.** Antiforgery tokens (unneeded for JSON; kept in reserve for a form-posting endpoint if
one appears); `SameSite=Strict` (breaks the post-login redirect and deep links; spec 0003 says `Lax`).

**Confidence.** High.

### D12. Step-up and tenant policy at the door

**Decision.** `TenantMiddleware` compares the session's `acr` evidence (from the principal) against
the tenant's required tier (T4's connect step surfaces `RequiredAcr` and `MaxAge` from the tenant
settings) and, when under-assured, writes spec 0003 §9.3's challenge through
`StepUpChallenge.Write(HttpResponse, acrValues, maxAge)`:
`401 WWW-Authenticate: Bearer error="insufficient_user_authentication", acr_values="…", max_age=…`.
The SPA answers by navigating to `/bff/login?acr_values=…&max_age=…&returnUrl=…`, which forwards
both into the pushed authorization request. Sensitive *operations* (a fixed set, T5 marks them on
the service method) use the same writer from the endpoint filter T5 installs. Initial sign-in to the
distribution requests no tier and no method restriction — a user has no tenant yet; tenant policy
applies when a tenant is entered.

**Confidence.** High; the mechanism is spec 0003's, only the placement is decided here.

### D13. MCP topology: one endpoint per tenant, one code path, per-tenant audience; what the identity server must add

**Decision.** `/{tenantId:int}/mcp` is mapped by `Tellma.Core.AspNetCore` inside the tenant group
when T6's MCP feature is enabled, in the SDK's stateless mode. Its RFC 8707 resource identifier is
`https://<host>/{tenantId}/mcp`; its RFC 9728 document is served by the platform at
`/.well-known/oauth-protected-resource/{tenantId:int}/mcp` (path insertion), with
`authorization_servers = [issuer]`, `scopes_supported = ["tellma_api"]`, `bearer_methods_supported = ["header"]`;
the 401 challenge names that document. Bearer tokens are validated by `Tellma.Bearer` with the
audience bound to the requested tenant's resource identifier, so a token minted for tenant A is
structurally invalid at tenant B. Human users arrive through a pre-registered public native client
(one per vendor: Claude Code, Codex, Cursor, …) and are resolved by `sub` through the ordinary
connect step; autonomous agents arrive with a service account's `client_credentials` token and are
resolved by `client_id` to a tenant service-account user (T4). An agent that needs two tenants
configures two servers.

**What the identity server must add** (recorded for the identity spec's next revision; nothing here
compiles today):

1. A distribution-facing **resource registration API** (`tellma_identity` scope): register
   `https://<own origin>/{tenantId}/mcp` as a grantable resource on the `tellma_api` scope and grant
   it to the platform's MCP clients and the caller's service accounts — ownership-scoped to the
   caller's origin exactly as service-account audiences are today. Called by the migrator's
   `provision` command; a tenant without it has no MCP surface (fail closed: the PRM document is
   served, the token cannot be obtained).
2. **CIMD** (`client_id_metadata_document_supported: true`, and `none` in
   `token_endpoint_auth_methods_supported`) so Claude Code, hosted Claude and Codex register without
   a manual client id; DCR stays unimplemented (deprecated by MCP).
3. `code_challenge_methods_supported: ["S256"]` (already emitted) and refresh-token rotation for the
   public MCP clients (already OpenIddict's default; verify in the identity suite).

**Rationale.** Per-tenant endpoints keep the audience a structural boundary (the property spec 0003
already relies on between distributions), keep the tool list a function of one tenant's permissions,
and give agents the live/sandbox pairing the brain dump wants without a "switch tenant" tool that
would have to re-authorize mid-session. Stateless mode needs no affinity.

**Rejected.** One server per distribution with a tenant argument on every tool (audience covers the
distribution, so a token replays across tenants and every tool grows a parameter that authorization
has to re-check); a distribution-wide resource with tenant enforcement from membership only
(the research's fallback — acceptable only until the identity server can register per-tenant
resources, and then only with the audit noting it).

**Confidence.** High on topology; medium on the identity-side additions being the minimal set.

### D14. Provisioning is a migrator command; the web app records intent and triggers a job

**Decision.** `Tellma.Distro.<Slug>.Migrator` is the single DDL-privileged executable, with these
commands (implemented once in `Tellma.Core`'s `TellmaMigrator`, driven by the distribution's
`Program.Main` through `TellmaMigratorHost.RunAsync(args, services => services.AddAcme(configuration))`):

| Command | Does |
|---|---|
| `migrate host` | Migrates the host database (`host` schema; and `idsvr` when the distribution runs identity in-proc), under `sp_getapplock('tellma:migrate:host')`. |
| `migrate tenants [--tenant <id>]* [--parallelism N]` | For every `Active`/`ReadOnly`/`Provisioning` tenant (or the named ones): applock `tellma:migrate:<database>`, `Migrate()`, versioned seeds (`__SeedHistory`, T8), `EnsureApplicationRole` grants (D6), RCSI check (report only for existing databases). Bounded parallelism, continues past failures, prints a per-tenant report, exits 2 on any failure. |
| `provision tenant --id <id> --name … --category Live\|Sandbox [--live-tenant <id>] --server … --database … [--admin-email …] [--from-config]` | Inserts the row as `Provisioning` (or reads it if the web app inserted it), creates the database (Azure: in the configured pool; on-prem: plain `CREATE DATABASE`), enables RCSI on the fresh database, runs the `migrate tenants` steps for it, creates the application principal from `Tellma:Host:ApplicationPrincipal` (`CREATE USER … FROM EXTERNAL PROVIDER` or `FOR LOGIN`) and adds it to `tellma_app`, bootstraps the first admin (T4/T8), registers the tenant's MCP resource with the identity server (D13), records the admin's membership hint, and flips the row to `Active`. Idempotent: rerunning converges. |
| `status` | Lists tenants with state, schema version, and pending migrations. |

The web app's `POST /api/admin/tenants` (control-plane surface) inserts the `Provisioning` row and
calls `ITenantProvisioningTrigger.StartAsync(tenantId)`. The default implementation
(`NotConfiguredProvisioningTrigger`) throws `TellmaCompositionException` at first use with the
message that the distribution must register a trigger (an Azure Container Apps Job starter, a local
process starter in Development); the reference distribution ships the local starter and the Job
starter behind `Tellma:Provisioning:Trigger`. Sandbox creation from a live tenant (database copy +
scrub) is a later provisioning command; the row shape already supports it (`LiveTenantId`).

**Rationale.** ARCHITECTURE.md's argument (one image, one DDL identity, version ordering) is
correct and unchanged; what is added is the command surface, the idempotency rule, and the exact
sequence in which a tenant becomes routable — last, after everything the first request will need.

**Confidence.** High.

### D15. Distribution contract surface, admin contract, health

**Decision.** Mapped by `MapTellma()`:

- `GET /api/distribution-info` (anonymous, `Cache-Control: public, max-age=60`):

  ```json
  {
    "slug": "acme",
    "displayName": "Acme",
    "deploymentId": "acme-staging",
    "platformVersion": "1.4.2",
    "issuer": "https://identity.tellma.com",
    "identityMode": "Standalone",
    "surfaces": { "web": "/{tenantId}/api/web", "api": "/{tenantId}/api/v1", "mcp": "/{tenantId}/mcp" },
    "login": "/bff/login"
  }
  ```

  No tenant list, no environment secrets, no instance identity.
- Admin contract (policy `ControlPlane`, bearer, scope `tellma_control_plane`, audience
  `urn:tellma:control-plane` **until** the identity server grants the control-plane client each
  distribution's origin — recorded as an identity-side change, because a control-plane token that
  is valid at every distribution is a fleet-wide capability the audience should not have to be):
  `GET /api/admin/info`, `GET /api/admin/tenants`, `POST /api/admin/tenants` (D14),
  `POST /api/admin/tenants/{id}/suspend` / `.../unsuspend` / `.../read-only` (body `{ "reason": "…" }`,
  D7), `GET /api/admin/tenants/{id}/members` (the hint table, D8). Usage/metering endpoints belong
  to T10 and later.
- Health: `GET /health/live` (always 200 while the process runs) and `GET /health/ready` (host
  database reachable, Data Protection keys loadable, authority discovery document cached), both
  anonymous, both deployable. App Service health checks point at `/health/ready`.

**Confidence.** High on the surface; low on the control-plane audience (**review flag**: see the
identity-side note).

### D16. Host baseline: Data Protection, forwarded headers, TLS

**Decision.** `AddTellmaAspNetCore` also owns:

- **Data Protection.** `SetApplicationName("tellma-" + slug)` so slot swaps keep sessions valid.
  Keys: Azure Blob + Key Vault key when `Tellma:DataProtection:BlobUri` and `KeyId` are set (the
  pinned `Azure.Extensions.AspNetCore.DataProtection.*` packages, the host's `TokenCredential`),
  otherwise `Tellma:DataProtection:KeyRingPath` (file system, optionally
  `ProtectKeysWithCertificate`). Outside Development, neither configured is a startup failure — a
  multi-instance deployment with per-instance keys would produce intermittently rejected cookies,
  which is the kind of failure that gets diagnosed for a week.
- **Forwarded headers.** `AddTellmaForwardedHeaders(configuration)` reproduces the identity host's
  rule set (an `Enabled` flag requires at least one known proxy or network; the framework's
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED` switch is refused). Identity's inline copy should adopt it
  later.
- **TLS.** HTTPS redirection and HSTS outside Development; the session cookie's `__Host-` prefix
  makes plain HTTP structurally unable to carry a session.

**Confidence.** High.

### D17. Telemetry and logging

**Decision.** Meter `Tellma.Core` (names in `Tellma.Core.Abstractions.Tenancy.TenancyTelemetryNames`):
`tellma.tenancy.resolutions` (counter, tag `outcome` ∈ `served|not_found|provisioning|read_only|suspended|not_member|user_inactive|host_unavailable`),
`tellma.tenancy.registry.reads` (counter, tag `result` ∈ `hit|refreshed|stale_served|failed`),
`tellma.tenancy.scopes` (counter, tag `kind` ∈ `request|background`). Meter `Tellma.Core.AspNetCore`
(names in `HostTelemetryNames` in that package): `tellma.session.operations` (counter, tags
`operation` ∈ `store|retrieve|renew|remove|revoke`, `result` ∈ `ok|miss|expired|failed`),
`tellma.session.refreshes` (counter, tag `outcome` ∈ `refreshed|rejected|deferred`),
`tellma.auth.backchannel_logouts` (counter, tag `outcome` ∈ `accepted|rejected`),
`tellma.auth.csrf_rejections` (counter, tag `rule` ∈ `sec_fetch_site|origin|header|content_type`).
No tenant tag on any instrument. Every tenant request runs inside a logger scope with `TenantId`,
`Subject`, `UserId`; background scopes carry the same plus `JobId` (T10). The `infra/monitoring/`
convention (checked-in KQL cross-checked against the emitted names) applies.

**Confidence.** High.

### D18. Testing

| Suite | Tier | Pins |
|---|---|---|
| `test/core/Tellma.Core.Tests` | unit | composition graph validation (aggregation, cycles, unknown items), connection composition and the keyword blacklist, registry cache bounds (fake clock via `TimeProvider`), state enforcement table, `RequestContext` immutability, sandbox context |
| `test/core/Tellma.Core.IntegrationTests` | `Category=Integration` (Testcontainers) | host model migration, registry against a real catalog, `EnsureApplicationRole` grants, membership directory writers and reconciler, session store CRUD and expiry sweep |
| `test/core/Tellma.Core.AspNetCore.Tests` | unit (`WebApplicationFactory`, fake authentication handler, in-memory registry) | tenant middleware outcomes per state and membership, CSRF matrix, policy/scheme isolation (cookie on a bearer route fails, bearer on the web route fails), endpoint audit violations, back-channel token validation vectors (bad `iss`, bad `aud`, missing `events`, `nonce` present, expired, sub-only), step-up challenge shape, distribution-info shape |
| `distributions/acme/test/Tellma.Distro.Acme.Web.Tests` | unit | composition parity (web and migrator build byte-identical models for both contexts), the audit passes for every mapped endpoint, one-way dependency (no `src/` project references `distributions/`), no secrets in tracked configuration |
| `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests` | `Category=Integration` | migrator `migrate host` + `provision tenant` on Testcontainers; full sign-in through the in-proc identity engine (reusing `test/apps/Tellma.Identity.IntegrationTests`' `OidcFlowClient`), first tenant request, suspension round trip, back-channel logout end to end, refresh rejection ends the session |

`Live=true` suites: none in this theme.

### D19. What may not change within a family major

Route shapes (`/{tenantId}/api/web`, `/{tenantId}/api/v1`, `/{tenantId}/mcp`, `/{tenantId}/hub`,
`/bff/*`, `/signin-oidc`, `/signout-callback-oidc`, `/signout-backchannel`, `/api/distribution-info`,
`/api/admin/*`, `/health/*`, `/.well-known/oauth-protected-resource/*`, `/id/*`), the
`Tellma:*` configuration keys (additive only), the `host` schema (expand-only), the feature contract
(`ITellmaFeature`, item records — additive only; a removed item type is a major), `RequestContext`
members (additive only), and the `tellma_app` role name. A distribution's custom endpoints live only
under the tenant group or under `/api/<non-reserved>` marked deployable; the audit refuses a
distribution endpoint under any reserved prefix.

---

## 3. Contracts

Code is normative for shape, not formatting. `using` lines are omitted; every member carries the
XML summary the repo requires.

### 3.1 `Tellma.Core.Abstractions` (no package references)

```csharp
namespace Tellma.Core.Abstractions.Tenancy;

/// <summary>Whether a tenant's actions may produce external side effects.</summary>
public enum TenantCategory
{
    /// <summary>A real customer tenant; side effects reach the outside world.</summary>
    Live = 0,
    /// <summary>A test tenant paired with a live one; every external side effect is withheld or routed to a sandbox channel.</summary>
    Sandbox = 1,
}

/// <summary>The lifecycle state of a tenant, as enforced by the distribution on every request.</summary>
public enum TenantState
{
    /// <summary>The row exists; the database is not yet routable. Requests answer 503.</summary>
    Provisioning = 0,
    /// <summary>Serving reads and writes.</summary>
    Active = 1,
    /// <summary>Serving reads; every mutating request answers 403.</summary>
    ReadOnly = 2,
    /// <summary>Every request answers 403; data retained.</summary>
    Suspended = 3,
    /// <summary>Indistinguishable from a tenant that never existed.</summary>
    Retired = 4,
}

/// <summary>
///     What a service may know about the ambient tenant: identity, display name, category and
///     state. Never the database location, which stays inside <c>Tellma.Core</c>.
/// </summary>
/// <param name="Id">The immutable tenant id that prefixes every tenant-scoped URL.</param>
/// <param name="Name">The display name shown before the tenant's own settings are loaded.</param>
/// <param name="Category">Live or sandbox; drives <see cref="ISandboxContext"/>.</param>
/// <param name="State">The state at resolution time; bounded staleness applies.</param>
/// <param name="LiveTenantId">For a sandbox, the live tenant it belongs to; null for a live tenant.</param>
public sealed record TenantDescriptor(int Id, string Name, TenantCategory Category, TenantState State, int? LiveTenantId);

/// <summary>
///     The immutable facts every unit of work runs under: the tenant (or none, for the
///     deployable's own work), the principal, the presentation preferences, and the fixed clock
///     of the request. Written by the platform's two context writers only; read everywhere
///     through <see cref="IRequestContextAccessor"/>.
/// </summary>
public sealed record RequestContext
{
    /// <summary>The context of tenant-less work: the deployable acting for itself.</summary>
    public static RequestContext Deployable { get; } = new();

    /// <summary>The ambient tenant, or null for deployable work.</summary>
    public TenantDescriptor? Tenant { get; init; }

    /// <summary>The authenticated subject (<c>sub</c>), or null when anonymous.</summary>
    public string? Subject { get; init; }

    /// <summary>The OAuth client the request arrived through (<c>client_id</c>/<c>azp</c>), when bearer-authenticated.</summary>
    public string? ClientId { get; init; }

    /// <summary>The tenant-local user id, once the connect step resolved it; null before that and for deployable work.</summary>
    public int? UserId { get; init; }

    /// <summary>The language of messages and errors (BCP 47, no extensions), negotiated by the platform.</summary>
    public string Language { get; init; } = "en";

    /// <summary>The formatting culture name (BCP 47, no extensions).</summary>
    public string Culture { get; init; } = "en";

    /// <summary>The calendar id for date rendering (<c>gregorian</c>, <c>umalqura</c>, <c>ethiopic</c>); carried separately from the culture on purpose.</summary>
    public string Calendar { get; init; } = "gregorian";

    /// <summary>The user's IANA time zone id, for rendering instants.</summary>
    public string TimeZone { get; init; } = "UTC";

    /// <summary>The tenant's IANA time zone id, in which <see cref="Today"/> is computed.</summary>
    public string TenantTimeZone { get; init; } = "UTC";

    /// <summary>The instant the unit of work started; every use of "now" in it sees this value.</summary>
    public DateTimeOffset Now { get; init; }

    /// <summary>The business date in <see cref="TenantTimeZone"/>; what <c>today()</c> binds to.</summary>
    public DateOnly Today { get; init; }

    /// <summary>The W3C <c>traceparent</c> of the request that caused this work, for background scopes to link to; null otherwise.</summary>
    public string? OriginTraceParent { get; init; }

    /// <summary>True when the ambient tenant is a sandbox.</summary>
    public bool IsSandbox => Tenant?.Category == TenantCategory.Sandbox;

    /// <summary>The ambient tenant, or an <see cref="InvalidOperationException"/> naming the caller when the work is not tenant-scoped.</summary>
    public TenantDescriptor RequireTenant() => Tenant ?? throw new InvalidOperationException("The current unit of work is not tenant-scoped.");
}

/// <summary>Read access to the ambient <see cref="RequestContext"/>. Scoped; never cached in a field.</summary>
public interface IRequestContextAccessor
{
    /// <summary>The current context; <see cref="RequestContext.Deployable"/> when nothing set one.</summary>
    RequestContext Current { get; }
}

/// <summary>One row of the cross-tenant membership hint.</summary>
/// <param name="Tenant">The tenant, with its state at read time.</param>
/// <param name="IsActive">The hint's last known activity of the user in that tenant.</param>
/// <param name="LastConfirmedAt">When a tenant-side fact last confirmed the hint.</param>
public sealed record TenantMembership(TenantDescriptor Tenant, bool IsActive, DateTimeOffset LastConfirmedAt);

/// <summary>
///     The best-effort answer to "which tenants is this subject a member of". A navigation aid:
///     every entry into a tenant is re-verified against that tenant's own user table, so a
///     stale answer can never grant access.
/// </summary>
public interface ITenantMembershipDirectory
{
    /// <summary>Lists the hints for a subject, excluding retired tenants.</summary>
    Task<IReadOnlyList<TenantMembership>> ListAsync(string subject, CancellationToken cancellationToken);

    /// <summary>Records a tenant-side fact about a subject's membership. Called after the tenant transaction committed; failures are logged and left to reconciliation.</summary>
    Task RecordAsync(string subject, int tenantId, bool isActive, CancellationToken cancellationToken);
}

/// <summary>The meter and instrument names of the tenancy layer.</summary>
public static class TenancyTelemetryNames
{
    /// <summary>The meter, named after the emitting package.</summary>
    public const string MeterName = "Tellma.Core";
    /// <summary>Tenant resolutions by outcome.</summary>
    public const string Resolutions = "tellma.tenancy.resolutions";
    /// <summary>Registry reads by result.</summary>
    public const string RegistryReads = "tellma.tenancy.registry.reads";
    /// <summary>Scopes created by kind.</summary>
    public const string Scopes = "tellma.tenancy.scopes";
    /// <summary>The tag naming an outcome or result.</summary>
    public const string OutcomeTag = "outcome";
}
```

```csharp
namespace Tellma.Core.Abstractions.Composition;

/// <summary>
///     A composable unit of a distribution: declares what it needs (data) and contributes what it
///     brings (also data — items the composition root realises). Implemented by module packages,
///     by the platform's own stack features, and by the distribution.
/// </summary>
public interface ITellmaFeature
{
    /// <summary>The stable, unique, lowercase dotted name ("gl", "core.users", "acme.inventory").</summary>
    string Name { get; }

    /// <summary>Records edges and options; runs before any container exists.</summary>
    void Declare(FeatureDeclaration declaration);

    /// <summary>Records contribution items; runs in dependency order after the graph validated.</summary>
    void Contribute(FeatureContribution contribution);
}

/// <summary>The declaration side of a feature: edges and options.</summary>
public sealed class FeatureDeclaration
{
    /// <summary>A hard dependency: unmet is a composition error.</summary>
    public FeatureDeclaration Requires<TFeature>() where TFeature : ITellmaFeature;

    /// <summary>An options type bound from a configuration path and validated at startup.</summary>
    public FeatureDeclaration Options<TOptions>(string configurationPath) where TOptions : class, new();
}

/// <summary>The base of everything a feature contributes. Records, so the composition can be inspected and, later, serialised.</summary>
public abstract record FeatureContributionItem;

/// <summary>The service lifetimes a contribution may request; mirrors the DI abstractions without referencing them.</summary>
public enum ServiceLifetimeKind
{
    /// <summary>One instance per container.</summary>
    Singleton,
    /// <summary>One instance per request or background scope.</summary>
    Scoped,
    /// <summary>A new instance per resolution.</summary>
    Transient,
}

/// <summary>A service registration.</summary>
/// <param name="ServiceType">The contract.</param>
/// <param name="ImplementationType">The implementation; must be constructible by the container.</param>
/// <param name="Lifetime">The lifetime.</param>
public sealed record ServiceContributionItem(Type ServiceType, Type ImplementationType, ServiceLifetimeKind Lifetime) : FeatureContributionItem;

/// <summary>A class contributing EF model configuration; the type implements the EF package's model-contributor contract, checked at startup.</summary>
/// <param name="ContributorType">The contributor.</param>
public sealed record ModelContributionItem(Type ContributorType) : FeatureContributionItem;

/// <summary>The contribution side of a feature.</summary>
public sealed class FeatureContribution
{
    /// <summary>Adds one item; the composition root's realizers interpret it.</summary>
    public FeatureContribution Add(FeatureContributionItem item);

    /// <summary>Adds a nested feature (a module adds its stacks this way); its declaration is validated with the rest.</summary>
    public FeatureContribution Feature(ITellmaFeature feature);

    /// <summary>A scoped service registration.</summary>
    public FeatureContribution Scoped<TService, TImplementation>() where TImplementation : class, TService;

    /// <summary>A singleton service registration.</summary>
    public FeatureContribution Singleton<TService, TImplementation>() where TImplementation : class, TService;

    /// <summary>A transient service registration.</summary>
    public FeatureContribution Transient<TService, TImplementation>() where TImplementation : class, TService;

    /// <summary>A model contributor.</summary>
    public FeatureContribution Model<TContributor>() where TContributor : class;
}

/// <summary>Every composition violation found by one gate, together. Thrown by <c>AddTellma</c> (graph gate) and by the startup validator (realized gate).</summary>
public sealed class TellmaCompositionException(IReadOnlyList<string> violations)
    : InvalidOperationException("The Tellma composition is invalid: " + string.Join(" ", violations))
{
    /// <summary>Each violation names the feature, the problem, and a suggested fix.</summary>
    public IReadOnlyList<string> Violations { get; } = violations;
}
```

### 3.2 `Tellma.Core`

```csharp
namespace Tellma.Core.Composition;

/// <summary>The composition root's entry point.</summary>
public static class TellmaServiceCollectionExtensions
{
    /// <summary>
    ///     Composes a distribution: registers the deployment identity, the core feature and the
    ///     selected features, validates the graph (throwing one aggregated exception), realises
    ///     every contribution, and installs the startup gate. Shared verbatim by the web host and
    ///     the migrator so both build the same models.
    /// </summary>
    /// <typeparam name="TDbContext">The tenant model.</typeparam>
    /// <typeparam name="THostDbContext">The host catalog model.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="slug">The distribution slug; becomes the deployment identity's application name.</param>
    /// <param name="configuration">The root configuration; the <c>Tellma</c> section is bound from it.</param>
    /// <param name="configure">Selects and configures features.</param>
    public static IServiceCollection AddTellma<TDbContext, THostDbContext>(
        this IServiceCollection services, string slug, IConfiguration configuration, Action<TellmaBuilder> configure)
        where TDbContext : TellmaDbContext
        where THostDbContext : TellmaHostDbContext;
}

/// <summary>The selection surface inside <c>AddTellma</c>.</summary>
public sealed class TellmaBuilder
{
    /// <summary>Adds a feature by type; constructed by the composition, not by DI.</summary>
    public TellmaBuilder AddFeature<TFeature>() where TFeature : ITellmaFeature, new();

    /// <summary>Adds a configured feature instance.</summary>
    public TellmaBuilder AddFeature(ITellmaFeature feature);

    /// <summary>Overrides the default table-type grant principals (default: the <c>tellma_app</c> role).</summary>
    public TellmaBuilder TableTypeGrants(params string[] principals);
}

/// <summary>Realises one kind of contribution item into container registrations; Core ships one per item type it understands.</summary>
public interface IContributionRealizer<in TItem> where TItem : FeatureContributionItem
{
    /// <summary>Applies the item, or reports violations to be aggregated.</summary>
    void Realize(TItem item, ITellmaFeature owner, RealizationContext context);
}
```

```csharp
namespace Tellma.Core.Tenancy;

/// <summary>Where a tenant's database is and which credential profile reaches it. Never leaves <c>Tellma.Core</c>.</summary>
/// <param name="SqlServer">The server host name.</param>
/// <param name="SqlDatabase">The database name.</param>
/// <param name="SqlCredentialProfile">The name of a configured credential profile.</param>
public sealed record TenantLocation(string SqlServer, string SqlDatabase, string SqlCredentialProfile);

/// <summary>A resolved tenant: descriptor, location, and the composed connection string.</summary>
/// <param name="Descriptor">What services may see.</param>
/// <param name="Location">Where the database is.</param>
/// <param name="ConnectionString">The composed connection string (credential keywords from the profile, defaults appended).</param>
/// <param name="Properties">The non-secret, distribution-defined property bag; null when absent.</param>
/// <param name="ResolvedAt">When this entry was last read from the catalog.</param>
public sealed record TenantInfo(TenantDescriptor Descriptor, TenantLocation Location, string ConnectionString, string? Properties, DateTimeOffset ResolvedAt);

/// <summary>The catalog read model with its bounded cache.</summary>
public interface ITenantRegistry
{
    /// <summary>Resolves a tenant, refreshing after the state-refresh interval; null when unknown or retired. Throws <see cref="TenantUnavailableException"/> when the catalog is unreachable beyond the staleness bound.</summary>
    ValueTask<TenantInfo?> TryResolveAsync(int tenantId, CancellationToken cancellationToken);

    /// <summary>Evicts a cached entry (the suspension hook calls this).</summary>
    void Invalidate(int tenantId);

    /// <summary>Lists tenants from the catalog (never from cache) — the migrator's fan-out and the admin contract.</summary>
    Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Opens connections and contexts against a tenant's database.</summary>
public interface ITenantConnectionFactory
{
    /// <summary>Opens a <see cref="SqlConnection"/> for the given tenant with the web app's profile.</summary>
    Task<SqlConnection> OpenAsync(TenantInfo tenant, CancellationToken cancellationToken);

    /// <summary>The pooled context factory for the tenant; one per tenant, cached alongside the registry entry.</summary>
    IDbContextFactory<TDbContext> ContextFactory<TDbContext>(TenantInfo tenant) where TDbContext : TellmaDbContext;
}

/// <summary>Opens a connection for the ambient tenant. Scoped; the batch executor's door to SQL.</summary>
public interface ITenantConnectionProvider
{
    /// <summary>The ambient tenant's resolved info.</summary>
    TenantInfo Tenant { get; }

    /// <summary>Opens a connection to the ambient tenant's database.</summary>
    Task<SqlConnection> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>The platform-internal writer of the ambient context. Scoped; two callers exist and both are platform code.</summary>
public interface IRequestContextHolder : IRequestContextAccessor
{
    /// <summary>Replaces the current context.</summary>
    void Set(RequestContext context);
}

/// <summary>A pluggable step that completes the request context after the tenant and principal are known; ordered ascending.</summary>
public interface IRequestContextInitializer
{
    /// <summary>The order; the platform's connect step runs at 100 and culture negotiation at 200.</summary>
    int Order { get; }

    /// <summary>Returns the enriched context, or throws a platform exception mapped to the response.</summary>
    ValueTask<RequestContext> InitializeAsync(RequestContext context, RequestContextInputs inputs, CancellationToken cancellationToken);
}

/// <summary>The raw request inputs negotiation may use, extracted by the web layer.</summary>
/// <param name="AcceptLanguage">The <c>Accept-Language</c> header value, or null.</param>
/// <param name="RequestedCulture">The platform culture header, or null.</param>
/// <param name="RequestedCalendar">The platform calendar header, or null.</param>
/// <param name="RequestedTimeZone">The platform time-zone header, or null.</param>
/// <param name="AuthenticationContext">The session's <c>acr</c>, its evidence time, and <c>auth_time</c>, for step-up decisions.</param>
public sealed record RequestContextInputs(
    string? AcceptLanguage, string? RequestedCulture, string? RequestedCalendar, string? RequestedTimeZone,
    AuthenticationContextClaims? AuthenticationContext);

/// <summary>Creates DI scopes for background work under an explicit context; the only way work runs for a tenant outside a request.</summary>
public interface ITenantScopeFactory
{
    /// <summary>Validates the tenant's state, creates the scope, sets the context, and starts a linked activity. Throws <see cref="TenantUnavailableException"/> for provisioning, suspended or retired tenants.</summary>
    ValueTask<TenantScope> CreateScopeAsync(RequestContext context, CancellationToken cancellationToken);
}

/// <summary>A background scope: the service provider plus the activity that spans it.</summary>
public sealed class TenantScope : IAsyncDisposable
{
    /// <summary>The scope's provider.</summary>
    public IServiceProvider Services { get; }

    /// <summary>The activity started for the scope, linked to the origin trace.</summary>
    public Activity? Activity { get; }

    /// <inheritdoc/>
    public ValueTask DisposeAsync();
}

/// <summary>The suspension hook: state transitions with immediate local effect and bounded fleet-wide effect.</summary>
public interface ITenantLifecycle
{
    /// <summary>Writes the new state and reason, records the actor, evicts the local cache entry, and notifies connection closers.</summary>
    Task SetStateAsync(int tenantId, TenantState state, string? reason, string actor, CancellationToken cancellationToken);
}

/// <summary>Starts the provisioning job for a tenant row in the <c>Provisioning</c> state. Distributions register the starter that fits their hosting.</summary>
public interface ITenantProvisioningTrigger
{
    /// <summary>Starts (or re-starts, idempotently) provisioning of the tenant.</summary>
    Task StartAsync(int tenantId, CancellationToken cancellationToken);
}

/// <summary>The tenant cannot be served right now (catalog unreachable beyond the staleness bound, or the tenant is not in a servable state).</summary>
public sealed class TenantUnavailableException(int tenantId, TenantState? state, string code)
    : InvalidOperationException($"Tenant {tenantId} is unavailable ({code}).")
{
    /// <summary>The tenant.</summary>
    public int TenantId { get; } = tenantId;
    /// <summary>The state, when known.</summary>
    public TenantState? State { get; } = state;
    /// <summary>The machine-readable reason (<c>host_unavailable</c>, <c>tenant_provisioning</c>, …).</summary>
    public string Code { get; } = code;
}

/// <summary>The scoped <see cref="ISandboxContext"/> every distribution gets from <c>AddTellma</c>.</summary>
internal sealed class TenantSandboxContext(IRequestContextAccessor accessor) : ISandboxContext
{
    /// <inheritdoc/>
    public bool IsSandbox => accessor.Current.IsSandbox;
}
```

```csharp
namespace Tellma.Core.Hosting;

/// <summary>The host catalog model; a distribution closes it with its tenant leaf and generates its migrations.</summary>
/// <typeparam name="TTenant">The tenant entity leaf (the default <see cref="Tenant"/> or a distribution extension).</typeparam>
public abstract class TellmaHostDbContext<TTenant>(DbContextOptions options) : TellmaHostDbContext(options)
    where TTenant : Tenant
{
    /// <summary>The tenants.</summary>
    public DbSet<TTenant> Tenants => Set<TTenant>();
}

/// <summary>The non-generic base the platform's services bind to.</summary>
public abstract class TellmaHostDbContext(DbContextOptions options) : DbContext(options)
{
    /// <summary>The schema every host table lives in.</summary>
    public const string Schema = "host";

    /// <summary>The membership hints.</summary>
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    /// <summary>The BFF sessions.</summary>
    public DbSet<Session> Sessions => Set<Session>();
}

/// <summary>A tenant row. Extendable by a distribution (columns only); the platform's columns are the contract.</summary>
public class Tenant
{
    /// <summary>The immutable id.</summary>
    public int Id { get; set; }
    /// <summary>The display name.</summary>
    [MaxLength(255)] public string Name { get; set; } = null!;
    /// <summary>Live or sandbox; immutable after provisioning.</summary>
    public TenantCategory Category { get; set; }
    /// <summary>For a sandbox, its live tenant.</summary>
    public int? LiveTenantId { get; set; }
    /// <summary>The lifecycle state.</summary>
    public TenantState State { get; set; }
    /// <summary>Why the state was set (operator text).</summary>
    [MaxLength(1024)] public string? StateReason { get; set; }
    /// <summary>When the state last changed.</summary>
    public DateTime StateChangedAt { get; set; }
    /// <summary>The database server host name.</summary>
    [MaxLength(255)] public string SqlServer { get; set; } = null!;
    /// <summary>The database name.</summary>
    [MaxLength(128)] public string SqlDatabase { get; set; } = null!;
    /// <summary>The configured credential profile name.</summary>
    [MaxLength(64)] public string SqlCredentialProfile { get; set; } = "default";
    /// <summary>Non-secret, distribution-defined JSON.</summary>
    public string? Properties { get; set; }
    /// <summary>Creation instant (UTC).</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Last modification instant (UTC).</summary>
    public DateTime ModifiedAt { get; set; }
    /// <summary>The subject or client id that last modified the row.</summary>
    [MaxLength(128)] public string? ModifiedBy { get; set; }
    /// <summary>Optimistic concurrency for operator edits.</summary>
    [Timestamp] public byte[] RowVersion { get; set; } = null!;
}

/// <summary>A membership hint row.</summary>
public sealed class TenantMembership
{
    /// <summary>The subject.</summary>
    [MaxLength(128)] public string Subject { get; set; } = null!;
    /// <summary>The tenant.</summary>
    public int TenantId { get; set; }
    /// <summary>The last known activity.</summary>
    public bool IsActive { get; set; }
    /// <summary>The last confirmation instant (UTC).</summary>
    public DateTime LastConfirmedAt { get; set; }
}

/// <summary>A BFF session row; the ticket is Data-Protection-encrypted because it holds tokens.</summary>
public sealed class Session
{
    /// <summary>The random session key carried by the cookie (base64url of 32 bytes).</summary>
    [MaxLength(64)] public string Key { get; set; } = null!;
    /// <summary>The subject.</summary>
    [MaxLength(128)] public string Subject { get; set; } = null!;
    /// <summary>The authority session id, for back-channel logout.</summary>
    [MaxLength(128)] public string? Sid { get; set; }
    /// <summary>The protected, serialized authentication ticket.</summary>
    public byte[] Ticket { get; set; } = null!;
    /// <summary>Creation instant (UTC).</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Last renewal instant (UTC).</summary>
    public DateTime RenewedAt { get; set; }
    /// <summary>Expiry instant (UTC); the sweep deletes past rows.</summary>
    public DateTime ExpiresAt { get; set; }
}
```

```csharp
namespace Tellma.Core.Migrations;

/// <summary>The migrator engine a distribution's migrator project drives from <c>Main</c>.</summary>
public static class TellmaMigratorHost
{
    /// <summary>Parses the command line, builds a generic host with the distribution's composition, and runs the command; returns the process exit code (0 converged, 1 usage, 2 partial failure).</summary>
    public static Task<int> RunAsync(string[] args, Action<HostApplicationBuilder> compose, CancellationToken cancellationToken = default);
}

/// <summary>The design-time factory base: builds the context from the same composition the hosts use.</summary>
public abstract class TellmaDesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext> where TContext : DbContext
{
    /// <summary>Composes the distribution into a throwaway host and resolves the context.</summary>
    protected abstract void Compose(HostApplicationBuilder builder);

    /// <inheritdoc/>
    public TContext CreateDbContext(string[] args);
}
```

### 3.3 `Tellma.Core.AspNetCore`

```csharp
namespace Tellma.Core.AspNetCore;

/// <summary>Composition of the web layer over an already-composed <c>Tellma.Core</c>.</summary>
public static class TellmaAspNetCoreServiceCollectionExtensions
{
    /// <summary>Registers the session cookie, the OIDC relying party, the bearer scheme, the policies, CSRF, Data Protection, the session store, the endpoint audit, and the health checks.</summary>
    public static IServiceCollection AddTellmaAspNetCore(this IServiceCollection services, IConfiguration configuration);

    /// <summary>Registers the forwarded-headers middleware with the checked configuration shape (refuses an enabled-but-empty proxy set and the framework's environment switch).</summary>
    public static IServiceCollection AddTellmaForwardedHeaders(this IServiceCollection services, IConfiguration configuration);
}

/// <summary>Middleware placement.</summary>
public static class TellmaApplicationBuilderExtensions
{
    /// <summary>Adds, in order: CSRF enforcement and the tenant middleware. Call after <c>UseAuthorization</c>.</summary>
    public static IApplicationBuilder UseTellma(this IApplicationBuilder app);
}

/// <summary>Endpoint mapping.</summary>
public static class TellmaEndpointRouteBuilderExtensions
{
    /// <summary>Maps the platform routes and returns the groups features and distributions map onto.</summary>
    public static TellmaEndpoints MapTellma(this IEndpointRouteBuilder endpoints);
}

/// <summary>The route groups of a distribution; every tenant group carries the tenant metadata, policy and JSON conventions.</summary>
public sealed class TellmaEndpoints
{
    /// <summary><c>/{tenantId:int:min(1)}/api/web</c>, policy <see cref="TellmaPolicies.Web"/>.</summary>
    public RouteGroupBuilder Web { get; }
    /// <summary><c>/{tenantId:int:min(1)}/api/v1</c>, policy <see cref="TellmaPolicies.Api"/>; a seam until the public surface ships.</summary>
    public RouteGroupBuilder Api { get; }
    /// <summary><c>/api</c> for deployable endpoints a distribution adds; each must call <see cref="AsDeployableEndpoint"/>.</summary>
    public RouteGroupBuilder Deployable { get; }

    /// <summary>Marks a deployable endpoint with the reason it needs no tenant, satisfying the audit.</summary>
    public static TBuilder AsDeployableEndpoint<TBuilder>(this TBuilder builder, string reason) where TBuilder : IEndpointConventionBuilder;

    /// <summary>Marks a tenant endpoint as mutating (or not); the projection sets it from the securable's action kind.</summary>
    public static TBuilder WithMutation<TBuilder>(this TBuilder builder, bool isMutation) where TBuilder : IEndpointConventionBuilder;
}

/// <summary>The policy names.</summary>
public static class TellmaPolicies
{
    /// <summary>Cookie session, authenticated.</summary>
    public const string Web = "Tellma.Web";
    /// <summary>Bearer, scope <c>tellma_api</c>, audience = origin.</summary>
    public const string Api = "Tellma.Api";
    /// <summary>Bearer, scope <c>tellma_api</c>, audience = the tenant's MCP resource.</summary>
    public const string Mcp = "Tellma.Mcp";
    /// <summary>Bearer, scope <c>tellma_control_plane</c>.</summary>
    public const string ControlPlane = "Tellma.ControlPlane";
}

/// <summary>Metadata on every tenant-scoped endpoint; its absence on a tenant route is an audit violation.</summary>
/// <param name="Surface">Which surface the endpoint belongs to.</param>
/// <param name="IsMutation">Whether the endpoint writes; read-only tenants refuse mutations.</param>
public sealed record TenantEndpointMetadata(TenantSurface Surface, bool IsMutation);

/// <summary>The tenant surfaces.</summary>
public enum TenantSurface
{
    /// <summary>The SPA's private API.</summary>
    Web,
    /// <summary>The versioned public API.</summary>
    Api,
    /// <summary>The MCP endpoint.</summary>
    Mcp,
    /// <summary>The real-time hub.</summary>
    Hub,
}

/// <summary>Metadata on every tenant-less endpoint, carrying the reason.</summary>
/// <param name="Reason">Why the endpoint belongs to the deployable.</param>
public sealed record DeployableEndpointMetadata(string Reason);

/// <summary>Writes the step-up challenge of the platform's identity model.</summary>
public static class StepUpChallenge
{
    /// <summary>Answers 401 with <c>WWW-Authenticate: Bearer error="insufficient_user_authentication"</c> and the requested constraints.</summary>
    public static void Write(HttpResponse response, string acrValues, int? maxAge);
}

/// <summary>The session store behind the cookie: rows in <c>host.Sessions</c>, a bounded per-instance cache, revocation by sid and subject.</summary>
public interface ITellmaSessionStore : ITicketStore
{
    /// <summary>Deletes every session of an authority session id and evicts it locally.</summary>
    Task RevokeBySidAsync(string sid, CancellationToken cancellationToken);

    /// <summary>Deletes every session of a subject and evicts them locally.</summary>
    Task RevokeBySubjectAsync(string subject, CancellationToken cancellationToken);

    /// <summary>Deletes expired rows; a background-task consumer.</summary>
    Task<int> SweepAsync(CancellationToken cancellationToken);
}

/// <summary>The meter and instrument names of the web layer.</summary>
public static class HostTelemetryNames
{
    /// <summary>The meter.</summary>
    public const string MeterName = "Tellma.Core.AspNetCore";
    /// <summary>Session store operations.</summary>
    public const string SessionOperations = "tellma.session.operations";
    /// <summary>Token refreshes by outcome.</summary>
    public const string SessionRefreshes = "tellma.session.refreshes";
    /// <summary>Back-channel logout deliveries by outcome.</summary>
    public const string BackchannelLogouts = "tellma.auth.backchannel_logouts";
    /// <summary>CSRF rejections by rule.</summary>
    public const string CsrfRejections = "tellma.auth.csrf_rejections";
}
```

Configuration (`Tellma` section; every key additive-only within a family major):

```json
{
  "Tellma": {
    "Host": {
      "ConnectionString": "Server=(localdb)\\MSSQLLocalDB;Database=Tellma.dev.<worktree>.acme;Integrated Security=true",
      "SqlCredentialProfiles": { "default": "Integrated Security=true", "migrator": "Integrated Security=true" },
      "ConnectionDefaults": "Encrypt=Mandatory;Connect Timeout=15;Max Pool Size=32;Min Pool Size=0",
      "ApplicationPrincipal": null,
      "TenantStateRefresh": "00:00:30",
      "TenantStateMaxStaleness": "00:05:00",
      "MembershipConfirmInterval": "1.00:00:00",
      "SeedTenants": [ { "Id": 1, "Name": "Acme", "Category": "Live", "SqlServer": "(localdb)\\MSSQLLocalDB", "SqlDatabase": "Tellma.dev.<worktree>.acme" } ]
    },
    "Authentication": {
      "Authority": null,
      "ClientId": "acme",
      "ClientSecret": null,
      "SessionIdleLifetime": "7.00:00:00",
      "SessionAbsoluteLifetime": "90.00:00:00",
      "RevocationLatency": "00:01:00",
      "MaxAuthorityOutage": "01:00:00"
    },
    "DataProtection": { "BlobUri": null, "KeyId": null, "KeyRingPath": null },
    "Provisioning": { "Trigger": "local" }
  },
  "TellmaIdentity": { "Mode": "InProc", "PathBase": "/id", "Seed": { "DevAdmin": { "Enabled": true } } }
}
```

### 3.4 The distribution's host, end to end

```csharp
// distributions/acme/src/Tellma.Distro.Acme.Web/Program.cs
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog(static (context, services, logger) => logger.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services));

builder.Services.AddAcme(builder.Configuration);                    // AddTellma<AcmeDbContext, AcmeHostDbContext>("acme", …)
builder.Services.AddTellmaAspNetCore(builder.Configuration);        // BFF, bearer, policies, CSRF, DP, audit, health
builder.Services.AddTellmaForwardedHeaders(builder.Configuration);
builder.Services.AddTellmaIdentityInProc(builder.Configuration);    // only when TellmaIdentity:Mode == InProc: AddTellmaIdentity + marks its endpoints deployable
builder.Services.AddTellmaEmail();
builder.Services.AddSmtpEmail(builder.Configuration);
builder.Services.AddTellmaWebhooks();
builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter(TenancyTelemetryNames.MeterName).AddMeter(HostTelemetryNames.MeterName)…);

WebApplication app = builder.Build();
app.UseForwardedHeaders();               // no-op unless enabled
app.UseSerilogRequestLogging();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler(); app.UseHsts(); app.UseHttpsRedirection(); }
app.UseRouting();
app.UseTellmaIdentity();                 // in-proc only
app.UseAuthentication();
app.UseAuthorization();
app.UseTellma();                         // CSRF + tenant middleware
app.MapStaticAssets();
TellmaEndpoints tellma = app.MapTellma();
tellma.Web.MapPost("centers/rebalance", RebalanceCenters.HandleAsync).WithMutation(true);   // a custom endpoint inherits everything
app.MapTellmaIdentity();                 // in-proc only
app.MapTellmaWebhooks();
app.Run();
```

```csharp
// distributions/acme/src/Tellma.Distro.Acme.Migrator/Program.cs
return await TellmaMigratorHost.RunAsync(args, host => host.Services.AddAcme(host.Configuration));
```

### 3.5 Contracts this theme needs from other themes

| Seam | Needed shape |
|---|---|
| T4 connect step | `sealed class UserConnectInitializer : IRequestContextInitializer` (Order 100): resolves `Subject`/`ClientId` to `UserId`, stamps activity, reads tags, throws `TellmaNotFoundException("tenant_not_found")` for non-members and `TellmaForbiddenException("user_inactive")` for inactive users; exposes `RequiredAcr`/`MaxAge` for D12 through the returned context's `RequestContextInputs`. |
| T3 negotiation | `sealed class CultureNegotiationInitializer : IRequestContextInitializer` (Order 200): fills `Language`, `Culture`, `Calendar`, `TimeZone`, `TenantTimeZone`, `Today` from the inputs, the user's preferences and the tenant settings. |
| T5 exceptions (seam 10) | `TellmaNotFoundException(code)`, `TellmaForbiddenException(code)`, `TellmaUnavailableException(code, retryAfter)` — this theme throws the first three outcomes of D7 through them and T6 maps them. |
| T6 projection | Projects stacks onto `TellmaEndpoints.Web` with `TenantEndpointMetadata(Web, isMutation)` stamped from the securable's action kind; maps `/{tenantId:int}/mcp` when the MCP feature is on; reads the platform headers into `RequestContextInputs`. |
| T2 executor | Opens connections only through `ITenantConnectionProvider` (request) or `ITenantConnectionFactory` (explicit tenant, migrator and reconciler only). |
| T10 runner | Calls `ITenantScopeFactory.CreateScopeAsync(storedContext)`; implements `ITenantConnectionCloser.CloseAsync(tenantId)` that D7 invokes; hosts the session sweep, the membership reconciler, and the provisioning-trigger poll. |
| T8 `UserService` | Calls `ITenantMembershipDirectory.RecordAsync` after commit on invite/activate/deactivate. |

---

## 4. Schema

All in the **host database**, schema `host`. UTC `datetime2(7)` throughout (host tables carry no
tenant time zone). Enum columns follow the platform's enum-as-string convention (T2) with a check
constraint. No IDENTITY; the tenant sequence self-heals past explicit ids.

```sql
CREATE SEQUENCE [host].[sq_Tenants] AS int START WITH 1 INCREMENT BY 1;

CREATE TABLE [host].[Tenants] (
    [Id]                   int            NOT NULL CONSTRAINT [PK_Tenants] PRIMARY KEY CLUSTERED,
    [Name]                 nvarchar(255)  NOT NULL,
    [Category]             varchar(16)    NOT NULL CONSTRAINT [CK_Tenants_Category] CHECK ([Category] IN ('Live', 'Sandbox')),
    [LiveTenantId]         int            NULL     CONSTRAINT [FK_Tenants_LiveTenant] REFERENCES [host].[Tenants]([Id]),
    [State]                varchar(16)    NOT NULL CONSTRAINT [CK_Tenants_State] CHECK ([State] IN ('Provisioning', 'Active', 'ReadOnly', 'Suspended', 'Retired')),
    [StateReason]          nvarchar(1024) NULL,
    [StateChangedAt]       datetime2(7)   NOT NULL,
    [SqlServer]            nvarchar(255)  NOT NULL,
    [SqlDatabase]          nvarchar(128)  NOT NULL,
    [SqlCredentialProfile] varchar(64)    NOT NULL CONSTRAINT [DF_Tenants_SqlCredentialProfile] DEFAULT ('default'),
    [Properties]           nvarchar(max)  NULL,
    [CreatedAt]            datetime2(7)   NOT NULL,
    [ModifiedAt]           datetime2(7)   NOT NULL,
    [ModifiedBy]           nvarchar(128)  NULL,
    [RowVersion]           rowversion     NOT NULL,
    CONSTRAINT [CK_Tenants_SandboxHasLive] CHECK (([Category] = 'Sandbox') = ([LiveTenantId] IS NOT NULL)),
    CONSTRAINT [CK_Tenants_LocationChars] CHECK ([SqlServer] NOT LIKE '%[;=]%' AND [SqlDatabase] NOT LIKE '%[;=]%'),
    CONSTRAINT [UQ_Tenants_Location] UNIQUE ([SqlServer], [SqlDatabase])
);
CREATE INDEX [IX_Tenants_LiveTenantId] ON [host].[Tenants]([LiveTenantId]) WHERE [LiveTenantId] IS NOT NULL;
CREATE INDEX [IX_Tenants_State] ON [host].[Tenants]([State]);
```

`UQ_Tenants_Location` is what makes co-location of the host schema with a single live tenant safe:
two rows can never claim one database. A distribution extension adds nullable or defaulted columns
only (expand-only rule).

```sql
CREATE TABLE [host].[TenantMemberships] (
    [Subject]         nvarchar(128) NOT NULL,
    [TenantId]        int           NOT NULL CONSTRAINT [FK_TenantMemberships_Tenant] REFERENCES [host].[Tenants]([Id]),
    [IsActive]        bit           NOT NULL,
    [LastConfirmedAt] datetime2(7)  NOT NULL,
    CONSTRAINT [PK_TenantMemberships] PRIMARY KEY CLUSTERED ([Subject], [TenantId])
);
CREATE INDEX [IX_TenantMemberships_TenantId] ON [host].[TenantMemberships]([TenantId]);
```

```sql
CREATE TABLE [host].[Sessions] (
    [Key]       varchar(64)    NOT NULL CONSTRAINT [PK_Sessions] PRIMARY KEY CLUSTERED,
    [Subject]   nvarchar(128)  NOT NULL,
    [Sid]       nvarchar(128)  NULL,
    [Ticket]    varbinary(max) NOT NULL,
    [CreatedAt] datetime2(7)   NOT NULL,
    [RenewedAt] datetime2(7)   NOT NULL,
    [ExpiresAt] datetime2(7)   NOT NULL
);
CREATE INDEX [IX_Sessions_Sid]       ON [host].[Sessions]([Sid]) WHERE [Sid] IS NOT NULL;
CREATE INDEX [IX_Sessions_Subject]   ON [host].[Sessions]([Subject]);
CREATE INDEX [IX_Sessions_ExpiresAt] ON [host].[Sessions]([ExpiresAt]);
```

`Key` is the base64url form of 32 CSPRNG bytes (43 characters), compared ordinally (`Latin1_General_BIN2`
collation on the column). `Ticket` is the `TicketSerializer` output protected by Data Protection;
nothing in the row is readable without the key ring. The sweep deletes `ExpiresAt < now` in batches
of 1,000.

```sql
-- host migrations history (own table, so a co-located tenant schema keeps its own)
CREATE TABLE [host].[__EFMigrationsHistory] (
    [MigrationId]    nvarchar(150) NOT NULL CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY,
    [ProductVersion] nvarchar(32)  NOT NULL
);
```

In **every tenant database**, applied by the migrator outside the migrations (idempotent,
recomputed from the model every run):

```sql
IF DATABASE_PRINCIPAL_ID('tellma_app') IS NULL CREATE ROLE [tellma_app];
-- for every schema S the tenant model maps:
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[S] TO [tellma_app];
-- for every sequence sq_<Table> in the model:
GRANT UPDATE ON OBJECT::[S].[sq_<Table>] TO [tellma_app];
-- table types: emitted by the migrations themselves through HasGrants('tellma_app') (spec 0001).
-- provisioning only, SaaS:
CREATE USER [<web app identity>] FROM EXTERNAL PROVIDER;  ALTER ROLE [tellma_app] ADD MEMBER [<web app identity>];
-- provisioning only, on-prem:
CREATE USER [tellma_app_user] FOR LOGIN [tellma_app_login];  ALTER ROLE [tellma_app] ADD MEMBER [tellma_app_user];
-- provisioning only, fresh database:
ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON;
```

---

## 5. Answers

| Brain-dump / briefing question | Answer | Decision |
|---|---|---|
| "Are those the proper layer names in a .NET business app?" | Entities, services, endpoints — folders named after contents; "Data/Service/Web" stays prose. | D1 |
| Where does the reference distribution live; slug; reserved-slug implications; project set | `distributions/acme/` in distribution-repo shape; slug `acme` (taken by the platform, not reserved); `Tellma.Distro.Acme.Web` + `.Migrator` + two test projects. | D1, D2 |
| `AddTellma` at minimal fidelity; module package and stack feature on one shape | Declare/contribute with data-only items realised by Core; `Requires` only; graph gate in `AddTellma`, realized gate at startup; modules and the distribution contribute `StackContributionItem`s. | D3 |
| `{tenantId}` shape | Immutable positive `int`, tenant-first prefix `/{tenantId:int:min(1)}`; platform routes never start with a digit. | D4 |
| "Is the TenantRegistry the right shape?" | No: it stored connection strings. Replaced by a read-model registry over the `host` catalog returning descriptor + composed connection string; writes go through `ITenantLifecycle` and the migrator. | D5–D7 |
| Catalog in config vs live DB vs dedicated DB; ARCHITECTURE's Catalog-DB assumption | Always the `host` schema of one host database; single-live co-locates it with the live database, multi-live dedicates one; configuration only seeds. | D5 |
| Per-tenant connection resolution and caching | Location + credential profile composed at resolution; one pooled context factory and one SqlClient pool per active tenant, `Max Pool Size=32`; 30 s state refresh, 5 min maximum staleness, then fail closed. | D6, D7 |
| Secrets policy | No secret in any table; no per-tenant passwords; managed identity as contained user in SaaS; one login on-prem; Key Vault references / environment / host-injected files for configuration secrets; startup rejects unresolved references and repo-tracked secrets; no DPAPI. | D6 |
| "Should we extend the TenantRegistry to blob storage / Key Vault?" | No; locations derive from the tenant id, one `TokenCredential` reaches every store, secret *names* may live in `Properties`. | §1.2, D6 |
| "Should we extend the TenantRegistry to support provisioning?" | No; provisioning is a migrator command with DDL rights, the web app inserts a `Provisioning` row and triggers the job through `ITenantProvisioningTrigger`. | D14 |
| Membership lookup | `host.TenantMemberships` hints, written on tenant-side transitions, confirmed on use, reconciled nightly; never an authorization source. | D8 |
| `ISandboxContext` implementation | `TenantSandboxContext` over `IRequestContextAccessor`; deployable work is never sandboxed; the audit and the scope factory are the invariant. | D9 |
| Request context abstractions and how they reach services and background scopes | `RequestContext` record, `IRequestContextAccessor`, scoped holder with two writers (`TenantMiddleware`, `ITenantScopeFactory`); no `AsyncLocal`. | D9 |
| "X-Today header?" (web section) | No; `today()` is the tenant-zone date computed once per request; the user's time zone is a separate context value used for rendering. | D9 |
| OIDC relying party with BFF cookie; CSRF; session revocation on deactivation | Server-side session store, refresh-anchored liveness, back-channel receiver, per-surface schemes, audit; header + origin CSRF; tenant deactivation is a per-request 403 plus connection close, distribution-wide removal revokes by subject. | D10, D11 |
| Suspension hook | `ITenantLifecycle.SetStateAsync`; immediate locally, ≤ 30 s fleet-wide; state table of behaviours. | D7 |
| "One MCP server per tenant or per distro?" | Per tenant at `/{tenantId}/mcp`, one code path, per-tenant audience; identity server must add resource registration and CIMD. | D13 |
| What provisioning leaves as a seam | The trigger, sandbox cloning, distro-specific per-tenant settings (as `Tenant` columns or non-secret `Properties`). | D14 |
| `/api/distribution-info` and the admin contract | Anonymous info document without tenant list; admin surface under `/api/admin/*` with the control-plane policy; health endpoints. | D15 |

---

## 6. Seams

1. **Batch abstraction (T2).** Position: the executor opens connections through
   `ITenantConnectionProvider` and never receives a connection string; a batch is bound to one
   tenant for its lifetime. No contract needed beyond D6's two interfaces.
6. **Feature composition (owner).** Contract in §3.1: `ITellmaFeature`, `FeatureDeclaration`,
   `FeatureContribution`, `FeatureContributionItem` and the realizer seam. T5 adds
   `StackContributionItem`, T4 `SecurableContributionItem`, T8 `SeedContributionItem`, T2 uses
   `ModelContributionItem`; each ships its realizer in `Tellma.Core`. Every item type is a record in
   `Tellma.Core.Abstractions`.
9. **Request context (owner).** Contract in §3.1/§3.2. T3 and T4 are `IRequestContextInitializer`s;
   T10 is the only caller of `ITenantScopeFactory`; T6 extracts `RequestContextInputs` from headers
   (`Accept-Language`, and the platform headers whose names T6 fixes — proposed
   `Tellma-Culture`, `Tellma-Calendar`, `Tellma-Time-Zone`, matching the `Tellma-Client` CSRF header's
   family).
10. **Platform exceptions (T5/T6).** Needed: `TellmaNotFoundException(code)`,
    `TellmaForbiddenException(code)`, `TellmaUnavailableException(code, retryAfter)`, each carrying a
    machine code the response body echoes (`tenant_not_found`, `tenant_suspended`, `tenant_read_only`,
    `tenant_provisioning`, `host_unavailable`, `user_inactive`, `csrf_rejected`).
11. **Permission evaluation (T4).** Consumed indirectly: the connect initializer is T4's; this theme
    guarantees it runs before any tenant endpoint and never for deployable ones.
14. **Telemetry names.** D17; instruments in the two meters named after their packages.
16. **Connect-call collapse (T4/T5).** Position: the tenant middleware touches no tenant database;
    whatever T4/T5 fold into the first business round trip is invisible to this theme, provided the
    optimistic path still produces `403 user_inactive` / `404 tenant_not_found` before any row is
    returned.
17. **Vocabulary.** Plural table names (`host.Tenants`, matching `gl.Invoices`); lowercase schema
    names (`host`, `core`, `gl`, `idsvr`); `int` ids; `CreatedAt/ModifiedAt/ModifiedBy` on host rows
    (no tenant user to reference, so `ModifiedBy` is a subject or client id, not an FK).

---

## 7. Departures from ARCHITECTURE.md

| ARCHITECTURE.md says | This design | Why |
|---|---|---|
| "its own Catalog DB listing tenants … reuses `Tellma.Core`'s sharding code unchanged" | One logical host catalog per distribution, physically co-located with the live database in single-live shape; written from scratch. | No such code exists; single-live distributions should not pay for a second database by default. |
| `samples/tellma-sample-distribution/` (layout) vs `distributions/<slug>/` (phasing) | `distributions/acme/` in distribution-repo shape. | One location; graduation is a folder move. |
| `Tellma.Core` provides the CRUD stack base including generated endpoints | New `Tellma.Core.AspNetCore` owns everything with an ASP.NET dependency; `Tellma.Core` stays host-agnostic. | The migrator and workers compose Core without the web stack; matches the Webhooks precedent. |
| Feature edges `Requires`/`Recommends`/`Excludes` + cardinality | `Requires` only; the others reserved. | Minimal fidelity per the breakdown; additive later. |
| "Endpoints are generated … read → GET, save → POST, delete → DELETE" | Not decided here; the tenant group is verb-agnostic. T6 decides and updates the document. | Out of theme. |
| A distribution on the shared authority references none of the identity projects | The reference distribution references the engine and selects in-proc by configuration. | Local development with no shared services (Guiding Principles) and spec 0003 §10.4. |
| Reserved-slug list | Add `acme` as taken. | D2. |
| "Permissions model under ad-hoc SQL" (open question) | Answered: `tellma_app` role, grants recomputed from the model after every migrate. | D6. |
| Migrator: `--all-tenants` / `--tenant <id>` | `migrate host`, `migrate tenants [--tenant]`, `provision tenant`, `status`. | Host schema and provisioning need their own commands. |

---

## 8. Verification

Relied on from `research/host-tenancy.md` (verified 2026-09-01 by the researcher): PAR
`PushedAuthorizationBehavior.Require` and the OIDC handler's .NET 9/10 state (§1.1–1.2); no
first-party BFF, no built-in token refresh, Duende licensing (§1.3–1.4); RFC 10017 cookie and CSRF
requirements (§1.5); .NET 10 antiforgery not enforcing on JSON endpoints and the cookie-to-header
pattern (§1.6); `SameSite` defaults and the spec 0003 `Lax` choice (§1.7); `ITicketStore` members
and the `ValidatePrincipal` per-request cost (§2); Finbuckle 10.1.3 facts and the roll-our-own
recommendation (§3.1); pooled-context and per-connection-string pooling facts (§3.2); Key Vault
reference syntax, 24-hour staleness and the unresolved-reference failure mode (§4.1);
`Azure.Extensions.AspNetCore.Configuration.Secrets` 1.5.2 (§4.2); managed-identity SqlClient modes,
`CREATE USER … FROM EXTERNAL PROVIDER`, SqlClient 7.0's Azure split (§4.3); the shard-map
catalog-without-credentials pattern (§4.4); DPAPI Windows-only and on-prem shapes (§4.5); elastic
pool limits, 30,000 sessions, logins-per-pool (§5.1–5.2); no cross-database queries or `USE` on
Azure SQL (§5.3); `AsyncLocal`/`HttpContextAccessor`/`Activity.Current` semantics and the framework
guidance against capturing request state in background work (§6); route-group parameter capture,
filter ordering and `IApiEndpointMetadata` (§7).

Relied on from the sibling research files: MCP 2026-07-28 authorization (PRM path insertion, RFC 8707
`resource`, CIMD preference, `code_challenge_methods_supported`), the C# SDK 2.2.0 hosting model and
`MapMcp` with route parameters, OpenIddict 7.6.1's lack of PRM/DCR/CIMD and static `RegisterResources`,
and each client's registration requirements (`web-api-mcp.md` §3.4–3.5, §4, §5); fallback-policy
semantics, `AllowAnonymous` being absolute, `IAuthorizationRequirementData`, and the
startup-audit technique (`users-roles-permissions.md` §2); the invite/delivery-status APIs as
implemented and the `sub` format (`users-roles-permissions.md` §5); `-u-ca-` culture traps, no
standard time-zone header, GitHub's `Time-Zone` precedent, IANA ids on .NET 10
(`settings-cache-l10n.md` §3.3–3.4, §5); host shutdown budgets and App Service Always On
(`background-inbox.md` §2); span links at creation (`background-inbox.md` §4.1); RCSI defaults and
the single-connection requirement to enable it, SqlClient 6.1.6 / 7.0.2 (`data-access.md` §3.1, §8).

Verified by me on 2026-09-01:

- **OIDC Back-Channel Logout 1.0** (https://openid.net/specs/openid-connect-backchannel-1_0.html):
  the logout token requires `iss`, `aud`, `iat`, `exp`, `jti`, `events`; "A Logout Token MUST contain
  either a sub or a sid Claim, and MAY contain both"; "A nonce Claim MUST NOT be present"; the RP
  answers `200` on success and `400` on any validation failure. The identity engine's
  `LogoutTokenFactory` emits `sub`, `sid`, `jti`, `events`, type `logout+jwt`, two-minute lifetime,
  signed with the asymmetric signing credential — D10's receiver validates exactly that.
- **`Microsoft.AspNetCore.Authentication.OpenIdConnect`** 10.0.11 is the current 10.0.x line
  (released 2026-08-11; search-result snippet of the NuGet listing, the package page itself was not
  fetched). The repo pins other ASP.NET packages at 10.0.9; the OIDC and JwtBearer packages need
  pins in `Directory.Packages.props` (10.0.9 for consistency with the framework band, or 10.0.11
  with the rest of the `Microsoft.Extensions.*` line — an implementation-time choice).
- Repo facts read directly: `ClientDescriptorFactory.Distribution` registers exactly
  `<origin>/signin-oidc` and `<origin>/signout-callback-oidc`, grants `authorization_code`,
  `refresh_token`, PAR, `tellma_api` scope and `resource:<origin>`, and stores
  `tellma:backchannel_logout_uri`; the control-plane client is deliberately never granted a
  distribution origin (`TellmaClientProperties.CallsDistributionApis` doc); `TellmaIdentityOptions`
  supports in-proc mode with `PathBase`, `ConfigureDbContext`, and a dev admin with fixed subject
  `00000000-0000-0000-0000-000000000001`; `WebhookEndpointMetadata` exists for a tenant middleware to
  skip; the in-proc test host and the migrations-host test asset are the prototypes of the two
  distribution projects.

Not verified (implementation must confirm):

- Whether the MCP C# SDK's `AddMcp` can serve a per-route PRM document; D13 serves the document from
  a platform endpoint and writes the challenge itself, so the design does not depend on it.
- That an Azure managed identity which created a database (and therefore owns it) may run
  `CREATE USER … FROM EXTERNAL PROVIDER` in it without being the server's Entra admin; the research
  says an Entra principal with sufficient rights is needed, and ownership should suffice, but this
  is a provisioning-time check to make in the first Azure deployment.
- The exact cookie-handler renewal cadence with a session store (documented as "past half of the
  sliding window"); the session sweep's batch size and the `RenewedAt` write frequency follow from it.
- Whether `dotnet ef` design-time discovery copes with two contexts composed through one
  `AddTellma` call when the migrator names `--context`; the explicit `IDesignTimeDbContextFactory`
  per context in D1 avoids relying on host discovery.
- The `Sec-Fetch-Mode: navigate` exemption in D11 assumes every top-level navigation into the
  platform lands on `/bff/login` or a callback; the SPA fallback route is anonymous and unaffected.
