# Spec: Tenant Settings, Localization, and the Version Cache

- **Author:** Ahmad Akra
- **Date:** 4 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every request a tenant makes needs three things before any business statement runs: the tenant's
configuration (its languages, calendars, time zone, display names, and the settings its packs
declare), the caller's culture and calendar for messages and formatting, and the assurance that
whatever the instance holds in memory — settings, permissions, preferences, reference lists — is
still what the database holds. This spec ships the machinery for all three: the two settings
tables and their typed read path, the language and calendar catalogues with per-request
negotiation, the ICU-backed resource pipeline, and the version-tag cache that keeps every
application instance fresh without a message bus.

The cache design is the load-bearing part. One application database per tenant is served by many
stateless instances, so an in-process cache is only safe when the database itself carries the
invalidation signal. That signal is a small non-temporal table of opaque tags, read at the head of
every round trip, after spec 0011's schema guard, and bumped as the last statement before every
commit — by the batch executor of spec 0011, from declarations on entity classes, never by service
code. A service author cannot forget to bump a tag because no service ever bumps one.

The spec builds on the frozen engine and conventions: spec 0008 fixes `today()` as the current
date in the tenant's zone and `TimeZone` as the backend zone name (spec 0008 §10.6, §13.1),
reserves the calendar codes `'gc'`, `'uq'`, `'et'` (spec 0008 §10.4), and keys its caches on schema
identity (spec 0008 §16); spec 0001 supplies the standalone table types and the reserved seed
band; spec 0007 supplies `DeploymentIdentity`. The batch abstraction, the entity contract, the
request context, the connect prologue, and the securables registry are defined by the concurrent
specs 0010, 0011, 0013 and 0014; this spec consumes them by contract name and restates only the
members it uses.

Two axes that are easy to conflate are kept apart throughout: the tenant's **content languages**
(at most three, deciding which `Name`, `Name2`, `Name3` column holds which language) and the
request's **culture** (any language the distribution ships strings for, deciding resource lookup
and formatting). An English-speaking auditor at an Amharic tenant reads English messages over
Amharic content. Likewise the **tenant time zone** (binding `today()`, a business fact) is kept
apart from the **display zone** (a per-request formatting choice).

The settings edit endpoint ships here as `SettingsService`, so that the settings row, its cache
and its save are one design; the user's own preferences (`core.UserPreferences`, the typed
preference columns on `core.Users`) belong to spec 0013, and the permission cache's value type to
spec 0013 as well — this spec supplies the cache primitive and the tags they validate against.

## Goals / Non-goals

**Goals**

- Ship `core.VersionTags`, the tag columns of `core.UserStamps`, the attributes that map written
  tables to tags, the registry, the prelude, guard and bump statements the executor emits, the
  per-kind mismatch policies, the wire form, and the migrator seed.
- Ship `VersionedCache<TKey, TValue>` — a private, bounded, single-flight, metered in-process cache
  validated by tags — and the four cache kinds built on it.
- Ship `[Cacheable]`: whole-table reference lists shared by every reader, loaded on the round trip
  the consumer already needed, with a per-entity tag the client caches on.
- Ship `core.Settings` and `core.SettingEntries`, `SettingKey<T>`, `TenantSettings` and its client
  view, the cold load that rides the connect round trip, and `SettingsService` with a field-masked
  save.
- Ship the language catalogue, the distribution's declaration, the tenant's validation, the
  `MultilingualShape` that keys the Queryex schema, `[Multilingual]` semantics and labels.
- Ship `ICalendarSystem` with `gc`, `uq`, `et`, calendar-aware formatting, the time-zone rules, and
  per-request negotiation of culture, calendar and display zone through the request context.
- Ship `Resources/Strings.resx` conventions and `IcuStringLocalizerFactory`.
- Ship the string pack: the deployment's shared resources as one JSON document per offered
  language, served tenantless to the SPA, which renders every server code and label itself.

**Non-goals (explicitly out of scope)**

- **The batch executor, emitter and analyzers** that emit this spec's statements (spec 0011).
- **The connect prologue, the permissions cache value and `core.UserPreferences`** (spec 0013).
- **The pipeline that declares dependencies, re-runs once, projects `[Cacheable]` and nulls gated
  twins** (spec 0014); **the routes, headers and problem mapping** on the wire (spec 0015).
- **Number-to-words and other language utilities, and fonts** — Locale packs, later. The SPA's own
  strings are the client workspace's; the server's reach it through the string pack (§10.4).
- **A per-tenant default culture, a pack-level language declaration, and the `uq` month-map engine
  amendment** — deferred; the surfaces below leave room for each.
- **Ethiopian calendar rendering in Excel number formats** — impossible in Excel; spec 0018 renders
  text.

## 1. Placement and architecture

### 1.1 Projects, namespaces, edges

Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are omitted,
so a block is never pasted into code. SQL statements are the exact shape to emit.

| Project | What this spec adds | Dependency edges |
|---|---|---|
| `Tellma.Core.Abstractions` | Namespaces `Tellma.Core.Abstractions.Caching` (tags, snapshot, registry, cache contracts, `CacheTelemetryNames`), `.Settings` (`SettingKey<T>`, `TenantSettings`, `TenantSettingsForClient`, `TenantSettingsDetails`, `SettingKeyDescriptor`, `SettingValueType`, `TenantSettingsPatch`, `MultilingualShape`, the entities `Settings` and `SettingEntry`), `.Localization` (`LanguageInfo`, `ILanguageCatalog`, `ILabelProvider`, `LocalizationContext`, `ILocalizationNegotiator`, `CoreStrings`, `IStringPackProvider`, `StringPack`, `LocalizationTelemetryNames`), `.Calendars` (`ICalendarSystem`, `ICalendarRegistry`, `CalendarCodes`, `DateStyle`); `VersionTagList` in `.TableTypes` | `Tellma.Core.Queryex` only (existing edge); BCL `System.Text.Json`, `System.Globalization` |
| `Tellma.Core` | Folders `Caching/` (`VersionedCache`, `VersionTagRegistry`, `VersionTagSnapshots`, `UserVersionTagSnapshots`, the prologue and epilogue contributors, `CacheableEntityStore`, `CacheableEntities`), `Settings/` (`TenantSettingsStore`, `TenantSettingsCache`, `SettingKeyRegistry`, `SettingsService`, the `core.settings` provisioning step), `Localization/` (`LanguageCatalog`, `LocalizationNegotiator`, `LabelProvider`, `IcuStringLocalizerFactory`, `StringPackComposer`), `Calendars/` (`GregorianCalendarSystem`, `UmAlQuraCalendarSystem`, `EthiopianCalendarSystem`, `CalendarRegistry`), `Resources/Strings.resx` and satellites | adds `MessageFormat` 8.0.0, `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Localization`, `Microsoft.AspNetCore.DataProtection.Abstractions` (`IDataProtectionProvider`, registered by spec 0010's web host; optional elsewhere, §5.3) |
| `Tellma.Core.Migrator` | Runs the `core.VersionTags` seed and wholesale bump (§2.8) on every `migrate` | unchanged |
| `test/core/Tellma.Core.Tests`, `test/core/Tellma.Core.IntegrationTests` | The suites of §12 | unchanged |
| `test/shared/Tellma.Testing.Resources` | The resource-audit helper of §12, referenced by every suite that owns a `Strings.resx` | new; BCL only |

Everything in `Tellma.Core.Abstractions` stays EF-free and framework-free; the one framework
type a contract names, `System.Text.Json.JsonElement`, is BCL. `IStringLocalizer` is not named in
Abstractions: a pack references `Microsoft.Extensions.Localization.Abstractions` itself and
addresses Core's strings through `CoreStrings` (§10.1).

### 1.2 Composition

`CoreFeature` (spec 0010's `ITellmaFeature`, added unconditionally) contributes, through spec
0010's `FeatureContribution`: the EF configurations of `Settings`, `SettingEntry`,
`core.VersionTags` and the tag columns of `core.UserStamps` (`Model<T>()`); `IVersionTagSnapshots`,
`IUserVersionTagSnapshots`, `IVersionTagRegistry`, `ISettingKeyRegistry`, `ILanguageCatalog`,
`ILocalizationNegotiator`, `ILabelProvider`, `IStringPackProvider`, `ICalendarRegistry`
(`Singleton<,>()`) and the two stores `TenantSettingsStore` and `CacheableEntityStore` as concrete
singletons; `ITenantSettingsCache` and `ICacheableEntities`, the façades over those stores that
hold the scope's `ITenantDatabase` and snapshot (§3.2), and the prelude and bump contributors of
§2.5 and the cold-load contributor of §5.7 as spec 0011's `IDataBatchContributor` (`Scoped<,>()`);
`IRequestContextInitializer` at `Order` 200 (§9.3); `ClientEvent("cache.changed")` (§2.5);
`Calendar<GregorianCalendarSystem>()`, `Calendar<UmAlQuraCalendarSystem>()`,
`Calendar<EthiopianCalendarSystem>()`; `ApiService<SettingsService>()`; `Securables(...)` for
`core.Settings.General` and every `core.Settings.<Category>` of §5.5;
`ProvisioningStep<SettingsProvisioningStep>()` (§5.7); and the `IStartupCheck`s of §1.3.

The distribution author's whole surface is two builder calls of spec 0010's `TellmaBuilder`,
`Languages(codes)` and `AddLanguage(info)`, plus two calls on spec 0010's `FeatureContribution` per
feature, `SettingKeys(declaringType)` and `Calendar<TCalendar>()`. `AddTellma` replaces the
framework's `IStringLocalizerFactory` with `IcuStringLocalizerFactory` (§10.2).

### 1.3 Startup checks

All report into spec 0010's realised gate as `CompositionProblem`s; none throws on its own.

- **Version-tag registry**: every name has one of the two forms of §2.1; `entity:` names come
  only from `[Cacheable]`; no two entities yield one `entity:` name; every `[BumpsUserVersionTag]`
  names an `int` property of its entity; every raw-SQL site the analyzer of spec 0011 flags for a
  user-level table declares `SqlOptions.UserIds`.
- **Cacheable entities**: the checks of §4.2.
- **Setting keys**: the grammar, uniqueness, type, `References` and `Secret` rules of §5.3.
- **Languages**: every declared code is in the catalogue or added by `AddLanguage`; the
  `Tellma.Core` satellite assembly of every declared non-English code is present; catalogue
  symbols are unique.
- **String pack**: no `Strings` key is declared by two composed assemblies (§10.4).
- **Calendars**: registered codes are unique, lower-case, ≤ 16 characters; `gc`, `uq`, `et`
  are present and agree with spec 0008's reserved codes.
- **Globalization mode**: the ICU requirement is spec 0010 §2.3's built-in `core.globalization`
  check, registered by `AddTellma` itself rather than contributed here.

## 2. Version tags

### 2.1 Vocabulary and names

A **version tag** is an opaque `Guid` compared for equality only, never ordered or indexed,
generated by the application (one fresh value per batch), stored in `uniqueidentifier` columns.
"Version tag" is the only word: *ETag* is reserved for HTTP validators on the blob endpoint,
*fingerprint* is reserved for content hashes (the securables registry's), and *version* alone
collides with the Queryex language version. The per-shape constant a client-facing DTO carries is
its **format version** (§2.7); it is never stored.

Tenant-level tags live in `core.VersionTags`, one row per name:

| Name | Bumped by writes to | Read by |
|---|---|---|
| `settings` | `core.Settings`, `core.SettingEntries` | the settings cache, schema-shape selection, negotiation |
| `permissions` | `core.Roles`, `core.Permissions` (spec 0013 declares); `SettingsService.Save` when a language column changed (§6.3) | the permissions cache, together with the user's `PermissionsTag` |
| `entities` | any `[Cacheable]` entity's table (composite) | the client: "some cached list changed" |
| `entity:<EntityName>` | that entity's table | the server list cache; the client's per-entity list |
| pack names | tables carrying `[BumpsVersionTag("<name>")]` | the pack's own caches |

User-level tags are two columns of `core.UserStamps` (§2.3): `PermissionsTag`, bumped by writes to
that user's `core.RoleMemberships` rows, and `PreferencesTag`, bumped by writes to the user's own
`core.Users` row, `core.UserPreferences` and `core.NotificationPreferences`. Permissions are
validated at **two levels**: a role or permission edit changes what every member may do and bumps
the tenant `permissions` tag; a membership edit changes one user and bumps that user's
`PermissionsTag`; a permissions cache entry is valid only when both match.

**Name grammar.** A name has one of two forms, both at most 128 characters
(`VersionTagNames.MaxNameLength`) and compared ordinally. A plain tenant-level name — the three
platform names and every pack name — is lower-case ASCII, `^[a-z][a-z0-9.-]*$`. An entity name
is the reserved prefix `entity:` followed verbatim by the entity's name, spec 0011's
`EntityMetadata.Name` (`entity:gl.Center`), produced only by `VersionTagNames.ForEntity`;
`[BumpsVersionTag("entity:…")]` written by hand fails the startup gate.

### 2.2 Contracts

```csharp
// Tellma.Core.Abstractions.Caching
public sealed record VersionTag(Guid Value)             // equality only; Guid.Empty = never read
{
    public static VersionTag None { get; }
    public static VersionTag New();                     // application-generated
    public string ToWire(int formatVersion);            // "{formatVersion}.{Value:N}"
}

public static class VersionTagNames
{
    public const string Settings = "settings";
    public const string Permissions = "permissions";
    public const string Entities = "entities";
    public const string EntityPrefix = "entity:";
    public const int MaxNameLength = 128;
    public static string ForEntity(string entityName);
}

public enum UserVersionTagNames { Permissions, Preferences }   // columns PermissionsTag, PreferencesTag on core.UserStamps

public sealed record VersionTagSnapshot(                // tenant-level names only
    int TenantId, IReadOnlyDictionary<string, VersionTag> Tags, DateTimeOffset ReadAtUtc)
{
    public VersionTag this[string name] { get; }        // None when absent
    public bool Has(string name);                       // false = the kind is uncacheable on this instance
}

public interface IVersionTagSnapshots
{
    VersionTagSnapshot Current(int tenantId);
    void Replace(VersionTagSnapshot snapshot);          // by the executor after every batch; tenant entries only
}

public sealed record UserVersionTagSnapshot(            // the caller's two core.UserStamps columns
    int TenantId, int UserId, VersionTag Preferences, VersionTag Permissions, DateTimeOffset ReadAtUtc);

public interface IUserVersionTagSnapshots
{
    UserVersionTagSnapshot? Current(int tenantId, int userId);   // null until the user's first connect on this instance
    void Replace(UserVersionTagSnapshot snapshot);      // by the executor after every batch that carried a connect prologue
}

public enum VersionTagMismatchPolicy { Rerun, Refresh }

public sealed record VersionTagDependency(
    string Name, VersionTag ExpectedTag, VersionTagMismatchPolicy OnMismatch);

public interface IVersionTagRegistry                    // built at composition from the EF model's attributes plus the platform's fixed rules for the two non-entity preference tables; validated at the startup gate
{
    IReadOnlyList<string> Names { get; }
    VersionTagEffects Resolve(IReadOnlySet<TableName> writtenTables);
}

public sealed record VersionTagEffects(
    IReadOnlyList<string> TenantNames, IReadOnlyList<UserVersionTagRule> UserRules);

public sealed record UserVersionTagRule(                // core.UserPreferences and core.NotificationPreferences -> Preferences, "UserId" are fixed rules
    TableName Table, UserVersionTagNames Column, string UserIdColumn);

// Tellma.Core.Abstractions.Entities (declared with spec 0011's entity contract; semantics here)
public sealed class BumpsVersionTagAttribute(string name) : Attribute;   // on entity class, repeatable, inherited

public sealed class BumpsUserVersionTagAttribute(
    UserVersionTagNames column, string userIdProperty = "UserId") : Attribute;   // on entity class, repeatable, inherited

public sealed class CacheableAttribute(int MaxRows = 0) : Attribute;   // on entity class, inherited; 0 = TellmaCacheOptions.EntityMaxRowsDefault

// Tellma.Core.Abstractions.TableTypes — [TableType] standalone shape beside IdList and StringList; physical [dbo].[VersionTagList_<hash8>]
public sealed class VersionTagList
{
    public string Name { get; set; }                    // nvarchar(128); key
    public Guid Tag { get; set; }                       // uniqueidentifier
    public bool Bumped { get; set; }                    // bit; 1 when the batch itself bumps the name (§2.5)
}
```

| Member | Meaning |
|---|---|
| `VersionTag.None` | The empty tag; matches no entry, so a comparison against it always loads. |
| `VersionTagSnapshot` | The tenant-level tags of one tenant as last read by a batch prelude, overlaid with that batch's own bumps; `ReadAtUtc` is the instance clock at the read. Holds no user-level entry. |
| `IVersionTagSnapshots` | A process-wide store bounded by `TellmaCacheOptions.TagSnapshots` entries (private `MemoryCache`, least-recently-used eviction); an absent tenant yields an empty snapshot, which is what makes every cache miss on a fresh instance. |
| `UserVersionTagSnapshot` | One user's `PreferencesTag` and `PermissionsTag` as the connect prologue's result set 0 read them (spec 0013 §7.3), overlaid with the batch's own bump of either (§2.5). |
| `IUserVersionTagSnapshots` | A process-wide store keyed `(tenantId, userId)`, bounded by `TellmaCacheOptions.PreferencesEntries` entries (private `MemoryCache`, least-recently-used eviction); filled only by the executor after a batch that carried a connect prologue. |
| `VersionTagDependency` | A cached input a batch composed; the executor guards writes and applies `OnMismatch` to reads (§2.6). |
| `IVersionTagRegistry.Resolve` | Maps the union of a batch's declared written tables to the tenant names to bump and the user-level rules that fired. `[Cacheable]` yields `entity:<Name>` and `entities`. |
| `UserVersionTagRule` | One user-level rule: rows written to `Table` bump `Column` for every distinct value of `UserIdColumn`. Attributes yield rules; `core.UserPreferences` and `core.NotificationPreferences` (not entities) have fixed rules. |

### 2.3 Tables

**`core.VersionTags`** — non-temporal; no audit; no sequence; no UDTT; not an entity; absent from
the Queryex schema and the CRUD stack.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Name` | `nvarchar(128)` | no | `PK_VersionTags` clustered | `settings`, `permissions`, `entities`, `entity:<Name>`, pack names |
| `Tag` | `uniqueidentifier` | no | `DF_VersionTags_Tag NEWID()` | application-generated on bump |

No index beyond the primary key: the prelude scans the one-page table, the guard seeks
`PK_VersionTags` by `Name` (§2.5), and tags are never range-compared. The table is seeded only by
the migrator (§2.8): no `HasData` row and no provisioning step, because only the registry of a
deployment knows the names it needs.

**`core.UserStamps`** — non-temporal; no UDTT; owned jointly with spec 0013 (which adds
`LastActiveAt`, and spec 0020 `InboxSeenAt`); exposed to Queryex read-only as `core.UserStamp`
(navigation `User`). The columns this spec owns:

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `UserId` | `int` | no | `PK_UserStamps` clustered; `FK_UserStamps_UserId → core.Users(Id) ON DELETE CASCADE` | one row per user, inserted with the user |
| `PermissionsTag` | `uniqueidentifier` | no | `DF_UserStamps_PermissionsTag NEWID()` | bumped by writes to the user's `RoleMemberships` |
| `PreferencesTag` | `uniqueidentifier` | no | `DF_UserStamps_PreferencesTag NEWID()` | bumped by writes to the user's own `Users` row, `UserPreferences`, `NotificationPreferences` |

`HasData` (spec 0013): `(1, NULL, 00000000-0000-0000-0000-000000000001,
00000000-0000-0000-0000-000000000002, NULL)`.

Tables that never bump anything and carry no tag attribute: `core.Jobs`, `core.UserStamps`,
`core.VersionTags`, `core.Blobs`, `core.UserPreferences`, `core.NotificationPreferences`,
`core.ScheduleStates`, `core.JobWorkerState`, `core.Notifications` (the two preference tables bump
user-level columns through fixed rules, never a tenant-level name).

### 2.4 Declarations the platform and its packs carry

| Entity | Attribute | Effect |
|---|---|---|
| `Settings`, `SettingEntry` | `[BumpsVersionTag("settings")]` | this spec |
| `Role`, `Permission` | `[BumpsVersionTag("permissions")]` | spec 0013 |
| `RoleMembership` | `[BumpsUserVersionTag(Permissions, "UserId")]` | spec 0013 |
| `User` | `[BumpsUserVersionTag(Preferences, "Id")]` | spec 0013 |
| any `[Cacheable]` entity | implied `entity:<Name>` + `entities` | §4 |
| `core.UserPreferences`, `core.NotificationPreferences` | fixed rule → `Preferences`, `UserId` | this spec's registry |

`Settings` declares `settings` only; the `permissions` bump on a language change is an explicit
`IDataBatch.BumpVersionTag("permissions")` call inside `SettingsService.Save` (§6.3), so that an
ordinary settings edit never sends every user down the cold path.

### 2.5 The statements

The executor of spec 0011 emits four statements from this spec; the contributors below hand it
the text and the parameters. Parameter names use the `@tm_` prefix, which is reserved for the
once-per-batch fixed text and never used by Queryex or by distribution SQL.

**The prelude** — `IDataBatchContributor` at `Order` 50, stage `Prologue`. It contributes the
statement to every tenant batch that carries no connect prologue (a batch composed outside spec
0013's `IGuardedBatchRunner`: a cache loader, a maintenance batch, the migrator); on a caller's
batch the connect prologue's result set 1 is this same statement, and the contributor reads that
result set instead of adding its own. Either way the rows are the first thing the round trip reads
after spec 0011's schema guard.

```sql
-- Prelude (prologue contributor at Order 50; the first statement after spec 0011's schema guard in every tenant batch; on caller batches the connect prologue reads it instead):
SELECT [Name], [Tag] FROM [core].[VersionTags];
```

**The guard** — emitted by the executor as the first statement after `BEGIN TRAN` in every
`Persist` batch on a caller's behalf. `@tm_expectedTags : VersionTagList` always holds
`permissions` and `settings`, with the tags the connect confirmed, plus every tenant-level
dependency the batch declared through `DependsOn`, each with its `ExpectedTag`, and `Bumped = 1`
on every name the batch itself bumps — known at composition from its written tables and
`BumpVersionTag` calls;
`@tm_UserId` and `@tm_ExpectedUserPermissionsTag` are the connect prologue's parameters (spec
0013). The guard seeks the expected rows (`FORCESEEK`), so no other row is locked. A row the batch
only reads is read under `REPEATABLEREAD`, which holds its shared lock to `COMMIT`: a concurrent
bump of it — a role save, a settings save — waits for this transaction instead of committing under
it, a wait bounded by one round trip and paid by the writers of those rows and by any persist
whose guard arrives while such a bump is queued behind them. A row the batch will bump is read
under `UPDLOCK`: two writers of one row — two role saves, two settings saves, two saves of a
cacheable entity whose validators read its own list — serialise on the update lock, and the second
fails the comparison and recomposes instead of deadlocking on the conversion from a shared to an
exclusive lock. An ordinary save takes no update lock, so no row serialises a tenant's saves.

```sql
-- Guard (first statement after BEGIN TRAN in every Persist batch on a caller's behalf; @tm_expectedTags : VersionTagList always holds 'permissions' and 'settings' plus declared dependencies,
-- Bumped = 1 on the names this batch bumps; FORCESEEK locks the expected rows only; REPEATABLEREAD holds the shared locks on the rows the batch only reads to COMMIT, UPDLOCK serialises the writers of a bumped row):
IF EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WITH (REPEATABLEREAD, ROWLOCK, FORCESEEK) JOIN @tm_expectedTags AS e ON e.[Name] = t.[Name] WHERE e.[Bumped] = 0 AND t.[Tag] <> e.[Tag])
   OR EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WITH (UPDLOCK, ROWLOCK, FORCESEEK) JOIN @tm_expectedTags AS e ON e.[Name] = t.[Name] WHERE e.[Bumped] = 1 AND t.[Tag] <> e.[Tag])
   OR EXISTS (SELECT 1 FROM [core].[UserStamps] AS s WITH (REPEATABLEREAD, ROWLOCK) WHERE s.[UserId] = @tm_UserId AND s.[PermissionsTag] <> @tm_ExpectedUserPermissionsTag)
    THROW 50412, N'StaleVersionTag', 1;
```

Spec 0013's caller re-check (`THROW 50401`) follows immediately; the business statements follow
that.

**The bump** — `IDataBatchContributor` at `Order` 50, stage `Epilogue`; contributed to every batch
whose `WrittenTables` resolve to at least one name or rule, or that called `BumpVersionTag`. It
is the last statement before `COMMIT`. `@tm_tag` is one `Guid` bound from C# for the whole batch,
so the writing instance stamps its own caches without a read-back. `@tm_tagNames : StringList`
holds the distinct tenant-level names (`entities` added whenever any `entity:*` name is present).
Each `@tm_userIds_<Column>` is a batch-local table the epilogue declares and fills by `INSERT …
SELECT DISTINCT … WHERE NOT EXISTS` from every source: the emitter's rows (the rule's
`UserIdColumn` read from both TVPs and from the deleted-children capture of every statement that
writes the table) and every raw statement's `SqlOptions.UserIds` table.

```sql
-- Bump (executor epilogue, last before COMMIT; @tm_tag one Guid per batch; @tm_tagNames : StringList = declared writes' names + BumpVersionTag(name) calls; 'entities' added when any 'entity:*' name is present):
UPDATE [core].[VersionTags] SET [Tag] = @tm_tag WHERE [Name] IN (SELECT [Id] FROM @tm_tagNames);
IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @tm_tagNames) THROW 50422, N'VersionTag.Missing', 1;   -- a registered name absent from the table is never silent; metered tellma.versiontags.missing
-- Only when a user-level rule fired; one statement per column named. @tm_userIds_<Column> : a batch-local id table the epilogue fills by UNION from every source:
-- the emitter's rows (the rule's UserIdColumn read from the TVPs) and every raw statement's SqlOptions.UserIds.
UPDATE u SET u.[PermissionsTag] = @tm_tag FROM [core].[UserStamps] AS u JOIN @tm_userIds_Permissions AS i ON i.[Id] = u.[UserId];
UPDATE u SET u.[PreferencesTag] = @tm_tag FROM [core].[UserStamps] AS u JOIN @tm_userIds_Preferences AS i ON i.[Id] = u.[UserId];
```

The declaration the epilogue emits ahead of the fill: `DECLARE @tm_userIds_Permissions TABLE ([Id]
int NOT NULL PRIMARY KEY);` (and the `Preferences` twin), so the join never multiplies rows.

**Rules the executor honours.**

- **Ordering is load-bearing.** Every cold load in a batch (settings, spec 0013's permission rows
  and profile, cacheable lists) is emitted after the prelude and stamped with the tag the prelude
  returned. A bump landing between the tag read and the rows makes the rows newer than their stamp,
  which the next prelude detects and reloads (convergent); the reverse order would stamp stale
  rows fresh until the next bump (stale forever).
- **The bump takes an exclusive lock** on each touched `VersionTags` row until commit; two
  concurrent saves of one cacheable table serialize on that row only for the trailing statements,
  never across business statements (there is no client I/O inside a batch) — unless both guards
  read the row, in which case the guard's update lock serialises them from the guard onward.
- **`UPDATE` only; a missing row never fails silently.** `THROW 50422 VersionTag.Missing` surfaces
  as spec 0014's `ValidationException` with that code, and is metered. A name absent from the
  *prelude* (`VersionTagSnapshot.Has(name) = false`) marks the cache kind behind it uncacheable on
  that instance: every request loads, `cache.outcome = uncached`, one `VersionTagRowMissing`
  Warning per name per process. A missing row costs performance, never correctness. There is no
  self-healing insert on the write path.
- **The post-batch snapshots need no read-back:** the executor overlays the bumped tenant names
  with `@tm_tag` on the prelude's rows and calls `IVersionTagSnapshots.Replace` with tenant entries
  only; `BatchOutcome.VersionTags` is that snapshot. On a batch that carried a connect prologue it
  builds the `UserVersionTagSnapshot` from the prologue's result set 0 (`PreferencesTag`,
  `UserPermissionsTag`; spec 0013 §7.3), overlays `@tm_tag` on whichever column a user-level rule
  bumped for the caller, and calls `IUserVersionTagSnapshots.Replace`;
  `BatchOutcome.UserVersionTags` is that snapshot, null on a batch without a connect prologue (a
  `Maintenance` batch, a `System` scope's batch). The response header (§2.7) carries post-bump
  values from both.
- **The bump publishes `cache.changed`.** For every bumped name that is `settings` or an
  `entity:<Name>`, the epilogue contributor calls spec 0020's
  `IClientEventPublisher.Publish(batch, ClientEvent("cache.changed", [], { tag: <name> }))` — the
  batch form fires after the commit, to every connected user of the tenant — so an open client
  refetches the list or the settings without waiting for its next response header. The
  `permissions` and user-level bumps publish nothing: the next request's guard handles them. The
  publisher's null default (spec 0020) publishes nothing.
- **Out-of-band writes** (a DBA script, a distribution's own `SqlConnection`) are documented for
  operators: run the wholesale bump of §2.8 afterwards, or the admin action
  `settings/refresh-caches` (§6.4).

### 2.6 Dependencies and mismatch policies

A batch that composed cached inputs declares them through `IDataBatch.DependsOn`. After a
`Read` or `Validate` batch the executor compares the prelude's tags with each expectation and
applies the declared policy; on a `Persist` batch every declared tenant-level name also enters the
guard, as `permissions` and `settings` always do.

| Dependency | Declared by | Policy | Behaviour |
|---|---|---|---|
| `permissions` and the user's `PermissionsTag` | spec 0013's connect prologue (always) | `Rerun` | The result is discarded, the permissions cache refreshes, and the batch re-runs **once** (spec 0013's `IGuardedBatchRunner`); a second mismatch surfaces as `StaleContextException` (§2.9) (503, `Retry-After: 1`) and is metered `stale.phase = exhausted`. Rows never reach a caller from a run whose permissions moved. |
| `settings` on a `Read` batch | every caller batch (a `TenantSettings` is always in use) | `Refresh` | The result is served — physical columns never disappear, so a query compiled under the old shape still executes; the fresh settings ride the same round trip (§5.7) and warm the cache, and the response carries the new tag. |
| `settings` on a `Validate` or `Persist` batch | every caller batch | `Rerun` | The connect prologue reports `SettingsStale` and fails its guard; the fresh settings ride the failed round trip, the runner applies them and re-runs this spec's initializer (§9.3), and the batch is recomposed once. A validator never decides under a setting that changed under it. |
| `PreferencesTag` | spec 0013's profile cache | `Refresh` | As settings. |
| `entity:<Name>` | `FromCache<TEntity>` on a `Read` batch | `Refresh` | The cached list is invalidated; the result came from the database anyway. |
| `entity:<Name>` | `FromCache<TEntity>` on a `Validate` batch; re-declared by spec 0014's persist batch through `DependsOn` | `Rerun` | A validator never accepts a row from a list that changed under it. |

The write guard leaves no window on the rows it holds: a bump of any expected name, tenant-level
or user-level, that would commit after the guard read waits for this transaction. What remains
is an input the batch used without declaring, which the two always-present names rule out; a
cached list read through `ICacheableEntities.GetAsync` in service code is undeclared unless the
service names it with `DependsOn`.

### 2.7 Wire form, format version, and the response header

Every client-facing cached shape carries a `FormatVersion` constant (initially `1`):
`TenantSettingsForClient.FormatVersion`, `CachedEntitySet.FormatVersion` (the list shape), and
spec 0013's `UserAccess.FormatVersion`, which spec 0015's `AccessSummary.FormatVersion` carries to
the client. The wire form of
a tag is `VersionTag.ToWire(formatVersion)` = `"{FormatVersion}.{Value:N}"`; the browser stores
DTOs keyed by that string and refetches when it changes for any reason. The database never stores
a format version; the in-process cache dies with the process and needs none.

Every authenticated response carries `Tellma-Version-Tags: settings=<wire>, permissions=<wire>,
preferences=<wire>, entities=<wire>`: spec 0015 writes `settings`, `permissions` and `entities`
from `BatchOutcome.VersionTags` and `preferences` from `BatchOutcome.UserVersionTags.Preferences`
under its `TellmaHeaders.VersionTags`. `entity:*` tags are deliberately absent from the header
(unbounded count); a changed `entities` composite makes the client fetch `settings/entity-tags`
(§6.2) and refetch only the lists whose tag moved. The same four wire tags travel in spec 0015's
`MeResult.Tags`. They double as a coarse activity signal — the tenant `permissions` tag changes
whenever any role changes — which is accepted and is never to be replaced by a counter.

**The discipline is enforced by a test, not remembered:** `Tellma.Core.Tests` holds a checked-in
snapshot of each DTO's serialized property tree (names and JSON kinds, from the source-generated
contract) paired with its `FormatVersion`; a change to the shape without a bump fails the build.

### 2.8 The migrator seed and wholesale bump

The migrator runs both statements on every `migrate` per tenant database, after applying
migrations — `@names : StringList` from `IVersionTagRegistry.Names`. Migrations (`HasData`,
`migrationBuilder.Sql`, reference-data steps outside the pipeline) are the one write path that
legitimately bypasses the executor, so every tag moves after every migration.

```sql
-- Migrator, every run per tenant database (@names : StringList from IVersionTagRegistry.Names), then wholesale after migrations. This is the only seed of core.VersionTags:
INSERT INTO [core].[VersionTags] ([Name], [Tag]) SELECT n.[Id], NEWID() FROM @names AS n
 WHERE NOT EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WHERE t.[Name] = n.[Id]);
UPDATE [core].[VersionTags] SET [Tag] = NEWID();
```

A distribution that deploys a new `[Cacheable]` entity or a new pack name therefore gets its row
on the next `migrate`; an instance running the new code against a database not yet migrated sees
`Has(name) = false` and runs uncacheable for that kind until then (§2.5).

### 2.9 Error numbers and exceptions

| Number | Code | Raised by | Surfaces as |
|---|---|---|---|
| `50412` | `StaleVersionTag` | the guard | spec 0013's runner re-connects cold and recomposes once, then `StaleContextException` (503, `stale-context`, `Retry-After: 1`) |
| `50422` | `VersionTag.Missing` | the bump | spec 0014's `ValidationException` with code `VersionTag.Missing`; metered |

`StaleContextException` is this spec's member of spec 0014's closed exception set (§14.1 of that
spec maps it at 503):

```csharp
// Tellma.Core.Abstractions.Errors
public sealed class StaleContextException(IReadOnlyList<VersionTagDependency> Dependencies)
    : TellmaException;                                  // 503 stale-context
```

`Dependencies` holds the dependencies the prologue reported stale; it is empty after a `50412`,
whose `THROW` names none. `TenantDatabaseMismatchException` (§5.7) is internal to `Tellma.Core` and
surfaces as a 500.

### 2.10 Round trips

The numbers spec 0014 must meet, for a warm instance: read 1; create 1; update 2; +1 on a cold or
stale path (the cold settings load, the cold connect, a `Rerun`); +2 when the in-transaction guard
fires (a cold re-connect and the recomposed persist; §2.9); a request that needs no other
statement still executes the prelude (or the connect prologue that absorbs it) alone — one page
read is the price of cross-instance freshness without a bus. No tag or setting is ever read in a
round trip of its own on a warm path.

## 3. The versioned cache

### 3.1 Contracts

```csharp
// Tellma.Core.Abstractions.Caching
public enum CacheOutcome { Hit, Miss, Stale, Oversized, Uncached }

public sealed record CacheResult<TValue>(TValue? Value, VersionTag Tag, CacheOutcome Outcome);   // Value null only when Outcome = Oversized (§4.4)

public sealed class TellmaCacheOptions                  // Tellma:Cache; ValidateOnStart
{
    public int SettingsEntries { get; set; } = 10000;
    public int PreferencesEntries { get; set; } = 50000;
    public int PermissionsEntries { get; set; } = 50000;
    public long EntityRows { get; set; } = 2000000;
    public int EntityMaxRowsDefault { get; set; } = 1000;
    public int EntityMaxRowsCeiling { get; set; } = 10000;
    public int TagSnapshots { get; set; } = 10000;
}

// Tellma.Core.Caching (runtime base: the singleton store of one kind — a private bounded MemoryCache, single-flight loads, meters — behind a scoped façade)
public abstract class VersionedCache<TKey, TValue>
{
    protected VersionedCache(string kind, long sizeLimit, IMeterFactory meters);
    public Task<CacheResult<TValue>> GetAsync(TKey key, VersionTag currentTag, ITenantDatabase database);
    public TValue? Peek(TKey key, VersionTag currentTag);   // the entry when present and its tag matches; never loads
    public void Set(TKey key, TValue value, VersionTag tag);
    public void Remove(TKey key);
    protected virtual Task<(TValue Value, VersionTag Tag, long Size)> LoadAsync(TKey key, ITenantDatabase database);   // one Read batch on the handle; tag from its prelude; the base throws NotSupportedException
    public void Dispose();
}
```

| Member | Meaning |
|---|---|
| `GetAsync` | Returns the entry when `entry.Tag == currentTag` (`Hit`); otherwise loads through `LoadAsync` on `database` — the caller's scoped handle, spec 0011's `ITenantDatabase`, because the store is a singleton and holds no scope — stores, and returns `Miss` (no entry) or `Stale` (an entry with another tag). `currentTag = VersionTag.None` always loads and, because the prelude of the load's own batch supplies the tag, the stored entry is stamped correctly even on a fresh instance. Returns `Uncached` — value loaded, nothing stored — when the tag `LoadAsync` returned is `VersionTag.None`: the load's own prelude found no `core.VersionTags` row for the kind's name (§2.5). Returns `Oversized` — no value, nothing stored — when the `entities` kind's probe overflows (§4.4). |
| `Peek` | The entry when present and its tag equals `currentTag`; otherwise `null`. Never loads: what `ITenantSettingsCache.Peek`, `ICacheableEntities.Peek` and spec 0013's evaluator within its deny window read through. |
| `Set` | Stores a value under the tag it is valid for: a save's new state under the batch's post-bump tag, so the writing instance is warm without a reload; spec 0013's prologue rows under the tags that prologue read. |
| `Remove` | Drops one entry when present; metered `cache.reason = removed`. Spec 0013's `Invalidate` and a refusing prologue. |
| `LoadAsync` | The loader of a kind that loads on a miss (`settings`, `entities`): exactly one `Read` batch created on the handle it was passed (`database.CreateBatch(Read)`), carrying no connect prologue, returning the value together with the tag that batch's prelude returned and the entry's size in the kind's unit. The kinds whose rows ride spec 0013's connect prologue (`permissions`, `preferences`) read through `Peek`, are fed by `Set`, never call `GetAsync`, and leave the base. |

### 3.2 Behaviour

- **Private `MemoryCache` per kind**, never the DI `IMemoryCache`: `SizeLimit` in the kind's own
  unit, `TrackStatistics = true`, entries `(Tag, Value)`. Compaction is the runtime's
  (priority, then least recently used), so idle tenants age out first.
- **Single flight.** A per-key guard (`ConcurrentDictionary<TKey, Lazy<Task<Entry>>>`, removed on
  completion) ensures one loader per key under any number of concurrent callers; a faulted loader
  is removed before its exception propagates, so the next caller retries.
- **No age limit.** The tag is the only invalidation channel. Settings and preferences are
  written only through the pipeline; a write that bypasses the executor — a script against a
  reference table — is repaired by `settings/refresh-caches` (§6.4) or the migrator's wholesale
  bump (§2.8), and a server-side reload would not move the tag the SPA caches on anyway.
  Permissions alone carry a backstop, spec 0013's `PermissionsMaxAge`, applied by the connect
  prologue rather than by the store.
- **Every key begins with the tenant id** because user ids are per-tenant integers and one subject
  belongs to several tenants.
- **Scoped façades, singleton stores.** `ITenantSettingsCache` and `ICacheableEntities` are
  scoped: each holds the scope's `ITenantDatabase` and its view of `IVersionTagSnapshots` and
  passes the handle to its store, the process-wide `VersionedCache`; a singleton never sees the
  ambient request context (spec 0010), so the store takes the handle as an argument.
- **Instances are shared and immutable.** A cached value is handed to every caller by reference;
  mutating one is a bug the value types prevent (`TenantSettings`, `CachedEntitySet`, spec 0013's
  `UserProfile` and `UserAccess` are immutable records with read-only collections).

| Kind | Key | Value | Size unit / limit | Owner of the value |
|---|---|---|---|---|
| `settings` | `(TenantId)` | `TenantSettings` + the pre-serialized `TenantSettingsForClient` bytes, one entry | entries / `SettingsEntries` | this spec |
| `preferences` | `(TenantId, UserId)` | `UserProfile` | entries / `PreferencesEntries` | spec 0013 |
| `permissions` | `(TenantId, UserId)` | `UserAccess`; valid when both `permissions` and `PermissionsTag` match | entries / `PermissionsEntries` | spec 0013 |
| `entities` | `(TenantId, EntityName)` | `CachedEntitySet<TEntity>` | rows / `EntityRows` | §4 |

Spec 0013's connect cache, keyed `(TenantId, Subject)`, is not a `VersionedCache` kind: it holds
the last `ConnectedUser` whose premises the prologue re-validates on every request. Limits come
from `Tellma:Cache`; a distribution changes a number in configuration, never in code.

## 4. Cacheable entities

### 4.1 Declaration

A distribution marks an entity cacheable with one attribute, `[Cacheable(MaxRows)]` (§2.2); an
unspecified `MaxRows` (`0`) resolves at startup to `TellmaCacheOptions.EntityMaxRowsDefault`
(1,000 unless configured), so the limit is a number of configuration, never of code. What
qualifies: small, read-mostly reference data read by pickers on most screens — countries,
currencies, units. Users, roles and anything with row-level security are not cacheable; their
display names reach the client through the details page's related-entity projection. Calendar
month names are resources, not entities.

What the declaration produces, with no further code:

1. **Tags.** `entity:<Name>` — the entity's name, spec 0011's `EntityMetadata.Name`
   (`entity:gl.Center`) — is registered with `IVersionTagRegistry` and seeded by the migrator;
   writes to the table bump it and the composite `entities`.
2. **Securable.** Spec 0014's `StackSecurableContributor` registers the entity's `Read`
   securable with `FilterRoot = null`: a cached list is shared by every reader, so a row-level
   filter on it could only be silently ignored. A caller must still hold `Read` on the resource.
3. **Projection.** Spec 0014 projects
   `EntityService<TEntity, TKey>.GetAllCachedAsync(DetailsRequest)` and spec 0015 the route
   `{resource-segment}/all` (body `AllRequest(Select, Include)`); the unfiltered `Read` is the
   only permission either checks.
4. **Server cache.** `ICacheableEntities.GetAsync<TEntity>()` in service code, or
   `IDataBatch.FromCache<TEntity>()` when the consumer is already composing a round trip
   (validation context, an import's natural-key resolution).

### 4.2 Startup checks

Reported into the realised gate: the entity is top-level (`TopLevelEntity<int>`; the key type is
`int` because `CachedEntitySet.ById` is keyed by `int`); it declares no child collection; the
resolved `MaxRows` (§4.1) is between 1 and `EntityMaxRowsCeiling` (10,000) — a "cacheable" type
above that is a configuration bug; and spec 0013's registry answers
`Find(resource, "Read").FilterRoot == null`.

### 4.3 Contracts

```csharp
// Tellma.Core.Abstractions.Caching
public sealed record CachedEntitySet<TEntity>(
    VersionTag Tag, DateTimeOffset LoadedAt, IReadOnlyList<TEntity> Items,
    IReadOnlyDictionary<int, TEntity> ById)
{
    public const int FormatVersion = 1;                 // the list shape's wire format version
}

public interface ICacheableEntities                    // scoped façade over the singleton CacheableEntityStore (§3.2)
{
    Task<CacheResult<CachedEntitySet<TEntity>>> GetAsync<TEntity>();
    CachedEntitySet<TEntity>? Peek<TEntity>();
    Task<IReadOnlyDictionary<string, string>> GetWireTagsAsync();   // entity name -> wire tag (settings/entity-tags)
}
```

| Member | Meaning |
|---|---|
| `GetAsync` | The `entities` kind's `GetAsync` under the snapshot's `entity:<Name>` tag; a miss runs one `Read` batch carrying the probe of §4.4; `Oversized` with a null `Value` when the probe overflows. |
| `Peek` | The current entry when present and its tag matches the snapshot; otherwise `null`. Never loads. |
| `GetWireTagsAsync` | For every `[Cacheable]` entity of the composition, `entity:<Name>` from the current snapshot as `ToWire(CachedEntitySet.FormatVersion)`; a name absent from the snapshot yields `VersionTag.None.ToWire(...)`, which never matches a client's stored value. Served by `settings/entity-tags` (§6.2). |

`Items` are in key order and include inactive rows (consumers filter); `ById` is the same rows by
`Id`.

### 4.4 Loading

The load is a capped probe: one `IDataBatch.Query<TEntity>` with `Select` = every scalar property
of the entity (no navigations), no `Filter`, `OrderBy = "Id"`, `Take = MaxRows + 1`, emitted after
the prelude. `FromCache<TEntity>` checks the cache first and appends the probe only on a miss; the
result populates the cache before the consumer's `BatchResult` resolves, so a cold lookup costs no
round trip of its own. On a `Validate` or `Persist` batch `FromCache` declares
`VersionTagDependency(entity:<Name>, tag, Rerun)`; on a `Read` batch, `Refresh` (§2.6).

**Overflow.** `MaxRows + 1` rows coming back is a signal, not a list: the rows are discarded,
nothing is cached, and `ICacheableEntities.GetAsync` and `FromCache` report `Outcome = Oversized`
with a null `Value`. The consumer re-reads the table through an ordinary query — spec 0014's
`GetAllCachedAsync` through one `Query<TEntity>` round trip (spec 0014 §5.5), a validator through
its loader (spec 0014 §7.2) — metered `cache.outcome = oversized`, with
`CacheableEntityOverflow(entity, rows, maxRows)` logged once per type per process. The table keeps
working uncached until an operator raises `MaxRows` or removes the attribute.

## 5. Tenant settings

### 5.1 Two tables, one criterion

`core.Settings` holds exactly one row of typed columns for the settings **the platform itself reads
to build every request's context**: the tenant-id guard, the tenant's multilingual display name,
the content languages, the calendars, the tenant time zone. It is closed to packs and distributions:
the entity `Settings` is sealed. `core.SettingEntries` holds one row per *set* key for **every other
setting**, declared in code as `SettingKey<T>` fields with a default, a category and a visibility; a
row exists only when the value departs from the default. The criterion is "read by the platform on
the request path" versus "read by a feature": the first set is validated against the platform's
catalogues (language membership, calendar registry, IANA zone mapping) — logic a key-value store
cannot express — and the second is open and validated against each key's declared type. Packs
cannot add columns to a Core-owned entity in any case (a leaf inherits from one base, and several
packs cannot all be that base), so pack settings are entries by construction.

Both entities carry `TopLevelEntity` (the four audit columns, `ModifiedAt` as the concurrency
token), `[Temporal]`, `[TableType]`, and `[BumpsVersionTag("settings")]`. There is no special
persistence path: the settings save hydrates, merges, validates and persists through spec 0011's
emitter like any entity, which is what makes audit stamping, the concurrency check, the history
row and the tag bump automatic. Neither is contributed as a stack; both are mapped leaves and
therefore roots of the Queryex schema under their entity names (`core.Settings`,
`core.SettingEntry`), reachable by reports later, with no endpoint of their own beyond
`SettingsService`.

### 5.2 Tables

**`core.Settings`** — single row; `[Temporal]` → `core.SettingsHistory`; UDTT `SettingsList`
(period columns excluded by derivation); carries `TopLevelEntity`; `[Multilingual]` on `Name`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | `PK_Settings`; `CK_Settings_Id CHECK ([Id] > 0)`; `CK_Settings_SingleRow CHECK ([Id] = 1)` | the sequence `core.sq_Settings` that spec 0011's convention emits for every keyed table exists and is never consumed: the single row is a `HasData` placeholder completed by provisioning (§5.7) |
| `TenantId` | `int` | no | | routing guard, never a filter; `0` = unprovisioned placeholder |
| `Name` | `nvarchar(255)` | no | | tenant display name, primary language |
| `Name2` | `nvarchar(255)` | yes | | |
| `Name3` | `nvarchar(255)` | yes | | |
| `PrimaryLanguage` | `varchar(35)` | no | | BCP 47 language tag ∈ the distribution's declared languages |
| `SecondaryLanguage` | `varchar(35)` | yes | `CK_Settings_Languages` | distinct from primary |
| `TertiaryLanguage` | `varchar(35)` | yes | `CK_Settings_Languages` | requires secondary; distinct |
| `PrimaryCalendar` | `varchar(16)` | no | `DF_Settings_PrimaryCalendar 'gc'` | ∈ the calendar registry |
| `SecondaryCalendar` | `varchar(16)` | yes | `CK_Settings_Calendars` | ≠ primary |
| `TimeZone` | `varchar(64)` | no | | IANA id, normalized |
| `CreatedAt` | `datetimeoffset(7)` | no | | server-owned |
| `CreatedById` | `int` | no | `FK_Settings_CreatedById → core.Users` | server-owned |
| `ModifiedAt` | `datetimeoffset(7)` | no | | concurrency token |
| `ModifiedById` | `int` | no | `FK_Settings_ModifiedById → core.Users` | server-owned |
| `ValidFrom` / `ValidTo` | `datetime2(7)` | no | `PERIOD FOR SYSTEM_TIME` | shadow |

```sql
CONSTRAINT [CK_Settings_Languages] CHECK (
    ([SecondaryLanguage] IS NULL OR [SecondaryLanguage] <> [PrimaryLanguage]) AND
    ([TertiaryLanguage] IS NULL OR ([SecondaryLanguage] IS NOT NULL
        AND [TertiaryLanguage] <> [PrimaryLanguage] AND [TertiaryLanguage] <> [SecondaryLanguage]))),
CONSTRAINT [CK_Settings_Calendars] CHECK ([SecondaryCalendar] IS NULL OR [SecondaryCalendar] <> [PrimaryCalendar])
-- WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [core].[SettingsHistory])); no index beyond the PK.
```

`HasData` (reserved band): `Id = 1`, `TenantId = 0`, `Name = N''`, `PrimaryLanguage = 'en'`,
`PrimaryCalendar = 'gc'`, `TimeZone = 'UTC'`, audit columns = the system user (`1`) at the
migration's fixed timestamp.

**`core.SettingEntries`** — `[Temporal]` → `core.SettingEntriesHistory`; UDTT `SettingEntriesList`;
sequence `core.sq_SettingEntries`; carries `TopLevelEntity`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | `PK_SettingEntries`, `CK_SettingEntries_Id CHECK ([Id] > 0)`, sequence `core.sq_SettingEntries` | |
| `Key` | `nvarchar(128)` | no | `UX_SettingEntries_Key` | `gl.posting.autoNumberOnPost`; `[Unique]` |
| `Value` | `nvarchar(max)` | no | | one JSON value; `[JsonColumn]` |
| `CreatedAt` | `datetimeoffset(7)` | no | | server-owned |
| `CreatedById` | `int` | no | `FK_SettingEntries_CreatedById → core.Users` | server-owned |
| `ModifiedAt` | `datetimeoffset(7)` | no | | concurrency token |
| `ModifiedById` | `int` | no | `FK_SettingEntries_ModifiedById → core.Users` | server-owned |
| `ValidFrom` / `ValidTo` | `datetime2(7)` | no | `PERIOD FOR SYSTEM_TIME` | shadow |

### 5.3 Setting keys

```csharp
// Tellma.Core.Abstractions.Settings
public enum SettingVisibility { Server, Client, Secret }   // Secret: server-only, protected at rest, never returned once stored

public enum MultilingualShape { Primary = 1, PrimaryAndSecondary = 2, All = 3 }   // the Queryex schema key

public abstract record SettingKey
{
    public string Name { get; }                         // dotted camelCase, ≥ 2 segments, ≤ 128 chars ("gl.posting.autoNumberOnPost")
    public string Category { get; }                     // default: first segment; securables core.Settings.<Category> × Read | Save
    public SettingVisibility Visibility { get; }
    public Type ValueType { get; }
    public Type? References { get; }                    // the top-level entity an id value points at; no foreign key
    public JsonElement DefaultJson { get; }
    public string LabelKey { get; }                     // "Setting_" + Name with '.' -> '_'
}

public sealed record SettingKey<T> : SettingKey         // declared once as a static field; registered through FeatureContribution.SettingKeys(typeof(Holder))
{
    public SettingKey(
        string name, T defaultValue, string? category = null,
        SettingVisibility visibility = SettingVisibility.Server,
        Func<T, string?>? validate = null, JsonTypeInfo<T>? typeInfo = null, Type? references = null);
    public T DefaultValue { get; }
    public Func<T, string?>? Validate { get; }
}

public interface ISettingKeyRegistry
{
    IReadOnlyList<SettingKey> Keys { get; }
    SettingKey? Find(string name);
    IReadOnlyList<string> Categories { get; }
}
```

Illustration (the whole distribution-side cost of two settings, as two public static fields of a
holder type `GlSettings`):

```csharp
public static readonly SettingKey<bool> AutoNumberOnPost = new("gl.posting.autoNumberOnPost", true, visibility: SettingVisibility.Client);
public static readonly SettingKey<int?> DefaultCenterId = new("gl.posting.defaultCenterId", null, references: typeof(Center));
```

registered once in the feature's contribution as `contribution.SettingKeys(typeof(GlSettings))`.
The registry collects every public static field of type `SettingKey<T>` on each registered type,
in declaration order.

| Rule | Enforced |
|---|---|
| Name grammar `^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)+$`, ≤ 128 characters | startup gate |
| Uniqueness across the composition (ordinal) | startup gate |
| `T` ∈ `bool`, `int`, `long`, `decimal`, `string`, `DateOnly`, `TimeOnly`, `Guid`, enums (as strings), their nullable and array forms — or the key supplies a `JsonTypeInfo<T>` from the declaring package's own source-generated context (the platform's context cannot know a pack's record type) | startup gate |
| `Category` is a single grammar segment; `general` (ordinal-ignore-case) shares the typed row's securables `core.Settings.General` | startup gate |
| `References` names a stack entity of spec 0014's `IStackRegistry` (a base resolves to its leaf through spec 0011's `IEntityMetadataProvider.Get`, so `typeof(Center)` stands for a distribution's leaf) keyed by `int`, and `T` ∈ `int`, `int?`, `int[]`; the value is never checked against the target table, so a deleted target leaves an id the editor shows bare | startup gate; documented |
| `Secret` and `References` are exclusive | startup gate |
| A `Secret` key's row holds, as a JSON string, the Data Protection ciphertext of its JSON (`IDataProtectionProvider.CreateProtector("Tellma.Core.Settings.Secret", <the tenant id's invariant decimal string>)`), written by `save` (§6.3) and unprotected on the first `Get<T>` — never on load, so the migrator and a process without the key ring never touch it; a value that fails to unprotect is treated as the default, logged `SettingsSecretUnreadable(key)` Critical and metered `tellma.settings.entries.invalid`, which is what a clone, or a restore under another tenant id, key ring or application name (spec 0010), yields; the history table holds ciphertext only. `TenantSettingsStore` takes `IDataProtectionProvider?`: a host without one reads every `Secret` key as its default with the same event, and a host whose job handlers read `Secret` keys registers Data Protection with the web host's application name and key ring (spec 0010) | `save`; `Get<T>` |
| `Validate` returns a dotted PascalCase validation code or `null` | documented; a thrown exception in the delegate is a 500 |
| A stored value that fails to parse as `T` or fails `Validate` is treated as the default, logged `SettingsEntryInvalid(key)` and metered `tellma.settings.entries.invalid`; an undeclared key in the table is `SettingsEntryUndeclared(key)` (Warning) and ignored | load path (`Get<T>` for a `Secret` key) |

`ValueType` is `typeof(T)`; `DefaultJson` is the default serialized once at construction.

### 5.4 Labels

`ILabelProvider.SettingLabel(key)` resolves `LabelKey` (`Setting_gl_posting_autoNumberOnPost`) in
the declaring assembly's `Strings` (§10.1) under the request culture, falling back to the humanized
last segment (`Auto number on post`). The label serves Excel and MCP; the settings page renders
the same key from the string pack (§10.4).

### 5.5 Securables

`CoreFeature` registers, through spec 0013's `SecurableRegistryBuilder`, two actions per settings
resource: `core.Settings.General` × `Read` and × `Save` (`FilterRoot = null`; `Save` sensitive —
step-up per spec 0013) for the typed row and `settings/refresh-caches`; and
`core.Settings.<Category>` × `Read` and × `Save` (`FilterRoot = null`, not sensitive) per distinct
`ISettingKeyRegistry.Categories` entry other than `general`. `Category` is written verbatim into
the resource name (`core.Settings.gl`); comparison is ordinal-ignore-case, as for every
securable. `Read` on a
category — which a `Save` grant satisfies (spec 0013) — admits it to `settings/details` (§6.2);
`Save` admits it to `settings/save` (§6.3), whose step-up for the typed row is asserted in code.
`settings/client` needs no securable: every member reads it, and it carries
`Client`-visible keys only. `Server` keys leave the server only through `details`; `Secret` keys
never leave it.

### 5.6 The resolved views and the cache

```csharp
// Tellma.Core.Abstractions.Settings
public sealed record TenantSettings                     // immutable; cached under the settings tag
{
    public int TenantId { get; init; }
    public VersionTag Tag { get; init; }
    public IReadOnlyList<string?> Names { get; init; }
    public IReadOnlyList<LanguageInfo> Languages { get; init; }
    public MultilingualShape Shape { get; init; }
    public IReadOnlyList<ICalendarSystem> Calendars { get; init; }
    public TimeZoneInfo TimeZone { get; init; }
    public string SqlServerTimeZoneName { get; init; }
    public DateTimeOffset ModifiedAt { get; init; }
    public IReadOnlyDictionary<string, JsonElement> Entries { get; init; }
    public Func<string, string?>? Unprotect { get; init; }   // bound by the loader to the tenant-scoped protector; null when the host has none
    public T Get<T>(SettingKey<T> key);
    public DateOnly Today(TimeProvider clock);
}

public sealed record TenantSettingsForClient(
    int TenantId, IReadOnlyList<string?> Names, IReadOnlyList<LanguageInfo> Languages,
    IReadOnlyList<string> Calendars, string TimeZone, IReadOnlyDictionary<string, JsonElement> Entries)
{
    public const int FormatVersion = 1;
}

public interface ITenantSettingsCache                  // scoped façade over the singleton TenantSettingsStore (§3.2)
{
    Task<CacheResult<TenantSettings>> GetAsync();       // every member is keyed on the scope's tenant
    Task<(byte[] Json, string WireTag)> GetForClientAsync();
    TenantSettings? Peek();
}
```

| Member | Meaning |
|---|---|
| `Names` | `[Name, Name2, Name3]` by language position 1..3. |
| `Languages` | One to three `LanguageInfo` in position order, resolved against `ILanguageCatalog`. |
| `Shape` | `Languages.Count` as `MultilingualShape`; the Queryex schema key (§7.4). |
| `Calendars` | One or two `ICalendarSystem`, primary first. |
| `TimeZone` / `SqlServerTimeZoneName` | The tenant zone as `TimeZoneInfo`, and the Windows id (`TimeZoneInfo.TryConvertIanaIdToWindowsId`, computed once per load) that spec 0011 binds to spec 0008's `TimeZone` slot. |
| `Entries` | Stored values by key, undeclared keys dropped; a `Secret` key's value as stored, protected. |
| `Get<T>` | The stored value parsed as `T`, else `DefaultValue`; a `Secret` value first run through `Unprotect`, which the loader binds to the tenant-scoped protector (§5.3); parsed once and memoized per entry. |
| `Today` | `clock.GetUtcNow()` converted to `TimeZone`, date part — the value spec 0011 binds to `today()`. |
| `TenantSettingsForClient` | Everything an authenticated member may know: names; languages (code, symbol, native name, direction); calendar codes; the IANA zone id; `Client`-visible entries, stored or default (the client never learns which), never a `Server` or `Secret` value and no related entities (§6.2). Materialized as pre-serialized UTF-8 JSON beside `TenantSettings`; `settings/client` writes the bytes and never serializes on the hot path. |
| `GetAsync` | `settings` kind: compares the snapshot's `settings` tag with the entry; a hit returns the instance with no round trip; a miss single-flight loads (§5.7). |
| `Peek` | The current entry when present and matching the snapshot; never loads. |

**Read path.** Spec 0010's request-context initializer chain calls `GetAsync()` after the
connect initializer (spec 0013, `Order` 100) and before negotiation (`Order` 200, §9.3); the value
lands on `RequestContext.TenantSettings`. On a cold connect the load rides the connect round trip
(§5.7); on a warm connect with an evicted entry, or one a loader batch's prelude outdated, it
costs one `Read` batch of its own — the "+1 cold" of §2.10. A stale entry a caller batch's
prologue discovers is refreshed by that same round trip (§5.7).

### 5.7 Cold load, the tenant-id guard, and provisioning

**Cold load** — `IDataBatchContributor` at `Order` 60, stage `Prologue`. On a caller's batch the
connect prologue of spec 0013 absorbs it, as it absorbs the prelude: the contributor adds no
statement of its own but hands the prologue its two statements and its parameter
`@tm_LoadSettings` — bound `1` when `ITenantSettingsCache.Peek()` is `null` or its tag differs
from the snapshot's at composition — and reads its two result sets from the prologue's result,
where they follow set 3 under `IF @tm_SettingsStale = 1 OR @tm_LoadSettings = 1`. The fresh rows
therefore arrive in the round trip that discovers the stale tag — the failed one on a `Validate`
or `Persist` batch, the served one on a `Read` batch (§2.6). On a loader's batch, which carries
no connect prologue, the contributor emits the statements itself, only when the cache missed,
bare after the prelude. Either way the prelude's tag read precedes the rows.

```sql
-- Cold settings load (prologue contributor at Order 60, after the tag read; on a caller batch under IF @tm_SettingsStale = 1 OR @tm_LoadSettings = 1; the loader asserts TenantId = the routed tenant, else a 500)
SELECT [Id], [TenantId], [Name], [Name2], [Name3], [PrimaryLanguage], [SecondaryLanguage], [TertiaryLanguage],
       [PrimaryCalendar], [SecondaryCalendar], [TimeZone], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]
  FROM [core].[Settings];
SELECT [Key], [Value] FROM [core].[SettingEntries];
```

The loader asserts `TenantId`, resolves languages against the catalogue and calendars against the
registry — an unresolvable value (a pack removed after the tenant chose its calendar) falls back
to the platform default (`en`, `gc`, `UTC`) with a Critical `SettingsValueUnresolvable(column,
value)` log and a `tellma.settings.entries.invalid` count, never a crash — normalizes and converts
the zone, drops undeclared entries, parses every non-`Secret` entry against its key and keeps a
`Secret` entry as stored — neither parsed nor unprotected before `Get<T>` (§5.3) — builds both
views, and stamps the entry with the tag the prelude returned. The contributor reads the sets
whenever they are present and `Set`s the views under that tag; when the guard failed on
`SettingsStale`, spec 0013's runner re-runs this spec's initializer (§9.3) before recomposing, so
the recomposed batch sees the new shape, calendar pair, zone and `Today`.

**Tenant-id guard.** `TenantId` is not a partition key (the database is the tenant); it is a
routing guard against a catalog typo, a backup restored under the wrong name, or a sandbox cloned
from live. The loader compares it with the routed tenant id on every load; a mismatch is the
internal `TenantDatabaseMismatchException(ExpectedTenantId, ActualTenantId)`, fail-closed, logged
Critical as `TenantDatabaseMismatch(expected, actual)`, surfacing as a 500. `TenantId = 0` marks
the unprovisioned placeholder: it is accepted only in a `System` scope (`RequestContext.Kind =
System`, the provisioning run) and is a mismatch everywhere else.

**Provisioning.** `SettingsProvisioningStep` — spec 0010's `ITenantProvisioningStep` with
`Name = "core.settings"`, `Order = 15`, `Version = 1`, listed among spec 0010's platform steps
between `core.bootstrap-administrator` (10) and `core.blob-container` (20) — runs in the migrator's
step-runner scope (spec 0010's
`ITenantScopeFactory.CreateScopeAsync(snapshot, allowNonActive: true)`, `Kind = System`) and
completes the placeholder through the pipeline: `TenantId` = the tenant's id, `Name` =
`TenantProvisioningContext.Tenant.Name`, `PrimaryLanguage` = the distribution's default language,
`PrimaryCalendar = 'gc'`, `TimeZone = 'UTC'`. The administrator changes the rest through
`settings/save`. Because the step writes through the emitter, the `settings` tag bumps; the
migrator's wholesale bump (§2.8) covers the `HasData` row itself.

## 6. The settings service

### 6.1 Contract

```csharp
// Tellma.Core.Settings (runtime; [ApiRoute("settings")] of spec 0014, registered by CoreFeature through contribution.ApiService<SettingsService>())
public sealed class SettingsService
{
    public Task<(byte[] Json, string WireTag)?> ClientAsync(string? ifTag);   // null = unchanged
    public Task<IReadOnlyDictionary<string, string>> EntityTagsAsync();
    public Task<TenantSettingsDetails> DetailsAsync();
    public Task<TenantSettingsForClient> SaveAsync(TenantSettingsPatch patch);
    public Task RefreshCachesAsync();
}

// Tellma.Core.Abstractions.Settings
public sealed record TenantSettingsDetails(             // the editor's view: only the categories the caller holds Read on
    Settings? General, IReadOnlyList<SettingKeyDescriptor> Keys, IReadOnlyDictionary<string, JsonElement> Values,
    IReadOnlyList<string> SetKeys, RelatedEntities Related);

public sealed record SettingKeyDescriptor(
    string Name, string Category, SettingVisibility Visibility, SettingValueType ValueType, bool IsNullable,
    bool IsArray, IReadOnlyList<string>? EnumValues, string? References, JsonElement DefaultJson);

public enum SettingValueType { Boolean, Int32, Int64, Decimal, String, Date, Time, Guid, Enum, Json }

public sealed record TenantSettingsPatch(               // field mask; a listed null clears
    IReadOnlyList<string> Fields, string? Name, string? Name2, string? Name3, string? PrimaryLanguage,
    string? SecondaryLanguage, string? TertiaryLanguage, string? PrimaryCalendar, string? SecondaryCalendar,
    string? TimeZone, IReadOnlyDictionary<string, JsonElement?>? Entries, DateTimeOffset ExpectedModifiedAt);
```

| Member | Annotation |
|---|---|
| `Client` | `[ApiAction]` `client`, member-only, idempotent, `Mutation = false` |
| `EntityTags` | `[ApiAction]` `entity-tags`, member-only, idempotent, `Mutation = false` |
| `Details` | `[ApiAction]` `details`, member-only, idempotent, `Mutation = false`; in code, one `IAccessEvaluator.Evaluate` over `core.Settings.General` and every category × `Read` (a `Save` grant satisfies `Read`, spec 0013), `ForbiddenException` when none is granted; the view holds the categories granted |
| `Save` | `[ApiAction]` `save`, member-only; in code (§6.3 step 1), `core.Settings.General × Save` when `Fields` names a typed member and `core.Settings.<Category> × Save` per listed entry key, through spec 0013's `RequireAsync`, which carries the typed row's step-up; calls `batch.BumpVersionTag("permissions")` only when a language column changed (the Queryex schema shape follows the languages), so an ordinary settings edit never cold-paths every user |
| `RefreshCaches` | `[ApiAction]` `refresh-caches`, resource `core.Settings.General`, action `Save`, idempotent; sensitive; bumps every tag |

### 6.2 `client`, `entity-tags` and `details`

`Client(ifTag)` returns `ITenantSettingsCache.GetForClientAsync()`: the pre-serialized
bytes and their wire tag, or the *unchanged* result when `ifTag` equals the current wire tag (spec
0015 shapes both). `EntityTags()` returns `ICacheableEntities.GetWireTagsAsync()`:
`{ "gl.Center": "1.<guid>", … }`, keyed by entity name, for every cacheable entity of the
composition. Both run on a `Read` batch through spec 0013's `IGuardedBatchRunner` so the prelude
keeps the snapshot current; on a warm instance neither adds a statement.

`Details()` serves the settings editor from the same cache, on a `Read` batch through the runner
whose only statements are the related queries below. `General` is the typed row, its `ModifiedAt`
the stamp `save` expects back, present when the caller holds `Read` on `core.Settings.General`;
`Keys` describes every declared key of a held category: `ValueType` is a `SettingValueType` — a
scalar, `Enum` with `EnumValues`, or `Json` for a key with its own `JsonTypeInfo<T>` — with
`IsNullable` and `IsArray` for the admitted forms, and `References` the referenced entity's name;
`Values` carries each such key's current value, stored or default, except a `Secret` key's;
`SetKeys` names the keys a row exists for, which is all the editor learns about a secret. `Related`
holds, for every key with `References`, the rows whose ids appear in `Values`: one `Query<TEntity>`
per referenced type — the generic closed over `SettingKey.References` once per type and cached by
the service, spec 0011 offering no non-generic entity query — restricted through spec 0011's
`KeySetRestriction("Id", IdList)` to those ids and to the target stack's `RelatedSelect` projection
(spec 0014's `StackDescriptor`), with no row-level filter, as spec 0014 treats the related entities
of a details read — a caller who may read a setting may read the display projection of what it
points at; an id whose row no longer exists is absent and the editor shows the bare id.
`settings/client` carries no related entities: its bytes are cached under the `settings` tag while a
referenced name changes under it, and the SPA resolves a `Client`-visible reference through the
cacheable lists or the picker.

### 6.3 `save`

`TenantSettingsPatch` has field-mask semantics: `Fields` lists the typed members and the entry
keys (as `entries.<key>`) the request sets; an unlisted member is untouched; a listed member whose
value is `null` **clears** it — which is what makes `SecondaryLanguage = null` expressible; a
listed entry whose value is `null` resets the key to its default and deletes the row.
`ExpectedModifiedAt` is the typed row's stamp and is required (`Concurrency.StampRequired`).

Pipeline, two round trips through `IGuardedBatchRunner`:

1. **Authorization** before any batch, through spec 0013's `RequireAsync`, which raises the typed
   row's step-up: `core.Settings.General × Save` when `Fields` names any typed member;
   `core.Settings.<Category> × Save` for every distinct category of the listed entry keys
   (`general` maps to the typed row's securable); a request touching two categories needs both.
2. **Validate batch**: `Query<Settings>` (the single row) and `Query<SettingEntry>` restricted to
   the listed keys through spec 0011's `KeySetRestriction("Key", StringList)`; a moved `settings`
   tag recomposes it (§2.6).
3. **Merge and validate** in memory (codes in §6.5): unknown field names; languages ∈ the
   distribution's declared set, distinct, tertiary ⇒ secondary; calendars ∈ the registry,
   secondary ≠ primary; the zone through `TimeZoneInfo.TryFindSystemTimeZoneById` and
   `TryConvertIanaIdToWindowsId`, stored normalized; each entry key declared,
   its value deserializable as `T` (through the key's `JsonTypeInfo<T>` when supplied), and
   accepted by `Validate`; a `Secret` entry's value, once accepted, is protected (§5.3) and only
   the ciphertext reaches the row. The merged row's new shape nulls `Name2`/`Name3` beyond it
   (§7.5).
4. **Persist batch**: `Save<Settings>` with the merged row carrying `ExpectedModifiedAt` as
   `ModifiedAt` under `ConcurrencyMode.Check`; `Save<SettingEntry>` for changed and new entries
   (new rows with `Id = 0`; existing rows carry their hydrated `ModifiedAt`); `Delete<SettingEntry>`
   `ByIds` for reset entries; `BumpVersionTag("permissions")` when any of the three language
   columns changed. The executor adds the guard, the audit stamps, the history rows and the
   `settings` bump.
5. **Warm the writer**: rebuild both views from the saved rows under
   `BatchOutcome.VersionTags["settings"]` and `Set` them; return `TenantSettingsForClient`, whose
   new wire tag rides the response header (§2.7). No reload.
6. **Mirror the names**: when the name group or the content-language list changed, spec 0010's
   `ITenantCatalog.RenameAsync(tenantId, names)` runs after the commit, registered through the
   persist batch's `OnCommitted` hook (spec 0011); a failure is logged, never surfaced, and the
   next save that changes them retries the mirror.

A language change also switches every subsequent request of the tenant to the schema of the new
`MultilingualShape` on this instance and, through the `permissions` bump, forces every user's next
request to recompose against it (§7.4); spec 0014's preprocessing nulls gated twins from then on.

### 6.4 `refresh-caches`

For use after out-of-band changes. One `Persist` batch through the runner with a raw statement
declaring `Writes = { core.VersionTags, core.UserStamps }` and `SqlOptions.Idempotent = true`;
the executor's own epilogue adds nothing (neither table carries a rule):

```sql
-- settings/refresh-caches: every tenant-level tag and both user-level columns of every user; @tm_tag is the batch's Guid
UPDATE [core].[VersionTags] SET [Tag] = @tm_tag;
UPDATE [core].[UserStamps] SET [PermissionsTag] = @tm_tag, [PreferencesTag] = @tm_tag;
```

The action returns nothing; the response header carries the new tags. Every instance's next
prelude reconciles; every user's next request takes the cold path once. After the commit the
action publishes `cache.changed` (§2.5) for `settings` and for every `entity:<Name>` of the
composition.

### 6.5 Validation codes

Dotted PascalCase resource keys, rendered by spec 0015 through `IStringLocalizer` under the request
culture; the path is the patch member or `entries.<key>`.

| Code | Condition |
|---|---|
| `Required` | `Name`, `PrimaryLanguage`, `PrimaryCalendar` or `TimeZone` cleared |
| `Settings.FieldUnknown` | a `Fields` entry names no typed member and no `entries.<key>` |
| `Settings.LanguageNotOffered` | a language outside `ILanguageCatalog.Offered` |
| `Settings.LanguagesNotDistinct` | two positions with one language |
| `Settings.TertiaryRequiresSecondary` | tertiary set while secondary is empty |
| `Settings.CalendarNotRegistered` | a code `ICalendarRegistry.TryGet` rejects |
| `Settings.CalendarsNotDistinct` | secondary = primary |
| `Settings.TimeZoneUnknown` | `TryFindSystemTimeZoneById` fails, or the IANA id has no Windows mapping |
| `Settings.KeyUndeclared` | an entry key `ISettingKeyRegistry.Find` returns `null` for |
| `Settings.EntryTypeMismatch` | the JSON value does not deserialize as the key's `T` |
| the key's own code | returned by `SettingKey<T>.Validate` |
| `Concurrency.StampRequired` | `ExpectedModifiedAt` absent |

A stale `ExpectedModifiedAt` is spec 0014's `ConcurrencyException` (409) from the emitter's
comparison.

## 7. Languages and the multilingual shape

### 7.1 Contracts

```csharp
// Tellma.Core.Abstractions.Localization
public sealed record LanguageInfo(string Code, string NativeName, string Symbol, bool IsRightToLeft);

public interface ILanguageCatalog
{
    IReadOnlyList<LanguageInfo> Offered { get; }
    LanguageInfo? Find(string code);                    // over the whole catalogue, offered or not
    bool IsOffered(string code);
}

public static class CoreStrings
{
    public const string BaseName = "Tellma.Core.Resources.Strings";
    public const string Assembly = "Tellma.Core";
}

public interface ILabelProvider
{
    string EntityLabel(Type entityType, bool plural = false);
    string PropertyLabel(Type entityType, string propertyName);   // twins carry " (E)" / " (ع)"
    string SettingLabel(SettingKey key);
    string EnumValueLabel(Type enumType, string value);
}
```

### 7.2 The catalogue

The catalogue is **data in code** in `Tellma.Core`'s `LanguageCatalog`: a frozen list of every
language the platform can *describe*, additive by platform pull request, reachable only through
`Find`, which searches all of it so that a tenant whose stored language a distribution stopped
offering still resolves its column labels. Initial entries (`Code`, `Symbol`):
`en E`, `ar ع`, `am አ`, `om Om`, `ti ት`, `so So`, `sw Sw`, `fr F`, `es Es`, `pt P`, `de D`, `tr T`,
`ur ا`, `hi हि`, `zh 中`. `Symbol` is the short native-script mark used in labels; symbols are
unique across the catalogue and every code is a predefined culture (both pinned by tests).
Describing a language is not shipping its strings: strings fall back to English per key (§10.3),
so a catalogue entry is safe before any translation exists. `Find` and `IsOffered` compare
ordinal-ignore-case.

### 7.3 The distribution's declaration and the tenant's validation

`tellma.Languages(codes)` declares the offered set once; the first code is the **distribution
default** (used when negotiation finds nothing and the tenant's primary is not offered);
`tellma.AddLanguage(info)` adds a language the catalogue does not describe. The startup checks are
§1.3. `SatelliteResourceLanguages` in the distribution's project trims shipped satellites to the
declared set (a build-side optimization, not required for correctness). Packs declare no language
support in this release: a pack lacking a tenant's language falls back to English string by string.

A tenant's content languages (`core.Settings`) must be offered, distinct, and tertiary ⇒ secondary
(§6.5). They decide which `Name`/`Name2`/`Name3` column holds which language and how those columns
are labelled, and nothing else.

### 7.4 `MultilingualShape` and the Queryex schema

Spec 0011's `IQueryexSchemaProvider.GetSchema(shape)` returns one immutable schema per shape —
`Primary`, `PrimaryAndSecondary`, `All` — omitting the `*2`/`*3` twins of `[Multilingual]` groups
beyond the shape: at most three schemas per process, built lazily, shared by every tenant with that
shape. The tenant's shape is `TenantSettings.Shape`, selected per request from the settings cache,
so a settings change that adds a language switches variants on the next request with no cache to
flush; the engine's own caches, keyed on schema identity (spec 0008 §16), are shared across
tenants. Physical columns are never gated, so a query compiled a moment before a change still
executes; a model change (a deployment) is the only other invalidation. Row-level-security
composition and child-entity path rewriting are `FilterTree` work, never schema variants.

### 7.5 `[Multilingual]` semantics

`[Multilingual]` (declared in `Tellma.Core.Abstractions.Entities` with spec 0011's entity contract)
sits on the primary property of a text group; the twins `<Name>2` and `<Name>3` are found by name
and must be nullable strings of the same maximum length (startup check by spec 0011's metadata
builder). What the declaration produces: schema gating (§7.4); the twin labels (§7.6); Excel column
mapping through the shape (spec 0018); search across the group (spec 0014); and **gated twins are
nulled** by spec 0014's preprocessing before validation, so a client that still sends `Name3` after
the tertiary language was removed writes nothing. An Excel column mapped to a gated twin is spec
0014's `Import.LanguageNotConfigured` when a value is supplied. A twin beyond the tenant's shape is
absent from the wire (spec 0015).

### 7.6 Labels

`ILabelProvider` resolves under the request culture by convention, with no per-entity code, for
Excel, MCP and message composition; the SPA renders the same keys from the string pack (§10.4):

| Call | Lookup order |
|---|---|
| `PropertyLabel(T, p)` | `<Schema>_<Entity>_<Property>` (the key grammar of §10.1) in the declaring assembly's `Strings` → each base class's assembly → `<Property>` in Core's `Strings` (`Name`, `Code`, `IsActive`, `Description`, `CreatedAt`, … ship there) → the humanized property name |
| twin `<Name>2` / `<Name>3` | the primary's label + `" (" + Symbol + ")"` of the tenant language at that position (`Name (E)`, `Name (ع)`); for a mono-lingual tenant the primary carries no suffix; a twin beyond the shape has no label and is not offered |
| `EntityLabel(T)` | `<Schema>_<Entity>` in the declaring assembly → base assemblies → the humanized class name |
| `EntityLabel(T, plural: true)` | `<Schema>_<Entity>_Plural` in the declaring assembly → base assemblies → the singular label; serves Excel sheet names (spec 0018) and list titles |
| `EnumValueLabel(E, v)` | `<Schema>_<Enum>_<Value>` in the declaring assembly → the humanized value |
| `SettingLabel(key)` | §5.4 |

The same provider serves Excel headers (spec 0018) and validation messages naming a field (spec
0015).

## 8. Calendars

### 8.1 Contracts

```csharp
// Tellma.Core.Abstractions.Calendars
public static class CalendarCodes
{
    public const string Gregorian = "gc";
    public const string UmAlQura = "uq";
    public const string Ethiopian = "et";
}

public enum DateStyle { Short, Medium, Long, Full }

public interface ICalendarSystem
{
    string Code { get; }
    System.Globalization.Calendar Calendar { get; }
    int MonthCount { get; }
    DateOnly MinSupported { get; }
    DateOnly MaxSupported { get; }
    (int Year, int Month, int Day) Decompose(DateOnly date);
    string Format(DateOnly date, CultureInfo culture, DateStyle style);
    string Format(DateTimeOffset instant, TimeZoneInfo zone, CultureInfo culture, DateStyle style,
        bool includeTime = true);
    string MonthName(int month, CultureInfo culture, bool abbreviated);
    DateOnly? TryParse(string text, CultureInfo culture);
}

public interface ICalendarRegistry
{
    IReadOnlyList<string> Codes { get; }
    ICalendarSystem this[string code] { get; }
    ICalendarSystem? TryGet(string code);
}
```

| Member | Meaning |
|---|---|
| `Calendar` | A BCL `Calendar` for arithmetic only; never assigned to a `DateTimeFormatInfo`. |
| `MonthCount` | 12, or 13 for `et`. |
| `MinSupported` / `MaxSupported` | The exact range the system converts without approximation; validators warn beyond it. |
| `Decompose` | Calendar year, month, day of a Gregorian `DateOnly`. |
| `Format(date, …)` | The platform's own rendering by `DateStyle`; digits always ASCII. |
| `Format(instant, zone, …)` | Converts to `zone`, then formats the date and, when `includeTime`, the time in the culture's short time pattern. |
| `MonthName` | From Core's resources for `uq`/`et` (`Calendar_uq_Month1` … `Calendar_et_Month13`), from the culture's `DateTimeFormat` for `gc`. |
| `TryParse` | The inverse of `Format(…, Short)` and of the numeric `yyyy-MM-dd` form in this calendar; `null` on failure. Used by spec 0018's text-date import. |
| `ICalendarRegistry` | The systems composed into the distribution, by code; `this[code]` throws `KeyNotFoundException`. `TryGet` compares ordinal-ignore-case. |

### 8.2 The three systems

`Tellma.Core.Calendars` implements and `CoreFeature` registers: `gc` over `GregorianCalendar`;
`uq` over `UmAlQuraCalendar` (AD 1900-04-30 … 2077-11-16 = AH 1318 … 1500); `et` as a port of the
Ethiopian calendar (Julian-day-number conversion, 13 months, leap rule `year % 4 == 3`, single era,
AD 1752-09-14 … 2500-01-01, conformance vectors in §12). The codes are the strings spec 0008 §10.4
reserves for the `cal` argument; the startup check asserts agreement. A distribution registers a
further system through `contribution.Calendar<TCalendar>()`. The `uq` month-map engine amendment
(spec 0008's reserved `'uq'` emission) is deferred; it will be specified with this spec's
successor when scheduled.

Tenant settings carry `PrimaryCalendar` (required, default `gc`) and `SecondaryCalendar`; a user
may prefer either through spec 0013's `User.PreferredCalendar`; a request may override with the
`Tellma-Calendar` header (§9). A code outside `ICalendarRegistry.Codes` in a preference is spec
0017's validation error `Users.CalendarUnknown`; a code outside the tenant's pair, in a preference
or in a header, is ignored by negotiation (§9.2).

### 8.3 Formatting rules

Calendar-aware formatting is platform code: `Format` decomposes through the `Calendar` object and
renders one of four styles — `Short` numeric in the culture's date order and separator; `Medium`
day, abbreviated month, year; `Long` day, full month, year; `Full` with the weekday name from the
culture. It never assigns a calendar to `DateTimeFormatInfo` (impossible for custom calendars and
for calendars outside a culture's `OptionalCalendars`) and never routes through `-u-ca-` culture
names. Dates beyond `UmAlQuraCalendar`'s range render through `HijriCalendar` with `HijriAdjustment
= 0`, documented in the member's remarks as a tabular approximation; `MaxSupported` exposes the
exact range.

## 9. Time zones, negotiation, and the request context

### 9.1 Two zones

`core.Settings.TimeZone` is the **tenant zone**, a business fact: spec 0011 binds spec 0008's
`Today` slot to `TenantSettings.Today(clock)` and the `TimeZone` slot to
`TenantSettings.SqlServerTimeZoneName`, so two colleagues filtering `PostingDate = today()` see the
same rows and no client controls a security-relevant filter. The **display zone**
(`LocalizationContext.TimeZone`, `RequestContext.TimeZone`) formats instants in messages and
Excel cells and is negotiated per request; there is no `X-Today` header.

### 9.2 Negotiation

```csharp
// Tellma.Core.Abstractions.Localization
public sealed record LocalizationContext(               // copied onto RequestContext by the initializer at Order 200
    CultureInfo Culture, ICalendarSystem Calendar, TimeZoneInfo TimeZone, int ContentLanguageIndex);

public sealed record NegotiationInput(
    string? AcceptLanguage, string? CalendarHeader, string? TimeZoneHeader, string? PreferredLanguage,
    string? PreferredCalendar, string? PreferredTimeZone, string? MessageLanguage, string? MessageCalendar,
    TenantSettings Settings);

public interface ILocalizationNegotiator
{
    LocalizationContext Negotiate(NegotiationInput input);   // override -> header -> preference -> tenant -> distribution default; extensions stripped; never throws
}
```

| Value | Override | 1st | 2nd | 3rd | 4th |
|---|---|---|---|---|---|
| Culture | `MessageLanguage` when offered by `ILanguageCatalog.Offered` | `Accept-Language`: q-ordered, first three ranges; `-u-`/`-x-`/`-t-` extensions stripped; matched by language subtag with parent fallback against `ILanguageCatalog.Offered`; the region is kept when the header carried one (`ar-SA`) | `PreferredLanguage` | tenant `PrimaryLanguage` when offered | the distribution default |
| Calendar | `MessageCalendar` when ∈ `ICalendarRegistry.Codes`, even outside the tenant's pair | `Tellma-Calendar`: a code ∈ the tenant's pair | `PreferredCalendar` when ∈ the pair | tenant `PrimaryCalendar` | — |
| Display zone | — | `Tellma-Time-Zone`: an IANA id `TryFindSystemTimeZoneById` accepts | `PreferredTimeZone` | tenant `TimeZone` | — |

`Accept-Language` sits first because the SPA sets it explicitly to the user's chosen language on
every call, while non-browser clients fall through to the stored preference. A header value that is
present but invalid — an unknown calendar code, an unparseable range, an unknown zone — is
**ignored** with a `tellma.localization.headers.rejected` count, never a 400: a stale browser must
not lock a user out. The response carries `Content-Language` and `Tellma-Calendar` with the
*effective* values (spec 0015), so a client that drifted (the administrator removed the secondary
calendar) keeps working and corrects itself. The header names are spec 0015's `TellmaHeaders`;
the negotiator sees values only.

`MessageLanguage` and `MessageCalendar` are a surface's fixed choice for the language and calendar
of its messages, not the user's: the MCP surface passes them (spec 0015 §11.4), the web surface
passes neither. `ContentLanguageIndex` (1..3) is derived from the culture the rest of the
negotiation yields (header → preference → tenant primary), never from `MessageLanguage`: the
position of the tenant content language whose language subtag equals that culture's, else 1, so a
message in the override language still names records in the user's content language. Servers use
it wherever they must pick "the" name of an entity (a message naming a record, an export's default
name column); no stored preference exists for it. Formatting culture = request culture in this
release; a user who wants a region's digits and separators stores a regioned `PreferredLanguage`
(`ar-SA`).

The negotiated `CultureInfo` is a clone of the matched predefined culture with
`DateTimeFormat.Calendar = GregorianCalendar` where `OptionalCalendars` allows it (true for every
catalogue language; pinned by test), so any incidental `ToString()` prints Gregorian and calendar
rendering is always explicit through `ICalendarSystem`.

### 9.3 The initializer and the context

`CoreFeature` registers spec 0010's `IRequestContextInitializer` at `Order` 200. It receives the
context spec 0013's connect initializer (`Order` 100) produced — `UserId` and, through the
`preferences` cache, the caller's `PreferredLanguage`/`PreferredCalendar`/`PreferredTimeZone` — and
spec 0010's `RequestContextInputs(AcceptLanguage, RequestedCalendar, RequestedTimeZone, Client,
MessageLanguage, MessageCalendar)`; it ensures `TenantSettings` (§5.6), calls `Negotiate` with the
three headers, the three preferences and the two message inputs (§9.2), and returns the context
with `Language`, `Culture`, `CultureInfo`, `Calendar`, `CalendarSystem`, `TimeZone`,
`TenantTimeZone`, `Today`, `ContentLanguageIndex` and `TenantSettings` set. It then sets
`CultureInfo.CurrentCulture` and `CurrentUICulture` for the request (they flow with the execution
context); the scoped context is the source of truth and the thread cultures are set from it, never
the other way round. `RequestLocalizationMiddleware` is not used: its default culture is static and
this one is per tenant. Spec 0013's runner re-runs this initializer after applying a prologue's
fresh settings (§5.7), so a recomposed batch sees the new context.

A background scope (spec 0010 §4.4), a job's or a provisioning step's, carries no locale field in
its snapshot: the scope factory runs this initializer with empty `RequestContextInputs`, so the
locale fields resolve from the run-as user's stored preferences, then the tenant's settings, through
the same negotiator with no headers.

## 10. Resources and ICU messages

### 10.1 Files and keys

Each assembly with user-facing text ships `Resources/Strings.resx` (neutral = English,
`NeutralResourcesLanguage("en")`) with satellites `Strings.<lang>.resx`, and a marker class
`Strings` in its `Resources` namespace (`Tellma.Core.Resources.Strings`,
`Tellma.Module.Gl.Resources.Strings`) — the base name the folder-and-file convention yields with no
`ResourcesPath`. Keys are `PascalCase_Underscored`. A label key begins with the qualified entity
name, spec 0011's `EntityMetadata.Name`, with the schema in PascalCase and `.` as `_`: labels
`<Schema>_<Entity>`, `<Schema>_<Entity>_Plural` and `<Schema>_<Entity>_<Property>` (`gl.Center` →
`Gl_Center`, `Gl_Center_Plural`, `Gl_Center_Name`; `core.User` → `Core_User_Email`); an enum's
values `<Schema>_<Enum>_<Value>` under the schema of the entities that carry it
(`Gl_CenterType_Abstract`); the bare `<Property>` keys of Core's `Strings` are the shared fallback
of §7.6. The prefix keeps two packs that each ship an `Invoice` apart in one string pack (§10.4).
Settings are `Setting_<key with '.' → '_'>`; a validation code is looked up as written, `.` → `_`
(`Settings.LanguageNotOffered` → `Settings_LanguageNotOffered`); problem titles and details are
`Problem_<code with '-' → '_'>` and `Problem_<…>_Detail` (`Problem_concurrency_conflict`); month
names `Calendar_<code>_Month<n>`. For `IStringLocalizer<Strings>` a pack references
`Microsoft.Extensions.Localization.Abstractions`, and it addresses Core's strings through
`IStringLocalizerFactory.Create(CoreStrings.BaseName, CoreStrings.Assembly)` — the framework's own
API, never a type from `Tellma.Core`.

`Strings` is the shared base name: everything in it also reaches the SPA through the string pack
(§10.4). Text the SPA never renders — an email subject or body, a document footer — goes in a
second file per assembly, `Resources/ServerStrings.resx` with its own marker class `ServerStrings`,
which the pack never reads. An author in doubt uses `Strings`: over-sharing costs bytes,
under-sharing costs the SPA a fallback to server-rendered text.

Author conventions, carried in the `Strings` markers' documentation and the distribution template:
`{count, plural, …}` for counts; `{gender, select, female {…} male {…} other {…}}` with `other`
mandatory; named arguments only, no positional `{0}`.

### 10.2 `IcuStringLocalizerFactory`

`AddTellma` registers `IcuStringLocalizerFactory` (`Tellma.Core.Localization`) as the process's
`IStringLocalizerFactory`, decorating the framework's `ResourceManagerStringLocalizerFactory`, so
every localizer in the process — `IStringLocalizer<T>` and `Create(baseName, location)` alike —
renders through ICU MessageFormat (plural, select, selectordinal, nesting). Values without `{`
skip the parser. One shared `MessageFormatter` (`useCache: true`, culture passed per call): the
pattern cache is lock-free and patterns come from resources only, so it is bounded by the resource
count. **Calendar-aware dates**: the formatter is constructed with a `CustomValueFormatter` whose
`TryFormatDate`/`TryFormatTime` delegate to the current request's `ICalendarSystem` and display
zone (`DateOnly`, `DateTime` as tenant-local, `DateTimeOffset` converted) and whose
`TryFormatNumber` keeps the built-in behaviour, so `{d, date, short|medium|long|full}` works in
every calendar — `medium` and `long`, which the library itself rejects, are supplied by the hook.
A malformed pattern renders the raw value and logs `MessageFormatMalformed(baseName, key)` once
per key; it never fails the request.

### 10.3 Two fallback chains

*Resources:* the request culture's parent chain to the neutral English resource (`ar-SA → ar →
neutral`), walked by the framework's hub-and-spoke `ResourceManager`; **no tenant-primary hop**. A
key absent everywhere returns the key text and counts `tellma.localization.missing` (tag
`culture`, the offered set) once per key per culture per process. The neutral `.resx` is English by
construction, which is how "an English string must always be supplied" is enforced.

*Content:* the requested content-language index → the primary column (`Name3 ?? Name`, `Name2 ??
Name`), applied by the client for display and by the server wherever it renders a name.

### 10.4 The string pack

```csharp
// Tellma.Core.Abstractions.Localization
public sealed record StringPack(string Language, string Version, ReadOnlyMemory<byte> Json);   // pre-serialised UTF-8; a flat object, resx keys verbatim

public interface IStringPackProvider                    // singleton; composed at startup, one pack per offered language
{
    StringPack? Get(string language);                   // null for a language outside the offered set
}
```

At startup `StringPackComposer` (`Tellma.Core.Localization`) builds one pack per language of
`ILanguageCatalog.Offered`: for every assembly that declares a composed feature (spec 0010) — Core,
each pack, the distribution — it enumerates that culture's own resource set (the neutral set
for `en`) and merges the entries into one flat JSON object, resx keys verbatim; a key declared by
two assemblies is a `CompositionProblem` (spec 0010). `Version` is
`DeploymentVersions.DistributionVersion`. Spec 0015 serves the pack at
`GET /api/strings/{language}?v=<version>`, tenantless and anonymous, compressed, with
`Cache-Control: public, max-age=31536000, immutable` when `v` equals the pack's version and
`no-store` otherwise; a language outside the offered set is 404. The bytes are held once per
language, as `TenantSettingsForClient` is.

The SPA is the reader. The languages it lets a user switch among are the tenant's content
languages from `settings/client` (§7.3 requires each to be offered, so each has a pack); it
fetches those packs and the English one, with the `DistributionVersion` stamped into its build as
`v`, merges each into its dictionary for that language beside its own strings, and renders every
server-originated text itself: a validation `code` becomes the key by the `.` → `_` mapping of
§10.1 and a problem `code` its `Problem_` key, with the response's `arguments` as ICU parameters,
raw values it formats in the active calendar; a label key (`<Schema>_<Entity>_<Property>`,
`Setting_<key>`, `NotificationType_<key>`) is looked up as is, and `arguments.property` is
resolved to its `<Schema>_<Entity>_<Property>` key the same way. A key the pack lacks falls
through to the English pack, then to the server-rendered `message` the response also carries
(spec 0015). No response the SPA caches carries culture-dependent text, so switching the UI
language re-renders labels, messages and multilingual content in place, with no request and no
cache invalidation. Server-side rendering — `ILabelProvider`, the rendered `message` — remains for
Excel, MCP, email and every client without a pack.

## 11. Telemetry and configuration

Meter `Tellma.Core`; constants in `Tellma.Core.Abstractions.Caching.CacheTelemetryNames` and
`Tellma.Core.Abstractions.Localization.LocalizationTelemetryNames` (each with `MeterName`).

```csharp
public static class CacheTelemetryNames
{
    public const string MeterName = "Tellma.Core";
    public const string Requests = "tellma.cache.requests";
    public const string LoadDuration = "tellma.cache.load.duration";
    public const string Entries = "tellma.cache.entries";
    public const string Size = "tellma.cache.size";
    public const string Limit = "tellma.cache.limit";
    public const string Evictions = "tellma.cache.evictions";
    public const string Bumps = "tellma.versiontags.bumps";
    public const string Stale = "tellma.versiontags.stale";
    public const string Missing = "tellma.versiontags.missing";
    public const string InvalidEntries = "tellma.settings.entries.invalid";
    public const string KindTag = "cache.kind";
    public const string OutcomeTag = "cache.outcome";
    public const string ReasonTag = "cache.reason";
    public const string TagKindTag = "tag.kind";
    public const string StalePhaseTag = "stale.phase";
}

public static class LocalizationTelemetryNames
{
    public const string MeterName = "Tellma.Core";
    public const string Missing = "tellma.localization.missing";
    public const string HeadersRejected = "tellma.localization.headers.rejected";
    public const string CultureTag = "culture";
    public const string HeaderTag = "header";
}
```

| Instrument | Kind | Unit | Tags (closed sets) |
|---|---|---|---|
| `tellma.cache.requests` | counter | `{request}` | `cache.kind` ∈ settings, preferences, permissions, entities; `cache.outcome` ∈ hit, miss, stale, oversized, uncached |
| `tellma.cache.load.duration` | histogram | `s` | `cache.kind` |
| `tellma.cache.entries` | observable gauge | `{entry}` | `cache.kind` |
| `tellma.cache.size` | observable gauge | `{unit}` | `cache.kind` |
| `tellma.cache.limit` | observable gauge | `{unit}` | `cache.kind` |
| `tellma.cache.evictions` | counter | `{entry}` | `cache.kind`; `cache.reason` ∈ capacity, replaced, removed |
| `tellma.versiontags.bumps` | counter | `{bump}` | `tag.kind` ∈ settings, permissions, entities, entity, other, user |
| `tellma.versiontags.stale` | counter | `{event}` | `stale.phase` ∈ read-rerun, write-guard, exhausted |
| `tellma.versiontags.missing` | counter | `{name}` | `tag.kind` |
| `tellma.settings.entries.invalid` | counter | `{entry}` | none |
| `tellma.localization.missing` | counter | `{lookup}` | `culture` |
| `tellma.localization.headers.rejected` | counter | `{header}` | `header` ∈ accept-language, calendar, time-zone |

`tellma.cache.size` and `tellma.cache.limit` share the kind's unit, so a fill ratio, and a thrash
alert — capacity evictions rising while misses rise at the limit — are telemetry-only queries. No
tenant, user, entity-name or key-name tag anywhere; those are structured log properties. Structured
events (level): `VersionTagRowMissing(name)` Warning once per name per process;
`CacheableEntityOverflow(entity, rows, maxRows)` Warning once per type per process;
`StaleVersionTagRetry(names)` Information; `SettingsEntryUndeclared(key)` Warning;
`SettingsEntryInvalid(key)` Warning; `SettingsSecretUnreadable(key)` Critical;
`SettingsValueUnresolvable(column, value)` Critical; `MissingResourceString(baseName, key, culture)`
Warning once; `MessageFormatMalformed(baseName, key)` Warning once;
`TenantDatabaseMismatch(expected, actual)` Critical. No event carries a setting value. Alert queries
live under `infra/monitoring/` and are cross-checked by the existing name test.

**Configuration.** `Tellma:Cache` → `TellmaCacheOptions` (§3.1), `ValidateOnStart`: every limit
≥ 1, `EntityMaxRowsDefault ≤ EntityMaxRowsCeiling`. Nothing else is configurable: the language list
and the calendars are code because they are facts about what the distribution ships, not about
where it runs.

## 12. Testing

Test projects mirror `src/`; nothing here talks to a third party, so there is no `Live=true`
suite.

**`test/core/Tellma.Core.Tests`** (PR tier, hermetic, cross-platform):

- **Catalogue invariants**: unique symbols; every code a predefined culture; `GregorianCalendar`
  assignable for the offered set × a region sample (`ar-SA`, `ar-EG`, `am-ET`, `en-US`, `fr-FR`).
- **Negotiation table**: headers × preferences × tenant for culture, calendar and display zone;
  extension stripping (`ar-SA-u-ca-islamic-umalqura → ar-SA`, the calendar taken from the header
  only); an invalid header ignored and counted; `ContentLanguageIndex` derivation; a
  `MessageLanguage` setting the culture while `ContentLanguageIndex` follows the header's; a
  `MessageCalendar` outside the tenant's pair taken as the calendar.
- **Calendars**: Ethiopian conformance vectors (Appendix A) and round trips against the Julian day
  number; `ICalendarSystem` golden strings per calendar × style × culture; the AH 1500 overflow
  through `HijriCalendar`; `TryParse` inverts `Format(…, Short)`.
- **ICU**: plural categories for `ar` and `am`; gender select; malformed pattern → raw value;
  `{d, date, medium}` in every calendar; both fallback chains; `CoreStrings` addressing from a
  test assembly.
- **String pack**: one pack per offered language, keys verbatim, the neutral set for `en`; a key
  declared by two assemblies refused; `ServerStrings` entries absent; the endpoint and its headers
  are spec 0015's to pin.
- **Resource audit**: every key of each composed assembly's neutral `Strings.resx` and
  `ServerStrings.resx` is either derived by convention from the composed model — entity, plural
  and property labels, enum values, `Setting_`, `Notification_`, `NotificationType_`,
  `NotificationChannel_`, `Problem_`, `Calendar_` months — or appears verbatim, or in its dotted
  form, as a string literal under `src/`; an orphan fails the test, as does a satellite key absent
  from its neutral file. The audit is `test/shared/Tellma.Testing.Resources` (§1.1), run here over
  Core and by each pack's and distribution's own suite over its own assemblies (spec 0017's
  reference stacks first).
- **`VersionedCache`**: tag mismatch is a miss; one loader under 64 concurrent callers; a
  faulted loader retried; bounds and eviction; `Uncached`, nothing stored, when the loader's
  prelude returns no row for the kind's name; every key begins with the tenant id; the loader
  runs on the handle it is passed.
- **Wire tags**: `ToWire` form; the **DTO shape snapshot paired with `FormatVersion`** for
  `TenantSettingsForClient` and `CachedEntitySet`.
- **Setting keys**: grammar, uniqueness, type-set, `JsonTypeInfo` and `References` rules as
  composition problems; `Get<T>` defaults, invalid stored values, `Validate` codes; a `Secret`
  value protected on save, unprotected on `Get<T>`, read as the default under another purpose
  and never present in the client view or the details view; category securables, `Read` and
  `Save`.
- **Version-tag registry**: attribute resolution to names and rules; the fixed preference-table
  rules; hand-written `entity:` names rejected; `[Cacheable]` startup checks.
- **`[Multilingual]`**: twin discovery, label suffixes per shape, schema gating over spec 0011's
  fixture model.
- **Settings patch**: field-mask merge semantics, every code of §6.5, the gated-twin nulling.

**`test/core/Tellma.Core.IntegrationTests`** (`Category=Integration`; LocalDB on Windows,
Testcontainers on Linux; spec 0011's fixture database, whose shared project
`test/shared/Tellma.Testing.Entities` gains a `[Cacheable(MaxRows = 50)]` fixture entity
`fixture.Lookups` for this suite):

- **End-to-end bump**: saving the fixture cacheable entity moves `entity:fixture.Lookup` and
  `entities` and leaves `settings` untouched; a membership save bumps only that user's
  `PermissionsTag`; a raw statement with `SqlOptions.UserIds` bumps the named users; a bump naming
  an unseeded row raises `VersionTag.Missing`; a `settings` or `entity:*` bump publishes one
  `cache.changed` per name after the commit (a recording `IClientEventPublisher`), a `permissions`
  bump none.
- **Guard and policies**: the guard raises `50412` on a moved tag and the runner re-runs once;
  a concurrent bump under `REPEATABLEREAD` waits for the persist; two writers of one tag row
  serialise on the update lock with no deadlock; the prelude heads every batch after the schema
  guard and the tag-then-rows ordering holds under a concurrent bump; `Rerun` and `Refresh` behave
  per §2.6, including a `settings` bump from a second instance that fails a validate batch's guard
  with the fresh rows in that round trip while a read batch is served.
- **Write audit**: change tracking is enabled on every fixture table; the executor in test mode
  compares `CHANGETABLE(CHANGES …)` since the batch's start with the declared write set after
  every batch and fails the test on any undeclared write.
- **Round trips**: `SqlConnection.RetrieveStatistics()` asserts §2.10 (1 warm read, 2 warm save,
  +1 cold tenant read).
- **Settings**: history rows for `Settings`/`SettingEntries` writes and none for tag bumps; the
  `TenantId` guard fails closed; the placeholder is accepted only in a `System` scope; the
  provisioning step completes it; `save` under a stale stamp is a 409; a language change bumps
  `permissions` and switches the schema shape; a `Name` change mirrors into the catalog after the
  commit and a failing mirror is logged, not surfaced; `refresh-caches` moves every row and both
  columns; `details` holds only the categories the caller holds `Read` on, with the related rows
  of every `References` key and a deleted target's id left bare.
- **Migrator**: seeding and the wholesale bump of `core.VersionTags` on every `migrate`; a new
  registry name gets its row on the next run.
- **Isolation**: two tenant databases sharing user ids and subjects never see each other's cache
  entries.

Runs on every PR: the unit suite and the integration suite (LocalDB on Windows, Testcontainers on
Linux); nightly: the full matrix.

## 13. Definition of done

- **Projects**: the additions to `Tellma.Core.Abstractions`, `Tellma.Core`, `Tellma.Core.Migrator`,
  `test/core/Tellma.Core.Tests` and `test/core/Tellma.Core.IntegrationTests`, and the new
  `test/shared/Tellma.Testing.Resources`, of §1.1 — each project README updated for its new
  folders, XML docs on every member, building and testing on Windows and Linux under
  warnings-as-errors, wired into `Tellma.slnx`.
- **Behavior**: the tags, statements, policies, wire form and seed of §2 and the executor
  obligations they impose, pinned by the integration suite; the cache of §3 and the cacheable
  mechanism of §4, pinned by both suites; the settings tables, keys, views, load, guard,
  provisioning step and service of §5–§6; the languages, shape and labels of §7; the calendars of
  §8; negotiation and the context of §9; resources, ICU and the string pack of §10 — all
  implemented and pinned by the suites of §12, green in CI.
- **Observability**: every instrument and structured event of §11 emitted and asserted at least
  once in the suites; the alert queries under `infra/monitoring/` name only instruments that
  exist.
- **CI**: the PR tier runs the unit and integration suites on both operating systems; the nightly
  tier runs the full matrix.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the per-dimension contents row
  (calendars `gc`/`uq`/`et` implemented and registered by Core, server-side `.resx` satellites
  shipping with the owning package, Locale packs keeping number-to-words and client assets, the
  string pack carrying the shared strings to the SPA); the
  guiding-principles caching row (a private bounded `MemoryCache` per kind validated by version
  tags, no `HybridCache`, output caching only for anonymous tenant-independent GETs); and the
  spec-pointers row (calendar codes land with this spec, not with the Locale packs). Public XML
  docs and error messages reference no `docs/` paths, per repo rule.
- **Not in scope of done**: the executor, emitter and analyzers that emit this spec's statements
  (spec 0011); the connect prologue, `UserProfile`, `UserAccess` and `core.UserPreferences` (spec
  0013); the pipeline's dependency declarations, re-run and preprocessing (spec 0014); routes,
  headers and problem mapping (spec 0015); the hub that delivers `cache.changed` and the publisher's
  default (spec 0020); the `uq` engine amendment; Locale packs.

## Decisions record

The load-bearing decisions, where not already evident above:

1. **Tags in a non-temporal key-value table, application-generated Guids** — a tag on a temporal
   or concurrency-stamped row writes history and moves the stamp on bookkeeping; an app-generated
   Guid is restore-safe and lets the writer stamp its own cache without a read-back (§2.1, §2.3).
2. **The executor bumps, from declarations** — the only component that knows every table a round
   trip writes is the executor; a service that never bumps cannot forget to (§2.4, §2.5).
3. **Prelude first, cold loads after, bump last before `COMMIT`** — the ordering is what makes a
   racing bump convergent instead of stale-forever (§2.5).
4. **Two-level permission tags** — per-user precision for the frequent membership edit, one
   tenant-wide recompute for the rare role edit, no member fan-out inside a role save (§2.1).
5. **Per-purpose mismatch policies** — permissions re-run always, because rows must never leave
   under moved permissions; settings and lists refresh on reads, where staleness is harmless, and
   re-run on validate and persist batches, where a validator must never decide under an input that
   moved (§2.6).
6. **Shared locks on every expected tag row inside the persist transaction, update locks on the
   rows the batch bumps** — closes the revocation race and the settings race at the cost of one
   bounded wait for the writers of those rows and the saves queued behind a pending bump, with no
   deadlock between two writers of one row; writers of different rows that each read the other's
   remain a retried victim (spec 0011) (§2.5).
7. **`UPDATE`-only bump with `VersionTag.Missing`; migrator-only seed** — a self-healing insert
   would race across instances for no gain; a missing row degrades to uncacheable, never to
   stale (§2.5, §2.8).
8. **Format version as a per-DTO constant pinned by a shape test** — folding the build identity
   into every tag would invalidate every client cache on every deploy (§2.7).
9. **A private bounded `MemoryCache` per kind, not `HybridCache`** — `HybridCache` serializes
   every write, shares the DI cache, and cannot validate its stamps against a database value
   (§3).
10. **`[Cacheable]` requires an unfiltered `Read` at startup** — a shared list cannot honour a
    row-level filter, so the rule is enforced by construction, not per request (§4.2).
11. **Typed row = read by the platform on the request path; everything else is a declared
    entry** — catalogue validation needs columns; packs cannot add columns to a Core entity
    (§5.1).
12. **`SettingKey<T>` declared once as a static field and registered in one line** — one grammar,
    one registry, one label convention, two securables per category, zero settings-page code
    (§5.3–§5.5).
13. **`TenantId` as a routing guard, never a filter; `0` accepted only in a `System` scope** —
    the database is the tenant; the guard catches misrouting and lets provisioning complete the
    placeholder (§5.7).
14. **A field-masked patch on the wire, an emitter save inside** — merge patch cannot express
    "clear"; JSON Patch mutates in place; the emitter gives audit, concurrency, history and the
    bump for free (§6.3).
15. **Content languages and request culture are separate axes; the culture is not restricted to
    the tenant's languages** — an English auditor at an Amharic tenant reads English chrome over
    Amharic content (§7.3, §9.2).
16. **The Queryex schema keyed by `MultilingualShape`, three per process** — per-tenant schemas
    would multiply the engine's caches by the tenant count for no semantic difference (§7.4).
17. **Calendars implemented in Core, rendered by platform code** — no Ethiopic calendar exists in
    .NET, a custom `Calendar` cannot be assigned to `DateTimeFormatInfo`, and `-u-ca-` cultures are
    a trap; Locale packs do not exist yet (§8).
18. **The tenant zone binds `today()`; the display zone only formats** — a client must not move a
    security predicate (§9.1).
19. **`Accept-Language` above the stored preference; invalid headers ignored** — the SPA sets the
    header deliberately; a stale browser must not lock a user out (§9.2).
20. **Resources fall back through culture parents to English, never to the tenant primary** — a
    Spanish-UI user with a missing string should see English, not Arabic (§10.3).
21. **`.resx` plus an ICU factory decorator** — the framework's fallback chain and satellite
    packaging with one dependency, wired in one place for every localizer in the process (§10.2).
22. **One catalogue, two renderers** — the shared `.resx` strings reach the SPA as a string pack
    and every server-originated text is a key plus raw arguments, so a language switch is client
    state and no cached response carries culture-dependent text (§10.4).
23. **`Secret` settings protected at rest under a tenant-scoped purpose, unprotected on first
    `Get<T>`** — the row, its history and any clone hold ciphertext only; a restore under another
    tenant or key ring reads as unset, the safe failure; no process that never reads a secret
    needs the key ring (§5.3).
24. **`details` serves the editor under per-category `Read`; `settings/client` stays member-wide
    and value-only** — related entities belong to an uncached read, where a referenced name can
    change without moving the settings tag (§6.2).

## Review flags

1. **Private `MemoryCache` versus `HybridCache` (§3).** Chosen: a private bounded `MemoryCache`
   per kind behind `VersionedCache`. Alternative: `HybridCache`, which arrives with stampede
   protection and an L2 story. Flips if a distributed L2 becomes a requirement; the `VersionedCache`
   surface can be re-implemented over it without touching consumers.
2. **Two-level permission tags (§2.1; cross-cutting S3).** Chosen: tenant `permissions` plus the
   user's `PermissionsTag`. Alternative: the tenant tag only, which makes every membership edit
   force every active user to recompute once. Flips if the user-level bump rule proves costly in
   the emitter or membership edits turn out rare.
3. **Format version as a constant plus a shape test (§2.7).** Alternative: fold the deployment's
   build identity into every wire tag — zero discipline, one refetch of every client cache per
   deploy. Flips if shape drift without a bump ever ships despite the test.
4. **Sealing `Settings` (§5.1).** Alternative: allow a distribution leaf with typed, indexed
   tenant columns other tables reference, exposed on the client view as a JSON bag. A
   `References` key covers the unconstrained case (§5.3). Flips on the first distribution that
   needs a referenced tenant-wide FK setting.
5. **Calendars in Core versus Locale packs (§8.2).** Alternative: `Tellma.Locale.Sa` and
   `Tellma.Locale.Et` now, referenced per distribution. Flips when Locale packs exist and carry
   enough else to justify the references.
6. **Umm al-Qura beyond AH 1500 (§8.3).** Chosen: tabular `HijriCalendar` approximation.
   Alternative: reject the date with a diagnostic. Flips if a tenant reports a wrong rendered
   date rather than a refused one.
7. **No tenant `DefaultCulture` (§9.2).** Chosen: formatting culture = request culture; a regioned
   `PreferredLanguage` carries digits and separators. Alternative: a typed, required specific
   culture on `core.Settings` for predictable non-browser output. Flips if MCP or scheduled exports
   produce inconsistent number formats across users of one tenant.
8. **`Tellma-Time-Zone` display header shipped now (§9.2; cross-cutting S15).** Alternative: the
   tenant zone as the only zone this release, adding the header later. Flips if the header's
   precedence over the stored preference confuses scheduled-export output.
9. **`Accept-Language` above the stored preference (§9.2).** Alternative: preference first, which
   protects a user whose browser sends an unexpected OS language to a non-SPA endpoint. Flips if
   a second browser client that cannot set the header appears.
10. **Resources never fall back to the tenant primary (§10.3).** Alternative: the chain request
    culture → tenant primary → English. Flips if tenants ship translations the distribution does
    not, which the resource model does not allow today.
11. **No age limit on any kind (§3.2).** Chosen: the tag is the only channel; `refresh-caches` and
    the migrator's wholesale bump repair out-of-band writes, and spec 0013's `PermissionsMaxAge`
    stands alone as the security backstop. Alternative: a bounded age per kind, which reloads
    lists the SPA would still cache under the unmoved tag. Flips if scripted writes to reference
    tables go unnoticed in production.
12. **`UPDLOCK` on the tag rows a batch bumps, `REPEATABLEREAD` on the rest (§2.5).** Alternative:
    shared locks everywhere, accepting a deadlock-and-retry between two writers of one row (two
    role saves), as spec 0011 accepts for its concurrency guard. Flips if the update lock's
    serialisation of concurrent writers of one cacheable table shows up under load.
13. **Explicit `SettingKeys(typeof(…))` registration (§5.3).** Alternative: manifest or reflection
    discovery. Flips when the manifest generator exists.
14. **`core.UserPreferences`, not `UserSettings` (settled; §2.1).** The opaque bag owned by spec
    0013 keeps "preferences" as the person's word and "settings" as the tenant's; symmetry with
    `Settings` lost.
15. **Tenant-level keys only; the person's preferences are the opaque bag (§5.3; cross-cutting
    S24).** Alternative: declared `SettingKey<T>` user-scope keys with validation and labels. Flips
    if the administrator-written keys of spec 0013 (`nav.pinned`) ever need a schema.
16. **Locks on the tag rows inside the persist transaction (§2.5; cross-cutting S26).**
    Alternative: an unlocked re-check leaving a race with a concurrent revocation. Flips if the
    waits on the tag rows measurably delay permission or settings writers under load.
17. **Packs declare no language support (§7.3; cross-cutting S35).** Alternative: a pack-level
    `SupportedLanguages` declaration with a startup warning. Flips when a compliance pack ships
    strings for a strict subset and silent English fallback is unacceptable.
18. **`SqlOptions.UserIds` names the users a raw statement's bump affects (§2.5; cross-cutting
    S37).** Alternative: derive nothing from raw SQL and bump every user's column. Flips if the
    startup gate cannot reliably detect raw writes to user-level tables.
19. **`refresh-caches` bumps both user-level columns of every user (§6.4).** Alternative: tenant
    rows only, matching the migrator's wholesale bump. Flips if the cold path for every user after
    an admin refresh proves disruptive on large tenants.
20. **A `core.settings` provisioning step completing a `HasData` placeholder with `TenantId = 0`
    (§5.7).** Alternative: no placeholder — the step inserts the row, and the loader has no
    special case. Flips if a `System`-scope acceptance of `TenantId = 0` is judged too wide a
    hole in the guard.
21. **Entry categories written verbatim into the securable resource (`core.Settings.gl`) (§5.5).**
    Alternative: PascalCase the segment (`core.Settings.Gl`) to match `core.Settings.General`.
    Cosmetic under case-insensitive comparison; flips on a naming-consistency preference.
22. **The SPA renders server codes and labels from the string pack; the server still renders
    `message` for every other reader (§10.4).** Alternative: server-rendered text only,
    re-fetched on a language switch, or every text carried in all offered languages. Flips if the
    pack's size or the drift between the two ICU renderers (calendar-aware dates) proves costly.

## Appendix A — Ethiopian calendar conformance vectors

Gregorian → Ethiopian `(Year, Month, Day)`; the suite of §12 pins these and their inverses.

| Gregorian | Ethiopian |
|---|---|
| 1752-09-14 | 1745-01-04 |
| 1900-01-01 | 1892-04-23 |
| 1971-09-11 | 1964-01-01 |
| 2000-01-01 | 1992-04-22 |
| 2007-09-12 | 2000-01-01 |
| 2011-09-11 | 2003-13-06 |
| 2015-09-12 | 2008-01-01 |
| 2019-09-11 | 2011-13-06 |
| 2020-01-01 | 2012-04-22 |
| 2024-09-11 | 2017-01-01 |
| 2026-09-04 | 2018-12-28 |
| 2500-01-01 | 2492-04-23 |

Leap rule: an Ethiopian year `y` has 366 days when `y % 4 == 3` (Pagume has 6 days); New Year
falls on Gregorian 11 September, or 12 September in the year before a Gregorian leap year.
