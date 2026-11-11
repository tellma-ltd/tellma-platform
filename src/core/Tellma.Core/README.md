# Tellma.Core

The platform's runtime. Every distribution host references it; the web host, the migrator and any
worker compose it through the same composition method. It takes no ASP.NET Core dependency, so a
host without the web stack composes it unchanged, and its services are testable without an HTTP
context.

Today it holds the composition root, the tenant data model (`TellmaDbContext`, its conventions,
the table-type binder and the entity metadata), the tenancy runtime (the catalog model, the tenant registry, connection
resolution, the catalog's write side and the membership hints), the first piece of blob storage (the
file-system store), the three calendar systems and the core of localization (the language
catalogue, the value sets, the negotiation of a request's language, region, calendar and time
zone, and the ICU string localizer with Core's shared resources). The rest of data access, the CRUD
pipeline, access control, settings, caching, the rest of localization and of blobs, Excel, jobs and
notifications join it as namespaces.

## The composition root

```csharp
services.AddTellma("acme", configuration, environment, tellma => tellma
    .AddFeature<GlFeature>()
    .UseEntity<Center, MyCenter>()
    .Languages(["en-GB", "ar-SA"]));
```

`Languages` declares, once, the languages the distribution offers, the default first, each
optionally with the region its users get by default; `AddLanguage` describes a language the
catalogue lacks (see [Localization](#localization)). A distribution that declares none offers
English.

`AddTellma` runs four steps, then installs the second of two gates:

1. **Collect.** Registers `DeploymentIdentity(slug, environment)` and `DeploymentVersions`, adds
   `CoreFeature` (`core`) first, and runs the composition method. Selection is explicit: there is no
   assembly scanning.
2. **Declare.** Each feature's `Declare` records its `Requires` edges and options bindings, and its
   `Contribute` its data-only items. A nested feature (`contribution.Feature(...)`) joins the same
   two steps.
3. **Graph gate.** Every problem below is collected and thrown at once in one
   `TellmaCompositionException`, before the host is built: a requirement no added feature has
   (naming the feature that requires it), two features with one name, a cycle (with its path), a
   feature type added as two instances, an options type bound to two paths, a slug or feature name
   outside its grammar, a second leaf for one default, an item no realizer handles, and two
   features registering one non-collection service type when neither depends on the other.
4. **Realise.** Items are realised in topological order (`core` first, each feature after what it
   requires and nests, otherwise in the order added). A later feature's service registration
   replaces an earlier one's, so a distribution replaces a pack's service by requiring the pack and
   registering the same service type; a collection service only ever adds: a type marked
   `[CollectionService]` (`IStartupCheck`, `IClientAssetContributor`) or one of the framework's
   multi-registration types (`IHostedService`, `IHostedLifecycleService`, `IConfigureOptions<>`,
   `IConfigureNamedOptions<>`, `IPostConfigureOptions<>`, `IValidateOptions<>`,
   `IOptionsChangeTokenSource<>`, `IStartupFilter`). Realizer problems are thrown together, again before
   the host is built.

The **realised gate**, `TellmaStartupGate`, is inserted as the first hosted service. In
`IHostedLifecycleService.StartingAsync` it runs every registered `IStartupCheck` inside one scope
and throws one `TellmaCompositionException` carrying every problem, before any other hosted service
starts and before the host serves traffic. When a contributed check cannot be created, the gate
reports that and still runs the built-in checks. `TellmaStartupGate.RunChecksAsync` runs the same checks
for a host that is not started, optionally skipping those with `RequiresInfrastructure`.

`TellmaComposition.Validate` runs the same composition host-free and returns, rather than throws,
every problem `AddTellma` would throw.

### Built-in startup checks

| Check | Fails when | Needs infrastructure |
|---|---|---|
| `core.options` | a bound options type fails its data-annotation validation (one problem per failure) | yes |
| `core.deployment-identity` | the registered `DeploymentIdentity` names another application, or no `ISandboxContext` resolves (the composition registers one; a host removed it or replaced it with one that cannot be created) | no |
| `core.globalization` | the process does not run ICU (invariant mode or Windows NLS) | no |
| `core.key-vault-literals` | a value under `Tellma:` is an unresolved `@Microsoft.KeyVault(` reference | yes |
| `core.sql-profiles` | no `default` credential profile; a profile that does not parse, has an unusable name, or does not set `Max Pool Size` within `Tellma:Sql:MaxPoolSizePerTenant`; outside Development, a profile or the catalog connection that does not set `Encrypt` (true or strict) or carries a password without `Tellma:Sql:AllowSqlPassword`; an unresolved Key Vault reference inside either; outside Development, an empty `Tellma:Sql:AllowedServers`; a malformed entry in it. Problems name a profile and a keyword, never a value | yes |
| `core.public-origin` | `Tellma:PublicOrigin` is missing, relative, more than an origin (a trailing slash included, since derived URIs append `/…` to it), or not https outside Development | yes |
| `core.taxonomy` | in Development, the repository's `taxonomy.json` does not list the slug | no |
| `core.entity-shapes` | the leaf registered for an entity does not implement a shape another feature requires of it; the fix lists the members | no |
| `core.model-contributors` | a model configuration Core contributes is not one the tenant model can apply: a concrete entity type configuration, closed, or open over exactly the entity it configures with that parameter constrained to the replaceable default, and with a parameterless constructor | no |
| `core.languages` | a declared code is malformed, names a language that is neither catalogued nor added, repeats a language, or names a region its language does not have; `Languages` was called with an empty list; an added language is malformed (code, regions, default region, native digits), already described, or shares a symbol; the `Tellma.Core` satellite assembly of an offered language other than English is not deployed | no |
| `core.calendars` | a composed calendar system's code is not 1 to 16 lower-case ASCII letters and digits starting with a letter; two systems share a code, ignoring case; `gc`, `uq` or `et` is not composed | no |
| `core.value-sets` | a value set is not named `<schema>.<PascalCase>`, repeats a value (ignoring case), gives a value both or neither of a label key and a label, or has an alias that is one of its values or maps to none; two sets share a name, ignoring case; a set has the name of one of the model's enums (`<entity schema>.<EnumType>`); a `[ValueSet]` property names a set no feature contributes | no |
| `core.time-zones` | never: it logs one warning (`TimeZoneUnresolvable`) per value of `core.TimeZone` the host's time-zone data cannot resolve, the sign of an image with stale tzdata | yes |
| `core.entity-metadata` | the tenant model cannot be built (the convention's message, naming the declaration), or an entity leaf in it breaks the entity contract (every problem at once, see [Entity metadata](#entity-metadata)); a referenced entity with no natural key is logged as a warning and never fails | no |

Options a feature declares are validated by `core.options` rather than by the host's
`ValidateOnStart`, which would stop startup at the first failing options type before the gate could
report the other problems.

## The tenant model

`TellmaDbContext` (`Tellma.Core.Data`) is the platform's tenant EF context, and its model is built
from the composition alone. `AddTellma` registers it as a singleton `IModel`, built on first use
from a context with no connection, beside the `TableTypeBinder` over it and the entity metadata
the startup gate validates.

- **Entities.** Every contributed entity (`contribution.Entity<T>()`), with each default a
  `UseEntity<TDefault, TLeaf>()` replaced swapped for its leaf, so the model maps one type per
  table; then, depth first, the children each one's `[NotMapped]` collections carry (a child is
  never contributed), each swapped for its leaf too.
- **Core's configurations**, in feature order: a closed `IEntityTypeConfiguration<T>`, or an open
  one (`UserConfiguration<TUser> where TUser : User`) closed over the leaf registered for its type
  parameter's constraint. `CoreFeature` contributes `dbo.__TellmaSchema` and
  `dbo.__TellmaProvisioning` this way; no module or distribution can contribute one.
- **Standalone table types**: every `contribution.TableType<T>()` class, in `dbo` under its class
  name unless its `[TableType]` says otherwise. `CoreFeature` contributes `IdList`, `BigIdList`,
  `GuidList`, `StringList`, `DateList`, `IdStampList` and `VersionTagList`.
- **The platform collation** (`DatabaseCollations.Platform`) and named default constraints.

Standalone types follow the entity rules: enums by name, timestamps `datetimeoffset(3)` unless
the class declares a column type.

`UseTellmaSqlServer(connectionString, deployment, sweepScope)` is the one place provider options
are applied: `UseSqlServer` with `UseHierarchyId()` and compatibility level 160 (so a JSON column
never turns into the native `json` type), the table-type extension under the sweep scope
(`TableTypeSweepScopes.Tenant` by default), the platform's conventions, and the connection
string's `Application Name` set to the deployment id, so EF's connections and the executor's share
one pool.

### Conventions

The conventions run, in this order, at the head of the model-finalizing conventions, over the
finished model and before the provider settles key generation and the table-type extension derives
each type. Each configures as a declaration would: an explicit model configuration wins over it,
and it wins over the provider's own conventions. Foreign keys are added explicitly, since the
provider's model cleanup removes every foreign key without a navigation, and the platform maps no
navigation. The provider's convention that indexes every foreign key is removed: an index exists
only where a convention or Core's configuration declares it, under the platform's name.

| Convention | Effect |
|---|---|
| `EnumStringConvention` | every enum or nullable enum property stored by member name |
| `EnumStringLengthConvention` | `varchar(n)`, `n = max(8, longest value)` (a flags enum's longest value joins every flag), unless a length, unicode or column type is declared; no `CHECK` |
| `JsonColumnConvention` | `[JsonColumn]` → `nvarchar(max)` |
| `TimestampConvention` | every `DateTimeOffset` property `datetimeoffset(3)`, unless a precision or column type is declared |
| `AuditConvention` | `CreatedById`/`ModifiedById` → `FK_<Table>_<Column>` to `core.Users`, no cascade, no index; both timestamps `datetimeoffset(7)` |
| `CollationConvention` | `[Collation]` → the column's collation, which the row image copies |
| `TableTypeConvention` | `[TableType]` → the extension's opt-in, granted to `tellma_app`; `[ExcludeFromTableType]` → the column's exclusion; standalone classes registered with their collations, exclusions and enums by name |
| `TemporalConvention` | `[Temporal]` → system versioning, history `<schema>.<Table>History`, shadow `ValidFrom`/`ValidTo` left out of the table type; children follow their parent unless `[Temporal(false)]`; a table-per-type root or leaf fails |
| `ChildConvention` | `[ParentKey]` → `FK_<Table>_<Column>` to the parent, no cascade, `IX_<Table>_<Column> INCLUDE (Id)` |
| `ReferenceConvention` | `[References(typeof(T))]` → `FK_<Table>_<Column>` to the leaf registered for `T`, no cascade, `IX_<Table>_<Column>` |
| `SiblingConvention` | `[Sibling]` → `FK_<Table>_Id` to the owner's leaf, `ON DELETE CASCADE` |
| `TableIndexConvention` | `[TableIndex(…)]` → `IX_<Table>_<Columns>` in the declared order |
| `TreeConvention` | `ParentId` → `FK_<Table>_ParentId` to the table itself and `IX_<Table>_ParentId`; shadow `Node hierarchyid NOT NULL`, `UX_<Table>_Node`, left out of the table type; subtree counts default 1; the maximum depth recorded |
| `ActivatableConvention` | `IsActive` defaults 1, `DF_<Table>_IsActive` |
| `JobEntityConvention` | `JobId` → `FK_<Table>_JobId` to `core.Jobs` `ON DELETE SET NULL`, `UX_<Table>_JobId` where not null |
| `UniqueConvention` | `[Unique]`, `[NaturalKey]` → `UX_<Table>_<Column>`, filtered when nullable; on a child `(ParentKey, Column)` |
| `BlobReferenceConvention` | `[BlobReference]` → `FK_<Table>_<Column>` to `core.Blobs`, `UX_<Table>_<Column>` where not null |
| `SequenceConvention` | every keyed table's key never database-generated, and every one but a sibling's sequence `<schema>.sq_<Table>`, `int` or `bigint` like the key, `START WITH 1000 INCREMENT BY 1 NO CYCLE` |
| `PositiveKeyConvention` | every keyed table → `CK_<Table>_Id CHECK ([Id] > 0)` |
| `MultilingualConvention` | the `P2`/`P3` twins of a `[Multilingual]` anchor take its length and unicode, unless they declare their own, and are nullable |
| `ConstraintNameConvention` | every primary key `PK_<Table>`, and every foreign key a Core configuration leaves unnamed `FK_<Table>_<Columns>` |

A declaration the model cannot be built with (a reference to an entity absent from the model or
mapped twice, an index over a property the class lacks, a key neither `int` nor `long`, a
capability column ignored or hidden, a missing `core.Users`, `core.Jobs` or `core.Blobs`) fails the model with the class and the declaration named.

### Entity metadata

`IEntityMetadataProvider` (a singleton) holds one validated `EntityMetadata` per mapped entity leaf,
children included, built once from the runtime model and the entity classes. The startup gate's
`core.entity-metadata` check is the model's first use, so a model that cannot be built or breaks
the entity contract fails startup, every problem at once, before the host serves traffic; the
provider the host resolves is the same instance the check built, and it never resolves for an
unsound model.

- **Lookup.** `Get` takes the leaf or any class in its chain below the platform's bases, so
  `Get(typeof(Center))` returns `MyCenter`'s metadata when the distribution registered it. The
  entity's `Name` is `<schema>.<default's class name>`.
- **Properties** are the mapped scalars in declaration order, base classes first, with their
  column, store facets, compiled getter and setter, and ownership: database-owned when computed;
  server-owned for the key, the audit columns, the subtree counts and the capability interfaces'
  members (read through the class's interface map, since attributes on interface members never
  reach the class); else as `[ServerOwned]`, `[WriteOnce]` or `[Derived]` says; else editable.
  `ResetServerOwned` and `ResetDerived` copy those members from a fresh instance.
- **Natural keys**: the declared ones by `Order`, then the ones inferred over strings backed by a
  single-column unique index (on a child, one scoped to the parent): the multilingual `Name`, then
  `Code`, then the required ones and the nullable ones in declaration order.
- **Display name**: `[DisplayNameSource]`, else the multilingual `Name` group, else the first
  natural key, else the key.
- **References** are every many-to-one foreign key, named by the model's navigation or the
  property's name without `Id`; **children** pair each `[NotMapped]` list with the child's
  `[ParentKey]` and its `[MaxChildren]` cap.
- **Fingerprints.** `SchemaFingerprint` hashes the name, column, store type, nullability and
  ownership of every editable and write-once property, and each child collection's fingerprint.
  `StorageFingerprint` hashes every table, its columns (name, store type, nullability, default,
  computed definition) and indexes, every sequence and every table type's physical name, each
  sorted ordinally, so the web host, the workers and the migrator, composing one model, compute
  one value.

The rules the check enforces beyond what the conventions refuse while building: every class
declares `[Table]` in a schema other than `dbo`, and a leaf re-declaring it names its default's
table; the capability columns are mapped and keep their names, as does every column a default
leaf declares; one class per table, no replaced default or derived type in the model, no class
two leaves share, unique entity names; children pair with exactly one parent key, belong to a
top-level root and are temporal only under a temporal one; top-level and child entities have a
table type that holds every capability column and no shadow column; trees are 2 to 32 deep and
keep `UX_<Table>_Node`; multilingual twins exist and match their anchor; display-name sources,
prefixes and `[RelatedSelect]` projections render; unique and natural keys sit on a backed scalar
that is not JSON or binary, natural keys on a key-list type; navigation names collide with nothing;
blob references are `int?` with a valid kind declared once; enums are stored by name in a sized
`varchar`; one ownership per property, never on a parent key or a computed column; column
annotations decorate mapped properties of the right type.

### The table-type binder

`TableTypeBinder` (`[TableTypeBinder]`) binds rows to table-valued parameters from the model's
table-type definitions: an entity's rows to its table's row image, a standalone class's to its
type. Each binding builds its `SqlMetaData` once, in the definition's column order (store type,
length, precision, scale; text with the platform collation's locale rather than the thread's
culture), and one compiled accessor per column, matched by name, that applies the model's value
conversion (so an enum binds by name). Records are created lazily and again on every enumeration,
so a retry re-sends the rows; an empty list binds as no value. Nothing else in the platform builds
a `SqlDataRecord`.

### Batches

`ITenantDatabase` (scoped) creates `IDataBatch`es; `ITenantDatabaseFactory` opens an
`ITenantDatabaseHandle` outside any tenant scope, which runs `Maintenance` batches only, one at a
time on its one connection. A batch is one round trip. Its statements take ordinals in append
order, and a statement's local names carry its ordinal (`@tb3_p0`, `@tb1_ids`). The command is
framed as follows: `SET NOCOUNT ON; SET XACT_ABORT ON;`, the schema guard (`THROW 50501` unless
`dbo.__TellmaSchema` lists the running model's fingerprint, bound as `@tm_schema`), the prologue
statements, `IF @tm_Guard = 1 BEGIN` on a batch marked as carrying the connect prologue,
`BEGIN TRANSACTION;` on an `Auto` batch that declares a write, then the body and the epilogue
statements, and the closing `COMMIT TRANSACTION;` and `END;`. An `Explicit` batch runs in a
`SqlTransaction` instead, and a `None` batch autocommits.

The scope's `IDataBatchContributor`s run their prologue stage, in ascending `Order`, before the
first statement that is not a prologue (or at execution, for a batch with none). They run their
epilogue stage at execution, in reverse order. A raw `Sql` statement binds each hole by its value:
a scalar by its CLR type, a list of table-type rows as a table-valued parameter, and a
`SqlIdentifier` as text. It may write a stack-owned table only under the caller's authority. It
collapses into an identical earlier statement when it declares no write or is idempotent. The
executor counts the result sets against what each statement declares, skips the body's sets when
the guard failed, and maps a server error's line number to the ordinal of the statement that
raised it. It traces each round trip as a span of `Tellma.Core`, logs a slow round trip with its
text, and runs the commit callbacks once the body ran. The statement compilers (queries, saves,
updates, deletes, the version-tag cache) join the batch in later releases; until then those
members throw `NotSupportedException`.

## Tenancy

One catalog database per distribution holds every tenant's registration, state and location
(`catalog.Tenants`), the version every catalog write replaces (`catalog.CatalogState`), the
membership hints (`catalog.TenantMemberships`) and the sessions (`catalog.Sessions`). It holds no
connection string and no secret: a tenant row names a server, a database and a credential profile.

| Piece | What it does |
|---|---|
| `CatalogDbContext`, `CatalogMigrations` | The catalog's EF model, in the platform collation with its own conventions (explicit names, `datetimeoffset(3)`, enum columns as `nvarchar(16)` with `CHECK`s, no identity), its sequence `catalog.sq_Tenants` from 1000, and the membership table type. The migrations live in `Tellma.Core.Migrator`; `CatalogMigrations.Latest` names the last, which the web host's startup compares with the catalog's history. |
| `ITenantRegistry` | The process's snapshot of the tenants with their composed connection strings, probed every `Tellma:Catalog:RefreshInterval` (15 s) and reloaded only when `catalog.CatalogState.Version` differs (`!=`, so a restored older catalog reloads too). Refreshes are single-flight; a forced refresh never joins one already running, so it reads what was committed before it was asked for. Beyond `Tellma:Catalog:MaxStaleness` (5 min) since the last confirmation, or before the first load, every lookup throws `TenantUnavailableException` (`catalog-unavailable`). A row whose server is malformed or outside `Tellma:Sql:AllowedServers`, whose database is malformed, or whose profile is not configured stays out of the snapshot, logged at Error. Listeners (`ITenantStateListener`) hear of every descriptor a reload changed. |
| The refresh service | Loads the registry at start within 10 seconds: a catalog behind `CatalogMigrations.Latest` fails startup naming the migrator command, as does a missing catalog database in Development; an unreachable catalog lets the host start not ready while the loop retries. The migrator turns this verification off. |
| `ITenantConnectionFactory` | A tenant's connection from the snapshot's string, composed with `SqlConnectionStringBuilder` from the profile's fragment, the row's server and database and the deployment id as `Application Name`; and the catalog's. No string is cached twice or logged. |
| `ITenantConnectionProvider` | The data layer's door inside a tenant scope: the snapshot's tenant and a connection to it, for the tenant the scope's request context is bound to and no other. Scoped; it keeps no string of its own, and a tenantless scope gets `InvalidOperationException`. |
| `ITenantCatalog` | Registration (`Provisioning`, the sequence's id or a requested one in 1–999), state transitions against the state table, renames and relocations of frozen or provisioning tenants. Each is one transaction that bumps the catalog version and whose own precondition settles a race (`THROW 50409` translated to `TenantStateException`), then refreshes the local registry. |
| `ITenantMembershipDirectory` | A subject's memberships in one query; the best-effort upsert of the hint writers, never thrown; and the per-tenant reconciliation that also deletes the tenant's stale hints, through the membership table type bound by `TableTypeBinder`. |
| `DatabaseCreation` | The migrator's `CREATE DATABASE` statement: the platform collation, and on Azure SQL Database the configured options and `CATALOG_COLLATION = DATABASE_DEFAULT`. |

Options: `Tellma:Catalog` (`TellmaCatalogOptions`) and `Tellma:Sql` (`TellmaSqlOptions`), validated
by the startup gate.

### The request context

Who a unit of work runs for, against which tenant and in which locale is one immutable
`RequestContext` (`Tellma.Core.Abstractions.Tenancy`) held by the service scope. Nothing copies it
into an `AsyncLocal`, a static or a singleton; a singleton that needs a tenant opens a tenant scope.

| Piece | What it does |
|---|---|
| `IRequestContextHolder` / `IRequestContextAccessor` | One scoped instance behind both. Every service reads `Current`; until a writer binds one it is the tenantless context (English, Gregorian, UTC), stamped by the host's clock on the first read. A writer binds the tenant and the principal in one write. Once bound to a tenant, a scope refuses any other tenant, any other principal (kind or subject) and, once the connection bound one, any other user. |
| `ITenantAccessGuard` | The request-side writer, called once by each tenant front door after the tenant and the principal are bound. In order: the tenant's state verdict (`tenant-suspended`; `tenant-frozen` for a mutation; `tenant-read-only` for a mutation the endpoint does not allow when read-only), every `IRequestContextInitializer` in `Order` (the user connection, which answers `tenant-not-found` for a non-member, then the locale negotiation), and the endpoint's assurance bar (`IndividualRequiredException` for a service account, `StepUpRequiredException` for a session below it). It opens no tenant database itself. When it returns, `UserId` is bound; an anonymous principal answers `tenant-not-found` before the state is looked at, and a composition whose initializers bind no user lets nobody through. |
| `ITenantScopeFactory` | The background-side writer. It looks the tenant up afresh, serves only `Active` and `ReadOnly` (also `Provisioning` and `Frozen` for a `System` snapshot passing `allowNonActive`; any other kind with the flag is refused before a scope exists), binds the snapshot's principal, client and trace parent, runs the initializers with empty inputs, and disposes the scope if any of that fails. A refused state throws `TenantUnavailableException` carrying the state, and a tenant the catalog no longer holds throws it with `tenant-not-found` and no state; `TenantNotFoundException` means one thing only, a run-as user who is not an active member. It starts no activity and opens no logger scope: both are the caller's. |
| `TenantSandboxContext` | The `ISandboxContext` of a tenant host, scoped: the bound tenant's category, and `InvalidOperationException` when no tenant is bound. Registered by `AddTellma` over whatever a host registered before it. |
| Instruments | `tellma.tenancy.resolutions` by `outcome` (the guard counts `served`, `read_only`, `frozen`, `suspended`, `not_member` and a `catalog_unavailable` raised under an initializer; the tenant middleware counts the rest) and `tellma.tenancy.scopes` by `kind` (the factory counts `background`). |

An initializer completes the context and never rebinds it: a result naming another tenant, kind or
subject than it was given, or another user than the one already connected, is refused. The scope's
context is replaced after each initializer, so the services the next one uses already read what the
last one resolved.

Neither writer sets `CultureInfo.CurrentCulture` or `CurrentUICulture`, and no initializer may rely
on setting them: the current cultures flow into what a method awaits and never back to its caller,
so a culture set inside `EnsureAccessAsync`, `CreateScopeAsync` or an initializer is gone before the
handler or the job runs. The caller applies the completed context in its own method, with
`RequestContext.ApplyCultures()`: a tenant front door after the guard returns and before it invokes
the handler, a background caller after the factory returns and before it runs its work. It also
applies the context's calendar and display zone as the flow's request localization (see
[String localization](#string-localization)), resolving the zone's IANA name once per process; a
caller that runs several units on one flow applies each unit's context inside the method that runs
it.

### Server-built links

`ITellmaLinks` builds the links server code puts into an email or a notification from
`Tellma:PublicOrigin`: `Tenant(42)` is `{origin}/42`, and `Target(42, "acme.SalesInvoice", 17)` is
`{origin}/42/sales-invoices/17` when the resource names a stack whose operations include `Details`,
and the tenant link otherwise (a null resource or id, an unknown resource, a stack without a details
page). Which resources name a stack is answered through `IStackRouteSource`; a composition without
a stack registry gets the empty one, so every target links to the tenant.

## Blobs: the file-system store

```csharp
builder.Services.AddFileSystemBlobStore();                       // binds Tellma:Blobs:FileSystem
builder.Services.AddFileSystemBlobStore(o => o.RootPath = "/srv/tellma/blobs");
```

`AddFileSystemBlobStore` registers the process's one `IBlobStore`: the default for local development
and for air-gapped on-premises deployments. Tenant `42`'s objects live under `{RootPath}/t42/`, the
object name's `/` becoming the directory separator (`user-image/3f/3fa9….thumb`).

- **Writes are create-only and atomic.** Each object streams to a fresh
  `{RootPath}/t42/.tmp/{guid}.part`, is flushed to disk, and is published under its final name
  without ever replacing one: a non-overwriting move on Windows, a hard link (`link(2)`) then an
  unlink on Unix, so of two writers racing to one name exactly one wins. On Unix the root's file
  system must support hard links; the store probes that before its first write or provisioning and
  fails closed otherwise (`FileSystemBlobStore.ProbeCreateOnlyPublish` is the startup check's seam). A reader sees an object whole or not at all; a crash leaves at most a `.part` file,
  which `PurgeIncompleteAsync` removes once it is older than the cut-off. A content stream that
  yields more or fewer bytes than declared fails the write and leaves nothing.
- **Names are validated twice.** Every name must satisfy `BlobName.IsValid` before storage is
  touched, and every resolved path must stay inside the tenant's directory.
- **Missing storage reads as absent.** A read, delete, list or purge in a tenant whose directory does
  not exist creates nothing; a write creates the directory once and retries once.
- **Reads share deletes.** A read stream is opened with `FileShare.Read | FileShare.Delete`, so the
  sweep can delete an object while a download of it is still streaming.
- **The root.** In Development an unset `RootPath` defaults to `Tellma/{slug}/blobs` under the
  user's local application data folder (`%LOCALAPPDATA%` on Windows, `~/.local/share` on Linux);
  outside Development it must be set. It must be absolute and, on Windows, at most 153 characters,
  so the deepest object path stays within 259. The resolved root is logged (`BlobFileSystemRoot`)
  when the store is created.
- **Telemetry.** `tellma.blobs.store.operations` and `tellma.blobs.store.duration` on the
  `Tellma.Blobs` meter, one measurement per call, tagged with the operation, the outcome and, on a
  write's duration, its size class; never a tenant.

Internally the namespace also holds the upload content-type sniffer (signatures over the
declaration; executables, HTML and SVG never stored) and the file-name sanitiser, which the
staging service uses.

## Calendars

`CoreFeature` registers the three calendar systems, in this order, and the `ICalendarRegistry` over
every system the composition contributed (a distribution adds one with
`contribution.Calendar<TCalendar>()`):

| Code | System | Exact range | Beyond it |
|---|---|---|---|
| `gc` | `GregorianCalendarSystem`, over `GregorianCalendar` | every `DateOnly` | — |
| `uq` | `UmAlQuraCalendarSystem`, over `UmAlQuraCalendar` | AD 1900-04-30 to 2077-11-16 (AH 1318 to 1500) | the tabular `HijriCalendar` with `HijriAdjustment = 0`, an approximation, except that Muharram 1501, whose tabular first day is the table's last, begins the day after the table with 29 days; before AH 1, the marked ISO Gregorian form |
| `et` | `EthiopianCalendarSystem`, a port of ICU's Julian-day-number arithmetic (13 months, leap year `y % 4 == 3`, one era) | AD 1752-09-14 to 2500-01-01 guaranteed; exact from the epoch, AD 0008-08-27 | before the epoch, the marked ISO Gregorian form |

Rendering is the platform's own code, never a culture's calendar. Each culture is read once
(`CalendarCultureData`): its Gregorian formats (a culture whose own calendar is another, such as
`ar-SA` on Umm al-Qura, which ICU made its default before release 78, is switched to its optional
Gregorian calendar on a private clone), the four styles' patterns
(`Short` the numeric short date, `Full` the long date with a weekday, `Long` without it, `Medium`
with the month abbreviated), its short time pattern and its names. Every pattern loses CLDR's
right-to-left marks and eras, every year renders in full and every digit in ASCII, so a date pasted
from an email or an export stays a date. The weekday leaves `Long` with the punctuation that joined
it, and with only that where the literal beside it also holds words. A date a calendar cannot
represent renders as its ISO Gregorian date marked with the Gregorian code (`0600-01-01 gc`).
`TryParse` reads the numeric style back (any script's digits, any separators between the three
groups, the year in four digits; outside them only white space and the pattern's own leading and
trailing text, such as Bulgarian's ` г.`) and the ISO `yyyy-MM-dd` form, and returns a date only
when the calendar renders it with the same three numbers.

Each system's framework `Calendar` covers exactly the dates the system decomposes: `uq`'s is the
table extended by the tabular calendar under the same rule (`UmAlQuraExtendedCalendar`), so month
arithmetic works beyond the table too.

Month names of `uq` and `et` are Core's resources, `Calendar_<code>_Month<n>` in
`Resources/Strings.resx` (English) and its Arabic and Amharic satellites, taken from CLDR so the
server and the browser agree; `gc` takes the culture's own.

A culture's data comes from the host's ICU unless it is one a request was negotiated to, which
carries the platform's pinned formats (see [Localization](#localization)). Two ICU releases can
differ in a pattern's punctuation or a name's spelling (Windows' and Ubuntu's do, for `en-GB`'s
full date and `am-ET`'s month names, and ICU 78 gives `ar-SA` a Gregorian default calendar). The
golden tests therefore run over the negotiated cultures, the tests of the cleaning pin the raw
culture data they assert, the tests over the host's own cultures assert properties, and the test of
a culture on Umm al-Qura compares it with the same culture's own Gregorian data.

## Localization

### The language catalogue

`LanguageCatalog` (`ILanguageCatalog`) is data in code: fifteen languages (`en`, `ar`, `am`, `om`,
`ti`, `so`, `sw`, `fr`, `es`, `pt`, `de`, `tr`, `ur`, `hi`, `zh`), each with its own name, the
short symbol that labels its column of a multilingual field, its direction and its formats
(`LanguageFormats`): the regions it is offered with (243 language and region pairs in all), its
default region (the United Kingdom for English, otherwise CLDR's likely region) and its native
digits (`arab` for Arabic, `arabext` for Urdu, `deva` for Hindi). The data is frozen so that a
host's ICU release never changes what a user may store.

A distribution's declaration (`tellma.Languages`, `tellma.AddLanguage`) selects what is *offered*;
`All`, `Find` and `Formats` cover everything *described*, so a tenant whose stored language is no
longer offered still resolves its labels. The declaration is resolved leniently (`LanguageSet`):
whatever is wrong is skipped and reported by `core.languages`, and the catalogue itself never
throws at construction, so a build step can compose it to report the problems.

Offering a language other than English takes the platform's strings in it: `core.languages` fails
startup when the `Tellma.Core` satellite assembly of an offered language is not deployed
(`<code>/Tellma.Core.resources.dll`). The platform ships the Arabic and Amharic satellites. For a
language added with `AddLanguage` the platform never ships one, so a distribution that offers the
language supplies the satellite itself: a translation of `Resources/Strings.resx` built into the
resource `Tellma.Core.Resources.Strings.<code>.resources`. A language that is described but not
offered needs no strings.

### Negotiation

`LocalizationNegotiator` (`ILocalizationNegotiator`) turns a request's headers, the user's
preferences and the tenant's settings into a `LocalizationContext`. Each choice falls through its
sources in order and ignores a source whose value is not acceptable:

| Choice | Sources, first acceptable wins | Acceptable when |
|---|---|---|
| Language | the first three `Accept-Language` ranges by quality, the user's preferred language, the tenant's primary language, the distribution's default | offered by the distribution |
| Region | the region of the range that chose the language, the user's preferred region, the tenant's region, the language's default region | offered with the negotiated language |
| Calendar | the calendar header, the user's preferred calendar, the tenant's primary calendar | one of the tenant's calendars |
| Time zone | the time-zone header, the user's preferred zone, the tenant's zone | a value of `core.TimeZone` (ignoring case; an alias is not accepted) that the host resolves |

A message's own language (an email or a notification rendered for a recipient) overrides the
language when it is offered, and its region then heads the region row; a message's own calendar
overrides the calendar when the registry has it. The index of the content language (which of the
tenant's languages multilingual values display in) follows the reader's language, never the
message's. A header that is present and refused is counted in
`tellma.localization.headers.rejected` by a `header` tag (`accept-language`, `calendar`,
`time-zone`): an `Accept-Language` with a malformed element (its sound ranges are still used), a
calendar the tenant does not use, a zone outside the set. A well-formed language the distribution
does not offer is ordinary and is not counted.

### Negotiated cultures

The culture of the outcome (`NegotiatedCultures.Get(language, region)`) is named by the pair
(`ar-SA`), so resources fall back from the pair to the language to English, and it always formats
with the Gregorian calendar, Latin digits and no bidirectional control characters: numbers and
dates pasted from an email or an export stay numbers and dates. One read-only instance exists per
pair.

Its formats are not the host's. Every catalogue pair carries the formats pinned in
`Localization/CultureData.txt`, an embedded resource generated once from CLDR 48 (ICU 78.2) by
`eng/culture-data/Tellma.CultureData.Generator`: every date and time pattern and name
`DateTimeFormatInfo` holds and every symbol, pattern and group size `NumberFormatInfo` holds, read
from the pair's Latin-digit Gregorian culture and cleaned of bidirectional marks and eras. The
generator lays a small table of corrections over CLDR where it regressed or contradicts the
Latin-digit rule (`CultureDataSource.Corrections`). Server text is therefore identical on Windows
and on every Linux image. What stays the host's is everything outside those two format objects:
comparison, casing and the culture's display names. A pair outside the catalogue (a language a
distribution added) has no pinned data and takes the host's Latin-digit Gregorian formats, cleaned
the same way.

### String localization

`AddTellma` replaces the host's `IStringLocalizerFactory` with `IcuStringLocalizerFactory`, so every
`IStringLocalizer` and `IStringLocalizer<T>` in the process renders a resource as an ICU message
(MessageFormat.NET, one shared formatter):

```csharp
localizer["Problem_tree_cycle_Detail"];
localizer["Validation_MaxLength", new { maxLength = 50 }];
localizer["Notification_core_report_shared", new Dictionary<string, object?> { ["actorName"] = name, ["title"] = title }];
```

Arguments are named, in one object: a dictionary or an object whose properties name them (an
anonymous type). A second object, or a lone value (a string, a number, a date), is a positional
argument and throws `ArgumentException`. Plural and select follow the culture's CLDR rules (`ar`'s
zero, one, two, few, many and other), and `#` and `{n, number}` format in that culture.
`{d, date, short|medium|long|full}` and `{t, time, …}` render through the flow's calendar system in
its display zone, with ASCII digits; a `DateOnly` and a `DateTime` of unspecified kind are dates
on the wall clock and never shift.

The culture, the calendar and the zone are the innermost `LocalizationScope`'s, so a job or a
message renders for its recipient:

```csharp
using (LocalizationScope.Enter(recipientContext))
{
    subject = localizer["Notification_core_report_shared", new { actorName, title }];
}
```

Outside every scope the culture is the thread's current UI culture, and the calendar and the zone
are the request's, which `RequestContext.ApplyCultures()` applies (`LocalizationScope.Request`),
else Gregorian and UTC. `LocalizationScope.ApplyRequest` applies one by hand and returns the value
that undoes it, or clears it with null. A resource is looked
up from the culture through its parents to the neutral English set (`ar-SA`, `ar`, English); no
tenant or distribution override is consulted. A key absent everywhere comes back as the key itself,
marked `ResourceNotFound`, logged once (`MissingResourceString`) and counted once per key and culture
in `tellma.localization.missing`, tagged by the culture's language code (`other` for a language the
distribution does not offer). A value that is not a well-formed ICU message, or does not format with
its arguments, comes back raw and is logged once (`MessageFormatMalformed`). A value with
placeholders read without arguments comes back raw, without a log: the caller reads the template.

The host's `LocalizationOptions.ResourcesPath` does not apply: a type's resources are addressed by
its full name (`Tellma.Core.Resources.Strings`), so every package addresses another's the same way.
Core's shared resources are `CoreStrings.BaseName` in `CoreStrings.Assembly`:

```csharp
IStringLocalizer core = factory.Create(CoreStrings.BaseName, CoreStrings.Assembly);
```

### Core's resources

`Resources/Strings.resx` (English, the neutral resources) and its `ar` and `am` satellites hold every
string Core shows, keyed `PascalCase_Underscored`:

| Family | Keys |
|---|---|
| Calendars | `Calendar_<code>`, `Calendar_<code>_Month<n>` |
| Regions | `Region_<code>` for every region of the catalogue, generated (below) |
| Problems | `Problem_<code>` and `Problem_<code>_Detail`, `-` written `_` |
| Validation | each pipeline and settings code with `.` written `_` (`Validation_MaxLength`, `Settings_UnknownSetting`) |
| Labels and actions | `Name`, `Code`, `CreatedAt`, …; `Action_<verb>`, `Entity_New`, `Entity_DisplayNameFallback` |
| Startup | `Boot_*`, the browser application's boot page, which its build copies into the boot script |
| Reports and dashboards | `ReportKind_*`, `ShareAccess_*`, `Core_Report_*`, `Core_Dashboard_*`, their notifications |
| Query language | `Queryex_<code>` for every diagnostic code |

`Strings.resx` reaches the browser through the string pack; `Resources/ServerStrings.resx` holds text
only the server renders and is empty so far.

The region names come from CLDR, never by hand: after the pinned culture data changes, regenerate
them with a Node.js that bundles CLDR 48 (Node 24):

```bash
node eng/region-names/generate-region-names.mjs
node eng/region-names/generate-region-names.mjs --check
```

### Value sets

A value set is a named enumeration over string columns (`ValueSetDescriptor`): its values, each
with a resource key or a literal label, and optional aliases. `ValueSetRegistry`
(`IValueSetRegistry`) holds the platform's four, then every set a feature contributed with
`contribution.ValueSet(descriptor)`:

| Set | Values | Labels |
|---|---|---|
| `core.Language` | every described language, offered or not | each language's own name, a literal |
| `core.Calendar` | the composed calendar systems' codes | the key `Calendar_<code>` |
| `core.TimeZone` | `QueryexTimeZones.Ids`, with its aliases | the id itself, a literal |
| `core.Region` | every described language's regions, in ordinal order | the key `Region_<code>` |

The time-zone set is the query language's own table, so every zone a user can pick compiles in a
query. A string property draws from a set with `[ValueSet("core.TimeZone")]`; the entity metadata
carries the name (`PropertyMetadata.ValueSet`) and the column stays a plain string.
`core.value-sets` validates every set and every such property.

## Layout

| Folder | Contents |
|---|---|
| `Composition/` | `AddTellma`, `TellmaBuilder`, `TellmaComposition.Validate`, `CoreFeature`, `IContributionRealizer<T>`, `RealizationContext`, `TellmaStartupGate`; internally the pipeline, the feature graph, the composition record the checks and later runtimes read, Core's model contributions, and the link builder with its stack-route source. |
| `Composition/Realizers/` | One realizer per item kind the platform understands: services, standalone table types, shape requirements, Core's model configurations, calendar systems, value sets and entities. Each spec that adds an item kind adds its realizer to `ContributionRealizerSet.CreateBuiltIn`. |
| `Composition/Checks/` | The built-in startup checks above. |
| `Data/` | `TellmaDbContext`, `UseTellmaSqlServer`, `TableTypeSweepScopes`; internally the model definition and factory, the platform's own tables and the entity-contract reflection. |
| `Data/Conventions/` | The model conventions above, the plugin that installs them, and the constraint names. |
| `Data/TableTypes/` | `TableTypeBinder`, its bindings and the store-type metadata. |
| `Data/Batches/` | `ITenantDatabase`, `ITenantDatabaseFactory` and their implementations, `DataBatch` with its internal channel, the assembly and line map, the hole renderer, the result-set cursor, the executor and its log events. `DataOptions` and `DataCancellation` sit in `Data/`. |
| `Data/Metadata/` | `EntityMetadataProvider` and the builder that validates the model into it, the compiled accessors, the fingerprints, and the source the startup check and the host share. |
| `Tenancy/` | `CatalogDbContext`, `CatalogMigrations`, the options, `ITenantRegistry`, `ITenantConnectionFactory`, `ITenantConnectionProvider`, `ITenantCatalog`, `TenantInfo`, `TenantLocation`, `TenantRegistration`, `DatabaseCreation`, `IRequestContextHolder`, `ITenantAccessGuard`, `TenantAccessRequirement`; internally the registry and its refresh service, the catalog reader and statements, the location grammar, the connection composition, the state table, the membership directory, the request-context holder, the access guard, the scope factory, the sandbox context, the scope's connection provider, the log events and instruments. |
| `Tenancy/Catalog/` | The row classes the catalog model maps for its migrations. |
| `Blobs/` | `FileSystemBlobStore`, its options, default root and validator, `AddFileSystemBlobStore`, the store instruments and log events; internally the content-type sniffer and the file-name sanitiser. |
| `Calendars/` | `GregorianCalendarSystem`, `UmAlQuraCalendarSystem`, `EthiopianCalendarSystem`, `CalendarRegistry`; internally the Ethiopian and the extended Umm al-Qura framework calendars, the shared renderer and reader, the culture data and the date-pattern reader. |
| `Localization/` | `LanguageCatalog`, `LanguageDeclaration`, `LocalizationNegotiator`, `ValueSetRegistry`, `IcuStringLocalizerFactory`; internally the catalogue's data, the declaration's resolution, the `Accept-Language` reader, the negotiated cultures, the pinned culture data with its reader, writer and generator source, and the localizer's resource reader, ICU renderer, calendar formatter, argument reader and missing-key telemetry. |
| `Resources/` | `Strings.resx`, Core's shared resources (English), with its `ar` and `am` satellites and the `Strings` marker; `ServerStrings.resx`, the server-only resources, with its marker. |

## Tests

`test/core/Tellma.Core.Tests` (`dotnet test test/core/Tellma.Core.Tests`) runs without a database.
`test/core/Tellma.Core.IntegrationTests` (`Category=Integration`) runs the catalog migrations, the
write side, the registry, the membership hints and the tenant scope factory against SQL Server, and
runs batches against the data layer's fixture database (`test/shared/Tellma.Testing.Entities`).
