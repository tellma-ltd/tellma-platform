# T3 — Tenant settings, localization, and the version cache (→ spec 0012)

Lens: distribution-author simplicity and AI-native authoring. The test applied to every decision
below: how many lines does a distribution (written by a coding agent) type to get the behaviour,
and is every fact declared exactly once. Where simplicity and another quality conflict, the
conflict is named and a review flag is raised.

Reader orientation: "the platform" is `Tellma.Core` + `Tellma.Core.Abstractions`; "a pack" is a
`Tellma.Module.*` / `Tellma.Industry.*` / `Tellma.Compliance.*` library that references
`Tellma.Core.Abstractions` only; "the distribution" is the composition root that references
everything. One application database per tenant; the database *is* the tenant.

---

## 1. Critique of the brain dump (Settings tables, Caching, Localization, and the tag/version parts of User)

### 1.1 The general shape is right; three things are missing

The three-part shape — a typed single-row settings table, a key-value table for ad-hoc settings,
and a version-tag-validated in-process cache read on every request — is the monolith's proven
shape (`dbo.Settings` with `SettingsVersion`/`DefinitionsVersion` `uniqueidentifier` columns,
`dbo.Users.UserSettingsVersion`/`PermissionsVersion`, and `VersionCache<TKey,TData>` with a
per-entry semaphore). Keeping it is correct. What the brain dump does not say, and a spec author
would have to invent:

1. **Who may add a setting, and how.** The typed table is described as "essential typed
   configuration", but a `Tellma.Module.Gl` pack cannot add a column to a Core-owned entity (a
   distribution leaf can inherit from exactly one base, and several packs cannot all be that
   base), so pack-declared settings *must* live in the key-value table. That fact decides the
   "typed vs KV" criterion by itself and the brain dump does not draw the conclusion.
2. **How the framework guarantees the tag bump.** The text asks "(how?)" and leaves it. The
   answer must be structural — a property of the batch executor, not a discipline — or the
   design is the monolith's `UPDATE dbo.Settings SET SettingsVersion = NEWID()` scattered through
   stored procedures, which is exactly the class of "awful stale-cache bugs" the text wants to
   prevent.
3. **What the client learns and when.** "The UI caches too" is stated, but nothing says how the
   browser discovers that a tag moved. Without a response-level channel the SPA either polls or
   stays stale until reload.

### 1.2 Detailed choices that need changing

- **`SettingsVersion` in the temporal `core.Settings` row.** The brain dump flags the churn
  itself. It is worse than churn: every tag bump would write a history row for the settings, so
  the settings audit trail fills with "nothing changed" rows and the concurrency stamp moves on
  bookkeeping. Tags must not live on any temporal row (nor on any row whose `ModifiedAt` is a
  concurrency stamp).
- **`TenantId` in the settings row.** Redundant as data (the database is the tenant) but valuable
  as a *guard*: the row is where a mis-pointed connection string, a backup restored into the wrong
  tenant's database, or a sandbox cloned from live without rewriting identity gets caught. Keep it,
  but as an assertion checked at first use, never as a filter.
- **`TenantConfigurationKV`** is a description, not a name; `Category` on it is a derivable
  column (the declaring key carries its category); `Value` "string or JSON" must be one thing (a
  JSON value; a bare string is a JSON string).
- **"The supplied culture headers are ignored if they are not one of the tenant's languages."**
  This conflates two axes the text elsewhere separates. The tenant's up-to-three languages are the
  *content* axis (which `Name` column holds which language). The request's culture is the *UI and
  message* axis (which resource satellite answers `IStringLocalizer`). An English-speaking auditor
  at an Amharic/Oromo tenant must get English messages while seeing Amharic content; nothing in the
  system breaks when the two differ. The restriction should be dropped.
- **"The server accepts standard culture headers and returns … numeric formats according to that
  culture."** Only inside messages and Excel metadata; the wire is invariant JSON. Worth saying,
  because `ar-SA` as `CurrentCulture` makes `DateTime.ToString()` print Umm al-Qura dates — a
  silent formatting bug waiting for the first Saudi tenant.
- **A custom calendar header "(does a standard one exist?)"** — no (verified in research §5.2), and
  BCP 47 `-u-ca-` extensions inside `Accept-Language` are a trap on .NET 10/ICU (research §3.4:
  mixed-calendar output, `Parent` collapses to the language). Calendar and time zone must be
  separate context values with their own headers.
- **"Settings … not subject to READ permissions"** is right for the client DTO and wrong for
  server-only entries (a ZATCA environment flag, a bank integration toggle). Visibility must be
  declared per key.
- **"Metaversion"** names the right thing (Django's cache `VERSION`) with a word nobody will
  search for. It is a *format version* of one cached shape, and it belongs on the wire (where
  caches outlive deployments), not in the database.
- **"Versions" naming.** HTTP owns "ETag" (an opaque validator of a *representation*, RFC 9110
  §8.8.3); "fingerprint" implies a content hash, which these are not; "version" alone collides
  with the Queryex *language version* and the app version. "Version tag" is unambiguous and the
  orchestrator already uses it.
- **User-level `UserSettingsVersion` and `PermissionsVersion`** are correct to keep separate (one
  is a security boundary, one is a convenience) but the *permissions* tag has an under-examined
  cost model: a role edit must bump the tag of every member of that role, which is a fan-out
  `UPDATE` the brain dump never mentions. A tenant-level `permissions` tag removes the fan-out at
  the price of one extra round trip per active user after a role edit (§2, D5).
- **Cacheable entities "(up to 50–100 records?)"** — countries alone are ~250; the cap must be an
  attribute parameter with a sane default (1,000 rows) and a hard process-wide byte budget, and
  the cache must be bypassed — not served — when the caller's read permission carries a filter,
  or the whole-table cache leaks rows past row-level security. The brain dump does not mention
  RLS and caching in the same breath; they must be.
- **Localization hierarchy "Every distro supports …"** is a truncated sentence; the intended rule
  (tenant languages ⊆ distribution languages ⊆ Core catalogue) is sound but needs the catalogue to
  be *data in code* (symbol, native name, direction), because satellite assemblies cannot be
  enumerated cheaply at runtime (research §3.2).
- **"An English string must always be supplied"** should be enforced by the neutral `.resx`
  *being* English (the framework's own fallback), not by a runtime check.

### 1.3 Internal inconsistencies

- Caching §: "Before executing any API call, the versions are read from the DB" versus Save flow
  step 1 "OnConnect … DB call #1" versus the orchestrator's connect-collapse hint. All three can be
  true only if the tag read is a *statement* the executor prepends to whatever the first batch is,
  and a write batch carries an in-database guard (D7). The text describes a separate call.
- Settings §: "any operation that updates the settings resets `SettingsVersion`" — but the brain
  dump's own save pipeline never mentions the reset, and nothing else does either. The bump has no
  owner.
- User §: the permissions tag "validated as soon as the user makes an API call" — but the flows read
  tags in DB call #1 *after* deciding which permissions to apply (the RLS filter is built in step 3
  from cached permissions). Either the permissions are re-read on every call (defeating the cache)
  or the request is optimistic and needs a re-run path. The design below makes the re-run explicit.
- Localization §: "The supplied culture headers are ignored if they are not one of the tenant's
  languages" versus the hierarchy where the *distro* subset (not the tenant) is what has resources.
  A tenant whose content language has no distro resources would get English regardless.

---

## 2. Decisions

Each decision states the decision, the rationale, rejected alternatives, a confidence, and a
review flag where an equally plausible alternative exists.

### D1 — Vocabulary: *version tag*, `VersionTag`, `core.VersionTags`; *format version* replaces "metaversion"

**Decision.** The opaque cache validator is a **version tag** (`Tellma.Core.Abstractions.Caching.VersionTag`,
a `readonly record struct` over a `Guid`); the table is `core.VersionTags`, the column is `Tag`;
the hardcoded per-shape constant is the **format version** (`int`), never stored, folded into the
wire representation `"{format}.{guid:N}"` (D8). "ETag" is reserved for the blob endpoint and HTTP
conditional requests (T7/T6). "Fingerprint" is not used.

**Rationale.** Precedent on both sides (research §6.1): Rails/Django/HybridCache say *version*;
HTTP owns *ETag*. "Version tag" survives next to Queryex's `LanguageVersion` and the application
version without a qualifier.

**Rejected.** `ETag` (collides with HTTP semantics on the same responses that carry these tags);
`Stamp` (already used for the concurrency stamp `ModifiedAt` in the orchestrator's hint).

**Confidence.** High.

### D2 — Two settings tables with one criterion: platform-owned typed row `core.Settings`; declared keys in `core.SettingEntries`

**Decision.**

- `core.Settings` — exactly one row (`Id = 1`), typed columns for the settings the **platform
  itself** reads on the hot path: tenant identity guard, tenant display name (multilingual), the
  content languages, the calendars, the tenant time zone. Closed to packs and distributions: the
  entity is `sealed` in `Tellma.Core.Abstractions`.
- `core.SettingEntries` — key/value rows for **every other setting**, declared in code by packs
  and distributions as `SettingKey<T>` fields (D4) with a default, a category, and a visibility;
  a row exists only when the value departs from the default.

The criterion is not "essential vs ad-hoc" but **"read by the platform to build the request
context" vs "read by a feature"**. The first set is fixed by the platform and validated by the
platform against its catalogues; the second set is open and validated against each key's declared
type.

**Why the typed row stays.** Languages, calendars and time zone are needed *before* any feature
code runs (to negotiate the request's culture, to pick the Queryex schema shape, to bind the
`TimeZone` slot). One indexed single-row read with fixed columns is the cheapest, most explicit
way to get them, and their validation (catalogue membership, distinctness, IANA id mapping) is
platform logic that a generic KV store cannot express.

**Why the typed row is closed.** Under leaf-only mapping a distribution *could* subclass the
settings entity and add columns, but then the platform's cached `TenantSettings` DTO would not
carry them, the settings page would need distribution code, and a pack could never do the same.
One mechanism for everyone who is not the platform — `SettingKey<T>` — is the simplicity lens's
answer: adding a setting is one static field, no migration, typed at every access site,
discoverable by the settings UI and by MCP without any further code.

**Rejected.** *Everything in KV* (languages/calendars/time zone would be re-validated by generic
code, and the request-context build would parse JSON on the hot path); *everything typed with a
distro-extended leaf* (packs excluded; each addition is a migration; settings page needs code);
*one settings table per pack* (N tables to read at connect, N DTOs, N caches).

**Confidence.** High on the split; medium on sealing the typed entity.

**Review flag.** A distribution that wants a *required, typed, indexed* tenant setting (say, a
legal-entity number that other tables reference) has no home under this rule other than a KV
entry or its own single-row table. If Ahmad wants leaf extension of `Settings` allowed, the price
is that the platform DTO exposes extension columns as a `JsonElement` bag ("extras") rather than
typed members.

### D3 — `Settings` and `SettingEntry` are ordinary audited, temporal entities saved through the bulk emitter

**Decision.** Both entities carry the four audit columns (`CreatedAt`, `CreatedById`,
`ModifiedAt`, `ModifiedById`), are system-versioned (`core.SettingsHistory`,
`core.SettingEntriesHistory`, period columns `ValidFrom`/`ValidTo` as EF shadow properties), and
opt into a UDTT (`[TableType]`). No special persistence path: the settings edit (T8) hydrates,
merges the patch, validates, and persists through the same emitter as any entity, which is what
makes the audit stamping, the `ModifiedAt` concurrency check, and the tag bump (D6) automatic.
`core.Settings` has no sequence: the single row is inserted by the migrator's seed at
provisioning with `Id = 1`; `core.SettingEntries` uses `sq_SettingEntries`.

**`TenantId` guard.** The row carries `TenantId` (type follows T1's tenant-id decision; `int`
below). It is compared with the resolved tenant id the first time a process opens the tenant's
database in a given connection string and again on every settings reload; a mismatch is a
fail-closed `TenantDatabaseMismatchException` (500-class, alerting), not a filter.

**Rationale.** "Settings are an entity with one row" is the smallest possible description a
spec author and a coding agent can hold; every capability (audit, temporal, concurrency,
tag-bump) is inherited rather than re-implemented.

**Rejected.** Non-temporal settings (loses "who changed the tenant's time zone and when", which
is the one question always asked after a settings incident); `CHECK` constraints for
cross-column rules beyond the trivial (the pipeline validates; the DB holds only the shape).

**Confidence.** High.

### D4 — `SettingKey<T>`: declared once, discovered, validated, localized, permissioned

**Decision.** A pack or distribution declares settings as static fields in a class marked
`[SettingKeys]`:

```csharp
[SettingKeys]
public static class GlSettings
{
    /// <summary>Whether posting a journal voucher also auto-numbers it.</summary>
    public static readonly SettingKey<bool> AutoNumberOnPost = new("gl.posting.autoNumberOnPost", defaultValue: true);

    /// <summary>The default center for postings that specify none. Null means "no default".</summary>
    public static readonly SettingKey<int?> DefaultCenterId = new("gl.posting.defaultCenterId", defaultValue: null);
}
```

That is the whole distribution-side cost of a tenant setting. The platform:

- **Discovers** `[SettingKeys]` classes through the feature manifest (T1's composition seam; the
  development fallback is assembly reflection over the composed feature assemblies).
- **Validates at startup** (aggregated into the single composition diagnostic): key grammar
  `^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)+$` (camelCase segments, at least two), uniqueness
  across the composition, and that `T` is JSON-representable with the platform's
  source-generated context (`bool`, `int`, `long`, `decimal`, `string`, `DateOnly`, `TimeOnly`,
  enums as strings, arrays of those, and any `[SettingValue]`-annotated record).
- **Reads** through `TenantSettings.Get(key)` (D9's cached DTO) with the declared default when no
  row exists; a stored value that fails to parse as `T` is treated as the default and logged +
  metered (`tellma.settings.entries.invalid`) — a bad row must not take the tenant down.
- **Labels** the key for UI and Excel by resource lookup `Setting_<key with '.' → '_'>` in the
  declaring assembly's `Strings` (D16), falling back to the humanized last segment
  ("Auto Number On Post").
- **Permissions** the key by its `Category` (explicit, default = first segment): the securables
  registry (T4) registers one securable `settings.<category>` × action `edit` per distinct
  category at startup, so the settings page's per-category permission the brain dump asks for
  exists with zero distribution code.
- **Visibility**: `SettingVisibility.Server` (default) never leaves the server;
  `SettingVisibility.Client` is included in `TenantSettingsForClient` (D9) for every
  authenticated member.
- **Scope**: `SettingScope.Tenant` (default, stored in `core.SettingEntries`) or
  `SettingScope.User` (stored in T4's per-user preference table and read through
  `IRequestContext.Preferences`). The same declaration mechanism, the same registry, the same
  labels — one concept for "a declared, defaulted, typed value".

**Rejected.** Settings registered by fluent calls in `AddTellma` (a second place to list what the
static field already says); attribute-only declaration on a POCO with properties (loses the
typed access site `settings.Get(GlSettings.AutoNumberOnPost)` that makes a coding agent's usage
mechanical); a `Category` column in the table (derivable from the declaration; no query needs
it).

**Confidence.** High for tenant scope; medium for unifying user preferences under the same type
(T4 owns that table).

**Review flag.** `SettingScope.User` reuses `SettingKey<T>` for user preferences; T4 may prefer
its own preference shape (pinned screens, grid widths are UI-owned blobs). The contract still
works if T4 keeps a free-form JSON bag for UI state and uses `SettingKey<T>` only for the
platform-known preferences (`ui.language`, `ui.calendar`, `ui.timeZone`).

### D5 — Which tags exist and where they live

**Decision.**

| Tag name | Level | Lives in | Bumped by writes to | Read by |
|---|---|---|---|---|
| `settings` | tenant | `core.VersionTags` | `core.Settings`, `core.SettingEntries` | settings cache (server DTO + client DTO), Queryex schema shape selection, request-context negotiation |
| `permissions` | tenant | `core.VersionTags` | `core.Roles`, `core.Permissions`, `core.RoleMemberships` (T4 declares) | permissions cache (T4) |
| `entities` | tenant | `core.VersionTags` | any `[Cacheable]` entity's table | client: "some cached list changed, fetch the per-entity tag list" |
| `entity:<EntityName>` | tenant | `core.VersionTags` | that entity's table | server cacheable-entity cache; client per-entity list cache |
| `preferences` | user | `core.UserStates.PreferencesTag` (T4's non-temporal sibling of `core.Users`; T4 names the table) | that user's preference rows | preferences cache (T4), client preference cache |

Tags are `uniqueidentifier`, **generated by the application** (`Guid.NewGuid()`), compared only
for equality, never indexed, never ordered. There is **no per-user permissions tag**: a role,
permission, or membership change bumps the tenant-level `permissions` tag and every active user's
permissions are recomputed lazily on their next request (one extra round trip each, once).

**Rationale.** A non-temporal, audit-free, tiny table keyed by name is the cheapest thing to read
on every request (one clustered scan of ≤ a few dozen rows) and the only place a tag can move
without a history row or a concurrency-stamp change. Application-generated Guids are restore-safe
(a restored backup cannot re-reach a value a browser cached) and let the writing instance stamp its
own cache without a read-back. Dropping the per-user permissions tag removes the fan-out
`UPDATE core.UserStates … WHERE UserId IN (members of role X)` and the per-user bump logic; the
cost is bounded and rare.

**Rejected.** `rowversion` (moves on any update including bookkeeping; research §4.1); monotonic
`bigint` (restore hazard; ordering not needed; research §4.3); tags on the temporal rows (D2
critique); SQL Server change tracking as the primary tag (database-wide, per-table enablement,
retention; research §6.3 — useful only as an audit of the bump discipline).

**Confidence.** High on the table and the Guid; medium on "no per-user permissions tag".

**Review flag.** T4 may want the per-user `PermissionsTag` back for large tenants (thousands of
active users, frequent membership edits). The infrastructure below supports both: a user-level
tag is just another column the connect prelude returns and the permissions cache key includes.

### D6 — The bump is a property of the batch executor: declared written tables → appended bump statement

**Decision.** Every write statement in a batch declares the tables it writes (the save emitter
does so automatically from the entity types it persists; a raw SQL write must declare
`WritesTo(typeof(TEntity), …)` or the builder refuses it — T2 seam). A batch finalizer
registered by the platform (`VersionTagBumpFinalizer : IDbBatchFinalizer`) computes the union of
tag names declared for those tables and appends **one** bump statement, parameterized with one new
Guid for all bumped tags:

```sql
-- Appended by VersionTagBumpFinalizer inside the batch's transaction. @tm_tag: uniqueidentifier,
-- @tm_tagNames: [dbo].[StringList] (spec 0001's standalone type). Self-healing: a name with no
-- row yet is inserted, so a newly declared [Cacheable] entity needs no migration step to work.
UPDATE [core].[VersionTags]
   SET [Tag] = @tm_tag, [BumpedAt] = SYSUTCDATETIME()
 WHERE [Name] IN (SELECT [Id] FROM @tm_tagNames);

INSERT INTO [core].[VersionTags] ([Name], [Tag], [BumpedAt])
SELECT n.[Id], @tm_tag, SYSUTCDATETIME()
  FROM @tm_tagNames AS n
 WHERE NOT EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WHERE t.[Name] = n.[Id]);
```

Tag names come from attributes on entity classes, in `Tellma.Core.Abstractions`:

- `[BumpsVersionTag("settings")]` — explicit; multiple allowed; used by the platform on
  `Settings`/`SettingEntry` and by T4 on `Role`/`Permission`/`RoleMembership`.
- `[Cacheable]` — implies `entity:<EntityName>` **and** `entities`.

The platform's own entities (`Settings`, `SettingEntry`) carry `[BumpsVersionTag("settings")]`
in their class declarations, so a distribution never writes a bump. The migrator's runtime seed
inserts a row for every tag name the model declares (`INSERT … WHERE NOT EXISTS` under the
migrator's app lock), so the self-healing branch above is a fallback, not the normal path.

**What this guarantees.** Every write that goes through the platform's data layer bumps the right
tags, in the same transaction as the write, with no per-service code. What it cannot guarantee is
a write that bypasses the data layer (a DBA script, a distribution's `SqlConnection` used
directly). Those are documented: `UPDATE core.VersionTags SET Tag = NEWID()` after any manual
change, plus the admin action "refresh caches" (T8's settings API) that bumps every tag.

**Rejected.** Bumps written by each service (the monolith's failure mode); triggers (no logic in
the database; `OUTPUT` restrictions on tables with triggers; research digest); change tracking
as the mechanism (§D5).

**Confidence.** High.

### D7 — Connect-time reads and the optimistic protocol: the tags prelude, the write guard, one bounded re-run

**Decision.**

1. **Every batch the platform executes begins with the tags prelude** — the executor prepends it
   to the first batch of a request or job scope (T2's executor exposes the result as
   `DbBatchResult.VersionTags`):

   ```sql
   SELECT [Name], [Tag] FROM [core].[VersionTags];
   ```

   T4's connect statement (subject → user, `LastActive`, `core.UserStates.PreferencesTag`) rides the
   same prelude. A request that needs no other statement (e.g. `settings/client` served from
   cache) still executes the prelude alone: one tiny round trip is the price of cross-instance
   freshness without Redis or a bus (research §6.4).

2. **The snapshot.** The prelude's rows replace the process's per-tenant
   `VersionTagSnapshot` (a `FrozenDictionary<string, VersionTag>` plus `ReadAtUtc`). All caches
   compare their entry's tag with the snapshot; the snapshot is the *only* invalidation channel.

3. **Reads are optimistic.** A read that composes cached inputs (RLS filter from the permissions
   cache, schema shape from the settings cache) uses the snapshot as it was at request start,
   executes, then compares the batch's returned tags with the tags its inputs were stamped with.
   A mismatch on a tag the request *depended on* (declared to the executor as
   `CacheDependency(TagName, ExpectedTag)`) discards the result, refreshes the caches, and re-runs
   **once**; a second mismatch surfaces as `StaleVersionTagException` (503-class, retryable) and
   is metered. Rows never reach the caller from a run whose dependencies moved.

4. **Writes are guarded in the database.** A batch that writes and that composed cached inputs
   carries a guard statement *before* `BEGIN TRAN`, parameterized with the
   `[dbo].[VersionTagList]` standalone type (`Name nvarchar(128)`, `Tag uniqueidentifier`; new
   plain class in `Tellma.Core.Abstractions.TableTypes`, registered like `IdList`):

   ```sql
   IF EXISTS (SELECT 1
                FROM [core].[VersionTags] AS t
                JOIN @tm_expectedTags AS e ON e.[Name] = t.[Name]
               WHERE t.[Tag] <> e.[Tag])
       THROW 51001, N'VersionTagMismatch', 1;
   ```

   Error 51001 maps to `StaleVersionTagException`; the pipeline refreshes and re-runs once, exactly
   as for reads. Under `SET XACT_ABORT ON` nothing after the guard executes; with the guard placed
   ahead of the transaction, nothing is left to roll back. Platform batch error numbers occupy
   51000–51099 (T2 owns the band; 51001 is claimed here).

5. **The response channel.** Every authenticated response carries the post-batch snapshot's
   tenant tags and the user's tags in one header (T6 owns the exact wire form; the semantic is
   this theme's): `Tellma-Version-Tags: settings=1.<guid>, permissions=1.<guid>,
   preferences=1.<guid>, entities=1.<guid>`. The SPA compares with what it holds and refetches
   the changed DTOs. `entity:*` tags are deliberately *not* in the header (unbounded count); the
   composite `entities` tag is, and a changed composite makes the client fetch the per-entity tag
   list (`settings/entity-tags`, D10).

**Failure modes named.** (a) *Deactivated user*: T4's connect statement returns the user's
`IsActive` in the prelude; the pipeline refuses before any business statement runs (the prelude is
the first statement of the same command, but the command's remaining statements have already
executed by the time the app reads the first result set — so the refusal is a discard, and for
writes the guard is the deactivation `IF` T4 emits alongside the tag guard). (b) *Stale
permissions*: covered by the guard (writes) and the re-run (reads). (c) *RLS pre-check
ordering*: the RLS pre-check for updates (T5) is a read in the validation round trip, which
already carries the prelude and a `permissions` dependency; the persist batch re-checks via the
guard. (d) *Two instances bumping the same tag concurrently*: last writer wins; each instance's
next prelude reconciles; no lost invalidation is possible because a bump always produces a value
that differs from every cached one.

**Rejected.** A separate connect round trip before every request (the brain dump's DB call #1 —
one round trip for a read becomes two); TTL-based trust of the snapshot to skip the prelude on
"pure cache" requests (adds a freshness knob and an inconsistency window that Lens C would reject
and that the simplicity lens does not need); T-SQL `RETURN` guards on read batches (a discarded
result set costs the same as a query never run, minus complexity).

**Confidence.** High on the prelude and the guard; medium on the header name.

### D8 — Format version: a `const int` per cached shape, folded into the wire tag

**Decision.** Each cached DTO declares `public const int FormatVersion` (initially 1):
`TenantSettingsForClient.FormatVersion`, T4's `UserPermissionsForClient.FormatVersion`,
`UserPreferencesForClient.FormatVersion`, and the cacheable-entity list shape's constant on the
platform side. The wire form of a tag is `"{FormatVersion}.{Guid:N}"`; the client stores DTOs keyed
by that string and refetches when it changes for any reason. The database never stores the format
version; the in-process cache dies with the process so it needs none.

**Rationale.** Research §6.2: this is Django's `VERSION`, and its only real job is guarding caches
that outlive deployments — the SPA's service-worker-persisted DTOs and any future L2. A
deployment that changes the shape of `TenantSettingsForClient` bumps one constant and every browser
refetches after its first response.

**Rejected.** A single global metaversion (bumps every cache for any shape change); storing it in
`core.VersionTags` (a deployment fact is not tenant data); naming it "metaversion".

**Confidence.** High.

### D9 — Cache infrastructure: private bounded `MemoryCache` per cache kind, single-flight loads, meters; `HybridCache` rejected

**Decision.** `Tellma.Core.Caching.VersionedCache<TKey, TValue>` is the platform's one cache
primitive: an abstract base over a **private** `MemoryCache` instance (never the DI `IMemoryCache`),
`SizeLimit` in the kind's own units, `TrackStatistics = true`, entries holding
`(VersionTag Tag, TValue Value)`, a `ConcurrentDictionary<TKey, Lazy<Task<Entry>>>` single-flight
guard removed on completion, and `IMeterFactory` instruments (D21). `GetAsync(key, currentTag)`
returns the entry when `entry.Tag == currentTag`, otherwise loads through the derived class's
`LoadAsync(key)` — which performs its own round trip and returns the value **together with the tag
it read in the same batch** — and stores it. The four platform cache kinds and their keys:

| Kind | Key | Value | Size unit / default limit |
|---|---|---|---|
| `settings` | tenant id | `TenantSettings` + `TenantSettingsForClient` (one entry) | entries / 10,000 |
| `permissions` (T4) | (tenant id, user id) | compiled permission set | entries / 50,000 |
| `preferences` (T4) | (tenant id, user id) | preference bag | entries / 50,000 |
| `entities` | (tenant id, entity name) | `IReadOnlyList<TEntity>` | rows / 2,000,000 |

Plus `tags` (the snapshot store, keyed by tenant id, entries / 10,000). All limits come from
`TellmaCacheOptions` bound to `Tellma:Cache`; a distribution changes a number in `appsettings`,
never code.

**The settings read path.** `TenantSettingsCache.GetAsync(tenantId, snapshot["settings"])` →
on miss, one batch: `SELECT … FROM core.Settings WHERE Id = 1; SELECT Key, Value FROM
core.SettingEntries;` plus the prelude; the loader resolves languages against the catalogue,
calendars against the calendar catalogue, the IANA zone to a `TimeZoneInfo` and its Windows id
(for Queryex's `TimeZone` slot), parses entries against the key registry, builds both DTOs, and
stamps the entry with the `settings` tag the same batch returned.

**Rationale.** Research §1: HybridCache serializes every write even L1-only and deserializes
mutable values on every read, shares the DI `IMemoryCache` (so a `SizeLimit` endangers MVC and
every third-party consumer), and its tag stamps are process-local — a DB-read tag cannot feed its
validity check. The requirements here (bounded per kind, stampede-safe, tag compared on every
request, metered) are ~150 lines over `MemoryCache`, most of it the monolith's `VersionCache`
with bounds and meters added. `TrackStatistics` + `GetCurrentStatistics()` (verified on .NET 10,
§8) give entry counts and estimated size for gauges without bookkeeping.

**Rejected.** `HybridCache` (above); one shared `MemoryCache` for all kinds (one limit for
unrelated shapes); `ConcurrentDictionary` without bounds (the monolith's shape; an attacker or a
long-lived process grows it without limit).

**Confidence.** Medium-high.

**Review flag.** HybridCache remains defensible for stampede protection out of the box and a
future L2; the cost is serialization on every write and DTOs that must be STJ-serializable
(entity classes with navigations are not). If Ahmad prefers fewer platform lines over these
constraints, the `VersionedCache` surface below can be re-implemented over HybridCache by folding
the tag into the key (`settings:{tenant}:{tag}`) and relying on TTL for eviction.

### D10 — Cacheable entities: `[Cacheable(MaxRows = …)]`, whole-table lists, unfiltered-read rule, two-level tags

**Decision.** A distribution marks an entity cacheable with one attribute:

```csharp
[Cacheable]                       // default MaxRows = 1,000
public sealed class Country : Entity { … }

[Cacheable(MaxRows = 5_000)]
public sealed class PostalCode : Entity { … }
```

What it produces, with no further code:

- **Server cache** (`ICacheableEntities.GetAllAsync<TEntity>()`): the whole table, ordered by key,
  loaded by one Queryex query with no filter, stamped with `entity:<Name>`; served only when the
  caller's effective read permission on the entity is **unfiltered** (T4's evaluation result says
  so); otherwise the caller's operation runs an ordinary filtered query and the cache is bypassed
  and metered (`cache.outcome = bypass`). The whole-table list never crosses a row-level-security
  boundary.
- **Limits.** A load that returns more than `MaxRows` rows, or whose estimated size exceeds
  `TellmaCacheOptions.EntityMaxBytes` (default 4 MB), is **not cached**: the caller gets the rows
  (this once), the event is logged with the entity name and metered, and the entry is marked
  "oversized" until the next tag change so the attempt is not repeated on every request. The
  attribute is validated at startup only for shape (a key column, no `[Multilingual]` gating
  issues); the size is a runtime fact.
- **Tags.** Writes bump `entity:<Name>` and the composite `entities` (D6).
- **Client** (T6 projects the routes): `POST {tenant}/api/web/<entity>/all` returns
  `{ tag: "1.<guid>", rows: [...] }` from the server cache; `POST {tenant}/api/web/settings/entity-tags`
  returns `{ "Country": "1.<guid>", … }` for every cacheable entity; the SPA fetches the latter when
  the `entities` tag in the response header changes and refetches only the lists whose tag moved.
- **What qualifies (guidance the attribute's XML docs carry):** small, read-mostly reference data
  read by pickers on most screens — countries, currencies, units, calendars' month names are
  *not* (they are resources), users are *not* (per-tenant thousands, and names are in the
  related-entity dictionary anyway).

**Rationale.** One attribute, one tag name convention, one endpoint pair; the RLS rule is the
only non-obvious part and it is enforced by the platform, not remembered by the distribution.

**Rejected.** Caching query *results* keyed by query text (unbounded key space; no tag can cover
it); a per-entity tag list in every response header (unbounded); a client-driven "give me everything
whose tag changed" endpoint (the two-call shape is simpler and the second call is rare).

**Confidence.** High.

### D11 — Two axes: content languages (tenant, ≤ 3) and the request culture (UI + message + formatting); the request culture is not restricted to the tenant's content languages

**Decision.**

- **Content languages** are `core.Settings.PrimaryLanguage` (required),
  `SecondaryLanguage`, `TernaryLanguage` (optional, ternary requires secondary, all distinct):
  BCP 47 language tags validated against the *distribution's declared languages* (D12). They
  decide which `Name`/`Name2`/`Name3` column holds which language and nothing else.
- **The request culture** (`IRequestContext.Culture`, one `CultureInfo` used as both
  `CurrentCulture` and `CurrentUICulture`) is negotiated per request (D15) from any language the
  distribution ships resources for. It decides resource lookup and number/date formatting *inside
  messages and Excel metadata*. It need not be a content language.
- **`IRequestContext.ContentLanguageIndex`** (1..3) is *derived*: the index of the tenant content
  language whose language subtag equals the request culture's language subtag, else 1. Servers use
  it wherever they must pick "the" name of an entity (error messages naming a record, Excel
  export's default name column). No stored preference is needed for it.
- **Formatting culture = request culture.** No separate formatting-culture preference in this
  release.

**Rationale.** The orchestrator's hint is right: the axes are different things. The derivation
rule removes a preference (and a column) while producing the answer users expect: an Arabic UI at
an Arabic/English tenant shows Arabic names first; an English UI at that tenant shows English
names first; an English UI at an Amharic-only tenant shows Amharic names (there is nothing else)
with English chrome.

**Rejected.** Ignoring `Accept-Language` outside the tenant's languages (the brain dump); a stored
content-language preference (one more setting, same answer 99% of the time); a separate formatting
culture (real need — Latin digits with Arabic UI — but the SPA formats client-side anyway and the
server's formatting surface is small; flagged).

**Confidence.** High on the axes; medium on "no formatting culture".

**Review flag.** A user-level `ui.formatCulture` preference (any CLDR culture, digits/separators
only) is a cheap later addition; the request context already has the slot (`Culture` vs
`UiCulture`) if Ahmad wants it from day one.

### D12 — The Core language catalogue, the distribution's declaration, and the tenant's validation

**Decision.** `Tellma.Core.Abstractions.Localization.LanguageCatalog` is **data in code**: a
frozen list of `LanguageInfo(Code, EnglishName, NativeName, Symbol, IsRightToLeft)` for every
language the platform knows how to *describe* (initially `en`, `ar`, `am`, `om`, `zh`, `fr`,
`es`, `tr`, `ur`, `hi`, `sw`, `ti`, `so` … — the monolith's five plus obvious neighbours; the
list is additive and adding an entry is a one-line PR). `Symbol` is the short native-script mark
used in labels (`en` → `E`, `ar` → `ع`, `am` → `አ`, `om` → `Om`, `zh` → `中`, `fr` → `F`,
`es` → `Es`, …); a unit test asserts symbols are unique across the catalogue.

The distribution declares its languages once, in `AddTellma`:

```csharp
services.AddTellma(tellma =>
{
    tellma.Languages("en", "ar");          // the offered UI/message languages, in display order
    // …
});
```

Rules, validated at startup into the one composition diagnostic:

- Every declared code exists in the catalogue (or was added with
  `tellma.AddLanguage(new LanguageInfo(...))`, the escape hatch).
- The first declared language is the distribution default (used when negotiation fails and the
  tenant's primary is not offered).
- A pack may declare `SupportedLanguages` in its feature declaration; a distribution language
  outside a referenced pack's set is a **warning** (strings fall back to English), never an error.

Tenant validation (settings save, T8): the three content languages ∈ the distribution's declared
languages; distinct; ternary ⇒ secondary. Calendars: primary ∈ calendar catalogue, secondary ≠
primary. Time zone: `TimeZoneInfo.TryFindSystemTimeZoneById` succeeds *and*
`TryConvertIanaIdToWindowsId` succeeds (the Windows id is what SQL Server's `AT TIME ZONE` and
Queryex's `TimeZone` slot need); the stored value is normalized to `TimeZoneInfo.Id`.

**Rationale.** Satellite assemblies cannot be enumerated at runtime (research §3.2), so a declared
list is needed anyway; putting the descriptive data (symbol, direction, native name) in Core means
neither the distribution nor the client's locale pack re-declares it — the client DTO carries it.
The `SatelliteResourceLanguages` MSBuild property can trim shipped satellites to the declared set
(a build-side optimization T1's template can set; not required for correctness).

**Rejected.** Deriving the offered set from satellite assemblies on disk; per-tenant symbol
overrides (the monolith's `PrimaryLanguageSymbol` columns — three columns for a cosmetic; the
catalogue symbol suffices and a collision within one tenant's three languages is impossible by the
uniqueness test).

**Confidence.** High.

### D13 — Calendars ship in Core: `gc`, `uq`, `et`; tenant primary/secondary; user preference and header

**Decision.** `Tellma.Core.Abstractions.Calendars.CalendarCatalog` declares
`CalendarInfo(Code, EnglishName, NativeNameKey, MonthCount, MinDate, MaxDate)` for `gc`
(Gregorian, BCL `GregorianCalendar`), `uq` (Umm al-Qura, BCL `UmAlQuraCalendar`, AD 1900-04-30 …
2077-11-16 = AH 1318 … 1500), and `et` (Ethiopian, `Tellma.Core.Calendars.EthiopianCalendar`
ported from the monolith's `Tellma.Utilities.Calendars`, 13 months, JDN-based, AD 1752-09-14 …
2500-01-01). The codes are the same strings spec 0008 §10.4 reserves for Queryex's `cal`
argument. Tenant settings carry `PrimaryCalendar` (required, default `gc`) and
`SecondaryCalendar` (optional); a user may prefer either (`ui.calendar`, a `SettingScope.User`
key) and a request may override with the `Calendar` header (D15). A calendar outside the tenant's
pair is rejected with a validation error (`Calendar_NotOffered`).

**Calendar-aware formatting** is one platform component, `ICalendarFormatter`, used by messages
(through `MessageFormat`'s `CustomValueFormatter` hook, D16), by Excel column metadata (T9), and
by any pack that renders a date to text. It never assigns a custom calendar to
`DateTimeFormatInfo` (impossible, research §4.4): it computes `(year, month, day)` through the
`Calendar` object, takes month names from Core's resources for `uq`/`et` (`Calendar_uq_Month1`
… `Calendar_et_Month13`) and from the culture's `DateTimeFormat` for `gc`, and renders one of four
named styles (`short` `d/M/yyyy` in the culture's separator and order; `medium` `d MMM yyyy`;
`long` `d MMMM yyyy`; `full` with weekday). Digits are always ASCII. Dates beyond a calendar's
range (an Umm al-Qura date after AH 1500) render in `gc` with a diagnostic marker rather than
throwing.

**Rationale.** A calendar a tenant can *select* must exist in every distribution, or a tenant
setting becomes invalid by removing a package; the three calendars are a few hundred lines and
carry no dependencies. This is a departure from ARCHITECTURE.md's "Locale packs implement
`ICalendar`" (§7).

**Rejected.** Ethiopian calendar via a hobby NuGet package (research §4.4: unclear licensing,
single author); `-u-ca-` culture names (research §3.4).

**Confidence.** High.

### D14 — Time zones: the tenant zone binds `today()`/`TimeZone`; the request zone is presentation-only; no `X-Today`

**Decision.** `core.Settings.TimeZone` (IANA id) is the **business** zone: the host binds
Queryex's `Today` slot to the current date in that zone and the `TimeZone` slot to that zone's
Windows id (spec 0008 §10.3/§10.6 say "the tenant's zone"; nothing changes). The request's
`IRequestContext.TimeZone` (`Time-Zone` header > `ui.timeZone` preference > tenant zone) is used
only to *present* instants — a `DateTimeOffset` in a message or an Excel cell. There is no
`X-Today` header: the server's clock is authoritative and the tenant zone is known.

**Rationale.** "Today" in an ERP is the tenant's business date; a user in another zone filtering
`PostingDate = today()` expects the same rows as their colleagues. Presenting an instant in the
viewer's zone is a different, harmless choice. Dropping `X-Today` removes a client-controlled
input to security-relevant filters.

**Rejected.** Binding `today()` per user (row sets differ by viewer; a stored report definition
means different things to different people); `X-Today` (the brain dump's option).

**Confidence.** High.

**Review flag.** The orchestrator's T6 text says "the culture, calendar, today and time-zone
headers and how `today()` is bound"; this decision removes "today" as a header. T6 should confirm
the SPA can live with server-side "today" (it can: the UI's date pickers default from the server's
`today` returned in the settings DTO's `TodayInTenantZone`? — no: it is per request; the client
computes it from the tenant zone, which it has).

### D15 — Negotiation: header > preference > tenant > distribution default; extensions stripped; a Gregorian-forced culture clone; no `RequestLocalizationMiddleware`

**Decision.** The request-context binder (T1's endpoint filter; T6's `[AsParameters]`/`BindAsync`
type) calls `ILocalizationNegotiator.Negotiate(NegotiationInput)` once per request:

| Value | 1st | 2nd | 3rd | 4th |
|---|---|---|---|---|
| Culture | `Accept-Language` (q-ordered, first ≤ 3 entries; `-u-…`/`-x-…` extensions stripped; matched by language subtag with parent fallback against the distribution's offered set) | user preference `ui.language` | tenant `PrimaryLanguage` if offered | distribution default |
| Calendar | `Calendar` header (code) | `ui.calendar` | tenant `PrimaryCalendar` | — |
| Time zone | `Time-Zone` header (IANA id) | `ui.timeZone` | tenant `TimeZone` | — |

A header value that is present but invalid (unknown calendar code, unknown zone, unparseable
language range) is **ignored** with a `tellma.localization.headers.rejected` count, never a 400:
a stale browser must not lock a user out. The negotiated `CultureInfo` is a clone of the matched
predefined culture (region kept when the header carried one, e.g. `ar-SA`) with
`DateTimeFormat.Calendar` set to `GregorianCalendar` — so any incidental `ToString()` prints
Gregorian, and calendar rendering is always explicit through `ICalendarFormatter`. The binder sets
`CultureInfo.CurrentCulture`/`CurrentUICulture` for the request (they flow with the execution
context) and stores the triple in the scoped `IRequestContextAccessor`; background jobs get the
same triple copied into the job scope at enqueue time (seam 9). `RequestLocalizationMiddleware`
is not used: its default culture is static and ours is per tenant.

**Rationale.** Mirrors GitHub's documented precedence for `Time-Zone` (research §5.2); one
function, three values, no framework middleware to configure per distribution. The
Gregorian-forced clone closes the `ar-SA` Hijri footgun structurally.

**Rejected.** `RequestLocalizationMiddleware` with a custom provider (static default; extension
handling; still needs our binder for calendar/zone); `-u-ca-` in `Accept-Language`; hard 400 on
bad headers.

**Confidence.** High. (Whether every predefined culture accepts `GregorianCalendar` in
`OptionalCalendars` is pinned by a test over the catalogue's languages × common regions; §8.)

### D16 — Resources: one `Strings.resx` per assembly, English neutral; an ICU `IStringLocalizerFactory` decorator; a shared `MessageFormatter`; calendar-aware dates through `CustomValueFormatter`

**Decision.**

- **Files.** Each assembly that has user-facing text ships `Localization/Strings.resx` (neutral =
  English) with satellites `Strings.<lang>.resx`, and a marker class `Strings` in its root
  namespace (`Tellma.Core.Localization.Strings`, `Tellma.Module.Gl.Strings`,
  `Tellma.Distro.Etpharma.Strings`). Keys are `PascalCase_Underscored`: entity/property labels
  `Center_Name`, settings `Setting_gl_posting_autoNumberOnPost`, messages `Center_CycleDetected`,
  calendar month names `Calendar_et_Month1`. Since `Tellma.Core`'s resources cannot be referenced
  by packs (no `Core` reference), Abstractions exposes the *address* of Core's strings
  (`CoreStrings.BaseName`/`CoreStrings.Assembly`) and packs address them through
  `IStringLocalizerFactory.Create(baseName, location)` — the framework's own API — never a type.
- **ICU everywhere.** `AddTellma` registers `IcuStringLocalizerFactory`, a decorator over the
  framework's `ResourceManagerStringLocalizerFactory`, so *every* `IStringLocalizer<T>` in the
  process renders through ICU MessageFormat (plural, select, selectordinal, nesting), with named
  arguments only. Values without `{` skip the parser. The identity server's per-type registration
  is not repeated: the factory decoration covers Core, packs, and the distribution with zero
  registration code.
- **One shared `MessageFormatter`** (`useCache: true`), culture passed per call — the pattern cache
  is a lock-free `ConcurrentDictionary` and a 16-thread/50k-call probe showed no errors (research
  §2.4); the pluralizer dictionaries are configured before first use and never mutated after.
  Patterns come from resources only (an unbounded cache over user-supplied patterns would be a
  memory hazard; user text is never formatted as a pattern).
- **Calendar-aware dates.** The formatter is constructed with a `CustomValueFormatter` whose
  `TryFormatDate`/`TryFormatTime` delegate to `ICalendarFormatter` using the *current request
  context's* calendar and time zone (`DateOnly`, `DateTime` (assumed tenant-local), and
  `DateTimeOffset` (converted to the presentation zone) are accepted), and whose `TryFormatNumber`
  keeps the built-in behaviour. `{d, date, short|medium|long|full}` therefore work in every
  calendar; `medium`/`long` — unsupported by the library — are supplied by the hook.
- **Conventions for authors** (in the XML docs of `Strings` markers and the distribution
  template): `{count, plural, …}` for counts; `{gender, select, female {…} male {…} other {…}}`
  with `other` mandatory; no positional `{0}`.
- **A malformed pattern** renders the raw value and logs once per key (never fails the request).

**Rationale.** `.resx` keeps the framework's fallback chain, satellite packaging, and tooling with
zero dependencies (research §3.1); the decorator is the one place ICU is wired.

**Rejected.** JSON resources via a third-party localizer (loses satellite trimming; one more
dependency); the identity server's `ThreadLocal<MessageFormatter>` (unnecessary per probe);
positional arguments (unreadable in translations).

**Confidence.** High.

### D17 — The fallback chain: request culture → its parents → tenant primary → neutral (English); missing strings are metered, never blank

**Decision.** `IcuStringLocalizerFactory`'s localizer looks a key up in the request's UI culture
(the framework walks `ar-SA → ar → neutral` itself); when the framework reports
`ResourceNotFound` for the culture *chain*, the platform retries once under the tenant's primary
content language (if it differs and is offered), then accepts the neutral (English) value. A key
absent everywhere returns the key text and counts `tellma.localization.missing` (tag: `culture`
from the offered set). The neutral `.resx` is English by construction, which is how "an English
string must always be supplied" is enforced: it is the file the build fails without.

**Rationale.** The brain dump's chain, implemented where the framework already does most of it.

**Confidence.** High.

### D18 — Labels and the `Name (E)` / `Name (ع)` convention: `[Multilingual]` on the primary, twins by name, labels resolved by convention

**Decision.** A multilingual text group is declared by one attribute on its primary property;
the twins are found by name and must be nullable strings of the same max length:

```csharp
[Required, MaxLength(255), Multilingual]
public string Name { get; set; } = null!;
[MaxLength(255)] public string? Name2 { get; set; }
[MaxLength(255)] public string? Name3 { get; set; }
```

`ILabelProvider.PropertyLabel(Type entity, string property)` returns the localized label for the
request culture, resolved by convention — `<Entity>_<Property>` in the declaring assembly's
`Strings`, then in each base class's assembly, then `<Property>` in Core's `Strings` (`Name`,
`Code`, `IsActive`, `Description` … ship there), then the humanized property name. For twins the
label is the primary's label plus the symbol of the tenant language at that index — `Name (E)`,
`Name (ع)` — and for a mono-lingual tenant the primary carries no suffix. The same provider
serves Excel headers (T9), validation messages naming a field (T5/T6), and the settings page
(D4). A twin beyond the tenant's language count has no label and is not offered.

**Rationale.** Zero resource entries for the common entity (all its property names are Core
keys); one attribute; the label convention is a platform function, not per-entity code.

**Rejected.** `[Display(Name = "Center_Name")]` on every property (the monolith; N lines per
entity); attributes on each twin; a pure naming convention without the attribute (`Address2`
would be mistaken for a language twin).

**Confidence.** High.

### D19 — The Queryex schema is keyed by *language shape*, not by tenant; gated twins are nulled on save

**Decision.** `IQueryexSchemaProvider.GetSchema(MultilingualShape shape)` (T2 builds it from the
EF model) returns one immutable `QueryexSchema` per shape — `Primary`, `PrimaryAndSecondary`,
`All` — omitting `*2`/`*3` twins of `[Multilingual]` groups beyond the shape. The provider holds
at most three schemas per process, built lazily, shared by every tenant with that shape, and the
settings cache maps a tenant to its shape (`TenantSettings.Shape`). Queryex's own L2/L3 caches,
keyed on schema identity (spec 0008 §16), are therefore shared across tenants too. Row-level
security composition and weak-entity path rewriting (T4) operate on `FilterTree`s, not on the
schema, so nothing else about the schema is per tenant. A model change (deployment) is the only
other invalidation.

The save pipeline's preprocessing (T5) sets gated twins to `null` before validation; an Excel
import column that maps to a gated twin is a validation error (`Import_LanguageNotConfigured`).

**Rationale.** Per-tenant schemas would multiply Queryex's compiled-SQL caches by the tenant
count for no semantic difference; the shape has three values.

**Rejected.** One schema with all twins present and a runtime "not configured" diagnostic
(the brain dump's "removed from queryex schema" is better: an expression cannot even name a
column the tenant does not have).

**Confidence.** High.

### D20 — Settings edit API (position for T8): one `settings/save` with a field-listed patch plus entry upserts; per-category securables; a "refresh caches" action

**Decision (position; T8 owns the endpoint).** `POST {tenant}/api/web/settings/save` takes
`{ general?: TenantSettingsPatch, entries?: SettingEntryPatch[], expectedModifiedAt? }` where the
patch is a typed record with an explicit `Fields` list (RFC 7396 semantics, field-mask style — the
research digest's recommendation over JSON Patch) and each entry patch is `{ key, value | null }`
(null = reset to default = delete the row). Authorization: `settings.general` × `edit` for the typed
part; `settings.<category>` × `edit` per entry category, registered from the key registry (D4).
Pipeline: hydrate `Settings` and affected `SettingEntry` rows in the validation round trip, merge,
validate (catalogue rules D12, key types D4), persist through the emitter (audit, concurrency,
temporal history, tag bump all automatic), return the client DTO with its new tag. A second
action `settings/refresh-caches` (admin-only) bumps every tag, for after out-of-band changes.

**Confidence.** Medium (T8's call on the exact shape).

### D21 — Telemetry names

**Decision.** Meter `Tellma.Core`; constants in
`Tellma.Core.Abstractions.Caching.CacheTelemetryNames` and
`Tellma.Core.Abstractions.Localization.LocalizationTelemetryNames`:

| Instrument | Kind | Unit | Tags (closed sets) |
|---|---|---|---|
| `tellma.cache.requests` | counter | `{request}` | `cache.kind` ∈ settings, permissions, preferences, entities, tags; `cache.outcome` ∈ hit, miss, stale, bypass, oversized |
| `tellma.cache.loads.duration` | histogram | `s` | `cache.kind` |
| `tellma.cache.entries` | observable gauge | `{entry}` | `cache.kind` |
| `tellma.cache.size` | observable gauge | `{unit}` (entries or rows per kind) | `cache.kind` |
| `tellma.cache.evictions` | counter | `{entry}` | `cache.kind`, `cache.reason` ∈ capacity, expired, replaced |
| `tellma.versiontags.bumps` | counter | `{bump}` | `tag.kind` ∈ settings, permissions, entities, entity |
| `tellma.versiontags.stale` | counter | `{event}` | `stale.phase` ∈ read-rerun, write-guard, exhausted |
| `tellma.settings.entries.invalid` | counter | `{entry}` | none (key name goes to the log) |
| `tellma.localization.missing` | counter | `{lookup}` | `culture` (offered set) |
| `tellma.localization.headers.rejected` | counter | `{header}` | `header` ∈ accept-language, calendar, time-zone |

No tenant, user, entity-name, or key-name tag anywhere; those go to structured log properties.

### D22 — Testing

- `test/core/Tellma.Core.Tests` (PR tier): language/calendar catalogue invariants (unique
  symbols, every code a predefined culture, Gregorian assignable to every offered culture ×
  region sample); negotiation table (headers × preferences × tenant, `-u-` stripping, invalid
  header ignored); `EthiopianCalendar` conformance vectors (ported from the monolith's test
  project) and `ICalendarFormatter` golden strings per calendar × style × culture; ICU
  decorator (plural categories for `ar`/`am`, gender select, malformed pattern → raw);
  fallback chain; `VersionedCache` (tag mismatch = miss, single-flight under 64 concurrent
  callers, bounds/eviction, format-version wire form); `SettingKey` grammar and registry
  validation diagnostics; `[Multilingual]` twin discovery and label resolution; schema shape
  gating over a fixture model (with T2's adapter).
- `test/core/Tellma.Core.IntegrationTests` (`Category=Integration`, Testcontainers/LocalDB):
  end-to-end bump — save a fixture `[Cacheable]` entity through the batch and observe
  `entity:<Name>` and `entities` moved, `settings` untouched; the write guard raises 51001 on a
  moved tag and the pipeline re-runs once; the tags prelude rides the first batch; temporal
  history rows for `Settings`/`SettingEntries` writes and none for tag bumps; the `TenantId`
  guard; migrator seed of `core.VersionTags` rows.
- No `Live=true` suite: nothing here talks to a third party.

### D23 — Configuration

`Tellma:Cache` → `TellmaCacheOptions { SettingsEntries = 10_000, PermissionsEntries = 50_000,
PreferencesEntries = 50_000, EntityRows = 2_000_000, EntityMaxRowsDefault = 1_000,
EntityMaxBytes = 4 MB, TagSnapshots = 10_000 }`, `ValidateOnStart`. Nothing else is
configurable; the language list is code (D12) because it is a fact about what the distribution
ships, not about where it runs.

---

## 3. Contracts

Namespaces are final; XML docs are the shape a spec author can copy. Code blocks are normative
for shape, not formatting.

### 3.1 Version tags — `Tellma.Core.Abstractions.Caching`

```csharp
namespace Tellma.Core.Abstractions.Caching;

/// <summary>
///     An opaque validator for one cached shape of tenant or user data. Compared for equality
///     only; a value that differs from a cached one means "reload". Application-generated, so a
///     database restore can never re-reach a value a cache already holds.
/// </summary>
/// <param name="Value">The tag value. <see cref="Guid.Empty"/> denotes "never read".</param>
public readonly record struct VersionTag(Guid Value)
{
    /// <summary>The tag no cache entry can ever match: forces a load.</summary>
    public static VersionTag None { get; } = new(Guid.Empty);

    /// <summary>Mints a fresh tag. Used by the batch finalizer for every bump.</summary>
    public static VersionTag New() { return new(Guid.NewGuid()); }

    /// <summary>
    ///     The wire form: the consumer's format version, a dot, and the 32-hex tag. A changed
    ///     format version changes the string, which is what makes a browser refetch after a
    ///     deployment that reshaped the cached value.
    /// </summary>
    /// <param name="formatVersion">The consuming shape's <c>FormatVersion</c> constant.</param>
    public string ToWire(int formatVersion) { return $"{formatVersion}.{Value:N}"; }
}

/// <summary>The tenant-level tag names the platform defines. Packs add none; entities add
///     <c>entity:&lt;Name&gt;</c> through <see cref="CacheableAttribute"/>.</summary>
public static class VersionTagNames
{
    /// <summary>Moves when <c>core.Settings</c> or <c>core.SettingEntries</c> change.</summary>
    public const string Settings = "settings";

    /// <summary>Moves when roles, permissions, or role memberships change.</summary>
    public const string Permissions = "permissions";

    /// <summary>Moves when any cacheable entity's table changes; the composite the client watches.</summary>
    public const string Entities = "entities";

    /// <summary>The prefix of a cacheable entity's own tag: <c>entity:Country</c>.</summary>
    public const string EntityPrefix = "entity:";

    /// <summary>Builds a cacheable entity's tag name from its logical entity name.</summary>
    public static string ForEntity(string entityName) { return EntityPrefix + entityName; }
}

/// <summary>
///     Declares that writes to this entity's table move the named tenant-level version tag. The
///     batch executor appends the bump to every batch that writes the table; the entity's author
///     writes no bump anywhere. Repeatable.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class BumpsVersionTagAttribute(string tagName) : Attribute
{
    /// <summary>The tag name, e.g. <see cref="VersionTagNames.Settings"/>.</summary>
    public string TagName { get; } = tagName;
}

/// <summary>
///     Marks a small, read-mostly entity whose whole table the platform caches per tenant and
///     serves to callers whose read permission carries no filter. Implies bumps of
///     <c>entity:&lt;Name&gt;</c> and <see cref="VersionTagNames.Entities"/> on every write.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class CacheableAttribute : Attribute
{
    /// <summary>Rows beyond this count are served but not cached, and the event is logged. Default 1,000.</summary>
    public int MaxRows { get; set; } = 1_000;
}

/// <summary>The tags of one tenant as last read from <c>core.VersionTags</c> by a batch prelude.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="Tags">Tag by name. A name absent here reads as <see cref="VersionTag.None"/>.</param>
/// <param name="ReadAtUtc">When the prelude ran, for diagnostics only.</param>
public sealed record VersionTagSnapshot(int TenantId, IReadOnlyDictionary<string, VersionTag> Tags, DateTime ReadAtUtc)
{
    /// <summary>The tag by name, or <see cref="VersionTag.None"/>.</summary>
    public VersionTag this[string name] => Tags.TryGetValue(name, out VersionTag tag) ? tag : VersionTag.None;
}

/// <summary>Holds the current snapshot per tenant; replaced by every batch prelude.</summary>
public interface IVersionTagSnapshots
{
    /// <summary>The last snapshot read for the tenant, or an empty one that forces loads.</summary>
    VersionTagSnapshot Current(int tenantId);

    /// <summary>Replaces the tenant's snapshot with what a batch prelude just returned.</summary>
    void Replace(VersionTagSnapshot snapshot);
}

/// <summary>A cached input a batch depended on; the executor guards writes and re-runs reads on it.</summary>
/// <param name="TagName">The tag name.</param>
/// <param name="ExpectedTag">The tag the cached input was stamped with.</param>
public sealed record CacheDependency(string TagName, VersionTag ExpectedTag);

/// <summary>Raised when a batch's declared cache dependencies no longer match the database and the
///     bounded re-run is exhausted. Retryable by the caller.</summary>
public sealed class StaleVersionTagException(IReadOnlyList<CacheDependency> dependencies)
    : Exception("Cached inputs were invalidated while the request executed.")
{
    /// <summary>The dependencies that moved.</summary>
    public IReadOnlyList<CacheDependency> Dependencies { get; } = dependencies;
}

/// <summary>Raised when the tenant database's settings row identifies a different tenant than the
///     one the connection was resolved for. Fail-closed; never a filter.</summary>
public sealed class TenantDatabaseMismatchException(int expectedTenantId, int actualTenantId)
    : Exception($"The database identifies itself as tenant {actualTenantId}, not {expectedTenantId}.")
{
    /// <summary>The tenant the connection string was resolved for.</summary>
    public int ExpectedTenantId { get; } = expectedTenantId;

    /// <summary>The tenant the settings row claims.</summary>
    public int ActualTenantId { get; } = actualTenantId;
}
```

The standalone table type for the write guard, beside `IdList` in `Tellma.Core.Abstractions.TableTypes`:

```csharp
namespace Tellma.Core.Abstractions.TableTypes;

/// <summary>A row of the <c>[VersionTagList]</c> table type: an expected tag by name, bound into the
///     write guard of a batch that composed cached inputs.</summary>
public class VersionTagList
{
    /// <summary>The tag name; the primary key of the type.</summary>
    [Key, MaxLength(128)]
    public string Name { get; set; } = null!;

    /// <summary>The expected tag value.</summary>
    public Guid Tag { get; set; }
}
```

### 3.2 The versioned cache — `Tellma.Core.Abstractions.Caching` (contract) and `Tellma.Core.Caching` (base)

```csharp
namespace Tellma.Core.Abstractions.Caching;

/// <summary>The outcome of one cache lookup, for meters and for the optimistic re-run decision.</summary>
public enum CacheOutcome
{
    /// <summary>Served from memory with a matching tag.</summary>
    Hit,
    /// <summary>No entry; loaded.</summary>
    Miss,
    /// <summary>An entry existed with a different tag; reloaded.</summary>
    Stale,
    /// <summary>The cache does not apply to this caller (filtered read permission).</summary>
    Bypass,
    /// <summary>The value exceeded its size cap; served once, not stored.</summary>
    Oversized,
}

/// <summary>A cached value with the tag it was loaded under.</summary>
/// <typeparam name="TValue">The cached value type; immutable by convention.</typeparam>
/// <param name="Value">The value.</param>
/// <param name="Tag">The tag the loading batch returned for this kind.</param>
/// <param name="Outcome">How the lookup was satisfied.</param>
public sealed record CacheResult<TValue>(TValue Value, VersionTag Tag, CacheOutcome Outcome);

/// <summary>Per-kind bounds and the process-wide defaults; bound from <c>Tellma:Cache</c>.</summary>
public sealed class TellmaCacheOptions
{
    /// <summary>Settings entries (one per tenant).</summary>
    public int SettingsEntries { get; set; } = 10_000;
    /// <summary>Permission-set entries (one per tenant × user).</summary>
    public int PermissionsEntries { get; set; } = 50_000;
    /// <summary>Preference entries (one per tenant × user).</summary>
    public int PreferencesEntries { get; set; } = 50_000;
    /// <summary>Total cached rows across all cacheable entities and tenants.</summary>
    public long EntityRows { get; set; } = 2_000_000;
    /// <summary>The default per-entity row cap when <see cref="CacheableAttribute.MaxRows"/> is not set.</summary>
    public int EntityMaxRowsDefault { get; set; } = 1_000;
    /// <summary>The per-list estimated-size cap in bytes; a larger list is served once and not cached.</summary>
    public long EntityMaxBytes { get; set; } = 4L * 1024 * 1024;
    /// <summary>Tag snapshots (one per tenant).</summary>
    public int TagSnapshots { get; set; } = 10_000;
}
```

```csharp
namespace Tellma.Core.Caching;

/// <summary>
///     The platform's one in-process cache primitive: a private bounded <see cref="MemoryCache"/>
///     whose entries carry the version tag they were loaded under, single-flight loads per key,
///     and meters per kind. Derived classes supply the load, which must return the value together
///     with the tag read in the same round trip.
/// </summary>
/// <typeparam name="TKey">The lookup key (tenant id, or a tenant/user or tenant/entity pair).</typeparam>
/// <typeparam name="TValue">The cached value; immutable by convention.</typeparam>
public abstract class VersionedCache<TKey, TValue> : IDisposable where TKey : notnull
{
    /// <summary>Creates the cache with its bound and its meter dimension.</summary>
    /// <param name="kind">The closed-set kind value for <c>cache.kind</c>.</param>
    /// <param name="sizeLimit">The bound, in the kind's own units (entries or rows).</param>
    /// <param name="meters">The platform meter factory.</param>
    protected VersionedCache(string kind, long sizeLimit, IMeterFactory meters) { /* private MemoryCache, TrackStatistics */ }

    /// <summary>
    ///     Returns the entry when its tag equals <paramref name="currentTag"/>; otherwise loads
    ///     (one loader per key in flight; concurrent callers await it) and stores the result.
    /// </summary>
    public ValueTask<CacheResult<TValue>> GetAsync(TKey key, VersionTag currentTag, CancellationToken cancellationToken);

    /// <summary>Stores a value the caller just produced (a save returning the new state) under its tag.</summary>
    public void Set(TKey key, TValue value, VersionTag tag);

    /// <summary>Loads the value and the tag it is valid under, in one round trip.</summary>
    protected abstract ValueTask<(TValue Value, VersionTag Tag, long Size)> LoadAsync(TKey key, CancellationToken cancellationToken);

    /// <inheritdoc />
    public void Dispose() { /* disposes the MemoryCache */ }
}
```

### 3.3 Settings — `Tellma.Core.Abstractions.Settings`

```csharp
namespace Tellma.Core.Abstractions.Settings;

/// <summary>
///     The single settings row of a tenant database (<c>core.Settings</c>, <c>Id = 1</c>): the
///     values the platform itself reads to build every request's context. Sealed: features add
///     settings through <see cref="SettingKey{T}"/>, never through columns here.
/// </summary>
[TableType, BumpsVersionTag(VersionTagNames.Settings)]
public sealed class Settings : AuditedEntity   // T2's base: Id, CreatedAt/ById, ModifiedAt/ById; temporal via T2's capability
{
    /// <summary>The tenant this database belongs to. A guard, checked on first use; never a filter.</summary>
    public int TenantId { get; set; }

    /// <summary>The tenant's display name in its primary language.</summary>
    [Required, MaxLength(255), Multilingual]
    public string Name { get; set; } = null!;

    /// <summary>The display name in the secondary language.</summary>
    [MaxLength(255)] public string? Name2 { get; set; }

    /// <summary>The display name in the ternary language.</summary>
    [MaxLength(255)] public string? Name3 { get; set; }

    /// <summary>The primary content language (BCP 47 tag from the distribution's declared languages).</summary>
    [Required, MaxLength(16)] public string PrimaryLanguage { get; set; } = null!;

    /// <summary>The secondary content language, backing every <c>*2</c> column; null when mono-lingual.</summary>
    [MaxLength(16)] public string? SecondaryLanguage { get; set; }

    /// <summary>The ternary content language, backing every <c>*3</c> column; requires a secondary.</summary>
    [MaxLength(16)] public string? TernaryLanguage { get; set; }

    /// <summary>The primary calendar code (<c>gc</c>, <c>uq</c>, <c>et</c>).</summary>
    [Required, MaxLength(8)] public string PrimaryCalendar { get; set; } = "gc";

    /// <summary>An optional second calendar users may switch to.</summary>
    [MaxLength(8)] public string? SecondaryCalendar { get; set; }

    /// <summary>The tenant's business time zone as an IANA id; binds <c>today()</c>.</summary>
    [Required, MaxLength(64)] public string TimeZone { get; set; } = null!;
}

/// <summary>One stored setting value (<c>core.SettingEntries</c>): a declared key whose value
///     departs from its default. Absent row = default.</summary>
[TableType, BumpsVersionTag(VersionTagNames.Settings)]
public sealed class SettingEntry : AuditedEntity
{
    /// <summary>The declared key name (<c>gl.posting.autoNumberOnPost</c>). Unique.</summary>
    [Required, MaxLength(128)] public string Key { get; set; } = null!;

    /// <summary>The value as a JSON value (<c>true</c>, <c>12</c>, <c>"text"</c>, <c>{…}</c>).</summary>
    [Required] public string Value { get; set; } = null!;
}

/// <summary>Where a declared key's value lives.</summary>
public enum SettingScope
{
    /// <summary>One value per tenant, in <c>core.SettingEntries</c>.</summary>
    Tenant,
    /// <summary>One value per user, in the per-user preference table.</summary>
    User,
}

/// <summary>Who may see a declared key's value.</summary>
public enum SettingVisibility
{
    /// <summary>Server only; never serialized to the browser.</summary>
    Server,
    /// <summary>Included in the client settings (or preferences) DTO for every member.</summary>
    Client,
}

/// <summary>Marks a static class whose <see cref="SettingKey{T}"/> fields the composition discovers.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SettingKeysAttribute : Attribute;

/// <summary>Marks a record type usable as a <see cref="SettingKey{T}"/> value; included in the
///     platform's source-generated JSON context.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class SettingValueAttribute : Attribute;

/// <summary>The untyped view of a declared key, for registries and UIs.</summary>
public abstract class SettingKey
{
    /// <summary>The key name; camelCase dotted segments, at least two.</summary>
    public string Name { get; }
    /// <summary>The permission category; defaults to the first segment.</summary>
    public string Category { get; }
    /// <summary>Tenant or user.</summary>
    public SettingScope Scope { get; }
    /// <summary>Server or client.</summary>
    public SettingVisibility Visibility { get; }
    /// <summary>The value CLR type.</summary>
    public abstract Type ValueType { get; }
    /// <summary>The default as a JSON value, for the settings UI and MCP.</summary>
    public abstract JsonElement DefaultJson { get; }
    /// <summary>The resource key of the label: <c>Setting_</c> + name with dots replaced by underscores.</summary>
    public string LabelKey { get; }
}

/// <summary>A typed, defaulted setting declared once by a pack or distribution.</summary>
/// <typeparam name="T">A JSON-representable value type (see the startup validation rules).</typeparam>
public sealed class SettingKey<T>(
    string name,
    T defaultValue,
    string? category = null,
    SettingScope scope = SettingScope.Tenant,
    SettingVisibility visibility = SettingVisibility.Server) : SettingKey
{
    /// <summary>The value used when no row exists or the stored row does not parse.</summary>
    public T DefaultValue { get; } = defaultValue;
}

/// <summary>Every declared key in the composition, validated at startup.</summary>
public interface ISettingKeyRegistry
{
    /// <summary>All keys, in declaration order.</summary>
    IReadOnlyList<SettingKey> Keys { get; }
    /// <summary>Finds a key by name, or null.</summary>
    SettingKey? Find(string name);
    /// <summary>The distinct tenant-scope categories, each of which is a <c>settings.&lt;category&gt;</c> securable.</summary>
    IReadOnlyList<string> Categories { get; }
}

/// <summary>Which multilingual twins a tenant's configuration enables; the Queryex schema key.</summary>
public enum MultilingualShape
{
    /// <summary>Primary only: <c>*2</c> and <c>*3</c> twins are absent.</summary>
    Primary = 1,
    /// <summary>Primary and secondary.</summary>
    PrimaryAndSecondary = 2,
    /// <summary>All three.</summary>
    All = 3,
}

/// <summary>
///     The resolved, immutable, cached view of a tenant's settings that every service reads.
///     Built once per <see cref="VersionTagNames.Settings"/> tag from the settings row, the
///     entries, and the catalogues.
/// </summary>
public sealed record TenantSettings
{
    /// <summary>The tenant id.</summary>
    public required int TenantId { get; init; }
    /// <summary>The tag this view was built under.</summary>
    public required VersionTag Tag { get; init; }
    /// <summary>Display names by language index (1..3).</summary>
    public required IReadOnlyList<string?> Names { get; init; }
    /// <summary>The content languages in order (1–3 entries), resolved from the catalogue.</summary>
    public required IReadOnlyList<LanguageInfo> Languages { get; init; }
    /// <summary>The multilingual shape implied by <see cref="Languages"/>.</summary>
    public MultilingualShape Shape => (MultilingualShape)Languages.Count;
    /// <summary>The primary calendar.</summary>
    public required CalendarInfo PrimaryCalendar { get; init; }
    /// <summary>The secondary calendar, or null.</summary>
    public CalendarInfo? SecondaryCalendar { get; init; }
    /// <summary>The business time zone.</summary>
    public required TimeZoneInfo TimeZone { get; init; }
    /// <summary>The business zone's Windows id, for SQL Server's <c>AT TIME ZONE</c> and Queryex's zone slot.</summary>
    public required string SqlServerTimeZoneName { get; init; }
    /// <summary>The stored entry values by key name (raw JSON); absent = default.</summary>
    public required IReadOnlyDictionary<string, JsonElement> Entries { get; init; }

    /// <summary>The value of a declared tenant-scope key: the stored value, else its default.</summary>
    public T Get<T>(SettingKey<T> key) { /* parses Entries[key.Name] with the platform JSON context, else DefaultValue */ }
}

/// <summary>What the browser caches: everything an authenticated member may know.</summary>
public sealed record TenantSettingsForClient
{
    /// <summary>Bumped when this record's shape changes; part of the wire tag.</summary>
    public const int FormatVersion = 1;
    /// <summary>The tenant id.</summary>
    public required int TenantId { get; init; }
    /// <summary>Display names by language index.</summary>
    public required IReadOnlyList<string?> Names { get; init; }
    /// <summary>The content languages with code, native name, symbol, and direction.</summary>
    public required IReadOnlyList<LanguageInfo> Languages { get; init; }
    /// <summary>The calendars offered (primary first).</summary>
    public required IReadOnlyList<CalendarInfo> Calendars { get; init; }
    /// <summary>The business time zone's IANA id.</summary>
    public required string TimeZone { get; init; }
    /// <summary>Client-visible entries by key (stored or default — the client never needs to know which).</summary>
    public required IReadOnlyDictionary<string, JsonElement> Entries { get; init; }
}

/// <summary>The settings cache: one entry per tenant holding both views.</summary>
public interface ITenantSettingsCache
{
    /// <summary>The server view under the tenant's current <c>settings</c> tag (loads on miss).</summary>
    ValueTask<CacheResult<TenantSettings>> GetAsync(int tenantId, CancellationToken cancellationToken);
    /// <summary>The client view and its wire tag.</summary>
    ValueTask<(TenantSettingsForClient Settings, string WireTag)> GetForClientAsync(int tenantId, CancellationToken cancellationToken);
}

/// <summary>Whole-table lists of cacheable entities.</summary>
public interface ICacheableEntities
{
    /// <summary>The rows of a cacheable entity under its current tag; <see cref="CacheOutcome.Bypass"/>
    ///     when the caller's read permission is filtered, in which case the caller queries normally.</summary>
    ValueTask<CacheResult<IReadOnlyList<TEntity>>> GetAllAsync<TEntity>(CancellationToken cancellationToken) where TEntity : class;
    /// <summary>Every cacheable entity's wire tag, for the client's per-entity refetch decision.</summary>
    ValueTask<IReadOnlyDictionary<string, string>> GetWireTagsAsync(CancellationToken cancellationToken);
}
```

### 3.4 Localization — `Tellma.Core.Abstractions.Localization`

```csharp
namespace Tellma.Core.Abstractions.Localization;

/// <summary>One language the platform can describe.</summary>
/// <param name="Code">BCP 47 language tag (<c>en</c>, <c>ar</c>, <c>zh-Hant</c>).</param>
/// <param name="EnglishName">The English name, for operator UIs.</param>
/// <param name="NativeName">What the language calls itself; never translated.</param>
/// <param name="Symbol">The short native-script mark used in labels: <c>Name (ع)</c>. Unique across the catalogue.</param>
/// <param name="IsRightToLeft">Text direction.</param>
public sealed record LanguageInfo(string Code, string EnglishName, string NativeName, string Symbol, bool IsRightToLeft);

/// <summary>Every language the platform describes (data in code, additive), and the distribution's declared subset.</summary>
public interface ILanguageCatalog
{
    /// <summary>All known languages.</summary>
    IReadOnlyList<LanguageInfo> All { get; }
    /// <summary>The languages this distribution declared, in display order; the first is the default.</summary>
    IReadOnlyList<LanguageInfo> Offered { get; }
    /// <summary>Finds a language by code (ordinal-ignore-case), or null.</summary>
    LanguageInfo? Find(string code);
    /// <summary>Whether the code is offered by this distribution.</summary>
    bool IsOffered(string code);
}

/// <summary>Marks the primary property of a multilingual text group; twins <c>&lt;Name&gt;2</c> and
///     <c>&lt;Name&gt;3</c> are found by name and gated by the tenant's languages.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MultilingualAttribute : Attribute;

/// <summary>The address of <c>Tellma.Core</c>'s own resources, for packs that cannot reference the assembly.</summary>
public static class CoreStrings
{
    /// <summary>The resource base name.</summary>
    public const string BaseName = "Tellma.Core.Localization.Strings";
    /// <summary>The assembly name.</summary>
    public const string Assembly = "Tellma.Core";
}

/// <summary>Localized labels for entities, properties, and settings keys, by convention.</summary>
public interface ILabelProvider
{
    /// <summary>The entity's display name (<c>&lt;Entity&gt;</c> key in its assembly, else humanized).</summary>
    string EntityLabel(Type entityType);
    /// <summary>The property's label; a multilingual twin carries its language symbol suffix for the current tenant.</summary>
    string PropertyLabel(Type entityType, string propertyName);
    /// <summary>The declared setting's label (<see cref="SettingKey.LabelKey"/>, else humanized last segment).</summary>
    string SettingLabel(SettingKey key);
}

/// <summary>The negotiated localization values of one request or job scope; part of the request context.</summary>
/// <param name="Culture">UI + formatting culture; a Gregorian-forced clone of a predefined culture.</param>
/// <param name="Calendar">The presentation calendar.</param>
/// <param name="TimeZone">The presentation time zone.</param>
/// <param name="ContentLanguageIndex">1..3: the tenant content language matching the culture, else 1.</param>
public sealed record LocalizationContext(CultureInfo Culture, CalendarInfo Calendar, TimeZoneInfo TimeZone, int ContentLanguageIndex);

/// <summary>Inputs to negotiation; nulls mean "not supplied".</summary>
/// <param name="AcceptLanguage">The raw <c>Accept-Language</c> header value.</param>
/// <param name="CalendarHeader">The raw <c>Calendar</c> header value.</param>
/// <param name="TimeZoneHeader">The raw <c>Time-Zone</c> header value.</param>
/// <param name="PreferredLanguage">The user's <c>ui.language</c> preference.</param>
/// <param name="PreferredCalendar">The user's <c>ui.calendar</c> preference.</param>
/// <param name="PreferredTimeZone">The user's <c>ui.timeZone</c> preference.</param>
/// <param name="Settings">The tenant's settings (fallbacks).</param>
public sealed record NegotiationInput(
    string? AcceptLanguage, string? CalendarHeader, string? TimeZoneHeader,
    string? PreferredLanguage, string? PreferredCalendar, string? PreferredTimeZone,
    TenantSettings Settings);

/// <summary>Resolves a request's culture, calendar, and time zone by the documented precedence.</summary>
public interface ILocalizationNegotiator
{
    /// <summary>Negotiates; never throws on bad input (invalid values are ignored and metered).</summary>
    LocalizationContext Negotiate(NegotiationInput input);
}

/// <summary>Standard header names the platform reads (T6 documents them on the wire).</summary>
public static class LocalizationHeaders
{
    /// <summary>The calendar code header.</summary>
    public const string Calendar = "Calendar";
    /// <summary>The IANA time zone header (GitHub's precedent).</summary>
    public const string TimeZone = "Time-Zone";
    /// <summary>The response header carrying the tenant and user version tags.</summary>
    public const string VersionTags = "Tellma-Version-Tags";
}
```

### 3.5 Calendars — `Tellma.Core.Abstractions.Calendars`

```csharp
namespace Tellma.Core.Abstractions.Calendars;

/// <summary>One calendar the platform can present dates in.</summary>
/// <param name="Code">The two-letter code shared with Queryex's <c>cal</c> argument: <c>gc</c>, <c>uq</c>, <c>et</c>.</param>
/// <param name="EnglishName">Gregorian, Umm al-Qura, Ethiopian.</param>
/// <param name="MonthCount">12, or 13 for Ethiopian.</param>
/// <param name="MinDate">The first Gregorian date the calendar can express.</param>
/// <param name="MaxDate">The last Gregorian date the calendar can express.</param>
public sealed record CalendarInfo(string Code, string EnglishName, int MonthCount, DateOnly MinDate, DateOnly MaxDate);

/// <summary>The calendars in Core, by code.</summary>
public interface ICalendarCatalog
{
    /// <summary>All calendars.</summary>
    IReadOnlyList<CalendarInfo> All { get; }
    /// <summary>Finds by code (ordinal-ignore-case), or null.</summary>
    CalendarInfo? Find(string code);
    /// <summary>The BCL <see cref="System.Globalization.Calendar"/> implementing the code.</summary>
    System.Globalization.Calendar Implementation(string code);
}

/// <summary>Named date styles a message or a header may ask for.</summary>
public enum DateStyle
{
    /// <summary>Numeric, in the culture's order and separator: <c>19/3/1448</c>.</summary>
    Short,
    /// <summary>Day, abbreviated month, year.</summary>
    Medium,
    /// <summary>Day, full month name, year.</summary>
    Long,
    /// <summary>Weekday, day, full month, year.</summary>
    Full,
}

/// <summary>Renders dates in any supported calendar and culture; the only date formatter the platform uses.</summary>
public interface ICalendarFormatter
{
    /// <summary>Formats a calendar date.</summary>
    string Format(DateOnly date, CalendarInfo calendar, CultureInfo culture, DateStyle style);
    /// <summary>Converts to the zone, then formats date and time.</summary>
    string Format(DateTimeOffset instant, TimeZoneInfo zone, CalendarInfo calendar, CultureInfo culture, DateStyle style, bool includeTime = true);
    /// <summary>The (year, month, day) of a date in the calendar; throws for out-of-range dates.</summary>
    (int Year, int Month, int Day) Decompose(DateOnly date, CalendarInfo calendar);
    /// <summary>The localized month name (from Core resources for <c>uq</c>/<c>et</c>, from the culture for <c>gc</c>).</summary>
    string MonthName(CalendarInfo calendar, int month, CultureInfo culture, bool abbreviated);
}
```

### 3.6 Telemetry names — `Tellma.Core.Abstractions.Caching.CacheTelemetryNames`, `…Localization.LocalizationTelemetryNames`

```csharp
namespace Tellma.Core.Abstractions.Caching;

/// <summary>Meter, instrument, and tag names for the version-tag caches. Constants, never literals at call sites.</summary>
public static class CacheTelemetryNames
{
    /// <summary>The meter name (the emitting package).</summary>
    public const string Meter = "Tellma.Core";
    /// <summary>Counter of lookups by kind and outcome.</summary>
    public const string Requests = "tellma.cache.requests";
    /// <summary>Histogram of load durations in seconds.</summary>
    public const string LoadDuration = "tellma.cache.loads.duration";
    /// <summary>Gauge of entries per kind.</summary>
    public const string Entries = "tellma.cache.entries";
    /// <summary>Gauge of size per kind, in the kind's unit.</summary>
    public const string Size = "tellma.cache.size";
    /// <summary>Counter of evictions.</summary>
    public const string Evictions = "tellma.cache.evictions";
    /// <summary>Counter of tag bumps by tag kind.</summary>
    public const string Bumps = "tellma.versiontags.bumps";
    /// <summary>Counter of stale-tag events by phase.</summary>
    public const string Stale = "tellma.versiontags.stale";
    /// <summary>Counter of stored setting entries that failed to parse.</summary>
    public const string InvalidEntries = "tellma.settings.entries.invalid";

    /// <summary>Tag keys.</summary>
    public static class Tags
    {
        /// <summary><c>cache.kind</c>.</summary>
        public const string Kind = "cache.kind";
        /// <summary><c>cache.outcome</c>.</summary>
        public const string Outcome = "cache.outcome";
        /// <summary><c>cache.reason</c> (evictions).</summary>
        public const string Reason = "cache.reason";
        /// <summary><c>tag.kind</c> (bumps).</summary>
        public const string TagKind = "tag.kind";
        /// <summary><c>stale.phase</c>.</summary>
        public const string StalePhase = "stale.phase";
    }
}
```

(`LocalizationTelemetryNames` carries `Missing = "tellma.localization.missing"`,
`HeadersRejected = "tellma.localization.headers.rejected"`, tags `culture`, `header`.)

### 3.7 What this theme needs from other themes' seams

From **T2 (batch abstraction, seam 1)** — the two hooks that make D6 and D7 structural:

```csharp
namespace Tellma.Core.Abstractions.Data;   // T2 owns; members this theme requires

/// <summary>What one statement writes; the save emitter fills it, raw SQL must declare it.</summary>
public interface IDbStatement
{
    /// <summary>The entity types whose tables this statement writes; empty for reads.</summary>
    IReadOnlySet<Type> WrittenEntityTypes { get; }
    /// <summary>Whether the executor may re-issue the statement after a transient failure.</summary>
    bool MayRetry { get; }
}

/// <summary>Runs once per batch, after every statement is added and before execution.</summary>
public interface IDbBatchFinalizer
{
    /// <summary>Inspects the batch and may append statements (inside the batch's transaction when one exists).</summary>
    void OnFinalizing(DbBatchFinalizingContext context);
}

/// <summary>The finalizer's view of a batch.</summary>
public sealed class DbBatchFinalizingContext
{
    /// <summary>The union of written entity types across statements.</summary>
    public IReadOnlySet<Type> WrittenEntityTypes { get; }
    /// <summary>Appends a raw statement with a TVP-bound parameter set.</summary>
    public void AppendRawSql(string sql, IReadOnlyList<DbParameterBinding> parameters, IReadOnlySet<Type> writes);
}

/// <summary>Batch-level declarations the executor honours.</summary>
public interface IDbBatchBuilder
{
    /// <summary>Declares the cached inputs this batch composed; writes are guarded, reads re-run once on mismatch.</summary>
    IDbBatchBuilder DependsOn(params IReadOnlyList<CacheDependency> dependencies);
}

/// <summary>Batch results this theme reads.</summary>
public interface IDbBatchResult
{
    /// <summary>The tags prelude's rows (present on every executed batch).</summary>
    VersionTagSnapshot VersionTags { get; }
}
```

Plus: the executor prepends the tags prelude to the first batch of a scope and calls
`IVersionTagSnapshots.Replace` with the result; error 51001 maps to `StaleVersionTagException`
and the pipeline (T5) re-runs once; the host parameter prefix `@tm_` stays outside Queryex's
`@qx` namespace; `[dbo].[StringList]` and `[dbo].[VersionTagList]` are registered standalone types.

From **T1 (request context, seam 9)**:

```csharp
namespace Tellma.Core.Abstractions.Hosting;   // T1 owns; members this theme populates or reads

/// <summary>The immutable per-request (or per-job) context; the scoped holder is populated once.</summary>
public interface IRequestContext
{
    int TenantId { get; }
    int? UserId { get; }
    bool IsSandbox { get; }
    /// <summary>The tenant's settings as cached at scope start (this theme's <see cref="TenantSettings"/>).</summary>
    TenantSettings Settings { get; }
    /// <summary>Negotiated culture, calendar, zone, content-language index (this theme's <see cref="LocalizationContext"/>).</summary>
    LocalizationContext Localization { get; }
    /// <summary>The user's preferences (T4's shape) — read here for the <c>ui.*</c> keys.</summary>
    IUserPreferences? Preferences { get; }
}
```

The binder order inside T1's endpoint filter: resolve tenant → `ITenantSettingsCache.GetAsync`
(using the previous snapshot; optimistic) → T4's user/preferences → `ILocalizationNegotiator`
→ set `CultureInfo.CurrentCulture/CurrentUICulture` → populate the holder. Job scopes copy a
serialized `LocalizationContext` (culture name, calendar code, zone id, index) at enqueue.

From **T4 (seam 5 consumer)**: `core.UserStates.PreferencesTag uniqueidentifier NOT NULL` returned by
the connect prelude; `[BumpsVersionTag(VersionTagNames.Permissions)]` on `Role`, `Permission`,
`RoleMembership`; the permission-evaluation result exposes `IsUnfiltered` for D10's bypass rule;
the securables registry accepts `settings.<category>` × `edit` registrations from
`ISettingKeyRegistry.Categories`.

From **T6 (wire)**: the `Tellma-Version-Tags` response header; `settings/client`,
`settings/entity-tags`, `<entity>/all` routes; the `Calendar` and `Time-Zone` request headers;
validation messages rendered through `IStringLocalizer` with the request culture.

From **T9**: `ILabelProvider` for headers; `ICalendarFormatter`/`LocalizationContext` for number
formats; `MultilingualShape` for column mapping.

---

## 4. Schema

Naming follows ARCHITECTURE.md: plural table names in schema `core`; entity classes singular
except `Settings` (a plural noun naming one row). Audit columns per the orchestrator's hint;
period columns are EF shadow properties (EF 10 cannot map them to CLR properties).

```sql
-- core.Settings — one row per tenant database. System-versioned; no tag columns.
CREATE TABLE [core].[Settings] (
    [Id]                 int            NOT NULL CONSTRAINT [PK_Settings] PRIMARY KEY,
    [TenantId]           int            NOT NULL,                     -- guard, never a filter (D3)
    [Name]               nvarchar(255)  NOT NULL,
    [Name2]              nvarchar(255)  NULL,
    [Name3]              nvarchar(255)  NULL,
    [PrimaryLanguage]    nvarchar(16)   NOT NULL,                     -- BCP 47; ∈ distribution languages
    [SecondaryLanguage]  nvarchar(16)   NULL,
    [TernaryLanguage]    nvarchar(16)   NULL,
    [PrimaryCalendar]    nvarchar(8)    NOT NULL CONSTRAINT [DF_Settings_PrimaryCalendar] DEFAULT N'gc',
    [SecondaryCalendar]  nvarchar(8)    NULL,
    [TimeZone]           nvarchar(64)   NOT NULL,                     -- IANA id, normalized
    [CreatedAt]          datetime2(7)   NOT NULL,
    [CreatedById]        int            NOT NULL CONSTRAINT [FK_Settings_CreatedById] REFERENCES [core].[Users] ([Id]),
    [ModifiedAt]         datetime2(7)   NOT NULL,                     -- concurrency stamp (T2)
    [ModifiedById]       int            NOT NULL CONSTRAINT [FK_Settings_ModifiedById] REFERENCES [core].[Users] ([Id]),
    [ValidFrom]          datetime2(7)   GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo]            datetime2(7)   GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]),
    CONSTRAINT [CK_Settings_SingleRow]        CHECK ([Id] = 1),
    CONSTRAINT [CK_Settings_TernaryNeedsSecondary] CHECK ([TernaryLanguage] IS NULL OR [SecondaryLanguage] IS NOT NULL),
    CONSTRAINT [CK_Settings_LanguagesDistinct] CHECK ([SecondaryLanguage] <> [PrimaryLanguage] AND [TernaryLanguage] <> [PrimaryLanguage] AND [TernaryLanguage] <> [SecondaryLanguage]),
    CONSTRAINT [CK_Settings_CalendarsDistinct] CHECK ([SecondaryCalendar] <> [PrimaryCalendar])
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [core].[SettingsHistory]));
-- No sequence: the row is inserted by the migrator's seed at provisioning. No index beyond the PK.
-- UDTT: [core].[SettingsList] (derived; period columns are not insertable and are excluded by derivation).
```

```sql
-- core.SettingEntries — declared keys whose value departs from the default. System-versioned.
CREATE TABLE [core].[SettingEntries] (
    [Id]            int            NOT NULL CONSTRAINT [PK_SettingEntries] PRIMARY KEY,   -- from sq_SettingEntries
    [Key]           nvarchar(128)  NOT NULL,
    [Value]         nvarchar(max)  NOT NULL,                     -- one JSON value
    [CreatedAt]     datetime2(7)   NOT NULL,
    [CreatedById]   int            NOT NULL CONSTRAINT [FK_SettingEntries_CreatedById] REFERENCES [core].[Users] ([Id]),
    [ModifiedAt]    datetime2(7)   NOT NULL,
    [ModifiedById]  int            NOT NULL CONSTRAINT [FK_SettingEntries_ModifiedById] REFERENCES [core].[Users] ([Id]),
    [ValidFrom]     datetime2(7)   GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo]       datetime2(7)   GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]),
    CONSTRAINT [UX_SettingEntries_Key] UNIQUE ([Key])
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [core].[SettingEntriesHistory]));
CREATE SEQUENCE [core].[sq_SettingEntries] AS int START WITH 1000 INCREMENT BY 1;   -- reserved band below 1000 (spec 0001 §4)
-- UDTT: [core].[SettingEntriesList]. Queryex: exposed as entity "SettingEntry" (Key, Value are strings) for the admin list page.
```

```sql
-- core.VersionTags — tenant-level tags. Not temporal, no audit, no sequence, no UDTT, not in the Queryex schema.
CREATE TABLE [core].[VersionTags] (
    [Name]      nvarchar(128)     NOT NULL CONSTRAINT [PK_VersionTags] PRIMARY KEY,
    [Tag]       uniqueidentifier  NOT NULL,
    [BumpedAt]  datetime2(3)      NOT NULL                        -- diagnostics ("when did settings last change")
);
-- Seeded by the migrator's runtime seed for every tag name the model declares
-- ('settings', 'permissions', 'entities', 'entity:<Name>' …), INSERT … WHERE NOT EXISTS under the app lock.
-- Read: SELECT [Name], [Tag] FROM [core].[VersionTags];              (the prelude, every batch)
-- Bump: the two statements of D6, one @tm_tag per batch.
-- Guard: the IF EXISTS … THROW 51001 of D7, before BEGIN TRAN.
```

```sql
-- Standalone table type (spec 0001 §5), declared from the VersionTagList class in Abstractions.
CREATE TYPE [dbo].[VersionTagList_<hash8>] AS TABLE (
    [Name]  nvarchar(128)     NOT NULL PRIMARY KEY,
    [Tag]   uniqueidentifier  NOT NULL
);
```

```sql
-- Owned by T4; the columns this theme requires on the non-temporal sibling of core.Users.
-- core.UserStates (T4 names it)
--   [UserId]          int               NOT NULL PRIMARY KEY REFERENCES [core].[Users] ([Id]),
--   [PreferencesTag]  uniqueidentifier  NOT NULL,   -- bumped by the preferences save (single writer)
--   … LastActive, inbox watermark (T4/T10)
```

Indexes: none beyond the keys above. Every read here is by primary key or a full scan of a
tiny table.

---

## 5. Answers to the brain dump's open questions in this theme

| Brain-dump question (abridged) | Answer | Decision |
|---|---|---|
| Caching: "How do we guarantee the cache version is invalidated when a cacheable entity is updated?" | Writes declare their tables; a batch finalizer appends the bump inside the transaction; `[Cacheable]`/`[BumpsVersionTag]` map tables to tag names. | D6 |
| Caching: "Is 'version' the accurate technical name? etag? fingerprint?" | "Version tag" (`VersionTag`); ETag stays HTTP's; fingerprint rejected. | D1 |
| Caching: "Is 'metaversion' the accurate technical name?" | No — "format version", a `const int` per cached shape folded into the wire tag. | D8 |
| Caching: LRU, thread-safe, stampede-safe, observability, "was it right to cache" | Private bounded `MemoryCache` per kind, single-flight, meters incl. `oversized`/`bypass` outcomes. | D9, D10, D21 |
| Settings: "Is calling the table Settings correct?" | Yes — `core.Settings`, one row, sealed entity `Settings`. | D2, D3 |
| Settings: "What do we call the KV table?" | `core.SettingEntries`, entity `SettingEntry`; no `Category` column. | D2, D4 |
| Settings: "Language axis vs culture axis?" | Two axes: content languages (tenant, ≤3, `PrimaryLanguage`…) and the request culture (any offered language); formatting culture = request culture. | D11 |
| Settings: "Criteria for top-level table vs KV? Everything in KV?" | Typed row = what the platform reads to build the request context; everything declared by packs/distros = KV via `SettingKey<T>`; not everything in KV. | D2 |
| Settings: "Single API method vs per category? Permission resource = category?" | One `settings/save`; securable `settings.<category>` per declared category (auto-registered) plus `settings.general`. | D20 |
| Settings: "Edit signature: patch vs load-and-save?" | Field-listed patch on the wire; hydrate-merge-save through the emitter inside. | D20 |
| Settings: "This will cause temporal churn, needs its own table" (SettingsVersion) | `core.VersionTags`, non-temporal. | D5 |
| Localization: "Compliance modules may not support all languages — restrict?" | No restriction; a pack may declare `SupportedLanguages`, mismatch is a startup warning; strings fall back to English. | D12 |
| Localization: "How do we organize the resource files in the backend?" | `Localization/Strings.resx` (+ satellites) and a `Strings` marker per assembly; key conventions; Core's address exposed from Abstractions. | D16 |
| Localization: "How do we support ICU message format?" | `IcuStringLocalizerFactory` decorator, shared `MessageFormatter`, named args, calendar-aware dates through `CustomValueFormatter`. | D16 |
| Localization: "a custom calendar header (does a standard one exist?)" | None exists; `Calendar` and `Time-Zone` headers, precedence header > preference > tenant. | D13, D15 |
| Localization: fallback chain | request culture → parents → tenant primary → neutral English. | D17 |
| User: "Separate SettingsVersion and PermissionsVersion or one?" | Separate: `preferences` (user-level column) and `permissions` (tenant-level row); no per-user permissions tag. | D5 |
| User: "Version, ETag, or fingerprint?" | Version tag. | D1 |
| User: "Is JSON the right shape for user preferences?" (tag aspect) | Preferences may reuse `SettingKey<T>` with `SettingScope.User`; storage shape is T4's. | D4 |
| Web: "X-Today header? Or time zone instead?" | Neither for `today()`: the tenant zone binds it; `Time-Zone` header exists for presentation only. | D14 |
| Name2/Name3 "removed from queryex schema if not configured" | Schema per `MultilingualShape` (3 process-wide schemas); gated twins nulled on save. | D19 |
| "Name (E)" / "Name (ع)" convention | `[Multilingual]` + catalogue `Symbol`; `ILabelProvider`; no suffix for mono-lingual tenants. | D18 |
| "Cacheable entities … up to 50–100 records?" | `[Cacheable(MaxRows)]`, default 1,000; byte cap; unfiltered-read rule. | D10 |
| "Settings publicly accessible, not subject to READ permissions" | True for `TenantSettingsForClient` (client-visible keys only); server-only keys never leave the server. | D4, D9 |

---

## 6. Seams

1. **Batch abstraction (T2 owns).** Needed: per-statement `WrittenEntityTypes` and `MayRetry`;
   `IDbBatchFinalizer` with `AppendRawSql`; `DependsOn(CacheDependency…)`; the tags prelude
   prepended to a scope's first batch with its result on `IDbBatchResult.VersionTags`; error
   51001 → `StaleVersionTagException`; host parameter prefix `@tm_`; `StringList` and
   `VersionTagList` standalone types. The bump statement and the guard statement are appended by
   this theme's finalizer, not emitted by T2 — T2 only guarantees the hook runs on every batch.
2. **Entity class vs wire shape (T2/T6).** `TenantSettings`/`TenantSettingsForClient` are read
   DTOs built from the entity, not persistence DTOs; the settings *save* goes through the entity.
   Cacheable lists are entity arrays (no children) plus a wire tag.
3. **One capability, declared once (T5 owns).** `[Cacheable]` and `[Multilingual]` are the two
   attributes this theme contributes; `[Cacheable]` projects a service method (`GetAllCached`), a
   route (`<entity>/all`), a tag name, and the bypass rule; `[Multilingual]` projects schema
   gating, labels, Excel mapping, and save preprocessing.
4. **Queryex schema per tenant configuration (T2 owns).** Key = `MultilingualShape` (three values),
   lifetime = process; not per tenant. RLS composition and weak-entity rewriting are `FilterTree`
   work outside the schema. The `TimeZone` slot binds the tenant's Windows zone id from
   `TenantSettings.SqlServerTimeZoneName`; `Today` binds the current date in
   `TenantSettings.TimeZone`.
5. **Version tags (this theme owns).** Names, table, bump, prelude, guard, wire form, format
   version as specified in D5–D8. T4 consumes `permissions` and owns `PreferencesTag`; T2 emits.
6. **Feature composition (T1 owns).** This theme needs: `tellma.Languages(…)` /
   `tellma.AddLanguage(…)` on the builder; discovery of `[SettingKeys]` classes through the
   manifest; the Core feature's Contribute registering `Settings`, `SettingEntry`, `VersionTags`
   in the model, the caches, the negotiator, the ICU factory decorator, and the finalizer; the
   aggregated startup diagnostic carrying language, key-grammar, and symbol-uniqueness failures.
9. **Request context (T1 owns).** `IRequestContext.Settings` and `.Localization` are this theme's
   members; the binder order in §3.7; job scopes carry a serialized `LocalizationContext`.
11. **Permission evaluation (T4 owns).** Needed: `IsUnfiltered` on the read-permission result
    (D10), `settings.<category>` securables from the registry (D4), `[BumpsVersionTag("permissions")]`
    on the three tables (D5).
13. **Wire shapes (T6 owns).** `Tellma-Version-Tags` response header; `settings/client`,
    `settings/entity-tags`, `<entity>/all`, `settings/save`, `settings/refresh-caches`; request
    headers `Calendar`, `Time-Zone`; `Accept-Language` semantics; the error-message localization
    hook (`IStringLocalizer` under the request culture).
14. **Telemetry (T2 owns the DB-call budget; each theme names its own).** D21's instruments under
    meter `Tellma.Core`; no tenant/entity/key tags.
16. **Connect-call collapse (T4/T5).** The tags prelude is the collapse's mechanism: reads are
    optimistic with one re-run; writes are guarded in-database. Failure modes in D7.
17. **Vocabulary.** Plural tables (`core.Settings` — already plural — `core.SettingEntries`,
    `core.VersionTags`); schema `core`; four audit columns; "version tag"; "format version";
    `Tag` column; `nvarchar` for every code column.

---

## 7. Departures from ARCHITECTURE.md

1. **Calendars ship in Core, not in Locale packs.** ARCHITECTURE.md ("Per-dimension contents →
   Locale: `ICalendar`, … calendar conversion rules") places calendar implementations in
   `Tellma.Locale.<id>`. This theme puts `gc`/`uq`/`et` and `ICalendarFormatter` in `Tellma.Core`
   because a calendar a tenant can *select* must exist in every distribution (a tenant setting
   cannot depend on which packs a distribution references), the codes are part of Queryex's
   language surface, and the three implementations are small and dependency-free. Locale packs keep
   `IAmountToText`, language-specific text utilities, and the client's strings and fonts.
   ARCHITECTURE.md's Locale row and the "Per-dimension contents" bullet change accordingly.
2. **Queryex's reserved calendar codes land with Core, not "with the Locale packs".** Spec 0008
   §10.4 says `'uq'`/`'et'` "land with the Locale packs as additive registry changes"; under
   departure 1 they land with Core's calendar catalogue (the engine amendment itself is T2's
   scope; the Umm al-Qura month-map table the engine needs — `core.UmAlQuraMonths`, seeded from
   .NET's table by the migrator — is claimed by this theme when that amendment is scheduled).
3. **No `HybridCache`, no output caching for these responses.** ARCHITECTURE.md's "In-memory
   caching with proper invalidation, output caching" is honoured with a private-`MemoryCache`
   design; output caching cannot serve authenticated POSTs (research digest), so the version-tag
   header is the client's cache channel instead.
4. **Settings table is closed to leaf extension.** ARCHITECTURE.md's "Extend a pack entity
   (additive — the common customization)" is not offered for `Settings`; the extension mechanism
   is `SettingKey<T>` (D2, review flag).
5. **Feature composition surface grows by two builder calls** (`Languages`, `AddLanguage`) and one
   discovery kind (`[SettingKeys]`). Consistent with the minimal-fidelity composition T1 builds.

Consistent with ARCHITECTURE.md and worth stating: settings and caching are Core services reached
through `Tellma.Core.Abstractions` interfaces (Dependency rule 2); telemetry naming follows the
Observability section; `.resx` satellites are the shipped form of "translations" and the
`SatelliteResourceLanguages` trim is optional.

---

## 8. Verification

Facts relied on, with source:

- **HybridCache** shares the DI `IMemoryCache`, serializes every write even L1-only, deserializes
  mutable values on read, tag invalidation is process-local timestamps, `MaximumPayloadBytes`
  default 1 MB; `IMemoryCache.GetOrCreate` has no stampede protection; `SizeLimit` is unitless and
  the docs warn against limiting the shared DI cache — research §1.2–1.6 (verified 2026-09-01
  against dotnet/extensions `main` and Microsoft Learn).
- **`MemoryCache.GetCurrentStatistics()`** exists on .NET 10 (`Microsoft.Extensions.Caching.Memory`),
  returns `MemoryCacheStatistics` and `null` unless `MemoryCacheOptions.TrackStatistics` is set —
  verified by me 2026-09-01: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.memory.memorycache.getcurrentstatistics?view=net-10.0-pp
- **`MessageFormat` 8.0.0**: plural/select/selectordinal/nesting, `date` styles limited to
  `short`/`full` (medium/long throw), `CustomValueFormatter` hook with `TryFormatDate/Time/Number`,
  per-call culture override, lock-free pattern cache, 16-thread/50k probe with zero errors —
  research §2.2–2.4 (probe on .NET 10.0.11, Windows/ICU).
- **`-u-ca-` culture names** create custom cultures whose `Calendar` does not change, whose ICU
  patterns produce mixed output, and whose `Parent` skips the region; `-u-nu-` does not switch
  digits — research §3.4 (probe).
- **No Ethiopian/Coptic `Calendar` in .NET; a custom `Calendar` cannot be assigned to
  `DateTimeFormatInfo.Calendar`; `UmAlQuraCalendar` range AH 1318–1500 (AD 1900-04-30 …
  2077-11-16); BCL formats Hijri only under Arabic cultures** — research §4.1–4.4 (probe + docs).
  The monolith's `EthiopianCalendar` (JDN-based, 13 months, leap `year % 4 == 3`, range
  1752-09-14 … 2500-01-01) and `Calendars.cs` codes `gc`/`et`/`uq` — read directly from
  `C:\Users\ahmad\source\repos\Tellma\Tellma.Utilities.Calendars` on 2026-09-01.
- **`TimeZoneInfo`** resolves IANA ids case-insensitively on Windows with ICU and
  `TryConvertIanaIdToWindowsId` works (`Asia/Riyadh` → `Arab Standard Time`); ICU-dependent —
  research §5.1 (probe + docs). Linux behaviour is stated by the docs, not probed.
- **No standard time-zone or calendar header/client hint; GitHub's `Time-Zone` header precedent
  and precedence** — research §5.2.
- **`AcceptLanguageHeaderRequestCultureProvider` tries at most 3 values; `RequestLocalizationOptions`
  default culture is static; extension handling** — research §3.3 (docs + aspnetcore source).
- **`.resx` satellite assemblies, `SatelliteResourceLanguages`, `IStringLocalizerFactory.Create(baseName, location)`**
  — research §3.1–3.2 (docs); the `Create(string, string)` overload is the framework's public API
  (Microsoft.Extensions.Localization; stable since 2.x — not re-verified today).
- **`rowversion` moves on any update; `uniqueidentifier` ordering is not byte order and
  `Guid.CreateVersion7()` is not sequential in SQL Server; monotonic counters have a restore
  hazard** — research T4 §4.1–4.3 (docs, updated 2026-08-24).
- **Temporal tables**: every UPDATE writes a history row even with no change; period columns are
  shadow properties in EF 10; MERGE into temporal targets is broken; TPT roots cannot be temporal —
  research T2 §2.1–2.4 (SQL docs updated 2026-08-18; efcore issues).
- **Identity server precedents** (`LanguageCatalog.Shipped`/`Offered`, `IcuStringLocalizer<T>` with
  `ThreadLocal<MessageFormatter>`, `ui_locales` provider, `locale` claim at invitation) — read from
  `src/apps/Tellma.Identity` and spec 0003 §6.1/§10.1 on 2026-09-01.
- **Spec 0008** §3 (schema contract; no computed properties; enum by store type), §10.3–10.6
  (`local`/`TimeZone` slot, `cal` codes `'gc'` only in v1 with `'uq'`/`'et'` reserved, `today()`
  "current date in the tenant's zone"), §16 (caches keyed on schema identity), §17 (language
  version) — read 2026-09-01.
- **Spec 0001** §1–2, §4–6 (`[TableType]`, `[ExcludeFromTableType]`, standalone types from plain
  classes, `sq_<TableName>`, reserved seed band, `model.GetTableTypes()`) — read 2026-09-01.

Still unverified or assumed:

- That `GregorianCalendar` is assignable to `DateTimeFormatInfo.Calendar` for **every** predefined
  culture (it is for `ar-SA`, `ar-EG`, `ar-AE`, `am-ET`, `en-US` per the probe's
  `OptionalCalendars` output; the general claim is pinned by a unit test in D22, not by a source).
- Linux/ICU parity for Amharic/Oromo plural rules and for `TimeZoneInfo` IANA conversion
  (docs say yes; not probed).
- EF Core 10 has no API for `HISTORY_RETENTION_PERIOD`; if a retention policy on
  `core.SettingsHistory` is wanted it is a `migrationBuilder.Sql` statement (believed true; not
  re-verified today).
- Whether hosted MCP clients (Claude, Codex) send `Accept-Language` at all (assumed not; the
  fallback chain handles either).
