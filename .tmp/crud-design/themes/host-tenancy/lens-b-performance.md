# Distribution host and multi-tenancy — design proposal (Tier-2 performance and operations)

Theme key `host-tenancy`, future spec `0010-distribution-host-and-multitenancy.md`. Written 2026-09-01
against the brain dump, the ten-spec breakdown, ARCHITECTURE.md, specs 0001/0003/0007/0008, the code in
`src/core/Tellma.Core.Abstractions`, `src/apps/Tellma.Identity`, and the research file
`research/host-tenancy.md` (plus the MCP, permissions, settings and background-work research files where
this theme's questions touch them).

The optimisation target throughout: the per-request cost of "being a multi-tenant distribution" must be
zero database calls and zero network calls in the common case; every cross-instance concern (catalog
changes, session revocation, token refresh) must be bounded in latency and bounded in cost; every pool,
cache and background loop must be sized and observable from day one.

---

## 1. Critique

**General design.** The brain dump treats multi-tenancy as a connection-string lookup ("a core service
resolves the connection string of the tenant's database") and treats distribution-side authentication as
one sentence in the web layer. Both are the tip of an operational iceberg that the identity spec and the
Azure hosting model already impose on every distribution: a server-side session that can be killed by a
back-channel logout, a token refresh every ten minutes per session that is also the policy re-evaluation
point, a Data Protection key ring shared across instances so a cookie issued by one instance decrypts on
another, per-tenant connection pools that multiply by instance count against a 30,000-session pool limit,
a suspension state that must take effect on every instance without a central bus, and a tenant catalog
that hundreds of background loops read. None of that is in the draft, and it is where the round trips,
the locks and the stale-cache bugs actually live.

**Detailed choices that do not survive the performance and operations lens.**

1. *Catalog table inside the live database for single-live distributions.* This makes the live tenant's
   backup carry the list of sandboxes (a restore silently un-registers every sandbox created after the
   backup), puts distribution-level rows in a tenant database (the tenant's own export, schema-drift
   check and dacpac comparison now see foreign tables), and forces two code paths (live-DB catalog
   versus dedicated-DB catalog) into the hottest lookup in the system. It also leaves distribution-level
   state with no home at all in the single-live shape: the BFF session store, the in-proc identity
   store (spec 0003 puts it in "the distribution's own database" — which one, when there are five
   sandboxes?), the membership mirror, and the migrator's fan-out list. One catalog database per
   distribution, always, is the only shape with one code path; the single-live/multi-live distinction is
   a registration policy, not a storage topology.
2. *Membership by fan-out for single-live distributions.* "Which tenants am I a member of" as N round
   trips (one per sandbox, each opening a pooled connection into a different database) is exactly the
   N+1 the guiding principles forbid, and it grows with every sandbox a customer clones. A mirror table in
   the catalog answers it in one primary-key-range read for both shapes.
3. *`TenantRegistry { ResolveConnectionString, RegisterConnectionString, CanRegisterConnectionString }`.*
   Three unrelated things are folded into one type: the hot read path (millions of calls per day, must
   be an in-memory dictionary lookup), the rare write path (a handful of calls per month, needs an audit
   trail and a cross-instance invalidation), and connection *strings* as registry data. Connection
   strings are not data: in SaaS every tenant database is reached with the same managed identity and a
   string that contains no secret, and on-prem every tenant is reached with the same login. The registry
   holds *locations* (server, database); the string is composed from a configured credential profile.
4. *"Keys and secrets are never stored in the DB in clear text."* The right instinct with the wrong
   consequence — it invites an encrypted-secrets column. The correct rule is that no tenant-level secret
   exists: Azure SQL is reached with the App Service's managed identity through a contained
   `CREATE USER … FROM EXTERNAL PROVIDER` in every tenant database; on-prem, one login whose password
   arrives from the host (environment variable or a root-owned secret file), never from configuration
   files and never from any table. There is nothing to encrypt.
5. *`{tenantId}` shape unspecified.* A slug per tenant means renames, uniqueness across a distribution,
   and a string key in every cache, log scope and dictionary. An `int` route value with an `:int`
   constraint costs one integer parse, indexes the registry snapshot directly, and matches the
   `TenantId` the control plane's telemetry path already expects.
6. *Blob storage and Key Vault in the registry.* Wrong layer: blob scoping is `<container per
   distribution>/<tenantId>/…` from configuration plus the ambient tenant id (spec 0016), and Key Vault
   is a configuration provider, not a per-tenant fact. The registry stays about databases.
7. *MCP topology framed as a user-friction question.* The performance question is context size and
   isolation: a per-distribution server would need a `tenantId` argument on every tool (and a tool to
   list tenants), which bloats every tool schema and puts tenant selection inside the model's reasoning;
   a per-tenant endpoint makes the tenant a fact of the URL, exactly as the web surface does, and lets
   the token's audience be the endpoint. Multi-tenant agents configure two servers; that is how every
   MCP client is designed to work.
8. *"For background tasks, do we need shared coordination state in the catalog DB?"* No, and the catalog
   must not become one: a cross-tenant table written by every job on every instance turns the one
   database every request depends on into a hot spot. Coordination is per tenant database (spec 0019's
   lease columns); the catalog is read-mostly by construction.
9. *Missing entirely:* how the request context reaches a background scope (the research shows an
   `AsyncLocal` design leaks a stale tenant into fire-and-forget work), the `ISandboxContext`
   implementation the email spec already demands from every distribution, health and readiness
   endpoints for slot swaps, `Always On`, the App Service Linux 5-second drain default, the
   `Data Protection` ring, pool sizing (`Max Pool Size` 100 per tenant per instance is the default and
   is wrong for hundreds of tenants), cross-instance cache invalidation for tenant state, the CSRF posture
   of a cookie-authenticated JSON API, the back-channel logout endpoint spec 0003 registers for every
   distribution, the `acr`/`auth_time` re-check the same spec requires per request, and the SignalR
   connection close on session end.
10. *Layer names.* "Data / Service / Web" is fine as a mental model; the folder names that carry weight
    in a .NET distribution are the artefacts a coding agent adds: `Entities/`, `Services/`,
    `Endpoints/` — see D2.

**Internal inconsistencies.** "Background tasks are isolated by tenant" versus "shared coordination state
in the catalog"; "one distro per customer" (single-live) versus "sandboxes can be created" (which is
programmatic registration, so `CanRegisterConnectionString` cannot be false even in single-live mode —
only *live* registration is policy-gated); "the identity server seeds `admin@localhost` and the
distribution seeds a matching tenant admin" (spec 0003) with no statement of which database the in-proc
identity engine uses in a multi-tenant distribution.

**Misalignments with ARCHITECTURE.md the draft inherits.** The layout tree says
`samples/tellma-sample-distribution/`, the phasing text says `distributions/<slug>/`; "reuses
`Tellma.Core`'s sharding code unchanged" — no such code exists in this repo (and it must not mean the
Elastic Database client library); the feature-composition section describes a manifest generator, slots,
providers and a bypass analyzer that the first release does not need.

---

## 2. Decisions

### D1 — The reference distribution lives at `distributions/acme/` and its slug is `acme`

**Decision.** The reference distribution is a real distribution in every sense (its own OIDC client,
its own `DeploymentIdentity`, deployable to `acme.app.tellma.com` as the smoke deployment). It lives at
`distributions/acme/` in the platform repo (Phase 1), with this layout:

```
distributions/acme/
├── src/
│   ├── Tellma.Distro.Acme.Web/                # ASP.NET host; references Tellma.Core, Tellma.Core.AspNetCore,
│   │   ├── Program.cs                         #   Tellma.Core.EntityFrameworkCore, Tellma.Module.Gl (+ .Abstractions)
│   │   ├── AcmeComposition.cs                 # the single AddTellma declaration shared with the migrator
│   │   ├── AcmeDbContext.cs                   # the tenant DbContext; OnModelCreating applies the feature registry
│   │   ├── Entities/                          # sealed leaves / distro-only entities
│   │   ├── Services/                          # distro business logic (custom validators, side effects)
│   │   ├── Endpoints/                         # distro-authored Minimal API endpoints joining the tenant group
│   │   ├── Properties/launchSettings.template.json
│   │   ├── appsettings.json / appsettings.Development.json
│   │   └── wwwroot/                           # the SPA shell (added with the UI phase)
│   └── Tellma.Distro.Acme.Migrator/           # console; references Web + Tellma.Core.Migrator + Tellma.Core.Migrations
│       ├── Program.cs                         #   + Tellma.Core.EntityFrameworkCore.Design + Microsoft.EntityFrameworkCore.Design
│       ├── AcmeDbContextFactory.cs            # IDesignTimeDbContextFactory<AcmeDbContext> over AcmeComposition
│       └── Migrations/                        # AcmeDbContext migrations + snapshot (tenant schema)
└── test/
    ├── Tellma.Distro.Acme.Web.Tests/          # composition validation, endpoint audit, options
    ├── Tellma.Distro.Acme.IntegrationTests/   # WebApplicationFactory + Testcontainers; in-proc identity; Category=Integration
    └── Tellma.Distro.Acme.E2E/                # Playwright, added with the UI phase
```

`samples/tellma-sample-distribution/` is removed from the ARCHITECTURE.md layout tree; `acme` is added
to the reserved-slug list so no customer distribution can take the name a first-party host answers at.

**Rationale.** A distribution that is not deployable is not a reference; every operational decision
below (managed identity, session store, slot-swap readiness) has to be exercised somewhere before a
customer distribution exists. `acme` is the conventional placeholder company, satisfies the slug
constraints (starts with a letter, 4 characters, lowercase), and is already the name the identity
in-proc test host uses for its `DeploymentIdentity`.

**Rejected.** `samples/…` (a sample is not deployed and drifts); `reference`/`ref` as the slug (generic
words invite later reservation conflicts and read badly in `acme.app.tellma.com`-style hostnames);
distributions inside `src/apps/` (they are not platform apps and must never be referenced by platform
code — a separate top-level folder makes the one-way dependency mechanical to test).

**Confidence.** High on location and project set; medium on the slug. **Review flag:** `acme` versus a
Tellma-owned real name (for example the customer-zero distribution `banan`) as the first reference.

### D2 — Layer names are folders in one Web project: `Entities/`, `Services/`, `Endpoints/`

**Decision.** The distribution's Web project keeps the three layers as folders, named after the
artefacts an agent adds rather than after architectural layers: `Entities/` (entity classes and
`IEntityTypeConfiguration<T>` classes — the data layer), `Services/` (application services, validators,
side effects — the service layer), `Endpoints/` (hand-written Minimal API endpoints — the web layer).
Platform documentation uses "data layer", "service layer" and "web layer" as the names of the three
platform tiers; distribution code never needs those words because the platform owns the tiers.

**Rationale.** The names a coding agent sees in the tree are the names it will imitate; "Domain",
"Application", "Infrastructure" (Clean Architecture) invite the agent to create interfaces and
repositories the platform has already decided against. One project keeps the distribution buildable
without project-reference ceremony; the migrator is the only second project because it must carry the
Design-time packages the web publish output must not contain.

**Rejected.** Per-layer projects in the distribution (three csproj files and cross-references for a
few dozen files); a `Features/` folder per entity (attractive for locality, but the platform's feature
composition projects most of a feature from the entity, so a per-feature folder would hold one or two
files).

**Confidence.** High.

### D3 — Package placement: `Tellma.Core.AspNetCore`, `Tellma.Core.Migrator`, `Tellma.Core.Migrations`

**Decision.** Three new Core-layer packages, and one new dependency edge:

| Package | Contents | References |
|---|---|---|
| `Tellma.Core` (existing, grows) | `AddTellma` composition root; the tenant registry, catalog, connection factory and per-tenant `DbContext` factory; the operation-context holder; `CatalogDbContext` and its entities; catalog telemetry | `Tellma.Core.Abstractions`, `Tellma.Core.Queryex`, `Tellma.Core.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer` |
| `Tellma.Core.AspNetCore` (new) | `AddTellmaAspNetCore`/`UseTellma`/`MapTellma`; tenant resolution middleware and route groups; the BFF (cookie + OIDC + catalog ticket store + refresh + back-channel logout); the CSRF filter; request-context population; the distribution contract and admin surfaces; health and readiness | `Tellma.Core`, `FrameworkReference Microsoft.AspNetCore.App`, `Microsoft.AspNetCore.Authentication.OpenIdConnect`, `Microsoft.AspNetCore.Authentication.JwtBearer` |
| `Tellma.Core.Migrations` (new) | EF migrations and design-time factory for `CatalogDbContext` (the platform-owned catalog schema, mirroring `Tellma.Identity.Migrations`) | `Tellma.Core`, `Microsoft.EntityFrameworkCore.Design` (private) |
| `Tellma.Core.Migrator` (new) | The migrator runtime a distribution's Migrator project calls: commands (`migrate`, `seed`, `provision`, `status`), catalog-first fan-out with bounded parallelism, per-database `sp_getapplock`, `__SeedHistory`, the provisioning-step and seed seams | `Tellma.Core`, `Tellma.Core.Migrations`, `Tellma.Core.EntityFrameworkCore` |

`Tellma.Core` gains the edge `Tellma.Core → Tellma.Core.EntityFrameworkCore → Microsoft.EntityFrameworkCore.SqlServer`.
The interfaces every pack and every theme consume stay in `Tellma.Core.Abstractions` (BCL-only, §3).

**Rationale.** The web host half of Core cannot live in `Tellma.Core` without dragging
`Microsoft.AspNetCore.App` into the migrator and every worker; it cannot live in
`Tellma.Core.Webhooks` (which is deliberately email-agnostic HTTP fronting). The `Microsoft.AspNetCore.*`
naming convention is the ecosystem's own for "the ASP.NET Core adapter of X". The catalog schema is
platform-owned and identical across distributions, so distribution-generated migrations for it would be
pure noise and would drift; the identity engine already sets the precedent of a platform-owned
migrations assembly. The migrator runtime is platform logic ("logic lives in packages and propagates on
version bump") and a distribution's Migrator project is then ten lines. The Core → EF edge is forced
regardless of this theme: Queryex's schema is built from the EF model, TVP binding must be driven by
`model.GetTableTypes()`, and the catalog context needs a provider.

**Rejected.** `FrameworkReference` inside `Tellma.Core` (pollutes every non-web host); putting the
catalog context in `Tellma.Core.EntityFrameworkCore` (that package is the table-types extension, not a
model host); distribution-generated catalog migrations (drift and noise); a `Tellma.Core.Tenancy`
package (the registry is consumed by everything in Core — splitting it buys nothing).

**Confidence.** High on `Tellma.Core.AspNetCore`; medium on splitting `Migrations`/`Migrator` (could be
one package). **Review flag:** one package `Tellma.Core.Migrator` carrying the catalog migrations too.

### D4 — `AddTellma` at minimal fidelity: BCL-only `ITellmaFeature`, `Requires` edges, one aggregated startup validation

**Decision.** `Tellma.Core.Abstractions.Composition` defines the feature contract with no package
dependency (§3.4): a feature *declares* (name, `Requires<T>` edges) and *contributes* (services by
`Type`, EF configurations by `Type`, and a typed bag of contribution items other specs define —
securables, endpoint mappers, seeds, schedules). `Tellma.Core` runs the three phases inside
`services.AddTellma(slug, environment, configuration, configure)`:

1. **Declare** — every feature added through `TellmaBuilder.AddFeature<T>()` (explicit selection; no
   assembly scanning, no manifest generator in this release) declares itself.
2. **Validate** — missing `Requires` targets, duplicate feature names, a model-configuration type that
   does not implement `IEntityTypeConfiguration<>`, a service registration whose implementation does not
   implement its service type, and two features registering the same singleton service type. Every
   violation is collected into one `TellmaCompositionException` (each item: feature, problem, suggested
   fix). The same validator runs host-free in tests (`TellmaComposition.Validate(configure)`).
3. **Realize** — services are registered; the `TellmaFeatureRegistry` singleton holds the ordered
   contributions; `AddTellma` registers `DeploymentIdentity(slug, environment.EnvironmentName)`, the
   tenant `DbContext` (`UseDbContext<TContext>()` — pooled factory per tenant, D8), the catalog, the
   registry, the operation context, `ISandboxContext`, and the startup gate.

A second aggregation runs at host start: `IStartupCheck` implementations (registered by any feature or
package: options validity, credential-profile templates, the endpoint securable audit that the
permissions and web specs define, the `ISandboxContext` registration) run inside one
`IHostedLifecycleService.StartingAsync`, and all failures are thrown together before the host serves
traffic. Startup does **not** fail when the catalog database is unreachable: the first snapshot load is
attempted with a 10-second bound, the readiness endpoint reports not-ready until it succeeds, and the
refresh loop keeps retrying — a transient catalog outage must not crash-loop every instance of a
distribution during a slot swap.

The stack feature (spec 0014) and a module package's feature (spec 0017, `GlModuleFeature`) use this
one shape; both ship as ordinary classes with `[Requires<CoreFeature>]`. The migrator shares the
composition by calling the same `AcmeComposition.Configure` method (a static method in the Web
project) inside `Host.CreateApplicationBuilder`, so the model that generates migrations is the model that
serves traffic.

**Rationale.** Everything ARCHITECTURE.md's feature composition promises beyond this (`Recommends`,
`Excludes`, slots and providers, cardinality, the manifest source generator, the Builder Tool, the
bypass analyzer) is either UX for a catalog that does not exist yet or protection against a failure
mode (bypassing `AddTellma`) that a startup audit already catches. `Requires` plus one aggregated
exception is the part that removes ceremony from a distribution today. A `Type`-based contribution API
keeps the contract in Abstractions without EF or DI package references and still lets Core apply
configurations with `ApplyConfiguration` and services with `ServiceDescriptor`.

**Rejected.** Feature contract in a new `Tellma.Core.Composition` package referencing
`Microsoft.Extensions.DependencyInjection.Abstractions` (adds a Module → non-Abstractions edge the
dependency graph forbids); `Contribute(IServiceCollection, ModelBuilder)` (needs both packages in
Abstractions); reflection-based discovery (hides the selection site the Builder Tool will later
reflect; slower startup; surprises in tests); failing startup on catalog unavailability (see above).

**Confidence.** High on the shape; medium on "explicit selection only". **Review flag:** whether
`AddFeature<T>()` per feature is acceptable ceremony versus `AddFeaturesFrom(typeof(GlModuleFeature).Assembly)`
(one line per pack, reflection-based).

### D5 — Tenant routing: `/{tenantId:int}/api/web/…`, `/{tenantId:int}/api/v1/…`, `/{tenantId:int}/mcp`; distribution-level surfaces under `/api/…` and `/auth/…`

**Decision.** The tenant id is a positive `int` in the first path segment with an `:int` route
constraint; three tenant-scoped groups exist, all created by `MapTellma()`:

| Group | Auth scheme | Filters (in order) | Consumers |
|---|---|---|---|
| `/{tenantId:int}/api/web` | session cookie | `CsrfHeader` → `TenantState` → (securable filter, spec 0013/0015) | the SPA; endpoint projection (0015); distro custom endpoints |
| `/{tenantId:int}/api/v1` | bearer JWT (`aud` = distribution origin) | `TenantState` → securable | external integrations; seam only in this release |
| `/{tenantId:int}/mcp` | bearer JWT (`aud` = origin or this endpoint's URL, D16) | `TenantState` → MCP request filters (0015) | agents; seam only |

Distribution-level (tenant-less) routes: `/auth/*` (BFF, D12), `/api/me/tenants` (D10),
`/api/distribution-info`, `/api/admin/*` (control-plane M2M, D15), `/api/webhooks/{key}` (existing),
`/healthz`, `/readyz` (D18), and `/id/*` when the identity engine runs in-proc. Everything else falls
back to the SPA shell (added with the UI phase).

Resolution happens in `TenantResolutionMiddleware`, placed after `UseRouting` and before
`UseAuthentication`/`UseAuthorization`, not in an endpoint filter: it reads `tenantId` from
`HttpContext.GetRouteValue`, looks the descriptor up in the registry snapshot (no I/O), and populates the
scoped `OperationContextHolder` (D11) before any handler argument is bound — a `DbContext` or service
constructed during binding must already see the tenant. Outcomes:

| Registry says | Response | Telemetry `outcome` |
|---|---|---|
| no such id, or `Retired` | `404` problem `tenant-not-found` (identical body for both, no enumeration) | `unknown` / `retired` |
| `Provisioning` | `503` problem `tenant-provisioning`, `Retry-After: 30` | `provisioning` |
| `Suspended` | `403` problem `tenant-suspended` | `suspended` |
| `ReadOnly` | resolved; the `TenantState` filter rejects endpoints whose securable action is not read-only with `403` problem `tenant-read-only` | `read_only` |
| `Active` | resolved | `resolved` |

The middleware runs before authentication, so a suspended tenant costs no cookie work, but the responses
above are returned only after authentication succeeds (`401` first, then the tenant verdict) so that an
unauthenticated caller cannot probe tenant existence; the middleware records the verdict and the
`TenantState` filter emits it.

**Rationale.** An integer route value is the cheapest stable key: it indexes the registry dictionary
directly, tags every log scope, and needs no normalisation. Tenant-first ordering (`/{tenantId}/api/…`)
matches the brain dump and keeps distribution-level `/api/*` unambiguous (a first segment `api` is never
an int). Middleware placement is the one point that sees every route and runs before binding; the
research's inference that filters run after binding is why an endpoint-filter-only design breaks the
moment a handler takes a tenant-bound service as a parameter.

**Rejected.** Slugs (`/acme-hq/api/…` — renames, uniqueness, string keys); `X-Tenant` header (not
bookmarkable, invisible in logs and access logs, lost by MCP clients); host-based tenants
(`<tenant>.acme.app.tellma.com` — one managed certificate per tenant, DNS per tenant, and the redirect
URI set explodes); GUID tenant ids (ugly URLs, no benefit — ids are not secrets, membership is the
control).

**Confidence.** High.

### D6 — One catalog database per distribution, always; single-live versus multi-live is a registration policy

**Decision.** Every distribution has exactly one catalog database (SaaS name `tellma-<slug>`;
development `Tellma.dev.<worktree-id>.<slug>`), migrated by `Tellma.Core.Migrations` through the
distribution's migrator before any tenant database. Schema `catalog` (§4) holds: `Tenants`,
`TenantStateChanges`, `TenantMemberships` (the membership mirror, D10), `Sessions` (the BFF ticket store,
D12), `CatalogState` (the single-row version stamp, D7). In in-proc identity mode the identity engine's
schema `idsvr` also lives in this database (its `ConfigureDbContext` hook is pointed at the catalog
connection), which is the only distribution-wide database and therefore the only correct answer to spec
0003's "the distribution's own database".

`Tellma:Tenancy:RegistrationPolicy` ∈ `SingleLive` | `MultiLive` governs the write side
(`ITenantCatalog.RegisterAsync`): under `SingleLive` a second `Live` registration is refused, sandboxes
are always registrable and must name their `LiveTenantId`; under `MultiLive` any registration is allowed.
An `IStartupCheck` fails startup when a `SingleLive` distribution's catalog holds more than one live
tenant (the policy was changed after the fact).

The catalog holds **no connection strings and no secrets**: a tenant row carries `Server`, `Database`
and an optional `CredentialProfile` name (D8).

**Rationale.** One shape means one code path for the registry, the migrator fan-out, the session store,
the membership mirror and the in-proc identity store, and a restore of any tenant database never changes
which tenants exist. The cost is one small database per distribution (a pool admits 500 on the default
tier) and one more pooled connection string (bounded at `Max Pool Size=10`). The brain dump's own
requirement that sandboxes can be created makes "single-live has no programmatic registration" false —
only *live* registration is policy-gated.

**Rejected.** Catalog table in the live database (critique §1.1); catalog in configuration (read-only:
suspension state must be mutable at runtime from the admin surface; sandboxes are created at runtime);
a shared catalog across distributions (couples distributions' `Tellma.Core` versions — the control
plane's own reason for never touching distribution databases).

**Confidence.** High. **Review flag:** for on-prem single-tenant delivery, whether requiring two
databases (catalog + tenant) is acceptable, versus a configuration-backed read-only catalog store as a
second, deliberately minimal store (`Sessions` and the mirror would then have to live in the tenant
database). The proposal says two databases; the alternative is a second code path.

### D7 — The registry is an in-memory snapshot; cross-instance invalidation by a 15-second version poll

**Decision.** `TenantRegistry` (singleton, `Tellma.Core`) holds an immutable
`FrozenDictionary<int, TenantDescriptor>` plus the catalog `Version` it was loaded from. Reads
(`Find`, `Get`, `Tenants`) are dictionary lookups with no I/O. A hosted refresh loop (a `PeriodicTimer`
over `TimeProvider`, `Tellma:Catalog:RefreshInterval` default `00:00:15`) executes one statement:

```sql
SELECT [Version] FROM [catalog].[CatalogState] WHERE [Id] = 1;
```

and reloads the full snapshot (one `SELECT` over `catalog.Tenants`, hundreds of rows at most) only when
the version differs (`!=`, never `<`, so a restored backup with an older stamp still triggers a reload).
Every catalog write (`ITenantCatalog.RegisterAsync`, `SetStateAsync`, `RenameAsync`) runs in one
transaction that also sets `CatalogState.Version = NEWID()`, then calls `RefreshAsync()` on the local
registry so the writing instance is consistent immediately; other instances converge within one refresh
interval. `RefreshAsync` is single-flight (a `SemaphoreSlim(1,1)`; concurrent callers await the in-flight
load).

Descriptor changes are observable: `ITenantRegistry.Changed` (an event carrying the changed tenant ids)
lets the connection factory drop cached strings and factories for tenants whose location changed, and
lets the SignalR host (spec 0019) close connections of a tenant that just became `Suspended`.

**Rationale.** Tenant routing is "heavily read, rarely changed" (brain dump): a snapshot converts every
read to a lookup, and a version stamp converts invalidation to one indexed single-row read per instance
per 15 s — 5,760 trivial reads per instance per day, against zero per request. No Redis, no bus, no
`SqlDependency`. The admin surface's `suspend` command lands on one instance; ARCHITECTURE.md's
"invalidates its tenant-resolution cache" is honoured immediately there and within 15 s elsewhere,
which is inside any reasonable suspension SLA.

**Rejected.** Per-request catalog read (one extra round trip per request, to a different database);
`SqlDependency`/query notifications (unsupported on Azure SQL Database's service broker model in
practice, and a poor fit for App Service); `rowversion` as the stamp (bumps on every row update
including membership mirror writes, and `MAX(rowversion)` needs a scan or a separate index);
`bigint` monotonic version (compared with `!=` it works, but a restore-then-increment can collide with a
value an instance already saw; a fresh GUID cannot).

**Confidence.** High. **Review flag:** the refresh interval (15 s) — 5 s halves suspension latency at
three times the reads; 60 s is fine if suspension latency of a minute is acceptable.

### D8 — Connection resolution: credential-profile templates, no per-tenant secrets, bounded pools, one pooled `DbContext` factory per tenant

**Decision.** `Tellma:Catalog:CredentialProfiles:<name>` holds a SqlClient connection-string
*template* with two placeholders; the default profile is named `default`:

```jsonc
"Tellma": {
  "Catalog": {
    "ConnectionString": "Server=tcp:sql-tellma-platform.database.windows.net,1433;Database=tellma-acme;Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=10",
    "RefreshInterval": "00:00:15",
    "CredentialProfiles": {
      "default": "Server=tcp:{server},1433;Database={database};Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=20;Min Pool Size=0"
    }
  }
}
```

`TenantConnectionFactory` (singleton, `Tellma.Core`) composes the tenant's string once per
`(tenantId, descriptor.Version)` with `SqlConnectionStringBuilder` (never string concatenation): it
substitutes `{server}`/`{database}`, forces `Application Name=tellma-<slug>` (so `sys.dm_exec_sessions`
attributes load to the distribution), and caches the result. Startup checks (`IStartupCheck`) reject a
template without both placeholders, a template containing a literal `@Microsoft.KeyVault(` (an
unresolved App Service reference), `Max Pool Size` above `Tellma:Catalog:MaxPoolSizePerTenant`
(default 20), and `Min Pool Size` above 0.

Per-tenant EF access uses one `PooledDbContextFactory<TContext>` per tenant, created lazily by
`TenantDbContextFactory<TContext>` (singleton) with that tenant's options and `poolSize: 16`, cached in a
`ConcurrentDictionary<int, …>` keyed on tenant id, and dropped when the registry reports a location
change. All factories share EF's internal service provider and the compiled model because the SQL Server
options extension's `ShouldUseSameServiceProvider` compares engine type and compatibility levels only,
and the relational extension's `GetServiceProviderHashCode` returns `0` — a connection string difference
does not fragment the model cache (verified against the EF Core source checkout, §8). The batch executor
of spec 0011 takes `ITenantConnectionFactory.OpenAsync(tenantId)` and never a `DbContext` for the
Queryex/TVP path; the `DbContext` is the model host and the migrator's tool.

Pool arithmetic that the spec must state as an invariant: with `T` active tenants, `I` instances and a
per-tenant `Max Pool Size` of `P`, the distribution can hold at most `T × I × P` sessions in the pool;
against the pool-wide limit of 30,000 sessions and the per-pool login/worker ceiling (210 workers on a
2-vCore General Purpose pool), `P = 20` keeps 200 tenants × 3 instances at 12,000 and relies on the
4–8-minute idle drain (`Min Pool Size=0`) so that idle tenants hold no connections. The background
scheduler (spec 0019) staggers per-tenant work and never opens every tenant at once; the migrator's
fan-out parallelism (default 8) is bounded for the same reason.

**Rationale.** The catalog stores locations; identities and passwords are a property of the
*distribution*, not the tenant, in every deployment shape (research §4.3–4.5), so composing the string
from a template removes the secret question entirely and makes rotation (on-prem password change) a
configuration reload, not a catalog migration. Per-tenant pooled factories are the only pooling-safe
shape EF documents; sharing the model makes the per-tenant cost one options object and a lazily filled
pool.

**Rejected.** Connection strings in the catalog (secret storage, rotation by `UPDATE`); one pooled
factory with `SetConnectionString` on checkout (driver-state reset caveats; a returned context that
still points at tenant A's string is a cross-tenant bug waiting for a pool race); `Active Directory
Default` in production (the docs warn about its latency; it is the developer-machine profile);
`Max Pool Size` left at 100 (hundreds of tenants × instances × 100 exceeds the pool session limit on
paper and the worker limit in practice).

**Confidence.** High.

### D9 — Secrets policy

**Decision.**
- **SaaS.** Tenant databases and the catalog are reached with the App Service's managed identity
  (`Authentication=Active Directory Managed Identity`); provisioning (D17) runs, inside each new
  database and under the migrator job's DDL identity:

  ```sql
  CREATE USER [tellma-acme] FROM EXTERNAL PROVIDER;
  ALTER ROLE db_datareader ADD MEMBER [tellma-acme];
  ALTER ROLE db_datawriter ADD MEMBER [tellma-acme];
  -- sequences and generated table types: the table-types extension emits GRANT EXECUTE ON TYPE for the
  -- configured principal set (Tellma:Database:ApplicationPrincipal); sequences get GRANT UPDATE.
  ```

  The distribution's own secrets (OIDC client secret for `acme`, the `acme-svc` machine-client secret,
  storage keys where not identity-based) come from the distribution's Key Vault through
  `Azure.Extensions.AspNetCore.Configuration.Secrets` (`AddAzureKeyVault(vaultUri, new
  DefaultAzureCredential())`, `ReloadInterval` 15 min) or App Service Key Vault references; a
  configuration value that still starts with `@Microsoft.KeyVault(` fails startup.
- **On-prem.** Integrated security with a service account (Windows hosts) or one SQL login whose
  password is supplied through `Tellma:Catalog:CredentialProfiles:default` read from an environment
  variable or a root-owned file (`Tellma:Catalog:CredentialProfilesFile`, a JSON document the host
  injects — systemd credentials, Docker/Kubernetes secrets); the Data Protection key ring is a
  file-system folder shared by the instances with keys protected by an X.509 certificate
  (`ProtectKeysWithCertificate`), never DPAPI (Windows-only).
- **Development.** `Integrated Security=True` against LocalDB/SQL container; OIDC secret generated at
  each boot by the in-proc provisioning call (D20); no secret files.
- **Never.** No secret in any database table, in `appsettings*.json`, in the catalog, in logs
  (`SqlConnectionStringBuilder.ToString()` output is redacted before logging by the factory: passwords
  are replaced with `***`), or in telemetry tags.
- **SqlClient line.** The repo moves to `Microsoft.Data.SqlClient` 6.1.6 (spec 0011 needs it for
  hierarchyid; EF SqlServer 10.0.11 requires ≥ 6.1.6); Entra authentication stays bundled on 6.x. When
  the provider moves to SqlClient 7.x, `Microsoft.Data.SqlClient.Extensions.Azure` is added in the same
  bump or every Entra mode fails at runtime.

**Confidence.** High.

### D10 — Membership directory: a catalog mirror for both shapes; the tenant's `User` table stays authoritative

**Decision.** `ITenantMembershipDirectory` (Abstractions) is backed by `catalog.TenantMemberships`
(`Subject`, `TenantId`, `IsActive`, `UpdatedAt`). `GET /api/me/tenants` performs one query (§4) and
returns the caller's tenants with name, category, state and live-tenant link — no tenant database is
opened. Writers:

- The user save pipeline (spec 0013/0017) records membership changes as a **post-commit side effect**
  (`RecordAsync` with the affected `(TenantId, Subject, IsActive)` rows through the
  `[catalog].[TenantMembershipList]` table type); a failure is logged and metered
  (`tellma.tenancy.membership.mirror_write` with `outcome=failed`) and never fails the save.
- The per-request connect step (spec 0013) **self-heals**: when the subject resolves to an active user in
  the tenant and the request-scoped `MembershipHint` says the mirror did not list this tenant (the hint
  is set by the middleware from a per-instance cache of the subject's mirror rows, TTL 60 s, populated
  lazily), the connect result enqueues a mirror upsert after the response.
- A reconciliation job (spec 0019 consumer, nightly, per tenant) rebuilds the tenant's mirror rows from
  its `User` table in one TVP write.

Single-live distributions use exactly the same path; the brain dump's fan-out is not implemented.

**Rationale.** One primary-key-range read replaces N cross-database round trips, and the write side
rides transactions that already happen. "Best effort" is bounded by the nightly rebuild and by the
self-heal on the first successful request, so the worst case a user sees is a tenant missing from the
picker until they open it by URL once.

**Rejected.** Fan-out (critique §1.2); an authoritative membership table in the catalog (two sources of
truth for access control; the tenant database must remain self-contained for backup/restore and
on-prem delivery); consulting the identity server (it holds no tenant membership by design, spec 0003
§6.1).

**Confidence.** High.

### D11 — Operation context: an immutable value in a scoped holder, initialised once per request and once per job scope; no `AsyncLocal`

**Decision.** `Tellma.Core.Abstractions.Context` defines the carrier (§3.2): `OperationDescriptor`
(tenant id, caller, locale, kind) and the read interfaces `IOperationContext : ITenantContext,
ICallerContext, ILocaleContext`. `Tellma.Core` registers one scoped `OperationContextHolder` that
implements all of them plus `ISandboxContext`; it is initialised exactly once per DI scope
(`Initialize(OperationDescriptor)` throws on a second call) by:

- `TenantResolutionMiddleware` for HTTP requests (tenant from the route, caller from the authenticated
  principal, locale from the headers/user/tenant precedence of specs 0012/0015, `OperationKind.Request`);
- the MCP request pipeline (same, `OperationKind.Request`, caller kind `ServiceAccount` for
  client-credentials tokens);
- the job runner of spec 0019 for each job execution: it takes the `OperationDescriptor` that was
  copied at enqueue time (`IOperationContext.Snapshot()` — the tenant *id*, the caller identity, the
  locale), re-resolves the `TenantDescriptor` from the registry when the scope opens (state may have
  changed), and initialises the holder with `OperationKind.Job`;
- the migrator for `seed`/`provision` (`OperationKind.Startup`, caller kind `System`).

Reading `Tenant` on a tenant-less scope throws `InvalidOperationException` ("no ambient tenant"); code
that may legitimately run tenant-less checks `HasTenant`. `ISandboxContext.IsSandbox` reads
`Tenant.Category == Sandbox` and therefore throws on a tenant-less scope — a distribution-level
operation that sends mail without a tenant is a bug that must surface loudly, not a silent live send.
No `AsyncLocal` carries any of this; `Activity.Current` is the only ambient value and it carries the
trace, not authority. Singletons never receive `IOperationContext`; they take
`IServiceScopeFactory` and open a scope.

The `UserId` slot (the tenant-local user id resolved by the connect step of spec 0013) and the
permission snapshot are *late-bound* fields on the holder (`ICallerContext.UserId` is `int?` until
connect runs); this is what lets the connect step ride the first business round trip (seam 16) instead
of being a separate call before every handler.

**Rationale.** The research (§6) shows every failure mode of ambient state: a value set in a callee
does not flow back, fire-and-forget work inherits the wrong tenant, pooled contexts capture the first
scope's value. A scoped holder initialised at one known point has none of them and costs a dictionary
lookup per resolution. Copying the descriptor at enqueue time is the only way a job can run with the
same caller and locale as the request that created it, days later, on another instance.

**Rejected.** `AsyncLocal<T>` source of truth (above); `IHttpContextAccessor` in services (null in jobs,
captured incorrectly in singletons); passing the context as a method parameter everywhere (viral, and
the batch executor would need it on every call).

**Confidence.** High. **Review flag:** `ISandboxContext` throwing on tenant-less scopes versus
returning `true` (treat tenant-less as sandbox: nothing external happens, but silently).

### D12 — Distribution-side authentication: BFF cookie with a catalog-backed `ITicketStore`, per-instance L1, store-coordinated token refresh, back-channel logout

**Decision.** `AddTellmaAspNetCore` configures:

- **Cookie scheme** (`DefaultScheme`): name `__Host-tellma.session` (plain `tellma.session` under
  Development over HTTP), `HttpOnly`, `Secure=Always` (`SameAsRequest` in Development), `SameSite=Lax`,
  `SlidingExpiration=true`, `ExpireTimeSpan=7 days` (equal to the refresh token's idle window),
  `SessionStore = CatalogTicketStore`, `EventsType = TellmaCookieEvents`. The cookie carries only the
  session key (43 characters, 256 bits of randomness); the ticket — principal, `sid`, tokens — is
  Data-Protection-encrypted and stored in `catalog.Sessions`. The Data Protection ring is shared across
  instances (Azure Blob + Key Vault in SaaS; a shared folder + certificate on-prem), with
  `SetApplicationName("tellma-<slug>")`.
- **OIDC scheme** (`DefaultChallengeScheme`): `Authority` = `Tellma:Authentication:Authority`
  (standalone) or the in-proc issuer; `ClientId = <slug>`; `ClientSecret` from configuration or the
  in-proc provisioning call; `ResponseType=code`; PKCE S256; `PushedAuthorizationBehavior=Require`;
  `SaveTokens=true`; `GetClaimsFromUserInfoEndpoint=false`; `MapInboundClaims=false`; scopes `openid
  profile email offline_access tellma_api`; `CallbackPath=/signin-oidc`,
  `SignedOutCallbackPath=/signout-callback-oidc`; claims kept on the principal: `sub`, `sid`, `name`,
  `email`, `locale`, `acr`, `amr`, `auth_time`, `tellma_acr_auth_time`. `OnTokenValidated` rejects a
  principal without `sub` or `sid`.
- **`CatalogTicketStore : ITicketStore`.** `StoreAsync` inserts the row; `RetrieveAsync` consults a
  per-instance `MemoryCache` (TTL `Tellma:Authentication:SessionCacheTtl`, default 60 s, entries sized,
  bounded at 50,000 entries) and falls back to one primary-key read; `RenewAsync` writes through and
  refreshes the cache; `RemoveAsync` deletes and evicts. Cache misses are metered
  (`tellma.auth.session_store` with `source=database`).
- **Token refresh** in `TellmaCookieEvents.ValidatePrincipal`: when the stored access token expires
  within `AccessTokenRefreshSkew` (default 60 s), the event re-reads the session row from the store
  (bypassing L1) and, if the row's `TokensVersion` already moved (another instance refreshed), adopts
  the newer ticket; otherwise it redeems the refresh token against the authority and writes the new
  ticket with a compare-and-set on `TokensVersion` (§4 statement); a lost CAS re-reads and adopts.
  Success → `ReplacePrincipal` + `ShouldRenew`; `invalid_grant` (revoked, rotated-and-reused, expired) →
  `RejectPrincipal`, the row is deleted, the SPA receives `401` and re-enters the challenge. This is one
  HTTPS call to the authority per active session per 10 minutes, inline on one request; it is also the
  policy re-evaluation point spec 0003 §9.4 relies on, and the point where a tightened `acr` shows up.
- **Back-channel logout** `POST /auth/backchannel-logout` (anonymous, `application/x-www-form-urlencoded`,
  parameter `logout_token`; the URI the identity provisioning call registers as
  `tellma:backchannel_logout_uri`): the token is validated with the OIDC handler's own
  `ConfigurationManager` (issuer, `aud = <slug>`, signature against the JWKS, `typ=logout+jwt`,
  `events` contains `http://schemas.openid.net/event/backchannel-logout`, `iat` within ±5 min, `sid`
  present, no `nonce`); then `DELETE catalog.Sessions … WHERE Sid = @sid` with `OUTPUT deleted.Subject`,
  the L1 entries for those keys are evicted, and `ISessionTerminationListener.SessionsTerminatedAsync(subject, keys)`
  fans out to the SignalR host (spec 0019) so the user's connections close. `jti` replay is not tracked:
  a replayed logout token deletes nothing and returns `200`. The built-in `RemoteSignOutPath`
  (front-channel) stays at its default and is unused; it cannot serve back-channel logout because it
  authenticates the *browser's* cookie and compares `sid` against it (verified in the handler source, §8).
- **Endpoints**: `GET /auth/login?returnUrl=` (challenge; `returnUrl` must be a local path),
  `GET /auth/step-up?acr=urn:tellma:acr:aal2|aal3&maxAge=&returnUrl=` (challenge with `acr_values` and
  `max_age`), `POST /auth/logout` (`{ "scope": "local" | "global" }`, default global → cookie sign-out +
  RP-initiated end-session), `GET /auth/session` (display profile: name, email, locale, `sub`, `acr`,
  `tellma_acr_auth_time`; anonymous callers get `401`), and the two handler callbacks. On login the BFF
  writes the small readable display-profile cookie spec 0003 §7.1 describes (`tellma.profile`, not
  `HttpOnly`, no tokens) for the SPA's instant launch.
- **Per-request assurance re-check.** The securable filter (spec 0013/0015) reads `acr` and
  `tellma_acr_auth_time` from `ICallerContext.Caller.Assurance`; a sensitive endpoint's unmet requirement
  yields `401` with `WWW-Authenticate: Bearer error="insufficient_user_authentication",
  acr_values="…", max_age=…` (mapping owned by spec 0015; the challenge writer lives in this package).

**Rationale (against the stateless-cookie alternative).** A stateless cookie carries 3–5 KB (principal +
three tokens) on every API call — on a mobile connection that is the dominant upload cost of a 200-byte
request — and, more importantly, it is unrevocable until it expires: a stolen cookie is a stolen refresh
token for seven days, and back-channel logout has nothing to delete. The store makes the cookie 43
characters, makes revocation a `DELETE`, and with L1 caching costs one primary-key read per session per
instance per minute. Coordinating refresh through the store's `TokensVersion` is what prevents two
instances from redeeming the same refresh token 31 seconds apart (reuse detection would then revoke the
whole family). Refreshing inline rather than in a background sweep keeps the design to one code path and
one HTTP call per session per ten minutes.

**Rejected.** Stateless cookie + revoked-`sid` list (above); `Duende.BFF` (paid per customer-facing
deployment); `Duende.AccessTokenManagement` (Apache-2.0 and fine, but it manages a stateless cookie's
tokens — the store-coordinated CAS is the part that matters and it is ~100 lines); an
`IDistributedCache` ticket store (there is no Redis; SQL is the distributed store and it is already
there); refreshing on every request (pointless — the access token is never presented anywhere).

**Confidence.** High on the store; medium on the 60-second L1 TTL. **Review flag:** L1 TTL 60 s (cross-
instance revocation latency) versus 10 s (six times the catalog reads, still trivial) versus no L1 at
all (one catalog round trip per request — the only option that makes revocation instant everywhere).

### D13 — CSRF posture for the cookie-authenticated JSON surface: required custom header + `Origin`/`Sec-Fetch-Site` + JSON-only bodies + no CORS

**Decision.** `CsrfHeaderEndpointFilter` on `/{tenantId:int}/api/web` and on every cookie-authenticated
distribution-level endpoint that changes state (`/auth/logout`, the self-service endpoints of spec 0017,
the blob upload endpoint of spec 0016) enforces, before authorization:

1. the request carries `X-Tellma-Client` (any value; the SPA sends `web/<build>`; the value is not a
   telemetry tag) — absence → `403` problem `csrf-header-missing`;
2. when `Sec-Fetch-Site` is present it is `same-origin` or `none`; otherwise, when `Origin` is present it
   equals the distribution's configured origin — else `403` problem `csrf-origin-mismatch`;
3. a request with a body has `Content-Type: application/json` (or `multipart/form-data` only on the
   endpoints that declare `AcceptsMultipart`) — else `415`.

No CORS policy is registered (default deny, no preflight ever succeeds), no antiforgery middleware, no
antiforgery token cookie, no per-session token endpoint. Bearer-authenticated surfaces (`/api/v1`,
`/mcp`) are exempt (no cookie, no CSRF class); anonymous endpoints (`/api/webhooks/{key}`,
`/auth/backchannel-logout`, `/api/distribution-info`, health) are exempt. The session cookie stays
`SameSite=Lax` per spec 0003; RFC 10017's `SHOULD SameSite=Strict` is deliberately not followed
because the post-login redirect chain and deep links from the identity server would drop the cookie.

**Rationale.** A custom header forces a CORS preflight for any cross-origin caller, and with no CORS
policy the preflight fails; the `Origin`/`Sec-Fetch-Site` check catches the remaining
same-site-different-origin case. The .NET 10 antiforgery documentation itself states JSON endpoints are
not rejected by the middleware and generally do not need it. Zero extra requests at app start, zero
per-request token echo, zero server-side token state — and the SPA already has to send platform headers
(culture, calendar, time zone) on every call.

**Rejected.** Cookie-to-header antiforgery tokens (a token endpoint at startup, a second cookie, and
the middleware's own verdict is not enforced for JSON anyway); `SameSite=Strict` (breaks the OIDC
return); `X-Requested-With: XMLHttpRequest` as the header (works, but a platform-named header is
self-documenting and the value doubles as the client build tag in structured logs).

**Confidence.** High. The header *name* is spec 0015's to confirm.

### D14 — Session revocation on user deactivation is tenant-level, not cookie-level

**Decision.** Deactivating a user in a tenant (spec 0013/0017) does not touch `catalog.Sessions`: the
user may be active in another tenant of the same distribution. It (a) bumps the user's permissions tag
so the next connect step (which reads the tag in the first batch) returns `403` for that tenant,
(b) records `IsActive = false` in the membership mirror (post-commit), and (c) calls
`ISessionTerminationListener.TenantAccessRevokedAsync(tenantId, subject)` so the SignalR host closes
that user's connections *for that tenant* (per-user, per-tenant group). Identity-level events
(back-channel logout, "sign out everywhere", `invalid_grant` at refresh) end the cookie session (D12).
Latency bounds the spec states: same instance immediate; other instances within the session L1 TTL for
cookie revocation and within the connect step's next round trip for tenant revocation (that is, the next
request).

**Confidence.** High.

### D15 — Suspension hook and the admin contract surface

**Decision.** `TenantState` transitions are the suspension hook: `Active → ReadOnly` (soft),
`Active|ReadOnly → Suspended` (hard), back to `Active`, and `→ Retired` (terminal; the database is
retained and detached from routing). Each transition is one catalog transaction (§4) that appends a
`TenantStateChanges` row (reason, actor), bumps `CatalogState.Version`, refreshes the local registry
and raises `ITenantRegistry.Changed`; the job runner (spec 0019) skips tenants not `Active` (a `ReadOnly`
tenant's background work is paused too — an export that writes an inbox row would otherwise violate the
read-only promise), and the SignalR host closes a `Suspended` tenant's connections.

The admin contract (control-plane command path; bearer JWT with scope `tellma_control_plane`, `aud` =
distribution origin, mapped under `/api/admin`):

| Endpoint | Body / result |
|---|---|
| `GET /api/admin/tenants` | `[{ id, name, category, state, liveTenantId, createdAt, modifiedAt }]` |
| `POST /api/admin/tenants/{id}/state` | `{ "state": "ReadOnly" \| "Suspended" \| "Active" \| "Retired", "reason": "…" }` → `200` with the descriptor; `409` when the transition is illegal |
| `POST /api/admin/tenants` | `{ "name", "category", "liveTenantId"?, "server"?, "database"? }` → registers a `Provisioning` row (D17); `409` under `SingleLive` for a second live tenant |
| `GET /api/admin/tenants/{id}/usage` | seam: `{ "period", "metrics": {} }` — empty object in this release; the metering shape belongs to the control-plane spec |
| `GET /api/admin/info` | same body as `/api/distribution-info` plus `registrationPolicy`, `tenantCount` |

**Confidence.** High on states; medium on the surface's exact shape (control plane is not yet
specified). **Review flag:** whether `ReadOnly` should pause background work or let read-only jobs
(exports) run — pausing is the safe default proposed.

### D16 — MCP topology: one endpoint per tenant, bearer JWT, audience = distribution origin or the endpoint URL; the identity server must add three things

**Decision.** `app.MapMcp("/{tenantId:int}/mcp")` (spec 0015 supplies the tools; this theme supplies
the hosting rules): stateless session mode (2026-07-28 revision, no affinity), `RequireAuthorization`
with the JWT bearer scheme, the `TenantState` filter, and the same `OperationContextHolder` population
as the web surface. Token validation accepts `aud` ∈ { `https://<slug>.app.tellma.com`,
`https://<slug>.app.tellma.com/{tenantId}/mcp` } — the request's own canonical URL — so a token minted
with the RFC 8707 `resource` an MCP client is required to send is valid only at that tenant's endpoint,
while CLI/native tokens carrying the origin audience (what spec 0003 grants today) also work. The
protected-resource metadata is served by the SDK at
`/.well-known/oauth-protected-resource/{tenantId}/mcp` with `resource` = the endpoint URL and
`authorization_servers` = [authority]. An agent that needs two tenants configures two servers; an
autonomous agent is a service account (`client_credentials`, `resource` = the endpoint) whose
`client_id` the tenant maps to a user row (spec 0013).

What the identity server must add (an amendment spec to 0003, since 0003 is frozen): (1) accept a
`resource` whose origin is a granted resource and copy the *requested* URL into `aud` (a handler
replacing exact-match `ValidateResources` for the `tellma_api` scope), so per-tenant audiences need no
per-tenant identity state; (2) client ID metadata documents (CIMD) with
`client_id_metadata_document_supported: true` and `none` in `token_endpoint_auth_methods_supported`,
which is what Claude Code, hosted Claude and ChatGPT/Codex use to register without an operator; (3)
pre-registered public native clients for the vendors that cannot use CIMD (Cursor: DCR or static id),
which the seed configuration already supports. Until (1) ships, the MCP endpoint validates `aud` =
origin only and tenant isolation rests on membership, exactly as the web surface.

**Rationale.** One endpoint per tenant is one code path with the web surface (tenant from the URL,
caller from the credential, everything else identical), keeps tool schemas free of a tenant argument,
and gives per-tenant token audiences for free once the identity server echoes `resource`. Stateless
mode removes session affinity — the one hosting requirement that would have forced ARR affinity on the
App Service.

**Rejected.** One MCP server per distribution with a `tenantId` argument (context bloat, a
tenant-selection tool, and a shared audience); per-tenant resource *registration* on the identity server
(hundreds of rows of identity state that must be created at provisioning and cleaned at retirement;
OpenIddict 7.x has only static `RegisterResources` — dynamic resources arrive in 8.0);
`StatefulForInitializeClients` by default (affinity for legacy clients; enable per deployment only if a
client that needs it appears).

**Confidence.** High on topology; medium on the `aud` rule pending the identity amendment.

### D17 — Provisioning is a seam plus a working local path: catalog row → migrator `provision` → steps → `Active`

**Decision.** Provisioning a tenant is five ordered actions, all idempotent, driven by
`Tellma.Core.Migrator`'s `provision --tenant <id>` command (or `provision --new --name … --category …
[--live-tenant <id>] [--server …] [--database …]` which registers the row first):

1. `ITenantCatalog.RegisterAsync` writes the `Provisioning` row (name, category, location; the default
   location is `Tellma:Catalog:DefaultServer` + `tellma-<slug>-<id>` in SaaS, `Tellma.dev.<wt>.<slug>.<id>`
   in Development);
2. `CREATE DATABASE` (Azure: `… (EDITION = …, SERVICE_OBJECTIVE = ELASTIC_POOL(name = …))` from
   `Tellma:Provisioning:CreateDatabaseTemplate`; on-prem: plain `CREATE DATABASE`; a sandbox uses
   `CREATE DATABASE … AS COPY OF <live>` on Azure and backup/restore on-prem through
   `Tellma:Provisioning:SandboxCloneMode`), then `ALTER DATABASE … SET READ_COMMITTED_SNAPSHOT ON` (the
   platform's one isolation story, spec 0011);
3. the contained user grant (D9) under the DDL identity;
4. `AcmeDbContext` migrations + `__SeedHistory` seeds (the tenant bootstrap of spec 0013/0017 runs as
   a seed: the first admin from `--admin-subject`/`--admin-email`, or the dev admin in Development);
5. every registered `ITenantProvisioningStep` (Abstractions; ordered by `Order`, idempotent, receives
   `TenantProvisioningContext(TenantDescriptor, TenantDatabaseLocation, IServiceProvider)`) — the
   distribution-specific integrations the brain dump worries about plug in here; then
   `SetStateAsync(Active)`.

The web app never runs provisioning. In multi-live SaaS, `POST /api/admin/tenants` (or the tenant
admin flow) registers the row and calls `ITenantProvisioningTrigger.RequestAsync(tenantId)`, whose
default implementation only logs "provisioning requested" (an operator or CI runs the migrator); the
Container Apps Job trigger is a later implementation of the same interface. Sandboxes get the same path
with `--category Sandbox --live-tenant <id>`.

**Confidence.** High on the seam; medium on the sandbox clone details (Azure `AS COPY OF` needs the
same server and takes minutes; on-prem restore needs file paths — both are deployment configuration).

### D18 — Distribution contract surface

**Decision.**

- `GET /api/distribution-info` (anonymous, output-cached 60 s, `Cache-Control: public, max-age=60`):
  ```json
  { "slug": "acme", "displayName": "Acme", "deploymentId": "acme-staging", "environment": "Staging",
    "version": "1.4.0+abc123", "platformVersion": "1.4.0", "authority": "https://identity.tellma.com",
    "clientId": "acme", "surfaces": ["web", "mcp"], "registrationPolicy": "MultiLive" }
  ```
- `GET /healthz` — liveness: process up, no I/O.
- `GET /readyz` — readiness: registry snapshot loaded (a stale snapshot older than
  `3 × RefreshInterval` reports degraded but ready), Data Protection ring readable, credential-profile
  templates valid. Slot-swap health probes point here.
- `GET /api/me/tenants` (cookie): `[{ "id", "name", "category", "state", "liveTenantId", "isActive" }]`.
- The `/auth/*` endpoints (D12), `/api/admin/*` (D15), `/api/webhooks/{key}` (spec 0007).

**Confidence.** High.

### D19 — Observability from day one

**Decision.** Meter `Tellma.Core` (an `IMeterFactory` meter named after the package; instrument names as
`const`s in `Tellma.Core.Abstractions.Tenancy.TenancyTelemetryNames`, §3.6); no tenant id on any
instrument. Instruments:

| Instrument | Kind | Tags |
|---|---|---|
| `tellma.tenancy.resolution` | counter | `outcome` ∈ resolved, unknown, retired, provisioning, suspended, read_only |
| `tellma.tenancy.catalog.refresh.duration` | histogram (s) | `outcome` ∈ unchanged, reloaded, failed |
| `tellma.tenancy.catalog.snapshot.age` | observable gauge (s) | — (alert when > 3 × interval) |
| `tellma.tenancy.catalog.tenants` | observable gauge | `category`, `state` |
| `tellma.tenancy.connection.factories` | observable gauge | — (number of per-tenant pooled factories alive) |
| `tellma.tenancy.membership.lookup.duration` | histogram (s) | — |
| `tellma.tenancy.membership.mirror_write` | counter | `outcome` ∈ succeeded, failed |
| `tellma.auth.session_store` | counter | `operation` ∈ retrieve, store, renew, remove; `source` ∈ cache, database |
| `tellma.auth.token_refresh` | counter | `outcome` ∈ refreshed, adopted, failed, revoked |
| `tellma.auth.backchannel_logout` | counter | `outcome` ∈ accepted, invalid |
| `tellma.auth.csrf_rejected` | counter | `reason` ∈ header_missing, origin_mismatch, content_type |

Logging: `TenantResolutionMiddleware` opens `ILogger.BeginScope` with `TenantId`, `TenantCategory`,
`Subject` (never email) and `ClientTag` for every request; the job runner does the same per job.
Tracing: `ActivitySource` `Tellma.Core`; the request activity is tagged `tellma.tenant.id` and
`tellma.deployment.id` (per-tenant identity is allowed on traces and logs, which is the control plane's
telemetry path, and forbidden on metrics). Alert queries under `infra/monitoring/` for snapshot age,
refresh failures, refresh `revoked` spikes and `csrf_rejected` spikes are checked in with the package,
cross-checked by the existing instrument-name test.

**Confidence.** High.

### D20 — Local development: in-proc identity in the catalog database, self-provisioned BFF client, deterministic names

**Decision.** In Development the reference distribution runs the identity engine in-proc at `/id`
(`TellmaIdentity:Mode=InProc`, `Issuer=https://localhost:<port>/id`, store =
`ConfigureDbContext → catalog connection`, schema `idsvr`, its own `__EFMigrationsHistory` in that
schema). On startup, `AddTellmaAspNetCore` in in-proc mode calls
`IClientProvisioningService.CreateDistributionAsync("acme", origin, origin + "/auth/backchannel-logout",
allowTokenExchange: false)` and injects the returned BFF secret into `OpenIdConnectOptions` through an
`IPostConfigureOptions` — the secret lives only in memory and rotates every boot. The migrator's
`provision --new --name "Acme Dev" --category Live` under Development seeds tenant `1` and hands the
tenant bootstrap (spec 0017) the dev admin's fixed subject `00000000-0000-0000-0000-000000000001`
(`admin@localhost`), so the first sign-in lands on an admin. Database names follow
`Tellma.dev.<worktree-id>.acme` (catalog) and `Tellma.dev.<worktree-id>.acme.1` (tenant); ports come
from `.dev-ports.local`. `dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator -- provision --new …`
then `dotnet run --project …Web` is the whole loop, on Windows and Linux.

**Confidence.** High.

### D21 — Testing

**Decision.**

| Suite | Tier | Pins |
|---|---|---|
| `test/core/Tellma.Core.Tests` | unit | composition validator (every violation aggregated, fix text present); registry snapshot semantics with `FakeTimeProvider` (version unchanged → no reload; changed → reload; `!=` on an older GUID); connection-string composition and the startup rejections (missing placeholders, Key Vault literal, pool size); holder initialise-once and tenant-less throws; `ISandboxContext` over category; `OperationDescriptor` round trip |
| `test/core/Tellma.Core.IntegrationTests` (`Category=Integration`, Testcontainers) | integration | catalog migrations apply from empty and are idempotent; state transitions bump `Version` and append history; membership TVP upsert; ticket-store CAS: two concurrent refreshers, exactly one redeems; session sweep |
| `test/core/Tellma.Core.AspNetCore.Tests` | unit (`TestServer`) | CSRF filter matrix (header/origin/content-type × cookie/bearer/anonymous); tenant verdict matrix (unknown/retired/provisioning/suspended/read-only × read/write endpoint); back-channel token validation with a test signing key (bad `aud`, missing `events`, stale `iat`, replay → 200 no-op); challenge writer for step-up; `/api/distribution-info` shape |
| `distributions/acme/test/…IntegrationTests` (`Category=Integration`) | integration | boots the Web host with in-proc identity (`Tellma.Identity.TestSupport`) against Testcontainers: full login → `/api/me/tenants` → tenant request; suspension end-to-end (admin `state` → 403 within one refresh); readiness before/after catalog availability; migrator `provision` creates a tenant reachable by URL |
| architecture tests | unit | no platform project references `distributions/**`; `Tellma.Module.*` never references `Tellma.Core`; `Tellma.Core.Abstractions` has zero package references |

PR runs unit + integration; nothing here is `Live=true`.

**Confidence.** High.

### D22 — Version pins this theme adds

`Microsoft.AspNetCore.Authentication.OpenIdConnect` and `Microsoft.AspNetCore.Authentication.JwtBearer`
at the repo's ASP.NET line (10.0.9 today, 10.0.11 latest stable — the bump is a Dependabot matter);
`Azure.Extensions.AspNetCore.Configuration.Secrets` 1.5.2; no Finbuckle (roll our own — the store/strategy
split is under 200 lines and Finbuckle's per-tenant-database recipe breaks pooling); no Duende packages;
`ModelContextProtocol.AspNetCore` is spec 0015's pin.

---

## 3. Contracts

Namespaces and members are final-looking. Code is normative for shape, not formatting; XML docs are
abbreviated to summaries. Nothing in `Tellma.Core.Abstractions` references a package.

### 3.1 Tenancy (`Tellma.Core.Abstractions.Tenancy`)

```csharp
namespace Tellma.Core.Abstractions.Tenancy;

/// <summary>Whether a tenant's actions may reach the outside world.</summary>
public enum TenantCategory
{
    /// <summary>A real business unit; external side effects are real.</summary>
    Live = 1,
    /// <summary>A test copy; external side effects are routed to sandbox channels or withheld.</summary>
    Sandbox = 2,
}

/// <summary>The routing state of a tenant, owned and enforced by the distribution.</summary>
public enum TenantState
{
    /// <summary>Registered; the database is being created and converged. Requests answer 503.</summary>
    Provisioning = 0,
    /// <summary>Serving traffic.</summary>
    Active = 1,
    /// <summary>Soft suspension: read endpoints serve, write endpoints answer 403, background work pauses.</summary>
    ReadOnly = 2,
    /// <summary>Hard suspension: every request answers 403; data retained.</summary>
    Suspended = 3,
    /// <summary>Detached from routing for good; answers 404 like an unknown id. Data retained.</summary>
    Retired = 4,
}

/// <summary>Where a tenant's database is. Never carries a credential.</summary>
/// <param name="Server">Server name or address as SqlClient expects it (host, or host,port).</param>
/// <param name="Database">Database name.</param>
/// <param name="CredentialProfile">Configured credential-profile name; null selects "default".</param>
public sealed record TenantDatabaseLocation(string Server, string Database, string? CredentialProfile);

/// <summary>An immutable view of one catalog row, as held by the registry snapshot.</summary>
/// <param name="Id">The tenant id — the first path segment of every tenant URL.</param>
/// <param name="Name">Display name mirrored from the tenant's settings.</param>
/// <param name="Category">Live or Sandbox.</param>
/// <param name="State">Routing state.</param>
/// <param name="LiveTenantId">For a sandbox, the live tenant it derives from; null for live tenants.</param>
/// <param name="Location">Database location.</param>
/// <param name="Version">Opaque stamp of this row; changes whenever any column changes.</param>
public sealed record TenantDescriptor(
    int Id, string Name, TenantCategory Category, TenantState State, int? LiveTenantId,
    TenantDatabaseLocation Location, Guid Version);

/// <summary>The ambient tenant of the current operation.</summary>
public interface ITenantContext
{
    /// <summary>True when the operation runs for a tenant.</summary>
    bool HasTenant { get; }

    /// <summary>The tenant; throws <see cref="InvalidOperationException"/> when <see cref="HasTenant"/> is false.</summary>
    TenantDescriptor Tenant { get; }
}

/// <summary>Read side of the tenant catalog: an in-memory snapshot, no I/O on any member except <see cref="RefreshAsync"/>.</summary>
public interface ITenantRegistry
{
    /// <summary>All tenants in the snapshot, including retired ones.</summary>
    IReadOnlyCollection<TenantDescriptor> Tenants { get; }

    /// <summary>The catalog version the snapshot was loaded from; <see cref="Guid.Empty"/> before the first load.</summary>
    Guid SnapshotVersion { get; }

    /// <summary>When the snapshot was last loaded successfully (UTC); null before the first load.</summary>
    DateTimeOffset? SnapshotLoadedAt { get; }

    /// <summary>Finds a tenant by id, or null.</summary>
    TenantDescriptor? Find(int tenantId);

    /// <summary>Gets a tenant by id; throws <see cref="TenantNotFoundException"/>.</summary>
    TenantDescriptor Get(int tenantId);

    /// <summary>Reloads the snapshot from the catalog if its version changed (or unconditionally when <paramref name="force"/>). Single-flight.</summary>
    Task RefreshAsync(bool force, CancellationToken cancellationToken);

    /// <summary>Raised after a reload that changed any descriptor; carries the changed tenant ids.</summary>
    event Action<IReadOnlyCollection<int>>? Changed;
}

/// <summary>Governs which registrations <see cref="ITenantCatalog.RegisterAsync"/> accepts.</summary>
public enum TenantRegistrationPolicy
{
    /// <summary>One live tenant plus any number of sandboxes derived from it.</summary>
    SingleLive = 0,
    /// <summary>Any number of live tenants, each with its own sandboxes.</summary>
    MultiLive = 1,
}

/// <summary>A registration request; the location may be omitted to take the deployment's defaults.</summary>
public sealed record TenantRegistration(string Name, TenantCategory Category, int? LiveTenantId, TenantDatabaseLocation? Location);

/// <summary>Write side of the tenant catalog. Every member runs one catalog transaction, bumps the catalog version, and refreshes the local registry.</summary>
public interface ITenantCatalog
{
    /// <summary>The policy in force.</summary>
    TenantRegistrationPolicy RegistrationPolicy { get; }

    /// <summary>Registers a tenant in the Provisioning state. Throws <see cref="TenantRegistrationRefusedException"/> under policy.</summary>
    Task<TenantDescriptor> RegisterAsync(TenantRegistration registration, string actor, CancellationToken cancellationToken);

    /// <summary>Moves a tenant to <paramref name="state"/>; illegal transitions throw <see cref="InvalidOperationException"/>.</summary>
    Task<TenantDescriptor> SetStateAsync(int tenantId, TenantState state, string reason, string actor, CancellationToken cancellationToken);

    /// <summary>Updates the mirrored display name (called by the settings save of the tenant, post-commit).</summary>
    Task RenameAsync(int tenantId, string name, CancellationToken cancellationToken);

    /// <summary>Changes the database location (dedicated-server promotion, on-prem moves).</summary>
    Task RelocateAsync(int tenantId, TenantDatabaseLocation location, string actor, CancellationToken cancellationToken);
}

/// <summary>One row of the caller's tenant list.</summary>
public sealed record TenantMembership(TenantDescriptor Tenant, bool IsActive);

/// <summary>One mirror row to record; the row shape of the [catalog].[TenantMembershipList] table type.</summary>
public sealed class TenantMembershipRecord
{
    /// <summary>Tenant id.</summary>
    public int TenantId { get; set; }
    /// <summary>The identity subject (36-character GUID string).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(36)]
    public string Subject { get; set; } = null!;
    /// <summary>Whether the user is active in the tenant.</summary>
    public bool IsActive { get; set; }
}

/// <summary>The best-effort "which tenants am I a member of" mirror in the catalog.</summary>
public interface ITenantMembershipDirectory
{
    /// <summary>The subject's memberships, excluding retired tenants; one catalog query.</summary>
    Task<IReadOnlyList<TenantMembership>> ListAsync(string subject, CancellationToken cancellationToken);

    /// <summary>Upserts mirror rows in one TVP statement. Failures are logged and metered, never thrown to a save pipeline.</summary>
    Task RecordAsync(IReadOnlyCollection<TenantMembershipRecord> records, CancellationToken cancellationToken);
}

/// <summary>Thrown by <see cref="ITenantRegistry.Get"/> for an unknown id.</summary>
public sealed class TenantNotFoundException(int tenantId) : Exception($"Tenant {tenantId} is not registered.")
{
    /// <summary>The id that was requested.</summary>
    public int TenantId { get; } = tenantId;
}

/// <summary>Thrown when the registration policy refuses a registration.</summary>
public sealed class TenantRegistrationRefusedException(string message) : Exception(message);

/// <summary>Instrument and tag names of the tenancy meter (meter name "Tellma.Core").</summary>
public static class TenancyTelemetryNames
{
    public const string MeterName = "Tellma.Core";
    public const string Resolution = "tellma.tenancy.resolution";
    public const string CatalogRefreshDuration = "tellma.tenancy.catalog.refresh.duration";
    public const string CatalogSnapshotAge = "tellma.tenancy.catalog.snapshot.age";
    public const string CatalogTenants = "tellma.tenancy.catalog.tenants";
    public const string ConnectionFactories = "tellma.tenancy.connection.factories";
    public const string MembershipLookupDuration = "tellma.tenancy.membership.lookup.duration";
    public const string MembershipMirrorWrite = "tellma.tenancy.membership.mirror_write";
    public const string OutcomeTag = "outcome";
    public const string CategoryTag = "category";
    public const string StateTag = "state";
}
```

### 3.2 Operation context (`Tellma.Core.Abstractions.Context`)

```csharp
namespace Tellma.Core.Abstractions.Context;

/// <summary>What kind of unit of work the context describes.</summary>
public enum OperationKind
{
    /// <summary>An HTTP request or an MCP tool call.</summary>
    Request = 0,
    /// <summary>A background job execution.</summary>
    Job = 1,
    /// <summary>Migrator or startup work.</summary>
    Startup = 2,
}

/// <summary>Who the caller is at the credential level. The tenant-local user is resolved later by the connect step.</summary>
public enum CallerKind
{
    /// <summary>A human, authenticated by the session cookie or a user bearer token.</summary>
    User = 0,
    /// <summary>A service account (client-credentials token).</summary>
    ServiceAccount = 1,
    /// <summary>The platform itself (seeds, built-in schedules).</summary>
    System = 2,
}

/// <summary>The assurance facts the identity server signed for this session.</summary>
/// <param name="Acr">The achieved tier, e.g. "urn:tellma:acr:aal2"; null for service accounts.</param>
/// <param name="AuthTime">When the user last authenticated by any method.</param>
/// <param name="AcrAuthTime">When the evidence behind <paramref name="Acr"/> was demonstrated (claim "tellma_acr_auth_time").</param>
public sealed record AuthenticationAssurance(string? Acr, DateTimeOffset? AuthTime, DateTimeOffset? AcrAuthTime);

/// <summary>The authenticated caller as known before any tenant database is consulted.</summary>
/// <param name="Subject">The stable identity subject ("sub"), or the client id of a service account.</param>
/// <param name="Kind">User, service account or system.</param>
/// <param name="ClientId">The OAuth client that presented the credential (BFF slug, "tellma-cli", a service-account id).</param>
/// <param name="SessionId">The identity session "sid"; null for tokens without one.</param>
/// <param name="Assurance">Signed assurance facts.</param>
/// <param name="ClientTag">Free-text client tag from the X-Tellma-Client header, for logs only; never a metric tag.</param>
public sealed record CallerIdentity(
    string Subject, CallerKind Kind, string? ClientId, string? SessionId, AuthenticationAssurance Assurance, string? ClientTag);

/// <summary>The ambient caller.</summary>
public interface ICallerContext
{
    /// <summary>True when a credential authenticated the operation.</summary>
    bool IsAuthenticated { get; }

    /// <summary>The caller; throws when <see cref="IsAuthenticated"/> is false.</summary>
    CallerIdentity Caller { get; }

    /// <summary>The tenant-local user id once the connect step has resolved it; null before.</summary>
    int? UserId { get; }
}

/// <summary>Formatting and calendar facts negotiated for the operation (rules owned by the settings and web specs).</summary>
/// <param name="Culture">UI/message and formatting culture; never carries a "-u-" extension.</param>
/// <param name="CalendarCode">Calendar code: "gc" (Gregorian), "uq" (Umm Al Qura), "et" (Ethiopian).</param>
/// <param name="TimeZone">The caller's IANA time zone, falling back to the tenant's.</param>
/// <param name="ClientToday">The client's "today" if it sent one; the host binds today() from it, else from <paramref name="TimeZone"/>.</param>
public sealed record LocaleContext(
    System.Globalization.CultureInfo Culture, string CalendarCode, TimeZoneInfo TimeZone, DateOnly? ClientToday);

/// <summary>The ambient locale.</summary>
public interface ILocaleContext
{
    /// <summary>The negotiated locale.</summary>
    LocaleContext Locale { get; }
}

/// <summary>The copyable value of an operation context: what a job needs to run as the request that enqueued it.</summary>
/// <param name="TenantId">The tenant id, or null for tenant-less work.</param>
/// <param name="Caller">The caller, or null for anonymous or system work.</param>
/// <param name="Locale">The locale.</param>
/// <param name="Kind">The kind of operation.</param>
public sealed record OperationDescriptor(int? TenantId, CallerIdentity? Caller, LocaleContext Locale, OperationKind Kind);

/// <summary>The whole ambient context of the current operation. Scoped; initialised exactly once per scope.</summary>
public interface IOperationContext : Tenancy.ITenantContext, ICallerContext, ILocaleContext
{
    /// <summary>The kind of operation.</summary>
    OperationKind Kind { get; }

    /// <summary>A copy suitable for enqueueing a job (the tenant by id, not by descriptor).</summary>
    OperationDescriptor Snapshot();
}
```

`Tellma.Core.Context.OperationContextHolder` (scoped) implements `IOperationContext` and
`ISandboxContext`; it exposes `Initialize(OperationDescriptor, TenantDescriptor?)` (throws on a second
call), `SetUserId(int)` (called once by the connect step), and `SetLocale(LocaleContext)` (the
negotiation may complete after the tenant is known, because tenant defaults participate).

### 3.3 Connection factory (`Tellma.Core.Tenancy`, consumed by spec 0011's batch executor)

```csharp
namespace Tellma.Core.Tenancy;

/// <summary>Opens connections to tenant databases from the registry snapshot and the configured credential profiles.</summary>
public interface ITenantConnectionFactory
{
    /// <summary>The composed, validated connection string for the tenant (cached per descriptor version). Never logged as-is.</summary>
    string GetConnectionString(int tenantId);

    /// <summary>Opens a pooled connection to the tenant database. The caller owns disposal.</summary>
    ValueTask<Microsoft.Data.SqlClient.SqlConnection> OpenAsync(int tenantId, CancellationToken cancellationToken);

    /// <summary>Opens a pooled connection to the catalog database.</summary>
    ValueTask<Microsoft.Data.SqlClient.SqlConnection> OpenCatalogAsync(CancellationToken cancellationToken);
}

/// <summary>One pooled EF factory per tenant, sharing the compiled model across tenants.</summary>
public interface ITenantDbContextFactory<TContext> where TContext : Microsoft.EntityFrameworkCore.DbContext
{
    /// <summary>A context bound to the tenant's database; disposal returns it to that tenant's pool.</summary>
    TContext Create(int tenantId);
}
```

### 3.4 Composition (`Tellma.Core.Abstractions.Composition`)

```csharp
namespace Tellma.Core.Abstractions.Composition;

/// <summary>Lifetimes, mirroring Microsoft.Extensions.DependencyInjection without referencing it.</summary>
public enum FeatureServiceLifetime { Singleton = 0, Scoped = 1, Transient = 2 }

/// <summary>A composable unit: declares reflectable facts, then contributes registrations.</summary>
public interface ITellmaFeature
{
    /// <summary>Declares the feature's name and edges. Runs before any container exists.</summary>
    void Declare(FeatureDeclaration declaration);

    /// <summary>Contributes services, model configurations and typed items. Runs once, after validation.</summary>
    void Contribute(IFeatureContribution contribution);
}

/// <summary>Declares a hard dependency on another feature. Equivalent to <see cref="FeatureDeclaration.Requires{TFeature}"/>.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresAttribute<TFeature> : Attribute where TFeature : ITellmaFeature;

/// <summary>The reflectable facts of a feature.</summary>
public sealed class FeatureDeclaration
{
    /// <summary>Unique feature name; defaults to the type's full name.</summary>
    public string Name { get; set; }

    /// <summary>The feature is broken without <typeparamref name="TFeature"/>; a missing target is a startup error.</summary>
    public void Requires<TFeature>() where TFeature : ITellmaFeature;

    /// <summary>The declared edges.</summary>
    public IReadOnlyCollection<Type> RequiredFeatures { get; }
}

/// <summary>Receives a feature's registrations.</summary>
public interface IFeatureContribution
{
    /// <summary>Registers a service by implementation type.</summary>
    void AddService(Type serviceType, Type implementationType, FeatureServiceLifetime lifetime);

    /// <summary>Registers a service by factory.</summary>
    void AddService(Type serviceType, Func<IServiceProvider, object> factory, FeatureServiceLifetime lifetime);

    /// <summary>Registers an EF entity configuration type (must implement IEntityTypeConfiguration&lt;T&gt;); applied by the tenant DbContext.</summary>
    void AddModelConfiguration(Type entityTypeConfiguration);

    /// <summary>Adds a typed contribution item (securables, endpoint mappers, seeds, schedules, table types) for the package that defines the type to consume.</summary>
    void Add<TItem>(TItem item) where TItem : notnull;
}

/// <summary>A check that runs at host start; every failure across all checks is aggregated into one exception.</summary>
public interface IStartupCheck
{
    /// <summary>Short name for diagnostics.</summary>
    string Name { get; }

    /// <summary>Returns violation messages; an empty result passes.</summary>
    Task<IReadOnlyList<string>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>One composition or startup violation.</summary>
/// <param name="Source">The feature or check that raised it.</param>
/// <param name="Problem">What is wrong.</param>
/// <param name="SuggestedFix">What to change.</param>
public sealed record CompositionViolation(string Source, string Problem, string? SuggestedFix);

/// <summary>Thrown once with every violation, before the host serves traffic.</summary>
public sealed class TellmaCompositionException(IReadOnlyList<CompositionViolation> violations)
    : Exception(FormatMessage(violations))
{
    /// <summary>All violations, in discovery order.</summary>
    public IReadOnlyList<CompositionViolation> Violations { get; } = violations;
    private static string FormatMessage(IReadOnlyList<CompositionViolation> violations) => /* one line per violation */ string.Empty;
}
```

`Tellma.Core.Composition` (in `Tellma.Core`):

```csharp
namespace Tellma.Core.Composition;

/// <summary>The composition root. Registers the deployment identity, the catalog, the registry, the tenant DbContext, the context holder and the startup gate.</summary>
public static class TellmaServiceCollectionExtensions
{
    /// <param name="slug">The distribution slug: a compile-time constant of the composition (validated like DeploymentIdentity.Application).</param>
    public static IServiceCollection AddTellma(
        this IServiceCollection services, string slug, IHostEnvironment environment, IConfiguration configuration,
        Action<TellmaBuilder> configure);
}

/// <summary>The selection site: which features and which tenant DbContext.</summary>
public sealed class TellmaBuilder
{
    public TellmaBuilder AddFeature<TFeature>() where TFeature : ITellmaFeature, new();
    public TellmaBuilder AddFeature(ITellmaFeature feature);
    /// <summary>The tenant DbContext; one pooled factory per tenant. Extra options (interceptors, logging) may be added.</summary>
    public TellmaBuilder UseDbContext<TContext>(Action<DbContextOptionsBuilder>? configure = null) where TContext : DbContext;
}

/// <summary>What the features contributed, in order; consumed by Core (model), by MapTellma (endpoints) and by other specs' packages.</summary>
public sealed class TellmaFeatureRegistry
{
    public IReadOnlyList<Type> ModelConfigurations { get; }
    public IReadOnlyList<TItem> GetItems<TItem>() where TItem : notnull;
    /// <summary>Applies every contributed configuration; called from the tenant DbContext's OnModelCreating.</summary>
    public void ApplyModel(ModelBuilder modelBuilder);
}

/// <summary>Host-free validation for tests and the migrator.</summary>
public static class TellmaComposition
{
    public static IReadOnlyList<CompositionViolation> Validate(Action<TellmaBuilder> configure);
}
```

The distribution's selection site and its two call sites:

```csharp
// distributions/acme/src/Tellma.Distro.Acme.Web/AcmeComposition.cs
public static class AcmeComposition
{
    public const string Slug = "acme";
    public static void Configure(TellmaBuilder tellma)
    {
        tellma.UseDbContext<AcmeDbContext>();
        tellma.AddFeature<CoreFeature>();      // users, roles, settings — spec 0013/0017
        tellma.AddFeature<GlModuleFeature>();  // Tellma.Module.Gl — spec 0017
        tellma.AddFeature<AcmeFeature>();      // this distribution's entities and services
    }
}

// Program.cs (Web)
builder.Services.AddTellma(AcmeComposition.Slug, builder.Environment, builder.Configuration, AcmeComposition.Configure);
builder.Services.AddTellmaAspNetCore(builder.Configuration);
builder.Services.AddTellmaEmail(); builder.Services.AddSmtpEmail(builder.Configuration);   // per spec 0007
WebApplication app = builder.Build();
app.UseTellma();                 // UseAuthentication → TenantResolutionMiddleware → UseAuthorization
RouteGroupBuilder web = app.MapTellma();   // BFF, contract, admin, health, the tenant groups; returns /{tenantId:int}/api/web
web.MapPost("reports/aging", AgingReportEndpoint.Handle);   // a custom endpoint inherits every filter
app.Run();

// Program.cs (Migrator)
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTellma(AcmeComposition.Slug, builder.Environment, builder.Configuration, AcmeComposition.Configure);
builder.Services.AddTellmaMigrator<AcmeDbContext>();
return await builder.Build().RunTellmaMigratorAsync(args);
```

### 3.5 Provisioning and seeds (`Tellma.Core.Abstractions.Provisioning`)

```csharp
namespace Tellma.Core.Abstractions.Provisioning;

/// <summary>What a provisioning step sees.</summary>
public sealed record TenantProvisioningContext(
    Tenancy.TenantDescriptor Tenant, Tenancy.TenantDatabaseLocation Location, IServiceProvider Services);

/// <summary>A distribution- or pack-specific provisioning action. Idempotent; runs after migrations and seeds, in <see cref="Order"/>.</summary>
public interface ITenantProvisioningStep
{
    /// <summary>Lower runs first; platform steps use 0–99, packs 100–199, distributions 200+.</summary>
    int Order { get; }
    Task ExecuteAsync(TenantProvisioningContext context, CancellationToken cancellationToken);
}

/// <summary>Requests provisioning of a registered tenant; the default implementation records the request and returns.</summary>
public interface ITenantProvisioningTrigger
{
    Task RequestAsync(int tenantId, CancellationToken cancellationToken);
}

/// <summary>A versioned data seed applied through the bulk save pipeline and tracked in __SeedHistory.</summary>
public interface IDataSeed
{
    /// <summary>Stable id, e.g. "core.currencies.v1".</summary>
    string Id { get; }
    Task ApplyAsync(TenantProvisioningContext context, CancellationToken cancellationToken);
}
```

### 3.6 Web hosting (`Tellma.Core.AspNetCore`)

```csharp
namespace Tellma.Core.AspNetCore;

public static class TellmaAspNetCoreServiceCollectionExtensions
{
    /// <summary>Cookie + OIDC + JWT bearer schemes, the catalog ticket store, CSRF filter, context population, admin and contract endpoints, health checks.</summary>
    public static IServiceCollection AddTellmaAspNetCore(this IServiceCollection services, IConfiguration configuration);
}

public static class TellmaApplicationBuilderExtensions
{
    /// <summary>UseAuthentication, TenantResolutionMiddleware, UseAuthorization — in that order.</summary>
    public static IApplicationBuilder UseTellma(this IApplicationBuilder app);
}

public static class TellmaEndpointRouteBuilderExtensions
{
    /// <summary>Maps every platform endpoint and returns the cookie-authenticated tenant web group for the projection and custom endpoints.</summary>
    public static RouteGroupBuilder MapTellma(this IEndpointRouteBuilder endpoints);

    /// <summary>The bearer-authenticated versioned public group (seam).</summary>
    public static RouteGroupBuilder MapTellmaPublicApi(this IEndpointRouteBuilder endpoints, string version = "v1");
}

/// <summary>Bound from "Tellma:Authentication".</summary>
public sealed class TellmaAuthenticationOptions
{
    public Uri? Authority { get; set; }               // null in in-proc mode (derived from the host)
    public string? ClientId { get; set; }             // defaults to the slug
    public string? ClientSecret { get; set; }         // from Key Vault; ignored in in-proc mode
    public Uri? Origin { get; set; }                  // https://acme.app.tellma.com; used for CSRF and audiences
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan SessionCacheTtl { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan AccessTokenRefreshSkew { get; set; } = TimeSpan.FromSeconds(60);
    public string CsrfHeaderName { get; set; } = "X-Tellma-Client";
}

/// <summary>Notified when sessions end so real-time connections can be closed (implemented by the hub host of the background-tasks spec).</summary>
public interface ISessionTerminationListener
{
    Task SessionsTerminatedAsync(string subject, IReadOnlyCollection<string> sessionKeys, CancellationToken cancellationToken);
    Task TenantAccessRevokedAsync(int tenantId, string subject, CancellationToken cancellationToken);
}

/// <summary>The body of GET /api/distribution-info.</summary>
public sealed record DistributionInfo(
    string Slug, string DisplayName, string DeploymentId, string Environment, string Version, string PlatformVersion,
    Uri Authority, string ClientId, IReadOnlyList<string> Surfaces, Tenancy.TenantRegistrationPolicy RegistrationPolicy);
```

### 3.7 Shapes needed from other themes

- From spec 0013 (permissions): the securable endpoint metadata type (`ISecurableMetadata` or an
  attribute implementing `IAuthorizationRequirementData`) and a `[TellmaPublic]` marker, so the
  startup audit can assert every endpoint in the three tenant groups carries one or the other and none
  carries `AllowAnonymous`; the connect step as `IConnectStep.ConnectAsync(IOperationContext, …)` that
  calls `OperationContextHolder.SetUserId`.
- From spec 0011 (data access): the batch executor takes `ITenantConnectionFactory` (§3.3) and the
  standalone table type `[catalog].[TenantMembershipList]` registered through `HasTableType<TenantMembershipRecord>`
  on `CatalogDbContext`.
- From spec 0012/0015: the locale negotiation function `LocaleContext Negotiate(HttpRequest, TenantDescriptor, UserPreferences?)`
  the middleware calls, and the header names (`Accept-Language`, `X-Tellma-Calendar`,
  `X-Tellma-Time-Zone`, `X-Tellma-Today` — names to be fixed by 0015).
- From spec 0019: the job runner's use of `OperationDescriptor` and `OperationContextHolder.Initialize`,
  the `ISessionTerminationListener` implementation over the hub, and the two catalog consumers (session
  sweep, membership reconciliation).

---

## 4. Schema

All tables live in the **catalog database**, schema `catalog`, created by `Tellma.Core.Migrations`.
Conventions: no IDENTITY; app-assigned ids from `catalog.sq_Tenants` (`START WITH 1000`, seed band
below 1000 reserved); `datetime2(3)` UTC timestamps; explicit constraint names. No temporal tables (the
catalog's audit is `TenantStateChanges`).

```sql
CREATE SEQUENCE [catalog].[sq_Tenants] AS int START WITH 1000 INCREMENT BY 1;
CREATE SEQUENCE [catalog].[sq_TenantStateChanges] AS bigint START WITH 1 INCREMENT BY 1;

CREATE TABLE [catalog].[CatalogState] (
    [Id]         int              NOT NULL CONSTRAINT [PK_CatalogState] PRIMARY KEY,
    [Version]    uniqueidentifier NOT NULL,
    [ModifiedAt] datetime2(3)     NOT NULL,
    CONSTRAINT [CK_CatalogState_SingleRow] CHECK ([Id] = 1)
);
-- seeded row: (1, NEWID(), SYSUTCDATETIME())
```

```sql
CREATE TABLE [catalog].[Tenants] (
    [Id]                int           NOT NULL CONSTRAINT [PK_Tenants] PRIMARY KEY,
    [Name]              nvarchar(255) NOT NULL,
    [Category]          varchar(16)   NOT NULL,   -- 'Live' | 'Sandbox'
    [State]             varchar(16)   NOT NULL,   -- 'Provisioning' | 'Active' | 'ReadOnly' | 'Suspended' | 'Retired'
    [LiveTenantId]      int           NULL,
    [Server]            nvarchar(255) NOT NULL,
    [Database]          nvarchar(128) NOT NULL,
    [CredentialProfile] varchar(64)   NULL,
    [Version]           uniqueidentifier NOT NULL,          -- per-row stamp; NEWID() on every update
    [CreatedAt]         datetime2(3)  NOT NULL,
    [ModifiedAt]        datetime2(3)  NOT NULL,
    CONSTRAINT [FK_Tenants_LiveTenant] FOREIGN KEY ([LiveTenantId]) REFERENCES [catalog].[Tenants]([Id]),
    CONSTRAINT [CK_Tenants_Category]   CHECK ([Category] IN ('Live', 'Sandbox')),
    CONSTRAINT [CK_Tenants_State]      CHECK ([State] IN ('Provisioning', 'Active', 'ReadOnly', 'Suspended', 'Retired')),
    CONSTRAINT [CK_Tenants_SandboxLink] CHECK (([Category] = 'Sandbox' AND [LiveTenantId] IS NOT NULL)
                                            OR ([Category] = 'Live'    AND [LiveTenantId] IS NULL)),
    CONSTRAINT [UX_Tenants_Location]   UNIQUE ([Server], [Database])
);
CREATE INDEX [IX_Tenants_Category_State] ON [catalog].[Tenants]([Category], [State]);
```

```sql
CREATE TABLE [catalog].[TenantStateChanges] (
    [Id]        bigint        NOT NULL CONSTRAINT [PK_TenantStateChanges] PRIMARY KEY,
    [TenantId]  int           NOT NULL CONSTRAINT [FK_TenantStateChanges_Tenant] REFERENCES [catalog].[Tenants]([Id]),
    [FromState] varchar(16)   NULL,      -- null on registration
    [ToState]   varchar(16)   NOT NULL,
    [Reason]    nvarchar(1000) NULL,
    [Actor]     nvarchar(255) NOT NULL,  -- subject, client id, or 'migrator'
    [ChangedAt] datetime2(3)  NOT NULL
);
CREATE INDEX [IX_TenantStateChanges_Tenant] ON [catalog].[TenantStateChanges]([TenantId], [ChangedAt] DESC);
```

```sql
CREATE TABLE [catalog].[TenantMemberships] (
    [Subject]   char(36)     NOT NULL,
    [TenantId]  int          NOT NULL CONSTRAINT [FK_TenantMemberships_Tenant] REFERENCES [catalog].[Tenants]([Id]),
    [IsActive]  bit          NOT NULL,
    [UpdatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_TenantMemberships] PRIMARY KEY CLUSTERED ([Subject], [TenantId])
);
CREATE INDEX [IX_TenantMemberships_Tenant] ON [catalog].[TenantMemberships]([TenantId]);
-- Standalone table type registered by CatalogDbContext through HasTableType<TenantMembershipRecord>:
--   [catalog].[TenantMembershipList] (TenantId int, Subject nvarchar(36), IsActive bit) — physical name carries the content hash.
```

```sql
CREATE TABLE [catalog].[Sessions] (
    [Key]           varchar(64)    NOT NULL CONSTRAINT [PK_Sessions] PRIMARY KEY,   -- 43-char base64url of 32 random bytes
    [Subject]       char(36)       NOT NULL,
    [Sid]           nvarchar(128)  NOT NULL,
    [Ticket]        varbinary(max) NOT NULL,   -- Data-Protection-encrypted serialized AuthenticationTicket
    [TokensVersion] int            NOT NULL,   -- compare-and-set for refresh
    [IssuedAt]      datetime2(3)   NOT NULL,
    [ExpiresAt]     datetime2(3)   NOT NULL,   -- sliding; = last renewal + SessionLifetime
    [LastRenewedAt] datetime2(3)   NOT NULL
);
CREATE INDEX [IX_Sessions_Sid]     ON [catalog].[Sessions]([Sid]);
CREATE INDEX [IX_Sessions_Subject] ON [catalog].[Sessions]([Subject]);
CREATE INDEX [IX_Sessions_Expires] ON [catalog].[Sessions]([ExpiresAt]);
```

Load-bearing statements (all parameterised; `SET XACT_ABORT ON` where a transaction is opened):

```sql
-- D7: refresh probe (one row, one read)
SELECT [Version] FROM [catalog].[CatalogState] WHERE [Id] = 1;

-- D7/D15: state transition (one transaction; the middleware's optimistic expected-state guard is @from)
SET XACT_ABORT ON; BEGIN TRAN;
UPDATE [catalog].[Tenants] SET [State] = @to, [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME()
  WHERE [Id] = @tenantId AND [State] = @from;
IF @@ROWCOUNT <> 1 BEGIN ROLLBACK; THROW 51001, 'Illegal or concurrent tenant state transition.', 1; END
INSERT INTO [catalog].[TenantStateChanges] ([Id], [TenantId], [FromState], [ToState], [Reason], [Actor], [ChangedAt])
  VALUES (NEXT VALUE FOR [catalog].[sq_TenantStateChanges], @tenantId, @from, @to, @reason, @actor, SYSUTCDATETIME());
UPDATE [catalog].[CatalogState] SET [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME() WHERE [Id] = 1;
COMMIT;

-- D10: membership list (one indexed range read + join)
SELECT t.[Id], t.[Name], t.[Category], t.[State], t.[LiveTenantId], t.[Server], t.[Database], t.[CredentialProfile], t.[Version], m.[IsActive]
  FROM [catalog].[TenantMemberships] m JOIN [catalog].[Tenants] t ON t.[Id] = m.[TenantId]
 WHERE m.[Subject] = @subject AND t.[State] <> 'Retired';

-- D10: membership upsert from the table type (no MERGE)
UPDATE m SET m.[IsActive] = r.[IsActive], m.[UpdatedAt] = SYSUTCDATETIME()
  FROM [catalog].[TenantMemberships] m JOIN @rows r ON r.[TenantId] = m.[TenantId] AND r.[Subject] = m.[Subject]
 WHERE m.[IsActive] <> r.[IsActive];
INSERT INTO [catalog].[TenantMemberships] ([Subject], [TenantId], [IsActive], [UpdatedAt])
SELECT r.[Subject], r.[TenantId], r.[IsActive], SYSUTCDATETIME() FROM @rows r
 WHERE NOT EXISTS (SELECT 1 FROM [catalog].[TenantMemberships] m WHERE m.[Subject] = r.[Subject] AND m.[TenantId] = r.[TenantId]);

-- D12: ticket retrieve (L1 miss)
SELECT [Subject], [Sid], [Ticket], [TokensVersion], [ExpiresAt] FROM [catalog].[Sessions]
 WHERE [Key] = @key AND [ExpiresAt] > SYSUTCDATETIME();

-- D12: token refresh compare-and-set (zero rows = lost the race; re-read and adopt)
UPDATE [catalog].[Sessions]
   SET [Ticket] = @ticket, [TokensVersion] = [TokensVersion] + 1, [ExpiresAt] = @expiresAt, [LastRenewedAt] = SYSUTCDATETIME()
OUTPUT inserted.[TokensVersion]
 WHERE [Key] = @key AND [TokensVersion] = @expectedVersion;

-- D12: back-channel logout
DELETE FROM [catalog].[Sessions] OUTPUT deleted.[Key], deleted.[Subject] WHERE [Sid] = @sid;

-- spec 0019 consumer: session sweep (unordered TOP is fine for a sweep)
DELETE TOP (1000) FROM [catalog].[Sessions] WHERE [ExpiresAt] < DATEADD(day, -1, SYSUTCDATETIME());
```

The in-proc identity engine's schema `idsvr` (its own tables and its own `__EFMigrationsHistory` in
that schema) shares this database in Development and on-prem in-proc deployments only.

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "Are those the proper layer names in a .NET business app?" | Yes as platform tiers; in distribution code they are folders `Entities/`, `Services/`, `Endpoints/` (D2). |
| "Every request comes with a tenantId as part of the url" — shape? | `int`, first segment, `:int` constraint: `/{tenantId:int}/api/web/…` (D5). |
| "Single live tenant … connection string defined in config" | The catalog is a dedicated database in both shapes; only `SingleLive` policy differs; connection strings are composed from a credential-profile template in config, the catalog holds locations (D6, D8). |
| "Tenant-to-db map lives in a catalog db (where do we store passwords?)" | There are no per-tenant passwords: managed identity in SaaS, one login on-prem, nothing in any table (D9). |
| "Is the TenantRegistry the right shape?" | Split into `ITenantRegistry` (snapshot reads), `ITenantCatalog` (writes), `ITenantConnectionFactory` (strings and connections) (D7, D8, §3). |
| "Tenant routing … benefits from aggressive caching" | In-memory snapshot; one single-row version read per instance per 15 s; immediate local refresh on writes (D7). |
| "Keys and secrets are never stored in the DB in clear text" | Never stored in the DB at all; startup rejects unresolved Key Vault references (D9). |
| "API to determine the tenants I'm a member of … fan out for single-live" | Catalog mirror for both shapes, self-healing at connect, nightly reconciliation; no fan-out (D10). |
| "One MCP server per tenant, or one for the entire distro?" | Per tenant at `/{tenantId}/mcp`; two servers for two tenants; audience = origin or the endpoint URL (D16). |
| "Extend the TenantRegistry to blob storage connection strings, Key Vault?" | No: blob scoping is configuration + ambient tenant (spec 0016); Key Vault is a configuration provider (D9). |
| "Extend the TenantRegistry to support provisioning new tenants?" | A separate seam: `ITenantCatalog.RegisterAsync` + migrator `provision` + `ITenantProvisioningStep` for distro-specific work + `ITenantProvisioningTrigger` (D17). |
| "Do we need shared coordination state in the catalog DB for background tasks?" | No; the catalog stays read-mostly; leasing is per tenant database (critique §1.8; spec 0019). |
| "Should we accept an X-Today header … or the user's time zone?" | Both travel in `LocaleContext` (`TimeZone`, `ClientToday`); header names and precedence are spec 0015/0012's; `today()` binds from `ClientToday` when sent, else from `TimeZone` (§3.2). |
| "Background tasks are isolated by tenant" (context in jobs) | `OperationDescriptor` copied at enqueue, holder initialised per job scope, tenant re-resolved from the registry (D11). |
| (breakdown) "Sandbox context implementation" | `OperationContextHolder` implements `ISandboxContext` from `Tenant.Category`; tenant-less scopes throw (D11). |
| (breakdown) "Distribution-side authentication as an OIDC relying party with the BFF cookie" | Cookie + OIDC handlers, catalog ticket store with L1, store-coordinated refresh, back-channel logout endpoint, local/global logout, step-up challenge (D12). |
| (briefing) "CSRF posture" | Required `X-Tellma-Client` header + `Origin`/`Sec-Fetch-Site` + JSON-only + no CORS; no antiforgery tokens (D13). |
| (briefing) "Session revocation on user deactivation" | Tenant-level (permissions tag + mirror + hub close), not cookie-level (D14). |
| (briefing) "Suspension hook" | `TenantState` transitions with history and version bump; `ReadOnly`/`Suspended`/`Retired` verdicts; admin surface (D15). |
| (briefing) "The distribution contract surface" | `/api/distribution-info`, `/healthz`, `/readyz`, `/api/me/tenants`, `/api/admin/*` (D18). |
| (briefing) "Where the reference distribution lives; slug; reserved-slug implications" | `distributions/acme/`; `acme` becomes reserved (D1). |
| (briefing) "`AddTellma` at minimal fidelity … one shape for module package and stack feature" | BCL-only `ITellmaFeature` with `Requires` edges and a typed contribution bag; one aggregated exception; explicit selection (D4). |

---

## 6. Seams

1. **Batch abstraction (T2 owns).** This theme supplies `ITenantConnectionFactory.OpenAsync(tenantId)`
   (§3.3) as the only way a batch reaches a tenant database, and requires the executor to take the
   tenant id from `ITenantContext` rather than from a parameter, so no code path can execute a batch
   against a tenant other than the ambient one. The catalog's own statements (§4) are executed through
   the same executor against `OpenCatalogAsync`, which is how the membership TVP rides T2's metadata-
   driven binding. Requirement on T2: `SqlConnection` obtained from the factory is opened with the
   pooled string; the executor never sets `ChangeDatabase`/`USE` (unsupported on Azure SQL).
2. **Entity class vs wire shape (T2/T5/T6).** Not touched.
3. **One capability, declared once (T5).** Touched only through composition: a capability's
   contributions (columns via model configuration, permission actions, endpoint mappers) are typed
   items added through `IFeatureContribution.Add<TItem>`; the stack feature is an `ITellmaFeature`.
4. **Queryex schema per tenant configuration (T2/T3/T4).** The schema cache key must include the
   `TenantDescriptor.Id`; this theme guarantees the descriptor is available before any handler runs.
5. **Version tags (T3).** The catalog version (`CatalogState.Version`, a GUID) follows the same
   opaque-GUID convention the settings tags should use; the catalog is not a version-tag home for tenant
   data.
6. **Feature composition seam (T1 owns).** Contract in §3.4. T5's stack feature and T8's module feature
   implement `ITellmaFeature`; T4 contributes securables via `Add<SecurableDefinition>`; T6 contributes
   endpoint mappers via `Add<IEndpointMapper>`-style items consumed by `MapTellma`; T10 contributes
   schedules; T8 contributes `IDataSeed`s.
7. **Natural keys.** Not touched.
8. **Background-task columns and lease statements (T10/T2).** The job runner opens each job scope
   through `IServiceScopeFactory`, calls `OperationContextHolder.Initialize(descriptor, registry.Find(tenantId))`,
   and skips (re-leases later) tenants whose state is not `Active`. Every job row stores the
   `OperationDescriptor` fields it needs (`TenantId`, `Subject`, `CallerKind`, culture name,
   calendar code, time-zone id) — never a serialized descriptor object.
9. **Request context (T1 owns).** Contract in §3.2 and D11: scoped holder, initialise-once, no
   `AsyncLocal`, late-bound `UserId`, `Snapshot()` for enqueueing.
10. **Platform exceptions and HTTP mapping (T5/T6).** This theme adds `TenantNotFoundException`,
    `TenantRegistrationRefusedException`, and the tenant-state verdicts (`404 tenant-not-found`,
    `503 tenant-provisioning`, `403 tenant-suspended`, `403 tenant-read-only`), plus the step-up
    challenge writer; the problem-details vocabulary is T6's to align.
11. **Permission evaluation API (T4).** Consumes `ICallerContext.Caller` (subject, kind, assurance) and
    `ICallerContext.UserId`; the connect step sets `UserId` through the holder. The endpoint audit needs
    T4's securable metadata type.
12. **Blob staging tokens (T7).** Not touched, except that the upload endpoint joins the CSRF filter with
    `AcceptsMultipart`.
13. **Wire shapes (T6).** `/api/me/tenants`, `/api/distribution-info` and the admin bodies (D15, D18)
    follow T6's JSON conventions (camelCase, source-generated).
14. **Telemetry names (T2 owns the DB-call budget).** This theme's instruments are in §3.1 and D19; the
    catalog reads made by the ticket store and registry are attributed to `db.name = <catalog>` by the
    SqlClient instrumentation, so the budget instruments must exclude catalog statements from the
    per-request tenant budget (tag `tellma.db.role = catalog | tenant` on the executor's span).
15. **Notification enqueue riding the save batch (T10).** Not touched.
16. **Connect-call collapse (T4/T5).** Enabled by the late-bound `UserId` and the fact that no catalog
    or identity call happens per request; the first tenant round trip is the first round trip.
17. **Vocabulary.** Catalog tables are plural (`Tenants`, `Sessions`) in schema `catalog`; audit is
    `CreatedAt/ModifiedAt` (no `SavedBy` — the actor is in `TenantStateChanges`); ids `int` for tenants,
    `bigint` for the history table.

---

## 7. Departures from ARCHITECTURE.md

| Departure | Reason |
|---|---|
| Reference distribution at `distributions/acme/`; `samples/tellma-sample-distribution/` removed from the layout tree; `acme` added to reserved slugs | The phasing text already says `distributions/<slug>/`; a deployable smoke distribution must be a real distribution (D1). |
| New Core-layer packages `Tellma.Core.AspNetCore`, `Tellma.Core.Migrations`, `Tellma.Core.Migrator`; `Tellma.Core.Webhooks` is no longer "the one project taking a FrameworkReference" | The web host half of Core cannot live in `Tellma.Core` without dragging ASP.NET into workers and the migrator (D3). |
| `Tellma.Core` → `Tellma.Core.EntityFrameworkCore` → `Microsoft.EntityFrameworkCore.SqlServer` edge added to the dependency graph | The catalog context, the per-tenant pooled factory, and (spec 0011) the Queryex schema adapter and metadata-driven TVP binding all need EF (D3). |
| A platform-owned migrations assembly for the catalog schema, against "only the distribution generates and ships migrations" | The catalog schema is identical across distributions and platform-owned; `Tellma.Identity.Migrations` is the precedent (D3). |
| "Reuses `Tellma.Core`'s sharding code unchanged" | No such code exists; the registry is written fresh and never uses the Elastic Database client library (D6–D8). |
| Feature composition reduced to `Requires` edges, explicit `AddFeature<T>()`, one aggregated exception; `Recommends`/`Excludes`, slots, providers, cardinality, the manifest source generator, the Builder Tool and the bypass analyzer deferred; the contract is BCL-only in Abstractions | Minimal fidelity for the first release; the startup endpoint audit covers the bypass failure mode (D4). |
| "Every distribution reads its connection strings … from its own Key Vault" refined: SaaS holds no tenant connection secrets at all; Key Vault holds the OIDC secrets and the credential-profile template only | Managed identity + contained users (D9). |
| Tenant suspension gains a `ReadOnly` state with write-rejection semantics and pauses background work; `Retired` answers 404 | ARCHITECTURE.md names soft/hard; the verdict table makes them concrete (D15). |
| "Endpoints are generated … read → GET, save → POST, delete → DELETE" | Not decided here (spec 0015), but the CSRF posture assumes the cookie surface is POST-everything with JSON bodies; GET endpoints under cookie auth remain safe either way (D13). |
| Startup does not fail on an unreachable catalog; readiness does | Slot swaps and transient outages must not crash-loop every instance (D4). |
| No landing page in Phase 1 stands; `/api/me/tenants` is the distribution-local tenant picker the landing page will later call | (D10, D18). |

---

## 8. Verification

Facts relied on from `research/host-tenancy.md` (all verified there on 2026-09-01 unless noted):

- ASP.NET Core 9/10 OIDC handler: PAR built in (`PushedAuthorizationBehavior.Require`), no built-in
  BFF, no built-in token refresh (dotnet/aspnetcore#8175 moved to .NET 12 planning), cookie handler
  returns 401/403 on `IApiEndpointMetadata` endpoints (§1.1–1.4).
- RFC 10017 (BCP 212) cookie/CSRF requirements; .NET 10 antiforgery doc's statement that JSON endpoints
  are not rejected by the middleware; OWASP custom-header and Origin guidance (§1.5–1.6).
- `ITicketStore` members in .NET 10; `ValidatePrincipal` per-request cost warning; Duende BFF's
  server-side session design as the reference (§2).
- Finbuckle 10.1.3 facts (EF Core ≥ 10.0.11, per-tenant-DB recipe incompatible with pooling) (§3.1);
  EF pooling documentation (`OnConfiguring` once per pooled instance; `PooledDbContextFactory`
  pattern; `SetConnectionString` caveat); SqlClient pool-per-connection-string, idle drain 4–8 min,
  fragmentation across many databases (§3.2).
- App Service Key Vault references (24-hour cache, literal string on failure); configuration provider
  1.5.2; Key Vault limits (§4.1–4.2).
- Managed identity for Azure SQL (`Active Directory Managed Identity`, contained users, Entra-only
  auth, SqlClient 7.0 split into `Extensions.Azure`), shard-map catalogs store location not credentials,
  on-prem DPAPI is Windows-only, integrated auth from Linux needs Kerberos (§4.3–4.5).
- Elastic pool limits (500 databases GP ≥ 6 vCores; 30,000 sessions; workers/logins per pool; no
  cross-database queries or `USE` on Azure SQL) (§5).
- `AsyncLocal`/`HttpContextAccessor`/`Activity.Current` semantics and the framework guidance against
  capturing request state in background work (§6).
- Route groups with parameters and constraints; endpoint filter ordering; filters see bound arguments
  (§7).

Facts from sibling research files: MCP 2026-07-28 statelessness, RFC 9728 path-insertion PRM, RFC 8707
`resource` requirement, CIMD preference, C# SDK 2.2.0 `MapMcp("/{tenantId}/mcp")`, OpenIddict 7.6.1
having RFC 8707 with static `RegisterResources` and no PRM/DCR/CIMD, per-client registration
requirements (`web-api-mcp.md` §3–5); .NET 10 authorization middleware and `AllowAnonymous` semantics,
`IAuthorizationRequirementData`, the identity invite/delivery-status APIs and the `sub` format
(`users-roles-permissions.md` §2, §5); `-u-ca-` culture trap, no standard time-zone header, IANA ids
(`settings-cache-l10n.md` §3–5); `HostOptions.ShutdownTimeout` 30 s, App Service Linux drain 5 s,
`Always On`, `PeriodicTimer` + `TimeProvider` (`background-inbox.md` §2); RCSI defaults and SqlClient
6.1.x line (`data-access.md` §3.1, §8).

Verified by me on 2026-09-01:

- `OpenIdConnectHandler.HandleRemoteSignOutAsync` (dotnet/aspnetcore `main`): accepts GET and
  form-encoded POST, authenticates the *current request's* cookie via `Context.AuthenticateAsync(Options.SignOutScheme)`,
  compares `sid`/`iss` against that principal, then `SignOutAsync(SignOutScheme)`; the file contains no
  `logout_token` handling — so the built-in `RemoteSignOutPath` is front-channel only and cannot serve
  spec 0003's back-channel POST (D12). `RemoteSignOutPath` API doc: "Requests received on this path
  will cause the handler to invoke SignOut using the SignOutScheme."
- `Microsoft.AspNetCore.Authentication.OpenIdConnect` is a separate NuGet package (not in the shared
  framework); latest stable 10.0.11 (2026-08-11), 10.0.9 (2026-06-09) matches the repo's ASP.NET line (D22).
- `Microsoft.EntityFrameworkCore.SqlServer` 10.0.11 requires `Microsoft.Data.SqlClient >= 6.1.6` (D9).
- EF Core source (checkout at `C:\Users\ahmad\workspace\efcore`, commit 9d1b7959 of 2026-06-09):
  `RelationalOptionsExtension.RelationalExtensionInfo.GetServiceProviderHashCode() => 0` and
  `ShouldUseSameServiceProvider(other) => other is RelationalExtensionInfo`;
  `SqlServerOptionsExtension.ExtensionInfo.ShouldUseSameServiceProvider` compares `EngineType` and the
  three compatibility levels only — a per-tenant connection string does not fragment the internal
  service provider or the model cache (D8).
- Identity engine as implemented: `LogoutTokenFactory` mints `typ=logout+jwt` with `sub`, `sid`,
  `jti`, `events`, 2-minute expiry, `aud = client_id`, signed with the asymmetric signing credential;
  `BackchannelLogoutService` POSTs `logout_token` form-encoded to the client property
  `tellma:backchannel_logout_uri` and treats any non-2xx as a delivery failure; `ClientDescriptorFactory.Distribution`
  registers `/signin-oidc` and `/signout-callback-oidc` on the origin, PAR + PKCE requirements, and
  `resource = origin`; `TellmaIdentityOptions.ConfigureDbContext` lets an in-proc host point the
  `idsvr` schema at any database; the dev admin subject is `00000000-0000-0000-0000-000000000001`;
  the acr-evidence claim is `tellma_acr_auth_time` and tiers are `urn:tellma:acr:aal1..3` (D12, D20).
- Repo state: `Tellma.Core` currently references only `Tellma.Core.Abstractions`; `Directory.Packages.props`
  pins `Microsoft.Data.SqlClient` 6.1.1, `Azure.Extensions.AspNetCore.DataProtection.Blobs/Keys`,
  `Testcontainers.MsSql` 4.13.0, `Microsoft.Extensions.TimeProvider.Testing`; `samples/`, `templates/`
  and `distributions/` are empty; the identity in-proc test host mounts the engine at `/id` with
  `DeploymentIdentity("acme", …)` (D1, D3, D20).

Still unverified or deliberately not measured:

- The catalog refresh probe's cost under Azure SQL serverless auto-pause (a paused catalog database
  resumes on the first probe; readiness handles it, but the resume latency is not measured).
- Whether `PooledDbContextFactory` instances per tenant meaningfully increase memory beyond the pool
  contents at hundreds of tenants (expected negligible; to be measured in the reference deployment).
- The exact `CREATE DATABASE … AS COPY OF` timing and the elastic-pool placement syntax in the
  provisioning template (Azure documentation to be re-read when spec 0010 is written).
- Whether Azure SignalR's per-user close API accepts a per-tenant group (spec 0019's concern; this theme
  assumes per-user-per-tenant groups exist).
- SqlClient token caching behaviour per pool for `Active Directory Managed Identity` (research left it
  open; the design does not depend on it beyond "tokens are cached in memory").
