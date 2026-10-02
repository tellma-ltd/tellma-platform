# Spec: Distribution Host and Multi-Tenancy

- **Author:** Ahmad Akra
- **Date:** 4 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

A Tellma distribution is a deployable ASP.NET Core application that composes the platform's Core
packages, zero or more module packs, and its own entities and logic, and serves many tenants — each
tenant one SQL Server database — behind one public origin. This spec ships the skeleton every later
CRUD-stack spec plugs into: the reference distribution `distributions/acme/` with its composition
library and its Web and Migrator projects, the `AddTellma` composition root and feature contract,
tenant routing and the tenant state model, the catalog database and the in-memory tenant registry,
per-tenant connection resolution without per-tenant secrets, the immutable request context and the
tenant scope for background work, the distribution's own BFF session over the shared OpenID Connect
authority of spec 0003, the CSRF posture of the cookie-authenticated JSON surface, the provisioning
seam and the migrator commands, and the distribution's information, health and admin surfaces.

It builds on four frozen specs. Spec 0001 supplies the table types the catalog and the membership
reconciliation ride on, and the `EXECUTE ON TYPE` grants the migrator emits. Spec 0003 fixes the
distribution's role as a confidential relying party (BFF cookie, refresh-anchored liveness,
back-channel logout by `sid`, the `401 insufficient_user_authentication` step-up challenge) and the
in-proc identity mode a standalone deployment runs. Spec 0007 requires every host to register
`ISandboxContext` explicitly and defines `DeploymentIdentity`. Spec 0008 §10.6 defines `today()` as
the current date in the tenant's zone, which fixes how the request context binds time.

The sibling specs written alongside this one consume what it defines. Spec 0011 opens every
connection through this spec's `ITenantConnectionProvider`; spec 0013 fills the request context's
`UserId` through the connect initializer at `Order` 100 and spec 0012 fills its locale at `Order`
200; spec 0014 reads `RequestContext` in every service; spec 0015 projects endpoints onto the route
groups `MapTellma` returns and maps this spec's exceptions to problem details; spec 0016 mounts the
blob endpoints on the `Blobs` group; spec 0017 ships the first feature (`GlFeature`) and the first
provisioning step; spec 0019 is the only background caller of `ITenantScopeFactory`; spec 0020's hub
implements this spec's two listeners.

Deliberately left to later specs: everything a tenant database contains (spec 0011 onward), the
endpoint projection and the MCP tools (spec 0015), the identity-server amendments the MCP surface
needs (spec 0021), self-serve provisioning beyond
a trigger seam, sandbox cloning, and the manifest generator, Builder tool and bypass analyzer of the
full feature-composition design.

## Goals / Non-goals

**Goals**

- Ship `Tellma.Core.AspNetCore` and `Tellma.Core.Migrator`, grow `Tellma.Core` into the
  host-agnostic composition root and tenancy runtime, and place every contract a module or
  distribution names in `Tellma.Core.Abstractions`.
- Ship the reference distribution `distributions/acme/` in the shape a customer distribution
  repository takes, deployable as the platform's smoke deployment, with `Program.cs` reduced to
  three platform calls.
- Ship feature composition at minimal fidelity: a BCL-only feature contract, `Requires` edges,
  data-only contribution items realised by Core, and two aggregated gates that report every problem
  at once.
- Ship the catalog database, the tenant registry with a bounded staleness, the tenant state model
  with its verdict table, connection resolution through credential profiles, the `tellma_app`
  database role, and the secrets policy every host obeys.
- Ship the request context, its two writers, the tenant access guard, and the tenant scope factory
  background work rebinds through.
- Ship the distribution's session subsystem: the catalog-backed ticket store, compare-and-set token
  refresh, the back-channel logout receiver, the BFF endpoints, per-surface authentication schemes
  and policies, the CSRF middleware, the step-up challenge, and the endpoint audit that makes an
  unsecured endpoint a startup failure.
- Ship the migrator commands, the provisioning-step seam and its completion table, the platform
  steps' ordering, and the local-development story from a fresh clone.
- Ship `/api/distribution-info`, `/health/live`, `/health/ready`, the minimal admin surface, and the
  per-tenant MCP endpoint topology with its protected-resource metadata document.

**Non-goals (explicitly out of scope)**

- **Tenant database contents** — entity bases, `TellmaDbContext`'s surface beyond registration,
  the batch executor, the emitter (spec 0011).
- **The connect prologue and the securables registry** — the connect initializer's statement, the
  permission cache, `SecurableEndpointMetadata` (spec 0013).
- **Endpoint projection, wire shapes, problem-details bodies, limits, the MCP tools** (spec 0015);
  this spec fixes only the route groups, the schemes and policies, the CSRF rules and the MCP
  endpoint's topology and audience.
- **Locale negotiation and tenant settings** (spec 0012); this spec reserves the context members
  that initializer fills.
- **The hub, its groups and events** (spec 0020); this spec defines the listeners the hub
  implements and the `Hub` route group.
- **Job scopes' contents and the worker** (spec 0019); this spec defines the scope factory and the
  snapshot the worker composes.
- **Self-serve provisioning, sandbox cloning, the manifest source generator, the Builder tool,
  the bypass analyzer, `Recommends`/`Excludes`/slots** — later releases.
- **Identity-server changes** — the `Distribution` seed kind, per-tenant resources, client ID
  metadata documents and the control-plane origin grant are spec 0021's, prerequisites of the MCP
  surface, of sandbox invites and of any caller of the admin surface, not of this spec's
  definition of done.

## 1. Placement and architecture

### 1.1 Packages and dependency edges

Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are omitted,
so a block is never pasted into code. SQL statements are the exact shape to emit.

| Package | Location | References | Owns from this spec |
|---|---|---|---|
| `Tellma.Core.Abstractions` | `src/core/Tellma.Core.Abstractions/` (existing) | `Tellma.Core.Queryex` only | namespaces `.Tenancy` (§3, §4) and `.Composition` (§2): every enum, record and contract a module or distribution names; the three exceptions of §4.6; `TenancyTelemetryNames` and `HostTelemetryNames` |
| `Tellma.Core` | `src/core/Tellma.Core/` (existing, grows) | `Tellma.Core.Abstractions`, `Tellma.Core.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId`, `Microsoft.Data.SqlClient`, `Microsoft.Extensions.*`, `Microsoft.AspNetCore.DataProtection.Abstractions`, `Cronos`, `DocumentFormat.OpenXml`, `MessageFormat` | `Tellma.Core.Composition` (the composition pipeline, `TellmaBuilder`, realizers, `TellmaStartupGate`, `CoreFeature`), `Tellma.Core.Tenancy` (`CatalogDbContext`, registry, catalog, connection factories, holder, guard, scope factory, `TenantSandboxContext`), `Tellma.Core.Data.TellmaDbContext` registration |
| `Tellma.Core.AspNetCore` | `src/core/Tellma.Core.AspNetCore/` (new) | `Tellma.Core`, `FrameworkReference Microsoft.AspNetCore.App`, `Microsoft.AspNetCore.Authentication.OpenIdConnect`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Azure.Extensions.AspNetCore.DataProtection.Blobs`, `Azure.Extensions.AspNetCore.DataProtection.Keys`, `Serilog.AspNetCore`, `OpenTelemetry.*` | `AddTellma(WebApplicationBuilder)`, `UseTellma`, `MapTellma`, the BFF and session store, CSRF, tenant middleware and access filter, the endpoint audit, route groups, `/api/distribution-info`, health, the admin surface, `SessionSweepService`, `MembershipReconcileService`, Data Protection and forwarded headers |
| `Tellma.Core.Migrator` | `src/core/Tellma.Core.Migrator/` (new) | `Tellma.Core`, `Tellma.Core.EntityFrameworkCore.Design`, `Microsoft.EntityFrameworkCore.Design` | `TellmaMigrator`, the platform-owned catalog migrations (`catalog.__EFMigrationsHistory`), `TellmaDesignTimeDbContextFactory<TContext>`, the commands of §6.1 |
| `Tellma.Defaults.Azure` | `src/defaults/Tellma.Defaults.Azure/` (new) | `Tellma.Core.Email`, `Tellma.Core.Webhooks`, `Tellma.Core.Imaging`, `Tellma.Connector.AcsEmail.Adapter`, `Tellma.Connector.SendGrid.Adapter`, `Tellma.Connector.AzureBlobs.Adapter` | `UseAzureDefaults(TellmaBuilder)`: email, webhooks, Azure blobs and imaging in one call (§1.3) |
| `Tellma.Defaults.Azure.AspNetCore` | `src/defaults/Tellma.Defaults.Azure.AspNetCore/` (new) | `Tellma.Core.AspNetCore`, `Microsoft.Azure.SignalR` | `UseAzureWebDefaults(TellmaBuilder)`: the Azure SignalR backplane, web host only (§1.3) |

`Tellma.Core` carries the data-access, pipeline, access, settings, blobs, Excel, jobs and
notifications runtimes of the sibling specs as namespaces; it takes no ASP.NET dependency (the Data
Protection abstractions package carries no framework reference), so the migrator and any worker host
compose it without the web stack. `Tellma.Core.AspNetCore` and `Tellma.Core.Migrator` are adapters
of `Tellma.Core`, not optional peers; the migrator package is the migration host — the command
runner, the locks and fan-out, the provisioning-step runner, the grant recomputation and the
design-time factory — and carries pre-generated migrations for the catalog alone, because no
distribution contributes to the catalog schema and every catalog must be identical for the identity
engine's schema to sit beside it; the tenant model's migrations, core, packs and distribution tables
together, are generated in the distribution's Migrator project. `Tellma.Core.Imaging`
(`Tellma.Core.Abstractions` + SkiaSharp, spec 0016) is never referenced by `Tellma.Core`.
`Tellma.Defaults.Azure` and `Tellma.Defaults.Azure.AspNetCore` are distribution-layer packages: the
two defaults bundles sit above the connectors and adapters they reference, and no Core package
references either. Modules (`Tellma.Module.<M>`) reference only Abstractions packages; the feature
contract lives in Abstractions because a module must ship a feature without an edge to
`Tellma.Core`.

**Package pins added or moved by this spec.** `Microsoft.Data.SqlClient` 6.1.6 (required by EF
SqlServer 10.0.11 and by spec 0011's `hierarchyid` reader), `Microsoft.EntityFrameworkCore.*`
10.0.11, `Microsoft.AspNetCore.Authentication.OpenIdConnect`, `…JwtBearer` and
`Microsoft.AspNetCore.SpaProxy` at the repository's ASP.NET line,
`Microsoft.Extensions.Configuration.KeyPerFile` 10.0.11,
`Azure.Extensions.AspNetCore.Configuration.Secrets` 1.5.2, `Testcontainers.MsSql` 4.14.0. When
SqlClient moves to 7.x, `Microsoft.Data.SqlClient.Extensions.Azure` is added in the same bump, or
every Entra authentication mode fails at runtime.

### 1.2 The reference distribution

```
distributions/acme/
├── README.md
├── src/
│   ├── Tellma.Distro.Acme.Web/
│   │   ├── Program.cs                          # three platform calls (§1.3)
│   │   ├── Endpoints/                          # custom endpoints mapped onto the returned groups
│   │   ├── Properties/launchSettings.json      # tracked; fixed ports; the SPA proxy on in Development
│   │   ├── appsettings.json / appsettings.Development.json
│   │   ├── wwwroot/                            # the published SPA (build output; untracked)
│   │   └── Tellma.Distro.Acme.Web.csproj       # SpaRoot, SpaProxyServerUrl, SpaProxyLaunchCommand
│   ├── Tellma.Distro.Acme/
│   │   ├── AcmeComposition.cs                  # Slug + Compose(TellmaBuilder); shared by both hosts
│   │   ├── Entities/                           # sealed leaves and distribution-only entities
│   │   ├── Services/                           # custom services, validators, effects
│   │   └── Tellma.Distro.Acme.csproj           # class library; referenced by the Web and Migrator projects
│   ├── Tellma.Distro.Acme.Client/
│   │   ├── src/                                # the Angular application; arrives with the UI phase
│   │   ├── proxy.conf.js                       # forwards every non-SPA prefix to the Web project
│   │   ├── aspnetcore-https.js                 # exports the ASP.NET Core development certificate
│   │   ├── angular.json / package.json / tsconfig*.json
│   │   └── Tellma.Distro.Acme.Client.esproj    # Microsoft.VisualStudio.JavaScript.Sdk
│   └── Tellma.Distro.Acme.Migrator/
│       ├── Program.cs                          # one line (§1.3)
│       ├── AcmeDesignTimeFactory.cs            # derives TellmaDesignTimeDbContextFactory<TellmaDbContext>
│       ├── Migrations/                         # tenant-model migrations and snapshot, generated by dotnet ef
│       └── Tellma.Distro.Acme.Migrator.csproj  # → Tellma.Distro.Acme, Tellma.Core.Migrator
└── test/
    ├── Tellma.Distro.Acme.Web.Tests/
    ├── Tellma.Distro.Acme.IntegrationTests/
    └── Tellma.Distro.Acme.E2E/                 # Playwright; scaffolded with no tests until the UI specs ship
```

- **Slug and names.** Slug `acme`; project and namespace prefix `Tellma.Distro.Acme`;
  `DeploymentIdentity("acme", environment.EnvironmentName)`; OIDC `client_id = acme`, service client
  `acme-svc`. Acme is a fictional customer so the folder exercises the real distribution shape; the
  deployment `acme.app.tellma.com` is the platform's smoke deployment.
- **Composition library.** The class library `Tellma.Distro.Acme` (project, package and namespace
  name alike) holds `AcmeComposition`, `Entities/` and `Services/`, everything both hosts compose.
  It references `Tellma.Core` and `Tellma.Defaults.Azure` (which brings `Tellma.Core.Email`,
  `Tellma.Core.Webhooks`, `Tellma.Core.Imaging` and the Azure connectors, §1.3) and gains
  `Tellma.Module.Gl` and `Tellma.Module.Gl.Abstractions` with spec 0017. It never references an
  ASP.NET package, directly or transitively, and never `Tellma.Core.Mcp`, a `*.Design` package or
  the Client esproj.
- **Web project references.** `Tellma.Distro.Acme`, `Tellma.Core.AspNetCore`,
  `Tellma.Defaults.Azure.AspNetCore`, `Tellma.Identity` (in-proc mode), the Client esproj with
  `ReferenceOutputAssembly=false`, and `Microsoft.AspNetCore.SpaProxy`; it gains `Tellma.Core.Mcp`
  with spec 0015. It never references a `*.Design` package.
- **Migrator project references.** `Tellma.Distro.Acme` and `Tellma.Core.Migrator` only, so a
  migrator build carries no ASP.NET package, never builds the Client esproj and needs no Node.
- **Client project.** The recommended ASP.NET Core + Angular shape: a JavaScript-SDK esproj beside
  the Web project, scaffolded by hand from a current Angular CLI workspace (vitest, pnpm) with the
  Visual Studio template as a guide only, because the template's recipe is out of date. In
  Development, `dotnet run` on the Web project starts the SPA proxy, which launches `pnpm start` and
  redirects the browser to the Angular dev server, so the browser's origin is the dev server's:
  `Tellma:PublicOrigin` in `appsettings.Development.json` is `https://localhost:4200`, and
  `proxy.conf.js` forwards `/bff`, `/api`, `/id`, `/signin-oidc`, `/signout-callback-oidc`,
  `/health`, `/openapi`, `/.well-known` and `/{tenantId}/api`, `/{tenantId}/hub` (websockets on),
  `/{tenantId}/blobs` and `/{tenantId}/mcp` to the Web project's port; every route it does not
  forward is an SPA deep link. `aspnetcore-https.js` exports the development certificate for the
  dev server on first start. The `__Host-` session cookie works unchanged behind the proxy because
  both origins are `https` on `localhost`. Outside Development the SPA proxy is inert:
  `dotnet publish` builds the client and copies `dist/<app>/browser` into `wwwroot`, which
  `MapTellma` serves with the fallback of §5.1.
- **Folders.** `Entities/`, `Services/`, `Endpoints/` name what a distribution adds. There is no
  `Data/` folder (a distribution owns no data-access code) and no per-feature folder tree. "Data,
  service, web" remain prose names for the layers.
- **`taxonomy.json`** at the repository root, created by this spec: `modules: []` (spec 0017 adds
  `"Gl"`), `compliance: []`, `reservedSlugs` (the platform's reserved-slug list plus `acme`, taken
  by the platform), `distributions: ["acme"]`. A unit test in `Tellma.Core.Tests` asserts every
  `distributions/<slug>/` folder is listed, every listed slug is lowercase, starts with a letter and
  is at most 15 characters, and no distribution slug appears in `reservedSlugs` except `acme`.

### 1.3 The three calls

Illustration — `Program.cs` of the Web project:

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddTellma(AcmeComposition.Slug, AcmeComposition.Compose, tellma => tellma.UseAzureWebDefaults());
WebApplication app = builder.Build();
app.UseTellma(); app.MapTellma();
app.Run();
```

Illustration — `Program.cs` of the Migrator project:

```csharp
return await TellmaMigrator.RunAsync(args, AcmeComposition.Slug, AcmeComposition.Compose);
```

Illustration — the shared composition method:

```csharp
public static void Compose(TellmaBuilder tellma) => tellma.UseAzureDefaults().AddFeature<GlFeature>().Languages(["en", "ar"]);
```

`UseAzureDefaults()` (`Tellma.Defaults.Azure`) is the Azure deployment's common composition, four
explicit calls in one: `AddTellmaEmail()` with the ACS and SendGrid transports registered and the
sender chosen by `Email:Provider` (spec 0007; the call is the explicit email composition spec 0007
requires of every host), `AddTellmaWebhooks()`, `AddAzureBlobStore()` bound from
`Tellma:Blobs:Azure` with the host's `TokenCredential`, and `AddTellmaSkia()`.
`UseAzureWebDefaults()` (`Tellma.Defaults.Azure.AspNetCore`), called from the web host's
`composeWeb` delegate (§5.1), selects the Azure SignalR backplane when
`Azure:SignalR:ConnectionString` is present (spec 0020 §6.6), so neither the composition library nor
the migrator carries an ASP.NET package. A distribution that wants a different sender, store,
processor or backplane composes those calls itself instead of the bundles. On-premises deployments
compose a different set (SMTP, the file-system store, a Redis backplane) and receive their own pair
of bundles with the first on-premises distribution. The web host and the migrator build
byte-identical EF models from the same method; a test pins the parity.

### 1.4 Design principles

1. **Fail closed.** Every cache has a stated maximum staleness and a stated behaviour when its
   backing store is unreachable; every tenant-routed endpoint passes the tenant middleware and the
   access guard or fails the startup audit; an unbound sandbox context throws.
2. **One code path per concern.** One catalog database for single-live and multi-live
   distributions; one connection-resolution path for every deployment; one access guard called from
   two front doors; one composition method for the web host and the migrator.
3. **No per-tenant infrastructure secret.** The catalog stores locations; databases are reached
   by managed identity or integrated security; configuration providers deliver the few secrets a
   host needs; a tenant's own integration credentials live inside its database as spec 0012's
   `Secret` settings.
4. **Minimal distribution ceremony.** A distribution writes one composition method, an optional
   feature class, and nothing for tenancy, sessions, CSRF, health or provisioning.

## 2. Feature composition

### 2.1 The feature contract

```csharp
// Tellma.Core.Abstractions.Composition
public interface ITellmaFeature
{
    string Name { get; }                                    // stable, unique, lowercase dotted: "core", "gl", "acme"
    void Declare(FeatureDeclaration declaration);
    void Contribute(FeatureContribution contribution);
}

public sealed class RequiresAttribute(string FeatureName) : Attribute;   // on type; repeatable; sugar for FeatureDeclaration.Requires(featureName)

public sealed class FeatureDeclaration
{
    public FeatureDeclaration Requires(string featureName);   // another feature's Name; "core" is implicit and never declared
    public FeatureDeclaration Options<TOptions>(string configurationPath);
    public IReadOnlyList<string> RequiredFeatures { get; set; }
}

public abstract record FeatureContributionItem;             // every item is a record; Core realises each through IContributionRealizer<TItem>

public enum ServiceLifetimeKind { Singleton, Scoped, Transient }

public sealed record ServiceContributionItem(
    Type ServiceType, Type? ImplementationType, Func<IServiceProvider, object>? Factory,
    ServiceLifetimeKind Lifetime) : FeatureContributionItem;
public sealed record ModelContributionItem(Type ContributorType) : FeatureContributionItem;
public sealed record SecurablesContributionItem(
    Action<SecurableRegistryBuilder> Configure) : FeatureContributionItem;
public sealed record SettingKeysContributionItem(Type DeclaringType) : FeatureContributionItem;
public sealed record CalendarContributionItem(Type CalendarType) : FeatureContributionItem;
public sealed record ProvisioningStepContributionItem(Type StepType) : FeatureContributionItem;
public sealed record JobHandlerContributionItem(Type HandlerType) : FeatureContributionItem;
public sealed record BuiltInScheduleContributionItem(
    string HandlerKey, string Cron, object? Arguments, string? TimeZoneId) : FeatureContributionItem;
public sealed record NotificationTypeContributionItem(
    NotificationTypeDescriptor Descriptor) : FeatureContributionItem;
public sealed record NotificationChannelContributionItem(
    NotificationChannelDescriptor Descriptor) : FeatureContributionItem;
public sealed record ClientEventContributionItem(string Name) : FeatureContributionItem;
public sealed record BlobKindContributionItem(
    string Kind, Func<BlobKindPolicy, BlobKindPolicy> Configure) : FeatureContributionItem;
public sealed record ValidatorContributionItem(
    Type EntityType, Type ComponentType, ComponentKind Kind) : FeatureContributionItem;
public sealed record StackCompanionContributionItem(
    Type EntityType, Type CompanionType) : FeatureContributionItem;
public sealed record ShapeRequirementContributionItem(
    Type EntityType, Type ShapeType) : FeatureContributionItem;                    // §2.4

public enum ComponentKind { Validator, PersistEffect, DetailsContributor }

public sealed class FeatureContribution
{
    public FeatureContribution Add(FeatureContributionItem item);
    public FeatureContribution Feature(ITellmaFeature feature);
    public FeatureContribution Singleton<TService, TImplementation>();
    public FeatureContribution Scoped<TService, TImplementation>();
    public FeatureContribution Transient<TService, TImplementation>();
    public FeatureContribution Model<TContributor>();
    public FeatureContribution Entity<TEntity>();
    public FeatureContribution Entity<TEntity, TService>();
    public FeatureContribution ApiService<TService>();
    public FeatureContribution Validator<TEntity, TValidator>() where TValidator : IEntityValidator<TEntity>;
    public FeatureContribution PersistEffect<TEntity, TEffect>() where TEffect : IPersistEffect<TEntity>;
    public FeatureContribution DetailsContributor<TEntity, TContributor>()
        where TContributor : IDetailsContributor<TEntity>;
    public FeatureContribution EntityCompanion<TEntity, TCompanion>();
    public FeatureContribution Securables(Action<SecurableRegistryBuilder> configure);
    public FeatureContribution SettingKeys(Type declaringType);
    public FeatureContribution Calendar<TCalendar>() where TCalendar : ICalendarSystem;
    public FeatureContribution ProvisioningStep<TStep>() where TStep : ITenantProvisioningStep;
    public FeatureContribution JobHandler<THandler>();
    public FeatureContribution BuiltInSchedule(
        string handlerKey, string cron, object? arguments = null, string? timeZoneId = null);
    public FeatureContribution NotificationType(NotificationTypeDescriptor descriptor);
    public FeatureContribution NotificationChannel(NotificationChannelDescriptor descriptor);
    public FeatureContribution ClientEvent(string name);
    public FeatureContribution BlobKind(string kind, Func<BlobKindPolicy, BlobKindPolicy> configure);
    public FeatureContribution RequiresShape<TEntity, TShape>();                    // §2.4; TShape is an interface
}

public interface IStartupCheck
{
    string Name { get; }
    Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services);
}

public sealed record CompositionProblem(string Source, string Problem, string? Fix);

public sealed class TellmaCompositionException(
    IReadOnlyList<CompositionProblem> Problems) : Exception;                        // one message line per problem; startup only, never mapped
```

| Member | Meaning |
|---|---|
| `ITellmaFeature.Name` | The feature's identity in diagnostics, in `SecurableDescriptor.Feature`, and in the provisioning-step and handler-key grammars (`<name>.<step>`). Duplicate names are a composition problem. |
| `Declare` | Records edges and options bindings only; runs before any contribution. |
| `Contribute` | Adds data-only items. A feature never sees `IServiceCollection` or `ModelBuilder`. |
| `[Requires("name")]` | Equivalent to `Requires("name")` inside `Declare`; both may be used, once per required feature. The name is another feature's `Name`, taken from the `<Module>Module.FeatureName` constant its Abstractions package exposes (`GlModule.FeatureName`, spec 0017 §5.1); `"core"` is never declared (below). |
| `Options<TOptions>(path)` | Binds `TOptions` from the configuration section at `path` with `ValidateDataAnnotations` and `ValidateOnStart`; the same type bound twice to different paths is a problem. |
| `Add` | The escape hatch for an item type defined by another spec without a sugar; an item no realizer handles is a composition problem naming the item type (a pack newer than Core). |
| `Feature` | A nested feature, validated and ordered with the rest. |
| `Singleton/Scoped/Transient<,>` | `ServiceContributionItem` by type. A dependent feature's registration of the same service type overrides its dependency's (topological realisation order): the "distribution replaces a pack service" mechanism. |
| `Model<T>` | `T` must implement EF's `IEntityTypeConfiguration<>`; applied by `TellmaDbContext` in feature order. |
| `Entity`, `Entity<,>`, `ApiService`, `Validator`, `PersistEffect`, `DetailsContributor`, `EntityCompanion` | Items realised by spec 0014 (`StackContributionItem`, `ApiServiceContributionItem`, `ValidatorContributionItem`, `StackCompanionContributionItem` — a companion's `[ApiAction]` methods projected under the stack's segment). |
| `Securables` | Realised by spec 0013 (`ISecurableContributor`). |
| `SettingKeys`, `Calendar` | Realised by spec 0012. |
| `ProvisioningStep` | Registers the step as a scoped service and adds it to the migrator's ordered list (§6.2). |
| `JobHandler`, `BuiltInSchedule` | Realised by spec 0019. |
| `NotificationType`, `NotificationChannel`, `ClientEvent` | Realised by spec 0020. |
| `BlobKind` | Realised by spec 0016: transforms the policy the kind's `[BlobReference]` preset produced (spec 0016 §1.3). |
| `RequiresShape` | Realised by the `core.entity-shapes` check (§2.3, §2.4). |
| `IStartupCheck` | Registered as a service by any package; the realised gate runs every check and aggregates the problems. |
| `CompositionProblem.Fix` | The remedy, phrased as the code or configuration to add; every built-in check supplies one. |

`CoreFeature : ITellmaFeature` (`Tellma.Core.Composition`, `Name = "core"`) is added first and
unconditionally by `AddTellma`; it contributes the users, roles, permissions, settings and cache
content of specs 0012, 0013 and 0017. Core is implicit: every other feature follows it in the
topological order without declaring it, so `GlFeature` carries no `[Requires]` (spec 0017 §5.5) and
a feature declares only the packs it builds on, by name (`[Requires(GlModule.FeatureName)]`). A
distribution's own feature uses the identical items. Store registrations stay on `Services`
(`AddFileSystemBlobStore`, `AddAzureBlobStore`, `AddTellmaSkia`).

### 2.2 The composition root and the builder

```csharp
// Tellma.Core.Composition (runtime)
public static class TellmaServiceCollectionExtensions
{
    public static IServiceCollection AddTellma(
        this IServiceCollection services, string slug, IConfiguration configuration,
        IHostEnvironment environment, Action<TellmaBuilder> compose);
}

public sealed class TellmaBuilder
{
    public IServiceCollection Services { get; set; }
    public IConfiguration Configuration { get; set; }
    public IHostEnvironment Environment { get; set; }
    public TellmaBuilder AddFeature<TFeature>() where TFeature : ITellmaFeature, new();   // idempotent
    public TellmaBuilder AddFeature(ITellmaFeature feature);
    public TellmaBuilder UseEntity<TDefault, TLeaf>() where TLeaf : TDefault;
    public TellmaBuilder Languages(IReadOnlyList<string> codes);
    public TellmaBuilder AddLanguage(LanguageInfo info);
    public TellmaBuilder Blobs(Action<BlobsBuilder> configure);
}

public sealed record DeploymentVersions(string PlatformVersion, string DistributionVersion);   // Tellma.Core.Abstractions.Composition; singleton

public interface IContributionRealizer<TItem> where TItem : FeatureContributionItem
{
    void Realize(TItem item, ITellmaFeature owner, RealizationContext context);
}

public sealed class RealizationContext
{
    public IServiceCollection Services { get; set; }
    public IReadOnlyList<CompositionProblem> Problems { get; set; }
}

public static class TellmaComposition
{
    public static IReadOnlyList<CompositionProblem> Validate(
        string slug, IConfiguration configuration, Action<TellmaBuilder> compose);   // host-free; steps 1–3 of §2.3
}

// Tellma.Core.Mcp
public static class TellmaMcpBuilderExtensions
{
    public static TellmaBuilder AddMcp(this TellmaBuilder tellma, Action<TellmaMcpOptions>? configure = null);
}
```

| Member | Meaning |
|---|---|
| `AddFeature<T>()` / `AddFeature(feature)` | Explicit selection; no assembly scanning. Adding the same type twice with different instances is a problem; the same instance twice is a no-op. |
| `UseEntity<TDefault, TLeaf>` | Leaf substitution; the stack, service and securables follow the leaf (spec 0014). |
| `Languages`, `AddLanguage` | The distribution's offered language subset and catalogue additions (spec 0012). |
| `AddMcp` | Extension shipped by `Tellma.Core.Mcp`; a feature requiring the stack feature (spec 0015). |
| `Blobs` | Kinds and the image processor (spec 0016). |
| `IContributionRealizer<TItem>` | One per item type Core understands; `Tellma.Core` ships every realizer of §2.1; the realizer for an item is found by the item's runtime type, base types excluded. |
| `RealizationContext.Problems` | A realizer reports rather than throws; problems aggregate. |
| `TellmaComposition.Validate` | Runs collect, declare and the graph gate without a host, for tests and the migrator's `status`. |
| `DeploymentVersions` | The informational versions of `Tellma.Core` and of the entry assembly; both carry the source commit as `+<sha>` because the repositories build with SourceLink and the SDK's `IncludeSourceRevisionInInformationalVersion` default. Registered by `AddTellma`; the information document, the log enricher and the trace resource read it (§5.9, §9). |

### 2.3 The pipeline and the two gates

`AddTellma(services, slug, configuration, environment, compose)` runs, in order:

1. **Collect.** Registers `DeploymentIdentity(slug, environment.EnvironmentName)` and
   `DeploymentVersions` (§2.2); adds `CoreFeature` first (§2.1); constructs `TellmaBuilder`; runs
   `compose`.
2. **Declare.** Each feature's `Declare` records its edges and options bindings; `Name` is read
   from the feature.
3. **Graph gate.** Problems collected: a `Requires` name no added feature carries (the problem
   names the feature that requires it); a duplicate name; a cycle; a type added twice with
   different instances; an options type bound twice to different paths; a slug that is not
   lowercase, letter-first and at most 15 characters. Any problem throws one
   `TellmaCompositionException` from `AddTellma`, before `Build()`.
4. **Realise.** `Contribute` runs in topological order (dependencies first); every item is realised;
   then the platform registers `CatalogDbContext`, `ITenantRegistry`, `ITenantCatalog`,
   `ITenantConnectionFactory`, `ITenantConnectionProvider`, `ITenantDbContextFactory`,
   `IRequestContextHolder`/`IRequestContextAccessor`, `ITenantAccessGuard`, `ITenantScopeFactory`,
   `TenantSandboxContext : ISandboxContext`, `ITenantMembershipDirectory`, `TellmaDbContext`, the
   options of §8 with `ValidateOnStart`, the `IStartupCheck`s below, and `TellmaStartupGate` as the
   **first** hosted service.
5. **Realised gate.** `TellmaStartupGate` (`IHostedLifecycleService.StartingAsync`) runs every
   registered `IStartupCheck` inside one service scope and throws one `TellmaCompositionException`
   carrying every problem before the host serves traffic.

Built-in checks registered by this spec, each an `IStartupCheck` named `core.<check>`:

| Check | Fails when |
|---|---|
| `core.options` | Any bound options type fails validation. |
| `core.model-contributors` | A `ModelContributionItem` type does not implement `IEntityTypeConfiguration<>`. |
| `core.deployment-identity` | `DeploymentIdentity.Application != slug`, or `ISandboxContext` does not resolve. |
| `core.globalization` | The process is not running ICU (`CultureInfo("en-US").CompareInfo.Version` probe); invariant and NLS modes break spec 0012's IANA conversion and calendar catalogue. |
| `core.key-vault-literals` | Any configuration value under `Tellma:` begins with `@Microsoft.KeyVault(` (an unresolved App Service reference). |
| `core.sql-profiles` | A profile violates §3.5's rules. |
| `core.public-origin` | `Tellma:PublicOrigin` is missing, relative, carries a path, or is not `https` outside Development. |
| `core.taxonomy` | The running slug is absent from `taxonomy.json`'s `distributions` (Development only; the file is read from the repository root when present). |
| `core.entity-shapes` | The leaf registered for an entity does not implement a shape another feature requires of it (§2.4); the `Fix` lists the interface and its members. |

`Tellma.Core.AspNetCore` adds `host.data-protection` (§5.8), `host.endpoint-audit` (§5.3) and
`host.identity` (in-proc secrets, §5.7); the sibling specs add the securables audit, natural-key
backing, version-tag registry, language catalogue, calendar registry, blob kinds and handler keys.
A check that needs the catalog and finds it unreachable reports nothing: reachability is a readiness
concern (§3.4), never a startup failure.

### 2.4 Extending another feature's entity

A feature never alters another feature's class: the distribution's leaf is the only class that adds
columns to a table (spec 0011 §2.8). A pack that needs columns on an entity it does not own — a
compliance pack tracking e-invoice state on the sales module's invoice — has two documented shapes.

- **A required shape, implemented by the distribution's leaf.** The pack declares an interface
  carrying the members it needs (`IEInvoiceTracked { EInvoiceStatus, EInvoiceUuid, … }`) and
  contributes `RequiresShape<SalesInvoice, IEInvoiceTracked>()`. The realised gate's
  `core.entity-shapes` check fails when the leaf registered for `SalesInvoice` does not implement
  the interface, and its `Fix` prints the members to add; the distribution implements it on its leaf
  (`MyInvoice : SalesInvoice, IEInvoiceTracked`) with the column facets it chooses. The pack's
  validators, persist effects and details contributors are generic over
  `TEntity : SalesInvoice, IEInvoiceTracked`, and its statements take column names from
  `EntityMetadata`; the columns are ordinary properties of the leaf, so they travel on the wire,
  appear in the Queryex schema and are written by the emitter like any other. Ceremony: one
  interface clause and the properties, on a class the distribution already owns; a missing member is
  a startup failure with the fix in the message.
- **A pack-owned sibling table, no distribution code.** The pack ships a one-row-per-owner entity
  whose primary key is the owner's key (`uae.SalesInvoiceEInvoices (InvoiceId PK FK)`), marked
  `[Sibling("EInvoice")]`, writes it from an `IPersistEffect<SalesInvoice>` inside the same persist
  batch through `IDataBatch.Sql` with declared writes, and reads it through an
  `IDetailsContributor<SalesInvoice>`. Spec 0011 §11.1 declares the one-to-one navigation from the
  owner to the sibling (`EInvoice.Status` on the invoice root), so grids filter and sort on the
  sibling's columns; the sibling never travels inside the owner's wire shape.

The first is the default when the columns belong on the owner's screens and exports; the second
when the pack must own the data's lifecycle outright, or the owner's leaf is not the distribution's
to change.

## 3. Tenancy

### 3.1 The tenant identifier and the route grammar

The tenant identifier is an immutable positive `int` allocated from `catalog.sq_Tenants`, never
reused and never renamed; there is no tenant slug. Every tenant surface is prefixed by the tenant
id:

| Surface | Route | Scheme / policy |
|---|---|---|
| Web API (SPA) | `/{tenantId:int:min(1)}/api/web/...` | session cookie; `Tellma.Web` |
| Public API | `/{tenantId:int:min(1)}/api/v{version:apiVersion}/...` | bearer; `Tellma.Api` (seam; nothing mapped this release) |
| MCP | `/{tenantId:int:min(1)}/mcp` | bearer; `Tellma.Mcp` |
| SignalR hub | `/{tenantId:int:min(1)}/hub` | session cookie; `Tellma.Web` |
| Blobs | `/{tenantId:int:min(1)}/blobs/{kind}` (POST), `/{tenantId:int:min(1)}/blobs/{kind}/{id}` (GET) | session cookie; `Tellma.Web` |
| SPA deep links | `/{tenantId:int:min(1)}/{**path}` | anonymous; serves the app shell |

Every tenantless route begins with a non-numeric segment (`/bff`, `/api`, `/signin-oidc`,
`/signout-callback-oidc`, `/health`, `/id`, `/.well-known`), so the integer constraint alone keeps
platform routes and tenant routes from shadowing each other. Enumerability is not a security
property: membership is verified on every request, and a non-member receives the same
`404 tenant-not-found` as a request for a tenant that does not exist.

### 3.2 The catalog database

Every distribution has exactly one catalog database — SaaS `tellma-<slug>-catalog`, Development
`<Tellma:Sql:DatabasePrefix>.catalog` with the default prefix `Tellma.dev.<slug>` — reached through
`Tellma:Catalog:ConnectionString` (`Max Pool Size=10`), migrated by the migrator before any tenant
database, holding schema `catalog` (§7) and, in in-proc identity mode, the identity engine's `idsvr`
schema with its own migration history. The catalog holds no connection string and no secret: a
tenant row carries `Server`, `Database` and a `CredentialProfile` name.

A distribution hosts one tenant or many by what its operators register, never by a policy
setting: the registry, the migrator fan-out, the session store and the membership directory are
identical for a catalog with one live tenant and for one with two hundred, and nothing refuses a
second live registration. The company picker's single-tenant shortcut (§5.5) reads the membership
list, not a policy.

### 3.3 Tenant states and verdicts

```csharp
// Tellma.Core.Abstractions.Tenancy
public enum TenantCategory { Live, Sandbox }

public enum TenantState { Provisioning, Active, ReadOnly, Suspended, Retired }

public sealed record TenantDescriptor(
    int Id, string Name, string? Name2, string? Name3, IReadOnlyList<string> Languages,
    TenantCategory Category, TenantState State, int? LiveTenantId);

public sealed record TenantDisplayNames(                          // the settings' name group and content-language codes, in order
    string Name, string? Name2, string? Name3, IReadOnlyList<string> Languages);
```

`Category` is immutable after registration; a sandbox is its own row, optionally naming the live
tenant it derives from (`LiveTenantId`: the picker's grouping key and the default source of a future
clone) — a standalone sandbox, for demonstrations and training, names none. `Name`, `Name2`, `Name3`
and `Languages` mirror the tenant settings' name group and content-language codes (§3.7), so the
company picker localises without opening a tenant database; `Name2`/`Name3` are null when the
tenant has no second or third language, each paired with its code in `Languages`, which holds the
distribution's default language alone until the first settings save.
`TenantDescriptor` never exposes the database location. Verdicts, returned only after authentication
succeeded (`401` first) and after membership was verified (`404 tenant-not-found` for a non-member):

| State | Read request | Mutating request | Background scope (`ITenantScopeFactory`) |
|---|---|---|---|
| `Provisioning` | `404 tenant-not-found`, indistinguishable from absent; the membership list (§3.8) carries the state, so the picker can show a tenant being set up | same | throws `TenantUnavailableException`, except the migrator's step runner (`allowNonActive`, §4.4) |
| `Active` | served | served | created |
| `ReadOnly` | served; the connect initializer composes its premises with `StampActivity = false` and `AllowStateFlip = false`, so reads succeed against a database set `READ_ONLY` | `403 tenant-read-only` | throws `TenantUnavailableException`, except the migrator's step runner (§4.4); the worker skips the tenant |
| `Suspended` | `403 tenant-suspended` | same | throws |
| `Retired` | `404 tenant-not-found`, indistinguishable from absent | same | throws |

"Mutating" is endpoint metadata: `TenantEndpointMetadata.IsMutation`, stamped by spec 0015's
projection — an operation is a mutation when its descriptor says so (spec 0014 §2.3) — and by
`WithMutation(bool)` on hand-mapped endpoints; the audit (§5.3) refuses a tenant endpoint without
it. Legal transitions: `Active ⇄ ReadOnly`, `Active | ReadOnly → Suspended`, `Suspended → Active`,
`Provisioning → Active` (the migrator), any state `→ Retired`; `Retired` is terminal. One verdict is
not a state: `503 tenant-schema-behind`, raised by spec 0011's executor at the head of every round
trip against a database whose recorded schema fingerprints do not include the running model's
(spec 0011 §4.3), so a database the migrator has not yet reached — or one two migrations ahead —
is refused before any statement runs, whatever the snapshot says.

### 3.4 The registry: an in-memory snapshot with a bounded staleness

```csharp
// Tellma.Core.Tenancy (runtime)
public sealed record TenantLocation(string Server, string Database, string CredentialProfile);

public sealed record TenantInfo(
    TenantDescriptor Descriptor, TenantLocation Location, string ConnectionString,
    string? Properties, Guid Version);

public interface ITenantRegistry                            // singleton; snapshot reads, no I/O
{
    IReadOnlyList<TenantInfo> Tenants { get; }
    Guid SnapshotVersion { get; }
    DateTimeOffset? SnapshotLoadedAt { get; }
    TenantInfo? Find(int tenantId);
    TenantInfo Get(int tenantId);                           // TenantNotFoundException
    Task RefreshAsync(bool force);                          // single-flight
}
```

- **Snapshot.** An immutable dictionary of `TenantInfo` by id plus the
  `catalog.CatalogState.Version` it was loaded from. `TenantInfo.ConnectionString` is composed when
  the snapshot is loaded (§3.5) — the snapshot is the process's only cache of tenant locations and
  strings — and never logged; `Properties` is the row's non-secret JSON bag.
- **Refresh loop.** A hosted service (`PeriodicTimer` over `TimeProvider`,
  `Tellma:Catalog:RefreshInterval`, default `00:00:15`) runs the version probe of §7.5 and reloads
  the full snapshot only when the version differs (`!=`, never `<`, so a restored backup with an
  older stamp still reloads). Refresh is single-flight; `RefreshAsync(force: true)` reloads
  regardless of the version.
- **Listeners.** After a reload that changed any descriptor, and after every local write, the
  registry calls each `ITenantStateListener.OnStateChangedAsync(tenant, previous)` (§4.5) for the
  changed tenants, and drops the pooled context factory of a tenant whose `Location` changed.
- **Staleness bound.** A failed refresh keeps serving the snapshot for up to
  `Tellma:Catalog:MaxStaleness` (default `00:05:00`) at Warning; beyond it every tenant request
  answers `503 catalog-unavailable` (`TenantUnavailableException`, `Retry-After: 15`) and
  `/health/ready` reports unhealthy. A suspension takes effect immediately on the instance that
  received it and within one interval fleet-wide; a catalog outage never becomes a permanently
  ignored suspension.
- **Startup.** The first load is attempted with a 10-second bound. On success the catalog's
  migration history is compared with the platform's latest catalog migration: a behind or missing
  catalog fails startup with a message naming the exact migrator command (in Development, a missing
  catalog database prints `dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator
  -- migrate`); the web host never migrates. On a reachability failure the host starts,
  `/health/ready` reports not-ready, and the loop keeps retrying — a transient outage during a slot
  swap must not crash-loop every instance.

### 3.5 Connection resolution, credential profiles and pools

```csharp
// Tellma.Core.Tenancy (runtime)
public interface ITenantConnectionFactory                   // singleton; explicit tenant (migrator, reconcilers)
{
    string GetConnectionString(int tenantId);               // the snapshot's composed string; never logged
    Task<SqlConnection> OpenAsync(int tenantId);
    Task<SqlConnection> OpenCatalogAsync();
}

public interface ITenantConnectionProvider                  // scoped; the batch executor's door to SQL
{
    TenantInfo Tenant { get; }
    Task<SqlConnection> OpenAsync();
}

public interface ITenantDbContextFactory
{
    TellmaDbContext Create(int tenantId);
}
```

- **Composition.** `Tellma:Sql:Profiles:<name>` holds SqlClient connection-string fragments; the
  default profile is `default`. The registry composes a tenant's string at snapshot load with
  `SqlConnectionStringBuilder` — never by concatenation — from the profile fragment, then
  `Data Source` = row `Server`, `Initial Catalog` = row `Database`, `Application Name` =
  `DeploymentIdentity.DeploymentId`. The row never overrides `Encrypt`, `Authentication` or any
  credential keyword (its columns admit neither `;` nor `=`, §7.1). Both factories read the string
  from the snapshot and keep no copy of their own: an identical string reaches the same SqlClient
  pool, so nothing is cached twice and a relocation invalidates one thing.
- **Profiles by deployment.** SaaS: `Authentication=Active Directory Managed Identity;User
  Id=<client id>;Encrypt=True`. On-prem Windows: `Integrated Security=true;Encrypt=True`. On-prem
  Linux: `User Id=tellma_app;Password=<host-injected>;Encrypt=True`. Development: `Integrated
  Security=true` on LocalDB, or the container's `sa`. The migrator runs under its own identity
  (SaaS: the job's managed identity, which owns the databases; on-prem: a DDL service account or
  `tellma_ddl`).
- **Profile rules** (`core.sql-profiles`): every profile sets `Max Pool Size` ≤
  `Tellma:Sql:MaxPoolSizePerTenant` (default 20); `Encrypt` is
  present outside Development; no `Password=`/`Pwd=` outside Development unless
  `Tellma:Sql:AllowSqlPassword = true`; no `@Microsoft.KeyVault(` literal.
- **Warm profiles.** `Min Pool Size` is a per-profile choice: absent or 0 on the multi-tenant
  default; a small positive value (`Min Pool Size=4`) on a warm profile assigned to a single-live
  deployment or to the few tenants whose first request after an idle period must not wait on a
  connection open. The row's `CredentialProfile` selects it per tenant. Warm sessions count against
  the pool invariant while idle, so `core.sql-profiles` warns — at startup and after every snapshot
  reload, never failing — when more than `Tellma:Sql:MaxWarmTenants` (default 10) tenants are
  assigned to profiles with a non-zero `Min Pool Size`.
- **Pool invariant.** `T` active tenants × `I` instances × `P` per-tenant pool size bounds the
  sessions held against an elastic pool's 30,000-session limit; `P = 20` keeps 200 tenants × 3
  instances at 12,000 and relies on SqlClient's 4–8-minute idle drain so idle tenants on a
  `Min Pool Size = 0` profile hold nothing.
  Spec 0019's worker staggers per-tenant work; the migrator's fan-out parallelism is bounded (§6.1).
- **EF access.** `ITenantDbContextFactory` keeps one `PooledDbContextFactory<TellmaDbContext>` per
  tenant (`poolSize` 16), created lazily from that tenant's composed string, cached by id, dropped
  on a location change. All factories share EF's internal service provider and the compiled model.
  Only the platform resolves a context from it (the model host and the migrator); nothing injects a
  `TellmaDbContext` into distribution or pack code (§6.5). `Database.SetConnectionString` is never
  used. Spec 0011's executor opens connections only through `ITenantConnectionProvider` and never
  receives a string; a batch is bound to one tenant for its lifetime and never issues `USE` or
  `ChangeDatabase`. Every SQL span carries `tellma.db.role = tenant | catalog`.

**The application role.** Every tenant database has a database role `tellma_app`, created by the
migrator on first migration and re-granted idempotently after every `migrate` from the model:
`SELECT, INSERT, UPDATE, DELETE` on every schema the tenant model maps, `UPDATE` on every
`sq_<Table>` sequence (`sp_sequence_get_range` needs it), and `EXECUTE ON TYPE` for every table type
through spec 0001's configured grant principals (`AddTellma` sets `tellma_app` as the default
principal set). The web application's principal — the managed identity's contained user in SaaS,
the login's user on-prem — is a member of `tellma_app`; the migrator's principal owns the database.
The role name is a platform constant, so migrations never carry an environment-specific principal.

### 3.6 Secrets policy

Binding on every host:

- **No plaintext secret in any table, tracked file, log or metric.** No credential, key or token
  is stored in plaintext in `catalog.*` or any tenant table (a tenant's own credentials are spec
  0012's `Secret` keys, ciphertext only), in `appsettings*.json` checked into a repository (a test
  in the reference distribution scans tracked configuration for `Password=`, `Pwd=` and
  `ClientSecret` values), in logs (the factory redacts before logging) or in telemetry tags.
- **SaaS tenant databases have no passwords.** Provisioning runs
  `CREATE USER [<web app identity>] FROM EXTERNAL PROVIDER` and adds it to `tellma_app`.
- **Secret configuration values** (`Tellma:Identity:ClientSecret`,
  `Tellma:Identity:ServiceClientSecret`, an on-prem SQL password, `Azure:SignalR:ConnectionString`)
  arrive through configuration providers: App Service Key Vault references or
  `Azure.Extensions.AspNetCore.Configuration.Secrets` (`AddAzureKeyVault`, `ReloadInterval` 15 min)
  in SaaS; environment variables or a key-per-file directory
  (`Microsoft.Extensions.Configuration.KeyPerFile`, fed by Docker or Kubernetes secrets or systemd
  `LoadCredential`) on-prem; user secrets in Development. An unresolved Key Vault reference fails
  startup (`core.key-vault-literals`).
- **No DPAPI** anywhere; every script runs on Linux.
- **Per-tenant secrets are spec 0012's `Secret` setting keys**: a tenant-database value protected
  with Data Protection under a purpose that names the tenant, so a clone or a restore under
  another tenant id cannot read it; `catalog.Tenants.Properties` may hold the *name* of such a
  secret, never its value.

### 3.7 The catalog write side

```csharp
// Tellma.Core.Tenancy (runtime)
public sealed record TenantRegistration(
    string Name, TenantCategory Category, int? LiveTenantId, TenantLocation? Location);

public interface ITenantCatalog                             // every member is one catalog transaction + version bump + local refresh
{
    Task<TenantDescriptor> RegisterAsync(TenantRegistration registration, string actor, int? requestedId = null);
    Task<TenantDescriptor> SetStateAsync(int tenantId, TenantState state, string? reason, string actor);
    Task RenameAsync(int tenantId, TenantDisplayNames names);
    Task RelocateAsync(int tenantId, TenantLocation location, string actor);
    Task<IReadOnlyList<TenantInfo>> ListAsync();            // from the store, never the snapshot
}

// Tellma.Core.Abstractions.Tenancy — derives from spec 0014's TellmaException
public sealed class TenantStateException(string Code, string Detail) : TellmaException;   // 409 tenant-registration-refused | tenant-illegal-transition
```

| Member | Meaning |
|---|---|
| `RegisterAsync` | Inserts a `Provisioning` row (id from `catalog.sq_Tenants`, or `requestedId` when free — passed only by the migrator's seed path and `provision --tenant <id>`, §6.1; the admin surface of §5.9 never passes it); `Location` defaults to the catalog's own server and `<DatabasePrefix>.<Id>` under `default`; `Language1` is the distribution's default language; refuses a `LiveTenantId` on a `Live` registration or one naming a tenant that is not `Live` (`TenantStateException` with `Code = Tenant.RegistrationRefused`; 409 on the wire, spec 0015 §7.1). |
| `SetStateAsync` | The suspension hook: validates the transition against §3.3's table (an illegal one is `TenantStateException` with `Code = Tenant.IllegalTransition`; a transition lost to a concurrent one is the same exception, translated from the `THROW 50409` of §7.5), runs the statement of §7.5, refreshes the local registry and notifies the listeners. `actor` is a subject, a client id, or `migrator`. |
| `RenameAsync` | Mirrors the tenant's name group and content-language codes from tenant settings (spec 0012's `SettingsService.Save` calls it post-commit, best effort). |
| `RelocateAsync` | Changes `Server`/`Database`/`CredentialProfile`; the tenant must be `ReadOnly` or `Provisioning`; every instance reloads the snapshot within one interval and drops the tenant's pooled context factory. |

Catalog statements run as parameterised `SqlCommand`s over `OpenCatalogAsync()` inside
`Tellma.Core.Tenancy` (they never pass through the tenant-scoped prologue of spec 0013); the
membership table type is bound by metadata through spec 0011's `TableTypeBinder`. Every catalog
write is one transaction (`SET XACT_ABORT ON`) that also sets `CatalogState.Version = NEWID()`, so
the writing instance is consistent immediately and every other instance converges within one
refresh interval. A THROW from catalog SQL uses the platform band of spec 0011's `TellmaSqlErrors`:
`50409` for a lost transition race, whose message is the `Code` of the `TenantStateException`
`SetStateAsync` raises; `Detail` is a log-facing sentence naming the tenant and the refused
registration or transition. The catalog is not a home for tenant-data version tags.

### 3.8 The membership directory

```csharp
// Tellma.Core.Abstractions.Tenancy
public sealed record TenantMembership(TenantDescriptor Tenant, bool IsActive, DateTimeOffset UpdatedAt);

public sealed class TenantMembershipRecord                  // [TableType] standalone -> [catalog].[TenantMembershipList]
{
    public int TenantId { get; set; }
    public string Subject { get; set; }                     // varchar(255)
    public bool IsActive { get; set; }
}

public interface ITenantMembershipDirectory
{
    Task<IReadOnlyList<TenantMembership>> ListAsync(string subject);       // excludes Retired; one catalog query
    Task RecordAsync(IReadOnlyList<TenantMembershipRecord> records);       // upsert; best effort; never fails a save
}
```

`catalog.TenantMemberships` is a navigation hint, never an authorization source. Writers: spec
0017's `UserService.AfterCommitAsync` after an invite, activation or deactivation commits (a second
transaction against the catalog; a failure is logged and metered, never surfaced); the migrator's
provisioning (the bootstrap administrator's hint); and `MembershipReconcileService`, a hosted timer
in `Tellma.Core.AspNetCore` (`Tellma:Tenancy:MembershipReconcileInterval`, default 24 h; first run
five minutes after the first snapshot) that takes `sp_getapplock('tellma:membership-reconcile')` on
the catalog with a zero timeout, so exactly one instance runs, and rebuilds the hint for every
`Active` tenant from `core.Users (Subject, IsActive)` through `TenantMembershipList` with the
statements of §7.5 — one tenant query and one catalog transaction per tenant, a failed tenant
logged and the run continued. The timer repairs any post-commit write that failed, without a
deployment. Readers: `GET /bff/user` (the company picker), the sign-in redirect that skips the
picker for a single live membership (§5.5), and the admin
`GET /api/admin/tenants/{id}/members` (§5.9). The directory answers in one catalog query and never
opens a tenant database; every entry into a tenant is re-verified by the connect initializer, so a
stale hint can at most show a tenant that answers `404` or hide one reachable by URL until the next
deployment. The connect step never writes the hint.

### 3.9 The sandbox context

`TenantSandboxContext : ISandboxContext` (scoped, registered by `AddTellma`) answers
`RequestContext.Tenant.Category == Sandbox` and throws `InvalidOperationException` when no tenant is
bound: a side-effecting connector called outside a tenant scope is a bug that must surface on the
first call rather than leak (unbound treated as live) or hide behind undelivered mail (unbound
treated as sandbox). Tenantless work that legitimately sends mail — the in-proc identity engine's
invitation mail — runs in the identity engine's own composition, which registers its own
`ISandboxContext`. The invariant is structural: every tenant-routed endpoint passes the tenant
middleware (the audit refuses any that would not) and every background scope is created by the
factory.

## 4. The request context

### 4.1 The record

```csharp
// Tellma.Core.Abstractions.Tenancy
public enum PrincipalKind { Anonymous, User, ServiceAccount, System }

public sealed record AuthenticationAssurance(string? Acr, DateTimeOffset? AuthTime, DateTimeOffset? AcrAuthTime);

public sealed record RequestContext                         // immutable; one per unit of work
{
    public TenantDescriptor? Tenant { get; init; }          // null for tenantless work
    public PrincipalKind Kind { get; init; } = PrincipalKind.Anonymous;
    public string? Subject { get; init; }                   // sub, a service account's client id, or "system"
    public string? ClientId { get; init; }
    public string? SessionId { get; init; }                 // the authority's sid
    public string? SessionHandle { get; init; }             // the catalog session's handle (§7.4); cookie scheme only
    public AuthenticationAssurance? Assurance { get; init; }
    public int? UserId { get; init; }                       // late-bound by the connect initializer
    public string Language { get; init; } = "en";           // messages; BCP 47, no extensions
    public string Culture { get; init; } = "en";            // formatting; BCP 47, no extensions
    public CultureInfo CultureInfo { get; init; }           // Gregorian-forced clone of Culture
    public string Calendar { get; init; } = "gc";           // gc | uq | et
    public ICalendarSystem CalendarSystem { get; init; }
    public int ContentLanguageIndex { get; init; } = 1;     // 1..3; the tenant content language matching Culture, else 1
    public string TimeZone { get; init; } = "UTC";          // the display zone: header -> User.PreferredTimeZone -> tenant
    public string TenantTimeZone { get; init; } = "UTC";    // binds today() and the TimeZone slot
    public DateTimeOffset Now { get; init; }
    public DateOnly Today { get; init; }                    // in TenantTimeZone
    public TenantSettings? TenantSettings { get; init; }    // null for tenantless work
    public string Client { get; init; } = "web";            // web | mcp | worker — spec 0015's client set
    public string? OriginTraceParent { get; init; }         // log correlation only; never a tracing instruction
    public bool IsSandbox { get; }                          // derived
    public bool IsSystem { get; }                           // derived: Kind = System
    public bool IsServiceAccount { get; }                   // derived: Kind = ServiceAccount
    public TenantDescriptor RequireTenant();                // throws InvalidOperationException when not tenant-scoped
    public static RequestContext Tenantless { get; }        // the tenantless context
}

public sealed record RequestContextSnapshot(
    int TenantId, PrincipalKind Kind, string? Subject, int? UserId, string Client, string? TraceParent);

public interface IRequestContextAccessor
{
    RequestContext Current { get; }                         // Tenantless when nothing set one
}

public interface IRequestContextInitializer
{
    int Order { get; }
    Task<RequestContext> InitializeAsync(RequestContext context, RequestContextInputs inputs);
}

public sealed record RequestContextInputs(
    string? AcceptLanguage, string? RequestedCalendar, string? RequestedTimeZone, string? Client,
    string? MessageLanguage, string? MessageCalendar);
```

| Member | Meaning |
|---|---|
| `Tenant`, `Kind`, `Subject`, `ClientId`, `SessionId`, `SessionHandle`, `Assurance`, `Now`, `Client`, `OriginTraceParent` | Set by the tenant middleware (§4.3) or the scope factory (§4.4) before any initializer runs. `Now` is fixed at the start of the unit of work from `TimeProvider`. |
| `SessionId`, `SessionHandle` | `SessionId` is the authority's `sid`. `SessionHandle` is the handle of the `catalog.Sessions` row (§7.4), read by the tenant middleware from the cookie ticket's properties (§5.5); null on bearer surfaces and in background scopes. It is the value that addresses one BFF session across tenants (spec 0020 §6.2); the session key itself never leaves the session store. |
| `OriginTraceParent` | Log-correlation data: the scope factory copies the snapshot's `TraceParent` into it and the logger scope of §9 carries it. Nothing starts, parents or links an activity from it — the activity a scope runs under belongs to the caller (§4.4; spec 0019 §9). |
| `UserId` | Set by spec 0013's connect initializer at `Order` 100; `null` before it and for `Anonymous`. A `System` snapshot binds `WellKnownIds.SystemUserId`. |
| `Language`, `Culture`, `CultureInfo`, `Calendar`, `CalendarSystem`, `ContentLanguageIndex`, `TimeZone`, `TenantTimeZone`, `Today`, `TenantSettings` | Set by spec 0012's negotiation initializer at `Order` 200 with the precedence request header → the user's stored preference → the tenant's settings → platform defaults (`en`, `gc`, `UTC`); where set, the message inputs of `RequestContextInputs` come first (below). Culture names are stripped of `-u-` extensions before negotiation; the calendar is never encoded in the culture name. |
| `TimeZone` versus `TenantTimeZone` | The display zone formats instants in messages and exports; the tenant zone computes `Today` and binds spec 0008's `today()` and `TimeZone` slots. No client header asserts today: a client cannot move a tenant's business date. |
| `Client` | From the `Tellma-Client` header's name part, one of `TellmaApiOptions.ClientNames` (§5.4); the MCP request filter sets `mcp`. |
| `Tenantless` | `Tenant = null`, `Kind = Anonymous`, platform defaults; the holder's value on tenantless surfaces. |
| `RequestContextSnapshot` | The serialisable copy a background caller hands to `CreateScopeAsync` (§4.4): the principal, the client and the trace parent, never a locale field. Spec 0019's worker composes it from the job row's `RunAsUserId` and `TraceParent` (null for a partition of several jobs, spec 0019 §8); the migrator's step runner composes a `System` one (§6.2). The locale fields are resolved in the scope by spec 0012's negotiation initializer (§4.4). |
| `IRequestContextInitializer` | A pure function from a context and the request inputs to a new context; run in `Order` by the access guard and the scope factory. Spec 0013 at 100, spec 0012 at 200. |
| `RequestContextInputs` | On the web surface, the first four are extracted by the host from `Accept-Language`, `Tellma-Calendar`, `Tellma-Time-Zone` and `Tellma-Client`, and neither message member is passed; spec 0015 §11.4's MCP filter passes both. `MessageLanguage`, when set and offered by the language catalogue, is the message language and formatting culture; `MessageCalendar`, when set, is the formatting calendar even outside the tenant's pair; `ContentLanguageIndex` derives from the rest of the negotiation, never from `MessageLanguage` (spec 0012 §9.2). Empty in a background scope (§4.4). |

### 4.2 The holder and the two writers

```csharp
// Tellma.Core.Tenancy (runtime)
public interface IRequestContextHolder : IRequestContextAccessor   // scoped; platform-internal writer
{
    void Set(RequestContext context);
}

public interface ITenantAccessGuard
{
    Task EnsureAccessAsync(TenantAccessRequirement requirement);
}

public sealed record TenantAccessRequirement(bool IsMutation, RequireAssuranceMetadata? Assurance);

// Tellma.Core.Abstractions.Tenancy
public sealed record RequireAssuranceMetadata(string Acr, TimeSpan? MaxAge);   // endpoint metadata; stamped from IsSensitive by the projection
```

The holder is scoped. Nothing stores the context in an `AsyncLocal`, a static or a singleton;
singletons take `IServiceScopeFactory` and open a scope. `Activity.Current` carries
`tellma.tenant.id` as a diagnostics mirror only. Exactly two writers exist: the tenant middleware
plus the access guard for requests, and the scope factory for background work. Every service reads
through `IRequestContextAccessor.Current`; `IHttpContextAccessor` never appears in Core services.

### 4.3 Requests: the tenant middleware and the access guard

`TenantMiddleware` (`Tellma.Core.AspNetCore`, after `UseAuthorization` and the CSRF middleware)
reads `tenantId` from the endpoint's route values:

- **No route value** → the holder stays at `Tenantless`; the request proceeds (tenantless
  surfaces).
- **A route value** → `ITenantRegistry.Find` (no I/O). Unknown, `Retired` or `Provisioning` → `404
  tenant-not-found`; snapshot beyond `MaxStaleness` → `503 catalog-unavailable`; otherwise the
  holder is set with the tenant part, the principal part (`Kind` per §5.5, `Subject`, `ClientId`,
  `SessionId`, `Assurance` from the authenticated principal's `sub`, `azp`/`client_id`, `sid`,
  `acr`, `auth_time`, `tellma_acr_auth_time`; `SessionHandle` from the cookie ticket's properties,
  §5.5), `Now`, `Client`, and the request `Activity` is tagged `tellma.tenant.id`. Placement after
  authorization means an anonymous probe receives `401` before it can learn whether a tenant id
  exists; binding before the endpoint means a `TellmaDbContext` or any tenant-scoped service
  injected as a handler argument already sees the tenant.

`ITenantAccessGuard.EnsureAccessAsync(requirement)` is the single decision whether the bound
principal may proceed against the bound tenant. Two front doors call it: `TenantAccessFilter`, an
endpoint filter on every tenant route group, and spec 0015's MCP request filters (the MCP SDK maps
request delegates that endpoint filters do not reach). It runs, in order:

1. **State verdict** per §3.3 for the requirement's `IsMutation` (`Suspended`, `ReadOnly`
   mutations; a `Provisioning` tenant was already refused by the middleware).
2. **The initializers** in `Order`: spec 0013's connect step (resolves `Subject` or `ClientId` to
   the tenant user through `IUserConnector.Connect()`; a non-member or a deactivated user raises
   `TenantNotFoundException`; the verification may ride the first business batch, whose executor
   raises the same exception on a failed deferred check so the optimistic path fails closed), then
   spec 0012's negotiation.
3. **Assurance.** When `requirement.Assurance` is present: a `ServiceAccount` principal, which has
   no `auth_time` to step up, gets spec 0014's `HumanRequiredException` (403 `human-required`);
   otherwise, when the session's `acr` differs from `Assurance.Acr`, or `MaxAge` is set and
   `AcrAuthTime` (else `AuthTime`) is older than it, the guard raises
   `StepUpRequiredException(Acr, MaxAge)` — as does spec 0013's `RequireAsync` for a sensitive
   pair a service checks in code, under the same bar — which the host answers with spec 0003's
   `401 insufficient_user_authentication` challenge (`StepUpChallenge.Write`).

Each initializer returns a new immutable context; the guard sets the final one. After
`EnsureAccessAsync` returns, `UserId` is set or the request was refused — the whole contract the
service pipeline relies on. The guard touches no tenant database itself. Its verdicts meter
`tellma.tenancy.resolutions` by `outcome`.

### 4.4 Background work: the tenant scope factory

```csharp
// Tellma.Core.Abstractions.Tenancy
public interface ITenantScopeFactory
{
    Task<TenantScope> CreateScopeAsync(RequestContextSnapshot snapshot, bool allowNonActive = false);
}

public sealed record TenantScope(IServiceProvider Services) : IAsyncDisposable;
```

`CreateScopeAsync` re-resolves the tenant from the registry (its state may have changed) and throws
`TenantUnavailableException` for every state but `Active`; creates an `AsyncServiceScope`; sets the
holder from the snapshot with `Kind`, `Subject`, `UserId`, `Client` and `OriginTraceParent` (the
snapshot's `TraceParent`, log-correlation data per §4.1); and runs the initializers:
`IUserConnector.ConnectAsUser` for `User` and `ConnectAsSystem` for `System` (which binds
`WellKnownIds.SystemUserId`), then spec 0012's negotiation initializer at `Order` 200 with empty
`RequestContextInputs`, which resolves the locale fields from the run-as user's stored preferences,
then the tenant's settings (spec 0012 §9.3). It starts no activity: the caller owns the activity the
scope runs under — spec 0019 §9's worker opens the scope inside its `process <key>` root and the
migrator's step runner inside its step activity (§6.2) — so the connect and data spans nest under
the caller's. `allowNonActive: true` is the migrator's door: it also accepts `Provisioning` and
`ReadOnly` (never `Suspended` or `Retired`) and requires `Kind = System` — any other `Kind` with the
flag set throws `InvalidOperationException` before a scope exists. Only the migrator's step runner
(§6.2) passes it; a job scope never does. Callers: spec 0019's worker (the only background caller)
and the migrator's provisioning steps (`Kind = System`). Scopes meter `tellma.tenancy.scopes` by
`kind ∈ request | background`.

### 4.5 Listeners

```csharp
// Tellma.Core.Abstractions.Tenancy
public interface ITenantStateListener
{
    Task OnStateChangedAsync(TenantDescriptor tenant, TenantState? previous);
}

public interface ISessionTerminationListener
{
    Task SessionsTerminatedAsync(string subject, IReadOnlyList<string> sessionHandles);   // handles of §7.4, never keys
    Task TenantAccessRevokedAsync(int tenantId, string subject);
}
```

Both are implemented by spec 0020's `TellmaHub` connection tracker (close a suspended tenant's
connections; close a revoked user's connections for that tenant; end sessions), and
`ITenantStateListener` also by spec 0019's worker (pause polling for a non-`Active` tenant). The
registry calls the first; the session store (§5.5) calls `SessionsTerminatedAsync` on back-channel
logout and revocation with the handles (§7.4) of the deleted rows — a session key reaches no
listener and no log; spec 0017's `UserService` calls `TenantAccessRevokedAsync` after a
deactivation commits. Listener failures are logged, never thrown into the caller.

### 4.6 Exceptions

```csharp
// Tellma.Core.Abstractions.Tenancy — derive from spec 0014's TellmaException
public sealed class TenantNotFoundException(int TenantId) : TellmaException;   // 404 tenant-not-found
public sealed class TenantUnavailableException(
    int TenantId, TenantState? State, string Code, TimeSpan? RetryAfter) : TellmaException;   // 503 catalog-unavailable | tenant-schema-behind; 403 tenant-suspended | tenant-read-only
public sealed class StepUpRequiredException(string Acr, TimeSpan? MaxAge) : TellmaException;   // 401 step-up-required + WWW-Authenticate
```

`TenantNotFoundException` also covers a `Provisioning` tenant on a request, non-members and
deactivated users (raised by the tenant middleware, the connect initializer and the executor's
deferred verification). `TenantUnavailableException` with code `tenant-schema-behind`
(`State = null`, `RetryAfter` 30 s) is raised by spec 0011's executor when the tenant database's
recorded schema fingerprints do not include the running model's (spec 0011 §4.3). Spec 0015 maps
the three, and §3.7's `TenantStateException`, to problem details; CSRF rejection is
`403 csrf-rejected` (§5.4). Problem codes are kebab-case.

## 5. The web host

### 5.1 `AddTellma`, `UseTellma`, `MapTellma`

```csharp
// Tellma.Core.AspNetCore
public static class TellmaWebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddTellma(
        this WebApplicationBuilder builder, string slug, Action<TellmaBuilder> compose,
        Action<TellmaBuilder>? composeWeb = null);
}

public static class TellmaApplicationBuilderExtensions
{
    public static WebApplication UseTellma(this WebApplication app);
}

public static class TellmaEndpointRouteBuilderExtensions
{
    public static TellmaEndpoints MapTellma(this WebApplication app);
}

public sealed class TellmaEndpoints
{
    public RouteGroupBuilder Web { get; set; }
    public RouteGroupBuilder Api { get; set; }
    public RouteGroupBuilder Hub { get; set; }
    public RouteGroupBuilder Blobs { get; set; }
    public RouteGroupBuilder Tenantless { get; set; }
}

public static class TellmaEndpointConventions                   // fluent on the mapped endpoint
{
    public static TBuilder AsTenantlessEndpoint<TBuilder>(this TBuilder builder, string reason)
        where TBuilder : IEndpointConventionBuilder;
    public static TBuilder WithMutation<TBuilder>(this TBuilder builder, bool isMutation)
        where TBuilder : IEndpointConventionBuilder;
    public static TBuilder RequireSecurable<TBuilder>(this TBuilder builder, string resource, string action)
        where TBuilder : IEndpointConventionBuilder;
    public static TBuilder AllowMember<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder;
    public static TBuilder AcceptsBinary<TBuilder>(this TBuilder builder, long maxBytes)
        where TBuilder : IEndpointConventionBuilder;
}

public static class TellmaAuthentication
{
    public const string SessionScheme = "Tellma.Session";
    public const string OidcScheme = "Tellma.OpenIdConnect";
    public const string BearerScheme = "Tellma.Bearer";
    public const string SessionCookieName = "__Host-tellma.session";
    public const string ProfileCookieName = "tellma.profile";
    public const string CsrfHeaderName = "Tellma-Client";
}

public static class TellmaPolicies
{
    public const string Web = "Tellma.Web";
    public const string Api = "Tellma.Api";
    public const string Mcp = "Tellma.Mcp";
    public const string ControlPlane = "Tellma.ControlPlane";
}

public enum TenantSurface { Web, Api, Mcp, Hub, Blobs }

public sealed record TenantEndpointMetadata(TenantSurface Surface, bool IsMutation);
public sealed record TenantlessEndpointMetadata(string Reason);
public sealed record AcceptsBinaryMetadata(long MaxBytes);
```

- **`AddTellma(builder, slug, compose, composeWeb)`** calls
  `services.AddTellma(slug, configuration, environment, …)` (§2.3) with one composer that runs
  `compose` and then `composeWeb` when given on the same `TellmaBuilder`, so both pass the gates of
  §2.3 in one realisation (`composeWeb` is web-only composition: a realtime mode, service
  registrations that need `Tellma.Core.AspNetCore`; a feature or entity added there diverges the two
  hosts' models and fails the parity test of §1.3), then `AddTellmaAspNetCore(configuration)`: the
  schemes and policies (§5.5), the session store, CSRF options, Data Protection and forwarded
  headers (§5.8), the endpoint audit and health checks, `SessionSweepService`, Serilog from the
  `Serilog` section, OpenTelemetry (the platform meters and activity sources, ASP.NET Core,
  HttpClient and SqlClient instrumentation — the last with an enrich callback that removes
  `db.query.text` and `db.query.summary` from every span, so the batch text reaches logs only
  through spec 0011's slow-round-trip event, and the span is named per spec 0011 §6.1; Azure Monitor
  gated on `APPLICATIONINSIGHTS_CONNECTION_STRING`), and — when `Tellma:Identity:Mode = InProc` —
  `AddTellmaIdentity` (§5.7). `TellmaMigrator.RunAsync(args, slug, compose)` never sees
  `composeWeb`, so both hosts build their EF models from `compose` alone (§1.3).
- **`UseTellma()`** installs the pipeline in this fixed order and nothing else: forwarded headers
  (guarded as §5.8 states), Serilog request logging, the exception handler producing RFC 9457
  problem details (the mapping is spec 0015's), HSTS and HTTPS redirection outside Development,
  response compression (spec 0015 §8.4), `UseRouting`, `UseTellmaIdentity` (in-proc only),
  `UseAuthentication`, `UseAuthorization`, the rate limiter (spec 0015 §8.2; after authentication
  so the partition key is the principal), request timeouts (spec 0015 §8.3), output caching
  (spec 0015 §8.5), `TellmaCsrfMiddleware`, `TellmaContractMiddleware` (spec 0015 §3.10),
  `TenantMiddleware`. Custom middleware goes between `UseTellma()` and `MapTellma()`.
- **`MapTellma()`** maps `/api/distribution-info`, `/health/live`, `/health/ready`, `/bff/*`,
  `/api/admin/*` (when enabled, §5.9), `/api/webhooks/{key}` (when `Tellma.Core.Webhooks` is
  composed), the in-proc identity endpoints,
  `/.well-known/oauth-protected-resource/{tenantId:int:min(1)}/mcp`, the tenant route groups of §5.2
  filled by feature contributions (spec 0015's projected endpoints including settings, inbox, job
  and schedule endpoints, spec 0020's `/{tenantId}/hub`, spec 0016's `/{tenantId}/blobs/{kind}` and
  `/{tenantId}/blobs/{kind}/{id}`, spec 0015's `/{tenantId}/mcp`), static assets and the SPA
  fallback (`MapFallbackToFile("index.html")` when `wwwroot/index.html` exists, excluding every
  reserved prefix); then runs the endpoint audit (§5.3). It returns `TellmaEndpoints` so a
  distribution maps custom endpoints onto `Web` and inherits every filter.

| Member | Meaning |
|---|---|
| `AsTenantlessEndpoint(reason)` | Stamps `TenantlessEndpointMetadata(reason)`; required on every endpoint a distribution maps outside a tenant group. |
| `WithMutation(isMutation)` | Stamps `TenantEndpointMetadata.IsMutation` on a hand-mapped tenant endpoint. |
| `RequireSecurable(resource, action)` | Stamps spec 0013's `SecurableEndpointMetadata` on a hand-mapped tenant endpoint; the securable must be registered (spec 0013's audit). |
| `AllowMember()` | Stamps `MemberEndpointMetadata`: any active member may call; no securable. |
| `AcceptsBinary(maxBytes)` | Stamps `AcceptsBinaryMetadata`; the only endpoints with a non-JSON body (spec 0016's upload). |

### 5.2 Route groups and policies

- `Web` = `/{tenantId:int:min(1)}/api/web`, `RequireAuthorization(TellmaPolicies.Web)`,
  `AddEndpointFilter<TenantAccessFilter>()`, metadata `TenantEndpointMetadata(Web, …)`, spec 0015's
  JSON conventions.
- `Api` = `/{tenantId:int:min(1)}/api/v{version:apiVersion}`, policy `Tellma.Api` (the public API's
  seam; nothing mapped this release; spec 0015 §12.2).
- `Hub` = `/{tenantId:int:min(1)}/hub` and `Blobs` = `/{tenantId:int:min(1)}/blobs`, sibling groups
  on `TellmaPolicies.Web` with the same filter and their own `Surface`.
- The MCP endpoint `/{tenantId:int:min(1)}/mcp` on `Tellma.Mcp` when spec 0015's MCP feature is
  composed; the guard runs in the MCP request filters.
- `Tenantless` = `/api`, for tenantless endpoints a distribution adds; each must call
  `AsTenantlessEndpoint`.

The application's fallback authorization policy is `RequireAuthenticatedUser` under the
`Tellma.Session` scheme, so an endpoint with no authorization metadata denies anonymous callers.
`AllowAnonymous` is permitted only on an endpoint carrying `TenantlessEndpointMetadata`: the
information document, the health probes, the BFF login and back-channel receiver, the
protected-resource metadata document, the strings pack (spec 0012 §10.4), the webhook receivers,
the Development OpenAPI document (spec 0015 §12.1), the SPA fallback and the identity engine's own
endpoints.

### 5.3 The endpoint audit

`TellmaEndpointAudit` (`host.endpoint-audit`, a check over `EndpointDataSource` run by `MapTellma`
and again by the realised gate) fails startup, naming route and method, when:

- an endpoint whose route begins with the `tenantId` parameter lacks `TenantEndpointMetadata`, lacks
  a policy from the set allowed for its surface (`Web`, `Hub`, `Blobs` → `Tellma.Web`; `Api` →
  `Tellma.Api`; `Mcp` → `Tellma.Mcp`), carries `IAllowAnonymous`, or does not carry exactly one of
  `SecurableEndpointMetadata` and `MemberEndpointMetadata` (spec 0013 §4.5);
- a cookie-scheme `GET` endpoint is stamped `IsMutation = true`;
- a CORS policy is attached to any cookie-authenticated endpoint;
- an endpoint carrying `IAllowAnonymous` lacks `TenantlessEndpointMetadata`;
- any other endpoint has neither an authorization policy nor `TenantlessEndpointMetadata` — the
  platform stamps the routes it maps, the webhook receivers among them, assembly membership marks
  the in-proc identity engine's endpoints, and `AsTenantlessEndpoint` marks a distribution's;
- a distribution endpoint sits under a reserved prefix (`/bff`, `/api/admin`,
  `/api/distribution-info`, `/health`, `/id`, `/openapi`, `/.well-known`, `/signin-oidc`,
  `/signout-callback-oidc`).

The same walk checks the securable rules of spec 0013 §4.5 and the projection rules of spec 0015
§4.6.

### 5.4 CSRF

`TellmaCsrfMiddleware` (after `UseAuthorization`, before `TenantMiddleware`; it reads the matched
endpoint's metadata) is the one CSRF control of every surface, tenant and tenantless alike, and
runs ahead of every endpoint filter. It applies to every request authenticated by `Tellma.Session`
whose method is not `GET`, `HEAD` or `OPTIONS`, in order:

1. If `Sec-Fetch-Site` is present it must be `same-origin` or `none`; else `403 csrf-rejected`
   (`rule = sec_fetch_site`).
2. If `Origin` (or, when absent, `Referer`) is present its origin must equal `Tellma:PublicOrigin`;
   else `403` (`rule = origin`).
3. The request must carry `Tellma-Client: <name>/<version>` with `<name>` in
   `TellmaApiOptions.ClientNames` (default `web`) and a non-empty `<version>`; else `403`
   (`rule = header`). The name part is `RequestContext.Client` and spec 0015's `tellma.client`
   tag, a closed set; the version part is logged, never a metric tag. A custom header forces a
   CORS preflight for any cross-origin caller, and no CORS policy exists anywhere in the platform,
   so the preflight fails.
4. A request with a body must declare `Content-Type: application/json` (parameters ignored); an
   endpoint carrying `AcceptsBinaryMetadata` accepts any declared type instead, and the header
   stays required; else `415` (`rule = content_type`).

Cookie-authenticated `GET`s (`/bff/user`, blob downloads) and the hub negotiate — requests a
browser cannot decorate — are exempt from the header, subject to rules 1–2 when the headers are
present, and must be side-effect free (§5.3); the hub rejects a cross-origin negotiate outright.
Bearer surfaces and anonymous endpoints are exempt. `SameSite=Lax` stays: `Strict` breaks the
post-login redirect chain and deep links; the required header is the equivalent control.

### 5.5 Authentication: schemes, the session store, refresh, back-channel logout, BFF endpoints

```csharp
// Tellma.Core.AspNetCore
public interface ICatalogSessionStore : ITicketStore        // rows in catalog.Sessions; per-instance cache; revocation
{
    Task RevokeBySidAsync(string sid);
    Task RevokeBySubjectAsync(string subject);
    Task<int> SweepAsync();
}

public static class StepUpChallenge
{
    public static void Write(HttpResponse response, string acrValues, int? maxAge);
}

public interface IAuthenticationPolicyProvider              // login-time constraints; default none
{
    Task<LoginPolicy> GetLoginPolicyAsync(int? tenantId);
}

public sealed record LoginPolicy(string? AcrValues, TimeSpan? MaxAge, IReadOnlyList<string>? AllowedMethods);
```

- **Cookie scheme `Tellma.Session`.** Cookie `__Host-tellma.session` (`tellma.session` under plain
  HTTP in Development), `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`; sliding
  `Tellma:Session:IdleLifetime` (default 7 days, equal to the authority's refresh-token idle
  window); absolute `Tellma:Session:AbsoluteLifetime` (default 90 days); `SessionStore =
  CatalogSessionStore`; `EventsType = TellmaSessionEvents`. The cookie carries only a 256-bit random
  session key (43-character base64url); the ticket — principal, `sid`, tokens — is
  `TicketSerializer` output protected by an `IDataProtector` (purpose
  `Tellma.Core.AspNetCore.Session`) in `catalog.Sessions`. At creation the store computes the row's
  `Handle` (§7.4) and stamps it into the ticket's properties, so every `RetrieveAsync` returns it
  with the ticket and the tenant middleware binds it as `RequestContext.SessionHandle` (§4.3); the
  key stays inside the cookie and the store. `RetrieveAsync` consults a per-instance bounded
  `MemoryCache` (`Tellma:Session:CacheTtl`, default 60 s, 50,000 entries) and falls back to one
  primary-key read (§7.5); `RenewAsync` writes through; `RemoveAsync` deletes and evicts. The TTL
  is the maximum time a revoked session keeps working on an instance other than the one that
  revoked it. `IApiEndpointMetadata` makes API endpoints answer `401`/`403` instead of redirecting.
- **OIDC scheme `Tellma.OpenIdConnect`.** `Authority = Tellma:Identity:Authority` (the host's own
  origin + `/id` in in-proc mode), `ClientId = slug`, `ClientSecret` from configuration,
  `ResponseType = code`, PKCE, `PushedAuthorizationBehavior = Require` (spec 0003 carries assurance
  and allowed methods inside PAR; a downgrade fails closed), `SaveTokens = true`,
  `MapInboundClaims = false`, `GetClaimsFromUserInfoEndpoint = false`, scopes
  `openid profile email offline_access tellma_api`, `CallbackPath = /signin-oidc`,
  `SignedOutCallbackPath = /signout-callback-oidc`. Principal claims kept: `sub`, `sid`, `name`,
  `email`, `locale`, `acr`, `amr`, `auth_time`, `tellma_acr_auth_time`. `OnTokenValidated` rejects
  a principal without `sub` or `sid`.
- **Refresh-anchored liveness** (`TellmaSessionEvents.ValidatePrincipal`). When the stored access
  token expires within `Tellma:Session:RefreshSkew` (default 60 s) the event re-reads the session
  row bypassing the cache; if `TokensVersion` already moved (another instance refreshed) it adopts
  the newer ticket; otherwise it redeems the refresh token at the authority and writes the new
  ticket with the compare-and-set of §7.5; a lost CAS re-reads and adopts. Success →
  `ReplacePrincipal` (the refreshed id token's `acr`, evidence time and `locale` replace the
  principal's) + `ShouldRenew`. `invalid_grant` or any 4xx → `RejectPrincipal`, the row is deleted,
  the SPA receives `401` and re-enters the challenge, so authority-side sign-out-everywhere, policy
  tightening and user disablement take effect within one access-token lifetime even when
  back-channel delivery failed. A 5xx or network failure keeps the session for up to
  `Tellma:Session:MaxAuthorityOutage` (default 1 h), after which the session is rejected. Concurrent
  requests on one session refresh once (in-process single flight per key; the authority's 30 s
  reuse leeway covers the cross-instance race the CAS does not).
- **Back-channel logout receiver** `POST /bff/backchannel-logout` (anonymous by design,
  `application/x-www-form-urlencoded`, parameter `logout_token`; the URI provisioning registers as
  `tellma:backchannel_logout_uri`). Validation with the OIDC handler's own configuration manager:
  signature against the JWKS, `iss`, `aud = slug`, `typ = logout+jwt`, `iat` within ±5 min, `events`
  containing `http://schemas.openid.net/event/backchannel-logout`, no `nonce`, `sid` or `sub`
  present. Then the delete of §7.5 (by `Sid`, or by `Subject` when only `sub` is present) with
  `OUTPUT deleted`, local eviction by the output keys,
  `ISessionTerminationListener.SessionsTerminatedAsync(subject, handles)` with the output handles,
  and `200`. Any validation failure answers `400` with no body. A replayed token deletes nothing
  and answers `200`; `jti` is not tracked. The handler's `RemoteSignOutPath` is front-channel only
  and stays unused.
- **BFF endpoints** (tenantless, cookie scheme): `GET /bff/login?returnUrl=&acr_values=&max_age=`
  (local-only `returnUrl`; challenges the OIDC scheme, forwarding `acr_values`/`max_age` and the
  `tellma_allowed_methods` supplied by `IAuthenticationPolicyProvider.GetLoginPolicyAsync(null)` —
  initial sign-in requests no tier because the user has no tenant yet). After the callback the BFF
  redirects to `returnUrl` when one was given; otherwise, when exactly one membership of §3.8 is
  `Live`, `Active` and `isActive`, straight into `/{tenantId}/`, skipping the picker; else to `/`,
  the picker. `POST /bff/logout` (`{ "scope": "local" | "global" }`, default `global`: local
  sign-out then RP-initiated end-session with `id_token_hint`; clears `tellma.profile`); `GET
  /bff/user` → `{ sub, name, email, locale, acr, tenants: [{ id, name, name2, name3, languages,
  category, state, liveTenantId, isActive }] }` from §3.8 — each entry is §3.3's `TenantDescriptor`
  plus `isActive` — with `401` when unauthenticated. On that callback redirect the BFF also writes,
  once, the readable profile cookie `tellma.profile`: `{ v, name, locale }` — the ID token's `name`
  and `locale` claims as of sign-in, so the tenantless shell (the picker) renders without a request;
  `v` versions the shape. It is `Path=/`, not `HttpOnly`, carries no token and nothing
  tenant-scoped, has the session cookie's absolute lifetime, is never rewritten, and is cleared by
  `/bff/logout`. Its presence is the SPA's synchronous signed-in signal: without it the SPA goes to
  `/bff/login` before spending a request. Everything a tenant shell needs — company name,
  preferences, landing page, calendar — is tenant data the BFF never reads: the SPA takes it from
  `me` (spec 0013) and `settings/client` (spec 0012), caches both per tenant in local storage, and
  validates the cache by version tags on its first request (spec 0015); a returning visit reopens
  the last-used tenant from that cache and falls back to the picker on `403` or `404`.
- **Bearer scheme `Tellma.Bearer`** (`JwtBearer`): `Authority = Tellma:Identity:Authority` (the
  issuer the OIDC scheme uses), the JWKS cached by the handler's configuration manager (no
  per-request identity call), issuer validated, `MapInboundClaims = false` (so `sub` stays `sub`),
  `RequireHttpsMetadata` outside Development, `ClockSkew` 60 s against 10-minute access tokens,
  `ValidTypes = ["at+jwt"]`, `NameClaimType = "sub"`; the audience is validated per request against
  the canonical resource of the route (§5.6), the MCP resource's validator and challenge being spec
  0015 §11.2's.
- **Policies.** `Tellma.Web` = scheme `Tellma.Session` + authenticated; `Tellma.Api` =
  `Tellma.Bearer` + scope `tellma_api` + audience = `Tellma:PublicOrigin`; `Tellma.Mcp` =
  `Tellma.Bearer` + `tellma_api` + the tenant's MCP audience; `Tellma.ControlPlane` =
  `Tellma.Bearer` + scope `tellma_control_plane` + audience = `Tellma:PublicOrigin` (the grant is
  spec 0021 §6). A policy names its scheme explicitly: a cookie never authenticates a bearer
  surface and a bearer never authenticates the web surface.
- **`PrincipalKind`** at binding: cookie → `User`; bearer with `auth_time` → `User` (a person
  through Claude Code, Codex, the CLI); bearer without it → `ServiceAccount` (`Subject` = the client
  id). A token carrying `tellma_kind` (spec 0021 §7) binds from the claim; the `auth_time`
  inference covers a token without it.
- **Step-up.** `Tellma:Session:StepUp { Acr, MaxAge }` is the bar spec 0015's projection stamps as
  `RequireAssuranceMetadata` on every endpoint whose securable is `IsSensitive`;
  `StepUpChallenge.Write` emits `401` with `WWW-Authenticate: Bearer
  error="insufficient_user_authentication", acr_values="…", max_age=…` per spec 0003.
- **Revocation on deactivation is tenant-level.** Deactivating a user in one tenant records
  `IsActive = false` in the hint table and calls `TenantAccessRevokedAsync(tenantId, subject)`; the
  next connect on that tenant answers `404`; the distribution session survives because the user may
  belong to other tenants (`/bff/user` shows `isActive: false`). `RevokeBySubjectAsync` exists for
  distribution-wide removal (the control plane, or a `sub`-only logout token).
- **`SessionSweepService`** — a hosted timer in `Tellma.Core.AspNetCore`
  (`Tellma:Session:SweepInterval`, default 15 min) calling `ICatalogSessionStore.SweepAsync`, whose
  statement (§7.5) is set-based, idempotent and safe on every instance at once; it is not a job,
  because `core.Jobs` is per tenant and nothing tenantless can ride it; `MembershipReconcileService`
  (§3.8) is its sibling.

### 5.6 MCP topology and the protected-resource document

`/{tenantId:int:min(1)}/mcp` is mapped inside the tenant group when spec 0015's MCP feature is
composed: the SDK's stateless mode (no affinity), policy `Tellma.Mcp`, the access guard in the MCP
request filters, the same context population as the web surface with `Client = "mcp"`. Its
RFC 8707 resource identifier is `{PublicOrigin}/{tenantId}/mcp`. The platform serves the RFC 9728
document at `/.well-known/oauth-protected-resource/{tenantId:int:min(1)}/mcp` (anonymous,
tenantless, `Cache-Control: public, max-age=300`) with `resource` = that URI,
`authorization_servers = [Tellma:Identity:Authority]`, `scopes_supported = ["tellma_api"]`,
`bearer_methods_supported = ["header"]`; the `401` challenge names it. Token validation requires the
exact endpoint audience `{PublicOrigin}/{tenantId}/mcp`; membership is verified by the connect
initializer. Human users arrive through a client ID metadata document or a pre-registered public
native client and are resolved by `sub`; autonomous agents wait for the machine-token transport
(spec 0015). An agent that needs two tenants configures two servers; live and sandbox are two URLs.
The server name is `tellma-tenant`; tool shape and listing are spec 0015's; the developer-tooling
server stays `dotnet tellma mcp`.

### 5.7 In-proc identity mode

`Tellma:Identity:Mode = InProc` makes `AddTellma` call `AddTellmaIdentity` with `PathBase = /id`,
`Issuer = {PublicOrigin}/id`, and the identity store pointed at the catalog connection (schema
`idsvr`, migrated by the identity engine's own migrations — the one schema the web process may
migrate, because the engine already owns that path). The OIDC handler's authority is then
`{PublicOrigin}/id`; `UseTellma` places `UseTellmaIdentity()` after routing; `MapTellma` maps its
endpoints and marks them tenantless.

The in-proc distribution owns its BFF client (`<slug>`) and service client (`<slug>-svc`) with
stable secrets from `Tellma:Identity:ClientSecret` and `Tellma:Identity:ServiceClientSecret`
(`host.identity` fails outside Development when either is absent), because a standalone deployment
may run several instances and a per-boot generated secret would invalidate the other instances'.
The engine's `Distribution` seed kind (spec 0021) carries `Origin`, `BackchannelLogoutUri`,
`ClientSecret` and `ServiceClientSecret`; `AddTellma` writes that seed entry from configuration and
`PublicOrigin`. In Development, when `ClientSecret` is absent, `AddTellma`
generates one per boot.

Development defaults scaffolded in `appsettings.Development.json`: in-proc mode, the development
administrator (`admin@localhost`, subject `00000000-0000-0000-0000-000000000001`), development
signing keys, the email log sink, and `Tellma:Seed:Tenants` (tenant 1 "Acme" Live, tenant 2 "Acme
Sandbox" Sandbox of 1). The migrator's `migrate` provisions both, and in each the bootstrap step
creates the tenant administrator on the development subject, so the first sign-in is the email-code
path with the code in the console.

### 5.8 Host baseline: Data Protection, forwarded headers, TLS, hosting

- **Data Protection.** `SetApplicationName("tellma-" + slug)` so slot swaps keep sessions valid
  and spec 0012's `Secret` settings readable: the application name and the key ring are part of
  every secret's protection, so a re-slugged deployment or a fresh key ring reads every secret as
  unset.
  Keys: Azure Blob + Key Vault key when `Tellma:DataProtection:BlobUri` and `KeyId` are set (the
  host's `TokenCredential`), otherwise `Tellma:DataProtection:KeyRingPath` (a shared folder,
  optionally `ProtectKeysWithCertificate`). Outside Development, neither configured is the startup
  failure `host.data-protection`; `/health/ready` verifies the key ring loads.
- **Forwarded headers.** `AddTellmaForwardedHeaders` requires explicit `KnownProxies` or
  `KnownNetworks` under `Tellma:ForwardedHeaders` and refuses `ASPNETCORE_FORWARDEDHEADERS_ENABLED`
  and enabled-but-empty.
- **TLS.** HTTPS redirection and HSTS outside Development; the `__Host-` prefix makes plain HTTP
  structurally unable to carry a session. `Tellma:PublicOrigin` is required, absolute, `https`
  outside Development and path-less; redirect URIs, MCP resource identifiers, the back-channel URI
  and the CSRF origin check all derive from it.
- **Hosting.** App Service health checks point at `/health/ready`; `Always On` is required;
  `WEBSITES_CONTAINER_STOP_TIME_LIMIT = 30` so spec 0019's drain completes; the host
  `ShutdownTimeout` is 30 s.

### 5.9 Information, health and admin surfaces

```csharp
// Tellma.Core.AspNetCore
public sealed record DistributionInfo(
    string Slug, string DisplayName, string DeploymentId, string PlatformVersion, string DistributionVersion,
    IdentityInfo Identity, IReadOnlyDictionary<string, string?> Surfaces, string Login);

public sealed record IdentityInfo(string Authority, string Mode);

public interface ITenantProvisioningTrigger                 // Tellma.Core.Tenancy
{
    Task StartAsync(int tenantId);
}
```

- **`GET /api/distribution-info`** (anonymous, `Cache-Control: public, max-age=60`): the
  `DistributionInfo` document — `slug`, `displayName` (`Tellma:DisplayName`), `deploymentId`,
  `platformVersion`, `distributionVersion` (`DeploymentVersions`, §2.2: informational versions that
  carry the source commit),
  `identity: { authority, mode: "Standalone" | "InProc" }`,
  `surfaces: { "web": "/{tenantId}/api/web", "mcp": "/{tenantId}/mcp" | null, "api": null }`,
  `login: "/bff/login"`. No tenant list, no secrets, no instance identity.
- **`GET /health/live`** (process up; no I/O) and **`GET /health/ready`** (registry snapshot loaded
  and within `MaxStaleness`; Data Protection keys loadable; the authority's discovery document
  cached), both anonymous, both tenantless.
- **Admin surface** (`Tellma.ControlPlane`, tenantless, JSON; mapped only when
  `Tellma:Admin:Enabled` is true, which defaults to true under `Tellma:Identity:Mode = Standalone`
  and to false under `InProc`, whose engine seeds no control-plane client — an on-premises operator
  uses the migrator commands of §6.1 instead; `/api/admin` stays a reserved prefix either way):
  `GET /api/admin/info` (the information document plus tenant counts by state);
  `GET /api/admin/tenants` (descriptors, never locations); `POST /api/admin/tenants/{id}/state` with
  `{ "state": "Active" | "ReadOnly" | "Suspended" | "Retired", "reason": "…" }` → the descriptor
  plus `{ "effectiveWithinSeconds": 15 }`, `409` for an illegal transition;
  `POST /api/admin/tenants` with `TenantRegistration` → registers a `Provisioning` row under a
  sequence-allocated id (the caller never chooses one: `RegisterAsync` without `requestedId`, §3.7)
  and calls `ITenantProvisioningTrigger.StartAsync`, `501` while only
  `NotConfiguredProvisioningTrigger` is registered; `GET /api/admin/tenants/{id}/members` (the hint
  table). The policy's audience and its grant are §5.5's.

The identity-server changes this spec and its siblings depend on — the `Distribution` seed client
kind, per-tenant resources under a granted origin, client ID metadata documents, the interim native
clients, the control-plane audience, the optional `tellma_kind` claim and the invite API's
`existingOnly` flag — are specified by spec 0021, which ships before this spec and the rest of the
family (specs 0010–0020).

## 6. Provisioning and the migrator

### 6.1 Commands

```csharp
// Tellma.Core.Migrator
public static class TellmaMigrator
{
    public static Task<int> RunAsync(
        IReadOnlyList<string> args, string slug, Action<TellmaBuilder> compose);   // exit 0 converged, 1 usage, 2 partial failure
}

public abstract class TellmaDesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext>
{
    public abstract void Compose(TellmaBuilder builder);    // the distribution points it at its Compose
}
```

`RunAsync` builds a generic host with the same composition and runs one command:

| Command | Does |
|---|---|
| `migrate [--catalog-only] [--tenant <id>]* [--all-tenants] [--parallelism N]` | Migrates the catalog under `sp_getapplock('tellma:migrate:catalog')` (`idsvr` too in in-proc mode); registers (the entry's `Id` as `RegisterAsync`'s `requestedId`, §3.7) and provisions every `Tellma:Seed:Tenants` entry not yet in the catalog; then for every `Active`, `ReadOnly` or `Provisioning` tenant (the default and `--all-tenants`) or the named ones: the tenant lock of §7.5, `Migrate()`, the schema fingerprint row of spec 0011 §4.3, the `tellma_app` grants (§6.4), the version-tag seed of spec 0012, provisioning steps whose recorded `Version` is behind, spec 0013's `IPermissionDriftScanner.ScanAsync` with its items printed. Bounded parallelism (default 4), continues past failures, per-tenant report. |
| `provision [--tenant <id>] [--name …] [--category Live\|Sandbox] [--live-tenant <id>] [--server …] [--database …] [--admin-email …] [--admin-subject …]` | With `--name` and `--category`, inserts the row as `Provisioning` (`--tenant` is then `RegisterAsync`'s `requestedId`, §3.7, else the sequence allocates one); with `--tenant` alone, resumes the `Provisioning` row of that id (the admin surface's). `CREATE DATABASE` (Azure: `(EDITION = …, SERVICE_OBJECTIVE = ELASTIC_POOL(name = …))` from `Tellma:Provisioning:CreateDatabaseTemplate`; on-prem: plain); `ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON` and `SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = AUTO, MAX_STORAGE_SIZE_MB = 1024, CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30))` on the fresh database; the application principal and `tellma_app`; migrations and the schema fingerprint row; the version-tag seed; every step in `Order`; the administrator's membership hint; the administrator's invite (spec 0017 §6.1; `--admin-subject` is Development-only, §6.2); then `SetStateAsync(Active, actor: "migrator")`. Idempotent: rerunning converges. |
| `set-state --tenant <id> --state … [--reason …]` | `ITenantCatalog.SetStateAsync` with actor `migrator`. |
| `status` | Lists tenants with state, the recorded schema fingerprints (`dbo.__TellmaSchema`, spec 0011 §4.3) against the running model's, and pending migrations; runs `TellmaComposition.Validate` and prints its problems. |

The migrator is the single DDL-privileged executable; `MigrationsAssembly` is the distribution's
Migrator project. The design-time factory `AcmeDesignTimeFactory :
TellmaDesignTimeDbContextFactory<TellmaDbContext>` overrides `Compose` so `dotnet ef` builds the
context from the same composition without host discovery. The migrator holds
`Tellma:Identity:ServiceClientSecret` so `provision` can invite a deployed tenant's administrator
through the `invite` action of spec 0017's `UserService` (`ExecuteActionAsync`, spec 0017 §6.1)
inside the system scope after the steps complete.

### 6.2 Provisioning steps

```csharp
// Tellma.Core.Abstractions.Tenancy
public sealed record TenantProvisioningContext(
    TenantDescriptor Tenant, IServiceProvider Services, bool IsNew, string? AdminSubject, string? AdminEmail);

public interface ITenantProvisioningStep                    // registered through FeatureContribution.ProvisioningStep<T>()
{
    string Name { get; }                                    // key in dbo.__TellmaProvisioning: platform "core.<step>", packs "<module>.<step>", distributions "<slug>.<step>"
    int Order { get; }                                      // platform 0–99, packs 100–199, distributions 200+
    int Version { get; }                                    // the step re-runs when it exceeds the recorded version
    Task RunAsync(TenantProvisioningContext context);
}
```

Steps run in `Order` inside a tenant scope and through the service pipeline. The step runner creates
the scope from a `System` snapshot — `Kind = System`, `Subject = "system"`,
`UserId = WellKnownIds.SystemUserId`, `Client = "worker"` and a null `TraceParent` — with
`ITenantScopeFactory.CreateScopeAsync(snapshot, allowNonActive: true)` (§4.4), because the tenant is
`Provisioning` on `provision` and may be `ReadOnly` on `migrate`. The step runner opens one
`Provisioning` frame per step (spec 0014 §13.3) and registers it in the scope as `IOpenWriteHost`;
the step injects it, enlists its writes with it and awaits its `PersistAsync`. The step runner
starts one root activity per step, named after the step's `Name`, the way spec 0019 §9's worker
starts `process <key>` — `Activity.Current` cleared, no parent — and opens the scope inside it, so
the step's connect and data spans nest under it. Each completion is recorded in the tenant
database's `dbo.__TellmaProvisioning` table (`Step nvarchar(128) PK`, `Version int NOT NULL`,
`CompletedAt datetimeoffset(3) NOT NULL`, `PlatformVersion nvarchar(64) NOT NULL`). A step runs when
no row exists or its `Version` exceeds the recorded one; a step with the same name registered twice
is a composition problem. `IsNew` is true while the tenant is `Provisioning` — under `provision` and
under `migrate`'s provisioning of a seed tenant — and false on a `migrate` of an `Active` or
`ReadOnly` tenant. `AdminEmail` comes from `--admin-email` or, in Development, from
`Tellma:Seed:AdminEmail`. `AdminSubject` is Development-only: it comes from `--admin-subject`, or is
the fixed development subject when `Tellma:Seed:AdminEmail` supplies the email; the bootstrapper
refuses a subject outside Development (spec 0013 §11), and a deployed administrator obtains one
through the invite of spec 0017 §6.1.

Platform steps: `10 core.bootstrap-administrator` (calls spec 0013's
`ITenantBootstrapper.BootstrapAdministrator(TenantBootstrapRequest(Email = AdminEmail, Name =
AdminEmail's local part, PreferredLanguage = the distribution default, Subject = AdminSubject))`; a
deployed administrator is then invited by the provisioning flow); `15 core.settings` (spec 0012
completes the settings placeholder row); `20 core.blob-container` (spec 0016 pre-creates the
tenant's container). Spec 0017 adds `100 gl.sample-centers`. There is no version-tag step: the
migrator seeds `core.VersionTags` from the registry on every `migrate`. `HasData` is used only for
the reserved id band (system user, Administrator role, permission and membership, the `UserStamps`
row, the `core.Settings` placeholder row, built-in schedules and their states, `JobWorkerState`).
Tenant-specific external integrations are steps a distribution ships.

### 6.3 The provisioning trigger

`ITenantProvisioningTrigger.StartAsync(tenantId)` is the self-serve seam the admin surface calls
after `RegisterAsync`. The platform ships `NotConfiguredProvisioningTrigger` (throws
`InvalidOperationException` naming the registration; the admin surface maps it to `501`); the
reference distribution ships a Development-only local process starter that runs its own Migrator
project with `provision --tenant <tenantId>`; a Container Apps Job starter is infrastructure work
outside this spec. Sandbox cloning (`CREATE DATABASE … AS COPY OF` on Azure, backup-restore
on-prem) is a later command; the row shape supports it.

### 6.4 Role and grants, recomputed after every run

```sql
IF DATABASE_PRINCIPAL_ID('tellma_app') IS NULL CREATE ROLE [tellma_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[<S>] TO [tellma_app];         -- every mapped schema
GRANT UPDATE ON OBJECT::[<S>].[sq_<Table>] TO [tellma_app];                     -- every sequence
-- EXECUTE ON TYPE is emitted by the table-type migrations (spec 0001)
-- provisioning only, SaaS:
CREATE USER [<web app identity>] FROM EXTERNAL PROVIDER;  ALTER ROLE [tellma_app] ADD MEMBER [<web app identity>];
-- provisioning only, on-prem:
CREATE USER [tellma_app_user] FOR LOGIN [tellma_app];  ALTER ROLE [tellma_app] ADD MEMBER [tellma_app_user];
-- provisioning only, fresh database (a no-op on Azure SQL, where it is the default):
ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON;
-- provisioning only, fresh database: the query store backs plan-regression and slow-statement analysis
ALTER DATABASE CURRENT SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = AUTO, MAX_STORAGE_SIZE_MB = 1024, CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30));
```

The web application's identity name comes from `Tellma:Sql:ApplicationPrincipal`.

### 6.5 The tenant model host

`AddTellma` registers `TellmaDbContext` (`Tellma.Core.Data`), whose `OnModelCreating` applies every
`ModelContributionItem` in feature order and calls `UseTableTypes(...)`. Migrations live in the
distribution's Migrator project. The context is platform-internal: no derived context, no injection
into distribution or pack code and no LINQ surface (spec 0011 §1.3); every distribution writes zero
data-access code. Spec 0011 owns everything the context exposes beyond this registration.

### 6.6 Local development

From a fresh clone: `pnpm install` in the Client project, then `dotnet run --project
distributions/acme/src/Tellma.Distro.Acme.Migrator -- migrate` (creates `Tellma.dev.acme.catalog`,
provisions tenants 1 and 2 from `Tellma:Seed:Tenants` with the development administrator), then
`dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Web`, which starts the Angular dev
server through the SPA proxy (§1.2) and opens `https://localhost:4200/`; sign in as
`admin@localhost` with the code from the console. A missing catalog at web startup prints that exact
`migrate` command instead of a stack trace. Development database names are `Tellma.dev.<slug>.*`;
`launchSettings.json` is tracked with fixed ports (`7052` for the Web project, `4200` for the dev
server) until the `dotnet tellma` CLI exists.

## 7. The catalog schema

All tables live in the catalog database, schema `catalog`, created by the platform-owned migrations
in `Tellma.Core.Migrator` (history table `catalog.__EFMigrationsHistory`) through `CatalogDbContext`
(`Tellma.Core.Tenancy`). No IDENTITY; `datetimeoffset(3)`; enum columns `nvarchar(16)` with CHECK
constraints (the catalog keeps its own conventions, its sequence start included — §7.1; tenant
tables follow spec 0011's); explicit constraint names; no temporal tables; no tenant-data version
tags.

### 7.1 `catalog.Tenants`

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | `PK_Tenants`; sequence `catalog.sq_Tenants` (`START WITH 1`) | the leading route segment; never reused |
| `Name` | `nvarchar(255)` | no | | display name; mirrored from tenant settings by `RenameAsync` |
| `Name2`, `Name3` | `nvarchar(255)` | yes | | the second and third names of the group |
| `Language1` | `varchar(35)` | no | | the primary content-language code (BCP 47); the distribution's default language at registration, mirrored with the names afterwards |
| `Language2`, `Language3` | `varchar(35)` | yes | `CK_Tenants_Names ((Name2 IS NULL) = (Language2 IS NULL) AND (Name3 IS NULL) = (Language3 IS NULL))` | the second and third codes, each paired with its name |
| `Category` | `nvarchar(16)` | no | `CK_Tenants_Category IN ('Live', 'Sandbox')` | immutable after registration |
| `LiveTenantId` | `int` | yes | `FK_Tenants_LiveTenantId → catalog.Tenants(Id)`; `CK_Tenants_LiveTenantId (Category = 'Sandbox' OR LiveTenantId IS NULL)` | a sandbox's source live tenant; null for a standalone sandbox |
| `State` | `nvarchar(16)` | no | `CK_Tenants_State IN ('Provisioning', 'Active', 'ReadOnly', 'Suspended', 'Retired')` | |
| `StateReason` | `nvarchar(1024)` | yes | | operator text |
| `StateChangedAt` | `datetimeoffset(3)` | no | | |
| `StateChangedBy` | `nvarchar(128)` | yes | | subject, client id, or `migrator`; not an FK |
| `Server` | `nvarchar(255)` | no | `CK_Tenants_Server (Server NOT LIKE '%[;=]%')` | host, or host,port |
| `Database` | `nvarchar(128)` | no | `CK_Tenants_Database (Database NOT LIKE '%[;=]%')` | |
| `CredentialProfile` | `nvarchar(64)` | no | `DF_Tenants_CredentialProfile 'default'` | a configured profile name |
| `Properties` | `nvarchar(max)` | yes | | non-secret, distribution-defined JSON |
| `Version` | `uniqueidentifier` | no | | `NEWID()` on every update; carried on `TenantInfo.Version` so a snapshot reload can tell which rows changed (listener calls, pooled-factory drop) |
| `CreatedAt` | `datetimeoffset(3)` | no | | |
| `ModifiedAt` | `datetimeoffset(3)` | no | | |

Indexes: `IX_Tenants_State (State)`, `IX_Tenants_LiveTenantId (LiveTenantId) WHERE LiveTenantId IS
NOT NULL`, `UX_Tenants_Location (Server, Database)`. No password, key, token or connection-string
column may ever be added; a model test asserts the column set.

### 7.2 `catalog.CatalogState`

One row: `Id int PK_CatalogState CK_CatalogState_Id (Id = 1)`, `Version uniqueidentifier NOT NULL`,
`ModifiedAt datetimeoffset(3) NOT NULL`; seeded `(1, NEWID(), SYSUTCDATETIME())` by the first
migration.

### 7.3 `catalog.TenantMemberships`

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Subject` | `varchar(255)` | no | `PK_TenantMemberships (Subject, TenantId)`; `COLLATE Latin1_General_100_BIN2` | the identity `sub` or a service account's client id; case-sensitive, as spec 0013 compares it |
| `TenantId` | `int` | no | PK; `FK_TenantMemberships_TenantId → catalog.Tenants(Id)` | |
| `IsActive` | `bit` | no | | |
| `UpdatedAt` | `datetimeoffset(3)` | no | | |

Index `IX_TenantMemberships_TenantId (TenantId)`. Table type `[catalog].[TenantMembershipList]
(TenantId int, Subject varchar(255) COLLATE Latin1_General_100_BIN2, IsActive bit)` is the
standalone `[TableType]` class `TenantMembershipRecord` registered on `CatalogDbContext` (the
physical name carries spec 0001's content hash).

### 7.4 `catalog.Sessions`

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Key` | `varchar(64)` | no | `PK_Sessions`; `COLLATE Latin1_General_100_BIN2` | base64url of 32 CSPRNG bytes; never leaves the store and the cookie |
| `Handle` | `char(22)` | no | `COLLATE Latin1_General_100_BIN2` | base64url of the first 16 bytes of SHA-256 over `Key`, computed by the store at creation; the session's name for listeners, hub groups (spec 0020 §6.2) and logs |
| `Subject` | `varchar(255)` | no | `COLLATE Latin1_General_100_BIN2` | the identity `sub` |
| `Sid` | `nvarchar(128)` | yes | | the authority's session id |
| `Ticket` | `varbinary(max)` | no | | Data-Protection-encrypted `TicketSerializer` output |
| `TokensVersion` | `int` | no | | compare-and-set for refresh |
| `CreatedAt` | `datetimeoffset(3)` | no | | |
| `RenewedAt` | `datetimeoffset(3)` | no | | |
| `ExpiresAt` | `datetimeoffset(3)` | no | | last renewal + `IdleLifetime`, capped by `CreatedAt` + `AbsoluteLifetime` |

Indexes: `IX_Sessions_Sid (Sid) WHERE Sid IS NOT NULL`, `IX_Sessions_Subject (Subject)`,
`IX_Sessions_ExpiresAt (ExpiresAt)`.

### 7.5 Statements

Parameterised; `SET XACT_ABORT ON` wherever a transaction is opened.

```sql
-- §3.4: refresh probe, every RefreshInterval per instance
SELECT [Version] FROM [catalog].[CatalogState] WHERE [Id] = 1;

-- §3.4: snapshot reload, only when the version changed
SELECT [Id], [Name], [Name2], [Name3], [Language1], [Language2], [Language3], [Category], [LiveTenantId], [State], [Server], [Database], [CredentialProfile], [Properties], [Version]
FROM [catalog].[Tenants];

-- §3.7: registration
SET XACT_ABORT ON; BEGIN TRAN;
DECLARE @id int = COALESCE(@requestedId, NEXT VALUE FOR [catalog].[sq_Tenants]);
INSERT INTO [catalog].[Tenants] ([Id], [Name], [Language1], [Category], [LiveTenantId], [State], [StateChangedAt], [StateChangedBy],
    [Server], [Database], [CredentialProfile], [Properties], [Version], [CreatedAt], [ModifiedAt])
VALUES (@id, @name, @language1, @category, @liveTenantId, N'Provisioning', SYSUTCDATETIME(), @actor,
    @server, @database, @profile, NULL, NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME());
UPDATE [catalog].[CatalogState] SET [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME() WHERE [Id] = 1;
COMMIT;
SELECT @id;

-- §3.7: state transition (@from is the precondition of the validated transition)
SET XACT_ABORT ON; BEGIN TRAN;
UPDATE [catalog].[Tenants]
   SET [State] = @to, [StateReason] = @reason, [StateChangedAt] = SYSUTCDATETIME(), [StateChangedBy] = @actor,
       [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME()
 WHERE [Id] = @tenantId AND [State] = @from;
IF @@ROWCOUNT <> 1 THROW 50409, N'Tenant.IllegalTransition', 1;   -- §3.7: translated to TenantStateException
UPDATE [catalog].[CatalogState] SET [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME() WHERE [Id] = 1;
COMMIT;

-- §3.7: rename (the name group and language codes mirrored from tenant settings)
SET XACT_ABORT ON; BEGIN TRAN;
UPDATE [catalog].[Tenants]
   SET [Name] = @name, [Name2] = @name2, [Name3] = @name3,
       [Language1] = @language1, [Language2] = @language2, [Language3] = @language3,
       [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME()
 WHERE [Id] = @tenantId;
UPDATE [catalog].[CatalogState] SET [Version] = NEWID(), [ModifiedAt] = SYSUTCDATETIME() WHERE [Id] = 1;
COMMIT;

-- §3.8: membership list
SELECT t.[Id], t.[Name], t.[Name2], t.[Name3], t.[Language1], t.[Language2], t.[Language3], t.[Category], t.[State], t.[LiveTenantId], m.[IsActive], m.[UpdatedAt]
  FROM [catalog].[TenantMemberships] AS m JOIN [catalog].[Tenants] AS t ON t.[Id] = m.[TenantId]
 WHERE m.[Subject] = @subject AND t.[State] <> N'Retired';

-- §3.8: membership upsert from @rows : TenantMembershipList (no MERGE); reconciliation adds the DELETE for one tenant
UPDATE m SET m.[IsActive] = r.[IsActive], m.[UpdatedAt] = SYSUTCDATETIME()
  FROM [catalog].[TenantMemberships] AS m JOIN @rows AS r ON r.[TenantId] = m.[TenantId] AND r.[Subject] = m.[Subject]
 WHERE m.[IsActive] <> r.[IsActive];
INSERT INTO [catalog].[TenantMemberships] ([Subject], [TenantId], [IsActive], [UpdatedAt])
SELECT r.[Subject], r.[TenantId], r.[IsActive], SYSUTCDATETIME() FROM @rows AS r
 WHERE NOT EXISTS (SELECT 1 FROM [catalog].[TenantMemberships] AS m WHERE m.[Subject] = r.[Subject] AND m.[TenantId] = r.[TenantId]);
DELETE m FROM [catalog].[TenantMemberships] AS m
 WHERE m.[TenantId] = @tenantId AND NOT EXISTS (SELECT 1 FROM @rows AS r WHERE r.[Subject] = m.[Subject]);

-- §5.5: ticket store
INSERT INTO [catalog].[Sessions] ([Key], [Handle], [Subject], [Sid], [Ticket], [TokensVersion], [CreatedAt], [RenewedAt], [ExpiresAt])
VALUES (@key, @handle, @subject, @sid, @ticket, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), @expiresAt);
SELECT [Subject], [Sid], [Ticket], [TokensVersion], [ExpiresAt] FROM [catalog].[Sessions]
 WHERE [Key] = @key AND [ExpiresAt] > SYSUTCDATETIME();
UPDATE [catalog].[Sessions] SET [Ticket] = @ticket, [ExpiresAt] = @expiresAt, [RenewedAt] = SYSUTCDATETIME() WHERE [Key] = @key;   -- RenewAsync
DELETE FROM [catalog].[Sessions] WHERE [Key] = @key;                                                                                -- RemoveAsync

-- §5.5: refresh compare-and-set (zero rows = lost the race; re-read and adopt)
UPDATE [catalog].[Sessions]
   SET [Ticket] = @ticket, [TokensVersion] = [TokensVersion] + 1, [ExpiresAt] = @expiresAt, [RenewedAt] = SYSUTCDATETIME()
OUTPUT inserted.[TokensVersion]
 WHERE [Key] = @key AND [TokensVersion] = @expectedVersion;

-- §5.5: back-channel logout and revocation
DELETE FROM [catalog].[Sessions] OUTPUT deleted.[Key], deleted.[Handle], deleted.[Subject] WHERE [Sid] = @sid;           -- Key evicts the local cache; Handle goes to the listener
DELETE FROM [catalog].[Sessions] OUTPUT deleted.[Key], deleted.[Handle], deleted.[Subject] WHERE [Subject] = @subject;

-- §5.5: session sweep (SessionSweepService; repeated until fewer than 1000 rows are deleted)
DELETE TOP (1000) FROM [catalog].[Sessions] WHERE [ExpiresAt] < DATEADD(day, -1, SYSUTCDATETIME());

-- §6.1: catalog and tenant migration locks
EXEC sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 600000;
```

## 8. Configuration

The `Tellma` section; keys are additive-only within a family major. Every option type is bound with
`ValidateDataAnnotations` and `ValidateOnStart`.

```jsonc
{
  "Tellma": {
    "DisplayName": "Acme",
    "PublicOrigin": "https://acme.app.tellma.com",
    "Catalog": { "ConnectionString": "…;Database=tellma-acme-catalog;Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=10", "RefreshInterval": "00:00:15", "MaxStaleness": "00:05:00" },
    "Sql": {
      "Profiles": { "default": "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=20;Min Pool Size=0" },
      "MaxPoolSizePerTenant": 20, "MaxWarmTenants": 10, "AllowSqlPassword": false, "DatabasePrefix": "tellma-acme", "ApplicationPrincipal": "tellma-acme"
    },
    "Tenancy": { "MembershipReconcileInterval": "1.00:00:00" },
    "Identity": { "Mode": "Standalone", "Authority": "https://identity.tellma.com", "ClientSecret": null, "ServiceClientSecret": null },
    "Session": {
      "IdleLifetime": "7.00:00:00", "AbsoluteLifetime": "90.00:00:00", "CacheTtl": "00:01:00", "RefreshSkew": "00:01:00",
      "MaxAuthorityOutage": "01:00:00", "SweepInterval": "00:15:00", "StepUp": { "Acr": "urn:tellma:acr:mfa", "MaxAge": "00:15:00" }
    },
    "DataProtection": { "BlobUri": null, "KeyId": null, "KeyRingPath": null },
    "ForwardedHeaders": { "Enabled": false, "KnownNetworks": [], "KnownProxies": [] },
    "Provisioning": { "CreateDatabaseTemplate": null },
    "Admin": { "Enabled": null },
    "Seed": { "Tenants": [], "AdminEmail": null },
    "ScratchPath": null
  }
}
```

| Key | Meaning |
|---|---|
| `DisplayName`, `PublicOrigin` | The information document and every derived URI (§5.8). |
| `Catalog:*` | §3.2, §3.4. |
| `Sql:Profiles`, `MaxPoolSizePerTenant`, `MaxWarmTenants`, `AllowSqlPassword` | §3.5. `DatabasePrefix` names provisioned databases (`<prefix>.<Id>`) and the Development catalog; `ApplicationPrincipal` is the web identity created at provisioning (§6.4). |
| `Tenancy:MembershipReconcileInterval` | §3.8. |
| `Identity:Mode` (`Standalone \| InProc`), `Authority`, `ClientSecret`, `ServiceClientSecret` | §5.5, §5.7; spec 0017's `IdentityServerClientOptions` binds the same section, with `ClientId = <slug>-svc` set by the platform. |
| `Session:*` | §5.5; `StepUp` is the assurance bar the projection stamps. |
| `DataProtection:*`, `ForwardedHeaders:*` | §5.8. |
| `Provisioning:CreateDatabaseTemplate` | §6.1; `{database}` is substituted. |
| `Admin:Enabled` | §5.9; `null` selects the identity-mode default. |
| `Seed:Tenants` (`Id`, `Name`, `Category`, `LiveTenantId`), `Seed:AdminEmail` | §5.7, §6.1; `Id` is `RegisterAsync`'s `requestedId` (§3.7). Development only by convention, not enforced. |
| `ScratchPath` | The temporary directory specs 0016 and 0018 use; default the operating system's. |

`Serilog`, `Azure:SignalR:ConnectionString` and `APPLICATIONINSIGHTS_CONNECTION_STRING` sit outside
the `Tellma` section.

## 9. Observability

```csharp
// Tellma.Core.Abstractions.Tenancy
public static class TenancyTelemetryNames
{
    public const string MeterName = "Tellma.Core";
    public const string Resolutions = "tellma.tenancy.resolutions";
    public const string CatalogRefreshDuration = "tellma.tenancy.catalog.refresh.duration";
    public const string CatalogSnapshotAge = "tellma.tenancy.catalog.snapshot.age";
    public const string CatalogTenants = "tellma.tenancy.catalog.tenants";
    public const string ConnectionFactories = "tellma.tenancy.connection.factories";
    public const string MembershipWrites = "tellma.tenancy.membership.writes";
    public const string Scopes = "tellma.tenancy.scopes";
    public const string OutcomeTag = "outcome";
    public const string CategoryTag = "category";
    public const string StateTag = "state";
    public const string KindTag = "kind";
    public const string SourceTag = "source";
}

// Tellma.Core.Abstractions.Tenancy
public static class HostTelemetryNames
{
    public const string MeterName = "Tellma.Core.AspNetCore";
    public const string SessionOperations = "tellma.session.operations";
    public const string SessionRefreshes = "tellma.session.refreshes";
    public const string BackchannelLogouts = "tellma.auth.backchannel_logouts";
    public const string CsrfRejections = "tellma.auth.csrf_rejections";
    public const string StepUpChallenges = "tellma.auth.stepup_challenges";
    public const string OperationTag = "operation";
    public const string SourceTag = "source";
    public const string OutcomeTag = "outcome";
    public const string RuleTag = "rule";
}
```

| Instrument | Kind | Tags |
|---|---|---|
| `tellma.tenancy.resolutions` | counter | `outcome` ∈ `served \| not_found \| provisioning \| read_only \| suspended \| not_member \| catalog_unavailable` |
| `tellma.tenancy.catalog.refresh.duration` | histogram (s) | `outcome` ∈ `unchanged \| reloaded \| failed` |
| `tellma.tenancy.catalog.snapshot.age` | observable gauge (s) | — |
| `tellma.tenancy.catalog.tenants` | observable gauge | `category`, `state` |
| `tellma.tenancy.connection.factories` | observable gauge | — |
| `tellma.tenancy.membership.writes` | counter | `outcome` ∈ `succeeded \| failed`, `source` ∈ `post_commit \| reconcile \| provision` |
| `tellma.tenancy.scopes` | counter | `kind` ∈ `request \| background` |
| `tellma.session.operations` | counter | `operation` ∈ `store \| retrieve \| renew \| remove \| revoke \| sweep`, `source` ∈ `cache \| database` |
| `tellma.session.refreshes` | counter | `outcome` ∈ `refreshed \| adopted \| rejected \| deferred` |
| `tellma.auth.backchannel_logouts` | counter | `outcome` ∈ `accepted \| rejected` |
| `tellma.auth.csrf_rejections` | counter | `rule` ∈ `sec_fetch_site \| origin \| header \| content_type` |
| `tellma.auth.stepup_challenges` | counter | — |

No tenant or user tag on any instrument. Every tenant request runs inside a logger scope with
`TenantId`, `TenantCategory`, `Subject` (never email) and `UserId`, to which spec 0015's telemetry
filter adds `Client`, `Resource` and `Operation`; background scopes add `JobId` and
`OriginTraceParent` (§4.1), the one place the enqueuing trace is joined to a job's log lines.
The request activity carries `tellma.tenant.id` and `tellma.deployment.id` (per-tenant identity is
allowed on traces and logs, never on metrics); SQL spans carry `tellma.db.role`. Every log event
carries `TraceId` and `SpanId` from the current activity, and `PlatformVersion` and
`DistributionVersion` as enricher properties from `DeploymentVersions` (§2.2); the OpenTelemetry
resource sets `service.version` to the distribution version and `tellma.platform.version` to the
platform version, so any log line joins its trace and names the exact commits. Log events: the
registry's reload (`Information`, count of changed descriptors), a failed refresh (`Warning` under
`MaxStaleness`, `Error` beyond it), a state transition (`Information`, from, to, actor), a
membership-hint write failure (`Warning`), a reconciliation run (`Information`, tenants and rows
changed; `Warning` per failed tenant), a rejected back-channel token (`Warning`, reason), a
refresh rejection (`Information`, reason), a CSRF rejection (`Warning`, rule, route). Alert queries
under `infra/monitoring/` are checked in and cross-checked by the existing instrument-name test.

## 10. Testing

Test projects mirror `src/`. Two traits select CI tiers: `Category=Integration` (infrastructure the
suite brings up itself; every pull request, on Windows with LocalDB and on Linux with Testcontainers
`mcr.microsoft.com/mssql/server:2022-latest`) and `Live=true` (real third-party services; nightly
and on demand, the only suites the nightly run carries; none in this spec). A `Category=Integration`
suite that needs Docker runs on the Linux pull-request runner; a Windows runner without Docker skips
it. Everything else runs on every pull request on Windows and Linux.
`distributions/acme/test/Tellma.Distro.Acme.E2E` (Playwright) is scaffolded with no tests and runs,
empty, on every pull request until the UI specs ship.

| Suite | Tier | Pins |
|---|---|---|
| `test/core/Tellma.Core.Tests` | unit | The graph gate (aggregation of every problem, cycles, unknown item types, `Fix` text, duplicate names, double options binding); topological override of a pack service by a dependent feature; registry snapshot semantics with `FakeTimeProvider` (unchanged version → no reload; changed → reload; `!=` on an older GUID; single flight; listener calls only for changed descriptors; staleness bound → `catalog-unavailable`); connection composition (profile precedence, `Application Name`, refusal of `;`/`=`, never logged) and every profile rule; the state-transition table including the illegal ones; `RequestContext` immutability, `Tenantless`, `RequireTenant`, snapshot round trip; the holder is set exactly by the two writers; `TenantSandboxContext` throws unbound; `taxonomy.json` consistency; `TellmaComposition.Validate` host-free. |
| `test/core/Tellma.Core.IntegrationTests` | `Category=Integration` | Catalog migrations apply from empty and are idempotent; registration and state transitions bump `CatalogState.Version`; concurrent transitions (one wins, the other raises `TenantStateException` with `Code = Tenant.IllegalTransition`); membership list and TVP reconciliation (upsert plus per-tenant delete); `tellma_app` role and grants recomputed from the model and covering every schema and sequence; session store CRUD (the handle computed at creation, carried by the retrieved ticket and output by the termination deletes); refresh CAS with two concurrent refreshers (exactly one redeems); sweep deletes only rows expired for a day; `ITenantScopeFactory` against each state, with and without `allowNonActive` (`Provisioning` and `ReadOnly` open only with it and only for `Kind = System`), starting no activity of its own. |
| `test/core/Tellma.Core.AspNetCore.Tests` | unit (`WebApplicationFactory`, fake authentication) | The tenant verdict matrix (unknown, retired, provisioning, suspended, read-only, active × read, write × anonymous, member, non-member); the CSRF matrix (each rule, each exemption, `AcceptsBinaryMetadata`, `415`); scheme isolation (a cookie on a bearer route fails, a bearer on the web route fails); every endpoint-audit violation named by route and method; back-channel token vectors (bad `iss`, bad `aud`, wrong `typ`, missing `events`, `nonce` present, expired, `sub`-only, replay → `200` no-op) and the listener receiving handles, never keys; `SessionHandle` bound on the cookie scheme and null on the bearer schemes; the step-up challenge shape; `/bff/user` shape and `401`; the information document shape and headers; health before and after the first snapshot; the PRM document shape; `PrincipalKind` derivation; `SessionSweepService` and `MembershipReconcileService` fire on their intervals, the latter only on the lock holder. |
| `distributions/acme/test/Tellma.Distro.Acme.Web.Tests` | unit | Composition parity (the web host and the migrator build byte-identical models); the audit passes for every mapped endpoint; one-way dependency (no `src/` project references `distributions/`); no secrets in tracked configuration; `Program.cs` contains exactly the three platform calls. |
| `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests` | `Category=Integration` | `migrate` then `provision` against the tier's SQL Server from an empty server; the seed tenants exist and are `Active`; `dbo.__TellmaProvisioning` records the platform steps with their versions and a bumped step re-runs; full sign-in through the in-proc engine (reusing the identity suite's OIDC flow client); the first tenant request binds `UserId`; a suspension takes effect on the next request and within one refresh on a second host; back-channel logout end to end; a refresh rejection ends the session; readiness before and after catalog availability; `ReadOnly` read succeeds and mutation is `403` against a database set `READ_ONLY`. |

Fixture: the catalog database is created per test class from the platform migrations; tenant
databases in the distribution suite come from `provision`. Test names describe behaviour and never
cite a document.

## 11. Definition of done

- **Projects**: `src/core/Tellma.Core.AspNetCore`, `src/core/Tellma.Core.Migrator` (new),
  `src/core/Tellma.Core` and `src/core/Tellma.Core.Abstractions` (grown),
  `distributions/acme/src/Tellma.Distro.Acme.Web`, `distributions/acme/src/Tellma.Distro.Acme`,
  `distributions/acme/src/Tellma.Distro.Acme.Client`,
  `distributions/acme/src/Tellma.Distro.Acme.Migrator`, `src/defaults/Tellma.Defaults.Azure`,
  `src/defaults/Tellma.Defaults.Azure.AspNetCore`, `test/core/Tellma.Core.AspNetCore.Tests`,
  `distributions/acme/test/Tellma.Distro.Acme.Web.Tests`,
  `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests` (new),
  `distributions/acme/test/Tellma.Distro.Acme.E2E` (new; scaffolded empty, §10), the grown
  `test/core/Tellma.Core.Tests` and `test/core/Tellma.Core.IntegrationTests` — each with a README
  stating purpose and usage, XML docs on every member, building and testing on Windows and Linux
  under warnings-as-errors, wired into `Tellma.slnx`; `taxonomy.json` at the repository root; the
  package pins of §1.1.
- **Behavior**: the composition pipeline and both gates of §2 (pinned by `Tellma.Core.Tests`); the
  route grammar, catalog, states, registry, connection resolution, `tellma_app`, secrets policy,
  membership directory and sandbox context of §3 (`Tellma.Core.Tests`,
  `Tellma.Core.IntegrationTests`); the request context, guard and scope factory of §4; the host
  pipeline, route groups, audit, CSRF, session subsystem, MCP topology, in-proc mode, baseline and
  surfaces of §5 (`Tellma.Core.AspNetCore.Tests`, the distribution's integration suite); the
  migrator commands, steps, trigger and local-development story of §6; the schema and statements of
  §7; every option of §8 validated at startup.
- **Observability**: every instrument of §9 registered under its meter, asserted by the instrument
  name test; the log events of §9 asserted in the unit suites; `infra/monitoring/` alert queries
  cross-checked.
- **CI**: pull-request tier, including the two `Category=Integration` suites, green on Windows and
  Linux; the `acme` smoke deployment built from `distributions/acme/`.
- **Docs**: the architecture document updated where this spec touches it — the platform repository
  layout (`distributions/acme/` in distribution-repo shape with the Client esproj; `TellmaDbContext`
  platform-internal); library architecture's package naming and dependency rules (one runtime
  `Tellma.Core` referencing EF, SqlClient, HierarchyId, Cronos, OpenXml, the Data Protection
  abstractions; `Tellma.Core.Abstractions` referencing `Tellma.Core.Queryex`;
  `Tellma.Core.AspNetCore`, `Tellma.Core.Mcp`, `Tellma.Core.Migrator` as adapters of `Tellma.Core`;
  `Tellma.Core.Imaging` native-isolated; the two defaults bundles
  `src/defaults/Tellma.Defaults.Azure/` and `src/defaults/Tellma.Defaults.Azure.AspNetCore/` as
  distribution-layer packages that reference connectors and adapters and that no Core package
  references); the distribution repository layout (the `Tellma.Distro.<Slug>` composition library
  both hosts reference, holding `Entities/` and `Services/` and referencing no ASP.NET package;
  `Endpoints/` in the Web project; no `Data/`); multi-tenancy (one catalog database per
  distribution, schema `catalog`, the five states); multi-tenancy and hosting secrets (no SaaS
  tenant database password; credential profiles; Key Vault for OIDC and on-prem-style secrets only);
  the data layer's migrations and migrator (platform-owned catalog migrations; `migrate`,
  `provision`, `set-state`, `status`; `dbo.__TellmaProvisioning`; `dbo.__TellmaSchema` and the
  `tenant-schema-behind` verdict; the migrator's service-client credentials; RCSI at provisioning;
  `tellma_app`); feature composition (minimal fidelity: BCL-only feature contract, `Requires` only,
  data-only items, explicit `AddFeature<T>()`, two gates, the endpoint audit); identity (the
  reference distribution references `Tellma.Identity` and selects in-proc by configuration; the
  in-proc store is the catalog database's `idsvr` schema; the distribution owns its BFF); hosting on
  Azure (`Always On`, `WEBSITES_CONTAINER_STOP_TIME_LIMIT = 30`); observability (meters
  `Tellma.Core` and `Tellma.Core.AspNetCore`; per-tenant identity on traces and logs only); the open
  question on the ad-hoc SQL permission set (answered by `tellma_app`); rollout and phasing
  (`Tellma.dev.<slug>.*` databases; tracked `launchSettings.json`); the reserved-slug list (`acme`;
  `taxonomy.json` gains `reservedSlugs` and `distributions`). Public XML docs and error messages
  reference no `docs/` paths, per repo rule.
- **Not in scope of done**: the identity-server amendments (spec 0021); the MCP tools; self-serve
  provisioning beyond `NotConfiguredProvisioningTrigger` and the Development starter; sandbox
  cloning; every consumer surface the sibling specs ship against these
  contracts.

## Decisions record

The load-bearing decisions, where not already evident above:

1. **`Tellma.Core.AspNetCore` owns everything with an ASP.NET dependency; `Tellma.Core` stays
   host-agnostic** — the migrator and any worker compose Core without the web stack, and Core
   services are testable without `HttpContext` (§1.1).
2. **The feature contract is BCL-only and lives in Abstractions; items are records realised by
   Core** — a module must ship a feature without an edge to `Tellma.Core`, and no feature ever sees
   `IServiceCollection` or `ModelBuilder` (§2.1).
3. **Explicit `AddFeature<T>()`; no assembly scanning** — the selection site is visible and
   AOT-friendly (§2.2).
4. **Two aggregated gates** — every composition and startup problem is reported at once, with a
   remedy, before traffic is served; an unreachable catalog is a readiness concern, never a startup
   failure, so a slot swap cannot crash-loop (§2.3, §3.4).
5. **An immutable positive `int` tenant id with a tenant-first route prefix** — the integer
   constraint alone separates platform routes from tenant routes, and membership verified per
   request makes enumerability harmless (§3.1).
6. **One catalog database per distribution, always; one tenant or many is what the operators
   register, never a policy** — one code path for the registry, the migrator, the session store, the
   membership hint and the in-proc identity store; a tenant restore never changes which tenants
   exist (§3.2).
7. **No SaaS tenant database has a password; the catalog stores a location and a profile name**
   — managed identity as a contained user in SaaS, integrated security on Windows on-prem, a
   host-injected SQL login on Linux on-prem (`Tellma:Sql:AllowSqlPassword`); a compromised catalog
   row cannot point the application at an attacker's credential (§3.5, §3.6).
8. **A snapshot registry with a 15-second version poll and a 5-minute staleness bound, then fail
   closed** — a suspension propagates within one interval and a catalog outage cannot become an
   ignored suspension (§3.4).
9. **Five tenant states with a verdict table; `Retired` and `Provisioning` answer `404`; `ReadOnly`
   reads succeed against a read-only database** — the state model is enforced by the distribution
   and the read-only case is genuinely read-only end to end (§3.3).
10. **The membership hint is navigation, never authorization; written post-commit and reconciled
    daily by a hosted timer; never written by the connect step** — no N+1 across tenant databases,
    no second source of truth for access, no catalog write on the hot path (§3.8).
11. **An unbound sandbox context throws** — a side-effecting connector outside a tenant scope is a
    bug that must surface on its first call (§3.9).
12. **One immutable `RequestContext` in a scoped holder with two writers; no `AsyncLocal`** —
    initializers are pure functions and a stale tenant can never leak into pooled or
    fire-and-forget work (§4.1, §4.2).
13. **`today()` binds to the tenant zone; the display zone only formats; no client today header** —
    spec 0008 §10.6 defines it so, and every user of a tenant must agree on the business date
    (§4.1).
14. **Tenant resolution is middleware after authorization; tenant access is one guard called from
    two front doors** — an anonymous probe gets `401` before learning whether a tenant exists, and
    the web surface and the MCP surface cannot diverge (§4.3).
15. **A catalog-backed session store with a per-instance cache, CAS-coordinated refresh and a
    back-channel receiver** — a 43-character cookie, revocation as a `DELETE` that hands listeners a
    derived handle rather than the key, and no cross-instance refresh race that trips the
    authority's reuse detection (§5.5, §7.4).
16. **Refresh-anchored liveness** — authority-side revocation and policy tightening take effect
    within one access-token lifetime even when back-channel delivery fails (§5.5).
17. **Per-surface schemes named explicitly in policies; a denying fallback policy; `AllowAnonymous`
    only on tenantless endpoints; an audit that fails startup** — a cookie can never authenticate a
    bearer surface and an unsecured endpoint cannot ship (§5.2, §5.3).
18. **CSRF by required `Tellma-Client` header plus `Origin`/`Sec-Fetch-Site` plus JSON-only bodies
    and no CORS; no antiforgery tokens** — the .NET antiforgery middleware enforces nothing on JSON
    endpoints, and the header doubles as the client build tag (§5.4).
19. **One MCP endpoint per tenant with a per-tenant audience** — RFC 8707 gives per-tenant audiences
    for free, and one endpoint per distribution would put tenant selection inside every tool call
    (§5.6).
20. **In-proc identity stores in the catalog database's `idsvr` schema with stable configured
    secrets** — the only distribution-wide database, and multi-instance standalone deployments
    cannot survive per-boot secrets (§5.7).
21. **Provisioning is a migrator command with a versioned step seam; the web application only
    records intent and calls a trigger** — DDL rights never reach the web identity, and seeds
    re-run by version rather than by force (§6.1, §6.2).
22. **`tellma_app` is a platform-constant role re-granted from the model after every migration** —
    migrations never carry an environment-specific principal, and the ad-hoc SQL permission set is
    answered once (§3.5, §6.4).
23. **`TellmaDbContext` is platform-internal; no derived context, no injection, no LINQ** — every
    distribution writes zero data-access code (§6.5).
24. **The catalog session sweep is a hosted timer, not a job** — jobs are per tenant and nothing
    tenantless can ride them (§5.5).
25. **`acme` is the reference distribution's slug** — a fictional customer exercises the real
    distribution shape and is deployable as the smoke deployment (§1.2).
26. **`UseAzureDefaults()` and `UseAzureWebDefaults()` bundle the Azure deployment's common
    composition** — one call in `compose` composes email, webhooks, blobs and imaging; one call in
    `composeWeb` composes the SignalR backplane, so the composition library and the migrator carry
    no ASP.NET package (§1.3).
27. **A pack extends another feature's entity through a required shape or a sibling table, never
    by touching the class** — the leaf stays the one place columns are added (§2.4).

## Review flags

1. **Session model** (§5.5): a catalog-backed `ITicketStore` with a 60 s per-instance cache and
   CAS-coordinated refresh, versus a stateless encrypted cookie with a throttled `ValidatePrincipal`
   refresh and a polled revoked-`sid` table (fewer tables, no Data Protection dependency; a 3–5 KB
   cookie per call, a ten-minute revocation bound, no session listing). Flips if the catalog read
   per session per instance per minute proves measurable at scale.
2. **Catalog placement** (§3.2): a dedicated catalog database always, versus allowing the `catalog`
   schema to co-locate with the live database by configuration for on-prem single-customer installs
   (one fewer database; a restore hazard and a two-contexts-one-database test matrix). Flips if
   on-prem operators refuse a second database.
3. **`today()` binding** (§4.1): the tenant-zone business date per spec 0008, versus the user's
   zone (one line in the negotiation initializer; two users of one tenant would then disagree on
   today's postings). Flips only with a spec 0008 amendment.
4. **Refresh interval** (§3.4): 15 s, versus 60 s (a quarter of the probes; a one-minute suspension
   latency) or 5 s. Flips with measured probe cost against hundreds of instances.
5. **Membership self-heal** (§3.8): a daily leader-elected reconciliation timer beside the
   post-commit writes, versus the connect step also upserting the hint at most once per 24 h per
   (subject, tenant) — faster repair, at the cost of a catalog write on the hot path. Flips if a
   day of staleness after a failed post-commit write proves too long.
6. **MCP audience** (§5.6): the fixed `<origin>/{int}/mcp` pattern on the identity server (spec
   0021 §3; the accepted `resource` copied into `aud`), versus per-tenant resource registration
   (identity state per tenant) or a permanent distribution-wide audience with membership as the
   only isolation. Flips with spec 0021's review.
7. **`Provisioning` verdict** (§3.3): `404 tenant-not-found` (a tenant being set up is
   indistinguishable from absent, never counts as a server error, and the picker shows the state
   from the membership list), versus `503` with `Retry-After` (an honest "come back later" that
   reveals the tenant's existence and counts against availability). Flips if a client must poll a
   tenant into readiness.
8. **`ReadOnly` and background work** (§3.3, §4.4): workers skip every non-`Active` tenant, versus
   creating the scope and running read-only handlers only. Flips if a read-only tenant needs
   scheduled exports.
9. **Feature contribution shape** (§2.1): records plus realizers (reflectable, serialisable for a
   future manifest), versus an interface per item kind. Flips if the realizer indirection proves
   harder to debug than a direct call.
10. **`TellmaDbContext` platform-internal** (§6.5): no derived context and no injection, versus an
    optional distribution `DbContext` for `DbSet` conveniences. Flips only with spec 0011
    reintroducing a LINQ surface.
11. **Composition library** (§1.2): the class library `Tellma.Distro.Acme` both hosts reference,
    so a migrator build never builds the Client esproj and needs no Node, versus the Migrator
    referencing the Web project (one project fewer). Flips if keeping entities and endpoints in
    separate projects proves a burden for distribution authors.
12. **Admin surface now** (§5.9): the minimal surface ships now because bearer authentication exists
    for MCP and spec 0021 §6 grants its audience first, versus deferring it until a control plane
    exists. Flips if no control-plane caller arrives before the surface needs maintenance.
13. **CSRF header name** (§5.4): `Tellma-Client` (RFC 6648-clean; doubles as the build tag), versus
    `X-Requested-With: XMLHttpRequest` (set by default in some client libraries). Flips if a
    required client library cannot set custom headers.
14. **Unbound sandbox context throws** (§3.9), versus treating unbound as sandbox (fail-safe, but
    hides the bug behind undelivered mail). Flips if tenantless mail senders multiply.
15. **Explicit feature selection** (§2.2): `AddFeature<T>()` per pack, versus assembly scanning.
    Flips if distributions accumulate dozens of packs.
16. **One `Tellma.Core` runtime package** (§1.1): data, pipeline, access, settings, blobs, Excel,
    jobs and notifications folded into `Tellma.Core`, `Tellma.Core.Abstractions` referencing
    `Tellma.Core.Queryex`, SkiaSharp alone isolated in `Tellma.Core.Imaging`, versus seven runtime
    packages and a separate data Abstractions. Flips if a distribution needs to omit a whole area
    for licence or size reasons.
17. **Workers skip every non-`Active` tenant** (§4.4; restates flag 8 from the worker's side).
18. **A provisioning step `Version` with a re-run rule** (§6.2), versus completion-only recording
    with a `--force-steps` switch. Flips if versioned re-runs prove error-prone against tenant data
    that diverged.
19. **`acme` as the reference distribution's slug** (§1.2), versus a slug that says what it is
    (`reference`, which must then be added to the reserved list). Flips on preference.
20. **`UseAzureDefaults()` and `UseAzureWebDefaults()` as bundles** (§1.3) versus per-call
    composition in every distribution (no bundle packages; four explicit calls in `Compose` and the
    realtime call in `composeWeb`). Flips if the bundles' package sets diverge across Azure
    distributions faster than a configuration switch absorbs.
21. **Required shapes as the primary cross-feature extension** (§2.4) versus pack-contributed shadow
    columns (no distribution code; invisible on the class and the wire). Flips if distributions
    find the interface ceremony heavier in practice than the sibling table.
22. **Warm profiles by configuration with a warning threshold** (§3.5) versus `Min Pool Size = 0`
    everywhere. Flips if warm profiles are ever assigned broadly enough to threaten the pool
    invariant, in which case the warning becomes a failure.
