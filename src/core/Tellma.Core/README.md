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
zone, and the ICU string localizer with Core's shared resources), and the Excel cell codec and
workbook writer. The rest of data access, the CRUD pipeline, access control, settings, caching, the
rest of localization, of blobs and of Excel, jobs and notifications join it as namespaces.

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

1. **Collect.** Registers `DeploymentIdentity(slug, environment)` and `DeploymentVersions` (a
   distribution version the web client cannot state in its `Tellma-Client` header, more than 128
   characters or not visible ASCII, is a problem), adds `CoreFeature` (`core`) first, and runs the composition method. Selection is explicit: there is no
   assembly scanning. Before any of it, the HTTP client factory's default loggers are removed from
   every client as a default (see [Outbound HTTP logging](#outbound-http-logging)).
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

### Outbound HTTP logging

`AddTellma` registers `ConfigureHttpClientDefaults(http => http.RemoveAllLoggers())`, so no HTTP
client of a host that composes a distribution (the web host, the migrator, and the migrator's
`emit-client-assets`) logs through the HTTP client factory's default loggers. At Trace those
loggers write each request and response header into the event's structured state with its raw
value; only the rendered message reads `Authorization: *`. So a bearer token, an API key or a
basic credential would reach any sink that serialises state (Serilog's properties, OpenTelemetry
log attributes) as soon as an operator enables Trace for `System.Net.Http`.

**What operators see change:** the per-request `Start processing HTTP request` / `End processing
HTTP request` lines (and their Trace header lines) are gone from every log, at every level.
Outbound requests remain observable through the HttpClient instrumentation's spans and metrics
wherever an exporter is configured, and each connector logs its own failures (SendGrid's refusals
at Warning, for one). The default applies before each client's own configuration, whenever the
client is registered: a client may add a logger of its own, which then decides what it writes.

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
| `core.string-pack` | a shared `Strings` resource key is declared by two composed assemblies, so the merged string pack would hold two texts for one key | no |
| `core.calendars` | a composed calendar system's code is not 1 to 16 lower-case ASCII letters and digits starting with a letter; two systems share a code, ignoring case; `gc`, `uq` or `et` is not composed | no |
| `core.value-sets` | two C# enum types of one type name are carried under one schema, so they share a name or their values' label keys; a value set is not named `<schema>.<PascalCase>`, repeats a value (ignoring case), gives a value both or neither of a label key and a label, or has an alias that is one of its values or maps to none; two sets share a name, ignoring case; a set has the name of one of the model's enums (`<entity schema>.<EnumType>`); a `[ValueSet]` property names a set no feature contributes | no |
| `core.time-zones` | never: it logs one warning (`TimeZoneUnresolvable`) per value of `core.TimeZone` the host cannot resolve as a tenant's zone (its time-zone data lacks the zone, or the pinned table maps it to no Windows name), the sign of an image with stale tzdata | yes |
| `core.entity-metadata` | the tenant model cannot be built (the convention's message, naming the declaration), or an entity leaf in it breaks the entity contract (every problem at once, see [Entity metadata](#entity-metadata)); a referenced entity with no natural key is logged as a warning and never fails | no |
| `core.version-tags` | an entity declares a tenant-level tag that is not a lower-case name (`^[a-z][a-z0-9.-]*`, at most 128 characters) or that starts with `entity:`; a cacheable entity's implied `entity:<schema>.<Class>` name is too long; a user-level tag names no mapped `int` property or is not `Permissions` or `Preferences`; two names differ in case alone | no |
| `core.shareables` | a shareable's `OwnerId` is not server-owned; its share entity does not reference it exactly once, is also a stack or is a child collection; it has no stack, or its stack's service does not derive from `ShareableService<TEntity, TShare>` | no |
| `jobs.handlers` | an entity-backed job handler processes an entity the tenant model does not map as a job entity; a built-in schedule whose handler key no handler on this host registers is logged as a warning (`SchedulerHandlerUnknown`) and never fails, since another host may run the handler | no |
| `jobs.schedules` | a built-in schedule's cron expression does not parse as five-field Unix cron (Cronos, `CronFormat.Standard`), or has no next occurrence in its zone (the host's resolution of its `TimeZoneId`, else UTC) | no |

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
- **The access tables** (`Tellma.Core.Access`). `CoreFeature` contributes `User` and `Role` (whose
  children `RoleMembership` and `Permission` follow), each configuration open over its leaf: the
  stored `State` derived from the invitation and join evidence, the covering `UX_Users_Subject`
  the connect prologue seeks, the kind and evidence checks, the membership window, the wildcard
  and filter-version checks, and the seeds (the system user, the `Administrator` role, its wildcard
  grant and the system user's membership in it, all in the reserved band below 1000). Beside them,
  two tables that are never entities: `core.UserStamps` (one row per user, the per-user version
  tags and inbox markers, deleted with the user; queryable read-only) and `core.UserPreferences`
  (key and JSON value per user, deleted with the user), with the `UserPreferenceList` table type.
- **`core.Blobs`** (`Tellma.Core.Blobs`), the table every `[BlobReference]` resolves to: never a
  stack, its storage key and hash shadow columns, its audit reference to the user leaf, and the
  sweep's, the storage key's and the staging quota's indexes.
- **Standalone table types**: every `contribution.TableType<T>()` class, in `dbo` under its class
  name unless its `[TableType]` says otherwise. `CoreFeature` contributes `IdList`, `BigIdList`,
  `GuidList`, `StringList`, `DateList`, `IdStampList`, `VersionTagList` and `UserPreferenceList`.
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
| `ChildConvention` | `[ParentKey]` → `FK_<Table>_<Column>` to the parent, no cascade, `IX_<Table>_<Column> INCLUDE (Id)` unless a configuration declares an unfiltered index that leads with the parent key, which then serves the parent's reads |
| `ReferenceConvention` | `[References(typeof(T))]` → `FK_<Table>_<Column>` to the leaf registered for `T`, no cascade, `IX_<Table>_<Column>` |
| `SiblingConvention` | `[Sibling]` → `FK_<Table>_Id` to the owner's leaf, `ON DELETE CASCADE` |
| `TableIndexConvention` | `[TableIndex(…)]` → `IX_<Table>_<Columns>` in the declared order |
| `TreeConvention` | `ParentId` → `FK_<Table>_ParentId` to the table itself and `IX_<Table>_ParentId`; shadow `Node hierarchyid NOT NULL`, `UX_<Table>_Node`, left out of the table type; subtree counts default 1; the maximum depth recorded |
| `ActivatableConvention` | `IsActive` defaults 1, `DF_<Table>_IsActive`, for inserts outside the save pipeline (which sets new rows active itself) |
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
annotations decorate mapped properties of the right type; and no mapped type, entity or not, holds
a shadow property the platform does not declare. The declared ones are the period columns
`ValidFrom`/`ValidTo` of a temporal entity, the `Node` of a tree, and the model-only columns of
`core.Jobs` (`StateJson`, `LeaseToken`, `ConfirmedKeys`) and `core.Blobs` (`StorageKey`,
`Sha256`), which the query language withholds like the period columns. Any other is refused, naming the column: above all
the shadow foreign key EF Core adds for a navigation without a foreign-key property, since an
entity carries foreign keys and never navigations. A many-to-many pair of mapped collections, whose
join table no class carries, and an owned type, whose keys no class carries, are each refused once,
naming the navigations behind them.

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
`BEGIN TRANSACTION;` on an `Auto` batch that declares a write, the transaction-head statements
(refused on a batch whose text opens no transaction), the persist re-checks, then the body and the
epilogue statements, and the closing `COMMIT TRANSACTION;` and `END;`. An `Explicit` batch runs in
a `SqlTransaction` instead, and a `None` batch autocommits. The persist re-checks open a writing
persist batch that carries the connect prologue: the version-tag guard compares the tags the batch
composed under (`settings`, the caller's `permissions`, its declared dependencies; bound as
`@tm_expectedTags`, each marked when the batch bumps it, through the optional
`IVersionTagRegistry`) and the caller's per-user permissions tag under locks held to `COMMIT`
(`THROW 50412`), then a user's batch re-checks that its caller is still active (`THROW 50401`).
The platform's own batches (`GuardPremises.ForSystem`) re-check the tags alone. The guard locks
the tag rows one at a time in binary name order, each by a seek on its name, and compares them
afterwards: SQL Server releases the lock of a row a seek's residual predicate rejects, which would
leave every fresh row unlocked. Each row is locked in its final mode: `XLOCK` for a name the batch
bumps (a bumped name it does not depend on is locked and never compared), `REPEATABLEREAD` for
every other, so the bump converts no lock. The caller's stamp row comes after every tag row, under
`UPDLOCK` when the batch writes the stamps table, so every batch takes the tag rows, in one order,
before any stamp row. An expectation of `VersionTag.None` is compared like any other.

The scope's `IDataBatchContributor`s run their prologue stage, in ascending `Order`, before the
first statement that is not a prologue (or at execution, for a batch with none). They run their
epilogue stage at execution, in reverse order. A raw `Sql` statement binds each hole by its value:
a scalar by its CLR type, a list of table-type rows as a table-valued parameter, and a
`SqlIdentifier` as text. It may write a stack-owned table only under the caller's authority. It
collapses into an identical earlier statement when it declares no write or is idempotent. The
executor counts the result sets against what each statement declares, skips the body's sets when
the guard failed, and maps a server error's line number to the ordinal of the statement that
raised it. It traces each round trip as a span of `Tellma.Core`, logs a slow round trip with its
text, and runs the commit callbacks once the body ran.

A failed round trip is classified. A transient error the server reported (a deadlock, a lock
timeout, throttling, `THROW 50503`), at any severity, rolled the batch back under `XACT_ABORT`, so
the whole round trip is sent again, up to `Tellma:Data:RetryAttempts` re-runs after waits of 50,
200 and 800 ms (jittered); then `DataAccessRetryExhaustedException`. A lost connection is re-run
when every statement may run twice (reads, idempotent writes), except that a read or validation
batch whose command timed out is re-run once only; a persist batch in its own transaction that
declared the root rows it inserts (`DataBatch.DeclareInsertedRoots`) is probed on a fresh
connection (a locking seek on those keys), and a not-committed verdict is re-run; anything else is
`DataAccessAmbiguousException` with its verdict. The probe itself runs again, with the same waits
and attempts, when its own round trip fails transiently or loses its connection. After a
not-committed verdict, any error that would reach the caller (the re-run's own, exhaustion,
cancellation) is preceded by a second probe, because the first attempt may have committed late;
after a write re-run without a verdict, any such error surfaces as `DataAccessAmbiguousException`
(unknown). An `Explicit` batch is never re-run once sent. A request the client fails to encode
(a table-valued parameter's row it cannot convert) was never run, so its error surfaces as it is.
The caller's token cancels the opens and the waits between attempts, and every command except a
sent persist's; a cancellation that leaves a write's outcome unknown surfaces as
`DataAccessAmbiguousException`, never as `OperationCanceledException`, while an `Explicit` batch
cancelled before its commit was rolled back and surfaces the cancellation. A final error
becomes the data layer's exception for it, carrying the statement's ordinal: the
`Tellma.Core.Data` exceptions for row security, conflicts, uniqueness, foreign keys, trees and
arithmetic, `TenantUnavailableException` (`tenant-schema-behind`, retry after 30 s) for the schema
guard's refusal, `BatchAssertionFailedException` for any other assigned platform or distribution
number, `InvalidOperationException` for a number outside the bands (a defect), and `DataAccessException`
for the rest. A number outside the bands in a batch that commits its own transaction may come from
a `RAISERROR`, which does not abort the batch, so it is probed (or reported unknown) as a defect
before it surfaces. A statement's reader stages its results on the cursor, so only the attempt that
counts completes them. The executor meters `tellma.data.roundtrips` (per scope that resolves its
`DataAccessScope`, recorded when the scope ends, zero included), `tellma.data.roundtrip.duration`,
`tellma.data.batch.statements`, `tellma.data.retries` and `tellma.data.commit.probes`.

### Ids and saves

There are no identity columns: `IIdAllocator` (a singleton) hands out ids from each table's
`sq_<Table>` sequence through a buffer of contiguous ranges per tenant and sequence. `Reserve`
counts the exact deficit of a payload (every row with no id yet, children included, a temporary
negative id unique per table), serves it from the buffer, and appends one `sp_sequence_get_range`
statement per short table to the batch it is given, sized to the shortfall plus
`Tellma:Data:IdBufferLowWater`, reading the table's `MAX([Id])` with the range. Once that batch
ran, `Assign` gives every new row its id in topological order, sets each child's parent key from
the nesting and rewrites temporary ids in self-typed references and a tree's parent key; a
temporary id no row defines stays negative for the pipeline to report. Disposing the reservation
before a batch saving its rows executed returns its ids to the front of the buffer; after one, even
a failed one, none returns. A range that starts at or under the table's largest id is discarded
and one more autocommit round trip consumes the gap (`IdAllocator.Healed`, Warning). A duplicate
primary key is never healed or retried: the executor drops that sequence's buffer
(`IdAllocator.BufferDropped`) and rethrows. Any batch writing a table whose buffer is in use and
below the low-water mark carries a refill. A refill that finds the sequence behind its table keeps
only the ids above it, logs `IdAllocator.BehindOnRefill` once and stops refilling until a
reservation heals; no id at or below the largest id a read found the table holding is buffered
again, whichever path returns it (`IdAllocator.StaleDiscarded`). A tenant's buffers are forgotten
when it enters or leaves `Frozen` or leaves the registry, since a relocation or a restore may put
another database behind it. `TakeAsync` serves callers outside any batch, with one
autocommit round trip when the buffer is short. `ReserveBuffered` (the internal `IIdBuffer`) is the
save pipeline's first try: an immediate reservation when the buffer covers every table's deficit,
else nothing, every take given back unmetered so the reservation that rides round trip 1 starts
from the same buffer. It takes the scope's tenant database, whose location picks the buffers as a
batch's does: one another database filled is dropped, never served.

`IDataBatch.Save` appends one statement per save, its text cached per root entity and recompile
hint: two table-valued parameters per table (new and existing rows) plus each child collection's
parents; the saved set; a concurrency guard taking U locks on every existing root that selects its
conflict rows and throws `50409` with a capped JSON message (90 ids per list for an `int` key, 50
for a `bigint` one); the root insert with server stamps and a tree's provisional node; each child
collection synchronised under the parents that supplied it (removed rows' descendants deleted
deepest first, changed rows updated, new rows inserted), nested collections after; and the root
update of rows that changed or whose children did, stamped. Unchanged rows are skipped by a
binary-collated `EXCEPT`, so a no-op leaves no history row. Every blob-reference column has a
capture table of old and new ids, which the `SaveHandle` names with the saved, new and touched
root sets. Above `Tellma:Data:LargeBatchThreshold` rows in any table every data-modifying statement
carries `OPTION (RECOMPILE)`. A save needs its batch in one transaction. Once the batch ran, the
rows carry the stamps the statement wrote and the new rows count as existing. The allocator meters
`tellma.data.ids.*`, the emitter `tellma.data.rows.saved` and `tellma.data.concurrency.conflicts`.

A tree save (`[Tree]`) runs the tree statements after its root update, in the same statement:
the affected set (every saved row and every descendant of a saved existing row), the paths walked
top-down from the rows whose parent lies outside it, a fence that throws `Tree.Cycle` when a row
no walk reached closes a cycle, the old nodes, the re-path of the rows whose node changed (never
stamped), and the recount of the old and new nodes' ancestors. A node is the ids from the root,
`/5/12/`; a new row's provisional node lasts only until the re-path. Both recursive walks carry
`MAXRECURSION` one below the tree's depth, so a row nested deeper than it is error 530,
`TreeDepthExceededException`. The walks' anchor parents are U-locked to the commit, by a seek on
the key, so a save under a node a concurrent save is moving reads the parent after the move, or
deadlocks with it and reads it on the victim's re-run. Two concurrent saves that would together
close a cycle likewise serialise, the second seeing the first's move, or deadlock, and the
victim's re-run meets the fence.

### Updates and deletes

`IDataBatch.Update` assigns the same values to every row it selects, in one statement that skips
the rows already holding them (null-safe, character columns compared binary), so a repeated
activation writes no history row; the updated ids are the receipt. By ids, `StampUnchanged` writes
and stamps those rows too, for an action's command over each target. By ids it captures the ids the
grant filter passes, the others skipped; all-or-nothing first selects the ids the read filter does
not show (absent and hidden alike) and throws `50404` with them, then throws `50403` unless every
id passes the grant filter. By query it captures at most the cap plus one keys and throws `50413`
past the cap and `50428` on another count. It never assigns the key, the audit or tree columns, the
parent of a tree row (a save re-paths it), a blob reference or a write-once column, nor a text
or binary value longer than its column, an undefined enum member or a decimal with more integral
digits than its column holds, and stamps
`ModifiedAt`/`ModifiedById` unless the table has no audit columns (`Stamp = false`). Assigning an
activatable tree's `IsActive` recounts the rows' ancestors.

`IDataBatch.Delete` deletes a root's collections deepest first in explicit statements, then the
roots, whose ids are the receipt; a sibling row goes by its cascade, except that a sibling holding
a blob reference is deleted explicitly before its owner, so its blobs are captured too. Every
table the database writes by the `ON DELETE` action of a key referencing a deleted table (a
sibling's cascade, `core.ScheduleStates` cascading and `core.Jobs.ScheduleId` set to null with a
schedule, a user's stamp and preference rows cascading with the user), and transitively the
tables a cascaded table's own keys write, is read from the relational model (`ForeignKeyActions`)
and declared among the delete's writes, for tags, guards and change tracking; the caller needs
no authority over those tables, and gains none for its own statements. A key's action reports no
rows, so when such a table has a user-level version tag rule (the preference tables do) the
delete first reads the users its reached rows name, by the keys from every table that loses rows,
into a capture the bump epilogue reads; the capture reads under update and range locks, so no
row the keys will reach is added after it. A save that deletes child rows declares the same
consequences of their deletion. A model where one of them has a user-level rule (a save cannot
tell which rows its deletes reach), or where a `SET DEFAULT` key writes a rule's user column,
fails the `core.version-tags` startup check. By ids it captures the existing rows under U locks and the read filter, selects the rest
and throws `50404` with them, throws `50403` unless every captured row passes the grant filter,
and with the stamps the rows were read with throws `50409` with the conflict rows the save's guard
reports. By query it caps and counts its keys as an update does. `WithDescendants` deletes tree
roots with every row under their nodes, captured under U locks, the roots checked as ids are and
the whole closure against the grant filter. A tree's old nodes are kept for the recount of their ancestors. The
`DeleteHandle` names the deleted keys and one capture table per blob-reference column, which a
later statement of the batch may read. A reference from another row is `547`,
`ForeignKeyViolationException`, whose `Conflict` is `StillReferenced` (a write's reference naming
no row is `MissingTarget`). A check that embeds a compiled filter takes a statement of its own,
so one update or delete is one to two statements; every statement of a call carries
`OPTION (RECOMPILE)` above `Tellma:Data:LargeBatchThreshold` ids or when a compiled filter asks for
it. Updates and deletes, like saves, need their batch in one transaction; the executor meters
`tellma.data.rows.deleted` and, for every tree statement, `tellma.data.tree.recomputes`.

`TreeStatements.Verify` appends the whole-table recount of a tree on the internal channel, the
form the weekly verify job runs in a maintenance batch; it returns how many rows it repaired,
meters `tellma.data.tree.repairs`, and logs `TreeVerify.Repaired` at Warning when any were, since
the persist-time statements keep the counts exact. `TreeCycleValidator<T>` is the C# form of the
fence, which the save pipeline runs for a tree on round trip 1 (`Request`, then `Report` once the
ids are assigned): it loads every row's parent once, lays the payload's over them, and reports `Tree.Cycle` on the rows that close a cycle (a row its own parent included) and
`Tree.TooDeep` on a row whose subtree would nest past the depth, each at the row's `ParentId`.

### Queries

The batch's query statements compile through the query language (`Tellma.Core.Queryex`) against
the schema of the tenant's multilingual shape. `QueryexSchemaProvider` (also
`IQueryexSchemaProvider`) builds one schema per shape from the entity metadata and the runtime
model: every entity under its metadata name, each mapped scalar with its store type, references
and parent keys as navigations, a sibling as an optional navigation on the owner's id (a left
join), and a tree's node; JSON, period and platform-secret columns are withheld, and only the
group twins the shape admits are properties. A column's own `[Collation]` rides on its property,
so a key-set restriction converts the table's keys to it, and the language refuses text of two
collations in one operation; the attribute is the only source, since the runtime model keeps no
collation, so `CollationConvention` refuses a fluent `UseCollation` on a tenant column. A table
annotated `Tellma:Queryex:ReadOnlyEntity` with a query name joins the schema as a read-only root
with no entity metadata: `core.UserStamp`, keyed by `UserId`, with its `User` navigation and its
activity and inbox columns (its two cache tags are withheld). Rows and counts read it; entity
queries and writes have no class for it. Its fingerprint digests the model.

| Member | Statements |
|---|---|
| `Rows` | One page of columnar rows. With `CaptureKeys` or `IncludeAncestors`, a keys-only twin first fills a statement-local table (`@tb{b}_keys`) under the filter, restrictions and paging, and the page reads those keys in the spec's order, its key appended as the last tiebreak only where the ordering does not already hold it (in any position or direction, as the language decides for the twin: the page keeps a zero skip, `OFFSET @qx{n}_skip ROWS`, which marks it paged); a capped count (cap + 1 rows counted) and a tree's ancestors (the nodes above the page, not on it, in the same order) follow. A grouped spec captures no keys and climbs to no ancestors; its count counts its groups. A grouped spec's `Measures` add their partial columns after the select items, which the page reads like any other column, its decimals with the precision and scale the server reports, so a caller that compiled the same spec itself evaluates its measures over the page; the count compiles without them. Each call's statements fit the batch's parameter budget together or none is added. |
| `Count` | A capped count: a result of cap + 1 means more than the cap. |
| `EvaluateConstant` | One row of path-free expressions. |
| `Assert` | A count compared with an expected value, throwing the given platform or distribution number and code. |
| `Query<TEntity>` | Entities: the root query with the keys the materializer needs appended, each named child collection restricted to its parents' keys (captured once, or the caller's id restriction), grandchildren through their parents' captured keys, the ancestors of a tree, and the JSON columns only with `AllProperties`. It selects only properties of its classes: a schema column a class does not hold, such as a tree's node, is a caller error that a rows query reads instead. Related entities dedupe by id, and each set's projection is the columns every instance carries. |

Every query statement of a batch binds the same context values (today, now, the user, the time
zone), read once per batch, and an identical statement composed in the same write epoch answers
from the first. A refused compile throws `ArgumentException` naming the diagnostics and leaves the
batch as it was. `IDisplayNames` renders an entity's display name from the columns it names under a
shape, by the rule the browser renders with; both sides read the golden file
`display-name-golden.json` of the client's core package.

## Tenancy

One catalog database per distribution holds every tenant's registration, state and location
(`catalog.Tenants`), the version every catalog write replaces (`catalog.CatalogState`), the
membership hints (`catalog.TenantMemberships`) and the sessions (`catalog.Sessions`). It holds no
connection string and no secret: a tenant row names a server, a database and a credential profile.

| Piece | What it does |
|---|---|
| `CatalogDbContext`, `CatalogMigrations` | The catalog's EF model, in the platform collation with its own conventions (explicit names, `datetimeoffset(3)`, enum columns as `nvarchar(16)` with `CHECK`s, no identity), its sequence `catalog.sq_Tenants` from 1000, and the membership table type. The migrations live in `Tellma.Core.Migrator`; `CatalogMigrations.Latest` names the last, which the web host's startup compares with the catalog's history. |
| `ITenantRegistry` | The process's snapshot of the tenants with their composed connection strings, probed every `Tellma:Catalog:RefreshInterval` (15 s) and reloaded only when `catalog.CatalogState.Version` differs (`!=`, so a restored older catalog reloads too). Refreshes are single-flight; a forced refresh never joins one already running, so it reads what was committed before it was asked for. Beyond `Tellma:Catalog:MaxStaleness` (5 min) since the last confirmation, or before the first load, every lookup throws `TenantUnavailableException` (`catalog-unavailable`). A row whose server is malformed or outside `Tellma:Sql:AllowedServers`, whose database is malformed, or whose profile is not configured stays out of the snapshot, logged at Error. Listeners (`ITenantStateListener`) hear of every descriptor a reload changed. A host that loads no snapshot at startup (the migrator) refreshes only after its own first load, so it never reads a catalog it has yet to create. |
| The refresh service | Loads the registry at start within 10 seconds: a catalog behind `CatalogMigrations.Latest` fails startup naming the migrator command, as does a missing catalog database in Development (a login the server cuts off before saying why is settled by asking `master` whether the database exists); an unreachable catalog lets the host start not ready while the loop retries. The migrator turns this verification off. |
| `ITenantConnectionFactory` | A tenant's connection from the snapshot's string, composed with `SqlConnectionStringBuilder` from the profile's fragment, the row's server and database and the deployment id as `Application Name`, with SqlClient's connect retry off (`Connect Retry Count=0`) because the executor owns retry; and the catalog's. No string is cached twice or logged. |
| `ITenantConnectionProvider` | The data layer's door inside a tenant scope: the snapshot's tenant and a connection to it, for the tenant the scope's request context is bound to and no other. Scoped; it keeps no string of its own, and a tenantless scope gets `InvalidOperationException`. |
| `ITenantCatalog` | Registration (`Provisioning`, the sequence's id or a requested one in 1–999), state transitions against the state table, renames and relocations of frozen or provisioning tenants. Each is one transaction that bumps the catalog version and whose own precondition settles a race (`THROW 50409` translated to `TenantStateException`), then refreshes the local registry. |
| `ITenantMembershipDirectory` | A subject's memberships in one query; the best-effort upsert of the hint writers, never thrown; and the per-tenant reconciliation that also deletes the tenant's stale hints, through the membership table type bound by `TableTypeBinder`. Internally, one tenant's hints in subject order, for the control plane's member list. |
| `ITenantProvisioningTrigger` | Starts provisioning a tenant the control plane registered. `NotConfiguredProvisioningTrigger`, the default, starts nothing; the web host's admin surface answers `501 provisioning-not-configured` while it is the one registered. |
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
page). Which resources name a stack is answered through `IStackRouteSource`, which the stack
registry implements (see [Entity stacks](#entity-stacks)); a host may register its own first.

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

## Blobs: kinds, options and staging

```csharp
// A feature adjusts a kind it or another feature declares; the host adjusts after every feature.
contribution.BlobKind("acme-scan", policy => policy with { MaxSize = 8 * 1024 * 1024 });
tellma.Blobs(blobs => blobs
    .Kind("user-image", policy => policy with { Thumbnail = null })
    .ImageProcessor<MyProcessor>());
```

- **Kinds come from the model.** Every `[BlobReference(kind, preset)]` of a stack's aggregate
  declares one kind, once across the composition. `IBlobKindRegistry` lists them with the policy
  (the preset after every transform, features' in contribution order and then the host's) and the
  owner: the nearest entity with a query surface (a stack's root, or a child that is queryable),
  the column, and the navigation path from the declaring entity up to that owner (`Spread.Album`
  for a grandchild whose parent has no query surface). The entity metadata carries the same policy
  instance. A transform that throws or returns null, or a kind on an entity no stack carries, fails
  the composition naming the kind.
- **Options.** `BlobOptions` binds `Tellma:Blobs`: `StagingTtl` (24 h; a kind's own `StagingTtl`
  overrides it), `MaxStagedPerUser` (200) and `MaxStagedBytesPerUser` (512 MiB) for the soft
  per-user quota, `MaxUploadSize` (100 MiB, the ceiling over every kind's `MaxSize`),
  `ThumbnailTimeout` (5 s), `ImageProcessTimeout` (15 s), and the sweep's `SweepBatchSize` and
  `SweepReclaimAfter`. A large body spools to `Tellma:ScratchPath` (the system temp directory when
  unset), in a file deleted on close.
- **Staging.** `IBlobService.StageAsync` (scoped) checks the declared length against the kind's
  `MaxSize` and the ceiling before reading, reads at most one byte more than declared, sniffs the
  type against the kind's allow-list, normalises an image kind through the one `IImageProcessor`
  (bounded by `ImageProcessTimeout`), renders a thumbnail where the policy asks for one (a render
  that returns nothing or throws stores none, with a `BlobThumbnailFailed` warning for a throw),
  inserts the `Staged` row — its id from `core.sq_Blobs`, its deadline from the
  database clock — and then writes the primary and the thumbnail to the store under a fresh
  storage key. A refusal is a `BlobRejectedException` whose code names a resource string:
  `Blob.UnknownKind`, `Blob.LengthRequired` (no length, or a body shorter than declared),
  `Blob.TooLarge`, `Blob.UnsupportedType` (naming what the bytes are, never a declaration they
  contradict), `Blob.ImageRejected` and `Blob.StagingQuotaExceeded`.
  The quota counts the caller's unexpired staged rows and bytes in the same statement as the
  insert, without locks (it is soft: two racing uploads may both pass), and does not apply to the
  platform's own scope. A store write that fails leaves the staged row for the sweep.
- **Provisioning.** The `core.blob-container` step (order 20, after `core.settings`) creates the
  tenant's container or directory through `IBlobStore.EnsureTenantAsync`, so the first upload finds
  it; a write that finds it missing still creates it. Removing a retired tenant's storage is an
  operator's action.
- **The startup checks.** `blobs` reads the composition alone, so the client-asset emission runs it
  too: at most one `IImageProcessor` (exactly one when an image kind exists), a transform only for a
  declared kind, and every final policy well formed. `blobs.deployment` needs infrastructure:
  exactly one `IBlobStore`, positive options (the processor's time budgets also within a timer's
  range), every kind's `MaxSize` within `MaxUploadSize`, an absolute scratch path and, for the
  file-system store, bound options and a passing create-only publish probe. A host that runs no
  real store composes `AddInMemoryBlobs()` from `Tellma.Core.Testing` in its tests.
- **Telemetry.** `tellma.blobs.uploads` counts every staging call by kind and outcome (`staged`,
  each refusal, `busy`, `failed`), with `tellma.blobs.upload.bytes` and
  `tellma.blobs.upload.duration` beside it, on the `Tellma.Core` meter (the store's instruments
  alone have the `Tellma.Blobs` meter); never a tenant, a user or a file name.

## Blobs: attaching, releasing and downloading

- **The pair.** Every stack whose aggregate carries a `[BlobReference]` column, on its root or on a
  child at any depth, runs `BlobReferenceValidator<T>` and `BlobReferenceEffect<T>`, attached by
  `core` ahead of every other validator and effect. Nothing registers them by hand.
- **The attach rule.** A value is newly attached when it is non-null on an inserted row (a child
  row whose key the stored collection lacks counts as inserted) or differs from the stored value
  on an updated one. Each must name a `Staged`, unexpired blob of the column's kind that the saving
  user uploaded, once in the whole payload; otherwise the row's property is refused with
  `Blob.NotAttachable` and a `reason` argument (`missing`, `not-staged`, `kind`, `uploader`,
  `expired`) or `Blob.DuplicateReference`. The blob rows load on the save's first round whenever it
  runs one (update rows, references, uniqueness, a tree, or new ids a cold buffer cannot cover, in
  which case the save requests every blob row of the payload on that round itself). The one shape
  that pays a round for them is an insert-only save whose ids come from a warm buffer and that
  needs no first round otherwise. A blob column is never checked as a reference or for uniqueness,
  so attaching needs no grant on `core.Blob`.
- **The persist.** For each column the effect releases the blobs the capture's old values name
  where the value changed or was cleared (`Released`, due at once), then confirms the new ones
  (`Committed`, no deadline) under the same conditions the validator checked, and throws
  `Blob.NotAttachable` (reason `persist`) when any new value was not confirmed: a blob that expired,
  was claimed or deleted, or was taken by another row after the validation round rolls the whole
  save back, and a missing blob row's foreign key failure is reported the same way. A
  delete of any shape releases its rows' blobs and their children's. The old values come from the
  rows as written, so an override save releases what the row actually held.
- **Downloading.** `IBlobService.ResolveAsync(kind, id, variant)` decides the caller's read of the
  kind's owner resource, then reads, on one read round trip, the owner row holding the id under that
  read (through the owner path for a child kind whose entity has no query surface) and the blob
  row. A committed blob is served when the owner row is visible; a staged one to its uploader alone;
  a variant only when the row lists it and the kind declares its format. Missing, hidden, released
  and another kind's blobs are all `null`, logged at debug level as `BlobDownloadNotFound`; a
  malformed variant is a `BadRequestException`.
  - **Step-up.** When the owner's read is sensitive, a granted owner read asks for the step-up a
    direct read asks for (`StepUpRequiredException`, or `IndividualRequiredException` for a service
    account), whether or not the owner row was found. The uploader's own staged blob, a kind any
    member reads, and the platform's own work are outside it.
  - **Round trips.** A download takes one round trip. The exception is a caller denied the owner
    read whose cached set is older than the fast-deny window: that caller first pays the evaluator's
    prologue-only re-check, so the download takes two.
- **Telemetry.** `tellma.blobs.attach.rejected` counts each refused attach by kind and reason
  (`duplicate` and `persist` beside the reasons above).

## Blobs: sweep, reconcile and verification

- **The sweep.** `core.blob-sweep` (built-in schedule 4, every 15 minutes; one run per tenant, on a
  read-only tenant too, as the system user) repeats rounds until one claims fewer than
  `SweepBatchSize` rows or the job is cancelled. A round's claim, a `Maintenance` batch with no
  transaction of its own, takes the earliest rows that are staged past their deadline, released,
  or deleting past their reclaim instant, on the database's clock, and marks them `Deleting` until
  `SweepReclaimAfter` from then. The round then deletes each row's primary and listed variants from
  the store in one call (a missing object is success), and row by row when that call fails, then
  deletes the rows whose objects are gone and that are still `Deleting`. Once per run the store
  purges its incomplete artefacts older than a day. Objects go before rows, so an object never
  outlives the row that leads the sweep to it.
- **Failures never fail the run.** A row whose objects the store could not delete, a row whose
  names fall outside the blob name grammar (staging never writes one), and a row a foreign key
  still holds (impossible by construction: only staged rows are confirmed), stay
  claimed for the sweep after their reclaim instant, are counted in `tellma.blobs.sweep.failures`
  and are warned of (`BlobSweepStoreFailed` with the store's exception, `BlobSweepRowRetained` with
  the id); a failed purge is counted and warned of too. `tellma.blobs.sweep.deleted` counts the rows
  deleted by the state they were claimed from (`staged`, `released`, `deleting`), each round that
  claimed rows logs `BlobSweepBatch` (claimed, deleted, failed), and the progress message `Blob.SweepProgress` counts
  the rows claimed and the objects deleted so far.
- **The reconcile.** `core.blob-reconcile` (built-in schedule 5, Saturday at 02:00) runs
  `BlobStatements.Reconcile` once per registered kind, in kind order, each its own `Maintenance`
  round trip over the kind's owner table and column: a kind's committed blobs that no owner row
  references and that were committed more than a day ago are released for the sweep. A non-zero
  count points at a path that bypassed the save pipeline, so it is counted in
  `tellma.blobs.reconcile.released` by kind and warned of (`BlobReconcileReleased`). Progress is
  `Blob.ReconcileProgress`, after each kind.
- **Verification.** `BlobVerifier` backs the migrator's `blobs verify --tenant <id>
  [--delete-orphans]`: for each registered kind and each of its 256 fan-out segments it lists the
  store, then reads the segment's rows, and reports the objects no row references and the objects a
  row references that the store lacks (a deleting row and a row staged within the last five minutes
  excepted). Listing before reading makes a reported orphan a true one, so `--delete-orphans`
  deletes it safely; rows are never written.
- **Alerts.** `infra/monitoring/blobs` holds the alert queries over `sweep.failures`,
  `reconcile.released` and the `attach.rejected` rate, checked against these instruments by a test.

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
arithmetic works beyond the table too, up to `DateOnly.MaxValue` (AD 9999-12-31, AH 9666-04-03).
Month and year lengths follow the tabular rule to the end of AH 9666, so the months that end after
that day still have lengths; a day after it does not compose, and `ToDateTime` and the arithmetic
refuse it with `ArgumentOutOfRangeException`.

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

### String packs and labels

`StringPackComposer` (`IStringPackProvider`, `INeutralStringKeys`) merges the `Strings` resources
of every composed feature's assembly (each manifest ending `.Resources.Strings.resources`; a
`ServerStrings` set never ships) into one flat dictionary per language: English, which every pack
falls back to, then each language the distribution offers. Keys are kept verbatim, a satellite's
value replaces the neutral one only in its own language, and the pack is its dictionary as JSON with
the keys in ordinal order, versioned by `DeploymentVersions.DistributionVersion`. The dictionaries
stay in memory for the server's own labels. A key belongs to the first assembly whose neutral set
declares it, and only that assembly's translations of it are merged; a translation of a key no
neutral set declares is left out. A key two assemblies both declare, neutrally or one of them in a
satellite, fails startup: the `core.string-pack` check reports one problem per key and pair of
resource sets.
`StringPackClientAssetContributor` hands every pack to the client-asset emission, which writes
`assets/strings-<language>-<hash>.json` and names each file in the module's `strings` map.

`LabelProvider` (`ILabelProvider`) answers every label the server shows from the merged dictionaries,
in the reader's language (the innermost `LocalizationScope`'s culture, else the thread's UI culture)
and else English. Each label walks its key chain (`LabelChains`, which the client entity model and
the conventional keys walk too), the first declared key wins, then falls back to the humanized name
(`P` is the entity's key prefix, `S` a schema's):

| Label | Keys, first declared wins |
|---|---|
| Entity | `P_Plural`, `P` (plural); `P` (singular); a missing plural reads the singular label |
| Create | `P_New`, `Entity_New` |
| Property | `P_<property>`, `<property>`; a multilingual twin reads its primary's |
| Navigation | `P_<navigation>`, `P_<foreign key>`, `<foreign key>`, `<navigation>`; a sibling has no foreign key of its own |
| Collection | `P_<collection>`, the child's `P_Plural`, the child's `P` |
| Action | `P_Action_<Action>`, `Action_<Action>` |
| Action field | `P_Action_<Action>_<field>`, `P_<field>`, `<field>`; an `[EntityRef]` field drops its `Id` (`AssigneeId` reads `Assignee`) |
| Enum value | `S_<enum>_<value>` over every schema whose entities, setting keys or action arguments carry the enum, in that order |
| Setting | the setting's label key |
| Value-set value | the value's literal label, else its label key, else the value |

`NamedEnums` names each C# enum once: `<schema>.<TypeName>` after its home schema, so
`gl.CenterType`. The home schema is the ordinally smallest schema among the entities and action
arguments declared in the enum's own assembly that carry it, else among every entity, setting key and
action argument that carries it, so the order features compose in never renames an enum. Its values
are labelled under every schema that carries it, the home schema's key first. The client metadata,
the setting descriptors and the labels all use that name.

A multilingual twin (`Name2`, `Name3`) reads its primary's label: alone when the tenant has one
content language, followed by the language's symbol in parentheses (`Name (ع)`) when it has more,
and the twin's humanized name beyond the tenant's shape. The key-resolving twins
(`PropertyLabelKey`, …) return the first key of the chain that English declares, a twin's being its
primary's, and null when none is.

`ConventionalStringKeys.Of(services)` lists every key a composed model derives by convention
(entities and their members, securables and the platform's actions, stacks, settings,
notifications, job handlers, calendars, value sets and the brand). A feature's resource audit takes
it as the derived keys and fails a key no convention derives and no source literal names
(`test/shared/Tellma.Testing.Resources`). Each family derives exactly the keys a lookup reads: a
collection has no bare key (its label falls back to its child entity's), an entity's securable reads
the entity's own labels, and the shared member labels Core ships (`Name`, `Code`, `Parent`, …) are
derived whichever entities a composition holds.

### The client entity model

`ClientEntityModel` (`IClientEntityModel`) describes every entity of the queryable model to the
client: its summary (name, route segment, label keys, operations, capabilities,
display-name rule, kind), its key, owner, properties, navigations, children, actions with their
argument fields and JSON Schema, related select, default order, searchable and natural keys, extras
and query limits, and every named enum: the C# enums with their wire names (a
`[JsonStringEnumMemberName]` member travels by it) and the value sets with their aliases. It is built
once from `QueryexSchemaProvider`'s full map, entities and enums in ordinal name order, leaving
out a shareable's share entity, which the client reads only through the `shares` extra. A label
resolves to the first key of its chain a neutral resource declares, else to the chain's first key
with a humanized `labelFallback`. A `[BlobReference]` property carries its kind's policy,
its size the kind's own `MaxSize` (a host whose `Tellma:Blobs:MaxUploadSize` is smaller does not
start). Read-only roots, which have no CLR type, are
server-owned with types from their store columns.

`EntityClientAssetContributor` writes it into the client metadata: the summaries as the Core chunk's
`entities`, the full metadata and the enums as the Entities chunk's `entities` and `enums`, and the
typed declarations as `tellma-entities.d.ts` (`EntityTypesWriter`): one wire interface
per entity (`<Schema><Entity>`, camelCase members, nullable members and twins optional), a literal
union per C# enum, and the `TmEntityTypes` and `TmEntityShapes` augmentations of `@tellma/core`. Two
declarations of one TypeScript name fail the emit.

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

## Version tags

A version tag is a `Guid` a tenant database keeps per cached input, in `core.VersionTags` (`Name`
`nvarchar(128)` key, `Tag`), and per user in `core.UserStamps` (`PermissionsTag`,
`PreferencesTag`). A cache keeps what it loaded with the tag it read, and a write moves the tag.

- **The registry.** `IVersionTagRegistry` (`VersionTagRegistry`, over the entity metadata) lists
  every tenant-level name: `entities`, `permissions` and `settings`, every name an entity declares
  with `[BumpsVersionTag]`, and `entity:<schema>.<Class>` for every `[Cacheable]` entity, in ordinal
  order. It resolves a batch's written tables to the names they bump (a cacheable entity's table
  bumps its own name and `entities`) and to the user-level rules (`[BumpsUserVersionTag]`, plus
  `core.UserPreferences` and `core.NotificationPreferences`, which bump each named user's
  preferences tag). `core.version-tags` reports what it refuses.
- **The prelude.** `VersionTagPreludeContributor` (`Order` 50, prologue) opens every batch that does
  not carry the connect prologue with `SELECT [Name], [Tag] FROM [core].[VersionTags];`; the
  connect prologue reads the same set through `VersionTagPrelude.ReadAsync` after the caller's
  stamp. The batch exposes what it read as `ReadVersionTags`.
- **The bump.** `VersionTagBumpContributor` (`Order` 50, epilogue) moves every name the batch's
  written tables resolve to, plus every `BumpVersionTag` call, to one new tag bound from C#
  (`@tm_tag`), refuses a missing row (`THROW 50422`, `VersionTag.Missing`), then moves the stamps
  of the users each rule's source names (the ids a statement declared, or the caller for
  `SqlOptions.ForCaller`, refused unless the batch carries a user's connect prologue) and returns
  the users it moved. The plan is fixed when the epilogue stage starts from the writes of every
  statement but the connect prologue's own (`DataBatch.BeginConnectPrologue`: its bookkeeping
  moves no tag). Refused: another prologue statement that writes a table with a tag; an epilogue
  write the plan does not cover; a write with a tag appended after the bump; an autocommit
  (`None`) batch whose writes bump a tag; and a dependency on a name the registry does not hold
  exactly. Re-running the statement moves the same rows to the same tag. On commit it publishes `cache.changed` to the tenant for each tenant-level
  name and to the affected users for each user-level one.
- **The snapshots.** `IVersionTagSnapshots` and `IUserVersionTagSnapshots` keep, per tenant (and
  per user), the last tags a batch read or wrote, bounded by `Tellma:Cache`. A batch's read
  replaces the tenant's snapshot; a bump overlays its new tags without a read-back.
- **The policies.** A dependency's `VersionTagMismatchPolicy` follows its batch's purpose: a read
  refreshes the cache entry it read under a moved tag (`Refresh`) and re-runs on a moved
  `permissions` tag (`Rerun`); validation and persist batches re-run (`Rerun`). A read or
  validation batch whose prelude disagrees with a `Rerun` dependency throws
  `StaleContextException` for the caller to recompose once; a persist batch's guard refuses the
  write instead (`THROW 50412`, `BatchAssertionFailedException`).
- **The seed.** `ITenantVersionTagSeeder` inserts a row for every registered name that a tenant
  database lacks, on every migrator run, and moves every tag after a run that applied a migration
  or ran a step, and on the way out of a run that failed after doing either (bounded, so a held
  tag row delays the failure by seconds). The wholesale move takes one table lock, so it forms no
  cycle with a live persist that holds some tag rows.
- **The wire.** `VersionTagWire` formats the four client tags (`settings`, `permissions`,
  `preferences`, `entities`) for the response header, under the format version of the shape each
  covers; the client's `permissions` tag joins the tenant's and the user's own.

`VersionTagMetrics` meters `tellma.versiontags.bumps` (by tag kind), `tellma.versiontags.stale` (by
phase) and `tellma.versiontags.missing` (by tag kind), and logs a missing row once per process.

## The versioned cache

`VersionedCache<TKey, TValue>` is the process-wide store of one cache kind (`CacheKinds`:
`settings`, `preferences`, `permissions`, `entities`): a private `MemoryCache` bounded in the kind's
own unit (`SizeOf`, one per entry unless the kind counts otherwise), whose entries carry the version
tag they are valid for. Every key begins with the tenant id (`ITenantCacheKey`: `TenantCacheKey`,
`TenantUserCacheKey`, `CacheableEntityKey`). The store is a singleton and holds no scope; a scoped
façade passes it the current tag from the process's snapshot and the scope's `ITenantDatabase`.

- **Lookups.** `GetAsync` hits when the entry's tag equals the current tag; `VersionTag.None` never
  matches. Otherwise it loads through the kind's `LoadAsync` (one read batch on the caller's
  handle), stores the value under the tag that batch's prelude returned, and reports `Miss` or
  `Stale`; a load whose prelude found no tag is `Uncached` (served, not stored) and an oversized one
  is `Oversized` (no value). `Peek(key, tag)` and `Peek(key, isCurrent)` never load. `Set` stores a
  value under a tag (`aged` meters the replaced entry as `age`); `Remove` drops one.
- **Single flight.** Callers that miss one key under one current tag share one load, run on the
  handle and token of the caller that started it. A failed load is forgotten before its exception
  reaches anyone, so the next caller loads afresh. A load cancelled under its starter, by the
  starter's token or by any token its batch links (the starter's scope's `DataCancellation`), stores
  nothing, and each caller still waiting starts its own load; a waiting caller's own cancellation
  ends only its wait. The entities façade passes the scope's `DataCancellation` token.
- **Instruments.** On the `Tellma.Core` meter, shared by every store: `tellma.cache.requests`
  (`cache.kind`, `cache.outcome`), `tellma.cache.load.duration`, the observable gauges
  `tellma.cache.entries`, `tellma.cache.size` and `tellma.cache.limit` (one series per live store,
  so each kind has one store per meter), and `tellma.cache.evictions` (`cache.reason`: `capacity`,
  `replaced`, `removed`, `age`). No instrument carries a tenant, user, entity or key.

### Cacheable entities

`[Cacheable(MaxRows)]` marks a small reference list; `MaxRows = 0` resolves to
`Tellma:Cache:EntityMaxRowsDefault`. `CacheableEntityStore` (the `entities` kind, sized in rows
against `EntityRows`) keeps one `CachedEntitySet` per tenant, entity and multilingual shape: every
row, inactive ones included, in key order, with `ById`. The shape is part of the key because the
list's columns follow it and a language change moves no entity tag; a list read under an old shape
is never served under the new one and simply ages out. A caller may ask by the leaf class or a base
it replaces; a base reads one view of the same instances.

- **The probe.** One entity query selecting every scalar property the tenant's query schema
  declares (and the JSON columns), no filter, ordered by `Id`, taking `MaxRows + 1` rows, after the
  prelude. More rows than `MaxRows` are an overflow: the rows are discarded, nothing is cached, the
  result is `Oversized` with no value, and `CacheableEntityOverflow` is logged once per entity per
  process.
- **`ICacheableEntities`.** The scoped façade: `GetAsync<T>()` (a miss runs one read batch),
  `Peek<T>()`, and `GetWireTagsAsync()`, each cacheable entity's `entity:<Name>` tag from the
  snapshot in its wire form (`VersionTag.None`'s when absent).
- **`IDataBatch.FromCache<T>()`.** A hit completes at once and declares the list's tag as a
  dependency (`Refresh` on a read batch, `Rerun` on a validate or persist batch). A miss appends the
  probe to the consumer's batch, so a cold lookup costs no round trip of its own; its rows are
  stamped with the batch's prelude tag and stored when the batch's results complete, before the
  handle does, and no dependency is declared. On a writing persist batch the guard still locks the
  list's tag row to the commit without comparing it, so a concurrent write of the table waits for
  the batch. A batch that already writes the entity's table is refused. Storing the list is best
  effort: it runs after the batch committed, so a store already disposed (the host stopping) skips
  and logs it (`CacheableEntityNotStored`) rather than failing the write.

`core.cacheable-entities` reports a cacheable entity that is not a `TopLevelEntity<int>`, declares a
child collection, resolves to a row limit outside 1 to `EntityMaxRowsCeiling`, or has a `Read`
securable with a filter root. It also creates the store, so a store that cannot be created (another
live `entities` cache on the host's meter) fails the host at startup rather than at its first read.

## Tenant settings

Each tenant database holds its settings in two temporal tables, both bumping the `settings` tag:
`core.Settings`, one typed row (`CK_Settings_SingleRow`: `Id = 1`) with the tenant's id, its names
in up to three content languages, the languages (`CK_Settings_Languages`: a tertiary needs a
secondary, none repeats), one or two calendars (`CK_Settings_Calendars`), the IANA zone, and the
optional region and digits; and `core.SettingEntries`, one JSON value per declared key. The
migrations seed the row as a placeholder for tenant `0` (English, Gregorian, UTC); the
`core.settings` provisioning step (order 15) completes it with the tenant's id and name and the
distribution's first language, or the languages and calendars a seeded tenant lists, validated as
the settings editor validates them. A row the step already completed is left alone.

- **Keys.** A feature declares its keys as `public static readonly SettingKey<T>` fields of a holder
  type and contributes it with `contribution.SettingKeys(typeof(Holder))`. `ISettingKeyRegistry`
  lists them in contribution and declaration order, the tabs (`general` first, then each category
  as its first key appears), and the descriptors the client renders. `core.setting-keys` reports a
  field holding no key, a name that is not dotted camelCase of two segments or more (at most 128
  characters), an undefined visibility, a type admitted only with a serialization contract of its
  own, a default that contradicts the type's nullability, a secret with a default or a reference, a
  category or group outside the grammar, a reference that is not an int id of an int-keyed stack, a
  name declared twice, and a category spelled two ways.
- **The store.** `TenantSettingsStore` (the `settings` kind) keeps one `TenantSettings` per tenant,
  with its client view serialized once. A miss loads through one read batch whose contributor
  (`SettingsColdLoadContributor`, after the prelude) appends the row and the entries, stamped with
  the prelude's tag. `ITenantSettingsCache` is the scoped façade (`GetAsync`, `GetForClientAsync`,
  `Peek`).
- **The routing guard.** A row whose tenant id is not the routed tenant's fails closed
  (`TenantDatabaseMismatchException`, logged `Critical`). The placeholder is served only to the
  platform's own work and never cached, so a provisioned database is read again at once.
- **Resolution.** A stored value the composition cannot resolve falls back, logged `Critical`
  (`SettingsValueUnresolvable`): the primary language to English (else the first offered), a
  secondary or tertiary one ends the list, the primary calendar to Gregorian, a secondary one is
  dropped, a zone the host's time-zone data does not have (or the query engine's pinned table,
  `QueryexTimeZones.TryGetWindowsName`, does not map to the Windows name SQL Server's conversions
  take; never the host ICU's mapping) to UTC, digits not named by `DigitStyle` to none. The
  placeholder reads as the platform's defaults directly, with no event. An entry no feature declares is dropped;
  one that does not read as its key, or fails its check, is dropped, logged and counted
  (`tellma.settings.entries.invalid`).
- **Secrets.** A secret is stored as the JSON string of its value's JSON, protected by the purpose
  `Tellma.Core.Settings.Secret` and the tenant id, kept protected in memory, and unprotected on its
  first `Get<T>`. One that does not unprotect reads as its default, logged `Critical`. A secret is
  never in the client view, and its default is never in the client metadata.
- **Negotiation.** `LocaleNegotiationInitializer` (order 200, after the user connection) ensures the
  scope's settings and negotiates the locale from the request's inputs, the connected user's stored
  preferences (`IUserLocalePreferences`, when the composition registers one) and the settings; it
  completes the context's language, culture, calendar, display and tenant zones, today in the
  tenant's zone, content-language position and settings, and sets no ambient culture. The language
  and culture are the negotiated ones (`LocalizationContext.Language`, `CultureTag`), never read back
  from the culture's name, and today is the date of the context's `Now` in the tenant's zone, so it
  never disagrees with `Now`.
- **Client metadata.** The core chunk carries `languages` (each offered language with its direction,
  default region, regions and native digits), `calendars`, `settingKeys` and `settingCategories`.
- **Securables.** `SettingsSecurableContributor` registers `core.Settings.General` (`Read`, and a
  sensitive `Save`) and `core.Settings.<category>` (`Read`, `Save`) for every other category.
- **The service.** `SettingsService` (route `settings`) serves five actions through the guarded
  runner:
  - `client` and `entity-tags` (any member): the client view's bytes and wire tag, and the
    `entity:*` tags, after a read batch that keeps the snapshots current.
  - `details` (any member holding `Read` on the typed row or a category, else forbidden): one read
    batch with the typed row when held, the values of the held categories' non-secret keys (a
    stored value only when it reads, else the default), the set keys and their stamps, and the
    rows their reference keys point at (an id whose row is gone is left bare).
  - `save` (`Save` on the resource of every listed field): a `TenantSettingsPatch` lists its
    fields, typed members by name and entries as `entries.<key>`. One validate batch reads what
    the patch lists (the typed row only when a typed member is listed); `SettingsPatchMerger`
    applies the patch and reports every refusal at once, then the stamps under `Check` (a
    conflict names who changed the row; a missing entry is a conflict of its own, id 0 when the
    row was gone before the read). One persist batch writes the row, the entries (a reset is a
    delete, an unchanged value is skipped) and the closing reads, and a language change also
    bumps `permissions` and every `entity:*` tag. A concurrent first write of a key is a conflict,
    not a unique violation. The answer carries the editor's view, the client view and the settings
    wire tag; the store is offered the committed settings. A name change is mirrored into the
    tenant catalog after commit; a failing mirror is logged (`SettingsNamesMirrorFailed`) and the
    save stands.
  - `refresh-caches` (`Save` on the typed row): one persist batch bumps every tenant tag and both
    user tags of every user, and announces the preferences change.

`core.CalendarMonths` holds every Umm al-Qura month of AH 1318 to 1500 as .NET computes it, keyed
by calendar, year and month, with the month's first Gregorian day and the day after its last; the
query language seeks it by calendar and first day. Only the migrations write it.

## Notifications and client events

The catalogue is composed per distribution, never per tenant. A feature registers each notification
type its code raises with `contribution.NotificationType(descriptor)`, each channel it ships a
sender for with `contribution.NotificationChannel(descriptor)` and each client event it publishes
with `contribution.ClientEvent(name)`. `NotificationTypeRegistry`, `NotificationChannelRegistry` and
`ClientEventRegistry` list them in realisation order, `CoreFeature`'s first: the type
`core.user.added`, the channels `inbox` (on by default) and `email` (off by default), and the events
`cache.changed`, `inbox.changed` and `session.ended`. `NotificationsClientAssetContributor` writes the three
registries into the Core chunk of the client metadata as the sections `notificationTypes`,
`notificationChannels` and `clientEvents`, with the platform's JSON rules.

| Check | Fails when | Needs infrastructure |
|---|---|---|
| `core.notification-types` | a key is not two or more dot-separated lower-case segments of at most 64 characters; a category is not one such segment of at most 32; a key is registered twice; a severity is undefined; a channel-only type (`Inbox = false`) is mutable or a toast; a target is neither a stack resource (an entity a feature contributed with `Entity<T>()`, named by its leaf's metadata) nor a registered securable resource, both matched as spelled, or names a stack resource whose key is neither `int` nor `long` | no |
| `core.notification-channels` | a key is not one lower-case segment of at most 16 characters; a key is registered twice; `inbox` or `email` is missing; an `INotificationChannel` sender cannot be created in the gate's tenantless scope, names an unregistered channel or `inbox`, or shares its channel with another sender | no |
| `core.client-events` | a name breaks the type-key grammar, or is registered twice | no |

`NotificationsOptions` binds from `Tellma:Notifications` and is validated by `core.options`.
`IClientEventPublisher` is registered, with `TryAdd`, as `NullClientEventPublisher`, and
`IRealtimeStatus` as a status reporting `false`, so a host without the hub composes and runs; the web
package replaces both. Every publisher derives from `ClientEventPublisher`, which refuses an
unregistered name, a payload over 4,096 UTF-8 bytes and a batch that already executed before
anything is registered or sent,
coalesces the events published on one batch into its one commit callback (each distinct name, set of
users and payload once), and logs a failed delivery as `ClientEventFailed` without throwing it.

### Raising, withdrawing and rendering

Core maps the inbox to `core.Notifications` (ids from `core.sq_Notifications`, lock escalation
disabled, an index per read path: a user's rows newest first, the unread count, the duplicate
lookup and the retention sweep) and the preferences to `core.NotificationPreferences` (one row per
user, type and channel that differs from the channel's default). `Notification` is a read-only
stack.

`Notifier` (scoped `INotifier`) checks and shapes every request in C# before it appends anything,
refusing a rule broken with `InvalidOperationException`, then appends one statement per request to
the caller's persist batch. The statement reserves one id range for the request's distinct
recipients and inserts a row for each one who is active, has not muted a mutable type, and holds no
duplicate (an unread row of the key, or any row under `Ongoing`); under `Replace` it first refreshes
each unread duplicate in place. It then enqueues one `core.notification-delivery` job per slice of
`DeliverySliceSize` recipients in which an active recipient receives a channel other than the inbox,
by the user's preference or the channel's default. The delivering channels are the registered ones
an `INotificationChannel` sender serves, resolved once by `NotificationDeliveryChannels`; with none,
or for an `InboxOnly` request, nothing is enqueued. A statement that enqueues refuses to run outside
a transaction, and the notifier refuses a batch in `None` mode. A withdrawal deletes the rows of a key,
for everyone or for named recipients. After the commit, one callback per batch counts what each
statement did (`tellma.notifications.created`, `suppressed`, `replaced`, `withdrawn` and
`delivery_enqueued`, by type), wakes the tenant's pollers when a delivery job was enqueued, and
publishes one `inbox.changed` to the users whose inbox changed, logging a failure as
`InboxEventFailed` without throwing it.

`NotificationRenderer` (scoped `INotificationRenderer`) renders a row's text server-side: the
resource `Notification_<type key, dots as underscores>` of the package whose feature registered the
type, formatted as an ICU message in the reader's language, calendar and zone, then the
`…_Detail` line on a new line when the neutral resources declare one. Each top-level argument
formats by its JSON kind: an ISO 8601 date-time with an offset as an instant, an ISO 8601 date as a
calendar date, numbers as numbers, booleans as `true`/`false`, null as nothing, a localized text in
the reader's content slot, and anything else as its JSON text. A missing resource renders the type
key; arguments that are not a JSON object render the message without them and log
`NotificationArgumentsInvalid` once per scope.

### The inbox, preferences, delivery and retention

`InboxService` (route `inbox`) serves the calling user's own inbox; every action is member-only,
hidden from MCP, and judged by the statements' own predicates on the caller, so another user's id
is ignored, never refused. `summary` reads, in one Read batch, the unread and unseen counts (capped
at `SummaryCountCap`), the highest unseen severity, the inbox version, the latest
`SummaryLatestCount` rows newest first (ties by id), the caller's running jobs with their labels, and
the actors as related users projected to their related select. `seen`, `read`, `read-all`, `clear`
and `clear-read` each run one Persist batch that bumps the caller's inbox version and publishes
`inbox.changed` to the caller after the commit; they return the rows touched and the new version.
`read` and `clear` take at most the notifications' `MaxIds` ids, dropping repeats and ids outside the
key's range. The verbs run in a read-only tenant and are refused in a frozen one.
`NotificationAccessCriteria` scopes the read of `Notification` to `UserId = me()`.

`NotificationPreferencesService` (route `notification-preferences`) reads the caller's stored
deviations (`get`, dropping types and channels no longer registered) and replaces them (`save`):
`NotificationPreferenceRules` refuses an unknown type or channel, a repeated pair and the inbox
turned off for a type that cannot be muted, with the codes in `NotificationValidationCodes`, and
drops every row equal to its channel's default. The save bumps the caller's preferences tag and is
admitted in a read-only tenant.

`NotificationDeliveryHandler` runs `core.notification-delivery` (four slices a claim, twelve
attempts, failure notices to the inbox alone, run in a read-only tenant). It reads every slice's
recipients and their stored preferences in one round trip, negotiates each active recipient's
localization once from their preferences and the tenant's settings, and hands each delivering
channel, in registration order, the recipients who receive it; then it persists the enlisted writes
once. A channel that throws fails the claim, and the job retries.

`NotificationRetentionHandler` runs `core.notification-retention`, a built-in schedule (id 2) at
03:30 every day. It deletes read rows older than `ReadRetention`, then unread rows older than
`UnreadRetention`, `RetentionPageSize` rows per maintenance batch, counting each page as
`tellma.notifications.retained` by band and reporting the totals as progress.

## The email outbox

`IEmailOutbox.EnqueueAsync(host, requests)` writes mail in the transaction of the change that
produced it: one `core.OutgoingEmails` row per request, enlisted with the caller's frame, so the
email and the change commit or roll back together. The call first resolves every blob attachment
and validates every request, reporting all refusals at once: `Email.InvalidMessage` at `[i]` for a
malformed message (no primary recipient, an address that could break a header, a subject that is
blank, on more than one line or longer than its column, a blank text body, more attachments than a
row holds, a regarding resource other than a dotted name), else `Email.TooManyRecipients` past
`MaxRecipients` and `Email.TooLarge` past `MaxMessageBytes` (the UTF-8 bodies, the message's files
and the resolved blobs' stored sizes); a blob reference that does not resolve, or a staged blob of
another kind, is `Blob.NotAttachable` at `[i].BlobAttachments[j]`. Then it stages every file as an
`email-attachment` blob, one round trip and one store write each: the message's own files, a staged
email attachment as it is, and a committed blob as a copy, so each email owns its files alone. The
kind keeps no thumbnail. The rows are `Pending`, their addresses stored as RFC 5322 lists
(`"Name" <address>`, joined by `, `). `OutgoingEmailService` enqueues each new row's
`core.outgoing-email` job in the same persist, run as the system user with no requester and due
at the row's `SendAfter`; the rows' blobs commit with it. On a rollback only the staged blobs
remain, and their staging deadline expires them. The blob service and store are resolved only for
a call with attachments, so a channel sender's tenantless gate scope needs neither.

`OutboxOptions` (`Tellma:Outbox`) holds `Retention` (180 days, one day to ten years),
`MaxMessageBytes` (7 MiB, 1 KiB to 20 MiB) and `MaxRecipients` (50, 1 to 1000). The outgoing
emails' stack (route segment `outgoing-emails`) serves the query, the details, the delete by ids
and two actions, read-only to MCP, under the securables `core.OutgoingEmail` × `Read`, `Delete`,
`Retry` and `Cancel`; it serves no delete by query, since a delete is validated row by row. A delete
of a row that may still be sent (unsettled, with a live job) is refused as `Email.NotDeletable` at
`Ids[i]`. `retry` sends again each row that failed, or that is unsettled with no live job: it
returns the row to `Pending` with its send and delivery columns cleared and enqueues a new sending
job, refusing any other row as `Email.NotRetryable`. `cancel` settles each `Pending` row
`Cancelled`, in a read-only tenant too, and refuses any other as `Email.NotCancellable`; its job then
meets a settled row and succeeds unsent. Both stamp the rows they change. `cancel` repeats its
precondition in the update, so a row the sending job reached since the validation is left as it
is, unstamped. `retry` holds every target to its precondition again in the persist, so a row
another administrator cancelled since the validation refuses the whole retry as
`Email.NotRetryable` and no job is enqueued; it stamps every target, one whose columns already held
their cleared values included. A request's `Source` is `api` or `notification`; any
other is refused before anything is staged. The rows are counted on `tellma.outbox.enqueued`
(meter `Tellma.Core`, tag `source`) once the frame they were enlisted with commits them, by the
stack's commit effect (`OutboxEnqueuedEffect`); a frame that rolls back counts nothing.

`OutgoingEmailHandler` runs `core.outgoing-email` (25 jobs a claim, one claim at a time per tenant,
twelve attempts backing off from one minute to two hours, run in a read-only tenant, failure notices
to the inbox alone). Each claim:

1. **Triages** the rows as the claim loaded them: a settled row succeeds unsent; a row still
   `Sending` was interrupted after it may have reached the provider, so it settles `Failed` with
   `Email.Interrupted` (logged as `OutboxSendInterrupted`) rather than being sent twice; a pending
   row whose job is being cancelled is left alone, so its job ends cancelled.
2. **Loads** the pending rows' attachments in one read round trip joined to their blobs, then reads
   each object whole from the store; a row whose object is missing is unsendable
   (`Email.AttachmentMissing`), as is one whose stored addresses no longer parse
   (`Email.InvalidMessage`).
3. **Pre-marks** the pending rows `Sending` and counts the attempt, in one maintenance round trip
   that returns the rows now sending, so a re-run returns the same rows. A claim whose host is
   stopping or whose lease was lost stops before it, every row still pending.
4. **Sends** the sendable rows in one call to the email sender with no cancellation, each message
   correlated as `(outbox, rowId, tenantId)`. A sender that throws sent nothing, so every message
   failed transiently (logged as `OutboxSendFailed` with the exception's type alone); a sender that
   answers another number of results than messages fails the claim with the rows still sending, so
   the next claim fails them rather than send any twice.
5. **Settles** every verdict in one maintenance round trip, matched on `State = 'Sending'`, before
   any job is marked: `Sent` keeps the provider's id and whether delivery events follow; `Sandboxed`
   keeps neither; `Rejected` keeps the provider's detail; a transient failure returns the row to
   `Pending` and retries its job with `Email.TransientFailure` while attempts remain, and fails it
   with `Email.AttemptsExhausted` on the twelfth. Every settled row is counted on
   `tellma.outbox.settled` (tag `state`), and each sent or sandboxed row's wait from its enqueue to
   its send, on the database clock, on `tellma.outbox.latency` (seconds).
6. **Notifies** the tenant's administrators once when any row settled rejected or failed:
   `core.email.failed` (an error, toasted, `{count}` emails, on the first such row), inbox-only and
   deduplicated on its type so an unread notice is replaced. A failed administrator lookup is logged
   as `OutboxNoticeRecipientsFailed` and loses only the notice.

An exception anywhere before the settle leaves every job unmarked, so the job machinery retries the
claim and the next triage meets the rows as this one left them. The startup check `outbox.sender`
reports a host whose job worker runs with no email sender registered (the handler resolves the
sender on each claim, so such a host still builds, and a claim it runs fails before it writes
anything, every row still pending); the queries in
`infra/monitoring/outbox` alert on interrupted sends, the failure rate, the latency's 95th
percentile and unmatched delivery events.

`OutgoingEmailDeliveryHandler` answers the `outbox` owner key's delivery events on the email
pipeline's tenantless webhook path. Opens, clicks and unclassified events are dropped; of the rest,
each row keeps only its strongest event (the higher status, then the later time). Each tenant is
looked up in the registry: a missing, provisioning or retired tenant's events are dropped
(`OutboxDeliveryTenantSkipped`); an active, read-only or suspended tenant's are applied; a frozen
tenant's wait for the provider to deliver them again, the call failing once every other tenant's
were applied, as does a tenant whose update failed. The update is one idempotent maintenance round
trip on a tenant database handle outside any tenant scope: a row moves only to a stronger status, or
to a later event of the same one, and an event already applied is skipped, whatever the row's
state. An event naming no row is logged as `OutboxDeliveryEventUnmatched`. Every event is counted
on `tellma.outbox.delivery_updates` by `result`: `applied`, `stale` (a repeat, no stronger, or
replaced by a stronger event of the same row), `unmatched` (each event of a row that does not
exist, or that names no row) or `tenant_skipped`.

`OutgoingEmailRetentionHandler` runs `core.outgoing-email-retention` nightly at 03:45 on built-in
schedule 7, as the system user and in a read-only tenant too. Each page reads up to 500 ids of rows
created longer ago than `Retention`, on the database clock, that are settled, or unsettled with no
live job, and deletes them through the stack's delete by ids, so the delete's own rules run and the
attachments' blobs are released to the sweep; a page whose delete is refused because a row changed
in between is read again once. The pages run until one is short, or until a full page deletes
nothing, which would otherwise be read again for ever. The rows deleted are counted on
`tellma.outbox.retained`.

`EmailNotificationChannel` is the `email` channel's sender, which `CoreFeature` registers (the
inbox has none: the notifier writes its rows), so email is a delivering channel in every
distribution; it resolves `IEmailSender` only when a test is sent, so the gate creates it in its
tenantless scope. Each addressed recipient gets one
internal email, to their contact address or else their sign-in address (a recipient with neither
is skipped), regarding the notification's target. Recipients sharing a localization share one
rendering of the notification's text; the subject (`Email_Notification_Subject`, the text's title
line, cut to its column), the text body and the HTML body (`Email_Notification_Text`,
`Email_Notification_Html`, the arguments HTML-encoded) are Core's server resources, given the
tenant's name in the recipient's content language, the text and the link to the target. All of
them are enqueued in one call with the delivery job's frame, so they commit with the job's
completion and none is written when the job fails.

Its `SendTestAsync` sends the caller one internal email straight through `IEmailSender`, bypassing
the outbox: to their contact address, else their sign-in address, rendered in their preferred
language and calendar but in the tenant's time zone (`Email_TestNotification_Subject` and
`Email_TestNotification_Text`, given the tenant's name and the time sent). A caller with no address,
or one the outbox would refuse, answers `NoContactAddress`; a host with no email sender answers
`NotConfigured`; a sent or sandboxed message answers `Sent`. A transport that throws, rejects the
message or fails transiently is raised as the `email` dependency unavailable, logged as
`TestEmailFailed` or `TestEmailNotSent` with the transport's detail (the channel adds no address
of its own, but the detail may quote one).

## Entity stacks

A feature stacks an entity with `Entity<TEntity>()` (the platform's default service) or
`Entity<TEntity, TService>()`, where the service derives from `EntityService<TEntity, TKey>` and may be
generic over the entity's leaf. It attaches a stack companion (`EntityCompanion<TEntity, TCompanion>()`,
whose `[ApiAction]` methods the stack serves), validators, persist effects, details contributors and
named filter contributors (`Validator`, `PersistEffect`, `DetailsContributor`, `NamedFilters`, each
closed or open over the leaf), a routed API service (`ApiService<TService>()`, which carries
`[ApiRoute]`) and confirmation keys (`Confirmation(key)`). An entity has one stack: a feature that
requires the stack's registrant may register it again, replacing its service and operations, and the
stack keeps its registrant's feature.

`CrudComposition` registers, per stack, the service closed over the leaf (scoped, and as
`EntityService<TLeaf, TKey>`), its companions, and its components under their contracts (a
component generic over a base closes over the substituted leaf); then `StackRegistry`, the singleton
`IStackRegistry`, built once on first resolution from the composition and the tenant model with no
tenant scope:

- **Descriptors.** A stack's operations are its `Entity<…>` item's, else its `[Stack]`'s, else
  every operation. Its capabilities, properties, children, searchable properties, natural keys and
  display name are read from the entity's metadata; its default select, related select and details
  expansion from the projection attributes, else derived (the key, natural keys, name, code, active
  flag and audit columns; the key, display-name columns and image; every reference, and every
  child's reference at any depth). Its actions are the built-in `activate` and `deactivate` of an
  activatable entity it saves, then the service's `[EntityAction]` and `[ApiAction]` methods, base class
  first, then each companion's `[ApiAction]` methods. Its extras are the service's and details
  contributors' `[DetailsExtras]`, then `permissions` when the stack serves details and writes
  anything. Its named filters are read once from contributors created from the root provider.
- **Child query surfaces.** A stack with `Query` exposes each child collection at every depth as a
  read-only query surface under the child's resource and segment, owned by its parent's resource
  through the child's parent-key navigation, unless the child's `[ApiResource(Queryable = false)]`
  opts it, and its subtree, out. Their actions stay empty until the child-action seam is defined.
- **Routed services and enlistments.** Each routed service's `[ApiAction]` methods are its actions,
  each secured by a declared `Resource` and `Action`, or member-only; `Enlistments` is every declared
  `IEnlists<TTarget>` edge between stacks, from any registered writer.
- **Ownership.** The registry owns every action descriptor and the method behind it, matched by
  reference; the method record says whether its last parameter is a `CancellationToken`, which
  the invoker binds to the request's token. `OwnerOf` maps a table, root or child, to its stack.

`StackSecurableContributor` registers each stack's securables: `Read` (filter root the resource,
or none for a cacheable entity) when it queries, serves details or exports; `Save` when it saves or
imports; `Delete`; each entity action's securable (filter root the resource when the action takes a
filter); each `[ApiAction]`'s; each routed service's (no filter root, its feature's); and each child
query surface's `Read`, owned by its parent through the parent-key navigation, in the root stack's
feature.

| Check | Fails when | Needs infrastructure |
|---|---|---|
| `core.stacks` | two features register a stack over one entity and neither requires the other, or one feature registers it twice differently; a stack is over a child entity, an abstract or keyless class, or an entity the tenant model does not map; its service does not derive from `EntityService<TLeaf, TKey>` or cannot close over the leaf; its operations include `Save` without `Details`, `Import` without `Query`, `Details` and `Save`, or `Export` without `Query`; a `[Stack]` ceiling is not positive (naming the stack and the ceiling); a resource segment, a child surface's or a routed service's first route segment, is not kebab-case or is taken twice, ignoring case; a child has no navigation through its parent key; an `EntityService` registered as a service, or one serving an entity whose stack runs the default service, that no stack runs | no |
| `core.stack-actions` | an action's name is not kebab-case segments joined by `/`, is taken twice in a stack, or takes a segment the stack serves itself (`query`, `get`, `save`, `delete`, `export`, `import` and the rest its operations and capabilities give it); a method is not a public, non-generic instance method, has an unsupported signature or result, or carries both attributes; a single-target action returns nothing; an `[EntityAction]` is declared on a companion or a routed service; a companion has no `[ApiAction]`; an action of a stack or companion names another resource; a member-only action names a securable; a routed service has no `[ApiRoute]`, a malformed route, or an action with neither a `Resource` nor `MemberOnly`; an `[EntityRef]` argument names no stack with `Query` | no |
| `core.stack-components` | a companion or component is attached to an entity no stack owns, or, not generic, to an entity only derived entities' stacks own; it cannot close over the stack's leaf, or does not implement its contract once closed; a service or details contributor declares an extra twice across its class and base classes, or declares `permissions`, which the platform produces; a named filter contributor cannot be created or read at startup, names a filter outside `^[a-z][a-z0-9-]*$`, gives one no tree, or shares a name with another | no |
| `core.stack-projections` | a stack's or child surface's default select, related select or default ordering, or a named filter's text, does not validate against the query schema; a details expansion names a segment that is neither a navigation nor a child collection; a named filter goes via a non-navigation, or tests the existence of an entity through a navigation that does not lead back to the filtered one | no |
| `core.confirmations` | a confirmation key is not dotted PascalCase, a feature declares it twice, two features declare it, or no neutral shared `Strings` resource of a composed feature declares it | no |
| `core.entity-pipeline` | `IEntityPipelineFactory` or an `IEntityPipeline<,>` is registered with a type, an instance or a factory that is not the platform's pipeline | no |
| `core.enlistments` | a writer's `IEnlists<TTarget>` names an entity no stack serves, or a stack whose operations lack `Enlist`; the declared enlistments between stacks form a cycle (a writer enlisting its own stack does not count) | no |
| `core.stack-securables` | a securable a stack, an action, a routed service or a child surface requires is not registered: a stack's standard operations are read from the segments it projects, apart from the stacks' securable contributor, so a gap in either fails (judged only when the securable registry builds) | no |

### The entity pipeline and the query path

`EntityPipelineFactory` (scoped `IEntityPipelineFactory`) creates the sealed
`EntityPipeline<TEntity, TKey>` behind each `EntityService`, over the stack whose leaf is the
entity. Each operation runs in the frame of `CrudOperations`: the scope's first operation names its
`DataAccessScope.Operation` `<Resource>:<operation>`, and every operation is counted
(`tellma.crud.operations`) and timed (`tellma.crud.operation.duration`, with its `crud.lane`) under
the outcome it ended with, a cancellation by the caller as `cancelled`; a read carries no
`crud.source`. The reads (the query, the details reads, the rows of a tree by parent and every
row of a cacheable stack), the save (see [The save pipeline](#the-save-pipeline)), and the deletes
and actions (see [The delete and action pipelines](#the-delete-and-action-pipelines)) are built, and
so are the Excel row source and the import's lookups (see
[The Excel row source](#the-excel-row-source)): an operation the stack does not serve is an
authoring bug (`InvalidOperationException`).

`QueryOperation` runs a query of a stack, and of a child query surface through `IChildQueryService`
(the child's own `Read` securable, the convention's search, no named filters):

1. **The type-level check**, `Read` on the surface's resource, before any batch.
2. **The admission** (`QueryShape`, no I/O): `Take` 50 by default, up to the stack's `MaxTake`; the
   skip window; the search's length once its bidi controls are stripped (ZWJ and ZWNJ kept); the
   clauses the caller sent; the named filters resolved in request order, each unknown name one
   `Query.UnknownNamedFilter` at `namedFilters[i]`; the ancestors kept for a page of a tree only.
3. **One guarded read** whose composition decides the root under the connected user's set and
   plans the query (`QueryPlanner`). The filter conjoins the caller's filters, the named filters,
   `IsActive = true` for an activatable root unless `IncludeInactive`, the search and the access
   filter, an empty conjunct dropped. The select defaults to the surface's (the multilingual twins
   the tenant's languages leave out dropped); the ordering to `DefaultOrderBy`, else `Id desc`, and
   an aggregate's to its select. The query compiles under `QueryexProfiles.PipelineLimits` (the
   batch is held to the same), and any diagnostic refuses it as `InvalidQueryException` located in
   the caller's clauses: `filters[i]` and `namedFilters[i]` by the request's indexes, `Select`,
   `OrderBy`, an aggregate's ordering at the select it was taken from. A ceiling crossed in the
   platform's own text is reported at no span: in the platform's filter conjuncts at the last
   filter the request sent, else its last named filter, else `Select`; in a default select or
   ordering at that clause. Any other diagnostic in the platform's text (a default, a search branch,
   an access filter) is a defect, `InvalidOperationException`. Then the traversal rule
   (`ForbiddenException` with `RestrictedPath`, the entity and the dotted path, or the step-up of a
   sensitive entity a path reads beyond its projection), the select-type rule (a geography column,
   `Query.GeographyNotSelectable` at `Select[i]`), and the one row read with its count capped at
   `CountCap` and its ancestors under the access filter alone.
4. **The re-check**: a path refused by a denial or a filter is re-checked once through
   `AccessEvaluator.RecheckAsync`. A restriction from a set older than `FastDenyWindow` costs one
   prologue-only round trip, at most once per scope, because a refusal raised in the composition
   reaches no batch whose prologue would compare the set's tags. A grant the re-check finds composes
   the read once more under the refreshed set.
5. **The data layer's failures**, wherever they arise (the connect, a re-check or the read): a
   division by zero or an overflow in the caller's expressions is a `ValidationException` with
   `Query.ArithmeticFailed` at the root, naming the reason; retries exhausted on deadlocks or
   transient failures are `DependencyUnavailableException` naming `database` with a one-second
   `RetryAfter`, counted as `unavailable`.

On warm caches the page, its count and its ancestors are one round trip. The arguments are bound
from what the caller's own clauses declare (`QueryDiscoveries`, cached per tenant schema and clause
texts and timed by `tellma.crud.query.discover.duration`): each JSON value converted to its
parameter's inferred type by the wire's exact grammars (`QueryArgumentValues`), a JSON null or a
missing value bound to null, a value no clause mentions ignored, and any other form a
`BadRequestException` naming the parameter; while a search applies, a caller's parameter named as
any the search can bind (`search`, `searchId`, `left`, `right`) is refused, whichever the search
text binds.

`QueryOperation` also reads the rows of a tree by parent (`get_by_parent_ids`): the type-level
check, then more than `MaxIds` parents refused before any round trip, then one guarded read with the
query's admission and planning. Without parents it reads the roots (a `ParentId = null` conjunct);
with parents, the rows whose parent key the distinct parents restrict; and with `IncludeRoots` too,
the rows whose key a batch-local key table holds, filled from both seeks in the same batch. It reads
`MaxTake + 1` rows from row 0 in the declared ordering, else `Id`, with no count and no ancestors,
and a level of more than `MaxTake` rows is `LimitExceededException` (`MaxTake`). An arithmetic
failure and exhausted retries translate as the query's do.

`DetailsOperation` serves `get`, `get_by_ids` and `all`. A read by ids runs the type-level check,
then refuses more than `MaxIds` ids, more than `MaxExtras` distinct extras, or an extra the stack
does not serve (`BadRequestException` naming it), before any round trip; then `DetailsPlanner`
composes one guarded round trip:

1. **The main ids**: the stack's key under the caller's `Read` filter, restricted to the requested
   ids and captured into a key table. Row-level security decides visibility here and nowhere else,
   so a hidden row reads as a missing one: `get` throws `NotFoundException`, and `get_by_ids` returns
   the visible rows, a repeated id once, in request order.
2. **The rows**: every mapped property, every child collection at any depth, and the related
   projection of each navigation the stack's details expansion follows from the root (up to
   `MaxExpandDepth` navigations), restricted to the captured ids. What a collection's rows point at
   is read by one grouped read per collection level and leading navigation, keyed back to the
   visible ids, so each target is read once however many rows point at it, and merged into the
   related entities, each entity once (`DetailsProjections`, cached per stack and schema).
3. **The extras**: the service's `ContributeDetails` when it declares a requested extra, then each
   `IDetailsContributor<T>` given the requested extras it declares. A class that leaves a requested
   extra absent on purpose (its caller may not read what the extra shows) omits it with
   `DetailsPlan.OmitExtra`, and decides that with `DetailsPlan.Decide`, which answers the caller's
   decision over the same permission set the read is composed under. An extra added or omitted that
   its class does not declare, that the request does not include, or twice, raw text that declares
   a write or no result set, and a requested extra its declaring class neither added nor omitted are
   authoring bugs (`InvalidOperationException`). Each extra is returned under its name in request
   order, a computed one serialised under `TellmaJson.Options` after the batch over the visible rows.
4. **The `permissions` extra**: per visible row, the granted abilities it refuses (`save`, `delete`,
   `activate`, and `action:<name>` per entity action). Each filtered grant adds one keyed read of the
   visible ids under its filter; a row it misses refuses the ability as `outside-filter`, and the
   service's `RefuseOnRecord` judges the rest after the batch. An ability the caller is not granted
   is not listed.

The steps after the round trip run in the cultures of the context the batch was composed under.
Exhausted retries are `DependencyUnavailableException` as the query's are, while an arithmetic
failure in a details read passes through as the fault it is: no caller's Queryex text is involved.
`all` reads a cacheable stack's list from `ICacheableEntities` with no round trip when it is warm,
each row a shallow copy of the cached one in a list of the call's own, so a caller's edits never
reach the rows the tenant's other readers are served; requested extras ride one details round trip
over the cached ids, and a table beyond the cache's row limit is read whole, in key order, on every
call.

The search (`QuerySearch`) is one disjunction over declared parameters, so its filter takes a few
constant shapes per root: each searchable column by its kind (`contains` or `startsWith`), or the
service's `SearchFilter` branch in their place; the id, from an integer or `#` and an integer the key
can hold; the display name's source over every slot the tenant's languages admit, an integer source
matched by equality in Latin and Arabic-Indic digits; its prefix; and the `prefix - source` split at
the first separator. A search no branch applies to matches no row.

`QueryTraversalRule` (public as `IQueryTraversalRule`) checks the paths of the caller's own clauses,
never the trusted trees beside them: a path into an entity other than the root reads all of it only
under an unrestricted `Read` decision (its resource or the wildcard), and otherwise only its related
projection. From a child surface's root without a grant of its own, the owner reached through the
owner navigation counts as the root. A `descendantOf` or `ancestorOf` whose key path leaves the root
reads the reached entity's tree position, which discovery reports as a path of its own, so the
position is held to the same rule (refused as `<navigations>.Node`). When no path is refused, the
rule itself asks for the step-up of each sensitive entity a path reads beyond its projection, as a
direct `Read` of it asks (`StepUpRequiredException`, or `IndividualRequiredException` for a service
account), never for the platform's own work, so no consumer of the public contract can skip it.
Its `Refusals` walks the same paths without the step-up, for an authoring tool: a step-up is the
session's to satisfy, not the text's to fix.

`QueryWireDiagnostics` carries a located diagnostic, or a located discovery site, to the wire's
`QueryDiagnostic` and `QueryexSite`: the request clause (`select`, `filters[i]`, `namedFilters[i]`,
`orderBy`, `having`, `measures`) and the item within it, a list clause's item as the engine names
it (`Select[2]`), a filter's path below its root (`Or[1]`), and nothing for a single expression. A
location the engine gave no clause, such as a ceiling the whole query exceeds, is placed where a
ceiling in the platform's text is placed. It is the one mapping to the wire: the web edge's
`query-invalid` problem carries an `InvalidQueryException`'s diagnostics through its map-less
`ToWire`, whose raise site already located them (one located nowhere is placed at the select).

### The validation stage

The parts a write's validation stage is assembled from, internal to the assembly until the write
pipeline drives them:

- **Context loader.** `ContextLoader` is `IContextLoader` with DataLoader semantics: a request is
  deduplicated by its structural key and handed back as a `ContextRef` that fills when its round
  runs. Keyed requests (`ByIds`, `ByKey` over a property unique across the table, `ByParentIds`)
  union their keys into one table-valued key set and their selects into one select per entity and
  key, and answer every key, a missing key as an explicit absence (every parent answers, with an
  empty list when it has no children); string keys compare as the platform collation does
  (ignoring case, width, kana type and trailing spaces, minding accents, `ß` meeting `ss`). A key
  already read, or primed from a row the operation holds (`Prime`, which wins over a read), is served from the cache
  unless the request selects more, which reads it again under the union. `Query`, `Rows`, `Count`,
  `Exists` (which shares the cap-0 count) and `Sql` merge identical requests; `Visible` conjoins the
  entity's read decision, taken when its round is composed for the connected user, reading nothing
  when it is denied (in a save, the decision a reference target's read is judged under, including
  its owner's read for a child without a query surface and the step-up of a sensitive read);
  `Ancestors` reads a tree's ancestor ids. `ByValues` (internal) groups the rows
  holding each of a property's values, unique or not, as the collation meets them: the save's
  uniqueness check reads through it. `Sql` is read-only: a statement declaring a write is refused when it is requested. A round
  (`BeginRound`, `DispatchAsync`) appends every pending request to one batch and completes the refs
  once it executes; a round with nothing pending sends nothing.
- **Scheduler.** `ValidationScheduler` runs the validators as tasks and dispatches one round each
  time every task is parked on `LoadAsync` or finished; a task registered during a round joins the
  next. A task that faults or a round that fails tears down the parked tasks; cancellation stops
  them. The ceiling counts dispatched rounds: one more is refused with an error naming the tasks
  still parked. `AddPipelineRound` raises it by one, before the rounds start, for a first round the
  pipeline alone needs. `tellma.crud.validation.rounds` records the rounds per operation, `crud.outcome`
  `ok` or `exceeded`.
- **Confirmations.** `Confirmations` (scoped, `IConfirmations`) is seeded once per scope with the
  keys the caller confirmed; a key no feature declared is an authoring error. Under a system
  principal every declared key counts as confirmed. `Frame(resource, operation)` opens one
  write's frame, which collects its warnings until it is disposed: a `Require` on the frame, or on
  the scope while the frame is the innermost open one (so an enlisted group is raised with its
  host), records a confirmed key's consent in the log and turns an unconfirmed one into a
  `ConfirmationRequest`; `tellma.crud.confirmations` counts both by `crud.key`. Outside every
  frame, `Require` keeps the warning in `Unframed`, which no outcome raises: code outside
  validation tests `IsConfirmed` and throws itself.
  `ValidationOutcome.Raise` throws a stale stamp first, then the errors, then every unconfirmed
  warning at once in key order.
- **Attribute walker.** `AttributeWalker` judges a saved row against its metadata facets first (a
  string's maximum length, a decimal's precision and scale, never rounding) and then the data
  annotations on its client-owned properties, the class's own attributes included, at each
  property's path, recursing into children with their parent key left alone; `IValidatableObject`
  is not called. A request body is walked through its public properties, a cycle visited once. A
  failed attribute maps to its validation code (`Required`, `Range`, `MaxLength` and the rest),
  else to `Validation.Invalid` naming the property and the attribute.
- **Save shape.** `SaveShape.Check` refuses an empty payload, enforces the stack's save, children
  and rows ceilings, and reports every repeat of a positive id at its path.

### The Excel row source

`ExcelRowsOperation` serves the row source the Excel codec streams from (`excel_rows`), created by
a stack's `ExcelRowSource` and, for an exportable child query surface, by
`IChildQueryService.ExcelRowSource` (a child refuses `All` and named filters). It is bound to one
`ExportSource`: by ids, an id key-set restriction over the distinct ids narrowed to the key type
(more than `MaxIds` refused), inactive rows included; by query, the clauses as a query's (filters,
named filters, search, arguments) and the activatable conjunct unless `IncludeInactive`; all, the
access filter alone. The ordering is the query's, else the source's, else `Id`, and the key ends
every ordering that does not already end with it, so pages cut by skip neither repeat nor drop a
row that ties on the ordering. The type-level
check runs once, before the first page.

- **`Pages`** streams the root `MaxTake` rows at a time, one guarded read batch per page; the
  query's `Take` caps the total, and `CountCap` counts on the first page only, neither bound by the
  stack's skip window, count cap or take ceiling. A short page ends the stream, and a page with no
  rows after the first is not yielded. Every dependent collection, at every depth, rides its
  owner's batch, restricted to the owner rows' keys captured into a batch-local key table; a
  collection that reaches `MaxTake` rows is completed by one more read per chunk over the owners'
  ids, its own collections riding it. Each page runs as its own operation, so a grant revoked
  between pages refuses the next page as `ForbiddenException`.
- **`ReadAsync`** reads several flat queries (an import's lookups) in one batch, each completed by
  further batches of `MaxTake`. A restriction's values bind as the column's table type (strings
  matched under the tenant's collation, numbers out of the column's range dropped). A lookup's
  target is read as a save's reference to it is (`TargetReads`): under the caller's `Read` decision
  on its own securable, else on its owner's seen through the child's navigation, else, for an
  entity no grant can reach, unrestricted; a denied target reads no rows, and a sensitive one asks
  for the step-up.

The traversal rule, the search's length and the refusal re-check apply as in a query; an arithmetic
failure and exhausted retries translate as the query's do.

### The save pipeline

`SaveOperation` (scoped) runs a stack's save in the frame of `CrudOperations` under the save's
`crud.source` and lane. Exhausted retries on any of its round trips are
`DependencyUnavailableException`. Each save is one `SaveRun`, in order:

1. **The shape**, then the type-level `Save` grant, before any round trip.
2. **Preprocessing.** On every row: the echoed stamp is kept, derived and server-owned columns are
   reset (and a new row of an activatable entity is made active, whatever its class initialises
   `IsActive` to), strings are trimmed (an empty nullable one becomes null) unless the property carries
   `[PreserveWhitespace]`, a nested child takes its parent key from the nesting, the twins of a
   language the tenant has not configured are dropped (refused under an import as
   `Import.LanguageNotConfigured`), and a value-set value takes its set's spelling. Then the
   service's `PreprocessAsync`, after which the payload is walked again: a `[Defaulted]` value the
   hook left empty is `Required`, and a temporary id no row defines is `Entity.NotFound` at the
   reference.
3. **The ids.** A warm buffer assigns them at once and the validators start before round trip 1
   with the final ids; otherwise the reservation statement rides round trip 1, which is then the
   pipeline's alone (`AddPipelineRound`), and the validators start after it.
4. **Round trip 1**, one guarded `Validate` batch composed for the connected user, carries beside
   the validators' loads: the update roots' before images under the `Read` filter with every
   collection the payload carries (a denied read loads none, so every update id is not found);
   the `Save` filter's count over the same ids; each reference target's ids under its `Read`
   decision (a denied target loads nothing; a child entity without a query surface is read under
   its owner's decision, seen through its navigation to the owner's row; an entity no grant can
   reach is checked for existence alone); the rows holding every unique value; and a tree's
   parents. The batch's prologue validates the set the decisions came from, so a moved
   permissions tag recomposes the round from the fresh set: its decisions are taken again and a
   deferred reservation is given back and taken afresh. A validator's `Visible` load is decided as
   a reference target's read is, a sensitive one asking for the step-up before any row reaches
   it.
5. **The ordered checks**, before any validator resumes: an update root missing or hidden is
   `NotFoundException` naming the ids, one outside the save filter `ForbiddenException`
   (`OutsideFilter`); under `ConcurrencyMode.Check`, an update without a stamp is
   `Concurrency.StampRequired` and one the stored row moved past fails the save at once
   (`ConcurrencyException`, the modifier's name read in a round trip of its own); the before images
   are matched to the payload (an omitted collection is untouched, a present one matches its
   children by id and deletes the rest) and primed into the loader; server-owned values are
   restored, a write-once value is kept or refused as `WriteOnce`, and a prefix holding `" - "` is
   `DisplayName.PrefixSeparator`; an update child its parent does not hold is `Entity.NotFound`; a
   new or changed reference to a target read under a sensitive securable asks for the step-up a
   direct read asks for, and one the caller cannot see is `Fk.NotFound`; a unique value repeated in
   the payload (per parent for a child) or held by another stored row is `Unique`, unless the
   persist frees it first: a child the synchronisation deletes, or a row the payload updates to
   another value when the row taking it is an update or a child (a new root's insert runs before
   the roots' update); a tree's cycles and depth are judged.
6. **The validators' rounds**, then the value sets (a new or changed value outside its set is
   `Validation.ValueNotInSet` unless an error is already recorded at its path), then
   `ValidationOutcome.Raise`. A save that does not persist returns its ids to the buffer.
7. **The tenant-state verdict** (`PersistVerdict`), before the persist is composed: a `Frozen`
   tenant refuses every persist (`tenant-frozen`); a `ReadOnly` one refuses a mutation that does not
   allow the state (`tenant-read-only`), unless a `WriteFrame` the scope's `WriteFrames` holds
   admits it (a read-only-safe API action, a job whose handler runs on a read-only tenant, a
   provisioning step); an API action frame that does not admit it refuses whatever the nested
   operation allows.
8. **The persist**, one guarded `Persist` batch under the pipeline's compilation limits, composed
   between the executor's fixed prefix and suffix in this order: the emitter's save under the stack's authority; the service's
   `ContributeAsync`, then each `IPersistEffect<T>` in registration order, through a
   `RecordingBatch`; the caller's `SaveOptions.OnPersist`, under the caller's own authority, so a
   write to a table another stack owns is refused at append; the access guards, when the context is
   not the system's and the batch writes `core.Users`, `Roles`, `RoleMemberships` or `Permissions`
   (the stack's root or a collection at any depth, a group enlisted with it, or any statement's
   declared writes): their lock, which the executor takes first in the transaction, and their
   invariants; under a
   filtered `Save` grant, the post-check over the saved ids (`RowSecurity`, expecting every root);
   and, when asked for, the read-back over the saved ids in details shape. A participant only
   appends: a round trip of its own while it contributes (`ComposeFence`), executing the batch, or
   setting its mode or limits is `InvalidOperationException`, and a commit callback it registers is
   contained like a post-commit failure. The tags a validation round's batch declared ride the
   persist's guard, to be re-run on a mismatch; no round declares an `entity:*` tag of its own yet,
   so in a running host this adds nothing until one does. Once sent, the persist completes whatever
   the caller's token says.
9. **A re-entered compose** (a moved tag the guard or the re-check found) calls no participant
   again: the validators re-run in memory for the fresh user, the loader answering only from the
   rounds already run (a load they did not serve is `StaleContextException`; a new error is
   `ValidationException`, a new unconfirmed warning `ConfirmationRequiredException`, neither
   counted nor logged twice), the pipeline's own statements are recomposed under the fresh grants,
   and the recording is replayed: each batch-local name must come out the same, and a result a
   participant read at compose time (a cache hit) must answer alike. A served tag the fresh
   snapshot moved past is `StaleContextException` before the validators re-run. The read-back's
   details hooks are read-side and pure, so they are rebuilt under the fresh grants with the rest of
   the pipeline's statements. Every recomposed batch of a CRUD operation (a query, a details read, a
   validation round or a persist) is counted once by its cause (`tellma.crud.stale_context.reruns`,
   `crud.tag`): the runner notes the cause in the scope's `RecomposeLog`, and the operation's frame
   counts it.
10. **The translation** of the persist's failures (`PersistFailures`): the emitter's conflict rows
    name each modifier in the content language, falling back to the primary name
    (`ConcurrencyException`), unless every row is missing (`NotFoundException`); the post-check is
    `ForbiddenException` (`OutsideFilter`); `50404` is `NotFoundException` with the ids the batch
    found missing; `50422` and a distribution's `50600` to `50699` are `ValidationException` at the
    root with the assertion's code (`Blob.NotAttachable` with `reason` `persist`); a cycle or a depth
    the tree fence finds is `Tree.Cycle` or `Tree.TooDeep` at the root; a unique index of the stack's
    tables is `Unique` (`Blob.NotAttachable` for a blob reference's, `Assignments.Concurrent` for the
    open assignment's) at the one payload row carrying the value, else at the root naming the
    property; a foreign key violation names its side (`ForeignKeyViolationException.Conflict`, read
    from the server's message: a write conflicts with the `FOREIGN KEY` constraint, a delete with the
    `REFERENCE` one, else the table the conflict occurred in decides what it can): a write of the
    stack's tables is `Fk.NotFound` the same way, and a delete, which only the save's child
    synchronisation makes, is `Fk.InUse` naming the referencing entity and property, at the payload
    collection that dropped the row when one alone dropped rows of the referenced entity, else at
    the root; a side the message does not tell is `Fk.InUse` when the synchronisation deleted rows
    the key references, else `Fk.NotFound`; anything else passes through. Conflicts are counted by
    kind (`tellma.crud.concurrency.conflicts`: `stamp`, `missing`, `cycle`). A commit whose response
    was lost and that the executor's probe proved is read back as a plain read over the final ids,
    whatever the caller's read filter admits: the roots' stamp, and the rows in details shape when
    asked for. A failure of that read never answers the committed save with an error a caller
    might retry: it is logged (`ProvenCommitReadFailed`) and the save answers its ids alone. The
    post-commit phase runs either way; the lost batch's commit callbacks do not run, and a
    participant's recorded result there stays unanswered. Any other ambiguous end, an update-only
    save's, which no inserted row can prove, is `DependencyUnavailableException` (`database`, one
    second): the caller reloads to learn whether it committed.
11. **The post-commit phase**, after the commit and outside any transaction, before the response
    is assembled (whose extras run contributor code that may fail): the saved rows are recorded
    (`tellma.crud.save.entities`, at every depth) and a committed override counted when an updated
    root's stored stamp differed from the one the caller echoed
    (`tellma.crud.concurrency.overrides`); then the service's `AfterCommitAsync` and each effect's,
    in order. A failure is logged (`PostCommitFailed`, with the operation's trace id) and counted by
    the failing component's type name (`tellma.crud.effects.failures`, `crud.effect`), and never
    changes the response.

### The delete and action pipelines

`DeleteOperation` and `ActionOperation` (scoped) run a stack's deletes and actions in the frame of
`CrudOperations`; each run shares `WriteRun` with the save: the access decisions taken for the user
each batch is composed for, the entity tags its rounds were served under, the persist's guard and
re-entry, the translation of its failures (`PersistFailures`) and the post-commit phase. Every id
list is de-duplicated in request order; an empty one takes no round trip on a delete or the
one-round-trip activate and deactivate, while a general action still runs its hooks and method;
more ids than the stack's
`MaxIds` is `LimitExceededException`; a write without its grant is `ForbiddenException` (`NoGrant`)
before any round trip. On every id-addressed write an id the caller cannot read, or that does not
exist, is `NotFoundException` naming those ids alone, before a visible row outside the grant is
`ForbiddenException` (`OutsideFilter`): both checks run inside the persist's transaction, or, on a
validated path, on round trip 1 beside the validators' first loads (and again in the persist). A
cacheable stack's Read takes no filter, so a caller holding only a filtered write grant there reads
no row and every id is not found.

- **Whether a write validates** (`HookOverrides`): the service overriding `ValidateDeleteAsync` or
  `ValidateActionAsync`, or a component of the stack's entity overriding the interface's default, is
  a fact of the classes, inspected once each. A delete by ids or with descendants that nothing
  validates is one round trip; one that is validated is two, the first carrying the two-stage
  check and the validators' first round.
- **A delete by ids** compares the stamps it is given, parallel to the ids (a null one is
  `Concurrency.StampRequired` at `Ids[<i>]`, another count than the ids' is `BadRequestException`), and
  a stale stamp is `ConcurrencyException`. A stack whose keys are `long` compares no stamps: a delete
  of it given stamps is `BadRequestException` before any round trip. **With descendants**, the roots are checked under the
  Read filter and the whole closure under the Delete grant: a subtree holding a row outside it is
  refused whole.
- **A delete by query** refuses a blank filter and a negative expected count (`BadRequestException`),
  compiles the filter before any round trip (its diagnostics at `filter`), runs no validation hook,
  and, in one round trip, counts the rows it matches beside the capped delete: more than
  `MaxDeleteByQueryRows` is `LimitExceededException` and another count than the caller confirmed
  `CountMismatchException`, both raised inside the transaction, so nothing is deleted. The count is
  capped as the delete is, reading one past the cap, so `StackLimits` refuses a cap of the largest
  integer. Since it names no ids a delete validator could see, the startup gate refuses a stack that
  keeps `DeleteByQuery` while its service or a validator judges its deletes; such a stack deletes by
  ids alone.
- **A row still referenced** (`ForeignKeyViolationException.Conflict` on the delete side) is
  `Fk.InUse` naming the referencing entity and property, at `Ids[0]` for one id, at `Ids` for several,
  and at the root by query.
- **Activate and deactivate** that nothing validates are one SQL-only round trip: one stamped
  update of `IsActive` under the read filter and the Activate grant, which leaves rows already in the
  state unstamped. Otherwise they take the general path, and answer alike.
- **The general action path**: the action resolves by name, the overload must match its method
  (`ExecuteActionAsync<TResult>` for a method returning a value, the other for one returning none:
  else `InvalidOperationException`, before any round trip), a single-target action takes exactly one
  id (else `InvalidOperationException`), and the arguments, as JSON or as the action's record, are
  deserialised and walked for their attributes at `Arguments.<Property>`. Round trip 1 loads the
  targets and checks them under the grant beside the validators' first loads; the validation hook,
  the components and the method run over one `ActionContext` with their rounds; then one persist:
  the method's and the components' recorded appends, the stamped `Update`s of the targets, the
  post-check when the action asks for it, and the read-back. An `Update` with a condition repeats
  the action's precondition: a target that no longer meets it, the read filter or the grant's
  filter is skipped, unstamped (yet a post-check still refuses a target that left the grant's
  filter), and the action's other appends still see it; one with a refusal code instead counts the
  targets meeting the condition first, and fails the action with that code at the root unless every
  target does, then writes and stamps every target. A method composes before the persist
  batch exists: its table-valued parameters and declared tables are named by the recording
  (`ReservedSqlNames.RecordingPrefix`, `@tm_rec<n>_…`) and keep those names wherever the recording
  lands, one recording's per batch, while an append that answers at once from its batch (a raw save or delete, a cache read) is
  `InvalidOperationException`. `ActionContext.Persisted` holds the method until the persist
  committed and then answers what its appends read: the task that read it leaves the rounds once
  the thread leaves the reading flow's context, and from the read on it composes nothing (a load,
  a save, an update or an append is `InvalidOperationException`, and one raised before the flow
  stops running fails the rounds, so nothing is sent). Every such refusal is recorded on the run,
  whichever flow raised it: one recorded before the persist is sent abandons the action, and one
  recorded after it (a flow that awaited something else before composing) is logged
  (`ActionComposedAfterPersisted`) and raised once the action committed, for every action. A read
  of `Persisted` during the rounds from a flow that carries no task's context is refused, and a
  released flow faulted with several exceptions abandons the action with the first it holds. The hook's and
  the components' released flows are awaited after the commit, as the method is, and a failure of
  theirs then is logged (`ActionTaskFailedAfterCommit`). When the action fails instead, every released
  flow, the method's included, resumes with the failure and is awaited before the action raises
  it, so work a flow does on the failure (undoing a remote call) finishes inside the operation and
  its scope; the action's failure is raised whatever the flows threw. A `PartialFailure` the method throws is
  rethrown once its persist committed. Any other failure of the method after the commit is raised
  only when the method answers a value of its own; otherwise it is logged
  (`ActionMethodFailedAfterCommit`) and the action answers its ids. The outcome's stamp is the one the rows `Save` marked carry, else the
  clock at the commit. A re-entered persist re-runs the hooks and components in memory under round
  trip 1's verdicts and replays the recordings; the method is never re-invoked.
- **A commit whose response was lost**: one the executor's probe proved answers a delete with the
  rows it was asked to delete and an action with its ids (and, when asked, the rows read back by a
  plain read). A failure of that read is logged (`ProvenCommitReadFailed`), and one of a method
  reading a result the lost batch never answered is treated as any failure after a commit; neither
  answers the committed write with an error a caller might retry, except that a method answering a
  value of its own has none to give, so its failure is raised. Any other ambiguous end is `DependencyUnavailableException`
  (`database`, one second).

### Write frames and enlisted writes

Every write runs in a frame. Each save, delete and action runs in a pipeline frame (`IWriteHost`,
handed to its hooks as `context.Host`); an `[ApiAction]` method, a job handler and a provisioning
step run in an open frame, which the scoped `IOpenWriteHost` resolves to (the innermost open frame
of the flow). A frame names its kind and its `Operation`: `<Resource>:<op>` (an entity action's
or an API action's by the action's name, such as `gl.Invoice:post`), `job:<key>` or
`provisioning:<step>`. A write nested in an open frame meets a read-only tenant's verdict as that
frame's descriptor judges it: admitted in an API action that is no mutation or allows a read-only
tenant, in a job that runs on one and in a provisioning step, refused in any other API action.

- **Enlisting.** A writer that declares `IEnlists<TTarget>` adds a save (`EnlistSaveAsync`) or a
  delete by ids (`EnlistDelete`) of the target's stack to a frame. The frame admits it only for a
  pair the registry holds, from a participant of the frame (the host's service, companions and
  components, a group's own participants, the class an open frame was opened for, or the email
  outbox, which every frame admits so that any code with a host can enqueue mail), and only
  while a pipeline frame validates or before an open frame's `PersistAsync`. A write to the
  writer's own stack is admitted from the open frame of that stack's API action alone, and a root
  id enlisted twice with one target in one frame is refused at the second. The injected
  `IOpenWriteHost` enlists from the open frame's own work alone: code inside a pipeline's or a
  group's frame nested in it (a hook of a front-door save the work called, an enlisted write's
  participant) enlists with its context's `Host`. Each refusal is `InvalidOperationException`.
- **A group** runs the target's own stages as tasks of the host's scheduler, on the host's rounds:
  preprocessing, the before images of its updates read unfiltered (a missing row is
  `Entity.NotFound` at the mapped path, with its ids), the server-owned members the writer names
  re-applied, the ids (at once from a warm buffer, else reserved on the host's next round, which an
  enlistment awaited after it then waits for), the validators and the components. The caller's
  grants and reference visibility are not asked of it. An enlisted save overrides stamps by
  default; under `Concurrency = Check` a default stamp is `Concurrency.StampRequired`. A delete
  that nothing validates loads nothing and deletes by its ids alone.
- **Errors** land in a re-homing view: each participant writes its own payload's paths, and the
  host's list receives every error through the enlistment's `MapPath` (the host's root when none),
  composed along the nesting, with `enlistedResource` naming the innermost target's entity.
- **The persist** is the host's one batch. Each group's statements sit ahead of (`BeforeHost`) or
  after (`AfterHost`, the default) its immediate host's own, so a group's own groups nest: B
  enlisted ahead of A, with C after B, composes B, C, A. Each group appends under its target's
  authority, the access guards join once every statement composed when the host's stack or any
  statement's declared writes meet a security table (a group's save or delete declares its
  target's tables, at any depth), and a failure is attributed by its
  statement's ordinal to the group that raised it, translated by that group and re-rooted onto the
  host's paths. After the commit the host's post-commit phase runs first, then each group's in
  composition order. Each enlisted write is counted once (`tellma.crud.enlistments`) under the
  host's operation (every kind of delete as `delete`) and outcome.
- **A re-entry** re-runs the host's validators in memory, and each enlistment of the re-run is
  answered from the group the same task (the hook, or one validator component) enlisted at the
  same step of the first run, with that group's ids, whatever order the tasks ran in. A step the
  first run had no such group for, or took while another flow of its task was parked at a load, or
  a first-run enlistment the re-run never makes, is `StaleContextException`; the groups an entity
  action's method enlisted are replayed as they are, since a re-run never invokes the method. A
  group's own validators never run again, so a re-entry composed for another permission set than
  the composition before it is `StaleContextException`, naming the permissions tag, whenever it
  would replay a group whose target's validators judged its rows; a group nothing validated
  replays, as does one whose only validator is the blob-reference check over rows that reference
  no blob, which judges nothing a permission set decides.
- **The front door.** A service's `SaveAsync`, `DeleteByIdsAsync`, `DeleteByQueryAsync`,
  `ExecuteActionAsync`, `ActivateAsync`, `DeactivateAsync` or `DeleteWithDescendantsAsync` entered
  while a pipeline frame of the flow composes is `InvalidOperationException`. From an open frame it
  runs a pipeline frame of its own, and the open frame's `PersistAsync` is refused while it composes.
- **Open frames** run their work as a task of their own rounds. `PersistAsync` is called once, from
  the work itself: it waits for the groups' rounds, raises their errors and then their warnings,
  and sends every group in one persist batch (none enlisted: no round trip). Every composition of
  that batch decides an API action's securable again for the user it is composed for, so a
  re-entry for a caller who lost it is `ForbiddenException` (`NoGrant`) and nothing commits. Its
  outcome's stamp is the one the batch's saves stamped; a batch that saved no row, a commit whose
  response was lost and a job's frame report the frame's clock instead, which no stored row
  carries. Work that returns with groups it never persisted is `InvalidOperationException`, and
  nothing is sent. A job's frame
  appends its groups to the completion batch the worker composes, after the outcome statement, and
  runs their post-commit phase once the worker reports the commit. A provisioning step runs inside
  a frame of its own, opened by `IProvisioningFrameOpener` around the step's creation and run.
- **`IApiActionInvoker`** accepts only the registry's own `ApiActionDescriptor` instance, resolves
  the handler for each request, requires the action's securable (or, for a member-only action, a
  connected member; the system scope passes), walks the body's annotations at the body's own member
  paths (no body where one is declared is `BadRequestException`), names the scope's operation after
  the action, and runs the method inside the action's open frame through every
  `IApiActionInterceptor`, the first registered outermost.
- **Stack table ownership.** `StackTableOwners` maps every root and child table of every stack to
  it, for the batch's append-time check: a statement appended through `IDataBatch`'s public members
  whose declared writes include a stack's table outside the appender's authority is
  `InvalidOperationException`. The platform's own composers (the emitter, the access guards, the
  job, notification and tree statements) append through the batch's internal channel.

## Queryex discovery

`QueryexService` (route `queryex`, registered by `CoreFeature`) serves `discover`, a member-only,
idempotent read hidden from the agents, for the editors: it checks clauses being written as `query`
or a role save would compile them and answers `200` with every problem. An aggregate query's measures
are checked as its compilation checks them, each problem at its item in clause `measures`. Admission (`DiscoverShape`)
runs before any I/O: a root the widest schema shape lacks is `NotFoundException`; measures or a
having clause without an aggregation, a confirmed parameter without a name, of a
hierarchy or geography type or named twice, and for a permission filter more than one filter or any
other clause or parameter, are each `BadRequestException`. The caller's set comes from the scope's
connection, and no statement ever runs. A cached caller costs no round trip while a prologue
confirmed them within the fast-deny window and nothing else is due (`IScopeConnection.IsPrologueDue`);
otherwise the guarded runner runs its prologue with no statement appended, which refuses a
deactivated caller (`TenantNotFoundException`), reloads a set past its maximum age, stamps activity
and hands back the confirmed set the paths are decided from.

- **The query profile.** One `DiscoverQuery` over every clause, the filters as the query composes
  them (one leaf each, conjoined in request order), with the comparisons when asked for. Then
  `CompileQuery` under the pipeline's ceilings, the select defaulting to the key, with the confirmed
  types plus the agreed inferred ones as nullable declarations; its SQL is discarded, its
  diagnostics are located as the query locates them, and the undeclared-parameter report (QX3007) is
  dropped, since the parameters' answer says the type is unknown. The traversal rule's `Refusals`
  under the connected user's decisions add one `Query.PathNotReachable { entity, path }` at each
  refused path, and mark the path unreachable.
- **The permission profile.** The one filter is judged by `PermissionFilterRules`, the rule the role
  save runs (the length, then the validation of a predicate over the root with a user and no
  parameters under the stored filter's ceilings) over the text trimmed as the save stores it, with
  every span moved back into the text as sent: `Permissions.FilterTooLong` at the part past the
  maximum, else the engine's diagnostics at `filters[0]`, QX3007 kept. A stored filter is the
  platform's text when a query runs, so every path is reachable.
- **The answer.** Each parameter with its sites, its confirmed type, else the type its uses agree
  on (null when nothing constrains it or they conflict), its conflicting types, the path it is
  compared with and the entity a key or foreign key at that path's end identifies; each path with
  the entity its last segment is read on; and the comparisons with their filter's index, dotted
  path or canonical expression, operator and literal in the wire's scalar encoding.

## The route model

`IRouteContracts`, a singleton `CoreFeature` registers, derives the web surface's routes once from
the stack registry: one `RouteContract` per projected endpoint, in mapping order. Each stack
projects its standard operations as its operations and capabilities imply them (`query`, and
`get-by-parent-ids` for a tree; `get`, `get-by-ids`, and `all` for a cacheable entity; `save`;
`delete`, `delete-by-query` when the stack keeps `DeleteByQuery`, and `delete-with-descendants` for
a tree), then every action, the
built-in ones and its companions' included; each child query surface projects `query`, then its
actions; each routed service its actions. A route's template is
`POST /{tenantId}/api/web/<segment>/<operation>`. Each carries its body and result types, its
securable (null for a member-only action), and the facts consumers read: the reads are idempotent
and not mutations, the writes mutations and not idempotent, an action's facts are its descriptor's.

The fingerprint is the first 12 characters of the base64url SHA-256 of a canonical description
(`ContractDescriber`): the request's and response's JSON contracts under `TellmaJson.Options`
(each member's JSON name, kind and nullability and each enum's values, sorted, transitively; an
entities result with its ids in the key type and the related projection of every entity its
stack's details expansion reaches), with the stack's, child surface's and action's
`ContractRevision`. The description holds no assembly versions, so it is the same in every process
and on every operating system; the suite pins the fingerprints as goldens.

`ContractsClientAssetContributor` writes the client metadata's Contracts chunk from the same
routes: `fingerprints` (template to fingerprint), `long` and `idempotent`.

## Securables

A securable is the `(Resource, Action)` pair a permission targets, with the query entity a grant's
filter binds against (`FilterRoot`, or none), whether it asks for a recent sign-in (`IsSensitive`),
the feature it belongs to and, for a child exposed as a query root, its `Owner`. Contributors
register them: each `ISecurableContributor` service (a collection service), and each
`contribution.Securables(builder => …)`, which the realizer turns into one on behalf of its feature.
`SecurableRegistry`, the singleton `ISecurableRegistry` `CoreFeature` registers, runs every
contributor once, on first resolution (the realised gate resolves it), then freezes the builder.

- **Merging.** The same pair registered again is one securable: the first registration's
  spelling, sensitive when any registration says so. Another filter root, another owner, another
  feature or another spelling of the resource or action is a problem.
- **A throwing contributor** is a problem; the other contributors still run, and the registry then
  holds no securable at all. The checks that judge against the registry (`core.notification-types`'
  securable targets, the endpoint audit's securables) skip that part and add one line pointing to
  `core.securables`.
- **Feature.** `Add`'s `feature`, else the feature whose contributor registered it: the item's
  feature, the feature that registered the contributor's type, or `core` for a contributor
  registered by a factory or outside every feature.
- **Sensitivity.** Registered sensitive, a Core default (`core.User` × `Save | Delete | Activate |
  Invite | Credentials`, `core.Role` × `Save | Delete | Activate`, `core.Settings.General` × `Save`,
  `core.Schedule` × `Save | Delete | Activate`; a default nobody registers changes nothing), or
  `MarkSensitive`, unless `MarkNotSensitive`. A mark of an unregistered pair, and a pair marked both
  ways, are problems; the latter stays sensitive.
- **Aliases** resolve a renamed resource on `Find` and `FindResource` alone, one step, to a
  registered resource. `FindResource` returns the resource's registered spelling, so a stored
  permission naming a former name or another casing resolves to the pair as registered.
- **Lookup.** `All` is ordered by resource, ignoring case, then by action: the platform's
  actions first, in the order `Read`, `Save`, `Delete`, `Activate`, `Invite`, `Preferences`,
  `Credentials`, `Retry`, `Cancel`, `Resume`, `Share`, `Publish`, `Transfer`, then any other action,
  ignoring case, so the client lists a resource's actions in that order; names compare
  ordinal-ignore-case and keep their registered spelling. `Fingerprint` is the lowercase hex SHA-256
  of the sorted `Resource|Action|FilterRoot|IsSensitive|OwnerResource|OwnerNavigation` lines and
  `alias|Old|New` lines.

`CoreFeature` registers `core.UserStamp` × `Read` (filter root `core.UserStamp`, owned by
`core.User` through `User`); the users', roles', jobs', schedules' and notifications' securables are their stacks'
(see [Entity stacks](#entity-stacks)).
`SecurablesClientAssetContributor` writes the `securables` section of the Core chunk: one
`ClientSecurable` per securable, each label key the first of its chain that a neutral shared
`Strings` resource of a composed feature declares (`INeutralStringKeys`), else the chain's first key
with humanized English as the fallback (pluralized for an entity's plural key). Core's `Strings`
declares `Action_Read`, `Action_Save`, `Action_Delete`, the plural label of each of its stacks and
child query surfaces (`Core_User_Plural`, `Core_Role_Plural`, `Core_RoleMembership_Plural`,
`Core_Permission_Plural`, `Core_Job_Plural`, `Core_Schedule_Plural`, `Core_Notification_Plural`), `Securable_core_UserStamp` and
`Feature_core`.

| Check | Fails when | Needs infrastructure |
|---|---|---|
| `core.securables` | a resource is not two or more dotted segments of an ASCII letter and letters or digits within 128 characters, an action not a PascalCase identifier within 32, or either is `*`; any merging, alias or mark problem above; a securable belongs to a feature that is not composed; an owner is not a registered resource, or its securable has no filter root; an owner chain (each pair to the same action of its owner) is a cycle; a filter root is not an entity of the query schema; a child entity's securable has no owner; an owner's navigation is not a path of navigations from the filter root, or ends elsewhere than the owner's filter root; the securables of one resource that take a filter take different filter roots (a permission on every action of the resource binds its filter against the one root they share) | no |
| `core.cacheable-entities` | a `[Cacheable]` entity is not a `TopLevelEntity<int>`, declares a child collection, resolves to a row limit outside 1 to `Tellma:Cache:EntityMaxRowsCeiling` (judged only when the cache options validate), or has a `Read` securable with a filter root; the entities store cannot be created (another live `entities` cache on the host's meter) | no |

The host's endpoint audit (`host.endpoint-audit`) refuses an endpoint whose
`SecurableEndpointMetadata` names a pair the registry does not hold, or holds only through an alias.

## Access evaluation

A caller's permissions are a `UserAccess` (in `Tellma.Core.Abstractions`): the grants of every
membership that has not ended, future ones included, each with its window, the grants of every
active public role, and the stored permissions that grant nothing, each kept as an `AccessProblem`.
`UserAccess.Decide` is a pure function of the set and the instant it is given, so a window opening or
closing needs no reload; `AccessComposition.Compose` is the same composition over a list of grants,
for "who holds this". The composition: a grant matches a securable `(R, A)` when its resource is `R`
or `*` and its action is `A` or `*`; when `A` is `Read`, a grant of another action on `R` matches too,
and so does one on `*`, but only when `R` registers that action (`ActionResourceMap`, frozen from the
registry and shared by every set), so a grant holding an action on one resource never reads every
other. A filtered grant never reaches a securable without a filter root; any unfiltered match is unrestricted; otherwise the filters union as
one leaf per distinct text and stamp, sorted ordinally, then each bespoke criterion of the action
(`IAccessCriteriaProvider`), then the owner's decision on the same action through the owner's
navigation. Nothing to union is a denial. The system set is unrestricted on everything; a set without
a user denies everything.

`UserAccessBuilder` builds a set from the `PermissionRow`s the connect prologue loads. Each row is
classified by `PermissionClassifier`, in the drift table's order, the first failing check deciding:

| Problem | The row |
|---|---|
| `WildcardResourceOnPublicRole` | names the resource `*` on a public role, which reaches every user |
| `UnknownResource` | names a resource the registry holds under no name or former name |
| `UnknownAction` | names an action the resource does not register; with the resource `*`, an action no resource registers |
| `FilterUnsupported` | carries a filter where none binds: the securable has no filter root, the resource is `*`, or the resource's securables that take a filter do not share one root (a filter on any action also reaches `Read`, so it must bind against the same entity there) |
| `VersionUnsupported` | carries a filter without a Queryex version, or with one this engine does not compile |
| `FilterInvalid` | carries a filter the engine does not validate against the tenant's schema, as a predicate over the root with a user, no parameters and the `QueryexProfiles.AccessLimits` |

A grant carries its pair as registered, its filter text and role name interned across sets, and its
window; a public role's grant has none, and a membership that has ended by the build grants nothing.
`RoleAccessGrant` itself refuses a filter on `*` and a public role's grant on `*`, so a list of grants
built anywhere else cannot hold either; the build drifts such rows before it would construct one. The
interner holds at most 65,536 strings and 4 Mi characters, and the memory of reported problems at
most 16,384; each starts over when full.
Each distinct filter is validated once per root, text and stamp; exact duplicates are one grant; the
grants are ordered by role, permission and window, the problems by permission, whatever order the
rows arrived in. Each problem is counted (`tellma.access.permissions.unresolved`, by `problem`) and
logged (`PermissionDrift`, event 3101, category `Tellma.Core.Access`) once per tenant, version of the
tenant's permissions, permission and problem; each build records `tellma.access.set.build.duration`
and `tellma.access.set.grants` and logs `AccessSetBuilt` (3106) at Debug.

A request reaches its caller's set through the connection. `ConnectInitializer` (Order 100, before
the locale negotiation at 200) connects the scope through `IUserConnector`: by subject for a request
(a service account's subject is its client id), by id for a background scope that carries its run-as
user, and as the system for a system scope. The connected user (`ConnectedUser`: id, kind, state,
profile, permission set, the tenant's tags) comes from the process's connect cache when present, and
otherwise from one prologue-only round trip that the concurrent first requests of one subject share.
Every guarded batch then carries the connect prologue as its first statement, one fixed text per
variant (by subject, by id, the system), bound with the same parameters every time: it resolves the
user, stamps the activity at most once per `ActivityStampInterval` (never under a `Frozen` tenant or
for a background request), records an invited user's first request (never under `Frozen`), reads every
tenant tag, and returns the permission rows, the profile and the settings only when the cached ones
are stale, so a stale part is refreshed in the round trip that found it. The guard around the body
passes when the user is the expected one, the permissions are current, and, for a validation or a
persist, the settings are too; a read runs under the settings it composed with. The bookkeeping
writes are autocommit, are audited, never count among the batch's written tables and bump nothing;
an `Explicit` batch that carries the subject prologue is refused. The permission sets
(`PermissionsCache`) and profiles (`ProfileCache`) are versioned caches keyed by tenant and user,
so two tenants that reuse an id never share an entry. A round trip that confirms the scope's own
set refreshes the subject's connect-cache entry only while that entry holds the same set, so it
never restores an entry dropped since or replaces a newer one.

`IGuardedBatchRunner` runs a read, a validation or a persist for the scope's caller: it composes the
prologue and the caller's statements, executes, applies what the prologue read, and returns the
caller's read when the guard passed. A failed guard, a persist whose in-transaction re-check finds a
moved tag (`50412`, after a cold re-connect) and a read or validation whose re-run input moved each
cost one recompose from the fresh state; when the round trip brought newer settings, the initializers
after the connection run again first. A second failure in the same call is a `StaleContextException`,
counted (`tellma.access.connect.results`, `result` = `guard_thrash`), metered as exhausted and logged
(`GuardThrash`, 3102). A persist whose re-check finds the caller deactivated (`50401`) drops the
caller's entries and answers `tenant-not-found`. Each prologue round trip records
`tellma.access.connect.prologue.duration` by `variant`; each connect counts one result (`warm`,
`cold`, `stale_permissions`, `stale_settings`, `stale_profile`, `unknown_subject`, `deactivated`),
and a refusal logs `ConnectRejected` (3105) without the subject.

A scope's decisions come from `IAccessEvaluator` (`AccessEvaluator`, one per scope), at the host's
clock. `EvaluateAsync` decides one pair or a list from the caller's one set (the system set for the
platform's own work), reading the bespoke criteria of the pair's resource on every call and deciding
a child's owner first; a pair the registry does not hold is a denial with one `UnknownResource` or
`UnknownAction` problem. A scope that names no caller is denied everything before any decision is
composed, criteria included. When a registered pair is denied from a set no prologue confirmed
within `FastDenyWindow`, the evaluator makes one prologue-only round trip (`RevalidateAsync` on the
connection) and decides every pair of that call again from what it returned; a passing prologue
advances the set's `ValidatedAt`, a moved tag brings the fresh rows, and a scope re-checks at most
once, however many calls race; a re-check that fails (cancelled, timed out or refused) leaves the
scope's one re-check to its next stale denial. `RequireAsync` raises `ForbiddenException` (`forbidden`, the
registered pair, `NoGrant`) for a denial, then holds a sensitive pair to `StepUpBar` (the
`urn:tellma:acr:aal2` class within 15 minutes unless the web host's `TellmaSessionOptions.StepUp`
says otherwise; never for the system; `IndividualRequiredException` for a service account). A
session meets a bar of the identity server's tier scheme by rank, so `aal3` meets an `aal2` bar;
a class outside the scheme is met only by itself. Every
pair `EvaluateAsync` or `RequireAsync` decides is recorded in the scope's `AccessWitness` and counted
(`tellma.access.decisions`, by `outcome`), once per pair however many re-checks it took.
`EvaluateForAsync` decides for another user from that user's set, loaded once per scope and user
(`LoadDetachedAsync`: by id, expecting nothing cached, never cached itself and never reporting the
user's tags to the response). The load runs for the scope: each call waits on it under its own
cancellation, and the scope's end, which disposes the evaluator, cancels it. The caller's own id
answers from the caller's set; a scope that names no caller, a scope outside a tenant, and an
unknown, inactive or system user are denied everything, the first two with no load. `EvaluateAllAsync`
decides every registered securable from the set as it is, for the caller's summary, whose own
prologue has just validated it; neither it nor `EvaluateForAsync` re-checks, records or counts.
`Invalidate` drops a user's cached set, profile and connect-cache entries (`ConnectCache.RemoveUser`,
which walks the entries), so the next connection of the user loads afresh, and the scope's load of
them; a scope already connected as the user keeps its connection to its end.

`AccessWitness` also holds the member witness, which `ConnectInitializer` records when a user or a
service account connects. `Confirm` answers, before a successful response, whether the endpoint's
declared pair (in any spelling the registry resolves) or, for a member endpoint, the member witness
was recorded; a missing one is counted (`tellma.access.witness.missing`) and logged
(`MissingWitness`, 3100, at Error, with the tenant, the user, the endpoint and the expectation) for
the host to replace the response with an error.

`IAccessHoldersResolver` (`AccessHoldersResolver<TUser, TRole>`, closed over the distribution's
leaves) answers who holds a securable, in one guarded read on the caller's behalf: the matching
stored permissions of active roles (the securable's resource and every owner on its chain, their
former names, the action or `*`, any action for `Read`, and on the resource `*` the actions the
chain registers for `Read`), the current memberships of those roles' active members other than the
system user, the audience every public role reaches, the members the caller may read under the
caller's own `core.User` × `Read` decision, and the involved roles, each with its related
projection. The current-membership predicate is `AccessFilters.CurrentMembershipSql("M")` written
literally, since an interpolated text binds as a parameter. `HoldersComposer` composes the result
with the evaluator's own composition, so a user is a holder exactly when a decision over the user's
own memberships allows the securable: `Everyone` is the public roles' and the criteria's outcome,
`Grants` and `Problems` those of every role at once (windowless), `MembersByRole` counts each role's
members (a public role counts the audience), and the readable holders are listed by display name in
the reader's content language, then by id, up to `MaxHolders`, with `Total` counting every holder and
`Capped` whether the visible ones were cut. A users' filter beyond `QueryexProfiles.PipelineLimits`
is an `InvalidOperationException`, logged (`FilterTooComplex`, 3107, at Error, with the roles behind
it), never a silent empty list. The resolver authorises nothing itself: the roles' names, filters and
member counts and the holder count reach any connected caller, so a caller that returns the result
holds the scope's caller to `core.Role` × `Read` first.

`AccessService` (route `access`, contributed by `CoreFeature`) projects the two member-only read
actions the role and user pages call, both hidden from MCP:

| Action | Round trips | What it answers |
|---|---|---|
| `check` (the caller) | 0 | the caller's decision per requested securable, from the set the scope already loaded |
| `check` (`UserId` another user) | 2 | after `core.Role` × `Read`, one read of the user under the caller's own `core.User` × `Read` filter (a hidden user is not found, as a missing one is), then the user's decision per securable |
| `holders` | 1 | after `core.Role` × `Read`, the resolver's answer for one securable |

A `check` names at most `MaxSecurables` (200) securables (`LimitExceededException`, limit
`MaxSecurables`); a `UserId` equal to the caller's own id is the caller's check. An unknown resource
or action is a `Denied` decision carrying its problem, never an error.

`IAdministratorDirectory` (`AdministratorDirectory`) reads, in one guarded round trip, the ids of at
most 20 active individuals, invited or joined, with a current membership of the administrator role,
lowest id first, for a tenant's administrative notices; it checks no permission of the caller.

`AccessOptions` binds from `Tellma:Access` and is judged by `core.options`: every count positive,
`MaxFilterLength` within the 2048-character filter column, every span positive, `FastDenyWindow`
within `PermissionsMaxAge`, and `ActivityStampInterval` at least 60 seconds.

## The access write side

Every path that writes the security tables keeps one thing true: the tenant has an active, invited
or joined individual with a started, permanent membership of the administrator role. The users'
and roles' stacks give a refused write its field-level message first; the guards hold the line
inside the transaction whatever composed the write.

### The guards

`AccessGuards` (scoped `IAccessGuards`) joins a persist batch the tenant database created; any other
batch, a `RecordingBatch` included, is an `ArgumentException`. Each call adds its text once per batch
however often it is made:

- `ContributeLock` puts the security lock at the transaction's head, ahead of the version-tag guard
  and the caller re-check: `sp_getapplock 'tellma.access'`, exclusive, owned by the transaction,
  waiting at most `AccessOptions.SecurityLockTimeout` (5 seconds by default, rounded up to whole
  milliseconds). A deadlock victim (`Access.LockDeadlock`) and a timeout (`Access.LockTimeout`) are
  `THROW 50503`, which the executor retries as transient.
- `ContributeInvariants` appends three assertions after every write, each a `THROW 50422`:
  `Access.LastAdministrator` (no active, invited or joined individual holds a started administrator
  membership without an end), `Access.PublicRoleHasMembers` (a public role has a member) and
  `Access.AdministratorRoleDamaged` (the administrator role is inactive or public, or lacks its
  unfiltered `*` × `*` permission). A membership that ends later never stands in for a permanent
  one, so ending the last permanent membership is refused even while a time-bounded one remains.

The save, delete and action pipelines call both once every write is composed, when the stack's
tables or any statement's declared writes (`IDataBatch.WrittenTables`: a participant's, the
caller's `OnPersist`, an action's own) include `core.Users`, `Roles`, `RoleMemberships` or
`Permissions`, unless the context is the system's; the lock still runs first, at the transaction's
head. A guarded batch composed by hand that writes those tables calls them itself. Because every
security write holds the lock, the assertions, which read under the transaction's snapshot, can never
pass for two concurrent writes that together break them. A save's `50422` surfaces as a
`ValidationException` carrying the code at the root; a hand-composed run's surfaces as
`BatchAssertionFailedException` with the same `Code`.

Each guard error of a failed attempt, a retried lock timeout included, is counted
(`tellma.access.guards.triggered`, by `guard`: `last_administrator`, `public_role_members`,
`administrator_damaged`, `lock_timeout`, `lock_deadlock`) and logged (`GuardTriggered`, 3103, at
Warning, with the tenant, the user and the code). The guards see the errors through
`DataBatch.OnServerErrors`, an observer the executor calls with each failed attempt's server errors.

### The users' and roles' rules

`CoreFeature` stacks `User` on `UserService<TUser>` and `Role` on `RoleService<TRole>`, the standard
operations closed over the distribution's leaves. The users' service declares `IEnlists<TUser>`, for a
user's self-service writes. The rules are separate components of the two stacks, `UserAccessRules<TUser>`
and `RoleAccessRules<TRole>`, so a leaf's service cannot drop them; they validate deletes and the
built-in actions as well as saves.

| Code | The users' rules refuse |
|---|---|
| `Users.SystemUserImmutable` | a save, an action or a delete of the system user, row 1 |
| `Users.KindNotAllowed` | a payload naming the system kind |
| `Users.EmailNotAllowed` | an email on a user that is not an individual |
| `Users.EmailLockedAfterInvite` | a changed email once the user was invited |
| `RoleMemberships.EndsBeforeStart` | a membership ending at or before its start |
| `RoleMemberships.EndsInPast` | a new membership, or one whose window changed, that ended already |
| `RoleMemberships.Overlap` | two windows of one role that overlap; a permanent one overlaps every other |
| `Users.TooManyRoles` | more memberships that have not ended than `MaxRolesPerUser` (`max`) |
| `Users.CannotRemoveOwnAdministratorMembership` | the caller removing, ending or deferring their own current administrator membership |
| `Users.CannotDeactivateSelf` / `Users.CannotDeleteSelf` | the caller deactivating or deleting their own user |
| `Users.OnlyNewUsersDeletable` | a delete of a user past `New`, whom the audit columns may reference (it is deactivated instead) |
| `Users.MembershipEscalation` | a new membership, or one moved to another role or widened, of a role holding a permission the caller does not hold unrestricted (`resource`, `action`) |

| Code | The roles' rules refuse |
|---|---|
| `Permissions.UnknownSecurable` | a pair no securable registers; `(*, A)` when no resource registers `A` (`resource`, `action`) |
| `Permissions.FilterNotSupported` | a filter on `*` or on a securable without a filter root |
| `Permissions.FilterTooLong` | a filter beyond `MaxFilterLength` (`maxLength`) |
| `Permissions.FilterVersionUnsupported` | an unchanged filter whose stored stamp this engine does not compile (`version`, `minimum`) |
| `Permissions.FilterInvalid` | a blank filter, or one the engine does not validate against the tenant's current schema as a predicate over the root: one error per diagnostic, each with its `code` and arguments |
| `Permissions.WildcardResourceOnPublicRole` | a permission on `*` of a public role |
| `Roles.PublicRoleHasMembers` | a role saved as public that has members |
| `Roles.AdministratorImmutable` | the administrator role made public, deactivated, deleted or stripped of its `*` × `*` grant |
| `Roles.HasMembers` | a delete of a role that has members |
| `Roles.TooManyPermissions` | more permissions than `MaxPermissionsPerRole` (`max`) |
| `Permissions.EscalationBeyondSelf` | a new or changed permission the caller does not hold unrestricted; every permission of a role the save makes public |

A filter's stamp follows its text: a new row, a changed text or a missing stamp takes the running
language version, and unchanged text keeps the stamp it was affirmed under. The filter-root rule is
the drift classifier's, so a permission the rules accept never drifts on the deployment that saved it.

The escalation test (`AccessDelegation`) lets a caller hand out a pair only when their own set holds
an unrestricted grant, current now, that covers it: the pair itself, the resource's `*` action, the
action on `*`, or `*` × `*`; for `Read`, also any action on the resource, or any action on `*` the
resource registers. A filtered grant never qualifies. The system set, and a caller holding `*` × `*`,
are exempt. Self-protection refusals are counted with the guards (`self_deactivation`, `self_deletion`,
`own_administrator_membership`) and logged as `GuardTriggered`; an escalation is counted as
`escalation` and logged as `EscalationRejected` (3104, at Information, with the role and the pair).

Saving a user or a role is a sensitive securable, so it asks for the step-up bar.

### The users' service and its identity actions

`UserService<TUser>` takes its platform collaborators as one `UserServiceDependencies` bundle (the
identity-server client, the membership directory, the links, the language catalogue, the value sets,
the guarded batch runner, the securables, the evaluator, the access guards, the request context, the
write frames, the open write frame, the access options, the entity metadata, the query schemas, the
user connection, the email outbox, the localization negotiator, the string localizer factory, the
clock, the users' instruments and the logger factory), which `UsersRuntimeComposition` registers per scope, so a leaf's service derives with a
two-argument constructor. A save lower-cases the emails, trims the names and the mobile, and puts the
locale members in their canonical case; it then checks each new or changed locale member against what
the deployment offers and each new or changed contact detail against its format, so a stored value the
deployment no longer offers stays until it changes.

| Code | The users' service refuses |
|---|---|
| `Users.LanguageNotOffered` / `Users.RegionNotOffered` | a language or a region the catalogue does not offer |
| `Users.CalendarUnknown` / `Users.TimeZoneUnknown` | a calendar or a time zone outside its value set |
| `Users.ContactEmailInvalid` / `Users.MobileInvalid` | a contact email that is not one plain address, a mobile that is not E.164 |
| `Users.NotIndividual` / `Users.AlreadyJoined` / `Required` | an `invite` target that is not an individual, has joined, or has no email; a `notification-preferences` target that is not an individual |
| `Users.ChannelNotTestable` | a `me/test-notification` of the `inbox` channel, at `channel` |
| `Users.NotService` | an `issue-credentials` target that is not a service account |
| `Users.Inactive` | an inactive target of either action, or an account deactivated while its credentials were issued |
| `Users.CredentialsConcurrent` | an account whose client another request changed while its credentials were issued |

The two identity actions call the identity server between the action's round trips, with no
transaction open, once every validator has spoken (`ActionContext.ValidatedAsync`, which resumes the
method at the validation's quiet point without a round trip of its own, and answers false when an
error, an unconfirmed warning or the tenant's state will refuse the action, so the identity server is
never called for an action that will not persist):

- **`invite`** sends one invitation per target in request order (the user's language or the tenant's
  first, the name in that language's content slot, the tenant's link, and a lookup only on a sandbox),
  then appends one write-back statement to the persist batch: each answered user's account linked
  unless it holds another, `InvitedAt` and `InviteStatus` written on success, `LastInviteError`
  replaced, the row stamped, and every answered user's preferences tag moved. Users whose stored
  account differs from the one answered come back as `Users.SubjectMismatch`, untouched; a sandbox's
  unknown address or credential-less account is `Users.SandboxRequiresExistingIdentity`. Once the
  action committed, the users who gained an account are recorded in the membership directory. A
  prefix the identity server answered is recorded and raised as `partial-failure` with the ids never
  attempted. A recompose of the persist replays the write-back and never calls the identity server
  again, the welcome emails it enlisted included: their rows reference no blob, so the outbox's one
  validator judged nothing a moved permission set could change. Every answered user with a status whose loaded row holds no account or the one answered is
  sent a welcome email through the outbox with the action's frame, so it commits with the write-back:
  to their contact address, else their sign-in address, in their own negotiated language and content
  slot (`Email_UserAdded_Subject`, `_Text` and `_Html` in Core's server resources, naming the tenant,
  the caller and the tenant's link, and telling an invited or reinvited user to set up their sign-in
  first). A user whose address the outbox would refuse is skipped and logged as
  `WelcomeEmailSkipped`; the invite stands. The same users are noticed with `core.user.added` through
  the action's `Notify` (arguments `tenantName` and `actorName`, both every content slot; no target;
  the caller as actor, none for the platform; deduplicated by the type's key, so a re-invite before
  the user read the first adds none), one request per `MaxRecipientsPerRequest` users.
- **`issue-credentials`** (single target) creates a client, joins the account on its first
  credentials, links it to the client only while it is still active and still names the client it was
  loaded with (else `Users.Inactive` or `Users.CredentialsConcurrent`, and nothing commits), and
  answers the client id and its one-time secret, which no log or rendering carries. Once the action
  committed, the client is recorded and the previous one deleted; a persist that failed deletes the
  new one instead. One whose commit is unknown (a lost connection around the commit) reads the account
  back once: a row naming the new client committed, so the client is recorded and the previous one
  deleted, and the failure is still raised; any other row did not, so the new client is deleted. Only a
  failed read-back keeps it, logged as `ServiceAccountClientKept`.
- **`invitation-status`** reads the requested users under the caller's read decision in one round
  trip, omitting the users the caller cannot read, then asks the identity server for the delivery of
  the invitations it emailed.

After a committed `deactivate`, every session listener is told of each linked user, the membership
hints turn inactive and a service account's client is deleted; after `activate`, the hints turn
active. These effects, like the actions' own, run outside any transaction and log a failure
(`ServiceAccountDeleteFailed`, `ServiceAccountClientKept`, `UserAccessRevocationFailed`, `UserMembershipHintFailed`) rather than
throw. `UserAccessCriteria` grants every member `Read` of their own user row (`Id = me()`). Invites
are counted on `tellma.users.invites` (meter `Tellma.Core`, tag `outcome`: `Invited`, `Reinvited`,
`Active`, `SubjectMismatch`, `Error`, `NotAttempted`) and logged as `UsersInvited` or
`UsersInvitePartial`. A user delete's preferences, stamps and notification preferences leave by their
keys' cascade, which the delete declares among its writes as it does every key's `ON DELETE` action.

### The caller's self-service and the preference bags

Every member reaches their own row without a role grant, through the users' member-only actions:

| Action | Round trips | What it does |
|---|---|---|
| `me` | 1 | the caller's profile (from the connected user), preference bag, access summary (every registered securable decided over the set the round trip validated, with its `HasFilter`, problems and next boundary) and the four client tags, the summary under the composite permissions tag |
| `me/save` | 3 | loads the caller's row with every mapped column and the last modifier's names, refuses a moved stamp as a conflict before anything is enlisted (unless `Override`), copies the request's eight members onto the stored row with the memberships left out, and enlists it through the action's open frame, its paths without the row index, so the users' rules run and no `Save` grant is asked |
| `me/locale` | 1 | validates in memory (`Users.LocaleEmpty` at `language`, `Users.LocaleResetConflict` at `reset`, then the save's locale rules, each value stored as its set spells it), then one persist batch: the access guards' lock, the update of the caller's row (each member set, `NULL` for each reset; an unchanged value writes no row), the invariants and the stored values; a read-only tenant admits it |
| `me/preferences/set`, `me/preferences/delete` | 1 | checks the keys and values in memory (paths `items[i].key`, `keys[i]`), then the bag's statement with the key cap asserted inside it (`Users.TooManyPreferenceKeys` at the request), then the bag; the answer carries the caller's moved preferences tag; a read-only tenant admits them |
| `me/test-notification` | 0 | refuses the `inbox` (`Users.ChannelNotTestable`) and an unregistered channel (`Notifications.UnknownChannel`), both at `channel`; answers `NotConfigured` for a channel with no sender, else the sender's `SendTestAsync` from the connected user (a due connect prologue runs alone first); each answer and each unavailable transport is counted on `tellma.users.test_notifications` (tags `channel`, `outcome`: `Sent`, `NoContactAddress`, `NotConfigured`, `Unavailable`, and `Rejected` for a message a platform channel's transport refused outright, still answered as the dependency unavailable) |

`client/sync` (`Client/ClientSyncService`, member-only, idempotent, hidden from agents) is the one
call a browser tab revalidates a tenant's cached documents with. It takes the tags the tab holds
and runs one Read batch: the connect prologue, then the caller's preference rows. It answers the
tenant's facts (`TenantFacts`: id, category, state, live tenant, whether the hub runs) and the four
client tags always; the settings, as the bytes the settings cache serialized when it loaded them,
when the held `settings` tag differs; the envelope `me` answers when a held caller tag differs;
every list's tag when the held `entities` tag differs or is absent; and the held lists whose tags
moved. Tags compare ordinally and a held name the server does not know is ignored. It is a polling
route, so it never stamps the caller's activity. `me` and `client/sync` compose the envelope
through one internal helper, `MeEnvelope`.

An administrator reads and writes another user's bag under `core.User` × `Preferences` and its
filter: `preferences/get` (one round trip; a user the filter hides is not found) and the
single-target entity actions `preferences/set` and `preferences/delete` (two round trips, the
target's bag answered and its preferences tag moved, each write logged as
`UserPreferencesChangedByAdministrator` at Information). The same grant reads and writes another
individual's notification settings: `notification-preferences/get` (one round trip, the stored
deviations of registered types and channels) and the single-target `notification-preferences/save`
(two round trips: `NotificationPreferenceRules` at `Arguments.Preferences`, the target's stamp row
locked, the deviations replaced and the target's preferences tag moved, logged as
`UserPreferencesChangedByAdministrator` with the area `notification-preferences`); a service
account is refused with `Users.NotIndividual`. `UserActivityContributor<TUser>` produces
the users' `activity` details extra: each visible user's `LastActiveAt` from `core.UserStamps`,
joined to the read's admitted ids.

`RoleService<TRole>`'s preprocessing collapses a role's exact duplicate permissions (the same
resource, action and filter, compared ordinally) to one row before the rules count and stamp them,
since no unique index can hold them apart: a stored row survives over a new one, and a stored
duplicate is deleted by the save. A leaf that overrides `PreprocessAsync` calls the base to keep it.

`RoleService<TRole>` declares two details extras for the role page's one read (its platform
collaborators come as one `RoleServiceDependencies` bundle, registered by
`UsersRuntimeComposition`):

- **`members`**: one query over `core.RoleMembership` restricted to the visible roles on `RoleId`,
  each row's user under the caller's own `core.User` × `Read` filter (rebased through the
  membership's `User`), ended memberships left out, ordered by the user's name, at most
  `MaxMembers` (500) rows. Each row carries its `Id`, `RoleId`, `UserId`, `StartsAt`, `EndsAt`,
  `Notes` and the user's name columns the tenant declares, `Email`, `ImageId`, `IsActive` and
  `State`. A caller whose read of the users is denied gets no `members` extra and no statement for
  it.
- **`problems`**: computed from the permissions the read loads, one `PermissionProblem` per stored
  permission that grants nothing, in role then permission order, judged by the evaluator's own
  `PermissionClassifier`; an invalid filter carries its diagnostics located at clause `filters[0]`,
  or the save's `Permissions.FilterInvalid` when the engine gave none.

A delete by query runs no validation round, so the delete rules above would never see its targets:
the users' and the roles' stacks leave `DeleteByQuery` out of their operations, so neither projects
`delete-by-query` and their services refuse it before any round trip. Their rows are deleted by id.
The startup gate refuses any stack over a security table that keeps it, such as a distribution's
leaf that declares every operation again, and, on the same grounds, any stack whose service or a
validator judges its deletes.

A new user's email is where the invitation goes, so a save that changes it hands the user's
memberships to whoever holds the new address: every membership that has not ended is then tested
for escalation as if granted (`Users.MembershipEscalation`). A payload that omits the memberships
keeps the stored ones, whose roles' pairs load in a second round, and the refusal is reported at
`Email`.

### Stamps and preferences

`UserStampsCompanion<TUser>`, a persist effect of the users' stack, inserts the `core.UserStamps` row
of every user a save inserts, in the save's transaction: both tags set to the batch's bump value, no
activity and an empty inbox. A row already there is kept, so a re-run after a lost connection is safe.

`UserPreferenceStatements` writes a user's preference bag (`core.UserPreferences`) over
`UserPreferenceList` rows, each write one statement appended to a persist batch: `Set` updates changed
values and inserts absent keys, `Delete` removes the named keys, and `Replace` deletes the keys the
items do not name, then sets the rest. `Set` and `Replace` assert the key cap after their insert
(`Users.TooManyPreferenceKeys`, a `50422`), and every write of one bag holds its owner's stamp row
under an update lock to its commit, so two writes cannot pass the cap together. The caller's own bag
is written against the connect prologue's user, whose re-check holds that row from the batch's head;
another user's is locked on that user's row by the statement's first line and named in a one-row id
list, so the epilogue moves that user's `PreferencesTag` either way. Keys compare as the database compares them, ignoring case; a value
compares in binary, so a change of case alone is written. `Check` and `CheckKeys` judge the items in
memory first: the key grammar (dotted segments, the first led by a lower-case letter, at most 128
characters; `Users.PreferenceKeyInvalid`), a key named twice ignoring case
(`Users.PreferenceKeyDuplicate`), and a value beyond `MaxPreferenceValueBytes` of UTF-8
(`Users.PreferenceValueTooLarge`).

### The first administrator

`ITenantBootstrapper` (`TenantBootstrapper<TUser>`, scoped) makes a tenant's first individual
administrator through the users' stack, in the platform's own scope of the tenant, so the audit stamps,
the stamp row and the tag bumps come with it and the guards stand aside. A user scope is refused. The
email address is trimmed and lower-cased, as the users' stack stores it.

- **A new address** creates an individual with no invitation evidence, so its state reads `New` until
  it is invited, and one permanent administrator membership.
- **An existing user** keeps its state and its other roles, and gains the membership only when it
  lacks a started, permanent one. Every administrator membership that has not ended is replaced by one
  permanent row starting where the last ended one ended, so the windows never overlap. A deactivated
  user is never reactivated: the bootstrap fails with an `InvalidOperationException` naming the user
  and pointing to another address, whose administrator then decides whether its privileges come
  back (no administrator may remain to reactivate it).
- **A subject** is accepted in a Development environment only (`InvalidOperationException` before
  anything is written otherwise). It rides the same save: the bootstrap holds it in the scope's
  `DevelopmentSubjects`, and `DevelopmentSubjectsEffect<TUser>`, a persist effect of the users' stack,
  records it on the created user as an invitation would (the subject, the invitation's instant,
  `InviteStatus.Invited`).

`IDevelopmentSubjects` (the same scoped `DevelopmentSubjects`) is that hold as a public seam, for a
distribution's development sample data: in the platform's own scope of a Development environment, a
provisioning step holds a subject per email address (normalised as the stack stores it) around a
users' save, and every user the save creates under a held address is recorded the same way, so the
principals the development identity server seeds sign in and join with no invitation sent. A user
that exists already keeps its evidence; the step records the membership hints itself. Outside
Development, in a user's scope, or while a hold is open, `Hold` throws `InvalidOperationException`.

The bootstrap is idempotent on the address. The `core.bootstrap-administrator` provisioning step
(order 10, before `core.settings`) calls it with the administrator the run names, named by the address's
local part and preferring the distribution's first offered language. The step runner schedules it by
the run's administrator, not by the tenant's record: a run that names none, such as a migration,
neither runs nor records it (and logs `ProvisioningAdministratorNotBootstrapped` while no run ever
has), and a run that names one runs it whatever the tenant records. `provision --tenant <id>
--admin-email <address>` on an active or read-only tenant runs it alone, which is how a tenant
provisioned without an administrator, or left without one, gets one.

`ITenantAdministratorInviter` (`TenantAdministratorInviter<TUser>`, scoped, closed over the user leaf
by `UsersRuntimeComposition`) invites that administrator after the run's steps, in the platform's own
scope of the tenant, through the users' `invite` action, so the identity call, the write-back, the
`core.user.added` notice, the welcome email and the membership hint are the action's own. It runs in a
provisioning frame named `core.invite-administrator`, so a read-only tenant admits it as it admits the
bootstrap. Nobody is invited when the run names no address, when it names a subject (the Development
bootstrap bound one), or when the administrator has joined already (logged
`TenantAdministratorAlreadyJoined`). A host holding no service-client secret, which only an in-process
Development host may, cannot authenticate to the identity server: it logs
`TenantAdministratorNotInvited` (a warning naming `Tellma:Identity:ServiceClientSecret` and the
`provision --tenant <id> --admin-email <address>` that invites the administrator once the secret is
configured, with the tenant's values) and invites nobody; the tenant still turns active. An invitation the identity server refuses, a sandbox's `Users.SandboxRequiresExistingIdentity`
among them, throws `InvalidOperationException` with the refusal, which fails the tenant with its steps
recorded; a success logs `TenantAdministratorInvited` with the status.

### The drift scan

`IPermissionDriftScanner` (`PermissionDriftScanner`, scoped, in the platform's own scope of the tenant)
reads every stored permission with its role's code and visibility in one round trip, and classifies each
with `PermissionClassifier`, the classifier the permission sets are built with, against the running
deployment's securables and the tenant's current schema. It returns a `PermissionDriftItem` for every
permission that grants nothing: the permission, the role and its code, the problem, and the engine's
diagnostics. The same instance is the migrator's `ITenantDriftReport`, which prints one line per item
after a tenant's migrations, such as
`Permission 12 of role 4 (clerks) grants nothing: FilterInvalid; QX3001 property=Name2 entity=fx.Widget.`

## The identity-server client

`IdentityServerClient` is the distribution's client of its identity server's v1 management API:
the bulk invite, the bulk delivery status, and creating and deleting service accounts. It is one
singleton per host, tenant-agnostic, registered by `CoreFeature` for the web host and the migrator
alike. Its options, `IdentityServerClientOptions`, bind from `Tellma:Identity` (the section the web
host's sign-in options bind from too) and the platform settles them after binding: `ClientId` is
`<slug>-svc`, and in in-proc identity mode `Authority` is the in-proc engine's issuer,
`{Tellma:PublicOrigin}/id`, whatever the section says. `core.options` judges them: the authority
absolute, `https` outside Development, with no trailing slash, query, fragment or user information;
`ServiceClientSecret` set, except in in-proc Development, where the web host fills it with the secret
it generates for its boot (the one the in-proc engine seeds) and the migrator has none, so a call
there fails with `InvalidOperationException` before anything is sent.

Each exchange goes through the named `HttpClient` `Tellma.Identity`, under a 30-second timeout on the
injected `TimeProvider`, with `Accept: application/json` and `User-Agent: Tellma.Core/<version>`;
the routes are relative to the authority, its path kept. The factory's own request logging is
removed from that client: its structured header values would carry the bearer token verbatim. The
service token is a `client_credentials` grant for the `tellma_identity` scope with no `resource`
parameter, the secret posted in the form body; it is cached until 60 seconds before it expires (a
claimed lifetime beyond a day counts as a day). Callers without a fresh token await one shared
fetch, which runs under its own timeout whatever its callers' cancellation; its failure reaches every
one of them, and the next call starts a fresh fetch. One `401` discards that token (unless a
concurrent caller already replaced it), fetches one and retries the call once.

The bulk members send chunks of at most `IIdentityServerClient.MaxChunkSize` (1,000), one call each,
in input order. A chunk's answer is recorded only when it holds one result per item, each echoing its
item's address or subject, with a known status or state, and either a status with its subject or a
refusal. Before any chunk completed, a failure raises by cause: the server unreachable (a transport
failure, a `5xx` or the timeout) as `DependencyUnavailableException` (`identity`, retry after 1 s),
the server refusing the distribution (a token-endpoint `4xx`, a second `401`, any other `4xx`) or an
answer it cannot record as `InvalidOperationException`, and the caller's cancellation as itself.
After one, any failure or a cancellation answers the completed chunks' results, a prefix the caller
tells apart by its length, and logs `IdentityChunkFailed` at Warning.

Every call is counted on `tellma.identity.calls` and timed on `tellma.identity.call.duration`
(meter `Tellma.Core`, tags `identity.endpoint` ∈ `invite | delivery | service-account` and `outcome`
∈ `ok | partial | unavailable | error | cancelled`) and logged once as `IdentityCall` at Information;
each chunk is an `identity.invite` or `identity.delivery` activity tagged `identity.count` and
`identity.outcome`. No log, exception message or `ToString` carries a token, a secret, an
Authorization header, an address or a subject.

## Background jobs

The job contracts live in `Tellma.Core.Abstractions.Jobs`. Core binds `JobsOptions` from
`Tellma:Jobs` and validates it with `core.options`: the poll intervals between one and 30 seconds
(the maximum at least the minimum), at most 256 parallel batches, a drain timeout of at least a
second, a gap threshold from an hour to 100 years and retention periods of at least a day. Handler keys
share the notification type keys' grammar.

### The tables

`CoreFeature` contributes the queue and the scheduler's tables to every tenant model:

| Table | Holds |
|---|---|
| `core.Jobs` | One row per job, ids from `core.sq_Jobs`. A `Running` job holds a lease (`LeaseToken` and `LeaseExpiresAt`) and no other status does (`CK_Jobs_Lease`); a `PendingConfirmation` job holds what it waits for and no other status does (`CK_Jobs_PendingConfirmations`). Lock escalation is disabled on it, so claiming and updating many rows never escalates to a table lock that would stall the enqueues riding user saves. |
| `core.Schedules` | The schedules, system-versioned into `core.SchedulesHistory`, ids from `core.sq_Schedules`; a feature's built-in schedules are seeded into it under their declared ids. Deleting a schedule deletes its state and leaves its jobs without a schedule. |
| `core.ScheduleStates` | Each schedule's firing state and lease, one row per schedule. Deleting its last job leaves the state without one. |
| `core.JobWorkerState` | The single row (`Id = 1`, seeded) the worker's tick records. |

A job entity's `JobId` references `core.Jobs` and is cleared when the job row is deleted.

### Handlers, built-in schedules and retired keys

A feature registers `contribution.JobHandler<THandler>()`, declares
`contribution.BuiltInSchedule(id, handlerKey, cron, ...)` and retires a key with
`contribution.RetiredJobKey(key)`. Composition checks each handler's attribute and shape (one of
`IJobHandler` or `IEntityJobHandler<TEntity>`, concrete, a well-formed key outside the `core.` prefix
unless Core registers it, a batch size in range), each built-in's id (within the reserved band, outside
the platform's own), key, expression width, time zone and arguments, and
across features refuses a key registered twice, a built-in id or key declared twice, and a retired key
that is still registered or that a built-in names. The startup checks above then hold the handlers to
the realised model and the built-ins' expressions to Cronos. `JobHandlerRegistry` is the singleton
the queue and the later worker read: each key's handler type, options, entity leaf and owning feature.

### Enqueueing

`IJobQueue` (scoped) appends one statement to a caller's `Persist` or `Maintenance` batch, in `Auto`
or `Explicit` transaction mode (an autocommit batch is refused, so the jobs and the stamps commit
together): it takes a contiguous id range from `core.sq_Jobs`, inserts every job in request order
and, for an entity-backed handler, stamps each named row's `JobId` in one `UPDATE` of the owner
table; the statement returns the ids in request order. Before anything is appended every request is
checked: its key is registered here, its `Entity` matches the handler's shape, every row is of one
entity type, saved (a positive id) and named once. Whether a row may take a job is the database's
call, not the in-memory member's: the `UPDATE` skips a row whose stored `JobId` names a job that has
not ended (`Pending`, `Running`, `Held`, `PendingConfirmation`, read with `READCOMMITTEDLOCK` so a
concurrent enqueue's committed job is seen), and when it stamps fewer rows than the requests name, a
row being missing or held, the statement throws `50422` with `JobEnqueueCodes.OwnerUnavailable` and
the batch rolls back; a statement that finds no open transaction throws
`JobEnqueueCodes.OutsideTransaction`. `RunAsUserId` defaults to the request's, else the system user
in a system scope, else the scope's user. The scope's confirmed keys, the current W3C trace parent,
the label and the arguments (at most 64 KB of JSON) are stored with each job. Once the batch
commits, the in-memory rows take their job ids and this host's pollers for the tenant are woken
(`TenantPollerWake`); a batch that fails stores nothing and wakes nothing. `EnqueueAsync` does the
same in a `Persist` batch of its own.

### Leases, outcomes and the completion transaction

`JobStatements` holds the lease statements, each on the batch's internal channel with `core.Jobs`
declared as its one write, so no caller authority refuses it:

- **The claim** collects the rows its token already leases (so a re-run returns them once), then
  leases the room left of one key's due `Pending` or lapsed `Running` rows in `DueAt, Id` order
  (`READPAST, UPDLOCK, ROWLOCK`, a seek of `IX_Jobs_Available`), counting an attempt, and returns
  every row. An entity-backed handler's entity rows ride the same round trip.
- **The renewal** extends every lease of the tenant by token and writes the buffered progress; a
  row it does not return, or returns under another token, lost its claim, and the whole lease is
  lost. A returned row whose cancel was requested cancels its item alone. A row whose completion is
  being sent is no longer judged, whichever path completes it, and a lease whose rows are all
  completing is left out, so the renewal never waits on a completion's locks.
- **The append** writes a fenced checkpoint into a handler's own transaction.
- **The completion** records each job's outcome under the claim's token.

The append and the completion throw `50422` with `Job.LeaseLost` when a row is no longer leased
under the token, rolling back whatever their transaction carries.

`JobRunner` runs what a claim leased, in the tenant whose database the claim's batch leased it from:

- **Completions without the handler.** The rows no handler may run complete at once in the system
  scope: a requested cancel (`Jobs.Cancelled`, unless the handler commits in steps and has its
  entity, which then runs with the item's token cancelled), an attempt past the last
  (`Jobs.AttemptsExhausted`, or the earlier error), and a missing entity (`Jobs.EntityMissing`).
- **Partitions.** The others are partitioned by run-as user and confirmed keys, in claim order.
  Each partition runs under its own `process <key>` activity and log scope, in a job scope opened
  by `ITenantScopeFactory`. A run-as user who is no longer active fails its jobs with
  `Jobs.UserInactive`.
- **The completion batch.** It is composed before the handler runs: the connect prologue, then the
  completion statement, whose outcomes are bound once known. The handler's own statements follow it
  on `JobBatch.Batch`, and the groups of every write it enlisted with the job frame
  (`IOpenWriteHost`, persisted onto the same batch). Then come the notices, `job.changed` to each
  requester and the nudge of a job queued again at once, all in one round trip. Only the worker
  sends it: until then, executing `JobBatch.Batch` throws `InvalidOperationException`.
- **Outcomes.** They follow the handler's marks, a requested cancel, then how the invocation
  ended; a retry at the last allowed attempt fails instead. A lost lease records nothing.
- **Failures.** A failed guard retries every item at once in a completion of the outcomes alone.
  Any other failure discards the handler's work and records the outcomes the failure maps to, a
  constraint an enlisted group breaks failing every item with its translated error. A batch that
  could not be sent after the handler threw maps the items by what it threw (a stop, its own error,
  a validation). A failure that cannot be recorded leaves the lease to lapse.
- **Progress.** `IJobProgress.Report` buffers progress for the next renewal and makes the
  checkpoint current at once; `FlushAsync` runs that renewal now; `Append` writes a fenced
  checkpoint that counts only once its batch committed. `Append` refuses `JobBatch.Batch`: the
  completion statement runs first in it and releases the lease, and records the reported
  checkpoint itself.
- **Order.** `RunAsync` returns how each claimed job ended in claim order, whichever completion
  ended it.
- **Notices.** A failure raises `core.job.failed` and a wait for confirmation
  `core.job.pending-confirmation`, to the requester or else the tenant's administrators. A handler's
  `NotifyOnSuccess` requests are raised only for a job that succeeded.

### The worker

`JobWorker` is the host's one hosted service for jobs, composed unless `Tellma:Jobs:Enabled` is
configured `false` (the migrator always turns it off). It is also an `ITenantStateListener`, so it
follows the registry's snapshot on the catalog's refresh interval and at once on every state change:
one `TenantPoller` per `Active` or `ReadOnly` tenant, retired after its current poll when the tenant
leaves those states (logged as `JobPollerRetired`, its orphan sample dropped). The startup gate holds
`DrainTimeout` below the host's `HostOptions.ShutdownTimeout`.

- **The poll.** `JobPoll` sends one `Maintenance` round trip in the system scope, each statement
  committing on its own and the whole re-runnable: the gap check and hold, one claim per key with a
  free slot (on a read-only tenant only the keys whose handler `RunsWhenReadOnly`), tick step 1
  through `IScheduleTick` when the scheduler composes one, the backlog sample once a minute, the
  orphan sample and the retired-key cancel once an hour, and the heartbeat stamp last. The poll is
  one activity and each claim a `receive <key>` span under it. After it come `job.changed` to each
  claimed row's requester, the gap notices (`core.scheduler.gap` to the administrators,
  `core.job.held` to each requester of a held row) in persist batches of their own, the withdrawal of
  the retired rows' confirmation notices, and tick step 2.
- **The interval.** A full claim or a full tick polls again at once; something short of that waits
  `MinPollInterval`; nothing doubles the wait up to `MaxPollInterval`; a failed poll is logged
  (`JobPollFailed`, the tenant in the log scope) and waits the maximum. An enqueue on this instance
  nudges the poller through `TenantPollerWake`, coalesced to one wake. A poller's first poll waits a
  random part of `MinPollInterval`, and each wait is lengthened by a random part of a fifth of
  itself, so pollers started together do not poll in lock-step.
- **The slots.** `JobConcurrency` bounds each tenant's key to its `MaxConcurrency` and the
  instance to `MaxParallelBatches` batches; a key with no free slot is left out of the poll. A
  poll reserves only its keys' own slots: an instance slot is taken when a claim that leased rows
  starts, and only while one is free, so an idle poll never holds one and the bound always holds. A
  claim that leased rows when no slot was left (polls planned in one instant may lease more than the
  free slots) is released uncounted (`JobClaimReleased`), as a drain releases a partition no
  handler started. A key refused at its limit wakes its tenant when one of its own batches ends; a
  tenant refused an instance slot, at its plan or at its start, is woken when a batch frees one, one
  tenant per slot, first refused first. `JobDispatcher` runs each claim that leased rows on the
  thread pool, counting its rows on `tellma.jobs.in_flight`, and the releases there too.
- **The renewals.** `JobRenewalScheduler` renews each tenant's leases once its earliest is due, a
  fifth of the lease after its last renewal; a failed renewal is logged and waits an interval. A
  report whose percent jumped five points renews at once, at most once per job every five seconds.
  A handler still running a lease length after every item it still holds had its cancel requested
  is abandoned (`JobHandlerAbandoned`): its lease is no longer renewed and lapses. The schedule runs
  on the clock's monotonic timestamp, so a step of the wall clock never delays a renewal, and a
  check that throws is logged (`JobRenewalFailed`) and the next one runs. The worker's
  synchronisation with the tenants' snapshot runs the same way (`JobSynchroniseFailed`), and a poll
  whose instruments or log lines throw has already handed its claims over.
- **The drain.** `StoppingAsync` stops the pollers, cancels the host token, and waits up to
  `DrainTimeout` for the polls in flight and every batch. The handlers still running then keep their
  lease: the renewals stop, one final renewal per tenant binds every such lease to the host's
  shutdown timeout, and each handler is abandoned (`WorkerDrainTimedOut`). A flush or jump of a
  handler still running after its final renewal extends its lease only to the end that renewal set.
- **Telemetry.** Every `tellma.jobs.*` instrument and `tellma.schedules.gap_detected` are recorded
  on the `Tellma.Core` meter; `JobWorkerHeartbeat` is logged whenever a poll stamps a tenant, at most
  once a minute, for the log-based staleness alert under `infra/monitoring/jobs/`.

### The jobs' stack

`JobService` serves `core.Job`'s query and details, and four actions on the general action path,
each over the targets its first round loads, every target held to the action's precondition (a
refused target fails the whole call at its position):

| Action | Securable | Precondition | Effect |
|---|---|---|---|
| `retry` | `Retry` | `Failed` (`Jobs.NotRetryable`) | Pending, due now, attempts and error cleared. |
| `cancel` | `Cancel` | not ended (`Jobs.NotCancellable`) | A running job, or a pending one already flagged, is flagged and stops at its next renewal; a job waiting for confirmation, or one with a checkpoint, is queued flagged so its next claim records the stop; any other is cancelled at once. Destructive, and served on a read-only tenant. |
| `resume` | `Resume` | `Held` (`Jobs.NotHeld`) | Pending, due now, cancel flag and error cleared. |
| `confirm` | `Confirm` | `PendingConfirmation` (`Jobs.NotPendingConfirmation`), requested by the caller (`Jobs.NotInitiator`), every recorded key among the one to sixteen keys sent | The recorded keys join the job's confirmed keys; pending, due now, from its first attempt. Hidden from MCP. |

Each action's statement is fixed text that repeats its precondition, so a repeat after a commit
fails the precondition instead of acting twice. Every target's requester is told `job.changed`. A
resume or a cancel that ends a requester's last held job withdraws their `core.job.held`, and one
that ends the last held job of the tenant withdraws `core.scheduler.gap`; a confirm, and the cancel
of a job waiting for confirmation, withdraw its `core.job.pending-confirmation`. Retry, resume and
confirm wake this instance's poller once they commit. `JobAccessCriteria` gives every member the
self-scope `RequestedById = me()` on `Read`, `Cancel` and `Confirm`, so a member reads, stops and
confirms the jobs they requested without a role; a row that owns a job reads the job's related
projection (`Job.Status`) for any reader of that row.

`JobLabels` renders a job's display text in the reader's language: the job's own label with its
arguments (an `entity` argument that names an entity renders as its plural label), else the
handler's `JobHandler_<key>` resource when a shared resource declares it, else the handler key. The
`jobHandlers` section of the client metadata lists every registered handler, by key, with whether a user
schedule may name it, whether it commits in steps, its label key, the key itself as the fallback
only when no resource declares the label, and the row a succeeded job leaves.

### The platform's maintenance handlers

| Key | Built-in | Does |
|---|---|---|
| `core.job-retention` | id 1, `0 3 * * *` | In pages of 1,000 rows, each its own `Maintenance` round trip: first queues flagged every job that waited for confirmation longer than `FailedRetention` and withdraws its notice, then deletes the succeeded jobs that ended longer than `SucceededRetention` ago, then the failed and cancelled ones past `FailedRetention`. A job that has not ended is never deleted; the rows that referenced a deleted job (every job entity's `JobId`, the schedule states' `LastJobId`) lose it, and each delete page declares the tables those keys write, read from the model's foreign keys. |
| `core.blob-sweep` | id 4, `*/15 * * * *` | Deletes the objects and rows of staged blobs past their deadline and of released blobs, a batch of rows per round, then purges the store's incomplete artefacts (see the blobs' sweep above). |
| `core.blob-reconcile` | id 5, `0 2 * * 6` | Releases each kind's committed blobs that no owner row references, one `Maintenance` round trip per kind. |
| `core.tree-verify` | id 6, `0 1 * * 6` | Recounts every tree table's subtree counts with the tree statements' verify, one `Maintenance` round trip per table, repairing the drifted rows. |

All four run on a read-only tenant and report their progress after every page, round, kind or
table.

## Excel

`Excel/` holds the workbook features' runtime contracts and the engine that writes them. Workbooks
are SpreadsheetML packages written with `DocumentFormat.OpenXml` (MIT), forward-only:

- **The contracts.** `ExcelContext` (the request's tenant, deployment, instant, languages, culture,
  calendar, display zone, labels and catalogue), `ExcelColumnSpec` (a column's header, Queryex type,
  scale and path, with the property, role, language, referenced entity and key kind an entity export
  adds), `ExcelSheetPlan` and `ExcelWorkbookPlan` (an entity export: its sheets in order, the parent
  first and only first, and the manifest's header block), `ExcelTableSpec` and `ExcelTableRow` (a
  report's one-sheet table, its header rows, frozen columns and each row's style and indent),
  `IExcelExporter`, and `ImportCheckpointState`, the progress a background import resumes from.
- **The cell codec.** Values encode by their column's type, never by value: numbers and dates as
  number cells, text as inline strings (never shared strings, so memory holds no string table), an
  editable export's `long` and its decimals beyond 15 significant digits as text, a date before
  1 March 1900 as ISO 8601 text (the 1900 date system's phantom 29 February makes every earlier serial
  a day off), an editable `DateTimeOffset` as ISO 8601 text with its offset, and characters XML cannot
  hold as `_xHHHH_` escapes. Decoding goes by the mapped property's type: a serial in either date
  system (serial 60 and below refused), ISO 8601 text, the manifest calendar's and the request
  calendar's text, the invariant and localized boolean words, choices by stored value or label.
- **Number formats.** `#,##0` and `#,##0.00…` in the display shape, `0` and `0.00…` in the editable
  one; dates as the culture's short date pattern, cleaned as the calendar systems clean it, behind the
  locale tag `[$-CCLLLL]` (the Windows calendar id, none for Gregorian and `17` for Umm al-Qura; the
  culture's LCID, `0000` for a culture without one). A calendar without a Windows calendar id, such as
  the Ethiopian, writes its dates as text in the calendar's short rendering, and the manifest records
  the pattern the importer reads them back with. Each distinct format is one `numFmt` entry from id
  164, and each distinct style one `cellXfs` entry.
- **The writer.** `WorkbookWriter` streams an entity export: the parent sheet into the package as the
  row source's pages arrive, each child sheet through a scratch file of its own copied in afterwards,
  then the hidden, protected `_tellma` manifest, the styles and the workbook. A scratch file is
  unshared and deleted by the system on close on Windows; elsewhere it is owner-only (`0600`) and
  unlinked as soon as it is open, so a killed process leaves nothing. A failed or cancelled write lets
  go of every part and spool, and the failure that stopped it is the one thrown. Each data sheet has a
  bold header frozen below it, an auto filter named as Excel names it, the editable shape's
  validation lists and length checks, a hidden stamp column, and right-to-left views for a
  right-to-left culture. A by-query export's capped count is checked on the first page, before the
  destination is opened (the writer takes a factory for it) or any scratch file made, and the row
  past a sheet's cap is refused. A sheet or table wider than 16,384 columns is refused by its record.
  Sheet names are scrubbed of what Excel refuses and of what XML cannot carry, kept within 31
  characters, never `History`, and made unique ignoring case. `WriteTableAsync` writes a report's
  table: several bold header rows, the pane frozen below them and after the frozen columns, each row
  style's font, fill and border, and no manifest. File names are the stem's ASCII form and the
  request's local time (`Widgets 2026-11-16 0930.xlsx`), `report` when the stem keeps nothing, the
  stem cut so the name fits 255 characters.
- **The planner.** `ExportPlanner` plans the two entity workbooks. The display plan is one sheet of
  the columns `DisplayColumns` expanded: a navigation reads its target's display-name columns
  (`IDisplayNames`) and writes the display name in the reader's content slot, the slot whose tenant
  language is the request culture's language or one of its parents', else the first; a group anchor
  reads the content slot's twin with the first slot behind a blank one; any other path or expression
  is typed by `QueryexEngine.Validate` and written as itself; every select term is read once, and
  `FoldingRowSource` folds each row back to one value per column. A header the request leaves blank
  is the labels of the path joined by ` / `, or the expression as sent; a derived header that
  coincides with another column's, ignoring case, gets the expression in parentheses, while a header
  the request supplied stays as sent. The editable plan's parent
  sheet is `Id`, `Stamp` (from `ModifiedAt`, when the entity has it), then every property the import
  writes in declaration order (never one the server owns or derives, a blob reference, a JSON
  document, a binary, a hierarchy path or one excluded from Excel): a multilingual group as one
  column per tenant language, a foreign key as one reference column in the requested key, else the
  target's natural key, else its surrogate `Id`. Each selected collection, at every depth, is a sheet
  of its own led by the reference to its owner row, named by the parent's and the collections' labels
  joined by ` · ` with leading segments dropped while the name is longer than 31 characters, its
  query riding its owner's. A columns subset keeps the natural key's columns; an unknown column,
  reference key or collection is a `BadRequestException` at its place in the request.
- **The operations.** `ExcelOperations<TEntity>` is attached by the platform, first among its
  companions, to every stack that serves `Export` or `Import`, and projects `export` under `Export`
  and `export-for-import` under `Import`, each under the stack's `Read`; `ChildExportOperations<TChild>`
  is the `export` action of every exportable child query surface, under the child's `Read`. Both are
  scoped and built by their own factories over the stack's service or the child query service. The
  stack composition reaches them only through `IPlatformCompanions`, which the composition root fills
  with `ExcelCompanions`, so the pipeline names no Excel type. A
  request names its rows by `Ids` alone or by the grid's clauses (`ExportSources`): ids beside a
  clause, an empty id list outside the editable template, or named filters on a child surface are a
  `BadRequestException`, as are none or more than 100 display columns and a header over 255
  characters. `ExcelExports` writes the plan to a scratch file under `Tellma:ScratchPath`, opened only
  once the first page passes the cap, and hands the rewound file to the caller; a refusal at the
  parent sheet's synchronous cap (`MaxSynchronousExportRows`) names `export/start` or
  `export-for-import/start` as the `LimitExceededException`'s remedy. The exports count their rows,
  durations and refusals (`tellma.excel.*`) and log each completion and refusal. `ExcelComposition`
  binds `Tellma:Excel`, registers the exporter, the context builder from the request context and the
  export runner, and the two checks: `excel` warns about each server-owned property excluded from
  Excel, and `excel.deployment` refuses a scratch directory that does not exist or takes no file.
- **The import intake.** `inspect-import` (under the stack's `Save`, attached beside the exports when
  the stack serves `Import`) checks the request's sheet and column overrides by form, then resolves
  the caller's own staged `import-file` upload in the one round trip the connect prologue rides and
  copies it, bounded, to a delete-on-close scratch file. `WorkbookReader` reads that copy: the
  package's compressed size, its central directory (read by `ZipDirectory`, one disk, bounded entries
  and directory size), the declared uncompressed sum and its structure (content types, one workbook
  part, no external link or relationship) are checked before any sheet is parsed; every part is read
  through `BoundedPartStream`, which refuses bytes beyond the part's declared length and any run
  over 4 MiB from one `<` to the next (a CDATA section, comment or processing instruction counted
  whole), so every node a reader materialises is bounded; text is read in chunks and refused before
  it is read whole once it passes what a cell may hold; two sheets may not name one part; no reader processes a DTD, and each reader's name table
  (`BoundedNameTable`) refuses a part whose distinct names pass 1 MiB. Shared strings
  (`SharedStringStore`) are charged against a budget and spill to a scratch file beyond it, and their
  entry count is capped. Decoded cell text is charged against a per-row budget and one budget for
  the whole workbook, and a sheet's rows are counted, when its dimension cannot be trusted, by skimming each row
  for a filled cell without decoding its text. Every loop checks the inspection's cancellation.
  `ImportManifest` reads the hidden manifest sheet as untrusted, dropping any row it cannot use or
  whose field passes 255 characters, and stops its column table at a byte bound. `HeaderIndex` matches headers by every form an export writes them in, rendered in
  the reader's culture, each tenant language's and English, NFC-composed, white space collapsed and
  case ignored; a key two paths give matches neither and is offered as a suggestion. `ImportMapper`
  binds sheets (override, manifest, sheet names, then the first visible sheet when there is no
  manifest) and columns (override, manifest, header index) to `ImportPaths`, gates the identity
  columns of a file from another tenant or deployment, and answers the `ImportPlan`: each sheet's and
  column's status, the row keys, the totals and whether the import must run in the background. A
  header past 255 characters is kept cut and maps only by an override.
- **The records.** `core.Exports` and `core.Imports` record each workbook operation, each row its
  creator's alone (`ExportAccessCriteria`, `ImportAccessCriteria`), with its file a blob of the
  `export-file` or `import-file` kind and its job a `core.Jobs` row. Both stacks serve query, details
  and delete; an import whose job may still run cannot be deleted (`Imports.JobActive`).

## Layout

| Folder | Contents |
|---|---|
| `Composition/` | `AddTellma`, `TellmaBuilder`, `TellmaComposition.Validate`, `CoreFeature`, `IContributionRealizer<T>`, `RealizationContext`, `TellmaStartupGate`; internally the pipeline, the feature graph, the composition record the checks and later runtimes read, Core's model contributions, and the link builder with its stack-route source. |
| `Composition/Realizers/` | One realizer per item kind the platform understands: services, standalone table types, shape requirements, Core's model configurations, calendar systems, value sets, notification types and channels, client events, securables, entities, stack companions, stack components, routed API services, confirmations and provisioning steps. Each spec that adds an item kind adds its realizer to `ContributionRealizerSet.CreateBuiltIn`. |
| `Composition/Checks/` | The built-in startup checks above. |
| `Crud/` | Internally: the stack composition (each stack with its service, companions and components, closed over its leaf), the descriptor builder, `StackRegistry` (`IStackRegistry` and `IStackRouteSource`), the stacks' securable contributor, the platform's default service, the registrations `CrudComposition` makes, the action-method reader, and the entity pipeline with its factory, its operation frame and its instruments, and the reads of reference and lookup targets (`TargetReads`), and the seam the platform's companions attach through (`IPlatformCompanions`). |
| `Crud/Query/` | Internally: the query path of [Entity stacks](#the-entity-pipeline-and-the-query-path): the surfaces, the admission, the planner, the search, the argument discovery and binding, the diagnostics' locations and their wire form, the traversal rule, the query operation (with the rows of a tree by parent) and the child query service. |
| `Crud/ExcelRows/` | Internally: [the Excel row source](#the-excel-row-source): the operation, the admitted query tree, one round trip's composition and its chunks, and the row source with its pages and lookups. |
| `Crud/Details/` | Internally: the details reads of [Entity stacks](#the-entity-pipeline-and-the-query-path): the projections, the planner and its composition with the extras' name rules and the record permissions, and the details operation with the cached list. |
| `Crud/Checks/` | The stack checks of [Entity stacks](#entity-stacks). |
| `Api/` | `IRouteContracts` and `RouteContract`; internally the route model, the contract describer and fingerprint, and the contracts chunk's client-asset contributor ([The route model](#the-route-model)), and the Queryex discovery service with its admission ([Queryex discovery](#queryex-discovery)). |
| `Crud/Validation/` | Internally: the validation stage of [Entity stacks](#the-validation-stage): the context loader, the scheduler, the confirmations, the attribute walker, the save shape and their instruments. |
| `Crud/Save/` | Internally: [the save pipeline](#the-save-pipeline) up to its persist step: the operation, one save's run (its steps, its rounds and its ordered checks), the per-entity row rules, the payload's rows, the uniqueness probes the write host, and the run every write shares. |
| `Crud/Delete/` | Internally: [the deletes](#the-delete-and-action-pipelines): the operation and one delete's run (its round trip 1, its rounds and its persist). |
| `Crud/Actions/` | Internally: [the actions](#the-delete-and-action-pipelines): the operation, one action's run (its arguments, its rounds and its persist) and the method invoker. |
| `Data/` | `TellmaDbContext`, `UseTellmaSqlServer`, `TableTypeSweepScopes`; internally the model definition and factory, the platform's own tables and the entity-contract reflection. |
| `Data/Conventions/` | The model conventions above, the plugin that installs them, and the constraint names. |
| `Data/TableTypes/` | `TableTypeBinder`, its bindings and the store-type metadata. |
| `Data/Ids/` | `IdAllocator`, its per-tenant sequence buffers, the reservation hold that assigns and rewrites ids, and its log events. |
| `Data/Saves/` | The save emitter: the plan of a root entity's tables and columns, the payload sorted into new and existing rows, and the statement's text. |
| `Data/Batches/` | `ITenantDatabase`, `ITenantDatabaseFactory` and their implementations, `DataBatch` with its internal channel, the assembly and line map, the hole renderer, the result-set cursor, the executor and its log events. `DataOptions` and `DataCancellation` sit in `Data/`. |
| `Data/Metadata/` | `EntityMetadataProvider` and the builder that validates the model into it, the compiled accessors, the fingerprints, and the source the startup check and the host share. |
| `Tenancy/` | `CatalogDbContext`, `CatalogMigrations`, the options, `ITenantRegistry`, `ITenantConnectionFactory`, `ITenantConnectionProvider`, `ITenantCatalog`, `TenantInfo`, `TenantLocation`, `TenantRegistration`, `DatabaseCreation`, `IRequestContextHolder`, `ITenantAccessGuard`, `TenantAccessRequirement`; internally the registry and its refresh service, the catalog reader and statements, the location grammar, the connection composition, the state table, the membership directory, the request-context holder, the access guard, the scope factory, the sandbox context, the scope's connection provider, the log events and instruments. |
| `Tenancy/Catalog/` | The row classes the catalog model maps for its migrations. |
| `Blobs/` | `FileSystemBlobStore`, its options, default root and validator, `AddFileSystemBlobStore`, the store instruments and log events; internally the content-type sniffer and the file-name sanitiser. |
| `Calendars/` | `GregorianCalendarSystem`, `UmAlQuraCalendarSystem`, `EthiopianCalendarSystem`, `CalendarRegistry`; internally the Ethiopian and the extended Umm al-Qura framework calendars, the shared renderer and reader, the culture data and the date-pattern reader. |
| `Excel/` | `ExcelContext`, `ExcelColumnSpec`, `ExcelColumnRole`, `ExcelKeyKind`, `ExcelDateEncoding`, `ExcelSheetPlan`, `ExcelWorkbookPlan`, `ExcelTableSpec`, `ExcelTableRow`, `ExcelRowStyle`, `IExcelExporter`, `ImportCheckpointState`, `ExcelOperations<TEntity>`, `ChildExportOperations<TChild>`; internally the cell codec and its decoding records, the cell reader, the column encodings, the number formats, the stylesheet, the package, worksheet and workbook writers, the manifest's header block and same-source gate, the serial dates, the sheet and file names, the column letters, the XML escapes and the scratch files; the exporter, the planner, the display columns and their folding row source, the request's sources, the context builder, the export runner, the companions' attachment and factories, the instruments, the log events, the checks and the composition step. |
| `Caching/` | The versioned cache base, its keys and kinds; internally, the cacheable entities' store and façade, the version-tag registry, its table's row, the prelude and bump contributors, the bump plan, the snapshots, the mismatch policies, the migrator's seeder, the instruments and the log events. |
| `Jobs/` | Internally: the job tables' configurations and seeds, the handler, built-in-schedule and retired-key realizers with their cross-feature checks, `JobHandlerRegistry`, the `jobs.handlers` and `jobs.schedules` checks, the queue and its enqueue statement, the poller wake, the lease statements, the runner with its leases, renewal, progress, outcome rules, backoff, pending-confirmation record, notices and completion-failure translation, the job and system scopes, the `process` and `settle` activities and the log scope, the composition step that binds the jobs' options, and the log events; `JobService` with its action and retention statements, the self-scope criteria, the label renderer, the `jobHandlers` contributor, and the retention and tree-verify handlers. |
| `Access/` | Internally: the access tables' configurations and seeds, the user stamp and preference rows, the securable registry with its name grammar and feature contributor, the securables section of the client metadata, the permission set build: the classifier, the builder, the interner and the access instruments and logs; and the user connection: the connect prologue, the connect, permissions and profile caches, the connector, the guarded runner and the connect initializer; and the permission filter rules the role save and the Queryex discovery share. |
| `Identity/` | `IdentityServerClient`; internally its transport and token cache, the wire shapes and their source-generated JSON metadata, the options' settling and validator, the composition step that binds them and registers the named HTTP client, the instruments and the log events. |
| `Notifications/` | `NotificationTypeRegistry`, `NotificationChannelRegistry`, `ClientEventRegistry`, `NotificationsClientAssetContributor`, `ClientEventPublisher`, `NullClientEventPublisher`, `NotificationConfiguration` and `NotificationDeliveryArguments`; internally the notifier, its statements and the delivering channels, the email channel's sender, the renderer, the preference row, the metrics, the name grammars, the hub-less real-time status, the composition step and the log events. |
| `Outbox/` | `OutgoingEmailService`; internally the outbox, the stored address lists, the outgoing emails' model configuration, the sending job's handler and the composition step that binds the outbox's options. |
| `Sharing/` | Internally: the share tables' model configuration, a shareable's shape in the model, the bespoke access criteria and the shared-with-me filter, the shares extra, the sharing commands behind share, publish and transfer, the `core.shareables` check and the composition step that binds `Tellma:Sharing`. |
| `Reports/` | `ReportService`, the reports' stack service, and `ReportServiceDependencies`; internally the reports' model configuration, the default titles, the document's binding and expressions, the dashboards' null read-through and the composition step that binds `Tellma:Reports`. |
| `Client/` | Internally: the client entity model, its data types, the action arguments' fields and schema, the declarations writer and the client-asset contributor; and `ClientSyncService`, the `client/sync` route. |
| `Localization/` | `LanguageCatalog`, `LanguageDeclaration`, `LocalizationNegotiator`, `ValueSetRegistry`, `IcuStringLocalizerFactory`, `ConventionalStringKeys`; internally the catalogue's data, the declaration's resolution, the `Accept-Language` reader, the negotiated cultures, the pinned culture data with its reader, writer and generator source, the localizer's resource reader, ICU renderer, calendar formatter, argument reader and missing-key telemetry, the string-pack composer with its resource sources and client-asset contributor, the label provider, the label key chains, the named enums, the key conventions and the name humanizer. |
| `Resources/` | `Strings.resx`, Core's shared resources (English), with its `ar` and `am` satellites and the `Strings` marker; `ServerStrings.resx`, the server-only resources (the email notification's subject and bodies), with its `ar` satellite and its marker. |

## Tests

`test/core/Tellma.Core.Tests` (`dotnet test test/core/Tellma.Core.Tests`) runs without a database.
`test/core/Tellma.Core.IntegrationTests` (`Category=Integration`) runs the catalog migrations, the
write side, the registry, the membership hints and the tenant scope factory against SQL Server, and
runs batches against the data layer's fixture database (`test/shared/Tellma.Testing.Entities`), and
holds the job tables, the queue's enqueue statement, the lease statements, the jobs' actions and
the maintenance handlers, and jobs run end to end through `JobPollDriver` to SQL Server in `Jobs/`.
`test/core/Tellma.Core.Tests/Excel` holds every workbook the writer produces to the Open XML SDK's
Office 2019 schema validator, and the planner's, the display columns' and the requests' rules over
the fixture entities; `test/core/Tellma.Core.IntegrationTests/Excel` runs the synchronous exports
through the stack's row source against SQL Server.
`test/core/Tellma.Core.Tests` also runs the resource audit over Core's own resources
(`Localization/CoreResourceAuditTests`): a key of `Strings.resx` or `ServerStrings.resx` that no
convention derives and no literal in `src/` or `client/projects/` names (test support, harnesses,
fixtures and the showcase left out) fails it, unless it is
listed as pending with the reason it waits.
