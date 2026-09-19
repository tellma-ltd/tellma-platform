# T3 — Tenant settings, localization, and the version cache (future spec 0012)

Design optimized for Tier-2 performance and operations: round trips, bulk shapes, plan-cache
stability, lock duration, cross-instance cache correctness, and day-one observability. Every
question in the theme is answered; where a trade-off conflicts with distro-author simplicity or
with long-term maintainability, the performance and operations reading wins and the alternative
is recorded as a review flag.

The headline numbers this design commits to, for a warm instance:

| Operation | Round trips | Statements this theme adds to the batch |
|---|---|---|
| Any read (query, details) | 1 | one `SELECT` over `core.VersionTags` (≤ 1 page) |
| Any save | 2 | the same read in round trip 1; one tag-bump `UPDATE` and one guard `IF` in round trip 2 |
| First request of a tenant on a fresh instance | 2 | the settings load (three statements) rides round trip 1 |
| First request of a user on a warm tenant | 2 | the user-settings load rides round trip 1 |
| Settings edit | 2 | as save; the reload rides the response round trip |

No tag or setting is ever read in a round trip of its own; no cache is ever invalidated by a bus,
a timer, or another instance — the database row read on every request is the only invalidation
channel, and the in-process cache is a pure accelerator on top of it.

---

## 1. Critique

**The general design is right and mostly proven.** Opaque per-kind version tags read at connect
time, compared against an in-process cache, bumped by every write, with a hardcoded shape
discriminator, is exactly the shape the previous Tellma ran for years (`dbo.Settings` carried
`SettingsVersion`, `DefinitionsVersion`, `SchedulesVersion` as `UNIQUEIDENTIFIER DEFAULT NEWID()`;
`dbo.Users` carried `PermissionsVersion` and `UserSettingsVersion`). The brain dump keeps what
worked. The problems are in the details.

**1.1 The tags sit on temporal rows.** `Settings.SettingsVersion` on the temporal `core.Settings`
row, and `UserSettingsVersion`/`PermissionsVersion`/`LastActive` on the temporal `core.User` row,
each write a history row for every bump — SQL Server writes a history row on every `UPDATE` even
when nothing changed. `LastActive` alone would produce one history row per user per request. The
brain dump notices this ("needs to be moved to its own table") but leaves both placements in the
sketch. The tags must not live on any temporal row; they belong in narrow non-temporal tables read
by primary key.

**1.2 "Guaranteed at framework level (how?)" is the load-bearing gap.** The brain dump asks for the
guarantee and offers no mechanism. Without one, every new cacheable entity is a latent stale-cache
bug that surfaces on the second app instance only. The mechanism has to be structural: the thing
that writes rows (the batch executor) is the thing that bumps tags, from a registry that maps
tables to tag keys, so a service author cannot forget — and a test-tier audit must prove that no
write path escaped it.

**1.3 "Before executing any API call, the versions are read from the DB" costs a round trip per
request.** Read literally, that is one extra round trip on every operation, doubling the read
path. The tag read must ride the first business round trip and be compared afterwards, with a
per-kind policy for what a mismatch means (re-run for permissions; serve-and-refresh for
settings; refresh-only for cached entities).

**1.4 The settings model conflates three axes.** `PrimaryLanguage` (which `Name` columns exist and
how they are labelled), the UI/message language (which resource satellite renders an error), and
the formatting culture (`ar-SA` vs `ar-EG` digits, separators, default calendar) are three
different things with three different owners: tenant, user, user. The brain dump's own open
question ("language axis vs culture axis?") is the symptom. Content languages are language tags
(`ar`); the UI language and the formatting culture are per-user preferences with tenant defaults.

**1.5 The fallback chain "request culture → tenant primary → English" is wrong for resources.** A
Spanish-speaking user at an Arabic tenant with a missing Spanish string should see English (the
neutral resource every string is guaranteed to have), not Arabic. The tenant-primary hop is
correct for *content* (`Name3` absent → `Name`), not for *resources*. Two chains, not one.

**1.6 "The supplied culture headers are ignored if they are not one of the tenant's languages"
throws away a legitimate case.** A tenant whose content languages are `en`/`ar` can perfectly well
serve an Amharic UI to a user if the distribution ships Amharic resources; the content columns are
unaffected. The restriction should apply to *content language selection* (which `Name` column to
prefer), never to the UI language.

**1.7 "Metaversion" is a hand-maintained constant that will be forgotten.** A per-cache-kind
constant bumped "when the shape of the cached item has changed due to a new deployment" is a
discipline, and disciplines fail silently. The in-process cache dies with the process, so the
constant only protects caches that outlive a deploy — the SPA's. Folding the deployment's build
identity into the wire tag gives the same protection with zero maintenance, at the cost of one
refetch per user per deploy, which is trivial.

**1.8 The settings tables have no criterion for what goes where**, and the key-value table has no
name, no key grammar, no type discipline, and a `Category` column that is derivable from the key.
A key-value table without a code-declared definition (type, default, visibility, validation) is a
`Text1`/`Text2` in disguise — the thing the architecture set out to kill.

**1.9 `core.Settings.TenantId` is the right idea for the wrong reason.** In a one-database-per-tenant
model the column is not a partition key; it is a **routing guard**: the catalog entry for tenant
12 must land on tenant 12's database, and a restored-to-the-wrong-name backup or a catalog typo is
a cross-tenant exposure. It should be the primary key of the single row and checked on every
settings load.

**1.10 Cacheable entities need a qualification rule, not a hunch.** "Up to 50–100 records?" is not a
contract. A cacheable type must be declared once, bounded by a row cap the loader enforces, be
public-read without a row-level filter (the cache is shared across users), have no child
collections, and bump its own tag on every write. A type that exceeds its cap must degrade loudly,
not silently serve one user's stale list to another.

**1.11 The calendar header question has a hard answer the brain dump does not yet know.** No
standard header exists; `-u-ca-` extensions in `Accept-Language` produce mixed-calendar output on
.NET 10 and collapse the culture's parent; `DateTimeFormatInfo.Calendar` refuses both a custom
`Calendar` and any calendar outside the culture's `OptionalCalendars`. Calendar-aware formatting is
platform code (a ported Ethiopian calendar; Umm al-Qura through a platform renderer under non-Arabic
cultures), carried in its own header, and never routed through `CultureInfo`.

**1.12 `today()` is already decided by spec 0008** as the current date in the tenant's zone, bound
by the host. An `X-Today` header would make two callers of the same tenant disagree about the
company's day. The tenant time zone belongs in `core.Settings`; a user-level zone is a display
preference only.

**1.13 What is missing entirely:** the shape of the tag read statement, the shape of the bump
statement and where it sits relative to `COMMIT` (it holds an exclusive lock on the tag row until
commit, so it must be the last thing before it), the stampede story for the moment a tag flips on
a busy instance (one loader per key per instance), a byte-bounded size story for entity caches,
the meters, and the decision between `HybridCache` and a private `MemoryCache`.

---

## 2. Decisions

### D1 — Vocabulary: "version tag", `Guid`, and the build-qualified wire tag

**Decision.** The concept is a **version tag**: an opaque `uniqueidentifier` compared only for
equality. Columns end in `Version` (`SettingsVersion` is the column name pattern, `Version` the
value column of the key-value table). The C# type is `System.Guid`, generated by the database
(`NEWID()`) in the bump statement. On the wire a tag is the string
`"{build}.{guid:N}"`, where `build` is the deployment's build identity (`DeploymentIdentity.Build`,
a value the composition root supplies from the entry assembly's informational version, qualified
with the process start time in the Development environment). "Metaversion" does not exist; the
build segment is what makes a client-held cache miss after a deploy.

**Rationale.** Rails, Django, and `HybridCache` all call this a *version*; HTTP reserves *ETag* for
representation validators (the blob endpoint of the record-plus-blobs theme uses ETags properly).
*Fingerprint* implies a content hash, which this is not. A random `Guid` needs no coordination
across instances, cannot be re-reached after a backup restore (a monotonic `bigint` can, and then
reads as "unchanged" once), and `rowversion` bumps on bookkeeping writes and so cannot be selective.
The build segment replaces a hand-maintained constant per cache kind: one refetch of settings,
permissions, user settings, and lookup lists per user per deploy is a few hundred kilobytes, while a
forgotten constant is a shape mismatch in a client-side cache that is very hard to diagnose.

**Rejected.** `rowversion` (bumps on any update; database-wide counter; unsuitable for temporal
rows); monotonic `bigint` (restore hazard; needs a sequence round trip or a row increment; ordering
is not needed anywhere in this theme); Guid v7 (ordering is irrelevant and it is not sequential in
SQL Server byte order anyway); a per-kind `FormatVersion` constant (discipline-dependent).

**Confidence.** High. **Review flag:** folding the build identity into every wire tag means every
deploy invalidates every client-side cache once; a distribution deploying many times a day pays
that many refetches per active user. The alternative — per-kind constants — is cheaper on the wire
and riskier in practice.

### D2 — Where tags live: `core.VersionTags` (tenant level) and `core.UserVersions` (user level)

**Decision.** Two non-temporal tables:

- `core.VersionTags ([Key] varchar(100) PK, [Version] uniqueidentifier NOT NULL)` — one row per
  tenant-level tag key. Reserved keys: `settings` (any write to `core.Settings` or
  `core.SettingsEntries`), `permissions` (any write to `core.Roles` or `core.Permissions`),
  `entities` (aggregate: bumped whenever any `entity:*` key is bumped), and `entity:<EntityName>`
  for every cacheable entity type (`entity:Country`). Keys are lower-case ASCII; the `entity:`
  prefix is reserved for the cacheable-entity capability; a feature may register further keys
  (`definitions`, `schedules`) through `IVersionTagRegistry`.
- `core.UserVersions ([UserId] int PK FK → core.Users, [UserSettingsVersion] uniqueidentifier,
  [PermissionsVersion] uniqueidentifier, …)` — one row per user, non-temporal, inserted together
  with the user row. The users-roles-permissions theme owns the table's remaining columns
  (`LastActiveAt`, inbox watermarks) and may rename the table; the contract this theme needs is:
  non-temporal, keyed by `UserId`, two `uniqueidentifier` tag columns with the names above, read
  by the connect statement in the same round trip as the user lookup.

**Rationale.** A key-value shape is the only one that admits an open set of keys (every cacheable
entity type a distribution adds gets a row without a migration on a platform table). The whole
table is ≤ a few dozen rows and lives on one page; reading all of it is one clustered scan, cheaper
than reading three named rows, and it lets every response carry every tag. The user row is separate
because user-level tags are bumped per user (a membership change) and read by primary key on
connect; putting them in the tenant table would make that read a second seek and would multiply the
tenant table by the user count.

**Rejected.** Tags as columns on `core.Settings` / `core.Users` (temporal churn, §1.1); a single
wide `core.TenantVersions` row (cannot hold an open set of entity keys); per-user permission tags
bumped by *role* writes (a fan-out `UPDATE` over every member inside the role-save transaction,
holding N row locks; tenant-level `permissions` plus user-level membership tags gives the same
precision without the fan-out — see D4 for how the two combine).

**Confidence.** High.

### D3 — Who bumps, and how the framework guarantees it

**Decision.** The batch executor bumps tags; services never do. Every statement added to a batch
declares the tables it writes (the save emitter derives the set from the entities it persists; a
raw SQL statement declares it explicitly through `SqlStatement.Writes`). At execution time the
executor resolves the union of written tables against the `IVersionTagRegistry` (populated at
composition: table → tenant keys; table + user-id column → user-level column) and appends, **as the
last statements before `COMMIT`**, exactly two statements:

```sql
-- @tm_tags : [StringList] — the distinct tenant keys derived from the batch's declared writes
UPDATE t SET t.[Version] = NEWID()
FROM [core].[VersionTags] AS t
INNER JOIN @tm_tags AS k ON k.[Id] = t.[Key];

-- only when a user-scoped rule fired; @tm_users : [IdList] — distinct user ids from the written rows
UPDATE uv SET uv.[PermissionsVersion] = NEWID()
FROM [core].[UserVersions] AS uv
INNER JOIN @tm_users AS u ON u.[Id] = uv.[UserId];
```

followed by the tag read of D4 (so the response carries post-bump values). Rules:

- **Placement.** The bump `UPDATE` takes an exclusive lock on each touched `VersionTags` row until
  commit; two concurrent saves of the same cacheable table serialize on that row for the remainder
  of their transactions. Emitting the bump last bounds that duration to the executor's own trailing
  statements, never to business statements or to any client I/O (there is none inside a batch).
- **One statement, fixed shape.** Keys travel as a `[StringList]` TVP so the statement has one plan
  regardless of how many keys a batch bumps; a batch bumps at most a handful.
- **Aggregate.** When any `entity:*` key is in the set, the executor adds `entities` to it.
- **Missing rows never fail silently.** A key with no row cannot be bumped by an `UPDATE`. Rows are
  created (a) by the migrator on every run per tenant database, from the registry
  (`INSERT … WHERE NOT EXISTS`), and (b) by the connect path: the per-request tag read returns
  every row, the executor compares the returned keys against the registry, and a missing key is
  inserted once (one extra round trip, logged at Warning as `VersionTagRowMissing`). Until the row
  exists the cache kind behind the key is treated as *uncacheable* (every request loads), so a
  missing row costs performance, never correctness.
- **The test tier proves the guarantee.** On the LocalDB fixture database, change tracking is
  enabled on every table; the executor in test mode compares `CHANGETABLE(CHANGES …)` since the
  batch's start against the batch's declared write set after every batch and fails the test on any
  undeclared write. This is the mechanism that turns "every write bumps its tag" from a promise
  into a failing build.
- **Bounded staleness backstop.** Every cache entry also carries `LoadedAt`; entity caches expire
  after `MaxAge` (default 15 minutes) regardless of tag, so a write that bypasses the executor
  (direct `DbContext.SaveChanges` in distro code, an operator's SQL) is stale for at most that
  long. Settings and permissions have no `MaxAge` — their tag is read on every request anyway.

**Rationale.** The only place that knows every table a round trip writes is the executor; making
it the bumper removes the class of bug the brain dump fears. The change-tracking audit exists
because a registry rule that is never exercised is as good as absent.

**Rejected.** Triggers (logic in the database); SQL Server change tracking as the *primary* tag
(database-wide, needs per-table enablement and retention on every tenant database, and a per-table
"current version" is a scan of the change table); `NEXT VALUE FOR` counters; bumping from the
service layer by convention.

**Confidence.** High on the mechanism; medium on the CI audit's cost (change tracking on a fixture
database is cheap, but it is one more moving part in the data-layer test fixture — the data-access
theme owns that fixture and must agree).

### D4 — Connect-time reads, the optimistic collapse, and per-kind staleness policy

**Decision.** Every batch that is the first round trip of a request carries, after the connect
statement of the users theme, the tenant tag read:

```sql
SELECT [Key], [Version] FROM [core].[VersionTags];
```

The user-level tags ride the connect statement's own result (`uv.UserSettingsVersion,
uv.PermissionsVersion`). The request-scoped `IVersionTagSnapshot` is populated from these result
sets. Business statements in the same round trip were compiled against the caches *as they were
before the round trip*; after it the executor compares the snapshot with the version each cache
entry was loaded under and applies the kind's policy:

| Cache kind | Validated against | On mismatch after a read round trip | On mismatch after the first round trip of a save |
|---|---|---|---|
| `settings` (tenant settings, derived cultures, Queryex schema variant) | `settings` | Serve the result; reload before the next use in this request; response carries the new tag | Reload, re-run validation that read settings |
| `permissions` (a user's effective permission set) | `permissions` **and** `UserVersions.PermissionsVersion` | Discard the result, reload, re-run the round trip (`tellma.versions.reruns`) | Reload, redo the RLS pre-check, continue |
| `user_settings` | `UserVersions.UserSettingsVersion` | Serve; reload lazily; response carries the new tag | Same |
| `entity:<Name>` | its own key (and `entities` on the wire) | Invalidate the entry; the result came from the database anyway | Same |

For a save, the persist round trip additionally carries a **fresh-tag guard** inside the
transaction, before any write, for the kinds the pipeline declared it depends on:

```sql
IF EXISTS (SELECT 1 FROM [core].[VersionTags]
           WHERE [Key] = N'permissions' AND [Version] <> @tm_expected_permissions)
    THROW 50012, N'STALE_VERSION_TAG:permissions', 1;
```

The executor maps error 50012 to `StaleVersionTagException(key)`; the pipeline reloads and retries
the persist once. This closes the window between a permission revocation and a commit at the cost
of one clustered seek.

**Round-trip ledger** (the numbers the pipeline theme should quote):

- Warm read: 1 round trip (connect + tags + queries).
- Warm save: 2 (connect + tags + validation context; persist + bumps + guard + tag read + echo).
- Fresh instance, first request for a tenant: the connect batch cannot compile RLS without
  permissions and cannot negotiate content language without settings, so round trip 1 is
  connect + tags + settings load + user-settings load + permission load (all keyed off the
  subject in SQL, no ids needed from C#), round trip 2 is the business call. 2 for a read, 3 for a
  save. No request ever pays more than one extra round trip for cold caches.
- A `permissions` mismatch after a warm read: +1 round trip, once per instance per role change.

**Failure modes named.** (1) Deactivated user: the connect row says `IsActive = 0`; the executor
discards the business result and the pipeline returns 403 — the query ran, but nothing leaves the
process. (2) Stale permissions on a read: re-run; the stale result is never returned. (3) Stale
permissions between a save's two round trips: the guard throws inside the transaction; nothing is
written. (4) Settings changed mid-request: physical columns never disappear (Name2 gating is
logical), so a compiled query stays valid; the client is told the new tag and refetches settings.
(5) Misrouted tenant database: the settings load (D7) compares `core.Settings.TenantId` with the
routed tenant id and fails closed with `TenantIdMismatch` at Critical.

**Rationale.** Reading tags in their own round trip doubles the read path; reading them in the same
round trip and comparing afterwards costs one page read and, in the rare mismatch, one re-run.
Two-level permission validation (tenant key for role definitions, user key for memberships) avoids
the fan-out `UPDATE` of D2 while keeping membership changes precise.

**Confidence.** High. **Review flag:** the fresh-tag guard is a per-save seek that most deployments
will never need; it can be an opt-in of the pipeline (`RequireFreshTags`) rather than a default.

### D5 — Cache infrastructure: private `MemoryCache` per kind, single-flight loads, meters

**Decision.** `Tellma.Core` ships `VersionedMemoryCache<TValue>`: one **private**
`Microsoft.Extensions.Caching.Memory.MemoryCache` instance per cache kind (`settings`,
`user_settings`, `permissions`, `entities`), never the DI-registered `IMemoryCache`, with:

- **Key** `(tenantId, key)`; for `user_settings`/`permissions` the key is the user id, for
  `entities` the entity name, for `settings` a constant.
- **Entry** `CachedValue<TValue>(Guid Version, TValue Value, DateTimeOffset LoadedAt)` — immutable;
  the version it was loaded under is compared with the request snapshot on every access; a mismatch
  is a miss.
- **Single-flight.** A `ConcurrentDictionary<(tenantId, key), Task<CachedValue<TValue>>>` of
  in-flight loads; `GetOrAdd` with a lazily started task so concurrent misses for one key on one
  instance run one loader and share its result; the entry is removed when the task completes; a
  faulted task is removed before its exception propagates so the next caller retries. Across
  instances there is no coordination (N instances → N loads per flip; N is small).
- **Sizing.** `SizeLimit` is set on every kind and every entry sets `Size`: entry count for
  `settings`/`user_settings`/`permissions` (defaults 2 048 / 32 768 / 32 768 entries), **row count**
  for `entities` (default 1 000 000 rows, so ~500 MB at 500 bytes a row in the worst case) — the
  only unit that bounds memory for lists of unknown width without serializing them. Compaction is
  the runtime's (expired → priority → least recently used), which is the bounded LRU the brain dump
  asks for; idle tenants age out first. `MaxAge` per kind (D3) uses absolute expiration.
- **Statistics.** `TrackStatistics = true` on every instance; `GetCurrentStatistics()` feeds
  observable gauges (verified: it returns null unless `TrackStatistics` is set).
- **Not `HybridCache`.** It serializes every write even with no L2, hands mutable values out as
  fresh deserialized copies per read, shares the DI `IMemoryCache` (which cannot be size-limited
  without breaking every framework consumer that does not set `Size`), and its tag stamps are
  process-local timestamps that cannot be validated against a database value. Its stampede
  protection is the one feature this design re-implements, in ~40 lines.

**Meters** (`IMeterFactory`, meter `Tellma.Core`, names as `const`s in
`Tellma.Core.Abstractions.Caching.CacheTelemetryNames`; no tenant tag anywhere):

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `tellma.cache.requests` | Counter | `{request}` | `cache.kind` (`settings`\|`user_settings`\|`permissions`\|`entities`), `cache.outcome` (`hit`\|`miss`\|`stale`\|`rejected`) |
| `tellma.cache.load.duration` | Histogram | `s` | `cache.kind` |
| `tellma.cache.entries` | ObservableGauge | `{entry}` | `cache.kind` |
| `tellma.cache.size` | ObservableGauge | `{unit}` | `cache.kind` (entries or rows, per D5 sizing) |
| `tellma.cache.evictions` | Counter | `{entry}` | `cache.kind`, `cache.eviction.reason` (`capacity`\|`replaced`\|`expired`) |
| `tellma.cache.entity.rows` | Histogram | `{row}` | `cache.entity` (entity name — a closed set per deployment) |
| `tellma.versions.bumps` | Counter | `{bump}` | `version.key.kind` (`settings`\|`permissions`\|`entities`\|`entity`\|`user_settings`\|`user_permissions`) |
| `tellma.versions.reruns` | Counter | `{rerun}` | `cache.kind` |

`cache.outcome = rejected` and `tellma.cache.entity.rows` answer "was this entity right to cache":
a type whose list exceeds its cap is rejected on every load, and the histogram shows how far each
type sits from its cap.

**Rationale.** The brain dump's requirements (bounded per kind, stampede-safe, metered, validated
against a database tag on every request) map onto a private `MemoryCache` with a single-flight
guard and onto nothing `HybridCache` adds. The alert queries backing these instruments go under
`infra/monitoring/` and are cross-checked by the existing name test.

**Confidence.** High. **Review flag:** `HybridCache` would be the framework-blessed choice and
would arrive with an L2 story; the review digest already marks this as a flag.

### D6 — Cacheable entities: declared once, bounded, public-read, batch-participating

**Decision.** An entity becomes cacheable by one declaration on its feature contribution
(`feature.Entity<Country>().Cacheable(maxRows: 500)`) or by `[Cacheable(MaxRows = 500)]` on the
class (fluent wins). The declaration:

1. registers `entity:Country` with `IVersionTagRegistry` (row seeded by the migrator; self-healed
   on connect) and the write rule `[core].[Countries] → entity:Country`;
2. registers the `entities` cache kind entry `Country` with `MaxRows`;
3. requires, at the startup validation gate, that `Country` is top-level, has no child collections,
   and that its `read` securable declares `SupportsFilter = false` (the users theme's registry) —
   a cached list is shared by every user, so a row-level filter on it would be silently ignored;
4. contributes nothing to the web surface — the client's lookup endpoint and its cache are the web
   theme's, driven by the `entities` tag on the `Tellma-Versions` response header.

Qualification rule, stated for distro authors: *small* (`MaxRows` ≤ 5 000, default 500),
*reference* (changes a few times a month), *flat*, *public*. Users, roles, and anything with RLS
are not cacheable; their display names reach the client through the details page's related-entity
dictionary.

Load path: `IEntityCache.GetAsync<Country>()` in service code, or `batch.FromCache<Country>()` when
the consumer is already building a round trip (validation context, import translation). The batch
form checks the cache first; only a miss adds the statement — a Queryex query over the entity's
root with all scalar properties, `Take = MaxRows + 1`, ordered by key — and the result populates
the cache before the consumer sees it, so a cold lookup costs no round trip of its own. When
`MaxRows + 1` rows come back the list is **not cached**: the consumer still receives it, the load
is metered as `rejected`, and `CacheableEntityOverflow` is logged once per type per process. Above
a hard ceiling of 20 000 rows the load throws — a "cacheable" type of that size is a configuration
bug, not a runtime condition.

The cached shape is `CachedEntitySet<TEntity>`: `ImmutableArray<TEntity> Items` in key order plus
`FrozenDictionary<int, TEntity> ById`, all rows including inactive ones (consumers filter). Cached
entities are shared instances; mutating one is a bug (the analyzer that polices ordinal binding can
grow a rule for this later).

**Rationale.** Every effect of "cacheable" is derived from one declaration, so the tag bump cannot
be forgotten (D3), and the load rides whatever round trip the consumer already needed. Row-count
sizing is the only bound that costs nothing to compute.

**Rejected.** Caching per language (entities carry all three names; consumers pick); caching
post-RLS per user (defeats sharing; RLS-bearing types are simply not cacheable); a global "all
entities under N rows are cached" heuristic (unbounded and undeclared).

**Confidence.** High.

### D7 — The settings tables: typed single row plus code-defined key-value entries

**Decision.** Two temporal tables and one criterion.

`core.Settings` — one row, `TenantId` is the primary key (the routing guard of §1.9), system
versioned, with the four audit columns. It holds only what the platform reads on every request or
what has no sensible default: the tenant's display names, the up-to-three content languages, the
up-to-two calendars, the time zone, and the default formatting culture. Nothing else. A
distribution may extend the leaf with typed columns (leaf-only mapping allows it) but is told not
to: typed columns need a migration and are invisible to the generic settings edit API.

`core.SettingsEntries` — one row per *set* key: `Id` (from `sq_SettingsEntries`), `Key`
(`varchar(200)`, unique), `Value` (`nvarchar(max)`, JSON), audit columns, system versioned. There is
no `Category` column: the first key segment *is* the category (`general.`, `gl.`, `hr.`,
`<slug>.`), and the settings load reads the whole table, so no query ever filters by it. An absent
row means "the default". Every key is declared in code (D8); an undeclared key in the table is a
Warning at load and is ignored.

**Criterion.** Typed column when the value is read by the platform on the request path or is
required with no default; key-value entry otherwise — including everything a module, industry,
compliance, or distribution pack owns, because C# single inheritance cannot compose one
`Settings` leaf from several packs and per-pack single-row tables would add a result set per pack
to every settings load.

**Rationale.** The settings load is one round trip of three tiny statements whatever the entry
count, cached whole under one tag; the typed row keeps the hot fields strongly typed and
non-nullable; entries keep pack settings migration-free. Temporal on both is free (they change a
few times a year) and gives "who changed the fiscal year start" for nothing, precisely because the
tag left the row.

**Rejected.** Everything in key-value (the request path would parse JSON for the time zone on every
cold load and lose `NOT NULL`); everything typed (packs cannot contribute columns); a `Category`
column (derivable, unused by any query); `SavedById` + period as the audit vocabulary (four columns
everywhere is the vocabulary position of this theme; system versioning is additive).

**Confidence.** High on the split; medium on the exact typed column list (`DefaultCulture` is the
one most likely to move — review flag in D10).

### D8 — Setting definitions, the two DTOs, and the read path

**Decision.** A setting is declared once as a static `TenantSettingDefinition<T>` in the owning
package's `.Abstractions` (`GlSettings.FiscalYearStartMonth = new("gl.fiscalYearStartMonth",
Default: 1, IsClientVisible: true, Validate: m => m is >= 1 and <= 12 ? null : "…")`). `T` is one
of `bool`, `int`, `long`, `decimal`, `string`, `string[]`, `DateOnly`, `Guid`, or any `T` with an
explicit `JsonTypeInfo<T>` (source-generated by the declaring package). Definitions are collected
at composition into `ISettingDefinitionRegistry`; a duplicate key or a malformed key
(`^[a-z][A-Za-z0-9]*(\.[a-z][A-Za-z0-9]*)+$`, ≤ 200 chars) fails the startup gate.

Two DTOs:

- `TenantSettings` (server): immutable record with the typed columns, `Entries`
  (`FrozenDictionary<string, JsonElement>`), `T Get<T>(TenantSettingDefinition<T>)` (parsed once and
  memoized per entry), and the **derived members computed once per load**: `Languages`
  (`TenantLanguage[]` with index, tag, symbol, native name, direction), `Calendars`, `TimeZone`
  (`TimeZoneInfo`), `SqlTimeZoneName` (the Windows id spec 0008's `TimeZone` slot needs, converted
  through `TimeZoneInfo.TryConvertIanaIdToWindowsId` at load), `DefaultCulture` (`CultureInfo`),
  `Version`.
- `TenantSettingsForClient`: the client-visible projection — names, languages (tag, symbol, native
  name, direction), calendars, time zone id, default culture tag, client-visible entries, and the
  wire `Version`. It is materialized **once per tag change as pre-serialized UTF-8 JSON** and
  cached beside `TenantSettings`; the endpoint writes the bytes and never serializes on the hot
  path. A caller that sends its current tag receives an empty "unchanged" result.

Read path (`ITenantSettingsProvider.GetAsync`): compare the request snapshot's `settings` tag with
the cached entry → hit returns the instance. Miss → single-flight load, one round trip, **tag read
first**:

```sql
SELECT [Version] FROM [core].[VersionTags] WHERE [Key] = N'settings';
SELECT [TenantId], [Name], [Name2], [Name3],
       [PrimaryLanguage], [SecondaryLanguage], [TernaryLanguage],
       [PrimaryCalendar], [SecondaryCalendar], [TimeZone], [DefaultCulture],
       [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]
FROM [core].[Settings];
SELECT [Id], [Key], [Value] FROM [core].[SettingsEntries];
```

The tag is read *before* the rows: if a bump lands between the two statements the loaded rows are
newer than the tag and the next request's comparison reloads (convergent); reading the rows first
would let a bump land after them and mark stale data fresh until the next bump (the classic
stale-forever ordering bug). The loader asserts `TenantId == routed tenant id` and fails closed.
When the settings load rides a connect batch (D4), the same three statements are appended in the
same order.

**Rationale.** Definitions give the key-value table the type discipline of a column with none of a
column's migration cost; pre-serializing the client DTO removes JSON work from every settings
request; the derived members turn per-request `FindSystemTimeZoneById`/`GetCultureInfo` calls into
one computation per tenant per change.

**Confidence.** High.

### D9 — The settings edit API shape (position for the reference-stacks spec)

**Decision.** One endpoint, `POST /api/web/{tenantId}/settings/save`, taking a **partial** DTO: the
typed columns as nullable members plus `Entries` (`Dictionary<string, JsonElement?>`) plus a
`Fields` list naming which typed members and which entry keys the request sets (field-mask
semantics — absent means untouched, a listed member set to null means "clear", which is what makes
`SecondaryLanguage = null` expressible). The permission resource is `settings.<category>`
(`settings.general` covers the typed row; `settings.gl` covers `gl.*` entries); a request touching
two categories needs both. Validation runs the definition validators and the cross-field rules of
D10; the persist is one batch (`UPDATE core.Settings …`, then per entry `UPDATE … WHERE [Key]=@k`
and an `INSERT` for keys with no row using allocator ids), the executor bumps `settings`, and the
response carries the reloaded `TenantSettingsForClient`. The concurrency stamp is
`core.Settings.ModifiedAt` for the typed row and per-entry `ModifiedAt` for entries.

**Rationale.** The settings row will grow to dozens of fields; load-and-save-back would make every
save a full-row write and a full-row concurrency conflict; JSON Patch brings an operation-based
attack surface and in-place mutation with caller-side rollback; merge patch cannot express "set to
null". A typed partial DTO with a field list is what both dominant API style guides converge on.

**Confidence.** Medium (the endpoint is the reference-stacks theme's to finalize).

### D10 — Three axes: content languages, UI language, formatting culture

**Decision.**

- **Content languages** are BCP-47 *language* tags (`en`, `ar`, `am`) chosen from the Core language
  catalogue (D13); `core.Settings.PrimaryLanguage` is required, `SecondaryLanguage` and
  `TernaryLanguage` optional and distinct; `Ternary` requires `Secondary`. They govern which
  `Name2`/`Name3` columns exist to the language (D14), how their labels read, and nothing else. The
  catalogue is validated by `CultureInfo.GetCultureInfo(tag, predefinedOnly: true)` at startup, so
  every content language is also a valid formatting culture, but the two uses stay separate.
- **UI language** is a per-user preference (`UserSettings` key `ui.culture`, seeded from the identity
  server's `locale` claim at user creation) constrained to the **distribution's shipped UI
  languages** — declared once in composition (`localization.UiLanguages("en", "ar")`) and matched
  against the satellites present at startup (a declared language whose Core satellite is missing
  fails the gate, which catches a `SatelliteResourceLanguages` mismatch). English is always
  shipped. **A request's UI language does not have to be a tenant content language.**
- **Formatting culture** (numbers, separators, default date pattern) is a per-user preference
  (`ui.culture` carries the full tag, `ar-EG`) defaulting to `core.Settings.DefaultCulture`, a
  required specific culture (`ar-SA`, `en-SA`, `am-ET`). It is unconstrained by the shipped set:
  formatting works for any predefined culture.
- **Calendar** is a per-user preference (`ui.calendar`) constrained to the tenant's
  `PrimaryCalendar`/`SecondaryCalendar`, which are constrained to the calendar systems the
  distribution composed (D12).
- **Time zone** is a tenant setting (`core.Settings.TimeZone`, IANA id) and binds `today()` and the
  `TimeZone` slot. A user-level display zone is deferred.

**Rationale.** The three axes have three owners; conflating them either blocks legitimate UI
languages or lets a content language silently change formatting. A separate `DefaultCulture` is
needed because `ar` alone does not say Saudi or Egyptian digits.

**Confidence.** High on the axes; medium on `DefaultCulture` as a typed column. **Review flag:**
`DefaultCulture` could derive from `PrimaryLanguage` plus a country code, or live as an entry
(`general.defaultCulture`); keeping it typed and required is the safer-for-formatting choice.

### D11 — Request negotiation: precedence, headers, extension stripping, effective values

**Decision.** The tenant endpoint filter of the host theme populates the scoped `ILocalizationContext`
once per request from these inputs, in this precedence (GitHub's `Time-Zone` precedent):

| Value | 1st | 2nd | 3rd | 4th |
|---|---|---|---|---|
| UI culture (resources) | `Accept-Language` ranges, up to 3 by q-value, matched to shipped UI languages by language prefix after stripping `-u-`/`-x-`/`-t-` extensions | user `ui.culture` | tenant `DefaultCulture`'s language | `en` |
| Formatting culture | the winning `Accept-Language` tag if `GetCultureInfo(tag, predefinedOnly: true)` succeeds | user `ui.culture` | tenant `DefaultCulture` | `en` |
| Calendar | `Tellma-Calendar` header (a code ∈ tenant calendars) | user `ui.calendar` | tenant `PrimaryCalendar` | — |
| Time zone | tenant `TimeZone` | — | — | — |
| `today()` | `TimeProvider.GetUtcNow()` converted to the tenant zone, date part | — | — | — |

`Accept-Language` sits first because the SPA sets it explicitly to the user's chosen UI language on
every call (it is not a forbidden request header, so `fetch` may set it) and non-browser clients
(MCP, scripts) fall through to the stored preference. An unsupported or malformed header value is
ignored, never rejected: the response carries `Content-Language` and `Tellma-Calendar` with the
*effective* values so a client that drifted (the admin removed the secondary calendar) keeps working
and can correct itself. `-u-ca-` names are never handed to `CultureInfo`.

The filter sets `CultureInfo.CurrentCulture`/`CurrentUICulture` from the context so `ToString()`
and `IStringLocalizer` see the right values; the scoped context stays the source of truth.
Background scopes are populated the same way from the copied request context (or, for
system-scheduled work, from tenant defaults) by the background-tasks theme's job runner.

**Confidence.** High. **Review flag:** `Accept-Language` above the stored preference is the
GitHub-style "explicit beats stored" reading; the reverse ("stored beats ambient browser language")
protects a user whose browser sends an unexpected OS language to a non-SPA endpoint.

### D12 — Calendars and the time zone

**Decision.** `Tellma.Core.Abstractions.Globalization.ICalendarSystem` is the calendar contract
(code, arithmetic through a `System.Globalization.Calendar`, month names per culture, date
formatting by style, supported range). Core registers `gc`; `Tellma.Locale.Sa` registers `uq`
(over `UmAlQuraCalendar`, which ends at AH 1500 / 2077-11-16 — beyond that range the system formats
through `HijriCalendar` with `HijriAdjustment = 0` and the platform's own renderer, documented as
tabular approximation); `Tellma.Locale.Et` registers `et` (a port of the previous Tellma's
`EthiopianCalendar`: Julian-day conversion, 13 months, `year % 4 == 3` leap rule, single era), since
.NET has no Ethiopic calendar and a custom `Calendar` cannot be assigned to
`DateTimeFormatInfo.Calendar` on any culture. The codes are the same three spec 0008 reserves
(`gc`, `uq`, `et`); `CalendarCodes` in Abstractions is the single source and the Queryex adapter
asserts agreement at startup. Calendar-aware rendering is **platform code** (the system's own
pattern renderer over month names, era text, and the culture's digits), never `DateTime.ToString`
with a culture — which cannot render Umm al-Qura under an English UI or Ethiopic at all.

The tenant time zone is stored as an IANA id, validated and normalized with
`TimeZoneInfo.TryFindSystemTimeZoneById`, and converted once per settings load to the Windows id
the backend's `AT TIME ZONE` needs. Both conversions require ICU globalization; the startup gate
refuses invariant or NLS mode (the documented `SortVersion` probe). App-local ICU
(`Microsoft.ICU.ICU4C.Runtime`) is **not** used: the package's latest release is 72.1.0.3 from
October 2023 and would pin the fleet to stale CLDR; the OS/container ICU is used and the container
base image is pinned instead.

**Confidence.** High on the shape; medium on the Umm al-Qura overflow handling (review flag:
rejecting dates beyond AH 1500 with a diagnostic is the honest alternative).

### D13 — Resources, ICU MessageFormat, the language catalogue, and the fallback chains

**Decision.**

- **Resource organisation.** `.resx`, one `Resources/Strings.resx` per package plus one
  `Strings.<culture>.resx` per translated language, addressed through `IStringLocalizer<Strings>`
  where `Strings` is a marker class in the package's `Resources` namespace (base name
  `<Package>.Resources.Strings`, which is what the folder-and-file convention yields with no
  `ResourcesPath`). Core's satellites ship with `Tellma.Core`; a distribution trims what it deploys
  with `<SatelliteResourceLanguages>` matching its declared UI languages. Modules reference
  `Microsoft.Extensions.Localization.Abstractions` (a framework package) for `IStringLocalizer<>`;
  `Tellma.Core.Abstractions` stays framework-free.
- **ICU.** The platform registers `IcuStringLocalizer<>` as the open-generic `IStringLocalizer<>`
  (after `AddLocalization()`, so it wins) — the decorator already in the identity server, moved into
  `Tellma.Core` with two changes: one **singleton** `MessageFormatter` (its `ConcurrentDictionary`
  pattern cache is safe to share; the probe ran 16 threads × 50 000 formats with zero errors; a
  `ThreadLocal` copy per thread multiplies memory and cold-starts every thread) and **calendar-aware
  argument pre-formatting**: `DateOnly`, `DateTime`, `DateTimeOffset`, and `LocalizedDate(value,
  DateStyle)` arguments are rendered through the request's `ICalendarSystem` and formatting culture
  *before* the pattern is formatted, so patterns write `{d}` and never `{d, date}` (whose built-in
  styles are Gregorian-only and limited to `short`/`full`). Plural, select, and selectordinal come
  from the package's CLDR 48.1 data; adding a language needs no plural code. The pattern cache is
  only ever fed resource text — a user-supplied pattern never reaches the formatter. The identity
  server keeps its private copy until it can reference the shared one; that consolidation is a
  follow-up, not part of this spec.
- **Language catalogue.** `Tellma.Core.Abstractions.Globalization.LanguageCatalogue`: a frozen
  list of `Language(Tag, EnglishName, NativeName, Symbol, Direction)` shipped in code and extended
  by platform PR. Initial entries: `en E`, `ar ع`, `am አ`, `fr F`, `es Es`, `pt P`, `de D`, `tr T`,
  `ur ا`, `hi हि`, `sw Sw`, `so So`, `ti ት`, `om O`, `zh 中`; the symbol is the short mark rendered in
  `Name (E)` / `Name (ع)`. A tenant's chosen languages must have distinct symbols (validated on
  settings save). A mono-lingual tenant renders labels without the symbol: `LabelFor("Name", 0)`
  returns `"Name"` when the tenant has one language and `"Name (E)"` otherwise.
- **Two fallback chains.** *Resources:* the UI culture's parent chain to the neutral English
  resource (the `ResourceManager` hub-and-spoke chain, `NeutralResourcesLanguage("en")` on every
  package); no tenant-primary hop. *Content:* the requested content-language index → the primary
  (`Name3 ?? Name`, `Name2 ?? Name`), applied by the client for display and by the server wherever
  it renders a name (exports, emails).
- **Missing strings** are a metered event (`tellma.localization.missing`, tags `resource.package`,
  `culture` — both closed sets) logged once per key per culture per process, never an exception.
  Coverage per package per language is a CI report, not a restriction: a compliance module that
  ships no Amharic simply falls back to English, and no language set is narrowed by referencing it.

**Confidence.** High.

### D14 — `Name2`/`Name3` gating in the Queryex schema

**Decision.** The Queryex schema adapter (data-access theme) builds up to **three schema variants**
per model, keyed by the tenant's content-language count (1, 2, 3), lazily and once per process.
The variant omits every multilingual property with index 2 when `SecondaryLanguage` is null and
index 3 when `TernaryLanguage` is null; the multilingual convention (`X`, `X2`, `X3` on an entity
that opts into the multilingual capability) is the data-access theme's, and this theme supplies the
predicate (`TenantSettings.ContentLanguageCount`). Selection is per request from the settings
snapshot, so a settings change that adds a language switches variants on the next request with no
cache to flush: the engine's caches are keyed by schema identity, the three variants coexist, and
nothing is ever mutated. Physical columns are never gated, so a query compiled a moment before the
change still executes.

**Rationale.** Three instances cost three engine caches at most; keying anything on per-tenant
identity would fragment the engine's template cache by tenant count for no benefit.

**Confidence.** High.

### D15 — User settings cache

**Decision.** `core.UserSettings` stays key-value (`Id`, `UserId`, `Key`, `Value`), keys declared as
`UserSettingDefinition<T>` with the same grammar and registry as tenant definitions; the platform
declares `ui.culture` (`string?`), `ui.calendar` (`string?`), `ui.pinnedScreens` (`string[]`). The
`user_settings` cache kind holds `UserSettings` (`FrozenDictionary<string, JsonElement>` +
`Get<T>`) per `(tenant, user)` validated by `UserVersions.UserSettingsVersion`; the client DTO is
the dictionary plus the wire tag. Key-value rather than one JSON column because two tabs editing
different preferences must not conflict; the load is one statement by `UserId` and rides the
connect batch when cold (D4). Admin-managed pinned screens are a later feature, not a table split
now.

**Confidence.** Medium (the users theme owns the table).

### D16 — Logs, and the tenant-id guard

Structured events (Serilog, level in parentheses): `VersionTagRowMissing(key)` (Warning, self-healed),
`CacheableEntityOverflow(entity, rows, maxRows)` (Warning, once per process per type),
`StaleVersionTagRetry(key)` (Information), `SettingsEntryUndeclared(key)` (Warning),
`MissingResourceString(package, key, culture)` (Warning, once), `TenantIdMismatch(routed, found)`
(Critical — the request fails closed and an alert fires). Tenant id appears in the log scope, never
on a metric.

### D17 — Testing

`test/core/Tellma.Core.Tests` (unit, every PR): single-flight (N concurrent misses → 1 loader,
faulted loader retried), tag comparison and per-kind policy, `SizeLimit`/eviction accounting,
negotiation precedence and extension stripping (`ar-SA-u-ca-islamic-umalqura` → `ar-SA` + calendar
from header only), both fallback chains, `LabelFor`, ICU rendering with calendar-aware date
arguments under `gc`/`uq`/`et`, catalogue symbol distinctness, definition-key grammar, the
pre-serialized client DTO. `test/core/Tellma.Core.Tests` LocalDB tier (`Category=Integration`,
every PR, on the data-access fixture): the change-tracking write audit (D3) over every fixture save;
tag-then-rows load ordering under a concurrent bump; `ServerRoundtrips` from
`SqlConnection.RetrieveStatistics()` asserting the ledger of D4 (1 warm read, 2 warm save, 2 cold
tenant read); the tenant-id guard; migrator seeding of `core.VersionTags`. `Live=true`: none.

---

## 3. Contracts

Namespaces are final; XML docs are abbreviated to summaries. Code is normative for shape, not
formatting.

### 3.1 Version tags — `Tellma.Core.Abstractions.Versioning`

```csharp
namespace Tellma.Core.Abstractions.Versioning;

/// <summary>The reserved tenant-level version-tag keys and the grammar of registered ones.</summary>
public static class VersionTagKeys
{
    /// <summary>Bumped by any write to <c>core.Settings</c> or <c>core.SettingsEntries</c>.</summary>
    public const string Settings = "settings";

    /// <summary>Bumped by any write to <c>core.Roles</c> or <c>core.Permissions</c>.</summary>
    public const string Permissions = "permissions";

    /// <summary>Bumped whenever any <c>entity:*</c> key is bumped; the client's single lookup-cache tag.</summary>
    public const string Entities = "entities";

    /// <summary>The prefix of a cacheable entity type's key: <c>entity:Country</c>.</summary>
    public const string EntityPrefix = "entity:";

    /// <summary>Composes the key of a cacheable entity type.</summary>
    public static string ForEntity(string entityName);

    /// <summary>The inclusive upper bound on a key's length (the column width).</summary>
    public const int MaxKeyLength = 100;
}

/// <summary>The user-level tags read at connect for the signed-in user.</summary>
/// <param name="UserSettingsVersion">Bumped by writes to that user's <c>core.UserSettings</c> rows.</param>
/// <param name="PermissionsVersion">Bumped by writes to that user's <c>core.RoleMemberships</c> rows.</param>
public readonly record struct UserVersionTags(Guid UserSettingsVersion, Guid PermissionsVersion);

/// <summary>
///     The tags observed by the current unit of work: populated by the executor from the first
///     round trip's tag result sets (or by a job runner from a dedicated read) and consulted by
///     every versioned cache before it hands out an entry. Request- or job-scoped.
/// </summary>
public interface IVersionTagSnapshot
{
    /// <summary>True once a round trip has populated the snapshot.</summary>
    bool IsPopulated { get; }

    /// <summary>Every tenant-level tag by key, as last read.</summary>
    IReadOnlyDictionary<string, Guid> Tenant { get; }

    /// <summary>The signed-in user's tags; null for anonymous or system-scoped work.</summary>
    UserVersionTags? User { get; }

    /// <summary>The tenant tag for a key, or null when the row does not exist yet (kind is then uncacheable).</summary>
    Guid? TryGet(string key);

    /// <summary>Renders a tag for the wire: build identity plus the value, per the wire-tag format.</summary>
    string ToWireTag(string key);
}

/// <summary>How a table write maps to a tag bump; registered at composition, read by the executor.</summary>
public abstract record VersionTagRule
{
    /// <summary>The written table, schema-qualified and bracket-quoted, e.g. <c>[core].[Countries]</c>.</summary>
    public required string Table { get; init; }

    /// <summary>A write to <see cref="Table" /> bumps this tenant-level key.</summary>
    public sealed record Tenant(string Key) : VersionTagRule;

    /// <summary>
    ///     A write to <see cref="Table" /> bumps <paramref name="Column" /> of
    ///     <c>core.UserVersions</c> for every distinct value of <paramref name="UserIdColumn" />
    ///     among the written rows.
    /// </summary>
    public sealed record User(string UserIdColumn, UserVersionColumn Column) : VersionTagRule;
}

/// <summary>The two user-level tag columns.</summary>
public enum UserVersionColumn { UserSettingsVersion, PermissionsVersion }

/// <summary>The composition-time registry of tag keys and rules; validated at the startup gate.</summary>
public interface IVersionTagRegistry
{
    /// <summary>Every registered tenant-level key (reserved ones included), for seeding and self-healing.</summary>
    IReadOnlyCollection<string> Keys { get; }

    /// <summary>Resolves the tag effects of writing the given tables.</summary>
    /// <returns>The distinct tenant keys to bump and the user-scoped rules that fired.</returns>
    VersionTagEffects Resolve(IReadOnlyCollection<string> writtenTables);
}

/// <summary>What the executor appends before COMMIT.</summary>
public sealed record VersionTagEffects(
    IReadOnlyCollection<string> TenantKeys,
    IReadOnlyCollection<VersionTagRule.User> UserRules);

/// <summary>Thrown by the executor when a fresh-tag guard fails inside a persist batch.</summary>
public sealed class StaleVersionTagException(string key) : Exception
{
    /// <summary>The key whose value moved since the pipeline read it.</summary>
    public string Key { get; } = key;
}
```

### 3.2 Caching — `Tellma.Core.Abstractions.Caching`

```csharp
namespace Tellma.Core.Abstractions.Caching;

/// <summary>Declares an entity type cacheable: whole list, tag-validated, shared by every user.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class CacheableAttribute : Attribute
{
    /// <summary>The row cap; a longer list is served but not cached. Default 500, maximum 5 000.</summary>
    public int MaxRows { get; init; } = 500;
}

/// <summary>An immutable cached list of one entity type, with a key index.</summary>
public sealed class CachedEntitySet<TEntity> where TEntity : class
{
    /// <summary>The tag the set was loaded under.</summary>
    public Guid Version { get; }

    /// <summary>When the set was loaded (the bounded-staleness backstop reads it).</summary>
    public DateTimeOffset LoadedAt { get; }

    /// <summary>Every row, in key order, inactive rows included.</summary>
    public ImmutableArray<TEntity> Items { get; }

    /// <summary>Rows by key.</summary>
    public FrozenDictionary<int, TEntity> ById { get; }
}

/// <summary>Reads cacheable entity lists for the ambient tenant.</summary>
public interface IEntityCache
{
    /// <summary>The list, from cache when its tag matches the snapshot, otherwise loaded in one round trip.</summary>
    ValueTask<CachedEntitySet<TEntity>> GetAsync<TEntity>(CancellationToken cancellationToken)
        where TEntity : class;

    /// <summary>The cached list without loading; null when absent or stale.</summary>
    CachedEntitySet<TEntity>? Peek<TEntity>() where TEntity : class;
}

/// <summary>Meter, instruments, and tags of the platform cache and version-tag telemetry.</summary>
public static class CacheTelemetryNames
{
    public const string MeterName = "Tellma.Core";
    public const string RequestsInstrument = "tellma.cache.requests";
    public const string LoadDurationInstrument = "tellma.cache.load.duration";
    public const string EntriesInstrument = "tellma.cache.entries";
    public const string SizeInstrument = "tellma.cache.size";
    public const string EvictionsInstrument = "tellma.cache.evictions";
    public const string EntityRowsInstrument = "tellma.cache.entity.rows";
    public const string BumpsInstrument = "tellma.versions.bumps";
    public const string RerunsInstrument = "tellma.versions.reruns";
    public const string MissingStringsInstrument = "tellma.localization.missing";
    public const string KindTag = "cache.kind";
    public const string OutcomeTag = "cache.outcome";
    public const string EvictionReasonTag = "cache.eviction.reason";
    public const string EntityTag = "cache.entity";
    public const string KeyKindTag = "version.key.kind";
    public const string ResourcePackageTag = "resource.package";
    public const string CultureTag = "culture";
    public const string HitOutcome = "hit";
    public const string MissOutcome = "miss";
    public const string StaleOutcome = "stale";
    public const string RejectedOutcome = "rejected";
    // cache.kind values
    public const string SettingsKind = "settings";
    public const string UserSettingsKind = "user_settings";
    public const string PermissionsKind = "permissions";
    public const string EntitiesKind = "entities";
}
```

### 3.3 Settings — `Tellma.Core.Abstractions.Settings`

```csharp
namespace Tellma.Core.Abstractions.Settings;

/// <summary>One tenant setting stored as a key-value entry, declared once by its owning package.</summary>
/// <typeparam name="T">A supported scalar, <c>string[]</c>, or any type with a supplied JsonTypeInfo.</typeparam>
public sealed record TenantSettingDefinition<T> : SettingDefinition
{
    public TenantSettingDefinition(string key, T @default, bool isClientVisible = false,
        Func<T, string?>? validate = null, JsonTypeInfo<T>? typeInfo = null);

    /// <summary>The value when no row exists.</summary>
    public T Default { get; }

    /// <summary>Returns a localizable error code for an invalid value, or null.</summary>
    public Func<T, string?>? Validate { get; }

    /// <summary>Source-generated metadata for a non-scalar T.</summary>
    public JsonTypeInfo<T>? TypeInfo { get; }
}

/// <summary>A user setting, same grammar and registry; the users theme's <c>core.UserSettings</c> stores it.</summary>
public sealed record UserSettingDefinition<T> : SettingDefinition { /* same members */ }

/// <summary>The non-generic base every definition shares; the registry keys on <see cref="Key" />.</summary>
public abstract record SettingDefinition
{
    /// <summary>Dotted lower-camel key; the first segment is the category and the permission resource suffix.</summary>
    public string Key { get; }

    /// <summary>The category (first key segment).</summary>
    public string Category { get; }

    /// <summary>Whether the value is projected into the client DTO.</summary>
    public bool IsClientVisible { get; }

    /// <summary>The key grammar every definition must satisfy.</summary>
    public const string KeyPattern = @"^[a-z][A-Za-z0-9]*(\.[a-z][A-Za-z0-9]*)+$";
    public const int MaxKeyLength = 200;
}

/// <summary>A content language of the tenant, in the position it occupies (0 = primary).</summary>
public sealed record TenantLanguage(int Index, Language Language)
{
    /// <summary>The label for a multilingual column in this position: "Name (ع)" or "Name" when mono-lingual.</summary>
    public string LabelFor(string baseLabel, bool monoLingual);
}

/// <summary>The tenant's settings as the server reads them; immutable; one instance per tag value per instance.</summary>
public sealed record TenantSettings
{
    public required int TenantId { get; init; }
    public required Guid Version { get; init; }
    public required string Name { get; init; }
    public string? Name2 { get; init; }
    public string? Name3 { get; init; }
    public required ImmutableArray<TenantLanguage> Languages { get; init; }   // 1..3
    public int ContentLanguageCount => Languages.Length;
    public required ImmutableArray<ICalendarSystem> Calendars { get; init; } // 1..2
    public required TimeZoneInfo TimeZone { get; init; }
    /// <summary>The backend zone name bound to spec 0008's <c>TimeZone</c> slot.</summary>
    public required string SqlTimeZoneName { get; init; }
    public required CultureInfo DefaultCulture { get; init; }
    public required DateTimeOffset ModifiedAt { get; init; }
    public required int ModifiedById { get; init; }
    /// <summary>Raw entries by key; undeclared keys are dropped at load.</summary>
    public required FrozenDictionary<string, JsonElement> Entries { get; init; }

    /// <summary>The typed value of a declared setting, or its default; parsed once and memoized.</summary>
    public T Get<T>(TenantSettingDefinition<T> definition);

    /// <summary>Label helper honouring the mono-lingual rule.</summary>
    public string LabelFor(string baseLabel, int languageIndex);

    /// <summary>The current date in the tenant zone.</summary>
    public DateOnly Today(TimeProvider clock);
}

/// <summary>Tag-validated access to the ambient tenant's settings.</summary>
public interface ITenantSettingsProvider
{
    /// <summary>The settings whose tag matches the snapshot; loads in one round trip on a miss.</summary>
    ValueTask<TenantSettings> GetAsync(CancellationToken cancellationToken);

    /// <summary>The pre-serialized client projection (UTF-8 JSON) for the same tag.</summary>
    ValueTask<ReadOnlyMemory<byte>> GetForClientAsync(CancellationToken cancellationToken);

    /// <summary>The cached instance without loading; null when absent or stale.</summary>
    TenantSettings? Peek();
}

/// <summary>The wire projection; shape is the web theme's, members are this theme's.</summary>
public sealed record TenantSettingsForClient(
    string Version,                                   // wire tag "{build}.{guid}"
    string Name, string? Name2, string? Name3,
    ImmutableArray<ClientLanguage> Languages,         // (Tag, Symbol, NativeName, Direction)
    ImmutableArray<string> Calendars,                 // codes
    string TimeZone, string DefaultCulture,
    IReadOnlyDictionary<string, JsonElement> Entries); // client-visible entries only

/// <summary>The partial edit shape (field-mask semantics) the settings edit endpoint accepts.</summary>
public sealed record TenantSettingsPatch(
    IReadOnlyList<string> Fields,                     // typed member names and entry keys being set
    string? Name, string? Name2, string? Name3,
    string? PrimaryLanguage, string? SecondaryLanguage, string? TernaryLanguage,
    string? PrimaryCalendar, string? SecondaryCalendar,
    string? TimeZone, string? DefaultCulture,
    IReadOnlyDictionary<string, JsonElement?>? Entries,
    DateTimeOffset ExpectedModifiedAt);
```

### 3.4 Globalization — `Tellma.Core.Abstractions.Globalization`

```csharp
namespace Tellma.Core.Abstractions.Globalization;

public enum TextDirection { LeftToRight, RightToLeft }

/// <summary>One language of the Core catalogue.</summary>
/// <param name="Tag">BCP-47 language subtag(s), validated as a predefined culture at startup.</param>
/// <param name="Symbol">The short mark rendered in multilingual labels, e.g. "ع".</param>
public sealed record Language(string Tag, string EnglishName, string NativeName, string Symbol, TextDirection Direction);

/// <summary>The languages a tenant may choose as content languages; extended by platform PR.</summary>
public static class LanguageCatalogue
{
    public static ImmutableArray<Language> All { get; }
    public static Language? Find(string tag);
}

/// <summary>The calendar codes shared with the query language.</summary>
public static class CalendarCodes
{
    public const string Gregorian = "gc";
    public const string UmAlQura = "uq";
    public const string Ethiopian = "et";
}

public enum DateStyle { Short, Medium, Long, Full }

/// <summary>A calendar system: arithmetic, names, and the platform's own rendering.</summary>
public interface ICalendarSystem
{
    string Code { get; }
    /// <summary>The BCL calendar for arithmetic (a custom subclass for Ethiopian).</summary>
    Calendar Calendar { get; }
    DateOnly MinSupported { get; }
    DateOnly MaxSupported { get; }
    /// <summary>Renders a date in this calendar for the given culture and style; never via DateTimeFormatInfo.</summary>
    string Format(DateOnly date, CultureInfo culture, DateStyle style);
    string Format(DateTimeOffset instant, TimeZoneInfo zone, CultureInfo culture, DateStyle style);
    IReadOnlyList<string> MonthNames(CultureInfo culture);
}

/// <summary>The calendar systems composed into this distribution, by code.</summary>
public interface ICalendarRegistry
{
    ICalendarSystem this[string code] { get; }
    bool TryGet(string code, [NotNullWhen(true)] out ICalendarSystem? calendar);
    IReadOnlyCollection<string> Codes { get; }
}

/// <summary>The localization values of the ambient unit of work; scoped; populated once per request or job.</summary>
public interface ILocalizationContext
{
    /// <summary>The formatting culture (numbers, separators).</summary>
    CultureInfo Culture { get; }
    /// <summary>The resource culture; always one of the distribution's shipped UI languages or its parent chain.</summary>
    CultureInfo UiCulture { get; }
    ICalendarSystem Calendar { get; }
    TimeZoneInfo TimeZone { get; }
    /// <summary>The index of the content language the caller prefers (0 = primary), for name fallback.</summary>
    int ContentLanguageIndex { get; }
    DateOnly Today { get; }
    DateTimeOffset Now { get; }
}

/// <summary>A date argument to a localized message, rendered through the request calendar before formatting.</summary>
public readonly record struct LocalizedDate(DateOnly Value, DateStyle Style = DateStyle.Short);

/// <summary>Header and key names shared with the web surface.</summary>
public static class LocalizationHeaders
{
    /// <summary>Request: the calendar code the caller wants; response: the effective one.</summary>
    public const string Calendar = "Tellma-Calendar";
    /// <summary>Response: every tag the client caches on, as <c>key=wiretag</c> pairs.</summary>
    public const string Versions = "Tellma-Versions";
    /// <summary>The user-setting keys the platform declares.</summary>
    public const string UiCultureKey = "ui.culture";
    public const string UiCalendarKey = "ui.calendar";
}
```

### 3.5 Shapes needed from other seams

From the **batch abstraction** (data-access theme):

```csharp
/// <summary>Members the version cache needs on the batch builder.</summary>
public interface IBatchBuilder
{
    /// <summary>Raw SQL must declare the tables it writes; the executor derives tag bumps from them.</summary>
    IBatchBuilder Raw(string sql, IReadOnlyList<SqlParameter> parameters, bool mayRetry,
        IReadOnlyCollection<string> writes);

    /// <summary>Appends the tenant tag read; the executor populates <see cref="IVersionTagSnapshot" /> from it.</summary>
    IBatchBuilder ReadVersionTags();

    /// <summary>Inside a transactional batch: THROW 50012 if a key's value differs from the expected one.</summary>
    IBatchBuilder RequireFreshTags(IReadOnlyDictionary<string, Guid> expected);

    /// <summary>A cacheable list: no statement when the cache is warm, one query and a cache fill when not.</summary>
    IBatchReader<CachedEntitySet<TEntity>> FromCache<TEntity>() where TEntity : class;

    /// <summary>The settings load (three statements, tag first), when the settings cache is cold.</summary>
    IBatchReader<TenantSettings> LoadTenantSettings();
}
```

The executor contract: it appends the D3 bump statements and the guard before `COMMIT`, in that
order, and the tag read last; it runs a tag comparison after every batch and raises
`IVersionTagSnapshot` updates and the per-kind policy of D4.

From the **request context** (host theme): a scoped holder exposing `TenantId`, `UserId?`,
`IsSandbox`, and the `ILocalizationContext` above; the job runner populates the same holder from a
copied `RequestContextSnapshot` (tenant, user, culture tag, calendar code) or from tenant defaults.

From **feature composition** (host theme): `feature.Entity<T>().Cacheable(maxRows)`,
`feature.AddCalendar<TCalendarSystem>()`, `feature.AddSettings(typeof(GlSettings))` (reflects the
static definitions), `feature.AddVersionTag(key, rules)`, and `localization.UiLanguages(...)`, all
in the *Contribute* phase; the startup gate runs the validations named in D6, D8, D10, D12.

From the **users theme**: `core.UserVersions` with the two columns; the connect statement selecting
them; `UserSettings` as key-value with the platform keys; the securables registry's
`SupportsFilter` per resource.

From the **web theme**: the `Tellma-Versions` response header (`settings=…; permissions=…;
user_settings=…; entities=…`), `Content-Language` and `Tellma-Calendar` response headers, the
settings endpoints (`settings/get` accepting the caller's tag, `settings/save` taking
`TenantSettingsPatch`), and the `DeploymentIdentity.Build` value from the composition root.

---

## 4. Schema

All tables in schema `core`; names plural except the single-row `Settings`; constraint names
explicit; period columns are EF shadow properties; no IDENTITY anywhere.

```sql
CREATE TABLE [core].[Settings] (
    [TenantId]          int              NOT NULL,
    [Name]              nvarchar(255)    NOT NULL,
    [Name2]             nvarchar(255)    NULL,
    [Name3]             nvarchar(255)    NULL,
    [PrimaryLanguage]   varchar(35)      NOT NULL,
    [SecondaryLanguage] varchar(35)      NULL,
    [TernaryLanguage]   varchar(35)      NULL,
    [PrimaryCalendar]   char(2)          NOT NULL CONSTRAINT [DF_Settings_PrimaryCalendar] DEFAULT ('gc'),
    [SecondaryCalendar] char(2)          NULL,
    [TimeZone]          varchar(64)      NOT NULL,                  -- IANA id, e.g. 'Asia/Riyadh'
    [DefaultCulture]    varchar(35)      NOT NULL,                  -- specific culture, e.g. 'ar-SA'
    [CreatedAt]         datetime2(7)     NOT NULL,
    [CreatedById]       int              NOT NULL,
    [ModifiedAt]        datetime2(7)     NOT NULL,                  -- concurrency stamp
    [ModifiedById]      int              NOT NULL,
    [ValidFrom]         datetime2(7)     GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo]           datetime2(7)     GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]),
    CONSTRAINT [PK_Settings] PRIMARY KEY CLUSTERED ([TenantId]),
    CONSTRAINT [FK_Settings_CreatedById]  FOREIGN KEY ([CreatedById])  REFERENCES [core].[Users] ([Id]),
    CONSTRAINT [FK_Settings_ModifiedById] FOREIGN KEY ([ModifiedById]) REFERENCES [core].[Users] ([Id]),
    CONSTRAINT [CK_Settings_Languages] CHECK (
        ([SecondaryLanguage] IS NULL OR [SecondaryLanguage] <> [PrimaryLanguage]) AND
        ([TernaryLanguage] IS NULL OR ([SecondaryLanguage] IS NOT NULL
            AND [TernaryLanguage] <> [PrimaryLanguage] AND [TernaryLanguage] <> [SecondaryLanguage]))),
    CONSTRAINT [CK_Settings_Calendars] CHECK ([SecondaryCalendar] IS NULL OR [SecondaryCalendar] <> [PrimaryCalendar])
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [core].[SettingsHistory]));
-- One row per database; inserted by tenant provisioning with the reserved system user as creator.
-- [TableType] opt-in: [core].[SettingsList] (period columns excluded automatically).
```

```sql
CREATE TABLE [core].[SettingsEntries] (
    [Id]            int              NOT NULL,                      -- sq_SettingsEntries
    [Key]           varchar(200)     NOT NULL,                      -- 'gl.fiscalYearStartMonth'
    [Value]         nvarchar(max)    NOT NULL,                      -- JSON text of the declared T
    [CreatedAt]     datetime2(7)     NOT NULL,
    [CreatedById]   int              NOT NULL,
    [ModifiedAt]    datetime2(7)     NOT NULL,
    [ModifiedById]  int              NOT NULL,
    [ValidFrom]     datetime2(7)     GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo]       datetime2(7)     GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]),
    CONSTRAINT [PK_SettingsEntries] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_SettingsEntries_Key] UNIQUE NONCLUSTERED ([Key]),
    CONSTRAINT [FK_SettingsEntries_CreatedById]  FOREIGN KEY ([CreatedById])  REFERENCES [core].[Users] ([Id]),
    CONSTRAINT [FK_SettingsEntries_ModifiedById] FOREIGN KEY ([ModifiedById]) REFERENCES [core].[Users] ([Id])
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [core].[SettingsEntriesHistory]));
CREATE SEQUENCE [core].[sq_SettingsEntries] AS int START WITH 1000 INCREMENT BY 1;
-- Absent row = default. [TableType] opt-in: [core].[SettingsEntriesList].
```

```sql
CREATE TABLE [core].[VersionTags] (
    [Key]     varchar(100)     NOT NULL,                            -- 'settings', 'permissions', 'entities', 'entity:Country', ...
    [Version] uniqueidentifier NOT NULL CONSTRAINT [DF_VersionTags_Version] DEFAULT (NEWID()),
    CONSTRAINT [PK_VersionTags] PRIMARY KEY CLUSTERED ([Key])
);
-- Non-temporal platform bookkeeping; not an entity (no Id, no sequence, not in the Queryex schema).
-- Rows seeded by the migrator from IVersionTagRegistry.Keys; self-healed at connect.
-- Read: SELECT [Key], [Version] FROM [core].[VersionTags];                      (every first round trip)
-- Bump: UPDATE t SET [Version] = NEWID() FROM [core].[VersionTags] t JOIN @tm_tags k ON k.Id = t.[Key];
```

```sql
CREATE TABLE [core].[UserVersions] (
    [UserId]              int              NOT NULL,
    [UserSettingsVersion] uniqueidentifier NOT NULL CONSTRAINT [DF_UserVersions_UserSettingsVersion] DEFAULT (NEWID()),
    [PermissionsVersion]  uniqueidentifier NOT NULL CONSTRAINT [DF_UserVersions_PermissionsVersion]  DEFAULT (NEWID()),
    -- the users theme adds LastActiveAt datetime2(0) NULL and inbox watermarks here
    CONSTRAINT [PK_UserVersions] PRIMARY KEY CLUSTERED ([UserId]),
    CONSTRAINT [FK_UserVersions_UserId] FOREIGN KEY ([UserId]) REFERENCES [core].[Users] ([Id])
);
-- Non-temporal sibling of core.Users; one row inserted with every user row.
```

Indexes beyond the primary keys: none. Every read is by primary key or a full scan of a one-page
table; tags are never indexed or range-compared.

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| Caching: "How do we guarantee the cache version is invalidated when a cacheable entity is updated?" | D3: the batch executor bumps from declared write sets via `IVersionTagRegistry`; raw SQL must declare writes; the LocalDB tier audits every batch with change tracking; a `MaxAge` backstop bounds any bypass. |
| Caching: "Is 'version' the accurate technical name? etag? fingerprint?" | D1: *version tag*; `Version` columns; ETag stays with HTTP representations (blobs). |
| Caching: "Is 'metaversion' the accurate technical name?" | D1: the concept is dropped; the deployment build identity is folded into the wire tag. |
| Settings: "Is calling the table Settings correct?" | D7: `core.Settings` (single typed row, `TenantId` PK); the key-value table is `core.SettingsEntries`. |
| Settings: "What do we call the KV table?" | D7: `core.SettingsEntries`; no `Category` column, category = first key segment. |
| Settings: "Language axis vs culture axis — PrimaryLanguage or PrimaryCulture?" | D10: content languages are language tags (`ar`); formatting culture is per user with a tenant `DefaultCulture`; UI language per user within the distro's shipped set. |
| Settings: "Criteria for top-level table vs KV? Everything in KV?" | D7: typed when read on the request path or required without a default; entries otherwise; never everything in KV. |
| Settings: "Single edit method vs per category; permission resource = category?" | D9: one `settings/save` with a field-masked partial DTO; resource `settings.<category>`; a request touching two categories needs both. |
| Settings: "Patch vs load-and-save?" | D9: partial DTO with an explicit `Fields` list (field-mask semantics), not JSON Patch, not full-row save. |
| Localization 1: "Should a compliance module that lacks a language restrict the distro's languages?" | D13: no restriction; fallback to English per string; CI reports coverage per package per language. |
| Localization 2: "How do we organize the resource files in the backend?" | D13: `Resources/Strings.resx` + per-culture satellites per package, `IStringLocalizer<Strings>` marker, `SatelliteResourceLanguages` trimming at the distro. |
| Localization 3: "How do we support ICU message format?" | D13: `IcuStringLocalizer<>` registered as the open-generic `IStringLocalizer<>`, singleton `MessageFormatter`, calendar-aware date arguments pre-formatted. |
| Localization prose: "culture headers ignored if not one of the tenant's languages" | D10/D11: the UI language is independent of content languages; only the content-language *index* is tenant-bound. |
| Localization prose: "custom calendar header (does a standard one exist?)" | D11/D12: none exists; `Tellma-Calendar` request/response header carrying `gc`/`uq`/`et`. |
| Localization prose: "fallback: request culture → tenant primary → English" | D13: resources fall back through the culture's parent chain to English only; content falls back to the primary column. |
| User entity: "Separate SettingsVersion and PermissionsVersion, or a single field?" | D2: separate (`UserVersions.UserSettingsVersion`, `PermissionsVersion`) — one has security consequences, and a preference edit must not force a permission recompute. |
| User entity: "Version, ETag, or fingerprint?" | D1: version tag. |
| User entity: "Is JSON the right shape for user preferences? Pinned screens in a distinct table?" | D15: key-value rows with declared keys; pinned screens are a declared `string[]` key; a table split waits for the admin-managed feature. |
| Web layer: "Accept an X-Today header?" | D11: no; `today()` is the tenant zone's date per spec 0008; the tenant zone is a setting. |
| Caching prose: "versions read before executing any API call" | D4: read in the first business round trip and compared afterwards, with a per-kind policy. |
| Caching prose: "LRU, thread safe, stampede safe, observability" | D5: private `MemoryCache` per kind with `SizeLimit`, single-flight loads, eight instruments. |
| Caching prose: "determine if a cached entity was right to cache" | D5/D6: `cache.outcome = rejected`, `tellma.cache.entity.rows`, `CacheableEntityOverflow`. |

---

## 6. Seams

**1. Batch abstraction (data-access theme owns).** Needed: `Raw(..., writes)`, `ReadVersionTags()`,
`RequireFreshTags(expected)`, `FromCache<T>()`, `LoadTenantSettings()` on the builder (§3.5); the
executor appends bump statements then the guard before `COMMIT`, the tag read last; every reader
of the tag result set populates the scoped `IVersionTagSnapshot`; the executor's own parameter
names live under `@tm_` and never `@qx`. The executor is the sole bumper.

**3. One capability, declared once (pipeline theme owns).** `Cacheable(maxRows)` is a capability
whose projections are: a tag key and write rule, a cache-kind entry, a startup validation
(top-level, no children, read securable without filter support), and nothing on the web surface.

**4. Queryex schema per tenant configuration (data-access theme owns).** Three variants keyed by
content-language count, process-lifetime, selected per request from `TenantSettings`; RLS and
weak-entity rewriting never touch the schema; `CalendarCodes` is the shared source for spec 0008's
reserved codes and the `uq` month-map table is contributed by `Tellma.Locale.Sa`.

**5. Version tags (this theme owns).** Keys, tables, bump and read statements, wire format,
per-kind policy, and the two-level permission validation are in D1–D4; consumers: the data-access
theme (emitter/executor), the users theme (`UserVersions`, permissions cache validated against the
pair), the web theme (`Tellma-Versions` header).

**6. Feature composition (host theme owns).** Contribute-phase calls: `AddCalendar<T>()`,
`AddSettings(typeof(...))`, `AddVersionTag(key, rules)`, `Entity<T>().Cacheable(...)`,
`Localization.UiLanguages(...)`; the aggregated startup validation includes D6/D8/D10/D12/D13
checks and the ICU-mode probe.

**9. Request context (host theme owns).** Needed: a scoped holder carrying tenant, user, sandbox
flag, and the `ILocalizationContext` of §3.4, populated by the tenant filter for requests and by
the job runner for background scopes from a copied snapshot; `CultureInfo.CurrentCulture` and
`CurrentUICulture` set from it, never the other way round.

**13. Wire shapes (web theme owns).** `TenantSettingsForClient` as pre-serialized bytes; the
`Tellma-Versions`, `Content-Language`, `Tellma-Calendar` response headers; `Accept-Language` and
`Tellma-Calendar` request headers; `settings/get` honouring a supplied tag with an "unchanged"
result.

**14. Telemetry names.** Meter `Tellma.Core`; the instruments of D5 as constants in
`CacheTelemetryNames`; no tenant tag; `cache.entity` and `resource.package` are closed per
deployment.

**16. Connect-call collapse (users/pipeline themes).** The tag read rides the connect batch; the
cold-cache loads ride it too (keyed off the subject in SQL); per-kind policy and failure modes in
D4; the ledger is one round trip for a warm read, two for a warm save, at most one extra for cold
caches.

**17. Vocabulary.** *Version tag*; `Version` column suffix; four audit columns everywhere with
system versioning additive; plural table names (`core.Users`, `core.SettingsEntries`) with the
single-row `core.Settings`; `core.SettingsEntries` replaces `TenantConfigurationKV`; `UserSettings`
(not `UserPreferences`) for the table, "preferences" only in UI copy; `ModifiedAt` as the
concurrency stamp on the settings rows.

---

## 7. Departures

- **Table naming.** The brain dump's singular `core.User`/`core.Settings` sketch is replaced by the
  architecture document's plural convention; `core.Settings` stays because it is the plural form.
  Not a departure from the architecture, recorded because the brain dump differs.
- **Platform bookkeeping tables without surrogate keys or sequences.** `core.VersionTags` (natural
  `Key`) and `core.UserVersions` (PK = FK) are not entities: no `Id`, no `sq_` sequence, absent from
  the Queryex schema and the CRUD stack. The architecture's "every table draws its surrogate keys
  from a per-table sequence" is read as applying to entity tables; the exception is named so the
  seed-band test and the sequence convention exclude these two tables explicitly.
- **Server-side translations ship with the package that owns the strings**, not with a Locale pack:
  `Tellma.Core`'s Arabic satellite is `ar/Tellma.Core.resources.dll` inside the `Tellma.Core`
  package, trimmed per distribution by `SatelliteResourceLanguages`. The client keeps the
  architecture's Locale-pack model for strings and fonts; on the server, satellite assemblies must
  carry the main assembly's name and sit beside it, so the same package is the natural carrier.
  Locale packs still own calendars, number-to-words, and formatting primitives.
- **`ICalendarSystem` rather than `ICalendar`** for the Locale-dimension contract, to avoid
  confusion with `System.Globalization.Calendar`, which every implementation also exposes.
- **No `HybridCache`.** The architecture names "in-memory caching with proper invalidation" and does
  not mandate a library; this is a choice, not a departure, but it is the one most likely to be
  questioned and is flagged in D5.
- **`today()` is tenant-zone only** and no `X-Today`/time-zone request header exists in the first
  release; the brain dump floated both. Spec 0008 already fixes the semantics.
- **The identity server's private `IcuStringLocalizer` and `LanguageCatalog` remain** for now; the
  platform ships its own in `Tellma.Core` (a hard dependency of the CRUD stack, like Queryex) rather
  than as a new optional Core-layer package. Consolidation is a follow-up.

---

## 8. Verification

Facts relied on from the research file (all verified 2026-09-01 there):

- `HybridCache` 10.9.0 serializes on every write even L1-only, uses the DI `IMemoryCache` as L1,
  keeps tag invalidation stamps process-local, and offers stampede protection per instance;
  `IMemoryCache.GetOrCreate` has none; `SizeLimit` on the shared DI cache breaks consumers that do
  not set `Size`. (§1.2–1.5, 1.8)
- `MessageFormat` 8.0.0: plural/select/selectordinal/nesting; `date` styles limited to
  `short`/`full` and Gregorian through `DateTimeFormatInfo`; `CustomValueFormatter` exists; a shared
  cached `MessageFormatter` survived 16 × 50 000 concurrent formats; per-call culture override
  works. (§2.2–2.4)
- `-u-ca-` culture names create custom cultures whose calendar does not change, whose ICU patterns
  produce mixed output, and whose `Parent` skips the region; `DateTimeFormatInfo.Calendar` rejects
  custom calendars and calendars outside `OptionalCalendars`; no Ethiopic calendar exists in .NET;
  `UmAlQuraCalendar` ends 2077-11-16 (AH 1500). (§3.4, 4.1–4.4)
- IANA ids resolve on Windows and Linux with ICU; `TryConvertIanaIdToWindowsId` needs ICU; no
  standard header carries time zone or calendar; GitHub's `Time-Zone` header precedence. (§5.1–5.2)
- `SatelliteResourceLanguages` trims satellites from referenced packages too; `.resx` is the only
  framework-native resource format; `AcceptLanguageHeaderRequestCultureProvider` tries at most
  three values. (§3.1–3.3)
- Rails/Django/HybridCache precedent for stored-version validation and a global key version; SQL
  Server change tracking as an audit primitive with the restore hazard. (§6.1–6.3)
- `rowversion` bumps on every update; a monotonic `bigint` can be re-reached after a restore; Guid
  v7 is not sequential in SQL Server byte order; temporal `UPDATE` writes a history row even when
  nothing changed; RCSI is on by default only on Azure SQL. (users-roles research §4; data-access
  research §2.2, §8)
- `SqlConnection.RetrieveStatistics()["ServerRoundtrips"]` counts round trips for the test ledger.
  (data-access research §9)

Verified by this design directly on 2026-09-01:

- `MemoryCache.GetCurrentStatistics()` returns `MemoryCacheStatistics` and **returns null unless
  `MemoryCacheOptions.TrackStatistics` is true** — https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.memory.memorycache.getcurrentstatistics
- `Microsoft.ICU.ICU4C.Runtime`'s latest release is **72.1.0.3 (2023-10-13)** with only
  `linux-x64/arm64` and `win-x64/x86/arm64` runtime assets — stale CLDR, hence no app-local ICU —
  https://www.nuget.org/packages/Microsoft.ICU.ICU4C.Runtime
- `Accept-Language` is **not** a forbidden request header in the Fetch standard (the list has
  `Accept-Charset` and `Accept-Encoding`), so the SPA may set it per call —
  https://fetch.spec.whatwg.org/#forbidden-request-header
- The previous Tellma's `dbo.Settings` used `UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID()` for
  `SettingsVersion`, `DefinitionsVersion`, `SchedulesVersion`, stored `PrimaryLanguageId`/`Symbol`
  pairs and `NCHAR(2)` calendars with `DateFormat`/`TimeFormat`; its `dbo.Users` carried
  `PermissionsVersion`, `UserSettingsVersion`, `PreferredLanguage`, `PreferredCalendar`, and
  `LastAccess` on the audited (non-temporal) user row —
  https://raw.githubusercontent.com/tellma-ltd/tellma/master/Tellma.Database.Application/dbo/Tables/dbo.Settings.sql ,
  https://raw.githubusercontent.com/tellma-ltd/tellma/master/Tellma.Database.Application/dbo/Tables/dbo.Users.sql
- The repo's identity server already ships `IcuStringLocalizer<T>` (decorator over the framework
  localizer, `ThreadLocal<MessageFormatter>`), `LanguageCatalog` (shipped `en`/`ar`, offered subset
  from options), `UiLocalesRequestCultureProvider`, and `.resx` files (`SharedResources.resx`,
  `SharedResources.ar.resx`, `EmailTemplates*.resx`) — read from `src/apps/Tellma.Identity`.
- `Directory.Packages.props` pins `MessageFormat` 8.0.0, `Microsoft.Extensions.*` 10.0.11,
  `OpenTelemetry.*` 1.16.0, `Microsoft.Data.SqlClient` 6.1.1; no `Microsoft.Extensions.Caching.*`
  pin exists yet (`Microsoft.Extensions.Caching.Memory` arrives transitively with
  `Microsoft.EntityFrameworkCore`; a direct pin at 10.0.11 is one line).

Not verified, stated as assumptions:

- That `KEY` being a reserved T-SQL word causes no trouble beyond bracket-quoting (EF and the
  emitter always quote; the column is never referenced from Queryex text).
- That enabling change tracking on the LocalDB fixture database is cheap enough for the PR tier —
  it should be, but the data-access theme owns the fixture and must confirm.
- The exact byte cost of a `[StringList]` TVP parameter versus an `IN (@k0, @k1)` list for one to
  three keys; either is negligible, and the TVP was chosen for plan stability, not measured.
- Whether `Tellma.Core` taking `Microsoft.Extensions.Localization` (not just `.Abstractions`) is
  acceptable weight for every distribution; the alternative is registering the framework's
  `AddLocalization()` from the composition root and keeping Core on `.Abstractions` only.
