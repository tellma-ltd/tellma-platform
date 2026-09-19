# T3 — Tenant settings, localization, and the version cache (→ spec 0012): decisions

This file is the settled design for the theme. It is written for a reader who has seen none of
the proposals: every name, type, statement, and column is final-looking. "The platform" is
`Tellma.Core` plus `Tellma.Core.Abstractions`; "a pack" is a `Tellma.Module.*` / `Tellma.Industry.*`
/ `Tellma.Compliance.*` library that references `Tellma.Core.Abstractions` only; "the distribution"
is the composition root. One application database per tenant; the database *is* the tenant.

Contract blocks use the platform's contract notation (`.tmp/crud-design/notation.md`): names are
normative; shape is described, not transcribed. SQL is the exact shape to emit.

The round-trip ledger this design commits to, for a warm instance:

| Operation | Round trips | What this theme adds to the batch |
|---|---|---|
| Any read (query, details) | 1 | one `SELECT` over `core.VersionTags` (one page) as the first statement |
| Any save | 2 | the same read in both round trips; one guard `IF` and one bump `UPDATE` in round trip 2 |
| First request of a tenant on a fresh instance | 2 | the settings load (two statements) rides round trip 1 |
| First request of a user on a warm tenant | 2 | the user-settings and permissions loads ride round trip 1 (T4 emits them) |
| Settings edit | 2 | as save; the client DTO is rebuilt from the saved rows, no reload |

No tag or setting is ever read in a round trip of its own. No cache is invalidated by a bus, a
timer, or another instance: the tag rows read at the head of every batch are the only
invalidation channel, and the in-process cache is a pure accelerator on top of them.

---

## 1. Critique of the brain dump (Settings tables, Caching, Localization, the tag columns of User)

**The general shape is right and proven.** A typed single-row settings table, a key-value table
for ad-hoc settings, and an in-process cache validated on every request by opaque version tags
bumped on every write is the shape the previous Tellma ran for years. Keeping it is correct. The
problems are in the details and in what is left unsaid.

1. **Tags sit on temporal rows.** `Settings.SettingsVersion` on the temporal `core.Settings` row
   and `UserSettingsVersion`/`PermissionsVersion`/`LastActive` on the temporal `core.User` row
   would each write a history row per bump — SQL Server writes a history row on every `UPDATE`
   even when nothing changed — and would move the row's concurrency stamp on bookkeeping. The
   brain dump notices the churn and leaves both placements in the sketch. Tags must not live on
   any temporal row nor on any row whose `ModifiedAt` is a concurrency token.
2. **"Guaranteed at framework level (how?)" is the load-bearing gap.** Without a structural
   mechanism every new cacheable entity is a latent stale-cache bug that surfaces on the second
   app instance only. The thing that writes rows — the batch executor — must be the thing that
   bumps tags, from declarations on the entity classes, so a service author cannot forget; and a
   test tier must prove no write path escaped it.
3. **"Before executing any API call, the versions are read from the DB" is a round trip per
   request** if read literally, doubling the read path. The tag read must ride the first business
   round trip and be compared afterwards, with a per-kind policy for what a mismatch means.
4. **The settings model conflates axes.** The tenant's up-to-three *content* languages (which
   `Name` column holds which language) and the request's *UI/message* culture (which resource
   satellite answers a lookup) are different things with different owners. "The supplied culture
   headers are ignored if they are not one of the tenant's languages" throws away a legitimate
   case: an English-speaking auditor at an Amharic tenant must get English messages while seeing
   Amharic content. The brain dump's own open question is the symptom.
5. **The fallback chain "request culture → tenant primary → English" is wrong for resources.** A
   Spanish-UI user with a missing Spanish string should see English (the neutral resource every
   string is guaranteed to have), not Arabic. The tenant-primary hop is right for *content*
   (`Name3 ?? Name`), not for resource strings. Two chains, not one.
6. **"A custom calendar header (does a standard one exist?)"** — none does, and BCP 47 `-u-ca-`
   extensions inside `Accept-Language` are a trap on .NET 10/ICU: the calendar object does not
   change, ICU patterns produce mixed output, and `Parent` collapses to the language.
   `DateTimeFormatInfo.Calendar` refuses both a custom `Calendar` and any calendar outside the
   culture's `OptionalCalendars`, and .NET has no Ethiopic calendar at all. Calendar-aware
   formatting is platform code carried in its own header, never routed through `CultureInfo`.
7. **`today()` is already fixed by spec 0008** as the current date in the tenant's zone, bound
   by the host. An `X-Today` header would let two callers of one tenant disagree about the
   company's day and would hand a client control over security-relevant filters.
8. **"Metaversion" names the right thing with a word nobody will search for.** It is a *format
   version* of one cached shape; it belongs on the wire (where caches outlive deployments), not
   in the database; and as a hand-maintained constant it needs a test that fails when the shape
   changes without it.
9. **"Version / ETag / fingerprint":** HTTP owns *ETag* (an opaque validator of a
   *representation*, RFC 9110 §8.8.3) and the blob endpoint will use it properly; *fingerprint*
   implies a content hash, which these are not; *version* alone collides with the Queryex
   language version and the application version. *Version tag* is unambiguous.
10. **The key-value table has no name, no key grammar, no type discipline, and a `Category`
    column derivable from the key.** A key-value table without a code-declared definition
    (type, default, visibility) is `Text1`/`Text2` in disguise. `Value` "string or JSON" must be
    one thing: a JSON value.
11. **`TenantId` in the settings row is right for the wrong reason.** In a
    database-per-tenant model it is not a partition key; it is a routing guard against a
    catalog typo, a backup restored under the wrong name, or a sandbox cloned from live. It must
    be asserted on every settings load and never used as a filter.
12. **"Settings … not subject to READ permissions"** is right for the client DTO and wrong for
    server-only entries (an e-invoicing environment flag, an integration secret reference).
    Visibility must be declared per key.
13. **Cacheable entities "(up to 50–100 records?)"** — countries alone are ~250. The cap must be
    a declaration parameter enforced by the loader, the cached list must be shared safely across
    users (no row-level filter can apply to it), and a type that exceeds its cap must degrade
    loudly.
14. **Internal inconsistencies.** The save flow reads tags in "DB call #1" *after* deciding
    which permissions apply (the RLS filter is built from cached permissions), so the request is
    optimistic and needs an explicit re-run path the brain dump never states; "any operation that
    updates the settings resets `SettingsVersion`" has no owner in the save pipeline; the
    localization hierarchy sentence "Every distro supports …" is truncated, and the intended rule
    (tenant languages ⊆ distribution languages ⊆ Core catalogue) needs the catalogue to be data
    in code because satellite assemblies cannot be enumerated at runtime.

---

## 2. Decisions

### D1 — Vocabulary: *version tag*, `VersionTag`, `core.VersionTags`, column `Tag`; *format version* replaces "metaversion"

**Decision.** The opaque cache validator is a **version tag**: `VersionTag`, a value type over a
`Guid` in `Tellma.Core.Abstractions.Caching`, compared for equality only, never ordered or
indexed. The tenant-level table is `core.VersionTags` with columns `Name` and `Tag`; user-level
tag columns end in `Tag` (`PermissionsTag`, `UserSettingsTag`). The hardcoded per-shape constant
is the **format version** (`FormatVersion`, an `int` on each client-facing cached DTO), never
stored, folded into the wire form `"{FormatVersion}.{guid:N}"` (D5). "ETag" is reserved for HTTP
conditional requests (the blob endpoint, T7). "Fingerprint" and "metaversion" are not used.

**Rationale.** Rails, Django, and `HybridCache` all call this a version; "version tag" survives
next to Queryex's `LanguageVersion` and the application version without a qualifier. A column
named `Key` would be a reserved T-SQL word; `Name` avoids it.

**Rejected.** `ETag` (collides with HTTP semantics on the same responses); `Stamp` (the
orchestrator's word for the `ModifiedAt` concurrency stamp); `Version` as the column name
(ambiguous next to the language version).

**Confidence.** High.

### D2 — Which tags exist and where they live

| Tag | Level | Lives in | Bumped by writes to | Read by |
|---|---|---|---|---|
| `settings` | tenant | `core.VersionTags` | `core.Settings`, `core.SettingEntries` | settings cache (server + client DTO), Queryex schema-shape selection, negotiation |
| `permissions` | tenant | `core.VersionTags` | `core.Roles`, `core.Permissions` (T4 declares) | the permissions cache (T4), together with the user's `PermissionsTag` |
| `entities` | tenant | `core.VersionTags` | any `[Cacheable]` entity's table (composite) | the client: "some cached list changed, fetch the per-entity tag list" |
| `entity:<EntityName>` | tenant | `core.VersionTags` | that entity's table | server cacheable-entity cache; client per-entity list cache |
| `PermissionsTag` | user | T4's non-temporal per-user row (working name `core.UserStates`) | `core.RoleMemberships` rows of that user | the permissions cache (T4) |
| `UserSettingsTag` | user | the same row | `core.UserSettings` rows of that user | the user-settings cache, client user-settings cache |

Tags are `uniqueidentifier`, **generated by the application** (`Guid.NewGuid()`, one value per
batch), compared only for equality. A pack may register further tenant-level names
(`definitions`, `schedules`) through `[BumpsVersionTag("<name>")]`; names are lower-case ASCII,
≤ 128 chars, and the `entity:` prefix is reserved.

**Permissions are validated at two levels.** A role or permission edit changes what *every*
member may do and bumps the tenant `permissions` tag; a membership edit changes one user and
bumps that user's `PermissionsTag`. The permissions cache entry for a user is valid only when
both tags match. This gives per-user precision for the frequent case (a new hire's memberships)
with no fan-out `UPDATE` over a role's members inside the role-save transaction, and one
tenant-wide recompute for the rare case (a role edit). `RoleMembership` is a child of `User`
(saved with the user), so the emitter knows the `UserId` of every membership row it writes.

**Rationale.** A non-temporal, audit-free, tiny table keyed by name is the cheapest thing to
read on every request (one clustered scan of a few dozen rows) and the only place a tag can move
without a history row or a concurrency-stamp change. A key-value shape admits an open set of
names (every cacheable entity a distribution adds gets a row without a migration on a platform
table). Application-generated Guids are restore-safe (a restored backup cannot re-reach a value a
browser cached) and let the writing instance stamp its own cache without a read-back.

**Rejected.** `rowversion` (moves on any update including bookkeeping); monotonic `bigint`
(restore hazard; ordering is never needed); Guid v7 (not sequential in SQL Server byte order and
ordering is irrelevant); tags on the temporal rows; a single wide tenant row (cannot hold an open
set); tenant-level `permissions` only (every membership edit forces every active user to recompute
once); per-user tags bumped by role writes (fan-out over members inside the role-save
transaction); SQL Server change tracking as the primary tag (database-wide, per-table enablement,
retention, and a per-table current version is a scan of the change table).

**Confidence.** High on the table and the Guid; medium-high on two-level permissions.

### D3 — The bump is a property of the batch executor: declared writes → one appended statement, last before `COMMIT`

**Decision.** Services never bump. Every statement in a batch declares the entity types whose
tables it writes (the save emitter derives the set from the entities it persists; a raw SQL write
declares it through the builder's `writes:` argument or the builder refuses it — T2 seam). Tag
names come from attributes on entity classes in `Tellma.Core.Abstractions`:

- `[BumpsVersionTag("settings")]` — explicit tenant-level name; repeatable; carried by the
  platform on `Settings`/`SettingEntry` and by T4 on `Role`/`Permission`.
- `[BumpsUserVersionTag(UserVersionTagNames.Permissions, UserIdProperty = "UserId")]` — a write
  to this entity's table bumps the named user-level column for every distinct value of the
  user-id property among the written rows (inserted, updated, or deleted); carried by T4 on
  `RoleMembership` and `UserSetting`.
- `[Cacheable]` — implies `entity:<EntityName>` **and** `entities`.

At composition the platform builds `IVersionTagRegistry` from the EF model (table → tag names;
table + user-id column → user-level column) and validates it at the startup gate (grammar,
prefix, uniqueness of `entity:` names). At execution the executor resolves the union of the
batch's written tables against the registry and appends, **as the last statements before
`COMMIT`**, one tenant bump and — only when a user-level rule fired — one user bump, both
parameterized with one fresh Guid `@tm_tag` for the whole batch:

```sql
-- @tm_tagNames : [dbo].[StringList] (spec 0001's standalone type; column [Id]) — the distinct
-- tenant-level names derived from the batch's declared writes, 'entities' added when any
-- 'entity:*' name is present.
UPDATE [core].[VersionTags]
   SET [Tag] = @tm_tag
 WHERE [Name] IN (SELECT [Id] FROM @tm_tagNames);

-- Only when a user-level rule fired. @tm_userIds : [dbo].[IdList]. One statement per distinct
-- user-level column the rules named (here PermissionsTag).
UPDATE u
   SET u.[PermissionsTag] = @tm_tag
  FROM [core].[UserStates] AS u
  JOIN @tm_userIds AS i ON i.[Id] = u.[UserId];
```

Rules:

- **Placement.** The bump takes an exclusive lock on each touched `VersionTags` row until commit;
  two concurrent saves of one cacheable table serialize on that row only for the executor's
  trailing statements, never across business statements (there is no client I/O inside a batch).
- **Fixed shape.** Names travel as a `StringList` TVP so the statement has one plan whatever the
  count; a batch bumps a handful at most.
- **`UPDATE` only; a missing row never fails silently.** Rows are created by the migrator on
  every run per tenant database from the registry (`INSERT … WHERE NOT EXISTS`). A name absent
  from the tag read (D4) marks the cache kind behind it **uncacheable** on that instance (every
  request loads; `cache.outcome = uncached`; one Warning per name per process), so a missing row
  costs performance, never correctness. There is no self-healing insert on the write path: it
  would race between instances and the migrator already seeds.
- **The migrator bumps every tag after applying migrations** (`UPDATE [core].[VersionTags] SET
  [Tag] = NEWID()`), because migrations (`HasData`, `migrationBuilder.Sql`, runtime seeding of
  reference data) are the one write path that legitimately bypasses the executor.
- **Out-of-band writes** (a DBA script, a distribution's own `SqlConnection`) are documented:
  run the same `UPDATE` afterwards, or the admin action `settings/refresh-caches` (D11).
- **The post-batch snapshot needs no read-back:** the executor overlays the bumped names with
  `@tm_tag` on the prelude's rows (D4) and the response carries post-bump values.
- **The test tier proves the guarantee.** On the LocalDB fixture database, change tracking is
  enabled on every table; the executor in test mode compares `CHANGETABLE(CHANGES …)` since the
  batch's start against the batch's declared write set after every batch and fails the test on
  any undeclared write.

**Rationale.** The only component that knows every table a round trip writes is the executor;
making it the bumper removes the class of bug the brain dump fears. The change-tracking audit
exists because a registry rule that is never exercised is as good as absent.

**Rejected.** Bumps written by each service (the previous Tellma's failure mode: `UPDATE
dbo.Settings SET SettingsVersion = NEWID()` scattered through procedures); triggers (no logic in
the database; `OUTPUT` restrictions on tables with triggers); `NEWID()` in the bump (the writing
instance would need a read-back to stamp its own cache); a self-healing `INSERT … WHERE NOT
EXISTS` in the bump (a cross-instance race on the primary key for no gain).

**Confidence.** High on the mechanism; medium on the CI audit's cost (T2 owns the fixture).

### D4 — The tag read heads every batch; reads are optimistic with a per-kind mismatch policy; writes are guarded inside the transaction; the response carries the tags

**Decision.**

1. **The prelude.** The executor makes the tag read the **first statement of every batch** it
   executes for a tenant scope:

   ```sql
   SELECT [Name], [Tag] FROM [core].[VersionTags];
   ```

   T4's connect statement (subject → user, `IsActive`, `LastActive` stamp, `PermissionsTag`,
   `UserSettingsTag`) follows it in the first batch of a request. Any cold-cache load in the
   same batch (settings, D10; user settings and permissions, T4; cacheable lists, D7) is
   emitted **after** the tag read and stamped with the tag the read returned. Ordering is
   load-bearing: a bump landing between the tag read and the rows makes the rows newer than
   their stamp, which the next prelude detects and reloads (convergent); the reverse order would
   let a bump land after the rows and stamp stale data fresh until the next bump (stale forever).
   A request that needs no other statement still executes the prelude alone: one page read is
   the price of cross-instance freshness without Redis or a bus.

2. **The snapshot.** The prelude's rows, overlaid with the batch's own bumps, replace the
   process-wide per-tenant `VersionTagSnapshot` held by `IVersionTagSnapshots` (bounded by
   tenant count). Every cache compares its entry's tag with the current snapshot before handing
   the entry out; the snapshot is the *only* invalidation channel. A request that begins on a
   fresh instance has an empty snapshot and misses everything, which is what makes cold loads
   ride its first batch.

3. **Reads are optimistic.** A batch that composed cached inputs declares them to the builder as
   `VersionTagDependency(Name, ExpectedTag, OnMismatch)`. After the batch, the executor compares
   the returned tags with the expectations and applies the declared policy:

   | Dependency | `OnMismatch` | Behaviour after a read batch |
   |---|---|---|
   | `permissions` and the user's `PermissionsTag` (T4 declares) | `Rerun` | The result is discarded, the caches refresh, and the batch re-runs **once**; a second mismatch surfaces as `StaleVersionTagException` (503-class, retryable) and is metered. Rows never reach the caller from a run whose permissions moved. |
   | `settings` | `Refresh` | The result is served (physical columns never disappear; a query compiled under the old shape still executes); the settings cache reloads before its next use; the response carries the new tag. |
   | `UserSettingsTag` | `Refresh` | As settings. |
   | `entity:<Name>` | `Refresh` | The cached list is invalidated; the result came from the database anyway. |

4. **Writes are guarded in the database.** A batch that writes and declared dependencies carries
   a guard as the **first statement inside the transaction**, parameterized with the
   `VersionTagList` standalone type (D3's `StringList` sibling; `Name nvarchar(128)`, `Tag
   uniqueidentifier`):

   ```sql
   SET XACT_ABORT ON;
   BEGIN TRAN;
   IF EXISTS (SELECT 1
                FROM [core].[VersionTags] AS t
                JOIN @tm_expectedTags AS e ON e.[Name] = t.[Name]
               WHERE t.[Tag] <> e.[Tag])
       THROW 51001, N'VersionTagMismatch', 1;
   -- T4 emits the same THROW for the user's PermissionsTag next to this one.
   -- … business statements, bumps, COMMIT
   ```

   Error 51001 maps to `StaleVersionTagException`; the pipeline (T5) refreshes and re-runs the
   persist round trip once. Platform batch error numbers occupy 51000–51099 (T2 owns the band;
   51001 is claimed here). The residual window is the transaction's own duration: a bump that
   commits after the guard read and before this commit is not seen. That window is milliseconds,
   permission changes are not a synchronous revocation mechanism, and closing it would require
   holding shared locks on the tag rows across every save; it is accepted and documented.

5. **The response channel.** Every authenticated response carries the post-batch tags the client
   caches on, in one header (T6 owns the exact wire form; the semantics are this theme's):
   `Tellma-Version-Tags: settings=1.<guid>, permissions=1.<guid>, usersettings=1.<guid>,
   entities=1.<guid>`. The SPA compares with what it holds and refetches the DTOs whose tag moved.
   `entity:*` tags are deliberately **not** in the header (unbounded count); the composite
   `entities` is, and a changed composite makes the client fetch the per-entity tag list
   (`settings/entity-tags`, D7).

**Round-trip ledger** (the numbers T5 should quote): warm read 1; warm save 2; first request for
a tenant on a fresh instance 2 for a read, 3 for a save (round trip 1 = prelude + connect +
settings + user settings + permissions, all keyed off the subject in SQL; round trip 2 = the
business call); a `permissions` mismatch after a warm read +1, once per instance per role change.
No request pays more than one extra round trip for cold caches.

**Failure modes.** (a) *Deactivated user*: the connect row says `IsActive = 0`; the business
statements of that batch have already executed on the server, but the executor discards every
result set and the pipeline returns 403 — nothing leaves the process; for writes T4 emits an
`IF` next to the guard so nothing is written. (b) *Stale permissions on a read*: re-run; the
stale result is never returned. (c) *Stale permissions between a save's two round trips*: the
guard throws inside the transaction; nothing is written. (d) *RLS pre-check ordering*: the
pre-check for updates (T5) is a read in the validation round trip, which carries the prelude and
a `permissions` dependency; the persist round trip re-checks through the guard. (e) *Settings
changed mid-request*: served and refreshed (policy above). (f) *Two instances bumping one tag
concurrently*: last writer wins; each instance's next prelude reconciles; no invalidation is
lost because every bump produces a value that differs from every cached one. (g) *Misrouted
tenant database*: the settings load compares `core.Settings.TenantId` with the routed id and
fails closed (D8).

**Rejected.** A separate connect round trip (the brain dump's DB call #1: one round trip for a
read becomes two); TTL-based trust of the snapshot to skip the prelude on "pure cache" requests
(a freshness knob and an inconsistency window for nothing); a uniform "always re-run" policy
(settings and user-settings mismatches are harmless mid-request and re-running would double
their cost); the guard before `BEGIN TRAN` (widens the window for no benefit — under
`XACT_ABORT ON` a `THROW` inside the transaction rolls back by itself); the guard as an opt-in
(the pipeline declares dependencies anyway; the guard is one seek).

**Confidence.** High on the prelude, the snapshot, and the guard; medium on the header name.

### D5 — Format version: a `FormatVersion` constant per client-facing cached shape, folded into the wire tag, pinned by a shape test

**Decision.** Each client-facing cached DTO declares `FormatVersion` (initially 1):
`TenantSettingsForClient.FormatVersion`, T4's `UserPermissionsForClient.FormatVersion` and
`UserSettingsForClient.FormatVersion`, and the cacheable-list shape's constant on the platform
side. The wire form of a tag is `"{FormatVersion}.{Guid:N}"`; the client stores DTOs keyed by
that string and refetches when it changes for any reason. The database never stores the format
version; the in-process cache dies with the process and needs none.

**The discipline is enforced by a test, not remembered.** `Tellma.Core.Tests` holds a checked-in
snapshot of each DTO's serialized property tree (names and JSON kinds, from the source-generated
contract) paired with its `FormatVersion`; a change to the shape without a bump fails the build.

**Rationale.** This is Django's cache `VERSION`: its only job is guarding caches that outlive
deployments — the SPA's persisted DTOs and any future L2. Folding the deployment's build identity
into every tag instead would invalidate every client cache on every deploy (a SaaS distribution
deploys many times a day) and would amend the shipped `DeploymentIdentity` record; the constant
plus the shape test costs one line per DTO and one refetch per real shape change.

**Rejected.** A single global constant (bumps every cache for any shape change); the build
identity on the wire (above); storing it in `core.VersionTags` (a deployment fact is not tenant
data); the name "metaversion".

**Confidence.** High.

### D6 — Cache infrastructure: a private bounded `MemoryCache` per kind, single-flight loads, meters; `HybridCache` rejected

**Decision.** `Tellma.Core.Caching.VersionedCache<TKey, TValue>` is the platform's one in-process
cache primitive: a **private** `MemoryCache` instance per cache kind (never the DI
`IMemoryCache`), `SizeLimit` in the kind's own units, `TrackStatistics = true`, entries holding
`(VersionTag Tag, TValue Value, DateTimeOffset LoadedAt)`, a per-key single-flight guard
(`ConcurrentDictionary<TKey, Lazy<Task<Entry>>>`, removed on completion; a faulted loader is
removed before its exception propagates so the next caller retries), and `IMeterFactory`
instruments (D22). `GetAsync(key, currentTag)` returns the entry when `entry.Tag == currentTag`
and the entry is within the kind's `MaxAge` (when one is set), otherwise loads through the derived
class's `LoadAsync(key)` — which performs its own batch and returns the value **together with the
tag the batch's prelude returned** — and stores it. `Set(key, value, tag)` stores a value the
caller just produced (a save returning the new state). The kinds and their keys:

| Kind | Key | Value | Size unit / default limit | `MaxAge` |
|---|---|---|---|---|
| `settings` | tenant id | `TenantSettings` + the pre-serialized `TenantSettingsForClient` bytes (one entry) | entries / 10 000 | none |
| `usersettings` (T4 owns the value) | (tenant id, user id) | the user's settings bag | entries / 50 000 | none |
| `permissions` (T4 owns the value) | (tenant id, user id) | the compiled permission set | entries / 50 000 | none |
| `entities` | (tenant id, entity name) | `CachedEntitySet<TEntity>` | rows / 2 000 000 | 15 minutes |

Compaction is the runtime's (expired → priority → least recently used), which is the bounded LRU
the brain dump asks for; idle tenants age out first. `MaxAge` on `entities` is a **bounded
staleness backstop** for writes that bypass the executor (D3); settings and permissions need none
because their tags are read on every request. Limits come from `TellmaCacheOptions` bound to
`Tellma:Cache` (D24); a distribution changes a number in configuration, never code.

**Rationale.** `HybridCache` serializes every write even L1-only and deserializes mutable values
on every read, shares the DI `IMemoryCache` (so a `SizeLimit` endangers MVC and every third-party
consumer that does not set `Size`), and its tag stamps are process-local timestamps that cannot be
validated against a database value. The requirements here — bounded per kind, stampede-safe, tag
compared on every request, metered — are ~150 lines over `MemoryCache`, most of it the previous
Tellma's `VersionCache` with bounds and meters added. Row count is the only size unit that bounds
memory for lists of unknown width without serializing them.

**Rejected.** `HybridCache` (above); one shared `MemoryCache` for all kinds (one limit for
unrelated shapes); an unbounded `ConcurrentDictionary` (the previous shape; grows without limit).

**Confidence.** Medium-high. Review flag in §9.

### D7 — Cacheable entities: `[Cacheable(MaxRows)]`, whole-table lists shared by every reader, unfiltered-read required at startup, two-level tags, batch participation

**Decision.** A distribution marks an entity cacheable with one attribute:

```csharp
// Illustration
[Cacheable]                       // default MaxRows = 1_000
public sealed class Country : Entity { … }

[Cacheable(MaxRows = 5_000)]
public sealed class PostalCode : Entity { … }
```

What the declaration produces, with no further code:

1. **Tags.** `entity:<EntityName>` (the entity's logical Queryex name) registered with
   `IVersionTagRegistry` and seeded by the migrator; writes bump it and the composite `entities`.
2. **Startup validation** (in the aggregated composition diagnostic): the entity is top-level,
   has a key, has no child collections, and its `read` securable declares
   `SupportsFilter = false` (T4's registry). A cached list is shared by every reader, so a
   row-level filter on it could only be silently ignored; the rule is enforced by construction,
   not checked per request. A caller must still hold `read` on the resource (T4).
3. **Server cache.** `ICacheableEntities.GetAsync<TEntity>()` in service code, or
   `batch.FromCache<TEntity>()` when the consumer is already building a round trip (validation
   context, import's natural-key translation). The batch form checks the cache first and adds a
   statement only on a miss — one Queryex query over the entity's root with all scalar properties,
   ordered by key, `Take = MaxRows + 1` — emitted after the prelude; the result populates the
   cache before the consumer sees it, so a cold lookup costs no round trip of its own.
4. **Limits.** When `MaxRows + 1` rows come back the list is **not cached**: the consumer still
   receives it, the load is metered `oversized`, and `CacheableEntityOverflow` is logged once per
   type per process. `MaxRows` above 10 000 fails the startup gate — a "cacheable" type of that
   size is a configuration bug.
5. **The cached shape** is `CachedEntitySet<TEntity>`: `Items` (immutable, key order, inactive
   rows included — consumers filter) plus `ById`. Cached instances are shared; mutating one is a
   bug.
6. **Client** (T6 projects the routes): `POST {tenant}/api/web/<entity>/all` returns
   `{ tag: "1.<guid>", rows: [...] }` from the server cache; `POST {tenant}/api/web/settings/entity-tags`
   returns `{ "Country": "1.<guid>", … }` for every cacheable entity. The SPA fetches the latter
   when the `entities` tag in the response header changes and refetches only the lists whose tag
   moved.

**What qualifies** (guidance the attribute's documentation carries): small (`MaxRows` ≤ 10 000,
default 1 000), read-mostly reference data read by pickers on most screens — countries,
currencies, units. Users, roles, and anything with row-level security are not cacheable; their
display names reach the client through the details page's related-entity dictionary. Calendars'
month names are resources, not entities.

**Rationale.** Every effect of "cacheable" derives from one declaration, so the tag bump cannot
be forgotten and the load rides whatever round trip the consumer already needed; the startup
rule is the fail-closed reading of "shared across users".

**Rejected.** A per-request bypass when the caller's read permission is filtered (a runtime
security rule where a startup rule suffices); caching query results keyed by text (unbounded
key space; no tag covers it); a per-entity tag list in every response header (unbounded); a
byte-estimate cap (costs a serialization pass; rows are free); caching per language (entities
carry all three names).

**Confidence.** High.

### D8 — Two settings tables with one criterion: the closed typed row `core.Settings` and the declared key-value rows `core.SettingEntries`; both temporal and audited; `TenantId` as a routing guard

**Decision.**

- `core.Settings` — exactly one row (`Id = 1`, `CHECK`), typed columns for the settings **the
  platform itself reads to build every request's context**: the tenant-id guard, the tenant's
  multilingual display name, the content languages, the calendars, the tenant time zone. Closed
  to packs and distributions: the entity `Settings` is sealed in `Tellma.Core.Abstractions`.
- `core.SettingEntries` — one row per *set* key, for **every other setting**, declared in code
  as `SettingKey<T>` fields (D9) with a default, a category, a scope, and a visibility; a row
  exists only when the value departs from the default; an undeclared key found in the table is a
  Warning at load and ignored.

The criterion is not "essential vs ad hoc" but **"read by the platform on the request path"
versus "read by a feature"**. The first set is fixed by the platform and validated against its
catalogues (language membership, calendar registry, IANA zone mapping) — logic a generic
key-value store cannot express; the second set is open and validated against each key's declared
type. Packs cannot add columns to a Core-owned entity anyway (a leaf inherits from one base, and
several packs cannot all be that base), so pack settings *must* be entries; the typed row is
closed so that there is one mechanism for everyone who is not the platform.

Both entities carry the four audit columns (`CreatedAt`, `CreatedById`, `ModifiedAt`,
`ModifiedById`; `ModifiedAt` is the concurrency stamp — T2's vocabulary), are system-versioned
(`core.SettingsHistory`, `core.SettingEntriesHistory`; period columns are EF shadow properties),
opt into a UDTT (`[TableType]`), and carry `[BumpsVersionTag("settings")]`. There is no special
persistence path: the settings edit (D11) hydrates, merges, validates, and persists through the
bulk emitter like any entity, which is what makes audit stamping, the concurrency check, the
history row, and the tag bump automatic. `core.Settings` has no sequence (the row is inserted by
provisioning, after the system user it references); `core.SettingEntries` uses
`sq_SettingEntries`.

**`TenantId` guard.** The row carries `TenantId` (type follows T1's tenant-id decision; `int`
below). The settings loader compares it with the routed tenant id on every load; a mismatch is a
fail-closed `TenantDatabaseMismatchException` (500-class, logged Critical, alerting), never a
filter.

**Rejected.** Everything in key-value (the request path would parse JSON for the time zone on
every cold load and lose `NOT NULL`); everything typed with a distribution-extended leaf (packs
excluded; every addition a migration; the settings page needs code); one settings table per pack
(N result sets per settings load, N DTOs, N caches); a `Category` column (derivable from the key;
no query filters by it); non-temporal settings (loses "who changed the tenant's time zone and
when", the first question after a settings incident); `TenantId` as the primary key (the entity
contract keys on `Id`; the guard is an assertion, not an identity).

**Confidence.** High on the split; medium on sealing the typed entity (review flag).

### D9 — `SettingKey<T>`: declared once, registered in one line, validated, labelled, permissioned

**Decision.** A pack or distribution declares settings as static fields:

```csharp
// Illustration — the whole distribution-side cost of two tenant settings and one user setting
public static class GlSettings
{
    public static readonly SettingKey<bool> AutoNumberOnPost =
        new("gl.posting.autoNumberOnPost", defaultValue: true, visibility: SettingVisibility.Client);
    public static readonly SettingKey<int?> DefaultCenterId =
        new("gl.posting.defaultCenterId", defaultValue: null);
    public static readonly SettingKey<string[]> PinnedReports =
        new("gl.ui.pinnedReports", defaultValue: [], scope: SettingScope.User, visibility: SettingVisibility.Client);
}
```

and registers the class once in its feature contribution: `feature.SettingKeys(typeof(GlSettings))`
(T1's composition seam; a future manifest generator can discover the class instead). The
platform:

- **Validates at startup** (aggregated into the single composition diagnostic): key grammar
  `^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)+$` (camelCase segments, at least two, ≤ 128 chars),
  uniqueness across the composition, and that `T` is one of the platform's JSON-representable
  types (`bool`, `int`, `long`, `decimal`, `string`, `DateOnly`, `TimeOnly`, `Guid`, enums as
  strings, nullable and array forms of those) **or** the key supplies a `JsonTypeInfo<T>` from the
  declaring package's own source-generated context — the platform's context cannot know a pack's
  record type at compile time.
- **Reads** through `TenantSettings.Get(key)` (tenant scope) or the user-settings bag (user
  scope, T4) with the declared default when no row exists; a stored value that fails to parse as
  `T`, or that fails the key's optional `Validate` delegate, is treated as the default and logged
  + metered (`tellma.settings.entries.invalid`) — a bad row must not take the tenant down.
- **Labels** the key for the settings page, Excel, and MCP by resource lookup
  `Setting_<key with '.' → '_'>` in the declaring assembly's `Strings` (D17), falling back to the
  humanized last segment.
- **Permissions** the key by its `Category` (explicit, default = first segment): the securables
  registry (T4) receives one securable `settings.<category>` × `edit` per distinct tenant-scope
  category at startup, so the per-category permission the brain dump asks for exists with zero
  distribution code. User-scope keys are self-editable and need no securable.
- **Visibility**: `Server` (default) never leaves the server; `Client` is included in the
  client DTO for every authenticated member.
- **Scope**: `Tenant` (default, `core.SettingEntries`) or `User` (T4's `core.UserSettings`,
  D21). One declaration type, one registry, one label convention for "a declared, defaulted,
  typed value".

**Rejected.** Fluent registration of each key in `AddTellma` (a second place to list what the
static field says); a POCO with properties (loses the typed access site
`settings.Get(GlSettings.AutoNumberOnPost)`); assembly scanning for the class (T1's composition is
explicit; one line per pack is cheaper than a scan and a manifest); a `[SettingValue]` attribute
promising inclusion in the platform's JSON context (impossible across assemblies); two definition
types for tenant and user scope (same grammar, same registry, same labels).

**Confidence.** High for tenant scope; medium for user scope (T4 owns that table).

### D10 — The settings DTOs and the read path: tag first, both views built once per tag, the client view pre-serialized

**Decision.** Two DTOs, both built once per `settings` tag change and cached together:

- `TenantSettings` (server): immutable; the typed columns plus the **derived members computed once
  per load** — `Languages` (1–3 `LanguageInfo` in position order), `Shape` (`MultilingualShape`,
  the Queryex schema key), `Calendars` (1–2 `ICalendarSystem`), `TimeZone` (`TimeZoneInfo`),
  `SqlServerTimeZoneName` (the Windows id spec 0008's `TimeZone` slot binds, converted once
  through `TimeZoneInfo.TryConvertIanaIdToWindowsId`), `Entries` (raw JSON by key, undeclared keys
  dropped), `Get<T>(SettingKey<T>)` (parsed once and memoized per entry), and `Tag`.
- `TenantSettingsForClient`: everything an authenticated member may know — names, languages
  (code, symbol, native name, direction), calendars (codes), time zone id, client-visible entries
  (stored or default; the client never needs to know which), `FormatVersion`. It is materialized
  **as pre-serialized UTF-8 JSON** beside `TenantSettings`; the `settings/client` endpoint writes
  the bytes and never serializes on the hot path, and a caller that sends its current wire tag
  receives an "unchanged" result (T6 shapes it).

Read path (`ITenantSettingsCache.GetAsync(tenantId)`): compare the snapshot's `settings` tag with
the cached entry → hit returns the instance with no round trip. Miss → single-flight load, one
batch: the prelude, then

```sql
SELECT [Id], [TenantId], [Name], [Name2], [Name3],
       [PrimaryLanguage], [SecondaryLanguage], [TernaryLanguage],
       [PrimaryCalendar], [SecondaryCalendar], [TimeZone],
       [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]
  FROM [core].[Settings];

SELECT [Key], [Value] FROM [core].[SettingEntries];
```

The loader asserts `TenantId` (D8), resolves languages against the catalogue and calendars
against the registry (an unresolvable value — a pack removed after the tenant chose its calendar
— falls back to the platform default with a Critical log and a `tellma.settings.entries.invalid`
count, never a crash), normalizes and converts the zone, parses entries against the key registry,
builds both views, and stamps the entry with the tag the prelude returned. When the settings load
rides a connect batch, the same two statements are appended in the same order after the prelude.

**Confidence.** High.

### D11 — Settings edit API (position for T8): one field-masked `settings/save`; per-category securables; `settings/refresh-caches`

**Decision (position; T8 owns the endpoint).** `POST {tenant}/api/web/settings/save` takes
`TenantSettingsPatch`: the typed members as nullable values plus `Entries` (key → JSON value or
null) plus a `Fields` list naming which typed members and which entry keys the request sets
(field-mask semantics: absent means untouched, a listed member set to null means "clear", which
is what makes `SecondaryLanguage = null` expressible; a null entry value resets the key to its
default and deletes the row), and `ExpectedModifiedAt` for the typed row. Authorization:
`settings.general` × `edit` for the typed part; `settings.<category>` × `edit` per entry
category; a request touching two categories needs both. Pipeline: hydrate `Settings` and the
affected `SettingEntry` rows in the validation round trip, merge, validate (catalogue rules D13,
calendar registry D14, zone D15, key types and delegates D9), persist through the emitter
(audit, concurrency, history, tag bump all automatic), rebuild the DTOs from the saved rows
under the batch's new tag (`Set`), and return the client DTO with its wire tag. A second action,
`settings/refresh-caches` (admin-only), bumps every tag for use after out-of-band changes.

**Rationale.** ASP.NET Core 10 has JSON Patch (operation-based, in-place mutation, security
caveats) and no maintained merge-patch package; merge patch cannot express "set to null"; a
typed partial DTO with an explicit field list is the compatible shape.

**Confidence.** Medium (T8's call on the exact shape).

### D12 — Two axes: content languages (tenant, ≤ 3) and the request culture (UI, messages, formatting); the request culture is not restricted to the tenant's content languages

**Decision.**

- **Content languages** are `core.Settings.PrimaryLanguage` (required), `SecondaryLanguage`,
  `TernaryLanguage` (optional; ternary requires secondary; all distinct): BCP 47 language tags
  validated against the *distribution's declared languages* (D13). They decide which
  `Name`/`Name2`/`Name3` column holds which language, and how those columns are labelled, and
  nothing else.
- **The request culture** (`LocalizationContext.Culture`, one `CultureInfo` used as both
  `CurrentCulture` and `CurrentUICulture`) is negotiated per request (D16) from any language the
  distribution ships resources for. It decides resource lookup and number/date formatting *inside
  messages and Excel metadata*; the wire is invariant JSON. It need not be a content language.
- **`LocalizationContext.ContentLanguageIndex`** (1..3) is *derived*: the position of the tenant
  content language whose language subtag equals the request culture's, else 1. Servers use it
  wherever they must pick "the" name of an entity (an error message naming a record, an export's
  default name column). No stored preference is needed for it.
- **Formatting culture = request culture** in this release. A user who wants a specific region's
  digits and separators sets a regioned preference (`ui.language = "ar-SA"`), which the SPA sends
  as `Accept-Language`.

**Rationale.** The axes are different things with different owners; the derivation rule removes a
preference while producing the answer users expect (an Arabic UI at an Arabic/English tenant
shows Arabic names first; an English UI at an Amharic-only tenant shows Amharic names with
English chrome).

**Rejected.** Ignoring `Accept-Language` outside the tenant's languages (the brain dump); a stored
content-language preference (one more setting, the same answer almost always); a typed tenant
`DefaultCulture` column for formatting (the server's formatting surface is small, the SPA formats
client-side, and the neutral `ar` culture's separators are a per-user taste better carried by the
regioned preference — review flag).

**Confidence.** High on the axes; medium on "no tenant default culture".

### D13 — The Core language catalogue, the distribution's declaration, the tenant's validation

**Decision.** `Tellma.Core.Abstractions.Localization.LanguageCatalog` is **data in code**: a frozen
list of `LanguageInfo(Code, EnglishName, NativeName, Symbol, IsRightToLeft)` for every language the
platform can *describe*; initial entries `en E`, `ar ع`, `am አ`, `om Om`, `ti ት`, `so So`, `sw Sw`,
`fr F`, `es Es`, `pt P`, `de D`, `tr T`, `ur ا`, `hi हि`, `zh 中`; additive by platform PR. `Symbol`
is the short native-script mark used in labels; a unit test asserts symbols are unique across the
catalogue and every code is a predefined culture. Describing a language is not the same as
shipping its strings: strings fall back to English per key (D18), so adding a catalogue entry is
safe before any translation exists.

The distribution declares its languages once, in `AddTellma`:

```csharp
// Illustration
services.AddTellma(tellma => { tellma.Languages("en", "ar"); /* … */ });
```

Rules, validated at startup into the one composition diagnostic:

- Every declared code exists in the catalogue (or was added with
  `tellma.AddLanguage(new LanguageInfo(…))`, the escape hatch for a language the platform does
  not yet describe).
- The first declared language is the distribution default (used when negotiation finds nothing
  and the tenant's primary is not offered).
- A declared language whose `Tellma.Core` satellite assembly is absent at startup fails the gate
  (catches a `SatelliteResourceLanguages` mismatch); English needs no satellite.
- A pack may declare `SupportedLanguages` in its feature declaration; a distribution language
  outside a referenced pack's set is a **warning** (its strings fall back to English), never an
  error, and no language set is narrowed by referencing a pack.

Tenant validation (settings save, D11): the content languages ∈ the distribution's declared
languages; distinct; ternary ⇒ secondary; the chosen languages' symbols distinct (guaranteed by
the catalogue test). Calendars: primary ∈ the calendar registry, secondary ≠ primary. Time zone:
D15.

**Rationale.** Satellite assemblies cannot be enumerated cheaply at runtime, so a declared list
is needed regardless; putting the descriptive data in Core means neither the distribution nor the
client re-declares symbols, native names, or direction — the client DTO carries them.
`SatelliteResourceLanguages` in the distribution's project trims shipped satellites to the
declared set (a build-side optimization T1's template sets; not required for correctness).

**Rejected.** Deriving the offered set from satellites on disk; per-tenant symbol overrides (the
previous Tellma's three symbol columns for a cosmetic; the catalogue symbol suffices).

**Confidence.** High.

### D14 — Calendars: the contract and registry in Abstractions; `gc`, `uq`, `et` implemented in `Tellma.Core` and registered by the Core feature; one platform renderer; tabular Hijri beyond AH 1500

**Decision.** `Tellma.Core.Abstractions.Calendars` defines `ICalendarSystem` (code, a BCL
`Calendar` for arithmetic, supported range, month names per culture, and the platform's own
`Format` by `DateStyle`), `ICalendarRegistry` (the systems composed into the distribution, by
code), and `CalendarCodes` (`gc`, `uq`, `et` — the same strings spec 0008 §10.4 reserves for the
`cal` argument; the Queryex adapter asserts agreement at startup). `Tellma.Core.Calendars`
implements all three: `gc` over `GregorianCalendar`; `uq` over `UmAlQuraCalendar` (AD 1900-04-30 …
2077-11-16 = AH 1318 … 1500); `et` as a port of the previous Tellma's `EthiopianCalendar`
(Julian-day-number conversion, 13 months, leap rule `year % 4 == 3`, single era, AD 1752-09-14 …
2500-01-01). The Core feature registers all three; a distribution may register further systems
through `feature.AddCalendar<TCalendarSystem>()`.

Tenant settings carry `PrimaryCalendar` (required, default `gc`) and `SecondaryCalendar`
(optional); a user may prefer either (`ui.calendar`, a user-scope key) and a request may override
with the `Tellma-Calendar` header (D16). A calendar outside the tenant's pair is a validation
error (`Calendar_NotOffered`).

**Calendar-aware formatting** is platform code: `ICalendarSystem.Format` computes
`(year, month, day)` through the `Calendar` object, takes month names from Core's resources for
`uq`/`et` (`Calendar_uq_Month1` … `Calendar_et_Month13`) and from the culture's `DateTimeFormat`
for `gc`, and renders one of four styles (`Short` numeric in the culture's order and separator;
`Medium` day, abbreviated month, year; `Long` day, full month, year; `Full` with weekday). Digits
are always ASCII. It never assigns a calendar to `DateTimeFormatInfo` (impossible for custom
calendars and for calendars outside a culture's `OptionalCalendars`). Dates beyond
`UmAlQuraCalendar`'s range render through `HijriCalendar` with `HijriAdjustment = 0`, documented
as a tabular approximation; `ICalendarSystem.MaxSupported` exposes the exact range so validators
can warn on long-dated values.

**Rationale.** A calendar a tenant can select must be registered in the distribution; shipping
the three known implementations in Core (a few hundred dependency-free lines) makes every
reference distribution correct by default while the registry keeps the Locale-pack home open for
future systems. This departs from ARCHITECTURE.md's placement of calendar implementations in
Locale packs (§7); those packs do not exist yet and would add a package reference per calendar
for no isolation benefit.

**Rejected.** The Ethiopian calendar from a hobby NuGet package (unclear licensing, single
author); `-u-ca-` culture names; rendering an out-of-range Umm al-Qura date in Gregorian with a
marker (silently changes the calendar the user asked for); app-local ICU
(`Microsoft.ICU.ICU4C.Runtime`'s latest release is 72.1.0.3 from 2023 and would pin the fleet to
stale CLDR — the OS/container ICU is used and the container base image is pinned instead).

**Confidence.** High on the shape; medium on the Core-versus-Locale-pack home and on the
overflow handling (review flags).

### D15 — Time zones: the tenant zone binds `today()` and the `TimeZone` slot; no `X-Today`, no time-zone header in this release; ICU mode required

**Decision.** `core.Settings.TimeZone` (IANA id, validated and normalized with
`TimeZoneInfo.TryFindSystemTimeZoneById`, and required to convert through
`TryConvertIanaIdToWindowsId`) is the **business** zone. The host binds Queryex's `Today` slot to
the current date in that zone (`TimeProvider.GetUtcNow()` converted, date part) and the `TimeZone`
slot to `TenantSettings.SqlServerTimeZoneName` (spec 0008 §10.6 and §13.1: "the tenant's zone, as
the backend zone name"). Every instant the server renders to text (messages, Excel cells) is
presented in the tenant zone. There is no `X-Today` header and no per-request time-zone header in
this release; `LocalizationContext.TimeZone` exists (equal to the tenant zone today) so a
`Tellma-Time-Zone` header or a `ui.timeZone` preference can be added later without changing
consumers. The startup gate refuses globalization-invariant and NLS modes (the documented
`SortVersion` probe): both conversions above require ICU.

**Rationale.** "Today" in an ERP is the tenant's business date; two colleagues filtering
`PostingDate = today()` must see the same rows; dropping `X-Today` removes a client-controlled
input to security-relevant filters.

**Rejected.** Binding `today()` per user; `X-Today`; a display-zone header now (real need, small
surface, cheap later).

**Confidence.** High.

### D16 — Negotiation: header > preference > tenant > distribution default; extensions stripped; a Gregorian-forced culture clone; effective values echoed; no `RequestLocalizationMiddleware`

**Decision.** The request-context binder (T1's endpoint filter) calls
`ILocalizationNegotiator.Negotiate(NegotiationInput)` once per request:

| Value | 1st | 2nd | 3rd | 4th |
|---|---|---|---|---|
| Culture | `Accept-Language` (q-ordered, first ≤ 3 ranges; `-u-`/`-x-`/`-t-` extensions stripped; matched by language subtag with parent fallback against the distribution's offered set; region kept when the header carried one, e.g. `ar-SA`) | user `ui.language` | tenant `PrimaryLanguage` if offered | distribution default |
| Calendar | `Tellma-Calendar` header (a code ∈ the tenant's pair) | user `ui.calendar` | tenant `PrimaryCalendar` | — |
| Time zone | tenant `TimeZone` | — | — | — |

`Accept-Language` sits first because the SPA sets it explicitly to the user's chosen language on
every call (it is not a forbidden request header) and non-browser clients (MCP, scripts) fall
through to the stored preference. A header value that is present but invalid (unknown calendar
code, unparseable range) is **ignored** with a `tellma.localization.headers.rejected` count,
never a 400: a stale browser must not lock a user out. The response carries `Content-Language`
and `Tellma-Calendar` with the *effective* values so a client that drifted (the admin removed the
secondary calendar) keeps working and corrects itself.

The negotiated `CultureInfo` is a clone of the matched predefined culture with
`DateTimeFormat.Calendar` set to `GregorianCalendar` where the culture's `OptionalCalendars`
allows it (it does for every culture in the catalogue; a unit test pins the offered set × common
regions), so any incidental `ToString()` prints Gregorian and calendar rendering is always
explicit through `ICalendarSystem`. The binder sets `CultureInfo.CurrentCulture` and
`CurrentUICulture` for the request (they flow with the execution context) and stores the
`LocalizationContext` in the scoped request-context holder; the scoped context is the source of
truth and the thread cultures are set from it, never the other way round. Background jobs receive
the same context copied at enqueue (culture name, calendar code, content-language index; seam 9)
or the tenant defaults for system-scheduled work. `RequestLocalizationMiddleware` is not used:
its default culture is static and ours is per tenant.

**Rejected.** `RequestLocalizationMiddleware` with a custom provider; `-u-ca-` in
`Accept-Language`; a 400 on bad headers; stored preference above the header (protects a user
whose browser sends an unexpected OS language to a non-SPA endpoint, but the SPA is the only
browser client and sets the header deliberately — review flag).

**Confidence.** High.

### D17 — Resources: `Resources/Strings.resx` per assembly, English neutral; an ICU `IStringLocalizerFactory` decorator; one shared `MessageFormatter`; calendar-aware dates through `CustomValueFormatter`

**Decision.**

- **Files.** Each assembly with user-facing text ships `Resources/Strings.resx` (neutral =
  English, `NeutralResourcesLanguage("en")`) with satellites `Strings.<lang>.resx`, and a marker
  class `Strings` in its `Resources` namespace (`Tellma.Core.Resources.Strings`,
  `Tellma.Module.Gl.Resources.Strings`), which is the base name the folder-and-file convention
  yields with no `ResourcesPath`. Keys are `PascalCase_Underscored`: entity/property labels
  `Center_Name`, settings `Setting_gl_posting_autoNumberOnPost`, messages `Center_CycleDetected`,
  month names `Calendar_et_Month1`. Packs reference `Microsoft.Extensions.Localization.Abstractions`
  for `IStringLocalizer<Strings>`; `Tellma.Core.Abstractions` stays framework-free and exposes only
  the *address* of Core's strings (`CoreStrings.BaseName`, `CoreStrings.Assembly`) so a pack can
  address them through `IStringLocalizerFactory.Create(baseName, location)` — the framework's own
  API — never a type.
- **ICU everywhere.** `AddTellma` registers `IcuStringLocalizerFactory`, a decorator over the
  framework's `ResourceManagerStringLocalizerFactory`, so *every* localizer in the process —
  `IStringLocalizer<T>` and `Create(baseName, location)` alike — renders through ICU MessageFormat
  (plural, select, selectordinal, nesting) with named arguments only; values without `{` skip the
  parser. The identity server's per-type registration and `ThreadLocal<MessageFormatter>` are not
  repeated; its private copy stays until it can reference the shared one (a follow-up).
- **One shared `MessageFormatter`** (`useCache: true`), culture passed per call: the pattern cache
  is a lock-free `ConcurrentDictionary` and a 16-thread × 50 000-call probe showed no errors; the
  pluralizer dictionaries are configured before first use and never mutated after. Patterns come
  from resources only — an unbounded cache over user-supplied patterns would be a memory hazard.
- **Calendar-aware dates.** The formatter is constructed with a `CustomValueFormatter` whose
  `TryFormatDate`/`TryFormatTime` delegate to the current request's `ICalendarSystem` and time
  zone (`DateOnly`, `DateTime` (tenant-local), and `DateTimeOffset` (converted to the tenant
  zone) are accepted) and whose `TryFormatNumber` keeps the built-in behaviour. `{d, date,
  short|medium|long|full}` therefore works in every calendar; `medium`/`long` — which the library
  itself rejects — are supplied by the hook.
- **Conventions for authors** (in the documentation of `Strings` markers and the distribution
  template): `{count, plural, …}` for counts; `{gender, select, female {…} male {…} other {…}}`
  with `other` mandatory; no positional `{0}`.
- **A malformed pattern** renders the raw value and logs once per key; it never fails the request.

**Rationale.** `.resx` keeps the framework's fallback chain, satellite packaging, and tooling with
zero dependencies; the factory decorator is the one place ICU is wired and it covers packs that
address Core's strings by base name.

**Rejected.** JSON resources through a third-party localizer (one more dependency; loses
satellite trimming); pre-formatting date arguments before the pattern (works, but puts the style
in code instead of in the translator's pattern); positional arguments.

**Confidence.** High.

### D18 — Two fallback chains: resources fall back through the culture's parents to English; content falls back to the primary column; missing strings are metered, never blank

**Decision.** *Resources:* the request culture's parent chain to the neutral English resource
(`ar-SA → ar → neutral`), which the framework's hub-and-spoke `ResourceManager` walks itself; no
tenant-primary hop. A key absent everywhere returns the key text and counts
`tellma.localization.missing` (tag `culture`, the offered set) once per key per culture per
process. The neutral `.resx` is English by construction, which is how "an English string must
always be supplied" is enforced. *Content:* the requested content-language index → the primary
(`Name3 ?? Name`, `Name2 ?? Name`), applied by the client for display and by the server wherever
it renders a name (exports, emails, messages naming a record).

**Rationale.** A Spanish-UI user with a missing Spanish string should see English, not the
tenant's Arabic; the tenant-primary hop the brain dump proposes is right for content, where the
primary column is the only guaranteed value.

**Confidence.** High. Review flag: this drops the brain dump's tenant-primary hop for resources.

### D19 — Labels and the `Name (E)` / `Name (ع)` convention: `[Multilingual]` on the primary, twins by name, labels by convention

**Decision.** A multilingual text group is declared by one attribute on its primary property;
the twins `<Name>2` and `<Name>3` are found by name and must be nullable strings of the same
maximum length (validated at startup):

```csharp
// Illustration
[Required, MaxLength(255), Multilingual] public string Name { get; set; } = null!;
[MaxLength(255)] public string? Name2 { get; set; }
[MaxLength(255)] public string? Name3 { get; set; }
```

`ILabelProvider.PropertyLabel(entityType, propertyName)` returns the localized label for the
request culture by convention — `<Entity>_<Property>` in the declaring assembly's `Strings`, then
in each base class's assembly, then `<Property>` in Core's `Strings` (`Name`, `Code`, `IsActive`,
`Description`, … ship there), then the humanized property name. For a twin the label is the
primary's label plus the symbol of the tenant language at that position — `Name (E)`, `Name (ع)`
— and for a mono-lingual tenant the primary carries no suffix. A twin beyond the tenant's language
count has no label and is not offered. The same provider serves Excel headers (T9), validation
messages naming a field (T5/T6), and the settings page (D9).

**Rationale.** Zero resource entries for the common entity (all its property names are Core
keys); one attribute; the label convention is a platform function, not per-entity code.

**Rejected.** `[Display(Name = …)]` on every property (the previous Tellma; N lines per entity);
attributes on each twin; a pure naming convention without the attribute (`Address2` would be
mistaken for a twin).

**Confidence.** High. T2 owns the entity contract and adopts the attribute (seam 2/3).

### D20 — The Queryex schema is keyed by `MultilingualShape`, not by tenant; gated twins are nulled on save

**Decision.** `IQueryexSchemaProvider.GetSchema(MultilingualShape shape)` (T2 builds it from the EF
model) returns one immutable `QueryexSchema` per shape — `Primary`, `PrimaryAndSecondary`, `All` —
omitting the `*2`/`*3` twins of `[Multilingual]` groups beyond the shape. The provider holds at
most three schemas per process, built lazily, shared by every tenant with that shape; the settings
cache maps a tenant to its shape (`TenantSettings.Shape`), selected per request from the snapshot,
so a settings change that adds a language switches variants on the next request with no cache to
flush. Queryex's own L2/L3 caches, keyed on schema identity (spec 0008 §16), are therefore shared
across tenants. Row-level-security composition and weak-entity path rewriting (T4) operate on
`FilterTree`s, not on the schema. Physical columns are never gated, so a query compiled a moment
before a change still executes; a model change (deployment) is the only other invalidation.

The save pipeline's preprocessing (T5) sets gated twins to `null` before validation; an Excel
import column that maps to a gated twin is a validation error (`Import_LanguageNotConfigured`).

**Rationale.** Per-tenant schemas would multiply Queryex's compiled-SQL caches by the tenant count
for no semantic difference; the shape has three values. The brain dump's "removed from queryex
schema" is better than a runtime diagnostic: an expression cannot even name a column the tenant
does not have.

**Confidence.** High.

### D21 — User settings (position for T4): key-value rows, keys declared as `SettingKey<T>` with `SettingScope.User`, validated by `UserSettingsTag`

**Decision (position; T4 owns the table).** `core.UserSettings` stays key-value (`Id`, `UserId`,
`Key`, `Value` as one JSON value); keys are declared with the same grammar and registry as tenant
keys; the platform declares `ui.language` (`string?`, seeded from the identity server's `locale`
claim at user creation), `ui.calendar` (`string?`), `ui.pinnedScreens` (`string[]`). The
`usersettings` cache kind holds the user's bag (`FrozenDictionary<string, JsonElement>` +
`Get<T>`) per `(tenant, user)`, validated by `UserSettingsTag` on T4's per-user row; the client DTO
is the client-visible entries plus the wire tag. Key-value rather than one JSON column because two
tabs editing different preferences must not conflict; `UserSetting` carries
`[BumpsUserVersionTag(UserVersionTagNames.UserSettings)]` so the emitter bumps the single writer's
tag. Admin-managed pinned screens are a later feature, not a table split now. The table name is
`UserSettings`, symmetric with `Settings`; "preferences" is UI copy.

**Confidence.** Medium (T4's call).

### D22 — Telemetry names

Meter `Tellma.Core`; constants in `Tellma.Core.Abstractions.Caching.CacheTelemetryNames` and
`Tellma.Core.Abstractions.Localization.LocalizationTelemetryNames`:

| Instrument | Kind | Unit | Tags (closed sets) |
|---|---|---|---|
| `tellma.cache.requests` | counter | `{request}` | `cache.kind` ∈ settings, usersettings, permissions, entities; `cache.outcome` ∈ hit, miss, stale, oversized, uncached |
| `tellma.cache.load.duration` | histogram | `s` | `cache.kind` |
| `tellma.cache.entries` | observable gauge | `{entry}` | `cache.kind` |
| `tellma.cache.size` | observable gauge | `{unit}` (entries or rows per kind) | `cache.kind` |
| `tellma.cache.evictions` | counter | `{entry}` | `cache.kind`; `cache.reason` ∈ capacity, expired, replaced |
| `tellma.versiontags.bumps` | counter | `{bump}` | `tag.kind` ∈ settings, permissions, entities, entity, other, user |
| `tellma.versiontags.stale` | counter | `{event}` | `stale.phase` ∈ read-rerun, write-guard, exhausted |
| `tellma.settings.entries.invalid` | counter | `{entry}` | none (the key goes to the log) |
| `tellma.localization.missing` | counter | `{lookup}` | `culture` (the offered set) |
| `tellma.localization.headers.rejected` | counter | `{header}` | `header` ∈ accept-language, calendar |

No tenant, user, entity-name, or key-name tag anywhere; those go to structured log properties.
Structured events (level): `VersionTagRowMissing(name)` (Warning, once per process),
`CacheableEntityOverflow(entity, rows, maxRows)` (Warning, once per type per process),
`StaleVersionTagRetry(name)` (Information), `SettingsEntryUndeclared(key)` (Warning),
`SettingsValueUnresolvable(column, value)` (Critical), `MissingResourceString(baseName, key,
culture)` (Warning, once), `TenantDatabaseMismatch(expected, actual)` (Critical). The alert queries
these back go under `infra/monitoring/` and are cross-checked by the existing name test.

### D23 — Testing

- `test/core/Tellma.Core.Tests` (PR tier): catalogue invariants (unique symbols, every code a
  predefined culture, Gregorian assignable for the offered set × a region sample); the negotiation
  table (headers × preferences × tenant, extension stripping
  `ar-SA-u-ca-islamic-umalqura → ar-SA` with the calendar taken from the header only, invalid
  header ignored); `EthiopianCalendar` conformance vectors (ported from the previous Tellma's
  tests) and `ICalendarSystem` golden strings per calendar × style × culture, including the AH 1500
  overflow; the ICU decorator (plural categories for `ar`/`am`, gender select, malformed pattern →
  raw, calendar-aware `{d, date, medium}`); both fallback chains; `VersionedCache` (tag mismatch =
  miss, single-flight under 64 concurrent callers with one loader, faulted loader retried,
  bounds/eviction, `MaxAge`); the wire-tag form and the **DTO shape snapshot paired with
  `FormatVersion`**; `SettingKey` grammar and registry diagnostics; `[Multilingual]` twin
  discovery and label resolution; schema-shape gating over a fixture model (with T2's adapter).
- `test/core/Tellma.Core.IntegrationTests` (`Category=Integration`, LocalDB/Testcontainers, on
  T2's fixture): end-to-end bump — save a fixture `[Cacheable]` entity through the batch and
  observe `entity:<Name>` and `entities` moved, `settings` untouched; a membership save bumps only
  that user's `PermissionsTag`; the guard raises 51001 on a moved tag and the pipeline re-runs
  once; the prelude heads every batch and the tag-then-rows ordering holds under a concurrent
  bump; the change-tracking write audit over every fixture save; `ServerRoundtrips` from
  `SqlConnection.RetrieveStatistics()` asserting the ledger (1 warm read, 2 warm save, 2 cold
  tenant read); history rows for `Settings`/`SettingEntries` writes and none for tag bumps; the
  `TenantId` guard; migrator seeding and post-migration bump of `core.VersionTags`.
- No `Live=true` suite: nothing here talks to a third party.

### D24 — Configuration

`Tellma:Cache` → `TellmaCacheOptions { SettingsEntries = 10_000, UserSettingsEntries = 50_000,
PermissionsEntries = 50_000, EntityRows = 2_000_000, EntityMaxRowsDefault = 1_000,
EntityMaxRowsCeiling = 10_000, EntityListMaxAge = 15 minutes, TagSnapshots = 10_000 }`,
`ValidateOnStart`. Nothing else is configurable: the language list and the calendars are code
because they are facts about what the distribution ships, not about where it runs.

---

## 3. Contracts

### 3.1 Version tags — `Tellma.Core.Abstractions.Caching`

```contract
// The opaque validator of one cached shape. Equality only. Guid.Empty means "never read".
record VersionTag(Value: Guid)
  None: VersionTag                       // static; matches no entry, forces a load
  New() -> VersionTag                    // static, sync; mints a fresh application-generated tag
  ToWire(formatVersion: int) -> string   // sync; "{formatVersion}.{Value:N}"

// The tenant-level names the platform defines; packs register more through the attribute.
record VersionTagNames
  Settings: string = "settings"
  Permissions: string = "permissions"
  Entities: string = "entities"
  EntityPrefix: string = "entity:"
  MaxNameLength: int = 128
  ForEntity(entityName: string) -> string   // static, sync

// The user-level tag columns on T4's per-user non-temporal row.
enum UserVersionTagNames = Permissions | UserSettings   // columns PermissionsTag, UserSettingsTag

annotation [BumpsVersionTag(name: string)]                      on entity class, repeatable, inherited
annotation [BumpsUserVersionTag(column: UserVersionTagNames, UserIdProperty: string = "UserId")]
                                                                on entity class, repeatable, inherited
annotation [Cacheable(MaxRows: int = 1000)]                     on entity class, inherited

// The tags of one tenant as last read by a batch prelude, overlaid with that batch's bumps.
record VersionTagSnapshot(TenantId: int, Tags: map<string, VersionTag>, ReadAtUtc: DateTime)
  this[name: string] -> VersionTag       // sync; None when absent
  Has(name: string) -> bool              // sync; false means the kind is uncacheable on this instance

service IVersionTagSnapshots
  Current(tenantId: int) -> VersionTagSnapshot        // sync; an empty snapshot forces loads
  Replace(snapshot: VersionTagSnapshot)               // sync; called by the executor after every batch

enum VersionTagMismatchPolicy = Rerun | Refresh

// A cached input a batch composed; the executor guards writes and applies the policy to reads.
record VersionTagDependency(Name: string, ExpectedTag: VersionTag, OnMismatch: VersionTagMismatchPolicy)

// Composition-time registry built from the EF model's attributes; validated at the startup gate.
service IVersionTagRegistry
  Names: list<string>                                           // every tenant-level name, for seeding
  Resolve(writtenEntityTypes: list<Type>) -> VersionTagEffects  // sync

record VersionTagEffects(TenantNames: list<string>, UserRules: list<UserVersionTagRule>)
record UserVersionTagRule(EntityType: Type, Column: UserVersionTagNames, UserIdProperty: string)

// Raised when a batch's dependencies no longer match the database and the bounded re-run is
// exhausted (reads) or the guard threw twice (writes). Retryable by the caller (503-class).
record StaleVersionTagException(Dependencies: list<VersionTagDependency>)   // an exception type

// Raised when core.Settings.TenantId differs from the routed tenant. Fail-closed; 500-class.
record TenantDatabaseMismatchException(ExpectedTenantId: int, ActualTenantId: int)   // an exception type
```

The standalone table type for the write guard, beside `IdList` in
`Tellma.Core.Abstractions.TableTypes`:

```contract
// [TableType] standalone shape; physical name VersionTagList_<hash8>.
data VersionTagList
  Name: nvarchar(128)   key
  Tag: uniqueidentifier
```

### 3.2 The versioned cache — `Tellma.Core.Abstractions.Caching` (contract) and `Tellma.Core.Caching` (base)

```contract
enum CacheOutcome = Hit | Miss | Stale | Oversized | Uncached

record CacheResult<TValue>(Value: TValue, Tag: VersionTag, Outcome: CacheOutcome)

// Bound from Tellma:Cache; ValidateOnStart.
data TellmaCacheOptions
  SettingsEntries: int = 10000
  UserSettingsEntries: int = 50000
  PermissionsEntries: int = 50000
  EntityRows: long = 2000000
  EntityMaxRowsDefault: int = 1000
  EntityMaxRowsCeiling: int = 10000
  EntityListMaxAge: TimeSpan = 15 minutes
  TagSnapshots: int = 10000

// The platform's one in-process cache primitive (Tellma.Core.Caching); derived per kind.
base VersionedCache<TKey, TValue>
  ctor(kind: string, sizeLimit: long, maxAge: TimeSpan?, meters: IMeterFactory)
  GetAsync(key: TKey, currentTag: VersionTag) -> CacheResult<TValue>
  Set(key: TKey, value: TValue, tag: VersionTag)                      // sync
  LoadAsync(key: TKey) -> (Value: TValue, Tag: VersionTag, Size: long) // abstract; one batch, tag from its prelude
  Dispose()                                                            // sync

// An immutable cached list of one cacheable entity type.
record CachedEntitySet<TEntity>
  Tag: VersionTag
  LoadedAt: DateTimeOffset
  Items: list<TEntity>              // key order, inactive rows included
  ById: map<int, TEntity>

service ICacheableEntities
  GetAsync<TEntity>() -> CacheResult<CachedEntitySet<TEntity>>   // where TEntity: cacheable entity
  Peek<TEntity>() -> CachedEntitySet<TEntity>?                   // sync; null when absent or stale
  GetWireTagsAsync() -> map<string, string>                      // entity name -> wire tag, for settings/entity-tags
```

### 3.3 Settings — `Tellma.Core.Abstractions.Settings`

```contract
enum SettingScope = Tenant | User
enum SettingVisibility = Server | Client
enum MultilingualShape = Primary (1) | PrimaryAndSecondary (2) | All (3)

// The untyped view of a declared key, for the registry, the settings page, Excel, and MCP.
base SettingKey
  Name: string                 // dotted camelCase segments, at least two, <= 128 chars
  Category: string             // default: first segment
  Scope: SettingScope
  Visibility: SettingVisibility
  ValueType: Type
  DefaultJson: JsonElement
  LabelKey: string             // "Setting_" + Name with '.' -> '_'

// A typed, defaulted setting declared once as a static field.
record SettingKey<T> : SettingKey
  ctor(name: string, defaultValue: T, category: string? = null, scope: SettingScope = Tenant,
       visibility: SettingVisibility = Server, validate: (T -> string?)? = null,
       typeInfo: JsonTypeInfo<T>? = null)      // typeInfo required for T outside the platform set
  DefaultValue: T
  Validate: (T -> string?)?                    // returns a localizable error code, or null

service ISettingKeyRegistry
  Keys: list<SettingKey>                       // declaration order
  Find(name: string) -> SettingKey?            // sync
  Categories: list<string>                     // distinct tenant-scope categories -> securables settings.<category>

// The resolved, immutable, cached server view of a tenant's settings.
record TenantSettings
  TenantId: int
  Tag: VersionTag
  Names: list<string?>                          // by language position 1..3
  Languages: list<LanguageInfo>                 // 1..3, position order
  Shape: MultilingualShape                      // = Languages.Count
  Calendars: list<ICalendarSystem>              // 1..2, primary first
  TimeZone: TimeZoneInfo
  SqlServerTimeZoneName: string                 // the backend zone name bound to Queryex's TimeZone slot
  ModifiedAt: DateTime
  Entries: map<string, JsonElement>             // stored values only; undeclared keys dropped
  Get<T>(key: SettingKey<T>) -> T               // sync; stored, else default; memoized
  Today(clock: TimeProvider) -> DateOnly        // sync; the current date in TimeZone

// What the browser caches; every authenticated member may see it.
record TenantSettingsForClient
  FormatVersion: int = 1                        // constant; part of the wire tag
  TenantId: int
  Names: list<string?>
  Languages: list<LanguageInfo>
  Calendars: list<string>                       // codes, primary first
  TimeZone: string                              // IANA id
  Entries: map<string, JsonElement>             // client-visible keys, stored or default

service ITenantSettingsCache
  GetAsync(tenantId: int) -> CacheResult<TenantSettings>
  GetForClientAsync(tenantId: int) -> (Json: bytes, WireTag: string)   // pre-serialized UTF-8
  Peek(tenantId: int) -> TenantSettings?                                 // sync

// The edit shape (position for T8): field-mask semantics over Fields.
record TenantSettingsPatch(
  Fields: list<string>,
  Name: string?, Name2: string?, Name3: string?,
  PrimaryLanguage: string?, SecondaryLanguage: string?, TernaryLanguage: string?,
  PrimaryCalendar: string?, SecondaryCalendar: string?, TimeZone: string?,
  Entries: map<string, JsonElement?>?,
  ExpectedModifiedAt: DateTime)
```

### 3.4 Localization — `Tellma.Core.Abstractions.Localization`

```contract
record LanguageInfo(Code: string, EnglishName: string, NativeName: string, Symbol: string, IsRightToLeft: bool)

service ILanguageCatalog
  All: list<LanguageInfo>                       // every language the platform describes
  Offered: list<LanguageInfo>                   // the distribution's declared subset; first = default
  Find(code: string) -> LanguageInfo?           // sync; ordinal-ignore-case
  IsOffered(code: string) -> bool               // sync

annotation [Multilingual]                       on the primary property of a text group

// The address of Tellma.Core's own resources, for packs that cannot reference the assembly.
record CoreStrings
  BaseName: string = "Tellma.Core.Resources.Strings"
  Assembly: string = "Tellma.Core"

service ILabelProvider
  EntityLabel(entityType: Type) -> string                            // sync
  PropertyLabel(entityType: Type, propertyName: string) -> string    // sync; twins carry the symbol suffix
  SettingLabel(key: SettingKey) -> string                            // sync

// The negotiated values of one request or job scope; part of T1's request context.
record LocalizationContext(Culture: CultureInfo, Calendar: ICalendarSystem, TimeZone: TimeZoneInfo,
                           ContentLanguageIndex: int)               // Culture: Gregorian-forced clone

record NegotiationInput(AcceptLanguage: string?, CalendarHeader: string?,
                        PreferredLanguage: string?, PreferredCalendar: string?,
                        Settings: TenantSettings)

service ILocalizationNegotiator
  Negotiate(input: NegotiationInput) -> LocalizationContext   // sync; never throws on bad input

record LocalizationHeaders
  Calendar: string = "Tellma-Calendar"                  // request: wanted code; response: effective code
  VersionTags: string = "Tellma-Version-Tags"           // response: name=wiretag pairs
  UiLanguageKey: string = "ui.language"                 // user-scope setting keys the platform declares
  UiCalendarKey: string = "ui.calendar"
  UiPinnedScreensKey: string = "ui.pinnedScreens"
```

### 3.5 Calendars — `Tellma.Core.Abstractions.Calendars`

```contract
record CalendarCodes
  Gregorian: string = "gc"
  UmAlQura: string = "uq"
  Ethiopian: string = "et"

enum DateStyle = Short | Medium | Long | Full

contract ICalendarSystem
  Code: string
  EnglishName: string
  Calendar: System.Globalization.Calendar       // arithmetic only; never assigned to a DateTimeFormatInfo
  MonthCount: int                               // 12, or 13 for et
  MinSupported: DateOnly
  MaxSupported: DateOnly
  Decompose(date: DateOnly) -> (Year: int, Month: int, Day: int)                     // sync
  Format(date: DateOnly, culture: CultureInfo, style: DateStyle) -> string            // sync
  Format(instant: DateTimeOffset, zone: TimeZoneInfo, culture: CultureInfo, style: DateStyle,
         includeTime: bool = true) -> string                                          // sync
  MonthName(month: int, culture: CultureInfo, abbreviated: bool) -> string            // sync

service ICalendarRegistry
  Codes: list<string>
  this[code: string] -> ICalendarSystem                        // sync; throws for unknown
  TryGet(code: string) -> ICalendarSystem?                     // sync
```

### 3.6 Telemetry names — `CacheTelemetryNames`, `LocalizationTelemetryNames`

```contract
record CacheTelemetryNames
  Meter: string = "Tellma.Core"
  Requests: string = "tellma.cache.requests"
  LoadDuration: string = "tellma.cache.load.duration"
  Entries: string = "tellma.cache.entries"
  Size: string = "tellma.cache.size"
  Evictions: string = "tellma.cache.evictions"
  Bumps: string = "tellma.versiontags.bumps"
  Stale: string = "tellma.versiontags.stale"
  InvalidEntries: string = "tellma.settings.entries.invalid"
  KindTag: string = "cache.kind"           // values: settings, usersettings, permissions, entities
  OutcomeTag: string = "cache.outcome"     // values: hit, miss, stale, oversized, uncached
  ReasonTag: string = "cache.reason"       // values: capacity, expired, replaced
  TagKindTag: string = "tag.kind"          // values: settings, permissions, entities, entity, other, user
  StalePhaseTag: string = "stale.phase"    // values: read-rerun, write-guard, exhausted

record LocalizationTelemetryNames
  Missing: string = "tellma.localization.missing"
  HeadersRejected: string = "tellma.localization.headers.rejected"
  CultureTag: string = "culture"
  HeaderTag: string = "header"             // values: accept-language, calendar
```

### 3.7 What this theme needs from other themes' seams

From **T2 (batch abstraction, seam 1)** — the hooks that make D3 and D4 structural:

```contract
// Every statement declares what it writes; the save emitter fills it, raw SQL must declare it.
contract IDbStatement
  WrittenEntityTypes: list<Type>       // empty for reads
  MayRetry: bool

// Batch-level declarations the executor honours.
service IDbBatchBuilder
  Raw(sql: string, parameters: list<DbParameterBinding>, mayRetry: bool, writes: list<Type>) -> IDbBatchBuilder
  DependsOn(dependencies: list<VersionTagDependency>) -> IDbBatchBuilder   // guards writes, re-runs/refreshes reads
  FromCache<TEntity>() -> Handle<CachedEntitySet<TEntity>>                 // no statement when warm
  LoadTenantSettings() -> Handle<TenantSettings>                           // the two statements of D10, when cold

// Batch results this theme reads.
contract IDbBatchResult
  VersionTags: VersionTagSnapshot      // the prelude's rows overlaid with this batch's bumps
```

Executor obligations: the tag read is the first statement of every batch; every cold load is
emitted after it and stamped with its tag; the guard is the first statement inside the
transaction; the bump statements are the last before `COMMIT`; `IVersionTagSnapshots.Replace` is
called after every batch; error 51001 maps to `StaleVersionTagException`; the host parameter
prefix `@tm_` stays outside Queryex's `@qx` namespace; `StringList`, `IdList`, and
`VersionTagList` are registered standalone types; the error band 51000–51099 is T2's, 51001 is
claimed here; the LocalDB fixture enables change tracking for the write audit.

From **T1 (request context, seam 9; composition, seam 6)**: a scoped holder exposing `TenantId`,
`UserId?`, `IsSandbox`, `Settings: TenantSettings`, and `Localization: LocalizationContext`; the
binder order resolve tenant → `ITenantSettingsCache.GetAsync` (last-known snapshot; optimistic) →
T4's user and user settings → `ILocalizationNegotiator` → set the thread cultures → populate the
holder; job scopes copy a serialized `LocalizationContext` (culture name, calendar code,
content-language index) at enqueue. Builder calls: `tellma.Languages(…)`, `tellma.AddLanguage(…)`,
`feature.SettingKeys(typeof(…))`, `feature.AddCalendar<T>()`; the Core feature registers
`Settings`, `SettingEntry`, `VersionTags` in the model, the caches, the negotiator, the ICU factory
decorator, the three calendars, and the registry; the aggregated startup diagnostic carries the
language, satellite, key-grammar, symbol-uniqueness, cacheable-entity, and ICU-mode checks.

From **T4 (seams 5 and 11)**: the per-user non-temporal row with `PermissionsTag uniqueidentifier
NOT NULL` and `UserSettingsTag uniqueidentifier NOT NULL` (defaults `NEWID()`, inserted with the
user), returned by the connect statement in the first batch; `[BumpsVersionTag("permissions")]`
on `Role` and `Permission`; `[BumpsUserVersionTag(Permissions)]` on `RoleMembership` and
`[BumpsUserVersionTag(UserSettings)]` on `UserSetting`; the permissions cache validated against
both tags and declaring a `Rerun` dependency; the securables registry accepting
`settings.<category>` × `edit` from `ISettingKeyRegistry.Categories` and exposing
`SupportsFilter` per resource for D7's startup rule; the deactivation `IF` next to the guard.

From **T6 (seam 13)**: the `Tellma-Version-Tags`, `Content-Language`, and `Tellma-Calendar`
response headers; the `Tellma-Calendar` request header and `Accept-Language` semantics; routes
`settings/client` (honouring a supplied tag with an "unchanged" result), `settings/entity-tags`,
`<entity>/all`, `settings/save`, `settings/refresh-caches`; validation messages rendered through
`IStringLocalizer` under the request culture; the 503 mapping of `StaleVersionTagException` and
the 500 mapping of `TenantDatabaseMismatchException`.

From **T9**: `ILabelProvider` for headers; `ICalendarSystem` and `LocalizationContext` for number
and date metadata; `MultilingualShape` for column mapping.

---

## 4. Schema

All tables in schema `core`; plural names except the single-row `Settings` (already a plural
noun); explicit constraint names; period columns are EF shadow properties; no IDENTITY anywhere;
audit column types follow T2's base (`datetime2(7)` below per the orchestrator's hint).

**`core.Settings`** — one row per tenant database; carries the audited base, temporal, `[TableType]`
(`SettingsList`; period columns excluded by derivation), `[BumpsVersionTag("settings")]`,
`[Multilingual]` on `Name`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK, `CHECK ([Id] = 1)` | no sequence; inserted by provisioning |
| `TenantId` | `int` | no | | routing guard, never a filter |
| `Name` | `nvarchar(255)` | no | | tenant display name, primary language |
| `Name2` | `nvarchar(255)` | yes | | |
| `Name3` | `nvarchar(255)` | yes | | |
| `PrimaryLanguage` | `nvarchar(16)` | no | | BCP 47; ∈ distribution languages |
| `SecondaryLanguage` | `nvarchar(16)` | yes | `CK_Settings_Languages` | distinct from primary |
| `TernaryLanguage` | `nvarchar(16)` | yes | `CK_Settings_Languages` | requires secondary; distinct |
| `PrimaryCalendar` | `nvarchar(8)` | no | default `N'gc'` | ∈ calendar registry |
| `SecondaryCalendar` | `nvarchar(8)` | yes | `CK_Settings_Calendars` | ≠ primary |
| `TimeZone` | `nvarchar(64)` | no | | IANA id, normalized |
| `CreatedAt` | `datetime2(7)` | no | | |
| `CreatedById` | `int` | no | FK → `core.Users` | |
| `ModifiedAt` | `datetime2(7)` | no | | concurrency stamp |
| `ModifiedById` | `int` | no | FK → `core.Users` | |
| `ValidFrom` / `ValidTo` | `datetime2(7)` | no | `PERIOD FOR SYSTEM_TIME` | shadow |

```sql
CONSTRAINT [CK_Settings_Languages] CHECK (
    ([SecondaryLanguage] IS NULL OR [SecondaryLanguage] <> [PrimaryLanguage]) AND
    ([TernaryLanguage] IS NULL OR ([SecondaryLanguage] IS NOT NULL
        AND [TernaryLanguage] <> [PrimaryLanguage] AND [TernaryLanguage] <> [SecondaryLanguage]))),
CONSTRAINT [CK_Settings_Calendars] CHECK ([SecondaryCalendar] IS NULL OR [SecondaryCalendar] <> [PrimaryCalendar])
-- WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [core].[SettingsHistory])); no index beyond the PK.
```

**`core.SettingEntries`** — declared keys whose value departs from the default; audited,
temporal (`core.SettingEntriesHistory`), `[TableType]` (`SettingEntriesList`),
`[BumpsVersionTag("settings")]`; sequence `sq_SettingEntries` starting above the reserved seed
band. Exposed to Queryex as entity `SettingEntry` (`Key`, `Value` are strings) for the admin list.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK, sequence `sq_SettingEntries` | |
| `Key` | `nvarchar(128)` | no | unique `UX_SettingEntries_Key` | `gl.posting.autoNumberOnPost` |
| `Value` | `nvarchar(max)` | no | | one JSON value |
| `CreatedAt` | `datetime2(7)` | no | | |
| `CreatedById` | `int` | no | FK → `core.Users` | |
| `ModifiedAt` | `datetime2(7)` | no | | concurrency stamp |
| `ModifiedById` | `int` | no | FK → `core.Users` | |
| `ValidFrom` / `ValidTo` | `datetime2(7)` | no | `PERIOD FOR SYSTEM_TIME` | shadow |

**`core.VersionTags`** — tenant-level tags. Non-temporal, no audit, no sequence, no UDTT, not an
entity, absent from the Queryex schema and the CRUD stack.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Name` | `nvarchar(128)` | no | PK clustered | `settings`, `permissions`, `entities`, `entity:<Name>`, … |
| `Tag` | `uniqueidentifier` | no | default `NEWID()` | application-generated on bump |

```sql
-- Seeded by the migrator on every run from IVersionTagRegistry.Names:
INSERT INTO [core].[VersionTags] ([Name], [Tag])
SELECT n.[Id], NEWID() FROM @names AS n
 WHERE NOT EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WHERE t.[Name] = n.[Id]);
-- Bumped wholesale by the migrator after applying migrations:
UPDATE [core].[VersionTags] SET [Tag] = NEWID();
-- Read: the prelude of D4. Bump: the statement of D3. Guard: the statement of D4.
```

**`VersionTagList`** — standalone table type (spec 0001), physical `[dbo].[VersionTagList_<hash8>]`:
`Name nvarchar(128) NOT NULL PRIMARY KEY`, `Tag uniqueidentifier NOT NULL`.

**Required on T4's per-user non-temporal row** (working name `core.UserStates`; T4 names it):
`UserId int PK FK → core.Users`, `PermissionsTag uniqueidentifier NOT NULL DEFAULT NEWID()`,
`UserSettingsTag uniqueidentifier NOT NULL DEFAULT NEWID()`; one row inserted with every user
row; T4 adds `LastActive` and the inbox watermark there.

Indexes beyond the keys above: none. Every read is by primary key or a full scan of a one-page
table; tags are never indexed or range-compared.

---

## 5. Answers to the brain dump's open questions in this theme

| Brain-dump question (abridged) | Answer | Decision |
|---|---|---|
| Caching: "How do we guarantee the cache version is invalidated when a cacheable entity is updated?" | Statements declare their written entity types; attributes map tables to tag names; the executor appends the bump last before `COMMIT`; the migrator bumps everything after migrations; a change-tracking audit in the test tier proves no write escaped; a `MaxAge` backstop bounds any bypass of entity lists. | D3, D6 |
| Caching: "Is 'version' the accurate technical name? etag? fingerprint?" | "Version tag"; ETag stays HTTP's; fingerprint rejected. | D1 |
| Caching: "Is 'metaversion' the accurate technical name?" | No — "format version", a `FormatVersion` constant per client-facing shape, folded into the wire tag and pinned by a shape test. | D5 |
| Caching: LRU, thread-safe, stampede-safe, observability, "was it right to cache" | Private bounded `MemoryCache` per kind, single-flight, meters with `oversized`/`uncached` outcomes and the overflow log. | D6, D7, D22 |
| Caching prose: "versions read before executing any API call" | Read as the first statement of every batch and compared afterwards; per-kind mismatch policy; writes guarded in the transaction. | D4 |
| Settings: "Is calling the table Settings correct?" | Yes — `core.Settings`, one row, sealed entity `Settings`. | D8 |
| Settings: "What do we call the KV table?" | `core.SettingEntries`, entity `SettingEntry`; no `Category` column. | D8, D9 |
| Settings: "Language axis vs culture axis?" | Two axes: tenant content languages (language tags, ≤ 3) and the per-request culture (any offered language, region allowed); formatting culture = request culture. | D12 |
| Settings: "Criteria for top-level table vs KV? Everything in KV?" | Typed = read by the platform on the request path; everything declared by packs and distributions = entries via `SettingKey<T>`; never everything in KV. | D8 |
| Settings: "Single API method vs per category? Permission resource = category?" | One `settings/save`; securables `settings.general` and `settings.<category>` × `edit`, auto-registered. | D11, D9 |
| Settings: "Edit signature: patch vs load-and-save?" | Field-masked partial DTO on the wire; hydrate-merge-save through the emitter inside. | D11 |
| Settings: "SettingsVersion … will cause temporal churn, needs its own table" | `core.VersionTags`, non-temporal. | D2 |
| Localization 1: "Compliance modules may not support all languages — restrict?" | No restriction; a pack may declare `SupportedLanguages`, mismatch is a startup warning; strings fall back to English. | D13 |
| Localization 2: "How do we organize the resource files in the backend?" | `Resources/Strings.resx` + satellites and a `Strings` marker per assembly; key conventions; Core's address exposed from Abstractions. | D17 |
| Localization 3: "How do we support ICU message format?" | `IcuStringLocalizerFactory` decorator, one shared `MessageFormatter`, named arguments, calendar-aware dates through `CustomValueFormatter`. | D17 |
| Localization prose: "culture headers ignored if not one of the tenant's languages" | Dropped: the request culture is independent of content languages; only the content-language index is tenant-bound. | D12 |
| Localization prose: "custom calendar header (does a standard one exist?)" | None exists; `Tellma-Calendar` request/response header; precedence header > preference > tenant. | D14, D16 |
| Localization prose: "fallback: request culture → tenant primary → English" | Resources: culture chain → English only; content: index → primary column. | D18 |
| User: "Separate SettingsVersion and PermissionsVersion or one?" | Separate: `UserSettingsTag` and `PermissionsTag` on T4's per-user row, plus tenant-level `permissions` for role and permission edits. | D2 |
| User: "Version, ETag, or fingerprint?" | Version tag. | D1 |
| User: "Is JSON the right shape for user preferences? Pinned screens in a distinct table?" | Key-value rows with declared `SettingKey<T>` keys (`ui.pinnedScreens` is a `string[]`); a table split waits for the admin-managed feature. | D21 |
| Web: "X-Today header? Or time zone instead?" | Neither: `today()` is the tenant zone's date per spec 0008; the tenant zone is a setting; a display-zone header is a later addition. | D15 |
| "Name2/Name3 removed from queryex schema if not configured" | Three process-wide schemas keyed by `MultilingualShape`; gated twins nulled on save. | D20 |
| "Name (E)" / "Name (ع)" convention | `[Multilingual]` + catalogue `Symbol`; `ILabelProvider`; no suffix for mono-lingual tenants. | D19 |
| "Cacheable entities … up to 50–100 records?" | `[Cacheable(MaxRows)]`, default 1 000, ceiling 10 000; unfiltered `read` securable required at startup. | D7 |
| "Settings publicly accessible, not subject to READ permissions" | True for `TenantSettingsForClient` (client-visible keys only); server-only keys never leave the server. | D9, D10 |

---

## 6. Seams

1. **Batch abstraction (T2 owns).** Needed: per-statement `WrittenEntityTypes` and `MayRetry`;
   `Raw(…, writes:)`; `DependsOn(VersionTagDependency…)`; `FromCache<T>()` and
   `LoadTenantSettings()` as load-if-cold statements; the tag read as the first statement of every
   batch with its result on `IDbBatchResult.VersionTags`; the guard first inside the transaction
   and the bumps last before `COMMIT`; error 51001 → `StaleVersionTagException`; `@tm_` prefix;
   `StringList`, `IdList`, `VersionTagList` standalone types; change tracking on the test
   fixture. The bump and guard statements are this theme's; T2 guarantees the executor emits them
   from the registry on every batch.
2. **Entity class vs wire shape (T2/T6).** `TenantSettings`/`TenantSettingsForClient` are read
   DTOs built from the entity, not persistence DTOs; the settings save goes through the entity.
   Cacheable lists are entity arrays (no children) plus a wire tag. `[Multilingual]` is adopted by
   T2's entity contract.
3. **One capability, declared once (T5 owns).** `[Cacheable]` projects a tag name and write
   rule, a cache-kind entry, a startup validation (top-level, no children, unfiltered `read`
   securable), a service method (`GetAllCached`), and a route (`<entity>/all`).
   `[Multilingual]` projects schema gating, labels, Excel mapping, and save preprocessing.
4. **Queryex schema per tenant configuration (T2 owns).** Key = `MultilingualShape` (three
   values), lifetime = process, selected per request from `TenantSettings`; RLS composition and
   weak-entity rewriting are `FilterTree` work outside the schema; the `TimeZone` slot binds
   `TenantSettings.SqlServerTimeZoneName`, `Today` the current date in `TenantSettings.TimeZone`;
   `CalendarCodes` is the shared source for spec 0008's reserved `cal` codes; the `uq` month-map
   table the engine amendment needs is claimed by this theme when T2 schedules it.
5. **Version tags (this theme owns).** Names, tables, bump, prelude, guard, policies, wire form,
   format version as in D1–D5. T4 consumes `permissions` and owns the per-user row's two tag
   columns; T2 emits; T6 carries the header.
6. **Feature composition (T1 owns).** Builder calls `Languages`, `AddLanguage`, `SettingKeys`,
   `AddCalendar`; the Core feature's Contribute registrations and the aggregated diagnostic
   contents (§3.7).
9. **Request context (T1 owns).** `Settings` and `Localization` are this theme's members; the
   binder order in §3.7; job scopes carry a serialized `LocalizationContext` or tenant defaults.
11. **Permission evaluation (T4 owns).** Needed: `SupportsFilter` per resource in the securables
    registry (D7), `settings.<category>` securables from `ISettingKeyRegistry.Categories` (D9),
    the tag attributes on `Role`/`Permission`/`RoleMembership`/`UserSetting` (D2), the
    permissions cache validated against `permissions` and `PermissionsTag` with a `Rerun`
    dependency (D4).
13. **Wire shapes (T6 owns).** The three response headers; the `Tellma-Calendar` request header;
    the five settings routes and `<entity>/all`; `Accept-Language` semantics; the error-message
    localization hook; the 503/500 mappings.
14. **Telemetry (T2 owns the DB-call budget; each theme names its own).** D22's instruments
    under meter `Tellma.Core`; no tenant/entity/key tags.
16. **Connect-call collapse (T4/T5).** The prelude is the collapse's mechanism: cold loads ride
    the first batch keyed off the subject in SQL; reads are optimistic with a per-kind policy;
    writes are guarded in the transaction; the ledger is 1 warm read, 2 warm save, at most one
    extra for cold caches; failure modes in D4.
17. **Vocabulary.** Plural tables (`core.SettingEntries`, `core.VersionTags`, `core.UserSettings`)
    and the single-row `core.Settings`; schema `core`; four audit columns with system versioning
    additive; `ModifiedAt` as the concurrency stamp; "version tag"; "format version"; column
    `Tag`; `UserSettings` (not `UserPreferences`) with "preferences" as UI copy only;
    `nvarchar` for every code column.

---

## 7. Departures from ARCHITECTURE.md

1. **Calendar implementations ship in `Tellma.Core`, not in Locale packs.** ARCHITECTURE.md's
   per-dimension contents place `ICalendar` and calendar conversion rules in `Tellma.Locale.<id>`.
   The contract and registry stay in Abstractions so a Locale pack *can* register a calendar, but
   `gc`/`uq`/`et` are implemented and registered by Core: the codes are part of Queryex's language
   surface, the three implementations are small and dependency-free, Locale packs do not exist
   yet, and a tenant-selectable calendar should not depend on which packs a distribution
   references. The contract is named `ICalendarSystem` to avoid confusion with
   `System.Globalization.Calendar`. Locale packs keep number-to-words, language-specific text
   utilities, and the client's strings and fonts. Spec 0008 §10.4's note that the reserved codes
   "land with the Locale packs" is superseded accordingly (T2 documents the engine amendment).
2. **Server-side translations ship with the package that owns the strings**, as satellite
   assemblies inside `Tellma.Core` (and each pack), trimmed per distribution by
   `SatelliteResourceLanguages` — a satellite must carry the main assembly's name and sit beside
   it, so the owning package is the natural carrier. The client keeps the Locale-pack model.
3. **`core.Settings` is closed to leaf extension.** ARCHITECTURE.md's "extend a pack entity
   (additive — the common customization)" is not offered for `Settings`; the extension mechanism
   is `SettingKey<T>` (D8, review flag).
4. **Platform bookkeeping tables without surrogate keys or sequences.** `core.VersionTags` (natural
   `Name`) and T4's per-user row (PK = FK) are not entities: no `Id`, no `sq_` sequence, absent
   from the Queryex schema and the CRUD stack. "Every table draws its surrogate keys from a
   per-table sequence" is read as applying to entity tables; the exception is named so the
   seed-band test and the sequence convention exclude these tables explicitly.
5. **No `HybridCache`, no output caching for these responses.** "In-memory caching with proper
   invalidation, output caching" is honoured with a private-`MemoryCache` design; output caching
   cannot serve authenticated POSTs, so the version-tag header is the client's cache channel.
6. **Feature composition surface grows** by `Languages`, `AddLanguage`, `SettingKeys`, and
   `AddCalendar`, consistent with the minimal-fidelity composition T1 builds.

---

## 8. Verification

Facts relied on from `research/settings-cache-l10n.md` (verified there 2026-09-01) and the
briefing's digest:

- `HybridCache` 10.9.0 uses the DI `IMemoryCache` as L1, serializes every write even L1-only,
  deserializes mutable values on read, keeps tag-invalidation stamps process-local, defaults
  `MaximumPayloadBytes` to 1 MB, and offers per-instance stampede protection; `IMemoryCache.GetOrCreate`
  has none; `SizeLimit` is unitless and the docs warn against limiting the shared DI cache
  (§1.2–1.5, 1.8).
- `MemoryCache.GetCurrentStatistics()` returns null unless `TrackStatistics` is set (both
  proposals verified this against Microsoft Learn on 2026-09-01; not re-verified here).
- `MessageFormat` 8.0.0: plural/select/selectordinal/nesting; `date` styles limited to
  `short`/`full` through `DateTimeFormatInfo`; `CustomValueFormatter` with
  `TryFormatDate/Time/Number`; per-call culture override; lock-free pattern cache; 16 threads ×
  50 000 formats with zero errors (§2.2–2.4).
- `-u-ca-` culture names create custom cultures whose calendar does not change, whose ICU
  patterns produce mixed output, and whose `Parent` skips the region; `-u-nu-` does not switch
  digits (§3.4). No Ethiopic calendar exists in .NET; a custom `Calendar` cannot be assigned to
  `DateTimeFormatInfo.Calendar`; `UmAlQuraCalendar` ends 2077-11-16 (AH 1500); the BCL formats
  Hijri only under Arabic cultures; the previous Tellma's `EthiopianCalendar` and calendar codes
  `gc`/`et`/`uq` (§4.1–4.4).
- IANA ids resolve case-insensitively with ICU; `TryConvertIanaIdToWindowsId` needs ICU; no
  standard header or client hint carries time zone or calendar; GitHub's `Time-Zone` precedence
  (§5.1–5.2).
- `.resx` is the only framework-native resource format; satellite assemblies and
  `SatelliteResourceLanguages` (which trims referenced packages' satellites too);
  `AcceptLanguageHeaderRequestCultureProvider` tries at most three values; the middleware's
  default culture is static (§3.1–3.3).
- Rails/Django/HybridCache precedent for stored-version validation and Django's global key
  `VERSION`; SQL Server change tracking as an audit primitive and its restore hazard (§6.1–6.3).
- `rowversion` bumps on every update; a monotonic `bigint` can be re-reached after a restore;
  Guid v7 is not sequential in SQL Server byte order; temporal `UPDATE` writes a history row even
  when nothing changed; RCSI is on by default only on Azure SQL; JSON columns stay
  `nvarchar(max)` (briefing §8).
- Spec 0008 §3 (no collections, no computed properties, enum by store type), §10.3–10.6
  (`local`, calendar codes `'gc'` only in v1 with `'uq'`/`'et'` reserved, `today()` "the current
  date in the tenant's zone"), §13.1 (`QueryexParameterOrigin.TimeZone` = "the tenant's time
  zone, as the backend zone name"), §16 (caches keyed on schema identity), §17 (language
  version) — read 2026-09-04.
- Spec 0001 §4: `HasData` confined to a reserved band with the sequence's `StartsAt` above it —
  read 2026-09-04. `StringList.Id` is the standalone type's column name —
  `src/core/Tellma.Core.Abstractions/TableTypes/BulkLists.cs`, read 2026-09-04.
- Spec 0007 §1.6: `DeploymentIdentity(Application, EnvironmentName)` with `DeploymentId`; no
  build member exists — read 2026-09-04.
- `Microsoft.ICU.ICU4C.Runtime` latest 72.1.0.3 (2023-10-13) and `Accept-Language` not being a
  forbidden request header in the Fetch standard were verified by one proposal on 2026-09-01
  against nuget.org and fetch.spec.whatwg.org; not re-verified here.

Still unverified or assumed:

- That `GregorianCalendar` is in `OptionalCalendars` for **every** predefined culture the
  catalogue may offer (true for `ar-SA`, `ar-EG`, `ar-AE`, `am-ET`, `en-US` per the probe; the
  general claim is pinned by a unit test, and the negotiator leaves the culture's default calendar
  when it is not).
- Linux/ICU parity for Amharic/Oromo plural rules and `TimeZoneInfo` IANA conversion (the docs say
  yes; not probed).
- That enabling change tracking on the LocalDB fixture is cheap enough for the PR tier (T2 owns
  the fixture).
- EF Core 10 has no API for `HISTORY_RETENTION_PERIOD`; a retention policy on the settings history
  tables would be a `migrationBuilder.Sql` statement.
- Whether hosted MCP clients send `Accept-Language` at all (assumed not; the fallback chain
  handles either).
- That `IStringLocalizerFactory.Create(string baseName, string location)` remains the stable
  overload in `Microsoft.Extensions.Localization` 10 (long-standing public API; not re-verified).

---

## 9. Review flags

1. **`HybridCache` versus a private `MemoryCache` (D6).** The framework-blessed choice would
   arrive with stampede protection and an L2 story out of the box; the cost is serialization on
   every write, STJ-serializable DTOs only, sharing the DI cache, and folding the database tag into
   the key. The `VersionedCache` surface can be re-implemented over `HybridCache` later without
   touching consumers.
2. **Per-user `PermissionsTag` (D2).** Two-level validation adds the user-level bump rule to the
   emitter (distinct `UserId` values from written rows). The simpler alternative — tenant-level
   `permissions` only — makes every membership edit force every active user to recompute once.
3. **Format version as a constant plus a shape test (D5)** versus folding the deployment's build
   identity into every wire tag (zero discipline; one refetch of every client cache per deploy;
   amends `DeploymentIdentity`).
4. **Sealing `Settings` (D8).** A distribution wanting a required, typed, indexed tenant setting
   that other tables reference has no home but an entry or its own single-row table. If leaf
   extension is allowed, the platform DTO exposes extension columns only as a JSON bag.
5. **Calendars in Core versus Locale packs (D14).** Following ARCHITECTURE.md strictly means
   creating `Tellma.Locale.Sa` and `Tellma.Locale.Et` now and referencing them per distribution.
6. **Umm al-Qura beyond AH 1500 (D14):** tabular `HijriCalendar` approximation versus rejecting the
   date with a diagnostic.
7. **No tenant `DefaultCulture` column (D12).** A typed, required specific culture (`ar-SA`) would
   give non-browser clients predictable digits and separators; the chosen design relies on the
   user's regioned `ui.language` preference and defers a tenant default.
8. **No per-request time zone (D15).** `Tellma-Time-Zone` and `ui.timeZone` are cheap later
   additions; the context already has the slot.
9. **`Accept-Language` above the stored preference (D16).** The reverse protects a user whose
   browser sends an unexpected OS language to a non-SPA endpoint.
10. **Resources never fall back to the tenant's primary language (D18)** — a departure from the
    brain dump's chain; the tenant hop is kept for content only.
11. **`MaxAge` backstop on entity lists (D6)** introduces a second, time-based staleness bound next
    to the tag; dropping it keeps "the tag is the only channel" pure at the price of unbounded
    staleness after an out-of-band write.
12. **The write guard's residual window (D4)** is the transaction's duration; closing it needs
    shared locks on tag rows across every save (and deadlock analysis for batches that both depend
    on and bump one tag). Accepted as is.
13. **Explicit `feature.SettingKeys(typeof(…))` registration (D9)** versus manifest or reflection
    discovery — one line per pack until T1's manifest generator exists.
14. **`UserSettings` versus `UserPreferences` (D21)** — T4's call; this theme argues symmetry with
    `Settings`.

---

## 10. Conflicts for other themes to reconcile

1. **T2 (batch/executor):** the tag read must be the first statement of every batch and cold
   loads must follow it; the guard is the first statement inside the transaction and the bumps the
   last before `COMMIT`; the emitter must extract distinct user-id values from written rows for
   `[BumpsUserVersionTag]`; raw SQL must declare its written entity types; error 51001 in T2's
   band; `VersionTagList` joins the standalone types; the LocalDB fixture enables change tracking;
   the `uq` engine amendment needs a month-map table this theme will claim; `[Multilingual]` joins
   the entity contract; `datetime2(7)` audit columns and `ModifiedAt` as the concurrency stamp
   are assumed from T2's base.
2. **T4 (users/permissions):** the per-user non-temporal row must carry `PermissionsTag` and
   `UserSettingsTag` (names fixed here) and the connect statement must return them in the first
   batch; `Role`/`Permission` carry `[BumpsVersionTag("permissions")]`, `RoleMembership` and
   `UserSetting` carry `[BumpsUserVersionTag]`; the permissions cache validates against both
   levels with a `Rerun` dependency; the securables registry exposes `SupportsFilter` and accepts
   `settings.<category>` × `edit`; user settings are key-value with `SettingKey<T>` keys and the
   platform's `ui.*` keys; the deactivation guard sits next to the tag guard; the table is named
   `UserSettings`.
3. **T5 (pipeline):** preprocessing nulls gated twins; the pipeline declares
   `VersionTagDependency` per batch and re-runs once on `StaleVersionTagException`; `[Cacheable]`
   projects `GetAllCached`; the settings save is an ordinary emitter save with a field-masked
   patch on the wire; the round-trip ledger quoted here (1 read, 2 save, +1 cold) is the number
   T5 must meet.
4. **T6 (web):** headers `Tellma-Version-Tags`, `Content-Language`, `Tellma-Calendar` (both
   directions); no `X-Today`, no time-zone header in this release; routes `settings/client`
   (tag-aware "unchanged"), `settings/entity-tags`, `<entity>/all`, `settings/save`,
   `settings/refresh-caches`; `StaleVersionTagException` → 503, `TenantDatabaseMismatchException`
   → 500; validation messages rendered under the request culture through the ICU factory.
5. **T1 (host):** the request context carries `Settings` and `Localization`; the binder order in
   §3.7; job scopes copy a serialized `LocalizationContext`; the builder gains `Languages`,
   `AddLanguage`, `SettingKeys`, `AddCalendar`; the tenant-id type of `core.Settings.TenantId`
   follows T1; provisioning inserts the settings row after the system user it references and
   runs the migrator's tag seed; the startup gate refuses invariant/NLS globalization.
6. **T8 (reference stacks):** the settings edit endpoint and `refresh-caches` action per D11; the
   first admin bootstrap must precede the settings row; seed data through the pipeline bumps
   tags automatically, `HasData` seeds rely on the migrator's wholesale bump.
7. **T9 (Excel):** headers through `ILabelProvider`; column mapping through `MultilingualShape`;
   an import column mapped to a gated twin is `Import_LanguageNotConfigured`; date and number
   metadata through `ICalendarSystem`/`LocalizationContext` (Ethiopian dates cannot be expressed
   as Excel number formats).
8. **T10 (background):** job scopes receive the serialized `LocalizationContext` or tenant
   defaults; the migrator's wholesale bump and `settings/refresh-caches` are the only bumps
   outside the executor.
