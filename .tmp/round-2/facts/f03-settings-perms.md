# F03 — Digest: settings, localization, version cache (0013) and users, roles, permissions (0014)

## 1. Sources read

| Path | Lines |
|---|---|
| `docs/specs/0013-settings-localization-and-cache.md` | 1833 |
| `docs/specs/0014-users-roles-and-permissions.md` | 1906 |
| `.tmp/round-2/README.md` (task) | 92 |
| `.tmp/round-2-brief.md` (skimmed for needs) | 462 |

Cross-checked by grep (not digested in full): `0016-crud-web-api.md` §2.4, §3.4, §3.9, §4.3, §6.2
(routes, `MeResult`, headers); `0018-core-and-gl-reference-stacks.md` §3.5, §4.2 (`me`, preference
actions); `0021-notifications-inbox-and-hub.md` hub event table; `0008-queryex.md` §10.4 (calendar
codes).

## 2. Key facts

Convention: in Part A a bare `§` is a 0013 section, in Part B a 0014 section; every other
reference names its spec. Gap and contradiction ids (G*, C*) point to sections 3 and 5.

**Part A — spec 0013 (digest 2.1–2.21).**

### 2.1 Version tags: vocabulary (0013 §2.1)

- A **version tag** is an opaque `Guid`, equality-only, application-generated (one fresh value per
  batch), stored in `uniqueidentifier`. "ETag" is reserved for the blob endpoint, "fingerprint"
  for content hashes (securables registry), "version" alone collides with Queryex. A DTO's
  per-shape constant is its **format version** (§2.7), never stored.
- Tenant-level tags in `core.VersionTags` (one row per name):
  - `settings` — bumped by writes to `core.Settings`, `core.SettingEntries`.
  - `permissions` — bumped by `core.Roles`, `core.Permissions` (0014 declares), and by
    `SettingsService.Save` when a language column changed (§6.3).
  - `entities` — composite; any `[Cacheable]` entity's table. Client meaning: "some cached list
    changed".
  - `entity:<EntityName>` — that entity's table (e.g. `entity:gl.Center`); server list cache and
    the client's per-entity list.
  - pack names — tables with `[BumpsVersionTag("<name>")]`.
- User-level tags = two columns of `core.UserStamps`: `PermissionsTag` (bumped by the user's
  `core.RoleMemberships` rows) and `PreferencesTag` (the user's own `core.Users` row,
  `core.UserPreferences`, `core.NotificationPreferences`).
- Permissions validated at two levels: tenant `permissions` tag AND the user's `PermissionsTag`;
  a permissions cache entry is valid only when both match.
- Name grammar: ≤ 128 chars (`VersionTagNames.MaxNameLength`), ordinal. Plain names
  `^[a-z][a-z0-9.-]*$`; entity names `entity:` + `EntityMetadata.Name`, only via
  `VersionTagNames.ForEntity`; hand-written `[BumpsVersionTag("entity:…")]` fails startup.

### 2.2 Version-tag contracts (0013 §2.2)

Namespace `Tellma.Core.Abstractions.Caching`:

- `VersionTag(Guid Value)` record: `None` (empty, matches nothing → always loads), `New()`,
  `ToWire(int formatVersion)` = `"{formatVersion}.{Value:N}"`.
- `VersionTagNames`: `Settings = "settings"`, `Permissions = "permissions"`,
  `Entities = "entities"`, `EntityPrefix = "entity:"`, `MaxNameLength = 128`, `ForEntity(name)`.
- `enum UserVersionTagNames { Permissions, Preferences }` → columns `PermissionsTag`,
  `PreferencesTag` on `core.UserStamps`.
- `VersionTagSnapshot(TenantId, Tags, ReadAtUtc)` (tenant names only; `Has(name)` false ⇒ that
  kind is uncacheable on this instance) in the process-wide `IVersionTagSnapshots`;
  `UserVersionTagSnapshot(TenantId, UserId, Preferences, Permissions, ReadAtUtc)` in
  `IUserVersionTagSnapshots` (filled only after a batch carrying a connect prologue).
- `enum VersionTagMismatchPolicy { Rerun, Refresh }`; `VersionTagDependency(Name, ExpectedTag,
  OnMismatch)`; `IVersionTagRegistry { Names; Resolve(writtenTables) }` → tenant names + user rules.
- Attributes (`Tellma.Core.Abstractions.Entities`): `[BumpsVersionTag(name)]`,
  `[BumpsUserVersionTag(column, userIdProperty = "UserId")]`, `[Cacheable(MaxRows = 0)]`
  (0 = `TellmaCacheOptions.EntityMaxRowsDefault`). Table type `VersionTagList (Name, Tag, Bumped)`.

### 2.3 Tag tables and declarations (0013 §2.3, §2.4)

- `core.VersionTags(Name nvarchar(128) PK_VersionTags, Tag uniqueidentifier DF NEWID())` —
  non-temporal, no audit, not an entity, absent from Queryex and the CRUD stack. Seeded only by the
  migrator (§2.8).
- `core.UserStamps` (jointly owned with 0014, which adds `LastActiveAt`; 0021 adds `InboxSeenAt`;
  Queryex read-only as `core.UserStamp`, nav `User`): `UserId` PK + FK cascade, `PermissionsTag`,
  `PreferencesTag` (both `uniqueidentifier`, DF `NEWID()`). Seed row for user 1 (0014 `HasData`).
- Tables that never bump: `core.Jobs`, `core.UserStamps`, `core.VersionTags`, `core.Blobs`,
  `core.UserPreferences`, `core.NotificationPreferences` (these two bump user-level via fixed
  rules), `core.ScheduleStates`, `core.JobWorkerState`, `core.Notifications`,
  `core.OutgoingEmails`, `core.OutgoingEmailAttachments`.
- Declarations: `Settings`, `SettingEntry` → `settings`; `Role`, `Permission` → `permissions`;
  `RoleMembership` → `[BumpsUserVersionTag(Permissions, "UserId")]`; `User` →
  `[BumpsUserVersionTag(Preferences, "Id")]`; any `[Cacheable]` → `entity:<Name>` + `entities`;
  `core.UserPreferences`, `core.NotificationPreferences` → fixed rule `Preferences`, `UserId`.
- The `permissions` bump on a language change is an explicit
  `IDataBatch.BumpVersionTag("permissions")` in `SettingsService.Save`.
- **Round-2 note:** `core.Assignments` and report/dashboard definitions (new in round 2) are in
  neither list; whatever cache semantics they need must be declared.

### 2.4 Emitted statements and executor rules (0013 §2.5)

- `@tm_` parameter prefix reserved. **Prelude** `SELECT [Name], [Tag] FROM [core].[VersionTags];`
  heads every tenant batch after 0012's schema guard (the connect prologue's result set 1 on caller
  batches). **Guard** after `BEGIN TRAN` in every caller `Persist` (expected `permissions`,
  `settings`, declared dependencies, the caller's `PermissionsTag`; `REPEATABLEREAD`/`UPDLOCK`) →
  `THROW 50412, N'StaleVersionTag'`. **Bump** last before `COMMIT`: one `@tm_tag` Guid per batch;
  `entities` auto-added with any `entity:*`; a missing row → `THROW 50422, N'VersionTag.Missing'`.
- Cold loads follow the prelude and carry its tag (convergent under a racing bump); a name absent
  from the prelude makes that kind uncacheable (`VersionTagRowMissing` once). Post-batch snapshots
  without read-back: `BatchOutcome.VersionTags`, `BatchOutcome.UserVersionTags` (null without a
  connect prologue).
- **`cache.changed` publication:** for every bumped name that is `settings` or `entity:<Name>`,
  the epilogue calls 0021's `IClientEventPublisher.Publish(batch, ClientEvent("cache.changed", [],
  { tag: <name> }))` — fires after commit, **to every connected user of the tenant**. The
  `permissions` tag and the user-level bumps (`PermissionsTag`, `PreferencesTag`) publish
  **nothing** ("the next request's guard handles them"). The null publisher publishes nothing.
  Registered in `CoreFeature` as `ClientEvent("cache.changed")` (§1.2).
- Out-of-band writes: run the migrator wholesale bump or `settings/refresh-caches`.

### 2.5 Dependencies and mismatch policies (0013 §2.6)

| Dependency | Policy | Behaviour |
|---|---|---|
| `permissions` + user `PermissionsTag` (connect prologue, always) | `Rerun` | discard, refresh, re-run once; second mismatch → `StaleContextException` (503, `Retry-After: 1`) |
| `settings` on `Read` | `Refresh` | result served; fresh settings ride the same round trip; response carries the new tag |
| `settings` on `Validate`/`Persist` | `Rerun` | prologue reports `SettingsStale`; initializer re-run; recompose once |
| `PreferencesTag` | `Refresh` | as settings |
| `entity:<Name>` via `FromCache` on `Read` | `Refresh` | cached list invalidated |
| `entity:<Name>` on `Validate` / persist | `Rerun` | |

### 2.6 Wire form and response header (0013 §2.7)

- `FormatVersion` constants (initially `1`): `TenantSettingsForClient.FormatVersion`,
  `CachedEntitySet.FormatVersion`, 0014's `UserAccess.FormatVersion` (carried to the client by
  0016's `AccessSummary.FormatVersion`).
- Wire tag = `"{FormatVersion}.{Value:N}"`; the **browser stores DTOs keyed by that string and
  refetches when it changes**. The database never stores a format version.
- **Every authenticated response** carries
  `Tellma-Version-Tags: settings=<wire>, permissions=<wire>, preferences=<wire>, entities=<wire>`
  (0016 `TellmaHeaders.VersionTags`; `settings`/`permissions`/`entities` from
  `BatchOutcome.VersionTags`, `preferences` from `BatchOutcome.UserVersionTags.Preferences`).
- `entity:*` tags are deliberately **absent** from the header; a changed `entities` makes the
  client fetch `settings/entity-tags` (§6.2) and refetch only the lists whose tag moved.
- The same four wire tags travel in 0016's `MeResult.Tags`.
- They double as a coarse activity signal (tenant `permissions` moves whenever any role changes),
  "never to be replaced by a counter".
- A checked-in DTO shape-snapshot test pairs each DTO's property tree with its `FormatVersion`.

### 2.7 Migrator, errors, round trips (0013 §2.8–§2.10)

- Migrator, every `migrate`: insert missing registry names with `NEWID()`, then
  `UPDATE [core].[VersionTags] SET [Tag] = NEWID();` — **every tag moves after every migration**,
  so every browser cache keyed on wire tags refetches after a migrating deploy.
- Errors: `50412 StaleVersionTag` → re-connect cold, recompose once, then `StaleContextException`
  (503, problem `stale-context`, `Retry-After: 1`); `50422 VersionTag.Missing` →
  `ValidationException` code `VersionTag.Missing`.
- `StaleContextException(IReadOnlyList<VersionTagDependency> Dependencies)` in
  `Tellma.Core.Abstractions.Errors`; empty after a 50412.
- Warm round-trip budget: read 1; create 1; update 2; +1 cold/stale; +2 when the guard fires. "No
  tag or setting is ever read in a round trip of its own on a warm path."

### 2.8 The versioned cache (0013 §3)

- `enum CacheOutcome { Hit, Miss, Stale, Oversized, Uncached }`;
  `CacheResult<TValue>(Value, Tag, Outcome)`.
- `TellmaCacheOptions` (section `Tellma:Cache`, `ValidateOnStart`): `SettingsEntries = 10000`,
  `PreferencesEntries = 50000`, `PermissionsEntries = 50000`, `EntityRows = 2000000`,
  `EntityMaxRowsDefault = 1000`, `EntityMaxRowsCeiling = 10000`, `TagSnapshots = 10000`.
- `VersionedCache<TKey, TValue>`: `GetAsync(key, currentTag, database)`, `Peek(key, currentTag)`,
  `Set(key, value, tag)`, `Remove(key)`, protected `LoadAsync`. Private `MemoryCache` per kind,
  single flight, **no age limit** (the tag is the only channel; only permissions carry 0014's
  `PermissionsMaxAge` backstop), keys start with tenant id, scoped façades over singleton stores,
  immutable shared values.
- Kinds: `settings` `(TenantId)` → `TenantSettings` + pre-serialized client bytes;
  `preferences` `(TenantId, UserId)` → `UserProfile` (0014); `permissions` `(TenantId, UserId)` →
  `UserAccess` (0014; valid when both tags match); `entities` `(TenantId, EntityName)` →
  `CachedEntitySet<TEntity>` (unit: rows).
- 0014's connect cache `(TenantId, Subject)` → last `ConnectedUser` is not a `VersionedCache`.
- This is **server-side in-process** caching only; 0013 says nothing about browser storage
  beyond "the browser stores DTOs keyed by that string".

### 2.9 Cacheable entities (0013 §4)

- `[Cacheable(MaxRows)]` for small read-mostly reference data (countries, currencies, units).
  **Users, roles and anything with RLS are not cacheable**; "their display names reach the client
  through the details page's related-entity projection". Calendar month names are resources.
- Produces: tag `entity:<Name>` (+ `entities`); `Read` securable with `FilterRoot = null` (caller
  still needs `Read`); `EntityService<TEntity,TKey>.GetAllCachedAsync(DetailsRequest)` and the
  route `{resource-segment}/all` (body `AllRequest(Select, Include)`, 0016); server-side
  `ICacheableEntities.GetAsync<TEntity>()` / `IDataBatch.FromCache<TEntity>()`.
- Startup: `TopLevelEntity<int>`, no child collections, `1 ≤ MaxRows ≤ 10000`, `Read`
  `FilterRoot == null`.
- `CachedEntitySet<TEntity>(VersionTag Tag, DateTimeOffset LoadedAt, IReadOnlyList<TEntity> Items,
  IReadOnlyDictionary<int, TEntity> ById)`, `FormatVersion = 1`; `Items` key-ordered and **include
  inactive rows** (consumers filter).
- `ICacheableEntities { GetAsync<T>(); Peek<T>(); GetWireTagsAsync() }` (entity name → wire tag;
  absent name → `VersionTag.None.ToWire(...)`).
- Load = capped probe: every scalar property (no navigations), `OrderBy = "Id"`,
  `Take = MaxRows + 1`. Overflow → `Oversized`, null value, `CacheableEntityOverflow` logged once;
  the consumer re-reads uncached.
- The server probe selects scalars only; whether the `/all` route's `Select`/`Include` lets the
  client ask for navigations is 0015/0016's.

### 2.10 Tenant settings tables (0013 §5.1, §5.2)

- `core.Settings` (sealed entity; **one row**, `CK_Settings_SingleRow CHECK ([Id] = 1)`; temporal →
  `core.SettingsHistory`; UDTT `SettingsList`; `TopLevelEntity`; `[Multilingual]` on `Name`) — the
  settings "read by the platform on the request path". Columns: `Id int`; `TenantId int` (routing
  guard; `0` = unprovisioned placeholder); `Name nvarchar(255)` not null, `Name2`, `Name3`
  nullable; `PrimaryLanguage varchar(35)` (BCP 47 ∈ declared languages); `SecondaryLanguage`,
  `TertiaryLanguage varchar(35)` nullable (`CK_Settings_Languages`: distinct; tertiary requires
  secondary); `PrimaryCalendar varchar(16)` DF `'gc'`; `SecondaryCalendar varchar(16)` nullable
  (`CK_Settings_Calendars` ≠ primary); `TimeZone varchar(64)` (IANA, normalized); `CreatedAt`,
  `CreatedById`, `ModifiedAt` (concurrency token), `ModifiedById`; `ValidFrom`/`ValidTo`.
- `HasData`: `Id=1, TenantId=0, Name=N'', PrimaryLanguage='en', PrimaryCalendar='gc',
  TimeZone='UTC'`, audit = system user 1.
- `core.SettingEntries` (temporal; UDTT `SettingEntriesList`; sequence `core.sq_SettingEntries`):
  `Id int`, `Key nvarchar(128)` (`UX_SettingEntries_Key`), `Value nvarchar(max)` (one JSON value,
  `[JsonColumn]`), audit columns. A row exists **only when the value departs from the default**.
- Both are Queryex roots (`core.Settings`, `core.SettingEntry`) with no endpoint beyond
  `SettingsService`; not contributed as stacks (so no `search`/`details` CRUD routes).

### 2.11 Setting keys (0013 §5.3, §5.4)

- `enum SettingVisibility { Server, Client, Secret }` — `Secret`: server-only, protected at rest,
  never returned once stored.
- `abstract record SettingKey { Name; Category; Visibility; ValueType; References; DefaultJson;
  LabelKey }`; `sealed record SettingKey<T>(name, defaultValue, category = null,
  visibility = Server, validate = null, typeInfo = null, references = null)` with `DefaultValue`,
  `Validate`. Declared as public static fields on a holder type, registered via
  `FeatureContribution.SettingKeys(typeof(Holder))`, collected **in declaration order**.
- `ISettingKeyRegistry { Keys; Find(name); Categories }`.
- Rules (startup gate unless noted): name grammar `^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)+$`,
  ≤ 128 (`gl.posting.autoNumberOnPost`); unique; `T` ∈ `bool, int, long, decimal, string,
  DateOnly, TimeOnly, Guid`, enums (as strings), nullable and array forms, or a key-supplied
  `JsonTypeInfo<T>`; `Category` default = first segment, single segment; `general` shares
  `core.Settings.General`; `References` = an `int`-keyed stack entity, `T` ∈ `int, int?, int[]`,
  **no FK, never checked** (deleted target → bare id); `Secret` and `References` exclusive;
  `Validate` returns a dotted PascalCase code or null (a throw = 500).
- `Secret` at rest: Data Protection ciphertext (purpose `"Tellma.Core.Settings.Secret"` + tenant
  id), unprotected on first `Get<T>`; unreadable/invalid stored values read as the default.
- `LabelKey` = `"Setting_" + Name` with `.`→`_` (`Setting_gl_posting_autoNumberOnPost`); server
  fallback = humanized last segment. **"The settings page renders the same key from the string
  pack."**
- Example distro holder `GlSettings`: `AutoNumberOnPost` (`bool`, default `true`, `Client`),
  `DefaultCenterId` (`int?`, `references: typeof(Center)`).
- **Not declared on a key:** display order beyond declaration order, sub-groups within a category,
  description/help text, UI hints (widget, min/max, multiline, placeholder), category label or
  order, or a "requires restart/reload" flag.

### 2.12 Settings securables (0013 §5.5)

- `core.Settings.General` × `Read` and × `Save` (`FilterRoot = null`; **`Save` sensitive — step-up
  per 0014**) — the typed row and `settings/refresh-caches`.
- `core.Settings.<Category>` × `Read` and × `Save` (`FilterRoot = null`, **not sensitive**) per
  distinct category other than `general`. Category written **verbatim** (`core.Settings.gl`;
  review flag 21); comparison ordinal-ignore-case.
- `Read` on a category (satisfied by a `Save` grant) admits it to `settings/details`; `Save` admits
  it to `settings/save`.
- `settings/client` needs **no securable** (every member; `Client` keys only). `Server` keys leave
  the server only through `details`; `Secret` keys never.

### 2.13 Resolved views and the settings cache (0013 §5.6)

- `TenantSettings` (immutable, server): `Names`, `Languages` (1–3), `Shape`, `Calendars` (1–2,
  primary first), `TimeZone`, `SqlServerTimeZoneName`, `ModifiedAt`, `Entries`, `Get<T>(key)`,
  `Today(clock)`.
- **`TenantSettingsForClient(int TenantId, IReadOnlyList<string?> Names,
  IReadOnlyList<LanguageInfo> Languages, IReadOnlyList<string> Calendars, string TimeZone,
  IReadOnlyDictionary<string, JsonElement> Entries)`**, `FormatVersion = 1`: names; languages
  (code, symbol, native name, direction); calendar codes; IANA zone id; `Client`-visible entries,
  stored or default ("the client never learns which"); never `Server`/`Secret`; no related
  entities. Pre-serialized UTF-8; `settings/client` writes the bytes.
- `ITenantSettingsCache { GetAsync(); GetForClientAsync() → (byte[] Json, string WireTag); Peek() }`.
- Read path: request-context initializer chain calls `GetAsync()` after connect (0014, `Order`
  100), before negotiation (`Order` 200); value lands on `RequestContext.TenantSettings`.
- **Absent from the client view:** `ModifiedAt`, any deployment/distribution identity or version,
  branding/logo, first-day-of-week, number/currency format, default culture.

### 2.14 Cold load, tenant-id guard, provisioning (0013 §5.7)

- The cold load rides the connect round trip (two selects: the typed row, all entries); an
  unresolvable language/calendar falls back to `en`/`gc`/`UTC` (Critical log), never a crash; a
  `TenantId` mismatch is a 500 (`TenantId = 0` accepted only in a `System` scope).
- `SettingsProvisioningStep`: `Name = "core.settings"`, `Order = 15`, `Version = 1` (between
  `core.bootstrap-administrator` 10 and `core.blob-container` 20). Sets `TenantId`, `Name` (from the
  catalog), `PrimaryLanguage` = distribution default, `PrimaryCalendar = 'gc'`, `TimeZone = 'UTC'`.
  A new tenant is therefore mono-lingual, Gregorian, UTC until the admin edits settings.

### 2.15 The settings service (0013 §6)

`SettingsService` is `[ApiRoute("settings")]` (0015), registered with
`contribution.ApiService<SettingsService>()`. Wire paths are 0016's (confirmed by grep of 0016
§2.4): **`POST /{tenantId}/api/web/settings/{client|entity-tags|details|save|refresh-caches}`**, JSON
body; `settings/client` is member-only "not anonymous: the client settings document is tenant
content" (0016 review flag 19); `settings/client` and `settings/entity-tags` carry
`NoActivityStampMetadata` (0016 §4.3, with `users/me` and `inbox/summary`). 0016 §6.2: every
tenant-surface response also carries `Content-Language`, `Tellma-Calendar`, `Tellma-Build`,
`Tellma-Trace-Id` and `Tellma-Version-Tags`.

| Action | Signature | Gate |
|---|---|---|
| `client` | `ClientAsync(string? ifTag)` → `(byte[] Json, string WireTag)?` (null = unchanged; 0016 shapes both) | member-only, idempotent, `Mutation = false`, no securable |
| `entity-tags` | `EntityTagsAsync()` → `{ "gl.Center": "1.<guid>", … }` | member-only, idempotent, `Mutation = false` |
| `details` | `DetailsAsync()` → `TenantSettingsDetails` | member-only; in code one `IAccessEvaluator.Evaluate` over `core.Settings.General` + every category × `Read`; `ForbiddenException` when none |
| `save` | `SaveAsync(TenantSettingsPatch)` → `TenantSettingsForClient` | member-only; in code `RequireAsync`: `General × Save` (step-up) when any typed member listed; `<Category> × Save` per entry category |
| `refresh-caches` | `RefreshCachesAsync()` → nothing | resource `core.Settings.General`, action `Save`, idempotent, **sensitive** |

- `client` and `entity-tags` run a `Read` batch through 0014's `IGuardedBatchRunner`; on a warm
  instance neither adds a statement.
- `TenantSettingsDetails(Settings? General, IReadOnlyList<SettingKeyDescriptor> Keys,
  IReadOnlyDictionary<string, JsonElement> Values, IReadOnlyList<string> SetKeys,
  RelatedEntities Related)` — only categories with `Read`. `General` (the typed row, whose
  `ModifiedAt` is the stamp `save` expects) present when `Read` on `core.Settings.General`.
- `SettingKeyDescriptor(string Name, string Category, SettingVisibility Visibility,
  SettingValueType ValueType, bool IsNullable, bool IsArray, IReadOnlyList<string>? EnumValues,
  string? References, JsonElement DefaultJson)`; `References` = the referenced entity's name.
- `enum SettingValueType { Boolean, Int32, Int64, Decimal, String, Date, Time, Guid, Enum, Json }`.
- `Values` = current value (stored or default) of every described key **except `Secret`**;
  `SetKeys` = keys with a stored row (all the editor learns about a secret). `Related` = per
  `References` key, the rows whose ids appear in `Values`, through the target stack's
  `RelatedSelect` projection (0015 `StackDescriptor`), no RLS; a missing row → bare id.
- `TenantSettingsPatch(IReadOnlyList<string> Fields, string? Name, Name2, Name3, PrimaryLanguage,
  SecondaryLanguage, TertiaryLanguage, PrimaryCalendar, SecondaryCalendar, TimeZone,
  IReadOnlyDictionary<string, JsonElement?>? Entries, DateTimeOffset ExpectedModifiedAt)` —
  field mask; entry keys listed as `entries.<key>`; listed `null` clears a typed member; listed
  `null` entry resets to default (deletes the row). **`ExpectedModifiedAt` is required** and is
  the typed row's stamp, even for an entries-only save (a caller with only `core.Settings.gl`
  rights does not receive `General` — see contradictions).
- Save pipeline (two round trips): authorize → `Validate` batch (single row + listed entries via
  `KeySetRestriction("Key", StringList)`) → in-memory merge and validate → `Persist` batch
  (`Save<Settings>` under `ConcurrencyMode.Check`, `Save<SettingEntry>`, `Delete<SettingEntry>`
  `ByIds`, `BumpVersionTag("permissions")` when a language column changed) → warm the writer
  (`Set` both views under `BatchOutcome.VersionTags["settings"]`) → mirror names into the catalog
  via `ITenantCatalog.RenameAsync` in `OnCommitted` (failure logged, never surfaced).
- A language change switches the tenant's `MultilingualShape` schema and, via the `permissions`
  bump, forces every user's next request to recompose; 0015 nulls gated twins thereafter.
- `refresh-caches`: one raw `Persist` statement updating every `VersionTags` row and both
  `UserStamps` columns of every user; after commit publishes `cache.changed` for `settings` and
  every `entity:<Name>`.
- Validation codes (§6.5; path = patch member or `entries.<key>`): `Required`,
  `Settings.FieldUnknown`, `Settings.LanguageNotOffered`, `Settings.LanguagesNotDistinct`,
  `Settings.TertiaryRequiresSecondary`, `Settings.CalendarNotRegistered`,
  `Settings.CalendarsNotDistinct`, `Settings.TimeZoneUnknown`, `Settings.KeyUndeclared`,
  `Settings.EntryTypeMismatch`, the key's own `Validate` code, `Concurrency.StampRequired`. Stale
  stamp → 0015 `ConcurrencyException` (409).

### 2.16 Languages and the multilingual shape (0013 §7)

- `LanguageInfo(string Code, string NativeName, string Symbol, bool IsRightToLeft)`.
- `ILanguageCatalog { Offered; Find(code) /* whole catalogue */; IsOffered(code) }`
  (ordinal-ignore-case).
- Catalogue (`Code Symbol`): `en E`, `ar ع`, `am አ`, `om Om`, `ti ት`, `so So`, `sw Sw`, `fr F`,
  `es Es`, `pt P`, `de D`, `tr T`, `ur ا`, `hi हि`, `zh 中`. Symbols unique; every code a
  predefined culture.
- Distribution: `tellma.Languages(codes)` (first code = **distribution default**) and
  `tellma.AddLanguage(info)`. Packs declare no language support; missing strings fall back to
  English per key.
- **Two axes:** tenant content languages (≤ 3; decide which of `Name`/`Name2`/`Name3` holds which
  language and their labels, "and nothing else") vs request culture (any offered language; "an
  English-speaking auditor at an Amharic tenant reads English messages over Amharic content").
- `enum MultilingualShape { Primary = 1, PrimaryAndSecondary = 2, All = 3 }` = `Languages.Count`;
  keys `IQueryexSchemaProvider.GetSchema(shape)` (0012), ≤ 3 schemas per process.
- `[Multilingual]` on the primary property; twins `<Name>2`, `<Name>3` (nullable, same max length).
  Gated twins nulled by 0015 preprocessing; **a twin beyond the tenant's shape is absent from the
  wire** (0016). Excel import of a gated twin → `Import.LanguageNotConfigured`.
- Labels (`ILabelProvider`, server-side): `PropertyLabel` → `<Schema>_<Entity>_<Property>` → base
  assemblies → Core's bare `<Property>` (`Name`, `Code`, `IsActive`, `Description`, `CreatedAt` …)
  → humanized. Twin label = primary label + `" (" + Symbol + ")"` of the tenant language at that
  position (`Name (E)`, `Name (ع)`); mono-lingual tenant → no suffix; a twin beyond the shape has
  no label and is not offered. `EntityLabel(T)` → `<Schema>_<Entity>`; plural
  `<Schema>_<Entity>_Plural` → singular; enum `<Schema>_<Enum>_<Value>`.
- Content fallback (§10.3): requested index → primary (`Name3 ?? Name`, `Name2 ?? Name`), "applied
  by the client for display and by the server wherever it renders a name".

### 2.17 Calendars (0013 §8)

- `CalendarCodes`: `Gregorian = "gc"`, `UmAlQura = "uq"`, `Ethiopian = "et"` (= 0008 §10.4's
  reserved `cal` codes). `enum DateStyle { Short, Medium, Long, Full }`.
- `ICalendarSystem { Code; MonthCount; MinSupported; MaxSupported; Decompose; Format(date | instant,
  …, DateStyle); MonthName; TryParse }`; `ICalendarRegistry { Codes; this[code]; TryGet(code) }`.
- `gc` over `GregorianCalendar`; `uq` over `UmAlQuraCalendar` (AD 1900-04-30 … 2077-11-16 =
  AH 1318 … 1500; beyond → `HijriCalendar` tabular approximation, review flag 6); `et` a port
  (JDN conversion, **13 months**, leap `year % 4 == 3`, single era, AD 1752-09-14 … 2500-01-01,
  vectors in Appendix A; New Year 11 Sep, or 12 Sep before a Gregorian leap year).
- Distros add calendars via `contribution.Calendar<TCalendar>()` (codes unique, lower-case,
  ≤ 16 chars).
- Month names: Core resources `Calendar_uq_Month1…12`, `Calendar_et_Month1…13` (so they are in the
  string pack); `gc` from the culture. Server digits always ASCII.
- Tenant: `PrimaryCalendar` (required, `gc`), `SecondaryCalendar` (optional) — **at most two
  calendars per tenant**. User: 0014 `User.PreferredCalendar` (outside registry → 0018
  `Users.CalendarUnknown`; outside the tenant's pair → ignored). Request: `Tellma-Calendar`.
- The `uq` month-map Queryex amendment is **deferred**; per 0008 §10.4 both `'uq'` and `'et'`
  produce QX3101 today (only `'gc'` is emitted).
- All formatting here is server-side C#; the SPA's calendar rendering is not specified in 0013.

### 2.18 Time zones and negotiation (0013 §9)

- Tenant zone (`core.Settings.TimeZone`) binds Queryex `today()` and `TimeZone` (business fact);
  display zone (`LocalizationContext.TimeZone`) only formats. **No `X-Today` header.**
- `LocalizationContext(Culture, Calendar, TimeZone, ContentLanguageIndex)`;
  `ILocalizationNegotiator.Negotiate(NegotiationInput)` never throws.
- Precedence (override → header → preference → tenant → distribution default):
  - Culture: `MessageLanguage` (MCP only) → `Accept-Language` (first three q-ranges, `-u-`/`-x-`/
    `-t-` stripped, language-subtag match with parent fallback against `Offered`, region kept
    e.g. `ar-SA`) → `PreferredLanguage` → tenant `PrimaryLanguage` if offered → distribution
    default.
  - Calendar: `MessageCalendar` → `Tellma-Calendar` (∈ the tenant's pair) → `PreferredCalendar`
    (∈ pair) → tenant `PrimaryCalendar`.
  - Display zone: `Tellma-Time-Zone` (IANA) → `PreferredTimeZone` → tenant `TimeZone`.
- "`Accept-Language` sits first because **the SPA sets it explicitly to the user's chosen
  language on every call**." (Requests the SPA does not issue through its HTTP layer — `<img>`
  blob loads, navigations — carry the browser's own `Accept-Language`.)
- Invalid header values are **ignored** (`tellma.localization.headers.rejected`), never 400.
- Responses carry `Content-Language` and `Tellma-Calendar` with the **effective** values (0016),
  so a drifted client corrects itself.
- `ContentLanguageIndex` (1..3): position of the tenant content language whose language subtag
  equals the negotiated culture's, else 1; no stored preference.
- Formatting culture = request culture; a regioned `PreferredLanguage` (`ar-SA`) carries digits and
  separators (review flag 7: no tenant default culture). Negotiated `CultureInfo` has
  `DateTimeFormat.Calendar = GregorianCalendar`.
- Initializer `Order` 200 sets `Language`, `Culture`, `CultureInfo`, `Calendar`, `CalendarSystem`,
  `TimeZone`, `TenantTimeZone`, `Today`, `ContentLanguageIndex`, `TenantSettings` on the request
  context. Background scopes negotiate from the run-as user's preferences, then the tenant.

### 2.19 Resources, ICU, `LocalizationScope` (0013 §10.1–§10.3)

- Each assembly ships `Resources/Strings.resx` (neutral English) + `Strings.<lang>.resx`, marker
  class `Strings`. Keys `PascalCase_Underscored`:
  - entity labels `<Schema>_<Entity>`, `<Schema>_<Entity>_Plural`, `<Schema>_<Entity>_<Property>`
    (`Gl_Center`, `Gl_Center_Plural`, `Gl_Center_Name`, `Core_User_Email`);
  - enum values `<Schema>_<Enum>_<Value>` (`Gl_CenterType_Abstract`);
  - settings `Setting_<key . → _>`; validation codes `.`→`_` (`Settings_LanguageNotOffered`);
  - problems `Problem_<code - → _>` + `Problem_<…>_Detail` (`Problem_concurrency_conflict`);
  - months `Calendar_<code>_Month<n>`;
  - also `Notification_`, `NotificationType_`, `NotificationChannel_` prefixes (the resource audit
    of §12 recognises them).
- `Resources/ServerStrings.resx` (marker `ServerStrings`) = text the SPA never renders (email
  bodies, document footers); **never in the string pack**.
- ICU conventions: `{count, plural, …}`, `{gender, select, … other {…}}` (`other` mandatory),
  **named arguments only**.
- `IcuStringLocalizerFactory` (server): calendar-aware `{d, date, short|medium|long|full}`;
  malformed pattern → raw value. The SPA needs an ICU renderer with the same semantics (0013 review
  flag 22 names "drift between the two ICU renderers" as a risk).
- `LocalizationScope.Enter(LocalizationContext)` / `Current` — async-local, nestable override for
  rendering text for someone other than the caller (0021 §2.4 per-recipient notification renderer,
  0018 §3.3 welcome email). Server-only; there is no client counterpart.
- Resource fallback: culture parent chain → neutral English, **no tenant-primary hop**; missing →
  key text + `tellma.localization.missing` once per key per culture.

### 2.20 The string pack (0013 §10.4)

- `StringPack(string Language, string Version, ReadOnlyMemory<byte> Json)` — flat JSON object, resx
  keys verbatim; `IStringPackProvider.Get(language)` (null outside the offered set).
- Composed at startup from every composed feature's assembly (Core, packs, distribution);
  duplicate key across assemblies = `CompositionProblem`. `Version` =
  `DeploymentVersions.DistributionVersion` (0007).
- **Route `GET /api/strings/{language}?v=<version>`** (0016) — **tenantless and anonymous**,
  compressed; `Cache-Control: public, max-age=31536000, immutable` when `v` matches, `no-store`
  otherwise; non-offered language → 404.
- SPA contract as written: the switchable languages are the **tenant's content languages** from
  `settings/client`; it fetches those packs **and English**, with the `DistributionVersion`
  **stamped into its build** as `v`; merges each beside its own strings; renders every
  server-originated text itself (validation `code` → key by `.`→`_`; problem `code` → `Problem_`
  key; response `arguments` as ICU parameters; raw values formatted in the active calendar; label
  keys as is; `arguments.property` → `<Schema>_<Entity>_<Property>`); a missing key → English pack →
  the server-rendered `message` (0016).
- "No response the SPA caches carries culture-dependent text", so a UI language switch re-renders
  in place with no request and no cache invalidation.

### 2.21 Telemetry and configuration (0013 §11)

- Meter `Tellma.Core`; constants `CacheTelemetryNames`, `LocalizationTelemetryNames`.
- Instruments: `tellma.cache.{requests, load.duration, entries, size, limit, evictions}` (tags
  `cache.kind` ∈ settings, preferences, permissions, entities; `cache.outcome` ∈ hit, miss, stale,
  oversized, uncached; `cache.reason`); `tellma.versiontags.{bumps, stale, missing}`;
  `tellma.settings.entries.invalid`; `tellma.localization.missing` (`culture`);
  `tellma.localization.headers.rejected` (`header` ∈ accept-language, calendar, time-zone). A
  rising `headers.rejected` is the server-side symptom of a drifted SPA.
- No tenant/user/entity/key metric tags; ten structured events (`VersionTagRowMissing`,
  `SettingsSecretUnreadable`, `TenantDatabaseMismatch`, …); no event carries a setting value. All
  server-side.
- Configuration: only `Tellma:Cache`. Languages and calendars are code.

**Part B — spec 0014 (digest 2.22–2.36).**

### 2.22 Vocabulary, enums, well-known names (0014 §2)

- **Tag** (cache validator), **stamp** (`LastActiveAt`, `ModifiedAt`), **fingerprint** (SHA-256 of
  the securables registry; deploy-scoped; travels in `me`; never stored in a tenant DB),
  **format version** (`UserAccess.FormatVersion = 1`; "the SPA's copy, which outlives deploys, is
  guarded the same way").
- **Securable** = `(Resource, Action, FilterRoot?, IsSensitive, Feature, Owner?)`.
- **Resource** = entity name (`core.User`, `core.Role`, `gl.Center`; fork-stable — a distro's
  `MyUser : User` keeps `core.User`); non-entity resources use the same grammar
  (`core.Settings.General`, `core.Settings.<Category>`): ≥ 2 segments of `[A-Za-z][A-Za-z0-9]*`,
  ≤ 128 chars, ordinal-ignore-case, stored as registered.
- **Action** = PascalCase ≤ 32 chars. Platform set (`AccessActions`): `Read` (query, details,
  export in any format), `Save` (create, update, import), `Delete` (by ids, by query, with
  descendants), `Activate` (both directions), `Invite`, `Preferences` (another user's bag),
  `Credentials` (service account client), `Retry`, `Cancel`, `Resume`, `Wildcard = "*"`.
- **Wildcard** `*` only, either position of a stored permission; no prefix wildcards (`gl.*`).
- **Display form** `resource:action` (logs, diagnostics, role editor). Route segments
  (`users`, `activate`) are never permission keys.
- `enum UserKind { Individual, System, Service }`; `enum UserState { New, Invited, Joined }`;
  `enum InviteStatus { Invited, Reinvited, Active }`; `enum Gender { Female, Male }`.
- `WellKnownIds`: `SystemUserId = 1`, `AdministratorRoleId = 1`, `AdministratorPermissionId = 1`,
  `SystemAdministratorMembershipId = 1`, `ReservedIdBandEnd = 999`.
- `CoreResources`: `User = "core.User"`, `Role = "core.Role"`, `SettingsGeneral =
  "core.Settings.General"`, `SettingsPrefix = "core.Settings."`, `Job = "core.Job"`,
  `Schedule = "core.Schedule"`, `Notification = "core.Notification"`, `OutgoingEmail =
  "core.OutgoingEmail"`, `Export = "core.Export"`, `Import = "core.Import"`, `Wildcard = "*"`.

### 2.23 `User` / `core.Users` (0014 §3.1, §3.2, §3.9)

- Class in `Tellma.Core.Abstractions.Access`, non-abstract, unsealed; distro extends
  (`MyUser : User`) and substitutes via `TellmaBuilder.UseEntity<User, MyUser>()`. Attributes:
  `[Table("Users", Schema = "core"), TableType, Temporal, BumpsUserVersionTag(Preferences, "Id"),
  ApiResource, Stack(Operations = StackOperations.All | StackOperations.Enlist)]`;
  `TopLevelEntity, IActivatable`.
- Columns (`core.Users`, temporal → `core.UsersHistory`, UDTT `UsersList`, `core.sq_Users`):
  `Id` (1 = system user); `Kind varchar(10)` (write-once, DF `'Individual'`); `Subject
  varchar(255)` (server-owned, binary collation, `UX_Users_Subject … INCLUDE (Kind, IsActive,
  State)`; OIDC `sub` or service client id); `Email nvarchar(255)` (`[NaturalKey, Searchable]`,
  `UX_Users_Email`; normalised trim+lower; required iff Individual; editable only while `New`);
  `State varchar(8)` (persisted computed: `Joined` if `JoinedAt`, `Invited` if `InvitedAt`, else
  `New`; database-owned); `InvitedAt datetimeoffset(3)`, `InviteStatus varchar(9)`,
  `LastInviteError nvarchar(1024)`, `JoinedAt datetimeoffset(3)` (all server-owned); `Name
  nvarchar(255)` (`[Multilingual, Searchable]`), `Name2`, `Name3`; `ImageId int?`
  (`[BlobReference("user-image", BlobPreset.Avatar, ReadAccess = BlobReadAccess.AnyMember)]`, FK
  `core.Blobs`); `SignatureId int?` (`"user-signature"`, `BlobPreset.Signature`, 1 MiB,
  `AnyMember`; SPA uploads a transparent PNG raster of the stylus capture); `PreferredLanguage
  varchar(35)` (BCP 47; null = tenant primary), `PreferredCalendar varchar(16)` (`gc|uq|et`; null
  = tenant primary), `PreferredTimeZone varchar(64)` (IANA; null = tenant zone); `Gender
  varchar(8)` ("the SPA's gender-inflected strings", 0016 §3.4); `ContactEmail nvarchar(255)`
  (null = Email); `ContactMobile varchar(32)` (E.164); `IsActive bit` (server-owned; only
  `activate`/`deactivate`); audit set; period columns. Child collection `RoleMemberships`
  (`null` untouched, `[]` delete all, list = synchronise). Index `IX_Users_IsActive_Name`.
- Preferences columns validated by 0018's `UserService`: language ∈ `ILanguageCatalog.IsOffered`
  (**not** bound to the tenant's content languages), calendar ∈ `ICalendarRegistry.Codes` (outside
  the tenant pair is ignored by negotiation), zone ∈ IANA list. Typed columns, not bag keys,
  "because background work addresses a user in their language" and they are filterable/exportable.
  A write to them writes a `core.UsersHistory` row and bumps the user's `PreferencesTag`.
- `Enlist` operation admits 0018's `me/save` (the one self-service write; editable set =
  `MeSaveRequest` members, 0018 §3.5).
- State machine (§3.2): create → `New`; bulk-invite returns `Invited`/`Reinvited`/`Active` →
  `Invited` (`Subject`, `InvitedAt`, `InviteStatus` written); per-user error → unchanged +
  `LastInviteError`; first prologue resolving the subject → `Joined` (`JoinedAt`, `ModifiedAt`
  untouched); service `issue-credentials` → `Joined`; sandbox clone clears service accounts →
  `New`. Re-invite of `Joined` = 0018 `Users.AlreadyJoined`. Delivery detail (`Pending | Sent |
  Delivered | Bounced | …`) is a **live drill-down** through the identity server from the user
  details page, never persisted. `IsActive` is orthogonal.
- Deletion: only `State = New` and not System (`Users.OnlyNewUsersDeletable`); not self
  (`Users.CannotDeleteSelf`). Deactivation: `deactivate` action; not self
  (`Users.CannotDeactivateSelf`); next prologue on any instance refuses the user; after commit
  0018 calls `ISessionTerminationListener.TenantAccessRevokedAsync(tenantId, subject)` (closes hub
  connections for that tenant); cookie untouched.
- Row 1 (system user) immutable: `Users.SystemUserImmutable`.

### 2.24 `core.UserStamps`, `core.UserPreferences` (0014 §3.3, §3.4)

- `core.UserStamps`: `UserId` PK/FK cascade, `LastActiveAt datetimeoffset(3)` (throttled, 60 s),
  `PermissionsTag`, `PreferencesTag`, `InboxSeenAt` (0021 `inbox/seen`). Non-temporal, not an
  entity, Queryex read-only `core.UserStamp` (a report can show `LastActiveAt`). Inserted by the
  companion statement (§8.2) with `@tm_tag` for both tags.
- **`core.UserPreferences` (the opaque bag):** `UserId int` + `Key varchar(128)` (PK `(UserId,
  Key)`, FK cascade), `Value nvarchar(max)` (opaque, JSON by convention, ≤
  `MaxPreferenceValueBytes`). Not temporal, **not an entity**, no audit, absent from Queryex and
  Excel. ≤ `MaxPreferenceKeys` keys per user. "The server never interprets a value and caches
  nothing from the bag; **the SPA caches the bag and the profile under `PreferencesTag`**."
- Example keys: `grid.users.columns`, `nav.pinned`, `tour.dismissed`. **"Pinned quick-access
  screens are the key `nav.pinned`"**; an "administrator-managed default for less technical users
  is a tenant setting (spec 0013) the client merges when the key is absent" (no such
  `SettingKey` is declared anywhere).
- One statement over table type `UserPreferenceList (Key, Value)` writes the bag (set = UPDATE +
  INSERT halves, delete = DELETE half); caps → `Users.TooManyPreferenceKeys` (in SQL),
  `Users.PreferenceValueTooLarge` (in C#). Writers: 0018's `me/preferences/set|delete` (caller; bumps
  the caller's `PreferencesTag`) and `preferences/set|delete` (another user under `core.User ×
  Preferences`; bumps the target's tag).
- **Two key classes, told apart by the client only:**
  - **Curated** (`nav.pinned`): explicit act of the user or of an admin holding `core.User ×
    Preferences`; written at once.
  - **Write-behind** (pages last visited, columns last chosen on a search page, a report's last
    parameters, the user a document type was last forwarded to — "each under its own key"): SPA
    applies locally at once, **flushes coalesced per-key writes on a timer and on page hide**;
    admin writes are pointless (overwritten by the next flush).
- **Warm-writer rule:** the write's response returns the new `PreferencesTag` in the header (0016);
  "the SPA adopts [it] while keeping its bag", so only a write from another device or an admin
  makes a device refetch `me` (which carries the bag) once; "a refetch overlays the device's
  unflushed keys onto the fetched bag".
- Server enforces key grammar, the two caps, 0018's per-request checks; interprets nothing; no key
  reserved from an admin.
- 0018 §3.5 (verified by grep): key grammar `^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)*$` ≤ 128
  (`Users.PreferenceKeyInvalid`), duplicates `Users.PreferenceKeyDuplicate`; the set/delete result
  **carries the full bag** and the new `PreferencesTag` wire tag; one round trip. Routes (0016
  §2.4): `POST users/me/preferences/set`, `users/me/preferences/delete`, `users/preferences/get`,
  `users/preferences/set`, `users/preferences/delete` under `/{tenantId}/api/web/`.

### 2.25 `Role`, `RoleMembership`, `Permission` (0014 §3.5–§3.7)

- `Role` (`[Table("Roles", Schema = "core"), TableType, Temporal, BumpsVersionTag("permissions"),
  ApiResource]`, `TopLevelEntity, IActivatable`): `Name` (`[Multilingual, Unique, Searchable]`,
  `UX_Roles_Name`), `Name2`/`Name3` (`[Unique]`, filtered unique indexes), `Code nvarchar(50)`
  (`[NaturalKey]`, `UX_Roles_Code`), `IsPublic bit` (DF 0, `IX_Roles_IsPublic`), `IsActive`
  (server-owned), child `Permissions`.
- **Public roles** (`IsPublic`): permissions apply to every active user; no memberships
  (`Roles.PublicRoleHasMembers`; guard L2; "the membership editor hides public roles"); no
  `Resource = '*'` (`Permissions.WildcardResourceOnPublicRole`); several may exist; none seeded.
- **Administrator** role (`Id = 1`, `Code = 'Administrator'`): cannot be made public, deactivated or
  deleted; its `*/*` cannot be removed or filtered (`Roles.AdministratorImmutable`, guard L3); names
  editable, more permissions may be added. "The role page shows members as a read-only extra with
  links; memberships are edited from the user only."
- `RoleMembership : ChildEntity` (`[BumpsUserVersionTag(Permissions, "UserId")]`): `UserId`
  (`[ParentKey]`), `RoleId` (FK), `Notes nvarchar(1024)` ("added per ticket 4711");
  `UX_RoleMemberships_UserId_RoleId`, `IX_RoleMemberships_RoleId_UserId` ("members of this role").
  **Child of `User` only**; read-only from the role side.
- `Permission : ChildEntity` (`[BumpsVersionTag("permissions")]`): `RoleId` (`[ParentKey]`),
  `Resource varchar(128)` (resource or `*`), `Action varchar(32)` (action or `*`), `Filter
  nvarchar(2048)` (Queryex predicate over the securable's `FilterRoot`), `FilterLanguageVersion
  int?` (`[Derived]`), `Notes`. `CK_Permissions_WildcardHasNoFilter`,
  `CK_Permissions_FilterVersion`; `IX_Permissions_RoleId … INCLUDE (…)`. Duplicates collapsed at save.
- Role-save validation (`RoleAccessRules<TRole>`), path `[i].Permissions[j]`: (1) pair registered
  or `*` — `Permissions.UnknownSecurable`; (2) filter only when resource ≠ `*` and `FilterRoot` ≠
  null (for `Action = '*'`: the root the resource's filterable securables share) —
  `Permissions.FilterNotSupported`; (3) stamp from before image (changed text → current
  `QueryexLanguage.Version`); (4) unchanged text below `QueryexLanguage.Minimum` →
  `Permissions.FilterVersionUnsupported` `{ version, minimum }` (re-enter to affirm); (5) length ≤
  `MaxFilterLength` — `Permissions.FilterTooLong`; (6) `QueryexEngine.Validate` with `Schema` =
  tenant schema, `Root`, `Mode = Filter`, `HasUser = true` (`me()` allowed), no parameters (`@x` is a
  diagnostic), `Limits = AccessLimits` — `Permissions.FilterInvalid` `{ code, …diagnostic args }`;
  (8) `Roles.TooManyPermissions`; (9) `Permissions.EscalationBeyondSelf`.
- A filter naming `Name2` on a mono-lingual tenant is invalid until a language is added.

### 2.26 Seeds and Queryex names (0014 §3.8)

- `HasData` id 1 in each table: the `System` user (never authenticates; admin by membership;
  bypasses guards), its `UserStamps` row, the `Administrator` role, its `*/*` permission, and the
  system user's membership.
- Queryex: `core.User`, `core.Role`, `core.RoleMembership`, `core.Permission`, read-only
  `core.UserStamp`; navigations `CreatedBy`/`ModifiedBy → core.User`, `Image → core.Blob`,
  `core.RoleMembership.User`/`.Role`, `core.Permission.Role`, `core.UserStamp.User`. `Kind`,
  `State`, `InviteStatus`, `Gender` are string-typed enums. `UserPreferences` not exposed.

### 2.27 Securables registry (0014 §4)

- `SecurableRef(Resource, Action)`; `SecurableOwner(Resource, Navigation)`;
  `SecurableDescriptor(Resource, Action, FilterRoot?, IsSensitive, Feature, Owner?)` with derived
  `SupportsFilter`.
- `SecurableRegistryBuilder { Add(resource, action, filterRoot, isSensitive = false, owner = null);
  Alias(old, new); MarkSensitive(r, a); MarkNotSensitive(r, a) }` (marking a pair both ways = a
  composition problem); `ISecurableContributor.Contribute(builder)`.
- `ISecurableRegistry { All /* by resource, then action */; Resources; Find(r, a) /* aliases
  resolved */; ForResource(r); Fingerprint /* hex SHA-256 over sorted
  Resource|Action|FilterRoot|IsSensitive */ }` — singleton, frozen at realise.
- Endpoint metadata: `SecurableEndpointMetadata(Resource, Action)`, `MemberEndpointMetadata()`,
  `NoActivityStampMetadata()` (polling endpoints; `@tm_StampActivity = 0`).
- Who registers (§4.2): stack contributor (0015 §2.7) per `StackDescriptor`: `Read` (Query/Details/
  Export), `Save`, `Delete`, `Activate` (activatable + Save) with `FilterRoot = Resource`; every
  `EntityActionDescriptor.Action` (`FilterRoot = Resource` if `SupportsFilter`); every non-null
  `ApiActionDescriptor.Securable`. Tree ops add nothing; export = `Read`; import = `Save`;
  `[Cacheable]` → `Read` with `FilterRoot = null`. `ApiService<T>()` registers `[ApiRoute]` action
  securables with `FilterRoot = null`; member-only actions register nothing.
  `contribution.Securables(configure)` for non-entity securables (settings categories, hand-mapped
  endpoints).
- **Sensitive (step-up) defaults (§4.3):** `core.User` × `Save | Delete | Activate | Invite |
  Credentials`; `core.Role` × `Save | Delete | Activate`; `core.Settings.General` × `Save`;
  `core.Schedule` × `Save | Delete | Activate`. Distro adds (`MarkSensitive`) or removes
  (`MarkNotSensitive`). Sensitive pairs are refused on MCP. Projection copies the flag as 0011's
  `RequireAssuranceMetadata` from `Tellma:Session:StepUp { Acr, MaxAge }`; host issues 0003's
  **`401 insufficient_user_authentication`**; pipeline raises `StepUpRequiredException` via
  `RequireAsync`. Member endpoints never sensitive. Service accounts get
  `IndividualRequiredException` on sensitive pairs (0016).
- §4.4: "The registry is the role editor's data source (**the SPA labels its resource and action
  keys from spec 0013's string pack**)", the validators' oracle, the drift oracle.
- §4.6 four layers: deny-by-default fallback policy; tenant group filter; endpoint metadata +
  startup audit; **authorization witness** (a 2xx from a handler that never evaluated its declared
  securable becomes a 500, `AccessEvents.MissingWitness`, `tellma.access.witness.missing`). Any new
  round-2 endpoint (assignments, report definitions) must evaluate its securable or the member
  witness.

### 2.28 Permission evaluation (0014 §5)

- `enum AccessOutcome { Denied, Filtered, Unrestricted }`;
  `enum AccessProblemCode { UnknownResource, UnknownAction, FilterUnsupported, FilterInvalid,
  VersionUnsupported }`.
- Grants (record hierarchy; on the wire a `kind` discriminator ∈ `role`, `bespoke`, `system`):
  `RoleAccessGrant(Resource, Action, PermissionId, RoleId, RoleName, IsPublic, Filter,
  FilterLanguageVersion)`, `BespokeAccessGrant(Resource, Action, Filter, Reason)`,
  `SystemAccessGrant(Resource, Action)`. `AccessProblem(PermissionId, RoleId, Code, Diagnostics)`.
- `AccessDecision(Resource, Action, Outcome, FilterTree? Filter, Grants, Problems)` + `IsAllowed`.
- `AccessCriterion(Action, FilterTree Filter, string Reason)`; `IAccessCriteriaProvider { Resource;
  GetCriteriaAsync(int userId) }` — registered per resource, several allowed; awaited once per
  evaluation (not cached by the platform). Platform own-rows reason key `self`.
- `UserAccess` (cached, immutable): `FormatVersion = 1`, `TenantId`, `UserId`, `Tag` (tenant
  `permissions`), `UserTag` (`PermissionsTag`), `ComputedAt`, `ValidatedAt`, `IsSystem`, `Grants`,
  `Problems`, `Decide(securable, criteria, owner)`, `static System`.
- `IAccessEvaluator` (scoped): `EvaluateAsync(r, a)` (records witness), `RequireAsync(r, a)`
  (`ForbiddenException` / `StepUpRequiredException`), `EvaluateAsync(IReadOnlyList<SecurableRef>)`,
  `EvaluateForAsync(userId, r, a)` (no witness), `EvaluateAllAsync()` (one per registered
  securable), `GetAccessAsync()`, `Invalidate(tenantId, userId)`.
- Composition contract (§5.2, pure): system → `Unrestricted`; candidates = grants whose resource is
  `R` or `*` and action `A` or `*` or — **for `A = Read` — any action (write implies read with the
  same filter)**; non-filterable securable keeps only unfiltered rows; any unfiltered candidate →
  `Unrestricted`; else `Or` of distinct `(Filter, stamp)` leaves sorted + bespoke criteria for `A`
  or `*`; empty → `Denied`. Bespoke criteria can grant on their own ("documents assigned to me")
  and never remove anything. Child securables with `Owner` union the owner's decision rebased via
  `FilterTree.Via(Owner.Navigation)` (§5.5; e.g. `core.RoleMembership` → (`core.User`, `User`)).
- What the pipeline does (§5.3, implemented by 0015): filter conjoined into every read, update,
  delete; navigation into `E` touches all columns only when `Read` on `E` is `Unrestricted`, else
  only `E`'s `[RelatedSelect]` projection (other columns → 403 naming `E`); collection ops on
  `Denied` → **403** ("so the SPA can hide the page"); by-id read outside the filter → **404**;
  save two-stage pre-check (outside `Read` filter → 404; outside `Save` grant → 403); in-transaction
  post-check `THROW 50403 RowSecurity` → 403; details related entities/extras under the root's
  `Read`, narrowed to `[RelatedSelect]`, no RLS; FK references validated under the target's `Read`
  filter; users, jobs, exports, imports, notifications self-scoped by bespoke criteria (0018 §3.2,
  0020 §11.2, 0019 §12.1, 0021 §2.2); enlisted groups carry no decision of their own.
- RLS is `FilterTree`, never a SQL Server security policy (§5.4); `SESSION_CONTEXT` only for
  correlation ids.

### 2.29 Permission set cache (0014 §6)

- Valid while **both** tenant `permissions` tag and the user's `PermissionsTag` match;
  `core.Users` writes bump neither (`IsActive`, `Kind`, `State` read live by the prologue).
- Store = 0013 `VersionedCache<(int TenantId, int UserId), UserAccess>` kind `permissions`, sized
  by `TellmaCacheOptions.PermissionsEntries`, fed by `Apply` from prologue results.
- **`PermissionsMaxAge` (default 2 h):** older set → next prologue binds `@tm_ReloadPermissions = 1`
  and returns rows without failing the guard; bounds out-of-band edits. Server-side only.
- **`FastDenyWindow` (default 5 s):** a `Denied` decision from a set validated longer ago triggers
  one prologue-only reconnect and a second decision ("a user granted a permission a moment ago is
  admitted within one window").
- Connect cache `(TenantId, Subject)` → `ConnectedUser`; profile cache = 0013 `preferences` kind
  `(TenantId, UserId)` → `UserProfile`, valid while `PreferencesTag` matches.

### 2.30 Connect prologue and guarded runner (0014 §7)

- `UserProfile(Name, Name2, Name3, Email, ContactEmail, ContactMobile, ImageId, SignatureId,
  PreferredLanguage, PreferredCalendar, PreferredTimeZone, Gender)`.
- `ConnectedUser(UserId, Kind, State, Profile, PreferencesTag, Access, TenantTags,
  LastActivityStampedAt)`; `ConnectPremises` (`ForSubject`, `ForUser`, `ForSystem`);
  `IUserConnector`; `IGuardedBatchRunner.RunAsync<TResult>(purpose, compose, read)` — "the only way
  a service executes a batch on a caller's behalf" (new round-2 services must use it).
- Prologue (`Order` 100) result sets: 0 connect row (always); 1 all tenant tags (always); 2
  permission rows (iff stale/reload); 3 profile (iff stale); then 0013's settings sets (iff
  `SettingsStale` or `@tm_LoadSettings`); body under `IF @tm_Guard = 1`. Activity stamp throttled
  at 60 s (fixed in text). `Invited → Joined` flip on first authenticated request (autocommit,
  exactly once). Settings mismatch fails the guard only when `@tm_SettingsStrict = 1`
  (Validate/Persist).
- Persist re-checks (§7.4): 0013's `50412` guard, then
  `IF NOT EXISTS (… [IsActive] = 1) THROW 50401, N'CallerInvalid'` → `TenantNotFoundException`.
- Round trips (§7.5, warm): query 1; details 1; save (update) 2; create 1; activate/deactivate/
  delete by ids 1 (2 on users/roles); role save 2; "can I" for caller 0; for another user 2;
  **`me` at application start 1** (prologue + bag). +1 cold/stale; +2 when the re-check fires.
- **Failure modes (§7.6) the SPA will see:** unknown subject or deactivated user →
  **`TenantNotFoundException` (404 `tenant-not-found`)**, cookie untouched (user may belong to other
  tenants); two guard failures → **`StaleContextException` (503 `stale-context`, `Retry-After: 1`)**;
  `50503 Access.LockTimeout` → transient, retried, then `DependencyUnavailableException` (503);
  `ReadOnly` tenant → prologue writes nothing, mutations refused by 0011's state verdict.
- Service accounts (§7.7): `Kind = Service` rows, no email/invitation; 0018 `issue-credentials`
  under `core.User × Credentials` returns client id + secret once; re-issue rotates; deactivation
  deletes the client; counted out of the lockout invariant (individuals only).
- `ConnectInitializer` at `Order` 100 (§7.8): `Connect()` for users/service accounts on a request,
  `ConnectAsUser` in a job scope, `ConnectAsSystem` for System; `Anonymous` on a tenant endpoint →
  `TenantNotFoundException`.

### 2.31 Write paths, guards, validators (0014 §8)

- Raw SQL declares `SqlOptions.Writes` and, for user-level tables, `SqlOptions.UserIds`
  (`ForCaller`); omission fails startup and the analyzer.
- Companion `UserStamps` insert after every `core.Users` insert (`IPersistEffect<User>`).
- `IAccessGuards { ContributeLock; ContributeInvariants }` on every persist touching `core.Users`,
  `core.Roles`, `core.RoleMemberships`, `core.Permissions` (except System scope): `sp_getapplock
  N'tellma.access'` exclusive, 5000 ms (`50503 Access.LockTimeout`); L1 `Access.LastAdministrator`
  (an active Invited/Joined Individual in role 1 remains); L2 `Access.PublicRoleHasMembers`; L3
  `Access.AdministratorRoleDamaged`. `50422` → `ValidationException` with that code. Recovery:
  `provision --admin-email`.
- `UserAccessRules<TUser>` codes: `Users.SystemUserImmutable`, `Users.CannotDeactivateSelf`,
  `Users.CannotDeleteSelf`, `Users.CannotRemoveOwnAdministratorMembership` ("make Bob an
  administrator, then Bob removes you"), `Users.EmailLockedAfterInvite`,
  `Users.OnlyNewUsersDeletable`, `Users.KindNotAllowed`, `Users.EmailNotAllowed`, `Required` at
  `Email`, `Users.TooManyRoles`, `Users.MembershipEscalation`.
- `RoleAccessRules<TRole>`: 0014 §3.7 rules + `Roles.PublicRoleHasMembers`, `Roles.AdministratorImmutable`,
  `Roles.HasMembers` (delete), `Roles.TooManyPermissions`.
- **Escalation:** `Permissions.EscalationBeyondSelf` — saving `(R, A, F)` requires the caller to
  hold `(R, A)` **unrestricted** (filtered grants don't qualify); `*/*` holders exempt.
  `Users.MembershipEscalation` applies the same test to every permission of a granted role.
- Validator paths are entity paths (`[2].RoleMemberships[0].RoleId`); every code is a dotted
  PascalCase resource key (Appendix A lists them all, plus `RowSecurity` 50403, `CallerInvalid`
  50401, `StaleVersionTag` 50412).

### 2.32 Drift (0014 §9)

- A row drifts (grants nothing, fail closed, never blocks other rows) on `UnknownResource`,
  `UnknownAction`, `FilterUnsupported`, `VersionUnsupported`, `FilterInvalid`. Listed in
  `UserAccess.Problems`, in the "why" answer, **returned by the role details read as a
  per-permission diagnostic through 0018's `RoleService`, "which the role editor renders as a
  banner"**, logged `AccessEvents.PermissionDrift`, counted, and in the post-deploy drift report
  (`IPermissionDriftScanner.ScanAsync()` → `PermissionDriftItem(PermissionId, RoleId, RoleCode,
  Code, Diagnostics)`, run by the migrator). `*/*` never drifts. Renames ship an `Alias` for one
  release.

### 2.33 "Can I, and why" — what the SPA can read (0014 §10)

- **`me`** (0018 `UserService.Me`; member-only, idempotent, **POST `users/me`** per 0016) returns
  0016 §3.4's `MeResult(UserProfileView User, string PreferencesTag, IReadOnlyDictionary<string,
  string> Preferences, AccessSummary Access, IReadOnlyDictionary<string, string> Tags, string
  SecurablesFingerprint)` (shape confirmed in 0016):
  - `UserProfileView(Id, Kind, State, Name, Name2, Name3, Email, ContactEmail, ContactMobile,
    ImageId, SignatureId, PreferredLanguage, PreferredCalendar, PreferredTimeZone, Gender)`;
  - `AccessSummary(string Tag, int FormatVersion, bool IsSystem, IReadOnlyList<SecurableSummary>
    Securables, IReadOnlyList<AccessProblem> Problems)` from `EvaluateAllAsync()`: **one entry per
    registered securable the caller is allowed**, `SecurableSummary(Resource, Action, HasFilter)`;
  - the four wire tags as `Tellma-Version-Tags` carries them; the registry `Fingerprint`.
  - "The SPA's first call after sign-in and its refresh call whenever a response's
    `Tellma-Version-Tags` header carries a tag the SPA does not hold." One round trip.
  - **This is the SPA's source for showing/hiding buttons and menu items**: `(Resource, Action)`
    allowed, and whether a filter applies. It carries no `IsSensitive`, no `FilterRoot`, no role
    list, and no per-row capability.
- **`access/check`** (0018 `AccessService.Check`; member-only, idempotent; route `access/check`):
  `AccessCheckRequest(UserId?, Securables: list<SecurableRef>)` → one `AccessDecision` per
  securable (outcome, composed filter rendered for display, grants with `kind`, problems).
  Caller: in memory, 0 round trips. Another user: requires `core.User × Read` on that row
  (filtered) **and** `core.Role × Read`; 2 round trips; no witness for the target.
- `IAdministratorDirectory.GetAdministratorIdsAsync()` — active Individual members of role 1 with
  State Invited/Joined, by Id, **capped at 20** (admin notice recipients).
- MCP: `tellma_whoami` (compacted `me`); `tellma_check_access` reserved.

### 2.34 Bootstrap (0014 §11)

- `TenantBootstrapRequest(Email, Name, PreferredLanguage?, Subject?)`;
  `ITenantBootstrapper.BootstrapAdministratorAsync` (System scope, idempotent on email) — provisioning
  step `10 core.bootstrap-administrator` from `TenantProvisioningContext.AdminEmail`/`AdminSubject`
  (`Tellma:Seed:AdminEmail` in Development; `provision --admin-email` otherwise). Creates a `New`
  user + membership in role 1; deployed flows then `invite`. Development in-proc: `admin@localhost`
  with subject `00000000-0000-0000-0000-000000000001`; first sign-in flips to `Joined`.

### 2.35 Limits (0014 §12)

- `AccessOptions` (`Tellma:Access`, `ValidateOnStart`): `MaxPermissionsPerRole = 300`,
  `MaxRolesPerUser = 32`, `MaxFilterLength = 2048`, `PermissionsMaxAge = 2 h`,
  `ActivityStampInterval = 60 s` (fixed in prologue text), `FastDenyWindow = 5 s`,
  **`MaxPreferenceKeys = 1024`**, **`MaxPreferenceValueBytes = 32768`**,
  `SecurityLockTimeout = 5 s`.
- `AccessLimits` (one stored filter): `MaxInputLength = 2048`, `MaxJoins = 8`, `MaxParameters = 64`,
  `MaxTypedNodes = 512`. `PipelineLimits` must admit `MaxParameters ≥ 1024`, `MaxJoins ≥ 64`;
  exceeding → 500 + `AccessEvents.FilterTooComplex`.

### 2.36 Telemetry (0014 §13)

- Meter `Tellma.Core`, `AccessTelemetryNames`: `tellma.access.{decisions (outcome), cache.hits,
  cache.misses (reason ∈ absent, tag, age, format), cache.entries, set.build.duration, set.grants,
  permissions.unresolved (problem), guards.triggered (guard), witness.missing, connect.results
  (result ∈ warm, cold, stale_permissions, stale_settings, stale_profile, unknown_subject,
  deactivated, guard_thrash), connect.prologue.duration (variant)}`.
- `AccessEvents` 3100–3107 (`MissingWitness`, `PermissionDrift`, `GuardThrash`, `GuardTriggered`,
  `EscalationRejected`, `ConnectRejected`, `AccessSetBuilt`, `FilterTooComplex`). No per-tenant or
  per-user metric tags. Nothing about client telemetry.

## 3. Gaps for round 2

**Settings page (0032) and its back end**

- G1. No endpoint lists offered languages (`ILanguageCatalog.Offered`), registered calendars
  (`ICalendarRegistry.Codes`) or accepted IANA zones — needed by the General tab and by the user
  preference editor (`users/me/save`, user details); browser `Intl.supportedValuesOf('timeZone')`
  may include zones the server rejects (`Settings.TimeZoneUnknown` when no Windows mapping).
- G2. No resource-key convention or order for a settings **category** (tab title, tab order); only
  `Setting_<key>` exists (0013 §5.4, §10.1).
- G3. `SettingKeyDescriptor` carries no description/help key, sub-group, display order, widget or
  range hint; `EnumValues` are raw strings without the enum type name, so the
  `<Schema>_<Enum>_<Value>` label key cannot be derived (0013 §6.1, §7.6).
- G4. `SettingValueType.Json` keys (own `JsonTypeInfo<T>`) have no generic editor; a distro escape
  hatch (custom field or tab component) is unspecified.
- G5. `settings/save` returns `TenantSettingsForClient`, not the new `General.ModifiedAt`; a second
  save needs a `settings/details` refetch (0013 §6.1, §6.3).
- G6. `settings/client`'s "unchanged" result shape and how `ifTag` travels (body member?) are
  defined by neither 0013 nor 0016 (grep found no definition).
- G7. No declared `Client`-visible keys for what the shell needs from the tenant: the admin
  default for `nav.pinned` that 0014 §3.4 says "is a tenant setting", branding/wordmark, About-modal
  links (terms, privacy), send-to-support opt-in, default home dashboard.

**Localization (0024) and the client entity model (0029)**

- G8. The tenantless shell's language selector has no source: `settings/client` is tenant-scoped,
  member-only; no tenantless endpoint lists the offered languages (the string pack route is
  tenantless but has no index).
- G9. Role-editor labels: 0014 §4.4 says the SPA labels resources and actions from the string pack,
  but no key convention exists for actions (`Read`, `Invite`, custom `[EntityAction]`s) or
  non-entity resources (`core.Settings.General`, `core.Export`); entity resources could reuse
  `<Schema>_<Entity>`.
- G10. The SPA must reimplement server rules stated only in C# terms: `ContentLanguageIndex`
  derivation (0013 §9.2), twin labels `Name (E)` (§7.6), content fallback `Name2 ?? Name` (§10.3),
  validation-code → key mapping (§10.4). A TS owner is needed (0024/0029).
- G11. Merge order and collision rule between the SPA's own strings and the server pack keys are
  unspecified (0013 §10.4 "beside its own strings").
- G12. Client-side calendar rendering is not in these specs (server `ICalendarSystem` only); 0005
  (frozen) owns the client controls. Queryex `'uq'`/`'et'` emit QX3101 today (0008 §10.4; 0013
  §8.2 defers the `uq` amendment), so reports cannot group by Hijri or Ethiopian month server-side.

**Permissions in the SPA (0025, 0027, 0030–0032)**

- G13. No endpoint serves the securables catalogue (`ISecurableRegistry.All` with `FilterRoot`,
  `IsSensitive`, `Owner`) for the role editor's pickers and filter-editor root; `MeResult` carries
  only `SecurablesFingerprint` "so the SPA refreshes its securable list on a deploy" — the list
  itself has no route (grep of 0016/0018 confirms).
- G14. `SecurableSummary(Resource, Action, HasFilter)` lacks `IsSensitive`; the SPA cannot
  anticipate step-up (`401 insufficient_user_authentication`) before a Users/Roles/General save; the
  re-auth round trip must not lose the edit form (draft survival, 0025; HTTP layer, 0024).
- G15. No per-row capability: with `HasFilter = true` on `Save`/`Delete`, the SPA cannot tell
  whether the open record is editable; it must show the action and handle 403/404.
- G16. `me` exposes no role ids/names; per-role home pages, "shared via role X" UX and role-scoped
  launcher entries would need them (or server-side resolution).
- G17. No reverse query "which users can see X, and why" (brief, report sharing); `access/check`
  is per user and per securable. 0033 needs its own resolver (memberships by role:
  `IX_RoleMemberships_RoleId_UserId`).
- G18. No push on permission, membership or preference changes (`cache.changed` covers `settings`
  and `entity:*` only, 0013 §2.5): an idle tab's menus stay stale until its next response header.

**Preferences and client state (0025)**

- G19. Write-behind flush policy is unstated: timer period, coalescing, page-hide transport (POST
  JSON with cookie; `fetch keepalive` body cap ~64 KB), cross-tab coordination of flushes, conflict
  rule between tabs/devices (0014 §3.4).
- G20. No budget or eviction policy for write-behind keys under `MaxPreferenceKeys = 1024` and
  `MaxPreferenceValueBytes = 32768` (e.g. one columns key per search page, one params key per
  report, recently-visited list size); no registry of platform-owned keys.
- G21. Homes undecided for theme (Light/Dark/System), pinned-bar visibility, and the session-menu
  language/calendar toggles: bag key, typed `User.Preferred*` column via `users/me/save` (3 round
  trips, a `core.UsersHistory` row, a `PreferencesTag` bump), or header-only client state.
- G22. Which display zone the SPA sends in `Tellma-Time-Zone` (browser zone vs nothing so that
  `PreferredTimeZone` applies) is undecided; the header outranks the stored preference (0013 §9.2).
- G23. Browser persistence of `TenantSettingsForClient`, `MeResult` and cacheable lists keyed by
  wire tag is unspecified beyond "the browser stores DTOs keyed by that string" (0013 §2.7).
  Revalidation arrives only on tenant responses; a warm launch that issues no data request needs an
  explicit revalidation call; `users/me`, `settings/client`, `settings/entity-tags` are POSTs.
- G24. The string pack is `no-store` when `v` mismatches (stale bundle after a deploy); warm launch
  needs the SW/IndexedDB to hold packs itself (0013 §10.4).

**New back-end pieces**

- G25. `core.Assignments` (0028): tag decision (none of 0013 §2.3's lists covers it), securable
  and `CoreResources` constant, an `IAccessCriteriaProvider` for "assigned to me" (0014 §5.2 names
  this shape) — awaited per evaluation, uncached, so a DB-backed provider costs a round trip —
  `NoActivityStampMetadata` on its polling summary, hub event (not `cache.changed`).
- G26. Report definitions (0033): securables `Read`/`Save`/`Delete` plus a custom action (e.g.
  `Publish`, PascalCase ≤ 32); own/shared visibility as bespoke criteria (reason keys); not
  `[Cacheable]` (per-user visibility); RLS of the root entity keeps applying at execution (0014
  §5.3) — sharing grants nothing.
- G27. Client observability (0026): all 0013/0014 instruments and events are server-side; nothing
  client-side is defined there.

## 4. Likely change points

- 0013 §5.3 / §6.1 — extend `SettingKey<T>` and `SettingKeyDescriptor` (category label key and
  order, description key, enum type key, UI hint) or add a category descriptor; bump the details
  shape (G2–G4).
- 0013 §6.1 / §6.2 — add the editor's catalogues (offered languages, calendars, zones) to
  `TenantSettingsDetails` or a new action; define the `client` unchanged result with 0016 (G1, G6).
- 0013 §6.3 — `ExpectedModifiedAt` for entries-only saves (per-entry stamps, optional stamp, or
  General `Read` implied), and the `save` result (return `ModifiedAt` or the details) (G5, C1).
- 0013 §5.6 — `TenantSettingsForClient` fields the shell needs, with a `FormatVersion` bump; or
  platform-declared `Client` keys (G7).
- 0013 §10.4 — which languages the SPA fetches (tenant content languages vs offered set; the
  tenantless shell), label-key conventions for categories, securables, launcher entries; SPA/server
  key merge; `v` stamping (G8, G9, G11, C2).
- 0013 §2.5 — `cache.changed` scope (brief's open question on the SignalR owner tab refreshing
  settings and permissions): maybe a per-user access/preferences event (G18).
- 0013 §2.3 / §2.4 — list round-2 tables (`core.Assignments`, report and dashboard definitions)
  among the bumping or never-bumping tables (G25, G26).
- 0014 §3.4 — write-behind details, platform key registry, the admin `nav.pinned` default, and the
  "keeping its bag" rule vs 0018's full-bag result (G19, G20, C6).
- 0014 §2 (`CoreResources`, `AccessActions`) — new resources/actions for assignments, report
  definitions, dashboards (G25, G26).
- 0014 §10 + 0016 §3.4 — `SecurableSummary.IsSensitive` (maybe `FilterRoot`); a securables-catalogue
  route for the role editor; the `me` refresh trigger wording; `AccessSummary.Tag` semantics (G13,
  G14, C4, C5).
- 0014 §4.4 — role-editor label keys (G9).
- 0014 §5.3 / 0015 — optional per-row capability flags in details reads (G15).
- 0014 §4.3 — the sensitive default set, if step-up friction on the stock Users/Roles pages proves
  too high (review flag 20).

## 5. Contradictions and surprises

- C1. **Entries-only settings save is impossible without General `Read`.** `ExpectedModifiedAt` is
  required (`Concurrency.StampRequired`) and is the typed row's stamp, but `General` (its only
  source) is present only when the caller holds `core.Settings.General × Read` (0013 §6.2 vs
  §6.3). And because every save runs `Save<Settings>` under `ConcurrencyMode.Check`, two admins
  editing different categories conflict with a 409.
- C2. **Language switcher scope.** 0013 §10.4: the SPA lets a user switch among the **tenant's
  content languages**; but 0013 §7.3/§9.2 and 0014 §3.1 make the culture any **offered** language
  ("an English auditor at an Amharic tenant"), and `PreferredLanguage` validates against the offered
  set. The brief's session menu shows "the tenant's configured languages". A designer must pick.
- C3. **"ar-SA locale" for the reference distro has no server home.** No tenant default culture
  (0013 review flag 7); content languages are bare codes (`ar`); region comes only from a user's
  `PreferredLanguage = 'ar-SA'` or the SPA's `Accept-Language`. Packs are per bare language
  (`/api/strings/ar`).
- C4. **The user-level `PermissionsTag` never reaches the client.** `Tellma-Version-Tags` and
  `MeResult.Tags` carry the tenant `permissions` tag only. A membership edit reaches the member's SPA
  only because a child change stamps the `User` root (0014 §3: "any child change stamps the
  parent"), firing `[BumpsUserVersionTag(Preferences, "Id")]` and moving `preferences` — an
  inference worth making explicit. `AccessSummary.Tag`'s meaning (tenant tag, or folded with the
  user tag) is not defined.
- C5. **`me` refresh trigger differs:** 0014 §10 says any header tag the SPA does not hold; 0016
  §3.9 says a `preferences` or `permissions` tag. `settings` → `settings/client`; `entities` →
  `settings/entity-tags` then per-entity `/all`.
- C6. **Warm-writer rule loses interleaved writes as worded.** 0014 §3.4: adopt the new
  `PreferencesTag` "while keeping its bag" — if another device wrote between, the adopted tag
  covers a write this device never saw. 0018 §3.5 returns the **full bag** in the set result, which
  permits a safe "returned bag + overlay unflushed keys" rule.
- C7. **Every deploy invalidates every client cache anyway.** The migrator bumps every
  `core.VersionTags` row on every `migrate` (0013 §2.8), so decision 8 ("folding the build identity
  would invalidate every client cache on every deploy") holds only for deploys without a migrator
  run. Expect a refetch burst (settings, `me`, entity tags, lists) after each deploy; user-level tags
  are not bumped by the migrator.
- C8. `cache.changed` payload `{ tag: <name> }` holds the tag **name** (`settings`,
  `entity:gl.Center`), not a wire value, and goes to every connected user of the tenant (0013 §2.5;
  0021 hub table). `permissions`, `entities` composite and user-level bumps publish nothing.
- C9. **Not cacheable:** users, roles and anything with RLS (0013 §4.1); user/role pickers query.
  `CachedEntitySet.Items` include inactive rows; the server probe has scalars only.
- C10. **Status semantics the SPA must decode:** collection op `Denied` → 403 ("so the SPA can hide
  the page"); by-id outside the filter → 404 (same as missing); save on readable-not-writable → 403;
  non-member or deactivated caller → **404 `tenant-not-found` on any tenant call** (must be told
  apart from an entity 404 by problem code and routed to the tenant picker); two guard failures →
  503 `stale-context` with `Retry-After: 1` (safe to auto-retry once).
- C11. **Step-up by default** on `core.User` × `Save|Delete|Activate|Invite|Credentials`,
  `core.Role` × `Save|Delete|Activate`, `core.Settings.General × Save`, `core.Schedule` ×
  `Save|Delete|Activate`; category saves (`core.Settings.gl`) are not sensitive. Service accounts get
  `IndividualRequiredException` on these.
- C12. **Preference key grammar is strict:** every segment starts lower-case
  (`^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)*$`, ≤ 128, `varchar`): no hyphens, no digit-first
  segments, no uppercase-first — so `grid.gl.Center.columns` and `report.42.params` are invalid;
  entity names and ids must be encoded. Values are strings (`IReadOnlyDictionary<string, string>`
  in `MeResult`), JSON by convention.
- C13. **Settings category securables are lower-case and verbatim** (`core.Settings.gl`) beside
  PascalCase `core.Settings.General`; `general` entries share the General securable (and its step-up).
- C14. **A language change in settings fans out:** it bumps `permissions` (0013 §6.3), so every
  user's next request recomposes and, per 0016 §3.9, every open SPA refetches `me`; filters naming a
  now-gated `Name2`/`Name3` drift (0014 §9).
- C15. **At most two calendars per tenant** (`PrimaryCalendar`, `SecondaryCalendar`); a
  `Tellma-Calendar` outside the pair is ignored; responses echo the effective `Tellma-Calendar` and
  `Content-Language` so a drifted client self-corrects. `today()` uses the tenant zone, never the
  display zone; there is no `X-Today`.
- C16. **Catalog name mirroring is best-effort:** a settings name change mirrors to the catalog
  (`ITenantCatalog.RenameAsync`) after commit; failure is only logged, so the tenant picker (catalog
  hint directory, 0011 §3.8) can show stale names until the next name-changing save.
- C17. **Secrets are write-only:** `Values` omits `Secret` keys; the editor learns only presence
  from `SetKeys`; a listed `null` resets any entry to its default (deletes the row).
- C18. **Activity stamping:** any SPA background poll on an endpoint without
  `NoActivityStampMetadata` moves `LastActiveAt` (60 s throttle); round-2 polling endpoints
  (assignments counts, dashboard refresh) must carry it.
- C19. **Deactivation is discovered lazily** by the SPA: hub connections close
  (`TenantAccessRevokedAsync`), but the tab learns from its next call (404 `tenant-not-found`); the
  session cookie survives (other tenants remain usable).
- C20. **Brief alignment:** "user sees only the settings tabs they can access" is supported twice —
  `settings/details` returns only `Read` categories, and `AccessSummary` lists
  `core.Settings.<Category> × Read` (a `Save` grant implies `Read`, 0014 §5.2) — so the tab strip can
  render from cached `me` on a warm launch before `details` loads.

