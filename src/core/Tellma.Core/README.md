# Tellma.Core

The platform's runtime. Every distribution host references it; the web host, the migrator and any
worker compose it through the same composition method. It takes no ASP.NET Core dependency, so a
host without the web stack composes it unchanged, and its services are testable without an HTTP
context.

Today it holds the composition root, the tenant data model (`TellmaDbContext`, its conventions and
the table-type binder), the first piece of blob storage (the file-system store) and the three
calendar systems. The rest of data access, the CRUD pipeline, access control, settings, caching,
localization, the rest of blobs, Excel, jobs and notifications join it as namespaces.

## The composition root

```csharp
services.AddTellma("acme", configuration, environment, tellma => tellma
    .AddFeature<GlFeature>()
    .UseEntity<Center, MyCenter>());
```

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
| `core.deployment-identity` | the registered `DeploymentIdentity` names another application, or no `ISandboxContext` resolves | no |
| `core.globalization` | the process does not run ICU (invariant mode or Windows NLS) | no |
| `core.key-vault-literals` | a value under `Tellma:` is an unresolved `@Microsoft.KeyVault(` reference | yes |
| `core.public-origin` | `Tellma:PublicOrigin` is missing, relative, more than an origin (a trailing slash included, since derived URIs append `/…` to it), or not https outside Development | yes |
| `core.taxonomy` | in Development, the repository's `taxonomy.json` does not list the slug | no |
| `core.entity-shapes` | the leaf registered for an entity does not implement a shape another feature requires of it; the fix lists the members | no |
| `core.model-contributors` | a model configuration Core contributes is not one the tenant model can apply: a concrete entity type configuration, closed, or open over exactly the entity it configures with that parameter constrained to the replaceable default, and with a parameterless constructor | no |

Options a feature declares are validated by `core.options` rather than by the host's
`ValidateOnStart`, which would stop startup at the first failing options type before the gate could
report the other problems.

## The tenant model

`TellmaDbContext` (`Tellma.Core.Data`) is the platform's tenant EF context, and its model is built
from the composition alone. `AddTellma` registers it as a singleton `IModel`, built on first use
from a context with no connection, beside the `TableTypeBinder` over it.

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

A declaration the model cannot be built with (a reference to an entity absent from the model or
mapped twice, an index over a property the class lacks, a key neither `int` nor `long`, a missing
`core.Users`, `core.Jobs` or `core.Blobs`) fails the model with the class and the declaration named.

### The table-type binder

`TableTypeBinder` (`[TableTypeBinder]`) binds rows to table-valued parameters from the model's
table-type definitions: an entity's rows to its table's row image, a standalone class's to its
type. Each binding builds its `SqlMetaData` once, in the definition's column order (store type,
length, precision, scale; text with the platform collation's locale rather than the thread's
culture), and one compiled accessor per column, matched by name, that applies the model's value
conversion (so an enum binds by name). Records are created lazily and again on every enumeration,
so a retry re-sends the rows; an empty list binds as no value. Nothing else in the platform builds
a `SqlDataRecord`.

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
`ar-SA`, is switched to its Gregorian calendar on a private clone), the four styles' patterns
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

Culture data comes from the host's ICU, so two releases can differ in a pattern's punctuation or a
name's spelling (Windows' and Ubuntu's do, for `en-GB`'s full date and `am-ET`'s month names).

## Layout

| Folder | Contents |
|---|---|
| `Composition/` | `AddTellma`, `TellmaBuilder`, `TellmaComposition.Validate`, `CoreFeature`, `IContributionRealizer<T>`, `RealizationContext`, `TellmaStartupGate`; internally the pipeline, the feature graph, the composition record the checks and later runtimes read, and Core's model contributions. |
| `Composition/Realizers/` | One realizer per item kind the platform understands: services, standalone table types, shape requirements, Core's model configurations, calendar systems and entities. Each spec that adds an item kind adds its realizer to `ContributionRealizerSet.CreateBuiltIn`. |
| `Composition/Checks/` | The built-in startup checks above. |
| `Data/` | `TellmaDbContext`, `UseTellmaSqlServer`, `TableTypeSweepScopes`; internally the model definition and factory, the platform's own tables and the entity-contract reflection. |
| `Data/Conventions/` | The model conventions above, the plugin that installs them, and the constraint names. |
| `Data/TableTypes/` | `TableTypeBinder`, its bindings and the store-type metadata. |
| `Blobs/` | `FileSystemBlobStore`, its options, default root and validator, `AddFileSystemBlobStore`, the store instruments and log events; internally the content-type sniffer and the file-name sanitiser. |
| `Calendars/` | `GregorianCalendarSystem`, `UmAlQuraCalendarSystem`, `EthiopianCalendarSystem`, `CalendarRegistry`; internally the Ethiopian and the extended Umm al-Qura framework calendars, the shared renderer and reader, the culture data and the date-pattern reader. |
| `Resources/` | `Strings.resx`, Core's shared resources (English), with its `ar` and `am` satellites and the `Strings` marker; so far the month names of `uq` and `et`. |

## Tests

`test/core/Tellma.Core.Tests` (`dotnet test test/core/Tellma.Core.Tests`).
